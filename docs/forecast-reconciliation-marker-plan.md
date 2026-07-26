# Forecast Reconciliation: Durable Marker Instead of a Date-Window Search

Status: **planned, not yet built**. Written so an interrupted/disconnected session can resume without re-deriving the design from conversation history.

## Problem

The Forecast page kept showing a $652 SSA deposit as still-pending for 7/10/2026, even though the real transaction had posted on 7/8 - 16 days earlier. Traced to `ForecastEngine.IsAlreadyReflectedInAnActualTransaction`: it decides "has this occurrence already posted" by searching for a matching real transaction within a date window computed from `asOfDate` (today) - `RecurrenceExpander.MaxMatchWindowDays` (14 days). Both the ledger's own backward-widening (so a recently-due occurrence doesn't vanish before it's confirmed) and the per-occurrence match window are each capped at 14 days, so the true worst case is 28 days back from today - but the code only widened by one 14-day radius, not two, so a transaction posted right at that edge (2 days before the boundary) was silently excluded from the reconciliation candidates before the matching logic even ran. Confirmed live: the real checking balance already reflected the 7/8 deposit, and the forecast was also still counting it as a future line - a genuine double-count, not a display quirk.

Widening the search window (even precisely, to the mathematically complete 28-day bound) was considered and rejected: it still leaves an unresolved case (a transaction posted more than 28 days before today, for an occurrence that itself is old enough to still matter) with no way to durably mark it as posted - the same "how wide is wide enough" question just resurfaces at a different boundary. The user's original spreadsheet model avoided this entirely with a per-item 'X' column: mark an item posted once, and it stays excluded from every future projection, permanently, with no re-derivation. The app already has an automatic-and-manual version of that same idea in two different places - `PaymentConfirmation` (manual "Confirm paid" click, works today, unaffected by this bug) and the broken automatic path this plan replaces. The fix: give the automatic path the same durability the manual one already has.

## Design

**New column** (`src/Expense.Domain/Entities/BankTransaction.cs`):
```csharp
/// <summary>
/// Which forecasted occurrence this transaction satisfies (e.g. the 7/10/2026 SSA
/// deposit) - set once by TransactionReconciliationService, checked by ForecastEngine
/// as a direct lookup instead of a date-window search computed relative to "today".
/// Null until classified, or if this transaction's category has no determinable
/// recurring schedule (most spending categories).
/// </summary>
public DateOnly? ReconciledOccurrenceDate { get; set; }
```
Nullable, additive, no changes to any existing column. New migration (`AddReconciledOccurrenceDateToBankTransaction`), applied to `expense_test` automatically and to production the same way as every other migration this session.

**New service**: `TransactionReconciliationService` (`src/Expense.Domain/Services/Forecast/TransactionReconciliationService.cs`), one method:
```csharp
Task ReconcileAsync(ExpenseDbContext context, DateOnly asOfDate, CancellationToken cancellationToken = default)
```
For every `BankTransaction` with `CategoryId != null && PostedDate != null`, whose category is either Direct-funded or an `AccountPayment`-strategy category (covers both debt-account payments and Amex payment categories - same two groups `ForecastEngine`/`ForecastAccuracyService` already handle), find the nearest occurrence in that category's full recurring schedule and set `ReconciledOccurrenceDate` to its date. "Full schedule" means built from **every historical `BudgetPeriod` version** overlapping the transaction's own posted date (the same fix just applied to `ForecastAccuracyService` - reuse that construction rather than re-implementing it a third time, extracting a shared helper if that's cleaner once both call sites are in view), not just the category's current budget - a transaction from 6 months ago must be judged against whatever was actually scheduled back then. Critically, there is **no "how many days back from today" bound at all** here - the search runs over the category's real history (from its earliest `BudgetPeriod.EffectiveFrom` forward), because classification depends only on the transaction's own fixed `PostedDate`, never on today's date. That's what makes this permanent instead of a bigger version of the same window bug.

Re-derive `ReconciledOccurrenceDate` for **every** qualifying transaction each time this runs, not just ones that are currently null - cheap at this app's real data scale (a few hundred transactions total), and it means a transaction that gets manually recategorized later (there are 7+ separate call sites across `CategorizationService`/`TransactionManagementService` that can change `CategoryId` - see below) self-corrects on the next run instead of requiring every one of those call sites to remember to reset the marker. Once computed for a given transaction, the answer never changes on its own (it depends only on that transaction's own `PostedDate` and the category's historical schedule, not on "today"), so re-running this regularly is just picking up new transactions and any recategorizations - not redoing work that could have been done once and cached correctly forever.

Amount is deliberately **not** part of this classification - `ReconciledOccurrenceDate` just answers "which occurrence does this transaction correspond to by timing," a fact that doesn't depend on amount at all. The existing amount-tolerance check (real amount must be at least 95% of the configured amount, no upper bound) stays exactly as it is today, applied at `ForecastEngine` read time.

**Simplified `ForecastEngine.IsAlreadyReflectedInAnActualTransaction`**: replace the windowed search entirely with a direct lookup - does any transaction exist with `CategoryId == line.CategoryId && ReconciledOccurrenceDate == line.Date`, and is its amount within tolerance? No `reconciliationTransactions` prefetch, no `asOfDate`-relative bound anywhere in this check. `ForecastEngineTests.cs`'s existing reconciliation tests need updating to seed `ReconciledOccurrenceDate` directly on the test transaction rather than relying on `PostedDate` falling in some window - this actually simplifies those tests too, since they no longer need to reason about window edges at all. The `DirectFundedBill_PostedEarly_...StillReconciles` test added this session (proving the original bug) should be rewritten the same way and kept as the regression test for this fix.

**Wiring**: call `TransactionReconciliationService.ReconcileAsync` from `ForecastResultProvider.GetForecastAsync`, right before `ForecastEngine.GenerateAsync` - cheap and idempotent at this app's scale, so no need to gate it behind the sync cadence. This one chokepoint already covers every case for free: `SyncStatusProvider.CaptureForecastSnapshotAsync` (called after both manual "Sync Now" clicks and every scheduled `SyncScheduler` run) already calls `GetForecastAsync` to capture a snapshot, and the Forecast/Dashboard pages call it directly on every render - so reconciliation runs on every sync *and* every page view without a separate `SyncScheduler` change or a third `BackgroundService`.

**Backfill**: every transaction that exists in the database today needs to be classified once, since none have ever had this column populated. Run `ReconcileAsync` in a dry-run/report mode first (log what it *would* assign, per transaction, without writing anything) and review that output before committing - the historical `BudgetPeriod`-versioning surprises found earlier this session (Perimeter's retroactive budget edit) are exactly the kind of thing that could produce a wrong-looking classification on real historical data, and this is the one step touching a lot of existing data at once. Once the output looks right, run it for real (a small one-off script or an optional parameter on `ReconcileAsync`, decide during implementation).

**Explicitly out of scope for this pass**: `ForecastAccuracyService` keeps its own separate window-based matching - it's a reporting tool that wants to surface *any* plausible actual transaction even when the amount differs meaningfully (that's the whole point of the accuracy report), which is a different need than `ForecastEngine`'s binary "should this line still show" exclusion. It could be simplified later to read `ReconciledOccurrenceDate` too for consistency, but nothing about it is broken today, so that's a deferred cleanup, not part of this plan.

## Tests (TDD throughout, as always)

- `BankTransaction` round-trip test for the new column (mirrors existing entity tests).
- `TransactionReconciliationServiceTests`:
  - A transaction posted early classifies to the correct occurrence, regardless of how many days before "today" it posted (use a gap well past 28 days to prove there's no hidden ceiling - this is the direct regression test replacing the window-based one).
  - A transaction is classified against the `BudgetPeriod` version that was actually in effect on its own posted date, not the category's current budget (same scenario as the Perimeter fix, reused here).
  - Re-running `ReconcileAsync` after a transaction's `CategoryId` changes updates `ReconciledOccurrenceDate` to match the new category (proves self-correction without needing to touch every recategorization call site).
  - A transaction whose category has no determinable schedule (no qualifying `FundingRule`) is left with `ReconciledOccurrenceDate == null`, no error.
- `ForecastEngineTests.cs`: update existing reconciliation tests to seed `ReconciledOccurrenceDate` directly; keep (rewritten) the `DirectFundedBill_PostedEarly_...` test as the regression case.
- Full `dotnet test` after each part, not just once at the end.

## Verification

- New migration applied to `expense_test` (automatic) and, after backfill review, to production.
- Backfill dry-run output reviewed by hand against a few real, known categories (SSA, GPC, Perimeter) before committing for real.
- After deploying: confirm via direct `psql` query that the SSA transaction from 7/8 now has `ReconciledOccurrenceDate = 2026-07-10`, and that the live Forecast page no longer shows the 7/10 SSA line.
- `dotnet publish` + `systemctl --user restart expense`, then visually verify the Forecast page via headless Chrome against real data.
