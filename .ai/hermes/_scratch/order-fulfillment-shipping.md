# Order Fulfillment + Shipping + Status-Transition Runtime — Evidence Report

Scope: Shopixy/PointOfSale C#/.NET 9 monorepo. Every claim is `File:Line`. Evidence extracted by reading source, not comments.

---

## 1. `IOrderFulfillmentService` — the ONLY mutator of `Order.FulfillmentStatus`

- Contract declaration: `PointOfSale.Core/Interfaces/Fulfillment/IOrderFulfillmentService.cs:33` — "the ONLY gateway for changing order fulfillment status."
- Implementation: `PointOfSale.Infrastructure/Fulfillment/OrderFulfillmentService.cs:35` (`OrderFulfillmentService : IOrderFulfillmentService`).
- Encapsulation on the entity: `Order.FulfillmentStatus` setter records `_originalFulfillmentStatus` for interceptor detection — `PointOfSale.Core/Entities/Order.cs:195-218`; `AcceptFulfillmentStatusChange()` at `Order.cs:223`.

### Transition methods (all funnel through `TransitionAsync<TEvent>`)

| Method | Target status | Source |
|---|---|---|
| `ConfirmAsync` | `FulfillmentStatus.Confirmed` (Created→Confirmed) | `OrderFulfillmentService.cs:53`, factory `:60-78` |
| `PackAsync` | `Packed` (Confirmed→Packed) | `OrderFulfillmentService.cs:81`, factory `:89-108` |
| `MarkOutForDeliveryAsync` | `OutForDelivery` (Packed→OutForDelivery) | `OrderFulfillmentService.cs:111`, factory `:120-140` |
| `MarkDeliveredAsync` | `Delivered` (OutForDelivery→Delivered) | `OrderFulfillmentService.cs:143`, factory `:169-194` |
| `SynchronizeFromShipmentAsync` | Confirmed/Packed/OutForDelivery/Delivered (shipment convergence) | `OrderFulfillmentService.cs:196`, contract `IOrderFulfillmentService.cs:137` |
| `MarkCompletedAsync` | `Completed` (Delivered→Completed) | `OrderFulfillmentService.cs:280`, factory `:287-305` |
| `MarkReturnedAsync` | `Returned` (Delivered→Returned) | `OrderFulfillmentService.cs:308`, factory `:334-356` |
| `MarkDeliveryFailedAsync` | `FailedDelivery` (OutForDelivery→FailedDelivery) | `OrderFulfillmentService.cs:359`, factory `:373-394` |
| `RetryDeliveryAsync` | `OutForDelivery` (FailedDelivery→OutForDelivery) | `OrderFulfillmentService.cs:397`, factory `:404-422` |
| `CancelAsync` | **DELIBERATELY REFUSED** (see §8) | `OrderFulfillmentService.cs:425-445` |

### `FulfillmentStatus` enum + state machine
- Enum values 0..8: `Created, Confirmed, Packed, OutForDelivery, Delivered, Completed, Returned, FailedDelivery, Cancelled` — `PointOfSale.Core/Enums/FulfillmentStatus.cs:14-73`.
- Allowed transitions (`GetAllowedTransitions`): `FulfillmentStatus.cs:83-98`:
  - Created → {Confirmed, Cancelled}
  - Confirmed → {Packed, Cancelled}
  - Packed → {OutForDelivery, Cancelled}
  - OutForDelivery → {Delivered, FailedDelivery}
  - Delivered → {Completed, Returned}
  - FailedDelivery → {OutForDelivery, Cancelled}
  - Completed/Returned/Cancelled → {} (terminal)
- Validation gate: `oldStatus.CanTransitionTo(targetStatus)` in `OrderFulfillmentService.cs:664-673` (rejects invalid transitions).

### State-machine guards inside `TransitionAsync` (`OrderFulfillmentService.cs:533-721`)
- Source-vs-mode ownership reject: `:554-578` (`source.IsAllowedFor(order.FulfillmentMode)`), logs `[SECURITY_VIOLATION]`.
- Terminal-order block: `:582-600` (`IsTerminalOrderLifecycle` at `:756-757`: Cancelled/Refunded/Completed).
- Replay collapse against current status: `:602-621`.
- Payment-authority gate (no fulfillment before payment recognized): `:623-641` + `RequiresRecognizedPaymentBeforeFulfillment` `:822-825`.
- Collection-accounting gate: `:643-661` + `RequiresPostedCollectionAccountingBeforeFulfillment` `:817-820`.
- Authorized-context enable + OrderStatus convergence: `:676-713` (`FulfillmentStatusSaveChangesInterceptor.EnableAuthorizedContext()`; `OrderStatusSaveChangesInterceptor.BeginAuthorizedContext()`; sets `order.FulfillmentStatus` `:680`; converges `Order.Status` via `ResolveOrderStatus` `:759-787`; publishes `OrderStatusChangedEvent` `:690-698`; publishes domain event `:701-702`; `SaveChangesAsync` `:704`).
- Row lock (`SELECT … FOR UPDATE` / `UPDLOCK,ROWLOCK,HOLDLOCK`): `LoadOrderForFulfillmentMutationAsync` `:723-754`; serializable tx `:543-546`.

---

## 2. `FulfillmentStatusSaveChangesInterceptor`

- File: `PointOfSale.Infrastructure/Data/FulfillmentStatusSaveChangesInterceptor.cs:21` (`SaveChangesInterceptor`).
- Authorized flag: `AsyncLocal<bool> _authorizedContext` `:29`; `EnableAuthorizedContext` `:35`; `DisableAuthorizedContext` `:40`.
- `ValidateFulfillmentStatusChanges`: `:69-99` — if not authorized and any modified `Order` has `FulfillmentStatusChanged`, throws `InvalidOperationException` `:93-97`. Service sets the flag around the mutation (`:676`, `:712`).
- Triggered on `SavingChanges` / `SavingChangesAsync` `:52-67`.

---

## 3. `FulfillmentMode` immutability (Manual vs Carrier)

- Enum `Manual=0`, `Carrier=1` — `PointOfSale.Core/Enums/FulfillmentMode.cs:21-63`; invariants documented `:9-19` (mode decided at checkout, never changes, Manual=admin only, Carrier=webhook only).
- `AllowsManualTransitions` / `AllowsCarrierWebhooks` extensions: `FulfillmentMode.cs:74-85`.
- Entity snapshot + immutability comment: `Order.cs:255-277` (`FulfillmentMode`, `FulfillmentCarrierCode`), computed `AllowsManualFulfillment`/`AllowsCarrierWebhooks` `:283-289`.

### DB trigger enforcing immutability
- Standalone SQL migration `add_fulfillment_ownership_snapshot.sql`:
  - Adds columns + backfills from `ShippingMethods.CarrierCode` — `:31-102`.
  - CHECK constraint `CK_Orders_FulfillmentOwnership` (Manual⇒carrier NULL, Carrier⇒carrier NOT NULL) — `:111-133`.
  - **Trigger** `trg_prevent_fulfillment_mode_update` (BEFORE UPDATE ON "Orders") + function `prevent_fulfillment_mode_update()` rejects any change to `FulfillmentMode` OR `FulfillmentCarrierCode` — `:142-179` (`RAISE EXCEPTION … ERRCODE='integrity_constraint_violation'`).
- EF-side columns also exist in model: `PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs:9088-9091` and migration `20260510130500_…cs:3769-3770` (column only; **no EF equivalent of the trigger/CHECK**).
- **Flag (missing/partial evidence):** The trigger/CHECK live ONLY in the standalone `.sql` script. No grep hit wires `add_fulfillment_ownership_snapshot.sql` into DbPatcher/CI (`grep` for that filename in `.cs`/`.ps1`/`.sql` returned nothing). The enforcement therefore depends on the SQL script having been applied out-of-band; the EF model snapshot does not include the trigger or the CHECK constraint. Verify deployment path before relying on DB-level immutability.

### Source-vs-mode matrix (`FulfillmentRequestSource.IsAllowedFor`)
- `Manual` only → Manual; `Webhook` only → Carrier; `System` → both — `PointOfSale.Core/Enums/FulfillmentRequestSource.cs:45-62`.
- Manual path: admin controller passes `FulfillmentRequestSource.Manual` to every `IOrderFulfillmentService` call — `PointOfSale.API/Controllers/OrderFulfillmentController.cs:106,131,157,189,217,260,295,323`.
- Carrier/webhook path: `CarrierShipmentService.SynchronizeFromShipmentAsync(..., FulfillmentRequestSource.Webhook, ...)` at `PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs:4813,4873`; also `ShipmentService.cs:1116`.
- `GetAvailableActionsAsync` returns EMPTY for carrier orders — `OrderFulfillmentService.cs:472-486`.

---

## 4. `OrderShipmentOrchestrationConsumer.cs` + `OrderFulfillmentEvents.cs`

### `OrderShipmentOrchestrationConsumer` — `PointOfSale.API/Consumers/OrderShipmentOrchestrationConsumer.cs`
- Listens: `OrderConfirmedEvent`, `OrderPaidEvent`, `OrderCollectionAccountingPostedEvent`, `OrderCancelledEvent` — `:15-20`.
- `EnsureShipmentExistsAsync` `:134-318`: loads order authority snapshot (`:150-161`), skips if no `ShippingMethodId` (`:173-181`), skips cancelled (`:183-191`), defers until collection accounting posted for non-COD (`:193-204`, `RequiresAccountingAuthorityBeforeShipment` `:324-341`), then `CreateShipmentAsync` (`:208-213`); on `ShipmentAlreadyExistsException` attempts completion of `PendingExternalRegistration` carrier registration (`:215-299`).
- `OrderCancelledEvent` handler `:71-132`: looks up shipment (`:82-95`); if found and not terminal, calls `ICarrierShipmentService.CancelShipmentAsync` (`:97-101`); if carrier refuses with `CANNOT_CANCEL`, marks `ReconciliationRequired` (`:113-128`, `MarkShipmentCancellationReconciliationRequiredAsync` `:343-377`).

### `OrderFulfillmentEvents.cs` — `PointOfSale.Core/Events/OrderFulfillmentEvents.cs`
- Base `OrderFulfillmentEventBase` (carries `Source`, `IdempotencyKey`, lineage) `:20-40`.
- `FulfillmentConfirmedEvent` (NewStatus=Confirmed) `:45`; `OrderPackedEvent` `:56`; `OrderOutForDeliveryEvent` (ShipmentId, TrackingNumber) `:72`; `FulfillmentDeliveredEvent` (DeliveryReference, ProofOfDeliveryUrl, PaymentMethod, PaymentStatus, OrderTotal) `:102` — note distinct name to avoid clash with `OrderDeliveredEvent` `:100`; `OrderCompletedEvent` `:145`; `OrderReturnedEvent` `:160`; `OrderDeliveryFailedEvent` `:191`; `OrderFulfillmentCancelledEvent` `:217` (declared but **not wired** — see §8).
- Shipment/tracking/delivery events (separate file `OrderEvents.cs`): `OrderShippedEvent` `OrderEvents.cs:116`, `OrderDeliveredEvent` `:128`, `OrderCancelledEvent` `:138` (has `CancellationReason`, `CancelledAt`, `RestoreInventory`). Shipment events consumed by `OrderStatusSyncConsumer` live in `ShipmentEvents.cs` (referenced by `OrderStatusSyncConsumer.cs:29-34`).

---

## 5. `OrderDeliveredEventConsumer.cs`

- NOTE: file is named `OrderDeliveredEventConsumer.cs` but the class is **`FulfillmentDeliveredEventConsumer`** and consumes **`FulfillmentDeliveredEvent`** — `PointOfSale.Infrastructure/Consumers/OrderDeliveredEventConsumer.cs:26`.
- Registered in DI: `PointOfSale.API/Program.cs:1853`.
- Integration of fulfillment→payment lifecycle:
  - **RULE 1 COD**: if COD & `PaymentStatus==Pending` → `IPaymentLifecycleService.MarkOfflineCollectionDeliveredAsync` (ledger journal) — `:55-94`.
  - **RULE 2 Wallet**: if Wallet & `PaymentStatus==AwaitingVerification` → reloads fresh status (`:103-107`); if still AwaitingVerification → throws (fail-closed, must approve first) `:118-127`; otherwise proceeds `:128-135`.
  - **RULE 3 Online**: `Collected`/`Settled` → no action `:140-147`.

---

## 6. `OrderStatusSyncService` + `OrderStatusSyncConsumer` + `IOrderStatusSyncService`

- Interface `PointOfSale.Core/Interfaces/IOrderStatusSyncService.cs:30` — two methods: `SyncFromShipmentAsync` `:47`, `SyncFromPaymentAsync` `:68`. "Orders REACT to events; never polls."
- Impl `PointOfSale.API/Services/OrderStatusSyncService.cs:50`.
  - `SyncFromShipmentAsync` `:67-241`: row-lock load (`:90`, `LoadOrderForStatusMutationAsync` `:351-390`), validates current SQL shipment lineage via `OrderFulfillmentLineageAuthority.LoadCurrentShipmentAsync` (`:102-122`), collapses replayed status, computes `MapShipmentStatusToOrderStatus` (`:403-427`: Pending/ReadyForPickup→Processing, PickedUp/InTransit/OutForDelivery→Shipped, Delivered→Delivered, Cancelled→Cancelled, Returned→**Refunded**, Failed→no change), guards: terminal (`:148-155`), forward-only `IsValidStatusProgression` (`:466-492`), blocks Pending promotion (`:166-177`), blocks outbound without financial authority (`:179-199`), then `BeginAuthorizedContext` + set `order.Status` (`:201-218`) and publish `OrderStatusChangedEvent` (`:207-216`).
  - `SyncFromPaymentAsync` `:244-336`: only acts when `oldStatus==Pending` (`:278-285`); only on paid state `PaymentStatusAuthority.IsPaidState` (`:287-294`); sets `OrderStatus.Confirmed` (`:296-311`).
- Consumer `PointOfSale.Infrastructure/Consumers/OrderStatusSyncConsumer.cs:28` subscribes to `ShipmentStatusChangedEvent`, `ShipmentCreatedEvent` (→ReadyForPickup), `ShipmentDeliveredEvent`, `ShipmentCancelledEvent`, `ShipmentReturnedEvent`, `OrderPaymentStatusChangedEvent` (`:29-34`); each delegates to the sync service; endpoint `order-status-sync`, retry policy `:279-297`. Validates non-empty TenantId `:237-247`.
- Note: `OrderFulfillmentService.TransitionAsync` ALSO converges `Order.Status` directly (§1 `:684-698`) — i.e. fulfillment transitions and the event-driven sync service are two convergence paths into `Order.Status`, both behind `OrderStatusSaveChangesInterceptor`.

---

## 7. `OrderStatus` enum transitions + `OrderStatusSaveChangesInterceptor` authorized-boundary model

- Enum `Pending=0, Confirmed=1, Processing=2, Shipped=3, Delivered=4, Completed=5, Cancelled=6, Refunded=7, OnHold=8` — `PointOfSale.Core/Enums/OrderStatus.cs:6-34`.
- **No centralized `OrderStatus` state-machine enum method** (unlike `FulfillmentStatus.GetAllowedTransitions`). Forward-only progression is enforced ad-hoc in `OrderStatusSyncService.IsValidStatusProgression` (`:466-492`) and in `OrderFulfillmentService.ResolveOrderStatus`/`GetOrderProgressionRank` (`OrderFulfillmentService.cs:759-800`). **Flag:** there is no single canonical `OrderStatus.CanTransitionTo`; transition rules are duplicated (sync service vs fulfillment service vs admin cancellation).
- Interceptor `PointOfSale.Infrastructure/Data/OrderStatusSaveChangesInterceptor.cs:22`:
  - Fail-closed by default (`_enforcementEnabled = true`) `:27`.
  - `BeginAuthorizedContext()` increments `AsyncLocal<int> _authorizedDepth` `:80-86`; `IsAuthorizedContext` `:86`.
  - `WithEnforcementDisabled()` / `DisableEnforcement()` for dev/test only `:52-78`.
  - `ValidateOrderStatusChanges` `:105-155`: if not authorized and any modified `Order` has `Status` property modified → logs `[SECURITY_VIOLATION]` with caller stack `:125-130` and throws `InvalidOperationException` `:143-145` when enforcement active; warns only when disabled `:147-154`.
  - Authorized writers observed: `OrderFulfillmentService.cs:677` (converges on fulfillment transition); `OrderStatusSyncService.cs:201,297`; `AdminOrderService.cs:686`; `CheckoutService.cs:602`; `PaymentLifecycleService.cs:2373` (all wrap `order.Status` writes in `BeginAuthorizedContext()`).

---

## 8. Cancellation paths, `CancelledAt`/`CancellationReason`, reservation release, inventory recovery, `OrderCancelledEvent`

### `CancelledAt` / `CancellationReason` fields
- `Order.CancelledAt` (`DateTime?`), `Order.CancellationReason` (`string?`) — `PointOfSale.Core/Entities/Order.cs:641-642`.

### Cancellation paths found
1. **Merchant/admin cancellation** — `AdminOrderService.CancelOrderAsync` `PointOfSale.API/Services/Admin/AdminOrderService.cs:475-814` (endpoint `AdminOrdersController.cs:287`). Reason required (`:487-493`); ownership check (`:496-503`); rejects Delivered/Completed (`:517-523,589-595`); blocks bypass of recognized payment (`:525-529,597-601`); within tx sets `order.Status` (`:686-690`), `CancelledAt`, `CancellationReason` (`:688-689`), transition `CheckoutState` (`:692-695`), releases coupon (`:697-705`) and inventory when not deferring (`:745-757`); publishes `OrderCancelledEvent` (`:783-792`) or `OrderStatusChangedEvent` (`:796-805`). Warehouses cancellation intent in `PaymentCancellationOutboxMessage` when payment still pending (`:710-738`).
2. **Automatic / payment-driven cancellation** — `PaymentLifecycleService` on Rejected/Cancelled/Expired sets `OrderStatus.Cancelled`, `CancelledAt`, `CancellationReason` and releases reservations+coupon — `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs:2370-2414` (authorized context `:2370-2373`); also publishes `OrderCancelledEvent` when already Cancelled `:6230-6239` (`RestoreInventory=false`).
3. **Automatic / checkout-failure cancellation** — `CheckoutService` closes unrecoverable post-commit payment-intent gap: sets `OrderStatus.Cancelled`, `CancelledAt`, `CancellationReason`, releases reservations+coupon, publishes `OrderCancelledEvent`(`RestoreInventory=false`) — `PointOfSale.API/Services/Checkout/CheckoutService.cs:588-620` (authorized context `:602`).
4. **`OrderShipmentOrchestrationConsumer` on `OrderCancelledEvent`** compensates the carrier shipment (cancel or mark ReconciliationRequired) — `PointOfSale.API/Consumers/OrderShipmentOrchestrationConsumer.cs:71-132` (see §4).
- **Customer/storefront self-cancellation path: NOT FOUND.** No storefront/customer controller or service exposes an order-cancel endpoint (grep of `StorefrontOrdersReadController.cs`, `OrdersController.cs`, storefront services returned no cancel action). **Flag:** the task asks for a customer-initiated cancellation path; evidence shows only merchant + automatic (payment/checkout) paths. The only "customer requested" reference is a string in `AdminAuditService.cs:274`, not a code path.

### `IOrderFulfillmentService.CancelAsync` is intentionally a dead-end
- `OrderFulfillmentService.CancelAsync` `:425-445` **refuses** with `ORDER_CANCELLATION_LIFECYCLE_REQUIRED` — fulfillment cancellation is delegated to the authoritative order-cancellation services; it does NOT write `FulfillmentStatus.Cancelled`. (Note the fulfillment state machine *does* allow `Cancelled` as a target — `FulfillmentStatus.cs:95`, and `OrderFulfillmentCancelledEvent` exists at `OrderFulfillmentEvents.cs:217` — but no caller transitions fulfillment to Cancelled. **Flag:** `OrderFulfillmentCancelledEvent` appears unused.)

### Reservation release + inventory recovery
- Admin: `ReleaseOrderReservationsAsync` when `!paymentCancellationQueued` — `AdminOrderService.cs:745-757`.
- Payment-driven: `_stockRepository.ReleaseOrderReservationsAsync` + coupon release — `PaymentLifecycleService.cs:2400-2414`.
- Checkout-failure: `_variantStockRepository.ReleaseOrderReservationsAsync` + coupon — `CheckoutService.cs:589-599`.
- Coupon release helper: `ReleaseCouponReservationAsync(...)` across all three paths.

### `OrderCancelledEvent` consumers (read-model sync, not inventory)
- `OrderProjectionConsumer.Consume(OrderCancelledEvent)` updates MongoDB projection Status="Cancelled" + timeline — `PointOfSale.Infrastructure/Consumers/OrderProjectionConsumer.cs:845-878`.
- **Inventory recovery is NOT performed inside `OrderCancelledEvent` consumers** — `RestoreInventory` is always `false` in every `OrderCancelledEvent` published (`AdminOrderService.cs:790`, `CheckoutService.cs:616`, `PaymentLifecycleService.cs:6239`). Actual inventory release happens at cancellation time in the authoritative services (above), not via the event.

---

## 9. Cross-cutting flags / missing evidence (summary)
- **FulfillmentMode trigger deployment unverified:** trigger + CHECK constraint exist only in `add_fulfillment_ownership_snapshot.sql`; no wiring into DbPatcher/CI found; EF model snapshot lacks trigger/CHECK. Confirm the script is applied in target environments.
- **No canonical `OrderStatus` state machine:** transition rules are duplicated across `OrderStatusSyncService` and `OrderFulfillmentService`; no `OrderStatus.CanTransitionTo` equivalent exists.
- **No customer/storefront order-cancellation code path** was found; only merchant (admin) + automatic (payment/checkout-failure) paths exist.
- **`OrderFulfillmentCancelledEvent` (`OrderFulfillmentEvents.cs:217`) appears unused** — fulfillment `CancelAsync` refuses, so no fulfillment→Cancelled transition is exercised.
- `OrderDeliveredEventConsumer.cs` filename vs class `FulfillmentDeliveredEventConsumer` consuming `FulfillmentDeliveredEvent` — naming is misleading but behavior is consistent with §5.
