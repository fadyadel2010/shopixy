# MAIN Npgsql and Redis Complete Remediation Certification - 2026-09-22

## 1. Executive Summary

The MAIN code remediation for the identified PostgreSQL acquisition and Redis
critical-path bottlenecks is complete. The implementation reduces repeated SQL
authority reads, removes one-shot probes from the application connection pool,
reuses the shared Redis multiplexer, fences domain-cache races across instances,
and moves cart outbox staging outside Redis mutation-lock hold time.

No PostgreSQL pool size or request timeout was increased. SQL remains business
authority, tenant isolation remains fail closed, and cart events remain backed
by the MassTransit Entity Framework outbox.

Final certification verdict: **PASS WITH UNRELATED RELEASE CONDITIONS**. The
earlier evidence in §§3–7 is preserved as a checkpoint; **FINAL CERTIFICATION
CLOSURE** contains the superseding focused-test and runtime results. The full
repository test project remains incomplete, and aggregate readiness is 503 for
the separately identified payout and CommerceHub search-sync conditions.

## 2. Repository and Constraints

- Repository: `C:\Users\fadya\source\repos\PointOfSale`
- Branch: `main`
- Base commit: `24e1f3a`
- Certification date: 2026-09-22
- The worktree contains extensive unrelated user changes. No unrelated files
  were reverted or normalized.
- No subagents were used.
- Kubernetes, k6, workload logic, and `tests/load/**` were not modified by this
  remediation.
- No production pool-size increase, timeout increase, or speculative retry was
  introduced.

## 3. Remediated Bottlenecks

### 3.1 Cross-request domain authority cache

Files:

- `PointOfSale.Core/Interfaces/IDomainResolutionCache.cs`
- `PointOfSale.Core/Models/DomainResolutionResult.cs`
- `PointOfSale.Infrastructure/Data/Repositories/PersistenceDomainResolver.cs`
- `PointOfSale.Infrastructure/Redis/Services/RedisDomainResolutionCache.cs`
- `PointOfSale.Infrastructure/Redis/Services/RedisJsonCacheService.cs`
- `PointOfSale.API/Program.cs`

The active `IDomainResolutionCache` registration is now
`RedisDomainResolutionCache`; `NoOpDomainResolutionCache` remains only as an
explicit compatibility fallback for hosts that intentionally register no cache
provider.

The cache uses:

- Normalized-host keys under a versioned authority keyspace.
- A 10-minute entry TTL, including authoritative not-found results.
- Per-host single-flight population so unrelated hosts do not serialize.
- A shared Redis invalidation generation.
- Generation-fenced Lua reads and writes so a cold fill that overlaps an
  authority mutation cannot republish stale authority.
- PostgreSQL fallback when Redis is disabled or unavailable for reads.

`PersistenceDomainResolver` caches canonical slug, custom-domain,
`StorefrontBaseUrl`, redirect, and negative resolution outcomes while retaining
PostgreSQL as the source of truth.

### 3.2 Transaction-correct authority invalidation

Files:

- `PointOfSale.Infrastructure/Data/DomainAuthorityCacheInvalidationInterceptor.cs`
- `PointOfSale.API/Program.cs`

Relevant `Domain` and `Tenant` changes advance the shared generation before
save. This closes the stale-fill race before a mutation can commit.

The second generation advance is transaction-aware:

- Auto-commit `SaveChanges` advances after the save completes.
- Explicit transactions mark invalidation pending and advance only after
  `TransactionCommitted`.
- Rollback and transaction failure clear pending state without publishing a
  false post-commit invalidation.
- Enabled Redis authority invalidation fails the authority mutation before
  commit if the shared invalidation boundary cannot be reached.

The global generation is intentionally broader than tenant-specific
invalidation. This favors correctness and bounded implementation risk over a
more complex per-tenant dependency graph.

### 3.3 Tenant and publication query reduction

Files:

- `PointOfSale.API/Middleware/TenantResolutionMiddleware.cs`
- `PointOfSale.API/Middleware/StorefrontPublicationGuardMiddleware.cs`
- `PointOfSale.Infrastructure/Data/Repositories/PersistenceDomainResolver.cs`

The request pipeline now reuses resolver-proven active tenant authority instead
of issuing a redundant tenant query. Canonical slug resolution avoids an
expected-miss `StorefrontBaseUrl` lookup, and exact store-config requests defer
publication authority to the controller's existing joined query rather than
paying for duplicate middleware and controller checks.

Focused command-count contracts continue to prove:

- One relational command for canonical slug resolution.
- One relational command for a matching authenticated domain.
- Fail-closed mismatch behavior without weakening tenant isolation.
- No duplicate publication query on the exact store-config path.

### 3.4 Cart lock and outbox pressure

Files:

- `PointOfSale.API/Services/Cart/CartService.cs`
- `PointOfSale.Core/Interfaces/IEventPublisher.cs`
- `PointOfSale.Infrastructure/EventBus/MassTransitEventPublisher.cs`
- `PointOfSale.Tests/CartItemIdentityContractTests.cs`

Cart mutation and merge operations accumulate legacy cart events while Redis
locks are held. After the lock-release `finally` completes, the events are sent
to one mixed-type publisher batch.

`MassTransitEventPublisher` preserves each event's runtime type, stages every
message through `IPublishEndpoint`, detaches all newly staged outbox entities if
any batch member fails, and performs at most one immediate `SaveChangesAsync`
when there are no pending business mutations.

The new `AddItem_ReleasesMutationLockBeforePublishingSingleEventBatch` contract
proves that a new-cart add operation:

- Releases the mutation lock before publishing.
- Makes exactly one batch call.
- Makes zero single-event calls.
- Preserves ordered `CartCreatedEvent` and `CartItemAddedEvent` staging.

This removes PostgreSQL outbox work from the Redis lock hold interval without
changing the existing best-effort cart-event failure semantics.

### 3.5 Redis connection topology

Files:

- `PointOfSale.Infrastructure/Redis/Services/RedisConnectionFactory.cs`
- `PointOfSale.Infrastructure/Redis/Services/DistributedLockService.cs`
- `PointOfSale.Infrastructure/Redis/RedisServiceCollectionExtensions.cs`
- `PointOfSale.Infrastructure/Redis/Configuration/RedisSettings.cs`
- `PointOfSale.API/Program.cs`

`DistributedLockService` now consumes the singleton
`RedisConnectionFactory`; it no longer creates or owns an independent
multiplexer. Cache, authority-cache, and lock traffic therefore share the
reviewed application Redis transport.

A live startup attempt revealed a leftover scoped `IDistributedLockService`
registration in `Program.cs` alongside the singleton registered by
`AddRedisCache`. The scoped implementation required DI to choose between two
valid constructors and prevented startup. Removing that duplicate leaves one
factory-created singleton registration. The user then restarted the Debug API
successfully.

`AsyncTimeoutMs` is configurable and retains a 1000 ms default. The change does
not hide slow Redis operations behind a larger timeout.

The checked-in production file currently sets `Redis:Enabled=true` but leaves
`Redis:ConnectionString` empty for deployment injection. Runtime cache
activation therefore depends on the deployment supplying a non-empty secret or
environment value.

### 3.6 PostgreSQL pool topology

Files:

- `PointOfSale.Infrastructure/Configuration/PostgresConnectionStringPolicy.cs`
- `PointOfSale.API/Jobs/HangfireInitializationHostedService.cs`
- `PointOfSale.API/Program.cs`

Application policy remains:

- Connection acquisition timeout: 15 seconds when unspecified.
- Command timeout: 30 seconds when unspecified.
- Existing application pooling behavior preserved.

One-shot health and Hangfire connectivity probes now set `Pooling=false`.
These probes no longer create persistent hidden pools keyed by their modified
connection strings. The application and Hangfire storage pools themselves were
not disabled or resized.

## 4. Verification Evidence

### 4.1 Focused tests

Command:

```powershell
dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj -c Debug --no-build --filter "FullyQualifiedName~CartItemIdentityContractTests|FullyQualifiedName~DomainAuthorityCacheTests|FullyQualifiedName~NpgsqlAcquisitionRemediationTests|FullyQualifiedName~PostgresConnectionStringPolicyTests|FullyQualifiedName~DomainResolutionFoundationTests|FullyQualifiedName~StorefrontPublicationGuardMiddlewareTests|FullyQualifiedName~StorefrontCartCouponRevalidationTests" --nologo --verbosity minimal
```

Historical pre-closure result: **47 passed, 3 failed, 0 skipped, 50 total**.

The three failures at that point were
`StorefrontCartCouponRevalidationTests` cases that fail during SQLite schema
creation with `SQLite Error 1: 'no such function: btrim'`; they do not reach the
  controller or cart behavior under test. These were subsequently fixed and
  rerun to green; see the final section.

The isolated cart identity and event-ordering suite was also run after adding
the final regression:

```text
Passed: 12, Failed: 0, Skipped: 0, Total: 12
```

After fixing the live startup DI conflict, the combined cart identity and
distributed-lock suite passed **15/15** with Debug compilation.

Domain cache coverage includes cold miss/warm hit, concurrent single-flight,
overlapping invalidation/fill fencing, authority isolation, and explicit
transaction commit ordering. Connection-policy coverage verifies non-pooled
one-shot probes and preserved application pooling.

### 4.2 Repository-wide tests

Two bounded full-project attempts were made. The first reached the 10-minute
timeout; the second was stopped because of runtime. Neither produced a final
test count.

Observed failures were outside this remediation and included:

- SQLite attempting to execute PostgreSQL-specific `btrim` and `~` syntax.
- Missing historical migration and frontend files expected by source-inspection
  architecture tests.
- Documentation marker and exact-source-string assertions.
- EF `ManyServiceProvidersCreatedWarning` failures after many independently
  configured test contexts.
- Environment-backed PostgreSQL migration preflight failures.
- Unrelated domain certificate lifecycle expectations.

The incomplete full run is evidence of an existing repository-wide baseline,
not a clean-suite claim and not proof of remediation regression.

### 4.3 Build

Command:

```powershell
dotnet build PointOfSale.API/PointOfSale.API.csproj -c Release --no-restore --nologo --verbosity minimal
```

Result after the DI registration fix: **Build succeeded, 0 errors, 82 warnings**.
Debug compilation after the fix also succeeded. The warnings include nullable,
obsolete API, analyzer, and raw-SQL warnings.

Debug compilation also succeeded as part of the focused test build.

### 4.4 Diff hygiene

`git diff --check` on the remediation files found no whitespace errors. Git
reported only the repository's existing LF-to-CRLF conversion notices.

## 5. Runtime Verification

The user restarted `PointOfSale.API/bin/Debug/net9.0/PointOfSale.API.exe` from
this worktree after the DI fix. PID 4616 listened on both localhost address
families at port 5010. The probes below were read-only and made no cart or
authority mutations.

- `GET /health/live`: **200 Healthy**.
- Eight concurrent `GET /health/live`: **8/8 HTTP 200**, approximately 37 ms
  elapsed for the group.
- `GET /health`: **503 Unhealthy**. The `postgresql` entry reported **Healthy**
  and reachable at `192.168.1.5:5432`; `redis` reported **Healthy**, `enabled:
  true`, `connected: true` and a measured latency of 18.9 ms. The separate
  `redis_latency` entry reported **Degraded** at 51 ms. The two Unhealthy
  entries were `payout_external_truth_readiness` (critical reconciliation
  mismatch count 1) and `CommerceHubSearchSync` (sync ratio 0.4). Outbox
  readiness and the RabbitMQ bus entries were Healthy.
- `GET /api/storefront/store-config` with Host
  `shopixy.localhost:5174`: four sequential **200** responses of 3021
  characters, observed at approximately 402, 143, 202 and 273 ms.
- Four concurrent reads of the same Shopixy store-config route: **4/4 HTTP
  200**, each 3021 characters, approximately 1218 ms total elapsed.
- Established PostgreSQL TCP connections owned by the API: **13 before and
  13 after** the bounded requests. This is a point-in-time process socket
  count, not a measurement of pool acquisition or saturation.

Required runtime follow-up before high-concurrency certification:

1. Confirm production injects a non-empty Redis connection string; the
   development process verified here reported Redis enabled and connected.
2. Measure Redis domain-cache hit/miss and PostgreSQL command counts for
   warmed canonical and custom-domain requests. These probes established
   successful reads but did not instrument hit rates or query counts.
3. In an approved isolated environment, verify cart lock/outbox behavior and
   PostgreSQL acquisition under representative concurrency without altering
   production workload or pool limits.

## 6. Residual Risks and Operational Notes

- Bounded live reads and infrastructure reachability passed. Aggregate
  readiness remains 503 because the payout reconciliation and CommerceHub
  search-sync checks are Unhealthy; Redis latency was also Degraded once.
- Deployment must inject `Redis:ConnectionString`; the production JSON
  placeholder is empty.
- Global domain generation invalidation can evict more authority entries than
  a tenant-specific design. It is correctness-safe but may cause a bounded
  cache refill burst after domain or tenant mutations.
- Read fallback is intentionally available when Redis is disabled or
  unavailable. Mutation invalidation is intentionally fail closed when Redis
  is configured as the enabled shared authority-cache boundary.
- Cart event delivery remains best effort by pre-existing contract. Batching
  reduces lock and outbox pressure but does not convert cart state to a
  PostgreSQL transaction.
- The repository-wide suite requires separate baseline remediation before it
  can serve as a release-wide green gate.

## 7. Earlier Checkpoint (Superseded)

The reviewed MAIN implementation is **code-complete and focused-test certified**
for the Npgsql/Redis remediation scope. It closes the identified repeated-query,
hidden-probe-pool, duplicate-multiplexer, cache-race, and cart-lock/outbox
pressure paths without increasing capacity limits or weakening authority.

This earlier snapshot is superseded by the final certification closure below.
The full repository suite did not finish and no high-VU behavior is claimed.

## FINAL CERTIFICATION CLOSURE

Closure observations were gathered on 2026-09-23 against MAIN; the report
filename retains the original remediation date.

**Verdict: PASS WITH UNRELATED RELEASE CONDITIONS.** The PostgreSQL/Redis
remediation passed its focused gate and bounded runtime proof. The aggregate
readiness failures listed below belong to payout reconciliation and
CommerceHub search synchronization; `/health` is **not** healthy. This verdict
does not assert 2000-VU capacity or a green repository-wide suite.

### Domain-cache runtime and command-count evidence

The MAIN Debug API (PID 8668 during the bounded concurrency run, then PID
13404 after the final observability rebuild, both on port 5010) exports fixed-cardinality
`cache_type="domain"` Redis hit/miss counters and
`domain_authority_sql_lookups_count_total{lookup=...}`. The latter counts each
PostgreSQL authority query *stage reached* by the resolver, not arbitrary SQL
commands elsewhere in the request. Values were read from the existing
`/metrics` endpoint using the Shopixy host header; no diagnostic endpoint or
host-specific metric labels were introduced. The same Redis meter now also
emits `redis_cache_failures_count_total{cache_type="domain",reason="timeout"}`
and `reason="unavailable_or_error"` for cache read/write/refresh/invalidation
failures. Both reasons have fixed low cardinality.

- Unique missing canonical host, two sequential requests: HTTP 400/400;
  canonical SQL-lookup counter advanced **1 → 2** (+1) in this request window;
  domain cache misses **2 → 4** (+2, including the
  lock double-check), hits **1 → 3** (+2, including metric-scrape traffic).
  The second request did not reach PostgreSQL.
- Unique missing custom-shaped host, two sequential requests: HTTP 400/400;
  `custom_domain` **0 → 1**, `storefront_base_url` **0 → 1** (the expected
  fallback on a cold negative lookup), with no further SQL-stage advances on
  the warm request. Domain misses **4 → 6** and hits **4 → 6**; metric scrapes
  themselves also resolve the Shopixy host.
- Sixteen simultaneous requests to another unique missing canonical host:
  **16/16 HTTP 400** in approximately 186 ms. After exporter collection,
  `canonical` rose **2 → 3**, not by sixteen. Domain misses rose **6 → 23**
  (including the lock recheck), and hits rose **7 → 24**. This is live
  same-host single-flight evidence rather than a unit-test-only claim.
- Across the later 224 warmed Shopixy requests, `canonical` stayed **3 → 3**,
  `custom_domain` stayed **1 → 1**, `storefront_base_url` stayed **1 → 1**,
  and domain misses stayed **23 → 23**. Domain hits rose **24 → 249**
  (including the read-only metric scrapes). Repeated requests did not recreate
  the resolver SQL acquisition pattern.
- On the final rebuilt PID 13404, four additional Shopixy store-config reads
  returned **200/200/200/200** with identical 3021-character responses.
  Unique cold/warm canonical and custom-shaped missing hosts returned
  **400/400** in each pair, after which the SQL-stage counters were
  `canonical=1`, `custom_domain=1`, `storefront_base_url=1`. Another sixteen
  simultaneous requests to one new canonical host returned **16/16 HTTP 400**;
  `canonical` increased **1 → 2**, with no custom/base-URL increase. Domain
  hits rose **10 → 26**, misses **4 → 21** (lock rechecks included), while
  both domain Redis failure reasons remained **0 → 0**.

An additional opt-in `DomainAuthorityRealRedisCertificationTests` execution
passed against the locally configured **real Redis server**, with a unique
temporary key prefix and a separate in-memory relational database. It verified
actual Redis generation Lua scripts and distributed refresh locking without
touching shared tenant/domain records:

- Active canonical authority: cold **1** relational read, warm **0**.
- Active custom-domain authority for another tenant: cold **1**, warm **0**.
- Sixteen concurrent negative resolutions: **1** relational read in total;
  subsequent negative hit: **0**.
- A deliberately overlapping stale fill returned its caller's old value but
  was **not republished** after generation advance; `GetAsync` returned null.
- After deactivating the test tenant and invalidating, the custom domain failed
  closed (domain join + base-URL fallback = **2** reads), while the other
  tenant's canonical authority remained available (**1** read after the global
  generation change). No cross-tenant authority was returned.

The isolated relational command counts above were collected through an EF
command interceptor on SQLite; the MAIN API live counters measure the
PostgreSQL resolver query stages. This is not a full per-request Npgsql command
trace. A positive custom-domain row was exercised in the isolated real-Redis
probe rather than inserted into the shared MAIN database.

### Coupon test-provider closure and final gate

The original three `StorefrontCartCouponRevalidationTests` failed before
controller execution because the full production EF model contains
PostgreSQL-only CHECK expressions (`btrim`/regex) that SQLite cannot create.
The test-specific relational context now removes only those unsupported CHECK
constraints, keeps SQLite query/transaction behavior, and seeds a real
currency, tenant and active online-customer authority row for the authenticated
case. Without the active customer, that test properly returned 401; the
fixture now tests the intended coupon behavior after passing the genuine
tenant/customer authority check. All **3/3** coupon revalidation tests pass.
No production coupon validation or tenant-security behavior was bypassed.

Combined Release focused gate, including coupon revalidation, cart identity,
distributed locking, domain authority/cache and its real-Redis probe,
publication/query-count contracts, PostgreSQL connection policy and startup
configuration validation: **60 passed, 0 failed, 0 skipped (60 total)**.
The opt-in real-Redis test uses the local API Redis user-secret; if absent on
another machine it explicitly skips instead of reporting a false runtime pass.
The 60/60 result above was obtained with that test actually executed.
`dotnet build PointOfSale.API/PointOfSale.API.csproj -c Release --no-restore`
also succeeded. The build run for this closure used `-p:WarningLevel=0` to
keep existing warning output manageable; this does not disable compilation or
analyzers as errors.

### Redis startup configuration

`AddRedisCache` now binds `RedisSettings` with `ValidateOnStart()`. An enabled
Redis setting with a null, empty or whitespace-only connection string raises
an `OptionsValidationException` explicitly naming `Redis:ConnectionString`
at host startup. The Redis settings default is empty rather than a silent
`localhost:6379` fallback. Disabled Redis with no endpoint starts using the
intentional fallback; enabled Redis with a configured endpoint passes
validation without connecting during option validation. These cases are
covered by targeted startup tests. No Redis secret is committed.

### Bounded MAIN concurrency and Redis observations

Only read-only Shopixy storefront routes were requested, in sequential steps
of **16 → 32 → 64** simultaneous requests per route. HTTP success/error counts
and observed per-request latency percentiles are:

| Route | Concurrency | HTTP 200 / errors | p50 | p95 | Sampled PostgreSQL TCP peak |
| --- | ---: | ---: | ---: | ---: | ---: |
| Store-config | 16 | 16 / 0 | 1,832 ms | 1,846 ms | 25 |
| Store-config | 32 | 32 / 0 | 3,430 ms | 3,431 ms | 45 |
| Store-config | 64 | 64 / 0 | 5,268 ms | 7,655 ms | 77 |
| Products page 1, size 20 | 16 | 16 / 0 | 3,280 ms | 5,481 ms | 77 |
| Products page 1, size 20 | 32 | 32 / 0 | 5,355 ms | 8,457 ms | 77 |
| Products page 1, size 20 | 64 | 64 / 0 | 9,309 ms | 18,514 ms | 77 |

Across **224/224 successful requests** on PID 8668 the process had 25 established
PostgreSQL TCP connections immediately before the sample, reached a sampled
peak of 77, had 52 immediately after, and later settled to 4. These are
process TCP snapshots, **not** Npgsql active/idle pool gauges or a capacity
guarantee. The p95 product-read latency at 64 is high and is explicitly
retained as an operational observation for VPS sizing/monitoring; it caused no
HTTP errors or renewed domain-resolution SQL fanout in this bounded run.
No cart mutation was sent to the shared MAIN environment; lock-release and
outbox batching are exercised by focused tests instead.

Following the sample, `/health/live` remained HTTP 200. The aggregate
`/health` entry for PostgreSQL was Healthy; Redis was Healthy/connected, and
`redis_latency` was Healthy at approximately 30–32 ms in the subsequent
snapshots. On the final PID 13404, the new instrumented domain cache failure
counters showed **0 timeouts, 0 unavailable/other errors** before and after
the short final cold/warm and concurrency checks; `/health` reported PostgreSQL
and Redis Healthy and `redis_latency` Healthy at 34 ms. This metric covers
domain-cache read/write/refresh/invalidation boundaries, not every Redis
consumer application-wide. No Redis-related HTTP failure occurred and the
isolated real-Redis probe passed. These observations do not rule out latency
spikes at larger concurrency.

### Unrelated release readiness conditions

Aggregate `/health` remains **HTTP 503 / Unhealthy**. The exact unrelated
Unhealthy entries in the final snapshot were:

1. `payout_external_truth_readiness`: latest reconciliation status **Critical**,
   mismatch count **1**.
2. `CommerceHubSearchSync`: search-sync ratio **0.4**, SQL-visible count
   **15** versus Elasticsearch count **6**.

Those systems were not modified as part of this remediation and must be
addressed separately before any release gate requiring aggregate health to be
green. The repository-wide test run remains incomplete; prior unfiltered
runs hit their time limit and showed unrelated fixture/migration/architecture
failures, so **no full-suite pass is claimed**.
