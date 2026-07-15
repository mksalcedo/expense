namespace Expense.Domain.Services.SpendingTracker;

public class SpendingTrackerPageData
{
    public required SpendingTrackerResult Week { get; set; }
    public required SpendingTrackerResult Month { get; set; }
}
