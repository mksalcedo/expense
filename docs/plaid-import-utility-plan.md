# Plaid Transaction Import Utility (small, first slice)

Status: **built and in use**. `PlaidTransactionImportService` (core logic, TDD) plus a real runnable console tool, `src/Expense.Importers.Plaid`, mirroring the existing `Expense.Importers.SimpleFin` project pattern. Run it with:
```
~/bin/plaid-cli transactions list --start-date <X> --end-date <Y> --json | dotnet run --project src/Expense.Importers.Plaid
```
Account mapping lives in `src/Expense.Importers.Plaid/plaid-account-map.json` (gitignored, real account IDs - copy from the committed `.example.json` template, same convention as SimpleFin's). Also writes a `CheckingBalanceSnapshot` from the same file's `available` balance (see below) - this was added as a second pass once it was discovered SimpleFin's balance figure was *also* stale by the exact amount of the missing transactions.

See conversation history for the full backstory: SimpleFin's connection to Wells Fargo Checking silently lagged 3+ days behind real posted transactions; the real Wells Fargo site confirmed the gap; a direct Plaid pull (via `~/bin/plaid-cli`, Plaid's official CLI) proved current and complete for the same account.

## How the data gets here (already working, no code needed)

The user runs, by hand, whenever they want fresh data:
```
~/bin/plaid-cli transactions list --start-date <X> --end-date <Y> --json > plaid-transactions.json
```
This is Plaid's own official CLI (confirmed working against the real, linked Wells Fargo Checking account this session). Output is **not single JSON** - it's one `{"diagnostic":...}` line followed by one payload line containing `accounts`/`item`/`total_transactions`/`transactions`. The importer must parse line-by-line and use the line that actually has a `transactions` array.

## Real schema (captured directly, not guessed)

```json
{
  "accounts": [{"account_id": "41mjaLMw...", "name": "EVERYDAY CHECKING ...4103", "mask": "4103", ...}],
  "transactions": [
    {
      "transaction_id": "dYMApNb3P0uMg3kYqogMHKqB19V5Nvtox7EkX",
      "account_id": "41mjaLMw...",
      "amount": 33.87,
      "date": "2026-07-25",
      "name": "Chipotle Mexican Grill",
      "merchant_name": "Chipotle Mexican Grill",
      "pending": true
    }
  ]
}
```

Confirmed field mapping to `BankTransaction`:
- `transaction_id` → `ExternalId` (Plaid's own stable ID - reliable for Plaid-to-Plaid re-import dedup, e.g. re-running an overlapping date range).
- `amount` → `-amount` (**sign is flipped** - Plaid reports positive = money out, negative = money in; confirmed directly against real data, e.g. a real payroll deposit shows as `-4492.86`).
- `date` → `TransactionDate` always; also `PostedDate` when `pending == false`, else `PostedDate = null` (matches the app's existing pending/posted convention).
- `name` → `Description` (always present).
- `merchant_name` → `Merchant` (nullable).
- `account_id` → local `Account.Id` via a small map (same idea as `simplefin-account-map.json`), passed into the service as a parameter for this first slice rather than a committed config file.
- `ImportSource` = the literal string `"Plaid"`.

## The real dedup problem, and the scope decision for this slice

If SimpleFin and Plaid are ever both configured for the same account, every real transaction *will* eventually appear via both (not a maybe - discussed and agreed on directly). Neither of `DedupService`'s existing two checks (`ExternalId` exact match, or a fingerprint that includes normalized description) can catch this cross-source case: Plaid's `transaction_id` never equals SimpleFin's own transaction id for the same real transaction, and the description text differs enough between sources (raw bank text vs. Plaid's cleaned merchant name) that fingerprint matching won't reliably catch it either.

This isn't hypothetical for the very first run: the sample date range already overlaps with dates SimpleFin already has (7/16, 7/17, 7/20, 7/21, 7/22) - importing without a cross-source check would create real duplicate rows on the first try.

**Decision for this slice**: add one new, narrow method to `DedupService` - `ExistsForAccountDateAmountAsync(context, accountId, postedDate, amount)` - checking only `(AccountId, PostedDate, Amount)`, deliberately ignoring description/source. Used only by the new Plaid importer, layered *after* the existing `ExternalId` check (so Plaid-to-Plaid re-imports still get the more precise match first). SimpleFin's own import path is untouched - this is additive, not a change to existing dedup behavior. Only applies when `PostedDate` is not null (pending transactions have nothing to collide with, since SimpleFin never reports pending rows).

Known accepted risk, named rather than hidden: two genuinely different real transactions for the same account, same day, same exact amount would incorrectly collide under this check (e.g., two identical-amount transfers same day). Narrow, real, but rare - acceptable for this slice.

## New code (TDD, as always)

- `src/Expense.Domain/Services/Ingestion/Plaid/PlaidTransactionModels.cs` - plain POCOs for deserializing the payload line (`PlaidTransactionsPayload`, `PlaidTransaction`, `PlaidAccount`).
- `src/Expense.Domain/Services/Ingestion/Plaid/PlaidTransactionImportService.cs` - one method: `Task<ImportSummary> ImportAsync(ExpenseDbContext context, string rawCliOutput, IReadOnlyDictionary<string, int> accountMap, CancellationToken cancellationToken = default)`. Reuses `DedupService` (existing `ExistsAsync` + new cross-source method) and `CategorizationService.ApplyMerchantRuleAsync`, same as `SimpleFinImportService` does today. Reuses the existing `ImportSummary` type.
- `DedupService.ExistsForAccountDateAmountAsync` - new method, additive only.

## Explicitly deferred (not this slice)

- No `Program.cs`/Sync Now UI wiring, no `ImportRun` tracking, no file-path configuration setting - this is a plain service, exercised directly by tests and (once proven) a short manual script, same pattern as the reconciliation dry-run tool used earlier this session.
- No decision yet on "coexist with SimpleFin" vs. "swap checking to Plaid-only" - that's a real, separate decision for later, informed by using this for a while.
- No `plaid-account-map.json` config file yet - account map passed directly as a parameter until this is wired into anything more permanent.

## Tests (TDD)

- Parses the two-line diagnostic+payload CLI output correctly (ignores the diagnostic line).
- Sign is flipped correctly (positive Plaid amount → negative `BankTransaction.Amount`, and vice versa).
- `pending: true` → `PostedDate` null; `pending: false` → `PostedDate` set to `date`.
- Re-importing the identical payload twice creates no duplicate rows (`transaction_id`-based dedup).
- A transaction that collides on `(AccountId, PostedDate, Amount)` with an existing (e.g. SimpleFin-sourced) row is skipped, even with totally different `ExternalId`/description.
- An unmapped `account_id` is skipped/reported, not a crash.
- `Description`/`Merchant` map from `name`/`merchant_name` correctly, including `merchant_name: null`.
- Newly-imported rows get categorized via the existing merchant-rule matching, same as SimpleFin transactions.
