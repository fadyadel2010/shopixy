# PASS SF-SHP5 Shipment Lifecycle Authority Certification Findings

Date: 2026-07-03

## Verdict

Shipment Lifecycle Authority is production-certifiable for the inspected Shipment Lifecycle runtime after carrier initial-status admission and duplicate transition replay hardening.

## Runtime-first findings

- Lifecycle states: persisted shipment lifecycle states are `PendingExternalRegistration`, `Pending`, `ReadyForPickup`, `PickedUp`, `InTransit`, `OutForDelivery`, `Delivered`, `Failed`, `Cancelled`, and `Returned`. The business labels `ReadyToShip` and `AwaitingPickup` map to `ReadyForPickup`; `DeliveryFailed` maps to `Failed`. `ReturnRequested` and `Completed` are not shipment lifecycle states and cannot be persisted as shipment status.
- Transition ownership: `ShipmentService`, `AdminShipmentService`, carrier registration, carrier sync, verified carrier webhooks, and carrier cancellation are backend writers. Every status-changing writer must pass `ShipmentLifecycleTransitionPolicy` before SQL mutation.
- Creation: shipment begins only when backend code creates a SQL `Shipment` from trusted tenant context, current SQL order authority, order-bound shipping method evidence, payment/accounting readiness, tenant-owned shipping method configuration, and carrier configuration where required. Frontend supplies only a request candidate.
- Carrier mapping: carrier adapters produce status evidence only. Carrier create responses now must pass response admission and the canonical transition policy before initial status persistence, so a provider cannot skip `PendingExternalRegistration` or `Pending` directly to a later lifecycle state.
- Cancellation, completion, failure, and return admission: cancellation is a canonical transition, not an administrative overwrite. Completion is represented by backend-owned delivered/order/fulfillment synchronization, not a separate shipment status. Failure and returned admission are policy-gated and terminal.
- Replay and duplicates: direct shipment updates and admin updates now treat same-status submissions as no-op DTO returns without SQL mutation or event emission. Carrier sync/webhooks already compute `statusChanged` and only emit on accepted changes; webhook event identity and payload hash are persisted in SQL `ShippingWebhookLogs`.
- Tenant, order, and customer binding: reads and writes use trusted `ITenantContext`, current SQL shipment/order lineage, role authorization, customer-order proof for Storefront reads, and verified carrier trust for callbacks/webhooks.
- Cache and projection: React, React Query, Zustand, Redis, Mongo projections, recovery, frontend timelines, and storefront/admin status badges present accepted backend DTOs only. Recovery recomposes SQL shipment/order authority and does not write shipment status from cache or projection.
- Boundaries: Orders, Checkout, Payment, Inventory, Warehouse, Affiliate, CommerceHub, Marketplace, returns, fulfillment, and tracking are adjacent evidence or consumers only. They cannot overwrite shipment lifecycle; lifecycle changes re-enter backend shipment authority and guarded fulfillment/order synchronization.

## Runtime hardening performed

- Rejected successful carrier create results whose `InitialStatus` cannot transition from the current SQL shipment status through `ShipmentLifecycleTransitionPolicy`.
- Added direct `ShipmentService.UpdateStatusAsync` same-status replay no-op before mutation, fulfillment synchronization, and event emission.
- Added `AdminShipmentService.UpdateShipmentStatusAsync` same-status replay no-op before mutation, fulfillment synchronization, and event emission.
- Expanded `ShipmentLifecycleAuthorityCertificationTests` to freeze canonical states, allowed/illegal transitions, carrier mapping admission, duplicate/replay handling, frontend cache non-authority, and SF-SHP5 documentation/context artifacts.

## Exact runtime files modified

- `PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs`
- `PointOfSale.API/Services/Shipping/ShipmentService.cs`
- `PointOfSale.API/Services/Admin/AdminShipmentService.cs`

## Regression freeze updated

- `PointOfSale.Tests/Architecture/ShipmentLifecycleAuthorityCertificationTests.cs`

## Explicit certification answers

- Did you review only Shipment Lifecycle runtime? Yes.
- Which frontend files were modified? None.
- Which backend files were modified? `CarrierShipmentService.cs`, `ShipmentService.cs`, and `AdminShipmentService.cs`.
- Shipment Identity owner: backend SQL `Shipments`.
- Shipment Lifecycle owner: backend SQL shipment aggregate plus `ShipmentLifecycleTransitionPolicy`.
- Shipment Status owner: backend SQL `Shipments.Status`.
- Carrier Status Mapping owner: backend carrier adapter evidence admitted by backend carrier service and lifecycle policy.
- Transition Rules owner: backend `ShipmentLifecycleTransitionPolicy`.
- Shipment Creation owner: backend shipment service/carrier service after SQL order, tenant, shipping method, payment/accounting, and carrier configuration checks.
- Shipment Cancellation owner: backend admin/carrier cancellation paths through canonical transition policy.
- Shipment Completion owner: backend delivered transition plus guarded fulfillment/order synchronization; no separate frontend completion state.
- Shipment Failure owner: backend policy-gated `Failed` transition.
- Return Admission owner: backend policy-gated `Returned` transition after delivered/in-flight/out-for-delivery evidence.
- Order Binding owner: backend SQL order/shipment lineage.
- Customer Binding owner: backend SQL order customer/session ownership for Storefront reads.
- Tenant Scope owner: trusted backend tenant context and tenant-scoped SQL predicates.
- SQL Truth owner: PostgreSQL `Shipments`, `Orders`, and `ShippingWebhookLogs` where applicable.
- Can frontend fabricate Shipment Lifecycle? No.
- Can carrier fabricate Shipment Lifecycle? No. Carrier evidence must resolve an existing shipment, pass trust/idempotency, and pass backend transition policy.
- Can replay fabricate transitions? No. Same-status direct/admin replays are no-op, carrier duplicates collapse, and recovery recomposes SQL truth.
- Can duplicate webhook transition twice? No. SQL webhook idempotency and `statusChanged` prevent a second lifecycle mutation.
- Can Shipment skip states? No, except policy-declared direct transitions such as `InTransit -> Delivered`; carrier creation cannot skip beyond the current policy.
- Can Shipment move backwards? No.
- Can React become Shipment Authority? No.
- Can React Query become Authority? No.
- Can Zustand become Authority? No.
- Can Redis become Authority? No.
- Can Mongo Projection become Authority? No.
- Where does Shipment begin? Backend SQL `Shipments` creation.
- Where does Lifecycle begin? The initial backend SQL shipment status assigned during creation.
- Where does Transition begin? Backend lifecycle admission through `ShipmentLifecycleTransitionPolicy`.
- Where does SQL persistence begin? SQL `Shipments` within backend transaction/persistence boundaries, plus `ShippingWebhookLogs` for webhook idempotency.
- Where does Presentation begin? After accepted backend DTO emission and frontend rendering.
- Were runtime changes required? Yes.
- Is Shipment Lifecycle production-certifiable? Yes.
