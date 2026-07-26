# PASS SF-PA5.5 Checkout Pricing & Totals Authority Certification Findings

Date: 2026-06-29

## Verdict

PointOfSale.Storefront Checkout Pricing & Totals Authority is production-certifiable with no runtime code change required.

Storefront frontend owns only pricing presentation, formatting, loading/error states, retry intent, step gating, and temporary UI state. Backend `CheckoutController`, `CheckoutService`, `StorefrontPricingService`, `TaxCalculationService`, `ShippingCalculationService`, coupon/promotion validation, payment-method validation, final transaction revalidation, tenant currency validation, and SQL order snapshots own Subtotal, Discounts, Coupons, Shipping Cost, Payment Fee, Taxes, Grand Total, Pricing Admission, Pricing Snapshot, Currency Rules, Recovery, Persistence, and Projection Policy.

## Storefront Files Reviewed

- `PointOfSale.Storefront/src/pages/CheckoutPage.tsx`
- `PointOfSale.Storefront/src/pages/RecoverCheckoutPage.tsx`
- `PointOfSale.Storefront/src/components/checkout/OrderSummary.tsx`
- `PointOfSale.Storefront/src/components/checkout/ReviewStep.tsx`
- `PointOfSale.Storefront/src/components/checkout/ShippingStep.tsx`
- `PointOfSale.Storefront/src/components/checkout/PaymentStep.tsx`
- `PointOfSale.Storefront/src/store/checkoutStore.ts`
- `PointOfSale.Storefront/src/store/cartStore.ts`
- `PointOfSale.Storefront/src/store/cartStateResolver.ts`
- `PointOfSale.Storefront/src/types/checkout.ts`
- `PointOfSale.Storefront/src/types/cart.ts`
- `PointOfSale.Storefront/src/services/api/storefrontApi.ts`
- `PointOfSale.Storefront/src/components/cart/CouponInput.tsx`
- `PointOfSale.Storefront/src/components/cart/CartSummary.tsx`
- `PointOfSale.Storefront/src/utils/formatMoney.ts`
- `PointOfSale.Storefront/src/utils/currency.ts`

## Backend Endpoints Reviewed

- `POST /api/storefront/checkout/pricing`
- `POST /api/storefront/checkout`
- `POST /api/storefront/cart/apply-coupon`
- `POST /api/storefront/cart/remove-coupon`
- `GET /api/storefront/cart`
- `GET /api/storefront/shipping-methods`
- `GET /api/storefront/payment-methods`
- `POST /api/storefront/checkout-session`
- `PATCH /api/storefront/checkout-session/{id}`
- `GET /api/storefront/checkout-session/recover/{token}`

## Backend Services Reviewed

- `CheckoutController`
- `CheckoutService`
- `StorefrontPricingService`
- `TaxCalculationService`
- `ShippingCalculationService`
- `StorefrontCartController`
- `CartService`
- `StorefrontCouponService`
- `CouponValidationService`
- `CouponUsageService`
- `PromotionEngine`
- `ShippingMethodService`
- `PaymentMethodReadRepository`
- `CheckoutSessionController`
- `CheckoutSessionService`

## Pricing Findings

- Subtotal is backend-owned. Pricing preview and final checkout recompute subtotal from backend cart snapshots after SQL variant price re-entry, using `snapshot.Items.Sum(item => item.CurrentPrice * item.Quantity)`.
- Shipping cost is backend-owned. Storefront submits only a shipping method ID candidate; backend validates the method and calculates cost with `ShippingCalculationService`.
- Payment fee is backend-owned. Storefront submits only a payment method code candidate; backend validates the method and calculates fixed/percentage fee in `StorefrontPricingService`.
- Taxes are backend-owned. `TaxCalculationService` reads tenant tax settings/country policy and returns tax rate, source, country, and amount.
- Grand total is backend-owned. `StorefrontPricingService` computes `subtotal - discount + shipping + tax + paymentFee`, rounds it, and returns the pricing DTO.
- Storefront `OrderSummary` and `ReviewStep` render `pricing.*` fields only and fail closed to retry/unavailable states when pricing is absent or failed.

## Discount Findings

- Coupon validation is backend-owned through cart coupon application and checkout revalidation by coupon ID/code against SQL coupon authority.
- Coupon discount amount is recalculated by backend coupon authority during preview/final checkout before pricing.
- Promotion and cart-promotion discounts are backend-owned through `PromotionEngine`; failures fail closed for checkout pricing unless a backend policy explicitly clears discounts.
- Storefront displays backend coupon/promotion/cart-promotion amounts from the pricing DTO only. It does not calculate discount amounts or choose winning coupon/promotion policy.

## Tax Findings

- Storefront may pass a normalized shipping-country candidate only.
- Tax authority begins in backend `TaxCalculationService`, which reads tenant `SystemSettings`, tenant country, and configured country/default rates.
- Storefront never calculates tax; it formats backend `pricing.tax` and blocks final review when pricing is unavailable.

## Recovery Findings

- LocalStorage persists only `shippingMethodId`, `paymentMethod`, `selectedPaymentMethodCode`, and `step`; it does not persist pricing, totals, discounts, taxes, shipping cost, payment fee, or grand total.
- SessionStorage persists address candidates only.
- Checkout recovery requires live backend cart revalidation and then Storefront refetches cart, shipping methods, payment methods, and pricing. Stale session totals cannot hydrate pricing authority.
- Redis/cart snapshots are backend service inputs only; recovery and checkout pricing must re-enter current backend cart, coupon, promotion, shipping, payment, tax, and currency authority.

## Failure Findings

- 400 invalid request/country/pricing shape, 401 inactive or tenant-mismatched customer, 403 cart ownership mismatch, 404 missing cart, 409 total mismatch/lock/conflict, 422 validation failures where applicable, and 503 promotion/infrastructure failures fail closed.
- `CheckoutExpectedPricingDto.Validate` rejects malformed or internally inconsistent client pricing echoes.
- `EnsureExpectedPricingMatches` rejects mismatches for subtotal, discount, coupon discount, promotion discount, cart promotion discount, shipping, tax, payment fee, manual discount, and total.
- Storefront maps pricing failures to unavailable/retry states and disables place order while pricing is missing/loading/failed.

## Boundary Review

- Pricing Boundary: backend `CheckoutService` + `StorefrontPricingService`; recovery source is current backend cart/pricing; failure blocks checkout.
- Discount Boundary: backend coupon validation and promotion engine; recovery source is current SQL/cart coupon state; failure blocks or clears only by backend policy.
- Tax Boundary: backend `TaxCalculationService`; recovery source is tenant settings/country policy; failure withholds pricing.
- Shipping Boundary: backend shipping-method validation and `ShippingCalculationService`; recovery source is current backend method/cart; failure marks stale or rejects.
- Payment Fee Boundary: backend payment-method validation and `StorefrontPricingService.CalculatePaymentFee`; recovery source is current backend payment method/pricing; failure marks stale or rejects.
- Totals Boundary: backend expected-pricing comparison and order persistence; recovery source is current backend pricing; mismatch returns conflict.
- Snapshot Boundary: backend order creation/refresh writes SQL `Subtotal`, `DiscountAmount`, `PromotionDiscountAmount`, `ManualDiscountAmount`, `TotalDiscountAmount`, `ShippingCost`, `TaxAmount`, `PaymentFee`, `Total`, `Currency`, and `PricingSnapshotJson`.
- Presentation Boundary: Storefront `OrderSummary` and `ReviewStep` format backend DTO values only.
- Recovery Boundary: checkout-session recovery and browser storage restore candidates only; current backend pricing must be fetched again.

## Pricing Pipeline

Authoritative Cart
-> Checkout Session
-> Address Candidate
-> Shipping Candidate
-> Payment Candidate
-> Backend Pricing Calculation
-> Pricing DTO
-> Storefront Presentation
-> Order Snapshot

- Pricing calculation begins in `CheckoutService.PreviewPricingAsync` / `CreateOrderAsync` / `RevalidateCheckoutAuthorityInsideTransactionAsync` when backend recomposes the cart snapshot and calls `StorefrontPricingService.CalculateCheckoutAsync`.
- Pricing authority begins at backend cart/variant/coupon/promotion/shipping/payment/tax/currency re-entry, before the pricing DTO is emitted.
- Totals snapshot begins when backend order creation/refresh persists recalculated financial fields and `PricingSnapshotJson`.
- Storefront presentation begins only after `CheckoutPricingPreviewResponseDto` or backend order response reaches Storefront components.

## Runtime Hardening Performed

- No runtime code change was required. Existing Storefront checkout pricing runtime already renders backend pricing only, does not persist pricing authority, refetches pricing on cart/selection/country changes, blocks final submit without a current pricing DTO, and final checkout rejects stale or forged pricing echoes.

## Exact Runtime Files Modified

- None.

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/StorefrontCheckoutPricingTotalsAuthorityCertificationTests.cs`

## Architecture Documentation Updates

- Added this findings document.

## AI Context Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
