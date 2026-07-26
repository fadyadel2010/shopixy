# CommerceHub Refund & Return Currency Authority Certification (C5)

Date: 2026-06-23

## Certification result

CommerceHub owns refund and return currency truth. The only valid currency is
`EGP`. The refund request has no client currency field. Its amount is derived
from the SQL-backed CommerceHub order item, whose order/sub-order currency is
re-proven as EGP before creation. Return currency is derived from the same
refund/order lineage and is re-proven before every customer projection.

## Runtime authority boundaries

- Customer refund request: `CustomerRefundService` rejects an order/sub-order
  currency that is missing, non-ISO, non-EGP, or mismatched before creating a
  request or taking its financial-state lock.
- Manual execution and reconciliation recovery: `RefundExecutionService`
  re-proves order, sub-order, tracked refund, and resolved payment currency
  before any financial decision, state transition, or ledger effect.
- Gateway execution/recovery: `CommerceHubRefundService` re-proves the same
  lineage immediately before gateway invocation; the gateway independently
  rejects anything other than EGP payment/refund evidence.
- Refund records and financial ledger: existing refund database constraint and
  ledger validation reject non-EGP currency and order/sub-order mismatches.
  C5 adds the equivalent `CommerceHubRefundExecutions` constraint.
- Customer, return, and Super Admin projections: all refund/return currency
  presentation paths now fail closed by re-proving EGP rather than forwarding
  `Order.Currency` as formatting evidence.

## Recovery, cache, and adversarial behavior

No CommerceHub refund or return local-storage/session-storage persistence path
was found. React Query/browser-history responses are presentation candidates:
the service boundaries reject malformed, missing, forged, USD, EUR, or stale
currency evidence before it can be returned for cache admission. Recovery
re-reads current SQL refund/order/payment lineage and re-validates currency;
replay/repair cannot turn an invalid stored/projection currency into authority.

Gateway callbacks do not set refund truth. Provider currency is validated by
the payment boundary, and gateway refund execution requires EGP payment plus
EGP refund currency. A forged request currency is ignored because customer
refund DTOs expose no currency field; forged response/projection evidence is
withheld by the re-proven EGP projection boundary.

## Explicit answers

| Question | Answer |
| --- | --- |
| Who owns refund/return currency truth? | CommerceHub backend, re-composed from current SQL CommerceHub order/refund/payment lineage. |
| Can refunds or returns use USD/EUR? | No. |
| Can refund currency diverge from payment/order? | No; execution requires exact EGP consistency. |
| Can return currency diverge from refund/order? | No; it is refund/order-derived and EGP-re-proven. |
| Can financial reversals use non-EGP or mixed currency? | No; gateway, financial decision, safety guard, ledger, and persistence reject it. |
| Can projections or cache restore non-EGP state? | No; projections fail closed and no durable refund/return browser cache authority exists. |
| Can forged currency become refund/return truth? | No. |

## C5 hardening

- Added `CommerceHubRefundReturnCurrencyAuthority` for EGP and exact
  order/sub-order/refund/payment consistency checks.
- Added runtime checks at customer creation, manual execution/recovery,
  gateway execution, customer/refund/return/Super Admin projections.
- Added the EGP database constraint migration for refund executions.
- Added `CommerceHubRefundReturnCurrencyAuthorityCertificationTests` as a
  regression freeze.
