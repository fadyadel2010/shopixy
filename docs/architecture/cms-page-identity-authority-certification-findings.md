# PASS SF-CMS2 CMS Page Identity Authority Certification

Date: 2026-06-30

Scope: PointOfSale.Web CMS page-management runtime only, plus backend admin CMS APIs/services directly consumed by CMS page management and Storefront preview/read boundaries.

## Pages Reviewed

- `PointOfSale.Web/src/modules/storefront/cms/pages/CmsPagesList.jsx`
- `PointOfSale.Web/src/modules/storefront/cms/pages/CmsPageEditor.jsx`
- `PointOfSale.Web/src/modules/storefront/cms/services/cmsService.js`
- `PointOfSale.Web/src/modules/storefront/cms/cmsPageIdentityAuthority.js`
- `PointOfSale.Web/src/modules/storefront/cms/store/cmsStore.js`

## Backend Endpoints Reviewed

- `GET /api/admin/pages`
- `GET /api/admin/pages/{id}`
- `GET /api/admin/cms/pages/by-slug/{slug}`
- `POST /api/admin/pages`
- `PUT /api/admin/pages/{id}`
- `POST /api/admin/pages/{id}/publish`
- `GET /api/admin/pages/{id}/preview`
- `POST /api/admin/pages/{id}/preview-link`
- `DELETE /api/admin/pages/{id}`
- Storefront CMS public/preview read boundary in `CmsPageReadService`

## Backend Services Reviewed

- `AdminCmsPagesController`
- `AdminCmsService`
- `CmsPageService`
- `CmsPageReadService`
- `CmsPagePreviewTokenService`
- `CmsPageContentValidator`

## Findings

Identity findings: CMS Page Identity is backend-owned. PostgreSQL `CmsPages.PageGroupId` is the canonical page id exposed as `AdminCmsPageResponse.Id` and list-row `Id`. `CmsPageService.CreatePageAsync` mints `PageGroupId`; all read/mutation paths predicate trusted tenant id plus `PageGroupId`; slugs are normalized and tenant-scoped in backend service code. Frontend route ids, list rows, form state, and preview URLs are selectors or presentation only.

Admission findings: frontend DTO admission now requires current backend tenant evidence, valid backend page id, valid version record id, supported content type, canonical slug agreement between the page DTO and current draft/published version, expected route id agreement for detail/editor responses, and duplicate-free list rows. Backend list DTOs now include `TenantId` so page-list presentation is tenant-admitted, not cache-admitted.

Recovery findings: refresh, deep link, browser restart, another browser, back/forward, reconnect, Redis loss, Mongo rebuild, and projection rebuild converge through `GET /api/admin/pages` or `GET /api/admin/pages/{id}` with no-store headers and frontend DTO admission. Stale local page identity is cleared or rejected; public read recovery still recomposes from current SQL publication state.

Concurrency findings: update, publish, and delete remain exact-version mutations. Backend serializable transactions and `FOR UPDATE` lock current page groups; update creates a new immutable draft version; publish requires the current draft version; delete requires current editable version. Conflict responses provide latest backend DTO evidence, and the editor admits that latest DTO before presentation replacement.

Cache findings: React state, Zustand, browser cache/BFCache, LocalStorage, SessionStorage, Redis, Mongo, and Elasticsearch cannot become Page Identity Authority. Admin CMS identity reads now use no-store headers. Redis/Mongo/search remain derivative public visibility or projection surfaces and are filtered through current SQL authority on reviewed boundaries.

Cross-system findings: CMS page identity becomes visible outside CMS only after backend SQL acceptance and, for public storefront/search/SEO/navigation, after publication authority. Builder, Storefront, SEO, Navigation, Search, and Analytics consume current SQL or certified derivative projections only; unsaved CMS candidates and frontend DTOs do not distribute identity.

## Identity Pipeline

Route -> Backend Lookup -> Backend Page Resolution -> Tenant Validation -> Publication/Version Validation -> DTO Admission -> Presentation.

Identity begins at backend SQL `CmsPages.PageGroupId`. Authority begins in backend trusted tenant/session plus `CmsPageService` SQL resolution. DTO Admission begins in `deriveCurrentCmsPageIdentityPresentation` or `deriveCurrentCmsPageListPresentation` after backend response receipt. Presentation begins only after admitted DTOs set React state.

## Boundary Review

Identity Boundary: backend `CmsPages.PageGroupId`; recovery source is current SQL read; failure behavior is 404 or frontend admission rejection.

Tenant Boundary: trusted `ITenantContext.RequiredTenantId` plus DTO `TenantId`; recovery source is current backend session/read; failure behavior is 400/401/403 or frontend admission rejection.

Publication Boundary: backend draft/published flags, current version, `PublishedAt`, and preview-token version binding; recovery source is current SQL; failure behavior is hidden public page, 404, 409, or 428.

Recovery Boundary: admin no-store backend reads and SQL-first public reads; recovery source is PostgreSQL `CmsPages`; failure behavior is withheld presentation.

Presentation Boundary: React state after DTO admission; recovery source is backend DTO reread; failure behavior is error state, not fallback identity.

Cache Boundary: Redis/Mongo/search/browser caches are derivative only; recovery source is SQL recomposition or invalidation; failure behavior is stale suppression/tombstone/admission rejection.

## Runtime Hardening Performed

- Added `TenantId` to admin CMS page list DTOs and mapping.
- Added CMS page identity admission helpers for list/detail/editor DTOs.
- Required editor loads, save responses, publish responses, and conflict latest-version DTOs to pass current tenant and expected route page-id admission before presentation.
- Required list responses to pass current tenant, unique page id, unique slug, version, status, and content-type admission as one collection.
- Added no-store headers to CMS admin page list/detail/by-slug/preview reads.

## Exact Runtime Files Modified

- `PointOfSale.API/DTOs/Admin/AdminCmsDtos.cs`
- `PointOfSale.API/Services/Cms/CmsPageService.cs`
- `PointOfSale.Web/src/modules/storefront/cms/cmsPageIdentityAuthority.js`
- `PointOfSale.Web/src/modules/storefront/cms/pages/CmsPagesList.jsx`
- `PointOfSale.Web/src/modules/storefront/cms/pages/CmsPageEditor.jsx`
- `PointOfSale.Web/src/modules/storefront/cms/services/cmsService.js`

## Regression Freezes Added

- `PointOfSale.Web/src/modules/storefront/cms/cmsPageIdentityAuthority.test.ts`
- `PointOfSale.Web/src/modules/storefront/cms/services/cmsService.test.ts`
- `PointOfSale.Tests/Architecture/CmsPageIdentityAuthorityCertificationTests.cs`

## Architecture Documentation Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Explicit Certification Answers

- Did you review ONLY PointOfSale.Web runtime? Yes, limited to PointOfSale.Web CMS runtime and backend CMS APIs/services directly consumed by CMS page management plus CMS preview/public read boundaries.
- Which frontend files were modified? `cmsPageIdentityAuthority.js`, `CmsPagesList.jsx`, `CmsPageEditor.jsx`, `cmsService.js`, plus frontend regression tests.
- Which backend files were modified? `AdminCmsDtos.cs` and `CmsPageService.cs`.
- Who owns Page Identity? Backend PostgreSQL `CmsPages.PageGroupId`.
- Who owns Slug? Backend CMS service normalization and tenant-scoped SQL uniqueness.
- Who owns Page Key? Backend canonical `PageGroupId` plus current version/slug lineage.
- Who owns Canonical Identity? Backend SQL `CmsPages` page group, tenant, slug, lifecycle, and version state.
- Who owns Publication State? Backend SQL `IsDraft`, `IsPublished`, `PublishedAt`, version, and delete state.
- Can frontend create Page Identity? No.
- Can React become Page Identity Authority? No.
- Can LocalStorage become Page Identity Authority? No.
- Can Redis become Page Identity Authority? No.
- Can stale Page survive recovery? No inspected path; recovery re-enters no-store backend reads and SQL authority.
- Can replay fabricate Page Identity? No.
- Can URL become Page Authority? No, URL page id is a lookup selector only.
- Can Search become Page Authority? No, search is derivative and filtered through current SQL authority.
- Where does Page Identity begin? Backend SQL `CmsPages.PageGroupId` minted by `CmsPageService.CreatePageAsync`.
- Where does Authority begin? Backend trusted tenant/session plus `CmsPageService` SQL page-group resolution.
- Where does DTO Admission begin? Frontend `deriveCurrentCmsPageIdentityPresentation` / `deriveCurrentCmsPageListPresentation`.
- Where does Presentation begin? React state after admitted backend DTOs.
- Were runtime changes required? Yes.
- Is CMS Page Identity production-certifiable? Yes, CMS Page Identity is production-certifiable.
