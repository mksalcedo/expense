using Bunit;
using Expense.Domain.Services.Forecast;
using Expense.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Web.Tests.Pages;

public class ForecastTests : BunitContext
{
    private class FakeForecastResultProvider(ForecastResult result) : IForecastResultProvider
    {
        public Task<ForecastResult> GetForecastAsync(CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    [Fact]
    public void Forecast_RendersStartingBalanceAndLedgerRows()
    {
        var result = new ForecastResult
        {
            StartingBalance = 6463.02m,
            Rows =
            [
                new ForecastLedgerRow { Date = new DateOnly(2026, 7, 20), Description = "Discover Payment", Amount = -150m, RunningBalance = 6313.02m },
                new ForecastLedgerRow { Date = new DateOnly(2026, 7, 31), Description = "Paycheck", Amount = 2000m, RunningBalance = 8313.02m }
            ]
        };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();

        Assert.Contains("6,463.02", cut.Markup);
        Assert.Contains("Discover Payment", cut.Markup);
        Assert.Contains("Paycheck", cut.Markup);
        Assert.Contains("8,313.02", cut.Markup);
    }

    [Fact]
    public void Forecast_RendersLowestProjectedBalance()
    {
        var result = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows =
            [
                new ForecastLedgerRow { Date = new DateOnly(2026, 7, 20), Description = "Big expense", Amount = -900m, RunningBalance = 100m },
                new ForecastLedgerRow { Date = new DateOnly(2026, 7, 25), Description = "Refund", Amount = 200m, RunningBalance = 300m }
            ]
        };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();

        Assert.Contains("Lowest projected balance", cut.Markup);
        Assert.Contains("100.00", cut.Markup);
    }
}
