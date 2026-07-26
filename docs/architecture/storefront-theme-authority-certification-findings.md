# PASS P2-GC Storefront Theme Authority

Date: 2026-06-09

Status: production-certifiable for the inspected runtime after hardening and deployment of
`20260609180000_StorefrontThemeAuthorityHardening`.

## Authority Decision

Tenant-owned PostgreSQL `StoreConfig` owns active theme implementation identity, draft/live preset
identity, draft/live visual overrides, customization state, publication version, and rollback
state. System `ThemePresets` and the compiled storefront theme registry define allowed
presentation implementations. Themes may render current configuration, CMS, navigation,
catalog, checkout, customer, payment, order, and shipping truth, but may not create or replace it.

## Findings

- Theme identity: compiled theme IDs are allowlisted by `StorefrontThemes`; unknown DTO, route,
  database, local-storage, and query values collapse to the default implementation.
- Activation: implementation activation is an Owner/Admin, tenant-derived, row-version-protected
  SQL `StoreConfig` mutation. Preset activation now has distinct draft and live identities.
- Customization: theme override JSON accepts only allowlisted colors, fonts, typography, and radius
  fields. Currency, branding authority, feature flags, behavior, content, navigation, commerce,
  customer, payment, order, and shipping fields are rejected.
- Content boundary: live theme rendering receives live SQL configuration and published content.
  Theme code selects components only; it cannot promote CMS drafts or bypass P2-GB visibility.
- Preview: anonymous `previewTheme` query authority was removed. Owner/Admin obtains a protected,
  ten-minute, tenant/theme-bound capability. The capability changes only the compiled presentation
  implementation over current live SQL configuration/content and cannot expose drafts.
- Assets: inspected theme assets are compiled application assets, not tenant-owned upload records.
  There is no runtime theme package installation, arbitrary template upload, executable script
  upload, tenant asset mutation, or deleted-theme asset resurrection path.
- Rendering: public configuration re-reads SQL, uses `ThemeLivePresetKey` and live overrides, and
  normalizes implementation identity through the compiled allowlist.
- Caching: Redis/Mongo theme data remains derivative. Projection recomposition now uses the live
  preset identity from current SQL; public configuration does not read projection/cache authority.
- Updates: Owner/Admin, trusted tenant context, optimistic concurrency, publish lock, durable event
  staging, and audit/version history govern theme writes and publication.
- Consumption: layout, page, and section resolvers consume only the server-resolved implementation
  ID. Business APIs continue to enforce P2-A through P2-F and P2-GA/P2-GB authority independently
  of theme rendering.
- Tenant isolation: SQL writes are tenant-filtered; preview capabilities are tenant-bound; a token
  issued for one tenant fails for another.
- Concurrency: row versions prevent stale draft, activation, publish, and rollback writes. Publish
  locks serialize theme publication, and projection replay reloads current SQL.
- Historical consistency: migration backfills live preset identity from existing state. Draft
  selection cannot alter live rendering. Rollback restores snapshotted live preset/overrides while
  preserving current draft branches.
- Adversarial: theme spoofing, unsupported implementation IDs, activation replay, anonymous preview
  override, cross-tenant preview reuse, expired preview reuse, authority-shaped override JSON,
  stale publish, stale rollback, and cache resurrection fail closed on inspected paths.

## Runtime Hardening Performed

- Added separate SQL `ThemeLivePresetKey` and backfilled current live state.
- Changed public and projection rendering to resolve the live preset only.
- Made theme publish atomically promote draft preset plus draft overrides.
- Made rollback restore live preset without overwriting current draft identity.
- Added a compiled server-side storefront theme allowlist and normalized legacy/spoofed values.
- Restricted theme override JSON to presentation-only fields and bounded values.
- Replaced anonymous `previewTheme` query switching with protected tenant-bound preview tokens.
- Removed browser query authority from the runtime theme resolver.

## Exact Runtime Files Modified

- `PointOfSale.Core/Constants/StorefrontThemes.cs`
- `PointOfSale.Core/Entities/StoreConfig.cs`
- `PointOfSale.API/DTOs/Admin/AdminStoreConfigDtos.cs`
- `PointOfSale.API/Controllers/AdminStoreConfigController.cs`
- `PointOfSale.API/Controllers/StorefrontStoreConfigController.cs`
- `PointOfSale.API/Services/Admin/AdminStoreConfigService.cs`
- `PointOfSale.API/Services/Storefront/StorefrontDraftConfigMapper.cs`
- `PointOfSale.API/Services/Storefront/StorefrontPreviewService.cs`
- `PointOfSale.API/Services/Storefront/StorefrontThemePreviewTokenService.cs`
- `PointOfSale.API/Program.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
- `PointOfSale.Infrastructure/Consumers/StorefrontConfigProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Migrations/20260609180000_StorefrontThemeAuthorityHardening.cs`
- `PointOfSale.Storefront/src/services/api/storefrontApi.ts`
- `PointOfSale.Storefront/src/storefront/themes/ThemeResolver.ts`
- `PointOfSale.Web/src/modules/storefront/pages/ThemeMarketplacePage.jsx`
- `PointOfSale.Web/src/modules/storefront/services/storeConfigService.js`

## Regression Freezes

- `PointOfSale.Tests/Admin/AdminStoreConfigThemeContractTests.cs`
- `PointOfSale.Tests/Storefront/StorefrontThemePreviewTokenServiceTests.cs`
- `PointOfSale.Tests/Architecture/StorefrontThemeAuthorityCertificationTests.cs`

## Explicit Answers

- Who owns theme authority? Tenant-owned SQL `StoreConfig`, constrained by system presets and the
  compiled theme registry.
- Can themes override StoreConfig authority? No.
- Can themes override CMS authority? No.
- Can previews expose drafts? Theme previews cannot; CMS draft preview remains separately governed
  by P2-GB exact-version capabilities.
- Can caches become authority? No.
- Can stale themes resurrect? No on inspected paths.
- Can tenant leakage occur? No inspected theme path permits it.
- Can replay overwrite active-theme truth? No; row-version and publish-lock controls reject stale
  mutation, while replayed projections reload current SQL.
- Were runtime code changes required? Yes.
- Is Storefront Theme Authority production-certifiable? Yes, for the inspected compiled-theme
  runtime after applying the migration.
