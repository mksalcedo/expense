using Expense.Domain.Data;
using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Forecast;

/// <summary>
/// CRUD for PaymentConfirmation - lets a user manually exclude one specific occurrence of
/// a forecasted payment from the forecast, for cases the automatic reconciliation in
/// ForecastEngine can't cover. Reason (see ConfirmationReason) is required and not
/// defaulted - callers must be explicit about which of the two meanings applies.
/// </summary>
public class PaymentConfirmationService
{
    public async Task<PaymentConfirmation> CreateAsync(
        ExpenseDbContext context, int accountId, DateOnly originalDate, DateOnly effectiveDate, decimal amount, ConfirmationReason reason)
    {
        var confirmation = new PaymentConfirmation
        {
            AccountId = accountId,
            OriginalDate = originalDate,
            EffectiveDate = effectiveDate,
            Amount = amount,
            Reason = reason,
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.PaymentConfirmations.Add(confirmation);
        await context.SaveChangesAsync();
        return confirmation;
    }

    public async Task RemoveAsync(ExpenseDbContext context, int confirmationId)
    {
        var confirmation = await context.PaymentConfirmations.SingleAsync(c => c.Id == confirmationId);
        context.PaymentConfirmations.Remove(confirmation);
        await context.SaveChangesAsync();
    }
}
