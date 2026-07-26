# PASS P2-GB Storefront Content Authority

Date: 2026-06-09

Status: production-certifiable for the inspected runtime after hardening.

## Authority Decision

Tenant-owned PostgreSQL `CmsPages` is the authority for storefront page identity, drafts,
publication, visibility, page content, builder blocks, and page SEO metadata. MongoDB, Redis,
Elasticsearch, events, preview links, frontend builder state, DTOs, and route values are derivative
or capability evidence only.

The current CMS stores one language-neutral page body and SEO set per version. It has no separate
localized-page variants, scheduler, restore endpoint, or standalone unpublish endpoint. Those
absent capabilities cannot create shadow authority. Adding them requires renewed P2-GB review.

## Findings

### Content Identity

`PageGroupId` is immutable page identity; `(PageGroupId, Version)` is SQL-unique. Slugs are
normalized, tenant-scoped, and checked against active draft/published groups. Admin route ids and
DTO slugs cannot cross tenant predicates.

### Drafts

Drafts remain SQL-only. Public page, list, SEO, sitemap, search, and footer consumers require
`IsPublished && !IsDraft`, non-deleted state, `PublishedAt`, and an active tenant. Each save now
creates a new immutable version and retires the prior draft from active state.

### Publication And Visibility

Publish is Owner/Admin-only, tenant-derived, serialized, and bound to the exact reviewed draft
version. It stages the projection event before the SQL commit. Delete withdraws active draft/live
state and stages a tombstone event when live content existed. Delayed publish/delete requests
cannot operate on a newer version.

### Localized Content

There are no independently persisted Arabic/English CMS variants. Language-dependent footer labels
are presentation fields, and CMS links are filtered through current SQL publication authority
before localization. Locale cannot expose a draft or unpublished page.

### SEO Content

CMS title, description, keywords, image, canonical URL, sitemap membership, and search visibility
derive from the same current published SQL page. Cached SEO is revalidated against SQL; canonical
origins remain governed by P2-GA.

### Builder Authority

Rich-text and block-builder payloads are validated and normalized before SQL persistence. Browser
builder state is not read by public consumers. Unsaved state cannot publish, and publish requires
the exact persisted draft version.

### Caching And Projections

Public page reads bypass Redis and MongoDB. Mongo projection replay reloads current SQL, verifies
active tenant lineage, uses a durable event fence and freshness filter, and fails closed to a
tombstone when SQL no longer proves visibility. Elasticsearch search results and footer cache hits
are filtered through current SQL publication state.

### Updates, Consumption, And Tenant Isolation

Only trusted-tenant Owner/Admin APIs mutate CMS pages. Public readers use trusted storefront tenant
resolution. SQL queries predicate tenant ownership, and preview tokens bind tenant, page group,
slug, and exact version. Foreign ids, slugs, headers, and routes do not grant foreign content.

### Concurrency And Historical Consistency

Update, publish, and delete require the current version. Relational mutations use a serializable
transaction; PostgreSQL locks the active page group with `FOR UPDATE`. Prior versions remain inert
history with neither draft nor published authority. Stale edits, publish replay, stale delete, and
preview-token drift cannot overwrite or expose newer truth.

### Adversarial Review

The inspected runtime blocks draft leakage without an authenticated admin or version-bound preview
capability, published-content spoofing, cache/projection poisoning as authority, stale
resurrection, cross-tenant access, locale bypass, SEO spoofing, publish replay, builder-state
publication, DTO/route tenant tampering, and lost-update races.

## Runtime Hardening Performed

- Added expected-version preconditions to CMS edit, publish, and delete operations.
- Serialized relational CMS mutations and locked PostgreSQL page groups.
- Changed draft edits from in-place overwrite to immutable version creation.
- Bound preview links to the exact page version granted.
- Updated the CMS admin UI to send current version authority on mutations.

## Exact Runtime Files Modified

- `PointOfSale.API/Controllers/AdminCmsPagesController.cs`
- `PointOfSale.API/Controllers/StorefrontPagesController.cs`
- `PointOfSale.API/DTOs/Admin/AdminCmsDtos.cs`
- `PointOfSale.API/Services/Admin/AdminCmsService.cs`
- `PointOfSale.API/Services/Admin/IAdminCmsService.cs`
- `PointOfSale.API/Services/Cms/CmsPagePreviewTokenService.cs`
- `PointOfSale.API/Services/Cms/CmsPageReadService.cs`
- `PointOfSale.API/Services/Cms/CmsPageService.cs`
- `PointOfSale.Web/src/modules/storefront/cms/pages/CmsPageEditor.jsx`
- `PointOfSale.Web/src/modules/storefront/cms/pages/CmsPagesList.jsx`
- `PointOfSale.Web/src/modules/storefront/cms/services/cmsService.js`

## Regression Freezes

- `PointOfSale.Tests/Architecture/CmsPublicVisibilityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/StorefrontContentAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Cms/CmsPagePreviewTokenServiceTests.cs`
- `PointOfSale.Tests/Cms/CmsPageWorkflowTests.cs`
- `PointOfSale.Tests/Cms/CmsPublicationReliabilityTests.cs`

## Explicit Answers

- Who owns storefront content authority? Tenant-owned PostgreSQL `CmsPages`.
- Can draft content leak publicly? No, except through an authorized, short-lived, exact-version
  preview capability.
- Can unpublished pages become visible? No.
- Can localized content bypass publication authority? No; no independent localized CMS authority
  exists, and localized presentation remains publication-filtered.
- Can SEO content diverge from page authority? No.
- Can builder state become authority? No.
- Can caches become authority? No.
- Can stale content resurrect? No inspected path permits it.
- Can tenant leakage occur? No inspected path permits it.
- Can replay overwrite content truth? No.
- Were runtime code changes required? Yes.
- Is Storefront Content Authority production-certifiable? Yes, for the inspected runtime.
