using Bunit;
using Expense.Domain.Entities;
using Expense.Domain.Services.Categories;
using Expense.Domain.Services.OneTimeEvents;
using Expense.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Web.Tests.Pages;

public class OneTimeEventsTests : BunitContext
{
    private class FakeOneTimeEventsPageProvider : IOneTimeEventsPageProvider
    {
        public List<OneTimeEventRow> Rows { get; set; } = [];
        public List<AccountOption> Accounts { get; set; } = [];

        public string? LastCreatedName { get; private set; }
        public decimal? LastCreatedAmount { get; private set; }
        public Direction? LastCreatedDirection { get; private set; }
        public DateOnly? LastCreatedDate { get; private set; }
        public int? LastCreatedAccountId { get; private set; }

        public int? LastUpdatedId { get; private set; }
        public string? LastUpdatedName { get; private set; }
        public decimal? LastUpdatedAmount { get; private set; }
        public DateOnly? LastUpdatedDate { get; private set; }
        public int? LastUpdatedAccountId { get; private set; }

        public int? LastDeletedId { get; private set; }

        public Task<OneTimeEventsPageData> GetEventsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new OneTimeEventsPageData { Events = Rows, Accounts = Accounts });

        public Task CreateEventAsync(string name, decimal amount, Direction direction, DateOnly date, int accountId, CancellationToken cancellationToken = default)
        {
            LastCreatedName = name;
            LastCreatedAmount = amount;
            LastCreatedDirection = direction;
            LastCreatedDate = date;
            LastCreatedAccountId = accountId;
            return Task.CompletedTask;
        }

        public Task UpdateEventAsync(int eventId, string name, decimal amount, Direction direction, DateOnly date, int accountId, CancellationToken cancellationToken = default)
        {
            LastUpdatedId = eventId;
            LastUpdatedName = name;
            LastUpdatedAmount = amount;
            LastUpdatedDate = date;
            LastUpdatedAccountId = accountId;
            return Task.CompletedTask;
        }

        public Task DeleteEventAsync(int eventId, CancellationToken cancellationToken = default)
        {
            LastDeletedId = eventId;
            return Task.CompletedTask;
        }
    }

    private static FakeOneTimeEventsPageProvider MakeProvider() => new()
    {
        Rows =
        [
            new OneTimeEventRow { Id = 1, Name = "HVAC repair", Amount = 850m, Direction = Direction.Expense, Date = new DateOnly(2026, 7, 20), AccountId = 1, AccountName = "Wells Fargo Checking" },
            new OneTimeEventRow { Id = 2, Name = "Tax refund", Amount = 600m, Direction = Direction.Income, Date = new DateOnly(2026, 4, 15), AccountId = 1, AccountName = "Wells Fargo Checking" }
        ],
        Accounts = [new AccountOption { Id = 1, Name = "Wells Fargo Checking" }]
    };

    [Fact]
    public void OneTimeEvents_RendersListWithoutAnOpenFormInitially()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IOneTimeEventsPageProvider>(provider);

        var cut = Render<OneTimeEvents>();

        Assert.Contains("HVAC repair", cut.Markup);
        Assert.Contains("Tax refund", cut.Markup);
        Assert.DoesNotContain("id=\"detail-name\"", cut.Markup);
    }

    [Fact]
    public void ClickingARow_PopulatesTheDetailFormWithThatEventsValues()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IOneTimeEventsPageProvider>(provider);

        var cut = Render<OneTimeEvents>();
        cut.Find("#event-row-1").Click();

        Assert.Equal("HVAC repair", cut.Find("#detail-name").GetAttribute("value"));
        Assert.Equal("850", cut.Find("#detail-amount").GetAttribute("value"));
        Assert.Equal("2026-07-20", cut.Find("#detail-date").GetAttribute("value"));
        Assert.Equal("1", cut.Find("#detail-account option[selected]").GetAttribute("value"));
    }

    [Fact]
    public void EditingAndSaving_CallsUpdateWithAllFieldsTogether()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IOneTimeEventsPageProvider>(provider);

        var cut = Render<OneTimeEvents>();
        cut.Find("#event-row-1").Click();
        cut.Find("#detail-name").Change("HVAC repair (revised)");
        cut.Find("#detail-amount").Change("900");
        cut.Find("#detail-save").Click();

        Assert.Equal(1, provider.LastUpdatedId);
        Assert.Equal("HVAC repair (revised)", provider.LastUpdatedName);
        Assert.Equal(900m, provider.LastUpdatedAmount);
        Assert.Equal(new DateOnly(2026, 7, 20), provider.LastUpdatedDate);
        Assert.Equal(1, provider.LastUpdatedAccountId);
    }

    [Fact]
    public void NewEventButton_OpensABlankFormThatCreatesOnSave()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IOneTimeEventsPageProvider>(provider);

        var cut = Render<OneTimeEvents>();
        cut.Find("#new-event-button").Click();
        cut.Find("#detail-name").Change("Property Tax");
        cut.Find("#detail-amount").Change("2300");
        cut.Find("#detail-direction").Change(nameof(Direction.Expense));
        cut.Find("#detail-date").Change("2026-11-01");
        cut.Find("#detail-account").Change("1");
        cut.Find("#detail-save").Click();

        Assert.Equal("Property Tax", provider.LastCreatedName);
        Assert.Equal(2300m, provider.LastCreatedAmount);
        Assert.Equal(Direction.Expense, provider.LastCreatedDirection);
        Assert.Equal(new DateOnly(2026, 11, 1), provider.LastCreatedDate);
        Assert.Equal(1, provider.LastCreatedAccountId);
    }

    [Fact]
    public void ClickingDelete_DeletesTheSelectedEvent()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IOneTimeEventsPageProvider>(provider);

        var cut = Render<OneTimeEvents>();
        cut.Find("#event-row-2").Click();
        cut.Find("#detail-delete").Click();

        Assert.Equal(2, provider.LastDeletedId);
    }

    [Fact]
    public void FilteringByName_HidesNonMatchingRows()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IOneTimeEventsPageProvider>(provider);

        var cut = Render<OneTimeEvents>();
        cut.Find("#event-filter").Input("hvac");

        Assert.Contains("HVAC repair", cut.Markup);
        Assert.DoesNotContain("Tax refund", cut.Markup);
    }
}
