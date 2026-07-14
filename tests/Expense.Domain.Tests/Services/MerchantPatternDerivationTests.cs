using Expense.Domain.Services.Categorization;

namespace Expense.Domain.Tests.Services;

public class MerchantPatternDerivationTests
{
    [Fact]
    public void DeriveMerchantPattern_PlainDescription_TakesLeadingNonDigitWords()
    {
        Assert.Equal("TRADER JOE S", CategorizationService.DeriveMerchantPattern("TRADER JOE S #123 NORCROSS GA"));
    }

    [Fact]
    public void DeriveMerchantPattern_StopsAtFourWords_EvenWithNoDigits()
    {
        Assert.Equal("MANHATTAN NYDELI & PEACHTREE", CategorizationService.DeriveMerchantPattern("MANHATTAN NYDELI &        PEACHTREE COR GA"));
    }

    // Real Wells Fargo descriptions: "PURCHASE ... AUTHORIZED ON MM/DD MERCHANT ..." -
    // the boilerplate prefix must be skipped so different real merchants don't get
    // merged into one useless "PURCHASE AUTHORIZED ON" group.
    [Theory]
    [InlineData("PURCHASE                                AUTHORIZED ON   07/09 THE HOME DEPOT #0131      DULUTH        GA  P466190855851044   CARD 1798", "THE HOME DEPOT")]
    [InlineData("PURCHASE                                AUTHORIZED ON   07/05 MARLOWS TAVERN M18        PEACHTREE COR GA  S466186611343156   CARD 1798", "MARLOWS TAVERN")]
    [InlineData("PURCHASE                                AUTHORIZED ON   07/04 TEDS MONTANA GRILL        404-2661344   GA  S386185805300673   CARD 1798", "TEDS MONTANA GRILL")]
    [InlineData("PURCHASE                                AUTHORIZED ON   07/04 KOHL'S #0447 3630 PEACHTR SUWANEE       GA  P346185644455282   CARD 1798", "KOHL'S")]
    public void DeriveMerchantPattern_SkipsWellsFargoAuthorizedOnBoilerplate(string realDescription, string expectedPattern)
    {
        Assert.Equal(expectedPattern, CategorizationService.DeriveMerchantPattern(realDescription));
    }

    [Fact]
    public void DeriveMerchantPattern_DifferentRealMerchants_ProduceDifferentPatterns()
    {
        // regression guard for the original bug: these must NOT all collapse to "PURCHASE AUTHORIZED ON"
        var homeDepot = CategorizationService.DeriveMerchantPattern("PURCHASE AUTHORIZED ON 07/09 THE HOME DEPOT #0131 DULUTH GA P466190855851044 CARD 1798");
        var kohls = CategorizationService.DeriveMerchantPattern("PURCHASE AUTHORIZED ON 07/04 KOHL'S #0447 3630 PEACHTR SUWANEE GA P346185644455282 CARD 1798");

        Assert.NotEqual(homeDepot, kohls);
        Assert.DoesNotContain("AUTHORIZED", homeDepot);
        Assert.DoesNotContain("AUTHORIZED", kohls);
    }

    [Fact]
    public void DeriveMerchantPattern_WhitespaceRuns_AreCollapsed()
    {
        Assert.Equal("SAVE AS YOU GO", CategorizationService.DeriveMerchantPattern("SAVE AS YOU GO TRANSFER DEBIT                TO                                XXXXXXXXXXX6626"));
    }
}
