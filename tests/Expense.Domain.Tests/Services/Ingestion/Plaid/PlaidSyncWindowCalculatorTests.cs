using Expense.Domain.Services.Ingestion.Plaid;

namespace Expense.Domain.Tests.Services.Ingestion.Plaid;

public class PlaidSyncWindowCalculatorTests
{
    [Fact]
    public void GetWindowStartDate_WithAPriorSuccessfulRun_ReturnsSevenDaysBeforeThatRun()
    {
        var lastSuccessfulRunAt = new DateTimeOffset(2026, 7, 20, 6, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 7, 29, 6, 0, 0, TimeSpan.Zero);

        var start = PlaidSyncWindowCalculator.GetWindowStartDate(lastSuccessfulRunAt, now);

        Assert.Equal(new DateOnly(2026, 7, 13), start);
    }

    [Fact]
    public void GetWindowStartDate_WithNoPriorSuccessfulRun_ReturnsSevenDaysBeforeNow()
    {
        var now = new DateTimeOffset(2026, 7, 29, 6, 0, 0, TimeSpan.Zero);

        var start = PlaidSyncWindowCalculator.GetWindowStartDate(null, now);

        Assert.Equal(new DateOnly(2026, 7, 22), start);
    }
}
