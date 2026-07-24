namespace Expense.Domain.Services.Forecast;

/// <summary>Thin abstraction over ForecastAccuracyService so UI components can be tested against a fake result.</summary>
public interface IForecastAccuracyPageProvider
{
    Task<List<AccuracyComparison>> GetRecentAccuracyAsync(CancellationToken cancellationToken = default);
}
