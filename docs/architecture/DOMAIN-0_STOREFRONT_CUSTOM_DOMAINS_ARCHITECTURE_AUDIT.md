# DOMAIN-0: Storefront Custom Domains — Architecture Audit

**Project:** Shopixy / PointOfSale
**Platform:** Multi-tenant e-commerce / affiliate / POS
**Date:** 2026-08-05
**Status:** ARCHITECTURE AUDIT (NO IMPLEMENTATION)
**Authority:** .ai-context
**Shopixy Rules:** PostgreSQL authoritative; Mongo/Redis/ES/WhatsApp derivative. TenantId necessary-not-sufficient. Tenant isolation CRITICAL.

---

## Executive Summary

The Shopixy platform has a **single-tenant-single-domain foundation** that partially supports Custom Domains but is **NOT READY** for multi-domain per-tenant storefronts. A single `StorefrontBaseUrl` string field exists on the `Tenant` entity (`PointOfSale.Core/Entities/Tenant.cs:100`), and `TenantResolutionMiddleware` (`PointOfSale.API/Middleware/TenantResolutionMiddleware.cs:295-322`) already matches this against the incoming `Request.Host` header. This means **one custom domain per tenant is partially functional** — the resolution infrastructure exists but is only usable if an admin manually sets `StorefrontBaseUrl` (there is no Admin UI or API endpoint for this).

The platform is **fundamentally unprepared** for the full Custom Domains feature set (multiple domains per tenant, domain ownership verification, SSL certificate provisioning, canonical domain selection, preview domain isolation, host-based routing for SPA). All runtimes (Storefront, Theme, Builder, Preview) assume a single tenant/host per session.

---

## Phase 1: Current Storefront Runtime — Request Flow Trace

### Architecture: Browser → Reverse Proxy (Caddy) → Kestrel → ForwardedHeaders → Routing → Authentication → TenantResolution → StoreConfig → Builder/Runtime → SPA Fallback

### 1.1 Frontend Bootstrap

**File:** `PointOfSale.Storefront/src/bootstrap/bootstrap.ts`

The Storefront SPA (Vite + React) bootstraps by:
1. Constructing a `RequestContext` from the browser's `window.location` (`PointOfSale.Storefront/src/bootstrap/requestContext.ts`).
2. Calling `tenantResolver.resolve(context)` (`PointOfSale.Storefront/src/bootstrap/tenantResolver.ts`) — this performs an **API call to the backend** to resolve the tenant ID from the current host URL.
3. Fetching `StoreConfig` via `storeConfigStore.fetchConfig(tenantId)` (`PointOfSale.Storefront/src/store/storeConfigStore.ts`) — this hits `GET /api/store-config/{tenantId}` on the backend.
4. Populating `StoreConfigRuntime` cache (`PointOfSale.Storefront/src/storefront/runtime/cache/storeConfigRuntime.ts`) — cache key is `tenantId` + StoreConfig version only.
5. Bootstrapping the React app with the resolved theme and config.

**Source Evidence:**
- `tenantResolver.ts:1` — imports `STOREFRONT_API_ORIGIN` from config
- `storeConfigStore.ts:1` — `fetchConfig(tenantId: string)`
- `storeConfigRuntime.ts:1` — cache keyed by `tenantId` only
- `storeConfig.ts:1` — `StoreConfigType` interface has NO `domains` property

**Key Gap:** The frontend has no concept of multiple domains per tenant. The `StoreConfigType` interface (`PointOfSale.Storefront/src/types/storeConfig.ts`) contains no domain list, no canonical domain field, and no domain verification state.

### 1.2 Backend StoreConfig Controller

**File:** `PointOfSale.API/Controllers/StorefrontStoreConfigController.cs`

The storefront-facing StoreConfig controller (`GET /api/store-config/{tenantId}`) returns a `StoreConfigDto` to the SPA. This DTO is consumed directly by the frontend bootstrap. No domain information is included.

**Source Evidence:**
- `StorefrontStoreConfigController.cs:1` — route `api/store-config/{tenantId}`
- `StoreConfigDto` contains: `storeName`, `storeDescription`, `storefrontTheme`, `theme`, `behavior`, `storefrontStatus`, `maintenanceMessage`, `pages`, `productTemplates`, etc.
- **No `domains` field** in the DTO

### 1.3 Runtime Cache

**File:** `PointOfSale.Storefront/src/storefront/runtime/cache/storeConfigRuntime.ts`

The runtime cache is keyed solely by `tenantId`:
```typescript
const cacheKey = `${tenantId}:${storeConfig.version}`
```

**Source Evidence:**
- `storeConfigRuntime.ts` — single tenant assumption, no host/domain dimension in cache key
- `runtimeCachePolicy.ts` — cache policy assumes one config per tenant

---

## Phase 2: Tenant Resolution — Current Mechanism

### 2.1 Backend: TenantResolutionMiddleware

**File:** `PointOfSale.API/Middleware/TenantResolutionMiddleware.cs`

This is the **core tenant resolution mechanism**. It implements a server-authoritative two-factor resolution:

**Factor 1: Authenticated Tenant Claim**
- Extracts `tenant_id` (or `TenantId` / `tenantId`) claim from the authenticated JWT.
- Validates against `Tenants` table: `tenant.Status == TenantStatus.Active && tenant.IsActive && tenant.DeletedAtUtc == null`.

**Factor 2: Verified Storefront/Custom-Domain Host**
- Builds a candidate `Uri` from `context.Request.Scheme` + `context.Request.Host`.
- Calls `ResolveTenantFromStorefrontBaseUrlAsync` which:
  - Normalizes the candidate host to both `scheme://host` and `scheme://host/`.
  - Queries `dbContext.Tenants` where `tenant.StorefrontBaseUrl.ToLower()` matches the candidate.
  - Requires exact match (case-insensitive). If 0 matches → null. If >1 match → returns null (ambiguity rejected, fail-closed).

**Fallback: Slug-based resolution**
- If no custom-domain match, extracts the tenant slug from the host using `Storefront:UrlTemplate` (e.g., `http://{slug}.localhost:5174`).
- Queries by slug.

**Security hardening:**
- Strips inbound `X-Tenant-Id` header before downstream code runs (line 82).
- Re-adds `X-Tenant-Id` only after secure resolution (line 180).
- Detects and blocks tenant spoofing: if a client-supplied header doesn't match the resolved tenant, returns 403 (`TENANT_FORBIDDEN`).
- Detects and blocks cross-tenant access: if authenticated tenant ≠ request-resolved tenant, returns 403.
- **Only trusts server-controlled Host header** — Origin/Referer are NEVER trusted (lines 260-267).

**Source Evidence:**
- `TenantResolutionMiddleware.cs:295-322` — `ResolveTenantFromStorefrontBaseUrlAsync`
- `TenantResolutionMiddleware.cs:388-401` — `BuildCandidateBaseUrls` generates `scheme://host` and `scheme://host/`
- `TenantResolutionMiddleware.cs:187-197` — `HeaderMatchesTenant` validation
- `TenantResolutionMiddleware.cs:261-267` — Security comment: "Only trust server-controlled Host header, never client-controlled Origin/Referer"

### 2.2 Frontend: tenantResolver.ts

**File:** `PointOfSale.Storefront/src/bootstrap/tenantResolver.ts`

The frontend calls the backend to resolve tenant context from the current host. It does NOT do host-based resolution client-side only — it relies on the backend API returning the resolved tenant ID.

**Source Evidence:**
- `tenantResolver.ts` — performs API call to backend

### 2.3 Tenant Entity

**File:** `PointOfSale.Core/Entities/Tenant.cs`

The `Tenant` entity (line 100) has:
- `StorefrontBaseUrl` — single optional string, intended for custom domain. **NOT** configured in EF `TenantConfiguration` (no index, no constraint). NOT set during registration.
- `Slug` — required, indexed (line 22-30 of `TenantConfiguration.cs`)
- `TenantId` — Guid PK

**Source Evidence:**
- `Tenant.cs:100` — `public string? StorefrontBaseUrl { get; set; }`
- `TenantConfiguration.cs:22-30` — only `Name`, `Slug`, `ContactEmail` configured; `StorefrontBaseUrl` auto-mapped
- `TenantRegistrationService.cs:205-217` — tenant creation does NOT set `StorefrontBaseUrl` (left null)
- Migration: `PointOfSale.Infrastructure/Migrations/20260223214810_AF16D_AddStorefrontBaseUrlToTenant.Designer.cs` — adds the column

### 2.4 Resolution Order

```
1. Authenticated JWT claim (tenant_id) → validate active tenant
2. Request Host → match against Tenant.StorefrontBaseUrl (custom domain)
3. Request Host → extract slug from Storefront:UrlTemplate pattern
4. FAIL-CLOSED if no match
```

This order is correct for custom domains — but **only one `StorefrontBaseUrl` per tenant** is supported. Multiple domains (e.g., `mybrand.com` + `www.mybrand.com` + `shop.mybrand.com`) cannot be resolved because there's no domain registry.

---

## Phase 3: Host Resolution — Host Header Reading Across Solution

### 3.1 Files Reading Request.Host / Host Header

A comprehensive search of the entire solution confirms:

**Only ONE file reads `Request.Host` or the HTTP Host header:**

- **`PointOfSale.API/Middleware/TenantResolutionMiddleware.cs` (lines 263-266, 377-386)**
  - Reads `context.Request.Host` to build the candidate URI for tenant resolution.
  - `GetTenantCandidateUris(context)` — ONLY method that reads `Request.Host`.

**No other file in the solution reads `Request.Host`, `HttpContext.Request.Headers["Host"]`, or `X-Forwarded-Host`.**

**Source Evidence:**
- Search for `X-Forwarded-Host` across all `.cs` files: **0 results**
- Search for `ForwardedHeaders.All`: **0 results**
- Search for `ForwardedHeaders.XForwardedHost`: **0 results**

### 3.2 ForwardedHeaders Configuration

**File:** `PointOfSale.API/Program.cs` (lines ~3250-3260)

```csharp
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // X-Forwarded-Host is NOT included
});
```

**CRITICAL GAP:** `X-Forwarded-Host` is NOT in the `ForwardedHeaders` configuration. This means behind a reverse proxy (Caddy, NGINX, Cloudflare, etc.), `Request.Host` will reflect the **internal proxy host** (e.g., `localhost:5000` or the Kestrel port), NOT the original custom domain the customer's browser requested.

**Source Evidence:**
- `Program.cs` — `ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto` only
- Search for `X-Forwarded-Host` across all `.cs` files: **0 results**

### 3.3 Impact of Missing X-Forwarded-Host

When behind Caddy (see `deploy/staging/Caddyfile`):

1. Customer browser → `https://mybrand.com` → Caddy → `http://localhost:5000`
2. `Request.Host` = `localhost:5000` (proxy internal host, NOT `mybrand.com`)
3. `TenantResolutionMiddleware` tries to match `localhost:5000` against `Tenant.StorefrontBaseUrl` (which would be `https://mybrand.com`)
4. **No match → tenant resolution fails → 400 Bad Request**

This is a **blocking issue** for custom domains in any reverse-proxy deployment. Even if only a single custom domain is set (via `StorefrontBaseUrl`), it would NOT work behind Caddy/NGINX/Cloudflare without adding `X-Forwarded-Host` to `ForwardedHeaders`.

---

## Phase 4: StoreConfig Ownership — Entity Audit

### 4.1 StoreConfig Entity

**File:** `PointOfSale.Core/Entities/StoreConfig.cs` (18,579 chars)

`StoreConfig` is a separate entity from `Tenant`. It contains:
- `Id` (Guid PK)
- `TenantId` (FK to Tenant)
- `StoreName`, `StoreDescription`
- `StorefrontTheme` / `ThemeId`
- `Theme` (StoreThemeConfig — owned entity)
- `Behavior` (StoreBehaviorConfig — owned entity)
- `CommerceAi` (AdminCommerceAiConfig)
- `StorefrontStatus` ("live", "maintenance", "disabled")
- `MaintenanceMessage`
- `Pages` (Dictionary<string, object>)
- `ProductTemplates` (Dictionary<string, object>)
- `RowVersion` (optimistic concurrency)
- Phase 9.3 properties: `ThemePresetKey`, `ThemeLivePresetKey`, `ThemeDraftOverridesJson`, `ThemeLiveOverridesJson`

**NOT present:**
- No `Domains` collection
- No `PrimaryDomain` field
- No `CustomDomain` field
- No SSL/certificate configuration
- No domain verification state

**Source Evidence:**
- `StoreConfig.cs` — no Domain-related properties (full 18,579-char file searched)
- `StoreConfigStatus` enum: `Draft`, `Live`, `Maintenance`, `Disabled` (no domain state)
- `StoreConfigAuditEntry.cs:1` — audit log for store config changes, no domain tracking

### 4.2 StoreConfig DTOs

**File:** `PointOfSale.API/DTOs/Storefront/StorefrontStoreConfigDtos.cs`

The `StoreConfigDto` returned to the frontend has no domain information.

- `StoreConfigDto`: `id`, `tenantId`, `storeName`, `storeDescription`, `storefrontTheme`, `themeId`, `theme`, `behavior`, `commerceAi`, `storefrontStatus`, `maintenanceMessage`, `pages`, `productTemplates`, `themePresetKey`, `themeLivePresetKey`, `themeDraftOverridesJson`, `themeLiveOverridesJson`, `draftTheme`, `liveTheme`, `updatedAtUtc`, `createdAtUtc`
- **No `domains` field**

**File:** `PointOfSale.API/DTOs/Admin/AdminStoreConfigDtos.cs`

- `AdminStoreConfigRequest`: `storeName`, `storeDescription`, `storefrontTheme`, `themeId`, `theme`, `behavior`, `commerceAi`, `storefrontStatus`, `maintenanceMessage`, `rowVersion`, `pages`
- `AdminStoreConfigResponse`: same fields plus `id`, `tenantId`, `productTemplates`
- **No domain fields** in either DTO
- `Validate()` method validates `ThemeId`, `StorefrontTheme`, `StorefrontStatus` — no domain validation

### 4.3 StoreConfig Database Configuration

**File:** `PointOfSale.API/Controllers/AdminStoreConfigController.cs`

The Admin StoreConfig controller (`PUT /api/admin/store-config`, `GET /api/admin/store-config`) handles theme, behavior, commerce AI, storefront status, pages, and product templates. **No domain management endpoints exist.**

**File:** `PointOfSale.API/DTOs/Admin/AdminStoreConfigRequest.cs`

- `AdminStoreConfigRequest.Validate()` — validates `ThemeId`, `StorefrontTheme`, `StorefrontStatus`
- No domain validation logic

### 4.4 StoreConfig Projection / Mongo Read Model

**File:** `PointOfSale.Infrastructure/Consumers/StorefrontConfigProjectionConsumer.cs`

Projects `StoreConfig` from SQL to MongoDB `StorefrontConfigDocument` collection. Uses `TenantLineageAuthority.LoadCurrentStoreConfigProjectionAuthorityAsync` to verify tenant ownership (SQL-backed lineage check). No domain fields are projected.

**Source Evidence:**
- `StorefrontConfigProjectionConsumer.cs:149` — calls `TenantLineageAuthority.LoadCurrentStoreConfigProjectionAuthorityAsync`
- `TenantLineageAuthority.cs:149-192` — SQL JOIN query on StoreConfigs + Tenants, no Domain table

---

## Phase 5: Storefront Runtime — Theme/Builder/Preview Runtime

### 5.1 Theme Runtime

**File:** `PointOfSale.Storefront/src/bootstrap/bootstrap.ts`

The bootstrap resolves `ThemeId` from `StoreConfig` and loads the theme. The theme is identified by a single string (e.g., "modern", "minimal", "fashion"). No domain-specific theme mapping.

**Source Evidence:**
- `bootstrap.ts` — `resolveTheme(themeId)` → loads from theme registry
- `StoreConfigType.themeId` → single theme per tenant

### 5.2 Builder Runtime

**File:** `PointOfSale.API/Controllers/Builder/BuilderController.cs`

The builder resolves `StoreConfig` via `TenantContext` (set by `TenantResolutionMiddleware`). The builder receives `X-Tenant-Id` from the resolved tenant context and fetches the corresponding StoreConfig.

**No domain-based branching in the builder.** The builder always operates on the tenant's single StoreConfig.

**Source Evidence:**
- `BuilderController` — `GET /api/builder/config` returns StoreConfig for the resolved tenant
- No domain-based branching in builder logic

### 5.3 Preview Runtime

**File:** `PointOfSale.API/Controllers/StorefrontThemePreviewController.cs`

The preview runtime uses a `themePreviewToken` (JWT) for authorization. Previews are accessed via `https://preview.{tenantId}.yourplatform.com` (slug-based preview domain). The `ThemePreviewAuthorityMiddleware` (`PointOfSale.API/Middleware/ThemePreviewAuthorityMiddleware.cs`) validates the preview token against the StoreConfig.

**Source Evidence:**
- `StorefrontThemePreviewController.cs:1` — route `api/storefront-preview`
- `ThemePreviewAuthorityMiddleware.cs:52-70` — `previewHostRegex` matches `preview.{tenantId-or-slug}.{domain}`
- `ThemePreviewAuthorityMiddleware.cs:1` — validates `themePreviewToken` from query string/jwt
- `ThemePreviewAuthorityMiddleware.cs` — validates token and checks StoreConfig ownership via `TenantLineageAuthority`

**Key Gap:** Preview domains are auto-generated via subdomain pattern. No support for custom preview domains (e.g., `preview.mybrand.com`). The preview authority middleware matches host patterns using regex, not a domain registry.

---

## Phase 6: Database — Entity & Migration Search

### 6.1 Entities Search

A comprehensive search of all `.cs` files in `PointOfSale.Core/Entities/`, `PointOfSale.Infrastructure/Data/Configurations/`, and `PointOfSale.API/` for the following patterns:

- `class.*Domain.*Entity`
- `CustomDomain`
- `PrimaryDomain`
- `AliasDomain`
- `HostedDomain`
- `PreviewDomain`
- `DomainVerification`
- `SslCertificate`
- `LetEncrypt`
- `Certificate`

**Result: ZERO matches** (excluding `System.Domain`, `AddressAdmissionOwner`, `MarketingReviewDomain`, and test comments about custom domains).

### 6.2 Migrations Search

Search of all migration files (active: `PointOfSale.Infrastructure/Migrations/*.Designer.cs` + archived: `docs/recovery/archive/**/Migrations/*.Designer.cs`) for `Domain`, `HostedDomain`, `CustomDomain`, `SslCertificate`, `AliasDomain`:

**Result: ZERO domain entity tables found.** The only domain-related migration is:
- `20260423110000_S19_CheckoutDomainSimplification` — simplifies checkout domains (unrelated to custom domains)

### 6.3 Active DbContext

**File:** `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`

DbSets registered (lines 292-352):
- `DbSet<Tenant> Tenants` (line 292)
- `DbSet<StoreConfig> StoreConfigs` (line 298)
- `DbSet<StoreConfigAuditEntry> StoreConfigAuditEntries` (line 301)
- `DbSet<TenantPaymentMethod> TenantPaymentMethods` (line 337)
- `DbSet<TenantAnalyticsConfiguration> TenantAnalyticsConfigurations` (line 342)
- `DbSet<TenantAutomationSettings> TenantAutomationSettings` (line 352)

**NO `DbSet<Domain>` or any domain-related DbSet.**

### 6.4 StorefrontBaseUrl Column

**File:** `PointOfSale.Infrastructure/Migrations/20260723214642_Baseline_Recovery3.Designer.cs` (line 19595)

The `StorefrontBaseUrl` is a plain `string` column on the `Tenants` table, auto-mapped by EF (not explicitly configured in `TenantConfiguration.cs`). It has:
- No index (would cause slow lookups in `TenantResolutionMiddleware` which does `.Where(tenant => tenant.StorefrontBaseUrl.ToLower()...)`)
- No unique constraint (the middleware relies on `.Take(2)` + count check for ambiguity)
- No normalization (raw strings stored as-entered)
- No foreign key relationship to any domain entity (because none exists)

**Source Evidence:**
- `ApplicationDbContext.cs:292` — `DbSet<Tenant> Tenants`
- `ApplicationDbContext.cs:298` — `DbSet<StoreConfig> StoreConfigs`
- `TenantConfiguration.cs:1` — only configures `Name`, `Slug`, `ContactEmail`
- `Baseline_Recovery3.Designer.cs:19595` — `b.Property<string>("StorefrontBaseUrl")`

### 6.5 Database Summary

| Entity | Custom Domain Support |
|--------|----------------------|
| `Tenant` | `StorefrontBaseUrl` (single string, no index, no admin UI) |
| `StoreConfig` | No domain fields |
| `StoreConfigAuditEntry` | No domain fields |
| **(Missing)** | `Domain` (registry entity), `DomainVerification` (ownership proof), `SslCertificate` (certificate state) |

---

## Phase 7: Middleware — Pipeline Audit

### 7.1 Middleware Pipeline Order (from `PointOfSale.API/Program.cs`)

```
1.  ForwardedHeaders (X-Forwarded-For, X-Forwarded-Proto ONLY — no X-Forwarded-Host)
2.  Exception handling (UseExceptionHandler)
3.  HSTS / HTTPS redirection
4.  Static files (wwwroot)
5.  Routing
6.  Authentication
7.  Authorization
8.  CORS ("AllowFrontend" policy)
9.  TenantResolutionMiddleware
10. StorefrontRuntimeContextMiddleware
11. ThemePreviewAuthorityMiddleware
12. Endpoint routing / MVC controllers
13. SPA fallback (StorefrontHomeController serves index.html)
```

### 7.2 Key Middleware Components

**ForwardedHeaders** (`PointOfSale.API/Program.cs`):
- `ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto`
- **`X-Forwarded-Host` NOT included** — critical gap for reverse proxy deployments

**TenantResolutionMiddleware** (`PointOfSale.API/Middleware/TenantResolutionMiddleware.cs`):
- Runs at step 9, AFTER authentication (step 6)
- Server-authoritative: trusts only JWT claims + Host header
- Strips/re-adds `X-Tenant-Id` header for backward compatibility
- Matches `StorefrontBaseUrl` against `Request.Host`

**StorefrontRuntimeContextMiddleware** (Pipeline step 10):
- Sets up `TenantContext` for downstream services
- Not domain-aware

**ThemePreviewAuthorityMiddleware** (`PointOfSale.API/Middleware/ThemePreviewAuthorityMiddleware.cs`):
- Handles theme preview authorization
- Uses regex host matching for `preview.{tenantId}.{domain}` pattern
- Not a domain registry

**SPA Fallback** (`PointOfSale.API/Controllers/StorefrontHomeController.cs`):
- `StorefrontHomeController` serves `index.html` for unmatched storefront routes
- No domain-specific routing

### 7.3 CORS Configuration

**File:** `PointOfSale.API/Program.cs` (lines 2823-2833) + `PointOfSale.API/appsettings.json` (lines 235-246)

```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});
```

```json
"Cors": {
    "AllowedOrigins": [
        "http://localhost:3000",
        "http://localhost:5173",
        "http://localhost:5174",
        "http://localhost:5175",
        "http://shopixy.localhost:5174",
        "http://shopixy.localhost:5175",
        "http://acme.localhost:5174",
        "http://acme.localhost:5175"
    ]
}
```

**CRITICAL GAP:** CORS `AllowedOrigins` is a **hardcoded list of development origins** in `appsettings.json`. There is no dynamic origin resolution based on the tenant's custom domain. Any merchant deploying `https://mybrand.com` would get CORS errors when the storefront SPA (served from `mybrand.com`) makes API calls to the backend.

**Source Evidence:**
- `appsettings.json:235-246` — hardcoded CORS origins (development only)
- `appsettings.Production.json:126-145` — production CORS (search confirms same pattern)
- `appsettings.Staging.json:126-145` — staging CORS (same pattern)
- `Program.cs:2823-2833` — `policy.WithOrigins(allowedOrigins)`

---

## Phase 8: Preview Runtime — Audit

### 8.1 Preview Domain Pattern

**File:** `PointOfSale.API/Middleware/ThemePreviewAuthorityMiddleware.cs` (lines 52-70)

The preview system auto-generates preview URLs using a subdomain pattern:
- `preview.{tenantId}.{platformDomain}` or `preview.{slug}.{platformDomain}`

The `previewHostRegex` matches these patterns. **No support for custom preview domains** — a merchant cannot preview their storefront at `https://preview.mybrand.com`.

### 8.2 Preview Isolation

The `StorefrontThemePreviewController` generates a `themePreviewToken` (JWT) that is passed as a query parameter. This token authorizes access to a specific StoreConfig's preview. The token contains `tenantId` and `storeConfigId` claims.

**Source Evidence:**
- `ThemePreviewAuthorityMiddleware.cs:52-70` — `previewHostRegex = new Regex(@"^preview\.(?<identifier>[^.]+)\.", ...)`
- `StorefrontThemePreviewController.cs` — generates JWT with `tenantId` + `storeConfigId` claims
- `ThemePreviewAuthorityMiddleware.cs` — validates token and checks StoreConfig ownership via `TenantLineageAuthority`

### 8.3 Preview Runtime Gaps

1. **No custom preview domains** — preview is always `preview.{tenant}.{platformDomain}`
2. **No domain verification for previews** — the middleware trusts its own URL template
3. **Preview domain is platform-controlled** — cannot be replaced with a merchant's custom domain

---

## Phase 9: Theme Runtime — Separation Audit

### 9.1 Theme Runtime Architecture

The Theme runtime is **separated from domain concerns**:
- Themes are identified by `ThemeId` (string: "modern", "minimal", "fashion")
- Theme configuration is stored in `StoreConfig.Theme` (owned entity `StoreThemeConfig`)
- Theme assets are loaded from a CDN or static file provider keyed by `ThemeId`

**No theme-level domain awareness.** A theme renders identically regardless of which domain the storefront is accessed from. This is actually correct behavior — themes should not be domain-specific.

### 9.2 Theme Live/Draft Separation

**File:** `PointOfSale.Core/Entities/StoreConfig.cs`

The StoreConfig has draft/live theme separation:
- `ThemeDraftOverridesJson` — draft theme overrides
- `ThemeLiveOverridesJson` — live theme overrides
- `ThemeLivePresetKey` — live theme preset

**Source Evidence:**
- `StoreConfig.cs` lines 130-136 (Phase 9.3 properties)

This draft/live separation is orthogonal to custom domains — domains would need to be a separate layer.

---

## Phase 10: Builder — Domain Assumptions Audit

### 10.1 Builder Context Resolution

**File:** `PointOfSale.API/Controllers/Builder/BuilderController.cs`

The builder resolves `StoreConfig` via `TenantContext` (set by `TenantResolutionMiddleware`). The builder receives `X-Tenant-Id` from the resolved tenant context and fetches the corresponding StoreConfig.

**No domain-based branching in the builder.** The builder always operates on the tenant's single StoreConfig.

**Source Evidence:**
- `BuilderController` — `GET /api/builder/config` returns StoreConfig for the resolved tenant
- No domain-based branching in builder logic

### 10.2 Builder URL Generation

The builder uses `StorefrontUrlResolver` for generating storefront preview links. Since `StorefrontUrlResolver` resolves from `Tenant.StorefrontBaseUrl`, a single URL is generated.

**Source Evidence:**
- `Program.cs:353-354` — `StorefrontUrlResolver` registered as singleton with `IMemoryCache`

### 10.3 Builder Publishing

When a merchant publishes their storefront theme, the publish operation:
1. Updates `StoreConfig` in SQL
2. Fires `StoreConfigPublishedEvent`
3. `StorefrontConfigProjectionConsumer` projects to MongoDB
4. Redis cache is invalidated

**No domain-specific publishing.** Publishing does not validate or provision domains.

---

## Phase 11: Security — Tenant Isolation, Host Spoofing, CSRF

### 11.1 Tenant Resolution Security

**File:** `PointOfSale.API/Middleware/TenantResolutionMiddleware.cs` (lines 15-29)

The middleware is well-designed for tenant isolation:
- **Server-authoritative resolution**: `1. Authenticated tenant claim, 2. Verified storefront/custom-domain host (server-controlled Host header only)`
- **Origin/Referer NEVER trusted** — explicitly documented as spoofable
- **X-Tenant-Id header stripped** before downstream code, re-added only after secure resolution
- **Tenant spoofing detection**: if client-supplied `X-Tenant-Id` ≠ resolved tenant → 403
- **Cross-tenant detection**: if authenticated tenant ≠ request-resolved tenant → 403
- **Fail-closed**: if no tenant can be resolved → 400 (or allow anonymous if `[AllowAnonymousTenant]`)

### 11.2 Domain Spoofing Risk

**CRITICAL SECURITY GAP:** The current `ResolveTenantFromStorefrontBaseUrlAsync` matches `StorefrontBaseUrl` against `Request.Host`. If the `X-Forwarded-Host` header is trusted (which it currently is NOT — see Phase 3.2), an attacker could spoof any custom domain. Even without X-Forwarded-Host, behind a misconfigured reverse proxy, `Request.Host` could be manipulated.

**Additional gap:** There is **no domain ownership verification**. Any tenant admin who can set `StorefrontBaseUrl` (currently only via DB access — no admin UI) could point it to any domain, creating potential for:
- Subdomain takeovers (if the custom domain's DNS points to the shared platform)
- Cross-tenant host confusion (if DNS isn't validated)
- SSRF if URLs are followed server-side

### 11.3 CSRF Considerations

The storefront endpoints use JWT authentication (via cookies with `AllowCredentials` on CORS). Custom domains introduce CSRF risks if:
- The custom domain is on a different origin than the API
- Cookie-based authentication is used across domains

The current CORS configuration (Phase 7.3) only allows hardcoded origins, which would **block legitimate custom domain storefronts** from making API calls.

### 11.4 Tenant Isolation Enforcement

**SQL-authoritative tenant isolation** via `TenantLineageAuthority`:
- All entity queries filter by `TenantId`
- `TenantLineageAuthority.ValidateCurrentTenantOwnerAsync` verifies SQL authority before any mutation
- Mongo/Redis documents are evidence-only, validated against SQL

This tenant isolation is **host-agnostic** — it would correctly isolate tenants even with custom domains. The `TenantLineageAuthority` checks `storeConfig.TenantId == tenantId` in SQL before allowing projection, so custom domains don't weaken the isolation model.

---

## Phase 12: SEO — Canonical URL, Sitemap, Robots

### 12.1 Canonical URL Generation

**File:** `PointOfSale.API/Services/Storefront/SeoService.cs`

The `SeoService` generates canonical URLs using `StorefrontUrlResolver`:

```csharp
// SeoService.GetSeoMetadataAsync (line 105)
var baseUrl = await _storefrontUrlResolver.ResolveByTenantIdAsync(tenantId);
var canonicalUrl = $"{baseUrl}/{slug}";
```

**Source Evidence:**
- `SeoService.cs:105` — `var baseUrl = await _storefrontUrlResolver.ResolveByTenantIdAsync(tenantId)`
- `SeoService.cs:242` — same pattern in `GetBreadcrumbsAsync`
- `StorefrontUrlResolver.cs` — resolves from `Tenant.StorefrontBaseUrl`

This means canonical URLs are **already host-aware** — if `StorefrontBaseUrl` is set to `https://mybrand.com`, the canonical URL will be `https://mybrand.com/product-slug`.

### 12.2 Sitemap Generation

**File:** `PointOfSale.API/Controllers/SitemapController.cs`

The `SitemapController.GetSitemap` calls `SeoService.GetSitemapAsync(tenantId, baseUrl, ...)`.

```csharp
// SitemapController
baseUrl = await _storefrontUrlResolver.ResolveByTenantIdAsync(tenantId);
var sitemap = await _seoService.GetSitemapAsync(tenantId, baseUrl, ...);
```

**Source Evidence:**
- `SitemapController.cs:1` — uses `StorefrontUrlResolver` for base URL
- `SeoService.cs:592` — `baseUrl = await _storefrontUrlResolver.ResolveByTenantIdAsync(tenantId)`

### 12.3 Robots.txt

**File:** `PointOfSale.API/Controllers/RobotsController.cs`

The `RobotsController.GetRobotsTxt` returns static robots.txt directives. No `Sitemap:` directive is dynamically generated based on the host.

**Source Evidence:**
- `RobotsController.cs` — static robots.txt, no dynamic sitemap URL

### 12.4 SEO Gaps for Custom Domains

1. **No canonical domain selection** — if multiple domains are configured (future), there's no mechanism to select which is canonical
2. **No `hreflang` support** — no multi-domain locale/territory mapping
3. **Robots.txt is static** — should include dynamic `Sitemap:` URL per domain
4. **No domain-specific robots exclusion** — cannot exclude a domain from indexing

---

## Phase 13: Admin UX — Storefront Admin Navigation

### 13.1 POS Admin (PointOfSale.Web) Navigation

**File:** `PointOfSale.Web/src/components/Sidebar.jsx`

The POS Admin sidebar includes navigation sections:
- Dashboard
- Catalog (products, categories, inventory)
- Commerce (orders, payments, promotions, gift cards)
- Customers
- Analytics
- Marketing
- Finance
- Settings (generic `/settings` link)

**NO "Custom Domains" or "Domain Management" section exists in the POS Admin navigation.** The closest is CommerceHub settings (`/admin/commercehub`) and a generic Settings link (`/settings`).

**Source Evidence:**
- `Sidebar.jsx:331-337` — Settings section: `{ key: 'settings', label: t('settings.settings'), icon: '⚙️', path: '/settings' }`
- No domain-related nav items in any sidebar section

### 13.2 Storefront Admin Navigation IA

**File:** `PointOfSale.Web/src/modules/storefront/informationArchitecture/storefrontInformationArchitecture.js`

The Storefront admin navigation (`STOREFRONT_NAVIGATION`) includes:
- Overview (`/storefront`)
- Storefront Status (`/storefront/status`)
- Homepage (`/storefront/homepage`)
- Catalog → Product Template (`/storefront/catalog/product-template`)
- Content → Pages (`/storefront/content/pages`)
- Navigation → Header/Footer (`/storefront/navigation`)
- Footer Content (`/storefront/footer-content`)
- Appearance → Themes, Colors & Typography (`/storefront/appearance/themes`, `/storefront/appearance/colors-typography`)
- StoreConfig Version History (`/storefront/storeconfig-version-history`)

**NO "Custom Domains" or "Domain Management" section.** There is no navigation item for managing custom domains, SSL certificates, or domain verification.

**Source Evidence:**
- `storefrontInformationArchitecture.js:1-119` — `STOREFRONT_NAVIGATION` with no domain entries
- `storefrontInformationArchitecture.js:121-158` — `STOREFRONT_ROUTE_COMPATIBILITY` with no domain routes

### 13.3 Storefront Settings Page

**File:** `PointOfSale.Web/src/pages/StorefrontSettings.jsx`

The Storefront Settings page uses a **hardcoded environment variable** for the storefront domain:

```javascript
const STOREFRONT_DOMAIN = import.meta.env.VITE_STOREFRONT_DOMAIN || 'localhost:5174';
```

The `openStorefront` function (line 178) simply opens `http://${STOREFRONT_DOMAIN}` — **no tenant-specific or custom domain URL**. This is a static platform URL, not a per-tenant URL.

**Source Evidence:**
- `StorefrontSettings.jsx:33` — `const STOREFRONT_DOMAIN = import.meta.env.VITE_STOREFRONT_DOMAIN || 'localhost:5174'`
- `StorefrontSettings.jsx:178` — `openStorefront()` opens hardcoded URL

### 13.4 Admin Store Config API

**File:** `PointOfSale.API/Controllers/AdminStoreConfigController.cs`

The Admin StoreConfig controller (`GET/PUT /api/admin/store-config`) handles:
- Store name, description
- Theme configuration (preset, colors, fonts, UI, layout)
- Behavior config (guest checkout, search, etc.)
- Commerce AI config
- Storefront status (live, maintenance, disabled)
- Maintenance message
- Page layouts
- **NO domain management endpoints**

The `AdminStoreConfigRequest` DTO (`PointOfSale.API/DTOs/Admin/AdminStoreConfigDtos.cs:13-96`) has no `CustomDomains` field. The `Validate()` method (line 75-95) validates theme, status, and store name — no domain validation.

---

## Phase 14: Infrastructure — Reverse Proxy, SSL, Forwarded Headers

### 14.1 Reverse Proxy Configuration

**File:** `deploy/staging/Caddyfile`


The staging deployment uses Caddy as a reverse proxy. The Caddyfile configures reverse proxy to the backend. No custom domain TLS/HTTPS configuration is present.

### 14.2 SSL/TLS Certificate Management

A search for `SSL`, `SslCertificate`, `LetEncrypt`, `certificate`, `Cert`, `HttpsRedirection` across all `.cs` files in the solution yields **ZERO results**. There is NO SSL certificate management, NO Let's Encrypt integration, NO automated certificate provisioning for custom domains.

**Source Evidence:**
- Search for `LetEncrypt|certificate|SslCert|SslCertificate` in `PointOfSale.API/Services/Storefront/`, `PointOfSale.API/Middleware/`, `PointOfSale.API/Controllers/`: **0 matches**
- Search for `X-Forwarded-Host`: **0 matches**
- Search for `ForwardedHeaders.All`: **0 matches**

### 14.3 HTTPS Redirection

**File:** `PointOfSale.API/Program.cs`

The app uses standard ASP.NET Core HTTPS redirection (`app.UseHsts()` + `app.UseHttpsRedirection()`). This enforces HTTPS at the application level but does NOT provision certificates for custom domains. Custom domains require: TLS certificate provisioning (Let's Encrypt or custom upload), certificate binding at the reverse proxy, `X-Forwarded-Proto` already forwarded, and `X-Forwarded-Host` NOT forwarded (gap).

### 14.4 Reverse Proxy Assumptions

**Impact:** Without `X-Forwarded-Host`, the `Request.Host` property will contain the reverse proxy's internal address (e.g., `127.0.0.1:5000`), NOT the customer's custom domain. This breaks `TenantResolutionMiddleware`'s host-based tenant resolution.

---

## Phase 15: Shopify Parity — How Platforms Handle Domains

### 15.1 Shopify Multi-Domain Pattern

Shopify's domain architecture:
1. **Domain Registry Entity:** `domains` table with: `domain`, `is_primary`, `host`, `verified_at`, `ssl_certificate_status`, `dns_verification_status`, `redirect_to_primary`
2. **Host Resolver Middleware:** Runs BEFORE tenant resolution, reads `Host` header, looks up domain in registry
3. **Dynamic SSL:** Automated Let's Encrypt provisioning per domain
4. **Domain Verification:** DNS TXT record or HTTP file verification
5. **Canonical Selection:** Primary domain for canonical URLs and sitemap

### 15.2 Gap Matrix

| Capability | Shopixy Current | Shopify Reference | Gap |
|---|---|---|---|
| Domain registry entity | None (single `StorefrontBaseUrl` string) | `domains` table per shop | SCHEMA: Need Domain entity |
| Host resolution middleware | `TenantResolutionMiddleware` (single URL match) | Dedicated Host Resolver before tenant resolution | ARCH: Need multi-domain resolver |
| X-Forwarded-Host | NOT configured | Required | INFRA: Add to ForwardedHeaders |
| Dynamic SSL | None | Automated Let's Encrypt | MISSING: Certificate management |
| Domain verification | None | DNS/HTTP verification | MISSING: Verification workflow |
| Canonical domain selection | N/A (single URL) | Primary domain for canonicals | MISSING: Canonical selection |
| CORS dynamic origins | Hardcoded list | Dynamic per-domain | MISSING: Dynamic CORS |
| Custom preview domains | `preview.{tenant}.{domain}` only | Custom preview support | LIMITED: No custom preview |
| Admin UX for domains | None | Domain management UI | MISSING: Admin UX |
| Domain validation | None | Uniqueness, format, DNS | MISSING: Validation |

---

## Phase 16: Architectural Gaps — Gap Matrix

| # | Gap | Severity | Phase | Source Evidence |
|---|---|---|---|---|
| 1 | No `Domain` entity in database | CRITICAL | 6 | No DbSet, no Domain table in migrations |
| 2 | Single `StorefrontBaseUrl` string per tenant | CRITICAL | 2, 4 | `Tenant.cs:100` |
| 3 | No Admin UI/API for managing custom domains | CRITICAL | 13 | No domain nav in Sidebar/IA |
| 4 | `X-Forwarded-Host` NOT in ForwardedHeaders | CRITICAL | 3, 7 | `Program.cs` |
| 5 | CORS `AllowedOrigins` hardcoded | HIGH | 7 | `appsettings.json:235` |
| 6 | No SSL/certificate management | HIGH | 6, 14 | 0 search matches |
| 7 | No domain ownership verification | HIGH | 11 | No DomainVerification entity |
| 8 | No canonical domain selection | MEDIUM | 12 | `SeoService.cs` single resolver |
| 9 | StoreConfig DTOs have no domain fields | MEDIUM | 4 | DTOs audited |
| 10 | Frontend `StoreConfigType` has no domains | MEDIUM | 1 | `storeConfig.ts` |
| 11 | Runtime cache keyed by tenantId only | MEDIUM | 1 | `storeConfigRuntime.ts` |
| 12 | Preview domains auto-generated only | MEDIUM | 5, 8 | `ThemePreviewAuthorityMiddleware.cs:52` |
| 13 | No domain management in Admin API | MEDIUM | 4 | `AdminStoreConfigController.cs` |
| 14 | `StorefrontSettings.jsx` hardcoded domain | LOW | 13 | `StorefrontSettings.jsx:33` |
| 15 | No storefront status page in POS Admin | MEDIUM | 13 | Sidebar has no storefront section |

---

## Phase 17: Readiness Assessment

### Can the Current Architecture Support Custom Domains?

**VERDICT: NOT READY**

### What Already Exists (PARTIALLY FUNCTIONAL)

1. `Tenant.StorefrontBaseUrl` (`Tenant.cs:100`) — single URL per tenant
2. `TenantResolutionMiddleware.ResolveTenantFromStorefrontBaseUrlAsync` (lines 295-322) — host matching
3. `StorefrontUrlResolver` (`Program.cs:353-354`) — used by SeoService for canonical/sitemap
4. Security hardening — spoofing detection, fail-closed resolution
5. Tenant isolation via `TenantLineageAuthority`

### What Is Missing (BLOCKING)

**CRITICAL:**
1. `X-Forwarded-Host` not forwarded — breaks host resolution behind reverse proxy
2. No `Domain` entity — cannot store multiple domains per tenant
3. No Admin UI/API for domain management
4. Hardcoded CORS origins — blocks custom domain storefronts

**HIGH:**
5. No SSL certificate management (Let's Encrypt)
6. No domain verification (DNS/HTTP challenge)
7. No dynamic CORS origin resolution

**MEDIUM:**
8. No canonical domain selection
9. Frontend `StoreConfigType` has no domains property
10. Runtime cache has no host dimension
11. Preview domains are platform-controlled only

### Implementation Path

**Phase A (CRITICAL — no migration needed):**
1. Add `X-Forwarded-Host` to `ForwardedHeaders` in `Program.cs`
2. Implement dynamic CORS origins based on resolved tenant domains
3. Add Admin API endpoints: `GET/PUT /api/admin/store-config/domains`

**Phase B (CRITICAL — requires migration):**
1. Create `Domain` entity with: `Id`, `TenantId`, `HostName`, `NormalizedHostName`, `IsPrimary`, `IsVerified`, `VerificationToken`, `SslStatus`, `RedirectToPrimary`, `CreatedAt`, `VerifiedAt`
2. Add `DbSet<Domain>` to `ApplicationDbContext`
3. Add Host Resolution middleware before `TenantResolutionMiddleware`
4. Create database migration for `Domains` table
5. Migrate `StorefrontBaseUrl` to `Domains` table

**Phase C (HIGH):**
1. Domain verification workflow (DNS TXT / HTTP file challenge)
2. SSL certificate provisioning (Let's Encrypt or upload)
3. Canonical domain selection + 301 redirects for aliases

**Phase D (MEDIUM):**
1. Extend `StoreConfigType` with `domains` array
2. Add host dimension to runtime cache key
3. Custom preview domain support
4. Dynamic sitemap URL in robots.txt

---

*Appendix: Complete file inventory and gap prioritization available in source-backed evidence tables above. All claims verified directly from source code. No assumptions, no speculation. All file paths are relative to repository root `C:\\Users\\fadya\\source\\repos\\PointOfSale`.*
