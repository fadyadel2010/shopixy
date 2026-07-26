# PASS AF-PA6.1 Withdrawal Request Authority Certification

Date: 2026-06-26

Scope: Withdrawal Request creation and admission only. Payout execution, settlement, and payment providers were intentionally not reviewed.

## Withdrawal Findings

- Backend owns Withdrawal Request creation through `AffiliatePayoutService.RequestPayoutCoreAsync`.
- `AffiliatePortalController.RequestPayout` resolves the current affiliate from backend session claims and forwards only amount plus optional store-scope intent into the service; it does not perform a separate balance-admission decision.
- SQL `AffiliatePayoutRequest` owns request identity, amount, status, remaining amount, currency, store tenant, affiliate account, and immutable payout wallet snapshot.
- Frontend `PayoutsTab` can create/cancel a local draft form only. There is no backend draft cancellation path in scope; canceling the form cannot create, release, or mutate funds.
- Payout history is backend SQL history projection only.

## Balance Findings

- Available balance is recomputed in `GetAvailableBalanceCoreAsync` from net approved commission (`Approved - Reversed`) plus allowed negative balance movements, withdrawals, and active Pending/Approved payout reservations.
- Pending/Earned commission is not withdrawable.
- `RawAvailable` is the guard value. `AvailableForWithdrawal` is clamped display only.
- Frontend balance cards, form max values, validation text, browser cache, React state, and modified responses cannot create withdrawable money.

## Validation Findings

- Minimum amount is enforced by `AffiliatePayoutRequest.MinimumThreshold`.
- Maximum amount is enforced by comparing the requested amount to backend `RawAvailable`.
- Wallet admission now revalidates current stored wallet data through `PayoutWallet.Create` and snapshots the normalized destination at request creation.
- Payment method/wallet changes after request do not silently redirect a request: approval requires snapshot parity through `EnsurePayoutWalletSnapshotMatches`.
- Withdrawal Admission occurs in `AffiliatePayoutService.RequestPayoutCoreAsync`, after store resolution, balance lock acquisition, current balance recomputation, active duplicate replay check, wallet validation, and before SQL persistence.

## Concurrency Findings

- Request creation runs inside a serializable transaction.
- Backend acquires an affiliate/store payout balance lock before recomputing balance and before inserting a request.
- Active same-affiliate, same-store, same-amount replay now returns the existing Pending/Approved request instead of creating a second reservation.
- Different active requests can exist only if each independently passes fresh backend balance admission after existing reservations are counted.

## Failure Findings

- `400` validation failures do not persist a request.
- `401/403` session or authorization loss cannot reach request creation.
- `404` missing affiliate cannot persist a request.
- `409` payout authority conflicts block admission.
- `422`-class domain validation remains non-persistent because creation happens before `SaveChanges`.
- `500` and timeout-before-commit roll back the transaction.
- Timeout-after-commit replay is contained by active duplicate detection and current reservation recomputation.

## Adversarial Findings

- Modified amount is rechecked against backend raw balance.
- Modified balance, available amount, approved amount, pending amount, reserved amount, remaining balance, request ID, affiliate ID, tenant ID, and wallet data are not accepted from the payout request payload.
- Store tenant ID is only scope intent; an arbitrary store cannot create funds because admission recomputes ledger-backed balance for that store.
- Replay of the same active amount cannot reserve twice.
- Wallet changes cannot bypass validation because request creation snapshots validated wallet data and approval checks snapshot parity.

## Withdrawal Pipeline

Approved Commission

↓

Backend `GetAvailableBalanceCoreAsync` computes Withdrawable Balance from net approved ledger authority

↓

`AffiliatePayoutService.RequestPayoutCoreAsync` performs Withdrawal Admission under serializable transaction and affiliate/store balance lock

↓

`AffiliatePayoutRequest.Create` creates the backend request identity and immutable wallet snapshot

↓

Pending/Approved `AffiliatePayoutRequest` rows become Reserved Balance in the next balance calculation

Withdrawal Admission occurs exactly in `AffiliatePayoutService.RequestPayoutCoreAsync`.

## Runtime Hardening Performed

- Revalidated the current stored payout wallet through `PayoutWallet.Create` before request snapshot capture.
- Added active Pending/Approved duplicate replay detection under the existing request-admission transaction/lock.
- Removed the controller-level preliminary balance check so replay and admission decisions are centralized at the serialized service boundary.
- Added no-store/no-cache request hints for affiliate payout reads and request responses.

## Exact Runtime Files Modified

- `PointOfSale.Infrastructure/Services/Affiliate/AffiliatePayoutService.cs`
- `PointOfSale.API/Controllers/AffiliatePortalController.cs`
- `PointOfSale.Web/src/services/affiliateApi.js`
- `PointOfSale.Web/src/services/affiliateApi.test.ts`
- `PointOfSale.Tests/Architecture/AffiliateWithdrawalRequestAuthorityCertificationTests.cs`

## Regression Freezes Added

- `AffiliateWithdrawalRequestAuthorityCertificationTests` freezes backend admission, raw balance validation, active duplicate replay containment, wallet validation/snapshotting, frontend amount-only payload, and AF-PA6.1 artifact presence.
- `affiliateApi.test.ts` freezes no-store payout calls and amount-only withdrawal request payload.

## Architecture Updates

- Updated all five authority context files with PASS AF-PA6.1 truth, risk closure, hardening, and freeze rules.

## Explicit Certification Answers

- Who owns Withdrawal Authority? Backend `AffiliatePayoutService.RequestPayoutCoreAsync` and SQL ledger/request authority.
- Can frontend fabricate Withdrawal Requests? No.
- Can frontend request more than available? It can send any amount, but backend admission rejects amounts above `RawAvailable`.
- Can duplicate requests occur? Active same-affiliate, same-store, same-amount duplicates cannot create a second reservation.
- Can replay reserve money twice? No.
- Can wallet changes bypass validation? No; wallet is validated and snapshotted at request creation, and approval checks snapshot parity.
- Where does Withdrawal Admission occur? `AffiliatePayoutService.RequestPayoutCoreAsync`.
- Were runtime changes required? Yes.
- Is Withdrawal Request Authority production-certifiable? Yes, for request creation and admission.
