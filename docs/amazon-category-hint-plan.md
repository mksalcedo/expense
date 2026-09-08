# Amazon Category Hints: auto-categorize detail-less order emails from the department name

Status: **built and verified against real production data (2026-09-08).** Written so an interrupted/disconnected session can resume without re-deriving it from conversation history.

**Verification note:** after deploying + applying the migration to prod, a real Amazon sync was run twice. It picked up a real Sept 8 supplement order (digest email, two orders `113-6146265-7385012` / `113-3773881-9769862`) and auto-categorized both to Supplements with `NeedsReview` cleared and title rewritten to "Amazon order — Supplements" - confirmed via direct `psql`. A concurrent real "Garden & Outdoor" order (`113-9957140-8010655`) correctly had its `DepartmentHint` backfilled but stayed in the Review Queue, since that department is deliberately unmapped. Live verification also caught a **fourth** hint line shape Amazon had just started using - the singular `1 item: Supplements` (colon, department last) - which the parser was extended to handle.

## Problem

Since mid-July 2026, Amazon's `auto-confirm@amazon.com` order-confirmation emails no longer contain an item list - just `Order #` and `Grand Total`. `AmazonOrderEmailParser` correctly falls back to a single `NeedsReview` placeholder per order (`ItemTitle = "(Item details unavailable in email - check Amazon order page)"`, `CategoryId = null`). The user then has to open each order's Amazon page and categorize it by hand. At ~22 orders/month, ~85-90% of them supplement reorders, this is a lot of repetitive manual work.

**The category is in the email - just in the part we don't parse.** Every Amazon email is sent as two MIME parts, `text/plain` and `text/html`. `GmailMessageParsing.ExtractPlainTextBody` only ever reads `text/plain`, and Amazon deliberately omits the category line from that part. The `text/html` part carries, per order, a line like:

- `2 Supplements` / `1 Nutrition & Wellness item` / `3 Grocery items` - single department
- `4 items: 3 Apparel, 1 Office` - genuinely mixed order, broken down by count

Digest emails (multiple orders in one message) give each `Order #` its own block with its own department line and `Grand Total` in the HTML.

The **email subject** also carries the department(s) (`Ordered 2 items: Supplements, Vitamins` / `Ordered: 3 Grocery items`) and has since ~July 18 - but it's per-email, so a digest's subject aggregates across orders and can't be mapped per-order. The HTML body's per-order line is the better signal; the subject is at best a redundant backup.

## Real evidence gathered (not guessed)

### Backtest: 38 orders, 2026-07-18 → 2026-09-06

Method: for each order the user categorized in the detail-less era, pull the email's HTML body, extract the per-order `{count} {department}` line, map department → user category with a seed table (`Nutrition & Wellness / Supplements / Vitamins / Health Care → Supplements`, `Grocery → Groceries`), and **auto-assign only when every department named for that order maps to exactly one user category**; otherwise leave it for review. Compared against the category the user actually assigned (DB ground truth). ~12 non-supplement / mixed / unusual orders were checked against full HTML; the ~26 plain supplement orders were each confirmed against their DB category (not assumed).

| Outcome | Count | % |
|---|---|---|
| Auto-categorized, correct | 32 | 84% |
| Correctly deferred (email shows mixed departments) | 2 | 5% |
| Deferred - department not in seed map | 4 | 11% |
| **Auto-categorized, wrong** | **0** | **0%** |

The 32 correct: 30 supplement orders + 2 grocery orders. Every supplement-family department name Amazon used (`Nutrition & Wellness`, `Supplements`, `Vitamins`, `Health Care`) mapped to the user's Supplements category, every time.

The 6 deferred:

| Date | HTML department line | User filed it as | Why it defers |
|---|---|---|---|
| 7/22 | `2 items: 1 Home, 1 Nutrition & Wellness` | Off-Budget/Misc + Supplements | genuinely mixed - needs the order page regardless |
| 8/17 | `4 items: 3 Apparel, 1 Office` | Clothing + Office Supplies | genuinely mixed |
| 8/6, 8/6, 8/9 | `1 Kitchen item` (x3) | Off-Budget/Misc (water filters) | "Kitchen" not in seed map |
| 8/18 | `1 Exercise & Fitness item` | Subscriptions (vibration plate) | not in seed map |

### Order-volume reconciliation (the backtest population is complete)

`auto-confirm@amazon.com` confirmation emails for 7/18-9/8: ~29 Gmail threads / ~35 messages (a few bundle 2-3 orders). DB: 38 distinct orders in that window. Amex: ~40 Amazon charges. All three line up - there is no hidden population of un-imported orders. Real rate is ~20-24 orders/month, steady all year.

### HTML structure

Department line lives in `<span class="rio-text rio-text-NNN">` inside a `<tr>` adjacent to the `Order #` row's `<tr>`, within a per-order block. Numbers are wrapped in Unicode isolate marks (`U+2066`..`U+2069`); the order id has a `U+202B` prefix. Example raw:

```html
<span class="rio-text rio-text-339">&#8294;4&#8297; items: &#8294;3&#8297; Apparel, &#8294;1&#8297; Office</span>
...
Order #</span> <span>&#8299;113-8777231-4791428</span>
```

The `text/plain` MIME part (what we parse today) does **not** contain this line - confirmed against the Gmail API's own `plaintextBody`, so it's Amazon's omission, not a conversion artifact on our side.

## Design decisions

1. **Keep `text/plain` as the primary parse source, unchanged.** The 300+ real-order itemized-parsing corpus stays on the exact code path it's on now. The HTML body is passed alongside purely as input to a new, isolated hint extractor - minimal blast radius.

2. **Auto-assign the category only when the order's department(s) resolve to exactly one user category.** A single-department order whose department is mapped → assign. A multi-department order → assign only if every department maps to the *same* user category (e.g. `Supplements` + `Vitamins` → both Supplements). Anything else (mixed categories, or any unmapped department) → leave `CategoryId = null`, exactly as today. This is what keeps the wrong-rate at zero.

3. **When auto-categorized, clear `NeedsReview` - the order is done and does not appear in the Review Queue.** The item title stays a placeholder (lightly improved to `Amazon order - {Category} ({shortOrderId})` so it doesn't look broken in the transaction list), but the title only feeds the Historical Analysis "Recurring Products" report, which is already near-useless in practice (27 sparse, title-fragmented rows out of 368 items; see below) - so forcing a review for the title's sake buys nothing real today. The raw email body, order-details URL, source message id, and parsed department hint are still stored on the row, so a future improved product report could backfill titles from them. An order that does **not** auto-categorize (mixed categories, or an unmapped department) keeps `NeedsReview = true` and behaves exactly as today.

   *Context checked during planning:* `HistoricalAnalysisService.GetRecurringProductReportAsync` only includes `AmazonOrderItem`s with a non-null `ProductId` (27 of 368 rows), every current product has exactly one linked item, near-identical titles fragment genuine repeats into separate rows, and four product rows have the literal placeholder string as their pattern (a pre-existing bug - a placeholder categorized before its title was fixed; worth a separate cleanup, out of scope here).

4. **The department→category map is user-owned data, seeded conservatively.** Only the departments proven safe in the backtest are seeded (`Nutrition & Wellness`, `Supplements`, `Vitamins`, `Health Care` → Supplements; `Grocery` → Groceries). Ambiguous Amazon departments (`Kitchen`, `Home`, `Health & Household`, `Exercise & Fitness`) are deliberately left unmapped - they mean different things for this user in different orders, and an unmapped department simply defers to review, which is today's behavior anyway. The user extends the map from the Review Queue as new departments appear.

5. **An auto-assigned category is never locked.** The row stays in the Review Queue, so a wrong auto-category is corrected there like any other. A later re-scan of the same email is a no-op (dedup is by `OrderId` for `NeedsReview` rows).

## Part 1: Capture the HTML body (TDD)

- `GmailMessage` record (`IGmailMessageSource.cs`) gains `string? HtmlBody`.
- `GmailMessageParsing` gains `ExtractHtmlBody(MessagePart)` - exact mirror of `ExtractPlainTextBody`, matching `part.MimeType == "text/html"`, same recursive descent, same base64url decode.
- `GoogleGmailMessageSource.SearchAsync` populates `HtmlBody` alongside the existing plain-text body.
- Both fake `IGmailMessageSource` implementations in the test suite (and any `GmailMessage` constructions in tests) updated for the new field.
- **Tests first**: `GmailMessageParsing` test that `ExtractHtmlBody` pulls the `text/html` part out of a real-shaped multipart `MessagePart` fixture and returns null when there's no HTML part; confirm it fails to compile against today's helper, then implement.

## Part 2: Parse per-order department hints from the HTML (TDD)

New class `AmazonOrderCategoryHintParser` (same folder). Pure, no DB.

```csharp
public record AmazonDepartmentCount(string Department, int Count);
public record AmazonOrderCategoryHint(string OrderId, IReadOnlyList<AmazonDepartmentCount> Departments);

public IReadOnlyList<AmazonOrderCategoryHint> Parse(string htmlBody);
```

Steps:

1. Reduce the HTML to text: strip `<script>/<style>`, replace block-closing tags with newlines, strip remaining tags, `WebUtility.HtmlDecode`, remove Unicode bidi/isolate marks (`⁦-⁩`, `‪-‮`, `‎`, `‏`), collapse intra-line whitespace. (No AngleSharp dependency - a regex reduction is enough for this one line; keep it in a private helper so it's unit-testable on its own.)
2. Slice into per-order blocks the same way the text parser handles multi-order digests: by `Order #\s*([\w-]{10,})` occurrences (each block runs to the next `Order #` or end).
3. Within each block, find the department line and parse it against, in order:
   - `^(\d+)\s+items:\s+(.+)$` → split the tail on `,`, each piece `^\s*(\d+)\s+(.+?)\s*$` → list of `AmazonDepartmentCount`
   - `^(\d+)\s+(.+?)\s+items?$` → single `AmazonDepartmentCount` (handles `1 Nutrition & Wellness item`, `3 Grocery items`)
   - `^(\d+)\s+([A-Z][A-Za-z&/ ]+?)$` → single `AmazonDepartmentCount` (handles the bare `2 Supplements` form from the newest template)
   - no match → this order has no hint (`Departments` empty); caller treats it as un-hinted, i.e. today's behavior
4. Department strings are stored/compared trimmed and case-insensitively.

**Tests first**, using real-shaped fixtures captured during planning (store them as fixture files, not synthetic strings):
- single-department, `... item` form → one hint, correct dept + count
- single-department, bare `2 Supplements` form → one hint
- genuinely mixed `4 items: 3 Apparel, 1 Office` → one hint, two `AmazonDepartmentCount`s
- digest email, 3 orders → 3 hints, each with its own dept + order id (use the real 2026-07-21 `Nutrition & Wellness and Grocery` digest body)
- HTML with no department line anywhere → empty result, no throw

## Part 3: Department -> Category mapping (TDD)

New entity + table `amazon_department_mappings`:

```csharp
public class AmazonDepartmentMapping
{
    public int Id { get; set; }
    public required string DepartmentName { get; set; }  // Amazon's department string, e.g. "Nutrition & Wellness"
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
}
```

- Unique index on `lower(department_name)` (citext or a computed lower column - match whatever convention the codebase already uses for case-insensitive uniqueness; check `MerchantRule`/`Product` first).
- Also add nullable `AmazonOrderItem.DepartmentHint` (string) here - populated by Part 4 on every placeholder row, read by Part 5's re-resolve.
- Migration `AddAmazonDepartmentMappings` (fold the `DepartmentHint` column into the same migration). Seed the mapping rows in the migration by looking category ids up by name (`Supplements`, `Groceries`) - guard for their absence so the migration is safe on a fresh/renamed DB. Also add the seed to `DbSeeder` for parity with test/dev DBs.
- **Entity round-trip test first** (mirrors `AmazonOrderItem` / `MerchantRule` test style), then the migration.

## Part 4: Apply the hint during import (TDD)

In `AmazonImportService.ImportOrderAsync`:

- Thread the HTML body through from `AmazonGmailSyncService.RunAsync` (`message.HtmlBody`) → `ImportOrderAsync` → available when building placeholder rows. (`AmazonOrderEmailParser.Parse` stays plain-text-only; the hint is applied in `ImportOrderAsync` after the parser returns its placeholder rows, matched to hints by `OrderId`.)
- For each `NeedsReview` placeholder row being inserted:
  1. Look up its `AmazonOrderCategoryHint` by `OrderId`.
  2. Resolve each `Department` via `amazon_department_mappings` (case-insensitive). If **any** named department is unmapped → no auto-assign.
  3. Collect the distinct resolved `CategoryId`s. If exactly one → `item.CategoryId = thatId`, `item.NeedsReview = false`, and set `item.ItemTitle = $"Amazon order - {categoryName} ({orderId[..10]})"`. If zero or more than one → leave `CategoryId = null` and `NeedsReview = true` (unchanged from today).
  4. Always store `DepartmentHint`, `RawEmailBody`, `OrderDetailsUrl`, `SourceMessageId` on the row regardless of whether it auto-categorized, so a wrong auto-category is traceable and a future product report can backfill.
- Record provenance in `NeedsReviewReason` even on the now-`NeedsReview=false` row (rename the field's usage in a comment, or leave it - it's just a free string): `"Category set from email department: Supplements"`. This is what a "why did this get this category" hover in the transaction list can show.
- `ReapplyRulesToPendingAsync` / product matching: unchanged. A `NeedsReview` row still skips product-pattern matching (its title is the generic placeholder). The department hint is the *only* way these rows get a category until their title is corrected.
- **Tests first** in the Amazon import test file:
  - placeholder order, HTML hint `1 Supplements item`, mapping exists → row inserted with `CategoryId` = Supplements, `NeedsReview` **false**, title rewritten, hint/body/url still stored
  - placeholder order, hint `4 items: 3 Apparel, 1 Office`, both mapped to *different* categories → `CategoryId` stays null
  - placeholder order, hint `1 Kitchen item`, `Kitchen` unmapped → `CategoryId` stays null
  - digest email, 3 orders, each hinted and mapped → 3 placeholders each with the right category
  - no HTML body at all (older email, or `text/html` missing) → behaves exactly as today (null category)
  - re-scan of an already-imported order → still deduped by `OrderId`, no double-insert, existing category untouched

## Part 5: UI - manage the map, and grow it from the Review Queue (TDD, bUnit)

Auto-categorized orders are not in the Review Queue at all (decision 3), so this part is only about the orders that *didn't* auto-resolve.

- **Review Queue**: for a `NeedsReview` Amazon row that has a department hint, show it ("Amazon department: *Kitchen*"). When the department is unmapped and the user picks a category, offer a "Remember: always file Amazon *Kitchen* as this category" checkbox next to the dropdown - ticking it on save creates an `AmazonDepartmentMapping` (same interaction shape as the existing merchant-rule "remember this" flow in `CategorizationService`). For a genuinely mixed hint (`3 Apparel, 1 Office`), show the breakdown but no single-checkbox shortcut.
- **A simple mapping editor** - a section on the Categories page or its own small page: list existing `department → category` rows, add/edit/delete. Low-frequency admin screen; no need to be fancy.
- **`AmazonOrderItem.DepartmentHint`** (nullable string, e.g. `"3 Apparel, 1 Office"` or `"Supplements"`) is populated by Part 4 on every placeholder row. After a mapping is created/changed, run a targeted re-resolve over still-`NeedsReview` Amazon rows whose stored hint now maps to exactly one category - set their category, clear `NeedsReview`, rewrite the title (same as Part 4's insert path). This catches the ones already sitting in the queue when you add e.g. "Kitchen → Off-Budget/Misc". Parallels `ReapplyRulesToPendingAsync`.
- **Tests**: bUnit coverage that the hint renders in the queue, the "remember" checkbox creates a mapping and clears the row, and adding a mapping re-resolves matching queued rows.

## Migration + production deploy

- One migration, `AddAmazonDepartmentMappings`: the `amazon_department_mappings` table + its seed rows + the `amazon_order_items.department_hint` column.
- Apply to `expense_test` (automatic via `DatabaseFixture`) and to the real prod DB (`dotnet ef database update --connection "<prod>"`, same pattern as every prior migration - see `docs/amazon-needs-review-plan.md`).
- `dotnet publish` + `systemctl --user restart expense`.

## Verification (against real data, not just tests)

1. After deploy, trigger a real Amazon sync. Its incremental window re-scans the last several days of emails; dedup means the already-imported orders are untouched, so this mainly proves nothing breaks.
2. Direct `psql`: confirm the seed mappings exist and point at the right category ids.
3. Wait for (or place) the next real supplement order; after the next sync, confirm via `psql` that its placeholder row came in with `CategoryId` = Supplements and `NeedsReview = true`, and that it shows correctly on the Spending Tracker without a manual step.
4. Re-run the planning backtest's logic against the live `amazon_department_mappings` + the stored hints to confirm the 84%/0%-wrong numbers hold on real post-deploy data.

## Explicitly out of scope

- **Recovering item titles / per-item prices from the email.** The email genuinely doesn't contain them in any part. The Amazon order-details page scrape stays the only source for that, and stays the workflow for the ~15% of orders that are genuinely mixed or in an ambiguous department.
- **Splitting a genuinely-mixed order's Grand Total across its departments.** We know the item *counts* per department (`3 Apparel, 1 Office`) but not the dollars. Proportional-by-count allocation was considered and rejected for v1 as too lossy to be worth the complexity - these orders defer to the page scrape.
- **Using the subject line.** The HTML body's per-order line supersedes it. Revisit only if a real email is found with the department in the subject but not the HTML body.
