# PASS F-M0-H CommerceHub Foundation Final Certification

Status: production-certifiable for the inspected integrated customer, POS vendor/admin, and Super Admin frontend runtime authority graph after final runtime hardening.

## Session Integration Findings

Customer session presentation re-enters `/api/customers/auth/me`; POS vendor/admin presentation re-enters `/api/auth/session`; Super Admin presentation re-enters `/api/super-admin/auth/session`. Login and refresh responses are evidence only. Stores are not persisted as authenticated authority. Storage and cross-tab events are terminal invalidation only. Refresh, focus, visibility, online, history, and BFCache lifecycle signals withhold or revalidate and cannot resurrect rejected authority.

Final integration hardening corrected POS capability composition so registration, login, current-session, and refresh responses all carry capabilities recomposed from current backend role, tenant, eligibility, governance, order-operation, finance, and repayment authority.

## Actor Findings

Customer, POS, and Super Admin actors require strict backend DTO identity validation. Cross-domain roles fail closed. Stale bootstrap and refresh completions are revision/request fenced. Actor, role, or tenant replacement purges prior scoped presentation before replacement authority renders. Capabilities cannot survive actor replacement independently of the accepted current session DTO.

## Route And Navigation Findings

Protected routes wait for current backend session hydration and withhold presentation during navigation-lifecycle revalidation. Deep links, bookmarks, redirects, browser history, and route state select candidate screens only. Customer, vendor, and Super Admin menus and direct-route guards consume the same accepted backend capability sets. Navigation capability does not authorize mutations; endpoint backend authority remains decisive.

## Scope-Isolation Findings

Customer, POS, Service Marketplace, and Super Admin runtimes retain distinct auth boundaries and query domains. POS tenant selection now derives only from the current backend-hydrated actor; legacy `tenantId` storage is removed rather than written or restored. CommerceHub order queries are scoped by current tenant and user. Actor transitions clear POS query/runtime caches. Marketplace realtime remains tenant/user/request scoped and invalidate-only.

## Cache Findings

React Query, Zustand, component state, bootstrap state, route state, and navigation state remain derivative. CommerceHub orders no longer use unscoped keys or previous-data replay, and failed revalidation withholds cached rows. CommerceHub listing reads clear prior presentation before loading. Publish, update, and unpublish no longer patch listing authority from mutation responses; they refetch current backend reads.

## Realtime Findings

Customer, POS, and Super Admin storage events are distinct terminal authority-loss signals. Service Marketplace BroadcastChannel and SignalR paths validate current scope and invalidate/refetch only. Reconnect cannot restore actors, tenants, capabilities, or subscriptions from stale frontend state; current backend authority must be accepted again.

## Failure Findings

401/403 authority loss clears or revalidates the relevant runtime boundary. Malformed session/capability DTOs fail closed. Bootstrap, refresh, and route-lifecycle failures withdraw protected presentation. Order revalidation failure no longer preserves cached rows as current truth. Listing read failure begins from cleared presentation. No inspected failure fallback creates or repairs authority from frontend state.

## Adversarial Findings

JWT, cache, storage, history, deep-link, navigation, role, capability, actor, tenant, cross-tab, reconnect, and malformed-DTO replay were reviewed across all three runtimes. The combined DTO boundaries, revision fences, route withholding, exact terminal events, scoped caches, actor-transition purge, backend capability composition, and refetch-only convergence prevent frontend authority emergence, tenant leakage, actor leakage, and capability leakage on inspected paths.

## Runtime Hardening Performed

- Composed POS CommerceHub vendor capabilities on registration, login, current-session, and refresh responses.
- Removed persisted POS tenant selection and changed order, storefront preview, and telemetry consumers to the current backend-hydrated actor.
- Scoped CommerceHub order queries by tenant/user, removed previous-data replay, and withheld rows on failed revalidation.
- Cleared CommerceHub listing presentation before current reads.
- Replaced listing publish/update/unpublish response patching with backend refetch convergence.

## Exact Runtime Files Modified

- `PointOfSale.API/Controllers/AuthController.cs`
- `PointOfSale.Web/src/store/authStore.js`
- `PointOfSale.Web/src/services/adminOrdersApi.js`
- `PointOfSale.Web/src/pages/Orders.jsx`
- `PointOfSale.Web/src/pages/CommerceHubSettings.jsx`
- `PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx`
- `PointOfSale.Web/src/modules/storefront/pages/ThemeMarketplacePage.jsx`
- `PointOfSale.Web/src/services/telemetry.js`

## Regression Freezes Added

- `CommerceHubFrontendAuthorityCertificationTests.CommerceHubFoundationFinalAuthorityGraph_MustRemainBackendOwnedFailClosedAndRefetchOnly`
- Existing focused customer, vendor, Super Admin, actor, route, navigation, scope-isolation, and query-domain tests were rerun.

## Architecture Updates

Updated `.ai-context/PROJECT_CONTEXT.md`, `.ai-context/ARCHITECTURE_TRUTH.md`, `.ai-context/KNOWN_RISKS.md`, `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`, and `.ai-context/CODEX_RULES.md` with PASS F-M0-H truth, hardening, residual evidence, and freeze rules.

## Explicit Answers

- Is the CommerceHub foundation authority graph complete? Yes, for inspected runtime paths.
- Can frontend authority emerge? No.
- Can session authority emerge? No.
- Can actor authority emerge? No.
- Can route authority emerge? No.
- Can navigation authority emerge? No.
- Can cache become authority? No.
- Can tenant leakage occur? No on inspected runtime paths.
- Can actor leakage occur? No on inspected runtime paths.
- Were runtime changes required? Yes.
- Is CommerceHub Foundation production-certifiable? Yes, for inspected runtime paths.

## Verification

- `PointOfSale.Web`: focused Vitest authority suite, 20 tests passed.
- `PointOfSale.Web`: production build passed.
- `PointOfSale.CommerceHub`: TypeScript and production build passed.
- `PointOfSale.Tests`: final focused `CommerceHubFrontendAuthorityCertificationTests`, 12 tests passed.

## Remaining Operational Evidence

Retain deployed-browser chaos coverage for simultaneous customer/vendor/Super Admin tabs, BFCache restoration, delayed terminal storage events, offline/reconnect, actor or tenant replacement during in-flight reads, governance revocation while protected screens are open, and malformed DTO injection.
