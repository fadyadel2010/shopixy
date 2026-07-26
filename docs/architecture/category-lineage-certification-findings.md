# Navigation, Category Lineage & Storefront Structure Certification Findings

Date: 2026-05-29

Scope: POS-ADMIN-CERTIFICATION-01 PASS 9.2, Navigation, Category Lineage & Storefront Structure Certification.

## Authority Model

- PostgreSQL `Categories` owns storefront category hierarchy, lineage, routing eligibility, SEO visibility, breadcrumb category ancestry, homepage category eligibility, and category navigation eligibility.
- A storefront category is public only when current SQL proves same `TenantId`, non-deleted row state, and a parent chain that is present, same-tenant, non-deleted, non-empty, non-self-referential, and acyclic.
- MongoDB category documents, Redis category/navigation/SEO caches, Elasticsearch category documents, navigation JSON, breadcrumb DTOs, route/slug cache entries, sitemap cache entries, and replay/rebuild payloads are derivative evidence only.
- If SQL and any derivative surface disagree, SQL wins and public structure fails closed.

## Findings

- Confirmed category read gap: storefront category tree and category-by-slug reads treated Redis/Mongo category projections as return authority. Projection pruning hid obvious orphan/cycle cases, but a stale Mongo/Redis category could still survive after SQL deletion or parent-lineage invalidation until sync/cache convergence.
- Confirmed navigation gap: storefront navigation reads could return cached or Mongo category links without proving that the linked category slug still belonged to a current SQL-live category lineage.
- Confirmed breadcrumb/SEO gap: category breadcrumbs, category SEO metadata, category slug resolution, and category sitemap URLs were still composed from category projections. That made breadcrumbs and SEO derivative surfaces able to preserve stale category structure after SQL truth changed.
- Existing rebuild/search protections held: tenant-admin category Mongo sync and category Elasticsearch sync already use `CategoryLineageAuthority`, delete stale derivative documents missing from SQL authority, and fail closed on invalid lineage.
- Existing marketplace protections held: CommerceHub category lineage remains governed by `CommerceHubCategoryLineageAuthority`; this pass did not rework that separate global category model.

## Runtime Hardening

- `StorefrontCategoryService` now recomposes public category tree and category-by-slug reads from current SQL category lineage on every read. Redis remains a write-through optimization artifact and Mongo remains projection evidence, not public return authority.
- `SeoService` now derives category SEO metadata, category breadcrumb ancestry, category slug-cache acceptance, and category sitemap URLs from current SQL category lineage.
- `StorefrontNavigationController` now revalidates category navigation links through current SQL category lineage before returning cached or Mongo-backed navigation DTOs. Stale category menu links are filtered out.
- Tenant authority is part of each new public read proof through `TenantLineageAuthority.IsActiveTenantAsync(...)`.

## Regression Freeze

- `NavigationCategoryStructureCertificationTests` freezes SQL-lineage recomposition for storefront category reads, category SEO/breadcrumb/sitemap/slug resolution, and category navigation-link filtering.
- Existing `CategoryLineageAuthorityTests` continue to freeze orphan, deleted-parent, self-reference, and cyclic-lineage fail-closed behavior.
- Existing operational tooling tests continue to freeze category Mongo/search sync as convergent SQL-first repair instead of additive rebuild.

## Certification Results

- Category lineage authority: certified on reviewed storefront/admin/search boundaries; invalid SQL lineage fails closed.
- Navigation authority: hardened so navigation menus cannot own category visibility.
- Storefront structure: hardened so storefront category structure is current SQL-derived on public reads.
- Breadcrumb authority: hardened so category breadcrumb ancestry is derived from current SQL lineage.
- Route ownership: category slug resolution now rejects stale cached category routes unless SQL still owns the slug.
- Category SEO: category sitemap and metadata now derive from current SQL lineage.
- Projection authority: Mongo category/search/navigation projections remain mirrors; admin sync/search repair delete stale derivative documents.
- Search/discovery: reviewed tenant-admin category search repair is convergent SQL-first; product search category lineage remains guarded by `ProductSearchSqlAuthorityOverlay`.
- Replay/rebuild: rebuilds and repairs must use current SQL lineage and stale-delete/tombstone outcomes.
- Recovery tools: reviewed category Mongo/search repair remains tenant-scoped, SQL-first, and stale-delete capable.
- Tenant isolation: all reviewed category lineage decisions bind `TenantId`; public navigation/category reads also require active tenant lineage.
- Resurrection chaos: under publish/build/cache/index/delete/restart/rebuild pressure, reviewed category tree, category route, category SEO, breadcrumb, navigation, Mongo sync, and category search repair converge to absence when SQL no longer proves live lineage.

## Remaining Debt

- Storefront category slugs are still generated from SQL category names because the current SQL category entity does not carry a first-class slug field. This preserves SQL authority but leaves historical slug stability as future schema debt.
- Category `NameAr`, category promotion banner overlays, and some storefront projection decoration remain Mongo-derived presentation fields. They are not category visibility authority; if they become route/SEO authority later, they need SQL ownership or explicit recomposition.
- Product SEO still depends on previously certified product/category projection and product SQL overlay behavior; this pass focused on category structure and navigation surfaces.
- Future feed/export, CDN purge, support tooling, cache warmers, analytics/reporting, localization, global scripts, and new category management workflows require path-specific certification before they are treated as resurrection-resistant public structure infrastructure.
