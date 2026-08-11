# Storefront Domain Full Engineering Certification

Date: 2026-08-06

## Executive Summary

The Storefront Domain recovery pass corrected frontend Information Architecture, Storefront sidebar/navigation behavior, authoring UX guards, backend host/domain resolution boundaries, persistence filters, audit immutability, middleware lifetime behavior, and stale resolver/cache contracts.

The implementation now builds and the focused Storefront/domain regression suites pass. Runtime project execution was explicitly skipped by the user after a prior bounded startup probe, so this certification cannot be a clean PASS.

Final certification: PASS WITH OBSERVATIONS.

## Issues Discovered, Root Cause, Solution

| Area | Issue | Root Cause | Solution Implemented |
| --- | --- | --- | --- |
| Information Architecture | Storefront sidebar imported missing IA exports. | Canonical operations/product navigation exports were absent. | Added `OPERATIONS_NAVIGATION`, `STOREFRONT_PRODUCT_NAVIGATION`, and `STOREFRONT_OPERATIONS_SIDEBAR_STATE_KEY`. |
| Navigation | Operations routes lacked canonical breadcrumb/screen resolution. | Storefront IA only resolved Storefront paths. | Added operations screens, breadcrumbs, and route compatibility resolution. |
| Sidebar | Storefront workspace could disappear for users without CommerceHub capabilities. | Capability filtering removed the root product node without rendering an access placeholder. | Added Storefront access placeholder behavior and unified product navigation source. |
| Breadcrumbs | Operations overview used hard-coded breadcrumbs. | Page bypassed canonical IA component. | Replaced with `StorefrontBreadcrumbs`. |
| React Runtime | Native browser confirms remained in Storefront/product-authoring flows. | Confirmation contract was scattered and native APIs bypassed app UX. | Added `ConfirmDialogProvider`, fail-closed default behavior, and converted Storefront/product authoring confirmations to the shared modal. |
| Product Authoring | Product variant/affiliate guards used native confirm for in-app navigation. | Legacy unsaved-change guards lived outside the shared confirm provider. | Converted link/back confirmations to `useConfirmDialog`; retained browser-native `beforeunload` where required. |
| Vite | Production build initially failed because local Vite install was broken. | `node_modules/vite/bin/vite.js` was missing. | Repaired dependency install with `npm ci` and pinned app Vite range to `^5.4.21`. |
| Middleware Runtime | API startup hit scoped-service resolution from the root provider. | `HostResolutionMiddleware` constructor accepted scoped resolver/normalizer services. | Middleware now resolves `IHostNormalizer` and `IDomainResolver` from `HttpContext.RequestServices`. |
| Tests | Host resolution tests still used old constructor/direct resolver patterns. | Tests were stale after middleware lifetime correction. | Updated tests to register resolver/normalizer in request services and use the production-like path. |
| Domain Resolver | Dead `DormantDomainResolver` references remained. | DOMAIN-2B placeholder naming survived DOMAIN-2C activation. | Removed the dead production resolver and added test-only `NotFoundDomainResolver`. |
| Cache Contract | No-op cache was named `DormantDomainResolutionCache`. | The name implied inactive domain lookup after persistence was active. | Renamed to `NoOpDomainResolutionCache` and updated DI/tests/comments. |
| Persistence | Pre-tenant host lookup could be blocked by tenant query filters. | Domain repository queries used global filters before tenant resolution. | Added `IgnoreQueryFilters()` plus explicit `!IsDeleted` conditions. |
| Query Filters | Deleted system/development domains could leak through. | Domain query filter operator precedence was wrong. | Corrected filter to apply `!IsDeleted` across all domain types. |
| Audit Immutability | Domain audit entries lacked full append-only protection. | App guard and migration trigger were incomplete. | Added `GuardAppendOnlyEvidence<DomainAuditEntry>()` and PostgreSQL append-only trigger/function. |
| Documentation Comments | Runtime comments still described dormant/future resolver behavior. | Comments lagged behind active DOMAIN-2C implementation. | Corrected active code comments around host/domain resolution and cache behavior. |

## Architecture Corrections

- Storefront IA now owns canonical Storefront and Operations navigation.
- Sidebar consumes the unified IA tree instead of duplicating operations lookups.
- Storefront product, operations, authoring, and shared product-authoring boundaries now render through aligned navigation/breadcrumb contracts.
- Host resolution now stops at `DomainResolutionResult`; tenant, StoreConfig, Theme, and CMS remain downstream boundaries.

## Runtime Corrections

- `HostResolutionMiddleware` no longer captures scoped services in its constructor.
- `TenantResolutionMiddleware` receives the domain resolution result through `HttpContext.Items`.
- Redirect handling remains host-level and occurs before downstream tenant/store/theme/CMS resolution.
- Storefront/product-authoring confirmations now use the React confirm provider.

## Import Graph Corrections

- Added missing Storefront IA barrel exports.
- Removed dead `DormantDomainResolver` production symbol.
- Replaced stale resolver references in tests.
- Verified Storefront/product-authoring runtime boundary has no native `window.confirm`, `window.prompt`, or `window.alert` calls outside sanitizer test fixtures.

## Navigation and IA Corrections

- Sidebar renders Storefront even for users without CommerceHub capability grants.
- Operations hierarchy is canonical: Overview, Ecommerce, Marketing, Settings.
- Breadcrumbs resolve through `StorefrontBreadcrumbs`.
- Route resolution handles path plus query-string compatibility.

## Persistence and EF Corrections

- Domain repository pre-tenant lookups bypass global tenant filters safely.
- Domain query filters correctly enforce soft-delete visibility.
- Domain audit entries are guarded as append-only in application code and migration SQL.
- Domain persistence tests cover tenant isolation, query filters, repository lookup, and append-only evidence.

## Regression Results

- `npm test -- --run ...storefront... ProductAffiliateSettingsCard.test.tsx ProductVariantsTab.test.tsx --maxWorkers=1 --reporter=dot`: 18 files passed, 182 tests passed.
- `npm run build`: passed, Vite transformed 5085 modules and produced production assets.
- `dotnet build PointOfSale.sln`: passed, 0 errors. Full rebuild still reports pre-existing warnings.
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-build --filter "FullyQualifiedName~DomainPersistenceTests|FullyQualifiedName~HostResolution|FullyQualifiedName~DomainResolutionFoundation|FullyQualifiedName~TenantResolution"`: 146 passed.
- `git diff --check`: passed. Git reported LF-to-CRLF normalization warnings only.

## Runtime Validation

Runtime project execution was skipped by explicit user instruction after a prior bounded startup attempt. The earlier foreground run did not show the original scoped-service crash before timeout, but no final runtime execution gate was completed after the skip instruction.

This prevents clean PASS certification.

## Remaining Observations

- ESLint is configured with legacy `.eslintrc.cjs`; current ESLint 9 expects `eslint.config.*`, so `npm run lint` does not execute without a config migration.
- `npm ci` reported dependency vulnerabilities; no audit fix was applied because that would be a broader dependency/security change.
- The repository contains many pre-existing untracked Storefront Domain artifacts and EF outputs.
- Full backend rebuild emits a broad pre-existing warning surface unrelated to the Storefront Domain recovery changes.

## Final Certification

PASS WITH OBSERVATIONS

The Storefront/domain architecture, IA, imports, build, and focused regression suites are recovered within the verified scope. Clean PASS is withheld because final runtime execution was intentionally skipped and therefore cannot be certified.
