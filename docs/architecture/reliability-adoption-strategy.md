# Reliability Adoption Strategy

Date: 2026-05-22

## Purpose

This document defines the incremental adoption strategy for the shared reliability primitives in `PointOfSale.Infrastructure.Reliability`.

The goal is controlled reliability convergence, not perfect normalization. PostgreSQL remains the authoritative write boundary. MongoDB, Redis, Elasticsearch, storefront, affiliate, CommerceHub, search, cache, and analytics surfaces remain derivative visibility systems.

## Reliability Tiers

| Tier | Name | Migration stance |
|---|---|---|
| Tier 1 | Critical authority consumers | Migrate first when the flow can safely use projection primitives. Keep lifecycle/outbox-specific manual protections where they are stronger than generic projection helpers. |
| Tier 2 | High visibility / derived state | Migrate opportunistically when the path already has version/freshness metadata or SQL reload support. Freshness-only behavior can remain temporarily. |
| Tier 3 | Low authority / analytics / soft state | Do not over-engineer. Use idempotency where duplicate side effects matter, but eventual convergence and lighter guarantees are acceptable. |

## Adoption Matrix

| Consumer / flow | Tier | Authority level | Current protection level | Current infrastructure usage | Migration priority | Remaining gaps |
|---|---:|---|---|---|---|---|
| `ProductVariantProjectionConsumer` | 1 | Variant identity, SKU/barcode, media, lifecycle, parent-lineage and stock visibility triggers | Strong but mixed | `ProjectionConsumerEventFence`, `ProjectionAuthorityValidator`, `ProjectionTombstoneGuard`, SQL variant reload, `ProductVariantLifecycleAuthority`, manual Mongo freshness filters | Immediate / partially migrated | Parent lifecycle and stock visibility now fail closed through SQL public-lineage recomposition; keep existing manual array filters until a narrow helper exists, and created/update/delete paths still own consumer-local Mongo merge logic. |
| Product/variant clone-import mutation surfaces | 1 | Variant semantic identity, simple-product SKU/barcode mirroring, media inheritance, initial stock fanout | Strong on inspected API mutation surfaces | `ProductVariantCloneImportAuthority`, `ProductVariantLifecycleAuthority`, product/variant request idempotency where available, SQL unique indexes, stock mutation support | Immediate / partially migrated for PRODUCT-CON-11B | Product create/update and variant create/update are hardened; no standalone CSV/Excel/ERP/supplier import framework or named clone endpoint was present, so future explicit import/clone workers must adopt this helper and add replay tests. |
| Product/variant merge-split semantic lifecycle | 1 | Variant semantic owner, supersession lineage, SKU/barcode ownership, reservation and stock mutation eligibility, public visibility | Strong on inspected authority predicates and mutation entrypoints | `ProductVariantMergeSplitAuthority`, `ProductVariantLifecycleAuthority`, SQL semantic lifecycle columns, current-owner filters, open-reservation rejection, product/variant/stock/search/CommerceHub fail-closed checks | Immediate / partially migrated for PRODUCT-CON-11C | Semantic ownership and replay collapse are hardened; no dedicated merge/split orchestration endpoint was present, so future physical stock transfer, reservation migration, supplier dedup, and bulk cleanup workflows must adopt this helper and add transaction/replay tests. |
| Product/variant inventory and reservation lineage | 1 | Variant stock ownership, reservation consumption, warehouse ownership, stock mutation, stock visibility fanout | Strong on inspected runtime mutation and fanout paths | `ProductVariantInventoryLineageAuthority`, `ProductVariantMergeSplitAuthority`, row locks, SQL stock/reservation filtering, stock-sync stale fanout suppression | Immediate / partially migrated for PRODUCT-CON-11D | Reservation creation/consumption, stock mutation, checkout/CommerceHub payment and delivery validation, and stock-sync fanout are hardened; future physical stock transfer, reservation migration, warehouse reassignment, external feed/export writers, and bulk inventory import/reconciliation workflows must adopt the helper and add replay/rebuild tests. |
| External derivative product export/replay visibility | 1 | CommerceHub external discovery, marketplace rebuilt cards, homepage cached/recommended cards, future feed/export eligibility | Strong on inspected CommerceHub search/reindex, marketplace product/homepage rebuild, and storefront homepage card recomposition | `ProductVariantLifecycleAuthority`, `ProductVariantInventoryLineageAuthority`, `CommerceHubSearchSqlAuthorityOverlay`, SQL-revalidated homepage/reindex boundaries, tombstone/scrub/no-op convergence | Immediate / partially migrated for PRODUCT-CON-11E | No standalone affiliate/supplier/catalog/inventory/pricing/marketplace feed writer was found in the inspected backend; future external sync, feed, cache warmer, reporting, and recommendation writers must adopt SQL recomposition plus replay/rebuild coverage. |
| `ProductMediaChangedEventConsumer` | 1 | Product gallery, primary-image, and variant media linkage visibility | Strong | `ProjectionEventFence`, `ProjectionAuthorityValidator`, SQL media/variant reload, search overlay | Immediate / migrated | Broader CDN/image workers still need the same pattern when introduced. |
| `ProductStockChangedConsumer` | 1 | Stock visibility propagation | Strong | `ProjectionEventFence`, SQL stock authority reload, cache/search side-effect collapse | Immediate / migrated | Future stock feed writers and repair scripts must reuse this pattern. |
| `ProductStockPriceSearchIndexConsumer` | 1 | Stock and pricing search visibility | Strong | `ProjectionEventFence`, mandatory SQL context, `ProductSearchSqlAuthorityOverlay`, `SqlTruthRebuildGuard` through overlay path | Immediate / migrated | Stock/price search delete decisions now happen after SQL overlay; other pricing consumers still use local replay contracts. |
| `ProductPromotionSearchIndexConsumer` | 1 | Promotion-decorated pricing/search visibility | Strong on inspected search side-effect path | Event fence, SQL promotion reload/version validation, SQL winning-promotion recomposition, product search SQL overlay | Immediate / migrated for promotion search indexing | Promotion-specific logic remains local, but Elasticsearch mutation no longer uses event payload or Mongo promotion fields as final authority. |
| `ProductCacheInvalidationConsumer` | 1 | Cache side-effect collapse for product lifecycle, publication, price, stock, and homepage visibility invalidation | Strong on product cache event fanout | Mandatory SQL context, `ProjectionConsumerEventFence` for product create/update/delete/stock/price/publication events, current SQL slug reload before product-detail invalidation | Immediate / migrated for product cache fanout | Cache invalidation is still best-effort; lower-risk CMS/navigation/footer cache paths and future CDN/feed/cache-warmer fanout remain separate review targets. |
| `ProductSearchIndexConsumer`, `ProductUpdatedSearchIndexConsumer`, `ProductDeletedSearchIndexConsumer` | 1/2 | Search visibility for catalog authority | Strong on create/update/delete/archive/publication runtime search side effects | `ProjectionEventFence`, mandatory SQL context, `ProductSearchSqlAuthorityOverlay` with category/media/variant/stock/pricing/promotion/tombstone overlays, lifecycle SQL recomposition | Immediate / migrated for inspected runtime search lifecycle paths | Product create/update/delete search indexing still contains path-specific logic; autocomplete/suggestion fields are protected as part of the product ES document rather than a separate suggestion authority layer. |
| `ProductCreatedEventConsumer` and `ProductUpdatedEventConsumer` | 1 | Storefront `ProductDocument` create/update visibility | Strong on shared storefront product writer path | `IProductSqlAuthorityProjectionWriter`, `IStorefrontProductProjectionMutationBoundary`, `ProductSearchSqlAuthorityOverlay`, reviewed tombstone/upsert mutation boundary, SQL recomposition of `DefaultUnit` / `Units` / simple-product purchasability metadata, no optional non-SQL fallback constructor | Immediate / migrated for GLOBAL-REL-CON storefront product projection governance | The shared writer and mutation boundary govern storefront `ProductDocument` writes only; affiliate, CommerceHub, promotion, and search consumers still keep their own hardened paths. |
| `ProductVisibilityLifecycleProjectionConsumer` | 1 | Storefront product publication/delete/archive lifecycle visibility | Strong on registered runtime lifecycle path | `ProjectionEventFence`, SQL product publication reload, `ProductSearchSqlAuthorityOverlay`, tombstone-on-missing/unpublished authority | Immediate / migrated for PRODUCT-CON-07 storefront lifecycle | Future bulk import/admin repair paths that mutate product publication must emit `ProductPublishStatusChangedEvent` or call equivalent SQL recomposition/tombstone behavior. |
| `CommerceHubProjectionConsumer` | 1 | CommerceHub product/listing publication, variant lifecycle, price, stock, media, promotion, shipping, store metadata, and tenant currency visibility | Strong on stock/delete/price/media/listing/publication/variant lifecycle and metadata fan-out; mixed elsewhere | `ProjectionConsumerEventFence` for stock/delete/price/media/listing/publication/variant lifecycle and store/shipping/currency metadata fan-out, `ProjectionAuthorityValidator`, `ProductVariantLifecycleAuthority`, SQL product/listing publication reloads, SQL stock/price/media/listing/store/currency reloads, manual listing freshness filters, CommerceHub search SQL overlay | Immediate / partially migrated | Variant updates now reconcile SQL listing publication plus current SQL variant lifecycle before Mongo/search side effects; remaining promotion/policy refreshes and future feed/export writers remain local/manual. |
| `CommerceHubQueryService` autocomplete/suggestions | 1/2 | CommerceHub autocomplete, search suggestions, zero-result suggestions, category suggestions, popular/product-name completion terms | Strong on inspected response boundary | Cached and fresh ES/Mongo suggestion responses recompose product suggestions through `CommerceHubSearchSqlAuthorityOverlay`, load listing tenant ownership from SQL, filter active category lineage from SQL, and filter suggested/popular terms to SQL-visible product/category matches | Immediate / migrated for PRODUCT-CON-10 response paths | This is not a standalone completion-index framework; storefront browser-side autocomplete cache and any future keyword/trending/suggestion rebuild workers remain separate review targets. |
| `AffiliateProductIntelligenceProjectionConsumer` | 1 | Affiliate product publication, variant lifecycle, inventory, and media visibility | Strong on stock/media/publication tombstone propagation | Event fence for stock/media events, SQL stock reload through `ProductVariantLifecycleAuthority`, SQL media reload/version validation, SQL product `PublishStatus.Published` visibility check, manual document freshness | Immediate / partially migrated | Stock recomposition now ignores archived/detached/orphan variant lineage; republish rebuild relies on adjacent product update projection for rich intelligence content, and non-stock/non-media/non-publication refreshes remain local. |
| `AffiliateProductPolicyProjectionConsumer` | 1 | Affiliate publication, pricing, media thumbnails, commission-policy, promotion, and storefront metadata visibility | Partial strong | Event fence for selected product/price/stock/media/publication events, SQL price/media/search overlay, manual promotion/store policy refresh | Immediate / partially migrated | Publication changes now trigger SQL-overlay refresh; promotion and policy rebuild semantics remain consumer-local, and CDN/feed purge remains outside this consumer. |
| `PromotionProjectionConsumer` | 1 | Promotion authority read model and storefront overlay visibility | Partial strong | Manual version/timestamp filters, SQL promotion recomputation, field-scoped storefront product overlay refresh, product search overlay for downstream indexing | Immediate / partially migrated | Product-promotion refresh no longer full-replaces `ProductDocument`, but activation/expiration/category-banner/cache side effects still rely on local protection instead of a fully shared fence model. |
| `DiscountReadModelRepairService` | 1 | SQL-backed promotion/coupon repair | Strong but local | `SqlTruthRebuildGuard`, SQL-backed rebuild, manual overlay ownership preservation | Immediate / partially migrated | Repair path is SQL-first; generic rebuild abstractions beyond current guard are not mature. |
| `CouponProjectionConsumer` | 1 | Coupon checkout/pricing visibility | Strong on SQL coupon/usage visibility | `ProjectionEventFence`, SQL `Coupon` reload, SQL `CouponUsages` usage recomposition, manual freshness filters, `ProjectionTombstoneGuard`, tombstone-on-missing SQL authority | Immediate / migrated for runtime visibility | Coupon events still lack true aggregate mutation versions; freshness remains event timestamp/version based with SQL recomposition. |
| `PaymentMethodProjectionConsumer` | 1 | Checkout payment-method visibility and limits | Strong on payment-method visibility | `ProjectionEventFence`, SQL `TenantPaymentMethod` reload, SQL version validation, encrypted config recomposition, soft tombstones | Immediate / migrated for runtime visibility | Event contracts remain legacy non-`IntegrationEvent`; the shared fence supports this through a legacy EventId overload. |
| `ShippingMethodProjectionConsumer` | 1 | Checkout shipping price/options visibility | Strong on SQL reload and duplicate side-effect collapse | `ProjectionEventFence`, SQL shipping-method reload, manual repository freshness filters, `TombstoneIfFreshAsync`, soft delete projection | Immediate / partially migrated | SQL overlay should wait until a shipping-specific overlay exists; freshness validation remains repository-local. |
| `TenantCurrencyProjectionConsumer` | 1 | Tenant currency visibility for storefront/affiliate/CommerceHub | Strong on storefront currency; adjacent fan-out hardened | `ProjectionEventFence`, SQL `Tenant.Currency` reload, manual `LastCurrencyEventVersion` freshness, preserved field watermark during StoreConfig replacement | Immediate / migrated for storefront currency | Future tenant-currency fan-out writers must continue to reload SQL currency or preserve a stronger SQL-derived watermark before search/cache side effects. |
| `StorefrontConfigProjectionConsumer` | 1 | Storefront checkout/config behavior visibility | Strong but specialized | `ProjectionEventFence`, `DerivativeProjectionMutationGuard` for reviewed Mongo replacement and Redis cache invalidation, SQL config/tenant/currency reload, manual event version freshness, preserved currency watermark, publish/rollback handling | Immediate / partially migrated | Config publish/rollback semantics remain specialized; shared fence and runtime guard now govern duplicate Mongo/Redis side effects, but a generic base class is still not appropriate. |
| Storefront/CommerceHub homepage and recommendation visibility paths | 1/2 | Homepage product shelves, curated product/category references, affiliate-mode homepage visibility, CommerceHub homepage projection | Strong on inspected homepage slice | Runtime SQL recomposition in `StorefrontHomeService`, SQL reference validation/scrubbing in homepage section read/admin/projection paths, homepage cache invalidation on stock/publish changes, `SqlTruthRebuildGuard` on CommerceHub homepage rebuild | Immediate / partially migrated for PRODUCT-CON-06 | Dedicated personalized recommendation/ranking snapshot/feed-writer engines were not found in this sweep; future engines must recompose SQL product/listing/category/media/stock/price/promotion/publication authority before public discovery mutation. |
| `AdminSyncController` product read-model repair | 1 | Manual storefront product rebuild and recommendation/homepage visibility repair trigger | Strong on inspected AdminSync product repair path | `IProductSqlAuthorityProjectionWriter`, `IStorefrontProductProjectionMutationBoundary`, SQL deleted-row scan for tombstone convergence, reviewed storefront tombstone/upsert boundary, source guard against bypass | Immediate / migrated for PRODUCT-CON-09 AdminSync path and GLOBAL-REL-CON storefront writer governance | This is not a universal repair framework; future bulk importers, cache warmers, feed exporters, direct repair endpoints, and recommendation rebuild workers must explicitly adopt SQL recomposition and tombstone/delete convergence. |
| `OrderProjectionConsumer` | 1 | Customer-critical storefront order read model | Strong specialized / ORDER-CON-01 shipment facet and PAYMENT-CON-01 payment facet hardened | SQL authority snapshot loader, `OrderFulfillmentLineageAuthority` for shipment-facet replay and rebuild visibility, `OrderPaymentLineageAuthority` for payment-facet replay, fail-closed payment delete/cache invalidation and conflicting-timeline suppression, manual `ProcessedEvents`/freshness, repair/reconciliation services | Keep manual now | Projection primitives are less important than SQL snapshot correctness and critical-read repair; widen SQL recomposition before adding new payload-owned shipment or payment exports. |
| Payment refund/settlement lifecycle fanout | 1 | SQL order payment status visibility after accepted refund and COD/gateway settlement | Strong specialized / PAYMENT-CON-01 hardened | `PaymentLifecycleService`, refund-id and settlement-id replay collapse, accounting outbox, canonical `OrderPaymentStatusChangedEvent` fanout from SQL lifecycle transitions | Keep lifecycle-owned | Do not reconstruct payment-status fanout from refund, settlement, callback, cache, export, or projection payloads. Future outward payment visibility must reload SQL payment/refund/settlement authority. |
| `CommerceHubOrderProjectionConsumer` | 1 | Customer-critical CommerceHub order read model | Strong but local | Manual event version and projection checksum handling, repair service | Keep manual now | Could later use `ProjectionEventFence`, but current SQL-backed repair is the higher-value invariant. |
| `StorefrontOrderProjectionRepairService` and `StorefrontOrderProjectionReconciliationService` | 1 | SQL repair for critical order read continuity | Strong local | SQL-first rebuild, freshest duplicate projection selection | Keep manual now | Could adopt `SqlTruthRebuildGuard` naming, but no behavior migration is needed. |
| `CommerceHubOrderProjectionRepairService` and `CommerceHubProjectionReconciliationService` | 1 | SQL repair for marketplace order/read continuity | Strong local | SQL-first rebuild, duplicate projection freshness, reconciliation sweep | Keep manual now | Same as storefront order repair; do not force generic rebuild abstractions. |
| `StockSyncOutboxProcessor` / worker | 1 | Inventory propagation after SQL stock mutation | Strong specialized | SQL outbox ordering, head-of-line blocking, compensation/replay, current SQL stock snapshot emission, `ProductVariantInventoryLineageAuthority` stale/detached fanout suppression | Keep manual now | Projection primitives can help downstream consumers, not the outbox authority processor itself; future feed/export stock writers must preserve the same SQL lineage suppression. |
| `InventoryConsistencyService` / `CommerceHubInventoryReconciliationJob` | 1 | Reservation, fulfillment, return and stock reconciliation | Strong specialized | SQL reconciliation jobs, lock/idempotency patterns, deterministic stock tests, current consumable reservation filtering through `ProductVariantInventoryLineageAuthority` | Keep manual now | Do not convert into projection framework; future reconciliation paths that consume reservations or publish visibility must reuse current SQL inventory lineage. |
| `ProductVariantStockMutationSupport` and `ProductVariantStockRepository` | 1 | SQL stock mutation and reservation authority | Strong specialized | Row locks, idempotency keys, SQL constraints, timestamp normalization, current inventory lineage checks and consumable reservation filtering | Keep manual now | Not a projection consumer; shared primitives apply only to emitted visibility paths, while mutation/consumption must continue to use SQL lineage authority. |
| `MarketplaceEventOutboxProcessor` | 1 | Marketplace authoritative outbox dispatch | Strong specialized | Critical outbox ordering, affected-tenant scope, terminal failure handling | Keep manual now | Generic projection fence is not a substitute for backbone ordering/readiness semantics. |
| `MarketplaceProjectionOutboxProcessor` | 1 | Marketplace projection compensation and rebuild | Strong specialized | Replay original payload where possible, SQL rebuild fallback, dead-letter/archive handling | Keep manual now | Candidate for explicit `SqlTruthRebuildGuard` calls at rebuild fallback entrypoints only. |
| `MarketplaceBackboneConsumers` / `MarketplaceIdempotentConsumer` | 1 | Marketplace product/order backbone projection | Strong specialized | Idempotent consumer wrapper, mutation-version freshness, SQL-backed read-model rebuild | Keep manual now | Do not replace wrapper with generic fence until thread-level semantics are proven equivalent. |
| `MarketplaceReadModelProjectionServices` | 1 | Marketplace product/order/read-model rebuild | Strong local / partially migrated | SQL rebuild from current product/order truth, mutation version checks; product rebuild declares `SqlTruthRebuildGuard` for variant/pricing/stock/media/tombstone overlays; order and tenant-scoped homepage rebuilds declare explicit SQL authority overlays | Opportunistic / partial | Product rebuild now tombstones deleted, inactive, or offline SQL products; global homepage composition remains aggregate visibility and is not a single-row SQL authority claim. |
| `AccountingOutboxProcessor` / worker | 1 | Ledger/accounting posting side effects | Strong specialized | SQL leases, critical event classification, ordering, readiness gates | Keep manual now | Projection primitives are not appropriate for financial ledger authority. |
| `CommerceHubPaymentCompensationOutboxWorker` | 1 | External captured-money/refund compensation | Strong specialized | SQL compensation rows, affected-store/suspense scope, reconciliation gates | Keep manual now | Do not generalize; money ambiguity requires bespoke fail-closed semantics. |
| `CommerceHubPaymentService` and payment recovery authority services | 1 | Provider session/captured-money authority | Strong specialized | Serializable locks, idempotency, unresolved webhook durability, suspense routing | Keep manual now | Projection primitives do not apply to provider execution authority. |
| `CommerceHubFinanceReconciliationJob.ResolveLinkedUnresolvedCapturedPaymentsAsync` | 1 | CommerceHub unresolved captured-money reconciliation | Strong specialized / CHECKOUT-CON-14 hardened | SQL unresolved webhook records, serializable per-candidate recovery transaction, webhook/payment/order row locks, accounting outbox idempotency, marketplace hold release from current SQL order truth | Keep manual now | Candidate discovery may inspect provider evidence; mutation must continue to reload locked SQL truth before suspense clearance or hold release. |
| `StorefrontUnresolvedPaymentReconciliationJob` / hosted service | 1 | Storefront captured-money reconciliation | Strong specialized / CHECKOUT-CON-14 hardened | SQL unresolved webhook records, serializable recovery transaction, webhook/payment-intent/order row locks, lifecycle replay, suspense recovery policy | Keep manual now | Do not migrate to projection abstractions; future recovery branches must preserve locked SQL recomposition before accounting enqueue or lifecycle replay. |
| `CommerceHubRefundService`, `RefundExecutionService`, `CommerceHubRefundRecoveryHostedService` | 1 | Refund execution/recovery authority | Strong specialized | Processing/reconciliation states, evidence validation, readiness gates | Keep manual now | Projection helpers are irrelevant; keep financial recovery explicit. |
| `CommerceHubPayoutService` and `CommerceHubPayoutReconciliationHostedService` | 1 | Vendor outward-money movement | Strong specialized / CHECKOUT-CON-14 hardened | Final readiness gates, serializable authority, payout row locks, reconciliation-required state | Keep manual now | Do not normalize with projection infrastructure; stale processing quarantine must keep reloading locked payout SQL truth. |
| `AffiliateSettlementOrchestrator`, `AffiliatePayoutService`, affiliate accounting consumers | 1 | Affiliate payout/commission/accounting authority | Strong specialized | Serializable payout boundary, global transfer evidence, net commission authority, accounting outbox | Keep manual now | Affiliate product visibility can migrate; payout authority should not. |
| `OrderStatusSyncConsumer`, `OrderShipmentOrchestrationConsumer`, shipment notification/registration consumers, `ShipmentAuthorityRecoveryHostedService` | 1 | Fulfillment/order/shipment side effects and recovery | Strong specialized / CHECKOUT-CON-10, CHECKOUT-CON-13, and ORDER-CON-01 shipment handoff hardened | Lifecycle service ownership, serializable fulfillment/shipment SQL recomposition, order/shipment row locks, `OrderFulfillmentLineageAuthority` at shipment-to-order handoff, replay collapse, recovery workers, provider evidence checks, stale carrier-registration success rejection into reconciliation | Keep manual now | Carrier/warehouse ingress remains specialized; future exports, callbacks, and duplicate external-notification paths must keep current SQL shipment lineage before side effects can amplify. |
| `CmsPageProjectionConsumer`, `NavigationProjectionConsumer`, `StorefrontFooterProjectionConsumer`, homepage/footer/cache services | 2 | Storefront content visibility | Manual adequate | Event version freshness, replay fences on reviewed CMS/navigation/footer paths, tenant-lineage fail-closed behavior for CMS/footer, tombstones for CMS, cache invalidation | Opportunistic | Do not block product work on these; shared tombstone/delete guard is a good low-risk adoption later. |
| `SmartOfferProjectionConsumer` | 2 | Offer/display visibility | Light | Basic projection updates | Opportunistic / low | Add version metadata only if offers become checkout authority. |
| `CommerceHubSearchIndexService`, `MarketplaceSearchService`, service discovery/provider trust projections | 2 | Discovery/search visibility | Manual adequate | Search index updates, provider trust/version projections | Opportunistic | SQL overlay only where price/stock/tenant authority enters search documents. |
| `ProviderTrustConsumer` and `ProviderTrustProjectionConsumer` | 2 | Service trust/discovery visibility | Manual adequate | Central event publisher authority, projection event version checks | Opportunistic | Not a priority unless trust scoring begins affecting money/fulfillment gates. |
| `CheckoutSessionProjectionConsumer` and `CheckoutSessionOrderConversionConsumer` | 2 | Checkout/session continuity visibility | Manual adequate | Session idempotency, duplicate session reconciliation, local freshness | Opportunistic | Session projection can remain specialized; avoid confusing it with order authority. |
| Abandoned cart consumers and `N8nRecoveryOrchestrator` | 2 | External recovery messaging/automation | Manual adequate | Idempotency keys, token validation, telemetry duplicate collapse | Opportunistic | Duplicate side-effect collapse matters, but business authority remains checkout/order SQL. |
| WhatsApp OTP/order/payment notification consumers | 2/3 | Transport notification side effects | Manual adequate | Non-authoritative event classification, rate limits, transport retries | Opportunistic only for duplicate-message pain | Do not treat notification delivery as business truth. |
| `RedisInvalidationConsumer` | 2 | Homepage, CMS, footer, marketplace product/order/review cache invalidation | Strong on reviewed cache fanout | Mandatory SQL context, `ProjectionEventFence` for reviewed homepage/CMS/footer/marketplace cache events, tenant-aware fence telemetry for marketplace-domain events | Migrated for POS-ADMIN-CERTIFICATION-01 PASS 7 follow-up | Cache invalidation remains best-effort derivative infrastructure; future cache warmers, feed/export cache writers, support-cache tooling, and analytics/reporting cache rebuild paths remain separate review targets. |
| `CommissionRulesCacheInvalidationConsumer` | 3 | Commission-rule cache invalidation | Light | Best-effort key deletion | Leave legacy for now | Duplicate invalidation is normally harmless; add fence if invalidation becomes externally costly or promoted to a reviewed recovery primitive. |
| `AnalyticsEventConsumer`, affiliate analytics consumers, daily analytics projections, CommerceHub search analytics background service | 3 | Analytics/BI/telemetry | Light | Payload guards, rate limits, eventual aggregation | Remain legacy | Eventual convergence is acceptable; over-hardening would add low-value complexity. |
| Financial dashboards/read services and daily financial aggregates | 3 with Tier 1 inputs | Operator visibility over SQL/ledger | Manual adequate | SQL reads, aggregation, reconciliation reports | Remain legacy unless writing derived projections | Must never become ledger authority; use SQL/ledger as read source. |
| Marketplace review consumers and aggregate rebuild services | 3/2 | Review/display reputation visibility | Manual adequate | Idempotent rebuild by review event, aggregate recomputation | Opportunistic only | Tier rises if review trust starts gating payouts or seller authority. |

## Migration Strategy

### Immediate Migration Candidates

Migrate or finish migrating these when making the next touched change in the area:

- Category import/admin repair tools, category mutation event handlers, and category cache invalidation: PRODUCT-CON-03 adds `CategoryLineageAuthority` and hardened storefront/search/affiliate/CommerceHub consumers, but future category writers must keep recomposing SQL lineage and tombstoning invalid descendants rather than trusting projection paths.
- `StorefrontConfigProjectionConsumer`: duplicate Mongo/Redis side-effect fencing and runtime dangerous-primitive declarations are now in place; future changes should focus on shared version validation only where it preserves specialized publish/rollback semantics.
- Remaining `CommerceHubProjectionConsumer` promotion/policy refresh branches: store/shipping/currency metadata fan-out is now fenced and store name is SQL-recomposed; add shared fences only where duplicate delivery can still trigger repeated search writes or external sync.
- Remaining promotion activation/expiration paths: add `ProjectionEventFence` around overlay, category banner, search decoration, and cache side effects after preserving current SQL recomputation.
- Marketplace projection rebuild fallback: product availability rebuild now has explicit `SqlTruthRebuildGuard.RequireSqlAuthority(...)`; add the same entrypoint assertion to remaining order/homepage/read-model repair paths when touched, without changing replay semantics.

### Opportunistic Migration Candidates

- CMS, navigation, footer, homepage, service discovery, provider trust, checkout-session, abandoned-cart, and review projections.
- Generic product search create/update/delete paths that do not carry price, stock, variant identity, media, or promotion authority.
- Cache invalidation paths where duplicate deletion is harmless but side-effect telemetry needs cleaner accounting.

### Intentionally Legacy For Now

- Payment lifecycle, provider webhooks, unresolved payment reconciliation, refunds, payouts, affiliate settlement, accounting outbox, stock mutation, inventory reconciliation, fulfillment lifecycle, shipment authority recovery, and critical order read repair.
- These flows already use lifecycle, transaction, readiness, idempotency, lease, or evidence-based protections that are more specific than projection primitives.
- CHECKOUT-CON-10, CHECKOUT-CON-13, and ORDER-CON-01 intentionally keep fulfillment/shipment hardening in lifecycle/shipment/carrier services and the explicit shipment-lineage handoff instead of migrating it into generic projection consumers: current mutation paths recompose SQL authority under serializable/locked boundaries, carrier success persistence reloads locked SQL shipment/order truth after the external call before applying local success, and shipment-to-order/storefront fanout now reloads current SQL lineage while callback ingress remains domain-specific evidence handling.

### Manual Protections To Keep Temporarily

- Mongo array filters for embedded variants and wishlist/order subdocuments.
- Storefront and CommerceHub order repair freshness selection.
- Financial readiness gates and reconciliation-required states.
- SQL outbox ordering and head-of-line blocking for stock and marketplace processors.
- Provider and payout evidence uniqueness/allocation logic.

### Mature Infrastructure

- `ProjectionEventFence`: mature for duplicate/active EventId collapse when an `ApplicationDbContext` is available and the consumer can complete only after derivative side effects are safe.
- `ProjectionConsumerEventFence`: mature compatibility facade for existing consumers.
- `ProjectionAuthorityValidator`: mature for version/timestamp checks where SQL version and event version semantics are explicit.
- `ProjectionTombstoneGuard`: mature for simple delete-resurrection checks.
- `ProductSearchSqlAuthorityOverlay`: mature for product search hydration across variant, pricing, stock, media, promotion, and tombstone visibility.
- `IProductSqlAuthorityProjectionWriter`: mature for storefront `ProductDocument` SQL recomposition + convergence decisions across create/update/lifecycle/admin rebuild paths.
- `IStorefrontProductProjectionMutationBoundary`: mature as the reviewed runtime wrapper for storefront product full-document upsert and visibility tombstone primitives.
- `ProductStorefrontVisibilityAuthority`: mature as the named storefront/affiliate-public pricing authority helper for product-shaped Mongo read models.
- `ProductVariantInventoryLineageAuthority`: mature for inspected stock mutation, reservation consumption, CommerceHub payment/delivery validation, checkout guards, and stock-sync stale fanout suppression.
- `OrderFulfillmentLineageAuthority`: mature for inspected shipment-to-order status synchronization, storefront shipment-facet replay collapse, and storefront order rebuild shipment visibility overlays.
- `SqlTruthRebuildGuard`: mature as a small entrypoint assertion that SQL authority was deliberately required.

### Still Experimental Or Too General

- A universal `ISqlAuthorityOverlay<TProjection>` implementation for every document type. Current maturity is product-search-specific, not a general rebuild framework.
- Generic base consumer classes. The current system has too many specialized aggregate shapes, embedded documents, and side-effect boundaries for a safe mega-framework.
- A universal rebuild engine. Order repair, promotion repair, marketplace rebuild, stock compensation, and financial reconciliation have distinct authority semantics.
- Automatic analyzer enforcement for every consumer. Start with review rules and focused source tests; add analyzers only after false-positive boundaries are understood.

## Reliability Enforcement Checklist

Use this checklist for every new consumer, rebuild flow, replay handler, synchronization path, or external propagation path.

1. SQL authority: identify the PostgreSQL row/table that owns truth. If none exists, document why the path is Tier 2 or Tier 3.
2. Event trigger only: confirm the payload is not treated as final authority for money, stock, price, variant identity, commission, fulfillment, payout, or tenant scope.
3. Replay fencing: use `ProjectionEventFence` or document the existing durable equivalent.
4. Duplicate side-effect collapse: prove duplicate delivery cannot duplicate external messages, search writes that matter, cache-visible state, payout/refund execution, stock movements, or accounting rows.
5. Stale replay rejection: compare event version and timestamp against SQL authority or stored projection freshness.
6. Rebuild-from-SQL: repair/rebuild paths must derive current visibility from PostgreSQL, not Mongo/search/cache.
7. Tombstone handling: stale create/update events must not resurrect deleted/archived visibility.
8. External payload distrust: provider, marketplace, affiliate, storefront, and browser payloads are evidence or intent only; re-read SQL before committing authority.
9. Tenant scope: require trusted tenant scope and explicit affected-tenant arrays for multi-store events.
10. Side-effect amplification: identify non-idempotent calls, notifications, search sync, cache warming, external provider calls, and downstream propagation.
11. Completion boundary: do not mark processed until required side effects are either complete, durably staged, or deliberately safe to retry.
12. Validation: add focused deterministic xUnit or source guard tests for the exact replay/stale/duplicate/rebuild property changed.

## Future Guardrails

- New Tier 1 visibility consumers must start from `ProjectionEventFence`, `ProjectionAuthorityValidator`, `ProjectionTombstoneGuard`, and SQL reload/overlay unless the PR documents a stronger local equivalent.
- New storefront `ProductDocument` create/update/lifecycle/rebuild writers must use `IProductSqlAuthorityProjectionWriter` and keep low-level upsert/tombstone calls behind `IStorefrontProductProjectionMutationBoundary`; reintroducing optional non-SQL fallback constructors or copy-pasted overlay/tombstone/upsert sequences is not allowed.
- Shared storefront product overlay/rebuild paths must recompose `DefaultUnit`, `Units`, purchase price, and simple-product purchasability semantics from SQL. A simple product without a current default unit must fail closed to non-purchasable visibility rather than falling back to legacy product-level price.
- Overlay-only storefront product refreshes such as promotions must use field-scoped Mongo updates; direct full `ProductDocument` replacement outside `ProductReadRepository` internals, `IProductSqlAuthorityProjectionWriter`, or `IStorefrontProductProjectionMutationBoundary` is not allowed.
- New rebuild and repair entrypoints must call `SqlTruthRebuildGuard.RequireSqlAuthority(...)` or explain why they are Tier 3 soft-state rebuilds.
- Reviewers should reject new consumer-local freshness code in Tier 1 unless the local shape is genuinely domain-specific and a follow-up migration note is added here.
- Keep architecture tests source-based and focused. They should assert high-risk consumers use shared primitives where already migrated and assert this strategy document remains present.
- Prefer small extension helpers around repeated exact patterns. Do not create a base consumer hierarchy until at least three migrated consumers share the same transaction, completion, and side-effect semantics.
- When a consumer has optional `ApplicationDbContext` injection for compatibility, new Tier 1 registration paths should provide it. Optional construction should only remain for tests or legacy wiring.

## Non-Goals

- Do not migrate every consumer in one sprint.
- Do not replace financial lifecycle, accounting, refund, payout, inventory mutation, fulfillment, or provider evidence protections with projection primitives.
- Do not build a generic projection framework or inheritance tree.
- Do not harden Tier 3 analytics/BI/telemetry beyond reasonable idempotency and payload validation.
- Do not require Mongo/search/cache/storefront/affiliate/CommerceHub to become authoritative.
- Do not rewrite stable manual order repair and financial reconciliation paths solely to use new names.

## Technical Debt Tracking

### Immediate Risk

- Category hierarchy adoption is partially hardened, not complete: admin sync/search sync, storefront category reads, product default-category mutation, affiliate category fan-out, and CommerceHub category path propagation now recompose or validate SQL lineage, but category mutation/import/cache tooling still needs explicit review before it can be called uniformly migrated. `Categories` still lack a dedicated monotonic projection mutation version.
- Media propagation is runtime-hardened for storefront product documents, product search, CommerceHub listing/search images, and affiliate public/intelligence images, but no dedicated CDN purge/invalidation worker exists in this codebase. CDN/browser cache expiry remains operational-only debt, and any future image optimizer, CDN purge worker, bulk importer, or direct media repair tool must validate SQL media authority before side effects.
- Tier 1 visibility paths that still do not use shared fences: storefront config and some CommerceHub store-config/shipping refresh branches. Coupon, shipping-method, and product cache invalidation now use event fences on their runtime visibility/fanout paths.
- Promotion projection remains high-risk and partially local because activation/expiration affects pricing overlays, search decoration, cache visibility, and storefront banners.
- The focused dangerous-primitive governance guard is now aligned and green again. `CrossSystemAuthorityRegressionGuardTests` explicitly governs reviewed direct-SQL/bulk mutation files, high-risk Tier-1 `ReplaceOneAsync` paths, raw Elasticsearch client writes, and Redis pattern invalidation fanout through allowlists plus dedicated reviewed-pattern assertions. This restores enforcement credibility on the inspected boundary, but it is still not a universal analyzer or blanket ban.
- Marketplace projection SQL rebuild fallback now covers product, order, and tenant-scoped homepage entrypoints; global homepage/read-model repair remains aggregate visibility and should get explicit guard semantics only when a concrete tenant/entity authority shape exists.
- PRODUCT-CON-06 homepage visibility is hardened for inspected storefront and CommerceHub homepage paths, but not a universal recommendation architecture. Future personalized recommendation workers, ranking snapshot stores, external feed exporters, homepage warmers, or delayed recommendation retry workers must add SQL recomposition and replay collapse before rollout.
- PRODUCT-CON-07 publication visibility is hardened for inspected storefront lifecycle, search, affiliate public/intelligence, and CommerceHub listing/search paths, but not a universal publication framework. Future bulk product import, direct admin repair, external feed/export publication writers, marketplace sync writers, or delayed publication retry workers must emit the publication lifecycle trigger or directly recompose/tombstone from current SQL product/listing publication authority.
- PRODUCT-CON-09 closes the inspected AdminSync product repair resurrection path by using the product SQL authority overlay and tombstones. Recommendation/rebuild safety is still incremental: new personalized recommendation engines, ranking snapshots, cache warmers, feed exporters, and direct repair endpoints must not copy AdminSync's old full-replace pattern or treat Mongo/ES/cache documents as rebuild authority.
- The 2026-05-21 consolidation follow-up closes the inspected shared storefront completeness regression: `ProductSearchSqlAuthorityOverlay` now recomposes `DefaultUnit` / `Units`, simple-product storefront purchasability, purchase price, and structured metadata from SQL, and product-promotion refresh no longer full-replaces `ProductDocument`. This remains a storefront-product rule, not a blanket guarantee for every future product-shaped projection.
- PRODUCT-CON-10 closes the inspected CommerceHub autocomplete/suggestion response replay path by SQL-revalidating cached and projected suggestion responses before return. Dedicated standalone completion indexes, keyword-cache rebuild jobs, trending workers, storefront browser autocomplete caches, feed/export completion writers, and delayed suggestion retry workers remain incremental and must adopt the same SQL recomposition/tombstone rule if introduced.
- PRODUCT-CON-11A closes inspected archived/detached variant resurrection paths by adding `ProductVariantLifecycleAuthority` and applying SQL public-lineage recomposition to storefront cart stock, product search/storefront recomposition, `ProductVariantProjectionConsumer`, CommerceHub cart/checkout/listing reconciliation, and affiliate intelligence stock recomposition. Future import/clone/merge/split, feed/export, recommendation, direct repair, standalone suggestion, cache warmer, or external sync code must adopt the same SQL lifecycle rule before mutating variant-shaped public visibility.
- PRODUCT-CON-11B closes inspected clone/import semantic carryover on product and variant create/update surfaces by adding `ProductVariantCloneImportAuthority` for deterministic SKU regeneration, barcode conflict rejection, same-product media inheritance, simple-product identity mirroring, and duplicate initial-stock warehouse rejection. Future dedicated import/clone/bulk catalog writers remain incremental debt and must adopt the same helper plus replay/idempotency coverage.
- PRODUCT-CON-11C closes inspected merge/split semantic ownership gaps by adding SQL semantic lifecycle metadata and `ProductVariantMergeSplitAuthority`, then applying current-owner fail-closed checks to lifecycle, ownership, stock/reservation, admin mutation, search, CommerceHub, and payment-recovery authority paths. Future explicit merge/split orchestration, supplier deduplication, physical stock transfer, and reservation migration remain incremental debt and must adopt the helper plus transaction/replay coverage.
- PRODUCT-CON-11D closes inspected stale reservation and detached stock fanout paths by adding `ProductVariantInventoryLineageAuthority`, then applying current inventory-lineage checks to reservation creation/consumption, stock mutation, checkout/CommerceHub payment and delivery validation, and stock-sync outbox replay suppression. Future warehouse reassignment, physical stock transfer, reservation migration, feed/export stock writers, and bulk inventory import/reconciliation workflows remain incremental debt and must adopt the helper plus transaction/replay/rebuild coverage.
- PRODUCT-CON-11E closes inspected derivative resurrection paths where CommerceHub search/reindex, marketplace product/homepage rebuilds, and storefront homepage cached cards could use local active-variant or raw stock evidence after SQL semantic ownership changed. These live export-like surfaces now recompose public lifecycle and inventory lineage from SQL, scrub/tombstone stale rebuild results, and leave future dedicated feed/export/cache-warmer/recommendation/external-sync writers as explicit incremental debt.
- PAYMENT-CON-01 closes the inspected refund/settlement visibility fanout and storefront order payment-facet replay gap by keeping canonical payment-status fanout in `PaymentLifecycleService` and recomposing payment replay from current SQL order payment lineage. CommerceHub payment visibility, affiliate/accounting/reporting exports, provider reconciliation exports, support/admin caches, notification retries, and future payment cache warmers remain explicit review boundaries if they derive status from payloads.

### Medium-Term Risk

- Product search, affiliate, CommerceHub, and promotion consumers still mix shared primitives with local freshness and Mongo merge logic. PRODUCT-CON-05 closes the inspected storefront product search lifecycle paths, and PRODUCT-CON-10 closes the inspected CommerceHub suggestion response boundary, but affiliate/CommerceHub promotion-policy refreshes and any future dedicated autocomplete/suggestion indexes must preserve SQL-first search authority explicitly.
- Order projection repair is strong but manually separate from `SqlTruthRebuildGuard`, making future rebuild paths easy to implement inconsistently.
- Cache invalidation still has uneven event fencing outside the product cache consumer. Most duplicate invalidations are harmless, but future CDN purge, cache warmer, feed/export, CMS/navigation/footer, or bulk repair fanout needs explicit replay collapse if side effects become costly.
- Optional `ApplicationDbContext` construction in some consumers means a path can lose shared fencing if registered without SQL context. `ProductCacheInvalidationConsumer` no longer has that optional downgrade.
- Direct full-document `ReplaceOneAsync` remains a dangerous primitive across the wider projection layer. The storefront product path is writer-governed now, and the focused guard now fences reviewed Tier-1 `ReplaceOneAsync` files, but there is still no repo-wide analyzer or universal architecture test that makes every specialized full-document replace automatically safe.
- Approved exceptions should remain local and explicit rather than generalized. `ServiceDiscoveryProjectionConsumer` is a reviewed raw-ES visibility exception backed by separate stale-event/no-op architecture assertions, and `DevelopmentCommerceHubSeedService` is a development-only full-replace exception that stays acceptable only while it remains development-scoped and routes search writes through `ICommerceHubSearchIndexService`.

### Acceptable Legacy Debt

- Analytics, BI, dashboards, search analytics, review aggregates, service discovery, basic CMS/navigation/footer projections, WhatsApp notifications, abandoned-cart recovery, and benign cache invalidation can remain lighter-weight.
- Financial lifecycle and reconciliation code should remain specialized because provider evidence, readiness gates, ledger posting, and payout/refund execution are not projection problems.
- Stock mutation and outbox processors should remain SQL/outbox-specific; projection primitives belong downstream.

## Final Review

Reliability infrastructure considered mature:

- `ProjectionEventFence`
- `ProjectionConsumerEventFence`
- `ProjectionAuthorityValidator`
- `ProjectionTombstoneGuard`
- `ProductSearchSqlAuthorityOverlay`
- `ProductVariantLifecycleAuthority`
- `ProductVariantInventoryLineageAuthority`
- `SqlTruthRebuildGuard` as a lightweight entrypoint assertion

Infrastructure still experimental:

- General-purpose `ISqlAuthorityOverlay<TProjection>` outside product search
- Universal rebuild engines
- Generic base consumers
- Analyzer-level enforcement across all consumers

Consumers fully migrated or mature enough on the inspected high-risk slice:

- `ProductMediaChangedEventConsumer`
- `ProductStockChangedConsumer`
- `ProductStockPriceSearchIndexConsumer`
- `CouponProjectionConsumer`
- `ProductCacheInvalidationConsumer` on product create/update/delete/stock/price/publication cache fanout
- Product search overlay hydration paths using `ProductSearchSqlAuthorityOverlay`

Consumers partially migrated:

- Category hierarchy/lineage consumers and rebuilds: PRODUCT-CON-03 hardened admin sync/search sync, storefront category reads, product category mutation, affiliate category fan-out, and CommerceHub category path propagation, but broader category mutation/import/cache tooling remains incremental.
- `ProductVariantProjectionConsumer`
- `CommerceHubProjectionConsumer`
- `AffiliateProductIntelligenceProjectionConsumer`
- `AffiliateProductPolicyProjectionConsumer`
- `ProductVisibilityLifecycleProjectionConsumer` is migrated for storefront publication/delete/archive lifecycle visibility, but the wider publication architecture is still incremental.
- `PromotionProjectionConsumer`
- `ProductPromotionSearchIndexConsumer`
- Product search create/update/delete/archive/publication consumers on the inspected Elasticsearch path
- `CommerceHubQueryService` autocomplete/suggestions on the inspected cached/ES/Mongo response path
- Storefront/CommerceHub homepage product-card and curated-reference paths on the inspected PRODUCT-CON-06 slice
- `ShippingMethodProjectionConsumer`
- `TenantCurrencyProjectionConsumer` is migrated for storefront currency replay collapse; adjacent CommerceHub/affiliate fan-out is SQL-reloaded but still locally guarded.

Consumers intentionally left legacy:

- Accounting outbox, financial readiness, payment/refund/payout recovery, affiliate settlement, stock mutation, inventory reconciliation, fulfillment/shipment authority, order projection repair, CMS/navigation/footer/homepage, analytics, WhatsApp, abandoned-cart, review and discovery flows.

Dangerous remaining gaps:

- Tier 1 checkout-visibility projections are not uniformly migrated to shared fences and validators; tenant-currency, coupon, payment-method, shipping-method, storefront-config, and CommerceHub store/shipping/currency metadata visibility now have runtime fences/reloads, but selected CommerceHub promotion/policy and domain-local refresh paths remain mixed.
- Promotion repair/projection remains a high-value next target.
- Marketplace rebuild fallback now has explicit SQL authority guard usage for product, order, and tenant-scoped homepage entrypoints; global homepage/read-model repair still has partial guard semantics because it is aggregate visibility rather than a single entity boundary.
- Optional SQL context wiring can silently reduce fencing coverage where legacy constructors remain.
- Recommendation visibility beyond the current homepage/product-section implementation is still future-work territory. There is no blanket guarantee for a future ML/personalization/ranking snapshot pipeline unless it adopts PRODUCT-CON-06 SQL recomposition, tombstone/delete convergence, and replay collapse explicitly.

Acceptable long-term technical debt:

- Tier 3 analytics and soft-state projections can remain eventually consistent.
- Lifecycle and financial authority flows should remain bespoke and SQL/evidence-driven.
- Local Mongo array filters and order repair freshness selection are acceptable until a narrow helper can represent them without weakening semantics.

Conclusion:

The system now has a realistic enterprise reliability adoption strategy, but reliability adoption is still partial. The strategy is no longer ad hoc for future work because Tier 1 migration targets, mature primitives, manual exceptions, non-goals, and debt categories are explicitly documented. Runtime adoption remains mixed and must be advanced incrementally, not declared complete.

Final maturity position:

- The inspected architecture is production-grade for scale on its hardened authority paths.
- Remaining highest-value work is governance and operational convergence, not a rewrite or a push for perfect consistency.

## 2026-05-21 Affiliate/Public Pricing Convergence Adoption Note

New Tier 1 rule added on this date:

- Affiliate/public runtime pricing is now an inspected Tier 1 visibility boundary whenever storefront pricing is unit-aware.
- Shared storefront-visible pricing helpers should be preferred over affiliate/public local price math.
- `DefaultUnit` plus current SQL `ProductUnits` are authority-critical for simple-product online visibility, and affiliate/public paths must fail closed when that authority is missing.
- Public affiliate search/detail, affiliate discovery/intelligence, and category-count surfaces should require both valid storefront-authority price and live stock authority on hardened paths.

Adoption status:

- `AffiliateProductPolicyProjectionConsumer`, `AffiliateProductIntelligenceProjectionConsumer`, `AffiliateProductDiscoveryService`, `PublicAffiliateProductsController`, and inspected `AffiliatePortalController` discovery/landing/detail paths now follow the storefront-authority convergence rule.
- `ProductDocument` now carries shared affiliate/public pricing helpers so future hardened consumers can reuse the same storefront semantics.

Remaining debt:

- This is still incremental. CommerceHub pricing/search retains its own SQL-authoritative listing model rather than the storefront helper model.
- Future affiliate/public feed exporters, recommendation workers, custom rebuild tools, and direct Mongo/search repair scripts must explicitly adopt the same convergence rule; the current strategy does not make that automatic.

## 2026-05-21 Governance Extraction Adoption Note

New Tier 1 rule added on this date:

- Storefront `ProductDocument` mutation must pass through the extracted runtime boundary: `IProductSqlAuthorityProjectionWriter` for SQL recomposition and `IStorefrontProductProjectionMutationBoundary` for the low-level reviewed product upsert/tombstone primitive.
- Direct product repository calls to `UpsertAsync` and `TombstoneIfFreshAsync` are intentionally contained dangerous primitives. They may remain in `ProductReadRepository`, but runtime Tier 1 product projection code must not call them outside `StorefrontProductProjectionMutationBoundary`.
- `StorefrontProductProjectionMutationBoundary` must keep typed mutation intent (`ReviewedFullDocumentUpsert`, `ReviewedVisibilityTombstone`), tenant/product/event validation, review reason validation, and active/online-visible upsert validation.
- Storefront-visible pricing helpers are now extracted into `ProductStorefrontVisibilityAuthority`; future storefront/affiliate/public product-shaped runtime paths should use those helpers instead of local raw `EffectivePrice` / `Price` / `OnlinePrice` logic.

Adoption status:

- `ProductSqlAuthorityProjectionWriter` now uses `IStorefrontProductProjectionMutationBoundary` after SQL overlay recomposition.
- `ProductDocument` delegates storefront and affiliate/public pricing helpers to `ProductStorefrontVisibilityAuthority`.
- `GlobalReliabilityInfrastructureExtractionTests` and `EventOutboxConsistencyArchitectureTests` now assert the extracted mutation boundary and block direct product repository upsert/tombstone usage outside the boundary.

Remaining debt:

- This is not a universal projection mutation engine. CommerceHub product/listing documents, affiliate documents, order repair, promotion overlays, CMS/navigation/footer projections, and Tier 3 analytics still retain domain-specific mutation patterns.
- Source guards are still focused tests, not compiler-enforced purity. Future new dangerous primitives must extend the runtime boundary or add an explicitly reviewed local boundary with tests and documentation.

## 2026-05-22 Variant Lifecycle Resurrection Adoption Note

New Tier 1 rule added on this date:

- Public variant lifecycle and purchasability require current SQL `ProductVariants` plus same-tenant parent `Products` lifecycle authority.
- `ProductVariantLifecycleAuthority` is the preferred helper for runtime public-lineage decisions. It covers active/non-deleted variant state, non-empty parent linkage, same-tenant non-deleted parent product, and active/online/published parent product state.
- Rebuild, replay, retry, and recovery paths may use stale payloads as triggers only. They must fail closed to no stock, no purchasability, tombstone/delete, or no-op when SQL no longer proves variant lineage.

Adoption status:

- Storefront cart stock authority, product search/storefront recomposition, `ProductVariantProjectionConsumer`, CommerceHub cart/checkout/listing reconciliation, CommerceHub stock/price refreshes, and affiliate intelligence stock recomposition now use SQL variant lifecycle recomposition on inspected paths.
- Focused runtime tests cover parent archive, detached/orphan lineage, inactive variant visible-price fallback, and fail-closed variant pricing when no active priced variant remains.

Remaining debt:

- This is not a universal feed/export/recommendation/import/clone framework.
- Future bulk catalog operations, import/clone/merge/split workflows, standalone suggestion indexes, recommendation workers, CDN/cache warmers, direct repair scripts, and external marketplace sync writers must adopt `ProductVariantLifecycleAuthority` or an equivalent SQL recomposition boundary with focused replay/rebuild tests.

## 2026-05-22 Variant Clone / Import Replay Adoption Note

New Tier 1 rule added on this date:

- Product and variant clone/import payloads are mutation intent, not semantic ownership authority.
- `ProductVariantCloneImportAuthority` is the preferred helper for runtime SKU regeneration, barcode ownership rejection, same-product media inheritance validation, and simple-product product/default-variant identity mirroring.
- Clone/import create paths may regenerate SKU deterministically when SQL says the requested value is already owned. Barcode conflicts fail closed. Initial stock imports must not contain duplicate warehouse rows. Public visibility still requires `ProductVariantLifecycleAuthority`.

Adoption status:

- `ProductsController`, `ProductVariantsController`, and `ProductVariantService` now use the helper on inspected create/update and default-variant synchronization paths.
- Focused tests cover SKU regeneration from stale clone payloads, product-level semantic duplicate detection, barcode carryover rejection, and media inheritance validation.

Remaining debt:

- This is not a standalone import orchestration framework.
- No dedicated CSV/Excel/ERP/supplier import or named product-clone endpoint was present in the inspected backend. Future explicit import/clone/template/bulk catalog workers must adopt this authority helper, use request/job idempotency for replay collapse, and emit the existing SQL-authoritative product/variant/publication/stock/media events instead of mutating projections directly.

## 2026-05-22 Variant Merge / Split Semantic Adoption Note

New Tier 1 rule added on this date:

- Product variant merge/split lifecycle is SQL-authoritative through `ProductVariants.SemanticLifecycleState` and replacement/lineage/operation metadata.
- `ProductVariantMergeSplitAuthority` is the preferred helper for current semantic owner checks, merge/split operation replay collapse, stale replay rejection, superseded-source identity clearing, split-parent retirement, lineage stamping, and open-reservation rejection.
- Superseded variants must fail closed for public lifecycle, SKU/barcode ownership, stock mutation, reservation creation, product search, CommerceHub visibility, vendor visibility, pricing/media/publication inheritance, and external/feed identity.

Adoption status:

- `ProductVariantLifecycleAuthority`, `ProductVariantCloneImportAuthority`, admin product/variant mutation paths, simple/default variant alignment, stock mutation support, storefront checkout stock repository, CommerceHub reservation/payment recovery authority, CommerceHub vendor visibility, and product search SQL recomposition now include current semantic owner checks.
- Focused runtime tests cover stale merge source public lifecycle, clone/import SKU reuse after merge, split-parent reservation rejection, merge operation replay collapse, and open-reservation supersession rejection.

Remaining debt:

- This is not a full merge/split orchestration framework.
- No dedicated admin merge/split, supplier deduplication, automatic duplicate cleanup, or bulk consolidation workflow was present in the inspected backend. Future workflows that move physical stock or migrate reservations must implement deterministic SQL stock locking, exact reservation lineage transfer or rejection, post-commit event fanout, and replay/rebuild tests.

## 2026-05-22 Variant Inventory / Reservation Lineage Adoption Note

New Tier 1 rule added on this date:

- Variant inventory lineage and reservation consumption require current SQL `ProductVariants`, parent `Products`, active `Warehouses`, `ProductVariantStocks`, and active unexpired `InventoryReservations`.
- `ProductVariantInventoryLineageAuthority` is the preferred helper for runtime inventory-reservable lineage, current stock-owner filtering, stock-mutation ownership, and consumable reservation filtering.
- Reservation cleanup/release can inspect stale rows to repair them; checkout, payment, fulfillment, recovery, and stock consumption cannot.
- Stock-sync outbox replay must suppress stale or detached availability fanout when current SQL variant/warehouse/stock lineage or event freshness no longer proves ownership.

Runtime adoption:

- `ProductVariantStockRepository`, `ProductVariantStockMutationSupport`, `CheckoutInventoryAuthorityGuard`, `InventoryConsistencyService`, and `StockSyncOutboxProcessor` now use current inventory lineage on inspected mutation, consumption, payment, fulfillment, and fanout paths.
- PASS 3 addendum on 2026-05-27 extends that reviewed adoption to legacy availability reads and derivative stock-total recomposition on inspected surfaces. `ProductVariantStockRepository`, `ProductVariantProjectionConsumer`, and `CommerceHubProjectionConsumer` now also converge reviewed availability and total-stock projection values through current stock-owner lineage instead of summing undeleted stock rows directly.
- PASS 3 addendum on 2026-05-27 also extends the reviewed adoption to derivative SQL stock snapshots and terminal replay compensation. `CartService`, `AffiliateProductIntelligenceProjectionConsumer`, and `ProjectionFailureCompensationService` now recompose stock only through current stock-owner lineage before preserving cart quantity, affiliate stock visibility, or a replacement stock-sync replay snapshot.
- Focused validation covered expired reservation consumption replay collapse, inactive warehouse reservation rejection, CommerceHub stock-authority loss, variant lifecycle authority, and checkout hardening source guards.

Remaining debt:

- No universal inventory migration framework was created. Future merge/split stock transfer, reservation migration, warehouse reassignment, stock feed/export, bulk inventory import, and specialized reconciliation workers must define transaction boundaries, lock order, replay collapse, derivative fanout suppression, and rebuild tests before rollout.

## 2026-05-22 External Derivative Replay Adoption Note

New Tier 1 rule added on this date:

- Product feed/export eligibility, search/reindex recovery, autocomplete/suggestion candidates, recommendation/homepage rebuilds, cache hydration, and external sync payloads are derivative visibility boundaries.
- Variant-shaped public derivative emission must prove current SQL publication and lifecycle through `ProductVariantLifecycleAuthority` and prove outward stock availability through `ProductVariantInventoryLineageAuthority`.
- Delayed fanout and stale snapshots are recomposition triggers only. Replay must collapse to current SQL visibility by upserting current state, deleting/tombstoning stale state, scrubbing curated references, emitting zero availability, or no-oping.

Runtime adoption:

- CommerceHub search overlay/reindex now use current public variant lifecycle and current inventory stock-owner semantics for listing emission and rebuild verification.
- Marketplace product rebuilds delete variant-product visibility when no current public semantic-owner variant remains, marketplace homepage curated listing rebuilds scrub superseded listing ids, and storefront homepage cached product cards recompose current SQL variant lifecycle and stock before return.
- CommerceHub autocomplete/suggestion paths already revalidate through the CommerceHub SQL overlay, so these updated overlay semantics also fence stale completion cards on the inspected response boundary.

Remaining debt:

- No standalone backend affiliate feed, supplier export, catalog/inventory/pricing feed, marketplace export, or external product-sync writer was found in this sweep. The live export-like surfaces are hardened; missing future writers are not implicitly safe.
- Future cache warmers that hydrate product visibility, external sync workers, analytics/reporting public projections, standalone completion indexes, personalized recommendation engines, and direct read-model/search repair tools must add explicit SQL recomposition, tombstone/delete convergence, and replay/rebuild coverage.

## 2026-05-22 Payment / Callback / Settlement Replay Adoption Note

New Tier 1 rule added on this date:

- Payment callback, refund, and settlement evidence may trigger SQL lifecycle work, but visible order payment status belongs to current SQL lifecycle truth.
- Accepted refund and COD/gateway settlement transitions must stage canonical payment-status fanout from `PaymentLifecycleService`; retry or reconciliation consumers must not synthesize competing status fanout from payloads.
- Storefront order payment-facet replay must use `OrderPaymentLineageAuthority` or an equivalent reviewed SQL boundary before mutating derivative visible payment status.

Runtime adoption:

- `RecordRefundAsync`, `RecordCodSettlementAsync`, and `RecordGatewaySettlementAsync` now fan out `OrderPaymentStatusChangedEvent` with explicit internal refund/settlement source lineage only when SQL payment status changes.
- Existing refund-id and settlement-id duplicate collapse now also prevents duplicate visible payment-status transitions.
- `OrderProjectionConsumer` now collapses stale `OrderPaymentStatusChangedEvent.NewStatus` against current SQL order payment truth without appending conflicting payload-derived timeline narrative; when current SQL order lineage is missing it deletes tenant-scoped derivative order/cache visibility rather than retaining stale payment state.

Remaining debt:

- CommerceHub payment read models, affiliate/accounting/reporting exports, support/admin caches, notification retry workers, provider settlement/reconciliation exports, callback adapters, and payment cache warmers remain specialized paths that must document SQL payment/refund/settlement lineage before projecting payment status from event or provider payloads.
- Focused lifecycle integration tests were added to `EcommerceRefundLifecycleTests`, but that SQLite fixture currently fails at schema creation before payment test bodies run. Do not treat that verification gap as proof of payment runtime coverage until the fixture schema issue is repaired and rerun.

## 2026-05-22 Accounting / Ledger Replay Adoption Note

New Tier 1 rule added on this date:

- Accounting outbox payloads are triggers, not ledger authority.
- `AccountingLedgerAuthority` is the preferred helper for inspected order collection, e-commerce refund, and COD/gateway settlement replay decisions.
- Current SQL order payment/fulfillment truth, current `OrderRefund` lineage, current `PaymentSettlementBatch` lineage, existing journal idempotency, and reconciliation state decide whether replay posts, returns an existing journal, collapses to no-op, or remains retryable.

Runtime adoption:

- `AccountingOutboxProcessor` now gates online payment, wallet, and COD delivery journal dispatch through current SQL accounting authority before revenue recognition.
- E-commerce refund journal dispatch now posts current SQL refund amount and settlement context instead of replay payload values.
- COD/gateway settlement journal dispatch now posts current SQL settlement batch gross, fees, provider, and settlement id instead of replay payload values.
- Focused `AccountingOutboxProcessorTests` cover stale terminal payment replay collapse, refund amount recomposition, settlement amount recomposition, existing idempotency behavior, and critical-message retry retention.

Remaining debt:

- Financial lifecycle and reconciliation code remains specialized and should not be forced into generic projection primitives.
- CommerceHub vendor finance dashboards, affiliate accounting exports, tax/reporting snapshots, BI aggregates, payout export files, support/admin financial caches, provider reconciliation exporters, and future accounting cache warmers still require explicit SQL ledger/accounting recomposition before exposing derivative financial truth.
- This adoption note does not claim universal financial export or analytics convergence. New reporting/export rebuilders must document SQL authority proof, replay collapse, rebuild behavior, and intentional residual debt in the same change.

## 2026-05-22 Dangerous Primitive Governance Adoption Note

New Tier 1 rule added on this date:

- Approved dangerous derivative primitives must be runtime-reviewed, not only statically allowlisted.
- Use richer domain boundaries first: `IProductSqlAuthorityProjectionWriter`, `IStorefrontProductProjectionMutationBoundary`, search index services, lifecycle services, outbox processors, and SQL authority helpers remain preferred.
- When a narrow derivative mutation cannot use an existing writer, `DerivativeProjectionMutationGuard` is the lightweight reviewed precondition. It requires primitive type, boundary, document key, event identity, authority kind, freshness guard, fail-closed behavior, review reason, and SQL authority for full visibility replacement/search writes.

Runtime adoption:

- `StorefrontFooterProjectionConsumer` now uses `ProjectionEventFence` before MongoDB side effects and calls `DerivativeProjectionMutationGuard` before its reviewed full `StorefrontFooterDocument` replacement.
- `StorefrontConfigProjectionConsumer` now calls `DerivativeProjectionMutationGuard` before its reviewed SQL-recomposed `StorefrontConfigDocument` full replacement and before Redis store-config cache invalidation. The guarded authority scope is storefront config plus checkout/store metadata, with the existing event fence and freshness filter still carrying replay protection.
- `SqlAuthorityOverlayKind` now includes named derivative scopes for storefront config/footer, checkout, and CommerceHub listing governance declarations.
- Focused tests cover guard rejection behavior, storefront config runtime behavior, and source governance markers for the storefront footer/config dangerous primitives.

Remaining debt:

- This is intentionally not a universal mutation engine. Existing approved CommerceHub, affiliate, coupon, payment-method, promotion, repository, search, Redis, and direct-SQL paths remain specialized unless they are changed. Storefront config/footer are examples of narrow adoption where existing SQL recomposition and freshness semantics made runtime guard insertion low-risk.
- Future dangerous primitive allowlist additions must add a runtime boundary or guard, replay/freshness/fail-closed tests, and documentation. A static exception alone is not enough for Tier 1 visibility or authority-adjacent mutation.

## 2026-05-22 Tenant Lineage Adoption Note

New Tier 1 rule added on this date:

- Tenant lineage for replay/rebuild/repair paths is SQL-authoritative. Payload tenant ids, raw tenant headers, Mongo documents, Redis key shape, Elasticsearch documents, and stale repair inputs are not ownership proof.
- `TenantLineageAuthority` is the preferred lightweight helper when a derivative path needs current active-tenant proof or current same-tenant row proof before visibility mutation.
- Admin and repair endpoints should consume trusted tenant scope from `ITenantContext.RequiredTenantId` or an equivalent reviewed resolver. Endpoint-local parsing of `X-Tenant-Id` is not repair authority.

Runtime adoption:

- Storefront config projection now reloads current SQL store-config plus active tenant lineage before Mongo replacement or Redis invalidation.
- Missing, inactive, deleted, moved, or cross-tenant storefront config lineage fails closed by deleting the tenant-scoped projection and invalidating the tenant's store-config cache.
- Admin search sync now uses trusted tenant context for product/category/CMS search repair scope.

Remaining debt:

- This is not a repo-wide tenant projection framework. Existing product/variant/payment/fulfillment/accounting helpers remain the stronger domain-specific boundaries where they already prove tenant ownership.
- Future CMS, homepage, affiliate, CommerceHub, search repair, support/admin cache, analytics, notification, cache warmer, and direct repair paths should adopt `TenantLineageAuthority` or document an equivalent SQL tenant recomposition boundary with focused replay/rebuild coverage.

## 2026-05-22 Tenant Derivative Isolation Adoption Note

New Tier 1 rule added on this date:

- Tenant-scoped derivative cache/projection/search writes must use current SQL tenant lineage plus tenant-scoped derivative ids or mutation filters.
- `TenantScopedDerivativeKey` is the preferred helper when a tenant-owned derivative search document needs a stable id outside SQL.
- Cache hydration and rebuild/replay are convergence operations. Redis, MongoDB, Elasticsearch, homepage JSON, affiliate profile/currency events, and repair inputs may trigger recomposition, but they cannot serve or write public visibility after current SQL tenant lineage rejects it.

Runtime adoption:

- `StorefrontHomeService` now revalidates active tenant lineage before returning homepage product/category cache hydration, and category cards are filtered through current SQL category lineage.
- `MarketplaceProductReadModelProjectionService` and `MarketplaceHomepageReadModelProjectionService` now delete/scrub tenant-scoped derivative documents when replay targets inactive tenant lineage.
- `AffiliateProductPolicyProjectionConsumer` now validates current product tenant ownership before public affiliate mutation, uses tenant+product Mongo filters, writes tenant-scoped public affiliate search ids, reloads active SQL tenant profile/currency on profile/currency fanout, and deletes affiliate tenant visibility when tenant lineage is inactive.
- `AF16E_ReindexAffiliateProductsWithStorefrontUrl` now treats active SQL tenant/product/policy visibility as repair authority and uses tenant-scoped public affiliate search ids while scrubbing legacy product-id search documents.

Remaining debt:

- This phase intentionally hardens live high-risk derivative surfaces rather than introducing a universal cache/projection engine.
- CMS/SEO caches, notification/support/admin caches, analytics hot/cold stores, marketplace localization/review caches, future recommendation engines, future cache warmers, and future feed/export writers must adopt the same tenant lineage and tenant-scoped key rules when they mutate or hydrate tenant-scoped visibility.
- Public affiliate product Mongo documents still use historical `ProductId` BSON ids. Runtime filters now include tenant scope and fail closed on duplicate-key collision; a future data migration can move the physical BSON id to tenant+product if needed.

## 2026-05-24 Tenant Admin Actor Authority Adoption Note

New Tier 1 rule added on this date:

- Tenant-bound privileged endpoints must recompose current SQL actor authority before operational side effects. JWT role and tenant claims are candidate evidence only.
- `AdminTenantAuthority` is the preferred lightweight helper for tenant-admin actor recomposition. It validates the current SQL `Users` row, current user role, active/deleted actor state, trusted tenant context, current active tenant lineage, and endpoint role metadata.
- `AdminTenantAuthorityMiddleware` is the runtime adoption point for role-protected POS/admin endpoints. It runs after normal ASP.NET authorization and fails closed for stale role replay, disabled/deleted users, tenant-claim mismatch, current actor tenant mismatch, and inactive tenants.
- Admin order, shipment, inventory, storefront configuration, CMS/publication, search/cache, and repair/rebuild controllers should consume `ITenantContext.RequiredTenantId` instead of reparsing tenant headers. Tenant headers are compatibility evidence after tenant resolution, not controller-local mutation authority.
- Tenant admin operational tooling must remain tenant-scoped. Global search index mutation, all-tenant repair fanout, global cache purge, or global projection mutation requires explicit platform authority or reviewed runbook semantics.

Runtime adoption:

- Role-protected tenant-bound endpoints now pass through SQL actor recomposition before controller execution.
- Admin data-sync repair uses trusted `ITenantContext.RequiredTenantId` instead of raw `X-Tenant-Id` parsing.
- Tenant admin search repair blocks global index deletion/recreation and retains tenant-scoped SQL-first reindex/cache invalidation.
- AF16E affiliate repair/reindex has a tenant-scoped entrypoint for admin use; all-tenant repair remains an internal/platform primitive.
- Focused tests cover current SQL admin authorization, stale role replay collapse, cross-tenant actor rejection, inactive actor/tenant rejection, raw tenant-header source guard, and operational control source guards.

Remaining debt:

- This is not a universal IAM or policy engine. It does not add delegated organizations, external SSO, ABAC, or a support impersonation ceremony.
- Future support tooling, tenant switching flows, global recovery jobs, cache warmers, publication repair tools, and bare-authorize tenant mutation endpoints must add explicit actor authority, tenant scope, replay-collapse behavior, and docs in the same change.
- Domain-specific lifecycle helpers remain the stronger mutation boundaries for payment, order, fulfillment, accounting, inventory, product/variant lifecycle, and derivative visibility. Actor authority gates who may request work; SQL domain authority still decides what work is valid.

## 2026-05-24 Operational Convergence Observability Adoption Note

New Tier 1 rule added on this date:

- Replay collapse, fail-closed authority rejection, stale fanout suppression, rebuild authority declaration, dangerous primitive governance, and drift detection must be operationally visible through a shared low-cardinality diagnostic contract.
- `OperationalConvergenceTelemetry` is the preferred telemetry boundary for semantic convergence. It defines phase, decision, authority, and reason enums and emits metrics on `PointOfSale.Operational.Convergence`.
- Metric labels must be stable and non-sensitive. Tenant scope is bucketed; raw tenant ids, order numbers, payment ids, customer data, document keys, provider payloads, and exception text are not telemetry dimensions.

Runtime adoption:

- `ProjectionEventFence` records duplicate completed replay, active overlap collapse, duplicate completion race, and successful fence completion.
- `ProjectionAuthorityValidator`, `SqlTruthRebuildGuard`, and `DerivativeProjectionMutationGuard` record stale version/timestamp rejection, rebuild authority declarations, and dangerous primitive accept/reject decisions.
- SQL authority helpers for tenant lineage, admin actor authority, product variant lifecycle, inventory lineage, order fulfillment lineage, order payment lineage, and accounting ledger replay emit fail-closed or retry/collapse diagnostics.
- Stock-sync suppression emits inventory replay suppression diagnostics; Mongo projection drift health checks and Elasticsearch drift reconciliation emit drift diagnostics.
- Focused runtime tests validate bounded tags, replay collapse telemetry, dangerous primitive governance telemetry, rebuild auditability, and accounting replay diagnostics.

Remaining debt:

- This is intentionally not a generic projection framework, SIEM, durable audit table, or full distributed tracing program.
- Specialized consumers that do not use the shared guards remain valid, but future replay/rebuild/drift-sensitive behavior in cache warmers, exporters, reporting rebuilders, support/admin caches, notification caches, external sync, and global runbooks must adopt this telemetry boundary or document a reviewed equivalent.
- Drift telemetry is diagnostic only. It does not make MongoDB, Redis, Elasticsearch, or health-check counts authoritative.

## 2026-05-24 Navigation Projection Replay Adoption Note

New Tier 1/2 rule clarified on this date:

- Storefront navigation projection is tenant-scoped derivative visibility. It must recompose from current SQL `StoreConfig` and active tenant lineage before public Mongo or Redis side effects.
- Duplicate `NavigationPublishedEvent` replay must collapse before full-document replacement or cache invalidation. A same-`EventId` replay is not a fresh event.
- Missing, inactive, moved, or cross-tenant store-config lineage must converge to delete/scrub/no-op plus cache invalidation, not stale navigation preservation.
- Full `StorefrontNavigationDocument` replacement and Redis navigation cache mutation are dangerous derivative primitives and require `DerivativeProjectionMutationGuard` or an equivalent reviewed runtime precondition.

Runtime adoption:

- `NavigationProjectionConsumer` now uses `ProjectionEventFence`, `TenantLineageAuthority`, and `DerivativeProjectionMutationGuard`.
- `SqlAuthorityOverlayKind.StorefrontNavigation` names the boundary for guard declarations and future governance tests.
- `CrossSystemAuthorityRegressionGuardTests` now includes navigation in the high-risk Mongo replacement review set and asserts replay/tenant/fail-closed/runtime-guard markers.

Remaining debt:

- This hardens the concrete navigation projection path found in the strategic review. CMS page projection, smart-offer overlays, online-customer admin projections, SEO caches, analytics/reporting caches, notification caches, support/admin caches, and future cache warmers/exporters remain specialized until their mutation or rebuild behavior is expanded.
- Future navigation/CMS/SEO repair or rebuild tooling must add SQL authority recomposition, replay collapse, fail-closed convergence, and observability tests before rollout.

## 2026-05-24 CHAOS-CON-01 Semantic Convergence Chaos Adoption Note

New Tier 1 rule clarified on this date:

- Replay/rebuild correctness must be validated under operational pressure, not only happy-path projection delivery.
- Duplicate outbox delivery, retry storms, dead-letter replay, delayed callbacks, interrupted rebuilds, stale cache hydration, stale tenant lineage, stale admin sessions, repair overlap, reindex overlap, and reconciliation overlap must converge through current SQL authority and reviewed lineage helpers.
- A replay-safe no-op is a valid outcome only when it is deterministic and diagnosable.

Runtime and test adoption:

- Added `SemanticConvergenceChaosValidationTests` as an additive chaos validation suite for shared Tier 1 convergence boundaries.
- Runtime paths exercised include `ProjectionEventFence`, `SqlTruthRebuildGuard`, `DerivativeProjectionMutationGuard`, `TenantLineageAuthority`, `AdminTenantAuthority`, `OrderPaymentLineageAuthority`, `OrderFulfillmentLineageAuthority`, and `AccountingLedgerAuthority`.
- Source-governance checks in the same suite ensure tenant-scoped repair/reindex, SQL-first rebuild declarations, navigation replay fencing, dangerous primitive governance, and product cache duplicate fanout fencing remain present on inspected operational surfaces.

Validated guarantees:

- Duplicate completed projection replay collapses before derivative side effects.
- Duplicate rebuild triggers are repeatable SQL-authority declarations, not a second authority source.
- Missing rebuild authority, stale cache hydration, and missing SQL authority for full derivative replacement fail closed.
- Tenant, actor, payment, fulfillment, and accounting replay recomposes from current SQL or collapses/retries without stale resurrection.
- Convergence telemetry classifies replay, rebuild, authority, and dangerous primitive decisions with bounded labels.

Discovered weakness:

- AF16E affiliate repair/reindex uses active SQL tenant filtering and tenant-scoped derivative keys, but it does not directly call `TenantLineageAuthority`. This remains acceptable as an equivalent local control on that path, but future adoption notes must distinguish shared-helper adoption from local SQL-lineage enforcement.

Remaining debt:

- This suite is not a substitute for path-specific tests on future CMS, SEO, notification, analytics, support-cache, feed/export, report rebuild, cache warmer, or global runbook behavior.
- Future Tier 1 operational surfaces should add chaos-style tests that combine replay collapse, rebuild overlap, tenant isolation, actor authority, dangerous primitive governance, and convergence telemetry rather than only asserting static source markers.

## 2026-05-24 STABILIZATION-CON-01 Operational Throughput Adoption Note

New Tier 1 rule clarified on this date:

- High-volume outbox and replay workers must be operationally tunable through bounded configuration. Batch size, max batches per poll, inter-batch delay, poll interval, retry threshold, and claim lease are operational controls, not business authority.
- Throughput tuning must preserve SQL eligibility, aggregate ordering blockers, tenant scope, retry delay, compensation, and replay collapse.

Runtime adoption:

- `MarketplaceEventOutboxWorker` now drains bounded configured event outbox batches per poll.
- `MarketplaceProjectionOutboxWorker` now drains bounded configured projection outbox batches per poll.
- `StockSyncOutboxWorker` now drains bounded configured stock sync batches per poll.
- `MarketplaceEventOutboxProcessor`, `MarketplaceProjectionOutboxProcessor`, and `StockSyncOutboxProcessor` now use configured claim leases.
- Options validation now covers marketplace event/projection batch sizes, max batches per poll, inter-batch delay, and stock sync batch-draining controls.

Validated guarantees:

- Marketplace projection outbox processing respects caller batch size and leaves residual queued work stable for the next pass.
- Duplicate projection completion cleans residual outbox rows without redispatching read-model side effects.
- Source governance asserts that high-volume outbox workers and processors use operational throughput options.

Remaining debt:

- This pass does not implement adaptive throttling, tenant-specific rate limiting, or fixed rebuild duration SLOs.
- Production capacity should be tuned from backlog age, dependency latency, repeated failure/dead-letter counts, and tenant hotspot telemetry.
- Future CMS, SEO, notification, analytics, support-cache, feed/export, report rebuild, cache warmer, and global runbook workers need equivalent throughput controls and path-specific tests when their operational load grows.

## 2026-05-24 OPERATIONS-RUNBOOKS-01 Operational Recovery Adoption Note

New Tier 1 rule clarified on this date:

- Replay/rebuild reliability is not only a runtime property; it must be operable by humans under incident pressure.
- Operator runbooks must preserve SQL-first authority, deterministic replay collapse, fail-closed derivative deletion, tenant-scoped recovery, lifecycle/reconciliation ownership, dangerous primitive governance, and bounded throughput controls.
- Runbooks must distinguish shared-helper adoption from equivalent local SQL authority checks.

Documentation and test adoption:

- Added `docs/operations/operational-recovery-runbooks.md` for replay, rebuild/reindex, backlog, cache/projection, tenant isolation, payment/accounting/fulfillment, drift, telemetry, escalation, and remaining-debt procedures.
- Added `docs/architecture/dangerous-operational-primitives.md` for direct SQL/bulk mutation, Mongo full replacement, raw Elasticsearch writes, Redis key/pattern mutation, raw bus publishing, global repair, tenant override, and manual financial repair governance.
- Added `OperationalRecoveryRunbookArchitectureTests` to keep the runbook aligned with required operational recovery areas and shortcut prohibitions.

Remaining debt:

- This is intentionally documentation and governance hardening, not a new runtime recovery framework.
- Future operational tools that change replay/rebuild behavior, add global scope, expose new dangerous primitives, or alter financial/tenant recovery must update runbooks and focused tests in the same change.

## 2026-05-24 TENANT-CERTIFICATION-01 Certification Freeze Adoption Note

New Tier 1 rule clarified on this date:

- Tenant isolation certification is a regression-freeze boundary across trusted tenant resolution, current SQL tenant lineage, current SQL actor authority, tenant-scoped derivative identity, replay collapse, SQL-first rebuild declarations, dangerous primitive governance, and convergence telemetry.
- Storefront CMS page projection is now a reviewed tenant-scoped derivative boundary. It must retain publish/delete `ProjectionEventFence` use, active SQL tenant-lineage validation, fail-closed tombstone convergence, full-replacement `DerivativeProjectionMutationGuard`, and `SqlAuthorityOverlayKind.StorefrontCms`.
- Future tenant-scoped public derivative writers should add certification-style tests that combine source markers with runtime behavior where practical.

Runtime and test adoption:

- `CmsPageProjectionConsumer` now collapses duplicate publish/delete replay before MongoDB side effects and fails closed when current SQL page or tenant authority is missing.
- `TenantIsolationCertificationFreezeTests` freeze trusted tenant resolution, tenant-admin trusted-context usage, global repair rejection, tenant-scoped affiliate/CommerceHub/CMS derivative controls, and certification documentation.
- `CrossSystemAuthorityRegressionGuardTests` now include storefront CMS page full replacement in the reviewed dangerous-primitive guard boundary.

Remaining debt:

- This is not universal helper adoption. Other CMS-adjacent, SEO, smart-offer, online-customer, notification, analytics, support-cache, feed/export, reporting rebuild, cache warmer, and global runbook paths still require path-specific certification when changed or expanded.

## 2026-05-24 SUPERADMIN-CON-01 Phase 2 Marketplace Governance Adoption Note

New Tier 1 rule clarified on this date:

- CommerceHub marketplace product visibility is SQL-listing-authoritative. Product publication replay may trigger rebuild, but current SQL `CommerceHubListings` plus tenant/product/variant/category visibility decides upsert or delete.
- Homepage/recommendation references are not visibility authority. Curated listing/category ids must be scrubbed during public reads and projection rebuilds when current SQL no longer proves visibility.
- CommerceHub search document ids for tenant-owned listings must be tenant-scoped for new writes; legacy listing-only ids are cleanup targets.

Runtime and test adoption:

- `MarketplaceProductReadModelProjectionService` now deletes marketplace product read models when no current published SQL listing exists for the product's public variant lineage.
- `HomepageProjectionConsumer` now uses `ProjectionConsumerEventFence` before homepage rebuild side effects.
- Super-admin homepage reference validation and homepage hydration now require active tenant lineage plus public variant lineage.
- CommerceHub search indexing uses tenant-scoped document ids, and tenant-aware delete/reindex paths scrub legacy ids.

Remaining debt:

- Marketplace reviews, localization, CMS-adjacent content, feed/export writers, ranking engines, cache warmers, support tools, and global runbooks remain path-specific surfaces until expanded and certified.

## 2026-05-24 SUPERADMIN-CON-01 Phase 3 Replay / Rebuild / Recovery Adoption Note

New Tier 1 rule clarified on this date:

- Global recovery operations are convergence tools, not authority sources. Tenant-admin recovery stays tenant-scoped; global/all-tenant recovery remains platform/runbook scope.
- CommerceHub search recovery must require SQL `CommerceHubListing` authority plus current tenant/product/variant/category/stock/price/media authority before indexing or preserving marketplace search visibility.
- Rebuild interruption, restart, and overlap are expected operational states. Each run must redeclare SQL authority and converge from SQL instead of Mongo, Redis, Elasticsearch, dead-letter payloads, or repair snapshots.

Runtime and test adoption:

- `AdminCommerceHubController` now calls tenant-scoped `ReindexTenantAsync` for tenant-admin CommerceHub search recovery.
- `CommerceHubReindexService` retains global `ReindexAsync` for platform/runbook scope and adds tenant-scoped SQL-first reindex for admin scope.
- `CommerceHubSearchSqlAuthorityOverlay` declares `SqlAuthorityOverlayKind.CommerceHubListing`.
- CommerceHub reindex pacing uses bounded `CommerceHubReindexOptions`.
- `SuperAdminRecoverySafetyCertificationTests` and marketplace outbox retry tests freeze tenant/global split, listing-authority declaration, pacing, documentation, and dead-letter reset behavior.

Remaining debt:

- This does not add adaptive rebuild scheduling, fixed SLOs, a dead-letter UI, SIEM-grade audit, or universal certification for future feed/export/ranking/cache-warmer/global script paths.

## 2026-05-24 SUPERADMIN-CON-01 Phase 4 Emergency Governance Adoption Note

New Tier 1 rule clarified on this date:

- Super-admin/platform mutations need attributable operational audit in addition to current SQL actor authority.
- Emergency override, rollback, support impersonation, and tenant override behavior must remain SQL-first, replay-safe, and documented.
- Request-level operational audit is a governance breadcrumb, not a replacement for domain audit, ledgers, or convergence telemetry.

Runtime and test adoption:

- `SuperAdminOperationalAuditMiddleware` now records every non-read `RequireSuperAdmin` operation after `SuperAdminPlatformAuthorityMiddleware` has recomposed the current SQL platform actor.
- Legacy platform-policy mutation endpoints outside `/api/super-admin/*` receive the same request-level operational audit coverage.
- `SuperAdminOperationalAuditMiddlewareTests` and `EmergencyGovernanceCertificationTests` freeze audit emission, middleware ordering, legacy endpoint authority, emergency/support/rollback documentation, and dangerous primitive guidance.

Remaining debt:

- No enterprise IAM, support break-glass workflow, SIEM-grade audit lake, request-body capture, or universal domain-specific audit for every platform endpoint was introduced.
- Future support, impersonation, tenant override, feed/export, report rebuild, cache warmer, and global runbook tooling must add path-specific SQL authority, durable audit, replay/rebuild behavior, dangerous primitive governance, and docs/tests.

## 2026-05-24 SUPERADMIN-CON-01 Phase 5 Operational Chaos Certification Adoption Note

New Tier 1 rule clarified on this date:

- Final super-admin operational certification treats replay storms, rebuild overlap, stale emergency sessions, moderation/review replay, marketplace visibility chaos, and dangerous primitive misuse as expected convergence pressure.
- CommerceHub public search mutation from review/moderation/recovery paths must revalidate current SQL listing authority before indexing; missing authority must delete/tombstone tenant-scoped search visibility.
- Dangerous primitive misuse, unsupported rollback, direct dead-letter suppression, and stale platform authority must fail closed with telemetry/audit diagnostics.

Runtime and test adoption:

- `MarketplaceProductReviewProjectionService` now runs review-triggered CommerceHub search sync through `CommerceHubSearchSqlAuthorityOverlay` and tenant-scoped delete/status-update filters.
- `SemanticConvergenceChaosValidationTests` now exercises stale/revoked/governance-restricted super-admin sessions and final replay/rebuild/dangerous-primitive overlap semantics.
- `SuperAdminOperationalChaosCertificationTests` freezes review replay SQL-overlay usage, rebuild/retry overlap source markers, emergency audit ordering, dangerous primitive rejection, and Phase 5 documentation coverage.
- Phase 5 findings live in `docs/architecture/super-admin-operational-chaos-certification-findings.md`.

Remaining debt:

- This is not a universal chaos framework or universal marketplace projection rewrite. Future review exports, localization, personalized ranking, feeds, cache warmers, support tools, analytics/reporting rebuilds, and global scripts need path-specific chaos certification when expanded.

## 2026-05-24 POS-ADMIN-CERTIFICATION-01 PASS 1 Session Authority Adoption Note

New Tier 1 identity rule:

- POS bearer authentication is a current-SQL operational authority boundary. A signed access token must not remain usable after current session rotation/revocation, SQL actor role or active-state change, tenant-lineage rejection, or current vendor governance ban.
- POS refresh state uses one current SQL session binding per user. Refresh and logout convergence is authorization behavior, not cache behavior.

Runtime and test adoption:

- `PosSessionAuthority` validates every default POS JWT after cryptographic validation and before tenant-admin operation.
- `AuthController`, tenant-owner onboarding issuance, and idempotent onboarding-session restoration issue `pos_session` access bindings and persist hash-only current refresh session authority.
- EF concurrency on `Users.RefreshToken` rejects overlapping stale session-rotation writes.
- `PosSessionAuthorityTests`, `PosAuthenticationLifecycleTests`, `PosSessionConcurrencyTests`, and `AdminAuthenticationSessionCertificationTests` cover SQL acceptance, stale role/session/logout rejection, refresh replay collapse, two-writer rotation rejection, onboarding issuer binding, tenant/governance rejection, token hashing, and documentation freeze.

Remaining debt:

- This does not introduce per-device concurrent POS sessions, refresh-family compromise history, enterprise IAM/SSO/MFA/ABAC, support impersonation, or a POS password-reset/session-management workflow.

## 2026-05-25 POS-ADMIN-CERTIFICATION-01 PASS 2 Admin Scope Adoption Note

New Tier 1 rule clarified on this date:

- Tenant-admin operational endpoints consume trusted tenant context after SQL-backed session/actor validation. A middleware compatibility header is not an endpoint authority contract.
- Mixed external-callback/admin operations must preserve their external authentication contract while rejecting any authenticated tenant actor whose supplied tenant does not match trusted context.

Runtime and test adoption:

- Admin AI, Commerce AI, abandoned-checkout, storefront footer, and abandoned-cart recovery administration paths now use `ITenantContext.RequiredTenantId`.
- Authenticated abandoned-cart recovery-result logging now rejects cross-tenant body scope before service or persistence work.
- Follow-up hardening on 2026-05-26 extended the same trusted-context and explicit SQL predicate contract to reviewed bank, expense-category, financial-account, and shipping-method admin paths, including bank statement/opening-balance operations, financial-account parent validation, and storefront shipping calculation.
- Tenant isolation freeze tests cover these controllers, and a runtime test proves the recovery-log fail-closed behavior.
- `AdminTenantOperationalScopeTests` prove foreign-tenant finance/shipping configuration ids fail closed without mutation or event publication.

Remaining debt:

- Replay/rebuild and public derivative controls were not changed by this pass; existing SQL-first controls remain applicable.
- Storefront/checkout/webhook callbacks, `SettingsController`, `ReportsController`, and future analytics/support/cache-warmer/report/export tools still require path-specific tenant authority certification.

## 2026-05-25 POS-ADMIN-CERTIFICATION-01 PASS 5 Public Visibility Adoption Note

New Tier 1 public projection rules:

- CommerceHub search, product read-model rebuild, listing fanout, homepage/recommendation curation, and maintenance/recovery mutation remain SQL-listing- and category-lineage-authoritative.
- Public image restoration is SQL-media-authoritative: current `ProductMedia`/`ProductVariants.MediaId` may populate product-card images; legacy and derivative image fields cannot survive SQL media deletion through replay or rebuild.
- Review-decorated search mutation is SQL-moderation-authoritative: `AverageRating` and `ReviewCount` are recomposed from current approved, non-deleted SQL `ProductReviews` before public search index mutation.

Runtime and test adoption:

- `CommerceHubSearchSqlAuthorityOverlay` now clears unproven media and recomposes current approved-review aggregates while existing search auto-healing, analytics maintenance, and review sync remain routed through the same overlay.
- Marketplace product read-model and product-update listing projection no longer repopulate public images from legacy or derivative image strings.
- Runtime and architecture freeze tests cover stale media suppression, approved-review aggregate replay convergence, category-lineage suppression, tenant-scoped delete, and overlay-governed public maintenance.

Remaining debt:

- Derivative popularity/click scoring, normal read-model propagation lag, future ranking/feed/export/localization/support/cache-warmer/reporting paths, and CDN/browser cache expiry remain explicit path-specific debt rather than claimed synchronous visibility authority.

## 2026-05-25 POS-ADMIN-CERTIFICATION-01 PASS 6 Operational Tooling Adoption Note

New Tier 1 operational tooling rule:

- Tenant-admin storefront product search repair is no longer allowed to index from baseline SQL rows. Operational search repair must recompose through `ProductSearchSqlAuthorityOverlay` and then map Elasticsearch documents from `ProductSearchDocumentMapper.MapFromProductDocument(...)`.
- Missing or invalid current SQL search authority is a delete/tombstone outcome, not a reason to publish a reduced “good enough” search document.
- Tenant-admin product read-model rebuild may invalidate product caches only through tenant-scoped `IProductCacheService`; direct Redis key enumeration/deletion in the repair controller is not an accepted operational shortcut.
- Reviewed AF16E affiliate repair/reindex may not invent derivative tenant currency during recovery. It must normalize tenant currency through `CurrencyNormalizationHelper.NormalizeCurrencyOrThrow(...)`, redeclare SQL repair authority through `SqlTruthRebuildGuard`, and delete stale affiliate visibility when tenant storefront/currency authority is missing.

Runtime and test adoption:

- `AdminSearchSyncController.SyncProducts(...)` now iterates current SQL product ids, applies the shared SQL search overlay per product, deletes stale search visibility when SQL rejects it, and indexes only the SQL-recomposed search document shape.
- `AdminSearchSyncController.SyncCategories(...)` now declares SQL rebuild authority, iterates the current SQL category lineage snapshot for authoritative category indexing, and deletes tenant-scoped stale category search documents that are missing from the current SQL authority set after the sync pass.
- `AdminSyncController` now keeps SQL-authoritative projection writes behind `IProductSqlAuthorityProjectionWriter` and delegates post-rebuild cache invalidation to `IProductCacheService`.
- `AF16E_ReindexAffiliateProductsWithStorefrontUrl` now rejects silent `USD` fallback, declares affiliate repair authority explicitly, and converges missing tenant storefront/currency authority by deleting stale public affiliate and affiliate-intelligence visibility.
- Added runtime tests for unit-authoritative storefront price preservation and fail-closed delete on invalid SQL category lineage.
- Added architecture/doc freeze coverage for the search repair markers, the reviewed cache invalidation boundary, and PASS 6 certification documentation.

Remaining debt:

- This note covers the reviewed tenant-admin storefront search repair, category search stale-delete convergence, product-rebuild cache-invalidation, and AF16E affiliate repair surfaces, not every future search/cache/CMS/SEO/export/support/feed rebuild path.

## 2026-05-25 POS-ADMIN-CERTIFICATION-01 PASS 7 Replay / Rebuild / Chaos Adoption Note

New Tier 1 recovery rule:

- Recovery outboxes must document worker-restart behavior. Active duplicate workers should collapse through a claim lease, processed-event fence, or equivalent completion record before lifecycle/reconciliation side effects.

Runtime and test adoption:

- `PaymentCancellationOutboxProcessor` now claims `PaymentCancellationOutboxMessage` rows through `ClaimedUntilUtc` before calling `IPaymentLifecycleService`.
- `PaymentCancellationOutboxOptions.ProcessingLeaseSeconds` bounds crash pickup and active-overlap behavior.
- Active claimed rows are skipped during polling, claims clear on processed/manual-review/failure outcomes, and active-overlap collapse emits convergence telemetry.
- `PaymentCancellationOutboxOperationalScalabilityTests` and `ReplayRebuildChaosCertificationTests` freeze the lease, source markers, and PASS 7 documentation.

Remaining debt:

- This does not introduce adaptive scheduling or a universal worker framework. Future CMS, SEO, notification, analytics, support-cache, feed/export, report rebuild, cache warmer, and global script workers still need path-specific replay/rebuild/worker-restart certification when expanded.
