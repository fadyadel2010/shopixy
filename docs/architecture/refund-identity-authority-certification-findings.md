# PASS M4-A Refund Identity Authority Certification Findings

Date: 2026-06-21

Status: Refund Identity Authority is production-certifiable for inspected frontend runtime paths after Super Admin return/refund identity hardening.

## Implemented Identity Artifacts

- `refundId`: customer refund history, embedded customer order refunds, Super Admin refund monitoring/details/actions, vendor refund-impact evidence.
- `returnId`: Super Admin refund return-lifecycle presentation and return mutation target.
- `executionId`: Super Admin refund execution evidence.
- `financialRefundId`: Super Admin refund execution financial lineage evidence.
- `paymentId`: Super Admin refund financial preview/execution evidence only.
- `paymentReference`: customer/order payment display evidence only.
- `transactionReference`: customer payout snapshot and Super Admin manual refund execution evidence only.
- `gatewayReference`: Super Admin execution/provider evidence only.
- Frontend-created `Idempotency-Key`: customer refund request replay key only; it is not refund identity authority.

Not found in inspected frontend runtime: `RefundNumber`, `RefundRequestId`, `RefundExecutionId` as a named DTO field, `RefundReference`, `ProviderRefundReference`, `RefundTrackingReference`, `RefundToken`.

## Findings

- Identity findings: backend refund/request/return/execution/financial rows own refund identity. Frontend route/search state, selected rows, React state, mutation responses, caches, storage, payment references, provider references, and idempotency keys are derivative only.
- Customer findings: customer refund rows pass financial and visibility boundaries before presentation. `activeRefundId` can select evidence only from current accepted backend refund rows; refresh/failure withholds rows and evidence dialogs. Customer submission sends item intent and idempotency only, then accepts only backend-created `refundId` response evidence.
- Vendor findings: inspected vendor refund evidence is financial/refund-impact presentation only and requires current vendor financial scope. Vendor rows cannot fabricate, substitute, or transfer refund identity.
- Admin findings: Super Admin monitoring validates refund list IDs, derives selected refund from the current accepted list, validates details against selected refund/order/vendor/customer/amount/currency scope, and clears URL-selected refunds absent from current backend evidence.
- Payment consistency findings: payment identity/reference fields remain evidence only. Frontend does not infer refund identity from `paymentId`, `paymentReference`, provider status, `gatewayReference`, or payment lifecycle.
- Return consistency findings: `returnId` now validates as backend return evidence and embedded return DTOs must match current `refundId`, `orderItemId`, `customerId`, and `vendorId`. Return identity cannot become refund identity or reconstruct a refund mapping.
- Bootstrap findings: initial load, refresh, focus, visibility, reconnect, history, and BFCache paths re-enter backend queries or withhold presentation; stale refund identity is not restored as authority.
- Cache findings: React Query and local component state are derivative. Customer and Super Admin selected IDs are candidates only and are re-derived from current accepted rows.
- Failure findings: 401/403/404/409/422, malformed DTOs, failed refreshes, and failed mutation responses cannot create replacement refund identities. Malformed refund/return identity evidence fails closed.
- Adversarial findings: identity replay, storage replay, history replay, route replay, selection replay, mutation replay, actor replay, and tenant replay cannot fabricate, restore, merge, repair, transfer, or substitute refund identity without current backend authority.

## Runtime Hardening Performed

- Added Super Admin return/refund identity validation for embedded return DTOs in refund details.
- Added standalone Super Admin return DTO validation for exact `returnId` equality on return read and mutation responses.
- Added regression coverage rejecting embedded return/refund mismatch and standalone return identity substitution.

## Exact Runtime Files Modified

- `PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.ts`
- `PointOfSale.Web/src/super-admin/refunds/refundsService.ts`
- `PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.test.ts`

## Regression Freezes Added

- `PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.test.ts`
- `PointOfSale.Tests/Architecture/RefundIdentityAuthorityCertificationTests.cs`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
- `docs/architecture/refund-identity-authority-certification-findings.md`

## Explicit Certification Answers

- Who owns refund identity authority? Backend refund request, refund, return, execution, financial refund, payment/order lineage, tenant/actor authorization, and SQL authority.
- Can frontend fabricate refund identities? No.
- Can customer fabricate refund identities? No.
- Can vendor fabricate refund identities? No.
- Can monitoring create refund identities? No; it is visibility and selected-row presentation only.
- Can payment identity become refund identity? No.
- Can return identity become refund identity? No.
- Can stale identities survive refresh? No inspected path preserves stale identity as authority.
- Can cache restore identities? No.
- Can replay restore identities? No.
- Can tenant leakage occur? No inspected frontend path introduces tenant leakage; backend authorization remains final.
- Were runtime changes required? Yes.
- Is Refund Identity Authority production-certifiable? Yes, for inspected frontend runtime paths.
