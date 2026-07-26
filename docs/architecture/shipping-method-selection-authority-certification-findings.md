# PASS M8-D Shipping Method Selection Authority Certification Findings

Date: 2026-06-24

## Verdict

CommerceHub Shipping Method Selection Authority is production-certifiable for the inspected cart, pricing, checkout, split-order, shipment, projection, cache, recovery, replay, and tenant-bound runtime paths. Backend SQL and `CommerceHubShippingService` own candidate generation, eligibility, deterministic cheapest-method resolution, per-store assignment, cost aggregation, and final validation. Frontends display the server result and submit only stale-detection candidates.

## Selection Model

- For each distinct `StoreId`, `CommerceHubShippingService` loads current active, non-deleted, tenant-matching SQL methods and excludes carrier-managed methods without an enabled same-tenant carrier configuration.
- It calculates each candidate's effective price using authoritative subtotal and weight, removes non-eligible candidates, then selects exactly one method by effective price, `SortOrder`, and ordinal-ignore-case name. The submitted `ShippingOptionId` cannot change that decision.
- Cart pricing and final checkout bypass the five-minute candidate cache. A posted method, posted candidate list, posted cheapest claim, and posted price are compared with fresh backend resolution; disagreement is rejected as `SHIPPING_CHANGED`.

## Findings

- CommerceHub cart/checkout: clients can send one identifier per store only as backend-bound intent. The response and persisted sub-order are built from the recalculated `StoreSelections`, never from the request selection or request price.
- Vendor configuration: vendors can administer their own tenant's active methods and prices through backend roles, but configuration is candidate input only. It cannot force the selection path to accept an inactive, foreign, carrier-disabled, or non-cheapest method.
- Split orders: checkout groups validated lines by store and binds each sub-order to that store's server-resolved selection. Aggregated shipping is only the rounded sum of those selections; it does not choose or substitute a vendor's method.
- Shipment: ordinary and carrier shipment creation are constrained to the method already bound to the authoritative order and revalidate active same-tenant eligibility. Shipment request data, carrier data, and snapshots cannot replace the method.
- Projection/cache/recovery: shipping projections are tenant-keyed SQL-derived views with replay fences and tombstones. CommerceHub cart and checkout intentionally bypass candidate cache. Browser/React Query/history/BFCache/local/session storage may retain a display candidate, but fresh backend calculation is required before pricing or order creation.
- Replay/failure/adversarial: idempotency binds the full request hash; a changed replay fails with `IDEMPOTENCY_PAYLOAD_CONFLICT`. Forged method IDs, prices, candidate lists, cheapest claims, cross-store IDs, stale/disabled methods, or projection/cache values fail fresh SQL resolution or result in `SHIPPING_CHANGED`. Protected vendor/shipment boundaries enforce authorization; missing/invalid input and unavailable stores fail closed.

## Runtime Hardening Performed

None. Runtime-first inspection found that CommerceHub already resolves the cheapest eligible method server-side, revalidates cart and final checkout from SQL, binds each split sub-order to that result, and blocks shipment substitution. This pass adds an architecture regression freeze.

## Exact Runtime Files Modified

None.

## Regression Freeze Added

- `PointOfSale.Tests/Architecture/ShippingMethodSelectionAuthorityCertificationTests.cs`

## Explicit Answers

- Who owns Shipping Method Selection Authority? Backend SQL and `CommerceHubShippingService`.
- Can frontend, customer, cart, or checkout select shipping methods? No. They can submit a candidate, but backend deterministically chooses and verifies the cheapest eligible method.
- Can vendor force a method or override cheapest selection? No; vendor configuration only affects backend-governed candidates.
- Can CommerceHub select an ineligible method? No.
- Can Vendor A's method be assigned to Vendor B? No; each candidate query and sub-order binding use that store/tenant ID.
- Can shipment creation or shipping cost calculation override selection? No. Shipment creation requires the order-bound method; cost calculation evaluates candidates and does not accept a client choice.
- Can cache or replay restore selection authority? No. Cache is bypassed for cart/final checkout; replay is request-hash-bound and final operations revalidate current SQL.
- Can tenant leakage occur? Not on inspected paths; method resolution, split-order binding, projections, and shipment predicates are tenant/store scoped.
- Were runtime changes required? No.
- Is Shipping Method Selection Authority production-certifiable? Yes, for the inspected CommerceHub shipping-selection runtime scope.
