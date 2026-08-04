# Category vs. Individually-Scheduled Item - a real data model gap (on hold, 2026-07-27)

Status: **on hold - design discussion only, nothing built.** Surfaced while investigating a real bug (Perimeter/Laura deferral collision - see `docs/cash-flow-whatif-analysis.md` and the November 2026 deferral incident). What started as "fix the matching key used by Deferral/Confirmation/PartialPayment" turned out to be a symptom of a deeper modeling problem, identified by the user, not a key-choice bug.

## The actual problem

`Category` currently conflates two genuinely different concepts into one:

1. **A reporting/grouping label** - how money should be summarized for Spending Tracker, budget-vs-actual, Historical Analysis (e.g. "Income," "Utilities," "Groceries").
2. **An individually-scheduled forecast line item** - something with its own amount, frequency, and specific date it hits (e.g. "EFX Paycheck," "MAS," "Piano," "Water," "GLAIC").

The schema forces these to be the same thing, one-to-one, via `BudgetPeriod.CategoryId` (each category can only carry one active schedule at a time). That's *why* EFX Paycheck, MAS, and Piano exist as three separate categories today, even though the user naturally thinks of all three as "Income" - the only way to give each its own schedule was to give each its own category. Same story for Water and GLAIC, which the user would naturally want grouped as "Utilities."

**Direct user quote worth preserving**: "I would see anything that is income related as a category of Income - including my EFX paycheck, MAS income, and also Piano. But instead, you made all 3 of those have a different category... I still wanted to forecast each of those 3 at different times, so you said they had to be separate categories, which is really bizarre as a budgeting concept. Same thing for expenses. I might have wanted to have a general Utilities category, and lump all those types of expenses into the Utilities category, but as individually budgeted/forecasted line items, hitting at different times of the month."

## Why this matters beyond being conceptually awkward

The Perimeter/Laura and Water/GLAIC collisions (see `docs/cash-flow-whatif-analysis.md`) aren't really "wrong matching key" bugs - they're downstream symptoms of this same gap. `PaymentDeferral`/`PaymentConfirmation`/`PartialPayment` currently key off `AccountId` (proposed fix: `CategoryId`), but neither is really correct, because the thing that actually needs a stable, unique identity is the *individual scheduled item*, not the account it's funded from or the category it reports under - both of which can legitimately be shared by multiple distinct scheduled items.

## Direction discussed, not yet designed in detail

Separate the two concepts:
- **Category** stays a reporting/grouping label, largely as it exists today, but a category can now legitimately have *many* individually-scheduled items under it.
- **A new concept** (name not decided - "recurring item," "scheduled line item," something else) - carries its own name, amount, frequency, anchor date, direction, and account, and points *to* a `Category` for reporting purposes (many-to-one, not one-to-one).
- `PaymentDeferral`/`PaymentConfirmation`/`PartialPayment` would key off this new item's own stable identity, not `Category` or `Account` - resolving the collision class of bug at the root instead of patching the symptom.

## Real open question, not yet answered

How should Category and this new "individual item" concept actually relate on the Categories page UI? Right now categories are a flat list. Options not yet discussed: items nested under a category (expandable list?), a separate management screen for items with a category picker, something else. This needs real UI/UX discussion before implementation, not just a data model decision.

## Scope note

This is bigger than the three-table fix originally proposed (`docs/cash-flow-whatif-analysis.md`'s deferral bug). Likely touches: Categories page, budget management, `ForecastEngine`, `TransactionReconciliationService`, Spending Tracker, Historical Analysis, and the Deferral/Confirmation/PartialPayment mechanisms. Needs a full design conversation before implementation - not started yet.

## Next step

Resume this conversation when ready - starting from the open UI question above. The narrower stopgap fix (add `CategoryId` to the three tables plus a database constraint preventing duplicate active budget periods per category) is still available as an interim patch if the bigger redesign takes a while, but hasn't been decided as the path forward either way.
