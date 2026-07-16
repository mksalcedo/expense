namespace Expense.Domain.Services.Forecast;

/// <summary>One Amex statement cycle's forecasted payment.</summary>
public class AmexCycleResult
{
    public required DateOnly CycleStart { get; set; }
    public required DateOnly CycleEnd { get; set; }
    public required DateOnly DueDate { get; set; }
    public required decimal Amount { get; set; }

    /// <summary>True if this cycle hasn't started yet, so Amount is the budget estimate alone (no actual data exists to compare against).</summary>
    public required bool IsFuture { get; set; }

    /// <summary>The actual qualifying-charges total for this cycle; 0 for a future cycle, since none has been computed.</summary>
    public required decimal ActualAmount { get; set; }
}
