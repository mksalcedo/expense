using Expense.Domain.Data;
using Expense.Domain.Services.Categorization;
using Expense.Domain.Services.Ingestion.Amazon;
using Microsoft.EntityFrameworkCore;

namespace Expense.Web.Services;

public record StagedScrape(int ItemId, AmazonOrderScrapePayload Payload);

/// <summary>
/// Holds the most recent scrape detected by the clipboard watcher (see
/// docs/amazon-order-scraper-bookmarklet.md) until the user accepts or cancels it on the
/// Review Queue page - registered Singleton (not Scoped, unlike most services here) since
/// the HTTP endpoint that stages a scrape and the Review Queue page's own circuit that
/// displays it are two entirely separate requests that need to share the same instance.
/// Thin DI-composition wiring, same as ReviewQueueProvider - the real logic (parsing,
/// finding the matching item) lives in AmazonOrderScrapeParser/CategorizationService.
/// </summary>
public interface IStagedScrapeStore
{
    event Action? Staged;
    StagedScrape? Current { get; }
    Task<bool> TryStageAsync(string json, CancellationToken cancellationToken = default);
    void Clear();
}

public class StagedScrapeStore(IDbContextFactory<ExpenseDbContext> contextFactory) : IStagedScrapeStore
{
    // CategorizationService has no dependencies of its own (every method takes its
    // ExpenseDbContext as a plain parameter) - constructed directly here rather than
    // injected, since this store is a Singleton and CategorizationService is registered
    // Scoped (ASP.NET Core's container won't let a Singleton consume a Scoped service via
    // constructor injection).
    private readonly CategorizationService categorization = new();

    public event Action? Staged;
    public StagedScrape? Current { get; private set; }

    public async Task<bool> TryStageAsync(string json, CancellationToken cancellationToken = default)
    {
        var payload = AmazonOrderScrapeParser.TryParse(json);
        if (payload?.OrderId is not { } orderId)
        {
            return false;
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var item = await categorization.FindNeedsReviewItemByOrderIdAsync(context, orderId);
        if (item is null)
        {
            return false;
        }

        Current = new StagedScrape(item.Id, payload);
        Staged?.Invoke();
        return true;
    }

    public void Clear() => Current = null;
}
