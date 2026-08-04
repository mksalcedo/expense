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
    // re-render -> undo -> re-render cycle, not just verify the call happened. Confirm/
    // Override mark the row excluded in place (matching ForecastEngine) rather than
    // removing it, and Undo simply clears that same flag - nothing is ever recreated.
    private class FakeForecastResultProvider(ForecastResult result) : IForecastResultProvider
    {
        private int _nextDeferralId = 1;
        private int _nextConfirmationId = 1;
        private int _nextPartialPaymentId = 1;

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

        public Task ConfirmPaymentAsync(int accountId, DateOnly originalDate, DateOnly effectiveDate, decimal amount, CancellationToken cancellationToken = default) =>
            ExcludeAsync(accountId, originalDate, effectiveDate, amount, ConfirmationReason.AlreadyPaid);

        public Task OverridePaymentAsync(int accountId, DateOnly originalDate, DateOnly effectiveDate, decimal amount, CancellationToken cancellationToken = default) =>
            ExcludeAsync(accountId, originalDate, effectiveDate, amount, ConfirmationReason.Overridden);

        private Task ExcludeAsync(int accountId, DateOnly originalDate, DateOnly effectiveDate, decimal amount, ConfirmationReason reason)
        {
            var row = result.Rows.Single(r => r.AccountId == accountId && r.OriginalDate == originalDate);
            row.Date = effectiveDate;
            row.Amount = amount;
            row.IsExcluded = true;
            row.ExclusionReason = reason;
            row.ConfirmationId = _nextConfirmationId++;
            return Task.CompletedTask;
        }

        public Task RemoveConfirmationAsync(int confirmationId, CancellationToken cancellationToken = default)
        {
            var row = result.Rows.Single(r => r.ConfirmationId == confirmationId);
            row.Date = row.OriginalDate;
            row.IsExcluded = false;
            row.ExclusionReason = null;
            row.ConfirmationId = null;
            return Task.CompletedTask;
        }

        public Task PayPartialAmountAsync(int accountId, DateOnly originalDate, DateOnly paidDate, decimal amount, CancellationToken cancellationToken = default)
        {
            var row = result.Rows.Single(r => r.AccountId == accountId && r.OriginalDate == originalDate);
            row.Amount += amount;
            row.PartialPayments.Add(new PartialPaymentSummary { PartialPaymentId = _nextPartialPaymentId++, Amount = amount, PaidDate = paidDate });
            return Task.CompletedTask;
        }

        public Task RemovePartialPaymentAsync(int partialPaymentId, CancellationToken cancellationToken = default)
        {
            var row = result.Rows.Single(r => r.PartialPayments.Any(p => p.PartialPaymentId == partialPaymentId));
            var partialPayment = row.PartialPayments.Single(p => p.PartialPaymentId == partialPaymentId);
            row.Amount -= partialPayment.Amount;
            row.PartialPayments.Remove(partialPayment);
            return Task.CompletedTask;
        }
    }

    private static ForecastLedgerRow AmexRow(decimal amount = -2000m, decimal runningBalance = -1000m) => new()
    {
        Date = new DateOnly(2026, 8, 20), Description = "Amex Payment", Amount = amount, RunningBalance = runningBalance,
        AccountId = 2, OriginalDate = new DateOnly(2026, 8, 20)
    };

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

        var dateCell = cut.Find("#ledger-table tbody td:first-child");
        Assert.Equal("07/20/2026", dateCell.TextContent);
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
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [AmexRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();

        Assert.NotNull(cut.Find("#defer-btn-0"));
        Assert.Empty(cut.FindAll("#remove-deferral-btn-0"));
        // No inline date input anymore - the modal is the only place a date gets entered.
        Assert.Empty(cut.FindAll("#defer-date-0"));
    }

    [Fact]
    public void ClickingDefer_OpensAModal_WithoutChangingAnythingYet()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [AmexRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#defer-btn-0").Click();

        Assert.NotNull(cut.Find("#action-modal"));
        Assert.Equal("Defer this payment?", cut.Find("#action-modal-title").TextContent);
        Assert.Contains("Amex Payment", cut.Find("#action-modal-explanation").TextContent);
        Assert.NotNull(cut.Find("#modal-date-input"));
        // Nothing applied yet - row still shows its original date, unstyled.
        var row = cut.Find("tbody tr");
        Assert.DoesNotContain("background-color: orange", row.GetAttribute("style") ?? "");
    }

    [Fact]
    public void DeferringAPayment_MovesItToTheNewDate_AndHighlightsItWithAWarning()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [AmexRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#defer-btn-0").Click();
        cut.Find("#modal-date-input").Change("2026-08-22");
        cut.Find("#action-modal-apply").Click();

        Assert.Empty(cut.FindAll("#action-modal"));
        Assert.Contains("08/22/2026", cut.Markup);
        Assert.Contains("Originally estimated for 08/20/2026", cut.Markup);
        Assert.Contains("reschedule", cut.Markup);
        var row = cut.Find("tbody tr");
        Assert.Contains("background-color: orange", row.GetAttribute("style"));
        Assert.NotNull(cut.Find("#remove-deferral-btn-0"));
        Assert.Empty(cut.FindAll("#defer-btn-0"));
    }

    [Fact]
    public void CancellingTheDeferModal_ClosesItAndChangesNothing()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [AmexRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#defer-btn-0").Click();
        cut.Find("#modal-date-input").Change("2026-08-22");
        cut.Find("#action-modal-cancel").Click();

        Assert.Empty(cut.FindAll("#action-modal"));
        Assert.Contains("08/20/2026", cut.Markup);
        Assert.DoesNotContain("08/22/2026", cut.Markup);
        var row = cut.Find("tbody tr");
        Assert.DoesNotContain("background-color: orange", row.GetAttribute("style") ?? "");
        Assert.NotNull(cut.Find("#defer-btn-0"));
    }

    [Fact]
    public void ClickingRemoveDeferral_OpensAConfirmationModal_NotAnInstantUndo()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [AmexRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#defer-btn-0").Click();
        cut.Find("#modal-date-input").Change("2026-08-22");
        cut.Find("#action-modal-apply").Click();

        cut.Find("#remove-deferral-btn-0").Click();

        Assert.NotNull(cut.Find("#action-modal"));
        Assert.Equal("Remove this deferral?", cut.Find("#action-modal-title").TextContent);
        Assert.Contains("08/20/2026", cut.Find("#action-modal-explanation").TextContent);
        // Still deferred - nothing has actually been undone yet, just prompted.
        Assert.Contains("08/22/2026", cut.Markup);
    }

    [Fact]
    public void RemovingADeferral_RevertsToTheOriginalDate_AndClearsTheHighlight()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [AmexRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#defer-btn-0").Click();
        cut.Find("#modal-date-input").Change("2026-08-22");
        cut.Find("#action-modal-apply").Click();
        cut.Find("#remove-deferral-btn-0").Click();
        cut.Find("#action-modal-apply").Click();

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
    public void ClickingConfirmPaid_OpensAModalWithNoEditableFields_JustAnExplanation()
    {
        var result = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [new ForecastLedgerRow { Date = new DateOnly(2026, 8, 20), Description = "Chase Amazon Prime Visa Payment", Amount = -357m, RunningBalance = 643m, AccountId = 5, OriginalDate = new DateOnly(2026, 8, 20) }]
        };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#confirm-btn-0").Click();

        Assert.Equal("Confirm this was paid as scheduled?", cut.Find("#action-modal-title").TextContent);
        var explanation = cut.Find("#action-modal-explanation").TextContent;
        Assert.Contains("Chase Amazon Prime Visa Payment", explanation);
        Assert.Contains("357.00", explanation);
        Assert.Contains("08/20/2026", explanation);
        Assert.Empty(cut.FindAll("#modal-date-input"));
        Assert.Empty(cut.FindAll("#modal-amount-input"));
    }

    [Fact]
    public void ConfirmingAPayment_MarksItExcludedInPlace_WithAnUndoButton()
    {
        var result = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [new ForecastLedgerRow { Date = new DateOnly(2026, 8, 20), Description = "Chase Amazon Prime Visa Payment", Amount = -357m, RunningBalance = 643m, AccountId = 5, OriginalDate = new DateOnly(2026, 8, 20) }]
        };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#confirm-btn-0").Click();
        cut.Find("#action-modal-apply").Click();

        var row = cut.Find("#ledger-table").QuerySelector("tbody tr");
        Assert.Contains("Chase Amazon Prime Visa Payment", row!.TextContent);
        Assert.Contains("08/20/2026", row.TextContent);
        Assert.Contains("357.00", row.TextContent); // the amount stays visible, not just the date
        Assert.NotNull(cut.Find("#undo-confirmation-btn-1"));
        Assert.Empty(cut.FindAll("#confirm-btn-0")); // action icons replaced by Undo, not left alongside it
    }

    [Fact]
    public void CancellingTheConfirmModal_LeavesTheRowUntouched()
    {
        var result = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [new ForecastLedgerRow { Date = new DateOnly(2026, 8, 20), Description = "Chase Amazon Prime Visa Payment", Amount = -357m, RunningBalance = 643m, AccountId = 5, OriginalDate = new DateOnly(2026, 8, 20) }]
        };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#confirm-btn-0").Click();
        cut.Find("#action-modal-cancel").Click();

        Assert.Empty(cut.FindAll("#action-modal"));
        Assert.NotNull(cut.Find("#confirm-btn-0"));
        Assert.Empty(cut.FindAll("#undo-confirmation-btn-1"));
    }

    [Fact]
    public void ClickingUndoConfirmation_OpensAConfirmationModal_NotAnInstantUndo()
    {
        var result = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [new ForecastLedgerRow { Date = new DateOnly(2026, 8, 20), Description = "Chase Amazon Prime Visa Payment", Amount = -357m, RunningBalance = 643m, AccountId = 5, OriginalDate = new DateOnly(2026, 8, 20) }]
        };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#confirm-btn-0").Click();
        cut.Find("#action-modal-apply").Click();
        cut.Find("#undo-confirmation-btn-1").Click();

        Assert.NotNull(cut.Find("#action-modal"));
        Assert.Equal("Undo this confirmation?", cut.Find("#action-modal-title").TextContent);
        // Still confirmed/excluded - nothing undone yet, just prompted.
        Assert.NotNull(cut.Find("#undo-confirmation-btn-1"));
    }

    [Fact]
    public void UndoingAConfirmation_RestoresTheNormalActionButtons()
    {
        var result = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [new ForecastLedgerRow { Date = new DateOnly(2026, 8, 20), Description = "Chase Amazon Prime Visa Payment", Amount = -357m, RunningBalance = 643m, AccountId = 5, OriginalDate = new DateOnly(2026, 8, 20) }]
        };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#confirm-btn-0").Click();
        cut.Find("#action-modal-apply").Click();
        cut.Find("#undo-confirmation-btn-1").Click();
        cut.Find("#action-modal-apply").Click();

        Assert.Single(cut.FindAll("tbody tr"));
        Assert.NotNull(cut.Find("#confirm-btn-0"));
        Assert.Empty(cut.FindAll("#undo-confirmation-btn-1"));
    }

    [Fact]
    public void ExcludedRow_IsStyledDistinctly_NotJustRemoved()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [AmexRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#override-btn-0").Click();
        cut.Find("#action-modal-apply").Click();

        var row = cut.Find("#ledger-table").QuerySelector("tbody tr");
        Assert.Contains("line-through", row!.GetAttribute("style") ?? "");
    }

    [Fact]
    public void Forecast_ShowsAnOverrideActionOnEachUndeferredRow()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [AmexRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();

        Assert.NotNull(cut.Find("#override-btn-0"));
    }

    [Fact]
    public void ClickingOverride_OpensAModalWithNoEditableFields_JustAnExplanation()
    {
        // Pure UX pass - Override doesn't gain the ability to enter a new amount yet, it
        // still just resubmits the row's own current value, same as before this change.
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [AmexRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#override-btn-0").Click();

        Assert.Equal("Override this payment?", cut.Find("#action-modal-title").TextContent);
        var explanation = cut.Find("#action-modal-explanation").TextContent;
        Assert.Contains("Amex Payment", explanation);
        Assert.Contains("2,000.00", explanation);
        Assert.Empty(cut.FindAll("#modal-date-input"));
        Assert.Empty(cut.FindAll("#modal-amount-input"));
    }

    [Fact]
    public void OverridingAPayment_MarksItExcludedInPlace_WithAnOverriddenReason()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [AmexRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#override-btn-0").Click();
        cut.Find("#action-modal-apply").Click();

        var row = cut.Find("#ledger-table").QuerySelector("tbody tr");
        Assert.Contains("Amex Payment", row!.TextContent);
        Assert.Contains("Overridden", row.TextContent);
        Assert.Contains("2,000.00", row.TextContent);
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
        cut.Find("#action-modal-apply").Click();

        var row = cut.Find("#ledger-table").QuerySelector("tbody tr");
        Assert.Contains("AlreadyPaid", row!.TextContent);
    }

    [Fact]
    public void ConfirmAndOverrideActions_AreAvailableEvenOnADeferredRow()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [AmexRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#defer-btn-0").Click();
        cut.Find("#modal-date-input").Change("2026-08-22");
        cut.Find("#action-modal-apply").Click();

        Assert.NotNull(cut.Find("#remove-deferral-btn-0"));
        Assert.NotNull(cut.Find("#confirm-btn-0"));
        Assert.NotNull(cut.Find("#override-btn-0"));
    }

    [Fact]
    public void Forecast_ShowsAPayPartialAmountAction_OnEachUndeferredRow()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [AmexRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();

        Assert.NotNull(cut.Find("#partial-pay-btn-0"));
        // No inline amount/date inputs anymore - both live in the modal now.
        Assert.Empty(cut.FindAll("#partial-amount-0"));
        Assert.Empty(cut.FindAll("#partial-date-0"));
    }

    [Fact]
    public void ClickingPayPartialAmount_OpensAModal_WithAmountAndDateFields()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [AmexRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#partial-pay-btn-0").Click();

        Assert.Equal("Record a partial payment?", cut.Find("#action-modal-title").TextContent);
        Assert.Contains("Amex Payment", cut.Find("#action-modal-explanation").TextContent);
        Assert.NotNull(cut.Find("#modal-amount-input"));
        Assert.NotNull(cut.Find("#modal-date-input"));
    }

    [Fact]
    public void PartialPaymentAction_IsAvailableEvenOnADeferredRow()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [AmexRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#defer-btn-0").Click();
        cut.Find("#modal-date-input").Change("2026-08-22");
        cut.Find("#action-modal-apply").Click();

        Assert.NotNull(cut.Find("#partial-pay-btn-0"));
    }

    [Fact]
    public void PayingAPartialAmount_ReducesTheRowAndListsItWithAnUndoButton()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [AmexRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#partial-pay-btn-0").Click();
        cut.Find("#modal-amount-input").Change("1000");
        cut.Find("#modal-date-input").Change("2026-07-20");
        cut.Find("#action-modal-apply").Click();

        var row = cut.Find("#ledger-table").QuerySelector("tbody tr");
        Assert.Contains("1,000.00", row!.TextContent);
        Assert.Contains("07/20/2026", row.TextContent);
        Assert.NotNull(cut.Find("#undo-partial-payment-btn-1"));
    }

    [Fact]
    public void CancellingThePartialPaymentModal_LeavesTheRowUntouched()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [AmexRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#partial-pay-btn-0").Click();
        cut.Find("#modal-amount-input").Change("1000");
        cut.Find("#action-modal-cancel").Click();

        Assert.Empty(cut.FindAll("#action-modal"));
        var row = cut.Find("#ledger-table").QuerySelector("tbody tr");
        Assert.Contains("2,000.00", row!.TextContent);
        Assert.Empty(cut.FindAll("#undo-partial-payment-btn-1"));
    }

    [Fact]
    public void ClickingUndoPartialPayment_OpensAConfirmationModal_NotAnInstantUndo()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [AmexRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#partial-pay-btn-0").Click();
        cut.Find("#modal-amount-input").Change("1000");
        cut.Find("#modal-date-input").Change("2026-07-20");
        cut.Find("#action-modal-apply").Click();

        cut.Find("#undo-partial-payment-btn-1").Click();

        Assert.NotNull(cut.Find("#action-modal"));
        Assert.Equal("Undo this partial payment?", cut.Find("#action-modal-title").TextContent);
        var explanation = cut.Find("#action-modal-explanation").TextContent;
        Assert.Contains("1,000.00", explanation);
        Assert.Contains("07/20/2026", explanation);
        // Still applied - nothing undone yet, just prompted.
        var row = cut.Find("#ledger-table").QuerySelector("tbody tr");
        Assert.Contains("1,000.00", row!.TextContent);
    }

    [Fact]
    public void UndoingAPartialPayment_RestoresTheOriginalAmount()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [AmexRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#partial-pay-btn-0").Click();
        cut.Find("#modal-amount-input").Change("1000");
        cut.Find("#modal-date-input").Change("2026-07-20");
        cut.Find("#action-modal-apply").Click();
        cut.Find("#undo-partial-payment-btn-1").Click();
        cut.Find("#action-modal-apply").Click();

        var row = cut.Find("#ledger-table").QuerySelector("tbody tr");
        Assert.Contains("2,000.00", row!.TextContent);
        Assert.Empty(cut.FindAll("#undo-partial-payment-btn-1"));
    }

    [Fact]
    public void PartialPaymentAction_IsNotShownOnAnExcludedRow()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [AmexRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#override-btn-0").Click();
        cut.Find("#action-modal-apply").Click();

        Assert.Empty(cut.FindAll("#partial-pay-btn-0"));
    }

    [Fact]
    public void EnteringNoAmount_AndApplyingPayPartial_DoesNothing()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [AmexRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#partial-pay-btn-0").Click();
        cut.Find("#action-modal-apply").Click();

        Assert.Empty(cut.FindAll("#action-modal"));
        var row = cut.Find("#ledger-table").QuerySelector("tbody tr");
        Assert.Contains("2,000.00", row!.TextContent);
        Assert.Empty(cut.FindAll("#undo-partial-payment-btn-1"));
    }

    [Fact]
    public void ActionButtons_AreIconsWithADescriptiveTooltip_NotFullText()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [AmexRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();

        var deferBtn = cut.Find("#defer-btn-0");
        Assert.Equal("Defer...", deferBtn.GetAttribute("title"));
        Assert.NotEqual("Defer...", deferBtn.TextContent.Trim());

        var confirmBtn = cut.Find("#confirm-btn-0");
        Assert.Equal("Confirm paid", confirmBtn.GetAttribute("title"));
        Assert.NotEqual("Confirm paid", confirmBtn.TextContent.Trim());

        var overrideBtn = cut.Find("#override-btn-0");
        Assert.Equal("Override", overrideBtn.GetAttribute("title"));
        Assert.NotEqual("Override", overrideBtn.TextContent.Trim());
    }

    [Fact]
    public void RemoveDeferralButton_IsAnIconWithADescriptiveTooltip()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [AmexRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#defer-btn-0").Click();
        cut.Find("#modal-date-input").Change("2026-08-22");
        cut.Find("#action-modal-apply").Click();

        var removeBtn = cut.Find("#remove-deferral-btn-0");
        Assert.Equal("Remove deferral", removeBtn.GetAttribute("title"));
        Assert.NotEqual("Remove deferral", removeBtn.TextContent.Trim());
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
