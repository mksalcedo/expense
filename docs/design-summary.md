# Expense / Cash-Flow App — Design Summary

Living summary of design decisions made so far, distilled from:
- The legacy spreadsheet: `~/Downloads/budget-2023-09-23.xlsx` (tabs "Budget New" and "In Progress" only)
- A long prior design conversation with ChatGPT — full reconstructed transcript in [`chatgpt-conversation-full.md`](./chatgpt-conversation-full.md)
- Follow-up discussion with Claude Code (this session)

This document will keep evolving as remaining open questions get resolved. Treat it as the source of truth handed to whichever tool ends up implementing the app — do not redesign the financial model without updating this doc first.

## Goal

Not "replace the spreadsheet" — **dramatically reduce the time cost of financial planning**. The spreadsheet is the accumulated correct financial logic from years of tuning and should be preserved conceptually, not redesigned from scratch. The forecast output should remain spreadsheet-shaped (viewable/editable, exportable to Excel), not just an in-app dashboard.

The real pain today is ~15–30 minutes/day of manual transaction entry/categorization, plus roughly once a year losing the better part of a day extending the forecast spreadsheet another 12 months forward.

## Three separate questions (core mental model)

| Module | Question it answers |
|---|---|
| **Forecast** | What will happen if I continue my current plan? (starts from actual checking balance, ~12 months rolling forward) |
| **Current Budget Tracking** | How am I doing this week/month? (category budget vs. actual vs. remaining, checked several times/week) |
| **Historical Analysis** | Is my current plan realistic? (required core functionality, not optional — informs whether *I* decide to change a budget; never auto-adjusts anything) |

**Required Historical Analysis reports** (all computed on demand from `bank_transactions`/`amazon_order_items`, no dedicated storage): weekly reports, monthly reports, 4-week averages, 13-week averages, year-to-date totals, budget-vs-actual (compared against the `budget_periods` amount in effect at the time, never today's), a category trend chart, and a recurring-product report (`Product | Category | Purchases | Average price | Total spent | Last purchased`) — useful given the real price-shopping effort already invested in supplements. Weeks are **Sunday-start**, matching the old spreadsheet.

## Core principles

1. **Actual checking balance is truth.** The forecast always starts from today's real balance, never from reconciling historical transactions. "You don't reconcile history, you correct the present."
2. **Budgets are dated-versioned targets, not predictions.** Stored as `{category, amount, effective_from, effective_through}`. Historical reports compare actual spending against the budget that was *in effect at the time*, never today's budget retroactively.
3. **Store atomic transactions, not period aggregates.** Weekly/monthly/13-week totals are computed on demand from transaction-level data.
4. **The forecast uses budgeted aggregates for variable spending**, not individual predicted purchases — e.g., a single "Variable Spending Budget" line (sum of Groceries+Restaurants+Supplements+Gas) in the forecast ledger, while the spending tracker owns category-level actual detail.
5. **Editable ≠ auto-adjusting.** Every planning number (budget amounts, Amex extra-principal figure, recurring rule amounts) is user-editable in the app at any time and the forecast recomputes instantly. "Budgets don't auto-change" means no *algorithm* silently adjusts them based on spending — it does not mean hardcoded values.

## Categories (v1, extensible)

Groceries, Restaurants, Supplements, Gas — the durable starting set. Must be an editable list (not a hardcoded enum) since more will likely be added later (e.g. Miscellaneous, Home Improvement, Medical). Off-budget/other spending (subscriptions, one-off purchases) stays informational-only for now — no target, but useful for spotting new categories worth tracking.

**Credit card payment categories (one per actual debt account, resolved):** one payment category per real account row — `Amex Payment`, `Discover Payment`, `Chase Sapphire Payment`, `Chase Amazon Visa Payment`, `Chase 0817 Payment`, `Wells Fargo Visa Payment`, `Wells Fargo LOC Payment`, `Apple Card Payment`, `SoFi Payment`, `Venmo Payment` — each `is_budgeted = true`, distinct from Off-Budget/Misc specifically because a deviation here *should* draw attention (an emergency charge, a missed/doubled payment), unlike genuinely informational off-budget spend. One category per *account*, not per issuer brand, since the discovered real account inventory (see Accounts model below) has three separate Chase cards and two separate Wells Fargo accounts, each with its own independent expected payment — the "identify which card changed" reasoning applies exactly as much between two Chase cards as it does between Chase and Discover. Each gets an ordinary merchant rule routing its checking-side payment transactions to it. The "budget" for each is **computed, not manually entered**: `accounts.min_payment + accounts.extra_payment` for every debt account, and the existing `MAX(actual, budget) + extra principal` forecast rule for Amex specifically.

Supplements matters because of heavy recurring healthcare-supplement spending via Amazon (sample month: 19 Amazon orders / 30–35 products, ~85% supplements, largely the same recurring products month to month).

## Accounts model

- **Active spending accounts**: Amex + Wells Fargo Checking only. These generate transactions that feed the 4 budget categories.
- **Amex is special**: it's both a spending account (Groceries/Supplements/Restaurants/Gas) *and* a debt account (balance, statement cycle, payments) — see funding model below.
- **Debt accounts — real inventory discovered via SimpleFIN verification (see ingestion section below), broader than the original placeholder list of "Discover, Chase, SoFi, Wells Fargo":**

  | Account | Balance/transaction sync | Notes |
  |---|---|---|
  | Discover | Automatic (SimpleFIN) | |
  | Chase Sapphire Reserve (9194) | Automatic (SimpleFIN) | matches "Chase 9194" in the old spreadsheet |
  | Chase Amazon Prime Visa (1300) | Automatic (SimpleFIN) | matches "Chase 1300" |
  | Chase Credit Card (0817) | Automatic (SimpleFIN) | matches "Chase 0817" |
  | Wells Fargo Cash Back Visa | Automatic (SimpleFIN) | matches "Wells Fargo CC" |
  | Wells Fargo Personal LOC | Automatic (SimpleFIN) | matches "Wells Fargo LOC" |
  | Apple Card | Best-effort (SimpleFIN, monthly refresh, observed unreliable re-auth) | falls back to manual when the connection isn't cooperating |
  | SoFi | Manual only, by choice | a fixed installment loan with no revolving-charge risk — no "surprise balance" scenario a live connection would protect against, so manual entry (the v1 default for all debt accounts anyway) is just as good here |
  | Venmo Credit Card | Manual only, by necessity | issued by Synchrony Bank but managed entirely through Venmo's mobile app with no web login at all — no credential surface for any aggregator to connect to |

  Every debt account still only needs `balance`, `min_payment`, `extra_payment` — deliberately in paydown-only mode, no new charges by policy (occasional emergency spend, e.g. an HVAC repair, may still happen). Where SimpleFIN provides transaction-level detail for a debt account too, **it's discarded/ignored** — only the balance field is used, keeping the schema consistent with the original design (debt accounts were never meant to feed the Spending Tracker or categorization at all, only `debt_balance_snapshots`).

**Adding/editing/removing a debt account is one unified operation, not three separate manual steps.** Given the SimpleFIN investigation alone turned up 9 real debt accounts against an original placeholder list of 4, this has to be easy: creating an account (name, min/extra payment) automatically creates its matching "X Payment" category (`is_budgeted = true`, funding strategy defaulting to `pay_in_full_amex` if it's charged to Amex, otherwise `none`) and suggests a starting merchant rule for it — which the user can then adjust if the auto-suggested pattern doesn't match the real bank description, but doesn't have to set up from scratch across separate screens. Editing or deactivating an account manages its associated category/rule as part of the same action. Deactivating (not hard-deleting) is the default for removal, preserving historical transactions/reports — consistent with "historical data is preserved" elsewhere in this doc.

## Amex funding / statement-cycle model

The $1,100/month is **extra principal reduction**, added on top of paying off the current statement cycle's new qualifying charges — not a fixed base payment that new charges get added to.

```
Planned Amex Payment = Qualifying New Charges (this statement cycle) + Extra Principal Reduction ($1,100)
```

Four concepts matter conceptually, but only one of them turned into a table — the other three collapsed into computed queries or periodic snapshots, consistent with "generate/derive, don't store" elsewhere in this doc:
- **Purchase** — what was bought, which budget category it affects. This is just `bank_transactions` on the Amex account. Counts against the budget immediately on the purchase date.
- **Obligation** — how much of a purchase should be added to an upcoming payment. Not stored — computed as a date-range query (`WHERE posted_date BETWEEN cycle_start AND cycle_end`) against `bank_transactions`, using the statement-cycle rules on `accounts` (`statement_close_day`/`payment_due_day`).
- **Payment** — the actual checking → Amex transfer. Just an ordinary `bank_transactions` row on the checking account once it happens (see categorization note below) — no separate payment table.
- **Allocation** — which obligations a given payment satisfied (partial payments, early payments, multiple payments/month, post-close refunds). Not tracked at all: there's no persisted per-purchase obligation ledger to allocate against in the first place. Reality — including an underpayment or a late payment — simply shows up in the next `debt_balance_snapshots` entry, the same way "actual balance is truth" resolves everything else in this design.

Each budget category has a **Funding Strategy** (v1: Groceries/Supplements/Restaurants/Gas = "pay in full at next Amex due date"). The forecast must never double-count: a purchase affects the budget immediately but does not leave checking until the corresponding payment posts.

**Categorizing the real Amex payment transaction (superseded — see Categories section above):** once an actual Amex payment posts on the checking account, an ordinary `merchant_rules` entry (e.g. matching "AMERICAN EXPRESS") routes it to the dedicated `Amex Payment` category, not Off-Budget/Misc — so a deviation from the expected payment amount surfaces in the Spending Tracker like any other category, rather than being buried in a bucket nobody actively watches.

**How the Amex payment amount is computed for each due date in the 12-month forecast (resolved):**

- **Cycle still entirely in the future** (hasn't started) → use the budgeted variable-spending total (sum of current `budget_periods` amounts, each **prorated to its monthly equivalent** regardless of what frequency it was entered in, for whichever categories have `funding_rules.strategy = pay_in_full_amex` — Groceries/Restaurants/Supplements/Gas today, but never hardcoded by name; adding, renaming, or removing a category just means updating its funding strategy, not touching code) + extra principal. There's no actual data yet, so this is the planning estimate.
- **Cycle in progress or already closed** → use `MAX(actual charges this cycle, budgeted variable-spending total)` + extra principal. The real number only overrides the budget when it's *higher*. If the cycle actually came in under budget, the forecast still shows the budgeted (larger) number — it does not get optimistic based on underspending. Any real savings shows up naturally the next time the actual checking balance is refreshed, not by the forecast lowering a future payment line in advance. This is the same asymmetry as "budgets are targets, not predictions," applied specifically to protect the forecast from ever looking rosier than the plan.
- This computation **never needs category data** — it's a straight sum of raw Amex transaction amounts for the cycle (see the categorization/forecast decoupling note below), compared against the budget total. It is decoupled from the Spending Tracker's category-level actual/remaining view, which is a separate computation over the same underlying transactions.

## Amazon ingestion — the load-bearing module

**Without solid Amazon ingestion/matching, the project isn't worth building** — this is the reason the project exists, not a nice-to-have. ~60–70% of Amazon purchases are supplements-adjacent but mixed with groceries/misc in the same orders. Moving supplement purchases to a dedicated vendor was considered and rejected — Amazon is genuinely the cheapest source after real price-shopping work already done.

**Resolved approach — Gmail-based email parsing (superseded an earlier browser-scraping plan):**

- Amazon removed the old "Order History Reports" CSV export in 2023. The current official replacement, "Request Your Data" (Privacy Hub), produces a ZIP of CSVs but can take hours-to-days to prepare — rejected as the primary mechanism since near-daily updates are wanted.
- An earlier plan called for browser-assisted extraction (a script attaching to an already-logged-in browser session, reading the live Orders/order-detail pages). That was **replaced** after directly inspecting real emails in the user's own Gmail account, which showed a cleaner and more durable path:
  - **`auto-confirm@amazon.com` ("Ordered: ...") is the primary source.** Its plaintext body reliably contains, for every order checked (single-item and multi-item): order ID, item title, quantity, per-item price, and grand total — arriving at order time, which matches the "count as soon as product is known" timing decision exactly. Example structure:
    ```
    Order #
    113-4492181-5586630

    * Standard Process Cardio-Plus - Antioxidant Support...
      Quantity: 1
      22.8 USD

    * Pure Encapsulations Vitamin D3 125 mcg...
      Quantity: 1
      21 USD

    Grand Total:
    46.43 USD
    ```
    The tax/shipping leftover (`grand total − sum(item prices)`) is prorated across items exactly as already decided for tax allocation.
  - **`shipment-tracking@amazon.com` ("Shipped: ...")** contains the same data but is redundant — and since Amazon can split one order across multiple shipments, the single "Ordered" email is the more reliable source for a complete item list, not the shipment emails.
  - **`order-update@amazon.com` ("Delivered: ...")** has item names and quantity only, **no price** — not used for ingestion.
  - **`return@amazon.com` / `payments-messages@amazon.com`** (refunds/returns) contain item name, quantity, order #, and refund subtotal — parsed the same way, feeding the refund handling already decided (categorized like the original purchase, typically a negative amount).
- **This eliminates browser automation, the Amazon ToS/detection risk, and DOM-fragility entirely.** Gmail's API is stable/versioned and Amazon's email templates change far less often than their website's HTML/CSS — a meaningfully lower long-term maintenance burden than any DOM-scraping approach (Playwright, browser extension, or bookmarklet — all of which share the same underlying fragility regardless of which executes them, since they all read the same frequently-restyled page).
- **Dedup key: Order ID (+ item name + price), never date ranges.** "Think in terms of unique order IDs, not date ranges" eliminates import-overlap/missed-day bookkeeping entirely. Same principle applies to bank CSV imports — dedupe via bank transaction ID when available, else a fingerprint of `account + posted date + amount + normalized description`, with source-row order as a tiebreaker for genuine duplicates.
- Trigger is manual/whenever-convenient, same as pulling a fresh Amex/checking CSV — there's no need for scheduled automation, though Gmail's proper OAuth (no stored password) would technically allow it later if ever wanted.
- Design principle carried over regardless of source: **fail loudly, not silently**, when an email doesn't match the expected structure (e.g. no items found, a price field doesn't parse) rather than importing wrong or empty data.
- The product → category lookup table is the actually-hard long-term problem, more so than the Amazon mechanics themselves. Known products auto-categorize; unknown ones land in a small review queue, get approved once, and are remembered forever.
- Aim for "accurate enough for budget categories" in v1, not full accounting-grade reconciliation of Amazon's split shipments / Subscribe & Save / gift-card / tax edge cases.

## Amex, checking & debt-account ingestion mechanism (resolved — via SimpleFIN Bridge)

**Superseded a Playwright/CDP-attach browser-automation plan** (human logs in, script automates clicking "Download" on the Statements & Activity page) after actually testing a real alternative rather than committing to browser automation on assumption alone.

**What was checked first, and ruled out:** Gmail was searched for a per-transaction Amex alert email (the same pattern that worked for Amazon) — none exists, only a weekly lump-sum account-snapshot email and payment/AutoPay confirmations. Amex's own account settings were also checked directly and have no "alert me on every transaction" option. Plaid was considered and found to have no public pricing (gated behind a sales-contact "Request Production Access" flow) — not a hard blocker, but enough uncertainty that a cheaper, more transparent alternative was worth finding first.

**Resolved approach: [SimpleFIN Bridge](https://beta-bridge.simplefin.org/)** — a bank-data aggregation service built specifically for personal finance tools (it's the aggregator behind Actual Budget, a well-known open-source budgeting app), not a business-oriented platform like Plaid/MX/Finicity.

- **Cost: $1.50/month or $15/year (+tax), confirmed directly from SimpleFIN's own site** — covers up to 25 connected institutions and 25 apps. Real, transparent, self-serve pricing, unlike Plaid.
- **Credentials never touch our app.** The user authenticates with each institution through SimpleFIN's own hosted connection flow (redirects to the institution's actual login domain) and receives a one-time SimpleFIN Token, which the app exchanges for a durable Access URL (embedded Basic Auth) used for all subsequent pulls. Same credential-safety model as Plaid's Link widget, at a fraction of the uncertainty and cost.
- **Plain JSON over REST** (`GET {access-url}/accounts?start-date=...`) — dramatically simpler to build against than browser automation: no DOM to navigate, nothing that breaks when a bank redesigns a page.
- **Actually verified end-to-end with real accounts, not just documentation**: connected Amex, Wells Fargo (checking + Cash Back Visa + Personal LOC), Discover, and all 3 Chase cards. Real balance and transaction data came back correctly for all of them — e.g. Amex's balance matched exactly what the actual Amex website showed, and a `-$8,337.24` payment on the Wells Fargo checking side matched an independent `+$8,337.24` payment on the Chase 0817 side, confirming data consistency. See the Accounts model section above for the full per-account outcome (most auto-sync; Apple Card is best-effort; SoFi and Venmo Credit Card stay manual, for different reasons each).
- **Operational details learned from real use, not assumed:**
  - The API warns that requested date ranges over **45 days** may be capped in the future — pulls should stay within that window each time (still far more than needed for a several-times-a-week cadence with normal dedup).
  - Refresh is roughly daily for most institutions, though SimpleFIN's own dashboard notes "transactions... often take a few days to appear" — daily-ish, not instantaneous, but a large improvement over a manual per-visit habit.
  - Apple Card specifically refreshes only monthly and showed unreliable re-authentication in practice — treated as best-effort with manual fallback, not depended upon.
- **Same dedup pipeline as everything else**: bank transaction ID when available, else the `account + posted date + amount + normalized description` fingerprint, with source-row order as a tiebreaker.
- **This eliminates browser automation from the entire ingestion design, for every source.** Amazon uses Gmail email parsing; Amex, Wells Fargo checking, and most debt accounts use SimpleFIN's API. Nothing in this system scrapes a webpage.
- **Reminder — this only changes how a row lands in `bank_transactions`, not what it means afterward.** Wells Fargo checking transactions still have an immediate cash-flow impact (real money already moved, feeding the actual-balance-is-truth forecast starting point). Amex transactions still have an immediate *budget* impact but a delayed *cash* impact (the purchase counts against its category right away; cash doesn't leave checking until the separate, later `Amex Payment` transaction posts, computed via the existing `MAX(actual, budget) + extra principal` rule). The dual nature of Amex and the statement-cycle logic designed earlier are entirely unaffected by this ingestion mechanism.

## Access model & platform constraints (confirmed)

- **No login/auth system.** Not cloud-hosted. Access control is simply "whoever has access to my laptop" — this is a local, single-user tool.
- **Mobile/remote access is explicitly out of scope for v1** — it's a "maybe someday, far-reaching version" idea, not a v1 requirement. Checking in from the computer at home is sufficient.
- These are access-model/requirements facts, not a tech-stack decision — the stack conversation itself is still deferred.

## Spreadsheet export (confirmed: one-way, formula-driven)

- **One-way export only.** The app's database is the sole source of truth going forward. Any permanent change is made in the tool, never in the exported spreadsheet.
- The exported spreadsheet exists for **local what-if scratch work** — e.g. "what happens over the next 12 months if I change the grocery budget" — and those edits are never expected to flow back into the system.
- Important requirement: the export **must be formula-driven, like the current "Budget New" tab**, not a flat table of computed literals. E.g. weekly grocery-budget rows should all reference one master cell (the way the existing sheet does with `=$N$2`, `=$N$4` for income amounts), so changing one assumption cell updates all 52 weeks downstream automatically, instead of requiring 52 separate edits. The export generator needs to reproduce this "master data + referencing formulas" pattern, not just paste computed values per row.
- **Scope: the Forecast only.** The Spending Tracker (category budget/actual/remaining) does not need a spreadsheet export — it only needs to exist as a live view in the app. Confirmed: as long as that view is in the app, it's covered.

## Transaction lifecycle model (resolved — simplified)

Two tables. That's the whole model:

- **Categorized amounts** — money confirmed to belong to a category (or, for Amazon, a computed set of categories). This is what feeds current-week/month spend and historical reports.
- **Pending categorization** — waiting on the user. A row lands here for any reason (doesn't need its own sub-status): an unrecognized checking/Amex merchant, a new Amazon product not yet in the lookup table, or an Amazon charge whose item total doesn't tie out to the bank amount. Whatever the reason, it's the same one queue to review; once resolved, the row moves to categorized amounts and stays there.

**Splits** are not core infrastructure. Amazon is the one case that genuinely needs an automatic multi-category breakdown, computed from matched item categories once available. Every other merchant is essentially single-category in practice (even a mixed-merchant store like Costco shows up wholesale as Groceries in the existing data, never split) — a manual split is a rare hand-override if it ever comes up, not something to build a dedicated UI for in v1.

**Pending vs. posted bank transactions** is real but is import plumbing, not a user-facing state. A charge can appear first as "pending" then later "posted" (sometimes with a different posted date), and the import step must recognize that as an update to the same row rather than a new duplicate. That's handled by the same dedup rule already defined for imports generally (bank transaction ID when available, else the account+date+amount+description fingerprint) — it never surfaces as a status the user manages.

Deliberately dropped from earlier drafts of this section: a separate "posting_state"/"amazon_match_state" dimension, a distinct "needs_review" bucket separate from "pending categorization," and any distinction between "manually guessed category" vs. "formally Amazon-verified category" — all of that only matters for penny-accurate reconciliation, which is explicitly not the goal here ("accurate enough for budget categories").

**Three follow-up questions, resolved:**

- **Amazon timing**: an item counts toward its category as soon as the *product* is known (from the order data itself, using the order's own price) — not gated on matching it to an actual bank charge. Deliberately ignores ship dates and bank posting dates to keep it simple. The bank-tie-out becomes a background integrity check, not something that blocks the number from showing up.
- **Visibility of pending amounts**: money sitting in "pending categorization" still needs to be visible before it's categorized — it shouldn't silently disappear from the current-week/month totals just because the category split isn't final. The spending tracker view should show category totals (Groceries/Restaurants/Supplements/Gas) plus a separate **"Uncategorized / pending review"** line, so total real spend is always visible even when some of it hasn't been sorted into a category yet.
- **Refunds**: not a special case. A refund is just a transaction (typically a negative amount) that gets categorized exactly the same way the original charge/merchant would be — same merchant rule for checking/Amex, same product's category for an Amazon item-level refund. No separate refund-handling logic needed.

## Weekly/monthly Spending Tracker carryover (resolved: none)

Unlike the current spreadsheet — which carries an over/underspend forward into the next week's "Remaining" (`Remaining = Budget − This Period's Spent + Last Period's Remaining`) — the app uses **no carryover at all, in either direction.** Each period's Remaining is simply:

```
Remaining = this period's budget_periods amount (prorated to this period's length) − SUM(this period's categorized spend)
```

No stored or carried-forward state, no dependency on the prior period. The cash-flow impact of overspending is already captured by the Amex forecast's `MAX(actual, budget)` rule; the decision of whether to actually change a budget going forward is left entirely to the historical trend reports (weekly/13-week averages, etc.) — carryover would have been a second, competing way of surfacing the same signal inside the wrong view.

## Database schema (resolved)

Guiding rule applied throughout: only add a table if it earns its place; generate/derive at read time instead of storing wherever the data doesn't need to persist.

**Accounts & balances**
- `accounts`: `id, name, type` (checking | active_spending | debt), `min_payment`, `extra_payment` (nullable, debt-type only), `payment_due_day` (nullable, every debt account — the forecast needs a day-of-month to place each account's recurring payment on the ledger), `statement_close_day` (nullable, **Amex only** — only Amex's forecast computation needs a "which charges belong to which cycle" boundary; every other debt account's payment is just a flat `min_payment + extra_payment` on its due day, no cycle-qualification logic), `is_active` (implied by, but not originally listed alongside, the "deactivate not hard-delete" account-management rule above — added during implementation once that gap was noticed). Both day-of-month fields are a recurring pattern, not a stored list of past/future cycles.
- `checking_balance_snapshots`: `as_of_date, balance`. The forecast always starts from the latest row.
- `debt_balance_snapshots`: `account_id, as_of_date, balance`. Kept as a history (not a single current-value field) specifically to support a debt-balance-over-time trend chart.

**Categorization & ingestion**
- `categories`: `id, name, is_budgeted`.
- `merchant_rules`: `merchant_pattern, category_id` — applies across accounts.
- `products`: `product_pattern, category_id` — the Amazon product lookup table.
- `bank_transactions`: `id, account_id, transaction_date, posted_date, description, merchant, amount, external_id, import_source, dedup_fingerprint, category_id (nullable), is_amazon_merchant, created_at`. `category_id IS NULL` is "pending categorization" — no separate table, just a filter. Amazon-merchant rows never get a `category_id` here; their category lives entirely at the item level (see the Amex forecast section above for why this is safe — the forecast side never needs it).
- `amazon_order_items`: `id, order_id (plain text, no parent orders table — the duplication of order_date across ~1-2 items per order isn't worth a join), order_date, item_title, price, quantity, tax_allocated, product_id (nullable), category_id (nullable), refund_amount (nullable), created_at`. Tax is **prorated across all items in the order** (proportional to each item's price). `product_id IS NULL` is that item's "pending categorization."

**Budgets**
- `budget_periods` (renamed from an earlier "budget_versions" — clearer name for the same thing: each row is the period during which a specific amount applied to a category): `category_id, amount, frequency (weekly | biweekly | monthly | quarterly | annual), effective_from, effective_through (nullable = current)`.
- **Budget amounts can be entered in any frequency, not a fixed unit.** A category isn't forced into "always weekly" or "always monthly" — one category could be weekly, another quarterly, and a category can even change frequency over time (a new dated `budget_periods` row can carry a different frequency than the last one, same as it can carry a different amount). Every consumer of a budget figure — the Spending Tracker's week/month views, the Amex forecast's monthly "qualifying charges" estimate, Historical Analysis's budget-vs-actual comparisons — prorates from whatever frequency was actually chosen via a canonical daily-rate conversion (`amount ÷ days-in-that-frequency`, then `× days-in-whatever-period-is-needed`), rather than assuming one global unit.

**Forecast — deliberately not stored as generated events**
- `recurring_rules`: `name, direction (income/expense), amount, frequency, anchor, account_id, active, start_date, end_date (nullable)`. Covers income (paychecks, SS, pension) and genuinely fixed bills (mortgage, internet, power, insurance, etc.) only. **Does not** need a `category_id`, and does not get rows for the Variable Spending Budget line (computed from `budget_periods` at generation time) or any debt payment including Amex (computed from `accounts.min_payment`/`extra_payment`, plus actual Amex cycle charges — see above).
- `one_time_events`: `name, amount, direction, date, account_id`.
- `funding_rules`: `category_id, strategy`. **Correction — this is a live, user-configurable table from day one, not the inert placeholder an earlier pass of this doc described.** It's exactly the mechanism that answers "which categories count toward the Amex payment forecast," and that has to be configurable: the user may rename, add, or remove budgeted categories over time, and each one needs an explicit funding strategy (`pay in full at next Amex due date` for anything charged to Amex, or `none` for categories that aren't). Set from the same category-management screen as everything else about a category — adding a category means picking its funding strategy, not editing code. The Amex forecast computation sums `budget_periods` for categories `WHERE funding_rules.strategy = 'pay_in_full_amex'`, never a hardcoded category name list.
- The forecast ledger itself is generated on demand from all of the above plus the latest balance snapshot, every time it's viewed or exported — never persisted, matching "no manual row creation."
- **Forecast horizon is a configurable setting, not a hardcoded constant** — defaults to 12 months, but should be adjustable (e.g., a simple app setting) so shorter ranges (3 months, say) can be used to keep things fast while testing during development, without a code change.

## V1 scope (draft — revisit before locking in)

**In scope:** Forecast engine (checking balance, recurring income/expenses, debt payments, one-time events, configurable-length rolling forecast defaulting to 12 months, lowest projected balance, monthly surplus/deficit); spending tracker (4 categories × current week + current month); transaction import for Amex + Wells Fargo checking via SimpleFIN; debt-account balance tracking (automatic via SimpleFIN for most accounts, manual for SoFi/Venmo/best-effort Apple Card); Amazon product categorization + review queue (via Gmail); historical analysis (required, not deferred).

**Proposed non-goals for v1** (revised — the original ChatGPT design pass explicitly avoided a Plaid-style bank-aggregation service on cost/credential grounds, but that turned out to not hold up: SimpleFIN, verified directly, is cheap, transparent, and credential-safe, so it's now core v1 scope rather than something ruled out): mobile app, tax tracking, investment tracking, net worth calculation, full double-entry accounting.

**Sequencing note:** the biggest execution risk is over-automating too early vs. a simple "Pull from SimpleFIN/Gmail → review a few new Amazon products → refresh forecast → done" loop. Likely worth sequencing Amex/checking ingestion + forecast first as a fast win, then layering in Amazon email-based ingestion (via Gmail) — while still treating Amazon as core v1 scope, not a deferred phase, per explicit priority. Notably, neither piece involves browser automation at all anymore, which lowers this risk considerably compared to the originally-planned approach.

## Tech stack (resolved)

- **C#/.NET, Blazor Server, PostgreSQL.** Postgres because the user already runs Postgres databases locally on Linux; .NET weighted toward the user's strongest existing familiarity (ahead of JavaScript, Python, Java) since there was no technical gap to justify trading that away — Npgsql/EF Core for data access, ClosedXML for formula-driven Excel export (writes real formulas like `=$N$2`, not just values — required by the spreadsheet-export decision), Google's official .NET Gmail API client for the Amazon email ingestion, and a plain `HttpClient` + JSON deserialization for SimpleFIN Bridge (a bare REST/JSON API — no special SDK needed or available).
- **Blazor Server, not a native desktop GUI.** Chosen over a native app (e.g. Avalonia UI) mainly because web UI patterns (tables, forms, a click-to-categorize review queue, charts) are far better-trodden territory to build and iterate on reliably than desktop UI toolkits, which matters given the user isn't writing this code themselves. Also leaves the door open to remote/phone access later without a rewrite, if that "distant future version" idea ever becomes real. Runs as a local web server bound to `localhost` only — no cloud, no auth system, matching the confirmed access model.
- **Practical shape:** one Blazor Server web app (Dashboard, Forecast, Spending Tracker, Review Queue, Categories/Budgets/Recurring Rules/Debt Accounts management), plus small standalone console-app importers (same solution, same EF Core models) for the SimpleFIN pull (Amex, checking, most debt accounts) and the Gmail-based Amazon parser — run manually/whenever convenient, writing into the same Postgres database the web app reads from.
- Ruled out: a pure CLI/TUI as the primary interface (the review-queue/categorization workflow is inherently a "look at a table, click something" interaction, poorly suited to a terminal) — though CLI remains fine for the small import-trigger utilities.

Functional design and tech stack are both now resolved — ready for an implementation plan.
