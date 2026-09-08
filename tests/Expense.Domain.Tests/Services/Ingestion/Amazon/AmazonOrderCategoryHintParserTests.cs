using Expense.Domain.Services.Ingestion.Amazon;

namespace Expense.Domain.Tests.Services.Ingestion.Amazon;

public class AmazonOrderCategoryHintParserTests
{
    private readonly AmazonOrderCategoryHintParser _sut = new();

    // Real-shaped fragments of auto-confirm@amazon.com HTML bodies (structure copied verbatim
    // from live 2026-08/09 emails: rio-text spans inside tr/td/div, order id wrapped in a
    // U+202B mark, numbers wrapped in U+2066/U+2069). Trimmed to the order block(s) only.

    // Newest template, single order, bare "{n} {Dept}" form (matches the "2 Supplements" line
    // in the email screenshot the user provided).
    private const string BareSingleDepartmentHtml = """
        <table><tr><td><div><span class="rio-text">Mark, your Supplements are confirmed!</span></div></td></tr>
        <tr><td><div><span class="rio-text">Arriving tomorrow</span></div></td></tr>
        <tr><td><div><span class="rio-text">&#8294;2&#8297; Supplements</span></div></td></tr>
        <tr><td><div><span class="rio-text">Mark - NORCROSS, GA</span></div></td></tr>
        <tr><td><div><span class="rio-text"><span>Order #</span> <span>&#8299;113-7204525-3005821</span></span></div></td></tr>
        <tr><td><div><span class="rio-text">Grand Total:</span></div></td></tr>
        <tr><td><div><span class="rio-text">$81.65</span></div></td></tr></table>
        """;

    // Mid-July..Aug template, single order, "{n} {Dept} item(s)" form.
    private const string ItemFormSingleDepartmentHtml = """
        <table><tr><td><div><span class="rio-text">Thanks for your order!</span></div></td></tr>
        <tr><td><div><span class="rio-text">Arriving Wednesday</span></div></td></tr>
        <tr><td><div><span class="rio-text">Mark - NORCROSS, GA</span></div></td></tr>
        <tr><td><div><span class="rio-text"><span>Order #</span> <span>&#8299;113-9460079-2470629</span></span></div></td></tr>
        <tr><td><div><span class="rio-text">&#8294;1&#8297; Nutrition &amp; Wellness item</span></div></td></tr>
        <tr><td><div><span class="rio-text">Grand Total:</span></div></td></tr>
        <tr><td><div><span class="rio-text">$28.99</span></div></td></tr></table>
        """;

    // Genuinely mixed single order - per-department counts. Real: order 113-8777231-4791428, 2026-08-17.
    private const string MixedSingleOrderHtml = """
        <table><tr><td><div><span class="rio-text">Thanks for your order!</span></div></td></tr>
        <tr><td><div><span class="rio-text">Arriving Wednesday</span></div></td></tr>
        <tr><td><div><span class="rio-text">Mark - NORCROSS, GA</span></div></td></tr>
        <tr><td><div><span class="rio-text"><span>Order #</span> <span>&#8299;113-8777231-4791428</span></span></div></td></tr>
        <tr><td><div><span class="rio-text">&#8294;4&#8297; items: &#8294;3&#8297; Apparel, &#8294;1&#8297; Office</span></div></td></tr>
        <tr><td><div><span class="rio-text">Grand Total:</span></div></td></tr>
        <tr><td><div><span class="rio-text">$84.76</span></div></td></tr></table>
        """;

    // Digest email bundling 3 separate orders, each single-department. Real: 2026-07-21
    // "Ordered: 4 Nutrition & Wellness and Grocery items" - dept line comes AFTER the Order #.
    private const string ThreeOrderDigestHtml = """
        <table><tr><td><div><span class="rio-text">Thanks for your order!</span></div></td></tr>
        <tr><td><div><span class="rio-text">Arriving overnight 7 AM &#8211; 11 AM</span></div></td></tr>
        <tr><td><div><span class="rio-text">Mark - NORCROSS, GA</span></div></td></tr>
        <tr><td><div><span class="rio-text"><span>Order #</span> <span>&#8299;113-6641743-8180261</span></span></div></td></tr>
        <tr><td><div><span class="rio-text">&#8294;1&#8297; Nutrition &amp; Wellness item</span></div></td></tr>
        <tr><td><div><span class="rio-text">Grand Total:</span></div></td></tr>
        <tr><td><div><span class="rio-text">$16.08</span></div></td></tr>
        <tr><td><div><span class="rio-text">Arriving tomorrow 10 AM &#8211; 3 PM</span></div></td></tr>
        <tr><td><div><span class="rio-text">Mark - NORCROSS, GA</span></div></td></tr>
        <tr><td><div><span class="rio-text"><span>Order #</span> <span>&#8299;113-5634569-7569032</span></span></div></td></tr>
        <tr><td><div><span class="rio-text">&#8294;3&#8297; Grocery items</span></div></td></tr>
        <tr><td><div><span class="rio-text">Grand Total:</span></div></td></tr>
        <tr><td><div><span class="rio-text">$0.00</span></div></td></tr>
        <tr><td><div><span class="rio-text">Arriving Thursday</span></div></td></tr>
        <tr><td><div><span class="rio-text">Mark - NORCROSS, GA</span></div></td></tr>
        <tr><td><div><span class="rio-text"><span>Order #</span> <span>&#8299;113-6275981-3164251</span></span></div></td></tr>
        <tr><td><div><span class="rio-text">&#8294;1&#8297; Nutrition &amp; Wellness item</span></div></td></tr>
        <tr><td><div><span class="rio-text">Grand Total:</span></div></td></tr>
        <tr><td><div><span class="rio-text">$0.00</span></div></td></tr></table>
        """;

    // Digest, dept line comes BEFORE the Order # (real: 2026-09-01 "Ordered 2 items: Supplements, Vitamins").
    private const string TwoOrderDigestDeptBeforeOrderHtml = """
        <table><tr><td><div><span class="rio-text">Mark, your Supplements items and more are confirmed!</span></div></td></tr>
        <tr><td><div><span class="rio-text">Arriving tomorrow 4 AM &#8211; 8 AM</span></div></td></tr>
        <tr><td><div><span class="rio-text">&#8294;1&#8297; Supplements item</span></div></td></tr>
        <tr><td><div><span class="rio-text">Mark - NORCROSS, GA</span></div></td></tr>
        <tr><td><div><span class="rio-text"><span>Order #</span> <span>&#8299;113-3097615-2760211</span></span></div></td></tr>
        <tr><td><div><span class="rio-text">Grand Total:</span></div></td></tr>
        <tr><td><div><span class="rio-text">$40.28</span></div></td></tr>
        <tr><td><div><span class="rio-text">Arriving tomorrow 4 AM &#8211; 8 AM</span></div></td></tr>
        <tr><td><div><span class="rio-text">&#8294;1&#8297; Vitamins item</span></div></td></tr>
        <tr><td><div><span class="rio-text">Mark - NORCROSS, GA</span></div></td></tr>
        <tr><td><div><span class="rio-text"><span>Order #</span> <span>&#8299;113-8477021-1529854</span></span></div></td></tr>
        <tr><td><div><span class="rio-text">Grand Total:</span></div></td></tr>
        <tr><td><div><span class="rio-text">$19.99</span></div></td></tr></table>
        """;

    // Newest form seen live 2026-09-08: singular "N item: {Dept}" (colon, department last).
    private const string SingularItemColonFormHtml = """
        <table><tr><td><div><span class="rio-text">Mark, your Supplements are confirmed!</span></div></td></tr>
        <tr><td><div><span class="rio-text">Arriving tomorrow 4 AM &#8211; 8 AM</span></div></td></tr>
        <tr><td><div><span class="rio-text">&#8294;1&#8297; item: Supplements</span></div></td></tr>
        <tr><td><div><span class="rio-text">Mark - NORCROSS, GA</span></div></td></tr>
        <tr><td><div><span class="rio-text"><span>Order #</span> <span>&#8299;113-6146265-7385012</span></span></div></td></tr>
        <tr><td><div><span class="rio-text">Grand Total:</span></div></td></tr>
        <tr><td><div><span class="rio-text">$45.00</span></div></td></tr></table>
        """;

    // Older template with no department line anywhere - just Order # + Grand Total.
    private const string NoDepartmentLineHtml = """
        <table><tr><td><div><span>Thanks for your order!</span></div></td></tr>
        <tr><td><div><span>Arriving Wednesday</span></div></td></tr>
        <tr><td><div><span>Mark - NORCROSS, GA</span></div></td></tr>
        <tr><td><div><span><span>Order #</span> <span>&#8299;113-1234567-1234567</span></span></div></td></tr>
        <tr><td><div><span>Grand Total:</span></div></td></tr>
        <tr><td><div><span>$12.00</span></div></td></tr></table>
        """;

    [Fact]
    public void Parse_BareForm_ReturnsOneHint_WithDepartmentAndCount()
    {
        var hints = _sut.Parse(BareSingleDepartmentHtml);

        var hint = Assert.Single(hints);
        Assert.Equal("113-7204525-3005821", hint.OrderId);
        var dept = Assert.Single(hint.Departments);
        Assert.Equal("Supplements", dept.Department);
        Assert.Equal(2, dept.Count);
    }

    [Fact]
    public void Parse_ItemForm_HandlesAMultiWordDepartmentName()
    {
        var hints = _sut.Parse(ItemFormSingleDepartmentHtml);

        var hint = Assert.Single(hints);
        Assert.Equal("113-9460079-2470629", hint.OrderId);
        var dept = Assert.Single(hint.Departments);
        Assert.Equal("Nutrition & Wellness", dept.Department);
        Assert.Equal(1, dept.Count);
    }

    [Fact]
    public void Parse_MixedOrder_ReturnsBothDepartmentsWithTheirCounts()
    {
        var hints = _sut.Parse(MixedSingleOrderHtml);

        var hint = Assert.Single(hints);
        Assert.Equal("113-8777231-4791428", hint.OrderId);
        Assert.Equal(2, hint.Departments.Count);
        Assert.Contains(hint.Departments, d => d is { Department: "Apparel", Count: 3 });
        Assert.Contains(hint.Departments, d => d is { Department: "Office", Count: 1 });
        Assert.Equal("3 Apparel, 1 Office", hint.ToDisplayString());
    }

    [Fact]
    public void Parse_ThreeOrderDigest_ReturnsAHintPerOrder_DeptLineAfterOrderNumber()
    {
        var hints = _sut.Parse(ThreeOrderDigestHtml);

        Assert.Equal(3, hints.Count);
        Assert.Equal(("113-6641743-8180261", "Nutrition & Wellness"), (hints[0].OrderId, hints[0].Departments[0].Department));
        Assert.Equal(("113-5634569-7569032", "Grocery"), (hints[1].OrderId, hints[1].Departments[0].Department));
        Assert.Equal(3, hints[1].Departments[0].Count);
        Assert.Equal(("113-6275981-3164251", "Nutrition & Wellness"), (hints[2].OrderId, hints[2].Departments[0].Department));
    }

    [Fact]
    public void Parse_Digest_DeptLineBeforeOrderNumber_StillMatchesEachOrderToItsOwnDepartment()
    {
        var hints = _sut.Parse(TwoOrderDigestDeptBeforeOrderHtml);

        Assert.Equal(2, hints.Count);
        Assert.Equal(("113-3097615-2760211", "Supplements"), (hints[0].OrderId, hints[0].Departments[0].Department));
        Assert.Equal(("113-8477021-1529854", "Vitamins"), (hints[1].OrderId, hints[1].Departments[0].Department));
    }

    [Fact]
    public void Parse_SingularItemColonForm_ResolvesToTheSingleDepartment()
    {
        var hints = _sut.Parse(SingularItemColonFormHtml);

        var hint = Assert.Single(hints);
        Assert.Equal("113-6146265-7385012", hint.OrderId);
        var dept = Assert.Single(hint.Departments);
        Assert.Equal("Supplements", dept.Department);
        Assert.Equal("Supplements", hint.ToDisplayString());
    }

    [Fact]
    public void Parse_NoDepartmentLine_ReturnsAHintWithNoDepartments_NoThrow()
    {
        var hints = _sut.Parse(NoDepartmentLineHtml);

        var hint = Assert.Single(hints);
        Assert.Equal("113-1234567-1234567", hint.OrderId);
        Assert.Empty(hint.Departments);
        Assert.Equal("", hint.ToDisplayString());
    }

    [Fact]
    public void Parse_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(_sut.Parse(null));
        Assert.Empty(_sut.Parse(""));
        Assert.Empty(_sut.Parse("   "));
    }

    [Fact]
    public void Parse_HtmlWithNoOrderNumberAtAll_ReturnsEmpty()
    {
        Assert.Empty(_sut.Parse("<html><body><p>Some unrelated Amazon marketing email. 2 Supplements on sale!</p></body></html>"));
    }
}
