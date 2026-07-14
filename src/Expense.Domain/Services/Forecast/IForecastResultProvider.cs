namespace Expense.Domain.Services.Forecast;

/// <summary>Thin abstraction over ForecastEngine so UI components can be tested against a fake result.</summary>
public interface IForecastResultProvider
{
    Task<ForecastResult> GetForecastAsync(CancellationToken cancellationToken = default);
}
