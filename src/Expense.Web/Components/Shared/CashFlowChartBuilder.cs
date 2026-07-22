using Expense.Domain.Services.Forecast;

namespace Expense.Web.Components.Shared;

public class CashFlowChartPoint
{
    public required DateOnly Date { get; init; }
    public required decimal Balance { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
}

public class CashFlowChartTick
{
    public required double X { get; init; }
    public required string Label { get; init; }
}

public class CashFlowYAxisTick
{
    public required double Y { get; init; }
    public required string Label { get; init; }
}

public class CashFlowChartModel
{
    public required List<CashFlowChartPoint> Points { get; init; }
    public required double ZeroLineY { get; init; }
    public required CashFlowChartPoint? LowestPoint { get; init; }

    /// <summary>
    /// SVG text-anchor for the lowest-point label: a plain "middle" anchor would run the label
    /// off the edge of the chart when the lowest point itself is near an edge (e.g. the very
    /// last row of a 12-month forecast) - "end"/"start" keep the full label on-screen instead.
    /// </summary>
    public required string LowestPointAnchor { get; init; }

    public required List<CashFlowChartTick> MonthTicks { get; init; }

    /// <summary>A small fixed set of evenly-spaced dollar labels down the Y-axis, so the chart's shape can be read against an actual scale.</summary>
    public required List<CashFlowYAxisTick> YAxisTicks { get; init; }

    /// <summary>
    /// Least-squares linear regression of balance vs. elapsed time - a single, direct
    /// "trending up/down/flat" signal that isn't thrown off by the same payday/bill spikes the
    /// raw line shows, without discarding or averaging away any of that raw detail.
    /// </summary>
    public required decimal TrendSlopePerDay { get; init; }
    public required (double X, double Y) TrendLineStart { get; init; }
    public required (double X, double Y) TrendLineEnd { get; init; }

    /// <summary>
    /// A centered moving average (see MovingAverageWindowDays) - unlike the single straight
    /// trend line above, this curve responds to local conditions, so it can gradually bend up
    /// or down over the course of the window rather than being forced into one fixed slope.
    /// </summary>
    public required List<CashFlowChartPoint> MovingAveragePoints { get; init; }

    public required int Width { get; init; }
    public required int Height { get; init; }
}

/// <summary>
/// Pure geometry for a "cash flow over time" line chart, kept entirely separate from
/// CashFlowChart.razor's markup so the math (date-to-x, balance-to-y, month ticks, trend
/// regression) is unit-testable without rendering anything. Reuses ForecastResult's own
/// LowestProjectedBalance/Date rather than recomputing the minimum independently, so the
/// chart's marker always agrees with the plain-text figure shown alongside it.
/// </summary>
public static class CashFlowChartBuilder
{
    /// <summary>Reserved on the left for Y-axis value labels - the plot area itself starts here, not at x=0.</summary>
    public const int LeftAxisMargin = 60;

    /// <summary>
    /// Reserved at the top so the highest Y-axis label's text ascender doesn't get clipped by
    /// the chart's own top edge - a label positioned exactly at y=0 renders partly above it.
    /// </summary>
    public const int TopMargin = 12;

    private const int YAxisTickCount = 4;

    /// <summary>Half-width of the moving-average window - e.g. 15 means "average everything within 15 days either side" (a ~30-day window).</summary>
    private const int MovingAverageWindowDays = 15;

    public static CashFlowChartModel? Build(ForecastResult forecast, int width, int height)
    {
        if (forecast.Rows.Count == 0) return null;

        var plotWidth = width - LeftAxisMargin;

        var minDate = forecast.Rows.Min(r => r.Date);
        var maxDate = forecast.Rows.Max(r => r.Date);
        var totalDays = (maxDate.ToDateTime(TimeOnly.MinValue) - minDate.ToDateTime(TimeOnly.MinValue)).TotalDays;

        double XFor(DateOnly date) => LeftAxisMargin + (totalDays <= 0
            ? 0
            : (date.ToDateTime(TimeOnly.MinValue) - minDate.ToDateTime(TimeOnly.MinValue)).TotalDays / totalDays * plotWidth);

        // Zero is always forced into the visible range - the whole point of this chart is
        // seeing whether/when the balance would go negative, so that reference line must
        // always be meaningful even in a year that never dips below a large positive balance.
        var actualMin = forecast.Rows.Min(r => r.RunningBalance);
        var actualMax = forecast.Rows.Max(r => r.RunningBalance);
        var minBalance = Math.Min(0m, actualMin);
        var maxBalance = Math.Max(0m, actualMax);
        var range = maxBalance - minBalance;
        if (range == 0m) range = 1m;

        var paddedMin = minBalance - range * 0.1m;
        var paddedMax = maxBalance + range * 0.1m;
        var paddedRange = paddedMax - paddedMin;

        var plotHeight = height - TopMargin;
        double YFor(decimal balance) => TopMargin + (plotHeight - (double)((balance - paddedMin) / paddedRange) * plotHeight);

        var points = forecast.Rows.Select(r => new CashFlowChartPoint
        {
            Date = r.Date, Balance = r.RunningBalance, X = XFor(r.Date), Y = YFor(r.RunningBalance)
        }).ToList();

        var lowestPoint = forecast.LowestProjectedBalanceDate is { } lowestDate
            ? new CashFlowChartPoint
            {
                Date = lowestDate, Balance = forecast.LowestProjectedBalance,
                X = XFor(lowestDate), Y = YFor(forecast.LowestProjectedBalance)
            }
            : null;

        var lowestPointAnchor = lowestPoint switch
        {
            null => "middle",
            var p when (p.X - LeftAxisMargin) >= plotWidth * 0.85 => "end",
            var p when (p.X - LeftAxisMargin) <= plotWidth * 0.15 => "start",
            _ => "middle"
        };

        var monthTicks = new List<CashFlowChartTick>();
        var cursor = new DateOnly(minDate.Year, minDate.Month, 1).AddMonths(1);
        while (cursor <= maxDate)
        {
            monthTicks.Add(new CashFlowChartTick { X = XFor(cursor), Label = cursor.ToString("MMM") });
            cursor = cursor.AddMonths(1);
        }

        var yAxisTicks = Enumerable.Range(0, YAxisTickCount)
            .Select(i =>
            {
                var value = paddedMin + paddedRange * i / (YAxisTickCount - 1);
                return new CashFlowYAxisTick { Y = YFor(value), Label = value.ToString("N0") };
            })
            .ToList();

        // Least-squares fit of balance vs. elapsed days since the window's start.
        var n = forecast.Rows.Count;
        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
        foreach (var row in forecast.Rows)
        {
            var x = (row.Date.ToDateTime(TimeOnly.MinValue) - minDate.ToDateTime(TimeOnly.MinValue)).TotalDays;
            var y = (double)row.RunningBalance;
            sumX += x; sumY += y; sumXY += x * y; sumX2 += x * x;
        }

        var denominator = n * sumX2 - sumX * sumX;
        var slopePerDay = denominator == 0 ? 0m : (decimal)((n * sumXY - sumX * sumY) / denominator);
        var intercept = denominator == 0 ? (decimal)(sumY / n) : (decimal)((sumY - (double)slopePerDay * sumX) / n);

        var trendLineStart = (X: XFor(minDate), Y: YFor(intercept));
        var trendLineEnd = (X: XFor(maxDate), Y: YFor(intercept + slopePerDay * (decimal)totalDays));

        var movingAveragePoints = forecast.Rows.Select(r =>
        {
            var windowStart = r.Date.AddDays(-MovingAverageWindowDays);
            var windowEnd = r.Date.AddDays(MovingAverageWindowDays);
            var average = forecast.Rows
                .Where(other => other.Date >= windowStart && other.Date <= windowEnd)
                .Average(other => other.RunningBalance);

            return new CashFlowChartPoint { Date = r.Date, Balance = average, X = XFor(r.Date), Y = YFor(average) };
        }).ToList();

        return new CashFlowChartModel
        {
            Points = points,
            ZeroLineY = YFor(0m),
            LowestPoint = lowestPoint,
            LowestPointAnchor = lowestPointAnchor,
            MonthTicks = monthTicks,
            YAxisTicks = yAxisTicks,
            TrendSlopePerDay = slopePerDay,
            TrendLineStart = trendLineStart,
            TrendLineEnd = trendLineEnd,
            MovingAveragePoints = movingAveragePoints,
            Width = width,
            Height = height
        };
    }
}
