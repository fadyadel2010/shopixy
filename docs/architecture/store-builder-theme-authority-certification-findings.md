# PASS SF-ADM2.5 Builder Theme Authority Certification

Date: 2026-06-30

Scope reviewed: PointOfSale.Web Store Builder Theme runtime only, plus directly consumed Store Config/theme backend APIs and services. Out of scope: PointOfSale.Storefront implementation internals except the directly consumed public runtime theme-preview token read, CommerceHub, Marketplace, Affiliate, publishing beyond theme publish acceptance, checkout, orders, media library, and version history.

Pages reviewed: `ThemeEditorPage.jsx`, `ThemeMarketplacePage.jsx`, and `StorefrontBuilderPage.jsx` only where it admits backend Store Config/preview evidence for Builder Theme rendering.

Frontend runtime reviewed: `BuilderThemeProvider.jsx`, `themeMarketplaceRegistry.js`, `storeConfigService.js`, `builderStore.js`, `builderWorkspaceAuthority.test.ts`, and `storeConfigService.test.ts`.

Backend endpoints reviewed: `GET /api/admin/store-config`, `GET /api/admin/store-config/themes/presets`, `PUT /api/admin/store-config/theme/preset/{presetKey}`, `PUT /api/admin/store-config/theme/overrides`, `POST /api/admin/store-config/theme/publish`, `PUT /api/admin/store-config`, `POST /api/admin/store-config/theme/preview-link/{themeId}`, and `GET /api/storefront/store-config?themePreviewToken=...`.

Backend services reviewed: `AdminStoreConfigService`, `IAdminStoreConfigService`, `StorefrontThemePreviewTokenService`, `StorefrontStoreConfigController`, and the admin/theme DTOs in `AdminStoreConfigDtos.cs`.

Findings:

- Theme Identity, Theme Preset, Theme Tokens, Theme Variables, Theme Version, Theme Persistence, Theme Acceptance, Theme Recovery, and Theme Audit are backend-owned by trusted tenant/session context, SQL `StoreConfigs`, SQL `ThemePresets`, `AdminStoreConfigController`, `AdminStoreConfigService`, EF `StoreConfig.RowVersion`, mandatory `If-Match`, SQL persistence, accepted `AdminStoreConfigResponse`, and preview-token capability validation.
- Frontend Theme Editor and Marketplace own only editing, temporary candidate values, preview navigation, presentation, and loading/error states. They now admit backend Store Config DTOs through `deriveCurrentStoreIdentityPresentation` before presentation, keep the current accepted RowVersion from admitted backend responses, and send `If-Match` on theme draft and publish mutations.
- Theme tokens remain backend-owned. Backend `ValidateThemeOverrideShape` allows only presentation-shaped colors, typography/font, radius, and border-radius fields; unsupported authority-shaped fields are rejected instead of being persisted.
- CSS variables are presentation output derived from backend-admitted theme DTOs. The frontend can preview candidate colors/font/radius locally, but accepted CSS variables begin only after backend validation, RowVersion persistence, accepted DTO emission, and frontend DTO admission.
- Failed reads or malformed/foreign/stale responses cannot establish theme presentation because frontend admission rejects missing current tenant, store name, status, theme id, or RowVersion evidence. Failed mutations cannot mark candidates clean because local accepted state advances only through accepted backend response admission.
- Refresh, reconnect, browser restart, server restart, Redis loss, Mongo rebuild, and workspace reload converge to `GET /api/admin/store-config` or the public SQL-backed runtime read. Browser cache, React state, Zustand, localStorage, sessionStorage, Redis, Mongo, and projections remain derivative or inert.
- Multiple tabs, another admin, retry, replay, duplicate save, stale RowVersion, modified Theme ID, modified Theme Version, injected tokens, injected CSS variables, and replayed DTOs cannot overwrite backend truth. The backend requires RowVersion/`If-Match`, binds EF original `RowVersion`, validates theme shape/preset support, and rejects conflicts.
- Candidate Theme does not leak cross-system before backend acceptance. Theme preview requires a server-issued tenant/theme-bound token; active/public runtime reads use live SQL theme state and preview can only swap compiled theme presentation for the validated tenant token.

Theme pipeline:

Backend Theme begins in SQL `StoreConfigs` and SQL `ThemePresets`.
Theme Authority begins at `AdminStoreConfigController`/`AdminStoreConfigService` after trusted tenant/session authorization, RowVersion precondition admission, validation, canonicalization, SQL persistence, and accepted DTO emission.
Workspace Admission begins when PointOfSale.Web reads `GET /api/admin/store-config` and admits current tenant/store/theme/status/RowVersion evidence.
Editable Theme Candidate begins in `ThemeEditorPage` after admitted backend config is copied into memory-only form state.
Theme Editing begins when admins change preset/color/font/radius controls.
Mutation begins when Theme Editor submits `updateThemeOverrides` or `publishTheme` with admitted RowVersion in `If-Match`.
Backend Validation begins in `AdminStoreConfigController` and `AdminStoreConfigService`.
Theme Canonicalization begins in backend preset lookup plus `ValidateThemeOverrideShape`.
RowVersion begins in SQL `StoreConfigs.RowVersion`.
SQL Persistence begins in `AdminStoreConfigService` save paths.
Accepted Theme DTO begins in `AdminStoreConfigResponse`.
DTO Admission begins in PointOfSale.Web through `deriveCurrentStoreIdentityPresentation`.
Theme Rendering/Presentation begins in Theme Editor, Marketplace, Builder preview, and Builder Theme Provider after accepted DTO admission.

Boundaries:

- Theme Boundary: backend SQL/config service owns; recovery source is backend config read; failure withholds/rejects.
- Theme Token Boundary: backend shape validation owns; recovery source is SQL `ThemeDraftOverridesJson`/`ThemeLiveOverridesJson` plus presets; failure rejects malformed/unsupported tokens.
- Theme Variable Boundary: backend accepted tokens own; frontend only renders derived CSS presentation; failure falls back to no accepted presentation.
- Mutation Boundary: `If-Match`/RowVersion plus backend service validation owns; failure returns 409/428/422/500 without accepted local replacement.
- Recovery Boundary: backend SQL read owns; failure clears/withholds accepted presentation.
- Presentation Boundary: admitted backend DTO owns accepted presentation; local candidate state owns preview only.
- Concurrency Boundary: EF RowVersion owns; stale/duplicate/replayed mutations conflict or fail closed.

Runtime hardening performed:

- Theme Editor now admits raw config and mutation responses through `deriveCurrentStoreIdentityPresentation`.
- Theme Editor now stores the accepted backend RowVersion and sends `If-Match` on draft override save and publish.
- Theme publish now uses the RowVersion returned by the immediately preceding accepted draft save.
- Theme Marketplace now admits load and activation responses through `deriveCurrentStoreIdentityPresentation` before presenting/replacing active theme state.
- Theme service methods now accept request options for theme preset/override/publish calls so callers can provide backend precondition headers.

Exact runtime files modified:

- `PointOfSale.Web/src/modules/storefront/pages/ThemeEditorPage.jsx`
- `PointOfSale.Web/src/modules/storefront/pages/ThemeMarketplacePage.jsx`
- `PointOfSale.Web/src/modules/storefront/services/storeConfigService.js`

Regression freezes added:

- `PointOfSale.Web/src/modules/storefront/services/storeConfigService.test.ts`
- `PointOfSale.Web/src/modules/storefront/builderWorkspaceAuthority.test.ts`
- `PointOfSale.Tests/Architecture/StoreBuilderThemeAuthorityCertificationTests.cs`

Architecture documentation updates:

- `docs/architecture/store-builder-theme-authority-certification-findings.md`

AI context updates:

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/CODEX_RULES.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/KNOWN_RISKS.md`

Certification answers:

- Only PointOfSale.Web Builder Theme runtime was reviewed, plus directly consumed backend theme/config APIs/services.
- Frontend cannot create accepted Theme, accepted Theme Identity, Theme Version, Theme Persistence, Recovery, or Audit.
- React, Zustand, localStorage, sessionStorage, browser cache, Redis, Mongo, projections, URLs, and replayed DTOs cannot become Theme Authority.
- Stale Theme cannot survive recovery as accepted authority because recovery re-enters backend SQL Store Config/Theme authority or fails closed.
- Duplicate/replayed theme mutations cannot overwrite newer Theme because backend RowVersion/`If-Match` gates all acceptance.
- Builder Theme Authority is production-certifiable for the inspected runtime.
