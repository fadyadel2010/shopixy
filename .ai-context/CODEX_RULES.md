# 2026-07-22 Storefront / Builder Runtime Certification Registration

- PASS SF-ADM2.3 Builder Section Composition Authority Freeze.
- PASS SF-ADM2.4 Builder Layout Authority Freeze.
- PASS SF-ADM2.5 Builder Theme Authority Certification.
- PASS SF-ADM2.6 Builder Section Content Authority Freeze.
- PASS SF-ADM2.7 Builder Publish Authority Certification.
- PASS SF-ADM2.8 Builder Preview Authority Certification.
- PASS SF-ADM2.9 Builder Version History & Rollback Authority Certification.
- PASS SF-ADM2.FINAL Store Builder End-to-End Authority Certification.
- PASS SF-ADM3.4 Admin Order Fulfillment & Shipment Authority Freeze.
- PASS SF-CMS2 CMS Page Identity Authority Freeze.
- PASS SF-CMS3 CMS Content Authority Freeze.
- PASS SF-CMS4 CMS SEO Authority Freeze.
- PASS SF-CMS5 CMS Publish Authority Freeze.
- PASS SF-CMS6 CMS Preview Authority Freeze.
- PASS SF-CMS7 CMS Version History & Rollback Authority Freeze.
- PASS SF-CMS.FINAL CMS End-to-End Authority Certification.
- P2-GD Storefront Navigation Authority Freeze: storefront navigation remains live SQL-derived, and Builder navigation writes remain POS Admin-only.

# PAYMENT-1.2 Payment Policy Evaluation Pipeline Rule (2026-07-10)

- Treat `docs/architecture/PAYMENT_1_2_POLICY_EVALUATION_PIPELINE.md` as the Payment Policy Evaluation Pipeline architecture freeze until superseded by an approved later phase.
- Do not implement runtime, EF entities, migrations, DTOs, APIs, controllers, provider adapters, frontend, policy persistence, rule authoring, governance UI, audit storage, replay tooling, or operational controls under PAYMENT-1.2.
- Preserve pipeline boundaries: Pipeline coordinates evaluation; `PipelineContext` is evaluation-local coordination state; `PolicyContext` is immutable input; facts remain owned by source authorities; policies/rules remain governed versions; Runtime executes only the returned decision; Adapters translate/expose capabilities, constraints, and evidence only.
- Pipeline work must preserve deterministic ordering, version binding, context/decision hashing, audit-before-executable-allow, replay safety, explicit conflict precedence, and fail-closed behavior for missing facts, invalid authority, missing versions, missing policies/rules, unknown capabilities/constraints, replay failure, and audit failure.
- Future payment evaluation pipeline, stage ordering, conflict precedence, `PipelineContext`, context assembly, policy discovery, rule execution, decision composition, failure, event, audit, or replay changes must update the PAYMENT-1.x architecture document and relevant `.ai-context` files together.

# PAYMENT-1.1 Payment Policy Framework Rule (2026-07-10)

- Treat `docs/architecture/PAYMENT_1_1_POLICY_FRAMEWORK.md` as the Payment Policy Framework architecture freeze until superseded by an approved later phase.
- Do not implement runtime, EF entities, migrations, DTOs, APIs, controllers, provider adapters, frontend, policy persistence, rule authoring, governance UI, or replay tooling under PAYMENT-1.1.
- Preserve framework boundaries: Facts are authoritative inputs; Rules never own facts; Policies compose rules; Governance owns lifecycle; Runtime never edits policies; Adapters never edit rules.
- Published policy/rule/catalog/fact-schema/evaluation/audit/replay versions must remain immutable and replayable. Corrections require new versions; rollback activates prior immutable versions.
- Future payment policy contracts, facts, rule catalog, governance, versioning, audit, replay, authoring, or persistence changes must update the PAYMENT-1.x architecture document and relevant `.ai-context` files together.

# CODEX Rules

Date: 2026-05-07

## Treasury Reconciliation And Analytics Rules

- Treasury reconciliation may detect provider, bank, refund, payout, settlement, and ledger drift; it may quarantine or route recovery back through certified authorities, but it cannot create money, complete money movement, or treat external evidence alone as internal SQL authority.
- Treasury analytics may calculate balances, KPIs, aggregates, forecasts, reports, exports, and alerts from tenant-scoped SQL ledger or CommerceHub financial transaction authority, but it cannot post journals, recognize cash, settle payouts, or override SQL financial truth.
- PASS C7-G requires CommerceHub settlement and payout final currency to remain EGP-only and CommerceHub-owned; do not derive platform settlement or payout currency from tenant/storefront currency labels, frontend payloads, caches, or projections.

## Purpose

This file defines mandatory operational behavior for future AI-assisted development, architecture work, and risk audits in this repository.

These rules are specific to PointOfSale. They are anchored to the authority and risk model documented in:

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`

When a deeper subsystem document exists, it must also be read before work starts.

## Mandatory Pre-Read

Before implementing code, proposing architectural changes, or reporting risks, always read:

1. `.ai-context/PROJECT_CONTEXT.md`
2. `.ai-context/ARCHITECTURE_TRUTH.md`
3. `.ai-context/KNOWN_RISKS.md`

Read additional local documents and owning code when the task touches their area, especially:

- `docs/architecture/*` for authority, finance, projection, backlog, or recovery work
- relevant services, interceptors, outboxes, consumers, hosted workers, schedulers, and migrations

Do not begin work or produce audit conclusions from memory alone.

## Non-Negotiable Rules

### 1. Read Context Before Work

- Establish the current architecture and risk posture before changing code.
- Do not infer authority from UI visibility, convenience, or historical memory.
- If code and prior assumptions disagree, follow the code and current durable documents.

### 2. Preserve Backward Compatibility

- Default to additive, non-breaking changes.
- Do not silently rename contracts, remove fields, repurpose enums, or change event meaning.
- Preserve API, event, schema, data-shape, and migration compatibility unless a breaking change is explicitly authorized.
- When a breaking change is unavoidable, document blast radius, rollout order, and downstream impact before implementation.

### 3. Respect Source-Of-Truth Ownership

- PostgreSQL is the authoritative internal write model for business truth.
- The SQL commit boundary is the internal durability boundary.
- MongoDB, Redis, Elasticsearch, RabbitMQ, and derivative read models are not authoritative business state.
- External providers own only their own side effects. They do not replace local SQL, ledger, or lifecycle authority.
- Do not move business truth into frontend code, cache state, projection documents, or transport success.

### 4. Avoid Duplicate Services And Shadow Authorities

- Search for existing lifecycle, orchestration, reconciliation, projection, or domain services before creating new ones.
- Extend the existing authority boundary when possible instead of creating a second path for the same mutation.
- Do not create duplicate write paths for payment, order, fulfillment, inventory, refund, payout, tenant-resolution, or accounting behavior.
- If a second path is truly required, document why the current owner cannot safely handle it.

### 5. Preserve Tenant Isolation

- Tenant identity must come only from trusted resolution paths.
- Never trust raw client tenant identifiers as authoritative input.
- Do not introduce reads, writes, caches, projections, events, or repair logic that can mix tenant scope.
- Multi-store or cross-tenant events must carry explicit scope when a single `TenantId` is insufficient.
- Treat tenant isolation regressions as critical defects.

### 6. Keep Migrations Rollout-Safe

- Prefer additive migrations, staged cutovers, and backfill-safe schema evolution.
- Preserve existing tenant data, historical records, and replay safety.
- Do not require all nodes, workers, or consumers to update simultaneously unless the rollout is explicitly designed for that dependency.
- Do not introduce destructive schema moves, silent data rewriting, or authority-shifting migrations without explicit validation and justification.

### 7. Preserve Docker And Runtime Compatibility

- Do not assume one developer machine layout, one filesystem layout, or one host-only deployment path.
- Preserve compatibility with the deployment shape documented in `.ai-context/PROJECT_CONTEXT.md`.
- The repository does not define a single authoritative Docker topology, so changes must remain compatible with both host/process deployment and external containerization.
- Do not introduce hidden startup, networking, storage, or background-worker assumptions that break separated service hosting.
- Prefer configuration-driven behavior over hard-coded environment assumptions.

### 8. Preserve Event Consistency

- Preserve atomicity between authoritative SQL mutation and local outbox or equivalent staged intent.
- Preserve idempotency expectations for APIs, consumers, webhooks, outboxes, and repair workers.
- Respect existing aggregate ordering assumptions.
- Do not emit rebuilt current-state payloads as if they were the original historical event unless the event is explicitly modeled as compensation or snapshot repair.
- Do not bypass lifecycle services, interceptors, or invariant checks through raw SQL, bulk updates, or convenience shortcuts without deliberate replacement controls.
- For category hierarchy, never treat Mongo category projections, cached breadcrumbs/paths, search documents, affiliate category event payloads, or CommerceHub category paths as lineage authority. Product-category mutation, rebuild, reindex, repair, and recovery paths must recompute current live category lineage from SQL and tombstone/delete invalid descendant visibility.
- For product media, never treat CDN URLs, Mongo product media, search thumbnails, affiliate thumbnails, CommerceHub listing images, legacy `Images`, or event payload media arrays as media authority. Product-media mutation, propagation, rebuild, reindex, repair, and recovery paths must recompute current media/order/variant linkage from SQL `ProductMedia` and `ProductVariants.MediaId`, reject stale/future media versions, and delete/tombstone visibility when SQL no longer proves ownership.
- For multi-channel product publication, never treat Mongo product documents, affiliate public/intelligence docs, CommerceHub listing documents, Elasticsearch documents, homepage/discovery documents, feed/export snapshots, cache entries, or publication event payloads as visibility authority. Product publication mutation, propagation, rebuild, reindex, repair, retry, and feed/export paths must recompute current SQL `Products.PublishStatus`, `IsOnlineVisible`, active/delete/archive state, and current SQL listing publication where applicable; stale publish and stale unpublish replay must collapse to the current SQL truth.
- For product search/index visibility, never treat Mongo product documents, Elasticsearch documents, autocomplete fields, search repair payloads, replayed product events, affiliate search documents, or CommerceHub search documents as authority. Search mutation, rebuild, reindex, repair, and auto-healing paths must recompose current SQL product/variant/stock/category/media/promotion/publication/listing authority before indexing, and must delete/tombstone when SQL does not prove live visibility.
- For autocomplete/suggestion visibility, never return cached CommerceHub autocomplete, search suggestions, zero-result suggestions, popular terms, product-name completion terms, or category suggestions as final authority. Response builders must recompose product suggestions from current SQL CommerceHub listing/product/variant/category/media/stock/price/publication authority, filter category suggestions through live SQL category lineage, and drop suggested/popular terms that no longer resolve to current SQL-visible CommerceHub product or category visibility.
- For homepage/recommendation visibility, never treat Redis homepage caches, Mongo product documents, Elasticsearch ranking/search hits, homepage section JSON, CommerceHub homepage read models, affiliate discovery documents, or replayed homepage/recommendation rebuild payloads as final authority. Storefront homepage shelves, curated product hydration, CommerceHub homepage projection, affiliate-mode homepage visibility, and future recommendation workers must recompose current SQL product/listing/category/media/stock/price/promotion/publication authority before serving or rebuilding public discovery visibility, and must remove/tombstone stale curated references instead of preserving them.
- For admin repair/rebuild tooling, never full-replace storefront/search/recommendation Mongo documents from stale repair snapshots or partial SQL field copies. AdminSync, direct repair endpoints, cache warmers, reindex tools, and future recommendation rebuild jobs must route product visibility through current SQL recomposition and write freshness-aware tombstones when SQL no longer proves active, online, published, live-category visibility.
- For storefront product read-model writes, never hand-roll `ProductSearchSqlAuthorityOverlay` + `TombstoneIfFreshAsync` + `UpsertAsync` sequencing in create/update/lifecycle/admin repair code when `IProductSqlAuthorityProjectionWriter` and `IStorefrontProductProjectionMutationBoundary` are available. Product create/update consumers, lifecycle projection consumers, and admin rebuild tooling must use the shared writer so optional non-SQL fallbacks cannot be reintroduced accidentally; the writer must route low-level product upsert/tombstone mutation through the reviewed boundary.
- For storefront simple products, never treat legacy product-level `Price` / `OnlinePrice`, stale Mongo `ProductDocument`, promotion overlay state, or event payload unit arrays as purchasability authority. `ProductUnits` plus the current SQL default unit own simple-product storefront price; shared overlay/writer/rebuild/search paths must recompose `DefaultUnit` and `Units` from SQL every time, and a simple product without a current default unit must fail closed to non-purchasable visibility.
- For public variant lifecycle, never treat projected variant stock, Mongo product variants, Elasticsearch documents, Redis entries, CommerceHub listing documents, affiliate documents, import/clone payloads, bulk repair payloads, or replayed variant events as proof that a variant is still purchasable or externally visible. Storefront, CommerceHub, affiliate, search, feed/export, rebuild, repair, retry, and cache-rehydration paths must prove current SQL variant-plus-parent lineage through `ProductVariantLifecycleAuthority` or an equivalent reviewed SQL recomposition boundary. Archived, detached, orphaned, deleted, inactive, unpublished, offline, or invalid-lineage variants must fail closed.
- For storefront product overlay mutations, never full-replace `ProductDocument` outside `ProductReadRepository` internals, `IProductSqlAuthorityProjectionWriter`, or `IStorefrontProductProjectionMutationBoundary`. Promotion or other overlay-only refresh paths must use field-scoped updates so they cannot silently overwrite SQL-authoritative unit/default-unit, media, category, publication, or tombstone state.
- For product cache invalidation fanout, never let duplicate product create/update/delete/stock/price/publication replay repeatedly execute Redis product-list, product-detail, promotion, or homepage pattern invalidation when a durable SQL event fence is available. Cache invalidation is best-effort infrastructure, but high-volume product cache consumers must require SQL context, collapse duplicate/active EventId overlap before Redis side effects, and prefer current SQL product slug/identity over stale payload cache keys.
- When a change intentionally introduces or preserves an approved raw-SQL locking/idempotency/reconciliation primitive, update the relevant architecture-test allowlist or exclusion in the same change. A failing source guard is a real governance defect, even if the underlying primitive is locally safe.
- Treat direct `ReplaceOneAsync`, `ReplaceOneModel`, or raw Elasticsearch indexing on Tier 1 visibility paths as dangerous primitives. Use a shared repository/writer when one exists; otherwise require explicit freshness fencing plus current SQL recomposition, and document why the path cannot use the shared primitive.
- When a change intentionally introduces or preserves a reviewed Tier-1 dangerous primitive, update the exact focused governance boundary in the same change: `CrossSystemAuthorityRegressionGuardTests` allowlists plus dedicated reviewed-pattern assertions for direct SQL/bulk mutation, high-risk `ReplaceOneAsync`, raw `IElasticClient` writes, or Redis `RemoveByPatternAsync` fanout.
- Development-only exceptions must stay obviously development-scoped. A dev seed or repair helper may use a dangerous primitive only when it lives under an explicit development/tooling boundary and still routes derivative search/index side effects through the reviewed service wrapper instead of raw low-level fanout.

### 9. Stay Reconciliation-Aware

- External side effects can remain uncertain after local authority commits.
- Do not force uncertain payment, refund, payout, shipment, or provider outcomes into false terminal states.
- Preserve `Processing`, `ReconciliationRequired`, unknown-outcome, and fail-closed behaviors where the architecture requires them.
- Never bypass readiness gates merely to restore availability.
- When a change crosses the SQL boundary into external execution, confirm that recovery, replay, and reconciliation paths still work.

### 9A. Preserve Storefront Pricing Authority Across Affiliate/Public Surfaces

- Do not introduce new affiliate/public/product-card/discovery/detail code that reads product-level `EffectivePrice`, `Price`, or `OnlinePrice` as final public price authority when storefront pricing is unit-aware.
- Prefer shared storefront-visible pricing helpers over consumer-local price math.
- Treat `DefaultUnit` and current SQL `ProductUnits` as authority-critical for simple-product online visibility.
- If storefront-authority pricing or stock cannot prove purchasability, fail closed instead of showing a stale price.
- Rebuild, replay, repair, and reindex paths must recompose affiliate/public pricing from current SQL-backed storefront authority, not from stale payload fields or stale Mongo snapshots.

### 10. Distinguish Confirmed Risks From Theoretical Risks

- A confirmed risk requires direct evidence from code, schema, runtime behavior, tests, or a documented live architectural gap.
- A theoretical risk is a plausible failure mode that was not found active in the inspected path.
- An accepted tradeoff is an intentional consistency, latency, or availability cost already allowed by the architecture.
- Label findings precisely and avoid inflating intentional tradeoffs into defects.
- Risk reports must include affected systems, root cause, impact, mitigation status, and evidence references.

### 11. Do Not Re-Report Accepted Tradeoffs

- Read `.ai-context/KNOWN_RISKS.md` before reporting findings.
- If the same issue is already recorded as an accepted tradeoff, do not restate it as a new bug.
- Extend an existing risk entry only when there is materially new evidence, broader scope, changed mitigation status, or explicit closure.
- Normal read-model lag, fail-closed financial gating under uncertainty, and reconciliation states are not standalone defects unless the implementation violates the documented design boundary.

### 12. Update `KNOWN_RISKS.md` After Major Findings

- After any major audit finding, architecture discovery, or mitigation that changes system risk posture, update `.ai-context/KNOWN_RISKS.md`.
- Merge overlapping findings instead of creating duplicates.
- Reclassify entries when they move between `THEORETICAL_RISK`, `CONFIRMED_RISK`, `MITIGATED_RISK`, or `ACCEPTED_TRADEOFF`.
- Record only meaningful deltas: new evidence, changed scope, mitigation progress, or closure.
- Do not leave major findings only in chat output.
- When the work changes the status of an architecture source guard, allowlist, or dangerous primitive policy, update the other `.ai-context` architecture files as well so future sessions do not assume stale enforcement.

### 9B. Preserve Extracted Storefront Product Governance Boundaries

- New Tier-1 storefront product create/update/lifecycle/rebuild/repair code must use `IProductSqlAuthorityProjectionWriter` for SQL recomposition and convergence.
- `IProductReadRepository.UpsertAsync` and `TombstoneIfFreshAsync` are dangerous storefront product mutation primitives. Runtime code must not call them directly except inside `StorefrontProductProjectionMutationBoundary`, where the mutation intent is typed and reviewed.
- `StorefrontProductProjectionMutationBoundary` is the approved wrapper for storefront `ProductDocument` full upsert and visibility tombstone mutation. Do not bypass its tenant/product/event validation, review reason requirement, or visible-projection check by adding another product repository write path.
- Storefront-visible pricing and affiliate/public pricing logic must prefer `ProductStorefrontVisibilityAuthority`-backed helpers (`StorefrontEffectivePrice`, `StorefrontVisiblePrice`, `TryResolveAffiliatePublicPricing`, `TryResolveAffiliateDiscoveryBasePrice`) over product-level `EffectivePrice`, `Price`, or `OnlinePrice`.
- If a future path legitimately cannot use these primitives, document the domain-specific reason in the architecture docs and add focused source/runtime tests in the same change.

### 9C. Preserve Variant Lifecycle Authority

- Use `ProductVariantLifecycleAuthority` when runtime code needs to decide whether a variant may be public, purchasable, stock-visible, search-visible, CommerceHub-visible, affiliate-visible, or feed/export-visible.
- A variant with missing/detached parent lineage, mismatched tenant lineage, deleted rows, inactive state, or an inactive/offline/unpublished parent must collapse to non-public visibility even when stale projections or retry payloads still claim stock, price, media, listing, or publication state.
- Replayed import/clone/bulk/stock/price/listing/search events must re-read SQL and converge to the current lifecycle result. Do not restore inherited pricing, stock, media, publication, or channel visibility from event payloads after SQL lineage changed.
- If future recommendation, feed/export, standalone autocomplete, external sync, direct repair, or cache warmer code cannot use the shared helper directly, it must define an equivalent SQL recomposition boundary, document the reason, and add replay/rebuild tests in the same change.

### 9D. Preserve Variant Clone / Import Authority

- Use `ProductVariantCloneImportAuthority` for product/variant clone, import, copy-from-existing, template duplication, marketplace sync, or bulk catalog ingestion code that creates or updates product/variant identity, media, or initial stock.
- Do not copy SKU, barcode, media linkage, inventory linkage, publication state, CommerceHub/affiliate/feed mappings, or external identifiers from a source product/variant as authority. SKU creation may deterministically regenerate to a free SQL-owned value; barcode conflicts must fail closed; media inheritance must be current same-product image media; public visibility still depends on `ProductVariantLifecycleAuthority`.
- Duplicate import/clone execution must collapse through an idempotency boundary or through SQL semantic ownership constraints plus deterministic no-op/regeneration behavior. Future import jobs must not rely on Mongo, Elasticsearch, Redis, feed snapshots, or stale queue payloads to decide whether a variant already exists or owns a SKU/barcode.

### 9E. Preserve Variant Merge / Split Semantic Authority

- Use `ProductVariantMergeSplitAuthority` for variant merge, split, duplicate consolidation, supplier deduplication, catalog normalization, automatic cleanup, or bulk repair code that supersedes one variant with another semantic owner.
- Only `ProductVariantSemanticLifecycleState.Current` variants may own SKU/barcode, inventory, reservations, pricing, media, publication, CommerceHub mappings, external/feed identity, or public purchasability. Merged sources, split parents, and detached variants are lineage records only.
- Before superseding a variant, reject or explicitly migrate open reservations through the authority boundary. Do not leave active reservations on a source variant that is being merged or split.
- Replayed merge/split jobs must collapse by operation id when the SQL row already reflects the same operation, and must reject stale divergent operations when another merge/split already superseded the variant.
- Rebuild, replay, cache, search, affiliate, CommerceHub, feed/export, and repair paths must recompose from current SQL semantic ownership and must not restore stock, SKU/barcode, pricing, media, publication, external mapping, or purchasability from stale payloads for superseded variants.
- If a future merge/split implementation needs physical stock transfer or reservation migration, implement it inside one SQL transaction with deterministic stock-row locking and post-commit product/variant/stock/listing event fanout; add focused runtime tests for duplicate execution, stale replay, partial fanout, and rebuild convergence in the same change.

### 9F. Preserve Variant Inventory / Reservation Lineage Authority

- Use `ProductVariantInventoryLineageAuthority` when runtime code decides whether variant stock can be reserved, stock can be mutated, reservations can be consumed, or stock visibility can be propagated.
- Do not treat an active `InventoryReservation`, a `ProductVariantStock` row, a stock-sync outbox payload, CommerceHub order state, Mongo/Search/cache availability, or a recovery candidate as sufficient inventory authority. Current SQL variant semantic ownership, active parent product lineage, active warehouse ownership, non-deleted stock, and active unexpired reservation state must be recomposed.
- Reservation creation must filter candidate stock rows through current stock-owner lineage before raw available quantity is accepted. An inactive warehouse or stale stock-owner row is not reservable inventory authority even if the row still has quantity.
- Reservation release and cleanup may inspect stale reservations to repair them, but checkout/payment/fulfillment consumption must filter to current consumable reservation lineage. Expired, released, superseded, orphaned, inactive-warehouse, or detached-product reservations must collapse to fail-closed/no-op instead of being consumed by replay.
- Delivery commit for an existing order item is inventory-lineage-authoritative, not publication-authoritative. Current public unpublish/offline state may block new reservation-backed payment retry, but it must not block stock consumption when current SQL order-item and inventory lineage still prove the reserved stock owner.
- Return receipt replay may inspect historical committed reservations as delivered-quantity evidence, but it must revalidate current stock ownership before adding resellable or damaged quantities. Inactive warehouse or superseded/detached variant lineage must fail closed instead of restoring SQL stock from historical reservation evidence.
- Stock-sync, feed/export, cache, CommerceHub, affiliate, and search stock propagation must suppress stale or detached stock events when current SQL lineage no longer proves the variant/warehouse/stock owner. Stale stock replay must not restore public availability after supersession, warehouse reassignment, deletion, archive, or rebuild.
- Storefront cart SQL fallback, affiliate intelligence SQL stock snapshots, and stock-compensation replay are frozen under the same rule. Do not sum raw undeleted `ProductVariantStocks` rows directly when preserving cart quantity, affiliate stock visibility, or replaying a "fresh" stock snapshot; recompose through current stock-owner lineage first.
- Future merge/split stock transfer, reservation migration, warehouse reassignment, external stock feed, inventory import, or reconciliation code must document the SQL transaction boundary, lock order, replay collapse behavior, and derivative fanout/rebuild semantics in the same change.

### 9G. Preserve Derivative Export / Replay Convergence

- Treat feed/export generation, external sync payloads, search/reindex snapshots, autocomplete/suggestion caches, recommendation/homepage rebuilds, cache hydration, and cache warmers as derivative visibility systems.
- Before emitting or returning product/variant visibility from those systems, recompose current SQL publication, variant lifecycle, semantic ownership, stock lineage, pricing/media/category authority, and owning channel metadata through existing helpers. Prefer `ProductVariantLifecycleAuthority` and `ProductVariantInventoryLineageAuthority` for variant-shaped public visibility.
- Replay suppression means deterministic collapse against current authority. Delayed outbox messages, queue retries, dead-letter replays, stale snapshots, warmers, and recovery rebuilds may trigger recomposition, but must delete/scrub/tombstone/no-op when SQL now rejects visibility.
- Do not add ad-hoc exporter or projection-local lifecycle rules that compete with existing product/variant authority helpers. If a future product feed/export writer has a domain-specific payload, keep the payload mapping local and the eligibility/visibility proof shared and reviewed.
- When no concrete feed/export writer exists in the inspected codebase, document that honestly. Harden the live derivative surfaces that exist, record the remaining debt, and require runtime replay/rebuild coverage when the missing writer is introduced.

### 9H. Preserve Order / Fulfillment Shipment Lineage Authority

- Use `OrderFulfillmentLineageAuthority` when shipment events, carrier callback fanout, stale queues, projection rebuilds, order shipment visibility, or order status synchronization need to decide whether a shipment still owns an order.
- Treat shipment event payload status, tracking, carrier values, webhook timing, outbox retry, Mongo order documents, Redis shipment/order cache, notification payloads, warehouse export state, and analytics projections as evidence only. Recompose the current non-deleted same-tenant SQL `Shipment` and owning `Order` before mutation or customer-visible shipment fanout.
- Do not duplicate order or fulfillment lifecycle semantics. `IOrderFulfillmentService`, payment lifecycle, shipment/carrier services, reservation lineage authority, and order status sync keep their existing ownership. Add shared SQL lineage validation at their handoff boundaries instead of creating a global fulfillment orchestrator.
- Missing, detached, deleted, wrong-order, cancelled, returned, failed, or stale shipment evidence must collapse deterministically against current SQL truth. Rebuild and repair may rehydrate from SQL; they must not reconstruct shipment tracking or visibility from an old callback or replay payload when SQL lineage no longer proves it.
- Future warehouse reassignment/transfer, affiliate shipment export, carrier adapter callback, external shipment feed, support/admin cache, analytics projection, and fulfillment reconciliation additions must document SQL lineage proof, replay collapse, rebuild convergence, and any intentional residual debt in the same change.

### 9I. Preserve Payment / Callback / Settlement Lineage Authority

- Use `PaymentLifecycleService` for `Order.PaymentStatus` mutation and use SQL payment/refund/settlement state as the only status authority. Provider callbacks, webhook logs, retries, reconciliation exports, stale queues, Mongo/Redis visibility, notifications, and dashboard/export projections are evidence or derivatives only.
- Refund and settlement code that changes visible payment status must stage canonical payment-status fanout from the lifecycle owner and preserve refund-id/settlement-id replay collapse. Do not make refund, settlement, export, cache, or projection consumers infer a competing status transition from payloads.
- Use `OrderPaymentLineageAuthority` or an equivalent reviewed SQL recomposition boundary before a payment facet replay can mutate customer-visible derivative order status. Stale callback or payment event payloads must not restore collected/settled/completed visibility after current SQL says failed, expired, cancelled, partially refunded, refunded, deleted, or otherwise no longer owns that visibility.
- Keep provider ambiguity explicit. Late provider success after terminal SQL payment truth must remain unresolved/suspense/manual-review evidence where existing policies require it; do not create fake synchronous provider consistency, duplicate capture lanes, or payment completion after refund/cancel just to clear a projection.
- Future CommerceHub payment visibility, affiliate/accounting/reporting exports, support/admin caches, payment cache warmers, notification workers, provider reconciliation exporters, and new callback adapters must document SQL lineage proof, replay collapse, settlement/refund convergence, rebuild/recovery behavior, and intentional remaining debt in the same change.

### 9J. Preserve Accounting / Ledger Replay Authority

- Use `AccountingLedgerAuthority` for accounting outbox dispatch paths that decide whether replayed order collection, refund, or settlement evidence may generate a journal. Do not let payload amount, payment status, COD delivery state, refund context, or settlement totals become final ledger authority.
- Ledger posting must converge from current SQL. Current `Orders`, `OrderRefunds`, `PaymentSettlementBatches`, existing `JournalEntries`, idempotency keys, and reconciliation state decide whether to post once, return an existing journal, collapse stale replay to no-op, or remain retryable for operator reconciliation.
- Stale online/wallet/COD collection replay must not recognize revenue after current SQL payment truth is failed, expired, rejected, amount-mismatched, pending, deleted, or not currently COD-delivered where COD delivery is required.
- Refund accounting must recompose from current `OrderRefund` rows before journal generation. Payload refund amounts and settlement context are evidence only; partial/full refund replay must not override current refund lineage or create a duplicate refund journal.
- Settlement accounting must recompose from current `PaymentSettlementBatch` rows before journal generation. Payload gross, fees, provider, settlement id, payout batch id, or export snapshots are not settlement authority.
- Future financial export writers, tax/reporting snapshots, BI aggregates, CommerceHub finance dashboards, affiliate accounting exports, payout export files, provider reconciliation exporters, support/admin caches, and accounting cache warmers must either use SQL ledger/accounting authority directly or document an equivalent reviewed recomposition boundary with replay/rebuild tests.

### 9K. Preserve Dangerous Primitive Governance

- Treat direct Mongo full replacement, raw Elasticsearch visibility writes, Redis cache mutation fanout, direct SQL/bulk mutation, and raw bus publishing as dangerous primitives. They are implementation details, not authority boundaries.
- Prefer existing domain authority services, SQL overlays, projection writers, lifecycle services, outbox processors, or cache/search wrappers. Do not add a new local helper that reinterprets visibility, payment, fulfillment, inventory, settlement, or accounting semantics.
- When a narrow derivative mutation path legitimately cannot use a richer domain writer, call `DerivativeProjectionMutationGuard.RequireReviewedMutation` or add an equivalent runtime precondition. The mutation must declare primitive type, boundary, document key, event id/timestamp/version, authority kind, freshness guard, fail-closed behavior, and a review reason. Full visibility replacement/search writes must declare SQL authority.
- A static allowlist entry is not enough for a new Tier 1 dangerous primitive. Add runtime guard behavior, focused tests, source guard assertions, and `.ai-context` or architecture documentation in the same change.
- Storefront footer projection is the current example: event delivery is trigger-only, SQL rows are recomposed, `ProjectionEventFence` collapses duplicate active replay, the freshness filter blocks stale replacement, and the runtime dangerous-primitive guard validates the reviewed Mongo replacement.
- Storefront config projection is the cache/config example: event delivery is trigger-only, SQL store config plus tenant currency are recomposed, duplicate active replay collapses before side effects, the freshness filter blocks stale full-document replacement, and the runtime dangerous-primitive guard validates both the Mongo replacement and Redis cache invalidation under the named storefront config/checkout/store metadata authority scope.

### 9L. Preserve Tenant Lineage Authority

- Use `TenantLineageAuthority` when replay, rebuild, repair, cache hydration, admin sync, or derivative mutation code must decide whether a tenant-scoped row or projection still belongs to a tenant.
- Do not infer tenant ownership from event payloads, raw request headers, Mongo/read-model documents, Redis key shape, Elasticsearch documents, cached storefront config, route ids, or stale rebuild inputs. These are candidate evidence only.
- Storefront config projection must reload current SQL `StoreConfig` joined to the current active `Tenant` before Mongo replacement or Redis invalidation. Missing, inactive, deleted, or cross-tenant lineage must fail closed to deletion/scrub/no-op, not stale visibility.
- Admin or repair tooling must use `ITenantContext.RequiredTenantId` or another reviewed trusted tenant resolver. Do not parse `X-Tenant-Id` locally as authority; the header exists only after trusted middleware resolution for compatibility.
- Future tenant-scoped CMS, homepage, affiliate, CommerceHub, search, support-cache, analytics, notification, cache-warmer, and direct repair paths must document tenant SQL lineage proof, replay collapse behavior, derivative fail-closed behavior, and remaining debt in the same change.

### 9M. Preserve Tenant-Scoped Derivative Projection And Cache Isolation

- Use tenant-scoped derivative identifiers for tenant-owned public projection writes outside PostgreSQL. `TenantScopedDerivativeKey` is the preferred helper when a Mongo/search/cache document id would otherwise be only a product, listing, category, or route id.
- Do not full-replace, delete, index, reindex, or repair a tenant-scoped derivative document with a product/listing/category id alone. The mutation filter or document id must include tenant scope unless the surface is explicitly global and documented.
- Cache hydration is trigger evidence only. Storefront homepage products/categories, affiliate public products/stores, CommerceHub homepage/listing read models, search/autocomplete/recommendation cards, and future cache warmers must revalidate active SQL tenant lineage plus current SQL visibility before serving or mutating public data.
- Store profile, tenant-currency, homepage, recommendation, affiliate, and CommerceHub replay must reload current SQL tenant/profile/currency/visibility before fanout. Payload names, slugs, currencies, section JSON, ranking lists, and cached cards are not authority.
- Inactive, deleted, moved, missing, or cross-tenant SQL lineage must collapse to delete, scrub, no-op, or empty visibility. It must not preserve stale affiliate stores, public affiliate products, marketplace homepage sections, storefront homepage categories, search documents, or cache entries.
- Future repair scripts and reindexers must use the same tenant lineage and tenant-scoped key rules as runtime consumers, including tests that prove stale tenant replay cannot hydrate or index into another tenant scope.

### 9N. Preserve Tenant Admin Actor Authority

- Use `AdminTenantAuthority` / `AdminTenantAuthorityMiddleware` for tenant-bound privileged endpoints that rely on POS/admin roles. JWT roles and tenant claims are session evidence only; current SQL `Users` and current active SQL `Tenant` rows decide tenant admin authority at request time.
- Do not add tenant-scoped admin mutation, repair, rebuild, cache invalidation, publication, support, or reindex endpoints that trust `X-Tenant-Id`, query-string tenant ids, frontend tenant selectors, stale support UI state, or repair payload tenant ids as authority. Use `ITenantContext.RequiredTenantId` after trusted tenant resolution.
- Tenant-scoped admin controllers for orders, fulfillment, inventory, storefront configuration, CMS/publication, search/cache, or repair/rebuild controls must not locally parse tenant headers as mutation authority. Use `ITenantContext.RequiredTenantId` after tenant resolution and let `AdminTenantAuthority` recompose actor scope after role authorization.
- Stale admin sessions must fail closed. If a user was disabled, deleted, role-downgraded, moved to another tenant, or the tenant was deactivated, privileged tenant-bound work must return forbidden rather than relying on still-valid JWT role claims.
- Tenant-admin operational tooling must remain tenant-scoped. Do not expose global index deletion/recreation, all-tenant repair fanout, global cache purge, or all-tenant projection mutation through tenant admin routes. Platform/global operations require explicit super-admin/platform authority or a reviewed runbook boundary.
- When a future support elevation, impersonation, tenant-switching, delegated-admin, cache warmer, publication repair, or global recovery path is introduced, document actor authority, tenant scope, replay collapse, platform boundary, and remaining debt in the same change with focused tests.

### 9O. Preserve Replay / Drift / Rebuild Observability

- Use `OperationalConvergenceTelemetry` for semantic convergence observability when code collapses replay, suppresses stale fanout, fails closed on authority, deletes/tombstones derivative visibility, rejects stale cache hydration, declares SQL rebuild authority, governs a dangerous primitive, or detects drift.
- Prefer shared authority boundaries as telemetry chokepoints: `ProjectionEventFence`, `ProjectionAuthorityValidator`, `SqlTruthRebuildGuard`, `DerivativeProjectionMutationGuard`, `TenantLineageAuthority`, `AdminTenantAuthority`, product/variant lifecycle and inventory authorities, order fulfillment/payment lineage authorities, and accounting ledger authority.
- New convergence reasons should be stable enum classifications (`OperationalConvergenceReason`) rather than raw exception messages, payload values, document keys, tenant names, user ids, provider ids, or free-form local strings.
- Metric labels must remain low-cardinality and non-sensitive. Tenant scope may be bucketed; raw tenant ids, payment ids, customer data, order numbers, document ids, and payload fragments must not be metric dimensions.
- A replay-safe no-op is not invisible behavior. If a path intentionally no-ops, suppresses, tombstones, deletes, retries, or fails closed because current authority rejects stale evidence, record a convergence decision at that boundary.
- Rebuild and repair tooling must record both the SQL authority declaration and any fail-closed suppression/delete behavior when a stale rebuild seed cannot prove current visibility. Do not rely on dashboard counts or ad-hoc logs as the only rebuild diagnostic.
- Drift diagnostics are not authority. A drift check may record detection and reconciliation attempts, but any repair must still recompose from SQL and existing lineage/domain authorities before derivative mutation.
- Remaining debt must be explicit when a path is intentionally lightweight. If a new cache warmer, exporter, reporting rebuild, support tool, or global runbook does not use the shared telemetry boundary, document why and add focused tests for its diagnostic behavior.

### 9P. Preserve Storefront Navigation Replay And Rebuild Authority

- Treat `NavigationPublishedEvent`, Mongo navigation documents, Redis navigation/menu/header/footer keys, and cached tenant slugs as derivative evidence only.
- Storefront navigation mutation must recompose from current SQL `StoreConfig` plus active tenant lineage before full `StorefrontNavigationDocument` replacement or Redis invalidation.
- Duplicate navigation replay must collapse before side effects through `ProjectionEventFence` or an equivalent durable fence. The same `EventId` must not be accepted as a fresh Mongo replacement.
- Missing, inactive, moved, or cross-tenant store-config lineage must fail closed by deleting/scrubbing tenant-scoped navigation visibility and invalidating known cache keys rather than preserving stale navigation.
- Full navigation document replacement and Redis navigation cache mutation are dangerous primitives. Use `DerivativeProjectionMutationGuard` with `SqlAuthorityOverlayKind.StorefrontNavigation` or document an equivalent reviewed runtime precondition with source/runtime tests.
- Future navigation, CMS, SEO, support-cache, or storefront operational repair/rebuild tooling must state SQL authority, tenant lineage, replay collapse, fail-closed behavior, and remaining debt in the same change.

### 9Q. Preserve Semantic Convergence Chaos Validation

- Treat replay storms, duplicate delivery, dead-letter replay, delayed callbacks, interrupted rebuilds, repair overlap, stale cache hydration, stale actor sessions, and tenant-lineage drift as expected operational pressure, not rare edge cases.
- New replay/rebuild-sensitive behavior must prove deterministic convergence: apply current SQL truth, collapse duplicate evidence, retry when authority is missing but recoverable, delete/tombstone stale derivative visibility, or fail closed when current authority rejects it.
- Chaos validation should exercise shared runtime boundaries where possible: `ProjectionEventFence`, `SqlTruthRebuildGuard`, `DerivativeProjectionMutationGuard`, `TenantLineageAuthority`, `AdminTenantAuthority`, product/variant lifecycle and inventory authorities, order payment/fulfillment lineage authorities, accounting ledger authority, and `OperationalConvergenceTelemetry`.
- When a path uses an equivalent local SQL-lineage or freshness check instead of a shared helper, document that precisely. Do not claim helper adoption that is not present in source.
- Operational repair and runbook paths must remain tenant-scoped unless they are explicitly platform/global scope. Tenant-admin repair must consume trusted tenant context and current SQL actor authority; global repair must be a reviewed platform primitive.
- Future chaos sweeps must update tests and governance docs with validated guarantees, discovered weaknesses, telemetry findings, and honest remaining debt. Do not hide a weakness simply because the current behavior still converges.

### 9R. Preserve Operational Throughput Controls

- High-volume outbox, replay, rebuild, cache warmer, and reindex workers must expose bounded operational controls instead of hard-coded cadence. At minimum, Tier 1 workers should make batch size, max batches per poll/run, poll interval, inter-batch delay, retry limits, and claim/processing lease configurable where those concepts apply.
- Throughput controls must not weaken SQL authority, tenant scope, aggregate ordering, retry delay, compensation, or replay collapse. Tuning may speed recovery, but it must not skip failed earlier aggregate events or stale authority checks.
- Runtime startup must actually bind throughput options from configuration for operational workers. Adding an options class without passing configuration through the service-registration boundary is governance drift.
- Payment-cancellation recovery throughput controls must remain wrapped around `IPaymentLifecycleService`; do not drain the payment-cancellation outbox by direct payment/order writes.
- Add focused tests when changing worker throughput behavior: bounded batch processing, duplicate replay/completion collapse, retry/dead-letter convergence, cancellation/restart safety, and source governance that prevents fixed timing constants from returning.
- Document production assumptions honestly. Do not claim enterprise throughput SLOs from unit tests; use operational telemetry, backlog age, dependency latency, and staging/prod-like runs for capacity claims.

### 9S. Preserve Operational Recovery Runbooks

- Keep `docs/operations/operational-recovery-runbooks.md` aligned with runtime replay, rebuild, backlog, cache/projection, tenant isolation, payment/accounting/fulfillment, drift, telemetry, escalation, and remaining-debt behavior.
- Keep `docs/architecture/dangerous-operational-primitives.md` aligned with approved low-level primitives: direct SQL/bulk mutation, full Mongo replacement, raw Elasticsearch writes, Redis pattern/manual key mutation, raw bus publishing, global/all-tenant repair, tenant override/support impersonation, and manual financial repair.
- Operator-facing recovery docs must state that PostgreSQL and named authority services decide business truth; MongoDB, Redis, Elasticsearch, queue payloads, dashboards, exports, and repair inputs are evidence only.
- Do not document manual derivative mutation, global repair, tenant override, payment status edit, ledger patch, or outbox/dead-letter suppression as a valid recovery shortcut unless a reviewed authority boundary, platform escalation, runtime guard, and focused tests are added in the same change.
- When runtime behavior changes replay/rebuild semantics, throughput controls, dangerous primitive approval, tenant/global repair scope, or payment/accounting/fulfillment reconciliation, update the runbook and `OperationalRecoveryRunbookArchitectureTests` in the same change.
- Remaining operational debt must stay explicit. Do not imply universal helper adoption for paths that use equivalent local SQL checks or have not been reviewed.

### 9T. Preserve Tenant Isolation Certification Freeze

- Keep `docs/architecture/tenant-isolation-certification-findings.md` aligned with tenant certification findings, verified guarantees, operational confidence, remaining tenant risks, and regression-freeze invariants.
- Tenant-scoped public derivative full replacements must use a domain writer or `DerivativeProjectionMutationGuard` with current SQL authority, freshness, fail-closed behavior, and a named `SqlAuthorityOverlayKind`.
- `CmsPageProjectionConsumer` is now a reviewed storefront CMS derivative boundary. Do not remove its `ProjectionEventFence`, active SQL tenant-lineage check, fail-closed tombstone path, `DerivativeProjectionMutationGuard`, or `SqlAuthorityOverlayKind.StorefrontCms` declarations without replacing them with an equivalent documented boundary and tests.
- `TenantIsolationCertificationFreezeTests` and `CrossSystemAuthorityRegressionGuardTests` are regression-freeze tests. If they fail after tenant/replay/rebuild/derivative changes, treat the failure as a governance defect until reviewed.
- Future tenant-scoped CMS-adjacent, SEO, smart-offer, online-customer, notification, analytics, support-cache, feed/export, reporting rebuild, cache warmer, or global runbook paths must document tenant SQL authority, actor/platform scope, replay collapse, derivative identity, dangerous primitive use, telemetry, and remaining debt in the same change.

### 9U. Preserve Super-Admin Platform Authority Certification Freeze

- Keep `docs/architecture/super-admin-platform-authority-certification-findings.md` aligned with super-admin authority findings, dangerous global primitives, operational escape hatches, auditability, and remaining platform-authority debt.
- Super-admin JWT role/auth-type claims are evidence only. `RequireSuperAdmin` endpoints must pass `SuperAdminPlatformAuthorityMiddleware`, which recomposes current SQL `SuperAdmins`, current `TokenVersion`, and current super-admin governance status.
- Do not add a platform/global endpoint, global cache/search mutation, all-tenant repair, marketplace-wide moderation mutation, tenant override, support impersonation, or emergency primitive without explicit `RequireSuperAdmin` or a reviewed platform runbook boundary.
- Stale super-admin access-token replay after logout, refresh-token replay revocation, password change, account deletion, or governance restriction must fail closed. Do not remove `token_version` from super-admin access tokens without replacing it with an equivalent current-SQL session authority.
- Global/all-tenant operation remains intentionally flexible, but it must name the SQL authority owner, preserve replay/rebuild convergence, record audit/telemetry diagnostics, and document dangerous primitive use and remaining debt.
- `SuperAdminPlatformAuthorityCertificationTests` are regression-freeze tests. If they fail after super-admin, platform, runbook, or dangerous-primitive changes, treat the failure as a governance defect until reviewed.

### 9V. Preserve Marketplace Governance Certification Freeze

- Keep `docs/architecture/marketplace-governance-certification-findings.md` aligned with CommerceHub marketplace governance findings, replay/rebuild guarantees, public visibility ownership semantics, moderation/recommendation governance, and remaining marketplace-risk debt.
- Do not rebuild or serve marketplace product visibility from product SQL alone. Current SQL `CommerceHubListings` plus current tenant/product/variant/category/stock/price/media/publication authority must prove public marketplace visibility.
- Homepage, recommendation, featured, and curated discovery references are trigger/governance evidence only. Public reads, projection rebuilds, cache hydration, and reindex jobs must remove stale curated references when current SQL listing/category/tenant visibility is missing.
- CommerceHub search document identity for tenant-owned listings must remain tenant-scoped. Legacy listing-only ids may be cleaned up, but new raw Elasticsearch writes, reindexers, and repair tools must not reintroduce listing-only or product-only public search ids.
- Duplicate homepage/recommendation replay must collapse before dangerous Mongo/Search/Redis side effects through `ProjectionEventFence` or a documented equivalent. Same-EventId replay is not a fresh rebuild.
- Marketplace moderation and global discovery operations must stay behind current SQL platform authority or documented platform runbook scope, preserve SQL listing ownership, and keep operational diagnostics visible.
- Future review, localization, feed/export, ranking, cache warmer, support, or global marketplace runbook paths must add path-specific SQL authority, replay/rebuild, tenant identity, dangerous primitive, telemetry, tests, and docs in the same change.

### 9W. Preserve Super-Admin Recovery / Rebuild Certification Freeze

- Tenant-admin recovery endpoints must not call all-tenant/global rebuilds, global cache purge, global search alias promotion, or forced dead-letter replay. Use trusted tenant context and tenant-scoped recovery entrypoints.
- Global/all-tenant recovery remains platform/runbook scope and must name the SQL authority owner, platform actor authority, dangerous primitive use, pacing controls, and diagnostics.
- CommerceHub search recovery must declare current SQL `CommerceHubListing` authority in addition to product, variant, category, stock, price, media, tenant, and tombstone authority.
- Rebuild interruption/restart and replay/rebuild overlap are valid only when each run redeclares SQL authority and converges through apply, delete, tombstone, no-op, retry, or fail-closed behavior.
- Dead-letter retry must reset failed rows into the normal ordered processor path. Do not direct-publish payloads, mark failed rows processed, skip aggregate blockers, or suppress poison authority failures.
- High-volume recovery tooling must keep bounded batch size, inter-batch delay, lease/cancellation behavior, and retry limits configurable or explicitly documented as bounded.
- Update `docs/architecture/super-admin-platform-authority-certification-findings.md`, `docs/operations/operational-recovery-runbooks.md`, and recovery governance tests whenever global replay/rebuild/recovery semantics change.

### 9X. Preserve Emergency Governance / Operational Audit Freeze

- Non-read `RequireSuperAdmin` operations must retain request-level operational audit after current SQL platform actor authority. Do not remove `SuperAdminOperationalAuditMiddleware` or move it before `SuperAdminPlatformAuthorityMiddleware`.
- Super-admin audit attribution must use the SQL-recomposed platform actor stored by `SuperAdminPlatformAuthorityMiddleware`; JWT claims remain evidence only.
- Do not add support impersonation, tenant override, emergency repair, global cache/search mutation, global recovery, or dangerous primitive endpoints without current SQL platform/support actor authority, explicit tenant/global scope, reason/reference capture where applicable, durable audit, replay-safe convergence, and docs/tests in the same change.
- Rollback from emergency operations must recompose from SQL or route through lifecycle/reconciliation owners. Do not restore from Mongo, Redis, Elasticsearch, old queue payloads, dashboards, exports, or support-tool snapshots.
- Keep `docs/architecture/emergency-governance-certification-findings.md`, `docs/architecture/dangerous-operational-primitives.md`, and `docs/operations/operational-recovery-runbooks.md` aligned when emergency/support/rollback semantics change.

### 9Y. Preserve Super-Admin Operational Chaos Certification Freeze

- Keep `docs/architecture/super-admin-operational-chaos-certification-findings.md` aligned with replay-storm, rebuild-overlap, marketplace visibility, moderation/review replay, dangerous primitive misuse, emergency authority, operational misuse, remaining debt, and regression-freeze findings.
- Review, moderation, ranking, feed, cache-warmer, recovery, or global runbook paths that mutate CommerceHub search or marketplace public visibility must revalidate current SQL CommerceHub listing authority before indexing. If current SQL authority is missing, delete or tombstone tenant-scoped visibility instead of indexing from Mongo, Redis, Elasticsearch, queue payloads, exports, or curated JSON.
- `MarketplaceProductReviewProjectionService` is now a reviewed moderation/review replay boundary for CommerceHub search sync. Do not remove its `CommerceHubSearchSqlAuthorityOverlay.ApplyAsync` check, tenant-scoped search delete, or tenant-scoped status update filter without replacing them with an equivalent documented boundary and tests.
- Duplicate replay, dead-letter retry, rebuild interruption, and overlapping recovery are valid only when they collapse, reset into normal ordered processing, redeclare SQL authority, or fail closed with telemetry/audit diagnostics.
- Unsafe raw search writes, unsupported derivative rollback, direct processed-row marking, direct publish to bypass the outbox, global repair from tenant-admin scope, and stale emergency-session reuse must remain forbidden shortcuts.
- `SuperAdminOperationalChaosCertificationTests` and `SemanticConvergenceChaosValidationTests` are regression-freeze tests. If they fail after marketplace/review/moderation/recovery/platform changes, treat the failure as a governance defect until reviewed.

### 9Z. Preserve POS Admin Authentication And Session Certification Freeze

- POS access-token role, tenant, and session claims are evidence only. Default POS bearer authentication must retain `PosSessionAuthority` recomposition of current SQL user, role, tenant ownership, current session binding, active tenant lineage, and current vendor governance rejection before accepting a session.
- Do not persist newly issued POS refresh credentials as raw token material. `Users.RefreshToken` is the hash-only current POS session binding, and POS access tokens must retain `PosSecurityConstants.SessionBindingClaim`.
- This applies to every POS issuer, including tenant-owner onboarding and idempotent registration-session restoration; do not add an alternate POS token issuer that omits current SQL session binding.
- Do not remove concurrency protection from POS session rotation. Login, refresh, logout, future password-reset, user-disable, role-change, or administrative session-revocation flows must collapse stale access/refresh reuse rather than silently preserving old authority.
- Legacy POS access tokens that lack `PosSecurityConstants.SessionBindingClaim`, and access tokens bound to an expired current SQL refresh session, must continue to fail closed rather than being accepted until JWT expiry.
- The certified behavior is one current POS session per user. If future requirements introduce multiple devices, session family history, compromise detection, delegated administration, support impersonation, or break-glass identity, define explicit SQL authority, revocation, audit, replay tests, and documentation rather than bypassing this boundary.
- Keep `docs/architecture/admin-authentication-session-certification-findings.md` and `AdminAuthenticationSessionCertificationTests` aligned when POS authentication/session semantics change.

## Mandatory Workflow

### Phase 1: Architecture & Risk Analysis

Required actions:

1. Read the mandatory context files.
2. Identify the authoritative subsystem and source-of-truth owner for the affected behavior.
3. Inspect the existing services, interceptors, outboxes, consumers, hosted workers, and migrations that already own the path.
4. Check `.ai-context/KNOWN_RISKS.md` to separate accepted tradeoffs from new or changed risks.
5. Classify any issue as confirmed risk, theoretical risk, mitigated risk, or accepted tradeoff before recommending structural changes.

Exit criteria:

- authority boundary is clear
- tenant scope is clear
- async and reconciliation boundaries are clear
- existing risk classification is clear

### Phase 2: Implementation

Required actions:

1. Implement inside the correct authority boundary.
2. Preserve backward compatibility unless an explicit breaking change is authorized.
3. Reuse existing services where they already own the concern.
4. Preserve tenant isolation, idempotency, and reconciliation behavior.
5. Keep migrations, startup, config, and runtime assumptions rollout-safe.

Exit criteria:

- no shadow authority was introduced
- no tenant-scope shortcut was introduced
- no lifecycle, outbox, or invariant guard was bypassed without deliberate replacement controls

### Phase 3: Impact Validation

Required actions:

1. Validate compile, tests, and targeted behavior appropriate to the change.
2. Inspect blast radius across APIs, read models, consumers, workers, migrations, and integrations.
3. Validate that host/process deployment and external containerization assumptions still hold.
4. Re-check ordering, idempotency, compensation, and reconciliation effects for async or financial changes.
5. Confirm the change did not turn an accepted tradeoff into an accidental defect.

Exit criteria:

- the changed path still respects architecture truth
- validation covered the relevant tenant and async boundaries
- rollout risk is understood

### Phase 4: Lessons/Risk Update

Required actions:

1. Update `.ai-context/KNOWN_RISKS.md` when the work uncovered a major risk, mitigated a risk, narrowed a risk, or confirmed an accepted tradeoff remains intentional.
2. Record durable lessons in the most local restart document when future work will depend on that context.
3. Avoid duplicate risk entries and avoid re-reporting already accepted tradeoffs.
4. State clearly whether the result changed architecture, reduced risk, preserved an accepted tradeoff, or only improved observability or recovery.

Exit criteria:

- durable documentation matches the new system understanding
- major findings are preserved outside the chat

## Audit Output Standard

Future audit output should:

- lead with confirmed risks
- separate theoretical risks explicitly
- identify accepted tradeoffs without relabeling them as bugs
- cite concrete code or document evidence
- propose fixes that preserve authority, tenancy, idempotency, event consistency, and reconciliation semantics

## Default Decision Rule

If speed conflicts with authority, tenant isolation, ledger integrity, event consistency, or reconciliation safety, choose the safer design and document the tradeoff.

## POS-ADMIN-CERTIFICATION-01 PASS 2 Tenant Scope Freeze

- Tenant-admin endpoints must consume `ITenantContext.RequiredTenantId`; do not read `X-Tenant-Id`, request bodies, query parameters, or UI selections to choose tenant-admin operational scope.
- An endpoint that supports both an external callback credential and an authenticated tenant actor must bind authenticated tenant identity to trusted tenant context before it bypasses the external credential check.
- Tenant-owned category admin paths must bind trusted tenant scope explicitly in SQL for list/detail/create/update/delete and default-category seeding. Do not reintroduce `FindAsync(...)` or a global-database existence check as the scope contract.
- Shared service-layer tenant updates must preserve tenant binding. Reviewed WhatsApp account/template update must require `Id` plus `TenantId`, not primary-key lookup alone.
- Global affiliate taxonomy mutation is platform scope. Do not restore `AdminAffiliateCategoryController` create/update/activate/deactivate to tenant-admin role checks alone.
- Reviewed tenant-admin reporting must not rely on ambient query filters or tenant-keyed caches as the scope contract. `DashboardController` must bind trusted tenant scope explicitly in SQL, and `FinancialReportsController` must consume `ITenantContext.RequiredTenantId` before delegating to ledger-backed report services.
- Reviewed tenant-owned finance and shipping configuration surfaces must not rely on primary-key-only `FindAsync(...)` or ambient query filters as the scope contract. `BanksController`, `ExpenseCategoriesController`, `FinancialAccountsController`, and `ShippingMethodsController` must bind trusted tenant scope explicitly in SQL for list/detail/update/delete/toggle/statement/opening-balance/parent-account/shipping-calculation paths.
- Preserve explicit exceptions honestly: external callbacks may carry target tenant evidence when validated against tenant-owned secrets or authoritative provider lineage, and platform/global work remains behind current platform authority and documented runbook scope.
- Add or extend freeze tests whenever a tenant-admin operational surface, callback authority mode, tenant-scoped rebuild, or derivative mutation path changes.

## POS-ADMIN-CERTIFICATION-01 PASS 3 Inventory / Reservation Freeze

- CommerceHub reservation creation must filter candidate stock rows through `ProductVariantInventoryLineageAuthority.FilterCurrentInventoryStockOwnersAsync(...)` before raw available quantity is accepted, and reviewed multi-row stock locks must stay deterministic by `WarehouseId`/`Id`.
- Marketplace reservation/payment recovery may require current public publication visibility, but fulfillment consumption for an already-authorized order item must use current SQL inventory lineage rather than the reservation-creation publication guard.
- Reservation-backed payment, checkout, fulfillment, rebuild, and return-receipt paths must keep exact current SQL order-item ownership and current stock-owner filtering. Do not reintroduce raw row checks that bypass current warehouse, variant, or stock authority.
- Add or extend runtime and source freeze tests whenever a reviewed CommerceHub reservation, delivery commit, payment recovery, or return-receipt inventory path changes.

## POS-ADMIN-CERTIFICATION-01 PASS 4 Financial Integrity Freeze

- Settlement accounting authority is a current non-deleted `PaymentSettlementBatch` plus current live `PaymentSettlementOrders` lineage; `PaymentSettlementBatch.OrderCount` is recorded metadata only.
- Gateway/COD settlement recording must reject non-positive gross amounts and fees outside `[0, gross]` before settlement persistence, accounting enqueue, or `Order.PaymentStatus` mutation.
- Accounting replay and immediate gateway/COD settlement fast paths must reject missing, soft-deleted, empty-lineage, or recorded-versus-current settlement-order drift before ledger mutation.
- Immediate gateway/COD settlement fast paths must use the same `AccountingLedgerAuthority` settlement evaluation as replay rather than a weaker fast-path-only settlement check.
- Post-settlement refund creation and refund accounting must also revalidate that the refunding order still belongs to the current settlement batch lineage before bank-account refund posting is allowed.
- Storefront payment replay with missing current SQL order/payment lineage must delete tenant-scoped derivative order visibility and invalidate its cache. A replay that conflicts with current SQL payment status may repair current status, but it must not append callback- or payload-derived timeline narrative as current financial truth.
- Do not add settlement repair, import, replay, export, dashboard, or reconciliation tooling that posts from batch headers, provider exports, replay payloads, or historical settlement-order mappings without current SQL settlement authority.
- Add or extend runtime and source freeze tests whenever settlement accounting, settlement recovery, refund settlement context, payment callbacks, or accounting replay behavior changes.

## POS-ADMIN-CERTIFICATION-01 PASS 5 Marketplace / Public Visibility Freeze

- CommerceHub category ids in SQL listings, Mongo read models, Elasticsearch documents, homepage section JSON, recommendation cards, cached suggestions, and repair inputs are evidence only. Public marketplace visibility must validate active SQL CommerceHub category parent lineage through `CommerceHubCategoryLineageAuthority` or a documented equivalent.
- Leaf-category `IsActive` is not enough for certified CommerceHub visibility. Inactive, missing, self-referential, or cyclic parent lineage must fail closed by search delete, read-model delete, curated reference scrub, validation rejection, no-op, or reindex-count exclusion.
- CommerceHub search overlay, product-detail reads, listing projection, marketplace product read-model rebuild, homepage/recommendation projection, public homepage reads, super-admin homepage validation, CommerceHub query suggestions/categories, and reindex verification must stay aligned on the same category lineage semantics.
- CommerceHub browse/search/suggestion/autocomplete request shaping and cached suggestion/autocomplete product revalidation must suppress both governance-hidden vendors and vendors suspended from new orders before public totals, facet inputs, product suggestions, or autocomplete products are returned. Item-only post-filtering is not a sufficient certified public-count or suggestion-authority model.
- CommerceHub public image replay/rebuild/recovery must keep media SQL-authoritative: use current `ProductMedia`/`ProductVariants.MediaId` and clear stale public images instead of restoring legacy product/variant strings or derivative image fields.
- CommerceHub search review decoration must remain moderation SQL-authoritative during overlay mutation: recompose `AverageRating` and `ReviewCount` from current approved, non-deleted `ProductReviews`, never from Mongo/search review summaries.
- Future marketplace ranking, feed/export, localization, support, review-export, analytics/reporting, cache-warmer, or global script paths must add SQL listing/category/media/moderation recomposition, replay/rebuild behavior, tenant-safe aggregation checks, dangerous primitive governance, tests, and docs before public visibility mutation.

## POS-ADMIN-CERTIFICATION-01 PASS 6 Operational Tooling Freeze

- Tenant-admin storefront product search repair must recompose visibility through `ProductSearchSqlAuthorityOverlay` before Elasticsearch mutation. Do not rebuild search visibility from partial SQL rows, legacy product price fields, stale repair snapshots, or derivative documents.
- Operational search repair documents must be built from `ProductSearchDocumentMapper.MapFromProductDocument(...)` after SQL overlay recomposition so unit-authoritative storefront price, stock, promotion, category, media, and publication visibility stay aligned with certified runtime search recovery.
- Missing or invalid current SQL search authority must delete/tombstone search visibility rather than index a “baseline” product document.
- `AdminDataSyncController` category and shipping-method sync must declare SQL rebuild authority and delete tenant-scoped stale Mongo documents that are missing from current SQL authority. One-way Mongo sync is not an approved operational rebuild shortcut.
- `AdminSearchSyncController` category search sync must declare SQL rebuild authority and delete tenant-scoped stale Elasticsearch category documents that are missing from current SQL authority. One-way category search sync is not an approved operational rebuild shortcut.
- Tenant-admin product read-model repair must keep cache invalidation behind `IProductCacheService`. Do not reintroduce direct Redis server enumeration or `KeyDeleteAsync` calls in `AdminSyncController` after SQL-backed projection rebuild.
- Reviewed affiliate repair/reindex must not inject `USD` or any other fallback currency when current `Tenant.Currency` authority is blank or invalid. AF16E recovery must normalize currency through `CurrencyNormalizationHelper.NormalizeCurrencyOrThrow(...)` and declare SQL repair authority before raw Mongo or Elasticsearch mutation.
- Missing current tenant storefront metadata or currency authority must delete stale affiliate visibility rather than preserve or rewrite derivative state. Do not skip AF16E repair while leaving stale public affiliate or affiliate-intelligence documents live.
- If a future operational search/cache/CMS/SEO/export/support rebuild path cannot use the shared overlay directly, document the equivalent SQL authority boundary and add runtime plus source-freeze tests in the same change.

## POS-ADMIN-CERTIFICATION-01 PASS 7 Replay / Rebuild / Chaos Freeze

- Payment cancellation outbox recovery must keep `ClaimedUntilUtc` row leasing before calling `IPaymentLifecycleService`. Do not remove active worker-overlap collapse because the lifecycle service is idempotent.
- Payment cancellation overlap telemetry must classify both visible active claims and row-version claim races as the same audited overlap condition. Do not leave one collapse branch as log-only behavior.
- Marketplace projection outbox recovery must keep row-versioned claim collapse plus duplicate-completion fences. Do not treat `ClaimedUntilUtc` alone as sufficient overlap protection.
- Marketplace projection duplicate-completion cleanup must also emit convergence telemetry when the processed-event unique key proves completion after dispatch. Do not leave the post-dispatch duplicate-completion race as log-only behavior.
- `PaymentCancellationOutboxOptions.ProcessingLeaseSeconds` is the bounded crash-recovery control for this path. Do not replace it with hard-coded leases, unbounded parallelism, or direct payment status mutation.
- Payment cancellation leases must clear on processed, manual-review, and retry/failure outcomes. A crashed worker should resume only after lease expiry through the same SQL/lifecycle authority path.
- Storefront footer replay must revalidate current SQL tenant lineage before full Mongo replacement. Missing, inactive, or deleted tenant authority must delete tenant-scoped footer visibility with convergence diagnostics instead of preserving stale public footer documents.
- `RedisInvalidationConsumer` must keep `ProjectionEventFence` around homepage, CMS, footer, marketplace product/order, and marketplace review cache invalidation. Duplicate replay must collapse before Redis key deletion or pattern invalidation fanout; cache invalidation remains derivative infrastructure, not authority.
- Replay/rebuild/recovery certification findings must remain documented in `docs/architecture/replay-rebuild-chaos-certification-findings.md` whenever worker-restart, dead-letter retry, outbox claim, rebuild overlap, or cache/search recovery semantics change.
- Future replay/rebuild/cache/export/support recovery workers must document worker restart semantics, replay amplification bounds, fail-closed outcomes, telemetry, and regression tests in the same change.

## POS-ADMIN-CERTIFICATION-01 PASS 8 Operational Survivability Freeze

- Reviewed operational meters must stay exported through the process OpenTelemetry pipeline. Do not add or retain a reviewed runtime meter and leave it disconnected from production scraping/export.
- Reviewed exported tenant- and order-oriented operational metrics must use bounded dimensions. `tenant_bucket` is the approved PASS 8 exported tenant dimension, and bounded presence flags are the approved reviewed order-correlation dimension unless a newer reviewed rule replaces them.
- Free-form metric labels on reviewed operational meters must be normalized before emission. Do not emit raw tenant ids, raw order ids, queue payload values, document keys, exception text, customer identifiers, payment ids, or other unbounded values as labels.
- Reviewed exported `CommerceReliabilityTelemetry` provider, endpoint, surface, carrier, validation-method, projection, storage-provider, stock-sync, and isolation-level labels are part of that same freeze. Normalize them at the telemetry boundary; do not rely on callers to keep those labels bounded.
- Reviewed accounting telemetry is part of that same freeze. `PointOfSale.Accounting` must stay exported with bounded `tenant_bucket` plus normalized operation/gateway/error labels, and its singleton fallback counters must remain bounded by the same reviewed dimensions rather than raw tenant/provider/error keys.
- Reviewed Redis cache performance telemetry is part of that same freeze. `PointOfSale.Redis.Performance` must stay exported with bounded `cache_type` and `pattern_family` dimensions, and it must not emit raw Redis keys, raw invalidation patterns, tenant ids, product ids, order ids, or payload fragments as metric labels.
- `CommerceHubReindexOptions` must remain configuration-driven, bounded, and startup-validated. Do not revert the reviewed recovery pacing path to unchecked configuration or hard-coded incident values.
- Payment-cancellation survivability is also part of the PASS 8 readiness boundary. `payment_cancellation_outbox_readiness` must remain visible for backlog age, repeated failure, terminal failure, active claim, and lease diagnostics; do not remove that readiness surface or replace it with log-only incident evidence.
- Deployment/restart and backup/restore assumptions are also part of the PASS 8 governance boundary. Keep restart overlap convergence on the existing lease/fence/readiness model, and keep PostgreSQL as the only reviewed restore authority while derivative stores are rebuilt or reconciled from SQL-first entrypoints.

## POS-ADMIN-CERTIFICATION-01 PASS 6 Service-Marketplace Recovery Freeze

- Legacy service-marketplace search recovery must reload current SQL service/tenant/provider-profile authority before raw Elasticsearch mutation. Do not restore `marketplace_services` visibility from service event payloads, stale search documents, or raw active service ids alone.
- `ServiceDiscoveryProjectionConsumer` and `ElasticsearchDriftReconciliationJob` must require active non-deleted service rows, active non-deleted tenants, and active non-deleted same-tenant provider profiles before preserving service search visibility.
- Missing or invalid service-search authority must delete or leave absent stale documents. Future service ranking, localization, feed/export, support, cache-warmer, profile-governance, or global script paths need path-specific certification before public service-search mutation.

## POS-ADMIN-CERTIFICATION-01 PASS 9.1 CMS Public Visibility Freeze

- Storefront CMS public visibility must be derived from current SQL `CmsPages` plus active tenant lineage. Redis CMS caches, Mongo `StorefrontPageDocument`, Elasticsearch `cmspages`, sitemap cache entries, SEO metadata, route caches, and replayed publish/delete events are not visibility authority.
- Public CMS reads must not return cache or projection data unless current SQL still proves same-tenant, active-tenant, non-deleted, published, non-draft page authority with `PublishedAt != null`.
- CMS SEO metadata, sitemap URLs, slug resolution, and unified-search CMS results must revalidate current SQL publication authority before returning public visibility. Deleted or unpublished CMS pages must not survive through SEO or search caches.
- Tenant-admin CMS search repair must declare `SqlAuthorityOverlayKind.StorefrontCms`, index only current published SQL pages, and delete tenant-scoped stale CMS search documents missing from current SQL authority.
- If scheduled CMS publication, CMS archive/approve/reject flows, CMS cache warmers, CDN purges, feed/export writers, localization, support tooling, or global scripts are added, they must add current SQL publication checks, tenant scope, replay/rebuild behavior, fail-closed tests, and documentation in the same change.

## POS-ADMIN-CERTIFICATION-01 PASS 9.6 Storefront Recovery / Rebuild Freeze

- Recovery, rebuild, repair, reindex, replay, cache rehydration, and startup bootstrap paths must treat MongoDB, Redis, Elasticsearch, replay payloads, and repair snapshots as candidate evidence only. Current PostgreSQL authority decides existence, publication, lineage, media, tenant, and storefront eligibility.
- Product projection rebuild and product search repair must continue routing through `IProductSqlAuthorityProjectionWriter` or `ProductSearchSqlAuthorityOverlay` before Mongo/search mutation. Missing SQL authority must tombstone/delete, not rebuild a baseline public document.
- CommerceHub reindex, catch-up, startup auto-healing, background auto-healing, and search analytics recovery must use `CommerceHubSearchSqlAuthorityOverlay` before indexing and must delete tenant-scoped search documents when SQL listing/product/category/vendor authority is gone.
- Tenant recovery identity must include tenant scope where available. Do not regress CommerceHub auto-healing to listing-id-only in-flight dedupe, active processing, startup grouping, or Mongo document lookup.
- Cache recovery must stay invalidation-first or SQL-read repopulated. Do not restore public product/category/CMS/listing visibility from Redis values, cache snapshots, cache warmer payloads, or replayed invalidation state.
- New feed/export/CDN purge/support/global-script/cache-warmer recovery paths require path-specific SQL authority checks, tenant scope, stale-delete behavior, replay/rebuild tests, and documentation before public visibility mutation.

## POS-ADMIN-CERTIFICATION-01 PASS 10.1 Event Propagation / Projection Freshness Freeze

- SQL commits are the only authoritative business-truth boundary. Event payloads, queue state, MongoDB, Redis, Elasticsearch, CommerceHub projections, homepage/discovery projections, and search projections are convergence evidence only.
- Authoritative integration events must continue through `IEventPublisher` and the MassTransit EF outbox unless an existing specialized SQL outbox is the reviewed owner for that flow. Do not introduce raw MassTransit publishing for business-truth events outside reviewed outbox infrastructure.
- New or changed outbox processors must expose retry, claim/lease or equivalent active-processing collapse, backlog/lag visibility, poison or terminal-failure behavior, and replay/catch-up documentation.
- New or changed projection consumers must be idempotent, stale-version resistant, and observable. Duplicate delivery must collapse before side effects when a durable fence exists, and out-of-order delivery must converge to current SQL truth rather than payload order.
- Projection lag must remain observable through readiness checks or exported bounded telemetry. Reviewed projection and MassTransit outbox readiness checks must keep `OperationalConvergenceTelemetry.RecordDriftDiagnostic(...)` emissions for lag, healthy tolerance, and health-check failure.
- Future feed/export/CDN purge/cache-warmer/support/reporting/analytics/global-script propagation paths require path-specific SQL authority, freshness markers, lag detection, replay/rebuild tests, and certification documentation before they are treated as convergence infrastructure.

## POS-ADMIN-CERTIFICATION-01 PASS 10.2 Redis Cache Freshness Freeze

- Redis is never public visibility authority. Product, category, CMS, homepage, search, SEO, footer, affiliate, CommerceHub, feed/export, and future recommendation cache reads must filter or recompose through current SQL authority before public return or cache population.
- Cache invalidation remains infrastructure fanout. Product and reviewed Redis invalidation consumers must keep durable duplicate/active replay collapse before pattern scans or key deletion when a fence is available.
- Cache warmers may only warm from reviewed read services or current SQL-authoritative recomposition paths. Do not add warmers that hydrate product/category/CMS/listing visibility from MongoDB, Elasticsearch, Redis snapshots, event payloads, or partial SQL rows without current SQL authority checks.
- Redis restart, loss, or failover must be treated as cache miss/repopulation, not recovery truth. Do not restore public visibility from Redis values, cache snapshots, warmer payloads, or invalidation payloads.
- Redis freshness observability must stay bounded. Reviewed metrics may use normalized `cache_type` and `pattern_family`, but must not emit raw keys, tenant ids, product ids, order ids, invalidation patterns, payload fragments, or exception text as metric labels.
- Marketplace CMS/footer and storefront footer cached reads must keep current SQL CMS publication filtering before public return. Do not reintroduce cached CMS scope/footer DTOs as final publication authority.
- Future public visibility cache, search/SEO cache, cache warmer, cache rebuild, CDN purge, support-cache, reporting-cache, or global-script cache changes must add path-specific SQL authority checks, replay/rebuild behavior, freshness telemetry, regression tests, and certification documentation in the same change.

## POS-ADMIN-CERTIFICATION-01 PASS 10.3 Mongo Projection Convergence Freeze

- MongoDB is a derivative read model. Do not use Mongo projections as authority for publication, visibility, category lineage, media ownership, simple-product units/pricing, variant lifecycle, stock, CMS publication, homepage eligibility, CommerceHub listing visibility, affiliate policy/pricing, orders, financial state, or tenant lineage.
- Storefront product Mongo writes on reviewed create/update/lifecycle/rebuild/repair paths must continue routing through `IProductSqlAuthorityProjectionWriter` and `IStorefrontProductProjectionMutationBoundary`. Missing SQL authority must tombstone/delete/suppress rather than rebuild a baseline product document.
- Category, CMS, homepage, CommerceHub, affiliate, and marketplace Mongo rebuild/repair paths that touch public visibility must derive final mutation from current SQL authority, retain tenant scope where applicable, include freshness markers, and delete/tombstone stale projections missing from SQL.
- Mongo projection consumers must remain idempotent and stale-event resistant through `ProjectionEventFence`, `ProjectionConsumerEventFence`, processed-event receipts, duplicate-key collapse, event timestamps, event versions, mutation versions, tombstones, or an equivalent reviewed freshness guard.
- Projection lag and drift must stay observable through readiness checks, bounded convergence telemetry, document-level freshness metadata, processed-event receipts, search-sync metadata, or path-specific diagnostics. Do not add log-only public-visibility projection recovery.
- Mongo rebuild, repair, startup recovery, cache warmer, feed/export, support, analytics, reporting, or global-script changes that touch public visibility require path-specific SQL authority checks, stale-delete/tombstone behavior, replay/chaos tests, and certification documentation in the same change.

## POS-ADMIN-CERTIFICATION-01 PASS 10.4 Elasticsearch Freshness / Search Convergence Freeze

- Elasticsearch is a derivative search/discovery read model. Do not use Elasticsearch documents, aliases, autocomplete fields, suggestions, popular terms, facets, counts, aggregations, or search analytics as authority for product, category, CMS, CommerceHub listing, affiliate, service, publication, pricing, stock, media, tenant, or visibility truth.
- Product/category/CMS/CommerceHub/affiliate/service search mutation, reindex, repair, auto-healing, and drift-reconciliation paths must recompose current SQL authority before indexing and must delete/suppress stale search documents when SQL authority is missing.
- CommerceHub search reindex and alias promotion must stay SQL-count verified. Do not promote a target index from raw Mongo or Elasticsearch counts when current SQL-visible listing authority disagrees.
- Tenant-admin search repair must remain tenant-scoped and SQL-first. Do not reintroduce global index deletion/recreation, listing-id-only recovery identity where tenant evidence exists, additive-only repair, or stale repair snapshots as public search visibility.
- Suggestions, autocomplete, zero-result assistance, popular terms, facets, and counts must remain candidate/read surfaces. Reviewed public responses must filter or bound them by current SQL-visible returned products/listings/categories/CMS pages; raw Elasticsearch aggregations are not count authority.
- Search freshness and drift must stay observable through document timestamps, search sync metadata, health checks, `SearchTelemetry`, auto-healing metrics, or bounded `OperationalConvergenceTelemetry`. Do not add log-only search recovery or unbounded metric dimensions.
- Future Elasticsearch-backed feed/export/CDN purge/support/global-script/reporting/search-analytics/recommendation paths that touch public visibility require path-specific SQL authority checks, stale-delete behavior, drift/freshness telemetry, replay/chaos tests, and certification documentation in the same change.

## POS-ADMIN-CERTIFICATION-01 PASS 10.5 Cross-System Convergence Freeze

- Whole-system convergence remains PostgreSQL-authoritative and eventually consistent. Do not claim MongoDB, Redis, Elasticsearch, RabbitMQ, event payloads, repair snapshots, cache warmers, startup state, or search aliases are business truth.
- New or changed convergence paths must map the SQL source, Mongo projection, Redis cache behavior, and Elasticsearch/search document impact before they are treated as certified public-visibility infrastructure.
- Product, category, CMS, homepage/discovery, CommerceHub, affiliate, service, recommendation, search, repair, replay, startup, feed/export, support, reporting, analytics, and global-script paths that touch public visibility must prove current SQL authority before public return or derivative mutation.
- Cross-system drift must be bounded, observable, and recoverable. Add or preserve freshness markers, processed-event receipts, replay fences, stale-version checks, stale-delete/tombstone behavior, cache invalidation/TTL, SQL-first repair/reindex, health checks, and bounded convergence telemetry as appropriate for the path.
- Repair, replay, and startup recovery must converge all affected derivative systems toward SQL truth. Subsystem-only recovery that leaves another reviewed public system permanently stale is not an accepted PASS 10.5 outcome.
- Do not add log-only cross-system recovery. If a path can drift across MongoDB, Redis, or Elasticsearch, expose diagnostics through readiness checks, telemetry, freshness metadata, repair reports, or documented operator runbooks.
- Future whole-system convergence changes must update `CrossSystemConvergenceCertificationTests`, `docs/architecture/cross-system-convergence-certification-findings.md`, and the `.ai-context` PASS 10.5 entries in the same change.

## POS-ADMIN-CERTIFICATION-01 PASS 10.6 Chaos, Lag & Operational Survivability Freeze

- Operational survivability is degradation and recovery, not zero downtime. Do not claim derivative stores are strongly consistent or promote MongoDB, Redis, Elasticsearch, RabbitMQ, MassTransit, replay payloads, repair snapshots, cache entries, search aliases, or startup state to business truth during outages.
- New or changed outage, lag, retry, replay, rebuild, repair, reindex, cache, startup, support, reporting, analytics, feed/export, or global-script paths that touch public visibility must document blast radius, fallback behavior, recovery mechanism, tenant scope, observability, SLO threshold, and remaining eventual behavior.
- MongoDB failure must remain projection/read-model failure. Public visibility paths must either recompose current SQL authority, fail safely, or recover by SQL-first rebuild/repair; stale Mongo projections must not be promoted during recovery.
- Redis failure must remain cache/coordination degradation. Cache recovery must be invalidation, TTL expiry, or SQL-read repopulation; Redis values, warmup payloads, snapshots, and invalidation payloads are not recovery truth.
- Elasticsearch failure must remain search degradation. Reindex, alias promotion, drift repair, autocomplete, suggestions, facets, and counts must stay SQL-filtered or SQL-verified on reviewed public paths.
- Consumer and outbox failure must stay observable through readiness, telemetry, leases/claims, processed-event receipts, retry diagnostics, or equivalent operator signals. Do not add silent or log-only critical propagation paths.
- Replay storms and worker restarts must collapse duplicates or stale work before derivative side effects when a durable fence, processed-event receipt, idempotency key, claim lease, or version guard is available.
- Recovery, repair, and startup paths must preserve tenant boundaries through trusted tenant context, affected-tenant contracts, tenant-scoped derivative keys, active tenant lineage, and tenant-scoped recovery identity where tenant evidence exists.
- Future operational survivability changes must update `ChaosLagOperationalSurvivabilityCertificationTests`, `docs/architecture/chaos-lag-operational-survivability-certification-findings.md`, and the `.ai-context` PASS 10.6 entries in the same change.

## POS-ADMIN-CERTIFICATION-01 PASS 11.1 Super Admin Identity / Session Freeze

- Super-admin authority must remain SQL-authoritative: current `SuperAdmins`, current `UserGovernanceProfiles`, and current `TokenVersion` decide platform authority.
- Do not treat super-admin JWT claims, cookies, refresh credentials, Redis state, session cache state, or raw role strings as platform authority.
- `SuperAdminPlatformAuthorityMiddleware` must guard every non-anonymous `RequireSuperAdmin` endpoint and must continue to honor `[AllowAnonymous]` auth endpoints.
- Login and refresh must recheck current governance login eligibility. Logout, password rotation, governance restriction, token-version mismatch, and account deletion must invalidate stale access/refresh authority.
- Legacy tenant-admin routes must not branch on `SuperAdmin` role strings to perform global or platform operations. Add or use a `RequireSuperAdmin` endpoint for platform work.
- Future platform-operator auth, session, refresh, logout, password, governance, middleware, audit, or legacy endpoint changes must update `SuperAdminIdentitySessionSecurityCertificationTests`, `docs/architecture/super-admin-identity-session-security-certification-findings.md`, and the `.ai-context` PASS 11.1 entries in the same change.

## POS-ADMIN-CERTIFICATION-01 PASS 11.2 Global Scope / Tenant Boundary Freeze

- Every new or changed global, tenant-scoped, hybrid, marketplace, recovery, replay, repair, reindex, diagnostics, reporting, support, or global-script path must declare intended scope, authority owner, enforcement owner, and observability.
- Global operations must use explicit platform authority through `RequireSuperAdmin` and current SQL platform actor validation. Do not let tenant role strings, tenant JWT claims, inherited tenant context, or operational convenience create global authority.
- Tenant operations must require trusted tenant context plus current SQL tenant actor authority and active tenant lineage. Do not let tenant routes invoke global search/index/recovery behavior; block, fail closed, or add a separate platform route.
- Hybrid marketplace operations must state whether they are intentionally global, tenant-filtered, or tenant-owned. Derivative search/Mongo/Redis candidates must not decide scope.
- Replay, recovery, repair, reindex, and startup paths must preserve tenant scope through tenant-scoped derivative keys, affected-tenant contracts, active tenant lineage, SQL overlays, fences, or equivalent reviewed controls.
- Future scope-governance changes must update `GlobalScopeTenantBoundaryCertificationTests`, `docs/architecture/global-scope-tenant-boundary-certification-findings.md`, and the `.ai-context` PASS 11.2 entries in the same change.

## POS-ADMIN-CERTIFICATION-01 PASS 11.3 Marketplace Governance Authority Freeze

- Marketplace governance is not marketplace visibility authority. Listing approval, rejection, suspension, vendor governance, homepage curation, recommendations, discovery boosts, featured/promoted placement, campaigns, reviews, popularity, and search analytics may only influence candidates, ordering, or governance state.
- Current SQL listing/product/vendor/category/tenant authority decides public marketplace visibility. Do not let homepage JSON, recommendation output, MongoDB, Redis, Elasticsearch, replay payloads, recovery snapshots, popularity metadata, or curated references resurrect deleted, unpublished, hidden, suspended, invalid-category, inactive-tenant, or invalid-variant content.
- Tenant-admin CommerceHub publish/update paths must preserve current vendor governance, live CommerceHub category lineage, product publication/online visibility, and public variant lineage checks before listing approval or update.
- Vendor-governance reactivation must not blindly restore `RestorePublicationOnActivate`. Restore listing publication only while current SQL tenant/product/variant/category authority still accepts it; otherwise clear stale restore state and converge search/homepage/read models through existing overlays.
- Homepage, recommendation, discovery, featured, promoted, search-suggestion, popular-term, replay, reindex, recovery, cache-warmer, feed/export, support, analytics, localization, review-export, ranking, or global-script changes that touch marketplace visibility must add path-specific SQL authority checks, auditability, replay/recovery behavior, regression tests, docs, and `.ai-context` PASS 11.3 updates in the same change.

## POS-ADMIN-CERTIFICATION-01 PASS 11.4 Global Search / Reindex / Recovery Freeze

- Operational tooling is not authority. Reindex, replay, repair, recovery, auto-healing, startup recovery, cache rebuild, and projection rebuild may converge derivative state only from current SQL authority.
- Global and CommerceHub search reindex must remain SQL-derived and stale-delete capable. Do not introduce Mongo-derived, Elasticsearch-derived, additive-only, or alias-promotion paths that can preserve documents absent from current SQL-visible authority.
- Tenant operational tooling must stay tenant-scoped. Tenant-admin repair/reindex/cache/sync tools must use trusted tenant context, tenant filters, tenant-scoped derivative keys, and SQL overlays before derivative mutation.
- Recovery and replay payloads must remain evidence only. Current SQL authority decides whether recovery applies, deletes, tombstones, suppresses, retries, routes to manual review, or fails closed.
- Search repair, CommerceHub auto-healing, startup recovery, and future feed/export/cache-warmer/support/reporting/search-analytics/global-script tooling that touch public visibility must declare authority owner, execution owner, enforcement owner, scope, auditability, stale-delete behavior, chaos/replay behavior, docs, and regression tests in the same change.

## POS-ADMIN-CERTIFICATION-01 PASS 11.5 Emergency Operations / Break-Glass Freeze

- Emergency authority is operational authority only. It may stop, suspend, freeze, isolate, recover, repair, reindex, replay, shut down, or diagnose; it must never create visibility, publication, tenant, governance, financial, inventory, or SQL authority.
- Break-glass operations must be explicit, scoped, auditable, revocable, and SQL-authoritative. Do not add silent operator bypass, support impersonation, tenant override, stale-session elevation, role-string platform authority, or undocumented emergency mutation paths.
- Emergency recovery must recompose current SQL authority before derivative mutation. MongoDB, Redis, Elasticsearch, queue payloads, repair snapshots, provider/carrier evidence, dashboards, exports, and startup state are discovery evidence only.
- Emergency marketplace actions may hide/suspend/suppress/unpublish/scrub but may not resurrect listings, vendors, homepage entries, recommendations, search documents, or publication intent after current SQL listing/product/vendor/category/tenant/variant authority is gone.
- Emergency tenant-scoped work must remain tenant-scoped through trusted tenant context, current SQL tenant actor/session authority, active tenant lineage, tenant filters, and tenant-scoped derivative keys. Global emergency work must use current SQL platform authority and request-level operational audit.
- Future emergency, break-glass, incident-response, shutdown, global repair, recovery, replay, reindex, support, cache-warmer, feed/export, reporting, analytics, global-script, or dangerous-primitive work must update `EmergencyGovernanceCertificationTests`, `docs/architecture/emergency-governance-certification-findings.md`, relevant runbooks, and `.ai-context` PASS 11.5 entries in the same change.

## POS-ADMIN-CERTIFICATION-01 PASS 12.1 Merchant Publication Intent Authority Freeze

- Publication intent is not visibility authority. Do not let tenant publish, republish, update, unpublish, clone, bulk publish, bulk unpublish, replay, recovery, reindex, feed/export, support, ranking, or cache-warmer paths create marketplace visibility without current SQL listing/product/variant/category/tenant/vendor authority.
- Publish, republish, and update must remain tenant-scoped and governance-controlled. Preserve current vendor governance, live CommerceHub category lineage, active shipping, product publication/online visibility, and public variant lineage checks before setting or preserving `CommerceHubListing.IsPublished`.
- Unpublish is tenant-owned withdrawal of intent and may only reduce visibility. Do not block removal of publication intent merely because marketplace governance blocks new listing management.
- Restore intent such as `RestorePublicationOnActivate` is conditional evidence only. Restore it only while current SQL tenant/product/variant/category authority accepts the listing; otherwise clear it and converge derivative systems through existing overlays.
- Search, discovery, homepage, projections, replay, recovery, reindex, and auto-healing must use SQL overlays, tenant-scoped identities where available, stale-delete/tombstone behavior, and replay fences before derivative mutation.
- Future publication-intent changes must update `PublicationIntentAuthorityCertificationTests`, `docs/architecture/publication-intent-authority-certification-findings.md`, and `.ai-context` PASS 12.1 entries in the same change.

## POS-ADMIN-CERTIFICATION-01 PASS 12.2 Marketplace Category Mapping & Taxonomy Authority Freeze

- CommerceHub category mapping is classification authority only. Do not let category selection, category mappings, category filters, attributes, homepage categories, suggestions, autocomplete, search documents, Mongo projections, Redis caches, analytics, replay, recovery, reindex, feed/export, support, ranking, or cache-warmer paths create or preserve marketplace visibility without current SQL listing/product/variant/category/tenant/vendor authority.
- SQL `CommerceHubCategories` owns marketplace taxonomy lineage. SQL `CommerceHubCategoryAttributes` owns category-bound attribute schema. SQL `CommerceHubListings.CommerceHubCategoryId` owns listing classification.
- Category pickers and category attribute endpoints must filter through `CommerceHubCategoryLineageAuthority` and fail closed for inactive, missing-parent, self-referential, cyclic, orphaned, deleted, or invalid categories.
- Publish/update, search overlays, homepage/discovery sanitation, suggestions/autocomplete, reindex, health, and auto-healing must preserve live CommerceHub category lineage checks before public return or derivative mutation.
- Attribute validation must stay category-lineage-bound. Stale category attributes, unknown attributes, stored listing schema snapshots, or Elasticsearch attribute fields must not become taxonomy or visibility authority.
- Future marketplace taxonomy, category mapping, attribute schema, filter generation, homepage category, suggestion/autocomplete, recovery, reindex, cache-warmer, feed/export, support, analytics, localization, ranking, or global-script changes must update `MarketplaceCategoryTaxonomyCertificationTests`, `docs/architecture/marketplace-category-taxonomy-certification-findings.md`, and `.ai-context` PASS 12.2 entries in the same change.

## POS-ADMIN-CERTIFICATION-01 PASS 12.3 Marketplace Attribute & Filter Governance Freeze

- CommerceHub attributes are category-bound classification/search metadata only. Do not let attributes publish, approve, create visibility, preserve visibility, or bypass marketplace governance.
- SQL `CommerceHubCategoryAttributes` owns current attribute schemas. Listing `AttributesJson`, `AttributeSchemaSnapshotJson`, Mongo attributes, Elasticsearch attributes, filter params, facets, autocomplete metadata, analytics, and replay/recovery payloads are derivative evidence only.
- Merchant-provided attributes must validate against live SQL category lineage and current SQL attribute definitions. Unknown keys, unsupported values, deleted attributes, invalid category lineage, and category/attribute mismatches must fail closed.
- Public search filters and facet generation must derive from current SQL filterable attributes on live category lineage. Do not allow arbitrary `attributes.*` filter keys to query MongoDB or Elasticsearch.
- Projection, indexing, replay, recovery, and reindex paths must strip stored attributes absent from the current SQL category schema before derivative mutation.
- Future attribute schema, merchant input, filter/facet, search metadata, indexing, replay, recovery, feed/export, cache-warmer, support, analytics, ranking, localization, or global-script changes must update `MarketplaceAttributeFilterGovernanceCertificationTests`, `docs/architecture/marketplace-attribute-filter-governance-certification-findings.md`, and `.ai-context` PASS 12.3 entries in the same change.

## POS-ADMIN-CERTIFICATION-01 PASS 12.4 Marketplace Pricing Authority Freeze

- CommerceHub final item price is current SQL product variant pricing only: positive `ProductVariant.OnlinePrice` when present, otherwise `ProductVariant.Price`. Do not let merchant listing payloads, `CommerceHubPrice`, MongoDB, Elasticsearch, Redis, client snapshots, affiliate projections, or promotion overlays become final item price authority.
- Marketplace price overrides remain disabled. Keep `CommerceHubListings.CommerceHubPrice` rejected, cleared before persistence, SQL-constrained to null, and stripped from derivative `CommerceHubPriceOverride` projections unless a future authority design is explicitly certified.
- Cart, checkout, projection, search, discovery, detail hydration, reindex, recovery, replay, and startup rebuild paths that serve or mutate CommerceHub prices must recompose or validate current SQL listing/product/variant/category/tenant/stock/media/pricing authority before returning or indexing prices.
- Storefront promotions are not CommerceHub item price authority. CommerceHub promo-code discounts may affect modeled checkout/commission totals, but must not mutate listing item price authority or search price authority.
- Price and promotion replay paths must complete durable projection fences after successful apply, SQL-authoritative no-op, or fail-closed decisions so stale or duplicate events cannot repeatedly restore derivative price state.
- Future marketplace pricing, override, promotion, cart, checkout, projection, search, discovery, affiliate, recovery, reindex, cache-warmer, feed/export, support, analytics, ranking, localization, or global-script changes must update `MarketplacePricingAuthorityCertificationTests`, `docs/architecture/marketplace-pricing-authority-certification-findings.md`, and `.ai-context` PASS 12.4 entries in the same change.

## POS-ADMIN-CERTIFICATION-01 PASS 12.5 Variant & Inventory Marketplace Certification Freeze

- Variant authority is current SQL `ProductVariants`, semantic lifecycle ownership, SKU/barcode ownership, variant attributes, and same-tenant parent product lineage. Do not let CommerceHub listing documents, MongoDB, Elasticsearch, Redis, cart snapshots, event payloads, import/clone/replay payloads, or cache state decide which variant is current, purchasable, or visible.
- Inventory authority is current SQL `ProductVariantStocks`, active `Warehouses`, `InventoryReservations`, and `StockMovements` filtered through `ProductVariantInventoryLineageAuthority`. Do not sum raw stock rows, raw reservation rows, or derivative stock fields as public availability without current stock-owner and reservation lineage.
- CommerceHub cart, checkout, payment recovery, fulfillment, return receipt, projection, search, discovery, detail hydration, reindex, recovery, replay, and startup rebuild paths must recompose current SQL variant lifecycle and current inventory lineage before accepting stock, preserving availability, or mutating derivative visibility.
- Deleted, inactive, detached, superseded, replaced, unpublished, offline, invalid-warehouse, stale-stock-owner, expired-reservation, released, orphaned, or cross-tenant rows must fail closed. Recovery may discover candidates from derivative systems, but current SQL decides apply, suppress, delete, tombstone, no-op, reconcile, or block.
- Future marketplace variant, inventory, stock, availability, cart, checkout, projection, search, discovery, affiliate, recovery, reindex, cache-warmer, feed/export, support, analytics, ranking, localization, or global-script changes must update `MarketplaceVariantInventoryCertificationTests`, `docs/architecture/marketplace-variant-inventory-certification-findings.md`, and `.ai-context` PASS 12.5 entries in the same change.

## POS-ADMIN-CERTIFICATION-01 PASS 12.6 Merchant vs Marketplace Governance Precedence Certification Freeze

- Merchant authority is request authority only. Marketplace governance is decision authority. Current SQL governance wins when merchant publication intent conflicts with suppression, suspension, ban, category/product removal, tenant disablement, replay, recovery, reindex, search, or discovery.
- Publish and update must remain tenant-scoped and governance-controlled. They must not create or preserve visibility without current vendor governance, live category lineage, product publication/online visibility, public variant lineage, and existing commercial/inventory authority.
- Unpublish may only reduce visibility and must not be blocked merely because governance blocks new listing management.
- Governance suppression and vendor suspension/ban must not be removed by merchant update, stale `RestorePublicationOnActivate`, stale `IsPublished`, MongoDB, Redis, Elasticsearch, homepage/discovery references, replay payloads, auto-healing, or reindex. Restore intent is conditional evidence only and must be cleared when current SQL authority no longer accepts restoration.
- Search overlays, discovery hydration, reindex, recovery, cache-warmer, feed/export, support, analytics, ranking, localization, or global-script paths that touch marketplace visibility must independently suppress current SQL suspended or banned vendor governance before preserving or indexing public listing visibility.
- Future merchant-vs-governance precedence changes must update `MerchantMarketplaceGovernancePrecedenceCertificationTests`, `docs/architecture/merchant-marketplace-governance-precedence-certification-findings.md`, and `.ai-context` PASS 12.6 entries in the same change.

## POS-ADMIN-CERTIFICATION-01 PASS 13.3 CommerceHubPriceAuthority Resolver Freeze

- CommerceHub item price resolution must use `CommerceHubPriceAuthority`; do not add new cart, checkout, projection, search, query, reindex, recovery, replay, feed/export, support, analytics, ranking, localization, or global-script direct price calculations.
- Current runtime final price remains `ProductVariant.EffectiveOnlinePrice`. Preserve customer-visible behavior until a future override-activation pass is explicitly certified.
- `CommerceHubPriceAuthority.OverridesEnabled` must remain `false` unless a future certified pass adds override governance, persistence, schema, UI, cart, checkout, projection, search, replay, recovery, docs, and tests.
- `CommerceHubListings.CommerceHubPrice` must remain rejected/cleared/constrained to null, and derivative `CommerceHubPriceOverride` fields must remain null.
- The resolver is subordinate to marketplace governance, vendor governance, category lineage, product lineage, variant lineage, media authority, tenant authority, and inventory authority. It must never create visibility.
- Future CommerceHub pricing changes must update `CommerceHubPriceAuthorityCertificationTests`, `docs/architecture/commercehub-price-authority-resolver-certification-findings.md`, and `.ai-context` PASS 13.3 entries in the same change.

## POS-ADMIN-CERTIFICATION-01 PASS 13.4 CommerceHub Price Override Persistence Freeze

- CommerceHub override persistence and audit metadata may exist, but override activation remains prohibited. Do not change `CommerceHubPriceAuthority.OverridesEnabled` from `false` without a future activation certification.
- `CommerceHubListings.CommerceHubPrice` remains disabled compatibility storage. Keep domain rejection, DbContext clearing, and the SQL null-only check constraint until governance, UI, migration, cart, checkout, projection, search, replay, recovery, operations, and rollback are certified together.
- `CommerceHubPriceOverrideUpdatedAtUtc`, `CommerceHubPriceOverrideUpdatedBy`, and `CommerceHubPriceOverrideReason` are attribution metadata only. They must not be used as cart, checkout, search, publication, visibility, approval, or governance authority.
- Derivative `PriceSource` and `OverridePresent` fields in MongoDB and Elasticsearch are diagnostics only. Current paths must continue emitting `ProductVariantEffectiveOnlinePrice` and `false` while overrides are disabled.
- Future override persistence, mutation, activation, audit, support, feed/export, search, projection, cart, checkout, recovery, reindex, cache-warmer, analytics, ranking, or global-script changes must update `CommerceHubPriceOverridePersistenceCertificationTests`, `docs/architecture/commercehub-price-override-persistence-audit-certification-findings.md`, and `.ai-context` PASS 13.4 entries in the same change.

## POS-ADMIN-CERTIFICATION-01 PASS 13.6 CommerceHub Price Override Controlled Activation Freeze

- Override activation is prohibited because PASS 13.6 found missing merchant manage permission, marketplace audit/emergency-disable permissions, merchant mutation workflow, immutable audit events, tenant-safe global rollout flag, active parity monitoring, operator tooling, and certified rollback.
- Do not partially activate overrides by flipping `OverridesEnabled`, relaxing the SQL null constraint, accepting write payloads, or emitting derivative override state independently.
- Future activation work must update `CommerceHubPriceOverrideActivationCertificationTests`, `docs/architecture/commercehub-price-override-controlled-activation-certification-findings.md`, and `.ai-context` PASS 13.6 entries in the same change.

## POS-ADMIN-CERTIFICATION-01 PASS 13.5 CommerceHub Price Override Governance Freeze

- Override activation remains prohibited. PASS 13.5 operational design must not be treated as a live feature flag, permission, API mutation, UI mutation, or customer-visible price change.
- Tenant merchants own CommerceHub price override decisions. Marketplace governance owns visibility, suppression, suspension, emergency disable, audit, rollback, and marketplace integrity only. It may not approve, reject, create, or edit merchant prices.
- Future activation must implement explicit merchant manage, marketplace audit, and marketplace emergency-disable permissions; versioned merchant mutation; immutable events; a global kill switch; tenant allowlist; optional cohorts; staged rollout; parity monitoring; and non-destructive fallback to `ProductVariant.EffectiveOnlinePrice`.
- Checkout financial-control baseline tests must remain green, and active override cart/checkout/projection/search/replay/recovery/rollback coverage must be certified before activation.
- Future override governance, permission, mutation, flag, rollout, rollback, monitoring, audit, chaos, or activation work must update `CommerceHubPriceOverrideGovernanceCertificationTests`, `docs/architecture/commercehub-price-override-governance-rollout-certification-findings.md`, and `.ai-context` PASS 13.5 entries in the same change.

## POS-ADMIN-CERTIFICATION-01 PASS 13.5B CommerceHub Price Override Business Model Correction Freeze

- CommerceHub price authority owner is the tenant merchant. CommerceHub visibility authority owner is marketplace governance.
- Marketplace governance may hide listings, suppress listings, suspend vendors, suspend tenants, emergency-disable override runtime, audit override activity, and preserve marketplace integrity.
- Marketplace governance may not approve prices, reject prices, create prices, or edit merchant prices. Do not add a price-approval permission or an approval-centric lifecycle.
- Merchant override lifecycle is optional `Draft`, then `Saved`, `Updated`, or `Removed`. Audit is oversight and must never become approval.
- Runtime override activation remains prohibited. Preserve the disabled resolver, rejected/cleared/null-constrained storage, derivative clearing, SQL-first replay/recovery/reindex/projection/search, rollout kill switch design, tenant rollout design, and emergency disable design.
- Future CommerceHub override business-model changes must update `CommerceHubPriceOverrideBusinessModelCertificationTests`, `docs/architecture/commercehub-price-override-business-model-correction-certification-findings.md`, and `.ai-context` PASS 13.5B entries in the same change.
## PASS 13.7 CommerceHub Price Override Implementation Freeze

- Do not reintroduce null-only persistence guards for `CommerceHubListings.CommerceHubPrice`.
- Do not duplicate CommerceHub override pricing logic outside `CommerceHubPriceAuthority`.
- Do not delete override records during kill-switch rollback.
- Do not add marketplace approval, rejection, creation, or editing workflows for merchant prices.
- Preserve tenant scope, actor audit metadata, immutable override events, stale mutation-version rejection, and SQL-first projection/search recovery.
## POS-ADMIN-CERTIFICATION-01 PASS 14.1 CommerceHub Product Publication Lifecycle Certification Freeze

- CommerceHub publication lifecycle changes must preserve tenant merchant intent ownership, marketplace visibility governance, PASS 13.7 merchant pricing ownership, and current SQL listing/product/variant/category/attribute/stock/media authority.
- Visibility-reducing unpublish, cleanup, suppression, and recovery deletion paths must remain usable after catalog authority removal. They must not require an active, undeleted, current variant before clearing stale listing or governance restore intent.
- MongoDB, Elasticsearch, Redis, events, homepage/discovery candidates, replay, reindex, and auto-healing remain derivative and must recompose current SQL authority before serving, indexing, or restoring visibility.
- Future CommerceHub publication, unpublish, restore, bulk operations, projection, search, discovery, homepage, cart, checkout, replay, recovery, cache-warmer, feed/export, support, analytics, ranking, localization, or global-script changes must update `CommerceHubPublicationLifecycleCertificationTests`, `docs/architecture/commercehub-publication-lifecycle-certification-findings.md`, and `.ai-context` PASS 14.1 entries in the same change.

## POS-ADMIN-CERTIFICATION-01 PASS 14.2 Merchant CommerceHub Operations Certification Freeze

- Do not describe the current CommerceHub POS Admin settings page as a complete merchant operations dashboard. It is an active-catalog single-listing editor.
- Preserve actionable backend authority failures in merchant UI flows. Do not replace shipping, taxonomy, governance, stale-version, actor-attribution, or rollout-policy outcomes with generic failure text.
- Client-side active-product, active-variant, and published-variant summaries are editor-scope counts only. Do not treat them as authoritative operational KPIs for suppressed, orphaned, drifted, or projection-freshness states.
- Future listing search, filters, suppression visibility, orphan cleanup, bulk operations, KPI, notification, analytics, or parity-monitoring work must preserve tenant scope, current SQL authority, PASS 13.7 pricing ownership, PASS 14.1 lifecycle authority, actionable outcomes, and update `MerchantCommerceHubOperationsCertificationTests`, `docs/architecture/merchant-commercehub-operations-certification-findings.md`, and `.ai-context` PASS 14.2 entries in the same change.

## POS-ADMIN-CERTIFICATION-01 PASS 14.3 Merchant Listing Operations Console Certification Freeze

- Merchant listing operations search, filters, KPIs, suppression visibility, orphan visibility, analytics, notifications, and bulk workflow results must derive from current SQL authority.
- Do not move merchant listing operations authority into client-side catalog summaries, MongoDB, Elasticsearch, Redis, or event payload snapshots.
- Merchant bulk publish and category update must fail closed for suppressed listings and orphaned product/variant/category authority. Merchant bulk unpublish may remain available as visibility-reducing cleanup.
- Suppression reason, timestamp, and owner are merchant diagnostics only. Do not add merchant controls that clear marketplace suppression or vendor/tenant governance.
- Future merchant listing operations console changes must preserve tenant scope, per-row bulk outcomes, marketplace governance precedence, PASS 13.7 pricing ownership, PASS 14.1 lifecycle authority, and update `MerchantCommerceHubOperationsCertificationTests`, `docs/architecture/merchant-commercehub-operations-certification-findings.md`, and `.ai-context` PASS 14.3 entries in the same change.

## POS-ADMIN-CERTIFICATION-01 PASS 15.1 Marketplace Governance Console Certification Freeze

- Super Admin marketplace governance console operations must remain SQL-authoritative. Dashboard counts, listing search, filters, suppression state, orphan state, vendor governance state, tenant governance state, and audit records must derive from PostgreSQL.
- Marketplace governance owns visibility only. Listing suppression/restoration and tenant suspension/restoration must not mutate merchant-owned publication intent, category selection, attributes, or CommerceHub price overrides.
- Listing restoration must fail closed unless current SQL tenant, vendor governance, product publication/online visibility, current variant lifecycle, and live CommerceHub category lineage all accept visibility.
- Vendor governance remains owned by `UserGovernanceProfile` and the existing user governance workflow; marketplace listing search may display vendor governance state but must not create a duplicate vendor authority model.
- Tenant governance remains SQL tenant lifecycle authority. Super Admin tenant suspension/restoration must audit actor/reason and trigger derivative visibility verification.
- Future governance dashboard, listing search, suppression, tenant governance, vendor governance, audit, monitoring, notification, parity, or frontend console changes must update `MarketplaceGovernanceConsoleCertificationTests`, `docs/architecture/marketplace-governance-console-certification-findings.md`, and `.ai-context` PASS 15.1 entries in the same change.

## POS-ADMIN-CERTIFICATION-01 PASS 15.2 Marketplace Operations Console Certification Freeze

- Super Admin marketplace operations are derivative recovery controls, not authority. Reindex, replay, recovery, repair, auto-healing, diagnostics, and dashboards must recompose current SQL listing/product/variant/category/tenant/vendor authority before mutating or presenting visibility conclusions.
- MongoDB, Elasticsearch, Redis, search sync status, queue payloads, health checks, and dashboard counters are candidate discovery or diagnostics only. They must not resurrect deleted, suppressed, orphaned, inactive, suspended, invalid-category, or superseded-variant listings.
- Global reindex, tenant reindex, catch-up reindex, listing verification, and tenant verification must be reasoned, audited, rate-limited where write-like, and tenant-scoped where a tenant or listing is targeted.
- Auto-healing must remain derivative. It may index only when the SQL overlay returns current visibility and must delete/no-op/fail closed when SQL authority is absent.
- Future marketplace operations, reindex, replay, recovery, repair, auto-healing, search sync, projection sync, diagnostics, monitoring, dashboard, or frontend console changes must update `MarketplaceOperationsConsoleCertificationTests`, `docs/architecture/marketplace-operations-console-certification-findings.md`, and `.ai-context` PASS 15.2 entries in the same change.

## POS-ADMIN-CERTIFICATION-01 PASS 15.3 Marketplace Finance & Risk Certification Freeze

- CommerceHub money authority remains PostgreSQL and the append-only financial/accounting ledger. Do not treat MongoDB, Redis, Elasticsearch, provider callbacks, payout rail responses, frontend snapshots, or dashboard counters as internal financial truth.
- Order totals, item prices, commission snapshots, vendor earnings, refunds, payouts, settlement-like payout entries, balances, and accounting entries must derive from current SQL authority and immutable ledger evidence. PASS 13.7 `CommerceHubPriceAuthority` remains the only CommerceHub item price resolver.
- Replay and recovery must not create or delete money. Ledger writes must preserve idempotency/fingerprint matching, duplicate-race detachment of pending transaction/outbox rows, append-only records, and fail-closed divergent idempotency collisions.
- Outward money movement must remain readiness-gated and reconciliation-aware. Payouts must require available ledger-confirmed earnings, same currency, positive net amount after negative adjustments, row-locked reservation, committed handoff token, and unique external evidence. Refunds must validate cumulative totals and block overlap with processing payouts.
- Future CommerceHub finance, commission, settlement, payout, refund, balance, accounting, recovery, reconciliation, monitoring, dashboard, or operator-control changes must update `MarketplaceFinanceRiskCertificationTests`, `docs/architecture/commercehub-finance-risk-certification-findings.md`, and `.ai-context` PASS 15.3 entries in the same change.

## POS-ADMIN-CERTIFICATION-01 PASS 15.4 CommerceHub Emergency Operations Freeze

- CommerceHub emergency authority is operational authority only. It may stop visibility, stop payment intake, stop outward money movement, suppress, suspend, isolate, verify, reindex, diagnose, or recover derivative state, but it must not create SQL, ledger, tenant, product, variant, category, inventory, payment, refund, payout, or publication authority.
- `CommerceHub:EmergencyStops` must remain default-off and routed through `CommerceHubFinancialReadinessGate`. Do not add payment, payout, or refund emergency stops outside the existing financial readiness/lifecycle boundaries.
- Listing suppression, tenant suspension, vendor governance, reindex, and auto-healing verification may reduce visibility. Restoration must continue to fail closed unless current SQL tenant, vendor, product, variant, and CommerceHub category authority accept visibility.
- Emergency rollback must recompose from SQL authority or route through lifecycle/reconciliation owners. Do not rollback by copying MongoDB, Redis, Elasticsearch, queue payloads, dashboards, exports, support-tool state, or old repair snapshots into business truth.
- Future emergency, break-glass, incident-response, visibility-stop, payment-stop, payout-stop, refund-stop, recovery-stop, rollback, diagnostic, monitoring, or operator-control changes must update `MarketplaceEmergencyOperationsCertificationTests`, `docs/architecture/commercehub-emergency-operations-certification-findings.md`, `docs/operations/operational-recovery-runbooks.md`, and `.ai-context` PASS 15.4 entries in the same change.

## POS-ADMIN-CERTIFICATION-01 PASS 15.5 Marketplace Audit, Compliance & Forensics Freeze

- Critical audit, governance, financial, accounting, ledger, and dead-letter evidence must remain append-only at reviewed SQL write boundaries. Do not add tracked update/delete paths for evidence rows.
- Super Admin operator evidence must persist durably to SQL. Do not route it back through the process-memory generic audit queue.
- Do not describe transient outbox rows, processed-event fences, logs, telemetry, MongoDB, Redis, Elasticsearch, or dashboards as durable forensic history.
- Future audit, retention, replay, recovery, listing-history, governance-history, finance-evidence, compliance, SIEM, search, or operator-accountability changes must update `MarketplaceAuditForensicsCertificationTests`, `docs/architecture/marketplace-audit-compliance-forensics-certification-findings.md`, and `.ai-context` PASS 15.5 entries in the same change.

## POS-ADMIN-CERTIFICATION-01 PASS 15.5B Forensic Evidence Hardening Freeze

- CommerceHub merchant publication, update, unpublish, bulk, governance suppression, and restore paths must append merchant lifecycle evidence in SQL.
- CommerceHub reindex, catch-up, verification, replay, repair, recovery, and auto-healing paths must append recovery-operation evidence with actor/system identity, scope, timing, outcome, reason, and correlation.
- Generic tenant audit must remain SQL-first. Do not restore memory-first audit persistence or remove retry backlog visibility.
- Forensic export is a transfer artifact only. Do not describe API export as WORM retention without an externally enforced immutable sink.
- Future forensic hardening changes must update `MarketplaceForensicEvidenceCertificationTests`, `docs/architecture/marketplace-forensic-evidence-hardening-certification-findings.md`, and `.ai-context` PASS 15.5B entries in the same change.

## POS-ADMIN-CERTIFICATION-01 PASS 15.6 Marketplace Production Readiness Certification Freeze

- CommerceHub public-launch verdict remains **NOT READY** until every A-class launch blocker in `docs/architecture/marketplace-production-readiness-certification-findings.md` is closed with deployment evidence.
- Do not downgrade PostgreSQL evidence-table roles, external WORM/SIEM retention and delivery, outage-surviving tenant-audit handoff, verified alert packs and dashboards, or deployment backup/restore/operator rehearsal into post-launch maturity debt.
- Preserve SQL-first runtime authority, fail-closed emergency controls, SQL/ledger finance authority, derivative search/projection behavior, and SQL-first replay/recovery while blockers are closed.
- Future readiness, deployment, audit-durability, compliance, alerting, backup, restore, or launch-approval changes must update `MarketplaceProductionReadinessCertificationTests`, `docs/architecture/marketplace-production-readiness-certification-findings.md`, and `.ai-context` PASS 15.6 entries in the same change.

## PRODUCTION READINESS CLOSURE 1 Evidence Database Hardening Freeze

- Certified forensic evidence tables must retain PostgreSQL `BEFORE UPDATE OR DELETE OR TRUNCATE` rejection triggers backed by `forensic.reject_append_only_evidence_mutation()`.
- Runtime application and background-worker identities may receive evidence `SELECT` and `INSERT` only through `pointofsale_evidence_writer`. Reporting and maintenance identities remain read-only.
- Runtime identities must never own evidence tables or inherit `pointofsale_evidence_owner` / `pointofsale_evidence_migration`.
- Do not add reversible rollback that removes append-only protection. Evidence schema evolution requires reviewed forward migrations.
- Future evidence-table additions, renames, archival tooling, retention procedures, migration changes, or role changes must update `20260601190000_EvidenceDatabaseHardening`, `ProvisionForensicEvidenceRoles.sql`, `VerifyForensicEvidenceDatabaseHardening.sql`, `EvidenceDatabaseHardeningCertificationTests`, and `docs/architecture/evidence-database-hardening-certification-findings.md` together.

## PASS 16.1 Customer Identity Certification Freeze

- CommerceHub customer identity remains **NOT READY** for public launch until password recovery, email verification/resend, profile mutation, password/email change, customer-facing session controls, append-only SQL identity evidence, and identity-specific monitoring are certified.
- Registration must keep normalized-email uniqueness SQL-owned, map duplicate races to `EMAIL_EXISTS`, and commit `CommerceHubCustomer` plus customer governance profile in one SQL transaction.
- Suspended and banned CommerceHub customers must not obtain new sessions or retain existing JWT access. Keep JTI revocation, durable `TokensValidAfterUtc` cutoff validation, HttpOnly cookies, and CSRF enforcement.
- Guest-to-account cart transition must preserve the guest cart snapshot until authenticated SQL-backed adoption completes. Logout must continue clearing customer-scoped browser state.
- Future customer registration, login, JWT, logout, revoke-all, recovery, verification, profile, guest-cart, frontend identity UX, audit, or monitoring changes must update `CustomerIdentityCertificationTests`, `CustomerSessionLifecycleCertificationTests`, `CustomerRecoveryCertificationTests`, `docs/architecture/customer-identity-certification-findings.md`, and `.ai-context` PASS 16.1 entries together.

## PASS 16.1B Customer Identity Runtime Completion Freeze

- Do not return raw password-reset or verification tokens from CommerceHub customer APIs. Deliver them only through `IEmailSender`, store hashes only, and keep link construction configuration-driven.
- Do not authenticate CommerceHub registrations before mailbox verification or allow unverified customers to obtain login sessions. Verification resend must remain enumeration-safe.
- Reset and verification consumption must remain conditional SQL mutations so concurrent replay cannot consume the same token twice.
- Password reset, password change, email change, logout, and revoke-all must preserve browser-cookie invalidation and durable session-cutoff authority where applicable.
- `CommerceHubCustomerIdentityEvents` must remain append-only in EF and PostgreSQL. Deployment evidence roles, SMTP proof, refresh-token rotation, identity monitoring, and chaos validation remain launch prerequisites.

## PASS 16.2 Product Discovery Certification Freeze

- CommerceHub product discovery must remain SQL-authoritative. Homepage, featured, promoted, search, autocomplete, category, filter, card, detail, related, recommendation, popular, SEO, and deep-link surfaces may use Elasticsearch, MongoDB, Redis, homepage JSON, analytics, and frontend state only as candidate, cache, ranking, or evidence inputs.
- Search/list/detail/suggestion/autocomplete responses must recompose returned listings through `CommerceHubSearchSqlAuthorityOverlay` or an equivalent current-SQL authority boundary before response. Do not return raw derivative hits as final customer-visible products.
- Homepage and recommendation-like product sections must filter current SQL listing/product/variant/category/tenant/vendor visibility before response. Future recommendation engines must not resurrect suppressed, unpublished, deleted, invalid-lineage, inactive, suspended, or superseded listings.
- Category and filter UX must stay live SQL lineage/schema bound. Unsupported or deleted attribute filters must fail closed, and visible facets must not be raw unvalidated search aggregations.
- Product cards and detail pages must display backend-authoritative price, stock, image, title, category, and suppression outcome; frontend code must not infer product visibility from cached DTOs, local state, or URL parameters.
- Future discovery, search, recommendation, SEO, homepage, category, filter, telemetry, monitoring, or frontend changes must update `CommerceHubProductDiscoveryCertificationTests`, `CommerceHubSearchVisibilityCertificationTests`, `CommerceHubDiscoveryAuthorityCertificationTests`, `docs/architecture/commercehub-product-discovery-certification-findings.md`, and `.ai-context` PASS 16.2 entries together.
## PASS 16.3 Cart Certification Freeze

- CommerceHub cart persistence, pricing, and checkout handoff must remain SQL-authoritative. Frontend cart state, local storage, displayed totals, price snapshots, displayed stock, shipping display values, product DTOs, MongoDB, Elasticsearch, Redis, and event payloads are cart intent/evidence only.
- Cart save/load/pricing/checkout paths must revalidate current SQL listing, product, variant, tenant, publication, category, stock, price, promo, shipping, payment-method, customer governance, vendor governance, readiness, and ownership authority before preserving or acting on cart lines.
- Cart sanitization must enforce full product publication visibility (`IsOnlineVisible`, `PublishStatus == Published`) with parity to `CalculateAsync` and checkout authority. Do not regress to checking only `IsActive`; offline or draft products must be removed during sanitization with an `ITEM_REMOVED_UNAVAILABLE` notice.
- Authenticated cart ownership must remain bound to `CommerceHubCustomerId` resolved from the CommerceHub customer authentication scheme. Do not trust raw client customer IDs or resurrect legacy `OnlineCustomerId` as customer ownership authority.
- Multi-tab and multi-device behavior must preserve optimistic cart version conflicts, checkout customer/idempotency locks, persisted cart locks, request-hash-bound idempotency, and final SQL recomputation. Do not silently last-write-wins authenticated cart changes.
- Guest-to-customer adoption must preserve the guest snapshot until backend sanitation and persistence succeeds, and duplicate merge quantity must remain clamped and revalidated by backend authority.
- Future cart, mini-cart, pricing preview, checkout handoff, merge, guest storage, session, conflict handling, inventory, promo, shipping, monitoring, or frontend UX changes must update `CommerceHubCartCertificationTests`, `CommerceHubCartPricingCertificationTests`, `CommerceHubCartInventoryCertificationTests`, `docs/architecture/commercehub-cart-certification-findings.md`, and `.ai-context` PASS 16.3 entries together.

## PASS 16.4 Checkout Certification Freeze

- CommerceHub checkout must remain SQL-authoritative. Frontend checkout payloads, displayed totals, expected unit prices, shipping selections, payment-method selections, idempotency keys, local recovery context, MongoDB, Elasticsearch, Redis, and provider handoff state are intent/evidence only.
- Checkout must require authenticated CommerceHub customer authority, current customer governance, customer/idempotency locks, request-hash-bound idempotency, saved-cart row lock/version match, and serializable SQL order creation before converting a cart into an order.
- Checkout must revalidate current SQL listing, product, variant, tenant, vendor governance, financial readiness, payment-method, shipping, promo, price, inventory, commission, and cart ownership authority. Stale or missing authority must fail closed with a refresh/retry outcome.
- Inventory reservation must remain backend-only, occur before order commit, and bind to marketplace order, sub-order, item, product, variant, quantity, and expiry evidence. Payment retry must fail closed when reservation authority is lost.
- Online checkout may create only a pending payment handoff. Provider execution, duplicate provider sessions, callbacks, unknown captured outcomes, retry eligibility, and recovery must remain inside payment lifecycle/reconciliation authority.
- Future checkout, payment-prep, reservation, shipping, address, order-creation, idempotency, concurrency, recovery, monitoring, or frontend UX changes must update `CommerceHubCheckoutCertificationTests`, `CommerceHubCheckoutFinancialCertificationTests`, `CommerceHubCheckoutReservationCertificationTests`, `docs/architecture/commercehub-checkout-certification-findings.md`, and `.ai-context` PASS 16.4 entries together.

## PASS 16.5A Order & Fulfillment Lifecycle Certification Freeze

- CommerceHub order lifecycle must remain SQL-authoritative. `CommerceHubMarketplaceOrder.CustomerId` owns customer visibility, `CommerceHubSubOrder.StoreId` owns merchant/POS fulfillment authority, and MongoDB, Redis, Elasticsearch, frontend state, status events, dashboards, and recovery candidates are evidence only.
- CommerceHub marketplace order/sub-order/item lifecycle fields must remain guarded by `CommerceHubBoundaryMutationScope.BeginOrderLifecycle()` in `ApplicationDbContext`. Do not add direct EF, raw SQL, bulk update, projection, or repair paths that mutate order state outside a reviewed lifecycle boundary.
- POS Admin fulfillment must remain tenant-scoped, serializable, row-locked, transition-validated, payment-ready before outbound movement, inventory-aware for cancellation/delivery, status-history/audit producing, and projection-event emitting.
- Super Admin order-state mutation must not be introduced as a silent override. Any future order repair/override workflow must be explicit, reasoned, audited, evidence-bearing, and must not bypass transition, payment, inventory, return, refund, or projection authority.
- Return and refund-trigger workflows must preserve SQL row locks, return policy transition checks, confirmed-receipt inventory safeguards, execution milestone checks, cumulative refund validation, and reconciliation-required states for uncertain external outcomes.
- Replay, recovery, repair, reindex, dashboards, and frontend actions must not resurrect cancelled, delivered, completed, returned, refunded, deleted, or stale order lifecycle state from projections, event payloads, caches, local storage, or old repair snapshots.
- Future CommerceHub order, fulfillment, cancellation, delivery, return, refund-trigger, projection, recovery, monitoring, dashboard, or frontend changes must update `CommerceHubOrderLifecycleCertificationTests`, `CommerceHubFulfillmentAuthorityCertificationTests`, `CommerceHubOrderTransitionCertificationTests`, `docs/architecture/commercehub-order-fulfillment-lifecycle-certification-findings.md`, and `.ai-context` PASS 16.5A entries together.

## PASS 16.5B Settlement & Merchant Balance Certification Freeze

- CommerceHub settlement, merchant balances, commission debt, vendor payment requests, online-payment payables, payouts, refunds, dashboards, and reconciliation must remain SQL ledger-authoritative. Frontend state, MongoDB, Redis, Elasticsearch, provider callbacks alone, dashboard counters, and exported reports are evidence only.
- COD customer cash is merchant-held, but platform commission is SQL receivable debt. Online customer money is platform-held until SQL ledger-confirmed vendor payable is paid out.
- COD commission debt `amountOwed >= 1000 EGP` must immediately suspend new CommerceHub order acceptance and public vendor/listing visibility. Grace-period fields may support operator messaging only and must not permit order acceptance above the hard limit.
- Settlement/debt repayment paths must require vendor scope, idempotency, unique external evidence, actor/reason evidence, mirrored ledger posting, financial state delta verification, and append-only audit evidence.
- Refund and payout changes must preserve cumulative refund overrun validation, payout/refund overlap blocking, external handoff/evidence fences, `ReconciliationRequired` uncertainty states, and ledger-fingerprint idempotency.
- Future CommerceHub settlement, balance, commission, vendor-payment, payout-method, payout, refund, dashboard, replay, recovery, reconciliation, monitoring, or frontend finance changes must update `CommerceHubSettlementCertificationTests`, `CommerceHubMerchantBalanceCertificationTests`, `CommerceHubFinancialReconciliationCertificationTests`, `docs/architecture/commercehub-settlement-merchant-balance-certification-findings.md`, and `.ai-context` PASS 16.5B entries together.

## PASS 16.5C COD Risk & Debt Control Certification Freeze

- CommerceHub COD risk controls must remain SQL ledger-authoritative. Vendor debt must be recomposed from SQL commission, settlement, approved repayment, adjustment, refund, and audit evidence; frontend state, MongoDB, Redis, Elasticsearch, dashboards, and cached visibility are evidence only.
- COD hard-limit suspension must remain authoritative at `amountOwed >= 1000 EGP` by default. Threshold-crossing evidence must be recorded when effective debt reaches the hard limit, and reactivation must require current SQL debt below the hard limit.
- Checkout must reject financially suspended vendors before new order creation. Public discovery, search, autocomplete, suggestions, homepage, product detail, recommendation-like surfaces, reindex, and auto-healing must not return or resurrect suspended vendor visibility without current SQL authority.
- Vendor debt reduction must remain ledger-backed, evidence-bearing, idempotent, externally referenced, actor/reason audited, and guarded against over-reduction or negative displayed debt.
- Existing orders must continue through SQL order, fulfillment, return, refund, and settlement lifecycle authority; COD risk suspension blocks new acceptance and visibility only.
- Future COD debt, threshold, suspension, reactivation, checkout, discovery, existing-order, repayment, settlement, refund, recovery, replay, dashboard, monitoring, or frontend risk-control changes must update `CommerceHubCodRiskCertificationTests`, `CommerceHubVendorDebtCertificationTests`, `CommerceHubVendorSuspensionCertificationTests`, `docs/architecture/commercehub-cod-risk-control-certification-findings.md`, and `.ai-context` PASS 16.5C entries together.

## PASS 16.5D Payout & Reconciliation Certification Freeze

- CommerceHub payout identity, payout execution, payout completion, merchant balance, settlement, refund interaction, recovery, dashboard, and reconciliation paths must remain SQL/ledger-authoritative. Frontend state, MongoDB, Redis, Elasticsearch, provider callbacks alone, queue payloads, dashboard counters, or exported reports are evidence only.
- Merchant payout identities must stay vendor-scoped through trusted tenant context and `CommerceHubVendorPayoutMethod.VendorId`. Merchant-facing payout method APIs must not trust raw client vendor ids or expose another vendor's payout identities.
- Super Admin payout execution may view vendor payout identities for platform execution, but must revalidate current vendor/method ownership, current platform actor authority, eligible ledger-backed payable balance, platform cash, final readiness, external evidence, idempotency, and ledger posting before recording completed payout truth.
- Completed payout evidence must remain globally unique across non-failed CommerceHub payouts. Completion must require terminal external reference or bank transaction evidence, and processing/reconciliation handoff must preserve committed handoff token and idempotency evidence.
- Refunds must not rewrite completed payout history. Post-payout refund impact must use debt/adjustment authority, and refund completion must continue blocking overlap with processing payout earnings.
- Future payout, payout-method, vendor repayment, payout dashboard, refund interaction, recovery, replay, reconciliation, monitoring, or Super Admin payout execution changes must update `CommerceHubPayoutCertificationTests`, `CommerceHubPayoutIdentityCertificationTests`, `CommerceHubFinancialReconciliationCertificationTests`, `docs/architecture/commercehub-payout-reconciliation-certification-findings.md`, and `.ai-context` PASS 16.5D entries together.

## PASS 16.5E Refund & Financial Reversal Certification Freeze

- CommerceHub refund requests, return requests, refund executions, financial reversals, dashboards, recovery, replay, settlement interaction, payout interaction, and reconciliation must remain SQL/ledger-authoritative. Frontend state, MongoDB, Redis, Elasticsearch, provider callbacks alone, queue payloads, dashboard counters, and reports are evidence only.
- Customer refund requests must prove customer/order-item ownership, idempotency, eligibility, payout snapshot integrity, and evidence ownership before persistence. Super Admin approval/rejection/retry/execution must remain explicit, actor-bound, reasoned, and audited. POS Admin return receipt must stay tenant-scoped through trusted tenant context.
- Refund execution must preserve serializable row locks, refund/return transition policies, outward-money readiness gates, payout-overlap blocking, cumulative refund overrun validation, financial state validation, ledger fingerprint idempotency, and `ReconciliationRequired` uncertainty states.
- COD refunds must adjust platform commission debt without assuming platform-held customer cash. Online-payment refunds must reverse platform cash/vendor payable before payout, and post-payout refunds must use debt/adjustment authority without rewriting completed payout history.
- Future refund, return, reversal, COD debt, online-payment payable, payout interaction, settlement interaction, dashboard, replay, recovery, reconciliation, monitoring, or frontend UX changes must update `CommerceHubRefundCertificationTests`, `CommerceHubFinancialReversalCertificationTests`, `CommerceHubReturnAuthorityCertificationTests`, `docs/architecture/commercehub-refund-financial-reversal-certification-findings.md`, and `.ai-context` PASS 16.5E entries together.

## PASS 16.5F-A CommerceHub Currency Authority Certification Freeze

- CommerceHub marketplace currency is EGP only through `FinancialCurrencyGuard.PlatformBaseCurrencyCode`. POS Admin and storefront may remain tenant-currency systems, but CommerceHub listing, pricing, cart, checkout, order, settlement, payout, refund, ledger, projection, replay, recovery, dashboard, and frontend paths must not derive marketplace currency from `Tenant.Currency`.
- Price overrides are amount-only CommerceHub marketplace overrides. Do not add `CommerceHubPriceCurrency`, `OverrideCurrency`, or equivalent fields without a separately certified FX/currency authority model.
- Tenant-currency events and storefront config updates may trigger CommerceHub derivative convergence but must not write tenant currency into CommerceHub product documents or search indexes.
- Future CommerceHub currency, publication, pricing, cart, checkout, order, settlement, payout, refund, ledger, projection, replay, recovery, dashboard, or frontend changes must update `CommerceHubCurrencyAuthorityCertificationTests`, `CommerceHubPublicationCurrencyCertificationTests`, `CommerceHubPriceOverrideCurrencyCertificationTests`, `docs/architecture/commercehub-currency-authority-certification-findings.md`, and `.ai-context` PASS 16.5F-A entries together.

## PASS 16.5F-B CommerceHub Publication Currency Policy Enforcement Freeze

- CommerceHub publication is EGP-only. POS Admin and storefront remain multi-currency, but products whose current catalog price currency is not EGP must not be published, republished, bulk-published, listing-updated, replayed, recovered, or reindexed into live CommerceHub visibility.
- Current product price currency authority is `Tenant.Currency` because the catalog model has no `Product.Currency` or `ProductVariant.Currency`. If product/variant currency authority is added later, move the publication check to that narrower source and keep CommerceHub EGP-only unless a certified FX model exists.
- Publish, republish, update, bulk publish, bulk category update, bulk override removal, replay, recovery, auto-healing, catch-up reindex, tenant reindex, and global reindex must fail closed or converge to non-visible state when product price currency is not EGP. Unpublish must remain available for legacy/stale listing withdrawal.
- Frontend publication UI must show the actionable rule message: `CommerceHub currently supports EGP products only. This product uses USD and cannot be published.`
- Future CommerceHub publication-currency changes must update `CommerceHubPublicationCurrencyPolicyCertificationTests`, `CommerceHubPublicationCurrencyValidationTests`, `CommerceHubBulkPublicationCurrencyTests`, `docs/architecture/commercehub-publication-currency-policy-certification-findings.md`, and `.ai-context` PASS 16.5F-B entries together.

## PASS 16.1B-A Authentication & Session Authority Certification Freeze

- CommerceHub customer registration and email verification must not issue JWTs, set customer auth cookies, or otherwise create authenticated access. Verified email is a prerequisite for login, not a session.
- Customer sessions may be created only by successful `CommerceHubCustomerAuthService.LoginAsync` after verified email, password validation, and customer governance approval.
- Customer JWT validation must preserve JTI revocation, precise issued-at claims, durable `TokensValidAfterUtc` cutoff enforcement, and current governance checks.
- Logout must revoke the current JTI when present and clear browser cookie authority. Password reset, password change, email change, and revoke-all must advance durable session cutoff.
- Customer protected routes must explicitly use `CustomerSecurityConstants.AuthenticationScheme` and `CustomerSecurityConstants.Role`; frontend route guards and local Zustand state are UX only.
- Future CommerceHub registration, verification, login, logout, password reset, email change, session creation/destruction, route guard, frontend auth, or identity-audit changes must update `CommerceHubAuthenticationAuthorityCertificationTests`, `CommerceHubEmailVerificationCertificationTests`, `CommerceHubSessionCreationCertificationTests`, `docs/architecture/commercehub-authentication-authority-certification-findings.md`, and `.ai-context` PASS 16.1B-A entries together.
## PASS S1-A Service Marketplace Listing Authority Freeze

- Do not let service marketplace search, detail, projection, or rebuild paths serve public listings without current SQL service, tenant, and provider-profile authority.

## PASS F-S1-A Service Marketplace Frontend Discovery Authority Freeze

- Do not let service marketplace frontend discovery promote URL state, cache, filters, sorting, pagination, or realtime refresh into listing visibility authority; all visible listings must come from backend-qualified DTOs.

## PASS F-S1-C Service Marketplace Frontend Service Detail Presentation Authority Freeze

- Do not let service marketplace frontend detail presentation invent authority beyond backend visibility-qualified service detail DTOs.
