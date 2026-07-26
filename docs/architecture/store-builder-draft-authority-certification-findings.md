# PASS SF-ADM2.2 Builder Draft Authority Certification

PointOfSale.Web Store Builder Draft Authority is production-certifiable with no runtime code change required. Backend trusted tenant/session authority, Owner/Admin authorization, SQL `StoreConfigs`, `AdminStoreConfigController`, `AdminStoreConfigService`, EF `StoreConfig.RowVersion`, mandatory `If-Match`/RowVersion preconditions, SQL persistence, accepted `AdminStoreConfigResponse`, audit/version history identity where exposed, and backend preview admission own Draft Identity, Draft Authority, Draft Version, Draft Persistence, Draft Concurrency, Draft Recovery, Draft Acceptance, Draft Audit, Draft History Identity, RowVersion, and If-Match.

Frontend Store Builder owns only editing, temporary candidate sections/settings, selection, undo UI, loading, presentation, navigation, autosave scheduling, and save/preview/publish button presentation.

## Pages Reviewed

- `PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx`
- `PointOfSale.Web/src/modules/storefront/store/builderStore.js`
- `PointOfSale.Web/src/modules/storefront/components/builder/BuilderTopBar.jsx`
- `PointOfSale.Web/src/modules/storefront/components/builder/BuilderCanvas.jsx`
- `PointOfSale.Web/src/modules/storefront/components/builder/SettingsPanel.jsx`
- `PointOfSale.Web/src/modules/storefront/components/builder/SectionPalette.jsx`
- `PointOfSale.Web/src/modules/storefront/components/builder/PreviewModal.jsx`
- `PointOfSale.Web/src/modules/storefront/components/builder/BuilderThemeProvider.jsx`
- `PointOfSale.Web/src/modules/storefront/services/storeConfigService.js`

## Backend Endpoints Reviewed

- `GET /api/admin/store-config`
- `PUT /api/admin/store-config/pages/{pageId}`
- `POST /api/admin/store-config/pages/{pageId}/publish` only as directly invoked from Builder after draft save
- `DELETE /api/admin/store-config/product-templates/{templateId}` only as directly invoked from Builder template controls
- `GET /api/admin/store-config/versions` only as directly invoked by Builder rollback selection
- `POST /api/admin/store-config/rollback/{version}` only as directly invoked by Builder rollback
- `GET /api/storefront/config/preview` only as authenticated Builder draft preview evidence
- `GET /api/storefront/store-config` only as runtime presentation evidence directly consumed by Builder bootstrap

## Backend Services Reviewed

- `AdminStoreConfigService.GetStoreConfigAsync`
- `AdminStoreConfigService.UpdatePageLayoutAsync`
- `AdminStoreConfigService.PublishPageAsync` only where directly invoked from Builder
- `AdminStoreConfigService.DeleteProductTemplateAsync` only where directly invoked from Builder
- `AdminStoreConfigService.RollbackStoreConfigAsync` only where directly invoked from Builder
- `AdminStoreConfigService.MapToResponseAsync`
- `AdminStoreConfigService.ApplyExpectedRowVersion`
- `AdminStoreConfigService.SaveDraftStoreConfigAsync`
- `StorefrontDraftConfigMapper.MapLive` only for directly consumed runtime config evidence

## Findings

Draft findings: Draft begins in backend SQL `StoreConfigs.CustomizationJson` draft nodes loaded through `GET /api/admin/store-config`. Frontend sections begin only as editable candidates after `admitWorkspaceConfig` admits backend tenant/store/theme/status/RowVersion evidence.

Editing findings: `builderStore` is memory-only Zustand state. Section add/reorder/toggle/duplicate/delete/settings changes mark local dirty state and populate autosave intent, but they do not create an accepted draft until backend mutation succeeds and the accepted DTO is re-admitted.

Autosave findings: Autosave is frontend scheduling only. `debouncedSave` calls `persistPageLayout`; `persistPageLayout` requires backend-admitted RowVersion, sends `If-Match`, and accepts no result unless `admitWorkspaceConfig(response)` succeeds.

Concurrency findings: Two admins, two tabs, another browser, replay, duplicate save, retry, stale RowVersion, and modified If-Match converge at backend precondition parsing plus EF RowVersion validation. Stale saves fail with 428/409 before SQL persistence and cannot overwrite newer backend truth.

Failure findings: 400, 401, 403, 404, 409, 422, 428, 500, timeout, offline, malformed DTO, failed preview, and failed admission keep candidates non-authoritative. Failed draft save does not call `setStoreConfig(acceptedConfig)` or `markClean()`.

Recovery findings: Refresh, browser restart, reconnect, server restart, Redis loss, Mongo projection rebuild, deep links, workspace reload, and BFCache return re-enter backend `GET /api/admin/store-config` and route/session authority. Runtime clears stale `storeConfig` and sections during backend re-entry; failed recovery withholds the editable workspace.

Cache findings: Builder Draft is not persisted in LocalStorage/sessionStorage. React/Zustand, browser cache/BFCache, route params, demo sections, product template fallbacks, and modified component state may only hold candidates or presentation and cannot replace backend RowVersion/If-Match admission.

Cross-system findings: Unsaved Builder candidates do not leak into Published Storefront, Theme Runtime, CMS, Navigation, SEO, Analytics, Checkout, Orders, CommerceHub, Marketplace, or Affiliate. Builder preview uses authenticated draft preview data only and explicitly does not fall back to live content.

## Draft Pipeline

Backend Draft -> Workspace Admission -> Editable Candidate -> Save Intent -> Mutation DTO -> Backend Validation -> If-Match -> RowVersion Validation -> Draft Persistence -> Accepted Draft DTO -> DTO Admission -> Workspace Refresh.

- Draft begins in backend SQL `StoreConfigs.CustomizationJson` draft state loaded by `AdminStoreConfigService.GetStoreConfigAsync`.
- Draft Authority begins at backend Store Config controller/service admission under trusted tenant/session and Owner/Admin authorization.
- Editing begins after `StorefrontBuilderPage` admits a backend `AdminStoreConfigResponse` and hydrates memory-only sections.
- Mutation begins when Save or Autosave calls `persistPageLayout` and submits candidate sections through `storeConfigService.updatePageLayout`.
- Accepted Draft begins only after backend RowVersion validation, SQL persistence, and `AdminStoreConfigResponse` emission.
- Presentation begins after frontend DTO admission sets `storeConfig` and renders Builder controls/canvas/preview.

## Boundary Review

- Draft Boundary: backend SQL `StoreConfigs.CustomizationJson` owns authority; recovery source is `GET /api/admin/store-config`; failure withholds or keeps candidates dirty.
- Editing Boundary: frontend memory owns candidate editing only; recovery source is admitted backend draft; failure discards or keeps non-authoritative candidates.
- Mutation Boundary: frontend submits save intent; backend `UpdatePageLayout` owns acceptance; failure returns 4xx/5xx or timeout without accepted draft replacement.
- Version Boundary: backend `StoreConfig.RowVersion`, `If-Match`, and EF original value own concurrency; recovery source is backend reread; failure is 428/409.
- Recovery Boundary: backend read/session/config authority owns recovery; browser/cache/storage can only trigger reread; failure withholds workspace.
- Presentation Boundary: React canvas/settings/preview render admitted backend evidence or candidates; recovery source is accepted DTO; failure shows loading/error/empty states.
- Concurrency Boundary: backend precondition and SQL persistence own conflict resolution; recovery source is current SQL; stale saves fail closed.

## Runtime Hardening Performed

No runtime code change was required. Existing SF-ADM2.1 hardening already withholds Builder workspace rendering until backend admission, clears stale workspace state during bootstrap/recovery, requires RowVersion/`If-Match` for draft mutations, and re-admits accepted mutation responses before local workspace state is refreshed.

## Exact Runtime Files Modified

None.

## Regression Freezes Added

- `PointOfSale.Web/src/modules/storefront/builderWorkspaceAuthority.test.ts`
- `PointOfSale.Tests/Architecture/StoreBuilderDraftAuthorityCertificationTests.cs`

## Architecture Documentation Updates

- Added this SF-ADM2.2 findings document.

## AI Context Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Explicit Certification Answers

- Did you review ONLY PointOfSale.Web runtime? Yes, limited to Store Builder Draft runtime and directly consumed backend Store Config/session/preview APIs.
- Which frontend files were modified? `PointOfSale.Web/src/modules/storefront/builderWorkspaceAuthority.test.ts` test only; no frontend runtime files.
- Which backend files were modified? `PointOfSale.Tests/Architecture/StoreBuilderDraftAuthorityCertificationTests.cs` test only; no backend runtime files.
- Who owns Draft Identity? Backend SQL `StoreConfigs` and Store Config services.
- Who owns Draft Authority? Backend Store Config controller/service authority under trusted tenant/session and Owner/Admin authorization.
- Who owns Draft Version? Backend `StoreConfig.RowVersion`, `If-Match`, and EF concurrency.
- Who owns Autosave? Frontend owns autosave scheduling; backend owns autosave acceptance.
- Can frontend create Draft? No accepted draft; it can create temporary candidates only.
- Can React become Draft Authority? No.
- Can Zustand become Draft Authority? No.
- Can LocalStorage become Draft Authority? No.
- Can Autosave bypass backend? No.
- Can stale Draft survive recovery? No accepted stale draft can survive; recovery re-enters backend authority or withholds.
- Can replay duplicate Draft mutations? Replay can resubmit intent only; backend RowVersion accepts once or rejects stale duplicates.
- Can duplicate Save overwrite newer Draft? No; backend RowVersion/If-Match rejects stale duplicates.
- Where does Draft begin? Backend SQL `StoreConfigs.CustomizationJson` draft state.
- Where does Draft Authority begin? `AdminStoreConfigController`/`AdminStoreConfigService` under trusted backend authority.
- Where does Editing begin? After admitted backend workspace DTO hydrates memory-only sections.
- Where does Mutation begin? `persistPageLayout` save/autosave intent calling `PUT /api/admin/store-config/pages/{pageId}`.
- Where does Accepted Draft begin? After backend RowVersion validation, SQL persistence, and accepted `AdminStoreConfigResponse` emission.
- Where does Presentation begin? After frontend DTO admission sets `storeConfig` and renders Builder controls/canvas/preview.
- Were runtime changes required? No.
- Is Builder Draft production-certifiable? Yes.
