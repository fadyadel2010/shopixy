# PASS SF-ADM2.8 Builder Preview Authority Certification

PointOfSale.Web Store Builder Preview Authority is production-certifiable after preview admission hardening. Backend trusted tenant/session authority, Owner/Admin authorization, SQL `StoreConfigs`, `AdminStoreConfigController.GetStoreConfigPreview`, `AdminStoreConfigService.GetStoreConfigPreviewAsync`, `StorefrontPreviewService.GetConfigPreviewAsync`, `StorefrontDraftConfigMapper.Map`, tenant-bound `StorefrontStoreConfigDto`, no-store response/fetch policy, and frontend DTO admission own Preview Identity, Preview Admission, Draft Resolution, Tenant Resolution, Preview Validation, Preview Recovery, Preview Lifetime for authenticated in-app preview, and presentation admission. Frontend owns only Open Preview, navigation, rendering, device-size presentation, temporary modal state, and abort/close behavior.

## Pages Reviewed

- `PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx`
- `PointOfSale.Web/src/modules/storefront/components/builder/PreviewModal.jsx`
- `PointOfSale.Web/src/modules/storefront/components/builder/BuilderThemeProvider.jsx`
- `PointOfSale.Web/src/modules/storefront/services/storeConfigService.js`
- `PointOfSale.Web/src/modules/storefront/store/builderStore.js`
- `PointOfSale.Web/src/auth/commerceHubVendorIdentityAuthority.js`
- `PointOfSale.Web/src/auth/posVendorSessionAuthority.js`
- `PointOfSale.Web/src/modules/storefront/pages/ThemeMarketplacePage.jsx` only for separate theme preview-token boundary
- `PointOfSale.Web/src/pages/StorefrontSettings.jsx` only for adjacent store config preview link boundary

## Backend Endpoints Reviewed

- `GET /api/storefront/config/preview`
- `GET /api/admin/store-config/preview`
- `GET /api/admin/store-config`
- `PUT /api/admin/store-config/pages/{pageId}`
- `GET /api/storefront/store-config` only as runtime presentation evidence directly consumed by Builder bootstrap/theme presentation
- `POST /api/admin/store-config/theme/preview-link/{themeId}` only as separate theme preview-token boundary
- `GET /api/storefront/store-config?themePreviewToken=...` only as separate theme preview-token consumption

## Backend Services Reviewed

- `AdminStoreConfigService.GetStoreConfigPreviewAsync`
- `StorefrontPreviewService.GetConfigPreviewAsync`
- `StorefrontDraftConfigMapper.Map`
- `StorefrontDraftConfigMapper.MapLive`
- `AdminStoreConfigService.UpdatePageLayoutAsync` only for save-before-preview admission
- `AdminStoreConfigService.MapToResponseAsync` only for workspace admission evidence
- `StorefrontThemePreviewTokenService` only for separate theme preview tokens

## Findings

Preview findings: Builder Preview begins when `StorefrontBuilderPage.handlePreview` opens the modal and saves the current draft. Preview Authority begins only after backend Owner/Admin authorization, trusted tenant resolution, SQL draft read, draft DTO mapping, tenant evidence emission, and frontend `deriveCurrentVendorScopedDto` admission.

Token findings: In-app Builder Preview does not use a share token; its lifetime is the authenticated POS admin session and the current backend request. Separate theme preview links use backend-issued, tenant-bound, 10-minute data-protection tokens and cannot become draft or live authority.

Draft findings: Draft Resolution is backend-owned by `StorefrontPreviewService.GetConfigPreviewAsync`, which reads current SQL `StoreConfigs` by trusted tenant and maps draft branches with `useDraftContent: true`. Missing draft branches produce empty preview sections, not live fallback.

Failure findings: 400, 401, 403, 404, 409, 422, 428, 500, timeout, malformed DTO, expired session, foreign tenant evidence, and save-before-preview failure withhold preview or show preview error. Invalid preview is not accepted and live content is not substituted.

Recovery findings: Refresh, reconnect, browser refresh, server restart, Redis loss, Mongo rebuild, and preview refresh re-enter backend SQL/session authority. Redis and Mongo are not read by Builder Preview authority.

Concurrency findings: Another admin edit or publish while preview is open cannot advance preview authority. The modal shows the backend-admitted DTO it fetched; refresh/save-preview re-enters RowVersion and current SQL draft authority. Publish updates live separately and does not convert Preview into Live.

Cache findings: React, Zustand, browser cache, BFCache, LocalStorage, SessionStorage, and the Preview modal can hold presentation only. `getPreviewConfig` uses no-store headers, preview state is cleared on close, and theme store injection restores prior config on modal teardown.

Cross-system findings: Preview does not leak into Published Storefront, SEO, Affiliate, CommerceHub, Analytics, Checkout, Orders, or projections. Public live config uses `MapLive`; Builder Preview uses draft mapping through authenticated preview APIs.

## Preview Pipeline

Draft -> Preview Intent -> Save-before-preview RowVersion admission -> Backend tenant/session validation -> SQL draft resolution -> Backend DTO with tenant evidence -> DTO admission -> Preview rendering.

- Preview begins: `StorefrontBuilderPage.handlePreview`.
- Preview Authority begins: `AdminStoreConfigController.GetStoreConfigPreview` / `/api/storefront/config/preview` after trusted tenant/session and Owner/Admin authorization.
- Draft Resolution begins: `StorefrontPreviewService.GetConfigPreviewAsync` SQL read by tenant.
- DTO Admission begins: `deriveCurrentVendorScopedDto` accepting tenant-bound backend preview DTO.
- Presentation begins: `PreviewModal` rendering admitted `previewConfig`.

## Boundary Review

- Preview Boundary: backend owns authority; recovery source is authenticated preview API; failure withholds modal content.
- Token Boundary: no token for in-app Builder Preview; separate theme tokens are backend-issued and tenant/expiry-bound; invalid token returns 401.
- Draft Boundary: backend SQL draft owns; recovery source is current SQL read; missing draft renders empty preview without live fallback.
- Recovery Boundary: backend session + SQL preview read owns; browser/cache can only retry; failure shows error.
- Presentation Boundary: React/PreviewModal owns rendering only; recovery source is admitted DTO; close clears preview state.
- Concurrency Boundary: backend RowVersion save-before-preview and current SQL preview read own accepted state; stale saves fail closed.
- Live Isolation Boundary: backend `Map` draft preview and `MapLive` public runtime are separate; failure never substitutes live.

## Runtime Hardening Performed

- Added tenant identity evidence to `StorefrontStoreConfigDto` and `StorefrontDraftConfigMapper`.
- Exposed authenticated Builder Preview at `/api/storefront/config/preview` while preserving `/api/admin/store-config/preview`.
- Added no-store response metadata to the backend preview action.
- Added no-store frontend headers to `storeConfigService.getPreviewConfig`.

## Exact Runtime Files Modified

- `PointOfSale.API/DTOs/Storefront/StorefrontStoreConfigDtos.cs`
- `PointOfSale.API/Services/Storefront/StorefrontDraftConfigMapper.cs`
- `PointOfSale.API/Controllers/AdminStoreConfigController.cs`
- `PointOfSale.Web/src/modules/storefront/services/storeConfigService.js`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/StoreBuilderPreviewAuthorityCertificationTests.cs`
- `PointOfSale.Web/src/modules/storefront/services/storeConfigService.test.ts`
- `PointOfSale.Tests/Storefront/StorefrontPreviewServiceTests.cs`

## Architecture Documentation Updates

- Added this SF-ADM2.8 findings document.

## AI Context Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Explicit Certification Answers

- Did you review ONLY PointOfSale.Web runtime? Yes, limited to Builder Preview and directly consumed backend preview/config/theme-preview boundaries.
- Which frontend files were modified? `PointOfSale.Web/src/modules/storefront/services/storeConfigService.js` and its test.
- Which backend files were modified? `StorefrontStoreConfigDtos.cs`, `StorefrontDraftConfigMapper.cs`, `AdminStoreConfigController.cs`, plus tests.
- Who owns Preview? Backend.
- Who owns Preview Token? Backend; in-app Builder Preview has no share token, theme preview token is backend-issued.
- Who owns Draft Resolution? Backend SQL/services.
- Who owns Tenant Resolution? Backend tenant context/session, with frontend DTO admission validating evidence.
- Who owns Preview Lifetime? Backend authenticated session/request; theme token expiry for separate theme preview.
- Can frontend create Preview? No, it can request/open/render only.
- Can React become Preview Authority? No.
- Can LocalStorage become Preview Authority? No.
- Can Redis become Preview Authority? No.
- Can Preview become Live? No.
- Can Live read Draft? No.
- Can stale Preview survive recovery? No accepted stale authority survives; recovery re-enters backend.
- Can replay Preview Token? In-app preview has no token; expired/foreign theme tokens fail validation.
- Can foreign tenant open Preview? No.
- Where does Preview begin? `handlePreview`.
- Where does Preview Authority begin? Backend authenticated preview endpoint.
- Where does Draft Resolution begin? `StorefrontPreviewService.GetConfigPreviewAsync`.
- Where does DTO Admission begin? `deriveCurrentVendorScopedDto`.
- Where does Presentation begin? `PreviewModal`.
- Were runtime changes required? Yes.
- Is Builder Preview production-certifiable? Yes.
