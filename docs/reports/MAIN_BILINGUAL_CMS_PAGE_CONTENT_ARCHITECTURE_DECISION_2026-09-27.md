# MAIN Bilingual CMS Page Content Architecture Decision - 2026-09-27

## 1. Status And Verdict

**Status: PROPOSED, DESIGN ONLY, NOT IMPLEMENTED.**

**Decision:** model Arabic and English CMS page content as normalized `CmsPageTranslation` rows owned by each immutable `CmsPage` version row.

The logical page remains `CmsPage.PageGroupId`. `CmsPage.Id` remains the immutable version-row identity. A translation row must never become the page identity, Navigation identity, or an independently publishable aggregate.

The initial locale policy proposed by this decision is:

| Concern | Proposed decision |
|---|---|
| Supported locales | `en`, `ar` |
| Default and canonical locale | `en` |
| Publication requirement | Complete English translation required; Arabic optional |
| Fallback | Whole requested locale falls back to English; no field-by-field language mixing |
| Slug | One shared lowercase ASCII slug per logical page version |
| Concurrency | One page-wide `expectedVersion` across all translations |
| Publish/rollback/delete | One atomic page-group operation across all translations |
| Preview token | Remains tenant + page group + version + shared slug bound; locale is a read selection |
| Navigation identity | Remains `PageGroupId`; labels remain Navigation-owned `Label`/`LabelAr` |

This report adds no bilingual runtime capability and makes no claim that Arabic CMS content currently exists.

## 2. Scope

This decision covers tenant Storefront CMS Pages in the MAIN Windows repository:

- PostgreSQL write and public-read authority.
- Admin DTOs and authoring semantics.
- Draft, publish, preview, history, rollback, and delete behavior.
- Public locale resolution and fallback.
- Shared slug and route behavior.
- Navigation and Dedicated Footer dependencies.
- MongoDB projection, Elasticsearch search, SEO, and sitemap implications.
- Expand, backfill, rollout, compatibility, and rollback strategy.
- Required automated and browser verification.

This decision does not cover Marketplace CMS implementation, Homepage sections, product/category translation storage, Navigation publication ownership, Dedicated Footer publication ownership, or theme localization.

## 3. Non-Negotiable Existing Invariants

The following certified contracts must survive implementation:

- `CmsPage.PageGroupId` identifies a logical page.
- `CmsPage.Id` identifies one immutable page-version row.
- Navigation and tenant Footer CMS references use `PageGroupId`, not a version row or locale row.
- Supported lifecycle states remain Draft, Published, and UnpublishedChanges.
- Save creates a new immutable version; it does not rewrite history.
- Publish is backend-owned, version-preconditioned, serializable, and group locked.
- Rollback resolves a backend-owned historical snapshot and appends a new published version.
- Delete is page-group scoped and fails closed in public Navigation, Footer, search, and SEO surfaces.
- Preview remains a signed exact-version read and never makes draft content public.
- Tenant identity remains backend-derived and every query remains tenant constrained.
- PostgreSQL remains publication and public visibility authority.
- MongoDB, Elasticsearch, Redis, and browser state remain derivative or candidate state.
- `StoreConfig.RowVersion` remains Navigation concurrency authority.
- Header and Footer Navigation remain one atomic draft/live scope.

Evidence for these invariants is in `CmsPageService`, the CMS architecture certification tests, and the 2026-09-25 and 2026-09-27 reports listed in section 24.

## 4. Verified Current State

### 4.1 Persistence And Lifecycle

`PointOfSale.Core/Entities/CmsPage.cs` stores one `Slug`, `Title`, `Content`, `ContentType`, `SchemaVersion`, and SEO set on every version row. There is no locale discriminator and no related translation entity.

`PointOfSale.API/Services/Cms/CmsPageService.cs` currently:

- Creates `PageGroupId` once and version row `Id` per snapshot in `CreatePageAsync`.
- Selects the current draft before the published row in `GetCurrentEditablePage`.
- Checks `expectedVersion` against the current aggregate version in `EnsureExpectedVersion`.
- Opens serializable relational transactions in `BeginMutationTransactionAsync`.
- Uses PostgreSQL `FOR UPDATE` in `LoadActivePageGroupForMutationAsync` and `LoadPageGroupHistoryForMutationAsync`.
- Demotes the previous draft before inserting a replacement draft in `UpdatePageAsync`.
- Releases the old published slot before activating a draft in `PublishPageAsync`.
- Copies a historical snapshot into a new monotonic published row in `RollbackPageAsync`.
- Tombstones every active row in the page group in `DeletePageAsync`.
- Publishes aggregate-scoped events using `PageGroupId` and page version.

`PointOfSale.API/Services/Cms/CmsPageContentValidator.cs` validates one content value at a time. It sanitizes RichText and validates Blocks with the supported `text`, `image`, and `faq` shapes.

### 4.2 Admin Contract

`PointOfSale.API/DTOs/Admin/AdminCmsDtos.cs` exposes one locale-neutral request and one locale-neutral snapshot. `AdminCmsPageResponse` may contain a Draft and Published snapshot, but each snapshot contains only one title, content value, content type, schema version, and SEO set.

`PointOfSale.API/Controllers/AdminCmsPagesController.cs` requires Owner/Admin authorization and tenant context. Update accepts body `ExpectedVersion`; publish, rollback, and delete require `If-Match`. Missing mutation preconditions return `428`; stale aggregate versions return the latest backend snapshot in a `409` response.

### 4.3 Public Read And Preview

`PointOfSale.API/Controllers/StorefrontPagesController.cs` accepts slug and preview parameters but has no locale parameter. It does not resolve `Accept-Language`.

`PointOfSale.API/Services/Cms/CmsPageReadService.cs` selects one published row by tenant and shared slug, or one token-bound preview row by tenant, `PageGroupId`, and version. `CmsPageDto` and `CmsPageListItemDto` contain no locale evidence.

`PointOfSale.API/Services/Cms/CmsPagePreviewTokenService.cs` binds a 15-minute token to tenant, `PageGroupId`, shared slug, exact version, and expiration. That binding is compatible with locale selection because all locale snapshots would belong to the same exact version.

### 4.4 Storefront Locale Behavior

`PointOfSale.Storefront/src/services/api/httpClient.ts` already places the active `en` or `ar` value in both `Accept-Language` and the `lang` query parameter for API calls.

The CMS endpoint ignores both values. `PointOfSale.Storefront/src/components/cms/CmsPage.tsx` fetches by slug and does not include active language in its effect dependency list, so a language switch does not trigger a CMS content reload.

The Storefront router already accepts default, `/en`, and `/ar` route families. `PointOfSale.Storefront/src/i18n/languageStore.ts` owns the active UI language and document direction.

### 4.5 Navigation And Footer

Navigation stores an explicit English `Label` and optional `LabelAr`. `NavigationAdmissionService.ResolveCmsPage` resolves CMS targets by `PageGroupId`, verifies tenant ownership, and requires a published target before Navigation publication.

`StorefrontNavigationController.ResolveCmsNavigationLink` recomposes the current shared slug from a published page group. The public Storefront chooses `labelAr` when the active language is Arabic.

Dedicated Footer links also persist `CmsPageGroupId` and explicit localized labels. `StorefrontFooterReadService` resolves a page by stable group ID, uses the current published shared slug, and currently falls back to the single page title when a CMS link has no explicit label.

### 4.6 Projections, Search, SEO, And Sitemap

`CmsPageProjectionConsumer` recomposes a published page from current SQL authority and writes one locale-neutral `StorefrontPageDocument`. Draft content is not projected.

`CmsPageSearchDocument` already has `NameAr`, but `AdminSearchSyncController.MapToCmsPageSearchDocument` always assigns `NameAr = null`. The document has no Arabic description or Arabic SEO fields. Unified search boosts `nameAr` for Arabic requests, but tenant CMS cannot currently populate it.

The Storefront search controller re-enters SQL to prove current CMS publication visibility, but it does not replace CMS search text with localized SQL content.

`SeoService.BuildCmsSeoMetadataAsync` and `GetSitemapAsync` read current published SQL rows, but neither accepts locale. The sitemap emits one canonical `/pages/{slug}` URL and no evidence-based `hreflang` alternates.

`PointOfSale.Storefront/src/utils/sitemap.ts` contains a client utility that advertises English and Arabic for every CMS page. It is a placeholder and does not verify that an Arabic translation exists, so it must not be treated as publication authority.

### 4.7 Repository Localization Precedent

Marketplace CMS is a separate aggregate with embedded JSON fields: `TitleJson`, `ContentJson`, `MetaTitleJson`, and `MetaDescriptionJson`. `MarketplaceLocalization` resolves `en`/`ar` and falls back between values.

This proves that embedded locale maps and locale negotiation are technically familiar in this repository. It does not prove that the same shape is correct for tenant CMS because tenant CMS has stronger immutable history, tenant isolation, Navigation identity, SQL-authoritative SEO/sitemap, and page-group concurrency requirements.

### 4.8 EF Index Preflight Finding

`CmsPageConfiguration.cs` expresses intended names for separate active draft and published uniqueness indexes. The effective baseline migration and current model snapshot do not contain all of them.

The verified effective model contains:

- Unique filtered `IX_CmsPages_PageGroupId` for published, non-deleted rows.
- Unique filtered `UQ_CmsPages_Tenant_Published_Slug` for published, non-deleted rows.
- Non-unique tenant/draft/slug and tenant/published/slug lookup indexes.
- Unique `(PageGroupId, Version)`.

It does not contain the named `UQ_CmsPages_PageGroupId_Draft` or `UQ_CmsPages_Tenant_Draft_Slug` indexes. Repeated `HasIndex` calls over identical property sets have collapsed into one EF model index in the snapshot.

No runtime database was inspected for this design report. Before a localization migration is generated, implementation must compare the target database catalog, migration history, configuration, and snapshot and deliberately reconcile this drift. The bilingual migration must not silently encode an unverified index assumption.

## 5. Problem Statement

The existing row shape can represent only one language per immutable version. Reusing unrelated fields, storing Arabic in SEO fields, or creating one logical page per language would violate certified identity and lifecycle contracts.

A correct design must answer these questions as one system:

- How both locale snapshots belong to one immutable page version.
- How publication remains atomic and page-wide.
- How missing Arabic behaves without fabricating content.
- How stable Navigation and Footer identities survive localization.
- How public routes select a locale without changing logical identity.
- How search, SEO, sitemap, preview, history, rollback, and deletion remain coherent.
- How existing English data and old clients survive rollout and rollback.

## 6. Options Considered

| Criterion | Fixed/embedded locale fields on `CmsPage` | Embedded JSON locale maps | Normalized translation rows |
|---|---|---|---|
| Minimal read joins | Strong | Strong | Requires one bounded child load |
| Database length/type constraints | Repeated columns | Weak inside JSON | Strong per translation column |
| Supported-locale constraint | Schema-specific columns | Application-only | SQL check + application policy |
| Content validation | Duplicated field code | JSON parsing required | Existing validator per row |
| Immutable history alignment | Possible | Possible | Natural parent-version ownership |
| Missing-locale representation | Nullable columns | Missing JSON property | Absent child row |
| SQL search/SEO/sitemap queries | Wide and repetitive | JSON extraction | Relational and explicit |
| More locales later | Schema migration per locale | Flexible | Insert policy/config change |
| Partial-shape risk | Medium | High | Low |
| Tenant isolation enforcement | Parent row | Parent row | Parent + composite tenant FK |
| Repository precedent | Navigation/Product-style pairs | Marketplace CMS | Product/category translation patterns and relational authority conventions |
| Operational complexity | Low initially, high as fields grow | Low initially, high validation debt | Moderate and explicit |

### 6.1 Rejected: One `CmsPage` Group Per Locale

Creating separate English and Arabic page groups is rejected. It would split publish state, version history, rollback, delete, Navigation identity, and concurrency. A single logical page could expose mismatched locale versions and would require Navigation to choose a locale-specific identity.

### 6.2 Rejected: Fixed Arabic Columns

Adding `TitleAr`, `ContentAr`, and parallel SEO columns is workable for exactly two languages, but it duplicates every content and SEO rule on the immutable parent and makes future locale growth a table rewrite. It also leaves ambiguous whether `ContentType` and `SchemaVersion` are shared or duplicated.

### 6.3 Rejected: Embedded JSON As Tenant CMS Authority

The Marketplace precedent reduces joins, but JSON makes required-title, maximum-length, content-type, schema-version, and locale completeness constraints application-only. It complicates SQL-authoritative SEO, sitemap, backfill verification, and indexing. It also risks field-by-field fallback and malformed partial locale maps.

### 6.4 Selected: Normalized Translation Rows

Normalized rows preserve a small lifecycle parent while making each locale snapshot explicit, constrained, queryable, and attached to one immutable version. The additional join is bounded to at most two rows initially and occurs inside existing SQL-authoritative reads.

## 7. Proposed Relational Model

### 7.1 `CmsPages` After Contract Migration

The long-term parent row retains aggregate and route state:

| Column | Role |
|---|---|
| `Id` | Immutable version-row identity |
| `PageGroupId` | Stable logical page identity |
| `TenantId` | Tenant authority |
| `Slug` | Shared canonical route segment |
| `Version` | Monotonic page-group version |
| `IsDeleted` | Group tombstone state on active rows |
| `IsDraft` | Active draft slot |
| `IsPublished` | Active published slot |
| `PublishedAt` | Publication evidence |
| `CreatedAt`, `UpdatedAt` | Aggregate version timestamps |

The current localized columns remain temporarily during expand/compatibility rollout and are removed only after the rollback window:

- `Title`
- `Content`
- `ContentType`
- `SchemaVersion`
- `MetaTitle`
- `MetaDescription`
- `MetaKeywords`
- `OpenGraphImageUrl`

### 7.2 New `CmsPageTranslations`

| Column | Proposed type and rule |
|---|---|
| `CmsPageVersionId` | `uuid`, references `CmsPages.Id` |
| `TenantId` | `uuid`, server-derived, never client-selected |
| `Locale` | `varchar(10)`, normalized lowercase, initially `en` or `ar` |
| `Title` | `varchar(200)`, required |
| `Content` | `text`, required and normalized |
| `ContentType` | `varchar(20)`, `RichText` or `Blocks` |
| `SchemaVersion` | positive integer |
| `MetaTitle` | nullable `varchar(200)` |
| `MetaDescription` | nullable `varchar(320)` |
| `MetaKeywords` | nullable `varchar(500)` |
| `OpenGraphImageUrl` | nullable `varchar(1000)` |

Proposed constraints and indexes:

- Primary key `(CmsPageVersionId, Locale)`.
- Composite foreign key `(CmsPageVersionId, TenantId)` to a unique parent key `(CmsPages.Id, CmsPages.TenantId)`.
- Check `Locale IN ('en', 'ar')` for the initial release.
- Check `ContentType IN ('RichText', 'Blocks')`.
- Check `SchemaVersion > 0`.
- Tenant lookup index `(TenantId, Locale, CmsPageVersionId)`.
- Cascade only on physical parent purge; soft deletion preserves translation history.

`TenantId` is intentionally repeated as defense-in-depth for tenant filters and efficient direct translation queries. The composite foreign key prevents a translation from claiming a tenant different from its version parent.

### 7.3 Entity Relationships

The proposed ownership chain is:

```text
Tenant
  -> CmsPage PageGroupId (logical aggregate)
      -> CmsPage Id + Version (immutable snapshot)
          -> CmsPageTranslation[en]
          -> CmsPageTranslation[ar] (optional)
```

Neither locale row has draft, published, deleted, version, or route authority. Those states exist only on the parent version row.

## 8. Locale Completeness And Fallback

### 8.1 Normalization

- Accept `en`, `en-*`, `ar`, and `ar-*` at the edge.
- Normalize supported variants to `en` or `ar`.
- Reject unsupported locale keys in Admin mutation payloads.
- Use explicit `lang` first, then `Accept-Language`, then `en`.
- Move or generalize the existing locale resolver out of the Marketplace-specific namespace before tenant CMS depends on it.

### 8.2 Complete Translation

A translation is complete when it has a non-empty title and content that passes the existing validator for its own content type and schema version. SEO fields remain optional.

An Arabic row must be either absent or complete. Persisting an Arabic title with missing/invalid Arabic body is rejected instead of publishing a hybrid page.

### 8.3 Publication Admission

- English must exist and be complete before publication.
- Arabic may be absent.
- If Arabic is supplied, it must be complete before publication.
- Publication activates one parent version and therefore all translation rows on that version atomically.
- No locale can be independently published or rolled back.

### 8.4 Public Fallback

Fallback is whole-locale:

1. Resolve requested locale.
2. Select that locale row if complete.
3. Otherwise select the English row.
4. Return one row's title, body, content type, schema version, and textual SEO together.
5. Set `Content-Language` to the locale actually returned.

Within the selected row, `MetaTitle` may fall back to that same row's `Title`. Missing optional meta description or keywords remain absent. English SEO text must not be mixed into an otherwise Arabic response.

The public response should expose `requestedLanguage`, actual `language`, `fallbackApplied`, and `availableLanguages` so clients and SEO code do not infer fallback.

## 9. Slug And Route Decision

The first bilingual release keeps one shared lowercase ASCII slug. This matches `CmsPageReadService.NormalizeSlug`, preserves current tenant slug uniqueness, and avoids changing preview-token, Navigation, Footer, search cleanup, and canonical identity at the same time as content localization.

Proposed canonical routes:

| Locale | Canonical public route |
|---|---|
| English | `/pages/{slug}` |
| Arabic with complete translation | `/ar/pages/{slug}` |

Existing `/{slug}`, `/pages/{slug}`, `/en/{slug}`, `/en/pages/{slug}`, `/ar/{slug}`, and `/ar/pages/{slug}` aliases may continue to resolve during transition. SEO canonical tags should converge English aliases to `/pages/{slug}` and Arabic aliases to `/ar/pages/{slug}`.

If Arabic falls back to English, the response canonical is the English URL and no Arabic `hreflang` is advertised. This prevents fallback-only duplicate English content from being represented as an Arabic publication.

Localized slugs are deferred. If later approved, they require a separate route-alias entity keyed by `PageGroupId` and locale, redirect history, locale-scoped uniqueness, preview-token changes, Navigation route recomposition, and search/sitemap migration. They must not be implemented by making locale rows the page identity.

## 10. Versioning And Concurrency

The current page-wide concurrency model is retained.

- A save checks one `expectedVersion` against the current page aggregate.
- A successful save creates a new `CmsPage` version row and a complete snapshot of its English and optional Arabic rows.
- A save demotes the previous active draft but does not mutate its translation rows.
- Publishing uses the active draft parent version and activates all translations on it.
- Rollback copies all translations from the selected historical parent into one new published parent version.
- Delete tombstones the page group; child snapshots remain for retained history until physical purge.

Independent locale saves without page-wide conflicts are rejected for the initial design. They would permit English and Arabic editors to build different current version numbers and would make publish, rollback, preview, and history composition ambiguous.

The Admin UI may track dirty state per language tab for usability, but the backend mutation remains one aggregate replacement. A conflict response returns the latest full bilingual aggregate so the existing Load Latest and Retry My Draft behavior can be extended without client-invented merging.

## 11. Proposed Admin API Contract

The examples below are proposed contracts, not current API responses.

### 11.1 Create Or Update Request

```json
{
  "slug": "about-us",
  "translations": {
    "en": {
      "title": "About Us",
      "contentType": "RichText",
      "content": "<p>Our story.</p>",
      "schemaVersion": 1,
      "seo": {
        "metaTitle": "About Us",
        "metaDescription": "Learn about our store.",
        "metaKeywords": "about,store",
        "openGraphImageUrl": "/media/about-en.png"
      }
    },
    "ar": {
      "title": "من نحن",
      "contentType": "RichText",
      "content": "<p>قصتنا.</p>",
      "schemaVersion": 1,
      "seo": {
        "metaTitle": "من نحن",
        "metaDescription": "تعرف على متجرنا.",
        "metaKeywords": "من نحن,متجر",
        "openGraphImageUrl": "/media/about-ar.png"
      }
    }
  },
  "expectedVersion": 7
}
```

Create omits `expectedVersion`. Update requires it. `translations.en` is required. `translations.ar` is optional and may be explicitly `null` to remove Arabic from the new version.

### 11.2 Aggregate Response

```json
{
  "id": "page-group-guid",
  "tenantId": "tenant-guid",
  "slug": "about-us",
  "availableLanguages": ["en", "ar"],
  "hasPendingChanges": true,
  "draft": {
    "recordId": "version-row-guid",
    "version": 8,
    "createdAtUtc": "2026-09-27T12:00:00Z",
    "updatedAtUtc": "2026-09-27T12:00:00Z",
    "publishedAtUtc": null,
    "translations": {
      "en": {
        "title": "About Us",
        "contentType": "RichText",
        "content": "<p>Our story.</p>",
        "schemaVersion": 1,
        "seo": {}
      },
      "ar": {
        "title": "من نحن",
        "contentType": "RichText",
        "content": "<p>قصتنا.</p>",
        "schemaVersion": 1,
        "seo": {}
      }
    }
  },
  "published": null
}
```

Version history uses the same bilingual snapshot shape for every entry. Rollback continues to submit only target version, reason, and `If-Match`; the browser must not submit a reconstructed translation snapshot.

### 11.3 Compatibility Contract

During one explicit expand/rollback window:

- Responses continue returning the current top-level locale-neutral fields as English mirrors.
- New responses also return `translations`.
- A legacy request with no `translations` updates English and carries forward the current Arabic row into the new version.
- A new request with `translations` uses replacement semantics; explicit `ar: null` removes Arabic.
- New writes mirror English into legacy parent columns so an older application binary still serves coherent English.
- Conflict responses include both the English compatibility fields and the full translation map.

This compatibility is justified by persisted historical data, rolling deployment, and executable rollback. It is temporary and must have a measured removal gate.

## 12. Proposed Public API Contract

Request selection:

```http
GET /api/storefront/pages/about-us?lang=ar
Accept-Language: ar
X-Tenant-Id: <trusted resolved tenant>
```

Proposed response with Arabic available:

```json
{
  "slug": "about-us",
  "requestedLanguage": "ar",
  "language": "ar",
  "fallbackApplied": false,
  "availableLanguages": ["en", "ar"],
  "title": "من نحن",
  "contentType": "RichText",
  "content": "<p>قصتنا.</p>",
  "seo": {
    "metaTitle": "من نحن",
    "metaDescription": "تعرف على متجرنا.",
    "metaKeywords": "من نحن,متجر",
    "openGraphImageUrl": "/media/about-ar.png"
  },
  "updatedAtUtc": "2026-09-27T12:00:00Z",
  "publishedAtUtc": "2026-09-27T12:05:00Z"
}
```

Proposed fallback response evidence when Arabic is absent:

```json
{
  "slug": "about-us",
  "requestedLanguage": "ar",
  "language": "en",
  "fallbackApplied": true,
  "availableLanguages": ["en"],
  "title": "About Us",
  "contentType": "RichText",
  "content": "<p>Our story.</p>",
  "seo": {}
}
```

Response headers should include `Content-Language` and `Vary: Accept-Language`. Preview responses retain the existing private no-store policy and `X-Preview-Mode: true`.

The public list endpoint should accept the same locale selection and return the actual language/fallback evidence for each page. It must not claim Arabic availability solely because the Storefront supports Arabic UI chrome.

## 13. Preview, History, Rollback, And Delete

### 13.1 Preview

The token format does not need a locale claim. It already proves the exact tenant, logical page, shared slug, and page version. Locale selects one child snapshot under that authorized version.

The Admin preview link may include the editor's active content locale in the URL for convenience. Changing between `en` and `ar` with the same token remains constrained to the same version.

### 13.2 History

Every version-history entry returns the complete translation map stored under that parent row. The history UI should show locale availability and permit inspecting either locale without altering the source snapshot.

### 13.3 Rollback

Rollback remains append-only and page-wide. Rolling back to v3 creates a new current version containing exactly the English and Arabic rows that existed on v3. It must not combine English from v3 with Arabic from another version.

### 13.4 Delete

Delete remains page-group scoped. One delete event is sufficient because consumers recompose or remove the entire logical page. Translation rows do not emit independent delete events.

## 14. Navigation And Dedicated Footer

No Navigation identity or publication schema change is required.

- CMS target `TargetId` remains `PageGroupId`.
- Backend route recomposition continues to use the current published shared slug.
- Navigation `Label` and `LabelAr` remain explicit Navigation-owned presentation fields.
- CMS title changes do not silently rewrite explicitly authored Navigation labels.
- Admin target options may add English and Arabic title fields for selector presentation.
- Derived Footer labels may use the corresponding published page title only when no explicit localized Footer label exists.
- Public route builders add the active locale prefix at render time; localized paths are not persisted into Navigation.
- Missing or deleted page groups continue to fail closed regardless of locale.

## 15. Projection And Event Design

Existing publish and delete events already carry tenant, `PageGroupId`, page version, slug, event version, and idempotency evidence. Consumers re-enter SQL, so locale payloads do not need to be copied onto the event wire.

This preserves rolling compatibility and avoids making an event a second content authority.

`CmsPageProjectionConsumer` should load all translation rows for the current SQL-published parent and denormalize them into one Mongo document. The document may use a locale map because Mongo is derivative. Public page rendering must continue to use PostgreSQL until separately certified otherwise.

Freshness fences remain page-group/version scoped, not locale scoped.

## 16. Search Design

Elasticsearch should retain one document per logical published page, not one document per locale.

Proposed document fields:

| Field | Source |
|---|---|
| `PageId` | `PageGroupId` |
| `Slug` | Shared published slug |
| `Name` | English title |
| `NameAr` | Arabic title when present |
| `Description` | English description/body excerpt |
| `DescriptionAr` | Arabic description/body excerpt when present |
| `MetaDescription` | English meta description |
| `MetaDescriptionAr` | Arabic meta description when present |
| `SearchText` | English searchable values |
| `SearchTextAr` | Arabic searchable values |
| `Version` | Published aggregate version |

The preferred stable document ID is `{TenantId}_{PageGroupId}` rather than `{TenantId}_{Slug}`. This aligns derivative identity with the certified aggregate and avoids slug-rename identity churn. Stale cleanup should compare authoritative page-group IDs.

Search query language changes boosts and result presentation, not visibility. Current SQL publication and tenant activity must still be rechecked. Arabic query results use Arabic fields when present and fall back to English result text when absent.

A full tenant CMS reindex is required after bilingual projection deployment. Reindex completion is an activation gate, not publication authority.

## 17. SEO And Sitemap Design

### 17.1 SEO Metadata

`SeoService.GetSeoMetadataAsync` must accept normalized locale and select the same published translation/fallback result as the page endpoint. SEO and body reads must not implement separate fallback rules.

For a complete Arabic translation:

- Canonical URL is `/ar/pages/{slug}`.
- `Content-Language` is `ar`.
- English and Arabic alternates are emitted.
- `x-default` points to English `/pages/{slug}`.

For Arabic fallback to English:

- Canonical URL is English `/pages/{slug}`.
- `Content-Language` is `en`.
- No Arabic alternate is emitted.
- Preview remains `noindex, nofollow`.

### 17.2 Sitemap

The SQL-authoritative sitemap should emit:

- One English CMS URL for every published page with complete English.
- One Arabic CMS URL only when that same published version has complete Arabic.
- `hreflang="en"`, `hreflang="ar"`, and `x-default` alternates only from verified translation availability.
- Parent version `UpdatedAt` as `lastmod`.

The placeholder Storefront sitemap utility must not advertise Arabic for every CMS page without SQL evidence.

## 18. Frontend Authoring And Rendering

### 18.1 Admin Editor

The editor should retain one page form with language tabs or a side-by-side translation switcher. It should not create separate routes or separate page IDs per locale.

Required behavior:

- Shared slug is visibly outside locale tabs.
- English is marked required.
- Arabic is marked optional until product policy changes.
- Each locale owns title, content type, content, schema version, and SEO controls.
- Locale completion is visible before publish confirmation.
- Dirty indicators may be locale-specific, but Save submits one aggregate.
- Existing in-flight response protection must preserve newer edits in either locale.
- Conflict Load Latest and Retry My Draft operate on the full aggregate.
- History and rollback remain one page-wide workflow.
- Preview opens the selected locale while retaining exact-version authorization.
- RichText sanitization and Blocks editing run independently for each locale value.

### 18.2 Public Storefront

`CmsPageView` must subscribe to active language and reload when it changes. The active language must be included in the request identity so an English response cannot remain displayed after an Arabic switch.

Rendering continues to consume one resolved public DTO. The component should not implement field-by-field fallback. It should use backend `language` and `fallbackApplied` evidence for document language, canonical tags, and diagnostics.

RichText remains sanitized in the browser as defense-in-depth. Arabic blocks use the existing RTL document direction; content alignment values remain authored data.

## 19. Migration And Rollout

### Phase 0: Reconcile Current Authority

- Inspect the target PostgreSQL catalog and migration history.
- Reconcile effective draft/published uniqueness with `CmsPageConfiguration` and the model snapshot.
- Add a relational concurrency test that proves one active draft and one active published row per page group and active slug policy.
- Do not combine an unverified index repair with translation backfill blindly.

### Phase 1: Expand

- Add `CmsPageTranslations` and constraints.
- Keep all current localized columns.
- Add English translation rows for every historical `CmsPage` row, including inactive and deleted history retained by policy.
- Copy title, content, content type, schema version, and SEO exactly.
- Do not fabricate Arabic rows.
- Verify one English child row per parent and zero tenant mismatches.

### Phase 2: Compatibility Read And Dual Write

- Read translations first when enabled, with legacy English fallback only during transition.
- Write normalized rows for every new version.
- Mirror English into legacy parent columns.
- Preserve existing Arabic when a legacy client updates only English.
- Keep public behavior English-identical before localized reads are activated.

### Phase 3: Admin Contract And Authoring

- Add translation maps to DTO admission and conflict responses.
- Deploy bilingual editor controls.
- Keep page-wide `expectedVersion` and existing mutation endpoints.
- Validate both locales on the backend.

### Phase 4: Public Reads And Derivatives

- Activate locale-aware public page/list/preview and SEO reads.
- Update Storefront language reload behavior.
- Update Mongo projection and Elasticsearch mapping.
- Reindex CMS search documents.
- Emit evidence-based localized sitemap entries.

### Phase 5: Contract

- Measure that no supported client sends legacy-only shapes.
- Stop legacy reads while continuing temporary English mirrors through the rollback window.
- After backup and rollback approval, remove legacy localized parent columns and compatibility DTO fields.
- Regenerate and inspect the EF snapshot; do not accept unexplained index changes.

## 20. Rollback Strategy

Before legacy columns are removed, rollback is operationally safe:

- Disable locale-aware reads.
- Roll back application binaries.
- Continue serving mirrored English from legacy columns.
- Leave translation rows intact; Arabic authoring data remains preserved but temporarily unavailable.
- Rebuild the previous Elasticsearch CMS index if required.

After legacy columns are removed, binary rollback requires a forward repair migration that restores parent English fields from `CmsPageTranslations`. Therefore column removal must occur only after the agreed rollback window and verified backups.

Dropping the translation table is not an acceptable routine rollback after Arabic authoring begins because it would destroy newly authored data.

## 21. Verification And Acceptance Criteria

### 21.1 Migration

- Every pre-existing version row has exactly one `en` translation with byte-equivalent normalized content and equivalent SEO.
- No Arabic row is generated from English data.
- Composite tenant foreign keys reject mismatched tenant ownership.
- Duplicate `(CmsPageVersionId, Locale)` rows are rejected.
- Unsupported locales are rejected.
- Effective draft/published indexes are catalog-verified and represented accurately in the EF snapshot.

### 21.2 Lifecycle

- English-only create, save, preview, publish, history, rollback, and delete preserve current behavior.
- Bilingual save creates one parent version with both locale snapshots.
- Arabic-only publication is rejected under the proposed policy.
- A supplied incomplete Arabic translation is rejected before persistence.
- Saving either locale advances one aggregate version.
- A stale English or Arabic editor receives the same page-wide `409` contract.
- Rollback restores all and only translations from the selected source version.
- Failed concurrent operations create no phantom parent or translation rows.

### 21.3 Tenant And Security

- Admin tenant identity remains `ITenantContext.RequiredTenantId` derived.
- Public tenant resolution remains trusted-host derived.
- Cross-tenant page, locale, preview, search, SEO, sitemap, Navigation, and Footer reads fail closed.
- Preview tokens cannot authorize another tenant, page group, slug, or version.
- RichText and URLs are validated independently for both locales.

### 21.4 Public Locale Behavior

- `lang` takes precedence over `Accept-Language`; unsupported values normalize to English under the agreed edge policy.
- Complete Arabic returns Arabic title, body, SEO, `Content-Language: ar`, and no English text mixing.
- Missing Arabic returns the complete English snapshot with explicit fallback evidence.
- Language switching reloads CMS content without a full page refresh.
- Public list responses report actual availability and fallback correctly.
- Preview can switch locale without drifting from the token-bound version.

### 21.5 Navigation And Footer

- Existing `PageGroupId` references survive bilingual saves, publication, and slug changes.
- Explicit Navigation and Footer labels remain unchanged by CMS title edits.
- Missing explicit Arabic derived labels use the published Arabic title only when available, otherwise the existing English behavior.
- Draft-only, deleted, wrong-tenant, and unavailable targets remain suppressed or publication-blocking exactly as certified.

### 21.6 Search, SEO, And Sitemap

- Arabic title/body queries can discover a bilingual page.
- English queries retain current discovery behavior.
- Search visibility still re-enters current SQL publication authority.
- Search document identity is stable across slug rename if the proposed `PageGroupId` ID is adopted.
- SEO metadata uses the same locale/fallback resolver as body reads.
- Arabic sitemap entries and `hreflang` exist only for complete Arabic translations.
- Fallback-only Arabic routes canonicalize to English and are not advertised as Arabic content.

### 21.7 Frontend Regression

- Existing delayed-save and delayed-publish protection retains newer edits across both locale tabs.
- Existing DTO tenant/page/version admission extends to translation maps and rejects duplicate or unsupported locale keys.
- Existing Page list, editor, preview, history, rollback, deletion, Navigation, Footer, and RTL/mobile tests remain green.
- Browser certification covers English desktop, Arabic desktop, Arabic mobile, missing-Arabic fallback, exact-version preview, and cleanup.

## 22. Risks And Mitigations

| Risk | Mitigation |
|---|---|
| Old client overwrites Arabic | Legacy-only mutation carries current Arabic forward; page-wide precondition still applies |
| Mixed-language public page | Whole-locale fallback only |
| Aggregate conflict frequency | Keep clear locale dirty indicators and return latest full aggregate; do not split publication authority |
| Translation child tenant leak | Composite tenant FK, tenant filters, parent join, cross-tenant tests |
| Migration loses history | Backfill every retained version, not only active rows; reconcile counts before activation |
| Projection/search serves stale locale text | SQL remains body/visibility authority; freshness fences and full reindex |
| Duplicate SEO URLs | Shared slug, explicit canonical policy, evidence-based `hreflang` |
| Rolling deployment cannot read new data | Temporary English mirrors and additive DTOs |
| EF index drift compounds | Catalog/configuration/snapshot reconciliation is Phase 0 |
| Marketplace model is copied without tenant guarantees | Treat Marketplace JSON only as locale-resolution precedent, not tenant CMS persistence authority |

## 23. Product Approval Gates

The architecture recommendation is complete, but implementation should not start until product ownership approves these visible behaviors:

1. English is the default and canonical locale.
2. Arabic is optional for publication in the first release.
3. Missing Arabic publicly falls back to the complete English page.
4. Shared slugs remain non-localized.
5. English and Arabic save under one page-wide optimistic-concurrency token.
6. Fallback-only Arabic routes canonicalize to English and are omitted from Arabic sitemap alternates.
7. Explicit Navigation/Footer labels remain independent from page titles.

If both locales must be mandatory, slugs must be localized, or locale editors must save independently, this ADR must be revised before implementation because those choices materially change publication admission, routing, identity, and concurrency.

## 24. Evidence Inventory

Primary source evidence:

- `PointOfSale.Core/Entities/CmsPage.cs`
- `PointOfSale.Infrastructure/Data/Configurations/CmsPageConfiguration.cs`
- `PointOfSale.Infrastructure/Migrations/20260723214642_Baseline_Recovery3.cs`
- `PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- `PointOfSale.API/DTOs/Admin/AdminCmsDtos.cs`
- `PointOfSale.API/DTOs/Storefront/CmsPageDtos.cs`
- `PointOfSale.API/Controllers/AdminCmsPagesController.cs`
- `PointOfSale.API/Controllers/StorefrontPagesController.cs`
- `PointOfSale.API/Services/Cms/CmsPageService.cs`
- `PointOfSale.API/Services/Cms/CmsPageReadService.cs`
- `PointOfSale.API/Services/Cms/CmsPagePreviewTokenService.cs`
- `PointOfSale.API/Services/Cms/CmsPageContentValidator.cs`
- `PointOfSale.API/Services/Admin/NavigationAdmissionService.cs`
- `PointOfSale.API/Controllers/StorefrontNavigationController.cs`
- `PointOfSale.API/Services/Storefront/StorefrontFooterReadService.cs`
- `PointOfSale.API/Services/Storefront/SeoService.cs`
- `PointOfSale.Core/Events/CacheInvalidationEvents.cs`
- `PointOfSale.Infrastructure/Consumers/CmsPageProjectionConsumer.cs`
- `PointOfSale.Infrastructure/MongoDB/Documents/StorefrontPageDocument.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Documents/CmsPageSearchDocument.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/UnifiedSearchIndexService.cs`
- `PointOfSale.API/Controllers/AdminSearchSyncController.cs`
- `PointOfSale.API/Controllers/StorefrontSearchController.cs`
- `PointOfSale.Core/Entities/MarketplaceCmsPage.cs`
- `PointOfSale.Infrastructure/Data/Configurations/MarketplaceCmsPageConfiguration.cs`
- `PointOfSale.API/Localization/MarketplaceLocalization.cs`
- `PointOfSale.API/Services/MarketplaceCms/MarketplaceCmsPageReadService.cs`
- `PointOfSale.Web/src/modules/storefront/cms/pages/CmsPageEditor.jsx`
- `PointOfSale.Web/src/modules/storefront/cms/cmsPageIdentityAuthority.js`
- `PointOfSale.Web/src/modules/storefront/cms/services/cmsService.js`
- `PointOfSale.Storefront/src/components/cms/CmsPage.tsx`
- `PointOfSale.Storefront/src/services/api/httpClient.ts`
- `PointOfSale.Storefront/src/services/api/storefrontApi.ts`
- `PointOfSale.Storefront/src/app/router.tsx`
- `PointOfSale.Storefront/src/i18n/languageStore.ts`
- `PointOfSale.Storefront/src/utils/sitemap.ts`

Certification and regression evidence:

- `PointOfSale.Tests/Cms/CmsPageWorkflowTests.cs`
- `PointOfSale.Tests/Cms/CmsPagePreviewTokenServiceTests.cs`
- `PointOfSale.Tests/Cms/CmsPageContentValidatorTests.cs`
- `PointOfSale.Tests/Architecture/CmsPageIdentityAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/CmsTenantIsolationCertificationTests.cs`
- `PointOfSale.Tests/Architecture/CmsPreviewAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/CmsPublishAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/CmsVersionHistoryRollbackAuthorityCertificationTests.cs`
- `docs/reports/MAIN_CMS_NAVIGATION_SIMPLIFICATION_FINAL_CERTIFICATION_2026-09-27.md`
- `docs/reports/MAIN_CMS_NAVIGATION_SIMPLIFICATION_WORKING_NOTES_2026-09-27.md`
- `docs/reports/MAIN_STOREFRONT_CMS_PAGES_FINAL_CERTIFICATION_2026-09-25.md`
- `docs/reports/MAIN_STOREFRONT_NAVIGATION_FINAL_CERTIFICATION_2026-09-25.md`
- `docs/reports/MAIN_STOREFRONT_AUTHORING_INTEGRATED_FINAL_CERTIFICATION_2026-09-25.md`

## 25. Final Decision

Use normalized translation rows per immutable CMS page version. Keep identity, publication, concurrency, preview, history, rollback, and deletion page-wide. Keep one shared slug. Backfill existing data as English only. Make Arabic optional but complete when supplied. Resolve locale at the public edge and fall back as a whole page, with explicit response and SEO evidence.

This is the smallest architecture that provides real bilingual content without weakening the certified page-group authority model or introducing a second publication system.
