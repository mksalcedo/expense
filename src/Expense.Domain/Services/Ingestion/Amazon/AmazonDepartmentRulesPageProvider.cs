using Expense.Domain.Data;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Ingestion.Amazon;

/// <summary>Thin DI-composition wiring (like ConfirmedPaymentsPageProvider) - all real logic lives in DepartmentMappingService.</summary>
public class AmazonDepartmentRulesPageProvider(
    IDbContextFactory<ExpenseDbContext> contextFactory, DepartmentMappingService mappings) : IAmazonDepartmentRulesPageProvider
{
    public async Task<AmazonDepartmentRulesPageData> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return new AmazonDepartmentRulesPageData
        {
            Mappings = await mappings.GetAllAsync(context, cancellationToken),
            Categories = await context.Categories.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync(cancellationToken)
        };
    }

    public async Task<int> UpsertAsync(string departmentName, int categoryId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await mappings.UpsertAndReapplyAsync(context, departmentName, categoryId, cancellationToken);
    }

    public async Task DeleteAsync(int mappingId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await mappings.DeleteAsync(context, mappingId, cancellationToken);
    }
}
