using Expense.Domain.Entities;
using Expense.Domain.Services.Budgets;

namespace Expense.Domain.Tests.Services;

public class BudgetProrationServiceTests
{
    private readonly BudgetProrationService _sut = new();

    [Fact]
    public void Convert_SameFrequency_IsIdentity()
    {
        var result = _sut.Convert(450m, Frequency.Weekly, Frequency.Weekly);
        Assert.Equal(450m, result);
    }

    [Fact]
    public void Convert_WeeklyGroceriesBudget_ToMonthlyEquivalent_IsApproximatelyCorrect()
    {
        // $450/week over a 365.25-day year averages to about $1,956.65/month -
        // the "≈ $1,950/month" figure from the design doc was a rough approximation,
        // not an exact target; assert against the real calendar-based math with a
        // sensible tolerance instead of the doc's shorthand number.
        var result = _sut.Convert(450m, Frequency.Weekly, Frequency.Monthly);
        Assert.InRange(result, 1950m, 1965m);
    }

    [Fact]
    public void Convert_MonthlyAmexExtraPrincipal_ToWeeklyEquivalent_IsApproximatelyCorrect()
    {
        var result = _sut.Convert(1100m, Frequency.Monthly, Frequency.Weekly);
        Assert.InRange(result, 251m, 254m);
    }

    [Fact]
    public void Convert_BiweeklyToWeekly_IsExactlyHalf()
    {
        var result = _sut.Convert(100m, Frequency.Biweekly, Frequency.Weekly);
        Assert.Equal(50m, result);
    }

    [Fact]
    public void Convert_QuarterlyToMonthly_IsApproximatelyOneThird()
    {
        var result = _sut.Convert(180m, Frequency.Quarterly, Frequency.Monthly);
        Assert.InRange(result, 59m, 61m);
    }

    [Fact]
    public void Convert_AnnualToMonthly_IsApproximatelyOneTwelfth()
    {
        var result = _sut.Convert(1200m, Frequency.Annual, Frequency.Monthly);
        Assert.InRange(result, 99m, 101m);
    }

    [Fact]
    public void Convert_RoundTripsBackToOriginal_WithinRoundingTolerance()
    {
        var toMonthly = _sut.Convert(450m, Frequency.Weekly, Frequency.Monthly);
        var backToWeekly = _sut.Convert(toMonthly, Frequency.Monthly, Frequency.Weekly);

        Assert.InRange(backToWeekly, 449.99m, 450.01m);
    }

    [Fact]
    public void ToDailyRate_ComputesTheCanonicalConversionBasis()
    {
        // $700/week -> $100/day, the basis every other conversion is built from
        var result = _sut.ToDailyRate(700m, Frequency.Weekly);
        Assert.Equal(100m, result);
    }
}
