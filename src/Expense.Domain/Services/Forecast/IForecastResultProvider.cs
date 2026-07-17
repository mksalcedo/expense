namespace Expense.Domain.Services.Forecast;

/// <summary>Thin abstraction over ForecastEngine so UI components can be tested against a fake result.</summary>
public interface IForecastResultProvider
{
    Task<ForecastResult> GetForecastAsync(CancellationToken cancellationToken = default);

    Task DeferPaymentAsync(int accountId, DateOnly originalDate, DateOnly deferredToDate, string? note, CancellationToken cancellationToken = default);

    Task RemoveDeferralAsync(int deferralId, CancellationToken cancellationToken = default);
}
