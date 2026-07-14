using Expense.Domain.Services.Ingestion.Amazon;

namespace Expense.Domain.Tests.Services.Ingestion.Amazon;

public class AmazonRefundEmailParserTests
{
    private readonly AmazonRefundEmailParser _sut = new();

    // Real payments-messages@amazon.com refund body, pulled directly from the user's
    // Gmail 2025-07-11 (this parser is scoped to this one clean template - other real
    // refund-email variants like "advance refund issued" or "dropoff confirmed" use
    // different wording and are out of scope for now).
    private const string RefundEmail = """
        Hello, We're writing to let you know we processed your refund of $23.31 for your Order 112-1510135-3538618 from JFP Western Inc..

        This refund is for the following item(s):     Item: MOS Cardstock Paper - 11" x 14", 250 GSM, 110 Sheets - Heavyweight Off White Poster Board Paper for Menus, Certificates & Crafts - Smooth Finish, Prin     Quantity: 1     ASIN: B0DKB7SPSR     Reason for refund: Account adjustment     Here's the breakdown of your refund for this item:         Item Refund: $21.99         Item Tax Refund: $1.32

        We'll apply your refund to the following payment method(s): AmericanExpress Credit Card [expiring on 7/2028]: $23.31 We've processed a refund for the above order in the amount of $23.31. The refund should appear on your account in 2-3 days if issued to a credit card.
        """;

    [Fact]
    public void Parse_RealRefundEmail_ExtractsOrderIdItemAndCombinedRefundAmount()
    {
        var refunds = _sut.Parse(RefundEmail);

        var refund = Assert.Single(refunds);
        Assert.Equal("112-1510135-3538618", refund.OrderId);
        Assert.Contains("MOS Cardstock Paper", refund.ItemTitle);
        Assert.Equal(1, refund.Quantity);
        Assert.Equal(23.31m, refund.RefundAmount); // Item Refund $21.99 + Item Tax Refund $1.32
    }

    // Real payments-messages@amazon.com body - a "goodwill" refund uses a single combined
    // line instead of separate Item Refund/Item Tax Refund lines (pulled from the user's
    // Gmail; this was a near-duplicate resend of the same order as RefundEmail above,
    // just with a different refund-breakdown wording).
    private const string GoodwillRefundEmail = """
        Hello, We're writing to let you know we processed your refund of $23.31 for your Order 112-1510135-3538618 from JFP Western Inc..

        This refund is for the following item(s):     Item: MOS Cardstock Paper - 11" x 14"     Quantity: 1     ASIN: B0DKB7SPSR     Reason for refund: Account adjustment     Here's the breakdown of your refund for this item:         Goodwill Refund: $23.31
        """;

    [Fact]
    public void Parse_GoodwillRefund_UsesTheSingleCombinedAmount()
    {
        var refunds = _sut.Parse(GoodwillRefundEmail);

        var refund = Assert.Single(refunds);
        Assert.Equal("112-1510135-3538618", refund.OrderId);
        Assert.Equal(23.31m, refund.RefundAmount);
    }

    [Fact]
    public void Parse_MalformedBody_ThrowsRatherThanImportEmptyOrWrongData()
    {
        const string malformed = "This is not an Amazon refund email at all.";

        Assert.Throws<FormatException>(() => _sut.Parse(malformed));
    }

    [Fact]
    public void Parse_BodyWithOrderButNoItemRefundDetail_Throws()
    {
        const string noItemDetail = "We processed your refund of $10.00 for your Order 112-0000000-0000000 from Some Seller.";

        Assert.Throws<FormatException>(() => _sut.Parse(noItemDetail));
    }
}
