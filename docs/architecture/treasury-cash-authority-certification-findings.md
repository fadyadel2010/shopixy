# PASS T1 Treasury Cash Authority Certification

Date: 2026-06-12

Verdict: Treasury Cash Authority is production-certifiable for the inspected runtime paths after ledger mutation, reversal lineage, treasury identity, and tenant co-ownership hardening.

## Cash Findings

PostgreSQL `LedgerAccounts`, `LedgerJournalEntries`, `LedgerLines`, and active same-tenant `TreasuryAccounts` own treasury cash position and treasury account identity. `LedgerAccounts.CurrentBalance` is a transactionally maintained ledger materialization, not an independent balance authority.

Available cash is the sum of active cashbox and bank treasury asset balances. Pending or held cash is represented by SQL clearing/liability lineage, not mutable dashboard fields. Reserved payout, refund, and settlement state remains owned by those separate certified authorities and cannot directly rewrite treasury cash.

## Balance Findings

Direct `CurrentBalance` persistence outside `FinancialPostingService` is rejected by `ApplicationDbContext`. Treasury balance, multi-balance, account-list, dashboard, and movement reads require active same-tenant treasury mappings to active asset ledger accounts. Route or DTO account codes cannot relabel revenue, expense, foreign-tenant, inactive, or unmapped ledger accounts as cash.

## Ledger Findings

Every cash mutation posts balanced ledger lines and updates account materializations in one SQL transaction. Ledger lines are append-only. Journal identity and financial lineage are immutable after posting; only the posting authority may record reversal status.

## Replay Findings

Posting idempotency is tenant/key unique and rechecked inside the transaction. Reversal posting and original-journal reversal marking now share one serializable transaction. Reversal journals retain explicit original-journal lineage, and treasury reads include both the reversed original and its posted reversing journal so history recomputes to current cash.

## Projection Findings

Redis, MongoDB, Elasticsearch, dashboards, DTOs, frontend totals, legacy cashbox/bank transaction rows, and cached read models are derivative only. Treasury runtime reads authoritative SQL ledger and treasury mappings directly.

## Cross-System Findings

CommerceHub, Affiliate, Storefront, and Service Marketplace cannot create treasury cash through their projections or DTOs. Their payment, refund, settlement, payout, and reservation authorities may trigger reviewed accounting postings, but treasury cash changes only when SQL ledger authority posts balanced lines.

## Isolation Findings

Treasury APIs derive tenant scope from trusted tenant context. Runtime reads use explicit tenant predicates even when query filters are bypassed for worker compatibility. The treasury-to-ledger relationship is now a composite `(TenantId, AccountId)` foreign key to `(TenantId, Id)`, and the migration fails closed if existing cross-tenant mappings require reconciliation.

## Historical Findings

Historical cash lineage is append-only ledger evidence. Reversals do not erase original movements. `BalanceAfter`, journal source identity, posting time, user identity, and reversal linkage remain SQL evidence. Stale snapshots cannot resurrect cash because current reads recompose from current SQL treasury identity and ledger rows.

## Adversarial Findings

- Cash inflation and suppression through direct balance mutation are blocked by the central persistence guard.
- Ledger bypass is blocked because treasury operations require posting authority and active treasury asset identity.
- Replay cannot create cash because journal idempotency and serializable posting converge.
- Projection poisoning cannot become cash authority.
- Settlement and payout replay remain bounded by their previously certified SQL authorities and ledger posting.
- Tenant guessing and forged treasury mappings cannot expose foreign balances.
- DTO and route account-code tampering cannot turn non-treasury accounts into cash reads or writes.

## Runtime Hardening Performed

- Added `LedgerMutationScope` and central persistence guards for ledger balances, journal reversal state, account identity, and treasury mapping identity.
- Made reversal posting plus original reversal marking atomic and explicit in lineage.
- Included reversed originals in treasury movement and dashboard ledger history.
- Made treasury reads worker-safe with explicit tenant predicates and `IgnoreQueryFilters`.
- Restricted treasury reads and writes to active same-tenant treasury asset mappings.
- Added composite treasury/ledger tenant co-ownership schema enforcement with fail-closed migration preflight.

## Exact Runtime Files Modified

- `PointOfSale.API/Extensions/TreasuryQueryExtensions.cs`
- `PointOfSale.API/Controllers/DashboardController.cs`
- `PointOfSale.API/Controllers/ReportsController.cs`
- `PointOfSale.Core/Entities/Accounting/JournalEntry.cs`
- `PointOfSale.Infrastructure/Accounting/FinancialReportService.cs`
- `PointOfSale.Infrastructure/Accounting/FinancialPostingService.cs`
- `PointOfSale.Infrastructure/Accounting/LedgerMutationScope.cs`
- `PointOfSale.Infrastructure/Analytics/StorefrontAnalyticsAuthorityReader.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
- `PointOfSale.Infrastructure/Data/Configurations/AccountConfiguration.cs`
- `PointOfSale.Infrastructure/Data/Configurations/TreasuryAccountConfiguration.cs`
- `PointOfSale.Infrastructure/Migrations/20260612120000_TreasuryCashAuthorityHardening.cs`
- `PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- `PointOfSale.Infrastructure/Treasury/TreasuryOperationService.cs`
- `PointOfSale.Infrastructure/Treasury/TreasuryReadService.cs`

## Regression Freezes

- `PointOfSale.Tests/Architecture/TreasuryCashAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/PosFinancialTreasuryAuthorityCertificationTests.cs`

Focused result: 19 passed, 0 failed. Full solution build passed with 0 warnings and 0 errors.

## Required Answers

1. Who owns treasury cash authority? PostgreSQL treasury identity plus double-entry ledger journals, lines, and ledger-maintained account balances.
2. Can cash be inflated? Not through certified runtime paths.
3. Can cash be suppressed? Not through certified runtime paths; reversals preserve both original and reversing lineage.
4. Can balances diverge from ledger truth? Direct mutation is blocked, and reversal/history reads now use effective ledger lineage.
5. Can replay create cash? No; tenant-scoped idempotency and serializable posting converge.
6. Can tenant leakage occur? Not through certified treasury reads or writes after composite co-ownership hardening.
7. Were runtime code changes required? Yes.
8. Is Treasury Cash Authority production-certifiable? Yes, after applying the treasury cash authority migration.
