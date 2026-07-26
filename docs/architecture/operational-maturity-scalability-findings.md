# Operational Maturity Scalability Findings

Date: 2026-05-24

## Scope

This review focused on platform stabilization, not new domain behavior. The inspected pressure points were replay throughput, durable outbox draining, projection rebuild convergence, cache invalidation fanout, telemetry usefulness, tenant isolation under operational load, and repair ergonomics.

## Findings

- SQL-first authority, replay collapse, tenant lineage, actor authority, dangerous primitive governance, and convergence telemetry held on the inspected shared boundaries.
- Product cache invalidation already collapses duplicate active/completed product replay before Redis fanout and resolves current SQL product slug before detail invalidation.
- Projection health checks already expose lag and repeated failures across stock, marketplace event, and marketplace projection outboxes.
- Marketplace event/projection outbox workers and stock sync workers had an operational scalability gap: worker cadence and batch draining were partially hard-coded even though options existed for poll interval and claim lease. This limited safe tuning during backlog recovery and large-tenant rebuild/replay pressure.
- PERFORMANCE-CON-01 follow-up found the payment-cancellation recovery outbox still had hard-coded batch size, retry threshold, retry backoff cap, and worker cadence. It also found that stock-sync options existed but were not bound from API startup configuration, which made production tuning less direct than the governance implied.

## Hardening Added

- Marketplace event outbox, marketplace projection outbox, and stock sync outbox workers now use bounded configurable batch sizes, max batches per poll, inter-batch delay, poll interval, and claim lease settings.
- Payment cancellation outbox recovery now uses `PaymentCancellationOutboxOptions` for batch size, max batches per poll, inter-batch delay, poll interval, idle interval, error delay, retry threshold, and retry backoff cap.
- API startup now binds both payment-cancellation and stock-sync outbox options from configuration.
- PASS 8 now also validates `CommerceHubReindexOptions` at API startup so invalid parallel batch size, search-status batch size, or inter-batch delay values fail closed before recovery pressure reaches production.
- Claim leases now use configured options rather than fixed processor constants.
- Worker loops can drain several bounded batches per poll while still yielding between batches to protect PostgreSQL, RabbitMQ, MongoDB, Redis, and Elasticsearch from recovery bursts.
- Runtime tests validate marketplace projection batch bounding and duplicate-completion cleanup.
- Runtime tests validate payment-cancellation batch bounding and configured retry/backoff behavior.
- Source governance tests validate that high-volume workers and processors continue to use operational throughput options.

## Operational Limits

- These controls are throughput governors, not synthetic benchmark claims. Production batch sizes must be tuned from live backlog age, dependency latency, retry rate, and tenant hotspot behavior.
- Duplicate and stale replay remains expected. The target guarantee is deterministic convergence: apply current SQL truth, collapse duplicate evidence, retry recoverable missing authority, or fail closed.
- Cache invalidation is still best-effort infrastructure. High-volume invalidation paths must continue to collapse duplicate replay before Redis pattern fanout.
- Projection rebuilds may be interrupted and retried. Rebuild correctness depends on redeclaring SQL authority and recomposing current state, not on finishing a single uninterrupted run.
- Payment-cancellation throughput controls are pacing controls around the existing payment lifecycle service. They do not allow direct payment status mutation, skip lifecycle recomposition, or convert payment cancellation into derivative authority.

## Production Readiness Assumptions

- PostgreSQL remains the primary scaling constraint and authority anchor.
- RabbitMQ/MassTransit/outbox delivery remains at-least-once.
- MongoDB, Redis, and Elasticsearch are derivative and may lag within operational readiness thresholds.
- Operators have access to readiness health checks, convergence telemetry, outbox lag counts, repeated failure counts, and terminal compensation paths.
- Payment-cancellation backlog is now part of that reviewed readiness surface rather than log-only recovery evidence, and reviewed accounting outbox telemetry is exported through a bounded meter instead of legacy raw-tenant singleton counters.
- Reviewed operational dashboard metrics are exported through the OpenTelemetry pipeline with bounded labels. Tenant-oriented reviewed metrics use `tenant_bucket` rather than raw tenant identifiers, and reviewed order-correlated reliability diagnostics expose presence flags instead of raw order ids.
- Reviewed Redis cache telemetry is exported as infrastructure diagnostics with bounded cache and pattern families, not raw cache keys or raw invalidation patterns.
- Global/all-tenant repair remains platform runbook scope; tenant-admin repair remains tenant-scoped and SQL-recomposed.
- Bounded recovery should be tuned by dependency latency and repeated-failure rate. Increasing batch size while lifecycle failures grow is a retry-amplification risk, not a throughput win.

## Remaining Debt

- No repo-wide projection framework was introduced. Specialized CMS, SEO, notification, analytics, support-cache, feed/export, report rebuild, cache warmer, and global runbook paths still require path-specific replay/rebuild validation when expanded.
- Marketplace and stock workers are now tunable, but adaptive throttling based on live dependency latency or tenant-specific backlog is intentionally left out until production telemetry proves it is needed.
- POS-ADMIN-CERTIFICATION-01 PASS 7 added payment-cancellation processing leases after worker-restart certification found the outbox still allowed active duplicate lifecycle entry before `ProcessedAtUtc` was saved. The lifecycle service remains payment authority, but the recovery outbox now collapses active worker overlap through `ClaimedUntilUtc`, an order-scoped advisory lock where available, and bounded `ProcessingLeaseSeconds`.
- Telemetry remains low-cardinality semantic diagnostics, not a SIEM, durable audit event store, or full distributed tracing program.
- PASS 8 improves OpenTelemetry meter registration and cardinality on reviewed operational meters; it still does not claim repo-wide telemetry normalization for every older metric surface.
- Large-tenant rebuild SLOs are not asserted by synthetic benchmarks in this pass; they should be derived from production-like staging runs with real dependency latencies.

## Operational Runbook Follow-Up

OPERATIONS-RUNBOOKS-01 adds the human-operation layer for these controls:

- `docs/operations/operational-recovery-runbooks.md` documents how operators should handle replay storms, rebuild interruption, dead-letter recovery, queue backlog pacing, cache/projection repair, tenant isolation incidents, payment/accounting/fulfillment incidents, drift diagnostics, telemetry interpretation, and escalation.
- `docs/architecture/dangerous-operational-primitives.md` documents low-level primitive governance so throughput pressure does not justify direct SQL, manual Redis/Mongo/Elasticsearch repair, raw bus publishing, global repair, tenant override, or manual financial repair outside reviewed boundaries.
- `OperationalRecoveryRunbookArchitectureTests` guards runbook coverage for the required recovery areas and unsafe shortcut prohibitions.

The scalability posture remains unchanged: operators can tune throughput to recover safely, but tuning must not bypass SQL authority, replay collapse, tenant scope, lifecycle/reconciliation ownership, or fail-closed behavior.

## TENANT-CERTIFICATION-01 Follow-Up

Tenant isolation certification did not change throughput authority or add fixed SLO claims. It hardened a narrow storefront CMS derivative gap and added regression-freeze tests around tenant resolution, tenant-admin operational scope, derivative tenant identity, replay fences, dangerous primitive guards, and certification documentation.

Operational confidence remains deterministic convergence under replay/rebuild pressure. Large-tenant rebuild and recovery tuning still depends on live backlog age, dependency latency, repeated failure rates, tenant hotspot behavior, and convergence telemetry.

## PASS 8 Operational Survivability Follow-Up

- OpenTelemetry meter registration is now part of the reviewed operational survivability boundary for checkout lock, affiliate, search, reservation, abandoned-cart, storefront publish, marketplace attachment, analytics reliability, and convergence telemetry.
- `PointOfSale.Accounting` is now part of that same reviewed export boundary. Accounting posting/failure/idempotency/dead-letter/cleanup telemetry uses `tenant_bucket` plus normalized operation/gateway/error labels, and the singleton fallback counters are bounded the same way.
- Reviewed exported telemetry now uses bounded dimensions such as `tenant_bucket`, `tenant_present`, `order_present`, and normalized reason/action/source/search labels so long-lived enterprise multi-tenant scraping does not silently degrade into cardinality exhaustion.
- Reviewed `CommerceReliabilityTelemetry` now also normalizes the remaining provider, endpoint, surface, carrier, validation-method, projection, storage-provider, stock-sync, and isolation-level labels on the exported boundary instead of relying on caller discipline.
- Redis cache performance telemetry is now included in the reviewed OpenTelemetry export boundary. Cache hit/miss/corruption/latency and pattern-invalidation pressure are diagnosable through bounded `cache_type` and `pattern_family` labels; raw Redis keys and tenant/entity-specific invalidation patterns remain logs/debug evidence only.
- Payment cancellation replay-safe recovery is now also represented on the reviewed readiness surface through `payment_cancellation_outbox_readiness`, exposing backlog, repeated failure, terminal failure, active claim, and lease diagnostics for deployment/restart and incident pressure.
- Deployment and DR assumptions are now frozen in the operational runbooks: rolling restart overlap is expected and must converge through the existing lease/fence model, while backup/restore remains SQL-first and does not claim point-in-time cross-store restore orchestration.
- This remains pragmatic operability hardening, not an enterprise observability platform rollout. Dashboard definition ownership, adaptive throttling, and path-by-path telemetry review outside the inspected surfaces remain explicit debt.
