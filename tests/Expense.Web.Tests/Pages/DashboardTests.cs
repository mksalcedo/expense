using Bunit;
using Expense.Domain.Entities;
using Expense.Domain.Services.Categorization;
using Expense.Domain.Services.Dashboard;
using Expense.Domain.Services.Forecast;
using Expense.Domain.Services.SpendingTracker;
using Expense.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Web.Tests.Pages;

public class DashboardTests : BunitContext
{
    private class FakeForecastResultProvider(ForecastResult result) : IForecastResultProvider
    {
        public Task<ForecastResult> GetForecastAsync(CancellationToken cancellationToken = default) => Task.FromResult(result);
        public Task DeferPaymentAsync(int accountId, DateOnly originalDate, DateOnly deferredToDate, string? note, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveDeferralAsync(int deferralId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private class FakeSpendingTrackerPageProvider(SpendingTrackerPageData data) : ISpendingTrackerPageProvider
    {
        public Task<SpendingTrackerPageData> GetSpendingTrackerAsync(CancellationToken cancellationToken = default) => Task.FromResult(data);
    }

    private class FakeReviewQueueProvider(ReviewQueueData data) : IReviewQueueProvider
    {
        public Task<ReviewQueueData> GetReviewQueueAsync(CancellationToken cancellationToken = default) => Task.FromResult(data);
        public Task<int> CategorizeTransactionAsync(int transactionId, int categoryId, string? merchantPatternToCreate, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> CategorizeAmazonItemAsync(int itemId, int categoryId, string? productPatternToCreate, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<ReapplyRulesResult> ReapplyRulesAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ReapplyRulesResult());
        public Task<int> BulkCategorizeTransactionsAsync(IReadOnlyList<int> transactionIds, int categoryId, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> BulkCategorizeAmazonItemsAsync(IReadOnlyList<int> itemIds, int categoryId, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private class FakeSyncStatusProvider(ImportRun? lastSimpleFinRun = null, ImportRun? lastAmazonRun = null) : ISyncStatusProvider
    {
        public int SimpleFinRunCount { get; private set; }
        public int AmazonGmailRunCount { get; private set; }
        public ImportRun NextSimpleFinRunResult { get; set; } = new() { Source = ImportSource.SimpleFin, RanAt = DateTimeOffset.UtcNow, Success = true, Summary = "ok" };
        public ImportRun NextAmazonGmailRunResult { get; set; } = new() { Source = ImportSource.AmazonGmail, RanAt = DateTimeOffset.UtcNow, Success = true, Summary = "ok" };

        public Task<ImportRun?> GetLastSimpleFinRunAsync(CancellationToken cancellationToken = default) => Task.FromResult(lastSimpleFinRun);
        public Task<ImportRun?> GetLastAmazonGmailRunAsync(CancellationToken cancellationToken = default) => Task.FromResult(lastAmazonRun);

        public Task<ImportRun> RunSimpleFinSyncAsync(CancellationToken cancellationToken = default)
        {
            SimpleFinRunCount++;
            return Task.FromResult(NextSimpleFinRunResult);
        }

        public Task<ImportRun> RunAmazonGmailSyncAsync(CancellationToken cancellationToken = default)
        {
            AmazonGmailRunCount++;
            return Task.FromResult(NextAmazonGmailRunResult);
        }
    }

    private static ForecastResult MakeForecast() => new()
    {
        StartingBalance = 6463.02m,
        Rows =
        [
            new ForecastLedgerRow { Date = new DateOnly(2026, 7, 20), Description = "Discover Payment", Amount = -150m, RunningBalance = 6313.02m },
            new ForecastLedgerRow { Date = new DateOnly(2026, 7, 31), Description = "Paycheck", Amount = 2000m, RunningBalance = 8313.02m }
        ]
    };

    private static SpendingTrackerPageData MakeSpendingTracker() => new()
    {
        Week = new SpendingTrackerResult
        {
            PeriodStart = new DateOnly(2026, 7, 12),
            PeriodEnd = new DateOnly(2026, 7, 18),
            Categories = [new CategorySpendingSummary { CategoryId = 1, CategoryName = "Groceries", Budget = 450m, Actual = 120m }],
            PendingAmount = 30m
        },
        Month = new SpendingTrackerResult
        {
            PeriodStart = new DateOnly(2026, 7, 1),
            PeriodEnd = new DateOnly(2026, 7, 31),
            Categories = [new CategorySpendingSummary { CategoryId = 1, CategoryName = "Groceries", Budget = 1956.70m, Actual = 800m }],
            PendingAmount = 60m
        }
    };

    private static ReviewQueueData MakeReviewQueue() => new()
    {
        TransactionGroups =
        [
            new PendingTransactionGroup { SuggestedPattern = "COSTCO", SampleDescription = "COSTCO WHSE", SampleDate = new DateOnly(2026, 7, 10), TransactionIds = [1, 2], TotalAmount = -212.99m, AccountName = "Wells Fargo Checking" }
        ],
        AmazonItemGroups =
        [
            new PendingAmazonItemGroup { SuggestedPattern = "VITAMIN", ItemTitle = "Vitamins", SampleDate = new DateOnly(2026, 7, 8), ItemIds = [10], TotalPrice = 25m },
            new PendingAmazonItemGroup { SuggestedPattern = "GADGET", ItemTitle = "New gadget", SampleDate = new DateOnly(2026, 7, 9), ItemIds = [11], TotalPrice = 40m }
        ],
        Categories = []
    };

    private FakeSyncStatusProvider RegisterFakes(ImportRun? lastSimpleFinRun = null, ImportRun? lastAmazonRun = null)
    {
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(MakeForecast()));
        Services.AddSingleton<ISpendingTrackerPageProvider>(new FakeSpendingTrackerPageProvider(MakeSpendingTracker()));
        Services.AddSingleton<IReviewQueueProvider>(new FakeReviewQueueProvider(MakeReviewQueue()));
        var syncStatusProvider = new FakeSyncStatusProvider(lastSimpleFinRun, lastAmazonRun);
        Services.AddSingleton<ISyncStatusProvider>(syncStatusProvider);
        return syncStatusProvider;
    }

    [Fact]
    public void Dashboard_RendersForecastSummary()
    {
        RegisterFakes();

        var cut = Render<Dashboard>();

        Assert.Contains("6,463.02", cut.Markup);
        Assert.Contains("Discover Payment", cut.Markup);
    }

    [Fact]
    public void Dashboard_RendersThisWeeksSpending()
    {
        RegisterFakes();

        var cut = Render<Dashboard>();

        Assert.Contains("Groceries", cut.Markup);
        Assert.Contains("450.00", cut.Markup);
        Assert.Contains("120.00", cut.Markup);
    }

    [Fact]
    public void Dashboard_RendersReviewQueuePendingGroupCount()
    {
        RegisterFakes();

        var cut = Render<Dashboard>();

        // 1 transaction group + 2 Amazon item groups = 3 pending groups
        Assert.Contains("3", cut.Markup);
    }

    [Fact]
    public void Dashboard_LinksToEveryOtherPage()
    {
        RegisterFakes();

        var cut = Render<Dashboard>();

        Assert.Contains("href=\"/forecast\"", cut.Markup);
        Assert.Contains("href=\"/spending-tracker\"", cut.Markup);
        Assert.Contains("href=\"/review-queue\"", cut.Markup);
        Assert.Contains("href=\"/categories\"", cut.Markup);
        Assert.Contains("href=\"/budgets\"", cut.Markup);
        Assert.Contains("href=\"/accounts\"", cut.Markup);
        Assert.Contains("href=\"/one-time-events\"", cut.Markup);
        Assert.Contains("href=\"/transactions\"", cut.Markup);
    }

    [Fact]
    public void Dashboard_AmazonSyncButton_IsClearlyLabeledForAmazonOrders()
    {
        RegisterFakes();

        var cut = Render<Dashboard>();

        var button = cut.Find("#sync-amazon-btn");
        Assert.Contains("Amazon", button.TextContent);
        Assert.Contains("Amazon order/refund emails", cut.Markup);
    }

    [Fact]
    public void Dashboard_WhenNeitherSourceHasEverSynced_ShowsNever()
    {
        RegisterFakes();

        var cut = Render<Dashboard>();

        Assert.Contains("Last synced: never", cut.Find("#sync-simplefin-status").TextContent);
        Assert.Contains("Last synced: never", cut.Find("#sync-amazon-status").TextContent);
    }

    [Fact]
    public void Dashboard_ShowsTheLastSuccessfulSyncTime()
    {
        var lastRun = new ImportRun
        {
            Source = ImportSource.SimpleFin, RanAt = new DateTimeOffset(2026, 7, 16, 8, 30, 0, TimeSpan.Zero), Success = true, Summary = "ok"
        };
        RegisterFakes(lastSimpleFinRun: lastRun);

        var cut = Render<Dashboard>();

        Assert.Contains("Last synced:", cut.Find("#sync-simplefin-status").TextContent);
        Assert.DoesNotContain("FAILED", cut.Find("#sync-simplefin-status").TextContent);
    }

    [Fact]
    public void Dashboard_ShowsTheErrorWhenTheLastSyncFailed()
    {
        var failedRun = new ImportRun
        {
            Source = ImportSource.AmazonGmail, RanAt = DateTimeOffset.UtcNow, Success = false, ErrorMessage = "Gmail OAuth token expired"
        };
        RegisterFakes(lastAmazonRun: failedRun);

        var cut = Render<Dashboard>();

        Assert.Contains("FAILED: Gmail OAuth token expired", cut.Find("#sync-amazon-status").TextContent);
    }

    [Fact]
    public void Dashboard_ClickingSimpleFinButton_TriggersASyncAndUpdatesTheDisplayedStatus()
    {
        var fake = RegisterFakes();
        fake.NextSimpleFinRunResult = new ImportRun
        {
            Source = ImportSource.SimpleFin, RanAt = new DateTimeOffset(2026, 7, 16, 9, 0, 0, TimeSpan.Zero), Success = true,
            Summary = "Transactions added: 5, duplicates skipped: 0, balance snapshots added: 2"
        };

        var cut = Render<Dashboard>();
        cut.Find("#sync-simplefin-btn").Click();

        Assert.Equal(1, fake.SimpleFinRunCount);
        Assert.Equal(0, fake.AmazonGmailRunCount);
        Assert.Contains("Last synced:", cut.Find("#sync-simplefin-status").TextContent);
    }

    [Fact]
    public void Dashboard_ClickingAmazonButton_TriggersASyncIndependentlyOfSimpleFin()
    {
        var fake = RegisterFakes();

        var cut = Render<Dashboard>();
        cut.Find("#sync-amazon-btn").Click();

        Assert.Equal(1, fake.AmazonGmailRunCount);
        Assert.Equal(0, fake.SimpleFinRunCount);
    }
}
