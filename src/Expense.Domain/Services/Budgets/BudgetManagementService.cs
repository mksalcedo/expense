using Expense.Domain.Data;
using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Budgets;

/// <summary>
/// Budgets are dated-versioned targets, not values edited in place: changing an amount
/// or frequency closes out the current period (effective_through = the day before the
/// new one starts, so periods are contiguous with no gap or overlap) and opens a new
/// current one, preserving history for Historical Analysis's budget-vs-actual-at-the-time
/// comparisons.
/// </summary>
public class BudgetManagementService
{
    public async Task SetBudgetAsync(ExpenseDbContext context, int categoryId, decimal amount, Frequency frequency, DateOnly effectiveFrom,
        Direction direction = Direction.Expense, DateOnly? anchor = null, int? accountId = null)
    {
        var currentPeriod = await context.BudgetPeriods
            .SingleOrDefaultAsync(p => p.CategoryId == categoryId && p.EffectiveThrough == null);

        if (currentPeriod is not null)
        {
            if (currentPeriod.EffectiveFrom >= effectiveFrom)
            {
                // The current period never had a single real day in effect before being
                // superseded (e.g. trying a few amounts in one sitting, all "starting
                // today") - closing it out would set its EffectiveThrough before its own
                // EffectiveFrom, an impossible range that's really just a discarded draft,
                // not real budget history. Replace it in place instead of leaving that
                // permanently-orphaned row behind.
                context.BudgetPeriods.Remove(currentPeriod);
            }
            else
            {
                currentPeriod.EffectiveThrough = effectiveFrom.AddDays(-1);
            }
        }

        context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = categoryId,
            Amount = amount,
            Frequency = frequency,
            Direction = direction,
            Anchor = anchor,
            AccountId = accountId,
            EffectiveFrom = effectiveFrom,
            EffectiveThrough = null
        });

        await context.SaveChangesAsync();
    }

    public async Task<List<BudgetPeriod>> GetCurrentBudgetsAsync(ExpenseDbContext context) =>
        await context.BudgetPeriods.Where(p => p.EffectiveThrough == null).ToListAsync();
}
