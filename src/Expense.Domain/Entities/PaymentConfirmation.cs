namespace Expense.Domain.Entities;

/// <summary>
/// A user-confirmed "this already happened" override for one specific occurrence of an
/// account's forecasted payment - excludes it from the forecast the same way a matching
/// real transaction would (see ForecastEngine), for cases the automatic CategoryId-based
/// reconciliation can't cover (e.g. a bank's payment text doesn't distinguish between two
/// cards on the same account type). Removing this row makes the occurrence reappear.
/// </summary>
public class PaymentConfirmation
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;
    public DateOnly OriginalDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
