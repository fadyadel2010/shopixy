# Fawry Payment Authority Certification (PASS SF-PAY7)

Date: 2026-07-03

## Scope

Reviewed only the PointOfSale.Storefront Fawry payment presentation runtime and backend APIs/services directly involved in Fawry checkout, payment session, reference code, PaymentIntent, webhook admission, signature validation, lifecycle transition, recovery, audit, projection, and SQL truth.

## Finding

PointOfSale.Storefront Fawry Payment Authority is production-certifiable after Fawry DTO admission hardening. Storefront owns only Fawry payment-method selection, reference-code presentation, expiration rendering, loading, retry, refresh, navigation, and admitted DTO rendering. Backend owns PaymentIntent, merchant/payment reference generation, Fawry reference number, provider session evidence, checkout token, amount, currency, webhook admission, signature validation, verification, payment success/failure/status, order status, audit, recovery, projection, and SQL truth.

## Runtime Hardening

- Backend checkout now issues a backend-generated checkout token for Fawry handoffs, matching the provider-session authority boundary required for authenticated and guest Fawry presentation.
- Storefront `requireStorefrontPaymentSessionAuthority` now rejects Fawry checkout responses missing backend `paymentIntentId`, backend `checkoutToken`, future `expiresAtUtc`, backend `referenceCode`, selected-provider agreement, or pending/processing payment status.
- Storefront treats Fawry reference evidence as a provider artifact, so mixed Stripe/Paymob/Fawry artifacts and terminal responses carrying reusable Fawry references fail closed before Zustand state.
- Storefront maps admitted backend `referenceCode` and `expiresAtUtc` into `orderResult` only after backend DTO admission, allowing `CheckoutSuccess` to render backend evidence without creating reference authority.

## Runtime Review

Fawry begins when Storefront submits the `FAWRY` payment-method candidate during checkout. Authority begins only after backend checkout admission validates current tenant/customer/session/cart/order/payment-method/pricing authority, creates or recovers SQL `Orders` and SQL `PaymentIntents`, invokes the backend Fawry adapter, generates a deterministic backend reference code, stores expiration/provider evidence in SQL, and emits an admitted no-store DTO. Verification begins only at the backend Fawry webhook boundary after tenant resolution from stored reference-code authority and tenant-specific HMAC signature validation. SQL persistence begins in SQL `Orders`, SQL `PaymentIntents`, SQL `PaymentWebhookLogs`, lifecycle writes, outbox/audit, and SQL-derived projection repair. Presentation begins only after accepted backend DTO emission and Storefront DTO admission.

Fawry webhook admission is backend-owned: `FawryWebhookController` uses the webhook request pipeline, tenant-specific webhook secret, fixed-time HMAC comparison, serializable transaction, SQL webhook log idempotency, stored PaymentIntent lookup, provider amount/currency forwarding, and `IPaymentLifecycleService` for authorized status transitions. Duplicate, in-flight, replayed, failed, malformed, unsigned, or unresolved events cannot mark paid by themselves.

## Certification Answers

- Frontend cannot fabricate accepted Fawry payment, merchant/reference evidence, checkout token, amount, currency, payment status, or order status.
- Replayed callbacks/webhooks cannot fabricate payment because admission requires backend signature validation, stored reference resolution, SQL webhook event idempotency, and lifecycle terminal-state collapse.
- Duplicate callbacks, duplicate webhooks, and duplicate redirects cannot succeed twice; only SQL/lifecycle authority can change state, and duplicates collapse to existing SQL truth.
- React, React Query, Zustand, LocalStorage, SessionStorage, browser cache/BFCache, Redis, and Mongo projections cannot become Fawry authority. They may present, invalidate, refetch, or rebuild from SQL only.
- Recovery cannot diverge from SQL because checkout/payment-intent recovery re-enters backend SQL PaymentIntent authority, clears stale expired reference evidence, regenerates tokens, and requires fresh DTO admission.

## Regression Freeze

`PointOfSale.Tests/Architecture/FawryPaymentAuthorityCertificationTests.cs`
