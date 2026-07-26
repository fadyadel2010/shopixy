# PASS F-M0-E CommerceHub Route Authority Certification

Status: production-certifiable for inspected customer, POS vendor/admin, and Super Admin frontend runtime routes after route lifecycle, redirect, and tenant-scope hardening.

## Protected-Route Findings

Customer, POS, and Super Admin protected routes wait for backend-hydrated session authority. Runtime guards now also withhold protected presentation while focus, reconnect, browser history, or BFCache restoration revalidates backend authority. Routes cannot establish authentication, actor, role, tenant, or governance authority.

## Deep-Link Findings

Direct URLs, bookmarks, copied links, and external links select candidate screens only. Protected screens re-enter current backend session authority before presentation. Public order-recovery links remain backend token/session validated and do not establish customer authority.

## Refresh Findings

Hard reload and bootstrap call current backend session endpoints. Customer uses `/api/customers/auth/me`, POS uses `/api/auth/session`, and Super Admin uses `/api/super-admin/auth/session`. Cached frontend identity is not sufficient.

## History Findings

Back/forward and BFCache restoration now enter a non-presenting validation state and revalidate backend authority. History cannot repaint a protected route as current authority while validation is pending.

## Redirect Findings

Customer, POS, and Super Admin post-login redirect candidates are normalized to same-origin internal paths. Malformed, protocol-relative, cross-origin, backslash, and Super Admin login-loop redirects fail closed to actor-specific defaults. Redirect targets remain navigation candidates only.

## Actor-Transition Findings

Customer, POS, and Super Admin actor replacement remains fenced by session revision checks and cache purge. Route lifecycle revalidation cannot preserve a prior actor route during customer, user, role, tenant, or Super Admin replacement.

## Authority-Loss Findings

Protected 401/403 handling clears the relevant frontend session presentation and scoped caches. Route lifecycle validation withholds protected content before the backend result settles, and failures redirect through the actor-specific login guard.

## Route-State Findings

React Router location state may carry an internal redirect candidate only. It cannot carry actor, role, tenant, governance, capability, or session truth. POS CommerceHub vendor identity no longer reads `tenantId` from local storage; it uses the current backend-hydrated auth store.

## Cross-Tab Findings

Storage events remain terminal authority-loss signals only. They clear presentation and caches and cannot create or restore route authority. Normal cross-tab data remains subject to backend session revalidation.

## Adversarial Findings

URL replay, history replay, redirect replay, route-state replay, storage replay, cache replay, malformed redirect state, BFCache replay, actor switching, and tenant switching cannot establish protected route access on inspected paths. Backend endpoints remain decisive for screen data and mutations.

## Runtime Hardening Performed

- Added fail-closed route presentation withholding during focus, online reconnect, `popstate`, visibility return, and persisted `pageshow` revalidation.
- Added same-origin internal redirect normalization for customer and POS login flows.
- Hardened Super Admin redirects against cross-origin, protocol-relative, malformed, and login-loop targets.
- Removed POS CommerceHub vendor tenant presentation from `localStorage`; current backend-hydrated POS session identity now supplies tenant scope.

## Exact Runtime Files Modified

- `PointOfSale.CommerceHub/src/store/customer-auth-store.ts`
- `PointOfSale.CommerceHub/src/providers/CustomerSessionBootstrap.tsx`
- `PointOfSale.CommerceHub/src/router/CustomerRouteGuards.tsx`
- `PointOfSale.CommerceHub/src/pages/CustomerLoginPage.tsx`
- `PointOfSale.CommerceHub/src/pages/CustomerRegisterPage.tsx`
- `PointOfSale.Web/src/store/authStore.js`
- `PointOfSale.Web/src/auth/posVendorSessionAuthority.js`
- `PointOfSale.Web/src/components/AuthBootstrap.jsx`
- `PointOfSale.Web/src/pages/Login.jsx`
- `PointOfSale.Web/src/pages/CommerceHubSettings.jsx`
- `PointOfSale.Web/src/super-admin/auth/SuperAdminLoginPage.tsx`
- `PointOfSale.Web/src/super-admin/auth/SuperAdminProtectedRoute.tsx`

## Regression Freezes Added

- Updated `PointOfSale.Web/src/auth/posVendorSessionAuthority.test.ts`.
- Updated `PointOfSale.Web/src/components/AuthBootstrap.test.tsx`.
- Added `CommerceHubFrontendAuthorityCertificationTests.CommerceHubRouteAuthority_MustRevalidateBackendTruthAndFailClosedAcrossNavigationLifecycle`.

## Architecture Updates

Updated all five `.ai-context` authority files with PASS F-M0-E ownership, runtime hardening, risk, and freeze rules.

## Explicit Answers

- Who owns route authority? Current backend authentication, session, actor, role, tenant, and governance authorities. Frontend route presentation is derivative only.
- Can routes establish authority? No.
- Can deep links bypass authority? No.
- Can history restore access? No.
- Can redirects restore access? No.
- Can stale routes survive authority loss? No on inspected paths; protected presentation is withheld during lifecycle revalidation and cleared on rejection.
- Can route state become authority? No.
- Can tenant leakage occur? No inspected route path retains browser-storage tenant scope; vendor tenant presentation derives from the current backend-hydrated actor.
- Were runtime changes required? Yes.
- Is CommerceHub Route Authority production-certifiable? Yes, for inspected runtime paths after hardening.

## Remaining Operational Evidence

Retain deployed-browser chaos coverage for simultaneous tabs, delayed storage events, BFCache restore, offline/reconnect, session revocation during navigation, role/tenant replacement, and network partitions during session revalidation.
