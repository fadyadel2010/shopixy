# PASS M5-A Return Identity Authority Certification Findings

Date: 2026-06-22

Status: Return Identity Authority is production-certifiable for the inspected frontend runtime.

## Implemented identity artifacts

- CommerceHub/Super Admin return workflow: `returnId`, `refundId`, `orderItemId`, `customerId`, `vendorId`, and optional `trackingNumber`.
- Super Admin refund context: backend-issued `refundId`, `executionId`, `financialRefundId`, `paymentId`, `transactionReference`, and `gatewayReference` are relationship or execution evidence only; none is a return identity.
- POS sales and purchase return surfaces: backend-issued `returnNumber`, plus backend-selected invoice, customer/supplier, warehouse, product, variant, and unit IDs. The frontend creates only idempotency/request-attempt keys; these are not return identities.

No implemented frontend `returnRequestId`, `returnShipmentId`, `returnExecutionId`, `returnReference`, or frontend-generated return identifier was found.

## Findings

- Customer: implemented customer runtime has refund history and refund request entry, not a standalone return-history/detail/tracking surface. It submits backend-bound order-item intent and presents only accepted backend refund evidence. It cannot mint a `returnId` or turn a route, row, or stale refund into a return.
- Vendor: CommerceHub vendor refund impacts are read-only, tenant-scoped financial evidence. No vendor return creation, merge, logistics, or return-identity mutation frontend path is implemented.
- Admin: monitoring selection is derived from the current backend refund list. Embedded returns must match current refund, item, customer, and vendor evidence. Standalone return reads and mark-failed/override mutations require exact `returnId` response equality. Dialogs close while current list/detail authority is fetching or failed.
- Refund/order/payment consistency: `refundId`, order IDs/numbers, payment IDs/references, provider/gateway references, execution IDs, and financial-refund IDs remain distinct backend evidence. The frontend does not infer or reconstruct a `returnId` from any of them.
- Bootstrap/cache/failure: React Query, selected rows, route/search/history state, dialog state, mutation responses, local/session storage, reconnect, focus, visibility, BFCache, 401/403/404/409/422, and malformed DTOs are candidates only. Current queries validate or withhold; they cannot restore return identity.
- Adversarial: identity, storage, history, route, selection, mutation, actor, and tenant replay cannot fabricate, repair, merge, substitute, replay, restore, or transfer return identity without current backend authority.

## Runtime hardening performed

No new runtime change was required in M5-A. Existing M4-A exact-return validation and current-list/current-detail mutation re-entry already close the implemented return frontend paths.

## Exact runtime files modified

None in M5-A.

## Regression freezes added

- `PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.test.ts` now freezes exact-target validation for standalone return reads and return mutations.

## Architecture updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
- `docs/architecture/return-identity-authority-certification-findings.md`

## Explicit certification answers

- Who owns return identity authority? Backend return/refund/order, tenant, actor, workflow, SQL, lock, idempotency, and endpoint-authorization authorities.
- Can frontend, customer, vendor, or monitoring fabricate return identities? No.
- Can refund, order, or payment identity become return identity? No.
- Can stale identity, cache, or replay restore return identity? No.
- Can tenant leakage occur? No inspected frontend path bypasses backend scope authorization.
- Were runtime changes required? No new M5-A runtime change; existing hardened runtime boundaries are sufficient.
- Is Return Identity Authority production-certifiable? Yes, for the inspected frontend runtime.
