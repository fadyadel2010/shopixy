# POS-ADMIN-CERTIFICATION-01 PASS 10.6 Chaos, Lag & Operational Survivability Certification

Scope: operational survivability across component outage, lag, restart, replay, repair, rebuild, recovery, degraded readiness, and tenant-isolated chaos conditions for the SQL-authoritative POS Admin and CommerceHub platform.

This pass certifies survivability, not zero downtime and not strong consistency. PostgreSQL remains the authority boundary when MongoDB, Redis, Elasticsearch, RabbitMQ, MassTransit consumers, reindex jobs, replay jobs, repair tools, or startup recovery are degraded.

## 1. Failure Inventory Findings

| Failure domain | Blast radius | Recovery mechanism | Observability | Fallback behavior |
|---|---|---|---|---|
| PostgreSQL | Business authority and outbox durability are unavailable | Restore PostgreSQL first; derivative stores rebuild from SQL after restore | database health, readiness gates, logs, SQL-backed outbox lag | fail closed for authoritative writes; derivative stores are not promoted |
| Outbox | SQL commits survive but derivative propagation lags | SQL outbox retries, leases, head-of-line checks, terminal/manual states for specialized flows | projection/outbox readiness, lag counts, repeated failure data, convergence telemetry | SQL remains truth; public reads use SQL overlays or fail closed |
| RabbitMQ | Message transport stalls or reconnects | MassTransit EF outbox redelivery after transport recovery | MassTransit bus outbox readiness, pending/lag telemetry | event payloads stay staged in SQL; no derivative authority promotion |
| MassTransit | normal consumer delivery stalls or duplicates | EF outbox retry, inbox/fence collapse, consumer idempotency | outbox readiness and consumer logs/telemetry | eventual convergence after restart/replay |
| Consumers | projections/search/cache updates lag | worker restart, replay, processed-event fences, stale-version rejection | lag health checks, replay telemetry, processed-event receipts | stale derivative data is candidate evidence only |
| MongoDB | read models unavailable or stale | consumer catch-up, SQL-first rebuild/repair, tombstones, read-time SQL filters | projection drift health, freshness fields, processed-event receipts | public reads fail safely or recompose from SQL where reviewed |
| Redis | cache unavailable, lost, or slow | cache miss, TTL expiry, SQL-read repopulation, fenced invalidation | Redis latency/performance telemetry and cache envelopes | correctness does not depend on cache state |
| Elasticsearch | search unavailable, stale, or alias promotion fails | SQL-first reindex, stale delete, drift reconciliation, auto-healing | search sync health, search telemetry, drift diagnostics | search degrades; visibility still requires SQL authority |
| Reindex | rebuild stalls, lags, or alias promotion fails | bounded `CommerceHubReindexOptions`, SQL-visible count checks, retry/manual rerun | reindex result logs, search health, convergence telemetry | existing index may stay stale, but public visibility remains SQL-filtered |
| Replay | duplicate or mass replay amplifies work | `ProjectionEventFence`, `ProjectionConsumerEventFence`, processed-event receipts, stale-version checks | replay decision telemetry and duplicate-completion diagnostics | duplicates collapse before derivative side effects where reviewed |
| Repair | repair tools run during outage or with stale candidates | `SqlTruthRebuildGuard`, SQL overlays, tenant-scoped repair identity | rebuild diagnostics, health/readiness output, repair reports | repair may delete/tombstone/suppress stale derivative state |
| Startup Recovery | cold start or cluster restart overlaps workers | leases, claims, idempotency, tenant-scoped auto-healing, readiness gates | startup logs, health checks, auto-healing metrics | derivative lag is tolerated; unsafe authority paths fail closed |

No undocumented authority inversion was found on the reviewed public-visibility and operational recovery surfaces. The main hidden dependency remains breadth: future feed/export/CDN purge/support/reporting/global-script paths need their own failure inventory before being treated as certified.

## 2. Mongo Failure Findings

Mongo unavailable or restarting blocks Mongo-backed projection reads and writes, but it does not change PostgreSQL authority. Reviewed storefront product writes use `IProductSqlAuthorityProjectionWriter` and `ProductSearchSqlAuthorityOverlay`; category/CMS/homepage/CommerceHub/affiliate repair paths derive final mutation from SQL and can delete or tombstone stale documents.

Mongo lag is observable through `ProjectionDriftHealthCheck`, outbox readiness, document freshness metadata, processed-event receipts, and bounded convergence telemetry. Public reads either recompose through current SQL authority, filter projected candidates, or fail safely. No reviewed stale-promotion path from Mongo to public visibility was found.

## 3. Redis Failure Findings

Redis unavailable, restarted, emptied, or slow is treated as cache miss or degraded acceleration. Redis keys, cache warmers, invalidation payloads, and cache envelopes are not authority. Reviewed public cache reads filter through current SQL publication/listing/product/CMS/footer authority before return or repopulation.

Duplicate Redis invalidation replay is bounded by durable fences on reviewed product and marketplace invalidation paths. Redis latency and invalidation pressure are observable through `PointOfSale.Redis.Performance` with bounded `cache_type` and `pattern_family` labels. Cache resurrection was not found on reviewed public visibility paths.

## 4. Elasticsearch Failure Findings

Elasticsearch down or lagging degrades discovery/search but does not own visibility. Product, category, CMS, CommerceHub, affiliate, and service search mutation/reindex paths reload SQL authority before indexing and delete or suppress stale documents when SQL authority is missing.

CommerceHub reindex is SQL-first and alias promotion is guarded by current SQL-visible listing counts. `CommerceHubSearchSyncHealthCheck`, `SearchTelemetry`, `ElasticsearchDriftReconciliationJob`, and `OperationalConvergenceTelemetry` surface drift. Search corruption remains recoverable by SQL-first reindex; exact search ranking and all-page facets remain eventual.

## 5. Consumer Failure Findings

Stopped, crashed, restarted, or backlogged consumers create derivative lag, not authority drift. Reviewed consumers use a mix of SQL reload, projection fences, processed-event receipts, freshness/version checks, and fail-closed tombstone/delete behavior.

Lag is visible through projection and MassTransit outbox readiness checks, repeated failure counts, active claim diagnostics, and convergence telemetry. Consumer starvation was not found on the reviewed critical backbones, but future workers must expose their own backlog and poison-message behavior before certification.

## 6. Outbox Failure Findings

Outbox backlog, publish delay, retry storms, and dead-letter growth are survivability conditions. Events remain preserved in SQL-backed outboxes; critical financial and marketplace events are intentionally retryable or routed to explicit terminal/manual states instead of being silently dropped.

Recovery occurs through claim leases, row-version conflict collapse, head-of-line ordering, retry scheduling, dead-letter retry, and readiness gates. Backlog can block readiness, but it does not demote SQL authority or promote queue state.

## 7. Replay Findings

Mass replay, duplicate replay, and recovery replay are expected operating modes. Idempotency survives through `ProjectionEventFence`, `ProjectionConsumerEventFence`, processed-event unique keys, payment-cancellation leases, marketplace projection duplicate-completion cleanup, stale-version validators, and SQL authority recomposition before derivative mutation.

Replay resurrection was not found on reviewed product/category/CMS/homepage/CommerceHub/affiliate/search/cache paths. Replay amplification remains debt for any future recovery worker that does not add an equivalent fence or processed-event receipt.

## 8. Startup Findings

Cold start, worker restart, and partial outage recovery converge through SQL-backed leases, retries, readiness gates, tenant-scoped CommerceHub auto-healing identity, cache-miss repopulation, and SQL-first search reindex/repair.

Startup drift is accepted while bounded and visible. Startup corruption was not found on reviewed recovery paths because startup recovery may discover candidates from derivative systems but must revalidate current SQL authority before public visibility mutation.

## 9. Multi-System Lag Findings

Simultaneous Mongo, Redis, Elasticsearch, consumer, and outbox lag remains eventual. The certified behavior is bounded, observable degradation: SQL continues as truth; Mongo/read projections can lag; Redis may miss or bypass stale envelopes; Elasticsearch can be stale or unavailable; public visibility stays SQL-filtered on reviewed paths.

Convergence eventually returns through event replay, SQL-first repair/rebuild, stale delete/tombstone behavior, cache TTL/invalidation, search reindex, and readiness-guided operation. No convergence deadlock was found on the reviewed chain, but there is no universal cross-store parity daemon.

## 10. Tenant Isolation Findings

Repair, replay, rebuild, and recovery must stay tenant-scoped. Reviewed controls include trusted tenant context, `TenantLineageAuthority`, `TenantScopedDerivativeKey`, tenant-scoped CommerceHub recovery identity `{ TenantId, ListingId }`, tenant-admin search repair restrictions, active tenant checks in affiliate repair, and affected-tenant event contracts.

Cross-tenant recovery leakage was not found on reviewed operational surfaces. The residual risk is operational helper adoption drift where future scripts use local tenant filters instead of shared helpers; those paths must document equivalent SQL lineage controls and add tests.

## 11. Operational Telemetry Findings

Operators can detect reviewed failures through health/readiness checks, outbox lag data, Mongo projection drift, Redis cache/latency telemetry, search sync health, auto-healing metrics, processed-event receipts, document freshness markers, and `OperationalConvergenceTelemetry`.

Invisible outage risk remains for future or non-reviewed background jobs that only log failures. New recovery, cache, feed/export, support, reporting, analytics, and global-script paths must add readiness, telemetry, freshness metadata, or documented repair reports before certification.

## 12. Survivability SLO Findings

Explicit thresholds exist for reviewed operational surfaces: projection and MassTransit outbox warning lag at 5 minutes and critical lag at 15 minutes, payment-cancellation readiness lag and active claims, CommerceHub search sync healthy at 95 percent and degraded above 80 percent, Redis latency diagnostics, and bounded reindex configuration.

These are survivability thresholds, not strong consistency promises. Remaining debt is a formal tenant/surface-specific SLO catalog and adaptive recovery throttling.

## 13. Chaos Findings

The certified chaos scenarios are Mongo Down, Redis Down, ES Down, Consumer Down, Outbox Backlog, Replay Storm, Worker Restart, Startup Recovery, Search Rebuild Failure, Cache Rebuild Failure, and Projection Repair Failure.

The reviewed outcome is consistent: SQL remains authority, derivative systems may degrade, public visibility fails closed or filters through SQL, replay duplicates collapse, repair/rebuild delete or tombstone stale state, lag is observable, and convergence returns after dependency recovery. No unrecoverable divergence, authority inversion, permanent stale state, or tenant leakage was found on reviewed paths.

## 14. Runtime Hardening Performed

This pass did not add a new platform feature. It adds certification and regression-freeze coverage tying PASS 10.1 through PASS 10.5 into an explicit operational survivability boundary for outage, lag, replay, startup, and chaos scenarios.

Existing runtime hardening relied on by this pass includes SQL authority overlays, `IProductSqlAuthorityProjectionWriter`, `SqlTruthRebuildGuard`, `ProjectionEventFence`, `ProjectionConsumerEventFence`, tenant-scoped derivative keys, readiness checks, bounded convergence telemetry, CommerceHub search SQL-visible health checks, Redis performance telemetry, and startup-validated reindex options.

## 15. Monitoring Improvements

The pass codifies the monitoring contract rather than adding a vendor dashboard. Operators must continue to use `/health`, readiness checks, Prometheus/OpenTelemetry meters, logs, processed-event receipts, search sync health, Redis performance telemetry, and repair/rebuild reports.

Reviewed outage signals must remain low-cardinality and bounded. Raw tenant ids, raw keys, raw order ids, payload fragments, and exception text are not approved metric labels.

## 16. Governance Updates

`.ai-context` now records PASS 10.6 as an operational survivability and chaos boundary. Future outage, lag, replay, rebuild, startup, cache, search, projection, support, analytics, feed/export, or global-script work that touches public visibility must document failure behavior, tenant scope, fallback, recovery, observability, SLO thresholds, and regression tests.

## 17. Honest Remaining Debt

The platform remains eventually consistent. There is no zero-downtime claim, no read-your-write guarantee across derivative stores, no universal chaos harness, no universal SQL-vs-Mongo-vs-Redis-vs-Elasticsearch parity scanner, no adaptive scheduler, no authoritative dashboard bundle, and no formal tenant-specific SLO catalog.

Future feed/export/CDN purge/cache-warmer/support/reporting/search-analytics/global-script paths remain uncertified until they receive path-specific SQL authority checks, tenant isolation review, replay/rebuild behavior, failure-mode documentation, and observability.

The guarantee now certified is precise: under reviewed derivative outage, lag, restart, replay, rebuild, and recovery conditions, PostgreSQL authority survives; derivative state may degrade but cannot become authority; recovery converges toward SQL; lag is visible; tenant scope is preserved; and stale public visibility fails closed on the reviewed boundaries.
