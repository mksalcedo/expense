using Bunit;
using Expense.Domain.Entities;
using Expense.Domain.Services.Forecast;
using Expense.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Web.Tests.Pages;

public class ConfirmedPaymentsTests : BunitContext
{
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
