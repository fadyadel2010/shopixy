# PASS M5-E Return Action Authority Certification Findings

Date: 2026-06-22

Status: Return Action Authority is production-certifiable for the inspected frontend runtime after override-action grant hardening.

## Action surfaces

Actual implemented return action surfaces:

- CommerceHub customer refund request submission and evidence upload, which are refund intents only and re-enter current backend order-item refundability.
- CommerceHub customer refund/order return-adjacent presentation, with no standalone customer return action mutation.
- POS vendor finance refund-impact presentation, read-only and not a return action surface.
- POS Admin backend return endpoints exist for schedule pickup, mark picked up, mark in transit, mark delivered, and confirm receipt, but no inspected frontend runtime surface invokes them.
- Super Admin refund detail return actions: override return status and mark return failed.
- POS sales return and purchase return create screens, separate POS return workflows whose controllers derive tenant, original invoice, line, stock, and financial authority server-side.

No implemented frontend `ConfirmPickup`, `ConfirmDelivery`, `ConfirmVendorReceipt`, `RetryReturn`, `ExecuteReturn`, `ReviewReturn`, return transfer, return merge, return restore, or return replay action surface was found.

## Action findings

Backend owns Return Action Authority, Pickup Actions, Delivery Actions, Vendor Confirmation Actions, Failure Actions, Return Override Actions, Return Mutation Authority, tenant scope, actor scope, SQL lineage, locks, idempotency, and endpoint authorization. Frontend owns display, buttons, dialogs, selection, validation, withholding, refetching, and backend-bound intent only.

Runtime hardening added backend-issued `canOverrideStatus` plus `allowedOverrideStatuses` to return DTOs. The frontend now rejects malformed override grants, hides override presentation without current backend grant, renders target statuses only from current backend evidence, and revalidates the selected override target against freshly refetched backend detail immediately before mutation.

## Customer findings

Customer refund submission and evidence upload cannot create CommerceHub return actions. Upload and create paths re-enter current backend order-item authority; stale modals, stale payout selection, media evidence, route state, and cache cannot restore revoked refund/return action authority. Customer has no inspected standalone return action execution path.

## Vendor findings

Vendor refund impacts are current-session-scoped read-only financial evidence and expose no `returnId` or mutation action. POS Admin return endpoints remain backend-authorized and vendor scoped; no inspected frontend runtime grants vendor return actions from cache, finance rows, selected rows, or payment/refund state.

## Admin findings

Super Admin monitoring is visibility only. Selected refund IDs are re-derived from the current validated list. Return dialogs require accepted current detail and close during refresh, failure, or authority loss. Mark failed requires current `canMarkFailed`. Override requires current `canOverrideStatus` and the selected target inside current `allowedOverrideStatuses`.

## Lifecycle consistency findings

Lifecycle cannot create actions. Return statuses and timestamps are presentation evidence until current backend action booleans and allowed override targets grant a backend-bound intent. Actions cannot bypass lifecycle because service execution uses the same policy as DTO grants.

## Refund consistency findings

Refund actions cannot create return actions. Return actions cannot create refund actions. Refund approval may create backend return workflow server-side, but frontend refund status, execution status, and financial preview do not grant return mutations.

## Order consistency findings

Order IDs, order items, sub-orders, and POS invoice references are lineage evidence only. Order actions cannot create CommerceHub return actions, and return actions cannot create order actions in the frontend.

## Payment consistency findings

Payment status, provider callbacks, gateway references, execution IDs, and financial refund IDs cannot create return actions. Payment action authority remains separate and backend-owned.

## Bootstrap findings

Initial load, refresh, reconnect, focus, visibility, history, route changes, and BFCache cannot preserve return action authority. Current list/detail queries validate or withhold, and stale dialogs close when accepted current authority disappears.

## Cache findings

React Query, component state, dialogs, selected rows, route state, history state, localStorage, and sessionStorage are action candidates only. Cache cannot restore return actions after backend authority loss because presentation and submission re-enter current list/detail authority.

## Failure findings

401, 403, 404, 409, 422, malformed DTOs, unsupported action booleans, duplicate/unknown override targets, failed refreshes, and failed mutations fail closed. Failures cannot create replacement return actions.

## Adversarial findings

Action replay, storage replay, history replay, route replay, selection replay, mutation replay, actor replay, tenant replay, refund replay, order replay, payment replay, provider replay, and malformed DTO injection cannot fabricate, preserve, restore, expand, transfer, or execute return actions without current backend authority. No inspected frontend path introduces tenant leakage.

## Runtime hardening performed

- Added backend return override action authority fields: `CanOverrideStatus` and `AllowedOverrideStatuses`.
- Added shared backend policy for allowed override targets and made `OverrideStatusAsync` enforce it.
- Added frontend validation for override action grants and allowed target lists.
- Bound Super Admin override presentation and submit-time intent to current backend `canOverrideStatus` and `allowedOverrideStatuses`.

## Exact runtime files modified

- `PointOfSale.API/DTOs/CommerceHub/CommerceHubReturnDtos.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubReturnPolicy.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubReturnMapper.cs`
- `PointOfSale.API/Services/CommerceHub/ReturnManagementService.cs`
- `PointOfSale.Web/src/super-admin/refunds/refundsService.ts`
- `PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.ts`
- `PointOfSale.Web/src/super-admin/refunds/RefundsPage.tsx`

## Regression freezes added

- `PointOfSale.Web/src/super-admin/refunds/refundLifecycleAuthority.test.ts`
- `PointOfSale.Tests/Architecture/ReturnLifecycleAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/ReturnActionAuthorityCertificationTests.cs`

## Architecture updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
- `docs/architecture/return-action-authority-certification-findings.md`

## Explicit certification answers

- Who owns return action authority? Backend return/refund/order/payment/customer/vendor/admin/tenant/actor SQL, workflow, policy, lock, idempotency, and endpoint authorization authorities.
- Can frontend fabricate actions? No.
- Can customer perform unauthorized return actions? No.
- Can vendor perform unauthorized return actions? No inspected frontend path grants vendor return actions; backend endpoints remain decisive.
- Can monitoring become action authority? No.
- Can selected rows preserve actions? No.
- Can lifecycle create actions? No.
- Can actions bypass lifecycle? No.
- Can refund actions create return actions? No frontend path can do so.
- Can payment actions create return actions? No.
- Can stale actions survive refresh? No.
- Can cache restore actions? No.
- Can replay restore actions? No.
- Can tenant leakage occur? No inspected frontend path bypasses tenant/actor authority.
- Were runtime changes required? Yes, to add explicit backend override-action grants and frontend target binding.
- Is Return Action Authority production-certifiable? Yes, for the inspected frontend runtime.
