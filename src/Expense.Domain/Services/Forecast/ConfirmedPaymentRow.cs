using Expense.Domain.Entities;

namespace Expense.Domain.Services.Forecast;

/// <summary>One row for the Confirmed Payments page - the full, unbounded history of every
/// manually confirmed/overridden occurrence, unlike the Forecast page's ledger which only
/// shows recent ones (see ForecastEngine.ExcludedPaymentVisibilityDays).</summary>
public class ConfirmedPaymentRow
{
    public required int ConfirmationId { get; set; }
    public required DateOnly Date { get; set; }
    public required DateOnly OriginalDate { get; set; }
    public required int AccountId { get; set; }
    public required string AccountName { get; set; }
    public required decimal Amount { get; set; }
    public required ConfirmationReason Reason { get; set; }
    public required DateTimeOffset ConfirmedAt { get; set; }
}
