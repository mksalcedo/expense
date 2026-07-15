using Expense.Domain.Data;
using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Categories;

/// <summary>
/// Adding a category also creates its funding_rules row explicitly (unlike some existing
/// seeded rows like Off-Budget/Misc, which have none - absence and an explicit 'none' row
/// are functionally equivalent everywhere the Amex forecast queries this table, but new
/// categories created through the UI always get an explicit row so every category's
/// strategy is visible and editable in one place). Removal deactivates, not hard-deletes,
/// to preserve historical transactions/reports.
/// </summary>
public class CategoryManagementService
{
    public async Task<Category> CreateCategoryAsync(ExpenseDbContext context, string name, bool isBudgeted, string fundingStrategy)
    {
        var category = new Category { Name = name, IsBudgeted = isBudgeted };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        context.FundingRules.Add(new FundingRule { CategoryId = category.Id, Strategy = fundingStrategy });
        await context.SaveChangesAsync();

        return category;
    }

    public async Task RenameCategoryAsync(ExpenseDbContext context, int categoryId, string newName)
    {
        var category = await context.Categories.SingleAsync(c => c.Id == categoryId);
        category.Name = newName;
        await context.SaveChangesAsync();
    }

    public async Task SetFundingStrategyAsync(ExpenseDbContext context, int categoryId, string strategy)
    {
        var rule = await context.FundingRules.SingleOrDefaultAsync(r => r.CategoryId == categoryId);
        if (rule is null)
        {
            context.FundingRules.Add(new FundingRule { CategoryId = categoryId, Strategy = strategy });
        }
        else
        {
            rule.Strategy = strategy;
        }
        await context.SaveChangesAsync();
    }

    public async Task DeactivateCategoryAsync(ExpenseDbContext context, int categoryId)
    {
        var category = await context.Categories.SingleAsync(c => c.Id == categoryId);
        category.IsActive = false;
        await context.SaveChangesAsync();
    }
}
