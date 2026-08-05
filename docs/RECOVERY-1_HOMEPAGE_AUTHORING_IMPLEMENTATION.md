# Recovery 1: Homepage Authoring Implementation

**Repository:** PointOfSale
**Date:** 2026-08-03
**Scope:** Storefront CMS Authoring Recovery 1, Homepage authoring only
**Result:** **PASS WITH OBSERVATIONS**

## Executive Summary

Recovery 1 closes the Homepage data-loss boundaries identified by `STOREFRONT_CMS_AUTHORING_CERTIFICATION.md` without reopening Runtime, Theme Runtime, Builder Runtime, Projection, or Cache architecture.

Homepage authoring now has one backend-owned scope revision, one draft save owner, fail-closed concurrency evidence transport, authoritative response adoption, semantic dirty detection, and explicit conflict recovery. The canonical Builder remains the only editable Homepage workspace; the legacy Manager redirects to it. Preview-only StoreConfig data remains read-only presentation context and cannot become Homepage concurrency evidence.

The Homepage recovery tests and all production builds pass. Broader diagnostic filters retain four failures in frozen or separately scoped code that this recovery did not modify, so the result is `PASS WITH OBSERVATIONS` rather than an unqualified repository-wide pass.

## Frozen Boundary

The following baselines were read before implementation and remain authoritative:

1. `STOREFRONT_CMS_AUTHORING_CERTIFICATION.md`
2. `RECOVERY-0_MARKETPLACE_CMS_VERSION_CONTRACT.md`
3. `FINAL_STOREFRONT_ARCHITECTURE_CERTIFICATION.md`
4. `RECOVERY-1_RUNTIME_OWNERSHIP_IMPLEMENTATION.md`
5. `RECOVERY-2_THEME_RUNTIME_IMPLEMENTATION.md`
6. `RECOVERY-3_BUILDER_RUNTIME_IMPLEMENTATION.md`
7. `RECOVERY-4_BROWSER_RUNTIME_CACHE_IMPLEMENTATION.md`
8. `RECOVERY-5_CACHE_CORE_AND_CLEANUP_IMPLEMENTATION.md`

This implementation does not change Navigation, Footer, tenant CMS Seed, Marketplace CMS, StoreConfig authority, customer Runtime, Theme Runtime, Builder Runtime, Projection, or Cache ownership. It also adds no database migration.

## Recovered Findings

| Certification finding | Recovery |
|---|---|
| `CMS-AUTH-P1-01`: Homepage mutations have no optimistic concurrency | Every authoritative read returns a backend-generated `workspaceVersion`; save, publish, and starter-template mutations require it. Missing evidence returns 428 and stale evidence returns 409. |
| `CMS-AUTH-P1-02`: Whole-workspace save overlaps granular mutations | `PUT /homepage/sections/draft` is the sole draft mutation owner. Granular create, update, delete, and reorder routes return `409/HOMEPAGE_AUTHORING_OWNER_RETIRED`. |
| Same-scope query refresh is not adopted | One frontend function admits and adopts loaded, saved, published, generated, refreshed, and conflict-reloaded snapshots. |
| Candidate state can be replaced without clear ownership | Automatic query adoption occurs only for initial hydration or a clean, idle workspace. Explicit refresh confirms before discarding dirty state. |
| Repeated saves can reuse stale evidence | Every accepted mutation response replaces the stored workspace version used by the next mutation. |
| Homepage preview reads StoreConfig | StoreConfig remains read-only preview context. Its `rowVersion` is not read, generated, stored, or sent by Homepage authoring. |

## Backend Contract

### Workspace version authority

`HomepageAuthoringResult<T>` carries response data and the backend-owned `WorkspaceVersion`. The controller exposes it as `meta.workspaceVersion` on Homepage reads and accepted mutations.

The service computes a scope-specific SHA-256 fingerprint over all authoritative Homepage rows in the selected global or tenant scope. Evidence includes row identity, section group, type, order, serialized data, semantic/schema versions, lifecycle flags, publication/draft identity, and timestamps. Including both active and tombstoned rows means any authoritative workspace transition rotates the version.

The version is not a StoreConfig `xmin`, a client-generated token, or an individual `HomepageSection.Version`. It represents the complete Homepage authoring scope.

Sources:

- `PointOfSale.API/DTOs/Homepage/HomepageSectionDtos.cs:86`
- `PointOfSale.API/SuperAdmin/Services/SuperAdminHomepageSectionManagementService.cs:71-89,396-430,629-653`
- `PointOfSale.API/SuperAdmin/Controllers/SuperAdminHomepageSectionsController.cs:29-48,303-308`

### Mutation preconditions and transactions

Draft save and publish:

- Begin a serializable transaction on relational providers.
- Require `expectedVersion` before mutating the workspace.
- Return `428/HOMEPAGE_WORKSPACE_VERSION_REQUIRED` when evidence is absent.
- Return `409/HOMEPAGE_WORKSPACE_CONFLICT` when evidence is stale.
- Translate EF concurrency and PostgreSQL serialization failures to the same Homepage conflict contract.
- Return the complete accepted snapshot and its successor workspace version.

Sources:

- `PointOfSale.API/SuperAdmin/Services/SuperAdminHomepageSectionManagementService.cs:94-186`
- `PointOfSale.API/SuperAdmin/Services/SuperAdminHomepageSectionManagementService.cs:188-312`
- `PointOfSale.API/SuperAdmin/Services/SuperAdminHomepageSectionManagementService.cs:396-418,540-605`
- `PointOfSale.API/SuperAdmin/Controllers/SuperAdminHomepageSectionsController.cs:126-221`

### Single save owner

The complete workspace draft endpoint owns create, update, delete-by-omission, and ordering. Legacy granular routes remain addressable for compatibility but cannot mutate data and return the structured retired-owner conflict.

`HomepageTemplateSeeder` does not create another persistence pipeline. It delegates starter-template generation through the same versioned `SaveDraftAsync` owner and delegates initial publication through `PublishSectionsAsync`.

Sources:

- `PointOfSale.API/SuperAdmin/Controllers/SuperAdminHomepageSectionsController.cs:62-124,295-301`
- `PointOfSale.API/SuperAdmin/Services/SuperAdminHomepageSectionManagementService.cs:188-312`
- `PointOfSale.API/Services/Homepage/HomepageTemplateSeeder.cs`

## Frontend Contract

### Evidence transport and admission

`homepageService.ts`:

- Requires `meta.workspaceVersion` on every Homepage snapshot response.
- Fails closed when a response or mutation lacks version evidence.
- Sends `expectedVersion` on save, publish, and starter-template requests.
- Classifies only `409/HOMEPAGE_WORKSPACE_CONFLICT` as a recoverable Homepage concurrency conflict.
- Keeps the StoreConfig preview request read-only and tenant-scoped.

Sources:

- `PointOfSale.Web/src/super-admin/homepage/homepageService.ts:43-47,135-166`
- `PointOfSale.Web/src/super-admin/homepage/homepageService.ts:174-227`
- `PointOfSale.Web/src/super-admin/homepage/homepageService.ts:234-258`

### Authoritative adoption and recovery

`HomepageBuilderPage` keeps authoring closed until a scope-valid, version-bearing snapshot is admitted. `adoptAuthoritativeSnapshot` is the single state adoption function and replaces:

- Candidate sections.
- Saved semantic baseline.
- Removed-section tracking.
- Selected-section identity where possible.
- Accepted workspace version.
- Hydrated scope identity.
- React Query preview cache state.

Save is a no-op when the semantic workspace is unchanged. Publish requires a clean accepted draft. Reorder remains local candidate state until Save Draft. Every accepted save, publish, and starter-template response is adopted immediately. Exact conflicts trigger an authoritative reload; a failed conflict reload clears accepted evidence so later mutations fail closed.

Sources:

- `PointOfSale.Web/src/super-admin/homepage/HomepageBuilderPage.tsx:430-486`
- `PointOfSale.Web/src/super-admin/homepage/HomepageBuilderPage.tsx:510-520,575-630`
- `PointOfSale.Web/src/super-admin/homepage/HomepageBuilderPage.tsx:632-697`
- `PointOfSale.Web/src/super-admin/homepage/HomepageBuilderPage.tsx:833-929`

### Dirty and preview isolation

Dirty state compares the semantic candidate fingerprint with the last accepted semantic baseline and separately tracks removed persisted identities. Backend timestamps, response versions, and preview-only StoreConfig changes do not make the Homepage dirty. Explicit refresh requires confirmation before replacing a dirty candidate, and browser unload is guarded while dirty.

StoreConfig is used only to render tenant preview context. Homepage authoring contains no `rowVersion` or `RowVersion` reference.

Sources:

- `PointOfSale.Web/src/super-admin/homepage/HomepageBuilderPage.tsx:417-439,530-542,683-697`
- `PointOfSale.Web/src/super-admin/homepage/homepageBuilderUtils.test.ts`
- `PointOfSale.Web/src/super-admin/homepage/HomepageBuilderPage.test.tsx`

### One editable page

The former `HomepageManagerPage` no longer owns query or mutation state. It redirects to `/super-admin/homepage`, leaving `HomepageBuilderPage` as the canonical editable surface.

Source: `PointOfSale.Web/src/super-admin/homepage/HomepageManagerPage.tsx:1-5`.

## Files Modified

### Backend production

- `PointOfSale.API/DTOs/Homepage/HomepageSectionDtos.cs`
- `PointOfSale.API/SuperAdmin/Controllers/SuperAdminHomepageSectionsController.cs`
- `PointOfSale.API/SuperAdmin/Services/SuperAdminHomepageSectionManagementService.cs`
- `PointOfSale.API/Services/Homepage/HomepageTemplateSeeder.cs`

### Frontend production

- `PointOfSale.Web/src/super-admin/homepage/homepageService.ts`
- `PointOfSale.Web/src/super-admin/homepage/HomepageBuilderPage.tsx`
- `PointOfSale.Web/src/super-admin/homepage/HomepageManagerPage.tsx`

### Regression tests

- `PointOfSale.Tests/SuperAdmin/SuperAdminHomepageSectionManagementServiceTests.cs`
- `PointOfSale.Web/src/super-admin/homepage/homepageService.test.ts`
- `PointOfSale.Web/src/super-admin/homepage/homepageBuilderUtils.test.ts`
- `PointOfSale.Web/src/super-admin/homepage/HomepageBuilderPage.test.tsx`
- `PointOfSale.Web/src/super-admin/homepage/HomepageManagerPage.test.tsx`

## Regression Coverage

The backend behavior gate proves:

- Initial global and tenant reads issue version evidence.
- Complete draft replacement preserves the published Homepage.
- First save rotates the loaded version.
- A repeated save uses and rotates the preceding response version.
- Replaying stale evidence is rejected without replacing the accepted draft.
- Missing evidence is rejected with 428.
- Publish and starter-template flows use the same versioned owners.

The frontend behavior gate proves:

- Authoring remains closed until a versioned snapshot loads.
- Missing response or mutation evidence fails closed.
- Save transports the loaded version and adopts the successor version.
- Repeated saves use each preceding response version.
- Semantically unchanged state does not issue a save.
- Publish and starter-template requests use current evidence.
- Exact conflicts reload authoritative state.
- Explicit refresh replaces state only through authoritative adoption.
- Preview-only StoreConfig changes do not alter Homepage dirty state or version ownership.
- The legacy Manager redirects to the Builder.

## Search Proof

Focused production-source searches produced:

```text
frontend_bulk_save_callers=1
frontend_publish_callers=1
frontend_template_callers=1
frontend_homepage_rowVersion_references=0
backend_granular_service_mutation_callers=0
backend_draft_owner=SuperAdminHomepageSectionManagementService.SaveDraftAsync
legacy_manager_editable_implementation=0
```

The backend template seeder and HTTP controller are clients of the same management service owner; they do not directly persist Homepage rows through separate pipelines.

## Validation Results

| Validation | Result |
|---|---|
| Four focused Homepage frontend files | **PASS: 21/21** |
| StoreConfig frontend service regression | **PASS: 9/9** |
| `SuperAdminHomepageSectionManagementServiceTests`, fresh Release binary | **PASS: 16/16** |
| Broad backend `Homepage` filter | **40/42; all 16 recovery service tests pass, two out-of-scope architecture assertions fail** |
| Focused StoreConfig backend filter | **23/25; two previously identified out-of-scope failures remain** |
| `PointOfSale.API` production build | **PASS** |
| `PointOfSale.Web` production build | **PASS** |
| `PointOfSale.Storefront` production build | **PASS** |
| `dotnet build PointOfSale.sln -c Release --no-restore --verbosity:minimal` | **PASS: 0 errors; existing analyzer warnings remain** |
| Focused `git diff --check` | **PASS; line-ending conversion warnings only** |

## Observations

The broad Homepage filter retains two source-string architecture failures in `HomepageDiscoveryRecommendationCertificationTests`:

- `ProductCacheInvalidationRetainsHomepageReplayCollapse` still requires `CacheKeyBuilder.HomePatternKey`, which Recovery 5 intentionally removed with the dormant Homepage-section cache family.
- `StorefrontHomepageShelvesStaySqlAuthorityBound` still requires a specific `ProductVariantInventoryLineageAuthority` call shape in frozen Storefront runtime code.

Neither failing assertion exercises Homepage authoring, and this recovery changed neither the assertion file nor the frozen cache/runtime production files.

The focused StoreConfig filter retains two failures:

- `AdminStoreConfigRollbackSafetyTests.RollbackStoreConfigAsync_PreservesLatestDraftWrittenByAnotherAdmin` supplies a `hero-banner` fixture without settings. This exact strict-admission fixture failure is already recorded in `STOREFRONT_CMS_AUTHORING_CERTIFICATION.md`.
- `AdminStoreConfigThemeContractTests.ActivateThemeAsync_ChangesOnlyStorefrontThemeAndPublishesProjectionEvent` expects an older Minimal theme primary token (`#111827`) while the current frozen theme authority returns `#0f172a`.

No StoreConfig service, StoreConfig test, Theme authority, Homepage Storefront runtime, or cache implementation was changed for this recovery. Those failures are reported rather than repaired across the frozen scope boundary.

The first Release solution build also exposed an optional-parameter compile regression on global `GetSectionsAsync()` calls. Restoring the legitimate `tenantId = null` default on the interface and implementation resolved it; the final Release solution build and fresh 16-test service gate pass.

## Final Certification

**PASS WITH OBSERVATIONS**

The Homepage authoring data-loss boundary is recovered within the authorized scope:

- One backend-owned complete-workspace version governs reads and mutations.
- Missing and stale concurrency evidence fail with distinct structured contracts.
- Draft persistence has one owner.
- The canonical Builder adopts every accepted authoritative response.
- Repeated saves cannot silently reuse stale evidence.
- Dirty, refresh, conflict, and scope transitions preserve explicit candidate ownership.
- StoreConfig preview context cannot become Homepage version authority.
- The legacy Manager cannot act as a second editor.

This result certifies the Homepage portion of Storefront CMS Authoring Recovery 1 only. It does not certify Footer concurrency/atomicity, tenant CMS dirty-state behavior, StoreConfig/Builder focus behavior, Seed Safety, Admission and Error Contracts, or the complete Storefront CMS Authoring subsystem.
