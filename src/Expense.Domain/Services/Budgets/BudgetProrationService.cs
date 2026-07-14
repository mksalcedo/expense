using Expense.Domain.Entities;

namespace Expense.Domain.Services.Budgets;

/// <summary>
/// Converts a budget amount entered in any frequency into whatever period a caller
/// actually needs, via a canonical daily rate. Used everywhere a budget figure needs
/// to appear in a different period than it was entered in: the Spending Tracker's
/// week/month views, the Amex forecast's monthly "qualifying charges" estimate, and
/// Historical Analysis's budget-vs-actual comparisons.
/// </summary>
public class BudgetProrationService
{
    private const decimal DaysPerYear = 365.25m;

    private static decimal DaysIn(Frequency frequency) => frequency switch
    {
        Frequency.Weekly => 7m,
        Frequency.Biweekly => 14m,
        Frequency.Monthly => DaysPerYear / 12m,
        Frequency.Quarterly => DaysPerYear / 4m,
        Frequency.Annual => DaysPerYear,
        _ => throw new ArgumentOutOfRangeException(nameof(frequency), frequency, null)
    };

    public decimal ToDailyRate(decimal amount, Frequency frequency) => amount / DaysIn(frequency);

    public decimal Convert(decimal amount, Frequency fromFrequency, Frequency toFrequency)
    {
        if (fromFrequency == toFrequency) return amount;
        return ToDailyRate(amount, fromFrequency) * DaysIn(toFrequency);
    }
}
