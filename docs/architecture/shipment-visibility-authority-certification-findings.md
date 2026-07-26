# PASS M7-D Shipment Visibility Authority Certification Findings

Date: 2026-06-23

## Verdict

Shipment Visibility Authority is production-certifiable for the inspected shipment, customer, guest, admin, carrier, search, projection, dashboard, export, cache, recovery, and replay runtime paths. Backend SQL lineage, current authentication, trusted tenant context, and verified carrier trust are the only visibility authorities. Frontend state is display evidence and backend-bound intent only.

## Visibility Findings

- Customer: `StorefrontShipmentController` proves current `StorefrontJwt` customer and tenant claims against the current SQL ecommerce order before reading its tenant-scoped shipment. Missing and foreign orders both return a non-enumerating 404. `StorefrontOrdersReadController` repeats current SQL customer ownership before tracking-projection repair and again rejects a projection whose customer binding disagrees.
- Guest: guest tracking requires an existing guest checkout, matching current session ID, a valid short-lived token bound to the same tenant, order, and session nonce, and rate-limit admission. Email/order-number recovery always returns an indistinguishable accepted response and sends a new link only to the order email. It does not expose shipment data.
- Vendor and admin: shipment list, detail, status, tracking, and cancellation are role-gated and query `Shipment.TenantId`. No vendor id, frontend selection, order id, search term, or export parameter establishes scope. There is no separately implemented shipment export or shipment dashboard reader outside these tenant-scoped SQL reads.
- Carrier: webhooks are admitted by verified provider trust, then resolve an existing shipment by matching carrier code and an unambiguous shipment ID or tracking number. Carrier-supplied tenant, shipment, and tracking fields are bounded match criteria; they cannot widen carrier or tenant scope. Duplicate and ambiguous callbacks fail closed.
- Order, projections, and replay: `OrderFulfillmentLineageAuthority` and storefront order projection loading recompose shipment facets from current SQL `{TenantId, OrderId}` lineage. `OrderProjectionConsumer` rejects missing lineage and collapses stale replay payloads against current SQL truth.
- Search, export, and dashboards: the reviewed admin list has tenant-scoped filter predicates before count, pagination, and projection. No shipment-specific public/admin search endpoint, export writer, or dashboard projection that could bypass those predicates was found.
- Cache and frontend: inspected shipment hooks use component state and request sequence fences; they do not persist shipment visibility in local/session storage or React Query. Storefront response admission rejects malformed, conflicting, and cross-order shipment DTOs. Refresh, reconnect, history, BFCache, and retry can only cause a new backend read.
- Failure and adversarial behavior: 401/403/404 prevent access; 409/422-style mutation failures do not return a new grant. Malformed DTOs, forged order/shipment/tracking/carrier references, stale responses, duplicate events, cache candidates, and replay evidence are rejected, withheld, or recomposed from SQL.

## Runtime Hardening Performed

- `ShipmentController` no longer derives its read or mutation tenant from a compatibility request header. It consumes `ITenantContext.RequiredTenantId`, which is populated only by `TenantResolutionMiddleware` after authenticated-claim or verified-host resolution. This removes a redundant controller-level authority input and preserves the middleware's tenant-spoofing defense as a second boundary.

## Regression Freeze

- `ShipmentOwnershipAuthorityCertificationTests.DirectShipmentReadAndMutation_MustUseTrustedTenantContextRatherThanCallerHeader` freezes the direct shipment endpoint against reintroducing caller-controlled tenant selection.

## Certification Answers

- Shipment Visibility Authority is backend-owned.
- Frontend cannot fabricate, emerge, replay, or restore shipment visibility authority.
- Customer A cannot see Customer B's shipment; Vendor A cannot see Vendor B's shipment; Carrier A cannot see Carrier B's shipment.
- Guest tracking, search, exports, cache, replay, projections, and order lineage cannot widen shipment visibility.
- Tenant leakage does not occur on the inspected paths.
