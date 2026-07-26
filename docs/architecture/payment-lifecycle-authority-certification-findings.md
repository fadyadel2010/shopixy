# Payment Lifecycle Authority Certification (PASS M3-D)

## Lifecycle Vocabulary

Actual implemented payment lifecycle vocabulary reviewed from code:

- `PaymentStatus`: `Pending`, `Authorized`, `Collected`, legacy `Paid`, legacy `PartiallyPaid`, `Failed`, `Refunded`, `PartiallyRefunded`, `AwaitingVerification`, `Rejected`, `Settled`, `CashOnDelivery`, `Expired`, `AmountMismatch`.
- `PaymentIntentStatus`: `Pending`, `Authorized`, `Paid`, `Failed`, `Cancelled`, `Expired`, `Skipped`, `AwaitingVerification`, `Rejected`, `AmountMismatch`, `Suspicious`.
- `CommerceHubPaymentRecordStatus`: `Pending`, `Paid`, `Failed`, `Ignored`, `RefundRequired`, `Refunded`, `RefundFailed`.
- `CommerceHubRefundStatus`: `Pending`, `Processing`, `Completed`, `Failed`, `ReconciliationRequired`.
- Vendor payment request status: `PendingVerification`, `Approved`, `Rejected`.
- Super Admin refund presentation statuses: `Requested`, `UnderReview`, `Approved`, `WaitingForReturn`, `AwaitingPayout`, `ReadyForExecution`, `Rejected`, `Cancelled`, `Processing`, `Paid`, `Executed`, `Completed`, `Failed`.

## Findings

- Lifecycle findings: frontend lifecycle presentation is derivative from current backend DTOs only. Storefront canonical status now validates canonical state, payment status, optional intent status, amount, and currency before pending, failed, manual-review, or success presentation.
- Customer findings: checkout, pending page, payment status, order details, recovery, and refund history cannot fabricate lifecycle. Pending pages poll backend status and success navigation requires exact current backend order identity and lifecycle evidence. Recovery refetches current backend `canRetryPayment` immediately before provider handoff.
- Vendor findings: vendor finance, earnings, payments, payment submission, proof viewing, and settlement presentation remain current backend vendor/session scoped. Vendor UI can submit backend-bound intent only; it cannot advance, repair, or preserve payment lifecycle.
- Admin findings: Super Admin payment monitoring remains validated visibility evidence. Super Admin refund monitoring/details now pass a dedicated lifecycle boundary, and selected refund/action state is re-derived from the current accepted list.
- Provider findings: Stripe client outcomes and Paymob redirects cannot create lifecycle; they lead to backend status/refetch. Provider callbacks remain backend-owned through payment lifecycle/webhook services.
- Order consistency findings: CommerceHub order lifecycle boundaries reject lifecycle/payment, refund, failed-payment fulfillment, and parent/child split contradictions before presentation. Super Admin refund detail validation rejects malformed payment status and completed refund presentation without execution evidence.
- Recovery findings: recovery links and tokens are lookup/access candidates only. Retry cannot resurrect terminal lifecycle because frontend refetches current backend retry authority and initiation validates exact order/provider/amount/currency.
- Bootstrap findings: refresh, focus, visibility, reconnect, history, and BFCache paths re-enter backend authority or withhold presentation; they do not restore lifecycle from browser state.
- Cache findings: React Query caches are derivative. Super Admin refund monitoring no longer uses previous-list placeholder data, and selected detail/action authority is removed when the current list no longer contains the refund.
- Failure findings: 401/403/404/409/422, malformed DTOs, failed refreshes, and polling errors do not create replacement lifecycle. Polling errors stay pending; malformed lifecycle evidence fails closed.
- Adversarial findings: localStorage, sessionStorage, React state, contexts, React Query, mutation responses, route state, history state, URL/search parameters, cross-tab events, provider redirects, callback replay, storage replay, actor replay, tenant replay, and mutation replay cannot fabricate, advance, regress, repair, reconcile, preserve, restore, replay, or override payment lifecycle on inspected paths.

## Runtime Hardening

- Storefront canonical payment status validation now enforces backend lifecycle vocabulary and rejects canonical-state/payment-status contradictions before pending-page display or confirmation navigation.
- Super Admin refund lifecycle authority now validates refund list/detail/action mutation DTOs, payment status evidence, payment-record status evidence, refund execution status, return status, money/currency/date evidence, duplicate monitoring rows, and selected refund membership.
- Super Admin refund detail and action presentation now requires the selected refund ID to be present in the current backend-backed list; stale selected rows are cleared and prior list placeholder replay is removed.

## Exact Runtime Files Modified

- `PointOfSale.Storefront/src/features/checkout/paymentFlow.ts`
- `PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.ts`
- `PointOfSale.Web/src/super-admin/refunds/refundsService.ts`
- `PointOfSale.Web/src/super-admin/refunds/RefundsPage.tsx`

## Regression Freezes Added

- `PointOfSale.Web/src/payment/paymentFlow.test.ts`
- `PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.test.ts`
- `PointOfSale.Tests/Architecture/PaymentLifecycleAuthorityCertificationTests.cs`

## Architecture Updates

- Added this M3-D findings file.
- Updated authority context, hardening context, known risks, project context, and Codex rules with the M3-D freeze.

## Certification Answers

- Who owns payment lifecycle authority? Backend payment, payment intent, payment attempt, provider, refund, capture, settlement, revocation, expiration, order, tenant, customer, vendor, and Super Admin authorization services.
- Can frontend fabricate lifecycle? No.
- Can customers advance lifecycle? No.
- Can vendors advance lifecycle? No.
- Can redirects create lifecycle? No.
- Can callbacks bypass lifecycle validation? No on inspected paths; backend callback/webhook lifecycle services remain decisive.
- Can payment lifecycle contradict order lifecycle? Inspected frontend DTO boundaries reject lifecycle/payment/order/refund contradictions before presentation.
- Can terminal lifecycle be resurrected? No; recovery and retry re-enter current backend authority.
- Can stale lifecycle survive refresh? No; stale refund selection is cleared, customer recovery refetches authority, and cache/history paths revalidate or withhold.
- Can cache restore lifecycle? No.
- Can replay restore lifecycle? No.
- Can tenant leakage occur? No inspected frontend lifecycle path introduces tenant leakage; backend endpoint authorization remains final.
- Were runtime changes required? Yes.
- Is Payment Lifecycle Authority production-certifiable? Yes, for the inspected frontend runtime paths, contingent on existing backend lifecycle and authorization boundaries remaining in force.

## Verification

- `npm test -- --run src/payment/paymentFlow.test.ts src/super-admin/refunds/refundLifecycleAuthority.test.ts src/super-admin/payments/paymentOwnershipAuthority.test.ts`
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter "FullyQualifiedName~PaymentLifecycleAuthorityCertificationTests" --no-restore`
- `npm run build` in `PointOfSale.Web` passed after rerunning outside the sandbox when the first sandboxed attempt was denied access to the Vite config path.
