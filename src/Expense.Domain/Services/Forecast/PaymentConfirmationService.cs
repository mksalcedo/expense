using Expense.Domain.Data;
using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Forecast;

/// <summary>
/// CRUD for PaymentConfirmation - lets a user manually mark one specific occurrence of a
/// forecasted payment as "this already happened", for cases the automatic reconciliation
/// in ForecastEngine can't cover.
/// </summary>
public class PaymentConfirmationService
{
    public async Task<PaymentConfirmation> CreateAsync(ExpenseDbContext context, int accountId, DateOnly originalDate)
    {
        var confirmation = new PaymentConfirmation
        {
            AccountId = accountId,
            OriginalDate = originalDate,
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
