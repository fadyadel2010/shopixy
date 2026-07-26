# PASS AF-PA6.2 POS Admin Payout Review, Approval & Authority Certification

## Scope

Reviewed POS Admin payout request list/detail presentation, refresh/filter/navigation behavior, approve/reject modals, backend admin endpoints, `AffiliatePayoutService`, `AffiliatePayoutRequest`, persistence guards, and frontend API payloads. Payment execution, settlement, and payment providers were intentionally excluded.

## Findings

- Review findings: request list/detail reads are store-scoped backend reads. The frontend can filter, refresh, display, and select a request id only.
- Approval findings: Approval Authority occurs in `AffiliatePayoutService.ApprovePayoutAsync` after backend admin identity resolution, request reload by `{Id, StoreTenantId}`, row-level lock, immutable wallet snapshot validation, and entity transition.
- Snapshot findings: amount, wallet snapshot, affiliate identity, store scope, and currency come from the persisted `AffiliatePayoutRequest`; frontend approve/reject payloads do not carry those fields.
- Concurrency findings: row-level locking plus `Pending`-only approval prevents double approval, replay approval, and simultaneous two-admin approval.
- Audit findings: approved and rejected requests now persist `ApprovedAtUtc`/`ApprovedBy` and `RejectedAtUtc`/`RejectedBy`, with optional notes.
- Failure findings: failed approval/rejection paths throw before `SaveChangesAsync` and roll back the transaction. Failed reads/mutations cannot change request state.
- Adversarial findings: modified frontend DTOs cannot submit amount, wallet, affiliate, store, currency, status, or actor authority for approve/reject.

## Approval Pipeline

Withdrawal Request
↓
Backend Request Reload by request id and store scope
↓
Snapshot Validation against immutable persisted request wallet evidence
↓
Approval Authority in `AffiliatePayoutService.ApprovePayoutAsync`
↓
Approved Request with `ApprovedAmount = RequestedAmount`, `ApprovedAtUtc`, `ApprovedBy`
↓
Ready For Payment

## Runtime Hardening

- Added `ApprovedBy` and `RejectedBy` to `AffiliatePayoutRequest`.
- Required backend-resolved current POS admin user id before approve/reject.
- Passed admin identity through `IAffiliatePayoutService` and persisted it inside entity transitions.
- Returned approval/rejection audit identity in admin payout review reads.
- Added SQL migration for the new audit columns.

## Regression Freezes

- `PointOfSale.Tests/Architecture/AffiliateWithdrawalRequestAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Core/Entities/Affiliate/AffiliatePayoutRequestWalletSnapshotTests.cs`
- `PointOfSale.Web/src/services/adminAffiliateApi.test.ts`

## Certification Answers

- Approval Authority owner: backend.
- POS Admin can change Amount: no.
- POS Admin can change Wallet: no.
- POS Admin can change Affiliate: no.
- POS Admin can approve twice: no.
- Two Admins can approve simultaneously: no.
- Replay duplicate approval: no.
- Failed approval can modify request state: no.
- Approval Authority location: `AffiliatePayoutService.ApprovePayoutAsync`, inside row-locked backend request reload and entity transition.
- Runtime changes required: yes, audit identity hardening.
- Production-certifiable: yes.
