namespace Expense.Domain.Entities;

/// <summary>
/// Income (paychecks, SS, pension) and genuinely fixed bills (mortgage, internet,
/// power, insurance) only - deliberately does NOT cover the Variable Spending Budget
/// line (computed from BudgetPeriod at generation time) or any debt payment including
/// Amex (computed from Account.MinPayment/ExtraPayment plus actual cycle charges).
/// </summary>
public class RecurringRule
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public Direction Direction { get; set; }
    public decimal Amount { get; set; }
    public Frequency Frequency { get; set; }

    /// <summary>
    /// A reference date the recurrence is computed from - e.g. the day-of-month for
    /// Monthly/Quarterly/Annual, or the actual date to count Weekly/Biweekly intervals from.
    /// </summary>
    public DateOnly Anchor { get; set; }

    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public bool Active { get; set; } = true;
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
}
