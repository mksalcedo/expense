namespace Expense.Domain.Services.Scheduling;

/// <summary>Pure calculation of "when should the next scheduled sync run" - kept separate from SyncScheduler's real background timer loop so it can be unit tested without needing to run a real BackgroundService.</summary>
public static class ScheduledSyncTimeCalculator
{
    /// <summary>
    /// Returns the soonest of the given times-of-day that is strictly after now, rolling
    /// over to the earliest time on the following day if now is at or past every time
    /// today. Operates on now's own offset, so the caller is responsible for passing real
    /// local time - there is deliberately no time zone handling here for a single-laptop
    /// personal app.
    /// </summary>
    public static DateTimeOffset GetNextRunTime(DateTimeOffset now, IReadOnlyList<TimeOnly> dailyTimesLocal)
    {
        var today = DateOnly.FromDateTime(now.DateTime);
        var candidatesToday = dailyTimesLocal
            .Select(t => new DateTimeOffset(today.Year, today.Month, today.Day, t.Hour, t.Minute, t.Second, now.Offset))
            .Where(candidate => candidate > now)
            .OrderBy(candidate => candidate);

        if (candidatesToday.Any())
        {
            return candidatesToday.First();
        }

        var tomorrow = today.AddDays(1);
        var earliestTime = dailyTimesLocal.OrderBy(t => t).First();
        return new DateTimeOffset(tomorrow.Year, tomorrow.Month, tomorrow.Day, earliestTime.Hour, earliestTime.Minute, earliestTime.Second, now.Offset);
    }
}
