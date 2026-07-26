# PASS F-M0-F CommerceHub Navigation Authority Certification

Status: production-certifiable for inspected customer, vendor, and Super Admin runtime navigation paths after backend capability issuance, strict frontend capability validation, capability-filtered menus, and capability-gated routes.

## Customer Navigation Findings

Customer account, settings, orders, and refund navigation now require canonical capabilities issued by the current backend customer profile/session response. The header and protected route tree consume the same validated capability set. Logout, session loss, malformed profile DTOs, refresh failure, and actor replacement remove the profile and therefore remove customer navigation.

## Vendor Navigation Findings

CommerceHub vendor catalog, order, finance, earnings, and debt-repayment navigation no longer derives from frontend role labels. `/api/auth/session` recomposes current user, tenant, role, marketplace eligibility, governance, order-operation, and repayment authority into a bounded capability list. The sidebar filters CommerceHub children by those capabilities, and direct route entry is fenced by matching capability guards.

Suspended or banned vendors cannot retain navigation because backend session authority rejects login/session continuity. Marketplace-ineligible vendors lose catalog and order capabilities. Financially restricted vendors retain only backend-permitted read/repayment navigation; submission actions remain controlled by current backend dashboard capability DTOs such as `canSubmitPayment`.

## Super Admin Navigation Findings

Finance, payments, orders, reviews, users, governance, operations, refunds, payouts, commission, promotions, homepage, storefront defaults, CMS, and footer navigation now require canonical capabilities issued by the current backend Super Admin session response. Desktop/mobile sidebars and direct routes consume the same validated capability set.

Revoked, suspended, banned, financially locked, expired, or token-version-stale Super Admin sessions fail before navigation presentation. Unknown, missing, duplicate, or fabricated capability values invalidate the session DTO.

## Capability Findings

Backend session/profile endpoints own navigation capability truth. Frontend allowlists validate shape and may suppress presentation only. Frontend role strings, route state, history, local state, storage, cache, and feature flags cannot add capabilities.

Business actions remain additionally controlled by endpoint-specific backend capability DTOs and mutation authorization. Navigation capability permits candidate screen presentation; it does not authorize a mutation.

## Refresh Findings

Customer `/me`, POS `/auth/session`, and Super Admin `/auth/session` revalidation replace the accepted capability set. Focus, visibility, online reconnect, interval, history navigation, and BFCache restoration re-enter backend authority. Protected presentation is withheld during route-lifecycle validation.

## Cache Findings

Navigation capability sets live only inside current backend-hydrated auth state. Auth stores are not persisted. Customer, vendor, and Super Admin query caches are purged on authority loss or actor replacement. Expanded sidebar state is presentation-only and cannot restore removed menu items.

## Actor-Transition Findings

Customer replacement purges customer queries. POS user, tenant, or role replacement clears query authority before hydration. Super Admin authority revisions fence stale completions. Since navigation capabilities are carried by the accepted current actor DTO, prior actor or tenant capabilities cannot survive replacement.

## Cross-Tab Findings

Cross-tab storage events remain terminal logout/authority-loss signals only. They clear session presentation and caches. No cross-tab payload can create actor, tenant, role, governance, eligibility, or navigation capability truth.

## Failure Findings

Missing capability arrays, unknown capability values, duplicate capabilities, malformed identities, 401/403 authority loss, governance rejection, session failure, and refresh failure fail closed. Menu and route guards deny presentation when the required backend capability is absent.

## Adversarial Findings

Menu replay, sidebar replay, capability injection, cache replay, storage replay, history replay, cross-tab replay, role replay, eligibility replay, suspension replay, tenant switching, and actor switching cannot create navigation authority on inspected paths. Direct URLs reach capability guards before protected CommerceHub screens render.

## Runtime Hardening Performed

- Added backend-issued customer navigation capabilities to current customer profile/session DTOs.
- Added backend-recomposed vendor CommerceHub capabilities for catalog, orders, finance, payments, and repayment.
- Added backend-issued Super Admin console navigation capabilities.
- Added strict customer, vendor, and Super Admin capability allowlists that reject malformed or fabricated DTOs.
- Filtered customer header, vendor sidebar, and Super Admin desktop/mobile sidebars from accepted backend capabilities.
- Filtered customer dashboard shortcuts, metrics, recent-order queries, and order/refund actions from accepted backend capabilities.
- Added customer, vendor, and Super Admin capability route guards for direct URL, bookmark, and history entry.
- Kept action-level controls derivative from current backend response capabilities.

## Exact Runtime Files Modified

- `PointOfSale.API/Controllers/AuthController.cs`
- `PointOfSale.API/DTOs/Customers/CustomerAuthDtos.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubCustomerAuthService.cs`
- `PointOfSale.API/SuperAdmin/Controllers/SuperAdminAuthController.cs`
- `PointOfSale.API/SuperAdmin/DTOs/SuperAdminLoginResponse.cs`
- `PointOfSale.CommerceHub/src/auth/customer-session-authority.ts`
- `PointOfSale.CommerceHub/src/components/layout/Header.tsx`
- `PointOfSale.CommerceHub/src/pages/CustomerDashboardPage.tsx`
- `PointOfSale.CommerceHub/src/router/CustomerRouteGuards.tsx`
- `PointOfSale.CommerceHub/src/router/index.tsx`
- `PointOfSale.CommerceHub/src/types/customer-auth.ts`
- `PointOfSale.CommerceHub/src/types/index.ts`
- `PointOfSale.Web/src/App.jsx`
- `PointOfSale.Web/src/auth/posVendorSessionAuthority.js`
- `PointOfSale.Web/src/components/CommerceHubVendorRouteGuard.jsx`
- `PointOfSale.Web/src/components/Sidebar.jsx`
- `PointOfSale.Web/src/super-admin/auth/SuperAdminCapabilityRoute.tsx`
- `PointOfSale.Web/src/super-admin/auth/superAdminAuth.types.ts`
- `PointOfSale.Web/src/super-admin/layout/SuperAdminLayout.tsx`

## Regression Freezes Added

- Updated `PointOfSale.Web/src/auth/posVendorSessionAuthority.test.ts`.
- Updated `PointOfSale.Web/src/components/AuthBootstrap.test.tsx`.
- Updated `PointOfSale.Web/src/super-admin/auth/superAdminSession.test.ts`.
- Added `CommerceHubFrontendAuthorityCertificationTests.CommerceHubNavigationAuthority_MustDeriveMenusRoutesAndActionsFromCurrentBackendCapabilities`.

## Architecture Updates

Updated all five `.ai-context` authority files with PASS F-M0-F ownership, runtime hardening, known-risk, and regression-freeze rules.

## Explicit Answers

- Who owns navigation authority? Current backend authentication, session, actor, role, tenant, governance, eligibility, suspension, and capability authorities.
- Can navigation fabricate capabilities? No.
- Can suspended vendors retain navigation? No.
- Can revoked admins retain navigation? No.
- Can stale menus survive refresh? No; current backend revalidation replaces or clears capability presentation.
- Can cache restore navigation? No.
- Can actions appear without capability authority? No inspected protected action is promoted by navigation alone; action presentation remains backend-capability-derived.
- Can tenant leakage occur? No inspected actor/tenant transition retains prior navigation capabilities or scoped caches.
- Were runtime changes required? Yes.
- Is CommerceHub Navigation Authority production-certifiable? Yes, for inspected runtime paths.

## Remaining Operational Evidence

Retain deployed-browser chaos coverage for simultaneous tabs, delayed storage events, BFCache restoration, offline/reconnect, governance changes during navigation, tenant replacement, and capability revocation while a protected screen is open.
