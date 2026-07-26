# PASS M8-C Shipping Method Eligibility Authority Certification Findings

Date: 2026-06-24

## Verdict

Shipping Method Eligibility Authority is production-certifiable for the inspected POS administration, Storefront, CommerceHub cart and checkout, standard checkout, shipment, projection, cache, replay, and tenant-bound runtime paths. The backend owns eligibility, candidate generation, activation, availability, resolution, and governance. Frontends can display backend evidence and submit an identifier as intent; they cannot make an ineligible method eligible.

## Runtime Eligibility Model

- The authoritative method is SQL `ShippingMethods`, scoped by `TenantId`. Participation requires the method to exist in the applicable tenant/store, not be soft deleted, be `IsActive`, and (for a non-manual carrier method) have an enabled same-tenant `CarrierConfiguration`.
- `AvailableCountries`, carrier metadata, and carrier configuration are attributes/configuration. `AvailableCountries` is not currently consumed by any candidate or final-resolution path, so it is not an implemented eligibility predicate. Carrier quote availability is a separate carrier-quote display service and is not accepted as a `ShippingMethod` selection at checkout.
- Hard deletion is not an application lifecycle path. The supported deletion path is soft delete; its event projects a freshness-aware tombstone.

## Findings

- Eligibility and candidates: POS administration controls activation/deactivation and soft deletion under trusted tenant roles. Storefront reads active tenant-scoped derivative methods. Standard checkout locks/re-reads SQL by `{Id, TenantId, !IsDeleted}`, requires `IsActive`, and requires an enabled tenant-local carrier configuration for carrier-managed methods. CommerceHub generates candidates server-side from active, non-deleted methods with an enabled same-tenant carrier configuration (unless manual), evaluates pricing eligibility, and deterministically selects the cheapest eligible candidate.
- CommerceHub and storefront separation: CommerceHub never consumes Storefront projections/caches; it queries SQL by `StoreId`/tenant. Storefront display does not create CommerceHub candidates. Both use the same tenant-owned SQL entity only through backend resolution.
- Checkout and replay: CommerceHub cart and final checkout bypass the five-minute candidate cache and recompute current SQL candidates. Checkout rejects forged/stale selection and price pairs with `SHIPPING_CHANGED`. Standard checkout revalidates the method in its transaction. Idempotency responses do not bypass current final validation.
- Shipment: ordinary shipment creation requires exact equality with the order's authoritative method and a current active same-tenant SQL method. M8-C hardened the dormant public carrier-creation path to derive method, carrier, service level, and snapshots from the order-bound active SQL method, rejecting missing, deleted/inactive, manual, or carrier-mismatched configurations.
- Projection/cache/recovery: projection consumers re-fetch SQL rather than trust event payloads; they fence duplicate events and apply freshness-aware upserts/tombstones keyed by `{TenantId, ShippingMethodId}`. Storefront Redis/Mongo data and CommerceHub in-memory candidates are derivative views. A stale view can at most be displayed or submitted; checkout and shipment re-resolve SQL. Admin sync rebuilds current tenant SQL state and removes stale derivative records.
- Tenant/failure/adversarial: SQL predicates bind tenant/store IDs. Empty, missing, inactive, deleted, cross-tenant, stale, forged, or price-mismatched selections fail resolution/recalculation. 401/403 are enforced at protected admin/shipment boundaries; 404/400/409 represent missing/ineligible/changed candidates. Projection/cache tampering and browser storage/history/BFCache/replay cannot cross the final SQL gates.

## Runtime Hardening Performed

- Standard checkout, CommerceHub candidate generation, and shipment creation now treat a disabled/missing same-tenant carrier configuration as ineligible for non-manual shipping methods.
- `CarrierShipmentService.CreateShipmentWithCarrierAsync` now requires the authoritative order shipping-method binding, revalidates active same-tenant non-deleted SQL eligibility, rejects carrier substitution, derives service level from the method, and snapshots the resolved method into the created shipment.

## Exact Runtime Files Modified

- `PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs`
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubShippingService.cs`
- `PointOfSale.API/Services/Shipping/ShipmentService.cs`

## Regression Freeze Added

- `PointOfSale.Tests/Architecture/ShippingMethodEligibilityAuthorityCertificationTests.cs`

## Architecture Update

- This record establishes that carrier quote results, projections, caches, browser state, historical snapshots, and CommerceHub request selections are candidates or presentation evidence only. Final participation authority remains current tenant-scoped SQL resolution.

## Explicit Answers

- Who owns Shipping Method Eligibility Authority? Backend tenant-scoped SQL and backend resolution/governance paths.
- Can frontend fabricate shipping method eligibility? No.
- Can shipping method eligibility emerge, replay, or restore? No; derivative state may only submit/display a candidate and current SQL decides participation.
- Can frontend create shipping candidates? No. CommerceHub candidates are generated server-side; storefront receives a read-only projection.
- Can CommerceHub use an ineligible method? No. It resolves only active, non-deleted methods scoped to the store tenant and recomputes at cart/checkout.
- Can CommerceHub restore a deleted method? No.
- Can storefront eligibility become CommerceHub eligibility? No.
- Can CommerceHub eligibility become storefront eligibility? No.
- Can checkout create eligibility? No.
- Can shipment creation use an ineligible method? No.
- Can projections restore eligibility? No.
- Can cache restore eligibility? No.
- Can replay restore eligibility? No.
- Can tenant leakage occur? Not on the inspected authoritative, projection, cache, checkout, CommerceHub, or shipment paths.
- Were runtime changes required? Yes; the legacy carrier creation entry point was bound to the order's active SQL method.
- Is Shipping Method Eligibility Authority production-certifiable? Yes, for the inspected runtime scope.
