# PASS SF-PRM.FIN Promotion Financial Authority Certification

Date: 2026-07-02

## Verdict

PointOfSale.Storefront Promotion Financial Authority is production-certifiable after source-documentation hardening. No runtime behavior change was required.

Frontend Storefront submits cart, shipping method, payment method, country, coupon, and expected-pricing candidates only. Accepted Promotion discount, Winning Promotion, stacking outcome, currency, tax, shipping, payment fee, grand total, expected-pricing validation, SQL order snapshot, recovery, and paid promotion usage exist only after backend pricing calculation and final checkout admission.

## Pages Reviewed

- `PointOfSale.Storefront/src/store/checkoutStore.ts`
- `PointOfSale.Storefront/src/types/checkout.ts`
- `PointOfSale.Storefront/src/types/cart.ts`
- `PointOfSale.Storefront/src/components/checkout/OrderSummary.tsx`
- `PointOfSale.Storefront/src/components/cart/CartSummary.tsx`
- Prior certified evidence: `docs/architecture/storefront-checkout-pricing-totals-authority-certification-findings.md`
- Prior certified evidence: `docs/architecture/storefront-promotions-authority-certification-findings.md`

## Backend Endpoints Reviewed

- `POST /api/storefront/checkout/pricing`
- `POST /api/storefront/checkout`
- `GET /api/storefront/cart`
- `POST /api/storefront/cart/apply-coupon`
- `POST /api/storefront/cart/remove-coupon`
- `GET /api/storefront/shipping-methods`
- `GET /api/storefront/payment-methods`
- `GET /api/storefront/checkout-session/recover/{token}`

## Backend Services Reviewed

- `CheckoutController`
- `CheckoutService`
- `StorefrontPricingService`
- `PromotionEngine`
- `CartService`
- `StorefrontCartController`
- `StorefrontCouponService`
- `CouponValidationService`
- `TaxCalculationService`
- `ShippingCalculationService`
- `PaymentLifecycleService`
- `CheckoutSessionController`

## Financial Findings

- Backend owns Promotion Financial Authority. `CheckoutService.PreviewPricingAsync` reloads the cart, recalculates prices, reconciles coupon/promotion authority, validates shipping/payment candidates, calls `StorefrontPricingService.CalculateCheckoutAsync`, and emits accepted pricing.
- Final checkout repeats authority re-entry inside the SQL transaction through `RevalidateCheckoutAuthorityInsideTransactionAsync`, recalculates promotion/pricing, validates expected pricing, and persists SQL order snapshot fields.
- The frontend `expectedPricing` echo is stale-detection evidence only. `EnsureExpectedPricingMatches` rejects modified discount, promotion discount, cart promotion discount, shipping, tax, payment fee, manual discount, or total.

## Pricing Findings

- Pricing begins in backend `CheckoutService.PreviewPricingAsync` and final checkout transaction revalidation.
- `StorefrontPricingService` computes subtotal from backend snapshot item current prices, aggregates backend coupon and promotion discount buckets, computes shipping, tax, payment fee, and final total.
- Source documentation hardening corrected `CheckoutPricingPreviewResponseDto.PromotionCalculationFailed` so it no longer describes fail-open pricing.

## Stacking Findings

- Stacking is disabled in the inspected runtime. `PromotionEngine.CanCombineCouponWithOffersAsync` returns false.
- Backend `PromotionEngine` calculates the best SQL offer, consumes only a prevalidated coupon discount, then `DetermineWinner` selects one winner by larger discount, with coupon as tie break.
- Frontend cannot select, inject, or stack promotions.

## Currency Findings

- Currency authority remains backend-owned through tenant currency, checkout currency validation, payment currency validation, and SQL order snapshot.
- Storefront formats accepted backend currency/pricing evidence only and does not submit currency authority in checkout pricing.

## Recovery Findings

- LocalStorage persists only shipping/payment candidates and step; sessionStorage persists address candidates only.
- Checkout recovery revalidates live backend cart state and requires fresh backend pricing. Redis cart state and Mongo projections are derivative inputs/evidence and cannot restore accepted financial totals.
- Recovery converges to SQL order/pricing truth after checkout persistence.

## Concurrency Findings

- Duplicate submit is gated in frontend presentation and backend idempotency/locking remains decisive.
- Final checkout locks shipping/payment authority rows, recalculates cart/catalog/discount authority inside the transaction, and rejects stale expected pricing.
- Paid offer usage is confirmed only after payment success by locking current SQL promotion authority, revalidating lifecycle/minimum/usage/current discount, and writing one durable `PromotionUsage`.

## Cache Findings

- React, React Query, Zustand, LocalStorage, SessionStorage, browser cache/BFCache, Redis, and Mongo projections may display, invalidate, refetch, or recover candidates only.
- No inspected cache can become Promotion Financial Authority because accepted pricing must be backend-calculated and final checkout must match backend recalculation.

## Cross-System Findings

- Coupons enter promotion conflict resolution only as backend-prevalidated coupon discount evidence.
- Checkout, Orders, Payments, Shipping, Tax, Smart Offers, Affiliate, Marketplace, Inventory, Builder, CMS, and Analytics cannot overwrite Promotion Financial Authority.
- Promotion cannot overwrite another domain authority; it contributes accepted backend discount evidence to checkout pricing and SQL order snapshots only.

## Financial Pipeline

Promotion Candidate -> Backend Rule Evaluation -> Pricing Engine -> Promotion Resolution -> Financial Validation -> Expected Pricing -> Checkout Transaction -> SQL Order Snapshot -> Accepted DTO -> Presentation.

- Promotion begins: backend SQL `Promotions` and backend cart/promotion evaluation.
- Pricing begins: `CheckoutService.PreviewPricingAsync` / final transaction revalidation before `StorefrontPricingService.CalculateCheckoutAsync`.
- Financial Authority begins: backend cart/catalog/coupon/promotion/shipping/payment/tax/currency re-entry before accepted pricing DTO emission.
- Winning Promotion begins: `PromotionEngine.CalculateAsync` / `DetermineWinner`.
- SQL persistence begins: `RefreshOrderAuthoritySnapshot`, `_dbContext.Orders.Add(order)`, and `SaveChangesAsync` inside checkout transaction.
- Presentation begins: Storefront `OrderSummary`/checkout state after accepted backend pricing or order DTO admission.

## Boundary Review

- Pricing Boundary: backend `CheckoutService` and `StorefrontPricingService`; recovery source is current backend cart and SQL order pricing; failure rejects/withholds checkout.
- Discount Boundary: backend `PromotionEngine` plus SQL coupon validation; recovery source is current SQL/cart authority; failure fails closed.
- Stacking Boundary: backend `PromotionEngine.DetermineWinner`; recovery source is backend recalculation; frontend stack attempts are ignored or rejected by expected pricing mismatch.
- Currency Boundary: tenant/order/payment backend currency validation; recovery source is SQL tenant/order truth; failure aborts payment/checkout.
- Recovery Boundary: backend cart/session/order recovery; recovery source is live backend cart or SQL order snapshot; stale snapshots cannot restore pricing authority.
- Presentation Boundary: Storefront components render accepted pricing DTO fields only; missing pricing withholds/retries.

## Runtime Hardening Performed

- Corrected stale source documentation on `CheckoutPricingPreviewResponseDto.PromotionCalculationFailed`; runtime already fails closed on promotion calculation failure.

## Exact Runtime Files Modified

- `PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/StorefrontPromotionFinancialAuthorityCertificationTests.cs`

## Architecture Documentation Updates

- Added this findings document.
- Updated all five `.ai-context` authority files with `PASS SF-PRM.FIN`.
