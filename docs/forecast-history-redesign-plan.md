# Forecast History Redesign: Per-Sync Snapshots, Starting-Balance Deltas, and Reconciliation-Aware Diffing

Status: **researched and scoped, not yet built**. Written so an interrupted/disconnected session can resume without re-deriving the design from conversation history.

## Problem

On 2026-07-29, the user tried to use the Forecast History page to understand why the lowest projected balance had dropped from roughly +$30 (seen earlier that day) to -$35.10 (seen later), and found the page didn't help at all.

Two separate problems, uncovered in order:

**1. Immediate cause: real data corruption, caused by Claude.** While verifying the new Plaid-scheduling feature earlier the same day, a throwaway script was run directly against the production database to exercise `SyncStatusProvider.RunScheduledPlaidSyncAsync` for real. To avoid wiring up the full DI graph, the script stubbed `IForecastResultProvider` with a fake returning an empty forecast (`StartingBalance = 0, no rows`) instead of the real one. `RunScheduledPlaidSyncAsync` calls the real `CaptureForecastSnapshotAsync` on every successful run, which wrote that fake zero-value forecast into `forecast_snapshots` for real - three separate times across three script runs that day, each one clobbering whatever real snapshot had existed before it. By the time the user checked, today's row showed `StartingBalance = 0, LowestProjectedBalance = 0`, which is why the page was useless: the diff-since-yesterday was comparing a real day's forecast against garbage.

Fixed same day: recaptured today's snapshot using the real `ForecastResultProvider` (properly wired this time, no fakes), confirmed it now matches the live forecast (`StartingBalance = 4418.31, LowestProjectedBalance = -35.10`, matching the Dashboard).

**Lesson for future verification scripts**: never stub a dependency with real side effects on production data (anything that writes to the database) just to avoid wiring the DI graph. Either wire it for real, or don't call the method that has the side effect.

**2. Deeper problem: the storage/diffing design itself was never adequate for the actual goal.** Even with clean data, `ForecastSnapshotService.CaptureAsync` keeps only **one row per calendar day** - every new capture completely overwrites that day's previous one (`if (existing is not null) { Remove(existing); }` before adding the new one). Since a snapshot is captured after every successful sync (SimpleFin/Amazon/Plaid, scheduled or manual - now multiple times a day since Plaid runs on schedule too), only the *last* capture of any given day survives. Any intraday swing - which is now the normal case, not the exception - is invisible by construction, regardless of whether the earlier value was correct or (as above) garbage.

The user's stated goal, directly: *"My goal is to see exactly what transactions changed/were added/were removed and impacted the minimum balance... I need to know what's new that caused the minimum balance to change."*

## Research: everything that can actually change the forecast

Read through `ForecastEngine.GenerateAsync` end to end (not just the parts already known about) to answer "what would a complete diffing mechanism need to account for." Findings, organized by whether the cause is a real change or just the passage of time:

### A. Real changes (the world changed)

- **`StartingBalance`** - pulled fresh from the latest `CheckingBalanceSnapshot` every generation. Likely the single biggest day-to-day driver of a shifted minimum balance. **Confirmed gap**: `ForecastSnapshotDiffer.Diff()` only ever compares `.Lines` - it never compares `previous.StartingBalance` vs `current.StartingBalance` at all. A perfect line-level diff would still silently miss "your starting balance dropped $70 because of a new debit," which is often the actual headline reason.
- **New/removed/changed forecast lines** - budget edits, a category's `IsActive` toggling, a debt account added/removed, a one-time event added/edited/deleted. This is what the existing `ForecastSnapshotDiffer` already covers, via `(AccountId, Description)` grouping with positional pairing in date order.
- **Reconciliation removing a line** - once a real transaction's `ReconciledOccurrenceDate` matches a forecast line's date, `IsAlreadyReflectedInAnActualTransaction` excludes that line from the ledger entirely (it's already reflected in the real starting balance; keeping the projected line too would double-count it). The line is not *adjusted* to the real amount, it just **disappears**. A $150 budgeted item that actually cost $162.37 shows up in today's diff as a bare "Removed: $150.00," with no indication it reconciled or what it really cost. That $12.37 variance is real money affecting every downstream running balance, and today's design throws the information away.
  - Related subtlety: `IsAlreadyReflectedInAnActualTransaction` has a hard-coded 5% tolerance floor (`ReconciliationAmountToleranceFraction`) - a real payment landing significantly under what was expected doesn't count as satisfying the line, so it stays projected. A payment landing right at that boundary can flip behavior in a way that would look like a mystery without knowing the rule exists.
- **The Amex/ActiveSpending payment line** - the least transparent one. Computed as `MAX(sum of every real charge on the account this cycle, prorated PayInFullAmex budget total) + extra principal`. The "every real charge" sum:
  - Includes *any* transaction on the account regardless of category (even uncategorized ones - it's a pay-in-full card, so an uncategorized charge still has to be paid).
  - Includes still-unposted charges, both self-reported (screenshot) and Plaid-reported pending.
  - Is a **raw sum over stored `BankTransaction` rows**, not pulled from an external balance figure - so a duplicate transaction directly inflates it. Every real duplicate found and fixed this session (Netflix, Publix, Chick-fil-A, Chipotle, Cava, Chase Autopay) could have been quietly inflating a forecasted Amex payment before it was caught.
  - Today's basic line-diff *would* catch that this line's amount changed (it's just another `(AccountId, Description)` group like any other), but it can't say *why* - that requires diffing the underlying set of contributing charge transactions, not just the rolled-up total.
- **Deferrals and confirmations** - moving a payment's date (`PaymentDeferral`) or manually overriding its amount/date (`PaymentConfirmation`) directly changes the ledger. Real user actions, but also where the real Perimeter/Laura collision bug lived (see `category-vs-scheduled-item-redesign.md`) - both key on `(AccountId, Date)` only, no way to disambiguate two categories sharing an account/day.
- **Partial payments** - reduce a line's remaining amount and create a synthetic `OneTimeEvent` for the cash already paid, which itself later gets excluded once `FindRealPostingFor` locates the matching real posting - a *second*, separate reconciliation-like mechanism running in parallel to `TransactionReconciliationService`, with its own matching window and its own category/account fallback logic.
- **`Dismissed` transactions have zero effect on any forecast number** - confirmed by grep, nothing in `ForecastEngine.cs`, `AmexCycleCalculator.cs`, or `ForecastAccuracyService.cs` checks `BankTransaction.Dismissed`. Whatever that flag means elsewhere in the app, it does not affect the forecast at all today. Not necessarily wrong, just worth knowing precisely.

### B. Apparent changes that are really just time passing

- `asOfDate` advancing shifts which occurrences fall inside the forecast window, which Amex cycle is "current," and which confirmed/excluded rows age out of the 7-day `ExcludedPaymentVisibilityDays` window - all with zero real data changes.
- A subtler wrinkle in the *existing* diff logic itself: `ForecastSnapshotDiffer` pairs same-group lines positionally (Nth occurrence in the old snapshot pairs with Nth in the new). For a monthly bill, as the window rolls forward one occurrence at a time, this can pair "last month's occurrence" against "next month's occurrence" and report a plain date/amount "Changed" - technically true, but not the same kind of signal as an occurrence that actually got rescheduled. Not fixed as part of this plan, just documented as a known source of diff noise.

## Proposed solution

Split into what's straightforward now vs. what's genuinely harder, per the user's agreement to scope it that way.

### Build now (items 1-4)

1. **Per-sync capture, not per-day.** `ForecastSnapshotService.CaptureAsync` stops upserting by `AsOfDate` - every successful sync (scheduled or manual, any source) inserts a new row. A capture already only happens right when something might have changed, so this ties history directly to real events instead of an arbitrary daily clock.
2. **Starting-balance delta in the diff.** `ForecastSnapshotDiffer.Diff` (or `ForecastSnapshotDiff`) gains an explicit starting-balance-changed field, computed from `previous.StartingBalance` vs `current.StartingBalance`, shown independently of the line diff.
3. **Reconciliation-aware line diffing.** Add `CategoryId` to `ForecastSnapshotLine` (tracked internally today via `LedgerLine.CategoryId` but dropped before the result reaches `ForecastResult`/`ForecastSnapshotLine`). When a line is missing from the newer snapshot, check whether a real `BankTransaction` now has `CategoryId` + `ReconciledOccurrenceDate` matching it; if so, report it as "Reconciled: budgeted $X -> actual $Y ($delta)" instead of a bare "Removed."
4. **Pick any two snapshots to compare**, not just "the two most recent." The diff logic itself (`ForecastSnapshotDiffer.Diff`) needs no change to support this - it already operates on any two `ForecastSnapshot` objects. The page/provider needs to let the user choose which two.

TDD throughout, matching this project's default workflow.

**Storage note**: multiplying capture frequency multiplies storage (~100+ lines per snapshot), but at this app's real scale that's trivial for Postgres - not building retention/pruning up front, just noting it as something to watch if it ever becomes real.

### Deferred, separate follow-up: Amex cycle drill-down

Explaining *why* the Amex/ActiveSpending payment line's amount changed requires capturing and diffing the set of charge transactions contributing to that cycle's total, not just the final rolled-up number - a meaningfully bigger design question (what exactly gets captured, how far back, what counts as "explaining" a cycle total). Not scoped as part of items 1-4; revisit once those are built and in real use.
