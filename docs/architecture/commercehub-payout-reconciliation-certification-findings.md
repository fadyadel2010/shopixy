# PASS 16.5D Payout & Reconciliation Certification

Date: 2026-06-02

Verdict: CommerceHub payout and reconciliation runtime is production-certifiable for inspected SQL/ledger paths. Public launch remains not ready until the broader PASS 15.6/16.1B deployment, retention, alert-pack, backup/restore, SMTP/session, and PostgreSQL-backed chaos blockers are closed.

## Payout Inventory

| Entity or surface | Authority owner | Source of truth | Derivative consumers |
|---|---|---|---|
| `CommerceHubVendorPayoutMethod` | Merchant payout identity service | Vendor-scoped PostgreSQL row keyed by `VendorId` | Merchant earnings page, Super Admin payout queue |
| `CommerceHubVendorPaymentMethod` | Platform collection method service | PostgreSQL platform collection destination | Merchant COD repayment UI |
| `CommerceHubVendorPaymentRequest` / `CommerceHubVendorPaymentProof` | Vendor repayment workflow and Super Admin review | PostgreSQL request, proof hash, transfer reference, approval state | Debt dashboard, Super Admin repayment review |
| `CommerceHubVendorEarning` | Financial workflow and payout services | PostgreSQL earning rows tied to sub-order financial snapshots | Payout eligibility, payout queue |
| `CommerceHubPayout` / `CommerceHubPayoutItem` | Payout services | PostgreSQL payout row plus earning allocations, handoff token, external evidence, completion fingerprint | Merchant payout history, Super Admin queue, reconciliation |
| `CommerceHubFinancialTransaction` | CommerceHub financial ledger | PostgreSQL append-only ledger rows | Wallets, dashboards, reconciliation, readiness gates |
| `CommerceHubFinancialReconciliationReport` | Reconciliation service | PostgreSQL report with mismatch details | Finance dashboard, health checks, incident response |
| `CommerceHubFinancialAuditLog` / `CommerceHubFinancialOperationAudit` | Finance audit services | Append-only SQL evidence | Forensics and operator review |

## Payout Method Findings

Merchant payout identities are owned by the vendor tenant. `VendorPayoutMethodsController` resolves `TenantId` from trusted tenant context and passes it to `CommerceHubVendorPayoutMethodsService`; the request body does not choose the vendor. The service lists and creates only rows where `method.VendorId == vendorId`, verifies the tenant exists, validates method type, records `CreatedBy`, and only supports create/list/default behavior. No merchant update/delete path was found.

Super Admin can view all vendor payout identities through the payout queue. That is platform execution visibility, not merchant authority. Execution revalidates `method.Id == request.PayoutMethodId && method.VendorId == vendorId` before snapshotting the destination into the payout.

## Merchant Payout Findings

Merchant payout creation is SQL-authoritative. It requires idempotency, vendor payout governance, outward-money readiness, serializable transaction scope, row locks over selected earnings, available earning status, settlement delay expiry, single currency, positive net amount after negative adjustments, and ledger confirmation before reserving earnings into a pending payout.

Processing and completion are externally fenced: one processing/reconciliation-required payout per vendor, committed handoff token, external transfer idempotency key, terminal evidence, evidence uniqueness, immutable completion fingerprint, and ledger-backed state verification. Failed payouts cannot release a processing handoff unless non-execution proof is supplied.

## Platform Collection Findings

COD commission collection remains platform receivable debt. Vendor repayments reduce debt only through vendor payment requests, proof evidence, Super Admin approval, external transfer reference, mirrored ledger posting, and audit logs. Duplicate collection is fenced by request idempotency, proof hash checks, transfer-reference uniqueness, and approval integrity checks.

## Payout Authority Findings

Authority is explicit:

- Merchants own payout identity submission and tenant-scoped payout request creation.
- Super Admin owns platform payout execution/recognition through `SuperAdminPayoutsService`.
- Banks/providers own external transfer execution evidence.
- PostgreSQL ledger owns internal financial truth after recognition.

The Super Admin execution path requires current platform actor authority, payout method/vendor binding, external evidence, platform cash, payable/eligible amount checks, final readiness inside the serializable transaction, ledger posting, and operation audit evidence.

## Reconciliation Findings

`FinancialReconciliationService` validates orders, sub-orders, promo cross-checks, refunds, orphan ledger rows, external money lifecycle, payouts, and aggregates. Payout reconciliation explicitly detects completed payouts missing external evidence, completed payout ledger debit mismatch, and failed payouts carrying external transfer evidence. Reconciliation can prove internal SQL correctness for orders, ledger rows, balances, settlements, payouts, and refunds. It cannot prove bank/provider outcomes without external evidence.

## Payout Failure Findings

Processing payouts that exceed the safe external handoff window are quarantined by `CommerceHubPayoutReconciliationHostedService` into `ReconciliationRequired` under a transaction and payout boundary. Completion after handoff reconciles external truth; failure after handoff requires confirmed non-execution proof. This prevents release, duplicate payout, or silent rollback when external outcome is unknown.

## Refund Interaction Findings

Refund services block overlap with processing payout earnings, validate cumulative refund totals, preserve reconciliation-required states for uncertain external outcomes, and convert post-payout vendor impact into debt/negative adjustment rather than rewriting completed payout history. No inspected path lets refunds corrupt completed payout ledger facts.

## Dashboard Findings

Merchant and Super Admin payout dashboards derive from SQL ledger, payout rows, payout read snapshots, risk flags, and reconciliation data. Queue execution state is backend-derived through `CanRecordCompletedPayout`, `ExecutionBlockedCode`, and `ExecutionBlockedReason`; frontend enablement is not payout authority.

## Recovery Findings

Replay and recovery cannot duplicate payouts on certified paths because payout idempotency, one-processing-per-vendor constraints, external handoff tokens, globally unique external evidence, completion fingerprints, and ledger financial fingerprints collapse duplicates or fail closed. Recovery moves stale processing records to `ReconciliationRequired`; it does not mark money completed by guessing.

## Auditability Findings

Payout incidents are reconstructable from payout rows, payout items, earning allocations, external handoff token/time, external evidence, completion fingerprint, financial transactions, financial audit rows, operation audit rows, Super Admin audit rows, reconciliation reports, and health-check evidence. Deployment-level WORM/SIEM retention remains governed by PASS 15.6 blockers.

## Chaos Findings

Inspected controls address double payout, evidence reuse across vendors, duplicate collection, payout identity cross-tenant mutation, stale payout completion, failed payout retry corruption, refund/payout overlap, settlement/payout race, reconciliation drift, dashboard mismatch, replay duplication, recovery duplication, and payout resurrection. Remaining chaos debt is a PostgreSQL-backed concurrent harness for payout/refund/settlement/replay/recovery races.

## Monitoring Findings

Baseline monitoring exists through `PayoutExternalTruthReadinessHealthCheck`, reconciliation reports, payout risk flags, finance dashboards, audit logs, readiness gates, and alert hooks. Production alert packs for payout evidence gaps, stale processing payouts, reconciliation-required payouts, ledger drift, and execution blocking remain launch prerequisites.

## Runtime Hardening Performed

No new runtime code hardening was required in PASS 16.5D. The inspected runtime already contained the required hardening from prior finance, emergency, settlement, COD risk, and external-money recovery passes: serializable payout transactions, row locks, ledger confirmation, one-processing-per-vendor constraints, external handoff tokens, unique external evidence, completion fingerprints, readiness gates, refund/payout overlap blocking, reconciliation quarantine, and Super Admin payout execution evidence requirements.

## Regression Freeze Added

Added:

- `CommerceHubPayoutCertificationTests`
- `CommerceHubPayoutIdentityCertificationTests`

Extended:

- `CommerceHubFinancialReconciliationCertificationTests`

Frozen behavior: payout ownership, payout identity ownership, merchant payout lifecycle, platform collection lifecycle, reconciliation behavior, refund/payout interaction, recovery behavior, auditability, dashboard authority, and Super Admin payout execution binding.

## Architecture Updates

This file plus `.ai-context` PASS 16.5D entries are the current authority record for payout ownership and reconciliation. Future payout, payout-method, vendor repayment, payout dashboard, refund interaction, reconciliation, recovery, monitoring, or Super Admin payout execution changes must update these tests, this document, and `.ai-context` together.

## Remaining Debt

- PASS 15.6 public-launch blockers remain.
- Production payout alert packs and dashboards remain required.
- External WORM/SIEM retention for payout/audit evidence remains required.
- PostgreSQL-backed payout/refund/settlement/replay/recovery chaos harness remains required.
- Production query-plan/capacity review for payout queue and reconciliation queries remains required.
- Merchant payout-method edit/delete/reverification workflows are not implemented; current certified scope is create/list/default plus Super Admin execution visibility.

## Required Answers

1. Who owns merchant payout identities? Merchant/vendor tenant SQL authority owns them through `CommerceHubVendorPayoutMethod.VendorId`.
2. Can payout identities leak across tenants? Merchants cannot list or create another tenant's identities on inspected paths. Super Admin can view all identities for platform execution.
3. Can merchants receive duplicate payouts? Not on certified paths: earning reservation, one processing/reconciliation slot, idempotency, unique external evidence, and ledger fingerprints fence duplicates.
4. Can the platform collect duplicate commission payments? Not on certified paths: repayment requests require idempotency, proof/evidence checks, approval integrity, and ledger posting.
5. Can payout execution target the wrong merchant? Not on certified Super Admin execution paths because payout method id must belong to the route `vendorId`, and payout rows snapshot that vendor/method binding.
6. Can reconciliation prove payout correctness? It can prove internal SQL/ledger correctness and evidence presence; it cannot prove external bank/provider truth without external evidence.
7. Can refunds corrupt completed payouts? No inspected path permits that. Refunds block payout overlap and model post-payout impact as debt/adjustment.
8. Can replay/recovery duplicate payouts? No on certified paths; replay must match idempotency/evidence/fingerprint, and recovery quarantines unknown outcomes.
9. Are payout incidents reconstructable? Yes for inspected runtime paths through SQL payout, ledger, audit, operation, Super Admin, and reconciliation evidence.
10. Is CommerceHub payout lifecycle production-ready? Runtime is production-certifiable for inspected paths; public launch remains blocked by broader deployment, monitoring, retention, backup/restore, and chaos-proof prerequisites.
