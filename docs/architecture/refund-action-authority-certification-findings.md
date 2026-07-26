# PASS M4-E Refund Action Authority Certification Findings

Date: 2026-06-22

Status: Production-certifiable for the inspected frontend refund runtime paths after customer evidence-upload revalidation hardening.

## Actual runtime action surfaces

- Customer: submit refund request, upload refund evidence, save/update payout method, select a saved payout method for a refund.
- Vendor: none. The implemented POS surface is read-only refund-impact financial evidence.
- Super Admin: approve, reject, retry, execute payout, override return status, and mark return failed.

## Findings

- Action authority: backend owns every refund action grant and endpoint decision. Frontend only presents controls, validates inputs, withholds stale controls, and submits intent.
- Customer: refund creation and evidence upload re-enter current backend order-item authority; saved payout IDs are rechecked against current backend payout evidence. Payout saving itself cannot grant a refund action.
- Vendor: current vendor/session/tenant-scoped `recentRefundImpacts` is financial evidence only; no refund review, approval, rejection, execution, or return mutation is implemented.
- Admin: selected IDs, filters, dialogs, and monitoring are candidates only. Every mutation re-fetches current list selection and matching detail, then requires current backend action evidence where supplied (`canApprove`, `canReject`, `canRetry`, `canExecute`, `canMarkFailed`). Return override is an intent only; its current backend endpoint remains the final grant.
- Lifecycle/payment/order/return: their statuses are validated backend evidence, never frontend action grants. No frontend inference converts a status, provider callback, payment evidence, order state, or return state into an action.
- Bootstrap/cache/failure/adversarial: refresh, polling, focus, reconnect, history/BFCache, route/search state, React Query, dialogs, selected rows, local/session storage, malformed DTOs, and 401/403/404/409/422 failures either re-enter backend authority or fail closed. They cannot restore, replay, transfer, or fabricate action authority. No inspected path creates tenant leakage; backend tenant and actor authorization remain decisive.

## Runtime hardening performed

- `RefundRequestModal` now resolves current backend order-item authority and requires current `canRefund` before uploading customer refund evidence.

## Exact runtime files modified

- `PointOfSale.CommerceHub/src/features/customer/components/RefundRequestModal.tsx`

## Regression freezes added

- `PointOfSale.Tests/Architecture/RefundActionAuthorityCertificationTests.cs`

## Architecture updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
- `docs/architecture/refund-action-authority-certification-findings.md`

## Explicit certification answers

- Who owns refund action authority? Backend.
- Can frontend fabricate actions? No.
- Can customer perform unauthorized refund actions? No.
- Can vendor perform refund actions? No implemented vendor refund mutation surface exists.
- Can monitoring become action authority? No.
- Can lifecycle, payment status, order state, or return state create actions? No.
- Can stale actions survive refresh? No.
- Can cache or replay restore actions? No.
- Can tenant leakage occur? No inspected frontend runtime path introduces it.
- Were runtime changes required? Yes: customer evidence upload now re-enters current backend authority.
- Is Refund Action Authority production-certifiable? Yes, for the inspected frontend runtime paths with backend authorization in force.
