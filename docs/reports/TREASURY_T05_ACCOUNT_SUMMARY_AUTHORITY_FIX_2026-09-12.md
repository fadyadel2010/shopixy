# Treasury T-05 Account Summary Authority Fix

**Date:** 2026-09-12  
**Tenant used for live verification:** `shopixy` (`83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5`)  
**Database:** `PosEcommerceDb_Baseline`  
**Scope:** T-05 only

## 1. Outcome

`AccountSummary.IsTreasury` now derives from canonical `TreasuryAccount` authority instead of `Account.IsSystem`.

The implementation projects authoritative account IDs through one explicit same-tenant Treasury-to-ledger join. It does not issue a query per account and does not change the Account Summary response shape, balances, or other fields.

An account is classified as Treasury only when all of the following are true:

- The mapping and ledger account belong to the requested tenant.
- The mapping is active and not deleted.
- The ledger account is active, not deleted, and an Asset.
- The mapping type is `Cashbox`, `Bank`, or `Clearing`.

`Account.IsSystem` is no longer consulted by this classification path.

## 2. Implementation

Changed `FinancialReportService.GetAccountSummariesAsync` in `PointOfSale.Infrastructure/Accounting/FinancialReportService.cs`:

- Added a projected `HashSet<Guid>` of canonical Treasury account IDs.
- Joined on both `TenantId` and `AccountId`.
- Applied explicit mapping, account-state, account-type, and Treasury-type gates.
- Replaced the previous `IsTreasury = a.IsSystem` projection with membership in the authoritative ID set.
- Preserved the existing repeatable-read transaction, ledger balance aggregation, account ordering, response fields, and account inclusion behavior.

No Treasury operational service, posting service, settlement flow, Cash Flow calculation, or database schema was changed for T-05.

## 3. Regression Matrix

Eight focused tests cover the requested A-J cases in `PointOfSale.Tests/Accounting/FinancialReportServiceTests.cs`.

| Case | Scenario | Expected | Result |
| --- | --- | --- | --- |
| A | Unmapped system Asset account | `IsTreasury=false` | PASS |
| B | Mapped system Asset account | `IsTreasury=true` | PASS |
| C | Mapped non-system Asset account | `IsTreasury=true` | PASS |
| D | Foreign-tenant mapping | `IsTreasury=false` | PASS |
| E | Inactive mapping | `IsTreasury=false` | PASS |
| F | Deleted mapping | `IsTreasury=false` | PASS |
| G | Inactive mapped account | `IsTreasury=false` | PASS |
| H | Deleted mapped account | `IsTreasury=false` | PASS |
| I | Mapped non-Asset account | `IsTreasury=false` | PASS |
| J | Multiple tenants | Only requested-tenant authority is visible | PASS |

An additional assertion rejects an unknown `TreasuryAccountType` value.

## 4. Live API Evidence

Endpoint: `GET /api/financialreports/accounts`

The probe used an existing current shopixy POS session and only performed an authenticated GET. It did not invoke the development token issuer or write authentication/business state.

| Evidence | Before | After |
| --- | ---: | ---: |
| HTTP status | 200 | 200 |
| Account count | 32 | 32 |
| `IsTreasury=true` count | 32 | 6 |
| Sorted account/balance snapshot hash | `12fcba807b76ee0a8c508291becccc63` | `12fcba807b76ee0a8c508291becccc63` |

The six post-fix true codes are:

`1010`, `1030`, `1110`, `1120`, `1130`, `1140`

All other shopixy accounts are false. In particular, `1020` is false and its balance remains `-3.8000`.

## 5. Database Non-Regression

All database checks ran in repeatable-read, explicitly read-only transactions and were rolled back.

| Invariant | Before | After | Result |
| --- | --- | --- | --- |
| Ledger account count | 32 | 32 | Unchanged |
| Account balance sum | `23112.6800` | `23112.6800` | Unchanged |
| Account state hash | `6ea3fc3a3bc0aaa03aab84aac89c8e72` | `6ea3fc3a3bc0aaa03aab84aac89c8e72` | Unchanged |
| Treasury mapping count | 6 | 6 | Unchanged |
| Active Treasury mappings | 6 | 6 | Unchanged |
| Treasury state hash | `d93da01fb65756053f6c96b250eab3b6` | `d93da01fb65756053f6c96b250eab3b6` | Unchanged |
| Journal count | 53 | 53 | Unchanged |
| Journal state hash | `a8a92a1941672311029d164ca25608f4` | `a8a92a1941672311029d164ca25608f4` | Unchanged |
| Ledger line count | 114 | 114 | Unchanged |
| Debit sum | `16187.3500` | `16187.3500` | Unchanged |
| Credit sum | `16187.3500` | `16187.3500` | Unchanged |
| Ledger line state hash | `c36525ecd717539b02caf6456638a49e` | `c36525ecd717539b02caf6456638a49e` | Unchanged |

No account, mapping, journal, ledger line, settlement, transfer, or balance was created or modified.

## 6. Cross-Tenant Evidence

The canonical predicate was evaluated read-only across all seeded tenants.

| Tenant | Accounts | Legacy `IsSystem` positives | Canonical positives | Active mappings |
| --- | ---: | ---: | ---: | ---: |
| ahramstore | 32 | 32 | 6 | 6 |
| antonnewstore | 32 | 32 | 6 | 6 |
| antonstore | 32 | 32 | 6 | 6 |
| greatone | 32 | 32 | 6 | 6 |
| highquality | 32 | 32 | 6 | 6 |
| shopixy | 32 | 32 | 6 | 6 |

The query joins mapping authority using each tenant's own `TenantId` and account ID. The multi-tenant regression test also proves that a foreign mapping/account is not returned or used for the requested tenant.

## 7. Verification

| Check | Result |
| --- | --- |
| New Account Summary regression tests | PASS, 8/8 |
| Accounting, Treasury authority, reporting, tenant-isolation, and anti-parallel-ledger selection | PASS, 48/48 |
| Backend solution build | PASS, 0 warnings, 0 errors |
| Frontend production build | PASS |
| Live API provider/query translation | PASS, HTTP 200 |
| `git diff --check` | PASS |

The frontend build retained the existing large-chunk warning; no frontend source was changed for T-05.

## 8. Scope Boundaries

- T-06 remains intentionally unresolved and unchanged.
- Account `1020` remains unmapped, classified as an Asset, and carries its existing ledger-backed balance of `-3.8000`.
- No Cash Flow logic was changed by this T-05 implementation.
- No Treasury operation-specific type rule was broadened or narrowed.
- No migration or data repair was added.
- A legitimate shopixy browser session was not available, so browser certification was not performed. The tenant-scoped authenticated API and read-only SQL evidence provide the runtime verification for this change.
