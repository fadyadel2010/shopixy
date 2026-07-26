# Shopixy AI QA Onboarding — Orders

Audience: a Senior QA Engineer (human or AI) joining the Shopixy / PointOfSale team and
owning order correctness across every channel. This guide distills the durable order business
rules from `.ai-context/*` and `docs/architecture/*`. It is an onboarding/training artifact, not
a summary of the code. When in doubt, the `.ai-context` authority files are the truth and the
certification findings under `docs/architecture/*` are the evidence; the README is STALE and must
not be trusted for current behavior.

Authority precedence used while writing this guide (and the rule you must use when the docs
disagree): `.ai-context/*` (repository-wide SQL/tenant/lifecycle truth) > order/checkout/finance
certification findings under `docs/architecture/*` > code comments. Where two documents disagree,
the conflict is called out explicitly under "Conflicts" inside the relevant section.

Scope note: This guide covers order authority, lifecycle, checkout, inventory integration, payment
integration, fulfillment, cancellation, returns, refund integration, projection, event publishing,
and recovery for the Storefront (POS/Admin) order, the CommerceHub multi-vendor marketplace order,
and the Affiliate-origin order. POS Admin is one of the tenant back-office surfaces that mutate the
generic `Order`; CommerceHub is the multi-vendor marketplace with its own `CommerceHubMarketplaceOrder`
+ `CommerceHubSubOrder` model. They are DIFFERENT authority models and must not be conflated (see
"Never Assume").


# Purpose

Establish a single QA source of truth for how orders must behave so order correctness can never be
silently corrupted by: stale projections, currency confusion, provider callbacks, frontend state,
duplicate delivery, out-of-order events, cross-tenant leakage, replay attacks, mistaken lifecycle
transitions, or premature certainty about external money or fulfillment.

QA's job for orders is not to verify that a happy path "works." It is to prove the system **fails
closed** when order truth is uncertain, ambiguous, mismatched, or late — and that no replay,
duplicate, projection, frontend state, or cross-channel spoof can ever create, resurrect,
double-count, misscope, or mislineage an order or its money/inventory effects.


# Business Goals

- PostgreSQL is the single authoritative write model for orders, sub-orders, order items, payment
  state, fulfillment state, reservations, refunds, returns, and accounting truth.
- Every order mutation crosses an authorized lifecycle boundary (`Order` lifecycle boundary /
  `IOrderFulfillmentService` / `IPaymentLifecycleService` / refund + return services) and the SQL
  commit boundary.
- Channel identity (Storefront vs Affiliate vs Marketplace) is fixed at creation and never changes;
  metadata never leaks across channels.
- Inventory, money, and order state stay mutually consistent: a reservation is claimed before commit,
  released only on safe cancellation, and consumed only through current lineage.
- External uncertainty (payment, fulfillment, shipment, refund) is modeled explicitly and never guessed
  into a false terminal state.
- Integrity is preferred over availability when contextual backlog or external-money truth is uncertain.
- Idempotency, duplicate collapse, and reconciliation keep the system safe under replay, retries, and chaos.
- No cross-tenant leakage: tenant scope is derived from trusted backend resolution, never from client/
  provider claims.
- Currency is sovereign per surface: CommerceHub marketplace orders are EGP-only; storefront/tenant
  currency is a separate, non-overriding surface.


# Order Authority

The organizing principle: PostgreSQL is the authoritative write model for every order concept.
MongoDB, Redis, Elasticsearch, RabbitMQ, and every projection/event payload are DERIVATIVE — they
lag, they are caches/evidence, they are NOT truth.

Per-channel authority ownership:

- Storefront (POS/Admin) generic `Order`: tenant-owned PostgreSQL `Orders`, `OrderItems`, checkout
  idempotency records, payment lifecycle rows, refund rows, inventory reservations, stock movements,
  accounting outbox rows, and current SQL state are authoritative. The order owns order identity and
  lifecycle coordination; it does NOT own inventory, treasury, customer balance, POS sales invoice, or
  financial reporting truth. Those remain owned by their own certified boundaries.
- CommerceHub marketplace order: PostgreSQL `CommerceHubMarketplaceOrders`, `CommerceHubSubOrders`, and
  `CommerceHubSubOrderItems` own marketplace order identity and lineage. `CustomerId` owns customer
  visibility; `StoreId` owns merchant/POS fulfillment authority. Multi-vendor marketplace status is
  aggregate state over SQL sub-orders. Frontend state, Mongo order docs, Redis, Elasticsearch, and
  events are derivative evidence only.
- Affiliate-origin order: a generic `Order` with `Source = OrderSource.Ecommerce` plus a canonical
  `AffiliateAttributions` row stamped in the same checkout transaction. Affiliate identity/attribution
  is backend-owned and immutable after conversion; it cannot be fabricated or changed by presentation.

Authority boundaries (do not bypass):

- `Order.Status` changes happen ONLY inside an authorized order lifecycle boundary. Legacy direct
  order-status and payment-status patches are RETIRED (`410 Gone`).
- `PaymentStatus` changes happen ONLY through `IPaymentLifecycleService`.
- `FulfillmentStatus` changes happen ONLY through `IOrderFulfillmentService` (generic `Order`) or
  `CommerceHubOrderManagementService.UpdateSubOrderStatusAsync` (marketplace sub-order).
- Storefront order/sub-order lifecycle field writes are blocked by `ApplicationDbContext` unless an
  explicit `CommerceHubBoundaryMutationScope.BeginOrderLifecycle()` / `BeginShippingAssignment()` is
  active. Direct EF status writes fail closed.
- Authoritative integration events go through `IEventPublisher`, never raw MassTransit publish.
- Super Admin has NO marketplace order-state override endpoint. Super Admin may operate audited
  return/refund workflows and restricted return-status overrides, but cannot change order/sub-order
  status. Confirmed inventory-bearing returns cannot be overridden.

Conflicts / boundary notes:
- The marketplace model (`CommerceHubMarketplaceOrder`/`CommerceHubSubOrder`) and the generic `Order`
  model are intentionally separate. Do not assume a generic `Order` lifecycle rule applies to a
  marketplace sub-order or vice versa; the allowed transition graphs differ (see Order Lifecycle).
- The frozen Payment Runtime (see `.ai/hermes/04-payments.md`) is certified but NOT live for order
  payment paths. Live order payment transitions route through the SQL-backed `IPaymentLifecycleService`.
- Storefront/CommerceHub frontend "lifecycle authority" objects (e.g. `orderLifecycleAuthority`,
  `orderCreationAuthority`, `marketplaceOrderTransitionAuthority`) are PRESENTATION validators only.
  They reject malformed/contradictory/stale DTOs; they never create or repair backend authority. A
  frontend that "renders success" is not proof an order exists — only an accepted backend-created
  order DTO with valid identity, canonical statuses, finite totals, and item evidence is.


# Order Lifecycle

Two distinct lifecycle vocabularies exist. Treat them as separate models.

## Storefront / POS generic Order

`Order.Status` is mutated only inside the authorized order lifecycle boundary. Outbound fulfillment is
blocked when the order lifecycle is `Cancelled`, `Refunded`, or `Completed`. Fulfillment progression
cannot regress order state: cancelled or refunded orders cannot resurrect into outbound fulfillment.

Forbidden transitions at the generic `Order` boundary (legal paths only through the dedicated lifecycle
service, not the Web status control):
- Direct `Cancelled`, `Shipped`, `Delivered`, `Completed`, `Refunded`.
- Direct operational `Confirmed` / `Processing`.
- Reopening `Cancelled` / `Refunded`.
- Mutating terminal `Delivered` / `Completed`.
- Generic admin `PUT /api/admin/orders/{id}/status` legal path = same-status no-op and `OnHold` where
  backend validation allows it. Illegal paths = the direct transitions above. Operational progression
  stays owned by the dedicated payment/fulfillment boundaries. RowVersion `If-Match` is required and a
  stale replay is rejected with `409`.

## CommerceHub marketplace sub-order

SQL marketplace statuses: `Pending, Confirmed, Processing, Shipped, Delivered, Completed, Cancelled,
Refunded, OnHold`.

Allowed vendor sub-order transitions (POS Admin fulfillment):
- `Pending -> Processing`, `Pending -> Cancelled`
- `Confirmed -> Processing`, `Confirmed -> Cancelled`
- `Processing -> Shipped`, `Processing -> Cancelled`
- `Shipped -> Delivered`, `Shipped -> Cancelled`
- `OnHold -> Cancelled`
- Same-status replay is a no-op.

Forbidden transitions: `Pending -> Delivered`, `Cancelled -> Shipped`, `Returned/refunded -> Processing`,
`Delivered -> Confirmed`, `Refunded -> Delivered`, `Completed -> Processing`, and ANY transition out of
`Cancelled`, `Delivered`, `Completed`, or `Refunded` through POS Admin fulfillment.

`Delivered`, `Completed`, and `Refunded` are terminal for POS Admin sub-order mutation. Cancellation and
refund terminal states expose no forward transitions.

Adjacent lifecycle vocabularies (do not mix):
- POS fulfillment `FulfillmentStatus`: `Created, Confirmed, Packed, OutForDelivery, Delivered, Completed,
  Returned, FailedDelivery, Cancelled`.
- CommerceHub return status: `Pending, PickupScheduled, PickedUp, InTransit, DeliveredToVendor, Confirmed,
  Failed`.
- CommerceHub refund-request status: `Requested, UnderReview, Approved, Rejected, Cancelled, Processing,
  Completed, Failed, WaitingForReturn, AwaitingPayout, Paid`.
- CommerceHub refund-execution status: `Pending, Processing, Completed, Failed, ReconciliationRequired`.

Split-order presentation rule: child sub-order lifecycle is validated against parent lifecycle before
presentation. A parent terminal cancellation/failure cannot render active fulfilled children; a parent
refunded lifecycle cannot render non-refunded/non-cancelled children as reconciled frontend truth. This is
frontend presentation only — backend remains the authority.


# Checkout Rules

Checkout owns ELIGIBILITY authority. It recomputes and revalidates everything from current backend SQL
authority; client values are stale-client assertions only.

Storefront checkout (POS/Admin storefront):
- Final storefront order creation runs inside a SERIALIZABLE SQL transaction. Before commit it re-locks
  `StoreConfigs` (must be `live`), `ShippingMethods`, `TenantPaymentMethods`, variant/product rows;
  recomputes authoritative pricing from that locked authority; re-runs stale-client pricing and
  payment-method-limit validation; re-runs coupon/promotion authority from the in-transaction snapshot;
  reloads affiliate commission policy and row-locks policy rows; and persists order + items + immutable
  snapshots from that same final authority.
- If the final transaction changes any persisted financial component (tax, discount, line total, affiliate
  commission base, winning coupon/promotion identity), the flow rebuilds `OrderItem` money snapshots,
  immutable affiliate commission snapshots, and order-level discount identity snapshots from the same final
  pricing authority before commit. Refreshing only the order header is NOT sufficient.
- Client line prices, totals, discounts, tax, shipping cost, payment fee, unit conversion, and payment
  split values do NOT become authority. Mismatches fail closed (409 / rejected).
- Promotion calculation failure fails closed: a promotion-engine error must NOT be converted into a
  successful order with discounts silently removed (would diverge Storefront pricing from POS/Admin pricing
  and downstream payment/accounting truth).
- Authenticated cart/checkout must re-check current SQL `OnlineCustomer` authority and reject inactive
  customers. A valid unexpired JWT is authentication evidence, not sufficient authorization after SQL
  customer deactivation.
- Idempotency: durable request/snapshot idempotency + serializable final authority revalidation + active-cart
  SQL uniqueness + inventory reservation + transactional order creation. The idempotency coordinator binds
  `OrderId`/`CompletedAtUtc` only AFTER proving claim-token ownership inside the current EF unit of work.
  Double-click / retry / refresh / parallel tab / replay cannot create a second order.
- Guest and authenticated checkout use durable checkout idempotency, cart execution locking, serializable
  final SQL execution, server-side price/discount/shipping/tax recomposition, and authoritative inventory
  reservation. The active-cart SQL unique index prevents concurrent duplicate active orders for the same
  tenant+cart.
- Product/quantity eligibility: current non-deleted, active, online-visible, `Published` SQL product state,
  active variant state, matching product/variant ownership, and live full category lineage. Quantities are
  positive, bounded, regrouped server-side, checked against SQL stock, and reserved through certified
  inventory authority before commit.

CommerceHub marketplace checkout:
- `CommerceHubCheckoutService` owns creation through authenticated customer authority, customer/idempotency
  locks, request-hash binding, serializable SQL, current cart/listing/product/variant/category/vendor/
  payment/shipping/promo/price/stock recomposition, reservation creation, and SQL outbox staging.
- Vendor allocation derives from current SQL listing/store ownership after checkout recomposition. Live
  sub-orders are SQL-unique per `(MarketplaceOrderId, StoreId)`. Parent reassignment, store reassignment,
  order-number mutation, and item-snapshot mutation after creation are blocked by immutable EF guards.
- Split-order shipping: checkout groups SQL-authoritative listing lines by `StoreId`, recomputes each
  store's eligible shipping options against SQL shipping-method config, and chooses one deterministic
  cheapest method per store. Marketplace shipping total is the SUM of persisted sub-order shipping; it is
  NOT an authority source. Sub-order shipping fields are IMMUTABLE after creation (behind an explicit
  shipping-assignment persistence boundary). A replayed checkout must match the customer-bound request hash
  and returns the original response only; it cannot reassign shipping. Vendor A's method/cost cannot bind
  Vendor B's sub-order.
- Multi-store checkout carries the authoritative per-store payment-method capability contract. The customer
  payment selector must consume that backend capability surface; a local hardcoded provider list is not
  authority. CommerceHub must validate the EXACT requested online provider per store set; STRIPE and PAYMOB
  are NOT interchangeable. Zero enabled `TenantPaymentMethod` rows = fail-closed "provider unavailable" — never
  a COD/any-provider fallback.
- Currency: CommerceHub owns the only accepted order currency = `EGP`
  (`FinancialCurrencyGuard.PlatformBaseCurrencyCode`). Cart, checkout, orders, sub-orders, and projections are
  EGP-normalized; non-EGP is rejected or withheld. Tenant/storefront currency cannot select, override, or
  format CommerceHub checkout currency. PostgreSQL `NOT VALID` EGP check constraints enforce new/changed rows.

Recovery of checkout:
- Checkout recovery tokens are tenant-bound, expiring, nonce-bound, and rejected after conversion. Recovery
  restores presentation state only; final checkout revalidates all current eligibility.
- Storefront recovery no longer returns stored cart snapshot/total/currency when live cart revalidation is
  unavailable; it fails closed to a cart-review path. LocalStorage/SessionStorage persist only shipping
  method id, payment method code, selected payment method, step, and address candidates — never pricing/totals.
- A `requires_initiation` or other non-terminal payment state must NOT be presented as fully completed payment
  authority. Payment-pending presentation is NOT payment success.


# Inventory Integration

Orders and inventory stay consistent through certified inventory authority. Checkout and privileged order
creation validate and reserve through `InventoryConsistencyService`. (Full inventory rules are in
`.ai/hermes/03-inventory.md`; the order-relevant invariants are summarized here.)

- Reservation is claimed against current SQL stock lineage BEFORE order commit. A reservation is a claim on
  current SQL stock lineage, not a number copied from a row.
- Payment, cancellation, expiry, and order lifecycle services own reservation conversion or release.
  Fulfillment does NOT accept client-selected inventory release behavior.
- Marketplace: active SQL reservation quantity must EXACTLY match current SQL CommerceHub order-item authority
  before marketplace payment initiation, payment recovery, reusable-session return, provider-session creation,
  provider-success recognition, or fulfillment commit. "At least enough reserved" is NOT sufficient.
- Consumable reservation requires full current lineage (variant + product + warehouse + stock + active +
  unexpired). An inactive warehouse or superseded variant is NOT reservable/consumable even if a stock row exists.
- Cancellation restores inventory reservations only AFTER payment cancellation is safe (see Cancellation Rules).
- Delivery commit for an already-authorized order item is inventory-lineage-authoritative, NOT
  publication-authoritative. Current public unpublish/offline state may block NEW reservation-backed payment
  retry, but must NOT block stock consumption when current SQL order-item and inventory lineage still prove the
  reserved stock owner.
- Return receipt replay may inspect historical committed reservations as delivered-quantity evidence, but must
  revalidate current stock ownership before adding resellable or damaged quantities. Inactive warehouse or
  superseded/detached variant lineage fails closed instead of restoring SQL stock from historical reservation
  evidence.


# Payment Integration

Payment state advances only through verified provider/payment lifecycle paths (`IPaymentLifecycleService`).
(Detailed payment rules are in `.ai/hermes/04-payments.md`; the order-relevant invariants are summarized here.)

- Online payment state advances only through verified provider/payment lifecycle paths. COD uses its explicit
  lifecycle and cannot be treated as collected online payment. Direct payment-status patching remains disabled.
- CommerceHub online payment: initiation persists a durable local payment authority row + a durable
  provider-session re-init fence before crossing the external provider boundary. If a provider returns a reusable
  external identity before the customer session is fully materialized, SQL retains that provider identity and
  retries recover against the same external identity instead of creating a replacement provider session.
- Unmatched CommerceHub provider webhooks that cannot be linked to a paid local payment row must persist
  unresolved external-money evidence into SQL (`CommerceHubUnresolvedPaymentWebhook`) before the request is
  allowed to fail retryable — unresolved cash truth must have a durable reconciliation anchor.
- A CommerceHub marketplace order with unresolved provider-success evidence, suspense-routed captured cash, or
  pending suspense-clearance reconciliation is NOT a retryable customer payment state. Customer order-detail
  surfaces and payment-initiation services must block retry until reconciliation clears the captured-money
  ambiguity. Follow backend `CanRetryPayment` / `PaymentRecoveryMessage` authority, never generic `Pending`.
- Storefront canonical payment-status surfaces must NOT report terminal `FAILED` guidance for suspense-routed,
  manual-review, amount-mismatch, or late-success quarantine states where provider cash may already exist. Those
  remain unresolved authority and stay pending/manual-review/reconciliation-visible until refund proof or local
  reconciliation establishes safe terminal failure.
- Payment owns verification before lifecycle mutation; the lifecycle service owns status transitions. Shipping
  owns delivery/collection evidence, not payment collection state. COD delivery and cash collection are separate
  facts unless a certified rule defines one admitted evidence event as both.


# Fulfillment Rules

Fulfillment status is owned by `IOrderFulfillmentService` (generic `Order`) and
`CommerceHubOrderManagementService` (marketplace sub-order). Mutations run through backend transition methods,
serializable SQL transaction boundaries, current order reload, transition validation, authorized
`FulfillmentStatusSaveChangesInterceptor` / `CommerceHubBoundaryMutationScope`, authoritative events, SQL
persistence, and accepted result DTOs. Frontend owns only requested action intent, loading/error state,
confirmation UI, refresh, and rendering.

Generic Order:
- Fulfillment transitions use the explicit fulfillment state machine, row locking, replay collapse,
  payment/accounting gates, and atomically advance the SQL order lifecycle.
- Outbound fulfillment is blocked when the order lifecycle is `Cancelled`, `Refunded`, or `Completed`.
- Fulfillment progression cannot regress order state. Cancelled or refunded orders cannot resurrect into
  outbound fulfillment.
- Fulfillment mutation routes require `Owner`, `Admin`, or `Manager`. Locked fulfillment loads retain tenant
  query filters; a guessed foreign order ID cannot be mutated.
- Manual fulfillment requires recognized payment/accounting authority before outbound progression.
- Fulfillment and SQL order lifecycle are committed together; shipment events cannot directly replace order
  lifecycle authority.
- Shipment services do NOT own fulfillment truth. Any shipment progress implying `Confirmed/Packed/OutForDelivery/
  Delivered` must call `IOrderFulfillmentService.SynchronizeFromShipmentAsync`; a `FulfillmentDeliveredEvent` must
  originate from `IOrderFulfillmentService`, not from shipment status mapping.

Marketplace sub-order (POS Admin):
- `CommerceHubOrderManagementService.UpdateSubOrderStatusAsync` resolves tenant context from `ITenantContext`, not
  client-submitted tenant ids. It loads marketplace order/sub-orders/items under serializable transaction and
  provider row locks, rejects mismatched `StoreId`, validates the transition graph, validates payment readiness
  before outbound fulfillment, records status history, updates item state, applies delivery/cancellation
  inventory side effects, and publishes status events.
- POS Admin cannot mutate another tenant's sub-order: the reviewed endpoint rejects sub-orders whose `StoreId`
  differs from the current tenant.
- Delivery is only reachable from `Shipped` and requires paid/COD readiness. Delivery commits inventory through
  `CommitDeliveryAsync`, updates delivered item timestamps, applies delivered financial workflow, publishes
  projection events, and locks the delivered state from later POS Admin mutation.

Shipment authority (backend `ShipmentService` / `AdminShipmentService`):
- Shipment identity, creation, tracking number, carrier assignment, shipment status, delivery timestamps, and
  cancellation evidence are backend-owned. Standalone shipment pages present current backend shipment DTOs; they
  do not persist or fabricate shipment state.
- Carrier-managed tracking is backend-owned; manual tracking mutation is blocked for carrier-managed shipments;
  stale carrier success is rejected into reconciliation evidence.
- Duplicate shipment requests/replay cannot create a second accepted shipment or inconsistent fulfillment state
  (serializable SQL, current order reload, order-bound shipping-method validation, existing-shipment detection,
  idempotency).


# Cancellation Rules

Cancellation authority is the admin order lifecycle service, not the fulfillment DTO.

- Storefront: cancellation through the fulfillment compatibility route fails closed and directs callers to the
  authoritative admin cancellation service, which coordinates payment cancellation and reservation restoration.
  Shipped/delivered state is NOT cancelable through the retired fulfillment path.
- Marketplace: vendor cancellation is allowed only BEFORE terminal fulfillment states and releases order-item
  reservations through `InventoryConsistencyService.ReleaseOrderItemReservationsAsync`. Marketplace status
  aggregates across sub-orders, so multi-vendor partial cancellation does NOT incorrectly cancel unrelated stores.
  Cancelled sub-orders cannot re-enter fulfillment through the certified POS Admin path.
- Recognized money blocks unsafe direct cancellation. Pending payment cancellation is outbox-backed.
- Inventory reservations are restored ONLY AFTER payment cancellation is safe. Release authority must be current
  SQL truth at mutation time (serializable transaction; reload reservation, order, payment-intent, CommerceHub
  payment, unresolved-webhook evidence; changed or uncertain truth collapses to skip or routes through payment
  lifecycle expiration).
- Customer cancellation has NO frontend transition executor in the CommerceHub path; it is backend-owned.
- Cancelled orders cannot resurrect (no forward transition out of `Cancelled` for either model).


# Return Rules

Return workflow is refund-owned. (See also Refund Integration.)

- Approved refund requests create/maintain `CommerceHubReturnRequest` rows. Return transitions run under
  serializable transactions and `FOR UPDATE` locks, validate with `CommerceHubReturnPolicy`, append audit logs,
  and synchronize refund status to `WaitingForReturn` or `AwaitingPayout` ONLY when the configured execution
  trigger milestone is reached.
- Confirmed return receipt is inventory-bearing and CANNOT be overridden through the generic Super Admin override
  path.
- Return-dependent refunds cannot execute until `CommerceHubReturnPolicy.IsExecutionTriggerReached(...)` accepts
  the current return status.
- Return rows are tied to the refund request and serialized (double return is prevented).
- Return replay reloads current SQL refund/payment/order/return state under lifecycle boundaries and routes
  uncertain provider outcomes to `ReconciliationRequired` instead of forcing terminal success. Recovery does NOT
  use Mongo/Elasticsearch as order authority.
- Return status vocabulary: `Pending, PickupScheduled, PickedUp, InTransit, DeliveredToVendor, Confirmed, Failed`.
- Fulfillment return requests CANNOT claim or synthesize refund authority. Physical return state cannot forge
  refund success. Refunded orders cannot resume fulfillment.


# Refund Integration

Refund authority remains in `IPaymentLifecycleService` (and the refund/return lifecycle services for marketplace).
(Detailed refund rules are in `.ai/hermes/04-payments.md`; the order-relevant invariants are summarized here.)

- Refunds require recognized collected money, are cumulatively bounded (amount cannot exceed collected/refundable
  balance), row-locked, tenant-scoped, replay-safe, and accounting-outbox backed. Full refund -> `Refunded`;
  partial -> `PartiallyRefunded`.
- Refund execution locks SQL order authority, verifies payment/refund state, bounds cumulative refund amount to
  collected money, and is unique by tenant/order/refund identity.
- Refund creation validates tenant-owned sub-order, paid payment state, cumulative refund totals, payout overlap,
  and safety/readiness gates. Full refunds mark sub-orders `Refunded` and aggregate marketplace status; partial
  refunds update payment/refund state without forcing order-state corruption.
- Marketplace refund execution is separated from order delivery. Return-dependent refunds cannot execute until the
  return execution trigger milestone is reached.
- `CommerceHubRefundStatus.ReconciliationRequired` is the explicit refund state for external refund execution that
  may already have happened while local financial recognition is incomplete.
- Manual refund payout confirmation is external-money evidence. Once an operator confirms money was sent and
  supplies a transfer reference, a later local persistence/ledger/projection failure must NOT convert that refund
  to a terminal safe-failed state. `Failed` is safe only when external non-execution is known/proven.
- Post-settlement refunds: current non-deleted `PaymentSettlementBatch` plus live `PaymentSettlementOrders`
  lineage is the membership authority. A post-settlement refund must prove the refunding order still belongs to
  the current settlement batch lineage (header count matches current mappings) before bank-account refund posting.
- Refund accounting recomposes from current SQL `OrderRefund` rows before journal generation. Partial/full refund
  replay must not override current refund lineage or create a duplicate refund journal.
- Settlement membership authority: `PaymentSettlementBatch.OrderCount` is recorded metadata only, NOT posting
  authority. Current `PaymentSettlementOrders` lineage decides membership. Settlement accounting rejects
  non-positive gross amounts and fees outside `[0, gross]` before settlement persistence, accounting enqueue, or
  `Order.PaymentStatus` mutation.

Conflict / correction note (PASS 4): a prior design trusted the denormalized `PaymentSettlementBatch.OrderCount`
header as settlement-membership proof. This is OVERRIDDEN — current live `PaymentSettlementOrders` lineage is the
authority, and a mismatch fails closed to retry/reconciliation-required. The same correction applies to
post-settlement refund settlement-lineage revalidation (including later refunds after the order has moved to
`PartiallyRefunded`).


# Projection Rules

Mongo/Search/cache/affiliate/CommerceHub/rebuild paths are TRIGGERS only. Before mutating derivative visibility
they must reload current SQL authority and TOMBSTONE/DELETE when SQL no longer proves visibility. Stale publish/
unpublish/replay must not resurrect hidden/deleted orders; stale replay must converge from SQL truth.

- Storefront order read model is MongoDB derived from PostgreSQL order events, with SQL-backed repair. Critical
  reads may self-repair from SQL before serving and must prefer the freshest matching projection if duplicates exist.
- CommerceHub marketplace order/read models are MongoDB derived from PostgreSQL authority plus marketplace
  outbox/backbone, with SQL-backed repair for critical order detail. Read models may lag and may be rebuilt;
  critical reads must prefer the freshest matching projection if duplicates exist.
- Mongo order-projection uniqueness is enforced by best-effort startup/consumer index creation, NOT a synchronous
  SQL boundary. Because duplicate/missing Mongo order docs can survive a historical race or index-creation failure:
  - Critical storefront and CommerceHub order reads/repairs must PROVE SQL existence first, repair targeted
    projections when required, and fail closed with temporary unavailability instead of returning false absence or
    partial authority.
  - A missing Mongo order document is NOT sufficient proof the order does not exist. Any projection-dependent `404`,
    stale response, or token/ownership check that depends on projected fields must read SQL authority directly or
    trigger targeted SQL-backed repair first.
  - If duplicate Mongo order documents survive, critical reads/repairs must select the FRESHEST projection by
    authoritative freshness markers (source-mutation ticks, event version, event timestamp, projection update time)
    rather than trusting the first matching document.
- Storefront payment replay deletion (PASS 4): when payment replay no longer has current SQL order authority, the
  consumer deletes tenant-scoped storefront order projection state and invalidates its cache. When replayed payment
  status conflicts with current SQL status, the consumer recomposes SQL status and freshness WITHOUT appending
  callback-/event-derived timeline narrative.
- Admin order reads are no-store at request and response boundaries; they re-enter SQL on refresh/reconnect/restart/
  Redis flush/Mongo rebuild/projection rebuild. React/React Query/Zustand/LocalStorage/SessionStorage/Redis/Mongo/
  stale DTOs/replay cannot become order authority.
- Projection repair is SQL-to-Mongo only. A stale projection cannot overwrite SQL order truth.
- Marketplace order currency projection: a non-EGP order projection is withheld with a service-unavailable response;
  cache/history/BFCache candidates become visible only after fresh backend evidence passes EGP checks.


# Event Publishing Rules

Authoritative integration events go through `IEventPublisher`, never raw MassTransit publish
(`IBus`/`ISendEndpointProvider`/`IPublishEndpoint`). SQL commit -> outbox intent is ATOMIC; outbox -> downstream
projection is ASYNCHRONOUS. Read-side lag is expected and accepted.

- Normal MassTransit integration events (product, category, CMS, pricing, media, inventory, listings, homepage,
  CommerceHub, affiliate, search, cache) go through `IEventPublisher` and the EF outbox. The publisher validates
  tenant scope, affected-tenant scope, aggregate ordering metadata, and transactional publish boundaries.
- High-sensitivity/high-volume flows use dedicated SQL outboxes: accounting, stock sync, marketplace event,
  marketplace projection, payment cancellation, and CommerceHub payment compensation. Dedicated outboxes use retry
  fields, retry backoff, claim leases, aggregate/warehouse-scoped ordering, and terminal failure handling.
  Accounting critical financial events remain retryable instead of being silently cleaned up as dead letters.
- Order/lifecycle events: status events are staged by the lifecycle service inside the same transaction as the
  status mutation (after validation, row locks, payment readiness, inventory side effects). Projection events are
  consequences, not authority.
- Do NOT emit rebuilt current-state payloads as if they were the original historical event unless the event is
  explicitly modeled as compensation or snapshot repair.
- Idempotency and replay collapse: Tier 1 projection consumers use `ProjectionEventFence` /
  `ProjectionConsumerEventFence`, processed-event rows, event ids, freshness filters, or local inbox documents.
  `ProcessedEvents` allows the same delivered message to be safely ignored after successful handling.
- Ordering is guaranteed ONLY per aggregate (event versioning). There is NO global total order. Consumers must be
  idempotent. Out-of-order delivery converges because handlers reload current SQL truth or suppress stale replay
  rather than trusting payload order alone.
- CommerceHub marketplace event outbox blocks newer same-aggregate events behind earlier unresolved events.
  Accounting outbox blocks newer financial truth messages behind earlier aggregate versions. Stock-sync outbox
  orders by event version/stock timestamp/event timestamp/creation timestamp/id within
  `{TenantId, VariantId, WarehouseId}`.


# Recovery Rules

Recovery discovery is evidence only. Mutation authority must be current SQL truth.

- Storefront order projection reconciliation/repair: repairs the exact SQL page before Mongo is trusted, and fails
  closed as temporary unavailability if Mongo still cannot prove the requested SQL page after repair.
- Inventory reservation cleanup: recomposes current cleanup authority per candidate, recomputes remaining reserved
  quantity, opens a serializable local release transaction, clears stale EF tracking, reloads locked SQL authority,
  and skips if current order/payment/provider truth no longer permits release. If a storefront pending/authorized/
  awaiting-verification payment state is discovered, the job exits the local release transaction and delegates to
  `IPaymentLifecycleService.MarkPaymentExpiredAsync(...)` instead of releasing inventory. Cleanup is conservative:
  it must NOT release stock while provider cash authority is unresolved.
- CHECKOUT-CON-14 (payment/reconciliation recovery): recovery workers that clear unresolved external money, enqueue
  suspense clearance, release marketplace holds, or move payout status must reload the exact evidence row and
  dependent payment/order/payout rows inside a serializable SQL transaction. Duplicate recovery workers, delayed
  retries, and stale reconciliation loops collapse when the locked SQL row is no longer in the recoverable state.
  - `CommerceHubFinanceReconciliationJob`: selects candidate IDs only, opens a per-candidate serializable
    transaction, locks the unresolved webhook row, locks the matched paid `CommerceHubPayment`, locks the current
    `CommerceHubMarketplaceOrder`, and only then queues suspense clearance or releases marketplace holds.
  - `StorefrontUnresolvedPaymentReconciliationJob`: selects webhook log IDs only, opens a serializable recovery
    transaction, locks the `PaymentWebhookLog`, locks the current `PaymentIntent` and `Order`, then delegates payment
    lifecycle replay and resolved-unresolved accounting enqueue inside that SQL boundary.
  - `CommerceHubPayoutReconciliationHostedService`: quarantines stale `Processing` payouts only after a serializable
    transaction reloads and locks the current payout row.
- Shipment recovery (`ShipmentAuthorityRecoveryHostedService`): establishes tenant candidates then re-enters
  `ShipmentService.CreateShipmentAsync` or `ShipmentService.ReconcileShipmentAuthorityAsync`; it does NOT write
  shipment status directly.
- CommerceHub / Storefront reconciliation currency is EGP-only. A suspense-routed CommerceHub provider-success
  webhook later matched to a paid local payment commits suspense clearance + marketplace hold release in the SAME
  local authority transaction. CommerceHub unresolved-payment recovery matches ONLY current SQL paid payments with
  provider identity and EXACT amount/currency before suspense-clearance accounting is queued.


# Business Invariants

1. PostgreSQL is the only order truth. If two systems disagree, PostgreSQL wins.
2. Channel identity (Storefront / Affiliate / Marketplace) is fixed at creation; it never changes and metadata
   never leaks across channels. Storefront cannot become Affiliate or Marketplace; Marketplace cannot become
   Storefront or Affiliate.
3. `Order.Status` changes only inside an authorized order lifecycle boundary; `PaymentStatus` only through
   `IPaymentLifecycleService`; `FulfillmentStatus` only through `IOrderFulfillmentService` / marketplace service.
   Direct patches are retired and fail closed.
4. Client-supplied checkout totals, discounts, tax, shipping, payment-method eligibility, and payment split values
   are stale-client assertions only and fail closed on mismatch.
5. Checkout final order creation is a serializable SQL transaction that re-locks store/shipping/payment/method/
   product/variant authority and rebuilds all money + commission snapshots from that final authority before commit.
6. A reservation is claimed before commit, released only after safe payment cancellation, and consumed only through
   current inventory lineage. Marketplace reservation quantity must EXACTLY match current SQL order-item authority.
7. Cancelled / Refunded / Delivered / Completed are terminal for their respective models; they cannot resurrect
   into outbound fulfillment or forward transitions.
8. CommerceHub marketplace orders are EGP-only; tenant/storefront currency cannot override. Non-EGP is rejected or
   withheld.
9. Multi-vendor split: sub-order shipping is immutable after creation; Vendor A's method/cost cannot bind Vendor B.
   Marketplace shipping total is the sum of sub-order values, not an authority source.
10. Refunds require recognized collected money, are cumulatively bounded, tenant-scoped, replay-safe, and
    accounting-outbox backed. `PaymentSettlementBatch.OrderCount` is recorded metadata, NOT settlement-membership
    authority; live `PaymentSettlementOrders` lineage is.
11. Returns are refund-owned; confirmed inventory-bearing returns cannot be overridden; return-dependent refunds
    require the execution trigger milestone.
12. Projection/derivative replay fails closed: reload SQL, tombstone/delete when SQL no longer proves visibility,
    collapse duplicate EventId delivery, prefer freshest duplicate projection by freshness markers. A missing Mongo
    order doc is NOT proof the order does not exist.
13. Authoritative events go through `IEventPublisher`; SQL commit -> outbox is atomic, outbox -> projection is async.
    Ordering is per-aggregate only; consumers are idempotent.
14. Recovery re-enters current SQL authority under serializable/locked boundaries; stale/duplicate/uncertain truth
    collapses to no-op/skip/quarantine/reconciliation-required, never a second mutation.
15. Tenant isolation is non-negotiable: tenant scope from trusted backend resolution, never client/provider claims.
    Cross-tenant order/fulfillment/sub-order access is a critical defect.
16. A successful SQL commit is the point the system considers an internal business mutation authoritative; at-least-
    once delivery is accepted, consumers must be idempotent, and no global total order is assumed.


# Never Assume

- Never assume a frontend "success" page means an order exists. Only an accepted backend-created order DTO with
  valid identity, canonical statuses, finite totals, non-empty order number, and item evidence establishes creation.
- Never assume payment success (redirect, Stripe.js outcome, provider callback) means the order is paid. It is
  evidence until lifecycle admission.
- Never assume a missing Mongo order document means the order does not exist. Prove SQL existence first; repair
  targeted projection; fail closed to temporary unavailability.
- Never assume the first matching Mongo order doc is authoritative when duplicates exist. Prefer the freshest by
  freshness markers.
- Never assume a generic `Order` lifecycle rule applies to a CommerceHub sub-order (or vice versa). Different models,
  different transition graphs.
- Never assume `Pending` + marketplace `OnHold` is a retryable customer payment state. Unresolved captured cash /
  suspense makes it `PAYMENT_RECONCILIATION_REQUIRED` / manual-review, not retryable. Follow `CanRetryPayment`.
- Never assume a tenant's configured currency legitimizes a non-EGP CommerceHub order or reconciliation. CommerceHub
  currency is EGP sovereign.
- Never assume `PaymentSettlementBatch.OrderCount` is settlement-membership authority. Live `PaymentSettlementOrders`
  lineage is.
- Never assume zero enabled payment methods means "fall back to COD." It is fail-closed "provider unavailable."
  STRIPE and PAYMOB are not interchangeable in CommerceHub.
- Never assume a promotion/coupon/tax/shipping/payment-fee computed client-side is authoritative. Backend recomputes.
- Never assume a shipment status mapping creates fulfillment authority. Only `IOrderFulfillmentService` originates
  fulfillment events.
- Never assume a return or physical receipt creates refund success. Returns are refund-owned; refund success is a
  payment-lifecycle mutation.
- Never assume Super Admin can override marketplace order state. No such endpoint exists; only audited return/refund
  workflows and restricted return-status overrides.
- Never assume a cached/Redis/stale snapshot reflects current order truth.
- Never assume recovery can reconstruct primary order/payment truth from browser state, cache, projection docs, stale
  queue payloads, or an old callback. Recovery uses current SQL authority.
- Never assume the frozen Payment Runtime is live. Live flows use the SQL-backed lifecycle services.


# QA Checklist

Order authority & lifecycle:
- [ ] Every `Order.Status` / `FulfillmentStatus` / marketplace sub-order status change goes through the authorized
      lifecycle boundary; no direct SQL/bulk/Ef patch of lifecycle fields.
- [ ] Generic `Order` illegal transitions blocked (direct Cancelled/Shipped/Delivered/Completed/Refunded, reopen
      Cancelled/Refunded, mutate terminal Delivered/Completed, direct Confirmed/Processing).
- [ ] Marketplace sub-order illegal transitions blocked (e.g. Pending->Delivered, Cancelled->Shipped, out of
      terminal Cancelled/Delivered/Completed/Refunded).
- [ ] `Delivered`/`Completed`/`Refunded` terminal for POS Admin sub-order mutation; cancelled/refunded cannot resurrect.
- [ ] `If-Match` RowVersion enforced on admin `PUT /status`; stale replay rejected with 409.

Checkout:
- [ ] Final storefront order creation is serializable and re-locks store/shipping/payment/method/variant/product
      authority; rebuilds all money + commission snapshots from final authority.
- [ ] Client totals/discounts/tax/shipping/payment-fee rejected on mismatch (409).
- [ ] Promotion calculation failure fails closed (no silent discount removal).
- [ ] Inactive `OnlineCustomer` rejected before order creation.
- [ ] Duplicate submission / retry / refresh / parallel tab / replay cannot create a second order (idempotency +
      active-cart uniqueness + serializable).
- [ ] CommerceHub: exact requested online provider validated per store; zero enabled methods = fail-closed.
- [ ] CommerceHub currency EGP-only; non-EGP rejected/withheld; NOT VALID EGP constraints enforced.
- [ ] Marketplace split shipping immutable after creation; Vendor A cost cannot bind Vendor B; replay matches request hash.

Inventory:
- [ ] Reservation claimed before commit through certified inventory authority.
- [ ] Marketplace reservation quantity EXACTLY matches current SQL order-item authority before payment/recovery/commit.
- [ ] Cancellation restores reservations only AFTER payment cancellation safe.
- [ ] Delivery commit uses inventory-lineage authority, not publication guard.
- [ ] Return replay revalidates current stock ownership; inactive warehouse/superseded variant fails closed.

Payment / refund:
- [ ] Online payment advances only through `IPaymentLifecycleService`; COD not treated as collected online.
- [ ] Unmatched CommerceHub webhook persists unresolved SQL evidence before retryable failure.
- [ ] Non-retryable payment state follows `CanRetryPayment`, not generic `Pending`.
- [ ] Refund only from recognized collected money; amount <= refundable balance; cumulative bound enforced.
- [ ] Post-settlement refund proves current settlement-batch lineage membership.
- [ ] Manual refund confirmation + transfer reference never downgraded to safe-failed by later local write failure.
- [ ] `PaymentSettlementBatch.OrderCount` NOT used as settlement-membership authority.

Fulfillment / shipment:
- [ ] Fulfillment mutation requires Owner/Admin/Manager; tenant-filtered row locks block foreign order mutation.
- [ ] Outbound fulfillment blocked when order lifecycle Cancelled/Refunded/Completed.
- [ ] Shipment progress implies fulfillment only via `IOrderFulfillmentService.SynchronizeFromShipmentAsync`.
- [ ] Duplicate shipment requests/replay cannot create second accepted shipment.

Projection / recovery:
- [ ] Critical order reads prove SQL existence first; missing Mongo doc fails closed, not "not found".
- [ ] Duplicate Mongo order docs: freshest by freshness markers selected.
- [ ] Storefront payment replay deletes tenant-scoped projection + cache when SQL authority missing; no appended
      callback timeline narrative on conflict.
- [ ] Recovery re-enters current SQL under serializable/locked boundary; stale/uncertain collapses, no second mutation.

Channel isolation:
- [ ] Storefront/Affiliate/Marketplace lineage fixed at creation; no cross-channel spoof/admission.
- [ ] Admin presentation admits backend source/affiliate/marketplace evidence only; no frontend fabrication.
- [ ] No cross-tenant order/fulfillment/sub-order read or mutation leaked.


# Common Failure Scenarios

1. Frontend "success" page without a backend order.
   Symptom: checkout returns a non-terminal payment state but UI presents completed order.
   Correct: order creation requires accepted backend DTO with valid identity/status/totals/items; non-terminal
   payment state must not be rendered as success.

2. Duplicate order from double-click / retry / parallel tab.
   Symptom: two orders created from one cart.
   Correct: backend idempotency + active-cart uniqueness + serializable transaction return the same order or replay
   it; no second order.

3. Stale-client total accepted.
   Symptom: tampered client total/Discount/tax reaches a successful order.
   Correct: backend recomputes and rejects mismatch before reservation/insert/payment/accounting.

4. Cross-channel lineage spoof.
   Symptom: Storefront order presented as Marketplace, or Affiliate attribution injected into Marketplace.
   Correct: backend Source/IsMarketplaceOrder/affiliate evidence must agree; contradictory evidence fails closed.

5. Marketplace reservation "at least enough."
   Symptom: active reservation quantity >= order quantity but ownership/quantity don't exactly match current SQL
   order item.
   Correct: rejected; exact ownership + exact quantity required.

6. Vendor A shipping/method/cost bound to Vendor B.
   Symptom: checkout/grouping reassigns split-order shipping.
   Correct: sub-order shipping immutable after creation; SQL tenant-scoped lookup rejects substitution; replay
   matches request hash.

7. Non-EGP CommerceHub order/reconciliation.
   Symptom: USD/EUR marketplace order or reconciliation group.
   Correct: EGP-only; rejected at creation (DB constraint) and withheld at read/projection; reconciliation treats
   non-EGP as anomaly.

8. Unmatched CommerceHub webhook dropped.
   Symptom: provider callback with no linked local payment row is dropped, losing unresolved cash truth.
   Correct: persisted as `CommerceHubUnresolvedPaymentWebhook` before retryable failure; readiness fails closed.

9. Read-model shows "order not found" because Mongo doc missing.
   Symptom: projection-dependent 404 though SQL order exists.
   Correct: prove SQL existence first, repair targeted projection, fail closed to temporary unavailability.

10. Duplicate Mongo order docs after index-creation race.
    Symptom: two order projection docs for one SQL order.
    Correct: critical reads select the freshest by freshness markers, not first match.

11. Cancelled/refunded order resurrects into fulfillment.
    Symptom: fulfillment route pushes a terminal order back outbound.
    Correct: outbound fulfillment blocked for Cancelled/Refunded/Completed; no forward transition out of terminal.

12. Settlement posting from `OrderCount` header.
    Symptom: settlement journal posted from denormalized batch header after lineage drifted.
    Correct: live `PaymentSettlementOrders` lineage must match; mismatch fails closed to retry/reconciliation.

13. Inventory released while provider payment unresolved.
    Symptom: cleanup releases stock from a stale snapshot.
    Correct: recompute current authority under serializable tx; skip or route to payment lifecycle expiration.

14. Super Admin "overrides" marketplace order state.
    Symptom: assumed admin endpoint changes sub-order status.
    Correct: no such endpoint exists; only audited return/refund workflows and restricted return-status overrides.


# High Risk Areas

1. Order lifecycle bypass (direct `Order.Status`/`FulfillmentStatus`/marketplace status writes outside the
   authorized boundaries). The DbContext guard blocks marketplace field writes without
   `CommerceHubBoundaryMutationScope`; verify it is not bypassed by bulk SQL/`ExecuteUpdate`.

2. Channel-identity leakage / spoof (Storefront <-> Affiliate <-> Marketplace). Admin presentation must admit
   backend source/affiliate/marketplace evidence; frontend cannot fabricate lineage.

3. CommerceHub currency sovereignty. Any path that could accept/restore non-EGP marketplace order/reconciliation
   must fail closed. DB `NOT VALID` EGP constraints enforce new/changed rows but legacy rows remain withheld.

4. Marketplace reservation exact-match vs unresolved external payment truth. Reservation cleanup/payment recovery
   must not release stock or accept fulfillment while provider cash authority is unresolved.

5. Settlement-membership authority drift (`OrderCount` vs live `PaymentSettlementOrders`). Post-settlement refunds
   and settlement posting must revalidate live lineage, including later refunds after `PartiallyRefunded`.

6. Split-order shipping immutability and vendor isolation. Replayed checkout must match request hash; Vendor A
   cannot bind Vendor B; generic shipment/carrier orchestration must not rebind split-order shipping.

7. Projection resurrection / false absence. Stale publish/unpublish/replay must not resurrect hidden orders; missing
   Mongo doc must not be treated as non-existence; duplicate docs must select freshest.

8. Tenant isolation leakage on orders/fulfillments/sub-orders (host resolution, event scope, cache keying,
   projection, admin scope from `X-Tenant-Id`). Cross-tenant access is critical.

9. Refund/return coupling. Confirmed inventory-bearing returns cannot be overridden; return-dependent refunds need
   the execution trigger; physical return cannot forge refund success; refunded orders cannot resume fulfillment.

10. Unresolved external-money ambiguity (CommerceHub suspense, scope-less captured payments). Globally blocks
    outward-money readiness; must be reconciled, not cleared by a stale callback or projection.

11. Checkout serializable transaction under concurrent promotion/affiliate-policy activation. Set-membership closure
    requires serializable isolation or equivalent authority-version fence; a below-serializable refresh can let a
    concurrent winning promotion land after in-transaction refresh.

12. Event/outbox consistency. Raw MassTransit publish bypasses `IEventPublisher`; consumers must be idempotent;
    per-aggregate ordering only; critical accounting outbox rows must not be time-cleaned before posting.

13. Governance/operational drift around already-hardened primitives. A failing architecture source guard
    (e.g. `StorefrontOrderAuthorityCertificationTests`, `CommerceHubOrderLifecycleCertificationTests`,
    `AdminOrder*CertificationTests`, `CrossSystemAuthorityRegressionGuardTests`) is itself a real defect.


# Regression Scenarios

These are the frozen invariants to defend in regression. (Test anchors exist; reuse, don't reinvent.)

- Storefront checkout + financial + reservation + concurrency: `StorefrontCheckoutAuthorityCertificationTests`,
  `StorefrontCheckoutPricingTotalsAuthorityCertificationTests`, `StorefrontCheckoutSubmissionAuthorityCertificationTests`.
- Storefront order authority (lifecycle, ownership, cancellation, refund, fulfillment, tenant isolation):
  `StorefrontOrderAuthorityCertificationTests`.
- CommerceHub marketplace order authority (creation, identity, vendor access, lifecycle, concurrency):
  `CommerceHubMarketplaceOrderAuthorityCertificationTests`.
- CommerceHub order & fulfillment lifecycle (transitions, cancellation, delivery, return, refund trigger,
  replay/recovery, projection):
  `CommerceHubOrderLifecycleCertificationTests`, `CommerceHubFulfillmentAuthorityCertificationTests`,
  `CommerceHubOrderTransitionCertificationTests`.
- CommerceHub currency: `CommerceHubOrderCurrencyAuthorityCertificationTests`.
- Split-order shipping: `ShippingSplitOrderAuthorityCertificationTests`.
- Admin order management (read, lifecycle, payment, fulfillment/shipment, cross-channel, storefront lineage,
  affiliate lineage): `AdminOrderManagementEndToEndAuthorityCertificationTests`,
  `AdminOrderReadAuthorityCertificationTests`, `AdminOrderLifecycleAuthorityCertificationTests`,
  `AdminOrderPaymentAuthorityCertificationTests`, `AdminOrderFulfillmentShipmentAuthorityCertificationTests`,
  `AdminOrderCrossChannelPresentationAuthorityCertificationTests`, `StorefrontOrderLineageAuthorityCertificationTests`,
  `AffiliateOrderLineageAdminPresentationAuthorityCertificationTests`.
- Orders/payments/refunds/accounting convergence (settlement lineage, storefront projection deletion,
  conflicting-timeline suppression): `FinancialIntegrityRiskHardeningTests`, `AccountingLedgerAuthorityTests`,
  `AccountingOutboxProcessorTests`, `PaymentLifecycleSettlementAuthorityTests`, `PaymentLifecycleRefundAuthorityTests`,
  `EcommerceRefundLifecycleTests`.
- Cross-system authority / tenant isolation regression freezes: `CrossSystemAuthorityRegressionGuardTests`,
  `TenantIsolationCertificationFreezeTests`.
- Chaos validation: `SemanticConvergenceChaosValidationTests`, `ReplayRebuildChaosCertificationTests`.
- Inventory reservation/commit/return interaction: `InventoryConsistencyServiceTests`,
  `InventoryReservationLifecycleIntegrationTests`, `CheckoutConcurrencyHardeningSourceTests`.


# Source Documents

Primary (read before any order work; conflicts resolve toward these):
- `.ai-context/ARCHITECTURE_TRUTH.md` — Order Lifecycle (POS/Storefront + CommerceHub), checkout rules, inventory/
  fulfillment/payment coupling, accounting lifecycle, projection/repair rules, CHECKOUT-CON-* rules.
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md` — PASS 16.5A order & fulfillment lifecycle; reservation cleanup
  authority recomposition; CHECKOUT-CON-14 recovery/reconciliation authority; order/payment/fulfillment lineage
  authority (9H/9I); payment lineage (9I); accounting ledger (9J).
- `.ai-context/PROJECT_CONTEXT.md` — subsystem ownership, financial/inventory/order authority tables.
- `.ai-context/CODEX_RULES.md` — non-negotiable rules (source-of-truth, shadow authorities, tenant isolation,
  reconciliation awareness, 9H/9I/9J lineage rules, POS-ADMIN-CERTIFICATION-01 PASS 3 inventory/reservation freeze).
- `.ai-context/KNOWN_RISKS.md` — confirmed/mitigated/accepted risk vocabulary and entries.

Order authority / lifecycle / channel certification findings (evidence):
- `docs/architecture/storefront-order-authority-certification-findings.md` (PASS P2-A)
- `docs/architecture/commercehub-marketplace-order-lifecycle-authority-certification-findings.md` (PASS 16.5A)
- `docs/architecture/commercehub-marketplace-order-authority-certification-findings.md` (PASS M2)
- `docs/architecture/commercehub-marketplace-order-creation-authority-certification-findings.md` (PASS M2-B)
- `docs/architecture/commercehub-marketplace-order-final-authority-certification-findings.md` (PASS M2-J)
- `docs/architecture/commercehub-marketplace-order-financial-authority-certification-findings.md` (PASS M2-I)
- `docs/architecture/commercehub-marketplace-order-state-transition-authority-certification-findings.md` (PASS M2-H)
- `docs/architecture/commercehub-marketplace-order-ownership-authority-certification-findings.md`
- `docs/architecture/commercehub-marketplace-order-identity-authority-certification-findings.md`
- `docs/architecture/commercehub-marketplace-order-visibility-authority-certification-findings.md`
- `docs/architecture/commercehub-marketplace-order-action-authority-certification-findings.md`
- `docs/architecture/commercehub-marketplace-split-order-authority-certification-findings.md`
- `docs/architecture/commercehub-order-checkout-currency-authority-certification-findings.md` (PASS C3)
- `docs/architecture/commercehub-order-fulfillment-lifecycle-certification-findings.md`
- `docs/architecture/orders-payments-refunds-accounting-certification-findings.md` (PASS 4 — settlement/refund/accounting)

Admin order management certification findings:
- `docs/architecture/admin-order-management-end-to-end-authority-certification-findings.md`
- `docs/architecture/admin-order-lifecycle-authority-certification-findings.md` (SF-ADM3.2)
- `docs/architecture/admin-order-read-authority-certification-findings.md` (SF-ADM3.1)
- `docs/architecture/admin-order-payment-authority-certification-findings.md` (SF-ADM3.3)
- `docs/architecture/admin-order-fulfillment-shipment-authority-certification-findings.md` (SF-ADM3.4)
- `docs/architecture/admin-order-cross-channel-presentation-authority-certification-findings.md` (SF-ORD.XCHANNEL)
- `docs/architecture/storefront-order-lineage-admin-presentation-authority-certification-findings.md` (SF-ORD5)
- `docs/architecture/affiliate-order-lineage-admin-presentation-authority-certification-findings.md` (AF-ORD1)
- `docs/architecture/marketplace-order-lineage-admin-presentation-authority-certification-findings.md`

Checkout / split certification findings:
- `docs/architecture/storefront-checkout-authority-certification-findings.md` (PASS P2-B)
- `docs/architecture/storefront-checkout-submission-authority-certification-findings.md` (SF-PA5.6)
- `docs/architecture/storefront-checkout-pricing-totals-authority-certification-findings.md` (SF-PA5.5)
- `docs/architecture/storefront-checkout-session-authority-certification-findings.md` (SF-PA5.1)
- `docs/architecture/storefront-checkout-address-authority-certification-findings.md`
- `docs/architecture/commercehub-checkout-certification-findings.md`
- `docs/architecture/shipping-split-order-authority-certification-findings.md` (PASS M8-F)
- `docs/architecture/POSADMIN_CHECKOUT_0_ARCHITECTURE.md`, `POSADMIN_CHECKOUT_1_RUNTIME_AUTHORITY.md`
  (POS Admin storefront checkout architecture/runtime)

Projection / event / outbox certification findings:
- `docs/architecture/event-propagation-projection-freshness-certification-findings.md` (PASS 10.1)
- `docs/architecture/mongo-projection-convergence-certification-findings.md` (PASS 10.3)
- `docs/architecture/affiliate-checkout-attribution-order-line-admission-commission-candidate-certification-findings.md`

Adjacent authority (read before payment/refund/inventory work, already distilled in companion guides):
- `.ai/hermes/04-payments.md` — payment lifecycle, webhook, refund, reconciliation, settlement authority.
- `.ai/hermes/03-inventory.md` — inventory reservation/commit/return lineage.
- `.ai/hermes/02-architecture.md` — repository-wide architectural truths and QA mental model.

Conflict note: the order rules above are consistent with the sibling `.ai/hermes` guides. Where earlier design
trusted the denormalized `PaymentSettlementBatch.OrderCount` as settlement-membership proof (a pattern that also
appears in older order/finance prose), the authoritative PASS 4 correction overrides it: current live
`PaymentSettlementOrders` lineage is the only membership authority. No other contradiction between the inspected
order documents was found; the marketplace (`CommerceHub*`) and generic (`Order`) models are intentionally distinct
and are documented as such rather than as a conflict.
