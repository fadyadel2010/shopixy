# PASS M7-E Shipment Action Authority Certification Findings

Date: 2026-06-23

## Verdict

Shipment Action Authority is production-certifiable for the inspected shipment, carrier, tracking, fulfillment, customer, guest, admin, recovery, projection, cache, replay, and tenant-bound runtime paths. The backend owns every action decision; frontend state is only disposable presentation evidence and backend-bound intent.

## Runtime-first action findings

- Creation is accepted only from `Owner`, `Admin`, or `Manager` roles and is revalidated against a current tenant-owned order, current tenant-owned active shipping method, financial readiness, one-active-shipment invariants, and a serializable transaction.
- Status, tracking, and cancellation actions are role-gated and tenant-scoped. `ShipmentService` and `AdminShipmentService` reload current SQL state, enforce `ShipmentLifecycleTransitionPolicy`, carrier/manual tracking ownership, financial gates, fulfillment synchronization, and transactional event emission.
- Carrier registration, retry, cancellation, tracking sync, and verified webhooks operate only on an existing tenant-bound shipment. Webhooks resolve an unambiguous `{carrier, shipment/tracking}` target, establish that tenant server-side, validate provider trust, deduplicate durable evidence, and then reapply lifecycle/financial/fulfillment guards.
- Fulfillment endpoints are role-gated and delegate to `IOrderFulfillmentService`; fulfillment synchronizes downstream from shipment state and is not a shipment writer or action grant. The cancellation endpoint is deliberately disabled in favor of the governed order cancellation lifecycle.
- Recovery discovers cross-tenant candidates only with query filters explicitly bypassed, then establishes the candidate tenant before calling the same guarded shipment/carrier services. It does not assign shipment status itself, so recovery cannot fabricate an action or evade lifecycle policy.
- Storefront customers have a read-only, current-JWT-and-order-ownership-checked shipment endpoint. Guest tracking is separately token/session-bound and read-only. No customer or guest shipment mutation route exists.
- The shipping UI accepts action buttons only from a current backend fulfillment-action response whose order ID exactly matches the displayed shipment. It re-fetches that evidence immediately before cancellation; malformed, stale, selected-row, route, cache, storage, or history evidence is withheld and never becomes an authority grant.

## Runtime hardening performed

- Bound `ShipmentService` shipping-method lookup explicitly to the supplied tenant, removing reliance on ambient filtering for this action input.
- Made recovery's cross-tenant discovery explicit and established the candidate tenant before every recovered create, carrier-registration retry, or authority reconciliation. Recovery continues through the normal backend services and their lifecycle gates.

## Exact runtime files modified

- `PointOfSale.API/Services/Shipping/ShipmentService.cs`
- `PointOfSale.API/Services/Shipping/ShipmentAuthorityRecoveryHostedService.cs`

## Regression freeze added

- `PointOfSale.Tests/Architecture/ShipmentActionAuthorityCertificationTests.cs`

## Explicit certification answers

- Who owns Shipment Action Authority? The backend: authenticated role policy, trusted tenant context, current SQL shipment/order lineage, lifecycle policy, carrier trust, financial/fulfillment guards, and transactional outbox.
- Can frontend fabricate shipment actions? No. It can submit intent, but it cannot grant authorization, select a lifecycle bypass, or make cached action evidence executable.
- Can shipment actions emerge, replay, or restore? No. Recovery and replay re-enter current backend scope, existing-entity, lifecycle, and idempotency guards.
- Can customer or guest tracking perform shipment actions? No; their shipment routes are read-only.
- Can vendor bypass shipment governance? No; tenant role checks and current SQL scope remain mandatory.
- Can admin bypass shipment policy? No; admin mutations use the same lifecycle, financial, carrier-tracking, fulfillment, and tenant constraints.
- Can carrier actions become shipment authority? No; they are trusted, scoped inputs to backend action policy for an existing shipment.
- Can tracking events execute shipment actions? Only as verified carrier inputs under backend policy; tracking evidence itself grants nothing.
- Can fulfillment perform shipment actions? No; it is downstream synchronization and cannot directly write shipment state.
- Can actions bypass lifecycle policy? No.
- Can cache or replay restore shipment actions? No; frontend candidates are withheld/refetched and backend replay/recovery is revalidated.
- Can Vendor A act on Vendor B shipment, or Carrier A act on Carrier B shipment? No; tenant/carrier target resolution is scoped and ambiguous/missing targets fail closed.
- Can tenant leakage occur? Not on the inspected paths.
- Were runtime changes required? Yes.
- Is Shipment Action Authority production-certifiable? Yes, for the inspected scope.
