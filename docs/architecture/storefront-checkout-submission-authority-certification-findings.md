# PASS SF-PA5.6 Checkout Submission Authority Certification Findings

Date: 2026-06-29

## Verdict

PointOfSale.Storefront Checkout Submission Authority is production-certifiable with no runtime code change required.

Storefront frontend owns Submission Intent, loading state, validation presentation, retry intent, navigation, and success/pending presentation only. Backend `CheckoutController`, `CheckoutService`, `CheckoutRequestIdempotencyCoordinator`, SQL `Orders`, SQL `OrderRequestIdempotency`, SQL cart/order uniqueness, final transaction revalidation, inventory reservation, payment-intent authority, and order projection/outbox policy own Order Creation, Order Identity, Order Number, Final Revalidation, SQL Transaction, Order Snapshot, Concurrency, Idempotency, Rollback, Persistence, Recovery, Audit, and Projection Policy.

## Storefront Files Reviewed

- `PointOfSale.Storefront/src/pages/CheckoutPage.tsx`
- `PointOfSale.Storefront/src/components/checkout/ReviewStep.tsx`
- `PointOfSale.Storefront/src/components/checkout/OrderSummary.tsx`
- `PointOfSale.Storefront/src/components/checkout/CheckoutSuccess.tsx`
- `PointOfSale.Storefront/src/pages/CheckoutPendingPage.tsx`
- `PointOfSale.Storefront/src/pages/OrderConfirmationPage.tsx`
- `PointOfSale.Storefront/src/store/checkoutStore.ts`
- `PointOfSale.Storefront/src/types/checkout.ts`
- `PointOfSale.Storefront/src/services/api/storefrontApi.ts`
- `PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.ts`
- `PointOfSale.Storefront/src/features/checkout/paymentFlow.ts`
- `PointOfSale.Storefront/src/features/checkout/activeCheckoutOrderContext.ts`

## Backend Endpoints Reviewed

- `POST /api/storefront/checkout`
- `POST /api/storefront/checkout/pricing`
- `GET /api/storefront/cart`
- `GET /api/storefront/shipping-methods`
- `GET /api/storefront/payment-methods`
- `POST /api/storefront/checkout-session`
- `PATCH /api/storefront/checkout-session/{id}`
- `GET /api/storefront/checkout-session/recover/{token}`
- `GET /api/orders/{orderId}/payment-status`
- Storefront order read/tracking endpoints only as presentation/recovery consumers.

## Backend Services Reviewed

- `CheckoutController`
- `CheckoutService`
- `CheckoutRequestIdempotencyCoordinator`
- `StorefrontPricingService`
- `TaxCalculationService`
- `ShippingCalculationService`
- `CheckoutSessionController`
- `CheckoutSessionService`
- `StorefrontShippingMethodsController`
- `StorefrontPaymentMethodsController`
- `PaymentMethodConfigurationValidator`
- `CouponValidationService`
- `PromotionEngine`
- `ProductVariantStockRepository`
- `PaymentLifecycleService` only for post-order payment status authority boundaries.

## Submission Findings

- Storefront `Place Order` delegates to `submitCheckout`; it does not create an order object, order ID, or order number.
- `CheckoutRequest` contains `cartId`, `shippingAddress`, `shippingMethodId`, `paymentMethod`, optional coupon/notes/manual-wallet evidence, and `expectedPricing`. It contains no `orderId` or `orderNumber`.
- Before submission, Storefront refreshes backend cart and backend pricing preview, then blocks when pricing is missing, stale selections exist, address/payment/manual-wallet candidates are invalid, or auth state is degraded.
- Storefront response handling admits backend response evidence only through `requireStorefrontPaymentSessionAuthority`; malformed order ID/order number/amount/currency/payment handoff evidence is rejected before presentation.
- `activeCheckoutOrderContext` deliberately does not persist order identity; browser storage cannot recover or create submission authority.

## Transaction Findings

- Backend creates orders only inside `CheckoutService.CreateOrderAsync`.
- Final order commit uses `IsolationLevel.Serializable`.
- Final transaction revalidates storefront availability, shipping method, payment method, cart/variant prices, coupon/promotion authority, taxes, payment fee, expected pricing, currency, affiliate authority, inventory reservation, order items, order number, payment intent, coupon/offer reservation, outbox, and idempotency binding before commit.
- Rollback prevents partial orders; inventory reservation compensation is attempted on failed creation paths.
- Idempotency binding is persisted with the order before transaction commit; post-commit side effects do not decide order creation.

## Concurrency Findings

- Storefront disables duplicate submission locally with `isSubmitting`; this is presentation only.
- Backend owns concurrency through checkout execution locks, server-derived checkout idempotency keys, request/snapshot hashes, SQL idempotency records, serializable transaction retries, active-cart order unique index recovery, and existing-order replay.
- Duplicate orders from double-click, retry, refresh, parallel tab, or replay are prevented by backend lock/idempotency/unique-index authority.

## Recovery Findings

- Network timeout or browser retry re-enters backend idempotency. If an order was committed but response storage is incomplete, backend recovers from durable `OrderRequestIdempotency.OrderId` or active cart order lookup.
- Cart/session recovery restores only candidates and requires live backend cart revalidation before pricing or submission can proceed.
- Stale submission survives only as backend replay of the already-created order; it cannot create a second order or bypass final revalidation.

## Failure Findings

- 400 invalid DTO/address/pricing/manual-wallet evidence fails closed before service admission.
- 401 tenant/customer mismatch or inactive customer fails closed.
- 403 cart ownership/customer header mismatch/phone block fails closed.
- 404 missing/expired cart fails closed unless backend idempotency recovers an already-created order.
- 409 checkout in progress, payload conflict, cart snapshot conflict, duplicate guard, total mismatch, stock conflict, and serializable race failures fail closed or replay existing backend order.
- 422 validation errors remain presentation/input failures only.
- 500 order creation failures roll back; 503 infrastructure/promotion/service failures fail closed.
- Timeout/deadlock recovery re-enters idempotency or returns conflict; frontend state cannot decide success.

## Audit Findings

- Order identity and order number are server generated.
- SQL `Orders` persist tenant, online customer/session, cart ID, created timestamp, address snapshot, shipping method snapshot, payment method snapshot, subtotal, discounts, shipping, tax, payment fee, total, currency, and `PricingSnapshotJson`.
- Order items snapshot current backend variant identity, SKU/name/attributes, quantity, unit price, cost, line totals, tax, discount allocation, and affiliate commission evidence where applicable.
- Outbox events and idempotency records bind the order to correlation/request evidence for replay and audit.

## Boundary Review

- Submission Boundary: Storefront `ReviewStep`/`CheckoutPage`/`checkoutStore.submitCheckout`; owner is frontend intent only; recovery source is current backend cart/pricing; failure disables or shows retry/error.
- Submission Authority Boundary: `CheckoutController.Checkout` and `CheckoutService.ProcessCheckoutAsync`; owner is backend; recovery source is SQL idempotency/order authority; failure returns typed closed errors.
- Transaction Boundary: `CreateOrderAsync` serializable SQL transaction; owner is backend; recovery source is SQL order/idempotency/unique index; failure rolls back.
- Order Boundary: SQL `Orders` plus order items; owner is backend; recovery source is current SQL; failure never creates frontend order.
- Audit Boundary: SQL order snapshots, pricing JSON, idempotency records, outbox; owner is backend; failure blocks or preserves durable committed evidence.
- Recovery Boundary: backend idempotency/existing order replay; owner is backend; LocalStorage/sessionStorage/Redis bytes outside backend service admission are candidates only.
- Presentation Boundary: pending/confirmation/tracking UI; owner is frontend presentation; recovery source is backend payment/order reads; failure withholds success.

## Order Pipeline

Checkout Session -> Address -> Shipping -> Payment -> Pricing -> Submission Intent -> Backend Final Revalidation -> SQL Transaction -> Order Created -> Order DTO -> Presentation

- Submission Intent begins when `ReviewStep` invokes `onPlaceOrder` and `checkoutStore.submitCheckout` builds `CheckoutRequest`.
- Submission Authority begins in `CheckoutController.Checkout` / `CheckoutService.ProcessCheckoutAsync` after backend request, tenant, owner, cart, and idempotency admission.
- SQL Transaction begins at `BeginTransactionAsync(IsolationLevel.Serializable)` in `CreateOrderAsync`.
- Order Snapshot begins when backend constructs and refreshes the SQL `Order`/`OrderItem` graph from transaction-scoped backend authority.
- Storefront Presentation begins after a backend `CheckoutResponseDto`, payment-status DTO, or order-read DTO reaches Storefront components.

## Runtime Hardening Performed

- No runtime code change was required. Existing Storefront checkout submission runtime already submits backend-bound intent only, blocks missing/stale pricing, refuses stored frontend order identity, and relies on backend idempotency/final transaction authority for order creation.

## Exact Runtime Files Modified

- None.

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/StorefrontCheckoutSubmissionAuthorityCertificationTests.cs`

## Architecture Documentation Updates

- Added this findings document.

## AI Context Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
