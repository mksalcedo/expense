using Expense.Domain.Services.Forecast;
using Expense.Web.Components.Shared;

namespace Expense.Web.Tests.Shared;

public class CashFlowChartBuilderTests
{
    private const int Margin = CashFlowChartBuilder.LeftAxisMargin;

    private static ForecastLedgerRow Row(DateOnly date, decimal runningBalance, bool isExcluded = false) => new()
    {
        Date = date, Description = "Row", Amount = 0m, RunningBalance = runningBalance, IsExcluded = isExcluded
    };

    [Fact]
    public void Build_ReturnsNull_WhenThereAreNoRows()
    {
        var forecast = new ForecastResult { StartingBalance = 1000m, Rows = [] };

        var model = CashFlowChartBuilder.Build(forecast, width: 800, height: 260);

        Assert.Null(model);
    }

    [Fact]
    public void Build_PlacesTheFirstAndLastPointsAtThePlotAreasHorizontalEdges()
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

        var model = CashFlowChartBuilder.Build(forecast, width: 800, height: 260)!;

        Assert.Equal(3, model.Points.Count);
        // The plot area is inset by LeftAxisMargin to leave room for Y-axis value labels -
        // it doesn't start at the SVG's literal x=0.
        Assert.Equal(Margin, model.Points[0].X, precision: 3);
        Assert.Equal(800, model.Points[^1].X, precision: 3);
        // Middle point is 10 days into a 20-day span - exactly halfway across the plot area.
        Assert.Equal(Margin + (800 - Margin) / 2.0, model.Points[1].X, precision: 3);
    }

    [Fact]
    public void Build_ForcesZeroIntoTheVisibleRange_EvenWhenAllBalancesAreThePositive()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [Row(new DateOnly(2026, 1, 1), 1000m), Row(new DateOnly(2026, 1, 11), 2000m)]
        };

        var model = CashFlowChartBuilder.Build(forecast, width: 800, height: 260)!;

        Assert.True(model.ZeroLineY >= 0 && model.ZeroLineY <= 260);
    }

    [Fact]
    public void Build_PlacesHigherBalances_HigherOnScreen_LowerYCoordinate()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [Row(new DateOnly(2026, 1, 1), 1000m), Row(new DateOnly(2026, 1, 11), 2000m)]
        };

        var model = CashFlowChartBuilder.Build(forecast, width: 800, height: 260)!;

        // SVG y grows downward - a bigger balance must have a smaller (higher-up) y.
        Assert.True(model.Points[1].Y < model.Points[0].Y);
    }

    [Fact]
    public void Build_LowestPoint_MatchesTheForecastsOwnLowestProjectedBalance()
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

        var model = CashFlowChartBuilder.Build(forecast, width: 800, height: 260)!;

        Assert.NotNull(model.LowestPoint);
        Assert.Equal(forecast.LowestProjectedBalance, model.LowestPoint!.Balance);
        Assert.Equal(forecast.LowestProjectedBalanceDate, model.LowestPoint.Date);
    }

    [Fact]
    public void Build_LowestPointLabel_AnchorsToTheLeft_WhenTheLowestPointIsNearTheRightEdge()
    {
        // A center-anchored label at the far-right point would run off the chart entirely -
        // real case: a 12-month forecast whose lowest balance is its very last row.
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows =
            [
                Row(new DateOnly(2026, 1, 1), 1000m),
                Row(new DateOnly(2026, 12, 31), -500m)
            ]
        };

        var model = CashFlowChartBuilder.Build(forecast, width: 800, height: 260)!;

        Assert.Equal("end", model.LowestPointAnchor);
    }

    [Fact]
    public void Build_LowestPointLabel_AnchorsToTheRight_WhenTheLowestPointIsNearTheLeftEdge()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [Row(new DateOnly(2026, 1, 1), -500m), Row(new DateOnly(2026, 12, 31), 1000m)]
        };

        var model = CashFlowChartBuilder.Build(forecast, width: 800, height: 260)!;

        Assert.Equal("start", model.LowestPointAnchor);
    }

    [Fact]
    public void Build_LowestPointLabel_AnchorsInTheMiddle_WhenTheLowestPointIsWellAwayFromBothEdges()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [Row(new DateOnly(2026, 1, 1), 1000m), Row(new DateOnly(2026, 6, 15), -500m), Row(new DateOnly(2026, 12, 31), 1000m)]
        };

        var model = CashFlowChartBuilder.Build(forecast, width: 800, height: 260)!;

        Assert.Equal("middle", model.LowestPointAnchor);
    }

    [Fact]
    public void Build_GeneratesOneMonthTick_PerCalendarMonthBoundaryInRange()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows =
            [
                Row(new DateOnly(2026, 1, 15), 1000m),
                Row(new DateOnly(2026, 2, 1), 1000m),
                Row(new DateOnly(2026, 3, 1), 1000m),
                Row(new DateOnly(2026, 3, 15), 1000m)
            ]
        };

        var model = CashFlowChartBuilder.Build(forecast, width: 800, height: 260)!;

        Assert.Equal(["Feb", "Mar"], model.MonthTicks.Select(t => t.Label).ToList());
    }

    [Fact]
    public void Build_SinglePointAtASingleDate_DoesNotThrow_AndPlacesItAtThePlotAreasLeftEdge()
    {
        var forecast = new ForecastResult { StartingBalance = 1000m, Rows = [Row(new DateOnly(2026, 1, 1), 1000m)] };

        var model = CashFlowChartBuilder.Build(forecast, width: 800, height: 260)!;

        Assert.Single(model.Points);
        Assert.Equal(Margin, model.Points[0].X, precision: 3);
    }

    [Fact]
    public void Build_GeneratesFourEvenlySpacedYAxisTicks_SpanningThePaddedRange()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [Row(new DateOnly(2026, 1, 1), 1000m), Row(new DateOnly(2026, 1, 11), 2000m)]
        };

        var model = CashFlowChartBuilder.Build(forecast, width: 800, height: 260)!;

        Assert.Equal(4, model.YAxisTicks.Count);
        // Bottom tick (lowest value) renders at the bottom of the plot area; top tick sits
        // TopMargin down from the literal top, leaving room for its label's ascender.
        Assert.Equal(260, model.YAxisTicks[0].Y, precision: 1);
        Assert.Equal(CashFlowChartBuilder.TopMargin, model.YAxisTicks[^1].Y, precision: 1);
    }

    [Fact]
    public void Build_ReservesTopMargin_SoTheHighestLabelIsNotClippedByTheChartsTopEdge()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [Row(new DateOnly(2026, 1, 1), 1000m), Row(new DateOnly(2026, 1, 11), 2000m)]
        };

        var model = CashFlowChartBuilder.Build(forecast, width: 800, height: 260)!;

        // A y=0 label would have its text ascender rendered above y=0, outside the SVG's own
        // viewBox - real bug found in manual testing (the top Y-axis value was visibly cut off).
        Assert.True(model.YAxisTicks[^1].Y > 0);
        Assert.True(model.Points.All(p => p.Y >= CashFlowChartBuilder.TopMargin));
    }

    [Fact]
    public void Build_TrendLine_MatchesTheDataExactly_WhenTheDataIsPerfectlyLinear()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows =
            [
                Row(new DateOnly(2026, 1, 1), 1000m),
                Row(new DateOnly(2026, 1, 6), 1500m),
                Row(new DateOnly(2026, 1, 11), 2000m)
            ]
        };

        var model = CashFlowChartBuilder.Build(forecast, width: 800, height: 260)!;

        // A perfect line's best-fit regression is itself - the trend line's endpoints should
        // land right on top of the raw line's first/last points.
        Assert.Equal(model.Points[0].Y, model.TrendLineStart.Y, precision: 1);
        Assert.Equal(model.Points[^1].Y, model.TrendLineEnd.Y, precision: 1);
    }

    [Fact]
    public void Build_TrendSlope_IsPositive_ForClearlyIncreasingData()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows =
            [
                Row(new DateOnly(2026, 1, 1), 1000m),
                Row(new DateOnly(2026, 1, 5), 900m), // a dip mid-way must not flip the overall reading
                Row(new DateOnly(2026, 1, 11), 3000m)
            ]
        };

        var model = CashFlowChartBuilder.Build(forecast, width: 800, height: 260)!;

        Assert.True(model.TrendSlopePerDay > 0);
    }

    [Fact]
    public void Build_TrendSlope_IsNegative_ForClearlyDecreasingData()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 3000m,
            Rows =
            [
                Row(new DateOnly(2026, 1, 1), 3000m),
                Row(new DateOnly(2026, 1, 5), 3100m),
                Row(new DateOnly(2026, 1, 11), 1000m)
            ]
        };

        var model = CashFlowChartBuilder.Build(forecast, width: 800, height: 260)!;

        Assert.True(model.TrendSlopePerDay < 0);
    }

    [Fact]
    public void Build_TrendLine_DoesNotThrow_ForASinglePoint()
    {
        var forecast = new ForecastResult { StartingBalance = 1000m, Rows = [Row(new DateOnly(2026, 1, 1), 1000m)] };

        var model = CashFlowChartBuilder.Build(forecast, width: 800, height: 260)!;

        Assert.Equal(0m, model.TrendSlopePerDay);
    }

    [Fact]
    public void Build_MovingAverage_HasOnePointPerRow()
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

        var model = CashFlowChartBuilder.Build(forecast, width: 800, height: 260)!;

        Assert.Equal(3, model.MovingAveragePoints.Count);
    }

    [Fact]
    public void Build_MovingAverage_OnlyReflectsNearbyPoints_UnlikeTheGlobalTrendLine()
    {
        // Two tight clusters, ~11 months apart, with very different balances. A moving average
        // (local window) at a January point should read close to January's own cluster value -
        // not blended with the far-away December cluster the way the single global trend line
        // (which connects overall start to overall end regardless of position) would be.
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows =
            [
                Row(new DateOnly(2026, 1, 1), 1000m),
                Row(new DateOnly(2026, 1, 3), 1000m),
                Row(new DateOnly(2026, 1, 5), 1000m),
                Row(new DateOnly(2026, 12, 1), 9000m),
                Row(new DateOnly(2026, 12, 3), 9000m),
                Row(new DateOnly(2026, 12, 5), 9000m)
            ]
        };

        var model = CashFlowChartBuilder.Build(forecast, width: 800, height: 260)!;

        Assert.Equal(1000m, model.MovingAveragePoints[0].Balance, precision: 0);
        Assert.Equal(9000m, model.MovingAveragePoints[^1].Balance, precision: 0);
    }

    [Fact]
    public void Build_MovingAverage_SmoothsOutASingleDayOutlier_WithinItsWindow()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows =
            [
                Row(new DateOnly(2026, 1, 1), 1000m),
                Row(new DateOnly(2026, 1, 3), 1000m),
                Row(new DateOnly(2026, 1, 5), 9000m), // one-day spike
                Row(new DateOnly(2026, 1, 7), 1000m),
                Row(new DateOnly(2026, 1, 9), 1000m)
            ]
        };

        var model = CashFlowChartBuilder.Build(forecast, width: 800, height: 260)!;

        var spikeDayAverage = model.MovingAveragePoints[2].Balance;
        Assert.True(spikeDayAverage < 9000m); // pulled down toward its neighbors, not left at the raw spike value
        Assert.True(spikeDayAverage > 1000m);
    }
}
