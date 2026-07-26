# Payment Identity Authority Certification (PASS M3-A)

## Authority

Backend payment, payment-attempt/session, provider, customer, vendor, tenant,
refund, settlement, and recovery services own identity truth. Frontends may use
a route value only as a lookup candidate and may present an identity only after
the current backend response proves the exact target.

## Implemented frontend identities

- Storefront: `orderId`, `orderNumber`, payment-method/status fields, and the
  backend-issued checkout/tracking token headers.
- CommerceHub: `marketplaceOrderId`, `orderId`, `orderNumber`, `subOrderId`,
  `paymentReference`, `paymentSession.merchantReference`, provider reference,
  `refundId`, payout transaction reference, and statement `transactionId`.
- Vendor/Super Admin: backend-issued `paymentRequestId`, payment-method `id`,
  statement `transactionId`, reference/correlation values, and payout IDs.

No frontend-created `PaymentId`, `PaymentAttemptId`, provider transaction ID,
Paymob transaction ID, or gateway transaction ID was found.

## Runtime hardening

- Storefront pending-payment status now rejects empty, malformed, or
  mismatched backend identity before presentation or confirmation navigation.
- Pending checkout URLs carry only the lookup candidate; order number, amount,
  currency, and method are not promoted from URL state.
- Storefront checkout identity, checkout tokens, tracking tokens, and Paymob
  return order IDs are no longer retained in browser storage.
- CommerceHub active payment-recovery identity is no longer retained in
  `sessionStorage`; recovery/detail routes re-enter their existing strict
  backend order-identity adapters.
- Paymob redirect fields are evidence only and always lead back to backend
  status authority; they cannot declare success or identity.

## Findings

- Customer: route/recovery candidates are validated by current backend detail
  or payment-status responses. Cached/replayed storage cannot restore an order
  or payment identity.
- Vendor: payment request, proof, earnings, finance, settlement, and statement
  identities are backend list/query DTO values; vendor actions send only a
  backend-issued target ID and remain endpoint-authorized.
- Admin: filtering/search and selected rows are presentation state. Approval,
  rejection, and proof access call backend endpoints with a backend-issued
  `paymentRequestId`; search does not create identity authority.
- Provider: Stripe client status is intentionally non-final; Paymob redirect
  values do not establish identity or completion. Backend payment status/webhook
  authority decides presentation.
- Split: CommerceHub customer adapters reject malformed/duplicate sub-order
  identity evidence and require backend-issued split composition.
- Bootstrap/cache/failure: active storage restoration was removed. Existing
  exact route-target validation rejects malformed/mismatched detail/recovery
  DTOs; 401/403/404 and request failure cannot produce a replacement identity.

## Certification answers

Backend owns payment identity authority. The frontend cannot fabricate payment
identity, route parameters cannot create it, provider redirects cannot create
it, and cache/storage cannot restore it. Customer and vendor foreign identity
access remains backend-authorized; split identities are backend-derived.
Tenant leakage is not introduced by frontend identity state. Runtime changes
were required and applied. The inspected frontend payment identity paths are
production-certifiable with current backend authorization as the final access
control boundary.

## Runtime files modified

- `PointOfSale.Storefront/src/features/checkout/activeCheckoutOrderContext.ts`
- `PointOfSale.Storefront/src/features/checkout/paymentFlow.ts`
- `PointOfSale.Storefront/src/services/api/paymentStatusApi.ts`
- `PointOfSale.Storefront/src/pages/CheckoutPage.tsx`
- `PointOfSale.Storefront/src/pages/CheckoutPendingPage.tsx`
- `PointOfSale.Storefront/src/components/checkout/PaymobPaymentForm.tsx`
- `PointOfSale.CommerceHub/src/features/checkout/activePaymentRecoveryContext.ts`

## Verification

- `npm run build` in `PointOfSale.Storefront`
- `npm run build` in `PointOfSale.CommerceHub`
