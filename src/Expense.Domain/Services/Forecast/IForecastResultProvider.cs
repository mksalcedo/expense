using Expense.Domain.Entities;

namespace Expense.Domain.Services.Forecast;

/// <summary>Thin abstraction over ForecastEngine so UI components can be tested against a fake result.</summary>
public interface IForecastResultProvider
{
    Task<ForecastResult> GetForecastAsync(CancellationToken cancellationToken = default);

    Task DeferPaymentAsync(int accountId, DateOnly originalDate, DateOnly deferredToDate, string? note, CancellationToken cancellationToken = default);

    Task RemoveDeferralAsync(int deferralId, CancellationToken cancellationToken = default);

    Task ConfirmPaymentAsync(int accountId, int? categoryId, DateOnly originalDate, DateOnly effectiveDate, decimal amount, CancellationToken cancellationToken = default);

    Task OverridePaymentAsync(int accountId, int? categoryId, DateOnly originalDate, DateOnly effectiveDate, decimal amount, CancellationToken cancellationToken = default);

    Task RemoveConfirmationAsync(int confirmationId, CancellationToken cancellationToken = default);

    Task PayPartialAmountAsync(int accountId, DateOnly originalDate, DateOnly paidDate, decimal amount, Direction direction, string lineDescription, CancellationToken cancellationToken = default);

    Task RemovePartialPaymentAsync(int partialPaymentId, CancellationToken cancellationToken = default);

    Task AdjustAmountAsync(int accountId, int? categoryId, DateOnly originalDate, decimal amount, CancellationToken cancellationToken = default);

    Task RemoveAmountAdjustmentAsync(int adjustmentId, CancellationToken cancellationToken = default);
}
