namespace Expense.Domain.Services.SpendingTracker;

public class SpendingTrackerResult
{
    public required DateOnly PeriodStart { get; set; }
    public required DateOnly PeriodEnd { get; set; }
    public required List<CategorySpendingSummary> Categories { get; set; }

    /// <summary>Uncategorized bank transactions + Amazon items in this period - still visible, not silently dropped.</summary>
    public required decimal PendingAmount { get; set; }
}
