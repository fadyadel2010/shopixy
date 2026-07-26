# PASS M8-H Shipping Method Final Authority Certification Findings

Date: 2026-06-25

## Verdict

Shipping Method Authority is production-certifiable after final runtime-first reinspection. Backend tenant-owned SQL `ShippingMethods`, trusted tenant/store resolution, vendor role authorization, `CommerceHubShippingService`, checkout final validation, split-order shipping assignment boundaries, order-bound shipment creation, same-tenant carrier configuration predicates, projection fences, cache admission, idempotency, recovery re-entry, and persistence guards own identity, ownership, eligibility, selection, cost, split-order assignment, scope isolation, lifecycle governance, persistence authority, and recovery authority. Frontend surfaces own only display, formatting, presentation, navigation, filtering, and backend-bound intent.

## Final Identity Findings

- Shipping method identity remains the backend-generated SQL `ShippingMethod.Id` scoped by `TenantId`; no frontend, Storefront, CommerceHub, carrier, shipment, order, projection, cache, replay, or recovery path creates an alternate identity authority.
- Names, carrier codes, service levels, snapshots, order fields, shipment fields, tracking references, and projection document IDs remain attributes or historical evidence only.

## Final Ownership Findings

- Ownership remains tenant/store ownership through SQL `ShippingMethods.TenantId`, assigned from trusted backend tenant context and governed by role-protected POS configuration endpoints.
- Storefront reads, CommerceHub selections, shipments, carrier registrations, order snapshots, and projections do not grant or transfer ownership.

## Final Eligibility Findings

- Eligibility remains backend-owned by active, non-deleted, same-tenant SQL method resolution plus enabled same-tenant carrier configuration for non-manual methods.
- Storefront display, carrier quotes, cached method rows, stale selections, forged DTOs, and historical snapshots cannot make an inactive, deleted, foreign, or carrier-disabled method eligible.

## Final Selection Findings

- CommerceHub selection remains backend-owned. `CommerceHubShippingService` generates current store-scoped candidates, calculates eligibility/cost, and deterministically chooses the cheapest eligible method by price, sort order, and name.
- Client-submitted option IDs and prices are stale-detection candidates only; cart and checkout bypass shipping-option cache and reject disagreement with `SHIPPING_CHANGED`.

## Final Cost Findings

- Shipping cost remains backend-owned by SQL shipping-method configuration and backend calculation services.
- Request totals, request prices, frontend previews, carrier quote displays, snapshots, projections, aggregation rows, and cache values are never accepted as authoritative cost writers.

## Final Split-Order Findings

- Split-order shipping assignment remains checkout-created from current backend `StoreSelections` and persists under the explicit `ShippingAssignment` boundary.
- `StoreId`, `Shipping`, `ShippingMethodId`, `ShippingMethodName`, and `ShippingSnapshotJson` are guarded against mutation outside that boundary; aggregation only sums persisted backend sub-order shipping.

## Final Scope Findings

- Scope remains tenant/store scoped through SQL predicates, trusted tenant/store lineage, Storefront tenant resolution, CommerceHub store candidate generation, projection keys, cache keys, and shipment/carrier same-tenant predicates.
- Storefront cached rows must carry current backend `TenantId` evidence or fail closed to Mongo/SQL re-entry.

## Final Shipment Findings

- Shipment creation remains order-bound and same-tenant. The requested method must equal the authoritative `Order.ShippingMethodId`, and the method must still be active, non-deleted, and carrier-eligible.
- Shipment identity, status, tracking, carrier references, and snapshots cannot become shipping-method authority.

## Final Carrier Findings

- Carrier shipment creation derives carrier and service level from the order-bound active shipping method and enabled same-tenant carrier configuration.
- Carrier quotes, callbacks, tracking events, carrier request IDs, and webhook payloads cannot create, replace, restore, or widen shipping-method authority.

## Final Projection Findings

- Mongo shipping-method projections re-fetch SQL by `{ShippingMethodId, TenantId}`, fence duplicate/stale events, tombstone deletes, and use tenant-keyed unique indexes.
- Order projections and repair/rebuild copy persisted SQL shipping evidence only; they do not write split-order shipping assignment or shipping-method configuration.

## Final Cache Findings

- Redis Storefront cache is tenant-keyed and row-level tenant-validated before display-price recalculation or response return.
- CommerceHub cart and checkout bypass candidate cache for authoritative calculation. Browser cache, BFCache, React Query, local/session storage, route/history, and in-memory state can only display or submit candidates and cannot survive current backend revalidation as authority.

## Final Replay Findings

- Checkout idempotency binds request hashes; changed replays conflict and identical replays return the already-created backend order rather than re-authoring shipping.
- Projection replay is fenced or tombstoned. Recovery replay re-enters backend cart, checkout, order, shipment, and projection authority instead of restoring authority from stale evidence.

## Final Recovery Findings

- Storefront abandoned-checkout recovery can restore a candidate `shippingMethodId` for UX continuity only; checkout refetches methods, marks stale selections, and final backend checkout revalidates SQL authority.
- Shipment recovery and projection repair read current SQL lineage and do not recreate shipping-method identity, selection, cost, scope, or split assignment.

## Final Cross-Domain Findings

- Order, shipment, tracking, carrier, payment, refund, return, earnings, settlement, payout, financial ledger, projections, caches, replay, and recovery are evidence or downstream consumers only.
- None of those domains can create, restore, widen, transfer, replay, replace, or lifecycle-advance Shipping Method Authority.

## Final Failure Findings

- `400` rejects malformed DTOs, missing selections, invalid tenant context, unavailable candidates, and invalid configuration evidence.
- `401`/`403` enforce authenticated and role-authorized admin, checkout, and shipment boundaries.
- `404` rejects absent, foreign, or unresolved methods.
- `409` rejects stale shipping candidates, idempotency payload conflicts, shipment method substitution, and changed backend shipping results.
- `422`-style malformed projection/cache evidence has no accepted authority path; malformed rows fail closed, are excluded, or re-enter SQL-backed recovery.

## Final Adversarial Findings

- Forged IDs, forged ownership, forged prices, forged candidates, forged tenant scope, forged vendor scope, projection tampering, replay tampering, cache tampering, carrier tampering, route/history/BFCache restoration, and React Query stale state cannot bypass backend governance.
- Tenant leakage was not found on inspected runtime paths.

## Runtime Hardening Performed

None. Final runtime reinspection found no new Shipping Method Authority gap after M8-A through M8-G controls were revalidated in composition.

## Exact Runtime Files Modified

None.

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/ShippingMethodFinalAuthorityCertificationTests.cs`

## Architecture Updates

- Updated all five `.ai-context` authority files with M8-H final authority truth, risk, hardening, and freeze rules.

## Explicit Certification Answers

- Who owns Shipping Method Authority? Backend tenant-owned SQL, trusted tenant/store context, CommerceHub shipping/checkout services, split-order persistence guards, shipment/order lineage, carrier predicates, projection fences, cache admission, idempotency, recovery re-entry, and governance.
- Can frontend fabricate shipping-method authority? No.
- Can identity emerge? No.
- Can ownership emerge? No.
- Can eligibility emerge? No.
- Can selection emerge? No.
- Can cost emerge? No.
- Can split-order authority emerge? No.
- Can scope emerge? No.
- Can shipment become shipping-method authority? No.
- Can carrier become shipping-method authority? No.
- Can order become shipping-method authority? No.
- Can projections restore authority? No.
- Can cache restore authority? No.
- Can replay restore authority? No.
- Can recovery restore authority? No.
- Can tenant leakage occur? Not on inspected runtime paths.
- Were runtime changes required? No.
- Is Shipping Method Authority production-certifiable? Yes.
