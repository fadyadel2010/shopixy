# MAIN Production Remediation Review

Date: 2026-09-25  
Target: `C:\Users\fadya\source\repos\PointOfSale`  
Baseline HEAD: `32ffffc` (`fix performance issues`)  
Comparison source: `D:\PointOfSale`

## Executive Verdict

The current MAIN worktree contains production-safe adaptations for the verified nullability race,
avoidable relational acquisition demand, same-key cache stampedes, duplicate product SQL
recomposition, set-based product/cart authority, redundant coupon reads, CommerceHub startup
recovery ordering, and MassTransit bus-outbox isolation. The focused Release verification is green.

The QA A1 process-local tenant-authority verdict cache is **REJECTED**. Its 60-second positive
verdict TTL permits stale authorization across replicas and after out-of-band mutations. MAIN's
host path already has a stronger shared Redis authority result with generation-fenced publication
and pre/post-commit invalidation. Authenticated-claim paths retain request-scoped PostgreSQL
authority where no reusable shared host verdict exists; the available QA evidence does not justify
weakening that security boundary.

This review found and fixed one additional MAIN defect: a timeout of an individual CommerceHub
startup-readiness probe could escape as `OperationCanceledException` and fault startup recovery
instead of participating in bounded retry. Per-attempt timeout is now converted to a not-ready
result, while application shutdown cancellation still propagates immediately.

The A1 re-audit also found one remaining avoidable tenant query: an authenticated tenant claim that
exactly matched the authoritative host result was re-proved from PostgreSQL. MAIN now reuses that
host verdict for absent or GUID-form tenant headers. Claim/host mismatches and claim-only requests
retain the guarded PostgreSQL path, while legacy slug-form tenant headers retain relational slug
validation.

This is a source-level review, not a production capacity certification. No k6 run, deployment,
infrastructure change, or 6,000-VU claim is part of this work.

## Evidence Boundary

- MAIN is a Git repository on branch `main`, tracking `origin/main`.
- MAIN had a large dirty worktree before this continuation. Existing user changes were preserved.
- `D:\PointOfSale` has no `.git` directory. `git status`, branch identity, and exact tree divergence
  cannot be independently established there.
- QA's live certification report identifies source commit
  `24e1f3aed74b83bdd034f11e143e5bd22531662a`, but also records 110 tracked modifications and
  numerous untracked files in the deployed worktree. The commit is therefore provenance evidence,
  not a complete identity for the QA source tree.
- The 2026-09-22 QA live run proves that the original Npgsql pool-exhaustion signature fell from
  32,725 exceptions to zero after the broader remediation. It also found a later timeout-ceiling
  bottleneck, so it does not prove unlimited capacity.
- The 2026-09-25 QA reports prove a shared `NullabilityInfoContext` race and substantial sequential
  application-tier demand. QA A1's post-change 3,000/6,000-VU A/B remained pending.

## Decision Register

Decision meanings:

- `PORT`: the narrow QA behavior is suitable for MAIN without changing its authority model.
- `ADAPT`: retain the remediation goal but implement it using MAIN's newer contracts.
- `REPLACE`: use a different mechanism because the QA implementation is not suitable.
- `REJECT`: do not implement the proposal because evidence or safety is insufficient.

| ID | Issue | Decision | MAIN disposition and evidence |
|---|---|---|---|
| A | Shared `NullabilityInfoContext` concurrency race | PORT | `ApiRequestNormalizationFilter.IsRequired` uses a method-local context. Parallel valid, invalid, parameter-nullability, and optional-property tests execute the real filter. No shared instance remains. |
| B | Repeated tenant authority reads within one request | ADAPT | `TenantAuthorityMemo` is keyed by `ApplicationDbContext` and tenant ID, uses canonical active/status/deletion guards, shares concurrent loads, and evicts faulted/cancelled tasks. Host resolution reuses MAIN's authoritative `DomainResolutionResult`; a matching authenticated claim now consumes the same verdict without another tenant command. |
| C | Repeated publication authority in one request | ADAPT | `StorefrontPublicationAuthorityMemo` shares one task per DbContext/tenant. A warmed tenant memo removes the redundant tenant join; cold/background callers retain the guarded join and fail closed. No cross-request publication staleness is introduced. |
| D | Same-key Redis refresh stampede | ADAPT | `KeyedSingleFlight` coalesces process-local misses, rechecks cache inside the flight, detaches the shared producer from follower cancellation, and value-removes completed/failed flights. The existing Redis refresh lock remains the cross-process coordinator. |
| E | Duplicate and per-item product SQL authority work | ADAPT | Refresh results carry `CacheReadResult.Refreshed`; freshly SQL-composed values skip an immediate duplicate overlay, while true Redis hits still recompose from SQL. List/cart paths use chunk-bounded `ApplyBatchAsync`; tenant/order/isolation and visibility semantics are covered. |
| F | Cart product fan-out and variant identity | ADAPT | Cart revalidation loads projected products once and applies one batch SQL authority snapshot. Simple products resolve and persist their canonical variant identity; mismatched, foreign, inactive, deleted, unpriced, or out-of-stock variants fail closed. |
| G | Redundant coupon-state and immediate post-apply reads | ADAPT | A clean unauthenticated guest cart skips persisted coupon hydration. Successful apply returns the validated, transactionally persisted cart directly. Later cart responses still hydrate/revalidate coupon authority; coupon transaction and outbox ordering remain intact. |
| H | Duplicate tenant reads for cart currency and tax country | ADAPT | Cart currency and tax fallback reuse `TenantAuthorityMemo`; currency remains fail-closed and tax still performs its required settings query. Focused relational tests prove zero additional tenant commands after memo warm-up. |
| I | CommerceHub recovery before search write readiness | ADAPT | Readiness covers ping, cluster health, write-index resolution, and index existence. Startup polls before scanning and checks again immediately before scanning; background reconciliation skips scans while not ready; dependency-not-ready outcomes remain deferred. This review additionally fixed per-attempt timeout retry semantics. |
| J | MassTransit bus-outbox serialization contention | PORT | The EF bus outbox retains PostgreSQL and `UseBusOutbox`, but explicitly uses `IsolationLevel.ReadCommitted`, matching PostgreSQL claimant locking rather than MassTransit's serializable default. Business transactions retain their own isolation choices. |

## QA A1 Rejection

QA A1 stores positive tenant-authority snapshots in singleton `IMemoryCache` for 60 seconds and
invalidates through an EF interceptor after commit. That design is not accepted for MAIN because:

- invalidation is process-local and therefore does not immediately converge across replicas;
- a positive stale verdict can continue authorizing a suspended or deleted tenant until TTL expiry;
- out-of-band SQL does not execute EF interceptors;
- the QA report explicitly states that multi-replica and out-of-band gaps are TTL-bounded rather
  than closed;
- QA's claimed load-level benefit was not A/B certified; its 3,000/6,000-VU after-runs were pending;
- MAIN's normal host path already avoids the extra tenant query using shared, generation-fenced
  Redis authority, so adding a second weaker authority cache would duplicate ownership.

A future optimization for claim-only requests would require a shared generation-fenced verdict,
complete mutation coverage, explicit failure behavior, multi-replica tests, and load evidence. It
should not reuse QA A1 unchanged.

MAIN instead applies a narrower A1 adaptation. When `HostResolutionMiddleware` has produced a
`Found` result and its tenant ID exactly matches the authenticated claim, tenant resolution reuses
that authoritative result rather than issuing a duplicate tenant query. This path does not create
or extend any cache lifetime. A mismatched claim still executes the canonical guarded tenant lookup
and is rejected as a cross-tenant violation. A non-GUID tenant header still loads the tenant row so
the existing slug-header contract can be validated rather than inferred from host data.

## Security and Correctness Review

- Tenant authority uses `IsActive`, `TenantStatus.Active`, and `DeletedAtUtc == null` guards.
- Shared host authority joins Domain and Tenant state and is invalidated for domain lifecycle,
  redirect, tenant slug/base URL, active status, lifecycle status, and deletion changes.
- Domain cache fills are fenced against overlapping mutations through a shared Redis generation.
- Matching claim/host authority reuse requires an exact tenant-ID match and does not memoize the
  synthetic host result for unrelated downstream tenant reads.
- Claim/host mismatches retain a guarded PostgreSQL lookup and fail with `403 TENANT_FORBIDDEN`.
- Product candidates are checked for tenant and requested-ID/slug consistency before cache
  population; true cache hits are re-authorized from current SQL.
- Batch product output preserves candidate order and emits `null` for rejected or foreign IDs.
- Coupon persistence still uses a relational transaction and publishes through the EF outbox.
- Cart simple-product identity remains variant-keyed and upgrades legacy null-variant lines without
  creating a duplicate line.
- CommerceHub dependency outages are deferred rather than persisted as real index failures.
- `ReadCommitted` applies only to the MassTransit EF bus outbox configuration; it does not globally
  weaken serializable business workflows.

## Additional Fix From This Review

Files:

- `PointOfSale.API/Services/CommerceHub/CommerceHubAutoHealingService.cs`
- `PointOfSale.Tests/CommerceHub/CommerceHubStartupRecoveryReadinessTests.cs`

Behavior:

- An individual readiness attempt still has a 10-second upper bound.
- If that attempt token expires while the host is still running, the result becomes
  `startup-readiness-attempt-timed-out` and bounded startup polling continues.
- If the host cancellation token is cancelled, `OperationCanceledException` is not intercepted and
  shutdown remains prompt.
- The regression test forces a timeout followed by readiness and verifies exactly two probes and a
  successful result.

## Verification

Pre-fix focused baseline in Debug:

```powershell
dotnet test "PointOfSale.Tests\PointOfSale.Tests.csproj" -c Debug --no-restore --nologo --filter "FullyQualifiedName~ApiRequestNormalizationFilterConcurrencyTests|FullyQualifiedName~BusOutboxIsolationConfigurationTests|FullyQualifiedName~CommerceHubStartupRecoveryReadinessTests|FullyQualifiedName~KeyedSingleFlightTests|FullyQualifiedName~TenantAuthorityMemoTests|FullyQualifiedName~StorefrontPublicationMemoTests|FullyQualifiedName~CartAndTaxTenantMemoTests|FullyQualifiedName~DomainAuthorityCacheTests|FullyQualifiedName~MarketplaceReadControllerTests|FullyQualifiedName~StorefrontCartCouponRevalidationTests|FullyQualifiedName~ProductSearchSqlAuthorityBatchTests|FullyQualifiedName~CartItemIdentityContractTests" -p:RunAnalyzers=false
```

Result: 76 passed, 0 failed, 0 skipped.

Post-fix readiness verification in Release:

```powershell
dotnet test "PointOfSale.Tests\PointOfSale.Tests.csproj" -c Release --no-restore --nologo --filter "FullyQualifiedName~CommerceHubStartupRecoveryReadinessTests" -p:RunAnalyzers=false
```

Result: 3 passed, 0 failed, 0 skipped. The Release API and test projects built successfully as part
of this command; existing compiler warnings remain.

Post-fix combined focused verification in Release:

```powershell
dotnet test "PointOfSale.Tests\PointOfSale.Tests.csproj" -c Release --no-build --nologo --filter "FullyQualifiedName~ApiRequestNormalizationFilterConcurrencyTests|FullyQualifiedName~BusOutboxIsolationConfigurationTests|FullyQualifiedName~CommerceHubStartupRecoveryReadinessTests|FullyQualifiedName~KeyedSingleFlightTests|FullyQualifiedName~TenantAuthorityMemoTests|FullyQualifiedName~StorefrontPublicationMemoTests|FullyQualifiedName~CartAndTaxTenantMemoTests|FullyQualifiedName~DomainAuthorityCacheTests|FullyQualifiedName~MarketplaceReadControllerTests|FullyQualifiedName~StorefrontCartCouponRevalidationTests|FullyQualifiedName~ProductSearchSqlAuthorityBatchTests|FullyQualifiedName~CartItemIdentityContractTests" -p:RunAnalyzers=false
```

Result: 77 passed, 0 failed, 0 skipped.

Post-fix combined focused verification in Debug:

```powershell
dotnet test "PointOfSale.Tests\PointOfSale.Tests.csproj" -c Debug --no-restore --nologo --filter "FullyQualifiedName~ApiRequestNormalizationFilterConcurrencyTests|FullyQualifiedName~BusOutboxIsolationConfigurationTests|FullyQualifiedName~CommerceHubStartupRecoveryReadinessTests|FullyQualifiedName~KeyedSingleFlightTests|FullyQualifiedName~TenantAuthorityMemoTests|FullyQualifiedName~StorefrontPublicationMemoTests|FullyQualifiedName~CartAndTaxTenantMemoTests|FullyQualifiedName~DomainAuthorityCacheTests|FullyQualifiedName~MarketplaceReadControllerTests|FullyQualifiedName~StorefrontCartCouponRevalidationTests|FullyQualifiedName~ProductSearchSqlAuthorityBatchTests|FullyQualifiedName~CartItemIdentityContractTests" -p:RunAnalyzers=false
```

Result: 77 passed, 0 failed, 0 skipped. The normal Debug API and test outputs built successfully;
existing compiler warnings remain.

A1 relational command-count verification in Debug and Release:

```powershell
dotnet test "PointOfSale.Tests\PointOfSale.Tests.csproj" -c Debug --no-restore --nologo --filter "FullyQualifiedName~NpgsqlAcquisitionRemediationTests" -p:RunAnalyzers=false
dotnet test "PointOfSale.Tests\PointOfSale.Tests.csproj" -c Release --no-restore --nologo --filter "FullyQualifiedName~NpgsqlAcquisitionRemediationTests" -p:RunAnalyzers=false
```

Result in each configuration: 21 passed, 0 failed, 0 skipped. The tests prove zero tenant commands
for an exact claim/host match, one guarded command before rejecting a mismatch, and one guarded
command when a slug-form tenant header requires validation.

A1 security/isolation verification in Debug:

```powershell
dotnet test "PointOfSale.Tests\PointOfSale.Tests.csproj" -c Debug --no-build --nologo --filter "FullyQualifiedName~ProductionSecurityHardeningTests|FullyQualifiedName~StorefrontTenantIsolationTests|FullyQualifiedName~PosTenantAuthorityCertificationTests"
```

Result: 41 passed, 0 failed, 0 skipped.

One broader combined command selected `TenantLifecycleAuthorityCertificationTests` and reported 70
passed and 1 failed. The failure is an unrelated source-text assertion requiring the literal
`tenant.IsActive` inside `TenantLineageAuthority.IsActiveTenantAsync`; that existing method now
delegates to `TenantAuthorityMemo`. Neither the failing certification nor the lineage implementation
was changed as part of A1.

Hygiene:

- `git diff --check`: no whitespace errors; only existing LF-to-CRLF working-copy notices.

## Residual Risk

- No full repository test suite was run.
- No real PostgreSQL, Redis, MongoDB, Elasticsearch, RabbitMQ, or multi-replica integration run was
  executed in this review.
- MAIN's outbox regression test verifies configuration source. QA contains a real-PostgreSQL
  contention proof, but that proof was not rerun against MAIN here.
- Process-local single-flight reduces in-process duplication only; the Redis lock and generation
  protocol remain responsible for cross-process cache coordination.
- Publication authority intentionally remains request-scoped, so one publication read per guarded
  request can remain. No cross-request publication verdict cache was introduced without complete
  invalidation evidence.
- Authenticated-claim authority still requires a guarded PostgreSQL lookup when it cannot reuse an
  exact authoritative host result, including claim/host mismatches and slug-form header validation.
  This is a deliberate security/correctness tradeoff pending stronger shared-cache evidence.
- No updated production throughput, latency, pool occupancy, or VU capacity claim is made.

## Worktree Note

MAIN contained substantial tracked and untracked changes before this review. This continuation did
not revert or rewrite unrelated work. The production behavior newly changed by this continuation is
limited to the CommerceHub readiness timeout handling and exact-match host/claim authority reuse
described above; focused tests and this report were added alongside those changes.
