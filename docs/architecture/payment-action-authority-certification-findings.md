# Payment Action Authority Certification (PASS M3-F)

## Implemented Payment Actions

Actual frontend runtime action surfaces found in code:

- Storefront checkout payment submission and provider handoff for Stripe, Paymob, COD, and manual wallet proof.
- CommerceHub customer continue/retry payment, submit refund request, upload refund evidence, save/update refund payout details, and select saved payout details for a refund.
- POS vendor submit manual vendor payment proof and open submitted proof.
- POS customer/supplier receive payment and make payment.
- POS manual wallet admin approve payment, reject payment, and view proof.
- Super Admin vendor payment-method creation, vendor payment proof access, approve vendor payment request, and reject vendor payment request.
- Super Admin refund review, approve refund, reject refund, retry refund execution, execute/send refund payout, override return status, and mark return failed.
- Super Admin vendor payout queue record completed payout.
- Super Admin vendor finance adjustment and settlement recording.

No frontend-created payment action grant, provider action grant, refund execution authority, payout execution authority, settlement authority, or approval authority was found.

## Findings

- Action findings: payment actions are derivative from current backend DTOs and endpoint authorization. Frontend code may show controls, validate input, and submit intent; it does not own action eligibility, approval, execution, payout, settlement, retry, or refund truth.
- Customer findings: retry payment re-fetches current backend order authority immediately before provider handoff. Refund action controls derive from backend `canRefund` item evidence. Runtime hardening now rebinds an open refund modal to the current order item and rejects saved payout selections that are absent from the current backend payout-method list.
- Vendor findings: vendor payment submission derives from current vendor session, current financial dashboard authority, current eligibility, and selected backend payment method. Proof access re-enters backend proof authorization.
- Admin findings: Super Admin payment monitoring is visibility only. Approve/reject modals hold ID candidates and resolve them against the current backend-backed list. Runtime hardening now also binds payout execution modals to the current queue row and closes/disables execution when current payout authority disappears.
- Refund findings: Super Admin refund actions derive from current validated refund list/detail DTOs and backend action booleans. Refund execution requires backend endpoint authority and current refund lifecycle evidence; frontend status, selected rows, and drawer state cannot execute refunds alone.
- Provider findings: Stripe outcomes, Paymob redirects, manual-wallet proof artifacts, and callback/recovery data are evidence or lookup candidates only. They cannot create retry, success, approval, refund, or payout authority.
- Order consistency findings: payment retry/refund action presentation remains inside current order identity, lifecycle, ownership, visibility, financial, and split-order boundaries. Refunded/terminal orders cannot expose active payment actions unless the current backend DTO grants them.
- Bootstrap findings: initial load, refresh, focus, visibility, reconnect, history, and BFCache paths re-enter backend authority or withhold action presentation.
- Cache findings: React Query, route state, selected rows, local state, localStorage, sessionStorage, and mutation responses cannot restore actions. Customer refund and Super Admin payout selections are re-derived from current accepted backend evidence.
- Failure findings: 401, 403, 404, 409, 422, malformed DTOs, action failures, and failed refreshes fail closed or converge through backend refetch; they do not create replacement actions.
- Adversarial findings: action replay, refund replay, recovery replay, storage replay, history replay, route replay, actor replay, tenant replay, selection replay, and mutation replay cannot fabricate, restore, transfer, preserve, or execute payment action authority on inspected frontend paths.

## Runtime Hardening Performed

- Customer refund modal action target now rebinds from `activeRefundItem` to the current backend-backed order item before presentation and closes when current `canRefund` disappears.
- Customer saved payout selection now requires membership in the current backend-backed payout-method list before refund submission.
- Super Admin payout execution now resolves execution target, vendor identity, method choices, action availability, and mutation target from the current backend-backed payout queue, not the originally selected modal object.

## Exact Runtime Files Modified

- `PointOfSale.CommerceHub/src/pages/CustomerOrderDetailsPage.tsx`
- `PointOfSale.CommerceHub/src/features/customer/components/RefundRequestModal.tsx`
- `PointOfSale.Web/src/super-admin/payouts/PayoutsPage.tsx`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/PaymentActionAuthorityCertificationTests.cs`

## Architecture Updates

- Added this M3-F findings file.
- Updated authority context, hardening context, known risks, project context, and Codex rules with M3-F payment action authority truth.

## Certification Answers

- Who owns payment action authority? Backend payment, refund, retry, manual payment, settlement, approval, execution, payment proof, lifecycle, order, customer, vendor, tenant, provider, payout, and Super Admin authorization services.
- Can frontend fabricate actions? No.
- Can customers perform unauthorized actions? No inspected frontend path can do so without current backend authority.
- Can vendors perform unauthorized actions? No inspected frontend path can do so without current backend authority.
- Can monitoring become action authority? No.
- Can refunds bypass lifecycle authority? No.
- Can redirects create actions? No.
- Can stale actions survive refresh? No after M3-F runtime hardening.
- Can cache restore actions? No.
- Can replay restore actions? No.
- Can tenant leakage occur? No inspected frontend action path introduces tenant leakage.
- Were runtime changes required? Yes.
- Is Payment Action Authority production-certifiable? Yes, for the inspected frontend runtime paths, contingent on backend authorization and lifecycle enforcement remaining in force.
