# Storefront Content Management — Reverse Engineered Capability

> Source of truth: implementation only. Every statement below is traced to a file in the
> `PointOfSale` solution. Where the implementation does NOT support a behavior, it is stated
> explicitly as "Not implemented" rather than inferred.

---

## Executive Summary

Shopixy's Storefront Content Management is **not** a single CRUD module. It is a
**Storefront Experience Composition Engine** built around a strict write-side (PostgreSQL,
draft/publish) → projection (MongoDB / Redis) → read-side (storefront + SEO + sitemap)
pipeline. The administrative screens (POS Admin, Super Admin, Marketplace CMS) only *author*
content. The runtime (Storefront SPA, `SeoService`, `StorefrontHomeService`,
`HomepageSectionReadService`, navigation/footer controllers) only *consumes* published,
tenant-scoped, sanitized derivatives.

The capability is composed of these independently-published content surfaces:

- **Homepage Sections** (`hero`, `banner`, `products`, `categories`) — draft/publish, schema-validated, cached in Redis, projected to a Marketplace MongoDB read model.
- **CMS Static Pages** (`CmsPage`) — RichText or Blocks content, draft/publish, version history, rollback, projected to MongoDB `storefront_pages`.
- **Storefront Navigation** (header + footer link menus) — read live from `StoreConfig.CustomizationJson`, recomposed against live category/CMS authority.
- **Storefront Footer** (`StorefrontFooterSection`/`StorefrontSocialLink`) — SQL-authoritative, tenant-or-global, with a derived fallback.
- **Store Identity / Theme / StoreConfig page-layout workspace** — `StoreConfig` entity is the single source of truth for store name, description, theme, behavior, and POS Admin builder JSON (`CustomizationJson.pages`). The public homepage renderer does not use the POS Admin `pages.home` layout; see Runtime Authority Divergence.
- **SEO** — `SeoService` produces metadata, breadcrumbs (with JSON-LD), canonical URLs, slug resolution; `SitemapController`/`RobotsController` are tenant-aware.
- **Media** — `ProductMedia` for product media only; `IMediaService` is the upload authority; homepage section images are arbitrary URLs (no upload pipeline in the homepage system).
- **CommerceHub Marketplace** — parallel but isolated content surfaces: `MarketplaceHomepageReadModelDocument`, `MarketplaceCmsPage`, `MarketplaceFooter`, all global-or-tenant scoped, governed by Super Admin.

---

## Business Purpose

To let a tenant (and the platform for the Marketplace) compose a branded, SEO-ready storefront
**without code changes**, via schema-bounded content blocks that are authored as drafts and
published atomically, with the runtime always reading a sanitized, cache-backed, tenant-isolated
projection of PostgreSQL business truth.

---

## Capability Scope

Implemented and in-scope (file evidence):

- Homepage composition: `PointOfSale.API/Services/Homepage/*`, `PointOfSale.Core/Entities/HomepageSection.cs`, `PointOfSale.API/Controllers/HomepageSectionsController.cs`, `PointOfSale.Storefront/src/pages/HomePage.tsx`, `PointOfSale.Storefront/src/hooks/useHomepageSections.ts`, `PointOfSale.CommerceHub/src/homepage/*`.
- Hero / Banner: schema types in `HomepageSectionSchemaRegistry.cs` (`hero`, `banner`).
- Sections / Featured Products / Collections: schema types `products`, `categories`; `StorefrontHomeService.cs`; `StorefrontHomeService` featured/new-arrivals/best-sellers logic.
- Navigation / Footer: `StorefrontNavigationController.cs` (live from `StoreConfig`), `StorefrontFooterController.cs`, `StorefrontFooterReadService.cs`, `StorefrontFooter*Document.cs`.
- Static Pages / CMS Pages: `PointOfSale.Core/Entities/CmsPage.cs`, `PointOfSale.API/Services/Cms/CmsPageService.cs`, `CmsPageReadService.cs`, `CmsPageContentValidator.cs`, `StorefrontPageDocument.cs`, `StorefrontPagesController.cs`.
- SEO: `PointOfSale.API/Services/Storefront/SeoService.cs`, `ISeoService.cs`, `SeoDtos.cs`, `SeoController.cs`, `SitemapController.cs`, `RobotsController.cs`.
- Media: `ProductMedia.cs`, `ProductMediaService.cs`, `IMediaService.cs`.
- Theme / Store Identity / Brand: `StoreConfig.cs`, `AdminStoreConfigController.cs`, `StorefrontThemePreviewTokenService.cs`, `StorefrontThemes.cs`.
- Publishing / Caching / Projection: `HomepageSectionCacheService.cs`, `CmsPageCacheService.cs`, `MarketplaceHomepageReadRepository.cs`, `HomepageProjectionConsumer.cs`, `CmsPageProjectionConsumer.cs`, `RedisInvalidationConsumer.cs`, `HomepageSectionEvents.cs`, `CacheInvalidationEvents.cs`.
- CommerceHub: `MarketplaceHomepageReadModelDocument.cs`, `MarketplaceHomepageReadRepository.cs`, `MarketplaceCmsPage.cs`, `MarketplaceCmsPageService.cs`, `MarketplaceCmsPageReadService.cs`, `MarketplaceFooter*`, `MarketplaceCmsPage*.cs`, `SuperAdminMarketplace*/` controllers & services.

---

## Business Terminology

- **Section**: A schema-validated homepage block (`hero`, `banner`, `products`, `categories`).
- **Draft vs Published**: Two boolean flags on `HomepageSection` and `CmsPage`. A row is never both; publishing flips `IsDraft=false, IsPublished=true`.
- **Scope / Tenant scope vs Global scope**: Homepage sections and footer support a `null` tenant (`TenantId == null`) = global fallback scope. CMS pages are strictly tenant-scoped.
- **SectionGroupId / PageGroupId**: Stable immutable identifier grouping all versions of a section/page; `Id` is the per-row primary key.
- **Read model / projection**: MongoDB documents (`StorefrontPageDocument`, `MarketplaceHomepageReadModelDocument`) holding LIVE-only content.
- **Builder**: The POS Admin "Storefront Builder" UI (`StorefrontBuilderPage.jsx`) that authors `StoreConfig.CustomizationJson.pages/*` draft/live page sections. This is distinct from the Super Admin `HomepageSection` engine.
- **Marketplace**: The CommerceHub storefront, isolated from tenant storefronts.

---

## Authority Matrix

| Concern | Authoritative owner | Write API | Read API | Notes |
|---|---|---|---|---|
| Homepage Sections (`HomepageSection`) | Super Admin (global + per tenant) via `SuperAdminHomepageSectionManagementService` | `SuperAdminHomepageSectionsController` | `HomepageSectionsController` (allow-anonymous, published) | Public homepage rendering authority. POS Admin does NOT own this entity. |
| StoreConfig page layouts (`CustomizationJson.pages.home/product/category`) | POS Admin (`Owner,Admin`) via Storefront Builder | `AdminStoreConfigController` → `AdminStoreConfigService` | `StorefrontStoreConfigController` maps live pages into `StorefrontStoreConfigDto`; `StorefrontConfigProjectionConsumer` maps live pages into Mongo `StorefrontConfigDocument` | Fully implemented author/publish/rollback pipeline. Product/category runtime pages consume it; public `HomePage.tsx` does NOT consume `pages.home`. |
| CMS Pages | POS Admin (`Owner,Admin` roles) via `CmsPageService` | `AdminCmsPagesController` (`[Authorize(Roles="Owner,Admin")]`) | `StorefrontPagesController` (allow-anonymous) | Tenant-scoped. |
| Marketplace CMS Pages | Super Admin via `MarketplaceCmsPageService` | `SuperAdminMarketplaceCmsPagesController` | `MarketplaceCmsPagesController` (allow-anonymous) | No tenant owner; platform-owned. |
| Navigation | POS Admin via `StoreConfig.CustomizationJson` | `AdminStoreConfigController` / storefront builder | `StorefrontNavigationController` (allow-anonymous) | Read from `CustomizationJson.navigation.live`. |
| Footer | POS Admin (tenant) / global seed | `AdminStorefrontFooterController`, `SuperAdminStorefrontFooterController` | `StorefrontFooterController` | SQL `StorefrontFooterSection`/`StorefrontSocialLink`. |
| Store Identity / Theme | POS Admin via `StoreConfig` | `AdminStoreConfigController` | storefront config endpoints | Single `StoreConfig` per tenant. |
| SEO metadata / sitemap / robots | Derived read-service (no authoring API) | N/A (computed from products/categories/CMS) | `SeoController`, `SitemapController`, `RobotsController` | Read-only; no CMS-authorable SEO records except per-page CMS meta. |
| Media (product) | POS Admin / catalog | `ProductMediaController` | product media reads | `IMediaService` is upload authority. |
| Marketplace Homepage / Footer / Branding | Super Admin | `SuperAdminMarketplace*` controllers | Marketplace public controllers | Isolated `marketplace_*` collections. |

**Authority boundaries confirmed:**
- `HomepageSection` **authoring** lives in `PointOfSale.API/SuperAdmin/...` — NOT POS Admin. `HomepageSectionsController` is `[AllowAnonymous]` and read-only (published or preview-for-SuperAdmin).
- POS Admin has a separate **StoreConfig page-layout authoring** surface for `pages.home/product/category` (`StorefrontBuilderPage.jsx` → `AdminStoreConfigController`). Its write/publish/rollback path persists successfully, but live public homepage rendering reads the Super Admin `HomepageSection` path instead of `StoreConfig.CustomizationJson.pages.home.live.sections`.
- CMS page **authoring** lives in POS Admin (`Owner,Admin`) in `AdminCmsPagesController`.
- The Storefront runtime never writes: `StorefrontHomeService` and `SeoService` are explicitly documented read-only ("no writes, no events, no sync logic").
- PostgreSQL is the system of record. MongoDB holds LIVE-only projections. Redis holds cache. Elasticsearch feeds best-sellers only.

---

## Homepage Composition

### Canonical Runtime Flow (implemented)

```
Storefront GET / (HomePage.tsx)
  └─ useHomepageSections() → GET /api/homepage/sections?mode=published   [HomepageSectionsController]
        └─ IHomepageSectionReadService.GetActiveSectionsAsync(tenantId, Published)
              ├─ (Preview mode) GetPreviewSectionsAsync → draft scope (Super Admin only)
              └─ (Published) GetPublishedSectionsAsync
                    ├─ tenant scope (TenantId) → if has sections, use them
                    └─ else global scope (TenantId == null)
              ├─ Redis cache first (HomepageSectionCacheService, "homepage-sections")
              ├─ cache MISS → SQL load (HomepageSections, IsPublished && !IsDeleted && IsActive)
              ├─ Schema validate + normalize (HomepageSectionSchemaRegistry)
              ├─ SanitizeDiscoveryReferencesAsync → strip dead product/category refs (CommerceHub authority)
              └─ Localize (title/Localized) at read time by ?lang / Accept-Language
        └─ SectionRenderer renders hero/banner/products/categories
```

Evidence: `HomepageSectionReadService.GetActiveSectionsAsync` (lines 41–106), `HomepageSectionsController.GetSections` (lines 27–94), `HomePage.tsx` (lines 30–64, fallback hero when `homeSections.length === 0`).

### POS Admin Storefront Builder write pipeline (implemented but not homepage render authority)

```
POS Admin /storefront/builder?page=home
  └─ StorefrontBuilderPage.jsx
       ├─ loadPageData() → storeConfigService.getStoreConfig() + getRuntimeStoreConfig()
       ├─ getAcceptedSectionsFromConfig(config, "home") → config.pages.home.sections
       └─ persistPageLayout() → storeConfigService.updatePageLayout("home", { sections })
             └─ PUT /api/admin/store-config/pages/home   [AdminStoreConfigController.UpdatePageLayout]
                  └─ AdminStoreConfigService.UpdatePageLayoutAsync
                       ├─ parse StoreConfig.CustomizationJson
                       ├─ root["pages"]["home"]["draft"] = NormalizeBuilderPageComposition(pageConfig)
                       ├─ ensure root["pages"]["home"]["live"] exists
                       └─ SaveDraftStoreConfigAsync(config)

Publish:
  storeConfigService.publishPage("home")
    └─ POST /api/admin/store-config/pages/home/publish
         └─ AdminStoreConfigService.PublishPageAsync
              ├─ acquire publish lock "page:home"
              ├─ copy draft → live
              ├─ set lastPublishedAt
              ├─ CreateAndAppendVersionSnapshot("Page Published")
              ├─ PersistStoreConfigChangeAsync(... Published)
              ├─ stage StoreConfigPublishedEvent + TenantUpdatedEvent
              └─ RecordAuditAsync(AuditActionTypes.PagePublished)

Rollback:
  POST /api/admin/store-config/rollback/{version}
    └─ RollbackStoreConfigAsync
         ├─ requires confirm=true
         ├─ reads VersionsJson snapshot
         ├─ MergeSnapshotLiveWithCurrentDrafts(...)
         ├─ CreateAndAppendVersionSnapshot("Rollback")
         ├─ PersistStoreConfigChangeAsync(... RolledBack)
         └─ RecordAuditAsync(AuditActionTypes.Rollback)
```

Evidence: `StorefrontBuilderPage.jsx` lines 102–113 (page key), 171–186 (reads `config.pages[page].sections`), 303–336 (saves layout); `storeConfigService.js` lines 59–75 (draft save + publish calls); `AdminStoreConfigController.cs` lines 199–310 and 492–609; `AdminStoreConfigService.cs` lines 258–324, 327–446, 678–800, 860–930, 932–1005; `StoreConfig.cs` lines 77–109.

**Runtime finding:** this POS Admin `pages.home.live.sections` state is mapped into public store-config DTOs and Mongo projections, but the customer-facing homepage component (`HomePage.tsx`) does not read it. `HomePage.tsx` imports `useHomepageSections`, not `selectConfig`, and renders sections returned from `/api/homepage/sections`.

### Starter Template / Seeding

`HomepageTemplateSeeder` seeds a 4-block starter (Hero → Categories → Products → Banner) when no
visible sections exist (`EnsurePublishedTemplateAsync`). Hero/Banner images are generated inline
SVG data-URLs (`CreatePlaceholderImageDataUrl`). Tenant scope and global scope each seed
independently. `GenerateDraftTemplateAsync` supports overwrite-or-fail (`HOMEPAGE_TEMPLATE_EXISTS`).

### Sections are ordered by `Order` then `CreatedAt` (`LoadScopeSectionsAsync`, lines 164–167).

---

## Hero Management

- Schema type `hero` (`HomepageSectionTypes.Hero`).
- Fields (required): `title`, `subtitle`, `imageUrl`, `ctaText`, `ctaLink`; localized: `titleLocalized`, `subtitleLocalized`, `ctaTextLocalized`.
- `imageUrl` is a **free-form URL string** — there is NO media-upload or CDN pipeline in the homepage hero system. It is validated only as a non-empty string.
- Rendered by `PointOfSale.Storefront/src/components/sections/HeroBanner.jsx` (storefront) and `PointOfSale.CommerceHub/src/homepage/sections/HeroSection.tsx` (marketplace, "Shopixy Marketplace" branded).
- Validation: unknown keys rejected; required localized-backed string enforced (`ValidateHero`, lines 148–174).

---

## Banner Management

- Schema type `banner` (`HomepageSectionTypes.Banner`).
- Identical field shape to Hero (`title`, `subtitle`, `imageUrl`, `ctaText`, `ctaLink` + localized).
- Rendered by `PointOfSale.CommerceHub/src/homepage/sections/BannerSection.tsx` (marketplace) and storefront `SectionRenderer`.
- Banner is a homepage section type only; there is a separate legacy `PromotionBannerStyle` migration (`20260116194740_AddPromotionBannerStyle`) for promotions, which is NOT part of this content capability.

---

## Section Management

Implemented section types (registry, `HomepageSectionSchemaRegistry`): `hero`, `products`, `banner`, `categories`. No other types are permitted — `HOMEPAGE_SECTION_TYPE_INVALID` is thrown for unknown types.

Each section carries: `Type`, `Order`, `Data` (JSON), `Version`, `SchemaVersion` (only `1` supported; `HOMEPAGE_SECTION_SCHEMA_VERSION_INVALID` otherwise), `IsActive`, `IsDraft`, `IsPublished`, `PublishedAt`, `DraftGroupId`, `TenantId`.

Management operations (`ISuperAdminHomepageSectionManagementService`):
- `CreateSectionAsync`, `UpdateSectionAsync`, `DeleteSectionAsync` (soft-delete: `IsDeleted=true`), `ReorderSectionsAsync` (must include every section exactly once), `PublishSectionsAsync`, `SaveDraftAsync`.
- All mutations invalidate the published Redis scope (`InvalidatePublishedScopeAsync`).
- `PublishSectionsAsync` demotes all current published rows (`IsPublished=false`) in a transaction, promotes draft rows (`IsDraft=false, IsPublished=true, PublishedAt=now`), skips deleted drafts, then publishes `HomepageSectionsPublishedEvent` (lines 314–404).

---

## Featured Products

Two distinct mechanisms exist:

### A. Homepage "products" section (curated / all / random)
Schema `products` (`ValidateProducts`): `source ∈ {all, random, curated}`, `maxItems` (default 8, cap 24), `productIds` (required if curated), `layout ∈ {grid, carousel}`.
- `curated` → explicit `productIds`.
- `all` / `random` → runtime resolves automatically.
**Runtime resolution (storefront)** is in `PointOfSale.CommerceHub/src/homepage/resolveHomepageSections.ts` and `PointOfSale.Storefront/src/components/sections/homepageCollectionUtils.ts`:
- `curated`: map product IDs to product details by query state.
- `random`: `selectHomepageCollectionItems` with a stable seed key (`${sectionId}:${title}:${layout}`) so the random selection is stable across renders.
- `all`: all automatic products, capped by `maxItems`.
- Fetch size for `random` = `max(limit*3, 24)` to allow stable sampling.

### B. StorefrontHomeService featured shelves (compute-time, not authoring)
`StorefrontHomeService` independently supplies:
- **Featured Products** (`GetFeaturedProductsAsync`): from MongoDB `ProductDocument` (GetFeaturedProductsAsync), TTL 30 min, recomposed from SQL authority.
- **New Arrivals** (`GetNewArrivalsAsync`): MongoDB, TTL 15 min.
- **Best Sellers** (`GetBestSellersAsync`): Elasticsearch `GetBestSellersAsync` → fallback to MongoDB New Arrivals on ES failure (degraded mode), TTL 15 min.
- **Curated Products** (`GetProductsByIdsAsync`): explicit IDs (used by homepage `products` curated sections).

These three shelves are NOT authorable in the homepage builder; they are automatic product selections. The homepage `products` section is the authorable surface; the `StorefrontHomeService` shelves are runtime product queries used by storefront product endpoints.

**Inventory awareness**: products are filtered by `IsActive && IsOnlineVisible` after projection (`StorefrontHomeService` lines 311–313). Discovery references (product/category IDs inside sections) are sanitized against live CommerceHub listing/category authority before returning (`SanitizeDiscoveryReferencesAsync`, lines 249–310).

> "Trending", "Recently Added" as named authorable selections: **Not implemented** as distinct section sources. The implemented automatic sources are `all`, `random`, `curated` only. "New Arrivals" and "Best Sellers" exist as runtime shelves, not as homepage section `source` values.

---

## Collections (Categories section)

Schema `categories` (`ValidateCategories`): `source ∈ {all, random, curated}`, `maxItems` (default 6, cap 18), `categoryIds` (required if curated).
- Runtime resolution mirrors products: curated maps IDs to category tree; all/random use `selectHomepageCollectionItems` over flattened category tree with stable seed.
- Evidence: `resolveHomepageSections.ts` `categories` case (lines 194–231).

There is **no** separate "Collections" aggregate entity in this capability (e.g., hand-built collections). "Collections" = the categories section source.

---

## Navigation

Implemented as **live config-driven**, not a standalone CRUD aggregate:

- `StorefrontNavigationController.GetNavigation` (allow-anonymous) reads `StoreConfig.CustomizationJson` → `navigation.live.{header,footer}`.
- Only returns links when `StoreConfig.StorefrontStatus == "live"`. Otherwise returns `EmptyNavigation`.
- Each link: `Label`, `LabelAr`, `Type ∈ {internal, category, cms, external}`, `Path`, `Slug`, `OpenInNewTab`, `Order`, `Enabled`.
- **Recomposition against SQL authority** (`RecomposeNavigationFromSqlAuthorityAsync`, lines 160–203): every `cms` link must resolve to a published CMS slug; every `category` link must resolve to a live category slug (via `CategoryLineageAuthority`); internal paths validated safe (no `/api/`, `/admin`, `preview=`); external URLs must be http/https with no userinfo. Non-authoritative targets are dropped and a warning logged.
- There is **no** `Menu`/`MenuItem` DB entity for navigation — it is JSON inside `StoreConfig`. The `StorefrontNavigationDocument` (MongoDB) exists as a read model but the live controller sources from `StoreConfig.CustomizationJson` (SQL), not Mongo.

> "Mobile navigation" as a distinct structure: **Not implemented** as a separate config; the same header/footer link sets render responsively in the SPA. Sorting = `Order` then source index. Visibility = `Enabled`.

---

## Footer

- SQL-authoritative via `StorefrontFooterSection` (+ `StorefrontFooterLink`) and `StorefrontSocialLink` entities, tenant-scoped or global (`TenantId == null`).
- `StorefrontFooterReadService.GetFooterAsync`:
  1. Requires live tenant (`StorefrontStatus == "live"`); else empty footer.
  2. Tenant sections → if none, global sections → if none, **derived** sections from published CMS pages (about/contact/privacy/terms preferred order).
  3. Social links: tenant → else global.
  4. Localized (Arabic) when `language != "en"`.
- Footer link types: `Internal`, `External`, `Cms` (resolves to published CMS slug → `/slug`). Invalid links dropped.
- Projected to `StorefrontFooterDocument` (Mongo) and invalidated via `StorefrontFooterUpdatedEvent` → `RedisInvalidationConsumer`.
- Marketplace has its own `MarketplaceFooterSection`/`MarketplaceFooterLink` and `MarketplaceFooterReadService`, isolated.

---

## Static Pages (CMS Pages)

### Entity & lifecycle (`CmsPage.cs`)
Fields: `PageGroupId`, `TenantId`, `Slug`, `Title`, `Content`, `ContentType ∈ {RichText, Blocks}`, `Version`, `SchemaVersion`, `IsDeleted`, `IsDraft`, `IsPublished`, `PublishedAt`, SEO: `MetaTitle`, `MetaDescription`, `MetaKeywords`, `OpenGraphImageUrl`.

### Authoring (`CmsPageService`, POS Admin `Owner,Admin`)
- `CreatePageAsync` → creates a **draft** (`IsDraft=true`). Slug normalized + uniqueness enforced per tenant.
- `UpdatePageAsync` → closes prior draft, writes new draft with `Version+1`, optimistic-concurrency via `ExpectedVersion`.
- `PublishPageAsync` → promotes latest draft to published, demotes old published, stages `StorefrontCmsPagePublishedEvent`, invalidates Redis.
- `RollbackPageAsync` → restores a prior `Version` as a new published version; stages publish event.
- `DeletePageAsync` → soft-delete; if it had a published version, stages `StorefrontCmsPageDeletedEvent` (captures published slug/version BEFORE clearing flags — deletion-fix).
- `GetVersionHistoryAsync` → version history per page group.

### Content validation (`CmsPageContentValidator`)
- `RichText`: HTML string, sanitized (`SanitizeHtml`).
- `Blocks`: JSON array, max 100 blocks; supported block types: `text`, `image`, `faq` (max 50 FAQ items). Block type regex `^[a-z0-9-]{2,50}$`.

### Read (`CmsPageReadService`, `StorefrontPagesController`)
- `GetPublishedPageAsync(slug)` → SQL lookup, requires live tenant (`IsTenantPubliclyActiveAsync`), returns `CmsPageDto` with safe fallback on parse error (empty content, keeps SEO).
- Preview via `previewToken` (signed `CmsPagePreviewTokenService`); `X-Preview-Mode`/`Cache-Control: no-store` headers set.
- Slug normalization: lowercase, non-`[a-z0-9-]` → `-`, max 160 chars, collapse `--`.

### Projection (`CmsPageProjectionConsumer` → MongoDB `storefront_pages`)
- Consumes `StorefrontCmsPagePublishedEvent` / `StorefrontCmsPageDeletedEvent`.
- **Fail-closed**: if SQL page authority not proven (no published non-deleted row) or tenant lineage inactive, it tombstones the projection rather than writing stale content.
- Uses `ProjectionEventFence` (idempotency), freshness filter (`LastEventOccurredAtUtc`/`Version`), reviewed mutation guard (`DerivativeProjectionMutationGuard` with `SqlAuthorityOverlayKind.StorefrontCms | StoreMetadata | Tombstone`, `FailsClosedWhenAuthorityMissing=true`).
- Legacy pages matched by slug; modern pages by `PageGroupId`; stale slug projections tombstoned on rename.

> Structured-data / schema.org JSON-LD for CMS pages: **Not implemented** in the CMS page projection. JSON-LD exists only for breadcrumbs in `SeoService` (`BuildBreadcrumbJsonLd`).

---

## Media Assets

- **Product media** (`ProductMedia` entity, `ProductMediaService`): tenant-scoped, `MediaType ∈ {image, video}`, `Position`, `Provider` (video: youtube/vimeo/mp4; image: none), `Url`.
  - Validation: images must be `.jpg/.jpeg/.png/.webp`; videos must be youtube/vimeo/mp4 with host checks; URL absolute or root-relative.
  - Mutations publish `ProductMediaChangedEvent` → `ProductMediaChangedEventConsumer` updates the MongoDB product read model. Variants referencing removed media are cleared.
- **Homepage/CMS images**: free-form URLs (hero `imageUrl`, banner `imageUrl`, CMS OpenGraph image). There is **no** image-optimization, responsive-image, thumbnail-generation, or CDN pipeline in the content-management code. `IMediaService` (`UploadImageAsync`/`UploadAttachmentAsync`/`DeleteMediaAsync`) is the general upload authority but is not wired into the homepage/CMS authoring flow (those take URLs).
- **Lazy loading**: `loading="eager"` on marketplace hero img (`HeroSection.tsx`); product gallery lazy-loading is in `ProductMediaGallery` (not part of this capability's core).

> Responsive images / CDN / thumbnail generation as a content-management feature: **Not implemented** in the reverse-engineered code.

---

## SEO

### `SeoService` (read-only, cache-first)
- **Metadata** (`GetSeoMetadataAsync`): types `product`, `category`, `cms`.
  - product/category: Redis cache (30 min) → MongoDB → build title/description/canonical/OG/image/keywords/index-follow.
  - cms: `BuildCmsSeoMetadataAsync` reads published CMS page SEO fields.
  - `Index = product.IsActive && product.IsOnlineVisible`.
- **Breadcrumbs** (`GetBreadcrumbsAsync`): product/category; builds category lineage trail; emits **JSON-LD** (`BuildBreadcrumbJsonLd`), cache 60 min.
- **Slug resolution** (`ResolveSlugAsync`): returns type/id/canonical slug; `RequiresRedirect` when input slug ≠ canonical.
- **Canonical URL**: `BuildCanonicalUrl(baseUrl, type, slug)`; base URL resolved via `StorefrontUrlResolver.ResolveByTenantIdAsync`.
- **Sitemap** (`GetSitemapAsync`): tenant-aware URLs (home, products, categories, CMS pages), cache 6 h; `SitemapController` serves JSON + XML (`application/xml`, sitemaps.org 0.9 schema).
- **Robots** (`GetRobotsTxtAsync`): tenant-aware, includes sitemap URL, cache 12 h; `RobotsController` serves `text/plain`.

### Per-page CMS SEO
`CmsPage` carries `MetaTitle`, `MetaDescription`, `MetaKeywords`, `OpenGraphImageUrl` (validated lengths: title 200, description 320, keywords 500, OG image 1000). Exposed via `CmsPageSeoDto` / `CmsPageSeo` (Mongo). `MarketplaceCmsSeoDto` adds `MetaTitleLocalized`/`MetaDescriptionLocalized`.

### Open Graph / Twitter
- OG fields produced: `og:title`, `og:description`, `og:image`, `og:type` (product/category/website), `og:url` (canonical). `SeoDtos.cs` documents "Open Graph and Twitter meta tags" support in the DTO design, but only OG fields are explicitly mapped; **Twitter Card-specific tags are not separately emitted** in the reverse-engineered service (the DTO comment acknowledges Twitter but no `twitter:*` mapping exists).

### Redirects
Slug resolution supports `RequiresRedirect` (canonical slug differs). No standalone redirect/URL-rewrite admin entity is implemented.

### Image Metadata for SEO
OG image only; no `alt`-driven SEO metadata pipeline beyond product/category image URLs.

> Canonical/SEO authoring UI (editing canonical per page manually): Only CMS-page meta fields are authorable. Product/category canonical/title/description are **derived**, not separately authorable.

---

## Publishing Workflow

### Homepage Sections
- Draft workspace per scope (tenant or global). `SaveDraftAsync` upserts draft rows (`IsDraft=true`). `PublishSectionsAsync` atomically promotes draft → published in a transaction and emits `HomepageSectionsPublishedEvent`.
- **Scheduled publishing**: **Not implemented** (no future-dated publish; publish is immediate on call).
- **Approval workflow**: **Not implemented** (no separate approver role; publishing is a single privileged action by Super Admin).
- **Preview**: `mode=preview` returns draft scope; gated to Super Admin role (`IsAuthorizedPreviewRequest` checks `SuperAdminSecurityConstants.Role`); sets `X-Preview-Mode` + `no-store`.

### CMS Pages
- Draft → Publish (immediate). Version history + Rollback (restore prior version as new published version). Optimistic concurrency (`ExpectedVersion`).
- Preview link via signed `previewToken` (time-boxed). Marketplace CMS uses `MarketplaceCmsPagePreviewTokenService`.
- **Scheduled publishing / approval workflow**: **Not implemented**.

### Cross-system effects of publish
- Homepage publish → `HomepageSectionsPublishedEvent` → `HomepageProjectionConsumer` (rebuilds Marketplace Mongo read model) + `RedisInvalidationConsumer` (invalidates homepage Redis + `marketplace:homepage:*` pattern).
- CMS publish/delete → `StorefrontCmsPagePublishedEvent`/`DeletedEvent` → `CmsPageProjectionConsumer` (Mongo) + `RedisInvalidationConsumer` (CMS + footer caches).
- All event consumers use `ProjectionEventFence` for idempotency (duplicate/replay collapse).

---

## Draft / Published Lifecycle

- `HomepageSection`: `IsDraft` XOR `IsPublished` (a row is one or the other; publishing flips flags). `IsDeleted` soft-deletes. `DraftGroupId` groups a draft workspace.
- `CmsPage`: same flags; `PageGroupId` groups versions; publish demotes prior published and promotes newest draft; rollback writes a new published version from history.
- Runtime never sees drafts except via authenticated preview.

---

## Canonical Runtime Flow

```
Request (storefront)
  → TenantContext (X-Tenant-Id; stripped at boundary; RequiredTenantId enforced)
  → Read service
       ├─ Redis cache (primary, best-effort; failure → continue, never 503)
       ├─ MongoDB / Elasticsearch (projection fallback; Mongo failure → 503)
       └─ SQL authority recomposition (sanitize dead refs, filter inactive tenants)
  → Localize (lang / Accept-Language)
  → Response (cache-control varies; preview = no-store)
```

PostgreSQL remains business truth; MongoDB/Redis/ES are derivatives. Sanitization recomputes
discovery references against live CommerceHub listing/category lineage on every read.

---

## Storefront Rendering Runtime

- SPA: `PointOfSale.Storefront` (React/TypeScript). Public homepage route (`router.tsx`) renders `HomePage.tsx`; `HomePage.tsx` calls `useHomepageSections()` and sends the returned `HomepageSection` list to `SectionRenderer`. CMS pages: `pages/CmsPage.tsx` → `components/cms/CmsPage.tsx`.
- StoreConfig runtime load: bootstrap fetches `GET /api/storefront/store-config` (`fetchStoreConfig`) and stores the whole DTO in Zustand (`storeConfigStore`). That DTO includes `pages.home/product/category`, because `StorefrontDraftConfigMapper.MapLive` maps `CustomizationJson.pages` into `StorefrontStoreConfigDto.Pages`.
- Homepage runtime exception: despite the DTO containing `pages.home`, `HomePage.tsx` does not select or render `config.pages.home.sections`. Implementation-only search found homepage renderer references only `useHomepageSections` / `fetchHomepageSections` / `HOMEPAGE_SECTIONS`; there is no `config.pages?.home` consumer in `PointOfSale.Storefront/src/pages/HomePage.tsx` or homepage route code.
- Product/category runtime pages do consume StoreConfig builder pages: `ProductListPage.tsx` reads `config.pages?.product?.sections`; `CategoryPage.tsx` reads `config?.pages?.category?.sections`; product details resolve `productTemplates` / product fallback sections through `ProductTemplateResolver`.
- Theme: `StorefrontThemes.cs` (`default`, `fashion`, `minimal`, `modern` registries), `ThemeResolver.ts`, `ThemeRegistry.ts`, `StorefrontThemePreviewTokenService` (preview isolation). `StoreConfig.ThemeLivePresetKey` / `ThemeLiveOverridesJson` are the published values; draft overrides (`ThemeDraftOverridesJson`) do not affect the live storefront until theme publish.
- Store identity: `StoreConfig.StoreName`, `StoreDescription`, theme colors/fonts/layout/behavior.

---

## Runtime Authority Divergence

### Claim status

**PARTIALLY TRUE.** Two independent authoring systems are implemented, both can author/publish/persist, and the public storefront homepage reads the Super Admin `HomepageSection` engine rather than the POS Admin `StoreConfig.CustomizationJson.pages.home` layout. The over-broad subclaim that System A is "never consumed by runtime" is false: System A live page layout is consumed by store-config mapping/projection and by product/category storefront pages. The divergence is specifically the **homepage render authority**, not every StoreConfig page surface.

### Verified architecture

System A — POS Admin Storefront Builder:
- UI: `PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx` (`page` URL param defaults to `home`, valid pages include `home`, `product`, `category`, `product-template`).
- API: `storeConfigService.updatePageLayout` → `PUT /api/admin/store-config/pages/{pageId}`; `publishPage` → `POST /api/admin/store-config/pages/{pageId}/publish`; rollback → `POST /api/admin/store-config/rollback/{version}`.
- Service/storage: `AdminStoreConfigService.UpdatePageLayoutAsync` writes `StoreConfig.CustomizationJson.pages[pageId].draft`; `PublishPageAsync` copies `draft` to `live`, sets `lastPublishedAt`, creates a version snapshot in `VersionsJson`, increments `LiveVersion`, stages `StoreConfigPublishedEvent`, and records page-publish audit.
- Runtime exposure: `StorefrontStoreConfigController.GetStoreConfig` reads SQL `StoreConfig` and calls `StorefrontDraftConfigMapper.MapLive`; `MapPages` selects `live` and returns `StorefrontStoreConfigDto.Pages.Home/Product/Category`. `StorefrontConfigProjectionConsumer.MapPages` also projects `pages.home.live` into Mongo `StorefrontConfigDocument.Pages.Home`.

System B — Super Admin HomepageSection engine:
- UI/API: `SuperAdminHomepageSectionsController` + `SuperAdminHomepageSectionManagementService` (`Create`, `Update`, `Delete`, `Reorder`, `SaveDraft`, `Publish`).
- Storage: SQL `HomepageSection` rows with `IsDraft`, `IsPublished`, `IsDeleted`, `TenantId`, `SectionGroupId`, `Data`, `Order`.
- Publish: `PublishSectionsAsync` demotes previous published rows, promotes draft rows, invalidates Redis, then publishes `HomepageSectionsPublishedEvent`.
- Runtime: `HomePage.tsx` → `useHomepageSections()` → `fetchHomepageSections()` → `GET /api/homepage/sections` → `HomepageSectionsController` → `HomepageSectionReadService.GetActiveSectionsAsync` → Redis published-scope cache → SQL `HomepageSections` (`IsPublished && !IsDeleted && IsActive`) → schema validation/localization → `SectionRenderer`.

### Root cause

The implementation contains two independently evolved page-composition planes:
- Phase 7.1/9.x StoreConfig page layouts (`StoreConfig.CustomizationJson.pages`) are generalized page-builder configuration for home/product/category/product-template and are publicly exposed through store-config DTOs/projections.
- Phase 3.5 homepage rendering switched `HomePage.tsx` to the dedicated `HomepageSection` draft/publish system. The comment in `HomePage.tsx` states: "Homepage sections now come from the dedicated homepage draft/publish system." The code follows that comment by calling `useHomepageSections()` instead of reading `config.pages.home.sections`.

### Write authority

- POS Admin is write authority for `StoreConfig.CustomizationJson.pages.home` through the Storefront Builder. It has draft save, live publish, version snapshots, rollback, publish locks, optimistic concurrency (`If-Match`/`RowVersion`), `StoreConfigPublishedEvent`, `StoreConfigRolledBackEvent`, and audit entries.
- Super Admin is write authority for `HomepageSection` rows through the Super Admin homepage controller/service and audit service.

### Read authority

- Public homepage render authority: `HomepageSection` only.
- StoreConfig page-layout read authority: public `GET /api/storefront/store-config` and Mongo `StorefrontConfigDocument` expose the published StoreConfig page layouts, including `pages.home.live`; product/category pages consume their respective builder sections.

### Runtime behavior

- On `/`, `HomePage.tsx` imports `useHomepageSections`, calls it, and renders `homeSections` via `SectionRenderer`. It uses `StoreConfig` only for `storeName` in the fallback hero.
- `useHomepageSections.ts` calls `fetchHomepageSections()` and normalizes `HomepageSectionDto` objects.
- `fetchHomepageSections()` calls `ENDPOINTS.HOMEPAGE_SECTIONS`, which is `/api/homepage/sections`.
- `HomepageSectionsController` requires a verified tenant and delegates to `IHomepageSectionReadService`.
- `HomepageSectionReadService` first checks Redis (`HomepageSectionCacheService.GetPublishedScopeAsync`) and falls back to SQL `HomepageSections` by tenant scope, then global scope.
- No implementation path was found where the public homepage renderer reads `config.pages?.home?.sections`.

### Business impact

- A POS Admin can save and publish Home page sections successfully in Storefront Builder, receive successful API responses, version history, rollback eligibility, audit, and StoreConfig projection side effects.
- Those Home page sections do not control the customer-facing homepage. A tenant admin can believe the homepage was changed while customers still see Super Admin `HomepageSection` content or the fallback hero.
- Product/category builder changes do render on their runtime pages, so the divergence is easy to miss during QA if only non-home pages are checked.

### Technical impact

- Duplicate authority surfaces exist for "home page sections": StoreConfig `pages.home` and `HomepageSection`.
- `StorefrontStoreConfigDto.Pages.Home` and `StorefrontConfigDocument.Pages.Home` are populated but not used by the public homepage SPA.
- Tests prove StoreConfig home sections are mapped/projection-supported (`StorefrontPreviewServiceTests`, `StorefrontConfigProjectionConsumerTests`), but runtime homepage source code proves rendering uses `/api/homepage/sections`.
- The comment in `storeConfigService.js` says `publishPage` "affects the live storefront"; for the home page this is only true for store-config exposure/projection, not for the actual public homepage render.

### Recommended remediation

Choose one homepage authority and remove ambiguity:
1. If POS Admin should own tenant storefront homepages, change `HomePage.tsx` to render `config.pages.home.sections` (or adapt StoreConfig pages into the `HomepageSection` schema), then retire or scope the Super Admin homepage engine to Marketplace/global templates only.
2. If Super Admin should remain homepage authority, hide or disable `page=home` in POS Admin Storefront Builder and update copy/API comments so POS Admin cannot publish an apparently-live home layout that the runtime ignores.
3. Add an architecture test asserting the chosen runtime source: either `HomePage.tsx` must read `config.pages.home.sections`, or POS Admin builder must not expose/publish `home`.
4. Add user-facing publish feedback that names the actual runtime surface affected by a publish operation.

---

## Projection Model

### MongoDB
- `StorefrontPageDocument` (`storefront_pages`): LIVE CMS pages only; unique `(tenantId, slug)`; holds `Seo`, `Sections`, `LastEventId/Version`.
- `StorefrontConfigDocument` (`storefront_configs`): StoreConfig projection including `Pages.Home/Product/Category` and `ProductTemplates`; `StorefrontConfigProjectionConsumer.MapPages` reads `CustomizationJson.pages.*.live` and maps it into Mongo. This proves System A is projected, even though public homepage render ignores `Pages.Home`.
- `StorefrontNavigationDocument` (`storefront_navigation`): read model (not used by the live navigation controller, which reads `StoreConfig`).
- `StorefrontFooterDocument` (`storefront_footer`): footer read model.
- `MarketplaceHomepageReadModelDocument` (`marketplace_homepage_read_models`): marketplace homepage, `ScopeKey` global or tenant; freshness-guarded upsert/delete.
- `ProductDocument` / `CategoryDocument`: product/category read models consumed by `StorefrontHomeService` and `SeoService`.

### Redis
- `HomepageSectionCacheService` ("homepage-sections", TTL = `HomepageSectionCacheOptions.PublishedTtlMinutes`, min 1). Key per tenant/global.
- `CmsPageCacheService`, `MarketplaceCmsPageCacheService`, `StorefrontFooterCacheService`, `MarketplaceFooterCacheService`.
- `StorefrontHomeService` caches featured/new-arrivals/best-sellers/categories (30/15/15/60 min) via `IRedisJsonCacheService.GetOrCreateAsync`.
- `SeoService` caches metadata/breadcrumbs/slug/sitemap/robots (30/60/60/360/720 min).

### Elasticsearch
- `IProductSearchIndexService.GetBestSellersAsync` feeds Best Sellers; on failure, MongoDB New Arrivals fallback (degraded mode, warning logged).
- `CmsPageSearchDocument` exists (CMS search index) but is not part of the core content-rendering projection.

### Projection rebuild / invalidation
- Rebuild: `HomepageProjectionConsumer` calls `IMarketplaceHomepageReadModelProjectionService.RebuildAsync` on publish.
- Invalidation: `RedisInvalidationConsumer` listens to publish/delete/footer/product/review events and removes the relevant Redis keys (with fence).
- Fail-closed: `CmsPageProjectionConsumer` tombstones rather than projecting unproven authority.

---

## Caching Strategy

- Cache-first everywhere on the read side. Redis is the primary data source for performance; MongoDB/SQL are fallbacks.
- Cache read failures are logged as WARNING and the request continues to the data source (never 503).
- Cache write failures logged as WARNING (best-effort).
- 503 is returned ONLY when the authoritative derivative (MongoDB for sections/SEO/CMS Mongo read) fails.
- TTLs are explicit per surface (see Projection Model). Publish events invalidate the published scope.
- Preview responses are `Cache-Control: no-store`.

---

## CommerceHub Integration

Marketplace is a **parallel, isolated** content plane:

- **Marketplace Homepage**: `MarketplaceHomepageReadModelDocument` (Mongo, `marketplace_homepage_read_models`), `MarketplaceHomepageReadRepository` (freshness-guarded upsert/delete, tenant-or-global). Rebuilt by `HomepageProjectionConsumer` on `HomepageSectionsPublishedEvent`. Resolved/rendered by `PointOfSale.CommerceHub/src/homepage/resolveHomepageSections.ts` and `HeroSection.tsx`/`BannerSection.tsx` (hard-coded "Shopixy Marketplace" branding).
- **Marketplace CMS Pages**: `MarketplaceCmsPage` entity (no tenant — platform-owned), `MarketplaceCmsPageService` (Super Admin authoring), `MarketplaceCmsPageReadService` (published/preview, localized EN/AR), projected to cache; `MarketplaceCmsPagesController` (allow-anonymous). Slug normalization identical to storefront; localized fields (`TitleJson`, `ContentJson`, `MetaTitleJson`, etc.) with `MarketplaceLocalization.MergeField`/`ResolveText`.
- **Marketplace Footer**: `MarketplaceFooterSection`/`MarketplaceFooterLink`, `MarketplaceFooterReadService`, `MarketplaceFooterCacheService`, `MarketplaceFooterController` (public).
- **Marketplace Branding**: footer social links + branding are marketplace-scoped; isolation enforced by `ScopeKey`/`TenantId` and separate `marketplace_*` collections/Redis patterns (`marketplace:homepage:*`, `marketplace:products:*`).
- **Super Admin authority**: marketplace homepage/CMS/footer are authored ONLY via `SuperAdminMarketplace*` controllers/services. POS Admin tenants have NO authority over marketplace content.
- **Marketplace isolation**: tenant storefront sections never leak into marketplace (separate collections, separate read repos, separate cache keys). Marketplace homepage read model queries tenant doc first, then global — strictly within marketplace collections.

> CommerceHub Marketplace is explicitly NOT governed by CommerceHub Super Admin in the sense of tenant storefronts; the marketplace content surfaces are platform/Super-Admin-owned and isolated from individual tenant POS storefronts.

---

## Search Integration

- Elasticsearch is used for **Best Sellers** selection (`GetBestSellersAsync`) and product search ranking (`PromotionRankingConfig` in `StoreConfig`). It is NOT a content-publishing projection for homepage sections or CMS pages (those project to MongoDB/Redis).
- `CmsPageSearchDocument` (Elasticsearch) exists for CMS search but is not part of the storefront rendering projection path.
- Search indexing as a content-publish side effect (e.g., reindex on CMS publish): **Not implemented** in the reverse-engineered publish pipeline (CMS publish projects to Mongo + Redis only; no ES index write observed).

---

## Security Model

### Who can create / publish / edit / delete
- **Homepage Sections**: Super Admin only (`SuperAdminHomepageSectionManagementService` / `SuperAdminHomepageSectionsController`). POS Admin: no authority. Storefront: read-only.
- **CMS Pages (tenant)**: POS Admin roles `Owner` or `Admin` (`[Authorize(Roles="Owner,Admin")]` on `AdminCmsPagesController`). Publish requires `ExpectedVersion` (optimistic concurrency).
- **CMS Pages (marketplace)**: Super Admin only (`SuperAdminMarketplaceCmsPagesController`).
- **Navigation / Footer / Store Config / Theme**: POS Admin (tenant) via `AdminStoreConfigController` / `AdminStorefrontFooterController`; global footer seeds via Super Admin.
- **Marketplace Homepage/Footer/Branding**: Super Admin only.

### Tenant isolation
- `ITenantContext.RequiredTenantId` enforced; `X-Tenant-Id` stripped at boundary. All queries scope by `TenantId`; global scope = `TenantId == null`.
- Discovery references (product/category IDs) sanitized against live CommerceHub authority per read.
- Navigation/footer links recomposed against live published CMS/category authority; unsafe links dropped.
- Projection consumers fail-closed when SQL authority or tenant lineage is unproven.
- Preview endpoints require authenticated Super Admin (homepage) or a valid signed `previewToken` (CMS/marketplace) and emit `no-store`.

---

## Audit & Reporting

- `SuperAdminHomepageSectionManagementService` and marketplace services integrate `ISuperAdminAuditService`.
- `StoreConfig` changes go through `StoreConfigAuditService` / `StoreConfigAuditEntry`.
- CMS publish/rollback/delete log structured events with tenant/slug/version.
- `ProjectionEventFence` provides idempotency/audit of event processing.
- No dedicated "content audit report" UI is implemented in this capability; audit is via Super Admin audit service + event logs.

---

## Events

- `HomepageSectionsPublishedEvent` (`HomepageSectionEvents.cs`): `PublishedAtUtc`, `SectionCount`. Consumed by `HomepageProjectionConsumer` (Mongo marketplace rebuild) + `RedisInvalidationConsumer` (homepage + `marketplace:homepage:*` caches).
- `StorefrontCmsPagePublishedEvent` / `StorefrontCmsPageDeletedEvent` (`CacheInvalidationEvents.cs`, `IAggregateScopedIntegrationEvent`): consumed by `CmsPageProjectionConsumer` (Mongo `storefront_pages`, fail-closed) + `RedisInvalidationConsumer` (CMS + footer caches).
- `MarketplaceCmsPagePublishedEvent` / `MarketplaceCmsPageDeletedEvent`: `RedisInvalidationConsumer` (marketplace CMS + footer caches).
- `StorefrontFooterUpdatedEvent` / `MarketplaceFooterUpdatedEvent`: footer cache invalidation.
- `ProductMediaChangedEvent`: product media → Mongo product read model.
- `NavigationPublishedEvent` (`CmsPageEvents.cs`): defined for navigation publishing (storefront builder), consumed for cache invalidation.
- All event consumers are MassTransit `IConsumer<T>` with retry intervals (1s/5s/15s) and `ProjectionEventFence` idempotency.

> Sitemap/robots generation does not emit events; they are computed on read with Redis caching.

---

## Background Jobs / Hosted Services

- No dedicated `IHostedService` for content projection was found in this capability. Projection is **event-driven** via MassTransit consumers (`HomepageProjectionConsumer`, `CmsPageProjectionConsumer`, `RedisInvalidationConsumer`, `NavigationProjectionConsumer`, `StorefrontFooterProjectionConsumer`), not polling jobs.
- `HomepageTemplateSeeder` runs on-demand (ensure template), not as a scheduled job.
- Any scheduled seeding/rebuild would be invoked from bootstrap, not a recurring hosted worker (not present in the reverse-engineered files).

---

## Failure Scenarios (implementation-backed)

- **Missing Hero / Missing Homepage**: `HomePage.tsx` renders `DefaultHeroFallback` when `homeSections.length === 0`; navigation returns `EmptyNavigation` when storefront not live; footer returns `Derived` (from CMS pages) or `Empty` when none.
- **Broken Navigation**: non-authoritative links (dead CMS slug, inactive category, unsafe path) are silently dropped with a warning; safe links still render.
- **Projection lag**: Redis cache TTLs mean up to TTL-minutes staleness after publish; publish events invalidate, but a race can serve cached old data until TTL/invalidation. CMS Mongo projection is async (eventual); `IsPublished` SQL is authoritative immediately.
- **Cache inconsistency**: cache failures are non-fatal (WARNING, continue to source). Invalidation is event-driven; if an event is lost, TTL bounds inconsistency.
- **Media failure**: product media read failures surface as missing images in gallery; homepage `imageUrl` is a URL with no validation of reachability.
- **SEO fallback**: `SeoService` falls back product→category→tenant defaults; parse errors in CMS content return empty/fallback content but preserve SEO fields; ES failure → Mongo New Arrivals fallback for best sellers.
- **Deleted content**: CMS delete stages delete event → Mongo tombstone; storefront 404s for the slug. Homepage section delete soft-deletes and is excluded from published scope.
- **Rendering fallback**: `CmsPageReadService.MapPageWithSafeFallback` / `MarketplaceCmsPageReadService` catch `JsonException`/`InvalidOperationException` and return fallback content (empty RichText / `[]` blocks) while keeping SEO.
- **Fail-closed projection**: `CmsPageProjectionConsumer` tombstones rather than projecting when SQL authority/tenant lineage unproven — protects against stale derivatives.

---

## Edge Cases

- **Global vs tenant scope**: homepage sections and footer fall back global → tenant; CMS pages are strictly tenant (no global). Marketplace homepage is tenant → global within marketplace collections.
- **Slug rename**: CMS publish tombstones stale slug projections; slug normalization prevents collisions; uniqueness enforced per tenant.
- **Concurrent admin edits**: optimistic concurrency (`ExpectedVersion`/`RowVersion`) returns conflict on CMS; homepage reorder requires exact membership.
- **Localization**: EN/AR supported via `*Localized` fields resolved at read time (`MarketplaceLocalization`); cache is language-agnostic (localization applied post-cache).
- **Preview safety**: preview mode sets `no-store` and requires auth/token; preview tokens are scoped to slug/page.
- **Legacy CMS pages**: `PageGroupId == Guid.Empty` matched by slug to avoid collapsing; modern pages by `PageGroupId`.
- **Large/empty sections**: `curated` requires ≥1 ID; `maxItems` capped (products 24, categories 18); empty product/category fetch returns `empty` status in SPA.

---

## Implementation Validation

Every statement above is traced to a concrete file:

- `PointOfSale.Core/Entities/HomepageSection.cs`
- `PointOfSale.Core/Entities/CmsPage.cs`, `ProductMedia.cs`, `StoreConfig.cs`, `StorefrontFooterSection.cs`, `StorefrontSocialLink.cs`, `MarketplaceCmsPage.cs`, `MarketplaceFooter*.cs`
- `PointOfSale.API/Services/Homepage/*` (ReadService, CacheService, SchemaRegistry, Modes, TemplateSeeder, SuperAdmin management)
- `PointOfSale.API/Services/Storefront/SeoService.cs`, `StorefrontHomeService.cs`, `StorefrontFooterReadService.cs`
- `PointOfSale.API/Services/Cms/*` (CmsPageService, CmsPageReadService, CmsPageContentValidator, CmsPageCacheService)
- `PointOfSale.API/Services/MarketplaceCms/*` (MarketplaceCmsPageService, ReadService, CacheService, Seeder, PreviewTokenService)
- `PointOfSale.API/Controllers/*` (HomepageSections, StorefrontPages, AdminCmsPages, MarketplaceCmsPages, StorefrontNavigation, StorefrontFooter, Seo, Sitemap, Robots, SuperAdmin*)
- `PointOfSale.Infrastructure/Consumers/*` (HomepageProjectionConsumer, CmsPageProjectionConsumer, RedisInvalidationConsumer, NavigationProjectionConsumer, StorefrontFooterProjectionConsumer)
- `PointOfSale.Infrastructure/MongoDB/Documents/*` (StorefrontPageDocument, StorefrontNavigationDocument, StorefrontFooterDocument, Marketplace/*)
- `PointOfSale.Infrastructure/MongoDB/Services/Marketplace/MarketplaceHomepageReadRepository.cs`
- `PointOfSale.Core/Events/*` (HomepageSectionEvents, CmsPageEvents, CacheInvalidationEvents)
- `PointOfSale.Storefront/src/pages/HomePage.tsx`, `CmsPage.tsx`, `hooks/useHomepageSections.ts`, `components/sections/*`
- `PointOfSale.CommerceHub/src/homepage/*` (resolveHomepageSections, HeroSection, BannerSection, types)
- Migrations: `20260327_ZZZ_AddHomepageSections.sql`, `20260328_AAA_HardenHomepageSectionsDraftPublishing.sql`, `20260329_AAB_AddCmsPages.sql`, `20260329_AAD_AddStorefrontFooterAndSocial.sql`, `20260329_AAE_AddMarketplaceFooterAndSocial.sql`, `20260329_AAC_AddMarketplaceCmsPages.sql`

---

## Certification Verdict

**Certified implementation-backed.** The Storefront Content Management capability is a
draft/publish, tenant-scoped, projection-backed experience composition engine. The following
behaviors were verified as **present**:

- Schema-bounded homepage sections (hero/banner/products/categories) with draft/publish + Redis cache + marketplace Mongo projection.
- CMS pages with RichText/Blocks content, version history, rollback, preview tokens, Mongo projection (fail-closed), Redis invalidation.
- Storefront navigation + footer derived from `StoreConfig`/SQL with live-authority recomposition and safe-link filtering.
- Store identity/theme via `StoreConfig` (draft vs live preset/overrides).
- POS Admin Storefront Builder page layouts via `StoreConfig.CustomizationJson.pages` (draft/live/publish/rollback/audit), with verified runtime authority divergence for `pages.home`.
- SEO metadata, breadcrumbs + JSON-LD, canonical URLs, slug resolution, tenant-aware sitemap (JSON+XML) and robots.txt.
- Product media with validation + Mongo sync; general `IMediaService` upload authority.
- CommerceHub marketplace content as an isolated, Super-Admin-governed plane (homepage/CMS/footer).
- Event-driven projection + Redis invalidation with idempotency fences and fail-closed guards.

The following were explicitly verified as **NOT implemented** (not assumed missing, confirmed absent):

- Scheduled/future-dated publishing (homepage + CMS).
- Separate approval/reviewer workflow for content.
- Responsive-image generation, CDN, thumbnail generation in content management.
- Separate "Trending"/"Recently Added" authorable section sources (only `all`/`random`/`curated`).
- Schema.org / JSON-LD structured data for CMS pages (only breadcrumb JSON-LD exists).
- Dedicated Twitter Card (`twitter:*`) tag emission (OG only is mapped; DTO comment mentions Twitter but no mapping).
- CommerceHub Marketplace governance by tenant CommerceHub Super Admin (marketplace content is platform/Super-Admin-owned and isolated).
- Polling/hosted-service content projection jobs (projection is event-driven via MassTransit).
- CMS publish → Elasticsearch reindex side effect (projects to Mongo + Redis only).
- Standalone redirect/URL-rewrite administration (only slug-resolution `RequiresRedirect`).

Runtime authority audit verdict for the two-homepage-system claim: **PARTIALLY TRUE**.
- TRUE: System A (POS Admin Storefront Builder → `StoreConfig.CustomizationJson.pages.home`) exists and supports authoring, draft save, publish, persistence, version snapshots, rollback, outbox events, projection, and audit.
- TRUE: System B (Super Admin `HomepageSection` engine) exists and supports authoring, draft/publish, Redis cache, marketplace Mongo projection, and public `GET /api/homepage/sections` runtime reads.
- TRUE: the live public storefront homepage render path reads System B (`HomePage.tsx` → `useHomepageSections` → `/api/homepage/sections` → `HomepageSectionReadService` → Redis/SQL `HomepageSection`) and does not render `StoreConfig.CustomizationJson.pages.home.live.sections`.
- FALSE as written: System A is not "never consumed by runtime". It is consumed by `StorefrontStoreConfigController`/`StorefrontDraftConfigMapper.MapLive`, projected by `StorefrontConfigProjectionConsumer.MapPages`, stored in `StorefrontConfigDocument.Pages.Home`, bootstrapped into the storefront config store, and its product/category pages are consumed by runtime pages. The divergence is therefore **homepage render authority divergence**, not total System A runtime non-consumption.
