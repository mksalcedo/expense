namespace Expense.Domain.Services.SpendingTracker;

/// <summary>
/// Pure rolling-balance math for Spending Tracker carryover - no DB access, callers assemble
/// the ordered per-period activity first (see SpendingTrackerService). The cap is a real
/// ceiling on the running state, applied after every period, not a display clamp computed
/// once at the end - once a surplus (or deficit) hits the cap, further periods in the same
/// direction don't keep silently banking past it (see CarryoverCalculatorTests for why this
/// matters: sum-then-clamp-once gives a different, wrong answer whenever a period ever
/// exceeds the cap and a later period pulls back the other way).
/// </summary>
public static class CarryoverCalculator
{
    public readonly record struct PeriodActivity(decimal Budget, decimal Actual);

    /// <param name="CarriedIn">The rolling balance carried into the final period, before that period's own budget/actual was applied.</param>
    /// <param name="RollingBalance">The resulting rolling balance as of the final period - what the Spending Tracker shows as Remaining.</param>
    /// <param name="Cap">The cap magnitude that applied to the final period (capMultiplier x that period's own budget), or null if uncapped.</param>
    public readonly record struct Result(decimal CarriedIn, decimal RollingBalance, decimal? Cap);

    /// <param name="periodsInOrder">Chronological, from the anchor period through the current period (inclusive).</param>
    /// <param name="capMultiplier">Null means uncapped in both directions.</param>
    public static Result Compute(IReadOnlyList<PeriodActivity> periodsInOrder, decimal? capMultiplier)
    {
        if (periodsInOrder.Count == 0)
        {
            throw new ArgumentException("At least one period is required.", nameof(periodsInOrder));
        }

        var rollingBalance = 0m;
        var carriedIn = 0m;
        decimal? cap = null;

        foreach (var period in periodsInOrder)
        {
            carriedIn = rollingBalance;
            var uncapped = carriedIn + (period.Budget - period.Actual);
            cap = capMultiplier is { } multiplier ? multiplier * period.Budget : null;
            rollingBalance = cap is { } c ? Math.Clamp(uncapped, -c, c) : uncapped;
        }

        return new Result(carriedIn, rollingBalance, cap);
    }
}
