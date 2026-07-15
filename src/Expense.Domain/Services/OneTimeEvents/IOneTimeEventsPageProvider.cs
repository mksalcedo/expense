using Expense.Domain.Entities;

namespace Expense.Domain.Services.OneTimeEvents;

/// <summary>Thin abstraction over OneTimeEventManagementService so UI components can be tested against a fake result.</summary>
public interface IOneTimeEventsPageProvider
{
    Task<OneTimeEventsPageData> GetEventsAsync(CancellationToken cancellationToken = default);

    Task CreateEventAsync(string name, decimal amount, Direction direction, DateOnly date, int accountId, CancellationToken cancellationToken = default);

    Task UpdateEventAsync(int eventId, string name, decimal amount, Direction direction, DateOnly date, int accountId, CancellationToken cancellationToken = default);

    Task DeleteEventAsync(int eventId, CancellationToken cancellationToken = default);
}
