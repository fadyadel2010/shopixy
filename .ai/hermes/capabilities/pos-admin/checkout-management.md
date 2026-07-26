# Checkout Management (POS Admin — Storefront Checkout Capability)

> Reverse-engineered from implementation. Implementation is authoritative; where it disagrees with
> existing `.ai/hermes` docs, the implementation wins and the discrepancy is recorded in the
> "Documentation Validation" section. This document covers the **storefront cart → order** capability
> owned by `CheckoutService` (`PointOfSale.API/Services/Checkout/CheckoutService.cs`) and its
> supporting surfaces. The separate **abandoned-checkout lead-capture** capability (`CheckoutSession`,
> `CheckoutSessionService`, `CheckoutSessionController`) is documented in its own section because it is
> explicitly NOT an order and never owns inventory, payments, or orders.

# Purpose

Checkout is the single, authoritative boundary that converts a validated cart into an
immutable `Order` plus a `PaymentIntent` (intent only — payment is never executed at
checkout time). It exists to:

- Solve the "stale client vs authoritative backend" problem: the storefront may send
  whatever prices/discounts/stock it last saw, but the backend **recomputes and
  revalidates every monetary, inventory, identity, and discount value from current SQL
  authority** before any order row is written.
- Guarantee exactly-once order creation under retries, double-clicks, parallel tabs, and
  concurrent carts via durable idempotency + a serializable SQL commit + an
  active-cart SQL uniqueness guard.
- Keep the cart read-only during checkout: the cart is a staging area, never the
  source of truth for price/stock/order.
- Produce a recoverable payment contract (Stripe secret, Paymob iframe, Fawry reference,
  or manual-wallet handoff) without ever moving money.

What Checkout owns:
- **Eligibility authority**: it recomputes and revalidates product/variant availability,
  price, stock, shipping method, payment method, coupon/promotion discount, tax,
  currency, customer identity, affiliate attribution, geographic address admission, and
  phone/OTP gate — all from current backend SQL.
- The order-creation transaction (order + items + immutable pricing/address/fulfillment
  snapshots + inventory reservation + coupon/promotion/affiliate reservations + outbox).
- The checkout idempotency claim lifecycle and the payment-intent handoff payload.

What Checkout does NOT own:
- Money movement (payment execution, capture, refund, payout) — owned by the Payments
  lifecycle (`IPaymentLifecycleService` / payment provider services).
- Order fulfillment, shipping dispatch, returns — owned by fulfillment/order lifecycle.
- Inventory truth — owned by the Inventory domain; checkout only *reserves* stock inside
  the order transaction via certified inventory authority (`IProductVariantStockRepository`).
- Catalog/pricing config — owned by Catalog/Pricing; checkout only consumes authoritative
  snapshots.
- Projection/storefront display state — derivative only.

# Business Scope

There are **two distinct capabilities** both named "checkout" in the codebase. Confusing
them is the most common source of wrong assumptions.

## A. Storefront Cart → Order Checkout (this document's primary subject)

Entrypoint: `POST /api/storefront/checkout` (`CheckoutController`),
`POST /api/storefront/checkout/pricing` (authoritative preview).
Service: `CheckoutService` (`ICheckoutService.ProcessCheckoutAsync`).

This is the purchase-authority capability. It covers:

- **Cart validation (read-only):** snapshot loaded from Redis; cart ownership
  (`CART_OWNERSHIP_MISMATCH` 403) and cart existence (`CART_NOT_FOUND` 404).
- **Request validation:** required `CartId`, `PaymentMethod`, `ShippingAddress`
  (Name/Phone/Street/City/Country), optional `BillingAddress`, required
  `ShippingMethodId`.
- **Caller/customer validation:** authenticated customer re-checked against current SQL
  `OnlineCustomer` authority; inactive → `CUSTOMER_INACTIVE` (401). Guest
  checkout = `customerId == null`, flagged from auth state, NOT from a request field
  (anti-spoofing).
- **Storefront availability:** SQL `StoreConfigs.StorefrontStatus` must equal `live`,
  re-locked with `FOR UPDATE` / `UPDLOCK` inside the commit transaction; otherwise
  `STOREFRONT_NOT_ACCEPTING_ORDERS` (409).
- **Phone risk gate:** merchant-managed blocked-phone list (`IPhoneRiskProfileService`)
  → `PHONE_BLOCKED` (403).
- **Geographic address admission:** shipping/billing addresses admitted through the
  geographic catalog (`IAddressAdmissionService`); admission evidence IDs and place
  IDs are snapshotted immutably onto the order.
- **Affiliate eligibility (conditional):** when Affiliate Mode is active, every cart
  item must be affiliate-eligible in SQL; otherwise `PRODUCT_NOT_ELIGIBLE` and the
  checkout is blocked.
- **Price recalculation (authoritative):** recomputed from `ProductVariant` SQL
  rows (NEVER Product, NEVER Mongo). Subtotal/tax/shipping/fees recomputed by
  `IStorefrontPricingService.CalculateCheckoutAsync`.
- **Coupon validation:** resolved from `ICouponValidationService`; coupon identity must
  be server-issued (`AppliedCouponId` present) or checkout is rejected.
- **Promotion / smart-offer validation:** `IPromotionEngine`; on engine failure the
  order is NOT silently discounted — it fails closed (503) or, if the offer could not
  be applied, the order is created with `PromotionCalculationFailed = true` and a
  warning returned (no silent discount removal).
- **Shipping method validation:** from SQL `ShippingMethods` authority (rates/thresholds).
- **Payment method validation:** from SQL `TenantPaymentMethods` (fees/limits);
  `INVALID_PAYMENT_METHOD` (400) / amount-limit violations.
- **Currency lockdown:** `Order.Currency` must resolve to tenant currency via
  `EnsureCheckoutCurrencyAsync`; final `Order.Currency == Tenant.Currency` is also
  enforced at the payment gateway.
- **Inventory validation & reservation:** variant-stock authority
  (`IProductVariantStockRepository`); reservation TTL = 10 minutes; non-tracked or
  backorderable variants skip stock checks.
- **Order creation:** single `SERIALIZABLE` SQL transaction committing order + items +
  snapshots + reservation + coupon/promotion/affiliate reservations + outbox.
- **Payment intent (intent only):** Stripe / Paymob / Fawry / ManualWallet / generic
  (COD). No money moves at checkout. The order starts `IsPaid = false` (COD stays
  unpaid until delivery).
- **Checkout idempotency & replay:** durable request/snapshot hash claim in SQL
  (`OrderRequestIdempotency`); same key replays the stored response.
- **Optional side effects:** WhatsApp order confirmation (opt-in only), order-velocity
  tracking (post-commit, non-fatal).

## B. Abandoned-Checkout Session Lead Capture (separate capability)

Entrypoints: `POST /api/storefront/checkout-session`,
`PATCH /api/storefront/checkout-session/{id}` (`CheckoutSessionController`).
Service: `CheckoutSessionService` (`ICheckoutSessionService`).
Entity: `CheckoutSession` (`Status: Active → Abandoned → ConvertedToOrder`).

Per its own source comments this is **NOT an order**: it "never creates or modifies
orders, never reserves inventory, never processes payments." It is a recoverable lead
record with a recovery token (`/recover-checkout/{token}`), autosaved form fields,
device/source attribution, and an items snapshot. A Hangfire job
(`AbandonedCheckoutScannerJob`, every 5 min, 30-min idle threshold) marks idle
`Active` sessions `Abandoned`; on `OrderCreatedEvent` the
`CheckoutSessionOrderConversionConsumer` marks the matching session `ConvertedToOrder`.
POS Admin surfaces (`AdminAbandonedCheckoutsController`) read the MongoDB projection
(`AbandonedCheckoutDocument`). **This capability is projection/lead evidence only and is
excluded from the order-authority boundaries below.**

## What Checkout does NOT own (explicit)

- Phone OTP issuance/sending is owned by `IStorefrontPhoneVerificationService`; checkout
  only *asks* whether the phone is verified and only blocks when the tenant has OTP
  enabled and verification is required and unmet.
- Inventory truth, fulfillment, payments execution, catalog/pricing config, CommerceHub
  marketplace checkout (separate `CommerceHubCheckoutService`), and affiliate
  commission *calculation* (occurs later at payment events) are all owned elsewhere.
  Checkout only stamps the affiliate *attribution* (identity) inline in the order
  transaction.

# Authority Boundaries

Distinguish three roles: **Authority** (writes the durable truth), **Projection**
(read-only derivative), **Evidence** (events/snapshots consumed by others), **Presentation**
(storefront UI).

| Concern | Authority | Projection only | Evidence emitted by Checkout |
|---|---|---|---|
| Checkout session (lead) | `CheckoutSessionService` (PG) | `AbandonedCheckoutDocument` (Mongo) | `CheckoutSessionAbandonedEvent`, `CheckoutSessionUpdatedEvent` |
| Cart | Cart domain (Redis PG) | storefront cart DTO | `CartActivityEvent` (abandonment tracking) |
| Customer identity | `OnlineCustomers` SQL | — | stamped on `Order.OnlineCustomerId`/`IsGuestCheckout` |
| Phone verification | `IStorefrontPhoneVerificationService` (Redis OTP) | — | none (checkout only queries) |
| Pricing | `ProductVariant` SQL + `IStorefrontPricingService` | Mongo product docs (NOT used for authority) | `PriceRecalculationEvent` |
| Inventory | `ProductVariantStocks` SQL (reservation write) | Mongo stock docs (NOT authority) | `InventoryReservedEvent` (outbox) |
| Reservation | `IProductVariantStockRepository.ReserveAsync` | Redis `reservation:{tenant}:{id}` (debug only) | `InventoryReservedEvent` |
| Payment intent | `PaymentIntent` SQL + provider services | — | `PaymentIntentCreatedEvent` |
| Order creation | `CheckoutService` inside SERIALIZABLE tx | Mongo order read-model | `OrderCreatedEvent` |
| Shipping | `ShippingMethods` SQL | — | shipping snapshot on order |
| Taxes | `IStorefrontPricingService` (tax rate/source) | — | pricing snapshot on order |
| Notifications | WhatsApp feature gate (opt-in) | — | `WhatsAppOrderConfirmationEvent` (best-effort) |
| Analytics / velocity | `IOrderVelocityService` (post-commit) | — | velocity signal logged |
| CommerceHub | SEPARATE `CommerceHubCheckoutService` | — | none shared |
| Affiliate | Attribution identity stamped at checkout; **commission computed later** at payment events | — | `AffiliateAttribution` row (same tx) |

Key boundary rules discovered in code:
- **Mongo/Redis are never authority for price or stock at checkout.** `RecalculatePricesInternalAsync`
  loads `AuthoritativeCheckoutVariant` from SQL; `ValidateAndReserveInventoryAsync` loads
  `ProductVariantStocks` from SQL. Mongo product documents "can lag Admin mutations."
- **Storefront display values are stale-client assertions.** Any mismatch with backend
  recomputation fails closed (409 `CHECKOUT_TOTAL_MISMATCH`, `PRICE_CHANGED`).
- **Payment is intent-only.** `IsPaid = false` at creation; COD stays unpaid until delivery.
  Checkout never calls a provider to capture.

# Checkout Lifecycle (implementation order)

The actual sequence inside `ProcessCheckoutAsync` (with `activeStep` labels from code):

1. **CallerValidation** — `ValidateCheckoutCallerOwnershipAsync` resolves and
   re-checks the owner (session/guest or authenticated customer).
2. **DurableIdempotencyReplay** — fast-path lookup by request hash BEFORE the
   cart is read (cart may be cleared/expired after first success); replays a
   completed record or recovers an existing-order binding.
3. **StorefrontAvailability** — `EnsureStorefrontAcceptingOrdersAsync` (no lock yet).
4. **AcquireLock** — owned distributed lock lease
   (`lock:checkout:{tenant}:{cartId}` via `ICheckoutExecutionLockManager`);
   contention → `CHECKOUT_IN_PROGRESS` (409).
5. **CartSnapshot** — Redis cart snapshot loaded (`GetCartSnapshotAsync`).
6. **IdempotencyClaim** — server checkout id built from
   `tenant+cart+owner+snapshotHash`; `OrderRequestIdempotency` claimed.
7. **DuplicateOrderGuard** — `FindActiveOrderForCartAsync` (SQL unique index
   `IX_Orders_TenantId_CartId_ActiveStatuses`); existing active order → replay.
8. **PhoneBlockValidation** — `IPhoneRiskProfileService` normalize + blocked check.
9. **AddressGeographicAdmission** — `AdmitCheckoutGeographyAsync`.
10. **AffiliateValidation** (conditional) — all cart items must be affiliate-eligible.
11. `CheckoutStartedEvent` + `CartActivityEvent` (abandonment tracking) emitted.
12. **PriceRecalculation** — `RecalculatePricesAsync` from SQL variant authority.
13. **CouponValidation / Promotion** — `ReconcileCheckoutDiscountAuthorityAsync`.
14. **ShippingMethodValidation** + **PaymentMethodValidation** (SQL authority).
15. **CurrencyValidation** — `EnsureCheckoutCurrencyAsync`.
16. **PaymentAmountValidation** — amount-limit check on the payment method.
17. **PhoneOtpVerification** (conditional) — blocks only if tenant OTP enabled and
    phone unverified (`PHONE_VERIFICATION_REQUIRED`).
18. **OrderCreation** — SERIALIZABLE transaction: re-lock `StoreConfigs` (live),
    revalidate authority in-transaction, reserve inventory, generate order number,
    add order, bind reservations, transactional payment-intent authority, stamp
    affiliate attribution inline, reserve coupon/promotion, emit `OrderCreatedEvent`,
    persist `InventoryReservedEvent` outbox, bind idempotency to order, commit.
19. **VelocityTracking** (post-commit, non-fatal).
20. **PaymentIntent** — Stripe/Paymob/Fawry/ManualWallet/generic; order
    transitions `PendingPaymentIntent → PaymentPending`; client secret/iframe/reference
    issued (no capture).
21. **StoreIdempotency** — replay-safe response persisted (time-limited tokens
    stripped from the stored copy).
22. **OptionalSideEffects** — WhatsApp confirmation (opt-in), final outbox flush.

# Implementation Discovery

## Frontend / operational surfaces (NOT documented in depth — UI-free capability)
- Storefront: `CheckoutPage.tsx`, `RecoverCheckoutPage.tsx`,
  `CheckoutPendingPage.tsx`, `CheckoutSuccess.tsx`, `RecoverCartPage.tsx`.
- CommerceHub: `CheckoutPage.tsx`, `CheckoutForm.tsx`, `CheckoutSummary.tsx`.
- Web (POS Admin): `AbandonedCheckouts.jsx`, `CheckoutShippingStep.jsx`.
These are operational surfaces; the capability below is valid regardless of UI.

## Controllers / endpoints
- `CheckoutController` (`/api/storefront/checkout`): `POST` = process,
  `POST pricing` = authoritative preview. `[AllowAnonymous]`, rate-limited
  (`checkout` policy), no-store cache. Headers: `X-Tenant-Id` (req),
  `X-Session-Id` (guest), `X-Customer-Id` (optional, from JWT claims),
  `Idempotency-Key`/`X-Request-Id` (compatibility only — the durable
  checkout key is server-derived).
- `CheckoutSessionController` (`/api/storefront/checkout-session`): create/resume
  + PATCH autosave. Lead-capture only.

## Application services
- `CheckoutService` (`ICheckoutService`) — the orchestrator (~8.8k LOC).
- `CheckoutSessionService` (`ICheckoutSessionService`) — lead capture.
- `CommerceHubCheckoutService` — SEPARATE marketplace checkout (not covered here).

## Domain / authority services injected into CheckoutService
- `IStorefrontPricingService.CalculateCheckoutAsync` — authoritative totals.
- `ICouponValidationService` — coupon resolve + `ReserveCouponAsync` / release.
- `IPromotionEngine` — smart-offer calculation.
- `IProductVariantStockRepository` — variant-stock reservation authority
  (`ReserveAsync`, `ReleaseOrderReservationsAsync`, `BindOrderReservationsAsync`).
- `IProductVariantReadRepository` / `IProductReadRepository` — variant/product SQL reads.
- `IPhoneRiskProfileService` — normalize + blocked-phone gate.
- `IOrderVelocityService` — post-commit velocity signal.
- `IAddressAdmissionService` — geographic address admission.
- `IStorefrontPhoneVerificationService` — WhatsApp OTP verification query.
- `ICheckoutExecutionLockManager` — owned distributed lock lease.
- `ICheckoutRequestIdempotencyCoordinator` — durable request/snapshot claim.
- `IWhatsAppFeatureGateService` — opt-in confirmation gate.

## Payment intent providers (intent only)
- `IStripePaymentService.CreatePaymentIntentAsync`
- `IPaymobPaymentService.CreatePaymentIntentAsync`
- `IPaymentService.CreatePaymentIntentAsync` (Fawry, ManualWallet)
- Generic/COD handled inline (no provider call).

## Repositories / entities (PostgreSQL — authority)
- `Orders`, `OrderItems`, `OrderRequestIdempotency`, `PaymentIntents`,
  `StoreConfigs`, `ShippingMethods`, `TenantPaymentMethods`,
  `ProductVariantStocks`, `Coupons`, promotion/offer tables,
  `AffiliateAttributions`, `OnlineCustomers`.
- `CheckoutSession` (lead) + `CommerceHubCheckoutIdempotency` (marketplace).

## Outbox / events (MassTransit)
- `OrderCreatedEvent`, `PaymentIntentCreatedEvent`, `InventoryReservedEvent`
  (persisted via outbox in the order transaction), `PriceRecalculationEvent`,
  `CheckoutStartedEvent`, `CheckoutFailedEvent`, `CartActivityEvent`,
  `OrderCancelledEvent` (payment-intent gap compensation),
  `WhatsAppOrderConfirmationEvent` (best-effort).
- Consumers: `CheckoutSessionOrderConversionConsumer` (marks session converted).

## Background jobs
- `AbandonedCheckoutScannerJob` — every 5 min, marks idle (>30 min)
  `Active` `CheckoutSession` → `Abandoned`.

## Redis (locks / OTP / debug)
- `lock:checkout:{tenant}:{cartId}` — owned execution lock.
- `reservation:{tenant}:{id}` — 10-min debug cache of reservation.
- `checkout_otp:*` / `checkout_phone_verified:*` / `checkout_otp_ratelimit:*`
  / `checkout_otp_lock:*` — WhatsApp OTP store (`CheckoutPhoneOtpStore`).
- Cart snapshot — read-only source for checkout.

## Mongo (projection only)
- Order read-model; `AbandonedCheckoutDocument` (admin abandoned-checkout view).
  Never used as checkout authority.

# Business Rules (implementation-backed)

## Validation & guest rules
- `CartId`, `PaymentMethod`, and a full `ShippingAddress`
  (Name/Phone/Street/City/Country) are required; missing → 400.
- Guest checkout requires a `X-Session-Id`; `IsGuestCheckout` is derived from
  auth state (`customerId == null`), NOT a request field.
- Authenticated customer is re-checked against current SQL `OnlineCustomer`;
  inactive → `CUSTOMER_INACTIVE` (401). A valid JWT is authentication, not
  sufficient authorization after SQL deactivation.
- Cart ownership must match tenant/owner → `CART_OWNERSHIP_MISMATCH` (403).
- Storefront must be `live` in SQL `StoreConfigs` → else `STOREFRONT_NOT_ACCEPTING_ORDERS` (409).

## Pricing & discount rules
- Prices come from `ProductVariant` (`Variant.OnlinePrice ?? Variant.Price`);
  NEVER `Product.Price`; stock from `ProductVariantStocks`, NEVER `WarehouseStocks`.
- Backend recomputes subtotal/tax/shipping/fees; client totals are assertions.
- `overallDiffPercent > 5.0` (PriceChangeThresholdPercent) OR any
  price refreshed → `CHECKOUT_PRICE_CHANGED` (409) — client must refresh cart.
- `EnsureExpectedPricingMatches` mismatch → `CHECKOUT_TOTAL_MISMATCH` (409).
- Coupon must carry a server-issued `AppliedCouponId`; code without id → rejected.
- Promotion-engine failure fails closed: returns 503, OR (if an offer simply
  could not be applied) creates the order with `PromotionCalculationFailed = true`
  + a warning — discounts are NEVER silently stripped.
- Variant must be active, product active/online-visible/published, CategoryId in a live
  lineage, and variant must belong to the product — else `CART_INVALID`.

## Inventory / reservation rules
- Reservation TTL = 10 minutes (`InventoryReservationTtlMinutes`).
- Non-tracked (`TrackStock=false`) or backorderable (`AllowBackorder=true`)
  variants skip stock checks entirely.
- Available `ProductVariantStocks` < requested qty → `OUT_OF_STOCK` (409).
- Reservation is performed INSIDE the order transaction so stock and order existence
  cannot diverge. On failure the reservation is released (compensation).
- If stock released but the `InventoryReservedEvent` failed to publish, an
  `AggregateException` is thrown (escalated) to prevent cross-system divergence.

## Payment selection rules
- Payment method validated from SQL `TenantPaymentMethods` (fee/limit);
  invalid/disabled → `INVALID_PAYMENT_METHOD` (400).
- Amount-limit validation on the method; order `Currency` must equal tenant currency.
- COD (`SupportsOffline`) orders start `IsPaid = false` and stay unpaid until
  delivery. Online payments only get a PaymentIntent — no capture at checkout.
- ManualWallet: merchant wallet details are derived server-side; proof file key is
  tenant-scoped; missing config → `MANUAL_WALLET_NOT_CONFIGURED`.
- Stripe/Paymob/Fawry return the contract; incomplete contract → `INVALID_PAYMENT_METHOD`.

## Idempotency & concurrency rules
- Durable claim key = `tenant+cart+owner+snapshotHash` (+ request hash).
  Same key replays stored response. Different snapshot/payload → `CHECKOUT_IDEMPOTENCY_CONFLICT` (409).
- Owned execution lock prevents two checkouts on one cart → `CHECKOUT_IN_PROGRESS` (409).
- Order commit uses `SERIALIZABLE`; serializable races retry up to 3 times
  (`CheckoutAuthorityCommitMaxRetries`), then `CHECKOUT_CONCURRENCY_CONFLICT` (409).
- SQL unique index `IX_Orders_TenantId_CartId_ActiveStatuses` blocks a second
  active order for the same tenant+cart; on violation the existing order is recovered.
- Idempotency cleanup may delete ONLY unbound/uncompleted claims owned by the
  current claim token; once bound to an order it is preserved (replay/compensation).

## OTP / phone rules
- OTP gate is CONDITIONAL: only blocks when `IStorefrontPhoneVerificationService`
  is wired, tenant has WhatsApp OTP enabled, and the phone is not verified for
  the session. Otherwise checkout proceeds.
- Blocked merchant phone (`IPhoneRiskProfileService`) → `PHONE_BLOCKED` (403).

## Expiration / failure recovery
- Abandoned-checkout sessions (separate entity) expire after 30 min idle.
- Post-commit payment-intent gap: if the order committed but the PaymentIntent
  could not be created, checkout re-checks for an existing PaymentIntent under a
  row lock; if none and the failure is a local intent failure, it FAILS the order
  (`CheckoutState.Failed`, releases reservations/coupon) — fail closed. If the
  outcome is unknown, it leaves the order for reconciliation (does NOT cancel).

# Checkout State Machine

Two state machines exist.

## Order.CheckoutState (authoritative, `Order.cs:IsValidCheckoutTransition`)
New orders start `PendingPaymentIntent`. Legal transitions:

```
Draft                → PendingPaymentIntent
PendingPaymentIntent → PaymentPending | Failed | Cancelled | Expired
PaymentPending       → Paid | Failed | Cancelled | Expired
Failed               → PendingPaymentIntent | Cancelled
Cancelled            → (terminal, no transitions)
Expired              → PendingPaymentIntent
Paid                 → (terminal, no transitions)
```
- `Draft` is never used by storefront checkout (orders are created directly in
  `PendingPaymentIntent`).
- Note: checkout itself only performs `PendingPaymentIntent → PaymentPending`.
  `Paid`/`Failed`/`Cancelled`/`Expired` are reached later by the payment and
  order-lifecycle services, NOT by `CheckoutService`.

## CheckoutSession.Status (lead capture only — NOT an order)
```
Active         → Abandoned | ConvertedToOrder
Abandoned      → (terminal for recovery; ConvertedToOrder possible via consumer)
ConvertedToOrder → (terminal)
```
- `Active`: customer filling fields / autosave.
- `Abandoned`: scanner detected >30 min idle.
- `ConvertedToOrder`: `CheckoutSessionOrderConversionConsumer` reacted to
  `OrderCreatedEvent` for the same cart.

# Checkout Orchestration

`CheckoutService.ProcessCheckoutAsync` is the only orchestrator for storefront
checkout. It calls downward into owned authority services and outward into
projection/evidence consumers.

- **Cart** — READ-ONLY source (`GetCartSnapshotAsync` from Redis). Checkout
  never writes the cart. After a successful order the cart is left intact
  (cleared later by cart lifecycle, not checkout).
- **Pricing** — calls `IStorefrontPricingService.CalculateCheckoutAsync`
  (authoritative). Consumes SQL pricing config; ignores client totals except to
  fail closed on mismatch.
- **Inventory** — acquires reservation via `IProductVariantStockRepository`
  INSIDE the order transaction. Checkout owns the *reservation* act; the
  stock truth is owned by Inventory. On checkout failure it releases the reservation.
- **Payments** — creates a PaymentIntent via the provider service (Stripe/Paymob/
  Fawry/ManualWallet) or inline for COD. Checkout owns the *intent* and
  the handoff payload; the Payments lifecycle owns capture/refund.
- **Orders** — Checkout owns creation inside SERIALIZABLE; downstream order
  lifecycle owns fulfillment, shipment, cancellation, returns.
- **Shipping** — validates the chosen `ShippingMethod` from SQL; snapshots
  method + computed fulfillment mode (carrier vs manual) immutably onto the order.
  Fulfillment ownership is decided at checkout and never re-inferred later.
- **Customer** — re-validates `OnlineCustomer` SQL authority; stamps
  `OnlineCustomerId` (storefront) — NOT the POS `CustomerId` (deliberate:
  storefront orders must not FK the POS customer table).
- **Notifications** — opt-in WhatsApp confirmation only, best-effort post-commit.
- **Affiliate** — when Affiliate Mode active, validates item eligibility,
  resolves the affiliate account from anti-tamper evidence (funnel-stamped
  cart > middleware > request `?ref=`), and STAMPS the `AffiliateAttribution`
  row inline in the same transaction. Commission *calculation* happens later at
  payment events, not at checkout.
- **Analytics** — `IOrderVelocityService` post-commit signal only.
- **CommerceHub** — SEPARATE `CommerceHubCheckoutService`; storefront
  checkout shares no orchestration with it.
- **Search / Elasticsearch** — no direct call; order projection is eventual via
  outbox → Mongo/ES consumers.

# Cross-System Effects

| System | What Checkout CHANGES | What it does NOT change |
|---|---|---|
| Products | none (read-only variant lookup) | product config, publish state |
| Inventory | RESERVES stock in `ProductVariantStocks` (10-min TTL) inside the order tx; releases on failure | stock truth/config; final decrement at fulfillment |
| Pricing | recomputes + persists `PricingSnapshotJson` + discount fields immutably on the order | pricing rules/engine config |
| Orders | CREATES the `Order` + `OrderItems` + `AffiliateAttribution` in one tx | fulfillment, shipment, cancellation, returns |
| Payments | CREATES a `PaymentIntent` (intent only) + transitions `PendingPaymentIntent→PaymentPending` | capture, refund, payout, provider webhooks |
| Customers | re-validates `OnlineCustomer`; stamps id + `IsGuestCheckout` | customer records/config |
| Storefront | emits `OrderCreatedEvent`/`PaymentIntentCreatedEvent` → Mongo/ES projections update | storefront config |
| CommerceHub | none (separate service) | — |
| Affiliate | stamps attribution identity; validates eligibility | commission calculation (later) |
| Shipping | snapshots method + fulfillment mode onto order | carrier config / dispatch |
| Analytics | velocity signal (post-commit) | reporting stores |
| Notifications | optional WhatsApp confirmation event (best-effort) | notification infra |
| Search | order projection refreshed via outbox consumer | search index config |
| Identity | reads JWT claims; never writes identity | auth/identity stores |

# Validation Checklist (operational)

After customer enters checkout (`POST /checkout-session`):
- A `CheckoutSession` (Active) is created/resumed; recovery token issued.
- Device/source attribution captured; items snapshot stored.

After OTP verification (if tenant enabled):
- Redis `checkout_phone_verified:{tenant}:{scope}:{phone}` set.
- Checkout's `PhoneOtpVerification` step passes; otherwise
  `PHONE_VERIFICATION_REQUIRED`.

After coupon applied (cart/pricing):
- Coupon carries a server `AppliedCouponId` (not just a code).
- At checkout `ReconcileCheckoutDiscountAuthorityAsync` re-resolves it;
  missing id → `CART_INVALID`.

After address changed:
- `AdmitCheckoutGeographyAsync` re-admits; admission evidence IDs
  snapshotted onto the order. Invalid country code is skipped (not fatal).

After payment method changed:
- `ValidatePaymentMethodAsync` re-checks SQL `TenantPaymentMethods`;
  amount-limit validated; currency must equal tenant currency.

After reservation created (inside order tx):
- `ProductVariantStocks` decremented (reserved) with 10-min TTL.
- `InventoryReservedEvent` staged in outbox.

After payment intent created:
- `PaymentIntent` row written; order `PendingPaymentIntent → PaymentPending`.
- Stripe secret / Paymob iframe / Fawry reference / ManualWallet handoff returned.

After checkout completed:
- Order committed; idempotency record bound to `OrderId` + completed.
- `CheckoutSession` for the cart marked `ConvertedToOrder` (consumer).

After checkout expired/abandoned (separate session entity):
- `AbandonedCheckoutScannerJob` (30-min idle) → `Abandoned`.
- No order, no inventory, no payment side effects occur from abandonment.

# Common Failure Scenarios

## Checkout cannot continue
- `STOREFRONT_NOT_ACCEPTING_ORDERS` (409): `StoreConfigs.StorefrontStatus`
  != `live`. Investigate: storefront config, not checkout.
- `CUSTOMER_INACTIVE` (401): authenticated customer deactivated in SQL
  despite valid JWT. Investigate `OnlineCustomers`.

## OTP verified but blocked
- Tenant has OTP enabled yet checkout still 403 `PHONE_BLOCKED`: the phone
  is on the merchant blocked list (`IPhoneRiskProfileService`), independent
  of OTP. Investigate phone-risk profile, not OTP.

## Inventory changed during checkout
- `OUT_OF_STOCK` (409) after price/reservation: a concurrent order consumed
  stock between cart load and the SERIALIZABLE reservation. Investigate
  `ProductVariantStocks` + reservation outbox.

## Price changed during checkout
- `CHECKOUT_PRICE_CHANGED` (409): backend recompute diff > 5% or any
  price refreshed. Client must refresh cart from authoritative catalog.

## Coupon rejected
- Code without server `AppliedCouponId` → `CART_INVALID`. Or coupon
  reservation fails inside the tx → compensation releases stock.

## Promotion disappeared
- Engine error → 503, OR order created with `PromotionCalculationFailed=true`
  + warning (no silent discount). Investigate `IPromotionEngine`.

## Reservation failed
- `ReserveAsync` false → `OUT_OF_STOCK`; partial mutation → compensation
  releases all reserved. Investigate `IProductVariantStockRepository`.

## Duplicate order
- Blocked by execution lock (`CHECKOUT_IN_PROGRESS`), idempotency claim,
  and the `IX_Orders_TenantId_CartId_ActiveStatuses` unique index. If a
  duplicate slips to the unique index, the existing active order is recovered.
  Investigate idempotency claim + index, not order logic.

## Payment intent missing (post-commit gap)
- Order committed but PaymentIntent creation failed: checkout re-checks under
  a row lock; if no PaymentIntent and local failure → order FAILED + reservations
  released (fail closed). Unknown outcome → left for reconciliation.

## Checkout timeout / lock lost
- Execution lock lease lost mid-flight → `ThrowIfOwnershipLost`; idempotency
  claim cleaned so retries can proceed. Investigate Redis lock lease/TTL.

## Customer session lost
- Guest with cleared Redis cart but prior success: durable idempotency
  fast-path replays by request hash before cart read. Recover via the
  stored idempotency record / `CheckoutSession` recovery token.

# Debugging Guide

When a checkout bug arrives, investigate in this order (derived from the
lifecycle and the compensation paths):

1. **Cart** — first, because the snapshot is the input.
   `GetCartSnapshotAsync` (Redis). Bad/missing cart → `CART_NOT_FOUND`,
   ownership mismatch → `CART_OWNERSHIP_MISMATCH`. Confirm the cart
   exists, is tenant/owner-bound, and has the items the client claims.
2. **Storefront availability** — `StoreConfigs.StorefrontStatus` must be
   `live`. A 409 `STOREFRONT_NOT_ACCEPTING_ORDERS` is a config issue,
   not a checkout bug.
3. **Customer / identity** — authenticated customer re-checked in SQL
   `OnlineCustomers`; inactive → 401. Guest vs authenticated is derived
   from auth state, not the request.
4. **Inventory** — `ProductVariantStocks` reservation inside the order tx.
   `OUT_OF_STOCK`, post-commit stock/release divergence, or missing
   `InventoryReservedEvent` → investigate `IProductVariantStockRepository`
   and the reservation outbox. Non-tracked/backorderable items skip checks
   by design.
5. **Pricing** — `RecalculatePricesAsync` from SQL `ProductVariant`.
   `PRICE_CHANGED` (>5% or any refresh) or `CHECKOUT_TOTAL_MISMATCH`
   → client cart is stale; force a refresh. Mongo product docs are NOT
   authority, so do not trust them here.
6. **Coupon / Promotion** — `ReconcileCheckoutDiscountAuthorityAsync`.
   Missing `AppliedCouponId`, promotion-engine error (503) or
   `PromotionCalculationFailed=true` (silent-fallback warning).
7. **OTP / Phone** — only blocks if `IStorefrontPhoneVerificationService`
   is wired AND tenant OTP enabled AND phone unverified. A 403
   `PHONE_BLOCKED` is the merchant blocked-phone list, independent of OTP.
   State lives in `checkout_otp:*` / `checkout_phone_verified:*` Redis keys.
8. **PaymentIntent** — after order commit. Stripe/Paymob/Fawry/ManualWallet
   contract creation. A post-commit gap is handled by the row-locked
   re-check + fail-closed compensation (`CheckoutState.Failed` + release).
   `INVALID_PAYMENT_METHOD` / `MANUAL_WALLET_NOT_CONFIGURED` are
   method/config issues.
9. **Order** — `Orders` + `OrderItems` committed in SERIALIZABLE.
   Duplicate-order guard = execution lock + idempotency + unique index
   `IX_Orders_TenantId_CartId_ActiveStatuses`. Serialized race → retry
   (3x) → `CHECKOUT_CONCURRENCY_CONFLICT`.
10. **Reservation (again, if divergence suspected)** — `reservation:{tenant}:{id}`
    Redis debug cache (10-min TTL) versus `ProductVariantStocks`.
11. **Outbox** — `OrderCreatedEvent`, `PaymentIntentCreatedEvent`,
    `InventoryReservedEvent` must be present for downstream projections/
    inventory/payment consumers. Missing outbox → `CheckoutPostCommitConsistencyException`.
12. **Storefront projection** — only AFTER the above are green. The Mongo/ES
    read-model lagging is a projection issue, never the checkout authority.

%%APPEND%%

# Related Knowledge

## Core Guides (.ai/hermes)
- `05-orders.md` — order authority, lifecycle, checkout rules (primary sibling).
- `04-payments.md` — charge registration, payment-intent authority, idempotency.
- `03-inventory.md` — variant-stock reservation authority, consumption rules.
- `02-architecture.md` — serializable checkout tx, idempotency/replay.
- `08-qa-philosophy.md` — fail-closed testing posture.

## System Guides (.ai/hermes/systems)
- `storefront/README.md` — storefront checkout surface.
- `commercehub/README.md` — SEPARATE marketplace checkout (`CommerceHubCheckoutService`).
- `affiliate/README.md` — affiliate attribution stamping at checkout.
- `service-marketplace/README.md` — internal POS-to-POS B2B (no storefront checkout).

## Related Capabilities
- Cart Management (Redis snapshot source).
- Pricing Management (authoritative totals engine).
- Inventory Management (reservation authority).
- Payment Management (intent + capture lifecycle).
- Order Management (post-checkout lifecycle).
- Affiliate (attribution + commission).
- CommerceHub Marketplace Checkout (parallel, separate service).

## Related Domains
- `CheckoutSession` (lead capture, NOT an order).
- `CommerceHubCheckoutIdempotency` (marketplace idempotency).
- WhatsApp OTP (`CheckoutPhoneOtpStore`, `IStorefrontPhoneVerificationService`).
- Phone Risk Profile (merchant blocked-phone gate).
- Geographic Address Admission (shipping/billing admission evidence).

# Documentation Validation

Compared against the existing `.ai/hermes` docs (`05-orders.md` "Checkout
Rules", `04-payments.md` "Charge Registration / Idempotency", `03-inventory.md`,
`02-architecture.md`). Findings are implementation-backed.

## Missing documentation (now added by this file)
- No dedicated capability file for storefront checkout existed; checkout rules
  were only embedded inside `05-orders.md`. The split between the
  **order-authority checkout** (`CheckoutService`) and the **lead-capture
  `CheckoutSession`** was not made explicit anywhere — a frequent source
  of wrong assumptions. This file separates them.
- The post-commit payment-intent gap compensation (fail-closed order
  cancellation + reservation release, or "leave for reconciliation" on
  unknown outcome) was not documented.
- The actual `ProcessCheckoutAsync` step order (CallerValidation →
  DurableIdempotencyReplay → ... → OptionalSideEffects) was not
  written down; only the final-transaction revalidation was described.

## Outdated / imprecise (implementation correct, docs understate)
- `05-orders.md` says "Final storefront order creation runs inside a
  SERIALIZABLE SQL transaction. Before commit it re-locks `StoreConfigs`..."
  — correct, but it omits that the SAME transaction also: re-validates
  affiliate commission policy under row lock, binds order reservations,
  stamps affiliate attribution inline, reserves coupon/promotion, and persists
  the `InventoryReservedEvent` outbox before commit. This file captures all.
- `05-orders.md` "Promotion calculation failure fails closed" is correct,
  but the IMPLEMENTATION has a second branch: if an offer merely could
  not be applied (not engine failure), the order is created with
  `PromotionCalculationFailed=true` + a warning rather than 503. Docs only
  mention the 503 branch.
- `04-payments.md` lists Fawry/Paymob/Stripe but omits **ManualWallet**
  as a first-class intent path with its own server-derived merchant wallet
  details and tenant-scoped proof key.

## Incorrect / architecture inconsistencies (disagreement → implementation wins)
- None found that contradict the core authority/lifecycle rules. The existing
  `05-orders.md` checkout rules are accurate and consistent with the code.

## Hidden implementation behavior (not obvious from docs)
- **CheckoutState.Draft is never used** by storefront checkout; orders are
  created directly in `PendingPaymentIntent`. Only `CheckoutService`
  performs `PendingPaymentIntent → PaymentPending`; all terminal states
  (`Paid`/`Failed`/`Cancelled`/`Expired`) are owned by the payment/order
  lifecycle, not checkout.
- **`OnlineCustomerId`, never `CustomerId`, is stamped on storefront orders**
  (deliberate — POS `CustomerId` would be a wrong FK). This is in code
  comments but easy to miss.
- **OTP is conditional**, not mandatory: checkout only blocks when the
  verification service is wired, the tenant has WhatsApp OTP enabled, and
  the phone is unverified. Many assume OTP always gates checkout.
- **A 403 `PHONE_BLOCKED` can occur even with OTP verified** — it is the
  merchant blocked-phone list, independent of the OTP flow.
- **Non-tracked / backorderable variants skip stock checks entirely** — not
  a checkout bug when "out of stock" does not trigger for such items.
- **Reservation TTL is 10 minutes** and lives in `ProductVariantStocks`;
  the Redis `reservation:{tenant}:{id}` key is debug-only.
- **Idempotency fast-path replays BEFORE the cart is read**, so a guest whose
  Redis cart was cleared after first success still recovers the order by
  request hash — a deliberately hidden recovery safety net.
- **`CheckoutSession` is not an order** and emits no inventory/payment
  side effects; the abandoned-checkout scanner and recovery token are lead
  capture only.

# End-of-task Confirmations

- Summary of discoveries: Storefront checkout is a single authoritative
  `CheckoutService.ProcessCheckoutAsync` flow that revalidates everything
  from SQL, reserves inventory + creates the order inside one SERIALIZABLE
  transaction, then issues a payment INTENT (no capture). A separate
  `CheckoutSession` entity is lead capture only. OTP is conditional;
  phone-block is independent; COD stays unpaid; promotion failure is
  fail-closed (or flagged, never silently discounted).
- Checkout lifecycle discovered: CallerValidation → DurableIdempotencyReplay
  → StorefrontAvailability → AcquireLock → CartSnapshot → IdempotencyClaim
  → DuplicateOrderGuard → PhoneBlockValidation → AddressGeographicAdmission
  → AffiliateValidation → started/cart-activity events → PriceRecalculation
  → Coupon/Promotion → Shipping/Payment validation → CurrencyValidation
  → PaymentAmountValidation → PhoneOtpVerification → OrderCreation
  (SERIALIZABLE) → VelocityTracking → PaymentIntent → StoreIdempotency
  → OptionalSideEffects.
- Checkout state machine discovered: Order.CheckoutState (Draft →
  PendingPaymentIntent; PendingPaymentIntent → PaymentPending|Failed|
  Cancelled|Expired; PaymentPending → Paid|Failed|Cancelled|Expired;
  Failed → PendingPaymentIntent|Cancelled; Cancelled/Paid terminal;
  Expired → PendingPaymentIntent). CheckoutSession.Status (Active →
  Abandoned|ConvertedToOrder).
- Business rules discovered: required fields; guest/auth derivation;
  inactive-customer 401; storefront `live` gate; >5% price-change → 409;
  total-mismatch 409; coupon needs server id; promotion fail-closed;
  variant-stock reservation (10-min TTL, non-tracked/backorder skip);
  payment-method/currency validation; COD unpaid; idempotency + lock +
  unique-index duplicate prevention; serializable retry (3x).
- Cross-system dependencies discovered: reads Cart (Redis, read-only),
  Pricing, Inventory (reservation only), Payments (intent only),
  Orders (creates), Shipping (snapshot), Customer (revalidate), Affiliate
  (attribution stamp), Notifications (opt-in WhatsApp), Analytics
  (velocity), CommerceHub (separate), Search (projection). See the
  Cross-System Effects table.
- Documentation inconsistencies found: checkout rules were embedded only in
  `05-orders.md` (no dedicated capability file); post-commit
  payment-intent gap compensation undocumented; `05-orders.md` understates
  the in-transaction work; `04-payments.md` omits ManualWallet as a
  first-class intent path. No contradictions with core authority rules.
- Hidden implementation behavior found: Draft state never used; OnlineCustomerId
  (not CustomerId) stamped; OTP conditional; PHONE_BLOCKED independent
  of OTP; non-tracked/backorder variants skip stock checks; 10-min
  reservation TTL; idempotency fast-path replays before cart read;
  CheckoutSession is not an order.
- Confirmation that ONLY this file was created:
  `.ai/hermes/capabilities/pos-admin/checkout-management.md` — no other
  file was written, edited, or deleted.
- Confirmation that no other files were modified: verified — only the single
  `write_file` (this file) plus in-place `patch` appends to the same
  file were used; no source, test, config, or other doc was touched.
- Confirmation that no memory writes occurred: no `memory` tool calls were
  made during this task.

