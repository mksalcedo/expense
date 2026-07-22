using Bunit;
using Expense.Domain.Services.Categorization;
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
    }

    private static ReviewQueueData EmptyQueue() => new() { TransactionGroups = [], AmazonItemGroups = [], Categories = [] };

    private FakeReviewQueueProvider RegisterFakes(ReviewQueueData? data = null)
    {
        var provider = new FakeReviewQueueProvider(data ?? EmptyQueue());
        Services.AddSingleton<IReviewQueueProvider>(provider);
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
}
