# MAIN Bilingual CMS Page Content Implementation Final Certification - 2026-09-27

## 1. Verdict

**Bilingual tenant CMS page content implementation: PASS.**

The MAIN Windows Development implementation now supports English and Arabic CMS page snapshots across persistence, Admin authoring, immutable lifecycle operations, public reads, preview, Storefront rendering, SEO, sitemap generation, MongoDB projection, and Elasticsearch indexing.

The implementation follows the decisions in `MAIN_BILINGUAL_CMS_PAGE_CONTENT_ARCHITECTURE_DECISION_2026-09-27.md`:

- English (`en`) is required, default, and canonical.
- Arabic (`ar`) is optional but must be complete when present.
- Missing Arabic falls back to the complete English page, never field by field.
- One lowercase ASCII slug is shared by both languages.
- `CmsPage.PageGroupId` remains the logical page identity.
- `CmsPage.Id` remains an immutable version-row identity.
- One aggregate-wide version precondition governs all translations.
- Publish, rollback, and delete operate across the complete bilingual snapshot.
- PostgreSQL remains publication and public-read authority.

This report supersedes the architecture report's earlier design-only implementation status. It does not alter the architecture decision as a historical record.

## 2. Persistence And Migration

The implementation adds normalized `CmsPageTranslation` rows owned by immutable `CmsPage` version rows.

Each translation persists:

- `CmsPageVersionId`
- server-derived `TenantId`
- normalized `Locale`
- `Title`
- `Content`
- `ContentType`
- `SchemaVersion`
- localized SEO fields

Database enforcement includes:

- Primary key `(CmsPageVersionId, Locale)`.
- Composite tenant-safe foreign key `(CmsPageVersionId, TenantId)`.
- Locale check restricted to `en` and `ar`.
- Content type and positive schema-version checks.
- Tenant/locale/version lookup index.
- Cascade only for physical parent deletion; soft-deleted immutable history remains retained.

The migration `20260927210427_BilingualCmsPageTranslations` also reconciles the effective CMS index model:

- Active draft uniqueness by `PageGroupId`.
- Active published uniqueness by `PageGroupId`.
- Active draft uniqueness by tenant and slug.
- Active published uniqueness by tenant and slug.
- Non-unique `PageGroupId` lookup.
- Existing unique `(PageGroupId, Version)` identity.

Legacy parent content columns remain as English compatibility mirrors for this expand/rollback window.

## 3. Migration Validation

The migration was first exercised against a disposable PostgreSQL database and then applied to MAIN Development database `PosEcommerceDb_Baseline`.

The migration backfill:

- Created one English translation for every existing CMS version.
- Included published, draft, historical, and deleted versions.
- Preserved all content, content type, schema version, and SEO values losslessly.
- Created no fabricated Arabic translations.
- Failed closed through transactional SQL assertions if counts or values differed.

The disposable validation database was dropped after verification.

The final read-only MAIN database audit, after the authenticated runtime exercise, reported:

| Evidence | Result |
|---|---:|
| CMS version rows | 51 |
| Translation rows | 52 |
| English rows | 51 |
| Arabic rows | 1 |
| Covered CMS versions | 51 |
| Parent/English mirror mismatches | 0 |
| Active draft duplicate sets | 0 |
| Active published duplicate sets | 0 |
| Active draft slug duplicate sets | 0 |
| Active published slug duplicate sets | 0 |
| Applied migrations | 39 |
| Latest migration | `20260927210427_BilingualCmsPageTranslations` |

The single Arabic row belongs to the soft-deleted runtime certification page. Its retention is expected immutable-history behavior. The page is no longer active, publicly readable, or present in the sitemap.

## 4. Admin Lifecycle

Admin page requests and responses now expose `translations.en` and optional `translations.ar` snapshots while retaining top-level English compatibility fields.

Mutation semantics are:

- New and updated pages require a complete English translation.
- Complete Arabic input creates or replaces Arabic for the new immutable version.
- Omitting `ar` preserves the current Arabic snapshot.
- Explicit `"ar": null` removes Arabic from the new snapshot.
- Unsupported and duplicate normalized locale keys fail validation.
- Existing aggregate-wide `expectedVersion` and `If-Match` contracts remain unchanged.
- Publish validates all translations before making the version public.
- Rollback appends an exact bilingual historical snapshot.
- Delete tombstones the complete page group and all locale visibility atomically.
- Conflict and history responses carry complete bilingual evidence.

The Admin editor now provides English and Arabic tabs, shared-slug editing, locale-scoped RichText/Blocks and SEO editing, explicit Arabic removal, aggregate validation, conflict handling, and version-history compatibility.

Admin response admission rejects malformed or partial translation snapshots before presenting them to the editor.

## 5. Public Reads And Storefront

`StorefrontLanguageResolver` is the language-selection authority for tenant CMS endpoints:

- Explicit `lang` has precedence over `Accept-Language`.
- Weighted `Accept-Language` values are honored.
- Regional values normalize to `en` or `ar`.
- Unsupported or absent values normalize to English.
- `q=0` values are not selected.

Public reads load current published content from SQL and return:

- `requestedLanguage`
- actual `language`
- `fallbackApplied`
- `availableLanguages`
- locale-correct `canonicalPath`
- evidence-based `alternatePaths`
- localized content and SEO

When Arabic is missing, the complete English translation is returned with English canonical evidence. No false Arabic alternate is emitted.

Responses emit `Vary: Accept-Language`. A page response emits the actual `Content-Language`. A list response emits `Content-Language` only when every returned page has the same actual language.

The Storefront explicitly requests the active locale, reloads CMS content when language changes, renders from the actual returned locale and direction, and installs canonical/hreflang tags from backend evidence rather than assumptions.

## 6. SEO And Sitemap

CMS SEO and sitemap generation remain SQL-authoritative.

Implemented behavior includes:

- Localized title, description, keywords, OpenGraph image, and structured-data evidence.
- English canonical URL for English and Arabic-fallback responses.
- Arabic canonical URL only when a complete Arabic translation exists.
- `en` and `x-default` alternates for every published page.
- `ar` alternate only when Arabic exists.
- Sitemap XML XHTML alternate links derived from the same evidence.
- Deleted pages excluded from public SEO and sitemap output.

## 7. Projection And Search

MongoDB publication projection now stores the complete `en`/`ar` translation map while retaining English compatibility mirrors. Draft content remains excluded.

Elasticsearch CMS documents now use stable ID `{TenantId}_{PageGroupId}` instead of slug identity and include:

- English and Arabic names.
- English and Arabic descriptions.
- English and Arabic meta descriptions.
- English and Arabic aggregate search text.

Indexing deletes obsolete legacy slug-keyed documents after a successful stable-ID upsert. Full tenant CMS reindex loads translation rows from SQL and removes tenant-scoped documents whose `PageGroupId` is absent from the current authoritative published set.

## 8. Authenticated Runtime Evidence

The live MAIN Development API and AhramStore tenant were exercised with an Owner account. Credentials, bearer tokens, and cookies were not written to repository files or included in this report.

The temporary bilingual lifecycle produced:

| Step | Result |
|---|---|
| POS login | `200`, Owner authority |
| Create bilingual draft | `201`, languages `ar,en`, version 1 |
| Publish aggregate | `200`, languages `ar,en`, version 1 |
| Explicit English read against Arabic preference | `200`, requested `en`, actual `en`, no fallback |
| Explicit Arabic read against English preference | `200`, requested `ar`, actual `ar`, no fallback |
| English `Content-Language` | `en` |
| Arabic `Content-Language` | `ar` |
| English canonical | `/pages/{slug}` |
| Arabic canonical | `/ar/pages/{slug}` |
| Arabic SEO | `200`, actual `ar`, Arabic canonical, `en/x-default/ar` alternates |
| Sitemap | `200`, English and Arabic page URLs plus XHTML alternate namespace |
| Delete | `204` |
| Deleted public page read | `404` |
| Deleted page in sitemap | absent |

Existing English-only pages were also verified:

- Explicit and weighted Arabic requests reported requested `ar`, actual `en`, and fallback `true`.
- Canonical and SEO evidence remained English.
- No false `/ar/pages/...` sitemap entries were emitted.

After the final controller build, list endpoint checks reported:

- Explicit Arabic request: `200`, requested `ar`, actual `en`, `Content-Language: en`.
- Weighted Arabic request: `200`, requested `ar`, actual `en`, `Content-Language: en`.
- Explicit English request: `200`, requested `en`, actual `en`, `Content-Language: en`.
- Every case emitted `Vary: Accept-Language`.

## 9. Automated Verification

| Gate | Result |
|---|---|
| API production compilation | PASS: 0 errors |
| Admin production Vite build | PASS |
| Storefront TypeScript and production Vite build | PASS |
| Backend CMS test namespace | PASS: 104/104 |
| Admin CMS focused Vitest | PASS: 4 files, 27/27 |
| Language resolver weighting/precedence tests | PASS |
| Bilingual lifecycle/read/fallback/rollback tests | PASS |
| Admin bilingual response-admission tests | PASS |
| Scope-specific `git diff --check` | PASS; line-ending notices only |
| Disposable migration validation | PASS |
| MAIN post-migration read-only audit | PASS |
| Authenticated live API round trip | PASS |

The builds retain existing bundle-size, dynamic-import, and Browserslist notices. An isolated API output attempt also encountered low disk space while copying bundled Playwright assets; that temporary output was removed. The normal API build subsequently completed successfully with `0 warnings` and `0 errors` after the live API was stopped.

## 10. Security And Isolation

- Tenant IDs are server-authoritative and constrained on every CMS query.
- Translation rows repeat tenant identity and enforce it through a composite foreign key.
- Admin mutation authorization remains Owner/Admin only.
- Preview remains bound to tenant, page group, exact version, and shared slug.
- SQL remains public visibility authority; MongoDB and Elasticsearch are derivative.
- No credentials, cookies, or bearer tokens were persisted in source or report artifacts.
- Unrelated dirty and untracked repository work was preserved.

## 11. Files In Scope

Principal implementation files include:

- `PointOfSale.Core/Entities/CmsPage.cs`
- `PointOfSale.Core/Entities/CmsPageTranslation.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
- `PointOfSale.Infrastructure/Data/Configurations/CmsPageConfiguration.cs`
- `PointOfSale.Infrastructure/Data/Configurations/CmsPageTranslationConfiguration.cs`
- `PointOfSale.Infrastructure/Migrations/20260927210427_BilingualCmsPageTranslations.cs`
- `PointOfSale.Infrastructure/Migrations/20260927210427_BilingualCmsPageTranslations.Designer.cs`
- `PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- `PointOfSale.API/DTOs/Admin/AdminCmsDtos.cs`
- `PointOfSale.API/DTOs/Storefront/CmsPageDtos.cs`
- `PointOfSale.API/DTOs/Storefront/SeoDtos.cs`
- `PointOfSale.API/Localization/StorefrontLanguageResolver.cs`
- `PointOfSale.API/Services/Cms/CmsPageService.cs`
- `PointOfSale.API/Services/Cms/CmsPageReadService.cs`
- `PointOfSale.API/Services/Storefront/SeoService.cs`
- `PointOfSale.API/Controllers/StorefrontPagesController.cs`
- `PointOfSale.API/Controllers/SeoController.cs`
- `PointOfSale.API/Controllers/SitemapController.cs`
- `PointOfSale.API/Controllers/AdminSearchSyncController.cs`
- `PointOfSale.Infrastructure/Consumers/CmsPageProjectionConsumer.cs`
- `PointOfSale.Infrastructure/MongoDB/Documents/StorefrontPageDocument.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Documents/CmsPageSearchDocument.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/IUnifiedSearchIndexService.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/UnifiedSearchIndexService.cs`
- `PointOfSale.Storefront/src/components/cms/CmsPage.tsx`
- `PointOfSale.Storefront/src/services/api/storefrontApi.ts`
- `PointOfSale.Storefront/src/types/cms.ts`
- `PointOfSale.Web/src/modules/storefront/cms/cmsPageIdentityAuthority.js`
- `PointOfSale.Web/src/modules/storefront/cms/cmsPageIdentityAuthority.test.ts`
- `PointOfSale.Web/src/modules/storefront/cms/pages/CmsPageEditor.jsx`
- `PointOfSale.Web/src/modules/storefront/cms/pages/CmsPageEditor.test.tsx`
- `PointOfSale.Tests/Cms/CmsPageWorkflowTests.cs`
- `PointOfSale.Tests/Cms/StorefrontLanguageResolverTests.cs`

## 12. Residual Risk

No blocking bilingual CMS defect was found.

The remaining verification gap is browser-driven visual interaction across the complete Admin and Storefront journey. Browser automation tooling was not available in this session, so UI confidence comes from production builds, focused component tests, live HTTP behavior, and the authenticated API lifecycle. Mobile/RTL visual behavior should remain part of routine release smoke testing.

The Development machine also had approximately 1.6 GB free during verification. This did not block normal builds, tests, migration work, or runtime certification, but repository artifact growth should be monitored.
