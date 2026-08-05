# POST AUTHORING NAVIGATION RESILIENCY RECOVERY

## Executive Summary

Certification: **PASS WITH OBSERVATIONS**

Navigation Editor load no longer fails the entire authoring surface when one stored navigation link points at an invalid CMS page target.

The recovery is intentionally load-scoped:

- `NavigationAdmissionService` remains the single Navigation business admission owner.
- Save and publish remain strict and still fail on invalid targets.
- `StoreConfig` draft/live ownership is unchanged.
- CMS, Theme, Runtime, Builder, Projection, and Cache were not redesigned.
- Invalid links are preserved in the editor response with structured warning data so an admin can repair or remove the bad link.

## Verified Root Cause

Observed failure:

```json
422 NAVIGATION_CMS_PAGE_NOT_FOUND
"The selected CMS page has no active draft or publication."
```

Source proves that exact message is emitted only by `NavigationAdmissionService.ResolveCmsPage(...)` in draft admission mode after these earlier branches did **not** fire:

- no target / empty GUID,
- page group not found,
- tenant mismatch,
- all tenant rows soft-deleted.

The exact branch is:

- `PointOfSale.API/Services/Admin/NavigationAdmissionService.cs:573` resolves CMS page groups.
- `PointOfSale.API/Services/Admin/NavigationAdmissionService.cs:632-638` accepts draft mode only when a tenant row is not deleted and is either `IsDraft` or `IsPublished`.
- `PointOfSale.API/Services/Admin/NavigationAdmissionService.cs:640-648` emits `NAVIGATION_CMS_PAGE_NOT_FOUND` with "has no active draft or publication" when no such row exists.

Therefore target `eac6ebf6-1418-4074-af02-2b82d8826831` is considered missing by Navigation admission because its CMS page group has no same-tenant, non-deleted row that is an active draft or publication. This is not a Marketplace lookup, projection lookup, theme fallback, or Storefront runtime issue; Navigation admission reads SQL `CmsPages` directly in `NavigationAdmissionService.AdmitAsync(...)` (`NavigationAdmissionService.cs:105-109`).

## Call Graph

Navigation Editor load:

```text
NavigationEditor.loadNavigation
-> cmsService.getNavigation
-> GET /admin/cms/navigation
-> AdminCmsPagesController.GetNavigation
-> AdminCmsService.GetNavigationAsync
-> StoreConfigs by tenant
-> NavigationStorageAdapter.FromStorage(draft/live)
-> AdmitNavigationForLoadAsync
-> NavigationAdmissionService.AdmitAsync
-> if whole snapshot fails, replay each link through NavigationAdmissionService.AdmitAsync
-> return AdminNavigationResponse with draft/live + validationWarnings + brokenLinks
-> navigationAuthoringContract.admitNavigationSnapshot
-> NavigationEditor renders editable links and highlights invalid rows
```

Save remains strict:

```text
NavigationEditor.handleSave
-> cmsService.updateNavigation
-> PUT /admin/cms/navigation
-> AdminCmsService.UpdateNavigationAsync
-> NavigationAdmissionService.AdmitAsync(Draft)
-> NavigationStorageAdapter.ToStorage
-> StoreConfig.CustomizationJson.navigation.draft
```

Publish remains strict:

```text
NavigationEditor.handlePublish
-> cmsService.publishNavigation
-> POST /admin/cms/navigation/publish
-> AdminCmsService.PublishNavigationAsync
-> NavigationStorageAdapter.FromStorage(draft)
-> NavigationAdmissionService.AdmitAsync(Publish)
-> StoreConfig.CustomizationJson.navigation.live = admitted draft
```

## Ownership Graph

- Navigation business validation owner: `NavigationAdmissionService.AdmitAsync(...)` (`NavigationAdmissionService.cs:39`).
- CMS target resolution owner: `NavigationAdmissionService.ResolveCmsPage(...)` (`NavigationAdmissionService.cs:573`).
- Navigation persistence owner: `AdminCmsService.UpdateNavigationAsync(...)` and `PublishNavigationAsync(...)` with `NavigationStorageAdapter` (`AdminCmsService.cs:552`, `:627`; `NavigationStorageAdapter.cs:12`, `:35`).
- Navigation editor response owner: `AdminCmsService.GetNavigationAsync(...)` (`AdminCmsService.cs:283`).
- Frontend DTO admission owner: `navigationAuthoringContract.admitNavigationSnapshot(...)` (`navigationAuthoringContract.js:160`).
- Editor invalid-link renderer: `NavigationEditor` warning banner and row highlighting (`NavigationEditor.jsx:434`, `:607`, `:686`).

No duplicate Navigation owner was introduced. Load resilience still delegates every valid/invalid link decision to `NavigationAdmissionService.AdmitAsync(...)` (`AdminCmsService.cs:355`, `:437`).

## Files Changed

- `PointOfSale.API/DTOs/Admin/AdminCmsDtos.cs`
  - Added transient link validation fields.
  - Added `validationWarnings` and `brokenLinks` response collections.
- `PointOfSale.API/Services/Admin/AdminCmsService.cs`
  - Made GET navigation tolerant by replaying failed snapshots link-by-link through the canonical admission service.
  - Preserves invalid links and annotates them.
  - Save/publish remain strict.
- `PointOfSale.Web/src/modules/storefront/cms/services/navigationAuthoringContract.js`
  - Admits warning collections and transient invalid-link fields.
  - Request mapper still omits validation annotations.
- `PointOfSale.Web/src/modules/storefront/cms/pages/NavigationEditor.jsx`
  - Shows non-blocking warning banner.
  - Highlights only invalid rows.
  - Clears transient invalid marker when a row is edited.
- `PointOfSale.Tests/Admin/NavigationAdmissionServiceTests.cs`
  - Added broken/mixed/repair/publish and CMS state regression coverage.
- `PointOfSale.Web/src/modules/storefront/cms/services/navigationAuthoringContract.test.ts`
  - Added warning admission and repairable broken CMS link coverage.

## Regression Tests

Added coverage proves:

- broken CMS target loads without failing the editor response,
- deleted CMS page is marked invalid,
- inactive/non-draft/non-published page is marked invalid,
- draft-only page remains valid for draft load,
- draft-only page is invalid for live/publish-mode admission,
- mixed valid and invalid links preserve the valid links,
- invalid link remains visible with target identity and validation message,
- repair save succeeds after removing/replacing the broken link,
- publish succeeds after repair,
- frontend accepts structured warnings,
- frontend request mapper does not submit validation annotations.

Primary tests:

- `NavigationAdmissionServiceTests.GetNavigationAsync_BrokenCmsTarget_LoadsMixedNavigationAndRepairCanPublish`
- `NavigationAdmissionServiceTests.GetNavigationAsync_CmsTargetStates_MarksOnlyInvalidLinksAndKeepsDraftOnlyRepairable`
- `navigationAuthoringContract admits load-time validation warnings and keeps broken CMS links repairable`

## Validation

Passed:

- Focused Navigation tests:
  - `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter FullyQualifiedName~NavigationAdmissionServiceTests --no-restore --verbosity:minimal`
  - 21 passed.
- Focused authoring contract tests:
  - `npm test -- --run src/modules/storefront/cms/services/navigationAuthoringContract.test.ts`
  - 11 passed.
- Focused CMS/authoring frontend tests:
  - `npm test -- --run src/modules/storefront/cms/services/cmsService.test.ts src/modules/storefront/cms/pages/CmsPagesList.test.tsx src/modules/storefront/cms/pages/CmsPageEditor.test.tsx`
  - 16 passed.
- Storefront navigation runtime tests:
  - `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter "FullyQualifiedName~StorefrontNavigationRuntimeTests" --no-restore --verbosity:minimal`
  - 1 passed.
- Focused Storefront theme/runtime tests:
  - `npm test -- --run src/storefront/runtime/navigationRuntimeAlignment.test.tsx src/storefront/runtime/runtimeOwnershipRecovery.test.ts src/storefront/themes/themeRuntimeArchitecture.test.ts src/storefront/themes/ThemeRegistry.test.ts src/storefront/themes/alizon/alizonLayout.test.tsx src/storefront/themes/hype/hypeLayout.test.tsx src/storefront/themes/pearl/pearlLayout.test.tsx src/storefront/themes/phoenix/phoenixLayout.test.tsx src/storefront/themes/sharp/sharpLayout.test.tsx`
  - 117 passed.
- Web build:
  - `npm run build` in `PointOfSale.Web`
  - passed.
- Storefront build:
  - `npm run build` in `PointOfSale.Storefront`
  - passed with existing Vite/Browserslist/chunk warnings.
- .NET build:
  - `dotnet build PointOfSale.sln --no-restore --verbosity:minimal`
  - passed.
- Search proof:
  - `rg`/`Select-String` confirmed the exact CMS failure branch, strict save/publish admission calls, new load-warning path, and frontend warning admission/rendering.
- Whitespace:
  - `git diff --check`
  - passed; Git emitted existing LF-to-CRLF warnings only.

Observation:

- Broader CMS-focused .NET run with `CmsPageWorkflowTests|CmsPublicationReliabilityTests|AdminCmsConflictControllerTests|FooterAuthoringRecoveryTests|StorefrontNavigationRuntimeTests` failed 10 `CmsPageWorkflowTests` cases before Navigation is involved, all at `CmsPageService.CreatePageAsync` with `Created CMS page could not be loaded.` The focused Navigation recovery tests, authoring frontend tests, Storefront runtime tests, and builds pass.

## Certification

**PASS WITH OBSERVATIONS**

The Navigation Editor is now resilient to invalid CMS navigation targets on load. The invalid link is returned as structured data, marked on that one link, and kept editable so admins can repair or remove it. Save and publish still enforce the canonical Navigation admission contract and cannot silently promote invalid navigation.
