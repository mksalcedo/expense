# Merchant Rules management page + direction-aware rules

Status: **built + shipped** (commit 4cedcda). Migration applied to prod. Full suite green.

UX follow-up (in progress): filter box, add-form-on-top, patch-not-reload on edit/delete,
and a per-rule "Matched" count column - all done, tests green, awaiting commit. Deferred:
group-by-category toggle (#5), overlap/shadow warnings.

Real Venmo rule split (#105 -> two direction-scoped rules) still pending - user's call to
do on the page or via the service.

## Problem

`merchant_rules` (pattern -> category) are created silently through the Review Queue's
"remember this pattern" flow and are **never shown anywhere afterward**. When a rule
mis-categorizes a transaction, the user can see the wrong result on the Transactions page
but has no way to find or fix the rule causing it.

Concrete case (2026-09-10): rule #105 `VENMO -> Venmo Credit Card Payment`. Plaid now labels
both incoming Venmo cashouts (Piano lesson income transferred to checking) and outgoing
Venmo card payments as the bare description `Venmo`, so the pattern can't tell them apart -
the $238 and $200 cashouts on 9/8 got auto-filed as card payments. For transfer-type
merchants (Venmo, Zelle, PayPal, bank transfers) the **amount sign is the meaning**, and a
pattern alone can't express that.

## Design

### 1. `MerchantRule.Direction` (nullable)

Add `Direction? Direction` to `MerchantRule` (the existing `Direction` enum: `Income` /
`Expense`).

- `null` = applies to any sign. Every existing rule stays `null` - exact current behavior,
  no migration data change beyond adding the nullable column.
- `Income` = only matches transactions with `Amount >= 0`.
- `Expense` = only matches transactions with `Amount < 0`.

A transaction's effective direction: `Amount >= 0 ? Direction.Income : Direction.Expense`
(a $0 transaction counts as Income - rare, harmless).

Rule matching (two call sites: `ApplyMerchantRuleAsync` at import, `ReapplyRulesToPendingAsync`
behind the "Re-apply Rules Now" button) both currently do
`rules.FirstOrDefault(r => MerchantPatternMatcher.Matches(searchText, r.MerchantPattern))`.
Extract a shared `MerchantRuleMatcher.FirstMatch(rules, searchText, amount)` that adds
`&& (r.Direction is null || r.Direction == effectiveDirection)`. Same change to the
retroactive-apply loop in `CategorizeTransactionAsync`.

Two rules with the same pattern and different directions is a **supported, intended** state
(`VENMO` income -> Piano, `VENMO` expense -> Venmo Credit Card Payment). No uniqueness
constraint is added (there isn't one today either). First-match-wins ordering is unchanged;
a direction-scoped rule and an any-direction rule for the same pattern - list order decides,
same as any two overlapping patterns today.

Migration `AddDirectionToMerchantRule` - just the nullable column.

### 2. `MerchantRuleService`

New service (mirrors `DepartmentMappingService`): `GetAllAsync`, `CreateAsync(pattern,
direction, categoryId)`, `UpdateAsync(id, pattern, direction, categoryId)`, `DeleteAsync(id)`.
After Create/Update, run a targeted re-resolve of pending bank transactions (the merchant-rule
half of `ReapplyRulesToPendingAsync`) and return how many were newly categorized. CRUD is by
id, not upsert-by-pattern - unlike department mappings, merchant rules legitimately duplicate
patterns.

### 3. `/merchant-rules` page + nav entry

Thin `IMerchantRulesPageProvider` (`GetAsync` -> { rules, categories }, plus create/update/
delete) over `MerchantRuleService`, same shape as `IAmazonDepartmentRulesPageProvider`.

Page: one row per rule - editable pattern text, a direction `<select>` (Any / Money in /
Money out), a category `<select>`, a Delete button; an add row at the bottom. Shows how many
pending transactions a new/edited rule just swept up. Nav entry "Merchant Rules" in the
config group (near Categories).

Not touching the Review Queue's rule-*creation* flow in this pass - it keeps creating
any-direction rules from the pattern field. (A follow-up could add a direction picker there,
but the management page is what unblocks the Venmo case: split rule #105 into two.)

## Parts (TDD)

1. **Entity + migration** - `MerchantRule.Direction`, config, `AddDirectionToMerchantRule`.
   Round-trip test first.
2. **Matching** - `MerchantRuleMatcher.FirstMatch` with the direction check; refactor the
   three call sites onto it. Tests: an `Expense`-only rule skips a positive transaction and
   vice-versa; a `null` rule matches both; income rule + expense rule for one pattern route
   opposite signs to different categories.
3. **`MerchantRuleService`** CRUD + re-resolve-pending. Tests against the real DB.
4. **`/merchant-rules` page** + provider + nav. bUnit tests: lists rules, add, edit
   direction/category, delete, "N transactions re-filed" message. NavMenu link test.
5. Migration to prod, publish, verify. Then (separately, user's call) split the real Venmo
   rule.

## Explicitly out of scope

- A direction picker in the Review Queue's create-a-rule flow.
- Rule ordering/priority UI (first-match-wins by list order stays as-is).
- Matching on raw `Description` vs normalized `Merchant` - Plaid's collapse of "VENMO
  CASHOUT ..." to "Venmo" is what forced the direction approach; description text isn't
  reliably there anymore.
