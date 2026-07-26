# PASS SF-CMS3 CMS Content Authority Certification Findings

Date: 2026-06-30

Scope reviewed: PointOfSale.Web CMS content editing runtime only, plus backend APIs/services directly consumed by that editor.

## Pages Reviewed

- `PointOfSale.Web/src/modules/storefront/cms/pages/CmsPageEditor.jsx`
- `PointOfSale.Web/src/modules/storefront/cms/pages/CmsPagesList.jsx`
- `PointOfSale.Web/src/modules/storefront/cms/cmsPageIdentityAuthority.js`
- `PointOfSale.Web/src/modules/storefront/cms/services/cmsService.js`
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

## Backend Services Reviewed

- `AdminCmsPagesController`
- `AdminCmsService`
- `CmsPageService`
- `CmsPageContentValidator`
- `CmsPageReadService`
- `CmsPageProjectionConsumer`
- `SafeRichTextSanitizer`

## Findings

CMS Content is production-certifiable after runtime hardening. Frontend CMS edits are temporary candidates only. Accepted content begins only after backend tenant/session authorization, content validation, canonicalization, sanitization, expected-version admission, SQL persistence, and accepted DTO admission.

Closed runtime gap: `Blocks` content accepted unknown kebab-case block/widget types and persisted raw JSON `data`. Image blocks and SEO OpenGraph image URLs were trimmed without backend media URL validation. This is now closed by explicit backend block allowlists, canonical supported block rewrites, unknown-field stripping by construction, safe URL validation, and regression freezes.

## Content Pipeline

Content Candidate begins in `CmsPageEditor` form, rich text editor, and block editor state.

Mutation DTO begins in `toRequestPayload(form)` and `cmsService.createPage/updatePage/publishPage`.

Backend Validation begins in `AdminCmsPagesController` and `CmsPageContentValidator`.

Canonicalization begins in `CmsPageContentValidator` and `CmsPageService` slug/title/SEO normalization.

Sanitization begins in `SafeRichTextSanitizer.Sanitize` inside backend content validation.

RowVersion/Version admission begins at `ExpectedVersion` and `If-Match` controller/service checks.

SQL Persistence begins in `CmsPageService` after serializable mutation and `FOR UPDATE` page-group loading.

Accepted DTO begins when backend maps current `CmsPages` evidence to `AdminCmsPageResponse`.

Presentation begins only after `deriveCurrentCmsPageIdentityPresentation` admits tenant/page/version evidence.

## Boundary Summary

Content Boundary: backend `CmsPageContentValidator`; recovery source is PostgreSQL `CmsPages`; invalid shape fails before persistence.

Widget Boundary: backend block allowlist; recovery source is SQL accepted block JSON; unknown widget JSON fails closed.

HTML Boundary: backend `SafeRichTextSanitizer`; recovery source is SQL sanitized content; scripts and unsafe attributes are stripped.

Media Boundary: backend safe URL validation for image blocks and SEO media; recovery source is SQL accepted URLs; unsafe schemes/credential URLs fail closed.

Recovery Boundary: backend page reads plus frontend DTO admission; recovery source is PostgreSQL; stale browser/cache state is discarded.

Presentation Boundary: `deriveCurrentCmsPageIdentityPresentation`; recovery source is accepted backend DTO; malformed/foreign/stale DTOs fail closed.

Concurrency Boundary: `ExpectedVersion`, `If-Match`, serializable transactions, `FOR UPDATE`; stale saves/publishes conflict and return latest backend evidence for admission.

## Certification Answers

- CMS Content, Rich Text, HTML, Widgets/Blocks, Sections where modeled, Media References, Sanitization, and Published Content are backend-owned.
- Frontend cannot create accepted content; it can only submit candidate DTOs.
- React, LocalStorage, Redis, Mongo, search, and browser caches cannot become Content Authority.
- Stale content cannot survive recovery because reads and conflicts re-enter SQL and frontend admission.
- Replay cannot fabricate accepted content because expected version and backend schema admission are mandatory.
- Injected HTML cannot become authority without backend sanitizer admission; injected script does not survive.
- Widget JSON cannot become authority because unknown block types are rejected and supported blocks are canonicalized.

## Runtime Hardening Performed

- Rejected unknown CMS block/widget types.
- Added backend block count and FAQ item bounds.
- Canonicalized supported `text`, `image`, and `faq` block data.
- Validated text alignment.
- Validated image block URLs and SEO OpenGraph URLs as safe relative or http(s) URLs without credentials.
- Added backend workflow and architecture regression freezes.

## Files Modified

- `PointOfSale.API/Services/Cms/CmsPageContentValidator.cs`
- `PointOfSale.API/Services/Cms/CmsPageService.cs`
- `PointOfSale.Tests/Cms/CmsPageContentValidatorTests.cs`
- `PointOfSale.Tests/Cms/CmsPageWorkflowTests.cs`
- `PointOfSale.Tests/Architecture/CmsContentAuthorityCertificationTests.cs`
- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Regression Freezes Added

- `CmsPageContentValidatorTests` covers unsafe rich text, unknown widget JSON, unsafe image URLs, and canonical supported block output.
- `CmsPageWorkflowTests` covers unsafe SEO media rejection before SQL persistence.
- `CmsContentAuthorityCertificationTests` freezes backend content authority, frontend candidate-only presentation, and authority documentation updates.
