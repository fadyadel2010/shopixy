# PASS SF-ADM2.3 Builder Section Composition Authority Certification

PointOfSale.Web Store Builder Section Composition is production-certifiable after backend section composition canonicalization. Backend trusted tenant/session authority, Owner/Admin authorization, SQL `StoreConfigs`, `AdminStoreConfigController.UpdatePageLayout`, `AdminStoreConfigService.UpdatePageLayoutAsync`, backend section canonicalization, allowed section-type validation, EF `StoreConfig.RowVersion`, mandatory `If-Match`, SQL persistence, and accepted `AdminStoreConfigResponse` own Section Identity, Section Collection, Section Order, Section Composition, Section Type, Section Persistence, Section Version, Section Acceptance, Section Recovery, and Section Audit.

Frontend Store Builder owns editing, selection, drag UI, undo/redo candidates, temporary section candidates, settings candidates, preview rendering, presentation, and navigation only.

## Pages Reviewed

- `PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx`
- `PointOfSale.Web/src/modules/storefront/store/builderStore.js`
- `PointOfSale.Web/src/modules/storefront/components/builder/BuilderCanvas.jsx`
- `PointOfSale.Web/src/modules/storefront/components/builder/SectionPalette.jsx`
- `PointOfSale.Web/src/modules/storefront/components/builder/SettingsPanel.jsx`
- `PointOfSale.Web/src/modules/storefront/components/builder/PreviewModal.jsx`
- `PointOfSale.Web/src/modules/storefront/builder/sectionRegistry.js`
- `PointOfSale.Web/src/modules/storefront/components/sections/*`
- `PointOfSale.Web/src/modules/storefront/services/storeConfigService.js`

## Backend Endpoints Reviewed

- `GET /api/admin/store-config`
- `PUT /api/admin/store-config/pages/{pageId}`
- `POST /api/admin/store-config/pages/{pageId}/publish` only as directly consumed after Builder save
- `DELETE /api/admin/store-config/product-templates/{templateId}` only as directly consumed by Builder template controls
- `GET /api/storefront/config/preview` only as authenticated Builder preview evidence
- `GET /api/storefront/store-config` only as Builder runtime presentation evidence

## Backend Services Reviewed

- `AdminStoreConfigService.GetStoreConfigAsync`
- `AdminStoreConfigService.UpdatePageLayoutAsync`
- `AdminStoreConfigService.NormalizeBuilderPageComposition`
- `AdminStoreConfigService.MapToResponseAsync`
- `AdminStoreConfigService.ApplyExpectedRowVersion`
- `AdminStoreConfigService.SaveDraftStoreConfigAsync`
- `StorefrontDraftConfigMapper.MapLive` only where directly consumed as Builder presentation evidence

## Findings

Section findings: Section begins as backend-admitted draft/live section evidence in SQL `StoreConfigs.CustomizationJson`. New or duplicated browser sections are temporary candidates until `UpdatePageLayoutAsync` canonicalizes them. Backend preserves only previously accepted section IDs for the page/template and mints server IDs for new or duplicate candidates.

Composition findings: Add, duplicate, delete, move, reorder, settings edits, and visibility edits are frontend candidate operations in `builderStore`. Composition Authority begins at backend page-layout mutation canonicalization, not in React or Zustand.

Order findings: Drag-and-drop may submit candidate order only. Backend rewrites `order` from the accepted array sequence before persistence, so frontend cannot own accepted section order.

Type findings: Section types are accepted only if backend allowlisted. Unknown, malformed, or injected section types fail the mutation with 422 and cannot become accepted sections.

Save findings: Save/autosave submit mutation intent through `persistPageLayout` with backend RowVersion and `If-Match`. Accepted Section begins only after backend validation, canonicalization, RowVersion persistence, accepted DTO emission, and frontend DTO admission.

Failure findings: 400, 401, 403, 404, 409, 422, 428, 500, malformed DTOs, missing sections, unsupported types, stale RowVersion, duplicate IDs, and replayed section IDs cannot become accepted composition. Failed saves keep candidates dirty/non-authoritative.

Recovery findings: Refresh, restart, reconnect, workspace reload, Redis loss, Mongo rebuild, BFCache, and stale tabs re-enter backend `GET /api/admin/store-config`. Frontend clears and hydrates sections through `admitSections` only from admitted backend DTOs.

Concurrency findings: Multiple tabs/admins and duplicate saves converge through backend RowVersion. A stale section order or composition replay can submit intent only; it cannot overwrite newer accepted sections after RowVersion changes.

Cache findings: React, Zustand, browser cache, LocalStorage, and SessionStorage never become Section Authority. Zustand has a separate `admitSections` path for backend DTOs; candidate `setSections` remains dirty/edit-only.

Cross-system findings: Candidate sections do not leak to Published Storefront, Preview, CMS, Theme Runtime, SEO, Analytics, Affiliate, Marketplace, CommerceHub, Checkout, Orders, or Media Library. Preview first saves through backend authority, then reads authenticated backend draft preview; live content is not substituted.

## Section Pipeline

Backend Sections -> Workspace Admission -> Editable Candidate Sections -> Canvas Editing -> Mutation DTO -> Backend Validation -> Backend Section Canonicalization -> RowVersion -> SQL Persistence -> Accepted Section DTO -> DTO Admission -> Canvas Rendering.

- Section begins in backend SQL `StoreConfigs.CustomizationJson` draft/live section evidence.
- Section Authority begins at backend page-layout mutation canonicalization inside `AdminStoreConfigService.UpdatePageLayoutAsync`.
- Editing begins after `StorefrontBuilderPage.admitWorkspaceConfig` admits backend workspace evidence and `admitSections` hydrates the canvas.
- Mutation begins when Save/Autosave calls `persistPageLayout` and submits candidate sections to `PUT /api/admin/store-config/pages/{pageId}`.
- Accepted Section begins after backend type validation, ID canonicalization, order canonicalization, RowVersion validation, SQL persistence, and accepted DTO emission.
- Presentation begins after frontend DTO admission sets `storeConfig` and hydrates canvas sections from the accepted DTO.

## Boundary Review

- Section Boundary: backend SQL/canonicalization owns identity and type; recovery source is admin Store Config read; failure rejects or mints backend IDs.
- Composition Boundary: backend page-layout mutation owns accepted collection; recovery source is SQL draft; failure keeps candidates non-authoritative.
- Order Boundary: backend sequence canonicalization owns accepted order; recovery source is accepted DTO; failure keeps prior backend order.
- Mutation Boundary: frontend submits intent; backend owns validation/acceptance; failure returns 4xx/5xx without clean state.
- Recovery Boundary: backend Store Config read owns recovery; browser/cache/state can only trigger refetch; failure withholds workspace.
- Presentation Boundary: React/Zustand render candidates or accepted DTOs; recovery source is backend DTO; failure shows loading/error.
- Concurrency Boundary: RowVersion/If-Match owns conflict resolution; recovery source is current SQL; stale saves fail 409/428.

## Runtime Hardening Performed

- Added backend section composition canonicalization before draft persistence.
- Backend now preserves only previously accepted section IDs, mints IDs for new/duplicate/foreign candidates, validates allowed section types, rewrites accepted order, and strips section authority to `{ id, type, order, enabled, settings }`.
- Malformed section composition now returns 422 instead of becoming accepted draft or generic server authority.
- Frontend Builder now hydrates bootstrap and accepted save responses through `admitSections`, so canvas state converges to backend-accepted section identity/order/collection.

## Exact Runtime Files Modified

- `PointOfSale.API/Services/Admin/AdminStoreConfigService.cs`
- `PointOfSale.API/Controllers/AdminStoreConfigController.cs`
- `PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx`
- `PointOfSale.Web/src/modules/storefront/store/builderStore.js`

## Regression Freezes Added

- `PointOfSale.Web/src/modules/storefront/builderWorkspaceAuthority.test.ts`
- `PointOfSale.Tests/Architecture/StoreBuilderDraftAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/StoreBuilderSectionCompositionAuthorityCertificationTests.cs`

## Architecture Documentation Updates

- Added this SF-ADM2.3 findings document.

## AI Context Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Explicit Certification Answers

- Did you review ONLY PointOfSale.Web runtime? Yes, limited to Store Builder Section Composition runtime and directly consumed Store Config backend APIs/services.
- Which frontend files were modified? `PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx`, `PointOfSale.Web/src/modules/storefront/store/builderStore.js`, `PointOfSale.Web/src/modules/storefront/builderWorkspaceAuthority.test.ts`.
- Which backend files were modified? `PointOfSale.API/Services/Admin/AdminStoreConfigService.cs`, `PointOfSale.API/Controllers/AdminStoreConfigController.cs`, `PointOfSale.Tests/Architecture/StoreBuilderDraftAuthorityCertificationTests.cs`, `PointOfSale.Tests/Architecture/StoreBuilderSectionCompositionAuthorityCertificationTests.cs`.
- Who owns Section Identity? Backend Store Config canonicalization and SQL persistence.
- Who owns Section Collection? Backend `UpdatePageLayoutAsync` and SQL `StoreConfigs.CustomizationJson`.
- Who owns Section Order? Backend order canonicalization from the accepted mutation sequence.
- Who owns Section Type? Backend allowlist validation.
- Can frontend create accepted Sections? No.
- Can React become Section Authority? No.
- Can Zustand become Section Authority? No.
- Can LocalStorage become Section Authority? No.
- Can frontend reorder accepted Sections? No; it submits candidate order only.
- Can stale Sections survive recovery? No accepted stale sections survive backend recovery.
- Can replay duplicate Section mutations? Replay can submit intent only; RowVersion and backend ID canonicalization prevent duplicate accepted authority.
- Can duplicate Save overwrite newer Sections? No; backend RowVersion/If-Match rejects stale duplicates.
- Where does Section begin? Backend SQL `StoreConfigs.CustomizationJson` section evidence.
- Where does Section Authority begin? Backend page-layout mutation canonicalization in `AdminStoreConfigService.UpdatePageLayoutAsync`.
- Where does Editing begin? After backend workspace DTO admission hydrates editable sections.
- Where does Mutation begin? `persistPageLayout` calling `PUT /api/admin/store-config/pages/{pageId}`.
- Where does Accepted Section begin? After backend validation/canonicalization, RowVersion persistence, and accepted DTO emission.
- Where does Presentation begin? After frontend DTO admission hydrates canvas rendering.
- Were runtime changes required? Yes.
- Is Builder Section Composition production-certifiable? Yes.
