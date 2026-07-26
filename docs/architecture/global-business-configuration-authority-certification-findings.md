# PASS SF-PA1.5 Global Business Configuration Authority Certification

Global Business Configuration Authority is production-certifiable after POS settings mutation and shared settings presentation hardening. Backend trusted tenant context, authenticated Owner/Admin role authorization, SQL `SystemSettings`, SQL `Tenant.Currency`, SQL `StoreConfig`, backend tax/currency/config services, outbox events, and current backend read admission own Currency, Tax, Company Identity, Business Identity, Store Identity, Store Branding, Locale/Country, Business Profile, Messaging Defaults, persistence, projection policy, recovery, and snapshot policy.

Frontend settings pages, SettingsContext, Storefront Settings, React state, React Query, browser cache/BFCache, local/session storage, route params, form fields, previews, formatters, receipt preview, and navigation own editing intent and presentation only. They cannot redefine business configuration, replace shared settings authority optimistically, repair stale configuration, or recover authority from browser/projection/cache state.

## Business Findings

Global business configuration is split across backend-owned SQL authorities: tenant/business identity and currency live on `Tenants`; company profile, logo, receipt messaging, and tax defaults live in tenant-scoped `SystemSettings`; storefront identity/branding/Commerce AI defaults live in `StoreConfigs`. Runtime hardening now requires Owner/Admin role authority for global settings mutations in `SettingsController.UpdateSettings` and locale/country mutation in `SetDefaultCountry`. Storefront configuration mutations were already Owner/Admin, row-version, and service-bound from SF-PA1.4.

## Currency Findings

Currency is backend-owned. POS/store currency authority begins at `SettingsController.UpdateSettings`, tenant-scoped settings validation, ISO currency normalization through backend currency guards, SQL `Tenant.Currency` persistence, and `TenantCurrencyChangedEvent` outbox staging. Storefront/public reads use backend SQL tenant currency. Affiliate payout eligibility resolves current store base currency from backend SQL. CommerceHub remains intentionally platform-EGP authority and rejects tenant currencies outside `FinancialCurrencyGuard.PlatformBaseCurrencyCode`.

## Tax Findings

Tax authority is backend-owned through tenant-scoped `SystemSettings` and `TaxCalculationService`. Tax rates are persisted invariantly and read by tenant scope. Orders and invoices preserve tax amounts/rates as persisted transactional evidence; later tax setting changes do not rewrite historical order, invoice, receipt, report, or analytics truth.

## Company Findings

Company identity, logo, contact profile, and receipt messaging defaults are backend-owned by tenant-scoped `SystemSettings` with trusted tenant context. Frontend file selection and form editing are candidate intent; accepted company identity begins only after backend save and reread.

## Snapshot Findings

Historical business truth is immutable at the transaction boundary. Storefront orders persist `Order.Currency`, `Order.TaxAmount`, `Order.PricingSnapshotJson`, and line-level price/tax/commission/shipping/payment snapshots. POS sales invoices persist invoice-level tax totals and line-level tax rates/amounts. Reports and analytics must consume persisted historical rows for historical periods, not current live configuration.

## Projection Findings

Projection begins after backend persistence/events. `TenantCurrencyProjectionConsumer` resolves current SQL tenant currency instead of trusting event payload currency. `StorefrontConfigProjectionConsumer` re-enters `TenantLineageAuthority.LoadCurrentStoreConfigProjectionAuthorityAsync` and uses Redis/Mongo/search as derivative availability. Rebuilds restore from SQL authority; projection never becomes authority.

## Recovery Findings

Server restart, browser refresh, Redis loss, Mongo rebuild, search rebuild, React remount, and delayed responses converge through backend SQL/session/config/settings reads. Shared frontend settings no longer replace global application settings optimistically before backend acceptance; failed writes leave previous shared presentation intact.

## Failure Findings

400, 401, 403, 404, 409, 422, 500, timeout, and projection failures fail closed. Unauthorized roles cannot mutate global business settings. Failed or malformed settings/config reads are unavailable presentation, not fallback authority. Timeout-after-commit recovers by rereading backend SQL truth.

## Cross-System Findings

POS, Storefront, Checkout, Affiliate, CommerceHub, Reports, Analytics, Commerce AI, Shipping, Payments, SEO, and Navigation consume either current backend configuration or immutable transaction snapshots. Different modules may transiently present stale data, but they do not own conflicting authority; convergence requires backend reread or rebuild from SQL.

## Business Pipeline

Business Configuration -> Backend Authority -> Persistence -> Projection -> Configuration Consumers -> Presentation.

- Business Authority begins at trusted backend tenant/session plus Owner/Admin mutation boundaries: `SettingsController`, `AdminStoreConfigController`, `AdminStoreConfigService`, SQL `SystemSettings`, `Tenants`, and `StoreConfigs`.
- Snapshot Policy begins when checkout/POS invoice/order services commit transaction rows with currency, tax, pricing, identity, shipping, payment, and commission evidence.
- Projection begins after SQL commit/outbox/event consumers copy current backend evidence into Redis, Mongo, search, or DTOs.
- Presentation begins only after frontend accepts backend-shaped settings/config/order/report DTO evidence.

## Boundary Review

- Business Boundary: backend tenant-scoped SQL/settings/config services own authority; SQL is recovery source; failure is 4xx/5xx or withheld presentation.
- Currency Boundary: backend `Tenant.Currency`/currency guards/platform EGP rules own authority; SQL/outbox is recovery source; invalid/stale currency fails closed.
- Snapshot Boundary: order/invoice rows own historical truth; persisted rows are recovery source; live config changes cannot rewrite history.
- Projection Boundary: event consumers/read models copy backend truth; SQL is recovery source; projection loss or staleness cannot author truth.
- Recovery Boundary: backend SQL/session/config/settings reads restore truth; browser/cache/projection state only triggers revalidation.
- Consumer Boundary: POS/checkout/reports/analytics/affiliate/CommerceHub/AI consume backend or snapshot evidence; failures withhold, refetch, or reject.
- Configuration Boundary: Owner/Admin settings/config mutation APIs own acceptance; frontend owns candidate intent only.

## Runtime Hardening Performed

- Added Owner/Admin authorization to POS global settings mutation and default-country/locale mutation endpoints.
- Added no-store response metadata to POS global settings and default-country reads.
- Added no-store/no-cache frontend request hints for global settings reads/writes.
- Removed optimistic replacement of shared `SettingsContext` currency/tax/company values before backend acceptance and reread.

## Exact Runtime Files Modified

- `PointOfSale.API/Controllers/SettingsController.cs`
- `PointOfSale.Web/src/contexts/SettingsContext.jsx`
- `PointOfSale.Web/src/pages/Settings.jsx`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/GlobalBusinessConfigurationAuthorityCertificationTests.cs`

## Architecture Updates

Added this SF-PA1.5 findings document.

## AI Context Updates

Updated `.ai-context/PROJECT_CONTEXT.md`, `.ai-context/ARCHITECTURE_TRUTH.md`, `.ai-context/KNOWN_RISKS.md`, `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`, and `.ai-context/CODEX_RULES.md`.

## Explicit Certification Answers

Who owns Currency? Backend SQL `Tenant.Currency` for POS/store currency; CommerceHub platform currency remains backend `FinancialCurrencyGuard.PlatformBaseCurrencyCode`.

Who owns Tax? Backend tenant-scoped `SystemSettings` and backend tax services; historical tax is persisted on orders/invoices.

Who owns Company Identity? Backend tenant-scoped `SystemSettings` plus SQL tenant/store evidence.

Who owns Business Identity? Backend trusted tenant context, SQL `Tenants`, SQL `SystemSettings`, and SQL `StoreConfigs`.

Can frontend redefine business configuration? No.

Can Currency rewrite historical orders? No.

Can Tax rewrite historical invoices? No.

Can Reports observe conflicting business truth? No authoritative conflict; reports consume persisted snapshots/current backend reads and may only transiently present stale data.

Can Checkout observe stale configuration? It may receive stale client presentation, but final authority re-enters backend/current SQL/snapshot boundaries; stale frontend configuration is not checkout authority.

Where does Business Authority begin? Backend trusted tenant/session plus Owner/Admin configuration mutation/read service boundaries.

Where does Snapshot Policy begin? At backend checkout/POS invoice/order commit when historical currency, tax, pricing, and business evidence is persisted.

Where does Projection begin? After backend SQL commit/outbox/event publication into Redis, Mongo, search, or DTO read models.

Were runtime changes required? Yes.

Is Global Business Configuration production-certifiable? Yes.
