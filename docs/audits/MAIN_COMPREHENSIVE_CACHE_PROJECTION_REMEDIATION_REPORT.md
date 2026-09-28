# MAIN Comprehensive Cache and Projection Remediation Report

Date: 2026-09-28  
Repository: `PointOfSale`  
Branch: `main`  
Reviewed commit: `13c7afd` plus the current uncommitted MAIN worktree  
Scope: MAIN application projects and tests only

## Verdict

**Scoped cache/projection remediation: PASS.**

**Repository release verdict: NO-GO.**

The remediated concurrency, tenant-authority, product-list freshness, deterministic-key, Redis-write, invalidation, and projection-governance suites pass. The complete test project did not finish within 15 minutes and emitted unrelated existing failures, including PostgreSQL schema drift. MAIN should not receive an overall release GO until those failures are resolved and the complete suite finishes successfully.

## Evidence Labels

- **SOURCE-PROVEN**: established by direct inspection of the MAIN source.
- **TEST-PROVEN**: established by a passing automated test in this worktree.
- **BUILD-PROVEN**: established by a successful Release build.
- **PARTIAL**: useful evidence exists, but a production-equivalent dependency or the complete suite was not successfully exercised.
- **BLOCKED**: validation failed or did not complete for a reason outside this remediation scope.

## Authority Model

- **SOURCE-PROVEN** PostgreSQL remains the authority for tenant lifecycle, product identity, visibility, publication, category lineage, affiliate eligibility, and product mutation versions.
- **SOURCE-PROVEN** MongoDB remains a derivative product projection. Product consumers invoke `IProductSqlAuthorityProjectionWriter`, which recomposes current PostgreSQL truth before applying a version-fenced MongoDB mutation.
- **SOURCE-PROVEN** Redis remains optional acceleration. Product cache reads are generation-owned and public list results are recomposed against current SQL authority before return.

## Remediated Findings

### 1. Concurrent Request Normalization

Previous risk: a shared `NullabilityInfoContext` could expose its mutable reflection cache to concurrent requests.

Remediation:

- `ApiRequestNormalizationFilter` now caches immutable required-member decisions in a `ConcurrentDictionary<ICustomAttributeProvider, bool>`.
- A local `NullabilityInfoContext` is created only during one-time metadata computation.

Evidence:

- **SOURCE-PROVEN** `PointOfSale.API/Infrastructure/Mvc/ApiRequestNormalizationFilter.cs`
- **TEST-PROVEN** `ApiRequestNormalizationFilterConcurrencyTests`

### 2. Scoped Tenant Authority Memoization

Previous risk: repeated tenant lifecycle checks performed redundant SQL reads and could diverge if callers implemented lifecycle predicates independently.

Remediation:

- `TenantAuthorityMemo` memoizes active tenant authority per `ApplicationDbContext` using a `ConditionalWeakTable`.
- The canonical load requires `IsActive`, `Status == TenantStatus.Active`, and `DeletedAtUtc == null`.
- Faulted or cancelled loads are evicted; no verdict is shared across scoped contexts.
- `TenantLineageAuthority.IsActiveTenantAsync` delegates to this canonical memo.

Evidence:

- **SOURCE-PROVEN** `PointOfSale.Infrastructure/Tenancy/TenantAuthorityMemo.cs`
- **SOURCE-PROVEN** `PointOfSale.Infrastructure/Reliability/TenantLineageAuthority.cs`
- **TEST-PROVEN** tenant/POS/JWT validation group: 37 passed, 0 failed
- **TEST-PROVEN** `TenantAuthorityMemoTests` proves one read per scope and no cross-scope verdict leakage

### 3. Deterministic Product Cache Keys

Previous risk: `ProductListQuery.GetQueryHash()` used process-randomized `string.GetHashCode()`, so distributed cache identity was not stable across processes or restarts.

Remediation:

- Product-list identity now uses `CacheKeyFilterNormalizer.NormalizeAndHash`.
- Canonical input is invariant, ordered, length-prefixed, and preserves explicit false/default values.
- Decimal values use invariant `G29` formatting.
- Hashes use full SHA-256 and the `h2-` schema marker.
- Product-list keys use the new `products:list:v2` namespace. Old entries are not interpreted as current entries and expire naturally.

Evidence:

- **SOURCE-PROVEN** `PointOfSale.API/DTOs/Storefront/StorefrontDtos.cs`
- **SOURCE-PROVEN** `PointOfSale.Infrastructure/Redis/CacheKeyFilterNormalizer.cs`
- **SOURCE-PROVEN** `PointOfSale.Infrastructure/Redis/CacheKeyBuilder.cs`
- **TEST-PROVEN** deterministic equality, culture independence, explicit-false identity, page identity, schema identity, and tenant isolation in `ComprehensiveCacheRemediationTests`

### 4. SQL-Authoritative Product List Membership and Order

Previous risk: validating only products returned by MongoDB could not detect a newly visible SQL product missing from the projection, a stale total count, or stale page order.

Remediation:

- `StorefrontProductService` queries the current SQL-visible product count and page IDs using tenant, lifecycle, publication, category, and affiliate lineage constraints.
- MongoDB candidates are reordered to SQL page order.
- Missing MongoDB candidates receive identity-only placeholders and are recomposed by the SQL overlay.
- The response uses the SQL-authoritative total count.
- Cache population is rejected unless projection count and membership are complete and revision evidence is complete.
- Warm cache hits are rebuilt against current SQL page membership before public return.

Evidence:

- **SOURCE-PROVEN** `PointOfSale.API/Services/Storefront/StorefrontProductService.cs`
- **TEST-PROVEN** `ProductList_MissingProjectedMember_ReturnsCurrentSqlPageAndRejectsCachePopulation`
- **TEST-PROVEN** product/cache focused group: 72 passed, 0 failed

### 5. Revision-Based Freshness

Previous risk: wall-clock lag alone cannot prove that every projected product reflects the current SQL mutation generation.

Remediation:

- Cache envelopes and population results carry optional `ProjectionVersion` and `SourceVersion`.
- If either revision is supplied, both are required and a projection behind its source is bypassed.
- Product lists compare each document's `LastEventVersion` with the event version derived from current SQL `ProductGeneration`.
- Timestamp lag remains a fallback only for cache families that do not yet supply revision proof.

Evidence:

- **SOURCE-PROVEN** `PointOfSale.Infrastructure/Redis/Models/CacheEnvelope.cs`
- **SOURCE-PROVEN** `PointOfSale.Infrastructure/Redis/Services/RedisJsonCacheService.cs`
- **TEST-PROVEN** `ProductListCacheFreshnessGuardTests`
- **TEST-PROVEN** caught-up, lagging, overlay-restamp, and mixed-page cases in `ProductSearchSqlAuthorityBatchTests`

### 6. Generation-Only Product Invalidation

Previous risk: request-path product invalidation performed broad Redis pattern scans and explicit key deletion in addition to generation advancement.

Remediation:

- Product invalidation now advances only the tenant-scoped storefront-products generation.
- Product detail, slug, list, home, and lookup-validation reads use that generation as their ownership fence.
- No product invalidation request performs `RemoveByPatternAsync` or `RemoveManyAsync`.

Evidence:

- **SOURCE-PROVEN** `PointOfSale.Infrastructure/Redis/Services/StorefrontProductCacheInvalidator.cs`
- **SOURCE-PROVEN** product and home cache call sites use `StorefrontProductsInvalidationGenerationKey`
- **TEST-PROVEN** `StorefrontProductInvalidation_UsesGenerationWithoutPatternScans`
- **TEST-PROVEN** `M45ProductLookupValidationCacheTests`

### 7. Redis Write Semantics and Telemetry

Previous risk: direct `StringSetAsync` false results were treated like successful writes, caller cancellation could be swallowed by generic write failure handling, and stale generation-fenced writes lacked distinct outcome telemetry.

Remediation:

- Direct writes check the Redis boolean result.
- Outcomes distinguish `attempted`, `script_executed`, `command_succeeded`, `command_false`, `fenced`, `cancelled`, and `failed`.
- Caller cancellation is rethrown.
- Modified write-path logs use a SHA-256-derived key hash rather than the complete key.

Evidence:

- **SOURCE-PROVEN** `PointOfSale.Infrastructure/Redis/Services/RedisJsonCacheService.cs`
- **SOURCE-PROVEN** `PointOfSale.Infrastructure/Redis/Services/RedisPerformanceTelemetry.cs`
- **TEST-PROVEN** direct command-false, cancellation propagation, and generation-fence tests in `ComprehensiveCacheRemediationTests`
- **PARTIAL** the new unit tests use mocked StackExchange.Redis interfaces; a production-equivalent Redis endpoint was not required for the focused run

### 8. Projection Convergence and Outbox Recovery

No additional production change was justified after review.

Observed controls:

- Critical outbox rows are ordered by tenant, aggregate, event version, creation time, and ID.
- Per-message mutation uses a serializable transaction and provider-specific row locking.
- Earlier critical aggregate messages block later messages.
- Critical poison messages remain operator-visible and continue on a slow replay cadence.
- Product create, update, and lifecycle consumers use the SQL-authority projection writer.
- MongoDB upsert/tombstone operations are guarded by current event version and event identity.
- Duplicate delivery remains expected under at-least-once publication and is collapsed at projection fences.

Evidence:

- **SOURCE-PROVEN** `PointOfSale.Infrastructure/Jobs/OutboxPublisherJob.cs`
- **SOURCE-PROVEN** product projection consumers and `ProductSqlAuthorityProjectionWriter`
- **TEST-PROVEN** projection/outbox/chaos validation group: 63 passed, 0 failed
- **TEST-PROVEN** `ProductSqlAuthorityProjectionWriterTests`
- **PARTIAL** no live RabbitMQ/MongoDB outage-and-recovery exercise was run in this pass

## Test Corrections

Three tests initially failed because they certified superseded implementation details:

- Tenant lifecycle architecture expected predicates inline in `TenantLineageAuthority`; it now verifies delegation to `TenantAuthorityMemo` and the canonical memo predicates.
- Projection architecture expected `request.EventVersion`; it now verifies the SQL-derived authoritative product generation.
- The semantic chaos fixture marked a tenant `IsActive` but left `Status` at `PendingSetup`; it now creates lifecycle-consistent active/suspended tenants.

The corrected tests passed together: 3 passed, 0 failed.

## Validation Results

### Release Build

Command:

```text
dotnet build PointOfSale.sln -c Release --no-restore --nologo --verbosity:minimal
```

Result: **BUILD-PROVEN**, succeeded with 0 warnings and 0 errors.

### Focused Product and Cache Group

Included deterministic keys, normalization concurrency, freshness guards, SQL list authority, projection writer, lookup validation, Redis write outcomes, and invalidation.

Result: **TEST-PROVEN**, 72 passed, 0 failed.

### Tenant, POS, and JWT Group

Included scoped tenant memoization, lineage, lifecycle, POS session/authentication, JWT middleware configuration, and JWT routing.

Result: **TEST-PROVEN**, 37 passed, 0 failed.

### Projection, Outbox, and Chaos Group

Included Redis and Mongo convergence certification, projection governance, outbox consistency, propagation freshness, telemetry, and semantic convergence chaos tests.

Result: **TEST-PROVEN**, 63 passed, 0 failed.

### Complete Test Project

Command:

```text
dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj -c Release --no-build --no-restore --logger "console;verbosity=minimal"
```

Result: **BLOCKED**, terminated at the 15-minute timeout after emitting failures outside this remediation scope.

Observed failure classes:

- EF Core `ManyServiceProvidersCreatedWarning` promoted to exceptions after many independently configured test contexts.
- Existing architecture/documentation assertions for unrelated storefront, treasury, service-marketplace, and authorization surfaces.
- PostgreSQL schema drift in the configured integration database, including missing `CommerceHubSubOrderItems.CostAmountSnapshot` and missing `IsSupplierCardFunding`.

The complete run produced no final total because it timed out. These failures were not modified or suppressed.

## Residual Risks

- **BLOCKED** The repository-wide test suite is not green and did not complete.
- **BLOCKED** The configured PostgreSQL integration database is behind the current model/migration expectations.
- **PARTIAL** Live Redis, MongoDB, RabbitMQ, and multi-instance outage recovery were not all exercised in this pass.
- **PARTIAL** Timestamp-only freshness remains for cache families that have not adopted source/projection revision evidence; the remediated product-list path uses revisions.
- **SOURCE-PROVEN** Generation invalidation deliberately leaves old physical Redis keys until TTL expiry. This is bounded storage behavior, not an authority risk.
- **SOURCE-PROVEN** The worktree contains extensive unrelated pre-existing changes. This report certifies the scoped paths and test groups, not every uncommitted change.

## Release Gate

The cache/projection remediation itself is ready for review, but MAIN is **NO-GO** for an overall release until all of the following are true:

1. Apply or repair the PostgreSQL integration schema so it matches the current migration/model state.
2. Resolve the unrelated failing architecture/documentation tests or restore their expected artifacts.
3. Fix the test harness service-provider churn so the full run does not fail after creating many EF internal providers.
4. Re-run the complete Release test project to completion with zero failures.
5. Run production-equivalent Redis/MongoDB/RabbitMQ convergence checks if this is a deployment certification rather than a code-review gate.

## Scoped Files

Primary production changes:

- `PointOfSale.API/DTOs/Storefront/StorefrontDtos.cs`
- `PointOfSale.API/Infrastructure/Mvc/ApiRequestNormalizationFilter.cs`
- `PointOfSale.API/Services/Storefront/StorefrontProductService.cs`
- `PointOfSale.Infrastructure/Redis/CacheKeyBuilder.cs`
- `PointOfSale.Infrastructure/Redis/CacheKeyFilterNormalizer.cs`
- `PointOfSale.Infrastructure/Redis/Models/CacheEnvelope.cs`
- `PointOfSale.Infrastructure/Redis/Services/RedisJsonCacheService.cs`
- `PointOfSale.Infrastructure/Redis/Services/RedisPerformanceTelemetry.cs`
- `PointOfSale.Infrastructure/Redis/Services/StorefrontProductCacheInvalidator.cs`

Primary regression coverage:

- `PointOfSale.Tests/Infrastructure/ComprehensiveCacheRemediationTests.cs`
- `PointOfSale.Tests/Infrastructure/ProductListCacheFreshnessGuardTests.cs`
- `PointOfSale.Tests/Infrastructure/ProductSearchSqlAuthorityBatchTests.cs`
- `PointOfSale.Tests/Infrastructure/ProductSqlAuthorityProjectionWriterTests.cs`
- `PointOfSale.Tests/Infrastructure/ApiRequestNormalizationFilterConcurrencyTests.cs`
- `PointOfSale.Tests/Tenancy/TenantAuthorityMemoTests.cs`
- `PointOfSale.Tests/Architecture/EventOutboxConsistencyArchitectureTests.cs`
- `PointOfSale.Tests/Architecture/TenantLifecycleAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Infrastructure/SemanticConvergenceChaosValidationTests.cs`

No new database migration was required for the scoped cache/projection remediation.
