using Bunit;
using Expense.Domain.Services.Ingestion.ManualCharges;
using Expense.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Web.Tests.Pages;

public class PendingChargesTests : BunitContext
{
    private class FakePendingChargesPageProvider : IPendingChargesPageProvider
    {
        public List<PendingChargeRow> Rows { get; set; } = [];
        public int? LastDeletedId { get; private set; }

        public Task<List<PendingChargeRow>> GetOpenChargesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows);

        public Task DeleteChargeAsync(int transactionId, CancellationToken cancellationToken = default)
        {
            LastDeletedId = transactionId;
            Rows = Rows.Where(r => r.Id != transactionId).ToList();
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void NoOpenCharges_ShowsAnEmptyStateMessage()
    {
        Services.AddSingleton<IPendingChargesPageProvider>(new FakePendingChargesPageProvider());

        var cut = Render<PendingCharges>();

        Assert.Contains("None right now.", cut.Markup);
    }

    [Fact]
    public void OpenCharges_AreListedWithAccountDateDescriptionAndAmount()
    {
        var provider = new FakePendingChargesPageProvider
        {
            Rows =
            [
                new PendingChargeRow
                {
                    Id = 7, AccountName = "Amex", Date = new DateOnly(2026, 7, 20), Description = "MORGAN COMPOUDING",
                    Amount = -131.65m, EnteredAt = new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero)
                }
            ]
        };
        Services.AddSingleton<IPendingChargesPageProvider>(provider);

        var cut = Render<PendingCharges>();

        var row = cut.Find("#pending-charges-table tbody tr");
        Assert.Contains("07/20/2026", row.TextContent);
        Assert.Contains("Amex", row.TextContent);
        Assert.Contains("MORGAN COMPOUDING", row.TextContent);
        Assert.Contains("131.65", row.TextContent);
    }

    [Fact]
    public void ClickingDelete_RemovesTheRowAndCallsTheProvider()
    {
        var provider = new FakePendingChargesPageProvider
        {
            Rows =
            [
                new PendingChargeRow
                {
                    Id = 7, AccountName = "Amex", Date = new DateOnly(2026, 7, 20), Description = "MORGAN COMPOUDING",
                    Amount = -131.65m, EnteredAt = DateTimeOffset.UtcNow
                }
            ]
        };
        Services.AddSingleton<IPendingChargesPageProvider>(provider);

        var cut = Render<PendingCharges>();
        cut.Find("#delete-btn-7").Click();

        Assert.Equal(7, provider.LastDeletedId);
        Assert.Contains("None right now.", cut.Markup);
    }
}
