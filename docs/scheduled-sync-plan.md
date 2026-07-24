# Scheduled Sync, Persisted Run History, and Failure Notifications — Implementation Plan

Status: **built**. Written so an interrupted/disconnected session can resume without re-deriving the design from conversation history.

**Update after initial build**: the desktop notifier (`notify-send`, section 4) was built, tried, and rejected — desktop notifications are too easy to miss away from the desk. It was removed entirely (no `ISyncFailureNotifier`/`DesktopSyncFailureNotifier`, no notification call in `SyncScheduler`). In its place: the NavMenu's "Sync Now" link now shows a durable in-app failure indicator (e.g. "Sync Now (1 sync failed)"), visible from every page, refreshed the same way the Review Queue count already is - reusing the existing `GetLastSimpleFinRunAsync`/`GetLastAmazonGmailRunAsync` methods, no new backend surface needed. The Dashboard banner (section 5) stayed as a secondary, more detailed surface.

## Problem

Sync currently only happens when someone clicks a button on the Sync Now page. The goal is for both SimpleFin (bank) and Amazon Gmail sync to also run automatically twice a day (~6:00 AM and ~3:00 PM), while keeping manual sync as-is. Two things fall out of "unattended, twice-daily sync":

1. **Errors need to reach the user without them checking the page** — today a failure is only visible if you're looking at Sync Now when it happens (manual sync) or happen to open the page afterward.
2. **The rich per-message detail Amazon sync already streams into a live modal is thrown away** the moment that modal closes or the page unloads — for a run nobody was watching, that detail is gone forever. It needs to be persisted and browsable later. SimpleFin's equivalent detail is explicitly **not** being built now ("I am going to be asking for similar detail for SimpleFin sync also, but haven't yet") — the persistence mechanism just needs to be source-agnostic so that's a drop-in later, not a schema change.

The app already runs continuously as a systemd user service (`expense.service`) with auto-restart, which makes an in-process scheduled background task the natural fit — no separate systemd timer or cron entry needed, and it reuses the exact same `ISyncStatusProvider` methods the manual buttons already call, so scheduled and manual runs behave identically and share the same `ImportRun` tracking.

## Current architecture (confirmed by reading the code)

- `ImportRun` (`src/Expense.Domain/Entities/ImportRun.cs`) — one row per completed sync attempt, `Source` (`SimpleFin`/`AmazonGmail`), `Success`, `Summary`/`ErrorMessage` (both `varchar(2000)` — too small for a full transcript). Table `import_runs`.
- `AmazonGmailSyncService.RunAsync` (`src/Expense.Domain/Services/Ingestion/Amazon/AmazonGmailSyncService.cs`) — the *only* place that both creates the `ImportRun` row and calls `context.SaveChangesAsync` for an Amazon run, on both success and failure paths (single outer try/catch, deliberately broad). It already takes `Action<SyncProgressLine>? onProgress`, invoked directly (not via `IProgress<T>`) at ~7 call sites, including inside the `private static RecordParseFailureAsync` helper. This is the single right place to also persist the log, so every caller (manual Blazor button, console importer, future scheduler) gets it for free.
- `SimpleFinSyncService.RunAsync` has **no** progress callback today — out of scope, per the explicit "not yet."
- `ISyncStatusProvider`/`SyncStatusProvider` (`src/Expense.Domain/Services/Dashboard/`) — thin composition root (own doc comment: "deliberately not unit-tested"), scoped in DI, uses `IDbContextFactory<ExpenseDbContext>` (not a scoped `DbContext` directly) — the pattern a hosted service must also use, since a `BackgroundService` is a singleton.
- No `BackgroundService`/`IHostedService` exists anywhere in this repo yet — this is new territory, not an extension of an existing pattern.
- No notification abstraction exists (`notify-send`, `Process.Start`, etc.) — also new.
- `AppSettings` (`src/Expense.Domain/Settings/AppSettings.cs`, bound via `IOptions<AppSettings>`) is the established place for config that would otherwise be a hardcoded constant — same rationale applies to schedule times.
- Tests: Domain-layer service tests use `DatabaseTestBase` (real Postgres `expense_test` DB, per-test transaction rollback, migrations applied once via `DatabaseFixture`). Web-layer tests use bUnit with hand-written fakes per test file (e.g. `FakeSyncStatusProvider` in `SyncNowTests.cs`, `FakeForecastResultProvider`/etc. in `DashboardTests.cs`) — no mocking library.
- `dotnet-ef` is available via the repo's local tool manifest (`dotnet tool restore`). Migrations are added via `dotnet ef migrations add <Name> --project src/Expense.Domain --startup-project src/Expense.Domain`. The real production DB (`Host=localhost;Port=5433;Database=expense`, distinct from `expense_test`) is currently fully migrated up to `20260720165816_AddPartialPayments` — applying a new migration to it will need `dotnet ef database update ... --connection "<prod connection string>"` since `DesignTimeDbContextFactory` hardcodes the test DB.

## Implementation plan (TDD: failing test → implement → green, at each step)

### 1. Persist the per-run progress log

- New entity `src/Expense.Domain/Entities/ImportRunProgressLine.cs`: `Id`, `ImportRunId`, `ImportRun` nav, `Sequence` (int, preserves order), `Text` (unbounded — no `HasMaxLength`, so Npgsql maps it to `text`, unlike `Summary`/`ErrorMessage`), `IsError`.
- `ImportRun` gets a new `public List<ImportRunProgressLine> ProgressLines { get; set; } = [];` nav collection — generic on purpose, so SimpleFin can reuse it later without a schema change.
- New `ImportRunProgressLineConfiguration` (mirrors `ImportRunConfiguration`'s style): table `import_run_progress_lines`, FK cascade delete, index on `(ImportRunId, Sequence)`.
- Add `DbSet<ImportRunProgressLine> ImportRunProgressLines` to `ExpenseDbContext`.
- Test first: new `tests/Expense.Domain.Tests/Entities/ImportRunProgressLineTests.cs` (mirrors `ImportRunTests.cs`'s style) — save/reload a run with lines in order; deleting the parent `ImportRun` cascades.
- Generate migration: `dotnet ef migrations add AddImportRunProgressLines --project src/Expense.Domain --startup-project src/Expense.Domain`.
- **In `AmazonGmailSyncService.RunAsync`**: introduce a local `List<ImportRunProgressLine> persistedLines` and a local `void Emit(SyncProgressLine line)` that both calls `onProgress?.Invoke(line)` and appends to `persistedLines` (with the running `Sequence`). Replace every direct `onProgress?.Invoke(...)` call — including inside `RecordParseFailureAsync`, whose `Action<SyncProgressLine>? onProgress` parameter becomes a required `Action<SyncProgressLine> emit` — with `Emit(...)`. Right before `context.ImportRuns.Add(run)`, set `run.ProgressLines = persistedLines;`. This captures the log on **both** success and failure paths, since it happens after the existing try/catch, unconditionally.
- Tests first, added to the existing `tests/Expense.Domain.Tests/Services/Ingestion/Amazon/AmazonGmailSyncServiceTests.cs` (matches its established `lines.Add` / reload-and-assert style):
  - `RunAsync_PersistsEveryEmittedProgressLine_InOrder` — run with an order + a refund message, reload `result.Run.ProgressLines` from a fresh context, assert same text/order/IsError as an `onProgress` capture of the same run.
  - `RunAsync_OnFailure_StillPersistsWhateverProgressLinesWereEmittedBeforeTheFailure` — a small throwing `IGmailMessageSource` fake (`SearchAsync` throws), assert whatever lines were already emitted before the throw are on the reloaded run.

### 2. Extend `ISyncStatusProvider` to expose run history + persisted detail

- Add to the interface:
  ```csharp
  Task<List<ImportRun>> GetRecentRunsAsync(ImportSource source, int count, CancellationToken cancellationToken = default);
  Task<List<SyncProgressLine>> GetRunProgressLogAsync(int importRunId, CancellationToken cancellationToken = default);
  ```
- Implement in `SyncStatusProvider` (thin pass-throughs, consistent with the rest of the class — no direct unit test, per its own "deliberately not unit-tested" doc comment; exercised via the Web-layer fake instead, same as every other method on this interface today).
- Update `FakeSyncStatusProvider` in `tests/Expense.Web.Tests/Pages/SyncNowTests.cs` (and the equivalent Dashboard fake, see below) to implement the two new methods.

### 3. Sync Now page: browsable run history

- Add a "Recent runs" section per source (last 10, newest first) to `SyncNow.razor`: date/time, success/fail, `Summary`, and a "View details" button per row.
- Add one shared detail modal (distinct from the existing *live* Amazon sync modal, which is unchanged) that opens with a given run's persisted `SyncProgressLine`s, rendered via the same header/body/result card markup the live modal already uses — factor that per-line rendering out of the existing inline `@foreach` into a small shared `RenderFragment`-returning method on the component (same pattern `Dashboard.razor` already uses for `RenderSpendingTable`), so it's not duplicated between the live modal and the history detail modal.
- Tests first, added to `tests/Expense.Web.Tests/Pages/SyncNowTests.cs`:
  - `RecentRunsSection_ListsPastRunsForEachSource_NewestFirst`
  - `RecentRunsSection_ShowsSuccessAndFailureStatusPerRun`
  - `ClickingViewDetails_OpensTheDetailModal_WithThatRunsPersistedProgressLog`
  - `ClosingTheHistoryDetailModal_HidesIt`

### 4. Scheduled background sync

- New `src/Expense.Domain/Services/Scheduling/` folder (separate from `Services/Dashboard`, since this isn't page-provider glue):
  - `ScheduledSyncTimeCalculator` — pure static method `GetNextRunTime(DateTimeOffset now, IReadOnlyList<TimeOnly> dailyTimesLocal)`, returns the soonest candidate strictly after `now` (rolling to the earliest time tomorrow if `now` is after every time today), agnostic of time zone (operates on `now`'s own offset — the caller passes real local time, so no `TimeZoneInfo` handling needed for a single-laptop personal app).
  - `ISyncFailureNotifier` / `DesktopSyncFailureNotifier` — `Task NotifyAsync(ImportSource source, string errorMessage, CancellationToken)`. Internally: a pure `internal static (string Title, string Body) BuildNotification(ImportSource, string)` (unit-testable), then a best-effort `Process.Start` of `notify-send` using `ProcessStartInfo.ArgumentList` (not a concatenated command string — avoids any injection risk from an error message's contents) wrapped in try/catch so a notification failure can never break the scheduler.
  - `SyncScheduler : BackgroundService` — resolves `IServiceScopeFactory`, `IOptions<AppSettings>`, `ISyncFailureNotifier`. Loop: compute next run time, `Task.Delay` to it, then run SimpleFin then Amazon sync via a fresh DI scope's `ISyncStatusProvider` (exact same calls the manual buttons make), notifying on any run whose `Success` is false or that throws. Whole iteration wrapped in try/catch so one bad cycle doesn't kill the loop. Like `SyncStatusProvider`, this is composition-root glue and is **not** directly unit tested (there's no reasonable way to unit test a real background timer loop) — only the pure `ScheduledSyncTimeCalculator` and `DesktopSyncFailureNotifier.BuildNotification` underneath it are.
- Add `ScheduledSyncTimesLocal` (`List<string>`, default `["06:00", "15:00"]`) to `AppSettings`, and an explicit entry in `appsettings.json` for discoverability (matches how `ForecastHorizonMonths` is already surfaced there).
- Tests first, new files:
  - `tests/Expense.Domain.Tests/Services/Scheduling/ScheduledSyncTimeCalculatorTests.cs` — before-earliest-time-today, between-times-today, after-last-time-today (rolls to tomorrow), unsorted input times, exactly-at-a-scheduled-instant (treated as already passed, so it doesn't refire in a tight loop).
  - `tests/Expense.Domain.Tests/Services/Scheduling/DesktopSyncFailureNotifierTests.cs` — `BuildNotification` content for each `ImportSource`, confirms the error message ends up in the body.
- Wire up in `Program.cs`: `builder.Services.AddSingleton<ISyncFailureNotifier, DesktopSyncFailureNotifier>();` and `builder.Services.AddHostedService<SyncScheduler>();`.

### 5. Dashboard failure banner

- Re-inject `ISyncStatusProvider` into `Dashboard.razor` (removed earlier this session along with the rest of the sync UI — this is a narrow read-only addition, not bringing back the sync buttons/modal/issues UI, which stays on Sync Now).
- In `RefreshDashboardDataAsync`, also fetch `GetLastSimpleFinRunAsync`/`GetLastAmazonGmailRunAsync`. If either's `Success` is false, render a banner above the Cash Flow section naming which source failed, its `ErrorMessage`, and a link to `/sync-now`.
- Tests first, added to `tests/Expense.Web.Tests/Pages/DashboardTests.cs` (needs a small `FakeSyncStatusProvider` added there too, implementing the full interface like the existing fakes in that file — only `GetLast*RunAsync` need real behavior for these tests):
  - `Dashboard_ShowsAFailureBanner_WhenTheLastSimpleFinSyncFailed`
  - `Dashboard_ShowsAFailureBanner_WhenTheLastAmazonSyncFailed`
  - `Dashboard_ShowsNoFailureBanner_WhenBothLastSyncsSucceededOrNeverRan`

### 6. Migrate the real database and verify end-to-end

- `dotnet ef database update --project src/Expense.Domain --startup-project src/Expense.Domain --connection "Host=localhost;Port=5433;Database=expense;Username=expense;Password=expense_dev_local_only"` — additive-only (new table), applied to the same live production DB the earlier `SyncIssues`/`PartialPayments` migrations already went into this session.
- `dotnet build` and full `dotnet test` after each numbered step, not just at the end.
- `dotnet publish src/Expense.Web -c Release -o publish` then `systemctl --user restart expense`.
- Manual verification: confirm `notify-send` actually reaches the desktop from inside the systemd user service context (this is genuinely new — `expense.service` sets no `DISPLAY`/`DBUS_SESSION_BUS_ADDRESS`, which most desktop environments import into the user's systemd session automatically, but it's unconfirmed here). Concretely: temporarily call the notifier once (e.g. a short-lived manual trigger, not waiting for 6am/3pm) and confirm a real desktop notification appears; if it doesn't, the fix is almost certainly adding `Environment=DISPLAY=... DBUS_SESSION_BUS_ADDRESS=...` to `expense.service` (values read from the active session via `systemctl --user show-environment`) — decide with the user at that point rather than guessing blind.
- Visually check the Sync Now page's new run history/detail modal and the Dashboard banner via headless Chrome against real data, same pattern as prior verifications this session.
