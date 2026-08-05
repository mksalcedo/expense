namespace Expense.Domain.Entities;

public class Category
{
    public int Id { get; set; }
    public required string Name { get; set; }

    // Not hard-deleted on removal - deactivated, to preserve historical transactions/reports
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// True for a category whose real payments are expected to spread across the whole
    /// month rather than cluster near one due date (e.g. Piano - several different payers on
    /// their own schedules), instead of a single bill/paycheck near its anchor date. Changes
    /// how TransactionReconciliationService picks which occurrence a transaction belongs to:
    /// by the transaction's own calendar month first, rather than nearest-anchor-by-distance
    /// (which pulls anything posted in the back half of a month into next month's occurrence
    /// - found live 2026-08-04, real Piano payments from late July were misattributed to
    /// August). Left false (the exact prior behavior) for genuine single-payment bills, where
    /// nearest-by-distance correctly handles an early/late payment crossing a month boundary.
    /// </summary>
    public bool ReconcileByCalendarMonth { get; set; }

    /// <summary>
    /// Spending Tracker carryover: how many multiples of a period's own budget the rolling
    /// carried-forward balance is allowed to reach, in either direction (surplus or deficit),
    /// before it stops growing. Null means uncapped. Defaults to 1.0 - a category can bank at
    /// most one extra period's worth of surplus, and a bad stretch never compounds past one
    /// period's worth of deficit either. Set higher (or null) for a category the user
    /// deliberately wants to save up in across several periods (e.g. Clothing, ahead of a big
    /// purchase) - see CarryoverCalculator.
    /// </summary>
    public decimal? CarryoverCapMultiplier { get; set; } = 1.0m;

    /// <summary>
    /// The date whose containing period is "period zero" for carryover purposes - no rolling
    /// balance is carried into that period from anything earlier. Reset via the Spending
    /// Tracker's "reset carryover" action ("starting this period" sets this directly; "starting
    /// next period" instead sets PendingCarryoverAnchorDate, so the period already in progress
    /// keeps whatever it already carried). Defaults to the date this column was introduced, so
    /// carryover starts accumulating from launch, not retroactively over pre-existing history.
    /// </summary>
    public DateOnly CarryoverAnchorDate { get; set; }

    /// <summary>
    /// Set only by "reset carryover starting next period" - the still-in-progress current
    /// period is left untouched, and this becomes the new CarryoverAnchorDate once its own
    /// period actually begins. Resolved at read time (see CarryoverCalculator); never written
    /// back into CarryoverAnchorDate automatically.
    /// </summary>
    public DateOnly? PendingCarryoverAnchorDate { get; set; }
}
