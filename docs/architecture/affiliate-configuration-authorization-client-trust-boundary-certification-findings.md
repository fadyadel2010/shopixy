# PASS AF-PA1.5 Affiliate Configuration Authorization & Client Trust Boundary Certification Findings

Status: Production-certifiable after frontend authority-loss convergence hardening.

Date: 2026-06-25

Scope: Frontend-only Affiliate Configuration authorization boundaries and client trust assumptions: POS auth/session bootstrap, route guards, role presentation, conditional rendering, session refresh, token refresh, tenant/actor replacement, feature visibility, hidden controls, DevTools/browser-storage tampering, 401/403 handling, and backend authority re-entry. Backend implementation, persistence, DTO mutation internals, and business validation were intentionally out of scope.

## Authority Conclusion

Affiliate Configuration Authorization is backend-owned. Backend authentication, authorization, role resolution, permission resolution, tenant resolution, store resolution, claims validation, feature authorization, business authorization, mutation authorization, and session authority remain decisive.

Frontend owns presentation only: route/menu visibility, loading states, error rendering, conditional UI, navigation, user guidance, and backend-bound request dispatch. Frontend state, JWT/session presentation, cached role values, route guards, hidden buttons, disabled controls, browser storage, React Context/Zustand, and DevTools-modified state cannot become business authority.

## Runtime Hardening Performed

- Added Affiliate Configuration admin endpoints to the POS protected-authority-loss revalidation boundary.
- A backend 401/403 from `/admin/affiliates`, `/admin/affiliate/categories/*`, or `/admin/products/{id}/affiliate/*` now dispatches the POS authority-lost event.
- `AuthBootstrap` already handles that event by withholding protected route presentation and re-reading `/auth/session` before restoring the route shell.
- Exact product matching was kept to `/admin/products/{id}/affiliate/*` so unrelated product admin reads do not trigger this Affiliate Configuration boundary.

This hardening does not authorize anything in the browser. It shortens stale presentation windows after backend denial and forces frontend convergence through backend session authority.

## Authorization Findings

- `ProtectedRoute` withholds POS protected presentation until `/auth/session` bootstrap/hydration completes.
- `PosRoleRouteGuard` suppresses `/admin/affiliates` and `/admin/affiliates/:id` presentation unless the current backend-hydrated role is `Owner` or `Admin`.
- The route guard and sidebar role checks are presentation alignment only. Backend affiliate admin endpoints remain the authority for access, reads, and mutations.
- No frontend permission, role, tenant, store, feature, or business decision is trusted as authorization.

## Client Trust Findings

- The POS frontend does not store bearer access/refresh tokens in localStorage; legacy token keys are actively cleared.
- The shared API client uses `withCredentials`, CSRF headers for unsafe methods, and backend session refresh rather than trusting a locally decoded JWT.
- Auth state is accepted only from backend-shaped `/auth/session` evidence through `derivePosVendorSessionUserAuthority`.
- Local roles, navigation capabilities, and vendor lifecycle data are presentation evidence only and are revalidated by backend endpoints.

## Session Findings

- Initial load, hard refresh, focus, visibility restoration, online events, BFCache restoration, browser history popstate, and periodic polling re-enter `/auth/session`.
- 401 responses attempt backend refresh then re-read `/auth/session`; refresh failure forces local logout and protected-route removal.
- 401/403 from Affiliate Configuration endpoints now immediately triggers protected-route withholding plus backend session revalidation.
- Stale open pages may briefly show old presentation until a backend denial or lifecycle revalidation, but they cannot retain business authority because every read/mutation re-enters backend authorization.

## Claims Findings

- JWT claims are not decoded by the inspected frontend as authority.
- Role/tenant/user values enter runtime through backend session DTO evidence only.
- Malformed session DTOs fail closed and clear protected presentation.
- Modified client state can only alter local presentation until the next backend session/API authority check.

## Feature Visibility Findings

- Hidden sidebar entries, hidden buttons, disabled controls, and CSS/DOM changes are not security boundaries.
- Removing CSS or rendering hidden components cannot authorize reads or mutations because all Affiliate Configuration API calls remain backend-authorized.
- Product affiliate submission DTOs remain explicit allowlists from AF-PA1.4 and do not include tenant/store/actor/permission/version fields.

## Route Trust Findings

- Routes are presentation only. `/admin/affiliates` routes sit under `ProtectedRoute` and `PosRoleRouteGuard`.
- Deep links, bookmarks, refreshes, browser history, and BFCache restoration re-enter backend session bootstrap/lifecycle validation.
- Route state does not select tenant, store, role, permission, or business authority.

## Tenant Findings

- Tenant and actor presentation are accepted from backend session DTO shape and are not selected by URL, storage, or route state.
- Actor/tenant/role/authentication transitions clear React Query and POS runtime caches.
- Affiliate admin reads and mutations derive tenant/store authority on the backend; frontend tenant values are display/cache-scope evidence only.

## Failure Findings

- 401: backend refresh plus `/auth/session`; refresh failure logs out and removes protected presentation.
- 403: remains backend denial; Affiliate Configuration denials now trigger immediate session revalidation and route withholding.
- Refresh/session failure fails closed by clearing authenticated presentation.
- Network failures, timeouts, and backend errors render errors/loading states only and do not mint authority.

## Adversarial Findings

- DevTools edits to React state, Zustand auth state, role, tenant, permission flags, feature flags, hidden components, disabled controls, localStorage, or sessionStorage can change only local presentation.
- Modified JWTs, forged claims, replayed requests, injected roles, injected permissions, or storage tampering are untrusted client candidates. Backend session and endpoint authorization remain decisive.
- Hidden controls can be exposed and clicked, but requests still cross the backend authority boundary.

## Authority Boundary Audit

Runtime authority pipeline:

```text
Browser / DevTools / Storage / React State / Zustand
↓
AuthBootstrap session presentation from /auth/session
↓
ProtectedRoute and PosRoleRouteGuard presentation
↓
Affiliate Configuration UI rendering and backend-bound request intent
↓
HTTP request with cookies and CSRF header

========================
AUTHORITY BOUNDARY
========================

Backend authentication
↓
Backend session, role, tenant, store, and claim validation
↓
Backend Affiliate Configuration authorization
↓
Application services and business rules
↓
Persistence
```

Frontend authority ends before the HTTP request crosses into backend authentication/authorization. Backend authority begins at request authentication/session validation and remains decisive through services, business rules, and persistence.

## Exact Runtime Files Modified

- `PointOfSale.Web/src/auth/posVendorSessionAuthority.js`

## Regression Freezes Added

- `PointOfSale.Web/src/auth/posVendorSessionAuthority.test.ts`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Explicit Certification Answers

- Who owns Affiliate Configuration Authorization? Backend authentication/session, backend role and permission resolution, trusted tenant/store context, claims validation, feature/business authorization, mutation authorization, services, and persistence.
- Can frontend authorize actions? No.
- Can frontend trust JWT alone? No.
- Can frontend trust cached permissions? No.
- Can frontend trust local roles? No.
- Can DevTools elevate authority? No.
- Can hidden buttons become authority? No.
- Can removed CSS expose authority? No; it can expose presentation only.
- Can stale sessions retain authority? No business authority; stale presentation is removed by lifecycle/session revalidation or backend denial.
- Can tenant switching leak authorization? No inspected path; actor/tenant transitions purge derivative caches and backend derives tenant authority.
- Can client-side state become trusted authority? No.
- Where is the exact Authority Boundary? At backend request authentication/session and authorization after the browser sends an HTTP request.
- Were runtime changes required? Yes, frontend authority-loss convergence was added for Affiliate Configuration endpoint 401/403 denials.
- Is Affiliate Configuration Authorization production-certifiable? Yes.
