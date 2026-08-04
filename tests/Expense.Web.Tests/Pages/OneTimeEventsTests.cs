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
        public List<CategoryOption> Categories { get; set; } = [];

        public string? LastCreatedName { get; private set; }
        public decimal? LastCreatedAmount { get; private set; }
        public Direction? LastCreatedDirection { get; private set; }
        public DateOnly? LastCreatedDate { get; private set; }
        public int? LastCreatedAccountId { get; private set; }
        public int? LastCreatedCategoryId { get; private set; }

        public int? LastUpdatedId { get; private set; }
        public string? LastUpdatedName { get; private set; }
        public decimal? LastUpdatedAmount { get; private set; }
        public DateOnly? LastUpdatedDate { get; private set; }
        public int? LastUpdatedAccountId { get; private set; }
        public int? LastUpdatedCategoryId { get; private set; }

        public int? LastDeletedId { get; private set; }

        public Task<OneTimeEventsPageData> GetEventsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new OneTimeEventsPageData { Events = Rows, Accounts = Accounts, Categories = Categories });

        public Task CreateEventAsync(string name, decimal amount, Direction direction, DateOnly date, int accountId, int? categoryId, CancellationToken cancellationToken = default)
        {
            LastCreatedName = name;
            LastCreatedAmount = amount;
            LastCreatedDirection = direction;
            LastCreatedDate = date;
            LastCreatedAccountId = accountId;
            LastCreatedCategoryId = categoryId;
            return Task.CompletedTask;
        }

        public Task UpdateEventAsync(int eventId, string name, decimal amount, Direction direction, DateOnly date, int accountId, int? categoryId, CancellationToken cancellationToken = default)
        {
            LastUpdatedId = eventId;
            LastUpdatedName = name;
            LastUpdatedAmount = amount;
            LastUpdatedDate = date;
            LastUpdatedAccountId = accountId;
            LastUpdatedCategoryId = categoryId;
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
            new OneTimeEventRow
            {
                Id = 2, Name = "Water additional", Amount = 185.20m, Direction = Direction.Expense, Date = new DateOnly(2026, 7, 30),
                AccountId = 1, AccountName = "Wells Fargo Checking", CategoryId = 5, CategoryName = "Water"
            }
        ],
        Accounts = [new AccountOption { Id = 1, Name = "Wells Fargo Checking" }],
        Categories = [new CategoryOption { Id = 5, Name = "Water" }]
    };

    [Fact]
    public void OneTimeEvents_RendersListWithoutAnOpenFormInitially()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IOneTimeEventsPageProvider>(provider);

        var cut = Render<OneTimeEvents>();

        Assert.Contains("HVAC repair", cut.Markup);
        Assert.Contains("Water additional", cut.Markup);
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
    public void ClickingARowWithACategory_PopulatesTheCategoryDropdown()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IOneTimeEventsPageProvider>(provider);

        var cut = Render<OneTimeEvents>();
        cut.Find("#event-row-2").Click();

        Assert.Equal("5", cut.Find("#detail-category option[selected]").GetAttribute("value"));
    }

    [Fact]
    public void ClickingARowWithNoCategory_LeavesTheCategoryDropdownOnNone()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IOneTimeEventsPageProvider>(provider);

        var cut = Render<OneTimeEvents>();
        cut.Find("#event-row-1").Click();

        Assert.Equal("", cut.Find("#detail-category option[selected]").GetAttribute("value"));
    }

    // Category is deliberately still optional (not every event maps to one - an Amex partial
    // payment top-up has no single category), but leaving it blank had been silently easy to
    // miss (user-identified real gap, 2026-08-03 audit: a real Water top-up event went
    // un-reconciled because its category was never set). A visible warning makes skipping it a
    // conscious choice instead of an invisible default.
    [Fact]
    public void NoCategorySelected_ShowsAWarning()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IOneTimeEventsPageProvider>(provider);

        var cut = Render<OneTimeEvents>();
        cut.Find("#event-row-1").Click(); // HVAC repair - no category in the fixture

        Assert.NotEmpty(cut.FindAll("#category-warning"));
    }

    [Fact]
    public void SelectingACategory_HidesTheWarning()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IOneTimeEventsPageProvider>(provider);

        var cut = Render<OneTimeEvents>();
        cut.Find("#event-row-1").Click();
        Assert.NotEmpty(cut.FindAll("#category-warning"));

        cut.Find("#detail-category").Change("5");

        Assert.Empty(cut.FindAll("#category-warning"));
    }

    [Fact]
    public void ClickingARowWithACategoryAlreadySet_ShowsNoWarning()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IOneTimeEventsPageProvider>(provider);

        var cut = Render<OneTimeEvents>();
        cut.Find("#event-row-2").Click(); // has CategoryId = 5 in the fixture

        Assert.Empty(cut.FindAll("#category-warning"));
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
        Assert.Null(provider.LastUpdatedCategoryId);
    }

    [Fact]
    public void SettingACategoryAndSaving_CallsUpdateWithTheCategoryId()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IOneTimeEventsPageProvider>(provider);

        var cut = Render<OneTimeEvents>();
        cut.Find("#event-row-1").Click();
        cut.Find("#detail-category").Change("5");
        cut.Find("#detail-save").Click();

        Assert.Equal(5, provider.LastUpdatedCategoryId);
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
        Assert.Null(provider.LastCreatedCategoryId);
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
        Assert.DoesNotContain("Water additional", cut.Markup);
    }
}
