# PASS AF-PA3.4 Affiliate Account State Machine & Business Lifecycle Certification

Date: 2026-06-25

## Account State Findings

Affiliate Account State is backend-owned by SQL `AffiliateAccount`, `AffiliateUser.EmailVerified`, `AffiliateAuthController`, `AffiliateJwt` validation, and `AffiliateService`. The runtime state machine is:

Visitor -> Registration Candidate -> Backend Registration -> Registered/Pending -> Verified -> Active -> Suspended -> Reactivated/Active -> Closed.

`Deleted` is supported as `IsDeleted` soft deletion and is terminal for authentication/admission. `Closed` is now an explicit terminal `AffiliateStatus` value.

Only `Active` plus `EmailVerified` can authenticate and access affiliate business capabilities. `Pending`, unverified, `Suspended`, `Closed`, and deleted accounts fail closed.

## Business Capability Findings

Portal, Dashboard, Profile, Wallet, Settings/Profile tab, Analytics, Promotion Link APIs, and related affiliate protected reads/mutations enter through `[Authorize(Policy = "AffiliateOnly")]`. That policy binds to the dedicated `AffiliateJwt` scheme, which re-enters current SQL authority on every protected request.

Frontend state, local/session storage, route tabs, browser history, cached profile metadata, and candidate wallet form state cannot create account state or business capability. They can only present backend-admitted evidence or trigger revalidation.

## Transition Findings

Allowed transitions:

- Visitor -> Registration Candidate: frontend candidate only.
- Registration Candidate -> Registered/Pending: backend registration commit.
- Registered/Pending -> Verified: backend email verification token consumption.
- Verified -> Active: backend verification path activates pending account.
- Active -> Suspended: backend admin/service transition with reason.
- Suspended -> Reactivated/Active: backend admin/service activation.
- Active -> Closed: backend admin/service terminal closure with reason.
- Any non-deleted state -> Deleted: backend soft deletion, if invoked by future administrative flow.

Forbidden transitions:

- Pending -> Suspended.
- Pending -> Closed.
- Suspended -> Closed without reactivation.
- Closed -> Active.
- Closed -> Suspended.
- Frontend -> any lifecycle transition.

## Portal Admission Findings

Portal admission begins at `AffiliatePortal` accepting a successful backend `/api/affiliate/profile` read. Backend admission requires the affiliate JWT to validate against current SQL `AffiliateUser`, current refresh-token hash, email verification, non-deleted account, and `AffiliateAccount.IsActive`.

Suspended, closed, deleted, and unverified accounts cannot be admitted to the portal.

## Session Findings

Login and refresh require current backend email verification and active account state. Access tokens carry `affiliate_session`, and `AffiliateJwt` validation compares it with the current SQL refresh-token hash plus `TokensValidAfterUtc`.

Suspension and closure now revoke sessions through `AffiliateUser.RevokeSessions(nowUtc)`, clearing the refresh token and setting the durable cutoff. Existing access tokens then fail on the next protected request because the account is no longer active and the token predates the cutoff.

## Lifecycle Findings

Refresh, browser back, cross-tab replacement, logout, login again, and session restore converge through backend profile/session revalidation. Stale frontend state can at most remain presentation until the next backend call; it cannot authorize protected APIs.

## Failure Findings

401/403/404, missing profile, failed restore, unverified email, suspended account, closed account, deleted account, invalid refresh, replayed refresh, and stale JWT all fail closed. Failed account states cannot appear active because frontend portal rendering is withheld without current backend profile admission.

## Adversarial Findings

Replay login still requires password, verified email, and active backend account. Replay refresh requires the current hashed refresh token and active backend account. Modified JWTs fail signature/issuer/audience/session/current SQL checks. Modified local storage, profile metadata, cookies, or candidate profile/wallet values cannot bypass `AffiliateOnly` backend authorization.

## Runtime Hardening Performed

- Added explicit terminal `AffiliateStatus.Closed`.
- Added `ClosedAtUtc` and `ClosureReason` backend evidence.
- Tightened domain transitions so only active accounts can suspend or close, and closed accounts cannot reactivate.
- Changed registration to remain `Pending`; backend email verification activates pending accounts.
- Added `CloseAffiliateAsync` and admin close endpoint.
- Added session revocation on suspension and closure.
- Updated EF status check constraint to include `Closed`.
- Exposed closure evidence in backend profile/admin detail DTOs.

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/AffiliateAccountStateMachineCertificationTests.cs`
- Updated `PointOfSale.Tests/Affiliate/AffiliateDomainTests.cs`
- Updated `PointOfSale.Tests/Architecture/AffiliateGovernanceCertificationTests.cs`

## Certification Answer

Affiliate Account State Machine is production-certifiable. Backend owns account state, verification, activation, suspension, reactivation, closure, session authority, portal admission, and business capability. Frontend owns only presentation, navigation, candidate requests, loading, status display, and guidance.
