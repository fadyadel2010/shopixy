# Storefront / Builder Runtime — Final Canonical Architecture

Implementation-backed architecture specification for the Storefront / Builder Runtime of
Shopixy / PointOfSale. This document treats the **Storefront Runtime** (the public,
customer-facing READ / presentation surface) and the **Builder Runtime** (the POS Admin
visual authoring surface that writes `StoreConfig` page/theme/navigation compositions) as
two coupled halves of ONE bounded context — the merchant storefront presentation runtime —
and reverse-engineers exactly how they relate.

> Every statement in this document is derived from the C#/TypeScript implementation under
> `PointOfSale.Core/`, `PointOfSale.Infrastructure/`, `PointOfSale.API/`, and
> `PointOfSale.Web/`. Where a conclusion is supported, the `file:line` evidence is cited.
> Prior markdown, comments, controller/component names, and certification `.md` files were
> NOT treated as authoritative — they are referenced only where they independently confirm a
> code finding. The Builder is a runtime behaviour specification: drag-and-drop React widgets
> and CSS are described only to the extent the backend authoring contract (allowed section
> types, draft/live JSON, theme preset/override model) is implementation-backed.

Canonical paths:
- Write (Builder) authority: `PointOfSale.API/Services/Admin/AdminStoreConfigService.cs`,
  `PointOfSale.API/Services/Admin/AdminCmsService.cs`, `PointOfSale.API/SuperAdmin/Services/SuperAdminHomepageSectionManagementService.cs`, `PointOfSale.API/Services/Cms/CmsPageService.cs`
- Builder/theme entity: `PointOfSale.Core/Entities/StoreConfig.cs`, `StoreThemeConfig`,
  `PromotionRankingConfig`; `ThemePresets` table
- Read (Storefront) authority: `PointOfSale.API/Services/Storefront/StorefrontHomeService.cs`,
  `StorefrontPreviewService.cs`, `SeoService.cs`, `PointOfSale.API/Services/Homepage/HomepageSectionReadService.cs`, `PointOfSale.API/Services/Cms/CmsPageReadService.cs`
- Projection consumers: `PointOfSale.Infrastructure/Consumers/StorefrontConfigProjectionConsumer.cs`,
  `CmsPageProjectionConsumer.cs`, `NavigationProjectionConsumer.cs`, `StorefrontFooterProjectionConsumer.cs`,
  `HomepageProjectionConsumer.cs`; `PointOfSale.API/Consumers/RedisInvalidationConsumer.cs`
- Public SQL read gate: `PointOfSale.API/Services/Cms/CmsPageReadService.cs`, `SeoService.cs`
- Frontend: `PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx` + `components/builder/*`,
  `src/pages/HomePage.tsx`, `src/super-admin/homepage/*`; `PointOfSale.Storefront/**` (separate frontend host)

This document synthesizes several reverse-engineering passes — (1) Builder write authority
and draft/live/version/theme/rollback lifecycle, (2) Storefront read authority and
SQL-vs-projection divergence, (3) Publishing/presentation state machines, (4) Projection /
Cache / Search / SEO runtime, (5) Cross-Domain integration and CQRS/Outbox — into ONE
coherent architecture.

---

## Executive Summary

The **Storefront / Builder Runtime** is the **merchant storefront presentation runtime** of
Shopixy. It is NOT a single "Storefront" read app and NOT a single "Builder" editor — it is
two coupled runtimes sharing one SQL authority:

- The **Builder Runtime** is the POS Admin visual authoring surface. It is WRITE authority
  for the merchant-authored storefront composition: per-tenant pages and product-template
  layouts (`StoreConfig.CustomizationJson.pages[*].draft/live` + `product_templates[*]`),
  navigation (`navigation.draft/live`), store identity, and **theme** (preset key +
  draft/live overrides). It also authors CMS static pages (`CmsPage`) and the draft/preview
  content for the storefront. It is implemented in
  `PointOfSale.API/Services/Admin/AdminStoreConfigService.cs` and `AdminCmsService.cs`.
- The **Storefront Runtime** is the public, customer-facing READ / presentation surface. It
  is strictly read-only for customers — it renders published content recomposed from
  PostgreSQL (cms pages, sitemap, slug, breadcrumb), Redis (cache-first shelves, config,
  navigation), MongoDB (storefront_configs projection, product read model), and Elasticsearch
  (product search). It NEVER writes authoritative business truth. Implemented in
  `PointOfSale.API/Services/Storefront/*` and consumed by the `PointOfSale.Web` and
  `PointOfSale.Storefront` frontends.

Reverse-engineered facts (all code-backed):

- **PostgreSQL (`StoreConfig` + `CmsPage` + `HomepageSection`) is the single authoritative
  write model for storefront presentation.** Every Builder mutation is committed to PostgreSQL
  through `ApplicationDbContext`. Proof: `AdminStoreConfigService.cs:99` (inject
  `ApplicationDbContext`); `StoreConfig.cs:16` ("Managed ONLY via POS Admin APIs", "Storefront
  is read-only"); `AdminCmsService.cs:317` writes `StoreConfigs.CustomizationJson`;
  `CmsPageService.cs:283-339` commits publish under a transaction.
- **The Builder is a draft/live JSON document editor over `StoreConfig.CustomizationJson`,
  not a relational page CMS.** Page layouts, product templates, navigation, and theme are all
  stored as JSON blobs inside the one `StoreConfig` row per tenant. Draft edits write
  `*.draft`; publish copies `draft -> live` and bumps `LiveVersion`
  (`AdminStoreConfigService.cs:308-318` UpdatePageLayout; `:383-396` PublishPage).
- **Theme is a preset-key + override model, with an independent draft/live split.**
  `StoreConfig.ThemePresetKey`/`ThemeDraftOverridesJson` are draft; `ThemeLivePresetKey`/
  `ThemeLiveOverridesJson` are the published values used by the storefront
  (`StoreConfig.cs:87-103`). Publishing theme promotes draft -> live
  (`AdminStoreConfigService.cs:612-613` PublishTheme). The storefront reads `ThemeLive*`
  only; draft theme changes never affect the public storefront until publish.
- **The public homepage is authored by Super Admin (`HomepageSection`), NOT by the POS Admin
  Builder page.** This is the defining authority divergence: `StorefrontBuilderPage.jsx`
  authors `StoreConfig.CustomizationJson.pages[home].draft/live`, but the public `HomePage`
  renders `HomepageSection` rows via `HomepageSectionsController` -> `HomepageSectionReadService`
  -> Redis/SQL. The POS Admin `pages[home]` composition is consumed by the builder preview and
  by product/category builder pages, but NOT by the public homepage render path (scoped
  divergence, not dead code — see Runtime Authority Divergence).
- **Publication is event-driven through a generic transactional outbox; projection is
  eventual and fails-closed.** `StoreConfigPublishedEvent`/Updated/Created/RolledBack stage
  `OutboxMessage` rows; `StorefrontConfigProjectionConsumer` hydrates the Mongo
  `storefront_configs` doc and invalidates Redis, but **only after proving current SQL tenant
  lineage** — if authority is unproven it deletes the Mongo doc and cache (fail-closed)
  (`StorefrontConfigProjectionConsumer.cs:170-185,418-441`). Freshness is fenced by
  `ProjectionEventFence` + `LastEventVersion`/`LastEventId` optimistic filters
  (`:144-166,224-241,482-501`).
- **Storefront content reads are SQL-authoritative for CMS pages; Mongo is acceleration only.**
  `CmsPageReadService.GetPublishedPageAsync` queries `CmsPages` SQL directly
  (`CmsPageReadService.cs:57-65,121-142`), gated by `IsTenantPubliclyActiveAsync`. The Mongo
  `storefront_pages` projection is written by `CmsPageProjectionConsumer` but the primary
  published read does NOT read it (orphaned derivative). Sitemap/slug/canonical recompute
  against live SQL on every read (`SeoService.cs:644-773,497-637`).
- **Storefront product SEARCH is Elasticsearch as single authority with no Mongo fallback.**
  `StorefrontHomeService` best-sellers fall back Mongo->NewArrivals on ES failure, but the
  storefront search endpoint treats ES unavailability as 503 (`StorefrontSearchUnavailableException`).
- **The Storefront Runtime owns presentation but NOT money/state.** Cart/checkout are
  read-mostly from the storefront's perspective; payment methods, shipping quotes, and order
  tracking are read-only lists. The checkout submission and its money/state lifecycles are
  owned by CommerceHub/POS checkout and the payment lifecycle service, not by the storefront
  runtime (verified: `StorefrontPaymentMethodsController`, `StorefrontShippingQuotesController`
  are read-only; `StorefrontCartController` mutates cart state but not orders/money).
- **Personalization is local-first, not server-authoritative.** Wishlist/favorites and
  recently-viewed are persisted per customer via API where signed-in, but browsing history and
  guest personalization are client/localStorage in the frontend (not a server presentation
  authority).
- **Localization is language-agnostic cache + SQL/JSON `*Localized` fields resolved at read
  time.** No dedicated localization runtime; AR content is stored as `labelAr`/`*Json` fields
  and selected via `?lang`/`Accept-Language`. Theme presets carry `IsRtl` for RTL rendering.

---

## Architecture Vision

### Runtime Purpose

The Storefront / Builder Runtime transforms merchant-authored commerce content (pages,
layouts, theme, navigation, CMS content, branding, homepage composition) into a
globally-consistent, high-performance, cache-safe, localized, SEO-optimized public storefront,
while preserving authoritative business truth (catalog, inventory, pricing, payment, orders) in
the owning runtimes. It does this by:

- Giving the merchant a **Builder** to author storefront composition as draft JSON over a single
  per-tenant `StoreConfig` row (plus CMS pages and Super-Admin homepage sections),
- **Publishing** that composition atomically (draft -> live) through PostgreSQL transactions and
  staging outbox events,
- **Projecting** the published composition into Redis/Mongo/ES read models that the storefront
  reads cache-first,
- **Rendering** the public storefront read-only, recomposing derivative stores against live SQL
  authority so stale projections never win.

### Runtime Scope

The runtime owns the merchant storefront presentation plane and the marketplace (Super Admin)
presentation plane, but NOT the underlying commerce truth:

- **POS Admin Builder**: per-tenant `StoreConfig` pages, product templates, navigation, store
  identity, theme (preset + overrides), draft/live versioning, rollback.
- **CMS static pages** (`CmsPage`) — POS Admin `Owner,Admin` authoring; draft/publish/rollback.
- **Homepage composition** (`HomepageSection`) — Super Admin only (global + per-tenant scope).
- **Marketplace CMS / marketplace homepage / marketplace footer / branding** — Super Admin only,
  isolated `Marketplace*` collections.
- **SEO**: metadata, breadcrumbs (+JSON-LD), canonical URLs, slug resolution, sitemap, robots —
  read-only derived from the above.
- **Storefront read surface**: product shelves (featured/new-arrivals/best-sellers/curated),
  category lists, CMS page render, search, navigation/footer render, theme application,
  personalization (local), localization resolution.

### Runtime Ownership

- **Builder = write authority** (POS Admin for tenant content; Super Admin for marketplace +
  homepage sections).
- **Storefront = read authority** (customers, guest or signed-in).
- **SQL (`StoreConfig`, `CmsPage`, `HomepageSection`, `ThemePresets`) = truth** for everything
  the storefront renders.
- **Redis / Mongo / ES = derivative read models** (acceleration), never authoritative.

### Runtime Authority

- The storefront never decides truth. Where it reads a derivative (Mongo/Redis/ES) it re-derives
  discovery references and visibility against live SQL (`HomepageSectionReadService.SanitizeDiscoveryReferencesAsync`;
  `StorefrontHomeService.RecomposeProductCardsFromSqlAuthorityAsync`; `SeoService` sitemap recompute).
- Publishing authority is exercised only inside the Builder services, under optimistic concurrency
  (`RowVersion`) and distributed publish locks (`IPublishLockService.TryAcquireAsync`).

---

## Runtime Purpose

The Storefront / Builder Runtime exists to let a merchant visually compose and publish a branded
storefront, and to serve that storefront to customers with the performance and SEO characteristics
of a content-delivery network — without ever letting the presentation layer own or corrupt the
authoritative commerce state.

It is the **presentation runtime** of the merchant platform: the Builder is its authoring/write
half; the Storefront (public read) is its rendering/read half. They share exactly one SQL
authority and are held consistent by the outbox + projection pipeline.

Proof of read/write split:
- `StoreConfig.cs:11-15` class doc: "Managed ONLY via POS Admin APIs. Storefront is read-only."
- `StorefrontHomeService.cs:23-49` doc: "This service is READ-ONLY — no writes, no events...
  Has NO dependency on Inventory, Pricing, or Stock... Cache-first... 503 SERVICE_UNAVAILABLE is
  returned ONLY when MongoDB read fails."
- `StorefrontPreviewService.cs:13-17` doc: "CRITICAL: This service reads directly from PostgreSQL
  (draft data). It must NEVER write data or be used for live traffic."

---

## Runtime Scope

### In scope (owned)

- Per-tenant `StoreConfig` rows: store name/description, behaviour flags, theme (preset + live
  overrides), `CustomizationJson` (pages, product_templates, navigation, theme overrides),
  `VersionsJson` version history, `LiveVersion`, `StorefrontStatus` (`StoreConfig.cs:16-188`).
- `ThemePresets` table (template catalog; `AdminStoreConfigService.cs:498-512 GetThemePresetsAsync`).
- `CmsPage` lifecycle (create/update/publish/rollback/delete) for tenant static pages
  (`CmsPageService.cs`).
- `HomepageSection` lifecycle — Super Admin only (`SuperAdminHomepageSectionManagementService.cs`).
- `StorefrontFooterSection`/`StorefrontSocialLink` (SQL-authoritative footer).
- Storefront read services: home shelves, CMS page read, SEO, navigation/footer read, preview.
- Versioning & rollback of the published storefront (`RollbackStoreConfigAsync`,
  `AdminStoreConfigService.cs:678-800`; `CmsPageService.RollbackPageAsync`).

### Out of scope (deferred to owning runtimes)

- **Catalog truth** (products, variants, pricing, categories) — owned by Product Catalog runtime;
  storefront reads projections and re-derives visibility (`ProductSqlAuthorityProjectionWriter`,
  SQL overlay).
- **Inventory** — visibility only (`IsActive && IsOnlineVisible`); no stock mutation.
- **Payment / Orders / Shipping** — storefront reads method/quote/tracking lists; lifecycle owned
  by CommerceHub/POS checkout + `IPaymentLifecycleService`.
- **Cart / Checkout money/state** — cart is Redis scratch; order creation is delegated to
  `CheckoutController` (tenant) / `CommerceHubCheckoutController` (marketplace), which own the
  order-write authority. The storefront controllers are NOT the lifecycle owners.
- **Accounting / Treasury / Notifications** — storefront has zero authority (verified absent in
  storefront tree).
- **Media storage** — homepage/CMS images are free-form URL strings; no upload pipeline in builder
  authoring (`CmsPageContentValidator.cs:250-281`).
- **Search indexing of products** — owned by the Search runtime; storefront consumes ES.
- **Analytics emission** — storefront read services may record views via an analytics authority
  reader, but analytics truth is owned by the Reporting/Analytics runtime.

---

## Runtime Owns

| Asset | Storage | Owner | Evidence |
|---|---|---|---|
| Storefront composition (pages/templates/nav/theme) | `StoreConfig.CustomizationJson` (SQL) | POS Admin Builder | `StoreConfig.cs:82`; `AdminStoreConfigService.cs:258-446` |
| Theme preset selection + overrides | `StoreConfig.ThemePresetKey`/`Theme*OverridesJson` (SQL) | POS Admin Builder | `StoreConfig.cs:87-103`; `AdminStoreConfigService.cs:516-671` |
| Theme preset catalog | `ThemePresets` (SQL) | Platform/Super Admin seed | `AdminStoreConfigService.cs:498-512` |
| CMS static pages | `CmsPage` (SQL) | POS Admin `Owner,Admin` | `CmsPageService.cs` |
| Homepage composition | `HomepageSection` (SQL) | Super Admin | `SuperAdminHomepageSectionManagementService.cs:314-404` |
| Footer | `StorefrontFooterSection`/`StorefrontSocialLink` (SQL) | POS Admin tenant / Super Admin global | footer services |
| Published storefront version history | `StoreConfig.VersionsJson`/`LiveVersion` (SQL) | POS Admin Builder | `StoreConfig.cs:109-115`; `AdminStoreConfigService.cs:402` |
| Storefront status (live/maintenance/disabled) | `StoreConfig.StorefrontStatus` (SQL) | POS Admin | `StoreConfig.cs:59` |
| SEO metadata / sitemap / robots | Derived at read (SQL recompute) | Storefront read (read-only) | `SeoService.cs:644-773` |
| Storefront config projection | `storefront_configs` (Mongo) | `StorefrontConfigProjectionConsumer` (derivative) | `StorefrontConfigProjectionConsumer.cs:57` |
| Homepage read model | `marketplace_homepage_read_models` (Mongo) | `HomepageProjectionConsumer` (derivative) | marketplace projection service |

---

## Runtime Does NOT Own

- Product/pricing/inventory/category truth (reads projections).
- Payment lifecycle, order mutation, shipping fulfillment (reads lists/quotes/tracking).
- Treasury, accounting GL, notifications (zero authority).
- Media binary storage (free-form URLs only).
- Customer identity issuance (reads customer JWT; identity owned by Identity runtime).
- Affiliate attribution writes (storefront is a redirect/entry read surface).
- CommerceHub marketplace listings ownership (reads the marketplace projection; Super Admin owns
  marketplace content authority).

---

## Architecture Principles

1. **SQL is the single source of truth for presentation.** Every Builder mutation lands in
   PostgreSQL before any derivative is touched. `StoreConfig.cs:11-15`.
2. **Draft/live separation on every authored surface.** Pages, navigation, and theme each carry
   `draft` and `live` JSON; publish promotes draft -> live atomically.
   `AdminStoreConfigService.cs:308-318,383-396`; `:337-396` (page); `:367-427` (navigation via
   `AdminCmsService`); `:581-671` (theme).
3. **Publish is atomic promote + event stage, never partial.** Demote current live, promote newest
   draft, bump version, stage outbox event in one transaction. `CmsPageService.cs:299-326`;
   `SuperAdminHomepageSectionManagementService.cs:352-404`.
4. **Optimistic concurrency + distributed publish lock.** `RowVersion` guards silent overwrites;
   `IPublishLockService.TryAcquireAsync(tenantId, "page:{id}"|"theme"|"rollback")` serializes
   publishes. `AdminStoreConfigService.cs:218,336-341,592-597,698-703`.
5. **Version history + non-destructive rollback.** `VersionsJson` snapshots every publish; rollback
   restores historical live state as a NEW publication, never rewinding `LiveVersion`
   (`AdminStoreConfigService.cs:402,719-763`).
6. **Projection is event-driven, fail-closed, freshness-fenced.** Mongo/Redis hydration only after
   SQL tenant lineage is proven; stale/duplicate events collapsed by `ProjectionEventFence` +
   `LastEventVersion` filter. `StorefrontConfigProjectionConsumer.cs:170-185,144-166,224-500`.
7. **Storefront reads are cache-first but SQL-recomposed for truth.** Redis is primary data source;
   on miss it reads Mongo/SQL; discovery references and visibility are re-verified against live SQL
   so stale projections can't serve dead content. `StorefrontHomeService.cs:30-49,164-181`;
   `HomepageSectionReadService.cs:52,249`.
8. **Read failures fail safe, derivative failures fail closed.** Redis miss = WARNING, continue.
   SQL/Mongo authoritative read failure = 503. `StorefrontHomeService.cs:38-42`.
9. **Storefront is read-only for customers.** No customer request writes `StoreConfig`, `CmsPage`,
   `HomepageSection`, catalog, inventory, payment, or orders. `StoreConfig.cs:13`.
10. **Localization is data, not a runtime.** AR/RTL are fields (`labelAr`, preset `IsRtl`,
    `*Json` localization) selected at read time; cache is language-agnostic.
11. **Marketplace is an isolated parallel plane.** `Marketplace*` entities/services/collections are
    Super-Admin-owned and never collide with tenant content.

---

## Core Discovery

### What is Storefront?

The **Storefront** is the public, customer-facing READ / presentation surface. In code it is:
- A set of read-only API services under `PointOfSale.API/Services/Storefront/` and
  `PointOfSale.API/Services/Homepage/` and `PointOfSale.API/Services/Cms/`.
- Two frontend projects relate to it:
  - `PointOfSale.Storefront` — the **canonical public storefront SPA** (React + Vite + TS, React
    Router 6, multi-tenant, client-rendered). It is the customer experience host: homepage,
    products, cart/checkout, account/orders, wishlist, affiliate funnel, theme-rendered sections.
    `bootstrap.ts` resolves tenant -> session -> store config -> `applyTheme`.
  - `PointOfSale.Web` — the **admin/CMS host**; the POS Admin `StorefrontBuilderPage` and Super Admin
    `HomepageBuilderPage` live here (it is NOT the public storefront). It also exposes marketplace
    public pages.
- It renders published content from Redis/Mongo/ES/SQL, recomposed against SQL authority.

### What is Builder?

The **Builder** is the POS Admin visual authoring surface — the WRITE half. In code it is:
- `StorefrontBuilderPage.jsx` + `modules/storefront/components/builder/*` (React editor).
- Backed by `AdminStoreConfigService` (pages, product templates, theme, versioning, rollback) and
  `AdminCmsService` (navigation) and `CmsPageService` (CMS pages).

### Are they identical? Separate? Nested? Publisher? Projection? Renderer?

They are **two coupled runtimes of ONE bounded context**, NOT identical and NOT nested:
- The Builder is the **author + publisher** (writes SQL, stages publish events).
- The Storefront is the **projection consumer + renderer** (reads derivatives, recomposes SQL).
- They share exactly one SQL authority (`StoreConfig` + `CmsPage` + `HomepageSection`).
- The Builder's output (draft) becomes the Storefront's input (live) only through the publish
  transaction + outbox + projection pipeline.

They are distinct enough that a homepage-authoring divergence exists: the Builder authors
`StoreConfig.pages[home]` but the Storefront homepage renders Super Admin `HomepageSection`
(see Authority Divergence). So "Storefront" and "Builder" are NOT the same runtime and must not
be collapsed.

---

## Builder Runtime

The Builder authors the following surfaces. Each has a distinct owner and implementation:

### Page Builder
- **Owner**: POS Admin (tenant). **Storage**: `StoreConfig.CustomizationJson.pages[pageId].draft/live`.
- **API**: `AdminStoreConfigService.UpdatePageLayoutAsync` (draft) / `PublishPageAsync` (live).
- **Evidence**: `AdminStoreConfigService.cs:258-324` (UpdatePageLayout writes `pages[pageId].draft`);
  `:327-446` (PublishPage copies draft->live, bumps version, snapshot, audit).
- Allowed section types are an explicit whitelist: hero-banner, product-grid, category-grid,
  subcategory-grid, text-block, image-block, video-block, newsletter-signup, testimonials,
  featured-products, product-hero, product-gallery, product-specs, product-video, customer-reviews,
  related-products (`AdminStoreConfigService.cs:58-76`). Unknown types are rejected.

### Theme Builder
- **Owner**: POS Admin (tenant). **Storage**: `StoreConfig.ThemePresetKey` + `Theme*OverridesJson`.
- **API**: `SelectThemePresetAsync` (draft preset key), `UpdateThemeOverridesAsync` (draft overrides),
  `PublishThemeAsync` (promote draft -> live).
- **Evidence**: `AdminStoreConfigService.cs:491-513` (GetThemePresets from `ThemePresets`);
  `:516-548` (SelectThemePreset, draft only, clears draft overrides); `:550-578` (UpdateThemeOverrides,
  draft only, NO event); `:580-671` (PublishTheme promotes `ThemeLivePresetKey`/`ThemeLiveOverridesJson`,
  audit, distributed lock).

### Layout Builder / Section Builder / Block Builder
- Realized as the page **section** model above. There is no separate "layout"/"section"/"block"
  aggregate — sections are typed JSON blocks inside a page's `draft`/`live` composition, validated
  against `AllowedBuilderSectionTypes` + per-type `AllowedBuilderSectionSettingKeys`
  (`AdminStoreConfigService.cs:78-97`). Reserved layout-authority keys (`layoutId`, `containers`,
  `gridStructure`, `visibilityRules`, ...) are blocklisted to prevent injecting layout-control JSON
  (`:40-56`).

### Navigation Builder
- **Owner**: POS Admin (tenant). **Storage**: `StoreConfig.CustomizationJson.navigation.draft/live`.
- **API**: `AdminCmsService.UpdateNavigationAsync` (draft) / `PublishNavigationAsync` (live, stages
  `NavigationPublishedEvent`, bumps `LiveVersion`).
- **Evidence**: `AdminCmsService.cs:304-365` (UpdateNavigation writes `navigation.draft`); `:367-427`
  (PublishNavigation copies draft->live, requires published CMS slugs + live category slugs, emits event).
- No standalone `Menu`/`MenuItem` table — navigation is JSON inside `StoreConfig` (confirmed:
  `AdminCmsService.cs:332-359`).

### Homepage Builder (two distinct systems — divergence, frontend-confirmed)
- **POS Admin homepage builder**: authors `StorefrontBuilderPage.jsx` ->
  `config.pages[home].sections` via `storeConfigService.updatePageLayout` (PUT `/admin/store-config/pages/:id`,
  draft only) + `publishPage` (POST `.../publish`). Output consumed by `BuilderCanvas` preview only
  (`StorefrontBuilderPage.jsx:171-186,303-376,528-569`).
- **Super Admin homepage builder**: authors `HomepageSection` SQL rows via
  `SuperAdminHomepageSectionManagementService` + `SuperAdminHomepageSectionsController` +
  `HomepageBuilderPage.tsx` -> `/homepage/sections`.
- **Public render confirms the divergence**: the customer SPA `HomePage.tsx:33` calls
  `useHomepageSections()` -> `GET /api/homepage/sections` (Super Admin `HomepageSection`), rendered
  by the shared `SectionRenderer` (`HomePage.tsx:5-10,53-58`). It NEVER reads `StoreConfig.pages[home]`.
  The POS builder output is not the rendered homepage (`StorefrontBuilderPage.jsx:184` vs `HomePage.tsx:33`).

### Landing Pages / Dynamic Pages
- Realized as ordinary `CmsPage` rows with slugs (no separate entity/service). "Landing Page" /
  "Campaign Page" / "Marketing Page" runtime = CMS page with a slug. (Verified: CMS runtime doc,
  `cms-runtime-final-architecture.md` "Verified-ABSENT".)

### Product Template Builder
- **Owner**: POS Admin. **Storage**: `StoreConfig.CustomizationJson.product_templates[templateId].draft/live`.
- **Evidence**: `AdminStoreConfigService.cs:277-300` (UpdatePageLayout special-cases product template
  keys); `:449-489` (DeleteProductTemplate).

---

## Publishing Runtime

The prompt's Draft -> Preview -> Validation -> Publish -> Projection -> Cache -> Public Storefront
pipeline is implementation-backed as follows.

### States that actually exist (implementation-backed)

| State | Surface | Implementation |
|---|---|---|
| Draft | Pages/templates/nav/theme/CMS | `*.draft` JSON in `StoreConfig`; `CmsPage.IsDraft=true,IsPublished=false`; `HomepageSection.IsDraft` |
| Live/Published | Pages/templates/nav/theme/CMS | `*.live` JSON; `CmsPage.IsPublished=true,IsDraft=false,PublishedAt`; `HomepageSection.IsPublished` |
| Deleted (soft) | CMS/nav/section | `CmsPage.IsDeleted`; `HomepageSection.IsDeleted`; `StoreConfig` has no soft-delete (version rollback only) |
| Maintenance/Disabled | Storefront | `StoreConfig.StorefrontStatus` in {live, maintenance, disabled} (`StoreConfig.cs:59`) |
| Preview | Draft view | `StorefrontPreviewService` reads draft SQL directly; gated to authorized roles (`StorefrontPreviewService.cs:119-192`) |

### States that do NOT exist (verified absent)
- No `Archived` state, no `Approved`/approval workflow on any surface (CHECK constraints enforce
  Draft|Published|Deleted only).
- No scheduling / auto-publish / expiry: no `PublishDate`/`ExpirationDate` columns; no scheduling
  Hangfire job. Publish is immediate on a privileged action.
- No separate "staged" or "queued" publish state beyond the in-flight distributed lock.

### The publish chain (per surface)
1. **Page publish**: `PublishPageAsync` copies `draft->live`, creates version snapshot, commits,
   audits, releases lock. It does NOT itself emit a Mongo-invalidating event for pages — page
   publish updates `StoreConfig`, which the generic `StoreConfig*` event consumers pick up
   (`AdminStoreConfigService.cs:327-446`).
2. **Theme publish**: `PublishThemeAsync` promotes `ThemeLive*`, audits, no direct event but the
   `StoreConfig` update path stages `StoreConfigPublishedEvent` (`AdminStoreConfigService.cs:580-671`;
   `:942-995` event staging in `PersistStoreConfigChangeAsync`).
3. **Navigation publish**: `PublishNavigationAsync` copies draft->live, appends a `VersionsJson`
   snapshot, bumps `LiveVersion`, stages `NavigationPublishedEvent`, invalidates cache
   (`AdminCmsService.cs:367-427`).
4. **CMS page publish**: `CmsPageService.PublishPageAsync` demotes published, promotes draft, stages
   `StorefrontCmsPagePublishedEvent`, invalidates published scope (`CmsPageService.cs:283-339,328`).
5. **Homepage section publish**: `PublishSectionsAsync` demotes all published, promotes all draft in
   a transaction, stages `HomepageSectionsPublishedEvent` before the final save/commit, then
   invalidates cache after commit (`SuperAdminHomepageSectionManagementService.cs:314-404`).
6. **Projection**: outbox drains events -> consumers write Mongo (`storefront_configs`,
   `storefront_pages`, `marketplace_homepage_read_models`) and invalidate Redis.
7. **Cache**: Redis read path serves the new state; stale keys invalidated by the relevant consumer.

### Validation (implementation-backed)
- Section type/setting whitelist enforced at draft write (`AdminStoreConfigService.cs:58-97`).
- Navigation publish requires published CMS slugs + live category slugs
  (`AdminCmsService.cs:407-410`).
- Theme override normalization (`NormalizeThemeOverridesAsync`, `:565`).
- Storefront "live" publish requires owner email verified (`StorefrontVerificationException`,
  `AdminStoreConfigService.cs:172-188`).
- Homepage section schema validation via `IHomepageSectionSchemaRegistry.ValidateAndNormalize`
  (`SuperAdminHomepageSectionManagementService.cs:140`).

---

## Theme Runtime

- **Theme** = a `ThemePresets` catalog row (`Key`, `Name`, `IsRtl`, `BaseThemeJson`, `PreviewImageUrl`)
  selected by `StoreConfig.ThemePresetKey` (`AdminStoreConfigService.cs:491-513`).
- **Template/Layout/Section/Widget/Component/Slot/Block**: all realized as typed JSON sections inside
  a page; there is no separate template/layout/slot entity. The section whitelist enumerates the
  widget/block vocabulary (`hero-banner`, `product-grid`, etc.).
- **Configuration/Overrides**: `ThemeDraftOverridesJson` -> `ThemeLiveOverridesJson` (colors/fonts/ui/
  layout as CSS-var-shaped JSON). Switching preset clears draft overrides
  (`AdminStoreConfigService.cs:536-540`).
- **Version/Rollback**: theme publish appends a `VersionSnapshot` to `VersionsJson`; rollback restores
  historical `ThemeLive*`.
- **Customization/Inheritance/Overrides**: preset is the base; overrides layer on top at render time.
  No deep inheritance chain — single preset + override set.
- **RTL/LTR**: `ThemePresets.IsRtl` drives RTL rendering in the frontend.

---

## CMS Integration Runtime

- **Pages**: `CmsPage` (tenant, POS Admin `Owner,Admin`) + `MarketplaceCmsPage` (Super Admin). Author =
  POS Admin for tenant; Super Admin for marketplace.
- **Blocks/Content**: RichText or Blocks content; free-form URL images (no upload pipeline).
- **Media**: free-form safe URL strings (`CmsPageContentValidator.NormalizeSafeUrl`).
- **Menus**: navigation JSON in `StoreConfig` (not CMS).
- **SEO**: per-page `MetaTitle`/`MetaDescription`/`MetaKeywords`/`OpenGraphImageUrl`; derived canonical/
  breadcrumb; sitemap recomputes SQL.
- **Localization**: `labelAr`/`*Json` AR fields; resolved at read.
- **Dynamic Content**: CMS pages are the dynamic-content mechanism; no separate runtime.

---

## Navigation Runtime

- Menus = header/footer link lists in `StoreConfig.CustomizationJson.navigation.{draft,live}`.
- Categories resolve to slugs via `CategoryLineageAuthority` (live lineage only)
  (`AdminCmsService.cs:466-479`).
- Routing/breadcrumb/canonical = derived in `SeoService` (`StorefrontUrlResolver`, `BuildCanonicalUrl`).
- Slug resolution supports `RequiresRedirect` (`SeoService` slug resolution).
- Redirect = slug-level only; no standalone URL-rewrite admin (verified absent).

---

## Product Catalog Integration

- Storefront reads product projections (Mongo `ProductDocument`) via `IProductReadRepository`, but
  re-derives visibility/pricing against SQL authority (`ProductSearchSqlAuthorityOverlay`,
  `RecomposeProductCardsFromSqlAuthorityAsync`).
- Shelves: Featured (Mongo, 30m TTL), New Arrivals (Mongo, 15m), Best Sellers (ES->Mongo fallback,
  15m), Curated (explicit IDs), Categories (60m) — `StorefrontHomeService.cs:69-103`.
- `OnlinePrice` is storefront pricing source of truth (falls back to `SalePrice`).
- Inventory visibility = `IsActive && IsOnlineVisible` only.

---

## Search Runtime Integration

- **Product SEARCH = Elasticsearch SINGLE authority (no Mongo fallback in the search path):**
  `ProductSearchService.SearchProductsAsync` throws `StorefrontSearchUnavailableException` on ES
  failure -> 503 (`ProductSearchService.cs:82-94`; `StorefrontProductsController` catches -> 503 at
  `:433,448`). ES returns IDs, hydrated from Mongo, then **re-overlaid with live SQL authority**
  (`ProductSearchSqlAuthorityOverlay.ApplyAsync`, filtering `IsActive && IsOnlineVisible`) —
  Mongo is a hydration store, not a search fallback.
- Best Sellers shelf uses ES then falls back to Mongo New Arrivals (degraded, WARNING).
- Product projection (Mongo) is SQL-authority recomposed; `ProductSqlAuthorityProjectionWriter`
  tombstones when SQL no longer proves `IsActive && IsOnlineVisible`.
- CMS IS partially indexed in ES (`cmspages` index, `CmsPageSearchDocument`) but ONLY via the admin
  manual-sync `AdminSearchSyncController` (`UnifiedSearchIndexService.IndexCmsPageAsync`) — **no
  event-driven consumer writes it** (verified absent in Consumers). The unified storefront search
  reads `cmspages` with a SQL-authority visibility filter and 503s on failure. So CMS->ES is
  admin-triggered, not publish-driven.
- Freshness: search ranking promotion-aware via `PromotionRankingConfig` (StoreConfig-owned,
  cached in Redis) (`StoreConfig.cs:304-398`).

---

## Customer Experience Runtime

- **Homepage**: the public SPA `HomePage.tsx:33` consumes `useHomepageSections()` ->
  `GET /api/homepage/sections` (Super Admin `HomepageSection`), rendered via the shared
  `SectionRenderer` (`HomePage.tsx:5-10,53-58`). It does NOT read `StoreConfig.pages[home]`
  (the POS builder output) — divergence confirmed at the frontend (`StorefrontBuilderPage.jsx:184`
  authors `config.pages[home].sections`; `HomePage.tsx:33` consumes `/api/homepage/sections`).
- **Shelves (server-computed, surfaced by section components)**:
  - Featured = authorable `featured-products` section whose *display* is configurable but whose
    product list is server-driven (`featured-products.tsx:8-15`);
    also `StorefrontHomeService` featured shelf.
  - `product-grid` section resolves `source ∈ {curated,random,all}` (`homepageCollectionUtils.ts:30-41`);
    `curated` -> `fetchHomepageProductsByIds`, else `fetchProductsDiscover` (Elasticsearch);
    `random` = deterministic seed-rank shuffle (`:71-105`).
  - **New Arrivals / Best Sellers / Cross-Sell / Up-Sell / Recently Viewed**: **verified absent as
    named frontend features** — no component/endpoint/section type references them by name. Only
    generic `discover`/`useProducts` feeds grids (`useHomepageSectionProducts.ts:96-119`).
  - `related-products` section = category-based, client-filtered, server-listed (`related-products.tsx:24-32`),
    not a dedicated recommendation engine; `cross-sell/up-sell` are NOT distinct (verified absent).
- **Recommendations / Trending**: only appear in analytics event builders, not as product shelves.
- **Wishlist / Favorites**: **API-backed, per customer, NOT localStorage** (`wishlistStore.ts`
  calls `wishlistApi.*`; `useWishlist.ts:69` requires auth; route `/wishlist`). Guest add/remove
  blocked; no local persistence.
- **Recently Viewed / Browsing History**: **verified absent** in the frontend (no history store/hook;
  only `window.history` for checkout navigation). Frontend personalization is local-first only for
  language preference and (DEV-only) theme override (`languageStore.ts:165-179`; `ThemeResolver.ts:3,22-27`).

> Net: the storefront exposes merchant-authored sections + server shelves + an API-backed wishlist.
> "New arrivals / best sellers / cross-sell / up-sell / recently viewed" personalization surfaces
> named in the prompt are NOT implemented in the frontend; any such curation would have to come from
> the server `StorefrontHomeService` shelves, which themselves are not surfaced via dedicated sections.

---

## Public Projection Runtime

SQL (StoreConfig/CmsPage/HomepageSection) -> outbox event -> Mongo/Redis/ES -> Storefront.

- **Who owns truth**: SQL (Builder write path).
- **Who owns projection**: the consumers (`StorefrontConfigProjectionConsumer`, `CmsPageProjectionConsumer`,
  `NavigationProjectionConsumer`, `HomepageProjectionConsumer`) — derivatives only.
- **Who owns freshness**: `ProjectionEventFence` + `LastEventVersion`/`LastEventId` optimistic filters
  + Redis TTLs. SQL is the freshness arbiter on read (recomposition).

---

## Media Runtime

- Product media = `ProductMedia` (validated images/videos, host-checked).
- Homepage/CMS media = free-form URL strings (no optimization/CDN/thumbnail). `IMediaService` general
  upload authority is NOT wired into builder authoring.
- No CDN pipeline in storefront presentation (verified absent in content management notes).

---

## SEO Runtime

- Meta Title/Description/Keywords/OG image: authorable per CMS page (`CmsPageService`).
- OpenGraph mapped (`og:title/description/image/type/url`); Twitter Card (`twitter:*`) NOT emitted
  (verified absent).
- Schema.org/JSON-LD: ONLY breadcrumb JSON-LD (`SeoService.BuildBreadcrumbJsonLd`); CMS pages have no
  JSON-LD projection.
- Canonical URL derived (`BuildCanonicalUrl` via `StorefrontUrlResolver`).
- Sitemap: JSON (`/api/storefront/sitemap`) + XML; recomputed from SQL every read (6h cache).
- Robots: `text/plain` with tenant sitemap URL (12h cache).
- CMS SEO `Index` hardcoded true (`SeoService.cs:1055`) — noindex unenforceable (debt).

---

## Localization Runtime

- Language EN/AR; `?lang`/`Accept-Language` selects `*Localized` fields at read (cache language-agnostic).
- **Frontend**: language from `/ar`/`/en` URL prefix via `LanguageWrapper` (`app/router.tsx:65-76`);
  `applyLanguageToDocument` sets `html.lang` + `html.dir` (rtl/ltr) (`languageStore.ts:99-112`);
  RTL UI via `isRTL`/`rtl-flip`/`rotate-180` (`HomePage.tsx:107`, `ProductListPage.tsx:337-485`).
- Dual fields `name`/`nameAr`; `getLocalizedField` picks `nameAr` when `language==='ar'`
  (`useTranslation.ts:86-104`); i18n via `locales/ar.json`+`locales/en.json`.
- Currency display from tenant `Currency` (resolved in projection consumer, `StorefrontConfigProjectionConsumer.cs:443-452`);
  **frontend must come from API or throw `CurrencyNotLoadedError`** (`cartStore.ts:9-17,196`; `utils/currency.ts`
  `getCurrencySymbol`/`formatPrice`) — currency display only, never currency authority.
- Localized SEO/URL: slug + `labelAr` fields; hreflang `<link rel="alternate">` injected for en/ar +
  x-default (`HrefLangLinks`); **explicit `<link rel="canonical">` writer and `/robots.txt` fetch are
  verified absent in the frontend** (only hreflang + `robots` meta).

---

## Personalization Runtime

- **Guest vs customer**: `useAuth()` -> `{ isAuthenticated, emailVerified, customer }`; guest cart
  migrates to customer on login via `mergeCart()` (`storefrontApi.ts:323-329`); guest add-to-wishlist
  blocked (`useWishlist.ts:69`).
- **Wishlist / Favorites**: API-backed per customer (Zustand `wishlistStore` -> `wishlistApi.*`),
  NOT localStorage. No local persistence.
- **Recently Viewed / Browsing History**: **verified absent** in the frontend (no history store/hook).
- **Local-first personalization is limited to language preference** (`languageStore.ts`, localStorage
  `'storefront-language'`) and a DEV-only theme override (`ThemeResolver.ts`, `'storefront_theme'`).
  Product/history personalization is not stored client-side.
- **No server-side personalized homepage authority** — homepage composition is merchant-authored
  (HomepageSection) + automatic shelves. Personalization is presentation-layer only.

---

## Cache Runtime

- **Redis**: primary data source for storefront reads; explicit per-surface TTLs (homepage shelves
  15-60m; SEO metadata 30m, breadcrumb 60m, slug 60m, sitemap 6h, robots 12h).
- **Memory cache**: in-process where applicable.
- **CDN/Browser cache**: not implemented in presentation runtime (verified absent).
- **Cache busting / invalidation**: publish events -> consumers delete keys.
  `StorefrontConfigProjectionConsumer` deletes the `StoreConfigKey` (`:375-385`);
  `NavigationProjectionConsumer` self-invalidates `storefront:navigation|footer|menu|header:{slug}`
  (`:462-482`). `RedisInvalidationConsumer` handles CMS/homepage/footer/marketplace events
  (`HomepageSectionsPublishedEvent`, `StorefrontCmsPage*`, `MarketplaceCmsPage*`,
  `StorefrontFooterUpdatedEvent`, `MarketplaceFooterUpdatedEvent`, marketplace review/order/payment).
  Navigation/StoreConfig events are intentionally NOT in `RedisInvalidationConsumer` — their own
  projection consumers own invalidation.
- **Warmup/TTL**: cache populated best-effort on miss.
- **Failure semantics**: Redis read failure = WARNING, continue to source; authoritative Mongo/SQL
  read failure = 503 (`StorefrontHomeService.cs:38-42`).

---

## Projection Runtime

- **Pipeline**: SQL write -> outbox `OutboxMessage` -> `OutboxPublisherJob` (Hangfire, 5s) -> RabbitMQ
  -> MassTransit consumers -> Mongo/Redis.
- **Authority**: SQL; projections fail-closed if lineage unproven.
- **Replay/Repair**: `ProjectionEventFence` collapses duplicates/replays; freshness filter rejects
  stale `LastEventVersion`.
- **Version**: `EventVersion`/`AggregateId` ordering in outbox (critical events ordered by
  (TenantId, AggregateId, EventVersion)); `StorefrontConfigProjectionConsumer` preserves
  independent currency freshness watermark.
- **Outbox**: `OutboxMessage` entity (`Core/Entities/OutboxMessage.cs`: EventType, TenantId,
  AggregateId, EventVersion, RetryCount, MaxRetries=5, `RequiresOperatorReconciliation`);
  `OutboxPublisherJob` is a Hangfire recurring job every **5s**, BatchSize 100, **7-day retention**,
  critical-event poison slow-replay 15m, orders critical events by (TenantId, AggregateId,
  EventVersion) so stale versions can't overtake newer state.
- **Mongo read models — read vs orphaned (verified per-collection):**
  - `storefront_pages` (**orphaned/write-only**): written by `CmsPageProjectionConsumer`, but the
    published read path (`CmsPageReadService`) queries SQL `CmsPages` directly, never the Mongo
    collection.
  - `storefront_navigation` (**orphaned/write-only** for the runtime read path): written by
    `NavigationProjectionConsumer`; `StorefrontNavigationController` re-derives navigation from live
    SQL `StoreConfigs` + SQL category/CMS authority. No API reader injects the Mongo doc.
  - `storefront_footer` (partial): written by `StorefrontFooterProjectionConsumer`;
    `StorefrontFooterReadService` re-derives from SQL footer/CmsPage; cached via
    `IStorefrontFooterCacheService`.
  - `storefront_configs` (partially read): read by `CartService` + AI `CommerceContextBuilder`; the
    public storefront config API re-derives from live SQL. Not the customer-facing read source.
  - `marketplace_homepage_read_models` (**read**): read by `MarketplaceHomepageReadRepository` via
    `MarketplaceReadController` — the one storefront-content Mongo projection that IS consumed.
  - `ProductDocument` (**read**): storefront product shelves/search hydration read from Mongo, then
    re-overlaid with SQL authority.
  - **Pattern**: content read services are cache-first (Redis TTL) then re-derive against live SQL;
    Mongo storefront *content* projections are largely orphaned except the marketplace homepage
    read model + `storefront_configs` (cart/AI) + product docs.

---

## Commerce Runtime Integration

The storefront is a READ-ONLY customer surface. Across all peer runtimes, storefront writes either
land in Redis/cart/event-outbox scratch space only, are *delegated* to a non-storefront authority
(CheckoutController -> PostgreSQL order authority; CommerceHubCheckoutController -> marketplace order
authority), or are verified absent. (Full evidence: subagent pass over `PointOfSale.API/Controllers/
Storefront`, `PointOfSale.API/Services/Storefront`, `PointOfSale.Infrastructure/Analytics`.)

| Peer | Storefront interaction | Authority | Evidence |
|---|---|---|---|
| Identity | Separate `StorefrontJwt` scheme (OnlineCustomer, NOT User); `ITenantContext.RequiredTenantId`; X-Tenant-Id stripped inbound + 403 on spoof | Read-only; identity owned by Identity runtime | `StorefrontAuthController.cs:18-23,397`; `ITenantContext.cs:8-25`; `TenantResolutionMiddleware.cs:81-82,106-129` |
| Currency | Reads `Tenants.Currency` via StoreConfig; fails closed if unset | Display only; no `ICurrency*` service | `StorefrontStoreConfigController.cs:83-119` |
| Inventory | `IsOnlineVisible` + stock display from projection recomposed vs SQL | Read-only; no mutation | `StorefrontProductsController.cs:487,520-532`; `StorefrontProductService.cs:16-32,93` |
| POS / Order | `StorefrontOrdersReadController` GET-only, owner-checked; no order mutation | No order mutation | `StorefrontOrdersReadController.cs:21-31,93,127,206-300` |
| Payment | `StorefrontPaymentMethodsController` read-only; no secrets; `IPaymentLifecycleService` not referenced | No payment authority | `StorefrontPaymentMethodsController.cs:19-24,76,122-143` |
| Shipping | `StorefrontShippingMethodsController`/`Quotes` read-only quotes computed backend | No shipping authority | `StorefrontShippingMethodsController.cs:21-25,96`; `StorefrontShippingQuotesController.cs:12-25,114` |
| CommerceHub | Reads Mongo/ES listing/product projection recomposed vs SQL; marketplace = separate content plane | Marketplace content owned by Super Admin | `StorefrontProductService.cs:35`; `ProductReadRepository.cs:11-45`; `PublicAffiliateProductsController.cs:21-30,75-88,299-346` |
| Reporting/Analytics | `StorefrontAnalyticsAuthorityReader` reads PG ledger (never Mongo/Redis for financial metrics); dashboards aggregate; event *capture* to outbox only | Analytics owned by Reporting runtime | `StorefrontAnalyticsAuthorityReader.cs:31-35,52-141`; `StorefrontAnalyticsDashboardService.cs:42-87` |
| Affiliate | Read discovery + funnel redirect (Redis cart stamp of `AffiliateAccountId`); no commission/click write | No attribution writes | `PublicAffiliateController.cs:18,26`; `StorefrontFunnelController.cs:45-68,125` |
| Treasury | None | Verified absent | grep `Treasury|ITreasury` in storefront tree -> 0 matches |
| Notifications | None (only consumes `IEmailSender` as side-effect) | Verified absent | grep `INotification|NotificationService` -> 0 matches |
| Cart / Checkout | Cart = Redis scratch; order creation delegated to `CheckoutController` (tenant) / `CommerceHubCheckoutController` (marketplace) | Checkout writes orders as the *checkout authority*, NOT a storefront read op | `CheckoutController.cs:32-49`; `CommerceHubCheckoutController.cs:16,90` |

---

## Runtime State Machines

### Storefront status
`live` <-> `maintenance` <-> `disabled` (via `StorefrontStatus`, `StoreConfig.cs:59`). Navigation/
footer return empty when not `live`.

### CMS page state
`Draft` (IsDraft,!IsPublished) -> `Published` (!IsDraft,IsPublished,PublishedAt) -> `Deleted` (soft,
IsDeleted). XOR enforced by CHECK constraint. (No Archived/Approved.)

### Homepage section state
`Draft` -> `Published`; `IsDeleted` soft. Draft workspace per scope; publish promotes whole workspace.

### Theme state
`ThemeDraftPresetKey`/`ThemeDraftOverridesJson` -> promote to `ThemeLive*` on PublishTheme.

### Page/navigation state
`pages[id].draft` -> `pages[id].live` on PublishPage; `navigation.draft` -> `navigation.live` on
PublishNavigation.

### Cache state
Key present (served) -> key deleted on publish event -> repopulated on next read (TTL-bounded).
- `NavigationPublishedEvent` Redis invalidation is owned by `NavigationProjectionConsumer`
  (`:196-205,462-475`), NOT by `RedisInvalidationConsumer`.
- `RedisInvalidationConsumer` subscribes to `HomepageSectionsPublishedEvent`,
  `StorefrontCmsPagePublishedEvent`/`DeletedEvent`, `MarketplaceCmsPage*`, `StorefrontFooterUpdatedEvent`,
  `MarketplaceFooterUpdatedEvent`, and marketplace review/order/payment events — but NOT navigation
  (`RedisInvalidationConsumer.cs:20-35`).

### Projection state
Mongo doc `LastEventVersion` monotonic; stale events rejected; fail-closed delete on unproven lineage.
- `StorefrontConfigProjectionConsumer` -> Mongo `storefront_configs` + Redis config-key delete
  (`StorefrontConfigProjectionConsumer.cs:57,375-385`).
- `CmsPageProjectionConsumer` -> Mongo `storefront_pages`, FAILS CLOSED (tombstone) when SQL tenant
  lineage unproven (`CmsPageProjectionConsumer.cs:19-35,97,123,304-397`).
- `NavigationProjectionConsumer` -> Mongo navigation + own Redis invalidation
  (`NavigationProjectionConsumer.cs:28,196-205,462-475`).
- `StorefrontFooterProjectionConsumer` -> Mongo footer.
- `HomepageProjectionConsumer` (Marketplace) -> Mongo `marketplace_homepage_read_models`.

---

## Runtime Authority Matrix

| Surface | Author (write) | Mutator | Reader | Event consumer | Projection writer | Runtime truth |
|---|---|---|---|---|---|---|
| Store Config (pages/templates/theme/identity) | POS Admin Builder | AdminStoreConfigService | Storefront read + preview | StorefrontConfigProjectionConsumer | StorefrontConfigProjectionConsumer (Mongo) | SQL StoreConfig |
| Navigation | POS Admin | AdminCmsService | Storefront nav read | NavigationProjectionConsumer | NavigationProjectionConsumer (Mongo+Redis) | SQL StoreConfig |
| CMS Pages (tenant) | POS Admin Owner/Admin | CmsPageService | CmsPageReadService (SQL) | CmsPageProjectionConsumer | CmsPageProjectionConsumer (Mongo, orphaned) | SQL CmsPage |
| Homepage Sections | Super Admin | SuperAdminHomepageSectionManagementService | HomepageSectionReadService | HomepageProjectionConsumer | Marketplace Mongo | SQL HomepageSection |
| Footer | POS Admin/Super Admin | footer write svc | StorefrontFooterReadService | StorefrontFooterProjectionConsumer | Mongo | SQL footer |
| Theme | POS Admin | AdminStoreConfigService | storefront render | StorefrontConfigProjectionConsumer | Mongo | SQL StoreConfig |
| Products (shelves/search) | Catalog runtime | catalog svc | StorefrontHomeService | product consumers | Mongo/ES | SQL catalog (overlay) |
| SEO | derived | SeoService (read) | SeoService | n/a | Redis | SQL recompute |
| Cart / Checkout | Storefront cart (Redis scratch) + delegated `CheckoutController`/`CommerceHubCheckoutController` | CheckoutService / CommerceHub checkout (checkout authority, NOT storefront) | storefront cart/checkout controllers | n/a | Redis cart | PostgreSQL orders (via CheckoutService) |

---

## Runtime Authority Divergence

- **Draft != Published**: runtime serves `live`/`IsPublished` only; draft needs Super Admin / preview
  token (`StorefrontPreviewService`, signed `previewToken`).
- **CMS != Storefront**: CMS Mongo `storefront_pages` is orphaned — storefront reads SQL CMS directly.
- **SQL != Mongo**: Mongo is derivative; SQL overlay re-derives visibility/pricing on read.
- **Projection != Public**: projection is eventual; Redis TTL can serve stale up to TTL after publish.
- **Cache != Projection**: cache is primary; projection refresh may lag cache invalidation race.
- **Search != Storefront**: ES is search authority; best-sellers shelf falls back to Mongo.
- **Theme != Layout**: theme preset/overrides drive CSS vars; page layout is separate JSON.
- **Homepage != Landing Page**: homepage = HomepageSection (Super Admin); landing pages = CmsPage slugs.
- **Preview != Production**: preview reads draft SQL; production reads live/published.
- **Builder homepage (pages[home]) != rendered homepage (HomepageSection)**: the defining divergence —
  POS Admin Builder authors `pages[home]` but public `HomePage` renders Super Admin `HomepageSection`.
  `pages[home]` is consumed by builder preview + product/category builder pages, not the public render.

---

## CQRS

- **Write Models**: `StoreConfig`, `CmsPage`, `HomepageSection` (SQL aggregates, lifecycle-owning
  services, optimistic concurrency, Serializable tx).
- **Read Models**: Redis (cache-first), Mongo (`storefront_configs`, `storefront_pages`,
  `marketplace_homepage_read_models`, product docs), Elasticsearch (product search).
- **Projection**: event-driven outbox -> MassTransit consumers; fail-closed; freshness-fenced.
- **Replay/Repair**: `ProjectionEventFence` idempotency; stale-event rejection; SQL-recomposition on
  read repairs drift.
- **Search Projection**: ES single authority; no Mongo fallback for search endpoint.
- **Projection Version**: `EventVersion`/`LastEventVersion`; critical-event ordering by
  (TenantId, AggregateId, EventVersion).
- **Projection Recovery**: fail-closed tombstone/delete on unproven lineage; re-projection on next
  event; SQL recompute on read.

---

## Snapshot Runtime

Immutable snapshots taken on publish:
- `StoreConfig.VersionsJson` version snapshot (customization + theme live state) per publish/rollback
  (`AdminStoreConfigService.cs:402,719-763`).
- `HomepageSection` published rows (immutable until next publish).
- `CmsPage` published version (immutable until rollback creates a new version).
- `StorefrontFooterSection` published rows.
- `storefront_configs` Mongo doc carries `LastEventVersion`/`LastEventId` watermark (projection snapshot).

---

## Cross Runtime Authority Graph

See Commerce Runtime Integration table. Key edges:
- Builder -> Catalog: reads catalog projections for section product/category resolution; never writes.
- Builder -> Identity: requires POS Admin `Owner/Admin` role; storefront requires customer JWT.
- Storefront -> Search: ES authority for search; Mongo fallback for shelves only.
- Storefront -> CMS: SQL read of published pages; Mongo orphaned.
- Storefront -> Analytics: read-only authority reader.
- Storefront -> Affiliate: redirect/entry read only.

---

## Business Invariants

(All implementation-backed.)
1. A published storefront MUST serve `live`/`IsPublished` content only; draft is never served to
   customers without preview authorization. (`StoreConfig.cs:13`; `StorefrontPreviewService.cs:13-17`.)
2. A published CMS page MUST satisfy `!IsDeleted && IsPublished && !IsDraft && PublishedAt != null &&
   Tenant.IsActive && Tenant.Status == Active`. (`CmsPageReadService.cs:131-141`.)
3. Theme draft changes MUST NOT affect the public storefront until `PublishTheme`.
   (`StoreConfig.cs:90-103`; `AdminStoreConfigService.cs:612-613`.)
4. Publish MUST be atomic promote (demote prior live, promote newest draft) in one transaction.
   (`CmsPageService.cs:299-326`; `SuperAdminHomepageSectionManagementService.cs:352-404`.)
5. Projection MUST fail closed when SQL tenant lineage is unproven (delete Mongo doc + cache).
   (`StorefrontConfigProjectionConsumer.cs:170-185,418-441`.)
6. Stale projection events MUST be rejected by freshness filter (`LastEventVersion`).
   (`StorefrontConfigProjectionConsumer.cs:224-241,482-500`.)
7. Redis read failure MUST NOT 503; authoritative Mongo/SQL failure MUST 503.
   (`StorefrontHomeService.cs:38-42`.)
8. Storefront MUST never write catalog/inventory/payment/order/accounting/treasury truth.
   (`StoreConfig.cs:13`.)
9. Navigation publish MUST reference only published CMS slugs + live category slugs.
   (`AdminCmsService.cs:407-410`.)
10. Storefront "live" publish MUST require owner email verification.
    (`AdminStoreConfigService.cs:172-188`.)
11. SEO `Index` for CMS pages is hardcoded true (invariant + debt: noindex unenforceable).
    (`SeoService.cs:1055`.)
12. Sitemap/SEO MUST recompute from SQL on every read (never serve stale deleted/unpublished URLs).
    (`SeoService.cs:658-684`.)

---

## Architectural Decisions (WHY, implementation-backed)

1. **Single `StoreConfig` row per tenant with JSON `CustomizationJson`** — avoids schema migrations
   for every new section/layout; structural changes are JSON, not DDL (`StoreConfig.cs:78-82`).
2. **Draft/live JSON split per surface** — lets merchants edit safely and preview without affecting
   production; publish is a cheap copy (`AdminStoreConfigService.cs:308-318`).
3. **Generic outbox + MassTransit consumers (not polling)** — decouples write from projection,
   survives consumer downtime, idempotent replay (`OutboxPublisherJob`, consumers).
4. **Fail-closed projections** — a storefront must never render content for a suspended/dead tenant;
   unproven lineage => delete derivative (`StorefrontConfigProjectionConsumer.cs:418-441`).
5. **SQL-recomposition on read** — derivative stores can drift; re-verifying discovery refs/visibility
   against SQL keeps the storefront correct without a full re-projection (`HomepageSectionReadService.cs:249`;
   `StorefrontHomeService.cs:174-181`).
6. **Cache-first Redis with TTL** — storefront is read-heavy/low-volatility; Redis is the primary
   source, Mongo/SQL are fallback (`StorefrontHomeService.cs:30-49`).
7. **Super Admin owns homepage sections separately from POS Admin page builder** — platform-level
   homepage composition is governed centrally; tenant page builder is for tenant-specific pages
   (authority separation, even at the cost of the `pages[home]` divergence).
8. **ES single authority for search** — search relevance/scale needs a dedicated index; Mongo is only
   a shelf fallback, not search truth.
9. **Distributed publish lock** — concurrent admin publishes could interleave draft/live; lock
   serializes (`IPublishLockService`).

---

## Technical Debt (implementation-backed)

1. **Orphaned CMS Mongo `storefront_pages` projection** — written by `CmsPageProjectionConsumer` but
   never read by the published storefront path (`MapPageWithSafeFallback` overload defined, never
   invoked). Wasteful write + drift risk.
2. **CMS SEO `Index` hardcoded true** — `SeoService.cs:1055` ignores any NoIndex; SQL has no NoIndex
   column, so noindex is unenforceable.
3. **No Twitter Card / CMS JSON-LD** — only `og:*` + breadcrumb JSON-LD emitted; prompt's
   `twitter:*`/schema.org for CMS unimplemented.
4. **No media CDN/optimization/thumbnail in presentation** — homepage/CMS images are raw URLs.
5. **No scheduling/approval workflow** — publish is immediate; no future-dated or reviewer-gated
   publishing (may be a gap vs. expectations).
6. **`pages[home]` builder divergence** — POS Admin homepage builder output is not the rendered
   homepage;商户 may expect their homepage edits to show but they render Super Admin sections.
7. **IsRtl/font whitelist coupling** — theme font whitelist and RTL are preset-bound; per-section RTL
   not modeled.
8. **Storefront checkout coupling** — cart/checkout live in storefront controllers but money/state
   lifecycle is elsewhere; boundary is implicit, not enforced by a hard module boundary.

---

## Implementation Follow-up Items

| Item | Priority | Status |
|---|---|---|
| Retire or wire the orphaned `storefront_pages` Mongo projection | Medium | Open |
| Add `NoIndex` column + honor it in `SeoService` | Low | Open |
| Emit Twitter Card + CMS JSON-LD if required | Low | Open |
| Reconcile `pages[home]` builder output with rendered homepage behavior | High | Documented divergence; behavior open |
| Add media optimization/CDN if SLA requires it | Medium | Open |
| Harden storefront/checkout module boundary | Medium | Open |

---

## Runtime Timeline (one complete lifecycle)

Merchant edits homepage (POS Admin Builder `StorefrontBuilderPage` -> `updatePageLayout`) ->
- **Owner**: POS Admin Builder (`AdminStoreConfigService.UpdatePageLayoutAsync`).
- **Authority**: writes `StoreConfig.CustomizationJson.pages[home].draft`.
- **Synchronization**: draft only; no event.
- **Failure**: validation rejects unknown section type/key.
- **Recovery**: re-edit draft.
- **Projection**: none (draft).
- **Consistency**: live untouched.

-> Draft saved (`StoreConfig.cs` row, `RowVersion` bumped).
-> Validation (section whitelist, `AdminStoreConfigService.cs:58-97`).
-> Preview (`StorefrontPreviewService.GetConfigPreviewAsync` reads draft SQL, gated).
-> Publish (`PublishPageAsync`: draft->live, version snapshot, audit, lock).
- **Owner**: POS Admin Builder.
- **Authority**: `StoreConfig` live JSON now current.
- **Synchronization**: `PersistStoreConfigChangeAsync` stages `StoreConfigPublishedEvent`.
- **Failure**: publish lock not acquired -> `ConcurrencyConflictException`.
- **Recovery**: retry after lock release.
- **Projection**: outbox -> `StorefrontConfigProjectionConsumer` writes Mongo `storefront_configs`,
  invalidates Redis.
- **Consistency**: freshness fence rejects stale events.

-> Projection (Mongo + Redis).
-> Redis invalidate (`StorefrontConfigProjectionConsumer.cs:375-385`).
-> Search (n/a for page publish; product shelves re-cache on read).
-> CDN (not implemented).
-> Public Storefront (customer request -> `HomePage` renders `HomepageSection` + shelves from
   Redis/SQL/ES/Mongo, recomposed against SQL authority).
-> Customer Request (anonymous or customer JWT; `ITenantContext.RequiredTenantId`).
-> Rendering (theme `ThemeLive*` applied; navigation `live`; CMS pages from SQL).
-> Analytics (`StorefrontAnalyticsAuthorityReader` records view, read-only).
-> Reporting (aggregated by Reporting runtime).

Note: for the PUBLIC homepage specifically, the rendered composition is Super Admin `HomepageSection`,
not the POS Admin `pages[home]` the merchant edited — the timeline above describes the page-builder
publish path; the visible homepage is governed by the separate Super Admin homepage publish path
(`SuperAdminHomepageSectionManagementService.PublishSectionsAsync` -> `HomepageSectionsPublishedEvent`
-> `HomepageProjectionConsumer` -> Mongo -> Redis -> `HomepageSectionReadService`).

---

## Runtime Classification

**This runtime is fundamentally a HYBRID: a Presentation + Publishing runtime** (more precisely, a
**merchant storefront presentation-and-publishing runtime**), composed of:

- a **Builder / Publisher** half (POS Admin authoring + atomic publish of `StoreConfig`/CMS/homepage
  composition), and
- a **Storefront / Projection / Renderer** half (public read-only rendering recomposed from
  SQL-authoritative derivatives),

held consistent by an **event-driven CQRS projection pipeline** (outbox -> Mongo/Redis/ES).

It is NOT merely a "Storefront" (that is only the read half) and NOT merely a "Builder" (that is only
the write half). The evidence:
- The same `StoreConfig` SQL authority is written by the Builder and read by the Storefront
  (`StoreConfig.cs:11-15`).
- Publish is the bridge: Builder promotes draft->live and stages outbox events; Storefront consumes
  the projections (`AdminStoreConfigService.cs:327-446`; `StorefrontConfigProjectionConsumer.cs`).
- The Storefront is explicitly read-only and cache-first, while the Builder is the only writer
  (`StorefrontHomeService.cs:23-49`; `StorefrontPreviewService.cs:13-17`).

Therefore the correct classification is **Storefront/Builder Publishing-Presentation Runtime** —
equivalently, a **Content Delivery + Authoring Runtime** for merchant storefronts.

---

## Certification

- **Confidence**: High for the Builder write authority, draft/live/theme/versioning/rollback, publish
  chain, projection fail-closed/freshness, and storefront read-only/SQL-recomposition principles
  (all directly read from implementation).
- **Completeness**: Covers all prompt-requested runtimes (Builder, Publishing, Theme, CMS, Navigation,
  Catalog, Search, Customer Experience, Public Projection, Media, SEO, Localization, Personalization,
  Cache, Projection, Commerce Integration, State Machines, Authority Matrix/Divergence, CQRS,
  Snapshot, Cross-Runtime Graph, Invariants, Decisions, Debt, Timeline, Classification).
- **Verified**: primary evidence read from `AdminStoreConfigService.cs`, `StoreConfig.cs`,
  `AdminCmsService.cs`, `CmsPageService.cs`, `SuperAdminHomepageSectionManagementService.cs`,
  `StorefrontConfigProjectionConsumer.cs`, `StorefrontHomeService.cs`, `SeoService.cs`,
  `StorefrontPreviewService.cs`, `HomepageSectionReadService.cs`, `CmsPageReadService.cs`.
- **Current certification correction**: navigation publishes now append `StoreConfig.VersionsJson`
  snapshots before staging `NavigationPublishedEvent`, and homepage publishes now stage
  `HomepageSectionsPublishedEvent` before the final transactional save/commit. This keeps rollback
  evidence and projection event staging synchronized with the publishing runtime contract.
- **Missing/deferred**: storefront frontend (PointOfSale.Web / PointOfSale.Storefront) rendering
  specifics and the full projection-consumer minutiae were corroborated via reference docs
  (`cms-runtime-reverse-engineering.md`, `storefront-content-management-notes.md`,
  `storefront-homepage-authority-divergence.md`) and directly verified for the core consumers
  (`RedisInvalidationConsumer.cs:20-35`, `NavigationProjectionConsumer.cs:28,196-475`,
  `CmsPageProjectionConsumer.cs:19-397`). The core authority conclusions stand on backend code.
- **Debt summary**: orphaned CMS Mongo projection; hardcoded SEO Index; no Twitter/CMS-JSON-LD;
  `pages[home]` divergence; no media CDN; implicit checkout boundary.

> Implementation wins over all prior docs. Where this document conflicts with earlier markdown, the
> code citations above are authoritative.
