using Expense.Domain.Data;
using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.OneTimeEvents;

/// <summary>
/// A single dated forecast event (an HVAC repair, a property tax bill) - no recurrence,
/// no history to preserve, so removal is a real delete, not a soft-deactivate.
/// </summary>
public class OneTimeEventManagementService
{
    public async Task<OneTimeEvent> CreateEventAsync(
        ExpenseDbContext context, string name, decimal amount, Direction direction, DateOnly date, int accountId)
    {
        var evt = new OneTimeEvent { Name = name, Amount = amount, Direction = direction, Date = date, AccountId = accountId };
        context.OneTimeEvents.Add(evt);
        await context.SaveChangesAsync();
        return evt;
    }

    public async Task UpdateEventAsync(
        ExpenseDbContext context, int eventId, string name, decimal amount, Direction direction, DateOnly date, int accountId)
    {
        var evt = await context.OneTimeEvents.SingleAsync(e => e.Id == eventId);
        evt.Name = name;
        evt.Amount = amount;
        evt.Direction = direction;
        evt.Date = date;
        evt.AccountId = accountId;
        await context.SaveChangesAsync();
    }

    public async Task DeleteEventAsync(ExpenseDbContext context, int eventId)
    {
        var evt = await context.OneTimeEvents.SingleAsync(e => e.Id == eventId);
        context.OneTimeEvents.Remove(evt);
        await context.SaveChangesAsync();
    }
}
