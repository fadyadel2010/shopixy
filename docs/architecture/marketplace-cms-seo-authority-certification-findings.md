# PASS M12 Marketplace CMS / SEO Authority Certification Findings

Date: 2026-06-11

Status: production-certifiable for the inspected runtime after marketplace CMS runtime hardening.

## Authority Decision

Marketplace CMS / SEO authority is SQL-owned. PostgreSQL `MarketplaceCmsPages` owns marketplace page identity, drafts, publication, versions, previewable version identity, public rendering, and marketplace page SEO metadata. Storefront tenant CMS SEO remains SQL-owned by tenant `CmsPages`. Canonical URLs, sitemap entries, robots sitemap references, structured-data inputs, and public indexing eligibility must derive from current published SQL truth and active tenant/domain resolution, not DTOs, caches, projections, frontend state, route values, or stale previews.

## Findings

### Page Findings

`MarketplaceCmsPages.PageGroupId` owns marketplace CMS page identity and `(PageGroupId, Version)` owns version identity. Slugs are normalized and active draft/published slug conflicts are checked in SQL. Deleted/inert historical versions no longer carry draft or published authority.

### Draft Findings

Drafts are SQL evidence only. Runtime hardening changed marketplace draft updates from active-draft overwrite to immutable version creation: the previous draft is retired and a new `Version = max + 1` draft is inserted after the caller proves the current expected version.

### Publication Findings

Publication is Super Admin runtime authority over SQL. Publish now requires an exact expected draft version, serializable SQL transaction, page-group `FOR UPDATE` lock on PostgreSQL, slug revalidation, current published withdrawal, event staging, and cache invalidation after commit. Stale publish attempts conflict.

### Preview Findings

Marketplace preview links are protected capabilities bound to slug, page group, expiration, and exact page version. Public preview rendering now loads only the token-bound version and uses `no-store` response headers. A copied token cannot drift forward to a newer draft or publication.

### Versioning Findings

Marketplace CMS versions are monotonic SQL rows. Update appends new draft versions; publish promotes the exact reviewed draft; delete withdraws active draft/live flags. Prior versions are inert history and cannot become public without a fresh current-version mutation.

### SEO Findings

Marketplace page SEO metadata is stored with the SQL page version and public rendering returns only current published SQL rows. Storefront SEO for tenant CMS pages uses `LoadCurrentPublishedCmsPageBySlugAsync`, active tenant lineage, and SQL page metadata. Canonical URL construction ignores caller `baseUrl` in favor of server-resolved storefront URL authority on storefront SEO paths.

### Sitemap Findings

Storefront sitemap generation recomposes categories from SQL lineage, products through SQL search authority overlay, and CMS pages from current published SQL `CmsPages`. It does not return a stale Redis sitemap as authority. Hidden, deleted, unpublished, or inactive-tenant CMS pages cannot enter inspected sitemap output.

### Projection Findings

Marketplace public page reads load SQL directly and filter cached page lists against current SQL publication authority. Redis remains optimization only. Storefront SEO slug resolution rejects cached CMS/product/category entries that current SQL authority no longer proves.

### Isolation Findings

Marketplace CMS is global platform content under Super Admin authority; tenant storefront CMS remains tenant-scoped. Storefront SEO/sitemap reads resolve tenant authority through trusted tenant context and active tenant SQL lineage. Storefront and marketplace CMS pages with the same slug remain isolated.

### Concurrency Findings

Marketplace update, publish, and delete now require current expected version authority. Relational mutations use serializable transactions and PostgreSQL page-group row locks. Stale edit, publish, and delete replay fail closed with concurrency conflicts.

### Historical Findings

Historical versions cannot resurrect because only active `IsDraft` or `IsPublished` rows are candidates, and old drafts are retired before new drafts are inserted. Delete withdraws active flags instead of allowing removed rows or projections to define current state.

### Adversarial Findings

Inspected paths block draft exposure through public reads, preview token drift, publication bypass, stale draft resurrection, SEO metadata poisoning from DTOs outside SQL persistence, canonical URL spoofing on storefront SEO, sitemap poisoning by hidden CMS pages, cache/projection authority, route tampering, and tenant storefront leakage.

## Runtime Hardening Performed

- Added marketplace CMS expected-version authority to update, publish, and delete.
- Changed marketplace CMS draft updates to append immutable draft versions and retire prior active drafts.
- Added serializable mutation transactions and PostgreSQL page-group `FOR UPDATE` locks for marketplace CMS mutations.
- Bound marketplace preview tokens to exact page version and public preview reads to that version.
- Changed marketplace delete to withdraw active SQL draft/published flags instead of hard-removing current authority rows.

## Exact Runtime Files Modified

- `PointOfSale.API/Services/MarketplaceCms/MarketplaceCmsPageService.cs`
- `PointOfSale.API/Services/MarketplaceCms/MarketplaceCmsPageReadService.cs`
- `PointOfSale.API/Services/MarketplaceCms/MarketplaceCmsPagePreviewTokenService.cs`
- `PointOfSale.API/Services/MarketplaceCms/MarketplaceCmsPageSeeder.cs`
- `PointOfSale.API/Controllers/MarketplaceCmsPagesController.cs`
- `PointOfSale.API/SuperAdmin/Controllers/SuperAdminMarketplaceCmsPagesController.cs`
- `PointOfSale.API/SuperAdmin/DTOs/MarketplaceCmsDtos.cs`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/MarketplaceCmsSeoAuthorityCertificationTests.cs`
- Updated `PointOfSale.Tests/Cms/MarketplaceCmsPageWorkflowTests.cs`
- Updated `PointOfSale.Tests/Cms/MarketplaceCmsPagePreviewTokenServiceTests.cs`

## Explicit Answers

- Who owns CMS authority? PostgreSQL `MarketplaceCmsPages` for marketplace CMS and tenant PostgreSQL `CmsPages` for storefront CMS.
- Can drafts become authority? No. Drafts are SQL review state only and require exact-version publish before public authority.
- Can unpublished pages appear? No inspected public path returns unpublished CMS pages.
- Can previews expose drafts publicly? No. Preview requires a protected, expiring, exact-version token and returns `no-store`; it does not publish.
- Can canonical URLs be spoofed? No on inspected storefront SEO paths; server-resolved storefront URL authority is used instead of caller `baseUrl`.
- Can hidden pages enter sitemaps? No. Sitemap CMS entries derive from current published SQL pages and active tenant lineage.
- Can tenant leakage occur? No inspected storefront CMS/SEO path permits it; marketplace CMS is global platform content and store CMS remains tenant-scoped.
- Were runtime code changes required? Yes.
- Is Marketplace CMS / SEO Authority production-certifiable? Yes, for the inspected runtime.

## Residual Risk

Production still needs CMS/SEO parity dashboards, crawler/indexing telemetry, WORM/SIEM evidence retention for Super Admin CMS publication, query-plan review, backup/restore rehearsal, and PostgreSQL-backed high-concurrency chaos runs for marketplace CMS edit/publish/delete/preview races. Adding marketplace localized standalone pages, scheduled publication, restore/rollback, per-domain marketplace CMS variants, external sitemap feeds, or dedicated marketplace structured-data editors requires renewed M12 review.
