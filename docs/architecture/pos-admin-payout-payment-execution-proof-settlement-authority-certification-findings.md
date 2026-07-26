# PASS AF-PA6.3 Payment Execution, Proof of Transfer & Settlement Authority Certification Findings

Payment execution is production-certifiable after backend paid-executor audit hardening.

Execution findings:
- Payment Execution begins in `AdminAffiliateController.MarkPayoutPaid`, not in the frontend. The endpoint reloads the store-scoped SQL payout request, requires `Approved`, derives amount from `ApprovedAmount`, requires payment reference, resolves current admin identity, and calls `AffiliateSettlementOrchestrator.ExecuteAffiliatePayoutAsync`.
- Frontend `adminAffiliateApi.markPayoutPaid` submits only request id in the route plus `paymentReference` and optional `adminNotes`.

Receipt/proof findings:
- Paid execution requires external payment reference and verified external transfer evidence when production-safe options require it.
- `AffiliatePayoutTransferEvidence` is uniquely keyed by rail/reference and store/currency/amount/fingerprint; `AffiliatePayoutTransferAllocation` binds that evidence to one payout reference id with idempotency.
- Proof cannot belong to another payout because allocation idempotency and evidence over-allocation/reference checks fail closed.

Settlement findings:
- Settlement begins inside `AffiliateSettlementOrchestrator.ExecutePayoutCoreAsync` after payout-request destination validation, evidence admission, balance validation, and before append-only withdrawal/paid ledger writes.
- `AffiliateBalanceLedger.Withdrawal`, `AffiliateCommissionLedger.Paid`, paid accounting, transfer allocation, and request `Paid` transition are committed in one serializable transaction.

Audit findings:
- Runtime now persists `PaidBy` on `AffiliatePayoutRequest` and exposes it in admin payout review reads.
- Payment audit includes request id, affiliate id, store id, approved amount, currency, payment reference, paid timestamp, paid executor, transfer evidence/allocation, balance withdrawal, commission paid rows, and accounting source reference.

Concurrency findings:
- Double click, replay, retry, simultaneous admins, and server restart re-enter the same backend orchestrator boundary.
- Duplicate payment is blocked by request status, serializable isolation/retry, external evidence/reference uniqueness, transfer-allocation idempotency, balance-ledger withdrawal idempotency, and commission paid uniqueness.

Failure findings:
- Missing/invalid reference, missing executor identity, non-approved request, amount mismatch, wallet snapshot change, insufficient balance, evidence mismatch, reused evidence, accounting readiness failure, duplicate ledger conflict, and persistence errors fail before commit or roll back the transaction.
- Failed execution cannot partially settle because settlement writes and paid transition are inside the same transaction.

Adversarial findings:
- Frontend cannot modify amount, wallet, affiliate, store, currency, paid state, paid timestamp, executor identity, ledger, or settlement. Backend derives those fields from SQL request and current claims.
- Affiliate profile/wallet changes after request are blocked by immutable wallet snapshot validation before payment execution.

Execution pipeline:
- Approved Withdrawal -> Backend Reload -> Immutable Snapshot Validation -> Payment Execution Authority (`AdminAffiliateController.MarkPayoutPaid` + `AffiliateSettlementOrchestrator.ExecuteAffiliatePayoutAsync`) -> Receipt/External Evidence Validation -> Paid Ledger -> Settlement -> Completed.

Settlement pipeline:
- Orchestrator serializable transaction -> readiness checks -> payout request destination validation -> evidence allocation -> balance validation -> `AffiliateBalanceLedger.Withdrawal` -> `AffiliateCommissionLedger.Paid` -> paid accounting/event outbox -> `AffiliatePayoutRequest.MarkPaid(PaidBy)` -> commit.

Runtime hardening performed:
- Added backend-owned `PaidBy` executor identity and required it before request-backed payment execution.

Exact runtime files modified:
- `PointOfSale.Core/Entities/Affiliate/AffiliatePayoutRequest.cs`
- `PointOfSale.Core/Interfaces/Affiliate/IAffiliateSettlementOrchestrator.cs`
- `PointOfSale.Infrastructure/Services/Affiliate/AffiliateSettlementOrchestrator.cs`
- `PointOfSale.API/Controllers/AdminAffiliateController.cs`
- `PointOfSale.Infrastructure/Migrations/20260626153000_AffiliatePayoutPaidByAuditIdentity.sql`

Regression freezes added:
- `PointOfSale.Tests/Architecture/AffiliatePayoutExecutionCertificationTests.cs`
- `PointOfSale.Web/src/services/adminAffiliateApi.test.ts`

Architecture updates:
- Updated all five authority context files for AF-PA6.3.
