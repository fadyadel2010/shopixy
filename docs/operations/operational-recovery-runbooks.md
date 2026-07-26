# Operational Recovery Runbooks

Date: 2026-05-26

## Purpose

These runbooks define how operators recover a replay-safe, SQL-first, multi-tenant commerce platform during production incidents. They are intentionally practical: the goal is safe human operation, not process ceremony.

The governing rule is simple:

- PostgreSQL and the named authority services decide business truth.
- MongoDB, Redis, Elasticsearch, RabbitMQ, MassTransit payloads, dashboards, and repair inputs are evidence only.
- Recovery may replay, rebuild, delete stale derivative state, rehydrate from SQL, retry, throttle, or escalate.
- Recovery must not invent business truth from projections, caches, queue payloads, or manual patches.

## Core Operating Model

Replay-safe means duplicate or stale delivery converges deterministically. A replay may apply current SQL truth, collapse to no-op, delete/tombstone stale derivative visibility, retry because SQL authority is temporarily unavailable, or fail closed when current authority rejects the evidence.

Fail-closed means the platform prefers hiding, deleting, tombstoning, blocking, or marking reconciliation-required over showing stale public visibility or moving money from uncertain evidence. Fail-closed deletion of Mongo, Redis, or Elasticsearch derivative state is usually a healthy recovery outcome when SQL no longer proves visibility.

Rebuild-safe means rebuilds recompose from current SQL. Rebuild seeds, old projection documents, queue payloads, and cached values are not rebuild authority. Rebuild interruption and rebuild overlap are expected; the next run must redeclare SQL authority and converge again.

Operator escalation rule:

- Tenant-scoped operational work stays tenant-scoped and uses trusted tenant context plus current SQL actor authority.
- All-tenant or global repair is platform scope only and requires explicit platform approval.
- Payment, accounting, refund, payout, and fulfillment ambiguity escalates to the owning lifecycle/reconciliation path. Operators must not manually repair financial truth.

## Platform Authority And Global Operations

Use this runbook when a global/all-tenant operation, marketplace-wide moderation action, platform recovery job, global cache/search operation, or emergency operational primitive is required.

Procedure:

1. Confirm the request is platform scope, not a tenant incident that should stay tenant-scoped.
2. Confirm the caller is a current SQL super-admin. Super-admin JWTs are evidence only; `SuperAdminPlatformAuthority` must validate current `SuperAdmins`, current token version, and current governance status.
3. Name the SQL authority owner before mutation: tenant rows, marketplace CMS rows, listing/publication rows, lifecycle/reconciliation services, or SQL-first rebuild overlays.
4. Prefer tenant-scoped or entity-scoped repair even under platform authority when the incident scope is narrow.
5. For a true global/all-tenant operation, use bounded batch size, cancellation-safe workers, replay collapse, and SQL-first rebuild declarations.
6. Record operational intent and result through super-admin audit records, structured platform logs, convergence telemetry, or a documented equivalent.
7. Treat fail-closed deletes, tombstones, no-ops, and rejected stale super-admin tokens as healthy authority outcomes until SQL evidence says otherwise.

Forbidden platform shortcuts:

- Using a stale super-admin access token after logout, password change, token rotation, account deletion, or governance restriction.
- Running a global/all-tenant operation from a tenant-admin route.
- Using tenant-admin role authority to mutate a platform-wide taxonomy or other shared global governance row. Global affiliate-category mutation is platform scope even when tenant-admin read access exists nearby.
- Recreating global search indexes to fix a single tenant.
- Copying Mongo, Redis, Elasticsearch, dashboard, export, or queue payload state into business truth.
- Suppressing dead letters, readiness gates, or reconciliation-required states to make a global dashboard look healthy.

Escalate when:

- Platform authority rejection telemetry appears for a legitimate active super-admin.
- A global operation cannot name its SQL authority owner.
- A global repair would touch money, tenant isolation, customer PII, fulfillment, or ledger state outside the owning lifecycle/reconciliation path.
- A dangerous primitive is required but lacks source/runtime governance or documented runbook scope.

## Replay Recovery

Use replay recovery when queue delivery, outbox dispatch, dead-letter replay, duplicate callback delivery, or projection lag needs to converge from already-committed SQL authority.

Procedure:

1. Identify the governing authority boundary: product/variant lifecycle, inventory lineage, order fulfillment lineage, order payment lineage, accounting ledger authority, tenant lineage, admin actor authority, or a documented equivalent SQL recomposition path.
2. Check operational convergence telemetry for replay phase decisions: applied, collapsed, suppressed, no-op, retry, failed-closed, deleted, tombstoned, or rehydrated.
3. Confirm whether the replay is duplicate completed delivery, active overlapping delivery, stale version/timestamp evidence, missing recoverable authority, or real drift.
4. If duplicate replay collapses with stable telemetry and backlog age improves, continue monitoring. No manual repair is needed.
5. If replay repeatedly retries because SQL authority is missing or inconsistent, route to the owning reconciliation runbook rather than forcing projection mutation.
6. If replay fails closed by deleting derivative visibility, verify SQL current state before treating it as an outage.
7. For dead-letter replay, replay in bounded batches and observe dependency latency, repeated failure count, and tenant hotspot behavior between batches.

Expected healthy outcomes:

- Duplicate EventId delivery collapses before side effects.
- Active overlapping replay suppresses the second worker.
- Stale event payloads no-op, tombstone, or delete stale derivative visibility.
- Current SQL truth is applied once even when many messages are replayed.
- Marketplace projection duplicate-completion race cleans the residual outbox row and records collapsed convergence telemetry after `ProcessedEvents` already proves completion.
- Replay telemetry is visible through `PointOfSale.Operational.Convergence`.
- Payment-cancellation backlog health is visible through `payment_cancellation_outbox_readiness`.
- Accounting outbox throughput and failure pressure are visible through `PointOfSale.Accounting`.

Escalate when:

- Replay collapse metrics are absent for a Tier 1 path that should use a fence or equivalent guard.
- Replays repeatedly fail for the same tenant/entity after SQL authority is confirmed healthy.
- Replay attempts create growing dependency latency or retry storms.
- Payment/accounting replay remains retryable because refund, settlement, or journal lineage is inconsistent.
- Tenant lineage rejects evidence that an operator believes should still be visible.

Replay anti-patterns:

- Requeuing all dead letters without first identifying the failing authority boundary.
- Treating no-op replay as data loss.
- Manually editing Mongo, Redis, or Elasticsearch to "make the UI match."
- Replaying stale provider callbacks to force payment success.
- Widening tenant scope because a tenant-scoped replay did not immediately fix visibility.
- Disabling fences, freshness filters, or `DerivativeProjectionMutationGuard` to clear backlog.

Replay storm containment:

- Reduce worker batch size and max batches per poll before dependency saturation.
- Increase inter-batch delay when PostgreSQL, RabbitMQ, MongoDB, Redis, or Elasticsearch latency rises.
- Pause non-critical rebuild/reindex jobs if they compete with Tier 1 replay.
- Keep aggregate ordering and retry delay intact; never skip earlier failed aggregate work to make counts drop.
- Split tenant hotspots into tenant-scoped recovery windows when the tooling supports it.

## Rebuild And Reindex Recovery

Use rebuild/reindex recovery when derivative read models, search indexes, homepage/cache state, or projection documents need regeneration from SQL truth.

Procedure:

1. State the rebuild scope: tenant, product/listing/category/order, projection type, cache family, or search index segment.
2. Confirm the rebuild path declares SQL authority through `SqlTruthRebuildGuard` or a documented equivalent.
3. Prefer tenant-scoped rebuilds. Use all-tenant rebuild only as platform scope.
4. Run with bounded batch size and cancellation-safe workers.
5. Treat deleted/tombstoned derivative documents as expected when SQL no longer proves visibility.
6. If interrupted, restart the rebuild from the documented entrypoint. Do not resume from stale Mongo/Redis/Elasticsearch snapshots as authority.
7. Verify convergence with drift diagnostics, sample SQL-to-derivative comparisons, and replay/rebuild telemetry.

Expected healthy outcomes:

- Rebuild no-ops are common when projections are already current.
- Stale projections are deleted or tombstoned.
- Overlapping rebuilds redeclare SQL authority and converge without changing business truth.
- Search documents disappear when product/listing/category/tenant lineage is not live.
- Rebuild telemetry records SQL authority declarations and fail-closed suppression.

Sequencing guidance:

- Recover SQL authority and lifecycle ambiguity before rebuilding derivatives.
- Rebuild tenant lineage and high-value customer visibility before analytics/reporting surfaces.
- Reindex search after product/category/media/publication/variant authority has converged.
- Rehydrate caches after Mongo/search projections are healthy unless the cache path revalidates SQL before serve.

Rebuild anti-patterns:

- Full-replacing Mongo documents from old repair snapshots.
- Partial SQL-row search rebuild that skips the reviewed SQL overlay and indexes a "baseline" document from minimal field copies.
- One-way Mongo sync that upserts current category or shipping-method rows but preserves tenant-scoped stale documents that no longer exist in SQL authority.
- Direct Redis key mutation in a product read-model repair controller instead of tenant-scoped invalidation through the approved product cache boundary.
- Recreating global Elasticsearch indexes from tenant-admin tooling.
- Using stale event payloads to rebuild pricing, stock, payment, shipment, or tenant visibility.
- Treating fail-closed deletion as an outage before checking SQL.
- Running all-tenant rebuilds to fix one tenant hotspot.
- Disabling cancellation or leases to keep one long rebuild alive.

### Replay/Rebuild Overlap Semantics

Overlap is expected during incident recovery. A duplicate replay, a restarted rebuild, or a tenant-scoped reindex running while global platform recovery is active must converge by redeclaring SQL authority and then applying, deleting, tombstoning, no-oping, retrying, or failing closed.

Rules:

- Tenant-admin CommerceHub search reindex is tenant-scoped. Use platform/runbook scope for global CommerceHub reindex.
- Tenant-admin CommerceHub search alias promotion/finalization and migration catch-up are platform/runbook scope only. A tenant-admin request should fail closed, not partially perform global search recovery.
- CommerceHub search recovery requires current SQL `CommerceHubListings` plus product, variant, category, tenant, stock, price, and media authority.
- Tenant-admin storefront product search repair must also redeclare current SQL search authority through `ProductSearchSqlAuthorityOverlay`; it must not restore Elasticsearch visibility from partial SQL rows or stale repair snapshots.
- Tenant-admin category search sync must also redeclare current SQL rebuild authority and delete tenant-scoped stale Elasticsearch category documents that are missing from the current SQL category authority set. One-way category search sync is not a safe recovery shortcut.
- Tenant-admin product read-model repair may invalidate derivative product caches only through `IProductCacheService`; it must not scan or delete Redis keys directly from `AdminSyncController`.
- Storefront footer replay/rebuild must revalidate current SQL tenant lineage before Mongo replacement. If the tenant is inactive, deleted, or missing, the correct recovery outcome is tenant-scoped footer visibility deletion plus convergence telemetry, not preserving the existing Mongo footer document.
- Homepage/CMS/footer/marketplace cache invalidation replay must retain the Redis invalidation `ProjectionEventFence`. Duplicate or active cache-invalidation replay should collapse before Redis key deletion or pattern scans; do not remove the fence to make cache refresh appear faster during incident pressure.
- Tenant-admin service-marketplace search repair may reindex only current active SQL `Service` rows for the authenticated tenant and delete only stale tenant-owned service documents. It must not delete/recreate the global `marketplace_services` index; missing global index state requires super-admin/platform recovery.
- Legacy service-marketplace replay and global drift reconciliation must also recompose current SQL service-search authority. A service document may be restored only when SQL proves the service is active and not deleted, the owning tenant is active and not deleted, and the provider profile is active, not deleted, and owned by the same tenant. Otherwise recovery should delete or leave absent the stale `marketplace_services` document.
- Rebuild interruption is not corruption. Restart from the documented SQL-first entrypoint; do not resume from Mongo, Redis, Elasticsearch, old queue payloads, or exported snapshots as authority.
- Dead-letter retry must reset failed rows into the normal claim/order/retry path. Do not mark failed rows processed or publish payloads directly to bypass ordering.
- Tune batch size and inter-batch delay to contain replay amplification; do not increase throughput when repeated failures or dependency latency are growing.
- Treat duplicate deletes or duplicate index attempts as safe derivative convergence only when SQL overlay and tenant-scoped search identity remain in force.

Escalate when:

- A tenant-admin route can trigger all-tenant rebuild, global index deletion, or global cache purge.
- A rebuild path cannot name SQL listing/product/category/tenant authority.
- Replay amplification grows side effects without `ProjectionEventFence`, processed-event collapse, claim leases, or equivalent diagnostics.
- Operators cannot distinguish fail-closed suppression from worker crash because telemetry or logs are absent.

## Queue Backlog And Throughput Recovery

Use backlog recovery when outbox, MassTransit, RabbitMQ, projection, stock sync, marketplace event, marketplace projection, or payment-cancellation lag threatens freshness or readiness.

Procedure:

1. Identify the backlog source: marketplace event outbox, marketplace projection outbox, stock sync outbox, payment-cancellation outbox, MassTransit queue, Hangfire job, projection retry table, or dead-letter queue.
2. Check backlog age, queued count, repeated failure count, oldest failure reason, dependency latency, and tenant distribution.
3. Classify the backlog as capacity pressure, poison message, tenant hotspot, external dependency outage, or authority rejection.
4. Tune bounded controls first: batch size, max batches per poll, inter-batch delay, poll interval, retry threshold, and claim lease.
5. Keep retry delay, leases, aggregate ordering blockers, idempotency, and compensation behavior intact.
6. Replay dead letters in small bounded batches after the root authority failure is understood.
7. If one tenant dominates, prefer tenant-scoped remediation, throttling, or temporary non-critical workload deferral over global fanout.

Throughput tradeoffs:

- Larger batches reduce backlog count but increase SQL and derivative-store pressure.
- More batches per poll improve drain rate but can starve request traffic or lower-priority workers.
- Longer leases reduce duplicate active processing but delay pickup after worker death.
- Shorter leases improve recovery after crashes but can increase duplicate overlap under slow dependencies.
- Inter-batch delay protects dependencies during replay storms.
- Payment-cancellation recovery exposes batch, max-batches-per-poll, cadence, retry-threshold, and retry-backoff controls, but still routes every mutation through `IPaymentLifecycleService`. Larger batches are unsafe when lifecycle failures or provider ambiguity are increasing.
- `payment_cancellation_outbox_readiness` is the reviewed health surface for this worker. Use it to inspect pending count, repeated failure count, terminal failure count, critical lag, active claims, and current `ProcessingLeaseSeconds` before widening throughput or restarting more nodes.

Retry storm handling:

- Stop widening throughput when failure count grows faster than successful completion.
- Look for one poison authority condition before scaling workers.
- Keep failed earlier aggregate events in place; do not bypass head-of-line ordering for correctness-critical streams.
- Escalate payment/accounting/fulfillment poison messages to lifecycle owners instead of manually marking processed.
- Payment-cancellation outbox replay must retain row leases before entering `IPaymentLifecycleService`; active duplicate workers should collapse and wait for lease expiry instead of invoking the lifecycle boundary twice for the same row.
- Payment-cancellation and marketplace-projection outbox claims rely on row-versioned writes as well as leases/completion fences. If overlap telemetry rises without backlog drain, inspect claim contention before widening throughput.
- Tune `PaymentCancellationOutbox:ProcessingLeaseSeconds` from payment-provider/lifecycle latency. A shorter lease speeds crash pickup but can increase duplicate overlap if dependencies are slow.
- PostgreSQL advisory locking in `PaymentCancellationOutboxProcessor` is a reviewed claim-serialization primitive only. It must stay inside the claim transaction, use the stable tenant/order key, and be followed by normal `IPaymentLifecycleService` processing; do not use direct SQL to mark payment-cancellation rows processed or to mutate order/payment truth.

## Deployment And Restart Recovery

Use this runbook when rolling out a new API build, restarting background workers, recovering from a process crash, or draining a node under backlog pressure.

Procedure:

1. Confirm reviewed startup validation still passes before sending traffic: configuration-bound leases, poll cadence, and `CommerceHubReindexOptions` must fail closed during startup rather than surfacing later under incident pressure.
2. Treat restart overlap as normal. Rolling restart is safe only because restarted workers re-enter the same SQL-first leases, fences, processed-event checks, and lifecycle boundaries as the prior node.
3. Keep readiness fail-closed until PostgreSQL, RabbitMQ, Redis, MongoDB, Elasticsearch, and storage dependencies are healthy enough for the node's reviewed runtime responsibilities.
4. Prefer one-node-at-a-time restart when backlog age, repeated failures, or dependency latency is already elevated.
5. Do not assume a clean shutdown or zero overlap. A restarted payment-cancellation, marketplace-projection, stock-sync, or projection consumer may collide with previously claimed work and must converge through the existing lease/fence path instead of special-case deployment logic.
6. After restart, sample convergence telemetry, backlog age, duplicate-collapse counts, and failed-closed decisions before widening throughput or restarting more nodes.

Expected healthy outcomes:

- Duplicate or overlapping restart pickup collapses through `ClaimedUntilUtc`, row-version checks, `ProjectionEventFence`, processed-event uniqueness, or equivalent reviewed guards.
- A node with invalid reviewed recovery pacing or missing reviewed meter registration fails during startup instead of silently weakening production diagnosis.
- Short-lived backlog growth during rolling restart is acceptable when aggregate ordering, leases, and replay collapse remain intact.

Forbidden shortcuts:

- Disabling leases, replay fences, or readiness checks to make a deployment appear faster.
- Manually clearing claimed rows, processed-event records, or queue state because a restarted worker looks idle.
- Replacing SQL-first recovery with deployment-only scripts that mutate MongoDB, Redis, Elasticsearch, or queue state directly.

## Backup, Restore, And DR Recovery

Use this runbook for infrastructure loss, PostgreSQL restore, derivative-store rebuild after restore, or disaster-recovery rehearsal.

Procedure:

1. Restore PostgreSQL first and treat it as the only business-truth restore boundary.
2. Bring the API up with reviewed startup validation and readiness checks still enabled. A restored node must not bypass fail-closed guards just because the environment is recovering.
3. Treat MongoDB, Redis, Elasticsearch, RabbitMQ, and dashboard snapshots as derivative or transport evidence only. They may be rebuilt, discarded, or resynchronized after PostgreSQL restore; they are not business-truth restore authority.
4. Rebuild public/product/search/cache/read-model visibility from the documented SQL-first repair and reindex entrypoints. If derivative state is stale or uncertain, prefer delete/tombstone/reindex over preserving it.
5. Re-run lifecycle/reconciliation owners for payment, refund, payout, settlement, fulfillment, and shipment ambiguity. Do not promote old queue payloads or derivative snapshots into restored authority.
6. Validate recovery with SQL-to-derivative sampling, readiness health checks, convergence telemetry, and bounded backlog drain rather than only UI appearance.

Expected healthy outcomes:

- Empty or partially rebuilt MongoDB, Redis, or Elasticsearch state is acceptable immediately after restore when SQL authority is intact and the reviewed rebuild paths are running.
- A restored queue may replay duplicate work; safe recovery depends on the same fences, leases, idempotency, and SQL recomposition rules used during normal operations.
- Fail-closed deletion after restore is healthy when current SQL no longer proves tenant, listing, media, stock, payment, or settlement visibility.

Explicit DR limitations:

- No repo-authoritative orchestration currently guarantees point-in-time cross-store restore consistency across PostgreSQL, MongoDB, Redis, Elasticsearch, RabbitMQ, and object storage.
- No certified per-tenant restore workflow exists; reviewed recovery remains platform or tenant scoped only after PostgreSQL authority is restored.
- Derivative snapshots and queue state can accelerate recovery, but they cannot replace SQL-first rebuild and reconciliation.

## Cache And Projection Recovery

Use cache/projection recovery when Redis, MongoDB, Elasticsearch, homepage/discovery, storefront, affiliate, or CommerceHub read surfaces drift from SQL truth.

Procedure:

1. Decide whether the target is cache, projection, search index, or customer-critical read repair.
2. Confirm the derivative path recomposes current SQL authority or revalidates before serve.
3. Prefer invalidation, tombstone/delete, and SQL-backed rehydrate over manual mutation.
4. For Redis, invalidate tenant-scoped keys or known patterns through approved cache boundaries. Avoid raw key edits as business repair.
5. For MongoDB, rebuild through the owning projection/repository/writer. Do not hand-edit full documents for Tier 1 visibility.
6. For Elasticsearch, reindex through SQL overlays and search index services. Do not index raw documents from stale Mongo.
7. Validate with SQL sample checks and drift diagnostics rather than only UI appearance.

Expected healthy outcomes:

- Redis cache miss hydrates from SQL-revalidated data or a SQL-recomposed projection.
- Duplicate homepage, CMS, footer, marketplace order/product, or review cache invalidation replay collapses through a processed-event fence before Redis fanout.
- Mongo rebuild deletes stale derivative documents when authority is gone.
- Elasticsearch reindex drops documents for unpublished, inactive, deleted, offline, invalid-lineage, or inactive-tenant entities.
- Storefront footer projection deletion is expected when current tenant lineage rejects a delayed footer replay.
- Fail-closed empty visibility is healthy when authority is uncertain.

Forbidden shortcuts:

- Manual Redis key mutation to restore stock, price, tenant config, payment, or order visibility.
- Direct Mongo `ReplaceOne` outside reviewed projection/writer boundaries.
- Raw Elasticsearch visibility writes from Mongo, CSV, dashboard, or payload snapshots.
- Copying a projection document from one tenant to another.
- Editing derivative state to bypass readiness gates.

## Tenant Isolation Incidents

Use this runbook when there is suspected tenant bleed, cross-tenant visibility, stale tenant replay, tenant-scoped cache leakage, or unsafe tenant override usage.

Immediate containment:

1. Freeze the affected tenant-scoped repair/rebuild action if still running.
2. Capture the trusted tenant context, route/host, actor id, operation id, event id, and tenant bucket telemetry. Do not put raw tenant/customer data into metric labels.
3. Disable only the narrow operational entrypoint if needed. Avoid global tenant repair fanout.
4. Verify current SQL tenant lineage for the affected rows.

Investigation:

- Check whether the path uses `TenantLineageAuthority` or an equivalent local SQL-lineage check.
- Confirm derivative ids or filters include tenant scope through `TenantScopedDerivativeKey` or equivalent tenant predicates.
- Check admin actor authority when the incident was request-driven.
- Inspect replay telemetry for fail-closed tenant rejection, duplicate replay collapse, and deleted/scrubbed derivative visibility.
- Compare SQL tenant ownership to Mongo document filters, Redis key scope, and Elasticsearch document ids.

Recovery:

- Rebuild or scrub the affected tenant scope from SQL authority.
- Delete stale derivative visibility where SQL lineage is missing, inactive, moved, or cross-tenant.
- Reindex with tenant-scoped entrypoints.
- Use all-tenant scrub only under platform authority when impact is proven broader than one tenant.

Forbidden shortcuts:

- Trusting `X-Tenant-Id`, query tenant ids, frontend tenant selectors, or repair payload tenant ids as authority.
- Copying documents between tenants.
- Running all-tenant repair from a tenant-admin endpoint.
- Recreating global search indexes to fix one tenant.
- Preserving visibility because a stale projection "looks right."

Escalate immediately for payment, accounting, customer PII, or admin actor leakage across tenants.

TENANT-CERTIFICATION-01 regression-freeze checks:

- Confirm whether the path uses `TenantLineageAuthority` or an explicitly documented equivalent SQL-lineage check.
- Confirm whether tenant-scoped derivative ids or filters include tenant scope through `TenantScopedDerivativeKey`, tenant+entity composite ids, or a tenant predicate.
- Confirm replay collapse happens before dangerous Mongo, Redis, or Elasticsearch side effects on Tier 1 public visibility paths.
- Confirm dangerous full replacements declare SQL authority and fail-closed behavior through `DerivativeProjectionMutationGuard` or a reviewed domain writer.
- Confirm tenant-owned category admin paths still bind trusted tenant scope explicitly in SQL for list/detail/create/update/delete/seed behavior; foreign category ids must fail closed instead of depending on `FindAsync(...)` or ambient filters.
- Confirm reviewed tenant-owned WhatsApp account/template update still binds `Id` and `TenantId` together on the shared service boundary, not only in the caller.
- Confirm findings are recorded in `docs/architecture/tenant-isolation-certification-findings.md` when the incident changes tenant risk posture.

SUPERADMIN-CON-01 Phase 2 marketplace-governance checks:

- Confirm marketplace product visibility has current SQL `CommerceHubListing` authority before rebuilding or reindexing public marketplace products.
- Confirm curated homepage/recommendation ids still resolve to current SQL-visible listings/categories, including active CommerceHub category parent lineage; scrub stale references instead of preserving them.
- Confirm current vendor suppression is reapplied on public marketplace product/homepage reads and curated-reference validation. Governance-hidden vendors and vendors suspended from new orders must scrub or reject visibility even when listing/category/product SQL rows still exist.
- Confirm CommerceHub Elasticsearch documents use tenant-scoped ids for new writes and that replay/reindex deletes legacy listing-only ids where encountered.
- Confirm duplicate homepage/recommendation replay is fenced before Mongo/Search/Redis side effects.
- Record changed marketplace governance findings in `docs/architecture/marketplace-governance-certification-findings.md`.

POS-ADMIN-CERTIFICATION-01 PASS 5 public-visibility checks:

- Confirm CommerceHub search, homepage/recommendation projection, public homepage reads, super-admin homepage validation, and marketplace product read-model rebuilds use `CommerceHubCategoryLineageAuthority` or an equivalent active parent-chain SQL check before preserving listing/category visibility.
- Confirm CommerceHub product-detail reads also reapply `CommerceHubSearchSqlAuthorityOverlay` before serving Mongo-backed detail data, and that detail category/image fields remain listing/media-authoritative rather than restoring storefront category drift or legacy variant image strings.
- Confirm marketplace product list/detail reads and homepage curated-product scrubbing also apply the shared current vendor-suppression boundary. Search-only vendor suppression is not sufficient certification coverage for public visibility.
- Treat an active leaf CommerceHub category with an inactive, missing, self-referential, or cyclic parent chain as missing public visibility authority. Expected recovery outcomes are search delete, read-model delete, curated reference scrub, validation rejection, or reindex-count exclusion.
- Treat governance-hidden vendors and vendors suspended from new orders as missing public visibility authority on CommerceHub public product/homepage surfaces. Expected recovery outcomes are search delete, list exclusion, detail not-found, curated-reference scrub, or validation rejection.
- During rebuild overlap, compare target search counts against SQL-visible listings after CommerceHub category lineage filtering; do not promote a target index or preserve a homepage cache because leaf-only category checks appear healthy.

POS-ADMIN-CERTIFICATION-01 PASS 3 inventory/recovery addendum checks:

- Confirm reviewed availability reads, stock-sync/event fanout totals, storefront variant stock projections, and CommerceHub variant stock projections recompose through current stock-owner lineage rather than summing undeleted stock rows directly.
- Confirm reviewed SQL fallback checkout enrichment and reviewed storefront/search SQL overlays also recompose through current public variant lineage plus current stock-owner lineage. `ProductVariantReadRepository` SQL fallback and `ProductSearchSqlAuthorityOverlay` must not preserve default-variant or stock authority from raw undeleted rows alone.
- When investigating stale availability or stock-total drift, raw `ProductVariantStocks` rows are not enough evidence. Re-run the check through current variant semantic ownership, active parent product lineage, active warehouse lineage, and current stock-owner lineage before accepting or repairing quantity.
- If only stale/inactive-warehouse stock rows remain, the correct recovery outcome is suppression, delete/tombstone, repair, or no-op. Do not preserve derivative availability because raw stock totals still look positive.

## Payment, Accounting, And Fulfillment Incidents

Use this runbook for settlement replay, refund replay, payment callback drift, accounting outbox retry, shipment/fulfillment replay, reconciliation-required records, or suspected duplicate financial operation.

Procedure:

1. Identify the owner: `PaymentLifecycleService`, `OrderPaymentLineageAuthority`, `AccountingLedgerAuthority`, `IOrderFulfillmentService`, `OrderFulfillmentLineageAuthority`, refund service, settlement batch authority, payout/reconciliation service, or carrier recovery worker.
2. Reload current SQL state under the owning service/reconciliation path. Provider payloads and projections are evidence only.
3. Check idempotency keys, existing journal entries, refund ids, settlement ids, order payment status, fulfillment status, shipment lineage, unresolved webhook records, and reconciliation-required states.
4. Check `payment_cancellation_outbox_readiness` before widening payment-cancellation throughput or retrying manual cancellation recovery. Terminal failures or critical lag on that surface mean the replay-safe worker needs recovery, not direct payment/order mutation.
5. For settlement accounting, verify the settlement batch is current and non-deleted, verify current gross/fees still satisfy settlement authority bounds, then verify current `PaymentSettlementOrders` lineage still exists for the batch and still matches the recorded `PaymentSettlementBatch.OrderCount`. For post-settlement refunds, also verify the refunding order still belongs to that current settlement batch lineage. Existing post-settlement refund history means later refunds still require the same live settlement-lineage proof even if current `Order.PaymentStatus` is already `PartiallyRefunded`. A mismatch, missing order membership, invalid current amount/fees, or deleted batch is retry/reconciliation-required state, not permission to post from stored settlement snapshots.
6. Replay only through the owning queue/outbox/reconciliation path.
7. If current SQL is terminal or incompatible with a stale success/failure payload, expect no-op, suspense, reconciliation-required, or fail-closed behavior.
8. For accounting, confirm replay posts current SQL truth once, returns an existing journal, collapses stale evidence, or remains retryable for operator reconciliation.
9. For fulfillment, confirm shipment/payment recomposition uses current order and shipment lineage and cannot resurrect detached, cancelled, completed, returned, or replaced fulfillment visibility.
10. For storefront payment projection replay, missing or deleted SQL order authority should delete tenant-scoped derivative order/cache visibility; replay that conflicts with current SQL payment truth must not append payment timeline narrative from the stale payload.

Forbidden shortcuts:

- Manually inserting ledger journal rows to clear a dashboard.
- Editing order payment status directly.
- Marking accounting outbox messages processed without proving journal convergence.
- Treating provider success as local payment truth after SQL says expired, failed, cancelled, refunded, or amount-mismatched.
- Replaying refund or settlement payload amounts as final accounting authority.
- Accepting non-positive settlement gross amounts or settlement fees outside `[0, gross]` because a provider export or dashboard still looks internally consistent.
- Treating `PaymentSettlementBatch.OrderCount` as final settlement-membership authority when the settlement batch is deleted or current `PaymentSettlementOrders` lineage is empty or drifted.
- Treating stored `OrderRefund` settlement snapshot fields as enough proof that a post-settlement refund may still post against the bank account after current settlement lineage changed.
- Restoring a storefront order/payment projection or payment timeline from callback/replay payloads after current SQL order payment authority is missing or conflicting.
- Updating shipment projection state without current fulfillment lineage.

Escalation boundaries:

- Payment provider ambiguity goes to payment reconciliation, not projection repair.
- Ledger inconsistency goes to accounting reconciliation, not cache/search repair.
- Refund, payout, and settlement uncertainty remains reconciliation-required until evidence is explicit.
- Fulfillment/provider uncertainty stays blocked or reconciliation-required rather than being forced terminal.

## Drift Investigation

Use drift investigation when SQL and derivative state disagree.

Decision tree:

1. If SQL says the entity is not visible, derivative deletion/tombstone is the likely fix.
2. If SQL says visible and derivative is missing, run SQL-backed rebuild/reindex for the tenant/entity.
3. If derivative shows stale price, stock, media, category, payment, shipment, tenant, or publication state, inspect the governing authority helper and replay telemetry.
4. If drift is only Redis and the hydrate path revalidates SQL, invalidate and observe rehydration.
5. If drift is Elasticsearch, reindex through SQL overlays and verify invalid lineage deletes.
6. If drift involves money, fulfillment, tenant isolation, or actor authority, escalate before derivative repair.

Low-signal patterns:

- One stale UI read immediately after SQL commit.
- Duplicate replay collapse without customer-visible drift.
- Fail-closed deletion after tenant/product/payment lineage rejection.
- Backlog count without backlog age or repeated failure growth.

Dangerous anomaly patterns:

- Derivative state exists after SQL lineage rejects visibility.
- Replay applies side effects repeatedly for the same completed EventId.
- Tenant-scoped derivative ids are product-only or listing-only.
- Payment/accounting replay uses payload amount/status instead of SQL.
- All-tenant repair starts from a tenant-admin route.

## Operational Telemetry And Diagnostics

Primary diagnostic surface:

- Meter: `PointOfSale.Operational.Convergence`
- Phases: replay, rebuild, authority, dangerous primitive, drift
- Decisions: applied, collapsed, suppressed, no-op, retry, failed-closed, deleted, tombstoned, rehydrated
- Authorities: tenant, admin actor, product variant lifecycle, inventory lineage, order payment, order fulfillment, accounting ledger, SQL rebuild, dangerous primitive, projection freshness, cache/search drift
- Additional reviewed exported operational meters: `PointOfSale.Accounting`, `PointOfSale.Checkout.Locks`, `PointOfSale.Inventory.Reservations`, `PointOfSale.Elasticsearch.Search`, `PointOfSale.Redis.Performance`, `PointOfSale.Affiliate`, `PointOfSale.AbandonedCartRecovery`, `PointOfSale.Storefront.Publish`, `PointOfSale.Analytics.Reliability`, `PointOfSale.Marketplace`

Operator interpretation:

- High collapse with falling backlog is healthy replay convergence.
- Fail-closed spikes are healthy only when tied to known deletes, inactive tenants, publication changes, stale callbacks, or invalid lineage. Unexpected spikes need SQL authority sampling.
- Rebuild declarations without derivative convergence suggest worker failure, dependency pressure, or dangerous primitive rejection.
- Drift detection without SQL-backed reconciliation means diagnostics are working but repair has not completed.
- Missing telemetry on a Tier 1 path is itself an operability gap.

Alert guidance:

- Alert on sustained backlog age, repeated failure growth, fail-closed spikes without known cause, drift that does not reconcile, and payment/accounting retry loops.
- Avoid paging on isolated duplicate replay collapse, one-off fail-closed deletion after known unpublish/delete, or cache miss bursts that rehydrate successfully.
- Reviewed exported tenant dimensions use `tenant_bucket`; do not expect one time series per tenant in Prometheus/OpenTelemetry for these surfaces.
- Order-correlated reviewed reliability diagnostics expose bounded presence flags instead of raw order ids.
- Redis cache telemetry uses bounded cache and pattern families. Do not expect raw Redis keys or invalidation patterns in exported metric labels.
- Accounting telemetry uses `tenant_bucket` plus normalized operation/gateway/error labels. Do not expect raw tenant ids, raw gateway ids, or raw exception text in exported accounting metric labels.
- Do not include raw tenant ids, order ids, payment ids, customer data, document keys, Redis keys, invalidation patterns, payload fragments, or exception text in metric labels.

## Dangerous Operational Primitive Rules

The following primitives are allowed only behind reviewed authority or runbook boundaries:

- Direct SQL or bulk mutation.
- Full Mongo document replacement.
- Raw Elasticsearch visibility writes.
- Redis pattern invalidation or manual key mutation.
- Raw MassTransit publish/send.
- Global/all-tenant repair, reindex, or cache purge.
- Tenant override or support impersonation.
- Manual payment, refund, settlement, payout, ledger, or fulfillment repair.

Before using one, confirm:

- Current SQL authority was recomposed.
- Tenant and actor authority are current when request-driven.
- Replay collapse, freshness, idempotency, or lease behavior is intact.
- Fail-closed behavior is explicit.
- Telemetry will classify the convergence decision.
- Scope is tenant-local unless explicitly platform/global.

## Support Tooling And Impersonation Governance

No production support impersonation endpoint is currently certified. Support dashboards, tenant selectors, stale support UI state, and repair payload tenant ids are evidence only; they cannot choose tenant scope or actor authority for mutation.

Rules for any future support or break-glass tooling:

1. Recompose the support/platform actor from current SQL authority before action.
2. Recompose the target tenant from current SQL tenant lineage and trusted tenant context.
3. Capture reason, reference, target tenant, effective actor, original actor, request id, and expiry/revocation semantics.
4. Emit request-level operational audit and domain audit for any mutation.
5. Preserve replay-safe convergence: duplicate support action delivery must collapse, no-op, fail closed, or route to reconciliation.
6. Keep financial, payment, refund, payout, ledger, and fulfillment ambiguity inside lifecycle/reconciliation owners.

Forbidden support shortcuts:

- Acting from a stale support dashboard tenant id.
- Mutating another tenant because an operator selected it in the UI.
- Reusing a stale elevated session after actor, tenant, or governance status changed.
- Hiding support mutations in local logs without durable audit.

## POS Admin Authentication And Session Incidents

Use this runbook for stale tenant-admin access-token reuse, refresh replay, unexpected logout persistence, actor disable/role-change persistence, or vendor-governance identity incidents.

Procedure:

1. Identify the POS user id, tenant scope, request id, and bounded authority rejection reason. Do not capture raw access or refresh token values in logs or incident notes.
2. Treat the access token and refresh credential as evidence only. Inspect current SQL `Users` session binding, actor active/deleted state, role and tenant ownership, tenant lineage, and applicable vendor governance status.
3. Expect a current session to carry `pos_session` matching the hashed current SQL refresh binding. A missing or mismatching binding is a rejected stale session, not a reason to restore token state.
4. For cached tenant-registration replay, re-check the current SQL owner row, `!IsDeleted`, active actor state, current tenant ownership, active tenant lineage, and current tenant governance before allowing any session restoration. Cached success payloads are evidence only.
5. For logout or containment, clear current SQL session authority through the owning auth path. Do not merely delete browser cookies or allow an expired session to remain operational. If logout returns `SESSION_REVOCATION_FAILED`, treat the session as still active until a later SQL-backed revoke succeeds.
6. For refresh storms, expect one successful current rotation at most; stale concurrent rotations should reject. Re-authenticate only after current SQL actor/tenant authority is confirmed.
7. Escalate to platform authority when containment requires tenant disable/governance action or when support/override pressure appears.

Expected healthy outcomes:

- Logout rejects the prior access and refresh session after SQL commit.
- Logout may return a retryable failure when SQL revocation persistence is unavailable; that is healthier than reporting success while a copied token still matches current SQL authority.
- A successful refresh invalidates the prior access binding and prior refresh credential.
- Tenant-owner onboarding issuance and idempotent session restoration produce the same SQL-bound session shape as ordinary POS login.
- Cached tenant-registration replay fails closed instead of restoring cookies when the owner row, tenant ownership, tenant lineage, or tenant governance state is no longer current.
- Stale role, disabled/deleted actor, inactive tenant, or governance-banned tenant sessions fail closed.
- POS session rejection is visible through `PointOfSale.Operational.Convergence` without raw identity/token labels.

Forbidden shortcuts:

- Restoring raw refresh values in SQL or storing a newly issued raw refresh credential.
- Accepting an old signed JWT because its expiry has not elapsed.
- Bypassing current SQL session validation for an admin repair or incident route.
- Implementing support impersonation or tenant override as an undocumented cookie or claim substitution.

POS-ADMIN-CERTIFICATION-01 freeze checks:

- Confirm `PosSessionAuthority` remains wired to default POS JWT validation.
- Confirm `Users.RefreshToken` remains a hash-only, concurrency-protected current session binding.
- Confirm tenant-owner onboarding and idempotent onboarding restoration retain hash-only `pos_session` issuance rather than introducing a parallel credential path.
- Confirm future password/reset/user-disable/session-revoke behavior clears or rotates that binding and adds stale-session tests.
- Record changed findings in `docs/architecture/admin-authentication-session-certification-findings.md`.

## Inventory / Reservation / Warehouse Authority Incidents

Use this runbook for stale stock suspicion, reservation replay, payment-retry authority loss, warehouse disable overlap, return receipt replay, reservation cleanup uncertainty, or inventory rebuild/recovery questions.

Procedure:

1. Identify the authority owner: `ProductVariantInventoryLineageAuthority`, `InventoryConsistencyService`, `ProductVariantStockMutationSupport`, `ProductVariantStockRepository`, checkout authority guards, or the owning payment/fulfillment boundary.
2. Recompose current SQL lineage before action: current variant semantic owner, active parent product, active warehouse, current stock owner row, reservation state, remaining reserved quantity, and expiry.
3. Treat `InventoryReservations`, `ProductVariantStocks`, CommerceHub order state, cache/search/public stock, and outbox payloads as evidence only until current SQL lineage accepts them.
4. For marketplace orders, verify whether the order spans multiple tenant-owned suborders. Do not assume one tenant scope for reservation validation.
5. If raw active reservation rows exist but current SQL lineage rejects them, fail closed and route to repair/recovery rather than forcing payment retry, fulfillment, or public stock reuse.
6. For reservation creation incidents, verify that the reviewed path filtered candidate stock rows through current stock-owner lineage before accepting availability. Raw stock in an inactive warehouse is evidence only, not reservable authority.
7. For delivery commit incidents, distinguish inventory lineage from public publication visibility. A later marketplace unpublish/offline change may block new payment retry, but it must not by itself block stock consumption for an already-authorized order item whose current SQL inventory lineage still exists.
8. For return receipt recovery, confirm the delivered reservation still resolves to current stock ownership before restoring resellable or damaged quantities. A disabled warehouse or superseded/detached variant must fail closed instead of receiving replayed stock.
9. Rebuild or replay only from SQL-first entrypoints. Do not repair inventory authority by copying stock totals, reservation totals, or projected availability from derivative stores.

Expected healthy outcomes:

- Expired, foreign-owner, inactive-warehouse, detached-variant, or stale-stock-backed reservations block payment retry and fulfillment reuse.
- Duplicate or stale reservation evidence collapses to rejection, release/repair, delete/tombstone, or no-op rather than oversell-friendly reuse.
- Marketplace reservation validation accepts current cross-tenant suborders only when exact current SQL order-item ownership and exact reserved quantity still match.
- Return receipt replay rejects inactive-warehouse or stale-variant stock restoration before SQL quantities change.
- Stock replay and rebuild may repeat recomposition work, but current SQL lineage still decides whether stock/reservation evidence is consumable.

Forbidden shortcuts:

- Forcing payment retry because the order is still pending while current inventory lineage rejects reservation authority.
- Treating raw reserved quantity on `ProductVariantStocks` as enough proof that reservations are still valid.
- Rebuilding inventory totals from Mongo, Elasticsearch, cache state, exports, or stale recovery snapshots.
- Restoring returned stock into an inactive warehouse or superseded/detached variant from historical reservation evidence.
- Reassigning warehouses, moving stock, or migrating reservations outside a reviewed SQL transaction boundary with lock-order and replay rules.

Record changed findings in `docs/architecture/product-inventory-reservation-certification-findings.md`.

## Rollback And Emergency Override Governance

Emergency override is allowed only as explicit platform authority or documented runbook scope. It is not a second truth model.

Emergency authority is operational authority. It may stop, suspend, freeze, isolate, recover, repair, reindex, replay, shut down, or diagnose, but it must not create visibility, publication, tenant, governance, financial, inventory, or SQL authority. Current SQL authority always wins.

Rollback procedure:

1. Identify the owning SQL authority or lifecycle/reconciliation service.
2. If the target is derivative visibility, invalidate, tombstone/delete, rebuild, or reindex from SQL.
3. If the target is money, payment, refund, payout, settlement, ledger, or fulfillment, route to the owning lifecycle/reconciliation path.
4. If a global operation was too broad, recover by scoped SQL-first rebuild/scrub, not by copying old derivative state back.
5. Verify request-level operational audit, domain audit where applicable, and convergence telemetry are present for incident reconstruction.

Safe rollback outcomes are apply current SQL truth, delete, tombstone, no-op, retry, block, or reconciliation-required. Unsafe outcomes are manual projection edits, direct ledger/payment/order status changes, direct dead-letter suppression, and global repair from tenant-admin routes.

## Super-Admin Operational Chaos Certification

Use this checklist during replay storms, overlapping global rebuilds, emergency recovery, stale moderation replay, marketplace visibility incidents, and dangerous primitive pressure.

Procedure:

1. Confirm current SQL super-admin authority before platform action. A stale JWT after logout, token rotation, password change, deletion, or governance restriction must fail closed.
2. Treat a super-admin refresh credential as evidence only. Before rotating platform tokens, confirm current SQL `SuperAdmins` state, current governance status, and current token version.
3. If current governance no longer allows login, revoke stored platform refresh authority and treat the refresh attempt as rejected stale authority, not as a candidate session to preserve.
4. Identify the SQL authority owner before mutation: CommerceHub listing/product/variant/category visibility, homepage section governance rows, payment/fulfillment/accounting lifecycle, tenant lineage, or a documented SQL overlay.
5. Prefer tenant-scoped repair when incident scope is tenant-local. Use global/all-tenant operations only under platform/runbook scope.
6. For replay storms and dead-letter retry, reset into the normal ordered processor path and watch convergence telemetry for collapse, retry, fail-closed, delete, tombstone, or applied decisions.
7. For rebuild overlap, restart from the SQL-first entrypoint. Do not resume from Mongo, Redis, Elasticsearch, old queue payloads, exports, or dashboards.
8. For marketplace review/moderation replay, ensure search mutation revalidates `CommerceHubSearchSqlAuthorityOverlay`; stale SQL listing authority should delete tenant-scoped search visibility.
9. For public image recovery, verify rebuilt cards use only current SQL `ProductMedia`/`ProductVariants.MediaId` and clear images whose current SQL media linkage is gone; do not restore legacy or derivative image strings.
10. For review-driven search repair, verify indexed rating/count fields match current approved, non-deleted SQL `ProductReviews`; do not restore Mongo/search review summaries as moderation authority.
11. For affiliate/public repair primitives such as AF16E, verify current SQL tenant storefront metadata and `Tenant.Currency` before mutation. Do not inject fallback currency; missing authority must delete stale affiliate visibility.
12. For dangerous primitives, verify SQL authority, tenant/global scope, freshness/idempotency, fail-closed behavior, auditability, and telemetry before execution.

Regression-freeze expectations:

- Duplicate replay collapse is healthy only when side effects do not repeat and telemetry classifies the outcome.
- POS-ADMIN-CERTIFICATION-01 PASS 7 freezes payment-cancellation recovery as a leased outbox: duplicate worker restart overlap must skip active claims, clear claims on success/manual-review/failure, and never bypass the payment lifecycle owner.
- Review, moderation, homepage, recommendation, and recovery inputs are not marketplace visibility authority.
- Public image and review-summary repair must recompose current SQL media and moderation truth before search/read-model mutation.
- Affiliate repair/reindex must recompose current SQL tenant storefront/currency truth before Mongo/search mutation and must delete stale derivative visibility when that authority is missing.
- Unsupported rollback from derivative state is forbidden.
- Missing telemetry or audit on a Tier 1 platform operation is an operability gap.
- Changed findings must be documented in `docs/architecture/super-admin-operational-chaos-certification-findings.md`.

## Remaining Debt

This runbook intentionally stays lightweight in these areas:

- It does not define enterprise IAM, SSO, delegated administration, or support impersonation ceremonies.
- It does not introduce adaptive throttling or tenant-specific scheduler policy.
- It does not claim fixed throughput or rebuild-duration SLOs.
- It does not turn telemetry into a SIEM, durable audit table, or full distributed tracing program.
- It does not claim every CMS, SEO, notification, analytics, support-cache, feed/export, reporting rebuild, cache warmer, or global runbook path already uses shared helpers.

Future operational surfaces must document whether they use shared helpers or equivalent local controls, add replay/rebuild/tenant/telemetry tests, and update this runbook when operator behavior changes.

## CommerceHub Emergency Operations Incident-Day Controls

Use this checklist when CommerceHub visibility, search, payment intake, refunds, payouts, or recovery jobs must be contained during an incident:

1. Stop visibility first when customer exposure is unsafe: suppress the listing, suspend the tenant, suspend or ban the vendor through governance, then enqueue tenant/listing verification so MongoDB and Elasticsearch converge from SQL.
2. Stop payment intake with `CommerceHub:EmergencyStops:CommerceIntakeStopped=true` or `PaymentIntakeStopped=true`. This blocks CommerceHub checkout/payment readiness before intake proceeds.
3. Stop outward money movement with `CommerceHub:EmergencyStops:OutwardMoneyMovementStopped=true`, or use `PayoutsStopped=true` / `RefundsStopped=true` to isolate a narrower incident.
4. Treat emergency stop configuration as an operator change that needs deployment/config audit. The runtime gate logs critical stop decisions, but business truth remains SQL/ledger/reconciliation authority.
5. For runaway replay, recovery, or reindex, prefer not starting broad global work, cancel running work through deployment/runtime cancellation where available, lower batch pressure, and restart from the SQL-first entrypoint. Do not mark poison rows processed or copy derivative state into truth.
6. For rollback, re-run SQL-first reindex, verification, replay, delete/tombstone, lifecycle, or reconciliation paths. Do not restore MongoDB, Redis, Elasticsearch, queue payloads, dashboards, or exports as business truth.

Current PASS 15.4 status:

- Marketplace visibility stop: certified through governance suppression, tenant suspension, vendor governance, SQL overlays, and verification.
- Payment intake stop: certified through `CommerceHubEmergencyStopOptions` in `CommerceHubFinancialReadinessGate`.
- Payout/refund stop: certified through the same readiness boundary before outward money movement.
- Replay/recovery stop: partially certified through cancellation, pacing, scoped operation choice, and SQL-first restart semantics; universal durable pause/resume remains maturity debt.
- Diagnostics: certified through governance/operations dashboards, health checks, readiness gates, audit records, structured logs, and convergence telemetry.

## Tenant-Admin Recovery Callback Scope

Use this rule for abandoned-cart recovery result logging and any future endpoint that serves both tenant-admin users and external automation callbacks:

1. Tenant-admin requests derive scope from trusted tenant context and current SQL-backed actor/session authority.
2. A tenant-admin payload tenant that differs from trusted context is a cross-tenant misuse attempt and must fail closed before persistence or side effects.
3. An external automation callback may carry a target tenant only when its tenant-specific callback secret is validated and idempotency remains in force.
4. Do not convert callback payload identity into a general tenant-admin override mechanism.

Current PASS 2 behavior:

- Authenticated abandoned-cart recovery-result cross-tenant attempts return `403` and emit `admin_tenant_scope_mismatch` operational diagnostics.
- Admin recovery settings, eligibility, and statistics use `ITenantContext.RequiredTenantId`.
- Tenant-admin CommerceHub search repair may reindex only the authenticated tenant scope. Alias finalization and post-finalization catch-up remain platform/global recovery runbook operations.
- Inspected online-customer and payment-method admin SQL paths bind tenant scope explicitly rather than relying on compatibility headers or ambient query filters for certification-critical scope selection.
- Tenant-admin WhatsApp account/template, transport-control, kill-switch, log, and analytics operations now derive scope from `ITenantContext.RequiredTenantId`, and the backing account/template service lookups bind tenant scope explicitly in SQL.
- Tenant-owned bank, expense-category, financial-account, and shipping-method administration now derives scope from `ITenantContext.RequiredTenantId` and binds SQL row selection explicitly. Foreign bank/account/category/shipping ids should converge to not-found or bad-request outcomes; operators must not treat those failures as permission to run all-tenant repair or copy configuration rows between tenants.
