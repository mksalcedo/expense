using Expense.Domain.Data;
using Expense.Domain.Entities;
using Expense.Domain.Services.Budgets;
using Expense.Domain.Services.Ingestion;
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
        // Two different sources (e.g. SimpleFin and a Plaid backup import) can both write
        // a snapshot for the same calendar day - sort by the real AsOfTimestamp (not just
        // AsOfDate) so the genuinely freshest data wins, e.g. a stale SimpleFin sync never
        // beats a more current Plaid pull just because it happened to run later. Id stays
        // as a final fallback only for a true same-instant tie.
        var startingBalance = await context.CheckingBalanceSnapshots
            .OrderByDescending(s => s.AsOfTimestamp)
            .ThenByDescending(s => s.Id)
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
                StartDate = account.PaymentStartDate ?? DateOnly.MinValue
            });
        }

        // Widened backward so a recently-due-but-unmatched occurrence isn't silently
        // dropped just because its scheduled date already passed - it stays projected
        // until a real matching transaction (checked below) excludes it.
        var lines = recurrenceExpander.Expand(
            recurringRules, [], asOfDate.AddDays(-RecurrenceExpander.MaxMatchWindowDays), windowEnd);

        // Which occurrence (if any) each categorized transaction satisfies is decided once,
        // durably, by TransactionReconciliationService - not re-derived here via a date
        // window relative to asOfDate (see docs/forecast-reconciliation-marker-plan.md for
        // why that used to silently miss transactions posted more than ~28 days ago).
        var reconciliationTransactions = await context.BankTransactions
            .Where(t => t.CategoryId != null && t.ReconciledOccurrenceDate != null)
            .ToListAsync(cancellationToken);

        // Reconciliation status is no longer decided here - a matched line still becomes a
        // row (marked IsExcluded/AutoReconciled in the main loop below), same visible-then-
        // fades treatment every other exclusion reason gets, instead of silently vanishing
        // with no trace on the page the user was already looking at.

        // Same backward-widening as above - a deferred/confirmed one-time event's original
        // date can end up in the past relative to asOfDate, and it must not vanish just
        // because of that.
        var oneTimeEvents = await context.OneTimeEvents.ToListAsync(cancellationToken);
        var oneTimeEventLines = recurrenceExpander.Expand([], oneTimeEvents, asOfDate.AddDays(-RecurrenceExpander.MaxMatchWindowDays), windowEnd);
        lines.AddRange(oneTimeEventLines);

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
            // whatever's already been paid toward the card this cycle. Still-unposted
            // charges are included alongside real posted ones - both self-reported
            // (screenshot-derived) and Plaid-reported pending charges - see
            // AmexCycleCalculator - so a looming overage is caught before it posts.
            var chargeTransactions = await context.BankTransactions
                .Where(t => t.AccountId == account.Id && t.Amount < 0)
                .Where(BankTransactionReconciliation.CountsAsReal)
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
                lines.Add(new LedgerLine
                {
                    Date = cycle.DueDate, Description = description, Amount = -cycle.Amount, AccountId = account.Id,
                    CategoryId = amexPaymentCategoryId, MatchWindowDays = RecurrenceExpander.MatchWindowDaysFor(Frequency.Monthly)
                });
            }
        }

        // Manually confirmed/overridden occurrences stay in the ledger rather than being
        // removed - marked IsExcluded and pinned at the EffectiveDate/Amount captured at the
        // moment the user acted, so the row (and its dollar amount) stay visible and in place
        // instead of only living in a separate undo list. Deferral is looked up independently
        // of confirmation status, so undoing a confirmation on a previously-deferred payment
        // naturally brings it back still deferred - nothing here needs to know about that.
        // Keyed by (AccountId, CategoryId, OriginalDate), not just (AccountId, OriginalDate) -
        // more than one line can share an account and date (e.g. a recurring bill anchored on
        // the same day a one-time top-up happens to land) and CategoryId is what tells them
        // apart (see PaymentConfirmation.CategoryId doc comment - a real bug found live
        // 2026-08-04, a Water top-up confirmation was silently "inherited" by MAS's unrelated
        // same-day/same-account income line).
        var confirmations = await context.PaymentConfirmations.Include(c => c.Account).ToListAsync(cancellationToken);
        var confirmationsByAccountAndDate = confirmations.ToDictionary(c => (c.AccountId, c.CategoryId, c.OriginalDate));
        var matchedConfirmationIds = new HashSet<int>();

        // Categories where several real transactions legitimately contribute to one line (e.g.
        // Piano - several payers on their own schedules) - these get a full list of candidate
        // transactions further below (PartialPaymentCandidates), never a single "this is the
        // whole story" near-miss suggestion, since offering Change Amount on just one of
        // several contributors would wrongly write off the rest as never coming.
        var calendarMonthCategoryIds = await context.Categories
            .Where(c => c.ReconcileByCalendarMonth)
            .Select(c => c.Id)
            .ToHashSetAsync(cancellationToken);

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
        // too would double-count it. Matched on a several-day window around the paid date,
        // plus:
        //  - CategoryId, for a debt/Amex-payment partial payment - its own AccountId is the
        //    debt/Amex account itself, but the real payment always posts against the
        //    checking account instead, tagged with that account's own payment category
        //    (same reasoning as IsAlreadyReflectedInAnActualTransaction above - a real bug
        //    found and fixed this session, confirmed against real data: the real posting
        //    never shares an AccountId with the debt/Amex account it pays down).
        //  - AccountId, as a fallback - a Direct-funded bill's partial payment already uses
        //    the checking/funding account as its own AccountId (no separate debt/Amex
        //    account in between), so account-based matching is already correct there.
        // Amount is compared via Math.Abs - a real expense transaction is stored negative
        // (this app's convention), while PartialPayment.Amount is always a positive
        // magnitude - deliberately not matched against other forecast lines (that's the
        // unrelated-collision risk already guarded against above), so this can't be swept
        // into anything else the way that original bug was.
        var partialPaymentAccountIds = partialPayments.Select(p => p.AccountId).Distinct().ToList();
        var partialPaymentCategoryIds = partialPaymentAccountIds
            .Select(id => accountPaymentCategoryIds.GetValueOrDefault(id))
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var realTransactionsForPartialPayments = (partialPaymentAccountIds.Count == 0 && partialPaymentCategoryIds.Count == 0)
            ? []
            : await context.BankTransactions
                .Where(t => partialPaymentAccountIds.Contains(t.AccountId)
                            || (t.CategoryId != null && partialPaymentCategoryIds.Contains(t.CategoryId.Value)))
                .Where(BankTransactionReconciliation.CountsAsReal)
                .ToListAsync(cancellationToken);

        BankTransaction? FindRealPostingFor(PartialPayment partialPayment)
        {
            var paymentCategoryId = accountPaymentCategoryIds.GetValueOrDefault(partialPayment.AccountId);
            return realTransactionsForPartialPayments.FirstOrDefault(t =>
                (paymentCategoryId is not null ? t.CategoryId == paymentCategoryId : t.AccountId == partialPayment.AccountId)
                && Math.Abs(t.Amount) == partialPayment.Amount
                && t.EffectiveDate() >= partialPayment.PaidDate.AddDays(-PartialPaymentMatchWindowDays)
                && t.EffectiveDate() <= partialPayment.PaidDate.AddDays(PartialPaymentMatchWindowDays));
        }

        var rows = new List<ForecastLedgerRow>();
        foreach (var line in lines)
        {
            if (line.SourceOneTimeEventId is { } sourceEventId && partialPaymentsByOwnEventId.TryGetValue(sourceEventId, out var ownPartialPayment))
            {
                var realPosting = FindRealPostingFor(ownPartialPayment);
                rows.Add(new ForecastLedgerRow
                {
                    Date = line.Date,
                    Description = realPosting switch
                    {
                        { PostedDate: { } posted } => $"{line.Description} - matched a real posted payment on {posted:MM/dd/yyyy}",
                        not null => $"{line.Description} - matched a real pending payment on {realPosting.TransactionDate:MM/dd/yyyy}",
                        null => line.Description
                    },
                    Amount = line.Amount,
                    RunningBalance = 0m,
                    AccountId = line.AccountId,
                    OriginalDate = line.Date,
                    CategoryId = line.CategoryId,
                    IsExcluded = realPosting is not null,
                    ExclusionReason = realPosting is not null ? ConfirmationReason.AutoReconciled : null
                });
                continue;
            }

            if (confirmationsByAccountAndDate.TryGetValue((line.AccountId, line.CategoryId, line.Date), out var confirmation))
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
                    CategoryId = line.CategoryId,
                    IsExcluded = true,
                    ExclusionReason = confirmation.Reason,
                    ConfirmationId = confirmation.Id,
                    ResolvedDate = DateOnly.FromDateTime(confirmation.CreatedAt.UtcDateTime)
                });
                continue;
            }

            // Automatic reconciliation against a real posted transaction - a one-time event
            // uses the windowed category check (the real payment that covers it is the same
            // one that already satisfied a same-category recurring line, so it can't also
            // carry this event's exact date); every other line type uses the exact-date
            // check, unchanged from before this consistency pass. Shows the real transaction's
            // actual amount, not the budgeted line's amount - a $70.97 real bill against a
            // $76.68 budgeted line should read as "$70.97, resolved," not silently claim the
            // stale estimate was exactly right (found live 2026-08-04).
            var reflectingTransaction = line.SourceOneTimeEventId is not null
                ? FindOneTimeEventCoveringTransaction(line, reconciliationTransactions)
                : FindReflectingTransaction(line, reconciliationTransactions);
            if (reflectingTransaction is not null)
            {
                rows.Add(new ForecastLedgerRow
                {
                    Date = line.Date,
                    Description = line.Description,
                    Amount = reflectingTransaction.Amount,
                    RunningBalance = 0m,
                    AccountId = line.AccountId,
                    OriginalDate = line.Date,
                    CategoryId = line.CategoryId,
                    IsExcluded = true,
                    ExclusionReason = ConfirmationReason.AutoReconciled,
                    ResolvedDate = reflectingTransaction.EffectiveDate()
                });
                continue;
            }

            var isDeferred = deferralsByAccountAndDate.TryGetValue((line.AccountId, line.Date), out var deferral);
            var appliedPartialPayments = partialPaymentsByAccountAndDate.GetValueOrDefault((line.AccountId, line.Date), []);
            var isCalendarMonthCategory = line.CategoryId is { } lineCategoryId && calendarMonthCategoryIds.Contains(lineCategoryId);

            decimal? nearMissAmount = null;
            DateOnly? nearMissDate = null;
            List<PartialPaymentCandidate>? partialPaymentCandidates = null;
            if (isCalendarMonthCategory)
            {
                partialPaymentCandidates = BuildPartialPaymentCandidates(line, reconciliationTransactions, appliedPartialPayments);
            }
            else
            {
                var nearMissTransaction = line.SourceOneTimeEventId is not null
                    ? FindOneTimeEventNearMissTransaction(line, reconciliationTransactions)
                    : FindNearMissTransaction(line, reconciliationTransactions);
                nearMissAmount = nearMissTransaction?.Amount;
                nearMissDate = nearMissTransaction is null ? null : nearMissTransaction.PostedDate ?? nearMissTransaction.TransactionDate;
            }

            // PartialPayment.Amount is always a positive magnitude (the UI never makes anyone
            // type a sign) - for an expense line (budgeted negative) that shrinks the debt
            // toward zero by adding it; for an income line (budgeted positive) it must instead
            // shrink the remaining expected amount by subtracting it, or a real partial
            // payment would wrongly inflate what's still expected (found live 2026-08-04).
            // Clamped at zero, never crossing past it to flip sign - a multi-payer income
            // line (e.g. Piano) can easily receive more than its budgeted total across several
            // real payments, and the extra is already reflected in the Forecast's real
            // starting balance (it already posted), so it must never appear as a second,
            // separate negative/positive line item once fully covered (found live 2026-08-10).
            var partialPaymentTotal = appliedPartialPayments.Sum(p => p.Amount);
            var adjustedAmount = line.Amount < 0
                ? Math.Min(0m, line.Amount + partialPaymentTotal)
                : Math.Max(0m, line.Amount - partialPaymentTotal);

            // Once fully covered, the row should drop off the active ledger like any other
            // resolved item - but only once there's truly nothing left to do: a calendar-month
            // category can still have a real, unclaimed transaction sitting in its candidate
            // list even after the budgeted amount clamps to zero (found live 2026-08-10).
            var hasUnclaimedCandidate = partialPaymentCandidates?.Any(c => c.PartialPaymentId is null) ?? false;
            var isFullyCoveredByPartialPayments = appliedPartialPayments.Count > 0 && adjustedAmount == 0m && !hasUnclaimedCandidate;

            // No single real transaction resolved this one - several did, on different dates -
            // so the most recent payment's date is the closest honest answer to "when."
            var resolvedDate = isFullyCoveredByPartialPayments
                ? appliedPartialPayments.Max(p => p.PaidDate)
                : (DateOnly?)null;

            rows.Add(new ForecastLedgerRow
            {
                Date = isDeferred ? deferral!.DeferredToDate : line.Date,
                Description = line.Description,
                Amount = adjustedAmount,
                RunningBalance = 0m,
                AccountId = line.AccountId,
                OriginalDate = line.Date,
                CategoryId = line.CategoryId,
                PartialPayments = appliedPartialPayments
                    .Select(p => new PartialPaymentSummary { PartialPaymentId = p.Id, Amount = p.Amount, PaidDate = p.PaidDate })
                    .ToList(),
                IsDeferred = isDeferred,
                DeferralId = isDeferred ? deferral!.Id : null,
                SuggestedOverrideAmount = nearMissAmount,
                SuggestedOverrideDate = nearMissDate,
                PartialPaymentCandidates = partialPaymentCandidates,
                IsExcluded = isFullyCoveredByPartialPayments,
                ExclusionReason = isFullyCoveredByPartialPayments ? ConfirmationReason.AutoReconciled : null,
                ResolvedDate = resolvedDate
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
                ConfirmationId = confirmation.Id,
                ResolvedDate = DateOnly.FromDateTime(confirmation.CreatedAt.UtcDateTime)
            });
        }

        var excludedVisibilityCutoff = asOfDate.AddDays(-ExcludedPaymentVisibilityDays);
        // Income before expense on days with more than one line - roughly matches how
        // banks tend to post credits before debits within a day, so the running balance
        // shown at each same-day row reads as less alarmist than an arbitrary order would.
        rows = rows.Where(r => !r.IsExcluded || r.Date >= excludedVisibilityCutoff)
            .OrderBy(r => r.Date).ThenByDescending(r => r.Amount >= 0).ToList();
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
    private static BankTransaction? FindReflectingTransaction(LedgerLine line, IReadOnlyList<BankTransaction> transactions)
    {
        if (line.CategoryId is null) return null;

        var expectedAmount = Math.Abs(line.Amount);
        var minimumAcceptedAmount = expectedAmount * (1m - ReconciliationAmountToleranceFraction);

        return transactions.FirstOrDefault(t => t.CategoryId == line.CategoryId && t.ReconciledOccurrenceDate == line.Date
            && Math.Abs(t.Amount) >= minimumAcceptedAmount);
    }

    /// <summary>
    /// A categorized one-time event (e.g. a top-up alongside an existing recurring bill in the
    /// same category - see CategorizedOneTimeEvent_IsExcluded_WhenItsCategoryHasANearbyReconciledTransaction)
    /// can't reuse IsAlreadyReflectedInAnActualTransaction's exact-date match: the real payment
    /// that covers it is the SAME one that already satisfied the recurring occurrence, and a
    /// BankTransaction can only ever record one ReconciledOccurrenceDate. Instead, treat "this
    /// category already reconciled to a date within the usual match window of this event's own
    /// date" as evidence the same payment covered the top-up too - still gated by the same
    /// amount floor so an unrelated, too-small reconciled transaction can't count.
    /// </summary>
    private static BankTransaction? FindOneTimeEventCoveringTransaction(LedgerLine line, IReadOnlyList<BankTransaction> transactions)
    {
        if (line.CategoryId is null) return null;

        var expectedAmount = Math.Abs(line.Amount);
        var minimumAcceptedAmount = expectedAmount * (1m - ReconciliationAmountToleranceFraction);

        return transactions.FirstOrDefault(t => t.CategoryId == line.CategoryId && t.ReconciledOccurrenceDate is { } reconciledDate
            && Math.Abs(reconciledDate.DayNumber - line.Date.DayNumber) <= RecurrenceExpander.MaxMatchWindowDays
            && Math.Abs(t.Amount) >= minimumAcceptedAmount);
    }

    /// <summary>
    /// Same category+date match as FindReflectingTransaction, deliberately without the
    /// amount-tolerance floor - a real bill that's genuinely different from what's budgeted
    /// (not a partial payment) is exactly what the tolerance floor can't safely tell apart on
    /// its own. Surfaced only as a suggestion for a human to confirm via Override, never used
    /// to auto-exclude anything - the floor itself stays exactly as strict as before.
    /// </summary>
    private static BankTransaction? FindNearMissTransaction(LedgerLine line, IReadOnlyList<BankTransaction> transactions)
    {
        if (line.CategoryId is null) return null;

        return transactions.FirstOrDefault(t => t.CategoryId == line.CategoryId && t.ReconciledOccurrenceDate == line.Date);
    }

    /// <summary>Windowed counterpart to FindNearMissTransaction, for a one-time event - same relationship as FindOneTimeEventCoveringTransaction has to FindReflectingTransaction.</summary>
    private static BankTransaction? FindOneTimeEventNearMissTransaction(LedgerLine line, IReadOnlyList<BankTransaction> transactions)
    {
        if (line.CategoryId is null) return null;

        return transactions.FirstOrDefault(t => t.CategoryId == line.CategoryId && t.ReconciledOccurrenceDate is { } reconciledDate
            && Math.Abs(reconciledDate.DayNumber - line.Date.DayNumber) <= RecurrenceExpander.MaxMatchWindowDays);
    }

    /// <summary>
    /// Every real transaction reconciled to this line's date (already correctly bucketed by
    /// ReconcileByCalendarMonth's own matching, see TransactionReconciliationService), each
    /// tagged with the PartialPayment that already claims it (if any, matched the same way
    /// PartialPaymentService's own reconciliation does: exact amount, PaidDate within
    /// PartialPaymentMatchWindowDays) - plus any recorded partial payment that never matched a
    /// real transaction at all, so a manually-entered one (e.g. cash, never synced) is never
    /// silently lost from view.
    /// </summary>
    private static List<PartialPaymentCandidate> BuildPartialPaymentCandidates(
        LedgerLine line, IReadOnlyList<BankTransaction> transactions, IReadOnlyList<PartialPayment> appliedPartialPayments)
    {
        var candidates = new List<PartialPaymentCandidate>();
        var matchedPartialPaymentIds = new HashSet<int>();

        // Ordered by effective date so, when two same-amount transactions both fall within a
        // single PartialPayment's match window, the earlier (closer, more likely the one it
        // was actually recorded against) transaction claims it - not an arbitrary DB order.
        var orderedTransactions = transactions
            .Where(t => t.CategoryId == line.CategoryId && t.ReconciledOccurrenceDate == line.Date)
            .OrderBy(t => t.PostedDate ?? t.TransactionDate);

        foreach (var txn in orderedTransactions)
        {
            var effectiveDate = txn.PostedDate ?? txn.TransactionDate;
            var amount = Math.Abs(txn.Amount);
            // Excludes PartialPayments already claimed by an earlier transaction in this same
            // loop - without this, two real transactions of the same amount could both match
            // the one PartialPayment record that only actually covers one of them, showing
            // both as "recorded" while the dollar reduction only ever counts it once (found
            // live 2026-08-07, a real Piano payment).
            var matchingPartialPayment = appliedPartialPayments.FirstOrDefault(p =>
                !matchedPartialPaymentIds.Contains(p.Id)
                && p.Amount == amount
                && p.PaidDate >= effectiveDate.AddDays(-PartialPaymentMatchWindowDays)
                && p.PaidDate <= effectiveDate.AddDays(PartialPaymentMatchWindowDays));

            if (matchingPartialPayment is not null)
            {
                matchedPartialPaymentIds.Add(matchingPartialPayment.Id);
            }
            candidates.Add(new PartialPaymentCandidate { Amount = amount, Date = effectiveDate, PartialPaymentId = matchingPartialPayment?.Id });
        }

        candidates.AddRange(appliedPartialPayments
            .Where(p => !matchedPartialPaymentIds.Contains(p.Id))
            .Select(p => new PartialPaymentCandidate { Amount = p.Amount, Date = p.PaidDate, PartialPaymentId = p.Id }));

        return candidates.OrderBy(c => c.Date).ToList();
    }
}
