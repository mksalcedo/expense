using Expense.Domain.Services.Categories;

namespace Expense.Domain.Services.Ingestion.ManualCharges;

/// <summary>Thin abstraction over ManualChargeEntryService so UI components can be tested against a fake result.</summary>
public interface IManualChargesPageProvider
{
    Task<List<AccountOption>> GetActiveSpendingAccountsAsync(CancellationToken cancellationToken = default);

    Task<List<ManualChargeReviewRow>> ReviewScreenshotAsync(
        int accountId, byte[] imageBytes, string mediaType, CancellationToken cancellationToken = default);

    Task<int> AddChargesAsync(int accountId, List<ManualChargeReviewRow> rows, CancellationToken cancellationToken = default);
}
