namespace Expense.Domain.Services.HistoricalAnalysis;

/// <summary>Thin abstraction over HistoricalAnalysisService so UI components can be tested against a fake result.</summary>
public interface IHistoricalAnalysisPageProvider
{
    Task<HistoricalAnalysisPageData> GetHistoricalAnalysisAsync(CancellationToken cancellationToken = default);

    /// <summary>13-week trend for one category, fetched on demand when the user picks it.</summary>
    Task<List<PeriodSpendingSummary>> GetCategoryTrendAsync(int categoryId, CancellationToken cancellationToken = default);
}
