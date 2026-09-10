using Expense.Domain.Data;
using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Categorization;

/// <summary>Thin DI-composition wiring (like AmazonDepartmentRulesPageProvider) - all real logic lives in MerchantRuleService.</summary>
public class MerchantRulesPageProvider(
    IDbContextFactory<ExpenseDbContext> contextFactory, MerchantRuleService rules) : IMerchantRulesPageProvider
{
    public async Task<MerchantRulesPageData> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return new MerchantRulesPageData
        {
            Rules = await rules.GetAllAsync(context, cancellationToken),
            Categories = await context.Categories.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync(cancellationToken)
        };
    }

    public async Task<int> CreateAsync(string merchantPattern, Direction? direction, int categoryId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await rules.CreateAsync(context, merchantPattern, direction, categoryId, cancellationToken);
    }

    public async Task<int> UpdateAsync(int id, string merchantPattern, Direction? direction, int categoryId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await rules.UpdateAsync(context, id, merchantPattern, direction, categoryId, cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await rules.DeleteAsync(context, id, cancellationToken);
    }
}
