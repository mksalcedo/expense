namespace Expense.Domain.Entities;

/// <summary>
/// A user-initiated, temporary override moving one specific occurrence of an account's
/// forecasted payment to a later date - e.g. deliberately pushing an Amex payment two
/// days so a paycheck lands first. Never touches the account's real recurring schedule;
/// removing this row reverts that one occurrence back to its normally computed date.
/// </summary>
public class PaymentDeferral
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;
    public DateOnly OriginalDate { get; set; }
    public DateOnly DeferredToDate { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
