# ARCH-5 CommerceHub Recovery, Replay & Operational Resilience Review

Date: 2026-06-25  
Status: PASS - production-certifiable  
Runtime hardening required: No

## Scope

This review inspected CommerceHub runtime recovery, replay, retry, repair, rebuild, hosted-service, worker, projection, cache, search, outbox, broker, payment, shipment, tracking, refund, settlement, payout, reconciliation, admin repair, startup, bootstrap, and maintenance paths. It was performed after the ARCH-1 Business Flow Review, ARCH-2 Domain Boundary Review, ARCH-3 Authority Flow Review, ARCH-4 Event & Read Model Review, and the existing CommerceHub authority certifications.

The proof target was: no recovery path can create, replace, restore, or mutate CommerceHub business authority unless it re-enters current backend SQL authority through the owning service or boundary.

## Recovery Findings

Recovery authority is owned by the current backend SQL row plus the owning backend service boundary. Recovery workers, hosted services, repair services, rebuild jobs, cache warmers, RabbitMQ retries, outbox retries, and admin operations own orchestration only.

Payment, shipment, refund, payout, reconciliation, projection, search, and abandoned-cart recovery all reload current SQL state, use tenant/id predicates, use locks or idempotency where a business transition could occur, and either call the owning domain service, mutate only boundary-owned lifecycle state, rebuild derivative read models, no-op, quarantine, mark reconciliation-required, or fail closed.

Recovery does not recreate deleted authority, restore stale authority, replace SQL from Mongo/Redis/Elasticsearch, or bypass the lifecycle owners.

## Replay Findings

Replay surfaces include idempotency replay, outbox retry, RabbitMQ retry, duplicate requests, duplicate events, duplicate callbacks, webhook replay, projection replay, projection rebuild replay, and hosted-service replay.

`MarketplaceEventOutboxProcessor` preserves per-aggregate ordering, validates tenant and aggregate identity, claims messages, publishes only existing outbox events, and resets failed delivery state only through `RetryFailedMessageAsync`. `MarketplaceProjectionOutboxProcessor` uses `ProcessedEvent` completion fences and removes duplicate projection outbox residue instead of reapplying authority. Payment and refund replay paths rely on provider identifiers, idempotency keys, amount/currency checks, lock keys, and persisted SQL state.

Replay cannot recreate authority, duplicate authority, restore deleted truth, skip SQL, reorder authority, or bypass ownership.

## Retry Findings

RabbitMQ `UseMessageRetry`, outbox retry, provider retry, webhook retry, refund retry, payment-intent recovery retry, Hangfire retry, and hosted-service polling retry are transport or orchestration retry only.

Retries publish existing outbox messages, rerun projection derivation, retry provider-bound operations under persisted idempotency keys, or re-enter lifecycle services after SQL reload. Retry cannot invent a marketplace order, payment, refund, shipment, payout, settlement, or listing.

## Repair Findings

Projection repair and admin repair are derivative-only except where they intentionally re-enter the owning backend boundary. `CommerceHubOrderProjectionRepairService` and `CommerceHubProjectionReconciliationService` rebuild CommerceHub order projections from SQL order/suborder/item authority and use `ProjectionProvesSqlAuthority` plus projection boundary scopes. Storefront order projection repair uses SQL ecommerce order authority, tenant/order/customer predicates, freshness guards, and Mongo replacement only.

Repair cannot restore deleted authority, mutate foreign domains, bypass lifecycle policies, or repair SQL from projections.

## Rebuild Findings

Projection rebuild, Mongo rebuild, Redis recovery, Elasticsearch reindex, search catch-up, homepage/catalog rebuild, discount read-model repair, and admin reindex paths rebuild derivative documents from SQL or invalidate/delete stale derivative state.

`CommerceHubSearchSqlAuthorityOverlay` is the runtime gate for search rebuild. It requires SQL truth, active tenant currency, live listing/product/variant/category/media/stock/price authority, and tombstones non-visible listings. `CommerceHubReindexService` reconciles missing SQL-visible listings and deletes stale search documents absent from current SQL identities.

Rebuild cannot recreate truth, restore deleted truth, skip SQL, or merge stale projections into authority.

## Projection Recovery Findings

CommerceHub projection recovery writes Mongo/read-model state and projection metadata only. Projection metadata mutations are guarded by `CommerceHubBoundaryMutationScope.BeginProjection()`. Freshness guards prevent older projection writes from replacing newer derivative documents. Terminal projection failures are recorded as projection state, not business truth.

Projection recovery cannot create orders, payments, shipments, refunds, earnings, settlements, payouts, products, listings, or tenant authority.

## Mongo Recovery Findings

Mongo index initializers and Mongo projection repair/reconciliation own indexes and derivative documents only. Mongo documents are recomposed from SQL, fenced by source timestamps/versions/checksums, and deleted or bypassed when current SQL authority is absent.

Mongo recovery cannot restore deleted SQL authority or serve as source of truth.

## Redis Recovery Findings

Redis recovery is cache warmup, envelope validation, lag bypass, refresh-lock refresh, and invalidation. `RedisJsonCacheService` deletes corrupt entries, bypasses stale projection-lag entries, and repopulates only through backend factories.

Redis recovery cannot create or restore CommerceHub authority and can only restore availability of cached reads.

## Elasticsearch Recovery Findings

Elasticsearch recovery is search index initialization, alias promotion, reindex, catch-up, stale delete, and drift reconciliation. Every CommerceHub search document is overlaid with current SQL authority, and stale documents are removed when SQL-visible identity is missing.

Elasticsearch recovery cannot recreate listings, products, stock, prices, categories, tenants, or visibility truth.

## Outbox Recovery Findings

The event outbox owns durable event delivery state only. Retry changes failed outbox rows back to retryable delivery state; it does not mutate the source aggregate. The projection outbox owns projection handoff state only and uses processed-event fences for duplicate replay.

Outbox recovery cannot recreate authority and cannot repair SQL from events.

## RabbitMQ Recovery Findings

RabbitMQ owns transport only. Consumer retry and duplicate delivery re-enter idempotent consumers, projection outbox fences, or owning services. Broker failure can delay availability but cannot change SQL truth without a backend service mutation.

RabbitMQ recovery cannot widen tenant scope, duplicate authority, or lifecycle-advance business objects by itself.

## Hosted Service Findings

Hosted services discovered include unresolved payment reconciliation, payout reconciliation, refund recovery, projection reconciliation, storefront projection reconciliation, financial aggregate refresh, payment compensation outbox, checkout payment-intent recovery, shipment authority recovery, cache warmup, Mongo index initialization, Hangfire initialization, seeders, cleanup services, search analytics, token cleanup, promotion expiration, audit logging, and platform startup validation.

Business-touching hosted services either re-enter domain services or mutate only the domain state they own under boundary scopes and SQL locks. Startup/bootstrap services register jobs, warm caches, seed platform/static bootstrap data, or initialize indexes; they do not replace runtime CommerceHub authority.

Hosted services never own CommerceHub business authority.

## Background Worker Findings

Background workers and Hangfire jobs include tracking sync, inventory reservation cleanup, refund evidence cleanup, CommerceHub cart cleanup, idempotency cleanup, trust reconciliation, Elasticsearch drift reconciliation, CommerceHub reindex, finance reconciliation, inventory reconciliation, affiliate ranking, stale return escalation, outbox processors, projection processors, and abandoned-cart recovery consumers.

Workers are availability, cleanup, projection, reconciliation, notification, or owning-service re-entry mechanisms. They do not bypass SQL ownership or tenant validation.

## Failure Recovery Findings

Crash, restart, power failure, broker failure, consumer failure, Mongo failure, Redis failure, Elasticsearch failure, timeout, partial commit, duplicate publish, lost event, and delayed event scenarios recover by replaying durable SQL/outbox state, retrying idempotent provider calls, rebuilding derivative state from SQL, invalidating caches, tombstoning search, no-oping duplicates, or marking reconciliation-required.

Failure recovery can delay visibility or mark operational uncertainty, but it cannot change business truth without current SQL authority.

## Lifecycle Findings

Recovery cannot skip lifecycle. Checkout payment-intent recovery can recreate a missing provider intent only for an existing SQL order and active reservation authority under idempotency; otherwise it expires/cancels through checkout/order lifecycle scopes. Payment recovery re-enters payment lifecycle services. Shipment recovery calls shipment/carrier services. Refund recovery runs refund/financial workflow boundaries. Payout recovery only quarantines stale processing payouts into `ReconciliationRequired`.

Replay cannot advance lifecycle, repair cannot reverse lifecycle, and rebuild cannot bypass lifecycle.

## Concurrency Findings

Concurrent recovery with checkout, payment, shipment, refund, settlement, payout, and parallel hosted workers is constrained by SQL locks, serializable transactions where required, advisory/distributed locks, per-aggregate outbox ordering, idempotency keys, uniqueness constraints, processed-event fences, freshness guards, and boundary scopes.

Concurrent recovery cannot duplicate business actions or duplicate authority; it can no-op, retry later, or mark reconciliation-required.

## Tenant Findings

Recovery, replay, repair, rebuild, hosted services, cache recovery, projection recovery, and admin repair paths preserve tenant scope through tenant/id predicates, trusted tenant context, `TenantLineageAuthority`, tenant-aware projection keys, tenant-scoped cache keys, SQL-visible identity loading, and per-tenant search deletes.

Tenant context cannot disappear into global authority, and tenant authority cannot cross boundaries through recovery.

## Operational Resilience Findings

Operational resilience mechanisms include idempotency, compensation, retry, recovery, repair, monitoring, dead letter handling, outbox processing, circuit-breaker-style fail closed behavior, timeouts, quarantine, reconciliation-required status, cache invalidation, derivative rebuild, and startup validation.

These mechanisms restore availability, delivery, projection freshness, search freshness, cache freshness, or operational certainty. They do not change CommerceHub business truth independently.

## Architectural Risks

No inspected runtime blocker remains. Residual operational proof remains deployed chaos validation for broker outages, retry storms, duplicate/late/missing events, provider timeout-after-external-success, Mongo/Redis/Elasticsearch tampering, projection rebuild races, cache poisoning, concurrent hosted workers, lock infrastructure outages, tenant replacement, and external-provider ambiguity.

Future import, migration, bulk repair, admin repair, provider recovery, or background worker changes must keep the same rule: re-enter the exact current SQL/service owner or fail closed.

## Final Questions

- Who owns Recovery Authority? Current backend SQL plus the owning backend service/boundary.
- Can Recovery recreate authority? No.
- Can Replay recreate authority? No.
- Can Repair recreate authority? No.
- Can Rebuild recreate authority? No.
- Can Retry recreate authority? No.
- Can Hosted Services recreate authority? No.
- Can Background Workers recreate authority? No.
- Can Cache Recovery recreate authority? No.
- Can Projection Recovery recreate authority? No.
- Can Search Recovery recreate authority? No.
- Can Startup Initialization recreate authority? No.
- Can Failure Recovery recreate authority? No.
- Can Operational Resilience change business truth? No; it restores availability and certainty only.
- Can Lifecycle be bypassed? No.
- Can Tenant boundaries leak during recovery? No inspected path permits tenant widening or cross-tenant authority.
- Were runtime changes required? No.
- Is CommerceHub Recovery & Operational Architecture production-certifiable? Yes.

## Runtime Hardening Performed

None. The runtime already enforced SQL/service re-entry for inspected recovery, replay, retry, repair, rebuild, and operational resilience paths.

## Exact Runtime Files Modified

None.

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/CommerceHubRecoveryOperationalResilienceArchitectureCertificationTests.cs`

## Architecture Updates

- `docs/architecture/commercehub-recovery-operational-resilience-review-findings.md`
- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
