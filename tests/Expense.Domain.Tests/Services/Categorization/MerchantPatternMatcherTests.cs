using Expense.Domain.Services.Categorization;

namespace Expense.Domain.Tests.Services.Categorization;

public class MerchantPatternMatcherTests
{
    [Fact]
    public void Matches_IsCaseInsensitive()
    {
        Assert.True(MerchantPatternMatcher.Matches("CHIPOTLE 1652 NORCROSS GA", "%chipotle%"));
    }

    [Fact]
    public void Matches_TrimsPercentWildcardsFromThePattern()
    {
        Assert.True(MerchantPatternMatcher.Matches("INGLES #123 NORCROSS GA", "%INGLES%"));
    }

    [Fact]
    public void Matches_CollapsesRunsOfWhitespaceInTheRawTextBeforeComparing()
    {
        // Real bank exports pad inconsistently between statements - a pattern derived from
        // collapsed text must still match raw text with different amounts of padding.
        var raw = "TRUIST MORTG     OLB MTGPMT 260604 3001469588      MARK SALCEDO";

        Assert.True(MerchantPatternMatcher.Matches(raw, "TRUIST MORTG OLB MTGPMT"));
    }

    [Fact]
    public void Matches_WhenPatternIsNotASubstring_ReturnsFalse()
    {
        Assert.False(MerchantPatternMatcher.Matches("SHELL GAS STATION", "%PUBLIX%"));
    }

    [Fact]
    public void IsSingleWord_TrueForAPlainSingleWord()
    {
        Assert.True(MerchantPatternMatcher.IsSingleWord("CHIPOTLE"));
    }

    [Fact]
    public void IsSingleWord_TrueForAWildcardedSingleWord()
    {
        Assert.True(MerchantPatternMatcher.IsSingleWord("%SOFI%"));
    }

    [Fact]
    public void IsSingleWord_FalseWhenTheresMoreThanOneWord()
    {
        Assert.False(MerchantPatternMatcher.IsSingleWord("PUBLIX NORCROSS GA"));
    }
}
