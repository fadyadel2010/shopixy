# PASS SF-ADM1.5 Global Business Configuration Authority Certification

PointOfSale.Web Store Administration Global Business Configuration is production-certifiable after Settings page admission hardening. Backend trusted tenant context, Owner/Admin authorization, SQL `SystemSettings`, SQL `Tenants`, SQL `StoreConfigs`, `SettingsController`, `AdminStoreConfigController`, `AdminStoreConfigService`, backend tax/currency/config services, outbox events, and current backend read admission own Business Identity, Business Configuration, Currency, Tax Defaults, Receipt Defaults, Theme Assignment, Brand Identity, Business Status, Visibility, Persistence, Recovery, Projection, Audit, and Cross-System Distribution.

Frontend Store Administration settings pages, `SettingsContext`, React state, browser cache/BFCache, LocalStorage, SessionStorage, form fields, receipt preview, formatting, grouping, loading, navigation, and retry controls own presentation and temporary candidate state only.

## Pages Reviewed

- `PointOfSale.Web/src/pages/Settings.jsx`
- `PointOfSale.Web/src/pages/StorefrontSettings.jsx`
- `PointOfSale.Web/src/contexts/SettingsContext.jsx`
- `PointOfSale.Web/src/modules/storefront/services/storeConfigService.ts`

## Backend Endpoints Reviewed

- `GET /api/settings`
- `PUT /api/settings`
- `GET /api/settings/default-country`
- `PUT /api/settings/default-country`
- `GET /api/admin/store-config`
- `PUT /api/admin/store-config`
- `GET /api/storefront/config`

## Backend Services Reviewed

- `SettingsController`
- `AdminStoreConfigController`
- `AdminStoreConfigService`
- `TaxCalculationService`
- `TenantCurrencyProjectionConsumer`
- `StorefrontConfigProjectionConsumer`
- checkout/order snapshot paths where directly consuming global settings evidence

## Findings

- Business: Business Identity begins in backend tenant/session authority plus SQL `Tenants`, tenant-scoped `SystemSettings`, and SQL `StoreConfigs`. Frontend company/profile/logo edits are candidate intent until backend acceptance and reread.
- Currency: Currency is backend-owned by `Tenant.Currency`, backend currency guards, tenant-scoped settings, SQL persistence, and `TenantCurrencyChangedEvent` outbox/projection. Frontend currency lists and formatters are presentation only.
- Tax: Tax Configuration is backend-owned by tenant-scoped `SystemSettings` and backend tax services. Checkout/orders persist tax snapshots and do not consume frontend tax state as authority.
- Theme: Theme Assignment is backend-owned by `StoreConfigs` through Store Administration config APIs and accepted row-versioned responses. Frontend theme controls are candidates/presentation.
- Receipt: Receipt header/footer/logo defaults are backend-owned by tenant-scoped `SystemSettings`; receipt preview is local presentation only.
- Recovery: Refresh, browser restart, another browser, another admin, server restart, Redis loss, Mongo/search rebuild, and projection rebuild converge through SQL/backend rereads. Settings page now withholds form presentation until a backend settings read is admitted and successful save presentation requires a fresh backend reread.
- Failure: 400, 401, 403, 404, 409, 422, 428, 500, malformed DTO, timeout, and cache/projection failure fail closed by retaining prior backend-admitted shared settings or withholding page presentation. Unauthorized mutation is Owner/Admin blocked.
- Cross-system: Store Administration, Storefront, Checkout, Orders, Analytics, Affiliate, CommerceHub, and POS consume current backend configuration or immutable historical snapshots. Redis/Mongo/search/browser caches are derivative only.

## Boundary Review

- Business Boundary: Backend SQL tenant/settings/config services own authority; SQL is recovery source; failure withholds presentation or returns 4xx/5xx.
- Currency Boundary: Backend `Tenant.Currency` and currency guards own authority; SQL/outbox/projection reread is recovery source; invalid currency fails closed.
- Tax Boundary: Backend tenant `SystemSettings` and tax services own authority; SQL/snapshots are recovery source; malformed tax reads fail closed/default only inside backend read composition.
- Theme Boundary: Backend `StoreConfigs` and row-versioned config APIs own authority; SQL `StoreConfigs` is recovery source; stale/missing RowVersion fails closed.
- Receipt Boundary: Backend tenant `SystemSettings` owns receipt defaults; SQL is recovery source; frontend preview cannot persist or distribute receipt authority.
- Recovery Boundary: Backend SQL/session/config/settings reads restore truth; browser/projection/cache state can only trigger refetch.
- Projection Boundary: Outbox/event consumers copy SQL truth; SQL is recovery source; projection loss cannot author truth.
- Presentation Boundary: Frontend begins presentation only after accepted backend DTO admission; failure withholds or clears presentation.
- Cross-System Boundary: Consumers use current backend reads or persisted transaction snapshots; conflicts converge to backend truth.

## Global Configuration Pipeline

Backend SQL (`Tenants`, `SystemSettings`, `StoreConfigs`) -> Business Services (`SettingsController`, config/tax/currency services) -> Configuration API (`/api/settings`, `/api/admin/store-config`) -> DTO Admission (backend tenant/role validation and frontend accepted DTO reread/admission) -> Settings Context / Settings Page presentation -> Cross-System Consumption (Storefront, Checkout, Orders, Analytics, Affiliate, CommerceHub, POS through backend reads, projections, or snapshots).

- Business Authority begins at trusted backend tenant/session plus SQL tenant/settings/config authority.
- Configuration Authority begins in backend settings/config service/controller admission.
- DTO Admission begins at backend response validation and frontend accepted backend reread/admission.
- Presentation begins in Store Administration after DTO admission succeeds.
- Cross-System propagation begins after SQL commit and outbox/projection or current backend read consumption.

## Runtime Hardening Performed

- Removed frontend default business settings from `Settings.jsx` initial page state.
- Added fail-closed settings presentation when `/api/settings` read fails or returns no DTO.
- Required successful settings save presentation to wait for a fresh backend settings reread.

## Exact Runtime Files Modified

- `PointOfSale.Web/src/pages/Settings.jsx`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/GlobalBusinessConfigurationAuthorityCertificationTests.cs`

## Architecture Documentation Updates

- Added this SF-ADM1.5 findings document.

## AI Context Updates

- Updated `.ai-context/PROJECT_CONTEXT.md`, `.ai-context/ARCHITECTURE_TRUTH.md`, `.ai-context/KNOWN_RISKS.md`, `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`, and `.ai-context/CODEX_RULES.md`.

## Explicit Certification Answers

- Did you review ONLY PointOfSale.Web runtime? Yes, limited to Store Administration global business configuration runtime and directly consumed backend APIs/services.
- Which frontend files were modified? `PointOfSale.Web/src/pages/Settings.jsx`.
- Which backend files were modified? None.
- Who owns Business Identity? Backend trusted tenant/session authority, SQL `Tenants`, tenant-scoped `SystemSettings`, and SQL `StoreConfigs`.
- Who owns Currency? Backend `Tenant.Currency`, backend currency guards, tenant-scoped settings, SQL persistence, and projection consumers.
- Who owns Tax Configuration? Backend tenant-scoped `SystemSettings` and backend tax services.
- Who owns Theme Assignment? Backend SQL `StoreConfigs` and Store Administration config APIs/services.
- Who owns Receipt Configuration? Backend tenant-scoped `SystemSettings`.
- Can frontend redefine Business Configuration? No.
- Can React become authority? No.
- Can LocalStorage become authority? No.
- Can stale Business Configuration survive recovery? No, recovery re-enters backend SQL/settings/config authority or withholds presentation.
- Can different systems observe conflicting business truth? No authoritative conflict; consumers converge on backend current truth or immutable snapshots.
- Where does Business Authority begin? Trusted backend tenant/session plus SQL tenant/settings/config authority.
- Where does Configuration Authority begin? Backend settings/config service/controller admission.
- Where does DTO Admission begin? Backend accepted DTO emission plus frontend backend-reread/admission before presentation.
- Where does Presentation begin? Store Administration page/context rendering after admitted backend DTOs.
- Where does Cross-System propagation begin? After SQL commit via outbox/projection or current backend read consumption.
- Were runtime changes required? Yes.
- Is Global Business Configuration production-certifiable? Yes.
