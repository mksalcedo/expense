using Expense.Domain.Entities;
using Expense.Domain.Services.Forecast;

namespace Expense.Domain.Tests.Services.Forecast;

public class TrackedBudgetLineCalculatorTests
{
    private readonly TrackedBudgetLineCalculator _sut = new();

    private static BankTransaction Charge(DateOnly postedDate, decimal amount) => new()
    {
        AccountId = 1,
        TransactionDate = postedDate,
        PostedDate = postedDate,
        Description = "charge",
        Amount = amount, // negative = an expense, matching real bank-statement sign convention
        ImportSource = "Test",
        CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public void FuturePeriod_HasNoActualData_UsesBudgetAloneRegardlessOfTransactions()
    {
        // Anchor Wednesday 2026-08-19, weekly - the period ending 2026-09-02 is entirely in
        // the future relative to asOfDate, so it must ignore the (implausible, but present)
        // transaction and use the budget alone.
        var results = _sut.CalculatePeriods(
            anchor: new DateOnly(2026, 8, 19), frequency: Frequency.Weekly, budgetAmount: 195m,
            categoryTransactions: [Charge(new DateOnly(2026, 8, 28), -500m)],
            asOfDate: new DateOnly(2026, 8, 19),
            windowStart: new DateOnly(2026, 9, 1), windowEnd: new DateOnly(2026, 9, 2));

        var result = Assert.Single(results);
        Assert.True(result.IsFuture);
        Assert.Equal(195m, result.Amount);
        Assert.Equal(0m, result.ActualAmount);
    }

    [Fact]
    public void WeeklyPeriodBoundaries_RunFromTheDayAfterThePreviousOccurrenceThroughThisOne()
    {
        // Anchor 2026-08-19 (Wed), weekly - the period ending 2026-08-19 itself should run
        // 2026-08-13 (the day after the prior Wed 08-12) through 2026-08-19 inclusive.
        var results = _sut.CalculatePeriods(
            anchor: new DateOnly(2026, 8, 19), frequency: Frequency.Weekly, budgetAmount: 195m,
            categoryTransactions: [],
            asOfDate: new DateOnly(2026, 8, 19),
            windowStart: new DateOnly(2026, 8, 19), windowEnd: new DateOnly(2026, 8, 19));

        var result = Assert.Single(results);
        Assert.Equal(new DateOnly(2026, 8, 13), result.PeriodStart);
        Assert.Equal(new DateOnly(2026, 8, 19), result.PeriodEnd);
    }

    // Real bug this guards (2026-08-19): the transactions counted here already left the
    // account for real (it's funded from checking, not a card) - the real starting balance
    // already reflects them. This line must only project what's STILL expected before the
    // period ends, or it double-counts money that's already gone. It must NOT reuse
    // AmexCycleCalculator's MAX(actual, budget) floor - that's only correct for a card,
    // where a charge doesn't touch checking until a later, separate payment.
    [Fact]
    public void ClosedPeriod_ActualSpendingUnderBudget_ShowsOnlyTheRemainingAmount()
    {
        var results = _sut.CalculatePeriods(
            anchor: new DateOnly(2026, 8, 19), frequency: Frequency.Weekly, budgetAmount: 195m,
            categoryTransactions: [Charge(new DateOnly(2026, 8, 17), -38.32m)],
            asOfDate: new DateOnly(2026, 8, 19),
            windowStart: new DateOnly(2026, 8, 19), windowEnd: new DateOnly(2026, 8, 19));

        var result = Assert.Single(results);
        Assert.False(result.IsFuture);
        Assert.Equal(38.32m, result.ActualAmount);
        Assert.Equal(156.68m, result.Amount); // 195 budgeted - 38.32 already spent = 156.68 still projected
    }

    [Fact]
    public void ClosedPeriod_ActualSpendingReachesOrExceedsBudget_DropsToZero()
    {
        var results = _sut.CalculatePeriods(
            anchor: new DateOnly(2026, 8, 19), frequency: Frequency.Weekly, budgetAmount: 195m,
            categoryTransactions:
            [
                Charge(new DateOnly(2026, 8, 15), -80m),
                Charge(new DateOnly(2026, 8, 17), -150m)
            ],
            asOfDate: new DateOnly(2026, 8, 19),
            windowStart: new DateOnly(2026, 8, 19), windowEnd: new DateOnly(2026, 8, 19));

        var result = Assert.Single(results);
        Assert.Equal(230m, result.ActualAmount);
        Assert.Equal(0m, result.Amount); // already spent more than budgeted - nothing further to project
    }

    [Fact]
    public void TransactionsOutsideThePeriod_AreNotCounted()
    {
        var results = _sut.CalculatePeriods(
            anchor: new DateOnly(2026, 8, 19), frequency: Frequency.Weekly, budgetAmount: 195m,
            categoryTransactions:
            [
                Charge(new DateOnly(2026, 8, 12), -100m), // the PRIOR period, not this one
                Charge(new DateOnly(2026, 8, 20), -100m) // the NEXT period, not this one
            ],
            asOfDate: new DateOnly(2026, 8, 19),
            windowStart: new DateOnly(2026, 8, 19), windowEnd: new DateOnly(2026, 8, 19));

        var result = Assert.Single(results);
        Assert.Equal(0m, result.ActualAmount);
        Assert.Equal(195m, result.Amount);
    }

    [Fact]
    public void MultipleWeeksInWindow_EachGetsItsOwnPeriod()
    {
        var results = _sut.CalculatePeriods(
            anchor: new DateOnly(2026, 8, 19), frequency: Frequency.Weekly, budgetAmount: 195m,
            categoryTransactions: [],
            asOfDate: new DateOnly(2026, 8, 19),
            windowStart: new DateOnly(2026, 8, 19), windowEnd: new DateOnly(2026, 9, 2));

        Assert.Equal(3, results.Count);
        Assert.Equal([new DateOnly(2026, 8, 19), new DateOnly(2026, 8, 26), new DateOnly(2026, 9, 2)], results.Select(r => r.PeriodEnd).ToList());
    }

    [Fact]
    public void MonthlyFrequency_UsesCalendarMonthBoundaries()
    {
        var results = _sut.CalculatePeriods(
            anchor: new DateOnly(2026, 8, 14), frequency: Frequency.Monthly, budgetAmount: 190m,
            categoryTransactions: [],
            asOfDate: new DateOnly(2026, 8, 14),
            windowStart: new DateOnly(2026, 8, 14), windowEnd: new DateOnly(2026, 8, 14));

        var result = Assert.Single(results);
        Assert.Equal(new DateOnly(2026, 7, 15), result.PeriodStart);
        Assert.Equal(new DateOnly(2026, 8, 14), result.PeriodEnd);
    }

    [Fact]
    public void PeriodInProgress_NotYetClosed_StillComparesActualAgainstBudget()
    {
        // asOfDate falls mid-period (before periodEnd) - not "future" (it's already started),
        // so real spending so far still counts against budget, same as a closed period.
        var results = _sut.CalculatePeriods(
            anchor: new DateOnly(2026, 8, 19), frequency: Frequency.Weekly, budgetAmount: 195m,
            categoryTransactions: [Charge(new DateOnly(2026, 8, 15), -250m)],
            asOfDate: new DateOnly(2026, 8, 17), // mid-period, period doesn't end until 08-19
            windowStart: new DateOnly(2026, 8, 19), windowEnd: new DateOnly(2026, 8, 19));

        var result = Assert.Single(results);
        Assert.False(result.IsFuture);
        Assert.Equal(0m, result.Amount); // already spent past budget - nothing further to project
    }
}
