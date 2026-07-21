using Expense.Domain.Data;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Ingestion.ManualCharges;

/// <summary>Thin DI-composition wiring (like ConfirmedPaymentsPageProvider) - all real logic lives in ManualChargeMatchingService.</summary>
public class PendingChargesPageProvider(
    IDbContextFactory<ExpenseDbContext> contextFactory, ManualChargeMatchingService matching) : IPendingChargesPageProvider
{
    public async Task<List<PendingChargeRow>> GetOpenChargesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.BankTransactions
            .Include(t => t.Account)
            .Where(t => t.ImportSource == ManualChargeMatchingService.ManualScreenshotImportSource && t.PostedDate == null)
            .OrderBy(t => t.TransactionDate)
            .Select(t => new PendingChargeRow
            {
                Id = t.Id,
                AccountName = t.Account.Name,
                Date = t.TransactionDate,
                Description = t.Description,
                Amount = t.Amount,
                EnteredAt = t.CreatedAt
            })
            .ToListAsync(cancellationToken);
    }

    public async Task DeleteChargeAsync(int transactionId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await matching.DeletePlaceholderAsync(context, transactionId, cancellationToken);
    }
}
