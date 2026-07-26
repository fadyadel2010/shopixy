# PASS SF-PAY2 Storefront Payment Method Authority Certification Findings

Date: 2026-07-02

## Verdict

PointOfSale.Storefront Payment Method selection runtime is production-certifiable with no runtime code change required.

Payment Method Authority remains backend-owned. Storefront owns only selected payment-method candidates, loading, retry, refresh, navigation, and admitted presentation. Accepted Payment Method exists only after backend read/admission and checkout/pricing validation.

## Scope Reviewed

- `PointOfSale.Storefront/src/pages/CheckoutPage.tsx`
- `PointOfSale.Storefront/src/components/checkout/PaymentStep.tsx`
- `PointOfSale.Storefront/src/components/checkout/ReviewStep.tsx`
- `PointOfSale.Storefront/src/store/checkoutStore.ts`
- `PointOfSale.Storefront/src/types/checkout.ts`
- `PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.ts`
- `PointOfSale.Storefront/src/pages/RecoverCheckoutPage.tsx`
- `PointOfSale.API/Controllers/StorefrontPaymentMethodsController.cs`
- `PointOfSale.API/Controllers/CheckoutController.cs`
- `PointOfSale.API/Controllers/CheckoutSessionController.cs`
- `PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs`
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.API/Services/Pricing/StorefrontPricingService.cs`
- `PointOfSale.Infrastructure/MongoDB/Services/PaymentMethodReadRepository.cs`
- `PointOfSale.Core/Payments/PaymentMethodConfigurationValidator.cs`

## Findings

- Payment Method begins as a candidate in `PaymentStep` and `checkoutStore.selectedPaymentMethodCode/paymentMethod`.
- Authority begins only in backend no-store payment-method reads and definitive checkout/pricing admission through `CheckoutService.ValidatePaymentMethodAsync` and `LoadPaymentMethodAuthorityAsync`.
- Validation begins in backend request DTO validation, SQL tenant payment-method lookup, enabled/deleted checks, configuration validation, supported-code checks, amount-limit checks, backend pricing, expected-pricing comparison, tenant currency validation, and final transaction row locks.
- SQL truth begins in `TenantPaymentMethods`; checkout snapshots persist accepted `PaymentMethodCode`, `PaymentMethodName`, and `PaymentFee` only after backend admission.
- Presentation begins only after backend DTO emission and Storefront DTO admission, or after accepted pricing/order DTOs.
- Frontend DTO admission rejects malformed lists/items, missing identity/code/display evidence, missing support/availability booleans, invalid fee/order/amount-limit evidence, duplicate identities/codes, and invalid limit ranges before Zustand state.
- React, React Query, Zustand, LocalStorage, SessionStorage, browser cache/BFCache, Redis, Mongo projections, stale DTO replay, recovery, refresh, reconnect, restart, Redis flush, Mongo rebuild, projection rebuild, and multiple tabs cannot fabricate accepted Payment Method Authority.
- Mongo payment-method documents are derivative read-model evidence only; projection misses/staleness fall back to SQL authority.
- Recovery restores only `paymentMethodCode` candidates, resets checkout state, requires live backend cart revalidation, and returns no checkout/payment tokens. Current payment-method reads then re-admit or mark the candidate stale.
- Gateway/provider choice is resolved by backend from the accepted SQL payment-method code. Storefront can submit only a candidate code and cannot enable disabled methods, bypass provider configuration, or choose an accepted gateway.
- Cross-system boundaries with Orders, Coupons, Promotions, Smart Offers, Affiliate, Marketplace, Inventory, and Checkout carry candidates, pricing/order snapshots, or accepted DTO evidence only; none can overwrite Payment Method Authority.

## Runtime Changes

- None.

## Regression Freeze

- `PointOfSale.Tests/Architecture/PaymentMethodAuthorityCertificationTests.cs`

## AI Context Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
