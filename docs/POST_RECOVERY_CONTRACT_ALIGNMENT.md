# Post-Recovery Contract Alignment

**Repository:** PointOfSale  
**Date:** 2026-08-03  
**Scope:** Post-recovery contract drift only  
**Certification:** **PASS WITH OBSERVATIONS**

## Executive Summary

Two runtime contract drifts were repaired without reopening Runtime, Theme, Builder, Cache, Projection, StoreConfig, Homepage, Navigation, Footer, or Seed recoveries.

Navigation load failed because a persisted `StoreConfig` with no `navigation` object was mapped to default Navigation content while discarding the persisted PostgreSQL `xmin` RowVersion. The backend response therefore contained `rowVersion: "0"`, which the recovered frontend correctly rejected as unusable concurrency evidence.

CMS publish structured errors were already emitted by the backend, but frontend presentation read Axios' transport `error.code` before the backend `response.data.code`. A structured `409/CONCURRENCY_CONFLICT` was therefore bypassed as `ERR_BAD_REQUEST` and could surface as a generic Axios-derived failure.

The repairs are minimal:

- Preserve the existing StoreConfig RowVersion when default Navigation content is synthesized for an existing StoreConfig row.
- Reject `0` as a Navigation mutation precondition server-side.
- Prefer backend structured authoring error codes over Axios transport codes in the shared authoring feedback mapper.

## Verified Root Cause #1

`rowVersion: "0"` was produced by backend mapping, not by HTTP serialization or frontend validation.

Source trace:

- Persistence/entity authority is `StoreConfig.RowVersion`, documented as the optimistic concurrency token and mapped to PostgreSQL `xmin` (`PointOfSale.Core/Entities/StoreConfig.cs:117-123`; `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:1299-1305`).
- Navigation GET loads the tenant `StoreConfig` and passes `config.RowVersion` into `ExtractNavigationFromConfig` (`PointOfSale.API/Services/Admin/AdminCmsService.cs:293-299`).
- Before this alignment, `ExtractNavigationFromConfig` returned `GetDefaultNavigation()` when JSON was empty, malformed, or had no `navigation` property. `AdminNavigationResponse.RowVersion` defaults to `"0"` (`PointOfSale.API/DTOs/Admin/AdminCmsDtos.cs:173-179`).
- That means a persisted StoreConfig row with blank JSON or `{}` lost the usable persisted token during response mapping.
- Controller then serialized and emitted that DTO and ETag (`PointOfSale.API/Controllers/AdminCmsPagesController.cs:587-593`, `:892-895`).
- Frontend `admitNavigationSnapshot` requires a positive integer RowVersion and rejects `"0"` (`PointOfSale.Web/src/modules/storefront/cms/services/navigationAuthoringContract.js:107-115`). This validation was correct under Recovery 2.

`"0"` is intentional only as a placeholder/default DTO value for no usable backend concurrency token. It is not a valid StoreConfig `xmin` token and must not cross the persisted StoreConfig response path.

Recovered contract:

- `ExtractNavigationFromConfig` now passes the persisted RowVersion into `GetDefaultNavigation(rowVersion)` for missing/blank/malformed Navigation JSON (`PointOfSale.API/Services/Admin/AdminCmsService.cs:520-570`).
- `TryParseNavigationRowVersion` now rejects `0` instead of accepting it as a mutation precondition (`PointOfSale.API/Controllers/AdminCmsPagesController.cs:898-912`).
- Frontend validation remains strict and unchanged in policy: missing, zero, or malformed RowVersion is rejected.

## Verified Root Cause #2

Backend CMS publish already returned structured authoring errors. The bypass was in frontend error-code precedence.

Source trace:

- `PublishCmsPage` requires version evidence, calls `PublishCmsPageAsync`, maps stale versions through `BuildCmsPageConflictResponse`, and maps expected invalid-operation publication failures to `AdminCmsApiErrorResponse` (`PointOfSale.API/Controllers/AdminCmsPagesController.cs:287-341`).
- `BuildCmsPageConflictResponse` emits `409` with code `CONCURRENCY_CONFLICT`, message, trace ID, and optional `LatestVersion` (`PointOfSale.API/Controllers/AdminCmsPagesController.cs:786-815`).
- `cmsService.publishPage` does not replace the error; it lets the Axios rejection propagate with backend `response.data` intact (`PointOfSale.Web/src/modules/storefront/cms/services/cmsService.js:76-80`).
- `CmsPagesList.handlePublish` catches the error and passes it to `getAuthoringErrorFeedback`, then renders the returned feedback in the page error banner (`PointOfSale.Web/src/modules/storefront/cms/pages/CmsPagesList.jsx:101-107`).
- The drift was `getAuthoringErrorFeedback` choosing `error.code` before `error.response.data.code`. Axios sets `error.code` to values like `ERR_BAD_REQUEST`, so backend `CONCURRENCY_CONFLICT` was ignored (`PointOfSale.Web/src/shared/content/authoringErrorFeedback.js:1-4` before this alignment).

Recovered contract:

- `getAuthoringErrorFeedback` now prefers `error.response.data.code` over Axios transport `error.code` and filters Axios status messages from presentation (`PointOfSale.Web/src/shared/content/authoringErrorFeedback.js:1-18`).
- CMS publish conflict and validation failures now render backend message text through the shared structured authoring mapper, not generic Axios strings.

## Source-Backed Call Graph

### Navigation Load

```text
StoreConfigs table
  -> StoreConfig.RowVersion / PostgreSQL xmin
  -> AdminCmsService.GetNavigationAsync
  -> ExtractNavigationFromConfig(customizationJson, config.RowVersion)
  -> GetDefaultNavigation(rowVersion) when Navigation JSON is absent
  -> AdminNavigationResponse.RowVersion
  -> AdminCmsPagesController.GetNavigation
  -> ETag + X-Resource-Version + JSON body
  -> cmsService.getNavigation
  -> admitNavigationSnapshot
  -> NavigationEditor state adoption
```

### CMS Publish Error

```text
POST /api/admin/pages/{id}/publish
  -> AdminCmsPagesController.PublishCmsPage
  -> IAdminCmsService.PublishCmsPageAsync
  -> ConcurrencyConflictException or InvalidOperationException
  -> AdminCmsApiErrorResponse
  -> Axios error.response.data
  -> cmsService.publishPage rejection passthrough
  -> CmsPagesList.handlePublish catch
  -> getAuthoringErrorFeedback
  -> rendered error banner
```

## Files Changed

Production:

- `PointOfSale.API/Services/Admin/AdminCmsService.cs`
- `PointOfSale.API/Controllers/AdminCmsPagesController.cs`
- `PointOfSale.Web/src/shared/content/authoringErrorFeedback.js`

Regression tests:

- `PointOfSale.Tests/Admin/NavigationAdmissionServiceTests.cs`
- `PointOfSale.Tests/Cms/AdminCmsConflictControllerTests.cs`
- `PointOfSale.Web/src/modules/storefront/cms/services/navigationAuthoringContract.test.ts`
- `PointOfSale.Web/src/modules/storefront/cms/services/cmsService.test.ts`
- `PointOfSale.Web/src/modules/storefront/cms/pages/CmsPagesList.test.tsx`
- `PointOfSale.Web/src/shared/content/footerAuthoringContract.test.ts`

## Regression Tests

Added or extended coverage for:

- Navigation load with valid version.
- Navigation load with invalid version.
- Navigation load with missing version.
- Navigation response mapping from persisted StoreConfig RowVersion through default Navigation content.
- Navigation server-side rejection of zero RowVersion preconditions.
- Navigation server-side rejection of missing RowVersion preconditions.
- CMS publish conflict response mapping.
- CMS publish validation failure response mapping.
- CMS publish service propagation of structured backend errors.
- CMS publish structured error rendering in `CmsPagesList`.
- No generic `AxiosError` / `ERR_BAD_REQUEST` presentation for structured CMS publish failures.

## Validation

| Gate | Result |
|---|---|
| `npx vitest run src/modules/storefront/cms/services/navigationAuthoringContract.test.ts src/modules/storefront/cms/services/cmsService.test.ts src/modules/storefront/cms/pages/CmsPagesList.test.tsx src/shared/content/footerAuthoringContract.test.ts --reporter=dot` in `PointOfSale.Web` | **PASS: 26/26** |
| `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj -c Release --filter "FullyQualifiedName~NavigationAdmissionServiceTests\|FullyQualifiedName~AdminCmsConflictControllerTests" --no-restore` | **PASS: 25/25** |
| `npm run build` in `PointOfSale.Storefront` | **PASS**; existing Browserslist, mixed import, and chunk-size warnings remain |
| `npm run build` in `PointOfSale.Web` | **PASS** |
| `dotnet build PointOfSale.sln -c Release --no-restore --verbosity:minimal` | **PASS: 0 warnings, 0 errors** |
| Focused `git diff --check` for touched files | **PASS**; line-ending warnings only |

Observation: the first focused backend test attempt in Debug output failed before running tests because `PointOfSale.API.exe` was locked by an existing `PointOfSale.API` process. The same focused tests passed in Release output without stopping that process.

## Search Proof

Focused production-source searches returned:

```text
frontend_navigation_version_regex=1
  PointOfSale.Web/src/modules/storefront/cms/services/navigationAuthoringContract.js:14

frontend_navigation_snapshot_admission=1
  PointOfSale.Web/src/modules/storefront/cms/services/navigationAuthoringContract.js:107

frontend_navigation_dto_mapper=1
  PointOfSale.Web/src/modules/storefront/cms/services/navigationAuthoringContract.js:201

backend_navigation_precondition_parser=1
  PointOfSale.API/Controllers/AdminCmsPagesController.cs:898

frontend_publish_error_mapper=1
  PointOfSale.Web/src/shared/content/authoringErrorFeedback.js:1

cms_publish_service_transport=1
  PointOfSale.Web/src/modules/storefront/cms/services/cmsService.js:76
```

No duplicate response adoption or duplicate error mapper was added. Navigation responses are admitted in `cmsService` before `NavigationEditor` state adoption. CMS publish still uses one service transport and one shared structured authoring feedback mapper.

## Certification

**PASS WITH OBSERVATIONS**

The remaining two runtime contract failures are aligned:

- Navigation load now preserves usable backend concurrency evidence for persisted StoreConfig fallback responses and rejects invalid mutation evidence.
- CMS publish now presents backend structured authoring errors instead of Axios transport failures.

The observation is limited to the external Debug binary lock encountered during the first focused backend test attempt and existing non-blocking frontend build warnings. No frozen recovery architecture was reopened.
