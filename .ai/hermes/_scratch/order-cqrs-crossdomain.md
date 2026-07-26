# Order CQRS / Projection / Outbox / Cross-Domain Runtime — Evidence Report

**Repo:** `C:\Users\fadya\source\repos\PointOfSale` (Shopixy/PointOfSale, C#/.NET 9)
**Method:** citation-only (`file:line`); claims verified against source. "MISSING EVIDENCE" flags where the task asked for something not found in code.
**Core invariant confirmed in code:** PostgreSQL = order write model (source of truth); MongoDB `orders` collection = storefront read model repaired on-demand from SQL; Redis = cache/locks; MassTransit + generic `OutboxMessage` = event delivery.

---

## 1. OrderProjectionConsumer — Mongo OrderDocument write, stale-rejection, checksum

File: `PointOfSale.Infrastructure/Consumers/OrderProjectionConsumer.cs`

- Subscribes to 10 event types: `OrderCreatedEvent`, `OrderStatusChangedEvent`, `OrderPaymentStatusChangedEvent`, `ShipmentStatusChangedEvent`, `OrderShippedEvent`, `OrderDeliveredEvent`, `OrderCancelledEvent`, `AffiliateAttributedEvent`, `AffiliateCommissionEarnedEvent`, `AffiliateCommissionReversedEvent` — `:38-49`.
- Writes to Mongo collection `"orders"` — `:65`.
- Ensures unique composite index `(TenantId, OrderId)` + `(TenantId, OrderNumber)` and freshness indexes on `LastEventVersion` / per-facet `Last*EventVersion` — `:82-133`.
- `OrderCreatedEvent` rejects non-positive `EventVersion` (throws `InvalidOperationException`) — `:152-159`; inserts new `OrderDocument` — `:263`; idempotent via existence check `:176-183` and duplicate-key catch `:274-280`.
- Per-facet stale-rejection filters built by `BuildFreshFacetEventFilter` — `:986-1020`: accepts the update only if the stored facet `Last*EventVersion` is **strictly less** than the incoming version, OR equal AND incoming `OccurredAtUtc` is **strictly newer**; also accepts when the version field is missing (legacy doc). Non-positive version rejected — `:995-1002`.
- `BuildFreshEventFilter` (lifecycle, used by non-facet events) same logic on `LastEventVersion` — `:956-984`.
- On `MatchedCount == 0` (stale/duplicate rejected) the consumer calls `HandleNoOrderProjectionMutationMatchAsync` — `:346-356` etc.; that handler logs rejection `:1068-1080`, then attempts `TryRehydrateOrderProjectionAsync` from SQL `:1082-1098`, else throws `:1100-1101`.
- `TryRehydrateOrderProjectionAsync` loads the SQL `Order` (`:1111-1121`) and rebuilds via `StorefrontOrderProjectionBuilder.Build` `:1139-1144` with a freshness-guarded `ReplaceOneAsync` `:1160-1164`; a no-match/no-upsert is treated as a SUCCESS (fresher data already present) `:1173-1176`.
- **NO checksum on the storefront `orders` document.** `OrderDocument` has no checksum field; `StorefrontOrderProjectionBuilder.ComputeAuthorityFingerprint` computes a JSON fingerprint only for *equality comparison during repair* (`:177-245`, used in `ProjectionAlreadyMatches` at `StorefrontOrderProjectionRepairService.cs:219-222` / `StorefrontOrderProjectionReconciliationService.cs:219-222`). Checksum (via `CommerceHubOrderProjectionChecksum.Compute`) exists only on the **CommerceHub** document, not storefront — see §7.
- Payment facet collapses replayed payloads against current SQL payment lineage via `OrderPaymentLineageAuthority.LoadCurrentVisibilityAsync` `:404-433`; if no SQL authority exists, it deletes the projection (`DeletePaymentProjectionWithoutSqlAuthorityAsync` `:417-422`, `:1190-1227`).
- Shipment facet loads `OrderFulfillmentLineageAuthority.LoadCurrentShipmentAsync` and collapses to SQL truth `:540-592`.

## 2. SQL-authority repair-on-read model (Storefront)

- `StorefrontOrderProjectionBuilder.Build` — `PointOfSale.Infrastructure/MongoDB/Projection/StorefrontOrderProjectionBuilder.cs:9-131`. Maps a SQL `Order` (+ optional `StorefrontOrderProjectionAuthoritySnapshot`) into an `OrderDocument`. Clamps `aggregateVersion` to `int.MaxValue` `:37`, `:100`.
- `StorefrontOrderProjectionAuthoritySnapshotLoader.LoadAsync` — `PointOfSale.Infrastructure/MongoDB/Projection/StorefrontOrderProjectionAuthoritySnapshotLoader.cs:11-64`. Reads authoritative affiliate attribution `:17-25`, commission ledger (earned − reversed) `:28-48`, and shipment visibility `:49-53` from SQL, returning a `StorefrontOrderProjectionAuthoritySnapshot` (record, `StorefrontOrderProjectionAuthoritySnapshot.cs:3-9`).
- `StorefrontOrderProjectionRepairService` — `PointOfSale.API/Services/Storefront/StorefrontOrderProjectionRepairService.cs`. `EnsureOrderProjectedAsync` `:40-57`, `EnsureOrderByNumberProjectedAsync` `:59-82`, `EnsureCustomerOrdersProjectedPageAsync` `:97-130` rebuild from SQL `BuildAuthorityQuery()` (ignores query filters; includes Customer, OnlineCustomer, Items) `:132-138`. `UpsertProjectionAsync` `:140-195` uses `ComputeAuthorityFingerprint` equality short-circuit (`ProjectionAlreadyMatches` `:197-223`) and a `BuildFreshnessGuard` filter `:225-243` (version + timestamp) before `ReplaceOneAsync(IsUpsert=true)` `:175`.
- `StorefrontOrderProjectionReconciliationService` — `PointOfSale.API/Services/Storefront/StorefrontOrderProjectionReconciliationService.cs`. Background `BackgroundService` every 30s `:18`, batch 50, max 5 passes `:19-20`. Sweeps SQL Ecommerce orders `:79-93` ordered by cursor, rebuilds via same builder/snapshot/fingerprint/freshness-guard `:108-184`. Duplicate-key (code 11000) treated as no-op `:157-164`.

## 3. IOrderReadRepository + OrderDocument — who reads

- `IOrderReadRepository` — `PointOfSale.Infrastructure/MongoDB/Services/IOrderReadRepository.cs`. Read-only; explicitly "NO SQL reads allowed in Storefront for orders" `:10-14`. Operations: `GetByIdAsync` `:20`, `GetByOrderNumberAsync` `:28`, `GetCustomerOrdersAsync` `:36`, `GetByIdsAsync` `:47`, `GetOrderTrackingAsync` `:55`, `GetOrdersByEmailAsync` `:63`.
- `OrderReadRepository` — `PointOfSale.Infrastructure/MongoDB/Services/OrderReadRepository.cs`. All queries tenant-scoped, dedupe by freshest `LastEventVersion` (`SelectFreshestProjection` `:169-177`); `GetByIdsAsync` keeps highest-version doc per `OrderId` `:125-134`.
- Consumers wiring (search_files content grep): `IOrderReadRepository` injected by `StorefrontOrdersReadController.cs:46,56`, `StorefrontAccountController.cs:45,56`; registered in `Program.cs:695`. Explicitly NOT used by Admin reads (certification tests assert absence) — `StorefrontOrderAuthorityCertificationTests.cs:100-101`, `AdminOrderReadAuthorityCertificationTests.cs:20`.
- `OrderDocument` — `PointOfSale.Infrastructure/MongoDB/Documents/OrderDocument.cs`. Fields incl. `Total` `:130`, affiliate block `:315-363`, per-facet freshness guards `:258-301`, `LastEventVersion` `:266`.

## 4. Generic Outbox — how Order events publish, ordering by EventVersion

File: `PointOfSale.Infrastructure/Jobs/OutboxPublisherJob.cs` + `PointOfSale.Core/Entities/OutboxMessage.cs`

- `OutboxMessage` written in the same transaction as the domain change — `OutboxMessage.cs:5-18`. Carries `EventType`, `EventPayload`, `TenantId`, `AggregateId`, `EventVersion`, `ProcessedAtUtc`, `RetryCount`, `RequiresOperatorReconciliation` — `OutboxMessage.cs:19-108`. `MaxRetries = 5` `:98`.
- `OutboxPublisherJob` runs via Hangfire every ~5s — `OutboxPublisherJob.cs:24,46`. Batch size 100, retention 7 days — `:31-32`.
- **Critical ordering by EventVersion:** critical messages ordered `OrderBy(m => m.TenantId).ThenBy(m => m.AggregateId).ThenBy(m => m.EventVersion).ThenBy(m => m.CreatedAtUtc).ThenBy(m => m.Id)` — `:86-92`; non-critical ordered by `CreatedAtUtc` only `:105-107`.
- Critical vs non-critical classification: `IsCriticalOutboxEvent` `:515-517` → `IntegrationEventAuthority.IsAuthoritativeEventType` / `IsCriticalEventTypeName`. Critical events also defer publication until no earlier unprocessed critical message for the same aggregate exists — `HasUnprocessedEarlierCriticalAggregateMessageAsync` `:601-642` + call `:318-336`. Compare via `IsEarlierCriticalAggregateMessage` (primary by `EventVersion`, tie-break by created-at/id) `:644-669`.
- `EnsureOutboxAuthorityScope` validates critical events: non-empty `TenantId` (outbox + payload), UTC `OccurredAtUtc`, positive `EventVersion` (throws if `OutboxMessage` version != payload version) `:676-745`. Sets `event-version` and `aggregate-id` headers on publish `:358-361`.
- Critical poison (exhausted retries) flagged for operator reconciliation but kept on a 15-min slow-replay cadence `:33,79,146-181,219-236`; non-critical poison is terminal-marked processed `:238-274`.

## 5. OrderEvents — authoritative vs non-authoritative

File: `PointOfSale.Core/Events/OrderEvents.cs` + `PointOfSale.Core/Events/IntegrationEventAuthority.cs`

- All core order events implement `IntegrationEvent, IAuthoritativeIntegrationEvent` (authoritative): `OrderCreatedEvent:13`, `OrderStatusChangedEvent:69`, `PaymentIntentOrderDomainEvent` (base, also authoritative) `:78`, `OrderAcceptedEvent`/`OrderAwaitingEvidenceEvent`/`OrderOperationalReviewQueuedEvent`/`OrderRejectedEvent` (sealed, inherit authoritative) `:87-93`, `OrderPaymentStatusChangedEvent:98`, `OrderShippedEvent:116`, `OrderDeliveredEvent:128`, `OrderCancelledEvent:138`.
- Authority classifier `IntegrationEventAuthority.IsAuthoritativeEventType` `:94-108`: authoritative if `IAuthoritativeIntegrationEvent` OR critical by name fragment, and NOT `INonAuthoritativeIntegrationEvent` nor a known non-authoritative name. Name fragments include `Order` `:9-12`; non-authoritative names include `WhatsAppOrderConfirmationRequestedEvent` `:48`.
- **Non-authoritative order-adjacent event:** `WhatsAppOrderConfirmationRequestedEvent` implements `INonAuthoritativeIntegrationEvent` — `PointOfSale.Core/Events/WhatsApp/WhatsAppOrderConfirmationRequestedEvent.cs:9`. (See §8.)
- Markers defined `IntegrationEvent.cs:19-32`; base `IntegrationEvent` carries `EventVersion` (alias `Version`) `:101-105`, `TenantId` `:117`.

## 6. Affiliate attribution + commission + refund handling

- `AffiliateOrderAttributionConsumer` — `PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateOrderAttributionConsumer.cs`. Consumes `AffiliateOrderPlacedEvent` `:43,64`. Validates `EventVersion > 0` `:85-89` and Tenant/StoreTenant match `:91-101`. Calls `IAffiliateService.AttributeOrderToAffiliateAsync` `:131-137`, then writes a **placeholder** `AffiliateOrderDocument` (`"AttributedPendingPayment"`, `PaidAmount=0`) into Mongo `affiliate_orders` `:163-214` for immediate portal visibility (non-fatal if it fails `:208-214`).
- `OrderAttributionStore` — `PointOfSale.Infrastructure/Attribution/OrderAttributionStore.cs`. Stores analytics attribution in Mongo `order_attributions` `:49` + Redis cache (TTL 7d) `:33,119-122`. **Immutable** (never updated) `:23`; insert with duplicate-key idempotency `:128-133`. `ApplyAuthoritativeRevenueAsync` overrides `OrderValue` from SQL net-revenue authority `:371-383`; range queries intersect with purchased orders `:223-238`. This is the **analytics** attribution store, distinct from the SQL `AffiliateAttribution` row stamped onto `OrderDocument`.
- Commission on order (projection side): `OrderProjectionConsumer` consumes `AffiliateAttributedEvent` → sets `IsAffiliateOrder`, `AffiliateId`, `AffiliateReferralCode`, `AttributedAtUtc` via freshness-guarded update `:1238-1320`; consumes `AffiliateCommissionEarnedEvent` → sets `AffiliateCommissionAmount` via `BuildFreshAffiliateCommissionEventFilter` (accepts only if new version, and never overwrites by same-version via `Ne(LastAffiliateCommissionEventId, eventId)`) `:1343-1408`; consumes `AffiliateCommissionReversedEvent` → atomic `$let/$cond` decrement clamped `>= 0` `:1415-1528`.
- **Commission is authoritative from SQL via the repair builder:** `StorefrontOrderProjectionAuthoritySnapshotLoader` computes `netAffiliateCommission = max(0, earned - reversed)` from `AffiliateCommissionLedger` `:28-48`, and `StorefrontOrderProjectionBuilder` stamps `AffiliateCommissionAmount` from that snapshot `:115,121-129`. So Mongo commission is SQL-authoritative on repair; the event path is an incremental projection.
- Refund handling: `OrderItemRefundedEvent` — `PointOfSale.Core/Events/Affiliate/OrderItemRefundedEvent.cs:21-46`. Implements `IntegrationEvent, IAggregateScopedIntegrationEvent` (aggregate = `OrderId` `:45`). Carries `RefundedAmount` (transaction-scoped) `:34` and `CumulativeRefundedAmount` `:40`; consumer calls idempotent `AffiliateService.ReverseCommissionForOrderItemAsync` `:18-19`. Note: ledger uses `CommissionEntryType.Reversed` (see §2 loader `:41`) — the projection-side debit is `AffiliateCommissionReversedEvent` handled at `:1415-1528`.

## 7. CommerceHub link — storefront Order vs marketplace orders

- **Separate SQL entities:** `CommerceHubMarketplaceOrder` is its own entity (`CommerceHubMarketplaceOrder.cs:6`, `DbSet` `ApplicationDbContext.cs:169`), with `SubOrders`/`Payments`, keyed by `MarketplaceOrderId` — distinct from storefront `Order` (keyed by `Order.Id`).
- **BUT the storefront marketplace *read model* is built from the storefront `Order`:** `MarketplaceOrderReadModelProjectionService.RebuildAsync` queries `_dbContext.Orders` (storefront Order) by `order.Id == orderId` `:459-465`, and builds `MarketplaceOrderReadModelDocument` from that Order's fields (Total `:502`, items `:504-518`, financial posting `:500-501`). So the document's `OrderId` (`MarketplaceOrderReadModelDocument.cs:18`) is the **storefront Order.Id**, not a `CommerceHubMarketplaceOrder` id.
- The `OrderReadModelConsumer` `:7-46` consumes `MarketplaceEvents.*` (`Marketplace/MarketplaceDomainEvents.cs`) whose `EntityId` is the storefront Order id (`IMarketplaceDomainEvent.EntityId` `:13`; `OrderReadModelConsumer` passes `@event.EntityId` `:38-43`). `Marketplace.OrderCreatedEvent` is a **distinct marketplace domain event** (record, `MarketplaceDomainEvents.cs:55-70`), NOT the storefront `OrderCreatedEvent`.
- The storefront `OrderProjectionConsumer` keyed on `(TenantId, OrderId)` writes the `"orders"` collection; the CommerceHub projection consumer (`CommerceHubOrderProjectionConsumer.cs`) writes a *different* collection `"commercehub_orders"` from `CommerceHubMarketplaceOrders` `:28,49`, with its own checksum `CommerceHubOrderProjectionChecksum.Compute` `:141` and `ProjectionChecksum` field `:156-159`.
- **Conclusion:** The customer-facing storefront Order and the CommerceHub marketplace order are SEPARATE runtimes/entities in SQL. The `marketplace_order_read_models` Mongo document is a *read model of the storefront Order projected for the CommerceHub marketplace channel*, fed by `Marketplace.*` events whose `EntityId` is the storefront `Order.Id`. The native `CommerceHubMarketplaceOrder` entity is projected into the separate `commercehub_orders` collection. (Note divergence risk: the marketplace read model `TotalAmount = order.Total` `:502` is taken from the storefront Order at rebuild time; any cross-channel reconciliation must treat marketplace `FinancialPostingStatus` as a per-store-channel overlay, not a copy of storefront status.)

## 8. Notifications — WhatsApp order confirmation

- `WhatsAppOrderConfirmationRequestedEvent` — `PointOfSale.Core/Events/WhatsApp/WhatsAppOrderConfirmationRequestedEvent.cs:9`. Implements `IntegrationEvent, INonAuthoritativeIntegrationEvent` (non-authoritative, notification side-effect, not business truth). Carries `OrderId`, `OrderNumber`, `CustomerPhone`, `OrderTotal`, etc. `:11-24`.
- `WhatsAppOrderConfirmationConsumer` — `PointOfSale.Infrastructure/Consumers/WhatsApp/WhatsAppOrderConfirmationConsumer.cs:11`. Consumes the requested event `:33`; double-checks feature gate `:44-48` and active account `:51-56`; composes template `:59-69`; writes `WhatsAppMessageLogDocument` with an **idempotency key** `whatsapp:order-confirmation:{TenantId}:{OrderId}` `:87`; dispatches `:93`. Because the event is `INonAuthoritativeIntegrationEvent`, the outbox publisher does NOT enforce tenant/version/aggregate ordering guards on it (see §5 `IntegrationEventAuthority.cs:48`).

## 9. OrderRequestIdempotency — dedup

File: `PointOfSale.Core/Entities/OrderRequestIdempotency.cs` + migration `20260415103000_AddOrderRequestIdempotency.cs` + `CheckoutRequestIdempotencyCoordinator.cs`

- Entity fields: `TenantId`, `Key`, `RequestHash` (max 64), `SnapshotHash?`, `OrderId?`, `ResponseStatusCode?`, `ResponsePayload?`, `CompletedAtUtc?`, `ClaimToken?`, `ClaimTimestampUtc?` — `OrderRequestIdempotency.cs:5-16`.
- Migration creates table + **unique index `IX_OrderRequestIdempotency_TenantId_Key`** `20260415103000_AddOrderRequestIdempotency.cs:39-43`. (A later migration adds `SnapshotHash` column `20260419113000_AddOrderRequestIdempotencySnapshotHash`.)
- Coordinator claims a key: checks existing persisted claim by `(TenantId, Key)` `:502-513`; inserts new `OrderRequestIdempotency` on race-safe `SaveChangesAsync`, swallowing `DbUpdateException` (unique-index conflict = another request already claimed) `:515-539`. Stale unclaimed rows pruned by `(TenantId, Key, OrderId IS NULL, not completed, claim older than cutoff)` `:542-557`.
- Related claim usage also in `OrdersController.cs:1498`, `PaymentService.cs:3021`. Cleanup job: `OrderRequestIdempotencyCleanupJob.cs`.
- This is **request-level dedup at the checkout boundary** (pre-write), NOT within the Mongo projection consumer (the projection consumer's dedup is via event-version freshness guards + Mongo unique index, §1).

---

## Authority divergences / risks flagged

- **Projection total vs SQL total:** No code computes or asserts that `OrderDocument.Total` (`OrderDocument.cs:130`) equals SQL `Order.Total`. Projection is *rebuilt* from SQL on repair/reconciliation (`StorefrontOrderProjectionBuilder.Build` maps `order.Total` `:58`), so at repair time they match; but between events, an event-path bug could drift. The only automatic convergence is the 30s reconciliation sweep (§2) and on-demand `Ensure*Projected` before reads. **No compile-time or runtime invariant enforces projection total == SQL total.** Recommend a convergence test / checksum on `Total`.
- **Storefront `orders` doc has no checksum;** only CommerceHub doc has one (`CommerceHubOrderProjectionConsumer.cs:141`). Storefront repair relies on `ComputeAuthorityFingerprint` JSON equality (`StorefrontOrderProjectionBuilder.cs:177-245`) which *excludes* `AffiliateCommissionAmount` only if not in snapshot — actually it IS included `:223` — but excludes `LastEvent*`/`Timeline`/freshness fields, so two docs with same business state but different event versions would be flagged as differing and re-upserted. Acceptable but worth noting.
- **Commission double-source:** `OrderDocument.AffiliateCommissionAmount` is set both by `AffiliateCommissionEarnedEvent` projection (§6) AND by SQL-authority snapshot on repair (§2). Repairs overwrite event-incremented values with `max(0, earned-reversed)`. If an event replay races a repair, the freshness guards on the commission facet (`:1035-1053`) protect against overwrite, but the *repair* path uses `BuildFreshnessGuard` on `LastEventVersion` (whole-doc), not the commission facet — potential edge race where a stale repair could reset commission if its `sourceVersion` > doc's `LastEventVersion`. Flag for review.
- **Marketplace read model = storefront Order, not CommerceHubMarketplaceOrder** (§7): if a storefront Order is deleted/soft-deleted but a marketplace order still exists, `RebuildAsync` returns `NotFound` `:473-481` and the marketplace doc is NOT auto-deleted (contrast with product/homepage paths which delete `:100-141`). Divergence: marketplace order read model can linger after storefront order tombstone.

## Missing evidence

- No dedicated `StorefrontOrderProjectionAuthoritySnapshot` *class* file beyond the 9-line record (`StorefrontOrderProjectionAuthoritySnapshot.cs`); its loader is the SQL-authority source (confirmed).
- `OrderPaymentLineageAuthority` / `OrderFulfillmentLineageAuthority` referenced but not opened here; their SQL queries are the authoritative truth behind payment/shipment facet collapsing (§1). Not quoted line-level — flagged.
- `OutboxPublisherJob` non-critical path order-by `CreatedAtUtc` only (`:105-107`) — cross-aggregate ordering for non-critical events is NOT version-guaranteed; acceptable since non-critical events are side-effects.
- `AffiliateAttributedEvent` / `AffiliateCommissionEarnedEvent` / `AffiliateCommissionReversedEvent` event class files not opened; their projection handling in `OrderProjectionConsumer` (`:1238-1528`) is the operative evidence. Event schema itself not cited line-level — flagged.
