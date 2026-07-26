# PASS M7-A Shipment Identity Authority Certification Findings

Date: 2026-06-23

## Verdict

Shipment Identity Authority is production-certifiable for the inspected shipment, fulfillment, tracking, carrier, storefront, admin, recovery, projection, cache, and replay runtime paths. PostgreSQL backend shipment rows own shipment identity and lineage. Frontend state is display evidence only.

## Findings

- Identity: `ShipmentService` creates a server-generated `Shipment.Id` inside a serializable transaction after proving the current tenant-owned order, active shipping method, financial readiness, and absence of an active shipment. The database enforces one active `{TenantId, OrderId}` shipment.
- Tracking and carrier: tracking, external carrier reference, and carrier request identifiers only resolve an existing shipment. Carrier callbacks reject missing or ambiguous tenant/shipment matches, require signature/trust validation, and collapse duplicate webhook evidence. A tracking or carrier identifier never creates a shipment.
- Fulfillment and lineage: shipping maps delivery progress through `IOrderFulfillmentService`; fulfillment state is not a shipment factory. Order, return, refund, delivery, and tracking identities remain linked evidence, not substitutes for shipment identity.
- Projection and recovery: order-facing projection and status synchronization recompose current SQL lineage through `OrderFulfillmentLineageAuthority`. Recovery creates only a backend shipment from a qualifying current SQL order under the same create guards; carrier retry and reconciliation operate on an existing shipment ID. No projection, export, cache, or replay is an identity source.
- Frontend/cache: shipping UI stores no shipment record in browser storage or React Query. Response admission now requires a canonical backend shipment ID, order ID, and shipment number, rejects conflicting IDs and cross-order evidence, and removes the tracking-only fallback that had shaped an order tracking field as a shipment object. Refresh, BFCache, history, and replay therefore cannot restore shipment authority.
- Tenant and failure: API reads/mutations use tenant-scoped shipment/order predicates; carrier references have tenant-scoped uniqueness; webhook ambiguity fails closed. Invalid identifiers, forged DTO values, malformed response objects, cross-order response data, unauthorized calls, and stale/replayed carrier events do not establish shipment identity.

## Runtime Hardening Performed

- Added storefront response-admission guards for canonical shipment identity and shipment-to-order lineage.
- Removed the tracking-only storefront fallback that could present order tracking data as a shipment-shaped object.

## Exact Runtime Files Modified

- `PointOfSale.Web/src/modules/storefront/shipping/shipmentIdentityAuthority.js`
- `PointOfSale.Web/src/modules/storefront/shipping/hooks/useShippingApi.js`
- `PointOfSale.Web/src/modules/storefront/shipping/pages/OrderTrackingPage.jsx`

## Regression Freezes Added

- `PointOfSale.Web/src/modules/storefront/shipping/shipmentIdentityAuthority.test.ts`
- `PointOfSale.Tests/Architecture/ShipmentIdentityAuthorityCertificationTests.cs`

## Explicit Answers

- Who owns Shipment Identity Authority? The backend PostgreSQL shipment write model and its tenant-scoped lineage/transaction constraints.
- Can frontend fabricate shipment identity? No; a client value is rejected as shipment evidence unless it is a complete backend DTO bound to the requested order, and never authorizes an operation.
- Can shipment identity emerge, replay, or restore? Not in the frontend. Backend recovery may create a server-generated record only from current SQL order authority; carrier replay and projection recovery resolve existing SQL identity.
- Can tracking, carrier, fulfillment, order, or return identity become shipment identity? No. Each is evidence or lineage that must resolve an existing backend shipment; none is an identity factory.
- Can cache or replay restore shipment identity authority? No. There is no persisted frontend shipment cache and stale response candidates are revalidated/fail closed.
- Can tenant leakage occur? Not on reviewed paths: shipment/order predicates, carrier-reference uniqueness, and webhook ambiguity rejection are tenant-bound.
- Were runtime changes required? Yes, storefront response admission and tracking-only fallback hardening.
- Is Shipment Identity Authority production-certifiable? Yes, for the inspected scope.
