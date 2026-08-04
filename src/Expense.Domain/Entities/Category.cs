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
}
