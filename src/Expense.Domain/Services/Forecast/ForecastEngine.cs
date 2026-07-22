using Expense.Domain.Data;
using Expense.Domain.Entities;
using Expense.Domain.Services.Budgets;
using Expense.Domain.Services.Ingestion.ManualCharges;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Forecast;

/// <summary>
/// Ties RecurrenceExpander + AmexCycleCalculator + the latest checking balance +
/// each debt account's configured payment into one dated ledger with a running
/// balance. Always starts from the latest real checking balance, never from
/// reconciling history.
/// </summary>
public class ForecastEngine(BudgetProrationService proration, RecurrenceExpander recurrenceExpander, AmexCycleCalculator amexCycleCalculator)
{
    // How long a manually confirmed/overridden occurrence stays visible in the ledger after
    // its effective date - long enough to catch a mistaken click, short enough that resolved
    // payments don't clutter a forward-looking forecast forever. The underlying
    // PaymentConfirmation row is never deleted by this - see the Confirmed Payments page for
    // the unbounded, filterable history.
    private const int ExcludedPaymentVisibilityDays = 7;

    // How many days a partial payment's recorded paid-date may differ from a real posted
    // transaction's date and still count as the same real-world payment - a manually-entered
    // date is only ever approximate, same reasoning as ManualChargeMatchingService.MatchWindowDays.
    private const int PartialPaymentMatchWindowDays = 5;

    public async Task<ForecastResult> GenerateAsync(
        ExpenseDbContext context, DateOnly asOfDate, DateOnly windowEnd, CancellationToken cancellationToken = default)
    {
        var startingBalance = await context.CheckingBalanceSnapshots
            .OrderByDescending(s => s.AsOfDate)
            .Select(s => s.Balance)
            .FirstOrDefaultAsync(cancellationToken);

        var directPeriods = await context.BudgetPeriods
            .Where(p => p.EffectiveThrough == null && p.Anchor != null && p.AccountId != null)
            .Join(context.FundingRules.Where(f => f.Strategy == FundingStrategies.Direct),
                p => p.CategoryId, f => f.CategoryId, (p, _) => p)
            .Include(p => p.Category)
            .Where(p => p.Category.IsActive)
            .ToListAsync(cancellationToken);

        var recurringRules = directPeriods.Select(p => new RecurringRule
        {
            Name = p.Category.Name,
            Direction = p.Direction,
            Amount = p.Amount,
            Frequency = p.Frequency,
            Anchor = p.Anchor!.Value,
            AccountId = p.AccountId!.Value,
            CategoryId = p.CategoryId,
            Active = true,
            StartDate = DateOnly.MinValue
        }).ToList();

        var debtAccounts = await context.Accounts
            .Where(a => a.Type == AccountType.Debt && a.IsActive && a.PaymentDueDay != null)
            .ToListAsync(cancellationToken);

        // Which category a debt account's real payment transactions get tagged with
        // (e.g. Discover -> "Discover Payment"), for the same actual-vs-scheduled
        // reconciliation Direct-funded categories get below.
        var accountPaymentCategoryIds = await context.FundingRules
            .Where(f => f.Strategy == FundingStrategies.AccountPayment && f.AccountId != null)
            .ToDictionaryAsync(f => f.AccountId!.Value, f => (int?)f.CategoryId, cancellationToken);

        foreach (var account in debtAccounts)
        {
            var amount = (account.MinPayment ?? 0m) + (account.ExtraPayment ?? 0m);
            if (amount == 0m) continue;

            recurringRules.Add(new RecurringRule
            {
                Name = $"{account.Name} Payment",
                Direction = Direction.Expense,
                Amount = amount,
                Frequency = Frequency.Monthly,
                Anchor = ClampedDate(asOfDate.Year, asOfDate.Month, account.PaymentDueDay!.Value),
                AccountId = account.Id,
                CategoryId = accountPaymentCategoryIds.GetValueOrDefault(account.Id),
                Active = true,
                StartDate = DateOnly.MinValue
            });
        }

        // Widened backward so a recently-due-but-unmatched occurrence isn't silently
        // dropped just because its scheduled date already passed - it stays projected
        // until a real matching transaction (checked below) excludes it.
        var lines = recurrenceExpander.Expand(
            recurringRules, [], asOfDate.AddDays(-RecurrenceExpander.MaxMatchWindowDays), windowEnd);

        var reconciliationTransactions = await context.BankTransactions
            .Where(t => t.CategoryId != null && t.PostedDate != null
                        && t.PostedDate >= asOfDate.AddDays(-RecurrenceExpander.MaxMatchWindowDays) && t.PostedDate <= asOfDate)
            .ToListAsync(cancellationToken);

        lines = lines.Where(l => !IsAlreadyReflectedInAnActualTransaction(l, reconciliationTransactions)).ToList();

        // Same backward-widening as above - a deferred/confirmed one-time event's original
        // date can end up in the past relative to asOfDate, and it must not vanish just
        // because of that.
        var oneTimeEvents = await context.OneTimeEvents.ToListAsync(cancellationToken);
        lines.AddRange(recurrenceExpander.Expand([], oneTimeEvents, asOfDate.AddDays(-RecurrenceExpander.MaxMatchWindowDays), windowEnd));

        var activeSpendingAccounts = await context.Accounts
            .Where(a => a.Type == AccountType.ActiveSpending && a.IsActive
                        && a.StatementCloseDay != null && a.PaymentDueDay != null)
            .ToListAsync(cancellationToken);

        foreach (var account in activeSpendingAccounts)
        {
            var qualifyingCategoryIds = await context.FundingRules
                .Where(f => f.Strategy == FundingStrategies.PayInFullAmex)
                .Select(f => f.CategoryId)
                .ToListAsync(cancellationToken);

            decimal monthlyBudgetTotal = 0m;
            foreach (var categoryId in qualifyingCategoryIds)
            {
                var currentPeriod = await context.BudgetPeriods
                    .Where(p => p.CategoryId == categoryId
                                && p.EffectiveFrom <= asOfDate
                                && (p.EffectiveThrough == null || p.EffectiveThrough >= asOfDate))
                    .FirstOrDefaultAsync(cancellationToken);

                if (currentPeriod is not null)
                {
                    monthlyBudgetTotal += proration.Convert(currentPeriod.Amount, currentPeriod.Frequency, Frequency.Monthly);
                }
            }

            // The actual-charges figure is every real charge on the account this cycle -
            // not just the ones already sorted into a PayInFullAmex category. This is a
            // pay-in-full card: an uncategorized charge still needs to be paid, so it can't
            // be invisible to "how much do I owe" just because it hasn't been categorized
            // yet. Amount < 0 excludes payments/credits (positive amounts) - those must
            // never offset real spending, or the forecast would understate what's owed by
            // whatever's already been paid toward the card this cycle. Still-unposted,
            // self-reported (screenshot-derived) charges are included alongside real posted
            // ones - see AmexCycleCalculator - so a looming overage is caught before it posts.
            var chargeTransactions = await context.BankTransactions
                .Where(t => t.AccountId == account.Id && t.Amount < 0
                            && (t.PostedDate != null || t.ImportSource == ManualChargeMatchingService.ManualScreenshotImportSource))
                .ToListAsync(cancellationToken);

            // Widened backward same as the debt-account/Direct path above, for the same
            // reason - a recently-due cycle isn't excluded just because its date passed,
            // only once a real matching payment transaction is found (or it ages fully out).
            var cycles = amexCycleCalculator.CalculateDuePayments(
                account.StatementCloseDay!.Value, account.PaymentDueDay!.Value, account.ExtraPayment ?? 0m,
                monthlyBudgetTotal, chargeTransactions, asOfDate, asOfDate.AddDays(-RecurrenceExpander.MaxMatchWindowDays), windowEnd);

            // Every account (including Amex) gets its own "{Name} Payment" category/merchant
            // rules at creation time (see AccountManagementService) - a real Amex payment
            // gets categorized into it exactly like a Chase/Discover payment would, so the
            // same reconciliation check applies here instead of leaving Amex permanently
            // manual-only.
            var amexPaymentCategoryId = accountPaymentCategoryIds.GetValueOrDefault(account.Id);
            foreach (var cycle in cycles)
            {
                var description = cycle.PendingSelfReportedAmount > 0m
                    ? $"{account.Name} Payment (includes ${cycle.PendingSelfReportedAmount:N2} pending, not yet posted)"
                    : $"{account.Name} Payment";
                var line = new LedgerLine
                {
                    Date = cycle.DueDate, Description = description, Amount = -cycle.Amount, AccountId = account.Id,
                    CategoryId = amexPaymentCategoryId, MatchWindowDays = RecurrenceExpander.MatchWindowDaysFor(Frequency.Monthly)
                };
                if (!IsAlreadyReflectedInAnActualTransaction(line, reconciliationTransactions))
                {
                    lines.Add(line);
                }
            }
        }

        // Manually confirmed/overridden occurrences stay in the ledger rather than being
        // removed - marked IsExcluded and pinned at the EffectiveDate/Amount captured at the
        // moment the user acted, so the row (and its dollar amount) stay visible and in place
        // instead of only living in a separate undo list. Deferral is looked up independently
        // of confirmation status, so undoing a confirmation on a previously-deferred payment
        // naturally brings it back still deferred - nothing here needs to know about that.
        var confirmations = await context.PaymentConfirmations.Include(c => c.Account).ToListAsync(cancellationToken);
        var confirmationsByAccountAndDate = confirmations.ToDictionary(c => (c.AccountId, c.OriginalDate));
        var matchedConfirmationIds = new HashSet<int>();

        var deferrals = await context.PaymentDeferrals.ToListAsync(cancellationToken);
        var deferralsByAccountAndDate = deferrals.ToDictionary(d => (d.AccountId, d.OriginalDate));

        // Real partial payments already made toward an occurrence - reduce its remaining
        // amount without excluding it entirely (unlike a confirmation), and independent of
        // deferral status, same reasoning as above.
        var partialPayments = await context.PartialPayments.ToListAsync(cancellationToken);
        var partialPaymentsByAccountAndDate = partialPayments
            .GroupBy(p => (p.AccountId, p.OriginalDate))
            .ToDictionary(g => g.Key, g => g.ToList());

        // A partial payment's own auto-created OneTimeEvent (the "$1000 today" cash record)
        // can coincidentally share its (AccountId, Date) with an unrelated deferral/
        // confirmation/other partial payment on the same account (e.g. paid on the bill's own
        // due date) - it must never be mistaken for that occurrence just because of that, so
        // it's excluded from all three matching lookups below and always shown as a plain row.
        var partialPaymentsByOwnEventId = partialPayments.ToDictionary(p => p.OneTimeEventId);

        // Once the real cash movement a partial payment recorded actually posts and syncs
        // normally, its synthetic OneTimeEvent line becomes redundant - the real transaction
        // already reduces the checking balance for real, so keeping the recorded line around
        // too would double-count it. Matched on account + exact amount + a several-day window
        // against real posted transactions - deliberately not against other forecast lines
        // (that's the unrelated-collision risk already guarded against above), so this can't
        // be swept into anything else the way that original bug was.
        var partialPaymentAccountIds = partialPayments.Select(p => p.AccountId).Distinct().ToList();
        var realTransactionsForPartialPayments = partialPaymentAccountIds.Count == 0
            ? []
            : await context.BankTransactions
                .Where(t => partialPaymentAccountIds.Contains(t.AccountId) && t.PostedDate != null)
                .ToListAsync(cancellationToken);

        BankTransaction? FindRealPostingFor(PartialPayment partialPayment) =>
            realTransactionsForPartialPayments.FirstOrDefault(t =>
                t.AccountId == partialPayment.AccountId && t.Amount == partialPayment.Amount
                && t.PostedDate is { } posted
                && posted >= partialPayment.PaidDate.AddDays(-PartialPaymentMatchWindowDays)
                && posted <= partialPayment.PaidDate.AddDays(PartialPaymentMatchWindowDays));

        var rows = new List<ForecastLedgerRow>();
        foreach (var line in lines)
        {
            if (line.SourceOneTimeEventId is { } sourceEventId && partialPaymentsByOwnEventId.TryGetValue(sourceEventId, out var ownPartialPayment))
            {
                var realPosting = FindRealPostingFor(ownPartialPayment);
                rows.Add(new ForecastLedgerRow
                {
                    Date = line.Date,
                    Description = realPosting is { PostedDate: { } posted }
                        ? $"{line.Description} - matched a real posted payment on {posted:MM/dd/yyyy}"
                        : line.Description,
                    Amount = line.Amount,
                    RunningBalance = 0m,
                    AccountId = line.AccountId,
                    OriginalDate = line.Date,
                    IsExcluded = realPosting is not null,
                    ExclusionReason = realPosting is not null ? ConfirmationReason.AutoReconciled : null
                });
                continue;
            }

            if (confirmationsByAccountAndDate.TryGetValue((line.AccountId, line.Date), out var confirmation))
            {
                matchedConfirmationIds.Add(confirmation.Id);
                rows.Add(new ForecastLedgerRow
                {
                    Date = confirmation.EffectiveDate,
                    Description = line.Description,
                    Amount = confirmation.Amount,
                    RunningBalance = 0m,
                    AccountId = line.AccountId,
                    OriginalDate = line.Date,
                    IsExcluded = true,
                    ExclusionReason = confirmation.Reason,
                    ConfirmationId = confirmation.Id
                });
                continue;
            }

            var isDeferred = deferralsByAccountAndDate.TryGetValue((line.AccountId, line.Date), out var deferral);
            var appliedPartialPayments = partialPaymentsByAccountAndDate.GetValueOrDefault((line.AccountId, line.Date), []);
            rows.Add(new ForecastLedgerRow
            {
                Date = isDeferred ? deferral!.DeferredToDate : line.Date,
                Description = line.Description,
                Amount = line.Amount + appliedPartialPayments.Sum(p => p.Amount),
                RunningBalance = 0m,
                AccountId = line.AccountId,
                OriginalDate = line.Date,
                PartialPayments = appliedPartialPayments
                    .Select(p => new PartialPaymentSummary { PartialPaymentId = p.Id, Amount = p.Amount, PaidDate = p.PaidDate })
                    .ToList(),
                IsDeferred = isDeferred,
                DeferralId = isDeferred ? deferral!.Id : null
            });
        }

        // A confirmation whose original occurrence didn't get (re)generated this run (e.g. it
        // has since aged outside the window) still needs to be reachable for Undo - shown from
        // its own captured snapshot rather than tied to a live line.
        foreach (var confirmation in confirmations.Where(c => !matchedConfirmationIds.Contains(c.Id)))
        {
            rows.Add(new ForecastLedgerRow
            {
                Date = confirmation.EffectiveDate,
                Description = $"{confirmation.Account.Name} Payment",
                Amount = confirmation.Amount,
                RunningBalance = 0m,
                AccountId = confirmation.AccountId,
                OriginalDate = confirmation.OriginalDate,
                IsExcluded = true,
                ExclusionReason = confirmation.Reason,
                ConfirmationId = confirmation.Id
            });
        }

        var excludedVisibilityCutoff = asOfDate.AddDays(-ExcludedPaymentVisibilityDays);
        rows = rows.Where(r => !r.IsExcluded || r.Date >= excludedVisibilityCutoff).OrderBy(r => r.Date).ToList();
        var runningBalance = startingBalance;
        foreach (var row in rows)
        {
            if (!row.IsExcluded)
            {
                runningBalance += row.Amount;
            }
            row.RunningBalance = runningBalance;
        }

        return new ForecastResult
        {
            StartingBalance = startingBalance,
            Rows = rows
        };
    }

    private static DateOnly ClampedDate(int year, int month, int day) =>
        new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));

    // A real payment can fall a little short of what's configured (e.g. an issuer raises the
    // real minimum payment before the user updates it here) without meaning the obligation
    // is unpaid - but a genuine partial payment must not be mistaken for a full one (see the
    // Amex case this guards against: a $2,334 payment is not proof a $5,852 cycle is settled).
    // Only a floor, not a band - overpaying by any amount always still counts as paid.
    private const decimal ReconciliationAmountToleranceFraction = 0.05m;

    // Matches purely on CategoryId, deliberately not AccountId too - a debt account's
    // synthetic rule uses the debt account's own AccountId (see above, it's also the
    // PaymentDeferral matching key), not the checking account the real payment actually
    // posts against, so AccountId isn't a usable signal here. CategoryId alone is already
    // unique per obligation (one current BudgetPeriod per category; one FundingRule per
    // debt account), so it doesn't need the extra filter.
    private static bool IsAlreadyReflectedInAnActualTransaction(LedgerLine line, IReadOnlyList<BankTransaction> transactions)
    {
        if (line.CategoryId is null) return false;

        var windowStart = line.Date.AddDays(-line.MatchWindowDays);
        var windowEnd = line.Date.AddDays(line.MatchWindowDays);
        var expectedAmount = Math.Abs(line.Amount);
        var minimumAcceptedAmount = expectedAmount * (1m - ReconciliationAmountToleranceFraction);

        return transactions.Any(t => t.CategoryId == line.CategoryId && t.PostedDate is { } posted && posted >= windowStart && posted <= windowEnd
            && Math.Abs(t.Amount) >= minimumAcceptedAmount);
    }
}
