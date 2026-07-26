# 2026-07-01 PASS SF-CPN4 Coupon Redemption Authority Certification Findings

Scope reviewed: coupon redemption presentation and redemption authority boundaries only. Coupon Read, Lifecycle, Eligibility, Promotion, Smart Offers, Affiliate, Marketplace, Builder, CMS, and Analytics authorities were treated as prior architectural truth and were not re-certified.

Pages and frontend runtime reviewed:
- `PointOfSale.Storefront/src/components/cart/CouponInput.tsx`
- `PointOfSale.Storefront/src/store/cartStore.ts`
- `PointOfSale.Storefront/src/services/api/storefrontApi.ts`

Backend endpoints reviewed:
- `POST /api/storefront/cart/apply-coupon`
- `POST /api/storefront/cart/remove-coupon`
- `GET /api/storefront/cart` only where it revalidates coupon presentation before response
- checkout order creation and payment confirmation services directly consumed by coupon redemption

Backend services reviewed:
- `StorefrontCartController`
- `StorefrontCouponService`
- `ICouponValidationService` / `CouponValidationService`
- `CheckoutService`
- `PaymentLifecycleService`
- `CouponReservation`, `CouponUsage`, and `CouponProjectionConsumer`

Findings:
- Coupon Redemption remains backend-owned. Frontend submits coupon code or token candidates only and renders the accepted backend `CartDto` returned by cart APIs.
- Accepted cart coupon presentation begins after `StorefrontCartController.ApplyCoupon` validates the request, authenticates customer/tenant authority, delegates validation to `StorefrontCouponService`, receives backend coupon identity/discount evidence, persists server-accepted cart coupon state, and revalidates the coupon before responding.
- Accepted order redemption begins only when checkout revalidates coupon identity/code/discount through `ValidateCouponByIdAsync`, creates a backend order, and reserves the coupon with `ReserveCouponAsync` inside the serializable SQL checkout transaction.
- Accepted usage count begins only when `PaymentLifecycleService` calls `ConfirmCouponUsageAsync` and SQL creates or reuses a unique `CouponUsage` for the order.
- Duplicate checkout, retry, replay, and gateway retry cannot redeem twice because order idempotency, active-cart uniqueness, coupon reservation uniqueness, and `CouponUsages(TenantId, OrderId)` uniqueness collapse duplicates to existing backend truth or fail closed.
- React, React Query, Zustand, LocalStorage, SessionStorage, browser cache/BFCache, Redis cart cache, and Mongo coupon projections are not redemption authority. Recovery converges to SQL coupon/order/usage truth or fails closed.

Runtime hardening performed: none. Existing runtime already preserved the backend-owned redemption boundary.

Regression freeze added:
- `PointOfSale.Tests/Architecture/CouponRedemptionAuthorityCertificationTests.cs`

Exact runtime files modified: none.
