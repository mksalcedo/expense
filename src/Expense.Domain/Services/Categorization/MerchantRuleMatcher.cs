using Expense.Domain.Entities;

namespace Expense.Domain.Services.Categorization;

/// <summary>
/// Picks the first merchant_rule that applies to a transaction - shared by import-time
/// categorization (<see cref="CategorizationService.ApplyMerchantRuleAsync"/>) and the
/// "Re-apply Rules Now" sweep, so the direction check can't drift between the two.
///
/// A rule with a non-null <see cref="MerchantRule.Direction"/> only applies to transactions
/// of that sign; a null Direction (every rule created before that column existed) applies to
/// either. A $0 transaction counts as income.
/// </summary>
public static class MerchantRuleMatcher
{
    public static MerchantRule? FirstMatch(IEnumerable<MerchantRule> rules, string searchText, decimal amount) =>
        rules.FirstOrDefault(r => IsMatch(r, searchText, amount));

    public static bool IsMatch(MerchantRule rule, string searchText, decimal amount)
    {
        var direction = amount >= 0 ? Direction.Income : Direction.Expense;
        return (rule.Direction is null || rule.Direction == direction)
            && MerchantPatternMatcher.Matches(searchText, rule.MerchantPattern);
    }
}
