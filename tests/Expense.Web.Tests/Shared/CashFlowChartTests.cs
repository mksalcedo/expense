using Bunit;
using Expense.Domain.Services.Forecast;
using Expense.Web.Components.Shared;

namespace Expense.Web.Tests.Shared;

public class CashFlowChartTests : BunitContext
{
    private static ForecastLedgerRow Row(DateOnly date, decimal runningBalance) => new()
    {
        Date = date, Description = "Row", Amount = 0m, RunningBalance = runningBalance
    };

    [Fact]
    public void NoRows_ShowsAnEmptyStateMessage_NotAChart()
    {
        var forecast = new ForecastResult { StartingBalance = 1000m, Rows = [] };

        var cut = Render<CashFlowChart>(p => p.Add(c => c.Forecast, forecast));

        Assert.Empty(cut.FindAll("#cash-flow-chart-svg"));
        Assert.Contains("No forecast data", cut.Markup);
    }

    [Fact]
    public void RendersAPolyline_WithOnePointPerRow()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows =
            [
                Row(new DateOnly(2026, 1, 1), 1000m),
                Row(new DateOnly(2026, 1, 11), 500m),
                Row(new DateOnly(2026, 1, 21), 1500m)
            ]
        };

        var cut = Render<CashFlowChart>(p => p.Add(c => c.Forecast, forecast));

        var pointsAttr = cut.Find("#cash-flow-chart-line").GetAttribute("points")!;
        var pairs = pointsAttr.Trim().Split(' ');
        Assert.Equal(3, pairs.Length);
    }

    [Fact]
    public void RendersTheTrendSummary_ComparingStartAndEndBalances()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [Row(new DateOnly(2026, 1, 1), 1000m), Row(new DateOnly(2026, 12, 31), 4000m)]
        };

        var cut = Render<CashFlowChart>(p => p.Add(c => c.Forecast, forecast));

        var summary = cut.Find("#cash-flow-trend-summary").TextContent;
        Assert.Contains("1,000.00", summary);
        Assert.Contains("4,000.00", summary);
    }

    [Fact]
    public void RendersMonthTickLabels()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [Row(new DateOnly(2026, 1, 15), 1000m), Row(new DateOnly(2026, 3, 15), 1000m)]
        };

        var cut = Render<CashFlowChart>(p => p.Add(c => c.Forecast, forecast));

        Assert.Contains("Feb", cut.Markup);
        Assert.Contains("Mar", cut.Markup);
    }

    [Fact]
    public void RendersATrendLine()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [Row(new DateOnly(2026, 1, 1), 1000m), Row(new DateOnly(2026, 12, 31), 4000m)]
        };

        var cut = Render<CashFlowChart>(p => p.Add(c => c.Forecast, forecast));

        Assert.NotEmpty(cut.FindAll("#cash-flow-chart-trend-line"));
    }

    [Fact]
    public void RendersYAxisValueLabels_AndAZeroLineLabel()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [Row(new DateOnly(2026, 1, 1), 1000m), Row(new DateOnly(2026, 1, 11), 9000m)]
        };

        var cut = Render<CashFlowChart>(p => p.Add(c => c.Forecast, forecast));

        // 4 Y-axis value labels plus the dedicated "0" label at the zero line.
        Assert.Contains("9,900", cut.Markup); // padded max, rounded
        Assert.Contains(">0<", cut.Markup);
    }

    [Fact]
    public void TrendSummaryWord_ReflectsTheOverallSlope_NotJustFirstVersusLastPoint()
    {
        // Mostly declining across the window, with one big late spike that makes a *simple*
        // start/end comparison look positive (ends higher than it started) - the summary word
        // must still say "down", since it's driven by the regression trend, not two data points.
        var forecast = new ForecastResult
        {
            StartingBalance = 5000m,
            Rows =
            [
                Row(new DateOnly(2026, 1, 1), 5000m),
                Row(new DateOnly(2026, 3, 1), 3000m),
                Row(new DateOnly(2026, 5, 1), 1000m),
                Row(new DateOnly(2026, 7, 1), 500m),
                Row(new DateOnly(2026, 9, 1), 100m),
                Row(new DateOnly(2026, 9, 2), 5500m)
            ]
        };

        var cut = Render<CashFlowChart>(p => p.Add(c => c.Forecast, forecast));

        Assert.Contains("Trending down", cut.Find("#cash-flow-trend-summary").TextContent);
    }

    [Fact]
    public void TrendLineAndMovingAverage_AreBothShownByDefault()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [Row(new DateOnly(2026, 1, 1), 1000m), Row(new DateOnly(2026, 12, 31), 4000m)]
        };

        var cut = Render<CashFlowChart>(p => p.Add(c => c.Forecast, forecast));

        Assert.True(cut.Find("#cash-flow-chart-toggle-trend-line").HasAttribute("checked"));
        Assert.True(cut.Find("#cash-flow-chart-toggle-moving-average").HasAttribute("checked"));
        Assert.True(cut.Find("#cash-flow-chart-toggle-raw-line").HasAttribute("checked"));
        Assert.NotEmpty(cut.FindAll("#cash-flow-chart-trend-line"));
        Assert.NotEmpty(cut.FindAll("#cash-flow-chart-moving-average-line"));
        Assert.NotEmpty(cut.FindAll("#cash-flow-chart-line"));
    }

    [Fact]
    public void UncheckingRawLineToggle_HidesOnlyTheRawLine()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [Row(new DateOnly(2026, 1, 1), 1000m), Row(new DateOnly(2026, 12, 31), 4000m)]
        };

        var cut = Render<CashFlowChart>(p => p.Add(c => c.Forecast, forecast));
        cut.Find("#cash-flow-chart-toggle-raw-line").Change(false);

        Assert.Empty(cut.FindAll("#cash-flow-chart-line"));
        Assert.NotEmpty(cut.FindAll("#cash-flow-chart-trend-line"));
        Assert.NotEmpty(cut.FindAll("#cash-flow-chart-moving-average-line"));
    }

    [Fact]
    public void UncheckingTrendLineToggle_HidesOnlyTheTrendLine()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [Row(new DateOnly(2026, 1, 1), 1000m), Row(new DateOnly(2026, 12, 31), 4000m)]
        };

        var cut = Render<CashFlowChart>(p => p.Add(c => c.Forecast, forecast));
        cut.Find("#cash-flow-chart-toggle-trend-line").Change(false);

        Assert.Empty(cut.FindAll("#cash-flow-chart-trend-line"));
        Assert.NotEmpty(cut.FindAll("#cash-flow-chart-moving-average-line"));
    }

    [Fact]
    public void UncheckingMovingAverageToggle_HidesOnlyTheMovingAverage()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [Row(new DateOnly(2026, 1, 1), 1000m), Row(new DateOnly(2026, 12, 31), 4000m)]
        };

        var cut = Render<CashFlowChart>(p => p.Add(c => c.Forecast, forecast));
        cut.Find("#cash-flow-chart-toggle-moving-average").Change(false);

        Assert.Empty(cut.FindAll("#cash-flow-chart-moving-average-line"));
        Assert.NotEmpty(cut.FindAll("#cash-flow-chart-trend-line"));
    }

    [Fact]
    public void RendersALowestPointMarker_WithItsDateAndAmount()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows =
            [
                Row(new DateOnly(2026, 1, 1), 1000m),
                Row(new DateOnly(2026, 1, 11), -250m),
                Row(new DateOnly(2026, 1, 21), 1500m)
            ]
        };

        var cut = Render<CashFlowChart>(p => p.Add(c => c.Forecast, forecast));

        Assert.NotEmpty(cut.FindAll("#cash-flow-chart-lowest-marker"));
        Assert.Contains("-250.00", cut.Markup);
        Assert.Contains("01/11/2026", cut.Markup);
    }
}
