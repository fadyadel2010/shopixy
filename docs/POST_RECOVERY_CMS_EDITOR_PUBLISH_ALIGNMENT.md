# Post-Recovery CMS Editor Publish Alignment

**Repository:** PointOfSale  
**Date:** 2026-08-03  
**Scope:** CMS Page Editor publish contract drift only  
**Certification:** **PASS WITH OBSERVATIONS**

## Executive Summary

The remaining CMS Page Editor publish drift was frontend-only. The backend already returned the authoritative conflict snapshot through `AdminCmsApiErrorResponse.LatestVersion` on `409/CONCURRENCY_CONFLICT`.

The editor failed to adopt that snapshot because the shared CMS conflict extractor only read `response.data.latestVersion`. The verified runtime payload supplied `data.LatestVersion`, so the authoritative backend snapshot stopped at `extractCmsConflict` and never reached `CmsPageEditor` state adoption.

The surgical fix keeps optimistic concurrency intact and does not retry or ignore `409` responses:

- `extractCmsConflict` now normalizes both `latestVersion` and `LatestVersion` into one frontend `latestVersion` field.
- `CmsPageEditor.handlePublish` adopts supplied publish-conflict snapshots through the existing `adoptAuthoritativePage` detail adoption function.
- `CmsPageEditor` disables Publish once the adopted authoritative state has no draft left to publish.

No backend source was changed.

## Verified Root Cause

Backend publish conflict authority was already present:

- `AdminCmsPagesController.PublishCmsPage` requires current version evidence and maps `ConcurrencyConflictException` to `BuildCmsPageConflictResponse` (`PointOfSale.API/Controllers/AdminCmsPagesController.cs:301-330`).
- `BuildCmsPageConflictResponse` reloads the latest CMS page and assigns it to `LatestVersion` on the structured error response (`PointOfSale.API/Controllers/AdminCmsPagesController.cs:786-815`).
- `cmsService.publishPage` sends `If-Match` and returns or propagates the Axios response without replacing structured backend errors (`PointOfSale.Web/src/modules/storefront/cms/services/cmsService.js:78-82`).

Frontend drift:

- Before this fix, `extractCmsConflict` only read `response.data.latestVersion`; the verified runtime response supplied `data.LatestVersion`.
- `CmsPageEditor.handlePublish` depended on `extractCmsConflict(err)` before it could set conflict state or adopt a latest snapshot (`PointOfSale.Web/src/modules/storefront/cms/pages/CmsPageEditor.jsx:234-251`).
- Because `LatestVersion` was not normalized, `conflict.latestVersion` was `null`, so the editor stayed on the stale `page` and form state. The existing `adoptAuthoritativePage` function was never reached.
- Publish button state also remained too permissive after an adopted publish response because the button was disabled only while saving or publishing, not when the authoritative state had no draft.

## Source-Backed Call Graph

```text
CmsPageEditor Publish button
  -> CmsPageEditor.handlePublish
  -> save dirty visible form first through handleSave when needed
  -> cmsService.publishPage(targetPage.id, targetPage.draft.version)
  -> Axios POST /admin/pages/{id}/publish with If-Match
  -> AdminCmsPagesController.PublishCmsPage
  -> IAdminCmsService.PublishCmsPageAsync
  -> success: AdminCmsPageResponse
  -> editor adoptAuthoritativePage(response)
  -> page/form/acceptedFingerprint updated
  -> dirty state clean
  -> canPublish recalculated from adopted page.draft
```

```text
Publish conflict
  -> backend 409 CONCURRENCY_CONFLICT
  -> AdminCmsApiErrorResponse.LatestVersion
  -> Axios error.response.data
  -> extractCmsConflict
  -> normalized latestVersion from latestVersion or LatestVersion
  -> CmsPageEditor.handlePublish catch
  -> adoptConflictLatestVersion
  -> adoptAuthoritativePage
  -> page/form/acceptedFingerprint updated
  -> dirty state clean
  -> Publish disabled when LatestVersion has no draft
```

## Why CmsPageEditor Differed From CmsPagesList

`CmsPagesList` uses a separate list-row publish path:

- `CmsPagesList.handlePublish` calls `cmsService.publishPage(page.id, page.draftVersion)` (`PointOfSale.Web/src/modules/storefront/cms/pages/CmsPagesList.jsx:88-101`).
- On success it does not adopt the publish response directly; it calls `loadPages()` and re-admits the list response (`PointOfSale.Web/src/modules/storefront/cms/pages/CmsPagesList.jsx:101-103`).
- On conflict it only renders structured feedback through `getAuthoringErrorFeedback` (`PointOfSale.Web/src/modules/storefront/cms/pages/CmsPagesList.jsx:104-107`). It does not hold or repair the editor detail snapshot.

`CmsPageEditor` holds the detail page object, form draft, accepted fingerprint, dirty state, and publish button state. It therefore must adopt the authoritative detail response itself. The stale editor state was caused by the supplied `LatestVersion` being discarded before it reached the editor's detail adoption function.

## Implementation

Changed production source:

- `PointOfSale.Web/src/modules/storefront/cms/services/cmsService.js`
- `PointOfSale.Web/src/modules/storefront/cms/pages/CmsPageEditor.jsx`

Details:

- `extractCmsConflict` now reads `response.data?.latestVersion ?? response.data?.LatestVersion ?? null` (`cmsService.js:21`).
- `CmsPageEditor.handlePublish` clears stale feedback before publish, preserves the strict `If-Match` publish call, and adopts a supplied conflict latest snapshot without retrying publish (`CmsPageEditor.jsx:222-254`).
- `adoptConflictLatestVersion` is the single LatestVersion adoption path and delegates to the existing `adoptAuthoritativePage` detail response owner (`CmsPageEditor.jsx:284-305`).
- `canPublish` now reflects backend-adopted state: saving/publishing must be idle, and either the form is dirty enough to save into a draft or the authoritative page currently has a draft (`CmsPageEditor.jsx:104-106`, `:558-560`).

## Regression Tests

Changed focused frontend tests:

- `PointOfSale.Web/src/modules/storefront/cms/services/cmsService.test.ts`
- `PointOfSale.Web/src/modules/storefront/cms/pages/CmsPageEditor.test.tsx`

Coverage added or retained:

- Publish success sends backend version evidence (`cmsService.test.ts:105-112`).
- Structured publish errors are not replaced by generic service errors (`cmsService.test.ts:115-130`).
- Exact `409/CONCURRENCY_CONFLICT` classification remains strict (`cmsService.test.ts:146-152`).
- Conflict `LatestVersion` casing is normalized (`cmsService.test.ts:155-170`).
- Dirty editor publish saves first and publishes with the saved response version (`CmsPageEditor.test.tsx:84-97`).
- Repeated publish is blocked after successful publish response adoption removes the draft (`CmsPageEditor.test.tsx:100-112`).
- Publish conflict with `LatestVersion` adopts the authoritative backend snapshot, clears dirty state, disables Publish, performs no detail refresh, and uses version `2` for the next save (`CmsPageEditor.test.tsx:115-144`).

## Validation

| Gate | Result |
|---|---|
| `npx vitest run src/modules/storefront/cms/pages/CmsPageEditor.test.tsx src/modules/storefront/cms/services/cmsService.test.ts src/modules/storefront/cms/pages/CmsPagesList.test.tsx --reporter=dot` in `PointOfSale.Web` | **PASS: 3 files, 16/16 tests** |
| `npx vitest run src/modules/storefront/cms --reporter=dot` in `PointOfSale.Web` | **PASS: 5 files, 33/33 tests** |
| `npm run build` in `PointOfSale.Storefront` | **PASS**; existing Browserslist, mixed import, and chunk-size warnings remain |
| `npm run build` in `PointOfSale.Web` | **PASS** |
| `dotnet build PointOfSale.sln -c Release --no-restore --verbosity:minimal` | **PASS: 0 warnings, 0 errors** |
| `git diff --check` for touched files | **PASS**; line-ending conversion notices only |

Observation: the focused publish-conflict component test intentionally logs the handled `409` through the component's existing `console.error` path. The test still passes and asserts adoption of the supplied backend snapshot.

## Search Proof

Focused production-source searches returned:

```text
cms_editor_publish_handler=1
  PointOfSale.Web/src/modules/storefront/cms/pages/CmsPageEditor.jsx:208

cms_editor_publish_transport_call=1
  PointOfSale.Web/src/modules/storefront/cms/pages/CmsPageEditor.jsx:231

cms_publish_service_transport=1
  PointOfSale.Web/src/modules/storefront/cms/services/cmsService.js:78

cms_conflict_extractor=1
  PointOfSale.Web/src/modules/storefront/cms/services/cmsService.js:15

cms_latestversion_normalizer=1
  PointOfSale.Web/src/modules/storefront/cms/services/cmsService.js:21

cms_editor_authoritative_detail_adoption_owner=1
  PointOfSale.Web/src/modules/storefront/cms/pages/CmsPageEditor.jsx:284

cms_editor_latestversion_adoption_path=1
  PointOfSale.Web/src/modules/storefront/cms/pages/CmsPageEditor.jsx:300

cms_editor_publish_success_adoption=1
  PointOfSale.Web/src/modules/storefront/cms/pages/CmsPageEditor.jsx:232
```

List comparison proof:

```text
cms_list_publish_handler=1
  PointOfSale.Web/src/modules/storefront/cms/pages/CmsPagesList.jsx:88

cms_list_success_refresh=1
  PointOfSale.Web/src/modules/storefront/cms/pages/CmsPagesList.jsx:102
```

There are two UI publish initiators for different surfaces: editor detail and list row. They share one CMS publish transport. The editor has one detail response-adoption owner and one LatestVersion adoption path; no second editor adoption implementation was added.

## Certification

**PASS WITH OBSERVATIONS**

The CMS Page Editor publish contract drift is repaired within the requested surgical scope:

- backend concurrency remains authoritative;
- `409/CONCURRENCY_CONFLICT` remains strict and is not ignored;
- publish is not retried automatically;
- supplied `LatestVersion` is adopted into editor state;
- stale editor version evidence is replaced;
- dirty state becomes clean after authoritative adoption;
- Publish button state reflects the adopted backend draft/published state.

No frozen backend, Runtime, Theme, Builder, Cache, Projection, StoreConfig, Homepage, Navigation, Footer, Seed, or Marketplace CMS subsystem was reopened.
