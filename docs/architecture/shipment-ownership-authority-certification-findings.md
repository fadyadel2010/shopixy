# PASS M7-B Shipment Ownership Authority Certification Findings

Date: 2026-06-23

## Verdict

Shipment Ownership Authority is production-certifiable for the inspected shipment, order, fulfillment, carrier, tracking, projection, export, recovery, and frontend paths after hardening the legacy storefront shipment read. PostgreSQL rows, trusted tenant context, current authenticated customer claims, backend role authorization, and verified carrier trust own shipment access and mutation authority. Browser state is presentation evidence only.

## Findings

- Customer ownership: the authenticated storefront shipment endpoint now proves the current JWT `CustomerId` and `TenantId` against the SQL `Order.OnlineCustomerId`, `Order.TenantId`, and requested order before returning a shipment. It returns a non-enumerating 404 for missing or foreign orders. Guest tracking remains exclusively on the session-bound, renewable-token route.
- Vendor and admin ownership: shipment list, detail, status, tracking, and cancel reads are role-protected and tenant-scoped with `Shipment.TenantId`; no vendor/browser selector is accepted as shipment scope.
- Carrier and tracking ownership: carrier registration, tracking sync, and webhook processing resolve an existing tenant-scoped shipment; webhooks require the carrier trust pipeline and reject ambiguous, missing, malformed, duplicate, or stale evidence. Carrier references do not grant customer, vendor, or tenant access.
- Fulfillment and order lineage: order events can create a server-generated shipment only after current SQL order, payment, and accounting checks. Fulfillment synchronizes an existing shipment/order lineage and cannot create browser-originated ownership.
- Projection, export, cache, and replay: admin reads use SQL shipment scope; storefront response admission requires canonical backend shipment/order identity. No shipment state is persisted in browser storage or React Query. Refresh, history, BFCache, recovery, and replay may re-fetch or repair only through current backend scope checks.
- Tenant and failure behavior: customer, admin, carrier, and worker paths use tenant-scoped predicates or trusted carrier resolution. Invalid or forged IDs/DTOs, foreign tenant/customer claims, malformed tracking evidence, and replayed callbacks fail closed or resolve no shipment authority.

## Runtime Hardening Performed

- Removed anonymous access from `StorefrontShipmentController`.
- Required the `StorefrontJwt` authentication scheme and proved JWT customer/tenant claims against current SQL order ownership before the shipment read.
- Preserved guest access only through the separate session- and token-bound tracking endpoint.

## Exact Runtime Files Modified

- `PointOfSale.API/Controllers/StorefrontShipmentController.cs`

## Regression Freeze Added

- `PointOfSale.Tests/Architecture/ShipmentOwnershipAuthorityCertificationTests.cs`

## Explicit Answers

- Who owns Shipment Ownership Authority? Backend PostgreSQL shipment/order lineage, trusted tenant context, current authenticated customer claims, role authorization, and verified carrier trust.
- Can frontend fabricate, emerge, replay, or restore shipment ownership? No. It can only present backend evidence and submit backend-bound intent; each read or mutation re-proves backend authority.
- Can customer, vendor, carrier, tracking, fulfillment, cache, or replay evidence become shipment ownership? No. They are scoped evidence or lifecycle input, not authority grants.
- Can tenant leakage occur? Not on the inspected paths. Vendor A, Customer A, and Carrier A cannot use their authority to view another vendor's, customer's, or carrier's shipment.
- Were runtime changes required? Yes: the legacy anonymous storefront shipment read required current customer/tenant proof.
- Is Shipment Ownership Authority production-certifiable? Yes, for the inspected runtime scope.
