# CMS Version History & Rollback Authority Certification Findings

PASS SF-CMS7 CMS Version History & Rollback Authority Certification.

PointOfSale.Web CMS Version History & Rollback is production-certifiable after runtime hardening.

Reviewed scope:
- Frontend: `CmsPageEditor.jsx`, `CmsPagesList.jsx`, `cmsService.js`, `cmsPageIdentityAuthority.js`, CMS service/admission tests.
- Backend: `AdminCmsPagesController`, `IAdminCmsService`, `AdminCmsService`, `CmsPageService`, `CmsPageReadService`, CMS DTOs, CMS workflow tests, CMS publish/preview projection boundaries.
- Shared projection/read boundaries: `StorefrontCmsPagePublishedEvent`, `CmsPageProjectionConsumer`, Redis invalidation, SQL public CMS reads, SEO/sitemap/search/navigation visibility by current SQL authority.

Findings:
- Version History begins at `GET /api/admin/pages/{id}/versions`; it is a no-store backend read over PostgreSQL `CmsPages` version rows.
- Rollback begins as frontend rollback intent containing only target version and optional reason.
- Rollback Authority begins at `AdminCmsPagesController.RollbackCmsPage` after tenant/session authority, Owner/Admin authorization, request validation, and mandatory `If-Match`.
- Snapshot Resolution and SQL Transaction begin in `CmsPageService.RollbackPageAsync` under serializable transaction and page-group `FOR UPDATE`.
- Rollback creates a new monotonic published CMS version from the backend-resolved historical SQL snapshot; it does not let the browser submit a snapshot or published state.
- Projection begins after `StorefrontCmsPagePublishedEvent`, SQL commit, Redis invalidation, and projection consumers re-enter SQL with freshness/event fences.
- Presentation begins only after version-history DTO admission or rollback response DTO admission.

Boundary decisions:
- Version Boundary owner: backend SQL `CmsPages`; recovery source: SQL; failure behavior: 404/withhold presentation.
- Rollback Boundary owner: backend controller/service; recovery source: SQL plus `If-Match`; failure behavior: 428/409/404/400 fail closed.
- Snapshot Boundary owner: backend SQL snapshot resolution; recovery source: historical `CmsPages`; failure behavior: missing versions fail closed.
- Projection Boundary owner: backend event/projection pipeline; recovery source: SQL recomposition; failure behavior: derivative caches are invalidated/rebuilt.
- Recovery Boundary owner: backend SQL reads; recovery source: PostgreSQL; failure behavior: stale browser/projection/cache evidence cannot advance authority.
- Presentation Boundary owner: frontend rendering only after accepted DTO admission; recovery source: backend reread; failure behavior: withhold/error.
- Concurrency Boundary owner: backend `If-Match` and SQL transaction; recovery source: latest backend DTO; failure behavior: conflict response.
- Cache Boundary owner: backend SQL; recovery source: SQL recomposition; failure behavior: Redis/Mongo/search/browser caches remain derivative.

Runtime hardening performed:
- Added CMS version history DTOs and rollback request DTO.
- Added no-store CMS version history admin API.
- Added CMS rollback admin API requiring `If-Match`.
- Added backend CMS version-history read service and SQL rollback transaction.
- Added frontend CMS version-history service/admission/UI.
- Added frontend rollback intent submission and accepted rollback DTO admission.
- Added regression freezes for backend workflow, frontend service/admission, and architecture context.

Production certification:
- CMS Version History is backend-owned.
- CMS Rollback is backend-owned.
- Frontend submits rollback intent only.
- React, LocalStorage, Redis, Mongo, Search, stale DTOs, replay, duplicate rollback, stale `If-Match`, and projection rebuilds cannot become authority.
