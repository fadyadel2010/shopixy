# PASS M8-A Shipping Method Identity Authority Certification Findings

Date: 2026-06-24

## Verdict

Shipping Method Identity Authority is production-certifiable for the inspected POS, Storefront, CommerceHub cart/checkout, shipment, projection, cache, replay, vendor-configuration, and tenant-bound runtime paths. The backend owns identity creation, resolution, lineage, selection-candidate, scope, and governance authority. Frontends only display backend evidence and submit an identifier as non-authoritative intent.

## Runtime Identity Model

- The canonical identity is the server-generated, tenant-owned SQL `ShippingMethod.Id` (`Guid`). There is no implemented `ShippingMethodCode`, `CarrierId`, `VendorShippingMethodId`, `CommerceHubShippingMethodId`, or `TenantShippingMethodId` identity authority.
- `Name`, `NameAr`, `CarrierName`, `CarrierCode`, and `ServiceLevel` are configuration/display or carrier-routing attributes, not alternate identity keys. `CarrierCode` is registry-validated and normalized.
- Order and shipment shipping-method name values are immutable historical snapshots. CommerceHub sub-order `ShippingMethodId` and `ShippingSnapshotJson` are backend-created historical lineage, not a new CommerceHub identity domain.

## Findings

- Vendor/POS configuration: `ShippingMethodsController` allocates the ID server-side, derives tenant scope from trusted context, applies role authorization, and scopes reads, update, and soft delete by `{Id, TenantId}`. A vendor may submit configuration intent only through this backend-governed path; it cannot choose, reinstate, or cross-bind an ID.
- Storefront and CommerceHub: Storefront reads receive a projection of SQL identity. CommerceHub loads active SQL methods by the store/tenant ID. The client-supplied `ShippingOptionId` is checked against that current candidate set, while the service resolves the resulting selection itself. Storefront and CommerceHub never translate names, carrier values, projection IDs, or each other’s data into a new shipping-method identity.
- Cart and checkout: cart pricing and CommerceHub checkout bypass shipping-option cache and recalculate from current SQL candidates. Final checkout rejects stale or forged ID/price pairs with `SHIPPING_CHANGED`; checkout idempotency binds the request hash and rejects changed-payload replays. The standard checkout revalidates the selected method inside its final transaction, including tenant and deletion state.
- Shipment: shipment creation resolves the supplied method only from active same-tenant SQL and copies only historical name/carrier snapshots to the server-created shipment. Shipment IDs, tracking IDs, carrier external references, and method IDs are distinct identities; none can become another.
- Projections/cache/replay: the projection consumer re-fetches SQL rather than trusting event payloads, keys documents by `{TenantId, ShippingMethodId}`, fences duplicate events, and writes freshness-aware tombstones on deletion. Storefront cache/read paths are tenant-keyed derivative views. Cache, browser history/BFCache, storage, stale responses, and event replay can submit or display a candidate but cannot make it authoritative; checkout and shipment paths resolve current SQL state again.
- Tenant/failure/adversarial: empty or malformed IDs fail validation; missing/inactive/deleted/cross-tenant IDs fail resolution; unauthorized vendor operations fail authorization; malformed projection or currency evidence fails closed; duplicate/stale event application is fenced/tombstoned. Forged method, carrier, and historical snapshot values never bypass backend selection or governance.

## Runtime Hardening Performed

None. Runtime-first reinspection found the required identity, scope, resolution, replay, and cache controls already present. This pass adds a regression freeze only.

## Exact Runtime Files Modified

None.

## Regression Freeze Added

- `PointOfSale.Tests/Architecture/ShippingMethodIdentityAuthorityCertificationTests.cs`

## Explicit Answers

- Who owns Shipping Method Identity Authority? Backend tenant-owned SQL `ShippingMethods` and backend resolution/governance paths.
- Can frontend fabricate, emerge, replay, or restore shipping-method identity? No. It can only submit/display candidates; current backend SQL resolution decides acceptance.
- Can vendor fabricate or restore identity? No; backend generates IDs and scopes mutation/retrieval to the trusted tenant and roles.
- Can storefront identity become CommerceHub identity, or the reverse? No. Both reference the same backend SQL identity only after backend resolution; neither is an identity source.
- Can cart or checkout create shipping-method identity? No. They recalculate candidate selections from current backend authority.
- Can shipment identity become shipping-method identity? No, and a method ID cannot become a shipment ID.
- Can projections, cache, or replay restore identity authority? No. They are derivative and are revalidated/recomputed from SQL before an authoritative operation.
- Can tenant leakage occur? Not on inspected paths; SQL, projection, cache, and shipment predicates bind tenant/store scope and stale/ambiguous evidence fails closed.
- Were runtime changes required? No.
- Is Shipping Method Identity Authority production-certifiable? Yes, for the inspected runtime scope.
