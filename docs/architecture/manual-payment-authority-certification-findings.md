# Manual Payment Authority Certification (PASS SF-PAY3)

## Authority

PointOfSale.Storefront Manual Payment runtime is backend-owned and Presentation Only. Storefront owns payment-method selection, wallet/reference candidate entry, receipt/proof upload intent, loading, retry, refresh, navigation, and admitted DTO presentation only.

Backend owns Payment Identity, PaymentIntent, Manual Payment Record, Reference Number, Receipt Evidence, Payment Status, Approval, Rejection, Verification, Amount, Currency, Order Identity, Customer Identity, Tenant Scope, Persistence, Recovery, and Audit.

## Findings

- Storefront `MANUALWALLET` begins as a selected payment-method candidate in `PaymentStep`, manual wallet candidate fields in `ManualWalletFields`, and candidate state in `checkoutStore`.
- Evidence upload is authenticated by backend manual-wallet upload tokens bound to tenant, checkout session, and recovery nonce; uploaded files are validated and stored as tenant-scoped private storage keys.
- Checkout submission sends wallet type, sender phone/reference candidate, and proof key candidate only. Backend revalidates current cart/order/customer/tenant authority, current payment-method authority, expected pricing, and proof-session binding before creating a SQL `PaymentIntent`.
- Manual payment authority begins only after backend admission creates the SQL `PaymentIntent` with backend amount, currency, order identity, tenant scope, backend reference, normalized proof key, and `AwaitingVerification` status.
- Storefront DTO admission now requires ManualWallet responses to prove backend `paymentIntentId`, `isManualWallet`, pending/awaiting-verification status, merchant wallet presentation evidence, wallet type, and instructions before presentation.
- Admin approval/rejection remains outside Storefront and is authorized through backend admin endpoints, state-machine validation, and `IPaymentLifecycleService` wallet approval/rejection sources.
- React, React Query, Zustand, LocalStorage, SessionStorage, browser cache/BFCache, Redis, Mongo projections, stale DTO replay, duplicate upload, duplicate checkout, recovery, restart, Redis flush, Mongo rebuild, and projection rebuild cannot fabricate accepted payment or approval.

## Runtime Hardening Performed

- Hardened `PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.ts` so `MANUALWALLET` checkout responses fail closed unless they carry backend-owned pending Manual Payment evidence, including `paymentIntentId`.

## Exact Runtime Files Modified

- `PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.ts`

## Regression Freeze

- `PointOfSale.Tests/Architecture/ManualPaymentAuthorityCertificationTests.cs`

## Verification

- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter ManualPaymentAuthorityCertificationTests` passed.
- `npm run build` passed in `PointOfSale.Storefront` with existing Vite chunk-size/dynamic-import warnings.

## Certification

Manual Payment remains backend-owned. Frontend cannot fabricate accepted payment, approve payment, mark payment paid, replace approved evidence, replay approval, or create accepted payment through duplicate upload. Manual Payment Authority is production-certifiable.
