# Vodafone Cash Payment Authority Certification (PASS SF-PAY6)

## Authority

PointOfSale.Storefront Vodafone Cash payment runtime is backend-owned and Presentation Only. Storefront owns wallet display, transfer instructions, reference/phone candidate entry, receipt/proof candidate upload, loading, retry, refresh, navigation, and admitted DTO presentation only.

Backend owns PaymentIntent, checkout token, merchant wallet, wallet identity, wallet configuration, reference number, reference validation, receipt evidence admission, amount, currency, customer identity, verification, payment status, order status, persistence, recovery, and audit.

## Findings

- Vodafone Cash begins as the `VodafoneCash` wallet type presented inside the Storefront `MANUALWALLET` payment-method lane.
- Frontend can display the backend merchant wallet and instructions, copy the wallet number, collect sender wallet/reference candidates, and upload receipt evidence candidates through the backend token-bound manual-wallet upload API.
- Storefront DTO admission requires backend `paymentIntentId`, `isManualWallet`, pending/awaiting-verification status, merchant wallet evidence, wallet type, and instructions before payment presentation.
- Backend checkout admission revalidates tenant, customer/session owner, cart, order, payment method, expected pricing, proof-session binding, wallet type, tenant-scoped proof key, amount, and currency before creating SQL `PaymentIntent`.
- Merchant wallet and wallet instructions are derived from tenant payment-method configuration on the backend and serialized into provider metadata; frontend values are never accepted as wallet identity.
- Payment reference is backend-generated on `PaymentIntent`; customer-entered wallet phone/reference evidence remains candidate evidence for review.
- Verification begins only in backend admin manual-wallet confirmation/rejection routes after role authorization, tenant-scoped `PaymentIntent` lookup, provider check, state-machine validation, and `IPaymentLifecycleService` status transition.
- Recovery clears manual wallet type, phone, proof key, and upload token, requiring re-entry; payment-intent recovery reloads current SQL manual-wallet authority.
- React, React Query, Zustand, LocalStorage, SessionStorage, browser cache/BFCache, Redis, Mongo projections, stale DTO replay, duplicate upload, duplicate checkout, duplicate verification, recovery, restart, Redis flush, Mongo rebuild, projection rebuild, and direct browser tampering cannot fabricate accepted Vodafone Cash payment or verification.

## Runtime Hardening Performed

- No runtime behavior change was required. Existing SF-PAY1 through SF-PAY3 hardening already keeps Vodafone Cash inside backend-owned ManualWallet authority.

## Exact Runtime Files Modified

- None.

## Regression Freeze

- `PointOfSale.Tests/Architecture/VodafoneCashPaymentAuthorityCertificationTests.cs`

## Verification

- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter VodafoneCashPaymentAuthorityCertificationTests`

## Certification

Vodafone Cash Payment remains backend-owned. Frontend cannot fabricate succeeded payment, merchant wallet, verification, accepted receipt evidence, payment status, order status, or recovery. Vodafone Cash Payment Authority is production-certifiable.
