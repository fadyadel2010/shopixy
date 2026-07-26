# PASS SF-CMS6 CMS Preview Authority Certification

Scope: PointOfSale.Web CMS preview runtime only, plus directly consumed backend CMS preview-link, preview-token, draft-resolution, public preview rendering, and Storefront preview boundaries.

Pages reviewed: `CmsPageEditor.jsx`, `CmsPagesList.jsx`, `cmsService.js`, `cmsStore.js`, `cmsPageIdentityAuthority.js`, and public preview URL construction.

Backend endpoints reviewed: `GET /api/admin/pages/{id}/preview`, `POST /api/admin/pages/{id}/preview-link`, and `GET /api/storefront/pages/{slug}?preview=true&previewToken=...`.

Backend services reviewed: `AdminCmsPagesController`, `AdminCmsService`, `CmsPageService`, `CmsPageReadService`, `CmsPagePreviewTokenService`, and `StorefrontPreviewService` only at the shared preview/config boundary.

Finding: CMS Preview is production-certifiable after public preview token hardening. Frontend submits Preview Intent and opens a backend-issued URL only. Backend owns preview token creation, tenant binding, token validation, token lifetime, exact page/version binding, draft/snapshot resolution, SEO/content DTO composition, no-store public preview rendering, recovery, and failure behavior.

Closed runtime gap: public CMS preview rendering previously allowed authenticated Owner/Admin/Manager requests with `preview=true` to bypass preview-token validation and fall back to current draft-by-slug resolution. Runtime now requires a backend-issued preview token for public preview rendering, validates tenant/slug/version before SQL preview reads, resolves only the token-bound page/version, returns 403 for missing/invalid/expired/foreign tokens, and never substitutes current draft or live content when token admission fails.

Preview pipeline: Preview Intent begins in `CmsPageEditor.handlePreview` or `CmsPagesList.handlePreview`; Preview Authority begins in `AdminCmsPagesController.CreateCmsPagePreviewLink`; Token Validation begins in `StorefrontPagesController.TryValidatePreviewToken`; Draft/Snapshot Resolution begins in `CmsPageReadService.GetPreviewPageByIdAsync`; Presentation begins after the public Storefront receives the admitted preview DTO with `X-Preview-Mode` and no-store headers.

Regression freezes added: `PointOfSale.Tests/Architecture/CmsPreviewAuthorityCertificationTests.cs` and `PointOfSale.Web/src/modules/storefront/cms/services/cmsService.test.ts`.

Runtime files modified: `PointOfSale.API/Controllers/StorefrontPagesController.cs`, `PointOfSale.API/Controllers/AdminCmsPagesController.cs`, `PointOfSale.Web/src/modules/storefront/cms/services/cmsService.js`, `PointOfSale.Web/src/modules/storefront/cms/services/cmsService.test.ts`, and `PointOfSale.Tests/Architecture/CmsPreviewAuthorityCertificationTests.cs`.
