using Bunit;
using Expense.Domain.Services.SpendingTracker;
using Expense.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Web.Tests.Pages;

public class SpendingTrackerTests : BunitContext
{
    private class FakeSpendingTrackerPageProvider(SpendingTrackerPageData data) : ISpendingTrackerPageProvider
    {
        public Task<SpendingTrackerPageData> GetSpendingTrackerAsync(CancellationToken cancellationToken = default) => Task.FromResult(data);
    }

    private static SpendingTrackerPageData MakeData() => new()
    {
        Week = new SpendingTrackerResult
        {
            PeriodStart = new DateOnly(2026, 7, 12),
            PeriodEnd = new DateOnly(2026, 7, 18),
            Categories =
            [
                new CategorySpendingSummary { CategoryId = 1, CategoryName = "Groceries", Budget = 450m, Actual = 120m },
                new CategorySpendingSummary { CategoryId = 2, CategoryName = "Restaurants", Budget = 150m, Actual = 200m }
            ],
            PendingAmount = 30m
        },
        Month = new SpendingTrackerResult
        {
            PeriodStart = new DateOnly(2026, 7, 1),
            PeriodEnd = new DateOnly(2026, 7, 31),
            Categories =
            [
                new CategorySpendingSummary { CategoryId = 1, CategoryName = "Groceries", Budget = 1956.70m, Actual = 800m }
            ],
            PendingAmount = 60m
        }
    };

    [Fact]
    public void SpendingTracker_RendersWeekAndMonthCategorySummaries()
    {
        Services.AddSingleton<ISpendingTrackerPageProvider>(new FakeSpendingTrackerPageProvider(MakeData()));

        var cut = Render<SpendingTracker>();

        Assert.Contains("Groceries", cut.Markup);
        Assert.Contains("Restaurants", cut.Markup);
        Assert.Contains("450.00", cut.Markup);
        Assert.Contains("120.00", cut.Markup);
        Assert.Contains("1,956.70", cut.Markup);
    }

    [Fact]
    public void SpendingTracker_RendersRemainingIncludingOverspend()
    {
        Services.AddSingleton<ISpendingTrackerPageProvider>(new FakeSpendingTrackerPageProvider(MakeData()));

        var cut = Render<SpendingTracker>();

        // Groceries week: 450 - 120 = 330 remaining
        Assert.Contains("330.00", cut.Markup);
        // Restaurants week: 150 - 200 = -50 (overspent)
        Assert.Contains("-50.00", cut.Markup);
    }

    [Fact]
    public void SpendingTracker_RendersPendingAmountForBothPeriods()
    {
        Services.AddSingleton<ISpendingTrackerPageProvider>(new FakeSpendingTrackerPageProvider(MakeData()));

        var cut = Render<SpendingTracker>();

        Assert.Contains("Pending", cut.Markup);
        Assert.Contains("30.00", cut.Markup);
        Assert.Contains("60.00", cut.Markup);
    }

    [Fact]
    public void SpendingTracker_RendersATotalsRow_IncludingPendingInTheActualAndRemainingTotals()
    {
        // Week: Groceries (450 budget, 120 actual) + Restaurants (150 budget, 200 actual)
        // + 30 pending. Budget total = 600. Actual total = 120+200+30 = 350 - pending has
        // to count here, since it's real money already spent, just not yet categorized.
        // Remaining total = 600-350 = 250.
        Services.AddSingleton<ISpendingTrackerPageProvider>(new FakeSpendingTrackerPageProvider(MakeData()));

        var cut = Render<SpendingTracker>();

        var totalsRow = cut.Find("#week-totals-row");
        Assert.Contains("600.00", totalsRow.TextContent);
        Assert.Contains("350.00", totalsRow.TextContent);
        Assert.Contains("250.00", totalsRow.TextContent);
    }

    [Fact]
    public void SpendingTracker_RendersPeriodDateRanges()
    {
        Services.AddSingleton<ISpendingTrackerPageProvider>(new FakeSpendingTrackerPageProvider(MakeData()));

        var cut = Render<SpendingTracker>();

        Assert.Contains("2026-07-12", cut.Markup);
        Assert.Contains("2026-07-18", cut.Markup);
        Assert.Contains("2026-07-01", cut.Markup);
        Assert.Contains("2026-07-31", cut.Markup);
    }
}
