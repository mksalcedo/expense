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

        public int? LastUpdatedId { get; private set; }
        public string? LastUpdatedName { get; private set; }
        public bool? LastUpdatedIsBudgeted { get; private set; }
        public string? LastUpdatedFundingStrategy { get; private set; }

        public int? LastDeactivatedId { get; private set; }
        public int? LastReactivatedId { get; private set; }

        public Task<CategoriesPageData> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CategoriesPageData { Categories = Rows });

        public Task CreateCategoryAsync(string name, bool isBudgeted, string fundingStrategy, CancellationToken cancellationToken = default)
        {
            LastCreatedName = name;
            LastCreatedIsBudgeted = isBudgeted;
            LastCreatedFundingStrategy = fundingStrategy;
            return Task.CompletedTask;
        }

        public Task UpdateCategoryAsync(int categoryId, string name, bool isBudgeted, string fundingStrategy, CancellationToken cancellationToken = default)
        {
            LastUpdatedId = categoryId;
            LastUpdatedName = name;
            LastUpdatedIsBudgeted = isBudgeted;
            LastUpdatedFundingStrategy = fundingStrategy;
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
            new CategoryRow { Id = 1, Name = "Groceries", IsBudgeted = true, IsActive = true, FundingStrategy = FundingStrategies.PayInFullAmex },
            new CategoryRow { Id = 2, Name = "Off-Budget/Misc", IsBudgeted = false, IsActive = true, FundingStrategy = FundingStrategies.None },
            new CategoryRow { Id = 3, Name = "Discontinued Thing", IsBudgeted = false, IsActive = false, FundingStrategy = FundingStrategies.None }
        ]
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
        Assert.NotNull(cut.Find("#detail-budgeted").GetAttribute("checked"));
        Assert.NotNull(cut.Find("#detail-funding").GetAttribute("checked")); // Groceries is pay-in-full-amex
    }

    [Fact]
    public void EditingAndSaving_CallsUpdateWithAllThreeFieldsTogether()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#category-row-2").Click(); // Off-Budget/Misc: not budgeted, not funded
        cut.Find("#detail-name").Change("Off-Budget & Misc");
        cut.Find("#detail-budgeted").Change(true);
        cut.Find("#detail-funding").Change(true);
        cut.Find("#detail-save").Click();

        Assert.Equal(2, provider.LastUpdatedId);
        Assert.Equal("Off-Budget & Misc", provider.LastUpdatedName);
        Assert.True(provider.LastUpdatedIsBudgeted);
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
        cut.Find("#detail-budgeted").Change(true);
        cut.Find("#detail-save").Click();

        Assert.Equal("Home Improvement", provider.LastCreatedName);
        Assert.True(provider.LastCreatedIsBudgeted);
        Assert.Equal(FundingStrategies.None, provider.LastCreatedFundingStrategy); // checkbox left unchecked
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
}
