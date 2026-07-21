namespace Expense.Domain.Services.Ingestion.ManualCharges;

/// <summary>Thin abstraction over the still-open placeholder list so the UI can be tested against a fake result.</summary>
public interface IPendingChargesPageProvider
{
    Task<List<PendingChargeRow>> GetOpenChargesAsync(CancellationToken cancellationToken = default);

    Task DeleteChargeAsync(int transactionId, CancellationToken cancellationToken = default);
}
