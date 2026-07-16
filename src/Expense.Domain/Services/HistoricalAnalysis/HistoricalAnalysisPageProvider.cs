using Expense.Domain.Data;
using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.HistoricalAnalysis;

/// <summary>Thin DI-composition wiring (like ForecastResultProvider) - all real logic lives in HistoricalAnalysisService.</summary>
public class HistoricalAnalysisPageProvider(IDbContextFactory<ExpenseDbContext> contextFactory, HistoricalAnalysisService analysis) : IHistoricalAnalysisPageProvider
{
    public async Task<HistoricalAnalysisPageData> GetHistoricalAnalysisAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var asOfDate = DateOnly.FromDateTime(DateTime.Today);
        var weekStart = asOfDate.AddDays(-(int)asOfDate.DayOfWeek);
        var monthStart = new DateOnly(asOfDate.Year, asOfDate.Month, 1);

        var weeklyReport = await analysis.GetWeeklyReportAsync(context, weekStart, cancellationToken);
        var monthlyReport = await analysis.GetMonthlyReportAsync(context, monthStart, cancellationToken);
        var yearToDate = await analysis.GetYearToDateAsync(context, asOfDate, cancellationToken);
        var fourWeekAverage = await analysis.Get4WeekAverageAsync(context, asOfDate, cancellationToken);
        var thirteenWeekAverage = await analysis.Get13WeekAverageAsync(context, asOfDate, cancellationToken);
        var recurringProducts = await analysis.GetRecurringProductReportAsync(context, cancellationToken);

        var categories = await context.Categories
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        return new HistoricalAnalysisPageData
        {
            WeeklyReport = weeklyReport,
            MonthlyReport = monthlyReport,
            YearToDate = yearToDate,
            FourWeekAverage = fourWeekAverage,
            ThirteenWeekAverage = thirteenWeekAverage,
            RecurringProducts = recurringProducts,
            Categories = categories
        };
    }

    public async Task<List<PeriodSpendingSummary>> GetCategoryTrendAsync(int categoryId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var asOfDate = DateOnly.FromDateTime(DateTime.Today);
        return await analysis.GetCategoryTrendAsync(context, categoryId, Frequency.Weekly, periodCount: 13, asOfDate, cancellationToken);
    }
}
