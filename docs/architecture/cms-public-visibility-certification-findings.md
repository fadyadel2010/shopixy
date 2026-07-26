# CMS Public Visibility Certification Findings

Date: 2026-05-29

Scope: POS-ADMIN-CERTIFICATION-01 PASS 9.1, CMS Authority, Publication & Visibility Certification.

## Authority Model

- PostgreSQL `CmsPages` is the authority for tenant storefront CMS visibility.
- A public CMS page is visible only when current SQL proves active tenant lineage, same `TenantId`, `IsPublished = true`, `IsDraft = false`, `IsDeleted = false`, and `PublishedAt != null`.
- `StorefrontPageDocument`, Redis CMS scope caches, Elasticsearch `cmspages`, sitemap cache content, and replayed publish/delete events are derivative evidence only.
- If SQL and any derivative surface disagree, SQL wins and the public path fails closed.

## Findings

- Confirmed visibility gap: `CmsPageReadService.GetPublishedPageAsync(...)` and `ListPublishedPagesAsync(...)` could serve Redis/Mongo published-scope data without current SQL publication revalidation. A stale cache or stale `storefront_pages` document could therefore preserve a deleted or unpublished CMS page on public storefront routes.
- Confirmed SEO gap: `SeoService.GetSitemapAsync(...)` returned cached sitemap data before recomposition. Once CMS URLs were included or cached externally, that pattern could preserve deleted or unpublished CMS URLs as SEO visibility.
- Confirmed search gap: unified search treated Elasticsearch CMS hits as returnable public results. A stale `cmspages` document could appear in public search even when SQL no longer published the page.
- Recovery gap: tenant-admin CMS search sync was intentionally unimplemented. That was safer than partial rebuild, but it left no certified convergent repair path to delete stale CMS search documents.
- Projection predicate gap: the reviewed `CmsPageProjectionConsumer` publish path already reloaded SQL and tenant lineage, but its SQL predicate accepted `IsPublished` without explicitly requiring `IsDraft = false` and `PublishedAt != null`. Public reads still failed closed, but the projection mirror was looser than the certified public predicate.
- Route-cache tenant gap: cached CMS slug resolution revalidated the current CMS page row, but did not revalidate active tenant lineage before accepting a cached CMS route result.

## Runtime Hardening

- Public CMS reads now load current published SQL pages directly and require active tenant authority. Redis and Mongo no longer decide public CMS page visibility.
- CMS SEO metadata now supports `type=cms` and derives from current SQL only.
- Sitemap generation now recomposes on each request and adds CMS page URLs only from current SQL. Redis sitemap data remains a write-through optimization artifact, not a return authority.
- Slug resolution can resolve CMS pages only after current SQL proves the page is published; cached CMS slug results are rejected when SQL no longer agrees.
- Cached CMS slug results now also revalidate active SQL tenant lineage before returning route visibility.
- Unified storefront search now filters CMS hits through current SQL tenant/page publication authority before returning them.
- Tenant-admin CMS search repair now declares `SqlAuthorityOverlayKind.StorefrontCms`, indexes only current published SQL CMS pages, and deletes tenant-scoped CMS search documents missing from the current SQL authority set.
- CMS page projection publish replay now mirrors the same SQL publication predicate used by public reads: same tenant, active tenant lineage, non-deleted, published, non-draft, and `PublishedAt != null`.

## Regression Freeze

- `CmsPageWorkflowTests.GetPublishedPageAsync_DoesNotLetCacheResurrectUnpublishedSqlPage` freezes the cache-resurrection failure mode.
- `CmsPublicVisibilityCertificationTests` freezes source-level invariants for public CMS SQL reads, CMS SEO/sitemap SQL recomposition, cached CMS slug tenant-lineage validation, SQL-filtered CMS search results, convergent CMS search repair, and projection-only mirror predicates.
- Focused validation on 2026-05-29:
  - `dotnet build PointOfSale.API\PointOfSale.API.csproj -nologo -v minimal -m:1`
  - `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj -nologo -v minimal --filter "FullyQualifiedName~CmsPageWorkflowTests|FullyQualifiedName~CmsPublicVisibilityCertificationTests"`

## Remaining Debt

- Marketplace CMS uses a separate global authority model and was not reworked in this pass.
- Product/category SEO still depends on previously certified product/category projection authority and existing read repositories; this pass only certifies CMS-specific public visibility and SEO resurrection controls.
- No scheduled CMS publication worker was found in the inspected storefront CMS path. If scheduled publication is added later, it must revalidate active tenant, active page group, current draft/published eligibility, slug ownership, and delete/archive state at execution time.
- No CMS approve/reject/archive workflow exists on this storefront CMS implementation. Current lifecycle is draft, publish, unpublished-by-replacement, and delete.
- Future CMS cache warmers, CDN purge jobs, feed/export writers, support tools, localization, and global scripts remain path-specific certification debt until they add current SQL publication checks, tenant scope, replay behavior, and tests.
