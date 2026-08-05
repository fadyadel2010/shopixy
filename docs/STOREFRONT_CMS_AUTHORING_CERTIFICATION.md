# Storefront CMS Authoring Certification

**Repository:** PointOfSale
**Date:** 2026-08-03
**Scope:** Storefront CMS Authoring only
**Mode:** Source-backed certification; no application code changed
**Final verdict:** **FAIL**

## Executive Summary

Storefront CMS Authoring is not certifiable in its current state.

The subsystem has several sound local implementations: tenant CMS pages use immutable versions and transactional publication, StoreConfig layouts use PostgreSQL `xmin`, `If-Match`, backend admission, and transactional outbox staging, and tenant Navigation adopts the rotated StoreConfig RowVersion after saves. Those local strengths do not form a coherent authoring system.

The release-blocking defect is source-proven: the shipped Marketplace CMS frontend never sends the version required by backend update, publish, or delete commands. Existing pages cannot be updated, newly created drafts cannot be published, and pages cannot be deleted through that authoring UI (`PointOfSale.Web/src/super-admin/marketplace-cms/marketplaceCmsService.js:19-35`; `PointOfSale.API/SuperAdmin/Controllers/SuperAdminMarketplaceCmsPagesController.cs:151-182,224-271`; `PointOfSale.API/Services/MarketplaceCms/MarketplaceCmsPageService.cs:168-186,247-265,308-320`).

The certification also verifies systemic P1 failures:

- Homepage authoring has no optimistic concurrency contract despite exposing a version in its DTO, and modern bulk save overlaps legacy per-section mutation (`PointOfSale.Web/src/super-admin/homepage/homepageService.ts:8-23,151-220`; `PointOfSale.API/SuperAdmin/Services/SuperAdminHomepageSectionManagementService.cs:175-224,257-305,398-519`).
- Tenant CMS seed is non-atomic, non-idempotent, and deterministically returns HTTP 500 for a tenant without `StoreConfig` after already committing seeded pages (`PointOfSale.API/Controllers/AdminCmsPagesController.cs:546-617`; `PointOfSale.API/Services/Admin/AdminCmsService.cs:291-318`).
- Tenant, Marketplace, and global footer services commit SQL before staging their authoring event, so a request can return 500 after persistence and leave derivative state stale (`PointOfSale.API/Services/Admin/AdminStorefrontFooterService.cs:161-168`; `PointOfSale.API/SuperAdmin/Services/SuperAdminMarketplaceFooterService.cs:154-160`; `PointOfSale.API/SuperAdmin/Services/SuperAdminStorefrontFooterService.cs:131-138`).
- Marketplace and global footers have no effective stale-write protection. Tenant footer's timestamp check is not a database concurrency token and is not locked across compare-and-replace (`PointOfSale.API/Services/Admin/AdminStorefrontFooterService.cs:63-70,100-161`; `PointOfSale.API/SuperAdmin/Services/SuperAdminMarketplaceFooterService.cs:62-160`; `PointOfSale.API/SuperAdmin/Services/SuperAdminStorefrontFooterService.cs:49-138`).
- Tenant and Marketplace CMS editors publish and preview persisted state while ignoring unsaved form edits (`PointOfSale.Web/src/modules/storefront/cms/pages/CmsPageEditor.jsx:149-220,319-340`; `PointOfSale.Web/src/super-admin/marketplace-cms/MarketplaceCmsPageEditor.jsx:135-203`).
- Navigation category authoring emits a category GUID when the tree DTO supplies no slug, while backend admission accepts only a generated live-category slug (`PointOfSale.Web/src/modules/storefront/cms/pages/NavigationEditor.jsx:125-158,763-770`; `PointOfSale.API/Controllers/CategoriesController.cs:84-115,424-430`; `PointOfSale.API/Services/Admin/AdminCmsService.cs:461-473,785-790`).
- Media is authored as URL text across CMS, Homepage, footer, and layouts. No audited authoring path validates asset existence, tenant ownership, deletion, or publication state.

### Scorecard

| Dimension | Result | Basis |
|---|---|---|
| Authoring workflow availability | Fail | Marketplace CMS update/publish/delete frontend contract is nonfunctional. |
| Draft integrity | Fail | Dirty state is absent or can be overwritten on CMS, Marketplace CMS, Builder focus refresh, and footer load/refresh paths. |
| Concurrency | Fail | Strong only for StoreConfig/layout and mostly for tenant CMS/Navigation; absent or ineffective for Homepage and global/Marketplace footer. |
| Validation | Fail | Strong local validators coexist with unsafe Homepage URL admission, reference gaps, category contract mismatch, and forked validation rules. |
| Publication atomicity | Fail | Footer truth and event staging are separate commits; seed has no operation-wide transaction. |
| Seed safety | Fail | Tenant seed has a verified 500 root cause and partial persistence; Marketplace startup seed is partial and raceable. |
| Error mapping | Fail | Expected authoring failures reach 500; missing preconditions are inconsistently mapped. |
| Projection handoff | Fail | Most publication events are sound, but footer handoff is non-atomic and StoreConfig rollback can change Navigation without its projection event. |
| Test protection | Fail | Focused frontend is green; focused backend and authoring architecture gates are red and major workflows have no behavior tests. |

## Frozen Boundary

The following references were read completely before the audit and are treated as certified and frozen:

1. `FINAL_STOREFRONT_ARCHITECTURE_CERTIFICATION.md`
2. `RECOVERY-5_CACHE_CORE_AND_CLEANUP_IMPLEMENTATION.md`
3. `RECOVERY-4_BROWSER_RUNTIME_CACHE_IMPLEMENTATION.md`
4. `RECOVERY-3_BUILDER_RUNTIME_IMPLEMENTATION.md`
5. `RECOVERY-2_THEME_RUNTIME_IMPLEMENTATION.md`
6. `RECOVERY-1_RUNTIME_OWNERSHIP_IMPLEMENTATION.md`

This certification did not reopen customer Runtime, Theme Runtime, Builder Runtime ownership, or Cache architecture. Runtime/projection/cache files are referenced only where an authoring command explicitly hands off an event, preview, refresh, or invalidation responsibility.

## Architecture

CMS Authoring is not one subsystem. It is a set of independently evolved authoring authorities:

| Surface | Frontend entry | Backend authority | Persisted draft/live model |
|---|---|---|---|
| Tenant CMS pages | `CmsPagesList.jsx`, `CmsPageEditor.jsx` | `AdminCmsPagesController`, `CmsPageService` | Immutable `CmsPage` version rows grouped by `PageGroupId` |
| Marketplace CMS pages | `MarketplaceCmsPagesPage.jsx`, `MarketplaceCmsPageEditor.jsx` | `SuperAdminMarketplaceCmsPagesController`, `MarketplaceCmsPageService` | Immutable `MarketplaceCmsPage` version rows |
| Navigation | `NavigationEditor.jsx` | `AdminCmsService` | `StoreConfig.CustomizationJson.navigation.draft/live` |
| Tenant footer | `StorefrontFooterSettings.jsx` | `AdminStorefrontFooterService` | Immediate-live replacement of footer section/link/social SQL rows |
| Marketplace footer | `MarketplaceFooterSettingsPage.jsx` | `SuperAdminMarketplaceFooterService` | Immediate-live replacement of Marketplace footer SQL rows |
| Global Storefront footer defaults | `StorefrontFooterDefaultsPage.jsx` | `SuperAdminStorefrontFooterService` | Immediate-live replacement of nullable-tenant footer SQL rows |
| Homepage | `HomepageBuilderPage.tsx`, `HomepageManagerPage.tsx` | `SuperAdminHomepageSectionManagementService` | Versioned `HomepageSection` draft/published rows |
| StoreConfig/status | `StorefrontSettings.jsx` and StoreConfig clients | `AdminStoreConfigService` | One tenant `StoreConfig` row with PostgreSQL `xmin` |
| Home/product-template layouts | `StorefrontBuilderPage.jsx` | `AdminStoreConfigService` | `CustomizationJson.pages` and `product_templates`, each with draft/live |
| Tenant seed | `CmsPagesList.jsx` | `AdminCmsPagesController.SeedDefaultPages` | Repeated page and Navigation commands, no batch entity |
| Marketplace seed | Hosted service only | `MarketplaceCmsPageSeeder` | Fire-and-forget repeated page commands |

There is no shared draft aggregate, publication orchestrator, concurrency abstraction, validation policy, or error contract across these authorities. This is directly visible in their incompatible tokens and mutations: semantic page versions, StoreConfig `xmin`, timestamp comparison, entity version fields that are not preconditions, and surfaces with no token at all.

### Authoring State Model

| Surface | Initial DTO and view model | Draft/change model | Save response adoption | Notification |
|---|---|---|---|---|
| Tenant CMS page | Server page has separate `draft`/`published`; editor chooses draft first (`CmsPageEditor.jsx:113-127`) | Local React form; no baseline dirty comparison or unload guard | Complete page response is identity-admitted and replaces page/form (`:159-181`) | Mixed inline error and `alert`; no consistent success notice |
| Marketplace CMS page | Separate draft/published DTO; editor chooses draft first (`MarketplaceCmsPageEditor.jsx:451-478`) | Local form; no dirty detection or guard | Response replaces page/form, but update currently fails for missing version (`:135-165`) | Inline error and `alert` |
| Navigation | Navigation DTO in Zustand plus category list (`NavigationEditor.jsx:113-204`) | Boolean dirty flag set by store mutations (`cmsStore.js:224-342`) | Full response replaces DTO and rotates RowVersion (`NavigationEditor.jsx:234-263`) | Status banners plus publish alert |
| Footer | Shared normalized form and serialized baseline (`FooterConfigScreen.jsx:52-125,157-171`) | Local full-configuration replacement | Parent adopts complete response | Inline errors; no success notification |
| Homepage Builder | Query DTO hydrated to client sections (`HomepageBuilderPage.tsx:374-502`) | Canonical serialized fingerprint and removed IDs (`:419-429`) | Bulk-save response is hydrated as working and saved baseline (`:730-761`) | Toasts |
| Homepage legacy manager | Query DTO plus modal-local section | No close/dirty guard | Mutation response discarded; query invalidated | Toasts |
| StoreConfig status | Accepted baseline plus candidate (`StorefrontSettings.jsx:34-50`) | Field-specific comparison | Accepted response replaces both (`:109-149`) | Toasts |
| StoreConfig layout | Zustand sections/history/dirty (`builderStore.js:23-73`) | Boolean dirty with autosave | Backend response must have RowVersion and replaces workspace (`StorefrontBuilderPage.jsx:310-396`) | Banners and alerts |

## Execution Graphs

### Tenant CMS Page

```text
GET /api/admin/pages/{groupId}
  -> AdminCmsService -> CmsPageService/read service -> SQL active draft/published rows
  -> AdminCmsPageResponse -> frontend identity admission -> local form

Save
  -> local form -> AdminCmsPageRequest.expectedVersion
  -> PUT /api/admin/pages/{groupId}
  -> serializable transaction + PostgreSQL FOR UPDATE
  -> retire old draft + insert immutable next draft version
  -> response replaces frontend page/form

Preview
  -> POST preview-link -> version/tenant/slug-bound token
  -> public preview resolves exact SQL version

Publish
  -> If-Match: semantic draft version
  -> serializable transaction
  -> retire old published row + promote draft
  -> stage StorefrontCmsPagePublishedEvent in the same save/transaction
  -> async CmsPageProjectionConsumer recomposes Mongo from SQL
```

Sources: `PointOfSale.API/Controllers/AdminCmsPagesController.cs:172-482`; `PointOfSale.API/Services/Cms/CmsPageService.cs:147-496,526-697`; `PointOfSale.Infrastructure/Consumers/CmsPageProjectionConsumer.cs:19-242`.

### Marketplace CMS Page

```text
GET /api/super-admin/marketplace/pages/{groupId}
  -> active draft/published SQL rows -> local form

Save existing
  -> request body must contain ExpectedVersion
  -> serializable transaction + group FOR UPDATE
  -> immutable next draft version

Publish/Delete
  -> expectedVersion query or If-Match required
  -> transactional page mutation + outbox event
  -> post-commit direct cache invalidation

Actual frontend
  -> sends none of ExpectedVersion/query/If-Match
  -> backend ConcurrencyConflictException -> 409
```

Sources: `PointOfSale.API/Services/MarketplaceCms/MarketplaceCmsPageService.cs:118-360,374-449`; `PointOfSale.API/SuperAdmin/Controllers/SuperAdminMarketplaceCmsPagesController.cs:95-271`; `PointOfSale.Web/src/super-admin/marketplace-cms/marketplaceCmsService.js:14-35`.

### Navigation

```text
GET /api/admin/cms/navigation
  -> tenant StoreConfig.CustomizationJson -> draft/live DTO + xmin RowVersion
  -> ETag and X-Resource-Version

Save
  -> frontend draft + RowVersion -> body and If-Match
  -> validate shape, CMS/category visibility, URL safety, duplicates
  -> set EF original xmin -> save navigation.draft
  -> response rotates RowVersion

Publish
  -> save first -> If-Match rotated RowVersion
  -> revalidate published CMS/live category targets
  -> copy draft to live + append StoreConfig snapshot
  -> stage NavigationPublishedEvent in same SaveChanges
  -> async NavigationProjectionConsumer recomposes Mongo
```

Sources: `PointOfSale.Web/src/modules/storefront/cms/pages/NavigationEditor.jsx:206-360`; `PointOfSale.API/Services/Admin/AdminCmsService.cs:281-421,659-721,723-855`; `PointOfSale.Infrastructure/Consumers/NavigationProjectionConsumer.cs:47-220`.

### Footer Configuration

```text
GET footer configuration + CMS page choices
  -> normalize complete graph -> local draft + JSON dirty baseline

PUT complete graph
  -> DTO/service validation
  -> delete all current section/social rows
  -> insert complete replacement graph
  -> SaveChanges commits SQL
  -> publish FooterUpdated event in a second operation
  -> return reloaded graph
```

Tenant footer adds an application timestamp comparison. Marketplace/global footer do not. None of the three has draft/publish separation. Sources: `PointOfSale.Web/src/shared/content/FooterConfigScreen.jsx:52-171,212-251`; `PointOfSale.API/Services/Admin/AdminStorefrontFooterService.cs:52-170`; `PointOfSale.API/SuperAdmin/Services/SuperAdminMarketplaceFooterService.cs:62-162`; `PointOfSale.API/SuperAdmin/Services/SuperAdminStorefrontFooterService.cs:49-140`.

### Homepage

```text
GET preview/published sections + schemas + optional tenant preview config
  -> query DTO -> client section model -> fingerprint dirty baseline

Modern save
  -> PUT /api/super-admin/homepage/sections/draft with complete workspace
  -> schema and reference validation per section
  -> transaction -> update/insert/tombstone draft rows

Legacy mutations
  -> POST/PUT/DELETE/reorder individual sections
  -> same draft rows through separate commands

Publish
  -> no expected version
  -> transaction -> archive published + promote current draft
  -> stage HomepageSectionsPublishedEvent
  -> async projection and Marketplace homepage invalidation
```

Sources: `PointOfSale.Web/src/super-admin/homepage/homepageService.ts:151-220`; `PointOfSale.API/SuperAdmin/Controllers/SuperAdminHomepageSectionsController.cs:29-359`; `PointOfSale.API/SuperAdmin/Services/SuperAdminHomepageSectionManagementService.cs:125-520`.

### StoreConfig and Layout Authoring

```text
GET /api/admin/store-config
  -> persisted StoreConfig or unpersisted default candidate
  -> body RowVersion + ETag

Layout save
  -> admitted workspace RowVersion -> If-Match
  -> strict section normalization/admission
  -> replace only target draft in CustomizationJson
  -> SaveChanges with original xmin
  -> response rotates RowVersion and replaces workspace

Preview
  -> first persist draft through the same save owner
  -> GET authenticated preview -> backend draft composition

Publish
  -> save response RowVersion -> If-Match
  -> distributed publish lock + xmin
  -> re-admit draft -> copy to live -> version snapshot
  -> stage StoreConfigPublishedEvent + TenantUpdatedEvent in same SaveChanges
  -> asynchronous SQL-authoritative projection
```

Sources: `PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx:132-190,208-421,492-589`; `PointOfSale.API/Controllers/AdminStoreConfigController.cs:68-433,720-779,882-929`; `PointOfSale.API/Services/Admin/AdminStoreConfigService.cs:95-554,868-1103`.

### Tenant CMS Seed

```text
POST /api/admin/cms/seed
  -> for each of about/contact/privacy/terms:
       read by slug
       create or immutable update (commit)
       publish (separate transaction/commit)
  -> GET Navigation
  -> PUT Navigation draft (separate commit)
  -> POST Navigation publish (separate commit)
  -> 200

Any exception
  -> broad catch(Exception)
  -> log SeedDefaultPages failed
  -> 500 INTERNAL_ERROR
  -> no rollback of prior commits
```

Source: `PointOfSale.API/Controllers/AdminCmsPagesController.cs:531-617`.

## Concurrency Analysis

### Token Ownership Matrix

| Surface | Token owner/storage | Frontend storage/send | Backend comparison/update | Response refresh | Certification |
|---|---|---|---|---|---|
| StoreConfig/full update | PostgreSQL `StoreConfig.xmin` mapped as `RowVersion` | DTO state; body RowVersion and quoted `If-Match` | EF original value; DB rotates `xmin` | Body plus `ETag`/`X-Resource-Version` | Sound local contract |
| Home/product-template layout | Same StoreConfig `xmin` | Builder workspace; quoted `If-Match` | EF original value on draft save and publish | Complete response admitted and stored | Sound, but local drafts can be overwritten by focus reload |
| StoreConfig preview | Current authenticated SQL draft | No preview concurrency token | Reads current draft | Preview DTO only | Correct for persisted draft; no unsaved preview |
| Tenant CMS page | Semantic `CmsPage.Version` in SQL version rows | Page DTO; update body `expectedVersion`; publish/delete/rollback `If-Match` | Serializable transaction and current-version comparison | Complete DTO; no ETag emitted | Effective but transport is inconsistent |
| Marketplace CMS page | Semantic `MarketplaceCmsPage.Version` | DTO exposes versions, frontend stores page but sends none | Serializable transaction and expected-version comparison | Complete DTO; no ETag | **Broken frontend contract** |
| Navigation | StoreConfig `xmin` | Navigation DTO; body RowVersion plus `If-Match` | EF original value | Complete DTO plus ETag | Effective except load-failure fallback |
| Tenant footer | Maximum child `UpdatedAt` value | Configuration DTO; body `ExpectedUpdatedAtUtc` | Application equality check before replacement | Complete DTO | Raceable; not a row token or locked compare-and-swap |
| Marketplace footer | None | None | No stale comparison; no concurrency token | Complete DTO | Fail |
| Global footer | Request DTO declares timestamp but frontend omits it and service ignores it | None | No stale comparison | Complete DTO | Fail |
| Homepage | Entity `Version` exists but is not used as a mutation precondition | DTO receives version; save payload omits it | Draft rows update in place; no `If-Match`/expected version | Modern save adopts response | Fail |
| Tenant seed | Internally reads each current version | No batch precondition | Per-command checks only | Final page list reload on success | No batch concurrency or idempotency |

StoreConfig `RowVersion` is owned by PostgreSQL, updated by PostgreSQL on row writes, exposed by backend responses, stored in frontend accepted DTOs, sent by frontend as `If-Match`, and applied by backend as the EF original `xmin` (`PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:1288-1335`; `PointOfSale.API/Services/Admin/AdminStoreConfigService.cs:1321-1348`; `PointOfSale.API/Controllers/AdminStoreConfigController.cs:882-929`).

CMS page `Version` is owned by backend immutable version rows and incremented by page services. It is not an HTTP ETag. The frontend must extract it from the response body. Tenant CMS does this; Marketplace CMS does not (`PointOfSale.Web/src/modules/storefront/cms/pages/CmsPageEditor.jsx:159-220`; `PointOfSale.Web/src/super-admin/marketplace-cms/marketplaceCmsService.js:19-35`).

### Duplicate Save Pipelines

| Overlap | Evidence | Risk |
|---|---|---|
| Homepage whole-draft save vs create/update/delete/reorder | `homepageService.ts:151-220`; `SuperAdminHomepageSectionManagementService.cs:125-305,398-520` | Two clients can mutate the same draft rows with no stale-write evidence. |
| Tenant CMS page list publish vs editor publish | `CmsPagesList.jsx:82-105`; `CmsPageEditor.jsx:199-241` | List uses list DTO version; editor can publish stale persisted state while unsaved form changes remain. |
| Marketplace list/editor publish | `MarketplaceCmsPagesPage.jsx:55-86`; `MarketplaceCmsPageEditor.jsx:167-188` | Both omit backend-required concurrency evidence. |
| Footer tenant/Marketplace/global replacement | Shared `FooterConfigScreen.jsx` with three distinct services | Similar UI hides different concurrency, localization, ordering, and validation semantics. |
| StoreConfig autosave/manual/preview/publish | `StorefrontBuilderPage.jsx:310-421,492-589` | These correctly converge on `persistPageLayout`; this is not duplicate serialization. |
| StoreConfig version snapshots | `AdminCmsService.cs:685-714`; `AdminStoreConfigService.cs:944-979` | Navigation and StoreConfig independently write the same history document with different actor metadata. |

### Stale DTO Survival

- Homepage Builder hydrates query state only once per scope, so same-scope refetches are not adopted (`HomepageBuilderPage.tsx:491-502`).
- CMS and Marketplace CMS forms have no dirty baseline. Existing-page publish uses the loaded `page` instead of saving the form first; Preview always targets persisted state (`CmsPageEditor.jsx:199-220,319-340`; `MarketplaceCmsPageEditor.jsx:167-203`).
- Builder focus/visibility refresh unconditionally clears and reloads admitted sections. It does not check `isDirty`; admission marks the reloaded state clean (`StorefrontBuilderPage.jsx:208-308`; `builderStore.js:58-73`).
- Footer Refresh and incoming configuration props replace the local draft without a dirty confirmation (`FooterConfigScreen.jsx:162-171,238-245`).
- Navigation load failure creates an editable object with no RowVersion, after which the API client sends `If-Match: "undefined"` (`NavigationEditor.jsx:173-232`; `cmsService.js:114-123`).

## Validation Analysis

### Responsibility Matrix

| Surface | Client validation | Server admission | Reference/visibility checks | Result |
|---|---|---|---|---|
| Tenant CMS pages | Title and slug required; form shaping | Slug/title/schema/content, bounded blocks/FAQ, sanitizer, safe image/OG URL | Tenant slug uniqueness | Strong backend; client dirty/preview semantics fail |
| Marketplace CMS pages | English title and slug required | Localized normalization, sanitizer, supported blocks, URL safety | Global slug application check | Bounds weaker than tenant CMS; concurrency client broken |
| Navigation | Add modal HTML requirements; inline edits can be blank | Max 50, label/type, safe path/URL, duplicates | Saved CMS on draft; published CMS on publish; live categories | Strong intent, category DTO contract is incompatible |
| Tenant footer | No semantic client validation | DTO lengths and type requirements; strict URL policy | Published tenant CMS pages, duplicate targets | Timestamp concurrency is not atomic; count and social uniqueness gaps |
| Marketplace footer | No semantic client validation | Basic localized fields and URL forms | Draft or published Marketplace CMS slug | Wrong visibility admission for immediately-live output |
| Global footer | No semantic client validation | Basic DTO and URL forms | CMS unresolved until tenant use | Localization wire contract loses Arabic data |
| Homepage | Schema-driven required/type/GUID checks | Closed schema, type/range/GUID, tenant/listing/category visibility at save | Strong save-time product/category checks | URL safety absent; no publish-time reference revalidation |
| StoreConfig layouts | Canonical frontend contract validator | Closed section/settings contracts, nested shapes, URL/GUID/list checks, Product Hero invariant | GUID syntax only | No product/category existence, tenant, deletion, or visibility admission |

### Validation Findings

1. Homepage Hero/Banner `imageUrl` and `ctaLink` use only `ReadRequiredString`; there is no URL scheme, control-character, credential, or length policy (`PointOfSale.API/Services/Homepage/HomepageSectionSchemaRegistry.cs:148-162,224-238,339-359`). Publish does not rerun schema/reference admission (`SuperAdminHomepageSectionManagementService.cs:307-395`).
2. Homepage references are strongly checked during save but not publication, allowing product/vendor/category visibility to change between draft and publish (`SuperAdminHomepageSectionManagementService.cs:137-138,191-192,452-457,644-729` versus `:307-395`).
3. Layout product/category GUID lists are checked syntactically but not resolved against tenant SQL authority (`PointOfSale.Core/Constants/StorefrontSectionContracts.cs:179-219`; `AdminStoreConfigService.cs:1937-2206`).
4. Marketplace CMS validation is a fork of tenant CMS and lacks equivalent block count, FAQ count, text length, and alignment bounds (`CmsPageContentValidator.cs:23-26,90-95,145-228`; `MarketplaceCmsPageContentValidator.cs:62-102,116-188`).
5. Marketplace footer accepts draft-only page slugs, but its public reader resolves published pages only (`SuperAdminMarketplaceFooterService.cs:72-86`; `MarketplaceFooterReadService.cs:112-140`).
6. Footer collection sizes and URL lengths are not bounded at admission; duplicate social platforms reach unique database indexes and non-concurrency `DbUpdateException` is not translated (`AdminStorefrontFooterDtos.cs:5-147`; `StorefrontSocialLinkConfiguration.cs:40-48`; `AdminStorefrontFooterService.cs:297-306`).
7. Explicitly null collection/object properties can reach `.Count`, iteration, or dereference and become 500 responses. Examples include Navigation header/footer (`AdminCmsService.cs:723-753`), Homepage sections/IDs (`SuperAdminHomepageSectionManagementService.cs:269-279,410-446`), and StoreConfig `CommerceAi` (`AdminStoreConfigService.cs:181-187`).
8. URL policy is duplicated across CMS, Marketplace CMS, Navigation, three footer services, Homepage, and StoreConfig contracts. The implementations have materially different credential, internal-path, length, and scheme behavior.

### Media Certification

Media references are not authoritative media references. They are URL strings:

- Tenant CMS: image block `imageUrl` and SEO `OpenGraphImageUrl` (`CmsPageContentValidator.cs:174-187,250-280`).
- Homepage: Hero/Banner `imageUrl` (`HomepageSectionSchemaRegistry.cs:148-162,224-238`).
- Layouts: URL/image settings in section contracts (`StorefrontSectionContracts.cs:204-219,243-263,343-389`).
- Footer: URL strings on links/social links (`AdminStorefrontFooterDtos.cs:67-147`).

No audited command resolves an asset ID or verifies media existence, tenant/store ownership, deletion state, or publication state. CMS and StoreConfig enforce safe URL syntax; Homepage does not. No CMS/Homepage/layout authoring component integrates the existing product media library.

## Seed Analysis

### Tenant Seed Transaction Boundary

There is no seed transaction. Each page create/update commits independently; each page publication has its own serializable transaction; Navigation update and Navigation publish are separate SaveChanges operations (`AdminCmsPagesController.cs:546-605`; `CmsPageService.cs:147-335`; `AdminCmsService.cs:300-421`).

Rollback is local only. Failure on page N leaves earlier page versions published. A page update can remain as a draft if its later publish fails. Navigation update can remain as a draft if Navigation publish fails.

### Tenant Seed Idempotency and Duplicate Detection

The operation checks each normalized tenant slug and creates or updates it. A repeat call does not no-op: it creates another immutable draft version, republishes it, overwrites administrator-authored content with defaults, appends Navigation history, and emits new events (`AdminCmsPagesController.cs:548-605`). There is no seed execution record, request key, content hash, or batch idempotency key.

Concurrent seed calls can race at the read/create boundary. Database uniqueness decides a loser, but the action maps all exceptions to 500.

Tenant ownership itself is correct: authenticated Owner/Admin access is resolved through trusted tenant context; the seed request contains no caller-selected tenant (`AdminCmsPagesController.cs:14-17,531-543`; `TenantResolutionMiddleware.cs:78-239`; `AdminTenantAuthorityMiddleware.cs:24-72`).

### Exact HTTP 500 Root Cause

For a tenant without a persisted `StoreConfig`:

1. `GetNavigationAsync` returns a synthetic default with RowVersion `0` when no row exists (`AdminCmsService.cs:291-298`; `AdminCmsDtos.cs:160-166`).
2. Seed unconditionally calls `UpdateNavigationAsync` with that synthetic RowVersion (`AdminCmsPagesController.cs:594-600`).
3. `UpdateNavigationAsync` requires a persisted row and throws `InvalidOperationException("Store config not found")` (`AdminCmsService.cs:312-318`).
4. The seed action has only `catch (Exception)`, logs `SeedDefaultPages failed`, and returns HTTP 500 `INTERNAL_ERROR` (`AdminCmsPagesController.cs:614-617`).

All four page publications occur before the Navigation phase, so they remain committed even though the client receives 500. This is the exact source-backed root cause of the current runtime failure. It affects legacy, fixture-created, or incomplete tenants; current tenant registration normally creates StoreConfig (`TenantRegistrationService.cs:248-295`).

### Marketplace Startup Seed

`MarketplaceCmsPageSeeder.StartAsync` starts an untracked `Task.Run` and returns immediately; Stop does not await or cancel it (`MarketplaceCmsPageSeeder.cs:22-30,72`). It skips all defaults if any active Marketplace page exists (`:39-48`). Each default draft creation and publication is a separate commit (`:50-54`).

Consequences:

- A partial seed permanently suppresses repair on the next startup.
- A custom pre-existing page suppresses all defaults.
- Concurrent hosts can both pass the empty-table check.
- Marketplace active slug uniqueness is application-only, not a unique database constraint (`MarketplaceCmsPageConfiguration.cs:72-101`; `MarketplaceCmsPageService.cs:423-437`).
- Failures are logged and swallowed, so the host continues with partial or absent defaults (`MarketplaceCmsPageSeeder.cs:58-69`).

## Error Mapping

### Endpoint Matrix

| Endpoint family | Success | Expected mappings | Incorrect/unhandled mappings |
|---|---:|---|---|
| Tenant CMS reads | 200 | 400 tenant, 404 page | Query/mapping failure -> sanitized 500, appropriate |
| Tenant CMS create | 201 | 400 validation, 409 slug | DB uniqueness/concurrency translation can escape to 500 |
| Tenant CMS update | 200 | 400 validation, 404, 409, 428 | Update uses body version while peer mutations use `If-Match`; no response ETag |
| Tenant CMS publish/rollback | 200 | 400 invalid state, 404, 409, 428 | Generic persistence/JSON failures -> 500 |
| Tenant CMS delete | 204 | 409, 428 | Missing page also returns 204; accepted only as idempotent delete semantics |
| Tenant CMS seed | 200 | 400 tenant | Every action exception -> 500, including missing StoreConfig and concurrency |
| Navigation GET | 200 + ETag | 400 tenant | Corrupt JSON becomes synthetic default rather than an explicit authoring error |
| Navigation PUT | 200 + ETag | 400 link validation, 409, 428 | Null collections -> 500 |
| Navigation publish | 200 + ETag | 400 invalid operation, 409, 428 | `InvalidNavigationLinkException` is not caught and becomes 500 (`AdminCmsPagesController.cs:724-751`) |
| Tenant footer PUT | 200 | 400 validation, 409, 428 | DB constraint failures and event failure -> 500; event failure occurs after SQL commit |
| Marketplace CMS create | 200 | 400, 409 slug | 200 is nonstandard for creation; relational race can become 500 |
| Marketplace CMS update | 200 | 400, 404, 409 | Missing version is 409 instead of 428; frontend always hits it |
| Marketplace CMS publish/delete | 200 | 400/404/409 | Missing version is 409; post-commit invalidation can return 500 after success |
| Marketplace/global footer PUT | 200 | 400, nominal 409 | No effective stale check; DB/event errors -> 500 after possible SQL commit |
| Homepage mutations | 200 | 400 schema/scope/reference, 404, selected 409 | Null collections and unrecognized DB failures -> 500; no precondition status exists |
| StoreConfig full/status PUT | 200 + ETag | 400, 409, 428 | Explicit null `CommerceAi` -> 500 |
| StoreConfig layout draft | 200 + ETag | 409, 422, 428 | Missing StoreConfig or malformed stored JSON -> 500 |
| StoreConfig layout publish | 200 + ETag | 404, 409, 422, 428 | Missing StoreConfig or malformed stored JSON -> 500 |
| Product-template delete | 200 + ETag | 409, 428 | Missing template returns unchanged 200; missing StoreConfig -> 500 |
| StoreConfig rollback | 200 + ETag | 400 rejected, 409, 428 | Unexpected persistence/JSON failures -> 500 |

`400` is appropriate for malformed requests and failed domain admission. `404` is appropriate for absent read/publish targets. `409` is appropriate for a supplied but stale version. `428` is the correct mapping for a missing required precondition. `422` is a defensible mapping for syntactically valid but semantically invalid layout composition. The architecture is not certifiable because equivalent failures do not map consistently and multiple expected authoring conditions reach `500`.

## Ownership Matrix

| Responsibility | Frontend | Backend | Owner |
|---|---|---|---|
| Unsaved candidate state | Form/Zustand state and local edit operations | None until save | Frontend |
| Dirty/change detection | Compare accepted baseline to candidate; warn before replacement/navigation | Returns accepted DTO baseline | Frontend |
| Persisted draft ownership | Displays and edits returned draft | Creates, versions, validates, and stores draft | Backend |
| Preview launch | Requests preview and opens local canvas/window | Resolves current persisted draft or issues exact-version token | Backend for preview truth; Frontend for presentation |
| Client validation | Immediate UX checks only | Must not trust it | Frontend, advisory |
| Admission validation | Sends candidate | Validates shape, semantics, references, visibility, and ownership before persistence/publication | Backend |
| Concurrency evidence transport | Stores accepted token and sends `If-Match`/expected version | Emits token and rejects stale/missing evidence | Frontend transport; Backend authority |
| RowVersion generation | Never generates | PostgreSQL `xmin` or backend semantic version | Backend/database |
| Persistence | None | SQL entities/JSON and transactions | Backend |
| Seed | Confirm/trigger only | Scope, duplicate policy, transaction, idempotency, page/navigation commands | Backend |
| Publication | User intent and confirmation | Lock/version check, revalidation, state transition, outbox, response | Backend |
| Projection | No direct mutation | Event consumers recompose derivative models from SQL | Backend |
| Cache refresh/invalidation | Query invalidation after accepted authoring response where applicable | Authoring event/semantic invalidation owner | Existing frozen cache owners |
| User notification | Toast/banner/dialog based on accepted/rejected response | Structured status/code/message | Frontend presentation; Backend error truth |
| Audit | Displays history where exposed | Stores actor/action/version evidence | Backend |
| Media reference truth | URL entry only | Must validate asset authority if media is represented as managed media | Backend; currently absent |

## Architectural Violations

### P0

| ID | Violation | Source evidence | Impact |
|---|---|---|---|
| CMS-AUTH-P0-01 | Marketplace CMS frontend omits every backend-required version on update, publish, and delete. | `marketplaceCmsService.js:19-35`; `MarketplaceCmsPageEditor.jsx:135-180`; `SuperAdminMarketplaceCmsPagesController.cs:151-182,224-271`; `MarketplaceCmsPageService.cs:168-186` | Shipped Marketplace CMS authoring cannot update, publish, or delete. New drafts cannot be published. |

### P1

| ID | Violation | Source evidence | Impact |
|---|---|---|---|
| CMS-AUTH-P1-01 | Homepage has no optimistic concurrency on per-section, bulk draft, reorder, or publish commands. | `HomepageSectionDtos.cs:5-84`; `SuperAdminHomepageSectionManagementService.cs:175-224,257-305,307-395,398-520` | Last writer wins; stale clients can overwrite a complete workspace. |
| CMS-AUTH-P1-02 | Modern and legacy Homepage pipelines mutate the same drafts; same-scope query refresh is not rehydrated. | `homepageService.ts:151-220`; `HomepageBuilderPage.tsx:491-502,946-952` | Duplicate save owners and durable stale DTOs. |
| CMS-AUTH-P1-03 | Tenant seed read/write contracts disagree for missing StoreConfig and all action failures collapse to 500 after partial commits. | `AdminCmsService.cs:291-318`; `AdminCmsPagesController.cs:546-617` | Verified current HTTP 500 and partially seeded tenant state. |
| CMS-AUTH-P1-04 | Tenant seed is not idempotent or operation-atomic. | `AdminCmsPagesController.cs:548-605`; `CmsPageService.cs:147-335` | Repeated seed overwrites custom content and creates version/event churn; rollback is partial. |
| CMS-AUTH-P1-05 | Marketplace startup seed is fire-and-forget, coarse-skip, non-atomic, and raceable. | `MarketplaceCmsPageSeeder.cs:22-72`; `MarketplaceCmsPageConfiguration.cs:72-101` | Partial defaults, permanent skipped repair, and duplicate slugs under multi-host startup. |
| CMS-AUTH-P1-06 | All footer mutation services commit SQL before event/outbox staging. | `AdminStorefrontFooterService.cs:161-168`; `SuperAdminMarketplaceFooterService.cs:154-160`; `SuperAdminStorefrontFooterService.cs:131-138` | 500 can be returned after truth changed; derivative projection/cache can remain stale. |
| CMS-AUTH-P1-07 | Marketplace/global footer have no stale-write protection; tenant timestamp comparison is not atomic. | `SuperAdminMarketplaceFooterService.cs:62-160`; `SuperAdminStorefrontFooterService.cs:49-138`; `AdminStorefrontFooterService.cs:63-70,100-161` | Concurrent full replacements lose updates. |
| CMS-AUTH-P1-08 | Tenant and Marketplace CMS publish/preview ignore unsaved form state. | `CmsPageEditor.jsx:149-220,319-340`; `MarketplaceCmsPageEditor.jsx:135-203` | Preview/publish does not represent visible edits; publication can replace the form with stale server state. |
| CMS-AUTH-P1-09 | Navigation category client/server target formats are incompatible. | `NavigationEditor.jsx:125-158,763-770`; `CategoriesController.cs:84-115,424-430`; `AdminCmsService.cs:461-473,785-790` | Category links selected by the UI are rejected by backend admission. |
| CMS-AUTH-P1-10 | Navigation publication validation exceptions are not mapped and become 500. | `AdminCmsPagesController.cs:682-703,724-751`; `AdminCmsService.cs:398-406` | Normal dependency drift between save and publish is reported as server failure. |
| CMS-AUTH-P1-11 | Homepage public URL/media fields have no URL-safety or length admission and are not re-admitted at publish. | `HomepageSectionSchemaRegistry.cs:148-162,224-238,339-359`; `SuperAdminHomepageSectionManagementService.cs:307-395` | Unsafe or unbounded authored values can be persisted and published. |
| CMS-AUTH-P1-12 | Homepage references are not revalidated at publication. | `SuperAdminHomepageSectionManagementService.cs:137-138,191-192,452-457,644-729` versus `:307-395` | A draft can publish references that became invisible after save. |
| CMS-AUTH-P1-13 | Layout product/category references are only syntactically validated. | `StorefrontSectionContracts.cs:179-219`; `AdminStoreConfigService.cs:1937-2206` | Missing, foreign, deleted, or invisible IDs can be accepted into drafts/live layouts. |
| CMS-AUTH-P1-14 | Builder focus/visibility refresh can discard unsaved local edits. | `StorefrontBuilderPage.jsx:208-308`; `builderStore.js:58-73` | A normal browser focus transition can replace candidate state before autosave. |
| CMS-AUTH-P1-15 | Footer load failure leaves an editable empty full-replacement form. | `MarketplaceFooterSettingsPage.jsx:16-45`; `StorefrontFooterDefaultsPage.jsx:15-38`; `FooterConfigScreen.jsx:212-251` | Saving after a failed load can erase an existing configuration. |
| CMS-AUTH-P1-16 | Global footer localization frontend and DTO do not round-trip section/link Arabic fields. | `FooterConfigScreen.jsx:99-124`; `StorefrontFooterDefaultsPage.jsx:41-55`; `AdminStorefrontFooterDtos.cs:35-76,158-178`; `SuperAdminStorefrontFooterService.cs:85-112` | The UI advertises localization but saves data under incompatible property names and can erase Arabic values. |
| CMS-AUTH-P1-17 | StoreConfig rollback can restore Navigation live JSON without emitting `NavigationPublishedEvent`. | `AdminStoreConfigService.cs:803-828`; `NavigationProjectionConsumer.cs:25-27` | Navigation derivative state can remain on the pre-rollback publication. |
| CMS-AUTH-P1-18 | CMS/Homepage/layout media have no authoritative asset existence or ownership admission. | `CmsPageContentValidator.cs:174-187`; `HomepageSectionSchemaRegistry.cs:148-162,224-238`; `StorefrontSectionContracts.cs:204-219,243-263` | Broken, foreign, deleted, or unowned media URLs remain authorable. |
| CMS-AUTH-P1-19 | Marketplace CMS active slug uniqueness is application-only. | `MarketplaceCmsPageConfiguration.cs:72-101`; `MarketplaceCmsPageService.cs:423-437` | Concurrent create/rename can produce duplicate public slugs. |

### P2

| ID | Violation | Source evidence | Impact |
|---|---|---|---|
| CMS-AUTH-P2-01 | CMS page concurrency transport is inconsistent and page ETags are not emitted. | `AdminCmsPagesController.cs:241-244,859-882`; `cmsService.js:66-101` | More client-specific logic and precondition drift. |
| CMS-AUTH-P2-02 | `extractCmsConflict` treats any HTTP 409 as concurrency, including slug conflicts. | `cmsService.js:10-20` | Wrong conflict UX and latest-version semantics. |
| CMS-AUTH-P2-03 | Navigation load failure creates editable state without a RowVersion. | `NavigationEditor.jsx:173-232`; `cmsService.js:114-123` | Save emits `If-Match: "undefined"` rather than failing closed. |
| CMS-AUTH-P2-04 | Navigation snapshots duplicate StoreConfig snapshot logic and hard-code actor identity. | `AdminCmsService.cs:685-714`; `AdminStoreConfigService.cs:944-979` | Divergent audit/history metadata. |
| CMS-AUTH-P2-05 | Tenant footer ignores submitted order while global footer uses it. | `AdminStorefrontFooterService.cs:113-158`; `SuperAdminStorefrontFooterService.cs:85-128` | Same UI contract persists different ordering semantics. |
| CMS-AUTH-P2-06 | Footer DTO/service validation omits count limits, URL lengths, enum rejection, and duplicate social admission. | `AdminStorefrontFooterDtos.cs:5-147`; `StorefrontSocialLinkConfiguration.cs:40-48` | Invalid payloads reach database errors/500 and unbounded replacement work. |
| CMS-AUTH-P2-07 | Marketplace footer permits draft-only CMS targets and weaker URL rules. | `SuperAdminMarketplaceFooterService.cs:72-92,219-270`; `MarketplaceFooterReadService.cs:112-140` | Saved links can disappear publicly; credential/internal-path policies diverge. |
| CMS-AUTH-P2-08 | Marketplace list Preview builds `/{slug}` while the route is `/:lang/:slug`. | `MarketplaceCmsPagesPage.jsx:89-95`; `PointOfSale.Web/src/App.jsx:183-185` | List preview is unroutable. |
| CMS-AUTH-P2-09 | Marketplace CMS validation is materially weaker than tenant CMS validation. | `MarketplaceCmsPageContentValidator.cs:62-102,116-188`; `CmsPageContentValidator.cs:23-26,90-95,145-228` | Oversized or unsupported content semantics vary by authoring surface. |
| CMS-AUTH-P2-10 | StoreConfig full request accepts `Pages` but update ignores it. | `AdminStoreConfigDtos.cs:59-63`; `AdminStoreConfigService.cs:168-189` | Caller believes page authoring was accepted when it was not persisted. |
| CMS-AUTH-P2-11 | Missing StoreConfig and malformed stored JSON become 500 on layout mutation. | `AdminStoreConfigService.cs:277-325,354-405,1321-1335`; `AdminStoreConfigController.cs:326-330,387-390` | Recoverable authoring state problems are reported as internal faults. |
| CMS-AUTH-P2-12 | Arbitrary page/template keys are accepted as JSON dictionary keys. | `AdminStoreConfigService.cs:286-325,1910-1921` | Unrecognized authoring namespaces can be persisted. |
| CMS-AUTH-P2-13 | Footer and Homepage null collections can produce 500 instead of validation responses. | `AdminStorefrontFooterDtos.cs:5-31`; `SuperAdminHomepageSectionManagementService.cs:269-279,410-446` | Malformed client input is misclassified as server failure. |
| CMS-AUTH-P2-14 | Marketplace page post-commit cache invalidation can fail the HTTP request after publication/delete committed. | `MarketplaceCmsPageService.cs:283-305,334-360` | Ambiguous retry can create duplicate user intent despite successful mutation. |
| CMS-AUTH-P2-15 | StoreConfig GET claims to create a default but returns an unpersisted random-ID candidate. | `AdminStoreConfigController.cs:61-64`; `AdminStoreConfigService.cs:104-116,920-941` | Read identity changes until the first accepted write. |
| CMS-AUTH-P2-16 | CMS preview can synthesize draft metadata when no draft row exists. | `AdminCmsService.cs:193-222` | Preview DTO metadata may not represent a persisted draft. |
| CMS-AUTH-P2-17 | Builder publish modal reads `isPublishing` through nonreactive `getState()`. | `StorefrontBuilderPage.jsx:857-864`; `PublishModal.jsx:84-103` | Duplicate-submit disabling is not guaranteed to rerender. |
| CMS-AUTH-P2-18 | StoreConfig declares layout/behavior fields with no audited authoring controls. | `PointOfSale.Web/src/types/storeConfig.ts:69-83`; `ThemeEditorPage.jsx:322-343` | DTO capabilities are not fully authorable through the CMS UI. |
| CMS-AUTH-P2-19 | Successful authoring notification patterns are inconsistent or absent. | CMS/Marketplace/Navigation/Footer pages cited above | Operators cannot consistently distinguish accepted, pending, and projected state. |

## Root Cause Matrix

| Observed symptom | Verified root cause | Responsible component | Recommended recovery | Priority |
|---|---|---|---|---|
| Marketplace page update/publish/delete always conflicts | Frontend never supplies body `ExpectedVersion`, query version, or `If-Match` | Marketplace CMS frontend API/editor | Adopt returned draft/current version for every mutation and preserve it after responses | P0 |
| Tenant seed returns 500 after pages appear | Missing Navigation read returns synthetic RowVersion 0; write rejects missing StoreConfig; broad catch maps it to 500 | Tenant seed orchestration and `AdminCmsService` contract | Make seed establish/require one consistent StoreConfig authority and map domain/precondition failures explicitly | P1 |
| Seed retry creates new versions and overwrites content | Seed always updates and publishes existing default slugs | Tenant seed orchestration | Define idempotent per-slug default admission and customized-content preservation | P1 |
| Homepage changes disappear under another admin | No expected version and overlapping bulk/per-section mutation paths | Homepage authoring API/service | Establish one draft revision precondition across all Homepage mutations | P1 |
| Footer request can fail after data changed | Event publication occurs after committed SaveChanges | Three footer services | Stage event/outbox in the same transaction/save as footer replacement | P1 |
| Footer concurrent edits overwrite each other | Global/Marketplace have no token; tenant compare is not atomic | Footer authoring services | Use one backend-owned revision with compare-and-swap semantics for each footer scope | P1 |
| Preview/publish omits visible CMS edits | Existing-page handlers use loaded `page`; no dirty baseline | Tenant and Marketplace CMS editors | Make save the single prerequisite for persisted preview/publish and retain candidate on rejection | P1 |
| Category Navigation link fails save | Category tree DTO has no slug; UI falls back to GUID; server checks generated slug | Navigation editor/category API/admission | Use one backend-issued category reference representation end to end | P1 |
| Navigation publish returns 500 after dependency drift | Controller omits `InvalidNavigationLinkException` mapping on publish | `AdminCmsPagesController` | Map publish admission failure to structured 4xx and keep draft intact | P1 |
| Builder edits disappear on tab focus | Focus handler reloads without dirty guard and admission resets state | `StorefrontBuilderPage` | Prevent automatic replacement while candidate is dirty or saving | P1 |
| Homepage accepts unsafe image/CTA values | Schema registry reads nonempty strings only | Homepage schema admission | Apply bounded safe URL/path admission and rerun it on publish | P1 |
| Published curated references can be stale | Save validates references; publish does not | Homepage and StoreConfig layout admission | Revalidate authoritative references at publication | P1 |
| Footer Arabic values disappear | Shared localized payload names do not match admin footer DTO; section Arabic absent | Global footer frontend/DTO/service | Align one round-trip localization contract before replacement | P1 |
| Navigation projection can miss rollback | Rollback restores Navigation JSON but emits only StoreConfig rollback event | StoreConfig rollback event handoff | Emit/route the authoritative Navigation publication transition during rollback | P1 |
| Broken/foreign media remains accepted | Authoring stores URLs without managed asset identity checks | CMS/Homepage/layout media admission | Define backend asset ownership/existence admission for managed media references | P1 |
| Invalid/null authoring payload becomes 500 | Initializers do not protect explicitly deserialized null; controllers catch generically | DTO and controller admission | Reject null collections/objects at model/service admission with 400/422 | P2 |
| Marketplace list preview is 404/unroutable | URL omits required language segment | Marketplace CMS list UI | Build the same language-qualified route used by the editor | P2 |
| StoreConfig layout missing row returns 500 | Mutation helper throws uncaught `InvalidOperationException` | StoreConfig controller/service | Map missing authority to an explicit creation/precondition/not-found contract | P2 |

## Risk Assessment

| Risk | Likelihood | Impact | Rating |
|---|---|---|---|
| Marketplace CMS cannot complete core lifecycle | Certain | Release-blocking workflow outage | Critical |
| Lost Homepage/footer updates under concurrent admins | High | Silent content loss | High |
| Partial tenant seed followed by 500/retry | High for incomplete tenants | Content overwrite, version churn, misleading failure | High |
| Footer event gap after committed SQL | Medium | Stale derivative state and ambiguous retry | High |
| Unsaved CMS/Builder edits discarded | High | Author work loss and wrong publication | High |
| Invalid Navigation category targets | High when category links are used | Workflow rejection | High |
| Unsafe/unbounded Homepage URLs | Medium | Security and content-integrity exposure at admission boundary | High |
| Stale product/category/media references | Medium | Broken published composition | High |
| Null/constraint payloads mapped to 500 | Medium | Operational noise and poor client recovery | Medium |
| Stale/non-authoritative tests | High | False confidence or false failures in CI | High |

## Test Coverage

### Executed Gates

| Gate | Result |
|---|---|
| Focused Web authoring tests, 11 files | **PASS: 57/57** |
| Focused backend authoring behavior, 16 test classes/groups | **FAIL: 79/92; 13 failed** |
| Focused CMS/StoreConfig/Builder authoring architecture gate | **FAIL: 67/82; 15 failed** |

The frontend gate covers service wrappers, DTO identity admission, StoreConfig settings, Builder source contracts, Homepage serialization/preview helpers, and one Builder load-stability test. It does not render the main CMS, Navigation, Footer, Marketplace CMS, Homepage Builder, or Homepage Manager workflows.

### Backend Failure Triage

The 13 behavior failures were:

- Ten `CmsPageWorkflowTests` fail after create because their fixture builds `ApplicationDbContext` without tenant context while production CMS queries use strict tenant filters (`PointOfSale.Tests/Cms/CmsPageWorkflowTests.cs:395-402`; failure at `CmsPageService.cs:193`).
- `StorefrontPreviewServiceTests.GetConfigPreviewAsync_ReturnsDraftConfigWithoutFallingBackToLive` fails its section assertion (`PointOfSale.Tests/Storefront/StorefrontPreviewServiceTests.cs:110`).
- `AdminStoreConfigRollbackSafetyTests.RollbackStoreConfigAsync_PreservesLatestDraftWrittenByAnotherAdmin` uses a section fixture no longer admitted by the current strict contract (`PointOfSale.Tests/Admin/AdminStoreConfigRollbackSafetyTests.cs:123`; failure at `AdminStoreConfigService.cs:2061`).
- `FooterSystemTests.MarketplaceCmsPageSeeder_SeedsDefaultPagesOnlyOnce` expects synchronous completion from a fire-and-forget hosted service and observed two rather than four pages (`PointOfSale.Tests/Cms/FooterSystemTests.cs:214-251`; `MarketplaceCmsPageSeeder.cs:22-27`).

The 15 architecture failures are predominantly stale assertions against frozen recovered architecture:

- Three expect removed CMS cache invalidation (`InvalidatePublishedScopeAsync`).
- Three expect direct Redis key deletion removed by Cache Recovery.
- One expects pre-Theme-Recovery header implementation text.
- One expects an endpoint-local preview cache header now owned by canonical Storefront middleware.
- Seven require historical certification marker text not present in the referenced artifact.

These stale failures do not reopen frozen Cache, Theme, or Builder Runtime. They do prove that the CMS authoring architecture gate is not currently a trustworthy green release gate.

### Existing Strengths

- Tenant CMS content sanitizer, block admission, unsafe image URL, SEO, immutable version, preview binding, history, rollback, and stale semantic version tests exist under `PointOfSale.Tests/Cms/`.
- StoreConfig controller preconditions, publish rollback on outbox failure, rollback safety, preview, and projection tests exist under `PointOfSale.Tests/Admin/` and `PointOfSale.Tests/Storefront/`.
- Homepage service tests cover draft isolation, publish, reorder, references, starter template, event ordering, and projection-adjacent behavior (`PointOfSale.Tests/SuperAdmin/SuperAdminHomepageSectionManagementServiceTests.cs`).
- Focused frontend StoreConfig and contract tests are green.

### Missing Coverage

No tests were found for `SeedDefaultPages` or `/api/admin/cms/seed`. No frontend behavior tests render `CmsPageEditor`, `NavigationEditor`, `FooterConfigScreen`, `MarketplaceCmsPageEditor`, `HomepageBuilderPage`, or `HomepageManagerPage`.

Material missing gates:

1. Hosted HTTP contract tests for every authoring endpoint, including auth/tenant authority, 400/404/409/422/428/500, ETag, and response adoption.
2. PostgreSQL two-admin races for CMS, StoreConfig, Homepage, and all footer scopes.
3. Marketplace frontend version propagation and full create/save/publish/delete lifecycle.
4. Tenant seed first run, repeat, customization preservation, missing StoreConfig, partial failure, and concurrent calls.
5. Footer event failure atomicity, stale saves, localization round-trip, and load-failure destructive-save prevention.
6. Navigation category selection, save/publish dependency drift, and projection convergence.
7. Main component dirty/preview/publish behavior and in-flight duplicate suppression.
8. CMS/Navigation/Footer projection consumer behavior tests; current coverage is mostly source-string architecture checks.
9. Media existence/ownership and dependency withdrawal between draft and publish.

## Recovery Roadmap

This is a certification roadmap only. No recovery was started.

### Recovery 0: Release Blocker

1. Restore the Marketplace CMS frontend/backend version contract for update, publish, and delete.
2. Add a complete frontend plus hosted API lifecycle gate before any broader cleanup.

### Recovery 1: Data-Loss Boundaries

1. Add one backend-owned revision precondition to all Homepage commands and eliminate overlapping stale save behavior.
2. Make footer truth plus outbox event one atomic mutation for tenant, Marketplace, and global scopes.
3. Establish effective compare-and-swap concurrency for all footer scopes.
4. Prevent dirty candidate replacement on CMS publish/preview, Builder focus refresh, and footer refresh/load failure.

### Recovery 2: Seed Safety

1. Resolve the missing-StoreConfig read/write contradiction.
2. Define tenant seed idempotency and customization-preservation semantics.
3. Introduce an operation-level transaction/recovery contract or an explicit resumable batch contract.
4. Make Marketplace startup seeding awaitable, per-slug, retryable, and safe under multiple hosts.

### Recovery 3: Admission and Error Contracts

1. Unify category reference representation between the Navigation UI and backend.
2. Add publish-time reference and URL/media admission for Homepage and layouts.
3. Align tenant/Marketplace CMS and footer validation where their contracts are equivalent.
4. Map missing preconditions to 428, stale evidence to 409, admission to 400/422, absence to 404/idempotent delete, and reserve 500 for unexpected faults.

### Recovery 4: Projection, Localization, and Quality Gates

1. Ensure Navigation rollback produces the required derivative handoff.
2. Align global footer localization DTOs and round-trip behavior.
3. Replace stale source-string tests with behavioral authoring and PostgreSQL integration gates.
4. Keep all work confined to CMS Authoring and its explicit command/event boundaries; do not reopen frozen Runtime, Theme, Builder Runtime, or Cache architecture.

## Certification

**FAIL**

Storefront CMS Authoring cannot be certified because a primary shipped authoring workflow is nonfunctional, multiple surfaces lack effective concurrency, footer publication is not atomic with its event handoff, seed can return 500 after partial success, dirty/stale DTOs can be published or discarded, validation responsibilities diverge at security and reference boundaries, and focused backend/architecture gates are not green.

This certification stops here. No fixes, redesign, source recovery, Runtime changes, Theme changes, Builder Runtime changes, or Cache changes were performed.
