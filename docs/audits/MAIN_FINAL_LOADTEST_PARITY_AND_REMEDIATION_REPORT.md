# MAIN Final Load-Test Parity and Remediation Audit

Date: 2026-09-28
Repository: `C:\Users\fadya\source\repos\PointOfSale`
Branch: `main`
Reviewed commit: `13c7afd2c9d8161e51205b657f3a996514220c6b` plus the current uncommitted MAIN worktree
Read-only comparison source: `D:\PointOfSale`

## Verdict

**Scoped load-path, cache, and projection remediation: PASS.**

**Parity transfer decision: CONDITIONAL GO for review.**

**Repository release decision: NO-GO.**

MAIN contains the correctness mechanisms needed to address the reference environment's observed product-list cache bypass and the earlier request-path contention findings. The reviewed MAIN implementation also preserves several stronger controls: SQL-authoritative product membership, generation-fenced cache ownership, full deterministic cache hashes, per-DbContext tenant authority, and atomic Lua-owned Redis writes.

The reference environment's load evidence cannot be inherited as MAIN load evidence. MAIN was not deployed or load-tested in this audit, its complete test project is not green, the configured PostgreSQL test target is a non-disposable Dell baseline database, and 244 source-level architecture/certification tests currently fail. Overall release approval therefore remains blocked.

## Evidence Labels

- **SOURCE-PROVEN**: established by direct inspection of MAIN or the read-only comparison source.
- **TEST-PROVEN**: established by a passing automated test against this MAIN worktree.
- **BUILD-PROVEN**: established by a successful MAIN Release build.
- **LOAD-PROVEN-REFERENCE**: established only by the comparison source's retained QA/load-test evidence; it is not proof for MAIN.
- **PARTIAL**: useful evidence exists, but the production-equivalent dependency or complete suite was not exercised successfully.
- **BLOCKED**: required evidence could not be gathered safely or an applicable validation gate failed.

## Scope and Safety

- MAIN was the only writable codebase. The comparison tree was accessed only through read/search operations.
- No branch switch, reset, stash, clean, commit, migration, deployment, load test, or external-service mutation was performed.
- No credentials or complete connection strings are recorded in this report.
- Effective PostgreSQL test configuration resolves to host `192.168.1.5`, database `PosEcommerceDb_Baseline`. That is not an approved disposable target, so PostgreSQL integration tests were not run.
- Mock, source-level, EF InMemory, and SQLite tests were permitted and used.
- MAIN began with extensive unrelated modified and untracked files. This audit did not revert or rewrite them.

## Reference Evidence Boundary

The comparison source's final QA report records a command-proven product-list `MISS -> SET -> HIT` recovery, zero correctness failures at 3000 and 6000 VU, and a 31-33% product-list hit rate after repairing projection freshness in the QA dataset. At 6000 VU it records HTTP p50 5,315 ms and p95 18,850 ms, with zero status-0, 5xx, or correctness failures.

This is **LOAD-PROVEN-REFERENCE**, not MAIN proof, because the evidence belongs to a different source tree, image, database, deployment, workload host, and restored QA environment. The reference report itself grants only a conditional GO for a separate MAIN review and explicitly states that no MAIN transfer was performed.

## A-J Decision Matrix

| Priority | Concern | MAIN decision | Evidence | Result |
|---|---|---|---|---|
| A | Request-normalization concurrency | Keep MAIN's immutable metadata cache. It avoids per-request reflection while never sharing a mutable `NullabilityInfoContext`. | Source inspection plus concurrent valid/invalid/optional argument tests. | **ALREADY STRONGER / TEST-PROVEN** |
| B | Tenant authority query demand | Keep per-`ApplicationDbContext` `TenantAuthorityMemo`; do not port the reference's process-wide 60-second positive authority cache. MAIN avoids cross-request stale authorization and multi-replica invalidation gaps. | Same-scope query deduplication, 100-way concurrent sharing, cancelled-load eviction, empty-ID no-query, and scope-isolation tests. | **REFERENCE MECHANISM REJECTED / TEST-PROVEN** |
| C | Stable distributed cache identity | Keep MAIN's versioned canonical key schema and full SHA-256 hash. This fixes process-randomized `string.GetHashCode()` identities and is stronger than truncated identity schemes. | Culture, ordering, explicit-default, schema-version, and tenant-isolation tests. | **ADOPTED AND STRENGTHENED / TEST-PROVEN** |
| D | Product-list authority | Keep SQL as authority for visible membership, total count, and page order; use MongoDB only as a candidate projection and overlay current SQL truth. | Missing-member, order, total-count, visibility, category, tenant, and affiliate tests. | **ADOPTED / TEST-PROVEN** |
| E | Projection freshness proof | Compare projected `ProductGeneration` with current SQL `ProjectionMutationVersion`; do not compare unrelated event-version namespaces. Preserve projection ingestion time on read overlays and stamp it only at the projection write boundary. | Old-but-caught-up, true lag, unrelated high/low event version, mixed page, read-overlay timestamp, and writer timestamp tests. | **ADOPTED AND CORRECTED / TEST-PROVEN** |
| F | Product cache invalidation and stale-write races | Keep tenant-scoped generation advancement and generation-owned reads. Do not reintroduce request-path pattern scans or broad deletion. | Invalidation ownership, lookup generation, and race-fence tests. | **ALREADY STRONGER / TEST-PROVEN** |
| G | Redis write truth and telemetry | Require direct `StringSetAsync` to return true; treat exceptions as failure; require the owned atomic Lua fence to report success; propagate caller cancellation. | Direct success, command false, command exception, Lua success/fence, cancellation, and generation-race tests. | **ADOPTED AND STRENGTHENED / TEST-PROVEN, LIVE REDIS PARTIAL** |
| H | Projection and outbox convergence | Keep SQL-authoritative projection recomposition, monotonic event/generation fences, ordered critical outbox handling, and operator-visible poison retries. | Projection writer, governance, redelivery, event propagation, outbox architecture, and semantic convergence tests. | **PARITY OR STRONGER / TEST-PROVEN, LIVE DEPENDENCIES PARTIAL** |
| I | Startup recovery and bus-outbox contention | Keep search write-readiness polling and MassTransit EF bus outbox `ReadCommitted` isolation. These reference remediations are already present in MAIN. | Startup retry/readiness tests and source-level outbox isolation test. | **ADOPTED / TEST-PROVEN** |
| J | QA diagnostics and release certification | Do not copy temporary discriminator/load instrumentation or claim the reference's environment results for MAIN. The reference-only outbox SQL-stage classifier is optional observability, not a correctness prerequisite, and was not ported. | Reference report review, MAIN source comparison, safe build/tests, and blocked full-suite evidence. | **DIAGNOSTICS REJECTED; RELEASE BLOCKED** |

## Final Remediations

### Product freshness and timestamp provenance

- `ProductSearchSqlAuthorityOverlay` now preserves the stored `SyncedAtUtc` value. A read-side authority overlay can update current product truth but cannot manufacture newer projection-ingestion evidence.
- `ProductSqlAuthorityProjectionWriter` assigns `SyncedAtUtc = DateTime.UtcNow` immediately before the reviewed projection mutation boundary, making timestamp ownership explicit.
- `StorefrontProductService` compares SQL and projected `ProductGeneration` values from the same version namespace. `LastEventVersion` may represent product, stock, variant, or another event stream and is retained as event metadata rather than used as product-generation proof.
- Product-list cache population fails closed when membership, timestamp, or revision proof is incomplete or any visible product is behind.

### SQL-authoritative list behavior

- Current SQL determines visible product count, ordered page IDs, tenant lifecycle, publication, category lineage, and affiliate eligibility.
- MongoDB candidates are reordered to the SQL page; missing candidates receive identity placeholders and are recomposed through the batched SQL overlay.
- Warm cache entries are rebuilt against the current SQL page before public return, so a newly visible SQL product cannot remain hidden by stale cached membership.

### Redis write behavior

- Direct Redis writes distinguish successful commands, false command results, failures, and cancellation.
- Generation-owned writes remain one atomic Lua decision, so a write cannot be accepted after its invalidation generation changes.
- Telemetry hashes cache keys rather than logging complete keys.

### Tenant authority

- Repeated authority reads in one request/DbContext share one task.
- Faulted or cancelled loads are evicted and may be retried.
- `Guid.Empty` fails closed without issuing a query.
- No authority verdict is shared across DbContext/request scopes.

## Validation

### Release build

```text
dotnet build PointOfSale.sln -c Release --no-restore --nologo --verbosity:minimal
```

Result: **BUILD-PROVEN**, succeeded with 0 warnings and 0 errors.

### Consolidated safe regression set

The final Release run included request-normalization concurrency, tenant authority, deterministic Redis behavior, product-list authority and freshness, lookup validation, projection writer/governance/convergence, outbox consistency, startup recovery, and bus-outbox isolation.

Result: **TEST-PROVEN**, 147 passed, 0 failed, 0 skipped in 19 seconds.

Supporting focused runs, which overlap with the consolidated set:

- Corrected freshness group: 31 passed, 0 failed.
- Redis remediation group: 10 passed, 0 failed.
- Normalization/cache/tenant group: 72 passed, 0 failed.
- Projection/freshness group: 44 passed, 0 failed.
- Projection/outbox/convergence group: 69 passed, 0 failed.
- Product lookup validation: 21 passed, 0 failed.
- Startup readiness and outbox isolation: 4 passed, 0 failed.

### Source-level architecture suite

Result: **BLOCKED**, 3,061 passed and 244 failed out of 3,305 in 31 seconds.

The failures span unrelated source-shape assertions, missing certification markers/documents, frontend/backend contract pins, and missing expected migration artifacts. Examples include stale CMS frontend token assertions and a missing `20260705183000_Geo8OrderShipmentGeographicPersistence.cs` artifact. The failures were not suppressed or rewritten to obtain a green result.

### Complete test project

Prior evidence from this worktree records that the complete Release test project did not finish before the 15-minute limit and therefore produced no trustworthy final total. Observed failure classes include:

- EF Core `ManyServiceProvidersCreatedWarning` after the test process creates more than twenty heterogeneous internal service providers. Stack traces localize manifestations to test helper context construction; suppressing the warning in production would hide rather than fix the test-harness issue.
- PostgreSQL schema drift, including missing `CommerceHubSubOrderItems.CostAmountSnapshot`; the earlier scoped report also records missing `IsSupplierCardFunding` schema.
- Broad architecture and certification drift outside this remediation.

Result: **BLOCKED**. No complete-suite success claim is made.

## Observability Decision

The comparison `SlowQueryLoggingInterceptor` adds debug classification for MassTransit poll/claim/ack SQL and a special warning for PostgreSQL `40001` serialization failures. MAIN already has the production correctness change that reduces this contention: bus-outbox delivery uses `ReadCommitted`.

The classifier was not ported because:

- It does not alter correctness, retries, isolation, or delivery behavior.
- It was not required to establish the scoped MAIN fixes.
- Adding event-specific production logs without an agreed operational consumer or alert contract would expand scope.

If operations requires stage-specific outbox telemetry, it should be added as a separate observability change with log-contract tests and cardinality/volume review.

## Residual Risks and Blockers

- **BLOCKED** The repository-wide test project has not completed successfully.
- **BLOCKED** The source-level architecture suite has 244 failures.
- **BLOCKED** The configured PostgreSQL test database is a non-disposable baseline and is behind current model expectations.
- **PARTIAL** Redis behavior is mock/source proven, not validated against a production-equivalent Redis/Sentinel endpoint in MAIN.
- **PARTIAL** RabbitMQ, MongoDB, OpenSearch outage/recovery, duplicate delivery, and multi-instance invalidation were not exercised live in MAIN.
- **PARTIAL** MAIN has no load-run evidence for 3000 or 6000 VU. Reference metrics are comparison evidence only.
- **PARTIAL** EF internal-provider churn remains in the complete test harness. A proper fix should consolidate test service-provider option shapes or shared fixtures; it should not globally suppress the warning.
- **SOURCE-PROVEN** Generation invalidation intentionally leaves old Redis keys to expire by TTL. This is bounded storage behavior, not an authority failure.
- **SOURCE-PROVEN** Extensive unrelated worktree changes prevent this report from certifying every uncommitted file.

## Release Gate

The scoped remediation is ready for review, but MAIN remains **NO-GO** for release until all of these gates pass:

1. Provision an explicitly disposable PostgreSQL test database with the current migration/model schema; do not use the Dell baseline database.
2. Repair the test harness's EF internal service-provider churn without suppressing production warnings.
3. Resolve or intentionally rebaseline the 244 architecture/certification failures after reviewing each contract.
4. Run the complete Release test project to completion with zero failures.
5. Deploy the reviewed MAIN candidate to an approved isolated QA environment and prove `MISS -> successful command-level SET -> same-key HIT`.
6. Run the canonical 3000/6000 VU profiles with frozen workload identity, dependency configuration, correctness checks, and restoration evidence before making a MAIN capacity claim.

## Scoped Change Inventory

Primary runtime paths reviewed or remediated in the broader MAIN port:

- `PointOfSale.API/Infrastructure/Mvc/ApiRequestNormalizationFilter.cs`
- `PointOfSale.API/Services/Storefront/StorefrontProductService.cs`
- `PointOfSale.Infrastructure/Tenancy/TenantAuthorityMemo.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs`
- `PointOfSale.Infrastructure/MongoDB/Services/ProductSqlAuthorityProjectionWriter.cs`
- `PointOfSale.Infrastructure/Redis/CacheKeyBuilder.cs`
- `PointOfSale.Infrastructure/Redis/CacheKeyFilterNormalizer.cs`
- `PointOfSale.Infrastructure/Redis/Models/CacheEnvelope.cs`
- `PointOfSale.Infrastructure/Redis/Services/RedisJsonCacheService.cs`
- `PointOfSale.Infrastructure/Redis/Services/RedisPerformanceTelemetry.cs`
- `PointOfSale.Infrastructure/Redis/Services/StorefrontProductCacheInvalidator.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubAutoHealingService.cs`
- `PointOfSale.API/Program.cs`

Primary regression coverage:

- `PointOfSale.Tests/Infrastructure/ApiRequestNormalizationFilterConcurrencyTests.cs`
- `PointOfSale.Tests/Tenancy/TenantAuthorityMemoTests.cs`
- `PointOfSale.Tests/Infrastructure/ComprehensiveCacheRemediationTests.cs`
- `PointOfSale.Tests/Infrastructure/ProductListCacheFreshnessGuardTests.cs`
- `PointOfSale.Tests/Infrastructure/ProductSearchSqlAuthorityBatchTests.cs`
- `PointOfSale.Tests/Infrastructure/ProductSqlAuthorityProjectionWriterTests.cs`
- `PointOfSale.Tests/Infrastructure/M45ProductLookupValidationCacheTests.cs`
- `PointOfSale.Tests/Infrastructure/SemanticConvergenceChaosValidationTests.cs`
- `PointOfSale.Tests/Architecture/EventOutboxConsistencyArchitectureTests.cs`
- `PointOfSale.Tests/Architecture/MongoProjectionConvergenceCertificationTests.cs`
- `PointOfSale.Tests/Architecture/ProductProjectionGovernanceTests.cs`
- `PointOfSale.Tests/Architecture/BoundedProjectionRedeliveryConvergenceRegressionTests.cs`
- `PointOfSale.Tests/Architecture/EventPropagationProjectionFreshnessCertificationTests.cs`
- `PointOfSale.Tests/CommerceHub/CommerceHubStartupRecoveryReadinessTests.cs`
- `PointOfSale.Tests/CommerceHub/BusOutboxIsolationConfigurationTests.cs`

No database migration was required or created by this scoped cache/projection remediation.

## Final Statement

The MAIN correction is source-proven, Release-build-proven, and test-proven for the permitted local paths. It preserves SQL authority and fails closed when projection or cache evidence is incomplete. The reference QA result justifies review of these mechanisms but does not certify MAIN's deployment or capacity. Until the repository-wide, schema-isolated, and live-load gates above are completed, the only defensible final status is **scoped PASS, overall NO-GO**.
