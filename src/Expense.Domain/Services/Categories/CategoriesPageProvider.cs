using Expense.Domain.Data;
using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Categories;

/// <summary>Thin DI-composition wiring (like ForecastResultProvider) - all real logic lives in CategoryManagementService.</summary>
public class CategoriesPageProvider(IDbContextFactory<ExpenseDbContext> contextFactory, CategoryManagementService categories) : ICategoriesPageProvider
{
    public async Task<CategoriesPageData> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var rows = await context.Categories
            .OrderBy(c => c.Name)
            .Select(c => new CategoryRow
            {
                Id = c.Id,
                Name = c.Name,
                IsBudgeted = c.IsBudgeted,
                IsActive = c.IsActive,
                FundingStrategy = context.FundingRules
                    .Where(r => r.CategoryId == c.Id)
                    .Select(r => r.Strategy)
                    .FirstOrDefault() ?? FundingStrategies.None
            })
            .ToListAsync(cancellationToken);

        return new CategoriesPageData { Categories = rows };
    }

    public async Task CreateCategoryAsync(string name, bool isBudgeted, string fundingStrategy, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await categories.CreateCategoryAsync(context, name, isBudgeted, fundingStrategy);
    }

    public async Task UpdateCategoryAsync(int categoryId, string name, bool isBudgeted, string fundingStrategy, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await categories.UpdateCategoryAsync(context, categoryId, name, isBudgeted, fundingStrategy);
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
