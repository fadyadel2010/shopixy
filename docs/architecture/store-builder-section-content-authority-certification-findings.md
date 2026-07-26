# PASS SF-ADM2.6 Builder Section Content Authority Certification

Date: 2026-06-30

Scope reviewed: PointOfSale.Web Store Builder Section Content runtime only, plus backend Store Config APIs/services directly consumed by Builder content editing.

## Pages Reviewed

- `PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx`
- `PointOfSale.Web/src/modules/storefront/components/builder/BuilderCanvas.jsx`
- `PointOfSale.Web/src/modules/storefront/components/builder/SettingsPanel.jsx`
- `PointOfSale.Web/src/modules/storefront/components/builder/SchemaSettingsForm.jsx`
- `PointOfSale.Web/src/modules/storefront/components/builder/PreviewModal.jsx`
- `PointOfSale.Web/src/modules/storefront/store/builderStore.js`
- `PointOfSale.Web/src/modules/storefront/builder/sectionRegistry.js`
- Built-in Builder section editors/previews: HeroBanner, ProductGrid, CategoryGrid, SubcategoryGrid, TextBlock, ImageBlock, VideoBlock, NewsletterSignup, Testimonials, FeaturedProducts.

## Backend Endpoints Reviewed

- `GET /api/admin/store-config`
- `PUT /api/admin/store-config/pages/{pageId}`
- `POST /api/admin/store-config/pages/{pageId}/publish`
- `GET /api/storefront/config/preview`
- `GET /api/storefront/store-config`

## Backend Services Reviewed

- `AdminStoreConfigController`
- `AdminStoreConfigService`
- `StorefrontStoreConfigController`
- `StorefrontPreviewService` by direct Builder preview consumption only
- `SafeRichTextSanitizer`

## Findings

Section Content Authority begins at backend page-layout content canonicalization in `AdminStoreConfigService.UpdatePageLayoutAsync`, after trusted tenant/session authority, Owner/Admin authorization, mandatory `If-Match` RowVersion admission, section type/key allowlists, content normalization, SQL persistence, and accepted `AdminStoreConfigResponse` emission.

Runtime hardening performed for SF-ADM2.6:

- Backend section settings now canonicalize content values before persistence.
- Text fields are trimmed, length-bounded, and passed through backend sanitization.
- Rich text/HTML-like fields are sanitized with `SafeRichTextSanitizer`.
- Image/video/link fields accept only safe absolute HTTP(S) or relative URLs.
- Product/category ID arrays are bounded, deduplicated, and string-normalized.
- Image, testimonial, spec, and review arrays are converted to bounded backend-owned shapes, stripping hidden or unknown nested properties.
- Existing section type/key allowlists, section ID/order canonicalization, mandatory RowVersion, SQL persistence, accepted DTO admission, and frontend backend-response hydration remain in force.

Content findings: Hero title/subtitle/buttons/images, product/collection references, gallery images, video URLs, rich text-like text fields, FAQ-style list content where represented, testimonials, footer/header-like page sections where represented, and section settings remain accepted only after backend canonicalization and accepted DTO admission.

Rich Text findings: Frontend text editors are candidate-only. Accepted rich text and HTML-like content are backend-owned through `SafeRichTextSanitizer` and SQL persistence. Failed or stale saves cannot promote frontend rich text to accepted content.

Validation findings: Backend owns max lengths, allowed section types, allowed setting keys, URL safety, allowed layout enums, grid numeric bounds, nested list shape, sanitization, and unknown-property stripping.

Failure findings: 400/401/403/404/409/422/428/500, malformed content, missing RowVersion, stale RowVersion, invalid section arrays, unsupported section types, and malformed accepted responses fail closed before accepted content replacement.

Recovery findings: Refresh, workspace reload, reconnect, server restart, Redis loss, and Mongo rebuild converge through SQL `StoreConfigs` plus backend DTO admission. Stale frontend content is cleared or remains dirty candidate state only.

Concurrency findings: Multiple tabs, another admin, autosave, replay, retry, duplicate save, and duplicate publish are governed by EF RowVersion and mandatory `If-Match`; stale content cannot overwrite newer backend truth.

Cache findings: React state, Zustand, undo/redo, browser cache/BFCache, LocalStorage, and SessionStorage cannot become Content Authority. Builder accepted presentation hydrates from accepted backend DTOs after bootstrap/save.

Cross-system findings: Candidate content is not published, projected, or previewed as accepted content before backend acceptance. Builder preview first saves through backend authority and then reads authenticated draft preview data; it does not fall back to live content on save failure.

## Content Pipeline

- Content begins: backend SQL `StoreConfigs.CustomizationJson` plus admin candidate edits after workspace admission.
- Content Authority begins: `AdminStoreConfigService.UpdatePageLayoutAsync` content canonicalization and RowVersion-guarded SQL persistence.
- Editing begins: `StorefrontBuilderPage` admits backend workspace DTO and hydrates Builder store sections.
- Mutation begins: `persistPageLayout` submits candidate sections to `PUT /api/admin/store-config/pages/{pageId}` with `If-Match`.
- Accepted Content begins: backend validates/canonicalizes/sanitizes, persists to SQL, emits `AdminStoreConfigResponse`, and frontend admits that DTO.
- Presentation begins: Builder canvas/preview render accepted DTO sections or unsaved candidate previews, never accepted truth from React/Zustand alone.

## Boundary Review

- Content Boundary: backend `NormalizeBuilderPageComposition` / `NormalizeSectionSettings`; recovery source SQL `StoreConfigs`; malformed content returns 422.
- Rich Text Boundary: backend `SafeRichTextSanitizer`; recovery source SQL accepted DTO; unsafe HTML is stripped/escaped before acceptance.
- HTML Boundary: accepted HTML-like values are sanitizer output only; unsafe tags/protocols fail closed by sanitization.
- Mutation Boundary: `AdminStoreConfigController.UpdatePageLayout`; recovery source current RowVersion; missing/stale preconditions return 428/409.
- Recovery Boundary: `GET /api/admin/store-config`; recovery source current SQL row; malformed reads withhold Builder workspace.
- Presentation Boundary: admitted backend DTO hydration through `admitWorkspaceConfig`/`admitSections`; failures keep dirty candidate or withhold workspace.
- Concurrency Boundary: EF `StoreConfig.RowVersion`; duplicate/stale saves conflict.
- Validation Boundary: backend section type/key/value canonicalization; unknown properties are stripped.

## Explicit Certification Answers

- Did you review ONLY PointOfSale.Web runtime? Yes, with backend Store Config APIs/services only where directly consumed by Builder content editing.
- Which frontend files were modified? None.
- Which backend files were modified? `PointOfSale.API/Services/Admin/AdminStoreConfigService.cs`.
- Who owns Section Content? Backend Store Config authority.
- Who owns Rich Text? Backend Store Config authority plus backend sanitizer.
- Who owns HTML? Backend Store Config authority plus backend sanitizer.
- Who owns Images? Backend Store Config authority after safe URL/list canonicalization; media upload pipeline was out of scope.
- Who owns Videos? Backend Store Config authority after safe URL canonicalization.
- Who owns Content Validation? Backend.
- Can frontend create accepted content? No.
- Can React become Content Authority? No.
- Can Zustand become Content Authority? No.
- Can LocalStorage become Content Authority? No.
- Can frontend persist accepted content? No; it submits mutation intent only.
- Can stale content survive recovery? No; recovery converges to backend SQL or withholds workspace.
- Can replay duplicate content mutations? Replay can submit intent, but RowVersion prevents stale accepted overwrite.
- Can duplicate Save overwrite newer content? No, EF RowVersion and `If-Match` reject stale saves.
- Where does Content begin? Backend SQL accepted content, with frontend only creating temporary candidates after admission.
- Where does Content Authority begin? Backend page-layout mutation validation/canonicalization.
- Where does Editing begin? Frontend Builder after backend workspace admission.
- Where does Mutation begin? `persistPageLayout` candidate submission with backend RowVersion.
- Where does Accepted Content begin? Backend canonicalization, sanitization, SQL persistence, and accepted DTO emission.
- Where does Presentation begin? Builder rendering after accepted DTO admission.
- Were runtime changes required? Yes, backend content canonicalization hardening.
- Is Builder Section Content production-certifiable? Yes.

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/StoreBuilderSectionContentAuthorityCertificationTests.cs`

## Architecture Documentation Updates

- `docs/architecture/store-builder-section-content-authority-certification-findings.md`

## AI Context Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
