# PASS F-M0-D CommerceHub Actor Resolution Authority Certification

Status: production-certifiable for inspected frontend runtime paths after actor DTO, bootstrap race, lifecycle revalidation, and replacement-cache hardening.

## Actor Identity Findings

Customer, POS vendor/admin, and Super Admin presentation derives from current backend session endpoints. Customer and Super Admin identifiers must be backend GUIDs with valid email evidence. POS identity requires current backend user and tenant GUIDs. Frontend state, storage, routes, history, and caches cannot create an actor.

## Actor-Type Findings

Customer type is fixed by the customer auth boundary. Super Admin requires exact backend `SuperAdmin` role and auth type. POS accepts only canonical backend `UserRole` values: `Owner`, `Admin`, `Manager`, `Cashier`, or `Warehouse`; unknown roles and `SuperAdmin` injected through POS DTOs fail closed.

## Scope Findings

Customer order scope remains backend ownership authority. POS scope requires current backend tenant and user identity, and actor replacement clears React Query authority before the replacement actor is presented. Super Admin scope remains platform session and governance authority.

## Bootstrap Findings

Initial load, refresh, interval, focus, visibility, and reconnect re-enter backend session authority. Customer, POS, and Super Admin asynchronous bootstrap/revalidation responses are fenced so responses started before login, logout, actor replacement, or authority loss cannot overwrite the current actor.

## Transition Findings

Customer replacement purges customer queries before the new profile is cached. POS user, tenant, or role replacement clears query authority before hydration. Super Admin logout and authority replacement increment a runtime revision that prevents stale bootstrap or refresh completion from resurrecting the previous actor.

## Cache Findings

Zustand and React Query remain derivative. Customer query keys are purged on customer identity change. POS query authority is cleared on user, tenant, or role replacement. Super Admin governance caches are purged on authority loss. Cache cannot restore actor authority.

## Cross-Tab Findings

Storage events are terminal revocation signals only. They clear current presentation and caches; they never carry actor identity, actor type, tenant scope, role, governance, or capabilities.

## Route Findings

Routes and navigation select candidate screens only. Protected customer, POS, and Super Admin screens wait for backend-derived hydration. Deep links, history, bookmarks, and redirects cannot establish actor identity or authority scope.

## Failure Findings

401/403 authority loss, malformed DTOs, invalid GUIDs, invalid emails, unknown roles, bootstrap failures, refresh failures, and stale asynchronous completions fail closed. No inspected failure path preserves a rejected actor as current truth.

## Adversarial Findings

Identity replay, role replay, actor replay, cache replay, storage replay, history replay, cross-tab replay, bootstrap replay, tenant switching, session switching, and malformed DTO injection cannot create or extend actor authority on inspected paths.

## Runtime Hardening Performed

- Added strict customer, POS, and Super Admin actor identity/type DTO validation.
- Prevented customer login from merging one login response with a different current `/me` actor.
- Added customer hydration request/revision fencing and customer cache purge on actor replacement.
- Added POS focus, visibility, reconnect, and interval backend revalidation.
- Prevented stale successful POS bootstrap responses from overwriting a newer login actor.
- Purged POS query authority on backend user, tenant, or role replacement.
- Added Super Admin runtime revision fencing across bootstrap, protected-route revalidation, refresh, logout, and actor replacement.

## Exact Runtime Files Modified

- `PointOfSale.CommerceHub/src/auth/customer-session-authority.ts`
- `PointOfSale.CommerceHub/src/store/customer-auth-store.ts`
- `PointOfSale.CommerceHub/src/providers/CustomerSessionBootstrap.tsx`
- `PointOfSale.CommerceHub/src/pages/CustomerLoginPage.tsx`
- `PointOfSale.Web/src/auth/posVendorSessionAuthority.js`
- `PointOfSale.Web/src/components/AuthBootstrap.jsx`
- `PointOfSale.Web/src/super-admin/auth/superAdminAuth.types.ts`
- `PointOfSale.Web/src/super-admin/auth/superAdminAuthStore.ts`
- `PointOfSale.Web/src/super-admin/auth/useSuperAdminSessionBootstrap.ts`
- `PointOfSale.Web/src/super-admin/auth/SuperAdminProtectedRoute.tsx`
- `PointOfSale.Web/src/super-admin/core/superAdminApiClient.ts`

## Regression Freezes Added

- `PointOfSale.Web/src/auth/posVendorSessionAuthority.test.ts`
- `PointOfSale.Web/src/components/AuthBootstrap.test.tsx`
- `PointOfSale.Web/src/super-admin/auth/superAdminSession.test.ts`
- `CommerceHubFrontendAuthorityCertificationTests.CommerceHubActorResolutionPresentation_MustDeriveFromCurrentBackendAuthorityOnly`

## Architecture Updates

Updated all five `.ai-context` authority files with PASS F-M0-D ownership, hardening, risk, and freeze rules.

## Explicit Answers

- Who owns actor resolution authority? Backend customer, POS, Super Admin, session, role, tenant, and governance authorities. The frontend owns derivative presentation only.
- Can frontend fabricate actors? No.
- Can actor type drift? No on inspected paths; unknown or cross-domain roles fail closed.
- Can stale actors survive refresh? No; refresh and lifecycle signals re-enter backend authority, and stale completions are fenced.
- Can capabilities survive actor replacement? No; actor-scoped query authority is purged before replacement presentation.
- Can cache restore actor authority? No.
- Can routes become authority? No.
- Can tenant leakage occur? No inspected user/tenant replacement path retains prior query authority.
- Were runtime changes required? Yes.
- Is CommerceHub Actor Resolution Authority production-certifiable? Yes, for inspected frontend runtime paths.

## Remaining Operational Evidence

Production browser automation should retain BFCache, simultaneous-tab, offline/reconnect, delayed storage-event, session replacement, tenant replacement, and authority-loss race exercises.
