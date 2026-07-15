using Expense.Domain.Entities;
using Expense.Domain.Services.Budgets;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Services.Budgets;

public class BudgetManagementServiceTests : DatabaseTestBase
{
    private readonly BudgetManagementService _sut = new();

    private async Task<Category> CreateCategoryAsync(string name = "Groceries")
    {
        var category = new Category { Name = name };
        Context.Categories.Add(category);
        await Context.SaveChangesAsync();
        return category;
    }

    [Fact]
    public async Task SetBudgetAsync_WithNoPriorPeriod_CreatesTheFirstOne()
    {
        var category = await CreateCategoryAsync();

        await _sut.SetBudgetAsync(Context, category.Id, 450m, Frequency.Weekly, new DateOnly(2026, 1, 1));

        var period = await Context.BudgetPeriods.SingleAsync(p => p.CategoryId == category.Id);
        Assert.Equal(450m, period.Amount);
        Assert.Equal(Frequency.Weekly, period.Frequency);
        Assert.Equal(new DateOnly(2026, 1, 1), period.EffectiveFrom);
        Assert.Null(period.EffectiveThrough);
    }

    [Fact]
    public async Task SetBudgetAsync_WithAnExistingCurrentPeriod_ClosesItOutAndOpensANewOne()
    {
        var category = await CreateCategoryAsync();
        await _sut.SetBudgetAsync(Context, category.Id, 450m, Frequency.Weekly, new DateOnly(2026, 1, 1));

        await _sut.SetBudgetAsync(Context, category.Id, 500m, Frequency.Weekly, new DateOnly(2026, 7, 15));

        var periods = await Context.BudgetPeriods.Where(p => p.CategoryId == category.Id).OrderBy(p => p.EffectiveFrom).ToListAsync();
        Assert.Equal(2, periods.Count);

        var old = periods[0];
        Assert.Equal(450m, old.Amount);
        Assert.Equal(new DateOnly(2026, 1, 1), old.EffectiveFrom);
        Assert.Equal(new DateOnly(2026, 7, 14), old.EffectiveThrough); // day before the new period starts - no gap, no overlap

        var current = periods[1];
        Assert.Equal(500m, current.Amount);
        Assert.Equal(new DateOnly(2026, 7, 15), current.EffectiveFrom);
        Assert.Null(current.EffectiveThrough);
    }

    [Fact]
    public async Task SetBudgetAsync_CanChangeFrequencyNotJustAmount()
    {
        var category = await CreateCategoryAsync();
        await _sut.SetBudgetAsync(Context, category.Id, 1100m, Frequency.Monthly, new DateOnly(2026, 1, 1));

        await _sut.SetBudgetAsync(Context, category.Id, 253.85m, Frequency.Weekly, new DateOnly(2026, 7, 15));

        var current = await Context.BudgetPeriods.SingleAsync(p => p.CategoryId == category.Id && p.EffectiveThrough == null);
        Assert.Equal(Frequency.Weekly, current.Frequency);
    }

    [Fact]
    public async Task GetCurrentBudgetsAsync_ReturnsOnlyTheActiveCurrentPeriodPerCategory()
    {
        var category = await CreateCategoryAsync();
        await _sut.SetBudgetAsync(Context, category.Id, 450m, Frequency.Weekly, new DateOnly(2026, 1, 1));
        await _sut.SetBudgetAsync(Context, category.Id, 500m, Frequency.Weekly, new DateOnly(2026, 7, 15));

        var current = await _sut.GetCurrentBudgetsAsync(Context);

        var entry = Assert.Single(current, b => b.CategoryId == category.Id);
        Assert.Equal(500m, entry.Amount);
        Assert.Null(entry.EffectiveThrough);
    }

    [Fact]
    public async Task GetCurrentBudgetsAsync_CategoryWithNoBudgetYet_IsExcluded()
    {
        await CreateCategoryAsync("Never Budgeted");

        var current = await _sut.GetCurrentBudgetsAsync(Context);

        Assert.Empty(current);
    }

    [Fact]
    public async Task SetBudgetAsync_ForADirectCategory_CanSpecifyDirectionAnchorAndAccount()
    {
        var account = new Account { Name = "Wells Fargo Checking", Type = AccountType.Checking };
        Context.Accounts.Add(account);
        await Context.SaveChangesAsync();
        var category = await CreateCategoryAsync("Truist Mortgage");

        await _sut.SetBudgetAsync(Context, category.Id, 2681.22m, Frequency.Monthly, new DateOnly(2026, 1, 1),
            direction: Direction.Expense, anchor: new DateOnly(2026, 1, 4), accountId: account.Id);

        var period = await Context.BudgetPeriods.SingleAsync(p => p.CategoryId == category.Id);
        Assert.Equal(Direction.Expense, period.Direction);
        Assert.Equal(new DateOnly(2026, 1, 4), period.Anchor);
        Assert.Equal(account.Id, period.AccountId);
    }

    [Fact]
    public async Task SetBudgetAsync_WithNoDirectionSpecified_DefaultsToExpense()
    {
        var category = await CreateCategoryAsync();

        await _sut.SetBudgetAsync(Context, category.Id, 450m, Frequency.Weekly, new DateOnly(2026, 1, 1));

        var period = await Context.BudgetPeriods.SingleAsync(p => p.CategoryId == category.Id);
        Assert.Equal(Direction.Expense, period.Direction);
        Assert.Null(period.Anchor);
        Assert.Null(period.AccountId);
    }
}
