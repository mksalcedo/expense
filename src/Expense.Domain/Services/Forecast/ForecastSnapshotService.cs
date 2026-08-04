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
    // Persists the full forecast, not just a near-term window - a diff only ever reports
    // lines that genuinely differ between two snapshots (see ForecastSnapshotDiffer), so
    // this doesn't mean showing more, it means having the data to notice a change wherever
    // it actually falls, including the far tail (confirmed real: a lowest-balance date a
    // year out, with only a ~4-month window previously captured, left nothing on either
    // side to diff near the date that actually mattered). forecast.Rows is already bounded
    // by the app's real forecast horizon (AppSettings.ForecastHorizonMonths), so no separate
    // cutoff is needed here.
    public async Task CaptureAsync(ExpenseDbContext context, ForecastResult forecast, DateOnly asOfDate, CancellationToken cancellationToken = default)
    {
        var snapshot = new ForecastSnapshot
        {
            AsOfDate = asOfDate,
            StartingBalance = forecast.StartingBalance,
            LowestProjectedBalance = forecast.LowestProjectedBalance,
            LowestProjectedBalanceDate = forecast.LowestProjectedBalanceDate,
            CapturedAt = DateTimeOffset.UtcNow,
            Lines = forecast.Rows
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
