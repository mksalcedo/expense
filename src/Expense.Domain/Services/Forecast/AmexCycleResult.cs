namespace Expense.Domain.Services.Forecast;

/// <summary>One Amex statement cycle's forecasted payment.</summary>
public class AmexCycleResult
{
    public required DateOnly CycleStart { get; set; }
    public required DateOnly CycleEnd { get; set; }
    public required DateOnly DueDate { get; set; }
    public required decimal Amount { get; set; }
}
