# PASS M5-B Return Ownership Authority Certification Findings

Date: 2026-06-22

Status: Return Ownership Authority is production-certifiable for the inspected frontend runtime.

## Ownership artifacts actually implemented

- CommerceHub customer refund/return-linked evidence: `refundId`, `orderId`, `orderItemId`, `vendorId`, `orderNumber`, and backend-issued media IDs. There is no standalone customer return ownership surface.
- Super Admin return workflow: `returnId`, `refundId`, `orderItemId`, `customerId`, `vendorId`, optional tracking/courier evidence, and refund/order/payment/execution evidence (`orderId`, `subOrderId`, `paymentId`, `executionId`, `financialRefundId`).
- POS sales/purchase returns: backend-issued `returnNumber`, original invoice ID/number, customer or supplier ID, warehouse ID, product, variant, and unit IDs. Frontend-generated idempotency and attempt keys are replay guards, not ownership artifacts.

No implemented frontend `returnOwnerId`, return ownership token, return tenant ID, return transfer ID, return merge ID, return restore ID, or client-generated return identifier was found.

## Findings

- Customer: refund history and order detail evidence are accepted only from current backend queries and withheld during fetch/error. Creation re-enters the current backend order item and payout authority. Customer route, dialog, selection, media, cache, and refresh state cannot establish return ownership.
- Vendor: the CommerceHub vendor surface is read-only refund-impact evidence. Its current vendor/session/tenant presentation guard rejects a scope mismatch; there is no vendor frontend return create, transfer, merge, repair, or logistics ownership path.
- Admin: monitoring is visibility only. A selected row is re-derived from the current list; detail and embedded return evidence must bind `returnId` to the current refund, order item, customer, and vendor. Return mutations refetch current list/detail authority and use the refreshed backend `returnId`.
- Refund, order, and payment consistency: neither refund IDs, order IDs/items/sub-orders, payment IDs/references, nor execution/financial-refund identifiers are converted into return ownership. They are validated backend-issued lineage evidence only.
- Tenant scope: Super Admin filtering is not ownership, vendor evidence is current session-scoped, and POS create controllers lock the original invoice under `RequiredTenantId` and derive customer/supplier, warehouse, and return lineage server-side.
- Bootstrap/cache/failure/adversarial: React Query, component state, dialogs, selected rows, URL/history state, local/session storage, focus, reconnect, BFCache, mutation responses, malformed DTOs, and 401/403/404/409/422 failures are candidates only. Current reads validate or withhold them; replay cannot restore ownership.

## Runtime hardening performed

None. The inspected runtime already has the required current-authority boundaries: customer creation re-entry, Super Admin current-list/current-detail mutation re-entry and lineage validation, vendor scope validation, and POS backend-derived tenant/invoice ownership.

## Exact runtime files modified

None.

## Regression freezes added

- `PointOfSale.Tests/Architecture/ReturnOwnershipAuthorityCertificationTests.cs`

## Architecture updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
- `docs/architecture/return-ownership-authority-certification-findings.md`

## Explicit certification answers

- Who owns return ownership authority? Backend return/refund/order/customer/vendor/tenant/actor, SQL lineage, lock, idempotency, and endpoint-authorization authorities.
- Can frontend fabricate return ownership? No.
- Can Customer A access Customer B returns? No inspected frontend path bypasses backend customer/order authorization.
- Can Vendor A access Vendor B returns? No; vendor evidence is current vendor/session scoped and there is no vendor return ownership mutation path.
- Can monitoring or selected rows become ownership? No.
- Can refund, order, or payment ownership become return ownership? No.
- Can stale state, cache, or replay restore return ownership? No.
- Can tenant leakage occur? No inspected frontend path widens tenant scope; POS return ownership is derived under the backend required tenant.
- Were runtime changes required? No.
- Is Return Ownership Authority production-certifiable? Yes, for the inspected frontend runtime.
