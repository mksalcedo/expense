# Forecast Accuracy: Estimates vs. Actual Report + Daily Snapshot/Diff

Status: **built and deployed**. Part 1 (Estimates vs. Actual, `/forecast-accuracy`) verified against real production data. Part 2 (daily snapshot capture + `/forecast-history` diff view) deployed and migration applied to production; the capture is wired into `SyncScheduler`'s existing 6am/3pm cadence, so the first real snapshot lands at the next scheduled run and a meaningful diff appears after the one after that - not independently verifiable before then. Written so an interrupted/disconnected session can resume without re-deriving the design from conversation history.

## Problem

The user observed the forecast's lowest-projected-balance figure swing meaningfully over a few days (~$200 → ~$400 → ~$1,100) and couldn't reconcile why - not because the math is necessarily wrong, but because there's no way to see *what changed*. Checked the real database directly for anything that could explain it (recent `PaymentConfirmation`/`PartialPayment`/`PaymentDeferral` rows) - found nothing conclusive; the one relevant partial payment had already been resolved before this week's swings began. That itself is the real finding: the forecast is deliberately never persisted (`ForecastEngine.GenerateAsync` always computes "as of today" fresh), so there's no historical state to diff against, even by querying the database directly after the fact.

Also worth noting directly: a session-notes line from 2026-07-19 flagged that `OneTimeEvent` having no `CategoryId` "would be a real gap for any future 'did my forecast match reality' report" - a real prior signal that this was needed, never turned into a tracked task or built. Not repeating that miss this time.

**User's framing, which should drive the design**: if forecasting is done correctly, the lowest projected balance shouldn't fluctuate much day to day. When it does, something posted meaningfully differently than assumed - that's a signal worth surfacing prominently, not background noise to shrug off.

Two complementary pieces, in order of how soon they help:

## Part 1: Estimates vs. Actual report (no new persistence needed - explains recent history right now)

For every recurring obligation that's already posted as a real transaction in the last ~60-90 days, compare what was assumed against what actually happened, surfacing anything that differs meaningfully.

**Confirmed from reading `ForecastEngine.cs`/`AmexCycleCalculator.cs` directly** (not guessed):
- Direct-funded bills/income: assumed amount is `BudgetPeriod.Amount` (via the `RecurringRule`s `ForecastEngine` already builds from `directPeriods`, `FundingRule.Strategy == Direct`). Actual: a `BankTransaction` with matching `CategoryId`.
- Debt-account payments (non-Amex): assumed is `Account.MinPayment + Account.ExtraPayment` (same synthetic-`RecurringRule` construction `ForecastEngine` already does). Actual: a `BankTransaction` tagged with that account's `FundingRule.Strategy == AccountPayment` category (via `accountPaymentCategoryIds`).
- Amex/`ActiveSpending` accounts: `AmexCycleCalculator.CalculateDuePayments` already returns, per cycle, both `ActualAmount` (real charges total for that cycle) and enough to recompute the budget-only figure (`monthlyBudgetTotal + extraPrincipal`) - no new calculation needed, just call it for a past window and read both sides of `AmexCycleResult` for any `!IsFuture` cycle.
- Matching real transactions to a scheduled occurrence: reuse the exact same `CategoryId` + `RecurrenceExpander.MatchWindowDaysFor(frequency)` window logic already in `ForecastEngine.IsAlreadyReflectedInAnActualTransaction` - except **capture the matched transaction's real amount/date** for comparison instead of just returning true/false and discarding it (which is what the existing method does today - the real amount is never retained anywhere once a line is excluded).

**New domain service**: `ForecastAccuracyService` (`src/Expense.Domain/Services/Forecast/ForecastAccuracyService.cs`), one method:
```csharp
Task<List<AccuracyComparison>> GetRecentAccuracyAsync(ExpenseDbContext context, DateOnly asOfDate, int lookbackDays = 90, CancellationToken cancellationToken = default)
```
`AccuracyComparison`: `Name, ScheduledDate, ScheduledAmount, ActualDate, ActualAmount, AccountId` (computed `Delta`/`DeltaPercent` as properties, same pattern as `ForecastResult.LowestProjectedBalance` being a computed property rather than stored). Also include occurrences that never found a match at all (a real miss - genuinely didn't post, worth its own flag, separate from "posted differently").

**New page**: `/forecast-accuracy` (or a new tab on Historical Analysis - decide based on how it feels once built) - one table, sorted by `|Delta|` descending, highlighting rows above a threshold (e.g. >10% or >$100) so real anomalies aren't buried among routine $0.01 rounding differences.

**Tests (TDD)**: new `tests/Expense.Domain.Tests/Services/Forecast/ForecastAccuracyServiceTests.cs` using `DatabaseTestBase` (matches `ForecastEngineTests.cs`'s own convention) - a Direct-funded bill posting at a different real amount, a debt-account payment posting on time at the configured amount (no flag), an Amex cycle whose real charges exceeded budget, and an occurrence with no matching transaction at all (flagged as a miss, not a delta).

## Part 2: Daily forecast snapshot + day-over-day diff (closes the gap going forward)

**New entities** (`src/Expense.Domain/Entities/`):
```csharp
public class ForecastSnapshot
{
    public int Id { get; set; }
    public DateOnly AsOfDate { get; set; }  // one row per calendar day, upserted
    public decimal StartingBalance { get; set; }
    public decimal LowestProjectedBalance { get; set; }
    public DateOnly? LowestProjectedBalanceDate { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public List<ForecastSnapshotLine> Lines { get; set; } = [];
}

public class ForecastSnapshotLine
{
    public int Id { get; set; }
    public int ForecastSnapshotId { get; set; }
    public ForecastSnapshot ForecastSnapshot { get; set; } = null!;
    public DateOnly Date { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public decimal RunningBalance { get; set; }
    public int AccountId { get; set; }
}
```
Only persist lines within a near-term window (e.g. next 120 days from `AsOfDate`) - the far tail of the 12-month forecast is both less actionable and would bloat storage for no real benefit. New migration, EF configuration (unique index on `AsOfDate`, cascade delete for lines - same pattern as `ImportRunProgressLine`).

**Capture mechanism**: add a step to the existing `SyncScheduler.RunScheduledSyncAsync` (`src/Expense.Domain/Services/Scheduling/SyncScheduler.cs`) - after both syncs run, resolve `IForecastResultProvider` from the same scope, call `GetForecastAsync()`, and upsert (replace, don't append) today's `ForecastSnapshot`. This reuses the already-running twice-daily cadence rather than adding a second `BackgroundService` - a snapshot taken right after fresh sync data lands is exactly the right moment, and upserting by `AsOfDate` means the second daily run just refines the same day's snapshot rather than creating a duplicate.

**Diff view**: new section (Dashboard or Forecast page) showing:
- A trend chart of `LowestProjectedBalance` over the last N days (reuse the existing `CashFlowChart`/`CashFlowChartBuilder` SVG pattern - same shape of problem, already solved once this session).
- "What changed since yesterday": diff the two most recent `ForecastSnapshot`s' `Lines`, matched by `(AccountId, Date, Description)` - report lines whose `Amount` differs, lines present in yesterday's but missing today's (resolved/deferred/dropped off), and lines newly appearing today. This is what directly answers "why did the lowest balance move" going forward, without needing to reverse-engineer it from raw tables the way this conversation just had to.

**Tests (TDD)**: entity round-trip test (mirrors `ImportRunTests.cs`), a diff-computation test (pure function, easy to unit test with two hand-built snapshots), `SyncScheduler` itself stays untested per its own established convention (composition-root glue, see its doc comment) - only the diff logic underneath needs coverage.

## Verification

- `dotnet test` after each part.
- New migration applied to both `expense_test` (automatic) and the real production DB (`dotnet ef database update ... --connection "<prod connection string>"`, same pattern as this session's other migrations).
- For Part 1: run the new report against the real database and manually cross-check a couple of known real discrepancies (e.g. an account whose real payment amount is known to differ from its configured min+extra) to confirm the numbers match reality, not just pass synthetic tests.
- For Part 2: since it only starts working from when it's deployed, verify by watching it capture at least two real daily snapshots (the next two scheduled sync times) and confirming the diff view renders sensibly against real data, not just a same-day no-op diff.
- `dotnet publish` + `systemctl --user restart expense`, then visually verify both new pages/sections via headless Chrome against real data, same pattern as every other verification this session.
