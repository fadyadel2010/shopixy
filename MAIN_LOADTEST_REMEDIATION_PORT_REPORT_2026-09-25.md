# MAIN Load-Test Remediation Port Report

Date: 2026-09-25  
Target: `main` at baseline HEAD `32ffffc` (`fix performance issues`)  
Comparison source: `D:\PointOfSale`

## Executive Summary

This change set ports the narrow, production-path remediations identified in the verified comparison source into MAIN. It addresses a request-normalization concurrency race, repeated relational authority reads, same-key cache stampedes, redundant product SQL recomposition, avoidable cart/coupon reads, CommerceHub startup recovery before search write readiness, and MassTransit bus-outbox serialization contention.

The port was adapted at hunk level. Whole files were not copied because the comparison source also contains diagnostic instrumentation and older adjacent implementations that do not belong in MAIN.

This report is not a load-capacity certification. No k6 run was performed, and no claim is made that MAIN supports 6,000 virtual users or any other concurrency target.

## Ported Remediations

### Request normalization concurrency

- Replaced the shared static `NullabilityInfoContext` and global lock with a method-local context in `ApiRequestNormalizationFilter.IsRequired`.
- This removes both unsafe shared mutable reflection state and global request serialization.
- Added parallel valid, invalid, mixed-parameter, and optional-null regression scenarios.

### Relational acquisition demand

- Added `TenantAuthorityMemo`, scoped by `ApplicationDbContext` through a `ConditionalWeakTable`.
- Repeated guarded tenant-by-id checks now share one successful task within a request/DbContext scope.
- Faulted or cancelled tenant loads are evicted; verdicts do not cross DbContext scopes.
- Authenticated tenant resolution, slug resolution, base-URL resolution, tenant-lineage checks, cart currency, and tax-country fallback reuse the memoized authority.
- MAIN's newer host-resolution behavior remains intact: a previously authoritative `DomainResolutionResult` is not followed by another tenant query.
- Added request/DbContext-scoped storefront publication memoization while retaining explicit tenant, active-status, and deletion guards.
- Converted cart empty-result and currency paths from synchronous tenant reads to async memoized reads.

### Cache stampede and product recomposition

- Added process-local, per-key `KeyedSingleFlight` task sharing with double-checked cache reads.
- Follower cancellation abandons only that waiter; it does not cancel or evict the shared load.
- Failed and completed loads are evicted so later misses can retry or refresh normally.
- Kept the existing distributed Redis refresh lock as the cross-process authority.
- Added `CacheReadResult<T>` so callers can distinguish a cache hit from a value refreshed by the shared factory.
- Updated product detail and list reads to skip a second identical SQL authority overlay when the current request shared the refresh result.
- Redis hits still recompose from current SQL authority.
- MAIN's existing batch SQL authority overlay, tenant guards, visibility filtering, affiliate filtering, and by-id/by-slug candidate validation were preserved.

### Cart coupon read demand

- Clean guest carts with no coupon code, coupon object, or discount now skip the persisted coupon-state query.
- Successful coupon application returns the just-validated and persisted cart directly instead of immediately revalidating the same coupon.
- Existing response-time revalidation remains on later cart operations.

### CommerceHub startup recovery

- Added application-level search write-readiness checks covering connectivity, cluster health, write-index resolution, and index existence.
- Startup recovery uses bounded readiness polling before scanning MongoDB or SQL.
- A second pre-scan readiness check closes the race between readiness polling and recovery enumeration.
- Background reconciliation skips scans while search writes are unavailable.
- Dependency-not-ready outcomes remain pending/deferred instead of being counted and persisted as real index failures.
- Real index failures retain retry, failure accounting, and explicit error logging.
- MAIN's SQL-visible-listing filtering remains in startup and background recovery.

### Bus outbox contention

- Configured the MassTransit EF bus outbox to use `ReadCommitted`.
- Existing PostgreSQL outbox claimant semantics and `UseBusOutbox` remain unchanged.

## MAIN Behavior Preserved

- Product SQL authority remains batched; no per-item authority loop was introduced.
- Storefront product visibility, tenant ownership, affiliate, and canonical identity checks remain authoritative.
- Canonical simple-product variant handling in cart mutations remains unchanged.
- Host resolution continues to avoid an extra tenant query after an authoritative domain result.
- CommerceHub startup/background candidates remain filtered through SQL-visible listing identities.
- Coupon ownership, validation, persistence, and later response normalization remain unchanged apart from the two verified redundant-read removals.

## Explicit Exclusions

The following were not changed:

- Kubernetes, Docker, K3s, or deployment manifests.
- PgBouncer, PostgreSQL, Redis, MongoDB, or OpenSearch configuration.
- Connection-pool sizes, infrastructure timeouts, CPU/memory resources, or workload topology.
- Load-test scripts, generated results, diagnostic assets, or request-phase diagnostic instrumentation.
- Existing unrelated dirty-worktree changes.

## Regression Coverage

Focused coverage includes:

- `ApiRequestNormalizationFilterConcurrencyTests`
- `KeyedSingleFlightTests`
- `TenantAuthorityMemoTests`
- `StorefrontPublicationMemoTests`
- `CartAndTaxTenantMemoTests`
- `ProductSearchSqlAuthorityBatchTests`
- `DomainAuthorityCacheTests`
- `MarketplaceReadControllerTests`
- `StorefrontCartCouponRevalidationTests`
- `CommerceHubStartupRecoveryReadinessTests`
- `BusOutboxIsolationConfigurationTests`

The test doubles in `DomainAuthorityCacheTests`, `MarketplaceReadControllerTests`, and `ProductSearchSqlAuthorityBatchTests` were updated to model the refresh-status contract without changing their existing cache semantics.

## Validation Evidence

Builds:

- `dotnet build PointOfSale.API\PointOfSale.API.csproj -c Release --no-restore --nologo -v:quiet -p:RunAnalyzers=false`
  - Result: succeeded, 0 errors, existing warnings remain.
- `dotnet build PointOfSale.Tests\PointOfSale.Tests.csproj -c Debug --no-restore --nologo -v:quiet -p:RunAnalyzers=false`
  - Result: succeeded, 0 errors, existing warnings remain.

Focused tests:

- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj -c Debug --no-build --nologo --filter "FullyQualifiedName~ApiRequestNormalizationFilterConcurrencyTests|FullyQualifiedName~BusOutboxIsolationConfigurationTests|FullyQualifiedName~CommerceHubStartupRecoveryReadinessTests|FullyQualifiedName~KeyedSingleFlightTests|FullyQualifiedName~TenantAuthorityMemoTests|FullyQualifiedName~StorefrontPublicationMemoTests|FullyQualifiedName~CartAndTaxTenantMemoTests|FullyQualifiedName~DomainAuthorityCacheTests|FullyQualifiedName~MarketplaceReadControllerTests|FullyQualifiedName~StorefrontCartCouponRevalidationTests|FullyQualifiedName~ProductSearchSqlAuthorityBatchTests"`
  - Result: 63 passed, 0 failed, 0 skipped.

Diff validation:

- `git diff --check`
  - Result: no whitespace errors. Git emitted only existing LF-to-CRLF working-copy notices.

An earlier broader coupon/authority selection exposed three assertions outside this port's backend scope: two certification-report marker assertions and one frontend source-shape assertion. The backend coupon authority assertion in that selection passed. These unrelated certification/frontend failures were not changed or hidden.

## Residual Risk and Capacity Statement

- The focused tests prove code-path behavior and relational command-count reductions under test conditions; they do not prove production throughput.
- No full repository test suite was run.
- No real Redis, PostgreSQL, MongoDB, or Elasticsearch load scenario was executed as part of this port.
- No k6 or equivalent load test was run.
- Production capacity must be established separately with representative infrastructure, data volume, observability, and acceptance thresholds.
- Therefore, this port makes no 6,000-VU or other capacity claim.

## Worktree Note

MAIN contained substantial unrelated tracked and untracked changes before this port. This work did not revert or rewrite those changes. Review and staging should be limited to the remediation files and tests described in this report.
