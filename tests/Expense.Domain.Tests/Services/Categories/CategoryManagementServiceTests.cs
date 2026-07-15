using Expense.Domain.Entities;
using Expense.Domain.Services.Categories;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Services.Categories;

public class CategoryManagementServiceTests : DatabaseTestBase
{
    private readonly CategoryManagementService _sut = new();

    [Fact]
    public async Task CreateCategoryAsync_CreatesCategoryAndItsFundingRuleTogether()
    {
        var category = await _sut.CreateCategoryAsync(Context, "Home Improvement", isBudgeted: true, fundingStrategy: FundingStrategies.None);

        Assert.Equal("Home Improvement", category.Name);
        Assert.True(category.IsBudgeted);
        Assert.True(category.IsActive);

        var rule = await Context.FundingRules.SingleAsync(r => r.CategoryId == category.Id);
        Assert.Equal(FundingStrategies.None, rule.Strategy);
    }

    [Fact]
    public async Task CreateCategoryAsync_WithPayInFullAmexStrategy_CreatesRuleAccordingly()
    {
        var category = await _sut.CreateCategoryAsync(Context, "Medical", isBudgeted: true, fundingStrategy: FundingStrategies.PayInFullAmex);

        var rule = await Context.FundingRules.SingleAsync(r => r.CategoryId == category.Id);
        Assert.Equal(FundingStrategies.PayInFullAmex, rule.Strategy);
    }

    [Fact]
    public async Task RenameCategoryAsync_UpdatesTheName()
    {
        var category = await _sut.CreateCategoryAsync(Context, "Groceries", isBudgeted: true, fundingStrategy: FundingStrategies.PayInFullAmex);

        await _sut.RenameCategoryAsync(Context, category.Id, "Groceries & Household");

        var reloaded = await Context.Categories.SingleAsync(c => c.Id == category.Id);
        Assert.Equal("Groceries & Household", reloaded.Name);
    }

    [Fact]
    public async Task SetFundingStrategyAsync_WhenRuleAlreadyExists_UpdatesItInPlace()
    {
        var category = await _sut.CreateCategoryAsync(Context, "Groceries", isBudgeted: true, fundingStrategy: FundingStrategies.None);

        await _sut.SetFundingStrategyAsync(Context, category.Id, FundingStrategies.PayInFullAmex);

        var rule = await Context.FundingRules.SingleAsync(r => r.CategoryId == category.Id);
        Assert.Equal(FundingStrategies.PayInFullAmex, rule.Strategy);
    }

    [Fact]
    public async Task SetFundingStrategyAsync_WhenNoRuleExistsYet_CreatesOne()
    {
        // Mirrors real data: Off-Budget/Misc was seeded with no funding_rules row at all
        var category = new Category { Name = "Off-Budget/Misc", IsBudgeted = false };
        Context.Categories.Add(category);
        await Context.SaveChangesAsync();

        await _sut.SetFundingStrategyAsync(Context, category.Id, FundingStrategies.None);

        var rule = await Context.FundingRules.SingleAsync(r => r.CategoryId == category.Id);
        Assert.Equal(FundingStrategies.None, rule.Strategy);
    }

    [Fact]
    public async Task DeactivateCategoryAsync_SoftDeletesOnly()
    {
        var category = await _sut.CreateCategoryAsync(Context, "Discontinued", isBudgeted: false, fundingStrategy: FundingStrategies.None);

        await _sut.DeactivateCategoryAsync(Context, category.Id);

        var reloaded = await Context.Categories.SingleAsync(c => c.Id == category.Id);
        Assert.False(reloaded.IsActive);
    }

    [Fact]
    public async Task ReactivateCategoryAsync_UndoesADeactivation()
    {
        var category = await _sut.CreateCategoryAsync(Context, "Reconsidered", isBudgeted: false, fundingStrategy: FundingStrategies.None);
        await _sut.DeactivateCategoryAsync(Context, category.Id);

        await _sut.ReactivateCategoryAsync(Context, category.Id);

        var reloaded = await Context.Categories.SingleAsync(c => c.Id == category.Id);
        Assert.True(reloaded.IsActive);
    }

    [Fact]
    public async Task UpdateCategoryAsync_SavesNameBudgetedAndFundingStrategyTogether()
    {
        var category = await _sut.CreateCategoryAsync(Context, "Groceries", isBudgeted: true, fundingStrategy: FundingStrategies.None);

        await _sut.UpdateCategoryAsync(Context, category.Id, "Groceries & Household", isBudgeted: false, fundingStrategy: FundingStrategies.PayInFullAmex);

        var reloaded = await Context.Categories.SingleAsync(c => c.Id == category.Id);
        Assert.Equal("Groceries & Household", reloaded.Name);
        Assert.False(reloaded.IsBudgeted);

        var rule = await Context.FundingRules.SingleAsync(r => r.CategoryId == category.Id);
        Assert.Equal(FundingStrategies.PayInFullAmex, rule.Strategy);
    }
}
