# PASS SF-ADM1.2 Store Configuration Read Authority Certification

PointOfSale.Web Store Administration configuration read runtime is production-certifiable with no runtime code change required. Backend SQL `StoreConfigs`, SQL `Tenants`, trusted tenant/session context, `AdminStoreConfigController.GetStoreConfig`, `AdminStoreConfigService.GetStoreConfigAsync`, public `StorefrontStoreConfigController.GetStoreConfig`, and live SQL mapping through `StorefrontDraftConfigMapper.MapLive` own Store Configuration, Store Identity, Business Identity evidence, Theme Assignment, Branding, Currency, SEO/runtime metadata where modeled, Store Status, Store Visibility, persistence, projection admission, and recovery.

Frontend Storefront Settings, Storefront Status Card, `storeConfigService`, shared settings formatters, React state, React Query, browser cache/BFCache, LocalStorage, SessionStorage, route params, DTO replay, and presentation formatting own loading, grouping, temporary candidate state, no-store requests, fail-closed DTO admission, and rendering only. They cannot create Store Configuration, Store Identity, Currency, Theme, SEO, Business Identity, Visibility, or recovery truth.

## Runtime Reviewed

- Page: `PointOfSale.Web/src/pages/StorefrontSettings.jsx`
- Dashboard card: `PointOfSale.Web/src/components/StorefrontStatusCard.jsx`
- Settings context/formatting: `PointOfSale.Web/src/contexts/SettingsContext.jsx`
- Config service: `PointOfSale.Web/src/modules/storefront/services/storeConfigService.js`
- Frontend DTO admission: `PointOfSale.Web/src/auth/commerceHubVendorIdentityAuthority.js`
- Admin read API: `PointOfSale.API/Controllers/AdminStoreConfigController.cs`
- Admin service: `PointOfSale.API/Services/Admin/AdminStoreConfigService.cs`
- Public runtime read API directly consumed by Store Administration preview/runtime helpers: `PointOfSale.API/Controllers/StorefrontStoreConfigController.cs`
- Runtime mapper: `PointOfSale.API/Services/Storefront/StorefrontDraftConfigMapper.cs`
- DTOs/entities: `AdminStoreConfigDtos.cs`, `StorefrontStoreConfigDtos.cs`, `StoreConfig.cs`, `Tenant.cs`

## Findings

Configuration findings: Store Configuration begins only at backend SQL plus backend services. Admin GET resolves trusted tenant context and returns an `AdminStoreConfigResponse` with `TenantId`, `RowVersion`, `StoreName`, `ThemeId`, `StorefrontStatus`, timestamps, draft/live theme evidence, behavior, and page/template presentation evidence. Missing admin config is created only inside the backend service boundary.

Store Identity findings: Store Identity is backend-owned by trusted session/tenant context, SQL `StoreConfig.TenantId`, and SQL `Tenant`. Frontend user/session state is admission evidence only; `deriveCurrentStoreIdentityPresentation` rejects DTOs that do not match current backend tenant evidence.

Business findings: Company/profile/business identity, branding, status, visibility, currency, locale/country, and operational settings are backend-owned. Frontend settings code formats and renders accepted values only; unsaved Storefront Settings edits remain local candidates until a separate backend mutation boundary accepts them.

Theme findings: Theme authority is backend-owned by SQL `StoreConfig.ThemeId`, theme preset keys, live/draft theme fields, and server mapping. Storefront Settings can display and submit candidates but cannot choose the active theme for reads.

SEO findings: SEO and runtime metadata remain backend-owned through StoreConfig/CMS/SEO services. Frontend Store Administration read runtime does not invent title, description, canonical, slug, or meta configuration.

Cache findings: Admin and public config GET endpoints are marked no-store, and `storeConfigService` sends `Cache-Control: no-store` plus `Pragma: no-cache`. React/component state and any React Query/cache/browser replay are derivative presentation only and must re-enter backend GET authority.

Recovery findings: Refresh, browser restart, another device, server restart, Redis loss, Mongo/search rebuild, another admin update, subscription/tenant/status changes, and stale tabs converge by reissuing backend reads or failing closed. Store Admin read runtime does not hydrate config from LocalStorage, SessionStorage, Redis, Mongo, or search.

Failure findings: Missing tenant context, 401/403 route/API authority loss, 404 missing public config, 409/412/428 mutation conflicts, malformed/partial DTOs, and 500/503 failures cannot replace presentation. Frontend read admission returns null and clears/withholds config presentation on malformed or failed reads.

Concurrency findings: Two admins, multiple tabs, refresh, and concurrent reads cannot cause stale configuration to replace current backend authority. Stale async completions are sequence-fenced in Storefront Settings and Storefront Status Card; mutation concurrency is separately row-version protected.

Cross-system findings: Store Administration, Storefront, Affiliate, Checkout, Orders, Analytics, and CommerceHub observe configuration as backend-owned evidence. Cross-system projection/cache paths may copy or display admitted SQL truth but cannot become configuration authority.

## Pipeline

SQL `StoreConfigs` and `Tenants`
-> `AdminStoreConfigService.GetStoreConfigAsync` or `StorefrontStoreConfigController.GetStoreConfig`
-> configuration API response with tenant/version/theme/status/currency evidence
-> frontend no-store service call
-> `deriveCurrentStoreIdentityPresentation` DTO admission
-> component baseline/state
-> presentation.

Configuration Authority begins at SQL `StoreConfigs`/`Tenants` plus backend service/controller admission.

DTO Admission begins in Store Administration at `deriveCurrentStoreIdentityPresentation`, after the backend response carries current tenant, row-version, store name, status, and theme evidence.

Presentation begins in `StorefrontSettings.jsx` and `StorefrontStatusCard.jsx` only after DTO admission succeeds.

## Boundaries

- Store Identity Boundary: backend trusted tenant/session context plus SQL `StoreConfig.TenantId`; recovery source is backend read; failure withholds.
- Configuration Boundary: SQL `StoreConfigs` plus `AdminStoreConfigService`; recovery source is SQL; failure withholds or backend creates default only inside service boundary.
- Business Boundary: SQL `Tenant`/`StoreConfig`/settings services; recovery source is backend read; failure withholds.
- Theme Boundary: SQL `StoreConfig` theme fields and `ThemePresets`; recovery source is backend read; failure uses backend mapper constraints or withholds.
- Currency Boundary: SQL `Tenant.Currency`; recovery source is backend read; public runtime fails 503 when absent.
- SEO Boundary: StoreConfig/CMS/SEO backend services; recovery source is backend read; frontend cannot invent.
- Recovery Boundary: backend revalidation/refetch; caches/storage are derivative; failure clears/withholds.
- Presentation Boundary: Storefront Settings/Status Card render only admitted backend evidence.

## Runtime Hardening

Runtime changes required: none.

Exact runtime files modified: none.

Regression freezes added: `PointOfSale.Tests/Architecture/StorefrontConfigurationAuthorityCertificationTests.cs` now records PASS SF-ADM1.2 artifacts.

Architecture documentation updates: this findings document.

AI context updates: `.ai-context/PROJECT_CONTEXT.md`, `.ai-context/ARCHITECTURE_TRUTH.md`, `.ai-context/KNOWN_RISKS.md`, `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`, and `.ai-context/CODEX_RULES.md`.

## Certification Answers

Did you review only PointOfSale.Web Store Administration runtime? Yes. Backend APIs/services were reviewed only where directly consumed by Store Administration configuration reads.

Which frontend files were modified? None.

Which backend files were modified? None.

Who owns Store Configuration? Backend SQL `StoreConfigs` plus backend store configuration services.

Who owns Store Identity? Backend trusted tenant/session context plus SQL `StoreConfig.TenantId` and `Tenants`.

Who owns Currency? Backend SQL `Tenant.Currency`.

Who owns Theme? Backend SQL `StoreConfig` theme fields and backend theme preset/service mapping.

Who owns SEO? Backend StoreConfig/CMS/SEO services and SQL-backed metadata.

Can frontend create Store Configuration? No.

Can React Query become authority? No.

Can Browser Cache become authority? No.

Can LocalStorage become authority? No.

Can stale configuration survive recovery? No; it must re-enter backend read authority or be withheld.

Can different modules observe conflicting authority? No inspected path permits that; modules consume backend-owned evidence or derivative projections.

Where does Store Configuration begin? SQL `StoreConfigs`/`Tenants` through backend controller/service admission.

Where does DTO Admission begin? `deriveCurrentStoreIdentityPresentation` for Store Administration presentation after backend response evidence.

Where does Presentation begin? `StorefrontSettings.jsx` and `StorefrontStatusCard.jsx` after DTO admission succeeds.

Were runtime changes required? No.

Is Store Configuration Read production-certifiable? Yes.
