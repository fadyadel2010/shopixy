# PASS SF-CPN5 Coupon Financial Authority Certification

Review scope: PointOfSale.Storefront coupon financial presentation, cart, checkout pricing, checkout submission, and directly consumed backend cart/coupon/checkout/pricing services.

Finding: Coupon Financial Authority is backend-owned and production-certifiable. Frontend submits coupon code/token candidates and renders accepted backend DTOs only. Accepted financial discount exists only after backend coupon validation, pricing calculation, currency validation, checkout total validation, SQL order snapshot persistence, and coupon reservation authority.

Runtime hardening performed: corrected stale checkout DTO comments that contradicted runtime revalidation, and added `CouponFinancialAuthorityCertificationTests` as the regression freeze. No behavioral runtime code change was required.

Reviewed pages/components: `CouponInput.tsx`, `CartSummary.tsx`, `OrderSummary.tsx`, `ReviewStep.tsx`, `CheckoutPage.tsx`, `cartStore.ts`, `checkoutStore.ts`, `storefrontApi.ts`, `cart.ts`, `checkout.ts`.

Reviewed backend endpoints/services: `POST /api/storefront/cart/apply-coupon`, `POST /api/storefront/cart/remove-coupon`, `GET /api/storefront/cart`, `POST /api/storefront/checkout/pricing`, `POST /api/storefront/checkout`, `StorefrontCartController`, `StorefrontCouponService`, `CouponValidationService`, `CartService`, `CheckoutController`, `CheckoutService`, `StorefrontPricingService`, `TaxCalculationService`.

Financial pipeline: Coupon Candidate begins in frontend entry as code/token only. Backend Validation begins in `StorefrontCartController.ApplyCoupon` through `StorefrontCouponService` and `CouponValidationService`. Financial Validation begins in `CouponValidationService.ValidateCouponInternalAsync`. Pricing begins in `CheckoutService.PreviewPricingAsync` and final checkout through `_storefrontPricingService.CalculateCheckoutAsync`. Canonicalization begins when backend recalculates coupon discount, pricing, tax, shipping, payment fee, and totals. SQL persistence begins in `CheckoutService.CreateOrderAsync` when the order and pricing snapshot are saved in the serializable transaction. Presentation begins after accepted cart/pricing/order DTOs reach Storefront components.

Authority boundaries: Backend owns Discount Type, Discount Value, Percentage, Fixed Amount, Currency, Maximum Discount, Minimum Order, Cart Total, Order Total, Subtotal, Tax, Shipping Discount where modeled, Final Discount, Stacking Rules, Financial Validation, Pricing Engine, Canonical Financial Totals, Persistence, Recovery, and Audit. Frontend owns Coupon Entry, Presentation, Formatting, Retry, Navigation, and candidate submission only.
