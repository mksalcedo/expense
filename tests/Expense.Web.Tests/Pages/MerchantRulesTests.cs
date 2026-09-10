using Bunit;
using Expense.Domain.Entities;
using Expense.Domain.Services.Categorization;
using Expense.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Web.Tests.Pages;

public class MerchantRulesTests : BunitContext
{
    private class FakeProvider : IMerchantRulesPageProvider
    {
        public List<MerchantRule> Rules { get; set; } = [];
        public List<Category> Categories { get; set; } =
        [
            new() { Id = 1, Name = "Groceries" },
            new() { Id = 2, Name = "Piano" },
            new() { Id = 3, Name = "Venmo Credit Card Payment" },
        ];

        public string? LastPattern { get; private set; }
        public Direction? LastDirection { get; private set; }
        public int? LastCategoryId { get; private set; }
        public int? LastUpdatedId { get; private set; }
        public int? LastDeletedId { get; private set; }
        public int NextReapplyCount { get; set; }

        public Task<MerchantRulesPageData> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new MerchantRulesPageData { Rules = Rules, Categories = Categories });

        public Task<int> CreateAsync(string merchantPattern, Direction? direction, int categoryId, CancellationToken cancellationToken = default)
        {
            LastPattern = merchantPattern;
            LastDirection = direction;
            LastCategoryId = categoryId;
            return Task.FromResult(NextReapplyCount);
        }

        public Task<int> UpdateAsync(int id, string merchantPattern, Direction? direction, int categoryId, CancellationToken cancellationToken = default)
        {
            LastUpdatedId = id;
            LastPattern = merchantPattern;
            LastDirection = direction;
            LastCategoryId = categoryId;
            return Task.FromResult(NextReapplyCount);
        }

        public Task DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            LastDeletedId = id;
            Rules = Rules.Where(r => r.Id != id).ToList();
            return Task.CompletedTask;
        }
    }

    private FakeProvider Register(params MerchantRule[] rules)
    {
        var provider = new FakeProvider { Rules = rules.ToList() };
        Services.AddSingleton<IMerchantRulesPageProvider>(provider);
        return provider;
    }

    [Fact]
    public void ListsEveryRule_WithPatternDirectionAndCategoryPreselected()
    {
        Register(
            new MerchantRule { Id = 10, MerchantPattern = "VENMO", CategoryId = 3, Direction = Direction.Expense },
            new MerchantRule { Id = 11, MerchantPattern = "KROGER", CategoryId = 1, Direction = null });

        var cut = Render<MerchantRules>();

        Assert.Equal("VENMO", cut.Find("#rule-pattern-10").GetAttribute("value"));
        var direction10 = cut.Find("#rule-direction-10");
        Assert.Equal("Expense", direction10.Children.First(o => o.HasAttribute("selected")).GetAttribute("value"));
        var direction11 = cut.Find("#rule-direction-11");
        Assert.Equal("", direction11.Children.First(o => o.HasAttribute("selected")).GetAttribute("value"));
        var category10 = cut.Find("#rule-category-10");
        Assert.Equal("3", category10.Children.First(o => o.HasAttribute("selected")).GetAttribute("value"));
    }

    [Fact]
    public void AddingARule_CallsCreate_WithDirection_AndReportsReFiledCount()
    {
        var provider = Register();
        provider.NextReapplyCount = 2;

        var cut = Render<MerchantRules>();
        cut.Find("#new-rule-pattern").Change("VENMO");
        cut.Find("#new-rule-direction").Change("Income");
        cut.Find("#new-rule-category").Change("2");
        cut.Find("#new-rule-add").Click();

        Assert.Equal("VENMO", provider.LastPattern);
        Assert.Equal(Direction.Income, provider.LastDirection);
        Assert.Equal(2, provider.LastCategoryId);
        Assert.Contains("2 transaction(s)", cut.Find("#rules-message").TextContent);
    }

    [Fact]
    public void AddingARule_WithAnyDirection_PassesNull()
    {
        var provider = Register();

        var cut = Render<MerchantRules>();
        cut.Find("#new-rule-pattern").Change("KROGER");
        cut.Find("#new-rule-category").Change("1");
        cut.Find("#new-rule-add").Click();

        Assert.Null(provider.LastDirection);
        Assert.Equal("KROGER", provider.LastPattern);
    }

    [Fact]
    public void AddButton_IsDisabled_UntilPatternAndCategoryAreSet()
    {
        Register();

        var cut = Render<MerchantRules>();

        Assert.True(cut.Find("#new-rule-add").HasAttribute("disabled"));
        cut.Find("#new-rule-pattern").Change("VENMO");
        Assert.True(cut.Find("#new-rule-add").HasAttribute("disabled")); // still no category
        cut.Find("#new-rule-category").Change("2");
        Assert.False(cut.Find("#new-rule-add").HasAttribute("disabled"));
    }

    [Fact]
    public void ChangingARowsDirection_CallsUpdate_WithTheRowsOtherFields()
    {
        var provider = Register(new MerchantRule { Id = 10, MerchantPattern = "VENMO", CategoryId = 3, Direction = null });

        var cut = Render<MerchantRules>();
        cut.Find("#rule-direction-10").Change("Expense");

        Assert.Equal(10, provider.LastUpdatedId);
        Assert.Equal("VENMO", provider.LastPattern);
        Assert.Equal(Direction.Expense, provider.LastDirection);
        Assert.Equal(3, provider.LastCategoryId);
    }

    [Fact]
    public void ChangingARowsCategory_CallsUpdate()
    {
        var provider = Register(new MerchantRule { Id = 10, MerchantPattern = "VENMO", CategoryId = 3, Direction = Direction.Income });

        var cut = Render<MerchantRules>();
        cut.Find("#rule-category-10").Change("2");

        Assert.Equal(10, provider.LastUpdatedId);
        Assert.Equal(Direction.Income, provider.LastDirection);
        Assert.Equal(2, provider.LastCategoryId);
    }

    [Fact]
    public void DeletingARule_CallsDelete_AndRemovesTheRow()
    {
        var provider = Register(new MerchantRule { Id = 10, MerchantPattern = "VENMO", CategoryId = 3 });

        var cut = Render<MerchantRules>();
        cut.Find("#rule-delete-10").Click();

        Assert.Equal(10, provider.LastDeletedId);
        Assert.Empty(cut.FindAll("#rule-delete-10"));
    }
}
