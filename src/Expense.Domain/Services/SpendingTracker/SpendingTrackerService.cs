using Expense.Domain.Data;
using Expense.Domain.Entities;
using Expense.Domain.Services.Budgets;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.SpendingTracker;

/// <summary>
/// Current-week/current-month budget vs. actual vs. remaining, scoped to PayInFullAmex
/// categories (the only ones with real day-to-day variable spending to track - Direct
/// categories are fixed bills/income, AccountPayment categories are paydown-only debt).
/// No carryover in either direction: Remaining = this period's prorated budget minus
/// this period's actual spend, full stop - see design-summary.md.
/// </summary>
public class SpendingTrackerService(BudgetProrationService proration)
{
    public Task<SpendingTrackerResult> GetCurrentWeekAsync(ExpenseDbContext context, DateOnly asOfDate, CancellationToken cancellationToken = default)
    {
        var daysSinceSunday = (int)asOfDate.DayOfWeek;
        var start = asOfDate.AddDays(-daysSinceSunday);
        var end = start.AddDays(6);
        return GetSummaryAsync(context, start, end, Frequency.Weekly, cancellationToken);
    }

    public Task<SpendingTrackerResult> GetCurrentMonthAsync(ExpenseDbContext context, DateOnly asOfDate, CancellationToken cancellationToken = default)
    {
        var start = new DateOnly(asOfDate.Year, asOfDate.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        return GetSummaryAsync(context, start, end, Frequency.Monthly, cancellationToken);
    }

    private async Task<SpendingTrackerResult> GetSummaryAsync(
        ExpenseDbContext context, DateOnly periodStart, DateOnly periodEnd, Frequency periodFrequency, CancellationToken cancellationToken)
    {
        var qualifyingCategoryIds = await context.FundingRules
            .Where(r => r.Strategy == FundingStrategies.PayInFullAmex)
            .Select(r => r.CategoryId)
            .ToListAsync(cancellationToken);

        var categories = await context.Categories
            .Where(c => c.IsActive && qualifyingCategoryIds.Contains(c.Id))
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var currentBudgets = await context.BudgetPeriods
            .Where(p => p.EffectiveThrough == null && qualifyingCategoryIds.Contains(p.CategoryId))
            .ToDictionaryAsync(p => p.CategoryId, cancellationToken);

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

        var summaries = categories.Select(category =>
        {
            var budget = currentBudgets.TryGetValue(category.Id, out var period)
                ? proration.Convert(period.Amount, period.Frequency, periodFrequency)
                : 0m;

            var bankSpend = -bankTotalsByCategory.GetValueOrDefault(category.Id);
            var amazonSpend = amazonTotalsByCategory.GetValueOrDefault(category.Id);

            return new CategorySpendingSummary
            {
                CategoryId = category.Id,
                CategoryName = category.Name,
                Budget = budget,
                Actual = bankSpend + amazonSpend
            };
        }).ToList();

        var pendingBank = await context.BankTransactions
            .Where(t => !t.IsAmazonMerchant && t.CategoryId == null
                        && t.PostedDate != null && t.PostedDate >= periodStart && t.PostedDate <= periodEnd)
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
}
