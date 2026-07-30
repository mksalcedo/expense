using Expense.Domain.Data;
using Expense.Domain.Entities;

namespace Expense.Domain.Services.Forecast;

/// <summary>
/// Captures a lightweight snapshot of the current forecast's key figures on every call, so
/// a future swing in the lowest projected balance can be explained by diffing against a
/// prior snapshot instead of trying to reconstruct past forecast state after the fact.
/// One row per capture, not per calendar day - captures happen after every successful
/// sync (SimpleFin/Amazon/Plaid, scheduled or manual), and with Plaid now scheduled
/// alongside the others, several real captures happen most days. Upserting by AsOfDate
/// used to silently destroy every earlier same-day capture, making any intraday swing
/// invisible by construction (found live 2026-07-29 - see
/// docs/forecast-history-redesign-plan.md).
/// </summary>
public class ForecastSnapshotService
{
    // The far tail of a 12-month forecast is both less actionable and would bloat storage
    // for no real diffing benefit - only the near-term window is worth persisting daily.
    private const int SnapshotWindowDays = 120;

    public async Task CaptureAsync(ExpenseDbContext context, ForecastResult forecast, DateOnly asOfDate, CancellationToken cancellationToken = default)
    {
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
                    Date = r.Date, Description = r.Description, Amount = r.Amount, RunningBalance = r.RunningBalance,
                    AccountId = r.AccountId, CategoryId = r.CategoryId
                })
                .ToList()
        };
        context.ForecastSnapshots.Add(snapshot);
        await context.SaveChangesAsync(cancellationToken);
    }
}
