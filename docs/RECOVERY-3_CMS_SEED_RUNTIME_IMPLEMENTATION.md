# Storefront CMS Authoring Recovery v1.0

## Sprint 3 - CMS Seed Runtime Implementation

**Repository:** PointOfSale  
**Date:** 2026-08-03  
**Scope:** Tenant CMS Seed runtime only  
**Certification:** **PASS WITH OBSERVATIONS**

## Executive Summary

Sprint 3 recovers the tenant CMS Seed runtime behind one execution owner, `CmsSeedService.SeedAsync`.

The HTTP controller now resolves trusted tenant scope and delegates once. The seed owner resolves the required StoreConfig once, detects existing authored state before mutation, creates only missing canonical defaults, persists CMS pages, Navigation, Homepage, and footer under one serializable relational transaction, stages events through the transactional outbox, commits once, and only then returns success.

Repeated requests do not create new page versions, duplicate Navigation links, duplicate Homepage sections, duplicate footer entries, or another StoreConfig. Existing authored aggregates are preserved. A fully seeded tenant receives structured `409/CMS_SEED_ALREADY_COMPLETED` rather than mutation churn.

Homepage authoring, Navigation authoring, footer authoring, StoreConfig authoring, Theme, Builder, Runtime, Projection, and Cache implementations were not changed. The seed runtime writes only its missing canonical defaults and emits the existing projection handoff events.

## Verified Root Cause

The root cause established by `STOREFRONT_CMS_AUTHORING_CERTIFICATION.md` was accepted without re-audit:

- The controller was the seed orchestrator.
- Each CMS page create/update and publish committed independently.
- Navigation draft save and publication committed independently.
- Existing canonical pages were overwritten on every retry.
- `GetNavigationAsync` returned a synthetic RowVersion for a missing StoreConfig while `UpdateNavigationAsync` required a persisted StoreConfig.
- A missing StoreConfig therefore produced HTTP 500 after the page commits had already succeeded.
- Broad exception handling collapsed expected seed failures to `INTERNAL_ERROR`.

The recovered source removes that controller-owned multi-commit path. `AdminCmsPagesController.SeedDefaultPages` now has one call to `ICmsSeedService.SeedAsync` (`PointOfSale.API/Controllers/AdminCmsPagesController.cs:538-569`).

## Execution Pipeline

```text
POST /api/admin/cms/seed
  -> trusted tenant resolution in AdminCmsPagesController
  -> CmsSeedService.SeedAsync (single execution owner)
  -> join existing transaction or begin one serializable transaction
  -> validate tenant existence/lifecycle
  -> resolve and lock the one existing StoreConfig
  -> resolve canonical CMS page slugs and duplicate/conflict state
  -> resolve Navigation footprint from StoreConfig JSON
  -> resolve tenant Homepage footprint
  -> resolve tenant footer footprint
  -> create only missing published CMS pages
  -> create Navigation draft/live only when Navigation is absent
  -> create published Homepage defaults only when Homepage is absent
  -> create footer defaults only when footer is absent
  -> flush all source mutations inside the transaction
  -> stage one event for each actual publication/update
  -> flush transactional outbox state inside the same transaction
  -> commit once when the seed owns the transaction
  -> return AdminCmsSeedResponse
```

Sources: `PointOfSale.API/Services/Admin/CmsSeedService.cs:67-258`; `PointOfSale.API/Controllers/AdminCmsPagesController.cs:538-569`.

## Transaction Boundary

`CmsSeedService.SeedAsync` is the only seed transaction owner.

- A relational provider is required; the seed cannot silently execute without rollback support.
- When no transaction exists, the service begins one serializable transaction and owns its commit/rollback.
- When a transaction already exists, the service joins it and does not begin, commit, roll back, or dispose a nested transaction.
- The PostgreSQL StoreConfig row is loaded with `FOR UPDATE`, serializing concurrent seeds for the same tenant.
- The first `SaveChangesAsync` flushes source state but does not commit.
- Events are staged after source persistence and before commit.
- The second `SaveChangesAsync` flushes outbox state.
- One `CommitAsync` makes source state and outbox state visible together.
- Any validation, persistence, event-stage, or unexpected failure rolls back the owned transaction and clears tracked pending mutations.

Sources: `PointOfSale.API/Services/Admin/CmsSeedService.cs:74-90,226-306,306-322`.

The two SaveChanges calls are flush points inside one transaction, not separate persistence paths or commit boundaries.

## Idempotency Strategy

The canonical duplicate strategy is state-based and customization-preserving:

| Aggregate | Canonical behavior |
|---|---|
| CMS pages | Canonical slugs are `about`, `contact`, `privacy`, and `terms`. One existing published page group claims its slug and is preserved. Only missing slugs are created directly as published version 1. |
| Duplicate CMS content | More than one active page group for a canonical slug returns `409/CMS_SEED_DUPLICATE_CONTENT` without mutation. |
| Draft-only canonical page | Returns `409/CMS_SEED_CONTENT_CONFLICT`; seed does not publish or overwrite administrator content implicitly. |
| Navigation | Any existing Navigation object is preserved as authored. Defaults are created only when Navigation is absent. |
| Homepage | Any active draft or published tenant Homepage section means the aggregate is authored and must be preserved. Defaults are created only when absent. |
| Footer | Any tenant footer section or social link means the aggregate is authored and must be preserved. Defaults are created only when absent. |
| StoreConfig | Seed never creates or duplicates StoreConfig. It requires the tenant's existing authority row. |
| Fully seeded tenant | Returns `409/CMS_SEED_ALREADY_COMPLETED` with no persistence or event publication. |

Seed-owned record and group IDs are deterministic from tenant ID plus canonical key. Event idempotency keys are deterministic from tenant, aggregate, and publication version.

Sources: `PointOfSale.API/Services/Admin/CmsSeedService.cs:112-149,324-363,366-589,727-737`.

## StoreConfig Contract

Sprint 3 chooses one explicit contract: **CMS Seed requires StoreConfig**.

- Fresh tenants created through the normal registration path already have StoreConfig.
- Legacy or incomplete tenants without StoreConfig receive `409/CMS_SEED_STORE_CONFIG_REQUIRED` before any page, Navigation, Homepage, footer, or event mutation.
- Seed does not become a second StoreConfig creation or authoring owner.
- StoreConfig is resolved once by `ResolveStoreConfigAsync` and locked on PostgreSQL.
- Navigation is inserted into the existing customization document without replacing unrelated StoreConfig data.

This removes the former synthetic-read/persisted-write ambiguity without changing StoreConfig authoring.

Source: `PointOfSale.API/Services/Admin/CmsSeedService.cs:104-112,306-322`.

## Error Contract

Expected failures are represented by `CmsSeedException` and mapped by the controller to their declared structured status/code/message response.

| Condition | HTTP | Code |
|---|---:|---|
| Invalid or deleted tenant | 404 | `CMS_SEED_TENANT_NOT_FOUND` |
| Missing StoreConfig | 409 | `CMS_SEED_STORE_CONFIG_REQUIRED` |
| Already seeded | 409 | `CMS_SEED_ALREADY_COMPLETED` |
| Duplicate canonical content | 409 | `CMS_SEED_DUPLICATE_CONTENT` |
| Draft-only canonical content | 409 | `CMS_SEED_CONTENT_CONFLICT` |
| Concurrent seed/authoring or unique conflict | 409 | `CMS_SEED_CONFLICT` |
| Invalid stored JSON or seed admission | 422 | `CMS_SEED_VALIDATION_FAILED` |
| Truly unexpected exception | 500 | `INTERNAL_ERROR` |

Request cancellation is rethrown and is not misclassified as an internal seed failure.

Sources: `PointOfSale.API/Services/Admin/CmsSeedService.cs:28-39,92-150,260-303,324-418,680-718,746-760`; `PointOfSale.API/Controllers/AdminCmsPagesController.cs:555-569`.

## Events

Events are emitted only for aggregates actually created by the seed:

- One `StorefrontCmsPagePublishedEvent` per newly created page.
- One `NavigationPublishedEvent` when Navigation is created.
- One `HomepageSectionsPublishedEvent` when Homepage defaults are created.
- One `StorefrontFooterUpdatedEvent` when footer defaults are created.

Source state is flushed before event staging. Production publication uses the existing MassTransit transactional outbox while the seed transaction is active. A rollback therefore makes neither source state nor outbox messages visible. Repeated/already-completed seeds stage no events, and deterministic idempotency keys protect retry delivery.

Source: `PointOfSale.API/Services/Admin/CmsSeedService.cs:154-236`.

## Files Changed

### Production

- `PointOfSale.API/Services/Admin/CmsSeedService.cs` - new single seed owner, transaction, defaults, duplicate policy, event staging, and seed errors.
- `PointOfSale.API/Controllers/AdminCmsPagesController.cs` - seed endpoint delegates once and maps seed errors.
- `PointOfSale.API/DTOs/Admin/AdminCmsDtos.cs` - typed seed success response.
- `PointOfSale.API/Program.cs` - scoped seed owner registration.

### Tests

- `PointOfSale.Tests/Cms/CmsSeedRuntimeTests.cs` - relational seed behavior, rollback, idempotency, events, and controller error mapping.
- `PointOfSale.Tests/Cms/AdminCmsConflictControllerTests.cs` - constructor setup updated for the seed dependency; existing Navigation recovery tests remain unchanged in behavior.

### Certification

- `RECOVERY-3_CMS_SEED_RUNTIME_IMPLEMENTATION.md`

No migration was added. No frozen Homepage, Navigation, Footer, StoreConfig authoring, Theme, Builder, Runtime, Projection, or Cache implementation file was changed by Sprint 3.

## Regression Tests

`CmsSeedRuntimeTests` and `CmsSeedControllerErrorMappingTests` cover:

- first seed and fresh tenant;
- complete CMS page creation;
- canonical Navigation creation;
- published Homepage creation;
- footer section/link creation;
- repeated and duplicate seed no-op behavior;
- existing tenant/custom content preservation;
- missing StoreConfig;
- invalid tenant;
- duplicate canonical slug;
- draft-only content conflict;
- malformed StoreConfig validation;
- joining an existing transaction without nesting or early commit;
- event-stage failure rollback across every aggregate;
- no successful event on rollback;
- exactly one event per actual aggregate mutation;
- no duplicate event idempotency key;
- structured mappings for already seeded, missing StoreConfig, invalid tenant, duplicate content, conflict, and validation;
- `INTERNAL_ERROR` only for an unexpected exception.

## Validation

| Gate | Result |
|---|---|
| Focused CMS Seed runtime and controller tests | **PASS: 18/18** |
| Related CMS publication, CMS controller, and Navigation admission tests | **PASS: 22/22** |
| Broader selected CMS diagnostic gate | **35/45; all 35 non-fixture tests pass, 10 pre-certified `CmsPageWorkflowTests` fixture failures remain** |
| `npm run build` in `PointOfSale.Storefront` | **PASS: 513 modules transformed** |
| `npm run build` in `PointOfSale.Web` | **PASS: 5066 modules transformed** |
| `dotnet build PointOfSale.sln -c Release --no-restore --verbosity:minimal` | **PASS: 0 errors** |
| Focused `git diff --check` | **PASS; line-ending conversion notices only** |

The 10 broader diagnostic failures are the exact issue already documented in `STOREFRONT_CMS_AUTHORING_CERTIFICATION.md`: `CmsPageWorkflowTests` construct `ApplicationDbContext` without the tenant context required by production CMS query filters, so a created row cannot be reloaded. Sprint 3 did not change `CmsPageService`, those tests, or tenant filter authority.

Existing Storefront build observations remain non-blocking: stale Browserslist data, mixed static/dynamic import notices, and the Vite chunk-size warning.

### Search Proof

Production source counts:

```text
seed_http_initiators=1
seed_service_implementations=1
seed_begin_transaction_sites=1
seed_storeconfig_resolution_calls=1
seed_commit_sites=1
seed_controller_legacy_mutation_loop=0
```

The single service contains two `SaveChangesAsync` flushes inside the same transaction: source persistence followed by outbox persistence. There is no second seed owner, nested seed transaction, StoreConfig creation path, controller persistence path, or post-commit mutation/event path.

## Certification

**PASS WITH OBSERVATIONS**

CMS Seed Runtime is recovered for Sprint 3:

- one execution owner;
- one transaction boundary;
- one required StoreConfig interaction contract;
- atomic source and event persistence;
- deterministic canonical defaults and identities;
- customization-preserving idempotency;
- structured expected failures;
- rollback without partial committed state;
- no duplicate persistence or event path;
- passing focused regression, frontend build, and solution build gates.

The observation is limited to the already certified tenant-context defect in 10 legacy `CmsPageWorkflowTests`. It does not exercise or invalidate the recovered seed owner. No frozen recovery was reopened.
