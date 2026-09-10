using Expense.Domain.Data;
using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Categorization;

/// <summary>
/// CRUD for merchant_rules behind the /merchant-rules management page, plus the "a rule just
/// changed - re-check the transactions already sitting in the Review Queue" sweep. Adding a
/// "Venmo, money in -&gt; Piano" rule should immediately clear the mis-filed Venmo cashouts
/// you're looking at, not just help future imports.
///
/// CRUD is by id, not upsert-by-pattern (unlike <c>DepartmentMappingService</c>): merchant
/// rules legitimately repeat a pattern with different directions, so two "VENMO" rows is a
/// supported state, not a conflict to collapse.
/// </summary>
public class MerchantRuleService
{
    public async Task<List<MerchantRule>> GetAllAsync(ExpenseDbContext context, CancellationToken cancellationToken = default) =>
        await context.MerchantRules
            .Include(r => r.Category)
            .OrderBy(r => r.MerchantPattern)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);

    /// <summary>Creates a rule, then re-resolves matching pending transactions. Returns how many were auto-categorized as a result.</summary>
    public async Task<int> CreateAsync(
        ExpenseDbContext context, string merchantPattern, Direction? direction, int categoryId, CancellationToken cancellationToken = default)
    {
        context.MerchantRules.Add(new MerchantRule
        {
            MerchantPattern = merchantPattern.Trim(),
            Direction = direction,
            CategoryId = categoryId
        });
        await context.SaveChangesAsync(cancellationToken);
        return await ReapplyToPendingTransactionsAsync(context, cancellationToken);
    }

    /// <summary>Repoints an existing rule's pattern/direction/category, then re-resolves matching pending transactions. Returns how many were auto-categorized.</summary>
    public async Task<int> UpdateAsync(
        ExpenseDbContext context, int id, string merchantPattern, Direction? direction, int categoryId, CancellationToken cancellationToken = default)
    {
        var rule = await context.MerchantRules.SingleAsync(r => r.Id == id, cancellationToken);
        rule.MerchantPattern = merchantPattern.Trim();
        rule.Direction = direction;
        rule.CategoryId = categoryId;
        await context.SaveChangesAsync(cancellationToken);
        return await ReapplyToPendingTransactionsAsync(context, cancellationToken);
    }

    public async Task DeleteAsync(ExpenseDbContext context, int id, CancellationToken cancellationToken = default)
    {
        var rule = await context.MerchantRules.SingleAsync(r => r.Id == id, cancellationToken);
        context.MerchantRules.Remove(rule);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Re-checks every still-pending bank transaction against all current merchant_rules
    /// (the merchant-rule half of CategorizationService's "Re-apply Rules Now" sweep - no
    /// history fallback, no Amazon items, so the count reported back to the page is purely
    /// "this rule change caught these").
    /// </summary>
    public async Task<int> ReapplyToPendingTransactionsAsync(ExpenseDbContext context, CancellationToken cancellationToken = default)
    {
        var pending = await context.BankTransactions
            .Where(t => t.CategoryId == null && !t.IsAmazonMerchant)
            .ToListAsync(cancellationToken);
        if (pending.Count == 0) return 0;

        var rules = await context.MerchantRules.ToListAsync(cancellationToken);
        var count = 0;
        foreach (var transaction in pending)
        {
            var searchText = (transaction.Merchant ?? transaction.Description).ToUpperInvariant();
            var match = MerchantRuleMatcher.FirstMatch(rules, searchText, transaction.Amount);
            if (match is not null)
            {
                transaction.CategoryId = match.CategoryId;
                count++;
            }
        }
        if (count > 0) await context.SaveChangesAsync(cancellationToken);
        return count;
    }
}
