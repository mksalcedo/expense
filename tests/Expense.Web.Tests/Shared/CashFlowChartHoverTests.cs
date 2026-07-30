using Expense.Web.Components.Shared;

namespace Expense.Web.Tests.Shared;

public class CashFlowChartHoverTests
{
    private static CashFlowChartPoint Point(DateOnly date, decimal balance, double x, double y) =>
        new() { Date = date, Balance = balance, X = x, Y = y };

    private static CashFlowChartModel Model(
        List<CashFlowChartPoint> points,
        List<CashFlowChartPoint>? trendPoints = null,
        List<CashFlowChartPoint>? movingAveragePoints = null) => new()
    {
        Points = points,
        ZeroLineY = 0,
        LowestPoint = null,
        LowestPointAnchor = "middle",
        MonthTicks = [],
        YAxisTicks = [],
        TrendSlopePerDay = 0m,
        TrendLineStart = (0, 0),
        TrendLineEnd = (0, 0),
        TrendPoints = trendPoints ?? points,
        MovingAveragePoints = movingAveragePoints ?? points,
        Width = 800,
        Height = 260
    };

    [Fact]
    public void GetHoverInfo_ReturnsNull_WhenNoLineIsWithinThreshold()
    {
        var model = Model([Point(new DateOnly(2026, 1, 1), 1000m, x: 100, y: 100)]);

        var info = CashFlowChartBuilder.GetHoverInfo(model, mouseX: 100, mouseY: 300, showRawLine: true, showTrendLine: false, showMovingAverage: false);

        Assert.Null(info);
    }

    [Fact]
    public void GetHoverInfo_ReturnsTheRawLine_WhenCursorIsCloseToIt()
    {
        var model = Model([Point(new DateOnly(2026, 1, 1), 1234m, x: 100, y: 100)]);

        var info = CashFlowChartBuilder.GetHoverInfo(model, mouseX: 100, mouseY: 105, showRawLine: true, showTrendLine: false, showMovingAverage: false);

        Assert.NotNull(info);
        var line = Assert.Single(info!.Lines);
        Assert.Equal(1234m, line.Value);
    }

    [Fact]
    public void GetHoverInfo_ExcludesALine_WhenItIsToggledOff()
    {
        var model = Model([Point(new DateOnly(2026, 1, 1), 1234m, x: 100, y: 100)]);

        // Cursor is right on the raw line's point, but the raw line is toggled off - nothing to show.
        var info = CashFlowChartBuilder.GetHoverInfo(model, mouseX: 100, mouseY: 100, showRawLine: false, showTrendLine: false, showMovingAverage: false);

        Assert.Null(info);
    }

    [Fact]
    public void GetHoverInfo_IncludesMultipleLines_WhenCursorIsNearWhereTheyCross()
    {
        var date = new DateOnly(2026, 1, 1);
        var raw = new List<CashFlowChartPoint> { Point(date, 1000m, x: 100, y: 100) };
        var trend = new List<CashFlowChartPoint> { Point(date, 1010m, x: 100, y: 103) };
        var movingAverage = new List<CashFlowChartPoint> { Point(date, 5000m, x: 100, y: 250) };
        var model = Model(raw, trend, movingAverage);

        var info = CashFlowChartBuilder.GetHoverInfo(model, mouseX: 100, mouseY: 101, showRawLine: true, showTrendLine: true, showMovingAverage: true);

        Assert.NotNull(info);
        Assert.Equal(2, info!.Lines.Count);
        Assert.Contains(info.Lines, l => l.Value == 1000m);
        Assert.Contains(info.Lines, l => l.Value == 1010m);
        Assert.DoesNotContain(info.Lines, l => l.Value == 5000m);
    }

    [Fact]
    public void GetHoverInfo_PicksTheNearestRowByX()
    {
        var model = Model(
        [
            Point(new DateOnly(2026, 1, 1), 1000m, x: 100, y: 100),
            Point(new DateOnly(2026, 1, 11), 2000m, x: 300, y: 100)
        ]);

        var info = CashFlowChartBuilder.GetHoverInfo(model, mouseX: 290, mouseY: 100, showRawLine: true, showTrendLine: false, showMovingAverage: false);

        Assert.NotNull(info);
        Assert.Equal(new DateOnly(2026, 1, 11), info!.Date);
    }

    [Fact]
    public void GetHoverInfo_RespectsACustomThreshold()
    {
        var model = Model([Point(new DateOnly(2026, 1, 1), 1000m, x: 100, y: 100)]);

        var info = CashFlowChartBuilder.GetHoverInfo(
            model, mouseX: 100, mouseY: 108, showRawLine: true, showTrendLine: false, showMovingAverage: false, thresholdPixels: 5);

        Assert.Null(info);
    }
}
