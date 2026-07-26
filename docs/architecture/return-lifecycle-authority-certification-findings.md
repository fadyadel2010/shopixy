# PASS M5-C Return Lifecycle Authority Certification Findings

Date: 2026-06-22

Status: Return Lifecycle Authority is production-certifiable for the inspected frontend runtime.

## Lifecycle vocabulary

Actual implemented CommerceHub return lifecycle vocabulary:

- `Pending`
- `PickupScheduled`
- `PickedUp`
- `InTransit`
- `DeliveredToVendor`
- `Confirmed`
- `Failed`

Actual implemented return execution triggers:

- `OnPickup`
- `OnDelivery`
- `OnVendorConfirmation`

`Cancelled` and `Rejected` were not implemented CommerceHub return lifecycle statuses in the inspected runtime.

## Lifecycle findings

Backend owns Return Lifecycle Authority, Return State Authority, Return Transition Authority, Pickup Authority, Delivery Authority, Vendor Confirmation Authority, Failure Authority, SQL lineage, locks, idempotency, tenant scope, actor scope, and endpoint authorization. Frontend owns display, badges, labels, formatting, selection, validation, withholding, refetching, and backend-bound intent only.

## Customer findings

CommerceHub customer runtime exposes refund request/history/order-linked evidence, not standalone return lifecycle creation or transition controls. Customer refund rows are accepted only through financial and visibility authority boundaries and are withheld during current fetch or error. Customer cannot advance, restore, repair, or resurrect return lifecycle.

## Vendor findings

The inspected vendor runtime presents current-session-scoped financial refund impacts only. Vendor refund-impact rows do not carry CommerceHub return lifecycle transitions and cannot repair, override, advance, or restore return lifecycle. POS Admin vendor receipt confirmation is backend-authorized outside frontend lifecycle authority and remains endpoint/policy controlled.

## Admin findings

Super Admin monitoring is visibility only. Selected rows are re-derived from the current accepted refund list. Return details must match current refund, order item, customer, and vendor lineage and pass the exact lifecycle vocabulary, trigger, timestamp, action-boolean, and identity boundary. Return override and failure dialogs are shown only from accepted current detail and close during refresh, failure, or current authority loss.

## Refund consistency findings

Refund lifecycle cannot create return lifecycle, and return lifecycle cannot create refund lifecycle. Refund status, execution status, financial refund identity, and return status are validated as separate backend evidence. Contradictions such as refund completed with return pending, refund rejected with return confirmed, or refund executed with return failed cannot be reconciled by frontend into authority.

## Order consistency findings

Order lifecycle cannot create or override return lifecycle. Order IDs, order items, sub-orders, and item statuses are lineage evidence only and must be accepted by current backend-bound refund/detail validation before return evidence can be shown.

## Payment consistency findings

Payment lifecycle cannot create or repair return lifecycle. Payment status, payment record status, provider references, gateway references, execution IDs, and financial refund IDs remain payment/refund evidence only.

## Transition findings

Frontend cannot create, skip, jump, replay, or locally advance return transitions. Super Admin return mutation submission refetches current backend monitoring authority and current detail authority, uses the refreshed backend `returnId`, and only then sends backend-bound intent. Mark-failed additionally requires current `canMarkFailed` evidence.

## Bootstrap findings

Initial load, refresh, reconnect, focus, visibility, route/search changes, history, and BFCache cannot preserve return lifecycle authority. Current list/detail queries validate or withhold; stale selections and dialogs close when current authority is absent, fetching, failed, or mismatched.

## Cache findings

React Query, component state, dialogs, selected IDs, route/search parameters, history state, localStorage, and sessionStorage are candidates only. Cache can display only accepted current backend DTOs and cannot restore lifecycle after authority loss.

## Failure findings

401, 403, 404, 409, 422, malformed DTOs, failed refreshes, failed mutations, unsupported status values, malformed timestamps, malformed action booleans, and substituted mutation responses fail closed. Failures cannot create replacement lifecycle.

## Adversarial findings

Lifecycle replay, storage replay, history replay, route replay, selection replay, mutation replay, actor replay, tenant replay, refund replay, order replay, payment replay, and malformed DTO injection cannot fabricate, advance, regress, repair, merge, reconcile, replay, restore, resurrect, or bypass return lifecycle without current backend authority. No inspected frontend path introduces tenant leakage.

## Runtime hardening performed

No runtime code change was required in M5-C. Existing M4-A/M4-C/M5-A/M5-B boundaries already enforce exact return target validation, current-list/current-detail mutation re-entry, stale dialog closure, and fail-closed DTO validation.

## Exact runtime files modified

None.

## Regression freezes added

- `PointOfSale.Tests/Architecture/ReturnLifecycleAuthorityCertificationTests.cs`

## Architecture updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
- `docs/architecture/return-lifecycle-authority-certification-findings.md`

## Explicit certification answers

- Who owns return lifecycle authority? Backend return/refund/order/payment/tenant/actor workflow, SQL, lock, idempotency, and endpoint authorization authorities.
- Can frontend fabricate lifecycle? No.
- Can customer advance lifecycle? No.
- Can vendor advance lifecycle? No.
- Can monitoring become lifecycle authority? No.
- Can refund lifecycle create return lifecycle? No.
- Can order lifecycle create return lifecycle? No.
- Can payment lifecycle create return lifecycle? No.
- Can frontend create transitions? No.
- Can frontend skip states? No.
- Can stale lifecycle survive refresh? No.
- Can cache restore lifecycle? No.
- Can replay restore lifecycle? No.
- Can tenant leakage occur? No inspected frontend path bypasses backend tenant/actor authority.
- Were runtime changes required? No.
- Is Return Lifecycle Authority production-certifiable? Yes, for the inspected frontend runtime.
