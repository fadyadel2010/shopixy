# 2026-07-22 Storefront / Builder Runtime Certification Registration

- PASS SF-ADM2.3 Builder Section Composition Authority Truth.
- PASS SF-ADM2.4 Builder Layout Authority Truth.
- PASS SF-ADM2.5 Builder Theme Authority Certification.
- PASS SF-ADM2.6 Builder Section Content Authority Truth.
- PASS SF-ADM2.7 Builder Publish Authority Certification.
- PASS SF-ADM2.8 Builder Preview Authority Certification.
- PASS SF-ADM2.9 Builder Version History & Rollback Authority Certification.
- PASS SF-ADM2.FINAL Store Builder End-to-End Authority Certification.
- PASS SF-CMS2 CMS Page Identity Authority Truth.
- PASS SF-CMS3 CMS Content Authority Truth.
- PASS SF-CMS4 CMS SEO Authority Truth.
- PASS SF-CMS5 CMS Publish Authority Truth.
- PASS SF-CMS6 CMS Preview Authority Truth.
- PASS SF-CMS7 CMS Version History & Rollback Authority Truth.
- PASS SF-CMS.FINAL CMS End-to-End Authority Certification.
- P2-GD Storefront Navigation Authority remains production-certifiable SQL authority over live navigation reads and POS Admin navigation writes.

# PAYMENT-END-TO-END-1 Execution Truth (2026-07-17)

- Payment workflow output is now source-certified into the Order Domain through `PaymentWorkflowExecutionService`, `PaymentIntentDispatcher`, provider-neutral `IPaymentIntentHandler` implementations, SQL Order persistence, and authoritative Order Domain events through `IEventPublisher`.
- Workflow remains pure, the dispatcher remains routing-only, handlers remain provider-neutral/channel-neutral, and existing Order event consumers remain the only synchronization path for POS Admin, Storefront, Affiliate, Marketplace/CommerceHub, and future channels.
- Canonical intent replay collapses against current SQL Order status before persistence and event publication, preventing duplicate persistence and duplicate Order Domain events for already-applied workflow intents.
- PAYMENT-END-TO-END-1 is source certification, not live operational certification; provider activation, environment evidence, dashboards, live channel drills, and durable intent-execution forensics remain future production-readiness work.

# PAYMENT-INTENTS-1 Infrastructure Truth (2026-07-17)

- Payment Intent Infrastructure is the first side-effecting boundary after pure Workflow intent production. Workflow emits immutable intent; `PaymentIntentDispatcher` resolves a registered handler; handlers load the Order aggregate, validate, execute one Order Domain action, persist, and publish authoritative Order Domain events.
- Dispatcher owns resolution and registration validation only. It must not own business rules, persistence, provider knowledge, Runtime execution, lifecycle mutation, or event publication.
- Payment Intent handlers are provider-neutral and channel-neutral. They must not call UI, Storefront, CommerceHub, Affiliate, notification services, provider SDKs, provider adapters, or RabbitMQ directly.
- Order Domain ownership is preserved by mutating only order status through the authorized order-status boundary. Payment lifecycle state remains owned by `IPaymentLifecycleService` / `PaymentLifecycleService`.
- PAYMENT-INTENTS-1 is source-certified infrastructure. PAYMENT-END-TO-END-1 now certifies workflow-to-dispatch wiring and replay collapse at source level; durable intent-execution forensics and live channel/provider rollout remain future production-readiness scope.

# PAYMENT-1.2 Payment Policy Evaluation Pipeline Truth (2026-07-10)

- Payment Policy Evaluation Pipeline owns the deterministic lifecycle for policy evaluation only: evaluation admission, context assembly, authority validation, fact normalization, completeness validation, snapshot/version binding, policy discovery, policy ordering, rule-group execution, rule evaluation, conflict resolution, decision composition, audit recording, decision return, and completion. Report: `docs/architecture/PAYMENT_1_2_POLICY_EVALUATION_PIPELINE.md`.
- `PipelineContext` is mutable only as evaluation-local coordination state for current stage, current policy/rule, intermediate results, candidate providers/methods, trace, warnings, failures, and audit buffer. It is not fact authority, runtime state authority, provider authority, or policy governance authority.
- `PolicyContext` remains the immutable provider-neutral input snapshot. Pipeline assembly may coordinate fact collection and normalization, but facts must come from current backend authority, immutable snapshots, or admitted evidence; frontend state, cache, projections, redirects, SDK results, dashboards, analytics, and raw provider payload replay are forbidden as policy authority.
- Pipeline evaluation is fail-closed: missing required facts, invalid authority, version mismatch, missing mandatory policy/rule, replay failure, audit failure, unknown capability, unknown constraint, dependency cycles, unresolved conflicts, non-deterministic rules, or side-effect attempts cannot produce executable allow decisions.
- Conflict precedence is deterministic: invalid authority/replay/version/policy/rule/audit failures, compliance/fraud/eligibility denies, quarantine, recovery, manual review, retry, alternatives, required evidence, constrained allow, and allow resolve in that order unless a certified override policy explicitly applies.

# PAYMENT-1.1 Payment Policy Framework Truth (2026-07-10)

- Payment Policy Framework owns provider-neutral policy contracts, canonical facts, rule model, policy model, rule catalog, fact-source boundaries, governance, immutable versioning, and replay-safe audit. Report: `docs/architecture/PAYMENT_1_1_POLICY_FRAMEWORK.md`.
- Facts are authoritative policy inputs from current backend authority, immutable snapshots, or admitted evidence. Rules never own facts, call providers, mutate runtime, or create transactions; policies compose versioned rules into deterministic decisions.
- Governance owns policy and rule lifecycle: Draft, Under Review, Approved, Published, Deprecated, Archived, Rejected, and Superseded. Runtime and Adapters never edit policies or rules.
- Policy, rule, catalog, fact schema, evaluation, audit, and replay versions are immutable once published. Corrections create new versions; rollback activates prior immutable versions and never rewrites decisions.
- Policy audit must record input snapshot, facts used, rules executed, rules skipped, decision, reason codes, versions, timestamp, actor/system, context hash, decision hash, and replay metadata.

# Architecture Truth

## PASS T8 Treasury Analytics Authority Truth

- Treasury analytics authority is derivative and non-monetary. Analytics may calculate balances, KPIs, aggregates, forecasts, exports, and alerts from tenant-scoped SQL ledger or CommerceHub financial transaction authority, but it never posts journals, settles payouts, recognizes cash, or overrides SQL financial truth.

## PASS T6 Treasury Reconciliation Authority Truth

- Treasury reconciliation authority is derivative and non-monetary. Reconciliation may detect provider, bank, payout, refund, settlement, and ledger drift; it may quarantine and route recovery into certified authorities, but it never creates money or treats external evidence alone as internal financial truth.

## PASS C7-G Settlement & Payout Final Currency Certification Truth

- Settlement and payout final currency authority is CommerceHub-owned and EGP-only. Settlement, payout, queue, dashboard, projection, and reconciliation views must preserve EGP platform currency truth and cannot treat tenant/storefront currency as platform settlement or payout currency.

## PASS S1-A Service Marketplace Listing Authority Truth

- Service marketplace listing/search/detail visibility is derivative until revalidated against current SQL service, tenant, and provider-profile authority.

## PASS F-S1-A Service Marketplace Frontend Discovery Authority Truth

- Service marketplace frontend discovery presentation is derivative over backend-qualified listing DTOs; URL state, filters, sorting, pagination, cache, and realtime refresh must not create visibility authority.

## PASS F-S1-C Service Marketplace Frontend Service Detail Presentation Authority Truth

- Service marketplace frontend detail presentation may shape backend DTOs for display, but backend SQL-qualified detail authority remains the source of visibility truth.

Date: 2026-05-27

## Purpose

This file defines the intended architectural truth of the PointOfSale system.

Its purpose is to document which parts of the system are authoritative, which parts are intentionally asynchronous, and which consistency tradeoffs are accepted by design so future audits do not misclassify them as accidental vulnerabilities.

This system is distributed by read models, outboxes, hosted workers, message transport, caches, and external providers. It is not a multi-writer system for business truth. PostgreSQL is the primary authority boundary for internal business state.

## Core Architectural Truth

- PostgreSQL is the authoritative write model for internal business truth.
- The SQL commit boundary is the canonical durability boundary for orders, payments, inventory, accounting, refunds, vendor balances, and payouts.
- MongoDB, Redis, and Elasticsearch are derivative read-performance layers. They are not authoritative business state.
- RabbitMQ and MassTransit are transport and distribution mechanisms. Message delivery is not the source of truth.
- External payment gateways, shipping providers, and payout rails are execution authorities for their own external side effects, but they are not the internal accounting source of truth.
- When external outcome certainty is missing, the system is expected to enter explicit blocked, processing, unknown-outcome, or reconciliation-required states instead of guessing.
- Direct state mutation of protected lifecycles is an architectural violation. Payment, order-status, and fulfillment transitions are intentionally forced through lifecycle services and interceptors.
- Tenant co-ownership of POS financial and inventory foreign keys is part of the write authority boundary. A valid `TenantId` on the row being saved is necessary but not sufficient when the row references tenant-owned parties, warehouses, products, variants, units, invoices, or accounts.
- Frontend, POS, admin, and storefront payloads are intent/evidence, not authority. Cached totals, projected stock, payment-method assumptions, local storage, optimistic UI, and provider client outcomes must be revalidated or recomputed by backend SQL authority before money, order, inventory, payout, commission, or accounting state changes.
- Tenant-facing settings that influence pricing, tax, receipts, company identity, or storefront checkout behavior must be tenant-scoped SQL authority or an explicit super-admin global authority surface. A tenant-admin write path must not mutate shared global rows that affect other tenants' customer-facing or financial behavior.
- `Tenant.Currency` and tenant-scoped `Tax.CurrencyCode` are the currency authority for a store's POS admin and storefront surfaces. New tenants may bootstrap with `EGP` as the default, but tenant admins may still choose another supported currency for their own POS/storefront authority.
- Legacy/global `SystemSettings` rows are not valid tenant runtime authority for tenant-facing pricing, tax, company-profile, or receipt behavior once tenant-scoped rows exist. Runtime reads for those keys must resolve by `SystemSettings.TenantId`; only explicit migration/backfill code may copy historical global values into tenant-owned rows.
- Tenant-facing numeric settings that participate in pricing or tax authority must be persisted in a culture-invariant canonical form. Runtime locale must not be able to reinterpret durable SQL financial settings into different monetary results.
- `Super Admin` payout, settlement, and central outward-money controls do not redefine tenant/storefront currency authority. Where no authoritative FX layer exists, those central surfaces must stay `EGP`-only or remain explicitly currency-scoped; they must never relabel tenant/storefront money as `EGP` text-only.
- Tenant-facing settings and tax authority must resolve by explicit `TenantId` and not rely on ambient EF query filters for correctness. Query filters may assist scoping, but the known tenant key remains the primary authority join.

## Source Of Truth Per Subsystem

| Subsystem | Intended source of truth | Notes |
|---|---|---|
| Tenant identity, configuration, catalog, pricing, CMS draft/live state | PostgreSQL | POS/Admin owns creation, mutation, and publish decisions |
| Category hierarchy, lineage, and product default-category ownership | PostgreSQL `Categories` | Mongo/storefront/category search/affiliate/CommerceHub paths are derivative; rebuilds must recompute live lineage from SQL and tombstone/delete invalid lineage |
| Product media ownership, gallery ordering, primary-image derivation, and variant media linkage | PostgreSQL `ProductMedia` plus `ProductVariants.MediaId` | Storefront product media, search thumbnails, affiliate thumbnails/intelligence, CommerceHub listing images, CDN/cache state, and rebuilds are derivative; media events are triggers and must reload SQL media authority before public visibility mutation |
| Storefront simple-product unit pricing and purchasability | PostgreSQL `ProductUnits` plus current default-unit selection | `ProductDocument.DefaultUnit`, `ProductDocument.Units`, storefront simple-product price/availability, promotion-visible simple-product price, and product search/storefront price surfaces are derivative visibility only; rebuild/replay/search/repair paths must recompose them from SQL and treat missing default unit as non-purchasable |
| Multi-channel product publication visibility | PostgreSQL `Products.PublishStatus`, `Products.IsOnlineVisible`, active/delete/archive state, plus SQL CommerceHub listing publication state | Storefront Mongo products, affiliate public/intelligence docs, CommerceHub listing docs, search documents, homepage/discovery, feeds/exports, caches, and retry/rebuild payloads are derivative; publication events are triggers and must reload SQL authority before restoring or removing visibility |
| Tenant-facing tax, receipt, and company-profile settings | PostgreSQL tenant-scoped `SystemSettings` rows | Runtime tenant authority resolves by `SystemSettings.TenantId`; prior global rows are migration input only |
| POS/admin operational workflows | PostgreSQL | APIs orchestrate authoritative writes |
| Storefront content read model | MongoDB derived from PostgreSQL publish events | Read-only projection; may lag |
| Storefront order read model | MongoDB derived from PostgreSQL order events, with SQL-backed repair | Critical reads may self-repair from SQL before serving and must prefer the freshest matching projection if duplicates exist |
| CommerceHub marketplace order/read models | MongoDB derived from PostgreSQL authority plus marketplace outbox/backbone, with SQL-backed repair for critical order detail | Read models may lag and may be rebuilt; critical reads must prefer the freshest matching projection if duplicates exist |
| Inventory stock, reservations, movements, transfers | PostgreSQL | Authoritative stock truth |
| Product variant identity, attribute-combination ownership, SKU ownership, and barcode ownership | PostgreSQL `ProductVariants` plus SQL unique indexes | `AttributeCombinationKey`, SKU, and barcode uniqueness are SQL authority; storefront, affiliate, CommerceHub, cache, search, and sync timing are derivative visibility only |
| Product variant public lifecycle, parent lineage, and external purchasability eligibility | PostgreSQL `ProductVariants` plus same-tenant parent `Products` publication lifecycle | Storefront cart stock, Mongo product variants, Elasticsearch documents, CommerceHub listings/search, affiliate intelligence/public projections, Redis caches, import/clone/rebuild payloads, and replayed variant events are derivative triggers only; archived, detached, orphaned, inactive, unpublished, deleted, or invalid-lineage variants must fail closed unless current SQL recomposition proves public lineage |
| Product variant merge/split semantic ownership and supersession lineage | PostgreSQL `ProductVariants.SemanticLifecycleState` plus replacement/operation/lineage metadata | Merged sources, split parents, and detached variants are lineage records only. Storefront, CommerceHub, affiliate, search, cache, inventory, reservation, pricing, media, publication, external mapping, feed/export, rebuild, and replay paths must converge to the current semantic owner and fail closed for superseded variants. |
| Product variant inventory and reservation lineage | PostgreSQL `ProductVariants`, parent `Products`, `Warehouses`, `ProductVariantStocks`, `InventoryReservations`, and stock movements | Reservation rows, stock-sync events, CommerceHub order state, Mongo/Search/cache stock, warehouse projections, and replay/rebuild payloads are evidence only. Current SQL variant semantic owner, active product lineage, active warehouse ownership, current stock row, and active unexpired reservation state decide whether stock can be reserved, consumed, committed, fulfilled, or propagated. |
| Payment lifecycle state | PostgreSQL via `IPaymentLifecycleService` | Protected by interceptor; direct writes are forbidden |
| Order status lifecycle | PostgreSQL via authorized lifecycle boundary | Protected by interceptor |
| Fulfillment lifecycle | PostgreSQL via `IOrderFulfillmentService` | Protected by interceptor |
| Accounting / financial reporting | PostgreSQL immutable double-entry ledger | Sole internal financial reporting authority |
| CommerceHub earnings, refunds, payouts, vendor balances | PostgreSQL | External execution may diverge temporarily; SQL tracks internal truth and reconciliation state |
| Affiliate commissions, balances, payout requests, and payout transfer allocations | PostgreSQL | External transfer evidence is modeled as platform rail-scoped SQL evidence plus allocation rows before local withdrawal truth |
| Search and discovery | Elasticsearch projection | Non-authoritative |
| Cache, locks, backplane, ephemeral runtime state | Redis | Infrastructure only, never business truth |
| WhatsApp transport/session state | Baileys service + runtime storage | Transport authority only, not business/business-financial truth |
| Payment gateway-side payment execution | External provider | External truth for external capture/refund attempt outcome, subject to local reconciliation |
| Bank/provider payout execution | External provider/bank | External truth for transfer execution, subject to local reconciliation |

## Ownership Boundaries

- `PointOfSale.API` is the orchestration boundary for authoritative writes, lifecycle transitions, webhooks, hosted workers, and cross-system recovery.
- `PointOfSale.Core` defines domain contracts, events, state machines, and invariant expectations.
- `PointOfSale.Infrastructure` implements persistence, outboxes, projections, consumers, reconciliation, and external adapters.
- `PointOfSale.Web` is an admin/POS client over authoritative APIs. It does not own business truth.
- `PointOfSale.Storefront` is a read-oriented customer surface. It does not own business truth and should not write directly.
- `PointOfSale.CommerceHub` is a customer/marketplace surface. It initiates workflows, but backend SQL services own truth.
- MongoDB consumers, Redis caches, and Elasticsearch indexing own projection freshness only. They do not own the underlying business decision.
- External providers own only their side effects. Internal recognition, classification, and reporting remain local SQL concerns.

## Global Authority Rules

- A successful SQL commit is the point at which the system considers an internal business mutation authoritative.
- Authoritative integration events must be staged before the source mutation is considered complete so event intent is atomic with business state.
- Authoritative integration events must go through the central `IEventPublisher` boundary. Direct `ConsumeContext.Publish` of business-truth events is not allowed because it bypasses aggregate-version, tenant-scope, affected-tenant, and transaction-boundary validation.
- Application and business services must not inject or call raw MassTransit publishers (`IPublishEndpoint`, `IBus`, or `ISendEndpointProvider`) for integration events. Raw MassTransit publishing is reserved for reviewed event-bus/outbox infrastructure such as `MassTransitEventPublisher`, authoritative outbox dispatchers, marketplace outbox dispatchers, and stock-sync outbox dispatchers.
- Authoritative event types must expose a deterministic aggregate ordering key either through `IAggregateScopedIntegrationEvent` or a supported aggregate-id property. Event types that carry affected-tenant arrays must implement `IAffectedTenantScopedIntegrationEvent`.
- At-least-once delivery is accepted throughout async pipelines. Consumers must be idempotent.
- Ordering is guaranteed only inside explicitly versioned aggregate streams, not across the entire system.
- Read-side lag is accepted unless a specific synchronous repair path is explicitly documented.
- Projection, search, cache, affiliate, CommerceHub, rebuild, and repair paths should use shared reliability primitives where available: `ProjectionEventFence` for duplicate/active EventId collapse before side effects, SQL authority overlays for derivative read models, `ProjectionAuthorityValidator` for version/timestamp freshness checks, `ProjectionTombstoneGuard` for stale resurrection protection, and `SqlTruthRebuildGuard` for SQL-first rebuild/reconciliation entrypoints. This is an incremental extraction layer; existing consumers may still carry local equivalents until migrated.
- Storefront product create/update/lifecycle projection and admin product rebuild now have an additional shared authority primitive: `IProductSqlAuthorityProjectionWriter`. Mongo `ProductDocument` mutation on those paths must go through that writer so SQL recomposition, event-version stamping, and tombstone convergence stay consistent; the prior optional `ApplicationDbContext`-less fallback in product create/update consumers is forbidden.
- Storefront product completeness is part of that same authority boundary. `ProductSearchSqlAuthorityOverlay` and `IProductSqlAuthorityProjectionWriter` are not allowed to partially recompose storefront-critical fields; `DefaultUnit`, `Units`, simple-product purchasability, product-level purchase price, and storefront detail/search metadata carried from SQL must converge on every shared writer/rebuild/search-overlay path before public mutation.
- Direct full `ProductDocument` replacement outside `ProductReadRepository` and the shared storefront writer is a governance violation on storefront product paths. Overlay-style mutations such as promotions must use field-scoped updates so they cannot overwrite SQL-authoritative unit/default-unit, category, media, publication, or tombstone state with stale in-memory documents.
- Reliability primitive adoption is tiered, not universal. `docs/architecture/reliability-adoption-strategy.md` is the current adoption matrix and migration guide: Tier 1 visibility consumers migrate first, Tier 2 consumers migrate opportunistically, Tier 3 analytics/soft-state paths stay lighter-weight, and financial lifecycle/outbox/reconciliation flows may keep stronger specialized manual protections.
- CHECKOUT-CON-14 adds the recovery/reconciliation authority rule: recovery workers may scan provider/webhook/carrier/payout evidence to find candidates, but before any mutation they must reload current SQL evidence and dependent payment/order/fulfillment/accounting/shipment/payout/commission truth under the appropriate transaction and row lock. Stale candidate snapshots, delayed callbacks, replayed recovery jobs, and provider/carrier payloads are evidence only; terminal or changed SQL truth must collapse to no-op, manual review, or reconciliation-required state rather than resurrecting payment, shipment, accounting, payout, commission, or fulfillment authority.
- Reservation cleanup follows the same recovery-authority rule: stale/orphan reservation discovery is not release authority. Before direct inventory or coupon reservation release, cleanup/repair code must recompose current SQL reservation, order, payment, CommerceHub payment, and unresolved-webhook truth under a transaction and row locks; active or uncertain current truth must collapse to skip or route through the payment lifecycle boundary.
- Integrity is preferred over availability whenever external money truth or authoritative backlog scope is uncertain.
- New direct SQL, `ExecuteUpdate`, or `ExecuteDelete` mutation paths are architecture-sensitive changes. They must be routed through an existing authority service or explicitly reviewed and allowlisted with tests proving tenant scope, lifecycle authority, transaction boundary, and outbox/reconciliation behavior.
- New frontend/API mutation contracts that carry calculated financial or inventory values must include backend stale-client protection: recompute from SQL, compare expected values where the client needs a price/stock promise, and fail closed rather than accepting client arithmetic as durable truth.
- Restoring a stronger uniqueness boundary for external-money evidence after a weaker schema was live is a production authority change, not a cosmetic migration. The rollout must preflight duplicate live rows and fail with explicit operator guidance before recreating the stronger unique index.
- Moving tenant-facing settings from legacy global rows into tenant-scoped runtime authority is also a production authority change. Backfill migrations must preflight duplicate live legacy keys and fail with explicit operator guidance rather than selecting an arbitrary winner during tenant fan-out.
- Variant attribute-combination identity is SQL authority. Concurrent create, replay, rebuild, storefront projection, affiliate projection, CommerceHub sync, SKU allocation, and barcode allocation must converge to the single PostgreSQL `ProductVariants` row selected by the `(TenantId, ProductId, AttributeCombinationKey)` uniqueness boundary. Projection timing and sync timing must never mint or select variant identity.
- Variant SKU ownership is SQL authority. Concurrent reassignment timing, replay timing, projection timing, storefront sync, affiliate sync, CommerceHub sync, cache state, and read-model visibility must never decide which variant owns a SKU. `ProductVariants` and the SQL SKU uniqueness boundary own SKU ownership; inventory, pricing, fulfillment, storefront, affiliate, and CommerceHub visibility must converge from that SQL row.
- Variant clone/import semantic ownership is SQL authority. Product and variant create/update paths must not copy SKU, barcode, product-level identity, media linkage, stock rows, or external/channel visibility as durable authority from a source payload. `ProductVariantCloneImportAuthority` governs deterministic SKU regeneration, barcode ownership rejection, product-owned media inheritance, duplicate initial-stock collapse, and simple-product default-variant identity mirroring on the inspected runtime mutation surfaces.
- Variant merge/split semantic ownership is SQL authority. `ProductVariantMergeSplitAuthority` governs current semantic owner checks, merge/split operation idempotency, stale-operation rejection, superseded-source identity clearing, split-parent retirement, lineage root stamping, and open-reservation rejection. A variant whose `SemanticLifecycleState` is not `Current`, or that carries supersession/replacement metadata, is not a public, purchasable, stock-reservable, SKU/barcode-owning, CommerceHub-visible, search-visible, exportable, pricing-inheriting, or media/publication-owning variant.
- Variant inventory and reservation lineage is SQL authority. `ProductVariantInventoryLineageAuthority` governs whether variant stock rows and reservation rows are current, consumable, and externally publishable. Superseded variants, inactive warehouses, deleted stock rows, orphan product lineage, expired reservations, released reservations, and stale stock-sync messages are not stock authority and must collapse to fail-closed rejection, safe release/repair, or deterministic replay no-op depending on the operation.
- PRODUCT-CON-11A adds the variant lifecycle resurrection rule: current PostgreSQL `ProductVariants` plus the same-tenant parent `Products` row own public variant lifecycle, lineage, and purchasability eligibility. `ProductVariantLifecycleAuthority` is the shared runtime helper for proving active variant state, non-deleted variant state, non-empty parent `ProductId`, same-tenant non-deleted parent, and active/online/published parent product state. Storefront cart, CommerceHub cart/checkout/listing/search, product search/storefront recomposition, affiliate stock/intelligence, replay, rebuild, and recovery paths must fail closed when that helper cannot prove public lineage, even if Mongo, Elasticsearch, Redis, CommerceHub listing, affiliate, or event payload state still shows the variant as visible or in stock.
- PRODUCT-CON-03 adds the category lineage rule: current PostgreSQL `Categories` rows and their live parent chain own category existence, lineage, and publication eligibility. A category whose parent is deleted, missing, self-referential, cyclic, cross-tenant, or `Guid.Empty` is not live lineage for storefront, search, affiliate, CommerceHub, breadcrumb, default-category, or product-category association purposes. Rebuild/reindex/repair paths must use SQL-lineage recomposition and tombstone/delete invalid lineage instead of trusting Mongo hierarchy, cached paths, breadcrumb strings, event payloads, or stale parent references.
- PRODUCT-CON-04 adds the media/CDN resurrection rule: current PostgreSQL `ProductMedia` rows and `ProductVariants.MediaId` own product gallery ordering, primary-image derivation, and variant media linkage. `ProductMediaChangedEvent` payloads, Mongo product media, search documents, affiliate thumbnails, CommerceHub listing images, CDN URLs, caches, and rebuild inputs are triggers or derivative visibility only. Runtime propagation and rebuild/reindex/repair paths must reject stale/future media event versions against SQL product mutation version, recompose media from SQL, and prefer tombstone/delete convergence over preserving stale image visibility.
- PRODUCT-CON-05 adds the search visibility/index authority rule: current PostgreSQL product, variant, stock, category lineage, media, promotion, publication, and CommerceHub listing authority decides whether a document may exist in storefront Elasticsearch, CommerceHub Elasticsearch, affiliate/public search, autocomplete/suggestion fields, homepage discovery, and rebuild/repair output. Product search create/update/stock/price/delete/archive/publication events are triggers only; search consumers must collapse duplicate EventId side effects where migrated, recompose current SQL authority before indexing, validate live category lineage, and delete/tombstone search documents when SQL no longer proves visibility. Stale Mongo product documents, stale Elasticsearch documents, replayed delete/create/update payloads, stale category/media/publication payloads, search rebuild inputs, and auto-healing candidates must never resurrect deleted, hidden, invalid-category, or unpublished search visibility.
- PRODUCT-CON-06 adds the homepage/recommendation discovery rule: storefront homepage product shelves, curated homepage product hydration, homepage section product/category references, CommerceHub homepage projections, affiliate-mode homepage visibility, and Redis homepage caches are not discovery authority. Runtime homepage product-card reads now recompose product/category/media/stock/price/promotion/affiliate visibility from SQL before returning cached or projected cards. Homepage section authoring and read/projection paths must validate or scrub curated CommerceHub listing/category IDs against current SQL listing, product, variant, category, and publication authority. Stock events invalidate homepage caches because stock can change featured/trending visibility. Rebuild/replay paths must prefer dropping invalid curated sections or tombstoning tenant homepage projections over preserving stale product/category references.
- PRODUCT-CON-07 adds the multi-channel publication replay rule: publish, unpublish, online visibility, delete, archive, category, media, stock, promotion, affiliate, CommerceHub, search, homepage, feed/export, repair, rebuild, and retry signals are triggers only. Current SQL product publication authority requires the product to exist, not be deleted, be active, be online-visible, and have `PublishStatus.Published`; CommerceHub additionally requires the current SQL listing to exist, not be deleted, and be published. Hardened storefront, affiliate, CommerceHub, and search mutation paths must fence duplicate publication side effects where migrated, recompose current SQL publication authority, and tombstone/delete derivative visibility when SQL no longer proves publication. Stale publication or stale unpublish replay must not resurrect hidden/deleted products, and stale unpublish replay must not hide a currently SQL-published product.
- PRODUCT-CON-08 adds the bulk cache fanout collapse rule: product create, update, delete, stock, price, and publication cache invalidation events are triggers only and may not repeatedly fan out Redis product-list, product-detail, promotion, or homepage pattern deletes under duplicate/active replay. `ProductCacheInvalidationConsumer` now requires SQL context, acquires a processed-event/advisory fence before Redis side effects for all product cache events, and reloads current SQL product slug before product-detail invalidation so stale payload slugs cannot drive cache-key authority. Cache invalidation remains best-effort and derivative, but duplicate EventId overlap must collapse before Redis side effects.
- PRODUCT-CON-09 adds the admin repair/rebuild resurrection rule: manual/admin product read-model repair is a rebuild trigger only, not a shortcut around publication, category, media, stock, promotion, or tombstone authority. `AdminSyncController` now uses `ProductSearchSqlAuthorityOverlay` before Mongo product repair writes, includes deleted SQL rows so repair can converge tombstones, and writes `ProductReadRepository.TombstoneIfFreshAsync(...)` when SQL does not currently prove active, online, published, live-category storefront visibility. Future recommendation, homepage, affiliate, CommerceHub, search, cache-warmer, or admin rebuild tools must preserve SQL-first recomposition and tombstone/delete convergence before derivative visibility mutation.
- PRODUCT-CON-10 adds the suggestion/autocomplete consistency rule: autocomplete, typeahead, suggestion, zero-result suggestion, popular-search, and product-name completion surfaces are search/discovery visibility only. CommerceHub suggestion responses now revalidate cached, Elasticsearch, and Mongo fallback product suggestions through current PostgreSQL CommerceHub listing, product publication, variant, category lineage, media, stock, price, tenant, and governance authority before returning them. Category suggestions must pass live SQL CommerceHub category lineage, and suggested/popular text terms are filtered to terms that still resolve to current SQL-visible CommerceHub products or categories. This is runtime response-boundary hardening; it does not create a universal dedicated suggestion-index framework or make browser-side short-lived autocomplete caches authoritative.

## 2026-05-12 Audit Revalidation

- The system-of-systems full-stack adversarial audit revalidated that customer-facing retryability, operator-visible payout execution authority, and critical order-read continuity are backend-derived authority contracts, not frontend inference.
- Critical storefront and CommerceHub order-read surfaces may still depend on Mongo as the served read model, but they must prove SQL existence first, repair targeted projections when required, and fail closed with temporary unavailability instead of returning false absence or partial authority.
- Because Mongo order-projection uniqueness is enforced by best-effort startup/consumer index creation rather than a synchronous SQL boundary, critical storefront and CommerceHub order reads/repairs must also prefer the freshest matching projection document by authoritative version/freshness markers instead of trusting arbitrary first-match Mongo results.
- Browser persistence remains continuity-only state. Secret recovery or session authority must stay server-issued and HttpOnly, while browser-visible recovery context may retain only non-secret routing identity.
- Because browser-visible recovery context is only continuity metadata and not authority, generic CommerceHub customer-session clear paths should also purge that visible context so expiry, revocation, or future logout callsites do not keep advertising stale order identity after auth state is gone.
- CHECKOUT-CON-04 adds the active CommerceHub checkout overlap rule: marketplace checkout must first collapse per active customer cart, then bind idempotency, then claim SQL cart/order/promo/reservation authority. Changing the idempotency key during an active duplicate checkout must not create an independent checkout authority lane.
- CHECKOUT-CON-05 adds the stale marketplace checkout availability rule: CommerceHub/marketplace stock, reservation, fulfillment, shipping, payment, affiliate, search, category, session, and projection visibility is derivative only. Marketplace payment initiation/recovery and fulfillment commit must recompose current SQL order, reservation, stock, catalog, shipping, and payment authority before provider-session creation, reservation finalization, order finalization, or stock/fulfillment commit.
- CHECKOUT-CON-06 adds the affiliate checkout price-authority rule: affiliate storefront/session/cache/search/category/banner visibility, affiliate promo overlays, affiliate referral/promotion-link snapshots, and affiliate commission projections are derivative only. Storefront affiliate checkout finalization must recompose current SQL catalog/pricing/coupon/promotion/payment/reservation truth and revalidate current SQL affiliate account, promotion-link, affiliate-user bridge, and commission-policy authority inside the final serializable checkout transaction before payment-intent placeholder creation, reservation commit, order creation, attribution creation, or commission snapshot persistence.
- CHECKOUT-CON-07 adds the marketplace reservation replay-authority rule: active SQL reservation quantity must exactly match current SQL CommerceHub order-item authority before marketplace payment initiation, payment recovery, reusable-session return, provider-session creation, provider-success recognition, or fulfillment commit. Missing, expired, consumed, over-reserved, or foreign-owner active reservations fail closed; duplicate marketplace reservation replay must not be allowed to hide behind "at least enough reserved" checks.
- CHECKOUT-CON-08 adds the out-of-order checkout visibility rule: storefront, CommerceHub, affiliate, payment-method, shipping, coupon/promotion, cache/search/category/banner, and session/recovery visibility are derivative only. Newer SQL checkout, reservation, payment eligibility, fulfillment eligibility, pricing, and promotion authority must not be overwritten by delayed older projection, cache hydration, rebuild, marketplace callback, affiliate callback, or recovery replay. Runtime commit/recovery paths must recompose SQL truth before checkout finalization, reservation reuse, payment recovery, fulfillment recovery, and checkout-session recovery. Projection/rebuild paths must fail closed through monotonic freshness/tombstones/event fences or rebuild from current SQL truth.
- CHECKOUT-CON-10 adds the fulfillment replay-after-consumption rule: after SQL inventory consumption, fulfillment state is high-risk irreversible authority. Fulfillment lifecycle transitions, shipment mutation, shipment authority repair, and fulfillment-derived commission approval must reload current SQL order/payment/reservation/inventory/fulfillment truth under serializable or locked SQL boundaries before emitting lifecycle events, shipment events, carrier/warehouse release side effects, movement mutations, or commission/payout eligibility. Stale carrier, warehouse, shipment-session, cache, projection, and recovery payloads are evidence only and must not recreate shipment authority or regress current fulfillment truth.
- CHECKOUT-CON-11 adds the delayed provider callback authority rule: provider callbacks, redirects, settlement payloads, cached payment state, replayed webhook payloads, and stale provider reconciliation data are external evidence only. Provider-driven payment success/failure, CommerceHub webhook outcome application, fulfillment release, accounting, payout, and order-state mutation must first recompose current SQL payment/order/fulfillment truth under locked or serializable SQL boundaries. Stale success/failure/capture/refund/settlement replay must collapse to no-op, manual review, or reconciliation rather than overwrite terminal SQL truth or recreate side-effect authority.
- CHECKOUT-CON-12 adds the partial financial lineage rule: immediate accounting, refund, settlement, payout, commission, reconciliation, and fulfillment-financial mutation paths must not trust provider payloads, cached settlement state, stale outbox/rebuild payloads, or in-memory post-commit snapshots. Before ledger/accounting mutation, collection/refund/settlement fast paths must reload locked SQL order/refund/settlement/outbox truth and collapse already-processed replay. Recovery/rebuild may discover evidence externally, but mutation authority must derive from SQL rows at mutation time.
- CHECKOUT-CON-13 adds the shipment/fulfillment double-execution authority rule: carrier execution, carrier-registration recovery, delayed carrier callbacks, shipment authority repair, shipment status mutation, fulfillment synchronization, inventory delivery commit, and shipment-derived payout/commission eligibility remain subordinate to current SQL shipment/order/fulfillment/inventory truth. After an external carrier create call returns, local success persistence must re-enter a serializable SQL boundary, lock and reload the current shipment and order rows, reject terminal/cancelled/stale shipment authority into reconciliation, and only then stamp carrier registration, shipment status, order shipment fields, or fulfillment delivery authority.
- The 2026-05-12 revalidation confirmed that the current worktree still honors those boundaries after the latest hardening pass and focused validation. No current evidence-backed architectural path was found in this audit slice where stale frontend state, projection lag, retries, or async timing can permanently override SQL money, stock, payout, or order authority without entering an existing blocked/reconciliation/fail-closed path.

## Financial Authority Flow

1. A financial business action is initiated through an API or a provider webhook.
2. The API resolves tenant scope, validates invariants, and enters the authorized lifecycle boundary.
3. The authoritative financial state transition is written in PostgreSQL.
4. Required accounting intents are staged into the accounting outbox.
5. If provider cash is externally confirmed against a missing, stale, expired, or mismatched local authority state, the system must stage tenant-scoped suspense accounting intent instead of silently accepting or discarding the external money fact.
6. Integration/projection events are staged through the appropriate outbox path.
7. SQL commit establishes authoritative internal truth.
8. Background workers publish/process derivative messages, projections, and downstream notifications.
9. If external provider execution is uncertain or conflicts with local state, the system records explicit recovery state and blocks unsafe follow-up actions until reconciled.

Important implications:

- Ledger truth is SQL truth, not message-delivery truth.
- Gateway callbacks can confirm or challenge external execution, but they do not bypass the internal lifecycle authority.
- Provider webhook ingress is tenantless at the HTTP host layer when the callback is sent to the shared API hostname. Webhook controllers that derive tenant authority from signed payloads and stored SQL payment/shipment records must be marked `[AllowAnonymousTenant]` so generic tenant middleware does not reject the callback before the webhook trust pipeline runs.
- Outward money movement is gated by readiness checks that intentionally fail closed when critical accounting, compensation, refund, payout, or integration backlog is unresolved.
- Inward external-cash recognition, such as approved vendor repayment proof, is not outward money movement. Already-received cash evidence must be posted into SQL ledger truth with duplicate-evidence, serializable, and financial-safety checks, not blocked behind the outward-transfer readiness gate.
- Scope-less unresolved captured-payment or provider-refund rows are treated as globally blocking external-money ambiguity. If SQL contains unresolved external-money truth with no durable tenant scope, CommerceHub readiness gates fail closed until reconciliation materializes scope or clears the ambiguity.
- Provider-confirmed captured money that cannot yet be recognized into a normal order/payment lifecycle must be routed into tenant-scoped suspense accounting or remain fail-closed unresolved. Manual-review status alone is not sufficient authority containment.
- Scope-less captured-payment compensation outbox rows are also globally blocking external-money ambiguity. A payment-compensation row must either prove affected tenant scope or remain a fail-closed blocker until scope is repaired or the compensation is reconciled. A non-empty base `TenantId` is valid tenant scope for that tenant; explicit affected-store arrays are additive scope, while an empty affected-store array must not broaden a different base tenant into a platform-wide blocker.
- Affiliate payout readiness follows the same external-money ambiguity rule. Affiliate outward money movement must fail closed for scope-less captured-payment compensation, unresolved provider cash, or unresolved provider-refund authority, and affected-store/suspense scope is additive rather than masked by a different base `TenantId`.
- Affiliate payout readiness must also fail closed for critical ambiguous marketplace aggregate events that look multi-store but do not carry explicit affected-tenant arrays, matching CommerceHub outward-money readiness behavior.
- Affiliate payout readiness must use the same non-tenant-prefiltered malformed/ambiguous critical-event scan as CommerceHub readiness. Literal payload markers such as `MarketplaceOrderId` are not proof of safe scope and must not be used to skip unhealthy critical backlog rows before ambiguity detection.
- Affiliate payout readiness is part of the payout authority transaction. It must run inside the same serializable retry scope as balance validation, transfer-evidence allocation, withdrawal creation, commission paid lifecycle rows, accounting, and payout-request reconciliation.
- Affiliate payout transfer evidence is platform cash-rail proof, not per-tenant duplicate truth. A rail namespace plus normalized external payment reference must not be materialized into independent evidence rows for multiple store tenants, and payout-allocation idempotency must remain global per payout reference, because one real-world transfer cannot safely fund duplicate local withdrawal recognition. This boundary must hold in migrations, EF runtime configuration, and settlement-orchestrator evidence lookup.
- Affiliate commission payout lifecycle uses the same SQL net-per-order-item authority as affiliate available balance. Partial refund reversals reduce payable commission and paid lifecycle rows must carry the unreversed remainder; only fully reversed or already paid order items are excluded. If reversal totals exceed approved commission authority, payout must fail closed.
- CommerceHub and affiliate readiness enforce ambiguous marketplace aggregate fail-closed behavior with a separate non-tenant-prefiltered scan over unhealthy critical authoritative outbox, MassTransit bus outbox, and marketplace outbox rows. Tenant-prefiltered scans are still used after that global ambiguity pass for scoped backlog efficiency.
- CommerceHub outward-money readiness is part of the final authority boundary for new refund/payout movement. Payout creation, gateway refund execution claims, and completed payout evidence recognition must run the final readiness read inside the same serializable transaction or claim boundary that records the local authority mutation.
- CommerceHub financial reconciliation reports must prove vendor coverage for every vendor with financial authority at the snapshot boundary. Coverage is derived from suborders, completed refunds, payments, payouts, and direct immutable vendor financial transactions so readiness gates do not fail closed merely because a vendor has ledger-only settlement, adjustment, or direct financial activity.
- POS sale cost basis is authoritative historical financial state. Completed `SalesInvoiceItem` rows must carry immutable `CostPriceSnapshot` and `CostAmountSnapshot` values captured from SQL product/variant cost authority inside the sale workflow. Later product/variant cost mutations affect future valuation only and must not rewrite historical COGS, historical sale margin, accounting journal valuation, or sale-return cost basis.
- POS return cost basis inherits authoritative historical financial state from the original sale line. Completed `ReturnItem` cost snapshots are immutable once written and accounting COGS reversal must derive from those snapshots, not from current product/variant cost.
- Inventory/cost-basis recalculation workers are not financial authority. They may rebuild derived valuation projections, analytics, and reporting snapshots, but replay/retry/reclaim timing must be fenced by SQL idempotency and must not mutate committed sale/return snapshots, historical COGS, immutable accounting journals, or stock movement financial linkage. Fresh SQL readers must never observe partial recalculation state as financial truth.
- Financial projection delivery workers are not financial authority. Profitability, margin, BI/reporting aggregates, valuation dashboards, and other derived financial visibility must be idempotent and freshness-fenced through SQL authority such as `ProcessedEvents` plus projection boundary timestamps. Duplicate, replayed, stale, or out-of-order projection delivery may recompute or refresh derived visibility from immutable SQL sale/return snapshots and accounting journals, but it must not create duplicate profit/margin totals, overwrite newer dashboard state with stale payloads, mutate historical valuation, or alter accounting truth.
- Accounting outbox replay for already journal-linked POS sale/refund messages must complete from existing SQL journal truth when the required revenue/refund and COGS journals are present. It must not re-dispatch through historical invoice/return objects in a way that rewrites immutable cost snapshots or creates duplicate valuation authority.

## Inventory Authority Flow

1. Inventory-affecting operations are requested through backend services.
2. `InventoryConsistencyService` performs authoritative mutation against PostgreSQL stock, reservations, and movement tables.
3. The service records inventory inbox/idempotency state before applying mutation.
4. The service writes authoritative stock movement history.
5. A stock-sync outbox message is created to project updated availability outward.
6. SQL commit establishes the new inventory truth.
7. Read-side consumers and projections update Mongo/search/cache layers asynchronously.
8. Cleanup and reconciliation jobs repair stale reservations, missing commits, missing returns, or lagging stock projections without changing the fact that SQL remained authoritative throughout.

Important implications:

- Customer-facing stock availability can be stale temporarily.
- Reservation cleanup is intentionally conservative and must not release stock when provider cash authority is unresolved.
- A projected stock row is not evidence that stock was authoritatively changed; the SQL movement/reservation history is.
- Stock-consuming writes from storefront, POS, admin, or background workflows must lock and validate SQL stock authority before decrement/reservation. Client inventory visibility and projection availability are advisory only.
- Reservation consumption is stricter than reservation cleanup. Consumption, commit, CommerceHub payment validation, fulfillment replay, and payment recovery may only use active unexpired reservations that are backed by current variant/product/warehouse/stock lineage. Release, expiration, and cleanup paths may still find stale reservations so they can repair them, but stale discovery is not consumption authority.
- Stock-sync outbox replay is a projection trigger only. Before publishing availability outward, replay must re-read current SQL variant semantic ownership, warehouse ownership, stock-row authority, and event freshness; detached or stale messages collapse to completed no-op instead of restoring stale Mongo/Search/cache/CommerceHub stock visibility.
- Affiliate and CommerceHub inventory visibility consumers may use Mongo product documents for display metadata, but availability, `InStock`, stock totals, and stock timestamps must be reloaded from PostgreSQL `ProductVariants`/`ProductVariantStocks` before propagation. Dedicated stock-event consumers must fence duplicate EventId delivery before side effects and reject mismatched SQL version/timestamp authority rather than trusting payload or Mongo freshness.
- Inventory mutation authority requires tenant co-ownership of every referenced warehouse, product variant, stock row, transfer item, reservation, and movement. A valid `TenantId` on the row being saved is necessary but not sufficient when foreign keys can point to tenant-owned records through single-column relationships.
- PostgreSQL S76 inventory hardening enforces this co-ownership for stock rows, movements, and transfers through composite tenant foreign keys where `TenantId` exists, plus constraint triggers for soft-delete, variant-product, and transfer-item checks that cannot be represented as ordinary foreign keys.

## Order Lifecycle

### POS / Storefront Orders

1. Authoritative order creation happens in backend services against PostgreSQL.
2. Initial order, payment, and reservation state are committed atomically where the workflow requires it.
3. `Order.Status` changes are expected to happen only inside an authorized order lifecycle boundary.
4. `PaymentStatus` changes are expected to happen only through `IPaymentLifecycleService`.
5. `FulfillmentStatus` changes are expected to happen only through `IOrderFulfillmentService`.
6. Projection/read-side updates happen asynchronously after commit.
7. For critical storefront order read surfaces, the system may repair a missing or stale Mongo projection from SQL before returning the response.

Important implications:

- Storefront publication/availability state that controls order acceptance is SQL authority, not Mongo/cache projection state.
- Storefront checkout write paths must fail closed unless SQL `StoreConfigs.StorefrontStatus` is `live`.
- Storefront checkout and pricing preview must fail closed when authoritative promotion calculation fails. A customer-visible promotion engine error must not be converted into a successful order with discounts silently removed, because that lets Storefront-visible pricing diverge from POS/Admin pricing and downstream payment/accounting truth.
- Authenticated storefront cart and checkout write paths must re-check current SQL `OnlineCustomer` authority and reject inactive customers before creating order, inventory, payment, affiliate, or cart mutation authority. A valid unexpired JWT is authentication evidence, not sufficient authorization after SQL customer deactivation.
- Client-supplied checkout totals, discounts, shipping amounts, tax amounts, payment method eligibility, and payment split values are stale-client assertions only. Backend order/payment services must recalculate authoritative values, enforce payment-method limits against those values, and reject mismatches before reservation, order insert, payment authority, accounting, or affiliate attribution.
- Customer-visible Storefront payment-method min/max gating should follow the authoritative checkout preview total once pricing is available. Subtotal-only gating is advisory at best because backend limit enforcement runs on final total, not subtotal.
- Payment provider client results and frontend polling state are not terminal payment authority. Backend canonical payment status, lifecycle services, webhook evidence, and reconciliation records decide terminal recognition or unresolved/pending state.
- Storefront canonical payment-status surfaces must not report terminal `FAILED` guidance for suspense-routed, manual-review, amount-mismatch, or late-success quarantine states where provider cash may already exist. Those states remain unresolved authority and must stay pending/manual-review/reconciliation-visible until refund proof or local reconciliation establishes safe terminal failure.
- A checkout that creates new order, inventory, payment, or outbox authority must re-check and lock the SQL `StoreConfigs` row inside the order-creation transaction before reserving inventory or inserting the order.
- The same final storefront order-creation transaction must also re-lock SQL `ShippingMethods` and `TenantPaymentMethods`, row-lock authoritative variant and product checkout rows, recompute authoritative pricing from that locked authority, re-run stale-client pricing and payment-method-limit validation, and persist the order snapshot from that transaction-scoped authority before inventory reservation or order insert continue.
- That final storefront order-creation transaction must rerun coupon and promotion authority from the refreshed in-transaction checkout snapshot and persist the resulting coupon/promotion winner fields from that same final authority. Repricing from a pre-transaction cart snapshot, even after locking shipping/payment/storefront rows, is not sufficient final financial authority.
- That same final storefront order-creation transaction must also reload affiliate commission policy authority for the in-scope products inside the transaction and row-lock any existing policy rows before immutable affiliate commission snapshots are persisted. Reusing pre-transaction affiliate commission policy snapshots is not sufficient settlement authority because commission ownership and reversal math depend on the persisted snapshot.
- Row-locking existing rows is not, by itself, a full set-membership fence for checkout-time discount or affiliate authority. If the final storefront checkout transaction runs below `Serializable`, a concurrent admin insert/activation of a new winning promotion or a previously absent affiliate policy can still land after the in-transaction refresh and before commit. Full commit-time closure for those set-based authorities requires serializable isolation or an equivalent authority-version fence.
- The current storefront implementation closes that set-membership gap by running the final checkout authority boundary inside a serializable SQL transaction with bounded retry on serialization/deadlock races and fail-closed conflict surfacing when the commit cannot safely win.
- If that final transaction-scoped authority changes any persisted financial component, including tax, discount, line total, affiliate commission base, or the winning coupon/promotion identity, the checkout flow must also rebuild the corresponding `OrderItem` money snapshots, immutable affiliate commission snapshots, and persisted order-level discount identity snapshots from the same final pricing authority before commit. Refreshing only the order header is not sufficient because storefront money and refund/commission truth live on order items.
- Idempotent replay of an already committed checkout response may still be served after the storefront is disabled because replay does not create new business authority.
- Shipment services do not own fulfillment truth. Any shipment progress that implies `Confirmed`, `Packed`, `OutForDelivery`, or `Delivered` must call `IOrderFulfillmentService.SynchronizeFromShipmentAsync`; a `FulfillmentDeliveredEvent` must originate from `IOrderFulfillmentService`, not from shipment status mapping.

### CommerceHub Orders

1. Checkout claims idempotency and distributed locks before authoritative mutation.
2. The marketplace order and sub-orders are created in PostgreSQL.
3. Inventory reservations are created in the same authoritative workflow.
4. Marketplace backbone/projection events are staged for downstream read models.
5. Payment initiation persists a durable local payment authority row and a durable provider-session re-init fence before crossing the external provider boundary.
6. Payment and fulfillment lifecycle transitions occur later through protected services.
7. If a provider returns a reusable external identity before the customer-facing payment session is fully materialized, SQL must retain that provider identity and retries must recover against the same external identity instead of creating a replacement provider session.
8. If provider acceptance occurs but durable provider identity persistence remains incomplete, retries stay blocked behind the re-init fence until webhook reconciliation or operator/provider-assisted recovery resolves the ambiguity.
9. Delivered or cancelled outcomes finalize inventory through authoritative backend services.
10. Read models may lag and later be repaired/rebuilt from SQL truth.

Important implications:

- The distributed checkout lock is a liveness aid, not the sole duplicate-order fence.
- Provider-session uniqueness is protected by a durable SQL fence before provider execution, not by best-effort post-failure ambiguity marking alone.
- Browser-authenticated CommerceHub customer cart and order-recovery APIs must resolve customer identity through `CustomerSecurityConstants.AuthenticationScheme`, which accepts the platform's HttpOnly cookie-backed JWT flow. Raw `Authorization` header presence is not a valid prerequisite for evaluating authenticated CommerceHub customer authority.
- CommerceHub `CustomerJwt` validation enforces token expiry, per-token JTI revocation, customer governance status, and a durable per-account cutoff for `CommerceHubCustomers` through `TokensValidAfterUtc`.
- Storefront `StorefrontJwt` and CommerceHub `CustomerJwt` tokens carry standard JWT `iat` plus a precise session-issued-at claim so durable cutoff checks do not depend on second-level timestamp precision alone.
- When a durable customer-session cutoff is set, Storefront and CommerceHub token validation must reject tokens issued at or before that cutoff. Equality is not safe because a copied token minted in the same clock tick or second as the cutoff still predates the post-incident authority boundary.
- CommerceHub customer compromise response must remain targeted through exact-token blacklist plus durable per-account cutoff. If exact-token blacklist persistence fails during logout, the system must fail closed by advancing the account cutoff or by returning service-unavailable rather than silently leaving copied tokens active.
- Provider-specific local prerequisites that can fail deterministically, such as Paymob integration ID parsing and marketplace-order country resolution, must run before the provider accepts reusable order/session identity.
- Once Paymob accepts an order ID, payment-key creation failures are unknown/recoverable provider-session outcomes. Retrying must create a payment key for the accepted Paymob order, not create a second Paymob order for the same local payment authority.
- Inbound CommerceHub provider callbacks that cannot yet be linked to a durable local payment row must still persist unmatched external-money evidence into SQL before the request is allowed to fail retryably. Distributed payment-lock contention or temporary local processing failure is not sufficient reason to rely only on provider redelivery, because unresolved cash/refund truth otherwise has no durable reconciliation anchor.
- The current CommerceHub implementation enforces that rule by persisting a `CommerceHubUnresolvedPaymentWebhook` row even when the payment-initiation lock is busy or its infrastructure is temporarily unavailable. In that degraded branch, the system preserves durable unresolved evidence plus any safe frozen order/store scope and explicitly defers suspense routing, marketplace hold, or payment linkage to reconciliation rather than dropping the callback.
- Customer-facing CommerceHub online checkout must distinguish order creation from payment completion. When checkout returns a payment session with `requires_initiation` or another non-terminal payment state, the UI must preserve a server-authorized initiate/resume-payment path and must not present the order as fully completed payment authority.
- Customer-facing CommerceHub payment initiation must honor the authoritative provider handoff contract. Some providers complete through a redirect URL and others through a provider token or client secret; the UI must preserve a server-authorized continue-payment path for either shape instead of treating non-redirect sessions as initiation failure.
- CommerceHub checkout must validate the exact requested online provider against every store's enabled payment-method authority. A tenant having some `SupportsOnline` method is not sufficient proof that `PAYMOB` and `STRIPE` are interchangeable.
- For CommerceHub checkout, zero enabled `TenantPaymentMethod` rows is a fail-closed "provider unavailable" state, not a permissive compatibility fallback. Absence of store payment-method authority must not be interpreted as support for COD or any online provider.
- CommerceHub cart pricing for a multi-store checkout currently carries the authoritative payment-method capability contract for that exact store set. The customer payment selector must consume that backend capability surface; a local hardcoded provider list is not authority.
- CommerceHub payment initiation is not a public lookup surface. Creating or reusing an online payment session must require authenticated customer ownership or a signed order-access token bound to the marketplace order before order/payment metadata or provider-session details are returned.
- When CommerceHub browser recovery uses signed order-access authority, that authority may arrive through an explicit request token or the server-issued HttpOnly recovery cookie. Controllers may normalize either source before fail-closed access checks, but service-layer authorization must still re-check against SQL order authority after order resolution and before any payment-session reuse or creation returns metadata.
- Successful non-COD CommerceHub checkout must durably persist the signed order-access authority into a server-issued HttpOnly `/api/commercehub` recovery cookie before the frontend clears transient state. The raw token does not need to remain in JS-visible response payloads once that cookie is issued.
- Browser CommerceHub payment recovery is safe only when the durable recovery channel keeps secret order-access authority in the HttpOnly cookie and keeps only non-secret active-order identity in browser session storage for reload-safe routing.
- That browser recovery authority is continuity-only between auth boundaries. Customer login, register, logout, and revoke-all-sessions must clear prior anonymous CommerceHub recovery cookies and visible browser recovery context so a shared browser cannot carry one customer's order-access authority into another session.
- Because the recovery cookie is server-issued and HttpOnly, auth-boundary scrubbing must not depend on the customer auth cookie still being present. Explicit logout remains responsible for expiring anonymous recovery authority even after the auth cookie has already expired or been deleted.
- Even when the server-owned recovery cookie may remain temporarily valid for anonymous continuity, CommerceHub client session-clear paths should still drop the visible `activePaymentRecoveryContext` on real auth loss so shared or expired-auth browsers do not keep advertising stale order identity after customer authority disappears.
- Any anonymous CommerceHub order-recovery surface must expose backend-derived retry/manual-review authority directly through the API contract, such as `CanRetryPayment` and `PaymentRecoveryMessage`. Generic `Pending`, `OnHold`, or provider labels are not enough to decide whether payment may be safely resumed because unresolved captured-money and suspense states are first-class blockers.
- A CommerceHub marketplace order with unresolved provider-success evidence, suspense-routed captured cash, or pending suspense-clearance reconciliation is not a retryable customer payment state. Customer order-detail surfaces and payment-initiation services must block retry until reconciliation clears the captured-money ambiguity.
- A CommerceHub pending payment blocked by `PROVIDER_SESSION_REINIT_FENCE`, provider-session reconciliation-required state, or another backend recovery fence is also not a retryable customer payment state even if generic lifecycle state still reads `Pending`. Authenticated order details, public recovery pages, and any browser recovery-context persistence must follow backend `CanRetryPayment` / `PaymentRecoveryMessage` authority instead of remembering continue-payment from `Pending` alone.
- Customer-visible CommerceHub retry guidance must be derived from the same provider-session recoverability classification that the initiation boundary enforces. A narrower UI contract is not sufficient if initiation still treats broader provider-session persistence gaps, such as outcome-unknown or accepted-provider-identity-with-missing-handoff states, as blocked reconciliation authority.
- The current CommerceHub implementation enforces that rule through the shared `CommerceHubPaymentSessionRecoveryAuthority` classifier, which both initiation and browser-facing recovery authority now consume.
- Customer-critical CommerceHub order detail must load SQL order authority before validating signed order-access tokens that depend on order metadata such as `OrderNumber`; Mongo projection presence is not proof that the order exists or that access validation may safely skip SQL-first authority.
- Clearing local CommerceHub cart state after checkout is acceptable only after SQL order creation succeeds and the UI still retains authoritative order/payment recovery state through the dedicated recovery page or authenticated customer order detail. Cart cleanup must not erase the continue-payment path or convert a non-terminal payment state into terminal customer-visible success.
- Local CommerceHub cart persistence is customer-session intent only. A persisted browser cart snapshot must be scoped to `guest` or the exact authenticated CommerceHub customer, and authenticated CommerceHub cart bootstrap must wait for customer auth hydration plus scope convergence before acting on persisted state. A prior authenticated customer's snapshot must not be merged into another customer's persisted server cart, and an authenticated server cart must remain authoritative except for an explicit one-time guest-to-auth adoption path.
- If a long-running checkout outlives its soft lease, the cart `Version` concurrency token plus transactional cart deletion are expected to force the stale worker to lose commit rather than persist a second marketplace order from the same cart snapshot.
- CommerceHub provider-success webhooks that cannot be matched to a paid local payment row must either route every financially affected tenant to suspense accounting and place the order on hold, or remain unresolved and globally fail closed for outward money readiness.
- When a suspense-routed CommerceHub provider-success webhook is later matched to a paid local payment row, suspense clearance intent and marketplace hold release must be committed in the same local authority transaction before the webhook is considered resolved.

## Accounting Lifecycle

1. A money-affecting state transition occurs through an authorized lifecycle service.
2. The service determines the accounting effect required by the mutation.
3. Accounting outbox records are staged from the authoritative SQL workflow.
4. SQL commit makes the business mutation durable.
5. Accounting outbox processing posts immutable ledger entries in PostgreSQL.
6. Financial reporting, treasury reads, and downstream financial consumers must treat the ledger as the source of truth.
7. If accounting outbox processing is stale or failed for critical event types, CommerceHub outward money movement is intentionally blocked until repaired.
8. Failed critical accounting outbox rows remain live recovery/readiness blockers and must not be removed by time-based cleanup before posting or explicit reconciliation.
9. Any accounting outbox event type that posts money, cash, liability, supplier/customer debt, expense, refund, purchase, settlement, payout, or adjustment ledger effects is critical financial truth until posted or explicitly reconciled. Critical-retention rules must be derived from the full dispatch table, not from a partial hand-maintained subset.

Important implications:

- The system accepts asynchronous journal processing but not alternate financial truth.
- The absence of an immediately updated read model is not a financial integrity bug if ledger/outbox recovery semantics remain intact.
- Direct mutation of payment state without the lifecycle service is intentionally blocked because it would bypass ledger-first recognition.
- POS sale and purchase mutation hardening may remove only unclaimed, unattempted, unfailed pending accounting intent before the source mutation changes. Claimed, retrying, attempted, failed, or error-bearing accounting outbox rows are live ledger recovery evidence and must block mutation until posted or explicitly reconciled.
- POS sale and purchase mutations that revert and reapply stock/accounting effects must lock the invoice authority row and carry a monotonic `AccountingMutationVersion` into accounting outbox payloads so stale messages cannot post against a later document snapshot.
- POS financial writes must prove tenant co-ownership for every referenced party/account edge before commit. Sales invoices, purchase invoices, invoice lines, account ledgers, customer/supplier transactions, supplier payments, and financial transactions cannot attach current-tenant authority to another tenant's customer, supplier, warehouse, product, variant, unit, purchase invoice, sales representative, or financial account.
- POS sale COGS posting must prefer immutable sale-line cost snapshots over current product/variant cost. Replay, duplicate accounting outbox delivery, analytics rebuild, projection repair, and reconciliation may recompute derived state from authoritative history, but they must not recalculate historical COGS from the latest product cost or mutate committed sale cost snapshots.

## Payout Lifecycle

1. A payout request is created idempotently in PostgreSQL.
2. Readiness gates verify that accounting, compensation, unresolved refunds, unresolved captured payments, payout backlog, and integration backbones are healthy enough to authorize a new outward transfer.
3. The payout is created as `Pending`.
4. Transition to `Processing` is the authorization point for external transfer handoff.
5. External transfer execution occurs outside the SQL transaction boundary.
6. Completion evidence or non-execution proof is later reconciled back into PostgreSQL.
7. `Completed` applies the vendor balance effect and ledger consequence in authoritative local state.
8. If the external transfer may already have happened but local completion certainty is missing, the payout can enter `ReconciliationRequired`.
9. Background reconciliation quarantines stale `Processing` payouts into `ReconciliationRequired` rather than silently failing them.

Important implications:

- `Processing` and `ReconciliationRequired` are intentional truth states, not accidental limbo.
- Completion after `Processing` or `ReconciliationRequired` is a reconciliation action for an already-authorized external handoff and must not be blocked by the same readiness or vendor-governance gate that blocks authorizing a brand-new payout.
- Failure after `Processing` or `ReconciliationRequired` is safe only with durable non-execution proof and must not be blocked by vendor governance that changed after external handoff.
- External payout evidence (`ExternalReferenceId` and `BankTransactionId`) is platform-cash-rail evidence and must be globally unique across non-failed CommerceHub payouts, not merely unique inside a vendor tenant.
- Affiliate payout evidence is first-class SQL authority: every local affiliate withdrawal must allocate a rail-scoped `AffiliatePayoutTransferEvidence` row, and allocation totals must not exceed the externally confirmed transfer amount/currency.
- Super Admin completed payout execution is a local recognition of externally confirmed vendor money movement. Because it creates completed payout, ledger, audit, and earning-allocation authority in one step, the final CommerceHub readiness gate must run inside the serializable transaction before those rows are persisted. Vendor suspension/governance may block pre-transfer authorization, but must not block recording SQL truth after external evidence proves the transfer already executed.
- Super Admin payout queue/read models must not advertise a vendor payout as executable when any payout for that vendor is already `ReconciliationRequired` or stale `Processing`. Those are outward-money authority blockers until reconciliation resolves them, even if remaining payable math is still positive.
- The canonical Super Admin payout queue contract must expose backend-derived execution authority directly, including an allow/deny decision plus explicit block code/reason, so frontend enablement never re-infers payout readiness from local payable math or risk-flag subsets.
- Super Admin payout queue pagination metadata is part of operator authority. Any operator-visible payout summary card or queue headline must either consume backend-supplied global totals across the full queue or explicitly label itself as page-scoped. Silent first-page truncation is not acceptable on a payout execution surface.
- Super Admin payout queue money summaries are authoritative only within a single known currency. Backend queue totals must be grouped by currency, mixed-currency queues must fail closed into per-currency summaries, and frontend/operator surfaces must never fabricate one combined payout total across different currencies or label a mixed aggregate with a hardcoded currency code.
- Super Admin completed-payout recognition requires immutable external execution evidence. `TransferReference` is descriptive audit context only; the authority mutation must carry at least one of `ExternalReferenceId` or `BankTransactionId`.

## Refund Lifecycle

1. Refund creation and refund completion are PostgreSQL-authoritative for internal order, payment, vendor earning, platform cash, and ledger effects.
2. Provider or manual external refund execution is outside the SQL transaction boundary and must be modeled as execution evidence, unknown outcome, or reconciliation-required state until local financial recognition is durably completed.
3. `CommerceHubRefundStatus.ReconciliationRequired` is the explicit refund authority state for external refund execution that may already have happened while local financial recognition is incomplete.
4. Manual refund payout confirmation is external-money evidence. Once an operator confirms that money was already sent and supplies a transfer reference, later local persistence, ledger, or projection failure must not convert that refund to a terminal safe-failed state.
5. Externally confirmed or accepted but locally incomplete refund payouts must continue blocking duplicate refund authorization and outward money movement until the transfer reference or provider outcome is reconciled into local financial truth.
6. Refund reconciliation recovery must resume from persisted execution evidence and authoritative order/refund inputs. If persisted immutable refund breakdown fields are inconsistent, recovery must derive a normalized ledger snapshot instead of mutating immutable refund history in place.

Important implications:

- A failed local write after confirmed manual refund transfer is not proof of external non-execution.
- Refund `Failed` is safe only when external non-execution is known/proven or when external handoff never occurred.
- `Processing` and `ReconciliationRequired` refunds are intentionally counted as active refund capacity and readiness blockers.
- Manual refund transfer references are customer-money rail evidence and need the same reconciliation discipline as payout transfer evidence.
- Refund-induced CommerceHub payment-status changes are payment lifecycle mutations. The payment lifecycle boundary must remain active until those changes and their ledger effects are persisted, including recovery paths that resume a previously persisted refund.
- Post-payout refund execution is outward money movement and remains blocked until CommerceHub financial ledger entries that affect the vendor/platform have accounting coverage or explicit reconciliation.
- Gateway refund execution must perform the final CommerceHub readiness gate inside the serializable refund-claim transaction before moving the refund into processing and before crossing the external provider boundary.

## Synchronization Model

The system uses multiple asynchronous propagation paths:

- MassTransit EF outbox for authoritative integration events
- accounting outbox for ledger/journal work
- marketplace event outbox for CommerceHub read/backbone propagation
- stock sync outbox for inventory read-model propagation
- payment compensation outbox for captured-payment recovery/refund compensation
- webhook logs/inboxes and processed-event records for inbound idempotency and replay handling
- hosted reconciliation/repair workers for gap closing

Synchronization is therefore:

- atomic from business mutation to local outbox intent
- asynchronous from local outbox intent to downstream projection/update visibility
- explicitly repairable when a downstream consumer or provider callback fails

## Eventual Consistency Assumptions

- MongoDB read models can lag behind PostgreSQL and still be correct by design.
- Redis caches can be stale and are expected to be invalidated or repopulated later.
- Elasticsearch search documents can lag behind catalog/inventory truth.
- CommerceHub and Storefront customer views may briefly observe older state after SQL truth has advanced.
- Critical storefront order reads are allowed to self-heal from SQL synchronously before serving.
- Storefront order detail, authenticated tracking, guest tracking, and order-by-number endpoints must resolve minimal SQL authority metadata first, then trigger SQL-backed projection repair before any projection-derived `404`, session check, or ownership check that would otherwise treat Mongo absence as order absence.
- If SQL proves the storefront order exists but the Mongo projection still cannot be served after repair, customer-critical storefront order reads must fail closed as temporary unavailability rather than return a false `404`.
- Storefront account order-history pagination now resolves total count and requested page ids from SQL authority, repairs that exact page before Mongo is trusted, and fails closed as temporary unavailability if Mongo still cannot prove the requested SQL page after repair.
- Critical CommerceHub order detail reads are allowed to self-heal from SQL synchronously before serving.
- A missing CommerceHub Mongo order document is not sufficient proof that the order does not exist. Any projection-dependent `404`, stale response, or token/ownership check that depends on projected fields must either read SQL authority directly or trigger targeted SQL-backed repair first.
- If duplicate Mongo order documents survive a historical race or index-creation failure, critical storefront and CommerceHub order reads and repair paths must select the freshest projection by authoritative freshness markers such as source-mutation ticks, event version, event timestamp, and projection update time rather than trusting the first matching document returned by Mongo.
- Storefront product/category/coupon/promotion read models do not currently have the same synchronous SQL-backed repair path as order detail. Their recovery depends on projection consumers, cache invalidation, and explicit SQL-backed admin rebuild flows for coupon/promotion projections.
- `Promotion.ProjectionMutationVersion` is the durable storefront-overlay replay fence. Activation, expiration, repair, and rebuild flows must preserve that monotonic SQL mutation authority into read-model freshness markers such as `PromotionDocument.LastEventVersion`, and any stale activation replay that arrives after a newer expiration must fail closed regardless of delivery order.
- Promotions remain storefront-only overlays. Visible storefront price, category-banner visibility, search decoration, and cart promotion snapshots must be recomposed from authoritative SQL base pricing plus the current active overlay set; replay payloads and cached promotion state are derivative hints only. Affiliate numeric pricing stays canonical/base-only and CommerceHub numeric pricing stays canonical/channel-owned.
- Product search indexing and search repair may use Mongo product documents as shape/input, but before indexing pricing-visible fields they must apply the SQL authority overlay for current product/variant base price, variant metadata, and current SQL promotion decoration. Search is never the source of base price or overlay truth.
- Affiliate public product projection may preserve projected stock freshness, but numeric product/variant pricing must be overlaid from SQL before publishing or indexing. Promotion events may refresh affiliate visibility, but storefront promotion overlays must not become affiliate numeric price authority.
- CommerceHub `VariantPriceUpdatedEvent` is a trigger, not price authority. CommerceHub fallback-price synchronization must re-read the current SQL variant price and reject the event when its version does not match SQL `ProductVariants` mutation authority, even if the payload carries a newer or corrupted price.
- Derivative pricing side-effect consumers may use active `ProjectionConsumerEventFence` claims to collapse overlapping duplicate delivery before cache invalidation, search indexing, affiliate projection, or CommerceHub synchronization. Durable `ProcessedEvents` completion remains reserved for the consumers whose projection freshness contract owns replay completion.
- Promotion visibility events are triggers only. Hardened promotion projection, search, affiliate, CommerceHub, cache, and repair paths must acquire a duplicate-delivery fence before derived side effects, reload current PostgreSQL promotion/product/variant truth before writing visible overlays, and reject stale or future promotion mutation versions through shared authority validation.
- Duplicate promotion projection delivery for the same event id and mutation version is not stale authority. Hardened promotion consumers use active event fencing to collapse overlapping duplicate workers, while later same-event recovery replay may still recompute deterministic product/category/search/cache visibility from SQL truth after partial projection loss. That recovery replay must not create a second overlay owner or bypass stale-version fencing.
- Operator/admin financial actions may be blocked during reconciliation uncertainty; this is an accepted integrity tradeoff.
- External provider truth and local SQL truth can temporarily diverge during webhook delay, retry, or outcome uncertainty; the accepted resolution path is compensation or reconciliation, not silent overwrite.

## Async Boundaries

The following are intentional async boundaries:

- SQL authority commit to projection/update visibility in MongoDB
- SQL authority commit to cache invalidation propagation
- SQL authority commit to Elasticsearch indexing
- SQL authority commit to downstream integration event consumption
- payment/refund/payout local state to external provider callbacks/evidence
- compensation request creation to external refund execution/reconciliation
- payout `Processing` handoff to final bank/provider completion evidence

Crossing an async boundary means immediate read-after-write consistency is not guaranteed unless a specific repair path exists.

## Compensation And Recovery Strategies

- Outbox retries are used for transient downstream delivery failures.
- Critical authoritative outbox failures are not simply discarded; they remain visible for replay or operator reconciliation.
- Critical accounting outbox failures are retained in the live outbox instead of being aged out by failed-row cleanup, so missing ledger intent cannot silently disappear from readiness gates.
- Marketplace projection terminal failures prefer replaying the original serialized payload. Rebuild from current SQL state is a fallback when the original payload is unusable.
- Unpublished failed marketplace outbox rows are retained as live readiness blockers and copied to a durable dead-letter archive for audit; cleanup may delete only published or compensated rows after retention.
- Stock sync terminal failures emit a fresh authoritative SQL stock snapshot event when replay is still possible.
- If authoritative source data no longer exists for a failed stock projection, the failed row may be quarantined/marked compensated to unblock later events, with audit trace.
- Coupon and promotion projection recovery uses direct SQL-backed rebuilds for admin repair rather than replaying historical authoritative events from a clean scope.
- Promotion repair/rebuild must preserve replay-fencing metadata, declare SQL authority through `SqlTruthRebuildGuard`, and recompute storefront overlay visibility from current SQL truth. Rebuild is not allowed to resurrect expired overlays, stale category banners, stale cached cart promotion state, or stale search decorations from historical activation payloads.
- Same-event duplicate promotion rebuild/replay is allowed to refresh derivative visibility from SQL truth only when the consumer/rebuild contract permits it. It must remain idempotent over overlay ownership, category banner state, search decoration, cache state, and processed-event completion while stale lower-version activation replay after expiration still fails closed.
- Captured-payment compensation outbox can trigger refund recovery flows when external cash succeeded against an incompatible local state.
- Refund recovery workers revisit `Processing` or unknown-outcome refunds rather than guessing completion, and reconciliation-required refunds remain blocked until confirmed locally or failed with external non-execution proof.
- Payout reconciliation workers move stale `Processing` payouts into `ReconciliationRequired` for explicit follow-up.
- Storefront and CommerceHub projection reconciliation services rebuild missing/stale read models from SQL.
- Inventory consistency and reservation cleanup jobs repair open reservations, expired reservations, missing delivery commits, and return discrepancies.

## Transaction Boundaries

- The primary transaction boundary is PostgreSQL.
- Business mutation plus local outbox staging should be atomic inside the same SQL transaction or equivalent EF unit of work.
- Authoritative integration events are expected to be published before the source mutation scope ends so the outbox row is atomic with the business state.
- External provider calls are inherently outside the local SQL commit boundary.
- Because external provider execution is outside the local transaction boundary, the system relies on idempotency, compensation, and reconciliation rather than distributed transactions.
- Inventory finalization and payment lifecycle mutation are intended to be committed together where the lifecycle requires shared atomicity.
- Ledger posting is authoritative in PostgreSQL even when triggered asynchronously from a prior committed business event.

## Idempotency Guarantees

- Checkout and payout/refund creation flows use idempotency keys for client/API retry safety.
- Webhook processing uses idempotency protections including durable logs and duplicate detection.
- Inventory mutation workflows record event inbox state and reject conflicting replays.
- Outbox consumers and projection handlers are expected to be idempotent.
- Processed-event tracking exists so the same delivered message can be safely ignored after successful handling.
- External transfer/refund handoffs use deterministic correlation or provider idempotency keys to reduce double execution risk.
- Idempotency is intended to guarantee safe replay, not instantaneous consistency.
- Idempotency cleanup may remove only unbound/uncompleted claim records owned by the current claim token. Once a request is bound to SQL business authority, cleanup must preserve that binding and rely on replay, lifecycle compensation, or reconciliation rather than deleting the business row.
- Serializable retry paths that call `ChangeTracker.Clear()` must reload or explicitly reattach their owned idempotency-claim rows before they rely on in-transaction `SaveChanges` to preserve the durable bind. Mutating a detached claim object is local memory only and does not satisfy the "business authority plus replay bind commit together" guarantee.
- Storefront checkout now enforces that rule through `CheckoutRequestIdempotencyCoordinator.BindOrderCreatedForCommitAsync(...)`: the final transaction binds `OrderId` / `CompletedAtUtc` only after coordinator-owned claim rehydration or tracked-entity reuse has re-proved claim-token ownership inside the current EF unit of work.

## Event Ordering Assumptions

- Ordering is only assumed per aggregate where event versioning is explicitly attached.
- Authoritative integration events carry aggregate ordering metadata and event version.
- Consumers may reject stale events by comparing event version or freshness markers.
- Stock projection freshness markers are normalized to UTC millisecond precision before cross-store persistence or comparison, because MongoDB read models truncate sub-millisecond SQL/.NET timestamp ticks.
- Critical outbox publishing preserves older-message-before-newer-message behavior for the same aggregate where configured.
- Marketplace and stock outbox processors intentionally enforce same-stream head-of-line blocking, while batch selection across unrelated aggregates may use time-based tie-breakers instead of a global version order.
- No global total order is assumed across tenants, aggregates, or subsystems.
- Rebuilt or compensated projection events are intended to preserve semantic correctness or advance to a fresh authoritative snapshot, not to recreate a system-wide historical total order.

## Reconciliation Mechanisms

- storefront order projection repair
- storefront order projection reconciliation background sweep
- CommerceHub projection reconciliation background sweep
- stock sync compensation and replay
- marketplace outbox compensation and replay
- accounting outbox health checks and replay/repair
- payment compensation outbox worker
- global fail-closed readiness checks for scope-less captured-payment compensation
- affiliate payout fail-closed readiness checks for scope-less unresolved external-money rows and additive affected-store/suspense scope
- unresolved webhook review and follow-up processing
- refund recovery hosted service
- payout reconciliation hosted service
- CommerceHub on-demand order projection repair for customer-critical order detail
- inventory consistency and reservation cleanup jobs
- readiness gates that prevent additional outward movement until existing unresolved authority gaps are cleared
- external-outcome readiness health checks for payouts, refunds, unresolved provider webhooks, and carrier registration recovery
- Health endpoints are infrastructure liveness/readiness surfaces and must remain outside tenant, CSRF, storefront publication, affiliate referral, and rate-limit subject middleware. TestSprite and local deterministic runners depend on `/health/live` returning quickly without business-context resolution before runner endpoints are exercised.
- STOCK-CON-01 deterministic runner coverage for last-unit concurrent stock deduction, fresh-reader visibility safety, duplicate/stale stock event delivery, and storefront/affiliate/CommerceHub convergence from SQL inventory truth
- STOCK-CON-02 deterministic runner coverage for same-request replay during an active last-unit stock mutation, proving replay timing does not amplify SQL stock deduction, stock movements, reservations, fulfillment truth, or derived storefront/affiliate/CommerceHub availability
- STOCK-CON-03 deterministic runner coverage for duplicate inventory worker delivery during active stock-sync processing, proving duplicate outbox workers and duplicate/stale stock projection delivery cannot mutate SQL stock or movements, resurrect storefront/affiliate/CommerceHub availability, duplicate processed-event/inventory-inbox state, or prevent SQL-truth rebuild/reconciliation convergence
- STOCK-CON-04 deterministic runner coverage for same-request replay during active sales-return stock restoration, proving replay timing cannot restore SQL stock twice, duplicate sales-return stock movements, drift historical returned-quantity boundaries, duplicate accounting reversal linkage, resurrect stale storefront/affiliate/CommerceHub availability, or prevent SQL-truth rebuild/reconciliation convergence
- STOCK-CON-05 deterministic runner coverage for duplicate CommerceHub return delivery during active return receipt confirmation, proving webhook/delivery timing cannot restore SQL stock twice, duplicate CommerceHub return movements, drift historical returned quantity, duplicate refund/reversal linkage, resurrect stale storefront/affiliate/CommerceHub availability, or prevent SQL-truth replay/rebuild/reconciliation convergence
- STOCK-CON-06 deterministic runner coverage for same-request replay during active purchase-return stock deduction, proving replay timing cannot deduct SQL stock twice, duplicate purchase-return deduction movements, drift historical purchased/returned quantity boundaries, duplicate supplier reversal/refund/accounting linkage, corrupt storefront/affiliate/CommerceHub availability, or prevent SQL-truth replay/rebuild/reconciliation convergence
- STOCK-CON-07 deterministic runner coverage for concurrent different-request sales returns against the same historical sold quantity, proving active return timing cannot mint extra returnable quantity, restore SQL stock twice, duplicate return movements or refund/reversal linkage, expose partial restoration visibility, resurrect stale storefront/affiliate/CommerceHub availability, or prevent SQL-truth replay/rebuild/reconciliation convergence
- VAR-CON-01 deterministic runner coverage for concurrent creation of the same variant attribute combination, proving active create timing cannot split authoritative variant identity, duplicate SKU/barcode ownership, orphan inventory or pricing linkage, expose uncommitted variant visibility, duplicate processed-event completion, or allow replay to mint an additional SQL variant. Storefront, affiliate, CommerceHub, cache, and search projections remain derivative and converge from SQL variant truth.
- VAR-CON-02 deterministic runner coverage for same-request replay during active variant attribute mutation, proving replay timing cannot split authoritative variant identity, corrupt `AttributeCombinationKey` truth, duplicate SKU/barcode ownership, orphan inventory or pricing linkage, expose partial mutation visibility, duplicate processed-event completion, or allow storefront/affiliate/CommerceHub projections to override SQL variant truth.
- VAR-CON-03 deterministic runner coverage for duplicate variant projection delivery during active projection processing, proving duplicate/out-of-order projection timing cannot split authoritative variant identity, overwrite fresh `AttributeCombinationKey`/SKU/barcode visibility with stale values, create orphan inventory or pricing linkage visibility, duplicate processed-event completion, resurrect deleted or stale projection state, or prevent storefront/affiliate/CommerceHub replay, rebuild, and reconciliation from converging from SQL variant truth.
- VAR-CON-04 deterministic runner coverage for concurrent SKU reassignment race safety, proving active reassignment timing cannot create dual SQL SKU ownership, stale SKU overwrite, SKU split-brain, inventory/pricing/fulfillment linkage drift, partial external visibility, duplicate processed-event completion, stale losing projection replay, or replay/rebuild/reconciliation drift. Storefront, affiliate, CommerceHub, cache, search, and sync projections remain derivative visibility consumers and converge from PostgreSQL `ProductVariants` truth.
- VAR-CON-05 deterministic runner coverage for variant deletion/archive during active inventory mutation, proving deletion timing cannot become inventory authority, active inventory mutation fails closed after SQL variant deletion, inventory/pricing/fulfillment/stock-movement linkage remains attached to PostgreSQL truth, fresh readers see no uncommitted deletion or orphan visibility, stale stock/create/delete replay cannot resurrect deleted variant visibility, and rebuild/reconciliation converge from PostgreSQL `ProductVariants` truth. Storefront, affiliate, CommerceHub, cache, search, and sync projections remain derivative visibility consumers only.
- VAR-CON-06 deterministic runner coverage for out-of-order variant projection propagation, proving a newer SQL variant mutation/version remains authoritative after V11 is delivered to storefront/affiliate/CommerceHub/search/cache visibility before stale V10 replay arrives. Freshness/version fencing, processed-event uniqueness, and SQL-truth rebuild/reconciliation must prevent stale `AttributeCombinationKey`, SKU, barcode, storefront, affiliate, CommerceHub, search, or cache visibility from overwriting the newer PostgreSQL `ProductVariants` truth.
- VAR-CON-07 deterministic runner coverage for stale external marketplace variant replay, proving an old marketplace V10 payload replayed after V11 SQL truth and V11 storefront/affiliate/CommerceHub/marketplace/search/cache visibility cannot overwrite authoritative variant state. Marketplace product events are non-authoritative triggers only; marketplace projection outbox, marketplace read models, storefront, affiliate, CommerceHub, search, and cache visibility must rebuild from current PostgreSQL `ProductVariants` truth with entity-mutation/version freshness fencing and processed-event idempotency.
- VAR-CON-08 deterministic runner coverage for duplicate variant projection delivery during active processing, proving the same authoritative projection event delivered to Worker A and Worker B concurrently cannot create duplicate storefront, affiliate, CommerceHub, search/index, cache, SKU, barcode, `AttributeCombinationKey`, media, or processed-event visibility. Processed-event uniqueness, freshness/version fencing, stale duplicate replay fail-closed behavior, and SQL-truth replay/rebuild/reconciliation must keep PostgreSQL `ProductVariants` as the only variant synchronization authority.
- VAR-CON-09 deterministic runner coverage for variant media synchronization corruption proves V10 media replay after V11 visibility, duplicate V11 delivery overlap, and stale delete/replacement replay cannot corrupt PostgreSQL variant/media ownership or resurrect stale media in storefront, affiliate, CommerceHub, search/index, cache/CDN visibility.
- VAR-CON-09B runtime hardening makes that media authority rule an enforced production invariant: `ProductMediaChangedEventConsumer` and media-bearing `ProductVariantUpdatedEvent` handling must acquire SQL `ProcessedEvents`/PostgreSQL advisory duplicate fences before side effects, re-read current SQL media/variant authority before applying payloads, reject stale or future/corrupted media event versions, and collapse duplicate EventId delivery before search/cache side effects. `ProductVariants.MediaId` is constrained to same-tenant/same-product `ProductMedia` through a composite database FK/alternate key, media removal explicitly unlinks variant media before deleting the media row, and product search reindex overlays media from SQL instead of treating Mongo product media as rebuild authority. PostgreSQL `ProductVariants` plus `ProductMedia` ownership remains the only media linkage authority; product media events, CDN/cache/search timing, affiliate/storefront/CommerceHub projections, and replay/rebuild/reconciliation are derivative visibility only and must converge from current SQL truth.
- The 2026-05-19 VAR-CON hardening review extends the runtime rule beyond media: variant stock and delete side-effect consumers, affiliate variant projections, CommerceHub variant synchronization, and search/index refresh consumers must acquire `ProjectionConsumerEventFence` before derived side effects when a durable `ApplicationDbContext` is available. Variant stock side effects must also re-read SQL stock authority and require the event's product/variant version plus stock timestamp to match the current SQL row before propagating availability, so a higher-version corrupted payload or stale replay timestamp fails closed. Search/index rebuild and refresh paths must apply `ProductSearchSqlAuthorityOverlay` before mapping Mongo product documents to Elasticsearch so PostgreSQL remains authority for variant/media visibility, SKU/barcode, stock, default variant fields, and tombstones. Default-variant ownership and one live attribute name per variant are now migration-enforced database invariants, with migration preflights that fail closed on existing duplicates.
- CHECKOUT-CON-04 runtime hardening makes active duplicate checkout overlap collapse explicit on the CommerceHub path: `CommerceHubCheckoutService` acquires a customer-cart checkout lock before the idempotency-key lock, claims the SQL cart under serializable row-locking authority, cleans failed cart-claim idempotency rows, and still composes marketplace order, promo usage, shipping, payment-method, inventory reservation, and outbox state from SQL inside the final serializable transaction. Storefront checkout continues to rely on its pre-existing cart-scoped execution lock, durable request-hash/idempotency coordinator, single-active-order-per-cart database invariant, and final serializable checkout authority transaction.
- CHECKOUT-CON-09 adds the payment replay after checkout finalization rule: provider callbacks, payment recovery, legacy callback fallbacks, redirects, refunds, payouts, fulfillment release, and accounting/payout propagation are subordinate to current SQL order/payment/reservation authority after checkout finalization. Runtime hardening now prevents the Paymob legacy webhook fallback from minting a new pending CommerceHub payment row unless it first recomposes current SQL initiation eligibility and exact active reservation authority. If SQL says the order is already paid, non-retryable, or reservation authority is gone, the callback path refuses stale payment authority and lets captured external evidence flow into unresolved/suspense reconciliation instead of recreating a payment lane.

## Accepted Consistency Tradeoffs

The following are intentional and should not be flagged as vulnerabilities on their own:

- SQL truth can be correct before MongoDB, Redis, Elasticsearch, or downstream event consumers reflect it.
- Customer-facing read surfaces can show stale data temporarily after an authoritative commit.
- Critical read surfaces may repair a projection from SQL at request time instead of requiring globally synchronous projection completion.
- External gateway or bank truth can temporarily be ahead of local SQL certainty; the system models that gap explicitly and reconciles it later.
- Refunds may remain in `Processing`, unknown-outcome, or `ReconciliationRequired` territory to avoid double refunding when external truth is uncertain.
- Payouts may remain in `Processing` or `ReconciliationRequired` to avoid falsely declaring success or failure without evidence.
- Readiness gates may still block tenants when actual unresolved financial or backbone state affects their scope; large prefiltered backlogs are scanned and warned, not automatically blocked by a fixed inspection-window overflow.
- Projection compensation may rebuild from current authoritative SQL when the original payload is corrupted or unusable, with audit trace, rather than dropping the event.
- At-least-once delivery is accepted and duplicate handling is expected downstream.

These tradeoffs are intentional because the system prefers integrity, replay safety, and explicit reconciliation over false certainty or unsafe availability.

## Non-Authoritative Components That Must Not Be Misclassified

The following must never be treated as the source of truth for audit conclusions:

- MongoDB order/content/product/search documents
- Redis cache contents or lock state
- Elasticsearch documents
- RabbitMQ queue depth or delivery success alone
- frontend-visible state in Storefront, CommerceHub, or admin SPAs
- webhook arrival timing by itself
- absence of immediate projection freshness immediately after SQL commit

## Audit Interpretation Guidance

Future audits should evaluate incidents and risks against these rules:

- A stale read model after SQL commit is not automatically a broken authority model if recovery and reconciliation paths exist and SQL truth remains intact.
- A blocked refund/payout/checkout path under readiness gating is not automatically an availability bug; it may be the intended integrity-preserving behavior.
- A reconciliation-required state is not a missing design decision. It is the design decision when external execution certainty is incomplete.
- A direct state write path that bypasses lifecycle services or interceptors is an architectural violation and should be treated as a real defect.
- Any attempt to elevate MongoDB, Redis, Elasticsearch, or message delivery into authoritative financial or inventory truth is a real architectural error.

## 2026-05-21 Final Maturity Review Conclusions

- No new inspected catastrophic stale-authority corruption path was validated in the hardened checkout/payment/fulfillment or product/search/publication slices during the final maturity review.
- The dominant remaining risk class is governance and operational drift around already-hardened primitives, not a broken SQL authority model.
- A failing architecture guard is itself a real governance defect even when the reviewed runtime paths are individually acceptable. "Guarded by CI" is only true while the relevant source guards are green.
- `CrossSystemAuthorityRegressionGuardTests` now materially defines the governance boundary for reviewed dangerous primitives: direct SQL/bulk mutation files, high-risk Tier-1 `ReplaceOneAsync` paths, raw Elasticsearch client writes, and Redis pattern invalidation fanout must stay explicitly allowlisted and backed by dedicated reviewed-pattern assertions.
- Reviewed direct SQL idempotency/locking helpers, replay fences, and full-document Mongo replacements remain local specialized primitives. They are not blanket-approved patterns for future Tier-1 authority work.
- Development-only or lower-tier exceptions are not Tier-1 precedent. `DevelopmentCommerceHubSeedService` is allowed to full-replace CommerceHub listing documents only because it is development-scoped and still uses `ICommerceHubSearchIndexService`; `ServiceDiscoveryProjectionConsumer` remains a reviewed raw-ES exception because separate architecture tests enforce its stale-trust no-op semantics.
- Do not over-engineer accepted CQRS lag into global synchronous consistency. The intended model remains SQL authority, replay-safe convergence, freshness-aware tombstones, and explicit reconciliation when external truth is uncertain.

## 2026-05-21 Affiliate/Public Pricing Truth

- Storefront-visible pricing semantics are authoritative for affiliate/public price visibility on hardened paths.
- For simple products, current SQL `ProductUnits` plus the current default unit own online price and purchasability. Missing default unit means fail closed.
- For variant products, affiliate/public visible pricing must come from storefront-visible active variant pricing, not legacy product-level price fields.
- `EffectivePrice`, `Price`, and `OnlinePrice` on product-level documents are legacy compatibility fields and are not sufficient authority for affiliate/public runtime visibility when storefront pricing is unit-aware.
- Affiliate/public rebuild, replay, and reindex flows must preserve storefront-authority convergence. Replaying a product or policy event must never resurrect stale price semantics after SQL/default-unit truth changed.
- Public discovery/category/card/detail surfaces must prefer fail-closed purchasability: no visible price when storefront-authority price is missing, invalid, or out of stock.

## 2026-05-21 Governance Extraction Truth

- Storefront product projection mutation is now a two-layer runtime authority boundary: `IProductSqlAuthorityProjectionWriter` performs SQL recomposition and convergence decisions, and `IStorefrontProductProjectionMutationBoundary` is the only approved runtime boundary for the underlying storefront `ProductDocument` upsert/tombstone repository primitives.
- Direct calls to `IProductReadRepository.UpsertAsync` and `TombstoneIfFreshAsync` are dangerous primitives. They are intentionally still implemented by `ProductReadRepository`, but Tier-1 storefront product runtime code must not call them outside `StorefrontProductProjectionMutationBoundary`.
- `StorefrontProductProjectionMutationBoundary` makes dangerous mutation intent explicit with `ReviewedFullDocumentUpsert` and `ReviewedVisibilityTombstone`, requires a review reason, validates tenant/product/event identifiers, and refuses full-document upsert of an inactive or non-online-visible projection.
- Storefront-visible pricing authority is now named by `ProductStorefrontVisibilityAuthority`. Product-level `EffectivePrice`, `Price`, and `OnlinePrice` remain legacy compatibility fields; storefront, affiliate/public, and product-shaped rebuild code should consume `StorefrontEffectivePrice`, `StorefrontVisiblePrice`, or the helper methods that delegate to `ProductStorefrontVisibilityAuthority`.
- This is governance hardening for storefront product projections, not a universal Mongo mutation framework. CommerceHub listing documents, affiliate documents, order repair, promotion overlays, and lower-tier projections still keep domain-specific mutation paths where they have separate SQL recomposition, freshness, or operational constraints.

## 2026-05-22 PRODUCT-CON-11A Variant Lifecycle Truth

- Public variant lifecycle is not inferred from projected stock, product documents, search documents, CommerceHub listing documents, affiliate documents, cache entries, or event delivery order. Public lifecycle is proven only by current SQL `ProductVariants` joined to current same-tenant parent `Products`.
- `ProductVariantLifecycleAuthority` defines the reusable public-lineage predicate. Hardened paths must require a non-deleted variant with non-empty `ProductId`, same-tenant non-deleted parent product, active/online/published parent lifecycle, and active variant state for public purchasability or stock visibility.
- Rebuild and replay are collapse operations, not resurrection operations. A stale variant stock, price, listing, media, publication, clone/import, bulk, or search event may request recomposition, but if SQL no longer proves live variant lineage the correct public result is zero stock, no listing/search visibility, tombstone/delete convergence, or no-op.
- CommerceHub variant update replay is now a governed mutation boundary for listing visibility: variant lifecycle changes reconcile SQL listing publication plus SQL variant lifecycle before Mongo listing/search side effects. Storefront cart authority also fails closed to `0` stock when SQL variant lifecycle is missing, even if Mongo still has stale availability.
- Remaining debt is narrow and explicit: this hardening covers inspected storefront cart, product search/storefront recomposition, CommerceHub cart/checkout/listing reconciliation, and affiliate intelligence stock paths. Future feed/export, recommendation, import/clone, merge/split, bulk repair, standalone suggestion, CDN/cache rehydration, or external marketplace sync code must adopt the same SQL lifecycle helper or document an equivalent authority boundary with tests.

## 2026-05-22 PRODUCT-CON-11B Clone / Import Semantic Identity Truth

- Clone/import/retry payloads are not allowed to carry variant semantic ownership forward. Current SQL product/variant rows decide SKU and barcode ownership; payload SKU may be regenerated on create, payload barcode must be rejected on ownership conflict, and product-level simple-product identity must mirror the current default variant.
- `ProductVariantCloneImportAuthority` is the named helper for inspected product and variant mutation surfaces. It checks live product and variant SKU/barcode ownership, produces deterministic SKU suffixes for cloned/imported create payloads, validates same-product image media inheritance, and prevents duplicate initial stock fanout for one warehouse.
- The platform still has no dedicated repository-level CSV/Excel/ERP/supplier import framework in the inspected backend. Future explicit import, clone, copy-from-existing, template duplication, or marketplace-sync entrypoints must call the clone/import authority helper and then rely on `ProductVariantLifecycleAuthority` plus existing product/variant/publication/stock/media event fanout for derivative convergence.

## 2026-05-22 PRODUCT-CON-11C Merge / Split Semantic Ownership Truth

- Variant merge/split state is durable SQL authority, not projected state. `ProductVariants.SemanticLifecycleState` plus replacement, lineage root, operation id/kind, and supersession timestamp decide whether a variant is the current semantic owner.
- `ProductVariantMergeSplitAuthority` is the named helper for merge/split lifecycle convergence. It collapses duplicate same-operation replay, rejects stale replay against an already superseded variant, clears source SKU/barcode/media ownership on merge or split-parent retirement, stamps lineage metadata, and rejects supersession while open reservations remain on the source variant.
- `ProductVariantLifecycleAuthority` now includes current semantic ownership in the public-lineage predicate. Clone/import SKU and barcode checks ignore superseded variants as owners while still treating current product/variant rows as authority.
- Inventory and reservation creation fail closed for superseded variants. Product/variant admin mutation, stock mutation, default-variant assignment, barcode lookup, product search recomposition, CommerceHub reservation authority, payment recovery, and vendor visibility must converge to current semantic owners.
- No dedicated admin merge/split orchestration endpoint was present in the inspected backend. Future explicit variant merge, split, duplicate consolidation, catalog normalization, supplier deduplication, or bulk cleanup code must call the merge/split authority helper inside a SQL transaction, emit existing SQL-authoritative product/variant/stock/listing events after commit, and prove replay/rebuild convergence with focused runtime tests.

## 2026-05-22 PRODUCT-CON-11D Inventory / Reservation Lineage Truth

- Variant inventory lineage is not inferred from reservation rows, stock-sync payloads, projected stock, warehouse projections, CommerceHub order state, payment recovery candidates, or rebuild snapshots. It is proven only by current SQL tenant/product/variant/warehouse/stock/reservation joins.
- `ProductVariantInventoryLineageAuthority` is the named helper for inventory-reservable variant lineage, current stock-owner filtering, current stock-mutation ownership, and active consumable reservation filtering.
- Reservation replay is a collapse operation. Expired, released, committed, superseded, orphaned, inactive-warehouse, deleted-stock, detached-product, or invalid-lineage reservations must not become consumable again through delayed checkout, payment recovery, fulfillment replay, reconciliation jobs, or rebuild.
- Stock mutation and stock-sync replay are fail-closed. Superseded variants and stale warehouse ownership cannot receive new stock mutations, and stock-sync outbox messages whose event version/timestamp is older than current SQL variant authority or whose warehouse/stock lineage is gone must complete as suppressed replay rather than publish stale availability.
- Rebuild/recovery semantics remain operationally pragmatic: cleanup can still release stale reservations, reconciliation can still inspect stale evidence, and derivative systems can lag, but payment, checkout, fulfillment, CommerceHub stock visibility, and external availability fanout must converge from current SQL inventory lineage before creating side effects.
- Remaining debt: explicit merge/split physical stock transfer, reservation migration, warehouse reassignment workflows, external stock feed writers, and bulk inventory import/reconciliation tools still need dedicated transaction-scoped implementations and replay tests before they can be considered fully converged.

## 2026-05-22 PRODUCT-CON-11E Derivative Export / Replay Truth

- Feed, export, search, autocomplete, recommendation, homepage, cache hydration, and external sync visibility are derivative facts. They may lag or rebuild, but they may not become product publication, variant lifecycle, semantic ownership, inventory, price, media, category, or external mapping authority.
- Public derivative recomposition must start from current SQL authority. For variant-shaped public visibility that means `ProductVariantLifecycleAuthority` for current public lineage and `ProductVariantInventoryLineageAuthority` for stock ownership before availability can be projected outside PostgreSQL.
- Replay suppression is not a promise that every delayed message is discarded. The governed rule is deterministic replay collapse: stale queue delivery, stale outbox fanout, old Mongo/search seeds, cache hydration, reindex, recovery, and rebuild rerun current SQL recomposition, then delete, scrub, tombstone, no-op, or emit current visibility. Historical payload visibility cannot win.
- Rebuild and recovery must fail closed. CommerceHub search overlay/reindex, marketplace product/homepage read-model rebuilds, and storefront homepage SQL card recomposition now suppress superseded variants and detached stock. Autocomplete/suggestion reads remain SQL-revalidated through the CommerceHub overlay, so a cached completion response is not resurrection authority.
- In the inspected backend there is no standalone catalog feed/export pipeline for affiliate, supplier, pricing, inventory, or marketplace payload generation to harden directly. Any new exporter or external sync writer must be an explicit reviewed derivative boundary, use SQL authority recomposition, preserve tombstone/delete convergence, and document replay/rebuild and partial-fanout recovery behavior in the same change.

## 2026-05-22 ORDER-CON-01 Order / Fulfillment Lineage Truth

- PostgreSQL owns order and shipment lineage. Shipment event payload status, tracking, carrier values, provider callback timing, queue delivery order, Mongo order documents, Redis cache entries, notifications, and warehouse/export fanout are never proof that a shipment still owns an order or that order status may advance.
- `OrderFulfillmentLineageAuthority` is the named SQL lineage boundary for shipment-triggered order mutation and derivative shipment visibility. It requires a current non-deleted same-tenant shipment row joined to its current non-deleted owning order row before a shipment replay may affect order status or storefront shipment visibility.
- `OrderStatusSyncService` treats the incoming shipment status as a trigger only. It locks the current SQL order, reloads current shipment lineage, maps the SQL shipment status, and rejects detached, deleted, wrong-order, or stale shipment evidence before publishing order status change fanout.
- Shipment visibility rebuild is SQL-first. Storefront order snapshot rebuilds read live shipment status, tracking number, carrier code, and delivery timestamp where SQL still proves live shipment ownership; SQL absence or rejected lineage fails closed rather than resurrecting old payload tracking state.
- Existing owners do not move: payment lifecycle owns authorization/capture/recovery/refund semantics, reservation lineage owns settlement/consumption eligibility, fulfillment lifecycle service owns `FulfillmentStatus`, shipment/carrier services own carrier registration and reconciliation, and derivative order projections converge after those authorities commit.
- Remaining debt is explicit: current order/shipment services already contain carrier and fulfillment replay controls, but future warehouse transfer/reassignment, affiliate fulfillment export, analytics/reporting shipment projection, admin support cache, external shipment feed, and new provider callback handlers must adopt this lineage rule at their mutation or visibility boundary.

## 2026-05-22 PAYMENT-CON-01 Payment / Callback / Settlement Truth

- PostgreSQL payment lifecycle state is the semantic owner for payment, refund, and settlement visibility. Provider webhook timing, callback retry state, provider reconciliation evidence, settlement jobs, queue replay, Mongo order documents, Redis cache state, notifications, affiliate exports, accounting/reporting projections, and payment export snapshots are not payment status authority.
- `PaymentLifecycleService` is the governed order-payment mutation boundary. It validates callback evidence against terminal SQL truth, contains refund-id and settlement-id replay collapse, owns accounting-outbox staging, and now owns canonical payment-status fanout when accepted refunds or COD/gateway settlements change `Order.PaymentStatus`.
- Callback replay protection is terminal-state aware. A delayed success after SQL payment truth is failed, rejected, expired, refunded, partially refunded, or otherwise terminal is external evidence/manual-review territory, not payment resurrection; a delayed failure after successful SQL collection/settlement collapses to current SQL truth.
- `OrderPaymentLineageAuthority` is the named SQL visibility boundary for storefront order payment-facet replay. `OrderProjectionConsumer` must reload current non-deleted same-tenant SQL order payment status before applying `OrderPaymentStatusChangedEvent`; stale event `NewStatus` may describe evidence, but it cannot overwrite the current payment facet.
- Rebuild and recovery are convergence operations. Existing SQL-backed order repair uses current `Order.PaymentStatus`, and payment facet replay now rehydrates or projects current SQL truth when a stale fanout arrives. Recovery jobs may keep unresolved/suspense/reconciliation rows when provider truth is ambiguous; they may not mint a duplicate capture, refund, settlement, or completion path to make projections look current.
- Remaining debt is explicit: this sweep governs the inspected payment lifecycle fanout and storefront order payment visibility path. New CommerceHub payment visibility documents, affiliate/accounting/reporting projections, support/admin caches, customer notification retries, provider settlement exports, or callback adapters must prove SQL payment/refund/settlement lineage and add replay/rebuild coverage before treating payment payloads as visible status.

## 2026-05-22 ACCOUNTING-CON-01 Accounting / Ledger Replay Truth

- PostgreSQL ledger tables, accounting outbox rows, current `Orders`, current `OrderRefunds`, and current `PaymentSettlementBatches` are the only internal authority for ledger posting semantics. Outbox payloads, retry state, provider callback payloads, queue delivery order, reporting snapshots, dashboard documents, and export files are evidence only.
- `AccountingLedgerAuthority` is the named runtime boundary for inspected order collection, refund, and settlement accounting replay. It validates current order payment truth, COD fulfillment proof, refund lineage, settlement batch lineage, and settlement/refund amounts before `AccountingOutboxProcessor` can call journal generation.
- Replayed collection accounting is collapse, not resurrection. A stale online/wallet/COD accounting message cannot recognize revenue after current SQL payment truth is failed, expired, rejected, amount-mismatched, pending, deleted, or not currently COD-delivered where COD delivery is required.
- Refund posting is SQL-recomposed. Current `OrderRefund` amount, refund id, settlement context, posting state, and current order payment truth supersede replay payload amount/context. Missing refund lineage collapses stale replay; internally inconsistent posted-without-journal state remains retryable/reconciliation-required instead of silently creating replacement truth.
- Settlement posting is SQL-recomposed from `PaymentSettlementBatch`. Current settlement type/id, provider, gross, fees, currency, order count, and settled timestamp decide gateway/COD settlement journals. Payload amounts and fees cannot create duplicate settlement accounting or stale payout settlement exports.
- Rebuild/recovery convergence is deterministic: existing journal idempotency returns the same journal, current SQL truth posts once, stale replay completes as no-op, and missing/inconsistent SQL authority stays retryable for reconciliation. Financial read models, exports, dashboards, caches, and analytics must converge from SQL ledger/reconciliation state and must fail closed when current ledger authority cannot prove visibility.
- Remaining debt is intentionally scoped. This is not a universal financial reporting/export rewrite. Future financial export writers, tax reports, payout export files, BI aggregates, affiliate accounting exports, CommerceHub finance dashboards, support/admin caches, and reconciliation snapshot rebuilders must use SQL ledger/accounting authority or document an equivalent reviewed boundary with replay/rebuild tests.

## 2026-05-25 POS-ADMIN-CERTIFICATION-01 PASS 4 Settlement Membership Truth

- `PaymentSettlementBatch` is settlement identity and amount metadata authority, but its `OrderCount` field is recorded batch metadata, not sufficient settlement-membership authority for journal posting on its own.
- Settlement amount authority is bounded: gateway/COD settlement creation must reject non-positive gross amounts and fees outside `[0, gross]` before settlement persistence or payment-status mutation.
- Current settlement membership authority is the live SQL `PaymentSettlementOrders` lineage for a current non-deleted settlement batch. Settlement replay and immediate settlement accounting must recompose that lineage before posting.
- `AccountingLedgerAuthority` now treats empty current settlement lineage or drift between recorded `OrderCount` and current live settlement-order count as fail-closed retry/reconciliation territory rather than journal authority.
- `AccountingLedgerAuthority` also treats a soft-deleted settlement batch as missing current SQL authority even when historical settlement-order mappings remain.
- `PaymentLifecycleService` immediate gateway/COD settlement accounting follows the same rule and now uses the same `AccountingLedgerAuthority` settlement evaluation as replay. A fast path may not post settlement journals from an in-memory, header-only, or weaker fast-path-only batch snapshot after current SQL gross/fees or lineage authority has changed.
- Post-settlement refunds use the same authority boundary. A refund may not classify itself or post accounting as a bank-account post-settlement refund unless the current non-deleted settlement batch still contains that order in live `PaymentSettlementOrders` lineage and the batch header count still matches current mappings.
- The architectural rule is therefore explicit: settlement accounting and post-settlement refund accounting truth is current non-deleted `PaymentSettlementBatch` plus current `PaymentSettlementOrders`, under normal ledger idempotency and reconciliation semantics. Header-only settlement snapshots, deleted batches, replay payloads, exports, dashboards, and manual operator assumptions are evidence only.
- Storefront payment visibility follows the same fail-closed rule. When `OrderPaymentLineageAuthority` cannot find current non-deleted SQL order authority, payment replay deletes the tenant-scoped derivative order document and invalidates its cache instead of leaving stale financial visibility resident. When replayed payment status conflicts with current SQL truth, it may repair status and freshness but must not append payload-derived payment timeline narrative.

## 2026-05-22 GOVERNANCE-CON-01 Dangerous Primitive Truth

- Dangerous derivative primitives are allowed only as reviewed implementation details, not as authority models. Full Mongo document replacement, raw Elasticsearch visibility writes, Redis cache mutation, direct SQL/bulk mutation, and raw bus publishing must stay behind named authority, projection, outbox, cache, or search boundaries with tests and documentation.
- `DerivativeProjectionMutationGuard` is the lightweight runtime contract for approved derivative mutations that cannot yet move behind a richer domain-specific writer. A reviewed mutation must name the primitive, boundary, entity/document key, event identity, authority kind, freshness guard, fail-closed behavior, and review reason. Full visibility replacement and search writes must also declare current SQL authority before the primitive can run.
- Runtime guards do not create business truth. They only make dangerous derivative mutation intent explicit after the owning path has already recomposed SQL authority or decided a tombstone/no-op convergence result.
- Storefront footer projection now follows this rule: `StorefrontFooterUpdatedEvent` is a trigger, SQL footer/social-link rows are recomposed as current visibility, `ProjectionEventFence` collapses duplicate active replay before side effects, the freshness filter blocks stale replacement, and `DerivativeProjectionMutationGuard` validates the reviewed Mongo replacement contract.
- Storefront config projection now follows this rule for both full Mongo replacement and Redis cache invalidation: store-config events are triggers, current SQL store config plus tenant currency are recomposed before side effects, `ProjectionEventFence` collapses duplicate active replay, `BuildFreshStoreConfigEventFilter` blocks stale replacement, and `DerivativeProjectionMutationGuard` validates the reviewed `StorefrontConfig`, `Checkout`, and `StoreMetadata` authority scope before mutation.
- Governance remains intentionally incremental. `IProductSqlAuthorityProjectionWriter`, `IStorefrontProductProjectionMutationBoundary`, product/variant lifecycle helpers, payment/fulfillment/accounting authorities, and specialized outbox processors remain the preferred authority boundaries. The generic dangerous-primitive guard is for narrow derivative mutation chokepoints, not a replacement for domain authority services.

## 2026-05-22 TENANT-CON-01 Tenant Lineage Truth

- Tenant lineage is SQL authority. Event `TenantId`, request payloads, raw headers, Mongo documents, Redis keys, Elasticsearch documents, cached storefront config, repair inputs, and rebuild seeds are evidence only until current PostgreSQL proves the owning tenant still exists, is active, and owns the referenced row.
- `TenantLineageAuthority` is the shared helper for inspected replay/rebuild tenant ownership checks. It rejects empty ids, missing SQL authority, deleted tenant-owned rows, cross-tenant ownership, and inactive/deleted tenant owners before derivative visibility mutation.
- Storefront config projection is now a tenant-lineage governed derivative boundary. Store-config events may trigger work, but current SQL `StoreConfig` joined to the current active `Tenant` decides whether Mongo storefront config and Redis store-config cache may exist. Missing, inactive, or cross-tenant lineage converges to projection deletion plus tenant-scoped cache invalidation.
- Admin search sync/rebuild tenant scope must come from trusted tenant context, not a locally parsed raw tenant header. `X-Tenant-Id` is compatibility output from trusted middleware and must not become repair-tooling authority.
- Cross-tenant replay and rebuild semantics are fail-closed: stale tenant ownership may request recomposition, but it cannot resurrect storefront config, product/category search visibility, homepage state, CMS visibility, payment/order visibility, or cache hydration into a tenant boundary unless current SQL lineage proves ownership.
- Remaining debt is explicit: this phase does not claim repo-wide tenant-lineage adoption for every affiliate, CMS, homepage, analytics, CommerceHub, notification, support, direct repair, or cache warmer path. New tenant-scoped derivative writers must either call `TenantLineageAuthority` or document an equivalent SQL authority boundary with focused replay/rebuild tests.

## 2026-05-22 TENANT-CON-01 Phase 2 Derivative Isolation Truth

- Tenant-scoped derivative projection ids and cache keys are not authority. They are isolation containers that must still converge from current SQL tenant/product/category/listing/profile authority before public visibility is served or mutated.
- `TenantScopedDerivativeKey` is the shared primitive for derivative systems that need a stable tenant+entity document id outside PostgreSQL. A product id alone is not an acceptable public search/write id for tenant-scoped affiliate visibility.
- Storefront homepage Redis/Mongo hydration is not final visibility. Product cards and category cards must be revalidated against active tenant lineage and SQL product/category lineage before response. Inactive tenant lineage returns empty visibility rather than cached homepage content.
- CommerceHub marketplace product and tenant-homepage rebuilds must check active tenant lineage before upsert. Missing or inactive tenant authority converges to delete/scrub/no-op, even when stale homepage sections, listing ids, or Mongo read models still exist.
- Public affiliate product projection and repair tooling must validate current SQL product tenant ownership, use tenant-scoped Mongo filters, use tenant-scoped Elasticsearch ids, and reload current tenant profile/currency from SQL for fanout. Inactive tenant profile/currency replay deletes affiliate tenant visibility.
- Replay/rebuild convergence remains deterministic: stale cache hydration, delayed projection events, repair scripts, reindexing, affiliate profile/currency replay, and homepage regeneration may ask current SQL for visibility, but cannot restore cross-tenant or inactive-tenant derivative state.
- Remaining debt is intentionally scoped. CMS page caches, SEO caches, notification/support/analytics caches, marketplace localization/review caches, and future cache warmers or feed writers still require explicit tenant-lineage adoption when they start hydrating or mutating tenant-scoped public visibility.

## 2026-05-24 TENANT-CON-01 Phase 3 Actor / Admin Scope Truth

- Tenant admin authority is current SQL actor authority, not a durable JWT role claim. A valid token may identify a candidate user and tenant, but the current `Users` row, current user role, current user active/deleted state, and current active `Tenant` row decide whether tenant-scoped privileged work may proceed.
- `AdminTenantAuthority` is the named runtime boundary for tenant-bound admin actor recomposition. It validates authenticated user id, token tenant claim, trusted tenant context, current SQL user tenant ownership, active actor state, active tenant lineage, and endpoint role metadata before privileged tenant-bound endpoints continue.
- `AdminTenantAuthorityMiddleware` applies that boundary after normal ASP.NET authorization. ASP.NET policies still perform coarse authentication and role checks; SQL actor authority collapses stale role replay, stale tenant claim replay, disabled-user replay, tenant-deactivation replay, and tenant-moved actor replay.
- Tenant-scoped operational tooling must use trusted tenant context. `X-Tenant-Id`, query-string tenant ids, frontend-selected tenant ids, stale support dashboard state, and repair payload tenant ids are candidate evidence only and must not decide admin repair/rebuild scope.
- Tenant-scoped admin mutation controllers must read the trusted `ITenantContext.RequiredTenantId` after tenant middleware resolution. Compatibility headers are not controller-local authorization inputs for orders, shipments, inventory, storefront configuration, CMS/publication, or repair/rebuild controls.
- Storefront configuration and CMS/publication controls are owner/admin tenant controls unless a future phase deliberately documents and tests narrower delegated permissions.
- Tenant admin operational mutation is scoped. Tenant admins may trigger tenant-scoped search/cache/rebuild repair that recomposes from current SQL truth; they may not delete or recreate global search indexes or run all-tenant repair fanout through tenant admin endpoints.
- Platform/global operations require explicit platform authority or reviewed runbooks. An all-tenant repair script can exist as a platform primitive, but tenant-admin controllers must call tenant-scoped entrypoints and must not silently widen to all tenants.
- Rebuild/recovery convergence rule: a rebuild triggered by an admin session must converge against current SQL tenant lineage and current SQL actor authority at the time of the request. Stale admin sessions, delayed repair calls, replayed privileged operations, and queued operational fanout cannot resurrect tenant control after SQL actor or tenant authority changed.
- Remaining debt is explicit: this is not a full IAM/ABAC/SSO framework and does not model delegated organizations, support impersonation ceremonies, or every future platform runbook. Future support elevation, tenant switching, impersonation, global repair, cache warmer, or publication-control tooling must state actor authority, tenant scope, replay collapse, and platform/super-admin boundaries in code, tests, and docs.

## 2026-05-24 OPERATIONS-CON-01 Replay / Drift / Rebuild Observability Truth

- Replay collapse, fail-closed rejection, rebuild suppression, and drift detection are operational facts. They must be observable even when they intentionally do not mutate business state.
- `OperationalConvergenceTelemetry` is the shared semantic convergence observability boundary. It emits bounded metrics and trace events for replay decisions, authority failures, rebuild diagnostics, dangerous primitive governance, and drift diagnostics.
- The approved classification dimensions are `OperationalConvergencePhase`, `OperationalConvergenceDecision`, `OperationalConvergenceAuthority`, and `OperationalConvergenceReason`. New observability for semantic convergence should extend these stable enums rather than inventing subsystem-local reason strings or logging raw payload data.
- Replay-safe no-op handling remains a valid convergence outcome, but it must be classified as collapsed, suppressed, no-op, retry, failed-closed, deleted, tombstoned, rehydrated, or applied at the governing boundary.
- Rebuild auditability is lightweight and deterministic: `SqlTruthRebuildGuard` records that a rebuild or repair path declared SQL authority; domain writers remain responsible for current-state upsert, delete, scrub, tombstone, or no-op convergence.
- Drift diagnostics are not new authority. MongoDB, Redis, Elasticsearch, and read-model drift checks record detection/reconciliation decisions, but repair still must recompute from PostgreSQL and existing lineage authorities before derivative mutation.
- Telemetry invariants: no sensitive tenant/payment/customer data in metric labels; no raw document keys or free-form exception messages in labels; tenant identity appears only as deterministic buckets; reasons are stable classifications; high-volume hot paths should emit at shared guards rather than repeated local logs.
- Remaining debt is intentional. This does not create perfect observability, a distributed tracing estate, SIEM policy, or a universal audit table. Future derivative surfaces and repair tooling must add the same convergence telemetry when they add new replay/rebuild/drift behavior.

## 2026-05-24 STRATEGIC-REVIEW-01 Storefront Navigation Replay Truth

- Storefront navigation read models are derivative visibility. `NavigationPublishedEvent`, Mongo navigation documents, Redis navigation/menu/header/footer keys, and storefront cache timing are not navigation authority.
- Current SQL `StoreConfig` joined to the current active owning tenant is the authority boundary for navigation projection. A stale event may trigger recomposition, but it cannot preserve navigation when SQL tenant/store-config lineage is missing, inactive, moved, or cross-tenant.
- Navigation projection uses `ProjectionEventFence` to collapse duplicate completed replay and active overlapping delivery before full-document Mongo replacement or Redis invalidation. The freshness filter must reject the same `EventId`; duplicate replay is not a reason to repeat infrastructure fanout.
- Full `StorefrontNavigationDocument` replacement and Redis navigation cache invalidation are reviewed dangerous derivative primitives. They must pass `DerivativeProjectionMutationGuard` with `StorefrontNavigation`, `StorefrontConfig`, and `StoreMetadata` authority declarations, current SQL authority, a freshness guard, fail-closed semantics, and a documented review reason.
- Rebuild/recovery semantics: navigation repair, replay, or future rebuild tooling may upsert current SQL navigation, delete/scrub stale navigation, invalidate cache, or no-op. It must not regenerate navigation from old customization JSON, stale Mongo documents, Redis keys, or payload slug values after SQL lineage rejects visibility.
- Remaining debt is explicit: CMS page projection, SEO/navigation-adjacent caches, smart-offer overlays, online-customer admin projections, analytics, notification caches, and support/admin caches are still specialized surfaces unless they adopt the shared guards. New tenant-scoped derivative full replacements must add equivalent replay/rebuild tests and governance documentation.

## 2026-05-24 CHAOS-CON-01 Semantic Convergence Chaos Truth

- Replay storms, duplicate outbox delivery, repeated callback delivery, dead-letter replay, stale search/cache replay, and duplicate rebuild triggers are valid operational conditions. Their correctness target is deterministic convergence, not single delivery or zero retries.
- Chaos validation confirmed that the shared inspected boundaries collapse duplicate completed projection delivery through `ProjectionEventFence`, reject dangerous full visibility replacement without current SQL authority through `DerivativeProjectionMutationGuard`, require explicit SQL authority for rebuild entrypoints through `SqlTruthRebuildGuard`, and emit low-cardinality convergence telemetry for replay, rebuild, authority, dangerous primitive, and drift decisions.
- Tenant, actor, payment, fulfillment, accounting, cache, search, and repair replay inputs remain evidence only. Current SQL tenant lineage, current SQL actor authority, current order payment/fulfillment truth, current accounting/refund/settlement authority, and reviewed derivative mutation guards decide apply, no-op, delete, retry, or fail-closed behavior.
- Partial rebuild interruption and rebuild overlap are convergence conditions. A rebuild may be retried or duplicated, but it must declare SQL authority again and must not restore derivative visibility from stale Mongo, Redis, Elasticsearch, event payload, or repair input state.
- Operational repair remains intentionally flexible but must be tenant-scoped and auditable. Tenant-admin repair must use trusted tenant context and current actor authority; global repair primitives remain platform/runbook scope.
- Discovered weakness: AF16E affiliate repair/reindex uses active SQL tenant filtering and tenant-scoped derivative keys, but it does not call `TenantLineageAuthority` directly. This is an equivalent local control on the inspected path, not observed tenant leakage, but it is governance drift because docs and future reviewers can overstate shared-helper adoption.
- Remaining debt: this chaos pass validates the inspected shared boundaries and high-risk operational source markers. It does not prove every CMS, SEO, notification, analytics, support cache, external feed/export, BI/reporting rebuild, or future global runbook path has identical replay/rebuild convergence until that path adopts shared guards or an equivalent tested boundary.

## 2026-05-24 STABILIZATION-CON-01 Operational Scalability Truth

- Throughput tuning is an operational concern, not an authority model. SQL aggregate ordering, tenant lineage, lifecycle services, outbox idempotency, and replay collapse remain the correctness boundaries.
- Marketplace event outbox, marketplace projection outbox, and stock sync outbox workers must use bounded configuration for batch size, max batches per poll, inter-batch delay, poll interval, and claim lease. Hard-coded worker cadence or lease values on these high-volume paths are governance drift because they make backlog recovery and large-tenant replay tuning unsafe.
- Bounded batch draining is allowed only when each batch still respects SQL eligibility, leases, retry delay, ordering blockers, and compensation behavior. Increasing throughput must not bypass failed earlier aggregate events or stale authority checks.
- Rebuild/replay scalability claims must be documented as operational assumptions unless validated with production-like dependency latency. The current guarantee is deterministic convergence and tunable recovery, not a fixed enterprise throughput SLO.

## 2026-05-24 PERFORMANCE-CON-01 Replay / Recovery Throughput Truth

- Payment-cancellation recovery throughput is now configuration-driven through bounded batch size, max batches per poll, inter-batch delay, poll interval, idle interval, error delay, retry threshold, and retry backoff cap.
- Payment-cancellation outbox tuning is only pacing around the existing payment lifecycle authority. It must continue to call `IPaymentLifecycleService` for every cancellation outcome and must not directly mutate payment or order state to drain backlog faster.
- API startup must bind high-volume operational options from configuration. Marketplace, stock-sync, accounting, and payment-cancellation workers are tunable recovery surfaces; defaults are conservative, not capacity guarantees.
- Remaining scalability debt is explicit: payment-cancellation outbox rows do not currently carry a processing lease. Correctness still relies on lifecycle idempotency and SQL authority. If active duplicate processing becomes a production pressure point, add lease columns and retry/restart tests rather than weakening lifecycle authority.

## 2026-05-24 OPERATIONS-RUNBOOKS-01 Operational Recovery Truth

- Operational runbooks are governance artifacts for human recovery behavior. They do not create a new business domain and do not move authority out of PostgreSQL, lifecycle services, lineage authorities, outboxes, or reconciliation owners.
- Replay recovery is correct when duplicate or stale evidence deterministically applies current SQL truth, collapses, no-ops, retries for recoverable authority, deletes/tombstones derivative visibility, or fails closed. Operator pressure is not a reason to bypass replay fences, freshness filters, lifecycle services, or SQL authority recomposition.
- Rebuild and reindex recovery must recompose from current SQL and redeclare SQL authority on each run. Interrupted rebuilds, overlapping rebuilds, and replay-safe no-op rebuilds are expected convergence conditions; stale Mongo, Redis, Elasticsearch, event payload, or repair snapshot state must not become rebuild authority.
- Cache and projection recovery prefers invalidation, delete/tombstone, SQL-backed rehydrate, and SQL-overlay reindex. Manual Mongo/Redis/Elasticsearch mutation is a dangerous primitive and cannot be used to manufacture visibility when SQL authority disagrees.
- Tenant isolation incidents must start from trusted tenant context, current SQL tenant lineage, tenant-scoped derivative identifiers/filters, and current SQL actor authority for request-driven operations. Global/all-tenant repair remains platform scope only.
- Payment, accounting, refund, payout, settlement, and fulfillment recovery must route through lifecycle/reconciliation owners. Provider payloads, queue payloads, projections, dashboards, exports, and manual edits are evidence only; uncertain external outcomes remain blocked, suspense, retryable, or reconciliation-required until authority is explicit.
- Dangerous operational primitives are documented in `docs/architecture/dangerous-operational-primitives.md`; operator procedures are documented in `docs/operations/operational-recovery-runbooks.md`; documentation coverage is guarded by `OperationalRecoveryRunbookArchitectureTests`.

## 2026-05-24 TENANT-CERTIFICATION-01 Tenant Isolation Certification Truth

- Tenant isolation confidence is a composed guarantee: trusted tenant resolution, current SQL tenant lineage, current SQL actor authority, tenant-scoped derivative identity, replay collapse, SQL-first rebuild declarations, dangerous primitive governance, and low-cardinality diagnostics must remain aligned.
- Raw `X-Tenant-Id`, query tenant ids, frontend tenant selectors, repair payload tenant ids, Mongo documents, Redis keys, Elasticsearch ids, and old rebuild inputs are evidence only. They cannot become tenant authority.
- Tenant-scoped public derivative full replacements must declare current SQL authority, freshness, and fail-closed behavior through a domain writer, `DerivativeProjectionMutationGuard`, or an explicitly documented equivalent. `SqlAuthorityOverlayKind.StorefrontCms` is the named boundary for storefront CMS page projection.
- `CmsPageProjectionConsumer` now fences duplicate publish/delete replay, fails closed on inactive or missing SQL tenant/page authority, and runtime-governs full page replacement. This closes a concrete certification gap in a CMS public visibility projection without creating a universal CMS framework.
- Certification findings are recorded in `docs/architecture/tenant-isolation-certification-findings.md`; operational recovery remains in `docs/operations/operational-recovery-runbooks.md`.
- Remaining debt is intentionally scoped. Other CMS-adjacent, SEO, smart-offer, online-customer, notification, analytics, support-cache, feed/export, report rebuild, cache warmer, and global runbook paths still require path-specific replay/rebuild/tenant/actor/telemetry validation when changed or expanded.

## 2026-05-24 SUPERADMIN-CON-01 Platform Authority Truth

- Platform authority is explicit global authority, not tenant authority. Super-admin users may operate globally, but the caller must be recomposed from current SQL before platform work proceeds.
- Super-admin JWT role/auth-type claims are candidate evidence only. Current SQL `SuperAdmins`, current `TokenVersion`, and current super-admin governance profile decide whether the actor still has platform authority.
- `SuperAdminPlatformAuthorityMiddleware` is the runtime boundary for `RequireSuperAdmin` endpoints. It guards both `/api/super-admin/*` controllers and older platform-policy controllers that use the same authorization policy.
- Stale platform authority fails closed: deleted super-admin rows, stale token versions after logout/refresh replay/password change, invalid claims, and non-active governance profiles reject the request and emit platform-authority convergence telemetry.
- Platform refresh rotation is also evidence-boundary work, not a bypass. A current refresh credential must still pass current SQL governance eligibility before new platform tokens are issued, and governance restriction revokes stored refresh authority.
- Global/all-tenant repair, platform recovery, global search/cache mutation, marketplace moderation, and emergency operational primitives remain intentionally available only under explicit platform authority or documented runbook scope.
- Remaining debt is explicit: this is not enterprise IAM, support impersonation, SIEM, universal audit queue adoption, adaptive global scheduling, or fixed rebuild SLO certification.

## 2026-05-24 SUPERADMIN-CON-01 Phase 2 Marketplace Governance Truth

- CommerceHub marketplace discovery is a shared public cross-tenant surface, but each visible item remains tenant-owned. Global aggregation is allowed only after current SQL listing/product/variant/category/tenant authority proves visibility.
- SQL `CommerceHubListings` are the marketplace publication authority for marketplace product read models and CommerceHub search visibility. Product publication alone is insufficient to resurrect marketplace visibility after listing unpublish/delete or governance hide.
- Homepage and recommendation sections are governance records. Curated product/category ids are not visibility authority; they must be revalidated against current SQL CommerceHub listing/category/tenant visibility before public reads, projection rebuilds, and replay recovery.
- CommerceHub Elasticsearch document identity is tenant-scoped for new writes. Legacy listing-only ids may be deleted during replay/reindex cleanup, but they are not the forward marketplace search identity model.
- Duplicate homepage projection replay must collapse before Mongo/Search/Redis side effects through `ProjectionEventFence` or an equivalent documented boundary.
- Remaining debt is explicit: this phase does not create a universal marketplace projection framework, personalized recommendation authority, enterprise moderation workflow, SIEM audit store, adaptive rebuild SLO, or path-specific certification for every review/localization/feed/cache-warmer/global-runbook path.

## 2026-05-24 SUPERADMIN-CON-01 Phase 3 Recovery Truth

- Super-admin recovery, replay, rebuild, reindex, and dead-letter retry operations are operational convergence tools, not authority sources.
- Tenant-admin CommerceHub search recovery must stay tenant-scoped through trusted tenant context. Global CommerceHub reindex remains platform/runbook scope.
- CommerceHub marketplace search recovery requires current SQL `CommerceHubListings` plus tenant/product/variant/category/stock/price/media/publication authority before indexing or preserving visibility.
- Rebuild interruption, restart, and overlap are normal convergence states. Each run must redeclare SQL authority and must not resume from Mongo, Redis, Elasticsearch, queue payloads, or repair snapshots as authority.
- Dead-letter retry must reset failed rows into the normal bounded claim/order/retry path; direct publish or processed-row suppression is not recovery.
- Operational pacing controls bound recovery pressure but do not define an enterprise rebuild SLO.
- Remaining debt is explicit: no adaptive global scheduler, dead-letter UI, SIEM-grade audit store, support break-glass ceremony, or universal feed/export/cache-warmer/global-runbook certification was introduced.

## 2026-05-24 SUPERADMIN-CON-01 Phase 4 Emergency Governance Truth

- Emergency authority is still explicit platform authority, not a separate business-truth model.
- Non-read `RequireSuperAdmin` operations must leave request-level operational audit after current SQL platform actor authority succeeds. Audit actor attribution must come from `SuperAdminPlatformAuthority` recomposition, not raw JWT claims alone.
- Domain-specific audit, ledgers, reconciliation records, and append-only financial audit tables remain stronger business records where they exist. Request-level operational audit provides attributable incident breadcrumbs for platform mutations and legacy platform-policy endpoints.
- Support impersonation and tenant override are not currently certified production API capabilities. Future support tooling must define current SQL support/platform actor authority, target tenant lineage, reason/reference, expiry/revocation, durable audit, and replay-safe convergence.
- Emergency rollback must recompose from SQL authority or route through lifecycle/reconciliation owners. MongoDB, Redis, Elasticsearch, queue payloads, dashboards, exports, and support-tool snapshots are rollback evidence only.
- Remaining debt is explicit: no enterprise IAM, support break-glass ceremony, SIEM-grade audit lake, request-body audit capture, or universal domain-specific audit for every older platform endpoint was introduced.

## 2026-05-24 SUPERADMIN-CON-01 Phase 5 Operational Chaos Truth

- Super-admin operational chaos is a convergence condition. Replay storms, rebuild overlap, dead-letter retry, stale moderation/review replay, stale emergency-session reuse, and dangerous primitive pressure do not create new authority.
- Current SQL platform actor authority gates emergency work; current SQL listing/product/variant/category/tenant authority gates marketplace visibility; lifecycle/reconciliation owners gate money, fulfillment, refund, payout, settlement, and ledger outcomes.
- Marketplace review aggregate replay is not search visibility authority. Review-triggered CommerceHub search sync must pass `CommerceHubSearchSqlAuthorityOverlay`; missing SQL listing authority deletes tenant-scoped search visibility instead of reindexing stale Mongo state.
- Rebuild overlap is safe only when each run redeclares SQL authority and converges through apply, delete, tombstone, no-op, retry, or fail-closed behavior.
- Dangerous primitive misuse must fail closed when SQL authority, freshness, fail-closed behavior, audit/runbook scope, or telemetry is missing.
- Phase 5 certification findings are recorded in `docs/architecture/super-admin-operational-chaos-certification-findings.md`. Remaining debt is intentionally scoped to future review exports, localization, ranking, feed/export, cache warmer, support, analytics/reporting, and global script paths until they receive path-specific certification.

## 2026-05-24 POS-ADMIN-CERTIFICATION-01 PASS 1 Operational Identity Truth

- POS access-token claims are evidence only. Current SQL `Users`, current hashed refresh-session binding, current actor role and tenant ownership, active tenant lineage, and current vendor governance ban state decide whether a default POS bearer session remains authenticated.
- The POS model intentionally exposes one current refresh-backed session per user. Login, tenant-owner onboarding issuance/restoration, refresh, or logout changes current SQL session authority; access tokens bound to a prior session fail closed after SQL commit.
- Cached tenant-registration replay is evidence only. Previously cached success payloads may restore a POS session only while the same current SQL owner row, tenant ownership, active tenant lineage, and tenant governance still authorize login.
- POS logout must not report success if the current SQL session binding could not be cleared. If revocation persistence fails, the API should return retryable service-unavailable rather than treating local cookie deletion as durable containment.
- New POS refresh credentials must be stored only as hashes. Legacy raw persisted refresh credentials and old access tokens without the session binding fail closed at rollout and require new sign-in; the access-token-visible session binding is not accepted as a refresh credential.
- Concurrent refresh/login/logout session rotation must reject stale writes through the existing SQL binding concurrency boundary. Refresh replay may be rejected without revoking a valid successor session; token-family compromise detection is not claimed.
- Tenant-admin role authorization remains layered: default POS authentication rejects stale session/actor evidence for every POS bearer request, and `AdminTenantAuthority` additionally recomposes current privileged role/tenant authority on role-protected operational endpoints.
- The 2026-05-26 re-certification confirmed that legacy POS access tokens without `pos_session` and expired current SQL session bindings remain fail-closed regression cases.
- Findings and remaining debt are recorded in `docs/architecture/admin-authentication-session-certification-findings.md`.

## 2026-05-25 POS-ADMIN-CERTIFICATION-01 PASS 2 Tenant-Admin Scope Truth

- Tenant-admin operational scope is current trusted context, not a compatibility request header or payload tenant. Admin controllers must consume `ITenantContext.RequiredTenantId` after the existing POS session and admin actor authority layers.
- `X-Tenant-Id` may remain a middleware-generated compatibility output, but tenant-admin controller code must not parse it to choose read or mutation scope.
- Mixed external callback/admin endpoints must separate authority modes. The abandoned-cart recovery callback may use payload tenant identity only when validated through that tenant's configured callback key; an authenticated tenant admin must match current trusted tenant context before callback-key bypass or persistence.
- Tenant-admin operational search recovery must stay tenant-scoped. Global CommerceHub alias promotion/finalization, migration catch-up, all-tenant rebuild, and equivalent platform search primitives remain platform/runbook authority even when adjacent tenant-scoped reindex tooling exists.
- Certification-critical tenant-admin SQL reads and mutations on sensitive customer/payment/admin-repair paths must bind trusted tenant scope explicitly in SQL; ambient EF query filters can assist isolation but are not the frozen authority contract for inspected admin-scope operations.
- Tenant-admin operational transport tooling follows the same rule. Inspected WhatsApp admin account/template lookup, enable/disable, and delete paths must bind trusted tenant scope explicitly and must not depend on optional tenant context or ambient query filters alone.
- Tenant-owned category administration follows the same rule. `CategoriesController` list/detail/create/update/delete and default-category seeding must bind the current trusted tenant explicitly in SQL and must not rely on ambient query filters or `FindAsync(...)` as the scope contract.
- Shared global taxonomy mutation is not tenant-admin authority. `AdminAffiliateCategoryController` create/update/activate/deactivate is platform authority behind `RequireSuperAdmin`; adjacent tenant-admin browse visibility does not widen that mutation scope.
- Tenant-owned WhatsApp account/template update must keep tenant binding on the shared service boundary as well as the controller boundary. `Id`-only update lookup is not a certified transport-admin scope contract.
- Reviewed tenant-admin reporting follows the same rule on the inspected boundary. `DashboardController` must consume `ITenantContext.RequiredTenantId` and bind trusted tenant scope explicitly in SQL for sales, customer, supplier, product, stock, and payment aggregates instead of relying on ambient query filters or tenant-scoped cache keys alone.
- Ledger-backed financial reporting remains service-scoped SQL authority. `FinancialReportsController` must consume `ITenantContext.RequiredTenantId` and pass that trusted tenant id to `IFinancialReportService`; optional tenant accessors are not the certified reporting-scope contract.
- Tenant-owned finance and shipping configuration administration follows the same rule on the inspected boundary. `BanksController`, `ExpenseCategoriesController`, `FinancialAccountsController`, and `ShippingMethodsController` must consume `ITenantContext.RequiredTenantId` and bind trusted tenant scope explicitly in SQL for list/detail/update/delete/toggle/statement/opening-balance/parent-account/shipping-calculation paths instead of relying on ambient query filters or primary-key-only lookup.
- PASS 2 does not alter SQL-first replay/rebuild, marketplace listing, derivative identity, cache/search, or global platform-operation authority. It closes tenant-admin scope-selection and admin SQL-scope drift without reducing legitimate external callback or platform flexibility.

## 2026-05-25 POS-ADMIN-CERTIFICATION-01 PASS 3 Product / Inventory / Reservation Truth

- Inventory replay, checkout, payment recovery, fulfillment consumption, and stock visibility remain SQL-lineage convergence problems, not projection-state acceptance problems.
- Current consumable inventory authority requires current same-tenant variant semantic ownership, active parent product lineage, active warehouse lineage, current stock-row ownership, active reservation status, positive remaining reserved quantity, and non-expired reservation timing.
- Marketplace orders may span multiple tenant-owned suborders. Reservation authority checks on those paths must recompose consumable reservation lineage per tenant and then aggregate by current SQL order-item ownership; a single-tenant shortcut is not valid marketplace authority.
- Marketplace payment retry is not allowed merely because an order is still pending and raw reservation rows exist. Current SQL product publication, variant ownership, warehouse activity, reservation lineage, and stock ownership must still prove retry-safe inventory authority.
- Marketplace reservation creation is not allowed merely because a raw `ProductVariantStocks` row has available quantity. Current SQL warehouse activity and current stock-owner lineage must still prove the stock row is reservable inventory authority before a reservation is created.
- CommerceHub return receipt is not allowed to restore stock merely because historical committed reservation rows exist. Current SQL warehouse activity, variant semantic ownership, and stock ownership must still prove return-stock authority before resellable or damaged quantities are added back.
- CommerceHub delivery commit for an existing order item is inventory-lineage-authoritative, not publication-authoritative. Current product/variant active lineage must still exist, but later public unpublish/offline changes do not invalidate stock consumption for an already-authorized reservation-backed order item.
- Legacy availability reads and derivative stock-total recomposition are also evidence only on the reviewed PASS 3 surfaces. Repository availability, stock fanout totals, storefront variant stock projections, and CommerceHub variant stock projections must converge through current stock-owner lineage instead of summing undeleted stock rows directly.
- Reviewed SQL fallback checkout enrichment and reviewed storefront/search stock overlays are also evidence-only consumers until current SQL lineage recomposes them. `ProductVariantReadRepository` SQL fallback must require current public variant lineage plus current stock-owner lineage, and `ProductSearchSqlAuthorityOverlay` must not sum raw variant stock rows that survive only on inactive-warehouse or stale stock-owner lineage.
- Reviewed storefront cart SQL stock snapshots, affiliate intelligence SQL stock snapshots, and stock-compensation replay totals are also evidence-only consumers until current SQL lineage recomposes them. `CartService`, `AffiliateProductIntelligenceProjectionConsumer`, and `ProjectionFailureCompensationService` must recompose through current stock-owner lineage instead of summing raw undeleted stock rows when they preserve derivative stock visibility or emit a "fresh" replay snapshot.
- Over-reserved, foreign-owner, expired, inactive-warehouse, detached-variant, or stale-stock-backed reservation rows are repair evidence only. They are not consumption, payment-retry, fulfillment, or public-visibility authority.
- Findings and regression-freeze invariants for this pass are recorded in `docs/architecture/product-inventory-reservation-certification-findings.md`.

## 2026-05-25 POS-ADMIN-CERTIFICATION-01 PASS 5 Marketplace / Public Visibility Truth

- CommerceHub category lineage is public visibility authority. A CommerceHub listing/category/reference is live only when the leaf category and each parent in the SQL parent chain are active, present, non-self-referential, and acyclic.
- CommerceHub category ids stored in listings, Mongo read models, Elasticsearch documents, homepage JSON, recommendations, autocomplete/suggestion payloads, caches, and repair inputs are evidence only. Public visibility paths must recompose through `CommerceHubCategoryLineageAuthority` or a documented equivalent before preserving search, homepage, recommendation, or marketplace read-model visibility.
- CommerceHub search, product-detail reads, marketplace product read-model rebuild, homepage/recommendation projection, public homepage reads, super-admin curation validation, CommerceHub query suggestions/categories, listing projection, and reindex verification now converge on the same category lineage authority. Leaf-category `IsActive` alone is no longer enough for certified public visibility.
- Current marketplace vendor suppression is also public visibility authority on CommerceHub surfaces. Governance-hidden vendors and vendors currently suspended from new orders must be suppressed consistently across search, product list/detail reads, homepage/recommendation reads, curated-reference validation, and rebuild/reindex verification; cached Mongo/Redis/Elasticsearch state and curated ids are evidence only.
- CommerceHub browse/search/suggestion/autocomplete request shaping and cached product-suggestion revalidation are part of that same public-visibility authority on the inspected query boundary. Excluded-vendor sets, cached suggestion/autocomplete product payloads, and public count/facet inputs must suppress both governance-hidden vendors and vendors suspended from new orders before the response is returned; response-item post-filtering alone is not a certified public-count or suggestion authority model.
- CommerceHub search auto-healing, popularity/ranking maintenance, replay recovery, and rebuild/reindex workers are derivative maintenance paths, not visibility authority. When they mutate public search visibility they must pass `CommerceHubSearchSqlAuthorityOverlay` and delete with tenant-scoped search identity when SQL listing/category/product authority is gone.
- Public CommerceHub image convergence is media-authoritative. Search overlay, product-detail hydration, marketplace product read-model rebuild, and public listing fanout may use only current SQL `ProductMedia`/`ProductVariants.MediaId` for image retention; missing SQL media authority must clear stale legacy or derivative images.
- CommerceHub search review decoration is moderation-authoritative during overlay mutation. `AverageRating` and `ReviewCount` must be rebuilt from current approved, non-deleted SQL `ProductReviews`; Mongo/search summaries and replay payload fields are evidence only.
- Marketplace aggregation remains intentionally global, but it is tenant-safe only when every retained listing survives current tenant-owned SQL listing/product/variant/category/media/stock/publication authority plus current vendor suppression. Rebuild/replay may repeat suppression checks, but it must not resurrect a hidden or order-suspended vendor from derivative product, homepage, or curated-reference state.
- Invalid CommerceHub category lineage must fail closed through delete, tombstone, scrub, validation rejection, no-op, or reindex-count exclusion. Rebuild/replay overlap may repeat these outcomes, but it must not restore public visibility from derivative category state.
- Legacy service-marketplace search repair follows the same operational scope model even though it is not CommerceHub listing authority. Tenant-admin repair is tenant-scoped and may not delete/recreate the global `marketplace_services` index; global service-marketplace index recreation remains super-admin/platform recovery scope. Active, non-deleted SQL `Service` rows are the public service-search source for that reviewed repair path.
- Remaining debt is scoped to future ranking, feed/export, localization, support, review-export, analytics/reporting, cache-warmer, and global script paths until they add path-specific SQL listing/category lineage recomposition, replay/rebuild tests, and documentation.

## 2026-05-25 POS-ADMIN-CERTIFICATION-01 PASS 6 Operational Tooling Truth

- Tenant-admin operational storefront search repair is a SQL-first convergence tool, not a partial search-authority fallback. `AdminSearchSyncController.SyncProducts(...)` must recompose current SQL product/category/unit/media/promotion/search visibility through `ProductSearchSqlAuthorityOverlay` before Elasticsearch mutation.
- Partial SQL-row search rebuild is dangerous evidence only. Product search repair must not index directly from `Products.SalePrice`, raw category names, legacy image fields, stale repair snapshots, or minimal SQL projections that skip current storefront/unit/publication/category authority.
- Fail-closed deletion is the correct recovery outcome when current SQL search authority is missing or invalid. Repeated repair runs and rebuild overlap remain safe only because each run redeclares SQL authority before index/delete mutation.
- Tenant-admin category and shipping-method Mongo sync is also a SQL-first convergence tool, not a one-way additive repair path. `AdminDataSyncController` must declare SQL rebuild authority and delete tenant-scoped stale Mongo documents that are missing from the current SQL authority set after sync.
- Tenant-admin category search sync is also a SQL-first convergence tool, not a one-way additive repair path. `AdminSearchSyncController.SyncCategories(...)` must declare SQL rebuild authority and delete tenant-scoped stale Elasticsearch category documents that are missing from the current SQL authority set after sync.
- Product read-model rebuild cache invalidation is a derivative infrastructure operation, not authority. `AdminSyncController` must retain SQL-authoritative projection rebuild through `IProductSqlAuthorityProjectionWriter` and delegate cache invalidation to tenant-scoped `IProductCacheService` instead of directly scanning or deleting Redis keys.
- Reviewed affiliate repair/reindex is a SQL-first convergence tool, not a tenant-currency fallback path. `AF16E_ReindexAffiliateProductsWithStorefrontUrl` must reload current SQL tenant storefront metadata and `Tenant.Currency`, declare affiliate repair authority through `SqlTruthRebuildGuard`, and fail closed when either authority is missing.
- Blank or invalid tenant currency is dangerous evidence only. Affiliate repair must not synthesize `USD` or any other default currency during rebuild/recovery, and missing tenant storefront/currency authority must converge by stale affiliate visibility deletion rather than derivative preservation.

## 2026-05-25 POS-ADMIN-CERTIFICATION-01 PASS 7 Replay / Rebuild / Chaos Truth

- Replay storms, rebuild overlap, delayed recovery, worker restart, queue lag, cache rebuild pressure, and dead-letter floods remain convergence conditions, not new authority models.
- Payment cancellation recovery is now a leased outbox boundary. `PaymentCancellationOutboxMessage.ClaimedUntilUtc`, row-version claim enforcement, and bounded `PaymentCancellationOutboxOptions.ProcessingLeaseSeconds` collapse both lease-visible overlap and row-version claim races before `IPaymentLifecycleService` is invoked.
- Marketplace projection outbox overlap is now guarded by `MarketplaceProjectionOutboxMessage.RowVersion`/PostgreSQL `xmin` in addition to lease/completion fences, so duplicate workers cannot both persist a claimed row from stale tracked state.
- Marketplace projection duplicate-completion race is also diagnosable now: when a processed-event unique-key race proves another worker already completed the same logical replay after dispatch, the residual outbox row is cleaned up and `OperationalConvergenceTelemetry` records a collapsed duplicate-completion decision instead of leaving the branch log-only.
- Payment cancellation row leases are operational coordination only. `IPaymentLifecycleService` remains the payment authority, and stale or duplicate cancellation evidence must converge through the lifecycle owner rather than direct payment-status mutation.
- Storefront footer projection replay is tenant-lineage-authoritative on the reviewed public-content boundary. `StorefrontFooterUpdatedEvent` may trigger recomposition, but `StorefrontFooterProjectionConsumer` must revalidate current SQL tenant lineage after replay fencing and before Mongo replacement; inactive, deleted, or missing tenant authority converges by deleting tenant-scoped footer visibility with convergence telemetry.
- Accounting, stock-sync, marketplace event, marketplace projection, and payment-cancellation recovery paths now all expose inspected claim/fence behavior for active worker overlap, crash pickup after lease expiry, and bounded replay amplification.
- PASS 7 findings are recorded in `docs/architecture/replay-rebuild-chaos-certification-findings.md`; operator guidance remains in `docs/operations/operational-recovery-runbooks.md`.
- PASS 7 follow-up on 2026-05-29 closes adjacent Redis replay amplification on reviewed cache invalidation. `RedisInvalidationConsumer` now acquires `ProjectionEventFence` before homepage, CMS, footer, marketplace product/order, and marketplace review Redis key or pattern invalidation; duplicate completed replay and active overlap collapse before Redis fanout, with tenant-scoped telemetry for marketplace-domain cache events.

## 2026-05-26 POS-ADMIN-CERTIFICATION-01 PASS 8 Operational Survivability Truth

- OpenTelemetry meter registration is part of the reviewed operational runtime boundary on PASS 8 surfaces. A reviewed operational meter that exists in code but is not exported is an operability defect, not a harmless omission.
- Reviewed accounting telemetry is part of that same PASS 8 runtime boundary. `PointOfSale.Accounting` must stay exported with bounded `tenant_bucket` plus normalized operation/gateway/error labels, and its singleton fallback counters must stay bounded by the same reviewed dimension discipline rather than raw tenant/provider/error keys.
- Reviewed exported tenant and entity-correlation dimensions must stay bounded. PASS 8 uses `tenant_bucket` for exported tenant-oriented operational meters, bounded presence flags for reviewed order-correlated diagnostics, and keeps raw tenant/order ids in logs, traces, SQL audit, and other reviewed non-metric evidence channels only.
- Reviewed free-form metric labels such as reason, stage, action, source, search type, and event type must be normalized before emission. Queue payload fragments, document keys, exception text, and raw entity identifiers are not approved metric labels.
- `CommerceHubReindexOptions` is a deployment and recovery safety boundary. Recovery pacing values must be configuration-driven, bounded, and startup-validated before the node is considered ready for incident pressure.
- Reviewed `CommerceReliabilityTelemetry` provider, endpoint, surface, carrier, validation-method, projection, storage-provider, stock-sync, and isolation-level labels are part of that same PASS 8 bounded-dimension contract. Exported reviewed telemetry must normalize those labels at the emission boundary instead of relying on caller discipline.
- Reviewed Redis cache performance telemetry is diagnostic infrastructure, not cache authority. `PointOfSale.Redis.Performance` must stay exported only with bounded cache and pattern families; raw Redis keys, invalidation patterns, tenant ids, product ids, order ids, and payload fragments are not approved metric labels.
- Payment-cancellation survivability also includes explicit readiness diagnostics now. `payment_cancellation_outbox_readiness` is the reviewed readiness boundary for backlog age, repeated failure, terminal failure, active claim, and lease tuning visibility on the replay-safe payment-cancellation worker.
- Deployment/restart survivability remains convergence-driven, not clean-shutdown-driven. Rolling restart, crash recovery, and deployment overlap are expected to re-enter the existing SQL-first leases, fences, processed-event checks, and readiness gates rather than a separate deployment authority path.
- Disaster recovery remains SQL-authoritative. PostgreSQL is the only reviewed restore authority; MongoDB, Redis, Elasticsearch, RabbitMQ, queue payloads, and dashboard snapshots may accelerate rebuild or diagnosis, but they are not business-truth restore authority and may be discarded or rebuilt after SQL restore.

## 2026-05-27 POS-ADMIN-CERTIFICATION-01 PASS 6 Service-Marketplace Recovery Truth

- Legacy service-marketplace Elasticsearch documents are derivative public visibility. Current SQL `Service` rows alone are not sufficient restore authority.
- Reviewed service-marketplace replay and global drift reconciliation must prove active non-deleted service authority, active non-deleted tenant authority, and active non-deleted same-tenant `MarketplaceProfile` authority before preserving `marketplace_services` visibility.
- Replayed service payloads, stale search documents, and raw active service ids are evidence only. Missing service/tenant/profile authority must converge by delete, absence, retry, or fail-closed behavior rather than public service-search resurrection.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 9.1 CMS Public Visibility Truth

- Storefront CMS public visibility is SQL-authoritative. A CMS page is public only when current PostgreSQL proves active tenant lineage, same `TenantId`, `IsPublished = true`, `IsDraft = false`, `IsDeleted = false`, and `PublishedAt != null`.
- Redis CMS scope cache, MongoDB `StorefrontPageDocument`, Elasticsearch `cmspages`, sitemap cache entries, and replayed CMS publish/delete events are derivative evidence only. None may decide public CMS visibility after SQL has unpublished or deleted a page.
- Public CMS reads, CMS SEO metadata, CMS sitemap URLs, CMS slug resolution, and CMS unified-search results must revalidate current SQL publication authority before returning customer-facing visibility.
- Tenant-admin CMS search repair is a SQL-first convergence tool. It may index only current published SQL CMS pages and must delete tenant-scoped CMS search documents that are missing from the current SQL authority set.
- No scheduled CMS publication worker was found in the inspected storefront CMS implementation. Any future scheduled publication worker must revalidate current tenant/page/version/route/publication eligibility at execution time and fail closed when SQL no longer proves visibility.
- PASS 9.1 findings and debt are recorded in `docs/architecture/cms-public-visibility-certification-findings.md`.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 9.2 Navigation / Category Lineage Truth

- Storefront category hierarchy, public category structure, category routing eligibility, category SEO visibility, and category breadcrumb ancestry are SQL-authoritative through current PostgreSQL `Categories` plus active tenant lineage.
- A storefront category is public only when current SQL proves same-tenant, non-deleted category lineage and every parent in the chain is present, same-tenant, non-deleted, non-empty, non-self-referential, and acyclic.
- Redis category caches, Mongo category documents, Elasticsearch category documents, navigation JSON, menu caches, breadcrumb DTOs, route/slug cache entries, sitemap cache entries, and replay/rebuild payloads are derivative evidence only.
- Public storefront category reads, category SEO metadata, category sitemap entries, category breadcrumb generation, category slug resolution, and storefront navigation category links must recompose or filter through current SQL category lineage before returning public visibility.
- Navigation menus may contain presentation links, but they do not own category visibility. A category menu link is returnable only while current SQL category lineage still accepts the referenced category slug.
- PASS 9.2 findings and debt are recorded in `docs/architecture/category-lineage-certification-findings.md`.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 9.3 Product Detail / Media / Public Projection Truth

- Storefront product detail visibility, product list visibility, product search hydration, product discovery response shaping, product SEO metadata, product breadcrumbs, product slug resolution, and product sitemap URLs are SQL-authoritative through current PostgreSQL product, category, media, variant, unit, stock, promotion, publication, and active tenant lineage.
- Redis product caches, MongoDB `ProductDocument`, Elasticsearch `ProductSearchDocument`, sitemap caches, SEO caches, replay payloads, thumbnails, CDN URLs, and rebuild payloads are derivative evidence only. They may provide candidates but may not decide public product visibility when current SQL disagrees.
- Product media public visibility is owned by current same-tenant SQL `ProductMedia` rows ordered by `Position` and by `ProductVariants.MediaId` for variant-specific image linkage. Legacy product or variant image strings are not public media authority after SQL media ownership is removed.
- Reviewed storefront product-by-slug/id reads, deprecated Mongo list reads, category-tree product reads, storefront search hydration, discovery hits, product SEO metadata, product breadcrumbs, product slug resolution, and product sitemap generation must recompose through `ProductSearchSqlAuthorityOverlay` or an equivalent current SQL proof before returning product visibility.
- A product whose current SQL authority no longer proves active tenant lineage, active product state, online visibility, published state, valid category lineage, current variant/public unit semantics, or current media lineage must fail closed by absence, filtered response, tombstone/delete, or no-op rather than preserving stale projection visibility.
- PASS 9.3 findings and debt are recorded in `docs/architecture/product-detail-media-public-projection-certification-findings.md`.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 9.4 Homepage / Discovery / Recommendation Truth

- Homepage, discovery, featured, trending, popular, new-arrival, curated, recommendation-like, public affiliate discovery, and CommerceHub homepage visibility are SQL-authoritative through current PostgreSQL product/listing/category/media/variant/unit/stock/price/promotion/publication, affiliate-policy, tenant, and vendor-governance authority.
- Redis homepage caches, MongoDB product/homepage documents, Elasticsearch search/ranking hits, analytics popularity inputs, affiliate search documents, homepage section JSON, and replayed rebuild payloads are derivative evidence only. They may influence candidate selection or ordering, but not return eligibility.
- Reviewed storefront homepage shelves and CommerceHub homepage reads must recompose or filter cached/projected candidates through current SQL authority before response. Invalid curated references must be scrubbed, omitted, tombstoned, or deleted rather than preserved.
- Reviewed affiliate discovery reads now use Elasticsearch only as candidate/ranking input. `AffiliateProductDiscoveryService` and `PublicAffiliateProductsController` must recompose every returned candidate through `ProductSearchSqlAuthorityOverlay`, current affiliate-policy authority, active tenant authority, storefront URL authority, and storefront affiliate pricing/stock semantics.
- PASS 9.4 findings and debt are recorded in `docs/architecture/homepage-discovery-recommendation-certification-findings.md`.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 9.5 Search / SEO / Public Index Truth

- Search, autocomplete, facets, counts, SEO, sitemaps, structured data, feeds, public indexes, and rebuild/repair tools are derivative public visibility surfaces. Current PostgreSQL product/category/CMS/listing/tenant/vendor/policy authority decides eligibility.
- Storefront unified search and header autocomplete must treat Elasticsearch hits as candidates only. Product hits must recompose through `ProductSearchSqlAuthorityOverlay`, category hits through current SQL category lineage, and CMS hits through current SQL CMS publication state before response.
- Product discovery and CommerceHub search may use Elasticsearch ranking, but returned counts/facets must not exceed SQL-visible returned items. Raw Elasticsearch aggregations are not public facet/count authority.
- CommerceHub suggestions, autocomplete, popular terms, zero-result suggestions, and cached suggestion responses must be filtered against current SQL listing/product/category/vendor authority before return.
- Legacy affiliate portal public discovery, landing-page, category-count, and detail endpoints must not return Mongo `AffiliateProductIntelligence` as visibility authority; they must revalidate current SQL product publication, affiliate policy, tenant lineage, storefront pricing, and stock authority.
- SEO metadata, canonical URLs, slug resolution, sitemaps, and breadcrumb JSON-LD remain SQL-authoritative through `SeoService`. Redis SEO/sitemap caches are performance artifacts only.
- No reviewed product feed generator or product/organization JSON-LD generator exists in the inspected backend. Future feed/export/schema generators require path-specific SQL visibility certification before public use.
- PASS 9.5 findings and debt are recorded in `docs/architecture/search-seo-public-index-certification-findings.md`.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 9.6 Storefront Recovery / Rebuild Truth

- Storefront and CommerceHub recovery, rebuild, repair, cache rehydration, startup bootstrap, replay, and reindex paths are derivative convergence systems. They may restore MongoDB, Redis, and Elasticsearch state, but they may not restore public visibility unless current PostgreSQL authority accepts the entity.
- Product projection rebuild, storefront search repair, category/CMS repair, promotion repair, CommerceHub reindex, and CommerceHub auto-healing must converge from current SQL product/category/CMS/listing/media/unit/stock/vendor/publication authority and delete, tombstone, invalidate, or suppress stale derivative state when SQL authority is missing.
- Cache recovery remains invalidation or SQL-authoritative repopulation only. Redis keys, cache warmers, replayed invalidation payloads, and startup cache state are not visibility authority.
- CommerceHub startup/background search auto-healing uses derivative Mongo/search state only to discover candidates. Listing recovery identity is tenant-scoped as `{ TenantId, ListingId }` where tenant evidence is available, and final search mutation still passes through `CommerceHubSearchSqlAuthorityOverlay`.
- PASS 9.6 findings and debt are recorded in `docs/architecture/storefront-recovery-rebuild-certification-findings.md`.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 10.1 Event Propagation / Projection Freshness Truth

- The platform provides observable eventual consistency, not strong consistency. SQL commits are authoritative; MassTransit/RabbitMQ, dedicated SQL outboxes, MongoDB, Redis, Elasticsearch, CommerceHub projections, homepage/discovery projections, and search projections converge from that truth.
- Normal authoritative integration events must remain published through `IEventPublisher` and the MassTransit EF outbox. Specialized accounting, stock-sync, marketplace event, marketplace projection, payment-cancellation, and CommerceHub compensation outboxes remain SQL-backed convergence infrastructure with leases, retries, ordering, and readiness diagnostics.
- Projection lag is an operability condition. It is acceptable while bounded and visible, but stale or blocked outboxes must not be silent. Projection and MassTransit outbox readiness checks now emit low-cardinality `OperationalConvergenceTelemetry` lag decisions in addition to health-check data/logs.
- Duplicate delivery, worker restart, delayed replay, rebuild overlap, and out-of-order delivery remain expected convergence conditions. Reviewed consumers must collapse duplicates, reject stale versions, reload SQL authority, or fail closed instead of promoting replay payloads to authority.
- PASS 10.1 findings and debt are recorded in `docs/architecture/event-propagation-projection-freshness-certification-findings.md`.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 10.2 Redis Cache Freshness / Invalidation Truth

- Redis cache entries, cache warmers, invalidation payloads, cache refresh locks, SEO/sitemap caches, homepage caches, CMS/footer caches, product DTO caches, marketplace caches, and search/suggestion caches are derivative evidence only.
- Current PostgreSQL authority decides public product/category/CMS/listing/homepage/search/SEO/footer visibility. Redis may provide a candidate or cached DTO, but reviewed public reads must recompose or filter through current SQL authority before return or before repopulating cache.
- Redis invalidation is best-effort infrastructure fanout. Correctness depends on SQL-filtered reads, bounded TTLs, replay-fenced invalidation where available, and observable cache/projection lag, not exactly-once cache deletion.
- Cache warmers may reduce cold-start latency but may not create public visibility. Warmed marketplace CMS/footer and storefront footer data must still be filtered against current SQL publication before public return.
- Redis restart, loss, or failover is a cache-miss/repopulation condition. Redis state is never restore authority; MongoDB, Elasticsearch, and Redis must converge from SQL or be discarded/rebuilt.
- PASS 10.2 findings and debt are recorded in `docs/architecture/redis-cache-freshness-invalidation-certification-findings.md`.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 10.3 Mongo Projection Convergence Truth

- MongoDB is a primary read model and projection store, not a source of truth. PostgreSQL remains authoritative for catalog, category lineage, product media, publication, CMS publication, homepage eligibility, CommerceHub listing visibility, affiliate policy/pricing, inventory, orders, financial state, and tenant lineage.
- Mongo projections may lag after SQL commit. They must converge through events, outboxes, replay, repair, rebuild, or SQL-filtered public reads. Permanent divergence, projection-only visibility truth, stale rebuild promotion, additive-only public repair, and projection resurrection are architecture violations on reviewed public-visibility boundaries.
- Reviewed storefront product Mongo mutation must continue through `IProductSqlAuthorityProjectionWriter` and `IStorefrontProductProjectionMutationBoundary`, with SQL recomposition, freshness-aware upsert/tombstone behavior, and complete storefront-critical fields.
- Reviewed category, CMS, homepage, CommerceHub, affiliate, and marketplace Mongo rebuild/repair paths must derive final mutation from current SQL authority, include tenant scope where applicable, and delete/tombstone/suppress stale projections when SQL authority is missing.
- Mongo projection freshness must remain measurable through timestamps, event versions, mutation versions, processed-event receipts, tombstones, search-sync metadata, readiness checks, and bounded convergence telemetry. A universal Mongo-vs-SQL parity scanner is not currently part of the certified architecture.
- PASS 10.3 findings are recorded in `docs/architecture/mongo-projection-convergence-certification-findings.md`.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 10.4 Elasticsearch Freshness / Search Convergence Truth

- Elasticsearch is a derivative search/discovery read model, not product, category, CMS, CommerceHub listing, affiliate, service, publication, visibility, count, facet, suggestion, or autocomplete authority.
- Product, category, CMS, CommerceHub, public affiliate, and service search documents may lag after SQL commit, but reviewed mutation, reindex, repair, and auto-healing paths must converge from current PostgreSQL authority and delete/suppress stale search documents when SQL authority is missing.
- Reindex and alias promotion are recovery mechanics only. CommerceHub alias promotion requires SQL-visible count verification, and tenant-admin product/category/CMS search repair must remain tenant-scoped and SQL-derived.
- Search freshness and drift must remain observable through document timestamps, CommerceHub search sync metadata, search health checks, auto-healing metrics, `SearchTelemetry`, and bounded `OperationalConvergenceTelemetry` drift diagnostics.
- Counts, facets, suggestions, popular terms, autocomplete candidates, and zero-result assistance may use Elasticsearch for speed or ranking but must be filtered or bounded by SQL-visible returned items on reviewed public surfaces. Raw Elasticsearch aggregations are not public count authority.
- PASS 10.4 findings are recorded in `docs/architecture/elasticsearch-freshness-search-convergence-certification-findings.md`.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 10.5 Cross-System Convergence Truth

- Whole-system convergence is SQL-first and eventually consistent. The intended convergence chain is `PostgreSQL -> Outbox -> Consumers -> MongoDB -> Redis -> Elasticsearch`, with direct SQL-first repair/rebuild/reindex paths allowed only as convergence tooling.
- MongoDB, Redis, and Elasticsearch may disagree temporarily with PostgreSQL and with each other. Reviewed public-visibility paths must make that disagreement bounded, observable, and recoverable by SQL overlays, freshness filters, replay fences, stale deletes/tombstones, cache invalidation/TTL, reindex/repair, health checks, and telemetry.
- Product, category, CMS, homepage/discovery, CommerceHub, affiliate, and service/search surfaces may use derivative stores for candidate selection, ordering, acceleration, or diagnostics only. Current PostgreSQL authority decides existence, publication, lineage, media, pricing, stock, policy, vendor/service, tenant, and return eligibility.
- Repair, replay, and startup recovery may restore derivative state but may not restore business truth. If SQL rejects an entity, reviewed recovery must delete, tombstone, suppress, invalidate, no-op, or fail closed instead of preserving stale MongoDB, Redis, or Elasticsearch visibility.
- Cross-system drift must stay observable through readiness checks, freshness metadata, processed-event receipts, search sync metadata, bounded `OperationalConvergenceTelemetry`, and path-specific diagnostics. No universal parity scanner or strong read-your-write guarantee is claimed.
- PASS 10.5 findings are recorded in `docs/architecture/cross-system-convergence-certification-findings.md`.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 10.6 Chaos, Lag & Operational Survivability Truth

- Operational survivability is a degradation and recovery guarantee, not a zero-downtime or strong-consistency guarantee. PostgreSQL remains authoritative when MongoDB, Redis, Elasticsearch, RabbitMQ, MassTransit, consumers, outboxes, replay, repair, reindex, cache warmup, and startup recovery are degraded.
- Derivative outage must not invert authority. MongoDB may be unavailable or stale, Redis may be empty or slow, Elasticsearch may be unavailable or corrupt, consumers may be stopped, and outboxes may backlog; reviewed public-visibility paths must still prove current SQL authority before returning or mutating derivative visibility.
- Recovery may discover candidates from derivative systems, but final mutation must revalidate current SQL, tenant scope, freshness, and replay/idempotency controls. Missing SQL authority must converge by delete, tombstone, suppression, invalidation, no-op, manual review, or fail-closed behavior.
- Operational lag must stay observable through readiness checks, health checks, bounded telemetry, freshness markers, processed-event receipts, search sync metadata, repair/rebuild reports, or equivalent diagnostics. Log-only outage recovery is not certified for reviewed public-visibility or critical operational paths.
- PASS 10.6 findings are recorded in `docs/architecture/chaos-lag-operational-survivability-certification-findings.md`.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 11.1 Super Admin Identity / Session Truth

- Super-admin platform authority is current SQL `SuperAdmins` existence, current SQL `UserGovernanceProfiles` state, and current `SuperAdmins.TokenVersion`.
- Super-admin JWTs, cookies, role claims, auth-type claims, refresh credentials, Redis state, and session cache state are candidate evidence only. They do not own platform authority.
- Every non-anonymous `RequireSuperAdmin` endpoint must pass `SuperAdminPlatformAuthorityMiddleware`, which reloads current SQL platform actor authority and governance state before controller execution.
- Login and refresh must recheck current governance login eligibility. Logout, password rotation, governance restriction, token-version mismatch, and account deletion must make stale access and refresh credentials fail closed.
- Legacy platform operations must use `RequireSuperAdmin` for platform authority. Tenant-admin routes must not branch on raw `SuperAdmin` role strings to perform global/platform work.
- PASS 11.1 findings are recorded in `docs/architecture/super-admin-identity-session-security-certification-findings.md`.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 11.2 Global Scope / Tenant Boundary Truth

- The platform has two authority domains: platform/global authority and tenant authority. Platform authority may intentionally cross tenants only under explicit `RequireSuperAdmin` policy and current SQL platform actor validation. Tenant authority may never cross tenants.
- Tenant-admin authority is current trusted tenant context plus current SQL tenant actor authority, current role, active user state, and active tenant lineage. Tenant JWT role and tenant claims are evidence only and are revalidated by `AdminTenantAuthorityMiddleware`.
- Global, tenant-scoped, and hybrid operations must declare their intended scope at the route/service boundary. Tenant-scoped reindex, repair, replay, recovery, diagnostics, and reporting paths must use trusted tenant context and tenant filters. Global reindex, repair, diagnostics, and marketplace governance paths must use platform authority and must not depend on inherited tenant context.
- Replay, recovery, repair, reindex, and startup paths may discover derivative candidates, but final mutation must revalidate current SQL authority, tenant scope, freshness, and idempotency/fence controls. Missing SQL authority must delete, tombstone, suppress, invalidate, no-op, or fail closed.
- Scope-changing platform operations must be auditable with actor attribution, request/path attribution, and outcome. Tenant authority failures must remain observable through structured logs or bounded telemetry.
- PASS 11.2 findings are recorded in `docs/architecture/global-scope-tenant-boundary-certification-findings.md`.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 11.3 Marketplace Governance Authority Truth

- Marketplace governance is decision authority, not visibility authority. Listing approval, suspension, vendor governance, homepage curation, recommendation-like shelves, discovery boosts, featured/promoted placement, and marketplace moderation may influence candidates or ordering only.
- Public marketplace visibility remains current SQL `CommerceHubListings` plus current SQL product, variant, category, tenant, vendor-governance, pricing, stock, media, and review authority. MongoDB, Redis, Elasticsearch, homepage JSON, search analytics, recommendations, popularity metadata, replay payloads, and recovery snapshots are derivative evidence only.
- Tenant-admin listing publish/update must not create listing approval unless current vendor governance, live CommerceHub category lineage, current product publication/online visibility, and current public variant lineage accept the listing.
- Vendor-governance reactivation may restore prior listing publication intent only while current SQL tenant, product, variant, and CommerceHub category authority still prove eligibility. Otherwise stale restore intent must be cleared, not resurrected.
- Homepage, discovery, recommendation, featured, and promoted content must revalidate current SQL listing/category/vendor authority before public return or derivative mutation. PASS 11.3 findings are recorded in `docs/architecture/marketplace-governance-authority-certification-findings.md`.

## 2026-05-30 POS-ADMIN-CERTIFICATION-01 PASS 11.4 Global Search / Reindex / Recovery Truth

- Operational tooling is convergence authority only. Reindex, replay, repair, recovery, auto-healing, startup recovery, cache rebuild, and projection rebuild may rebuild derivative state, but they may not create publication, visibility, governance, or tenant authority.
- Global search and CommerceHub reindex must remain SQL-derived. Alias promotion, same-index repair, tenant repair, startup recovery, and auto-healing must revalidate current SQL listing/product/category/vendor/tenant/variant authority before indexing and must delete/suppress stale search documents when SQL authority is absent.
- Tenant operational tooling must stay tenant-scoped through trusted tenant context, tenant-scoped derivative identities, SQL overlays, and tenant filters. Global tooling must remain explicit platform/runbook scope.
- Replay and recovery payloads are triggers or evidence only. Current SQL authority decides apply, delete, tombstone, no-op, retry, manual review, or fail-closed behavior.
- PASS 11.4 findings are recorded in `docs/architecture/global-search-reindex-recovery-certification-findings.md`.

## 2026-05-30 POS-ADMIN-CERTIFICATION-01 PASS 11.5 Emergency Operations / Break-Glass Truth

- Emergency authority is operational authority, not business authority. It may stop, suspend, freeze, isolate, recover, repair, reindex, replay, shut down, or diagnose, but it may not create visibility, publication, tenant, governance, financial, inventory, or SQL authority.
- Break-glass work must be explicit through current SQL platform or tenant actor authority, current governance/session state, declared scope, reason/runbook context, and observable outcome. JWTs, role strings, dashboards, derivative stores, queue payloads, support selectors, and stale sessions are evidence only.
- Emergency recovery, repair, reindex, and replay may discover candidates from MongoDB, Redis, Elasticsearch, RabbitMQ, MassTransit, outboxes, repair snapshots, provider/carrier payloads, or startup state, but final mutation must revalidate current SQL authority and converge by apply, delete, tombstone, invalidate, no-op, retry, manual review, block, or reconciliation-required.
- Emergency marketplace actions may hide, suspend, suppress, unpublish, scrub, or reindex. They cannot restore public marketplace visibility unless current SQL listing/product/vendor/category/tenant/variant authority still accepts it.
- Revocation wins immediately at the next authority check: current SQL super-admin authority, governance state, token version, tenant actor/session authority, and active tenant lineage decide whether emergency authority still exists.
- PASS 11.5 findings are recorded in `docs/architecture/emergency-governance-certification-findings.md`.

## 2026-05-30 POS-ADMIN-CERTIFICATION-01 PASS 12.1 Merchant Publication Intent Authority Truth

- Publication intent is merchant request authority, not visibility authority. Tenant publish, republish, update, and unpublish requests may change SQL listing intent only through reviewed handlers.
- Marketplace visibility remains current SQL `CommerceHubListings` plus current SQL product, variant, CommerceHub category, tenant, vendor-governance, pricing, stock, media, and review authority. MongoDB, Redis, Elasticsearch, homepage JSON, search analytics, events, and recovery snapshots are derivative evidence only.
- Publish, republish, and update must require current vendor governance, live CommerceHub category lineage, active shipping, product publication/online visibility, and public variant lineage. Unpublish is tenant-owned withdrawal of intent and may only reduce visibility.
- Vendor-governance restore may preserve prior intent only while current SQL tenant/product/variant/category authority still accepts the listing; stale restore intent must be cleared instead of resurrected.
- Replay, recovery, reindex, auto-healing, homepage/discovery, and search mutation must revalidate current SQL listing/product/category/vendor/tenant/variant authority before derivative mutation and must delete, suppress, no-op, or fail closed when authority is absent.
- PASS 12.1 findings are recorded in `docs/architecture/publication-intent-authority-certification-findings.md`.

## 2026-05-31 POS-ADMIN-CERTIFICATION-01 PASS 12.2 Marketplace Category Mapping & Taxonomy Authority Truth

- CommerceHub category mapping is classification authority only. It may classify a tenant-owned listing; it may not publish, rank into visibility, or preserve public visibility after current SQL authority is gone.
- SQL `CommerceHubCategories` owns marketplace taxonomy lineage. SQL `CommerceHubCategoryAttributes` owns category-bound attribute schema. SQL `CommerceHubListings.CommerceHubCategoryId` owns listing classification.
- A CommerceHub category is live only when the row is active and every parent in the chain is present, active, non-self-referential, and acyclic according to `CommerceHubCategoryLineageAuthority`.
- POS Admin category pickers, category attribute forms, publish/update handlers, search overlays, homepage/discovery sanitation, suggestions/autocomplete, reindex, health, and auto-healing must derive taxonomy acceptance from current SQL lineage and fail closed for invalid, deleted, inactive, orphaned, or cyclic categories.
- MongoDB category fields, Elasticsearch category fields, cached trees, homepage category JSON, category facets, autocomplete buckets, suggestions, query analytics, and replay/rebuild payloads are derivative evidence only.
- PASS 12.2 findings are recorded in `docs/architecture/marketplace-category-taxonomy-certification-findings.md`.

## 2026-05-31 POS-ADMIN-CERTIFICATION-01 PASS 12.3 Marketplace Attribute & Filter Governance Truth

- CommerceHub attributes are category-bound classification/search metadata only. They may classify, filter, facet, search, and rank, but they may not publish, approve, create visibility, or bypass marketplace governance.
- SQL `CommerceHubCategoryAttributes` owns the current attribute schema for a live CommerceHub category. Listing `AttributesJson`, `AttributeSchemaSnapshotJson`, Mongo attributes, Elasticsearch attributes, query filters, facets, autocomplete metadata, and replay payloads are derivative evidence only.
- Merchant-provided attributes must validate against current live category lineage and current SQL attribute definitions. Unknown keys, invalid values, invalid category lineage, and unsupported category/attribute combinations fail closed.
- Search filters and facets must derive from current SQL filterable attributes on live category lineage. Unsupported public attribute filters fail closed instead of querying arbitrary derivative `attributes.*` fields.
- Projection, indexing, replay, recovery, and reindex paths must strip stored attributes absent from the current SQL category schema before derivative mutation, preventing deleted attributes from resurrecting as filters or search metadata.
- PASS 12.3 findings are recorded in `docs/architecture/marketplace-attribute-filter-governance-certification-findings.md`.

## 2026-05-31 POS-ADMIN-CERTIFICATION-01 PASS 12.4 Marketplace Pricing Authority Truth

- Marketplace item pricing is commercial authority owned by current SQL product variant pricing. It is not visibility authority and may not create publication, approval, ranking, or search visibility.
- Final CommerceHub item price is current SQL `ProductVariant.EffectiveOnlinePrice`: positive `OnlinePrice` when available, otherwise `Price`. CommerceHub cart, checkout, projection, detail/search hydration, and reindex paths must recompose or validate against this SQL value before serving or mutating derivative pricing.
- `CommerceHubListings.CommerceHubPrice` and derivative `CommerceHubPriceOverride` fields are compatibility remnants only. They must remain rejected, cleared, and SQL-constrained to null unless a future pricing authority design is explicitly certified.
- Storefront promotions are storefront overlays, not CommerceHub item price authority. CommerceHub promo-code calculations may discount checkout/commission totals where explicitly modeled, but they must not mutate listing item price authority.
- MongoDB, Elasticsearch, Redis, client price snapshots, search facets/stats, affiliate projections, replay payloads, and recovery snapshots are derivative pricing evidence only. Current SQL variant price wins during replay, recovery, reindex, startup rebuild, and chaos conditions.
- PASS 12.4 findings are recorded in `docs/architecture/marketplace-pricing-authority-certification-findings.md`.

## 2026-05-31 POS-ADMIN-CERTIFICATION-01 PASS 12.5 Variant & Inventory Marketplace Certification Truth

- Marketplace variant authority is commercial authority owned by current SQL `ProductVariants`, SQL variant semantic lifecycle, SKU/barcode uniqueness, variant attributes, and same-tenant parent product lineage. It is not visibility authority by itself.
- Marketplace inventory authority is availability authority owned by current SQL warehouse stock, reservations, stock movements, active warehouse lineage, current variant semantic ownership, and same-tenant active product lineage.
- Cart, checkout, projection, search, detail hydration, reindex, replay, and recovery may use derivative stock or variant evidence only as candidates. Current SQL variant lifecycle plus current inventory lineage decides whether a variant can be visible, purchasable, reserved, consumed, or indexed.
- Deleted, inactive, detached, superseded, replaced, unpublished, offline, invalid-warehouse, stale-stock-owner, expired-reservation, released, orphaned, or cross-tenant rows must fail closed. Replay and recovery must suppress, delete, tombstone, no-op, reconcile, or block instead of resurrecting stock or variants.
- PASS 12.5 findings are recorded in `docs/architecture/marketplace-variant-inventory-certification-findings.md`.

## 2026-05-31 POS-ADMIN-CERTIFICATION-01 PASS 12.6 Merchant vs Marketplace Governance Precedence Certification Truth

- Merchant authority is request authority. Marketplace governance is decision authority. Current SQL governance authority wins when merchant intent conflicts with governance.
- Publish and update may request publication only while current vendor governance, product publication, public variant lineage, category lineage, tenant activity, pricing, inventory, and shipping authority accept the listing. Unpublish may only reduce visibility.
- Suppression and vendor suspension/ban cannot be silently removed by merchant update, replay, reindex, auto-healing, search hydration, homepage/discovery, or stale derivative documents. Restore intent is conditional evidence only and must be cleared when current SQL authority no longer accepts restoration.
- CommerceHub search, discovery, reindex, and recovery must suppress current SQL suspended or banned vendor governance before preserving or indexing listing visibility, even when `CommerceHubListings.IsPublished`, MongoDB, Redis, Elasticsearch, or replay payloads still claim visibility.
- PASS 12.6 findings are recorded in `docs/architecture/merchant-marketplace-governance-precedence-certification-findings.md`.

## 2026-05-31 POS-ADMIN-CERTIFICATION-01 PASS 13.3 CommerceHubPriceAuthority Resolver Truth

- `CommerceHubPriceAuthority` is the sole CommerceHub item price resolver boundary.
- Current final CommerceHub item price remains current SQL `ProductVariant.EffectiveOnlinePrice`; resolver adoption must not change customer-visible pricing.
- Override-shaped resolver output is future-proofing only. `CommerceHubPriceAuthority.OverridesEnabled` remains `false`, `CommerceHubListings.CommerceHubPrice` remains disabled/cleared/constrained, and derivative override fields remain null.
- Cart, checkout, projection, search overlay, query/detail hydration, reindex, replay, recovery, auto-healing, and startup rebuild paths must resolve or inherit CommerceHub item price through `CommerceHubPriceAuthority`.
- The resolver is commercial price authority only. It may not create visibility, publication, category, vendor-governance, variant, media, inventory, or tenant authority.
- PASS 13.3 findings are recorded in `docs/architecture/commercehub-price-authority-resolver-certification-findings.md`.

## 2026-05-31 POS-ADMIN-CERTIFICATION-01 PASS 13.4 CommerceHub Price Override Persistence Truth

- CommerceHub price override persistence is prepared but inactive. SQL audit metadata may exist, but it is not runtime price authority.
- `CommerceHubListings.CommerceHubPrice` remains the disabled compatibility storage field. It must stay rejected by domain methods, cleared by DbContext protection, and constrained to null until a future activation certification changes the authority model.
- `CommerceHubPriceOverrideUpdatedAtUtc`, `CommerceHubPriceOverrideUpdatedBy`, and `CommerceHubPriceOverrideReason` are future attribution metadata only. They do not create visibility, approval, publication, cart, checkout, or search price authority.
- CommerceHub Mongo and Elasticsearch `PriceSource` / `OverridePresent` fields are derivative diagnostics only. Current SQL variant pricing and `CommerceHubPriceAuthority` still decide final CommerceHub item price.
- Controlled override activation still requires merchant manage permission, marketplace audit/emergency-disable permissions, merchant mutation workflow, admin UI, immutable audit trail, rollout flags, cart/checkout/search/projection parity certification, operational dashboards, migration preflight, rollback, and updated regression freezes.
- PASS 13.4 findings are recorded in `docs/architecture/commercehub-price-override-persistence-audit-certification-findings.md`.

## 2026-05-31 POS-ADMIN-CERTIFICATION-01 PASS 13.6 CommerceHub Price Override Controlled Activation Truth

- Override activation is prohibited until all rollout controls are certified. No partial activation is permitted.
- Current final CommerceHub item price remains SQL `ProductVariant.EffectiveOnlinePrice` through `CommerceHubPriceAuthority`.
- The disabled resolver constant, entity rejection, EF save guard, SQL null constraint, and derivative clearing remain required safety locks.
- Attribution columns alone are not sufficient auditability. Activation requires immutable audit events, override-specific governance, tenant-safe flag rollback, parity monitoring, and operator tooling.
- PASS 13.6 findings are recorded in `docs/architecture/commercehub-price-override-controlled-activation-certification-findings.md`.

## 2026-05-31 POS-ADMIN-CERTIFICATION-01 PASS 13.5 CommerceHub Price Override Governance Truth

- CommerceHub price overrides remain disabled. Governance design, rollout controls, and rollback design do not activate pricing authority.
- Tenant merchants own CommerceHub price override authority. Marketplace governance owns visibility, suppression, suspension, emergency disable, audit, rollback, and marketplace integrity only. It may not approve, reject, create, or edit merchant prices.
- Marketplace approval is prohibited. Future merchant workflow is optional `Draft`, then `Saved`, `Updated`, or `Removed`; stale merchant mutation replay must fail closed.
- Future runtime use requires a default-off global emergency kill switch, default-empty tenant allowlist, optional cohorts, current-version merchant records, immutable events, parity monitoring, and non-destructive fallback to `ProductVariant.EffectiveOnlinePrice`.
- PASS 13.5 findings are recorded in `docs/architecture/commercehub-price-override-governance-rollout-certification-findings.md`.

## 2026-05-31 POS-ADMIN-CERTIFICATION-01 PASS 13.5B CommerceHub Price Override Business Model Correction Truth

- CommerceHub price authority owner is the tenant merchant. CommerceHub visibility authority owner is marketplace governance.
- Marketplace governance may hide or suppress listings, suspend vendors or tenants, emergency-disable override runtime, audit activity, and preserve marketplace integrity.
- Marketplace governance may not approve prices, reject prices, create prices, or edit merchant prices. Audit must never become approval.
- Runtime override activation remains prohibited. Replay, recovery, reindex, projection, search, and the centralized resolver remain SQL-first, with current SQL authority winning.
- PASS 13.5B findings are recorded in `docs/architecture/commercehub-price-override-business-model-correction-certification-findings.md`.
## PASS 13.7 CommerceHub Price Override Implementation Truth

CommerceHub pricing is merchant-owned. `CommerceHubPriceAuthority` is the single runtime resolver and honors persisted positive listing overrides only when the default-off global kill switch, tenant allowlist, and optional cohort policy allow that tenant. Kill-switch rollback is non-destructive. Mongo projection, Elasticsearch SQL overlay, cart, checkout, replay, reindex, auto-healing, and recovery must converge from current SQL listing and variant authority through that resolver. Marketplace governance remains visibility authority only.
## 2026-05-31 POS-ADMIN-CERTIFICATION-01 PASS 14.1 CommerceHub Product Publication Lifecycle Certification Truth

- Merchant publication intent is tenant-owned request authority. Marketplace governance owns visibility. Current SQL listing, tenant, governance, product, variant, category, attribute, pricing, stock, and media lineage decides public visibility and purchasability.
- Withdrawal must remain possible after catalog authority is removed because it only reduces visibility. Unpublish must clear stale governance restore intent without requiring an active, undeleted, current variant.
- Projection, search, homepage/discovery, replay, reindex, and auto-healing are derivative convergence paths and must recompose current SQL authority before indexing, serving, or restoring visibility.
- PASS 14.1 findings are recorded in `docs/architecture/commercehub-publication-lifecycle-certification-findings.md`.

## 2026-06-01 POS-ADMIN-CERTIFICATION-01 PASS 14.2 Merchant CommerceHub Operations Certification Truth

- PASS 14.1 lifecycle authority remains valid, but merchant operational UX is only partially certified.
- The settings page is an active-catalog single-listing editor, not an authoritative listing operations dashboard. Client-side product, variant, and published counts must not be treated as full operational KPIs because suppressed, orphaned, drifted, and derivative-freshness states are outside that summary.
- Merchant-facing validation failures must preserve actionable backend authority outcomes.
- Production certification requires server-side listing search, operational filters, suppression and orphan cleanup visibility, authoritative KPI APIs, bulk workflows with per-row outcomes, dedicated merchant notifications, listing analytics, and parity/drift monitoring.

## 2026-06-01 POS-ADMIN-CERTIFICATION-01 PASS 14.3 Merchant Listing Operations Console Certification Truth

- Merchant listing operations console reads are SQL-authoritative. Search, filters, KPIs, suppression visibility, orphan visibility, analytics, and notifications must derive from current SQL CommerceHub listing, product, variant, category, search, and order authority, not client-side active-catalog summaries or derivative search documents.
- Merchant bulk operations are tenant-scoped intent/cleanup workflows with per-row outcomes. Bulk publish and category update may not override marketplace suppression or resurrect orphaned product/variant/category authority. Bulk unpublish remains visibility-reducing cleanup authority and may operate on stale administrative listing rows.
- Merchant visibility into suppression is diagnostic only. Marketplace governance owns visibility, suppression, vendor suspension, tenant suspension, and emergency decisions; merchant UI must not convert suppression reason display into governance control.
- Remaining production maturity debt is parity/drift monitoring, alert dashboards, bulk attribute editing, immutable notification inbox, and deeper analytics segmentation.

## 2026-06-01 POS-ADMIN-CERTIFICATION-01 PASS 15.1 Marketplace Governance Console Certification Truth

- Super Admin marketplace governance console reads and actions are SQL-authoritative. Dashboard counts, listing search, visibility filters, suppression state, orphan state, vendor governance state, tenant lifecycle state, and audit activity derive from PostgreSQL.
- Marketplace governance owns visibility only. Super Admin listing suppression/restoration and tenant suspension/restoration must not mutate merchant-owned publication intent, category selection, attributes, or CommerceHub price overrides.
- Listing restoration is conditional authority, not a resurrection shortcut. Restore may publish again only while current SQL tenant, vendor governance, product publication/online visibility, current variant lifecycle, and live CommerceHub category lineage accept visibility; otherwise restore intent is cleared and the action fails closed.
- Tenant governance is SQL tenant lifecycle authority. Suspending a tenant must remove active tenant authority and queue derivative visibility verification; restoring a tenant must not independently restore listings unless listing/product/vendor/category/variant authority also accepts visibility.
- PASS 15.1 findings are recorded in `docs/architecture/marketplace-governance-console-certification-findings.md`.

## 2026-06-01 POS-ADMIN-CERTIFICATION-01 PASS 15.2 Marketplace Operations Console Certification Truth

- Super Admin marketplace operations are operational convergence controls only. Reindex, catch-up reindex, listing verification, tenant verification, startup recovery, background reconciliation, and auto-healing may repair MongoDB and Elasticsearch, but they cannot create listing, publication, tenant, vendor, category, product, variant, price, or inventory authority.
- CommerceHub operations may discover candidates from MongoDB, Elasticsearch, search sync watermarks, health checks, queue state, dashboards, and replay/recovery inputs. Before derivative mutation they must recompose current PostgreSQL authority through reviewed SQL overlays and delete, no-op, suppress, or fail closed when authority is absent.
- Global reindex and tenant reindex are SQL-authoritative repair tools. They must preserve tenant scope, remove stale search documents absent from current SQL-visible authority, and avoid resurrecting deleted, suppressed, orphaned, inactive, suspended, invalid-category, or superseded-variant listings.
- Diagnostics are trustworthy only when they distinguish SQL authority counts from derivative MongoDB, Elasticsearch, and auto-healing queue state. A derivative count is never marketplace truth by itself.
- PASS 15.2 findings are recorded in `docs/architecture/marketplace-operations-console-certification-findings.md`.

## 2026-06-01 POS-ADMIN-CERTIFICATION-01 PASS 15.3 Marketplace Finance & Risk Certification Truth

- CommerceHub internal money authority is PostgreSQL plus the append-only financial/accounting ledger. MongoDB, Redis, Elasticsearch, frontend snapshots, provider callbacks, payout rail responses, and dashboard counters are evidence only.
- Order totals and item prices are fixed by checkout SQL authority and PASS 13.7 `CommerceHubPriceAuthority`. Finance consumes persisted order/sub-order snapshots and may not recalculate item price from derivative state.
- Commission, vendor earning, refund, payout, and settlement-like payout ledger authority must pass SQL snapshot validation, cumulative amount validation, ledger confirmation, currency checks, serializable transaction boundaries, and immutable ledger fingerprint idempotency before outward money movement or balance mutation.
- Replay may append only the same financial fact; divergent idempotency/fingerprint collisions fail closed. Recovery may only reload current SQL evidence and route corrections through the same ledger/refund/payout services; it may not update or delete ledger facts.
- PASS 15.3 findings are recorded in `docs/architecture/commercehub-finance-risk-certification-findings.md`.

## 2026-06-01 POS-ADMIN-CERTIFICATION-01 PASS 15.4 CommerceHub Emergency Operations Truth

- CommerceHub emergency authority is operational authority only. It may stop visibility, stop payment intake, stop outward money movement, suppress, suspend, isolate, verify, reindex, diagnose, and recover derivative state, but it may not create SQL, ledger, tenant, product, variant, category, inventory, payment, refund, payout, or publication authority.
- `CommerceHub:EmergencyStops` is a default-off incident control bound into `CommerceHubFinancialReadinessGate`. It blocks payment intake and outward money movement before checkout/payment, payout, or refund execution, while preserving SQL/ledger/reconciliation as financial truth.
- Marketplace visibility stop authority remains SQL governance authority: listing suppression, tenant suspension, vendor governance, search overlays, auto-healing verification, and SQL-derived reindex may reduce visibility; restoration must revalidate current tenant/vendor/product/variant/category authority.
- Recovery and rollback must converge from SQL through existing reindex, verification, replay, lifecycle, and reconciliation paths. Derivative state, queue payloads, dashboards, and exports are never rollback authority.
- PASS 15.4 findings are recorded in `docs/architecture/commercehub-emergency-operations-certification-findings.md`.

## 2026-06-01 POS-ADMIN-CERTIFICATION-01 PASS 15.5 Marketplace Audit, Compliance & Forensics Truth

- Forensic evidence is a production authority concern, not a dashboard concern. Critical audit, governance, ledger, accounting, dead-letter, and operator records must be append-only at reviewed write boundaries.
- Super Admin operator evidence must persist durably to SQL and may not depend on a process-memory queue. Merchant listing and recovery-operation history require durable lifecycle evidence before full forensic certification.
- Published outboxes, processed-event fences, logs, telemetry, MongoDB, Redis, Elasticsearch, and dashboards are supporting evidence only. Their retention or visibility does not replace durable business-event and ledger history.
- PASS 15.5 findings are recorded in `docs/architecture/marketplace-audit-compliance-forensics-certification-findings.md`.

## 2026-06-01 POS-ADMIN-CERTIFICATION-01 PASS 15.5B Forensic Evidence Hardening Truth

- CommerceHub merchant lifecycle and certified recovery-operation ledgers are append-only SQL forensic evidence. Events, outboxes, logs, telemetry, MongoDB, Redis, Elasticsearch, and dashboards remain supporting evidence only.
- Generic tenant audit writes persist to isolated SQL first and use the process queue only as retry fallback. Retry depth/count must remain visible; a future SQL audit outbox is still required for PostgreSQL-outage process-crash durability.
- Unified forensic search and export are operator evidence-discovery controls. Export payloads are transfer artifacts until an external SIEM or object store applies WORM retention.
- PASS 15.5B findings are recorded in `docs/architecture/marketplace-forensic-evidence-hardening-certification-findings.md`.

## 2026-06-01 POS-ADMIN-CERTIFICATION-01 PASS 15.6 Marketplace Production Readiness Certification Truth

- CommerceHub is **NOT READY** for public production launch until deployment-enforced forensic, retention, alerting, backup, restore, and operator-rehearsal controls are proven.
- Runtime SQL authority, tenant authority, merchant intent, governance visibility, finance ledger authority, derivative search/projection behavior, and SQL-first recovery remain the certified architecture.
- PostgreSQL evidence-table roles, external WORM/SIEM retention and delivery, outage-surviving tenant-audit handoff, verified alert packs and dashboards, and deployment rehearsal evidence are launch prerequisites rather than post-launch maturity work.
- PASS 15.6 findings are recorded in `docs/architecture/marketplace-production-readiness-certification-findings.md`.

## 2026-06-01 PRODUCTION READINESS CLOSURE 1 Evidence Database Hardening Truth

- Certified audit, governance, finance, accounting, dead-letter, merchant-lifecycle, and recovery-operation evidence tables are PostgreSQL-enforced append-only stores.
- Migration `20260601190000_EvidenceDatabaseHardening` installs database triggers that reject `UPDATE`, `DELETE`, and `TRUNCATE` with `FORENSIC_APPEND_ONLY_VIOLATION`, including direct SQL paths that bypass EF guards.
- Deployment role provisioning transfers evidence-table ownership to `pointofsale_evidence_owner`. Application and background-worker identities receive `SELECT` / `INSERT` only; reporting and maintenance identities receive `SELECT` only; reviewed migration authority remains separate.
- The hardening migration is intentionally irreversible. Evidence schema evolution must use reviewed forward migrations and post-deployment verification.
- PASS 15.6 Launch Blocker #1 is closed in repository implementation and is eliminated in an environment only after provisioning and verification evidence are captured.

## 2026-06-01 PASS 16.1 Customer Identity Certification Truth

- CommerceHub customer identity is global SQL authority through `CommerceHubCustomers`; frontend state, cookies, JWT claims, carts, and derivative state do not create customer ownership.
- Registration must commit customer identity and the required governance profile in one SQL transaction. Normalized email uniqueness remains database-owned, and concurrent duplicate registration must collapse to `EMAIL_EXISTS`.
- Customer access JWTs remain HttpOnly-cookie-backed, CSRF-protected, JTI-revocable, and subordinate to the durable `TokensValidAfterUtc` cutoff plus current customer governance state. Suspended or banned customers must not obtain or retain authenticated access.
- Guest browsing and cart use are allowed. Guest-to-account transition must preserve the guest snapshot until SQL-backed authenticated cart adoption succeeds; logout must not leak customer-scoped cart state to a later guest.
- CommerceHub customer identity is not launch-certifiable until password recovery, email verification/resend, customer profile mutation, password/email change, customer-facing session controls, append-only identity evidence, and identity-specific monitoring are implemented and certified.
- PASS 16.1 findings are recorded in `docs/architecture/customer-identity-certification-findings.md`.

## 2026-06-01 PASS 16.1B Customer Identity Runtime Completion Truth

- CommerceHub password reset, verification, profile, password-change, and email-change mutations remain global PostgreSQL customer-identity authority.
- Registration must not create browser session authority before mailbox verification, and unverified customers must not pass CommerceHub login.
- Reset and verification raw tokens may exist only transiently for delivery through `IEmailSender`; PostgreSQL stores SHA-256 hashes only. Consumption must remain conditional and atomic so replay races collapse to one winner.
- Identity lifecycle evidence is append-only SQL evidence protected by EF guards and PostgreSQL trigger enforcement.
- Runtime implementation does not equal launch approval. Refresh-token rotation, deployment-applied schema and roles, SMTP proof, external retention, dashboards, alerts, and database-backed chaos evidence remain prerequisites.

## 2026-06-01 PASS 16.2 Product Discovery Certification Truth

- CommerceHub customer product discovery is derivative visibility. PostgreSQL listing, tenant, vendor governance, product publication, variant lifecycle, CommerceHub category lineage, category attribute schema, media, price, inventory, review, and suppression authority decides whether a product can appear on homepage, search, autocomplete, category, filter, card, detail, related, featured, promoted, popular, or deep-link surfaces.
- Elasticsearch, MongoDB, Redis, homepage section JSON, popular-search analytics, autocomplete caches, frontend URL state, and search telemetry are candidate, ranking, cache, or evidence layers only. They may never create product visibility.
- CommerceHub search/detail/suggestion/autocomplete/list responses must recompose returned products through `CommerceHubSearchSqlAuthorityOverlay` or an equivalent current-SQL authority boundary before response. Search counts and facets on certified paths must reflect SQL-visible returned items, not raw derivative hits.
- Homepage and recommendation-like surfaces must filter current SQL listing/category/vendor visibility before response. A future personalized recommendation engine must call the same SQL authority boundary before returning or rebuilding recommendations.
- Direct product and category links are safe only while current SQL authority accepts the listing/category. Deleted, suppressed, unpublished, invalid-lineage, inactive, suspended, or superseded authority must collapse to absence rather than stale detail or stale category visibility.
- PASS 16.2 findings are recorded in `docs/architecture/commercehub-product-discovery-certification-findings.md`.
## 2026-06-01 PASS 16.3 Cart Certification Truth

- CommerceHub cart state is customer intent until backend sanitation, pricing, or checkout recomposes current SQL authority. PostgreSQL listing, tenant, product, variant, category, stock, price, promo, shipping, vendor governance, customer governance, and readiness authority decide whether a cart line can be persisted, priced, or checked out.
- Browser guest carts, local storage, frontend summaries, price snapshots, displayed stock, and shipping display values are not business truth. They may seed a save/pricing/checkout request only after backend revalidation.
- Authenticated cart ownership is `CommerceHubCustomerId` resolved from the CommerceHub customer authentication scheme. Raw client customer IDs and legacy `OnlineCustomerId` must not become cart ownership authority.
- Multi-tab and multi-device mutations must remain conflict-aware through cart versioning, checkout customer/idempotency locks, persisted cart locks, request-hash-bound checkout idempotency, and final SQL authority recomputation.
- Guest-to-customer adoption must preserve the guest snapshot until backend sanitation and persistence succeeds. Future backend merge work must retain duplicate-line quantity reconciliation, stock/price revalidation, and ownership/version conflict behavior.
- Cart sanitization must enforce full product publication visibility (`IsOnlineVisible`, `PublishStatus == Published`) with parity to `CalculateAsync` and checkout authority. Products that are active but offline or draft must be removed during sanitization with an `ITEM_REMOVED_UNAVAILABLE` notice.
- PASS 16.3 findings are recorded in `docs/architecture/commercehub-cart-certification-findings.md`.

## 2026-06-01 PASS 16.4 Checkout Certification Truth

- CommerceHub checkout is a backend SQL-authoritative conversion boundary. Frontend cart payloads, displayed totals, price snapshots, shipping selections, payment-method selections, local idempotency keys, recovery context, MongoDB, Elasticsearch, Redis, and provider handoff state are request intent or evidence only.
- A valid checkout requires authenticated CommerceHub customer authority, current customer governance, per-customer checkout locking, request-hash-bound idempotency, saved-cart row locking/version match, and serializable SQL order creation.
- Checkout must recompose current SQL listing, product, variant, tenant, vendor governance, payment-method, shipping, promo, price, inventory, commission, financial-readiness, and customer/order authority before committing an order. Stale or missing authority must fail closed rather than silently repricing, substituting stock, or accepting client totals.
- Inventory reservation must precede order commit and stay tied to marketplace order, sub-order, and item identity. Payment retry must not proceed after reservation authority is lost; customers must restart checkout from current SQL truth.
- Online payment preparation is not payment success. Checkout may create a pending order and payment handoff marker, but payment provider execution, callbacks, retry, captured-money ambiguity, and recovery remain payment lifecycle/reconciliation authority.
- PASS 16.4 findings are recorded in `docs/architecture/commercehub-checkout-certification-findings.md`.

## 2026-06-01 PASS 16.5A Order & Fulfillment Lifecycle Certification Truth

- CommerceHub order lifecycle authority is PostgreSQL. `CommerceHubMarketplaceOrder` owns marketplace/customer aggregate order state and `CommerceHubSubOrder` owns vendor fulfillment state; MongoDB order documents, Redis, Elasticsearch, frontend state, and status events are derivative evidence only.
- Customer visibility is owned by `CommerceHubMarketplaceOrder.CustomerId` plus signed order-access token validation against SQL metadata. POS Admin fulfillment mutation is owned by the current tenant only through `CommerceHubSubOrder.StoreId`; raw client tenant ids and derivative documents are not fulfillment authority.
- CommerceHub order/sub-order lifecycle fields may be persisted only inside `CommerceHubBoundaryMutationScope.BeginOrderLifecycle()`. POS Admin status changes must pass `CommerceHubOrderStateHelper` transition validation, serializable row locks, payment readiness checks, inventory release/commit side effects, status history, and projection event publication.
- Super Admin order surfaces are SQL read/search/export surfaces unless a future explicit audited repair workflow is introduced. Super Admin return/refund operations are explicit lifecycle workflows and may not become silent order-state override authority.
- Cancellation, delivery, return, refund, replay, recovery, and projection repair must converge from current SQL state. Terminal cancellation, delivery, completion, and refunded outcomes must not be resurrected by stale projections, replayed events, recovery candidates, or frontend action state.
- PASS 16.5A findings are recorded in `docs/architecture/commercehub-order-fulfillment-lifecycle-certification-findings.md`.

## 2026-06-02 PASS 16.5B Settlement & Merchant Balance Certification Truth

- CommerceHub settlement, merchant balances, commission debt, online-payment payables, payout eligibility, refund impact, and finance dashboards are PostgreSQL-ledger-derived authority. `CommerceHubFinancialTransaction` plus order/sub-order/refund/payout/settlement/payment-request SQL evidence owns internal money truth.
- COD money ownership is split intentionally: the merchant holds customer cash, while the platform owns a SQL receivable for commission debt. Online-payment money is platform-held until SQL ledger-confirmed vendor payable is paid out.
- COD debt threshold enforcement is hard authority, not dashboard guidance. `amountOwed >= 1000 EGP` must immediately suspend new CommerceHub order acceptance and public vendor/listing visibility; any configured grace period is operational messaging only.
- Settlement and vendor debt-repayment records require idempotency, external evidence, actor/reason evidence, mirrored ledger posting, serializable financial state checks, and append-only audit evidence. Reconciliation can prove internal SQL correctness but cannot prove external gateway or bank outcomes without external evidence.
- PASS 16.5B findings are recorded in `docs/architecture/commercehub-settlement-merchant-balance-certification-findings.md`.

## 2026-06-02 PASS 16.5C COD Risk & Debt Control Certification Truth

- COD risk-control authority is PostgreSQL-ledger-derived. `CommerceHubVendorDebtService` recomposes current vendor debt from SQL commission, settlement, adjustment, repayment, and refund evidence; dashboards, search indexes, MongoDB, Redis, frontend state, and cached visibility are not debt authority.
- Hard-limit suspension is authoritative at `amountOwed >= threshold` and the default hard threshold is 1000 EGP. Threshold-crossing evidence must also be recorded at the exact hard limit; only current debt below the threshold clears ordering suspension.
- Checkout, public product discovery, search, autocomplete, homepage, product detail, and recommendation-like surfaces must call current SQL vendor suppression/debt authority before accepting new orders or returning public visibility. Existing orders continue through order/fulfillment/refund/settlement lifecycle authority and must not be cancelled solely by vendor COD suspension.
- Reactivation is automatic only when current SQL debt falls below the hard threshold through approved ledger-backed settlement, clearance, repayment approval, refund reversal, or adjustment paths.
- PASS 16.5C findings are recorded in `docs/architecture/commercehub-cod-risk-control-certification-findings.md`.

## 2026-06-02 PASS 16.5D Payout & Reconciliation Certification Truth

- Merchant payout identities are vendor-scoped PostgreSQL authority through `CommerceHubVendorPayoutMethod.VendorId`. Merchant APIs must derive vendor identity from trusted tenant context; request payloads, frontend state, MongoDB, Redis, Elasticsearch, and dashboard rows are not payout identity authority.
- Super Admin payout execution is platform operation authority. It may view vendor payout identities for execution, but every execution must revalidate current `vendorId` plus `payoutMethodId` ownership, current platform actor authority, external transfer evidence, idempotency, platform cash, eligible vendor payable, final readiness, and ledger posting before internal payout truth is recognized.
- Completed payouts are SQL/ledger facts backed by terminal external evidence. `ExternalReferenceId` and `BankTransactionId` remain platform cash-rail evidence and must be globally unique across non-failed CommerceHub payouts, not merely unique per vendor.
- Payout reconciliation can prove internal SQL correctness for payout rows, earning allocations, ledger debits, balances, and evidence presence. External bank/provider outcomes remain external truth and require explicit evidence; uncertain outcomes must remain `Processing` or `ReconciliationRequired`.
- Refunds must not rewrite completed payout history. Post-payout refund impact is modeled through debt or adjustment authority, and active/processing payout overlap remains blocked before refund completion.
- PASS 16.5D findings are recorded in `docs/architecture/commercehub-payout-reconciliation-certification-findings.md`.

## 2026-06-02 PASS 16.5E Refund & Financial Reversal Certification Truth

- CommerceHub return, refund, and financial reversal authority is PostgreSQL/ledger-first. `CommerceHubRefundRequest`, `CommerceHubReturnRequest`, `CommerceHubRefund`, `CommerceHubRefundExecution`, refund evidence, financial transactions, ledgers, and audit rows own internal refund truth; frontend state, dashboards, MongoDB, Redis, Elasticsearch, queue payloads, and external callbacks are evidence only.
- Customer refund authority is request intent only and must prove customer/order-item ownership, idempotency, eligibility, payout snapshot, and evidence ownership before a refund request is persisted. Super Admin owns approval, rejection, retry preparation, and manual refund execution. POS Admin owns tenant-scoped receipt confirmation for returns through trusted tenant context.
- Refund execution must remain serializable, readiness-gated, idempotent, payout-overlap blocked, cumulative-overrun checked, and ledger-backed. COD refunds adjust commission debt; online refunds adjust platform cash/vendor payable before payout, and post-payout refunds create debt/adjustment authority without rewriting completed payout history.
- Recovery and replay must reload current SQL and collapse through terminal states, idempotency keys, row locks, reconciliation-required states, and ledger fingerprints. They may not resurrect failed, paid, completed, or reconciled refund/return states from stale projections or worker candidates.
- PASS 16.5E findings are recorded in `docs/architecture/commercehub-refund-financial-reversal-certification-findings.md`.

## 2026-06-02 PASS 16.5F-A CommerceHub Currency Authority Certification Truth

- POS Admin and storefront currency remain tenant-scoped authority through `Tenant.Currency`; CommerceHub marketplace currency is platform authority and is EGP only through `FinancialCurrencyGuard.PlatformBaseCurrencyCode`.
- CommerceHub listing/search/cart/checkout/order/finance DTOs and persisted marketplace state must expose EGP authority. PASS 16.5F-B further restricts publication intake: until a separately certified FX/product-currency model exists, only EGP-priced catalog products may be published.
- Tenant-currency events, storefront config projection, existing Mongo documents, checkout/cart payloads, search documents, provider callbacks, vendor payout queue rows, and tenant settings are not CommerceHub currency authority.
- Price overrides are amount-only marketplace overrides and must resolve under EGP. Future override currency fields require a new certified FX/currency authority model before activation.
- PASS 16.5F-A findings are recorded in `docs/architecture/commercehub-currency-authority-certification-findings.md`.

## 2026-06-02 PASS 16.5F-B CommerceHub Publication Currency Policy Enforcement Truth

- CommerceHub is currently an Egypt marketplace, so CommerceHub publication authority is EGP-only. POS Admin and storefront may remain multi-currency, but non-EGP catalog price authority cannot enter CommerceHub publication.
- Current product price currency authority is `Tenant.Currency` because the inspected catalog model has no `Product.Currency` or `ProductVariant.Currency` fields. If a future product-level or variant-level currency authority is introduced, CommerceHub publication must move to that narrower source and keep EGP-only validation.
- Publish, republish, listing update, bulk publish, bulk update, replay, recovery, auto-healing, catch-up reindex, tenant reindex, and global reindex must reject or converge non-EGP listings to non-visible state. Unpublish remains allowed for visibility reduction and legacy cleanup.
- PASS 16.5F-B findings are recorded in `docs/architecture/commercehub-publication-currency-policy-certification-findings.md`.

## 2026-06-02 PASS 16.1B-A Authentication & Session Authority Certification Truth

- CommerceHub customer authentication authority is `Current SQL CommerceHubCustomer + Current Verification State + Current Governance State + Current JWT JTI/Cutoff State`. Frontend auth state, route guards, cookies, Redis blacklist cache, and JWT claims are evidence only unless validated against backend authority.
- Registration and email verification must not create authenticated access. Customer sessions may be created only by successful CommerceHub login after verified email, password validation, and governance approval.
- Customer JWT access must remain HttpOnly-cookie-backed, JTI-revocable, and subordinate to durable `TokensValidAfterUtc`. Logout must revoke current JTI and clear cookies; revoke-all, password reset, password change, and email change must advance the durable cutoff.
- Customer protected routes must explicitly bind to the `CustomerJwt` scheme and `Customer` role. Frontend route guards are UX controls only and must not be treated as backend authorization.
- PASS 16.1B-A findings are recorded in `docs/architecture/commercehub-authentication-authority-certification-findings.md`.
