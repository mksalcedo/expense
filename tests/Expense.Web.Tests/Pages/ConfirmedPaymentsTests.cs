using Bunit;
using Expense.Domain.Entities;
using Expense.Domain.Services;
using Expense.Domain.Services.Forecast;
using Expense.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Web.Tests.Pages;

public class ConfirmedPaymentsTests : BunitContext
{
    private readonly DataChangeNotifier _dataChangeNotifier = new();

    public ConfirmedPaymentsTests()
    {
        Services.AddSingleton<IDataChangeNotifier>(_dataChangeNotifier);
    }

    private class FakeConfirmedPaymentsPageProvider(List<ConfirmedPaymentRow> rows) : IConfirmedPaymentsPageProvider
    {
        public Task<List<ConfirmedPaymentRow>> GetConfirmedPaymentsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(rows);

        public Task RemoveConfirmationAsync(int confirmationId, CancellationToken cancellationToken = default)
        {
            rows.RemoveAll(r => r.ConfirmationId == confirmationId);
            return Task.CompletedTask;
        }
    }

    private static ConfirmedPaymentRow MakeRow(int id, DateOnly date, string account = "Chase Amazon Prime Visa", decimal amount = -357m) => new()
    {
        ConfirmationId = id, Date = date, OriginalDate = date, AccountId = 5, AccountName = account, Amount = amount,
        Reason = ConfirmationReason.AlreadyPaid, ConfirmedAt = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
    };

    // Real gap this guards (2026-08-17): confirming a payment elsewhere (Forecast) or a
    // background sync auto-reconciling one never used to be reflected here without a
    // manual refresh, since this page only ever loaded its data once.
    [Fact]
    public void DataChangeNotifier_Firing_RefreshesTheList_WithoutNavigatingOrReloading()
    {
        var rows = new List<ConfirmedPaymentRow> { MakeRow(1, new DateOnly(2026, 7, 7)) };
        Services.AddSingleton<IConfirmedPaymentsPageProvider>(new FakeConfirmedPaymentsPageProvider(rows));

        var cut = Render<ConfirmedPayments>();
        Assert.DoesNotContain("Piano", cut.Markup);

        // Simulates a payment confirmed elsewhere (or auto-reconciled by a background
        // sync) - nothing on this page did anything to cause it.
        rows.Add(MakeRow(2, new DateOnly(2026, 8, 1), account: "Piano"));
        _dataChangeNotifier.NotifyChanged();

        cut.WaitForAssertion(() => Assert.Contains("Piano", cut.Markup));
    }

    [Fact]
    public void ConfirmedPayments_RendersEveryRow_RegardlessOfAge()
    {
        var rows = new List<ConfirmedPaymentRow>
        {
            MakeRow(1, new DateOnly(2024, 3, 5)),
            MakeRow(2, new DateOnly(2026, 7, 7))
        };
        Services.AddSingleton<IConfirmedPaymentsPageProvider>(new FakeConfirmedPaymentsPageProvider(rows));

        var cut = Render<ConfirmedPayments>();

        Assert.Equal(2, cut.FindAll("#confirmed-payments-table tbody tr").Count);
    }

    [Fact]
    public void ConfirmedPayments_SortsMostRecentFirst()
    {
        var rows = new List<ConfirmedPaymentRow>
        {
            MakeRow(1, new DateOnly(2024, 3, 5)),
            MakeRow(2, new DateOnly(2026, 7, 7))
        };
        Services.AddSingleton<IConfirmedPaymentsPageProvider>(new FakeConfirmedPaymentsPageProvider(rows));

        var cut = Render<ConfirmedPayments>();

        var dates = cut.FindAll("#confirmed-payments-table tbody tr td:first-child").Select(td => td.TextContent).ToList();
        Assert.Equal(["07/07/2026", "03/05/2024"], dates);
    }

    [Fact]
    public void FilteringByYear_ShowsOnlyThatYear()
    {
        var rows = new List<ConfirmedPaymentRow>
        {
            MakeRow(1, new DateOnly(2024, 3, 5)),
            MakeRow(2, new DateOnly(2026, 7, 7))
        };
        Services.AddSingleton<IConfirmedPaymentsPageProvider>(new FakeConfirmedPaymentsPageProvider(rows));

        var cut = Render<ConfirmedPayments>();
        cut.Find("#filter-year").Change("2024");

        var row = Assert.Single(cut.FindAll("#confirmed-payments-table tbody tr"));
        Assert.Contains("03/05/2024", row.TextContent);
    }

    [Fact]
    public void FilteringByYearAndMonth_NarrowsFurther()
    {
        var rows = new List<ConfirmedPaymentRow>
        {
            MakeRow(1, new DateOnly(2026, 3, 5)),
            MakeRow(2, new DateOnly(2026, 7, 7))
        };
        Services.AddSingleton<IConfirmedPaymentsPageProvider>(new FakeConfirmedPaymentsPageProvider(rows));

        var cut = Render<ConfirmedPayments>();
        cut.Find("#filter-year").Change("2026");
        cut.Find("#filter-month").Change("7");

        var row = Assert.Single(cut.FindAll("#confirmed-payments-table tbody tr"));
        Assert.Contains("07/07/2026", row.TextContent);
    }

    [Fact]
    public void YearFilterOptions_AreBuiltFromTheActualData()
    {
        var rows = new List<ConfirmedPaymentRow> { MakeRow(1, new DateOnly(2024, 3, 5)), MakeRow(2, new DateOnly(2026, 7, 7)) };
        Services.AddSingleton<IConfirmedPaymentsPageProvider>(new FakeConfirmedPaymentsPageProvider(rows));

        var cut = Render<ConfirmedPayments>();

        var options = cut.FindAll("#filter-year option").Select(o => o.TextContent).ToList();
        Assert.Equal(["All years", "2026", "2024"], options);
    }

    [Fact]
    public void UndoingAConfirmedPayment_RemovesItFromTheList()
    {
        var rows = new List<ConfirmedPaymentRow> { MakeRow(1, new DateOnly(2026, 7, 7)) };
        Services.AddSingleton<IConfirmedPaymentsPageProvider>(new FakeConfirmedPaymentsPageProvider(rows));

        var cut = Render<ConfirmedPayments>();
        cut.Find("#undo-confirmation-btn-1").Click();

        Assert.Empty(cut.FindAll("#confirmed-payments-table tbody tr"));
    }
}
