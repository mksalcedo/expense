using Expense.Domain.Entities;
using Expense.Domain.Services.Forecast;

namespace Expense.Domain.Tests.Services.Forecast;

public class RecurrenceExpanderTests
{
    private readonly RecurrenceExpander _sut = new();

    private static RecurringRule MakeRule(
        Direction direction, decimal amount, Frequency frequency, DateOnly anchor,
        DateOnly? startDate = null, DateOnly? endDate = null, bool active = true, string name = "Rule") => new()
    {
        Name = name,
        Direction = direction,
        Amount = amount,
        Frequency = frequency,
        Anchor = anchor,
        AccountId = 1,
        Active = active,
        StartDate = startDate ?? anchor,
        EndDate = endDate
    };

    [Fact]
    public void Expand_WeeklyRule_ProducesOneLinePerSevenDays()
    {
        var rule = MakeRule(Direction.Income, 1000m, Frequency.Weekly, new DateOnly(2026, 1, 4)); // a Sunday

        var lines = _sut.Expand([rule], [], new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        var dates = lines.Select(l => l.Date).ToList();
        Assert.Equal(
        [
            new DateOnly(2026, 1, 4),
            new DateOnly(2026, 1, 11),
            new DateOnly(2026, 1, 18),
            new DateOnly(2026, 1, 25)
        ], dates);
        Assert.All(lines, l => Assert.Equal(1000m, l.Amount)); // income is positive
    }

    [Fact]
    public void Expand_BiweeklyRule_ProducesOneLineEveryFourteenDays()
    {
        var rule = MakeRule(Direction.Income, 2000m, Frequency.Biweekly, new DateOnly(2026, 1, 2));

        var lines = _sut.Expand([rule], [], new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 28));

        var dates = lines.Select(l => l.Date).ToList();
        Assert.Equal(
        [
            new DateOnly(2026, 1, 2),
            new DateOnly(2026, 1, 16),
            new DateOnly(2026, 1, 30),
            new DateOnly(2026, 2, 13),
            new DateOnly(2026, 2, 27)
        ], dates);
    }

    [Fact]
    public void Expand_MonthlyRule_UsesAnchorDayOfMonthEachMonth()
    {
        var rule = MakeRule(Direction.Expense, 150m, Frequency.Monthly, new DateOnly(2026, 1, 15));

        var lines = _sut.Expand([rule], [], new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 30));

        var dates = lines.Select(l => l.Date).ToList();
        Assert.Equal(
        [
            new DateOnly(2026, 1, 15),
            new DateOnly(2026, 2, 15),
            new DateOnly(2026, 3, 15),
            new DateOnly(2026, 4, 15)
        ], dates);
        Assert.All(lines, l => Assert.Equal(-150m, l.Amount)); // expense is negative
    }

    [Fact]
    public void Expand_MonthlyRule_ClampsToLastDayOfShorterMonths()
    {
        // Anchor on the 31st - Feb (28 days in 2026) and April (30 days) must clamp
        var rule = MakeRule(Direction.Expense, 100m, Frequency.Monthly, new DateOnly(2026, 1, 31));

        var lines = _sut.Expand([rule], [], new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 30));

        var dates = lines.Select(l => l.Date).ToList();
        Assert.Equal(
        [
            new DateOnly(2026, 1, 31),
            new DateOnly(2026, 2, 28),
            new DateOnly(2026, 3, 31),
            new DateOnly(2026, 4, 30)
        ], dates);
    }

    [Fact]
    public void Expand_QuarterlyRule_ProducesOneLineEveryThreeMonths()
    {
        var rule = MakeRule(Direction.Expense, 300m, Frequency.Quarterly, new DateOnly(2026, 1, 10));

        var lines = _sut.Expand([rule], [], new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        var dates = lines.Select(l => l.Date).ToList();
        Assert.Equal(
        [
            new DateOnly(2026, 1, 10),
            new DateOnly(2026, 4, 10),
            new DateOnly(2026, 7, 10),
            new DateOnly(2026, 10, 10)
        ], dates);
    }

    [Fact]
    public void Expand_AnnualRule_ProducesOneLinePerYear()
    {
        var rule = MakeRule(Direction.Expense, 1200m, Frequency.Annual, new DateOnly(2025, 6, 1));

        var lines = _sut.Expand([rule], [], new DateOnly(2026, 1, 1), new DateOnly(2027, 12, 31));

        var dates = lines.Select(l => l.Date).ToList();
        Assert.Equal([new DateOnly(2026, 6, 1), new DateOnly(2027, 6, 1)], dates);
    }

    [Fact]
    public void Expand_InactiveRule_ProducesNoLines()
    {
        var rule = MakeRule(Direction.Income, 1000m, Frequency.Weekly, new DateOnly(2026, 1, 4), active: false);

        var lines = _sut.Expand([rule], [], new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        Assert.Empty(lines);
    }

    [Fact]
    public void Expand_RespectsStartDateAndEndDate()
    {
        var rule = MakeRule(
            Direction.Income, 1000m, Frequency.Weekly, new DateOnly(2026, 1, 4),
            startDate: new DateOnly(2026, 1, 11), endDate: new DateOnly(2026, 1, 18));

        var lines = _sut.Expand([rule], [], new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        var dates = lines.Select(l => l.Date).ToList();
        Assert.Equal([new DateOnly(2026, 1, 11), new DateOnly(2026, 1, 18)], dates);
    }

    [Fact]
    public void Expand_OneTimeEventWithinWindow_ProducesOneLine()
    {
        var evt = new OneTimeEvent { Name = "HVAC repair", Amount = 850m, Direction = Direction.Expense, Date = new DateOnly(2026, 3, 5), AccountId = 1 };

        var lines = _sut.Expand([], [evt], new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        var line = Assert.Single(lines);
        Assert.Equal(new DateOnly(2026, 3, 5), line.Date);
        Assert.Equal(-850m, line.Amount);
        Assert.Equal("HVAC repair", line.Description);
    }

    [Fact]
    public void Expand_OneTimeEventOutsideWindow_ProducesNoLines()
    {
        var evt = new OneTimeEvent { Name = "Past event", Amount = 100m, Direction = Direction.Income, Date = new DateOnly(2025, 1, 1), AccountId = 1 };

        var lines = _sut.Expand([], [evt], new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        Assert.Empty(lines);
    }
}
