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

## IMPORTANT - this needs a real test pass

The extraction logic below was written without being able to see a live, logged-in Amazon
order-details page - Amazon's page structure isn't something that can be verified from
here. It's written defensively (multiple fallback patterns, fails loudly with an alert
showing exactly what it found instead of silently applying wrong data), but it will very
possibly need selector adjustments once tried against a real page.

**To test it:** open a real Amazon order-details page (the same page the Review Queue's
"View on Amazon" link opens), click the bookmarklet, and see what the alert says:
- If it says "found 0 items" - the selectors didn't match this page's layout at all.
- If it copies items but the titles/prices/quantities look wrong - the selectors matched
  the wrong elements.
- If it looks right - paste it into the Review Queue's paste target and confirm the applied
  title/price/quantity are correct.

Report back exactly what you saw (the alert text, or a screenshot of the order page if
something looks off) and the selectors can be adjusted from there.

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

    function parseQuantity(text) {
        const match = text.match(/Qty:?\s*(\d+)/i);
        return match ? parseInt(match[1], 10) : 1;
    }

    // Amazon's order-details page layout varies (regular physical orders, digital orders,
    // Subscribe & Save, etc.) and changes over time without notice - this looks for the
    // most common pattern: each real item is a product link (href containing "/product/"
    // or "/dp/") with a price and "Qty:" somewhere in the same surrounding block. This is
    // a best-effort v1 - see amazon-order-scraper-bookmarklet.md for what to do if it
    // doesn't match your actual order page.
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
            let qtyText = null;
            for (let i = 0; i < 6 && block; i++) {
                block = block.parentElement;
                if (!block) break;
                const text = block.textContent;
                if (!priceText && /\$[\d,]+\.\d{2}/.test(text)) priceText = text;
                if (!qtyText && /Qty:?\s*\d+/i.test(text)) qtyText = text;
                if (priceText && qtyText) break;
            }

            const price = priceText ? parsePrice(priceText) : null;
            // No price found nearby - likely a "Buy it again"/related-product suggestion
            // link, not a real line item in this order. Skip it rather than guess.
            if (price === null) continue;

            seen.add(title);
            items.push({
                title: title,
                price: price,
                quantity: qtyText ? parseQuantity(qtyText) : 1
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
javascript:%28function%20%28%29%20%7B%0A%20%20%20%20%27use%20strict%27%3B%0A%0A%20%20%20%20function%20extractOrderId%28%29%20%7B%0A%20%20%20%20%20%20%20%20const%20params%20%3D%20new%20URLSearchParams%28window.location.search%29%3B%0A%20%20%20%20%20%20%20%20return%20params.get%28%27orderID%27%29%20%7C%7C%20params.get%28%27orderId%27%29%20%7C%7C%20null%3B%0A%20%20%20%20%7D%0A%0A%20%20%20%20function%20parsePrice%28text%29%20%7B%0A%20%20%20%20%20%20%20%20const%20match%20%3D%20text.match%28%2F%5C%24%28%5B%5Cd%2C%5D%2B%5C.%5Cd%7B2%7D%29%2F%29%3B%0A%20%20%20%20%20%20%20%20return%20match%20%3F%20parseFloat%28match%5B1%5D.replace%28%2F%2C%2Fg%2C%20%27%27%29%29%20%3A%20null%3B%0A%20%20%20%20%7D%0A%0A%20%20%20%20function%20parseQuantity%28text%29%20%7B%0A%20%20%20%20%20%20%20%20const%20match%20%3D%20text.match%28%2FQty%3A%3F%5Cs%2A%28%5Cd%2B%29%2Fi%29%3B%0A%20%20%20%20%20%20%20%20return%20match%20%3F%20parseInt%28match%5B1%5D%2C%2010%29%20%3A%201%3B%0A%20%20%20%20%7D%0A%0A%20%20%20%20%2F%2F%20Amazon%27s%20order-details%20page%20layout%20varies%20%28regular%20physical%20orders%2C%20digital%20orders%2C%0A%20%20%20%20%2F%2F%20Subscribe%20%26%20Save%2C%20etc.%29%20and%20changes%20over%20time%20without%20notice%20-%20this%20looks%20for%20the%0A%20%20%20%20%2F%2F%20most%20common%20pattern%3A%20each%20real%20item%20is%20a%20product%20link%20%28href%20containing%20%22%2Fproduct%2F%22%0A%20%20%20%20%2F%2F%20or%20%22%2Fdp%2F%22%29%20with%20a%20price%20and%20%22Qty%3A%22%20somewhere%20in%20the%20same%20surrounding%20block.%20This%20is%0A%20%20%20%20%2F%2F%20a%20best-effort%20v1%20-%20see%20amazon-order-scraper-bookmarklet.md%20for%20what%20to%20do%20if%20it%0A%20%20%20%20%2F%2F%20doesn%27t%20match%20your%20actual%20order%20page.%0A%20%20%20%20function%20findItemBlocks%28%29%20%7B%0A%20%20%20%20%20%20%20%20const%20links%20%3D%20Array.from%28document.querySelectorAll%28%27a%5Bhref%2A%3D%22%2Fproduct%2F%22%5D%2C%20a%5Bhref%2A%3D%22%2Fdp%2F%22%5D%27%29%29%0A%20%20%20%20%20%20%20%20%20%20%20%20.filter%28a%20%3D%3E%20a.textContent.trim%28%29.length%20%3E%208%29%3B%0A%0A%20%20%20%20%20%20%20%20const%20seen%20%3D%20new%20Set%28%29%3B%0A%20%20%20%20%20%20%20%20const%20items%20%3D%20%5B%5D%3B%0A%0A%20%20%20%20%20%20%20%20for%20%28const%20link%20of%20links%29%20%7B%0A%20%20%20%20%20%20%20%20%20%20%20%20const%20title%20%3D%20link.textContent.trim%28%29%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20if%20%28seen.has%28title%29%29%20continue%3B%0A%0A%20%20%20%20%20%20%20%20%20%20%20%20%2F%2F%20Walk%20up%20a%20few%20ancestor%20levels%20looking%20for%20a%20price%20and%20quantity%20nearby%20-%0A%20%20%20%20%20%20%20%20%20%20%20%20%2F%2F%20Amazon%20nests%20the%20actual%20price%2Fqty%20in%20a%20sibling%20block%2C%20not%20right%20next%20to%20the%0A%20%20%20%20%20%20%20%20%20%20%20%20%2F%2F%20link%20itself.%0A%20%20%20%20%20%20%20%20%20%20%20%20let%20block%20%3D%20link%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20let%20priceText%20%3D%20null%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20let%20qtyText%20%3D%20null%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20for%20%28let%20i%20%3D%200%3B%20i%20%3C%206%20%26%26%20block%3B%20i%2B%2B%29%20%7B%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20block%20%3D%20block.parentElement%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20if%20%28%21block%29%20break%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20const%20text%20%3D%20block.textContent%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20if%20%28%21priceText%20%26%26%20%2F%5C%24%5B%5Cd%2C%5D%2B%5C.%5Cd%7B2%7D%2F.test%28text%29%29%20priceText%20%3D%20text%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20if%20%28%21qtyText%20%26%26%20%2FQty%3A%3F%5Cs%2A%5Cd%2B%2Fi.test%28text%29%29%20qtyText%20%3D%20text%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20if%20%28priceText%20%26%26%20qtyText%29%20break%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20%7D%0A%0A%20%20%20%20%20%20%20%20%20%20%20%20const%20price%20%3D%20priceText%20%3F%20parsePrice%28priceText%29%20%3A%20null%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20%2F%2F%20No%20price%20found%20nearby%20-%20likely%20a%20%22Buy%20it%20again%22%2Frelated-product%20suggestion%0A%20%20%20%20%20%20%20%20%20%20%20%20%2F%2F%20link%2C%20not%20a%20real%20line%20item%20in%20this%20order.%20Skip%20it%20rather%20than%20guess.%0A%20%20%20%20%20%20%20%20%20%20%20%20if%20%28price%20%3D%3D%3D%20null%29%20continue%3B%0A%0A%20%20%20%20%20%20%20%20%20%20%20%20seen.add%28title%29%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20items.push%28%7B%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20title%3A%20title%2C%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20price%3A%20price%2C%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20quantity%3A%20qtyText%20%3F%20parseQuantity%28qtyText%29%20%3A%201%0A%20%20%20%20%20%20%20%20%20%20%20%20%7D%29%3B%0A%20%20%20%20%20%20%20%20%7D%0A%0A%20%20%20%20%20%20%20%20return%20items%3B%0A%20%20%20%20%7D%0A%0A%20%20%20%20const%20orderId%20%3D%20extractOrderId%28%29%3B%0A%20%20%20%20const%20items%20%3D%20findItemBlocks%28%29%3B%0A%0A%20%20%20%20if%20%28items.length%20%3D%3D%3D%200%29%20%7B%0A%20%20%20%20%20%20%20%20alert%28%27Amazon%20order%20scraper%3A%20found%200%20items%20on%20this%20page.%20The%20page%20layout%20may%20not%20%27%20%2B%0A%20%20%20%20%20%20%20%20%20%20%20%20%27match%20what%20this%20bookmarklet%20expects%20-%20report%20this%20back%20so%20the%20selectors%20can%20%27%20%2B%0A%20%20%20%20%20%20%20%20%20%20%20%20%27be%20adjusted.%27%29%3B%0A%20%20%20%20%20%20%20%20return%3B%0A%20%20%20%20%7D%0A%0A%20%20%20%20const%20payload%20%3D%20JSON.stringify%28%7B%20orderId%3A%20orderId%2C%20items%3A%20items%20%7D%29%3B%0A%20%20%20%20navigator.clipboard.writeText%28payload%29.then%28function%20%28%29%20%7B%0A%20%20%20%20%20%20%20%20const%20summary%20%3D%20items.map%28function%20%28i%29%20%7B%0A%20%20%20%20%20%20%20%20%20%20%20%20return%20%27-%20%27%20%2B%20i.title%20%2B%20%27%20%28%24%27%20%2B%20i.price.toFixed%282%29%20%2B%20%27%20x%27%20%2B%20i.quantity%20%2B%20%27%29%27%3B%0A%20%20%20%20%20%20%20%20%7D%29.join%28%27%5Cn%27%29%3B%0A%20%20%20%20%20%20%20%20alert%28%27Copied%20%27%20%2B%20items.length%20%2B%20%27%20item%28s%29%20to%20clipboard%3A%5Cn%5Cn%27%20%2B%20summary%20%2B%0A%20%20%20%20%20%20%20%20%20%20%20%20%27%5Cn%5CnNow%20switch%20to%20the%20Review%20Queue%20tab%20and%20paste%20%28Ctrl%2BV%29%20into%20the%20item%5C%27s%20paste%20target.%27%29%3B%0A%20%20%20%20%7D%29.catch%28function%20%28err%29%20%7B%0A%20%20%20%20%20%20%20%20alert%28%27Found%20%27%20%2B%20items.length%20%2B%20%27%20item%28s%29%20but%20could%20not%20copy%20to%20clipboard%3A%20%27%20%2B%20err.message%29%3B%0A%20%20%20%20%7D%29%3B%0A%7D%29%28%29%3B
```

## Clipboard shape it produces

```json
{
  "orderId": "113-0140431-5777821",
  "items": [
    { "title": "THORNE Vitamin C", "price": 24.99, "quantity": 1 },
    { "title": "NeoCell Grassfed Collagen Peptides Powder", "price": 32.50, "quantity": 2 }
  ]
}
```

`orderId` is optional (parsed from the page's URL if present) - when it is present, the
Review Queue checks it against the item's actual known order and refuses to apply the data
if they don't match, rather than silently applying the wrong order's items to the wrong row.

## Status

Built 2026-08-11. Not yet verified against a real Amazon order page - see the warning above.
