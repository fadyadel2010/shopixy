# PASS AF-PA3.1 Affiliate Identity, Registration & Entry Authority Certification Findings

Date: 2026-06-25

## Scope

This pass certifies only Affiliate website identity, registration, authentication, session, and dashboard entry authority. Commission logic, product selection, and promotion links were intentionally out of scope.

## Certification Result

Affiliate Identity, Registration, and Entry Authority is production-certifiable after runtime hardening. Backend SQL plus `AffiliateAuthController`, affiliate JWT/cookie validation, affiliate profile reads, refresh/logout, account status checks, email verification, database uniqueness constraints, and protected affiliate portal endpoints own Affiliate Identity, Registration Acceptance, Authentication, Authorization, Account Creation, and Account Status.

Frontend landing, register, login, portal shell, local profile metadata, loading state, validation copy, duplicate-click fences, navigation, bookmarks, browser history, and tab/query state remain presentation or backend-bound candidate intent only.

## Identity Lifecycle Map

Visitor -> Registration Candidate -> Backend Validation -> Affiliate Identity Created -> Verified Login -> Authenticated Session -> Dashboard Entry.

Affiliate identity begins at backend registration commit: `AffiliateAccount` and `AffiliateUser` are persisted by `AffiliateAuthController.Register` after backend validation and SQL uniqueness acceptance. Authenticated authority begins later, only after backend-accepted verified login/refresh issues affiliate cookies.

## Findings

Registration findings:
- Frontend registration collects display name, email, and password candidate input only.
- Registration success no longer caches an authenticated affiliate profile or navigates directly to the portal.
- Duplicate same-click frontend submission is synchronously fenced.
- Backend duplicate email/account/referral races are normalized through SQL unique-constraint handling into `409 Conflict`.

Identity findings:
- Backend owns affiliate identity through `AffiliateAccount` and `AffiliateUser`.
- Local storage stores non-sensitive presentation metadata only after backend login/profile/refresh acceptance.
- Failed backend session restore purges local profile metadata, so stale local data cannot preserve identity presentation.

Entry findings:
- Public landing/register/login remain public presentation surfaces.
- `/affiliate/portal` restores the current backend affiliate profile before rendering protected portal presentation.
- Failed bootstrap, session loss, and cross-tab profile changes withhold portal content and redirect to login.

Session findings:
- Login requires backend password verification, verified email, active account, backend refresh-token persistence, and affiliate cookie issuance.
- Refresh re-enters backend refresh-token hash, email verification, and active-account checks.
- Logout clears backend refresh-token state where present, clears affiliate cookies, and purges frontend presentation metadata.

Failure findings:
- Invalid input remains a failed candidate state.
- Duplicate registration returns a backend rejection, not a success-shaped identity.
- Failed restore/network/backend authority loss clears stale local presentation.
- Registration failure does not cache affiliate identity or authenticate the browser.

Adversarial findings:
- Modified registration DTOs cannot submit tenant/store/role/account authority because the backend DTO admits only email, password, and display name.
- Modified registration responses are not cached as session authority.
- Modified local storage cannot enter dashboard because portal entry revalidates `/affiliate/profile`.
- Replayed login/refresh must pass backend credential/token/account checks.

## Runtime Hardening Performed

- Added frontend stale local affiliate profile purge on failed `restoreSession`.
- Added explicit portal fail-closed rendering when backend profile is unavailable.
- Added portal cross-tab local profile metadata invalidation.
- Added synchronous duplicate-submit fences to affiliate registration and login forms.
- Added backend duplicate-registration race normalization for affiliate SQL uniqueness violations.

## Exact Runtime Files Modified

- `PointOfSale.Web/src/services/affiliateAuthApi.js`
- `PointOfSale.Web/src/modules/affiliate/pages/AffiliateRegister.jsx`
- `PointOfSale.Web/src/modules/affiliate/pages/AffiliateLogin.jsx`
- `PointOfSale.Web/src/modules/affiliate/pages/AffiliatePortal.jsx`
- `PointOfSale.API/Controllers/AffiliateAuthController.cs`

## Regression Freezes Added

- `PointOfSale.Web/src/services/affiliateAuthApi.test.ts`
- `PointOfSale.Tests/Affiliate/AffiliateIdentityTests.cs`

## Explicit Certification Answers

Who owns Affiliate Identity? Backend SQL and backend affiliate auth/account services.

Can frontend fabricate Affiliate identity? No.

Can duplicate registration occur? No accepted duplicate identity; SQL uniqueness is decisive, and duplicate races return conflict.

Can failed registration appear successful? No.

Can dashboard be accessed before backend authentication? No protected portal presentation is withheld until backend profile restore succeeds, and backend endpoints remain authorized.

Where does Affiliate identity begin? At backend registration commit when `AffiliateAccount` and `AffiliateUser` are persisted after backend validation.

Were runtime changes required? Yes.

Is Affiliate Identity & Registration production-certifiable? Yes, for the inspected AF-PA3.1 scope.
