# Amex Pending Charges — Implementation Plan

Status: **approved, not yet built**. Written so an interrupted/disconnected session can resume without re-deriving the design from conversation history.

## Problem

Amex's own website shows pending charges (and very recent posted ones) days before SimpleFin's feed has them. Confirmed by directly inspecting SimpleFin's raw API response (not guessed): the Amex account's transaction list simply stops a couple of days short of what's visible on amex.com, and no `pending` field is present anywhere in the payload for any connected account, even when the SimpleFin `pending=1` query parameter is explicitly requested. This is a real limitation of this SimpleFin bridge connection, not a bug in this app's sync code, and not something fixable by changing how we call SimpleFin.

A screen-scraping utility to pull pending charges directly from amex.com was considered and rejected: it would require defeating MFA/bot-detection on every run, would be fragile against Amex changing their site, and — most importantly — automated scraping of a bank's site risks tripping their fraud detection and having the *real* account flagged/locked, a much worse outcome than a few days of lag.

## Chosen approach: user-driven screenshot import

The user occasionally (not routinely) screenshots their own Amex "recent activity" view (already logged in normally, no automation involved) and feeds it to this feature. Claude's API (vision-capable model) extracts the transaction rows; the user reviews and confirms before anything is added. This is materially different from scraping — no automated access to Amex's systems occurs at any point; it's the same in kind as manually typing numbers off a screenshot into a spreadsheet, just with OCR assistance.

## Key design decision: don't reuse `PostedDate` for a meaning it was never given

The instinct to fake a `PostedDate` for a manually-entered charge (so it would count toward the Amex forecast) was **rejected** — `PostedDate == null` means "not yet posted" everywhere else in this app (`ForecastEngine`'s reconciliation windows, the Amex actual-charges query at `ForecastEngine.cs:136`, etc.), and overloading it would corrupt that meaning for anything that assumes it going forward.

Instead:
- `TransactionDate` = the date shown in the screenshot (existing field, already the right one for this).
- `PostedDate` = stays genuinely `null` — this charge is still, truthfully, not posted.
- `CreatedAt` = already captures "when this was entered" — no new field needed for that.
- `ImportSource = "ManualScreenshot"` — a new literal value for the existing `ImportSource` string column, marking these rows as distinct from `"SimpleFin"`/`"Test"` ones. No schema/migration change needed for any of this — every field involved already exists.

This only matters for **pay-in-full/ActiveSpending accounts** (currently just Amex) — debt accounts (Chase, Discover, etc.) use a fixed min+extra payment, not a charges-summed total, so there's nothing for a "pending charges you've seen" adjustment to plug into there. That's not a decision to make later; it falls out of how `AmexCycleCalculator` already works.

## Forecast change: blended into the same cycle, not a separate additive line (superseded 2026-07-22)

**Original design** (below) kept a separate always-additive "Amex Payment (pending, self-reported)" line. **Revised** after real usage revealed two problems: (1) it was attached to whichever cycle was soonest-due (the already-closed one), not the currently-open cycle these charges actually belong to; (2) more fundamentally, the whole point of this feature is to catch a looming *budget overage* before charges post - a separate additive line risks double-counting against the flat monthly budget figure instead of correctly flagging only the amount by which real spending (posted + pending) exceeds it.

**Current design:** `AmexCycleCalculator` treats a still-unposted, self-reported charge exactly like a posted one for the purpose of the existing `MAX(actual, budget) + extra principal` calculation - bucketed into whichever cycle's `[CycleStart, CycleEnd]` window contains it, using `PostedDate ?? TransactionDate` as its effective date. `AmexCycleResult.PendingSelfReportedAmount` reports how much of that cycle's `ActualAmount` came from still-pending charges, so `ForecastEngine` can annotate the single "Amex Payment" line's description (e.g. "Amex Payment (includes $131.65 pending, not yet posted)") without adding a second dollar-bearing row. This means an overage becomes visible in the forecast the moment enough pending charges are entered to exceed budget - not just once they post - while charges still under budget don't distort the total, only the description.

~~Original: mirrors Amex's own UI, which separates "Pending Charges" from "Posted Charges" rather than blending them - a second ledger line landing on the same due date as the real cycle's payment line, both feeding the running balance but visually distinct.~~

## De-dup — both directions

1. **Before adding** (in the review table): each freshly-extracted row is checked against existing `BankTransaction`s (both normally-synced ones and previously-added `ManualScreenshot` ones still unmatched) using: same `AccountId`, exact `Amount` match, transaction date within a few days. A match means "skip this one, it's already tracked."
2. **After adding, at every future SimpleFin sync**: newly-synced transactions are checked against any still-open `ManualScreenshot` placeholders using the same match (account + exact amount + date window). A match means: delete the placeholder, let the real (properly `ExternalId`-tagged, correctly `PostedDate`-set, normally-categorized) transaction stand as the authoritative record. This must be **visible**, not silent — reported in the SimpleFin sync's existing summary line (e.g. "...; removed 1 manually-entered charge now confirmed posted").

**Known residual risk, accepted deliberately, not silently**: a manually-entered charge that never actually posts (e.g. a voided authorization) would sit unmatched forever, permanently counted in the forecast unless removed. Mitigation: a small "Manually Entered Pending Charges" list (same spirit as the Confirmed Payments page) so the user can see and delete a stale one themselves. Not auto-expired — that would be exactly the kind of silent behavior this app has repeatedly moved away from.

## Review-table UX

Every row Claude extracts from the screenshot is shown — nothing is silently filtered, matching the same transparency bar set by the Amazon sync progress modal.

- **New rows**: editable (date/description/amount, in case OCR misreads something), included in "Add All" by default.
- **Rows matching an existing transaction**: shown grayed-out/struck-through in the same table (same visual treatment as excluded Forecast rows), with the specific matched record named (e.g. *"Already in system — matches INGLES MARKETS #474, -$171.95, posted 07/18/2026"*), excluded from "Add All" by default, but with an "Add anyway" override in case the heuristic match is wrong.
- A plain new "new row" can be individually "Ignore"-d too.
- **One commit action**: "Add All" — no separate per-row "Add" button (redundant with Add All for this single-sitting review workflow; per-row action is "Ignore" or "Add anyway", never a duplicate way to do the same commit).

## Credentials

`Anthropic:ApiKey` is already set via `dotnet user-secrets` for `Expense.Web` (pulled from the user's existing key at `~/dev/mta/src/.env`, used for an unrelated jigsaw-puzzle-game project — same key, not yet a dedicated one; usage/billing for both apps will show up commingled in the Anthropic console, which the user has accepted).

## Implementation checklist

- [x] Domain: a service to call the Anthropic API with an uploaded/pasted image, prompted to return structured JSON (date, description, amount per row).
- [x] Domain: matching/de-dup logic (account + exact amount + date-window) shared by both the pre-add check and the post-sync reconciliation step.
- [x] Domain: `SimpleFinSyncService` (or wherever the sync summary is built) gets the new "remove matched placeholders" step + summary text.
- [x] Domain: `ForecastEngine` gets the new "Amex Payment (pending, self-reported)" ledger line for `ActiveSpending` accounts, sourced from open `ManualScreenshot` charges.
- [x] Web: new page (`/add-pending-charges`) — screenshot upload/paste, calls the parsing service, renders the review table (new-row/matched-row states, Add All, per-row Ignore/Add-anyway), commits accepted rows as real `BankTransaction`s through normal merchant-rule categorization.
- [x] Web: small "Manually Entered Pending Charges" list/page (`/pending-charges`) for reviewing and deleting stale unmatched placeholders.
- [x] Tests throughout, TDD as usual for this project - domain first, then web layer with a fake provider.
- [x] Visual verification in a browser before calling it done (this is a new page + new Forecast row, both UI-visible).
