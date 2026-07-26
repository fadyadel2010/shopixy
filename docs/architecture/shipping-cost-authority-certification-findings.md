# PASS M8-E Shipping Cost Authority Certification Findings

Date: 2026-06-24

## Verdict

Shipping Cost Authority is production-certifiable for the inspected Shipping Method configuration, Storefront checkout, CommerceHub cart and checkout, split orders, shipment creation, SQL snapshots, Mongo projections, recovery, caches, browser persistence, replay, vendor configuration, and tenant-bound runtime paths. Backend SQL and backend calculation services own shipping cost truth. Frontends display backend evidence and submit only non-authoritative stale-detection candidates.

## Actual Cost Model

- `ShippingMethod.BaseRate`, optional `PerKgRate`, and optional `FreeShippingThreshold` are backend-managed tenant configuration. `ShippingCalculationService` applies free shipping when authoritative subtotal reaches the threshold; otherwise it calculates `max(0, base rate + per-kg rate * non-negative authoritative weight)` and rounds to two decimals.
- CommerceHub derives subtotal from current SQL price authority and derives weight from the validated SQL variant weight, falling back to product weight. For each store it loads current active, non-deleted same-tenant methods, excludes disabled carrier configurations, evaluates the effective price, and deterministically chooses the cheapest eligible result.
- Carrier quote configuration (`FixedShippingCost`, carrier rates, and carrier free-shipping threshold) is used by the separate backend quote service. It is not an accepted CommerceHub checkout price source and no client carrier quote becomes order cost truth.

## Findings

- **Cost and CommerceHub:** cart pricing and final checkout bypass the five-minute candidate cache and recompute per-store prices from current SQL. Posted `ShippingTotal`, `ShippingPrice`, and method IDs are compared only for stale detection; disagreement returns `SHIPPING_CHANGED`. Persisted sub-order shipping comes only from `shippingResult.StoreSelections`, and marketplace shipping is the rounded sum of those server-created sub-order amounts.
- **Vendor and tenant scope:** vendors can change only their own shipping-method configuration through trusted tenant context and roles. That configuration is candidate input, not an order override. Method lookup binds `TenantId == StoreId`; Vendor A cannot supply a method or calculated price for Vendor B.
- **Split order and aggregation:** each validated store group gets one backend-resolved selection. Aggregation never accepts a request total and cannot substitute a store selection. A vendor's amount stays on its own sub-order before the marketplace total is summed.
- **Shipment and snapshots:** shipment creation copies `Order.ShippingCost` only after current order/method lineage is resolved; it does not calculate or mutate cost. `ShippingSnapshotJson` is built by checkout from the backend result and is historical evidence, not a writer or recalculation source.
- **Projection and recovery:** order projection and repair read SQL `ShippingTotal`, sub-order `Shipping`, and snapshots, then serialize derivative documents with checksum/version fences. Shipping-method projection re-fetches SQL and invalidates tenant-scoped cache. No projection or repair path is a source for cart calculation, checkout, or shipment cost.
- **Cache and replay:** browser persistence retains cart items and presentation fields only; the cart-sync DTO has no shipping total or shipping price. React Query is in-memory and checkout performs a fresh backend pricing call. Refresh, reconnect, history/BFCache, storage, stale responses, and cache tampering can at most display or submit a candidate. Checkout idempotency is payload-bound; a changed replay conflicts, while an identical replay returns the already backend-created order rather than re-authoring a cost.
- **Failure and adversarial behavior:** unauthenticated/unauthorized configuration or shipment operations are rejected by endpoint authorization; absent/invalid candidate data is rejected with 400/409; stale or forged totals, prices, estimates, aggregate claims, method IDs, cross-store IDs, caches, projections, and replay payloads cannot pass fresh SQL calculation. Carrier, cart, checkout, vendor, and shipment callers have no accepted authoritative shipping-cost field.

## Runtime Hardening Performed

None. Runtime-first reinspection found that the authoritative calculation, fresh checkout validation, split-order binding, aggregation, SQL-created snapshots, and derivative projection/cache boundaries already prevent shipping-cost authority from emerging outside the backend.

## Exact Runtime Files Modified

None.

## Regression Freeze Added

- `PointOfSale.Tests/Architecture/ShippingCostAuthorityCertificationTests.cs`

## Architecture Updates

- Added PASS M8-E ownership and prevention rules to all five authority-context files.

## Explicit Answers

- Who owns Shipping Cost Authority? Backend SQL configuration, `ShippingCalculationService`, `CommerceHubShippingService`, and final checkout governance.
- Can frontend calculate authoritative shipping cost? No.
- Can customer, cart, or checkout set shipping cost? No; posted values are stale-detection candidates only.
- Can vendor override shipping cost or bypass pricing rules? No; a vendor can govern only its tenant configuration through backend roles, which is evaluated by backend rules.
- Can CommerceHub use forged shipping totals? No.
- Can Vendor A cost become Vendor B cost? No.
- Can aggregated totals override authoritative totals? No; aggregation sums server-resolved sub-orders only.
- Can shipment snapshots become shipping cost authority? No.
- Can cache or replay restore shipping cost authority? No.
- Can tenant leakage occur? Not on the inspected paths; candidate lookup, sub-order binding, projection keys, and shipment lineage remain tenant/store scoped.
- Were runtime changes required? No.
- Is Shipping Cost Authority production-certifiable? Yes, for the inspected runtime scope.
