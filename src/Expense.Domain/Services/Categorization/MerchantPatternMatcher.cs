using System.Text.RegularExpressions;

namespace Expense.Domain.Services.Categorization;

/// <summary>
/// Shared substring-match logic for merchant_rules (bank transactions) and products
/// (Amazon items). Deliberately a single shared implementation - CategorizationService
/// and AmazonImportService used to each have their own private copy of this, and only
/// one of them got the whitespace-collapsing fix for real bank exports' inconsistent
/// internal padding, so a pattern like "TRUIST MORTG OLB MTGPMT" silently never matched
/// "TRUIST MORTG     OLB MTGPMT". One shared implementation means a fix here can't
/// silently miss a sibling copy again.
/// </summary>
public static class MerchantPatternMatcher
{
    public static bool Matches(string text, string pattern) =>
        Regex.Replace(text, @"\s+", " ").Contains(pattern.Trim('%'), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True if a new pattern is a single word, e.g. "PAYMENT" or "FEE" - a real risk (it'll
    /// match that word appearing inside any unrelated transaction's description), unlike a
    /// single-word but genuinely distinctive brand name (e.g. "CHIPOTLE"). Doesn't try to
    /// tell those apart automatically - just flags every single-word pattern for a human to
    /// confirm is actually specific, rather than silently accepting a dangerously generic one.
    /// </summary>
    public static bool IsSingleWord(string pattern) => !pattern.Trim('%').Trim().Contains(' ');
}
