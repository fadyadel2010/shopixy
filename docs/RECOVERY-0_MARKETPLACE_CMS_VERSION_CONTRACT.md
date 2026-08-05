# Recovery 0: Marketplace CMS Version Contract

**Repository:** PointOfSale  
**Date:** 2026-08-03  
**Scope:** Storefront CMS Authoring Recovery Sprint 0 only  
**Certification:** **PASS**

## Root Cause

The backend already enforced the correct immutable semantic-version contract:

- Create produces draft version 1.
- Update requires `MarketplaceCmsPageRequest.ExpectedVersion` and returns a new immutable draft version.
- Publish requires `expectedVersion` query or `If-Match` evidence and publishes that exact draft version without incrementing it.
- Delete requires the current draft version, or the current published version when no draft exists.

The Marketplace CMS frontend loaded and retained the version-bearing DTOs but discarded their version evidence at every mutation boundary:

- Editor detail responses contained `draft.version` or `published.version`.
- List responses contained `draftVersion` and `publishedVersion`.
- Update sent no `expectedVersion` in the request body.
- Publish sent no query or header version.
- Delete sent no query or header version.
- Successful mutation responses were either retained without being used by the next command or discarded before a list refresh.

This made update, publish, and delete deterministically fail the backend concurrency check.

## Implementation

### Transport

`PointOfSale.Web/src/super-admin/marketplace-cms/marketplaceCmsService.js` now:

- Requires a positive backend-issued version before update, publish, or delete.
- Sends update evidence as body `expectedVersion`, matching the existing request DTO.
- Sends publish and delete evidence through the existing `expectedVersion` query contract.
- Returns the delete response instead of discarding it.
- Resolves the current detail version draft-first, then published.
- Resolves the current list version draft-first, then published.
- Distinguishes the backend concurrency error from other HTTP 409 errors.
- Fails locally without issuing a mutation when current version evidence is unavailable.

No API route, DTO, immutable version rule, backend concurrency rule, publication transaction, event flow, Runtime, Builder, Theme, or Cache architecture changed.

### Editor State And Response Adoption

`PointOfSale.Web/src/super-admin/marketplace-cms/MarketplaceCmsPageEditor.jsx` now:

- Uses the loaded detail response version for an existing-page update.
- Replaces page state with every create and update response.
- Uses the newly adopted update/create response version for the next update or publish.
- Replaces page state with the publish response, including its published version.
- Reloads and adopts authoritative page/form state after an exact Marketplace CMS concurrency conflict.
- Clears stale page evidence if the conflict refresh itself fails, causing later mutations to fail closed.

### List State, Delete, And Refresh

`PointOfSale.Web/src/super-admin/marketplace-cms/MarketplaceCmsPagesPage.jsx` now:

- Publishes with the row's current draft version.
- Immediately maps the publish response back to list state, including the returned draft/published version fields.
- Deletes with the draft version when present and otherwise the published version.
- Adopts a successful delete response by removing the deleted page from state. The preserved backend delete DTO does not return a successor version because no active page remains.
- Uses versions returned by an explicit list refresh for the next mutation.
- Reloads authoritative list state after a concurrency conflict.
- Clears list rows when refresh fails so stale rows cannot issue later mutations.
- Disables publish when the row has no draft.

## Regression Tests

### Frontend Service Contract

`PointOfSale.Web/src/super-admin/marketplace-cms/marketplaceCmsService.test.ts`

- Create sends no client-generated version and adopts draft version 1.
- First update sends version 1 and adopts version 2.
- Repeated update sends version 2 and adopts version 3.
- Publish sends version 3 and adopts published version 3.
- Delete sends published version 3 and adopts the delete response.
- Missing/non-positive evidence fails closed without an HTTP mutation.
- Only `MARKETPLACE_CMS_CONCURRENCY_CONFLICT` is treated as a version conflict.

### Frontend Workflow State

`PointOfSale.Web/src/super-admin/marketplace-cms/MarketplaceCmsPageEditor.test.tsx`

- Immediate create-to-publish uses the create response's version.
- Repeated updates use each preceding response's new version.
- Publish uses the latest repeated-update response version.
- A 409 conflict refreshes the page and the next update uses the refreshed version.

`PointOfSale.Web/src/super-admin/marketplace-cms/MarketplaceCmsPagesPage.test.tsx`

- Publish response adoption supplies the current published version to delete.
- Manual refresh replaces the row version used by the next publish.
- Successful delete removes the returned page identity from frontend state.

### Backend Lifecycle

`PointOfSale.Tests/Cms/MarketplaceCmsPageWorkflowTests.cs`

The lifecycle regression proves:

```text
Create v1
  -> Update(expected v1) returns v2
  -> Update(expected v2) returns v3
  -> stale Update(expected v2) is rejected and v3 remains current
  -> Publish(expected v3) returns published v3
  -> Delete(expected published v3)
  -> no active draft or published version remains
```

## Validation

| Gate | Result |
|---|---|
| `npx vitest run` for the three Marketplace CMS Sprint 0 frontend test files | **PASS: 9/9** |
| `dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --filter FullyQualifiedName~PointOfSale.Tests.Cms.MarketplaceCmsPageWorkflowTests --no-restore` | **PASS: 10/10** |
| `dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --filter FullyQualifiedName~MarketplaceCms --no-restore` | **21/22; Sprint 0 tests pass. One pre-certified out-of-scope Marketplace seed test fails.** |
| `npm run build` in `PointOfSale.Web` | **PASS** |
| `dotnet build PointOfSale.sln -c Release --no-restore` | **PASS: 0 errors** |
| Focused `git diff --check` | **PASS**; line-ending conversion warnings only |
| Focused ESLint invocation | **NOT RUN**; ESLint 9 exits because `PointOfSale.Web` has no `eslint.config.js`, `eslint.config.mjs`, or `eslint.config.cjs` |

The broader Marketplace filter's single failure is `FooterSystemTests.MarketplaceCmsPageSeeder_SeedsDefaultPagesOnlyOnce`. `STOREFRONT_CMS_AUTHORING_CERTIFICATION.md` already identifies that fire-and-forget Marketplace seed behavior and failing test as Recovery 2 scope. Sprint 0 did not modify seed code or its test.

The Release solution build completed with existing analyzer/compiler warnings and zero errors.

## Certification

**PASS**

The Marketplace CMS page version contract is recovered for Sprint 0.

An existing page can update, adopt the returned version, update again, publish with the latest version, adopt the published version, and delete with current evidence. Stale evidence is rejected by the backend; the frontend refreshes authoritative state after an exact concurrency conflict and fails closed when current evidence cannot be loaded.

No Homepage, Navigation, Footer, CMS Seed, StoreConfig, Builder, Theme, Cache, or Runtime source was changed.
