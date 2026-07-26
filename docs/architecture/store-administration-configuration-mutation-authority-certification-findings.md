# PASS SF-ADM1.4 Store Configuration Mutation Authority Certification

Scope: PointOfSale.Web Store Administration Store Configuration mutation runtime only. Backend APIs/services were reviewed only where directly consumed by Store Administration configuration save/update.

## Pages Reviewed

- `PointOfSale.Web/src/pages/StorefrontSettings.jsx`
- `PointOfSale.Web/src/pages/StorefrontSettings.test.tsx`

## Frontend Services Reviewed

- `PointOfSale.Web/src/modules/storefront/services/storeConfigService.js`
- `PointOfSale.Web/src/modules/storefront/services/storeConfigService.test.ts`
- `PointOfSale.Web/src/auth/commerceHubVendorIdentityAuthority.js`

## Backend Endpoints Reviewed

- `PUT /api/admin/store-config` in `PointOfSale.API/Controllers/AdminStoreConfigController.cs`
- `GET /api/admin/store-config` only as the accepted baseline source directly feeding the save workflow

## Backend Services Reviewed

- `PointOfSale.API/Services/Admin/AdminStoreConfigService.cs`
- `PointOfSale.API/DTOs/Admin/AdminStoreConfigDtos.cs`

## Mutation Findings

Store Administration submits mutation intent only. `StorefrontSettings` builds a candidate DTO from `candidateConfig`, calls `storeConfigService.updateStoreConfig`, and never calls `api.put('/admin/store-config')` directly. Backend mutation authority begins at `AdminStoreConfigController.UpdateStoreConfig` after trusted tenant context, Owner/Admin authorization, DTO validation, precondition validation, and service entry.

## RowVersion Findings

RowVersion authority is backend-owned. `AdminStoreConfigResponse.RowVersion` is emitted from SQL `StoreConfig.RowVersion`, frontend only echoes the admitted `candidateConfig.rowVersion`, and `AdminStoreConfigService.ApplyExpectedRowVersion` sets EF `Property(x => x.RowVersion).OriginalValue` before persistence.

## Conflict Findings

Conflict resolution is backend-owned. Missing or malformed version evidence returns `428 PRECONDITION_REQUIRED`; stale version evidence and EF concurrency failures become `409 CONCURRENCY_CONFLICT`. Frontend shows failure presentation only and keeps the previous baseline plus dirty candidate.

## Baseline Replacement Findings

Baseline replacement begins only after an accepted backend mutation response passes `deriveCurrentStoreIdentityPresentation`. That admission requires current tenant evidence, nonblank store name, valid status, theme evidence, and row-version evidence. Malformed, foreign-tenant, failed, stale, timeout, or network-lost responses cannot replace `baselineConfig`.

## Failure Findings

400, 401, 403, 404, 409, 422, 428, 500, timeout, offline, malformed DTO, and rejected accepted-response evidence do not replace accepted configuration. The catch path displays error presentation only; it does not call `setBaselineConfig` or `setCandidateConfig` with candidate data.

## Concurrency Findings

Two admins, multiple tabs, browser retry, double click, replay, refresh, stale candidates, and stale RowVersion converge at backend row-version authority. `saveInFlightRef` suppresses local duplicate entry as ergonomics only; backend `If-Match`/RowVersion and EF concurrency are the duplicate/stale write defense.

## Recovery Findings

Refresh, browser restart, server restart, Redis loss, reconnect, and retry cannot recover configuration authority from frontend state. Store Administration recovery re-enters `GET /api/admin/store-config`; Redis/Mongo/search are not in the inspected mutation authority path.

## Cache Findings

Store Configuration mutation success cannot be fabricated by React state, React Query, LocalStorage, SessionStorage, browser cache, BFCache, or response tampering. The reviewed settings runtime has memory-only candidate state, no local/session storage draft persistence, no optimistic baseline patch, and no mutation-response admission without current backend evidence.

## Cross-System Findings

Accepted Store Configuration can appear to Store Administration, Storefront, Affiliate, CommerceHub, Checkout, Analytics, or projection consumers only after backend SQL persistence and backend response/projection/read admission. No inspected PointOfSale.Web Store Administration mutation code propagates candidates to those systems before backend acceptance.

## Mutation Pipeline

Accepted Baseline: backend `GET /api/admin/store-config` returns SQL-backed `AdminStoreConfigResponse` with tenant and RowVersion evidence.

Candidate: `StorefrontSettings` clones admitted baseline into memory-only `candidateConfig`.

Save Intent: admin clicks Save and `handleSave` starts one in-flight mutation.

Mutation DTO: `storeConfigService.updateStoreConfig` sends candidate fields plus `rowVersion` and `If-Match`.

Backend Validation: `AdminStoreConfigController.UpdateStoreConfig` validates tenant, role, DTO, and precondition.

If-Match: `RequireExpectedRowVersion` parses request `RowVersion` or `If-Match`.

RowVersion Validation: `AdminStoreConfigService.ApplyExpectedRowVersion` binds EF original row version.

SQL Persistence: `_dbContext.SaveChangesAsync` persists `StoreConfigs` or throws concurrency failure.

Accepted Response: backend maps current SQL entity to `AdminStoreConfigResponse` with current RowVersion.

DTO Admission: frontend runs `deriveCurrentStoreIdentityPresentation` on the accepted response.

Baseline Replacement: `setBaselineConfig(acceptedConfig)` and `setCandidateConfig(acceptedConfig)` run only after admission.

Presentation: Storefront Settings renders baseline-derived current status/store/theme and candidate controls.

## Authority Boundaries

Mutation Boundary: frontend save click creates intent; backend `UpdateStoreConfig` owns acceptance. Recovery source: current backend SQL. Failure behavior: reject or fail closed without baseline replacement.

Concurrency Boundary: backend precondition parsing plus EF RowVersion. Recovery source: current SQL reread. Failure behavior: 409/428 and frontend error presentation.

Version Boundary: backend `StoreConfig.RowVersion`, response RowVersion, `If-Match` parsing, EF original value. Recovery source: current backend GET. Failure behavior: stale or missing version cannot persist.

Persistence Boundary: `AdminStoreConfigService.PersistStoreConfigChangeAsync` and `_dbContext.SaveChangesAsync`. Recovery source: SQL and outbox/audit staging. Failure behavior: transaction failure prevents accepted response.

Recovery Boundary: browser/runtime recovery must reread backend config. Recovery source: SQL-backed admin config API. Failure behavior: clear/withhold settings presentation.

Presentation Boundary: `deriveCurrentStoreIdentityPresentation` admission and Storefront Settings render. Recovery source: accepted backend DTO only. Failure behavior: malformed evidence is withheld.

## Runtime Hardening Performed

No runtime code change was required for SF-ADM1.4. Existing runtime already satisfies the mutation authority model from the prior save-workflow hardening.

## Exact Runtime Files Modified

None.

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/StorefrontConfigurationAuthorityCertificationTests.cs` now records SF-ADM1.4 artifact/context coverage.

## Architecture Documentation Updates

- Added this certification findings document.

## AI Context Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Certification Decision

Store Configuration Mutation is production-certifiable for PointOfSale.Web Store Administration. Frontend submits mutation intent only; backend exclusively owns mutation acceptance, RowVersion, If-Match validation, persistence, conflict resolution, recovery, projection, and audit truth.
