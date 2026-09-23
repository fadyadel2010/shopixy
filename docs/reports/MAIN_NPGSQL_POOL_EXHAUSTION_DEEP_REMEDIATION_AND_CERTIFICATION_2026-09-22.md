# MAIN Npgsql Pool Exhaustion Deep Remediation and Certification — 2026-09-22

> Supersession note: the later MAIN Npgsql/Redis remediation activates the
> Redis-backed domain authority cache and mutation invalidation. Statements in
> this report that caching was deferred describe the earlier certification
> point only; see
> `MAIN_NPGSQL_REDIS_COMPLETE_REMEDIATION_CERTIFICATION_2026-09-22.md` for the
> current result.

## 1. Executive Summary

MAIN Shopixy PostgreSQL connection-acquisition remediation is complete on the
storefront hot path without changing pool size, timeouts, SQL authority,
tenant isolation, authorization, outbox durability, cart locking, or
correctness.

- Tenant resolution now issues 1 relational command for canonical slug hosts
  and for matching authenticated domains, instead of first issuing a
  `StorefrontBaseUrl` query that is expected to miss on every normal
  storefront request.
- `/api/storefront/store-config` no longer pays for two publication checks.
  The middleware defers to the controller, which enforces active,
  `TenantStatus.Active`, non-deleted, and `"live"` publication state in one
  joined StoreConfig/Tenant authority query.
- Cart revalidation no longer issues per-item product reads. It issues one
  `GetByProductIdsAsync` plus one set-based `ApplyBatchAsync` SQL authority
  overlay for the whole cart, and memoizes identical affiliate admission
  checks. Cancellation is preserved.
- Pool/timeout policy is unchanged: connection timeout 15s, command timeout
  30s via `PostgresConnectionStringPolicy`.
- At this certification point, cross-request domain caching remained deferred. Only
  `NoOpDomainResolutionCache` exists; complete mutation invalidation and
  multi-instance convergence are unproven.
- Live verification on the running API shows warmed Shopixy storefront
  success with stable PostgreSQL TCP usage and healthy PostgreSQL/Redis/
  MongoDB/Elasticsearch/RabbitMQ health entries. Aggregate `/health` remains
  `Unhealthy` because of unrelated pre-existing payout/search-sync readiness
  failures, not Npgsql connectivity or pool exhaustion.
- Focused regression re-run in this session: 9/9 passed
  (`NpgsqlAcquisitionRemediationTests` +
  `StorefrontPublicationGuardMiddlewareTests`).

Verdict: **PASS for the remediated acquisition surface; CONDITIONAL for
full production pool behavior under 1000/2000-VU load** because the named
M4.5 diagnostic and 1000/2000-VU reports are absent from MAIN and no
high-VU replay was executed from MAIN in this session.

## 2. Repository, Constraints, and Worktree Discipline

- Repository: `C:\Users\fadya\source\repos\PointOfSale`
- Branch: `main`
- Commit: `24e1f3a`
- Remote: `https://github.com/fadyadel2010/shopixy.git`
- Absolute constraint honored: **NO SUBAGENTS**. All investigation, edits,
  test execution, live probes, and reporting were done directly.
- Worktree is heavily dirty; all unrelated user changes were preserved.
  This report certifies only the remediation files in §4.
- Never entered or edited the separate load-test copy, Kubernetes assets,
  M4.5 material, k6 workloads, or `tests/load/*`.
- Did not increase Npgsql pool or timeout settings.
- Did not restart or stop the user's API on `http://localhost:5010`.
- Listener observed earlier in the session as PID `13464`
  (`PointOfSale.API\bin\Debug\net9.0\PointOfSale.API.exe`). At final live
  verification the listening owner was PID `19888` on the same executable
  path. This agent did not restart the API; the PID change is recorded as
  observed runtime state, not as an agent action.
- MAIN dependencies used for live checks:
  - PostgreSQL `192.168.1.5:5432`
  - Redis `192.168.1.5:6379`
  - Elasticsearch `192.168.1.5:9200`
  - RabbitMQ `192.168.1.5:5672`
  - MongoDB `192.168.109.128:27017`

## 3. Remediation Scope

### 3.1 Tenant resolution

File: `PointOfSale.API/Middleware/TenantResolutionMiddleware.cs`

- `ResolveRequestTenantAsync` now accepts the already-resolved
  `authenticatedTenant`.
- When `DomainResolutionResult` identifies the same tenant as the
  authenticated tenant authority, the middleware reuses that request-scoped
  entity instead of issuing a second tenant lookup.
- Mismatched authenticated/host tenants still load the host tenant and fail
  closed with `403` below.
- `ResolveTenantFromCandidateUriAsync` now resolves canonical template hosts
  directly by slug. It no longer issues a `StorefrontBaseUrl` query first on
  every normal storefront request. Custom-domain hosts still fall back to
  `ResolveTenantFromStorefrontBaseUrlAsync`.

Query-count behavior proven by tests:

- Canonical slug resolution: 1 relational command.
- Matching authenticated domain: 1 relational command.
- Mismatched authenticated domain: fail-closed `403`, 2 commands.

### 3.2 Store-config publication gate

Files:

- `PointOfSale.API/Middleware/StorefrontPublicationGuardMiddleware.cs`
- `PointOfSale.API/Controllers/StorefrontStoreConfigController.cs`

- Exact `/api/storefront/store-config` is excluded from the middleware
  publication query because its controller already reads the complete SQL
  StoreConfig/Tenant authority.
- The controller uses one joined query filtered by:
  - `config.TenantId == tenantId`
  - `Tenant.IsActive`
  - `Tenant.Status == TenantStatus.Active`
  - `Tenant.DeletedAtUtc == null`
- Missing authority returns `STOREFRONT_CONFIG_NOT_FOUND`.
- Non-`"live"` `StorefrontStatus` returns fail-closed
  `STOREFRONT_NOT_PUBLISHED`.
- Inactive, suspended, inactive-flag, and soft-deleted tenant states all fail
  closed in one relational command.

### 3.3 Cart revalidation batching

File: `PointOfSale.API/Services/Cart/CartService.cs`

- Revalidation collects distinct non-empty cart `ProductId` values once.
- It calls one batch `GetByProductIdsAsync`.
- It filters to current-tenant candidates and applies one set-based
  `ProductSearchSqlAuthorityOverlay.ApplyBatchAsync`.
- Per-line checks after that are in-memory except attributed affiliate
  admission.
- Identical affiliate `(ProductId, AccountId, ReferralCode, PromotionLinkId)`
  tuples are memoized per revalidation pass.
- `OperationCanceledException` is rethrown when the caller requested
  cancellation; other exceptions retain the prior warning-and-return path.
- The same `CartService.cs` diff also contains canonical simple-product SKU
  identity hardening from the parallel cart-identity work. That behavior is
  preserved here but is not claimed as an Npgsql acquisition fix beyond the
  batch revalidation and affiliate memoization described above.

Contract behavior proven by tests:

- Revalidation uses one batch product read.
- No per-item `FindByProductIdAsync`.
- Legacy simple lines upgrade to canonical SKU identity without changing
  cart identity.

## 4. Final Diff Inventory

Tracked remediation modifications verified with `git diff --stat`:

- `PointOfSale.API/Controllers/StorefrontStoreConfigController.cs`
- `PointOfSale.API/Middleware/StorefrontPublicationGuardMiddleware.cs`
- `PointOfSale.API/Middleware/TenantResolutionMiddleware.cs`
- `PointOfSale.API/Services/Cart/CartService.cs`
- `PointOfSale.Tests/Middleware/StorefrontPublicationGuardMiddlewareTests.cs`

Remediation test assets:

- `PointOfSale.Tests/Infrastructure/NpgsqlAcquisitionRemediationTests.cs`
  — new, untracked, part of this remediation.
- `PointOfSale.Tests/CartItemIdentityContractTests.cs`
  — untracked, includes the cart batch repository-call contract used as
  acquisition evidence.
- `PointOfSale.Tests/Infrastructure/ProductSearchSqlAuthorityBatchTests.cs`
  — untracked, prior set-based SQL authority batch evidence reused for this
  certification.

`git diff --check` on the tracked remediation files reports no whitespace
errors apart from pre-existing LF/CRLF warnings.

The broader worktree contains many unrelated tracked modifications and
untracked files from other user workstreams. Those were not reverted and are
not part of this certification. The remediation certification is limited to
the files listed above.

## 5. Representative PostgreSQL Acquisition Inventory

### 5.1 Storefront read path

- At this certification point, domain resolution remained persistence-backed with an explicit no-op cache.
- Tenant resolution after remediation:
  - Anonymous canonical host: 1 tenant command.
  - Authenticated matching domain: 1 tenant command total.
  - Authenticated mismatch: 2 tenant commands, fail-closed `403`.
- Store-config path:
  - Middleware: 0 publication commands for exact store-config.
  - Controller authority: 1 joined StoreConfig/Tenant command for
    inactive/deleted/unpublished fail-closed cases.
  - Theme preset lookup remains a separate bounded query only after authority
    succeeds.
- Product list/search/discovery/home authority continues to use the prior
  set-based `ApplyBatchAsync` / `ApplyBatchByIdsAsync` /
  `ApplyBatchByTenantAsync` remediation from 2026-09-19 and 2026-09-20.
  Those paths remain sequential `await` on a single scoped `DbContext`; no
  new parallelism was introduced.

### 5.2 POS and order write paths

Representative findings:

- `PointOfSale.API/Controllers/SalesController.cs`:
  - One explicit transaction per sale.
  - Batched sale-item preparation followed by
    `LockMultipleStocksAsync` for distinct warehouse/variant targets.
  - Accounting outbox staging and synchronous accounting execution remain
    inside the sale transaction; stock events publish only after commit
    preparation.
- `PointOfSale.API/Controllers/OrdersController.cs`:
  - `CalculateAuthoritativeCreateOrderFinancialsAsync` batches product,
    product-unit, and default-variant authority with three set-based queries
    before the per-item in-memory loop.
  - Shipping-method and coupon lookups remain optional scalar queries.
  - One transaction covers idempotency claim, order, reservation, coupon
    reservation, marketplace outbox enqueue, and transactional MassTransit
    publish. Expensive DTO enrichment and minimal replay-payload update occur
    after commit.
- `PointOfSale.API/Services/Pos/PosCartRuntimeService.cs` remains the POS
  checkout boundary used before sale persistence.

No per-item Npgsql fanout was found in the representative POS/order
authority preparation beyond the already-bounded patterns above.

### 5.3 MassTransit and outbox paths

- `PointOfSale.API/Program.cs`:
  - `AddDbContext<ApplicationDbContext>` remains non-pooled scoped.
  - Uses `UseNpgsql(defaultConnectionString)` with security interceptors.
  - MassTransit uses `AddEntityFrameworkOutbox` with Postgres and
    `UseBusOutbox`, `QueryDelay = 1s`.
- `PointOfSale.Infrastructure/EventBus/MassTransitEventPublisher.cs`:
  - Publishes through `IPublishEndpoint` so the bus outbox can stage the
    message in the current `DbContext`.
  - Flushes with `SaveChangesAsync` only when there are no pending domain
    mutations left, preserving transactional outbox behavior.
  - `PublishSensitiveAsync` intentionally bypasses the bus outbox via `IBus`
    and is blocked for authoritative event types.
- `PointOfSale.Infrastructure/Jobs/OutboxPublisherJob.cs`:
  - Background loop polls every 5 seconds, selects up to 100 pending IDs,
    then processes messages sequentially.
  - Each message gets its own DI scope and serializable transaction.
  - PostgreSQL path uses `SELECT *, xmin ... FOR UPDATE` to preserve the
    `xmin` concurrency token while holding the row lock.
  - The connection/transaction is intentionally held across broker publish
    for row-lock correctness. This is bounded by sequential batch processing,
    not request concurrency.
- `PointOfSale.Infrastructure/Eventing/MarketplaceEventOutboxProcessor.cs`:
  - Sequential per-message DI scopes.
  - Claim path commits a short lease transaction with
    `pg_advisory_xact_lock`, then releases it before broker publish.
  - Publish/failure marking uses a separate bounded unit of work.
- Accounting, marketplace projection, stock-sync, and payment-cancellation
  outbox processors follow the same sequential claim-then-process pattern.

Conclusion: no request-sized outbox connection fanout was introduced. The
only cross-operation hold is the deliberate generic outbox row lock across
publish, which is sequential and bounded.

## 6. Pool and Timeout Policy

`PointOfSale.Infrastructure/Configuration/PostgresConnectionStringPolicy.cs`
is unchanged:

- Default connection timeout: 15 seconds.
- Default command timeout: 30 seconds.
- Health-check normalization uses a shorter 5-second timeout and does not
  affect request-path policy.
- No `MaxPoolSize`, `MinimumPoolSize`, multiplexing, retry, or DbContext
  pooling change was made.

## 7. Deferred Domain Caching

- `PointOfSale.Core/Interfaces/IDomainResolutionCache.cs` defines get/set/
  remove for normalized hosts, including negative-result caching and
  invalidation on `DomainVerified`, `DomainDeleted`, and
  `DomainPromotedToPrimary`.
- `PointOfSale.API/Middleware/NoOpDomainResolutionCache.cs` explicitly
  returns miss and performs no storage.
- Activation remains deferred because complete post-commit invalidation and
  multi-instance convergence are unproven. No Redis, memory, or negative
  caching was enabled as part of this remediation.

## 8. Builds and Tests

### 8.1 Fresh focused verification in this session

Command:

```powershell
dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj -c Debug --filter "FullyQualifiedName~NpgsqlAcquisitionRemediationTests|FullyQualifiedName~StorefrontPublicationGuardMiddlewareTests" --nologo --verbosity minimal
```

Result:

- `Passed! - Failed: 0, Passed: 9, Skipped: 0, Total: 9`
- Duration: `1 m 12 s`
- Covers:
  - Canonical slug one-command resolution.
  - Matching authenticated-domain reuse with one command.
  - Mismatched authenticated-domain fail-closed behavior.
  - Four store-config fail-closed authority cases.
  - Store-config middleware controller deferral.

### 8.2 Prior focused evidence reused

- `CartItemIdentityContractTests`: 11 passed.
- `ProductSearchSqlAuthorityBatchTests`: 10 passed, including 1-to-2000
  product bounded-command evidence.
- `ProductionSecurityHardeningTests` plus `StorefrontTenantIsolationTests`:
  36 passed.
- Debug API build: succeeded, `0 Warning(s)`, `0 Error(s)`.
- Release API build: succeeded with pre-existing warnings and no errors.
- Test project rebuilt successfully.
- Broader storefront architecture run: 41 passed, 10 unrelated/pre-existing
  certification source/document assertions failed, including missing
  `"PASS SF-PA1.4"` evidence. Those failures are unrelated to this
  acquisition remediation.

## 9. Live Verification

The running API was observed only; it was not restarted or stopped.

### 9.1 Dependencies and health

All five DELL dependency TCP endpoints remain reachable.

`/health/live`:

```json
{
  "status": "Healthy",
  "entries": {}
}
```

`/health` aggregate:

- Status: `Unhealthy`
- Entry count: 37
- Healthy infrastructure entries:
  - `postgresql`: Healthy, reachable.
  - `redis`: Healthy, reachable.
  - `mongodb`: Healthy, reachable.
  - `elasticsearch`: Healthy, reachable.
  - `rabbitmq`: Healthy, reachable.
- Unrelated failures:
  - `payout_external_truth_readiness`: Unhealthy.
  - `CommerceHubSearchSync`: Unhealthy.
  - Degraded: `projection_drift`, `redis_latency`, `stock_reservation`,
    `certificate_provider`, `caddy_edge`.
- No Npgsql connectivity or pool-exhaustion health failure was reported.

### 9.2 Representative warmed requests

Host: `shopixy.localhost:5174`

Store-config, five sequential warmed calls:

- `200 3.288942 3021`
- `200 0.404511 3021`
- `200 0.319596 3021`
- `200 0.229151 3021`
- `200 0.286654 3021`

Products `?page=1&pageSize=20`, five sequential warmed calls:

- `200 4.234784 12299`
- `200 0.754321 12299`
- `200 0.434154 12299`
- `200 0.360644 12299`
- `200 0.347700 12299`

The first call in each sequence is cold; subsequent calls stabilize at
approximately 0.22–0.40s for store-config and 0.34–0.75s for products.
Response sizes are stable at 3021 and 12299 bytes respectively.

Earlier in the session the same Shopixy paths returned `200` with 2.20s and
2.85s cold timings. `mena.localhost:5174` and `anatico.localhost:5174`
returned `400`, consistent with unresolved/nonexistent live tenant hosts.

### 9.3 PostgreSQL connection stability

Per-process TCP snapshots for the API listener:

- Earlier: 26 `Established` connections to `192.168.1.5:5432`.
- After ten warmed store-config calls: still 26 established.
- After ten warmed product calls: still 26 established.
- Final verification: 14 `Established` connections to `192.168.1.5:5432`,
  plus 1 RabbitMQ, 4 Redis, 5 Elasticsearch, and 7 MongoDB established
  connections.

The pool did not grow across the warmed request sequences; the final lower
snapshot is consistent with idle-pool settling, not exhaustion. These are
point-in-time TCP observations, not a substitute for high-VU pool telemetry.

`/metrics` did not expose Npgsql/pool counters to the local probe, so no
pool-gauge series is claimed.

## 10. Load-Test Hygiene and Missing Evidence

`tests/load` status was inspected but not modified.

Pre-existing baseline changes remain exactly as recorded:

- Modified:
  - `tests/load/data/manifests/latest.json`
  - `tests/load/python/orchestrate.py`
- Multiple pre-existing untracked reports, scripts, and assets remain.

Required reports remain absent from MAIN:

- `tests/load/results/M4.5_2000VU_API_INTERNAL_BOTTLENECK_DIAGNOSTIC_REPORT.md`
- Named 1000-VU/4-CPU report.
- Named 1000-VU/8-CPU report.
- Named 2000-VU/8-CPU report.

This limitation is documented without seeking another copy, entering another
checkout, or reconstructing load results. Consequently, high-concurrency pool
exhaustion cannot be declared fully eliminated from MAIN evidence alone.

## 11. Limitations and Follow-Ups

1. No 1000/2000-VU replay was executed from MAIN in this session.
2. TCP snapshots prove stability under sequential warmed load, not pool-head
   behavior under sustained concurrency.
3. No safe domain-cache activation until complete post-commit invalidation
   and multi-instance convergence are proven.
4. The broader architecture suite still has 10 unrelated pre-existing
   certification failures.
5. Aggregate `/health` still reports unrelated payout and search-sync
   readiness failures.
6. The worktree remains heavily dirty outside the remediation files; future
   certification should isolate or commit unrelated changes before a clean
   high-VU gate.

## 12. Relevant Files

- `PointOfSale.API/Middleware/TenantResolutionMiddleware.cs`
- `PointOfSale.API/Middleware/StorefrontPublicationGuardMiddleware.cs`
- `PointOfSale.API/Controllers/StorefrontStoreConfigController.cs`
- `PointOfSale.API/Services/Cart/CartService.cs`
- `PointOfSale.Tests/Infrastructure/NpgsqlAcquisitionRemediationTests.cs`
- `PointOfSale.Tests/Middleware/StorefrontPublicationGuardMiddlewareTests.cs`
- `PointOfSale.Tests/CartItemIdentityContractTests.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs`
- `PointOfSale.Tests/Infrastructure/ProductSearchSqlAuthorityBatchTests.cs`
- `PointOfSale.Infrastructure/Data/Repositories/PersistenceDomainResolver.cs`
- `PointOfSale.API/Middleware/NoOpDomainResolutionCache.cs`
- `PointOfSale.Core/Interfaces/IDomainResolutionCache.cs`
- `PointOfSale.Infrastructure/EventBus/MassTransitEventPublisher.cs`
- `PointOfSale.Infrastructure/Jobs/OutboxPublisherJob.cs`
- `PointOfSale.Infrastructure/Eventing/MarketplaceEventOutboxProcessor.cs`
- `PointOfSale.Infrastructure/Configuration/PostgresConnectionStringPolicy.cs`
- `PointOfSale.API/Program.cs`
- `PointOfSale.API/Controllers/SalesController.cs`
- `PointOfSale.API/Controllers/OrdersController.cs`
- `PointOfSale.API/Services/Pos/PosCartRuntimeService.cs`
- `PointOfSale.API/appsettings.Development.local.json`
- `docs/reports/STOREFRONT_SQL_FANOUT_DEEP_REVIEW_AND_CERTIFICATION_2026-09-20.md`
- `docs/reports/STOREFRONT_PRODUCTS_SQL_FANOUT_REMEDIATION_2026-09-19.md`
