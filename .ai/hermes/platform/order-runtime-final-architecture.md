99# FINAL CANONICAL ARCHITECTURE — ORDER RUNTIME (Shopixy / PointOfSale)

> Status: DEFINITIVE, implementation-backed specification.
> Authority rule: **Implementation is the ONLY source of truth.** Every architectural
> claim is cited to `file:line` in the C# backend (`PointOfSale.API`, `PointOfSale.Core`,
> `PointOfSale.Infrastructure`). Markdown, XML comments, UI, controller names and DTO
> names are treated as *secondary evidence only* — where a comment claims a behaviour that
> the code does not enforce, that gap is recorded as a finding, never repeated as truth.
> Scope: the **tenant storefront/omnichannel `Order`** aggregate
> (`PointOfSale.Core/Entities/Order.cs`). This is a DISTINCT runtime from the CommerceHub
> `CommerceHubMarketplaceOrder`/`CommerceHubSubOrder` marketplace aggregate and from the
> POS `SalesInvoice` register aggregate — the divergence between the three is documented
> explicitly (§ Runtime Authority Divergence).

---

## 1. Executive Summary

The `Order` is **not an entity — it is a coordinating runtime**. Concretely it is an
**Order Lifecycle Runtime**: a PostgreSQL-authoritative aggregate that holds *three
independent, separately-encapsulated state machines* (checkout, payment, fulfillment),
freezes an immutable historical + financial snapshot at creation, and coordinates every
participating runtime (cart, pricing, currency, inventory/reservation, payment lifecycle,
accounting GL, shipping/fulfillment, affiliate, notifications, analytics, reporting)
**without ever letting a derived/read-model state become business truth**.

Load-bearing evidence:

- The `Order` aggregate carries THREE state fields, each of which can ONLY be mutated by
  ONE authorized service, enforced at the persistence boundary by a dedicated
  `SaveChangesInterceptor` (fail-closed):
  - `Order.Status` (`OrderStatus`) — guarded by `OrderStatusSaveChangesInterceptor`
    (`PointOfSale.Infrastructure/Data/OrderStatusSaveChangesInterceptor.cs:112-146`);
    writes allowed only inside `BeginAuthorizedContext()` (`:80-86`).
  - `Order.PaymentStatus` — guarded by `PaymentStatusSaveChangesInterceptor`
    (`PointOfSale.Infrastructure/Data/PaymentStatusSaveChangesInterceptor.cs:75-109`);
    writes allowed only after `IPaymentLifecycleService` sets the authorized context
    (`:34-44`). Encapsulated with a private backing field on the entity
    (`Order.cs:115-149`).
  - `Order.FulfillmentStatus` — guarded by `FulfillmentStatusSaveChangesInterceptor`
    (`PointOfSale.Infrastructure/Data/FulfillmentStatusSaveChangesInterceptor.cs:76-98`);
    writes allowed only via `IOrderFulfillmentService` (`:35-45`). Encapsulated with a
    private backing field (`Order.cs:189-223`).
- `Order.CheckoutState` (`CheckoutState`) is a fourth state machine, self-encapsulated in
  the aggregate itself with a `private set` and a guarded transition method
  `TransitionCheckoutState` / `IsValidCheckoutTransition` (`Order.cs:106,656-692`).
- Money is tracked **per SKU on `OrderItem`** (`PaidAmount`/`RefundedAmount`), not on the
  order header (`OrderItem.cs:135-186`; Order-level derivations `Order.cs:557-607`).
- Historical + financial immutability is achieved by snapshotting: pricing snapshot JSON
  (`Order.PricingSnapshotJson`, `Order.cs:426`), immutable shipping/payment-method fields
  (`Order.cs:360-372,502-520`), immutable affiliate commission terms on each item
  (`OrderItem.cs:123-133`), and immutable variant attributes (`OrderItem.cs:72-77`).
- PostgreSQL is authoritative and tenant-isolated at the DB level (global query filter
  `ApplicationDbContext.cs:622-623`); MongoDB `OrderDocument` is a derived read projection
  that carries a monotonic `ProjectionMutationVersion` and is rejected/repaired when stale
  (`Order.cs:162-180`; projection consumer + repair services — see § CQRS).

**Answer to "what is an Order":** an **Order Lifecycle Runtime / Domain Coordinator**, not
a mere record and not a "checkout runtime" (checkout is a separate service that *ends* the
moment the order is committed). Full defence in § Architectural Synthesis.

---

## 2. Architecture Vision

The Order runtime exists to convert a *mutable, cache-resident purchase intent* (the Redis
cart) into a *historically immutable, financially authoritative, inventory-safe,
payment-consistent* commercial record, and then to act as the single coordination point
across every downstream runtime for the remainder of that record's life.

Design pillars, each code-cited:

1. **Single-writer-per-concern.** Each mutable concern of an order has exactly one
   authorized mutator, enforced at `SaveChanges` (the three interceptors above). No
   controller, consumer, or service may shortcut a state field.
2. **SQL is truth; everything else is derived.** Orders live in PostgreSQL under the tenant
   global query filter (`ApplicationDbContext.cs:622`). Mongo/Redis/ES are projections and
   caches, reconciled from SQL and fail-closed on staleness.
3. **Freeze at commit.** Prices, currency, addresses, shipping method, payment method,
   variant attributes and affiliate commission terms are snapshotted into the order/items
   at creation and are not re-read from live catalog/policy afterwards.
4. **Fail-closed money.** Payment status can only advance through `IPaymentLifecycleService`
   and every money-recognizing transition is coupled to a ledger journal
   (`PaymentStatus` doc contract `PaymentStatus.cs:35-107`; `FinancialPostingStatus`
   gate `Order.cs:537-546`, CHECK constraint `ApplicationDbContext.cs:1721-1727`).
5. **Separation of lifecycles.** Checkout, payment and fulfillment progress on independent
   axes and are only *synchronized* into the customer-facing `OrderStatus` by a dedicated
   sync service — never conflated.

---

## 3. Runtime Purpose, Scope, Owns / Does NOT Own

### Runtime Purpose
Own the authoritative lifecycle of a committed tenant order from creation to archival, and
coordinate the runtimes that contribute to or consume that lifecycle.

### Runtime Scope
- Tenant storefront + omnichannel orders: `Order.Source ∈ {Ecommerce, POS, Amazon,
  Marketplace, Manual}` (`OrderSource.cs:6-22`).
- One `Order` aggregate + `OrderItem` children + `OrderRefund`/`OrderRefundLine`
  (`Order.cs:694-697`).
- NOT in scope: CommerceHub marketplace orders (`CommerceHubMarketplaceOrder`) and the POS
  `SalesInvoice` register path — separate aggregates with separate authority (§ Divergence).

### Runtime OWNS (SQL truth it writes)
- The `Order`/`OrderItem` records, `OrderNumber`, the four state machines
  (`OrderStatus`, `CheckoutState`, `PaymentStatus`*, `FulfillmentStatus`*), the frozen
  snapshots (pricing/address/shipping/payment/affiliate), and refund records.
  (*payment & fulfillment are owned by the Order but MUTATED only by their lifecycle
  services — the Order is the system of record, the services are the writers.)

### Runtime does NOT OWN (defers to owning services)
- **Cart** → Redis (`CartService`), read-only at checkout.
- **Pricing/promotion/coupon computation** → pricing engine, re-authored at checkout, then frozen.
- **Currency policy** → tenant/storefront config; order only stores the frozen code.
- **Inventory truth** → `ProductVariantStock` SQL (reservation via `InventoryConsistencyService`).
- **Payment state** → `IPaymentLifecycleService` (the ONLY `PaymentStatus` writer).
- **Fulfillment state** → `IOrderFulfillmentService` / carrier webhooks.
- **Accounting GL** → journal entries via the accounting outbox (order never writes `JournalEntry`).
- **Affiliate commission** → affiliate runtime consumes order events; order only stores frozen terms.


---

## 4. Architecture Principles (each code-cited)

- **P1 — One authorized mutator per state field (fail-closed at persistence).**
  `OrderStatusSaveChangesInterceptor` throws unless `IsAuthorizedContext`
  (`OrderStatusSaveChangesInterceptor.cs:107,135-146`), enabled by default in production
  (`:27`), scoped by `BeginAuthorizedContext()` (`:80-86`). Same pattern for payment
  (`PaymentStatusSaveChangesInterceptor.cs:73,103-108`) and fulfillment
  (`FulfillmentStatusSaveChangesInterceptor.cs:74,93-97`). Violations are logged
  `LogCritical [SECURITY_VIOLATION]` with a captured stack trace.
- **P2 — CheckoutState is a guarded self-transition.** The aggregate refuses invalid
  transitions itself: `TransitionCheckoutState` throws on an illegal edge
  (`Order.cs:656-670`); the legal edge set is a closed switch (`Order.cs:672-692`) with
  `Paid` and `Cancelled` terminal (`:687,689`).
- **P3 — Money lives on items, not the header.** `OrderItem.PaidAmount`/`RefundedAmount`
  are the ledger fields (`OrderItem.cs:154,161`); order-level totals are computed
  projections (`Order.cs:563-581`), and the legacy header `IsPaid` is explicitly flagged
  as possibly out-of-sync (`Order.cs:522-531`).
- **P4 — Freeze at commit.** Immutable snapshots: shipping method name/ETA
  (`Order.cs:360-372`), payment method code/name/fee (`Order.cs:502-520`), pricing snapshot
  JSON (`Order.cs:426`), variant attributes JSON (`OrderItem.cs:72-77`), affiliate
  commission terms (`OrderItem.cs:123-133`).
- **P5 — Monotonic projection versioning.** `Order.AdvanceProjectionMutationVersion` refuses
  regression (`Order.cs:171-180`); read models reject stale versions.
- **P6 — DB-level tenant isolation.** `Order`, `OrderRefund`, `OrderRefundLine` are under the
  EF global query filter `TenantId == CurrentTenantId && !IsDeleted`
  (`ApplicationDbContext.cs:622-627`). (Contrast: CommerceHub marketplace orders are NOT —
  § Divergence.)
- **P7 — Customer-authority boundary enforced in the schema.** A CHECK constraint forbids an
  order from having BOTH a POS `CustomerId` and a storefront `OnlineCustomerId`, and forbids
  an `Ecommerce` order from carrying a POS `CustomerId`
  (`CK_Orders_CustomerAuthorityBoundary`, `ApplicationDbContext.cs:1724-1727`).
- **P8 — Financial-posting integrity enforced in the schema.** `CK_Orders_FinancialPostingStatus_Valid`
  requires the timestamp/journal/failure-reason fields consistent with each posting state
  (`ApplicationDbContext.cs:1715-1720`).
- **P9 — Optimistic concurrency.** The order row uses PostgreSQL `xmin` as a row version
  (`ApplicationDbContext.cs:1763-1765`).

---

## 5. Order Runtime

- **Aggregate:** `Order : BaseEntity, ITenantOwned, IProjectionVersioned`
  (`Order.cs:12`). Children: `OrderItem` (`Order.cs:695`), `OrderRefund` (`Order.cs:696`).
- **Ownership.** Owned by the tenant (`TenantId` NOT NULL, `Order.cs:14-18`); associated to
  EITHER a POS `Customer` (`CustomerId`, `Order.cs:29-30`) OR a storefront `OnlineCustomer`
  (`OnlineCustomerId`, `Order.cs:39-40`) OR a guest (guest email/phone/name,
  `Order.cs:57-59`, `IsGuestCheckout` `Order.cs:67`) — never both a POS + online customer
  (P7 constraint).
- **Identity.** `OrderNumber` unique per tenant among non-deleted rows
  (`IX_Orders_TenantId_OrderNumber`, `ApplicationDbContext.cs:1733-1736`); assigned during
  creation (created empty at `CheckoutService.cs:5431`).
- **Where orders begin.** Storefront: `CheckoutService.CreateOrderAsync` constructs the
  aggregate (`CheckoutService.cs:5427`) inside a single Serializable transaction
  (checkout stage table, § Checkout). External channels: imported via `Source`/`SourceOrderId`
  with a unique `(TenantId, Source, SourceOrderId)` index (`ApplicationDbContext.cs:1751-1754`).
- **Where orders end.** Terminal states: `CheckoutState.Paid`/`Cancelled` are terminal
  (`Order.cs:687,689`); `FulfillmentStatus.Completed`/`Returned`/`Cancelled` have no allowed
  transitions (`FulfillmentStatus.cs:93-95`); soft-delete via `IsDeleted` (query filter).
- **Persistence.** PostgreSQL, precision 18,2 on all money fields
  (`ApplicationDbContext.cs:1766-1776`); indexed by status/payment-status/checkout-state/
  financial-posting-status + created-at for operational queries (`:1737-1760`).
- **Duplicate-order guard.** A filtered unique index guarantees at most one *active* order
  per cart: `IX_Orders_TenantId_CartId_ActiveStatuses` over statuses
  {Pending, Confirmed, Processing, OnHold} (`ApplicationDbContext.cs:1727-1743`,
  filter defined `:1728`).

---

## 6. Order Lifecycle Runtime — the FOUR state machines

The order does not have one lifecycle; it has four orthogonal ones. This is the defining
characteristic of the runtime.

### 6.1 CheckoutState (placement/payment-intent axis) — `CheckoutState.cs`
`Draft → PendingPaymentIntent → PaymentPending → Paid` with `Failed/Cancelled/Expired`
branches. Legal edges (`Order.cs:672-692`):
- `Draft → PendingPaymentIntent`
- `PendingPaymentIntent → {PaymentPending, Failed, Cancelled, Expired}`
- `PaymentPending → {Paid, Failed, Cancelled, Expired}`
- `Failed → {PendingPaymentIntent, Cancelled}` (retry)
- `Expired → PendingPaymentIntent` (retry)
- `Paid` terminal, `Cancelled` terminal.
New orders start at `PendingPaymentIntent` (`Order.cs:106`).

### 6.2 PaymentStatus (financial-recognition axis) — `PaymentStatus.cs:24-107`
`Pending(0) → Authorized(1) / CashOnDelivery(10) → Collected(2) → Settled(9)`, with
`Refunded(5)`, `PartiallyRefunded(6)`, `AwaitingVerification(7)`, `Rejected(8)`,
`Failed(4)`, `Expired(11)`, `AmountMismatch(12)`. Only `Collected` recognizes revenue
(`PaymentStatus.cs:35-40`). `Paid=2` and `PartiallyPaid=3` are `[Obsolete]`
(`PaymentStatus.cs:42-53`). Mutated ONLY by `IPaymentLifecycleService` (§ Payment).

### 6.3 FulfillmentStatus (physical axis) — `FulfillmentStatus.cs:14-73`
`Created → Confirmed → Packed → OutForDelivery → Delivered → {Completed | Returned}`, with
`FailedDelivery(7)` and `Cancelled(8)`. The state machine is codified as an allowed-
transitions table (`FulfillmentStatus.cs:83-98`) and `CanTransitionTo` (`:103-106`);
`Completed/Returned/Cancelled` are terminal (`:93-95`). `Delivered` triggers the payment
lifecycle for offline collection (`FulfillmentStatus.cs:42-48`). Mutated ONLY by
`IOrderFulfillmentService` (§ Fulfillment).

### 6.4 OrderStatus (customer-facing rollup axis) — `OrderStatus.cs:6-34`
`Pending(0), Confirmed(1), Processing(2), Shipped(3), Delivered(4), Completed(5),
Cancelled(6), Refunded(7), OnHold(8)`. This is the coarse, presentation-facing status; it
is SYNCHRONIZED from the payment+fulfillment axes by `OrderStatusSyncService` (§ Status
Runtime), never set ad hoc — the interceptor enforces the authorized boundary
(`OrderStatusSaveChangesInterceptor.cs:112-146`).

**Key architectural fact:** the "assumed" single lifecycle (Draft→Cart→Paid→Shipped→...)
does NOT exist as one enum. It is composed from four independent machines. Cart is not even
part of the order (it is a Redis pre-order artifact); "Draft" exists only as a `CheckoutState`
value and is not the entry state used by the storefront (entry is `PendingPaymentIntent`).

---

## 7. Checkout Runtime (where checkout STOPS and the order BEGINS)

Checkout is a **separate runtime** (`PointOfSale.API/Services/Checkout/CheckoutService.cs`,
~8.7k lines; orchestrator `ProcessCheckoutAsync`). It does not persist as part of the order;
it *produces* the order and then ceases. The ordered pipeline (verified line map):

| Stage | What | Authority |
|---|---|---|
| 0 | Caller ownership validation (SQL) | `ValidateCheckoutCallerOwnershipAsync` |
| 0.5 | Durable idempotency pre-cart replay (SQL) | `CheckoutRequestIdempotencyCoordinator.TryClaimAsync` |
| 0.7 | Storefront accepting-orders gate (SQL StoreConfig) | `EnsureStorefrontAcceptingOrdersAsync` |
| 1 | Acquire owned execution lock (Redis) | `CheckoutExecutionLockManager.TryAcquireAsync` |
| 2 | Cart snapshot (Redis → immutable) | `GetCartSnapshotAsync` |
| 2.5 | Snapshot-hash idempotency claim | `CheckoutCartIdempotencySnapshotBuilder` |
| 2.7 | Duplicate active-order guard (SQL) | `FindActiveOrderForCartAsync` + `IX_Orders_TenantId_CartId_ActiveStatuses` |
| 4 | **Price recalculation from SQL variants** | `RecalculatePricesAsync` (ignores cart price) |
| 4.5 | Coupon/promo reconciliation | `ReconcileCheckoutDiscountAuthorityAsync` |
| 5 | Shipping + payment method validation (SQL) | ShippingMethod / TenantPaymentMethod |
| 5.5 | Currency lockdown | `EnsureCheckoutCurrencyAsync` |
| 6 | **Order creation (1 Serializable txn)** | `CreateOrderAsync` (`CheckoutService.cs:5427`) |
| 6.x | Inventory validate + reserve (IN txn) | `ValidateAndReserveInventoryAsync` |
| 6.x | Coupon/offer reservation (IN txn) | `ReserveCouponAsync` / `ReserveAppliedOfferPromotionAsync` |
| 6.x | Order persist + outbox + idempotency bind | `BindOrderCreatedForCommitAsync` |
| 7 | **Payment intent (intent only)** | provider adapters |
| 8 | Persist replay-safe idempotency result | `CheckoutRequestIdempotencyCoordinator.CompleteAsync` |
| 9 | Optional side-effects (WhatsApp) non-fatal | `TryEmitWhatsAppOrderConfirmationAsync` |

**The boundary.** Checkout stops at commit of the Serializable order transaction (stage 6).
At that instant the order exists in SQL with `Status=Pending`, `PaymentStatus=Pending`,
`CheckoutState=PendingPaymentIntent`, `FulfillmentStatus=Created`
(`CheckoutService.cs:5452-5453`; defaults `Order.cs:106,115,189`). Everything after — payment
capture, fulfillment, refunds — is the Order Lifecycle Runtime, not checkout.

**Validation authorities at checkout (all re-authored, cart never trusted):**
- Pricing: recomputed from SQL `ProductVariants`, `EffectivePrice = OnlinePrice ?? Price`;
  cart prices discarded.
- Currency: locked from storefront/tenant config, not the client.
- Inventory: reserved from `ProductVariantStock` inside the same Serializable txn so stock
  and order cannot diverge.
- Identity: `IsGuestCheckout` derived from auth state, NOT the request DTO
  (`CheckoutService.cs:5448-5449`); affiliate attribution stamped server-side (`Order.cs:78-79`).
- Address: geographic admission evidence captured into immutable place-id fields
  (`CheckoutService.cs:5475-5492`; `Order.cs:301-320`).

---

## 8. Order Snapshot Runtime (immutable vs mutable)

At creation the order freezes a historical snapshot; these are written once and not
re-derived from live sources afterwards:

| Snapshot | Field(s) | Immutable? | Evidence |
|---|---|---|---|
| Customer identity | `CustomerId`/`OnlineCustomerId`/guest fields | frozen at create | `Order.cs:29-67`, set `CheckoutService.cs:5436-5449` |
| Prices (line) | `OrderItem.UnitPrice/CostPrice/Total` | frozen SKU snapshot | `OrderItem.cs:90-116` |
| Pricing (order) | `PricingSnapshotJson` | frozen JSON | `Order.cs:423-426` |
| Currency | `Order.Currency` | frozen code | `Order.cs:418-421` |
| Address | shipping/billing + geographic place-ids | frozen | `Order.cs:293-351`, `CheckoutService.cs:5469-5498` |
| Taxes | `TaxAmount` / `OrderItem.TaxAmount` | frozen | `Order.cs:408-411`, `OrderItem.cs:102-105` |
| Shipping | `ShippingMethodName`, `EstimatedDeliveryDays`, `ShippingCost` | frozen | `Order.cs:360-372` |
| Discounts/coupons | `DiscountAmount`, `PromotionDiscountAmount`, `CouponCode`, applied-promotion name/desc | frozen | `Order.cs:386-491` |
| Variant | `OrderItem.SKU/VariantName/VariantAttributesJson/ImageUrl` | frozen | `OrderItem.cs:55-83` |
| Payment method | `PaymentMethodCode/Name/Fee` | frozen | `Order.cs:502-520` |
| Affiliate terms | `OrderItem.AffiliateCommission*Snapshot` | frozen (CHECK-enforced) | `OrderItem.cs:123-133`, `ApplicationDbContext.cs:~1810+` |
| Fulfillment mode | `FulfillmentMode`/`FulfillmentCarrierCode` | frozen at create | `Order.cs:255-277`, set `CheckoutService.cs:5466-5467` |

Mutable after creation (by authorized services only): the four state machines, tracking
number/carrier (`Order.cs:374-375`), timestamps (`Order.cs:637-642`), admin notes
(`Order.cs:616-619`), and the per-item money fields (`OrderItem.cs:154,161`).

**DB immutability (corrected):** `Order.cs:240-266` and `FulfillmentMode.cs:9-11` assert
`FulfillmentMode`/`FulfillmentCarrierCode` are "IMMUTABLE after order creation (enforced by
DB trigger)". A trigger **does** exist — `trg_prevent_fulfillment_mode_update` (BEFORE UPDATE
ON "Orders") plus CHECK `CK_Orders_FulfillmentOwnership` (Manual⇒carrier NULL, Carrier⇒code
NOT NULL) — in the standalone script `./add_fulfillment_ownership_snapshot.sql:142-177`,
which `RAISE EXCEPTION ... 'FulfillmentMode is immutable ...'` on any change
(`:146,:152`). **However**, this trigger/CHECK is NOT present in the EF migration pipeline
(no grep hit in `PointOfSale.Infrastructure/Migrations`; the EF model snapshot lacks both),
and **no wiring into CI/DbPatcher was found** (grep for the script name across `.cs/.ps1`
returns nothing). Therefore immutability depends on that script having been applied
out-of-band; it is not part of the managed migration story. Recorded as follow-up FU-3.

---

## 9. Financial Snapshot Runtime (what becomes immutable financially)

- **Header totals** (frozen at commit): `Subtotal`, `PromotionDiscountAmount`,
  `DiscountAmount` (coupon), `ManualDiscountAmount`, `TotalDiscountAmount`, `TaxAmount`,
  `ShippingCost`, `PaymentFee`, `Total`, `Currency` (`Order.cs:379-421`), precision 18,2
  (`ApplicationDbContext.cs:1766-1776`).
- **Grand total identity:** `Total = Subtotal − Discount + Tax + Shipping`
  (`Order.cs:413-416`).
- **Per-SKU money ledger** (mutable within rules): `OrderItem.PaidAmount`/`RefundedAmount`;
  `NetPaidAmount = Paid − Refunded` (`OrderItem.cs:179`); `RefundedAmount` "never exceeds
  PaidAmount" by convention (`OrderItem.cs:156-161`) — hardened by refund migrations S22/S23
  (§ Refund).
- **Financial posting lifecycle** (the ledger-truth gate): `FinancialPostingStatus`
  (`NotRequired/Pending/Posted/Failed`) with `PendingAtUtc/PostedAtUtc/JournalId/FailureReason`
  (`Order.cs:537-546`), integrity CHECK constraint `CK_Orders_FinancialPostingStatus_Valid`
  (`ApplicationDbContext.cs:1715-1720`). `IsFinanciallyRecognized` = Collected/Settled/Refunded
  (`Order.cs:157-160`).
- **Settlement** is a `PaymentStatus` transition `Collected → Settled` (bank receipt,
  `PaymentStatus.cs:82-87`) — see § Payment for the ledger coupling.


---

## 10. Cart Integration Runtime

- Cart is a **Redis-only** artifact owned by `CartService`; it is NOT part of the order and
  is read-only at checkout. Orders reference the producing cart via `Order.CartId`
  (`Order.cs:48-53`).
- The cart→order handoff is the immutable cart snapshot taken at checkout stage 2; cart
  prices are then discarded and re-authored from SQL (§ Checkout stage 4).
- Duplicate protection spans the boundary: the filtered unique index
  `IX_Orders_TenantId_CartId_ActiveStatuses` (`ApplicationDbContext.cs:1727-1743`) makes a
  cart yield at most one active order.
- **Divergence:** Cart ≠ Order — the cart is mutable/cache-resident and best-effort; the
  order is SQL-authoritative and immutable-at-commit. No cart price, quantity, or total is
  trusted once checkout begins.

## 11. Identity Integration Runtime (who may create / modify orders)

- **Dual customer identity, mutually exclusive:** `CustomerId` (POS `Customer`) XOR
  `OnlineCustomerId` (storefront `OnlineCustomer`), enforced by
  `CK_Orders_CustomerAuthorityBoundary` (`ApplicationDbContext.cs:1724-1727`). An
  `Ecommerce` order may never carry a POS `CustomerId`.
- **Guest:** `IsGuestCheckout` derived from auth state at checkout (NOT the DTO)
  (`CheckoutService.cs:5448-5449`); guest contact captured in `GuestEmail/Phone/Name`
  (`Order.cs:57-59`). Guest order access is cookie/token gated
  (`Security/GuestOrderAccessCookie.cs`).
- **Who may CREATE:** storefront customers + guests via `CheckoutController` → `CheckoutService`;
  admins/POS via `OrdersController`/`AdminOrdersController` (Source=POS/Manual); external
  channels via import (Source=Amazon/Marketplace).
- **Who may MODIFY:** nobody modifies a state field directly. Payment → `IPaymentLifecycleService`,
  fulfillment → `IOrderFulfillmentService`, status → authorized sync boundary, checkout state →
  the aggregate's own guarded transition. Admin/customer read surfaces:
  `AdminOrdersController`, `CustomerOrdersController`, `StorefrontOrdersReadController`,
  `SuperAdminOrdersController` (read/query; mutations route through the lifecycle services).
- **SuperAdmin:** `SuperAdminOrdersController`/`SuperAdminOrdersService` are a
  platform read/oversight surface; they do NOT own the order lifecycle and still route any
  mutation through the same authorized services (SuperAdmin isolation principle).

## 12. Pricing Integration Runtime

- Pricing is **re-authored from SQL** at checkout (`RecalculatePricesAsync`,
  `EffectivePrice = OnlinePrice ?? Price`); the client/cart price is never trusted.
- Once computed, prices are **frozen** into `OrderItem.UnitPrice/CostPrice/Total`
  (`OrderItem.cs:90-116`) and the order header discount/tax/total fields
  (`Order.cs:379-416`), plus a full `PricingSnapshotJson` audit blob (`Order.cs:423-426`).
- Promotion vs coupon are stored separately: `PromotionDiscountAmount` (offers) vs
  `DiscountAmount` (coupon) vs `ManualDiscountAmount`, reconciled into `TotalDiscountAmount`
  (`Order.cs:386-406`). `PromotionCalculationFailed`/`PromotionWarningMessage` preserve a
  degraded-pricing signal for replay (`Order.cs:480-491`).

## 13. Currency Integration Runtime

- Currency is locked at checkout from storefront/tenant config (stage 5.5) and stored as the
  frozen `Order.Currency` code (`Order.cs:418-421`).
- **Invariant:** an order never loses its currency; every money field is denominated in the
  frozen code. (Platform financial base for the marketplace/GL is EGP via
  `FinancialCurrencyGuard`, but the tenant order currency is whatever was locked.)

## 14. Inventory Integration Runtime + 15. Reservation Runtime

- Inventory truth is `ProductVariantStock` SQL (`AvailableQuantity = Quantity − Reserved`).
  The order does NOT own inventory.
- **Reservation at checkout:** `ValidateAndReserveInventoryAsync` runs INSIDE the
  Serializable order-creation transaction (`emitExternalReservationSideEffects:false`), so
  stock and order commit atomically — they cannot diverge. Reservation TTL from
  `InventoryReservationTtlMinutes`; `InventoryReservedEvent` emitted.
- **Oversell prevention / concurrency:** Serializable isolation on the checkout txn +
  authoritative stock read + fail-closed SQL-vs-Mongo divergence (SQL wins → 0 on missing).
- **Release / rollback:** pre-commit failures call `ReleaseInventoryReservationAsync`
  (total-mismatch, order-create failure, unexpected error paths); post-commit payment-intent
  loss that becomes unrecoverable FAILS the order, releases reservations + coupon, emits
  `OrderCancelledEvent`.
- **Deduction:** committed reservation converts to a deduction on delivery/fulfillment
  progression (inventory runtime owns the commit); the order coordinates via events.

## 16. Payment Integration Runtime

Authoritative writer of `Order.PaymentStatus`: **`IPaymentLifecycleService`**
(`PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs`) — the ONLY mutator for
the storefront `Order` (enforced by `PaymentStatusSaveChangesInterceptor`). Single funnel
`HandlePaymentResultAsync` maps a `PaymentLifecycleOutcome` to a handler
(`PaymentLifecycleService.cs:105-117`).

State machine (verified transition methods):
- **Authorize** (pre-auth, no money): `HandleAuthorizedAsync` requires `Pending|Authorized`,
  sets `Authorized` (`PaymentLifecycleService.cs:1782`). No journal.
- **Capture/Collect** (revenue recognized): `HandleSuccessAsync` sets `Collected`
  (`:2053`), recomputes `IsPaid` from item-level (`:2055`), `PaidAt=now` (`:2056`), and
  allocates money per-item via `ApplySuccessfulPaymentAllocation` (`:4156-4182`). COD collect
  requires `FulfillmentStatus.Delivered` (`:1944-1948`); wallet requires review-approval
  source (`:1951-1954`).
- **Settle** (bank receipt): `RecordOfflineSettlementAsync` (`:298`) /
  `RecordGatewaySettlementAsync` (`:554`) require all target orders `Collected`
  (`:414,:672`), set `Settled` (`:454,:714`), enqueue settlement accounting outbox, and are
  GATED on collection accounting already `Posted` (reject `collection_accounting_not_posted`
  `:1489,:1542`).
- **Failure:** `HandleFailureAsync` sets `Failed`/`Rejected`/`Expired` per outcome
  (`:2228-2233,:2385`).
- **Review (wallet):** `AdmitPaymentReviewAsync` → `AwaitingVerification` (`:268`);
  approve/reject facades (`:806-837`).
- **Idempotency/replay:** every handler returns `wasIdempotent:true` when already terminal
  for that outcome (`:1732-1740,:2247-2275`; refund dedup `FindExistingRefund` `:879-886`).

Authorized-write pattern (uniform): `EnableAuthorizedContext()` → set status →
`order.AcceptPaymentStatusChange()` → `SaveChanges` → `finally DisableAuthorizedContext()`
(canonical example `:2043-2131`).

Providers (Stripe/Paymob/Fawry/ManualWallet/COD) are **capture-only adapters**: they call
into the lifecycle service (e.g. `StripePaymentService.cs:1742`, `PaymobPaymentService.cs:1323`
call `RecordRefundAsync`); they never mutate `PaymentStatus` directly.

**Ledger coupling (fail-closed money):** on capture, `EnqueueSuccessOutboxAsync` stages an
`AccountingOutboxMessage` in the same txn (`:5207-5276`); post-commit
`PostCommittedCollectionAccountingFromSqlTruthAsync` posts from SQL truth and stamps
`Order.FinancialPostingStatus=Posted` + `FinancialPostedAtUtc` + `FinancialPostingJournalId`
(`:5341-5346`). The order never writes `JournalEntry` directly — accounting is owned by
`ICommerceAccountingService`.

**Who owns payment state:** the payment lifecycle service owns the *transitions*; the order
owns the *record*. Payment never owns the order (see Invariant BI-4).

**FLAG:** chargeback is NOT handled for the storefront `Order` (only CommerceHub runtimes
have chargeback). Recorded as gap G-1.

## 17. Receipt Integration Runtime

- Storefront orders do not have a dedicated thermal/print receipt runtime; the POS
  `SalesInvoice` path owns `ReceiptData` (a JSON DTO, no thermal printing) — separate
  aggregate. Storefront order confirmation is delivered as a WhatsApp/email notification
  (§ Notifications), not a printed receipt. Recorded as scope note (order receipt = the
  order record + confirmation notification, not a print artifact).

## 18. Shipping Integration Runtime (where shipping begins)

- Shipping ownership is decided at checkout and frozen: `FulfillmentMode` (Manual|Carrier)
  + `FulfillmentCarrierCode` (`Order.cs:255-277`, set `CheckoutService.cs:5466-5467`).
- Shipping starts when fulfillment advances `Packed → OutForDelivery`
  (`FulfillmentStatus.cs:34-38`), owned by `IOrderFulfillmentService` (Manual) or carrier
  webhooks (Carrier). Tracking number/carrier stored on the order
  (`Order.cs:374-375`); `ShippedAt`/`DeliveredAt` timestamps (`Order.cs:639-640`).
- Shipment orchestration: `OrderShipmentOrchestrationConsumer` (`PointOfSale.API/Consumers`);
  delivery finalization: `OrderDeliveredEventConsumer`; `OrderShippedEvent`/`OrderDeliveredEvent`
  (`OrderEvents.cs:116,128`).
- **Manual vs Carrier are mutually exclusive at the write path:** Manual orders reject
  carrier webhooks; Carrier orders reject manual admin transitions (`FulfillmentMode.cs:32-62`;
  computed guards `Order.cs:279-289`).

## 19. Refund Runtime

- Entities: `OrderRefund` (header) + `OrderRefundLine` (per-item allocation)
  (`Order.cs:696`; `OrderRefund.cs`, `OrderRefundLine.cs`).
- Written ONLY through `IPaymentLifecycleService.RecordRefundAsync`
  (`PaymentLifecycleService.cs:839`): allowed from `Collected|Settled|PartiallyRefunded`
  (`:888-894`); full vs partial decided by cumulative refunded vs refundable
  (`:934-935`); **over-refund blocked** (`:927-931`); zero/negative rejected (`:912-915`).
- Refund amount allocated across items into `OrderRefundLine` and bumps
  `OrderItem.RefundedAmount` (`AllocateRefundAcrossItems` `:961,:4332-4375`).
- **DB-level guarantees:** `OrderRefundLine.Amount >= 0` CHECK
  (`CK_OrderRefundLines_Amount_NonNegative`, S23 migration `:25-28`); line amount also
  guarded in-code by a throwing setter (`OrderRefundLine.cs:22-36`); unique
  `(OrderRefundId, OrderItemId)` (S22 `:122-127`).
- Pre- vs post-settlement refunds route to different accounting outbox messages
  (`EnqueueSettledEcommerceRefundAsync` vs `EnqueueEcommerceRefundAsync`, `:971-985`);
  refund GL posted from SQL truth (`PostCommittedRefundAccountingFromSqlTruthAsync` `:5374`).
- Affiliate refund: `OrderItemRefundedEvent` (`Events/Affiliate/OrderItemRefundedEvent.cs`)
  drives commission reversal in the affiliate runtime.
- **FLAG G-2:** a separate CommerceHub `RecordRefundAsync` exists on
  `ICommerceHubFinancialLedgerService` — different aggregate, do not conflate.

## 20. Cancellation Runtime

- Cancellation is expressed across axes: `OrderStatus.Cancelled` (`OrderStatus.cs:27`),
  `CheckoutState.Cancelled` (terminal, `Order.cs:687`), `FulfillmentStatus.Cancelled`
  (terminal, `FulfillmentStatus.cs:69-72`); `CancelledAt`/`CancellationReason`
  (`Order.cs:641-642`).
- **Automatic cancellation:** checkout post-commit unrecoverable payment-intent gap FAILS
  the order (`CheckoutState.Failed`), releases inventory reservations + coupon, emits
  `OrderCancelledEvent` (§ Checkout failure/compensation).
- **Payment expiry:** `MarkPaymentExpiredAsync` expires active intents
  (`PaymentLifecycleService.cs:1111-1193`); late funds after expiry are quarantined, not
  auto-cancelled.
- **Merchant/customer cancellation:** routed via `IOrderFulfillmentService.CancelAsync`
  (Manual mode) and the authorized status boundary; inventory released, no financial
  recognition (`FulfillmentStatus.cs:68-72`).


## 21. Order Fulfillment Runtime (authoritative writer)

Authoritative writer of `Order.FulfillmentStatus`: **`IOrderFulfillmentService`**
(`PointOfSale.Infrastructure/Fulfillment/OrderFulfillmentService.cs`) — the ONLY mutator,
enforced by `FulfillmentStatusSaveChangesInterceptor`.

Public transitions (each maps to a target state):
- `ConfirmAsync` → `Confirmed` (`OrderFulfillmentService.cs:53-63`)
- `PackAsync` → `Packed` (`:81-92`)
- `MarkOutForDeliveryAsync` → `OutForDelivery` (`:111-123`)
- `MarkDeliveredAsync` → `Delivered` (`:143-172`)
- `MarkCompletedAsync` → `Completed` (`:280-290`)
- `MarkReturnedAsync` → `Returned` (`:308-337`)
- `MarkDeliveryFailedAsync` → `FailedDelivery` (`:359-376`)
- `RetryDeliveryAsync` → `OutForDelivery` (`:397-407`)
- `CancelAsync` → `Cancelled` (`:425-436`)
- `SynchronizeFromShipmentAsync` → drives carrier-sourced progression (`:196-261`)

The shared mutation core (`:663-721`):
1. **Row lock** the order (`SELECT ... FOR UPDATE` on PostgreSQL / `UPDLOCK,ROWLOCK,HOLDLOCK`
   on SQL Server) — `LoadOrderForFulfillmentMutationAsync` (`:723-754`).
2. **Fail-closed money gate:** if the target is an outbound progression and the paid order's
   collection accounting is not yet posted, the transition is BLOCKED with LogCritical
   (`:643-661`) — fulfillment cannot outrun the ledger.
3. **State-machine validation:** `oldStatus.CanTransitionTo(targetStatus)` (`:664`) using the
   allowed-transitions table (`FulfillmentStatus.cs:83-98`).
4. **Authorized write:** `FulfillmentStatusSaveChangesInterceptor.EnableAuthorizedContext()`
   + `OrderStatusSaveChangesInterceptor.BeginAuthorizedContext()` scope (`:676-677`); set
   `FulfillmentStatus`, `AcceptFulfillmentStatusChange()` (`:680-681`).
5. **Converge the customer-facing `OrderStatus`** in the SAME authorized scope via
   `ResolveOrderStatus` (`:684-688,759-787`) and publish `OrderStatusChangedEvent` (`:690-698`)
   + the fulfillment domain event (`:701-702`), then `SaveChanges` + commit (`:704-708`).

`FulfillmentMode` gating: `GetAvailableActionsAsync` returns no manual actions unless
`order.AllowsManualFulfillment` (`:472`); Manual-mode orders reject carrier webhooks and
vice-versa (`FulfillmentMode.cs:32-62`; computed guards `Order.cs:279-289`). `Delivered`
triggers the payment lifecycle for offline collection (`FulfillmentStatus.cs:42-48`).

## 22. Order Status Runtime (who owns the customer-facing rollup)

- `Order.Status` (`OrderStatus`) is a **derived rollup**, not an independently-set field. It
  is converged from the authoritative fulfillment axis by `ResolveOrderStatus`
  (`OrderFulfillmentService.cs:759-787`): Confirmed→Confirmed, Packed→Processing,
  OutForDelivery→Shipped, Delivered→Delivered, Completed→Completed, FailedDelivery→OnHold.
- **Monotonic progression:** a status only advances if its rank is higher
  (`GetOrderProgressionRank` `:789-800`), except `OnHold` which can interrupt.
- **Terminal guard:** once `Cancelled/Refunded/Completed`, the rollup is frozen
  (`IsTerminalOrderLifecycle` `:756-757`, honored at `:763-765`).
- **Authorized boundary:** direct `Order.Status` writes are rejected outside
  `OrderStatusSaveChangesInterceptor.BeginAuthorizedContext()`
  (`OrderStatusSaveChangesInterceptor.cs:107,135-146`); the fulfillment service opens that
  scope during convergence (`OrderFulfillmentService.cs:677`).
- **Cross-service sync:** `OrderStatusSyncService`/`OrderStatusSyncConsumer`
  (`IOrderStatusSyncService`) reconcile status from payment/fulfillment sub-states and via
  `OrderStatusSaveChangesInterceptor` interceptor path (`Data/OrderStatusSaveChangesInterceptor.cs`);
  transitions emit `OrderStatusChangedEvent` (`OrderEvents.cs:69`).
- **Replay/recovery:** status changes are event-sourced downstream (Mongo projection); the
  SQL row + `xmin` row-version (`ApplicationDbContext.cs:1763-1765`) is the recovery anchor.

## 23. Order Authority Runtime (who can / cannot mutate an order)

| Concern | Sole authorized mutator | Enforcement |
|---|---|---|
| `Order.Status` | authorized lifecycle boundary (fulfillment convergence / status-sync) | `OrderStatusSaveChangesInterceptor` fail-closed |
| `Order.PaymentStatus` | `IPaymentLifecycleService` | `PaymentStatusSaveChangesInterceptor` fail-closed |
| `Order.FulfillmentStatus` | `IOrderFulfillmentService` (or carrier webhook) | `FulfillmentStatusSaveChangesInterceptor` fail-closed |
| `Order.CheckoutState` | the aggregate's own `TransitionCheckoutState` | guarded switch throws on illegal edge |
| Money per SKU | payment lifecycle (allocation) + refund runtime | in-txn allocation; refund CHECK constraints |
| Snapshots (price/addr/etc.) | checkout at creation ONLY | frozen; no re-read path |

Nobody — not a controller, not SuperAdmin, not an admin service — may write a state field
outside its authorized channel. This is the crown-jewel invariant of the runtime.


## 24. Order Snapshot Runtime / 25. Financial Snapshot Runtime
See §8 and §9. Immutability is achieved by (a) frozen entity fields written once at creation,
(b) `PricingSnapshotJson` audit blob, (c) `OrderRefundLine` non-negative CHECK, and (d) the
`AdvanceProjectionMutationVersion` no-regress guard for the read side.

## 26. Audit Runtime
- Every state transition emits an authoritative integration event (`OrderCreatedEvent`,
  `OrderStatusChangedEvent`, `OrderPaymentStatusChangedEvent`, `OrderShippedEvent`,
  `OrderDeliveredEvent`, `OrderCancelledEvent`, all `IAuthoritativeIntegrationEvent`,
  `OrderEvents.cs:13,69,98,116,128,138`).
- The Mongo `OrderDocument` maintains a `Timeline` array appended on each transition
  (`OrderProjectionConsumer.cs:324-342`) — an audit trail projection.
- Financial audit: `PricingSnapshotJson` + `FinancialPostingJournalId` link the order to its
  GL journal; interceptor `LogCritical [SECURITY_VIOLATION]` records unauthorized-write attempts.

## 27. CommerceHub Integration Runtime

- **The storefront `Order` and the marketplace `CommerceHubMarketplaceOrder` are SEPARATE
  aggregates** (`Order.cs:12` vs `CommerceHubMarketplaceOrder.cs:6`). Confirmed: different
  classes, different tables, different lifecycle services, different tenant model (Order is
  `ITenantOwned` + EF filtered; MarketplaceOrder is `IProjectionVersioned` only, NOT tenant
  filtered).
- Marketplace order read model: `Marketplace/OrderReadModelConsumer` →
  `MarketplaceOrderReadModelDocument`. This is the CommerceHub side; it does not mutate the
  storefront `Order`.
- **Divergence:** CommerceHub Order ≠ Merchant `Order`. Settlement/commission/vendor-earning
  flows are CommerceHub-owned (`ICommerceHubFinancialLedgerService`, separate
  `RecordRefundAsync`). Do not conflate the two runtimes.

## 28. Affiliate Integration Runtime

- **Attribution is server-authoritative and frozen at checkout:** `Order.AffiliateAccountId`,
  `AffiliateReferralCode`, `PromotionLinkId` stamped from `HttpContext` via
  `AffiliateModeResolver`, never from the client (`Order.cs:71-95`); guest affiliate checkout
  sets `IsGuestCheckout` (`Order.cs:62-67`).
- **Commission terms frozen per item:** `OrderItem.AffiliateCommission*Snapshot`
  (`OrderItem.cs:123-133`), CHECK-constrained mutually-exclusive
  (eligible-with-snapshots XOR ineligible-without) (`ApplicationDbContext.cs:~1805+`).
  Commission recording MUST read these snapshots, not current policy.
- **Attribution pipeline:** `AffiliateOrderAttributionConsumer` listens to
  `AffiliateOrderPlacedEvent`, resolves v1 (AffiliateAccountId-only) vs v2 (PromotionLinkId)
  and creates the `AffiliateAttribution` row BEFORE payment capture consumers run
  (`AffiliateOrderAttributionConsumer.cs:14-43`). Store: `OrderAttributionStore` /
  `OrderAttributionResolver` (`PointOfSale.Infrastructure/Attribution`).
- **Refund handling:** `OrderItemRefundedEvent` (`Events/Affiliate/OrderItemRefundedEvent.cs`)
  drives commission reversal.
- Order ownership stays with the tenant; affiliate is an attribution overlay, not an owner.

## 29. Notification Integration Runtime

- Order confirmation notification is a **non-fatal side-effect** at checkout stage 9
  (`TryEmitWhatsAppOrderConfirmationAsync`) and via
  `WhatsAppOrderConfirmationRequestedEvent` → `WhatsAppOrderConfirmationConsumer`
  (`Consumers/WhatsApp/`). These events are NOT authoritative — they must never gate ordering
  or version fences. Delivery failure does not fail the order.
- Channels: WhatsApp is evidenced; email/SMS/push are dispatched through the notification
  runtime as best-effort. **FLAG G-3:** only WhatsApp order-confirmation is confirmed in the
  Order slice; other channels are not order-runtime-owned.

## 30. Reporting Integration Runtime / 31. Analytics Integration Runtime

- **Authoritative order state for reporting = SQL** (`Order`/`OrderItem`); reporting reads
  SQL directly, not the Mongo projection. Revenue recognition follows `PaymentStatus`
  (`Collected`/`Settled`) + `FinancialPostingStatus=Posted`; profit uses frozen
  `OrderItem.CostPrice` (`OrderItem.cs:113-116`).
- Analytics: `Order.SessionId` is a reference into the analytics domain
  (`Order.cs:42-46`); `OrderAttribution`/`OrderItemPixelInfo`
  (`PointOfSale.Core/Analytics/`) carry attribution to analytics. Analytics events are
  non-authoritative and eventually consistent — they never feed back into order truth.

## 32. CQRS — Write Models, Read Models, Projection Usage

**Write model (authority):** PostgreSQL `Orders`/`OrderItems`/`OrderRefunds`/`OrderRefundLines`,
tenant-filtered (`ApplicationDbContext.cs:622-627`), optimistic `xmin` concurrency
(`:1763-1765`). Written ONLY through the authorized lifecycle services + checkout.

**Read model:** MongoDB `orders` collection of `OrderDocument`
(`OrderProjectionConsumer.cs:65`). Read by `StorefrontOrdersReadController`,
`StorefrontAccountController` via `IOrderReadRepository`. NOT authoritative.

**Projection consumer** (`OrderProjectionConsumer`):
- Handles `OrderCreatedEvent` (insert, `:186-260`), `OrderStatusChangedEvent`,
  `OrderPaymentStatusChangedEvent`, `OrderShippedEvent`, `OrderDeliveredEvent`,
  `OrderCancelledEvent` (facet updates).
- **EventVersion validation:** rejects non-positive `EventVersion` at insert
  (`:151-158`).
- **Per-facet freshness fences** (stale-event rejection): `BuildFreshFacetEventFilter`
  (`:986-1017`) only applies an update when the incoming event is newer than the stored
  facet version/timestamp (separate fences for overall / status / payment / shipment facets,
  `OrderDocument.Last*EventVersion/OccurredAtUtc`, indexes `:106-120`). `.Max(...)` operators
  ensure monotonic version/timestamp advance (`:337-341`).
- **Repair-on-miss:** when a facet update matches no document, `HandleNoOrderProjectionMutationMatchAsync`
  triggers reconciliation (`:346-356`).

**SQL-authority repair-on-read:** `StorefrontOrderProjectionRepairService` rebuilds the
projection on demand from SQL truth via `StorefrontOrderProjectionAuthoritySnapshotLoader`
+ `StorefrontOrderProjectionBuilder` (`StorefrontOrderProjectionRepairService.cs:152-184`);
`StorefrontOrderProjectionReconciliationService` is the background reconciler. SQL always wins.

**Outbox / publishing:** the generic transactional outbox `OutboxMessage`
(`PointOfSale.Core/Entities/OutboxMessage.cs`) is drained by `OutboxPublisherJob`
(`PointOfSale.Infrastructure/Jobs/OutboxPublisherJob.cs`): critical order events are ordered
by `(TenantId, AggregateId, EventVersion)` so a stale version cannot overtake newer state
(`:81-88`); exhausted critical rows go to operator reconciliation, NOT terminal dead-letter
(`:74-79`, `CriticalPoisonSlowReplayInterval=15min :33`). `OrderRequestIdempotency` provides
request-level dedup (snapshot-hash variant migration `20260419113000`).

**Replay/consistency:** monotonic `ProjectionMutationVersion` (`Order.cs:162-180`) + Mongo
facet fences + SQL-authority repair make projection replay convergent and fail-closed; the
Mongo doc can always be rebuilt from SQL.


---

## 33. Runtime Authority Matrix

For each concept: Business Owner / Mutation Authority / Read Authority / Projection Authority
/ Runtime Truth.

| Concept | Business Owner | Mutation Authority | Read Authority | Projection Authority | Runtime Truth |
|---|---|---|---|---|---|
| Cart | Customer (session) | Redis `CartService` | storefront | (none; not projected) | Redis cache |
| Checkout | — | `CheckoutService` (one-shot) | caller | `OrderProjectionConsumer` (post-commit) | SQL txn (transient) |
| Order record | Tenant | Checkout (create) + authorized lifecycle svcs | all read ctrls | Mongo `OrderDocument` | **SQL `Orders`** |
| `Order.Status` | Order lifecycle | `IOrderFulfillmentService`/`IOrderStatusSyncService` (authorized ctx) | read ctrls | Mongo status facet | **SQL `Orders.Status`** |
| `Order.PaymentStatus` | Payment lifecycle | `IPaymentLifecycleService` | read ctrls | Mongo payment facet | **SQL** |
| `Order.FulfillmentStatus` | Fulfillment svc | `IOrderFulfillmentService`/carrier webhook | read ctrls | Mongo shipment facet | **SQL** |
| `Order.CheckoutState` | Order aggregate | `Order.TransitionCheckoutState` | read ctrls | Mongo (adjacent) | **SQL** |
| Money (per SKU) | Payment lifecycle | `IPaymentLifecycleService` (alloc/refund) | read ctrls | derived | **SQL `OrderItem`** |
| Inventory | Inventory runtime | `InventoryConsistencyService` | storefront/ops | Mongo stock doc | **SQL `ProductVariantStock`** |
| Shipping/Shipment | Fulfillment/Carrier | `IOrderFulfillmentService`/carrier | storefront | Mongo shipment doc | **SQL `Orders` + shipment** |
| Customer identity | Customer domain | customer svc (creation) | read ctrls | Mongo order doc | **SQL `Orders.Customer*/Guest*`** |
| Merchant/tenant | Tenant domain | tenant admin | read ctrls | — | **SQL tenant config** |
| CommerceHub order | CommerceHub | `CommerceHub*Authority` svcs | CommerceHub ctrls | `commercehub_orders` | **SQL `CommerceHub*`** (separate) |
| Affiliate | Affiliate runtime | affiliate svc | portal | `affiliate_orders` + order doc | **SQL `AffiliateAttribution`/`Ledger`** |
| Refund | Payment lifecycle | `IPaymentLifecycleService.RecordRefundAsync` | read ctrls | Mongo + ledger | **SQL `OrderRefund(s)`** |
| Receipt | POS `SalesInvoice` | POS register svc | POS | — | **SQL `SalesInvoice`** (separate) |
| Reporting | Analytics/finance | (read-only; none) | finance ctrls | — | **SQL `Orders`** |
| Analytics | Analytics domain | (derived) | dashboards | Mongo analytics docs | **SQL (source)** |

## 34. Runtime Authority Divergence

Documented, implementation-observed divergences where a derived state must NOT be trusted as
business truth:

- **Cart ≠ Order.** Cart is Redis/mutable/best-effort; order is SQL/immutable-at-commit.
  Cart price/quantity/total never trusted past checkout stage 2 (re-authored from SQL).
- **Checkout ≠ Order.** Checkout is a transient runtime that ends at the Serializable commit;
  the order is the durable record. No checkout state leaks into order authority beyond the
  created snapshot.
- **Inventory Reservation ≠ Inventory.** Reservation (held during `PendingPaymentIntent`) is a
  sub-state of `ProductVariantStock`; the order only references `CartId`/`InventoryReservedEvent`.
  Oversell prevention = authoritative stock read + Serializable + SQL-wins divergence.
- **Payment ≠ Order.** `PaymentStatus` lives ON the order but its *transitions* are owned by
  `IPaymentLifecycleService`; a provider can never set it. The order is the system of record,
  not the authority.
- **CommerceHub Order ≠ Merchant Order.** Separate aggregate, separate table, separate
  tenant model, separate GL/commission. The `marketplace_order_read_models` Mongo doc is a
  projection BUILT FROM the storefront `Order` (`MarketplaceOrderReadModelProjectionService`
  queries `Orders` by `Id`), not from `CommerceHubMarketplaceOrder` — so the customer-facing
  marketplace view IS the storefront order's projection, while settlement/commission is the
  CommerceHub side. Reconcile via `Order.Id`, never conflate totals.
- **Order Total ≠ Settlement Total.** `Order.Total` is customer-facing; CommerceHub settlement
  total (fees/commission/vendor earning) is a different figure owned by CommerceHub. No code
  asserts equality across the two.
- **Customer Address ≠ Order Address.** The frozen `ShippingAddress`/geo place-ids on the order
  (snapshot at checkout) can diverge from a live customer-profile address change — by design
  (historical immutability). Reporting/archival uses the order's frozen address.
- **Projection Total drift (risk).** `OrderDocument.Total` is rebuilt from SQL on repair
  (no compile/runtime invariant asserts equality between reads); the 30s reconciliation sweep
  + on-demand `Ensure*Projected` are the only convergence. See FU-4.

## 35. Cross-Domain Authority Graph

Reads / Writes / Dependencies / Synchronization per integration.

- **Identity.** Order READS `Customer`/`OnlineCustomer` identity, WRITES only a frozen FK +
  guest fields. Writes customer profiles via customer domain. Sync: none (frozen).
- **Currency.** Order READS tenant/storefront currency config at checkout; WRITES frozen
  `Currency`. No downstream currency writes.
- **Pricing.** Order READS `ProductVariant`/`promotion` SQL at checkout; WRITES frozen
  `PricingSnapshotJson` + line prices. No re-read. Sync: replay-safe only while snapshot
  intact.
- **Inventory.** Order does NOT own stock. READS `ProductVariantStock` for validation;
  WRITES nothing to stock — `InventoryConsistencyService` owns reservation/deduction within
  the checkout txn. Events: `InventoryReservedEvent`/`InventoryReleasedEvent`.
- **Payments.** Order READS payment-intent state; WRITES `PaymentStatus` only via
  `IPaymentLifecycleService`. Payment providers WRITE into the lifecycle service only.
  Sync: accounting outbox + `FinancialPostingStatus` gate.
- **CommerceHub.** Order WRITES nothing to CommerceHub. CommerceHub consumes storefront order
  events to build `marketplace_order_read_models`; CommerceHub OWNS settlement/commission via
  its own ledger. Sync: `Marketplace.*` events keyed by `Order.Id`.
- **Affiliate.** Order WRITES frozen `AffiliateAccountId`/commission snapshots at checkout;
  affiliate runtime reads those + consumes `AffiliateOrderPlacedEvent`→`AffiliateAttributedEvent`
  → `OrderItemRefundedEvent`. Sync: event-driven, commission recomputed from SQL ledger on
  projection repair.
- **Shipping.** Order WRITES `FulfillmentMode`/tracking; fulfillment svc + carrier webhooks
  WRITE `FulfillmentStatus`. Sync: `OrderShipmentOrchestrationConsumer` + status-sync consumer.
- **Notifications.** Order emits `WhatsAppOrderConfirmationRequestedEvent` (non-authoritative).
  One-way; never reads back.
- **Reporting / Analytics.** Read SQL only; no writes to orders. Analytics WRITES to its own
  domain keyed by `SessionId`/`AffiliateAttribution`.
- **Search.** (Out of current scope; not order-runtime-owned.)
- **Storefront / POS.** Storefront creates via CheckoutService; POS creates
  `SalesInvoice`/POS orders via a DIFFERENT aggregate path. Sync: none (separate aggregates).

## 36. Business Invariants (implementation-backed only)

- **BI-1 — One mutator per state field (fail-closed).** Each of `Status`/`PaymentStatus`/
  `FulfillmentStatus` can be set only through its authorized service behind a
  `SaveChangesInterceptor`; unauthorized writes throw `InvalidOperationException`
  (`OrderStatusSaveChangesInterceptor.cs:135-146`, `PaymentStatusSaveChangesInterceptor.cs:103-108`,
  `FulfillmentStatusSaveChangesInterceptor.cs:93-97`).
- **BI-2 — Orders never lose currency.** `Order.Currency` is frozen at creation
  (`Order.cs:418-421`) and every money field denominated in it.
- **BI-3 — Orders preserve the customer snapshot.** Identity + guest fields + geo place-ids
  are frozen at commit and never re-read from live profile (`Order.cs:29-67,293-351`).
- **BI-4 — Payment never owns the Order.** `PaymentStatus` transitions are owned by
  `IPaymentLifecycleService`, not payment providers or the order header
  (`PaymentStatusSaveChangesInterceptor.cs`).
- **BI-5 — Inventory never trusts the Cart.** Stock is re-validated from SQL inside the
  Serializable order txn; cart quantity is advisory only (§ Checkout stage 4/6.x).
- **BI-6 — Money is tracked per SKU.** `OrderItem.PaidAmount`/`RefundedAmount` are the ledger;
  order totals are derived (`OrderItem.cs:154-186`, `Order.cs:557-605`).
- **BI-7 — Historical totals are immutable.** Header subtotal/discount/tax/shipping/total are
  frozen; refunds adjust only `RefundedAmount`, never the original `Total`
  (`Order.cs:379-416`, refund path §19).
- **BI-8 — Fulfillment cannot outrun the ledger.** Outbound fulfillment is blocked until
  collection accounting is `Posted` (`OrderFulfillmentService.cs:643-661`).
- **BI-9 — SQL is truth.** Mongo `OrderDocument` is repair-rebuildable from SQL and rejects
  stale events via facet version fences (`OrderProjectionConsumer.cs:986-1017`);
  `StorefrontOrderProjectionRepairService` rebuilds from SQL.
- **BI-10 — Tenant isolation at the DB.** Orders are under the EF global query filter
  (`ApplicationDbContext.cs:622-627`); cross-tenant reads are structurally impossible.

## 37. Architectural Decisions (WHY, not HOW)

- **AD-1 — Why four state machines, not one.** Payment, fulfillment and the customer-facing
  status advance on different, occasionally-independent timelines (e.g. capture before ship;
  delivered-before-settled; COD paid on delivery). A single enum cannot express orthogonal
  progress without illegal in-between states. Encapsulated per-concern mutators + a converged
  rollup (`OrderStatus`) give both correctness and a clean customer view.
- **AD-2 — Why snapshots exist.** Catalog prices, currency, addresses, shipping methods and
  affiliate commission TERMS change after sale. Freezing them at commit makes the order a
  legally/ financially coherent historical record and prevents retroactive drift from
  breaking revenue/commission/refund math (frozen `OrderItem`/`PricingSnapshotJson`/`Address`).
- **AD-3 — Why reservations exist.** Payment is asynchronous; without a reservation, two
  concurrent checkouts could oversell. Reserving inside the SAME Serializable order txn makes
  stock and order commit atomically (no split-brain).
- **AD-4 — Why orders become immutable.** Once `Collected`/`Settled`, money + commission +
  accounting are recognized; mutating historical totals would corrupt GL, tax and affiliate
  payouts. Immutability (enforced by frozen fields + trigger on `FulfillmentMode` +
  refund-only `RefundedAmount`) protects downstream ledgers.
- **AD-5 — Why checkout disappears.** Checkout is a coordination/validation concern, not a
  persistent state. Persisting it would duplicate the order and invite divergence. It ends
  at commit; the order is the durable artifact.
- **AD-6 — Why the cart isn't authority.** Cart is a cache of intent, mutable by the browser
  and not transactional. Trusting it for price/stock/total would enable tampering and race
  losses. SQL is re-read at checkout for every authoritative figure.
- **AD-7 — Why SQL is the single write model.** Strong consistency, tenant isolation, ACID
  money, and fail-closed interceptors require a relational system of record; Mongo/Redis are
  derived and rebuildable.
- **AD-8 — Why fail-closed interceptors.** A missing service call should NEVER silently set a
  money/fulfillment state. The `SaveChanges` boundary is the cheapest, last-line, always-on
  guard regardless of which controller/service initiated the change.

## 38. Technical Debt (implementation-backed findings)

- **TD-1 — `OrderStatus` has no single canonical state-machine.** Transition rules are
  duplicated across `OrderStatusSyncService.IsValidStatusProgression` and
  `OrderFulfillmentService.ResolveOrderStatus`/`GetOrderProgressionRank`; unlike
  `FulfillmentStatus.GetAllowedTransitions` there is no `OrderStatus.CanTransitionTo`. Risk:
  the two can drift. (`OrderFulfillmentService.cs:759-800` vs `OrderStatusSyncService.cs:466-492`.)
- **TD-2 — `FulfillmentMode` immutability not in the migration pipeline.** Trigger/CHECK live
  only in the standalone `add_fulfillment_ownership_snapshot.sql` with NO CI/DbPatcher wiring;
  the EF model snapshot lacks them. Environments that missed the script are unprotected.
- **TD-3 — No storefront chargeback handling.** `PaymentLifecycleService` has no chargeback
  transition for the storefront `Order`; only CommerceHub runtimes handle chargeback. A
  storefront chargeback (e.g. Stripe dispute) has no in-order state.
- **TD-4 — No customer/storefront self-cancellation path.** Only merchant (admin) +
  automatic (payment/checkout-failure) cancellations exist; no storefront cancel endpoint
  was found. Customer-initiated cancellation is currently unsupported in code.
- **TD-5 — `OrderFulfillmentCancelledEvent` appears unused / `CancelAsync` is a dead-end.**
  `IOrderFulfillmentService.CancelAsync` deliberately refuses
  (`ORDER_CANCELLATION_LIFECYCLE_REQUIRED`); fulfillment `Cancelled` target is reachable in
  the enum but no caller exercises it. Dead/unreachable path.
- **TD-6 — Mongo `orders` doc has no checksum.** Only the CommerceHub doc has one. Storefront
  repair relies on `ComputeAuthorityFingerprint` JSON equality, which excludes freshness/Timeline
  fields (re-upserts on version-only difference). Acceptable but weaker than a checksum.
- **TD-7 — Projection Total vs SQL Total not asserted.** No runtime/compile invariant enforces
  `OrderDocument.Total == Order.Total`; convergence depends on the 30s sweep + on-demand repair.
- **TD-8 — Commission double-source edge race.** `OrderDocument.AffiliateCommissionAmount` is
  set by both event projection and SQL-authority snapshot on repair; a stale repair using the
  whole-doc `LastEventVersion` freshness guard (not the commission facet) could reset commission
  in a narrow race window.
- **TD-9 — Legacy `Order.IsPaid` is deprecated but still written.** `Order.cs:531` warns it may
  be out-of-sync; `PaymentStatus.Paid`/`PartiallyPaid` are `[Obsolete]`. Item-level tracking is
  the truth. Lingering dual model.
- **TD-10 — `OrderDeliveredEventConsumer.cs` filename vs class `FulfillmentDeliveredEventConsumer`**
  consuming `FulfillmentDeliveredEvent` — misleading naming (behavior correct).

## 39. Implementation Follow-up Items

| ID | Title | Current Implementation | Why | Future Improvement | Priority | Status |
|---|---|---|---|---|---|---|
| FU-1 | Canonical `OrderStatus` state machine | Rules duplicated in sync svc + fulfillment svc | drift risk | extract `OrderStatus.CanTransitionTo` + central validator | High | Open |
| FU-2 | Storefront chargeback | none | disputes unhandled in order | add `PaymentLifecycleService` chargeback transition + suspense | High | Open |
| FU-3 | `FulfillmentMode` immutability in migrations | standalone `.sql` only, no CI wiring | unpatched envs unprotected | port trigger/CHECK into EF migration + add to DbPatcher | High | Open |
| FU-4 | Assert projection Total == SQL Total | 30s sweep + repair only | silent drift | add convergence test / checksum on `Total` | Med | Open |
| FU-5 | Storefront self-cancellation | merchant + auto only | no customer cancel | add authorized storefront cancel path | Med | Open |
| FU-6 | Dead `OrderFulfillmentCancelledEvent` / `CancelAsync` | `CancelAsync` refuses | dead code | wire or remove | Low | Open |
| FU-7 | Storefront projection checksum | JSON fingerprint only | weaker than CH | add `OrderProjectionChecksum.Compute` for storefront doc | Low | Open |
| FU-8 | Commission double-source race | event + repair both write | narrow reset race | repair uses commission facet freshness guard | Med | Open |
| FU-9 | Legacy `IsPaid`/`Paid` enum | deprecated but live | dual model | fully migrate to item-level + remove legacy | Low | Open |

## 40. Complete Order Lifecycle (one full order, implementation-evidenced)

```
Customer (auth/guest)
  │  IsGuestCheckout derived from auth (CheckoutService.cs:5448-5449); affiliate stamped server-side (Order.cs:78-95)
  ▼
Cart (Redis)  ── immutable snapshot @ checkout stage 2; prices discarded
  ▼
Checkout (one-shot runtime, ~8.7k LOC)  stages 0→9 (see §7 table)
  │  • price re-authored from SQL (stage 4)   • currency locked (5.5)
  │  • inventory reserved IN Serializable txn (6.x)   • coupon reserved (6.x)
  ▼
Validation  — caller ownership, storefront-accepting gate, dup active-order guard (2.7)
  ▼
Order Creation — CreateOrderAsync (CheckoutService.cs:5427) in 1 Serializable txn
  │  Status=Pending, PaymentStatus=Pending, CheckoutState=PendingPaymentIntent, FulfillmentStatus=Created
  │  snapshots frozen (pricing/address/currency/payment-method/fulfillment-mode/affiliate)
  │  OrderCreatedEvent → outbox → OrderProjectionConsumer inserts Mongo doc
  ▼
Payment Intent (checkout stage 7) — provider adapter only; lifecycle owns state
  │  On success → IPaymentLifecycleService.HandleSuccessAsync → Collected + per-item PaidAmount
  │  Collection accounting outbox → Posted (FinancialPostingStatus=Posted)
  │  On failure/expiry → quarantine/cancel + inventory+ coupon release + OrderCancelledEvent
  ▼
Receipt — WhatsAppOrderConfirmationRequestedEvent (NON-authoritative, non-fatal, stage 9)
  ▼
Fulfillment — IOrderFulfillmentService transitions (row-locked, money-gated):
  Created→Confirmed→Packed→OutForDelivery→Delivered→Completed
  │  OrderShipmentOrchestrationConsumer creates/links shipment (defers until accounting posted)
  │  Delivered → COD: MarkOfflineCollectionDeliveredAsync (ledger); Wallet: approval gate
  │  Each step converges Order.Status via OrderStatusSaveChangesInterceptor authorized ctx
  ▼
Shipping — Manual admin or carrier webhook (mutually exclusive by FulfillmentMode)
  │  tracking/carrier stamped; OrderShippedEvent/DeliveredEvent → projection
  ▼
Delivery — MarkDeliveredAsync; FulfillmentDeliveredEvent → payment lifecycle (COD collect)
  ▼
Reporting — finance reads SQL (Collected/Settled + Posted); profit from frozen CostPrice
  ▼
Refund (if needed) — RecordRefundAsync (only from Collected/Settled/PartialRefunded)
  │  over-refund blocked; per-item RefundedAmount bump; GL posted from SQL truth
  │  OrderItemRefundedEvent → affiliate commission reversal
  ▼
Archive — soft-delete (IsDeleted) under tenant filter; terminal FulfillmentStatus Completed/Returned
```

**Authority:** each segment above has exactly one owner (cart→Redis; checkout→CheckoutService;
payment→IPaymentLifecycleService; fulfillment→IOrderFulfillmentService; status→converged in
authorized ctx; money→item-level via lifecycle; accounting→ICommerceAccountingService).
**Synchronization:** Mongo projection (facet fences), 30s reconciliation, on-demand repair,
outbox ordered by EventVersion. **Replay:** idempotent handlers (`wasIdempotent`) + freshness
guards + request idempotency. **Recovery:** SQL is the anchor; projection rebuildable;
critical outbox poison → operator reconciliation (not dead-letter). **Failure behaviour:**
fail-closed interceptors, quarantine on amount/late mismatch, checkout auto-cancel on
unrecoverable intent gap with inventory/coupon release. **Consistency:** Serializable checkout
txn + row locks in fulfillment + SQL-wins divergence = strong consistency on the write path,
eventual-but-reconciled on the read path.

## 41. Most Important Question — ANSWERED

**"How does Shopixy transform a customer's purchase intent into a historically immutable,
financially authoritative, inventory-safe, payment-consistent Order while coordinating every
participating runtime without allowing any derived state to become business truth?"**

Shopixy does it by treating the `Order` not as a record but as an **Order Lifecycle Runtime**
with four encapsulated, checked-by-interceptor state machines, and by enforcing a strict
truth hierarchy:

1. **Intent → commitment.** The mutable, cache-resident cart is snapshotted and then
   **thrown away as authority**: at checkout every price, stock level and currency figure is
   re-read from SQL inside a single Serializable transaction, so the order is born from
   authoritative data, not from client-supplied intent.
2. **Freeze to make history immutable.** At the moment of commit the order snapshots prices,
   currency, addresses (with geo place-ids), shipping & payment methods, variant attributes and
   affiliate commission terms. From that point the order is a self-contained historical record;
   live catalog/policy changes cannot retroactively alter it.
3. **One writer per concern, enforced at the DB boundary.** `OrderStatus`, `PaymentStatus` and
   `FulfillmentStatus` each have exactly one authorized service, and a `SaveChangesInterceptor`
   fails closed on any other writer. This is how payment, fulfillment and shipping can each
   advance independently without ever corrupting each other's state — and why a payment provider
   or a controller can never silently set money/fulfillment state.
4. **Money is per-SKU and ledger-gated.** Revenue is recognized only at `Collected`/`Settled`,
   each transition is coupled to a SQL-authoritative accounting outbox that must be `Posted`
   before fulfillment can ship, and refunds adjust only the immutable ledger's `RefundedAmount`.
   Outbound money is gated on `FinancialPostingStatus`, so the order can never be shipped,
   settled or recognized ahead of its ledger.
5. **Inventory safety by atomic reservation.** Stock is reserved inside the SAME Serializable
   checkout transaction and released on any failure/cancel, so oversell and split-brain between
   cart and warehouse are structurally impossible; the order only coordinates, it never owns stock.
6. **Coordination without truth-leak.** Every participating runtime (cart, pricing, currency,
   inventory, payments, accounting, fulfillment, shipping, affiliate, notifications, commercehub,
   reporting, analytics) communicates through **authoritative integration events** consumed into
   **derived** Mongo projections. Those projections carry per-facet version fences and are
   rebuildable from SQL on demand; non-authoritative side-effects (WhatsApp confirmations) are
   explicitly excluded from version/tenant ordering and can never gate the order. SQL `Orders` is
   the single runtime truth; nothing derived ever flows back as authority.

In one sentence: **Shopixy converts intent into an immutable, SQL-authoritative Order by
re-deriving all truth from SQL at commit, freezing a historical+financial snapshot, isolating
each lifecycle axis behind a single fail-closed writer, and treating every other runtime as a
derived projection that can be rebuilt from SQL — so derived state can never become business
truth.**

## 42. Architectural Synthesis — what an Order actually IS

**Classification: ORDER LIFECYCLE RUNTIME / Domain Coordinator (within the commerce bounded
context).**

Evidence summary that forces this classification:
- It is an aggregate (`Order.cs:12`) that **owns** a record, but its defining behaviour is the
  coordination of **four orthogonal state machines** (`OrderStatus`/`PaymentStatus`/
  `FulfillmentStatus`/`CheckoutState`) each guarded by a fail-closed interceptor — a runtime,
  not a passive entity.
- It **coordinates** cart, pricing, currency, inventory, payments, accounting, fulfillment,
  shipping, affiliate, notifications, commercehub, reporting and analytics through events and
  authoritative boundaries — the role of a Domain Coordinator.
- It is the **system of record** for tenant commercial history (SQL, tenant-filtered,
  optimistic concurrency) — a Lifecycle Runtime, not a transaction wrapper.

Why every competing classification is incomplete:
- **"Just an entity"** — wrong: it carries encapsulated state machines + interceptor-enforced
  authority; passivity is exactly what it is NOT.
- **"Checkout Runtime"** — wrong: checkout is a one-shot upstream runtime that *produces* the
  order and then disappears (§7); the order's life (payment, fulfillment, refund) is far longer
  and separate.
- **"Transaction Runtime"** — incomplete: payment is only one of four axes; the order also owns
  fulfillment, shipping and the customer-facing rollup.
- **"Commerce Runtime"** — incomplete: it is the *tenant storefront* commerce runtime, but it is
  explicitly DISTINCT from the CommerceHub marketplace runtime and the POS `SalesInvoice`
  register (separate aggregates). "Commerce Runtime" over-generalizes across bounded contexts.
- **"Financial Runtime" / "Fulfillment Runtime"** — each captures only one axis; the whole is
  the composition.
- **"Business Orchestrator"** — close, but "orchestrator" implies it drives the other services;
  in reality it mostly *owns the record and converges status*, while payment/fulfillment/accounting
  own their own transitions. It is a **coordinator with authoritative state**, i.e. an Order
  Lifecycle Runtime.

**Conclusion:** The canonical name is **Order Lifecycle Runtime** — a SQL-authoritative,
tenant-isolated aggregate that coordinates every participating runtime via authoritative events
and fail-closed, per-axis mutation authority, while preserving an immutable historical +
financial snapshot for its entire life.

## 43. Certification

This document is certified as **implementation-backed** as of its creation date. Every
architectural claim is cited to `file:line` in `PointOfSale.Core`, `PointOfSale.Infrastructure`
and `PointOfSale.API`. Where a comment/name asserted a behaviour not present in code, the gap is
recorded as a finding (TD-2, FU-3: `FulfillmentMode` trigger exists in standalone SQL but not in
the EF migration pipeline / CI; TD-3: no storefront chargeback; TD-4: no storefront cancellation).
Out-of-scope aggregates (CommerceHub `CommerceHubMarketplaceOrder`, POS `SalesInvoice`) are
explicitly flagged as separate runtimes. Confidence: **HIGH** for all in-scope claims; residual
risk confined to the follow-up items (FU-1…FU-9) and the verification gap in TD-2 (whether the
standalone fulfillment-ownership SQL was applied in target environments).
