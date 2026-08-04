# Automatic Payment Smoothing for Large Bills (on hold, 2026-07-27)

Status: **on hold - functional scope not yet decided, nothing built.** Grew directly out of `docs/cash-flow-whatif-analysis.md`'s findings; read that first for the real-data backstory (three separate low points in the 12-month forecast, all traced to a single large Amex statement payment landing as a lump sum on its due date, all improved by manually splitting into two partial payments timed around real paychecks).

## The ask

A standing, configurable way to automatically split a large recurring payment (starting with Amex, possibly other bills/categories) into multiple smaller payments timed around paychecks, instead of manually creating partial payments for each cycle by hand.

## Why this isn't a trivial toggle

What actually made the manual splits work was timing each half *relative to real paycheck dates*, not a fixed calendar offset from the bill's own due date. This was already proven the hard way in the same analysis: a fixed 1st/15th split of a different bill (Perimeter) made things worse, not better, because a fixed calendar date drifts in and out of alignment with a biweekly paycheck that itself drifts across the calendar. So a genuinely useful version of this feature needs to know about the paycheck schedule and compute split dates relative to it - that's the real complexity, not "split into two payments."

## Two directions considered, not yet chosen between

1. **Paycheck-aware, fully automatic.** A bill/account gets configured with something like "split into N payments, each M days after the nearest occurrence of [a chosen income rule]." The forecast engine computes this automatically every cycle, no manual action needed. Real, valuable, matches the ask directly - but a genuinely new concept in the data model (a rule whose timing references *another* rule), touches `AmexCycleCalculator`/`ForecastEngine` directly, and needs a real decision about how it interacts with existing manual partial payments/deferrals on the same cycle (leaning: a manual override on a specific cycle should always win over the automatic split, same precedent as "Confirm Paid" already overriding the normal schedule elsewhere).

2. **Paycheck-aware, one-click assist (not automatic).** The Forecast page shows a "Split around nearest paychecks" button on any upcoming large payment; clicking it computes the two dates/amounts and creates the partial payments instantly (same math already done by hand in the what-if analysis). Still requires the user to see and approve it each cycle, rather than money movements happening silently. Smaller to build, and closer to how this app has handled every other real financial decision so far (deferrals, confirmations, partial payments are all explicit user actions today, by design - nothing currently happens automatically to a real payment schedule).

**Leaning, not yet decided**: option 2 first, given the app's consistent design pattern of keeping real financial commitments as deliberate, visible, user-confirmed actions rather than automated ones. Not committed to this - open to reconsidering once scope is discussed further.

## Open question, not yet answered

Which categories/bills should this actually apply to - just debt/credit accounts generally (Amex and similar), or specific budget categories too (like Perimeter, even though the what-if analysis found no benefit there)? This changes the shape of the feature and hasn't been discussed yet.

## Next step

Resume this conversation when ready - starting from the open scope question above.
