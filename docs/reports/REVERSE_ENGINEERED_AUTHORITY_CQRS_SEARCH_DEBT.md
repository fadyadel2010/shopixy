# Merchant Platform — Cross-Domain Authority, Outbox/CQRS/Projection, Search Infrastructure & Technical Debt

> Implementation-backed reverse engineering. Every claim is cited `path:line`.
> Scope: runtime architecture only (no UI/React). Repo: `C:/Users/fadya/source/repos/PointOfSale`.
> Single authoritative write model = **PostgreSQL** (`ApplicationDbContext`, Npgsql). Derivative projections = **MongoDB** (read models), **Elasticsearch** (search index), **Redis** (caches). Events via **MassTransit + RabbitMQ**. Multi-tenancy via `TenantId`.

---

## 1. OUTBOX MODEL

### 1.1 The eight outbox entities

| Outbox entity | File | Purpose | Written by | Processed by | Fail-closed behavior |
|---|---|---|---|---|---|
| `OutboxMessage` | `PointOfSale.Core/Entities/OutboxMessage.cs:19` | Generic integration-events backbone (abandoned-cart hardening). | Domain commands + `OutboxPublisherJob` enqueue | **`OutboxPublisherJob`** (Hangfire, every 5s) → `IPublishEndpoint` to RabbitMQ | Critical events mark `RequiresOperatorReconciliation` (poison) but keep slow-replaying; non-critical terminal-fail marked processed |
| `AccountingOutboxMessage` | `PointOfSale.Core/Entities/Accounting/AccountingOutboxMessage.cs:22` | Financial postings (sales/purchases/refunds/settlements/ledger) | `IAccountingOutboxService` (staged in same SQL tx as business op) | **`AccountingOutboxWorker`** (`BackgroundService`) → `AccountingOutboxProcessor` → `IFinancialPostingService` | `IsFailed` terminal after retries; `RecordNonTerminalFinancialFailure` keeps money events alive forever (non-terminal) |
| `MarketplaceEventOutboxMessage` | `PointOfSale.Core/Entities/MarketplaceEventOutboxMessage.cs:9` | Marketplace backbone events (orders/payments/listings) | Marketplace domain + `MarketplaceEventOutboxService` | **`MarketplaceEventOutboxWorker`** → `MarketplaceEventOutboxProcessor` → RabbitMQ | `RecordNonTerminalProjectionFailure` keeps critical marketplace events alive; dead-letters via `MarketplaceEventOutboxDeadLetterArchive` |
| `MarketplaceProjectionOutboxMessage` | `PointOfSale.Core/Entities/MarketplaceProjectionOutboxMessage.cs:13` | Durable post-commit handoff for Mongo/secondary read-model writes | `MarketplaceIdempotentConsumer<TEvent>.AddProjectionOutboxMessage` (`Consumers/Marketplace/MarketplaceIdempotentConsumer.cs:186`) | **`MarketplaceProjectionOutboxWorker`** → `MarketplaceProjectionOutboxProcessor` | Terminal `IsFailed` after retries; resets for retry |
| `StockSyncOutboxMessage` | `PointOfSale.Core/Entities/StockSyncOutboxMessage.cs:10` | Warehouse-scoped stock sync events (variant inventory) to downstream | `ProductVariantStockChangedEvent` | **`StockSyncOutboxWorker`** → `StockSyncOutboxProcessor` | `RecordNonTerminalProjectionFailure` keeps alive; **suppression** if SQL inventory lineage lost (`ResolveStockSyncSuppressionReasonAsync`) |
| `PaymentCancellationOutboxMessage` | `PointOfSale.Core/Entities/PaymentCancellationOutboxMessage.cs:22` | Durable payment-cancellation ops (order cancellation) | Order cancellation command | **`PaymentCancellationOutboxWorker`** → `PaymentCancellationOutboxProcessor` → `IPaymentLifecycleService.HandlePaymentResultAsync` | Terminal `IsFailed`; lease (`ClaimedUntilUtc`) collapses worker overlap |
| `CommerceHubPaymentCompensationOutboxMessage` | `PointOfSale.Core/Entities/CommerceHubPaymentCompensationOutboxMessage.cs:10` | Compensation commands for provider payments captured but rejected by CommerceHub lifecycle | CommerceHub capture-path | **`CommerceHubPaymentCompensationOutboxWorker`** (API) | Routes corrupt/uncertain gateway outcomes to `RequiresOperatorReconciliation` |
| `MarketplaceEventOutboxDeadLetterArchive` | `PointOfSale.Core/Entities/MarketplaceEventOutboxDeadLetterArchive.cs:7` | Audit archive of failed marketplace outbox rows; recoverable while live row blocks readiness | `MarketplaceEventOutboxProcessor` on terminal failure | n/a (archive only) | — |

### 1.2 The publisher / workers (Infrastructure)

- **Generic publisher:** `PointOfSale.Infrastructure/Jobs/OutboxPublisherJob.cs:26` — Hangfire recurring, `BatchSize=100`, `RetentionDays=7` (`OutboxPublisherJob.cs:31-32`). Uses pessimistic locking: `FROM "OutboxMessages" FOR UPDATE` on Npgsql (`OutboxPublisherJob.cs:491-498`) and `UPDLOCK, ROWLOCK, HOLDLOCK` on SqlServer (`OutboxPublisherJob.cs:504-508`). Critically: it explicitly `SELECT *, xmin` because "OutboxMessage uses xmin as the concurrency token, so omitting it causes every post-publish update to fail closed as a false DbUpdateConcurrencyException" (`OutboxPublisherJob.cs:492-494`).
- **Critical vs non-critical split:** `IntegrationEventAuthority` (`PointOfSale.Core/Events/IntegrationEventAuthority.cs:9-37`) defines `CriticalEventNameFragments` (Order, Payment, Inventory, Stock, Product, CommerceHub, Marketplace, Ledger, Accounting, Wallet, etc.). `OutboxPublisherJob.IsCriticalOutboxEvent` (`OutboxPublisherJob.cs:515-517`) gates ordering + poison handling.
- **Workers registered:** `MarketplaceEventingExtensions.cs:77-80` (`MarketplaceEventOutboxWorker`, `MarketplaceProjectionOutboxWorker`, `MarketplaceEventBackboneCleanupWorker`, `MarketplaceReadModelIndexInitializer`); `AccountingServiceExtensions.cs:69` (`AccountingOutboxWorker`); `StockSyncOutboxExtensions.cs:36`; `PaymentCancellationOutboxExtensions.cs:50`; `Program.cs:503` (`CommerceHubPaymentCompensationOutboxWorker`).

### 1.3 Critical poison / operator reconciliation (fail-closed)

- `MarkExhaustedCriticalMessagesForOperatorReconciliationAsync` (`OutboxPublisherJob.cs:146-181`) flags exhausted critical messages with `RequiresOperatorReconciliation=true` rather than dropping them.
- `ProcessMessageAsync` (`OutboxPublisherJob.cs:219-236`) logs `LogCritical` "CRITICAL POISON MESSAGE" and **keeps slow replaying** (`CriticalPoisonSlowReplayInterval = 15 min`, `OutboxPublisherJob.cs:33`) so a downstream outage cannot leave SQL committed while storefront/marketplace/accounting projections go stale forever.
- Non-critical exhausted messages are marked `ProcessedAtUtc` so they do not block newer messages (`OutboxPublisherJob.cs:238-274`).

---

## 2. CQRS: WRITE vs READ AUTHORITY

### 2.1 Authority boundaries (implementation)

- **Write authority = SQL only.** "Direct PaymentStatus changes are BLOCKED" — `IPaymentLifecycleService` (`PointOfSale.Core/Interfaces/Payments/IPaymentLifecycleService.cs:11-15`). Variants: "PostgreSQL is write-only for variants" (`Consumers/ProductVariantProjectionConsumer.cs:32`). Products: Mongo is "READ-ONLY source for Storefront" (`ProductVariantProjectionConsumer.cs:31`).
- **Read authority = Mongo/ES/Redis** are derivative; every read path re-overlays SQL truth at query time (see 2.4).
- **Repair-on-read:** `ProductSearchSqlAuthorityOverlay` (`Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs:14`) and `CommerceHubSearchSqlAuthorityOverlay` re-derive visibility/pricing/stock from SQL into the projection before presentation. The generic guard `SqlTruthRebuildGuard.RequireSqlAuthority` (`Reliability/SqlTruthRebuildGuard.cs:27-73`) *forces* an explicit SQL-authority kind for every rebuild entrypoint; missing kind throws `SQL_TRUTH_REBUILD_AUTHORITY_KIND_REQUIRED`.

### 2.2 EventVersion monotonic ordering (stale-event prevention)

- `OutboxMessage.EventVersion` (`OutboxMessage.cs:56`) with doc: "Monotonic aggregate version for critical integration events. Used by the outbox publisher to prevent stale events from overtaking newer state across POS, Storefront, Marketplace, Affiliate, and Accounting."
- `OutboxPublisherJob.HasUnprocessedEarlierCriticalAggregateMessageAsync` (`OutboxPublisherJob.cs:601-642`) defers a critical message until all earlier-version critical messages for the same aggregate+tenant scope publish first. `IsEarlierCriticalAggregateMessage` (`OutboxPublisherJob.cs:644-659`): Version is the primary ordering key, time is a tie-breaker.
- `AccountingOutboxProcessor` uses explicit `MessageOrderingComparisonExpression` (`AccountingOutboxProcessor.cs:78-107`) ordering by `AggregateVersion` then `OccurredAtUtc`/`CreatedAt`/`Id`; `HasUnprocessedEarlierAggregateVersionAsync` (`AccountingOutboxProcessor.cs:335-341`) defers.
- Marketplace: `MarketplaceEventOutboxProcessor.HasUnpublishedEarlierEntityEventAsync` (`MarketplaceEventOutboxProcessor.cs:404-436`) — "S56: Version is the source-mutation ordering key. Time is only a tie-breaker… downstream systems must not observe newer aggregate events while an older event in the same stream is unresolved."
- Stock: `StockSyncOutboxProcessor.HasUnpublishedEarlierVariantEventAsync` (`StockSyncOutboxProcessor.cs:479-501`) canonical `EventVersion → StockUpdatedAtUtc → OccurredAtUtc → CreatedAt → Id` ordering.
- **Stale-event rejection at the ES write side too:** `ProductSearchIndexService.IndexAsync` refuses to overwrite a newer doc (`ProductSearchIndexService.cs:54-67`): "This prevents out-of-order events from overwriting fresher data."
- `StockSyncOutboxProcessor.ResolveStockSyncSuppressionReasonAsync` (`StockSyncOutboxProcessor.cs:421-477`) actively **suppresses** replay when SQL inventory lineage is lost or `ProjectionMutationVersion > message.EventVersion` ("StaleStockMutationVersion").

### 2.3 ProjectionEventFence (idempotent at-least-once consumers)

- `ProjectionEventFence` (`Reliability/ProjectionEventFence.cs:18`) is a SQL-backed fence that **collapses already-completed EventId deliveries** and **active overlapping deliveries** before side effects run. Uses `pg_try_advisory_lock` keyed by `{EventId}:{ConsumerName}:{EventType}` SHA-256 (`ProjectionEventFence.cs:127-131, 276-281`). `TryAcquireAsync` returns null (skip) if already completed (`IsAlreadyCompletedAsync`, `ProjectionEventFence.cs:260-274`) or if advisory lock not acquired (`ProjectionEventFence.cs:132-147`). `CompleteAsync` inserts a `ProcessedEvent` row idempotently (`ProjectionEventFence.cs:187-229`).
- All critical projection consumers claim the fence (e.g. `ProductSearchIndexConsumer.cs:99-122`); architecture tests assert the fence is present in every projection consumer (`CheckoutConcurrencyHardeningSourceTests.cs:778-812`, `CommerceHubEventReadModelArchitectureCertificationTests.cs:50-51`).

### 2.4 Repair-on-read concretely

- `ProductSearchSqlAuthorityOverlay.ApplyCoreAsync` (`ProductSearchSqlAuthorityOverlay.cs:85-344`) re-reads `Products`, `Variants`, `Stocks`, `ProductUnits`, `Promotions`, live `Categories` from SQL and overwrites the Mongo `ProductDocument` fields. Returns null (→ delete from ES) when SQL product missing/deleted or inactive/not published (`ProductSearchSqlAuthorityOverlay.cs:129-132, 170-172, 285-289`).
- `CommerceHubProjectionReconciliationService` (`PointOfSale.API/Services/CommerceHub/CommerceHubProjectionReconciliationService.cs:14`) is a `BackgroundService` that rebuilds CommerceHub order projections from SQL order/suborder/item authority (registered `Program.cs:500`). Same pattern: `StorefrontOrderProjectionReconciliationService` (`Program.cs:501`).

---

## 3. CROSS-DOMAIN AUTHORITY GRAPH

### 3.1 Merchant Platform → CommerceHub
- **Owns:** marketplace order lifecycle, sub-orders, items, vendor earnings, CommerceHub payments, refunds.
- **Reads from SQL:** `CommerceHubPaymentCompensationOutboxWorker` loads `CommerceHubPayments` + `Order.SubOrders.Items` (`CommerceHubPaymentCompensationOutboxWorker.cs:130-137`) to execute provider-compensation refunds.
- **Emits:** `CommerceHubLedgerPosting` accounting outbox (`AccountingOutboxMessage.ResolveDefaultSourceType` maps it → `CommerceHubFinancialTransaction`, `AccountingOutboxMessage.cs:158-161`); marketplace events via `MarketplaceEventOutboxMessage`.
- **Compensation / fail-closed:** `CommerceHubPaymentCompensationOutboxWorker.cs:147-156` blocks automatic retry when a refund request was durably started but no gateway reference recorded ("prevent a duplicate external refund"). Corrupt allocation snapshots route to operator reconciliation (`CommerceHubPaymentCompensationOutboxWorker.cs:290-315`).
- **Reconciliation workers:** `CommerceHubProjectionReconciliationService`, `CommerceHubUnresolvedPaymentReconciliationHostedService`, `CommerceHubPayoutReconciliationHostedService`, `CommerceHubRefundRecoveryHostedService`, `CommerceHubDailyFinancialAggregateRefreshService`, `CommerceHubAutoHealingService` (all `Program.cs:490-503`).

### 3.2 Merchant Platform → Payments (`IPaymentLifecycleService`)
- **Owns:** all `PaymentStatus` transitions. "This is the ONLY way to change PaymentStatus" (`IPaymentLifecycleService.cs:11-15`). Outcomes: Authorized/Succeeded/Failed/Cancelled/Rejected; `AmountMismatch` is SYSTEM-ONLY (`IPaymentLifecycleService.cs:34-36`).
- **Integration point:** `PaymentCancellationOutboxProcessor` calls `IPaymentLifecycleService.HandlePaymentResultAsync` (`PaymentCancellationOutboxProcessor.cs:225`). COD, gateway settlement, wallet flows all converge through `HandlePaymentResultAsync` (`PaymentLifecycleService.cs:99,276,531,801,818,1091`).
- **Defers to provider:** actual capture/refund happen in provider adapters (Paymob, Fawry); lifecycle service is the canonical state machine + journal emitter.
- **Runbook rule:** "PostgreSQL advisory locking in `PaymentCancellationOutboxProcessor` … must stay inside the claim transaction … do not use direct SQL to mutate order/payment truth" (`docs/operations/operational-recovery-runbooks.md:212`).

### 3.3 Merchant Platform → Treasury / Accounting
- **Owns:** ledger/journal postings are derivative of Merchant Platform business events.
- **Emits:** `AccountingOutboxMessage` for `PosSale`, `PosPurchase`, `PosRefund`, `OnlinePayment`, `CodDelivered`, `GatewaySettlement`, `CommerceHubLedgerPosting`, etc. (`AccountingOutboxProcessor.cs:50-73` `_criticalFinancialTruthEventTypes`).
- **Fail-closed for money:** `RecordNonTerminalFinancialFailure` (`AccountingOutboxMessage.cs:252-261`) — financial truth events are **never** marked `IsFailed` terminal while business state is committed; they retry with capped exponential backoff forever.
- **Owned-by-Treasury deferred:** `ITreasuryReadService` is the read side; `TreasuryOperationService` is the write side. Merchant Platform emits, Treasury consumes/projection-reads.

### 3.4 Merchant Platform → Shipping / Fulfillment
- **Fulfillment authority:** `ShipmentAuthorityRecoveryHostedService` (`Program.cs:698`) repairs shipment authority from SQL. `CarrierShipmentService` (`PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs`) integrates carriers; `TrackingSyncJob` syncs tracking.
- **COD:** `MarkCodDeliveredAsync` / `RecordCodSettlementAsync` live in `IPaymentLifecycleService` (`IPaymentLifecycleService.cs:66-80`) — shipping delivery is the trigger that moves payment Collected→Settled and emits journals. Standard COD completion is lifecycle-owned; CommerceHub uses a *different* writer (`docs/architecture/COD_ARCHITECTURE_VALIDATION.md:43`).

### 3.5 Merchant Platform → Identity
- **Canonical identity:** `CanonicalIdentityReconciliationHostedService` (`Program.cs:499`, `PointOfSale.Infrastructure/Identity/CanonicalIdentityReconciliationService.cs:618`) reconciles identity.
- **Defers:** JWT issuance/validation is identity-owned; Merchant Platform only enforces `IsActive` defense-in-depth (see technical debt 6.2).

### 3.6 Merchant Platform → Affiliate
- **Owns:** affiliate links, attribution, settlement orchestration (`IAffiliateSettlementOrchestrator`, `AffiliatePayoutService`).
- **Emits:** affiliate events; affiliate policy/intelligence projections (`Consumers/Affiliate/AffiliateProductPolicyProjectionConsumer.cs`, `AffiliateProductIntelligenceProjectionConsumer.cs`).
- **Reads in search:** `ProductSearchService.HydrateFromMongoDbAsync` filters `IsAffiliateEnabled` when affiliate-mode active (`ProductSearchService.cs:160-194`, AF-12).
- **Settlement reconciliation:** `AffiliateAttributionReconciliationHostedService` (`BackgroundService`).

### 3.7 Merchant Platform → Notifications
- Outbox emits notification-style events (`WhatsAppOrderConfirmationRequestedEvent`, `WhatsAppPaymentApprovalRequestedEvent`, etc. are explicitly **non-authoritative** in `IntegrationEventAuthority.NonAuthoritativeEventTypeNames` `IntegrationEventAuthority.cs:39-52` — they never gate critical ordering).
- `WhatsAppMessageDispatcher` (`PointOfSale.Infrastructure/WhatsApp/Services/WhatsAppMessageDispatcher.cs:129`) is notification-only; it can SKIP/FAILED but never mutates business truth.

### 3.8 Merchant Platform → SuperAdmin
- **Owns:** platform-level recovery/seed. `SuperAdminHomepageSectionManagementService`, `SuperAdminMarketplaceOperationsController`, `SuperAdminRootSeeder`, `MarketplaceCmsPageSeeder` (`Program.cs:926-927`).
- **Search control is platform-scoped:** tenant admins cannot delete/recreate the global ES index — `SearchReindexController.DeleteIndex`/`RecreateIndex` return `403 GLOBAL_SEARCH_CONTROL_FORBIDDEN` (`SearchReindexController.cs:62-97`). An anonymous `/reindex-dev` endpoint was **permanently removed** for security (`SearchReindexController.cs:99-113`).

---

## 4. SEARCH INFRASTRUCTURE (Elasticsearch)

### 4.1 Client & index definitions
- **Client:** NEST `IElasticClient` (`ProductSearchIndexService.cs:21`). Settings in `ElasticsearchSettings` (`_settings.Enabled`, `ProductsIndexName`, `NumberOfShards/Replicas`).
- **Products index:** created in `ProductSearchIndexService.EnsureIndexExistsAsync` (`ProductSearchIndexService.cs:162-307`) with multilingual analyzers (multilingual_analyzer, arabic_custom, autocomplete_analyzer; ICU/ascii-folding not required — built-in). `ProductSearchDocument` mapping, `Dynamic(DynamicMapping.Strict)` (`ProductSearchIndexService.cs:285`). Index name referenced as `_settings.ProductsIndexName` (`ProductSearchIndexService.cs:74,199,409,...`).
- **Marketplace services index:** `marketplace_services` (`ElasticsearchDriftReconciliationJob.cs:33`), doc `MarketplaceServiceDocument`. A second index for CommerceHub search: `CommerceHubSearchIndexService` (`PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchIndexService.cs`).

### 4.2 How indexing happens (event-driven)
- **Primary path:** MassTransit consumers → Mongo product projection → ES index. `ProductSearchIndexConsumer` (`Consumers/ProductSearchIndexConsumer.cs:35`) consumes `ProductCreatedEvent`, acquires `ProjectionEventFence`, hydrates Mongo `ProductDocument`, overlays SQL authority via `ProductSearchSqlAuthorityOverlay.ApplyAsync` (`ProductSearchIndexConsumer.cs:140`), then `IProductSearchIndexService.IndexAsync`.
- **Sibling consumers:** `ProductUpdatedSearchIndexConsumer`, `ProductStockPriceSearchIndexConsumer`, `ProductPromotionSearchIndexConsumer`, `ProductMediaChangedEventConsumer`, `ProductDeletedSearchIndexConsumer` (`Consumers/` dir). All tenant-isolated by `TenantId` and idempotent (`DocumentId = {TenantId}_{ProductId}`, `ProductSearchIndexConsumer.cs:33`).
- **Mongo is the ES feeder:** `ProductVariantProjectionConsumer` writes Mongo `products` collection (`ProductVariantProjectionConsumer.cs:70`); the ES consumers read Mongo then overlay SQL. Mongo = canonical read-model; ES = search-only index.

### 4.3 Reindex endpoints
- `SearchReindexController.ReindexProducts` (`SearchReindexController.cs:119-234`): reads SQL `Products` for tenant, overlays SQL authority, deletes-from-ES anything not `IsActive && IsOnlineVisible`, indexes the rest. `ClearCache` invalidates Redis (`SearchReindexController.cs:239-278`). `GetStatus` counts Mongo docs (`SearchReindexController.cs:283-326`).
- **Drift reconciliation job:** `ElasticsearchDriftReconciliationJob` (`Jobs/ElasticsearchDriftReconciliationJob.cs:27`) compares SQL service truth vs ES `marketplace_services`, bulk-indexes missing + bulk-deletes orphans ("Goal: Zero drift between RDBMS and Search Index", `ElasticsearchDriftReconciliationJob.cs:14-26`).

### 4.4 Visibility / publication logic (SQL-first)
- **Publication = SQL authority overlay.** A product is searchable only if `IsActive && IsOnlineVisible && PublishStatus == Published` (`ProductSearchSqlAuthorityOverlay.cs:170-172`). ES search query itself also filters `IsActive=true AND IsOnlineVisible=true` (`ProductSearchIndexService.cs:347-349`).
- **Fail-closed search:** `ProductSearchService.SearchProductsAsync` throws `StorefrontSearchUnavailableException` if ES fails (no silent empty results) (`ProductSearchService.cs:82-94, 136-140`).
- **Promotion-aware ranking** via `function_score` (additive boosts, `ScoreMode=Sum`, `BoostMode=Sum`) (`ProductSearchIndexService.cs:400-492`).

### 4.5 Redis role
- Caches store config / promotion-ranking config (`IStoreConfigCacheService`, invalidated on reindex `SearchReindexController.cs:251`). Product cache invalidated by `ProductVariantProjectionConsumer` after every variant change (`ProductVariantProjectionConsumer.cs:40-42`).

---

## 5. TECHNICAL DEBT (implementation-backed)

### 5.1 Duplicate authority / dual ownership
- **AffiliateAccountId "dual-write bridge"** — `AffiliatePromotionLinksController.cs:104` comment literally says *"Resolve AffiliateAccountId for dual-write bridge"*; it copies `AffiliateAccountId` from `AffiliateUsers` onto the link row. The term "dual-write bridge" signals redundant ownership of the affiliate→account mapping across two tables.
- **Two parallel event fences:** `ProjectionEventFence` (`Reliability/ProjectionEventFence.cs`) for generic consumers and `ProjectionConsumerEventFence` for CommerceHub/Affiliate (architecture tests reference both, `CheckoutConcurrencyHardeningSource`). Two fence mechanisms for the same logical purpose = hidden coupling / drift risk.
- **Two SQL-authority overlays:** `ProductSearchSqlAuthorityOverlay` (search) and `CommerceHubSearchSqlAuthorityOverlay` — overlapping re-derivation logic that can diverge.

### 5.2 Projection drift / hidden coupling
- `ProductVariantProjectionConsumer` is 1744 lines (`ProductVariantProjectionConsumer.cs` total) — a single consumer owning created/updated/deleted/stock + cache-invalidation + ES index; mixed responsibilities.
- Mongo `ProductDocument` variants carry `ProjectionMutationVersion` and `LastStockEventVersion`; the stock sync processor must constantly re-check `ProjectionMutationVersion > message.EventVersion` (`StockSyncOutboxProcessor.cs:460-465`) because Mongo and SQL versions drift.
- `ElasticsearchDriftReconciliationJob` exists *because* ES↔SQL drift is a known recurring condition (`ElasticsearchDriftReconciliationJob.cs:14-26`).

### 5.3 Legacy / TODO / deferred work (real markers)
- **JWT deactivation gap (recurring TODO, 5+ sites):** `OnlineCustomersAdminController.cs:31,371,430` — "TODO (Phase 10.3.2): Deactivation must also revoke/invalidate JWTs"; `StorefrontOnlineCustomerController.cs:22-23,116` — "TODO (Phase 10.3.2): JWT middleware must check IsActive and reject tokens for deactivated customers". **Meaning: deactivating an online customer does NOT currently revoke active JWTs — only a defense-in-depth IsActive check exists.** This is a live security gap, not just debt.
- **`legacyEventType` dual matching:** `MarketplaceIdempotentConsumer` matches both `eventType` (full name) and `legacyEventType` (class name) for dedupe (`MarketplaceIdempotentConsumer.cs:44,51,59,117`) — legacy event-naming compatibility carried forward.
- **ManualWallet cleanup:** `ManualWalletProofCleanupJob.cs` has 11 debt markers — heavy manual-cleanup scaffolding around wallet proof lifecycle.

### 5.4 Manual synchronization / ad-hoc SQL risk
- The runbook itself warns: "do not use direct SQL to mark payment-cancellation rows processed or to mutate order/payment truth" (`docs/operations/operational-recovery-runbooks.md:212`) — implying prior incidents of ad-hoc SQL bypassing the outbox/lifecycle.
- Recovery scripts in repo root (`af16e-reindex-products.ps1`, `af17a-reindex-trigger.sql`, `af16e-trigger-reindex-via-events.sql`, `fix-elasticsearch-*.ps1`) indicate **manual ES/Mongo reindex is a routine operational crutch**, not a self-healing system.

### 5.5 Runtime ambiguity / partial migration
- **Two relational providers supported:** Npgsql/PostgreSQL AND SqlServer (`OutboxPublisherJob.cs:489-509`, `AccountingOutboxProcessor.cs:223-224`). Dual-provider SQL dialect paths (`FOR UPDATE` vs `UPDLOCK`) are maintained throughout — a partial migration surface that doubles query-correctness risk.
- **Hybrid fence unlock semantics:** `ProjectionEventFence` opens a raw connection and takes `pg_try_advisory_lock` (`ProjectionEventFence.cs:120-130`); on non-relational providers it no-ops the lock (`ProjectionEventFence.cs:110-118`). Behavior diverges by provider.
- **`ManualWallet` proof lifecycle** is split between "Runtime execution remains application-owned and provider-neutral" and lifecycle mutation (`IPaymentLifecycleService.cs:51-58`) — a partially migrated payment path.

### 5.6 Dead / questionable code
- `MarketplaceProjectionOutboxProcessor.ProcessMessageAsync` orders by `OccurredAtUtc` only (`MarketplaceProjectionOutboxProcessor.cs:64-66`) for the *projection* queue (deliberate, per comment line 59-60: "Global version ordering across unrelated aggregates causes backlog amplification"), while the *event* queue orders by version. Two different ordering contracts on two sibling tables — easy to misread as inconsistency.

---

## 6. ARCHITECTURAL DECISIONS (WHY — reverse-engineered)

### 6.1 Why PostgreSQL remains the single authority
- Every projection is re-derived from SQL at read time (`ProductSearchSqlAuthorityOverlay.ApplyCoreAsync`, `ProductSearchSqlAuthorityOverlay.cs:85-344`); writes are forbidden outside the lifecycle (`IPaymentLifecycleService.cs:11-15`). **Why:** one source of truth avoids split-brain; outbox guarantees durability; `SqlTruthRebuildGuard` forces explicit authority on every rebuild (`SqlTruthRebuildGuard.cs:27`).

### 6.2 Why MongoDB exists
- Read-model store for storefront product catalog (Shopify-style embedded variants, `ProductVariantProjectionConsumer.cs:29-32`). **Why:** rich nested product documents with fast reads; "MongoDB is READ-ONLY source for Storefront" while "PostgreSQL is write-only for variants" — CQRS read/write separation.

### 6.3 Why Elasticsearch exists
- Search intelligence in analyzers (multilingual Arabic+Latin), not in app code ("NO translation dictionaries. NO data duplication", `SearchReindexController.cs:24-26`). **Why:** full-text/fuzzy/autocomplete/faceting at scale; ES is explicitly *not* source of truth ("Elasticsearch is NOT the source of truth", `ProductSearchIndexConsumer.cs:30-33`).

### 6.4 Why Redis exists
- Hot caches for store config + promotion-ranking + product cache; invalidated explicitly on mutation (`ProductVariantProjectionConsumer.cs:40-42`, `SearchReindexController.cs:251`). **Why:** avoid re-reading Mongo/SQL on every request for stable config.

### 6.5 Why outbox + projection queue (two-stage)
- `MarketplaceIdempotentConsumer` first stages a `MarketplaceProjectionOutboxMessage` in the authoritative SQL tx, then a *separate* worker (`MarketplaceProjectionOutboxProcessor`) performs Mongo/secondary writes **after commit** (`MarketplaceIdempotentConsumer.cs:48-141`). **Why:** the domain transaction must not block on Mongo/ES latency; durable handoff survives crashes; `ProjectionEventFence` dedupes at-least-once redelivery.

### 6.6 Why orders/events are immutable (fail-closed)
- `OutboxMessage` critical poison rows are kept operator-visible and slow-replayed, never dropped (`OutboxPublisherJob.cs:33,146-236`). `AccountingOutboxMessage.RecordNonTerminalFinancialFailure` keeps money events alive forever (`AccountingOutboxMessage.cs:252-261`). `StockSyncOutboxProcessor` suppresses stale replays rather than letting them corrupt inventory (`StockSyncOutboxProcessor.cs:421-477`). **Why:** losing a financial/marketplace event is worse than a backlog; fail-closed = never silently lose authority-affecting state. The system prefers operator reconciliation (`RequiresOperatorReconciliation`) over silent data loss.

### 6.7 Why storefront never owns products
- Storefront reads Mongo (projection) + overlays SQL; it never writes product truth. Product mutation is POS/Admin-owned SQL. **Why:** tenant isolation + single writer prevents concurrent catalog edits from storefront and admin clobbering each other; defensive tenant check in hydration (`ProductSearchService.cs:183`).

### 6.8 Why EventVersion is the ordering key, not time
- Stated explicitly in three processors: "S56/ S38C: Version is the source-mutation ordering key. Time is only a tie-breaker" (`MarketplaceEventOutboxProcessor.cs:409-412`, `StockSyncOutboxProcessor.cs:484-488`). **Why:** wall-clock time is unreliable across distributed writers; a monotonic version guarantees causal ordering of aggregate mutations even with out-of-order delivery.

---

## 7. EVIDENCE INDEX (key files)

| Concern | File |
|---|---|
| Generic outbox + poison logic | `PointOfSale.Infrastructure/Jobs/OutboxPublisherJob.cs` |
| Authority classification | `PointOfSale.Core/Events/IntegrationEventAuthority.cs` |
| Accounting outbox | `PointOfSale.Infrastructure/Accounting/AccountingOutboxProcessor.cs`, `AccountingOutboxMessage.cs` |
| Marketplace event outbox | `PointOfSale.Infrastructure/Eventing/MarketplaceEventOutboxProcessor.cs` + `MarketplaceEventOutboxWorker.cs` |
| Marketplace projection outbox | `PointOfSale.Infrastructure/Eventing/MarketplaceProjectionOutboxProcessor.cs`, `Consumers/Marketplace/MarketplaceIdempotentConsumer.cs` |
| Stock sync outbox | `PointOfSale.Infrastructure/Eventing/StockSyncOutboxProcessor.cs` |
| Payment cancellation | `PointOfSale.Infrastructure/Payment/PaymentCancellationOutboxProcessor.cs` |
| CommerceHub compensation | `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentCompensationOutboxWorker.cs` |
| Payments authority | `PointOfSale.Core/Interfaces/Payments/IPaymentLifecycleService.cs`, `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs` |
| SQL authority overlay | `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs` |
| Rebuild guard | `PointOfSale.Infrastructure/Reliability/SqlTruthRebuildGuard.cs` |
| Projection fence | fence `PointOfSale.Infrastructure/Reliability/ProjectionEventFence.cs` |
| ES service/index | `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchIndexService.cs` |
| ES consumers | `PointOfSale.Infrastructure/Consumers/ProductSearchIndexConsumer.cs`, `ProductVariantProjectionConsumer.cs` |
| Reindex controller | `PointOfSale.API/Controllers/SearchReindexController.cs` |
| ES drift job | `PointOfSale.Infrastructure/Jobs/ElasticsearchDriftReconciliationJob.cs` |
| Mongo read repo | `PointOfSale.Infrastructure/MongoDB/Services/ProductReadRepository.cs` |
| CommerceHub reconcile | `PointOfSale.API/Services/CommerceHub/CommerceHubProjectionReconciliationService.cs` |
