using Expense.Domain.Data;
using Expense.Domain.Entities;
using Expense.Domain.Services.Categories;
using Expense.Domain.Services.Categorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Expense.Domain.Services.Ingestion.ManualCharges;

/// <summary>
/// Thin DI-composition wiring (like SyncStatusProvider) - builds the Anthropic client from
/// configuration at call time rather than via generic DI, same reason SyncStatusProvider
/// builds GmailServiceFactory/SimpleFinClient this way instead. HttpClient itself is a normal
/// typed-client injection (see Program.cs), same as SimpleFinSyncService.
/// </summary>
public class ManualChargesPageProvider(
    IDbContextFactory<ExpenseDbContext> contextFactory, ManualChargeMatchingService matching,
    CategorizationService categorization, HttpClient httpClient, IConfiguration configuration) : IManualChargesPageProvider
{
    public async Task<List<AccountOption>> GetActiveSpendingAccountsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Accounts
            .Where(a => a.Type == AccountType.ActiveSpending && a.IsActive)
            .OrderBy(a => a.Name)
            .Select(a => new AccountOption { Id = a.Id, Name = a.Name })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<ManualChargeReviewRow>> ReviewScreenshotAsync(
        int accountId, byte[] imageBytes, string mediaType, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entryService = CreateEntryService();
        return await entryService.ReviewScreenshotAsync(
            context, accountId, imageBytes, mediaType, DateOnly.FromDateTime(DateTime.Today), cancellationToken);
    }

    public async Task<int> AddChargesAsync(int accountId, List<ManualChargeReviewRow> rows, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entryService = CreateEntryService();
        return await entryService.AddChargesAsync(context, accountId, rows, cancellationToken);
    }

    private ManualChargeEntryService CreateEntryService()
    {
        var apiKey = configuration["Anthropic:ApiKey"]
            ?? throw new InvalidOperationException("Anthropic:ApiKey not set. Run: dotnet user-secrets set \"Anthropic:ApiKey\" \"...\" --project src/Expense.Web");

        var visionClient = new AnthropicVisionClient(httpClient, apiKey);
        var parsingService = new AmexScreenshotParsingService(visionClient);
        return new ManualChargeEntryService(parsingService, matching, categorization);
    }
}
