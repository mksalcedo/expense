namespace Expense.Domain.Services.SpendingTracker;

public class CategorySpendingSummary
{
    public required int CategoryId { get; set; }
    public required string CategoryName { get; set; }
    public required decimal Budget { get; set; }
    public required decimal Actual { get; set; }
    public decimal Remaining => Budget - Actual;
}
