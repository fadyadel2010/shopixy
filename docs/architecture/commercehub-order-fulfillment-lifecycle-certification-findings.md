# CommerceHub Order & Fulfillment Lifecycle Certification Findings

Scope: PASS 16.5A
Date: 2026-06-01

## Order Lifecycle Inventory

SQL marketplace order statuses are `Pending`, `Confirmed`, `Processing`, `Shipped`, `Delivered`, `Completed`, `Cancelled`, `Refunded`, and `OnHold`.

Allowed CommerceHub vendor sub-order transitions are:

- `Pending -> Processing`, `Pending -> Cancelled`
- `Confirmed -> Processing`, `Confirmed -> Cancelled`
- `Processing -> Shipped`, `Processing -> Cancelled`
- `Shipped -> Delivered`, `Shipped -> Cancelled`
- `OnHold -> Cancelled`
- Same-status replay is a no-op.

Forbidden transitions include `Pending -> Delivered`, `Cancelled -> Shipped`, `Returned -> Processing` by equivalent return state, `Delivered -> Confirmed`, `Refunded -> Delivered`, `Completed -> Processing`, and any transition out of `Cancelled`, `Delivered`, `Completed`, or `Refunded` through POS Admin fulfillment.

Adjacent lifecycle inventories:

- POS fulfillment `FulfillmentStatus`: `Created`, `Confirmed`, `Packed`, `OutForDelivery`, `Delivered`, `Completed`, `Returned`, `FailedDelivery`, `Cancelled`.
- CommerceHub return status: `Pending`, `PickupScheduled`, `PickedUp`, `InTransit`, `DeliveredToVendor`, `Confirmed`, `Failed`.
- CommerceHub refund request status: `Requested`, `UnderReview`, `Approved`, `Rejected`, `Cancelled`, `Processing`, `Completed`, `Failed`, `WaitingForReturn`, `AwaitingPayout`, `Paid`.
- CommerceHub refund execution status: `Pending`, `Processing`, `Completed`, `Failed`, `ReconciliationRequired`.

## Ownership Findings

Marketplace order ownership is split deliberately. `CommerceHubMarketplaceOrder.CustomerId` owns customer visibility. `CommerceHubSubOrder.StoreId` owns merchant/POS fulfillment authority. Multi-vendor marketplace status is aggregate state over SQL sub-orders. Frontend state, MongoDB order documents, Redis, Elasticsearch, and events are derivative evidence only.

Customer order reads require either authenticated `CommerceHubCustomerId == CustomerId` or a signed order access token validated against SQL order metadata. POS Admin mutation requires the current tenant context and an exact `subOrder.StoreId == storeId` check. Super Admin order surfaces are global SQL read/search/export surfaces.

## POS Admin Authority Findings

POS Admin fulfillment authority is owned by `CommerceHubOrderManagementService.UpdateSubOrderStatusAsync`. The controller resolves tenant context from `ITenantContext`, not client-submitted tenant ids. The service loads the marketplace order/sub-orders/items under serializable transaction and provider row locks, rejects mismatched `StoreId`, validates the transition graph, validates payment readiness before outbound fulfillment, records status history, updates item state, applies delivery/cancellation inventory side effects, and publishes status events.

POS Admin cannot mutate another tenant's sub-order through the reviewed endpoint.

## Super Admin Authority Findings

Super Admin order service is read-only for order state: list, stats, detail, and CSV export use SQL. No Super Admin order-state override endpoint was found for CommerceHub order or sub-order status.

Super Admin has refund review/execution authority but read-only return monitoring. The owning POS vendor controls audited, serializable, policy-bounded return logistics and receipt; no Super Admin return override or failure endpoint exists.

## Status Transition Findings

CommerceHub order transitions are explicitly modeled in `CommerceHubOrderStateHelper` and enforced by `CommerceHubOrderManagementService`. `Delivered`, `Completed`, and `Refunded` vendor states are terminal for POS Admin mutation. Cancellation and refund terminal states do not expose forward transitions. Same-state replay collapses without mutation.

The DbContext guard also blocks persistence of marketplace order/sub-order lifecycle fields outside `CommerceHubBoundaryMutationScope.BeginOrderLifecycle()`, so direct EF status writes fail closed.

## Cancellation Findings

Vendor cancellation is allowed only before terminal fulfillment states and releases order-item reservations through `InventoryConsistencyService.ReleaseOrderItemReservationsAsync`. Marketplace status aggregates across sub-orders, so multi-vendor partial cancellation does not incorrectly cancel unrelated stores. Cancelled sub-orders cannot re-enter fulfillment through the certified POS Admin path.

## Delivery Findings

Delivery is only reachable from `Shipped` in the CommerceHub vendor graph and requires paid/COD readiness. Delivery commits inventory through `CommitDeliveryAsync`, updates delivered item timestamps, applies delivered financial workflow, publishes projection events, and locks the delivered state from later POS Admin mutation.

## Return Findings

Return workflow is refund-owned: approved refund requests create/maintain `CommerceHubReturnRequest` rows. Return transitions run under serializable transactions and `FOR UPDATE` locks, validate with `CommerceHubReturnPolicy`, append audit logs, and synchronize refund status to `WaitingForReturn` or `AwaitingPayout` only when the configured execution trigger milestone is reached.

Confirmed return receipt is inventory-bearing, vendor-scoped, and terminal. There is no generic Super Admin return override path.

## Refund Trigger Findings

Refund execution is separated from order delivery. Refund creation validates tenant-owned sub-order, paid payment state, cumulative refund totals, payout overlap, and safety/readiness gates. Return-dependent refunds cannot execute until `CommerceHubReturnPolicy.IsExecutionTriggerReached(...)` accepts the current return status. Full refunds mark sub-orders `Refunded` and aggregate marketplace status; partial refunds update payment/refund state without forcing order-state corruption.

## Dashboard And Projection Findings

Merchant, customer, and Super Admin reads use SQL authority for critical lifecycle views. Customer critical order detail can invoke SQL-backed projection repair before reading MongoDB. Projection repair and read repositories preserve source mutation versions, customer id, event versions, and freshness markers so duplicate or stale Mongo documents do not become authority.

## Frontend Findings

Customer order/refund pages render backend status/action DTOs and customer ownership results. POS Admin exposes tenant-scoped sub-order fulfillment plus backend-granted vendor return logistics and receipt actions. Super Admin order UI is read/search/export oriented; its refund console exposes backend-derived refund actions and read-only return state.

## Replay And Recovery Findings

Status replay collapses on same current state, completed vendor states, projection mutation versions, and SQL-first projection repair. Refund recovery reloads current SQL refund/payment/order/return state under lifecycle boundaries and routes uncertain provider outcomes to `ReconciliationRequired` instead of forcing terminal success. Recovery does not use MongoDB or Elasticsearch as order authority.

## Auditability Findings

CommerceHub sub-order status transitions append `CommerceHubSubOrderStatusHistory`. Return operations append tenant audit logs with actor/source/reason/evidence fields. Refund approval/execution and financial side effects write refund, financial audit, ledger, and status-event evidence. Incidents are reconstructable for certified SQL lifecycle paths, but deployment retention/alert evidence remains a broader launch blocker.

## Chaos Findings

Reviewed chaos cases:

- Cross-tenant fulfillment: blocked by `subOrder.StoreId != storeId`.
- Invalid transitions: blocked by `CanTransition`.
- Double delivery: terminal delivered state blocks mutation.
- Double cancellation: terminal cancelled state has no forward path.
- Double return: return rows are tied to refund request and serialized.
- Double refund trigger: cumulative refund validation, idempotency, and return milestone checks apply.
- Projection drift: repaired from SQL before critical customer detail.
- State resurrection: terminal and boundary guards prevent reviewed runtime resurrection.
- Super Admin bypass: no order-state or return-state override endpoint exists; refund actions remain separately audited and restricted.

## Monitoring Findings

Existing telemetry/logging covers blocked fulfillment, status events, refund recovery, reconciliation-required outcomes, projection repair failures, and financial readiness gates. Remaining monitoring debt is a dedicated order-lifecycle dashboard/alert pack for invalid transition attempts, tenant-scope rejections, projection lag, return SLA drift, and refund-trigger stalls.

## Runtime Hardening Performed

No runtime code hardening was required in this pass. Existing SQL boundary guards, serializable lifecycle services, transition validation, inventory/refund coupling, projection repair, and audit evidence satisfied the reviewed runtime certification boundary.

## Regression Freeze Added

Added:

- `CommerceHubOrderLifecycleCertificationTests`
- `CommerceHubFulfillmentAuthorityCertificationTests`
- `CommerceHubOrderTransitionCertificationTests`

The freeze covers order ownership, fulfillment authority, Super Admin authority posture, status transitions, cancellation, delivery, return, refund trigger, replay/recovery, projection behavior, and frontend/backend alignment.

## Architecture Updates

PASS 16.5A entries were added to `.ai-context/PROJECT_CONTEXT.md`, `.ai-context/ARCHITECTURE_TRUTH.md`, `.ai-context/KNOWN_RISKS.md`, `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`, and `.ai-context/CODEX_RULES.md`.

## Remaining Debt

CommerceHub order lifecycle runtime is production-certifiable, but public launch remains blocked by the broader PASS 15.6 and PASS 16.1B deployment evidence, external retention, alerting, SMTP/session, backup/restore, and chaos-proof requirements. Order-specific maturity debt remains: dedicated lifecycle monitoring dashboard, invalid-transition alert pack, production SQL concurrency chaos harness, explicit Super Admin order repair workflow if needed, and richer immutable operator notifications.

## Required Answers

1. Who owns order fulfillment authority? POS Admin owns tenant-scoped CommerceHub sub-order fulfillment through backend SQL lifecycle services; generic POS `Order` fulfillment remains owned by `IOrderFulfillmentService`.
2. Can POS Admin mutate another tenant's orders? No, the certified endpoint rejects sub-orders whose `StoreId` differs from the current tenant.
3. Can Super Admin override order or return state? No CommerceHub order-state or return-state override endpoint exists. Super Admin retains audited refund operations and read-only return monitoring.
4. Are return operations audited? Yes. Owning-vendor return transitions and Super Admin refund operations are audited separately.
5. Can invalid status transitions occur? Not through reviewed runtime paths; they are blocked by transition validation and DbContext boundary guards.
6. Can cancelled orders resurrect? Not through reviewed CommerceHub fulfillment paths.
7. Can delivered orders regress? Not through reviewed POS Admin fulfillment paths; delivered vendor orders are terminal for mutation.
8. Can replay/recovery corrupt lifecycle state? Reviewed replay/recovery paths reload SQL authority and collapse stale or uncertain state rather than resurrecting it.
9. Are lifecycle incidents reconstructable? Yes for certified SQL lifecycle paths, with remaining deployment retention and alerting prerequisites inherited from prior readiness passes.
10. Is CommerceHub order lifecycle production-ready? Runtime order lifecycle is production-certifiable; public launch remains not ready until broader deployment/readiness blockers are closed.
