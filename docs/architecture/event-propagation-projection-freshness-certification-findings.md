# POS-ADMIN-CERTIFICATION-01 PASS 10.1 Event Propagation & Projection Freshness Certification

Date: 2026-05-29

## Scope

This pass certifies observable eventual consistency after PASS 9 established SQL-authoritative public visibility. It verifies how committed PostgreSQL mutations propagate into MassTransit/RabbitMQ, dedicated SQL outboxes, consumers, MongoDB read models, Redis cache invalidation, Elasticsearch indexes, CommerceHub projections, homepage/discovery projections, replay, and recovery.

This pass does not introduce a new event bus, queue, messaging contract, or strong consistency requirement.

## Event Flow Findings

Certified propagation chain:

```text
PostgreSQL write authority
  -> MassTransit EF bus outbox or specialized SQL outbox
  -> RabbitMQ/MassTransit publish or SQL outbox processor
  -> MassTransit consumer or marketplace projection pipeline
  -> MongoDB read model / Redis invalidation / Elasticsearch index
  -> read-time SQL authority overlay or recovery/rebuild path
```

Product, category, CMS, pricing, media, inventory, listings, homepage, CommerceHub, affiliate, search, and cache flows all remain derivative after SQL commit. Normal MassTransit integration events go through `IEventPublisher` and the EF outbox. High-sensitivity or high-volume flows also use dedicated outboxes: accounting, stock sync, marketplace event, marketplace projection, payment cancellation, and CommerceHub payment compensation.

No new hidden authority path was introduced in this pass. The reviewed dangerous primitives remain governed by source tests and path-specific SQL recomposition rules from PASS 6-PASS 9.

## Outbox Findings

MassTransit EF outbox rows are atomic with authoritative mutations when events are published through `IEventPublisher`. The publisher validates tenant scope, affected-tenant scope, aggregate ordering metadata, and transactional publish boundaries for authoritative events.

Dedicated outboxes use retry fields, retry backoff, claim leases, aggregate or warehouse-scoped ordering, and terminal failure handling. Marketplace/stock terminal projection failures route into compensation. Accounting critical financial events remain retryable instead of being silently cleaned up as dead letters.

Backlog visibility exists through readiness checks for MassTransit bus outbox, projection outboxes, accounting outbox, and payment-cancellation outbox.

## Consumer Reliability Findings

MassTransit consumers rely on retry definitions or exception rethrow where projection failures must retry. Hosted workers wrap each polling iteration, log failures, and continue looping unless shutdown is requested. Dedicated processors acquire SQL claims before side effects, so process restart resumes from SQL after lease expiry.

Silent stop risk is reduced by readiness checks and convergence telemetry rather than by assuming consumers are always live.

## Idempotency Findings

Duplicate delivery is expected. Tier 1 projection consumers use `ProjectionEventFence`, `ProjectionConsumerEventFence`, processed-event rows, event ids, freshness filters, or local inbox documents. Marketplace projection outbox uses processed-event completion records and cleans residual duplicate rows after completion races.

Redis invalidation, storefront footer, product/search, coupon, shipping, config, payment-method, affiliate, marketplace, and stock-sync paths have duplicate or freshness protection on reviewed boundaries.

## Ordering Findings

Global ordering is not guaranteed and is not required. Ordering is enforced per aggregate or per warehouse-scoped inventory stream where correctness depends on sequence:

- MassTransit publisher stamps authoritative aggregate versions.
- Marketplace event outbox blocks newer same-aggregate events behind earlier unresolved events.
- Stock-sync outbox orders by event version, stock timestamp, event timestamp, creation timestamp, and id within `{TenantId, VariantId, WarehouseId}`.
- Accounting outbox blocks newer financial truth messages behind earlier aggregate versions.
- Projection handlers reject stale or future/corrupted versions where versioned authority exists.

Out-of-order delivery should converge because handlers reload current SQL truth or suppress stale replay rather than trusting payload order alone.

## Projection Freshness Findings

Freshness markers exist on reviewed Mongo/search paths through event versions, event timestamps, processed-event fences, projection mutation versions, tombstone guards, and document-level timestamps such as `ProjectedAtUtc`, `LastEventVersion`, or path-specific last-event fields.

PASS 10.1 hardening adds convergence-meter emission from projection and MassTransit outbox readiness checks. Lag is now visible both through health-check data and the low-cardinality `PointOfSale.Operational.Convergence` meter.

## Lag Detection Findings

Lag is observable through:

- `projection_outbox` readiness for stock-sync, marketplace event, and marketplace projection outboxes.
- `mass_transit_bus_outbox_readiness` for pending EF bus outbox messages.
- `accounting_outbox_readiness`.
- `payment_cancellation_outbox_readiness`.
- CommerceHub search sync health and auto-healing metrics.
- stock-sync delivery lag histogram.
- affiliate event lag telemetry.
- operational convergence drift/lag telemetry.

Hardening performed in this pass records projection and bus-outbox lag as `OperationalConvergenceTelemetry.RecordDriftDiagnostic(...)` with bounded labels.

## Replay & Catch-Up Findings

Replay and catch-up are SQL-first. Rebuild/reindex/admin repair paths use SQL overlays, SQL truth rebuild guards, tenant-scoped recovery identity, stale-delete/tombstone behavior, and normal ordered processors. Dead-letter retry resets failed messages into the standard processor path instead of bypassing ordering.

Crash recovery depends on bounded leases: too-short leases can amplify duplicate attempts, while too-long leases delay pickup. That remains an operational tuning tradeoff, not a strong-consistency guarantee.

## Cross-System Propagation Findings

MongoDB, Redis, Elasticsearch, CommerceHub, homepage, discovery, affiliate, and search projections receive SQL-triggered updates through consumers, dedicated outboxes, or repair/rebuild services. Reviewed public reads from PASS 9 revalidate derivative candidates through SQL authority before returning visibility.

Redis remains invalidation/cache infrastructure only. Elasticsearch and MongoDB remain candidate/read surfaces only.

## Startup Recovery Findings

Startup and background recovery paths are derivative catch-up mechanisms. CommerceHub auto-healing uses tenant-scoped listing identity where tenant evidence is available. MongoDB index initialization, marketplace index initialization, Hangfire initialization, and search auto-healing do not become visibility authority.

## Chaos Certification Findings

Validated chaos scenarios:

- Consumer down: SQL outbox rows remain pending and readiness detects lag.
- Consumer restart: claim leases expire and processors resume through SQL.
- Duplicate event: processed-event fences collapse side effects.
- Out-of-order event: version/timestamp freshness rejects stale overwrite.
- Projection lag: health checks and convergence telemetry expose lag.
- Replay/rebuild overlap: SQL recomposition decides apply/delete/tombstone/no-op.

## Runtime Hardening Performed

`ProjectionOutboxHealthCheck` now emits bounded convergence telemetry for critical lag, warning lag, healthy tolerance, and check failure.

`MassTransitBusOutboxReadinessHealthCheck` now emits the same bounded convergence telemetry for EF bus outbox lag and check failure.

New telemetry reasons were added: `ProjectionLagDetected`, `ProjectionLagWithinTolerance`, and `OutboxReadinessCheckFailed`.

## Monitoring Improvements

Projection lag is no longer health-check-only evidence on the hardened surfaces. It is also exported through the reviewed operational convergence meter with bounded `surface`, `authority`, `decision`, `reason`, and `tenant_bucket` dimensions.

## Governance Updates

PASS 10.1 regression tests freeze:

- projection and MassTransit outbox readiness registration,
- lag/failure convergence telemetry emission,
- MassTransit sequence-based pending-row detection,
- dedicated outbox lease/order/failure controls,
- event-flow documentation and remaining debt.

## Honest Remaining Debt

This pass does not add a universal chaos framework, adaptive lag SLOs, a tenant-specific scheduler, a new monitoring vendor, or repository-wide conversion of all local freshness checks to one helper.

Some analytics, notification, feed/export, CDN purge, cache-warmer, support, reporting, and global-script paths remain path-specific certification debt before they can mutate public visibility or be claimed as Tier 1 convergence infrastructure.

Projection count drift checks are still coarse for products; correctness for public visibility comes from SQL revalidation and stale-delete repair, not count parity alone.
