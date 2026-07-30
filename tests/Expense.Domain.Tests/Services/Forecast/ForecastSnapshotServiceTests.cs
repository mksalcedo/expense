using Expense.Domain.Services.Forecast;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Services.Forecast;

public class ForecastSnapshotServiceTests : DatabaseTestBase
{
    private readonly ForecastSnapshotService _sut = new();

    private static ForecastResult MakeForecast(decimal starting, params ForecastLedgerRow[] rows) => new()
    {
        StartingBalance = starting, Rows = rows.ToList()
    };

    [Fact]
    public async Task CaptureAsync_NewDay_CreatesASnapshotWithItsLines()
    {
        var forecast = MakeForecast(1000m,
            new ForecastLedgerRow { Date = new DateOnly(2026, 7, 25), Description = "GPC", Amount = -432m, RunningBalance = 568m, AccountId = 1 });

        await _sut.CaptureAsync(Context, forecast, new DateOnly(2026, 7, 23));

        var reloaded = await Context.ForecastSnapshots.SingleAsync(s => s.AsOfDate == new DateOnly(2026, 7, 23));
        Assert.Equal(1000m, reloaded.StartingBalance);
        Assert.Equal(568m, reloaded.LowestProjectedBalance);
    }

    [Fact]
    public async Task CaptureAsync_SameDayTwice_KeepsBothCaptures_NotOnlyTheLatest()
    {
        // Real bug this guards (found live 2026-07-29): upserting by AsOfDate meant every
        // capture the same day silently destroyed the previous one - with Plaid now
        // scheduled alongside SimpleFin/Amazon, several real captures happen most days,
        // and any intraday swing (the actual point of Forecast History) was invisible by
        // construction, regardless of whether the earlier value was even correct.
        var morning = MakeForecast(1000m,
            new ForecastLedgerRow { Date = new DateOnly(2026, 7, 25), Description = "GPC", Amount = -432m, RunningBalance = 568m, AccountId = 1 });
        await _sut.CaptureAsync(Context, morning, new DateOnly(2026, 7, 23));

        var afternoon = MakeForecast(1200m,
            new ForecastLedgerRow { Date = new DateOnly(2026, 7, 25), Description = "GPC", Amount = -432m, RunningBalance = 768m, AccountId = 1 });
        await _sut.CaptureAsync(Context, afternoon, new DateOnly(2026, 7, 23));

        var snapshots = await Context.ForecastSnapshots.Where(s => s.AsOfDate == new DateOnly(2026, 7, 23)).OrderBy(s => s.CapturedAt).ToListAsync();
        Assert.Equal(2, snapshots.Count);
        Assert.Equal(1000m, snapshots[0].StartingBalance);
        Assert.Equal(1200m, snapshots[1].StartingBalance);
    }

    [Fact]
    public async Task CaptureAsync_OnlyPersistsLinesWithinTheNearTermWindow()
    {
        var forecast = MakeForecast(1000m,
            new ForecastLedgerRow { Date = new DateOnly(2026, 7, 25), Description = "Near", Amount = -100m, RunningBalance = 900m, AccountId = 1 },
            new ForecastLedgerRow { Date = new DateOnly(2027, 6, 1), Description = "Far", Amount = -100m, RunningBalance = 800m, AccountId = 1 });

        await _sut.CaptureAsync(Context, forecast, new DateOnly(2026, 7, 23));

        var reloaded = await Context.ForecastSnapshots.Include(s => s.Lines).SingleAsync(s => s.AsOfDate == new DateOnly(2026, 7, 23));
        Assert.Contains(reloaded.Lines, l => l.Description == "Near");
        Assert.DoesNotContain(reloaded.Lines, l => l.Description == "Far");
    }
}
