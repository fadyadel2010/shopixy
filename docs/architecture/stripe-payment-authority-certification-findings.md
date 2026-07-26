# Stripe Payment Authority Certification (PASS SF-PAY4)

## Authority

PointOfSale.Storefront Stripe payment runtime is backend-owned and Presentation Only. Storefront owns Stripe presentation, redirect/pending navigation, Stripe SDK rendering/confirmation, loading, retry, refresh, and admitted DTO presentation only.

Backend owns PaymentIntent, Stripe PaymentIntent, Stripe session/handoff, client secret authority, amount, currency, customer identity, metadata, webhook signature validation, Stripe event identity, payment success, payment failure, payment status, order status, persistence, recovery, and audit.

## Findings

- Stripe begins as a Storefront payment-method candidate and becomes presentable only after backend checkout admission creates or replays the SQL order and SQL `PaymentIntent` for provider `Stripe`.
- Authority begins in backend checkout/payment services after tenant, customer/session, cart, order, payment-method, expected-pricing, checkout-token, currency, inventory reservation, and SQL row-lock admission.
- Backend `StripePaymentService` creates Stripe PaymentIntents from SQL order amount/currency only, sends backend metadata, encrypts the Stripe client secret, persists provider identity to SQL `PaymentIntents`, and returns only accepted presentation DTO fields.
- Storefront `requireStorefrontPaymentSessionAuthority` rejects malformed Stripe handoffs before Zustand state unless the DTO proves current cart binding, backend `paymentIntentId`, checkout token, future expiration, Stripe client secret, publishable key, selected-provider agreement, amount, currency, order identity, and no mixed provider artifacts.
- Stripe.js confirmation in `StripePaymentForm` is presentation/client SDK only. `resolveStripeClientOutcome` always returns pending, so frontend never treats Stripe `succeeded`, `processing`, or redirect completion as financial success.
- Webhook verification begins at `POST /api/webhooks/stripe`, where backend reads the raw payload, requires `Stripe-Signature`, resolves tenant from stored payment authority, verifies with `EventUtility.ConstructEvent`, logs Stripe event identity, and rejects invalid signatures.
- Payment success/failure/cancel begin only in backend Stripe webhook handling through `IPaymentLifecycleService` with Stripe provider amount/currency, event id, SQL payment intent, terminal-state collapse, and authorized payment-status mutation context.
- SQL persistence begins in SQL `Orders`, SQL `PaymentIntents`, SQL `PaymentWebhookLogs`, payment lifecycle status writes, outbox/event publication, and audit/log records. Redis idempotency is derivative only; Mongo projections are not Stripe authority.
- Recovery re-enters backend order/payment-intent/session authority. Replay regenerates time-limited presentation fields only from current SQL payment intent evidence and clears expired or terminal Stripe artifacts.
- React, React Query, Zustand, LocalStorage, SessionStorage, browser cache/BFCache, Redis, Mongo projections, Stripe redirects, stale DTO replay, duplicate checkout, duplicate redirect, duplicate webhook, recovery, restart, Redis flush, Mongo rebuild, projection rebuild, Orders, Coupons, Promotions, Smart Offers, Affiliate, Marketplace, Inventory, Checkout, Paymob, Vodafone Cash, Manual Payment, refunds, settlement, chargebacks, and direct browser tampering cannot fabricate Stripe success.

## Runtime Hardening Performed

- No runtime behavior change was required. Existing SF-PAY1 through SF-PAY3 hardening and inspected Stripe runtime already keep Stripe authority backend-owned.

## Exact Runtime Files Modified

- None.

## Regression Freeze

- `PointOfSale.Tests/Architecture/StripePaymentAuthorityCertificationTests.cs`

## Verification

- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter StripePaymentAuthorityCertificationTests` passed.
- Frontend build not required because runtime was not modified.

## Certification

Stripe Payment remains backend-owned. Frontend cannot fabricate succeeded payment, fabricate client secret authority, bypass webhook verification, make replay succeed twice, make duplicate redirects succeed twice, or make cache/recovery/projections become Stripe authority. Stripe Payment Authority is production-certifiable.
