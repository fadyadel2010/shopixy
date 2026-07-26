# PASS M8-G Shipping Method Scope Isolation Authority Certification Findings

Date: 2026-06-25

## Verdict

Shipping Method Scope Isolation Authority is production-certifiable after runtime-first inspection and Storefront cache admission hardening. Backend tenant-owned SQL `ShippingMethods`, trusted tenant/store resolution, vendor role authorization, CommerceHub store-lineage validation, split-order persistence boundaries, order-bound shipment creation, carrier configuration predicates, projection fences, and recovery re-entry own all shipping-method scope. Frontend surfaces display backend evidence and submit backend-bound intent only.

## Scope Findings

- The implemented authority scope is tenant/store scope over SQL `ShippingMethods.TenantId`. CommerceHub uses the vendor store ID as the tenant/store scope for candidate generation. There is no independent frontend, Storefront, CommerceHub, shipment, carrier, or projection-owned shipping-method scope domain.
- Storefront reads are read-only tenant projections. Runtime hardening now requires cached Storefront shipping-method DTOs to carry backend `TenantId` evidence and rejects missing or mismatched cache rows before display-price recalculation.
- CommerceHub cart and final checkout bypass shipping candidate cache, recompute from current SQL, and reject mismatched method/price candidates with `SHIPPING_CHANGED`.

## Tenant Findings

- POS configuration creates `ShippingMethod.TenantId` from trusted `ITenantContext.RequiredTenantId`; create/update/delete/list/detail/calculate/sync bind SQL predicates to the current tenant.
- Storefront tenant resolution validates middleware-resolved tenant context and authenticated tenant claims before cart/scoped shipping-method reads.
- Tenant A cannot use Tenant B's shipping method on inspected authoritative paths. Cross-tenant IDs resolve as absent or stale and cannot survive final SQL re-entry.

## Vendor Findings

- Vendor shipping configuration is tenant-scoped POS configuration. Vendor DTOs do not carry accepted tenant/vendor ownership fields.
- Vendor A cannot restore, widen, modify, select, or ship with Vendor B's method because SQL candidate, checkout, shipment, projection, and carrier predicates bind method tenant to the current store/tenant.

## Storefront Findings

- Storefront shipping-method list reads use tenant-validated cart ownership, Mongo/SQL tenant filters, and tenant-keyed Redis cache.
- Hardened cached DTO admission rejects stale, legacy, forged, or foreign-tenant cache entries before they can become current display evidence.
- Storefront carrier quote display is separate carrier quote evidence and is not accepted as checkout shipping-method scope.

## CommerceHub Findings

- `CommerceHubShippingService` loads active, non-deleted methods where `method.TenantId == storeId`, excludes carrier-managed methods without enabled same-tenant `CarrierConfiguration`, computes eligibility, and selects deterministically.
- CommerceHub request method IDs and prices are stale-detection candidates only. Storefront projections/cache cannot become CommerceHub scope because CommerceHub uses current SQL re-entry.

## Split-Order Findings

- Checkout validates listing, product, variant, tenant, payment, and governance lineage before shipping calculation.
- Each sub-order persists only the backend-resolved selection for its own `StoreId`; `Shipping`, `ShippingMethodId`, `ShippingMethodName`, and `ShippingSnapshotJson` are behind the `ShippingAssignment` boundary.
- Aggregation sums server-resolved sub-orders and cannot widen or substitute scope.

## Shipment Findings

- Shipment creation locks the current same-tenant order, requires the request method to equal `order.ShippingMethodId`, and revalidates active same-tenant SQL method eligibility.
- Shipment references and snapshots are historical lineage only and cannot restore or widen method scope.

## Carrier Findings

- Carrier-managed shipment creation derives carrier and service level from the order-bound shipping method and rejects manual, foreign, disabled, or caller-substituted carrier scope.
- Carrier configuration predicates require the same tenant as the shipping method. Carrier callbacks/tracking cannot create method scope.

## Projection Findings

- Mongo shipping-method projections re-fetch SQL by `{ShippingMethodId, TenantId}`, use freshness fences, tombstone deletes, and tenant-keyed unique indexes.
- Admin sync rebuilds current tenant SQL state and removes stale tenant documents. Projections copy backend scope; they do not create or widen scope.

## Cache Findings

- Redis Storefront shipping-method cache is tenant-keyed and now row-level tenant-validated on admission.
- CommerceHub cart and checkout bypass shipping-option cache for authoritative calculation.
- Browser, React Query, BFCache, local/session storage, and in-memory candidates can only display or submit candidates; current backend revalidation decides scope.

## Replay Findings

- Checkout idempotency binds the request hash and returns only the already-created backend order for identical replay.
- Changed replay fails with `IDEMPOTENCY_PAYLOAD_CONFLICT`; stale shipping selection replay fails with `SHIPPING_CHANGED` or validation errors.
- Projection event replay is fenced or tombstoned and cannot resurrect a deleted or foreign method.

## Failure Findings

- `401`/`403` enforce authenticated/authorized admin and shipment boundaries.
- `404`/missing resolution rejects absent or cross-tenant methods.
- `409` rejects stale checkout shipping candidates, idempotency payload conflicts, and shipment method substitution.
- `400`/validation failures reject missing store selections, invalid tenant context, malformed IDs, and unavailable candidates.
- `422`-style malformed evidence has no accepted shipping-method scope path; malformed cache/projection rows fail closed or re-enter SQL fallback.

## Adversarial Findings

- Forged `TenantId`, `StoreId`, `VendorId`, `ShippingMethodId`, `ShippingOptionId`, carrier code, price, projection document, Redis row, checkout replay, and browser cache evidence cannot bypass backend governance on inspected paths.
- Scope cannot emerge from Storefront display, CommerceHub request state, split aggregation, shipment creation, carrier registration, projection repair, cache, recovery, or replay.

## Runtime Hardening Performed

- Added backend tenant-scope evidence to Storefront shipping-method DTOs.
- Storefront shipping-method Redis cache admission now rejects missing or mismatched cached `TenantId` rows before currency validation, display-price calculation, or response return.

## Exact Runtime Files Modified

- `PointOfSale.API/DTOs/Storefront/ShippingMethodDto.cs`
- `PointOfSale.API/Services/Storefront/ShippingMethodService.cs`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/ShippingMethodScopeIsolationAuthorityCertificationTests.cs`

## Architecture Updates

- Updated all five `.ai-context` authority files with M8-G scope isolation truth, risk, hardening, and freeze rules.

## Explicit Certification Answers

- Who owns Shipping Method Scope Authority? Backend tenant-owned SQL, trusted tenant/store resolution, CommerceHub checkout/shipping services, shipment/order lineage, carrier predicates, projection fences, and governance.
- Can frontend fabricate shipping-method scope? No.
- Can shipping-method scope emerge? No.
- Can shipping-method scope replay? No.
- Can shipping-method scope restore? No.
- Can Tenant A use Tenant B shipping method? No.
- Can Vendor A use Vendor B shipping method? No.
- Can Storefront scope become CommerceHub scope? No.
- Can CommerceHub scope become Storefront scope? No.
- Can split-order widen shipping scope? No.
- Can shipment widen shipping scope? No.
- Can carrier widen shipping scope? No.
- Can projections restore scope? No.
- Can cache restore scope? No after tenant-scope cache admission hardening.
- Can replay restore scope? No.
- Can tenant leakage occur? Not on inspected paths.
- Were runtime changes required? Yes, for Storefront Redis cache tenant-scope admission.
- Is Shipping Method Scope Isolation Authority production-certifiable? Yes.
