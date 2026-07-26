# PASS M4-G Refund Scope Isolation Authority Certification Findings

Date: 2026-06-22

Status: Production-certifiable for the inspected frontend runtime after customer create-response order-item scope binding.

## Implemented scope artifacts

- Customer refund rows: `refundId`, `orderId`, `orderItemId`, `vendorId`, `orderNumber`, `quantity`, `currency`, `status`, evidence `mediaId`, and payout evidence.
- Customer create intent: `orderItemId`, `quantity`, reason, payout reference/details, evidence URLs, and an idempotency key. It carries no customer, vendor, tenant, order, payment, return, execution, or amount authority field.
- POS vendor financial impact: parent `vendorId` plus `refundId`, `impactType`, `amount`, `signedDebtAmount`, `currency`, correlation metadata, and timestamp.
- Super Admin list/detail: `refundId`, `orderId`, `customerId`, `vendorId`, `orderItemId`, `subOrderId`, `paymentId` when present, `returnId` when present, `executionId`, and `financialRefundId` when execution exists.

## Runtime findings

- Customer isolation: history, detail, and evidence are accepted only from backend list/order DTOs. Route IDs and selected evidence IDs only select from the current accepted result. Customer refund creation and evidence upload re-enter current order-item authority; creation responses now fail closed unless their `orderItemId` matches that current item.
- Vendor isolation: the only implemented POS vendor refund surface is read-only financial impact. Its parent DTO must bind to the active vendor tenant and malformed impact rows are withheld. There is no vendor refund-evidence, review, or execution route.
- Customer/vendor and vendor/admin: no frontend conversion exists. Customer requests contain item-level intent only; vendor finance is read-only; Super Admin review/execution requires current backend monitoring-row and matching detail authority.
- Order, payment, and return consistency: customer rows validate item/order financial shape; Super Admin detail validates the current list's refund/order/vendor/customer scope, embedded return linkage, payment identity/status, and financial refund execution evidence. Frontend withholds contradictions rather than reconciling them.
- Bootstrap, refresh, cache, dialogs, history, and route replay: customer pages clear presentation while fetching or failed. Vendor finance withholds query data while fetching and session-scopes cache keys. Super Admin makes search `refundId` a candidate only, clears absent selections, withholds detail/dialogs during list/detail refresh or failure, and re-enters list/detail authority at mutation time.
- Failures and malformed DTOs: 401/403/404/409/422 and malformed payloads produce no replacement scope; validators reject malformed GUIDs, enums, dates, media, payment/return/execution references, and conflicting detail/list scope.

## Adversarial conclusion

Storage, history, route, selection, mutation, actor, and tenant replay cannot establish refund scope in the inspected frontend. The frontend never manufactures a refund/customer/vendor/admin/tenant/order/payment/return/execution scope; backend endpoint authorization and current backend DTOs remain decisive.

## Regression freeze

`RefundScopeIsolationAuthorityCertificationTests` freezes the customer create-response binding, vendor current-tenant impact binding, and Super Admin current list/detail/refresh/mutation boundary.
