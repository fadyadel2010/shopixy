# 2026-07-02 PASS SF-SOF.RECOVERY-AUDIT Smart Offer Recovery and Audit Authority Certification

## Verdict

Smart Offer Recovery and Audit are production-certifiable for the inspected runtime paths. No runtime behavior change was required.

Backend SQL/cart/checkout/payment/coupon/audit/outbox/projection authority owns Redeem Recovery, Generated Coupon Recovery, Coupon Usage, Reservation, Reservation Release, Replay Protection, Payment Confirmation, Usage Confirmation, Audit Trail, Audit Events, Outbox Events, Projection Rebuild, Recovery Source, Persistence, and convergence. PointOfSale.Storefront owns recovery intent, refresh, retry, navigation, and accepted DTO presentation only.

## Pages reviewed

- `PointOfSale.Storefront/src/pages/RecoverCheckoutPage.tsx`
- `PointOfSale.Storefront/src/storefront/providers/OfferProvider.tsx`
- `PointOfSale.Storefront/src/storefront/components/offers/OfferPopup.tsx`
- `PointOfSale.Storefront/src/storefront/services/smartOfferEngine.ts`
- `PointOfSale.Storefront/src/storefront/services/smartOfferCouponBridge.ts`
- `PointOfSale.Storefront/src/components/cart/CouponInput.tsx`
- `PointOfSale.Storefront/src/store/cartStore.ts`
- `PointOfSale.Storefront/src/store/checkoutStore.ts`
- `PointOfSale.Storefront/src/pages/CheckoutPage.tsx`
- `PointOfSale.Storefront/src/pages/CheckoutPendingPage.tsx`
- `PointOfSale.Storefront/src/pages/OrderConfirmationPage.tsx`
- `PointOfSale.Web/src/pages/AuditLog.jsx`

## Backend endpoints reviewed

- `GET /api/storefront/smart-offers`
- `POST /api/storefront/smart-offers/{id}/redeem`
- `POST /api/storefront/cart/apply-coupon`
- `GET /api/storefront/cart`
- `POST /api/storefront/checkout/pricing`
- `POST /api/storefront/checkout`
- `POST /api/payments/cod/confirm/{orderId}`
- `GET /api/payments/status/{orderId}`
- `POST /api/storefront/checkout-session`
- `PATCH /api/storefront/checkout-session/{id}`
- `GET /api/storefront/checkout-session/recover/{token}`
- `GET /api/audit-logs`
- `GET /api/audit-logs/export`

## Backend services reviewed

- `StorefrontSmartOffersController`
- `SmartOfferRedeemTokenService`
- `StorefrontCartController`
- `StorefrontCouponService`
- `CouponValidationService`
- `CheckoutService`
- `CheckoutSessionController`
- `CheckoutSessionService`
- `CheckoutPaymentIntentRecoveryService`
- `PaymentLifecycleService`
- `AuditLogsController`
- `SmartOfferProjectionConsumer`
- `CouponProjectionConsumer`
- `DiscountReadModelRepairService`
- `ApplicationDbContext`

## Recovery findings

Recovery begins at backend re-entry, not in the browser. Smart Offer redeem recovery starts when `StorefrontSmartOffersController.RedeemSmartOffer` proves trusted tenant context, authenticated customer ownership, active SQL Smart Offer, and active generated SQL coupon, then issues a short-lived protected token. Generated Coupon recovery starts when `StorefrontCartController.ResolveSmartOfferRedemptionAsync` validates the token against the current tenant/customer and reloads the current active Smart Offer/generated coupon from SQL before normal coupon validation.

Checkout recovery starts only after `CheckoutSessionController.RecoverByToken` validates the signed recovery token, reloads the current SQL checkout session, verifies the stored recovery nonce, rejects converted sessions, and requires a live backend cart reload through `ICartService.GetCartAsync`. If the live cart cannot be revalidated, recovery fails closed with `RECOVERY_CART_REVALIDATION_FAILED`.

Payment recovery is backend-owned. `CheckoutPaymentIntentRecoveryService` repairs orders that committed before durable payment-intent creation, reuses provider idempotency keys, and releases inventory/coupon reservations when an unrecoverable pending order expires. Storefront pending/confirmation pages poll canonical backend payment status and do not show success until backend status is `PAID`.

## Audit findings

Audit begins in backend append-only SQL audit authority. `AuditLogsController` reads and exports tenant-scoped SQL `AuditLogs` under Owner/Admin authorization, uses no-store response semantics, and orders by backend timestamp. `AuditLog.jsx` sends no-store/no-cache GET requests and only presents backend DTOs; it has no audit mutation or storage recovery path.

## Replay findings

Redeem replay cannot fabricate usage because a replayed redeem token must still validate as protected, unexpired, tenant/customer-bound evidence and must resolve to the current active SQL Smart Offer/generated coupon before coupon validation. Coupon replay cannot fabricate usage because `CouponValidationService` reserves and confirms inside SQL transactions, locks coupon capacity, and collapses duplicate usage through SQL uniqueness. Checkout replay re-enters backend cart, pricing, expected-pricing, idempotency, and payment handoff authority. Payment replay re-enters payment lifecycle authority and duplicate confirmation collapses to existing SQL usage. Consumer replay cannot change Smart Offer usage because consumers only update derivative projections and coupon usage projection reloads SQL usage counts with event fences/freshness filters.

## Projection findings

Projection begins only after backend events or explicit SQL repair. `SmartOfferProjectionConsumer` writes Mongo storefront config from SQL Smart Offer rows; the reviewed Storefront Smart Offer runtime reads `StorefrontSmartOffersController` SQL directly. `CouponProjectionConsumer` reloads SQL coupon and SQL `CouponUsages`, uses event fences and freshness filters, and writes tombstones for missing SQL authority. `DiscountReadModelRepairService` rebuilds coupon/promotion read models from SQL authority and deletes stale Mongo documents. Mongo projection cannot become Recovery Source or SQL truth.

## Cache findings

React, Zustand, sessionStorage, localStorage, browser cache/BFCache, and Smart Offer display-frequency caches own only refresh, retry, presentation, navigation, and candidate transport. `smartOffer:pendingRedeemToken` is a short-lived redeem-token candidate only and must always re-enter backend token validation and current SQL Smart Offer/generated-coupon/coupon authority. Redis cart state is live cart/cache evidence and cannot become Smart Offer recovery authority; checkout recovery refuses recovery when live backend cart revalidation fails. Mongo is derivative projection evidence only.

## Cross-system findings

Coupons own generated-coupon validation, reservation, confirmation, release, and usage rows after Smart Offer token admission. Checkout owns final pricing revalidation, expected-pricing stale detection, SQL order snapshot persistence, and payment handoff. Payments own payment confirmation and usage confirmation re-entry. Orders own persisted order/pricing/payment state. Pricing, Promotions, Inventory, Affiliate, Marketplace, Builder, CMS, and Analytics cannot overwrite Smart Offer Recovery; they may only contribute through their own backend-owned, already certified authority boundaries.

## Recovery pipeline

Redeem -> Generated Coupon -> Checkout -> Payment -> Usage Confirmation -> Audit -> Outbox -> Projection -> Recovery -> Presentation.

- Recovery begins: backend redeem/cart/checkout/payment/session recovery after token/session/order authority admission.
- Audit begins: backend append-only SQL `AuditLogs` and backend audit/event services.
- Projection begins: backend event consumers or repair services after SQL authority reload.
- SQL persistence begins: SQL `StorefrontCartCouponStates`, checkout SQL order transaction, SQL coupon reservations/usages, payment lifecycle SQL state, and SQL audit/outbox writes.
- Presentation begins: Storefront/Web React only after accepted backend cart, checkout, payment-status, recovery, or audit DTOs.

## Boundary review

- Recovery Boundary: owner is backend SQL/cart/checkout/payment/coupon authority; recovery source is current SQL plus live backend cart/payment re-entry; failure behavior is fail closed, review required, or backend status pending/manual-review.
- Audit Boundary: owner is backend append-only SQL audit authority; recovery source is SQL `AuditLogs`; failure behavior is no-store read/export failure.
- Projection Boundary: owner is backend derivative consumers/repair; recovery source is SQL authority reload; failure behavior is duplicate collapse, stale no-op, tombstone, rebuild, or retry.
- Replay Boundary: owner is backend protected tokens, SQL transactions, SQL uniqueness, idempotency, event fences, and freshness filters; recovery source is current SQL; failure behavior is duplicate collapse or rejection.
- Presentation Boundary: owner is frontend rendering only; recovery source is accepted backend DTOs; failure behavior is loading, retry, error, or withheld success.

## Runtime hardening performed

No runtime behavior change was required. Existing runtime already keeps Smart Offer recovery backend-owned and presentation-only on the frontend. Added architecture regression freeze `PointOfSale.Tests/Architecture/StorefrontSmartOfferRecoveryAuditAuthorityCertificationTests.cs` and this findings document.

## Exact runtime files modified

None.

## Regression freezes added

- `PointOfSale.Tests/Architecture/StorefrontSmartOfferRecoveryAuditAuthorityCertificationTests.cs`

## Architecture documentation updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Explicit certification answers

- Did you review ONLY the directly involved runtimes? Yes.
- Which frontend files were modified? None.
- Which backend files were modified? None.
- Who owns Smart Offer Recovery? Backend SQL/cart/checkout/payment/coupon authority.
- Who owns Redeem Recovery? Backend `StorefrontSmartOffersController`, protected redeem token service, and SQL Smart Offer/generated-coupon authority.
- Who owns Generated Coupon Recovery? Backend SQL generated coupon plus `StorefrontCartController`/`StorefrontCouponService`/`CouponValidationService`.
- Who owns Smart Offer Usage? Backend coupon/order/payment SQL authority after generated-coupon admission.
- Who owns Usage Confirmation? Backend payment lifecycle and `CouponValidationService.ConfirmCouponUsageAsync`.
- Who owns Reservation? Backend checkout SQL transaction and `CouponValidationService.ReserveCouponAsync`.
- Who owns Reservation Release? Backend payment/order failure or expiry recovery through `ReleaseCouponReservationAsync`.
- Who owns Replay Protection? Backend protected tokens, idempotency, SQL uniqueness, event fences, and freshness filters.
- Who owns Audit Trail? Backend append-only SQL `AuditLogs`.
- Who owns Audit Events? Backend audit/event services.
- Who owns Outbox Events? Backend services after SQL authority admission.
- Who owns Projection Rebuild? Backend SQL-derived projection consumers and repair services.
- Who owns Recovery Source? Backend SQL truth plus live backend cart/payment re-entry.
- Can frontend fabricate Recovery? No.
- Can frontend fabricate Usage? No.
- Can replay fabricate Usage? No.
- Can duplicate payment create duplicate usage? No.
- Can duplicate consumers change Smart Offer state? No.
- Can Redis become Recovery Authority? No.
- Can Mongo Projection become Recovery Authority? No.
- Can React become Recovery Authority? No.
- Can LocalStorage become Recovery Authority? No.
- Can recovery diverge from SQL truth? No inspected path allows that; recovery re-enters SQL/backend authority or fails closed.
- Where does Recovery begin? Backend redeem/cart/session/payment recovery admission.
- Where does Audit begin? Backend SQL audit append/read authority.
- Where does Projection begin? Backend event consumer or SQL repair service after SQL reload.
- Where does SQL persistence begin? SQL cart coupon state, checkout order transaction, coupon reservation/usage, payment lifecycle, audit, and outbox writes.
- Where does Presentation begin? Frontend after accepted backend DTOs.
- Were runtime changes required? No.
- Is Smart Offer Recovery & Audit production-certifiable? Yes.
