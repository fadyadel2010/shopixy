# Storefront CMS Authoring Recovery v1.0

## Sprint 2 — Navigation Authoring Implementation

Date: 2026-08-03  
Scope: Navigation authoring only  
Certification: **PASS WITH OBSERVATIONS**

## Executive Summary

Sprint 2 recovers Navigation authoring behind one canonical admission contract.

Category, product, and CMS-page links now use backend-issued GUID identities. Slugs and paths are backend-resolved routing locators, never authoring identities. External, collection, and custom links also enter the same admission pipeline. Draft save and publish share that pipeline, with publish applying the stricter publication rules.

The frontend admits backend responses before placing them in authoring state, obtains selectable targets from a Navigation-specific backend endpoint, performs only obvious shape and URL/path checks, maps the draft through one DTO mapper, and adopts every successful save or publish response. The backend remains authoritative for entity existence, deletion, tenant ownership, storefront visibility, publication, route uniqueness, and collection availability.

No Homepage, Footer, CMS Seed, StoreConfig authoring, Theme, Builder, Runtime, Projection, or Cache recovery was reopened. The worktree already contained changes in those frozen areas before this sprint; they were preserved.

## Verified Root Cause

The primary certification findings were accepted without re-audit:

- Category authoring could emit a GUID through the `slug` field while backend validation treated the same field as a generated route slug.
- Navigation validation was split across frontend shape assumptions and multiple backend helper methods.
- Product, collection, custom, external, category, and CMS-page resolution did not share one identity and lifecycle contract.
- Save and publish failures did not consistently produce an authoring-specific structured error; publish admission failures could fall through as generic failures.
- Load failures could leave an editable fallback with no usable backend `RowVersion`.
- Successful mutations were not required to be admitted and adopted as the next authoring snapshot.

The root cause was contract ambiguity: authoring identity, runtime locator, validation ownership, persistence mapping, and response adoption were not separated.

## Canonical Navigation Contract

All authoring link types use the following common fields:

- `label` and optional `labelAr`
- canonical `type`
- type-specific identity
- `enabled`
- `openInNewTab`, effective only for external links
- order derived from array position

| Link type | Canonical authoring identity | Backend-owned locator | Authoritative admission |
|---|---|---|---|
| `category` | `targetId = Category.Id` | generated category slug | same tenant, exists, not deleted, live category lineage, unique storefront route |
| `product` | `targetId = Product.Id` | product slug and `/products/{slug}` | same tenant, exists, not deleted, active, online-visible, published, non-empty slug, live category lineage |
| `cms` | `targetId = CmsPage.PageGroupId` | current admitted CMS slug | same tenant and active page group; draft admission accepts an active draft or publication; publish admission requires a published, non-draft version with `PublishedAt` |
| `external` | absolute URL in `path` | same admitted URL | HTTP or HTTPS, no credentials or control characters, maximum 2048 characters |
| `collection` | backend-issued `collectionKey` | registered collection path | key must exist in the backend collection registry; v1 exposes `all-products` as `/products` |
| `custom` | storefront-relative `path` | same admitted path | begins with one `/`, no backslash/control characters, maximum 2048 characters, and cannot address admin/API/preview surfaces |

### Identifier ownership

- GUID is the only canonical identifier for category, product, and CMS-page authoring.
- Category slug, product slug, CMS slug, and resolved path are routing evidence owned by the backend.
- The selected store is the tenant-owned `StoreConfig` row loaded and written by tenant ID under the existing `xmin` concurrency contract.
- Target queries are tenant-scoped. A specifically requested foreign GUID is loaded only so the authoritative error can distinguish `WRONG_TENANT` from `NOT_FOUND`; it is never admitted.
- A legacy category GUID placed in `slug`, an unambiguous legacy category route slug, a legacy CMS slug, and legacy `internal` type can be normalized only at the admission boundary. Canonical responses no longer expose those legacy identities.
- Ambiguous legacy category slugs and duplicate derived category routes are rejected. A GUID cannot override an ambiguous frozen runtime route.

## Admission Pipeline

The recovered pipeline is:

```text
Load tenant Navigation
  -> decode frozen StoreConfig JSON through NavigationStorageAdapter
  -> admit draft with NavigationAdmissionMode.Draft
  -> admit live with NavigationAdmissionMode.Publish
  -> return canonical Navigation snapshot + backend RowVersion
  -> frontend response admission
  -> authoring draft
  -> edit or select a backend-issued target
  -> obvious frontend validation
  -> mapNavigationDraftToRequest (single DTO mapper)
  -> PUT /admin/cms/navigation
  -> NavigationAdmissionService.AdmitAsync (authoritative draft admission)
  -> NavigationStorageAdapter (mechanical persistence only)
  -> save with existing StoreConfig concurrency
  -> return complete canonical snapshot
  -> frontend response admission and adoption
  -> POST /admin/cms/navigation/publish
  -> the same AdmitAsync pipeline in Publish mode
  -> publish + outbox under the existing transaction/concurrency contract
  -> return, admit, and adopt the published snapshot
```

Every header and footer item enters `NavigationAdmissionService.AdmitAsync`. Load, save, and publish do not contain alternate business validators.

The editor fails closed when load response admission fails. It does not create an editable empty Navigation, and it offers an explicit retry.

## Validation Ownership

### Frontend

The frontend prevents only obviously invalid authoring:

- missing/oversized label;
- missing or empty backend GUID for entity-backed links;
- missing collection key;
- malformed or oversized custom relative path;
- malformed, credential-bearing, control-character, oversized, or non-HTTP(S) external URL;
- malformed backend target-selection responses;
- missing/zero backend `RowVersion` or malformed mutation response.

The frontend does not decide tenant ownership, deletion, visibility, category lineage, publication, route uniqueness, or collection availability.

### Backend

`NavigationAdmissionService` is the sole business admission owner. It is authoritative for:

- request/menu shape, menu limits, labels, type support, and duplicate targets;
- category identity, deletion, tenant, lineage visibility, and route uniqueness;
- product identity, deletion, tenant, active/online/published visibility, slug, and category lineage;
- CMS page-group identity, deletion, tenant, draft eligibility, and publish eligibility;
- external URL safety;
- custom storefront-path safety;
- registered collection keys.

`NavigationStorageAdapter` contains no entity or lifecycle rules. It only bridges the canonical authoring contract to the frozen StoreConfig/runtime representation.

### Concurrency

The existing StoreConfig `xmin`/`RowVersion`, `If-Match`, 428 precondition, 409 conflict, outbox, and transaction behavior remains authoritative and unchanged in ownership. Navigation conflict classification now requires the exact 409 plus `CONCURRENCY_CONFLICT` contract.

## Error Contract

Every Navigation admission failure is a `NavigationAdmissionException` mapped to HTTP 422:

```json
{
  "code": "NAVIGATION_CATEGORY_DELETED",
  "message": "Category 'Example' was deleted.",
  "traceId": "...",
  "navigation": {
    "linkType": "category",
    "menu": "header",
    "index": 0,
    "field": "targetId",
    "targetId": "..."
  }
}
```

Canonical codes cover invalid shape, menu limit, missing/long label, unsupported type, duplicate target, missing/deleted/hidden/ambiguous category, missing/deleted/hidden product, missing/deleted/unpublished CMS page, wrong tenant, invalid external URL, unavailable collection, and invalid custom path.

GET, PUT, and publish use the same structured error representation. The frontend extracts this representation and presents its message in load, save, or publish feedback without reclassifying backend business rules.

## DTO Contract

### Mutation request

`AdminNavigationRequest` contains:

- `rowVersion`
- `header[]`
- `footer[]`

Each link contains `label`, `labelAr`, `type`, `targetId`, `collectionKey`, `path`, `openInNewTab`, `order`, and `enabled`. The frontend never submits entity slugs.

### Snapshot response

`AdminNavigationResponse` contains:

- authoritative `rowVersion`;
- canonical `draft`;
- canonical optional `live`;
- `hasPendingChanges`.

The frontend admits and adopts the complete response after load, save, and publish. It does not patch local state with the submitted request.

### Target selection

`GET /admin/cms/navigation/targets` returns tenant-scoped `categories`, `products`, `cmsPages`, and `collections`. Every option contains its canonical identity, display label, backend-resolved path, and state. The editor no longer derives category identity from a general category tree or permits manual entity slugs.

### Frozen storage bridge

The storage adapter retains `authoringType`, `targetId`, and `collectionKey`, while emitting the runtime-compatible `type`, `slug`, and `path` fields expected by the frozen consumer:

| Authoring type | Frozen runtime type |
|---|---|
| category | category |
| CMS page | cms |
| external | external |
| product | internal |
| collection | internal |
| custom | internal |

This is a storage compatibility boundary, not a second DTO mapper or validation owner.

## Files Changed

### Backend production

- `PointOfSale.API/DTOs/Admin/AdminCmsDtos.cs`
- `PointOfSale.API/Exceptions/StorefrontAdminExceptions.cs`
- `PointOfSale.API/Services/Admin/IAdminCmsService.cs`
- `PointOfSale.API/Services/Admin/AdminCmsService.cs`
- `PointOfSale.API/Services/Admin/NavigationAdmissionService.cs` (new)
- `PointOfSale.API/Services/Admin/NavigationStorageAdapter.cs` (new)
- `PointOfSale.API/Controllers/AdminCmsPagesController.cs`
- `PointOfSale.API/Program.cs`

### Frontend production

- `PointOfSale.Web/src/modules/storefront/cms/services/navigationAuthoringContract.js` (new)
- `PointOfSale.Web/src/modules/storefront/cms/services/cmsService.js`
- `PointOfSale.Web/src/modules/storefront/cms/services/index.js`
- `PointOfSale.Web/src/modules/storefront/cms/pages/NavigationEditor.jsx`

### Regression and authority tests

- `PointOfSale.Tests/Admin/NavigationAdmissionServiceTests.cs` (new)
- `PointOfSale.Tests/Admin/AdminCmsPublishReliabilityTests.cs`
- `PointOfSale.Tests/Cms/AdminCmsConflictControllerTests.cs`
- `PointOfSale.Tests/Architecture/CmsEndToEndAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/StorefrontNavigationAuthorityCertificationTests.cs`
- `PointOfSale.Web/src/modules/storefront/cms/services/navigationAuthoringContract.test.ts` (new)

Shared files were changed only in their Navigation sections or for dependency registration/test construction. No frozen seed, homepage, footer, runtime, projection, theme, builder, cache, or StoreConfig-authoring behavior was modified by Sprint 2.

## Regression Tests

Coverage includes:

- all six canonical link types in one request;
- category GUID resolution and backend-owned slug;
- hidden lineage, deleted, missing, legacy-slug ambiguity, and derived-route ambiguity for categories;
- product GUID resolution, hidden, missing, deleted, and category-lineage visibility;
- CMS page-group GUID resolution, draft-save eligibility, publish eligibility, unpublished rejection, and deleted-page rejection;
- wrong-tenant target rejection;
- invalid external schemes, protocol-relative URLs, credential-bearing URLs, and oversized frontend URLs;
- collection-key and custom-path mapping;
- target-selection filtering;
- legacy load normalization into canonical identity;
- storage round-trip and runtime compatibility mapping;
- successful save persistence and complete response;
- rejected save with no persistence mutation;
- structured 422 update and publish errors;
- exact conflict classification;
- single DTO mapping;
- save and publish response adoption with rotated `RowVersion`.

## Validation

### Scoped Navigation validation

- Backend Navigation tests: **PASS**, 24 passed, 0 failed.
- Frontend Navigation contract/service tests: **PASS**, 8 passed, 0 failed.
- Selected Navigation write/public authority tests: **PASS**, 2 passed, 0 failed; included in the 24-test scoped run.

### Storefront regression validation

- `PointOfSale.Storefront` test suite: **PASS**, 25 files and 234 tests passed.
- `PointOfSale.Storefront` production build: **PASS**, TypeScript and Vite completed; 513 modules transformed. Existing dynamic/static import and chunk-size warnings remain.

### Build validation

- `PointOfSale.Web` production build: **PASS**, 5066 modules transformed.
- `PointOfSale.sln` Release build with `--no-restore`: **PASS**, 0 errors and 200 existing warnings.
- Focused `git diff --check`: **PASS**; only repository line-ending conversion notices were emitted.

### Search proof

- Exactly one frontend save initiator: one `cmsService.updateNavigation(...)` call in `NavigationEditor.jsx`.
- Exactly one backend update controller owner: one `UpdateNavigation(...)` action.
- Exactly one backend persistence owner: one `UpdateNavigationAsync(...)` implementation.
- Exactly one Navigation DTO mapper: one `mapNavigationDraftToRequest(...)` declaration.
- Exactly one business admission owner: one `NavigationAdmissionService.AdmitAsync(...)` declaration.
- Exactly one frozen-storage adapter: one `NavigationStorageAdapter` declaration.
- Draft load, live load, save, and publish all call that same admission owner.
- Production search returned no `ValidateNavigationMenu`, `ValidateNavigationLink`, `GetPublishedCmsPageSlugsAsync`, `InvalidNavigationLinkException`, or superseded `NavigationAuthoringMapper` owner.

## Observations

1. The combined legacy CMS/runtime architecture fixture currently reports 6 passes and 2 failures. Both failures assert literals in frozen, pre-existing recovery scopes: `InvalidatePublishedScopeAsync` in the recovered Cache/CMS read path and `headerLinks.map` in the recovered Storefront runtime header. They are not Navigation-authoring failures. Per the sprint boundary, Cache and Runtime were not altered to satisfy stale source-literal assertions.
2. The frozen runtime has no separate collection aggregate/route registry. Navigation v1 therefore exposes the backend-owned `all-products` collection only. Future collection types must be added to the same backend registry and admission pipeline; the frontend must not invent them.
3. When no tenant `StoreConfig` with a usable positive `RowVersion` exists, the editor now fails closed instead of editing an unsaveable fallback. Creating or seeding that row belongs to frozen CMS Seed/StoreConfig authority and was intentionally not added here.

## Certification

Navigation authoring now has one canonical identifier contract, one admission pipeline, one DTO mapper, one save owner per boundary, authoritative backend validation, complementary frontend validation, structured errors, response adoption, and regression coverage for every required link class and failure class.

The authorized Navigation scope is recovered. The observations are confined to frozen scopes or explicit v1 boundaries and do not introduce an alternate Navigation authority.

**PASS WITH OBSERVATIONS**
