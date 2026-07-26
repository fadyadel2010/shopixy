# Paymob Payment Authority Certification (PASS SF-PAY5)

Date: 2026-07-03

Scope: PointOfSale.Storefront Paymob payment runtime only. Reviewed backend APIs and services only where directly involved in Paymob payment-session creation, payment-key creation, iframe/redirect handoff, callback/webhook verification, payment lifecycle, recovery, and presentation.

## Certification

PointOfSale.Storefront Paymob Payment Authority is production-certifiable with no runtime behavior change required.

Backend checkout/payment services, SQL `Orders`, SQL `PaymentIntents`, backend-generated checkout tokens, tenant Paymob merchant configuration, Paymob authentication/order/payment-key creation, Paymob iframe URL generation, encrypted merchant secrets, Paymob callback HMAC validation, Paymob webhook HMAC validation, provider identity validation, SQL `PaymentWebhookLogs`, `IPaymentLifecycleService`, authorized payment-status transitions, terminal-state collapse, idempotent duplicate-webhook handling, checkout payment-intent recovery, and accepted backend DTO emission own PaymentIntent, Paymob Session, Payment Key, Checkout Token, Amount, Currency, Customer Identity, Merchant Configuration, Integration Id, Provider Metadata, Callback Verification, Webhook Verification, HMAC Validation, Provider Transaction Id, Payment Success, Payment Failure, Payment Status, Order Status, Persistence, Recovery, and Audit.

PointOfSale.Storefront owns only Paymob presentation, redirect/iframe handoff, pending navigation, loading, retry, refresh, and admitted DTO presentation. Paymob callback and redirect values are treated as routing evidence only; final success/failure presentation requires canonical backend payment status after backend webhook/lifecycle persistence.

## Runtime Evidence

- Storefront checkout responses pass `requireStorefrontPaymentSessionAuthority` before `checkoutStore` stores `orderResult` or `CheckoutPage` renders Paymob.
- Paymob DTO admission rejects missing or malformed cart binding, backend `paymentIntentId`, checkout token, future expiration, HTTPS iframe URL, payment key, amount, currency, selected-provider agreement, mixed provider artifacts, and terminal responses that still contain reusable provider artifacts.
- `PaymobPaymentForm` redirects only to the backend-issued iframe URL and converts Paymob return query parameters into pending navigation; it does not expose a success callback or mark payment paid.
- `CheckoutService` creates/replays Paymob handoff evidence through `_paymobPaymentService.CreatePaymentIntentAsync`, emits backend `PaymentIntentId`, `PaymentStatus`, `CheckoutToken`, `ExpiresAtUtc`, `PaymobIframeUrl`, and `PaymobPaymentKey`, and clears expired/replayed Paymob artifacts until backend recovery regenerates them.
- `PaymobPaymentService` locks the SQL order, validates tenant/customer/order/payment-method/currency authority, loads tenant Paymob configuration, creates the Paymob order and payment key server-side, persists SQL `PaymentIntent` metadata/expiry, and collapses duplicate active intents through idempotency and SQL uniqueness.
- `PaymobWebhooksController` uses the webhook request pipeline, size limits, rate limiting, server-side tenant resolution, HMAC extraction, and non-200 failure semantics. Invalid HMAC returns unauthorized; unresolved failures return retryable failure.
- `PaymobPaymentService.ProcessWebhookAsync` verifies canonical Paymob HMAC, validates provider/order/integration evidence, records SQL webhook logs, handles duplicate and in-flight events, and invokes `IPaymentLifecycleService` with Paymob source, provider amount, provider currency, and transaction evidence.
- `PaymobWebhooksController.HandleCallback` calls backend callback validation, rejects missing/mismatched stored routing context, and redirects only to `/checkout/pending`; payment success remains webhook/lifecycle-owned.
- `PaymentStatusSaveChangesInterceptor` and `IPaymentLifecycleService` keep payment and order status transitions backend-authorized. Duplicate callbacks, duplicate webhooks, duplicate redirects, stale provider evidence, replay, Redis loss, Mongo rebuild, projection rebuild, React, React Query, Zustand, LocalStorage, SessionStorage, and browser cache cannot fabricate Paymob success.

## Authority Boundaries

- Paymob begins when Storefront submits a `PAYMOB` payment-method candidate to backend checkout, then backend creates or recovers the SQL order/payment intent and Paymob handoff.
- Authority begins at backend checkout/payment admission after tenant, customer/session, cart, order, payment method, expected pricing, currency, inventory reservation, row locks, SQL payment intent creation/recovery, tenant merchant configuration, and backend Paymob payment-key creation.
- Callback verification begins in backend `PaymobWebhooksController.HandleCallback` and `PaymobPaymentService.ValidateCallbackAsync` after HMAC validation and stored payment-intent routing metadata resolution.
- Webhook verification begins in backend `PaymobWebhooksController.HandleWebhook` and `PaymobPaymentService.ProcessWebhookAsync` after raw payload capture, HMAC extraction, canonical HMAC validation, provider identity validation, SQL webhook logging, and idempotency checks.
- SQL persistence begins in SQL `Orders`, SQL `PaymentIntents`, SQL `PaymentWebhookLogs`, payment lifecycle writes, outbox/events, and audit/logging.
- Presentation begins only after accepted backend DTO emission and Storefront payment-session DTO admission.

## Residual Operational Proof

Retain deployed browser/API/database/provider/cache chaos coverage for malformed Paymob DTOs, forged payment keys, stale checkout tokens, expired Paymob sessions, cross-cart/cross-order/cross-tenant replay, invalid integration IDs, mixed provider artifacts, duplicate checkout, multiple tabs/devices, duplicate callback, duplicate webhook, duplicate redirect, webhook replay, provider timeout-after-commit, webhook retry after failure, Redis flush, Mongo rebuild, projection lag, BFCache, and direct React/Zustand/storage tampering.
