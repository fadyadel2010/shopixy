# PASS F-M0-A CommerceHub Customer Session Authority Certification Findings

Scope: CommerceHub customer-facing frontend runtime session presentation.

Status: CommerceHub Customer Session Authority is production-certifiable for inspected frontend runtime paths after session DTO validation, login `/me` revalidation, profile DTO boundary hardening, broad customer-authority 401/403 purge, and terminal-event cross-tab hardening.

## Session Identity Findings

Current customer and session presentation now derive from backend `/api/customers/auth/me` authority. Frontend state stores only the accepted current profile presentation and has no persisted auth storage. Malformed, unverified, expired, missing, or non-current DTOs fail closed and clear customer presentation.

## Login Findings

Login submits credentials to backend authority and treats the login response as evidence only. Authenticated UI presentation and customer query cache population occur only after the cookie-backed backend `/me` endpoint accepts the current session and returns a valid profile DTO.

## Refresh Findings

CommerceHub customer refresh tokens remain absent. Page refresh, focus, visibility, interval, and reconnect behavior revalidate `/me`; they do not rotate, repair, extend, or resurrect sessions from frontend state.

## Logout Findings

Logout calls backend revocation, clears frontend session presentation, removes customer-scoped query caches, clears recovery context, and broadcasts terminal revocation to other tabs. Password reset, password change, email change, and revoke-all also clear local presentation and broadcast terminal revocation where handled by the frontend.

## Cache Findings

Zustand customer auth state and React Query customer keys are derivative presentation caches only. The auth store is not persisted to localStorage or sessionStorage. Customer-scoped query caches are removed on authority loss and terminal events.

## Cross-Tab Findings

Cross-tab storage events are logout/revocation signals only. They cannot create or restore authority. Receiving tabs purge customer presentation and customer-scoped caches, then future presentation must re-enter backend `/me` authority.

## History Findings

Route guards wait for hydration and derive authenticated navigation from current in-memory `/me` presentation. Browser history, redirects, deep links, back/forward, and bookmarks cannot restore sessions because refresh and bootstrap call backend authority rather than storage or route state.

## Failure Findings

Protected customer authority 401/403 responses dispatch immediate authority-loss purge across customer account, order, refund, payout, checkout/payment, and authenticated review-context/mutation paths. `/me` failures, malformed DTOs, aborted current checks, and login establishment failures do not preserve stale identities as current truth. Failures fail closed.

## Actor-Transition Findings

Logout/login replacement, password reset, password change, email change, and revoke-all clear prior customer presentation and customer query caches before replacement authority can render. CommerceHub customer identity is global; tenant/order ownership remains backend SQL authority and is not selected by frontend state.

## Adversarial Findings

JWT replay, cache replay, storage replay, history replay, refresh replay, cross-tab replay, identity replay, actor switching, tenant/order switching, and malformed DTO injection cannot create authenticated presentation on inspected paths. The frontend cannot read HttpOnly JWT cookies and cannot derive session truth from cookie presence.

## Runtime Hardening Performed

- Added frontend customer profile and auth response authority boundaries.
- Required login success to revalidate current backend `/me` authority before session presentation.
- Purged session presentation and customer query caches on protected customer 401/403 authority loss, including checkout/payment and customer review authority paths.
- Broadcast terminal revocation for password reset, password change, email change, revoke-all, and logout paths handled by the frontend.

## Exact Runtime Files Modified

- `PointOfSale.CommerceHub/src/auth/customer-session-authority.ts`
- `PointOfSale.CommerceHub/src/services/customer-auth-service.ts`
- `PointOfSale.CommerceHub/src/store/customer-auth-store.ts`
- `PointOfSale.CommerceHub/src/pages/CustomerLoginPage.tsx`
- `PointOfSale.CommerceHub/src/providers/CustomerSessionBootstrap.tsx`
- `PointOfSale.CommerceHub/src/api/http-client.ts`
- `PointOfSale.CommerceHub/src/pages/CustomerSettingsPage.tsx`
- `PointOfSale.CommerceHub/src/pages/CustomerResetPasswordPage.tsx`

## Regression Freezes Added

- `CommerceHubFrontendAuthorityCertificationTests.CustomerCommerceHubSessionPresentation_MustDeriveFromCurrentBackendAuthorityOnly`

## Architecture Updates

Updated all five `.ai-context` authority files with PASS F-M0-A runtime truth, hardening, known risks, and freeze rules.

## Explicit Answers

- Who owns customer session authority? Backend CommerceHub customer authentication/session/identity/revocation/governance authority, backed by current SQL and `CustomerJwt` validation.
- Can frontend fabricate authentication? No on inspected paths.
- Can revoked sessions survive? No as current frontend presentation after backend rejection or terminal-event convergence.
- Can storage restore sessions? No. Storage is terminal revocation signal only.
- Can refresh resurrect sessions? No. Refresh/reconnect/focus/visibility re-enter `/me`; no customer refresh token exists.
- Can history restore sessions? No. Route/history state must re-enter backend session authority.
- Can tenant leakage occur? No inspected frontend session path selects tenant/order ownership; backend SQL authority owns order/tenant visibility.
- Were runtime changes required? Yes.
- Is CommerceHub Customer Session Authority production-certifiable? Yes, for inspected frontend runtime paths.

## Remaining Debt

- Production browser chaos evidence should cover BFCache, multi-tab revocation, delayed storage events, network partitions during `/me`, and backend revocation-cache outages.
- Customer active-device inventory and individual remote-session termination remain outside the current frontend model.
