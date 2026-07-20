namespace Expense.Domain.Entities;

/// <summary>
/// A real partial payment made toward one specific forecasted occurrence - e.g. paying
/// $1000 today of a $2000 bill due later, rather than the whole thing at once. Reduces
/// that occurrence's remaining forecasted amount (see ForecastEngine) without excluding
/// it entirely, unlike PaymentConfirmation. The paid amount/date are also recorded as a
/// real OneTimeEvent (see OneTimeEventId) so the cash impact shows up on its own date -
/// removing this row deletes that OneTimeEvent too, undoing both halves together.
/// </summary>
public class PartialPayment
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;
    public DateOnly OriginalDate { get; set; }
    public decimal Amount { get; set; }
    public DateOnly PaidDate { get; set; }
    public int OneTimeEventId { get; set; }
    public OneTimeEvent OneTimeEvent { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}
