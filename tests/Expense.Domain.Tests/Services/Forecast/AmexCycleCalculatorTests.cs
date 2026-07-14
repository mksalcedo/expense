using Expense.Domain.Entities;
using Expense.Domain.Services.Forecast;

namespace Expense.Domain.Tests.Services.Forecast;

public class AmexCycleCalculatorTests
{
    private readonly AmexCycleCalculator _sut = new();

    private static BankTransaction Charge(DateOnly postedDate, decimal amount) => new()
    {
        AccountId = 2,
        TransactionDate = postedDate,
        PostedDate = postedDate,
        Description = "charge",
        Amount = amount, // negative = a charge, matching real bank-statement sign convention
        ImportSource = "Test",
        CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public void CycleBoundaryMath_CloseBeforeDue_DueDateLandsInSameMonth()
    {
        // close on the 5th, due on the 30th - due date is later the same month
        var results = _sut.CalculateDuePayments(
            statementCloseDay: 5, paymentDueDay: 30, extraPrincipal: 0m, monthlyBudgetTotal: 0m,
            qualifyingTransactions: [], asOfDate: new DateOnly(2026, 1, 1),
            windowStart: new DateOnly(2026, 3, 1), windowEnd: new DateOnly(2026, 3, 31));

        var result = Assert.Single(results);
        Assert.Equal(new DateOnly(2026, 3, 5), result.CycleEnd);
        Assert.Equal(new DateOnly(2026, 3, 30), result.DueDate);
        Assert.Equal(new DateOnly(2026, 2, 6), result.CycleStart);
    }

    [Fact]
    public void CycleBoundaryMath_DueBeforeClose_DueDateLandsInNextMonth()
    {
        // close on the 25th, due on the 15th - due date rolls into the following month
        var results = _sut.CalculateDuePayments(
            statementCloseDay: 25, paymentDueDay: 15, extraPrincipal: 0m, monthlyBudgetTotal: 0m,
            qualifyingTransactions: [], asOfDate: new DateOnly(2026, 1, 1),
            windowStart: new DateOnly(2026, 3, 1), windowEnd: new DateOnly(2026, 3, 31));

        var result = Assert.Single(results);
        Assert.Equal(new DateOnly(2026, 2, 25), result.CycleEnd);
        Assert.Equal(new DateOnly(2026, 3, 15), result.DueDate);
    }

    [Fact]
    public void FutureCycle_HasNotStarted_UsesBudgetOnly()
    {
        var qualifying = new List<BankTransaction> { Charge(new DateOnly(2026, 2, 10), -50m) };

        var results = _sut.CalculateDuePayments(
            statementCloseDay: 25, paymentDueDay: 15, extraPrincipal: 1100m, monthlyBudgetTotal: 900m,
            qualifyingTransactions: qualifying, asOfDate: new DateOnly(2026, 1, 1), // before the cycle even starts (Jan 26)
            windowStart: new DateOnly(2026, 3, 1), windowEnd: new DateOnly(2026, 3, 31));

        var result = Assert.Single(results);
        Assert.Equal(2000m, result.Amount); // budget (900) + extra (1100) - a future cycle never looks at actual data, even if some already exists
    }

    [Fact]
    public void InProgressCycle_UnderBudget_StillShowsBudgetedAmount()
    {
        // cycle runs Jan 26 - Feb 25; only $200 charged so far against a $900 budget
        var qualifying = new List<BankTransaction> { Charge(new DateOnly(2026, 2, 1), -200m) };

        var results = _sut.CalculateDuePayments(
            statementCloseDay: 25, paymentDueDay: 15, extraPrincipal: 1100m, monthlyBudgetTotal: 900m,
            qualifyingTransactions: qualifying, asOfDate: new DateOnly(2026, 2, 10), // mid-cycle
            windowStart: new DateOnly(2026, 3, 1), windowEnd: new DateOnly(2026, 3, 31));

        var result = Assert.Single(results);
        Assert.Equal(2000m, result.Amount); // MAX(200, 900) + 1100 - never gets optimistic from underspending
    }

    [Fact]
    public void ClosedCycle_OverBudget_ShowsActualAmount()
    {
        // cycle runs Jan 26 - Feb 25; $1,250 actually charged against a $900 budget
        var qualifying = new List<BankTransaction>
        {
            Charge(new DateOnly(2026, 2, 1), -750m),
            Charge(new DateOnly(2026, 2, 20), -500m)
        };

        var results = _sut.CalculateDuePayments(
            statementCloseDay: 25, paymentDueDay: 15, extraPrincipal: 1100m, monthlyBudgetTotal: 900m,
            qualifyingTransactions: qualifying, asOfDate: new DateOnly(2026, 3, 1), // cycle already closed
            windowStart: new DateOnly(2026, 3, 1), windowEnd: new DateOnly(2026, 3, 31));

        var result = Assert.Single(results);
        Assert.Equal(2350m, result.Amount); // MAX(1250, 900) + 1100
    }

    [Fact]
    public void TransactionsOutsideCycleBounds_AreExcludedFromActual()
    {
        // cycle under test runs Jan 26 - Feb 25 inclusive; one charge lands exactly on
        // the prior close date (belongs to the prior cycle), one lands the day after
        // this cycle's close (belongs to the next cycle) - neither counts here
        var qualifying = new List<BankTransaction>
        {
            Charge(new DateOnly(2026, 1, 25), -10000m), // prior cycle's close date
            Charge(new DateOnly(2026, 2, 26), -10000m)  // day after this cycle's close
        };

        var results = _sut.CalculateDuePayments(
            statementCloseDay: 25, paymentDueDay: 15, extraPrincipal: 0m, monthlyBudgetTotal: 900m,
            qualifyingTransactions: qualifying, asOfDate: new DateOnly(2026, 3, 1),
            windowStart: new DateOnly(2026, 3, 1), windowEnd: new DateOnly(2026, 3, 31));

        var result = Assert.Single(results);
        Assert.Equal(900m, result.Amount); // budget only - the two out-of-range charges never entered the MAX comparison
    }

    [Fact]
    public void MultipleCyclesInWindow_AreAllReturned()
    {
        var results = _sut.CalculateDuePayments(
            statementCloseDay: 25, paymentDueDay: 15, extraPrincipal: 0m, monthlyBudgetTotal: 900m,
            qualifyingTransactions: [], asOfDate: new DateOnly(2026, 1, 1),
            windowStart: new DateOnly(2026, 3, 1), windowEnd: new DateOnly(2026, 5, 31));

        var dueDates = results.Select(r => r.DueDate).OrderBy(d => d).ToList();
        Assert.Equal(
        [
            new DateOnly(2026, 3, 15),
            new DateOnly(2026, 4, 15),
            new DateOnly(2026, 5, 15)
        ], dueDates);
    }

    [Fact]
    public void CloseDayClampsOnShorterMonths()
    {
        // close day 31 - a February cycle must clamp to the 28th
        var results = _sut.CalculateDuePayments(
            statementCloseDay: 31, paymentDueDay: 20, extraPrincipal: 0m, monthlyBudgetTotal: 900m,
            qualifyingTransactions: [], asOfDate: new DateOnly(2026, 1, 1),
            windowStart: new DateOnly(2026, 3, 1), windowEnd: new DateOnly(2026, 3, 31));

        var result = Assert.Single(results);
        Assert.Equal(new DateOnly(2026, 2, 28), result.CycleEnd);
    }
}
