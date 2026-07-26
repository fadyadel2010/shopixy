# PASS SF-ADM2.9 Builder Version History & Rollback Authority Certification

Date: 2026-06-30

Scope: PointOfSale.Web Builder Version History and Rollback runtime only, plus directly consumed Store Config version/rollback backend APIs and projection consumption.

## Pages Reviewed

- `PointOfSale.Web/src/modules/storefront/pages/VersionHistoryPage.jsx`
- `PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx`
- `PointOfSale.Web/src/modules/storefront/components/builder/RollbackModal.jsx`
- `PointOfSale.Web/src/modules/storefront/components/builder/BuilderTopBar.jsx`
- `PointOfSale.Web/src/modules/storefront/services/storeConfigService.js`
- `PointOfSale.Web/src/modules/storefront/store/builderStore.js`
- `PointOfSale.Web/src/modules/storefront/types/builder.js`

## Backend Endpoints Reviewed

- `GET /api/admin/store-config`
- `GET /api/admin/store-config/versions`
- `POST /api/admin/store-config/rollback/{version}`
- `GET /api/storefront/store-config`

## Backend Services Reviewed

- `AdminStoreConfigController`
- `AdminStoreConfigService`
- `PublishLockService`
- `StoreConfigAuditService`
- `StorefrontConfigProjectionConsumer`
- `TenantLineageAuthority`
- `StorefrontStoreConfigController`
- `StorefrontDraftConfigMapper`

## Findings

Version History is backend-owned. It begins at SQL `StoreConfigs.VersionsJson` plus `LiveVersion`, is read through `AdminStoreConfigService.GetVersionHistoryAsync`, and is presented by PointOfSale.Web only after `GET /api/admin/store-config/versions`. Frontend version arrays, modal state, route state, React state, and browser storage are not version authority.

Rollback is backend-owned. Frontend may send rollback intent only. Rollback Authority begins in `AdminStoreConfigController.RollbackStoreConfig` after trusted tenant context, Owner/Admin authorization, explicit confirmation, mandatory `If-Match`, and RowVersion parsing. The transaction boundary is EF `SaveChangesAsync` inside `PersistStoreConfigChangeAsync`. The service resolves the historical version from backend `VersionsJson`, validates snapshot JSON and theme authority, extracts historical live state, preserves current drafts, creates a new monotonic version snapshot, persists SQL, stages `StoreConfigRolledBackEvent`, and records audit.

Snapshot authority is backend-owned. Version snapshots are created by `CreateAndAppendVersionSnapshot` from backend-accepted live state. Rollback rejects missing versions, malformed snapshots, missing/retired theme presets, malformed theme overrides, and current theme authority violations. Frontend cannot fabricate snapshots because rollback accepts only a version number and confirmation/reason; snapshot lookup occurs inside the backend service.

Failure and recovery are fail-closed. 400/rollback rejection, 401/403 authorization failure, 404-style missing tenant/config through guarded reads, 409 stale RowVersion/concurrency, 422 malformed state where applicable, 428 missing/invalid `If-Match`, 500, timeout, restart, Redis loss, Mongo rebuild, and projection rebuild cannot activate a rollback without SQL acceptance. Recovery converges through current SQL `StoreConfigs`, admin reread admission, or projection recomposition from SQL.

Concurrency is backend-owned. Multiple admins, publish during rollback, rollback during publish, duplicate rollback, retry, replay, stale tabs, and stale RowVersion are bounded by mandatory `If-Match`, EF RowVersion original-value binding, tenant publish/rollback locks, monotonic `LiveVersion`, and projection event fencing/freshness filters. Duplicate rollback may create a new monotonic publication only after fresh backend RowVersion admission; it cannot overwrite newer state with stale authority.

Cache is derivative. React/Zustand state, browser cache/BFCache, LocalStorage, SessionStorage, Redis, and Mongo may present or invalidate only. Redis is invalidated after accepted projection; Mongo is rebuilt from current SQL; neither can resolve rollback or become version authority.

Cross-system visibility begins only after backend acceptance. Builder sees accepted admin DTOs, Preview remains draft-only, Published Storefront/SEO/Analytics/Checkout and adjacent consumers see rollback only after SQL live state and projection/read-model consumption. Affiliate, CommerceHub, Orders, Checkout, Marketplace, and Theme runtime are not authority sources for rollback.

## Rollback Pipeline

- Version begins: backend SQL `StoreConfigs.VersionsJson` plus `LiveVersion`.
- Rollback begins: frontend rollback intent in `VersionHistoryPage.handleRollback` or `StorefrontBuilderPage.handleRollback`.
- Authority begins: `AdminStoreConfigController.RollbackStoreConfig` after tenant/session/role and RowVersion admission.
- Transaction begins: EF persistence boundary in `PersistStoreConfigChangeAsync`.
- Current Draft begins: existing backend SQL draft branches preserved by `PreserveDraftBranches`.
- Current Live Version begins: new backend `CreateAndAppendVersionSnapshot` publication, never historical version reuse.
- Projection begins: `StoreConfigRolledBackEvent` consumption and SQL recomposition in `StorefrontConfigProjectionConsumer`.
- Presentation begins: accepted backend DTO admission and version-history reload.

## Runtime Hardening Performed

- `VersionHistoryPage` now reads/admits current backend store config before rollback.
- Standalone rollback now sends backend RowVersion through `If-Match`.
- Standalone rollback response must pass `deriveCurrentStoreIdentityPresentation` before success presentation.
- Version-history reads now use no-store headers.

## Exact Runtime Files Modified

- `PointOfSale.Web/src/modules/storefront/pages/VersionHistoryPage.jsx`
- `PointOfSale.Web/src/modules/storefront/services/storeConfigService.js`

Backend runtime files modified: none.

## Regression Freezes Added

- `PointOfSale.Web/src/modules/storefront/versionHistorySourceOfTruth.test.ts`
- `PointOfSale.Web/src/modules/storefront/services/storeConfigService.test.ts`
- `PointOfSale.Tests/Architecture/StoreBuilderVersionHistoryRollbackAuthorityCertificationTests.cs`

## Architecture Documentation Updates

- `docs/architecture/store-builder-version-history-rollback-authority-certification-findings.md`

## AI Context Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Certification Answers

Reviewed only PointOfSale.Web runtime: yes, plus directly consumed Store Config backend APIs/services.

Frontend files modified: `VersionHistoryPage.jsx`, `storeConfigService.js`.

Backend files modified: none.

Version History, Version Snapshot, Rollback, Current Draft, Current Live Version, and Projection Trigger are backend-owned. Frontend cannot perform rollback; it sends intent only. React, LocalStorage, Redis, Mongo, stale DTOs, stale RowVersion, failed rollback, duplicate rollback, browser refresh, server restart, Redis loss, Mongo rebuild, and projection rebuild cannot become version or rollback authority. Rollback cannot bypass RowVersion or Publish Authority.

Runtime changes were required for standalone Version History RowVersion admission. Builder Version History & Rollback is production-certifiable.
