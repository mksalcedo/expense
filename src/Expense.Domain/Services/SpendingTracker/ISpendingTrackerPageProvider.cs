namespace Expense.Domain.Services.SpendingTracker;

/// <summary>Thin abstraction over SpendingTrackerService so UI components can be tested against a fake result.</summary>
public interface ISpendingTrackerPageProvider
{
    Task<SpendingTrackerPageData> GetSpendingTrackerAsync(CancellationToken cancellationToken = default);
}
