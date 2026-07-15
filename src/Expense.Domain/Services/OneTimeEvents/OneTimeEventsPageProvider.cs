using Expense.Domain.Data;
using Expense.Domain.Entities;
using Expense.Domain.Services.Categories;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.OneTimeEvents;

/// <summary>Thin DI-composition wiring (like ForecastResultProvider) - all real logic lives in OneTimeEventManagementService.</summary>
public class OneTimeEventsPageProvider(IDbContextFactory<ExpenseDbContext> contextFactory, OneTimeEventManagementService events) : IOneTimeEventsPageProvider
{
    public async Task<OneTimeEventsPageData> GetEventsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var rows = await context.OneTimeEvents
            .OrderBy(e => e.Date)
            .Select(e => new OneTimeEventRow
            {
                Id = e.Id,
                Name = e.Name,
                Amount = e.Amount,
                Direction = e.Direction,
                Date = e.Date,
                AccountId = e.AccountId,
                AccountName = e.Account.Name
            })
            .ToListAsync(cancellationToken);

        var accounts = await context.Accounts
            .Where(a => a.IsActive)
            .OrderBy(a => a.Name)
            .Select(a => new AccountOption { Id = a.Id, Name = a.Name })
            .ToListAsync(cancellationToken);

        return new OneTimeEventsPageData { Events = rows, Accounts = accounts };
    }

    public async Task CreateEventAsync(string name, decimal amount, Direction direction, DateOnly date, int accountId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await events.CreateEventAsync(context, name, amount, direction, date, accountId);
    }

    public async Task UpdateEventAsync(int eventId, string name, decimal amount, Direction direction, DateOnly date, int accountId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await events.UpdateEventAsync(context, eventId, name, amount, direction, date, accountId);
    }

    public async Task DeleteEventAsync(int eventId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await events.DeleteEventAsync(context, eventId);
    }
}
