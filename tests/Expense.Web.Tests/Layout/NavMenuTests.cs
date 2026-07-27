using Bunit;
using Expense.Domain.Entities;
using Expense.Domain.Services.Categorization;
using Expense.Domain.Services.Dashboard;
using Expense.Domain.Services.Ingestion.Amazon;
using Expense.Web.Components.Layout;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Web.Tests.Layout;

public class NavMenuTests : BunitContext
{
    private class FakeReviewQueueProvider(ReviewQueueData data) : IReviewQueueProvider
    {
        public ReviewQueueData Data { get; set; } = data;
        public Task<ReviewQueueData> GetReviewQueueAsync(CancellationToken cancellationToken = default) => Task.FromResult(Data);
        public Task<int> CategorizeTransactionAsync(int transactionId, int categoryId, string? merchantPatternToCreate, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> CategorizeAmazonItemAsync(int itemId, int categoryId, string? productPatternToCreate, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<ReapplyRulesResult> ReapplyRulesAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ReapplyRulesResult());
        public Task<int> BulkCategorizeTransactionsAsync(IReadOnlyList<int> transactionIds, int categoryId, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> BulkCategorizeAmazonItemsAsync(IReadOnlyList<int> itemIds, int categoryId, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task DismissTransactionsAsync(IReadOnlyList<int> transactionIds, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DismissAmazonItemsAsync(IReadOnlyList<int> itemIds, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateAmazonItemDetailsAsync(int itemId, string itemTitle, decimal price, int quantity, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AddManualAmazonItemAsync(string orderId, DateOnly orderDate, string itemTitle, decimal price, int quantity, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    // Only NavMenu.razor's narrow "did the last sync fail" read is exercised here - the
    // full sync UI (buttons, modal, history, issues) lives on Sync Now and is tested there.
    private class FakeSyncStatusProvider(ImportRun? lastSimpleFinRun = null, ImportRun? lastAmazonRun = null) : ISyncStatusProvider
    {
        public Task<ImportRun?> GetLastSimpleFinRunAsync(CancellationToken cancellationToken = default) => Task.FromResult(lastSimpleFinRun);
        public Task<ImportRun?> GetLastAmazonGmailRunAsync(CancellationToken cancellationToken = default) => Task.FromResult(lastAmazonRun);
        public Task<ImportRun> RunSimpleFinSyncAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ImportRun { Source = ImportSource.SimpleFin, RanAt = DateTimeOffset.UtcNow, Success = true });
        public Task<ImportRun> RunAmazonGmailSyncAsync(Action<SyncProgressLine>? onProgress = null, CancellationToken cancellationToken = default) => Task.FromResult(new ImportRun { Source = ImportSource.AmazonGmail, RanAt = DateTimeOffset.UtcNow, Success = true });
        public Task<PlaidImportResult> RunPlaidImportAsync(DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default) => Task.FromResult(new PlaidImportResult(true, "ok"));
        public Task<RecentRunsPage> GetRecentRunsAsync(ImportSource source, int page, int pageSize, CancellationToken cancellationToken = default) => Task.FromResult(new RecentRunsPage { Runs = [], TotalCount = 0 });
        public Task<List<SyncProgressLine>> GetRunProgressLogAsync(int importRunId, CancellationToken cancellationToken = default) => Task.FromResult(new List<SyncProgressLine>());
        public Task<List<SyncIssue>> GetActiveSyncIssuesAsync(CancellationToken cancellationToken = default) => Task.FromResult(new List<SyncIssue>());
        public Task ResolveSyncIssueAsync(int syncIssueId, string orderId, string itemTitle, decimal price, int quantity, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task IgnoreSyncIssueAsync(int syncIssueId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static ReviewQueueData EmptyQueue() => new() { TransactionGroups = [], AmazonItemGroups = [], Categories = [] };

    private FakeReviewQueueProvider RegisterFakes(ReviewQueueData? data = null, ImportRun? lastSimpleFinRun = null, ImportRun? lastAmazonRun = null)
    {
        var provider = new FakeReviewQueueProvider(data ?? EmptyQueue());
        Services.AddSingleton<IReviewQueueProvider>(provider);
        Services.AddSingleton<ISyncStatusProvider>(new FakeSyncStatusProvider(lastSimpleFinRun, lastAmazonRun));
        return provider;
    }

    [Fact]
    public void ClickingRefresh_ReloadsThePage()
    {
        RegisterFakes();
        var handler = JSInterop.SetupVoid("location.reload");

        var cut = Render<NavMenu>();
        cut.Find("#nav-refresh-btn").Click();

        handler.VerifyInvoke("location.reload");
    }

    [Fact]
    public void ReviewQueueLink_HasNoCountSuffix_WhenNothingIsPending()
    {
        RegisterFakes();

        var cut = Render<NavMenu>();

        var link = cut.Find("#nav-review-queue-link");
        Assert.Equal("Review Queue", link.TextContent.Trim());
    }

    [Fact]
    public void ReviewQueueLink_ShowsSingularCount_ForExactlyOnePendingItem()
    {
        RegisterFakes(new ReviewQueueData
        {
            TransactionGroups = [new PendingTransactionGroup { SuggestedPattern = "X", SampleDescription = "X", SampleDate = new DateOnly(2026, 7, 1), TransactionIds = [1], TotalAmount = -10m, AccountName = "Amex" }],
            AmazonItemGroups = [],
            Categories = []
        });

        var cut = Render<NavMenu>();

        Assert.Equal("Review Queue (1 item needs review)", cut.Find("#nav-review-queue-link").TextContent.Trim());
    }

    [Fact]
    public void ReviewQueueLink_ShowsPluralCount_ForMultiplePendingItems()
    {
        RegisterFakes(new ReviewQueueData
        {
            TransactionGroups =
            [
                new PendingTransactionGroup { SuggestedPattern = "X", SampleDescription = "X", SampleDate = new DateOnly(2026, 7, 1), TransactionIds = [1], TotalAmount = -10m, AccountName = "Amex" },
                new PendingTransactionGroup { SuggestedPattern = "Y", SampleDescription = "Y", SampleDate = new DateOnly(2026, 7, 2), TransactionIds = [2], TotalAmount = -20m, AccountName = "Amex" }
            ],
            AmazonItemGroups = [],
            Categories = []
        });

        var cut = Render<NavMenu>();

        Assert.Equal("Review Queue (2 items need review)", cut.Find("#nav-review-queue-link").TextContent.Trim());
    }

    [Fact]
    public void ReviewQueueLink_CountsBothTransactionAndAmazonItemGroups()
    {
        RegisterFakes(new ReviewQueueData
        {
            TransactionGroups = [new PendingTransactionGroup { SuggestedPattern = "X", SampleDescription = "X", SampleDate = new DateOnly(2026, 7, 1), TransactionIds = [1], TotalAmount = -10m, AccountName = "Amex" }],
            AmazonItemGroups = [new PendingAmazonItemGroup { SuggestedPattern = "Y", ItemTitle = "Y", SampleDate = new DateOnly(2026, 7, 2), ItemIds = [2], TotalPrice = 5m }],
            Categories = []
        });

        var cut = Render<NavMenu>();

        Assert.Equal("Review Queue (2 items need review)", cut.Find("#nav-review-queue-link").TextContent.Trim());
    }

    [Fact]
    public void NavMenu_HasASyncNowLink()
    {
        RegisterFakes();

        var cut = Render<NavMenu>();

        var link = cut.Find("#nav-sync-now-link");
        Assert.Equal("sync-now", link.GetAttribute("href"));
    }

    [Fact]
    public void SyncNowLink_HasNoFailureSuffix_WhenBothLastSyncsSucceededOrNeverRan()
    {
        RegisterFakes(lastSimpleFinRun: new ImportRun { Source = ImportSource.SimpleFin, RanAt = DateTimeOffset.UtcNow, Success = true });

        var cut = Render<NavMenu>();

        Assert.Equal("Sync Now", cut.Find("#nav-sync-now-link").TextContent.Trim());
    }

    [Fact]
    public void SyncNowLink_ShowsSingularFailureSuffix_WhenExactlyOneSourceFailed()
    {
        RegisterFakes(lastSimpleFinRun: new ImportRun { Source = ImportSource.SimpleFin, RanAt = DateTimeOffset.UtcNow, Success = false, ErrorMessage = "connection timed out" });

        var cut = Render<NavMenu>();

        Assert.Equal("Sync Now (1 sync failed)", cut.Find("#nav-sync-now-link").TextContent.Trim());
    }

    [Fact]
    public void SyncNowLink_ShowsPluralFailureSuffix_WhenBothSourcesFailed()
    {
        RegisterFakes(
            lastSimpleFinRun: new ImportRun { Source = ImportSource.SimpleFin, RanAt = DateTimeOffset.UtcNow, Success = false, ErrorMessage = "connection timed out" },
            lastAmazonRun: new ImportRun { Source = ImportSource.AmazonGmail, RanAt = DateTimeOffset.UtcNow, Success = false, ErrorMessage = "Gmail OAuth token expired" });

        var cut = Render<NavMenu>();

        Assert.Equal("Sync Now (2 syncs failed)", cut.Find("#nav-sync-now-link").TextContent.Trim());
    }
}
