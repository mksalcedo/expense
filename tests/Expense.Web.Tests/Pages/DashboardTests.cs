using Bunit;
using Expense.Domain.Entities;
using Expense.Domain.Services.Categorization;
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
            new PendingTransactionGroup { SuggestedPattern = "COSTCO", SampleDescription = "COSTCO WHSE", TransactionIds = [1, 2], TotalAmount = -212.99m }
        ],
        AmazonItemGroups =
        [
            new PendingAmazonItemGroup { SuggestedPattern = "VITAMIN", ItemTitle = "Vitamins", ItemIds = [10], TotalPrice = 25m },
            new PendingAmazonItemGroup { SuggestedPattern = "GADGET", ItemTitle = "New gadget", ItemIds = [11], TotalPrice = 40m }
        ],
        Categories = []
    };

    private void RegisterFakes()
    {
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(MakeForecast()));
        Services.AddSingleton<ISpendingTrackerPageProvider>(new FakeSpendingTrackerPageProvider(MakeSpendingTracker()));
        Services.AddSingleton<IReviewQueueProvider>(new FakeReviewQueueProvider(MakeReviewQueue()));
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
    }
}
