# Cash Flow What-If Analysis (started 2026-07-27)

Exploration of changes to smooth out low points in the 12-month forecast. Each what-if is run against the real forecast engine (so real deferrals/confirmations/partial payments/Amex-cycle logic are all respected exactly as configured) inside a database transaction that's always rolled back - nothing here has touched real data unless a separate, explicit follow-up says otherwise.

Two different questions came up during this, worth keeping distinct:
- **Lowest point**: the single worst projected balance anywhere in the 12-month window.
- **Smoothness**: how much the balance swings week to week generally, independent of where the single worst point happens to be. Measured via weekly-sampled balance std dev, week-to-week volatility (std dev of changes), range, and worst single-week drop/jump.

A schedule can improve one without improving the other - several findings below only look good on one dimension, or neither.

## Perimeter Church ($900/month) - two variants tried, neither helped

- **Split into two fixed monthly dates (1st and 15th), same $900/month total**: fixed the *existing* worst week (April 25, 2027: $516.63 → $593.31) but created a new, deeper worst week in July ($559.46 → $109.46) that didn't exist before. Net: worse.
- **Switched to biweekly ($415.38 every 2 weeks, timed a few days after each paycheck), same annual total ($10,800/year either way)**: worse on every smoothness metric (range, both std devs, worst drop), and also worse at the lowest point ($516.63 → $74.96). The paycheck-aligned idea is structurally sound (avoids the fixed-date-vs-drifting-paycheck mismatch that broke the 1st/15th split), but even done correctly, paying more frequently front-loads cash out earlier and more often than one monthly lump sum, which turned out to be a net negative here.

Conclusion: Perimeter's payment schedule is not where the real leverage is - it's $900 against a cash flow that swings nearly $7,000/year, too small a lever to matter.

## Amex payment ($6,259.69, cycle due 2026-08-20/deferred display date 8/24) - real improvement found

Identified as the actual dominant driver of the year's worst single-week drop (-$6,316.86, week of 8/24/2026) by directly inspecting which ledger lines fired that week - it dwarfed everything else combined (~$500 total from other bills that same week).

**What-if**: split the same $6,259.69 into two partial payments, each timed 1-2 days after a real paycheck (8/23, 2 days after the 8/21 paycheck; 9/6, 2 days after the 9/4 paycheck), instead of one lump sum on the due date. Modeled using the app's own real `PartialPaymentService` mechanism (same one already used for existing partial payments), not a hypothetical bypass.

**Important gotcha hit while setting this up**: the partial-payment matching key is the cycle's *true original* `OriginalDate` (2026-08-20, matching this Amex account's real `PaymentDueDay`), not whatever *displayed* date shows in the ledger after an existing deferral is applied (2026-08-24 - this cycle already has a deferral pushing it 4 days later). Using the displayed date instead of the true original date silently fails to match at all, and the split payments get added *on top of* the full original payment instead of replacing it - a real trap worth remembering if this kind of what-if gets tried again.

**Result**: genuine improvement.
| Metric | Current (lump sum) | Split around paychecks |
|---|---|---|
| Balance range | $6,962.84 | $6,962.84 (unchanged) |
| Balance std dev | $1,878.11 | $1,775.08 |
| Week-to-week volatility | $3,260.46 | $3,106.23 |
| Worst single-week drop | -$6,316.86 | -$6,104.56 |
| Biggest single-week jump | $4,809.87 | $4,809.87 (unaffected) |

Both scenarios converge to the identical balance by 2026-09-10 ($2,299.52) - confirms this is a pure timing shift, not a cost change. **Did not move the year's overall lowest point** (still $516.63 on 2027-04-25, untouched) - this August Amex cycle was the driver of the worst *swing*, not the worst *floor*.

## Second Amex cycle found and fixed: April 2027 low point

Same pattern, found the same way (inspecting the dominant ledger line in the specific low-point week): **Amex Payment, $4,628.34, due 2027-04-20** is overwhelmingly the driver of the year's lowest point ($516.63 on 4/25/2027) - everything else in that window (Chase payment, Verizon, Gas) is small by comparison. Unlike the August cycle, this one has no pre-existing deferral or partial payment - a clean cycle to test.

**What-if**: split this $4,628.34 into two partial payments too, $2,314.17 each, timed 2 days after the nearest paychecks (4/18 and 5/2).

**Combined result (both Amex splits applied together)** - this is the first change in this whole exploration that improves *every* metric, not just some:

| Metric | Baseline | Both Amex cycles split |
|---|---|---|
| Lowest point | $516.63 (2027-04-25) | **$544.52** (2026-10-25) |
| Balance range | $6,962.84 | $6,934.95 |
| Balance std dev | $1,878.11 | $1,698.38 |
| Week-to-week volatility | $3,260.46 | $2,935.30 |
| Worst single-week drop | -$6,316.86 | -$6,104.56 |
| Biggest single-week jump | $4,809.87 | $4,809.87 (unaffected, unrelated driver) |

Both original low points (August, April) stop being the worst week once smoothed - the new floor relocates to a smaller, previously-hidden pinch point in October 2026, not yet investigated.

**Status: analysis only, nothing applied to real data yet.** If this gets adopted for real, it would use the app's existing "Partial Payment" feature on the Forecast page (same mechanism modeled here) for the 2026-08-20 and 2027-04-20 Amex cycles specifically.

## Third Amex cycle found (October 2026) - same pattern, not pursued further

Checked the new low point after the first two splits: $3,628.34 Amex payment due 2026-10-20 (already partially reduced by an existing $1,000 partial payment, still the dominant line in its week by a wide margin). Confirms the pattern a third time - not worth continued one-by-one investigation once the pattern was already established.

## Led to a feature idea, on hold

This pattern (every low point traced to a large Amex payment landing as a lump sum) prompted a real feature idea - a standing, configurable way to automatically split large payments around paychecks instead of doing it manually per cycle. See `docs/amex-payment-smoothing-plan.md` (on hold, scope not yet decided).
