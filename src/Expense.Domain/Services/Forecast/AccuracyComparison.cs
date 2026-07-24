namespace Expense.Domain.Services.Forecast;

/// <summary>One recurring obligation's assumed amount/date vs. what actually posted (or didn't).</summary>
public class AccuracyComparison
{
    public required string Name { get; set; }
    public int AccountId { get; set; }
    public required DateOnly ScheduledDate { get; set; }
    public required decimal ScheduledAmount { get; set; }
    public DateOnly? ActualDate { get; set; }
    public decimal? ActualAmount { get; set; }

    public bool WasMatched => ActualAmount is not null;
    public decimal? Delta => ActualAmount is null ? null : ActualAmount - ScheduledAmount;
    public decimal? DeltaPercent => ActualAmount is null || ScheduledAmount == 0 ? null : Math.Round((Delta!.Value / ScheduledAmount) * 100, 1);
}
