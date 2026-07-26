# PASS C4 — CommerceHub Payment Currency Authority

Date: 2026-06-23

## Certification result

CommerceHub Payment Currency Authority is production-certifiable. CommerceHub owns payment currency truth and every inspected payment currency-bearing runtime record is EGP only. POS, tenant, storefront, vendor, and gateway evidence are not currency authorities for CommerceHub payments.

## Runtime findings and hardening

- Checkout creates CommerceHub marketplace orders from `FinancialCurrencyGuard.PlatformBaseCurrencyCode` and rejects stores not operating in EGP. Payment initiation revalidates the loaded SQL order as EGP before a session can be created.
- Payment records are created from the SQL marketplace order only and are now explicitly normalized through `RequireCommerceHubPaymentCurrency`.
- Stripe intents and Paymob order/payment-key payloads now require EGP immediately before their amounts and currency fields are emitted. Payment amount conversion is fixed to EGP two-decimal minor units; generic zero-decimal handling is not available on CommerceHub payment paths.
- Paymob, Stripe intent, and Stripe refund callbacks reject non-EGP or missing currency. Callback currency is compared with the existing EGP payment record; it cannot overwrite it.
- Unresolved webhook/suspense/reconciliation paths reject non-EGP or missing currency rather than persisting generic ISO or `XXX` provisional currency. Existing invalid unresolved currency cannot be carried forward as authoritative currency.
- Refund gateway execution requires both the requested refund currency and linked payment currency to be EGP.
- `ApplicationDbContext.GuardCommerceHubCurrencySovereignty` blocks any added or modified CommerceHub entity with a `Currency` property unless it is EGP. This covers payment, order, sub-order, refund, execution, financial, unresolved-webhook, replay, repair, and projection-shaped persistence writes. PostgreSQL `NOT VALID` EGP checks additionally fence new/changed payment, unresolved-webhook, and refund rows while preserving historical forensic evidence.

## Cache, recovery, and presentation

CommerceHub frontend payment recovery state is presentation/retry intent only. It receives payment/order currency from current backend responses and has no client currency mutation or storage authority. Browser history, BFCache, React Query, local storage, session storage, stale gateway pages, and retry context cannot persist or restore non-EGP payment truth because recovery re-enters backend order/payment authority and persistence rejects foreign currency.

## Adversarial and failure outcomes

- Forged USD/EUR/tenant/storefront currency in a payment request has no DTO authority and cannot alter the SQL order or payment currency.
- A foreign or missing gateway callback currency fails closed with `COMMERCEHUB_PAYMENT_CURRENCY_INVALID`; a foreign currency cannot become a pending, captured, refunded, or suspense payment currency.
- Existing-record callback tampering also fails payment-record currency equality checks.
- Malformed, 401, 403, 404, 409, and 422 paths do not provide a currency mutation route. Gateway signature validation remains required before callback processing.

## Explicit answers

| Question | Answer |
| --- | --- |
| Who owns CommerceHub Payment Currency Truth? | CommerceHub backend SQL authority, fixed to EGP. |
| Can payment sessions use USD or EUR? | No. |
| Can tenant or storefront currency affect payment currency? | No. |
| Can gateway requests contain non-EGP currency? | No. |
| Can a gateway callback override CommerceHub currency? | No; it is rejected unless EGP and equal to the payment record. |
| Can payment records or mixed-currency records exist? | No; the persistence guard blocks them. |
| Can payment currency diverge from order/checkout currency? | No; all are EGP and payment creation validates the order. |
| Can projections, replay, repair, cache, or recovery restore non-EGP payment state? | No; persistence guards and backend re-entry fail closed. |
| Can forged currency become payment truth? | No. |
| Were runtime changes required? | Yes. |
| Is authority production-certifiable? | Yes. |

## Regression freeze

`CommerceHubPaymentAuthorityCertificationTests.PaymentCurrency_MustRemainEgpAcrossGatewayCallbacksRecoveryAndPersistence` freezes the EGP-only service, gateway, recovery, and persistence requirements.

## Exact runtime files modified

- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubRefundGatewayService.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
- `PointOfSale.Infrastructure/Migrations/20260623130000_CommerceHubPaymentCurrencyAuthorityHardening.cs`
- `PointOfSale.Tests/Architecture/CommerceHubPaymentAuthorityCertificationTests.cs`
