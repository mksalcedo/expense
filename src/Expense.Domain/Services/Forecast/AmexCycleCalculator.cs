using Expense.Domain.Entities;

namespace Expense.Domain.Services.Forecast;

/// <summary>
/// Computes each Amex statement cycle's forecasted payment. A cycle that hasn't
/// started yet uses the budgeted total only (there's no actual data). A cycle already
/// in progress or closed uses MAX(actual charges, budgeted total) - the forecast never
/// gets optimistic from underspending; real savings only show up later via the next
/// actual checking-balance refresh. chargeTransactions should be every real charge on
/// the account (not filtered by category) - the caller is responsible for excluding
/// payments/credits before passing them in.
/// </summary>
public class AmexCycleCalculator
{
    public List<AmexCycleResult> CalculateDuePayments(
        int statementCloseDay,
        int paymentDueDay,
        decimal extraPrincipal,
        decimal monthlyBudgetTotal,
        IReadOnlyList<BankTransaction> chargeTransactions,
        DateOnly asOfDate,
        DateOnly windowStart,
        DateOnly windowEnd)
    {
        var results = new List<AmexCycleResult>();

        var cursor = new DateOnly(windowStart.AddMonths(-2).Year, windowStart.AddMonths(-2).Month, 1);
        var cursorEnd = new DateOnly(windowEnd.AddMonths(1).Year, windowEnd.AddMonths(1).Month, 1);

        while (cursor <= cursorEnd)
        {
            var closeDate = ClampedDate(cursor.Year, cursor.Month, statementCloseDay);
            var dueDate = NextOccurrenceOfDay(closeDate.AddDays(1), paymentDueDay);

            if (dueDate >= windowStart && dueDate <= windowEnd)
            {
                var (prevYear, prevMonth) = AddMonth(cursor.Year, cursor.Month, -1);
                var cycleStart = ClampedDate(prevYear, prevMonth, statementCloseDay).AddDays(1);

                var isFuture = cycleStart > asOfDate;
                decimal amount;
                decimal actualAmount = 0m;
                if (isFuture)
                {
                    amount = monthlyBudgetTotal + extraPrincipal;
                }
                else
                {
                    actualAmount = -chargeTransactions
                        .Where(t => t.PostedDate is { } posted && posted >= cycleStart && posted <= closeDate)
                        .Sum(t => t.Amount);
                    amount = Math.Max(actualAmount, monthlyBudgetTotal) + extraPrincipal;
                }

                results.Add(new AmexCycleResult
                {
                    CycleStart = cycleStart, CycleEnd = closeDate, DueDate = dueDate, Amount = amount,
                    IsFuture = isFuture, ActualAmount = actualAmount
                });
            }

            var (nextYear, nextMonth) = AddMonth(cursor.Year, cursor.Month, 1);
            cursor = new DateOnly(nextYear, nextMonth, 1);
        }

        return results;
    }

    private static DateOnly NextOccurrenceOfDay(DateOnly onOrAfter, int day)
    {
        var candidate = ClampedDate(onOrAfter.Year, onOrAfter.Month, day);
        if (candidate >= onOrAfter) return candidate;

        var (year, month) = AddMonth(onOrAfter.Year, onOrAfter.Month, 1);
        return ClampedDate(year, month, day);
    }

    private static DateOnly ClampedDate(int year, int month, int day) =>
        new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));

    private static (int Year, int Month) AddMonth(int year, int month, int delta)
    {
        var total = year * 12 + (month - 1) + delta;
        var monthIndex = ((total % 12) + 12) % 12;
        var y = (total - monthIndex) / 12;
        return (y, monthIndex + 1);
    }
}
