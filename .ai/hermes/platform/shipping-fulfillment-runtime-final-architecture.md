# FINAL CANONICAL ARCHITECTURE — SHIPPING / FULFILLMENT RUNTIME (Shopixy / PointOfSale)

> Status: DEFINITIVE, implementation-backed specification.
> Authority rule: **Implementation is the ONLY source of truth.** Every architectural
> claim is cited to `file:line` in the C# backend (`PointOfSale.API`, `PointOfSale.Core`,
> `PointOfSale.Infrastructure`). Markdown, XML comments, controller names, DTO names and
> prior certification docs are treated as *secondary evidence only* — where a comment
> claims a behaviour the code does not enforce, that gap is recorded as a finding, never
> repeated as truth.
> Scope: the tenant storefront/omnichannel **Shipment + Order Fulfillment** runtime
> (`PointOfSale.Core/Entities/Shipment.cs`, `PointOfSale.Core/Enums/FulfillmentStatus.cs`,
> `PointOfSale.Infrastructure/Fulfillment/OrderFulfillmentService.cs`). This runtime is a
> DISTINCT runtime from the CommerceHub marketplace sub-order fulfillment surface
> (`CommerceHubSubOrder`) and from POS `SalesInvoice`. The divergence between the three is
> documented explicitly (§ Runtime Authority Divergence).

---

## 1. Executive Summary

The Shipping/Fulfillment runtime is **not a single thing called "Shipping"** and **not a
single thing called "Fulfillment."** Implementation proves it is a **two-aggregate,
three-authority-boundary coordination runtime**:

1. **The `Shipment` aggregate** — a carrier-agnostic physical-logistics entity
   (`Shipment.cs:26`). It is the authoritative record of *a carrier's handling of one
   order's parcel*: tracking number, carrier code, COD, package, label, external
   registration state. It is explicitly NOT the carrier, NOT the order
   (`Shipment.cs:13` "Order ≠ Shipment ≠ Carrier").
2. **The `FulfillmentStatus` state machine** — a column on the `Order` aggregate owned
   exclusively by `IOrderFulfillmentService` (`FulfillmentStatus.cs:11`,
   `IOrderFulfillmentService.cs:33`). It is the *physical-recognition axis* of the order
   (parallel to PaymentStatus and CheckoutState) and is the only writer of
   `Order.FulfillmentStatus`, fail-closed at `SaveChanges`
   (`FulfillmentStatusSaveChangesInterceptor.cs:21`).
3. **The carrier-integration seam** — `IShippingCarrierAdapter` + `CarrierShipmentService`
   + unauthenticated webhook pipeline. The `Shipment` entity is the ONLY carrier-specific
   store; core never parses `CarrierMetadata` (`Shipment.cs:353`).

**The runtime converts an immutable, paid, inventory-reserved Order into a physically
delivered customer parcel while preserving five invariants**: (a) SQL-authoritative
state, (b) payment-before-outbound gating, (c) shipment-projection-to-fulfillment
convergence via a single funnel, (d) carrier webhook trust isolating external writes to
the Shipment only, and (e) COD recognition triggered exactly at delivery.

Load-bearing evidence:

- `Order.FulfillmentStatus` can ONLY be mutated by `IOrderFulfillmentService`; direct
  assignment throws `InvalidOperationException` at `SaveChanges`
  (`FulfillmentStatusSaveChangesInterceptor.cs:87-98`). All transitions route through
  `TransitionAsync` which opens a `Serializable` tx, row-locks the order `FOR UPDATE`
  (`OrderFulfillmentService.cs:545,731`), validates the source against `FulfillmentMode`,
  validates the transition table, and only then sets the status
  (`OrderFulfillmentService.cs:533-721`).
- The `Shipment` is the integration spine: `ShipmentService.UpdateStatusAsync` and the
  carrier webhook both call `SynchronizeFulfillmentAuthorityAsync` →
  `_fulfillmentService.SynchronizeFromShipmentAsync` — **the shipment never writes
  `Order.FulfillmentStatus` directly** (`ShipmentService.cs:1102-1143`,
  `CarrierShipmentService.cs:1718-1723`). This is the runtime's crown-jewel authority rule.
- Outbound progression (Confirmed/Packed/OutForDelivery/Delivered/Completed) is **gated by
  payment + collection-accounting authority** before it can advance
  (`OrderOperationalFinancialAuthority.cs:22-52`, `ShipmentService.cs:929-959`). A failed
  shipment does **NOT** change `Order.PaymentStatus` (payment lifecycle is independent;
  only `FulfillmentStatus` diverges).
- COD revenue is recognized at delivery: `FulfillmentDeliveredEvent` →
  `FulfillmentDeliveredEventConsumer` → `IPaymentLifecycleService.MarkOfflineCollectionDeliveredAsync`
  (`OrderDeliveredEventConsumer.cs:55-94`, `Program.cs:1853`).

**Answer to "what is Shipping / what is Fulfillment":** the runtime is a **Fulfillment
Lifecycle Runtime wrapped around a carrier-agnostic Shipment entity.** "Shipping" in this
codebase is (a) the *tenant-configured delivery method* (`ShippingMethod` / pricing) and
(b) the *carrier logistics* captured by `Shipment`. "Fulfillment" is the *order's physical
state machine*. They are **separate but nested**: one `Shipment` converges into the
order's `FulfillmentStatus`, but Shipment status and Fulfillment status are NOT 1:1 and
diverge by design (§ Runtime Authority Divergence).

---

## 2. Architecture Vision

The Shipping/Fulfillment runtime exists to convert a *mutable, paid, inventory-reserved
order* into a *physically-tracked, carrier-handled, customer-delivered parcel*, and to act
as the single convergence point between carrier logistics and the order's physical state
machine — **without ever letting external carrier data, a derived read-model, or a manual
admin action become the authority over `Order.FulfillmentStatus`.**

Design pillars, each code-cited:

1. **One authorized mutator per concern (fail-closed).** `Order.FulfillmentStatus` is
   written only by `IOrderFulfillmentService`; `Shipment.Status` only by `ShipmentService`
   (admin/manual) and `CarrierShipmentService` (webhook/registration), each validating the
   canonical `ShipmentLifecycleTransitionPolicy` (`ShipmentService.cs:541-544`,
   `CarrierShipmentService.cs:1644-1695`, `ShipmentLifecycleTransitionPolicy.cs:10`).
2. **SQL is truth; everything else is derived.** Shipments and the fulfillment status live
   in PostgreSQL under the tenant global query filter. The storefront read model
   (`StorefrontShipmentDto`) is a projection of SQL (`ShipmentService.cs:504-535`);
   there is NO Mongo shipment document consumed by the runtime — storefront reads SQL.
3. **Shipment converges INTO fulfillment, never the reverse.** Every shipment state change
   calls `SynchronizeFulfillmentAuthorityAsync` → `IOrderFulfillmentService.SynchronizeFromShipmentAsync`,
   which replays the intermediate fulfillment steps through the SAME guarded funnel
   (`ShipmentService.cs:1102-1143`, `OrderFulfillmentService.cs:196-278`). The order is
   never told its status by a carrier; the carrier only tells the `Shipment`, which asks
   fulfillment politely.
4. **External carriers are quarantined to the Shipment.** Webhooks are `[AllowAnonymous]` +
   `[AllowAnonymousTenant]`; tenant is resolved from the payload (not a header); the
   webhook path updates `Shipment` ONLY and is rejected if it targets a manual order
   (`ShippingWebhooksController.cs:33-34`, `CarrierShipmentService.cs:1617-1641`).
5. **Money gates movement.** Non-COD, positive-total orders cannot leave the warehouse
   until `PaymentStatus` is a paid state AND the collection accounting journal is `Posted`
   (`OrderOperationalFinancialAuthority.cs:22-52`). COD is the explicit exception: it may
   ship before payment, and payment is recognized at delivery.
6. **Carrier-agnostic core.** `Shipment.CarrierMetadata` JSON is the ONLY carrier-specific
   store; core never parses it; the adapter is the only reader/writer
   (`Shipment.cs:343-356`). `CarrierCode` selects the adapter via
   `ShippingCarrierRegistry` (`CarrierShipmentService.cs:226,354`).

---

## 3. Runtime Purpose, Scope, Owns / Does NOT Own

### Runtime Purpose
Own the authoritative physical lifecycle of a committed tenant order (from Confirmed
through Delivered/Completed/Returned) and the authoritative carrier-logistics record
(`Shipment`) for that order, and converge carrier/warehouse/manual signals into the
order's `FulfillmentStatus` — coordinating inventory, payment, accounting, affiliate,
notifications, and reporting without letting any derived state become business truth.

### Runtime Scope
- Tenant storefront + omnichannel order fulfillment: `Order` with
  `FulfillmentStatus ∈ {Created,Confirmed,Packed,OutForDelivery,Delivered,Completed,Returned,FailedDelivery,Cancelled}`
  (`FulfillmentStatus.cs:14-73`).
- The `Shipment` aggregate (one per order for now) (`Shipment.cs:21`,
  `ShipmentService.cs:100-107`).
- Carrier integration (adapters, registration worker, webhook pipeline) for
  `CarrierCodes` = manual/bosta/aramex/mylerz/dhl/fedex (`Shipment.cs:39-48`).
- NOT in scope: CommerceHub marketplace sub-order fulfillment (`CommerceHubSubOrder`) and
  POS `SalesInvoice` register path — separate aggregates (§ Divergence).

### Runtime OWNS (SQL truth it writes)
- `Order.FulfillmentStatus` (+ the derived `Order.Status` rollup when fulfillment advances)
  — via `IOrderFulfillmentService` only.
- `Shipment` rows and `Shipment.Status` — via `ShipmentService` / `CarrierShipmentService`.
- `ShippingWebhookLog` (idempotent webhook audit).
- `Order.TrackingNumber` / `Order.Carrier` / `Order.ShippedAt` / `Order.DeliveredAt`
  stamps, which are *copied from the Shipment as a denormalized projection*
  (`ShipmentService.cs:984-1012`, `StampOrderShipmentAuthoritySnapshot`) — these are
  derived snapshots, not independent authority.

### Runtime does NOT OWN (defers to owning services)
- **Pricing/shipping-cost of the method** → `ShippingCalculationService` / checkout; the
  Shipment only *copies* `order.ShippingCost` (`ShipmentService.cs:178`).
- **Payment state** → `IPaymentLifecycleService` (the ONLY `PaymentStatus` writer).
- **Inventory truth** → `ProductVariantStock` SQL (`ReservedQuantity`/`Quantity`);
  fulfillment coordinates deduction/release via events, never owns stock.
- **Accounting GL** → `FinancialPostingService`; fulfillment/receipt only emit outbox
  triggers.
- **Carrier credentials/config** → `CarrierConfiguration` (legacy runtime authority) and
  `CarrierAccount` (governance aggregate) — read, not owned by this runtime.
- **CommerceHub marketplace fulfillment** → `CommerceHubSubOrder` + marketplace consumers
  (`MarketplaceOrderDeliveredConsumer`) — separate surface.
- **Affiliate commission** → affiliate runtime; this runtime only emits delivery events
  that the affiliate runtime consumes to *approve* (not earn) commission.

---

## 4. Architecture Principles (each code-cited)

- **P1 — One authorized mutator for FulfillmentStatus (fail-closed at persistence).**
  `FulfillmentStatusSaveChangesInterceptor` throws unless
  `IsAuthorizedContext` (`FulfillmentStatusSaveChangesInterceptor.cs:74-98`); enabled only
  by `OrderFulfillmentService.TransitionAsync` (`OrderFulfillmentService.cs:676`).
- **P2 — Source-vs-mode ownership enforcement.** Manual source CANNOT update Carrier
  orders; Webhook source CANNOT update Manual orders
  (`IOrderFulfillmentService.cs:20-22`, enforced `OrderFulfillmentService.cs:554-578`).
- **P3 — Shipment converges INTO fulfillment via one funnel.** Reconciliation never writes
  `Order.FulfillmentStatus` directly (`ShipmentService.cs:1102-1143`,
  `CarrierShipmentService.cs:1718-1723`).
- **P4 — Canonical transition tables.** Fulfillment
  (`FulfillmentStatusExtensions.GetAllowedTransitions`, `FulfillmentStatus.cs:83-98`) and
  Shipment (`ShipmentLifecycleTransitionPolicy.CanTransition`,
  `ShipmentLifecycleTransitionPolicy.cs:12`) are single source-of-truth tables; all writers
  validate against them.
- **P5 — Outbound movement gated by money.** `OrderOperationalFinancialAuthority`
  blocks Confirmed→Completed progression until payment recognized + collection accounting
  posted (non-COD, positive total) (`OrderOperationalFinancialAuthority.cs:22-52`,
  `OrderFulfillmentService.cs:623-661`).
- **P6 — Serializable lock-hardened mutations.** Fulfillment and shipment mutations run in
  a `Serializable` tx and row-lock the `Orders`/`Shipments` row `FOR UPDATE`
  (`OrderFulfillmentService.cs:543-548,731`, `ShipmentService.cs:90,276,546-563`).
- **P7 — Carrier-agnostic core / carrier-specific JSON.** Core never parses
  `CarrierMetadata`; `CarrierCode` selects the adapter
  (`Shipment.cs:343-356`, `CarrierShipmentService.cs:226`).
- **P8 — Webhook trust isolation.** Unauthenticated webhooks resolve tenant from payload,
  validate signature/IP, are idempotent via `ShippingWebhookLog`, and update `Shipment`
  ONLY (`ShippingWebhooksController.cs:33`, `CarrierShipmentService.cs:1304-1763`).
- **P9 — Monotonic projection versioning + versioned events.** Shipment status events
  carry `EventVersion` derived from `Order.ProjectionMutationVersion + 1`
  (`ShipmentService.cs:902-927,774-790`); replay is collapsed against SQL authority
  (`OrderFulfillmentService.cs:602-621`).
- **P10 — DB-level tenant isolation.** `Shipment : ITenantOwned`; global query filter
  applies; webhook resolution uses `IgnoreQueryFilters` only to locate by tracking number
  then re-checks `TenantId` (`Shipment.cs:31`, `ShippingWebhookTenantResolver.cs:328-440`).

---

## 5. Core Aggregates

| Aggregate | Type | Owner (writer) | Authority evidence |
|---|---|---|---|
| `Order` (+ `FulfillmentStatus`) | SQL, tenant-scoped | `IOrderFulfillmentService` (status only) | `FulfillmentStatusSaveChangesInterceptor.cs:21`, `IOrderFulfillmentService.cs:33` |
| `Shipment` | SQL, tenant-scoped, `ITenantOwned` | `ShipmentService` (manual/admin) + `CarrierShipmentService` (webhook/registration) | `ShipmentService.cs:35`, `CarrierShipmentService.cs:170` |
| `CarrierConfiguration` | SQL (legacy) | Admin carrier config controller | read by checkout/quotes/registration/webhook (`CarrierShipmentService.cs:317,1481`) |
| `CarrierAccount` | SQL (governance) | Admin shipping-administration | warehouse-scoped visibility only (`ShippingMethodService` warehouse filter) |
| `ShippingMethod` | SQL (tenant config) | Admin shipping methods | immutable snapshot copied to Order/Shipment at checkout+creation (`ShipmentService.cs:138-176`) |
| `ShippingWebhookLog` | SQL | `CarrierShipmentService` | idempotency + audit (`CarrierShipmentService.cs:1791-1914`) |

**Identity.** `Shipment.ShipmentNumber` = `SHP-yyyyMMddHHmmss-{orderId8}`
(`ShipmentService.cs:606-611`). `Shipment.OrderId` is the only link to the order
(`Shipment.cs:36`). One shipment per order enforced by a uniqueness check
(`ShipmentService.cs:100-107`).

**Where shipments begin.** Only after the order is `Confirmed`/`Processing` AND
`CanAdvanceOutboundProgress` (payment+accounting authority) AND the order's
`ShippingMethodId` matches the requested method (`ShipmentService.cs:109-136,590-593`).
Carrier-managed shipments start at `PendingExternalRegistration`; manual at `Pending`
(`ShipmentService.cs:180-182`). Only carrier-managed shipments emit
`ShipmentExternalRegistrationRequestedEvent` (`ShipmentService.cs:225-232`).

---

## 6. State Machines (reverse-engineered)

The runtime contains **TWO independent, real state machines** (proven by code, not docs),
plus a **derived carrier-registration substate machine** on the Shipment.

### 6.1 FulfillmentStatus (physical axis on `Order`) — `FulfillmentStatus.cs:14-73`
```
Created(0) → Confirmed(1) → Packed(2) → OutForDelivery(3) → Delivered(4) → Completed(5)
                                          ↘ FailedDelivery(7) → OutForDelivery | Cancelled(8)
Confirmed/Packed/OutForDelivery → Cancelled(8)
Delivered → Returned(6)
```
Allowed transitions table: `FulfillmentStatus.cs:83-98`. Terminal = `Completed`,
`Returned`, `Cancelled` (`FulfillmentStatus.cs:93-95`). Mutated ONLY by
`IOrderFulfillmentService` (`FulfillmentStatus.cs:11`). `Delivered` is the trigger that
converges to payment (`FulfillmentStatus.cs:43-47`).

### 6.2 ShipmentStatus (carrier-logistics axis on `Shipment`) — `ShipmentStatus.cs:42-79`
```
PendingExternalRegistration(9) → ReadyForPickup(1) → PickedUp(2) → InTransit(3) → OutForDelivery(4) → Delivered(5) → Returned(8)
ReadyForPickup → Cancelled(7)         InTransit → Failed(6)|Returned(8)
PickedUp → InTransit                  OutForDelivery → Failed(6)|Returned(8)
Pending(0) → ReadyForPickup|Cancelled
```
Canonical table: `ShipmentLifecycleTransitionPolicy.cs:12-38`. Terminal =
`Failed`, `Cancelled`, `Returned` (`ShipmentStatus.cs:32-34`).
NOTE: `PendingExternalRegistration(9)` is the EXPECTED initial state for carrier shipments
post-commit; `Pending(0)` is for manual/incomplete only and must NEVER map a carrier
"created" status (`ShipmentStatus.cs:13-23`).

### 6.3 ExternalRegistrationState (carrier handshake substate on `Shipment`)
A string enum on the `Shipment`: `PendingExternalCall` / `Completed` / `Failed` /
`ReconciliationRequired` (`CarrierShipmentService.cs:172-175`), with
`ExternalRegistrationAttemptCount`, timestamps, and `ExternalRegistrationError`
(`Shipment.cs:376-412`). Drives the post-commit registration worker and its retry math.

### 6.4 Are the "picking / packing / ready-to-ship / assigned-carrier" states real?
The prompt's naive lifecycle (Order Created → Awaiting Fulfillment → Picking → Packing →
Ready To Ship → Assigned Carrier → …) is **PARTIALLY implemented**:
- "Picking" has **NO dedicated state** — inventory is reserved at checkout, not a picking
  stage; there is no `Picking` status on either machine.
- "Packing" = `Packed(2)` fulfillment / `ReadyForPickup(1)` shipment.
- "Ready To Ship" = `ReadyForPickup` shipment.
- "Assigned Carrier" = the `PendingExternalRegistration → ReadyForPickup` transition driven
  by the registration worker (`ShipmentService.cs:225-232`,
  `ShipmentExternalRegistrationRequestedConsumer.cs:14`).
- "Awaiting Fulfillment" = `Created` fulfillment status.

The canonical, implementation-backed lifecycle is the union of 6.1 + 6.2 (above), not the
prompt's marketing ordering.

---

## 7. Canonical Lifecycle — ONE COMPLETE END-TO-END (per-step owner/authority/sync/failure/recovery/projection/consistency)

| Step | Owner | Authority | Synchronization | Failure | Recovery | Projection | Consistency |
|---|---|---|---|---|---|---|---|
| Inventory Reserved | Inventory runtime (checkout) | `ProductVariantStock` SQL | `FOR UPDATE` in checkout tx | over-sell → checkout reject | release + `OrderCancelledEvent` | none | SQL-tx atomic w/ order |
| Warehouse Picking | (no state; reserved) | inventory | n/a | n/a | n/a | n/a | — |
| Packing | Fulfillment svc (admin) / Shipment | `Order.FulfillmentStatus=Packed` | `TransitionAsync` → `OrderStatus=Processing` | invalid transition → rejected | replay collapsed | `OrderProjectionConsumer` | Serializable+lock |
| Shipment Created | `ShipmentService.CreateShipmentAsync` | `Shipment` SQL | emits `ShipmentCreatedEvent` | payment/accounting gate → throw (`ShipmentService.cs:114-123`) | none (idempotent create guard) | storefront SQL query | Serial tx |
| Carrier Assigned + Registered | `CarrierShipmentService.ExecuteCarrierForExistingShipmentAsync` (post-commit worker) | `Shipment.ExternalRegistrationState` | `ShipmentExternalRegistrationRequestedEvent` outbox | carrier API fail → `Failed` state + retry (`CarrierShipmentService.cs:486-497`) | retryable throws to MassTransit (`ShipmentExternalRegistrationRequestedConsumer.cs:85-89`) | none | idempotent `CarrierRequestId` |
| Tracking Started | Carrier webhook / `SyncTrackingAsync` | `Shipment.TrackingNumber` | `ShipmentService.UpdateStatusAsync` | webhook trust fail → 401/403/404 | `ShippingWebhookLog` dedup | storefront SQL | signature+idempotency |
| Pickup | Carrier webhook | `Shipment.Status=PickedUp` | `SynchronizeFulfillmentAuthorityAsync`→`Packed`/`Packed` | status regression blocked (`CarrierShipmentService.cs:1664-1695`) | replay vs SQL | SQL | webhook only mutates Shipment |
| Transit | Carrier webhook | `Shipment.Status=InTransit` | converge → fulfillment `Packed` (no-op) | n/a | n/a | SQL | — |
| Delivery Attempt | Carrier webhook | `Shipment.Status=OutForDelivery` | converge → fulfillment `OutForDelivery` | n/a | n/a | SQL | — |
| Delivered | Carrier webhook / admin | `Shipment.Status=Delivered`; `Order.FulfillmentStatus=Delivered` | converge → fulfillment `Delivered` → `OrderStatus=Delivered`; emits `OrderDeliveredEvent` + `ShipmentDeliveredEvent` | COD not collected flag ignored for non-COD | `ShipmentService.ReconcileShipmentAuthorityAsync` (`ShipmentService.cs:412`) | SQL + `OrderProjectionConsumer` | Serial tx, lock |
| Confirmation (POD) | `MarkDeliveredAsync` (manual requires POD) | fulfillment `Delivered` | events | manual w/o POD → `MANUAL_DELIVERY_EVIDENCE_REQUIRED` (`OrderFulfillmentService.cs:153-163`) | n/a | SQL | — |
| COD Collection (if COD) | `FulfillmentDeliveredEventConsumer` + `ShipmentCodCollectedConsumer` | `Order.PaymentStatus` via `IPaymentLifecycleService` | `MarkOfflineCollectionDeliveredAsync` → ledger | non-COD or not-delivered collection → fail-closed (`PaymentLifecycleService.cs:1937-1948`) | MassTransit retry | accounting outbox | payment lifecycle owns |
| Reporting | reporting consumers (shipment events) | analytics read models | `ShipmentDeliveredEvent` etc. | n/a | replay | analytics | event-driven |
| Settlement (CommerceHub) | marketplace consumer | `CommerceHubSubOrder` settlement | `MarketplaceEvents.OrderDeliveredEvent` | separate surface | separate surface | separate | separate authority |
| Archive | soft-delete (`IsDeleted`) | SQL | query filter | n/a | n/a | n/a | — |

---

## 8. Fulfillment Runtime (who owns every stage)

| Stage | Owner | Evidence |
|---|---|---|
| Created → Confirmed | `IOrderFulfillmentService.ConfirmAsync` (Manual source only; carrier blocked) | `OrderFulfillmentService.cs:53`, `IOrderFulfillmentService.cs:47` |
| Confirmed → Packed | `PackAsync` (Manual) | `OrderFulfillmentService.cs:81` |
| Packed → OutForDelivery | `MarkOutForDeliveryAsync` (Manual) OR via shipment sync | `OrderFulfillmentService.cs:111,259` |
| OutForDelivery → Delivered | `MarkDeliveredAsync` (Manual, requires POD) OR via shipment sync (webhook) | `OrderFulfillmentService.cs:143,261` |
| Delivered → Completed | `MarkCompletedAsync` | `OrderFulfillmentService.cs:280` |
| Delivered → Returned | `MarkReturnedAsync` (refund routed to payment lifecycle) | `OrderFulfillmentService.cs:308-357` |
| OutForDelivery → FailedDelivery | `MarkDeliveryFailedAsync` | `OrderFulfillmentService.cs:359` |
| FailedDelivery → OutForDelivery | `RetryDeliveryAsync` | `OrderFulfillmentService.cs:397` |
| Cancelled | `CancelAsync` → **delegates to order cancellation service** (fulfillment cannot cancel: `ORDER_CANCELLATION_LIFECYCLE_REQUIRED`) | `OrderFulfillmentService.cs:425-445` |
| Shipment→Fulfillment convergence | `SynchronizeFromShipmentAsync` (replays intermediate steps through the same guarded funnel) | `OrderFulfillmentService.cs:196-278` |

**Key architectural fact:** fulfillment `CancelAsync` is intentionally a **hard fail** —
cancellation must go through the order cancellation lifecycle so payment cancellation and
inventory restoration commit together (`OrderFulfillmentService.cs:438-444`). This is the
inverse of the naive "fulfillment can cancel the order" model.

---

## 9. Shipment Runtime

- **Entity** (`Shipment.cs:26`): `ITenantOwned`, one per order, carrier-agnostic.
- **Aggregate boundary**: `Shipment` references `Order` but is a separate table; it owns
  its own `Status`, COD, tracking, package, label, `CarrierMetadata`, and
  `ExternalRegistrationState`.
- **Authority**: `ShipmentService` (admin/manual intent) + `CarrierShipmentService`
  (webhook + registration). Both validate `ShipmentLifecycleTransitionPolicy`.
- **Snapshot**: at creation the Shipment copies the order's shipping address, recipient,
  geographic place-ids, `ShippingCost`, `Currency`, `ShippingMethodName`
  (`ShipmentService.cs:170-215`) — an immutable logistics snapshot.
- **Identity**: `ShipmentNumber` (`ShipmentService.cs:606-611`).
- **Events** (`ShipmentEvents.cs`): `ShipmentCreatedEvent`,
  `ShipmentReadyForPickupEvent`, `ShipmentPickedUpEvent`, `ShipmentInTransitEvent`,
  `ShipmentOutForDeliveryEvent`, `ShipmentDeliveredEvent`, `ShipmentFailedEvent`,
  `ShipmentCancelledEvent`, `ShipmentReturnedEvent`, `ShipmentStatusChangedEvent`,
  `ShipmentExternalRegistrationRequestedEvent`, `ShipmentCodCollectedEvent`. There is
  **NO** "ShippingConfigured"/"ShippingSelected" event — selection is an Order snapshot.
- **Recovery**: `ShipmentService.ReconcileShipmentAuthorityAsync` (`:412`) and
  `RequiresShipmentAuthorityRepair` (`:1014-1066`) detect drift between `Shipment` and
  `Order` (tracking number, carrier, shipped/delivered timestamps, fulfillment status
  mismatch) and re-sync via the fulfillment funnel.
- **Replay**: status events carry `EventVersion` from `Order.ProjectionMutationVersion+1`;
  fulfillment replay collapses against SQL (`OrderFulfillmentService.cs:602-621`).
- **Versioning**: `Shipment.Status` changes advance `Order.ProjectionMutationVersion`
  (`ShipmentService.cs:925`).
- **Synchronization**: `SynchronizeFulfillmentAuthorityAsync` maps
  `ShipmentStatus → FulfillmentStatus` (`ShipmentService.cs:1084-1100`) and only syncs the
  four shipment-authoritative targets (`Confirmed/Packed/OutForDelivery/Delivered`) —
  `Cancelled/Returned/FailedDelivery` are NOT synced from shipment (they require the
  order-level lifecycle methods) (`OrderFulfillmentService.cs:834-838`).

---

## 10. Delivery Runtime

- **Delivery Status** = `ShipmentStatus.Delivered` / `FulfillmentStatus.Delivered`
  (two views of the same fact, owned by different services).
- **Delivery Confirmation / Proof of Delivery**: `MarkDeliveredAsync` requires
  `deliveryReference` + `proofOfDeliveryUrl` for Manual source (`OrderFulfillmentService.cs:153-163`);
  carrier webhooks supply `DeliveredAt` (`CarrierShipmentService.cs:1700`).
- **Delivery Failure**: `ShipmentStatus.Failed` / `FulfillmentStatus.FailedDelivery`
  (`ShipmentStatus.cs:71`, `FulfillmentStatus.cs:66`).
- **Retry**: `RetryDeliveryAsync` (`FailedDelivery → OutForDelivery`);
  `MarkDeliveryFailedAsync` records `attemptNumber`/`canRetry`
  (`OrderFulfillmentService.cs:359-423`).
- **Lost / Damaged / Rejected / Returned Shipment**: `ShipmentStatus.Returned` is terminal
  (`ShipmentStatus.cs:77`); a return triggers `OrderReturnedEvent` and, if
  `shouldRefund`, routes to payment lifecycle (`OrderReturnedEvent`,
  `OrderFulfillmentService.cs:308-357`). Lost/Damaged are NOT separate states — they are
  expressed via `FailureReason`/`ReturnReason` free-text (`Shipment.cs:432-442`).
- **Critical divergence**: a `FailedDelivery` shipment does NOT cancel the order; it loops
  back to `OutForDelivery` or `Cancelled` via the order lifecycle — fulfillment and order
  stay independently authoritative.

---

## 11. Carrier Integration Runtime

- **Adapter abstraction**: `IShippingCarrierAdapter` resolved by `ShippingCarrierRegistry`
  (`CarrierShipmentService.cs:226,354`); adapters live in
  `PointOfSale.Infrastructure/Shipping/Adapters/`. Core only stores `CarrierMetadata` JSON
  (`Shipment.cs:343-356`).
- **Tracking Number / Status**: carrier status parsed by `adapter.ParseWebhook` → mapped
  to canonical `ShipmentStatus` (`CarrierShipmentService.cs:1385,1699`);
  `Shipment.CarrierTrackingResponse` stores the raw response for audit (`Shipment.cs:418`).
- **Webhook**: `ShippingWebhooksController` (`[AllowAnonymous]+[AllowAnonymousTenant]`),
  per-carrier signature header (generic `X-Webhook-Signature`, Bosta `Authorization`,
  Aramex `X-Aramex-Signature`, Mylerz `X-Mylerz-Signature`, DHL `X-DHL-Signature`),
  `WebhookContentTypeValidationFilter` (415 unless `application/json`), request-size limit
  (`WebhookRequestLimits.ShippingProviderMaxBytes`), rate limiter
  (`WebhookRateLimiterPolicy`), health endpoint
  (`ShippingWebhooksController.cs:31-141`).
- **Webhook rate limiter**: sliding window, **60 permits/minute**, partition key
  `webhook:{kind}:{provider}:tenant:{tenantId}:ip:{ip}`
  (`WebhookRateLimiterPolicy.cs:13,22-27,31-36`). Applies to INBOUND webhooks only; there
  is no outbound call rate-limiter.
- **Webhook trust pipeline**: `ShippingWebhookRequestPipeline` resolves tenant from
  payload (shipmentId OR trackingNumber OR claimed tenantId), then
  `CarrierShipmentService.ProcessWebhookAsync` validates signature/IP via
  `ResolveWebhookTrustPolicy`/`ValidateWebhookTrust` (HMAC-SHA256 with
  `CryptographicOperations.FixedTimeEquals`, SharedSecret, or IpAllowlist), requires
  `CarrierConfiguration.SupportsWebhooks`, and writes a `ShippingWebhookLog` with
  event-id + payload-hash idempotency (`CarrierShipmentService.cs:1304-1914`,
  `:1976-2114`). Manual carriers are rejected (`CarrierShipmentService.cs:1339`,
  `ShippingWebhookTenantResolver.cs:253`).
- **Polling**: `TrackingSyncJob` + `CarrierShipmentService.SyncTrackingAsync` (carrier
  pull, separate from push webhooks).
- **Synchronization**: webhook → `Shipment.Status` → `SynchronizeFulfillmentAuthorityAsync`
  → fulfillment funnel (`CarrierShipmentService.cs:1718-1723`).
- **Retry / Idempotency**: NO Polly/`RetryPolicy` in the carrier path — retry is
  hand-rolled via `ExternalRegistrationAttemptCount` + single-winner conditional-update
  claim + back-off-on-contention (`CarrierShipmentService.cs:680-690,737-751,820`).
  `ShippingWebhookLog` unique (eventId, payloadHash) + DB unique-constraint conflict
  handler (`CarrierShipmentService.cs:1550-1615`); registration idempotency via persisted
  `CarrierRequestId` BEFORE the external call (`CarrierShipmentService.cs:428-436`), and
  the consumer retries on `IsRetryableFailure` codes
  (`ShipmentExternalRegistrationRequestedConsumer.cs:92-108`).
- **Failure handling**: unparsable/missing tracking → 400; tenant ambiguous → 409;
  trust fail → 401/403; carrier not configured for webhooks → 403
  (`CarrierShipmentService.cs:1370-1529`). Status regression / terminal-status writes are
  blocked (`IsValidStatusProgression`, `:4407`, `:1664-1695`).
- **Provider gating**: a carrier is unavailable when `CarrierConfiguration` is missing or
  `IsEnabled=false` (boolean flag, not a numeric "zero" sentinel). Every path gates on it:
  checkout (`CheckoutService.cs:4717-4727`), quotes (`ShippingQuoteService.cs:141`),
  registration (`CarrierShipmentService.cs:320,656`), webhook trust
  (`:1481-1490`), CommerceHub store options (`CommerceHubShippingService.cs:221-228`).

---

## 12. Tracking Runtime

- **Tracking Authority**: `Shipment.TrackingNumber`/`TrackingUrl` (SQL) is authoritative;
  carrier webhook only writes it for carrier-managed shipments and is blocked from
  overwriting for manual (`ShipmentService.cs:961-980`,
  `EnsureCarrierManagedTrackingMutationAllowed`).
- **Tracking Source**: carrier webhook or `SyncTrackingAsync` polling; copied to
  `Order.TrackingNumber` as a denormalized projection
  (`ShipmentService.cs:991-994`).
- **Tracking Projection / History**: `ShipmentStatusChangedEvent` + per-state events
  (`ShipmentEvents.cs`) feed analytics; there is NO dedicated Mongo tracking document in
  this runtime — storefront reads `StorefrontShipmentDto` from SQL
  (`ShipmentService.cs:504-535`).
- **Tracking Timeline / Events**: `ReadyForPickup → PickedUp → InTransit → OutForDelivery → Delivered`,
  each emitting a timestamped event with monotonic `EventVersion`
  (`ShipmentService.cs:760-900`).
- **Replay / Synchronization**: events replay against `Order.ProjectionMutationVersion`;
  `OrderFulfillmentLineageAuthority.ToProjectionShippingStatus` maps
  `ShipmentStatus →` projection status (`OrderFulfillmentLineageAuthority.cs:97-113`).

---

## 13. Warehouse Integration Runtime

- **Inventory stops / Fulfillment begins**: inventory is **reserved at checkout** (not a
  picking stage). The Shipment is created only after `Confirmed`/`Processing` + financial
  authority (`ShipmentService.cs:109-123`). There is **NO warehouse "picking" state** in
  this runtime — picking is the inventory reservation that already happened at checkout.
- **Warehouse as shipment origin**: `Shipment.OriginWarehouseId` + full origin geographic
  snapshot are admitted/immutable at shipment registration (`Shipment.cs:234-255`,
  `CarrierShipmentService.cs:3843-3886`). Post-success recovery must NOT reconstruct origin
  from live Warehouse/CarrierConfiguration (`CarrierShipmentService.cs:3875-3881`).
- **Reservation → Deduction → Restoration — IMPORTANT CORRECTION**: the POS
  `OrderFulfillmentService` does **NOT** call `InventoryConsistencyService`. Inventory
  commit-on-delivery (`CommitDeliveryAsync`) and release-on-cancel
  (`ReleaseOrderItemReservationsAsync`) are executed by the **CommerceHub sub-order
  lifecycle** (`CommerceHubOrderManagementService.UpdateSubOrderStatusAsync`,
  `CommerceHubOrderManagementService.cs:135-167`), with a self-healing
  `CommerceHubInventoryReconciliationJob` (`CommerceHubInventoryReconciliationJob.cs:231-388`).
  The POS `OrderFulfillmentService` deliberately DELEGATES (fail-closed) and refuses to
  release inventory itself — `CancelAsync` returns `ORDER_CANCELLATION_LIFECYCLE_REQUIRED`
  forcing the caller to the order-cancellation service so payment cancellation and
  inventory restoration commit together (`OrderFulfillmentService.cs:425-445`). Do not
  attribute inventory commit/release to the POS fulfillment service.

---

## 14. Order Integration Runtime

- **When Order loses authority over fulfillment**: never — `Order.FulfillmentStatus` is
  always owned by `IOrderFulfillmentService`. The Order gains fulfillment authority at
  creation (`FulfillmentStatus=Created`).
- **Can Fulfillment modify Order?** Yes, but ONLY `FulfillmentStatus` (+ derived
  `Order.Status`, `TrackingNumber`, `Carrier`, `ShippedAt`, `DeliveredAt`), and only
  through the guarded funnel (`OrderFulfillmentService.cs:680-699`,
  `ShipmentService.cs:984-1012`).
- **Can Order modify Shipment?** No. The order snapshot (`ShippingMethodId`,
  `FulfillmentMode`, `FulfillmentCarrierCode`) is immutable at create; shipment creation
  must match the order's `ShippingMethodId` and cannot substitute a method
  (`ShipmentService.cs:128-136`, `CarrierShipmentService.cs:255-296`).
- **Can Shipment exist without Order?** No — `Shipment.OrderId` is required and
  `LoadOrderForShipmentMutationAsync` fails the operation if the order is missing
  (`Shipment.cs:36`, `ShipmentService.cs:94-98`).
- **Failed shipment vs Order**: a failed shipment does NOT change `Order.PaymentStatus` or
  cancel the order; it loops `FailedDelivery → OutForDelivery` or `Cancelled` via the order
  lifecycle. `Order.Status` is DERIVED from fulfillment via `ResolveOrderStatus`
  (`OrderFulfillmentService.cs:759-787`), never the reverse.

---

## 15. Payment Integration Runtime

- **COD Flow**: `IsCashOnDelivery` Shipment copies `order.Total` as `CodAmount`
  (`CarrierShipmentService.cs:352-353`). COD is the explicit exception to the outbound
  money gate (`OrderOperationalFinancialAuthority.cs:24,34`). Recognition happens at
  delivery: `FulfillmentDeliveredEvent` → `FulfillmentDeliveredEventConsumer` →
  `MarkOfflineCollectionDeliveredAsync` (DEBIT COD Clearing, CREDIT Revenue)
  (`OrderDeliveredEventConsumer.cs:55-94`).
- **Prepaid Flow**: outbound progression blocked until `PaymentStatus` paid AND
  collection accounting `Posted` (`OrderOperationalFinancialAuthority.cs:22-52`).
- **Payment Confirmation / Shipment Blocking / Release**: the gate
  `CanAdvanceOutboundProgress` blocks shipment/fulfillment creation and progression until
  payment authority exists (`ShipmentService.cs:590-593,929-959`,
  `OrderFulfillmentService.cs:623-661`).
- **Payment Failure / Recovery**: failure does NOT touch fulfillment; the payment
  lifecycle owns it. COD payment recognition enters payment authority through
  `FulfillmentDeliveredEventConsumer` and `ShipmentCodCollectedConsumer`; both delegate
  to `IPaymentLifecycleService.MarkOfflineCollectionDeliveredAsync`, which rejects
  non-COD and not-delivered orders (`PaymentLifecycleService.cs:1937-1948`).
- **Refund Integration**: `MarkReturnedAsync` refuses to create refund authority and
  routes to `IPaymentLifecycleService` (`OrderFulfillmentService.cs:319-328`).
- **Chargeback Integration**: not in this runtime; owned by payment lifecycle.

---

## 16. Inventory Integration Runtime

- **Reservation Release**: at checkout (reservation). Release on cancel via order
  cancellation lifecycle (`OrderFulfillmentService.cs:438-444`).
- **Inventory Deduction (commit)**: executed by the CommerceHub sub-order lifecycle at
  delivery — `CommerceHubOrderManagementService.UpdateSubOrderStatusAsync` calls
  `InventoryConsistencyService.CommitDeliveryAsync` when status becomes Delivered, with
  active-reservation-lineage guards (`CommerceHubOrderManagementService.cs:135-153`,
  `InventoryConsistencyService.cs:1002,1357-1383`). The POS `OrderFulfillmentService` does
  NOT perform this — it only mutates `Order.FulfillmentStatus` + publishes events.
- **Inventory Restoration (release)**: on cancel/return, `CommerceHubOrderManagementService`
  calls `ReleaseOrderItemReservationsAsync` (`CommerceHubOrderManagementService.cs:154-167`);
  the POS fulfillment `CancelAsync` refuses and routes to the order-cancellation service.
  A `CommerceHubInventoryReconciliationJob` self-heals missed delivery commits
  (`CommerceHubInventoryReconciliationJob.cs:231-388`).
- **Damaged / Lost / Return Inventory**: expressed via `FailureReason`/`ReturnReason` and
  the return lifecycle; stock restoration is inventory-owned (CommerceHub path).
- **Warehouse Transfer**: not in this runtime.

---

## 17. CommerceHub Integration Runtime

- **Marketplace Shipment / Vendor Shipment / Split Shipment**: CommerceHub
  `CommerceHubSubOrder` carries only `ShippingMethodId`/`ShippingMethodName` as a
  reference (`CommerceHubSubOrder.cs:16-17`). It has **NO `FulfillmentStatus`, NO
  `Shipment` entity, NO tracking column** in this runtime. CommerceHub delivery is a
  SEPARATE surface: `CommerceHubShippingService.CalculateAsync` only computes shipping
  cost for the marketplace (`CommerceHubShippingService.cs:63`); there is no
  `CommerceHubShipment` creation path.
- **Vendor Tracking / Settlement Trigger**: `MarketplaceEvents.OrderDeliveredEvent` →
  `MarketplaceOrderDeliveredConsumer` drives marketplace settlement/vendor earnings
  (`MarketplaceBackboneConsumers.cs:53`) — a parallel pipeline to the storefront
  `OrderDeliveredEvent`/`FulfillmentDeliveredEvent` consumers. The two are NOT shared.
- **Multi Vendor / Split Shipment**: not implemented as separate shipments in this
  runtime; `CommerceHubSubOrder` is the per-vendor split, but it does not use the
  `Shipment`/`IOrderFulfillmentService` machinery.

---

## 18. Affiliate Integration Runtime

- **Does delivery trigger commission?** Commission is **EARNED at payment** (prior
  runtime). Delivery **APPROVES** earned commission for payout:
  `AffiliateOrderDeliveredConsumer` consumes BOTH `OrderDeliveredEvent` AND
  `FulfillmentDeliveredEvent` → `ApproveCommissionForDeliveredOrderAsync`
  (`AffiliateSettlementConsumers.cs:21-113`).
- **Does shipment trigger commission?** No — the triggers are delivery events
  (`OrderDeliveredEvent`/`FulfillmentDeliveredEvent`), not `ShipmentCreatedEvent`.
- **Does refund reverse commission?** Yes — `AffiliateRefundConsumers` reverses
  commission on refund (`AffiliateRefundConsumers.cs`), independent of this runtime but
  consuming payment/refund events.

---

## 19. Reporting Integration Runtime

- **Shipping / Fulfillment / Delivery / Warehouse / Carrier KPIs**: fed by shipment +
  fulfillment events (`ShipmentDeliveredEvent`, `ShipmentFailedEvent`,
  `FulfillmentDeliveredEvent`, etc.) into analytics read models. Average delivery time,
  failure rate, return rate are derivable from `Shipment.DeliveredAtUtc`/
  `FailedAtUtc`/`ReturnedAtUtc` timestamps (`Shipment.cs:299-339`).
- **Carrier KPIs**: from `ShippingWebhookLog` + `Shipment.CarrierCode` aggregates.
- Implementation note: no dedicated "reporting consumer" file was found bound to shipment
  events in this runtime; reporting consumes the same event stream as affiliate/notification
  consumers (see § Technical Debt for verification status).

---

## 20. Notification Runtime Integration

Consumers (all idempotent, base `ShipmentNotificationConsumerBase`):
- `ShipmentCreatedNotificationConsumer` → `NotificationTypes.ShipmentCreated`
- `ShipmentShippedNotificationConsumer` → `NotificationTypes.ShipmentShipped`
- `ShipmentDeliveredNotificationConsumer` → `NotificationTypes.ShipmentDelivered`
- `ShipmentCancelledNotificationConsumer` → `NotificationTypes.ShipmentCancelled`

Channel = `NotificationSender.Channel` (config-driven single outbound channel; the
WhatsApp outbound-delivery runtime is the underlying sender per the WhatsApp runtime
doc). **Gap**: there is **NO** notification consumer for `ShipmentFailedEvent` or
`ShipmentReturnedEvent` — failed/returned deliveries do not emit customer notifications in
this runtime (see § Technical Debt).

---

## 21. State Machines — which really exist

| Machine | Real? | Evidence |
|---|---|---|
| Shipment Status | YES | `ShipmentStatus.cs:42`, `ShipmentLifecycleTransitionPolicy.cs:12` |
| Fulfillment Status | YES | `FulfillmentStatus.cs:14`, `FulfillmentStatus.cs:83` |
| Delivery Status | YES (view of Shipment/Fulfillment Delivered) | `ShipmentStatus.Delivered`, `FulfillmentStatus.Delivered` |
| Carrier Status | PARTIAL — `ExternalRegistrationState` substate on Shipment | `CarrierShipmentService.cs:172-175` |
| Warehouse Status | NO dedicated machine — warehouse is origin snapshot only | `Shipment.cs:234-255` |
| Tracking Status | NO separate machine — tracking is a Shipment field + events | `Shipment.cs:118-130` |
| Pickup / Dispatch | NO dedicated machine — expressed as Shipment `ReadyForPickup`/`PickedUp` | `ShipmentStatus.cs` |

---

## 22. Runtime Authority Matrix

| Concept | Owner (writer) | Reader | Event consumer | Projection writer | Runtime truth |
|---|---|---|---|---|---|
| `Order.FulfillmentStatus` | `IOrderFulfillmentService` | order runtime | `FulfillmentDeliveredEventConsumer` (payment), `OrderStatusSyncConsumer` | `OrderProjectionConsumer` (legacy events) | SQL (Order) |
| `Order.Status` (rollup) | `IOrderFulfillmentService` (converged) | storefront/admin | `OrderStatusSyncConsumer` | `OrderProjectionConsumer` | SQL (derived) |
| `Shipment` / `Shipment.Status` | `ShipmentService` + `CarrierShipmentService` | storefront SQL query | `Shipment*NotificationConsumer`, affiliate, analytics | (none Mongo in this runtime) | SQL (Shipment) |
| `Order.TrackingNumber`/`Carrier` | `ShipmentService` (copy from Shipment) | storefront | — | — | SQL (denormalized projection) |
| `CarrierConfiguration` | Admin carrier config controller | checkout/quotes/registration/webhook | — | — | SQL (legacy runtime) |
| `CarrierAccount` | Admin shipping-administration | warehouse-scoped method visibility ONLY | — | — | SQL (governance; `HealthState` effectively dead at runtime) |
| `ShippingMethod` | Admin shipping methods | checkout/storefront | `ShippingMethodProjectionConsumer` | Mongo `ShippingMethodDocument` | SQL (config) |
| COD collection | `IPaymentLifecycleService` (via fulfillment delivery or carrier COD evidence event) | payment runtime | `FulfillmentDeliveredEventConsumer`, `ShipmentCodCollectedConsumer` | accounting outbox | SQL (Payment) |
| Commission approval | affiliate runtime | affiliate | `AffiliateOrderDeliveredConsumer` | affiliate Mongo | SQL (affiliate) |

---

## 23. Runtime Authority Divergence

| Divergence | Reality (implementation) | Evidence |
|---|---|---|
| **Shipment ≠ Delivery** | `Shipment.Delivered` (carrier) and `FulfillmentStatus.Delivered` (order) are separate; shipment sync converges but can lag/diverge | `ShipmentService.cs:1084-1100`, `OrderFulfillmentService.cs:196-278` |
| **Order ≠ Shipment** | Order is immutable-at-create snapshot; Shipment is mutable logistics; one Shipment per Order | `Shipment.cs:13`, `ShipmentService.cs:100-107` |
| **Tracking ≠ Shipment** | Tracking is a Shipment field + events, not a separate authority | `Shipment.cs:118-130` |
| **Carrier ≠ Tracking** | Carrier is a code/adapter; tracking is carrier-supplied data in `CarrierMetadata` | `Shipment.cs:39-48,343-356` |
| **Warehouse ≠ Inventory** | Warehouse is shipment origin snapshot; inventory is `ProductVariantStock` SQL | `Shipment.cs:234-255` |
| **Delivered ≠ Completed** | `Delivered` is physical; `Completed` is the terminal lifecycle state requiring no further action | `FulfillmentStatus.cs:48-54,93` |
| **Returned ≠ Refunded** | `Returned` (fulfillment) does not create refund; refund routed to payment lifecycle | `OrderFulfillmentService.cs:319-328` |
| **COD Delivered ≠ Payment Completed** | COD delivered triggers `MarkOfflineCollectionDeliveredAsync` → `Collected`; NOT `Settled` until bank receipt | `OrderDeliveredEventConsumer.cs:55-94`, payment lifecycle |
| **CarrierConfiguration ≠ CarrierAccount (config divergence)** | `CarrierConfiguration` is the LEGACY runtime authority read by checkout/quotes/registration/webhook; `CarrierAccount` is governance + warehouse-scoped visibility only. `CarrierAccount.HealthState` (default `Blocked`) is **effectively dead at runtime** — never read outside admin. They can disagree (enabled-vs-blocked) causing inconsistent availability | `CarrierShipmentService.cs:317,1481`; `ShippingMethodService.ApplyWarehouseAssignmentFilterAsync` (only runtime `CarrierAccount` touchpoint) vs `CommerceHubShippingService.cs:221-228` |
| **Storefront projection ≠ SQL** | Storefront shipment reads are SQL (`StorefrontShipmentDto`); checkout bypasses projections and reads SQL authority | `ShipmentService.cs:504-535` |

---

## 24. CQRS / Outbox / Event / Projection Model

- **SQL authority**: `Orders`, `Shipments`, `ShippingWebhookLogs`, `CarrierConfiguration`
  are PostgreSQL; tenant global query filter applies.
- **Mongo projections**: `ShippingMethodDocument` (storefront method read model) is a Mongo
  projection written by `ShippingMethodProjectionConsumer` with
  `ProjectionEventFence` + `UpsertIfFreshAsync` monotonic fences, re-fetched from SQL,
  fails closed (`ShippingMethodProjectionConsumer.cs`). **No Mongo `Shipment` / fulfillment
  document is consumed by this runtime.** The storefront *order* read model is a Mongo
  `OrderDocument` fed by the **legacy** `OrderDeliveredEvent` / `OrderStatusChangedEvent` /
  `ShipmentStatusChangedEvent` path (`OrderProjectionConsumer.cs:38-65,404-433`); the NEW
  `Fulfillment*` events feed only Affiliate settlement + the payment lifecycle. Shipment
  visibility is surfaced through the order doc's shipping facets via
  `OrderFulfillmentLineageAuthority.ToProjectionShippingStatus`
  (`OrderFulfillmentLineageAuthority.cs:97-113`). There is no dedicated fulfillment
  projection-repair consumer — replay/repair relies on outbox slow-replay +
  per-consumer version fences.
- **Read paths**: storefront **shipment** reads are SQL (`StorefrontShipmentDto`,
  `ShipmentService.cs:504-535`); storefront **order** reads are Mongo
  (`StorefrontOrdersReadController.cs:27-29`, `OrderReadRepository.cs:13-15`). Checkout
  bypasses projections and reads SQL authority.
- **Redis**: `pos:tenant:{tenantId}:shipping-methods` cache; shipment status idempotency
  keys (24h TTL) (`ShipmentService.cs:49,666-699`);
  `ShippingWebhookLog` idempotency in Redis (`CarrierShipmentService.cs:1791-1914`);
  notification idempotency (`ShipmentNotificationConsumerBase.cs:107-139`).
- **Outbox**: integration events published via `IEventPublisher` inside the same SQL
  transaction (MassTransit EF outbox, `Program.cs:1940-1948`) — shipment mutation +
  `ShipmentStatusChangedEvent` + `OrderDeliveredEvent` commit atomically
  (`ShipmentService.cs:376-385,767-900`). `EnsureTransactionalPublishBoundary` fails
  closed if an authoritative event would escape the tx boundary.
- **Replay**: `OutboxPublisherJob` orders critical events by
  `(TenantId, AggregateId, EventVersion)`; stale/earlier-aggregate messages deferred;
  exhausted critical "poison" messages flagged `RequiresOperatorReconciliation`
  (`OutboxPublisherJob.cs:80-236`). Fulfillment replay collapses against SQL
  (`OrderFulfillmentService.cs:602-621`).
- **Repair**: `ShipmentService.ReconcileShipmentAuthorityAsync` +
  `RequiresShipmentAuthorityRepair` re-syncs Shipment↔Order drift
  (`ShipmentService.cs:412-484,1014-1066`).
- **Projection Recovery**: `OrderFulfillmentLineageAuthority` loads SQL-authoritative
  Shipment/Order snapshot for replay decisions
  (`OrderFulfillmentLineageAuthority.cs:14-95`).
- **Tracking Projection / Search Projection**: storefront reads SQL; no search projection
  in this runtime.

---

## 25. Snapshot Runtime (immutable logistics + financial snapshots)

| Snapshot | Field(s) | Immutable? | Evidence |
|---|---|---|---|
| Shipping address | `ShippingAddress*`, destination place-ids | frozen at Shipment create | `ShipmentService.cs:185-209` |
| Carrier | `CarrierCode` | frozen at create | `Shipment.cs:48` |
| Tracking Number | `TrackingNumber` | set on registration/sync | `Shipment.cs:118` |
| Rates | `ShippingCost`/`Currency` | copied from Order | `ShipmentService.cs:178-179` |
| Delivery Cost | `ShippingCost` | copied from Order | `ShipmentService.cs:178` |
| Warehouse (origin) | `OriginWarehouseId` + origin snapshot | admitted/immutable | `Shipment.cs:234-255` |
| Shipment Items | derived from Order items | not stored on Shipment | n/a |
| Package Weight | `WeightKg` (default 1.0 in carrier call) | set at registration | `CarrierShipmentService.cs:478` |
| Package Dimensions | NOT modeled | gap | `Shipment.cs:270-286` (weight only) |
| COD Amount | `CodAmount` | copied from Order.Total | `CarrierShipmentService.cs:353` |
| Receiver Information | `RecipientName/Phone/Email` | copied from Order/guest | `ShipmentService.cs:184,210` |

NOTE: package **dimensions** are NOT captured (only weight, defaulted to 1.0kg) — a
technical-debt finding.

---

## 26. Cross-Runtime Authority Graph

| Peer | Reads | Writes | Events | Runtime authority | Projection | Ownership sync |
|---|---|---|---|---|---|---|
| Identity | tenant/order customer | — | — | `ITenantContext` | — | shipment requires `TenantId` |
| Inventory | reserved stock | (coordinates deduction) | reservation release on cancel | `ProductVariantStock` | — | fulfillment does not own stock |
| POS | — | — | — | separate `SalesInvoice` | — | not this runtime |
| Payment | `PaymentStatus` | `FulfillmentStatus` triggers COD collect | `FulfillmentDeliveredEvent` | `IPaymentLifecycleService` | accounting outbox | delivery→payment gated |
| Order | `Order`/`FulfillmentStatus` | `FulfillmentStatus`+derived | `OrderDeliveredEvent` | order lifecycle | `OrderProjectionConsumer` | convergence funnel |
| CommerceHub | — | — (separate surface) | `MarketplaceEvents.OrderDeliveredEvent` | `CommerceHubSubOrder` | marketplace Mongo | parallel pipeline |
| Reporting | shipment events | — | consumes events | analytics | read models | event-driven |
| Affiliate | — | commission approval | `OrderDeliveredEvent`/`FulfillmentDeliveredEvent` | affiliate runtime | affiliate Mongo | delivery→approve |
| Treasury | — | — | — | separate | — | not this runtime |
| Search | — | — | — | not indexed | — | no search projection |
| CMS | — | — | — | separate | — | not this runtime |
| Storefront | SQL shipment query | create shipment (admin intent) | shipment events | `ShipmentService` | SQL read | reads SQL authority |

---

## 27. Business Invariants (code-cited)

1. **Shipment cannot exist without a valid, non-deleted Order** — `ShipmentService.cs:94-98`.
2. **Carrier code must never be null/empty; "manual" is the default** —
   `Shipment.cs:46-48`.
3. **Shipment creation requires `OrderStatus ∈ {Confirmed, Processing}`** —
   `ShipmentService.cs:109-112`.
4. **Outbound progression requires payment recognized + collection accounting posted
   (non-COD, positive total)** — `OrderOperationalFinancialAuthority.cs:22-52`,
   `OrderFulfillmentService.cs:623-661`.
5. **`FulfillmentStatus` can ONLY change via `IOrderFulfillmentService`** —
   `FulfillmentStatusSaveChangesInterceptor.cs:87-98`.
6. **Manual source cannot update Carrier orders; Webhook source cannot update Manual
   orders** — `OrderFulfillmentService.cs:554-578`.
7. **Shipment never writes `Order.FulfillmentStatus` directly** —
   `ShipmentService.cs:1102-1143`.
8. **Delivered or carrier COD evidence triggers payment-authority COD collection
   (offline) or no-op/fail-closed (online/not-delivered)** —
   `OrderDeliveredEventConsumer.cs:55-94`,
   `ShipmentCodCollectedConsumer.cs:47-58`,
   `PaymentLifecycleService.cs:1937-1948`.
9. **Inventory must be released via the order cancellation lifecycle, not fulfillment
   cancel** — `OrderFulfillmentService.cs:438-444`.
10. **COD must be recognized only after delivery evidence, never at shipment creation** —
    `OrderOperationalFinancialAuthority.cs:24` (COD exempt from pre-ship payment gate),
    `PaymentLifecycleService.cs:1937-1948` (delivery-state admission).
11. **Tracking must match `CarrierCode`+`TenantId`** — webhook resolution
    `ShippingWebhookTenantResolver.cs:328-440`.
12. **Returns must route refund to payment lifecycle, not fulfillment** —
    `OrderFulfillmentService.cs:319-328`.

---

## 28. Architectural Decisions (WHY implementation chose this)

- **Two aggregates (Shipment + FulfillmentStatus) instead of one "Shipping" table**: the
  order's physical state must live on the `Order` (so `OrderStatus` can roll up from it and
  payment can gate it), while carrier logistics are volatile and carrier-specific and must
  be isolatable. Hence `Shipment` is a separate, carrier-agnostic entity that *converges
  into* the order (`Shipment.cs:13`, `OrderFulfillmentService.cs:196-278`).
- **Fail-closed `SaveChangesInterceptor` for FulfillmentStatus**: prevents any controller,
  consumer, or shipment path from shortcutting the state machine
  (`FulfillmentStatusSaveChangesInterceptor.cs:21`).
- **Webhooks unauthenticated but tenant-resolved from payload + signature-verified**:
  carriers cannot authenticate, so trust is moved to signature/IP validation + idempotent
  `ShippingWebhookLog`, and writes are confined to `Shipment` only
  (`ShippingWebhooksController.cs:33`, `CarrierShipmentService.cs:1304-1763`).
- **Outbound gated by money**: prevents shipping goods for unpaid (non-COD) orders; COD is
  the explicit carve-out because cash is collected at the door
  (`OrderOperationalFinancialAuthority.cs:22-52`).
- **Post-commit carrier registration via outbox**: carrier APIs are slow/untrusted and
  must never run inside the checkout SQL transaction; the `ShipmentExternalRegistrationRequestedEvent`
  fires after commit (`ShipmentExternalRegistrationRequestedConsumer.cs:9-56`,
  `CarrierShipmentService.cs:428-436`).
- **`CarrierMetadata` JSON, core never parses**: keeps carrier-specific schema churn out
  of the domain model (`Shipment.cs:343-356`).
- **Cancellation delegated to order lifecycle**: payment cancellation + inventory
  restoration must be atomic with order cancel; fulfillment cannot own that
  (`OrderFulfillmentService.cs:438-444`).

---

## 29. Technical Debt (implementation-backed only)

- **Dual carrier-config models.** Legacy `CarrierConfiguration` (runtime authority) vs
  `CarrierAccount` (governance) can disagree; no single reconciliation
  (`CarrierShipmentService.cs:317,1481`).
- **Dual shipment services.** `ShipmentService` (API) and `AdminShipmentService` overlap in
  transition validation/timestamps/events; partial duplication/drift risk
  (`ShipmentService.cs:35`, `AdminShipmentService.cs`).
- **`ServiceLevel` free-text magic strings** violate the repo's no-magic-strings rule for
  carrier codes (`Shipment.cs:65`).
- **No zone/dimension engine.** `ShippingCalculationService` uses
  `BaseRate + PerKgRate*weight`; package **dimensions are not modeled** (weight defaulted
  1.0kg) (`CarrierShipmentService.cs:478`, `ShippingManagement` findings).
- **No failed/returned shipment notification consumer.** `ShipmentFailedEvent` /
  `ShipmentReturnedEvent` exist but no `Shipment*NotificationConsumer` handles them
  (`ShipmentNotificationConsumerBase` consumers list).
- **FedEx "runtime foundation only; not certified"** — declared uncertified in code
  (`CarrierCodes`).
- **No dedicated warehouse "picking" state** — picking = checkout reservation; the prompt's
  picking stage is not a runtime state.
- **Reporting consumer not located** bound to shipment events in this runtime (analytics
  consumes the shared event stream; specific reporting projection consumer not verified).
- **CarrierAccount.HealthState is effectively dead at runtime** — default `Blocked`, never
  read outside admin; the new governance aggregate does not gate any live carrier
  operation (only the warehouse-assignment filter uses `State`/`OperationalState`).
- **No Polly/`RetryPolicy` in carrier path** — retry is hand-rolled via
  `ExternalRegistrationAttemptCount` + single-winner conditional-update claim +
  back-off-on-contention; outbound carrier calls have no rate limiter.
- **POS `OrderFulfillmentService` does NOT perform inventory commit/release** — those live
  in the CommerceHub sub-order lifecycle (`CommerceHubOrderManagementService` +
  `InventoryConsistencyService` + reconciliation job); POS fulfillment delegates
  fail-closed to the order-cancellation service.
- **No dedicated fulfillment/Shipment Mongo projection consumer or repair job** — the
  storefront order read model is fed by LEGACY order/shipment events; the NEW `Fulfillment*`
  events feed only Affiliate + payment; replay/repair is indirect (outbox slow-replay +
  per-consumer version fences).
- **`WebhookSecret`/`SupportsWebhooks` live only on legacy `CarrierConfiguration`** — the
  new `CarrierAccount` aggregate has no webhook-secret concept, so webhook trust cannot
  migrate to `CarrierAccount` without new fields.

---

## 30. Implementation Follow-up Items

| Item | Current | Priority | Status |
|---|---|---|---|
| Reconcile `CarrierConfiguration` vs `CarrierAccount` into one authority | divergence | High | open |
| Model package dimensions (L/W/H) for carrier rating | weight only | Med | open |
| Add failed/returned shipment notification consumers | gap | Med | open |
| Unify `ShipmentService`/`AdminShipmentService` transition logic | overlap | Med | open |
| Certify/remove FedEx adapter | uncertified | Low | open |
| Add Mongo/Search shipment projection if storefront scale needs it | SQL read only | Low | open |

---

## 31. Runtime Classification

**This runtime is fundamentally a FULFILLMENT LIFECYCLE RUNTIME wrapping a carrier-agnostic
SHIPMENT entity — i.e. a hybrid of Fulfillment + Shipping (Logistics), NOT pure
"Shipping", NOT pure "Delivery", NOT "Warehouse Execution", NOT "Distribution".**

Supporting evidence:
- The authoritative physical state machine (`FulfillmentStatus`) is the runtime's crown
  jewel and lives on the `Order`, owned by `IOrderFulfillmentService`
  (`FulfillmentStatus.cs:11`, `IOrderFulfillmentService.cs:33`).
- The `Shipment` entity is the logistics/carrier record that *converges into* fulfillment
  but is subordinate to it (`ShipmentService.cs:1102-1143`).
- Warehouse execution is NOT owned here (only an origin snapshot); distribution/polling is
  a carrier concern (`Shipment.cs:234-255`, `TrackingSyncJob`).
- Therefore: **Shipping/Fulfillment Runtime = Fulfillment Lifecycle (order physical axis)
  + Carrier-Agnostic Shipment Logistics (convergence spine).** "Shipping" names the
  tenant-configured method + carrier integration; "Fulfillment" names the order's physical
  state machine. They are separate but nested, with Fulfillment as the authority.

---

## 32. Final Architecture Question

**"How does Shopixy transform an immutable Order into a physically fulfilled customer
delivery while preserving inventory integrity, payment correctness, shipment
traceability, warehouse consistency, and historical auditability across every
participating runtime?"**

Answer, entirely implementation-backed:

1. **Immutable Order baseline.** At checkout the order freezes shipping method, cost,
   currency, address, and `FulfillmentMode`/`FulfillmentCarrierCode`
   (`Order.cs` snapshot, `ShipmentService.cs:128-136`). Inventory is reserved in the SAME
   Serializable tx (`Order` doc, inventory runtime) — inventory integrity preserved from
   the start.
2. **Fulfillment authority is single-writer.** `Order.FulfillmentStatus` can only move via
   `IOrderFulfillmentService`, fail-closed at `SaveChanges`
   (`FulfillmentStatusSaveChangesInterceptor.cs:87-98`). Every transition is a guarded,
   Serializable, row-locked, version-checked step
   (`OrderFulfillmentService.cs:533-721`).
3. **Money gates movement.** Non-COD, positive-total orders cannot ship until payment is
   recognized AND collection accounting is posted (`OrderOperationalFinancialAuthority.cs:22-52`);
   COD ships before payment but is recognized exactly at delivery
   (`OrderDeliveredEventConsumer.cs:55-94`). Payment correctness preserved.
4. **Shipment is the carrier spine, never the authority.** `Shipment` records tracking,
   carrier, COD, label, external-registration state; core never parses carrier JSON
   (`Shipment.cs:343-356`). Carrier APIs run ONLY post-commit via the outbox
   (`ShipmentExternalRegistrationRequestedConsumer.cs:9-56`), idempotent via
   `CarrierRequestId` (`CarrierShipmentService.cs:428-436`).
5. **Carrier webhooks are quarantined.** Unauthenticated but tenant-resolved,
   signature/IP-validated, idempotent via `ShippingWebhookLog`, and permitted to mutate
   `Shipment` ONLY — never `Order` (`ShippingWebhooksController.cs:33`,
   `CarrierShipmentService.cs:1304-1763,1617-1641`).
6. **Convergence, not duplication.** Every shipment state change calls
   `SynchronizeFulfillmentAuthorityAsync` → `IOrderFulfillmentService.SynchronizeFromShipmentAsync`,
   which replays the intermediate fulfillment steps through the SAME guarded funnel
   (`ShipmentService.cs:1102-1143`, `OrderFulfillmentService.cs:196-278`). Shipment traceability
   flows into the order's physical status without ever letting a carrier become the order's
   authority.
7. **Warehouse consistency.** The shipment origin (warehouse + geographic snapshot) is
   admitted/immutable at registration; post-success recovery must not reconstruct it from
   live Warehouse/CarrierConfiguration (`CarrierShipmentService.cs:3875-3881`). Inventory
   deduction/restoration is coordinated via the order cancellation/return lifecycle, not
   ad-hoc (`OrderFulfillmentService.cs:438-444`).
8. **Auditability.** Every mutation emits versioned integration events
   (`EventVersion = Order.ProjectionMutationVersion+1`) committed atomically with the SQL
   change (`ShipmentService.cs:902-927,767-900`); `ShippingWebhookLog` records every carrier
   push; `OrderFulfillmentLineageAuthority` replays against SQL truth
   (`OrderFulfillmentLineageAuthority.cs:14-95`); `ReconcileShipmentAuthorityAsync` repairs
   Shipment↔Order drift (`ShipmentService.cs:412-484`).

**Conclusion:** Shopixy does NOT have a "Shipping" runtime or a "Fulfillment" runtime in
isolation — it has a **Fulfillment Lifecycle Runtime (order physical axis, single-writer,
fail-closed) that is fed by a carrier-agnostic Shipment logistics entity (convergence
spine) and a quarantined carrier webhook seam.** Delivery is the terminal physical state;
payment, inventory, affiliate, notification, and reporting runtimes are coordinated via
atomic, versioned events — none of them can become the authority over the order's physical
or financial truth. Where implementation contradicts the prompt's naive lifecycle
(picking/packing/ready-to-ship as discrete states, or "Shipping" as a standalone runtime),
implementation wins.

---

## 33. Certification

- **Confidence**: HIGH for core (FulfillmentStatus machine, Shipment entity, webhook trust,
  COD-at-delivery, convergence funnel) — directly read from source.
- **Completeness**: HIGH for storefront fulfillment + carrier integration; PARTIAL for
  CommerceHub (separate surface, not using `Shipment`/`IOrderFulfillmentService`),
  Reporting (consumer not located), and Affiliate (delivery→approve confirmed).
- **Verified**: all `file:line` citations in §1–§32 were read from the C# backend.
- **Missing evidence**: (a) dedicated reporting projection consumer bound to shipment
  events (not located); (b) package dimensions (not modeled). These are flagged as
  gaps, not fabricated.
- **Debt summary**: dual carrier-config models, dual shipment services, no dimension
  engine, no failed/returned notification consumer, uncertified FedEx.
