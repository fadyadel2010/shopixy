# MAIN M4.5 complete Npgsql remediation final certification - 2026-09-23

## Verdict

**PASS for the bounded MAIN Npgsql/Redis remediation scope, with separate release conditions.** The final source passed the focused authority, cache, cart, payment-provider, transaction-occupancy, tenant-registration and request-normalization gate. A freshly rebuilt Debug process served the remediated storefront paths and the final first-use missing-slug concurrency probe without an HTTP error. This verdict does not claim a healthy aggregate readiness gate, a green repository-wide suite, active Npgsql lease telemetry, or 1000/2000-VU capacity.

The aggregate `/health` and `/health/ready` endpoints remain HTTP 503. Their current non-Healthy entries are recorded below and are not relabeled as passing. No pool ceiling, timeout, Kubernetes resource, k6, or `tests/load/**` change was used to obtain this result.

## Evidence boundary

- Repository: `PointOfSale`, branch `main`.
- The worktree contained extensive pre-existing modified, deleted, and untracked user work before this pass. It was preserved; no reset or broad cleanup was performed.
- The requested source attachments `M4.5_2000VU_NEW_BOTTLENECK_DEEP_DIAGNOSTIC_2026-09-22.md` and `M4.5_NEW_NPGSQL_BOUNDARY_REMEDIATION_REPORT_2026-09-22.md` were not present. Findings were reconciled from the user-provided scope, current source, and the available MAIN reports.
- This remediation pass did not modify LOAD-TEST files or represent LOAD-TEST observations as MAIN evidence; unrelated pre-existing load-test work remains in the worktree.
- Runtime TCP socket counts are point-in-time process observations. They are not active Npgsql lease, wait-queue, or per-request SQL-command counts.

## Remediation closure

### Slug authority and burst coalescing

- Missing slugs now use a tenant-and-slug negative key with a global SEO authority generation fence.
- The negative entry is written only after current product, category, and CMS authority discovery confirms a miss. Positive discovery skips negative-cache publication.
- Redis coordinates cross-instance refreshes; a bounded process-local `SlugDiscoveryFlights` fallback coalesces work when Redis coordination is unavailable.
- The local flight never shares a scoped `DbContext`: followers await only the owner's result task.
- Product discovery has a SQL-authoritative fallback when Mongo projection data is absent or delayed.
- Cached positive candidates remain candidates only. Current SQL authority still validates product, category, CMS, tenant, and slug state.
- `SlugResolverController` no longer repeats tenant lookup already established by middleware and service authority branches.

### Product, search, publication, and cart

- Cold product detail no longer performs a duplicate SQL overlay; warm candidate responses still recompose SQL authority.
- Product batch overlay folds active tenant authority into the SQL product read and retains set-based ancillary authority reads.
- Home categories reuse the current SQL lineage snapshot instead of issuing a redundant category-existence query.
- Product-only and empty unified-search results avoid a redundant tenant command while mixed category/CMS results retain independent tenant authority.
- Publication remains one joined StoreConfig/Tenant authority query. It was not replaced with unsafe domain-cache authority because publication status has an independent mutation boundary.
- Cart mutation candidate reads can occur before the owner lock, but product, variant, stock, price, promotion, tax, cumulative quantity, cart state, and persisted result are revalidated under the lock.
- Deferred cart events remain after token-owned lock release. Merge continues to acquire owner locks in deterministic order.

### Transaction and connection occupancy

| Path | Classification | Final state |
| --- | --- | --- |
| Stripe create/recovery | SHORTENED | Durable local claim commits and disposes before Stripe I/O. Finalization opens a fresh transaction, locks/re-reads order and intent, validates pending state/provider identity, persists provider fields and outbox, then commits. Ambiguous provider failures retain the pending idempotent claim for reconciliation. |
| Paymob create/recovery | SHORTENED | Claim transaction commits and disposes before provider I/O. A unique-violation transaction is rolled back and disposed before any recovery query; recovery uses a fresh transaction. Finalization retries dispose the rolled-back transaction before delay. |
| Generic `PaymentService` / Fawry bridge | SHORTENED | Incomplete persisted claims release and dispose their transaction before readiness/provider continuation. New non-COD claims commit and dispose before provider work. |
| Affiliate registration/resend | SHORTENED | Token state commits and the transaction is disposed before verification email I/O. |
| Tenant registration | SHORTENED | Bootstrap/session state commits, transaction is disposed, and tracking is cleared before post-commit event, email, and accounting-seed work. Scoped bootstrap publishing is awaited rather than fire-and-forget. |
| Projection event fence | LEAK CLOSED | An acquired PostgreSQL advisory lock is released and a connection opened by the fence is closed when post-acquisition checks fail. Cleanup uses non-cancelled best effort and preserves the original exception. |
| Cart coupon mutation | KEEP | Tracked coupon state and durable outbox mutation remain in one SQL transaction. Redis cart refresh occurs after commit. Splitting this would weaken financial/event atomicity. |
| Checkout/order inventory authority | KEEP | Serializable/order transactions retain relational order, stock reservation, idempotency, and outbox invariants. No unrelated Redis/Mongo/provider await was proved inside the retained core boundaries. |
| Readers and one-shot probes | BOUNDED | Reviewed explicit readers are consumed and disposed in their SQL operation. Health/Hangfire availability probes use the non-pooled one-shot policy. |

The audit searched explicit transaction, connection-open, command, and reader paths and traced the provider/registration paths identified by the earlier bottleneck review. Concrete external-await and failure-cleanup defects found in that audit were fixed above. No pool-size or timeout increase was made.

### Connection topology

- `ApplicationDbContext` uses the normalized `ConnectionStrings:DefaultConnection` value.
- MassTransit EF outbox uses `ApplicationDbContext` and therefore that application data-source boundary.
- Hangfire storage is an independent provider/storage boundary even when configured from the same normalized connection string; it must not be counted as the application pool solely because host/database text matches.
- One-shot PostgreSQL health/availability probes explicitly disable pooling.
- Design-time factories are not runtime request pools.
- No `MaxPoolSize`, connection timeout, or command timeout increase was introduced.

## Duplicate-authority conclusions

| Area | Conclusion |
| --- | --- |
| Domain and tenant resolution | Redis is a fenced candidate/negative cache; current SQL-fill authority and transactional invalidation remain authoritative. Request middleware reuses its established result. |
| Publication | Storefront publication is separate mutable StoreConfig authority and keeps one joined SQL query. Domain activity alone cannot prove publication. |
| Store config/theme | Tenant config and tenant lifecycle are joined. Global theme fallback remains a separate read because its ownership and fallback precedence differ. |
| Product/detail/list/search/home | Redis, Mongo, and Elasticsearch provide candidates. Set-based SQL recomposition remains final authority; only demonstrably duplicated reads were removed. |
| Slug | Positive cache entries remain SQL-revalidated candidates. Negative misses are short-lived, generation-fenced, and published only from a SQL-confirmed miss. |
| Cart coupon | Persisted cart coupon state, current coupon definition, customer usage, reservation, tracked mutation row, and outbox are distinct authorities, not duplicate reads. |

## Coupon authority matrix

| Stage | Authority | Required behavior |
| --- | --- | --- |
| Cart response | Tenant/cart-scoped `StorefrontCartCouponStates` | Read exactly once even when Redis appears coupon-free; SQL may be newer than the cart projection. |
| Coupon definition | Current tenant coupon row | Validate active/deleted state, validity window, limits, amount/type, and applicable scope. |
| Customer usage | SQL usage records | Enforce per-customer and aggregate use limits from durable records. |
| Reservation | SQL reservation/current ownership | Validate cart/customer ownership, expiry, and concurrent reservation state. |
| Mutation | Tracked coupon-state row inside transaction | Re-read under the mutation transaction; do not reuse an earlier untracked response snapshot. |
| Event durability | EF outbox in the same transaction | Coupon state and event commit or roll back together. |
| Redis cart | Derivative response cache | Refresh only after commit; a refresh failure cannot undo the durable financial mutation. |

Focused relational tests cover coupon-free, same-tenant active coupon, foreign-tenant state, commit, outbox-failure rollback, and post-commit Redis refresh failure. No financially authoritative SQL result was replaced with a cross-request cache.

## Cart A-G classification

| Class | Work | Placement |
| --- | --- | --- |
| A | Current cart load, item identity, and state-dependent decisions | Inside the token-owned cart lock. |
| B | Pure request shape/range validation | Before lock acquisition. |
| C | Mongo/read-model product candidate retrieval | Before the lock when useful; never final authority. |
| D | Caller, tenant, and owner/session verification | Before lock acquisition, with locked cart ownership still enforced. |
| E | SQL product/variant/stock/price/promotion/tax revalidation, cumulative quantity, recalculation, and Redis persist | Inside the lock because these depend on current cart state. |
| F | Event/outbox staging and publish batch | After lock release; source tests assert ordering and failure behavior. |
| G | Merge/multi-cart mutation | Acquire sorted owner keys, mutate/persist while both are held, release all token-owned locks even when renewal reports a conflict. |

This shortens the lock without accepting stale candidate authority. No live cart mutation was sent to the shared MAIN data during certification.

## Runtime-discovered concurrency defect

The first fresh 16-way slug burst returned 14 expected 404 responses and two HTTP 500 responses. The API log identified an independent MVC-filter race:

`System.ArgumentException: An item with the same key has already been added. Key: PointOfSale.API.Controllers.SlugResolverController`

`ApiRequestNormalizationFilter` shared one static `NullabilityInfoContext`; concurrent first-use reflection raced in its internal cache. Access to that context is now serialized by a narrow lock. The filter remains scoped and request execution remains concurrent. `ApiRequestNormalizationFilterTests.OnActionExecutionAsync_ConcurrentPrimitiveValidation_IsThreadSafe` drives 128 concurrent validations through this path.

After rebuilding and restarting, the first MVC action burst on the new process completed 16/16 with the expected 404 status. The earlier two 500s are retained here as discovery evidence and are not included as final patched-binary results.

## Verification

### Tests and builds

- Current-source combined Release gate: **489 passed, 0 failed, 0 skipped**. Filter included request normalization, Npgsql acquisition, product batch authority, publication, cart identity/coupon, Redis domain authority including real Redis, connection policy, storefront search reliability, distributed locks, slug flights, transaction occupancy, Stripe, Paymob, tenant registration, and projection fence coverage.
- Focused normalization suite after the concurrency fix: **6/6 passed**.
- Earlier broad payment/provider/transaction checkpoint: **394/394 passed**; superseded by the 489-case current-source gate.
- Release API build after all source edits: **succeeded, 0 warnings, 0 errors**.
- Debug API build after all source edits: **succeeded, 0 warnings, 0 errors**.
- Scoped `git diff --check`: no whitespace errors; Git emitted only LF-to-CRLF working-copy notices.

Known broader-suite conditions remain outside this remediation gate:

- `AffiliateEmailVerificationLifecycleTests` encounters the existing SQLite PostgreSQL-expression incompatibility `near "~": syntax error`.
- `SemanticConvergenceChaosValidationTests.ActorAuthorityChaos_RejectsStaleJwtAndDelayedAdminRepairReplay` has the existing expected/actual semantic mismatch `current_actor_role_not_authorized` versus `tenant_inactive_or_deleted`.

### Fresh Debug runtime

Final process:

- PID: `15132`
- Binary: `PointOfSale.API/bin/Debug/net9.0/PointOfSale.API.exe`
- Port/environment: `http://localhost:5010`, Development
- Process start UTC: `2026-09-23 14:25:40`
- Debug DLL write UTC: `2026-09-23 14:24:18`
- Normalization-filter source write UTC: `2026-09-23 14:20:45`

The DLL is newer than the final runtime-fix source edit.

| Probe | Result | Approximate elapsed/body evidence |
| --- | --- | --- |
| `GET /health/live` | HTTP 200 | Process live. |
| `GET /health/ready` | HTTP 503 | Aggregate readiness conditions below. |
| `GET /health` | HTTP 503 | `postgresql=Healthy`, `redis=Healthy`. |
| `GET /api/storefront/store-config` | HTTP 200 | 363 ms; 3021-byte body. |
| `GET /api/storefront/products?page=1&pageSize=20` | HTTP 200 | 425 ms; 12295-byte body. |
| `GET /api/storefront/search?q=chair&page=1&pageSize=20` | HTTP 200 | 138 ms; 144-byte body. |
| `GET /api/storefront/products/by-id/cb81e893-e41f-fc67-dd39-d7c9f4afaa33` | HTTP 200 | 216 ms; 1971-byte body. |
| `GET /api/storefront/resolve-slug/realme-gt-neo-3` | HTTP 200 | 59 ms; 163-byte body. |

All storefront probes used Host `shopixy.localhost:5174` and tenant `83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5`.

Final missing-slug first-use burst on slug `main-final-patched-92f15564c0734bd29934438498d34f9b`:

- Requests: 16 simultaneous, read-only.
- Responses: **16/16 HTTP 404**, zero HTTP errors.
- Burst elapsed: approximately **748 ms**.
- `seo-slug-negative` metrics from zero: hits **15**, misses **17** (initial reads plus lock recheck), refresh histogram count **1**.
- Sampled established API-to-PostgreSQL sockets: peak **25**, post-run **25**.
- Domain Redis failures: **0 timeout**, **0 unavailable/error**.

This proves bounded same-process/cross-request coalescing on the final binary. It is not a capacity or exact pool-occupancy measurement.

## Aggregate readiness conditions

The final `/health` snapshot was `Unhealthy` while PostgreSQL and Redis connectivity were Healthy:

| Check | State | Current description |
| --- | --- | --- |
| `projection_drift` | Degraded | 23 documents of minor drift. |
| `payout_external_truth_readiness` | Unhealthy | Payout state is not safely reconciled with external truth. |
| `redis_latency` | Degraded | 61 ms in the final snapshot. |
| `stock_reservation` | Degraded | One stale reservation holding 1.00 units. |
| `certificate_provider` | Degraded | Provider execution disabled. |
| `caddy_edge` | Degraded | Edge integration disabled. |
| `CommerceHubSearchSync` | Unhealthy | Search synchronization ratio unhealthy. |

These conditions block an aggregate-ready release declaration. The bounded remediation verdict means the changed authority/occupancy paths behaved as specified; it does not waive these operational signals.

## Final limitations

- No high-VU MAIN traffic was generated.
- No shared-data cart write or real payment-provider mutation was executed; those invariants are covered by focused deterministic tests and source architecture guards.
- TCP socket sampling cannot prove active/idle Npgsql pool composition or acquisition wait time.
- Missing diagnostic attachments prevent claiming byte-for-byte reconciliation with those unavailable documents.
- The user-held API process may be stopped after reviewing this evidence.
