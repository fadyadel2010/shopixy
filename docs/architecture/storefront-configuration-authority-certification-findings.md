# PASS P2-GA Storefront Configuration Authority

Date: 2026-06-09

Status: production-certifiable for the inspected runtime after hardening.

## Authority Decision

Tenant-owned PostgreSQL is the authority for storefront configuration:

- `StoreConfig` owns store name, branding, SEO metadata, behavior settings, feature flags,
  publication state, and draft/live configuration.
- `Tenant` owns tenant lifecycle, slug, custom storefront origin, and currency.
- Redis, MongoDB, memory caches, events, themes, drafts, projections, DTOs, route values, request
  hosts, and browser state are derivative evidence only.
- Storefront language is not persisted as store configuration in the current model. Locale remains
  a client presentation/search preference and cannot change identity, prices, currency, checkout,
  payment, or order authority.

Theme, CMS, navigation, version, checkout, payment, and order ownership remain outside P2-GA and
retain their previously certified authorities.

## Findings

### Store Identity

Public store configuration and affiliate store labels now read current SQL `StoreConfig` truth.
The public endpoint no longer trusts Redis/Mongo or synthesizes a live default. Trusted tenant
middleware supplies tenant identity; `X-Tenant-Id` cannot override storefront context.

### Brand

Store name, logo, favicon, colors, and brand metadata are read from current SQL live configuration.
Draft content is not exposed by the public configuration endpoint. A stale projection cannot
override branding.

### Domain

`Tenant.StorefrontBaseUrl` and `Tenant.Slug` are SQL-owned. Resolved origins must be absolute
HTTP(S) origins without credentials, path, query, or fragment. Custom-host resolution checks at
most two SQL matches and fails closed unless exactly one active tenant owns the host, preventing
ambiguous cross-tenant selection.

There is no tenant-facing custom-domain attachment API in the inspected runtime. Custom origins
are provisioned through privileged control-plane/SQL administration; therefore an unverified
tenant request cannot attach a foreign domain. A future self-service domain feature requires
separate ownership-challenge and uniqueness hardening before release.

### SEO

SEO metadata, canonical links, sitemap URLs, and robots URLs ignore caller-supplied origins and
resolve the current SQL tenant origin through `StorefrontUrlResolver`. Public SEO requires a
current active, non-deleted tenant.

### Language

No backend store-language authority exists. Language parameters affect presentation/search only.
They do not alter SQL configuration or financial authority. Adding default/supported languages
later requires a tenant-owned SQL model and P2-GA freeze updates.

### Currency

Storefront currency derives from current SQL `Tenant.Currency`. Cart, landing configuration,
Commerce AI context, and public store configuration no longer use projected currency or silent
USD/EGP fallback. Financial and checkout authorities continue to revalidate currency under their
completed certifications.

### Feature Flags

Publication status, promotion ranking, Commerce AI enablement, and behavior settings derive from
SQL `StoreConfig`. Missing or failed feature configuration fails closed. Redis may receive a
derivative promotion-ranking copy but is not read as authority.

### Caching And Projections

The public configuration, publication, currency, feature, SEO-origin, and store-identity paths
re-read SQL. MongoDB and Redis remain rebuildable projections/caches. The SQL projection consumer
already reloads current tenant/config state, applies freshness fencing, and invalidates cache.
No cache hit can make a store live or restore deleted/stale configuration.

### Configuration Updates

The primary admin configuration controller remains trusted-tenant, Owner/Admin restricted, and
row-version protected. The Commerce AI shadow write path was hardened to the same Owner/Admin and
`If-Match` concurrency requirements, no longer creates missing configuration, and stages the
configuration event in the SQL unit of work.

### Configuration Consumption

Storefront runtime, cart, landing page, SEO, publication guard, chat, Commerce AI context, and
affiliate identity display now consume SQL authority for configuration-owned values. Checkout,
payment, order, customer, shipping, promotions, and financial consumers remain bound to P2-A
through P2-F and P1 financial authority.

### Tenant Isolation

Reads and writes are tenant-predicated. Public tenant resolution is host/slug based against active
SQL tenants and rejects ambiguous custom-host ownership. Admin writes derive tenant identity from
trusted middleware. Request headers, DTO tenant ids, projections, and guessed routes do not grant
foreign configuration access.

### Concurrency And Idempotency

Configuration updates require current row version. Stale and concurrent writes receive conflict or
precondition failure instead of overwriting newer truth. Publish remains lock/row-version protected.
Projection replay reloads current SQL state rather than applying event payload as authority.

### Historical Consistency

Public runtime serves the live SQL branch only. Drafts cannot replace live truth until an
authorized publish. Missing configuration fails closed instead of resurrecting generated defaults.
Deleted/inactive tenant state blocks publication and SEO visibility. Projection replay cannot
restore an older configuration over current SQL.

### Adversarial Review

The inspected runtime blocks configuration DTO tampering, tenant-header substitution, domain
ambiguity, caller-controlled canonical origins, projected identity/currency spoofing, cache
poisoning as authority, fail-open feature flags, stale write replay, and draft/live substitution.

## Runtime Hardening Performed

- Replaced cache/projection-first public configuration with current SQL live mapping.
- Replaced projected publication, feature-flag, branding, store-name, Commerce AI, and currency
  decisions with SQL reads.
- Removed synthetic live defaults and currency fallbacks.
- Forced SEO origins through the SQL storefront URL resolver.
- Restricted storefront tenant context to trusted middleware.
- Rejected ambiguous custom-domain matches and validated origins.
- Hardened the Commerce AI configuration write route with role and optimistic-concurrency checks.
- Kept caches write-only/derivative on the inspected promotion path.

## Exact Runtime Files Modified

- `PointOfSale.API/Controllers/AdminCommerceAiController.cs`
- `PointOfSale.API/Controllers/AffiliateAnalyticsController.cs`
- `PointOfSale.API/Controllers/StorefrontChatController.cs`
- `PointOfSale.API/Controllers/StorefrontLandingPageController.cs`
- `PointOfSale.API/Controllers/StorefrontStoreConfigController.cs`
- `PointOfSale.API/Helpers/StorefrontTenantContextResolver.cs`
- `PointOfSale.API/Middleware/TenantResolutionMiddleware.cs`
- `PointOfSale.API/Services/AI/CommerceContextBuilder.cs`
- `PointOfSale.API/Services/Cart/CartService.cs`
- `PointOfSale.API/Services/Storefront/PromotionRankingService.cs`
- `PointOfSale.API/Services/Storefront/SeoService.cs`
- `PointOfSale.API/Services/Storefront/StorefrontDraftConfigMapper.cs`
- `PointOfSale.API/Services/Storefront/StorefrontPublicationStatusService.cs`
- `PointOfSale.API/Services/StorefrontUrlResolver.cs`

## Regression Freezes

- `PointOfSale.Tests/Architecture/StorefrontConfigurationAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/StorefrontCurrencyAuthorityArchitectureTests.cs`

## Explicit Answers

- Who owns storefront configuration authority? Tenant-owned PostgreSQL `StoreConfig` and `Tenant`.
- Can store identity be spoofed? No, inspected runtime identity is re-read from SQL.
- Can domains be hijacked? Not through inspected tenant/runtime APIs; ambiguous ownership fails
  closed and custom origin mutation is privileged control-plane work.
- Can storefront currency diverge from authority? No, inspected runtime reads `Tenant.Currency`.
- Can feature flags bypass authority? No, SQL decides and missing/error state fails closed.
- Can caches become authority? No.
- Can stale configuration resurrect? No, public runtime reads current SQL live truth.
- Can tenant leakage occur? No inspected path permits it.
- Can replay overwrite configuration truth? No, row versions protect writes and projection replay
  reloads SQL.
- Were runtime code changes required? Yes.
- Is Storefront Configuration Authority production-certifiable? Yes, for the inspected runtime and
  current pre-provisioned domain model.

