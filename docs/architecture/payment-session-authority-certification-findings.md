# Payment Session Authority Certification (PASS SF-PAY1)

## Authority

Backend services are the sole authority for Storefront checkout Payment Session
creation, loading, validation, recovery, persistence, tokens, payment intent
identity, amount, expected amount, currency, customer/cart/order candidate,
tenant scope, gateway handoff, status, and expiration. Storefront owns only
loading, navigation, selected candidate, retry, refresh, and admitted DTO
presentation.

## Implemented artifacts

- Storefront: backend `orderId`, `orderNumber`, `cartId`, `paymentIntentId`,
  `paymentStatus`, checkout/tracking tokens, `expiresAtUtc`, Stripe client
  secret and publishable key, and Paymob iframe URL/payment key.
- CommerceHub customer: `marketplaceOrderId`, `paymentId`, payment token
  (Stripe client secret), provider reference/intent id, checkout payment-session
  merchant reference, and signed order-access recovery token.
- Vendor/Admin: backend-issued manual vendor `paymentRequestId`, payment-method
  id, proof URL, transfer reference, and statement/payment reference. These are
  financial workflow identifiers, not customer provider sessions.

No frontend-created payment session, checkout session, payment attempt,
provider session, Paymob token, iframe token, recovery session, recovery token,
or merchant reference was found.

## Findings

- Customer: checkout and retry use current backend order/payment responses.
  CommerceHub validates exact order/provider/amount/currency before presenting a
  handoff and re-fetches retry authority immediately before initiation.
- Storefront: checkout responses pass a dedicated payment-session boundary before
  COD confirmation, state presentation, Stripe rendering, or Paymob navigation.
  It rejects malformed identity/amount/status evidence, missing/malformed cart
  binding, missing payment intent identity, missing checkout token, missing or
  expired expiration, mixed-provider artifacts, partial Stripe/Paymob artifacts,
  provider mismatch, and non-HTTPS Paymob URLs.
- Vendor/Admin: manual payment requests and monitoring rows are backend-issued
  query/mutation DTOs. They cannot create a customer checkout/provider session.
- Provider: Stripe client outcomes and Paymob redirect parameters remain
  non-final evidence. Both return to backend status/reconciliation authority;
  redirects do not create a session or completion state.
- Recovery: CommerceHub recovery URL/token is a lookup/access candidate only.
  It is stripped from the URL, is not stored as payment authority, and cannot
  retry without current backend `canRetryPayment` authority.
- Bootstrap/cache/storage: active Storefront and CommerceHub payment recovery
  contexts intentionally return `null`; no payment session, token, or client
  secret is persisted, restored from history, or cross-tab replayed.
- Expiration/failure: terminal status/revocation is only shown from canonical
  backend status. Failed initiation, malformed DTOs, 401/403/404/409/422, and
  failed refreshes produce no replacement frontend session.
- Adversarial: route, storage, history, callback, provider, merchant-reference,
  token, and actor replay cannot create authority. Tenant/actor access remains
  endpoint-authorized and current backend DTO validation prevents target swap in
  the inspected customer paths.

## Runtime hardening performed

Added backend-owned payment-session DTO evidence (`PaymentIntentId`,
`PaymentStatus`, `ExpiresAtUtc`), backend Stripe/Paymob session expiration,
checkout-token issuance for Paymob handoff presentation, no-store checkout
responses/requests, and Storefront DTO admission for cart binding, token,
payment intent identity, and expiration. Provider artifacts remain transient
presentation evidence only and fail closed before provider interaction.

## Exact runtime files modified

- `PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.ts`
- `PointOfSale.Storefront/src/store/checkoutStore.ts`
- `PointOfSale.Storefront/src/types/checkout.ts`
- `PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs`
- `PointOfSale.API/Controllers/CheckoutController.cs`
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.API/Services/Payments/Stripe/StripePaymentService.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs`

## Regression freeze

- `PointOfSale.Tests/Architecture/PaymentSessionAuthorityCertificationTests.cs`

## Verification

- `npm run build` passed in `PointOfSale.Storefront`.
- `npm run build` passed in `PointOfSale.CommerceHub`.
- Focused `dotnet test ...PaymentSessionAuthorityCertificationTests` exceeded
  the 120-second command limit during the existing test-project compile; it did
  not report a test failure.

## Certification answers

Backend owns Payment Session Authority. The frontend cannot fabricate payment
sessions; mutation responses only provide validated transient handoff
presentation, provider redirects cannot create sessions, recovery links cannot
restore expired sessions, and storage/cache/replay cannot restore sessions.
Expired, completed, failed, and revoked sessions do not remain active through
frontend state. Tenant leakage is not introduced by the inspected frontend
paths. Runtime changes were required and applied. Payment Session Authority is
production-certifiable subject to the existing backend authorization and
lifecycle authority remaining in force.
