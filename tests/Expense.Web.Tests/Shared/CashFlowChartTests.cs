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

    private IRenderedComponent<CashFlowChart> RenderChart(ForecastResult forecast)
    {
        var module = JSInterop.SetupModule("./js/cashFlowChart.js");
        module.SetupVoid("attach", _ => true);
        module.SetupVoid("detach", _ => true);

        return Render<CashFlowChart>(p => p.Add(c => c.Forecast, forecast));
    }

    [Fact]
    public void NoRows_ShowsAnEmptyStateMessage_NotAChart()
    {
        var forecast = new ForecastResult { StartingBalance = 1000m, Rows = [] };

        var cut = RenderChart(forecast);

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

        var cut = RenderChart(forecast);

        var pointsAttr = cut.Find("#cash-flow-chart-line").GetAttribute("points")!;
        var pairs = pointsAttr.Trim().Split(' ');
        Assert.Equal(3, pairs.Length);
    }

    [Fact]
    public void RendersTheTrendSummary_FromTheFittedRate_NotRawStartAndEndBalances()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [Row(new DateOnly(2026, 1, 1), 1000m), Row(new DateOnly(2026, 12, 31), 4000m)]
        };

        var cut = RenderChart(forecast);

        var summary = cut.Find("#cash-flow-trend-summary").TextContent;
        // A steady climb from 1,000 to 4,000 over ~12 months: ~8.24/day -> ~+251/month,
        // and the fitted line rises the full +3,000 across the window.
        Assert.Contains("Trending up", summary);
        Assert.Contains("+251/month", summary);
        Assert.Contains("+3,000 projected across ~12 months", summary);
        // The raw endpoint balances themselves are deliberately not shown here any more.
        Assert.DoesNotContain("1,000.00", summary);
        Assert.DoesNotContain("4,000.00", summary);
    }

    [Fact]
    public void TrendSummary_SaysRoughlyFlat_WhenTheSlopeIsOnlyAFewDollarsAMonth()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 5000m,
            Rows =
            [
                Row(new DateOnly(2026, 1, 1), 5000m),
                Row(new DateOnly(2026, 6, 1), 4980m),
                Row(new DateOnly(2026, 12, 31), 5010m)
            ]
        };

        var cut = RenderChart(forecast);

        var summary = cut.Find("#cash-flow-trend-summary").TextContent;
        Assert.Contains("Roughly flat", summary);
        Assert.DoesNotContain("Trending", summary);
    }

    [Fact]
    public void RendersMonthTickLabels()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [Row(new DateOnly(2026, 1, 15), 1000m), Row(new DateOnly(2026, 3, 15), 1000m)]
        };

        var cut = RenderChart(forecast);

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

        var cut = RenderChart(forecast);

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

        var cut = RenderChart(forecast);

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

        var cut = RenderChart(forecast);

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

        var cut = RenderChart(forecast);

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

        var cut = RenderChart(forecast);
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

        var cut = RenderChart(forecast);
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

        var cut = RenderChart(forecast);
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

        var cut = RenderChart(forecast);

        Assert.NotEmpty(cut.FindAll("#cash-flow-chart-lowest-marker"));
        Assert.Contains("-250.00", cut.Markup);
        Assert.Contains("01/11/2026", cut.Markup);
    }

    [Fact]
    public async Task Hovering_NearAPointOnTheRawLine_ShowsATooltipWithTheDateAndBalance()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [Row(new DateOnly(2026, 1, 1), 1000m), Row(new DateOnly(2026, 1, 11), 2500m)]
        };
        var model = CashFlowChartBuilder.Build(forecast, width: 800, height: 260)!;

        var cut = RenderChart(forecast);
        await cut.InvokeAsync(() => cut.Instance.OnChartHover(model.Points[0].X, model.Points[0].Y));

        Assert.NotEmpty(cut.FindAll("#cash-flow-chart-hover"));
        Assert.Contains("01/01/2026", cut.Markup);
        Assert.Contains("Balance: 1,000.00", cut.Markup);
    }

    [Fact]
    public async Task Hovering_FarFromAnyLine_ShowsNoTooltip()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [Row(new DateOnly(2026, 1, 1), 1000m), Row(new DateOnly(2026, 1, 11), 2500m)]
        };

        var cut = RenderChart(forecast);
        await cut.InvokeAsync(() => cut.Instance.OnChartHover(-1000, -1000));

        Assert.Empty(cut.FindAll("#cash-flow-chart-hover"));
    }

    [Fact]
    public async Task HoverEnd_ClearsTheTooltip()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [Row(new DateOnly(2026, 1, 1), 1000m), Row(new DateOnly(2026, 1, 11), 2500m)]
        };
        var model = CashFlowChartBuilder.Build(forecast, width: 800, height: 260)!;

        var cut = RenderChart(forecast);
        await cut.InvokeAsync(() => cut.Instance.OnChartHover(model.Points[0].X, model.Points[0].Y));
        await cut.InvokeAsync(() => cut.Instance.OnChartHoverEnd());

        Assert.Empty(cut.FindAll("#cash-flow-chart-hover"));
    }

    [Fact]
    public async Task Hovering_ExcludesAToggledOffLine_EvenWhenTheCursorIsRightOnItsPoint()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [Row(new DateOnly(2026, 1, 1), 1000m), Row(new DateOnly(2026, 1, 11), 2500m)]
        };
        var model = CashFlowChartBuilder.Build(forecast, width: 800, height: 260)!;

        var cut = RenderChart(forecast);
        cut.Find("#cash-flow-chart-toggle-trend-line").Change(false);
        cut.Find("#cash-flow-chart-toggle-moving-average").Change(false);
        cut.Find("#cash-flow-chart-toggle-raw-line").Change(false);
        await cut.InvokeAsync(() => cut.Instance.OnChartHover(model.Points[0].X, model.Points[0].Y));

        Assert.Empty(cut.FindAll("#cash-flow-chart-hover"));
    }
}
