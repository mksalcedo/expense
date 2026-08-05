using Expense.Domain.Services.SpendingTracker;

namespace Expense.Domain.Tests.Services.SpendingTracker;

public class CarryoverCalculatorTests
{
    [Fact]
    public void SinglePeriod_UnderBudget_CarriesNothingIn_AndRollingBalanceIsTheSurplus()
    {
        var result = CarryoverCalculator.Compute([new CarryoverCalculator.PeriodActivity(450m, 300m)], capMultiplier: 1.0m);

        Assert.Equal(0m, result.CarriedIn);
        Assert.Equal(150m, result.RollingBalance);
    }

    [Fact]
    public void SinglePeriod_OverBudget_RollingBalanceIsNegative()
    {
        var result = CarryoverCalculator.Compute([new CarryoverCalculator.PeriodActivity(150m, 210m)], capMultiplier: 1.0m);

        Assert.Equal(-60m, result.RollingBalance);
    }

    [Fact]
    public void SecondPeriod_CarriesInThePriorPeriodsRollingBalance()
    {
        var periods = new[]
        {
            new CarryoverCalculator.PeriodActivity(450m, 400m), // +50 surplus
            new CarryoverCalculator.PeriodActivity(450m, 470m)  // -20 this period
        };

        var result = CarryoverCalculator.Compute(periods, capMultiplier: 1.0m);

        Assert.Equal(50m, result.CarriedIn);
        Assert.Equal(30m, result.RollingBalance);
    }

    [Fact]
    public void Surplus_IsClamped_AtTheCapMultiple_OfThatPeriodsOwnBudget()
    {
        var periods = new[]
        {
            new CarryoverCalculator.PeriodActivity(450m, 100m), // +350
            new CarryoverCalculator.PeriodActivity(450m, 50m)   // +400 more, would be +750 uncapped
        };

        var result = CarryoverCalculator.Compute(periods, capMultiplier: 1.0m);

        Assert.Equal(450m, result.RollingBalance);
        Assert.Equal(450m, result.Cap);
    }

    [Fact]
    public void Deficit_IsClamped_AtTheNegativeCapMultiple_OfThatPeriodsOwnBudget()
    {
        var periods = new[]
        {
            new CarryoverCalculator.PeriodActivity(150m, 400m), // -250
            new CarryoverCalculator.PeriodActivity(150m, 200m)  // -50 more, would be -300 uncapped
        };

        var result = CarryoverCalculator.Compute(periods, capMultiplier: 1.0m);

        Assert.Equal(-150m, result.RollingBalance);
    }

    // The cap is a real ceiling on the running state, not just a display clamp applied once
    // at the end - once a surplus hits the cap, additional surplus in later periods is lost
    // rather than silently banked past the visible number. Regression guard: a naive
    // sum-then-clamp-once approach would give 400 here, not 100.
    [Fact]
    public void Cap_AppliesAtEveryStep_NotJustToTheFinalTotal()
    {
        var periods = new[]
        {
            new CarryoverCalculator.PeriodActivity(450m, 100m), // +350 -> 350
            new CarryoverCalculator.PeriodActivity(450m, 50m),  // +400 more -> capped at 450
            new CarryoverCalculator.PeriodActivity(450m, 800m)  // -350 this period
        };

        var result = CarryoverCalculator.Compute(periods, capMultiplier: 1.0m);

        Assert.Equal(100m, result.RollingBalance);
    }

    [Fact]
    public void NullCapMultiplier_NeverClamps()
    {
        var periods = new[]
        {
            new CarryoverCalculator.PeriodActivity(450m, 0m),
            new CarryoverCalculator.PeriodActivity(450m, 0m),
            new CarryoverCalculator.PeriodActivity(450m, 0m)
        };

        var result = CarryoverCalculator.Compute(periods, capMultiplier: null);

        Assert.Equal(1350m, result.RollingBalance);
        Assert.Null(result.Cap);
    }

    [Fact]
    public void HigherMultiplier_AllowsCarryoverPastOnePeriodsBudget()
    {
        var periods = new[]
        {
            new CarryoverCalculator.PeriodActivity(100m, 0m),
            new CarryoverCalculator.PeriodActivity(100m, 0m),
            new CarryoverCalculator.PeriodActivity(100m, 0m)
        };

        var result = CarryoverCalculator.Compute(periods, capMultiplier: 4.0m);

        Assert.Equal(300m, result.RollingBalance);
        Assert.Equal(400m, result.Cap);
    }

    [Fact]
    public void EmptyPeriodList_Throws()
    {
        Assert.Throws<ArgumentException>(() => CarryoverCalculator.Compute([], capMultiplier: 1.0m));
    }
}
