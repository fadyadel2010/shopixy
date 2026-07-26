# PASS M4-D Refund Visibility Authority Certification Findings

Date: 2026-06-22

Status: Production-certifiable for the inspected frontend runtime paths after Super Admin detail-cache visibility hardening.

## Actual Runtime Visibility Surfaces

- CommerceHub customer: refund history, status, payout summary, evidence dialog, order navigation, embedded order-item refund rows, and refund-request entry.
- POS vendor: CommerceHub finance dashboard `recentRefundImpacts` only. No vendor refund detail, review, or execution surface is implemented.
- Super Admin: refund monitoring list, status/vendor/date filtering, selected detail drawer, customer/order/item/payment/refund evidence, review, rejection, execution/retry, and return-adjacent controls.

## Findings

- Visibility authority: backend refund, customer, vendor, admin, payment, order, return, tenant, actor, SQL, and endpoint authorization own every grant. Frontend only validates, withholds, filters, selects, navigates, and presents backend evidence.
- Customer: list rows go through financial and visibility boundaries; evidence selection resolves only from accepted current rows; fetching/error suppresses cached rows. Customer A cannot see Customer B refunds without a backend grant.
- Vendor: refund-impact rows render only under the current backend-backed vendor/session/tenant finance scope. Vendor A cannot see Vendor B refund evidence through this runtime.
- Admin: filters and URL selection are request candidates only. A selected detail must match the current accepted monitoring row. Monitoring cannot widen visibility or turn into ownership.
- Payment/order/return consistency: payment, order, provider, and return data are evidence inside the current backend refund detail; none can mint or extend refund visibility. Refund visibility cannot exceed the corresponding current backend payment, order, or return visibility.
- Bootstrap/cache/failure/adversarial: initial load, focus, polling, reconnect, history, BFCache, route/search replay, selection replay, storage, malformed DTOs, and 401/403/404/409/422 failures either re-enter backend authority or withhold the refund surface. No inspected path restores visibility from cache or replay.

## Runtime Hardening Performed

- Super Admin now withholds the detail drawer, execution dialog, and return dialog whenever current monitoring authority or current detail authority is fetching, fails, or no longer contains the selected refund.
- Super Admin detail validation now rejects malformed customer, payout, and refund evidence media before those fields reach a visibility surface.

## Exact Runtime Files Modified

- `PointOfSale.Web/src/super-admin/refunds/RefundsPage.tsx`
- `PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.ts`

## Regression Freezes Added

- `PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.test.ts`
- `PointOfSale.Tests/Architecture/RefundVisibilityAuthorityCertificationTests.cs`

## Explicit Certification Answers

- Who owns refund visibility authority? The backend.
- Can frontend fabricate visibility? No.
- Can Customer A see Customer B refunds? No.
- Can Vendor A see Vendor B refund evidence? No.
- Can monitoring widen visibility? No.
- Can payment visibility create refund visibility? No.
- Can refund visibility exceed payment, order, or return visibility? No.
- Can stale visibility survive refresh, cache, or replay? No.
- Can tenant leakage occur? No inspected frontend path introduces it.
- Were runtime changes required? Yes.
- Is Refund Visibility Authority production-certifiable? Yes, for the inspected frontend runtime paths with backend authorization in force.
