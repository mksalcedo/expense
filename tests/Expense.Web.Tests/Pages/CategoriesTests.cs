using Bunit;
using Expense.Domain.Entities;
using Expense.Domain.Services.Categories;
using Expense.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Web.Tests.Pages;

public class CategoriesTests : BunitContext
{
    private class FakeCategoriesPageProvider : ICategoriesPageProvider
    {
        public List<CategoryRow> Rows { get; set; } = [];
        public List<AccountOption> Accounts { get; set; } = [];

        public string? LastCreatedName { get; private set; }
        public string? LastCreatedFundingStrategy { get; private set; }
        public DirectBudgetInput? LastCreatedDirectBudget { get; private set; }

        public int? LastUpdatedId { get; private set; }
        public string? LastUpdatedName { get; private set; }
        public string? LastUpdatedFundingStrategy { get; private set; }
        public DirectBudgetInput? LastUpdatedDirectBudget { get; private set; }

        public int? LastDeactivatedId { get; private set; }
        public int? LastReactivatedId { get; private set; }

        public Task<CategoriesPageData> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CategoriesPageData { Categories = Rows, Accounts = Accounts });

        public Task CreateCategoryAsync(string name, string fundingStrategy, DirectBudgetInput? directBudget = null, CancellationToken cancellationToken = default)
        {
            LastCreatedName = name;
            LastCreatedFundingStrategy = fundingStrategy;
            LastCreatedDirectBudget = directBudget;
            return Task.CompletedTask;
        }

        public Task UpdateCategoryAsync(int categoryId, string name, string fundingStrategy, DirectBudgetInput? directBudget = null, CancellationToken cancellationToken = default)
        {
            LastUpdatedId = categoryId;
            LastUpdatedName = name;
            LastUpdatedFundingStrategy = fundingStrategy;
            LastUpdatedDirectBudget = directBudget;
            return Task.CompletedTask;
        }

        public Task DeactivateCategoryAsync(int categoryId, CancellationToken cancellationToken = default)
        {
            LastDeactivatedId = categoryId;
            return Task.CompletedTask;
        }

        public Task ReactivateCategoryAsync(int categoryId, CancellationToken cancellationToken = default)
        {
            LastReactivatedId = categoryId;
            return Task.CompletedTask;
        }
    }

    private static FakeCategoriesPageProvider MakeProvider() => new()
    {
        Rows =
        [
            new CategoryRow { Id = 1, Name = "Groceries", IsActive = true, FundingStrategy = FundingStrategies.PayInFullAmex },
            new CategoryRow { Id = 2, Name = "Off-Budget/Misc", IsActive = true, FundingStrategy = FundingStrategies.None },
            new CategoryRow { Id = 3, Name = "Discontinued Thing", IsActive = false, FundingStrategy = FundingStrategies.None },
            new CategoryRow
            {
                Id = 4, Name = "Truist Mortgage", IsActive = true, FundingStrategy = FundingStrategies.Direct,
                BudgetAmount = 2681.22m, BudgetFrequency = Frequency.Monthly, BudgetDirection = Direction.Expense,
                BudgetAnchor = new DateOnly(2026, 1, 4), BudgetAccountId = 10
            },
            new CategoryRow { Id = 5, Name = "Discover Payment", IsActive = true, FundingStrategy = FundingStrategies.AccountPayment }
        ],
        Accounts = [new AccountOption { Id = 10, Name = "Wells Fargo Checking" }]
    };

    [Fact]
    public void Categories_RendersListWithoutAnOpenFormInitially()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();

        Assert.Contains("Groceries", cut.Markup);
        Assert.Contains("Off-Budget/Misc", cut.Markup);
        Assert.DoesNotContain("id=\"detail-name\"", cut.Markup);
    }

    [Fact]
    public void ClickingARow_PopulatesTheDetailFormWithThatCategorysValues()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#category-row-1").Click();

        Assert.Equal("Groceries", cut.Find("#detail-name").GetAttribute("value"));
        Assert.Equal(FundingStrategies.PayInFullAmex,
            cut.Find("#detail-funding-strategy option[selected]").GetAttribute("value"));
    }

    [Fact]
    public void EditingAndSaving_CallsUpdateWithNameAndFundingStrategyTogether()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#category-row-2").Click(); // Off-Budget/Misc: not funded
        cut.Find("#detail-name").Change("Off-Budget & Misc");
        cut.Find("#detail-funding-strategy").Change(FundingStrategies.PayInFullAmex);
        cut.Find("#detail-save").Click();

        Assert.Equal(2, provider.LastUpdatedId);
        Assert.Equal("Off-Budget & Misc", provider.LastUpdatedName);
        Assert.Equal(FundingStrategies.PayInFullAmex, provider.LastUpdatedFundingStrategy);
    }

    [Fact]
    public void NewCategoryButton_OpensABlankFormThatCreatesOnSave()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#new-category-button").Click();
        cut.Find("#detail-name").Change("Home Improvement");
        cut.Find("#detail-save").Click();

        Assert.Equal("Home Improvement", provider.LastCreatedName);
        Assert.Equal(FundingStrategies.None, provider.LastCreatedFundingStrategy); // left at default
    }

    [Fact]
    public void SelectingAnActiveCategory_ShowsDeactivateNotReactivate()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#category-row-1").Click();
        cut.Find("#detail-deactivate").Click();

        Assert.Equal(1, provider.LastDeactivatedId);
    }

    [Fact]
    public void SelectingAnInactiveCategory_ShowsReactivateNotDeactivate()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#category-row-3").Click();
        cut.Find("#detail-reactivate").Click();

        Assert.Equal(3, provider.LastReactivatedId);
    }

    [Fact]
    public void FilteringByName_HidesNonMatchingRows()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#category-filter").Input("groc");

        Assert.Contains("Groceries", cut.Markup);
        Assert.DoesNotContain("Off-Budget/Misc", cut.Markup);
    }

    [Fact]
    public void SelectingANonDirectCategory_HidesTheDirectBudgetFields()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#category-row-1").Click(); // Groceries: pay-in-full-amex, not Direct

        Assert.Empty(cut.FindAll("#detail-amount"));
    }

    [Fact]
    public void SelectingADirectCategory_PopulatesTheDirectBudgetFields()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#category-row-4").Click(); // Truist Mortgage: Direct

        Assert.Equal("2681.22", cut.Find("#detail-amount").GetAttribute("value"));
        Assert.Equal("2026-01-04", cut.Find("#detail-anchor").GetAttribute("value"));
        Assert.Equal("10", cut.Find("#detail-account option[selected]").GetAttribute("value"));
    }

    [Fact]
    public void SavingADirectCategory_PassesAllFiveBudgetFieldsTogether()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#category-row-4").Click();
        cut.Find("#detail-amount").Change("2750.00");
        cut.Find("#detail-save").Click();

        Assert.Equal(4, provider.LastUpdatedId);
        Assert.NotNull(provider.LastUpdatedDirectBudget);
        Assert.Equal(2750.00m, provider.LastUpdatedDirectBudget!.Amount);
        Assert.Equal(Frequency.Monthly, provider.LastUpdatedDirectBudget.Frequency);
        Assert.Equal(Direction.Expense, provider.LastUpdatedDirectBudget.Direction);
        Assert.Equal(new DateOnly(2026, 1, 4), provider.LastUpdatedDirectBudget.Anchor);
        Assert.Equal(10, provider.LastUpdatedDirectBudget.AccountId);
    }

    [Fact]
    public void SavingANonDirectCategory_PassesNoBudgetInput()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#category-row-1").Click();
        cut.Find("#detail-save").Click();

        Assert.Null(provider.LastUpdatedDirectBudget);
    }

    [Fact]
    public void SelectingAnAccountPaymentCategory_ShowsNoEditableAmountField()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#category-row-5").Click(); // Discover Payment: account_payment

        Assert.Empty(cut.FindAll("#detail-amount"));
        Assert.Contains("linked account", cut.Markup);
    }

    [Fact]
    public void ChoosingDirectStrategyOnANewCategory_ShowsTheBudgetFieldsForEntry()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#new-category-button").Click();
        cut.Find("#detail-funding-strategy").Change(FundingStrategies.Direct);

        Assert.NotNull(cut.Find("#detail-amount"));
        Assert.NotNull(cut.Find("#detail-account"));
    }
}
