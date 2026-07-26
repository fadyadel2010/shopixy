# 2026-07-02 PASS SF-SOF.FIN Smart Offer Financial Authority Certification

## Pages reviewed

- `PointOfSale.Storefront/src/storefront/providers/OfferProvider.tsx`
- `PointOfSale.Storefront/src/storefront/components/offers/OfferPopup.tsx`
- `PointOfSale.Storefront/src/storefront/services/smartOfferEngine.ts`
- `PointOfSale.Storefront/src/storefront/services/smartOfferCouponBridge.ts`
- `PointOfSale.Storefront/src/components/cart/CouponInput.tsx`
- `PointOfSale.Storefront/src/components/cart/CartSummary.tsx`
- `PointOfSale.Storefront/src/components/checkout/OrderSummary.tsx`
- `PointOfSale.Storefront/src/components/checkout/ReviewStep.tsx`
- `PointOfSale.Storefront/src/pages/CartPage.tsx`
- `PointOfSale.Storefront/src/pages/CheckoutPage.tsx`
- `PointOfSale.Storefront/src/pages/RecoverCheckoutPage.tsx`
- PointOfSale.Web Smart Offer Management financial presentation where directly consumed: `SmartOffersPage.jsx`, `SmartOfferEditor.jsx`, `SmartOfferPreview.jsx`, `adminSmartOfferReadAuthority.js`, and `adminSmartOffersApi.js` were treated as prior SF-SMO.READ/LIFECYCLE/RULE certified presentation/rule authority and were not modified.

## Backend endpoints reviewed

- `GET /api/storefront/smart-offers`
- `POST /api/storefront/smart-offers/{id}/redeem`
- `POST /api/storefront/cart/apply-coupon`
- `POST /api/storefront/checkout/pricing`
- `POST /api/storefront/checkout`

## Backend services reviewed

- `StorefrontSmartOffersController`
- `StorefrontCartController`
- `SmartOfferRedeemTokenService`
- `StorefrontCouponService`
- `CouponValidationService`
- `CheckoutController`
- `CheckoutService`
- `StorefrontPricingService`
- `TaxCalculationService`
- `ShippingCalculationService`
- `PromotionEngine` only where checkout pricing competition is directly consumed
- `PaymentLifecycleService` only where payment success confirms already persisted promotion/coupon/order authority

## Findings

Smart Offer Financial Authority is backend-owned. Storefront Smart Offer DTOs contain display and trigger candidates only, with no accepted discount, coupon value, currency, tax, shipping, payment fee, total, winner, or persistence fields. React, sessionStorage, and localStorage may decide popup display frequency and carry a short-lived redeem token candidate, but cannot create accepted financial impact.

Winning Smart Offer begins only after backend Smart Offer redeem admission proves tenant, authenticated customer, active Smart Offer, and active generated coupon, and checkout/cart pricing then enters SQL-backed coupon validation. Smart Offer itself does not select an accepted checkout financial winner in the frontend. Competition with promotions and coupons is resolved in backend coupon/promotion/checkout pricing authority.

Pricing begins at backend cart coupon validation and checkout pricing preview. The generated coupon value, discount type, percentage/fixed amount, maximum discount, minimum order, currency, tax, shipping, payment fee, and grand total are calculated or validated by backend services. Final checkout repeats authority inside the SQL transaction, compares backend recalculation with expected-pricing echo, and persists the SQL order snapshot only after validation.

Recovery converges to backend truth. Refresh, reconnect, restart, Redis flush, Mongo rebuild, projection rebuild, stale DTO replay, pending redeem-token replay, payment failure, and payment retry must re-enter backend cart/pricing/order/payment authority or fail closed. Redis cart state is a candidate/cache for cart operation state; SQL coupon/order/promotion authority and final checkout revalidation decide accepted financial impact. Mongo projections are visibility/read-model evidence only.

## Financial pipeline

Smart Offer Candidate begins in Storefront popup presentation and trigger filtering.

Backend Rule Evaluation begins at `StorefrontSmartOffersController.RedeemSmartOffer` and generated-coupon SQL revalidation for active tenant/customer/offer/coupon evidence.

Winner Resolution begins in backend coupon/promotion validation and checkout pricing competition; frontend never emits a winning Smart Offer.

Pricing begins in `StorefrontCouponService`/`CouponValidationService`, `CheckoutService.PreviewPricingAsync`, and `StorefrontPricingService.CalculateCheckoutAsync`.

Financial Validation begins in generated-coupon validation, expected-pricing validation, payment/shipping/tax validation, and `RevalidateCheckoutAuthorityInsideTransactionAsync`.

Expected Pricing begins as backend `CheckoutPricingPreviewResponseDto`; frontend echoes it only for stale-detection.

SQL Persistence begins when cart coupon state is persisted and when checkout saves SQL `Orders` with `PricingSnapshotJson`.

Accepted DTO Presentation begins only after backend cart/pricing/order DTO emission and frontend rendering of admitted `cart.*`, `pricing.*`, or order response fields.

## Boundaries

- Financial Boundary: owner is backend coupon/checkout/pricing services; recovery source is SQL coupon/order plus live backend cart revalidation; failure behavior is 400/409/503 or withheld pricing.
- Winner Boundary: owner is backend coupon/promotion/checkout resolution; recovery source is current SQL coupon/promotion/order evidence; failure behavior is rejected/expired token, invalid coupon, or checkout total mismatch.
- Pricing Boundary: owner is backend `StorefrontPricingService` after checkout service revalidation; recovery source is current backend pricing preview and final transaction recalculation; failure behavior is no order placement.
- Recovery Boundary: owner is backend cart recovery/checkout/session/order services; recovery source is SQL and live backend cart/pricing re-entry; failure behavior is re-review or fail closed.
- Presentation Boundary: owner is Storefront React rendering of accepted backend DTOs; recovery source is backend refetch/revalidation; failure behavior is loading/error/no render.

## Runtime hardening performed

No runtime behavior change was required. Added architecture regression freeze `PointOfSale.Tests/Architecture/StorefrontSmartOfferFinancialAuthorityCertificationTests.cs` and this findings document.

## Exact runtime files modified

None.

## Regression freezes added

- `PointOfSale.Tests/Architecture/StorefrontSmartOfferFinancialAuthorityCertificationTests.cs`

## Architecture documentation updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
