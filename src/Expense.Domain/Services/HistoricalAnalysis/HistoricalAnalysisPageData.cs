using Expense.Domain.Entities;

namespace Expense.Domain.Services.HistoricalAnalysis;

public class HistoricalAnalysisPageData
{
    public required List<PeriodSpendingSummary> WeeklyReport { get; set; }
    public required List<PeriodSpendingSummary> MonthlyReport { get; set; }
    public required List<PeriodSpendingSummary> YearToDate { get; set; }
    public required List<CategoryAverageSummary> FourWeekAverage { get; set; }
    public required List<CategoryAverageSummary> ThirteenWeekAverage { get; set; }
    public required List<RecurringProductSummary> RecurringProducts { get; set; }
    public required List<Category> Categories { get; set; }
}
