using Expense.Domain.Entities;

namespace Expense.Domain.Services.Forecast;

public class ForecastSnapshotDiff
{
    public List<ForecastLineChange> Changed { get; set; } = [];
    public List<ForecastSnapshotLine> Added { get; set; } = [];

    /// <summary>Lines missing from the newer snapshot with no matching reconciled
    /// transaction found - genuinely removed (a budget/rule change, a deleted one-time
    /// event, etc.), not resolved by something real. See Reconciled below for the other
    /// case.</summary>
    public List<ForecastSnapshotLine> Removed { get; set; } = [];

    /// <summary>Lines missing from the newer snapshot *because* a real transaction
    /// reconciled against them in the meantime - shown with the budgeted-vs-actual
    /// variance instead of as a bare removal, since that variance is real money affecting
    /// every downstream running balance. See docs/forecast-history-redesign-plan.md.</summary>
    public List<ReconciledLine> Reconciled { get; set; } = [];

    /// <summary>Null when the starting balance didn't change. Often the actual headline
    /// reason a minimum balance shifted - a line-level diff alone can't see it, since it's
    /// a top-level snapshot field, not a ledger row.</summary>
    public StartingBalanceChange? StartingBalanceChange { get; set; }
}

public class ReconciledLine
{
    public required string Description { get; set; }
    public int AccountId { get; set; }
    public DateOnly Date { get; set; }
    public required decimal BudgetedAmount { get; set; }
    public required decimal ActualAmount { get; set; }
    public decimal Variance => Math.Abs(ActualAmount) - Math.Abs(BudgetedAmount);
}

public class StartingBalanceChange
{
    public required decimal OldBalance { get; set; }
    public required decimal NewBalance { get; set; }
    public decimal Delta => NewBalance - OldBalance;

    /// <summary>The real checking-account transactions that explain Delta - see
    /// ForecastSnapshotDiffer.Diff. Empty (not null) when none were supplied, so callers can
    /// always iterate it directly.</summary>
    public List<StartingBalanceTransaction> Transactions { get; set; } = [];
}

public class StartingBalanceTransaction
{
    public required DateOnly Date { get; set; }
    public required string Description { get; set; }
    public required decimal Amount { get; set; }
}

public class ForecastLineChange
{
    public required string Description { get; set; }
    public int AccountId { get; set; }
    public DateOnly OldDate { get; set; }
    public DateOnly NewDate { get; set; }
    public decimal OldAmount { get; set; }
    public decimal NewAmount { get; set; }
}
