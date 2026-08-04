namespace Expense.Domain.Services.SpendingTracker;

/// <summary>Thin abstraction over SpendingTrackerService so UI components can be tested against a fake result.</summary>
public interface ISpendingTrackerPageProvider
{
    Task<SpendingTrackerPageData> GetSpendingTrackerAsync(CancellationToken cancellationToken = default);

    /// <summary>The Sunday-start week containing referenceDate - not necessarily this week, see Dashboard's week navigation.</summary>
    Task<SpendingTrackerResult> GetWeekAsync(DateOnly referenceDate, CancellationToken cancellationToken = default);

    /// <summary>The calendar month containing referenceDate - not necessarily this month, see Dashboard's month navigation.</summary>
    Task<SpendingTrackerResult> GetMonthAsync(DateOnly referenceDate, CancellationToken cancellationToken = default);
}
