# PASS SF-PA1.2 Store Configuration Read Authority Certification

Store Configuration Read Authority is production-certifiable after runtime read-admission and no-store hardening. Backend SQL `StoreConfig` plus SQL `Tenant` own Store Configuration, Store Identity, Company/Profile identity as tenant/store evidence, Branding, Currency, Tax/Locale/Messaging when modeled, Storefront Settings, SEO metadata, persistence, projection admission, and recovery truth. Frontend Storefront Settings, Dashboard cards, React state, React Query, browser cache/BFCache, local/session storage, route params, and formatting own presentation only.

## Read Findings

Admin reads enter `AdminStoreConfigController.GetStoreConfig`, trusted `ITenantContext.RequiredTenantId`, Owner/Admin authorization, and `AdminStoreConfigService.GetStoreConfigAsync`. Current SQL `StoreConfigs` is loaded by tenant; missing admin config is created at the backend service boundary only, then returned with `TenantId`, `RowVersion`, `StoreName`, `StorefrontStatus`, `ThemeId`, theme/behavior/Commerce AI settings, timestamps, and metadata. Public runtime reads enter `StorefrontStoreConfigController.GetStoreConfig`, trusted tenant context, SQL `StoreConfigs` joined to SQL `Tenants`, SQL tenant currency, live mapping through `StorefrontDraftConfigMapper.MapLive`, and fail-closed `404`/`503` behavior.

## Bootstrap Findings

Runtime bootstrap remains backend-owned from PASS SF-PA1.1. Storefront Settings and Dashboard render only after POS session bootstrap accepts backend-shaped tenant/session/capability evidence, then configuration reads re-enter backend GET authority. Cached configuration cannot bypass bootstrap.

## Store Identity Findings

Store Identity is backend-owned by trusted tenant/session context and SQL `StoreConfig.TenantId`. Frontend `tenantId`, routes, URLs, and active store presentation are candidates only. Frontend read admission now rejects store-config DTOs that lack matching backend tenant evidence, backend row-version evidence, valid store name, valid status, and backend theme id.

## Projection Findings

SQL is authoritative. Mongo, Redis, events, DTOs, React state, and browser cache are projection or presentation only. Public config reads use current SQL and fail closed when SQL config or SQL currency is unavailable. Projection rebuild can restore derivative read models, but presentation authority re-enters SQL/API reads.

## Cache Findings

Admin and public config GET responses are marked no-store. Frontend shared `storeConfigService` sends `Cache-Control: no-store` and `Pragma: no-cache` for admin and public runtime config reads. Settings and StatusCard reject malformed or stale-shaped responses rather than repairing with frontend defaults.

## Recovery Findings

Browser refresh, app reload, server restart, Redis loss, Mongo rebuild, cache loss, network reconnect, tenant change, active-store change, and React remount converge by re-entering backend session and config GET authority. Redis/Mongo loss cannot remove SQL truth; browser cache cannot restore authority.

## Failure Findings

400, 401, 403, 404, 409, 422, 429, 500, timeout, and projection-unavailable paths fail closed for configuration authority. 401/403 from config endpoints trigger POS authority-loss revalidation. Failed or malformed reads clear current config presentation or show unavailable/error UI, not fallback truth.

## Concurrency Findings

Concurrent reads may observe the latest committed SQL row available to each request. RowVersion is returned as read evidence and required for mutations outside this pass. Stale in-flight Settings/StatusCard reads are sequence-fenced so older completions cannot replace newer accepted backend evidence.

## Adversarial Findings

Modified StoreId, TenantId, Currency, Branding, Company, React state, cached responses, replayed reads, and injected configuration cannot fabricate Store Configuration. Frontend admission rejects foreign tenant DTOs, missing rowVersion, missing themeId, blank store name, and invalid status.

## Cross-System Findings

POS admin Settings, Storefront runtime, Checkout/cart currency consumers, Commerce AI, Affiliate display, SEO, Reports/Analytics, Theme metadata, CMS metadata, navigation metadata, and Auth Provider configuration reads remain backend/API-derived. Different modules can have transient stale presentation only; current truth converges by re-entering their backend SQL/service read boundary.

## Authority Boundaries

SQL Boundary: `StoreConfigs` and `Tenants` own configuration, identity, currency, persistence, and recovery. Failure withholds or returns backend error.

Configuration Boundary: `AdminStoreConfigService` and public `StorefrontStoreConfigController` map SQL to DTOs. Failure does not synthesize frontend config.

Projection Boundary: events/Mongo/Redis are derivative and rebuildable. Recovery source is SQL.

API Boundary: admin/public config controllers own read admission and no-store response behavior. Failure is 4xx/5xx/withheld presentation.

Cache Boundary: browser/React Query/memory/local/session storage are presentation only. Recovery source is backend re-read.

React Query Boundary: query cache can request/refetch only and cannot become authority.

Presentation Boundary: Settings, Dashboard, StatusCard, formatters, skeletons, and labels render accepted backend evidence only.

Recovery Boundary: backend SQL/session/config services restore truth; frontend lifecycle events trigger revalidation.

## Runtime Read Pipeline

SQL `StoreConfigs`/`Tenants` -> `AdminStoreConfigService` or `StorefrontStoreConfigController` -> Store Configuration Authority -> Configuration API -> DTO with tenant and row-version evidence -> frontend read-admission normalization -> React/component state -> presentation.

Configuration Authority begins at backend SQL plus trusted tenant/session service boundary. Projection begins after backend commits/events produce derivative documents or DTOs. Presentation begins only after frontend accepts backend-shaped DTO evidence.

## Runtime Hardening Performed

- Admin and public Store Config GET endpoints now emit no-store response metadata.
- Shared frontend config service now sends no-store/no-cache headers for admin and public runtime config GETs.
- Store config presentation admission now requires matching tenant, store name, valid status, theme id, and rowVersion.
- Storefront Settings now starts with no local config truth, rejects malformed reads, clears presentation on failure, and sequence-fences stale completions.
- Storefront StatusCard now reads through the shared no-store service and renders accepted backend evidence without `live`/`My Store` fallback truth.

## Exact Runtime Files Modified

- `PointOfSale.API/Controllers/AdminStoreConfigController.cs`
- `PointOfSale.API/Controllers/StorefrontStoreConfigController.cs`
- `PointOfSale.Web/src/auth/commerceHubVendorIdentityAuthority.js`
- `PointOfSale.Web/src/components/StorefrontStatusCard.jsx`
- `PointOfSale.Web/src/modules/storefront/services/storeConfigService.js`
- `PointOfSale.Web/src/pages/StorefrontSettings.jsx`

## Regression Freezes Added

- `PointOfSale.Web/src/auth/commerceHubVendorIdentityAuthority.test.ts`
- `PointOfSale.Web/src/modules/storefront/services/storeConfigService.test.ts`
- `PointOfSale.Tests/Architecture/StorefrontConfigurationAuthorityCertificationTests.cs`

## Architecture Documentation Updates

This document records PASS SF-PA1.2.

## AI Context Updates

Updated `.ai-context/PROJECT_CONTEXT.md`, `.ai-context/ARCHITECTURE_TRUTH.md`, `.ai-context/KNOWN_RISKS.md`, `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`, and `.ai-context/CODEX_RULES.md`.

## Explicit Certification Answers

Who owns Store Configuration? Backend SQL `StoreConfig` plus owning backend services.

Who owns Currency? Backend SQL `Tenant.Currency` and backend currency consumers.

Who owns Company Identity? Backend tenant/store profile authority.

Who owns Store Identity? Backend trusted tenant context plus SQL `StoreConfig.TenantId`.

Can frontend invent configuration? No.

Can React Query become authority? No.

Can browser cache override backend? No.

Can StoreId become authority? No.

Can stale projections survive recovery? Not as authority; projections rebuild from SQL and reads re-enter backend authority.

Can different modules observe different configuration? They can transiently observe stale presentation, but not different authority; convergence is backend SQL/API re-entry.

Where does Configuration Authority begin? Backend SQL plus trusted backend service/API boundary.

Where does Projection begin? After backend persistence/events/DTO mapping as derivative evidence.

Where does Presentation begin? Frontend after accepted backend-shaped DTO evidence.

Were runtime changes required? Yes.

Is Store Configuration Read Authority production-certifiable? Yes.
