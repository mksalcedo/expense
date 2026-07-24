using Expense.Domain.Services.Scheduling;

namespace Expense.Domain.Tests.Services.Scheduling;

public class ScheduledSyncTimeCalculatorTests
{
    private static readonly List<TimeOnly> SixAmAndThreePm = [new TimeOnly(6, 0), new TimeOnly(15, 0)];

    [Fact]
    public void GetNextRunTime_ReturnsTheEarliestTimeToday_WhenBeforeIt()
    {
        var now = new DateTimeOffset(2026, 7, 22, 3, 0, 0, TimeSpan.Zero);

        var next = ScheduledSyncTimeCalculator.GetNextRunTime(now, SixAmAndThreePm);

        Assert.Equal(new DateTimeOffset(2026, 7, 22, 6, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void GetNextRunTime_ReturnsTheLaterTimeToday_WhenBetweenTheTwoScheduledTimes()
    {
        var now = new DateTimeOffset(2026, 7, 22, 10, 0, 0, TimeSpan.Zero);

        var next = ScheduledSyncTimeCalculator.GetNextRunTime(now, SixAmAndThreePm);

        Assert.Equal(new DateTimeOffset(2026, 7, 22, 15, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void GetNextRunTime_RollsOverToTomorrowsEarliestTime_WhenAfterTheLastScheduledTimeToday()
    {
        var now = new DateTimeOffset(2026, 7, 22, 20, 0, 0, TimeSpan.Zero);

        var next = ScheduledSyncTimeCalculator.GetNextRunTime(now, SixAmAndThreePm);

        Assert.Equal(new DateTimeOffset(2026, 7, 23, 6, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void GetNextRunTime_HandlesUnsortedInputTimes()
    {
        var unsorted = new List<TimeOnly> { new(15, 0), new(6, 0) };
        var now = new DateTimeOffset(2026, 7, 22, 3, 0, 0, TimeSpan.Zero);

        var next = ScheduledSyncTimeCalculator.GetNextRunTime(now, unsorted);

        Assert.Equal(new DateTimeOffset(2026, 7, 22, 6, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void GetNextRunTime_WhenExactlyAtAScheduledInstant_TreatsItAsAlreadyPassed()
    {
        var now = new DateTimeOffset(2026, 7, 22, 6, 0, 0, TimeSpan.Zero);

        var next = ScheduledSyncTimeCalculator.GetNextRunTime(now, SixAmAndThreePm);

        Assert.Equal(new DateTimeOffset(2026, 7, 22, 15, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void GetNextRunTime_PreservesTheOffsetOfNow()
    {
        var now = new DateTimeOffset(2026, 7, 22, 3, 0, 0, TimeSpan.FromHours(-4));

        var next = ScheduledSyncTimeCalculator.GetNextRunTime(now, SixAmAndThreePm);

        Assert.Equal(TimeSpan.FromHours(-4), next.Offset);
    }
}
