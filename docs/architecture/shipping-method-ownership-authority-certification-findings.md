# PASS M8-B Shipping Method Ownership Authority Certification Findings

Date: 2026-06-24

## Verdict

Shipping Method Ownership Authority is production-certifiable for the reviewed POS administration, Storefront, CommerceHub, checkout, shipment, projection, recovery, cache, replay, and management-screen runtime paths. The backend owns ownership, tenant scope, mutation, activation, deletion, selection, shipment binding, and governance. The frontend displays backend evidence and submits non-authoritative intent only.

## Runtime Ownership Model

- The only implemented ownership model is tenant ownership: SQL `ShippingMethod.TenantId`, assigned from `ITenantContext.RequiredTenantId` on server-side creation. There is no separate vendor, store, storefront, CommerceHub, carrier, or shipment ownership field/domain for shipping methods. In CommerceHub, `StoreId` is the tenant/store scope used to resolve that tenant's active SQL methods.
- The canonical owner is therefore the current tenant governed by backend role policy. Vendor A and Vendor B cannot own each other's method because a method cannot be created with caller-supplied `TenantId`, and read/update/delete predicates bind both `Id` and trusted tenant scope.
- Shipment, order, and CommerceHub sub-order method fields are backend-written lineage references and historical display snapshots, never ownership sources.

## Findings

- Ownership and mutation: `ShippingMethodsController` allocates IDs server-side, sets `TenantId` from trusted context, and scopes list, detail, update, deletion, calculation, and sync to the current tenant. Create/update/activation/deactivation are role protected; delete is a soft delete and emits a tombstone-triggering event.
- Vendor and tenant isolation: no vendor-specific shipping-method authority exists. POS tenant roles may submit configuration intent only within their trusted tenant. Cross-tenant IDs resolve as absent, and caller DTOs contain no ownership, tenant, or vendor assignment field.
- Storefront and CommerceHub: Storefront is read-only and receives active, tenant-scoped projection/cache evidence. CommerceHub resolves active methods from SQL using the store/tenant ID, recalculates pricing, and treats client option IDs as candidates. Neither path can confer or change ownership.
- Shipment: creation now requires both an active same-tenant SQL method and exact equality with the shipping method already bound to the authoritative order. A supplied different same-tenant method returns `SHIPMENT_SHIPPING_METHOD_MISMATCH` (409); shipment references and snapshots cannot substitute, create, or restore method ownership.
- Projection, cache, recovery, replay: the projection consumer reloads SQL by `{ShippingMethodId, TenantId}`, fences duplicate events, and writes freshness-aware tombstones. Mongo documents and cache keys are tenant scoped. Admin sync rebuilds from current tenant SQL and deletes stale tenant documents. Browser state, React state, storage, history/BFCache, cache contents, and replay can only display or submit candidates; checkout and shipment re-resolve SQL authority.
- Failures and adversarial input: unauthorized calls are blocked by role policy; malformed/empty/foreign/deleted/inactive IDs cannot resolve as current authority; forged tenant/vendor fields are not accepted by the mutation DTOs; cache/projection tampering cannot bypass SQL checkout/shipment resolution; stale event replay is fenced/tombstoned; conflicting shipment substitution is rejected.

## Runtime Hardening Performed

- Bound shipment creation to the order's existing authoritative `ShippingMethodId`, preventing an authenticated tenant caller from substituting another active same-tenant method during shipment creation.
- Added an explicit conflict error and controller mapping for that rejected substitution.

## Exact Runtime Files Modified

- `PointOfSale.API/Services/Shipping/ShipmentService.cs`
- `PointOfSale.API/Services/Shipping/IShipmentService.cs`
- `PointOfSale.API/Controllers/ShipmentController.cs`

## Regression Freeze Added

- `PointOfSale.Tests/Architecture/ShippingMethodOwnershipAuthorityCertificationTests.cs`

## Architecture Updates

- This certification record documents the tenant-only ownership model and the order-to-shipment method-binding invariant.

## Explicit Answers

- Who owns Shipping Method Ownership Authority? The backend: tenant-owned SQL `ShippingMethods`, trusted tenant context, backend roles, and SQL resolution/governance.
- Can frontend fabricate shipping method ownership? No.
- Can shipping method ownership emerge, replay, or restore? No; derivative state can only provide a candidate and current SQL decides authority.
- Can Vendor A own, modify, or restore Vendor B shipping method? No.
- Can Tenant A own Tenant B shipping method? No.
- Can storefront or CommerceHub usage become ownership? No.
- Can shipment references or snapshots become ownership? No.
- Can projections, cache, or replay restore ownership? No.
- Can tenant leakage occur? Not on the inspected paths; all authoritative resolution and projection/cache boundaries bind tenant scope.
- Were runtime changes required? Yes: shipment creation now rejects same-tenant method substitution against the backend-bound order selection.
- Is Shipping Method Ownership Authority production-certifiable? Yes, for the inspected runtime scope.
