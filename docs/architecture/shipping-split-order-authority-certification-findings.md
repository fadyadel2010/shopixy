# PASS M8-F Shipping Split Order Authority Certification Findings

Status: production-certifiable for the inspected CommerceHub runtime boundary.

## Runtime findings

CommerceHub checkout groups SQL-authoritative listing lines by `StoreId`, recalculates each store's eligible shipping options against SQL shipping-method configuration, and chooses one deterministic cheapest method per store. Checkout bypasses the shipping-option cache, rejects supplied total, method, or price disagreements, and persists the resolved method, cost, name, and snapshot on that store's `CommerceHubSubOrder`. Marketplace shipping is the sum of persisted sub-order shipping; it is not an authority source.

The shipping method query requires `ShippingMethod.TenantId == storeId`, active/non-deleted state, and enabled carrier configuration where applicable. Thus Vendor A's method or cost cannot bind Vendor B's sub-order. Customer store IDs are cross-checked against locked-cart and live listing lineage before the split is created.

Sub-order shipping fields are immutable after creation. M8-F additionally places them behind an explicit shipping-assignment persistence boundary; broad order lifecycle, payment, financial, projection, repair, recovery, and shipment code cannot mutate them. A replayed checkout must match the customer-bound request hash and returns the original response only; it cannot reassign shipping.

Mongo order projections and repair/rebuild construct documents from SQL marketplace/sub-order fields and a checksum/version fence. They copy shipping evidence; they never write sub-order shipping. Cart, checkout, React Query, browser cache/history/BFCache, local/session storage, and recovery paths submit candidates or display current backend evidence only. Final checkout uses fresh SQL, so cache/replay cannot restore shipping authority.

Generic shipment and carrier orchestration is scoped to canonical `Order`/`Shipment` lineage and does not reference `CommerceHubMarketplaceOrder` or `CommerceHubSubOrder`; it therefore cannot rebind split-order vendor, method, or cost. Shipment creation is not a split-order shipping authority path.

## Failure and adversarial findings

Missing/invalid selection, forged method, forged cost, or changed server result fails with `400`/`409`; customer identity is required (`401`); live listing/store/tenant or shipping-method authority loss fails closed before checkout/payment/fulfillment. Idempotency request-hash mismatch is `409`. SQL tenant-scoped lookup rejects Vendor A/Vendor B substitution. Projection/cache tampering can affect only derivative data and is overwritten or rejected on current SQL re-entry.

## Runtime hardening performed

- Added `ShippingAssignment` to `CommerceHubBoundaryMutationScope`.
- Checkout explicitly enters that scope while creating split-order shipping evidence.
- Added an ApplicationDbContext persistence guard for `StoreId`, `Shipping`, `ShippingMethodId`, `ShippingMethodName`, and `ShippingSnapshotJson`.
- Added behavioral and architecture regression freezes.

## Exact runtime files modified

- `PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs`
- `PointOfSale.Infrastructure/Data/CommerceHubBoundaryMutationScope.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`

## Regression freezes added

- `PointOfSale.Tests/CommerceHub/CommerceHubDataAuthorityBoundaryTests.cs`
- `PointOfSale.Tests/Architecture/ShippingSplitOrderAuthorityCertificationTests.cs`

## Explicit certification answers

- Who owns Split Order Shipping Authority? Backend checkout, shipping service, SQL sub-order persistence, tenant/vendor lineage, and governance.
- Can frontend assign shipping to sub-orders? No.
- Can customer influence shipping assignment? Only by submitting a candidate selection; backend recomputes and decides.
- Can checkout rebind shipping? No; it creates a fresh immutable binding only after fresh SQL validation.
- Can Vendor A shipping/method/cost become Vendor B shipping/method/cost? No.
- Can aggregation override sub-order shipping? No; it is a sum of sub-order values.
- Can shipment creation override split-order shipping? No; it is a separate canonical-order path.
- Can projections, cache, or replay restore shipping assignment? No; all are derivative/revalidated evidence.
- Can tenant leakage occur? No on the inspected vendor-scoped SQL paths.
- Were runtime changes required? Yes: an explicit split-order shipping persistence boundary was added.
- Is Split Order Shipping Authority production-certifiable? Yes.
