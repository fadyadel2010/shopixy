# 2026-08-30 Affiliate Attribution Certification Registration

- PASS AF-PA4.4 Promotion Link Resolution, Storefront Entry & Attribution Admission source certification.
- PASS AF-PA5.1 Customer Journey Attribution Persistence source certification.
- PASS AF-PA5.2 Cart Attribution, Eligible Product Admission & Mixed Cart source certification.
- PASS AF-PA5.3 Checkout Attribution, Order Line Admission & Commission Candidate source certification.

# 2026-07-22 Storefront / Builder Runtime Certification Registration

- PASS SF-ADM2.3 Builder Section Composition Authority Certification.
- PASS SF-ADM2.4 Builder Layout Authority Certification.
- PASS SF-ADM2.5 Builder Theme Authority Certification.
- PASS SF-ADM2.6 Builder Section Content Authority Certification.
- PASS SF-ADM2.7 Builder Publish Authority Certification.
- PASS SF-ADM2.8 Builder Preview Authority Certification.
- PASS SF-ADM2.9 Builder Version History & Rollback Authority Certification.
- PASS SF-ADM2.FINAL Store Builder End-to-End Authority Certification.
- PASS SF-ADM3.4 Admin Order Fulfillment & Shipment Authority Certification.
- PASS SF-CMS2 CMS Page Identity Authority Certification.
- PASS SF-CMS3 CMS Content Authority Certification.
- PASS SF-CMS4 CMS SEO Authority Certification.
- PASS SF-CMS5 CMS Publish Authority Certification.
- PASS SF-CMS6 CMS Preview Authority Certification.
- PASS SF-CMS7 CMS Version History & Rollback Authority Certification.
- PASS SF-CMS.FINAL CMS End-to-End Authority Certification.
- P2-GD Storefront Navigation Authority remains production-certifiable SQL authority over live navigation reads and POS Admin navigation writes.

# 2026-07-17 PAYMENT-END-TO-END-1 Unified Payment Execution

- Added `docs/architecture/PAYMENT_END_TO_END_1_CERTIFICATION.md` and source-certified workflow output through the canonical Payment Intent execution pipeline into the Order Domain.
- Added `PaymentWorkflowExecutionService` to execute immutable workflow intents in order through `PaymentIntentDispatcher` without moving side effects into Workflow or business rules into the dispatcher.
- Completed canonical intent handler coverage with `QueueOperationalReviewHandler` and `CompleteAdmissionWorkflowHandler`; existing handlers now collapse already-applied replay before persistence or Order Domain event publication.
- Existing Order event flow remains the only synchronization path for POS Admin, Storefront, Affiliate Checkout, Marketplace Split Checkout, and future channels. No UI, provider, RabbitMQ, SignalR, or frontend dependency was introduced.
- Recommended next phase: `PAYMENT-PROVIDERS-1` for provider/channel expansion on top of the now-certified canonical execution pipeline.

# 2026-07-17 PAYMENT-INTENTS-1 Payment Intent Infrastructure

- Added `docs/architecture/PAYMENT_INTENTS_1_INFRASTRUCTURE.md` and implemented the first provider-neutral Payment Intent Infrastructure execution layer after Workflow.
- New boundary: immutable workflow intents adapt to `IPaymentWorkflowIntent`, `PaymentIntentDispatcher` resolves registered handlers, and handlers execute only Order Domain status actions, persist through the SQL order store, and publish authoritative Order Domain events through `IEventPublisher`.
- Implemented `AdvanceCollectionHandler`, `AwaitEvidenceHandler`, and `RejectAdmissionHandler`; reject admission refuses to mutate orders after money recognition and never writes `PaymentStatus`.
- Provider/channel neutrality preserved: no provider SDK/status/credential, UI, Storefront, CommerceHub, Affiliate, notification, or direct RabbitMQ dependency exists in the intent infrastructure.
- Completed by `PAYMENT-END-TO-END-1`; next payment phase is `PAYMENT-PROVIDERS-1` for provider/channel expansion on the certified execution pipeline.

# 2026-07-10 PAYMENT-1.2 Payment Policy Evaluation Pipeline

- Added `docs/architecture/PAYMENT_1_2_POLICY_EVALUATION_PIPELINE.md` as a design-only Payment Policy Evaluation Pipeline certification. No runtime, implementation, EF, migration, DTO/API/controller, provider adapter, frontend, policy persistence, rule authoring UI, governance UI, audit storage, or replay tooling was modified.
- Frozen pipeline: deterministic evaluation stages, `PipelineContext`, `PolicyContext` assembly, authority validation, fact normalization, completeness validation, version binding, policy discovery, policy ordering, rule-group execution, rule evaluation, conflict resolution, decision composition, audit recording, and decision return.
- `PipelineContext` is evaluation-local coordination state; `PolicyContext` remains the immutable provider-neutral input snapshot. The Pipeline coordinates evaluation but never owns facts, edits policies/rules, calls providers, mutates runtime state, or creates transactions.
- Pipeline evaluation is fail-closed and replay-safe: missing facts, invalid authority, version mismatch, missing mandatory policy/rule, replay failure, audit failure, unknown capability, and unknown constraint cannot produce executable allow decisions.
- Recommended next phase: `PAYMENT-1.3 Policy Persistence, Governance Workflow, and Replay Test Architecture`.

# 2026-07-10 PAYMENT-1.1 Payment Policy Framework

- Added `docs/architecture/PAYMENT_1_1_POLICY_FRAMEWORK.md` as a design-only Payment Policy Framework certification. No runtime, implementation, EF, migration, DTO/API/controller, provider adapter, frontend, policy persistence, rule authoring UI, or governance UI was modified.
- Frozen framework: provider-neutral Policy Contracts, canonical Facts Model, Rule Model, Policy Model, Rule Catalog, Fact Sources, Governance, Versioning, and Audit.
- Facts are authoritative inputs from current authority, immutable snapshots, or admitted evidence. Rules never own facts. Policies compose versioned rules. Governance owns policy/rule lifecycle. Runtime executes only policy-approved actions. Adapters expose capabilities/constraints/evidence only.
- Published policy, rule, catalog, fact schema, evaluation, audit, and replay versions are immutable and replay-safe. Corrections create new versions; rollback activates prior immutable versions and never rewrites decisions.
- Recommended next phase: `PAYMENT-1.2 Policy Persistence, Governance Workflow, and Replay Test Architecture`.

# Project Context

## PASS T8 Treasury Analytics Authority

- Treasury analytics is derivative and non-monetary. Dashboards, exports, projections, and summaries may calculate and cache financial views, but they must revalidate from tenant-scoped SQL ledger or CommerceHub financial transaction authority and cannot create money, settle money, post journals, or override SQL financial truth.

## PASS T6 Treasury Reconciliation Authority

- Treasury reconciliation is derivative and non-monetary. Reconciliation can detect drift, quarantine unsafe movement, and route recovery back through certified payment, refund, payout, settlement, or accounting authorities, but it cannot create money or substitute provider/bank evidence for internal SQL financial authority.

## PASS C7-G Settlement & Payout Final Currency Certification

- Settlement and payout final currency authority is EGP-only for CommerceHub platform money movement. Projection, queue, dashboard, settlement, and payout surfaces must preserve CommerceHub-owned EGP currency truth and must not relabel tenant/storefront currencies as platform settlement money.

## PASS S1-A Service Marketplace Listing Authority

- Service Marketplace Listing Authority is source-certified for SQL-revalidated search reads, visibility-qualified service detail, and DTO listing-intent hardening.

## PASS F-S1-A Service Marketplace Frontend Discovery Authority

- Service Marketplace Frontend Discovery Authority is source-certified for backend-qualified discovery DTO presentation, replayable URL state, fail-closed cache behavior, and bounded frontend filter/sort/pagination handling.

## PASS F-S1-C Service Marketplace Frontend Service Detail Presentation Authority

- Service Marketplace Frontend Service Detail Presentation Authority is source-certified for bounded frontend presentation over backend visibility-qualified service detail authority.

## Overall System Purpose

PointOfSale is a multi-tenant commerce operating platform. It combines POS/admin operations, tenant storefronts, a multi-vendor marketplace (`CommerceHub`), affiliate/referral workflows, financial ledgering, inventory/fulfillment, customer messaging, and analytics under one backend authority model.

The architectural pattern is:

- PostgreSQL is the transactional write authority.
- MongoDB, Redis, and Elasticsearch are read-performance/projection layers.
- RabbitMQ + MassTransit distribute integration events.
- Background reconciliation/repair services close gaps when projections or provider callbacks lag.

## Subsystems

| Subsystem | Primary Role | Authority Profile |
|---|---|---|
| `PointOfSale.API` | Main application host for REST APIs, auth, webhooks, SignalR, Hangfire, hosted workers | Orchestrates authoritative writes and operational workflows |
| `PointOfSale.Core` | Domain entities, enums, interfaces, domain/integration events | Business-rule contract |
| `PointOfSale.Infrastructure` | EF Core, MongoDB, Redis, Elasticsearch, RabbitMQ consumers, accounting, shipping, jobs | Adapters, projections, cross-system reliability |
| `PointOfSale.Web` | POS/admin SPA | Admin UI over authoritative APIs |
| `PointOfSale.Storefront` | Tenant-branded storefront SPA | Customer read surface over projected/storefront APIs |
| `PointOfSale.CommerceHub` | Marketplace/customer SPA | Multi-vendor discovery, cart, checkout, refunds, payouts |
| Affiliate APIs + projections | Affiliate auth, discovery, attribution, commission reporting | Global affiliate identity with store-scoped economics |
| `PointOfSale.WhatsApp.Baileys` | Node.js WhatsApp transport microservice | Transport/session state only, not business truth |
| `PointOfSale` | Minimal MVC shell | Legacy/non-core runtime surface |
| `PointOfSale.Admin` | Archived standalone storefront-admin app | Deprecated; functionality moved into `PointOfSale.Web` |

## System Ownership Boundaries

- POS/Admin owns catalog, pricing, tenant config, CMS draft/live publication, order/payment lifecycle, inventory state, accounting truth, vendor balances, and payout state.
- Category hierarchy and product default-category ownership are SQL-authoritative. Storefront category documents, affiliate category labels, CommerceHub category paths, search category documents, Redis caches, breadcrumbs, and rebuild payloads are derivative visibility only and must converge from PostgreSQL category lineage.
- Product media ownership, gallery ordering, and variant media linkage are SQL-authoritative through `ProductMedia` and `ProductVariants.MediaId`. Storefront product media documents, search thumbnails, affiliate thumbnails/product intelligence, CommerceHub listing images, CDN/cache state, and rebuild payloads are derivative visibility only and must converge from PostgreSQL media rows and tombstones.
- Multi-channel product publication is SQL-authoritative. PostgreSQL `Products.PublishStatus`, `Products.IsOnlineVisible`, `Products.IsActive`, deletion/archive state, and SQL CommerceHub listing publication state decide storefront, affiliate, CommerceHub, search, homepage/discovery, feed/export, category, media, stock, and promotion publication visibility; MongoDB, Elasticsearch, Redis, feed snapshots, event payloads, and retry/rebuild inputs are triggers or caches only.
- Product search visibility is SQL-authoritative for catalog existence, active/online/publication visibility, category lineage, pricing, stock, media, promotion overlays, and tombstones. Elasticsearch, MongoDB product documents, search replay events, rebuild inputs, autocomplete fields, affiliate search documents, and CommerceHub search documents are derivative visibility only and must converge from current PostgreSQL product/category/media/variant/listing authority before public search mutation.
- CommerceHub autocomplete, search suggestions, zero-result suggestions, and popular-search/product-name completion terms are derivative read surfaces. `CommerceHubQueryService` must recompose suggestion products from current SQL listing/product/variant/category/media/price/stock/publication authority through the CommerceHub search SQL overlay before returning cached, Elasticsearch, or MongoDB results, and must filter category/term suggestions against live SQL CommerceHub visibility.
- CommerceHub product-detail hydration is a derivative public read surface. `CommerceHubQueryService.GetProductByIdAsync(...)` must recompose current SQL listing/category/media/stock/price/review authority through the CommerceHub search SQL overlay before serving Mongo-backed detail data, and detail variant/media/category fields must not restore legacy listing/product/variant derivative state when SQL authority has been removed.
- Homepage, recommendation, featured-product, trending/new-arrival, curated shelf, affiliate discovery, and CommerceHub homepage/discovery visibility are derivative read surfaces. PostgreSQL product/listing/category/media/stock/price/promotion/publication authority decides whether a homepage or recommendation item may be served; Redis, MongoDB, Elasticsearch ranking/search results, homepage section JSON, and replayed rebuild payloads may only trigger or cache visibility.
- Admin product-variant repair and product read-model rebuild tooling is not recommendation or publication authority. AdminSync rebuilds must use SQL product/category/media/variant/stock/price/promotion/publication recomposition and must tombstone deleted, unpublished, inactive, offline, or invalid-lineage products instead of full-replacing Mongo documents from stale repair state.
- Product cache invalidation is infrastructure fanout, not authority. Product create/update/delete/stock/price/publication cache events must collapse duplicate active replay before Redis product-list, product-detail, promotion, or homepage pattern invalidation, and current SQL product data must be preferred over stale payload cache keys when available.
- Storefront product read-model mutation is SQL-authoritative visibility repair only. Product create/update lifecycle consumers and admin product rebuild tooling must route Mongo `ProductDocument` writes through the shared `IProductSqlAuthorityProjectionWriter`, which recomposes current SQL product/category/media/variant/price/stock/publication authority through `ProductSearchSqlAuthorityOverlay` and converges to freshness-aware tombstones when SQL no longer proves visibility.
- Storefront simple-product purchasability is SQL-authoritative through current `ProductUnits` plus the current default unit selection. `ProductDocument.DefaultUnit` and `ProductDocument.Units` are storefront-authority-critical fields, must be recomposed from SQL on every shared writer/rebuild/search-overlay path, and a simple product without a current SQL default unit is non-purchasable even if legacy product-level price fields remain populated.
- Storefront product promotion refresh is overlay-only, not full-document authority. Promotion fan-out may mutate only promotion overlay fields on `ProductDocument`; direct full `ProductDocument` replacements outside `ProductReadRepository` internals, `IProductSqlAuthorityProjectionWriter`, or the reviewed `IStorefrontProductProjectionMutationBoundary` are governance violations because they can bypass SQL-authoritative unit/default-unit, media, category, publication, and tombstone recomposition.
- Storefront and CommerceHub customer surfaces do not own authoritative business state; they consume APIs plus projected read models.
- MongoDB documents are publish/read models, not authoritative records.
- Redis is cache/session/lock/backplane infrastructure, not business truth.
- Elasticsearch is search/discovery infrastructure, not business truth.
- RabbitMQ/MassTransit deliver events, but message delivery is not the source of truth; the SQL commit boundary is.
- Shipment services own shipment rows and shipment/order projection events only. Fulfillment lifecycle truth, including delivery events that trigger COD/accounting/affiliate flows, is owned by `IOrderFulfillmentService`.
- WhatsApp transport owns session/socket lifecycle only; tenant/account/business rules stay in the main platform.

## Technologies Used

| Layer | Technologies |
|---|---|
| Backend | .NET 9, ASP.NET Core, EF Core 9, C# |
| Primary data | PostgreSQL, Hangfire PostgreSQL storage, MassTransit EF outbox/inbox |
| Read models | MongoDB |
| Cache/realtime | Redis, SignalR, StackExchange.Redis |
| Messaging | MassTransit, RabbitMQ |
| Search | Elasticsearch via NEST |
| Frontend | React 18, Vite, TypeScript/JavaScript, Tailwind CSS, Axios, Zustand |
| Edge service | Node.js 20+, Express, TypeScript, Baileys |
| Storage | AWS SDK S3 client with R2/S3-compatible object storage |
| Observability | OpenTelemetry, Prometheus exporter, optional OTLP |
| Edge/proxy | Caddy |

## Databases Used

| Store | Purpose |
|---|---|
| PostgreSQL | Primary system of record for tenants, catalog, inventory, orders, payments, fulfillment, affiliate write-side state, CommerceHub finance, ledger/accounting, auth, outboxes, and Hangfire |
| MongoDB | Read models for storefront products/categories/pages/navigation/config/orders, CommerceHub products/orders, affiliate dashboards/orders/earnings, marketplace review/read models, analytics cold storage, processed-event inboxes |
| Redis | Tenant-scoped cache, distributed locks, rate-limiting support, JWT blacklists, WhatsApp ephemeral state, session helpers, SignalR backplane |
| Elasticsearch | Product search, CommerceHub search, service discovery/public affiliate search documents |

## Multi-Tenant Architecture

- Tenant context is resolved by `TenantResolutionMiddleware`.
- Trusted resolution order is authenticated tenant claim first, then verified request host/custom domain.
- Raw inbound `X-Tenant-Id` is stripped before processing and only re-added after secure resolution.
- Most SQL entities are tenant-owned; `TenantSaveChangesInterceptor` blocks empty or mismatched `TenantId` writes.
- POS financial and inventory authority also requires tenant co-ownership of referenced tenant-owned records. `ApplicationDbContext` rejects cross-tenant invoice, invoice-line, stock, ledger, supplier-payment, and financial-transaction foreign-key edges before commit.
- Storefront tenancy is host-based, using `Storefront:UrlTemplate` and wildcard subdomains.
- MongoDB documents and Redis keys are tenant-scoped.
- Integration events carry tenant scope via `TenantId` and, for multi-store flows, `AffectedTenantIds`.
- Explicit exception: affiliate identity can be global/tenant-agnostic, but commission and store economics remain tagged with the originating store tenant.

## Deployment Architecture

- The main deployment unit is the `PointOfSale.API` ASP.NET Core process.
- The API hosts REST controllers, auth, SignalR, Hangfire workers, MassTransit bus integration, metrics, and multiple reconciliation/repair background services.
- `PointOfSale.Web`, `PointOfSale.Storefront`, and `PointOfSale.CommerceHub` are separate SPA build artifacts.
- Staging is documented as Caddy-fronted:
- `api-staging.yourdomain.com` -> API upstream
- `app-staging.yourdomain.com` -> POS/admin SPA
- `*.app-staging.yourdomain.com` -> storefront SPA
- `/api`, `/hubs`, and `/health` preserve the original `Host` header for secure tenant resolution.
- `PointOfSale.WhatsApp.Baileys` is deployed as a separate Node process and called over HTTP by the API.

## Infrastructure

- Caddy handles TLS termination, static SPA hosting, and reverse proxying.
- PostgreSQL, RabbitMQ, MongoDB, Redis, and Elasticsearch are required runtime dependencies.
- Hangfire runs recurring jobs; additional reconciliation services run as hosted background services inside the API host.
- Prometheus scrapes `/metrics`; optional OTLP export is supported.
- Data Protection keys are persisted to the filesystem.
- R2/S3-compatible object storage is used for media/evidence workflows.
- Secrets are expected from environment variables or an external secret manager, not committed appsettings.

## Integrations

- Payments: Stripe, Paymob, Fawry/manual wallet flows.
- Shipping/carriers: Bosta, Aramex, DHL, Mylerz.
- Messaging: WhatsApp via Baileys, with transport abstraction preserving fallback/migration paths.
- Automation: native Hangfire/MassTransit abandoned-cart recovery with audited WhatsApp dispatch.
- Email: SMTP.
- AI: Commerce/admin assistant routing through external LLM HTTP adapters.
- Observability: Prometheus and OTLP-compatible collectors.
- Object storage: R2/S3-compatible APIs.

## Docker Architecture

- No repository-authoritative `Dockerfile` or `docker-compose` is currently checked in.
- The documented live topology is host/process-based rather than repo-managed container orchestration.
- If containerized externally, the natural split is:
- Caddy/static frontend host
- `PointOfSale.API`
- `PointOfSale.WhatsApp.Baileys`
- PostgreSQL
- RabbitMQ
- MongoDB
- Redis
- Elasticsearch

## API Boundaries

- `api/admin/*`: authoritative admin/POS write APIs.
- `api/storefront/*`: storefront APIs; read-heavy and projection-oriented.
- `api/commercehub*`: marketplace/customer cart, checkout, order, refund, payout, and finance APIs.
- `api/affiliate/*` and `api/public/affiliate/*`: affiliate auth, discovery, public referral surfaces.
- `api/super-admin/*`: global control-plane APIs.
- Webhook controllers: payment providers and shipping/provider callbacks.
- `/hubs/chat`: SignalR real-time boundary.
- `/health`, `/metrics`, `/swagger`, `/hangfire`: operational interfaces.

## High-Risk Areas

- SQL authority commit succeeding while projection/outbox delivery lags or fails.
- Duplicate product publication/cache invalidation replay can amplify Redis homepage/product invalidation if a new cache path bypasses processed-event fencing.
- Storefront product projection completeness is now a hard correctness boundary: any future rebuild, repair, promotion, cache-warmer, or search/index path that drops `DefaultUnit`, `Units`, or simple-product purchasability semantics can silently corrupt public storefront availability even when SQL truth is correct.
- Payment-provider races: late captures, duplicate callbacks, compensation/outbox ordering.
- Provider-confirmed cash that arrives before normal local convergence now routes through suspense or reconciliation paths and remains high-sensitivity authority territory.
- Refund and payout flows that enter explicit reconciliation-required states.
- Inventory reservation cleanup versus unresolved external payment truth.
- Tenant isolation leakage through host resolution, event scope, cache keying, or projection bugs.
- Tenant co-ownership violations where a current-tenant financial or inventory row points at another tenant's party, warehouse, product, variant, unit, invoice, or account.
- Large authoritative backlog causing fail-closed readiness gates and tenant-level operational freezes.
- CommerceHub and affiliate multi-tenant events where affected-tenant scope is broader than a single scalar `TenantId`.
- Governance enforcement drift remains a validated risk class, but the focused enforcement boundary is restored: `CrossSystemAuthorityRegressionGuardTests` is green again and explicitly governs reviewed direct-SQL/bulk mutation files, high-risk Tier-1 `ReplaceOneAsync` paths, raw Elasticsearch client writes, and Redis pattern invalidation fanout through allowlists plus dedicated reviewed-pattern assertions.
- Direct MongoDB full-document replacement remains a dangerous primitive even after the recent product/search hardening. Tier-1 storefront product authority now routes through the shared SQL writer, but many specialized projections still depend on local freshness filters and SQL recomposition instead of a repo-wide ban on `ReplaceOneAsync`.
- Approved dangerous-primitive exceptions must stay narrow. Current reviewed exceptions include raw SQL idempotency/locking in `ProductVariantsController`, `SalesReturnsController`, `CommerceHubFinanceReconciliationJob`, `ProjectionEventFence`, and `TestAuthController`; raw ES writes in `ServiceDiscoveryProjectionConsumer`, affiliate public visibility projection, reviewed search-index services, and the affiliate reindex script; and a development-only CommerceHub seed path that may full-replace `CommerceHubProductDocument` only inside `PointOfSale.API.Development` while still routing search writes through `ICommerceHubSearchIndexService`.

## Scalability Assumptions

- PostgreSQL is the primary write bottleneck and authority anchor.
- Read scale is achieved through MongoDB, Redis, and Elasticsearch rather than direct SQL fan-out.
- Eventual consistency is acceptable for non-authoritative customer read surfaces.
- Critical storefront order reads are allowed to self-repair projections from SQL before serving.
- RabbitMQ decouples projection/search/update workloads from synchronous request handling.
- Multi-instance API scaling assumes shared Redis and shared external dependencies.
- The system prefers integrity over availability when backlog scope or external money truth is uncertain.

## Financial Authority Boundaries

- The immutable double-entry ledger in PostgreSQL is the sole authoritative source for financial reporting.
- Payment state transitions are sealed behind lifecycle services and SaveChanges interceptors; direct writes are architectural violations.
- Accounting journals are emitted through an accounting outbox and processed asynchronously, but financial truth remains SQL/ledger-based.
- CommerceHub vendor earnings, refunds, payouts, and reconciliation state are SQL-authoritative.
- MongoDB, Redis, and Elasticsearch must never be treated as financial authority.
- External gateways and banks remain external execution authorities; unresolved outcomes are modeled explicitly and reconciled before further outward money movement.
- Externally confirmed but locally incompatible captured-money or refund outcomes are contained through suspense accounting, unresolved-webhook logs, and hosted recovery workers rather than by silent overwrite.

## Inventory Authority Boundaries

- PostgreSQL warehouse stock, variant stock, reservations, stock movements, transfers, and fulfillment status are authoritative.
- Storefront, CommerceHub, Redis caches, and Elasticsearch hold replicated availability only.
- Customer-facing channels can request checkout/reservation actions through the API, but stock mutation authority remains in backend services against PostgreSQL.
- Reservation cleanup and stock-sync outbox processing are protection/reconciliation mechanisms around SQL authority, not replacement authorities.
- Variant inventory lineage is SQL-authoritative through current same-tenant `ProductVariants`, parent `Products`, active `Warehouses`, `ProductVariantStocks`, and active unexpired `InventoryReservations`. Superseded, detached, archived, inactive, deleted, expired, released, orphaned, or invalid-warehouse rows are not consumable inventory authority.

## 2026-05-21 Affiliate/Public Pricing Convergence

- Storefront-visible pricing is now the authority boundary for affiliate/public product visibility as well as storefront reads.
- `ProductDocument.DefaultUnit` plus current SQL `ProductUnits` are authority-critical for simple-product online purchasability; a simple product without a current default unit must fail closed to non-purchasable visibility.
- Affiliate/public read models must not treat legacy product-level `Price`, `OnlinePrice`, or `EffectivePrice` shortcuts as authority when storefront pricing is unit-aware.
- Public affiliate search cards, affiliate discovery/intelligence, and public product-detail surfaces must hide products when current storefront-authority pricing is missing or when the product is out of stock.
- Replay, rebuild, and repair paths for affiliate/public pricing must recompose from current SQL-backed storefront authority instead of restoring stale event payload or stale Mongo product-level pricing.

## 2026-05-21 Final Architecture Maturity Review

- The inspected Tier-1 checkout/payment/fulfillment and product/search/publication paths did not reveal a new catastrophic stale-authority corruption gap after the CHECKOUT-CON, PRODUCT-CON, and semantic-convergence sweeps.
- The dominant remaining debt class is governance and operational drift, not core SQL-authority corruption.
- Current production maturity still depends on keeping reviewed raw-SQL idempotency/locking primitives, shared projection writers, replay fences, and readiness/reconciliation workers aligned with their architecture tests and docs.
- Future work should not over-engineer normal CQRS lag, cache lag, or recommendation/search freshness into synchronous cross-system consistency. The hardened rule is SQL-first convergence plus fail-closed repair, not perfect global immediacy.

## 2026-05-21 Governance Extraction Sweep

- Storefront product Tier-1 mutation now has an explicit runtime mutation boundary: `IStorefrontProductProjectionMutationBoundary` / `StorefrontProductProjectionMutationBoundary`.
- `ProductSqlAuthorityProjectionWriter` still owns SQL recomposition and tombstone/upsert disposition, but it no longer calls `IProductReadRepository.UpsertAsync` or `TombstoneIfFreshAsync` directly. Those dangerous projection primitives are contained in the reviewed mutation boundary.
- The mutation boundary requires tenant/product/event identifiers plus a non-empty review reason, distinguishes `ReviewedFullDocumentUpsert` from `ReviewedVisibilityTombstone`, and rejects full-document upserts unless the recomposed projection is currently active and online-visible.
- Storefront-visible pricing and affiliate/public pricing helpers are now extracted into `ProductStorefrontVisibilityAuthority`. `ProductDocument.StorefrontEffectivePrice`, `StorefrontVisiblePrice`, `TryResolveAffiliatePublicPricing`, and `TryResolveAffiliateDiscoveryBasePrice` delegate to that helper so future runtime code naturally consumes the named storefront authority boundary.
- Future storefront product create/update/lifecycle/rebuild paths must route SQL-recomposed `ProductDocument` writes through `IProductSqlAuthorityProjectionWriter`, and direct repository writes must stay behind `StorefrontProductProjectionMutationBoundary`.

## 2026-05-22 PRODUCT-CON-11A Variant Lifecycle Resurrection Hardening

- Public variant lifecycle is SQL-authoritative through current `ProductVariants` plus the parent `Products` row. A variant is not public, purchasable, searchable, exportable, stock-linked, or price-inheriting unless SQL proves same-tenant parent lineage, non-deleted rows, active variant state, active/online/published parent product state, and a non-empty parent `ProductId`.
- `ProductVariantLifecycleAuthority` is the shared runtime helper for this public-lineage rule. Storefront cart stock authority, variant projection stock/publication handling, CommerceHub cart/checkout/listing reconciliation, affiliate intelligence stock recomposition, and product search overlays must use current SQL lifecycle recomposition instead of trusting stale Mongo, Elasticsearch, Redis, listing, event, or retry payload state.
- Archived, detached, orphaned, unpublished, inactive, deleted, or invalid-lineage variants must fail closed. Replayed stock, price, listing, media, import/clone, bulk, search, cache, CommerceHub, affiliate, or rebuild work may trigger recomposition, but cannot restore purchasability or public visibility unless current SQL lineage still proves it.
- Storefront cart availability now treats missing SQL lifecycle authority as `0` stock even if Mongo product documents still contain projected variant availability. CommerceHub cart and checkout validation now require current variant-plus-parent publication authority before accepting line items.
- Search/storefront product recomposition deletes or tombstones derivative visibility when a variant product no longer has an active priced variant under valid parent lineage. CommerceHub variant update replay now reconciles listing publication/search visibility from current SQL variant lifecycle instead of no-oping.
- Remaining debt is intentional and scoped: this is not a universal feed/export/recommendation/import framework. Future bulk import, clone, merge/split, feed writer, personalized recommendation, standalone suggestion, direct repair, or external sync code that creates or mutates variant-shaped visibility must adopt `ProductVariantLifecycleAuthority` or an equivalent SQL recomposition boundary with focused runtime tests and documentation.

## 2026-05-22 PRODUCT-CON-11B Variant Clone / Import Replay Divergence Hardening

- Clone/import-style product and variant mutation must converge through current SQL semantic ownership. `ProductVariantCloneImportAuthority` is the shared helper for SKU regeneration, barcode ownership rejection, same-product media inheritance validation, and simple-product identity mirroring.
- Product create/update and variant create/update now treat SKU/barcode/media/initial-stock payloads as intent, not authority. Duplicate SKU carryover deterministically regenerates a safe SKU where creation semantics allow it; barcode carryover is rejected when another live product or variant owns it; variant media inheritance must point at current product-owned image media; duplicate initial stock rows for the same warehouse are rejected before stock fanout.
- Simple-product default variants and product-level SKU/barcode now converge onto the same SQL-owned identity after clone/import-like create/update, preventing a product row from preserving stale cloned SKU while the default variant silently owns a different regenerated SKU.
- Dedicated CSV/Excel/ERP/supplier import services were not present in the inspected backend. Future explicit import, clone, template-duplication, marketplace-sync, or bulk catalog ingestion entrypoints must use `ProductVariantCloneImportAuthority` plus `ProductVariantLifecycleAuthority`, preserve request idempotency/replay collapse, and emit existing SQL-authoritative product/variant/publication/stock/media events rather than writing derivative visibility directly.

## 2026-05-22 PRODUCT-CON-11C Variant Merge / Split Semantic Divergence Hardening

- Variant merge/split semantic ownership is SQL-authoritative on `ProductVariants` through `SemanticLifecycleState`, replacement lineage, operation id/kind, lineage root, and supersession timestamp. Only `Current` semantic owners may own public lifecycle, SKU/barcode uniqueness, inventory reservation authority, stock mutation authority, CommerceHub visibility, search visibility, or feed/export identity.
- `ProductVariantMergeSplitAuthority` is the shared runtime helper for merge/split replay collapse, stale-operation rejection, source identity clearing, split-parent retirement, lineage root stamping, and open-reservation rejection before a variant is superseded.
- Existing public lifecycle, product search recomposition, clone/import SKU/barcode ownership checks, product/variant admin mutation, simple/default variant alignment, POS stock mutation support, storefront/checkout stock reservation, CommerceHub marketplace reservation authority, payment-recovery reservation validation, and CommerceHub vendor visibility now fail closed when a variant is semantically merged, split-parent retired, or detached.
- Replayed merge/split jobs may only converge to the current SQL semantic owner. Superseded variants cannot resurrect purchasability, stock, pricing, media, publication, external mapping visibility, or barcode/SKU ownership through stale events, rebuilds, cache reads, CommerceHub listings, or reservation retries.
- Remaining debt is intentional and scoped: no public admin merge/split orchestration endpoint was present in the inspected backend. Future explicit merge/split/bulk-dedup/supplier-normalization workflows must call `ProductVariantMergeSplitAuthority`, publish existing SQL-authoritative product/variant/stock/listing events after commit, invalidate product/CommerceHub/search/cache visibility, and add end-to-end replay tests for inventory transfer semantics if they move physical stock between semantic owners.

## 2026-05-22 PRODUCT-CON-11D Variant Inventory / Reservation Lineage Convergence Hardening

- Inventory reservation and stock mutation authority now converges through `ProductVariantInventoryLineageAuthority`. The helper requires current same-tenant variant semantic ownership, active parent product lineage, active warehouse ownership, non-deleted stock rows, and active unexpired reservation rows before stock can be reserved, consumed, committed, or propagated.
- Storefront/POS stock mutation support and checkout reservation repositories no longer treat a valid variant id plus stock row as sufficient authority. Superseded variants, inactive warehouses, detached product lineage, expired reservations, released reservations, and orphan product/warehouse links fail closed or collapse to deterministic no-op where replay is harmless.
- CommerceHub payment and delivery reservation checks filter active reservations through current SQL inventory lineage before provider or fulfillment side effects. If raw reservation rows exist but current stock/warehouse/variant lineage no longer backs them, the path fails as stock authority lost instead of silently accepting stale reservation visibility.
- Stock-sync outbox replay now suppresses stale or detached stock fanout when current SQL variant/warehouse/stock authority no longer proves ownership, or when a newer SQL variant mutation already supersedes the outbox event. Suppressed stale messages are completed as collapsed replay rather than being published to Mongo/search/cache availability consumers.
- Rebuild and recovery semantics remain SQL-first: cleanup/release paths may still repair stale reservations, but consumption, fulfillment, payment recovery, stock visibility, and cross-system fanout may only use current consumable reservation lineage. MongoDB, Redis, Elasticsearch, CommerceHub feeds, affiliate visibility, and analytics remain derivative.
- Remaining debt is intentional and scoped: this is not a universal physical inventory-transfer or external feed framework. Future explicit merge/split stock migration, warehouse reassignment jobs, bulk inventory imports, feed/export stock writers, and specialized reconciliation workers must reuse the inventory lineage authority or document an equivalent SQL transaction boundary with replay/rebuild tests.

## 2026-05-22 PRODUCT-CON-11E External Derivative Replay Convergence Hardening

- External-feed and export-like product visibility is a derivative-system boundary. Current SQL product publication, variant public lifecycle, semantic ownership, inventory lineage, listing/category/store authority, and current derivative mutation fences decide what may leave the platform; MongoDB, Redis, Elasticsearch, autocomplete snapshots, recommendation shelves, warmed cache cards, delayed outbox payloads, and old rebuild seeds are not export authority.
- `ProductVariantLifecycleAuthority` now exposes both tenant-scoped and self-consistent global public-lineage queries plus loaded-aggregate checks. CommerceHub search/reindex overlay and marketplace homepage curated listing rebuilds use those predicates so a superseded, detached, unpublished, or orphaned listing replay collapses to delete/scrub visibility instead of reappearing in search or recommendation-like shelves.
- CommerceHub external discovery recomposition and marketplace product rebuilds filter availability through `ProductVariantInventoryLineageAuthority` before stock reaches CommerceHub search or marketplace read models. Stale stock from an inactive warehouse, superseded variant, or detached stock owner fails closed as no derivative availability.
- Storefront homepage cached product cards are cache evidence only. Every featured/new-arrival/best-seller/curated card is recomposed from SQL before return, variant products with no current public semantic-owner variant are suppressed, and SQL stock quantities for variants ignore stock rows that no longer satisfy inventory lineage.
- Projection rebuild/recovery semantics are convergence semantics: a retry, reindex, autocomplete response, curated homepage rebuild, stale queue replay, partial fanout recovery, or cache hydration can ask for current visibility, but it cannot restore stale public visibility after SQL authority rejects it. Expected CommerceHub reindex counts now use the same public variant lifecycle filter as the overlay used to build the documents.
- No dedicated backend product feed/export writer for affiliate feeds, supplier feeds, marketplace exports, inventory feeds, pricing feeds, or external sync payloads was present in the inspected runtime paths. The hardening lands on the concrete export-like surfaces that exist now. Future feed writers, cache warmers that hydrate product visibility, external sync workers, analytics/reporting public projections, recommendation engines, and direct repair tools must call the lifecycle/inventory authorities or an equivalent reviewed SQL recomposition boundary and add replay/rebuild tests before rollout.

## 2026-05-22 ORDER-CON-01 Order / Fulfillment Semantic Resurrection Hardening

- Shipment-triggered order mutation and storefront shipment visibility now use `OrderFulfillmentLineageAuthority`. A delayed `ShipmentStatusChangedEvent`, queue retry, provider callback fanout, projection replay, or rebuild seed is evidence only until current SQL proves a non-deleted same-tenant `Shipment` still belongs to the non-deleted owning `Order`.
- `OrderStatusSyncService` still owns order status synchronization behind its order lock, but it now maps the current SQL shipment status instead of the replayed event status. Detached, soft-deleted, wrong-order, and stale-status shipment evidence collapses to no-op or current SQL truth before order status fanout.
- `OrderProjectionConsumer` now recomposes the shipment facet from live SQL shipment lineage before storing storefront shipping status, tracking, or carrier data. Storefront order projection rebuild snapshots also load live shipment visibility, so rebuild/reconciliation no longer reconstructs shipment visibility solely from stale order stamps or event payload tracking fields.
- Shipment/payment/fulfillment lineage remains split by existing authority owners: payment lifecycle owns payment settlement/refund recovery, reservation lineage stays under `ProductVariantInventoryLineageAuthority`, fulfillment status stays under `IOrderFulfillmentService`, and carrier registration/reconciliation keeps its existing shipment service controls.
- Rebuild/recovery rule: replay may ask for current order or shipment visibility, but no stale shipment can make a cancelled, detached, completed, returned, or replaced fulfillment lane visible again without live SQL order/shipment lineage. Missing lineage fails closed and may only trigger SQL order rehydrate.
- Intentional remaining debt: this sweep hardens the storefront shipment facet and shipment-triggered order status boundary, not every future warehouse export, affiliate fulfillment export, analytics projection, support cache, carrier adapter, or external shipment feed. New derivative fulfillment surfaces must use the shared lineage authority or document an equivalent SQL recomposition boundary and add replay/rebuild tests.

## 2026-05-22 PAYMENT-CON-01 Payment / Callback / Settlement Resurrection Hardening

- Payment callback evidence remains subordinate to locked SQL payment truth. `PaymentLifecycleService` already rejects stale provider success after terminal collected/failed/expired/refunded truth and collapses stale provider failures; refund and settlement visibility fanout now comes from that lifecycle owner instead of being reconstructed by refund, settlement, projection, or cache paths.
- Accepted SQL refund mutations and COD/gateway settlements now publish canonical `OrderPaymentStatusChangedEvent` payment visibility alongside their existing refund/settlement events inside the lifecycle transaction. Refund-id and settlement-id replay collapse before any second payment visibility transition can be staged.
- Storefront payment read-model replay now uses `OrderPaymentLineageAuthority` before mutating the payment facet. A delayed payment status event can trigger projection repair, but it cannot restore stale completion, stale refund visibility, or stale settlement visibility when current SQL order payment status disagrees or order lineage is gone.
- Settlement and refund correctness remain SQL/accounting-outbox concerns. MongoDB, Redis, Elasticsearch, affiliate/support/export dashboards, callback logs, provider reconciliation exports, notifications, and old outbox payloads remain derivative evidence or visibility only.
- Rebuild/recovery rule: payment rebuilds and queue recovery may converge current SQL status forward, no-op, rehydrate, or enter explicit suspense/reconciliation review; they must not resurrect payment completion after cancellation/refund/expiration or invent a new refund/settlement owner from stale callback payloads.
- Intentional remaining debt: this pass closes the inspected lifecycle fanout and storefront payment-facet replay gap. CommerceHub payment projections, affiliate/accounting/reporting exports, support/admin caches, provider export writers, new callback adapters, and any future payment cache warmer still need explicit review if they start deriving payment visibility from payloads instead of current SQL lifecycle truth.

## 2026-05-22 ACCOUNTING-CON-01 Accounting / Ledger Replay Convergence Hardening

- Accounting outbox delivery is now explicitly governed by `AccountingLedgerAuthority` for inspected order collection, refund, and settlement journal paths. Outbox payloads remain triggers; current SQL order payment/fulfillment truth, `OrderRefund` rows, and `PaymentSettlementBatch` rows decide whether a journal may be posted and what amount/context is posted.
- Stale order collection replay collapses to a processed no-op when current SQL payment truth is terminal non-recognition (`Failed`, `Expired`, `Rejected`, `AmountMismatch`, pending states) or when COD delivery is not currently proven by fulfillment/order state. Missing SQL authority remains retryable/reconciliation territory rather than silent posting.
- Refund accounting now recomposes from current SQL `OrderRefund` authority before journal generation. A replayed partial/full refund payload cannot override current refund amount, settlement context, refund id lineage, or order payment truth.
- Gateway and COD settlement accounting now reload current `PaymentSettlementBatch` authority before ledger posting. Replayed settlement payload amounts, fees, provider labels, or stale batch ids cannot become journal authority; missing or malformed settlement lineage remains retryable.
- Rebuild/recovery semantics: accounting recovery jobs and queue replay can re-enter the outbox processor, but replay converges by posting current SQL truth, returning the existing journal through idempotency, collapsing stale non-authoritative work to no-op, or remaining retryable for operator reconciliation. MongoDB, Redis, Elasticsearch, dashboards, exports, and reporting snapshots are derivative and must not rehydrate ledger truth from stale payloads.
- Intentional remaining debt: this hardens the core accounting outbox paths found in the runtime. CommerceHub vendor finance read models, affiliate accounting exports, BI/reporting snapshots, tax/export feeds, payout export files, and support/admin caches still need explicit SQL accounting authority checks if they begin generating financial visibility from payloads or snapshots instead of SQL ledger/reconciliation state.

## 2026-05-25 POS-ADMIN-CERTIFICATION-01 PASS 4 Financial Settlement Lineage Certification

- The audited order/payment/refund/accounting slices did not reveal a second foundational lifecycle rewrite requirement after ORDER-CON-01, PAYMENT-CON-01, and ACCOUNTING-CON-01. Payment lifecycle authority, storefront payment visibility recomposition, refund replay collapse, and accounting idempotency remained aligned with the prior SQL-first model.
- One material replay gap remained in settlement accounting semantics: `PaymentSettlementBatch.OrderCount` was still a denormalized snapshot that both accounting replay and the immediate settlement-accounting fast path could trust without revalidating the current `PaymentSettlementOrders` lineage.
- `AccountingLedgerAuthority` now loads both recorded settlement-batch order count and current live `PaymentSettlementOrders` count, rejects soft-deleted settlement batches as missing current authority, and settlement posting now retries when current lineage is empty or when header count drifts from current mappings.
- `PaymentLifecycleService` immediate gateway/COD settlement accounting now enforces the same invariant through `EnsureSettlementBatchRetainsCurrentOrderLineageAsync(...)`; settlement accounting may not post from the batch header alone.
- Post-settlement refund authority now follows the same current-lineage rule. Refund creation and immediate/replayed refund accounting must prove that the current non-deleted settlement batch still contains the refunding order in live `PaymentSettlementOrders` lineage and that the batch header count still matches current mappings before bank-account refund posting is allowed.
- Operationally, settlement batch header metadata remains useful, but it is no longer sufficient posting authority. Current settlement membership is a non-deleted SQL settlement batch plus live SQL settlement-order mapping, and mismatch now surfaces as explicit convergence telemetry (`SettlementOrderLineageMismatch`) rather than silent financial drift.
- A storefront payment-projection residual was also closed on 2026-05-27. `OrderProjectionConsumer` now deletes the tenant-scoped Mongo order projection and invalidates its cache when payment replay no longer has current SQL order authority; replay that conflicts with current SQL payment status may repair current status and freshness, but cannot append callback-derived timeline narrative.
- `FinancialIntegrityRiskHardeningTests` now freezes the missing-authority deletion and conflicting-narrative suppression rule alongside settlement/refund accounting convergence.

## 2026-05-22 GOVERNANCE-CON-01 Dangerous Primitive Governance Hardening

- Approved derivative mutation primitives now have a shared runtime precondition helper: `DerivativeProjectionMutationGuard`. The guard requires explicit primitive type, boundary name, document key, event id/timestamp/version, authority kind, freshness guard, SQL authority for full visibility replacement/search writes, fail-closed behavior, and a review reason before a dangerous derivative mutation is executed.
- `StorefrontFooterProjectionConsumer` now uses `ProjectionEventFence` before SQL-to-Mongo footer side effects and calls `DerivativeProjectionMutationGuard` before the reviewed `StorefrontFooterDocument` full replacement. Duplicate or active replay collapses before MongoDB mutation, and stale footer replay remains blocked by the existing freshness filter.
- `StorefrontConfigProjectionConsumer` now applies the same dangerous-primitive runtime contract to its SQL-recomposed `StorefrontConfigDocument` full replacement and Redis store-config cache invalidation. Store config events are trigger-only; current SQL store config, tenant currency, freshness filters, and durable event fencing must be present before MongoDB or Redis side effects.
- `SqlAuthorityOverlayKind` now names storefront config/footer, checkout, and CommerceHub listing derivative authority scopes so future guarded mutation requests can state the authority boundary without inventing local labels.
- `CrossSystemAuthorityRegressionGuardTests` now asserts that reviewed storefront footer/config full-document replacement and config cache mutation retain durable event fencing, runtime dangerous-primitive guards, authority-kind declarations, and freshness filters. `DerivativeProjectionMutationGuardTests` validate fail-closed rejection for missing SQL authority, missing freshness guard, or non-fail-closed mutation intent.
- Intentional remaining debt: this is not a repo-wide mutation framework and does not rewrite every approved direct Mongo/Elasticsearch/Redis path. Existing specialized boundaries remain valid where they already use SQL recomposition, projection fences, freshness filters, tombstones, or domain-specific lifecycle services. Future allowlist additions for dangerous primitives must either route through a named authority boundary or add an equivalent runtime guard, source guard, tests, and documentation in the same change.

## 2026-05-22 TENANT-CON-01 Tenant Lineage / Storefront Config Boundary Hardening

- Tenant ownership for replay, rebuild, repair, and derivative mutation paths is now explicitly modeled as SQL lineage authority, not only ambient tenant filtering. `TenantLineageAuthority` is the shared helper for current active tenant proof and current tenant-owned entity proof on replay/rebuild paths.
- Storefront config projection now reloads current SQL `StoreConfig` joined to the current active owning `Tenant` before Mongo replacement or Redis cache invalidation. A stale event for a moved, missing, deleted, inactive, or cross-tenant store config fails closed by deleting the tenant-scoped storefront config projection and invalidating that tenant's versioned store-config cache.
- Admin Elasticsearch sync endpoints now resolve tenant scope from `ITenantContext.RequiredTenantId`, which is populated by trusted middleware, instead of reparsing `X-Tenant-Id` locally. Request headers remain compatibility output after trusted resolution, not admin repair authority.
- Rebuild/recovery tenant convergence rule: a tenant-scoped repair or projection may use event ids, route ids, cache keys, or document ids as candidate evidence only. Current SQL tenant lineage decides whether to upsert current visibility, delete/scrub stale visibility, invalidate tenant-scoped cache, or no-op.
- Remaining debt is intentional and scoped: this is Phase 1, not a universal tenant framework. Future CMS, homepage, affiliate, CommerceHub, notification, analytics, search-repair, cache-warmer, and direct repair surfaces that create tenant-scoped visibility must adopt `TenantLineageAuthority` or an equivalent reviewed SQL recomposition boundary with replay/rebuild tests.

## 2026-05-22 TENANT-CON-01 Phase 2 Derivative Projection / Cache Isolation

- Derivative tenant isolation now extends beyond storefront config into inspected homepage, CommerceHub rebuild, affiliate public projection, affiliate repair, and cache-hydration paths. Tenant-scoped derivative identifiers must include tenant scope through `TenantScopedDerivativeKey` where the backing store does not enforce relational ownership.
- Storefront homepage product and category cache hydration is fail-closed after Redis/Mongo reads: products and categories are revalidated against active SQL tenant lineage and current SQL category/product visibility before returning cached cards. A stale or cross-tenant homepage cache entry may trigger recomposition, but it cannot be served as final visibility.
- CommerceHub marketplace product and homepage rebuilds now delete/scrub tenant-scoped documents when SQL no longer proves an active tenant owner. Inactive or deleted tenant lineage collapses rebuild/replay to delete/no-op instead of preserving stale marketplace product or homepage visibility.
- Public affiliate product projection now validates current SQL product tenant ownership before Mongo or Elasticsearch mutation, uses tenant+product Mongo filters, indexes public affiliate search documents with tenant-scoped ids, and scrubs legacy product-id search documents during mutation. Store profile and tenant-currency replay reload current active tenant profile/currency from SQL; inactive tenant lineage deletes affiliate tenant visibility.
- Remaining debt is explicit: this pass does not retrofit every CMS, SEO, notification, analytics, support-cache, marketplace localization, review, WhatsApp, or payment/provider cache path. New tenant-scoped derivative writers, cache warmers, reindexers, and repair tools must adopt SQL tenant lineage proof, tenant-scoped derivative keys, fail-closed cache hydration, and replay/rebuild tests.

## 2026-05-24 TENANT-CON-01 Phase 3 Actor Authority / Admin Scope Governance

- Tenant-scoped admin and operational authority now has a SQL-backed actor recomposition boundary. `AdminTenantAuthority` reloads the current `Users` row and active `Tenant` row before privileged tenant-bound endpoints can proceed, and treats JWT role/tenant claims as evidence only.
- `AdminTenantAuthorityMiddleware` runs after ASP.NET authorization and collapses stale admin-session replay. A user whose role was downgraded, account was disabled/deleted, tenant claim no longer matches trusted tenant context, or tenant is inactive receives fail-closed `ADMIN_SCOPE_FORBIDDEN` instead of relying on the still-valid JWT role.
- Admin data-sync, order admin, shipment admin, inventory admin, store-config, and CMS/publication controllers now consume `ITenantContext.RequiredTenantId` rather than reparsing `X-Tenant-Id` as local authority. The header remains compatibility output from trusted tenant middleware, not repair or mutation authority.
- Tenant admin search operations remain tenant-scoped. Global Elasticsearch index deletion/recreation is blocked from tenant admin scope with `GLOBAL_SEARCH_CONTROL_FORBIDDEN`; tenant-scoped reindex remains available through SQL recomposition.
- AF16E affiliate repair/reindex now has an explicit `ReindexTenantAsync` entrypoint and the admin controller invokes only that tenant-scoped path. The all-tenant script method remains an internal/platform runbook primitive, not tenant-admin operational authority.
- Remaining debt is intentional and scoped: this pass does not redesign IAM, delegated admin, external SSO, support impersonation, or every non-role bare `[Authorize]` endpoint. Future support tooling, platform runbooks, global repair jobs, and admin impersonation flows must use current SQL actor authority, current tenant lineage, explicit platform scope, and replay/rebuild tests before they may mutate tenant-scoped operational state.

## 2026-05-24 OPERATIONS-CON-01 Replay / Drift / Rebuild Observability

- Semantic convergence decisions now have a centralized low-cardinality telemetry boundary: `OperationalConvergenceTelemetry` (`PointOfSale.Operational.Convergence`). It records replay collapse, fail-closed authority rejection, rebuild authority declarations, dangerous primitive governance, and drift diagnostics through bounded enum dimensions plus tenant buckets.
- `ProjectionEventFence` records duplicate completed replay, active overlapping replay, duplicate completion races, and successful fence completion. This closes the prior blind spot where replay collapse could be correct but operationally invisible.
- `SqlTruthRebuildGuard`, `DerivativeProjectionMutationGuard`, `TenantLineageAuthority`, `AdminTenantAuthority`, `ProductVariantLifecycleAuthority`, `ProductVariantInventoryLineageAuthority`, `OrderFulfillmentLineageAuthority`, `OrderPaymentLineageAuthority`, `AccountingLedgerAuthority`, stock-sync suppression, projection authority validation, Mongo projection drift health checks, and Elasticsearch drift reconciliation now emit convergence diagnostics at their shared authority boundaries.
- Metrics intentionally avoid raw tenant ids, document keys, payment ids, customer data, callback payloads, and free-form exception text. Tenant scope is bucketed; reasons, decisions, authority names, primitive names, and phases are enum/stable-label based.
- Remaining debt is explicit: this is not a SIEM, distributed tracing platform, or repo-wide event audit table. Existing specialized consumers that bypass the shared guards still need local review if they add new replay/rebuild/drift decisions. Future cache warmers, feed writers, report rebuilders, support tools, and global runbooks must either reuse this telemetry boundary or document an equivalent low-cardinality operational diagnostic contract.

## 2026-05-24 STRATEGIC-REVIEW-01 Navigation Replay / Tenant-Lineage Hardening

- Strategic replay/rebuild review found a concrete derivative projection drift gap in `NavigationProjectionConsumer`: the Mongo freshness filter admitted the same `EventId`, so duplicate replay could repeat full-document replacement and Redis invalidation even when the data eventually converged.
- Navigation publication is now governed like the adjacent storefront config/footer boundaries. `NavigationPublishedEvent` is trigger evidence only; current SQL `StoreConfig` plus active tenant lineage decides whether navigation may be projected.
- `ProjectionEventFence` now collapses duplicate completed and active navigation replay before MongoDB or Redis side effects. Missing, inactive, moved, or cross-tenant store-config lineage fails closed by deleting the tenant-scoped navigation projection and invalidating known tenant/slug navigation cache keys.
- `DerivativeProjectionMutationGuard` now guards the reviewed `StorefrontNavigationDocument` full replacement and Redis navigation cache invalidation with a named `StorefrontNavigation` authority scope, SQL authority declaration, freshness guard, fail-closed behavior, and review reason.
- `CrossSystemAuthorityRegressionGuardTests` now treats `StorefrontNavigationDocument` replacement as a high-risk derivative primitive and asserts that navigation retains event fencing, tenant lineage, runtime dangerous-primitive governance, freshness rejection for duplicate `EventId`, and fail-closed deletion behavior.
- Remaining debt is intentional: this pass does not retrofit every CMS, SEO, smart-offer, online-customer, analytics, notification, or support-cache projection. Future tenant-scoped derivative projections that full-replace documents or invalidate Redis must adopt the same SQL lineage, replay collapse, runtime guard, telemetry, and source-test pattern or document a reviewed equivalent.

## 2026-05-24 CHAOS-CON-01 Replay / Rebuild / Partial Failure Chaos Validation

- A focused semantic convergence chaos suite now validates replay storms, duplicate completed projection delivery, duplicate rebuild triggers, interrupted rebuild authority, stale cache hydration, cross-tenant replay, stale admin JWT/session replay, stale payment/shipment replay, accounting/refund/settlement replay, and tenant-scoped operational repair controls.
- The validated guarantee is deterministic convergence: duplicate and stale evidence collapses, rebuilds redeclare SQL authority, dangerous derivative primitives reject missing SQL/freshness/fail-closed preconditions, tenant and actor scope fails closed, and payment/fulfillment/accounting visibility recomposes from current SQL truth.
- No new duplicate settlement/accounting posting, duplicate fulfillment progression, stale payment resurrection, stale shipment resurrection, or cross-tenant derivative visibility leak was reproduced on the inspected shared boundaries.
- Honest weakness found: the AF16E affiliate repair/reindex path is tenant-scoped and uses active SQL tenant filters plus `TenantScopedDerivativeKey`, but it does not directly call `TenantLineageAuthority`. Treat this as governance drift and keep future docs/tests precise about whether a path uses the shared helper or an equivalent local SQL-lineage check.
- Operational pressure finding: current resilience confidence is strong for shared authority helpers and inspected Tier 1 operational surfaces, but not universal for every future CMS, SEO, notification, analytics, support-cache, feed/export, report rebuild, or platform runbook path.

## 2026-05-24 STABILIZATION-CON-01 Operational Maturity / Throughput Controls

- Operational scalability review found that marketplace event/projection outbox workers and stock sync outbox workers had partially hard-coded cadence, lease, and batch-draining behavior even though runtime options already existed for some of those concerns.
- Marketplace event outbox, marketplace projection outbox, and stock sync outbox processing are now tunable through bounded batch size, max-batches-per-poll, inter-batch delay, poll interval, and claim lease options. This improves backlog recovery ergonomics without changing SQL authority, aggregate ordering, retry, or compensation semantics.
- Marketplace projection outbox tests now validate bounded batch processing and duplicate-completion cleanup. Source governance tests assert that high-volume workers and processors continue to use throughput options instead of reintroducing fixed timing constants.
- Operational limits and production assumptions are documented in `docs/architecture/operational-maturity-scalability-findings.md`. This pass does not claim synthetic throughput SLOs or adaptive throttling; production tuning still depends on observed backlog age, dependency latency, repeated failure counts, and tenant hotspot behavior.

## 2026-05-24 PERFORMANCE-CON-01 Replay / Recovery Throughput Optimization

- Payment-cancellation outbox recovery no longer depends on hard-coded worker cadence, batch size, retry threshold, or retry backoff cap. `PaymentCancellationOutboxOptions` exposes bounded batch size, max batches per poll, inter-batch delay, poll/idle/error cadence, retry threshold, and retry backoff cap.
- API startup now binds payment-cancellation and stock-sync recovery controls from configuration so operators can tune backlog drain rate without code changes.
- Focused tests validate payment-cancellation batch bounding, configured retry/backoff behavior, and source governance markers for option-driven workers.
- Intentional remaining debt: no adaptive throttling, tenant-specific scheduler, synthetic recovery SLO, or payment-cancellation processing lease was introduced. Payment cancellation recovery still converges through `IPaymentLifecycleService` and lifecycle idempotency rather than direct state mutation.

## 2026-05-24 OPERATIONS-RUNBOOKS-01 Operational Recovery Playbooks

- Production operational recovery guidance now lives in `docs/operations/operational-recovery-runbooks.md`. It covers replay recovery, rebuild/reindex recovery, queue backlog recovery, cache/projection recovery, tenant isolation incidents, payment/accounting/fulfillment incidents, drift investigation, telemetry interpretation, escalation boundaries, and remaining debt.
- Dangerous operational primitive guidance now lives in `docs/architecture/dangerous-operational-primitives.md`. Direct SQL/bulk mutation, full Mongo replacement, raw Elasticsearch writes, Redis pattern/manual key mutation, raw bus publishing, global/all-tenant repair, tenant override/support impersonation, and manual financial repair are documented as reviewed primitives rather than authority models.
- The operator model is explicit: replay-safe no-op/collapse/delete/tombstone behavior is healthy when current SQL authority rejects stale evidence; fail-closed derivative deletion is expected; rebuild interruption and overlap are normal convergence conditions; tenant-admin recovery stays tenant-scoped; all-tenant/global repair is platform runbook scope.
- `OperationalRecoveryRunbookArchitectureTests` now guard that the runbook continues to document required recovery areas, SQL-first replay/rebuild semantics, dangerous shortcut prohibitions, escalation boundaries, and intentionally lightweight remaining debt.
- Remaining debt is honest and intentional: these playbooks do not introduce enterprise IAM, support impersonation ceremonies, adaptive throttling, fixed recovery SLOs, SIEM/audit-store behavior, or universal helper adoption across CMS, SEO, analytics, notification, support-cache, feed/export, reporting rebuild, cache warmer, and global runbook paths.

## 2026-05-24 TENANT-CERTIFICATION-01 Tenant Isolation Certification & Regression Freeze

- Tenant isolation certification validated the inspected trusted tenant-resolution, SQL tenant-lineage, SQL admin actor authority, tenant-scoped derivative identity, replay/rebuild convergence, dangerous primitive governance, and operational telemetry boundaries.
- `CmsPageProjectionConsumer` is now aligned with adjacent navigation/config/footer projections: publish/delete replay is fenced before MongoDB side effects, inactive or missing SQL tenant/page authority fails closed by tombstoning existing page visibility, and full `StorefrontPageDocument` replacement is runtime-reviewed through `DerivativeProjectionMutationGuard` with `SqlAuthorityOverlayKind.StorefrontCms`.
- `TenantIsolationCertificationFreezeTests` now freeze trusted tenant resolution, tenant-admin trusted-context usage, global search-control rejection, tenant-scoped affiliate/CommerceHub/CMS derivative controls, and the certification documentation requirements.
- Certification findings live in `docs/architecture/tenant-isolation-certification-findings.md`. Operator incident handling remains in `docs/operations/operational-recovery-runbooks.md`; the previously mentioned `docs/architecture/operational-recovery-runbooks.md` path is not canonical in this repo.
- Remaining risk is explicit, not hidden: CMS page projection is hardened, but other CMS-adjacent, SEO, smart-offer, online-customer, notification, analytics, support-cache, feed/export, reporting rebuild, cache warmer, and global runbook paths still need path-specific certification before universal tenant-isolation claims.

## 2026-05-24 SUPERADMIN-CON-01 Platform Authority Certification

- Super-admin/platform-admin authority is now a current-SQL boundary, not only a JWT role claim. `SuperAdminPlatformAuthority` reloads the current `SuperAdmins` row, requires the current `TokenVersion`, checks current super-admin governance status, and records authority rejection telemetry before any `RequireSuperAdmin` endpoint may proceed.
- Super-admin access tokens now carry `token_version`; logout, refresh-token replay revocation, password-change session revocation, account deletion, and governance restriction collapse stale access-token reuse fail-closed.
- `SuperAdminPlatformAuthorityMiddleware` guards every endpoint using the `RequireSuperAdmin` policy, including legacy platform-policy controllers outside `/api/super-admin/*`.
- Certification findings live in `docs/architecture/super-admin-platform-authority-certification-findings.md`; platform/global operator procedures now live in `docs/operations/operational-recovery-runbooks.md`; dangerous primitive guidance includes the SUPERADMIN-CON-01 platform authority freeze.
- Remaining platform-authority debt is explicit: no enterprise IAM/SSO/ABAC, support impersonation ceremony, SIEM-grade audit store, fixed global rebuild SLO, or universal audit-queue adoption for older platform-policy endpoints was introduced. Global/all-tenant repair remains intentionally flexible under explicit platform authority and runbook scope.

## 2026-05-24 SUPERADMIN-CON-01 Phase 2 Marketplace Governance Certification

- CommerceHub marketplace aggregation remains intentionally global, but marketplace product visibility is now certified as tenant-owned and SQL-listing-authoritative. Marketplace product read-model rebuilds must require a current published SQL `CommerceHubListing` plus current product, variant, category, tenant, stock, price, and media authority before public visibility is restored.
- Homepage, recommendation, and curated discovery references are governance references only. Super-admin homepage validation, public homepage reads, and marketplace homepage projection filter curated listing references through current SQL tenant/listing/product/variant/category visibility and fail closed by scrubbing stale references.
- CommerceHub public search writes now use tenant-scoped Elasticsearch document ids while cleanup deletes legacy listing-only ids. Search/reindex still recomposes from `CommerceHubSearchSqlAuthorityOverlay`; Mongo and Elasticsearch remain derivative visibility only.
- `HomepageProjectionConsumer` now fences duplicate active homepage replay before projection side effects, making stale/duplicate homepage rebuilds deterministic and diagnosable.
- Certification findings live in `docs/architecture/marketplace-governance-certification-findings.md`. Remaining marketplace debt is explicit: reviews/localization/CMS-adjacent surfaces, future ranking engines, feed/export writers, cache warmers, and global runbooks need path-specific certification when expanded.

## 2026-05-24 SUPERADMIN-CON-01 Phase 3 Replay / Rebuild / Recovery Safety

- Super-admin/global recovery was certified as replay-safe convergence rather than a new authority model.
- Tenant-admin CommerceHub search recovery now uses trusted tenant context and `ReindexTenantAsync`; global all-listing `ReindexAsync` remains platform/runbook scope.
- CommerceHub search rebuild/recovery now declares current SQL listing authority explicitly through `SqlAuthorityOverlayKind.CommerceHubListing`.
- CommerceHub reindex pacing is configurable through bounded batch and inter-batch delay options.
- Marketplace outbox dead-letter retry remains a reset into the normal ordered processor path, not direct replay forcing.
- Operational overlap semantics are documented: interrupted or overlapping rebuilds must redeclare SQL authority and converge through SQL-backed apply/delete/no-op/fail-closed behavior.
- Remaining recovery-risk debt is scoped: no adaptive scheduler, fixed rebuild SLO, universal dead-letter UI, SIEM-grade audit store, or universal feed/export/cache-warmer/global-runbook certification was added.

## 2026-05-24 SUPERADMIN-CON-01 Phase 4 Auditability / Emergency Governance

- Emergency governance certification validated operational auditability, emergency override boundaries, support/impersonation absence, rollback semantics, dangerous-action observability, and governance-document consistency across super-admin and legacy platform-policy surfaces.
- `SuperAdminOperationalAuditMiddleware` now records request-level audit for every non-read `RequireSuperAdmin` operation after `SuperAdminPlatformAuthorityMiddleware` recomposes current SQL platform actor authority. This covers legacy platform-policy mutations outside `/api/super-admin/*` as well as super-admin route mutations.
- No production support impersonation endpoint was found. Tenant override/support impersonation remains a dangerous primitive requiring future SQL actor authority, target tenant lineage, reason/reference capture, revocation semantics, durable audit, and replay-safe convergence before mutation.
- Rollback/override governance is SQL-first: operators may invalidate, delete/tombstone, rebuild, reindex, replay, no-op, retry, block, or route to reconciliation, but must not restore business truth from Mongo, Redis, Elasticsearch, old queue payloads, dashboards, exports, or support-tool state.
- Certification findings live in `docs/architecture/emergency-governance-certification-findings.md`; dangerous primitive and operational recovery runbooks now include Phase 4 emergency governance freeze guidance.
- Remaining auditability debt is explicit: no enterprise IAM/SSO/ABAC, support break-glass workflow, SIEM-grade audit lake, request-body audit capture, or universal domain-specific audit events for every older platform endpoint was introduced.

## 2026-05-24 SUPERADMIN-CON-01 Phase 5 Operational Chaos Certification

- Final super-admin operational chaos certification validated replay storms, dead-letter retry overlap, rebuild overlap, stale emergency-session reuse, dangerous primitive misuse, marketplace visibility chaos, moderation/review replay, and emergency recovery pressure as convergence scenarios rather than new authority models.
- Targeted hardening added: `MarketplaceProductReviewProjectionService` now applies `CommerceHubSearchSqlAuthorityOverlay` before review-triggered CommerceHub search mutation and deletes tenant-scoped search visibility when current SQL listing authority is missing.
- Runtime chaos tests now cover stale/revoked/governance-restricted super-admin sessions, duplicate replay collapse after fence completion, deterministic rebuild overlap declarations, and rejection of raw search bypass plus unsupported derivative rollback.
- `SuperAdminOperationalChaosCertificationTests` freeze source and documentation invariants for review/moderation replay, tenant-scoped search identity, SQL-first CommerceHub reindex, normal dead-letter retry, request audit ordering, and dangerous primitive rejection.
- Certification findings live in `docs/architecture/super-admin-operational-chaos-certification-findings.md`; platform, marketplace, emergency, dangerous primitive, and recovery runbook docs link the Phase 5 freeze.
- Remaining chaos-risk debt is explicit: no enterprise IAM/SSO/ABAC, SIEM-grade immutable audit, adaptive global scheduler, fixed rebuild SLO, universal chaos framework, or universal certification for future feed/export/ranking/localization/cache-warmer/support/global-script paths was introduced.

## 2026-05-24 POS-ADMIN-CERTIFICATION-01 PASS 1 Admin Session Authority

- POS tenant-admin identity is now SQL-session-authoritative on the default bearer boundary. Access tokens carry a `pos_session` binding and `PosSessionAuthority` validates current SQL user, role, tenant ownership, current hashed session, tenant lineage, and vendor governance status before accepting the token.
- POS refresh credentials are stored hash-only in `Users.RefreshToken`. Login, tenant-owner onboarding issuance, cached onboarding-session restoration, and refresh rotation replace the one current session binding; logout clears it; stale access tokens and stale refresh credentials fail closed.
- EF concurrency protection on the current POS refresh-session binding rejects concurrent stale rotation writes instead of silently creating multiple valid current sessions.
- `OperationalConvergenceTelemetry` now classifies POS session authority rejection without exposing raw tokens or raw tenant identifiers in metric labels.
- Remaining identity debt is scoped: one current POS session per user is intentional; no device/session inventory, refresh-family compromise workflow, enterprise IAM/SSO/MFA/ABAC, POS password-reset ceremony, or support impersonation workflow was added.

## 2026-05-25 POS-ADMIN-CERTIFICATION-01 PASS 2 Tenant Admin Scope Integrity

- Tenant-admin AI, Commerce AI, abandoned-checkout, storefront footer, and abandoned-cart recovery administration now use trusted `ITenantContext.RequiredTenantId` instead of locally parsing the compatibility tenant header.
- Authenticated abandoned-cart recovery-result logging is tenant-bound: a supplied tenant that differs from trusted request context is rejected before operational persistence. External automation callbacks remain supported through tenant-specific API-key validation and idempotency.
- Tenant-admin CommerceHub search recovery remains tenant-scoped through `ReindexTenantAsync`, but alias finalization and migration catch-up are now explicitly blocked from tenant-admin scope with `GLOBAL_SEARCH_CONTROL_FORBIDDEN` and remain platform/runbook recovery primitives.
- Inspected tenant-admin accounting, storefront-auth-provider, wishlist-analytics, pixel-tracking, online-customer-session, online-customer, and payment-method surfaces now consume trusted tenant context consistently; the sensitive online-customer/payment-method SQL paths also bind tenant scope explicitly in query predicates.
- Inspected tenant-owned bank, expense-category, financial-account, and shipping-method admin surfaces now consume trusted tenant context and bind scope explicitly in SQL for configuration reads/mutations, bank statement/opening-balance operations, financial-account parent validation, and shipping calculation/update/delete paths.
- This pass preserves existing replay/rebuild and marketplace/public visibility authority controls; it narrows admin scope selection without claiming universal certification for storefront/checkout/webhook or future analytics/support/cache-warmer/report/export operational tooling.

## 2026-05-25 POS-ADMIN-CERTIFICATION-01 PASS 3 Product / Inventory / Reservation Integrity

- Inventory correctness remains SQL-authoritative through current variant semantic ownership, active parent product lineage, active warehouse ownership, current stock-row ownership, and active unexpired consumable reservation state. Reservation rows, stock rows, order state, cache/search/public stock, and replay/rebuild inputs are evidence only.
- Marketplace reservation authority is now explicitly multi-tenant safe on the reviewed boundary. `InventoryConsistencyService` validates current consumable reservations across all tenant-owned marketplace suborders instead of assuming one tenant scope for the whole marketplace order.
- Marketplace payment recovery now uses the same SQL-first inventory lineage model as hardened checkout and fulfillment boundaries. Payment retry is blocked when current product publication, current variant ownership, active warehouse lineage, current consumable reservations, or current stock ownership no longer proves reservation-backed inventory authority.
- CommerceHub reservation creation now filters candidate stock rows through current stock-owner lineage and acquires warehouse locks in deterministic `WarehouseId`/`Id` order before allocation. Raw available stock in an inactive warehouse is not reservable inventory authority.
- CommerceHub return receipt replay now revalidates current stock ownership before restoring resellable or damaged quantities, so inactive-warehouse or superseded-variant historical reservation evidence cannot mutate SQL stock.
- CommerceHub delivery commit now stays inventory-lineage-authoritative instead of publication-authoritative. Marketplace unpublish/offline changes can block new payment retry, but they do not prevent stock consumption for an already-authorized order item whose current SQL product/variant lineage still exists.
- PASS 3 addendum on 2026-05-27 closes a remaining legacy availability and derivative-total gap on the reviewed surfaces. Repository availability reads, stock-sync/event total recomposition, storefront variant stock projection totals, and CommerceHub variant stock projection totals now all converge through current stock-owner lineage instead of summing undeleted stock rows directly.
- PASS 3 addendum on 2026-05-27 also closes a remaining SQL fallback and search-overlay lineage gap on reviewed storefront/search surfaces. `ProductVariantReadRepository` SQL fallback now requires current public variant lineage plus current stock-owner lineage, and `ProductSearchSqlAuthorityOverlay` now recomposes variant stock totals only from current stock-owner lineage instead of summing raw variant stock rows directly.
- PASS 3 addendum on 2026-05-27 also closes a remaining storefront cart, affiliate intelligence, and stock-compensation replay gap on reviewed derivative SQL snapshot surfaces. `CartService`, `AffiliateProductIntelligenceProjectionConsumer`, and `ProjectionFailureCompensationService` now recompose stock only from current stock-owner lineage instead of preserving raw undeleted stock rows.
- Inactive warehouses, detached stock owners, and stale stock-owner lineage are therefore no longer allowed to overstate reviewed cart, checkout, stock fanout, storefront, or CommerceHub stock totals merely because raw SQL stock rows still exist.
- Inactive warehouses and stale stock-owner rows are therefore no longer allowed to preserve reviewed storefront cart quantity, affiliate intelligence stock visibility, or stock-compensation replay totals merely because raw SQL stock rows still exist.
- Superseded/default-stale variants, unpublished parent products, inactive warehouses, and stale stock-owner rows are therefore no longer allowed to survive reviewed SQL fallback checkout enrichment or reviewed search/storefront stock recomposition merely because raw SQL rows still exist.
- This pass tightened certification without rewriting stable stock architecture. Stock repair, cleanup, and operational recovery remain intentionally available, but reviewed payment/recovery/consumption paths now fail closed instead of trusting stale reservation or warehouse evidence.
- Findings, guarantees, remaining debt, and regression-freeze invariants are recorded in `docs/architecture/product-inventory-reservation-certification-findings.md`.

## 2026-05-25 POS-ADMIN-CERTIFICATION-01 PASS 5 Marketplace / Public Visibility

- CommerceHub category lineage is now a shared SQL authority boundary for marketplace public visibility. `CommerceHubCategoryLineageAuthority` validates the active CommerceHub category parent chain and rejects inactive, missing, self-referential, or cyclic lineage.
- CommerceHub search overlay, CommerceHub listing projection, marketplace product read-model rebuilds, homepage/recommendation projection, public homepage reads, super-admin homepage reference validation, CommerceHub query category/suggestion filtering, and reindex count verification now use the same CommerceHub category lineage semantics instead of leaf-category `IsActive` checks alone.
- CommerceHub product-detail reads now also recompose the current SQL listing/category/media/review snapshot before serving Mongo-backed detail data, so stale listing visibility, stale CommerceHub category identity, and legacy variant image strings cannot survive detail reads after replay/rebuild drift.
- Current vendor suppression is now part of the same CommerceHub public-visibility contract. Governance-hidden vendors and vendors suspended from new orders are suppressed consistently across CommerceHub search, marketplace product list/detail reads, homepage/recommendation reads, and super-admin curated-reference validation instead of only on search/suggestion paths.
- CommerceHub browse/search/suggestion/autocomplete response shaping is part of that same vendor-suppression contract on the inspected query boundary. Request-time excluded-vendor sets, cached suggestion/autocomplete product revalidation, and public counts/facet inputs must suppress both governance-hidden vendors and vendors suspended from new orders before the response is returned; item-only post-filtering is not a sufficient public-count or suggestion-authority model.
- The certified convergence behavior is fail-closed: stale category lineage deletes or suppresses search visibility, deletes marketplace read-model visibility, scrubs curated homepage/recommendation references, rejects invalid super-admin curation, or excludes listings from reindex verification counts.
- The same fail-closed behavior now applies to hidden or order-suspended vendors: marketplace product pages, homepage product sections, and curated references scrub or reject those listings even if stale Mongo, Redis, Elasticsearch, or homepage JSON still carries them.
- CommerceHub public card images now recompose only from current SQL `ProductMedia`/`ProductVariants.MediaId`; legacy product/variant image strings and derivative images cannot restore media after SQL media ownership is removed.
- CommerceHub search overlay now recomposes `AverageRating` and `ReviewCount` from current approved, non-deleted SQL `ProductReviews`; stale review summaries from Mongo or search payloads cannot survive review/rebuild replay.
- Legacy service-marketplace search repair is now split by operator scope. Tenant-admin `MarketplaceController` repair may reindex/delete only current tenant-owned service search documents and cannot delete/recreate the global `marketplace_services` index; global service-marketplace index recreation remains super-admin/platform scope.
- Existing listing-authoritative marketplace, tenant-scoped search identity, replay fencing, and tenant-safe aggregation guarantees remain in force. This pass does not introduce a personalized ranking engine, universal feed/export/cache-warmer certification, enterprise moderation workflow, adaptive rebuild SLO, SIEM-grade audit, or support impersonation ceremony.

## 2026-05-25 POS-ADMIN-CERTIFICATION-01 PASS 6 Operational Tooling & Dangerous Primitive Certification

- Tenant-admin operational search repair remains available, but it is no longer allowed to manufacture storefront product search visibility from partial SQL rows. `AdminSearchSyncController.SyncProducts(...)` now redeclares current SQL search authority through `ProductSearchSqlAuthorityOverlay` and maps Elasticsearch documents from the shared SQL-authoritative `ProductDocument` shape.
- The certified fail-closed behavior is explicit: if current SQL product/category/unit/publication authority no longer proves search visibility, the repair path deletes the stale Elasticsearch document instead of indexing a “baseline” document.
- Tenant-admin category and shipping-method Mongo sync now also converges stale derivative absence instead of only current-row presence. `AdminDataSyncController` declares SQL rebuild authority and deletes tenant-scoped stale Mongo documents that are missing from the current SQL authority set after sync.
- Tenant-admin product read-model repair remains SQL-backed through `IProductSqlAuthorityProjectionWriter`, and its post-rebuild product cache invalidation now routes through tenant-scoped `IProductCacheService`; `AdminSyncController` no longer enumerates or deletes Redis keys directly.
- Reviewed affiliate repair/reindex remains available, but it is no longer allowed to invent tenant-facing derivative currency during recovery. `AF16E_ReindexAffiliateProductsWithStorefrontUrl` now requires current SQL `Tenant.Currency` through `CurrencyNormalizationHelper.NormalizeCurrencyOrThrow(...)` and redeclares SQL repair authority through `SqlTruthRebuildGuard.RequireSqlAuthority(...)` before raw Mongo/Elasticsearch mutation.
- The certified fail-closed affiliate behavior is explicit: if current tenant storefront metadata or tenant currency authority is missing, AF16E repair deletes stale public affiliate and affiliate-intelligence visibility instead of preserving or rewriting derivative state with a silent `USD` fallback.
- Tenant-admin category search sync now also converges stale derivative absence instead of only current-row presence. `AdminSearchSyncController.SyncCategories(...)` declares SQL rebuild authority and deletes tenant-scoped stale Elasticsearch category documents that are missing from the current SQL category authority set after sync.
- This pass narrows concrete hidden-authority and primitive-bypass paths in operational tooling without removing recovery flexibility. Broader future CMS/SEO/analytics/cache-warmer/export/support operational rebuild paths still require path-specific certification before they are treated as safe Tier 1 recovery primitives.

## 2026-05-25 POS-ADMIN-CERTIFICATION-01 PASS 7 Replay / Rebuild / Chaos Certification

- Replay/rebuild/chaos certification validated duplicate replay, rebuild overlap, delayed queue catch-up, worker restart, cache/search recovery overlap, dead-letter retry, replay amplification containment, and recovery auditability as SQL-first convergence scenarios.
- Targeted hardening added payment-cancellation outbox row leases. `PaymentCancellationOutboxProcessor` now filters active claims, claims rows through `ClaimedUntilUtc`, uses an order-scoped PostgreSQL advisory transaction lock where available, and clears the lease on processed/manual-review/failure outcomes before another worker may retry after expiry.
- `PaymentCancellationOutboxOptions.ProcessingLeaseSeconds` is now the bounded crash-recovery control for that path. Payment lifecycle authority remains in `IPaymentLifecycleService`; the lease only collapses active duplicate workers before lifecycle entry.
- PASS 7 follow-up also hardens storefront footer replay. `StorefrontFooterProjectionConsumer` now revalidates active SQL tenant lineage after replay fencing and before Mongo replacement; inactive, deleted, or missing tenant authority deletes tenant-scoped footer visibility and emits convergence telemetry instead of preserving stale public footer state.
- PASS 7 follow-up on 2026-05-29 also hardens reviewed Redis invalidation replay. `RedisInvalidationConsumer` now fences homepage, CMS, footer, marketplace product/order, and marketplace review cache invalidation before Redis key or pattern fanout, so duplicate replay and active overlap collapse through processed-event telemetry instead of amplifying cache scans.
- Existing accounting, stock-sync, marketplace event, marketplace projection, search/reindex, homepage/recommendation, cache invalidation, affiliate repair, and financial replay protections remain in force.
- Certification findings live in `docs/architecture/replay-rebuild-chaos-certification-findings.md`. Remaining chaos debt is explicit: no adaptive scheduler, universal chaos framework, fixed rebuild SLO, SIEM audit lake, support impersonation, or universal CMS/SEO/analytics/feed/export/cache-warmer certification was introduced.

## 2026-05-26 POS-ADMIN-CERTIFICATION-01 PASS 8 Operational Survivability Certification

- Reviewed operational telemetry export is now part of the runtime survivability boundary. `AffiliateTelemetry`, `SearchTelemetry`, `InventoryReservationTelemetry`, and `CheckoutLockTelemetry` are wired into the OpenTelemetry meter pipeline together with the previously reviewed meters.
- PASS 8 follow-up now also exports reviewed accounting telemetry through `PointOfSale.Accounting` and bounds its singleton fallback counters by `tenant_bucket` plus normalized operation/gateway/error labels, so long-lived multi-tenant runtime does not accumulate one in-memory key per raw tenant or provider string.
- Reviewed exported operational metrics now use bounded dimensions on the hardened surfaces. Tenant-oriented labels converge through `tenant_bucket`, reviewed order-correlated diagnostics expose bounded presence flags instead of raw order ids, and reviewed free-form labels are normalized before emission.
- `CommerceHubReindexOptions` is now startup-validated, so invalid recovery pacing fails closed before deployment or recovery pressure reaches the running system.
- PASS 8 follow-up also normalizes the remaining reviewed exported `CommerceReliabilityTelemetry` provider/surface/endpoint/carrier/projection/storage-provider/stock-sync/isolation-level labels at the telemetry boundary itself instead of relying on caller discipline.
- PASS 8 long-term survivability follow-up also brings reviewed Redis cache performance telemetry into the OpenTelemetry export boundary. `PointOfSale.Redis.Performance` exposes bounded cache and pattern families only; raw Redis keys, invalidation patterns, tenant ids, product ids, and order ids remain logs/debug evidence rather than metric dimensions.
- PASS 8 long-term survivability follow-up also adds reviewed payment-cancellation readiness health. `payment_cancellation_outbox_readiness` now surfaces backlog, repeated failure, terminal failure, active-claim, and lease diagnostics for the replay-safe payment-cancellation worker instead of leaving that recovery path visible only through logs.
- PASS 8 follow-up also makes deployment and restore assumptions explicit in runbooks: rolling restart overlap is expected to converge through existing leases/fences, and PostgreSQL remains the only restore authority while MongoDB/Redis/Elasticsearch/RabbitMQ are rebuilt or reconciled as derivative systems after restore.
- This pass does not add an observability vendor platform, adaptive throttling, or synthetic SLO claims. It hardens diagnosability and operational safety on the reviewed replay/rebuild/search/reservation/checkout-lock surfaces that already exist.

## 2026-05-27 POS-ADMIN-CERTIFICATION-01 PASS 6 Service-Marketplace Recovery Follow-Up

- Legacy service-marketplace search recovery is now SQL-first on the reviewed replay/reconciliation boundary. `ServiceDiscoveryProjectionConsumer` reloads current SQL service, tenant, and provider-profile authority before preserving `marketplace_services` search visibility for create/update/activation/trust/statistics replay.
- `ElasticsearchDriftReconciliationJob` now declares global SQL search authority and restores only active, non-deleted services whose owning tenant is active and whose provider profile is active and same-tenant. Missing authority deletes or leaves absent stale service search documents.
- Raw service event payloads and raw active service ids are therefore no longer enough to resurrect legacy public service-search visibility under replay or drift repair pressure.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 9.1 CMS Public Visibility

- Storefront CMS public page reads are now SQL-authoritative on the inspected boundary. `CmsPageReadService` no longer lets Redis CMS scope cache or Mongo `StorefrontPageDocument` decide public visibility; current active tenant plus current same-tenant published, non-draft, non-deleted SQL `CmsPages` state must prove visibility.
- CMS SEO surfaces are now aligned with that authority model. CMS SEO metadata, CMS sitemap URLs, and CMS slug resolution derive from current SQL CMS publication state, and sitemap generation no longer returns Redis cached sitemap data before CMS SQL recomposition.
- Unified storefront search now treats Elasticsearch CMS hits as candidates only; CMS search results are filtered through current SQL tenant/page publication authority before response shaping.
- Tenant-admin CMS search repair is now a convergent SQL-first repair path. It declares `SqlAuthorityOverlayKind.StorefrontCms`, indexes only current published SQL CMS pages, and deletes tenant-scoped stale CMS search documents absent from current SQL authority.
- Regression protection was added in `CmsPageWorkflowTests` and `CmsPublicVisibilityCertificationTests`; findings and remaining debt are documented in `docs/architecture/cms-public-visibility-certification-findings.md`.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 9.2 Navigation / Category Lineage / Storefront Structure

- Storefront category structure is now SQL-authoritative on the reviewed public read boundary. `StorefrontCategoryService` recomposes category tree and category-by-slug reads from current PostgreSQL category lineage and active tenant authority instead of returning Redis/Mongo category projections as visibility authority.
- Category SEO, category sitemap URLs, category breadcrumb ancestry, and cached category slug resolution now derive from current SQL category lineage through `SeoService`; stale category cache, breadcrumb, sitemap, or projection state cannot preserve deleted, orphaned, self-referential, cyclic, or missing-parent category visibility.
- Storefront navigation reads now filter category menu links through current SQL category lineage before returning cached or Mongo-backed navigation. Navigation JSON and menu caches remain presentation evidence only and cannot own category visibility.
- Reviewed tenant-admin category Mongo/search repair remains SQL-first and stale-delete capable; category search repair and category Mongo sync continue to use `CategoryLineageAuthority` and tenant scope.
- Findings, guarantees, regression-freeze coverage, and remaining debt are recorded in `docs/architecture/category-lineage-certification-findings.md`.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 9.3 Product Detail / Media / Public Projection

- Storefront product detail, deprecated Mongo list, category-tree list, product search hydration, discovery response shaping, product SEO metadata, product breadcrumbs, product slug resolution, and product sitemap URLs are now SQL-authoritative on the reviewed public boundary.
- Redis, MongoDB, Elasticsearch, search hits, SEO caches, and sitemap caches are candidate evidence only for reviewed product public reads. Current `ProductSearchSqlAuthorityOverlay` recomposes SQL product/category/media/variant/unit/stock/promotion authority and public reads require `{ IsActive: true, IsOnlineVisible: true }` before returning or caching product visibility.
- Product media visibility now stays tied to current SQL `ProductMedia` plus `ProductVariants.MediaId`; the shared product SQL overlay no longer carries legacy variant image strings into refreshed variant projections.
- The shared product SQL overlay now also requires active SQL tenant lineage, so inactive or removed tenant authority fails closed before product/detail/search/SEO media visibility is retained.
- CommerceHub listing/detail/search/homepage/rebuild protections from PASS 5/PASS 7 remain in force: SQL listing/product/variant/category/media/stock/price/review/vendor authority decides marketplace visibility.
- Findings, guarantees, regression-freeze coverage, and remaining debt are recorded in `docs/architecture/product-detail-media-public-projection-certification-findings.md`.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 9.4 Homepage / Discovery / Recommendations

- Storefront homepage shelves, featured products, new arrivals, best sellers, curated product hydration, homepage categories, CommerceHub homepage projections, public affiliate discovery, and public affiliate product reads are SQL-authoritative on the reviewed visibility boundary.
- Redis homepage caches, MongoDB product/homepage documents, Elasticsearch ranking/search hits, analytics popularity inputs, homepage section JSON, affiliate search documents, and replayed rebuild payloads are candidate or ordering evidence only.
- `AffiliateProductDiscoveryService` and `PublicAffiliateProductsController` now recompose Elasticsearch affiliate discovery candidates through current SQL product/category/media/variant/unit/stock/publication, active tenant, affiliate-policy, storefront URL, and storefront affiliate pricing authority before returning public visibility.
- Existing storefront homepage and CommerceHub homepage protections remain in force: current SQL tenant/product/category/listing/vendor authority decides return eligibility, and stale curated references are scrubbed or omitted.
- Findings, guarantees, regression-freeze coverage, and remaining debt are recorded in `docs/architecture/homepage-discovery-recommendation-certification-findings.md`.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 9.5 Search / SEO / Public Indexes

- Storefront unified search, storefront header autocomplete, storefront product search/discovery, CommerceHub search/list/suggestions/autocomplete, legacy affiliate portal public discovery, SEO metadata, sitemaps, and breadcrumb JSON-LD were certified against SQL-authoritative public visibility.
- Elasticsearch, MongoDB affiliate intelligence, Redis SEO/sitemap caches, suggestions, popular terms, and rebuild inputs are now documented and tested as candidate/cache evidence only on the reviewed boundaries.
- Runtime hardening added SQL recomposition for storefront unified/header search and legacy affiliate portal discovery, changed reviewed product/CommerceHub/affiliate search counts to SQL-visible returned items, suppressed raw index-derived public aggregations where SQL-visible aggregation is not available, and rebuilt CommerceHub public category/price facets from visible listings only.
- CommerceHub reindex remains SQL-first and stale-delete capable through `CommerceHubSearchSqlAuthorityOverlay`, SQL listing seeds, and SQL-visible count verification before alias promotion.
- No reviewed product feed generator or product/organization JSON-LD generator exists in the inspected backend; those remain future path-specific certification debt.
- Findings, guarantees, regression-freeze coverage, and remaining debt are recorded in `docs/architecture/search-seo-public-index-certification-findings.md`.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 9.6 Storefront Recovery, Cache & Projection Rebuild

- Storefront recovery, projection rebuild, cache recovery, startup bootstrap, replay, repair tooling, deletion convergence, CommerceHub recovery, and tenant isolation were certified on the reviewed public-visibility boundaries.
- Product projection rebuild, storefront search repair, category/CMS repair, promotion repair, Redis invalidation, CommerceHub reindex, CommerceHub startup/background auto-healing, and CommerceHub search analytics recovery remain SQL-authoritative convergence paths. MongoDB, Redis, Elasticsearch, replay payloads, repair snapshots, and startup state are candidate/derivative evidence only.
- Runtime hardening tightened CommerceHub auto-healing recovery identity from listing-id-only to `{ TenantId, ListingId }` where tenant evidence is available, including startup/background grouping, in-flight dedupe, active processing, and batch Mongo lookup.
- Reviewed cache recovery remains invalidation-first or SQL-read-repopulated. Cache state cannot restore public product/category/CMS/listing visibility.
- Findings, guarantees, regression-freeze coverage, and remaining debt are recorded in `docs/architecture/storefront-recovery-rebuild-certification-findings.md`.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 10.1 Event Propagation & Projection Freshness

- Event propagation, outbox reliability, consumer reliability, idempotency, ordering, projection freshness, lag detection, replay/catch-up, startup recovery, and chaos/delay convergence were certified as observable eventual consistency boundaries.
- SQL remains authoritative. MassTransit/RabbitMQ, dedicated SQL outboxes, MongoDB, Redis, Elasticsearch, CommerceHub projections, homepage/discovery projections, and search projections are derivative convergence systems.
- Runtime hardening added bounded `OperationalConvergenceTelemetry` emissions from projection and MassTransit outbox readiness checks, so blocked or lagging propagation is visible through the convergence meter in addition to health-check responses and logs.
- Existing leases, processed-event fences, version freshness checks, SQL authority overlays, stale-delete/tombstone behavior, and replay/rebuild recovery paths remain the convergence guarantees for duplicate, delayed, out-of-order, and restarted-worker scenarios.
- Findings and remaining debt are recorded in `docs/architecture/event-propagation-projection-freshness-certification-findings.md`.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 10.2 Redis Cache Freshness

- Redis cache freshness, invalidation correctness, cache convergence, warmer safety, cache recovery, and cache observability were certified as derivative convergence boundaries.
- Redis remains a performance, coordination, and ephemeral-state layer only. Product, category, CMS, homepage, search, SEO, footer, affiliate, and CommerceHub public visibility must still be accepted by current SQL authority before return or cache population.
- Runtime hardening closed adjacent cache-resurrection gaps on marketplace CMS/footer and storefront footer reads. Marketplace CMS detail reads now use current SQL by slug, marketplace CMS cached scope reads are filtered against current SQL published/non-draft rows, and cached marketplace/storefront footer CMS links are filtered through current SQL CMS publication before return.
- Product and general Redis invalidation retain duplicate/active replay collapse through `ProjectionConsumerEventFence` and `ProjectionEventFence`; Redis invalidation remains best-effort, with convergence provided by SQL-filtered reads, bounded TTLs, and observable lag/Redis performance telemetry.
- Findings and remaining debt are recorded in `docs/architecture/redis-cache-freshness-invalidation-certification-findings.md`.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 10.3 Mongo Projection Convergence

- Mongo projection convergence, projection correctness, rebuild safety, repair safety, freshness metadata, drift observability, startup recovery, and chaos behavior were certified as derivative SQL-first convergence boundaries.
- MongoDB remains a primary read model but not business truth. Storefront product/category/CMS/config/navigation/footer/order projections, CommerceHub product/order/homepage/review projections, affiliate intelligence/public/analytics projections, and analytics/support documents may lag but must not permanently diverge on reviewed public-visibility boundaries.
- Reviewed product Mongo writes converge through `IProductSqlAuthorityProjectionWriter`, `ProductSearchSqlAuthorityOverlay`, freshness filters, and tombstones. Category/CMS/homepage/CommerceHub/affiliate repair and rebuild paths remain SQL-derived and stale-delete/tombstone capable where they touch public visibility.
- Drift is observable through projection/outbox readiness, `OperationalConvergenceTelemetry`, document-level freshness markers, processed-event receipts, CommerceHub search sync metadata, and SQL-derived repair/rebuild diagnostics. This pass does not add a universal Mongo-vs-SQL row scanner or strong consistency guarantee.
- Regression-freeze coverage was added in `MongoProjectionConvergenceCertificationTests`; findings and remaining debt are recorded in `docs/architecture/mongo-projection-convergence-certification-findings.md`.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 10.4 Elasticsearch Freshness & Search Convergence

- Elasticsearch freshness, search convergence, index correctness, reindex safety, repair safety, drift observability, startup recovery, and chaos behavior were certified as derivative SQL-first convergence boundaries.
- Elasticsearch indexes found on the reviewed backend are `products`, `categories`, `cmspages`, `commercehub_products` with versioned `commercehub_products_v{n}` alias targets, `public_affiliate_products`, and `marketplace_services`. No separate authoritative suggestion/autocomplete or analytics Elasticsearch index was found.
- Product/category/CMS/CommerceHub/affiliate/service search writes and repair paths remain SQL-derived and stale-delete capable on reviewed public-visibility boundaries. Elasticsearch hits, autocomplete fields, suggestions, facets, counts, and alias targets are candidate/search infrastructure only.
- Runtime hardening changed `CommerceHubSearchSyncHealthCheck` to compare Elasticsearch freshness against current SQL-visible CommerceHub listing authority instead of Mongo published projection count, and to emit bounded `OperationalConvergenceTelemetry` search-drift diagnostics.
- Regression-freeze coverage was added in `ElasticsearchFreshnessSearchConvergenceCertificationTests`; findings and remaining debt are recorded in `docs/architecture/elasticsearch-freshness-search-convergence-certification-findings.md`.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 10.5 Cross-System Convergence Certification

- Whole-system convergence across PostgreSQL, MongoDB, Redis, Elasticsearch, outbox propagation, repair/rebuild tooling, startup recovery, freshness telemetry, drift detection, SLO thresholds, and chaos behavior was certified as observable eventual consistency.
- The certified hierarchy remains PostgreSQL authority, MongoDB read model, Redis acceleration layer, and Elasticsearch discovery layer. Derivative systems may lag, but reviewed public-visibility paths must converge toward current SQL truth by event propagation, SQL-filtered reads, stale-delete/tombstone repair, cache invalidation/TTL, and SQL-first reindex.
- Cross-system drift is observable through projection/outbox readiness, Mongo projection drift health, Redis cache envelopes/performance telemetry, CommerceHub search sync health, service-marketplace drift reconciliation, document freshness markers, processed-event receipts, search sync metadata, and bounded `OperationalConvergenceTelemetry`.
- Regression-freeze coverage was added in `CrossSystemConvergenceCertificationTests`; findings and remaining debt are recorded in `docs/architecture/cross-system-convergence-certification-findings.md`.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 10.6 Chaos, Lag & Operational Survivability Certification

- Operational survivability under MongoDB, Redis, Elasticsearch, consumer, outbox, replay, repair, reindex, worker restart, startup recovery, and multi-system lag conditions was certified as observable eventual convergence, not zero downtime.
- PostgreSQL remains the only internal business authority during derivative outages. MongoDB, Redis, Elasticsearch, RabbitMQ, MassTransit, outbox rows, replay payloads, repair snapshots, search aliases, cache values, and startup state remain convergence evidence only.
- Reviewed outage behavior is fail-closed or SQL-filtered: Mongo lag blocks or degrades projections, Redis loss becomes cache miss/repopulation, Elasticsearch outage degrades search, consumers and outboxes create observable lag, and repair/replay/rebuild paths revalidate current SQL before derivative mutation.
- Regression-freeze coverage was added in `ChaosLagOperationalSurvivabilityCertificationTests`; findings and remaining debt are recorded in `docs/architecture/chaos-lag-operational-survivability-certification-findings.md`.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 11.1 Super Admin Identity & Session Security Certification

- Platform-operator identity, session authority, access-token replay, refresh rotation, logout, password rotation, governance restriction, account deletion, middleware routing, legacy endpoint scope, tenant isolation, auditability, and chaos behavior were certified against the current SQL platform-authority model.
- Super-admin authority remains `Current SQL SuperAdmin Authority + Current Governance State + Current Token Version`. JWTs, cookies, role claims, refresh credentials, Redis state, and session cache state are evidence only.
- Runtime hardening made platform authority and operational audit middleware honor `[AllowAnonymous]`, moved legacy service-marketplace global reindex to a dedicated `RequireSuperAdmin` route, tenant-scoped tenant diagnostics, and removed `SuperAdmin` from a tenant-admin manual-wallet role list.
- Regression-freeze coverage was added in `SuperAdminIdentitySessionSecurityCertificationTests`; findings and remaining debt are recorded in `docs/architecture/super-admin-identity-session-security-certification-findings.md`.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 11.2 Global Scope & Tenant Boundary Certification

- Global scope, tenant scope, hybrid marketplace scope, recovery/replay/reindex scope, diagnostics scope, service-layer scope, tenant-context propagation, auditability, and chaos behavior were certified against the current platform/tenant authority split.
- Platform authority remains explicit through `RequireSuperAdmin`, `SuperAdminPlatformAuthorityMiddleware`, current SQL super-admin authority, current governance state, and token version. Tenant authority remains isolated through trusted tenant context, current SQL tenant actor authority, active tenant lineage, tenant filters, and SQL authority overlays.
- Reviewed tenant reindex, repair, recovery, diagnostics, and marketplace service paths remain tenant-scoped. Reviewed global reindex/repair paths are intentionally global only under platform authority and do not inherit tenant filters accidentally.
- Regression-freeze coverage was added in `GlobalScopeTenantBoundaryCertificationTests`; findings and remaining debt are recorded in `docs/architecture/global-scope-tenant-boundary-certification-findings.md`.

## 2026-05-29 POS-ADMIN-CERTIFICATION-01 PASS 11.3 Marketplace Governance Authority Certification

- Marketplace governance, listing approval, listing suspension/rejection, vendor suspension/reactivation, homepage curation, recommendation-like shelves, discovery, promoted/featured placement, replay/recovery, auditability, and chaos behavior were certified as governance decision authority only.
- Marketplace visibility remains current SQL listing, product, vendor, category, and tenant authority. Governance references, homepage JSON, recommendations, popularity/search analytics, MongoDB, Redis, Elasticsearch, replay payloads, and recovery snapshots are candidate or convergence evidence only.
- Runtime hardening tightened tenant-admin listing publish/update to require live CommerceHub category lineage and current product/variant public authority, and tightened vendor-governance restore so stale `RestorePublicationOnActivate` cannot republish after product/category/tenant/variant authority is gone.
- Regression-freeze coverage was added in `MarketplaceGovernanceAuthorityCertificationTests`; findings and remaining debt are recorded in `docs/architecture/marketplace-governance-authority-certification-findings.md`.

## 2026-05-30 POS-ADMIN-CERTIFICATION-01 PASS 11.4 Global Search, Reindex & Recovery Certification

- Global search operations, tenant search repair, CommerceHub reindex, startup/background auto-healing, replay, recovery, projection repair, and operational diagnostics were certified as SQL-derived convergence tooling, not authority.
- Reindex, replay, repair, recovery, healing, and startup paths may discover candidates from MongoDB, Redis, Elasticsearch, queue state, or repair snapshots, but reviewed public search/listing mutation must revalidate current SQL product/listing/category/vendor/tenant/variant authority before indexing or preserving visibility.
- Runtime hardening added a CommerceHub stale Elasticsearch document sweep: global, target-index, and tenant reindex now delete search documents absent from current SQL-visible CommerceHub listing authority before completion or alias count verification.
- Regression-freeze coverage was added in `GlobalSearchReindexRecoveryCertificationTests`; findings and remaining debt are recorded in `docs/architecture/global-search-reindex-recovery-certification-findings.md`.

## 2026-05-30 POS-ADMIN-CERTIFICATION-01 PASS 11.5 Emergency Operations & Break-Glass Certification

- Emergency operations, break-glass pressure, marketplace emergency controls, recovery, repair, reindex, replay, shutdown/disable, diagnostics, tenant isolation, revocation, auditability, runbooks, and chaos behavior were certified as operational authority only.
- Emergency authority may stop, suspend, freeze, isolate, recover, repair, reindex, replay, or diagnose. It may not create visibility authority, publication authority, tenant authority, governance authority, or bypass SQL authority.
- Reviewed emergency paths remain explicit through `RequireSuperAdmin` or tenant-admin authority, current SQL actor/governance/session validation, SQL overlays/lineage helpers, lifecycle/reconciliation owners, replay fences/leases, stale-delete/tombstone behavior, and request/domain audit.
- No production support impersonation or tenant override endpoint was certified. Future break-glass support tooling requires SQL platform actor authority, target tenant lineage, reason/reference, expiry/revocation semantics, durable audit, replay safety, docs, and tests before rollout.
- Regression-freeze coverage was added in `EmergencyGovernanceCertificationTests`; findings and remaining debt are recorded in `docs/architecture/emergency-governance-certification-findings.md`.

## 2026-05-30 POS-ADMIN-CERTIFICATION-01 PASS 12.1 Merchant Publication Intent Authority

- CommerceHub merchant publication intent was certified as tenant-owned request authority only. Publish, republish, update, and remove-from-marketplace are not marketplace visibility authority.
- Public marketplace visibility remains current SQL listing, product, variant, CommerceHub category, tenant, vendor-governance, pricing, stock, media, and review authority plus marketplace governance enforcement.
- Runtime hardening changed tenant unpublish so governance-restricted vendors are not blocked from withdrawing publication intent or clearing stale restore intent; publish/update still require current vendor governance, live category lineage, active shipping, current product publication, and public variant lineage.
- Replay, recovery, reindex, search, homepage/discovery, and auto-healing remain SQL-derived convergence paths. Derivative MongoDB, Redis, Elasticsearch, queue payloads, and recovery snapshots may discover candidates but cannot create publication or visibility.
- Regression-freeze coverage was added in `PublicationIntentAuthorityCertificationTests`; findings and remaining debt are recorded in `docs/architecture/publication-intent-authority-certification-findings.md`.

## 2026-05-31 POS-ADMIN-CERTIFICATION-01 PASS 12.2 Marketplace Category Mapping & Taxonomy Authority

- CommerceHub marketplace category mapping was certified as classification authority only, not visibility authority.
- SQL `CommerceHubCategories`, `CommerceHubCategoryAttributes`, and `CommerceHubListings.CommerceHubCategoryId` own taxonomy, attribute schema, and listing classification. MongoDB category fields, Elasticsearch category documents, homepage category references, suggestions, autocomplete buckets, cached category trees, and filter metadata are derivative evidence only.
- Runtime hardening now filters the public CommerceHub category tree through live SQL category lineage, rejects category-attribute requests for non-live lineage, and makes attribute validation fail closed when the selected category lineage is invalid.
- Publish/update, public search/detail/list, homepage/discovery, projection, reindex, health, and auto-healing paths continue to require current SQL listing/product/variant/category/tenant/vendor authority before preserving marketplace visibility.
- Regression-freeze coverage was added in `MarketplaceCategoryTaxonomyCertificationTests`; findings and remaining debt are recorded in `docs/architecture/marketplace-category-taxonomy-certification-findings.md`.

## 2026-05-31 POS-ADMIN-CERTIFICATION-01 PASS 12.3 Marketplace Attribute & Filter Governance

- CommerceHub marketplace attributes were certified as taxonomy-bound classification/search metadata only, not publication, approval, or visibility authority.
- SQL `CommerceHubCategoryAttributes` owns the current category attribute schema. `CommerceHubListings.AttributesJson`, listing schema snapshots, Mongo attributes, Elasticsearch attributes, search filters, and facets are derivative evidence only.
- Runtime hardening rejects unsupported merchant attribute keys, loads filter/facet definitions only from live SQL category lineage, fails closed for unsupported public attribute filters, and strips deleted/unsupported stored attributes before Mongo/Elasticsearch projection through the SQL search overlay.
- Publish/update, search, facets, projection, indexing, replay, recovery, and reindex paths continue to require current SQL listing/product/variant/category/tenant/vendor authority before preserving marketplace visibility.
- Regression-freeze coverage was added in `MarketplaceAttributeFilterGovernanceCertificationTests`; findings and remaining debt are recorded in `docs/architecture/marketplace-attribute-filter-governance-certification-findings.md`.

## 2026-05-31 POS-ADMIN-CERTIFICATION-01 PASS 12.4 Marketplace Pricing Authority

- CommerceHub marketplace item pricing was certified as commercial authority owned by current SQL product variant pricing, not by listing overrides, merchant payloads, MongoDB, Elasticsearch, Redis, client snapshots, or storefront promotion overlays.
- Final CommerceHub item price is `ProductVariant.EffectiveOnlinePrice`: current SQL `OnlinePrice` when positive, otherwise current SQL `Price`. Cart, checkout, projection, and search paths recompose or validate against that SQL authority.
- Marketplace price overrides remain disabled. `CommerceHubListing.CommerceHubPrice` is rejected/cleared, SQL-constrained to null, and derivative `CommerceHubPriceOverride` fields are cleared during projection and search overlay.
- Runtime hardening completed CommerceHub projection fences for SQL-rejected variant-price events and promotion-triggered SQL base-price refresh, reducing duplicate replay and stale price resurrection pressure.
- Regression-freeze coverage was added in `MarketplacePricingAuthorityCertificationTests`; findings and remaining debt are recorded in `docs/architecture/marketplace-pricing-authority-certification-findings.md`.

## 2026-05-31 POS-ADMIN-CERTIFICATION-01 PASS 12.5 Variant & Inventory Marketplace Certification

- CommerceHub marketplace variants, stock calculations, availability, cart pricing, checkout reservation, search projection, replay, recovery, and merchant variant governance were certified as SQL-authoritative variant and inventory lineage.
- Variant authority remains current SQL `ProductVariants` plus `ProductVariantLifecycleAuthority`; inventory authority remains current SQL `ProductVariantStocks`, `Warehouses`, `InventoryReservations`, and `StockMovements` plus `ProductVariantInventoryLineageAuthority`.
- Runtime hardening routed CommerceHub cart and checkout variant authority loading through `WherePubliclyVisibleLineage` before accepting price or stock, so superseded/deleted/detached variants fail closed on purchase boundaries.
- Search and discovery inventory remains derivative through `CommerceHubSearchSqlAuthorityOverlay`, projection fences, stock timestamps, event versions, and SQL recomposition before public visibility.
- Regression-freeze coverage was added in `MarketplaceVariantInventoryCertificationTests`; findings and remaining debt are recorded in `docs/architecture/marketplace-variant-inventory-certification-findings.md`.

## 2026-05-31 POS-ADMIN-CERTIFICATION-01 PASS 12.6 Merchant vs Marketplace Governance Precedence Certification

- Merchant publication intent, listing updates, suppression, vendor governance, category/product governance, search/discovery, replay/recovery, auditability, and chaos behavior were certified as subordinate to current SQL marketplace governance authority.
- Merchant authority remains request authority. Marketplace governance remains decision authority and may allow, deny, suppress, suspend, clear stale restore intent, or restore only when current SQL tenant/product/variant/category/listing authority still accepts visibility.
- Runtime hardening made `CommerceHubSearchSqlAuthorityOverlay` and CommerceHub SQL-visible reindex identity scans suppress vendors whose current SQL `UserGovernanceProfile` is `Suspended` or `Banned`, preventing stale `IsPublished` rows or derivative search documents from surviving governance lag.
- Regression-freeze coverage was added in `MerchantMarketplaceGovernancePrecedenceCertificationTests`; findings and remaining debt are recorded in `docs/architecture/merchant-marketplace-governance-precedence-certification-findings.md`.

## 2026-05-31 POS-ADMIN-CERTIFICATION-01 PASS 13.3 CommerceHubPriceAuthority Resolver

- CommerceHub marketplace price resolution is now centralized through `ICommerceHubPriceAuthority` / `CommerceHubPriceAuthority`.
- Runtime behavior is unchanged: final CommerceHub item price remains current SQL `ProductVariant.EffectiveOnlinePrice`.
- CommerceHubPrice override remains disabled. `CommerceHubPriceAuthority.OverridesEnabled` is `false`, listing override persistence remains cleared/constrained, and derivative override fields remain stripped.
- Publish/update/unpublish, cart, checkout, Mongo projection, search SQL overlay, reindex, recovery, auto-healing, startup rebuild, and query/detail hydration now inherit the centralized resolver directly or through `CommerceHubSearchSqlAuthorityOverlay`.
- Regression-freeze coverage was added in `CommerceHubPriceAuthorityCertificationTests`; findings and remaining debt are recorded in `docs/architecture/commercehub-price-authority-resolver-certification-findings.md`.

## 2026-05-31 POS-ADMIN-CERTIFICATION-01 PASS 13.4 CommerceHub Price Override Persistence

- CommerceHub price override persistence and audit infrastructure is now prepared without activating overrides.
- Runtime behavior is unchanged: final CommerceHub item price remains current SQL `ProductVariant.EffectiveOnlinePrice`, `CommerceHubPriceAuthority.OverridesEnabled` remains `false`, cart and checkout continue resolving through the centralized resolver, and customer-visible prices are unchanged.
- SQL `CommerceHubListings.CommerceHubPrice` remains the future compatibility storage field but is still rejected, cleared, and constrained to null. Nullable audit metadata now exists for future attribution: `CommerceHubPriceOverrideUpdatedAtUtc`, `CommerceHubPriceOverrideUpdatedBy`, and `CommerceHubPriceOverrideReason`.
- CommerceHub Mongo and Elasticsearch metadata now expose derivative future-state fields `PriceSource` and `OverridePresent`; current projection and search paths emit `ProductVariantEffectiveOnlinePrice` and `false`.
- Regression-freeze coverage was added in `CommerceHubPriceOverridePersistenceCertificationTests`; findings and remaining debt are recorded in `docs/architecture/commercehub-price-override-persistence-audit-certification-findings.md`.

## 2026-05-31 POS-ADMIN-CERTIFICATION-01 PASS 13.6 CommerceHub Price Override Controlled Activation

- Controlled override activation was reviewed and rejected. The platform is not activation-ready, so no partial runtime activation was performed.
- `CommerceHubPriceAuthority.OverridesEnabled` remains `false`; listing writes remain rejected/cleared/SQL-constrained to null; derivative override state remains cleared.
- Required blockers include override-specific merchant manage permission, marketplace audit/emergency-disable permissions, merchant mutation workflow, immutable audit events, tenant-safe global rollout flag, active-state parity monitoring, operator tooling, and a certified rollback runbook.
- Regression-freeze coverage was added in `CommerceHubPriceOverrideActivationCertificationTests`; findings are recorded in `docs/architecture/commercehub-price-override-controlled-activation-certification-findings.md`.

## 2026-05-31 POS-ADMIN-CERTIFICATION-01 PASS 13.5 CommerceHub Price Override Governance

- CommerceHub price overrides remain disabled. PASS 13.5 freezes the required future operational model only.
- Tenant merchants own CommerceHub price override decisions. Marketplace governance owns visibility, suppression, suspension, emergency disable, audit, and rollback authority, but may not approve, reject, create, or edit merchant prices.
- Future activation requires explicit merchant manage, marketplace audit, and marketplace emergency-disable permissions; a global kill switch plus tenant allowlist and optional cohorts; immutable override events; staged rollout; non-destructive rollback to `ProductVariant.EffectiveOnlinePrice`; parity monitoring; and operator tooling.
- The PASS 13.6 checkout baseline blocker was fixture drift: financial-control test products were not seeded as published and online-visible after checkout adopted public SQL lineage. The fixtures were aligned, but future active-override checkout parity certification remains mandatory.
- Regression-freeze coverage was added in `CommerceHubPriceOverrideGovernanceCertificationTests`; findings are recorded in `docs/architecture/commercehub-price-override-governance-rollout-certification-findings.md`.

## 2026-05-31 POS-ADMIN-CERTIFICATION-01 PASS 13.5B CommerceHub Price Override Business Model Correction

- CommerceHub is merchant-controlled: tenant merchants own publication intent, listing content, and CommerceHub price overrides.
- Marketplace governance owns visibility, suppression, vendor suspension, tenant suspension, emergency disable, marketplace integrity, and auditability only. Audit is oversight, not pricing approval.
- The approval-centric PASS 13.5 assumptions were removed. No marketplace price approval permission or pricing approval/rejection workflow is allowed.
- Merchant override lifecycle is optional `Draft`, then `Saved`, `Updated`, or `Removed`. Runtime override activation remains prohibited and all existing safety locks remain closed.
- Regression-freeze coverage was added in `CommerceHubPriceOverrideBusinessModelCertificationTests`; findings are recorded in `docs/architecture/commercehub-price-override-business-model-correction-certification-findings.md`.
## PASS 13.7 CommerceHub Price Override Implementation

- Tenant merchants may persist positive CommerceHub listing price overrides through the existing tenant-scoped CommerceHub settings workflow.
- `CommerceHubPriceAuthority` is the only active runtime pricing resolver: enabled allowlisted tenants use `CommerceHubListing.CommerceHubPrice ?? ProductVariant.EffectiveOnlinePrice`; disabled tenants fall back without deleting stored records.
- `CommerceHub:PriceOverrides` defaults off and supports a global kill switch, tenant allowlist, optional cohorts, and configuration reload.
- Override save, update, and removal publish immutable authoritative events with actor, tenant, listing, previous/new values, timestamp, correlation id, reason, and mutation version.
- Marketplace governance remains visibility-only and may suppress listings without approving, rejecting, creating, or editing merchant prices.
## 2026-05-31 POS-ADMIN-CERTIFICATION-01 PASS 14.1 CommerceHub Product Publication Lifecycle Certification

- CommerceHub publication is certified end-to-end from tenant merchant selection through SQL listing intent, taxonomy/attribute validation, PASS 13.7 price authority, projection, search, discovery, cart, checkout, unpublish, governance suppression, conditional restore, replay, reindex, and auto-healing.
- Merchant withdrawal is visibility-reducing intent authority and must remain available after variant deletion or semantic replacement. `UnpublishFromCommerceHubHandler` resolves the tenant listing first, uses explicit tenant predicates with `IgnoreQueryFilters()`, and clears stale publication/restore intent even when current variant authority is gone.
- MongoDB, Elasticsearch, Redis, event payloads, homepage/discovery candidates, and recovery inputs remain derivative. Current SQL listing, tenant, vendor governance, product, variant, category, attribute, pricing, stock, and media authority wins.
- PASS 14.1 findings are recorded in `docs/architecture/commercehub-publication-lifecycle-certification-findings.md`.

## 2026-06-01 POS-ADMIN-CERTIFICATION-01 PASS 14.2 Merchant CommerceHub Operations Certification

- Merchant operations are partially certified: single-listing publication and existing SQL-backed finance workflows are usable, but the POS Admin listing surface is not a complete daily operations console.
- Runtime hardening now preserves actionable backend failure reasons in `CommerceHubSettings.jsx`.
- Merchant listing search, operational filters, suppression and orphan cleanup visibility, authoritative listing KPI APIs, bulk workflows, listing analytics, dedicated notifications, and merchant parity/drift monitoring remain required before production certification.
- Regression-freeze coverage was added in `MerchantCommerceHubOperationsCertificationTests`; findings are recorded in `docs/architecture/merchant-commercehub-operations-certification-findings.md`.

## 2026-06-01 POS-ADMIN-CERTIFICATION-01 PASS 14.3 Merchant Listing Operations Console Certification

- Merchant listing operations are now certified for core production use: SQL-authoritative listing search, operational filters, suppression visibility, orphan visibility, bulk operations with per-row outcomes, KPIs, analytics, and actionable notifications are exposed through tenant-scoped admin APIs and `CommerceHubSettings.jsx`.
- Merchant visibility into suppression remains read-only. Marketplace governance still owns suppression and visibility decisions; merchant bulk publish/category operations fail closed for suppressed or orphaned rows, while bulk unpublish remains visibility-reducing cleanup authority.
- Remaining debt is operational maturity: SQL-vs-derivative parity/drift scanner, dedicated alert dashboards, bulk attribute editing, richer immutable notification inbox, and deeper analytics segmentation.
- Regression-freeze coverage was updated in `MerchantCommerceHubOperationsCertificationTests`; findings are recorded in `docs/architecture/merchant-commercehub-operations-certification-findings.md`.

## 2026-06-01 POS-ADMIN-CERTIFICATION-01 PASS 15.1 Marketplace Governance Console Certification

- Super Admin marketplace governance is now certified for core production visibility governance: SQL-backed dashboard counts, global listing search, governance filters, listing suppression/restoration, tenant suspension/restoration, audit records, and frontend controls are exposed through `SuperAdminMarketplaceGovernanceController` and `MarketplaceGovernancePage.tsx`.
- Listing restoration fails closed unless current SQL tenant, vendor governance, product publication/online visibility, current variant lifecycle, and live CommerceHub category lineage accept visibility. Suppression and tenant governance trigger CommerceHub auto-healing verification.
- Marketplace governance remains visibility-only. Merchant-owned publication intent, category selection, attributes, and CommerceHub pricing are not mutated by Super Admin governance controls.
- Remaining debt is operational maturity: SQL-vs-derivative parity dashboards, governance telemetry counters and alert rules, immutable operator notifications, vendor-governance shortcuts from the marketplace console, and production query-plan/index review.
- Regression-freeze coverage was added in `MarketplaceGovernanceConsoleCertificationTests`; findings are recorded in `docs/architecture/marketplace-governance-console-certification-findings.md`.

## 2026-06-01 POS-ADMIN-CERTIFICATION-01 PASS 15.2 Marketplace Operations Console Certification

- Super Admin marketplace operations are now certified for core CommerceHub search/projection recovery: SQL/Mongo/Elasticsearch/auto-healing diagnostics, global reindex, tenant reindex, catch-up reindex, listing verification, and tenant verification are exposed through `SuperAdminMarketplaceOperationsController` and `MarketplaceOperationsPage.tsx`.
- Reindex, recovery, repair, and auto-healing remain derivative convergence operations. Current PostgreSQL listing/product/variant/category/tenant/vendor authority decides visibility; MongoDB, Elasticsearch, queue state, and dashboard counts are candidate discovery or diagnostics only.
- Operator actions require reasons, use Super Admin audit records, and tenant/listing verification queues preserve tenant scope before auto-healing recomposes through `CommerceHubSearchSqlAuthorityOverlay`.
- Remaining debt is operational maturity: SQL-vs-Mongo-vs-Elasticsearch parity drill reports, immutable operator notifications, operation progress history, production query-plan review, and broader non-search CommerceHub recovery dashboards.
- Regression-freeze coverage was added in `MarketplaceOperationsConsoleCertificationTests`; findings are recorded in `docs/architecture/marketplace-operations-console-certification-findings.md`.

## 2026-06-01 POS-ADMIN-CERTIFICATION-01 PASS 15.3 Marketplace Finance & Risk Certification

- CommerceHub finance is certified for core production use under SQL/ledger authority. Order/sub-order snapshots, commission snapshots, vendor earnings, refunds, payouts, financial transactions, accounting outbox rows, and reconciliation states are PostgreSQL-authoritative.
- Runtime hardening added the missing CommerceHub payment lifecycle boundary around delivered financial workflow COD collection/payment-field mutations. Inspection also verified existing hardening: commission snapshot arithmetic validation, non-pending earning repricing locks, serializable payout/refund/ledger transactions, payout earning row locks, ledger-confirmed payout eligibility, external payout handoff/evidence fences, refund overrun validation, refund/payout overlap blocking, readiness-gated outward money movement, and immutable ledger fingerprint idempotency.
- Replay cannot create money and recovery cannot destroy money on certified paths because ledger writes append only when the idempotency/fingerprint proof matches the financial fact; duplicate races detach pending financial transaction and accounting outbox rows.
- Remaining debt is operational maturity: finance parity drill reports, alert packs, immutable operator notifications, production query-plan/index review, and a PostgreSQL-backed chaos harness for concurrent payout/refund/replay/recovery.
- Regression-freeze coverage was added in `MarketplaceFinanceRiskCertificationTests`; findings are recorded in `docs/architecture/commercehub-finance-risk-certification-findings.md`.

## 2026-06-01 POS-ADMIN-CERTIFICATION-01 PASS 15.4 CommerceHub Emergency Operations

- CommerceHub emergency operations and incident response are certified for core production use across visibility suppression, tenant suspension, search/projection recovery, finance stops, rollback, diagnostics, runbooks, auditability, frontend/backend alignment, operator safety, and incident-day chaos.
- Runtime hardening added default-off `CommerceHub:EmergencyStops` controls. `CommerceHubFinancialReadinessGate` now blocks payment intake and outward money movement before checkout/payment, payout, or refund execution when `CommerceIntakeStopped`, `PaymentIntakeStopped`, `OutwardMoneyMovementStopped`, `PayoutsStopped`, or `RefundsStopped` are enabled.
- Marketplace visibility can be stopped through governance suppression, tenant suspension, vendor governance, SQL overlays, and verification. Visibility restoration remains conditional on current SQL tenant/vendor/product/variant/category authority.
- Replay/recovery stop is partially certified through cancellation, pacing, scoped operation choice, and SQL-first restart semantics; universal durable pause/resume controls remain operational maturity debt.
- Regression-freeze coverage was added in `MarketplaceEmergencyOperationsCertificationTests`; findings are recorded in `docs/architecture/commercehub-emergency-operations-certification-findings.md`.

## 2026-06-01 POS-ADMIN-CERTIFICATION-01 PASS 15.5 Marketplace Audit, Compliance & Forensics

- CommerceHub forensic readiness is not production-certified. Finance evidence is strong and reconstructable for certified SQL/ledger flows, but complete merchant listing history, durable recovery-operation history, retention guarantees, unified incident search, and external immutable export remain incomplete.
- Runtime hardening added EF append-only guards for generic audit rows, CommerceHub financial audit rows, vendor-payment audit rows, accounting posting audits, store-config audits, core ledger entries, and marketplace dead-letter archives.
- Super Admin audit records now persist through an isolated SQL scope instead of the process-memory generic audit queue.
- Regression-freeze coverage was added in `MarketplaceAuditForensicsCertificationTests`; findings are recorded in `docs/architecture/marketplace-audit-compliance-forensics-certification-findings.md`.

## 2026-06-01 POS-ADMIN-CERTIFICATION-01 PASS 15.5B Forensic Evidence Hardening

- CommerceHub merchant listing and certified recovery-operation history now append durable SQL ledger rows with actor/system identity, tenant/listing scope, timestamps, correlation/request identifiers, reasons, outcomes, and details.
- Generic tenant audit changed from memory-first persistence to isolated SQL-first persistence with retry fallback and health visibility.
- Super Admin forensic APIs now expose unified incident search, bounded evidence export, retention inventory, and evidence-health diagnostics.
- Final production certification remains blocked on deployment controls: PostgreSQL evidence-table roles, external WORM/SIEM retention, outage-surviving SQL audit outbox fallback, alert rules, and broader non-CommerceHub recovery-ledger adoption.
- Regression-freeze coverage was added in `MarketplaceForensicEvidenceCertificationTests`; findings are recorded in `docs/architecture/marketplace-forensic-evidence-hardening-certification-findings.md`.

## 2026-06-01 POS-ADMIN-CERTIFICATION-01 PASS 15.6 Marketplace Production Readiness Certification

- Final public-launch verdict: **NOT READY**.
- Reviewed runtime authority remains SQL-first and no new runtime authority inversion was validated.
- Public launch remains blocked on PostgreSQL evidence-table roles, external WORM/SIEM retention and delivery, outage-surviving tenant-audit handoff, verified alert packs and dashboards, and deployment backup/restore/operator rehearsal evidence.
- Production risks, operational maturity debt, and future enhancements are explicitly classified in `docs/architecture/marketplace-production-readiness-certification-findings.md`.
- Regression-freeze coverage was added in `MarketplaceProductionReadinessCertificationTests`.

## 2026-06-01 PRODUCTION READINESS CLOSURE 1 Evidence Database Hardening

- PostgreSQL now enforces append-only behavior for certified audit, governance, finance, accounting, dead-letter, merchant-lifecycle, and recovery-operation evidence tables through rejection triggers.
- Deployment SQL provisions separate evidence owner, writer, reader, migration, and maintenance roles. Runtime application and worker identities may insert and read evidence but may not update, delete, truncate, alter, or drop it.
- Post-deployment verification reports ACL drift, trigger drift, inventory drift, and representative direct-SQL mutation rejection.
- Repository implementation closes PASS 15.6 Launch Blocker #1; each environment must still apply role provisioning and retain verification evidence before launch approval.

## 2026-06-01 PASS 16.1 Customer Identity Certification

- CommerceHub customer identity remains **NOT READY** for public production launch.
- Runtime hardening made registration transactional across customer and governance-profile creation, converges concurrent normalized-email registration to `EMAIL_EXISTS`, rejects suspended customers during login and JWT validation, and preserves guest cart contents until authenticated cart adoption completes.
- The active baseline supports registration, login, cookie-backed JWT access, profile read, logout, revoke-all backend authority, guest browsing, anonymous cart use, and guest-to-account cart adoption.
- Launch remains blocked on CommerceHub password recovery, email verification/resend, profile mutation, password and email change, revoke-all frontend UX, append-only SQL identity lifecycle evidence, and identity-specific telemetry, dashboards, and alerts.
- Regression-freeze coverage is in `CustomerIdentityCertificationTests`, `CustomerSessionLifecycleCertificationTests`, and `CustomerRecoveryCertificationTests`; findings are recorded in `docs/architecture/customer-identity-certification-findings.md`.

## 2026-06-01 PASS 16.1B Customer Identity Runtime Completion

- CommerceHub recovery, verification, profile mutation, password change, email change, customer-facing revoke-all UX, and append-only SQL identity evidence now have runtime implementations.
- Recovery and verification links use the existing `IEmailSender` boundary and configurable `CommerceHub:CustomerIdentity:PublicBaseUrl`. Raw tokens are hashed at rest and no longer returned through auth-shaped API responses.
- Reset and verification token consumption use conditional SQL updates so replay races collapse to one winner.
- Public-launch verdict remains **NOT READY** until migration deployment, SMTP delivery verification, evidence-role inventory updates, refresh-token rotation, identity telemetry dashboards and alerts, and PostgreSQL-backed chaos validation are complete.

## 2026-06-01 PASS 16.2 Product Discovery Certification

- CommerceHub product discovery was reviewed across homepage, featured/promoted shelves, search, autocomplete, categories, filters, product cards, product details, recommendation-like surfaces, pricing, inventory, deep links, frontend UX, auditability, chaos, and monitoring.
- Runtime discovery authority is SQL-first: CommerceHub search/detail/suggestion/autocomplete/list responses recompose returned listings through `CommerceHubSearchSqlAuthorityOverlay`; homepage sections are filtered by public SQL authority; category/filter metadata is live SQL lineage/schema bound; reindex deletes stale Elasticsearch documents absent from current SQL-visible authority.
- No independent personalized recommendation engine was found. Existing recommendation-like surfaces are homepage shelves, related-by-category products, featured fallback products, popular searches, autocomplete suggestions, and zero-result suggestions; these route through the same SQL-authoritative product/search/category filters.
- No runtime code hardening was required in this pass. Regression-freeze coverage was added in `CommerceHubProductDiscoveryCertificationTests`, `CommerceHubSearchVisibilityCertificationTests`, and `CommerceHubDiscoveryAuthorityCertificationTests`.
- Runtime discovery is production-certifiable, but public launch remains **NOT READY** until broader PASS 15.6/16.1B deployment, evidence, SMTP, monitoring, alerting, and chaos-validation blockers are closed. Findings are recorded in `docs/architecture/commercehub-product-discovery-certification-findings.md`.
## 2026-06-01 PASS 16.3 Cart Certification

- CommerceHub cart was reviewed across guest cart, authenticated cart, cart merge, add/remove/update/clear, pricing preview, inventory checks, session ownership, multi-tab/device behavior, frontend UX, checkout handoff, auditability, chaos, and monitoring.
- Runtime cart authority is SQL-first whenever cart state crosses the backend: `CommerceHubCartService` sanitizes cart lines against current listing/product/variant/tenant/stock/price authority, `CommerceHubCheckoutService` locks and version-checks the persisted cart before order creation, and checkout recomputes price, inventory, shipping, promo, vendor, readiness, and totals before persisting order snapshots.
- Guest cart state remains browser-local intent. It is safe but not durable across devices or storage loss; guest-to-customer adoption preserves the guest snapshot until backend sanitation/persistence succeeds.
- Runtime hardening performed: cart sanitization (`SanitizeAsync`) now enforces full product publication visibility (`IsOnlineVisible`, `PublishStatus == Published`) in addition to `IsActive`, achieving parity with `CalculateAsync` and checkout authority. Products that are active but offline or draft are now removed during sanitization with an `ITEM_REMOVED_UNAVAILABLE` notice.
- Regression-freeze coverage was added in `CommerceHubCartCertificationTests`, `CommerceHubCartPricingCertificationTests`, and `CommerceHubCartInventoryCertificationTests`.
- Cart runtime is production-certifiable, but public launch remains **NOT READY** until broader PASS 15.6/16.1B deployment, evidence, SMTP, monitoring, alerting, and chaos-validation blockers are closed. Findings are recorded in `docs/architecture/commercehub-cart-certification-findings.md`.

## 2026-06-01 PASS 16.4 Checkout Certification

- CommerceHub checkout was reviewed across checkout session, checkout pricing, inventory reservation, shipping, address handling, order creation, payment preparation, idempotency, concurrency, recovery, frontend UX, financial controls, auditability, chaos, and monitoring.
- Runtime checkout authority is SQL-first: authenticated customer authority, customer/idempotency locks, request-hash-bound idempotency, saved-cart row lock/version checks, serializable order creation, current SQL listing/product/variant/tenant/payment/shipping/promo/price/inventory recomputation, and backend inventory reservation all run before a marketplace order is committed.
- Order creation snapshots item price, shipping, payment method, commission, promo, vendor earning, customer address, order access, and sub-order financial evidence. Online payment is prepared only as a pending handoff after the order exists; provider execution and retry remain payment lifecycle/recovery authority.
- No runtime code hardening was required in this pass. Regression-freeze coverage was added in `CommerceHubCheckoutCertificationTests`, `CommerceHubCheckoutFinancialCertificationTests`, and `CommerceHubCheckoutReservationCertificationTests`.
- Checkout runtime is production-certifiable, but public launch remains **NOT READY** until broader PASS 15.6/16.1B deployment, evidence, SMTP, monitoring, alerting, and chaos-validation blockers are closed. Findings are recorded in `docs/architecture/commercehub-checkout-certification-findings.md`.

## 2026-06-01 PASS 16.5A Order & Fulfillment Lifecycle Certification

- CommerceHub order and fulfillment lifecycle was reviewed across order creation handoff, ownership, customer/merchant/Super Admin visibility, POS Admin fulfillment, status transitions, cancellation, delivery, returns, refund triggers, auditability, recovery, replay, projections, dashboards, frontend behavior, backend authority, chaos, and monitoring.
- Runtime order lifecycle authority is SQL-first. `CommerceHubMarketplaceOrder.CustomerId` owns customer visibility; `CommerceHubSubOrder.StoreId` owns merchant/POS fulfillment authority; order/sub-order status writes are guarded by `CommerceHubBoundaryMutationScope`; POS Admin fulfillment runs through `CommerceHubOrderManagementService` with tenant scope, serializable locks, transition validation, payment readiness, inventory release/commit, status history, and projection events.
- No CommerceHub Super Admin order-state override endpoint was found. Super Admin order views are SQL read/search/export surfaces; Super Admin return/refund operations are explicit, audited, and restricted by return/refund lifecycle policy.
- No runtime code hardening was required in this pass. Regression-freeze coverage was added in `CommerceHubOrderLifecycleCertificationTests`, `CommerceHubFulfillmentAuthorityCertificationTests`, and `CommerceHubOrderTransitionCertificationTests`.
- CommerceHub order lifecycle runtime is production-certifiable, but public launch remains **NOT READY** until broader PASS 15.6/16.1B deployment, evidence, SMTP/session, monitoring, alerting, backup/restore, and chaos-validation blockers are closed. Findings are recorded in `docs/architecture/commercehub-order-fulfillment-lifecycle-certification-findings.md`.

## 2026-06-02 PASS 16.5B Settlement & Merchant Balance Certification

- CommerceHub settlement and merchant-balance runtime is production-certifiable for inspected paths under PostgreSQL ledger authority. Merchant balances, COD commission debt, online-payment payables, settlements, vendor payment requests, payouts, refunds, dashboards, and reconciliation derive from SQL financial transactions, SQL snapshots, and append-only evidence rather than MongoDB, Redis, Elasticsearch, or frontend state.
- Runtime hardening changed COD commission-debt threshold enforcement: `amountOwed >= 1000 EGP` now immediately suspends new CommerceHub orders. The configured grace period remains operator metadata and no longer permits order acceptance above the hard limit.
- Regression-freeze coverage was added in `CommerceHubSettlementCertificationTests`, `CommerceHubMerchantBalanceCertificationTests`, and `CommerceHubFinancialReconciliationCertificationTests`; existing vendor financial-control coverage was updated for immediate threshold suspension.
- Public launch remains **NOT READY** until broader PASS 15.6/16.1B deployment, evidence retention, alert packs, dashboards, backup/restore, SMTP/session, and PostgreSQL-backed chaos blockers are closed. Findings are recorded in `docs/architecture/commercehub-settlement-merchant-balance-certification-findings.md`.

## 2026-06-02 PASS 16.5C COD Risk & Debt Control Certification

- CommerceHub COD risk-control runtime is production-certifiable for inspected paths. Vendor COD commission debt is recomposed from SQL financial transactions, settlements, approved repayments, adjustments, eligible refund impacts, and append-only evidence rather than frontend state or derivative stores.
- Runtime hardening aligned threshold-crossing evidence with hard-limit enforcement: exact-threshold debt now records `ThresholdCrossedAtUtc` when effective debt reaches `amountOwed >= threshold`, matching immediate new-order suspension and visibility suppression.
- Checkout rejects financially suspended vendors through `CommerceHubCheckoutService`; discovery/search/autocomplete/homepage/product-detail/recommendation-like surfaces suppress unsafe vendors through `CommerceHubPublicVendorVisibilityService` and `CommerceHubQueryService`; existing orders continue under PASS 16.5A lifecycle authority.
- Regression-freeze coverage was added in `CommerceHubCodRiskCertificationTests`, `CommerceHubVendorDebtCertificationTests`, and `CommerceHubVendorSuspensionCertificationTests`.
- Public launch remains **NOT READY** until broader PASS 15.6/16.1B deployment, evidence retention, alert packs, dashboards, backup/restore, SMTP/session, and PostgreSQL-backed COD chaos blockers are closed. Findings are recorded in `docs/architecture/commercehub-cod-risk-control-certification-findings.md`.

## 2026-06-02 PASS 16.5D Payout & Reconciliation Certification

- CommerceHub payout ownership and reconciliation runtime is production-certifiable for inspected paths. Merchant payout identities are vendor-scoped SQL rows; Super Admin payout execution revalidates vendor/method binding, platform actor authority, eligible ledger-backed payable balance, platform cash, external evidence, and idempotency before recording completed payout truth.
- No new runtime code hardening was required in this pass. Existing runtime controls include serializable payout transactions, earning row locks, ledger confirmation, one processing/reconciliation slot per vendor, external transfer handoff tokens, globally unique external evidence, completion fingerprints, refund/payout overlap blocking, reconciliation quarantine, and payout external-truth readiness checks.
- Regression-freeze coverage was added in `CommerceHubPayoutCertificationTests` and `CommerceHubPayoutIdentityCertificationTests`; `CommerceHubFinancialReconciliationCertificationTests` now freezes payout evidence and ledger-debit reconciliation.
- Public launch remains **NOT READY** until broader PASS 15.6/16.1B deployment, retention, alert-pack, dashboard, backup/restore, SMTP/session, and PostgreSQL-backed payout/refund/settlement chaos blockers are closed. Findings are recorded in `docs/architecture/commercehub-payout-reconciliation-certification-findings.md`.

## 2026-06-02 PASS 16.5E Refund & Financial Reversal Certification

- CommerceHub refund and financial-reversal runtime is production-certifiable for inspected paths. Customer refund requests, Super Admin review/execution, POS Admin tenant-owned return receipt, refund rows, return rows, execution evidence, financial transactions, ledgers, payout interaction, settlement interaction, recovery, and reconciliation remain PostgreSQL/ledger-authoritative.
- No runtime code hardening was required. Existing controls include customer ownership checks, Super Admin actor/evidence checks, POS tenant return receipt checks, serializable locks, return/refund transition policies, refund idempotency, outward-money readiness gates, payout-overlap blocking, cumulative refund overrun validation, financial state validation, ledger fingerprint idempotency, and reconciliation-required states.
- Regression-freeze coverage was added in `CommerceHubRefundCertificationTests`, `CommerceHubFinancialReversalCertificationTests`, and `CommerceHubReturnAuthorityCertificationTests`.
- Public launch remains **NOT READY** until broader PASS 15.6/16.1B deployment, retention, alert-pack, dashboard, backup/restore, SMTP/session, and PostgreSQL-backed refund/payout/settlement chaos blockers are closed. Findings are recorded in `docs/architecture/commercehub-refund-financial-reversal-certification-findings.md`.

## 2026-06-02 PASS 16.5F-A CommerceHub Currency Authority Certification

- CommerceHub currency authority is EGP only. POS Admin and storefront tenants may remain multi-currency through tenant currency authority, but CommerceHub listing, pricing, cart, checkout, order, settlement, payout, refund, and ledger runtime paths must not derive currency from `Tenant.Currency`.
- Runtime hardening changed cart pricing/sanitation, checkout order creation, CommerceHub Mongo projection, Elasticsearch SQL overlay, tenant-currency replay, vendor repayment defaults, vendor financial-control defaults, and payout queue fallback to use `FinancialCurrencyGuard.PlatformBaseCurrencyCode`.
- Regression-freeze coverage was added in `CommerceHubCurrencyAuthorityCertificationTests`, `CommerceHubPublicationCurrencyCertificationTests`, and `CommerceHubPriceOverrideCurrencyCertificationTests`.
- Remaining debt: DB-level EGP check constraints, SQL/Mongo/Elasticsearch currency parity scanner, and PostgreSQL-backed mixed-currency chaos coverage. Findings are recorded in `docs/architecture/commercehub-currency-authority-certification-findings.md`.

## 2026-06-02 PASS 16.5F-B CommerceHub Publication Currency Policy Enforcement

- CommerceHub publication is now fail-closed for non-EGP product price authority. POS Admin and storefront remain multi-currency, but CommerceHub publish, republish, bulk publish, bulk listing update, and listing update paths reject products whose current catalog price currency is not EGP.
- Current catalog price currency authority is `Tenant.Currency`; no separate `Product.Currency` or `ProductVariant.Currency` field exists in the inspected catalog model.
- Bulk visibility-increasing/listing-update operations validate per row and preserve unpublish for legacy/stale listing withdrawal. Replay, recovery, auto-healing, catch-up reindex, tenant reindex, and global reindex converge through `CommerceHubSearchSqlAuthorityOverlay`; reindex expected visibility now excludes non-EGP listings.
- Frontend publication UI now shows the actionable rule message and disables publish/update for non-EGP rows. Regression-freeze coverage was added in `CommerceHubPublicationCurrencyPolicyCertificationTests`, `CommerceHubPublicationCurrencyValidationTests`, and `CommerceHubBulkPublicationCurrencyTests`. Findings are recorded in `docs/architecture/commercehub-publication-currency-policy-certification-findings.md`.

## 2026-06-02 PASS 16.1B-A Authentication & Session Authority Certification

- CommerceHub customer authentication authority is runtime production-certifiable for inspected paths: registration creates no authenticated access, verification creates no session, unverified customers cannot login, and customer JWT sessions are created only by login.
- Runtime hardening made `GET /api/customers/auth/me` explicitly require the `CustomerJwt` scheme and `Customer` role instead of relying on manual claim checks after anonymous endpoint execution.
- Logout revokes the current JTI and clears browser cookie authority; revoke-all, password reset, password change, and email change advance durable `CommerceHubCustomers.TokensValidAfterUtc` cutoff.
- Regression-freeze coverage was added in `CommerceHubAuthenticationAuthorityCertificationTests`, `CommerceHubEmailVerificationCertificationTests`, and `CommerceHubSessionCreationCertificationTests`. Findings are recorded in `docs/architecture/commercehub-authentication-authority-certification-findings.md`.

## 2026-09-06 Gate 3R CommerceHub Payment Configuration Authority

- CommerceHub payment configuration is platform-owned. Vendor IDs establish the participating stores, but vendor Storefront `TenantPaymentMethods` do not authorize CommerceHub providers.
- COD is CommerceHub platform policy. Paymob and Stripe availability comes from registered `IPaymentProviderAdapter` executable capabilities; session creation and webhook verification are mandatory, and Paymob also requires callback parsing.
- `CommerceHubCartService`, `CommerceHubCheckoutService`, and `CommerceHubPaymentService` no longer read `TenantPaymentMethods`. Provider-unavailable initiation returns `PAYMENT_PROVIDER_NOT_CONFIGURED` before reservation checks, locks, payment rows, inventory mutation, or adapter calls.
- Gate evidence passed: focused tests 26/26, full CommerceHub tests 799/799, API build with 0 errors, POS Web and CommerceHub production builds, `/health/live` HTTP 200, and `/health/marketplace` HTTP 200.
- `ORD-CH-0017` remained one pending EGP 2,150 order with no provider transaction, earnings, refunds, payouts, financial audit rows, or financial transactions. External provider execution remains uncertified until platform sandbox configuration exists.
