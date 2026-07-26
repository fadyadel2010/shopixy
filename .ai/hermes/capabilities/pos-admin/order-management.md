# Order Management (POS Admin)

> Reverse-engineered from implementation. Implementation is authoritative; documentation
> claims that conflict with the code below are flagged in "Documentation Validation".
> Capability is described independently of any UI; the POS Admin Orders surface is only an
> operational presentation of this capability.

## Purpose

Order Management owns the **authoritative e-commerce order record** for the PointOfSale
platform and the complete operational lifecycle that turns a storefront cart into money,
shipment, and customer-facing visibility.

Business problem it solves:
- Give one durable, tenant-scoped source of truth for "what did this customer buy, for how
  much, and where is it now" across every channel (Storefront, POS, CommerceHub/Marketplace,
  Amazon, manual entry).
- Keep the financial truth (Payment), the physical truth (Fulfillment/Shipment), and the
  presentation truth (Mongo/Storefront read models) consistent without letting any downstream
  projection retroactively rewrite the order.
- Provide safe admin mutation paths (status override, cancellation) that preserve
  cross-system integrity (don't cancel an order whose money was already collected without a
  refund; don't let an admin advance fulfillment ahead of financial recognition).

What it owns:
- The `Order` aggregate and its `OrderItem`/`OrderRefund` children (SQL/PostgreSQL).
- Order identity (`OrderNumber`), order-level status (`OrderStatus`), payment status
  (`PaymentStatus`), fulfillment status (`FulfillmentStatus`), checkout state
  (`CheckoutState`), fulfillment ownership mode (`FulfillmentMode`).
- The pricing snapshot, customer snapshot, shipping/geographic snapshot, and financial
  authority flags persisted at order creation.
- The SQL → outbox → Mongo projection pipeline for storefront order visibility.
- CommerceHub sub-order read/listing authority inside POS Admin.

## Business Scope

Reverse-engineered responsibilities (only those proven in code):

Order Creation
- Two creation paths:
  1. Storefront/CommerceHub **checkout pipeline** (`CheckoutService.CreateOrderAsync`): the
     only supported path for `OrderSource.Ecommerce`/`Marketplace`. Enforces inventory
     reservation, pricing re-validation, coupon/offer reservation, idempotency, affiliate
     attribution, and atomic event publishing.
  2. Admin/back-office manual creation (`OrdersController.CreateOrder`): explicitly **rejects**
     `Ecommerce`/`Marketplace` sources (HTTP 400) because those must go through checkout to
     keep cart → payment intent → inventory → projection → settlement synchronized.
- Manual admin creation still runs the same guardrails: idempotency key, request hash,
  financial recomputation, currency validation, customer/online-customer validation,
  order-number generation, inventory reservation, coupon reservation, marketplace outbox
  enqueue, and atomic `OrderCreatedEvent` via the EF/MassTransit outbox.

Order Identity
- `OrderNumber` format: Postgres uses a tenant sequence
  (`DocumentNumberingSupport.FormatOperationsOrderNumber` → `ORD-yyyyMMdd-<seq>`); SQL Server
  falls back to a date-prefix + max-suffix query under a tenant row lock. Not user-supplied.

Order Number — see above.

Order Lifecycle
- Two cooperating state machines on every order:
  - `OrderStatus` (operational): Pending → Confirmed → Processing → Shipped → Delivered →
    Completed, plus Cancelled / Refunded / OnHold.
  - `CheckoutState` (placement/payment progression): Draft → PendingPaymentIntent →
    PaymentPending → Paid, plus Failed / Cancelled / Expired.
- `PaymentStatus` (money) and `FulfillmentStatus` (physical) are **separate** machines,
  each owned by its own lifecycle service (see Authority Boundaries).

Order Status — owned by `OrderStatusSaveChangesInterceptor` (fail-closed). Direct
`order.Status =` writes are blocked unless the current async flow entered an authorized
context via `OrderStatusSaveChangesInterceptor.BeginAuthorizedContext()`.

Payment Status
- Owned exclusively by `IPaymentLifecycleService` (`PaymentLifecycleService`).
- `Order.PaymentStatus` is encapsulated; the backing field tracks original vs current value
  so a `SaveChanges` interceptor can detect unauthorized direct writes. `Order.cs` comment
  references `PaymentStatusSaveChangesInterceptor`, but the enforced runtime guard for
  `PaymentStatus` is the encapsulated setter + lifecycle-service-only mutation; verify
  against the actual interceptor wiring if you debug a "status changed unexpectedly" report.

Fulfillment Status
- Owned exclusively by `IOrderFulfillmentService` (`OrderFulfillmentService`). Encapsulated
  setter + interceptor guard, same pattern as payment.

Shipment Status
- Shipment is a separate aggregate (`Shipment`). Order status is *derived* from shipment
  status by `OrderStatusSyncService.SyncFromShipmentAsync` (see Lifecycle). Shipment is the
  authority for the physical journey; order status is a projection of it (for outbound
  progression) plus payment/fulfillment truth.

Cancellation
- Admin cancellation (`AdminOrderService.CancelOrderAsync`) is a guarded operation, not a
  status flip. Rules:
  - Reason is **required** (`CancellationReasonRequiredException`).
  - Delivered/Completed orders are immutable (cannot cancel) — `AdminOrderImmutableException`.
  - If money was recognized (PaymentStatus in Collected/Settled/Refunded/PartiallyRefunded,
    or a Paid PaymentIntent exists), cancellation is **refused** with
    `AdminOrderPaymentRequiresRefundException` — you must run the refund lifecycle first.
  - If a cancellable PaymentIntent exists (Pending/Authorized/AwaitingVerification), the order
    is moved to **OnHold** (not Cancelled), a `PaymentCancellationOutboxMessage` is persisted,
    and inventory release is **deferred** until payment cancellation completes. A marker note
    `[admin-cancellation-pending]` records this.
  - Otherwise the order moves to **Cancelled**, inventory reservations are released,
    coupon reservation released, and `OrderCancelledEvent` is emitted.

Partial Cancellation
- Not implemented as order-level partial cancellation. Per-line refunds exist
  (`OrderRefund`/`OrderRefundLine`, CommerceHub refund/return requests) but they flow through
  `IPaymentLifecycleService.RecordRefundAsync`, not through order cancellation.

Order Items
- `OrderItem` is an **immutable SKU snapshot**. Pricing/stock come from `ProductVariant`
  (VariantId), never from `Product`. Money is tracked per item (`PaidAmount`,
  `RefundedAmount`); `Order.Total` = Σ `OrderItem.Subtotal`. Enables partial payment/refund
  and SKU-level disputes.

Order Snapshot
- Pricing snapshot (`PricingSnapshotJson`), customer snapshot (GuestEmail/Phone/Name or
  CustomerId/OnlineCustomerId), and shipping/geographic snapshot (full denormalized address +
  place IDs + catalog release IDs) are persisted at creation and are immutable afterward.

Pricing Snapshot
- Captured at checkout commit for audit reconstruction. The Order totals
  (Subtotal, PromotionDiscountAmount, DiscountAmount, ManualDiscountAmount, TotalDiscountAmount,
  TaxAmount, Total, Currency, ShippingCost) are the accepted authority; Mongo merely echoes them.

Customer Snapshot
- Customer identity is either POS `Customer` (CustomerId), Storefront `OnlineCustomer`
  (OnlineCustomerId), or guest (GuestEmail/Phone/Name + IsGuestCheckout). Affiliate guest
  checkout sets IsGuestCheckout = true.

Inventory Reservation
- Reserveinventory happens inside the checkout transaction against `ProductVariantStocks`
  (write-side reservation authority). A reservation id + expiry are produced and an
  `InventoryReservedEvent` outbox row is persisted atomically with the order.
- Release happens on cancellation (non-pending-payment case) and on payment failure/expiry
  via the lifecycle outbox. Reservation release is per-reservation (filters to
  RemainingReservedQuantity > 0); net-sum release is explicitly avoided to prevent
  over-release when positive and negative reservations offset.

Order Completion
- `FulfillmentStatus.Completed` (manual) or `OrderStatus.Completed` (admin/order sync).
  Terminal. Delivered/Completed orders are immutable to admin status mutation.

Order Failure
- `PaymentStatus.Failed`, `CheckoutState.Failed`, `CheckoutState.Expired`,
  `PaymentStatus.Expired`/`AmountMismatch` (provider mismatch blocks fulfillment).
  Failure does not delete the order; inventory is released via the lifecycle outbox.

Returns Relationship
- `FulfillmentStatus.Returned` and `OrderStatus.Refunded` are reached via the return/refund
  flow, not ordinary cancellation. `OrderFulfillmentService.MarkReturnedAsync` refuses to
  create refund authority — a separate `IPaymentLifecycleService` refund must be recorded.

Refund Relationship
- `OrderRefund`/`OrderRefundLine` entities; `IPaymentLifecycleService.RecordRefundAsync`
  drives `PaymentStatus` → Refunded/PartiallyRefunded and posts accounting journals.

CommerceHub Orders
- Represented by `CommerceHubMarketplaceOrder` → `CommerceHubSubOrder`. The sub-order is the
  order-of-record inside POS Admin for marketplace channels (`Source = Marketplace`). It has
  its own Status/PaymentStatus, commission snapshot, and store (tenant) binding. POS Admin
  lists/reads them alongside standard orders (combined query). Marketplace orders are **not**
  rows in the `Orders` table.

Storefront Orders
- Standard `Order` rows with `Source = Ecommerce` and an `OnlineCustomerId` association. These
  are what get projected to Mongo for the storefront read model.

Affiliate Attribution
- `Order.AffiliateAccountId` / `AffiliateReferralCode` / `PromotionLinkId` are stamped at
  checkout from **server-side** `AffiliateModeResolver` (never client payload). An
  `AffiliateAttribution` row is created inline during checkout. The `OrderProjectionConsumer`
  also consumes `AffiliateAttributedEvent`/`AffiliateCommissionEarnedEvent` to mark affiliate
  attribution on the Mongo read model. Admin list/detail attaches affiliate evidence by
  joining `AffiliateAttributions` against the order's stamped fields.

Notifications
- `WhatsAppOrderConfirmationRequestedEvent` + `WhatsAppOrderConfirmationConsumer` produce the
  order confirmation message. Notification is a downstream effect of order creation, not owned
  by Order Management.

Analytics
- `Order.SessionId` is a reference to the analytics session captured at checkout. Order
  Management stores only the reference; journey data lives in the Analytics domain.

Order Visibility
- Two read surfaces:
  1. POS Admin: reads SQL directly (`OrdersController`, `AdminOrdersController`,
     `AdminOrderService`) — PostgreSQL is the ONLY source of truth here.
  2. Storefront: reads Mongo (`OrderDocument`) projected from SQL via `OrderProjectionConsumer`.
- Elasticsearch is **not** used for order search in this codebase (no order index found);
  storefront order history is Mongo-keyed by `(TenantId, OnlineCustomerId)`.

Order Search
- Admin search filters SQL (order number, customer name/email, guest name/email/phone,
  date range, status, payment status, source, affiliate flag). Marketplace search uses
  sub-order order number / full name / phone.

Order Projection
- SQL → EF/MassTransit outbox → RabbitMQ → `OrderProjectionConsumer` → Mongo `orders`
  collection. Idempotent, tenant-isolated, version-freshness-checked (LastEventVersion
  monotonicity). Mongo is a derivative read model, never authoritative.

What Order Management does NOT own:
- Accounting/ledger journals (created by Payment Lifecycle when money is recognized; Order
  merely carries `FinancialPostingStatus` flags).
- Settlement/bank posting (Payment Lifecycle + treasury).
- Inventory quantities themselves (Inventory domain owns stock; Order Management only
  reserves/releases).
- Shipping carrier integration (Shipping domain owns carrier webhooks; Order Management
  consumes shipment status).
- Customer identity records (Customer/OnlineCustomer domains).
- Affiliate commission payout (Affiliate domain owns earnings; Order stamps attribution).
- Storefront UI/CMS (Storefront domain).

## Authority Boundaries

| Concern | Authority (writes truth) | Projection / Evidence | Presentation |
|---|---|---|---|
| Order record | `Orders` SQL (PostgreSQL) | Mongo `orders` doc | Storefront, POS Admin |
| Order status | `OrderStatusSaveChangesInterceptor` + authorized flows | `OrderStatusChangedEvent` → Mongo | UI |
| Payment status | `IPaymentLifecycleService` (Payment domain) | `OrderPaymentLineageAuthority` SQL truth used to collapse replays | UI |
| Fulfillment status | `IOrderFulfillmentService` | `Fulfillment*Event` → Mongo | UI |
| Inventory reservation | `ProductVariantStocks` (Inventory domain) | `InventoryReservedEvent` outbox | UI |
| Inventory release | Lifecycle outbox → Inventory domain | — | UI |
| Pricing snapshot | Checkout pricing engine (SQL product/variant authority) | `PricingSnapshotJson` on Order | UI |
| Shipping/shipment | `Shipment` SQL + carrier webhooks (Shipping domain) | `ShipmentStatusChangedEvent` → Mongo | UI |
| Shipment→Order derivation | `OrderStatusSyncService` (collapses to SQL order status) | `OrderStatusChangedEvent` | UI |
| Customer identity | Customer / OnlineCustomer domains | FK on Order | UI |
| Notifications | WhatsApp/Notification domain | `WhatsAppOrderConfirmationRequestedEvent` | WhatsApp |
| Analytics | Analytics domain | `Order.SessionId` reference only | dashboards |
| Search | SQL (admin) / Mongo (storefront) | — | UI |
| CommerceHub projection | `CommerceHubSubOrder` SQL | `CommerceHubOrderDocument` Mongo | CommerceHub UI |
| Affiliate attribution | Checkout stamping + Affiliate domain | `AffiliateAttribution` join | UI |
| Accounting | Ledger (Payment Lifecycle) | `FinancialPostingStatus` flags on Order | UI |

Key invariants:
- PostgreSQL is the only system of record for orders. Mongo/Redis/ES are derivatives.
- `Order.FulfillmentMode` (Manual/Carrier) is snapshotted at creation and **immutable**; it —
  not `ShippingMethod` — determines who may drive fulfillment. Manual orders reject carrier
  webhooks; Carrier orders reject manual admin transitions (409 Conflict).
- Payment status can never be advanced by a projection replay. `OrderPaymentLineageAuthority`
  loads current SQL payment truth; a replayed `OrderPaymentStatusChangedEvent` that disagrees
  is collapsed to SQL truth and the stale projection may be deleted if no SQL authority exists.
- Outbound fulfillment/order progression is gated by `OrderOperationalFinancialAuthority`:
  non-COD, positive-total orders cannot advance outbound until payment is in a paid state
  AND (if required) the collection accounting journal is posted.

## Order Lifecycle

Reverse-engineered actual flow:

```
Storefront cart (Redis)
  ↓  CheckoutService.CreateOrderAsync
     - distributed lock (checkout:{tenant}:{cartId})
     - idempotency claim (request hash)
     - re-validate prices from SQL ProductVariant (NOT projection)
     - re-validate shipping + payment from SQL
     - reserve inventory (ProductVariantStocks) → reservation id + expiry
     - stamp affiliate attribution (server-side)
     - reserve coupon / applied offer
  ↓  Build Order aggregate (immutable SKU/item snapshots, pricing snapshot,
     geographic snapshot, FulfillmentMode, PaymentMethod snapshot)
  ↓  EmitOrderCreatedEventAsync (OrderCreatedEvent via EF outbox) + marketplace outbox enqueue
  ↓  PersistInventoryReservedOutboxAsync (atomic with order + idempotency bind)
  ↓  SaveChangesAsync + transaction.Commit  (exactly-once order+idempotency boundary)
Order created in SQL (Status=Pending, PaymentStatus=Pending, CheckoutState=PendingPaymentIntent,
                       FulfillmentStatus=Created)
  ↓  Outbox → RabbitMQ → OrderProjectionConsumer → Mongo OrderDocument (idempotent)
  ↓  WhatsAppOrderConfirmationRequestedEvent → confirmation message
  ↓  Payment: IPaymentLifecycleService drives PaymentStatus via PaymentIntent webhooks
       Pending → Authorized → Collected → Settled (or COD/Failed/Expired/Refunded…)
  ↓  OrderStatusSyncService.SyncFromPaymentAsync: Pending → Confirmed once payment is a paid state
  ↓  Fulfillment: IOrderFulfillmentService (Manual) OR carrier webhooks (Carrier)
       Created → Confirmed → Packed → OutForDelivery → Delivered → Completed
  ↓  Shipping: Shipment aggregate updated by Shipping domain / carrier webhooks
  ↓  OrderStatusSyncService.SyncFromShipmentAsync derives OrderStatus from ShipmentStatus:
       PickedUp/InTransit/OutForDelivery → Shipped
       Delivered → Delivered
       Returned → Refunded
       Failed → (no change; admin intervention)
       Cancelled → Cancelled
  ↓  Mongo projection updated from each facet event (OrderStatusChanged,
     OrderPaymentStatusChanged, ShipmentStatusChanged, OrderShipped, OrderDelivered,
     OrderCancelled) — each gated by SQL authority + version freshness
Customer sees updated order in Storefront (Mongo read model)
```

Cancellation path (admin):
```
AdminOrderService.CancelOrderAsync
  - reason required; Delivered/Completed immutable
  - if money recognized → refuse (refund lifecycle required)
  - if pending PaymentIntent → OnHold + PaymentCancellationOutboxMessage, defer inventory release
  - else → Cancelled + release inventory + release coupon + OrderCancelledEvent
```

## Implementation Discovery

Frontend — out of scope (UI only). Operational surface in POS Admin = `/api/admin/orders`.

Controllers / Endpoints:
- `OrdersController` (`/api/orders`): list (combined standard + marketplace), get by id /
  number, manual `CreateOrder` (rejects Ecommerce/Marketplace), plus admin fulfillment/
  payment mutation endpoints. Uses `Idempotency-Key` header; 5-min stale-claim window.
- `AdminOrdersController` (`/api/admin/orders`): `GET` list, `GET {id}`, `PUT {id}/status`
  (requires `If-Match` RowVersion), `POST {id}/cancel`. Strict tenant isolation via
  `ITenantContext.RequiredTenantId`; PostgreSQL only, no Mongo.
- `StorefrontOrdersReadController`: storefront Mongo reads.
- `CommerceHubOrdersController` / `CommerceHubOrderManagementService`: marketplace order
  reads/management; access gated by HMAC-signed `CommerceHubOrderAccessService` tokens.
- `OrderFulfillmentController`: fulfillment transitions.

Application services:
- `CheckoutService` — checkout authority: variant-based pricing/stock, reservation, event
  emission (≈8800 lines; the storefront order creation authority).
- `AdminOrderService` — admin read + status override + cancellation with all side-effect
  orchestration (inventory release, coupon release, payment-cancellation outbox).
- `OrderStatusSyncService` — derives `OrderStatus` from Shipment and Payment truth (SQL
  authoritative), enforces financial gating before outbound promotion.
- `CommerceHubOrderManagementService`, `CommerceHubCustomerOrderService` — CommerceHub.
- `StorefrontOrderProjectionReconciliationService` (background),
  `StorefrontOrderProjectionRepairService`, `CommerceHubOrderProjectionRepairService` —
  projection repair/reconciliation against SQL truth.

Domain services:
- `IOrderFulfillmentService` (`OrderFulfillmentService`) — fulfillment state machine,
  ownership enforcement, delivery→payment trigger.
- `IPaymentLifecycleService` (`PaymentLifecycleService`) — payment money-recognition machine,
  journals, settlement, refunds, checkout-state transitions.
- `OrderOperationalFinancialAuthority` — shared gating rules for outbound progress.
- `OrderPaymentLineageAuthority` — SQL payment truth loader for projection replays.
- `OrderFulfillmentLineageAuthority` — SQL fulfillment truth loader.

Repositories:
- `IProductVariantStockRepository` — `ReleaseOrderReservationsAsync`, reservation writes.
- `IOrderReadRepository` (Mongo), `CommerceHubOrderReadRepository`,
  `MarketplaceOrderReadRepository` — read models only.

Entities (SQL):
- `Order`, `OrderItem`, `OrderRefund`, `OrderRefundLine`, `OrderRequestIdempotency`,
  `CommerceHubMarketplaceOrder`, `CommerceHubSubOrder`, `PaymentIntent`, `Shipment`,
  `CouponReservation`, `PaymentCancellationOutboxMessage`, `AffiliateAttribution`.

Database tables (PostgreSQL authoritative):
- `Orders`, `OrderItems`, `OrderRefunds`, `OrderRefundLines`, `OrderRequestIdempotencies`,
  `CommerceHubMarketplaceOrders`, `CommerceHubSubOrders`, `PaymentIntents`, `Shipments`,
  `CouponReservations`, `PaymentCancellationOutboxMessages`, `OutboxMessages` (MassTransit EF
  outbox), `ProductVariantStocks`, `AffiliateAttributions`.

Events (MassTransit integration events, `IAuthoritativeIntegrationEvent`):
- `OrderCreatedEvent`, `OrderStatusChangedEvent`, `OrderPaymentStatusChangedEvent`,
  `OrderShippedEvent`, `OrderDeliveredEvent`, `OrderCancelledEvent`,
  `FulfillmentConfirmed/Packed/OutForDelivery/Delivered/Completed/Returned/DeliveryFailedEvent`,
  `CheckoutOrderCreatedEvent`, marketplace `OrderCreatedEvent`, `AffiliateAttributedEvent`,
  `WhatsAppOrderConfirmationRequestedEvent`, `InventoryReservedEvent`.

Background jobs / consumers:
- `OrderProjectionConsumer` (SQL→Mongo order projection), `OrderStatusSyncConsumer`,
  `OrderDeliveredEventConsumer`, `OrderShipmentOrchestrationConsumer`,
  `CheckoutSessionOrderConversionConsumer`, `AffiliateOrderAttributionConsumer`,
  `WhatsAppOrderConfirmationConsumer`, `StorefrontOrderProjectionReconciliationService`,
  `AbandonedCheckoutScannerJob`.
- Outbox drain: MassTransit EF outbox publishes `OutboxMessages` → RabbitMQ.

Reservation integration: `ProductVariantStocks` reservation within checkout tx; release via
lifecycle outbox. Net-sum release avoided (per-reservation filtering).

Inventory integration: SQL `ProductVariantStocks` is the stock + reservation authority. Checkout
re-validates stock from SQL, never from projections.

Pricing integration: Checkout re-derives totals from SQL `ProductVariant.OnlinePrice ?? Price`;
the accepted totals are persisted as a snapshot.

Payment integration: `IPaymentLifecycleService` owns `PaymentStatus` and `CheckoutState`; emits
the canonical paid-state transitions that drive `SyncFromPaymentAsync`.

Shipping integration: `Shipment` aggregate + carrier webhooks drive `ShipmentStatus`;
`OrderStatusSyncService` maps it onto `OrderStatus` under financial gating.

Affiliate integration: server-side attribution stamping at checkout + `AffiliateAttribution`
row; `AffiliateOrderAttributionConsumer` + projection events update dashboards.

CommerceHub integration: `CommerceHubSubOrder` is the order-of-record for marketplace; POS
Admin lists/reads it; commission snapshot persisted; access token–gated.

Mongo: `orders` collection (storefront), `commerceHubOrders`, marketplace read models —
all derived, idempotent, version-freshness-checked, SQL-authority-collapsed.

Redis: checkout cart snapshot, distributed checkout lock, idempotency, storefront order cache
(`storefront:tenant:{tid}:order:{oid}`, 10-min TTL) invalidated by the projection consumer.

Elasticsearch: **not** used for orders in this codebase (no order index located). Storefront
order history is Mongo-keyed.

MassTransit: EF outbox (`OutboxMessages`) makes order + event + idempotency a single commit
boundary; RabbitMQ transports events to consumers.

## Business Rules

Order creation rules
- Ecommerce/Marketplace orders MUST be created via checkout; admin `CreateOrder` rejects those
  sources (HTTP 400).
- Variant-based: a `Product` is never sold; the `ProductVariant` (SKU) is. Pricing/stock from
  variant only.
- Order items are immutable SKU snapshots (price, cost, attributes, SKU, image) captured at
  commit.

Duplicate protection / Idempotency
- `OrderRequestIdempotency` keyed by `Idempotency-Key` header + request hash; the same key
  short-circuits to the original response. Checkout uses
  `CheckoutRequestIdempotencyCoordinator` + `CartId` as the durable duplicate guard
  (`FindActiveOrderForCartAsync`). A DB unique violation on the active cart order is recovered
  by returning the existing order (no double reservation).

Order numbering
- Sequence-based on Postgres; date-prefix + locked max-suffix on SQL Server. Deterministic,
  tenant-scoped, never client-supplied.

Status transitions
- `OrderStatus` may only be changed inside an authorized context
  (`BeginAuthorizedContext`); otherwise the `OrderStatusSaveChangesInterceptor` throws
  (fail-closed).
- Admin direct status update is restricted: lifecycle statuses (Cancelled, Shipped, Delivered,
  Completed, Refunded) are **prohibited** on the generic `PUT /status` endpoint — dedicated
  lifecycle endpoints must be used.
- Delivered/Completed orders are immutable to admin status mutation.
- `IsValidStatusTransition`: forward-only Pending→Confirmed→Processing→Shipped→Delivered→
  Completed; OnHold reachable from any active state and from OnHold; Cancelled reachable from
  any active/OnHold state; Cancelled/Refunded are terminal.

Cancellation rules
- Reason required. Delivered/Completed immutable. Money-recognized orders require refund
  lifecycle first (refusal, not silent cancel). Pending-payment orders → OnHold + deferred
  inventory release + payment-cancellation outbox. Others → Cancelled + immediate inventory +
  coupon release + event.

Reservation rules
- Reserve inside the checkout transaction; persist reservation id + expiry in the outbox
  atomically with the order. Release per-reservation filtering to `RemainingReservedQuantity>0`.

Release rules
- On non-pending cancellation, payment failure, or expiry via the lifecycle outbox. Deferred
  while a payment-cancellation is pending.

Completion rules
- `FulfillmentStatus.Completed` or `OrderStatus.Completed` is terminal and immutable to admin
  status mutation.

Shipment rules
- Shipment status is the physical authority; order status is derived from it by
  `OrderStatusSyncService`. Failed shipment does **not** auto-cancel the order (admin
  intervention). Returned shipment → `OrderStatus.Refunded`.

Payment rules
- `PaymentStatus` only via `IPaymentLifecycleService`. Money-recognition states
  (Collected/Settled/Refunded/PartiallyRefunded) create journal entries. COD/Expired/
  AmountMismatch block fulfillment.
- Outbound order/fulfillment progression gated by `OrderOperationalFinancialAuthority`:
  non-COD positive-total orders need a paid `PaymentStatus` and (if applicable) posted
  collection accounting before advancing.

Snapshot rules
- Pricing, customer, and geographic snapshots immutable after creation. `ProjectionMutationVersion`
  on `Order`/`CommerceHubSubOrder` is strictly monotonic (regression throws). Mongo projection
  uses `LastEventVersion` freshness to reject stale replays.

Visibility rules
- Tenant isolation is mandatory. For standard orders, `AdminOrderService` proves ownership via
  Customer→Tenant, Shipment→Tenant, or PaymentIntent→Tenant joins; unprovable ownership =
  ORDER_NOT_FOUND.
- Mongo projection rejects `Guid.Empty` tenant and non-positive `EventVersion`.

Projection rules
- Idempotent inserts; facet events use `$set` + `$push Timeline` with `Max` version guards.
- Replayed payment facet events are collapsed against SQL `OrderPaymentLineageAuthority`; if no
  SQL order exists, the stale Mongo payment projection is deleted.

Fail-closed behavior
- `OrderStatusSaveChangesInterceptor` enforcement is **enabled by default** (fail-closed).
- `CommerceHubOrderAccessService` fails fast at startup if its signing key is missing (no
  fallback).
- Cross-tenant access blocked; `X-Tenant-Id` stripped; `ITenantContext.RequiredTenantId`.
- Lifecycle endpoints preferred over generic status flips; invalid transitions rejected rather
  than auto-fixed.

## Status Transitions

Operational `OrderStatus` machine (from `AdminOrderService.IsValidStatusTransition` +
`OrderStatusSyncService` mapping):

```
Pending
  → Confirmed        (SyncFromPaymentAsync once payment is a paid state)
  → Processing       (SyncFromShipmentAsync: shipment Pending/PickedUp/InTransit/ReadyForPickup)
  → Shipped          (SyncFromShipmentAsync: PickedUp/InTransit/OutForDelivery)
  → Delivered        (SyncFromShipmentAsync: Delivered)
  → Completed        (manual fulfillment complete / admin)
  → Cancelled        (explicit admin cancel; also shipment Cancelled)
  → OnHold           (admin; also pending-payment cancellation)
  → Refunded         (shipment Returned / return+refund flow)

OnHold → any forward state or back (admin)
Cancelled → (terminal)
Refunded  → (terminal)
Completed → (terminal)
Delivered → Completed / Refunded only
```

`CheckoutState` machine (from `Order.TransitionCheckoutState`):
```
Draft → PendingPaymentIntent
PendingPaymentIntent → PaymentPending | Failed | Cancelled | Expired
PaymentPending → Paid | Failed | Cancelled | Expired
Failed → PendingPaymentIntent | Cancelled
Cancelled → (terminal)
Expired → PendingPaymentIntent
Paid → (terminal)
```

`PaymentStatus` machine (from enum doc + `PaymentLifecycleService`):
```
Pending → Authorized | CashOnDelivery | Collected | Failed | Expired | AmountMismatch
Authorized → Collected | Failed
CashOnDelivery → Collected | Failed
Collected → Settled | Refunded | PartiallyRefunded
Settled → (terminal, unless post-settlement refund)
Refunded / PartiallyRefunded → (terminal)
AwaitingVerification → Collected | Rejected
Any → Failed
```

`FulfillmentStatus` machine (from `FulfillmentStatusExtensions.GetAllowedTransitions`):
```
Created → Confirmed | Cancelled
Confirmed → Packed | Cancelled
Packed → OutForDelivery | Cancelled
OutForDelivery → Delivered | FailedDelivery
Delivered → Completed | Returned
FailedDelivery → OutForDelivery | Cancelled
Completed / Returned / Cancelled → (terminal)
```
Delivered triggers payment lifecycle (COD → collected; wallet → verify/approve; online → no-op).

## Cross-System Effects

- Products / Catalog: read-only at order time (variant prices/cost snapshotted). Later catalog
  changes do NOT alter an existing order.
- Inventory: checkout reserves against `ProductVariantStocks`; cancel/fail/expiry releases.
  Order never owns stock quantities.
- Pricing: snapshot persisted; downstream never recomputes the order total.
- Payments: `IPaymentLifecycleService` recognizes money; order carries `PaymentStatus`,
  `CheckoutState`, `FinancialPostingStatus`, and per-item `PaidAmount`/`RefundedAmount`.
- Checkout: order is the durable outcome of checkout; checkout idempotency/cart guard prevents
  duplicates.
- Customers: order references POS Customer, Storefront OnlineCustomer, or guest snapshot; does
  not mutate the customer record.
- Storefront: Mongo `OrderDocument` is the customer-visible projection of the SQL order.
- CommerceHub: `CommerceHubSubOrder` is the marketplace order-of-record; commission snapshot +
  store binding persisted; access token–gated.
- Affiliate: attribution stamped at checkout; commission snapshot on items; dashboards updated
  via events.
- Shipping: `Shipment` status drives derived `OrderStatus` via `OrderStatusSyncService`.
- Search: admin searches SQL; storefront history reads Mongo. No ES order index.
- Analytics: only `SessionId` reference stored on the order.
- Accounting: journals created by Payment Lifecycle on money recognition; order only holds
  `FinancialPostingStatus` flags + journal id.
- Notifications: WhatsApp confirmation emitted on creation.

What does NOT change:
- Order totals after creation (no repricing).
- Customer/Inventory/Pricing source records due to an order.
- Mongo/Redis being authoritative (they are derivatives; SQL wins on conflict).

## Validation Checklist

After creating an order (storefront):
- SQL `Order` row exists (Status=Pending, PaymentStatus=Pending, CheckoutState=
  PendingPaymentIntent, FulfillmentStatus=Created), with immutable item/price/geo snapshots.
- Inventory reserved (ProductVariantStocks reservation + InventoryReservedEvent outbox).
- Coupon/offer reserved if applied.
- `OrderCreatedEvent` in outbox → Mongo `OrderDocument` created; Redis cache invalidated.
- AffiliateAttribution stamped if affiliate; WhatsApp confirmation requested.

After payment succeeds:
- `PaymentStatus` advanced by `IPaymentLifecycleService` (Collected/Authorized/COD).
- `OrderStatusSyncService.SyncFromPaymentAsync` moves Pending→Confirmed.
- Mongo payment status updated (collapsed to SQL truth).
- Per-item `PaidAmount` updated; `IsFullyPaidComputed` reflects reality.

After payment fails:
- `PaymentStatus.Failed`; `CheckoutState.Failed`; inventory released via outbox; order stays
  non-cancelled (admin may cancel).

After cancellation:
- If money recognized → refused (refund first).
- If pending payment → OnHold + PaymentCancellationOutboxMessage; inventory release deferred.
- Else → Cancelled; inventory + coupon released; `OrderCancelledEvent` emitted; Mongo updated.

After shipment:
- `Shipment` status updated; `OrderStatusSyncService` derives OrderStatus (Shipped/Delivered/
  Refunded); Mongo updated; financial gating enforced before outbound promotion.

After completion:
- `FulfillmentStatus.Completed` / `OrderStatus.Completed`; terminal; admin status mutation
  blocked.

After inventory release:
- `ProductVariantStocks` reservation cleared (per-reservation); Mongo unaffected (no stock in
  read model).

## Common Failure Scenarios

Order exists in SQL but not Storefront
- Cause: `OrderProjectionConsumer` did not process `OrderCreatedEvent`, or Mongo write failed,
  or Redis cache stale.
- Investigate: outbox/RabbitMQ consumer health; Mongo `orders` collection; Redis cache
  invalidation; `StorefrontOrderProjectionReconciliationService` / repair service.

Order missing from POS Admin
- Cause: ownership join not satisfied (no Customer/Shipment/PaymentIntent tenant linkage), or
  IsDeleted, or wrong tenant header. Marketplace orders live in `CommerceHubSubOrders`, not
  `Orders`.
- Investigate: `AdminOrderService.VerifyOrderOwnershipAsync`; confirm Customer/Shipment/
  PaymentIntent tenant; for marketplace, query `CommerceHubSubOrders` by StoreId.

Inventory reserved forever
- Cause: reservation released only on cancel/fail/expiry outbox; if the outbox/lifecycle worker
  died, reservation lingers.
- Investigate: `PaymentCancellationOutboxMessages`; lifecycle outbox drain; reservation
  expiry; `ReleaseOrderReservationsAsync` path.

Payment succeeded but order not updated
- Cause: `SyncFromPaymentAsync` skipped (order not Pending) or `OrderPaymentStatusChangedEvent`
  not projected.
- Investigate: `PaymentStatus` in SQL (authority); `OrderPaymentLineageAuthority`; Mongo
  collapse logic; outbox.

Duplicate order
- Cause: missing/!idempotency header, or cart guard bypassed.
- Investigate: `OrderRequestIdempotency`; `FindActiveOrderForCartAsync`; checkout lock.

Wrong order total
- Cause: projection recomputed totals (forbidden) or pricing snapshot not used.
- Investigate: `PricingSnapshotJson` + SQL totals (authority); Mongo should echo, not recompute.

Projection stale
- Cause: replayed event with older `LastEventVersion`, or consumer skipped.
- Investigate: `LastEventVersion` monotonicity; `OrderProjectionConsumer` freshness filter;
  repair service.

Shipment mismatch
- Cause: `OrderStatusSyncService` maps shipment→order; a failed shipment intentionally does not
  cancel the order.
- Investigate: `MapShipmentStatusToOrderStatus`; `ShouldUpdateOrderStatus` (Failed = no change);
  financial gating logs (`GetOutboundProgressBlockReason`).

Notification missing
- Cause: `WhatsAppOrderConfirmationRequestedEvent` not published/consumed.
- Investigate: WhatsApp consumer; event published at creation.

CommerceHub mismatch
- Cause: sub-order projection out of sync with `CommerceHubSubOrder` SQL.
- Investigate: `CommerceHubOrderProjectionRepairService`; sub-order SQL truth; access token.

Affiliate attribution missing
- Cause: client payload trusted (it must not be) or `AffiliateModeResolver` did not stamp.
- Investigate: `Order.AffiliateAccountId`/`ReferralCode`/`PromotionLinkId`; `AffiliateAttribution`
  join in admin evidence.

## Debugging Guide

Start investigation in this order:

1. SQL first — `Orders` (and `CommerceHubSubOrders` for marketplace) is the system of record.
   Confirm the order exists, its TenantId, Status, PaymentStatus, CheckoutState,
   FulfillmentStatus, FulfillmentMode, totals, and snapshots.
2. Inventory — check `ProductVariantStocks` reservation and whether a release outbox fired.
3. Pricing Snapshot — compare `PricingSnapshotJson`/SQL totals vs any downstream display; SQL
   wins.
4. Payment — `IPaymentLifecycleService` is the only writer of `PaymentStatus`; check
   `PaymentIntents` and `OrderPaymentLineageAuthority`.
5. Shipment — `Shipment` status is the physical authority; `OrderStatusSyncService` derives
   order status; check financial gating.
6. Outbox — MassTransit `OutboxMessages` + RabbitMQ; confirm `OrderCreatedEvent`/facet events
   were published and consumed.
7. Projection — Mongo `orders`/`commerceHubOrders`; verify version freshness and SQL-collapse
   behavior; use repair/reconciliation services if stale.
8. Storefront — only after SQL+Mongo confirmed; check Redis cache (10-min TTL) invalidation.
9. CommerceHub — `CommerceHubSubOrder` SQL + token-gated access; marketplace orders are NOT in
   `Orders`.

## Related Knowledge

Core Guides
- Tenant isolation & `ITenantContext.RequiredTenantId` (authority boundary: tenant is
  necessary-not-sufficient; X-Tenant-Id stripped).
- Lifecycle ownership convention: Payment→`IPaymentLifecycleService`, Order→order lifecycle,
  Fulfillment→`IOrderFulfillmentService`; direct writes violate unless reviewed; events via
  `IEventPublisher`; fail closed.

System Guides
- Checkout capability (variant-based reservation, idempotency).
- Payment Lifecycle capability (money recognition, settlement, refunds, journals).
- Fulfillment / Shipping capability (carrier webhooks, shipment status).
- Inventory capability (ProductVariantStocks reservation/release).
- CommerceHub / Marketplace capability (sub-orders, commission, access tokens).
- Affiliate capability (attribution stamping, commission).
- Storefront projection / MongoDB read models.

Related Capabilities
- Checkout, Payments, Fulfillment, Shipping, Inventory, CommerceHub, Affiliate, Storefront
  Read Models, Notifications (WhatsApp), Analytics (SessionId reference).

Related Domains
- `PointOfSale.Core.Entities` (Order, OrderItem, OrderRefund, CommerceHubSubOrder,
  PaymentIntent, Shipment, AffiliateAttribution).
- `PointOfSale.Core.Payments` (OrderOperationalFinancialAuthority, PaymentStatusAuthority).
- `PointOfSale.Infrastructure.Fulfillment`, `PointOfSale.Infrastructure.Payments`,
  `PointOfSale.Infrastructure.Reliability` (lineage authorities).
- `PointOfSale.Infrastructure.MongoDB.Projection`, `PointOfSale.Infrastructure.Consumers`.

## Documentation Validation

Implementation-backed findings only (do not rewrite docs; report discrepancies):

1. MISSING/INCORRECT AUTHORITY BOUNDARY — `AdminOrderService` class header comment states
   "The Order entity does NOT have TenantId column. Tenant isolation MUST be enforced via
   ownership joins." This is **wrong**: `Order` implements `ITenantOwned` and has a non-null
   `TenantId` column (enforced at DB level, per `Order.cs`). POS Admin code still uses
   ownership joins as defense-in-depth, but the stated premise is false and misleading.

2. HIDDEN IMPLEMENTATION BEHAVIOR — Cancellation of an order with a *pending* PaymentIntent does
   NOT immediately cancel; it moves to **OnHold**, enqueues a `PaymentCancellationOutboxMessage`,
   and **defers inventory release** until payment cancellation completes. A naive reader of
   "cancel releases inventory" would be wrong about timing.

3. HIDDEN IMPLEMENTATION BEHAVIOR — A *failed shipment* does **not** cancel or change the order
   status (admin intervention required). The shipment→order mapping explicitly leaves the order
   in its current state on `ShipmentStatus.Failed`.

4. HIDDEN IMPLEMENTATION BEHAVIOR — `Order.Status` direct assignment is blocked at runtime by
   `OrderStatusSaveChangesInterceptor` (fail-closed, enabled by default). Generic admin
   `PUT /status` rejects lifecycle statuses (Cancelled/Shipped/Delivered/Completed/Refunded);
   dedicated endpoints are required. This is stronger than "admin can update status."

5. INCORRECT BUSINESS RULE ASSUMPTION — Payment/fulfillment outbound progression is gated by
   `OrderOperationalFinancialAuthority`: non-COD, positive-total orders cannot advance outbound
   until payment is a paid state AND (if required) the collection accounting journal is posted.
   Documentation that assumes fulfillment can proceed independently of payment is wrong.

6. MISSING LIFECYCLE STAGE — `CheckoutState` (Draft/PendingPaymentIntent/PaymentPending/Paid/
   Failed/Cancelled/Expired) is a distinct, implementation-backed machine governing placement
   and payment progression, separate from `OrderStatus`. Often omitted from order lifecycle
   descriptions.

7. MISSING AUTHORITY BOUNDARY — `FulfillmentMode` (Manual/Carrier) is snapshotted at creation
   and immutable; it (not ShippingMethod) decides who may drive fulfillment. Carrier orders
   reject manual admin transitions (409); Manual orders reject carrier webhooks. This ownership
   split is a core authority boundary not always documented.

8. HIDDEN IMPLEMENTATION BEHAVIOR — Mongo payment projection replays are **collapsed against SQL
   `OrderPaymentLineageAuthority`**; a replayed `OrderPaymentStatusChangedEvent` that disagrees
   with SQL is ignored for the timeline, and if no SQL order exists the Mongo payment projection
   is deleted. Projection is never allowed to lead SQL.

9. MISLEADING NAMING — `Order.PaymentStatus` enum marks `Paid` (=2) and `PartiallyPaid` (=3) as
   `[Obsolete]`; canonical recognition is `Collected`/`Settled`/`Refunded`/`PartiallyRefunded`.
   Code referencing `Paid` should be treated as `Collected`.

10. MISSING SCOPE NOTE — Elasticsearch is **not** used for order search in this codebase; admin
    search is SQL, storefront history is Mongo. Any doc implying an ES order index is incorrect.

---

### Summary of discoveries
Order Management is the PostgreSQL-authoritative order capability spanning Storefront, POS, and
CommerceHub. Orders are created only through the checkout/factory pipeline (admin manual create
rejects Ecommerce/Marketplace). Each order carries four cooperating state machines
(OrderStatus, CheckoutState, PaymentStatus, FulfillmentStatus) plus immutable SKU, pricing,
customer, and geographic snapshots. Status writes are fail-closed (interceptor-gated). SQL is
the only system of record; Mongo/Redis are derivatives enforced to never lead SQL via version
freshness + SQL-lineage collapse.

### Business rules discovered
Variant-based selling (Product never sold; ProductVariant is); idempotency + cart duplicate
guard; SQL sequence order numbering; interceptor-gated status writes; admin lifecycle-status
prohibition on generic endpoint; Delivered/Completed immutability; cancellation requires reason,
refuses money-recognized orders, defers inventory release for pending-payment orders (OnHold +
outbox); per-reservation inventory release; payment/fulfillment outbound gating by
`OrderOperationalFinancialAuthority`; immutable FulfillmentMode ownership; projection collapse to
SQL truth.

### Order lifecycle discovered
Cart → CheckoutService (lock, idempotency, SQL price/stock re-validation, inventory
reservation, affiliate stamp, coupon/offer reserve) → Order aggregate in SQL → EF outbox
OrderCreatedEvent → Mongo projection + WhatsApp confirmation → Payment Lifecycle (PaymentStatus)
→ SyncFromPayment (Pending→Confirmed) → Fulfillment (Created→…→Completed) / Carrier webhooks →
Shipment status → SyncFromShipment derives OrderStatus (Shipped/Delivered/Refunded) → Mongo
updated → customer sees order. Cancellation is a guarded side-effect orchestration, not a flip.

### Cross-system dependencies discovered
Inventory (ProductVariantStocks reserve/release), Pricing (SQL variant snapshot), Payments
(IPaymentLifecycleService owns PaymentStatus + journals), Shipping/Shipment (physical authority
for order status derivation), CommerceHub (sub-order order-of-record + commission), Affiliate
(server-side attribution stamp), Storefront (Mongo projection), Notifications (WhatsApp),
Analytics (SessionId reference only), Accounting (journals via Payment Lifecycle). Elasticsearch
NOT used for orders.

### Documentation inconsistencies found
(1) AdminOrderService comment falsely claims Order has no TenantId column. (2) "Cancel releases
inventory" hides the OnHold + deferred-release behavior for pending-payment orders. (3) Failed
shipment auto-cancel assumption is wrong (no change). (4) Admin status-update capability is
weaker than implied (interceptor + lifecycle-endpoint prohibition). (5) Outbound fulfillment
independence from payment is wrong (financial gating). (6) CheckoutState lifecycle stage often
omitted. (7) FulfillmentMode ownership boundary under-documented. (8) Mongo payment-projection
collapse-to-SQL behavior hidden. (9) Obsolete Paid/PartiallyPaid enum naming. (10) ES order
index assumption incorrect.

### Hidden implementation behavior found
OnHold + deferred inventory release on pending-payment cancellation; failed shipment no-op on
order status; fail-closed OrderStatus interceptor; projection replay collapse to SQL
OrderPaymentLineageAuthority with deletion of orphan Mongo payment projections; per-reservation
(not net-sum) inventory release; CommerceHub access token fail-fast startup; checkout serializable
retry + DB-unique cart guard recovering existing order without double reservation.

### Confirmation that only this file was created
Only `.ai/hermes/capabilities/pos-admin/order-management.md` was created. No other file was
created, edited, or deleted.

### Confirmation that no other files were modified
No source, config, test, or documentation files were modified. The investigation was
read-only except for the single new capability file above.

### Confirmation that no memory writes occurred
No memory writes were performed; this task was scoped strictly to capability documentation.
