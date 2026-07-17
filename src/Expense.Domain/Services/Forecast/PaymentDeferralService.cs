using Expense.Domain.Data;
using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Forecast;

/// <summary>
/// CRUD for PaymentDeferral - lets a user push one specific occurrence of a forecasted
/// payment to a later date without touching the account's real recurring schedule.
/// </summary>
public class PaymentDeferralService
{
    public async Task<PaymentDeferral> CreateAsync(
        ExpenseDbContext context, int accountId, DateOnly originalDate, DateOnly deferredToDate, string? note)
    {
        var deferral = new PaymentDeferral
        {
            AccountId = accountId,
            OriginalDate = originalDate,
            DeferredToDate = deferredToDate,
            Note = note,
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.PaymentDeferrals.Add(deferral);
        await context.SaveChangesAsync();
        return deferral;
    }

    public async Task RemoveAsync(ExpenseDbContext context, int deferralId)
    {
        var deferral = await context.PaymentDeferrals.SingleAsync(d => d.Id == deferralId);
        context.PaymentDeferrals.Remove(deferral);
        await context.SaveChangesAsync();
    }
}
