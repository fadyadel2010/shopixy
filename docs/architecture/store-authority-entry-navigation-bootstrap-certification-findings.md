# PASS SF-PA1.1 Store Authority Entry, Navigation & Bootstrap Certification

## Scope

This pass certifies only Store Authority entry, sidebar navigation, protected route admission, runtime bootstrap, permission resolution, session restoration, cache invalidation, recovery, failure, concurrency, and initial authority establishment for POS Store Administration.

In scope routes: `/storefront`, `/storefront/themes`, `/storefront/theme`, `/storefront/builder`, `/storefront/versions`, `/storefront/cms/pages`, `/storefront/cms/pages/:pageId`, `/storefront/cms/navigation`, `/storefront/cms/footer`, and `/ecommerce/auth-providers`.

Out of scope for this pass: Store Configuration editing, Theme editing, Store Builder editing, CMS content editing, Publish workflow, Orders, Checkout, Products, Coupons, CommerceHub, Affiliate, Payments, Shipping, and Inventory.

## Entry Findings

Store Authority entry is backend-owned. Frontend route state, sidebar expansion, hidden links, direct URLs, bookmarks, browser history, React state, and local/session storage do not establish Store Authority.

The backend session bootstrap endpoint `/api/auth/session` returns the current backend-shaped POS actor, `NavigationCapabilities`, and `VendorLifecycleAuthority`. The frontend accepts that session only through `requirePosVendorSessionUserAuthority`, which rejects malformed actor id, tenant id, role, duplicate/unknown capabilities, lifecycle mismatch, and lifecycle/capability incoherence.

Runtime hardening was required: `CommerceHubVendorRouteGuard` and `Sidebar` previously checked `commercehub.storefront.*` capabilities but did not apply the backend `blocksStorefront` lifecycle blocker at the same route/menu admission boundary. A stale or lifecycle-blocked Storefront capability could render the Storefront shell until a backend API denied data. Runtime now uses `canAccessCommerceHubVendorNavigationCapability`, which requires current backend-shaped session authority plus the applicable lifecycle blocker to be open before rendering Storefront routes or sidebar entries.

## Navigation Findings

Navigation Authority is backend-owned through `AuthController.BuildCommerceHubNavigationCapabilitiesAsync` and `BuildCommerceHubVendorLifecycleAuthorityAsync`. Frontend navigation renders only from accepted backend session evidence.

Storefront sidebar entries now require backend capability and `blocksStorefront !== true`. CMS entries require `commercehub.storefront.cms.manage` and `blocksStorefront !== true`. Auth Providers requires `commercehub.storefront.manage` and `blocksStorefront !== true`.

Hidden menus are not security. Backend `[Authorize(Roles = "Owner,Admin")]`, current POS session authority, tenant context, and endpoint authorization remain decisive.

## Bootstrap Findings

Bootstrap Authority begins at backend `/api/auth/session`, after current POS session validation, tenant validation, role validation, vendor lifecycle resolution, and backend navigation capability resolution. Frontend `AuthBootstrap` owns only loading, withholding route presentation, cache purging, and applying backend-admitted presentation state.

Runtime pipeline:

1. Application starts and mounts `AuthBootstrap`.
2. `AuthBootstrap` clears legacy token storage and calls `/api/auth/session`.
3. Backend validates POS session through current SQL-bound session authority.
4. Backend validates current actor, role, tenant, session binding, tenant active state, and governance state.
5. Backend resolves Store Authority inputs: tenant identity, POS role, vendor lifecycle, debt/governance blockers, and navigation capabilities.
6. Frontend accepts only valid backend-shaped session evidence through `requirePosVendorSessionUserAuthority`.
7. `ProtectedRoute` withholds protected routes until hydration completes.
8. Sidebar renders Storefront navigation only from `canAccessCommerceHubVendorNavigationCapability`.
9. `CommerceHubVendorRouteGuard` admits Storefront routes only from the same backend-derived capability plus lifecycle blocker evidence.
10. Protected route rendering begins only after backend admission evidence is accepted.

Store Authority begins at backend `/api/auth/session` current-session and lifecycle/capability resolution. Permission Authority begins at backend navigation capability resolution. Navigation becomes available only after frontend accepts backend-shaped session evidence and the lifecycle blocker permits the requested capability.

## Permission Findings

Permission resolution is backend-owned. Frontend does not infer Storefront permissions from role, URL, route name, cached page data, component state, or React Query. Runtime route/menu admission uses backend-provided `NavigationCapabilities` and `VendorLifecycleAuthority` only.

Admin Storefront APIs in scope are backend-protected: `AdminStoreConfigController`, `AdminCmsPagesController`, `AdminStorefrontFooterController`, and `StorefrontAuthProvidersAdminController` require Owner/Admin authorization and tenant context. CMS legacy routes under `/api/admin/pages` are aliases on the same backend-authorized controller.

## Session Findings

Expired sessions cannot continue as Store Administration authority. `AuthBootstrap` revalidates on initial load, interval, focus, online, visibility, BFCache `pageshow`, and popstate. Backend 401 refresh attempts re-enter `/api/auth/refresh` and then `/api/auth/session`; failed refresh clears POS auth state and redirects protected POS routes to login.

Role changes, session rotation, tenant changes, and governance restrictions converge through backend session validation and frontend actor-scope cache purge. `PosSessionAuthority` rejects stale role claims, revoked/rotated session bindings, inactive/deleted actors, tenant mismatch, inactive/deleted tenants, and banned/suspended tenant governance.

## Cache Findings

React Query, memory cache, browser cache, local storage, session storage, BFCache, and component state are presentation only. Actor-scope changes clear query caches and invalidate POS runtime cache. Authority-loss events from protected Storefront/CommerceHub endpoints purge relevant queries and re-enter `/api/auth/session` while withholding route presentation.

Cached permissions cannot survive backend revocation as authority. They may exist transiently as stale presentation, but route admission and recovery revalidation must converge to backend truth or fail closed.

## Recovery Findings

Refresh, browser restart, application reload, server restart, React remount, lost cache, lost session, and BFCache restoration re-enter backend session/bootstrap authority. If backend bootstrap fails, protected route presentation is withheld or unauthenticated state is applied. Stale authority cannot be restored from browser storage because POS user authority is not persisted as trusted Store Authority and legacy token storage is cleared.

## Failure Findings

400, 401, 403, 404, 409, 422, 429, 500, timeout, network failure, and bootstrap failure fail closed for authority. 401/403 from Storefront/CommerceHub protected authority endpoints dispatch POS vendor authority-loss revalidation. 401 refresh failure logs out. 403 does not grant route admission. Network/timeout failures may preserve loading/error presentation but cannot create Store Authority.

## Concurrency Findings

Multiple tabs converge through backend session revalidation, storage logout signals, focus/visibility/online/page lifecycle revalidation, and actor-scope cache clearing. Logout in another tab clears local authority presentation. Role/permission/store ownership changes are observed when any tab re-enters `/api/auth/session` or receives backend 401/403 from protected Storefront endpoints.

No tab can retain Store Authority as a business authority source after backend revocation. A stale tab can only show transient presentation until the next revalidation or protected API denial, then route authority is withheld and backend truth is reloaded.

## Adversarial Findings

Route edits, StoreId/TenantId edits, permission edits, role edits, session edits, navigation edits, React state edits, cached response edits, and replayed bootstrap candidates cannot fabricate Store Authority. Frontend session acceptance requires backend-shaped GUID actor/tenant evidence, allowed POS role, allowed capability names, no duplicate capabilities, matching lifecycle tenant/vendor id, valid lifecycle status, valid boolean blockers, and lifecycle/capability coherence.

Deep links and bookmarks cannot bypass authorization. The route guard now denies Storefront route shells unless backend-admitted capability and `blocksStorefront !== true` are present, while backend endpoint authorization remains decisive.

## Authority Boundaries

Authentication Boundary: backend POS auth/session owns authentication; frontend renders login/loading/error only. Recovery source is `/api/auth/session` or `/api/auth/refresh`; failure logs out or withholds protected routes.

Session Boundary: backend `PosSessionAuthority` and current SQL own session validity; frontend stores no authority. Recovery source is current SQL session binding; failure clears POS auth presentation.

Permission Boundary: backend `BuildCommerceHubNavigationCapabilitiesAsync` owns capabilities; frontend renders from accepted evidence only. Recovery source is backend session bootstrap; failure hides navigation/routes.

Tenant Boundary: backend tenant context and `TenantLineageAuthority` own tenant resolution; frontend tenant ids are candidates/presentation only. Recovery source is backend session and endpoint tenant context; failure denies.

Store Boundary: backend tenant/store lifecycle and Storefront admin services own Store Identity and Store Authority; frontend route params and URLs are presentation only. Recovery source is backend session/API reads; failure withholds.

Navigation Boundary: backend navigation capabilities plus lifecycle blockers own navigation admission; frontend sidebar/route guard render only. Recovery source is `/api/auth/session`; failure redirects/withholds.

Bootstrap Boundary: backend `/api/auth/session` owns bootstrap authority; frontend `AuthBootstrap` owns loading and cache purge. Recovery source is backend session re-entry; failure hydrates null or keeps withheld route presentation.

Projection Boundary: frontend pages, React Query, CMS/store config projections, and browser caches are display/read candidates only. Recovery source is backend API re-entry; failure shows error/skeleton, not authority.

Recovery Boundary: backend current SQL/session/lifecycle owns recovery truth; frontend lifecycle events trigger revalidation only. Failure behavior is fail closed.

Cache Boundary: React Query, POS runtime cache, browser storage, and BFCache are never authority. Recovery source is backend revalidation and cache invalidation; failure cannot promote cached authority.

## Runtime Hardening Performed

Added `canAccessCommerceHubVendorNavigationCapability` to require backend-shaped POS session authority, backend-issued capability, and the matching lifecycle blocker before frontend route/menu presentation.

Changed `CommerceHubVendorRouteGuard` to use the new admission helper, so direct/bookmarked Storefront routes fail closed when backend lifecycle blocks Storefront.

Changed `Sidebar` to use the same admission helper, so Storefront navigation visibility and protected route admission are aligned.

## Exact Runtime Files Modified

`PointOfSale.Web/src/auth/posVendorSessionAuthority.js`

`PointOfSale.Web/src/components/CommerceHubVendorRouteGuard.jsx`

`PointOfSale.Web/src/components/Sidebar.jsx`

## Regression Freezes Added

`PointOfSale.Web/src/auth/posVendorSessionAuthority.test.ts` now freezes Storefront navigation admission denial when backend lifecycle blocks Storefront despite capability presence.

`PointOfSale.Web/src/components/CommerceHubVendorRouteGuard.test.tsx` now freezes direct/bookmarked Storefront route denial when backend lifecycle blocks Storefront.

Focused verification command: `npm test -- --run src/auth/posVendorSessionAuthority.test.ts src/components/CommerceHubVendorRouteGuard.test.tsx`.

Result: 2 test files passed, 12 tests passed. React Router emitted existing future-flag/act warnings.

## Architecture Documentation Updates

This document records PASS SF-PA1.1 and the runtime hardening performed.

## AI Context Updates

Updated `.ai-context/PROJECT_CONTEXT.md`, `.ai-context/ARCHITECTURE_TRUTH.md`, `.ai-context/KNOWN_RISKS.md`, `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`, and `.ai-context/CODEX_RULES.md`.

## Explicit Certification Answers

Who owns Store Authority? Backend current POS session, tenant/store lifecycle, Storefront admin services, and SQL persistence.

Who owns Navigation Authority? Backend `/api/auth/session` navigation capability and lifecycle blocker resolution.

Who owns Bootstrap Authority? Backend `/api/auth/session` after current SQL/session/tenant/role/lifecycle validation.

Can frontend establish Store Authority? No.

Can cached permissions become authority? No.

Can React Query become authority? No.

Can browser cache bypass backend validation? No.

Can deep links bypass authorization? No.

Can stale tabs retain Store Authority? No, they must revalidate and converge or fail closed.

Can Store Administration render before backend admission? No protected route rendering is admitted only after accepted backend session evidence; after hardening, Storefront capability also requires lifecycle blocker admission.

Where does Store Authority begin? Backend `/api/auth/session` and protected Storefront admin endpoint authorization, backed by current SQL/session/tenant/lifecycle truth.

Where does Bootstrap Authority begin? Backend `/api/auth/session`.

Were runtime changes required? Yes, frontend Storefront route/sidebar admission was hardened to honor backend `blocksStorefront` lifecycle authority.

Is Store Authority production-certifiable? Yes, after runtime hardening and regression freezes, Store Authority entry/navigation/bootstrap is production-certifiable for the scoped pass.
