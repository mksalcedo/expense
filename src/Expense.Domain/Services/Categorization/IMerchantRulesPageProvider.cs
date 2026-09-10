using Expense.Domain.Entities;

namespace Expense.Domain.Services.Categorization;

public class MerchantRulesPageData
{
    public required List<MerchantRule> Rules { get; init; }
    public required List<Category> Categories { get; init; }

    /// <summary>Rule id -&gt; how many historical transactions its pattern+direction matches. See MerchantRuleService.GetMatchCountsAsync.</summary>
    public Dictionary<int, int> MatchCounts { get; init; } = new();
}

/// <summary>Thin abstraction over MerchantRuleService so the /merchant-rules page can be tested against a fake.</summary>
public interface IMerchantRulesPageProvider
{
    Task<MerchantRulesPageData> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds a rule, then re-resolves matching pending transactions. Returns how many were auto-categorized as a result.</summary>
    Task<int> CreateAsync(string merchantPattern, Direction? direction, int categoryId, CancellationToken cancellationToken = default);

    /// <summary>Repoints an existing rule, then re-resolves matching pending transactions. Returns how many were auto-categorized.</summary>
    Task<int> UpdateAsync(int id, string merchantPattern, Direction? direction, int categoryId, CancellationToken cancellationToken = default);

    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
