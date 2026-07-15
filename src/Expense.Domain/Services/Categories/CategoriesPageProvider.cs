using Expense.Domain.Data;
using Expense.Domain.Entities;
using Expense.Domain.Services.Budgets;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Categories;

/// <summary>Thin DI-composition wiring (like ForecastResultProvider) - all real logic lives in CategoryManagementService/BudgetManagementService.</summary>
public class CategoriesPageProvider(
    IDbContextFactory<ExpenseDbContext> contextFactory, CategoryManagementService categories, BudgetManagementService budgets) : ICategoriesPageProvider
{
    public async Task<CategoriesPageData> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var currentPeriods = (await budgets.GetCurrentBudgetsAsync(context)).ToDictionary(p => p.CategoryId);

        var categoryData = await context.Categories
            .OrderBy(c => c.Name)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.IsActive,
                FundingStrategy = context.FundingRules.Where(r => r.CategoryId == c.Id).Select(r => r.Strategy).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var rows = categoryData.Select(c =>
        {
            currentPeriods.TryGetValue(c.Id, out var period);
            return new CategoryRow
            {
                Id = c.Id,
                Name = c.Name,
                IsActive = c.IsActive,
                FundingStrategy = c.FundingStrategy ?? FundingStrategies.None,
                BudgetAmount = period?.Amount,
                BudgetFrequency = period?.Frequency,
                BudgetDirection = period?.Direction,
                BudgetAnchor = period?.Anchor,
                BudgetAccountId = period?.AccountId
            };
        }).ToList();

        var accounts = await context.Accounts
            .Where(a => a.IsActive)
            .OrderBy(a => a.Name)
            .Select(a => new AccountOption { Id = a.Id, Name = a.Name })
            .ToListAsync(cancellationToken);

        return new CategoriesPageData { Categories = rows, Accounts = accounts };
    }

    public async Task CreateCategoryAsync(string name, string fundingStrategy, DirectBudgetInput? directBudget = null, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var category = await categories.CreateCategoryAsync(context, name, fundingStrategy);
        await ApplyDirectBudgetAsync(context, category.Id, directBudget);
    }

    public async Task UpdateCategoryAsync(int categoryId, string name, string fundingStrategy, DirectBudgetInput? directBudget = null, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await categories.UpdateCategoryAsync(context, categoryId, name, fundingStrategy);
        await ApplyDirectBudgetAsync(context, categoryId, directBudget);
    }

    private async Task ApplyDirectBudgetAsync(ExpenseDbContext context, int categoryId, DirectBudgetInput? directBudget)
    {
        if (directBudget is null) return;

        await budgets.SetBudgetAsync(context, categoryId, directBudget.Amount, directBudget.Frequency,
            DateOnly.FromDateTime(DateTime.Today), directBudget.Direction, directBudget.Anchor, directBudget.AccountId);
    }

    public async Task DeactivateCategoryAsync(int categoryId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await categories.DeactivateCategoryAsync(context, categoryId);
    }

    public async Task ReactivateCategoryAsync(int categoryId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await categories.ReactivateCategoryAsync(context, categoryId);
    }
}
