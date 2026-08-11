# Amazon order-page scraper bookmarklet

## What this is

The Review Queue's "Paste screenshot / order data" flow for a NeedsReview Amazon item
(see `docs/amazon-needs-review-plan.md`) accepts two things pasted into the same target:

1. A **screenshot** of the order-details page - Claude Vision reads the item title(s) off it.
   Only the first item found is applied; a multi-item order needs the rest added by hand
   via "+ Add another item". This still works exactly as before - nothing about it changed.
2. **Structured order data**, copied to the clipboard by this bookmarklet - exact title,
   price, and quantity read straight from the page's own HTML, no OCR guessing involved.
   Every item in the order is applied automatically (the first updates the existing
   placeholder row, the rest are added the same way "+ Add another item" would).

The bookmarklet is the newer, better path when it works - the screenshot path stays as a
fallback for as long as it's still useful (e.g. if Amazon changes their page layout and the
bookmarklet's selectors stop matching before they're fixed).

## Verified against three real saved order pages (2026-08-11)

Not a live browser test yet (see below), but the actual script - not a simulation of it -
was run end-to-end (via Node + jsdom) against three real Amazon order-details pages the
user saved and shared:

- **Single-item order** ($22.50, Pure Encapsulations B12 Folate): correctly found the one
  item, correct title, correct price (not the order total - confirmed $22.50 x1 + $1.35 tax
  = $23.85 total, and the scraper picked the item price, not the total). No quantity
  indicator exists anywhere on a single-item order page, so quantity correctly fell back to
  the default of 1.
- **Quantity-3 order** (Celestial Seasonings tea): initial version got this **wrong** -
  quantity silently defaulted to 1 instead of 3, because the real page never shows "Qty: N"
  as text anywhere; the number lives inside `<div class="od-item-view-qty"><span>3</span></div>`,
  a bare digit tagged only by a CSS class, not matched by any text-based "Qty" regex. Fixed
  by reading quantity from the nearest element whose class contains "qty" instead of
  searching visible text for the word "Qty". Also confirmed the price picked up was the
  correct **per-unit** price ($3.48), not the $10.44 line subtotal that happens to sit
  nearby in the DOM (3 x $3.48 = $10.44 - both real numbers on the page, only one is right
  for this app's Price-is-per-unit convention).
- **Two-item order** (Levoit air purifier filter + Pure Encapsulations B12, both quantity 1):
  correctly found both distinct items with correct titles and correct prices - confirmed
  $25.99 + $22.50 = $48.49 (matches the page's item(s) subtotal exactly), plus $2.91 tax =
  $51.40 (matches the order's grand total). This closes the "only tested single-product
  orders" gap from the first two samples.

All three real pages produce exactly correct output with the current version below.

## What's still unverified

- **Never actually clicked in a real browser** - only run against saved HTML in Node. The
  live page includes some content that may load dynamically after the initial page load
  (e.g. a "Buy it again" recommendations section was present on one saved page but didn't
  appear in the static HTML at all - it's untested whether the live bookmarklet sees more
  candidate links than the saved-file tests did). The "no price found nearby" filter should
  reject those the same way it already correctly rejected two footer credit-card ad links
  on the single-item test, but this hasn't been confirmed live.
- **A multi-item order where the same product appears twice at different quantities**, or
  where one item in a multi-product order also has quantity > 1, hasn't been tested - all
  three real samples so far have quantity > 1 only in a single-item order.
- **Clipboard write permission** in a real browser session (vs. the mocked one used for
  testing) hasn't been confirmed - most browsers grant it automatically for a paste-adjacent,
  user-gesture-triggered call like this, but worth knowing if the alert reports a clipboard
  error.

**To test it for real:** open a real Amazon order-details page (the same page the Review
Queue's "View on Amazon" link opens), click the bookmarklet, and see what the alert says:
- If it says "found 0 items" - the selectors didn't match this page's layout at all.
- If it copies items but the titles/prices/quantities look wrong - report back what you see
  (or save the page and send it over, same as before) and the selectors can be adjusted.
- If it looks right - paste it into the Review Queue's paste target and confirm the applied
  title/price/quantity are correct.

## Installation

1. Show your browser's bookmarks bar if it's hidden (Ctrl+Shift+B in Chrome/Firefox).
2. Right-click the bookmarks bar → **Add page** (or **New bookmark**).
3. Name it something like `Scrape Amazon Order`.
4. Paste the entire `javascript:...` block below (the whole thing, starting with `javascript:`) into the URL field.
5. Save.

To use it: open the Amazon order-details page for the order you need, click the bookmark,
then switch back to the Review Queue tab and paste (Ctrl+V) into the item's paste target.

## Source (readable)

```javascript
(function () {
    'use strict';

    function extractOrderId() {
        const params = new URLSearchParams(window.location.search);
        return params.get('orderID') || params.get('orderId') || null;
    }

    function parsePrice(text) {
        const match = text.match(/\$([\d,]+\.\d{2})/);
        return match ? parseFloat(match[1].replace(/,/g, '')) : null;
    }

    // Amazon's order-details page layout varies (regular physical orders, digital orders,
    // Subscribe & Save, etc.) and changes over time without notice - this looks for the
    // most common pattern: each real item is a product link (href containing "/product/"
    // or "/dp/") with a price and a quantity indicator somewhere in the same surrounding
    // block. Verified against two real saved order pages (2026-08-11): quantity is NOT
    // shown as "Qty: N" text anywhere - it's a bare number inside an element whose class
    // contains "qty" (e.g. <div class="od-item-view-qty"><span>3</span></div>), which is
    // why this reads quantity from that element directly rather than by matching visible
    // text. A single-item order has no such element at all, so quantity correctly falls
    // back to 1 when none is found. This is still a best-effort based on only two real
    // samples - see amazon-order-scraper-bookmarklet.md for what to do if it doesn't match
    // your actual order page.
    function findItemBlocks() {
        const links = Array.from(document.querySelectorAll('a[href*="/product/"], a[href*="/dp/"]'))
            .filter(a => a.textContent.trim().length > 8);

        const seen = new Set();
        const items = [];

        for (const link of links) {
            const title = link.textContent.trim();
            if (seen.has(title)) continue;

            // Walk up a few ancestor levels looking for a price and quantity nearby -
            // Amazon nests the actual price/qty in a sibling block, not right next to the
            // link itself.
            let block = link;
            let priceText = null;
            let quantity = null;
            for (let i = 0; i < 6 && block; i++) {
                block = block.parentElement;
                if (!block) break;
                const text = block.textContent;
                if (!priceText && /\$[\d,]+\.\d{2}/.test(text)) priceText = text;
                if (quantity === null) {
                    const qtyEl = block.querySelector('[class*="qty" i]');
                    if (qtyEl) {
                        const qtyMatch = qtyEl.textContent.match(/\d+/);
                        if (qtyMatch) quantity = parseInt(qtyMatch[0], 10);
                    }
                }
                if (priceText && quantity !== null) break;
            }

            const price = priceText ? parsePrice(priceText) : null;
            // No price found nearby - likely a "Buy it again"/related-product suggestion
            // link, not a real line item in this order. Skip it rather than guess.
            if (price === null) continue;

            seen.add(title);
            items.push({
                title: title,
                price: price,
                quantity: quantity !== null ? quantity : 1
            });
        }

        return items;
    }

    const orderId = extractOrderId();
    const items = findItemBlocks();

    if (items.length === 0) {
        alert('Amazon order scraper: found 0 items on this page. The page layout may not ' +
            'match what this bookmarklet expects - report this back so the selectors can ' +
            'be adjusted.');
        return;
    }

    const payload = JSON.stringify({ orderId: orderId, items: items });
    navigator.clipboard.writeText(payload).then(function () {
        const summary = items.map(function (i) {
            return '- ' + i.title + ' ($' + i.price.toFixed(2) + ' x' + i.quantity + ')';
        }).join('\n');
        alert('Copied ' + items.length + ' item(s) to clipboard:\n\n' + summary +
            '\n\nNow switch to the Review Queue tab and paste (Ctrl+V) into the item\'s paste target.');
    }).catch(function (err) {
        alert('Found ' + items.length + ' item(s) but could not copy to clipboard: ' + err.message);
    });
})();
```

## Bookmarklet URL (paste this whole thing as the bookmark's URL)

Generated programmatically from the readable source above (percent-encoded via Python's
`urllib.parse.quote`, then round-trip decoded and syntax-checked with `node --check` to
confirm it matches exactly) - not hand-encoded, so it isn't at risk of the kind of stray
unescaped-character bug that's easy to introduce by typing percent-encoding out by hand.

```
javascript:%28function%20%28%29%20%7B%0A%20%20%20%20%27use%20strict%27%3B%0A%0A%20%20%20%20function%20extractOrderId%28%29%20%7B%0A%20%20%20%20%20%20%20%20const%20params%20%3D%20new%20URLSearchParams%28window.location.search%29%3B%0A%20%20%20%20%20%20%20%20return%20params.get%28%27orderID%27%29%20%7C%7C%20params.get%28%27orderId%27%29%20%7C%7C%20null%3B%0A%20%20%20%20%7D%0A%0A%20%20%20%20function%20parsePrice%28text%29%20%7B%0A%20%20%20%20%20%20%20%20const%20match%20%3D%20text.match%28%2F%5C%24%28%5B%5Cd%2C%5D%2B%5C.%5Cd%7B2%7D%29%2F%29%3B%0A%20%20%20%20%20%20%20%20return%20match%20%3F%20parseFloat%28match%5B1%5D.replace%28%2F%2C%2Fg%2C%20%27%27%29%29%20%3A%20null%3B%0A%20%20%20%20%7D%0A%0A%20%20%20%20%2F%2F%20Amazon%27s%20order-details%20page%20layout%20varies%20%28regular%20physical%20orders%2C%20digital%20orders%2C%0A%20%20%20%20%2F%2F%20Subscribe%20%26%20Save%2C%20etc.%29%20and%20changes%20over%20time%20without%20notice%20-%20this%20looks%20for%20the%0A%20%20%20%20%2F%2F%20most%20common%20pattern%3A%20each%20real%20item%20is%20a%20product%20link%20%28href%20containing%20%22%2Fproduct%2F%22%0A%20%20%20%20%2F%2F%20or%20%22%2Fdp%2F%22%29%20with%20a%20price%20and%20a%20quantity%20indicator%20somewhere%20in%20the%20same%20surrounding%0A%20%20%20%20%2F%2F%20block.%20Verified%20against%20two%20real%20saved%20order%20pages%20%282026-08-11%29%3A%20quantity%20is%20NOT%0A%20%20%20%20%2F%2F%20shown%20as%20%22Qty%3A%20N%22%20text%20anywhere%20-%20it%27s%20a%20bare%20number%20inside%20an%20element%20whose%20class%0A%20%20%20%20%2F%2F%20contains%20%22qty%22%20%28e.g.%20%3Cdiv%20class%3D%22od-item-view-qty%22%3E%3Cspan%3E3%3C%2Fspan%3E%3C%2Fdiv%3E%29%2C%20which%20is%0A%20%20%20%20%2F%2F%20why%20this%20reads%20quantity%20from%20that%20element%20directly%20rather%20than%20by%20matching%20visible%0A%20%20%20%20%2F%2F%20text.%20A%20single-item%20order%20has%20no%20such%20element%20at%20all%2C%20so%20quantity%20correctly%20falls%0A%20%20%20%20%2F%2F%20back%20to%201%20when%20none%20is%20found.%20This%20is%20still%20a%20best-effort%20based%20on%20only%20two%20real%0A%20%20%20%20%2F%2F%20samples%20-%20see%20amazon-order-scraper-bookmarklet.md%20for%20what%20to%20do%20if%20it%20doesn%27t%20match%0A%20%20%20%20%2F%2F%20your%20actual%20order%20page.%0A%20%20%20%20function%20findItemBlocks%28%29%20%7B%0A%20%20%20%20%20%20%20%20const%20links%20%3D%20Array.from%28document.querySelectorAll%28%27a%5Bhref%2A%3D%22%2Fproduct%2F%22%5D%2C%20a%5Bhref%2A%3D%22%2Fdp%2F%22%5D%27%29%29%0A%20%20%20%20%20%20%20%20%20%20%20%20.filter%28a%20%3D%3E%20a.textContent.trim%28%29.length%20%3E%208%29%3B%0A%0A%20%20%20%20%20%20%20%20const%20seen%20%3D%20new%20Set%28%29%3B%0A%20%20%20%20%20%20%20%20const%20items%20%3D%20%5B%5D%3B%0A%0A%20%20%20%20%20%20%20%20for%20%28const%20link%20of%20links%29%20%7B%0A%20%20%20%20%20%20%20%20%20%20%20%20const%20title%20%3D%20link.textContent.trim%28%29%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20if%20%28seen.has%28title%29%29%20continue%3B%0A%0A%20%20%20%20%20%20%20%20%20%20%20%20%2F%2F%20Walk%20up%20a%20few%20ancestor%20levels%20looking%20for%20a%20price%20and%20quantity%20nearby%20-%0A%20%20%20%20%20%20%20%20%20%20%20%20%2F%2F%20Amazon%20nests%20the%20actual%20price%2Fqty%20in%20a%20sibling%20block%2C%20not%20right%20next%20to%20the%0A%20%20%20%20%20%20%20%20%20%20%20%20%2F%2F%20link%20itself.%0A%20%20%20%20%20%20%20%20%20%20%20%20let%20block%20%3D%20link%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20let%20priceText%20%3D%20null%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20let%20quantity%20%3D%20null%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20for%20%28let%20i%20%3D%200%3B%20i%20%3C%206%20%26%26%20block%3B%20i%2B%2B%29%20%7B%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20block%20%3D%20block.parentElement%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20if%20%28%21block%29%20break%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20const%20text%20%3D%20block.textContent%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20if%20%28%21priceText%20%26%26%20%2F%5C%24%5B%5Cd%2C%5D%2B%5C.%5Cd%7B2%7D%2F.test%28text%29%29%20priceText%20%3D%20text%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20if%20%28quantity%20%3D%3D%3D%20null%29%20%7B%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20const%20qtyEl%20%3D%20block.querySelector%28%27%5Bclass%2A%3D%22qty%22%20i%5D%27%29%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20if%20%28qtyEl%29%20%7B%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20const%20qtyMatch%20%3D%20qtyEl.textContent.match%28%2F%5Cd%2B%2F%29%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20if%20%28qtyMatch%29%20quantity%20%3D%20parseInt%28qtyMatch%5B0%5D%2C%2010%29%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%7D%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%7D%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20if%20%28priceText%20%26%26%20quantity%20%21%3D%3D%20null%29%20break%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20%7D%0A%0A%20%20%20%20%20%20%20%20%20%20%20%20const%20price%20%3D%20priceText%20%3F%20parsePrice%28priceText%29%20%3A%20null%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20%2F%2F%20No%20price%20found%20nearby%20-%20likely%20a%20%22Buy%20it%20again%22%2Frelated-product%20suggestion%0A%20%20%20%20%20%20%20%20%20%20%20%20%2F%2F%20link%2C%20not%20a%20real%20line%20item%20in%20this%20order.%20Skip%20it%20rather%20than%20guess.%0A%20%20%20%20%20%20%20%20%20%20%20%20if%20%28price%20%3D%3D%3D%20null%29%20continue%3B%0A%0A%20%20%20%20%20%20%20%20%20%20%20%20seen.add%28title%29%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20items.push%28%7B%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20title%3A%20title%2C%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20price%3A%20price%2C%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20quantity%3A%20quantity%20%21%3D%3D%20null%20%3F%20quantity%20%3A%201%0A%20%20%20%20%20%20%20%20%20%20%20%20%7D%29%3B%0A%20%20%20%20%20%20%20%20%7D%0A%0A%20%20%20%20%20%20%20%20return%20items%3B%0A%20%20%20%20%7D%0A%0A%20%20%20%20const%20orderId%20%3D%20extractOrderId%28%29%3B%0A%20%20%20%20const%20items%20%3D%20findItemBlocks%28%29%3B%0A%0A%20%20%20%20if%20%28items.length%20%3D%3D%3D%200%29%20%7B%0A%20%20%20%20%20%20%20%20alert%28%27Amazon%20order%20scraper%3A%20found%200%20items%20on%20this%20page.%20The%20page%20layout%20may%20not%20%27%20%2B%0A%20%20%20%20%20%20%20%20%20%20%20%20%27match%20what%20this%20bookmarklet%20expects%20-%20report%20this%20back%20so%20the%20selectors%20can%20%27%20%2B%0A%20%20%20%20%20%20%20%20%20%20%20%20%27be%20adjusted.%27%29%3B%0A%20%20%20%20%20%20%20%20return%3B%0A%20%20%20%20%7D%0A%0A%20%20%20%20const%20payload%20%3D%20JSON.stringify%28%7B%20orderId%3A%20orderId%2C%20items%3A%20items%20%7D%29%3B%0A%20%20%20%20navigator.clipboard.writeText%28payload%29.then%28function%20%28%29%20%7B%0A%20%20%20%20%20%20%20%20const%20summary%20%3D%20items.map%28function%20%28i%29%20%7B%0A%20%20%20%20%20%20%20%20%20%20%20%20return%20%27-%20%27%20%2B%20i.title%20%2B%20%27%20%28%24%27%20%2B%20i.price.toFixed%282%29%20%2B%20%27%20x%27%20%2B%20i.quantity%20%2B%20%27%29%27%3B%0A%20%20%20%20%20%20%20%20%7D%29.join%28%27%5Cn%27%29%3B%0A%20%20%20%20%20%20%20%20alert%28%27Copied%20%27%20%2B%20items.length%20%2B%20%27%20item%28s%29%20to%20clipboard%3A%5Cn%5Cn%27%20%2B%20summary%20%2B%0A%20%20%20%20%20%20%20%20%20%20%20%20%27%5Cn%5CnNow%20switch%20to%20the%20Review%20Queue%20tab%20and%20paste%20%28Ctrl%2BV%29%20into%20the%20item%5C%27s%20paste%20target.%27%29%3B%0A%20%20%20%20%7D%29.catch%28function%20%28err%29%20%7B%0A%20%20%20%20%20%20%20%20alert%28%27Found%20%27%20%2B%20items.length%20%2B%20%27%20item%28s%29%20but%20could%20not%20copy%20to%20clipboard%3A%20%27%20%2B%20err.message%29%3B%0A%20%20%20%20%7D%29%3B%0A%7D%29%28%29%3B
```

## Clipboard shape it produces

```json
{
  "orderId": "113-0140431-5777821",
  "items": [
    { "title": "Celestial Seasonings Wild Berry Zinger Herbal Tea, Caffeine Free, 20 Tea Bags Box", "price": 3.48, "quantity": 3 }
  ]
}
```

`orderId` is optional (parsed from the page's URL if present) - when it is present, the
Review Queue checks it against the item's actual known order and refuses to apply the data
if they don't match, rather than silently applying the wrong order's items to the wrong row.

## Status

Built 2026-08-11. Verified (via Node + jsdom, running the actual script end-to-end, not a
simulation) against three real saved order pages - single item/quantity 1, single
item/quantity 3, and two different items/quantity 1 each. All three now produce correct
output; the quantity-3 case caught and fixed a real bug (quantity silently defaulting to 1 -
see "Verified against three real saved order pages" above). Not yet clicked in an actual
live browser - see "What's still unverified" above.
