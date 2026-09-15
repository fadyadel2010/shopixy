# Treasury T-06 Pre-Deployment Gate - 2026-09-13

## 1. Gate Decision

T-06 is approved for a controlled migration deployment. This decision authorizes only migration `20260912154614_T06SupplierCardFundingAuthority` through the reviewed deployment process; it does not record or authorize a business-data mutation, instrument creation, funding, purchase, payment, reversal, journal, or historical reclassification.

## 2. Gate Scope

The gate covers migration reproducibility, source/model alignment, retained-data compatibility, tenant isolation, legacy outbox replay, lock ordering, focused regression evidence, and the absence of automatic supplier Card authority assignment. T-01 through T-05 and T-07 through T-10 behavior is outside the implementation scope and was not deliberately changed by this gate work.

## 3. Authority Decision

Future supplier Card outflows require `SupplierCardInstrumentType.FundedCompanyCard`. The authority snapshot is the tenant-owned instrument ID, its dedicated Asset ledger-account ID, and its authority version. Tender label `Card` is not funding authority.

## 4. Prohibited Fallbacks

No future supplier Card path may infer or assign `1020`, `1110`, or `1030`. `1020` remains historical General Ledger evidence, `1110` remains customer Card clearing authority, and `1030` can be a funding source only when explicitly selected through a same-tenant active Bank Treasury account.

## 5. Environment Identity

The inspected Development target was `PosEcommerceDb_Baseline` on PostgreSQL at the configured Development endpoint. Credentials are intentionally omitted. The target was audited read-only and was not migrated.

## 6. Read-Only Safety

The target audit ran under `REPEATABLE READ`, executed `SET TRANSACTION READ ONLY`, and rolled back. No real instrument, funding document, journal, payment, purchase, balance, seed, account classification, `1020`, or customer `1110` mutation was performed.

## 7. Migration State

The latest applied target migration is `20260912120000_T01ShopixyLedgerBalanceCacheRepair`. T-06 is the only pending migration, and none of its tables or columns is present on the target. This is the expected pre-deployment state.

## 8. Migration Content

The reviewed T-06 migration adds seven columns, two tables, two alternate keys, three check constraints, ten foreign keys, and thirteen explicit indexes. It contains no data bootstrap, account creation, instrument creation, funding creation, historical snapshot backfill, journal rewrite, balance rewrite, or account reclassification.

## 9. Model Reproducibility

`dotnet ef migrations has-pending-model-changes` returned `No changes have been made to the model since the last migration.` The idempotent script from T-01 to T-06 generated successfully and contains the expected schema operations guarded by the EF migration-history check.

## 10. Disposable Migration Proof

Unique clone `t06_predeploy_gate_20260913_14908_ad925ed2e51f4a42abaef` was created from the Development baseline. T-06 applied successfully twice, exactly one T-06 migration-history row remained, and the clone was destroyed with `DISPOSABLE_DATABASE_DESTROYED=True`.

## 11. Protected Data Fingerprint

The protected snapshot SHA-256 was `B06ED877589694450234F943EB7BCEB3954134AF6553846FDC98529935BB4D90` before the migration, after the first apply, and after the second apply. Protected business data was unchanged.

## 12. Automatic Assignment Check

After disposable migration, supplier-card instrument rows, funding rows, and designated supplier-card account rows were all zero. Four historical purchase Card rows retained null instrument, ledger-account, and version snapshots. No legacy authority was invented.

## 13. Historical Baseline

The read-only target audit fingerprint remained `7913292C215C306A85232C1FA75821C1EAE6B9E82F13ECEF7A9EC0B51F0512E7`. Shopixy account `1020 POS Visa Account` remained ledger-derived and cached at EGP `-3.80` across eight retained ledger lines. Historical reclassification is not authorized.

## 14. Outbox Preflight

All 121 accounting outbox messages are processed; none is pending or failed. The six retained supplier Card messages consist of four `PosPurchase` and two `PurchaseReversal` messages. Each has an exact journal ID and intentionally has no T-06 authority snapshot.

## 15. Legacy Replay Compatibility

Legacy `PosPurchase` and `PurchaseReversal` replay accepts only an exact existing journal matching `JournalId`, `TenantId`, `BusinessEntityId`, and `SourceType == "PurchaseInvoice"`. It marks the message processed without dispatch, authority backfill, posting, or reversal. The regression theory covering both event types passed.

## 16. Tenant Isolation

Instrument, funding, supplier-payment, ledger-account, and Treasury relationships use tenant-scoped keys where supported. The disposable cross-tenant funding insertion was rejected by PostgreSQL. Runtime validation also binds purchase-payment snapshots to the invoice tenant.

## 17. Funding And Balance Authority

Fundings are immutable, tenant-scoped, request-hash-bound documents posting Dr dedicated supplier-card Asset and Cr the explicitly configured Cashbox or Bank account. Available balances and statements are ledger-derived. `PreventNegative` and ordered ledger-account locking reject overspend.

## 18. Replay And Idempotency

New outbox payloads freeze and validate instrument ID, ledger-account ID, and authority version. Funding, purchase, and supplier-payment idempotency binds keys to request identity; same-request replay converges, while changed requests conflict. Reversal uses original journal lines and repost uses current explicit authority.

## 19. Lock-Order Certification

The focused Release suite includes the corrected `PI_CON_01` product-cost-before-stock lock ordering and `PR_CON_02` purchase-return replay scenario. Both passed in the final focused run. No T-06 lock-order regression was observed.

## 20. Focused Test Evidence

The final fresh Release command selected T-06 integration, purchase-payment integrity, accounting outbox, commerce accounting, financial reporting, Treasury transfer, settlement authority, migration replay, and the two concurrency scenarios. Result: 119 passed, 0 failed, 0 skipped. The separate current vendor-debt run passed 2 of 2.

## 21. Broader Failure Classification

Fresh `CrossSystemCriticalRiskHardeningTests` result was 167 passed and 6 failed. The four other historical failures now pass. The six remaining failures inspect unchanged `HEAD` files and assert obsolete source shapes rather than failed T-06 behavior:

- Affiliate placeholder expects an empty payment-method insert, while current pending/zero-paid behavior stores the event method.
- Order-status guard expects a global enabled boolean, while current default-on scoped disable depth is fail-closed and concurrency-safe.
- Paymob expects direct `AuthenticatePaymobAsync`, while current code commits local intent before delegated runtime provider initiation.
- Product mutation expects transaction/event code in controller methods, while current authority is in `ProductAuthoringOrchestrator` and stages events before commit.
- Variant mutation expects an inline commit, while the endpoint is deprecated and unreachable legacy completion commits through its helper.
- Navigation publish expects direct cache invalidation, while it stages the authoritative event before save and projection handling is event-driven.

The historical vendor-debt artifact's third test was another passing selection: only two named vendor-debt tests failed there, current source and `HEAD` define two facts, current discovery lists two, and both now pass. The attempted full-suite result is excluded because EF `ManyServiceProvidersCreatedWarning` caused a cascade and therefore was not valid certification evidence.

## 22. Build And Operational Conditions

API Release build passed with 0 warnings and 0 errors. The Vite production build passed with the existing large-chunk warning. `git diff --check` found no whitespace errors and only line-ending notices. Web lint remains tooling-blocked because ESLint 9 cannot find `eslint.config.(js|mjs|cjs)`; this is unrelated to T-06. Deployment must retain pre/post migration evidence, verify T-06 is the sole pending migration, and must not create or fund an instrument without separately approved business instructions.

## 23. Final Verdict

GO
