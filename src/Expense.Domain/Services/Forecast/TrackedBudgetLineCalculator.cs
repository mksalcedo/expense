using Expense.Domain.Entities;

namespace Expense.Domain.Services.Forecast;

/// <summary>
/// Computes each period's forecasted amount for a TrackedBudget category funded from an
/// ordinary (non-statement-cycle) account - walked by the category's own anchor/frequency
/// instead of a statement's close/due days, with no separate "due later" step: the period's
/// own end date IS the line's date, since the money already left the account as it was spent,
/// not borrowed and paid back afterward like a credit card.
///
/// Deliberately NOT AmexCycleCalculator's MAX(actual, budget) floor - that formula is only
/// correct for a card, where a charge doesn't touch checking until a later, separate payment,
/// so showing the full cycle total as an upcoming deduction represents real money that hasn't
/// left yet. Here, real spending already reduced the real starting balance the moment it
/// posted, so the line must only project what's STILL expected before the period ends -
/// max(0, budget - actual) - or it double-counts money that's already gone (found live
/// 2026-08-19: a $222.43-vs-$195-budgeted week showed as an *additional* $222.43 forecasted,
/// on top of the $222.43 that had already left checking).
///
/// categoryTransactions should already be filtered to real, expense-side transactions for this
/// one category (same contract as AmexCycleCalculator's chargeTransactions - the caller
/// excludes payments/credits, not this).
/// </summary>
public class TrackedBudgetLineCalculator
{
    public List<TrackedBudgetLineResult> CalculatePeriods(
        DateOnly anchor,
        Frequency frequency,
        decimal budgetAmount,
        IReadOnlyList<BankTransaction> categoryTransactions,
        DateOnly asOfDate,
        DateOnly windowStart,
        DateOnly windowEnd)
    {
        var results = new List<TrackedBudgetLineResult>();

        foreach (var k in OccurrenceIndexes(anchor, frequency, windowStart, windowEnd))
        {
            var periodEnd = RecurrenceExpander.Occurrence(anchor, frequency, k);
            var periodStart = RecurrenceExpander.Occurrence(anchor, frequency, k - 1).AddDays(1);
            var isFuture = periodStart > asOfDate;

            var actualAmount = isFuture
                ? 0m
                : -categoryTransactions
                    .Where(t => (t.PostedDate ?? t.TransactionDate) >= periodStart && (t.PostedDate ?? t.TransactionDate) <= periodEnd)
                    .Sum(t => t.Amount);

            results.Add(new TrackedBudgetLineResult
            {
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                Amount = isFuture ? budgetAmount : Math.Max(0m, budgetAmount - actualAmount),
                IsFuture = isFuture,
                ActualAmount = actualAmount
            });
        }

        return results;
    }

    // Same forward/backward walk shape as RecurrenceExpander.Occurrences, but yielding the
    // index k (not just the date) - CalculatePeriods needs k-1 to find each period's own
    // start boundary, not just the flat list of end dates.
    private static IEnumerable<int> OccurrenceIndexes(DateOnly anchor, Frequency frequency, DateOnly rangeStart, DateOnly rangeEnd)
    {
        var indexes = new List<int>();

        var k = 0;
        while (true)
        {
            var date = RecurrenceExpander.Occurrence(anchor, frequency, k);
            if (date > rangeEnd) break;
            if (date >= rangeStart) indexes.Add(k);
            k++;
        }

        k = -1;
        while (true)
        {
            var date = RecurrenceExpander.Occurrence(anchor, frequency, k);
            if (date < rangeStart) break;
            if (date <= rangeEnd) indexes.Add(k);
            k--;
        }

        return indexes;
    }
}
