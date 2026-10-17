# Infrastructure Health Remediation Certification

Date: **2026-10-10 local time**. Evidence timestamps below are UTC, **2026-10-09**.
Repository: `C:\Users\fadya\source\repos\PointOfSale` — MAIN only.

## 1. Executive verdict and scope

**Verified source remediation PASS; infrastructure/data-ownership follow-ups remain open. The application is not uniformly Healthy.**

Implemented and activated fixes for:

1. Invalid Redis latency sampling, ignored cancellation and incomplete failure classification.
2. Expensive full-catalog reconstruction in the five-second projection health path.
3. Global count cancellation that could conceal incorrect tenant/product projection identities, incorrect tenant-scope derivation and a hard-coded collection name.
4. A security-sensitive single-product SQL overlay gap admitting foreign-owned child variants/media.
5. Missing sanitized exception-category evidence in health responses.

**508 distinct selected backend cases passed, zero failed/skipped in the final results**, including 50 new cases and two updated existing health cases. Fresh API/Core/Infrastructure/Tests builds succeeded. Ten post-activation aggregate observations all returned **200 / Degraded**, both Redis checks Healthy, PostgreSQL/schema/financial readiness Healthy, and no projection-check timeouts.

**Remaining:** 58 Mongo product documents across 11 tenant identities have no current MAIN SQL tenant authority. They were preserved and the Degraded alert remains. An independently observed Redis PING timeout demonstrates intermittent delay outside the API's cache-command path; server versus network versus local scheduling attribution cannot be completed from the available repository/observer evidence. No infrastructure tuning/restart or data repair was attempted. Disabled certificate-provider and Caddy execution legitimately remain Degraded.

## 2. Entry state and preservation controls

- Branch: **main**.
- HEAD: **`256339621c0ea49f3602097cdacdf89ffe71754f`**.
- Fresh entry inventory: **58 tracked modifications, 72 untracked files**.
- Entry tracked diff SHA-256: **`16e8816a4397aa522599dd89bf80d8a5f2f33673eb0856ec7cbd296004969f25`**.
- Current branch/HEAD, status, tracked diff/stat and individual untracked paths were inspected. Every tracked/untracked entry file was SHA-256 inventoried before product edits; final comparison allows only this task's explicit files. No pre-existing file was deleted.
- Prior carrier certification and relevant Redis configuration/startup/Npgsql/cache-projection/tenant-isolation/product-projection reports were read. Historical reports supplied context, not substituted runtime facts.

The previous carrier report and all its source changes remain intact. No staging, commits, reset, clean, stash, checkout/revert, migration creation/application, secondary repository/deployment access or load-test execution occurred. Synthetic PostgreSQL cases used the existing isolated `TestDatabaseScope`; MAIN queries used READ ONLY transactions. No business-data DML, projection rebuild/deletion/backfill, Redis key deletion/flush/eviction/configuration change, carrier operation or financial write was issued by this task.

## 3. Runtime/configuration and entry financial baseline

The actual listener on port **5010** was freshly inspected, rather than trusting a prior PID:

| Field | Entry | Activated |
|---|---|---|
| API PID | 20340 | **3864** |
| Parent launcher PID | 8676 | **13200** |
| Executable | `PointOfSale.API\bin\Debug\net9.0\PointOfSale.API.exe` | Same MAIN path |
| Working directory | `PointOfSale.API` | Same MAIN directory |
| Environment | Development | Development |
| Parent command/profile | `dotnet run --launch-profile http --no-build --no-restore` | Same |
| Effective PostgreSQL | **192.168.1.5:5432 / PosEcommerceDb_Baseline** | Same |
| Redis | Enabled, **192.168.1.5:6379** | Same |
| Redis connect/sync/async budgets | **5000 / 1000 / 1000 ms** | Unchanged |
| Mongo product projection | **192.168.109.128:27017 / ecommerce_read_db / products** | Same |

Runtime environment plus the API's actual configuration ordering/user-secrets identity were resolved in a bounded diagnostic; only sanitized effective fields were emitted. No connection-string credentials or tokens were written to the report. Public health identity independently agreed with SQL. Startup profile and working directory were rechecked immediately before restart, and existing DLL hashes matched entry artifacts.

Actual entry migration history: **49 applied**, ending `20261009182344_ManualCarrierCashRemittance`, **zero pending**, **no pending model changes**. Latest financial reconciliation was **Healthy / 0 mismatches**. The entry READ ONLY snapshot records full row-content fingerprints for 29 protected tables.

Only the verified MAIN API process was terminated for the required source activation. Its startup environment/profile was preserved; Redis, PostgreSQL, MongoDB and unrelated application services were not restarted.

Final active Debug DLL SHA-256 values:

| Artifact | SHA-256 |
|---|---|
| API | `28e97dd3dc1ffe4caae68b7410430586f88bb18675b2b1b999eec4e955706f9f` |
| Infrastructure | `5cc2576814463658658c700db2b2d41441f04f9c019b4d0a30e629790406375d` |
| Core | `803de6a2d22fb1fba59e8145a153ba39b438b10d21c0b80c02eab75f66c23174` |

## 4. Endpoint/aggregation trace and bounded baseline

`Program.cs` registers the dependency checks through `AddHealthChecks` and operational checks through `ObservabilityServiceExtensions.AddProductionObservability`:

- `/health`: all registered checks.
- `/health/ready`: checks tagged `ready`, including Redis dependency, PostgreSQL and financial/outbox checks.
- `/health/marketplace`: Marketplace and CommerceHubPlatform.
- `/health/live`: no dependency checks; application liveness only.

ASP.NET Core's normal aggregate mapping remains: an Unhealthy check yields **503**, Healthy/Degraded yields **200**. Individual checks run independently; Redis failure did not relabel PostgreSQL or financial checks. `HealthCheckJsonResponseWriter.WriteAsync` serializes each check's own result. No endpoint predicate, critical registration or overall-state mapping was loosened.

Ten aggregate/liveness observations were collected sequentially over roughly two minutes, with ten-second gaps. Readiness and marketplace subsets were also requested at observations 1, 5 and 10. No concurrency/load generator was used. Each retained observation contains the HTTP state, UTC timestamp, overall duration, every check's name/status/duration/data and available exception/failure category.

### Baseline observations

Times are UTC on 2026-10-09; durations are **health-entry duration milliseconds**, not necessarily Redis command duration. H=Healthy, D=Degraded, U=Unhealthy.

| UTC time | Aggregate | Redis status/ms | Latency status/ms | Projection status/ms |
|---|---|---|---|---|
| 22:38:59.705 | 503 / U | U / 569.318 | U / 1346.535 | U / 5906.432 |
| 22:39:18.842 | 200 / D | H / 29.933 | D / 147.259 | D / 3025.798 |
| 22:39:34.979 | 503 / U | U / 258.416 | U / 930.705 | U / 6082.870 |
| 22:39:46.609 | 200 / D | H / 33.855 | D / 85.407 | D / 1564.928 |
| 22:39:58.355 | 200 / D | H / 10.756 | H / 38.667 | D / 1716.554 |
| 22:40:12.772 | 200 / D | H / 6.115 | D / 56.486 | D / 2248.476 |
| 22:40:24.662 | 200 / D | H / 8.296 | D / 141.838 | D / 1838.087 |
| 22:40:37.185 | 200 / D | H / 11.500 | H / 46.388 | D / 2465.824 |
| 22:40:48.782 | 200 / D | H / 15.636 | D / 66.809 | D / 1480.674 |
| 22:41:00.312 | 200 / D | H / 108.418 | D / 154.467 | D / 1483.851 |

Liveness: **10/10 HTTP 200**. PostgreSQL, CommerceHubPlatform and financial readiness: **10/10 Healthy**. The three marketplace observations were **200 Healthy**. Readiness was **503 Unhealthy**, then **200 Degraded**, **200 Degraded**. The two failed projection checks reported `Failed to check projection drift` with duration beyond their five-second budget. The baseline response writer did not expose exception categories, and the prior detached launcher retained only 118 bytes of launch output/empty stderr; the exact baseline exception type cannot be recovered. It is not invented here. The source's timeout/failure path and timing support an expensive-probe budget failure, not a demonstrated projection-consumer failure.

Several dependency durations rose together during the two bad aggregate requests, so those samples alone do not isolate server failure or API thread-pool contention. No request-level stack/thread-pool trace was available on the old process. The subsequent direct observer and source-level tests distinguish confirmed defects from that uncertainty.

## 5. Redis findings and implemented correction

### Confirmed source defects

`PointOfSale.Infrastructure/HealthChecks/ProductionHealthChecks.cs`, `RedisLatencyHealthCheck.CheckHealthAsync`, previously:

- Sequentially SET a random key, GET it and DELETE it.
- Applied 50/200 ms latency thresholds to the **sum of three round trips and continuations**.
- Ignored both SET success and GET content.
- Ignored caller cancellation.
- Classified command failures as Degraded instead of Unhealthy.

The separate `RedisDependencyHealthCheck.CheckHealthAsync` already measured PING but also ignored caller cancellation. Its 100 ms Unhealthy threshold remains unchanged; the misleading `DegradedLatencyThreshold` variable name does not change the actual contract.

Correction: `RedisConnectionFactory.ProbeLatencyAsync` performs **one read-only PING on the existing shared connection**, bounded by the existing configured AsyncTimeoutMs and caller token. Both checks use it. No retries, new health multiplexer, cache mutations or connection replacement occur. The latency thresholds remain **50 ms Degraded / 200 ms Unhealthy**, dependency readiness remains **100 ms Unhealthy**. Missing connections/timeouts/command failures remain or become Unhealthy; disabled latency remains explicitly Degraded and disabled dependency remains Unhealthy. Caller cancellation propagates rather than becoming a successful/degraded sample. PING/failure-category diagnostics are included.

This corrects what the checker measures; it does not certify cache write authorization or pretend that Redis latency can never recur.

### Connection/cache execution trace

`RedisServiceCollectionExtensions.AddRedisCache` explicitly registers a singleton `RedisConnectionFactory`, direct `IConnectionMultiplexer` from that factory, singleton distributed locking and JSON cache services. `RedisJsonCacheService`, generation-fenced authority cache and `DistributedLockService` reuse it. The configured `IDistributedCache` integration and SignalR backplane also have their own integration-owned connections. Runtime entry had four established Redis sockets; final had four. A per-request multiplexer/reconnection explosion was not found and its lifetime was not changed speculatively.

`RedisConnectionFactory.CreateConnection` retains its existing bounded startup guard, configured budgets and `AbortOnConnectFail=false` reconnection behavior. Cache key/generation/tenant/session semantics, cache fallback paths, critical cart lock behavior and invalidation were not changed. Existing cache/tenant regressions passed.

### Independent runtime diagnostics and external blocker

Only bounded PING and selected read-only INFO fields were requested against the verified Redis endpoint. No SCAN, KEYS, MONITOR, write, deletion or server configuration command was used. Observer `AllowAdmin=true` permits INFO on that temporary client only; it did not change application/server settings.

An early separate StackExchange.Redis observer encountered **RedisTimeoutException**, PING after **1437 ms**, configured command timeout **1000 ms**. Its exception diagnostics reported one outstanding command, no queued writes, 10/10 client managers available, IOCP busy 0, worker busy 1 and zero queued pool items. This is evidence that the API's three-command probe is **not the sole source** of intermittent latency. It does not by itself distinguish Redis/server scheduling from network or observer-host scheduling. The early command aborted before writing its normal diagnostic JSON; the observed tool failure is retained as a disclosed failed diagnostic, not counted as a passing test.

A later successful bounded observer at 23:22 UTC recorded:

- 10/10 independent client PINGs: **3.527–50.764 ms**.
- 10/10 raw TCP connect+RESP PING exchanges: **5.728–50.962 ms**.
- Redis version **6.0.16**, uptime **242928 s**, connected clients **13**, blocked clients **0**, rejected connections **0**, instantaneous operations/s **0**, latest fork **1209 microseconds**.
- INFO cumulative command averages are retained in the diagnostic artifact, not interpreted as per-request measurements.

These establish intermittent responsiveness, not permanent outage or proven connection exhaustion. No isolated Redis server/docker executable was available for a real isolated-service test in this environment; Redis functional failure/cancellation/threshold coverage uses strict mocked protocol interfaces. The live observer is read-only diagnostic evidence, not a synthetic integration test claimed against MAIN.

**Follow-up requires authorized infrastructure telemetry:** correlate future command-timeout UTC timestamps with Redis host CPU/run queue, persistence/fork events and network RTT/packet loss, plus Windows/API scheduling metrics. Those host/internal metrics were not available here. No threshold increase or server intervention is justified by the existing evidence.

## 6. Projection ownership, performance and security findings

### Data contract and actual MAIN diagnosis

`ProductDocument` is a **tenant-owned derivative projection**, with `(TenantId, ProductId)` as its unique business identity. Its collection is shared physically across tenants; it is not a global public catalog. `ProductReadRepository` read/list/search/hydration/update/tombstone paths include TenantId filters. SQL product query filters require the current tenant; global health queries explicitly bypass them only for the observer inventory.

The chain is `ProductCreated/Updated/VisibilityLifecycle` consumers → `ProductSqlAuthorityProjectionWriter.ApplyAsync` → current SQL overlay → `StorefrontProductProjectionMutationBoundary.ApplyAsync` → tenant/product repository mutation with event/version fences. Full upserts and tombstones remain SQL-derived; duplicate/stale delivery remains fenced. Health checks do not invoke those mutation paths. Their tests and convergence/governance tests passed.

Bounded READ ONLY SQL plus Mongo identity/metadata projection established:

- **11 SQL tenants**, **8 SQL tenants with products**.
- **196 Mongo documents**, **138** in current SQL tenant scope.
- **98 current visible product identities** in both SQL canonical visibility and Mongo, with **0 missing, 0 extra visible, 0 duplicate pair identities**.
- **58 visible documents across 11 other tenant identities** absent from both current MAIN SQL Tenants and Products.
- **0 empty Mongo tenant identities** in the bounded inventory.
- MAIN SQL contains **0 mismatched variant-owner rows and 0 mismatched media-owner rows**.

The 58 documents are **orphan/foreign relative to this MAIN SQL authority**. Their provenance could be historical/development tenancy or another SQL authority, but that provenance was not proven from this permitted target. Their existence in a multi-tenant collection is not by itself cross-tenant disclosure, and it is not enough to declare them legitimate current shared data. The existing Degraded alert was retained and made precise. No deletion/reassignment/rebuild occurred. An authorized ownership/provenance investigation is required before any data repair.

### Health-path defects and correction

`ProjectionDriftHealthCheck.CheckHealthAsync` previously used `ProductSearchSqlAuthorityOverlay.ApplyBatchAsync` once per product-owning tenant to hydrate full product, category, policy, units, media, variants, attributes, stock and promotions simply to count visible documents. The five-second health budget was unchanged while baseline healthy probes still took **1481–3026 ms**, and two failed at roughly **5906/6083 ms**.

The new `ProductProjectionHealthSnapshotReader.ReadAsync` uses **three read-only SQL commands**, independent of product-owning tenant count, loading only tenant IDs, product visibility admission and category lineage. It preserves active/nondeleted tenant, publication, current semantic variant ownership/price, same-tenant variant, and live same-tenant acyclic category predicates. It does not hydrate catalog payloads or populate caches/projections. The existing five-second budget remains.

On MAIN, the lightweight result equaled the full canonical overlay. One diagnostic measured **193 ms versus 1974 ms**; final read-only comparison measured **65 ms versus 1279 ms**. These are bounded paired measurements, not a load-capacity claim.

Other corrected checker defects:

- Current tenant scope comes from **Tenants**, not only product rows; a known tenant with zero products is not mislabeled foreign.
- Uses configured `MongoDbSettings.ProductsCollectionName`, not a hard-coded collection.
- Mongo query fetches visible identity pairs only. Missing+unexpected+duplicate counts replace the absolute difference of global totals, preventing equal totals or opposite tenant drift from masking incorrect membership.
- Duplicate pair identities are explicitly Unhealthy; existing 10/50 drift thresholds remain unchanged.
- Foreign count still produces Degraded, even with exact current-scope membership.
- Caller cancellation propagates; dependency timeouts/failures remain Unhealthy with a sanitized failure category.

### Security-sensitive child-ownership correction

An isolated PostgreSQL parity case failed because `ProductSearchSqlAuthorityOverlay.ApplyCoreAsync` admitted a positive-priced variant belonging to tenant B linked to tenant A's product. The batch overlay already filtered child ownership; the single overlay did not. The single overlay also lacked equivalent media ownership filtering.

Both single-path child selections now require `TenantId == requested tenant`. Same-tenant variants/media remain present; foreign children cannot authorize visibility or be serialized into the product projection. The synthetic single/batch parity test and mismatched variant case verify this using isolated PostgreSQL; MAIN mismatch counts were zero, so no existing exposure was demonstrated.

Affected callers were traced through the shared overlay: product detail/wishlist/SEO/landing/cart and affiliate reads; admin search-sync/reindex and discount repair; product projection writer; product search/index, variant, media, stock/price, promotion and affiliate projection consumers. They continue to use the same central SQL authority; no caller permission or projection mutation fence was bypassed. Current MAIN health inventory is read-only and cannot grant public read/write authority.

## 7. Aggregation evidence and explicitly rejected workarounds

`HealthCheckJsonResponseWriter.WriteAsync` now emits **exceptionCategory only** (exception type name). Messages, stacks, sensitive payloads and credentials are not emitted. The regression verifies that a failing Redis check stays Unhealthy while independent PostgreSQL stays Healthy and projection stays Degraded; synthetic exception secrets never reach JSON. HTTP mapping/registrations were not changed.

Rejected: increasing Redis thresholds or timeouts, treating a timeout as Healthy, dropping Redis from readiness, swallowing cancellation, retries, cache disablement/tenant-key changes, flushing/deleting keys, removing orphan-document alerts, modifying tenant identities, rebuilding/deleting projections, financial corrections, enabling Caddy/certificates and restarting shared infrastructure. None was needed for the verified source corrections; operational follow-ups remain separately identified.

## 8. Tests, builds, failures and final results

Fresh full dependency-chain build:

```text
dotnet build "PointOfSale.Tests/PointOfSale.Tests.csproj" -c CarrierCancellationVerification --no-restore --no-incremental -v:quiet /clp:ErrorsOnly
```

Result: **452 warnings, 0 errors**. This built current API/Core/Infrastructure/Tests source. Required activation Debug build used the same API source: **187 warnings, 0 errors**. No warnings were suppressed. Fresh `dotnet ef migrations has-pending-model-changes ... --configuration CarrierCancellationVerification --no-build` reported no changes; READ ONLY migration/model inspection independently confirmed 49/0.

Each final test group used the fresh configuration with `--no-build --no-restore`, actual `--list-tests` discovery and TRX output. Every individual outcome/counter was checked and identities deduplicated:

| Disjoint selected group | Passed | Coverage |
|---|---:|---|
| Focused health | **52** | 21 Redis cases; 28 isolated PostgreSQL projection/tenant cases; 1 response/aggregation case; 2 existing projection health cases |
| Projection/cache/tenant | **117** | SQL overlay/public hydration, writer, cache freshness/invalidation, authority memo/tenant isolation, convergence/governance/outbox, semantic chaos, and generic Clearing transfer rejection |
| Carrier/finance | **144** | Carrier remittance SQL/API, Manual COD, prerequisite/refund lineage, new refund evidence, legacy settlement containment |
| Cancellation/tracking/accounting | **195** | Cancellation SQL/contracts/evidence, concurrent tracking and scheduled tenant context, tenant isolation, Treasury settlement, accounting writer/outbox |
| **Distinct total** | **508** | **0 failed / 0 skipped** |

New functional coverage proves existing threshold boundaries, one read-only command/no retries, configured timeout for a never-completing command, caller/pre-cancellation, unavailable/disabled classification, matching SQL visibility, category cycles/foreign lineage, variant semantic supersession, same-tenant operations, rejected foreign mutation seeds, equal-count wrong identities, duplicates and configured Mongo collection. Strict mocked Redis interfaces reject any extra command; PostgreSQL cases exercise actual relational translation/query filters and prove three-command inventory.

Disclosed earlier failures:

- Redis red run: **19 failed / 2 passed / 21**, establishing invalid probe/cancellation/diagnostic behavior before correction.
- Initial combined health run: **48 passed / 1 failed / 49**, revealing the actual foreign-variant single-overlay gap. It was fixed, not removed from coverage. Subsequent expanded health run passed 51, then final 52 with response coverage.
- Initial projection/cache run: **115 passed / 2 failed / 117**, from old string assertions expecting weaker visibility patterns, removed helper names and historical log wording. The architecture checks now explicitly require current `IsSearchable`, the inline active/status/deleted tenant SQL join, and cached-slug SQL validation/rejection/rediscovery flow. Assertions were not skipped or relaxed to accept an unvalidated path. Final complete group passed 117.
- Observer diagnostics initially failed because INFO needed observer admin-command permission, one PING timed out, and a temporary diagnostic output file collided between parallel observers. Only the observer tooling was corrected; those failures were not used to justify application/server configuration changes. Final diagnostics were run sequentially.

No full repository-wide test-suite result is claimed. No product UI source changed, so browser/UI alteration verification was not required.

## 9. Post-fix repeated observations and comparison

The active API exposed the new PING/identity diagnostics and sanitized exception field. Ten sequential observations at ten-second gaps were collected after activation without overlapping builds or test load.

| UTC time | Aggregate | Redis status/ms | Latency status/ms | Projection status/ms |
|---|---|---|---|---|
| 23:45:42.263 | 200 / D | H / 15.430 | H / 15.052 | D / 196.899 |
| 23:45:53.549 | 200 / D | H / 15.375 | H / 15.711 | D / 81.216 |
| 23:46:03.992 | 200 / D | H / 10.117 | H / 10.378 | D / 104.507 |
| 23:46:14.485 | 200 / D | H / 18.221 | H / 18.391 | D / 80.004 |
| 23:46:24.957 | 200 / D | H / 7.278 | H / 7.502 | D / 72.780 |
| 23:46:35.892 | 200 / D | H / 23.446 | H / 23.080 | D / 93.743 |
| 23:46:46.531 | 200 / D | H / 12.732 | H / 12.807 | D / 80.370 |
| 23:46:56.938 | 200 / D | H / 9.866 | H / 9.992 | D / 54.758 |
| 23:47:07.373 | 200 / D | H / 28.804 | H / 28.897 | D / 102.893 |
| 23:47:17.822 | 200 / D | H / 25.619 | H / 25.602 | D / 115.720 |

Every final observation: liveness 200, Redis dependency/latency Healthy, projection Degraded solely with retained foreign count/current-scope drift zero, PostgreSQL/CommerceHubPlatform/financial readiness Healthy. Exception/failure categories for these checks were null because no exception occurred. Readiness at 1/5/10 was **200 Degraded**; marketplace was **200 Healthy**. All registered disabled-provider checks remained visible.

| Measure | Baseline | Final |
|---|---:|---:|
| Aggregate 503 / 200 | 2 / 8 | **0 / 10** |
| Redis dependency Healthy / Unhealthy | 8 / 2 | **10 / 0** |
| Redis latency Healthy / Degraded / Unhealthy | 2 / 6 / 2 | **10 / 0 / 0** |
| Redis latency check duration median | 113.622 ms | **15.381 ms** |
| Projection check median / maximum | 2043.281 / 6082.870 ms | **87.480 / 196.899 ms** |
| Projection Unhealthy / Degraded | 2 / 8 | **0 / 10** |
| Current visible SQL/Mongo identity counts | 98 / 98 | **98 / 98** |
| Foreign document count | 58 | **58, alert retained** |

These repeated results demonstrate the source fix's observed effect. They are not proof that future dependency/network latency cannot recur or that every degradation was removed.

Final captured API logs were inspected through a bounded sanitized category/exception summary, without publishing payloads: no Redis connection-failed/restored entries, Redis critical/degraded latency entries, Redis timeouts or projection-check exceptions in the sampled output. Normal projection Degraded result logs remain. Four `PostgreSqlDistributedLockException` occurrences in startup orchestration were recorded separately; the sampled PostgreSQL/schema/financial checks were Healthy. Those startup lock observations were not attributed to Redis or changed in this scope. The earlier detached launcher lacked usable API log output, so absence of old log events is not claimed as absence of old failures.

## 10. Final preservation and remaining actions

Final READ ONLY SQL inspection confirmed **49 applied / zero pending / no pending model changes**, and **Healthy / zero-mismatch financial reconciliation**. All **29** protected full-row content fingerprints matched this task's entry snapshot:

Orders, Shipments, OrderRefunds, PaymentIntents, settlement batches/allocations, ledger journal entries/lines/accounts, stock movements, cancellation operations, accounting/payment/generic outboxes, shipping webhook logs, carrier configuration, CommerceHub refunds/executions/requests/payments/financial transactions/vendor earnings/payouts, all four carrier evidence/remittance tables, Products and Tenants.

Mongo bounded identity/visibility/event-version/generation metadata digest stayed:

```text
E73FAA4E6DCE721D4A7D7335FE3AD228161161CFA9D4CB202E5EF59AFC348CE1
```

This is the inspected metadata digest, not a claimed full-BSON content audit. No MAIN projection mutation command was executed.

| Remaining item | Classification | Required next action |
|---|---|---|
| 58 documents / 11 absent SQL tenant identities | Persistent unresolved projection provenance/data hygiene; no current-scope membership drift or demonstrated cross-tenant disclosure | Obtain authorized source/ownership inventory for those identities before any repair. Preserve alert and documents. No rebuild/delete/reassign is authorized here. |
| Independent PING timeout | Intermittent external Redis path/server/observer scheduling issue, exact infrastructure layer unlocalized | Infrastructure operator correlation of server scheduling/persistence and network/Windows timing around a future occurrence. No configuration increase/restart justified yet. |
| Single-overlay child tenant mismatch vulnerability | Source gap fixed and regression-proven; MAIN mismatch counts zero | Retain central same-tenant child admission. No existing-data repair indicated by this inventory. |
| Certificate provider/Caddy disabled | Unrelated intentional Degraded checks | Leave configuration/execution unchanged; separate activation authorization required. |
| Startup distributed-lock log exceptions | Separate observed orchestration condition | Investigate only if recurring operationally; do not infer a Redis cause or alter finance state. |

## 11. File-by-file change inventory

| File | Change |
|---|---|
| `PointOfSale.Infrastructure/Redis/Services/RedisConnectionFactory.cs` | Shared read-only bounded/cancellable `ProbeLatencyAsync` |
| `PointOfSale.API/HealthChecks/SystemDependencyHealthChecks.cs` | Redis dependency uses shared probe, preserves readiness threshold, propagates cancellation, reports failure category |
| `PointOfSale.Infrastructure/HealthChecks/ProductionHealthChecks.cs` | Correct Redis latency probe/failures; lightweight configured-collection projection inventory and pair-identity drift/duplicate diagnostics; foreign alert retained |
| `PointOfSale.Infrastructure/HealthChecks/ProductProjectionHealthSnapshotReader.cs` **new** | Three-command read-only tenant/visibility/category inventory |
| `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs` | Same-tenant single-path media/variant ownership fences |
| `PointOfSale.API/HealthChecks/HealthCheckJsonResponseWriter.cs` | Sanitized exception category, no raw exception disclosure |
| `PointOfSale.Tests/Infrastructure/InfrastructureHealthRedisTests.cs` **new** | 21 failure/budget/cancellation/threshold/no-mutation cases |
| `PointOfSale.Tests/Infrastructure/InfrastructureHealthResponseTests.cs` **new** | Independent aggregation and exception-redaction case |
| `PointOfSale.Tests/Infrastructure/ProjectionHealthSnapshotPostgresTests.cs` **new** | 28 real PostgreSQL visibility/lineage/identity/query-budget/security cases |
| `PointOfSale.Tests/Infrastructure/ProjectionDriftHealthCheckTests.cs` | Existing checks now supply actual identity cursor fixtures instead of only aggregate counts |
| `PointOfSale.Tests/Architecture/ProductDetailMediaPublicProjectionCertificationTests.cs` | Precise current SQL/searchability/slug-validation contracts replacing obsolete strings |
| `.ai/reports/INFRASTRUCTURE_HEALTH_REMEDIATION_CERTIFICATION.md` **new** | This report |

All other entry files were preserved byte-for-byte, including the previous carrier report and migrations/model snapshot. No configuration file changed.

Final working-tree inventory: **65 tracked modifications, 77 untracked files**. The seven changed existing files and five additions above are exactly the task delta; the prior 58/72 baseline is preserved. Final tracked diff SHA-256 is `d6b5a008a2665051a92f77975d7bd827a5cce76b63d66fbf074ed04305dedb8f`. **`git -c core.safecrlf=false diff --check` passed.** The first final full-tree hashing command exceeded its 120-second tool limit; a bounded 600-second retry completed the byte-for-byte check successfully. That tooling delay is not described as a product test failure or dependency diagnosis.

## 12. Evidence index

Sanitized evidence/tooling is retained in `C:\Users\fadya\AppData\Local\Temp\opencode`:

- `infrastructure-health-{entry,final}-tree.json`: entry/final hashes and preservation inventory.
- `carrier-cancellation-final-closeout-infrastructure-{entry,preactivation,final}-runtime.json`: independently inspected runtime/DLL/health identity.
- `infrastructure-health-{entry,final}-sql.json`: actual 49/0/model/financial and protected-row snapshots.
- `infrastructure-health-{baseline,final}-samples.json`: every aggregate/liveness observation and sampled readiness/marketplace subsets.
- `infrastructure-health-comparison.json`: computed counts/timing comparison.
- `infrastructure-health-effective-configuration.json`: redacted effective target/settings.
- `infrastructure-health-redis-readonly-diagnostics.json`: successful independent PING/raw RESP/selected INFO evidence.
- `infrastructure-health-projection-readonly-diagnostics.json`: bounded hashed-tenant inventory, canonical/lightweight parity, mismatch counts and unchanged metadata digest.
- `infrastructure-health-log-diagnostics.json`: bounded sanitized API log/process summary.
- `infrastructure-health-debug-build.log`, `infrastructure-health-activation.json`: controlled activation evidence.
- `infrastructure-health-{focused,projection-cache,carrier-finance,cancellation-tracking}-{discovery,execution}.log` and corresponding `.trx`: final distinct discovery/results.
- `infrastructure-health-redis-red.trx`: disclosed pre-fix regression failures.
- `infrastructure-health-test-summary.json`: 508 distinct Passed outcomes/counters.
- `infrastructure-health-certification-manifest.json`: artifact hashes and final preservation/migration/result cross-checks.

Final scoped source verdict is PASS with the explicit operational/data-ownership blockers above. Aggregate Degraded status is expected and preserved; an overall fully Healthy application verdict is not claimed.
