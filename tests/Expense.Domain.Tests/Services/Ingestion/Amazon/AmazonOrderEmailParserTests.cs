using Expense.Domain.Services.Ingestion.Amazon;

namespace Expense.Domain.Tests.Services.Ingestion.Amazon;

public class AmazonOrderEmailParserTests
{
    private readonly AmazonOrderEmailParser _sut = new();

    // Real auto-confirm@amazon.com body, single item (pulled directly from the user's Gmail 2026-07-14)
    private const string SingleItemEmail = """
        Your Orders

        https://www.amazon.com/gp/css/order-history?ref_=d_yo_default

            Thanks for your order!
        Ordered

        Shipped

        Out for delivery

        Delivered

        Arriving overnight 7 AM – 11 AM

        Mark - NORCROSS, GA

        Order #
        113-5254486-7378657

        View or edit order
        https://www.amazon.com/your-orders/order-details?orderID=113-5254486-7378657&ref_=p_btn_fed_veo

        * Qunol Ultra CoQ10 100mg, 3x Better Absorption, Patented Water and Fat Soluble Natural Supplement Form of Coenzyme Q10, Antioxidant for Heart Health, 120 Count Softgels
          Quantity: 1
          29.97 USD

        Grand Total:
        31.77 USD

        Amazon.com
        """;

    // Real auto-confirm@amazon.com body, two items (pulled directly from the user's Gmail 2026-07-12)
    private const string MultiItemEmail = """
        Your Orders

        https://www.amazon.com/gp/css/order-history?ref_=d_yo_default

            Thanks for your order!
        Ordered

        Shipped

        Out for delivery

        Delivered

        Arriving tomorrow 10 AM – 3 PM

        Mark - NORCROSS, GA

        Order #
        113-4492181-5586630

        View or edit order
        https://www.amazon.com/your-orders/order-details?orderID=113-4492181-5586630&ref_=p_btn_fed_veo

        * Standard Process Cardio-Plus - Antioxidant Support - Heart Health, Circulation & Blood Flow Supplement with Vitamin B6, Niacin & Riboflavin - Energy Metabolism Supplement - 90 Tablets
          Quantity: 1
          22.8 USD

        * Pure Encapsulations Vitamin D3 125 mcg (5,000 IU) - Supplement to Support Bone, Joint, Breast, Heart, Colon, and Immune Health* - with Vitamin D - 60 Capsules
          Quantity: 1
          21 USD

        Grand Total:
        46.43 USD

        Amazon.com
        """;

    [Fact]
    public void Parse_SingleItemOrder_ExtractsOrderIdAndItemAndProratesTax()
    {
        var items = _sut.Parse(SingleItemEmail, new DateOnly(2026, 7, 14));

        var item = Assert.Single(items);
        Assert.Equal("113-5254486-7378657", item.OrderId);
        Assert.Equal(new DateOnly(2026, 7, 14), item.OrderDate);
        Assert.Contains("Qunol Ultra CoQ10", item.ItemTitle);
        Assert.Equal(1, item.Quantity);
        Assert.Equal(29.97m, item.Price);
        Assert.Equal(1.80m, item.TaxAllocated); // 31.77 - 29.97
    }

    [Fact]
    public void Parse_MultiItemOrder_ExtractsBothItemsAndProratesTaxProportionally()
    {
        var items = _sut.Parse(MultiItemEmail, new DateOnly(2026, 7, 12));

        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Equal("113-4492181-5586630", i.OrderId));

        var cardio = items.Single(i => i.ItemTitle.Contains("Cardio-Plus"));
        Assert.Equal(22.8m, cardio.Price);
        Assert.Equal(1.37m, cardio.TaxAllocated); // 2.63 leftover * (22.8/43.8), rounded

        var vitaminD = items.Single(i => i.ItemTitle.Contains("Vitamin D3"));
        Assert.Equal(21m, vitaminD.Price);
        Assert.Equal(1.26m, vitaminD.TaxAllocated); // 2.63 leftover * (21/43.8), rounded
    }

    // Real auto-confirm@amazon.com body, older template using "Total" (no colon)
    // instead of "Grand Total:" - pulled directly from the user's Gmail (2025 order)
    private const string OlderTemplateEmail = """
        Thanks for your order, Mark!
        Ordered

        Shipped

        Out for delivery

        Delivered

        Arriving Saturday

        Mark - NORCROSS, GA

        Order #
        112-8265526-8324223

        View or edit order
        https://www.amazon.com/gp/css/order-details?orderID=112-8265526-8324223&ref_=p_btn_fed_veo

        * Ancestral Supplements Grass Fed Beef Prostate Supplements for Men with Liver, 3000mg, Prostate Health Support Promotes Men's Health, Non-GMO, 180 Capsules
          Quantity: 1
          52 USD

        Total
        55.12 USD

        Amazon.com
        """;

    [Fact]
    public void Parse_OlderTemplateWithPlainTotalLabel_StillParsesCorrectly()
    {
        var items = _sut.Parse(OlderTemplateEmail, new DateOnly(2025, 6, 1));

        var item = Assert.Single(items);
        Assert.Equal("112-8265526-8324223", item.OrderId);
        Assert.Equal(52m, item.Price);
        Assert.Equal(3.12m, item.TaxAllocated); // 55.12 - 52
    }

    [Fact]
    public void Parse_MalformedBody_ThrowsRatherThanImportEmptyOrWrongData()
    {
        const string malformed = "This is not an Amazon order email at all.";

        Assert.Throws<FormatException>(() => _sut.Parse(malformed, new DateOnly(2026, 7, 14)));
    }

    [Fact]
    public void Parse_BodyWithNoItems_Throws()
    {
        const string noItems = """
            Order #
            113-0000000-0000000

            Grand Total:
            10.00 USD
            """;

        Assert.Throws<FormatException>(() => _sut.Parse(noItems, new DateOnly(2026, 7, 14)));
    }

    [Fact]
    public void Parse_BodyWithNoGrandTotal_Throws()
    {
        const string noGrandTotal = """
            Order #
            113-0000000-0000000

            * Some Item
              Quantity: 1
              10.00 USD
            """;

        Assert.Throws<FormatException>(() => _sut.Parse(noGrandTotal, new DateOnly(2026, 7, 14)));
    }
}
