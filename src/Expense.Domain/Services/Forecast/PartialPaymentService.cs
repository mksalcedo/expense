using Expense.Domain.Data;
using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Forecast;

/// <summary>
/// CRUD for PartialPayment - records a real partial payment toward one forecasted
/// occurrence as both a PartialPayment row (reduces the remaining forecasted amount, see
/// ForecastEngine) and a real OneTimeEvent (the actual cash leaving on the date paid),
/// created together so the user never has to build both by hand. Removing one removes
/// both, atomically - there's no meaningful state where only half exists.
/// </summary>
public class PartialPaymentService
{
    public async Task<PartialPayment> CreateAsync(
        ExpenseDbContext context, int accountId, DateOnly originalDate, DateOnly paidDate, decimal amount, CancellationToken cancellationToken = default)
    {
        var account = await context.Accounts.SingleAsync(a => a.Id == accountId, cancellationToken);

        var oneTimeEvent = new OneTimeEvent
        {
            Name = $"{account.Name} Payment (partial)",
            Amount = amount,
            Direction = Direction.Expense,
            Date = paidDate,
            AccountId = accountId
        };
        context.OneTimeEvents.Add(oneTimeEvent);
        await context.SaveChangesAsync(cancellationToken);

        var partialPayment = new PartialPayment
        {
            AccountId = accountId,
            OriginalDate = originalDate,
            Amount = amount,
            PaidDate = paidDate,
            OneTimeEventId = oneTimeEvent.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.PartialPayments.Add(partialPayment);
        await context.SaveChangesAsync(cancellationToken);
        return partialPayment;
    }

    public async Task RemoveAsync(ExpenseDbContext context, int partialPaymentId, CancellationToken cancellationToken = default)
    {
        var partialPayment = await context.PartialPayments.SingleAsync(p => p.Id == partialPaymentId, cancellationToken);
        var oneTimeEvent = await context.OneTimeEvents.SingleAsync(e => e.Id == partialPayment.OneTimeEventId, cancellationToken);

        context.PartialPayments.Remove(partialPayment);
        context.OneTimeEvents.Remove(oneTimeEvent);
        await context.SaveChangesAsync(cancellationToken);
    }
}
