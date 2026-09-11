# PASS M4-C Refund Lifecycle Authority Certification Findings

Date: 2026-06-21

Status: Updated 2026-09-03. Super Admin refund actions retain current-authority revalidation; return logistics mutations belong to the owning POS vendor.

## Lifecycle Vocabulary

Actual implemented refund lifecycle vocabulary reviewed from runtime code:

- Refund request/status presentation: `Requested`, `UnderReview`, `Approved`, `WaitingForReturn`, `AwaitingPayout`, `ReadyForExecution`, `Rejected`, `Cancelled`, `Processing`, `Paid`, `Executed`, `Completed`, `Failed`.
- Refund execution status: `Pending`, `Processing`, `Completed`, `Failed`.
- Return lifecycle status: `Pending`, `PickupScheduled`, `PickedUp`, `InTransit`, `DeliveredToVendor`, `Confirmed`, `Failed`.
- Return execution triggers: `OnPickup`, `OnDelivery`, `OnVendorConfirmation`.
- Payment evidence used for refund consistency: `Pending`, `Authorized`, `Collected`, `Paid`, `PartiallyPaid`, `Failed`, `Refunded`, `PartiallyRefunded`, `AwaitingVerification`, `Rejected`, `Settled`, `CashOnDelivery`, `Expired`, `AmountMismatch`.
- Payment record evidence used for refund consistency: `Pending`, `Paid`, `Failed`, `Ignored`, `RefundRequired`, `Refunded`, `RefundFailed`.

## Lifecycle Findings

Backend owns Refund Lifecycle Authority, Refund Request Lifecycle, Refund Review Lifecycle, Refund Execution Lifecycle, Refund Completion Lifecycle, Refund Failure Lifecycle, Return-Refund Coordination Lifecycle, payment/refund consistency, order/refund consistency, tenant scope, actor scope, SQL lineage, locks, idempotency, and endpoint authorization. Frontend owns display, formatting, badges, status labels, filtering, selection, navigation, validation, withholding, refetching, and backend-bound intent only.

## Customer Findings

Customer refund history and embedded order-detail refund rows accept only backend refund status values that pass financial and visibility boundaries. Customer presentation maps `AwaitingPayout` and `ReadyForExecution` to awaiting-payout copy and maps `Paid`, `Executed`, and `Completed` to paid copy; this is label formatting only. Customer refund submission can create backend-bound request intent only after current order-item and payout authority are re-entered; it cannot advance, restore, fabricate, or repair lifecycle. Customer stale rows are withheld during fetching or failure, and evidence dialogs select only current accepted rows.

## Vendor Findings

Inspected POS vendor refund-impact evidence is financial presentation under current backend vendor/session/tenant scope. Vendor runtime has no refund lifecycle mutation surface and cannot advance, repair, override, or restore refund lifecycle. Vendor financial rows are evidence only and cannot patch refund status.

## Admin Findings

Super Admin refund monitoring validates lifecycle vocabulary and duplicate rows before presentation. URL-selected refund IDs are candidates only and are re-derived from the current accepted monitoring list. Details validate refund status, payment status, payment-record status, execution status, return status, dates, money, execution evidence, failed-refund evidence, and current selected-row scope. Approve, reject, retry, and execute submissions re-fetch current monitoring and detail authority; return state remains read-only on this surface.

## Payment Consistency Findings

Payment lifecycle cannot create refund lifecycle. Payment status and payment-record status are accepted only as backend evidence inside refund detail validation. Completed refund presentation requires execution evidence, failed refund presentation requires failure evidence, and malformed payment/refund contradictions fail closed before presentation.

## Order Consistency Findings

Order lifecycle cannot create refund lifecycle. Customer refund entry remains bound to current backend order-item refundability, and Super Admin detail validation binds refund detail to current monitoring row order/customer/vendor/amount/currency evidence. Cancelled, failed, refunded, or otherwise contradictory order/payment/refund evidence cannot be reconciled by frontend into lifecycle truth.

## Return Consistency Findings

Return lifecycle cannot become refund lifecycle. Embedded return DTOs must match current refund, item, customer, and vendor identities and pass return status/trigger/timestamp/action validation. Return milestone labels are presentation only. Owning-vendor return submissions re-enter the current vendor order/return authority and use the current backend status before mutation.

## Bootstrap Findings

Initial load, refresh, reconnect, focus, visibility, history, and BFCache paths re-enter backend queries or withhold presentation. Customer refund rows are hidden during active fetch or error. Super Admin refund lists poll/refetch current authority, stale selections close when absent from current list, and selected detail reads are disabled without a current selected list row.

## Cache Findings

React Query, route/search params, selected IDs, component state, dialogs, modal form state, localStorage, sessionStorage, and history state are derivative. Caches can display only accepted backend DTOs and cannot restore lifecycle after current authority loss. Mutation success responses are validated and rebound to current selected scope before detail cache presentation.

## Failure Findings

401, 403, 404, 409, 422, malformed DTOs, failed refreshes, failed mutations, and timeout-shaped ambiguity cannot create replacement lifecycle. Malformed refund, return, payment, execution, date, money, or failure evidence fails closed. Failed current refresh prevents mutation submission instead of preserving stale lifecycle.

## Adversarial Findings

Lifecycle replay, storage replay, history replay, route replay, selection replay, mutation replay, actor replay, tenant replay, provider callback replay, payment replay, order replay, return replay, malformed DTO injection, and stale modal replay cannot fabricate, advance, regress, repair, restore, replay, merge, override, or replace refund lifecycle without current backend authority. No inspected frontend path introduces tenant leakage.

## Runtime Hardening Performed

- Retained Super Admin mutation-time revalidation for refund actions: approve, reject, retry, and execute refetch current backend monitoring and detail authority before submitting intent.
- Removed Super Admin return mutations; vendor return commands validate current vendor identity, action grant, and expected status.
- Added fail-closed localized message for missing current backend refund action authority.

## Exact Runtime Files Modified

- `PointOfSale.Web/src/super-admin/refunds/RefundsPage.tsx`
- `PointOfSale.Web/src/i18n/locales/en.json`
- `PointOfSale.Web/src/i18n/locales/ar.json`

## Regression Freezes Added

- `PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.test.ts`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
- `docs/architecture/refund-lifecycle-authority-certification-findings.md`

## Explicit Certification Answers

- Who owns refund lifecycle authority? Backend refund, return, payment, order, tenant, actor, SQL, idempotency, lock, reconciliation, and endpoint authorization authorities.
- Can frontend fabricate lifecycle? No.
- Can customer advance lifecycle? No.
- Can vendor advance lifecycle? No.
- Can monitoring become lifecycle authority? No; monitoring is visibility and selected-row candidate state only.
- Can payment lifecycle create refund lifecycle? No.
- Can refund lifecycle contradict payment lifecycle? No inspected frontend path can establish that contradiction as authority; malformed contradictions fail closed before presentation.
- Can refund lifecycle contradict order lifecycle? No inspected frontend path can establish that contradiction as authority; order/refund consistency remains backend-owned and frontend-boundary validated.
- Can refund lifecycle bypass return lifecycle? No; return evidence is validated and return mutation submissions re-enter current refund detail authority.
- Can stale lifecycle survive refresh? No inspected path preserves stale lifecycle as current authority.
- Can cache restore lifecycle? No.
- Can replay restore lifecycle? No.
- Can tenant leakage occur? No inspected frontend path introduces tenant leakage; backend authorization remains final.
- Were runtime changes required? Yes.
- Is Refund Lifecycle Authority production-certifiable? Yes, for inspected frontend runtime paths.
