# POS-ADMIN-CERTIFICATION-01 PASS 7 Replay / Rebuild / Chaos Certification

Date: 2026-05-25

## Scope

This pass certified replay storms, rebuild overlap, delayed recovery, queue lag, worker restart behavior, cache/search rebuild overlap, dead-letter retry, replay amplification, operational recovery pressure, and recovery auditability across the SQL-first POS Admin and CommerceHub platform.

The target is deterministic replay-safe convergence under operational chaos. It is not synchronous consistency, a queue rewrite, or removal of replay/rebuild tooling.

## Boundaries Audited

- `ProjectionEventFence` and `ProjectionConsumerEventFence` duplicate/active replay collapse.
- Marketplace event and projection outbox processors, dead-letter retry reset, claim leases, and duplicate completion cleanup.
- Stock-sync and accounting outbox processors, aggregate ordering blockers, claim leases, retry behavior, and SQL authority suppression.
- Payment cancellation recovery outbox and its interaction with `IPaymentLifecycleService`.
- CommerceHub search reindex, homepage/recommendation projection, marketplace product read-model rebuild, and tenant/global recovery split.
- Storefront product search/admin repair, product read-model repair, cache invalidation boundaries, and affiliate AF16E repair.
- Financial replay paths for settlement/refund/accounting authority, including delayed replay and immediate/replayed posting parity.
- Operational runbooks, dangerous primitive governance, and convergence telemetry.

## Replay Storm Certification

Validated guarantees:

- Duplicate EventId delivery on Tier 1 projection paths collapses through durable processed-event fences or equivalent inbox/outbox completion records before derivative side effects.
- Product, CommerceHub, affiliate, homepage, CMS, coupon, shipping, config, payment-method, cache, review, and promotion replay paths re-read SQL authority or fail closed through documented local equivalents.
- Accounting, stock-sync, marketplace event, and marketplace projection outboxes keep aggregate or warehouse/order-scoped ordering instead of allowing newer work to bypass unresolved earlier authority.

Finding hardened in this pass:

- Payment cancellation recovery was bounded for throughput but did not claim or lease rows before calling `IPaymentLifecycleService`. A restarted worker or duplicate processor could enter the lifecycle service twice for the same active outbox row until one saved `ProcessedAtUtc`.
- Marketplace projection outbox used `ClaimedUntilUtc`, but without an explicit row-version concurrency token the active-claim collapse still depended on a stale in-memory claim view under duplicate worker overlap.
- The lifecycle owner remains the correctness authority, but the outbox processor still needed deterministic active-overlap collapse to avoid replay amplification under worker restart pressure.

Runtime hardening added:

- `PaymentCancellationOutboxMessage` now carries `ClaimedUntilUtc` plus `TryClaim`, `MarkProcessed`, and `ClearClaim` semantics.
- `PaymentCancellationOutboxMessage` and `MarketplaceProjectionOutboxMessage` now also carry explicit `RowVersion`/`xmin` concurrency tokens so duplicate claimers cannot both save a claimed row under worker restart overlap.
- `PaymentCancellationOutboxProcessor` filters active claims, claims each row before lifecycle work, uses an order-scoped PostgreSQL advisory transaction lock when available, clears the lease on success/manual-review/failure, and emits low-cardinality overlap telemetry for both lease-visible overlap and row-version claim collisions.
- `MarketplaceProjectionOutboxProcessor` now treats active-claim and duplicate-completion overlap as first-class convergence telemetry instead of silent skips.
- `PaymentCancellationOutboxOptions` now exposes bounded `ProcessingLeaseSeconds` so operators can tune crash-recovery delay without changing payment authority.
- 2026-05-27 follow-up: `StorefrontFooterProjectionConsumer` now revalidates current SQL tenant lineage after replay fencing and before stale-event suppression or full footer Mongo replacement. If the tenant is inactive, deleted, or missing, the replay fails closed by deleting tenant-scoped footer visibility, recording tenant-lineage authority failure telemetry, completing the replay fence, and refusing to preserve public footer state from old events or existing Mongo documents.
- 2026-05-29 follow-up: `RedisInvalidationConsumer` now acquires `ProjectionEventFence` before homepage, CMS, footer, marketplace product/order, and marketplace review cache invalidation side effects. Duplicate or active replay collapses before Redis key or pattern invalidation, and marketplace-domain cache events preserve tenant scope in fence telemetry through the tenant-aware `ProjectionEventFence.TryAcquireAsync(...)` overload.
- 2026-05-29 follow-up: `MarketplaceProjectionOutboxProcessor` now records `OperationalConvergenceTelemetry` when duplicate completion is detected after dispatch during the processed-event unique-key race. Residual outbox cleanup stays replay-safe, and operators can distinguish healthy duplicate-completion collapse from a silent worker skip.

## Rebuild Overlap Certification

Validated guarantees:

- CommerceHub reindex and tenant-admin search repair redeclare SQL listing/product/category/media/stock/price/publication authority before index/delete mutation.
- Homepage and recommendation projection rebuilds fence duplicate replay before Mongo/Search/Redis side effects and scrub stale curated references from current SQL authority.
- Product read-model repair continues through `IProductSqlAuthorityProjectionWriter`; cache invalidation stays behind tenant-scoped `IProductCacheService`.
- Rebuild interruption or overlap is safe only because each run recomposes from SQL and converges by apply, delete, tombstone, no-op, retry, or fail-closed behavior.

No derivative rebuild path was promoted to authority in this pass.

## Queue Lag / Delayed Recovery Certification

Validated guarantees:

- Backlog drain is bounded by batch, max-batches-per-poll, retry delay, inter-batch delay, and claim lease controls on the inspected high-volume workers.
- Delayed replay is stale evidence only; current SQL product/listing/category/inventory/payment/accounting authority decides visibility or mutation.
- Dead-letter retry resets failed marketplace and stock messages into the normal ordered processor path rather than direct publish or processed-row suppression.
- Payment cancellation recovery now also has a processing lease, so queue lag plus worker restart no longer creates active duplicate lifecycle calls for the same row.

## Worker Restart / Crash Recovery Certification

Validated guarantees:

- Accounting, stock-sync, marketplace event, marketplace projection, and payment cancellation outboxes now all have inspected active-processing leases or equivalent completion fences.
- Marketplace projection and payment-cancellation claim collapse is now backed by row-versioned writes instead of lease state alone.
- Lease expiry is intentionally a recovery boundary: a crashed worker may leave a row claimed until expiry, then another worker can resume through the same SQL authority path.
- A shortened lease may increase duplicate attempts under slow dependencies, so operators must tune it from dependency latency and repeated failure rate rather than availability pressure alone.

## Cache Rebuild Overlap Certification

Validated guarantees:

- Product cache invalidation collapses duplicate product replay before Redis fanout and reloads current SQL slug/identity where needed.
- Homepage/CMS/footer/marketplace Redis invalidation now also collapses duplicate replay before cache key or pattern fanout. Cache invalidation remains derivative infrastructure; the fence bounds replay amplification and does not make Redis visibility authority.
- Admin product rebuild no longer performs raw Redis key enumeration; it delegates to the tenant-scoped product cache boundary.
- Homepage, CommerceHub search, recommendation, product search, affiliate, storefront footer, and marketplace read-model cache/search rebuilds remain derivative recomposition paths and must not serve or index from cached state as final authority.
- Storefront footer replay now treats active tenant lineage as a public-content authority boundary. A delayed footer update may trigger recomposition, but current tenant authority decides replacement versus fail-closed deletion.

## Replay Amplification Protections

Validated protections:

- Durable projection fences collapse completed and active duplicate EventIds.
- Outbox rows use claim leases, retry delay, terminal failure thresholds, and compensation paths where appropriate.
- Dead-letter retry resets into normal ordered processors.
- Search and cache recovery deletes/tombstones stale visibility rather than recursively rehydrating from derivative state.
- Payment cancellation now has active-worker overlap containment, closing the inspected lease gap in the recovery outbox family.
- Redis cache invalidation storms for reviewed homepage/CMS/footer/marketplace cache events are bounded by durable processed-event fences; duplicate delivery may repeat SQL fence checks, but it should not repeat Redis pattern scans after completion.

## Operational Chaos Certification

Scenarios validated through code inspection and regression coverage:

- duplicate projection replay,
- active worker overlap,
- delayed queue catch-up,
- dead-letter retry reset,
- reindex/rebuild overlap,
- stale marketplace visibility replay,
- stale review/moderation replay,
- cache invalidation replay,
- financial replay retry,
- payment cancellation worker restart overlap,
- dangerous primitive misuse.

Convergence remains SQL-first: replay and rebuild inputs trigger recomposition, but current SQL authority decides apply, delete, tombstone, no-op, retry, block, or fail-closed behavior.

## Recovery Auditability

- `ProjectionEventFence`, SQL authority overlays, dangerous primitive guards, tenant/admin/platform authority checks, inventory/accounting lineage authorities, stock-sync suppression, and drift diagnostics emit convergence telemetry.
- Payment cancellation active-overlap collapse now records `OperationalConvergenceTelemetry` under the outbox/order-payment boundary for both visible active claims and row-version claim races.
- Marketplace projection outbox overlap and duplicate-completion collapse now records `OperationalConvergenceTelemetry` under the outbox/projection boundary.
- Storefront footer tenant-lineage replay rejection now records both authority-failure and derivative-deletion convergence telemetry so operators can distinguish healthy fail-closed public-content cleanup from worker loss.
- Redis invalidation replay collapse now records projection-fence convergence decisions for duplicate completed delivery and active overlap, including tenant scope for marketplace events that carry it.
- Request-level super-admin audit and domain-specific financial/lifecycle audit remain separate from semantic convergence telemetry.

## Convergence Guarantees

- PostgreSQL authority remains the source of business truth.
- MongoDB, Redis, Elasticsearch, queue payloads, dashboards, repair snapshots, and dead letters are evidence only.
- Duplicate delivery may repeat reads but must not create duplicate durable business mutation or stale public visibility.
- Rebuild overlap is acceptable only when every run redeclares SQL authority.
- Fail-closed derivative deletion is a healthy outcome when current SQL rejects visibility.
- Payment, accounting, refund, payout, fulfillment, and inventory recovery must remain inside lifecycle/lineage owners.

## Remaining Chaos / Recovery Debt

- This pass does not introduce adaptive throttling, tenant-specific schedulers, fixed rebuild SLOs, a universal chaos framework, SIEM-grade audit, support impersonation, or enterprise IAM.
- CMS/SEO/notification/analytics/support-cache/feed/export/reporting/cache-warmer/global-script paths remain path-specific certification debt when expanded.
- Some inspected paths use documented local SQL authority checks instead of universal helper adoption; tests and docs must continue to state the actual boundary.
- Lease tuning remains operational: too-short leases can increase duplicate overlap under slow dependencies, while too-long leases delay crash pickup.

## Regression Freeze Invariants

- Payment cancellation outbox replay must retain row leases before calling `IPaymentLifecycleService`.
- Payment cancellation and marketplace projection outbox claims must retain row-versioned concurrency enforcement; do not regress to lease-only in-memory claim collapse.
- Payment cancellation row leases must clear on processed, manual-review, and retry/failure outcomes.
- Payment cancellation active overlap must skip lifecycle mutation and emit convergence diagnostics.
- Storefront footer replay must keep tenant-lineage revalidation before Mongo replacement, delete tenant-scoped footer visibility when current tenant authority is gone, and emit convergence diagnostics.
- `RedisInvalidationConsumer` must keep `ProjectionEventFence` around homepage, CMS, footer, marketplace product/order, and marketplace review cache invalidation. Duplicate cache replay must collapse before Redis `RemoveByPatternAsync` or key deletion fanout.
- Dead-letter retry must reset into normal ordered processors.
- Rebuild/reindex paths must recompose from SQL and fail closed on missing authority.
- Cache/search repair must not restore visibility from Mongo, Redis, Elasticsearch, payloads, or partial SQL rows.
- Dangerous primitives require SQL authority, tenant/platform scope, replay/freshness protection, telemetry/audit, docs, and tests in the same change.
