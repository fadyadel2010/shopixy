# PASS SF-ADM2.7 Builder Publish Authority Certification

Date: 2026-06-30

Scope: PointOfSale.Web Store Builder publish runtime only, plus directly consumed Store Config backend publish APIs/services.

## Pages Reviewed

- `PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx`
- `PointOfSale.Web/src/modules/storefront/components/builder/BuilderTopBar.jsx`
- `PointOfSale.Web/src/modules/storefront/components/builder/PublishModal.jsx`
- `PointOfSale.Web/src/modules/storefront/components/builder/PreviewModal.jsx`
- `PointOfSale.Web/src/modules/storefront/store/builderStore.js`
- `PointOfSale.Web/src/modules/storefront/services/storeConfigService.js`

## Backend Endpoints Reviewed

- `GET /api/admin/store-config`
- `PUT /api/admin/store-config/pages/{pageId}`
- `POST /api/admin/store-config/pages/{pageId}/publish`
- `GET /api/storefront/store-config`
- `GET /api/admin/store-config/preview`
- `GET /api/admin/store-config/versions`

## Backend Services Reviewed

- `AdminStoreConfigController`
- `AdminStoreConfigService`
- `PublishLockService`
- `StoreConfigAuditService`
- `StorefrontDraftConfigMapper`
- `StorefrontStoreConfigController`
- `StorefrontPreviewService`
- `StorefrontConfigProjectionConsumer`
- `TenantLineageAuthority`

## Publish Findings

Publish begins in the frontend only as intent when `PublishModal` confirms and `StorefrontBuilderPage.handlePublish` runs. Publish Authority begins only in `AdminStoreConfigController.PublishPage` and `AdminStoreConfigService.PublishPageAsync` after trusted tenant context, Owner/Admin authorization, mandatory `If-Match`, RowVersion admission, publish lock admission, current SQL `StoreConfigs` load, draft existence, live snapshot replacement, version snapshot creation, SQL persistence, and accepted DTO emission.

Runtime hardening closed one failure-presentation gap: missing publish targets and no-draft publish attempts now fail closed with explicit 404/422 responses rather than returning a success-shaped accepted config or generic 500. Lock contention maps to 409 via backend concurrency conflict. Frontend still cannot create live content; it only accepts backend DTOs after publish response admission.

## Version Findings

Published Version is backend-owned. `CreateAndAppendVersionSnapshot` increments `LiveVersion`, snapshots `CustomizationJson`, records actor evidence, and updates `VersionsJson` before the same persistence boundary stages `StoreConfigPublishedEvent`. Frontend version labels, alerts, modals, and Zustand flags are presentation only.

## Projection Findings

Projection begins only after backend persistence stages a `StoreConfigPublishedEvent`. `StorefrontConfigProjectionConsumer` re-enters current SQL authority through `TenantLineageAuthority.LoadCurrentStoreConfigProjectionAuthorityAsync`, maps live content only, writes Mongo through a freshness filter, and invalidates Redis. Mongo/Redis are derivative and cannot become publish authority.

## Failure Findings

- 400: malformed request/tenant failures do not reach publish authority.
- 401/403: ASP.NET authorization and Store Admin session authority block endpoint execution.
- 404: missing publish target fails closed after hardening.
- 409: stale RowVersion, duplicate/concurrent publish, or lock contention fails closed.
- 422: missing draft fails closed after hardening.
- 428: missing/invalid `If-Match` fails before service mutation.
- 500/timeout: no accepted frontend publish state unless backend returns an admitted DTO; SQL `SaveChangesAsync` is the publish commit boundary.

## Recovery Findings

Refresh, reconnect, server restart, Redis loss, Mongo rebuild, and projection rebuild converge to SQL `StoreConfigs` through admin read admission or projection SQL recomposition. Failed or stale publish attempts cannot become live. Preview remains authenticated draft preview and is not live publish authority.

## Concurrency Findings

Multiple admins, duplicate clicks, replay, retry, and stale tabs are bounded by mandatory `If-Match`, EF RowVersion original value binding, tenant publish lock, and projection event fences/freshness filters. A stale publish cannot overwrite a newer publish; it either loses RowVersion/lock admission or is ignored as stale projection evidence.

## Cache Findings

React, Zustand, browser cache/BFCache, LocalStorage, SessionStorage, Redis, and Mongo never own Publish Authority. Frontend cache can hold presentation/candidate state only; Redis/Mongo are invalidated or rebuilt from current SQL.

## Cross-System Findings

Published content becomes visible only after backend SQL acceptance and live mapping/projection consumption. Builder and Preview can show draft evidence; Storefront reads live SQL mapping; Affiliate, CommerceHub, SEO, Analytics, Checkout, Orders, and other systems remain outside this certification except where they consume current Store Config projection/read evidence.

## Publish Pipeline

- Draft: backend SQL `StoreConfigs.CustomizationJson` draft branch.
- Publish Intent: frontend `handlePublish`.
- Backend Validation: `AdminStoreConfigController.PublishPage`.
- RowVersion: `RequireExpectedRowVersion` plus EF original value binding.
- SQL Transaction/Commit Boundary: EF `SaveChangesAsync` inside `PersistStoreConfigChangeAsync`.
- Published Snapshot: `pageContent["live"] = liveObj!`.
- Published Version: `CreateAndAppendVersionSnapshot`.
- Projection: `StoreConfigPublishedEvent` consumed by `StorefrontConfigProjectionConsumer`.
- Accepted DTO: `AdminStoreConfigResponse`.
- Presentation: frontend `admitWorkspaceConfig` then UI success/navigation.

## Boundary Review

- Publish Boundary: backend Store Config endpoint/service owns authority; recovery source is SQL; failure withholds accepted publish.
- Version Boundary: backend `LiveVersion`/`VersionsJson` owns authority; recovery source is SQL; failure is 409/422/500 without success.
- Projection Boundary: backend event consumer owns derivative projection; recovery source is SQL; stale events are fenced.
- Recovery Boundary: backend reads and projection rebuild own recovery; failure withholds presentation.
- Presentation Boundary: frontend owns modal/progress/alerts only; recovery source is accepted DTO; failure shows error.
- Concurrency Boundary: RowVersion and publish lock own conflict resolution; recovery source is current SQL; stale attempts fail closed.

## Runtime Hardening Performed

- Missing publish target now throws `KeyNotFoundException` and maps to 404 `PUBLISH_TARGET_NOT_FOUND`.
- Missing draft now throws `ArgumentException` and maps to 422 `INVALID_STORE_CONFIG`.
- Page publish lock contention now throws `ConcurrencyConflictException` and maps to 409.

## Exact Runtime Files Modified

- `PointOfSale.API/Controllers/AdminStoreConfigController.cs`
- `PointOfSale.API/DTOs/Admin/AdminStoreConfigDtos.cs`
- `PointOfSale.API/Services/Admin/AdminStoreConfigService.cs`

Frontend files modified: none.

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/StoreBuilderPublishAuthorityCertificationTests.cs`

## Architecture Documentation Updates

- `docs/architecture/store-builder-publish-authority-certification-findings.md`

## AI Context Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Certification Answers

- Reviewed only PointOfSale.Web runtime: yes, plus directly consumed publish backend APIs/services.
- Frontend files modified: none.
- Backend files modified: `AdminStoreConfigController.cs`, `AdminStoreConfigDtos.cs`, `AdminStoreConfigService.cs`.
- Publish owner: backend.
- Published Version owner: backend SQL Store Config service.
- Live Store owner: backend SQL live config and live mapper/projection.
- Projection Trigger owner: backend outbox/event after SQL acceptance.
- Publish Transaction owner: backend EF persistence boundary.
- Can frontend publish the Store: no.
- Can React become Publish Authority: no.
- Can LocalStorage become Publish Authority: no.
- Can Redis become Publish Authority: no.
- Can stale publish survive recovery: no.
- Can replay duplicate publish: no; it must re-enter RowVersion/lock authority.
- Can duplicate Publish overwrite newer publish: no.
- Where Publish begins: frontend intent in `handlePublish`.
- Where Publish Authority begins: backend `PublishPage` endpoint/service after precondition admission.
- Where SQL transaction begins: EF persistence boundary in `PersistStoreConfigChangeAsync`.
- Where Published Version begins: `CreateAndAppendVersionSnapshot`.
- Where Projection begins: `StoreConfigPublishedEvent` consumer after backend acceptance.
- Where Presentation begins: accepted backend DTO admission in Builder.
- Runtime changes required: yes, failure-boundary hardening.
- Builder Publish production-certifiable: yes.
