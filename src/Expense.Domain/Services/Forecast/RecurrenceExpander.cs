using Expense.Domain.Entities;

namespace Expense.Domain.Services.Forecast;

/// <summary>
/// Expands recurring_rules/one_time_events into dated ledger lines over a window.
/// Anchor is just a reference point the recurrence is computed from - occurrences can
/// fall before or after it, bounded only by the rule's own StartDate/EndDate and the
/// requested window.
/// </summary>
public class RecurrenceExpander
{
    /// <summary>
    /// The outer bound (in either direction) for reconciling a scheduled occurrence
    /// against a real matching transaction - see ForecastEngine's reconciliation step and
    /// MatchWindowDaysFor below.
    /// </summary>
    public const int MaxMatchWindowDays = 14;

    /// <summary>
    /// How many days before/after a scheduled occurrence a real transaction still counts
    /// as satisfying it - capped at half the frequency's own interval so the window can
    /// never reach into an adjacent occurrence (e.g. a Weekly bill can't have its window
    /// overlap next week's).
    /// </summary>
    public static int MatchWindowDaysFor(Frequency frequency) => Math.Min(MaxMatchWindowDays, MinIntervalDays(frequency) / 2);

    private static int MinIntervalDays(Frequency frequency) => frequency switch
    {
        Frequency.Weekly => 7,
        Frequency.Biweekly => 14,
        Frequency.Monthly => 28,
        Frequency.Quarterly => 90,
        Frequency.Annual => 365,
        _ => throw new ArgumentOutOfRangeException(nameof(frequency))
    };

    public List<LedgerLine> Expand(
        IEnumerable<RecurringRule> rules,
        IEnumerable<OneTimeEvent> events,
        DateOnly windowStart,
        DateOnly windowEnd)
    {
        var lines = new List<LedgerLine>();

        foreach (var rule in rules)
        {
            if (!rule.Active) continue;

            var rangeStart = rule.StartDate > windowStart ? rule.StartDate : windowStart;
            var rangeEnd = rule.EndDate is { } end && end < windowEnd ? end : windowEnd;
            if (rangeStart > rangeEnd) continue;

            var signedAmount = rule.Direction == Direction.Income ? rule.Amount : -rule.Amount;

            var matchWindowDays = MatchWindowDaysFor(rule.Frequency);
            foreach (var date in Occurrences(rule.Anchor, rule.Frequency, rangeStart, rangeEnd))
            {
                lines.Add(new LedgerLine
                {
                    Date = date, Description = rule.Name, Amount = signedAmount, AccountId = rule.AccountId,
                    CategoryId = rule.CategoryId, MatchWindowDays = matchWindowDays
                });
            }
        }

        foreach (var evt in events)
        {
            if (evt.Date < windowStart || evt.Date > windowEnd) continue;

            var signedAmount = evt.Direction == Direction.Income ? evt.Amount : -evt.Amount;
            lines.Add(new LedgerLine { Date = evt.Date, Description = evt.Name, Amount = signedAmount, AccountId = evt.AccountId, SourceOneTimeEventId = evt.Id });
        }

        return lines.OrderBy(l => l.Date).ToList();
    }

    private static IEnumerable<DateOnly> Occurrences(DateOnly anchor, Frequency frequency, DateOnly rangeStart, DateOnly rangeEnd)
    {
        var dates = new List<DateOnly>();

        var k = 0;
        while (true)
        {
            var date = Occurrence(anchor, frequency, k);
            if (date > rangeEnd) break;
            if (date >= rangeStart) dates.Add(date);
            k++;
        }

        k = -1;
        while (true)
        {
            var date = Occurrence(anchor, frequency, k);
            if (date < rangeStart) break;
            if (date <= rangeEnd) dates.Add(date);
            k--;
        }

        return dates;
    }

    private static DateOnly Occurrence(DateOnly anchor, Frequency frequency, int k) => frequency switch
    {
        Frequency.Weekly => anchor.AddDays(7 * k),
        Frequency.Biweekly => anchor.AddDays(14 * k),
        Frequency.Monthly => AddMonthsClamped(anchor, k),
        Frequency.Quarterly => AddMonthsClamped(anchor, k * 3),
        Frequency.Annual => AddMonthsClamped(anchor, k * 12),
        _ => throw new ArgumentOutOfRangeException(nameof(frequency))
    };

    private static DateOnly AddMonthsClamped(DateOnly date, int months)
    {
        var totalMonths = date.Year * 12 + (date.Month - 1) + months;
        var monthIndex = ((totalMonths % 12) + 12) % 12;
        var year = (totalMonths - monthIndex) / 12;
        var month = monthIndex + 1;
        var day = Math.Min(date.Day, DateTime.DaysInMonth(year, month));
        return new DateOnly(year, month, day);
    }
}
