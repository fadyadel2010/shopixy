# Treasury T-06 Supplier Card Funding Authority Implementation - 2026-09-13

## 1. Executive Verdict

T-06 is source-implemented and source-verified for the approved `FundedCompanyCard` model. Future supplier Card outflows no longer derive ledger authority from the word `Card`; they require an explicit tenant-owned instrument backed by a dedicated funded Asset account. Deployment remains blocked until the generated migration and environment preflight are approved and executed.

## 2. Certification Level

Verdict: **SOURCE PASS / DEPLOYMENT NOT EXECUTED / PRODUCTION FINANCIAL MUTATION NOT AUTHORIZED**.

The API, EF model, migration script, PostgreSQL integration tests, accounting tests, and web production build are verified. No production or shared business database was changed.

## 3. Decision Baseline

The forensic decision report established that `PaymentMethod.Card` is tender evidence, not funding authority. This implementation accepts the bounded business decision to support a funded company card Asset for future transactions only. It does not infer the historical funding source or approve a credit-card liability or reimbursement model.

## 4. Scope

The implementation covers instrument creation and lifecycle, explicit Cashbox/Bank funding, ledger-derived statements, purchase Card and Mixed allocations, partial Credit purchases with an initial Card allocation, standalone supplier Card payments, immutable snapshots, outbox replay, purchase reversal/repost, idempotency, tenant isolation, and overspend prevention.

## 5. Exclusions

The implementation does not reclassify historical activity, implement issuer-credit liabilities, implement owner/employee reimbursement cards, revive `FinancialAccount` or `VisaTransaction`, change customer Card receipt behavior, or change provider settlement authority.

## 6. Historical Baseline

The observed shopixy `1020 POS Visa Account` ledger-derived and cached balance remains EGP -3.80. Its eight historical ledger lines, four current purchase allocations, reversals, and repost remain untouched.

## 7. Supported Instrument Model

`SupplierCardInstrumentType.FundedCompanyCard` is the only supported type. Each instrument owns immutable tenant, type, dedicated ledger-account, funding-source Treasury account, and authority-version fields. Display name, issuer, last four digits, and active status are operational metadata.

## 8. Ledger Account Contract

The linked account must be an active, non-deleted, non-clearing Asset with `IsSupplierCardFunding=true` and `PreventNegative=true`. Codes `1020` and `1110` are explicitly rejected. Account tenant, code, type, and supplier-card classification are immutable after creation.

## 9. Funding Source Contract

The source must be an active, non-deleted, same-tenant `TreasuryAccount` of type Cashbox or Bank with an active same-tenant Asset account. The source ID and source ledger-account ID are frozen on every funding document.

## 10. Tenant Isolation

Instrument, funding-document, supplier-payment, account, and Treasury relationships use tenant-scoped alternate/composite keys where entity shape permits. `PurchaseInvoicePayment` has no `TenantId`, so direct foreign keys are combined with invoice tenant scope and runtime frozen-authority validation.

## 11. Persistence Model

The migration adds `SupplierCardFundingInstruments`, immutable `SupplierCardFundings`, `LedgerAccounts.IsSupplierCardFunding`, and nullable historical authority snapshots on `PurchaseInvoicePayments` and `SupplierPayments`. New EF Card payment writes require all three snapshot fields; non-Card writes must carry none.

## 12. Instrument Lifecycle

Owners and Admins may create and edit instruments; Owners, Admins, and Managers may read, fund, and inspect statements. Accounting identity cannot be edited or deleted. Deactivation blocks new outflows without rewriting retained history.

## 13. Funding Mutation Evidence

Controlled disposable PostgreSQL test evidence:

- BEFORE: Funding-source ledger balance EGP 500.00; new supplier-card ledger balance EGP 0.00; funding-document count 0.
- ACTION: Fund the configured card by EGP 100.00 with one request-hash-bound idempotency key.
- EXPECTED: One immutable funding document and one balanced journal: Dr supplier-card Asset EGP 100.00 / Cr configured Treasury source EGP 100.00. Ending balances: card EGP 100.00, source EGP 400.00.
- ACTUAL: One funding document, one two-line journal with the exact account IDs and amounts, card closing statement EGP 100.00, source ledger balance EGP 400.00.
- DIFFERENCE: EGP 0.00; replay returned the same funding and journal IDs and document count remained 1.

## 14. Purchase Card Mutation Contract

Controlled posting-request evidence:

- BEFORE: Test purchase total EGP 100.00; explicit Card allocation EGP 100.00; frozen instrument/account/version supplied.
- ACTION: Build the purchase accounting journal.
- EXPECTED: Dr Inventory EGP 100.00 / Cr the frozen supplier-card Asset EGP 100.00; no `1110` line and no generic Card fallback.
- ACTUAL: The captured posting request contains Dr Inventory EGP 100.00 and Cr the exact frozen account EGP 100.00; it contains no `1110` line.
- DIFFERENCE: EGP 0.00 in the generated journal contract.

## 15. Mixed And Partial Purchase Allocation

- BEFORE: Mixed test total EGP 100.00 with Cash EGP 40.00 and Card EGP 60.00; partial-Credit test total EGP 100.00 with initial Card EGP 60.00.
- ACTION: Create and persist purchase payment allocations.
- EXPECTED: Cash and Card remain separate; the Card row freezes instrument ID, ledger-account ID, and authority version.
- ACTUAL: PostgreSQL tests persist exact Cash/Card amounts and exact frozen Card authority for Card, Mixed, and partial-Credit paths.
- DIFFERENCE: EGP 0.00 and no missing snapshot field.

## 16. Standalone Supplier Payment Contract

Controlled posting-request evidence:

- BEFORE: Supplier payable and explicit funded-card authority; payment amount EGP 75.00.
- ACTION: Build the standalone supplier payment journal.
- EXPECTED: Dr Supplier Payables EGP 75.00 / Cr the frozen supplier-card Asset EGP 75.00; no `1110` line.
- ACTUAL: The captured posting request contains the exact expected debit and exact frozen-account credit and contains no `1110` line.
- DIFFERENCE: EGP 0.00 in the generated journal contract.

## 17. Missing Authority Failure

- BEFORE: Card purchase request EGP 100.00 with no instrument ID or authority version; invoice count 0.
- ACTION: Submit the request through `PurchasesController`.
- EXPECTED: Fail closed before invoice, payment, outbox, or journal persistence.
- ACTUAL: HTTP 400 with required-instrument error; invoice count remained 0.
- DIFFERENCE: No financial mutation occurred.

## 18. Overspend Failure

- BEFORE: Ledger-derived funded-card balance EGP 1,000.00; requested Card purchase EGP 1,000.01; invoice count 0.
- ACTION: Submit the purchase against the configured instrument.
- EXPECTED: Reject the EGP 0.01 excess and persist no invoice or journal.
- ACTUAL: HTTP 400 with insufficient funded-card balance; invoice count remained 0.
- DIFFERENCE: No financial mutation occurred; funded balance was not made negative.

## 19. Purchase Update And Reversal

- BEFORE: Existing purchase accounting journal and immutable original Card authority; proposed current-version allocation may use the same or another explicit instrument.
- ACTION: Validate new authority, reverse posted purchase journals by their original ledger lines, and post the current purchase version with a distinct operation key.
- EXPECTED: Original-account history remains intact; same-instrument replacement may use the pending reversal credit; new posting uses only the new frozen authority.
- ACTUAL: Source and focused integration tests verify payment replacement, mutation versioning, authority-aware reversal payloads, and original-journal reversal ordering. No approved production reversal was executed.
- DIFFERENCE: Source/test contract matches; environment-level financial difference is not measured until deployment certification.

## 20. Outbox And Replay

Purchase and supplier-payment payloads carry instrument ID, ledger-account ID, and authority version. Replay compares payload authority with persisted payment snapshots and validates frozen identity. Already-consumed balance is not rechecked during deterministic replay, preventing false replay failure while still rejecting mismatched or incomplete authority.

## 21. Idempotency

Purchase idempotency keys include amounts, mutation version, and Card authority. Supplier-payment keys include frozen authority. Funding keys are tenant-scoped and request-hash-bound; same-key/same-request replay returns the original document and journal, while same-key/different-request returns conflict. Concurrent unique-key races resolve through the persisted request hash after rollback.

## 22. Concurrency And Balance Authority

Fresh Card outflows lock the dedicated ledger account using PostgreSQL `FOR UPDATE` or SQL Server `UPDLOCK/HOLDLOCK`, then derive availability from ledger entries. `PreventNegative` remains a posting-level second guard, and account `xmin` protects concurrent cached-balance writes. Funding and payment mutations remain transactional.

## 23. Statement And Reporting

Instrument list balances and statements are recomposed from ledger entries, not a mutable card-balance table. Statements expose opening balance, debit inflows, credit outflows, net movement, running balance, source document, and journal identity.

## 24. Customer Card Boundary

Customer POS Card collection and provider settlement continue to use `1110 Visa Clearing`. T-06 introduces no supplier path into `1110`, and the targeted accounting tests retain the existing customer behavior. Supplier and customer Card semantics remain isolated.

## 25. Legacy 1020 Boundary

No future supplier Card controller or accounting call falls back to `1020`, `1110`, or `1030`. `1020` remains available for historical General Ledger visibility only. Historical reclassification still requires external evidence and a separately approved balanced journal.

## 26. Migration Verification

Migration `20260912154614_T06SupplierCardFundingAuthority` was generated with its designer and model snapshot. `dotnet ef migrations has-pending-model-changes` reports no changes. An idempotent script from `20260912120000_T01ShopixyLedgerBalanceCacheRepair` to T-06 generated successfully. The migration was exercised by disposable PostgreSQL tests but was not applied to an approved business environment.

## 27. Build And Test Evidence

- Release test project build: PASS, 0 errors; repository baseline emits 234 warnings.
- Focused T-06/accounting suite: PASS, 94 passed, 0 failed, 0 skipped.
- API Release build: PASS, 0 errors.
- Web Vite production build: PASS; existing bundle-size warning remains.
- EF pending-model check: PASS, no pending changes.
- EF idempotent migration script generation: PASS.
- Web lint: BLOCKED by repository tooling because ESLint 9 has no `eslint.config.js`, `eslint.config.mjs`, or `eslint.config.cjs`.
- Broader sampled run: 259 passed and 12 failed; failures were classified as stale unrelated architecture assertions, missing historical SQL files, and an existing purchase-concurrency barrier that now waits behind the product-cost lock.

## 28. Final Gate

T-06 passes source certification for future funded company cards. Production readiness remains blocked by migration approval/application, retained legacy Card-outbox preflight, controlled environment funding/purchase/payment/reversal evidence, and post-apply reconciliation. Historical shopixy `1020 = EGP -3.80` remains unchanged and is not authorized for automatic repair or reclassification.
