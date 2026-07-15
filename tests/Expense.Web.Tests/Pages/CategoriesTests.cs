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

        public string? LastCreatedName { get; private set; }
        public bool? LastCreatedIsBudgeted { get; private set; }
        public string? LastCreatedFundingStrategy { get; private set; }

        public int? LastRenamedId { get; private set; }
        public string? LastNewName { get; private set; }

        public int? LastFundingStrategyChangedId { get; private set; }
        public string? LastNewFundingStrategy { get; private set; }

        public int? LastDeactivatedId { get; private set; }

        public Task<CategoriesPageData> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CategoriesPageData { Categories = Rows });

        public Task CreateCategoryAsync(string name, bool isBudgeted, string fundingStrategy, CancellationToken cancellationToken = default)
        {
            LastCreatedName = name;
            LastCreatedIsBudgeted = isBudgeted;
            LastCreatedFundingStrategy = fundingStrategy;
            return Task.CompletedTask;
        }

        public Task RenameCategoryAsync(int categoryId, string newName, CancellationToken cancellationToken = default)
        {
            LastRenamedId = categoryId;
            LastNewName = newName;
            return Task.CompletedTask;
        }

        public Task SetFundingStrategyAsync(int categoryId, string strategy, CancellationToken cancellationToken = default)
        {
            LastFundingStrategyChangedId = categoryId;
            LastNewFundingStrategy = strategy;
            return Task.CompletedTask;
        }

        public Task DeactivateCategoryAsync(int categoryId, CancellationToken cancellationToken = default)
        {
            LastDeactivatedId = categoryId;
            return Task.CompletedTask;
        }
    }

    private static FakeCategoriesPageProvider MakeProvider() => new()
    {
        Rows =
        [
            new CategoryRow { Id = 1, Name = "Groceries", IsBudgeted = true, IsActive = true, FundingStrategy = FundingStrategies.PayInFullAmex },
            new CategoryRow { Id = 2, Name = "Off-Budget/Misc", IsBudgeted = false, IsActive = true, FundingStrategy = FundingStrategies.None }
        ]
    };

    [Fact]
    public void Categories_RendersExistingCategoriesWithTheirState()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();

        Assert.Contains("Groceries", cut.Markup);
        Assert.Contains("Off-Budget/Misc", cut.Markup);
    }

    [Fact]
    public void RenamingACategory_CallsProviderWithNewName()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#category-name-1").Change("Groceries & Household");

        Assert.Equal(1, provider.LastRenamedId);
        Assert.Equal("Groceries & Household", provider.LastNewName);
    }

    [Fact]
    public void ChangingFundingStrategy_CallsProviderWithNewStrategy()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#category-funding-2").Change(FundingStrategies.PayInFullAmex);

        Assert.Equal(2, provider.LastFundingStrategyChangedId);
        Assert.Equal(FundingStrategies.PayInFullAmex, provider.LastNewFundingStrategy);
    }

    [Fact]
    public void ClickingDeactivate_CallsProviderWithThatCategoryId()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#category-deactivate-1").Click();

        Assert.Equal(1, provider.LastDeactivatedId);
    }

    [Fact]
    public void SubmittingNewCategoryForm_CallsProviderWithEnteredValues()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#new-category-name").Change("Home Improvement");
        cut.Find("#new-category-budgeted").Change(true);
        cut.Find("#new-category-funding").Change(FundingStrategies.None);
        cut.Find("#new-category-submit").Click();

        Assert.Equal("Home Improvement", provider.LastCreatedName);
        Assert.True(provider.LastCreatedIsBudgeted);
        Assert.Equal(FundingStrategies.None, provider.LastCreatedFundingStrategy);
    }
}
