# PASS SF-ADM2.4 Builder Layout Authority Certification

Date: 2026-06-30

Runtime reviewed: PointOfSale.Web Store Builder Layout runtime only.

## Scope Reviewed

- Pages reviewed: `PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx`.
- Frontend runtime reviewed: builder canvas, section palette/settings, builder store, section registry, page-layout service, and workspace authority tests under `PointOfSale.Web/src/modules/storefront`.
- Backend endpoints reviewed: `GET /api/admin/store-config`, `PUT /api/admin/store-config/pages/{pageId}`, product-template page layout branch, authenticated preview/publish handoff only where they consume accepted builder layout.
- Backend services reviewed: `AdminStoreConfigController`, `AdminStoreConfigService.UpdatePageLayoutAsync`, `AdminStoreConfigService.MapToResponseAsync`, `StorefrontDraftConfigMapper`, `StorefrontPreviewService`.
- Out of scope by instruction: PointOfSale.Storefront, CommerceHub, Marketplace, Affiliate, publishing, theme editing, version history, checkout, orders, and media library except for cross-system non-leakage reasoning from accepted DTO boundaries.

## Findings

- Layout Identity is backend-owned. The reviewed runtime has no independent frontend row/container/grid identity model; accepted layout identity is the backend page/template draft branch in SQL `StoreConfigs.CustomizationJson`, backend section IDs, backend canonical section order, and backend-normalized layout settings.
- Grid Authority is backend-owned. Frontend may submit candidate `columns`, `rows`, `layout`, `alignment`, `fullWidth`, and `maxWidth`, but backend `NormalizeBuilderPageComposition` now validates the section type, rewrites section order, preserves only previously accepted section IDs, mints new section IDs, allowlists setting keys by section type, clamps integer grid settings, enumerates layout/alignment settings, and strips injected grid/container/column/responsive authority fields.
- Containers and columns are backend-owned. No accepted container/column IDs are admitted from React or Zustand; injected `containerId`, `containers`, `gridId`, `columnId`, `columnStructure`, `responsiveRules`, `breakpoints`, or `visibilityRules` are removed before SQL persistence.
- Failed layout saves cannot become accepted. `AdminStoreConfigController.UpdatePageLayout` requires trusted tenant/session authority, Owner/Admin authorization, and `If-Match`/RowVersion evidence; malformed composition/layout candidates return failure before SQL persistence.
- Recovery converges to backend layout. Refresh, browser restart, another browser, reconnect, Redis loss, Mongo rebuild, and workspace reload call backend Store Config authority and hydrate canvas state only from accepted DTO admission.
- Cache cannot become authority. React state, Zustand state, undo stacks, browser cache/BFCache, LocalStorage, SessionStorage, stale DTOs, and demo sections are candidate/presentation only and are cleared or overwritten by backend admission.
- Concurrency is backend-owned. Duplicate saves, replay, another admin, and stale tabs must present RowVersion/`If-Match`; EF RowVersion conflict prevents stale layout overwrite.
- Cross-system leakage is blocked at this boundary. Candidate layout remains in builder memory until backend acceptance; preview/presentation consume accepted draft DTOs after backend admission.

## Layout Pipeline

Backend Layout begins in SQL `StoreConfigs.CustomizationJson`.

Layout Authority begins at backend page-layout mutation validation in `AdminStoreConfigService.UpdatePageLayoutAsync`: tenant/session authorization, RowVersion admission, section/type/order canonicalization, backend setting allowlist, layout setting normalization, SQL persistence, and accepted DTO emission.

Editing begins after `StorefrontBuilderPage.admitWorkspaceConfig` admits current backend config and `admitSections` hydrates the canvas.

Mutation begins only when Save/Autosave calls `persistPageLayout` and `storeConfigService.updatePageLayout` with backend RowVersion evidence.

Accepted Layout begins only after backend validation/canonicalization, EF RowVersion persistence, and accepted `AdminStoreConfigResponse` emission.

Presentation begins only after frontend DTO admission hydrates canvas sections from accepted backend response.

## Boundary Review

- Layout Boundary: backend `UpdatePageLayoutAsync`; recovery source is SQL Store Config; failures reject/withhold.
- Container Boundary: backend setting sanitizer; recovery source is accepted DTO; injected container fields are stripped.
- Grid Boundary: backend setting normalizer; recovery source is accepted DTO; malformed values clamp or fallback.
- Responsive Boundary: backend reserved-key strip; recovery source is accepted DTO; frontend responsive payloads are not accepted authority.
- Mutation Boundary: `PUT /api/admin/store-config/pages/{pageId}` with `If-Match`; stale/missing evidence fails closed.
- Recovery Boundary: `GET /api/admin/store-config`; stale local state is cleared and backend DTO re-admitted.
- Presentation Boundary: `admitSections`; React/Zustand render only admitted sections.
- Concurrency Boundary: EF `StoreConfig.RowVersion`; stale/duplicate/replayed saves cannot overwrite newer accepted layout.

## Runtime Hardening Performed

- `AdminStoreConfigService` now allowlists section setting keys by backend section type.
- Backend layout settings are canonicalized: integer grid settings are clamped, layout/alignment/card-style values are enumerated, and unknown layout/container/grid/column/responsive authority keys are stripped recursively.
- Existing section composition canonicalization remains: accepted IDs/order/type originate from backend mutation acceptance.

## Files Modified

- `PointOfSale.API/Services/Admin/AdminStoreConfigService.cs`
- `PointOfSale.Tests/Architecture/StoreBuilderLayoutAuthorityCertificationTests.cs`
- `docs/architecture/store-builder-layout-authority-certification-findings.md`
- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Explicit Certification Answers

- Did you review ONLY PointOfSale.Web runtime? Yes.
- Which frontend files were modified? None.
- Which backend files were modified? `PointOfSale.API/Services/Admin/AdminStoreConfigService.cs`.
- Who owns Layout Identity? Backend SQL Store Config plus backend canonicalization.
- Who owns Grid? Backend.
- Who owns Containers? Backend.
- Who owns Columns? Backend.
- Who owns Responsive Layout? Backend.
- Can frontend create accepted Layout? No.
- Can React become Layout Authority? No.
- Can Zustand become Layout Authority? No.
- Can LocalStorage become Layout Authority? No.
- Can frontend reorder accepted Layout? No; backend rewrites accepted order.
- Can stale Layout survive recovery? No; recovery re-enters backend Store Config authority.
- Can replay duplicate Layout mutations? Replay can be attempted, but stale RowVersion/`If-Match` fails and cannot overwrite newer layout.
- Can duplicate Save overwrite newer Layout? No; EF RowVersion owns concurrency.
- Where does Layout begin? SQL `StoreConfigs.CustomizationJson`.
- Where does Layout Authority begin? Backend page-layout mutation validation/canonicalization.
- Where does Editing begin? After backend workspace admission and canvas hydration.
- Where does Mutation begin? `persistPageLayout`/`updatePageLayout` save intent.
- Where does Accepted Layout begin? Backend SQL persistence plus accepted DTO emission.
- Where does Presentation begin? Frontend DTO admission into canvas rendering.
- Were runtime changes required? Yes, backend layout setting canonicalization was added.
- Is Builder Layout production-certifiable? Yes.
