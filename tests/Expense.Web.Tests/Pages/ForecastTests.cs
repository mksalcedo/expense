using Bunit;
using Expense.Domain.Entities;
using Expense.Domain.Services.Accounts;
using Expense.Domain.Services.Forecast;
using Expense.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Web.Tests.Pages;

public class ForecastTests : BunitContext
{
    public ForecastTests()
    {
        // The "show resolved items" preference is read/written via localStorage - Loose mode
        // auto-returns default values for any JS call not explicitly configured, so existing
        // tests that don't care about persistence don't all need their own JSInterop setup.
        JSInterop.Mode = JSRuntimeMode.Loose;

        // Default: no savings accounts - matches every existing test's expectations (no
        // savings row). A test that cares about the savings row registers its own instance,
        // which overrides this one (last registration wins when resolving a single service).
        Services.AddSingleton<IAccountsPageProvider>(new FakeAccountsPageProvider([]));
    }

    private class FakeAccountsPageProvider(List<AccountRow> rows) : IAccountsPageProvider
    {
        public Task<AccountsPageData> GetAccountsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AccountsPageData { Accounts = rows });
        public Task<int> CreateAccountAsync(string name, AccountType type, decimal? minPayment, decimal? extraPayment, int? paymentDueDay, int? statementCloseDay, decimal? apr, DateOnly? paymentStartDate = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAccountAsync(int accountId, string name, decimal? minPayment, decimal? extraPayment, int? paymentDueDay, int? statementCloseDay, decimal? apr, DateOnly? paymentStartDate = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeactivateAccountAsync(int accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReactivateAccountAsync(int accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateBalanceAsync(int accountId, DateOnly asOfDate, decimal balance, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }


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

        public Task ConfirmPaymentAsync(int accountId, int? categoryId, DateOnly originalDate, DateOnly effectiveDate, decimal amount, CancellationToken cancellationToken = default) =>
            ExcludeAsync(accountId, originalDate, effectiveDate, amount, ConfirmationReason.AlreadyPaid);

        public Task OverridePaymentAsync(int accountId, int? categoryId, DateOnly originalDate, DateOnly effectiveDate, decimal amount, CancellationToken cancellationToken = default) =>
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

        public Task PayPartialAmountAsync(int accountId, DateOnly originalDate, DateOnly paidDate, decimal amount, Direction direction, CancellationToken cancellationToken = default)
        {
            var row = result.Rows.Single(r => r.AccountId == accountId && r.OriginalDate == originalDate);
            row.Amount += direction == Direction.Income ? -amount : amount;
            var newId = _nextPartialPaymentId++;
            row.PartialPayments.Add(new PartialPaymentSummary { PartialPaymentId = newId, Amount = amount, PaidDate = paidDate });

            // Mirrors ForecastEngine.BuildPartialPaymentCandidates: recording a payment that
            // matches an existing unclaimed candidate marks *that* candidate claimed, in place,
            // rather than adding a new row - so a suggestion goes struck-through, not doubled.
            if (row.PartialPaymentCandidates is { } candidates)
            {
                var matching = candidates.FirstOrDefault(c => c.Amount == amount && c.Date == paidDate && c.PartialPaymentId is null);
                if (matching is not null)
                {
                    matching.PartialPaymentId = newId;
                }
                else
                {
                    candidates.Add(new PartialPaymentCandidate { Amount = amount, Date = paidDate, PartialPaymentId = newId });
                }
            }
            return Task.CompletedTask;
        }

        public Task RemovePartialPaymentAsync(int partialPaymentId, CancellationToken cancellationToken = default)
        {
            var row = result.Rows.Single(r => r.PartialPayments.Any(p => p.PartialPaymentId == partialPaymentId));
            var partialPayment = row.PartialPayments.Single(p => p.PartialPaymentId == partialPaymentId);
            row.Amount += row.Amount >= 0 ? partialPayment.Amount : -partialPayment.Amount;
            row.PartialPayments.Remove(partialPayment);

            var candidate = row.PartialPaymentCandidates?.SingleOrDefault(c => c.PartialPaymentId == partialPaymentId);
            if (candidate is not null) candidate.PartialPaymentId = null;
            return Task.CompletedTask;
        }
    }

    private static ForecastLedgerRow AmexRow(decimal amount = -2000m, decimal runningBalance = -1000m) => new()
    {
        Date = new DateOnly(2026, 8, 20), Description = "Amex Payment", Amount = amount, RunningBalance = runningBalance,
        AccountId = 2, OriginalDate = new DateOnly(2026, 8, 20)
    };

    private static ForecastLedgerRow PianoRow(decimal amount = 600m, decimal runningBalance = 1600m) => new()
    {
        Date = new DateOnly(2026, 8, 5), Description = "Piano", Amount = amount, RunningBalance = runningBalance,
        AccountId = 1, OriginalDate = new DateOnly(2026, 8, 5)
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

    // Same spreadsheet-style summary table as the Dashboard's Cash Flow section, for
    // consistency wherever the lowest projected balance is reviewed - including the savings
    // buffer and the computed "lowest + savings" row.
    [Fact]
    public void Forecast_ShowsTheSavingsBalance_AndTheComputedLowestPlusSavingsRow()
    {
        var result = new ForecastResult
        {
            StartingBalance = 4209.21m,
            Rows = [new ForecastLedgerRow { Date = new DateOnly(2027, 7, 7), Description = "Water", Amount = -193m, RunningBalance = -109.58m }]
        };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));
        Services.AddSingleton<IAccountsPageProvider>(new FakeAccountsPageProvider(
            [new AccountRow { Id = 6, Name = "Emergency Fund", Type = AccountType.Savings, IsActive = true, LatestBalance = 1545.56m }]));

        var cut = Render<Forecast>();

        Assert.Equal("4,209.21", cut.Find("#starting-balance-row td:last-child").TextContent.Trim());
        Assert.Equal("-109.58", cut.Find("#lowest-balance-row td:last-child").TextContent.Trim());
        Assert.Equal("1,545.56", cut.Find("#savings-row td:last-child").TextContent.Trim());
        Assert.Equal("1,435.98", cut.Find("#lowest-balance-plus-savings-row td:last-child").TextContent.Trim());
    }

    [Fact]
    public void Forecast_WithNoSavingsAccounts_ShowsNoSavingsRow()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();

        Assert.Empty(cut.FindAll("#savings-row"));
        Assert.Empty(cut.FindAll("#lowest-balance-plus-savings-row"));
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

        var rows = cut.Find("#ledger-table").QuerySelectorAll("tbody tr");
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

        var headers = cut.Find("#ledger-table").QuerySelectorAll("th");
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

        var cells = cut.Find("#ledger-table").QuerySelectorAll("tbody td");
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

        var ledgerTable = cut.Find("#ledger-table");
        Assert.Contains("border-collapse: collapse", ledgerTable.GetAttribute("style"));
        foreach (var cell in ledgerTable.QuerySelectorAll("th").Concat(ledgerTable.QuerySelectorAll("td")))
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
        var row = cut.Find("#ledger-table").QuerySelector("tbody tr");
        Assert.Contains("background-color: orange", row!.GetAttribute("style"));
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

        Assert.Single(cut.Find("#ledger-table").QuerySelectorAll("tbody tr"));
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
    public void ClickingOverride_OpensAModalWithEditableAmountAndDate_PreFilledWithTheCurrentValues()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [AmexRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#override-btn-0").Click();

        Assert.Equal("Change this amount?", cut.Find("#action-modal-title").TextContent);
        Assert.Equal("-2000", cut.Find("#modal-amount-input").GetAttribute("value"));
        Assert.Equal("2026-08-20", cut.Find("#modal-date-input").GetAttribute("value"));
    }

    [Fact]
    public void ClickingOverride_WithASuggestedAmount_PreFillsTheModalWithIt_NotTheBudgetedAmount()
    {
        // Real case this covers: a $70.97 real Gas bill against a $76.68 budgeted line -
        // Override should pre-fill with the real number instead of leaving the user to look
        // it up themselves.
        var row = AmexRow(amount: -76.68m);
        row.SuggestedOverrideAmount = -70.97m;
        row.SuggestedOverrideDate = new DateOnly(2026, 7, 31);
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [row] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#override-btn-0").Click();

        Assert.Equal("-70.97", cut.Find("#modal-amount-input").GetAttribute("value"));
        Assert.Equal("2026-07-31", cut.Find("#modal-date-input").GetAttribute("value"));
        var explanation = cut.Find("#action-modal-explanation").TextContent;
        Assert.Contains("-70.97", explanation);
        Assert.Contains("07/31/2026", explanation);
    }

    [Fact]
    public void RowWithASuggestedOverrideAmount_ShowsAnInlineNote_WithoutOpeningTheModal()
    {
        var row = AmexRow(amount: -76.68m);
        row.SuggestedOverrideAmount = -70.97m;
        row.SuggestedOverrideDate = new DateOnly(2026, 7, 31);
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [row] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();

        var note = cut.Find("#near-miss-note-0").TextContent;
        Assert.Contains("70.97", note);
        Assert.Contains("07/31/2026", note);
    }

    [Fact]
    public void RowWithNoSuggestedOverrideAmount_ShowsNoInlineNote()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [AmexRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();

        Assert.Empty(cut.FindAll("#near-miss-note-0"));
    }

    [Fact]
    public void CancellingTheOverrideModal_WithEditedFields_LeavesTheRowUntouched()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [AmexRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#override-btn-0").Click();
        cut.Find("#modal-amount-input").Change("-1.00");
        cut.Find("#action-modal-cancel").Click();

        Assert.Empty(cut.FindAll("#action-modal"));
        var row = cut.Find("#ledger-table").QuerySelector("tbody tr");
        Assert.Contains("2,000.00", row!.TextContent);
        Assert.DoesNotContain("Overridden", row.TextContent);
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
    public void OverridingWithAnEditedAmountAndDate_UsesTheEditedValues_NotTheOriginal()
    {
        var row = AmexRow(amount: -76.68m);
        row.SuggestedOverrideAmount = -70.97m;
        row.SuggestedOverrideDate = new DateOnly(2026, 7, 31);
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [row] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#override-btn-0").Click();
        cut.Find("#modal-amount-input").Change("-70.97");
        cut.Find("#modal-date-input").Change("2026-07-31");
        cut.Find("#action-modal-apply").Click();

        var ledgerRow = cut.Find("#ledger-table").QuerySelector("tbody tr");
        Assert.Contains("70.97", ledgerRow!.TextContent);
        Assert.Contains("07/31/2026", ledgerRow.TextContent);
        Assert.DoesNotContain("76.68", ledgerRow.TextContent);
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

    // Real bug this guards (found live 2026-08-04, user-identified): the partial-payment
    // button/modal/note wording was written only for the expense case ("Pay", "paid") - using
    // it as-is on an income line like Piano would have been actively misleading, since you're
    // recording money received, not money paid out.
    [Fact]
    public void IncomeRow_ShowsRecordPartialIncomeButtonTitle_NotPayPartialAmount()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [PianoRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();

        Assert.Equal("Record partial income", cut.Find("#partial-pay-btn-0").GetAttribute("title"));
    }

    [Fact]
    public void ExpenseRow_StillShowsPayPartialAmountButtonTitle()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [AmexRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();

        Assert.Equal("Pay partial amount", cut.Find("#partial-pay-btn-0").GetAttribute("title"));
    }

    [Fact]
    public void ClickingPartialPayOnAnIncomeRow_ShowsDateReceivedLabel_NotDatePaid()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [PianoRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#partial-pay-btn-0").Click();

        Assert.Equal("Record partial income?", cut.Find("#action-modal-title").TextContent);
        var modal = cut.Find("#action-modal");
        Assert.Contains("Date received", modal.TextContent);
        Assert.DoesNotContain("Date paid", modal.TextContent);
    }

    [Fact]
    public void RecordingPartialIncome_ReducesTheRemainingExpectedAmount_NotInflatesIt()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [PianoRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#partial-pay-btn-0").Click();
        cut.Find("#modal-amount-input").Change("429");
        cut.Find("#modal-date-input").Change("2026-07-22");
        cut.Find("#action-modal-apply").Click();

        var row = cut.Find("#ledger-table").QuerySelector("tbody tr");
        Assert.Contains("171.00", row!.TextContent);
        Assert.DoesNotContain("1,029.00", row.TextContent);
    }

    [Fact]
    public void PartialIncomeNote_ShowsReceivedWording_NotPaid()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [PianoRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#partial-pay-btn-0").Click();
        cut.Find("#modal-amount-input").Change("429");
        cut.Find("#modal-date-input").Change("2026-07-22");
        cut.Find("#action-modal-apply").Click();

        var row = cut.Find("#ledger-table").QuerySelector("tbody tr");
        Assert.Contains("Received $429.00 on 07/22/2026", row!.TextContent);
        Assert.DoesNotContain("Paid $429.00", row.TextContent);
    }

    // Real gap this guards (found live 2026-08-04, user-identified): a single near-miss
    // suggestion pointed at Change Amount is wrong for a multi-payer category like Piano -
    // accepting just one real transaction via Change Amount would wrongly mark the *entire*
    // line resolved, writing off the rest of the month's still-expected income. Categories
    // flagged ReconcileByCalendarMonth get a full list of individually-recordable candidates
    // instead, and no single-suggestion Change Amount prompt at all.
    [Fact]
    public void RowWithPartialPaymentCandidates_ShowsEachOneIndividually_WithItsOwnRecordAction()
    {
        var row = PianoRow();
        row.PartialPaymentCandidates =
        [
            new PartialPaymentCandidate { Amount = 95m, Date = new DateOnly(2026, 8, 2) },
            new PartialPaymentCandidate { Amount = 25m, Date = new DateOnly(2026, 8, 3) }
        ];
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [row] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();

        Assert.Contains("95.00 on 08/02/2026", cut.Find("#suggested-partial-0-0").TextContent);
        Assert.Contains("25.00 on 08/03/2026", cut.Find("#suggested-partial-0-1").TextContent);
        Assert.NotNull(cut.Find("#record-suggested-partial-btn-0-0"));
        Assert.NotNull(cut.Find("#record-suggested-partial-btn-0-1"));
        // No single "found nearby - review with Change Amount" prompt for this kind of category.
        Assert.Empty(cut.FindAll("#near-miss-note-0"));
    }

    [Fact]
    public void ClickingRecordOnACandidate_PreFillsTheModalWithItsExactAmountAndDate()
    {
        var row = PianoRow();
        row.PartialPaymentCandidates = [new PartialPaymentCandidate { Amount = 95m, Date = new DateOnly(2026, 8, 2) }];
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [row] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#record-suggested-partial-btn-0-0").Click();

        Assert.Equal("95", cut.Find("#modal-amount-input").GetAttribute("value"));
        Assert.Equal("2026-08-02", cut.Find("#modal-date-input").GetAttribute("value"));
    }

    [Fact]
    public void RecordingACandidate_MovesItToStruckThrough_InTheSameList_NotADifferentOne()
    {
        var row = PianoRow();
        row.PartialPaymentCandidates =
        [
            new PartialPaymentCandidate { Amount = 95m, Date = new DateOnly(2026, 8, 2) },
            new PartialPaymentCandidate { Amount = 25m, Date = new DateOnly(2026, 8, 3) }
        ];
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [row] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#record-suggested-partial-btn-0-0").Click();
        cut.Find("#action-modal-apply").Click();

        // The recorded one is gone from the "not yet recorded" list...
        Assert.Empty(cut.FindAll("#suggested-partial-0-0"));
        var ledgerRow = cut.Find("#ledger-table").QuerySelector("tbody tr");
        Assert.Contains("Received $95.00 on 08/02/2026", ledgerRow!.TextContent);
        Assert.NotNull(cut.Find("#undo-partial-payment-btn-1"));
        // ...while the still-unclaimed one is untouched, still in the list.
        Assert.Contains("25.00 on 08/03/2026", cut.Find("#suggested-partial-0-1").TextContent);
        // The remaining expected amount shrank correctly (600 - 95 = 505).
        Assert.Contains("505.00", ledgerRow.TextContent);
    }

    [Fact]
    public void UndoingARecordedCandidate_RestoresItToTheUnclaimedList()
    {
        var row = PianoRow();
        row.PartialPaymentCandidates = [new PartialPaymentCandidate { Amount = 95m, Date = new DateOnly(2026, 8, 2) }];
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [row] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#record-suggested-partial-btn-0-0").Click();
        cut.Find("#action-modal-apply").Click();
        cut.Find("#undo-partial-payment-btn-1").Click();
        cut.Find("#action-modal-apply").Click();

        Assert.Contains("95.00 on 08/02/2026", cut.Find("#suggested-partial-0-0").TextContent);
        Assert.Empty(cut.FindAll("#undo-partial-payment-btn-1"));
        var ledgerRow = cut.Find("#ledger-table").QuerySelector("tbody tr");
        Assert.Contains("600.00", ledgerRow!.TextContent);
    }

    [Fact]
    public void RowWithAnEmptyCandidatesList_ShowsNoSuggestionsAndNoNearMissNote()
    {
        var row = PianoRow();
        row.PartialPaymentCandidates = [];
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [row] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();

        Assert.Empty(cut.FindAll("#suggested-partial-0-0"));
        Assert.Empty(cut.FindAll("#near-miss-note-0"));
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
        Assert.Equal("Change amount", overrideBtn.GetAttribute("title"));
        Assert.NotEqual("Change amount", overrideBtn.TextContent.Trim());
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
    public void Forecast_ShowsAShowResolvedToggle_CheckedByDefault()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [AmexRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();

        Assert.True(cut.Find("#show-resolved-toggle").HasAttribute("checked"));
    }

    [Fact]
    public void UncheckingShowResolved_HidesExcludedRows_ButLeavesNormalRowsVisible()
    {
        var result = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows =
            [
                AmexRow(),
                new ForecastLedgerRow { Date = new DateOnly(2026, 8, 25), Description = "GPC", Amount = -351m, RunningBalance = -1351m, AccountId = 1, OriginalDate = new DateOnly(2026, 8, 25) }
            ]
        };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#override-btn-0").Click();
        cut.Find("#action-modal-apply").Click();
        cut.Find("#show-resolved-toggle").Change(false);

        Assert.DoesNotContain("Amex Payment", cut.Markup);
        Assert.Contains("GPC", cut.Markup);
    }

    [Fact]
    public void ReCheckingShowResolved_BringsExcludedRowsBack()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [AmexRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#override-btn-0").Click();
        cut.Find("#action-modal-apply").Click();
        cut.Find("#show-resolved-toggle").Change(false);
        cut.Find("#show-resolved-toggle").Change(true);

        Assert.Contains("Amex Payment", cut.Markup);
    }

    [Fact]
    public void UncheckingShowResolved_DoesNotHideDeferredOrPartiallyPaidRows()
    {
        // Deferred/partially-paid rows are never IsExcluded (still fully or partially owed),
        // so the toggle - scoped to resolved/settled items only - must never touch them.
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [AmexRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));

        var cut = Render<Forecast>();
        cut.Find("#defer-btn-0").Click();
        cut.Find("#modal-date-input").Change("2026-08-22");
        cut.Find("#action-modal-apply").Click();
        cut.Find("#show-resolved-toggle").Change(false);

        Assert.Contains("Amex Payment", cut.Markup);
    }

    [Fact]
    public void UncheckingShowResolved_SavesThePreferenceToLocalStorage()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [AmexRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));
        var setItemCall = JSInterop.SetupVoid("localStorage.setItem", _ => true).SetVoidResult();

        var cut = Render<Forecast>();
        cut.Find("#show-resolved-toggle").Change(false);

        setItemCall.VerifyInvoke("localStorage.setItem");
    }

    [Fact]
    public void OnLoad_UsesTheSavedShowResolvedPreferenceFromLocalStorage()
    {
        var result = new ForecastResult { StartingBalance = 1000m, Rows = [AmexRow()] };
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(result));
        JSInterop.Setup<string?>("localStorage.getItem", _ => true).SetResult("false");

        var cut = Render<Forecast>();
        cut.Find("#override-btn-0").Click();
        cut.Find("#action-modal-apply").Click();

        Assert.False(cut.Find("#show-resolved-toggle").HasAttribute("checked"));
        Assert.DoesNotContain("Amex Payment", cut.Markup);
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
