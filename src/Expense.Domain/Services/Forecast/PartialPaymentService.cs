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
    /// <param name="lineDescription">The original forecast line's own description (e.g.
    /// "Piano") - used to name the synthetic OneTimeEvent this creates. Deliberately not
    /// re-derived from the account, which only reads correctly for a debt/Amex payment
    /// (where the line's own description already IS "{account.Name} Payment" anyway, so this
    /// produces the identical text there) - a Direct-funded category like Piano is paid into
    /// the plain checking account itself, and naming its synthetic event after that account
    /// produced the nonsensical "Wells Fargo Checking Payment (partial)" (found live
    /// 2026-09-05, from a real $95 Piano partial payment).</param>
    public async Task<PartialPayment> CreateAsync(
        ExpenseDbContext context, int accountId, DateOnly originalDate, DateOnly paidDate, decimal amount, Direction direction,
        string lineDescription, CancellationToken cancellationToken = default)
    {
        var oneTimeEvent = new OneTimeEvent
        {
            Name = $"{lineDescription} (partial)",
            Amount = amount,
            Direction = direction,
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
