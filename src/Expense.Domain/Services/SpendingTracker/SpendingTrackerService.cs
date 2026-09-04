using Expense.Domain.Data;
using Expense.Domain.Entities;
using Expense.Domain.Services.Budgets;
using Expense.Domain.Services.Ingestion;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.SpendingTracker;

/// <summary>
/// Current-week/current-month budget vs. actual vs. remaining, scoped to TrackedBudget
/// categories (the only ones with real day-to-day variable spending to track - Direct
/// categories are fixed bills/income, AccountPayment categories are paydown-only debt).
///
/// Carryover: a category's own budgeted Frequency decides which of the two views is its
/// "native" one (Weekly -> the week view, everything else -> the month view) - that view
/// shows a real rolling balance (see CarryoverCalculator), the other view stays a plain
/// this-period-only Budget minus Actual, exactly as before carryover existed. See
/// Category.CarryoverAnchorDate/CarryoverCapMultiplier and ResetCarryoverAsync.
///
/// PendingAmount is spend only (negative-amount transactions) - real checking activity
/// includes plenty of legitimately-uncategorized non-spend rows (paycheck deposits, etc.),
/// and this view is specifically about "have I sorted all my spending into a category yet."
/// </summary>
public class SpendingTrackerService(BudgetProrationService proration)
{
    public Task<SpendingTrackerResult> GetCurrentWeekAsync(ExpenseDbContext context, DateOnly asOfDate, CancellationToken cancellationToken = default)
        => GetSummaryAsync(context, PeriodStart(asOfDate, Frequency.Weekly), PeriodEnd(PeriodStart(asOfDate, Frequency.Weekly), Frequency.Weekly), Frequency.Weekly, asOfDate, cancellationToken);

    public Task<SpendingTrackerResult> GetCurrentMonthAsync(ExpenseDbContext context, DateOnly asOfDate, CancellationToken cancellationToken = default)
        => GetSummaryAsync(context, PeriodStart(asOfDate, Frequency.Monthly), PeriodEnd(PeriodStart(asOfDate, Frequency.Monthly), Frequency.Monthly), Frequency.Monthly, asOfDate, cancellationToken);

    /// <summary>
    /// "Starting this period" zeroes CarryoverAnchorDate to today's period, wiping any
    /// carried-in balance immediately. "Starting next period" only queues
    /// PendingCarryoverAnchorDate - the period already in progress keeps whatever it already
    /// carried, and the reset only takes effect once that next period actually begins (see
    /// ResolveEffectiveAnchor).
    /// </summary>
    public async Task ResetCarryoverAsync(ExpenseDbContext context, int categoryId, bool startingNextPeriod, DateOnly asOfDate, CancellationToken cancellationToken = default)
    {
        var category = await context.Categories.SingleAsync(c => c.Id == categoryId, cancellationToken);
        var activeBudget = await context.BudgetPeriods
            .Where(p => p.CategoryId == categoryId && p.EffectiveThrough == null)
            .SingleOrDefaultAsync(cancellationToken);
        var frequency = DetermineNativeFrequency(activeBudget);
        var currentPeriodStart = PeriodStart(asOfDate, frequency);

        if (startingNextPeriod)
        {
            category.PendingCarryoverAnchorDate = NextPeriodStart(currentPeriodStart, frequency);
        }
        else
        {
            category.CarryoverAnchorDate = currentPeriodStart;
            category.PendingCarryoverAnchorDate = null;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The individual bank transactions and Amazon items making up a category's Actual
    /// figure for a period - same filtering predicates as the aggregated totals in
    /// GetSummaryAsync, so the two always reconcile exactly.
    /// </summary>
    public async Task<List<CategoryTransactionLine>> GetCategoryTransactionsAsync(
        ExpenseDbContext context, int categoryId, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default)
    {
        var bankLines = await context.BankTransactions
            .Where(t => !t.IsAmazonMerchant && t.CategoryId == categoryId)
            .Where(BankTransactionReconciliation.CountsAsReal)
            .Where(t => (t.PostedDate ?? t.TransactionDate) >= periodStart && (t.PostedDate ?? t.TransactionDate) <= periodEnd)
            .Select(t => new CategoryTransactionLine { Date = t.PostedDate ?? t.TransactionDate, Description = t.Description, Amount = -t.Amount })
            .ToListAsync(cancellationToken);

        var amazonLines = await context.AmazonOrderItems
            .Where(i => i.CategoryId == categoryId && i.OrderDate >= periodStart && i.OrderDate <= periodEnd)
            .Select(i => new CategoryTransactionLine { Date = i.OrderDate, Description = i.ItemTitle, Amount = i.Price * i.Quantity + i.TaxAllocated - (i.RefundAmount ?? 0m) })
            .ToListAsync(cancellationToken);

        return bankLines.Concat(amazonLines).OrderBy(l => l.Date).ToList();
    }

    private static Frequency DetermineNativeFrequency(BudgetPeriod? activeBudget) =>
        activeBudget?.Frequency == Frequency.Weekly ? Frequency.Weekly : Frequency.Monthly;

    private static DateOnly PeriodStart(DateOnly date, Frequency frequency) => frequency switch
    {
        Frequency.Weekly => date.AddDays(-(int)date.DayOfWeek),
        _ => new DateOnly(date.Year, date.Month, 1)
    };

    private static DateOnly PeriodEnd(DateOnly periodStart, Frequency frequency) => frequency switch
    {
        Frequency.Weekly => periodStart.AddDays(6),
        _ => periodStart.AddMonths(1).AddDays(-1)
    };

    private static DateOnly NextPeriodStart(DateOnly periodStart, Frequency frequency) =>
        frequency == Frequency.Weekly ? periodStart.AddDays(7) : periodStart.AddMonths(1);

    private static DateOnly ResolveEffectiveAnchor(Category category, DateOnly currentPeriodStart) =>
        category.PendingCarryoverAnchorDate is { } pending && pending <= currentPeriodStart
            ? pending
            : category.CarryoverAnchorDate;

    private static List<DateOnly> EnumeratePeriodStarts(DateOnly anchorDate, DateOnly asOfDate, Frequency frequency)
    {
        var starts = new List<DateOnly>();
        var last = PeriodStart(asOfDate, frequency);
        // Always include at least the current period, even if the anchor somehow resolves to
        // later than it (shouldn't happen in practice - a category's anchor is never set in
        // the future relative to "today" - but this keeps the caller from indexing into an
        // empty list rather than relying on that invariant always holding).
        var cursor = PeriodStart(anchorDate, frequency);
        if (cursor > last)
        {
            cursor = last;
        }
        while (cursor <= last)
        {
            starts.Add(cursor);
            cursor = NextPeriodStart(cursor, frequency);
        }
        return starts;
    }

    private async Task<SpendingTrackerResult> GetSummaryAsync(
        ExpenseDbContext context, DateOnly periodStart, DateOnly periodEnd, Frequency periodFrequency, DateOnly asOfDate, CancellationToken cancellationToken)
    {
        var qualifyingCategoryIds = await context.FundingRules
            .Where(r => r.Strategy == FundingStrategies.TrackedBudget)
            .Select(r => r.CategoryId)
            .ToListAsync(cancellationToken);

        var categories = await context.Categories
            .Where(c => c.IsActive && qualifyingCategoryIds.Contains(c.Id))
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        // Every dated budget version for these categories, not just the one active today -
        // carryover needs each historical period's own period-correct budget, same principle
        // as the existing period-correct lookup below.
        var allBudgetPeriods = await context.BudgetPeriods
            .Where(p => qualifyingCategoryIds.Contains(p.CategoryId))
            .ToListAsync(cancellationToken);

        // Period-correct, not "whatever's open today" - matters once a past period becomes
        // browsable (Dashboard week/month navigation), since a since-changed budget would
        // otherwise misreport what was actually budgeted back then.
        var budgetsAtPeriodStart = allBudgetPeriods
            .Where(p => p.EffectiveFrom <= periodStart && (p.EffectiveThrough == null || p.EffectiveThrough >= periodStart))
            .ToDictionary(p => p.CategoryId);

        // Still-unposted, self-reported (screenshot-derived) charges count using the date
        // they were seen/entered instead of a real PostedDate - consistent with how the
        // Forecast page's Amex cycle calculation already treats these (see AmexCycleCalculator).
        var bankTotalsByCategory = await context.BankTransactions
            .Where(t => !t.IsAmazonMerchant && t.CategoryId != null && qualifyingCategoryIds.Contains(t.CategoryId.Value))
            .Where(BankTransactionReconciliation.CountsAsReal)
            .Where(t => (t.PostedDate ?? t.TransactionDate) >= periodStart && (t.PostedDate ?? t.TransactionDate) <= periodEnd)
            .GroupBy(t => t.CategoryId!.Value)
            .Select(g => new { CategoryId = g.Key, Total = g.Sum(t => t.Amount) })
            .ToDictionaryAsync(g => g.CategoryId, g => g.Total, cancellationToken);

        var amazonTotalsByCategory = await context.AmazonOrderItems
            .Where(i => i.CategoryId != null && qualifyingCategoryIds.Contains(i.CategoryId.Value)
                        && i.OrderDate >= periodStart && i.OrderDate <= periodEnd)
            .GroupBy(i => i.CategoryId!.Value)
            .Select(g => new { CategoryId = g.Key, Total = g.Sum(i => i.Price * i.Quantity + i.TaxAllocated - (i.RefundAmount ?? 0m)) })
            .ToDictionaryAsync(g => g.CategoryId, g => g.Total, cancellationToken);

        var summaries = new List<CategorySpendingSummary>();
        foreach (var category in categories)
        {
            var budget = budgetsAtPeriodStart.TryGetValue(category.Id, out var period)
                ? proration.Convert(period.Amount, period.Frequency, periodFrequency)
                : 0m;

            var bankSpend = -bankTotalsByCategory.GetValueOrDefault(category.Id);
            var amazonSpend = amazonTotalsByCategory.GetValueOrDefault(category.Id);

            var summary = new CategorySpendingSummary
            {
                CategoryId = category.Id,
                CategoryName = category.Name,
                Budget = budget,
                Actual = bankSpend + amazonSpend
            };

            var activeBudget = allBudgetPeriods.SingleOrDefault(p => p.CategoryId == category.Id && p.EffectiveThrough == null);
            var nativeFrequency = DetermineNativeFrequency(activeBudget);
            if (nativeFrequency == periodFrequency)
            {
                var (carriedIn, rollingBalance, cap) = await ComputeCarryoverAsync(
                    context, category, periodFrequency, asOfDate, allBudgetPeriods.Where(p => p.CategoryId == category.Id).ToList(), cancellationToken);
                summary.IsCarryoverTracked = true;
                summary.CarriedIn = carriedIn;
                summary.RollingBalance = rollingBalance;
                summary.CarryoverCap = cap;
            }

            summaries.Add(summary);
        }

        var pendingBank = await context.BankTransactions
            .Where(t => !t.IsAmazonMerchant && t.CategoryId == null && t.Amount < 0)
            .Where(BankTransactionReconciliation.CountsAsReal)
            .Where(t => (t.PostedDate ?? t.TransactionDate) >= periodStart && (t.PostedDate ?? t.TransactionDate) <= periodEnd)
            .SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0m;

        var pendingAmazon = await context.AmazonOrderItems
            .Where(i => i.CategoryId == null && i.OrderDate >= periodStart && i.OrderDate <= periodEnd)
            .SumAsync(i => (decimal?)(i.Price * i.Quantity + i.TaxAllocated - (i.RefundAmount ?? 0m)), cancellationToken) ?? 0m;

        return new SpendingTrackerResult
        {
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Categories = summaries,
            PendingAmount = -pendingBank + pendingAmazon
        };
    }

    private async Task<(decimal CarriedIn, decimal RollingBalance, decimal? Cap)> ComputeCarryoverAsync(
        ExpenseDbContext context, Category category, Frequency frequency, DateOnly asOfDate,
        IReadOnlyList<BudgetPeriod> categoryBudgetPeriods, CancellationToken cancellationToken)
    {
        var currentPeriodStart = PeriodStart(asOfDate, frequency);
        var effectiveAnchor = ResolveEffectiveAnchor(category, currentPeriodStart);
        var periodStarts = EnumeratePeriodStarts(effectiveAnchor, asOfDate, frequency);
        var rangeStart = periodStarts[0];
        var rangeEnd = PeriodEnd(currentPeriodStart, frequency);

        // One query per data source across the whole anchor-to-now range, bucketed by period
        // in memory below - avoids one query per historical period.
        var bankRows = await context.BankTransactions
            .Where(t => !t.IsAmazonMerchant && t.CategoryId == category.Id)
            .Where(BankTransactionReconciliation.CountsAsReal)
            .Where(t => (t.PostedDate ?? t.TransactionDate) >= rangeStart && (t.PostedDate ?? t.TransactionDate) <= rangeEnd)
            .Select(t => new { Date = t.PostedDate ?? t.TransactionDate, t.Amount })
            .ToListAsync(cancellationToken);

        var amazonRows = await context.AmazonOrderItems
            .Where(i => i.CategoryId == category.Id && i.OrderDate >= rangeStart && i.OrderDate <= rangeEnd)
            .Select(i => new { i.OrderDate, Total = i.Price * i.Quantity + i.TaxAllocated - (i.RefundAmount ?? 0m) })
            .ToListAsync(cancellationToken);

        var activities = new List<CarryoverCalculator.PeriodActivity>();
        foreach (var start in periodStarts)
        {
            var end = PeriodEnd(start, frequency);
            var budgetPeriod = categoryBudgetPeriods
                .SingleOrDefault(p => p.EffectiveFrom <= start && (p.EffectiveThrough == null || p.EffectiveThrough >= start));
            var budget = budgetPeriod is null ? 0m : proration.Convert(budgetPeriod.Amount, budgetPeriod.Frequency, frequency);

            var bankSpend = -bankRows.Where(r => r.Date >= start && r.Date <= end).Sum(r => r.Amount);
            var amazonSpend = amazonRows.Where(r => r.OrderDate >= start && r.OrderDate <= end).Sum(r => r.Total);

            activities.Add(new CarryoverCalculator.PeriodActivity(budget, bankSpend + amazonSpend));
        }

        var result = CarryoverCalculator.Compute(activities, category.CarryoverCapMultiplier);
        return (result.CarriedIn, result.RollingBalance, result.Cap);
    }
}
