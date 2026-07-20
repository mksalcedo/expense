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
        public ForecastResult Result { get; set; } = result;
        public Task<ForecastResult> GetForecastAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result);
        public Task DeferPaymentAsync(int accountId, DateOnly originalDate, DateOnly deferredToDate, string? note, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveDeferralAsync(int deferralId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ConfirmPaymentAsync(int accountId, DateOnly originalDate, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task OverridePaymentAsync(int accountId, DateOnly originalDate, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveConfirmationAsync(int confirmationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private class FakeSpendingTrackerPageProvider(SpendingTrackerPageData data) : ISpendingTrackerPageProvider
    {
        public SpendingTrackerPageData Data { get; set; } = data;
        public Task<SpendingTrackerPageData> GetSpendingTrackerAsync(CancellationToken cancellationToken = default) => Task.FromResult(Data);
    }

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

    private class FakeSyncStatusProvider(ImportRun? lastSimpleFinRun = null, ImportRun? lastAmazonRun = null) : ISyncStatusProvider
    {
        public int SimpleFinRunCount { get; private set; }
        public int AmazonGmailRunCount { get; private set; }
        public ImportRun NextSimpleFinRunResult { get; set; } = new() { Source = ImportSource.SimpleFin, RanAt = DateTimeOffset.UtcNow, Success = true, Summary = "ok" };
        public ImportRun NextAmazonGmailRunResult { get; set; } = new() { Source = ImportSource.AmazonGmail, RanAt = DateTimeOffset.UtcNow, Success = true, Summary = "ok" };
        public List<SyncIssue> ActiveSyncIssues { get; set; } = [];

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

        public Task<List<SyncIssue>> GetActiveSyncIssuesAsync(CancellationToken cancellationToken = default) => Task.FromResult(ActiveSyncIssues);

        public string? LastResolvedOrderId { get; private set; }
        public string? LastResolvedItemTitle { get; private set; }
        public decimal? LastResolvedPrice { get; private set; }
        public int? LastResolvedQuantity { get; private set; }

        public Task ResolveSyncIssueAsync(int syncIssueId, string orderId, string itemTitle, decimal price, int quantity, CancellationToken cancellationToken = default)
        {
            LastResolvedOrderId = orderId;
            LastResolvedItemTitle = itemTitle;
            LastResolvedPrice = price;
            LastResolvedQuantity = quantity;
            ActiveSyncIssues = ActiveSyncIssues.Where(i => i.Id != syncIssueId).ToList();
            return Task.CompletedTask;
        }

        public Task IgnoreSyncIssueAsync(int syncIssueId, CancellationToken cancellationToken = default)
        {
            ActiveSyncIssues = ActiveSyncIssues.Where(i => i.Id != syncIssueId).ToList();
            return Task.CompletedTask;
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

    private FakeSyncStatusProvider RegisterFakes(ImportRun? lastSimpleFinRun = null, ImportRun? lastAmazonRun = null, List<SyncIssue>? activeSyncIssues = null)
    {
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(MakeForecast()));
        Services.AddSingleton<ISpendingTrackerPageProvider>(new FakeSpendingTrackerPageProvider(MakeSpendingTracker()));
        Services.AddSingleton<IReviewQueueProvider>(new FakeReviewQueueProvider(MakeReviewQueue()));
        var syncStatusProvider = new FakeSyncStatusProvider(lastSimpleFinRun, lastAmazonRun) { ActiveSyncIssues = activeSyncIssues ?? [] };
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
    public void Dashboard_ShowsWhenTheLowestProjectedBalanceOccurs()
    {
        // MakeForecast()'s lowest running balance (6,313.02) is on the Discover Payment
        // row, 2026-07-20 - same "Occurs on" treatment as the Forecast page itself.
        RegisterFakes();

        var cut = Render<Dashboard>();

        Assert.Contains("Occurs on 07/20/2026", cut.Markup);
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
    public void Dashboard_RightAlignsAmountColumns()
    {
        RegisterFakes();

        var cut = Render<Dashboard>();

        var headers = cut.FindAll("th").Select(h => h.TextContent).ToList();
        Assert.All(cut.FindAll("th"), h =>
        {
            if (h.TextContent is "Amount" or "Running balance" or "Budget" or "Actual" or "Remaining")
            {
                Assert.Equal("text-right", h.GetAttribute("class"));
            }
        });
        Assert.Contains(headers, h => h is "Amount" or "Budget"); // sanity check the headers we expect actually rendered
    }

    [Fact]
    public void Dashboard_ThisWeeksSpending_ShowsPendingRowInTheTable()
    {
        RegisterFakes();

        var cut = Render<Dashboard>();

        var pendingRow = cut.Find("#spending-week-pending-row");
        Assert.Contains("Pending", pendingRow.TextContent);
        Assert.Contains("30.00", pendingRow.TextContent);
    }

    [Fact]
    public void Dashboard_ThisWeeksSpending_ShowsATotalsRow_IncludingPending()
    {
        // Groceries: 450 budget, 120 actual, +30 pending. Budget total = 450.
        // Actual total = 120+30 = 150. Remaining total = 450-150 = 300.
        RegisterFakes();

        var cut = Render<Dashboard>();

        var totalsRow = cut.Find("#spending-week-totals-row");
        Assert.Contains("450.00", totalsRow.TextContent);
        Assert.Contains("150.00", totalsRow.TextContent);
        Assert.Contains("300.00", totalsRow.TextContent);
    }

    [Fact]
    public void Dashboard_RendersThisMonthsSpending_UnderneathThisWeeksSpending_WithItsOwnPendingAndTotals()
    {
        // Month: Groceries 1,956.70 budget, 800 actual, +60 pending. Budget total =
        // 1,956.70. Actual total = 800+60 = 860. Remaining total = 1,956.70-860 = 1,096.70.
        RegisterFakes();

        var cut = Render<Dashboard>();

        Assert.Contains("This Month's Spending", cut.Markup);
        var pendingRow = cut.Find("#spending-month-pending-row");
        Assert.Contains("60.00", pendingRow.TextContent);
        var totalsRow = cut.Find("#spending-month-totals-row");
        Assert.Contains("1,956.70", totalsRow.TextContent);
        Assert.Contains("860.00", totalsRow.TextContent);
        Assert.Contains("1,096.70", totalsRow.TextContent);
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
    public void Dashboard_LinksToItsOwnDetailPages()
    {
        // Only the pages this dashboard summarizes get their own "drill in" link here -
        // every other page (Categories, Budgets, Accounts, etc.) is reachable from the
        // navigation menu now, so the old "Manage" section of links was removed.
        RegisterFakes();

        var cut = Render<Dashboard>();

        Assert.Contains("href=\"/forecast\"", cut.Markup);
        Assert.Contains("href=\"/spending-tracker\"", cut.Markup);
        Assert.Contains("href=\"/review-queue\"", cut.Markup);
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

    [Fact]
    public void Dashboard_AfterSimpleFinSync_ReloadsForecastSpendingTrackerAndReviewQueue()
    {
        // Real gap this guards against: the sync buttons used to only refresh the "Last
        // synced" timestamp, leaving Cash Flow/This Week's Spending/Review Queue counts
        // showing stale pre-sync data until the whole page was manually reloaded.
        var forecastProvider = new FakeForecastResultProvider(MakeForecast());
        var spendingProvider = new FakeSpendingTrackerPageProvider(MakeSpendingTracker());
        var reviewQueueProvider = new FakeReviewQueueProvider(MakeReviewQueue());
        Services.AddSingleton<IForecastResultProvider>(forecastProvider);
        Services.AddSingleton<ISpendingTrackerPageProvider>(spendingProvider);
        Services.AddSingleton<IReviewQueueProvider>(reviewQueueProvider);
        Services.AddSingleton<ISyncStatusProvider>(new FakeSyncStatusProvider());

        var cut = Render<Dashboard>();
        Assert.Contains("6,463.02", cut.Markup);
        Assert.Contains("3 item(s) need categorization", cut.Markup); // 1 bank group + 2 Amazon groups in MakeReviewQueue

        // Simulate the sync having actually imported new data by the time it completes.
        forecastProvider.Result = new ForecastResult { StartingBalance = 9999.99m, Rows = [] };
        spendingProvider.Data = new SpendingTrackerPageData
        {
            Week = new SpendingTrackerResult { PeriodStart = new DateOnly(2026, 7, 12), PeriodEnd = new DateOnly(2026, 7, 18), Categories = [], PendingAmount = 0m },
            Month = new SpendingTrackerResult { PeriodStart = new DateOnly(2026, 7, 1), PeriodEnd = new DateOnly(2026, 7, 31), Categories = [], PendingAmount = 0m }
        };
        reviewQueueProvider.Data = new ReviewQueueData { TransactionGroups = [], AmazonItemGroups = [], Categories = [] };

        cut.Find("#sync-simplefin-btn").Click();

        Assert.Contains("9,999.99", cut.Markup);
        Assert.Contains("0 item(s) need categorization", cut.Markup);
    }

    [Fact]
    public void Dashboard_DoesNotShowAManageSection_NavigationMenuCoversIt()
    {
        RegisterFakes();

        var cut = Render<Dashboard>();

        Assert.DoesNotContain("Manage", cut.Markup);
    }

    [Fact]
    public void Dashboard_ShowsTheLastRunsSummary_NotJustTheTimestamp()
    {
        var lastAmazonRun = new ImportRun
        {
            Source = ImportSource.AmazonGmail, RanAt = DateTimeOffset.UtcNow, Success = true,
            Summary = "Order items added: 3, duplicates skipped: 319, refunds applied: 0; 2 email(s) failed to parse"
        };
        RegisterFakes(lastAmazonRun: lastAmazonRun);

        var cut = Render<Dashboard>();

        Assert.Contains("2 email(s) failed to parse", cut.Markup);
    }

    [Fact]
    public void Dashboard_WithNoSyncIssues_DoesNotShowTheSyncIssuesSection()
    {
        RegisterFakes();

        var cut = Render<Dashboard>();

        Assert.Empty(cut.FindAll("#sync-issues-section"));
    }

    [Fact]
    public void Dashboard_WithActiveSyncIssues_ShowsThemForReview_IncludingTheRawEmailBody()
    {
        var issues = new List<SyncIssue>
        {
            new()
            {
                Id = 1, Source = ImportSource.AmazonGmail, MessageId = "msg-1", Subject = "Ordered: 2 Nutrition items",
                Reason = "could not find any items in the email body", ReceivedDate = new DateOnly(2026, 7, 18),
                Body = "Order #\n113-3763507-4662613\n\nGrand Total:\n56.17 USD", CreatedAt = DateTimeOffset.UtcNow
            }
        };
        RegisterFakes(activeSyncIssues: issues);

        var cut = Render<Dashboard>();

        var section = cut.Find("#sync-issues-section");
        Assert.Contains("1", section.TextContent);
        Assert.Contains("Ordered: 2 Nutrition items", section.TextContent);
        Assert.Contains("could not find any items in the email body", section.TextContent);
        Assert.Contains("07/18/2026", section.TextContent);
        Assert.Contains("56.17 USD", section.TextContent); // the raw body, so Gmail never needs to be opened
    }

    [Fact]
    public void ResolvingASyncIssue_SubmitsTheEnteredDetails_AndRemovesItFromTheList()
    {
        var issues = new List<SyncIssue>
        {
            new() { Id = 1, Source = ImportSource.AmazonGmail, MessageId = "msg-1", Subject = "Ordered: 2 Nutrition items", Reason = "could not find any items", ReceivedDate = new DateOnly(2026, 7, 18), CreatedAt = DateTimeOffset.UtcNow }
        };
        var fake = RegisterFakes(activeSyncIssues: issues);
        var cut = Render<Dashboard>();

        cut.Find("#resolve-order-id-1").Change("113-3763507-4662613");
        cut.Find("#resolve-item-title-1").Change("Some Supplement");
        cut.Find("#resolve-price-1").Change("56.17");
        cut.Find("#resolve-quantity-1").Change("2");
        cut.Find("#resolve-btn-1").Click();

        Assert.Equal("113-3763507-4662613", fake.LastResolvedOrderId);
        Assert.Equal("Some Supplement", fake.LastResolvedItemTitle);
        Assert.Equal(56.17m, fake.LastResolvedPrice);
        Assert.Equal(2, fake.LastResolvedQuantity);
        Assert.Empty(cut.FindAll("#sync-issues-section"));
    }

    [Fact]
    public void IgnoringASyncIssueAsNotAnOrder_RemovesItFromTheList()
    {
        var issues = new List<SyncIssue>
        {
            new() { Id = 1, Source = ImportSource.AmazonGmail, MessageId = "msg-1", Subject = "An Amazon Gift Card you sent was received", Reason = "could not find an 'Order #' line", ReceivedDate = new DateOnly(2026, 7, 18), CreatedAt = DateTimeOffset.UtcNow }
        };
        RegisterFakes(activeSyncIssues: issues);
        var cut = Render<Dashboard>();

        cut.Find("#ignore-not-order-btn-1").Click();

        Assert.Empty(cut.FindAll("#sync-issues-section"));
    }

    [Fact]
    public void Dashboard_AfterAmazonSync_RefreshesTheSyncIssuesList()
    {
        var fake = RegisterFakes();
        var cut = Render<Dashboard>();
        Assert.Empty(cut.FindAll("#sync-issues-section"));

        fake.ActiveSyncIssues =
        [
            new SyncIssue { Id = 2, Source = ImportSource.AmazonGmail, MessageId = "msg-2", Subject = "New failure", Reason = "bad format", CreatedAt = DateTimeOffset.UtcNow }
        ];
        cut.Find("#sync-amazon-btn").Click();

        Assert.NotNull(cut.Find("#sync-issues-section"));
        Assert.Contains("New failure", cut.Markup);
    }
}
