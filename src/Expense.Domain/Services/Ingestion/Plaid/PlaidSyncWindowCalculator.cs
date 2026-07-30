namespace Expense.Domain.Services.Ingestion.Plaid;

/// <summary>
/// Pure calculation of the start date for an incremental scheduled Plaid sync window -
/// kept separate from SyncStatusProvider's real ImportRun lookup so it can be unit tested
/// without a database. Mirrors AmazonGmailSyncService's own incremental-window pattern:
/// OverlapDays before the last successful run. No separate bootstrap-window special case
/// (unlike Amazon's 400-day fallback) - when there's no prior successful run, this falls
/// back to just OverlapDays before now. The manual date-range picker on the Import Data
/// page is the deliberate safety net for anything a narrow window might miss, not a wider
/// automatic fallback - the user's own call, since it holds regardless of any other
/// source's state. Both inputs are expected in UTC (matching how ImportRun.RanAt is
/// always stored) - uses the DateTimeOffset's own date component directly rather than
/// converting to local time, so the result doesn't depend on the caller's time zone.
/// </summary>
public static class PlaidSyncWindowCalculator
{
    public const int OverlapDays = 7;

    public static DateOnly GetWindowStartDate(DateTimeOffset? lastSuccessfulRunAt, DateTimeOffset now) =>
        DateOnly.FromDateTime((lastSuccessfulRunAt ?? now).AddDays(-OverlapDays).DateTime);
}
