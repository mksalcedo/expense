using Expense.Domain.Data;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Forecast;

/// <summary>Thin DI-composition wiring (like ForecastResultProvider) - all real logic lives in PaymentConfirmationService.</summary>
public class ConfirmedPaymentsPageProvider(
    IDbContextFactory<ExpenseDbContext> contextFactory, PaymentConfirmationService confirmations) : IConfirmedPaymentsPageProvider
{
    public async Task<List<ConfirmedPaymentRow>> GetConfirmedPaymentsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.PaymentConfirmations
            .Include(c => c.Account)
            .OrderByDescending(c => c.EffectiveDate)
            .Select(c => new ConfirmedPaymentRow
            {
                ConfirmationId = c.Id,
                Date = c.EffectiveDate,
                OriginalDate = c.OriginalDate,
                AccountId = c.AccountId,
                AccountName = c.Account.Name,
                Amount = c.Amount,
                Reason = c.Reason,
                ConfirmedAt = c.CreatedAt
            })
            .ToListAsync(cancellationToken);
    }

    public async Task RemoveConfirmationAsync(int confirmationId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await confirmations.RemoveAsync(context, confirmationId);
    }
}
