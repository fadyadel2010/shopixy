# PASS AF-PA3.3 Affiliate Profile Edit, Payment Profile & Candidate Authority Certification

Date: 2026-06-25

## Scope

Certified only Affiliate Profile editing, Payment Profile editing, and candidate authority for the implemented runtime surface.

Excluded by instruction: product selection, commissions, withdrawals.

Runtime surface found:
- Affiliate profile read: `GET /api/affiliate/profile`.
- Editable payment profile surface: legacy payout wallet update, `PUT /api/affiliate/profile/payout-wallet`.
- No runtime endpoint was found for affiliate display-name edit, phone edit, multiple payment methods, remove payment method, or default payout method selection. Those are future-path certification debt and must be backend-owned before rollout.

## Profile Findings

Backend owns accepted Affiliate Profile through `AffiliateAccount` SQL and `AffiliatePortalController.GetProfile`. Frontend `ProfileTab` displays backend-issued `displayName`, `email`, `status`, `createdAt`, referral code, and wallet fields.

Current runtime does not expose editable display-name or phone controls for affiliates. Therefore frontend cannot commit display-name or phone edits because there is no submitted backend mutation path for those fields. Any future profile edit endpoint must preserve the same candidate-to-backend-acceptance model.

Frontend local storage keeps non-sensitive presentation metadata through `affiliateAuthApi`, but protected portal rendering and `ProfileTab` profile data are admitted from backend `/affiliate/profile`, not local storage.

## Payment Profile Findings

The implemented payment profile is one payout wallet on `AffiliateAccount`: `PayoutWalletNumber`, `PayoutWalletType`, and `PayoutWalletUpdatedAtUtc`.

Backend mutation resolves the current affiliate actor from the protected affiliate session, loads the current non-deleted `AffiliateAccount`, and calls `AffiliateAccount.UpdatePayoutWallet`. The domain method delegates to `PayoutWallet.Create`, which normalizes and validates wallet number/type before SQL persistence.

Runtime hardening now requires frontend `ProfileTab` to perform a fresh backend profile read after `updatePayoutWallet` succeeds before accepted wallet presentation or success state is promoted.

## Candidate State Findings

Frontend wallet form state is candidate intent only. Cancel restores from the current accepted `data` baseline. Refresh, logout, login, actor replacement, and portal remount re-enter backend profile/session authority.

The hardening closes the prior ambiguity where mutation acknowledgement could show success before the profile read baseline reconverged. Failed post-mutation profile reads now keep the UI in failure/candidate handling instead of promoting visible accepted wallet truth.

## Field Dependency Findings

Backend validation owns wallet type and number compatibility:
- Mobile wallets (`VodafoneCash`, `OrangeMoney`, `EtisalatCash`, `InstaPay`) normalize to Egyptian 11-digit format.
- Bank/Other require 6-32 alphanumeric characters.

Frontend validation remains guidance only and cannot make invalid combinations accepted. Unsupported or malformed combinations fail at backend/domain validation.

## Lifecycle Findings

`AffiliatePortal` bootstraps and periodically revalidates via backend profile restore, keys tab rendering by backend-admitted `affiliateAccountId`, and redirects on profile/session loss. `ProfileTab` reads profile on mount and applies profile data only from backend reads.

Candidate edits are in component memory. Refresh, tab remount, logout before save, and login again discard candidate edits unless backend has accepted them and a current backend read returns them. Actor A edits cannot appear for Actor B because portal actor replacement remounts tab-local state by backend account ID.

## Failure Findings

400/422 validation failures remain backend rejection and surface as errors. 401 triggers affiliate session refresh/logout handling through `affiliateAxios`. 403/404/409/500/timeouts/offline failures do not promote accepted profile/payment state.

After hardening, failed update or failed post-update profile refetch cannot appear as committed accepted profile data. Partial frontend commit is avoided because visible accepted wallet state is not patched from candidate form values or from the mutation acknowledgement.

## Adversarial Findings

Modified form state, hidden/read-only DOM edits, altered wallet type/number, replayed update calls, and modified client validation can at most alter a backend-bound candidate payload. Backend actor resolution, domain validation, SQL persistence, and current profile read admission remain decisive.

Frontend cannot fabricate accepted payment methods in the implemented surface. The browser has no accepted multi-method/default method state, and accepted wallet presentation is derived from backend profile reads.

## Profile State Machine

Backend Accepted Profile

↓ backend `/api/affiliate/profile`

Frontend Baseline

↓ user edits wallet candidate form

Candidate Profile / Payment Candidate

↓ candidate differs from baseline

Dirty State

↓ `PUT /api/affiliate/profile/payout-wallet`

Submission

↓ backend actor resolution, domain validation, SQL save

Backend Acceptance

↓ fresh backend `/api/affiliate/profile` read admission

Committed Profile Presentation

↓ apply backend profile response

New Baseline

Profile authority begins at backend acceptance in SQL and becomes frontend accepted presentation only after current backend profile read admission.

## Runtime Hardening Performed

- `ProfileTab` now gates accepted wallet success/display promotion on a fresh backend profile read after successful wallet mutation.
- Added a synchronous wallet save in-flight fence.
- Added accessible edit label and label/control associations for wallet candidate controls.
- Moved wallet success/error rendering so accepted backend convergence remains visible after edit mode exits.

## Exact Runtime Files Modified

- `PointOfSale.Web/src/modules/affiliate/pages/portal/ProfileTab.jsx`

## Regression Freezes Added

- `PointOfSale.Web/src/modules/affiliate/pages/portal/ProfileTab.test.tsx`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Explicit Certification Answers

Who owns Affiliate Profile Authority? Backend SQL `AffiliateAccount` plus protected backend affiliate profile/session endpoints.

Who owns Payment Profile Authority? Backend SQL `AffiliateAccount`, `AffiliatePortalController.UpdatePayoutWallet`, and `PayoutWallet` domain validation for the implemented payout-wallet surface.

Can frontend fabricate payment methods? No. Frontend can only submit candidate wallet intent; accepted payment profile presentation comes from backend profile reads.

Can candidate edits become accepted profile? Only after backend mutation acceptance and current backend profile read admission.

Can Actor A edits appear for Actor B? No. Portal actor admission is backend-profile based and tab state remounts by backend `affiliateAccountId`.

Can failed updates appear successful? No after hardening. Mutation success alone is insufficient; visible success waits for backend profile baseline refresh.

Can reset restore stale data? Reset/cancel restores the current accepted frontend baseline. Lifecycle re-entry refreshes from backend; future profile/payment surfaces must keep reset tied to backend-admitted baselines.

Where does Profile Authority begin? Backend SQL acceptance, then frontend accepted presentation begins only after current backend profile read admission.

Were runtime changes required? Yes, frontend candidate convergence hardening in `ProfileTab.jsx` plus a focused regression freeze.

Is Affiliate Profile & Payment Candidate Authority production-certifiable? Yes, for the inspected implemented runtime surface, with future display-name, phone, multi-method, removal, and default-method features explicitly requiring backend-owned implementation and certification before rollout.
