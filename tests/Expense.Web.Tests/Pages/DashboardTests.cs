using Bunit;
using Expense.Domain.Entities;
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
        public Task ConfirmPaymentAsync(int accountId, DateOnly originalDate, DateOnly effectiveDate, decimal amount, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task OverridePaymentAsync(int accountId, DateOnly originalDate, DateOnly effectiveDate, decimal amount, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveConfirmationAsync(int confirmationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PayPartialAmountAsync(int accountId, DateOnly originalDate, DateOnly paidDate, decimal amount, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemovePartialPaymentAsync(int partialPaymentId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private class FakeSpendingTrackerPageProvider(SpendingTrackerPageData data) : ISpendingTrackerPageProvider
    {
        public SpendingTrackerPageData Data { get; set; } = data;
        public Task<SpendingTrackerPageData> GetSpendingTrackerAsync(CancellationToken cancellationToken = default) => Task.FromResult(Data);
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

    private void RegisterFakes(ForecastResult? forecast = null)
    {
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(forecast ?? MakeForecast()));
        Services.AddSingleton<ISpendingTrackerPageProvider>(new FakeSpendingTrackerPageProvider(MakeSpendingTracker()));
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
    public void CashFlow_ShowsAnExcludedRow_StruckThroughWithItsReason()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [new ForecastLedgerRow
            {
                Date = new DateOnly(2026, 7, 20), Description = "Chase Amazon Prime Visa Payment", Amount = -357m, RunningBalance = 643m,
                AccountId = 5, OriginalDate = new DateOnly(2026, 7, 20), IsExcluded = true, ExclusionReason = ConfirmationReason.AlreadyPaid, ConfirmationId = 1
            }]
        };
        RegisterFakes(forecast: forecast);

        var cut = Render<Dashboard>();

        var row = cut.FindAll("tbody tr").First(r => r.TextContent.Contains("Chase Amazon Prime Visa Payment"));
        Assert.Contains("line-through", row.GetAttribute("style") ?? "");
        Assert.Contains("AlreadyPaid", row.TextContent);
    }

    [Fact]
    public void CashFlow_ShowsADeferredRow_HighlightedWithItsOriginalDate()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [new ForecastLedgerRow
            {
                Date = new DateOnly(2026, 7, 22), Description = "Amex Payment", Amount = -2000m, RunningBalance = -1000m,
                AccountId = 2, OriginalDate = new DateOnly(2026, 7, 20), IsDeferred = true, DeferralId = 1
            }]
        };
        RegisterFakes(forecast: forecast);

        var cut = Render<Dashboard>();

        var row = cut.FindAll("tbody tr").First(r => r.TextContent.Contains("Amex Payment"));
        Assert.Contains("background-color: orange", row.GetAttribute("style") ?? "");
        Assert.Contains("Originally estimated for 07/20/2026", row.TextContent);
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
    public void Dashboard_RendersTheCashFlowTrendChart()
    {
        RegisterFakes();

        var cut = Render<Dashboard>();

        Assert.NotEmpty(cut.FindAll("#cash-flow-chart-svg"));
        Assert.NotEmpty(cut.FindAll("#cash-flow-chart-line"));
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
    public void Dashboard_LinksToItsOwnDetailPages()
    {
        // Only the pages this dashboard summarizes get their own "drill in" link here -
        // every other page (Categories, Budgets, Accounts, Review Queue, Sync Now, etc.) is
        // reachable from the navigation menu now.
        RegisterFakes();

        var cut = Render<Dashboard>();

        Assert.Contains("href=\"/forecast\"", cut.Markup);
        Assert.Contains("href=\"/spending-tracker\"", cut.Markup);
    }

    [Fact]
    public void Dashboard_DoesNotShowAManageSection_NavigationMenuCoversIt()
    {
        RegisterFakes();

        var cut = Render<Dashboard>();

        Assert.DoesNotContain("Manage", cut.Markup);
    }

    [Fact]
    public void Dashboard_DoesNotShowReviewQueueOrSyncNow_TheyHaveTheirOwnPagesNow()
    {
        RegisterFakes();

        var cut = Render<Dashboard>();

        Assert.DoesNotContain("Review Queue", cut.Markup);
        Assert.DoesNotContain("Sync Now", cut.Markup);
        Assert.Empty(cut.FindAll("#sync-simplefin-btn"));
        Assert.Empty(cut.FindAll("#sync-amazon-btn"));
    }
}
