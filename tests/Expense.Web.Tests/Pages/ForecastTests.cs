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

    [Fact]
    public void Forecast_ShowsWhenTheLowestProjectedBalanceOccurs()
    {
        var result = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows =
            [
                new ForecastLedgerRow { Date = new DateOnly(2026, 7, 20), Description = "Big expense", Amount = -900m, RunningBalance = 100m },
                new ForecastLedgerRow { Date = new DateOnly(2026, 8, 20), Description = "Bigger expense", Amount = -50m, RunningBalance = 50m }
            ]
        };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();

        Assert.Contains("Occurs on 08/20/2026", cut.Markup);
    }

    [Fact]
    public void Forecast_HighlightsTheLowestBalanceRow()
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
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();

        var rows = cut.FindAll("tbody tr");
        Assert.Contains("background-color: yellow", rows[1].GetAttribute("style"));
        Assert.DoesNotContain("background-color: yellow", rows[0].GetAttribute("style") ?? "");
        Assert.DoesNotContain("background-color: yellow", rows[2].GetAttribute("style") ?? "");
    }

    [Fact]
    public void Forecast_FormatsDatesAsMonthDayYear()
    {
        var result = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [new ForecastLedgerRow { Date = new DateOnly(2026, 7, 20), Description = "Big expense", Amount = -900m, RunningBalance = 100m }]
        };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();

        Assert.Contains("07/20/2026", cut.Markup);
        Assert.DoesNotContain("2026-07-20", cut.Markup);
    }

    [Fact]
    public void Forecast_AmountAndBalanceHeaders_AreRightAligned()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();

        var headers = cut.FindAll("th");
        Assert.Equal("Amount", headers[2].TextContent);
        Assert.Equal("Running balance", headers[3].TextContent);
        Assert.Contains("text-align: right", headers[2].GetAttribute("style"));
        Assert.Contains("text-align: right", headers[3].GetAttribute("style"));
    }

    [Fact]
    public void Forecast_AmountAndBalanceCells_AreRightAligned()
    {
        var result = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [new ForecastLedgerRow { Date = new DateOnly(2026, 7, 20), Description = "Big expense", Amount = -900m, RunningBalance = 100m }]
        };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();

        var cells = cut.FindAll("tbody td");
        Assert.Contains("text-align: right", cells[2].GetAttribute("style")); // Amount
        Assert.Contains("text-align: right", cells[3].GetAttribute("style")); // Running balance
    }

    [Fact]
    public void Forecast_TableCells_HaveBordersAndSpacing()
    {
        var result = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [new ForecastLedgerRow { Date = new DateOnly(2026, 7, 20), Description = "Big expense", Amount = -900m, RunningBalance = 100m }]
        };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();

        Assert.Contains("border-collapse: collapse", cut.Find("table").GetAttribute("style"));
        foreach (var cell in cut.FindAll("th").Concat(cut.FindAll("td")))
        {
            var style = cell.GetAttribute("style") ?? "";
            Assert.Contains("border:", style);
            Assert.Contains("padding:", style);
        }
    }

    [Fact]
    public void Forecast_HasAnExportToExcelLink()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();

        var link = cut.Find("#export-excel-link");
        Assert.Equal("/export/forecast.xlsx", link.GetAttribute("href"));
        // target="_blank" so Blazor Server's SPA navigation interception doesn't try to
        // treat this file-download endpoint as a page navigation (it isn't one, and
        // otherwise the click throws a TaskCanceledException in the circuit).
        Assert.Equal("_blank", link.GetAttribute("target"));
    }
}
