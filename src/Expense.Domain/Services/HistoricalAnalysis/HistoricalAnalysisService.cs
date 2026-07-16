using Expense.Domain.Data;
using Expense.Domain.Entities;
using Expense.Domain.Services.Budgets;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.HistoricalAnalysis;

/// <summary>
/// "Is my current plan realistic?" - computed entirely on demand from bank_transactions/
/// amazon_order_items, never stored. Scoped to Direct and PayInFullAmex categories - both
/// have real BudgetPeriod history to compare against (AccountPayment categories have no
/// BudgetPeriod at all; None-strategy categories have no budget to compare).
///
/// Budget-vs-actual always uses the BudgetPeriod in effect at the period's start date,
/// never today's - the whole point is seeing what was actually budgeted at the time,
/// e.g. a mortgage payment that changed mid-year.
/// </summary>
public class HistoricalAnalysisService(BudgetProrationService proration)
{
    private static readonly string[] QualifyingStrategies = [FundingStrategies.Direct, FundingStrategies.PayInFullAmex];

    public Task<List<PeriodSpendingSummary>> GetWeeklyReportAsync(ExpenseDbContext context, DateOnly weekStart, CancellationToken cancellationToken = default) =>
        GetPeriodReportAsync(context, weekStart, weekStart.AddDays(6), Frequency.Weekly, cancellationToken);

    /// <summary>All-time, not scoped to a period - "Product | Category | Purchases | Average price | Total spent | Last purchased".</summary>
    public async Task<List<RecurringProductSummary>> GetRecurringProductReportAsync(ExpenseDbContext context, CancellationToken cancellationToken = default) =>
        await context.AmazonOrderItems
            .Where(i => i.ProductId != null)
            .GroupBy(i => i.ProductId!.Value)
            .Select(g => new RecurringProductSummary
            {
                ProductId = g.Key,
                ProductPattern = g.First().Product!.ProductPattern,
                CategoryName = g.First().Category!.Name,
                Purchases = g.Count(),
                AveragePrice = g.Average(i => i.Price),
                TotalSpent = g.Sum(i => i.Price * i.Quantity + i.TaxAllocated - (i.RefundAmount ?? 0m)),
                LastPurchased = g.Max(i => i.OrderDate)
            })
            .OrderByDescending(s => s.Purchases)
            .ToListAsync(cancellationToken);

    public Task<List<PeriodSpendingSummary>> GetMonthlyReportAsync(ExpenseDbContext context, DateOnly monthStart, CancellationToken cancellationToken = default)
    {
        var start = new DateOnly(monthStart.Year, monthStart.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        return GetPeriodReportAsync(context, start, end, Frequency.Monthly, cancellationToken);
    }

    public async Task<List<PeriodSpendingSummary>> GetYearToDateAsync(ExpenseDbContext context, DateOnly asOfDate, CancellationToken cancellationToken = default)
    {
        var periodStart = new DateOnly(asOfDate.Year, 1, 1);
        var (categories, actualByCategory) = await GetQualifyingCategoriesAndActualAsync(context, periodStart, asOfDate, cancellationToken);

        return categories.Select(category => new PeriodSpendingSummary
        {
            PeriodStart = periodStart,
            PeriodEnd = asOfDate,
            CategoryId = category.Id,
            CategoryName = category.Name,
            Budget = null,
            Actual = actualByCategory.GetValueOrDefault(category.Id)
        }).ToList();
    }

    public async Task<List<PeriodSpendingSummary>> GetCategoryTrendAsync(
        ExpenseDbContext context, int categoryId, Frequency frequency, int periodCount, DateOnly asOfDate, CancellationToken cancellationToken = default)
    {
        var category = await context.Categories.SingleAsync(c => c.Id == categoryId, cancellationToken);
        var periods = GetPeriodBoundaries(frequency, periodCount, asOfDate);

        var results = new List<PeriodSpendingSummary>();
        foreach (var (start, end) in periods)
        {
            var (_, actualByCategory) = await GetQualifyingCategoriesAndActualAsync(context, start, end, cancellationToken);
            results.Add(new PeriodSpendingSummary
            {
                PeriodStart = start,
                PeriodEnd = end,
                CategoryId = category.Id,
                CategoryName = category.Name,
                Budget = null,
                Actual = actualByCategory.GetValueOrDefault(category.Id)
            });
        }
        return results;
    }

    public Task<List<CategoryAverageSummary>> Get4WeekAverageAsync(ExpenseDbContext context, DateOnly asOfDate, CancellationToken cancellationToken = default) =>
        GetRollingWeeklyAverageAsync(context, 4, asOfDate, cancellationToken);

    public Task<List<CategoryAverageSummary>> Get13WeekAverageAsync(ExpenseDbContext context, DateOnly asOfDate, CancellationToken cancellationToken = default) =>
        GetRollingWeeklyAverageAsync(context, 13, asOfDate, cancellationToken);

    private async Task<List<CategoryAverageSummary>> GetRollingWeeklyAverageAsync(
        ExpenseDbContext context, int weekCount, DateOnly asOfDate, CancellationToken cancellationToken)
    {
        var qualifyingCategoryIds = await context.FundingRules
            .Where(r => QualifyingStrategies.Contains(r.Strategy))
            .Select(r => r.CategoryId)
            .ToListAsync(cancellationToken);

        var categories = await context.Categories
            .Where(c => qualifyingCategoryIds.Contains(c.Id))
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var totalsByCategory = categories.ToDictionary(c => c.Id, _ => 0m);
        foreach (var (start, end) in GetPeriodBoundaries(Frequency.Weekly, weekCount, asOfDate))
        {
            var (_, actualByCategory) = await GetQualifyingCategoriesAndActualAsync(context, start, end, cancellationToken);
            foreach (var category in categories)
            {
                totalsByCategory[category.Id] += actualByCategory.GetValueOrDefault(category.Id);
            }
        }

        var currentBudgets = await context.BudgetPeriods
            .Where(p => p.EffectiveThrough == null && qualifyingCategoryIds.Contains(p.CategoryId))
            .ToDictionaryAsync(p => p.CategoryId, cancellationToken);

        return categories.Select(category =>
        {
            var currentBudget = currentBudgets.TryGetValue(category.Id, out var period)
                ? proration.Convert(period.Amount, period.Frequency, Frequency.Weekly)
                : (decimal?)null;

            return new CategoryAverageSummary
            {
                CategoryId = category.Id,
                CategoryName = category.Name,
                AverageActual = totalsByCategory[category.Id] / weekCount,
                CurrentBudget = currentBudget
            };
        }).ToList();
    }

    private static List<(DateOnly Start, DateOnly End)> GetPeriodBoundaries(Frequency frequency, int periodCount, DateOnly asOfDate)
    {
        var boundaries = new List<(DateOnly, DateOnly)>();

        if (frequency == Frequency.Weekly)
        {
            var currentWeekStart = asOfDate.AddDays(-(int)asOfDate.DayOfWeek);
            for (var i = periodCount - 1; i >= 0; i--)
            {
                var start = currentWeekStart.AddDays(-7 * i);
                boundaries.Add((start, start.AddDays(6)));
            }
        }
        else if (frequency == Frequency.Monthly)
        {
            var currentMonthStart = new DateOnly(asOfDate.Year, asOfDate.Month, 1);
            for (var i = periodCount - 1; i >= 0; i--)
            {
                var start = currentMonthStart.AddMonths(-i);
                boundaries.Add((start, start.AddMonths(1).AddDays(-1)));
            }
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(frequency), frequency, "Only Weekly and Monthly are supported for trend periods.");
        }

        return boundaries;
    }

    private async Task<List<PeriodSpendingSummary>> GetPeriodReportAsync(
        ExpenseDbContext context, DateOnly periodStart, DateOnly periodEnd, Frequency periodFrequency, CancellationToken cancellationToken)
    {
        var (categories, actualByCategory) = await GetQualifyingCategoriesAndActualAsync(context, periodStart, periodEnd, cancellationToken);
        var qualifyingCategoryIds = categories.Select(c => c.Id).ToList();

        var budgetsAtPeriodStart = await context.BudgetPeriods
            .Where(p => qualifyingCategoryIds.Contains(p.CategoryId)
                        && p.EffectiveFrom <= periodStart && (p.EffectiveThrough == null || p.EffectiveThrough >= periodStart))
            .ToDictionaryAsync(p => p.CategoryId, cancellationToken);

        return categories.Select(category =>
        {
            var budget = budgetsAtPeriodStart.TryGetValue(category.Id, out var period)
                ? proration.Convert(period.Amount, period.Frequency, periodFrequency)
                : (decimal?)null;

            return new PeriodSpendingSummary
            {
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                CategoryId = category.Id,
                CategoryName = category.Name,
                Budget = budget,
                Actual = actualByCategory.GetValueOrDefault(category.Id)
            };
        }).ToList();
    }

    private async Task<(List<Category> Categories, Dictionary<int, decimal> ActualByCategory)> GetQualifyingCategoriesAndActualAsync(
        ExpenseDbContext context, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken)
    {
        var qualifyingCategoryIds = await context.FundingRules
            .Where(r => QualifyingStrategies.Contains(r.Strategy))
            .Select(r => r.CategoryId)
            .ToListAsync(cancellationToken);

        var categories = await context.Categories
            .Where(c => qualifyingCategoryIds.Contains(c.Id))
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var bankTotalsByCategory = await context.BankTransactions
            .Where(t => !t.IsAmazonMerchant && t.CategoryId != null && qualifyingCategoryIds.Contains(t.CategoryId.Value)
                        && t.PostedDate != null && t.PostedDate >= periodStart && t.PostedDate <= periodEnd)
            .GroupBy(t => t.CategoryId!.Value)
            .Select(g => new { CategoryId = g.Key, Total = g.Sum(t => t.Amount) })
            .ToDictionaryAsync(g => g.CategoryId, g => g.Total, cancellationToken);

        var amazonTotalsByCategory = await context.AmazonOrderItems
            .Where(i => i.CategoryId != null && qualifyingCategoryIds.Contains(i.CategoryId.Value)
                        && i.OrderDate >= periodStart && i.OrderDate <= periodEnd)
            .GroupBy(i => i.CategoryId!.Value)
            .Select(g => new { CategoryId = g.Key, Total = g.Sum(i => i.Price * i.Quantity + i.TaxAllocated - (i.RefundAmount ?? 0m)) })
            .ToDictionaryAsync(g => g.CategoryId, g => g.Total, cancellationToken);

        var actualByCategory = categories.ToDictionary(
            c => c.Id,
            c => -bankTotalsByCategory.GetValueOrDefault(c.Id) + amazonTotalsByCategory.GetValueOrDefault(c.Id));

        return (categories, actualByCategory);
    }
}
