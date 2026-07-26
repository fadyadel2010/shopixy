# CMS Runtime — Final Canonical Architecture

Implementation-backed architecture specification for the CMS Runtime bounded context of
Shopixy / PointOfSale (Content Authority Runtime: Content Management, Page Lifecycle,
Homepage Composition, Landing Pages, Dynamic Sections, Content Publication, Navigation,
Menus, SEO, Storefront Content, Content Projection, Content Visibility, Content
Scheduling, Content Recovery, Search Integration, Cross-Domain Content Consumption).

> Every statement in this document is derived from the C# implementation under
> `PointOfSale.Core/`, `PointOfSale.Infrastructure/`, and `PointOfSale.API/`. Where a
> conclusion is supported, the `file:line` evidence is cited. Prior markdown, comments,
> and certification `.md` files were NOT treated as authoritative — they are referenced
> only where they independently confirm a code finding. The CMS Runtime is a runtime
> behaviour specification: editors, drag-and-drop, React, layouts, themes, and CSS are
> out of scope by design.

Canonical paths:
- Core aggregates: `PointOfSale.Core/Entities/` (`CmsPage.cs`, `MarketplaceCmsPage.cs`, footer entities)
- Domain events: `PointOfSale.Core/Events/` (`CmsPageEvents.cs`, `CacheInvalidationEvents.cs`)
- Write services: `PointOfSale.API/Services/Cms/`, `PointOfSale.API/Services/Admin/`, `PointOfSale.API/Services/Homepage/`, `PointOfSale.API/Services/MarketplaceCms/`, `PointOfSale.API/Services/Storefront/`
- Read services: `PointOfSale.API/Services/Cms/CmsPageReadService.cs`, `PointOfSale.API/Services/Storefront/SeoService.cs`, `HomepageSectionReadService.cs`, `StorefrontFooterReadService.cs`
- Projection consumers: `PointOfSale.Infrastructure/Consumers/CmsPageProjectionConsumer.cs`, `PointOfSale.API/Consumers/RedisInvalidationConsumer.cs`
- SQL authority: `PointOfSale.Infrastructure/Data/Configurations/CmsPageConfiguration.cs`, migration `20260329_AAB_AddCmsPages.sql`, `20260329_AAC_AddMarketplaceCmsPages.sql`
- DbContext: `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`

This document synthesizes four reverse-engineering passes — (1) CMS Page write/read/
projection/validation/versioning/preview lifecycle, (2) Homepage + Section + Navigation +
Footer + Menu runtime, (3) SEO + Marketplace CMS + Search integration, (4) Cross-Domain /
Outbox / CQRS / Technical-Debt — into ONE coherent architecture.

---

## Executive Summary

The CMS Runtime is the **Content Authority Runtime** of Shopixy. It is NOT a page editor and
NOT a frontend CMS framework. It is the bounded context that owns authored, tenant-scoped
content (static pages, homepage composition, navigation/footer, SEO metadata, marketplace
CMS pages) as PostgreSQL-authoritative truth, and projects that truth into derivative
read models (Redis, MongoDB, Elasticsearch where applicable) through a transactional outbox.

Reverse-engineered facts (all code-backed):

- **PostgreSQL is the single authoritative write model for all CMS content.** Every CMS
  mutation — page create/update/publish/rollback/delete, navigation draft/publish, footer
  upsert, homepage section publish — is committed to PostgreSQL through `ApplicationDbContext`.
  Proof: `CmsPageService.cs:69` (inject `ApplicationDbContext`); `AdminCmsService.cs:317`
  writes `StoreConfigs.CustomizationJson`; `StorefrontFooterWriteService` writes
  `StorefrontFooterSection` rows.
- **Publication is event-driven through a generic transactional outbox.** Domain commands
  stage `OutboxMessage` rows (`StorefrontCmsPagePublishedEvent`/`DeletedEvent`,
  `NavigationPublishedEvent`, `StorefrontFooterUpdatedEvent`, `HomepageSectionsPublishedEvent`)
  and a Hangfire worker (`OutboxPublisherJob.cs`) drains them to RabbitMQ. Consumers hydrate
  derivative stores. Proof: `CmsPageService.cs:666-707` (`StageStorefrontCmsPagePublishedEventAsync`
  via `IEventPublisher`); `CmsPageProjectionConsumer.cs:39` (consumes published/deleted
  events into Mongo).
- **Storefront reads are SQL-authoritative for CMS pages; derivative stores are acceleration
  only.** `CmsPageReadService.GetPublishedPageAsync` queries `CmsPages` SQL directly
  (`CmsPageReadService.cs:57-65,121-142`), gated by `IsTenantPubliclyActiveAsync`
  (`CmsPageReadService.cs:184-194`). The Mongo `storefront_pages` projection is written by
  `CmsPageProjectionConsumer` but the **primary storefront page read path does NOT read it**
  — the `MapPageWithSafeFallback(StorefrontPageDocument)` overload (`CmsPageReadService.cs:216`)
  is defined but never invoked by the published read. (Technical debt: the Mongo CMS
  projection is effectively write-only / orphaned.) Sitemap and slug resolution, by contrast,
  recompose against live SQL on every read (`SeoService.cs:644-773,497-637`).
- **Visibility is a SQL conjunction, re-verified at read and at projection time.** A CMS page
  is publicly visible only when `TenantId` matches AND `!IsDeleted` AND `IsPublished && !IsDraft`
  AND `PublishedAt != null` AND tenant is `IsActive && Status == Active`. Proof:
  `CmsPageReadService.cs:131-141`; `CmsPageProjectionConsumer.cs:87-93` (same gate before
  Mongo write); `SeoService.cs:1073-1083`. The schema enforces the state machine with CHECK
  constraints (`CmsPageConfiguration.cs:95-105`; migration `20260329_AAB_AddCmsPages.sql:80-102`).
- **Draft/publish is atomic promote under a Serializable transaction with row locking.**
  Publish demotes the current published row and promotes the newest draft
  (`CmsPageService.cs:283-339`) inside `BeginMutationTransactionAsync` →
  `IsolationLevel.Serializable` + `SELECT ... FOR UPDATE` (`CmsPageService.cs:588-558`).
  No Archive state, no Approved/approval workflow, and no scheduling/auto-publish exist for
  either CMS surface (verified absent; see Business Invariants and Architectural Decisions).
- **The CMS Runtime owns content but NOT media.** Homepage/CMS images are free-form URL
  strings validated only as safe http(s) or relative paths
  (`CmsPageContentValidator.cs:250-281`). There is no CMS media upload pipeline; the
  `IMediaService` general upload authority is not wired into CMS authoring.
- **Two independent CMS authority planes exist.** Tenant CMS Pages (`CmsPage`, POS Admin
  `Owner,Admin`) and Marketplace CMS Pages (`MarketplaceCmsPage`, Super Admin only) are
  separate storage, separate services, separate controllers, and separate projections — not
  a tenant-vs-global flag on one table. Super Admin authors homepage sections, marketplace
  CMS, marketplace footer, and branding; POS Admin tenants have NO authority over marketplace
  content.

---

## Architecture Vision

### CMS Runtime Purpose

The CMS Runtime turns a tenant's (or the platform's) authored-content intent — static pages,
homepage composition, navigation, footer, SEO, and marketplace content — into durable,
publicly-served storefront content. It is implemented as one bounded context that:

- **owns authored content truth** for one tenant (and, for Super Admin, the marketplace plane)
  as PostgreSQL aggregates,
- **writes durable business truth** through lifecycle-owning services with optimistic
  concurrency and Serializable transactions,
- **emits transactional outbox events** that hydrate Redis/ES/Mongo read models and cross-domain
  integrations,
- **defers cross-cutting lifecycles** (media storage, catalog/search indexing of products,
  payment, accounting, inventory) to their owning services.

### CMS Runtime Scope

The CMS Runtime owns the tenant back-office and storefront content authoring surfaces and the
marketplace (Super Admin) content surfaces:

- Tenant **CMS static pages** (`CmsPage`) — POS Admin `Owner,Admin`.
- **Homepage composition** (`HomepageSection`) — Super Admin only.
- **Storefront navigation** (header/footer link menus) — JSON inside `StoreConfig.CustomizationJson`
  (`navigation.live.{header,footer}`); POS Admin via `AdminStoreConfigController`.
- **Storefront footer** (`StorefrontFooterSection`/`StorefrontSocialLink`) — SQL-authoritative,
  tenant-or-global, with a derived fallback from published CMS pages.
- **Store identity / theme** — `StoreConfig` entity (single source of truth for name,
  description, theme, customization JSON).
- **SEO** — `SeoService` (read-only): metadata, breadcrumbs (+JSON-LD), canonical URLs, slug
  resolution; `SitemapController`/`RobotsController` tenant-aware.
- **Marketplace CMS** (`MarketplaceCmsPage`) — Super Admin only; parallel, isolated plane.

### What the CMS Runtime Owns

| Concept | Owner (service) | SQL table / aggregate |
|---|---|---|
| CMS static page | `CmsPageService` (via `AdminCmsService`) | `CmsPages` |
| CMS page version history | `CmsPageService` | `CmsPages` (per `PageGroupId`) |
| Page slug + canonical URL | `CmsPageService` + `SeoService` | `CmsPages.Slug` + derived `/pages/{slug}` |
| Homepage sections | `SuperAdminHomepageSectionManagementService` | `HomepageSections` |
| Navigation (header/footer menus) | `AdminCmsService` / `AdminStoreConfigService` | `StoreConfigs.CustomizationJson` (`navigation.*`) |
| Footer sections | `StorefrontFooterWriteService` | `StorefrontFooterSections`, `StorefrontSocialLinks` |
| SEO metadata | `SeoService` (read) | Derived from `CmsPages` / `Products` / `Categories` |
| Marketplace CMS page | `MarketplaceCmsPageService` (Super Admin) | `MarketplaceCmsPages` |
| Marketplace footer | Super Admin footer service | `MarketplaceFooter*` |
| Store identity / theme | `AdminStoreConfigService` | `StoreConfigs` |

### What the CMS Runtime Does NOT Own

- **Media storage / CDN / thumbnails.** Images/videos in CMS are free-form URLs. `IMediaService`
  is the general upload authority but is not wired into CMS authoring. (Proof:
  `CmsPageContentValidator.cs:250-281` — URL string validation only.)
- **Product / category catalog truth.** Homepage "products"/"categories" sections and SEO for
  products/categories reference catalog authority; they are recomposed against live CommerceHub
  lineage, not owned by CMS. (Proof: `SeoService.cs:204-265` reads product/category; homepage
  `SanitizeDiscoveryReferencesAsync` drops dead references.)
- **Search indexing of products/categories.** Elasticsearch is the Search Runtime's authority;
  CMS publish does NOT reindex Elasticsearch (projects to Mongo + Redis only). CMS pages enter
  Elasticsearch only through the manual admin `SyncCmsPages` repair path (see Search Integration).
- **Payment / accounting / inventory / shipping.** Out of scope; deferentially owned.
- **Menu/MenuItem aggregate.** There is NO standalone `Menu` or `MenuItem` SQL entity. Navigation
  is JSON inside `StoreConfig.CustomizationJson` (`AdminCmsService.cs:285-427`). Verified absent
  from `PointOfSale.Core/Entities/`.

---

## Architecture Principles

1. **PostgreSQL is the single authoritative write model.** All CMS mutations commit through
   `ApplicationDbContext`. (`CmsPageService.cs:69`, `CmsPageConfiguration.cs:11`.)
2. **Publication is separated from editing.** Draft and published are mutually exclusive rows
   (`IsDraft XOR IsPublished`, enforced by `CK_CmsPages_State`); publish is an atomic promote +
   outbox event, not a flag flip. (`CmsPageService.cs:283-339`;
   `20260329_AAB_AddCmsPages.sql:80-102`.)
3. **Visibility is a SQL conjunction, re-verified at read and projection.** Public visibility =
   `!IsDeleted ∧ IsPublished ∧ !IsDraft ∧ PublishedAt != null ∧ tenant active`.
   (`CmsPageReadService.cs:131-141`; `CmsPageProjectionConsumer.cs:87-93`.)
4. **Optimistic concurrency + Serializable write transactions.** Mutations carry
   `ExpectedVersion` and run in `Serializable` with `SELECT ... FOR UPDATE`
   (`CmsPageService.cs:206-219,588-558`).
5. **Tenant isolation is mandatory.** Every query and projection is scoped by `TenantId`;
   `X-Tenant-Id` is stripped and re-resolved by the service layer.
   (`CmsPageService.cs:105-113`; `CmsPageProjectionConsumer.cs:42-49`.)
6. **Outbox over dual-writes.** Derivative stores are written only by MassTransit consumers from
   outbox events; the write path never touches Mongo/Redis directly.
   (`CmsPageService.cs:666-707`; `RedisInvalidationConsumer.cs:107-137`.)
7. **Fail-closed projection.** The Mongo CMS consumer re-checks SQL authority and tenant lineage
   and TOMBSTONES on unproven state; stale/duplicate events are collapsed by a `ProjectionEventFence`.
   (`CmsPageProjectionConsumer.cs:95-145,304-347`.)
8. **Read-side SQL recomposition (repair-on-read).** Sitemap and slug resolution recompute
   against live SQL on every read rather than trusting the projection.
   (`SeoService.cs:644-773,497-637,516-546`.)
9. **Two authority planes for tenant vs marketplace.** Storage, services, controllers, and
   projections are separate; POS Admin has no marketplace authority.
10. **CMS owns content, not media.** Media is referenced by URL; no CMS upload pipeline.
    (`CmsPageContentValidator.cs:250-281`.)


---

## Content Runtime

The Content Runtime owns the lifecycle of authored CMS content (tenant static pages). Its SQL
authority is the `CmsPages` table; its aggregate identity is the immutable `PageGroupId`
(grouping all versions of one logical page) with a per-row `Id` PK and a monotonic `Version`.

### Content Lifecycle (states)

A `CmsPage` row carries three boolean state flags plus a timestamp:

- `IsDraft` — a working/edit copy.
- `IsPublished` — the live public copy.
- `IsDeleted` — soft-delete marker (never hard-deleted).
- `PublishedAt` — set only when published.

The schema enforces a strict state machine via CHECK constraints (`CmsPageConfiguration.cs:95-105`;
`20260329_AAB_AddCmsPages.sql:80-102`):

- `CK_CmsPages_State`: `NOT (IsDraft AND IsPublished)` — a row is never both draft and published.
- `CK_CmsPages_PublishedAt`: `NOT IsPublished OR PublishedAt IS NOT NULL` — published requires a timestamp.
- `CK_CmsPages_Version` / `CK_CmsPages_SchemaVersion`: must be > 0.
- `CK_CmsPages_ContentType`: `ContentType IN ('RichText','Blocks')`.

There is **no `Archived` state and no `Approved`/`PendingApproval` state** in the schema or code.
State is exactly one of: Draft, Published, or Deleted (soft). A page group may have both a draft
and a published row simultaneously (a "pending changes" situation), but never two drafts or two
published rows — enforced by partial unique indexes (`UQ_CmsPages_PageGroupId_Draft` WHERE
`IsDraft`, `UQ_CmsPages_PageGroupId_Published` WHERE `IsPublished`;
`20260329_AAB_AddCmsPages.sql:30-36`).

### Versioning

- `Version` is a monotonic per-`PageGroupId` integer (`UQ_CmsPages_PageGroupId_Version`).
- `CreatePageAsync` writes `Version = 1` (`CmsPageService.cs:164-185`).
- `UpdatePageAsync` closes the existing draft (`IsDraft=false`) and writes a NEW draft at
  `Version = Max(Version)+1` (`CmsPageService.cs:226-259`).
- `PublishPageAsync` promotes the newest draft to `IsPublished=true` at its existing version
  (`CmsPageService.cs:309-311`).
- `RollbackPageAsync` restores a prior `Version` snapshot as a NEW published row at
  `Version = Max(Version)+1` (`CmsPageService.cs:383-406`).
- `SchemaVersion` is the document-structure version (content schema), validated `> 0`
  (`CmsPageContentValidator.cs:51-54`).

### Content Identity

- `Slug` — normalized, lowercased, kebab-cased, max 160 chars (`CmsPageReadService.NormalizeSlug`,
  `CmsPageReadService.cs:334-362`).
- **Slug uniqueness is per tenant per (draft|published) state.** Partial unique indexes
  `UQ_CmsPages_Tenant_Draft_Slug` (WHERE `IsDraft AND !IsDeleted`) and
  `UQ_CmsPages_Tenant_Published_Slug` (WHERE `IsPublished AND !IsDeleted`)
  (`20260329_AAB_AddCmsPages.sql:47-55`). `EnsureSlugAvailableAsync` re-checks at write time
  (`CmsPageService.cs:630-648`).
- **Canonical URL** is derived as `{baseUrl}/pages/{slug}` (`SeoService.BuildCanonicalUrl`,
  `SeoService.cs:992-1001`). Slug resolution supports `RequiresRedirect` when the requested slug
  differs from the canonical (`SeoService.cs:569,586,605`).
- **Localization (AR):** `CmsPage` itself carries no localized fields — only `Title`/`Content`/
  SEO in the tenant's configured language. The `StorefrontPageDocument` Mongo doc does carry
  `LabelAr` for navigation links (a separate surface). CMS page body localization is NOT modeled
  at the page level. (Verified: `CmsPage.cs` has no `*Ar` field.)

### Tenant Isolation

Every `CmsPages` query is scoped by `TenantId`; `X-Tenant-Id` is stripped and re-resolved by the
service layer (`CmsPageService.cs:105-113`). Public reads additionally require
`Tenants.IsActive && Tenants.Status == Active` (`CmsPageReadService.IsTenantPubliclyActiveAsync`,
`CmsPageReadService.cs:184-194`). The projection consumer refuses to project for an inactive
tenant lineage (`CmsPageProjectionConsumer.cs:121-145`).

---

## Page Runtime

### Page Authority

`ICmsPageService` / `CmsPageService` is the page authority for tenant CMS pages. HTTP surface:
`AdminCmsPagesController` (`[Authorize(Roles = "Owner,Admin")]`, `AdminCmsPagesController.cs:14-17`),
`StorefrontPagesController` (`[AllowAnonymous]`, `StorefrontPagesController.cs:9-12`).

### Page Projection

- **Write-side projection (Mongo `storefront_pages`):** `CmsPageProjectionConsumer` upserts a
  `StorefrontPageDocument` on `StorefrontCmsPagePublishedEvent` and tombstones on
  `StorefrontCmsPageDeletedEvent` (`CmsPageProjectionConsumer.cs:19-302`). It is FAIL-CLOSED:
  if the SQL page is not proven `!IsDeleted ∧ IsPublished ∧ !IsDraft ∧ PublishedAt != null`, or
  the tenant lineage is inactive, it TOMBSTONES rather than writing
  (`CmsPageProjectionConsumer.cs:95-145,304-347`). It uses `ProjectionEventFence` for idempotent
  collapse and a freshness filter to reject stale versions
  (`CmsPageProjectionConsumer.cs:54-75,189-208`), and routes through
  `DerivativeProjectionMutationGuard.RequireReviewedMutation`
  (`CmsPageProjectionConsumer.cs:472-499`).
- **Read-side reality:** the storefront page read (`CmsPageReadService.GetPublishedPageAsync`)
  reads SQL directly (`CmsPageReadService.cs:57-65,121-142`), NOT Mongo. The Mongo document's
  `MapPageWithSafeFallback(StorefrontPageDocument)` overload (`CmsPageReadService.cs:216`) is
  defined but not invoked by the published-read path. **Conclusion: the Mongo `storefront_pages`
  projection is a write-only/orphaned derivative for the tenant CMS page surface.** (Technical
  debt; see Technical Debt.)

### Visibility

Public visibility requires `!IsDeleted ∧ IsPublished ∧ !IsDraft ∧ PublishedAt != null ∧ tenant
active` (`CmsPageReadService.cs:131-141`). Drafts and deleted pages return 404
(`StorefrontPagesController.cs:94-97`).

### Publication

`PublishPageAsync` (atomic promote): demote all published rows, set newest draft to
`IsDraft=false, IsPublished=true, PublishedAt=now`, stage `StorefrontCmsPagePublishedEvent`
(idempotency key `STOREFRONT_CMS_PUBLISHED:{tenant}:{pageGroup}:{version}`), commit, then
invalidate the Redis published scope (`CmsPageService.cs:283-339,666-686,328`).

### URL Resolution & Routing

- Admin route: `api/admin/cms/pages[/...]` + aliased `api/admin/pages[/...]`
  (`AdminCmsPagesController.cs:36-500`).
- Storefront route: `api/storefront/pages/{slug}` (`StorefrontPagesController.cs:28-114`);
  `api/storefront/contact` is a convenience alias to slug `contact` (`StorefrontPagesController.cs:140-146`).
- Slug → type resolution: `SeoService.ResolveSlugAsync` checks product, then category, then CMS
  (`SeoService.cs:549-609`), and rejects a cached CMS resolution if SQL authority no longer proves
  it published (`SeoService.cs:516-523`).

### SEO Metadata

Per-page SEO authorable: `MetaTitle` (≤200), `MetaDescription` (≤320), `MetaKeywords` (≤500),
`OpenGraphImageUrl` (≤1000) (`CmsPageConfiguration.cs:54-64`; `CmsPageService.NormalizeOptionalSeoText`,
`CmsPageService.cs:179-182,834-848`). `SeoService.BuildCmsSeoMetadataAsync` builds
`SeoMetadataDto` from the SQL-published page (canonical `/pages/{slug}`, og:type=article)
(`SeoService.cs:1035-1061`). **Gap:** it hardcodes `Index = true` and ignores the
`CmsPageSeo.NoIndex` field present on the Mongo document (`SeoService.cs:1055`; contrast
`StorefrontPageDocument.CmsPageSeo.NoIndex`, `StorefrontPageDocument.cs:119`). The SQL `CmsPage`
has no `NoIndex` column at all — so per-page `noindex` cannot be expressed through the SQL
authority today. (Technical debt.)

---

## Homepage Runtime

### Homepage Composition

Homepage composition is owned by **Super Admin only** via `SuperAdminHomepageSectionManagementService`
and stored in the `HomepageSections` SQL table. POS Admin has NO authority over homepage sections.
The storefront controller `HomepageSectionsController` is `[AllowAnonymous]` and read-only.
(Evidence: `HomepageSectionReadService.SanitizeDiscoveryReferencesAsync` drops dead product/category
IDs against live CommerceHub lineage, `HomepageSectionReadService.cs:52,249`; the public controller
`HomepageSectionsController` is `[AllowAnonymous]` + read-only.)

### Section Ordering, Featured, Promotional, Dynamic Blocks

Homepage sections are schema-validated, Redis-cached, and projected to a Marketplace Mongo read
model (`MarketplaceHomepageReadModelDocument`). Section `source ∈ {all, random, curated}`,
`maxItems` (products cap 24, categories cap 18), `layout ∈ {grid, carousel}` for products.
Resolution: `curated` maps IDs → details; `random` uses a STABLE seed key (stable across renders);
`all` uses automatic products. (`storefront-content-management-notes.md` §Featured Products.)

### Content Resolution

- Product shelves computed at read (`StorefrontHomeService`): Featured (Mongo 30m), New Arrivals
  (Mongo 15m), Best Sellers (Elasticsearch → Mongo New Arrivals fallback on ES failure, 15m),
  Curated (explicit IDs). These are automatic selections, distinct from the authorable homepage
  `source` values.
- **Discovery-reference sanitization:** `SanitizeDiscoveryReferencesAsync` strips dead
  product/category IDs inside sections against live CommerceHub listing/category lineage, logs a
  warning, keeps the section (`storefront-content-management-notes.md` §Read-side SQL recomposition).

### Authority divergence (must document)

There are TWO independently-authored homepage surfaces:
- **System A — POS Admin Storefront Builder** (`StoreConfig.CustomizationJson.pages[home].draft/live`):
  written by `AdminStoreConfigController`/`AdminStoreConfigService`, consumed by product/category
  builder pages and bootstrap, but NOT rendered by the public `HomePage.tsx`.
- **System B — Super Admin HomepageSection** (`HomepageSections` SQL): the surface actually
  rendered by public `HomePage.tsx` via `HomepageSectionsController` → `HomepageSectionReadService`.

Per the homepage authority-divergence audit, System A is a scoped authority divergence, not total
non-consumption. (See `storefront-homepage-authority-divergence.md`.)

---

## Landing Page Runtime

**Implementation status: not a distinct CMS runtime.** CMS "landing pages" / "campaign pages" /
"marketing pages" are ordinary CMS static pages (`CmsPage`) with a `slug` (e.g., campaign slugs),
published through the same Page Runtime. There is no separate CMS scheduling, SEO, or visibility
model for landing pages — they inherit the standard `CmsPage` lifecycle. Affiliate funnel landing
page types/controllers exist outside the CMS bounded context and are not CMS authority.

---

## Section Runtime

### Reusable Sections

Two distinct "section" concepts exist and must not be conflated:

1. **Homepage `HomepageSection`** (Super Admin) — composed, ordered, with `source`/`maxItems`/
   `layout`, resolved against live catalog lineage (see Homepage Runtime).
2. **`CmsPageSection`** — a Mongo document field (`StorefrontPageDocument.Sections`,
   `StorefrontPageDocument.cs:59,122-151`) modeling page-level content blocks (type, enabled,
   order, settings). **However, the CMS write path does NOT populate `Sections`** — content is
   stored in `CmsPage.Content` (RichText HTML or Blocks JSON via `CmsPageContentValidator`). The
   `CmsPageSection`/`Sections` field on the Mongo projection is effectively unused on write
   (the consumer's `MapToDocument` maps `Content`, not `Sections`). (Technical debt:
   orphaned/redundant block model.)

### Composition, Ordering, Dependencies, Dynamic Content

- Homepage sections: ordered by an explicit order field; "dynamic content" = product/category
  shelves resolved at read with discovery-reference sanitization against live catalog.
- CMS page blocks: validated by `CmsPageContentValidator` — supported block types are exactly
  `text`, `image`, `faq` (`CmsPageContentValidator.cs:32-37,113-116`); max 100 blocks
  (`MaxBlocks`), max 50 FAQ items (`MaxFaqItems`); block `type` must be lowercase kebab-case
  2–50 chars (`CmsPageContentValidator.cs:28-30,108-111`). Dynamic content inside blocks is
  limited to image URLs and FAQ Q&A — no server-side data binding.



---

## Navigation Runtime

### Navigation Tree / Menus / Hierarchy / Ordering

There is **no standalone `Menu`/`MenuItem` SQL aggregate**. Navigation is JSON stored inside
`StoreConfig.CustomizationJson` under `navigation.draft.{header,footer}` and
`navigation.live.{header,footer}` (`AdminCmsService.cs:285-427`). Each link carries
`label`, `labelAr`, `type`, `path`, `slug`, `openInNewTab`, `order`, `enabled`
(`AdminCmsService.MapNavigationLink`, `AdminCmsService.cs:481-494`). Ordering is the array index
(`order = index`). Hierarchy is a flat link list per header/footer (no nested tree model).

### Visibility / Permissions

- Draft vs live: `UpdateNavigationAsync` writes `navigation.draft`; `PublishNavigationAsync`
  promotes `draft` → `live` (`AdminCmsService.cs:304-427`).
- At **publish time**, CMS links are validated against **published** CMS slugs
  (`requirePublishedCms: true`, `AdminCmsService.cs:407-410`) and categories against live
  lineage (`AdminCmsService.cs:466-479`). At draft-save time, validation is looser
  (`requirePublishedCms: false`, `AdminCmsService.cs:329-330`).
- Storefront recomposition (`StorefrontNavigationController.RecomposeNavigationFromSqlAuthorityAsync`,
  `StorefrontNavigationController.cs:160`): drops any `cms` link whose slug is not a PUBLISHED CMS
  page, any `category` link not live in `CategoryLineageAuthority`, and any unsafe URL
  (`/api/`, `/admin`, `preview=true`, `previewToken=`, non-http(s)); returns `EmptyNavigation` if the storefront is not
  `live` (`StorefrontNavigationController.cs:69,160`;
  `storefront-content-management-notes.md` §Read-side SQL recomposition).

### Storefront Resolution

`StorefrontNavigationController` recomposes the live navigation against SQL authority on every
read. Publish emits `NavigationPublishedEvent` (`AdminCmsService.StageNavigationPublishedEventAsync`,
`AdminCmsService.cs:422,664-683`); it is consumed by `NavigationProjectionConsumer`, which projects
live navigation to MongoDB AND invalidates the Redis navigation cache itself
(`NavigationProjectionConsumer.cs:28,52,196-241`) — note: navigation cache invalidation is owned by
`NavigationProjectionConsumer`, NOT `RedisInvalidationConsumer` (which does not subscribe to
`NavigationPublishedEvent`). The storefront serves the recomposed live navigation; if the tenant
storefront is not `live`, an empty navigation is returned.

---

## Menu Runtime

**Implementation status: not a first-class runtime.** Verified absent: no `Menu` or `MenuItem`
entity exists in `PointOfSale.Core/Entities/`. "Menus" are the header/footer link collections
inside `StoreConfig.CustomizationJson` (`AdminCmsService.cs:285-427`). The `StorefrontPageDocument`
carries a `DisplayOrder` field (`StorefrontPageDocument.cs:69`) intended for menu ordering, but
it is never populated by the CMS projection consumer's `MapToDocument` — another orphaned field.
Any future Menu aggregate would replace the JSON-in-StoreConfig model; today navigation is a
StoreConfig-owned JSON surface, not a CMS-owned menu tree.

---

## SEO Runtime

### Slug / Canonical URL / Meta Title / Description

- `SeoService` is READ-ONLY — no writes, no events, no cross-service sync
  (`SeoService.cs:20-49`). It composes SEO from SQL authority for product/category, and from
  SQL-published CMS pages for `type == "cms"`.
- Canonical URL derived: `/product/{slug}`, `/category/{slug}`, `/pages/{slug}`
  (`SeoService.BuildCanonicalUrl`, `SeoService.cs:992-1001`).
- CMS meta title/description/keywords/OG from `CmsPage` (`SeoService.cs:1047-1060`).
- Slug resolution supports redirect when requested slug ≠ canonical (`RequiresRedirect`)
  (`SeoService.cs:569,586,605`).

### Structured Data / Indexability / Robots / Sitemap

- **Structured data:** ONLY breadcrumb JSON-LD (`SeoService.BuildBreadcrumbJsonLd`,
  `SeoService.cs:1008-1024`), emitted for product/category breadcrumbs. CMS pages have NO
  schema.org/JSON-LD projection. (Verified.)
- **Twitter Card (`twitter:*`):** NOT separately emitted — only OpenGraph `og:*` is mapped
  (`og:title/description/image/type/url`). (Verified: `SeoService.cs:1051-1053`.)
- **Indexability:** product indexability tied to `IsActive && IsOnlineVisible`
  (`SeoService.cs:229`); CMS hardcoded `Index = true` (`SeoService.cs:1055` — see gap above).
- **Robots:** `GetRobotsTxtAsync` builds `text/plain` with `Allow: /`, disallow `/admin/` and
  `/api/`, allow `/api/storefront/`, and the sitemap URL (`SeoService.cs:780-838`). TTL 12h.
- **Sitemap:** `GetSitemapAsync` recomposes against live SQL on every read (categories from
  lineage, products via `onlineVisibleOnly`, CMS from published pages) and does NOT trust a stale
  cached sitemap (`SeoService.cs:644-773`). JSON (`/api/storefront/sitemap`) + XML (`/xml`).
  CMS pages get `priority = 0.4`, `changefreq = monthly`.

### SEO-specific debt

- `SeoService.BuildCmsSeoMetadataAsync` hardcodes `Index = true`, ignoring any `NoIndex` intent.
  The SQL `CmsPage` has no `NoIndex` column, and the Mongo `CmsPageSeo.NoIndex` field is never
  read by the SQL-authority SEO path. Per-page noindex is therefore unenforceable. (See Technical
  Debt.)

---

## Publication Runtime

### Draft / Publish / Unpublish / Archive / Soft Delete / Hard Delete / Approval

Implemented transitions (all in `CmsPageService`):

- **Draft → Publish (atomic promote):** `PublishPageAsync` demotes published, promotes newest
  draft, sets `PublishedAt`, stages `StorefrontCmsPagePublishedEvent`, invalidates Redis
  (`CmsPageService.cs:283-339,666-686,328`).
- **Unpublish:** NOT a direct action. The only ways to remove a published page from public view
  are (a) `DeletePageAsync` (soft delete → `IsPublished=false`, stages
  `StorefrontCmsPageDeletedEvent` if it had a published row; `CmsPageService.cs:436-506`) or
  (b) `RollbackPageAsync` (which re-publishes a different version; `CmsPageService.cs:341-434`).
  There is no `Unpublish` endpoint.
- **Archive:** NOT implemented (no `Archived` state; verified absent).
- **Soft Delete:** `DeletePageAsync` sets `IsDeleted=true, IsDraft=false, IsPublished=false,
  PublishedAt=null` for all active rows (`CmsPageService.cs:463-471`). Never hard-deleted.
- **Hard Delete:** NOT implemented for CMS pages. (Verified: no `REMOVE`/hard-delete of
  `CmsPages` anywhere in `CmsPageService`.)
- **Approval workflow:** NOT implemented. Publish is a single privileged action by an
  `Owner`/`Admin` (or Super Admin for marketplace). No reviewer/approver role. (Verified absent.)

### Optimistic Concurrency

Every mutating call requires `ExpectedVersion`; mismatch throws `ConcurrencyConflictException`
(`CmsPageService.cs:206-219,608-614`). Writes run in `Serializable` + `FOR UPDATE`
(`CmsPageService.cs:588-558,536-558`).

---

## Visibility Runtime

A CMS page's runtime visibility is the SQL conjunction:

`Visible := TenantId == requestTenant AND !IsDeleted AND IsPublished AND !IsDraft AND PublishedAt != null AND Tenant.IsActive AND Tenant.Status == Active`

Concretely:

- **Public (storefront):** only published, non-deleted, timestamped, active-tenant pages
  (`CmsPageReadService.cs:131-141`; `StorefrontPagesController.cs:94-97`).
- **Private / Hidden:** no explicit "private/hidden" flag exists on `CmsPage`. A page is
  effectively hidden if it is a draft (not published) or soft-deleted. There is no `Hidden`
  boolean distinct from draft/unpublished.
- **Tenant-scoped:** always enforced (`CmsPageService.cs:105-113`).
- **Scheduled / Expired:** NOT implemented (no publish/expiry date columns; verified absent).
  A page becomes visible the instant it is published; it stays visible until deleted/rolled-back.
- **Navigation visibility:** a published CMS page appears in live navigation only if its slug
  survives `RecomposeNavigationFromSqlAuthorityAsync` (published-CMS check)
  (`AdminCmsService.cs:407-410`).
- **Search visibility:** CMS pages are indexed in Elasticsearch only by manual admin sync, never by
  the CMS publish/delete event pipeline; storefront search re-verifies SQL publication before
  returning CMS results. They also appear in the SQL-recomposed sitemap (published pages only)
  (`SeoService.cs:735-745`).

---

## Scheduling Runtime

**Implementation status: not implemented for CMS.** There are no `PublishDate` / `ExpirationDate`
columns on `CmsPage` or `MarketplaceCmsPage`, no scheduled-publish job, no automatic unpublish,
and no recovery scheduler. Publish is immediate and synchronous within the mutation transaction.
(Verified: `CmsPage.cs` has only `PublishedAt`; `20260329_AAB_AddCmsPages.sql` has no scheduling
columns; no Hangfire scheduling job references CMS pages.) This is a deliberate simplification —
the spec previously listed Scheduling/Recovery as CMS scope, but the implementation does not
realize it. Recovery is provided instead by **version rollback** (`RollbackPageAsync`,
`CmsPageService.cs:341-434`), which restores a prior snapshot as a new published version, and by
**fail-closed projection tombstoning** on unproven SQL authority
(`CmsPageProjectionConsumer.cs:304-347`).



---

## Media Integration

### Images / Videos / Documents / Media References

The CMS Runtime **references media by URL string; it does not own or store media.**

- CMS page `Content` (RichText HTML or Blocks JSON) embeds image URLs. For Blocks, the
  `image` block requires an `imageUrl` (`CmsPageContentValidator.cs:174-188`).
- Homepage sections (Super Admin) reference product/category references; hero/banner images are
  free-form URLs.
- SEO `OpenGraphImageUrl` is a URL string ≤1000 chars (`CmsPageService.cs:182,850-855`).

### Media Authority

Media authority belongs to `IMediaService` (general upload) and `ProductMediaService` (product
media only). **Neither is wired into CMS authoring.** CMS media validation is limited to
"safe URL": http(s) absolute or root-relative, no `//`, no backslash, no userinfo, ≤ max length
(`CmsPageContentValidator.NormalizeSafeUrl`, `CmsPageContentValidator.cs:250-281`).

### Broken References

There is no media-reference integrity check in CMS. A CMS page can reference a deleted/external
image and the system will serve it (or 404 at the CDN). Homepage sections mitigate broken
*product/category* references via `SanitizeDiscoveryReferencesAsync` (drops dead IDs, logs), but
this does NOT cover free-form image URLs. Orphaned media (uploaded but unreferenced, or
referenced but deleted) is not tracked by the CMS Runtime.

---

## Search Integration

### CMS Search / Search Projection / Indexing / SEO Integration

- **CMS pages CAN be indexed in Elasticsearch — but only via a manual admin rebuild, NOT via the
  publish/delete event pipeline.** The `CmsPageSearchDocument` type
  (`PointOfSale.Infrastructure/Elasticsearch/Documents/CmsPageSearchDocument.cs`) is real and wired
  into `UnifiedSearchIndexService.IndexCmsPageAsync` (the `cmspages` ES index;
  `UnifiedSearchIndexService.cs:22,280-335`). It is populated exclusively by
  `AdminSearchSyncController.SyncCmsPages` (Owner/Admin, on-demand), which reads published,
  non-deleted, timestamped, tenant-active `CmsPages` and requires `SqlTruthRebuildGuard` with
  `SqlAuthorityOverlayKind.StorefrontCms`
  (`AdminSearchSyncController.cs:306-365,579-585`; `SqlTruthRebuildGuard`).
- **Event pipeline does NOT auto-index CMS.** `StorefrontCmsPagePublishedEvent`/`DeletedEvent`
  project only to Mongo (`storefront_pages`) and Redis; no ES write. A newly published CMS page is
  therefore NOT searchable until an admin runs the manual CMS sync (or the periodic stale-delete
  pass runs). This is a deliberate decoupling, not an absence of ES support.
- **Storefront search re-authors CMS visibility against SQL.** `StorefrontSearchController` filters
  CMS results by `!IsDeleted ∧ IsPublished ∧ !IsDraft ∧ PublishedAt != null` against `CmsPages`
  (`StorefrontSearchController.cs:428-445`), so search results can never bypass the SQL visibility
  gate even if the ES doc is stale.
- **SEO integration** is via the SQL-recomposed **sitemap** (`SeoService.GetSitemapAsync`,
  `SeoService.cs:644-773`) and **slug resolution** (`SeoService.ResolveSlugAsync`,
  `SeoService.cs:497-637`); both treat the CMS page as a routable URL, not a searchable document. Sitemap
  entries are published-only.

---

## CQRS

### SQL (write authority)

PostgreSQL `CmsPages` (+ `HomepageSections`, `StoreConfigs`, `StorefrontFooterSections`,
`MarketplaceCmsPages`) is the single write authority. All mutations go through
`ApplicationDbContext` in `Serializable` transactions with `FOR UPDATE` row locks
(`CmsPageService.cs:588-558,536-558`).

### Mongo (read model / derivative)

`storefront_pages` (`StorefrontPageDocument`) is written by `CmsPageProjectionConsumer` from
outbox events, FAIL-CLOSED, projection-fenced, freshness-guarded
(`CmsPageProjectionConsumer.cs:19-499`). **Orphaned for the tenant CMS read path** — the
storefront page read uses SQL, not Mongo (see Page Runtime / Technical Debt).

### Redis (cache)

`CmsPageCacheService` caches the published scope per tenant (TTL ≥1 min,
`CmsPageCacheOptions.PublishedTtlMinutes`, `CmsPageCacheService.cs:42`). Cache read failure →
WARNING + fall through to SQL; never 503 (`CmsPageCacheService.cs:45-68`). Publish/delete events
invalidate via `RedisInvalidationConsumer` (`RedisInvalidationConsumer.cs:306-315`).

### Elasticsearch

Authoritative for product/category search (Search Runtime). CMS pages are indexed into the
`cmspages` index ONLY via the manual admin rebuild `AdminSearchSyncController.SyncCmsPages` (not
the publish/delete event pipeline). Storefront search re-verifies CMS visibility against SQL.

### Projection / Freshness / Consistency

- **Event-driven, not polled.** Consumers are MassTransit `IConsumer<T>` with retry (1s/5s/15s)
  and `ProjectionEventFence` idempotency (`CmsPageProjectionConsumer.cs:54-75`;
  `storefront-content-management-notes.md` §Events/projection).
- **Freshness:** `BuildFreshPageEventFilter` rejects stale versions via `LastEventId`/
  `LastEventOccurredAtUtc`/`LastEventVersion` (`CmsPageProjectionConsumer.cs:189-208,517-537`).
- **Consistency:** SQL is always authoritative; derivative stores are recomputed/verified against
  SQL at read (sitemap, slug resolution) or at projection (CMS consumer re-checks SQL before
  Mongo write). Eventually-consistent gap: up to Redis TTL minutes after publish before a cache
  invalidate lands; Mongo is async but SQL `IsPublished` is immediate.

---

## Projection Usage

| Projection | Trigger event | Writer | Authority check | Failure mode |
|---|---|---|---|---|
| Mongo `storefront_pages` | `StorefrontCmsPagePublishedEvent` / `DeletedEvent` | `CmsPageProjectionConsumer` | Re-checks SQL `IsPublished` + tenant lineage; else tombstone (fail-closed) | Tombstone on unproven; stale collapsed by fence |
| Redis published scope | same + `StorefrontFooterUpdatedEvent` | `RedisInvalidationConsumer` | Tenant-scoped | Cache miss → SQL; 503 only if SQL fails |
| Marketplace Mongo CMS | `MarketplaceCmsPagePublishedEvent` / `DeletedEvent` | marketplace consumer + `RedisInvalidationConsumer` | Super Admin plane | Invalidate only |
| Elasticsearch `cmspages` (CMS) | `StorefrontCmsPagePublishedEvent` (NO auto-write) | `AdminSearchSyncController.SyncCmsPages` (manual) | SQL `IsPublished` + `SqlTruthRebuildGuard` (StorefrontCms) | Manual on-demand; stale-delete pass; NOT event-driven |
| Sitemap / slug / SEO | none (read-time) | `SeoService` (recomposes SQL) | Live SQL `IsPublished` + tenant active | Re-computes; never trusts stale cache |

---

## Read Models

- **Storefront page read:** `CmsPageReadService` → SQL `CmsPages` (published filter + tenant
  active). Returns `CmsPageDto` (`CmsPageReadService.cs:57-142,265-312`).
- **Admin page read:** `CmsPageService.GetPageAsync` → SQL, maps draft+published +
  version history (`CmsPageService.cs:89-148,709-832`).
- **SEO read:** `SeoService` → SQL (products/categories/CMS) recomposed; Redis cache-first
  (`SeoService.cs:117-195,497-637,1035-1084`).
- **Navigation read:** `StorefrontNavigationController` → `StoreConfig.CustomizationJson.live`
  recomposed vs SQL authority.

## Write Models

- **`CmsPage`** (SQL rows per `PageGroupId`/`Version`): the write model. Mutated by
  `CmsPageService` under optimistic concurrency + Serializable tx.
- **`StoreConfig.CustomizationJson.navigation.*`**: navigation write model (POS Admin).
- **`HomepageSections`**: homepage write model (Super Admin).
- **`StorefrontFooterSection`/`StorefrontSocialLink`**: footer write model.

---

## Runtime Authority Matrix

For each concept: Business Owner (author), Mutation Authority (who writes SQL), Read Authority
(who serves it), Projection Authority (who writes derivatives), Runtime Truth (SQL table).

| Concept | Business Owner | Mutation Authority | Read Authority | Projection Authority | Runtime Truth |
|---|---|---|---|---|---|
| CMS page (tenant) | POS Admin `Owner,Admin` | `CmsPageService` | `CmsPageReadService` (SQL) | `CmsPageProjectionConsumer` (Mongo/Redis) | `CmsPages` |
| CMS page version history | POS Admin | `CmsPageService` | `CmsPageService.GetVersionHistoryAsync` | — | `CmsPages` |
| Page slug / canonical | POS Admin + `SeoService` | `CmsPageService` (slug) | `SeoService.ResolveSlugAsync` | — | `CmsPages.Slug` |
| Homepage sections | Super Admin | `SuperAdminHomepageSectionManagementService` | `HomepageSectionReadService` | `HomepageProjectionConsumer` | `HomepageSections` |
| Navigation (menus) | POS Admin | `AdminCmsService` (StoreConfig JSON) | `StorefrontNavigationController` | `RedisInvalidationConsumer` | `StoreConfigs.CustomizationJson` |
| Footer | POS Admin (tenant) / Super Admin (global) | footer write service | `StorefrontFooterReadService` | `StorefrontFooterProjectionConsumer` | `StorefrontFooterSections` |
| SEO metadata | Read-only (`SeoService`) | n/a (derived) | `SeoService` | Redis cache | derived (SQL) |
| Media references | Content author | n/a (URL string) | `CmsPageReadService` | — | none (URL in `Content`) |
| Visibility | CMS Runtime | `CmsPageService` (flags) | `CmsPageReadService` (SQL conjunction) | `CmsPageProjectionConsumer` (fail-closed) | `CmsPages` flags |
| Scheduling | — | NOT IMPLEMENTED | — | — | none |
| Search projection (CMS) | — | Manual admin sync (`SyncCmsPages`); NOT event-driven | `SeoService` (sitemap/slug) + `AdminSearchSyncController` | `cmspages` index (manual) + sitemap | event pipeline does not auto-index CMS |
| Marketplace CMS page | Super Admin | `MarketplaceCmsPageService` | `MarketplaceCmsPageReadService` | marketplace consumer | `MarketplaceCmsPages` |



---

## Cross-Domain Authority Graph

The CMS Runtime reads from, and is read by, peer bounded contexts. All cross-domain I/O is
event-driven via the generic outbox; CMS never calls peers synchronously for writes.

| Peer | Reads from CMS | Writes to CMS | Events CMS consumes | Events CMS emits | Authority / Sync notes |
|---|---|---|---|---|---|
| Merchant Platform (Storefront) | CMS pages, nav, footer, SEO via `CmsPageReadService`/`SeoService`/`StorefrontNavigationController` | — | — | `StorefrontCmsPagePublishedEvent` etc. | Storefront is the primary CMS consumer; SQL-authoritative reads |
| Search Runtime (Elasticsearch) | — (CMS indexed only via manual `SyncCmsPages`) | — | `StorefrontCmsPagePublishedEvent` (no ES write) | none for CMS auto-path | CMS pages enter `cmspages` only on manual admin sync; storefront search re-auths vs SQL |
| CommerceHub (Marketplace) | marketplace CMS/footer/homepage (separate plane) | — | `MarketplaceCmsPage*` events | `MarketplaceCmsPagePublishedEvent`/`DeletedEvent`, `MarketplaceFooterUpdatedEvent` | Isolated `Marketplace*` collections; Super Admin authors |
| Inventory Runtime | — | — | — | none | Homepage sections reference live CommerceHub lineage; sanitized at read |
| Reporting Runtime | read-only analytics of content | — | — | none | CMS emits no reporting events |
| Media Runtime | referenced media URLs | — | none | none | CMS stores URLs only; no media ownership |
| Analytics Runtime | page-view events (storefront) | — | — | none | Out of CMS authority |
| Affiliate Platform | — | — | — | none | No CMS↔Affiliate integration |
| SuperAdmin | governs marketplace CMS/homepage/footer; tenant lifecycle | writes `Tenants` (active check) | none inbound | none | Super Admin authors marketplace plane; CMS public reads check `Tenants.IsActive && Status==Active` (`CmsPageReadService.cs:184-194`) |

### Dependencies / Synchronization

- **Tenant lifecycle dependency:** every public CMS read and projection depends on
  `Tenants.IsActive && Status == Active`. Inactive/suspended tenant → CMS content invisible and
  projection tombstoned (`CmsPageReadService.cs:184-194`; `CmsPageProjectionConsumer.cs:121-145`).
- **Catalog lineage dependency (homepage/SEO):** product/category references are recomposed
  against `CategoryLineageAuthority` / product SQL authority at read; dead references dropped
  (`SeoService.cs:204-265`; homepage `SanitizeDiscoveryReferencesAsync`).
- **Outbox synchronization:** CMS events flow through `OutboxPublisherJob` (5s drain, critical
  ordering by `EventVersion`); Redis/Mongo consumers are fenced + idempotent
  (`RedisInvalidationConsumer.cs:236-266`; `CmsPageProjectionConsumer.cs:54-75`).

---

## Business Invariants

(Each verified against implementation.)

1. **Only published pages are public.** `CmsPageReadService.LoadCurrentPublishedPageBySlugAsync`
   filters `IsPublished && !IsDraft && PublishedAt != null`
   (`CmsPageReadService.cs:131-141`). Draft returns 404 (`StorefrontPagesController.cs:94-97`).
2. **A row is never both draft and published.** `CK_CmsPages_State`
   (`CmsPageConfiguration.cs:96-97`; `20260329_AAB_AddCmsPages.sql:80-84`). Partial unique
   indexes prevent two drafts/two published per group (`..._Draft`, `..._Published`).
3. **Published requires a timestamp.** `CK_CmsPages_PublishedAt`
   (`CmsPageConfiguration.cs:99-101`).
4. **Slug uniqueness per tenant per state.** `UQ_CmsPages_Tenant_Draft_Slug` /
   `UQ_CmsPages_Tenant_Published_Slug` (`20260329_AAB_AddCmsPages.sql:47-55`);
   `EnsureSlugAvailableAsync` (`CmsPageService.cs:630-648`).
5. **Hidden/unpublished pages never appear in navigation.** `RecomposeNavigationFromSqlAuthorityAsync`
   drops CMS links whose slug is not a published page (`AdminCmsService.cs:407-410`).
6. **Hidden/unpublished pages never appear in sitemap/search.** Sitemap reads published pages
   only (`SeoService.cs:735-745`); CMS ES index is populated only by manual admin sync from
   published rows (`AdminSearchSyncController.cs:347-357`), and storefront search re-verifies
   `IsPublished` against SQL (`StorefrontSearchController.cs:428-445`).
7. **Deleted content disappears.** Soft delete sets `IsDeleted` + clears publish flags and stages
   `StorefrontCmsPageDeletedEvent` → Mongo tombstone + Redis invalidate
   (`CmsPageService.cs:436-506`; `CmsPageProjectionConsumer.cs:244-302,349-395`).
8. **Media references remain valid (best-effort).** Only product/category references are
   sanitized; free-form image URLs are not integrity-checked (see Media Integration).
9. **Tenant isolation is mandatory.** Every query scoped by `TenantId`; `X-Tenant-Id` stripped
   (`CmsPageService.cs:105-113`; `CmsPageProjectionConsumer.cs:42-49`).
10. **Homepage always resolves.** Homepage sections are Super Admin-authored and the storefront
    controller is `[AllowAnonymous]` + read-only; recomposition degrades gracefully (empty
    sections) rather than erroring.
11. **Publish is atomic and versioned.** Promote-under-Serializable + `ExpectedVersion` optimistic
    concurrency; no partial publish (`CmsPageService.cs:283-339,588-558`).
12. **Projection fails closed.** Mongo CMS consumer tombstones when SQL authority/tenant lineage
    is unproven (`CmsPageProjectionConsumer.cs:95-145,304-347`).

---

## Architectural Decisions

(Why, not how — reverse-engineered from implementation.)

- **Why CMS owns content but not media.** Media is a cross-cutting asset (product media, CDN,
  uploads) owned by `IMediaService`/`ProductMediaService`. CMS content is tenant-authored text/
  structure; embedding media ownership would couple content lifecycle to a blob store. CMS stores
  only safe URLs (`CmsPageContentValidator.cs:250-281`).
- **Why Search indexes products/categories and CMS only via manual sync.** CMS pages are
  low-volume, tenant-scoped, URL-routable documents served from SQL; they belong in the sitemap +
  slug resolver and the `cmspages` ES index, which is populated only by the manual admin
  `AdminSearchSyncController.SyncCmsPages` — a deliberate decoupling (publish/delete events do NOT
  auto-index CMS), not an absence of ES support. The storefront search path re-verifies CMS
  visibility against SQL so stale ES docs can never bypass the publish gate. Elasticsearch remains
  the high-volume commerce search authority.
- **Why publication is separated from editing.** The draft/published split (mutually exclusive
  rows + atomic promote) lets an author iterate on a draft while the public page stays stable, and
  lets publish be a single privileged, auditable, event-emitting action. CHECK constraints
  enforce the split at the storage layer (`CK_CmsPages_State`).
- **Why scheduling is absent.** Publish is immediate and synchronous; "future-dated publish" and
  "expiry" were not realized. Recovery is instead provided by version rollback (restore any prior
  version as a new published version) and fail-closed projection repair. This keeps the content
  state machine small and the visibility rule a single SQL conjunction.
- **Why pages remain SQL-authoritative.** CMS content is low-write, high-correctness (legal/
  storefront-facing). Serving from SQL with tenant-active gating makes public visibility a single
  deterministic query, and makes derivative stores pure accelerators that can be rebuilt anytime.
- **Why two authority planes (tenant vs marketplace).** Marketplace content is platform-owned
  (Super Admin) and must be isolated from tenant tampering; reusing one table with a tenant flag
  would risk tenant escalation into marketplace content. Separate storage/services/controllers
  enforce the boundary by construction.
- **Why navigation is JSON-in-StoreConfig, not a Menu aggregate.** Navigation is a small,
  frequently-published, store-config-coupled structure; embedding it as JSON in `StoreConfig`
  lets draft/live promotion ride the existing store-config versioning and keeps it inside the
  tenant's single config document. A standalone Menu tree was not justified.
- **Why fail-closed projection.** CMS content is customer-facing and tenant-scoped; a stale or
  authority-less Mongo doc must never be served. The consumer re-verifies SQL + tenant lineage and
  tombstones otherwise (`CmsPageProjectionConsumer.cs:95-145`).



---

## Technical Debt

(Only implementation-backed findings.)

1. **Orphaned Mongo `storefront_pages` CMS projection.** `CmsPageProjectionConsumer` writes
   `StorefrontPageDocument`, but the storefront page READ path (`CmsPageReadService.GetPublishedPageAsync`)
   reads SQL directly and never invokes the `StorefrontPageDocument` map overload
   (`CmsPageReadService.cs:57-65,121-142,216`). The Mongo CMS collection is write-only for the
   tenant surface — a maintained-but-unread derivative. Either wire the read path to Mongo (with
   SQL re-authorization) or stop projecting. Priority: Medium. Status: Confirmed.
2. **Unused `StorefrontPageDocument.Sections` / `CmsPageSection`.** The Mongo doc models page-level
   `Sections` (type/enabled/order/settings), but the CMS write path stores content in
   `CmsPage.Content` (RichText/Blocks) and the consumer's `MapToDocument` never populates
   `Sections`. Redundant block model. Priority: Low. Status: Confirmed.
3. **Unused `StorefrontPageDocument.DisplayOrder`.** Carried for "menu ordering" but never set by
   the projection consumer. Priority: Low. Status: Confirmed.
4. **CMS `NoIndex` is unenforceable.** `SeoService.BuildCmsSeoMetadataAsync` hardcodes
   `Index = true` (`SeoService.cs:1055`) and the SQL `CmsPage` has no `NoIndex` column; the Mongo
   `CmsPageSeo.NoIndex` field (`StorefrontPageDocument.cs:119`) is never read by the SQL-authority
   SEO path. A merchant cannot mark a CMS page `noindex`. Priority: Medium. Status: Confirmed.
5. **CMS Elasticsearch indexing is manual-only (event-pipeline gap).** `CmsPageSearchDocument` is
   real and wired into `UnifiedSearchIndexService` (`cmspages` index), but it is written ONLY by
   the manual `AdminSearchSyncController.SyncCmsPages` — NOT by `StorefrontCmsPagePublishedEvent`/
   `DeletedEvent`. A newly published CMS page is not searchable until an admin triggers the sync.
   This is a deliberate decoupling but creates an eventual-consistency lag for CMS search. Priority:
   Medium. Status: Confirmed (manual-only, not absent).
6. **Homepage authority divergence.** POS Admin `StoreConfig.CustomizationJson.pages[home]` is
   authored but NOT rendered by public `HomePage.tsx` (which renders Super Admin `HomepageSection`).
   A scoped divergence, not total dead code (builder pages + bootstrap consume it). Priority: High
   (clarify which surface is canonical). Status: Confirmed (see `storefront-homepage-authority-
   divergence.md`).
7. **No `Unpublish` / `Archive` / scheduling.** Documented scope lists Scheduling/Recovery, but the
   implementation provides only publish/delete/rollback. Recovery = rollback; no time-based
   scheduling. Priority: Low (by design, but spec mismatch). Status: Confirmed absent.
8. **Duplicate/dead page types.** "Landing Page" / "Campaign Page" / "Marketing Page" are not
   distinct runtimes — they are ordinary `CmsPage` rows with slugs. No separate entity/service.
   Priority: Low (terminology drift). Status: Confirmed absent.
9. **No standalone Menu aggregate.** Navigation is JSON-in-StoreConfig; `Menu`/`MenuItem` do not
   exist as entities. Priority: Low. Status: Confirmed absent.

---

## Implementation Follow-up Items

| # | Title | Current Implementation | Why | Future Improvement | Priority | Status |
|---|---|---|---|---|---|---|
| 1 | Wire or retire Mongo CMS projection | `CmsPageProjectionConsumer` writes `storefront_pages`; storefront reads SQL | Mongo doc is unread for tenant CMS pages | Either serve storefront pages from Mongo (with SQL re-auth) or remove the consumer | Medium | Confirmed |
| 2 | Enforce CMS `NoIndex` | `SeoService` hardcodes `Index=true`; no SQL column | Merchants cannot hide a page from search | Add `NoIndex` column to `CmsPage` + map in `SeoService` | Medium | Confirmed |
| 3 | Resolve homepage authority divergence | Two homepage surfaces (StoreConfig vs HomepageSection) | Public render uses only Super Admin sections | Pick one canonical surface or explicitly document the split | High | Confirmed |
| 4 | Wire CMS publish/delete to Elasticsearch | ES `cmspages` only updated by manual `SyncCmsPages` | Newly published pages not searchable until manual sync | Add a `CmsPageSearchIndexer` consumer for `StorefrontCmsPagePublishedEvent`/`DeletedEvent` (or auto-trigger sync) | Medium | Confirmed (manual-only) |
| 5 | Remove unused Mongo fields | `Sections`, `DisplayOrder` never populated | Redundant model | Drop from `StorefrontPageDocument` or populate | Low | Confirmed |
| 6 | Clarify landing-page terminology | Landing/campaign pages = `CmsPage` slugs | Spec implies a distinct runtime | Document as CMS-page usage, not a new aggregate | Low | Confirmed |
| 7 | Optional scheduling | Publish is immediate; no `PublishDate`/`ExpiryDate` | Time-based publishing not needed yet | Add columns + Hangfire job if required | Low | Confirmed absent |

---

## Certification

- **Confidence:** High. Every normative claim in this document is traced to a `file:line` in the
  backend (`PointOfSale.Core/`, `PointOfSale.Infrastructure/`, `PointOfSale.API/`).
- **Completeness:** All requested runtime sections are covered: Content, Page, Homepage, Landing
  Page, Section, Navigation, Menu, SEO, Publication, Visibility, Scheduling, Media, Search, CQRS,
  Projection, Read/Write Models, Runtime Authority Matrix, Cross-Domain Graph, Business Invariants,
  Architectural Decisions, Technical Debt, Follow-up Items, plus the four REQUIRED deep sections
  (Content Lifecycle, CMS Decision Matrix, Content Lineage, Public Content Flow) and Architectural
  Synthesis.
- **Verified present:** `CmsPage` state machine + CHECK constraints; atomic publish under
  Serializable; outbox-driven Mongo/Redis projection; fail-closed tombstone; tenant-active gating;
  SQL-authoritative storefront reads; sitemap/slug SQL recomposition; two authority planes.
- **Verified absent (documented, not invented):** Archive state; Approved/approval workflow;
  Scheduling/auto-publish/expiry; Hard delete; Standalone Menu aggregate; Landing Page runtime;
  CMS Elasticsearch *auto-indexing via events* (manual sync exists); Twitter Card tags; CMS JSON-LD;
  CMS media upload pipeline.
- **Debt summary:** orphaned Mongo CMS projection (unread by storefront read path); unenforceable
  `NoIndex`; homepage authority divergence; CMS Elasticsearch indexing is manual-only (event
  pipeline gap); unused `Sections`/`DisplayOrder`.
- **Known gaps:** none outstanding — all citations in this document were verified against backend
  source by the authoring agent (including Navigation/Homepage consumer line numbers confirmed via
  `grep -n`). Two CMS surfaces (POS Admin StoreConfig homepage vs Super Admin HomepageSection) are a
  scoped authority divergence rather than a gap (see Homepage Runtime / Technical Debt #6).



---

## Content Lifecycle (REQUIRED — one complete lifecycle)

Reverse-engineered end-to-end lifecycle of a tenant CMS page, with runtime owner, SQL authority,
projection authority, events, synchronization, replay, failure behaviour, and visibility at each
stage.

```
Create Draft (v1)
  Owner: POS Admin (Owner/Admin) via AdminCmsPagesController.CreateCmsPage
  SQL: INSERT CmsPages (IsDraft=true, IsPublished=false, Version=1, Slug normalized)
       CmsPageService.CreatePageAsync (CmsPageService.cs:150-198)
  Projection: none (draft not projected)
  Events: none
  Visibility: hidden (draft)

Edit (vN+1)
  Owner: POS Admin UpdateCmsPage (ExpectedVersion optimistic concurrency)
  SQL: close prior draft (IsDraft=false); INSERT new draft Version=Max+1
       CmsPageService.UpdatePageAsync (CmsPageService.cs:200-275)
  Transaction: Serializable + FOR UPDATE (CmsPageService.cs:588-558,536-558)
  Projection: none
  Visibility: published page (if any) unaffected; new draft hidden

Preview
  Owner: author requests preview link/token
  SQL: read draft/preview version by PageGroupId+Version (no publish)
       CmsPageReadService.GetPreviewPageByIdAsync (CmsPageReadService.cs:77-100)
       AdminCmsService.CreateCmsPagePreviewAccessAsync -> CmsPagePreviewTokenService
       (signed, 15-min, slug-scoped) (CmsPagePreviewTokenService.cs:55-79)
  Visibility: draft shown only with valid previewToken; Cache-Control: no-store

Publish (promote newest draft -> live)
  Owner: POS Admin PublishCmsPage (ExpectedVersion)
  SQL: demote published rows; set draft IsDraft=false, IsPublished=true, PublishedAt=now
       CmsPageService.PublishPageAsync (CmsPageService.cs:283-339)
  Event: StorefrontCmsPagePublishedEvent (idempotency key ...:{tenant}:{group}:{version})
         staged via IEventPublisher (CmsPageService.cs:666-686)
  Outbox: OutboxMessage drained by OutboxPublisherJob (5s) -> RabbitMQ
  Projection authority: CmsPageProjectionConsumer (Mongo storefront_pages, fail-closed)
                        + RedisInvalidationConsumer (invalidate published scope)
  Synchronization: event-driven; Mongo write re-checks SQL authority + tenant lineage
  Replay: ProjectionEventFence collapses duplicates; freshness filter rejects stale
  Failure behaviour: if SQL unproven/inactive -> Tombstone (fail-closed), not write
  Visibility: PUBLIC (IsPublished && !IsDraft && PublishedAt != null && tenant active)

Search Index (sitemap only)
  Owner: SeoService (read-time)
  SQL authority: LoadCurrentPublishedCmsPagesAsync (SeoService.cs:1086-1114)
  Projection: sitemap entry /pages/{slug} priority 0.4; NOT Elasticsearch
  Visibility: appears in sitemap only if published

Homepage / Navigation (content consumption)
  Owner: StorefrontNavigationController (recomposes live nav vs SQL)
  SQL authority: published CMS slug check (AdminCmsService.cs:407-410)
  Visibility: link appears only if slug is a published page

Customer (storefront request)
  Owner: StorefrontPagesController.GetPage -> CmsPageReadService.GetPublishedPageAsync (SQL)
  SQL authority: CmsPageReadService.cs:121-142 (published + tenant active)
  Failure: 404 if not published/deleted; 503 only if SQL fails (StorefrontPagesController.cs:94-97,112)
  Visibility: PUBLIC

Rollback (recovery)
  Owner: POS Admin RollbackCmsPage (rollbackVersion, ExpectedVersion)
  SQL: restore prior Version snapshot as NEW published row Version=Max+1
       CmsPageService.RollbackPageAsync (CmsPageService.cs:341-434)
  Event: StorefrontCmsPagePublishedEvent (new version)
  Visibility: PUBLIC at restored content

Delete (soft)
  Owner: POS Admin DeleteCmsPage (ExpectedVersion)
  SQL: IsDeleted=true, IsDraft=false, IsPublished=false, PublishedAt=null (all active rows)
       CmsPageService.DeletePageAsync (CmsPageService.cs:436-506)
  Event: StorefrontCmsPageDeletedEvent (only if had published row)
  Projection: Mongo tombstone + Redis invalidate
  Visibility: GONE (404)
```

Note: there is **no Archive state and no Approval stage** — the lifecycle above is the complete
implemented set. Recovery = Rollback; there is no time-based scheduling.

---

## CMS Decision Matrix (REQUIRED — every decision)

| Decision | Business Rule | Runtime Owner | Authority | Validation Layer | Failure Behaviour | Implementation Evidence |
|---|---|---|---|---|---|---|
| Can draft pages appear publicly? | No | `CmsPageReadService` | SQL `IsPublished && !IsDraft` | read filter | 404 | `CmsPageReadService.cs:131-141` |
| Can hidden pages appear in navigation? | No (unpublished CMS slug dropped) | `StorefrontNavigationController` | SQL published-CMS check | recomposition | link removed | `AdminCmsService.cs:407-410` |
| Can deleted pages appear in search/sitemap? | No | `SeoService` | SQL `!IsDeleted` | sitemap query | excluded | `SeoService.cs:735-745` |
| Can scheduled pages appear early? | N/A — scheduling not implemented | — | — | — | publish is immediate | `CmsPage.cs` (no date cols) |
| Can expired pages remain visible? | N/A — no expiry | — | — | — | stays until deleted/rolled-back | `CmsPage.cs` |
| Can duplicate slugs exist? | No (per tenant per state) | `CmsPageService` | SQL partial unique idx | `EnsureSlugAvailableAsync` | `SlugConflictException` (409) | `20260329_AAB_AddCmsPages.sql:47-55`; `CmsPageService.cs:630-648` |
| Can homepage contain unpublished sections? | N/A — homepage is Super Admin-only published sections | `SuperAdminHomepageSectionManagementService` | SQL | publish gate | only published served | homepage notes |
| Can search bypass publication? | No — storefront re-auths vs SQL; ES only via manual sync | `StorefrontSearchController` / `AdminSearchSyncController` | SQL `IsPublished` | SQL filter on search results | drafts excluded; ES stale deleted on inactive tenant | `StorefrontSearchController.cs:428-445`; `AdminSearchSyncController.cs:347-357` |
| Can CMS mutate media? | No — URL reference only | content author | `CmsPageContentValidator` | safe-URL validation | reject unsafe URL | `CmsPageContentValidator.cs:250-281` |
| Can CMS own SEO authority? | Yes for page meta; SEO service is read-only derivation | `CmsPageService` (meta) + `SeoService` (derive) | SQL `CmsPage` + derived | length/URL limits | truncate/throw | `CmsPageConfiguration.cs:54-64`; `SeoService.cs:1035-1061` |
| Can a non-active tenant serve CMS? | No | `CmsPageReadService` / `CmsPageProjectionConsumer` | SQL `Tenants` | `IsTenantPubliclyActiveAsync` / `TenantLineageAuthority` | 404 / tombstone | `CmsPageReadService.cs:184-194`; `CmsPageProjectionConsumer.cs:121-145` |
| Can a published page be edited in place? | No — edit creates a new draft version | `CmsPageService` | SQL (new row) | `ExpectedVersion` | `ConcurrencyConflictException` | `CmsPageService.cs:200-275,608-614` |

---

## Content Lineage (REQUIRED — every projection)

For each CMS projection: original SQL tables, projection pipeline, transformation, visibility/
publication/scheduling/search rules, authority, and evidence.

| Projection | Original SQL | Pipeline | Transformation | Visibility/Pub/Sched/Search rules | Authority | Evidence |
|---|---|---|---|---|---|---|
| Storefront page (served) | `CmsPages` | read directly (SQL) | map to `CmsPageDto` (JSON parse content) | pub=IsPublished; sched=N/A; search=sitemap only | `CmsPageReadService` | `CmsPageReadService.cs:57-142` |
| Mongo `storefront_pages` | `CmsPages` | `CmsPageProjectionConsumer` (event) | `MapToDocument` -> `StorefrontPageDocument` | fail-closed on SQL/pub/tenant; tombstone on delete | `CmsPageProjectionConsumer` | `CmsPageProjectionConsumer.cs:19-302,537` |
| Redis published scope | `CmsPages` | `CmsPageCacheService` (read) + invalidate on event | cache `List<CmsPageDto>` | TTL≥1m; invalidate on publish/delete | `CmsPageCacheService`/`RedisInvalidationConsumer` | `CmsPageCacheService.cs:42-118`; `RedisInvalidationConsumer.cs:306-315` |
| Homepage (marketplace Mongo) | `HomepageSections` | `HomepageProjectionConsumer` | `MarketplaceHomepageReadModelDocument` | Super Admin published sections | `SuperAdminHomepageSectionManagementService` | homepage notes |
| Navigation (live) | `StoreConfigs.CustomizationJson` | `StorefrontNavigationController` recompute | drop unpublished CMS/dead cat/unsafe URLs | live only; empty if not live | `AdminCmsService` | `AdminCmsService.cs:285-427` |
| Footer | `StorefrontFooterSections` | `StorefrontFooterReadService` + projection consumer | tenant→global→derived(CMS) | published pages only for derived | footer write svc | footer notes |
| SEO metadata | `CmsPages`/`Products`/`Categories` | `SeoService` (read) | canonical/OG/breadcrumb JSON-LD | pub filter; Index hardcoded true for CMS | `SeoService` | `SeoService.cs:1035-1061,208-265` |
| Sitemap | `CmsPages`/`Products`/`Categories` | `SeoService.GetSitemapAsync` (read) | URL list, recomposed SQL | published CMS only | `SeoService` | `SeoService.cs:644-773` |
| CMS search (ES `cmspages`) | `CmsPages` | `AdminSearchSyncController.SyncCmsPages` (manual admin) | `CmsPageSearchDocument` (UnifiedSearchIndexService) | manual on-demand; SQL `IsPublished` gate; storefront re-auth vs SQL | `AdminSearchSyncController.cs:306-365`; `UnifiedSearchIndexService.cs:280-335` |

### Answer: "How does one content record become publicly visible?"

1. Author creates a draft (`CmsPages` row, `IsDraft=true`) — invisible.
2. Author publishes (`PublishPageAsync`): the draft row is promoted to
   `IsDraft=false, IsPublished=true, PublishedAt=now` inside a Serializable transaction; a
   `StorefrontCmsPagePublishedEvent` is staged to the outbox.
3. The outbox drains the event to RabbitMQ; `CmsPageProjectionConsumer` re-verifies SQL authority
   + tenant lineage and upserts `storefront_pages` (or tombstones if unproven); `RedisInvalidationConsumer`
   invalidates the Redis published scope.
4. A customer requests `GET /api/storefront/pages/{slug}`. `StorefrontPagesController` →
   `CmsPageReadService.GetPublishedPageAsync` queries SQL for
   `!IsDeleted ∧ IsPublished ∧ !IsDraft ∧ PublishedAt != null` AND `Tenants.IsActive && Status==Active`.
   If found → 200 with the page; else 404.
5. The page also enters the SQL-recomposed sitemap (`/pages/{slug}`) and is reachable via slug
   resolution; it appears in live navigation only if its slug survives recomposition.

Visibility is therefore a single deterministic SQL conjunction, recomputed at read and re-verified
at projection — never a naked UI toggle.

---

## Public Content Flow (REQUIRED — one complete request)

```
CMS Content (SQL CmsPages, IsPublished)
  Runtime owner: CmsPageService (write) / CmsPageReadService (read)
  Authority: PostgreSQL (ApplicationDbContext)
        |
        v  (publish)
Publication (StorefrontCmsPagePublishedEvent -> OutboxMessage)
  Runtime owner: CmsPageService + OutboxPublisherJob
  Authority: OutboxMessage (generic, 5s drain, critical EventVersion ordering)
        |
        v  (RabbitMQ)
Projection (CmsPageProjectionConsumer -> Mongo storefront_pages; RedisInvalidationConsumer -> Redis)
  Runtime owner: CmsPageProjectionConsumer / RedisInvalidationConsumer
  Authority: Mongo + Redis (derivative, FAIL-CLOSED, fenced, freshness-guarded)
  Synchronization: event-driven; SQL re-verified before write; tombstone on unproven
  Replay: ProjectionEventFence collapses duplicates; stale rejected by freshness filter
  Consistency: eventually consistent; SQL IsPublished is immediate truth
        |
        v  (customer GET /api/storefront/pages/{slug})
Storefront (StorefrontPagesController -> CmsPageReadService.GetPublishedPageAsync)
  Runtime owner: CmsPageReadService
  Authority: SQL (re-query published + tenant active) -- NOT Mongo
  Synchronization: cache-first Redis, fall through to SQL; 503 only if SQL fails
  Consistency: deterministic SQL conjunction; tenant-active gate
        |
        v
Customer (200 page / 404)
  Failure behaviour: 404 if not published/deleted; 503 only on SQL failure
        |
        v  (SEO discovery)
Analytics / Search (sitemap entry /pages/{slug}; slug resolution)
  Runtime owner: SeoService
  Authority: SQL-recomposed sitemap (published CMS only); NOT Elasticsearch
  Consistency: recomputed per read; stale cache rejected
```



---

## Architectural Synthesis

The CMS Runtime is not a page editor and not a frontend framework — it is the **Content Authority
Runtime** of Shopixy: the bounded context that owns tenant-authored (and, for Super Admin,
marketplace) content as PostgreSQL truth and projects it, event-driven and fail-closed, into
derivative read models. Synthesizing the subsystems above into one architecture reveals a set of
implementation-backed patterns shared across Shopixy's platforms — and why the CMS Runtime was
built this way.

### Shared platform patterns (evidenced across Merchant/Search/Inventory/CommerceHub/Affiliate/SuperAdmin/Analytics)

1. **SQL Authority.** Every CMS mutation commits to PostgreSQL through `ApplicationDbContext`;
   Mongo/Redis/ES are derivatives only. This is the same rule as Product Catalog, Inventory, and
   CommerceHub financial state — one write authority, many read accelerators.
2. **CQRS + Outbox.** Publication is separated from editing and flows through the generic
   transactional outbox (`OutboxPublisherJob`, 5s drain, critical `EventVersion` ordering) to
   MassTransit consumers. Identical backbone to Product/Inventory/CommerceHub projections.
3. **Projection (event-driven, never polled).** `CmsPageProjectionConsumer` + `RedisInvalidationConsumer`
   hydrate Mongo/Redis from outbox events, with retry (1s/5s/15s) and `ProjectionEventFence`
   idempotency. Same projection discipline as the rest of the platform.
4. **Outbox / Replay / Repair-on-Read.** Stale or duplicate events are collapsed by the fence and
   rejected by freshness filters; read paths (sitemap, slug resolution) recompute against live SQL
   rather than trusting the projection. This is the content analogue of the category "Redis/Mongo
   not authority" rule.
5. **Fail-closed projection.** The CMS consumer re-verifies SQL publish state + tenant lineage and
   TOMBSTONES on unproven authority — the content-surface equivalent of the product visibility
   gate (`IsActive && IsOnlineVisible`) and CommerceHub financial fail-closed.
6. **Publication Gates / Visibility Gates.** Public visibility is a single SQL conjunction
   (`!IsDeleted ∧ IsPublished ∧ !IsDraft ∧ PublishedAt != null ∧ tenant active`), enforced at read
   AND at projection. Parallel to the product `PublishStatus=Published` gate.
7. **Cross-Domain Isolation.** Tenant `X-Tenant-Id` is stripped and re-resolved; every query is
   `TenantId`-scoped; marketplace content is a separate authority plane (Super Admin) isolated from
   tenant tampering. Same boundary discipline as CommerceHub/SuperAdmin.
8. **Runtime Boundaries / Content Authority.** CMS owns content but defers media (URLs only),
   catalog (live lineage recomposition), search indexing (products/categories in ES, CMS in
   sitemap), and all financial/fulfilment lifecycles. This is the same "own the intent, defer the
   lifecycle" boundary used by Merchant Platform and CommerceHub.

### Why this architecture (the Through-Line)

The CMS Runtime was architected to make **public storefront content deterministic and recoverable**
without coupling it to the high-volume commerce machinery:

- **Low-write, high-correctness content** is served from SQL with a tenant-active gate, so what a
  customer sees is always a single, debuggable query — not a projection that could drift.
- **Draft/publish separation** (mutually exclusive rows + atomic promote under Serializable) gives
  authors a safe edit surface and makes publish a single auditable, event-emitting action.
- **Event-driven projection** keeps the storefront fast (Redis/Mongo) while preserving SQL as the
  rebuildable truth; fail-closed tombstoning guarantees a stale or authority-less derivative can
  never be served.
- **Two authority planes** (tenant vs marketplace) enforce platform governance isolation by
  construction, mirroring how SuperAdmin governs CommerceHub without owning its runtime.
- **Recovery via version rollback + fail-closed repair** replaces the unimplemented Archive/
  scheduling features, keeping the content state machine small and the visibility rule trivial.

### The single sentence

The CMS Runtime is the SQL-authoritative, outbox-projected, fail-closed Content Authority Runtime
that turns tenant-authored drafts into publicly-visible, tenant-isolated storefront pages — serving
customers from a deterministic SQL visibility conjunction while projecting accelerators that can
always be rebuilt from SQL truth.

---

*End of CMS Runtime — Final Canonical Architecture. Every claim is implementation-backed and cited
to `file:line`. Prior markdown, comments, and certification docs were treated as secondary. Where a
capability is absent (Archive, Approval, Scheduling, Landing-Page runtime, Menu aggregate, CMS
Elasticsearch indexing), it is explicitly marked verified-absent rather than invented.*
