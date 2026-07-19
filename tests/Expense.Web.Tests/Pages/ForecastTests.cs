using Bunit;
using Expense.Domain.Entities;
using Expense.Domain.Services.Forecast;
using Expense.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Web.Tests.Pages;

public class ForecastTests : BunitContext
{
    // Stateful fake: DeferPaymentAsync/RemoveDeferralAsync/ConfirmPaymentAsync/
    // RemoveConfirmationAsync actually mutate the underlying result (mirroring what
    // re-querying the real backend would show) so tests can drive the full action ->
    // re-render -> undo -> re-render cycle, not just verify the call happened.
    private class FakeForecastResultProvider(ForecastResult result) : IForecastResultProvider
    {
        private int _nextDeferralId = 1;
        private int _nextConfirmationId = 1;

        public Task<ForecastResult> GetForecastAsync(CancellationToken cancellationToken = default) => Task.FromResult(result);

        public Task DeferPaymentAsync(int accountId, DateOnly originalDate, DateOnly deferredToDate, string? note, CancellationToken cancellationToken = default)
        {
            var row = result.Rows.Single(r => r.AccountId == accountId && r.OriginalDate == originalDate);
            row.Date = deferredToDate;
            row.IsDeferred = true;
            row.DeferralId = _nextDeferralId++;
            return Task.CompletedTask;
        }

        public Task RemoveDeferralAsync(int deferralId, CancellationToken cancellationToken = default)
        {
            var row = result.Rows.Single(r => r.DeferralId == deferralId);
            row.Date = row.OriginalDate;
            row.IsDeferred = false;
            row.DeferralId = null;
            return Task.CompletedTask;
        }

        public Task ConfirmPaymentAsync(int accountId, DateOnly originalDate, CancellationToken cancellationToken = default) =>
            ExcludeAsync(accountId, originalDate, ConfirmationReason.AlreadyPaid);

        public Task OverridePaymentAsync(int accountId, DateOnly originalDate, CancellationToken cancellationToken = default) =>
            ExcludeAsync(accountId, originalDate, ConfirmationReason.Overridden);

        private Task ExcludeAsync(int accountId, DateOnly originalDate, ConfirmationReason reason)
        {
            var row = result.Rows.Single(r => r.AccountId == accountId && r.OriginalDate == originalDate);
            result.Rows.Remove(row);
            result.Confirmations.Add(new ConfirmedPayment
            {
                ConfirmationId = _nextConfirmationId++, AccountId = accountId, AccountName = row.Description, OriginalDate = originalDate, Reason = reason
            });
            return Task.CompletedTask;
        }

        public Task RemoveConfirmationAsync(int confirmationId, CancellationToken cancellationToken = default)
        {
            var confirmation = result.Confirmations.Single(c => c.ConfirmationId == confirmationId);
            result.Confirmations.Remove(confirmation);
            result.Rows.Add(new ForecastLedgerRow
            {
                Date = confirmation.OriginalDate, Description = confirmation.AccountName, Amount = 0m, RunningBalance = 0m,
                AccountId = confirmation.AccountId, OriginalDate = confirmation.OriginalDate
            });
            return Task.CompletedTask;
        }
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
    public void Forecast_ShowsADeferActionOnEachUndeferredRow()
    {
        var result = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [new ForecastLedgerRow { Date = new DateOnly(2026, 8, 20), Description = "Amex Payment", Amount = -4442.38m, RunningBalance = -273.64m, AccountId = 2, OriginalDate = new DateOnly(2026, 8, 20) }]
        };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();

        Assert.NotNull(cut.Find("#defer-date-0"));
        Assert.NotNull(cut.Find("#defer-btn-0"));
        Assert.Empty(cut.FindAll("#remove-deferral-btn-0"));
    }

    [Fact]
    public void DeferringAPayment_MovesItToTheNewDate_AndHighlightsItWithAWarning()
    {
        var result = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [new ForecastLedgerRow { Date = new DateOnly(2026, 8, 20), Description = "Amex Payment", Amount = -4442.38m, RunningBalance = -273.64m, AccountId = 2, OriginalDate = new DateOnly(2026, 8, 20) }]
        };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#defer-date-0").Change("2026-08-22");
        cut.Find("#defer-btn-0").Click();

        Assert.Contains("08/22/2026", cut.Markup);
        Assert.Contains("Originally estimated for 08/20/2026", cut.Markup);
        Assert.Contains("reschedule", cut.Markup);
        var row = cut.Find("tbody tr");
        Assert.Contains("background-color: orange", row.GetAttribute("style"));
        Assert.NotNull(cut.Find("#remove-deferral-btn-0"));
        Assert.Empty(cut.FindAll("#defer-btn-0"));
    }

    [Fact]
    public void RemovingADeferral_RevertsToTheOriginalDate_AndClearsTheHighlight()
    {
        var result = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [new ForecastLedgerRow { Date = new DateOnly(2026, 8, 20), Description = "Amex Payment", Amount = -4442.38m, RunningBalance = -273.64m, AccountId = 2, OriginalDate = new DateOnly(2026, 8, 20) }]
        };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#defer-date-0").Change("2026-08-22");
        cut.Find("#defer-btn-0").Click();
        cut.Find("#remove-deferral-btn-0").Click();

        Assert.Contains("08/20/2026", cut.Markup);
        Assert.DoesNotContain("Originally estimated for", cut.Markup);
        var row = cut.Find("tbody tr");
        Assert.DoesNotContain("background-color: orange", row.GetAttribute("style") ?? "");
        Assert.NotNull(cut.Find("#defer-btn-0"));
    }

    [Fact]
    public void Forecast_ShowsAConfirmPaidActionOnEachUndeferredRow()
    {
        var result = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [new ForecastLedgerRow { Date = new DateOnly(2026, 8, 20), Description = "Chase Amazon Prime Visa Payment", Amount = -357m, RunningBalance = 643m, AccountId = 5, OriginalDate = new DateOnly(2026, 8, 20) }]
        };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();

        Assert.NotNull(cut.Find("#confirm-btn-0"));
    }

    [Fact]
    public void ConfirmingAPayment_RemovesItFromTheLedgerAndListsItAsConfirmed()
    {
        var result = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [new ForecastLedgerRow { Date = new DateOnly(2026, 8, 20), Description = "Chase Amazon Prime Visa Payment", Amount = -357m, RunningBalance = 643m, AccountId = 5, OriginalDate = new DateOnly(2026, 8, 20) }]
        };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#confirm-btn-0").Click();

        Assert.Empty(cut.Find("#ledger-table").QuerySelectorAll("tbody tr"));
        Assert.Contains("Chase Amazon Prime Visa Payment", cut.Markup);
        Assert.Contains("08/20/2026", cut.Markup);
        Assert.NotNull(cut.Find("#undo-confirmation-btn-1"));
    }

    [Fact]
    public void UndoingAConfirmation_BringsTheRowBackToTheLedger()
    {
        var result = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [new ForecastLedgerRow { Date = new DateOnly(2026, 8, 20), Description = "Chase Amazon Prime Visa Payment", Amount = -357m, RunningBalance = 643m, AccountId = 5, OriginalDate = new DateOnly(2026, 8, 20) }]
        };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#confirm-btn-0").Click();
        cut.Find("#undo-confirmation-btn-1").Click();

        Assert.Single(cut.FindAll("tbody tr"));
        Assert.NotNull(cut.Find("#confirm-btn-0"));
    }

    [Fact]
    public void Forecast_ShowsAnOverrideActionOnEachUndeferredRow()
    {
        var result = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [new ForecastLedgerRow { Date = new DateOnly(2026, 8, 20), Description = "Amex Payment", Amount = -2000m, RunningBalance = -1000m, AccountId = 2, OriginalDate = new DateOnly(2026, 8, 20) }]
        };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();

        Assert.NotNull(cut.Find("#override-btn-0"));
    }

    [Fact]
    public void OverridingAPayment_RemovesItFromTheLedgerAndListsItWithAnOverriddenReason()
    {
        var result = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [new ForecastLedgerRow { Date = new DateOnly(2026, 8, 20), Description = "Amex Payment", Amount = -2000m, RunningBalance = -1000m, AccountId = 2, OriginalDate = new DateOnly(2026, 8, 20) }]
        };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#override-btn-0").Click();

        Assert.Empty(cut.Find("#ledger-table").QuerySelectorAll("tbody tr"));
        var confirmationsRow = cut.Find("#confirmations-table").QuerySelector("tbody tr");
        Assert.Contains("Amex Payment", confirmationsRow!.TextContent);
        Assert.Contains("Overridden", confirmationsRow.TextContent);
    }

    [Fact]
    public void ConfirmingAPayment_ListsItWithAnAlreadyPaidReason()
    {
        var result = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [new ForecastLedgerRow { Date = new DateOnly(2026, 8, 20), Description = "Chase Amazon Prime Visa Payment", Amount = -357m, RunningBalance = 643m, AccountId = 5, OriginalDate = new DateOnly(2026, 8, 20) }]
        };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#confirm-btn-0").Click();

        var confirmationsRow = cut.Find("#confirmations-table").QuerySelector("tbody tr");
        Assert.Contains("AlreadyPaid", confirmationsRow!.TextContent);
    }

    [Fact]
    public void ConfirmAndOverrideActions_AreAvailableEvenOnADeferredRow()
    {
        var result = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [new ForecastLedgerRow { Date = new DateOnly(2026, 8, 20), Description = "Amex Payment", Amount = -2000m, RunningBalance = -1000m, AccountId = 2, OriginalDate = new DateOnly(2026, 8, 20) }]
        };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#defer-date-0").Change("2026-08-22");
        cut.Find("#defer-btn-0").Click();

        Assert.NotNull(cut.Find("#remove-deferral-btn-0"));
        Assert.NotNull(cut.Find("#confirm-btn-0"));
        Assert.NotNull(cut.Find("#override-btn-0"));
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
