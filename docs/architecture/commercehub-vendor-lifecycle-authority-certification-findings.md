# PASS M1-D CommerceHub Vendor Lifecycle Authority Certification Findings

Date: 2026-06-16

## Certification Result

CommerceHub Vendor Lifecycle Authority is production-certifiable for inspected POS/Admin vendor runtime paths after backend-issued lifecycle authority composition and frontend lifecycle boundary hardening.

Backend vendor, tenant, governance, participation/marketplace approval, KYC, debt, suspension, blocking, and capability authorities own lifecycle truth. The frontend owns derivative presentation only.

## Lifecycle Findings

- POS auth/session/refresh now emits `VendorLifecycleAuthority` from current backend `CommerceHubVendorAuthority` plus current vendor debt authority.
- Frontend session acceptance requires canonical lifecycle evidence bound to the current backend tenant/vendor identity.
- Canonical frontend-accepted statuses are `Pending`, `Approved`, `Active`, `Suspended`, `Blocked`, `DebtBlocked`, `Disabled`, `Rejected`, and `Terminated`.
- Missing, malformed, foreign-tenant, unsupported, alias-shaped, or debt-contradictory lifecycle DTOs fail closed.

## Presentation Findings

- Lifecycle presentation cannot be established from local/session storage, route state, navigation state, React state, contexts, feature flags, mutation responses, history, or cross-tab events.
- Status chips, banners, warnings, and restricted-state presentation must derive from accepted backend lifecycle evidence or endpoint DTOs that match current session scope.
- Frontend lifecycle repair/aliasing is rejected at the session boundary.

## Restriction Findings

- Restricted lifecycle states cannot carry operating capabilities for catalog, order operation, storefront, CMS, or analytics.
- Debt-blocked vendors may retain backend-issued finance/payment remediation presentation only when backend debt evidence agrees.
- Catalog publish/update/unpublish/bulk actions, order operations, storefront/CMS actions, analytics, payment submission, and payout-method changes remain gated by accepted backend lifecycle/capability/evidence.

## Transition Findings

- Pending to approved/active, approved to suspended/blocked/debt-blocked/terminated, and debt-blocked to active transitions re-enter backend `/api/auth/session` authority through refresh/focus/reconnect/session convergence.
- Mutation responses do not patch lifecycle truth into frontend authority.
- Stale lifecycle cannot survive actor/tenant replacement because session DTO validation and scoped cache purge reject previous tenant/vendor evidence.

## Bootstrap Findings

- Initial load, refresh, focus, visibility, reconnect, and session revalidation require current backend session authority.
- Bootstrap cannot restore old lifecycle from persisted auth storage because POS auth-store persistence remains de-authoritized and lifecycle proof is required on the current backend session DTO.

## Cache Findings

- React Query caches, component state, and scoped vendor data remain derivative.
- Cache can present only after current backend session/lifecycle scope is accepted; stale actor/tenant lifecycle evidence fails closed.
- Cross-tab events remain terminal invalidation only and cannot carry lifecycle truth.

## Failure Findings

- 401/403 protected CommerceHub vendor failures purge/revalidate POS vendor authority.
- Malformed lifecycle DTOs, missing lifecycle evidence, and lifecycle/capability contradictions fail closed.
- Failed reads withhold current lifecycle-dependent presentation rather than preserving active state as truth.

## Adversarial Findings

- Lifecycle replay: rejected without current backend-issued tenant-bound lifecycle DTO.
- Governance/debt/suspension/blocking replay: rejected when status, debt flag, booleans, or capabilities contradict.
- Cache/storage/history/mutation replay: cannot establish session lifecycle authority.
- Actor/tenant replay: rejected by GUID tenant binding and scoped purge.
- Backend lifecycle authority cannot be bypassed on inspected runtime paths.

## Runtime Hardening Performed

- Added backend `VendorLifecycleAuthority` DTO composition to POS registration, login, current session, and refresh responses.
- Added frontend `deriveCommerceHubVendorLifecyclePresentation`.
- Hardened `derivePosVendorSessionUserAuthority` to require lifecycle proof and reject restricted lifecycle plus operating capabilities.
- Hardened `deriveCommerceHubVendorEligibilityPresentation` to derive action presentation from accepted lifecycle evidence.

## Runtime Files Modified

- `PointOfSale.API/Controllers/AuthController.cs`
- `PointOfSale.Web/src/auth/posVendorSessionAuthority.js`
- `PointOfSale.Web/src/auth/posVendorSessionAuthority.test.ts`
- `PointOfSale.Tests/TestDoubles/NoOpCommerceHubVendorDebtService.cs`
- `PointOfSale.Tests/Architecture/CommerceHubRefreshReplayCertificationTests.cs`
- `PointOfSale.Tests/Security/PosAuthenticationLifecycleTests.cs`
- `PointOfSale.Tests/Security/ProductionSecurityHardeningTests.cs`

## Regression Freezes Added

- `posVendorSessionAuthority.test.ts` now freezes lifecycle proof requirement, tenant binding, status allowlist, malformed DTO rejection, restricted lifecycle/capability contradiction rejection, and debt-blocked evidence matching.

## Architecture Updates

- Updated all five authority context files with M1-D truth, risks, hardening, and freeze rules.
- Added this findings document.

## Explicit Answers

- Who owns vendor lifecycle authority? Backend vendor, tenant, governance, participation/approval, KYC, debt, suspension, blocking, and capability authorities own lifecycle truth. Frontend owns derivative presentation only.
- Can frontend fabricate lifecycle? No.
- Can blocked vendors appear active? No on inspected paths; blocked lifecycle plus active operating capabilities fails closed.
- Can suspended vendors operate? No; suspended lifecycle cannot carry catalog/order/storefront/CMS/analytics operating capabilities.
- Can debt-blocked vendors continue operating? No; debt-blocked vendors lose operating capabilities and may only retain backend-issued remediation presentation when debt evidence agrees.
- Can stale lifecycle survive refresh? No; refresh re-enters backend lifecycle authority.
- Can cache restore lifecycle? No.
- Can tenant leakage occur? No inspected tenant leakage path remains; lifecycle DTOs are tenant-bound.
- Were runtime changes required? Yes.
- Is Vendor Lifecycle Authority production-certifiable? Yes, with residual deployed-browser chaos coverage retained for multi-tab, BFCache, delayed storage event, and live governance/debt transition scenarios.
