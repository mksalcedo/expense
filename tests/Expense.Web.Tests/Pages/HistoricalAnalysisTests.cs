using Bunit;
using Expense.Domain.Entities;
using Expense.Domain.Services;
using Expense.Domain.Services.HistoricalAnalysis;
using Expense.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Web.Tests.Pages;

public class HistoricalAnalysisTests : BunitContext
{
    private readonly DataChangeNotifier _dataChangeNotifier = new();

    public HistoricalAnalysisTests()
    {
        Services.AddSingleton<IDataChangeNotifier>(_dataChangeNotifier);
    }

    private class FakeHistoricalAnalysisPageProvider : IHistoricalAnalysisPageProvider
    {
        public HistoricalAnalysisPageData Data { get; set; } = null!;
        public List<PeriodSpendingSummary> TrendResult { get; set; } = [];
        public int? LastTrendCategoryId { get; private set; }

        public Task<HistoricalAnalysisPageData> GetHistoricalAnalysisAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Data);

        public Task<List<PeriodSpendingSummary>> GetCategoryTrendAsync(int categoryId, CancellationToken cancellationToken = default)
        {
            LastTrendCategoryId = categoryId;
            return Task.FromResult(TrendResult);
        }
    }

    private static HistoricalAnalysisPageData MakeData() => new()
    {
        WeeklyReport =
        [
            new PeriodSpendingSummary { PeriodStart = new DateOnly(2026, 7, 12), PeriodEnd = new DateOnly(2026, 7, 18), CategoryId = 1, CategoryName = "Groceries", Budget = 450m, Actual = 120m }
        ],
        MonthlyReport =
        [
            new PeriodSpendingSummary { PeriodStart = new DateOnly(2026, 7, 1), PeriodEnd = new DateOnly(2026, 7, 31), CategoryId = 1, CategoryName = "Groceries", Budget = 1956.70m, Actual = 800m }
        ],
        YearToDate =
        [
            new PeriodSpendingSummary { PeriodStart = new DateOnly(2026, 1, 1), PeriodEnd = new DateOnly(2026, 7, 15), CategoryId = 1, CategoryName = "Groceries", Budget = null, Actual = 9500m }
        ],
        FourWeekAverage = [new CategoryAverageSummary { CategoryId = 1, CategoryName = "Groceries", AverageActual = 410m, CurrentBudget = 450m }],
        ThirteenWeekAverage = [new CategoryAverageSummary { CategoryId = 1, CategoryName = "Groceries", AverageActual = 430m, CurrentBudget = 450m }],
        RecurringProducts =
        [
            new RecurringProductSummary { ProductId = 1, ProductPattern = "%FISH OIL%", CategoryName = "Supplements", Purchases = 6, AveragePrice = 22.50m, TotalSpent = 135m, LastPurchased = new DateOnly(2026, 6, 1) }
        ],
        Categories = [new Category { Id = 1, Name = "Groceries" }, new Category { Id = 2, Name = "Supplements" }]
    };

    private FakeHistoricalAnalysisPageProvider RegisterFake()
    {
        var provider = new FakeHistoricalAnalysisPageProvider { Data = MakeData() };
        Services.AddSingleton<IHistoricalAnalysisPageProvider>(provider);
        return provider;
    }

    // Real gap this guards (2026-08-17): a background scheduled sync completing while the
    // user just sits on this page never used to be reflected without a manual refresh.
    [Fact]
    public void DataChangeNotifier_Firing_RefreshesTheReport_WithoutNavigatingOrReloading()
    {
        var provider = RegisterFake();

        var cut = Render<HistoricalAnalysis>();
        Assert.Contains("9,500.00", cut.Markup);

        // Simulates a background sync landing new YTD spend - nothing on this page did
        // anything to cause it.
        provider.Data = new HistoricalAnalysisPageData
        {
            WeeklyReport = provider.Data.WeeklyReport,
            MonthlyReport = provider.Data.MonthlyReport,
            YearToDate = [new PeriodSpendingSummary { PeriodStart = new DateOnly(2026, 1, 1), PeriodEnd = new DateOnly(2026, 7, 15), CategoryId = 1, CategoryName = "Groceries", Budget = null, Actual = 12345m }],
            FourWeekAverage = provider.Data.FourWeekAverage,
            ThirteenWeekAverage = provider.Data.ThirteenWeekAverage,
            RecurringProducts = provider.Data.RecurringProducts,
            Categories = provider.Data.Categories
        };
        _dataChangeNotifier.NotifyChanged();

        cut.WaitForAssertion(() => Assert.Contains("12,345.00", cut.Markup));
    }

    [Fact]
    public void HistoricalAnalysis_RendersWeeklyMonthlyAndYearToDateReports()
    {
        RegisterFake();

        var cut = Render<HistoricalAnalysis>();

        Assert.Contains("Groceries", cut.Markup);
        Assert.Contains("450.00", cut.Markup); // weekly budget
        Assert.Contains("1,956.70", cut.Markup); // monthly budget
        Assert.Contains("9,500.00", cut.Markup); // YTD actual
    }

    [Fact]
    public void HistoricalAnalysis_RendersRollingAverages()
    {
        RegisterFake();

        var cut = Render<HistoricalAnalysis>();

        Assert.Contains("410.00", cut.Markup); // 4-week average
        Assert.Contains("430.00", cut.Markup); // 13-week average
    }

    [Fact]
    public void HistoricalAnalysis_RendersRecurringProductReport()
    {
        RegisterFake();

        var cut = Render<HistoricalAnalysis>();

        Assert.Contains("%FISH OIL%", cut.Markup);
        Assert.Contains("Supplements", cut.Markup);
        Assert.Contains("22.50", cut.Markup);
        Assert.Contains("135.00", cut.Markup);
        Assert.Contains("2026-06-01", cut.Markup);
    }

    [Fact]
    public void HistoricalAnalysis_RightAlignsAmountColumns()
    {
        RegisterFake();

        var cut = Render<HistoricalAnalysis>();

        Assert.All(cut.FindAll("th"), h =>
        {
            if (h.TextContent is "Budget" or "Actual" or "Remaining" or "Average actual" or "Current budget" or "Average price" or "Total spent")
            {
                Assert.Equal("text-right", h.GetAttribute("class"));
            }
        });
    }

    [Fact]
    public void SelectingACategory_FetchesAndRendersItsTrend()
    {
        var provider = RegisterFake();
        provider.TrendResult =
        [
            new PeriodSpendingSummary { PeriodStart = new DateOnly(2026, 6, 28), PeriodEnd = new DateOnly(2026, 7, 4), CategoryId = 2, CategoryName = "Supplements", Budget = null, Actual = 88m }
        ];

        var cut = Render<HistoricalAnalysis>();
        cut.Find("#trend-category-select").Change("2");

        Assert.Equal(2, provider.LastTrendCategoryId);
        Assert.Contains("88.00", cut.Markup);
    }
}
