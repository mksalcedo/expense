using Expense.Domain.Data;
using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Forecast;

/// <summary>
/// Captures (upserting per calendar day) a lightweight snapshot of the current forecast's
/// key figures, so a future swing in the lowest projected balance can be explained by
/// diffing against a prior day's snapshot instead of trying to reconstruct past forecast
/// state after the fact.
/// </summary>
public class ForecastSnapshotService
{
    // The far tail of a 12-month forecast is both less actionable and would bloat storage
    // for no real diffing benefit - only the near-term window is worth persisting daily.
    private const int SnapshotWindowDays = 120;

    public async Task CaptureAsync(ExpenseDbContext context, ForecastResult forecast, DateOnly asOfDate, CancellationToken cancellationToken = default)
    {
        var existing = await context.ForecastSnapshots
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.AsOfDate == asOfDate, cancellationToken);
        if (existing is not null)
        {
            context.ForecastSnapshots.Remove(existing);
            await context.SaveChangesAsync(cancellationToken);
        }

        var windowEnd = asOfDate.AddDays(SnapshotWindowDays);
        var snapshot = new ForecastSnapshot
        {
            AsOfDate = asOfDate,
            StartingBalance = forecast.StartingBalance,
            LowestProjectedBalance = forecast.LowestProjectedBalance,
            LowestProjectedBalanceDate = forecast.LowestProjectedBalanceDate,
            CapturedAt = DateTimeOffset.UtcNow,
            Lines = forecast.Rows
                .Where(r => r.Date <= windowEnd)
                .Select(r => new ForecastSnapshotLine
                {
                    Date = r.Date, Description = r.Description, Amount = r.Amount, RunningBalance = r.RunningBalance, AccountId = r.AccountId
                })
                .ToList()
        };
        context.ForecastSnapshots.Add(snapshot);
        await context.SaveChangesAsync(cancellationToken);
    }
}
