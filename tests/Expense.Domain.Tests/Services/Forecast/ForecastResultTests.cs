using Expense.Domain.Services.Forecast;

namespace Expense.Domain.Tests.Services.Forecast;

public class ForecastResultTests
{
    [Fact]
    public void LowestProjectedBalanceDate_ReturnsTheDateOfTheLowestBalanceRow()
    {
        var result = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows =
            [
                new ForecastLedgerRow { Date = new DateOnly(2026, 7, 20), Description = "Big expense", Amount = -900m, RunningBalance = 100m },
                new ForecastLedgerRow { Date = new DateOnly(2026, 8, 20), Description = "Bigger expense", Amount = -50m, RunningBalance = 50m },
                new ForecastLedgerRow { Date = new DateOnly(2026, 8, 25), Description = "Refund", Amount = 200m, RunningBalance = 250m }
            ]
        };

        Assert.Equal(new DateOnly(2026, 8, 20), result.LowestProjectedBalanceDate);
    }

    [Fact]
    public void LowestProjectedBalanceDate_WhenTied_ReturnsTheEarliestOccurrence()
    {
        var result = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows =
            [
                new ForecastLedgerRow { Date = new DateOnly(2026, 7, 20), Description = "First dip", Amount = -900m, RunningBalance = 100m },
                new ForecastLedgerRow { Date = new DateOnly(2026, 8, 20), Description = "Same dip", Amount = 0m, RunningBalance = 100m }
            ]
        };

        Assert.Equal(new DateOnly(2026, 7, 20), result.LowestProjectedBalanceDate);
    }

    [Fact]
    public void LowestProjectedBalanceDate_WhenNoRows_IsNull()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [] };

        Assert.Null(result.LowestProjectedBalanceDate);
    }
}
