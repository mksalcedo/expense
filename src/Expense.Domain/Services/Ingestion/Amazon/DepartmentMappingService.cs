using Expense.Domain.Data;
using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Ingestion.Amazon;

/// <summary>
/// CRUD for AmazonDepartmentMapping, plus the "a mapping just changed - re-resolve the
/// placeholder orders already sitting in the Review Queue" sweep. Adding
/// "Kitchen -&gt; Off-Budget/Misc" should immediately clear the Kitchen orders you've been
/// looking at, not just help future imports.
/// </summary>
public class DepartmentMappingService
{
    public async Task<List<AmazonDepartmentMapping>> GetAllAsync(ExpenseDbContext context, CancellationToken cancellationToken = default) =>
        await context.AmazonDepartmentMappings
            .Include(m => m.Category)
            .OrderBy(m => m.DepartmentName)
            .ToListAsync(cancellationToken);

    /// <summary>Creates (or points an existing one at a new category) a department mapping, then re-resolves matching pending placeholders. Returns how many were auto-categorized as a result.</summary>
    public async Task<int> UpsertAndReapplyAsync(
        ExpenseDbContext context, string departmentName, int categoryId, CancellationToken cancellationToken = default)
    {
        departmentName = departmentName.Trim();
        var existing = await context.AmazonDepartmentMappings
            .FirstOrDefaultAsync(m => m.DepartmentName.ToLower() == departmentName.ToLower(), cancellationToken);

        if (existing is null)
        {
            context.AmazonDepartmentMappings.Add(new AmazonDepartmentMapping { DepartmentName = departmentName, CategoryId = categoryId });
        }
        else
        {
            existing.CategoryId = categoryId;
        }
        await context.SaveChangesAsync(cancellationToken);

        return await ReapplyToPendingPlaceholdersAsync(context, cancellationToken);
    }

    public async Task UpdateAsync(ExpenseDbContext context, int id, string departmentName, int categoryId, CancellationToken cancellationToken = default)
    {
        var mapping = await context.AmazonDepartmentMappings.SingleAsync(m => m.Id == id, cancellationToken);
        mapping.DepartmentName = departmentName.Trim();
        mapping.CategoryId = categoryId;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(ExpenseDbContext context, int id, CancellationToken cancellationToken = default)
    {
        var mapping = await context.AmazonDepartmentMappings.SingleAsync(m => m.Id == id, cancellationToken);
        context.AmazonDepartmentMappings.Remove(mapping);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Sweep of still-pending placeholder rows (CategoryId null, NeedsReview, with a stored
    /// DepartmentHint): if the hint now resolves to a single category, auto-categorize it -
    /// same effect as if the mapping had existed when the order first imported. Also run by
    /// the Amazon sync so a mapping added between syncs takes effect without a manual step.
    /// </summary>
    public async Task<int> ReapplyToPendingPlaceholdersAsync(ExpenseDbContext context, CancellationToken cancellationToken = default)
    {
        var candidates = await context.AmazonOrderItems
            .Where(i => i.CategoryId == null && i.NeedsReview && i.DepartmentHint != null)
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0) return 0;

        var mappings = await context.AmazonDepartmentMappings.Include(m => m.Category).ToListAsync(cancellationToken);

        var reapplied = 0;
        var changed = false;
        foreach (var item in candidates)
        {
            // Bring a pre-feature placeholder's title up to the "Amazon order — {department}"
            // form (guarded against a title the user has since typed in themselves).
            if (AmazonDepartmentResolver.IsSystemPlaceholderTitle(item.ItemTitle)
                && item.ItemTitle != AmazonDepartmentResolver.PlaceholderTitle(item.DepartmentHint))
            {
                item.ItemTitle = AmazonDepartmentResolver.PlaceholderTitle(item.DepartmentHint);
                changed = true;
            }

            var names = AmazonDepartmentResolver.ParseDepartmentNames(item.DepartmentHint);
            var category = AmazonDepartmentResolver.Resolve(names, mappings);
            if (category is not null)
            {
                AmazonDepartmentResolver.ApplyResolvedCategory(item, category, item.DepartmentHint!);
                reapplied++;
                changed = true;
            }
        }

        if (changed) await context.SaveChangesAsync(cancellationToken);
        return reapplied;
    }
}
