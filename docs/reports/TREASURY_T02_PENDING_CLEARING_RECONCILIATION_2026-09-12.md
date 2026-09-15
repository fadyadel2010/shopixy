# Treasury T-02 Pending Clearing Reconciliation - 2026-09-12

## 1. Decision

**T-02 classification: Case A, derivative cache drift.** The former EGP 2,040.00 overstatement was not an open settlement obligation and required no new journal. T-01 repaired the affected caches from the immutable ledger; shopixy Treasury and Clearing Aging now both report EGP 55.00 pending.

## 2. Scope

This follow-up addresses only T-02 from the Phase 1 Treasury audit: determine whether the EGP 2,040.00 gap represented stale presentation state or missing settlement/accounting facts. T-03 through T-09 remain outside this change.

## 3. Tenant Boundary

All financial evidence in this report is for:

| Field | Value |
| --- | --- |
| Tenant | `shopixy` |
| Tenant ID | `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5` |
| Currency | EGP |
| Database | `PosEcommerceDb_Baseline` |

The separate `greatone` browser session was not used as shopixy balance evidence.

## 4. Evidence Method

PostgreSQL reconciliation ran in a repeatable-read transaction with `SET TRANSACTION READ ONLY`. Queries carried the shopixy tenant ID explicitly and inspected current business records, accounting outbox rows, immutable journals and lines, settlement rows, Treasury mappings, and CommerceHub operational finance rows. Authenticated API checks used a shopixy-scoped development token and read-only GET endpoints.

## 5. Accounting Authority

Current business rows authorize posting. `LedgerJournalEntries` and `LedgerLines` are accounting/reporting authority. `LedgerAccounts.CurrentBalance` is a denormalized cache that must equal the ledger-derived normal balance. Treasury dashboard pending clearing reads that cache (`TreasuryReadService.cs:335-349`), while Clearing Aging recomputes from journal lines (`FinancialReportService.cs:505-594`).

## 6. Original Discrepancy

Before T-01, Treasury reported EGP 2,095.00 and Clearing Aging reported EGP 55.00. The difference decomposed exactly as:

| Account | Old cache | Ledger | Drift |
| --- | ---: | ---: | ---: |
| 1110 Visa Clearing | 55.00 | 55.00 | 0.00 |
| 1120 Stripe Clearing | 0.00 | 0.00 | 0.00 |
| 1130 COD Clearing | 400.00 | 0.00 | 400.00 |
| 1140 Wallet Clearing | 1,640.00 | 0.00 | 1,640.00 |
| **Total** | **2,095.00** | **55.00** | **2,040.00** |

## 7. T-01 Repair State

Migration `20260912120000_T01ShopixyLedgerBalanceCacheRepair` is applied. It locks ledger account, journal, and line tables for the repair, recomputes selected shopixy caches from posted/reversed-status lines, updates only mismatches, and does not create or alter journal history.

## 8. Current Clearing Parity

The post-repair database result is:

| Account | Current cache | Ledger-derived balance | Lines |
| --- | ---: | ---: | ---: |
| 1110 Visa Clearing | 55.00 | 55.00 | 3 |
| 1120 Stripe Clearing | 0.00 | 0.00 | 0 |
| 1130 COD Clearing | 0.00 | 0.00 | 7 |
| 1140 Wallet Clearing | 0.00 | 0.00 | 0 |

All four accounts are active, non-deleted clearing accounts with active same-tenant Treasury mappings.

## 9. Visa EGP 55 Lineage

Visa EGP 55.00 is a real ledger-backed balance, not part of the T-02 overstatement:

| Date | Source | Debit | Credit | Effect |
| --- | --- | ---: | ---: | ---: |
| 2026-09-10 | `INV-20260910-0025`, card sale | 110.00 | 0.00 | 110.00 |
| 2026-09-10 | `INV-20260910-0026`, mixed card portion | 55.00 | 0.00 | 55.00 |
| 2026-09-11 | `SR-20260911-1003`, card refund | 0.00 | 110.00 | -110.00 |
| **Net** | | | | **55.00** |

`SR-20260911-1004` returned the mixed invoice to customer credit, so it did not credit Visa Clearing. No Visa settlement batch exists. The remaining EGP 55.00 therefore remains a valid pending card-clearing amount as of 2026-09-12.

## 10. Stripe Lineage

Stripe Clearing has no ledger lines and no current balance. The CommerceHub Stripe order observed in the database has failed initialization attempts because Stripe integration is not configured; it has no successful provider transaction and does not authorize a shopixy Stripe clearing balance.

## 11. COD Ledger Lineage

Three POS COD collections debit account 1130 by EGP 410.00 each, totaling EGP 1,230.00. Four pre-settlement refunds credit account 1130 by EGP 200.00, EGP 210.00, EGP 410.00, and EGP 410.00, also totaling EGP 1,230.00. The authoritative COD balance is therefore zero.

## 12. COD Opening Offset Proof

The first COD debit was EGP 410.00 but its posting-time `BalanceAfter` was EGP 810.00. Each later fully refunded COD cycle returned `BalanceAfter` to EGP 400.00. This proves that the old EGP 400.00 existed in the cache before current COD journal history and was not produced by the seven authoritative COD lines.

## 13. Wallet Lineage

No wallet order, payment intent, provider transaction, journal, ledger line, settlement row, refund, accounting outbox message, or legacy Visa/Treasury transaction supports EGP 1,640.00. Account 1140 had no ledger history. Its former value was cache-only state.

## 14. Settlement Evidence

Shopixy has zero `PaymentSettlementBatches` and zero `PaymentSettlementOrders`. The three collected POS COD orders were fully refunded before settlement and have no settlement membership. No missing or failed accounting outbox row explains either stale amount: all 35 shopixy accounting outbox rows are processed and none are failed.

## 15. CommerceHub Boundary

CommerceHub COD projections are a separate marketplace finance domain and do not feed POS COD Clearing account 1130. The superficially matching EGP 400.00 vendor event occurred on 2026-09-01, after the last POS COD line on 2026-08-31. Its operational transaction is a Vendor/Cash `CommerceHubFinancialTransaction`; current GL mapping sends Vendor/Cash entries to Bank account 1030 with Treasury Suspense 2030 as offset (`CommerceAccountingService.cs:1840-1930`). Clearing account 1130 has no CommerceHub line.

## 16. Dashboard Semantics

`TreasuryReadService.GetDashboardAsync` loads active accounts for the requested tenant, selects nonzero `IsClearing` account caches, and sums those values. After T-01 restored the cache invariant, the dashboard result is consistent with the ledger without changing dashboard behavior.

## 17. Clearing Aging Semantics

`FinancialReportService.GetClearingAgingAsync` includes posted and reversed-status journal lines through the requested as-of date and computes normal account balances. It currently nets each clearing account and assigns age buckets from account movements; it is not matched settlement-lot aging. That T-07 semantic limitation does not affect the EGP 55.00 parity conclusion.

## 18. API Verification

Authenticated shopixy API results after T-01:

| Endpoint | Result |
| --- | --- |
| `GET /api/treasury/accounts` | Clearing balances 55.00, 0.00, 0.00, 0.00 |
| `GET /api/treasury/balances?accountCodes=1110,1120,1130,1140` | Visa 55.00; all others 0.00 |
| `GET /api/treasury/dashboard?from=2026-07-01&to=2026-09-12` | `totalPendingClearing=55.00` |
| `GET /api/financialreports/clearing-aging?asOf=2026-09-12` | `totalPending=55.00`, Visa only |

No shopixy browser-content certification is claimed.

## 19. Regression Coverage

`FinancialReportServiceTests.ClearingDashboardAndAging_AgreeWithTenantLedger_WhenCachesAreReconciled` creates controlled Visa, COD, Wallet, revenue, collection, and refund fixtures. It derives the expected pending amount from authoritative ledger lines, asserts equality with both dashboard and Clearing Aging, compares returned clearing account codes, and proves that a foreign tenant's stale EGP 2,040.00 clearing cache is excluded. It does not hardcode live shopixy balances as the assertion authority.

The existing T-01 PostgreSQL integration test independently proves targeted cache rebuilding, tenant isolation, no journal/line creation, idempotent reapplication, and corrected no-negative control behavior.

## 20. Safety And Residual Risk

No settlement, transfer, journal, ledger line, source transaction, or business-data mutation was performed for T-02. The repair was already applied by T-01. Continuous tenant-wide cache-parity monitoring remains desirable, but adding a new health subsystem is outside this bounded reconciliation. T-03 through T-09 remain open as recorded by Phase 1.

## 21. Closure

| Gate | Result |
| --- | --- |
| EGP 2,040.00 decomposed | PASS |
| Source and settlement lineage checked | PASS |
| COD and Wallet shown not to be open obligations | PASS |
| CommerceHub boundary separated | PASS |
| Cache equals clearing ledger after T-01 | PASS |
| Treasury dashboard equals Clearing Aging | PASS |
| Cross-tenant exclusion covered | PASS |
| New financial journal required | NO |

**T-02 is closed as Case A.** The old EGP 2,040.00 was stale derivative cache state: EGP 400.00 in COD Clearing plus EGP 1,640.00 in Wallet Clearing. The only current shopixy pending-clearing amount is the ledger-backed EGP 55.00 Visa balance.
