namespace Expense.Domain.Services.Forecast;

public class TrackedBudgetLineResult
{
    public required DateOnly PeriodStart { get; set; }
    public required DateOnly PeriodEnd { get; set; }

    /// <summary>The remaining, not-yet-spent portion of the budget still projected before the
    /// period ends - max(0, budget - ActualAmount) for a started period, or the plain budget
    /// for a future one. Real spending already reduced the real account balance the moment it
    /// posted, so this must never re-add what's already gone.</summary>
    public required decimal Amount { get; set; }

    /// <summary>True if this period hasn't started yet, so Amount is the budget estimate alone (no actual data exists to compare against).</summary>
    public required bool IsFuture { get; set; }

    /// <summary>The actual qualifying-transactions total for this period; 0 for a future period, since none has been computed.</summary>
    public required decimal ActualAmount { get; set; }
}
