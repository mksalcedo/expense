namespace Expense.Domain.Entities;

/// <summary>
/// A user-known correction to one specific forecasted occurrence's amount - e.g. a real
/// $70.31 bill replacing a $76.68 recurring estimate, known before it's even due - without
/// treating the occurrence as resolved/paid (see PaymentConfirmation for that). The row
/// stays live in the forecast and still needs its own eventual Defer/Confirm/Override/
/// Partial action once it actually happens. Removing this row reverts the occurrence back
/// to its normally computed amount.
/// </summary>
public class PaymentAmountAdjustment
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;

    /// <summary>Same reasoning as PaymentConfirmation.CategoryId - more than one line can share an account and date.</summary>
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }

    public DateOnly OriginalDate { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
