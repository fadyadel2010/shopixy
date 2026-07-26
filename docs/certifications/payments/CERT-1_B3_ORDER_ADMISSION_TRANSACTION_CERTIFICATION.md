# CERT-1 B3 Order Admission & Transaction Certification

Certification: CERT-1  
Phase: B  
Control: B3 - Order Admission & Transaction Certification  
Scope: Storefront Order Admission only  
Certification Date: 2026-07-20  
Source of Truth: Source code implementation only

## Final Certification

PASS

Order Admission is implemented as a backend-owned, SQL-transactional authority boundary in `CheckoutService.CreateOrderAsync`. An order officially exists only after the serializable checkout transaction commits. Before that commit, the order object and order items are in-memory or pending EF state only.

Order Admission owns the local admission transaction, final authority revalidation, inventory reservation, coupon and offer reservation, order and order-item persistence, payment-intent authority seeding, idempotency order binding, and transactional outbox staging. Provider runtime execution, payment settlement, refunds, CommerceHub, Marketplace settlement, and fulfillment shipment execution are outside this boundary.

No architectural violations were identified.

## Source Evidence

- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.API/Services/Checkout/CheckoutRequestIdempotencyCoordinator.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
- `PointOfSale.Infrastructure/Data/Repositories/ProductVariantStockRepository.cs`
- `PointOfSale.Infrastructure/Discounts/CouponValidationService.cs`
- `PointOfSale.Infrastructure/EventBus/MassTransitEventPublisher.cs`
- `PointOfSale.API/Program.cs`
- `PointOfSale.Infrastructure/Data/Configurations/InventoryReservationConfiguration.cs`
- `PointOfSale.Infrastructure/Data/Configurations/ProductVariantStockConfiguration.cs`
- `PointOfSale.Infrastructure/Data/Configurations/OutboxMessageConfiguration.cs`
- `PointOfSale.Infrastructure/Data/Configurations/StockSyncOutboxMessageConfiguration.cs`
- `PointOfSale.Core/Entities/Order.cs`
- `PointOfSale.Core/Entities/OrderItem.cs`
- `PointOfSale.Core/Entities/InventoryReservation.cs`
- `PointOfSale.Core/Entities/CouponReservation.cs`
- `PointOfSale.Core/Entities/PaymentIntent.cs`
- `PointOfSale.Core/Entities/OrderRequestIdempotency.cs`
- `PointOfSale.Tests/Architecture/VerifiedSafeFlowsArchitectureTests.cs`
- `PointOfSale.Tests/Architecture/StorefrontStockAuthorityArchitectureTests.cs`
- `PointOfSale.Tests/Architecture/StorefrontCheckoutSubmissionAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/EventOutboxConsistencyArchitectureTests.cs`

## Executive Summary

An order officially exists when `CheckoutService.CreateOrderAsync` completes its serializable SQL transaction with `transaction.CommitAsync(cancellationToken)`. The implementation sets `orderCommitted = true` only after `CreateOrderAsync` returns to `ProcessCheckoutAsync`, which means admission is considered complete only after the order transaction has committed.

Order Admission is owned by `CheckoutService.CreateOrderAsync`. The public checkout flow in `ProcessCheckoutAsync` validates caller, cart, pricing, shipping, payment method, phone/geography, lock ownership, idempotency, and expected pricing before entering order creation. `CreateOrderAsync` then repeats authority-sensitive validation inside the final transaction before admitting the order.

Inside the admission transaction, implementation performs final storefront/order availability validation, revalidates checkout authority, refreshes order/order-item snapshots, reserves inventory, persists the order and items, binds inventory reservations to order items, seeds local payment-intent authority when applicable, creates affiliate attribution where applicable, reserves coupon/offer capacity, stages order and inventory outbox evidence, binds the idempotency record to the order, saves changes, and commits.

Admission finishes at the transaction commit. Post-commit code performs non-admission work, including durable outbox existence checks, Redis reservation tracking, velocity tracking, provider payment-session creation, checkout state progression for payment, idempotency response completion, and optional notification side effects.

## Transaction Boundary

Transaction start is implemented in `CheckoutService.CreateOrderAsync`:

`await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);`

Transaction end is implemented by `await transaction.CommitAsync(cancellationToken);`. On failure, `CreateOrderAsync` clears the in-memory idempotency binding, nulls the local reservation result, calls `transaction.RollbackAsync(CancellationToken.None)`, and rethrows or converts known serializable races into retry/checkout concurrency exceptions.

Serializable isolation is explicitly requested for each order-admission attempt. Serialization failures are detected by `IsCheckoutTransactionSerializationFailure`, clear EF tracking, retry up to `CheckoutAuthorityCommitMaxRetries`, then fail closed with `CheckoutConcurrencyConflictException`.

Atomicity is implemented by sharing the same `ApplicationDbContext` transaction across:

- Order and order-item inserts.
- Inventory stock `ReservedQuantity` mutations.
- `InventoryReservation` rows.
- `StockSyncOutboxMessage` rows written by the stock repository.
- `PaymentIntent` authority seeding.
- `CouponReservation` rows.
- Offer `PromotionUsage` reservation rows.
- Affiliate attribution rows.
- MassTransit EF bus outbox rows staged through `_eventPublisher`.
- Explicit `OutboxMessages` rows for `InventoryReservedEvent`.
- Marketplace event outbox rows.
- `OrderRequestIdempotency` order binding.

Rollback behavior is implementation-backed. If any exception occurs before commit, the transaction rolls back these SQL changes. The inventory repository does not open a separate transaction when `Database.CurrentTransaction` already exists, so its stock and reservation mutations participate in the caller's checkout transaction.

## Order Admission Pipeline

The implemented Order Admission pipeline is:

1. Authoritative checkout pricing is recalculated.
2. Payment-method amount limits and expected pricing are validated.
3. Order and order items are constructed as immutable authority snapshots.
4. Serializable SQL transaction begins.
5. Storefront accepting-orders authority is locked and rechecked.
6. Cart, shipping method, payment method, pricing, promotion, and affiliate authority are revalidated inside the transaction.
7. Order and order-item snapshots are refreshed from transaction-scoped authority.
8. Inventory is validated and reserved through `ValidateAndReserveInventoryAsync`.
9. Order number is generated.
10. Order and order items are added and saved.
11. Inventory reservations are bound to persisted order items.
12. Transactional payment-intent authority is seeded for supported provider/manual-wallet methods.
13. Affiliate attribution is created when affiliate authority exists.
14. Coupon reservation is created when a coupon is applied.
15. Offer promotion reservation is created when an offer is applied.
16. Order-created events are staged through the MassTransit EF outbox and marketplace outbox.
17. `InventoryReservedEvent` is staged in explicit `OutboxMessages`.
18. Idempotency record is bound to the order before commit.
19. Changes are saved.
20. Transaction commits.
21. Post-commit checks and runtime handoff work proceed outside Order Admission.

## Inventory Reservation

Inventory reservation is owned by `CheckoutService.ValidateAndReserveInventoryAsync` and `ProductVariantStockRepository`.

Reservation authority is SQL `ProductVariantStocks`, not browser inventory or Mongo projections. Checkout loads authoritative variant availability through `GetAvailableQuantitiesAsync`, reloads product/variant salability from SQL, and skips reservation only for non-stock-tracked or backorderable products.

Reservation rows are initially created by `ProductVariantStockRepository.ReserveAsync` with `OrderId`, placeholder `OrderItemId = Guid.Empty`, `VariantId`, `WarehouseId`, `ReservedQuantity`, active status, event id, and expiry. After order items are saved, `BindOrderReservationsAsync` assigns those reservations to the concrete `OrderItem.Id` and product id.

Rollback is transaction-backed. During `CreateOrderAsync`, repository mutation methods run inside the active checkout transaction. If order admission rolls back, stock reserved quantities, reservation rows, stock-sync outbox rows, and order rows roll back together.

Release behavior exists outside successful admission. `ReleaseOrderReservationsAsync` releases remaining reserved quantity for an order, decrements stock `ReservedQuantity`, updates reservation released quantity/status, and writes stock-sync outbox messages. `ValidateAndReserveInventoryAsync` also compensates partial reservation loops with `ReleaseInventoryReservationAsync` if reservation mutation has begun and a later reservation step fails.

Overselling protection is implemented by:

- Serializable checkout transaction.
- Repository-level row locks using PostgreSQL `FOR UPDATE` or SQL Server `UPDLOCK, ROWLOCK, HOLDLOCK`.
- Available quantity checks using `Quantity - ReservedQuantity`.
- Invariant checks before save.
- Database check constraint `CK_ProductVariantStocks_ReservedQuantity_Within_Quantity`.
- Reservation check constraint `CK_InventoryReservations_ReservedFlow_Valid`.

## Coupon Reservation

Coupon reservation is owned by `CouponValidationService.ReserveCouponAsync`, called from `CreateOrderAsync` inside the checkout transaction.

`ReserveCouponAsync` refuses to run without an open SQL transaction for relational providers. It locks the coupon row for capacity mutation, revalidates coupon authority by id/code/customer/snapshot, and rejects invalid or stale coupon authority before inserting a reservation.

Reservation ownership is tenant/order/customer/coupon scoped. `CouponReservation` persists `TenantId`, `CouponId`, `CustomerId`, `OrderId`, `Status`, `CorrelationId`, and reservation timestamps.

Duplicate protection is implemented by:

- Unique active reservation index on `{ TenantId, OrderId }`.
- Unique active reserved-customer index on `{ TenantId, CouponId, CustomerId }`.
- Existing reservation reuse for idempotent same-order reservation.
- Duplicate exception handling for order and customer reservation conflicts.

Rollback is transaction-backed. Because the reservation is created inside `CreateOrderAsync` before commit, rollback removes the coupon reservation along with the order.

Usage counters are not incremented during Order Admission. Admission reserves coupon capacity. Confirmed usage is implemented in `ConfirmCouponUsageAsync`, which creates `CouponUsage`, marks reservation confirmed, recalculates `CurrentUsage`, and publishes `CouponConsumedEvent` later under its own transactional lifecycle.

## Payment Authority

Order Admission seeds local payment authority through `TryCreateTransactionalPaymentIntentAuthorityAsync`.

For `STRIPE`, `PAYMOB`, `FAWRY`, and `MANUALWALLET`, the method:

- Resolves the canonical payment provider from payment method code.
- Checks for an existing active non-deleted intent for the tenant/order/provider.
- Creates a SQL `PaymentIntent` with tenant id, order id, amount, currency, payment method, provider, generated payment reference, request id as idempotency key, pending status, and manual-wallet evidence fields when supplied.
- Adds the payment intent to `_dbContext.PaymentIntents`.
- Stamps `order.PaymentReference` and `order.PaymentGateway`.

This payment authority is transaction-owned by Order Admission because the new `PaymentIntent` is added before the admission transaction saves and commits.

Provider independence is preserved. `TryCreateTransactionalPaymentIntentAuthorityAsync` does not call Stripe, Paymob, Fawry, HTTP clients, SDKs, or provider runtime services. Actual provider payment-session creation occurs later in `ProcessCheckoutAsync` after `CreateOrderAsync` returns and after `orderCommitted = true`.

For non-provider methods such as COD, no `PaymentIntent` row is seeded inside Order Admission. Post-commit generic payment handling generates a payment reference, transitions checkout state, stages a `PaymentIntentCreatedEvent`, and saves outside the admission transaction.

## Order Persistence

Order creation is implemented by constructing an `Order` aggregate inside `CreateOrderAsync`, adding order items to `order.Items`, then adding the order to EF with `_dbContext.Orders.Add(order)` and saving.

Order items are immutable SKU snapshots. They persist product id, variant id, SKU, product name, variant name, variant attributes JSON, image URL, quantity, unit price, cost price, tax/discount amounts, total, and affiliate commission snapshot fields.

Order totals are server-calculated snapshots. `CreateOrderAsync` uses `StorefrontPricingService.CalculateCheckoutAsync`, validates expected pricing, and persists subtotal, coupon discount, promotion discount, manual discount, total discount, tax, shipping, payment fee, grand total, currency, and `PricingSnapshotJson`.

Order snapshots include:

- Customer/guest identity fields derived from authenticated caller/session and address evidence.
- Shipping and billing address snapshots.
- Geographic admission evidence and release ids when present.
- Shipping method id/name/cost/estimated delivery snapshot.
- Payment method enum/code/name/fee snapshot.
- Coupon and promotion identifiers/name/type.
- Fulfillment ownership snapshot.
- Source `OrderSource.Ecommerce`.

The implementation starts orders as `OrderStatus.Pending`, `PaymentStatus.Pending`, and `IsPaid = false`.

## Outbox

Outbox participation is implemented in multiple transaction-backed forms.

`EmitOrderCreatedEventAsync` is called inside `CreateOrderAsync` before final save and commit. It stages:

- Core `OrderCreatedEvent` through `_eventPublisher.PublishAsync`.
- `CheckoutOrderCreatedEvent` through `_eventPublisher.PublishAsync`.
- `AffiliateOrderPlacedEvent` through `_eventPublisher.PublishAsync` when affiliate authority exists.
- Marketplace `OrderCreatedEvent` through `IMarketplaceEventOutboxService.EnqueueAsync`.

`MassTransitEventPublisher` publishes authoritative integration events through the MassTransit EF bus outbox. `Program.cs` configures `AddEntityFrameworkOutbox<ApplicationDbContext>` with `UsePostgres()` and `UseBusOutbox()`. The publisher also enforces that authoritative events are published while a transaction or pending business mutation exists.

`PersistInventoryReservedOutboxAsync` explicitly adds an `OutboxMessage` for `InventoryReservedEvent` inside the transaction using a deterministic message id derived from event name, tenant id, order id, reservation id, and request id.

`ProductVariantStockRepository` writes `StockSyncOutboxMessage` rows for inventory projection changes as part of stock reservation mutation. Because the repository detects the current transaction, these rows participate in the checkout transaction.

Ordering and consistency are implemented by:

- Event version fields on authoritative events.
- Critical outbox constraints requiring tenant id, aggregate id, and positive event version.
- Critical aggregate ordering index on `OutboxMessages`.
- Stock-sync ordering indexes and unique variant/warehouse event-version constraints.
- Deterministic explicit outbox ids for inventory and payment-intent-created outbox helpers.

Publishing itself is not performed by Order Admission. Admission stages durable outbox rows; background processors publish them later.

## Idempotency

Idempotency is implemented by `CheckoutRequestIdempotencyCoordinator`, durable `OrderRequestIdempotency` rows, checkout lock ownership, request hashes, snapshot hashes, and active-order uniqueness.

Before cart loading, `ProcessCheckoutAsync` computes a deterministic request hash and performs a fast-path idempotency lookup. This allows replay or existing-order recovery after cart expiry or cart clearing.

After cart snapshot loading, checkout computes a cart snapshot hash, derives a server checkout idempotency key, persists active cart checkout idempotency metadata, and attempts to claim the SQL idempotency slot.

Duplicate submission behavior is:

- Existing completed response returns replay.
- Existing durable order id returns existing-order recovery.
- Same in-flight fresh claim returns in-progress.
- Same idempotency key with different snapshot hash returns payload conflict.
- Same idempotency key with different request hash returns payload conflict.
- Stale unfinished claims are deleted and may be retried.

SQL claim insertion is concurrency-aware. PostgreSQL uses `ON CONFLICT (TenantId, Key) DO NOTHING`; SQL Server uses `UPDLOCK, HOLDLOCK`; EF fallback catches duplicate inserts.

Order binding is part of admission. `BindOrderCreatedForCommitAsync` is called before transaction commit and its changes are saved with order admission. This creates a durable recovery boundary even if post-commit payment-session work fails.

Full response completion is post-admission. `CompleteAsync` stores the replay response after payment-session work. Therefore, idempotency has two implemented recovery levels: committed order binding first, and full response replay after post-commit payment presentation succeeds.

Active duplicate order protection is also implemented by `FindActiveOrderForCartAsync` before creation and by the unique filtered index `IX_Orders_TenantId_CartId_ActiveStatuses`. If the database unique guard fires, checkout recovers the existing active order and marks idempotency accordingly.

## Runtime Boundary

Order Admission ends at `transaction.CommitAsync` inside `CreateOrderAsync`.

Order Admission does not execute Stripe, Paymob, or Fawry provider runtime. The implementation-backed provider runtime boundary is:

- `CreateOrderAsync` commits local order admission first.
- `ProcessCheckoutAsync` sets `orderCommitted = true`.
- Post-commit payment logic then calls `CreateCheckoutPaymentSessionAsync`.
- `CreateCheckoutPaymentSessionAsync` delegates online methods to `_paymentOrchestrationService.ExecuteAsync` and legacy/manual methods to `_paymentService.CreatePaymentIntentAsync`.

Provider runtime, payment settlement, payment success/failure lifecycle, refunds, and provider webhooks are outside Order Admission.

## Critical Invariants

- An order officially exists only after the serializable order-admission transaction commits.
- Inventory reservation and order persistence are atomic in `CreateOrderAsync`.
- Coupon reservation and order persistence are atomic in `CreateOrderAsync`.
- Payment-intent authority seeding for gateway/manual-wallet methods is atomic with order persistence.
- Order idempotency order binding is atomic with order persistence.
- Order-created and inventory-reserved outbox staging is atomic with order persistence.
- Provider runtime starts only after order admission has committed.
- Stock reservation uses SQL stock authority and row locks, not browser or Mongo inventory.
- Reserved stock cannot exceed physical quantity by repository checks and database constraints.
- Coupon capacity uses confirmed usages plus active reservations.
- Coupon usage counters are not incremented by admission; they are confirmed later.
- Order totals, shipping, payment method, coupon/promotion, address, currency, and item values are persisted as admission-time snapshots.
- Orders start unpaid.
- Checkout duplicate submissions recover through durable idempotency or active-order uniqueness rather than creating a second active order for the cart.
- Outbox publishing is asynchronous; admission owns durable staging, not broker delivery.

## Architecture Boundaries

Order Admission owns:

- Serializable SQL transaction.
- Final checkout authority revalidation.
- Order persistence.
- Order-item persistence.
- Inventory reservation.
- Coupon reservation.
- Offer promotion reservation.
- Payment-intent authority seeding.
- Affiliate attribution creation where applicable.
- Outbox staging.
- Idempotency order binding.
- Duplicate active-order guard.
- Admission-time immutable snapshots.

Order Admission does not own:

- Stripe provider execution.
- Paymob provider execution.
- Fawry provider execution.
- Provider webhooks.
- Settlement.
- Refunds.
- Payment success/failure lifecycle.
- CommerceHub checkout.
- Marketplace settlement.
- Shipment runtime execution.
- Optional notification delivery.

## Architecture Risks

No architectural violations were identified.

## Certification Checklist

| Area | Result |
|---|---|
| Transaction | PASS |
| Inventory Reservation | PASS |
| Coupon Reservation | PASS |
| Payment Authority | PASS |
| Order Persistence | PASS |
| Outbox | PASS |
| Idempotency | PASS |
| Runtime Boundary | PASS |
| Critical Invariants | PASS |
| Architecture Boundaries | PASS |
| Overall Certification | PASS |

## Final Certification

PASS
