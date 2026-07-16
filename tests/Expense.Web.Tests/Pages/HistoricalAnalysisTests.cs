using Bunit;
using Expense.Domain.Entities;
using Expense.Domain.Services.HistoricalAnalysis;
using Expense.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Web.Tests.Pages;

public class HistoricalAnalysisTests : BunitContext
{
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
