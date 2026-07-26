# PASS P2-GD Storefront Navigation Authority Certification

Date: 2026-06-09

Status: production-certifiable after runtime hardening.

## Authority Decision

Tenant-owned PostgreSQL owns storefront navigation authority:

- `StoreConfig.CustomizationJson.navigation.live` owns published header/footer menu identity,
  membership, ordering, visibility, and target declarations.
- `StoreConfig.RowVersion` owns navigation edit and publish concurrency.
- current SQL `CmsPages` publication state owns whether a CMS target is publicly resolvable.
- current SQL category lineage owns whether a category target is publicly resolvable.
- SQL `StorefrontFooterSections`, `StorefrontFooterLinks`, and `StorefrontSocialLinks` own the
  dedicated footer configuration.

Builder state, draft navigation, MongoDB, Redis, browser state, DTOs, events, projections, themes,
routes, and generated defaults are derivative only.

## Findings

### Navigation Identity

Navigation identity is tenant-scoped through the trusted tenant context and the tenant's single
SQL `StoreConfig`. Duplicate targets inside a menu are rejected. Public reads never accept a
tenant, menu, or identity from caller-controlled route or DTO data.

### Publication

Only `navigation.live` is publicly read. Draft and legacy root navigation shapes fail closed.
Publishing requires Owner/Admin authorization, an expected SQL row version, and successful target
revalidation. Empty live menus are authoritative withdrawal; there is no separate unpublish
endpoint.

### Targets

CMS targets are revalidated against current non-deleted, published, non-draft SQL pages with a
publication timestamp. Category targets are recomposed from current live SQL lineage. Internal
paths reject control characters, backslashes, protocol-relative paths, admin/API paths, and
preview query authority. External links require HTTP(S) and reject credentials.

### Ordering

The backend canonicalizes ordering from request list position. Caller-supplied duplicate or stale
order values cannot become ordering authority. Public reads apply deterministic order and source
index tie-breaking.

### Visibility

Header and footer menus are independently read from the live branch. Disabled, maintenance, or
non-active tenant storefronts return no navigation. Storefront headers no longer synthesize
Home/Products when authority returns an empty menu.

### Builder

The browser editor is a draft client only. Save and publish carry the server-issued row version.
Unsaved state cannot publish, and stale state receives a concurrency conflict.

### Caching And Projection

Public navigation and dedicated storefront footer reads now use current SQL directly. MongoDB and
Redis cannot expose, restore, or reorder navigation. The remaining StoreConfig projection is
derivative, SQL-recomposed, event-fenced, freshness-guarded, and fail-closed.

### Updates

Navigation and footer mutations derive tenant identity from trusted context and require
Owner/Admin. Navigation update/publish requires `RowVersion` or `If-Match`. Footer replacement
requires the last server `UpdatedAtUtc`. Missing preconditions return HTTP 428.

### Consumption

Storefront renderers consume the navigation store populated by the public navigation API. Themes
do not create fallback menu authority. Empty or failed reads render no navigation.

### Tenant Isolation

All SQL reads and writes are tenant-scoped. CMS and category targets are resolved inside the same
tenant. A foreign tenant identifier cannot be supplied through navigation routes or DTOs.

### Concurrency And Idempotency

SQL optimistic concurrency prevents stale navigation update/publish replay. Footer replacement
compares the expected aggregate update timestamp and translates database concurrency failures.
Projection event fences and freshness predicates collapse duplicate/stale delivery.

### Historical Consistency

Draft history is inert until a version-bound publish succeeds. Public reads do not use snapshots,
cache fallbacks, or deleted projection state. Rebuilds recompute from current SQL truth.

### Adversarial Review

Navigation spoofing, DTO/route tenant tampering, draft exposure, hidden target exposure, unsafe URL
targets, stale replay, cache/projection poisoning, menu resurrection, and cross-tenant target
guessing fail closed under the hardened runtime paths.

## Runtime Hardening Performed

- replaced cache/projection-first public navigation reads with current SQL live-state composition;
- removed generated default navigation and frontend fallback links;
- added current CMS publication and category-lineage target validation on save, publish, and read;
- added safe internal/external URL validation and duplicate target rejection;
- canonicalized menu and footer ordering;
- added row-version preconditions to navigation update and publish;
- made dedicated footer reads current-SQL-only and live-storefront-gated;
- added footer aggregate replay protection and restricted footer writes to Owner/Admin;
- preserved version tokens through the admin navigation and footer editors.

## Exact Runtime Files Modified

- `PointOfSale.API/Controllers/AdminCmsPagesController.cs`
- `PointOfSale.API/Controllers/AdminStorefrontFooterController.cs`
- `PointOfSale.API/Controllers/StorefrontNavigationController.cs`
- `PointOfSale.API/DTOs/Admin/AdminCmsDtos.cs`
- `PointOfSale.API/DTOs/Admin/AdminStorefrontFooterDtos.cs`
- `PointOfSale.API/Services/Admin/IAdminCmsService.cs`
- `PointOfSale.API/Services/Admin/AdminCmsService.cs`
- `PointOfSale.API/Services/Admin/AdminStorefrontFooterService.cs`
- `PointOfSale.API/Services/Storefront/StorefrontFooterReadService.cs`
- `PointOfSale.Web/src/modules/storefront/cms/pages/NavigationEditor.jsx`
- `PointOfSale.Web/src/modules/storefront/cms/pages/StorefrontFooterSettings.jsx`
- `PointOfSale.Web/src/modules/storefront/cms/services/cmsService.js`
- `PointOfSale.Storefront/src/components/layout/Header.tsx`

## Regression Freezes

- `PointOfSale.Tests/Architecture/StorefrontNavigationAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/NavigationCategoryStructureCertificationTests.cs`
- `PointOfSale.Tests/Architecture/StorefrontContentAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/RedisCacheFreshnessInvalidationCertificationTests.cs`
- `PointOfSale.Tests/Cms/FooterSystemTests.cs`
- `PointOfSale.Tests/Admin/AdminCmsPublishReliabilityTests.cs`

## Explicit Answers

- Who owns navigation authority? Tenant-owned PostgreSQL navigation and footer rows.
- Can draft navigation leak? No.
- Can unpublished navigation become visible? No.
- Can navigation expose draft CMS content? No.
- Can navigation bypass CMS authority? No.
- Can builder state become authority? No.
- Can caches become authority? No.
- Can stale navigation resurrect? No.
- Can tenant leakage occur? No inspected runtime path permits it.
- Can replay overwrite navigation truth? No; expected-version preconditions reject stale writes.
- Were runtime code changes required? Yes.
- Is Storefront Navigation Authority production-certifiable? Yes.

## Residual Risk

Production PostgreSQL race/load evidence is operational and is not stored in this repository.
Adding mega-menu-specific persistence, scheduled publication, restore, standalone unpublish,
localized navigation variants, or arbitrary executable/custom URL schemes requires renewed P2-GD
runtime review.
