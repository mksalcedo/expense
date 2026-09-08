using System.Net;
using System.Text.RegularExpressions;

namespace Expense.Domain.Services.Ingestion.Amazon;

/// <summary>
/// Pulls the per-order "{count} {department}" line out of an Amazon order-confirmation
/// email's <b>HTML</b> body - the text/plain part deliberately omits it. Three real shapes
/// (all seen live 2026-07..09):
///
/// <list type="bullet">
///   <item><c>2 Supplements</c> - bare, newest template</item>
///   <item><c>1 Nutrition &amp; Wellness item</c> / <c>3 Grocery items</c> - "... item(s)" form</item>
///   <item><c>4 items: 3 Apparel, 1 Office</c> - genuinely mixed order, per-department counts</item>
/// </list>
///
/// Digest emails carry several Order # blocks; each gets its own hint, matched to the
/// nearest department line. Never throws - an email with no recognizable hint yields an
/// empty list (or per-order entries with empty Departments), which callers treat exactly
/// like today's no-information placeholder path.
/// </summary>
public partial class AmazonOrderCategoryHintParser
{
    [GeneratedRegex(@"<(script|style|head)\b[^>]*>.*?</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ScriptStyleHead();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakTag();

    [GeneratedRegex(@"</(td|tr|div|p|h[1-6]|li|span|table)>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockCloseTag();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex AnyTag();

    // Unicode bidi/isolate marks Amazon wraps around numbers and the order id, plus soft hyphen.
    [GeneratedRegex(@"[⁦-⁩‪-‮‎‏­]")]
    private static partial Regex BidiMarks();

    [GeneratedRegex(@"\d{3}-\d{7}-\d{7}")]
    private static partial Regex OrderId();

    // "4 items: 3 Apparel, 1 Office" and the singular "1 item: Supplements" - "N item(s): <breakdown>"
    [GeneratedRegex(@"^(?<total>\d+)\s+items?:\s+(?<breakdown>.+)$")]
    private static partial Regex MixedForm();

    // "1 Nutrition & Wellness item" / "3 Grocery items"
    [GeneratedRegex(@"^(?<count>\d+)\s+(?<dept>.+?)\s+items?$")]
    private static partial Regex ItemForm();

    // "2 Supplements" - a bare count + a short capitalised department phrase and nothing else
    [GeneratedRegex(@"^(?<count>\d+)\s+(?<dept>[A-Z][A-Za-z][A-Za-z &/'-]{0,38})$")]
    private static partial Regex BareForm();

    // One piece of a mixed-form breakdown - "3 Apparel" (with count) or just "Supplements" (count implied).
    [GeneratedRegex(@"^\s*(?:(?<count>\d+)\s+)?(?<dept>[A-Za-z][A-Za-z0-9 &/'-]{0,48}?)\s*$")]
    private static partial Regex BreakdownPiece();

    public IReadOnlyList<AmazonOrderCategoryHint> Parse(string? htmlBody)
    {
        if (string.IsNullOrWhiteSpace(htmlBody)) return [];

        var lines = ToLines(htmlBody);

        var orderLines = new List<(int LineIndex, string OrderId)>();
        for (var i = 0; i < lines.Count; i++)
        {
            var idMatch = OrderId().Match(lines[i]);
            if (!idMatch.Success) continue;

            // Guard against a stray order-id-shaped token elsewhere in the body: require the
            // literal "Order #" label on this line or the one just before it.
            var hasLabel = lines[i].Contains("Order #", StringComparison.OrdinalIgnoreCase)
                           || (i > 0 && lines[i - 1].Contains("Order #", StringComparison.OrdinalIgnoreCase));
            if (hasLabel && orderLines.All(o => o.OrderId != idMatch.Value))
            {
                orderLines.Add((i, idMatch.Value));
            }
        }

        if (orderLines.Count == 0) return [];

        var deptLines = new List<(int LineIndex, IReadOnlyList<AmazonDepartmentCount> Departments)>();
        for (var i = 0; i < lines.Count; i++)
        {
            var parsed = ParseDepartmentLine(lines[i]);
            if (parsed.Count > 0)
            {
                deptLines.Add((i, parsed));
            }
        }

        var usedDeptLines = new HashSet<int>();
        var hints = new List<AmazonOrderCategoryHint>();
        foreach (var (orderLineIndex, orderId) in orderLines)
        {
            var nearest = deptLines
                .Where(d => !usedDeptLines.Contains(d.LineIndex))
                .OrderBy(d => Math.Abs(d.LineIndex - orderLineIndex))
                .Select(d => (int?)d.LineIndex)
                .FirstOrDefault();

            if (nearest is { } lineIndex)
            {
                usedDeptLines.Add(lineIndex);
                hints.Add(new AmazonOrderCategoryHint(orderId, deptLines.First(d => d.LineIndex == lineIndex).Departments));
            }
            else
            {
                hints.Add(new AmazonOrderCategoryHint(orderId, []));
            }
        }

        return hints;
    }

    private static IReadOnlyList<AmazonDepartmentCount> ParseDepartmentLine(string line)
    {
        var mixed = MixedForm().Match(line);
        if (mixed.Success)
        {
            var pieces = mixed.Groups["breakdown"].Value.Split(',');
            int.TryParse(mixed.Groups["total"].Value, out var total);
            var result = new List<AmazonDepartmentCount>();
            foreach (var piece in pieces)
            {
                var m = BreakdownPiece().Match(piece);
                if (!m.Success) continue;
                // A piece with no explicit count is the whole order (the singular
                // "1 item: Supplements" form); otherwise fall back to 1.
                var count = int.TryParse(m.Groups["count"].Value, out var c) ? c
                    : pieces.Length == 1 ? Math.Max(total, 1)
                    : 1;
                result.Add(new AmazonDepartmentCount(m.Groups["dept"].Value.Trim(), count));
            }
            return result;
        }

        var itemForm = ItemForm().Match(line);
        if (itemForm.Success && int.TryParse(itemForm.Groups["count"].Value, out var itemCount))
        {
            return [new AmazonDepartmentCount(itemForm.Groups["dept"].Value.Trim(), itemCount)];
        }

        var bareForm = BareForm().Match(line);
        if (bareForm.Success && int.TryParse(bareForm.Groups["count"].Value, out var bareCount))
        {
            return [new AmazonDepartmentCount(bareForm.Groups["dept"].Value.Trim(), bareCount)];
        }

        return [];
    }

    private static List<string> ToLines(string html)
    {
        html = ScriptStyleHead().Replace(html, " ");
        html = LineBreakTag().Replace(html, "\n");
        html = BlockCloseTag().Replace(html, "\n");
        html = AnyTag().Replace(html, " ");
        html = WebUtility.HtmlDecode(html);
        html = BidiMarks().Replace(html, "");

        return html.Split('\n')
            .Select(l => Regex.Replace(l, @"\s+", " ").Trim())
            .Where(l => l.Length > 0)
            .ToList();
    }
}
