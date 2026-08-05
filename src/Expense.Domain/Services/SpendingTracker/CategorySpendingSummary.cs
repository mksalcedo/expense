namespace Expense.Domain.Services.SpendingTracker;

public class CategorySpendingSummary
{
    public required int CategoryId { get; set; }
    public required string CategoryName { get; set; }
    public required decimal Budget { get; set; }
    public required decimal Actual { get; set; }
    public decimal Remaining => Budget - Actual;

    /// <summary>
    /// True on whichever of the week/month views matches this category's own budgeted
    /// Frequency (Weekly categories carry over on the week view, everything else on the
    /// month view) - see SpendingTrackerService.DetermineNativeFrequency. The other view
    /// always shows false here, with a plain this-period-only Remaining, unchanged from
    /// before carryover existed.
    /// </summary>
    public bool IsCarryoverTracked { get; set; }

    /// <summary>The rolling balance carried into this period from before it started - 0 if this is the anchor period.</summary>
    public decimal CarriedIn { get; set; }

    /// <summary>The carryover-adjusted Remaining - what the Spending Tracker actually displays when IsCarryoverTracked.</summary>
    public decimal? RollingBalance { get; set; }

    /// <summary>The cap magnitude that applied this period (CarryoverCapMultiplier x this period's own budget), or null if uncapped.</summary>
    public decimal? CarryoverCap { get; set; }
}
