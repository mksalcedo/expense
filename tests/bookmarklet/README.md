# Amazon order-scraper bookmarklet - regression test

The bookmarklet in `docs/amazon-order-scraper-bookmarklet.md` has broken live three times
(recommendation carousel x2, open mini-cart flyout once), each caught only by a real click
because the noise elements load via JS and aren't in a saved HTML page. This harness runs
the bookmarklet's **actual** `findItemBlocks()` - extracted verbatim from the doc's readable
```javascript block, single source of truth - against synthetic DOM fixtures that reproduce
each live-diagnosed failure, so the next selector change can be regression-checked offline
before it ships.

```
cd tests/bookmarklet
npm install          # jsdom, once
node test.js
```

Run this before changing any selector in the bookmarklet, and add a fixture whenever a new
live failure is diagnosed. It is not wired into `dotnet test` (different toolchain); it's a
manual pre-ship check, same as the ad-hoc Node+jsdom runs the doc already describes.
