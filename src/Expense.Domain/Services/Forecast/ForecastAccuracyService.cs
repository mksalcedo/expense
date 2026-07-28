using Expense.Domain.Data;
using Expense.Domain.Entities;
using Expense.Domain.Services.Budgets;
using Expense.Domain.Services.Ingestion.ManualCharges;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Forecast;

/// <summary>
/// Compares what was assumed for each recurring obligation (bill/income/debt payment/Amex
/// cycle) against what actually posted, over a recent lookback window - answers "did
/// anything come in meaningfully different than forecasted" using only data that already
/// exists (no persisted forecast history required). Deliberately reuses the exact same
/// RecurringRule construction and CategoryId/match-window reconciliation ForecastEngine
/// already does, just retaining the matched transaction's real amount/date for comparison
/// instead of discarding it once a match is found.
/// </summary>
public class ForecastAccuracyService(RecurrenceExpander recurrenceExpander, AmexCycleCalculator amexCycleCalculator, BudgetProrationService proration)
{
    public async Task<List<AccuracyComparison>> GetRecentAccuracyAsync(
        ExpenseDbContext context, DateOnly asOfDate, int lookbackDays = 90, CancellationToken cancellationToken = default)
    {
        var windowStart = asOfDate.AddDays(-lookbackDays);
        var comparisons = new List<AccuracyComparison>();

        // Every historical version of the budget that overlaps the lookback window, not
        // just the current one - a past occurrence must be judged against whatever was
        // actually scheduled on its own date, not whatever the category's budget happens
        // to be as of asOfDate (editing a budget today must not rewrite history). One
        // RecurringRule per version, clipped to its own StartDate/EndDate - RecurrenceExpander
        // already honors those bounds, so each version only ever produces occurrences for
        // the span it was really in effect.
        var directPeriods = await context.BudgetPeriods
            .Where(p => p.Anchor != null && p.AccountId != null
                        && p.EffectiveFrom <= asOfDate && (p.EffectiveThrough == null || p.EffectiveThrough >= windowStart))
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
            StartDate = p.EffectiveFrom,
            EndDate = p.EffectiveThrough
        }).ToList();

        var debtAccounts = await context.Accounts
            .Where(a => a.Type == AccountType.Debt && a.IsActive && a.PaymentDueDay != null)
            .ToListAsync(cancellationToken);

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

        var lines = recurrenceExpander.Expand(recurringRules, [], windowStart, asOfDate);

        var reconciliationTransactions = await context.BankTransactions
            .Where(t => t.CategoryId != null && t.PostedDate != null && t.PostedDate >= windowStart.AddDays(-RecurrenceExpander.MaxMatchWindowDays) && t.PostedDate <= asOfDate)
            .ToListAsync(cancellationToken);

        // The bank-sync history (e.g. SimpleFin's own pull window) may not go back nearly
        // as far as this report's lookback - an occurrence from before the earliest posted
        // transaction we've ever seen has no observed data at all ("no match found" there
        // means "we never looked", not "it didn't post") and must never be shown as a false
        // miss. Only real, already-observed silence counts as a genuine miss.
        var earliestObservedPostedDate = await context.BankTransactions
            .Where(t => t.PostedDate != null)
            .Select(t => t.PostedDate)
            .MinAsync(cancellationToken);

        foreach (var line in lines)
        {
            if (line.CategoryId is null) continue; // no way to match without a category link

            var deadline = line.Date.AddDays(line.MatchWindowDays);
            var match = reconciliationTransactions.FirstOrDefault(t =>
                t.CategoryId == line.CategoryId
                && t.PostedDate >= line.Date.AddDays(-line.MatchWindowDays)
                && t.PostedDate <= deadline);

            // Too early to call a still-unmatched occurrence a miss - it may just not have
            // posted yet (bank processing delay), not have failed to happen.
            if (match is null && deadline > asOfDate) continue;

            if (match is null && (earliestObservedPostedDate is null || line.Date < earliestObservedPostedDate)) continue;

            comparisons.Add(new AccuracyComparison
            {
                Name = line.Description,
                AccountId = line.AccountId,
                ScheduledDate = line.Date,
                ScheduledAmount = Math.Abs(line.Amount),
                ActualDate = match?.PostedDate,
                ActualAmount = match is null ? null : Math.Abs(match.Amount)
            });
        }

        var activeSpendingAccounts = await context.Accounts
            .Where(a => a.Type == AccountType.ActiveSpending && a.IsActive && a.StatementCloseDay != null && a.PaymentDueDay != null)
            .ToListAsync(cancellationToken);

        foreach (var account in activeSpendingAccounts)
        {
            var qualifyingCategoryIds = await context.FundingRules
                .Where(f => f.Strategy == FundingStrategies.PayInFullAmex)
                .Select(f => f.CategoryId)
                .ToListAsync(cancellationToken);

            // Every historical version overlapping the lookback window, not just the
            // current one - each past cycle needs the budget that was actually in effect
            // while it was open, not whatever it is as of asOfDate (same fix as the
            // Direct-funded periods above).
            var allPeriods = await context.BudgetPeriods
                .Where(p => qualifyingCategoryIds.Contains(p.CategoryId)
                            && p.EffectiveFrom <= asOfDate && (p.EffectiveThrough == null || p.EffectiveThrough >= windowStart))
                .ToListAsync(cancellationToken);

            decimal MonthlyBudgetTotalAsOf(DateOnly date) => qualifyingCategoryIds.Sum(categoryId =>
            {
                var period = allPeriods.FirstOrDefault(p =>
                    p.CategoryId == categoryId && p.EffectiveFrom <= date && (p.EffectiveThrough == null || p.EffectiveThrough >= date));
                return period is null ? 0m : proration.Convert(period.Amount, period.Frequency, Frequency.Monthly);
            });

            var chargeTransactions = await context.BankTransactions
                .Where(t => t.AccountId == account.Id && t.Amount < 0
                            && (t.PostedDate != null || t.ImportSource == ManualChargeMatchingService.ManualScreenshotImportSource || t.ImportSource == "Plaid"))
                .ToListAsync(cancellationToken);

            // Same blind-spot guard as the Direct/debt-payment comparisons above, scoped to
            // this specific account - a cycle that ended before this account's own earliest
            // observed transaction has zero real overlap, so its "actual" figure would just
            // reflect the absence of data, not genuinely low spending.
            var earliestObservedForAccount = await context.BankTransactions
                .Where(t => t.AccountId == account.Id && t.PostedDate != null)
                .Select(t => t.PostedDate)
                .MinAsync(cancellationToken);

            var extraPrincipal = account.ExtraPayment ?? 0m;
            // The budget total passed here only affects AmexCycleResult.Amount, which this
            // report never reads (it recomputes its own ScheduledAmount per cycle below) -
            // asOfDate's total is as good a value as any to satisfy the signature.
            var cycles = amexCycleCalculator.CalculateDuePayments(
                account.StatementCloseDay!.Value, account.PaymentDueDay!.Value, extraPrincipal,
                MonthlyBudgetTotalAsOf(asOfDate), chargeTransactions, asOfDate, windowStart, asOfDate);

            // Compare true real spending against the budget-only assumption, independent of
            // the MAX(actual,budget) payment floor - a cycle that came in UNDER budget is a
            // real (favorable) variance worth showing too, not just overages.
            foreach (var cycle in cycles.Where(c => !c.IsFuture && (earliestObservedForAccount is null || c.CycleEnd >= earliestObservedForAccount)))
            {
                var cycleBudgetTotal = MonthlyBudgetTotalAsOf(cycle.CycleEnd);
                comparisons.Add(new AccuracyComparison
                {
                    Name = $"{account.Name} Payment",
                    AccountId = account.Id,
                    ScheduledDate = cycle.DueDate,
                    ScheduledAmount = cycleBudgetTotal + extraPrincipal,
                    ActualDate = cycle.DueDate,
                    ActualAmount = cycle.ActualAmount + extraPrincipal
                });
            }
        }

        return comparisons;
    }

    private static DateOnly ClampedDate(int year, int month, int day) =>
        new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));
}
