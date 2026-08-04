# Tech Debt

Known gaps that are intentionally not fixed yet - not urgent, but worth not forgetting. Add an entry whenever something like this turns up; each entry should stand alone (what it is, why it matters, current status) rather than assume the reader has the original conversation's context.

## Unmapped-account sync failures are invisible in scheduled runs (found 2026-07-27)

**What**: `simplefin-account-map.json` / `plaid-account-map.json` map each source's own account ID to this app's internal account ID. If a source (SimpleFin or Plaid) reports an account ID that isn't a key in the relevant map file, that account's balance/transactions are silently skipped for the run - no error, no warning. The skip is tracked in-memory (`ImportSummary.UnmappedAccounts`), but that list is never written into the persisted `import_runs.summary` text, and per-run progress lines (`import_run_progress_lines`) aren't populated for scheduled SimpleFin runs at all. So if this happens during an unattended scheduled sync, there is currently no way to discover it happened short of directly querying the database.

**Why it matters**: a newly-linked account, a re-linked account that got a fresh source-side ID (this already happened once for real, with one of the two SoFi loan accounts), or a corrupted/reverted map file would all silently stop syncing that account - with zero visible signal on the Import Data page or anywhere else in the app.

**Current status**: confirmed via direct query that this has never actually happened (zero historical occurrences across all `import_runs.summary` text and all progress lines, checked 2026-07-27). Not an active problem - a monitoring blind spot for if/when it does happen.

**Possible fix, not yet built**: surface `UnmappedAccounts` in the persisted run summary text (same place `TransactionsAdded`/`DuplicatesSkipped` already show), so it's visible on the Import Data history table without needing to query the database directly.

## Amex cycle forecast ignores PayInFullAmex spending made on other accounts (found 2026-07-28)

**What**: The Spending Tracker's actual-vs-budget comparison sums real transactions by `CategoryId` only (`SpendingTrackerService.cs` - no `AccountId` filter), so it correctly reflects total spending in a PayInFullAmex category regardless of which card was used. But the Amex cycle forecast (`ForecastEngine.cs` and `ForecastAccuracyService.cs`, both `chargeTransactions` queries) filters to `t.AccountId == account.Id` for the Amex account specifically, while comparing against the *same* category-based `monthlyBudgetTotal` used by Spending Tracker. The two sides of `MAX(actual Amex charges, monthlyBudgetTotal)` don't agree on what "actual" means: one is Amex-only, the other implicitly assumes all budgeted spending happens on Amex.

**Why it matters**: if spending in a PayInFullAmex category (e.g. Groceries) happens on the checking debit card instead of Amex, the real Amex-specific charges for that cycle drop, but `monthlyBudgetTotal` doesn't change - so `MAX` keeps using the full budget figure as the forecasted Amex payment, even though less than that will actually be charged to Amex. This overstates the forecasted Amex payment (understating the projected checking balance) for that cycle. It self-corrects once the real Amex payment posts and reconciliation replaces the estimate - it doesn't compound across cycles - but it recurs every cycle for as long as the debit/Amex split habit continues, which can read as a persistent quirk even though each instance is a fresh, non-compounding error.

**Current status**: confirmed via code reading (2026-07-28), not yet observed as a real live discrepancy. Purely a "how does it behave" finding so far, not a fix in progress.

**Possible fix, not yet built**: either (a) have the Amex cycle's "actual" side sum total category spending across all accounts, the same way Spending Tracker does, instead of restricting to `AccountId == Amex`, or (b) accept the current Amex-only behavior as correct and instead lower budgeted amounts for categories habitually split across cards. Needs a decision on which behavior is actually intended before building anything.
