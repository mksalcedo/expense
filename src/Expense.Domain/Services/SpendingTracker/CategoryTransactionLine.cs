namespace Expense.Domain.Services.SpendingTracker;

/// <summary>
/// One real bank transaction or Amazon item contributing to a CategorySpendingSummary's
/// Actual figure - the drill-down list shown when a category name is clicked on the
/// Spending Tracker. Amount is always a positive-spend/negative-refund figure, the same
/// sign convention SpendingTrackerService already uses for Actual itself.
/// </summary>
public class CategoryTransactionLine
{
    public required DateOnly Date { get; set; }
    public required string Description { get; set; }
    public required decimal Amount { get; set; }
}
