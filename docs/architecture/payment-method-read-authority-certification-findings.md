# PASS SF-PM.READ Payment Method Read Authority Certification Findings

Date: 2026-07-02

## Verdict

PointOfSale.Storefront payment method read and selection presentation is production-certifiable after read-boundary hardening.

Backend SQL `TenantPaymentMethods`, admin payment configuration, backend configuration validation, projection consumers, SQL fallback reads, storefront read admission, and accepted DTO emission own Payment Method identity, method code, display name, online/offline type evidence, gateway/configuration policy, enabled state, availability, display order, amount limits, tenant scope, persistence, projection recovery, and SQL truth.

PointOfSale.Storefront owns loading, retry, refresh, selected method candidate, stale-selection presentation, navigation, rendering, and accepted DTO presentation only.

## Files Reviewed

- `PointOfSale.Storefront/src/store/checkoutStore.ts`
- `PointOfSale.Storefront/src/components/checkout/PaymentStep.tsx`
- `PointOfSale.Storefront/src/components/checkout/ReviewStep.tsx`
- `PointOfSale.Storefront/src/pages/RecoverCheckoutPage.tsx`
- `PointOfSale.Storefront/src/types/checkout.ts`
- `PointOfSale.API/Controllers/StorefrontPaymentMethodsController.cs`
- `PointOfSale.API/Controllers/PaymentMethodsAdminController.cs`
- `PointOfSale.Infrastructure/MongoDB/Services/PaymentMethodReadRepository.cs`
- `PointOfSale.Infrastructure/Consumers/PaymentMethodProjectionConsumer.cs`
- `PointOfSale.Infrastructure/MongoDB/Documents/PaymentMethodDocument.cs`
- `PointOfSale.Core/Entities/TenantPaymentMethod.cs`

## Runtime Hardening

- Added no-store response semantics to admin and Storefront payment-method reads.
- Added no-store request headers for Storefront payment-method reads.
- Added backend Storefront suppression for unsupported projected payment-method codes before DTO emission.
- Added Storefront DTO admission before Zustand state is updated, rejecting malformed list payloads, missing or malformed identity, duplicate id/code evidence, missing display name, missing enabled/availability booleans, invalid fee/fee type, invalid display order, and invalid amount limits.

## Authority Pipeline

Admin configuration candidate -> SQL `TenantPaymentMethods` -> backend validation/encryption -> outbox event -> projection consumer reloads SQL authority -> Mongo read model or SQL fallback -> `StorefrontPaymentMethodsController` tenant/configuration/code admission -> `PaymentMethodDto` -> Storefront DTO admission -> Storefront presentation and selected-code candidate.

Payment Method begins in SQL `TenantPaymentMethods` after backend admin admission.
Authority begins when trusted backend tenant/session/role context reaches SQL payment-method configuration and read services.
SQL truth begins in `TenantPaymentMethods`.
Presentation begins only after backend DTO emission and Storefront DTO admission.

## Cache And Recovery

React, Zustand, localStorage, sessionStorage, browser cache, and React component state cannot become Payment Method Authority. The persisted checkout state stores only selected payment-method code candidates, not the payment-method list or fee/configuration authority.

Mongo is a derivative projection. Projection consumers reload SQL authority, fence duplicate/stale events, write tombstones for deleted authority, and SQL fallback is used when projection evidence is missing or stale. Redis is not a payment-method read authority in this runtime.

Refresh, reconnect, browser restart, Redis flush, Mongo rebuild, projection rebuild, stale DTO replay, and local storage tampering can only trigger refetch, stale-selection handling, withholding, or backend re-entry.

## Cross-System Boundary

Checkout consumes selected payment method as a candidate and revalidates against current backend payment-method authority during pricing preview and final checkout. Orders, Coupons, Promotions, Smart Offers, Affiliate, Marketplace, payment lifecycle, payment sessions, payment intents, gateway verification, Stripe, Paymob, Vodafone Cash, and manual payment approval were not reviewed beyond this read/presentation boundary and cannot overwrite Payment Method Read Authority.

## Certification Answers

- Payment Method Identity: backend SQL/admin/read DTO authority.
- Method Code: backend.
- Gateway Type / online-offline capability evidence: backend.
- Display Name: backend.
- Enabled State: backend.
- Availability: backend.
- Currency Support: not modeled as payment-method read authority in this runtime; checkout tenant currency validation remains backend-owned.
- Country Restrictions: not modeled as payment-method read authority in this runtime; checkout/address country validation remains backend-owned.
- Display Order: backend.
- Tenant Scope: backend trusted tenant context.
- Frontend fabrication: rejected before presentation unless admitted backend DTO evidence exists.
- Cache authority: React, React Query, Zustand, LocalStorage, browser cache, Redis, and Mongo projection cannot become authority.
- Replay/recovery: replay cannot fabricate authority; stale methods are suppressed, refetched, marked stale, or revalidated against SQL-backed backend reads.

## Regression Freeze

- `PointOfSale.Tests/Architecture/AdminPaymentMethodReadAuthorityCertificationTests.cs`
