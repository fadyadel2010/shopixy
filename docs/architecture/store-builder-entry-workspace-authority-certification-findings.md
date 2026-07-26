# PASS SF-ADM2.1 Store Builder Entry & Workspace Authority Certification

PointOfSale.Web Store Builder entry and workspace initialization is production-certifiable after workspace admission hardening. Backend POS session authority, tenant context, Owner/Admin authorization, backend-issued `commercehub.storefront.manage` navigation capability, SQL `StoreConfigs`, `AdminStoreConfigController`, `AdminStoreConfigService`, RowVersion evidence, and accepted backend DTO admission own Builder Authority, Workspace Admission, Current Store, Current Draft, Current Published Version, Current Theme, Builder Permissions, Feature Availability, Recovery, Persistence, and Audit.

Frontend Store Builder owns route presentation, loading/error states, navigation, editable draft candidates after backend admission, canvas rendering, temporary UI state, and save/preview/publish button presentation only.

## Pages Reviewed

- `PointOfSale.Web/src/App.jsx`
- `PointOfSale.Web/src/components/Sidebar.jsx`
- `PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx`
- `PointOfSale.Web/src/modules/storefront/store/builderStore.js`
- `PointOfSale.Web/src/modules/storefront/components/builder/BuilderTopBar.jsx`
- `PointOfSale.Web/src/modules/storefront/components/builder/BuilderCanvas.jsx`
- `PointOfSale.Web/src/modules/storefront/components/builder/SectionPalette.jsx`
- `PointOfSale.Web/src/modules/storefront/components/builder/SettingsPanel.jsx`
- `PointOfSale.Web/src/modules/storefront/components/builder/PreviewModal.jsx`
- `PointOfSale.Web/src/modules/storefront/services/storeConfigService.js`

## Backend Endpoints Reviewed

- `GET /api/auth/session`
- `GET /api/admin/store-config`
- `PUT /api/admin/store-config/pages/{pageId}`
- `GET /api/storefront/store-config` only as runtime presentation evidence directly consumed by builder bootstrap
- `GET /api/storefront/config/preview` only as authenticated draft preview evidence directly consumed by builder preview

## Backend Services Reviewed

- `AuthController.BuildCommerceHubNavigationCapabilitiesAsync`
- `PosSessionAuthority`
- `AdminStoreConfigController`
- `AdminStoreConfigService.GetStoreConfigAsync`
- `AdminStoreConfigService.UpdatePageLayoutAsync`
- `AdminStoreConfigService.MapToResponseAsync`
- `AdminStoreConfigService.ApplyExpectedRowVersion`
- `StorefrontDraftConfigMapper.MapLive` only for directly consumed runtime config evidence

## Findings

Builder findings: Builder Entry begins at `/storefront/builder` route presentation, but Builder Authority begins only after backend POS session/capability/lifecycle admission through `CommerceHubVendorRouteGuard` and protected Store Config APIs.

Workspace findings: Workspace Bootstrap begins only when `StorefrontBuilderPage` receives backend `AdminStoreConfigResponse` and admits it with `deriveCurrentStoreIdentityPresentation`. Runtime now withholds the editable palette/canvas/settings workspace while `storeConfig` is absent, loading, failed, malformed, or lacks RowVersion evidence.

Bootstrap findings: Current Store, Current Theme, Current Draft, Current Published Version, and product template evidence come from backend `GET /api/admin/store-config`; public runtime config may hydrate runtime-only presentation fields but cannot admit the workspace alone.

Permission findings: Builder Permissions are backend-owned by POS session role, `commercehub.storefront.manage`, lifecycle blockers, `[Authorize(Roles = "Owner,Admin")]`, and Store Config endpoint authorization. Sidebar, route params, React state, and hidden buttons are not permission authority.

Recovery findings: Refresh, browser restart, another device, reconnect, server restart, Redis loss, Mongo rebuild, session expiry, bookmark, deep link, and workspace reload re-enter backend route/session/config authority. Runtime clears stale workspace state during bootstrap re-entry and withholds rendering until a fresh backend-admitted config exists.

Failure findings: 400, 401, 403, 404, 409, 428, 500, expired session, malformed DTO, missing store, missing draft evidence, missing RowVersion, and failed bootstrap fail closed by showing loading/error presentation and no editable workspace.

Concurrency findings: Multiple tabs, browsers, devices, or another admin cannot make stale React workspace state authoritative. Draft mutations require current backend RowVersion evidence through `If-Match`, and accepted mutation responses are re-admitted before the local workspace baseline is refreshed.

Cache findings: Builder state is Zustand memory only and is not persisted in LocalStorage/sessionStorage. Browser cache/BFCache, React state, DTO replay, and modified builder context can render only after current backend admission or are withheld.

Cross-system findings: Store Administration, Store Builder, Published Storefront, CMS, Theme Runtime, and Analytics converge on backend Store Config/current read/projection authority. Unsaved builder candidates do not become published Storefront, CMS, theme, analytics, or cross-system authority.

## Builder Pipeline

Builder Route -> Backend Admission -> Workspace Bootstrap -> Current Store -> Current Draft -> Current Theme -> Workspace DTO -> DTO Admission -> Workspace Rendering.

- Builder Entry begins at `/storefront/builder` navigation or direct route.
- Builder Authority begins at backend `/api/auth/session` capability/lifecycle admission plus protected Store Config endpoint authorization.
- Workspace Bootstrap begins in `StorefrontBuilderPage.loadPageData` with `GET /api/admin/store-config`.
- DTO Admission begins in `admitWorkspaceConfig` after `deriveCurrentStoreIdentityPresentation` accepts current tenant/store/theme/status/RowVersion evidence.
- Workspace Rendering begins only after `storeConfig` is set from admitted backend evidence.

## Boundary Review

- Builder Boundary: backend session/capability/role owns authority; recovery source is `/api/auth/session`; failure redirects/withholds.
- Workspace Boundary: backend `AdminStoreConfigResponse` owns admission; recovery source is `GET /api/admin/store-config`; failure withholds editable workspace.
- Permission Boundary: backend role/capability/endpoint authorization owns; recovery source is session/API revalidation; failure returns 401/403 and clears/refetches.
- Draft Boundary: backend SQL `StoreConfigs.CustomizationJson` draft plus RowVersion owns accepted draft identity; recovery source is backend GET; failure keeps candidates non-authoritative.
- Theme Boundary: backend Store Config theme fields own current theme; recovery source is backend GET/runtime read; failure withholds theme-dependent presentation.
- Recovery Boundary: backend revalidation owns recovery; browser/cache/state only triggers retry; failure shows admission error.
- Presentation Boundary: React/Zustand/canvas own rendering and temporary candidate state only; recovery source is admitted backend DTO; failure withholds.

## Runtime Hardening Performed

- `StorefrontBuilderPage` now withholds `BuilderTopBar`, `SectionPalette`, `BuilderCanvas`, and `SettingsPanel` until a backend-admitted workspace config with RowVersion exists.
- Builder bootstrap clears stale `storeConfig` and sections during backend re-entry and on failed/malformed reads.
- Draft workspace mutations now require backend-admitted RowVersion and send `If-Match`; accepted mutation responses are re-admitted before refreshing local workspace state.

## Exact Runtime Files Modified

- `PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx`

## Regression Freezes Added

- `PointOfSale.Web/src/modules/storefront/builderWorkspaceAuthority.test.ts`
- `PointOfSale.Tests/Architecture/StoreBuilderEntryWorkspaceAuthorityCertificationTests.cs`

## Architecture Documentation Updates

- Added this SF-ADM2.1 findings document.

## AI Context Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Explicit Certification Answers

- Did you review ONLY PointOfSale.Web runtime? Yes, limited to Store Builder entry, workspace bootstrap, workspace admission, and directly consumed backend Store Config/session APIs.
- Which frontend files were modified? `PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx` and `PointOfSale.Web/src/modules/storefront/builderWorkspaceAuthority.test.ts`.
- Which backend files were modified? `PointOfSale.Tests/Architecture/StoreBuilderEntryWorkspaceAuthorityCertificationTests.cs` test only; no backend runtime files.
- Who owns Builder Authority? Backend POS session, tenant, role, lifecycle, capability, and protected API authorization.
- Who owns Workspace Admission? Backend Store Config APIs/services plus frontend DTO admission of backend evidence.
- Who owns Current Draft? Backend SQL `StoreConfigs.CustomizationJson` draft through `AdminStoreConfigService` and RowVersion.
- Who owns Current Published Version? Backend SQL Store Config live/published state and version history services.
- Who owns Builder Permissions? Backend session capabilities, Owner/Admin authorization, and protected endpoints.
- Can frontend create Builder Workspace? No; editable workspace rendering is withheld until backend admission.
- Can React become Builder Authority? No.
- Can LocalStorage become Builder Authority? No.
- Can stale Workspace survive recovery? No; bootstrap clears and re-enters backend admission.
- Can deep links bypass backend admission? No.
- Can bookmarked Builder pages bypass authorization? No.
- Where does Builder Entry begin? `/storefront/builder` navigation/direct route.
- Where does Builder Authority begin? Backend session/capability/lifecycle plus protected Store Config API authorization.
- Where does Workspace Bootstrap begin? `StorefrontBuilderPage.loadPageData` calling backend Store Config APIs.
- Where does DTO Admission begin? `admitWorkspaceConfig` after `deriveCurrentStoreIdentityPresentation` accepts backend evidence.
- Where does Workspace Rendering begin? After `storeConfig` is set from admitted backend evidence.
- Were runtime changes required? Yes.
- Is Store Builder Entry & Workspace production-certifiable? Yes.
