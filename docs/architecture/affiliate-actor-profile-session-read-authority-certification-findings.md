# PASS AF-PA3.2 Affiliate Actor Profile, Session & Read Authority Certification Findings

Date: 2026-06-25

## Scope

This pass certifies only Affiliate Actor profile, session, portal bootstrap, profile/dashboard rendering, actor-bound reads, cache behavior, browser history, cross-tab behavior, and failure/adversarial profile authority. Commission logic, product selection, and promotion links were intentionally out of scope.

## Certification Result

Affiliate Actor Profile, Session, and Read Authority is production-certifiable after frontend actor-read hardening. Backend SQL `AffiliateUser`/`AffiliateAccount`, `AffiliateAuthController`, the `AffiliateJwt` authentication scheme, current refresh-token hash binding, session cutoff, email verification, active account checks, logout/refresh, and protected affiliate portal/profile/dashboard/analytics endpoints own Affiliate Actor Profile, Affiliate Session, Actor Identity, Profile Truth, Account Status, Dashboard Summary, and read authority.

Frontend portal state, local profile metadata, local/session storage, component state, loading state, browser history/BFCache, tab/query state, cross-tab storage events, request timing, and read cache are derivative presentation only.

## Runtime Pipeline

Visitor -> Authenticated Session -> Backend Profile -> Actor Admission -> Portal Rendering.

Actor Admission occurs in `AffiliatePortal` only after `affiliateAuthApi.restoreSession()` successfully reads `GET /api/affiliate/profile` through the `AffiliateJwt` backend authority path and accepts the returned backend `affiliateAccountId`. The portal withholds rendering before that point.

## Findings

Profile findings:
- Backend profile reads return the current `AffiliateAccountId`, referral code, display name, status, creation/activation timestamps, and payout wallet profile from SQL for the authenticated affiliate actor.
- Dashboard header display name/referral code derive from the backend-admitted profile, not from local storage.
- Profile tab display name, account status, and payout wallet profile derive from `GET /api/affiliate/profile`.

Session findings:
- Login requires backend password verification, email verification, active account status, refresh-token hash persistence, and affiliate cookie issuance.
- Refresh requires the presented refresh token to match current backend hash authority and re-checks email verification and account activity.
- Protected affiliate reads require the `AffiliateOnly` policy, which is bound to the `AffiliateJwt` scheme and current SQL session/profile validation.
- Logout clears backend refresh-token state when present, clears affiliate cookies, purges local presentation metadata, and redirects to login.

Read findings:
- Profile/dashboard/analytics summary reads now send no-store/no-cache request hints.
- Backend controllers resolve read scope from the current backend-authenticated `affiliate_account_id` claim after `AffiliateJwt` validation.
- Stale actor tab content is remounted when the accepted backend `affiliateAccountId` changes.

Cache findings:
- Local storage keeps only non-sensitive presentation metadata and is purged on failed restore/logout.
- React component state is keyed by backend-admitted actor id for portal tab rendering.
- No React Query persistence or browser storage authority is used for affiliate profile/session reads.

Actor isolation findings:
- Actor A cannot appear inside Actor B session after actor replacement because the portal tab boundary is keyed by the backend-admitted affiliate account id.
- Cross-tab changes to affiliate profile metadata force portal presentation to clear and redirect to login.
- Browser back/history can restore only a candidate route; portal rendering re-enters backend profile authority before display.

Failure findings:
- Failed restore clears stale local metadata and withholds portal rendering.
- 401 from protected affiliate reads attempts backend refresh; failed refresh clears presentation and redirects to login.
- 403/404/timeouts do not create actor authority; affected surfaces fail closed or show unavailable state.

Adversarial findings:
- Modified local storage cannot admit the portal because backend profile restore is required.
- Modified session/JWT must pass signature, issuer/audience, role, user/account identity, active account, email verification, refresh-token hash, and session cutoff validation.
- Replayed old profile reads cannot remain visible as current actor after backend actor replacement because tab state remounts on accepted actor id change.
- Frontend cannot fabricate profile, account status, payment profile, dashboard summary, or statistics authority.

## Runtime Hardening Performed

- `AffiliatePortal` now keys tab rendering by `profile.affiliateAccountId` plus active tab, remounting tab-local profile/dashboard state on backend-admitted actor replacement.
- `affiliateAuthApi.restoreSession` now sends no-store/no-cache profile-read hints.
- `affiliateApi` now sends no-store/no-cache hints for profile, dashboard, earnings summary, and analytics summary reads.

## Exact Runtime Files Modified

- `PointOfSale.Web/src/modules/affiliate/pages/AffiliatePortal.jsx`
- `PointOfSale.Web/src/services/affiliateAuthApi.js`
- `PointOfSale.Web/src/services/affiliateApi.js`

## Regression Freezes Added

- `PointOfSale.Web/src/services/affiliateAuthApi.test.ts`
- `PointOfSale.Web/src/services/affiliateApi.test.ts`
- `PointOfSale.Tests/Architecture/AffiliateSessionAuthorityCertificationTests.cs`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Explicit Certification Answers

Who owns Affiliate Actor Profile? Backend SQL and backend affiliate auth/profile services.

Can frontend fabricate profile? No.

Can Actor A appear inside Actor B session? No, after hardening.

Can stale cache survive logout? No authoritative cache survives logout; presentation metadata is purged and backend cookies/session are cleared.

Can browser history restore another actor? No; history can restore a route candidate only, and portal rendering re-enters backend profile authority.

Can local storage become authority? No.

Where does Actor Admission occur? In `AffiliatePortal` after successful backend `/api/affiliate/profile` restore through `AffiliateJwt` authority, using backend `affiliateAccountId`.

Were runtime changes required? Yes.

Is Affiliate Actor Profile & Session Authority production-certifiable? Yes, for the inspected AF-PA3.2 scope.
