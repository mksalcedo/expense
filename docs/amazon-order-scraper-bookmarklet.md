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
bookmarklet's selectors stop matching before they're fixed). It's also available from inside
the app itself at **Amazon Order Scraper** in the nav menu (`/amazon-order-scraper`), with a
real draggable bookmarklet link - no manual copy-paste of a URL required.

## Two real live-browser bugs, found and fixed (2026-08-12)

The first real live click (not a saved-file test) found 28 items instead of 1 - the
bookmarklet had picked up Amazon's "Products related to your order" recommendation carousel
(a Berberine supplement, a multivitamin, aquarium test kits, a Yamaha piano - all completely
unrelated to the real order) in addition to the one real item. This carousel loads via JS
*after* the initial page render, so it was invisible to every prior "save the page, then
test" verification pass - it simply isn't present in a static HTML save. Each recommended
item also has a real price shown right next to it, so the "no price found nearby" filter
(which correctly excludes footer credit-card ad links) couldn't tell a recommendation apart
from a genuine order line either.

**First attempt:** scope the whole search to `document.querySelector('#orderDetails')` - a
stable, server-rendered container id present in every real saved order page tested - instead
of searching the entire document, on the theory that the carousel loads outside it. This
only partially worked: re-tried live, it dropped from 28 items to 13, still wrong.

**Root cause, found via a diagnostic pass:** rather than guess a third time, a second,
diagnostic-only bookmarklet was built - instead of scraping for the app, it dumps every
candidate link's ancestor chain (tags/ids/classes) and whether it's inside `#orderDetails`,
copies that report to the clipboard, and the user pasted it back into the conversation
directly (not into the app). That live DOM dump showed the real cause: the recommendation
carousel is Amazon's standard reusable widget - every single noise item, wherever it landed
in the DOM (including *inside* `#orderDetails` some of the time, which is why container
scoping only half-worked), sat inside an `<li class="a-carousel-card">`. The one real order
item never did.

**Actual fix:** exclude any candidate link found inside `.closest('.a-carousel-card')`,
rather than trying to scope by container. Verified two ways: against all five real saved
order pages (still correct, no regression), and against a synthetic HTML fixture built to
exactly reproduce the reported live structure (`#orderDetails` containing both the real item
and an `#adfeedbackdetails` > `.a-carousel` > `.a-carousel-card` block, matching the live
diagnostic dump byte-for-byte in shape) - since no real saved file has ever captured the
carousel at all, this synthetic fixture is the only way to regression-test this specific
fix's correctness offline.

## Verified against five real saved order pages (2026-08-11/12) plus one synthetic fixture

The actual script - not a simulation of it - was run end-to-end (via Node + jsdom) against
five real Amazon order-details pages the user saved and shared, plus one synthetic fixture
built to reproduce the carousel bug (see above, since a real save can never capture it):

- **Single-item order** ($22.50, Pure Encapsulations B12 Folate): correctly found the one
  item, correct title, correct price (not the order total - confirmed $22.50 x1 + $1.35 tax
  = $23.85 total). No quantity indicator exists anywhere on a single-item order page, so
  quantity correctly fell back to the default of 1.
- **Quantity-3 order** (Celestial Seasonings tea): initial version got this **wrong** -
  quantity silently defaulted to 1 instead of 3, because the real page never shows "Qty: N"
  as text anywhere; the number lives inside `<div class="od-item-view-qty"><span>3</span></div>`,
  a bare digit tagged only by a CSS class, not matched by any text-based "Qty" regex. Fixed
  by reading quantity from the nearest element whose class contains "qty" instead of
  searching visible text. Also confirmed the price picked up was the correct **per-unit**
  price ($3.48), not the $10.44 line subtotal sitting nearby in the DOM.
- **Two-item order** (Levoit air purifier filter + Pure Encapsulations B12, both quantity 1):
  correctly found both distinct items with correct titles and correct prices - confirmed
  $25.99 + $22.50 = $48.49 (matches the page's item(s) subtotal exactly), plus $2.91 tax =
  $51.40 (matches the order's grand total).
- **Single-item order, tested live twice** (Pure Encapsulations Vitamin D3, $21.00): exposed
  both bugs above in sequence (28 items, then 13 after the first fix) - neither saved copy of
  this page ever showed more than the 1 real item, which is exactly why the bug needed a live
  diagnostic pass rather than another saved-file test.
- **Synthetic carousel fixture**: built to exactly match the live diagnostic dump's reported
  structure. Confirms the `.a-carousel-card` exclusion works even when the carousel is
  nested inside `#orderDetails`, which is the specific case the first fix missed.

All produce exactly correct output with the current version below.

## What's still unverified

- **Not yet re-confirmed with an actual live click** after the `.a-carousel-card` fix - this
  is the third attempt at this specific page, so treat "should be right this time" with
  appropriate skepticism until it's actually re-tried live.
- **A multi-item order where the same product appears twice at different quantities**, or
  where one item in a multi-product order also has quantity > 1, hasn't been tested.
- **Clipboard write permission** in a real browser session (vs. the mocked one used for
  testing) hasn't been confirmed - most browsers grant it automatically for a paste-adjacent,
  user-gesture-triggered call like this, but worth knowing if the alert reports a clipboard
  error.
- **Other Amazon recommendation/carousel widgets** ("Buy it again," "Customers who bought
  this item also bought," etc.) presumably use the same `.a-carousel-card` markup and would
  be excluded the same way, but only the "Products related to your order" variant has
  actually been observed live.

**To test it for real:** open a real Amazon order-details page (the same page the Review
Queue's "View on Amazon" link opens), click the bookmarklet, and see what the alert says:
- If it says "found 0 items" - the selectors didn't match this page's layout at all.
- If it copies items but the titles/prices/quantities look wrong (or too many items) -
  **don't paste it into the Review Queue** - report back what you saw. If it's not obviously
  the same recommendation-carousel shape as before, the most useful next step is usually the
  diagnostic bookmarklet (ask for it) rather than another saved-page guess - a live DOM dump
  found the real cause much faster than saved-file iteration did.
- If it looks right - paste it into the Review Queue's paste target and confirm the applied
  title/price/quantity are correct.

## Installation

The app itself now has an install page with a real draggable link - **Amazon Order
Scraper** in the nav menu (`/amazon-order-scraper`). That's the easiest way to install or
update this bookmark. The manual steps below are the same thing, for reference:

1. Show your browser's bookmarks bar if it's hidden (Ctrl+Shift+B in Chrome/Firefox).
2. Right-click the bookmarks bar → **Add page** (or **New bookmark**).
3. Name it something like `Scrape Amazon Order`.
4. Paste the entire `javascript:...` block below (the whole thing, starting with `javascript:`) into the URL field.
5. Save.

To use it: open the Amazon order-details page for the order you need, click the bookmark,
then switch back to the Review Queue tab and paste (Ctrl+V) into the item's paste target.

A bookmark stores a static copy of the URL at save time - updating this doc/page later
doesn't retroactively update an already-saved bookmark. Re-install (drag the link again,
or replace the bookmark's URL) after any fix here.

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
    // block. Verified against five real saved/diagnosed order pages (2026-08-11/12):
    // quantity is NOT shown as "Qty: N" text anywhere - it's a bare number inside an
    // element whose class contains "qty" (e.g.
    // <div class="od-item-view-qty"><span>3</span></div>), which is why this reads
    // quantity from that element directly rather than by matching visible text. A
    // single-item order has no such element at all, so quantity correctly falls back to 1
    // when none is found.
    function findItemBlocks() {
        // Two real bugs found live, in order:
        // 1. "Products related to your order" (a recommendation carousel) loads via JS
        //    after the initial page render, so it's invisible to every saved-HTML test -
        //    only a real live click ever showed it (found live 2026-08-12: 28 items copied,
        //    27 from the carousel).
        // 2. Scoping to document.querySelector('#orderDetails') (the first fix) only
        //    partially helped (28 -> 13) - a live diagnostic dump showed the carousel is
        //    sometimes injected INSIDE #orderDetails too, not just alongside it, so
        //    container scoping alone can't tell it apart.
        // The real, reliable signal (confirmed via a live diagnostic pass, 2026-08-12):
        // every recommendation-carousel item, wherever it lands in the DOM, sits inside an
        // <li class="a-carousel-card"> - Amazon's standard reusable carousel widget markup,
        // used for "Products related to your order," "Buy it again," and similar modules.
        // The real order item never does. Excluding by that marker is what actually works,
        // rather than trying to scope by container.
        const links = Array.from(document.querySelectorAll('a[href*="/product/"], a[href*="/dp/"]'))
            .filter(a => a.textContent.trim().length > 8)
            .filter(a => !a.closest('.a-carousel-card'));

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
            // No price found nearby - likely a footer/promotional link, not a real line
            // item in this order. Skip it rather than guess.
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
javascript:%28function%20%28%29%20%7B%0A%20%20%20%20%27use%20strict%27%3B%0A%0A%20%20%20%20function%20extractOrderId%28%29%20%7B%0A%20%20%20%20%20%20%20%20const%20params%20%3D%20new%20URLSearchParams%28window.location.search%29%3B%0A%20%20%20%20%20%20%20%20return%20params.get%28%27orderID%27%29%20%7C%7C%20params.get%28%27orderId%27%29%20%7C%7C%20null%3B%0A%20%20%20%20%7D%0A%0A%20%20%20%20function%20parsePrice%28text%29%20%7B%0A%20%20%20%20%20%20%20%20const%20match%20%3D%20text.match%28%2F%5C%24%28%5B%5Cd%2C%5D%2B%5C.%5Cd%7B2%7D%29%2F%29%3B%0A%20%20%20%20%20%20%20%20return%20match%20%3F%20parseFloat%28match%5B1%5D.replace%28%2F%2C%2Fg%2C%20%27%27%29%29%20%3A%20null%3B%0A%20%20%20%20%7D%0A%0A%20%20%20%20%2F%2F%20Amazon%27s%20order-details%20page%20layout%20varies%20%28regular%20physical%20orders%2C%20digital%20orders%2C%0A%20%20%20%20%2F%2F%20Subscribe%20%26%20Save%2C%20etc.%29%20and%20changes%20over%20time%20without%20notice%20-%20this%20looks%20for%20the%0A%20%20%20%20%2F%2F%20most%20common%20pattern%3A%20each%20real%20item%20is%20a%20product%20link%20%28href%20containing%20%22%2Fproduct%2F%22%0A%20%20%20%20%2F%2F%20or%20%22%2Fdp%2F%22%29%20with%20a%20price%20and%20a%20quantity%20indicator%20somewhere%20in%20the%20same%20surrounding%0A%20%20%20%20%2F%2F%20block.%20Verified%20against%20five%20real%20saved%2Fdiagnosed%20order%20pages%20%282026-08-11%2F12%29%3A%0A%20%20%20%20%2F%2F%20quantity%20is%20NOT%20shown%20as%20%22Qty%3A%20N%22%20text%20anywhere%20-%20it%27s%20a%20bare%20number%20inside%20an%0A%20%20%20%20%2F%2F%20element%20whose%20class%20contains%20%22qty%22%20%28e.g.%0A%20%20%20%20%2F%2F%20%3Cdiv%20class%3D%22od-item-view-qty%22%3E%3Cspan%3E3%3C%2Fspan%3E%3C%2Fdiv%3E%29%2C%20which%20is%20why%20this%20reads%0A%20%20%20%20%2F%2F%20quantity%20from%20that%20element%20directly%20rather%20than%20by%20matching%20visible%20text.%20A%0A%20%20%20%20%2F%2F%20single-item%20order%20has%20no%20such%20element%20at%20all%2C%20so%20quantity%20correctly%20falls%20back%20to%201%0A%20%20%20%20%2F%2F%20when%20none%20is%20found.%0A%20%20%20%20function%20findItemBlocks%28%29%20%7B%0A%20%20%20%20%20%20%20%20%2F%2F%20Two%20real%20bugs%20found%20live%2C%20in%20order%3A%0A%20%20%20%20%20%20%20%20%2F%2F%201.%20%22Products%20related%20to%20your%20order%22%20%28a%20recommendation%20carousel%29%20loads%20via%20JS%0A%20%20%20%20%20%20%20%20%2F%2F%20%20%20%20after%20the%20initial%20page%20render%2C%20so%20it%27s%20invisible%20to%20every%20saved-HTML%20test%20-%0A%20%20%20%20%20%20%20%20%2F%2F%20%20%20%20only%20a%20real%20live%20click%20ever%20showed%20it%20%28found%20live%202026-08-12%3A%2028%20items%20copied%2C%0A%20%20%20%20%20%20%20%20%2F%2F%20%20%20%2027%20from%20the%20carousel%29.%0A%20%20%20%20%20%20%20%20%2F%2F%202.%20Scoping%20to%20document.querySelector%28%27%23orderDetails%27%29%20%28the%20first%20fix%29%20only%0A%20%20%20%20%20%20%20%20%2F%2F%20%20%20%20partially%20helped%20%2828%20-%3E%2013%29%20-%20a%20live%20diagnostic%20dump%20showed%20the%20carousel%20is%0A%20%20%20%20%20%20%20%20%2F%2F%20%20%20%20sometimes%20injected%20INSIDE%20%23orderDetails%20too%2C%20not%20just%20alongside%20it%2C%20so%0A%20%20%20%20%20%20%20%20%2F%2F%20%20%20%20container%20scoping%20alone%20can%27t%20tell%20it%20apart.%0A%20%20%20%20%20%20%20%20%2F%2F%20The%20real%2C%20reliable%20signal%20%28confirmed%20via%20a%20live%20diagnostic%20pass%2C%202026-08-12%29%3A%0A%20%20%20%20%20%20%20%20%2F%2F%20every%20recommendation-carousel%20item%2C%20wherever%20it%20lands%20in%20the%20DOM%2C%20sits%20inside%20an%0A%20%20%20%20%20%20%20%20%2F%2F%20%3Cli%20class%3D%22a-carousel-card%22%3E%20-%20Amazon%27s%20standard%20reusable%20carousel%20widget%20markup%2C%0A%20%20%20%20%20%20%20%20%2F%2F%20used%20for%20%22Products%20related%20to%20your%20order%2C%22%20%22Buy%20it%20again%2C%22%20and%20similar%20modules.%0A%20%20%20%20%20%20%20%20%2F%2F%20The%20real%20order%20item%20never%20does.%20Excluding%20by%20that%20marker%20is%20what%20actually%20works%2C%0A%20%20%20%20%20%20%20%20%2F%2F%20rather%20than%20trying%20to%20scope%20by%20container.%0A%20%20%20%20%20%20%20%20const%20links%20%3D%20Array.from%28document.querySelectorAll%28%27a%5Bhref%2A%3D%22%2Fproduct%2F%22%5D%2C%20a%5Bhref%2A%3D%22%2Fdp%2F%22%5D%27%29%29%0A%20%20%20%20%20%20%20%20%20%20%20%20.filter%28a%20%3D%3E%20a.textContent.trim%28%29.length%20%3E%208%29%0A%20%20%20%20%20%20%20%20%20%20%20%20.filter%28a%20%3D%3E%20%21a.closest%28%27.a-carousel-card%27%29%29%3B%0A%0A%20%20%20%20%20%20%20%20const%20seen%20%3D%20new%20Set%28%29%3B%0A%20%20%20%20%20%20%20%20const%20items%20%3D%20%5B%5D%3B%0A%0A%20%20%20%20%20%20%20%20for%20%28const%20link%20of%20links%29%20%7B%0A%20%20%20%20%20%20%20%20%20%20%20%20const%20title%20%3D%20link.textContent.trim%28%29%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20if%20%28seen.has%28title%29%29%20continue%3B%0A%0A%20%20%20%20%20%20%20%20%20%20%20%20%2F%2F%20Walk%20up%20a%20few%20ancestor%20levels%20looking%20for%20a%20price%20and%20quantity%20nearby%20-%0A%20%20%20%20%20%20%20%20%20%20%20%20%2F%2F%20Amazon%20nests%20the%20actual%20price%2Fqty%20in%20a%20sibling%20block%2C%20not%20right%20next%20to%20the%0A%20%20%20%20%20%20%20%20%20%20%20%20%2F%2F%20link%20itself.%0A%20%20%20%20%20%20%20%20%20%20%20%20let%20block%20%3D%20link%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20let%20priceText%20%3D%20null%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20let%20quantity%20%3D%20null%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20for%20%28let%20i%20%3D%200%3B%20i%20%3C%206%20%26%26%20block%3B%20i%2B%2B%29%20%7B%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20block%20%3D%20block.parentElement%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20if%20%28%21block%29%20break%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20const%20text%20%3D%20block.textContent%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20if%20%28%21priceText%20%26%26%20%2F%5C%24%5B%5Cd%2C%5D%2B%5C.%5Cd%7B2%7D%2F.test%28text%29%29%20priceText%20%3D%20text%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20if%20%28quantity%20%3D%3D%3D%20null%29%20%7B%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20const%20qtyEl%20%3D%20block.querySelector%28%27%5Bclass%2A%3D%22qty%22%20i%5D%27%29%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20if%20%28qtyEl%29%20%7B%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20const%20qtyMatch%20%3D%20qtyEl.textContent.match%28%2F%5Cd%2B%2F%29%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20if%20%28qtyMatch%29%20quantity%20%3D%20parseInt%28qtyMatch%5B0%5D%2C%2010%29%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%7D%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%7D%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20if%20%28priceText%20%26%26%20quantity%20%21%3D%3D%20null%29%20break%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20%7D%0A%0A%20%20%20%20%20%20%20%20%20%20%20%20const%20price%20%3D%20priceText%20%3F%20parsePrice%28priceText%29%20%3A%20null%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20%2F%2F%20No%20price%20found%20nearby%20-%20likely%20a%20footer%2Fpromotional%20link%2C%20not%20a%20real%20line%0A%20%20%20%20%20%20%20%20%20%20%20%20%2F%2F%20item%20in%20this%20order.%20Skip%20it%20rather%20than%20guess.%0A%20%20%20%20%20%20%20%20%20%20%20%20if%20%28price%20%3D%3D%3D%20null%29%20continue%3B%0A%0A%20%20%20%20%20%20%20%20%20%20%20%20seen.add%28title%29%3B%0A%20%20%20%20%20%20%20%20%20%20%20%20items.push%28%7B%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20title%3A%20title%2C%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20price%3A%20price%2C%0A%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20%20quantity%3A%20quantity%20%21%3D%3D%20null%20%3F%20quantity%20%3A%201%0A%20%20%20%20%20%20%20%20%20%20%20%20%7D%29%3B%0A%20%20%20%20%20%20%20%20%7D%0A%0A%20%20%20%20%20%20%20%20return%20items%3B%0A%20%20%20%20%7D%0A%0A%20%20%20%20const%20orderId%20%3D%20extractOrderId%28%29%3B%0A%20%20%20%20const%20items%20%3D%20findItemBlocks%28%29%3B%0A%0A%20%20%20%20if%20%28items.length%20%3D%3D%3D%200%29%20%7B%0A%20%20%20%20%20%20%20%20alert%28%27Amazon%20order%20scraper%3A%20found%200%20items%20on%20this%20page.%20The%20page%20layout%20may%20not%20%27%20%2B%0A%20%20%20%20%20%20%20%20%20%20%20%20%27match%20what%20this%20bookmarklet%20expects%20-%20report%20this%20back%20so%20the%20selectors%20can%20%27%20%2B%0A%20%20%20%20%20%20%20%20%20%20%20%20%27be%20adjusted.%27%29%3B%0A%20%20%20%20%20%20%20%20return%3B%0A%20%20%20%20%7D%0A%0A%20%20%20%20const%20payload%20%3D%20JSON.stringify%28%7B%20orderId%3A%20orderId%2C%20items%3A%20items%20%7D%29%3B%0A%20%20%20%20navigator.clipboard.writeText%28payload%29.then%28function%20%28%29%20%7B%0A%20%20%20%20%20%20%20%20const%20summary%20%3D%20items.map%28function%20%28i%29%20%7B%0A%20%20%20%20%20%20%20%20%20%20%20%20return%20%27-%20%27%20%2B%20i.title%20%2B%20%27%20%28%24%27%20%2B%20i.price.toFixed%282%29%20%2B%20%27%20x%27%20%2B%20i.quantity%20%2B%20%27%29%27%3B%0A%20%20%20%20%20%20%20%20%7D%29.join%28%27%5Cn%27%29%3B%0A%20%20%20%20%20%20%20%20alert%28%27Copied%20%27%20%2B%20items.length%20%2B%20%27%20item%28s%29%20to%20clipboard%3A%5Cn%5Cn%27%20%2B%20summary%20%2B%0A%20%20%20%20%20%20%20%20%20%20%20%20%27%5Cn%5CnNow%20switch%20to%20the%20Review%20Queue%20tab%20and%20paste%20%28Ctrl%2BV%29%20into%20the%20item%5C%27s%20paste%20target.%27%29%3B%0A%20%20%20%20%7D%29.catch%28function%20%28err%29%20%7B%0A%20%20%20%20%20%20%20%20alert%28%27Found%20%27%20%2B%20items.length%20%2B%20%27%20item%28s%29%20but%20could%20not%20copy%20to%20clipboard%3A%20%27%20%2B%20err.message%29%3B%0A%20%20%20%20%7D%29%3B%0A%7D%29%28%29%3B%0A
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

Built 2026-08-11, fixed twice on 2026-08-12 after two real live-click bugs (see above) - the
second fix came from a live DOM diagnostic dump rather than another saved-file guess, after
the first fix (`#orderDetails` scoping) only partially worked. Verified (via Node + jsdom,
running the actual script end-to-end, not a simulation) against five real saved order pages
plus one synthetic fixture reproducing the exact reported carousel structure. Not yet
re-confirmed with a live click since the `.a-carousel-card` fix - see "What's still
unverified" above.
