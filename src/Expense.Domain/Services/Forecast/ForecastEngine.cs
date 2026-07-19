using Expense.Domain.Data;
using Expense.Domain.Entities;
using Expense.Domain.Services.Budgets;
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

        var oneTimeEvents = await context.OneTimeEvents.ToListAsync(cancellationToken);
        lines.AddRange(recurrenceExpander.Expand([], oneTimeEvents, asOfDate, windowEnd));

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
            // whatever's already been paid toward the card this cycle.
            var chargeTransactions = await context.BankTransactions
                .Where(t => t.AccountId == account.Id && t.PostedDate != null && t.Amount < 0)
                .ToListAsync(cancellationToken);

            var cycles = amexCycleCalculator.CalculateDuePayments(
                account.StatementCloseDay!.Value, account.PaymentDueDay!.Value, account.ExtraPayment ?? 0m,
                monthlyBudgetTotal, chargeTransactions, asOfDate, asOfDate, windowEnd);

            foreach (var cycle in cycles)
            {
                lines.Add(new LedgerLine
                {
                    Date = cycle.DueDate, Description = $"{account.Name} Payment", Amount = -cycle.Amount, AccountId = account.Id
                });
            }
        }

        var confirmations = await context.PaymentConfirmations.Include(c => c.Account).ToListAsync(cancellationToken);
        var confirmedAccountAndDates = confirmations.Select(c => (c.AccountId, c.OriginalDate)).ToHashSet();
        lines = lines.Where(l => !confirmedAccountAndDates.Contains((l.AccountId, l.Date))).ToList();

        var deferrals = await context.PaymentDeferrals.ToListAsync(cancellationToken);
        var deferralsByAccountAndDate = deferrals.ToDictionary(d => (d.AccountId, d.OriginalDate));

        var rows = new List<ForecastLedgerRow>();
        var runningBalance = startingBalance;
        foreach (var line in lines.OrderBy(l => deferralsByAccountAndDate.TryGetValue((l.AccountId, l.Date), out var d) ? d.DeferredToDate : l.Date))
        {
            var isDeferred = deferralsByAccountAndDate.TryGetValue((line.AccountId, line.Date), out var deferral);
            runningBalance += line.Amount;
            rows.Add(new ForecastLedgerRow
            {
                Date = isDeferred ? deferral!.DeferredToDate : line.Date,
                Description = line.Description,
                Amount = line.Amount,
                RunningBalance = runningBalance,
                AccountId = line.AccountId,
                OriginalDate = line.Date,
                IsDeferred = isDeferred,
                DeferralId = isDeferred ? deferral!.Id : null
            });
        }

        return new ForecastResult
        {
            StartingBalance = startingBalance,
            Rows = rows,
            Confirmations = confirmations.Select(c => new ConfirmedPayment
            {
                ConfirmationId = c.Id, AccountId = c.AccountId, AccountName = c.Account.Name, OriginalDate = c.OriginalDate, Reason = c.Reason
            }).ToList()
        };
    }

    private static DateOnly ClampedDate(int year, int month, int day) =>
        new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));

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
        return transactions.Any(t => t.CategoryId == line.CategoryId && t.PostedDate is { } posted && posted >= windowStart && posted <= windowEnd);
    }
}
