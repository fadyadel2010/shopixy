# PASS F-M0-B CommerceHub Vendor Session Authority Certification Findings

## Vendor Identity Findings
Vendor identity presentation now derives from current backend POS/Admin `/api/auth/session` authority. Persisted POS auth-store state was removed as a vendor identity source; malformed or incomplete user, tenant, or role DTOs fail closed.

## Login Findings
POS/Admin login no longer promotes the login response into authenticated CommerceHub vendor UI. Login success must re-enter `/api/auth/session` before session presentation is established.

## Eligibility Findings
CommerceHub marketplace eligibility remains backend-owned. The frontend can only present protected endpoint results and cannot grant eligibility from cached listings, route state, storage, or login/refresh DTOs.

## Suspension Findings
Suspension and vendor governance are backend-owned. Protected CommerceHub vendor 401/403 authority loss dispatches terminal frontend purge so suspended vendors cannot preserve active UI from stale runtime state.

## Refresh Findings
Refresh responses are not session presentation authority. Refresh now re-enters `/api/auth/session`; focus, interval, reconnect, and page refresh may revalidate only and cannot resurrect authority.

## Cache Findings
Finance, earnings, payments, and vendor session caches are scoped by current backend tenant/user identity and removed on authority loss/logout. Cache cannot restore vendor authority.

## Cross-Tab Findings
Cross-tab storage is terminal logout evidence only. It clears current-tab session and vendor-scoped query caches and does not bootstrap, repair, or create authority.

## History Findings
Protected routes wait for backend session hydration. Browser history, deep links, bookmarks, and back/forward cannot render authenticated vendor UI until backend session authority is accepted.

## Failure Findings
Malformed session DTOs, protected CommerceHub vendor 401/403, refresh failures, and bootstrap failures fail closed by clearing authenticated presentation when current backend authority is absent.

## Actor-Transition Findings
Logout/login, tenant replacement, and user replacement purge old auth state and scope CommerceHub vendor caches by current tenant/user. Prior vendor identity cannot leak into replacement sessions through auth-store persistence or generic query keys.

## Adversarial Findings
JWT replay, refresh DTO replay, storage replay, cache replay, history replay, cross-tab replay, eligibility replay, suspension replay, debt replay, malformed DTO injection, actor switching, and tenant switching cannot create frontend vendor session authority on inspected runtime paths.

## Runtime Hardening Performed
- Added POS vendor session DTO authority validation.
- Removed persisted POS auth-store session authority.
- Required login and refresh to re-enter `/api/auth/session`.
- Added protected CommerceHub vendor 401/403 authority-loss dispatch.
- Added terminal cross-tab logout purge.
- Scoped CommerceHub finance, earnings, and payments query keys by tenant/user.

## Exact Runtime Files Modified
- `PointOfSale.Web/src/auth/posVendorSessionAuthority.js`
- `PointOfSale.Web/src/store/authStore.js`
- `PointOfSale.Web/src/services/api.js`
- `PointOfSale.Web/src/components/AuthBootstrap.jsx`
- `PointOfSale.Web/src/pages/Login.jsx`
- `PointOfSale.Web/src/components/Layout.jsx`
- `PointOfSale.Web/src/pages/CommerceHubFinance.jsx`
- `PointOfSale.Web/src/pages/CommerceHubEarnings.jsx`
- `PointOfSale.Web/src/pages/CommerceHubPayments.jsx`
- `PointOfSale.Web/src/utils/storageScope.js`
- `PointOfSale.Web/src/queryClient.js`

## Regression Freezes Added
- `PointOfSale.Web/src/auth/posVendorSessionAuthority.test.ts`
- `PointOfSale.Web/src/components/AuthBootstrap.test.tsx`
- `CommerceHubFrontendAuthorityCertificationTests.PosAdminCommerceHubVendorSessionPresentation_MustDeriveFromCurrentBackendAuthorityOnly`

## Architecture Updates
Updated `.ai-context/PROJECT_CONTEXT.md`, `.ai-context/ARCHITECTURE_TRUTH.md`, `.ai-context/KNOWN_RISKS.md`, `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`, and `.ai-context/CODEX_RULES.md` with PASS F-M0-B authority truth and freeze rules.

## Explicit Answers
- Who owns vendor session authority? Backend POS Admin authentication/session, backend vendor, eligibility, suspension, debt-block, participation, and tenant authorities own truth; frontend owns derivative presentation only.
- Can frontend fabricate authentication? No.
- Can frontend grant eligibility? No.
- Can suspended vendors remain active? No inspected path preserves active UI after backend protected authority loss.
- Can debt-blocked vendors preserve capabilities? No inspected path can preserve capabilities after backend debt-block authority rejects them.
- Can cache restore vendor authority? No.
- Can refresh resurrect vendor authority? No.
- Can tenant leakage occur? No inspected actor/tenant cache path remains unscoped after hardening.
- Were runtime changes required? Yes.
- Is CommerceHub Vendor Session Authority production-certifiable? Yes, for inspected frontend runtime paths, with residual browser chaos evidence recommended.
