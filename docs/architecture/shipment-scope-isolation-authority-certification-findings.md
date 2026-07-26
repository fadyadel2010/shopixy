# PASS M7-G Shipment Scope Isolation Authority Certification Findings

Date: 2026-06-24

## Verdict

Shipment Scope Isolation Authority is production-certifiable for the inspected runtime scope after excluding soft-deleted shipments from carrier webhook resolution and carrier mutation paths. Backend SQL lineage, trusted tenant context, current customer claims, backend roles, and verified carrier trust decide who may exist inside a shipment scope. Frontend state is presentation evidence and backend-bound intent only.

## Runtime-first scope findings

- Tenant and admin: admin shipment list, detail, status, tracking, and cancellation operations begin from the trusted tenant and query `Shipment.TenantId`. Search filters, pagination, and response projection remain downstream of that predicate; no shipment export endpoint was found.
- Customer: the storefront shipment read requires the current Storefront JWT tenant/customer claims and proves `Order.TenantId` plus `Order.OnlineCustomerId` before the tenant-scoped shipment read. Foreign and missing orders receive the same 404. Customer history and tracking use their own current customer-bound order authority.
- Guest tracking: guest tracking is restricted to the current tenant, an existing guest checkout, matching checkout session, and a short-lived token bound to the same tenant/order/session nonce. Email/order-number recovery reveals no shipment data and sends a fresh link only to the stored order email.
- Vendor: there is no separate vendor shipment-scope selector. Vendor/admin operational shipment routes are role-gated and tenant-scoped; vendor identity or a browser-selected shipment cannot replace the server tenant predicate.
- Carrier: carrier webhooks are verified, normalized, and resolve only an existing non-deleted shipment with the resolved tenant, exact carrier code, and exact shipment/tracking identity. Ambiguous, missing, forged, wrong-carrier, and soft-deleted targets fail closed. Carrier references are match criteria, not scope grants.
- Storefront and CommerceHub: Storefront `Shipment` is tenant-owned order fulfillment. CommerceHub uses independent marketplace-order/suborder shipping selections and customer order scope; it has no runtime bridge that turns a storefront shipment into CommerceHub shipment authority or the reverse.
- Tracking, search, export, recovery: tracking cannot widen scope because every carrier operation resolves an existing tenant/carrier-bound SQL shipment. The only shipment list/search-like path applies tenant filtering before filters/count/paging. No separate shipment export path exists. Recovery enumerates candidates server-side, sets the candidate tenant, and re-enters guarded create/carrier/reconciliation services rather than restoring a frontend or projected scope.
- Cache and replay: reviewed shipment frontend hooks use component-local candidates and no local/session storage or persisted React Query cache. Refresh, BFCache, browser history, retries, carrier replay, and recovery can only cause a current backend reread or guarded re-entry; they cannot restore a scope grant.
- Failure and adversarial behavior: 401/403/404/409/422-style rejection, malformed DTOs, forged shipment/tenant/customer/vendor/carrier references, tampered projections, invalid tracking tokens, duplicate callbacks, and ambiguous lookups create no scope authority. Soft-deleted shipment rejection is now explicit in webhook resolution, webhook processing, sync, cancel, and label lookup paths.

## Runtime hardening performed

- Added `!IsDeleted` predicates to webhook target resolution by shipment ID and by `{carrier, tracking}`.
- Added `!IsDeleted` to carrier webhook processing and carrier cancel, tracking-sync, and label lookup reads.

## Exact runtime files modified

- `PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs`
- `PointOfSale.API/Services/Shipping/ShippingWebhookRequestPipeline.cs`

## Regression freezes added

- `PointOfSale.Tests/Architecture/ShipmentScopeIsolationAuthorityCertificationTests.cs`
- Updated `PointOfSale.Tests/Architecture/ShipmentTrackingAuthorityCertificationTests.cs`

## Explicit certification answers

- Who owns Shipment Scope Authority? Backend SQL shipment/order lineage, trusted tenant context, current customer claims, backend roles, and verified carrier trust.
- Can frontend fabricate shipment scope? No.
- Can shipment scope emerge, replay, or restore? No; every read, mutation, recovery, and carrier callback re-proves current backend scope.
- Can Customer A enter Customer B scope? No.
- Can Vendor A enter Vendor B scope? No.
- Can Carrier A enter Carrier B scope? No.
- Can storefront scope become CommerceHub scope, or the reverse? No.
- Can tracking, search, or exports widen shipment scope? No. No separate shipment export runtime path exists in the reviewed implementation.
- Can recovery cross shipment scope? No; it establishes the candidate tenant and re-enters guarded services.
- Can cache or replay restore shipment scope? No.
- Can tenant leakage occur? Not on the inspected runtime paths.
- Were runtime changes required? Yes; soft-deleted carrier targets required explicit exclusion.
- Is Shipment Scope Isolation Authority production-certifiable? Yes, for the inspected runtime scope.
