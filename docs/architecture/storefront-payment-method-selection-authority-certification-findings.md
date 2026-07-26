# PASS SF-PA5.4 Payment Method Selection Authority Certification Findings

Date: 2026-06-29

## Verdict

PointOfSale.Storefront Payment Method Selection Authority is production-certifiable after review-step presentation hardening.

Storefront frontend owns only Payment Method Candidate state, selected code state, loading/error presentation, stale-selection presentation, manual-wallet candidate fields, formatting, and navigation. Backend `StorefrontPaymentMethodsController`, `PaymentMethodReadRepository`, SQL `TenantPaymentMethods`, `PaymentMethodConfigurationValidator`, `CheckoutController`, `CheckoutService`, `StorefrontPricingService`, final transaction revalidation, and SQL order snapshots own Payment Method Identity, Eligibility, Availability, Fees, Admission, Risk/Configuration Policy, Currency/amount compatibility, Recovery, Persistence, Projection Policy, and Checkout Payment Snapshot.

## Storefront Files Reviewed

- `PointOfSale.Storefront/src/components/checkout/PaymentStep.tsx`
- `PointOfSale.Storefront/src/components/checkout/ReviewStep.tsx`
- `PointOfSale.Storefront/src/components/checkout/ManualWalletFields.tsx`
- `PointOfSale.Storefront/src/pages/CheckoutPage.tsx`
- `PointOfSale.Storefront/src/pages/RecoverCheckoutPage.tsx`
- `PointOfSale.Storefront/src/store/checkoutStore.ts`
- `PointOfSale.Storefront/src/types/checkout.ts`
- `PointOfSale.Storefront/src/services/api/storefrontApi.ts`
- `PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.ts`
- `PointOfSale.Storefront/src/features/checkout/paymentFlow.ts`

## Backend Endpoints Reviewed

- `GET /api/storefront/payment-methods`
- `POST /api/storefront/checkout/pricing`
- `POST /api/storefront/checkout`
- `POST /api/storefront/checkout-session`
- `PATCH /api/storefront/checkout-session/{id}`
- `GET /api/storefront/checkout-session/recover/{token}`

## Backend Services Reviewed

- `StorefrontPaymentMethodsController`
- `PaymentMethodReadRepository`
- `PaymentMethodProjectionConsumer`
- `PaymentMethodConfigurationValidator`
- `CheckoutController`
- `CheckoutService`
- `StorefrontPricingService`
- `CheckoutSessionController`
- `CheckoutSessionService`

## Findings

- Payment identity: frontend stores/submits a payment-method code candidate only. Backend list reads expose current tenant-owned enabled/configuration-valid methods and final checkout admits the submitted code through locked current SQL `TenantPaymentMethods`.
- Eligibility: backend owns availability/configuration and amount-limit admission. Frontend can disable presentation from backend DTOs and current pricing preview, but final checkout repeats admission against backend pricing and SQL method authority.
- Fees: frontend displays method fee metadata and backend pricing preview `paymentFee`; it does not calculate authoritative payment fees. `StorefrontPricingService.CalculatePaymentFee` owns fee calculation for preview and final checkout.
- Selection: `selectPaymentMethod(code)` records a candidate. Backend authority begins only at pricing preview/final checkout validation and final transaction revalidation.
- Snapshot: checkout order creation/refresh snapshots `PaymentMethodCode`, `PaymentMethodName`, and `PaymentFee` from backend-admitted payment method and pricing authority.
- Recovery: localStorage and checkout recovery restore only `paymentMethodCode` candidates. Recovery requires live cart revalidation; subsequent payment-method fetch marks missing candidates stale and blocks review/final checkout.
- Failure: missing tenant, invalid payment config, disabled/deleted method, stale selection, amount-limit mismatch, expected-pricing mismatch, unavailable cart, auth degradation, 400/401/403/404/409/422/503, and network failure fail closed or require refetch/review.
- Adversarial: modified payment code, fee edits, DTO injection, React state changes, replayed recovery code, localStorage edits, Redis/Mongo projection drift, and browser refresh cannot create Payment Method Authority because backend admission and snapshot recompute from SQL/pricing authority.

## Payment Pipeline

Authoritative Checkout Session
-> Authoritative Checkout Address
-> Authoritative Shipping Method
-> Backend Payment Eligibility
-> Payment Methods DTO
-> Payment Candidate
-> Backend Payment Admission
-> Checkout Payment Snapshot
-> Presentation

- Payment Candidate begins in `PaymentStep`, `checkoutStore.selectedPaymentMethodCode/paymentMethod`, autosave `paymentMethodCode`, and recovery-restored `session.paymentMethod?.code/session.paymentMethodCode`.
- Payment Authority begins in backend payment-method list admission for presentation and definitively in `CheckoutService.ValidatePaymentMethodAsync` / `LoadPaymentMethodAuthorityAsync` during pricing preview and final checkout transaction.
- Payment Snapshot begins when backend order creation/refresh persists `PaymentMethodCode`, `PaymentMethodName`, and `PaymentFee`.
- Storefront presentation begins when admitted payment method DTOs or admitted pricing/order snapshots reach Storefront components.

## Boundary Review

- Payment Boundary: backend SQL `TenantPaymentMethods` and checkout validation own authority; recovery source is current backend method/cart/pricing reads; failure withholds or marks stale.
- Eligibility Boundary: backend configuration validator, enabled/deleted flags, amount limits, tenant scope, and final transaction revalidation own eligibility; failure returns unavailable/stale/conflict.
- Fee Boundary: `StorefrontPricingService` owns payment fee calculation; frontend displays preview/order values only; mismatch fails checkout.
- Identity Boundary: backend method `Code`/`Id`/display fields own identity; frontend code is a candidate; missing current DTO fails/stales.
- Snapshot Boundary: backend order persistence owns `PaymentMethodCode`, `PaymentMethodName`, and `PaymentFee`; frontend cannot write snapshots.
- Presentation Boundary: Storefront renders current DTO/pricing evidence and stale-selection messages only.
- Recovery Boundary: checkout-session recovery and localStorage restore candidates only; current backend reads re-admit or fail closed.

## Runtime Hardening Performed

- Updated `ReviewStep` to render the currently admitted backend `PaymentMethodDto` display name and description instead of hardcoded frontend payment method names. This keeps review presentation derivative from the backend payment-method DTO.

## Exact Runtime Files Modified

- `PointOfSale.Storefront/src/components/checkout/ReviewStep.tsx`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/StorefrontPaymentMethodSelectionAuthorityCertificationTests.cs`

## Architecture Documentation Updates

- Added this findings document.

## AI Context Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
