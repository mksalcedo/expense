namespace Expense.Domain.Entities;

/// <summary>
/// Shared between BudgetPeriod and RecurringRule - both represent the same underlying
/// concept of "how often does this amount apply/recur."
/// </summary>
public enum Frequency
{
    Weekly,
    Biweekly,
    Monthly,
    Quarterly,
    Annual
}
