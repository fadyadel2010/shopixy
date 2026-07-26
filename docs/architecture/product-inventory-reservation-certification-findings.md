# POS-ADMIN-CERTIFICATION-01 PASS 3 Product / Inventory / Reservation Certification Findings

Date: 2026-05-25

## Scope

This certification reviewed product variant semantic ownership, inventory lineage, warehouse authority, reservation integrity, replay/rebuild/recovery behavior, marketplace/storefront stock visibility, operational inventory tooling, and inventory auditability across the SQL-authoritative POS Admin + CommerceHub platform.

The target was not a new inventory architecture. The target was deterministic replay-safe convergence under operational pressure, duplicate delivery, stale evidence, and cross-tenant marketplace order composition.

## Boundaries Audited

- Variant semantic ownership: merge/split supersession semantics, default/current variant authority, detached/superseded variant rejection, and variant-backed inventory eligibility.
- Inventory lineage and warehouse authority: current same-tenant variant, active parent product, active warehouse ownership, current stock-row ownership, and stock replay/fanout suppression.
- Reservation integrity: creation, expiry, current consumable reservation filtering, exact order-item reservation authority, and delivery/payment reuse.
- Replay/rebuild/recovery: reservation cleanup, stock recomposition, payment recovery, fulfillment commit, stock-sync replay, and SQL-first rebuild semantics.
- Marketplace/storefront consistency: CommerceHub payment retry authority, order-item reservation validation, and public stock visibility as derivative current SQL lineage.
- Operational governance and auditability: dangerous inventory primitives, fail-closed diagnostics, and architecture freeze coverage for reviewed inventory authority paths.

## Runtime Hardening Added

- `ProductVariantInventoryLineageAuthority` now exposes `FilterCurrentConsumableReservationsAcrossTenantsAsync(...)` so multi-tenant marketplace reservation validation can recompose current consumable SQL reservation lineage per tenant instead of assuming a single tenant scope.
- `InventoryConsistencyService.EnsureMarketplaceOrderReservationsActiveAsync(...)` now validates active reservations across all tenant-owned suborders, rejects foreign-owner active reservations, rejects over-reserved replay, and distinguishes raw active reservation rows from current lineage-backed reservation authority.
- `CommerceHubPaymentRecoveryAuthorityService` no longer trusts raw active reservations or raw stock rows when deciding whether an online marketplace payment may be retried.
- Payment recovery authority now requires current SQL product publication eligibility (`IsActive`, `IsOnlineVisible`, `PublishStatus.Published`, `TrackStock`), current SQL variant semantic ownership, current consumable reservation lineage, and current stock ownership through `WhereCurrentInventoryStockOwner(...)`.
- CommerceHub reservation creation now filters candidate stock rows through current stock-owner lineage before availability is accepted, and it fails closed with `MARKETPLACE_RESERVATION_STOCK_AUTHORITY_LOST` when raw stock rows still exist but the warehouse or stock owner is no longer current SQL authority.
- CommerceHub reservation stock locking now acquires warehouse rows in deterministic `WarehouseId`/`Id` order before allocation, reducing replay/concurrency deadlock risk without creating a second inventory authority model.
- CommerceHub return receipt now revalidates current stock ownership before adding resellable or damaged returned quantities. Delivered reservation evidence is not enough to restore stock when the warehouse is inactive or the variant/stock owner is no longer current SQL inventory authority.
- CommerceHub delivery commit now uses current SQL order-item plus inventory-lineage authority rather than public publication visibility. Marketplace unpublish/offline changes can block new reservation-backed payment recovery, but they do not prevent stock consumption for already-authorized delivery of an existing order item.
- PASS 3 addendum on 2026-05-27 closes a remaining legacy availability and derivative-total gap on the reviewed surfaces. `ProductVariantStockRepository`, `ProductVariantStockMutationSupport`, `InventoryConsistencyService`, `ProductVariantProjectionConsumer`, and `CommerceHubProjectionConsumer` now recompose reviewed availability or total stock only through current stock-owner lineage instead of summing undeleted stock rows directly.
- PASS 3 addendum on 2026-05-27 also closes a reviewed SQL fallback/storefront-search residual gap. `ProductVariantReadRepository` SQL fallback now requires current public variant lineage plus current stock-owner lineage, and `ProductSearchSqlAuthorityOverlay` now recomposes variant stock totals only from current stock-owner lineage instead of summing raw variant stock rows directly.
- PASS 3 addendum on 2026-05-27 closes a final reviewed cart/affiliate/recovery residual gap on derivative SQL snapshot paths. `CartService`, `AffiliateProductIntelligenceProjectionConsumer`, and `ProjectionFailureCompensationService` now recompose authoritative stock only from current stock-owner lineage instead of summing raw undeleted stock rows during storefront cart fallback, affiliate intelligence rebuild, or stock-sync terminal compensation replay.

## Findings

### Certified guarantees that held

- Variant semantic ownership remains SQL-first. Superseded, detached, or non-current variants are not accepted as stock, reservation, or public visibility authority on the reviewed paths.
- Inventory consumption and reservation reuse remain lineage-based, not projection-based. Reservation rows, stock rows, order state, cache/search visibility, and replay payloads are evidence only until current SQL lineage recomposition accepts them.
- Fail-closed stale suppression is working on the reviewed mutation and visibility boundaries. Missing or stale reservation/warehouse/variant/product lineage converges to rejection, release/repair, delete/tombstone, or no-op rather than stale stock reuse.
- Replay/rebuild safety remains SQL-authoritative. Replays may re-trigger validation and derivative repair, but they do not create new stock authority from derivative state.

### Residual gaps found and closed in this pass

- Marketplace payment recovery had a narrower authority check than the hardened checkout path. It could trust raw active reservation rows and raw stock rows without using the shared current-lineage filter, which created a stale reservation/warehouse lineage retry gap under payment recovery.
- Marketplace reservation authority validation assumed `expectedItems.First().TenantId` when filtering active reservations. That was unsafe for a marketplace order spanning multiple tenant-owned suborders.
- CommerceHub return receipt was an adjacent residual gap: committed historical reservation rows could drive stock restoration after warehouse or variant lineage changed. Stock-sync fanout later suppressed stale public propagation, but SQL stock mutation itself still needed the current stock-owner guard.
- CommerceHub reservation creation still accepted raw `ProductVariantStocks` rows before checking whether the stock row was owned by an active warehouse and current variant lineage. That allowed inactive-warehouse raw stock to look reservable even though later replay/recovery checks would reject it.
- CommerceHub delivery commit reused the reservation-creation publication guard. That made public publication drift stronger than inventory lineage and could block stock consumption for an already-reserved order item after a later marketplace unpublish/offline change.
- A remaining legacy read/fanout gap also existed on reviewed derivative paths. Repository availability reads, stock-sync/event total recomposition, storefront variant projection totals, and CommerceHub variant projection totals still summed undeleted stock rows directly, so inactive-warehouse or stale stock-owner lineage could overstate reviewed availability even after mutation paths were hardened.
- A final reviewed SQL-backed visibility gap also remained outside the earlier mutation fanout fixes. `ProductVariantReadRepository` SQL fallback could still select a superseded or no-longer-public default variant and sum raw stock rows, and `ProductSearchSqlAuthorityOverlay` could still overstate variant totals from inactive warehouses because it summed `variant.Stocks` directly.
- A final reviewed derivative SQL snapshot gap remained after those fixes. Storefront cart SQL stock fallback, affiliate intelligence SQL stock snapshots, and stock-sync terminal compensation replay still summed raw undeleted stock rows directly, so inactive-warehouse or stale stock-owner lineage could preserve cart quantity, affiliate stock visibility, or compensation replay totals after mutation-time hardening had already rejected that stock as current authority.

## Replay / Rebuild Guarantees

- Marketplace payment retry now converges from current SQL order-item, product, variant, warehouse, reservation, and stock ownership. Stale reservation rows or inactive warehouse lineage block retry instead of silently reusing stale inventory evidence.
- Marketplace reservation validation now requires exact reservation ownership and exact reserved quantity per current SQL order item. "At least enough reserved" is not sufficient.
- Marketplace reservation creation now treats inactive-warehouse or stale stock-owner rows as authority loss rather than available stock. Raw stock quantity alone is not reservable evidence.
- Duplicate or stale active reservation visibility no longer survives by crossing tenant boundaries inside one marketplace order.
- SQL stock ownership remains the rebuild/recovery truth source. Current stock owner filtering suppresses stale warehouse or detached stock reuse during reservation-backed payment recovery.
- Return receipt replay now also converges through current stock ownership. If a warehouse is disabled or a delivered variant is detached/superseded before return receipt, the return fails closed instead of restoring stock onto stale inventory lineage.
- Delivery commit remains inventory-lineage-authoritative instead of publication-authoritative. Current public unpublish/hide decisions do not resurrect or block an already-authorized physical stock consumption path; current variant/product active lineage still does.
- Reviewed availability reads and reviewed derivative stock-total recomposition now follow the same rule. Cart/checkout availability, stock-sync/event fanout totals, storefront variant stock totals, and CommerceHub variant stock totals recompose only from current stock-owner lineage and therefore cannot resurrect stale stock merely because undeleted rows remain.
- Reviewed SQL fallback checkout enrichment and reviewed storefront/search overlay stock recomposition now follow that same rule. Mongo-miss fallback checkout reads and SQL-backed search/storefront repair paths cannot resurrect superseded default variants or inflate stock from inactive-warehouse rows merely because undeleted SQL variant/stock rows remain.
- Reviewed derivative SQL stock snapshots now follow that same rule. Storefront cart fallback, affiliate intelligence stock recomposition, and stock-sync compensation replay totals cannot preserve inactive-warehouse or stale stock-owner quantity merely because undeleted SQL stock rows still exist.

## Reservation And Warehouse Lineage Semantics

- Consumable reservation authority requires all of the following at evaluation time: current same-tenant current variant ownership, active parent product lineage, active warehouse lineage, non-deleted stock ownership, active reservation status, positive remaining reserved quantity, and unexpired reservation timing.
- Reservation rows that are expired, foreign-owner, orphaned, tied to inactive warehouses, tied to detached/superseded variants, or no longer backed by current stock ownership are repair evidence only. They are not payment, checkout, or fulfillment consumption authority.
- Warehouse activity is part of inventory authority, not a projection hint. An inactive warehouse collapses reservation-backed payment retry and fulfillment reuse on the reviewed paths.
- Warehouse activity is also part of reservation creation authority on the reviewed CommerceHub checkout path. A raw stock row in an inactive warehouse is not reservable inventory.
- Return stock restoration uses the same warehouse/variant stock-owner authority. Returned goods cannot resurrect sellable or damaged inventory into an inactive warehouse or stale variant lineage through replayed return evidence.

## Operational Auditability Findings

- The inspected inventory authority paths remain diagnosable through explicit fail-closed exception codes and convergence-oriented source freezes.
- New regression coverage freezes that reviewed marketplace reservation and payment recovery paths must keep using cross-tenant current reservation filtering and current stock-owner filtering rather than raw row evidence.
- Return receipt regression coverage freezes that inactive-warehouse and superseded-variant return replay must reject before SQL stock quantity changes.
- New regression coverage also freezes that `ProductVariantReadRepository` SQL fallback keeps using public variant lineage plus current stock-owner lineage, and that `ProductSearchSqlAuthorityOverlay` keeps warehouse-lineage-aware stock recomposition on reviewed storefront/search boundaries.
- New regression coverage also freezes that `CartService`, `AffiliateProductIntelligenceProjectionConsumer`, and `ProjectionFailureCompensationService` keep using current stock-owner lineage for reviewed SQL stock snapshots and compensation replay totals instead of raw stock-row sums.
- This pass did not add a new SIEM-grade inventory audit subsystem. It strengthened path-level authority diagnostics and freeze coverage on the reviewed runtime boundaries.

## Remaining Inventory Risk Debt

- No universal stock migration or warehouse reassignment framework was introduced. Future physical stock transfer, reservation migration, warehouse reassignment, bulk inventory import, ERP feed/writeback, and specialized reconciliation workers still need path-specific SQL transaction, lock-order, replay, rebuild, and auditability certification.
- This pass reviewed and hardened CommerceHub marketplace reservation/payment recovery authority, not every future recommendation/export/cache-warmer/reporting surface that might consume stock-shaped visibility.
- This addendum reviewed legacy availability reads and reviewed derivative stock-total recomposition, not every future feed/export, warehouse transfer, inventory repair, or bespoke reporting path that may aggregate stock quantity.
- This addendum reviewed storefront cart fallback, affiliate intelligence rebuild, and stock compensation replay, not every admin/POS/reporting/read-only stock summary surface that still presents raw stock rows for operator diagnostics outside public or reservation authority.
- Operational inventory tooling remains intentionally flexible under dangerous-primitive governance and SQL-first recovery rules. The certification target is safe convergence, not removal of high-privilege recovery capability.

## Regression Freeze Invariants

- Do not treat raw `InventoryReservations`, `ProductVariantStocks`, CommerceHub order state, or projected/public stock visibility as enough inventory authority for payment retry, checkout, fulfillment, or rebuild.
- Do not filter marketplace reservations through a single tenant when the marketplace order can span multiple tenant-owned suborders.
- Do not accept "at least enough reserved" checks where current SQL order-item authority requires exact reservation ownership and exact reserved quantity.
- Do not reuse reservation-backed authority when current SQL product publication, variant semantic ownership, warehouse activity, or stock ownership no longer proves the reservation is consumable.
- Do not bypass `ProductVariantInventoryLineageAuthority` or `WhereCurrentInventoryStockOwner(...)` on reviewed reservation-backed inventory consumption and payment recovery paths.
- Do not sum undeleted `ProductVariantStocks` rows directly on reviewed availability or derivative stock-total paths. Repository availability, stock fanout totals, storefront variant stock projections, and CommerceHub variant stock projections must recompose through current stock-owner lineage first.
- Do not let reviewed SQL fallback checkout reads or reviewed storefront/search SQL overlays choose variants from raw undeleted rows alone. They must recompose current public variant lineage and current stock-owner lineage before exposing default-variant, price, or stock authority.
- Do not let reviewed storefront cart SQL snapshots, affiliate intelligence SQL snapshots, or stock compensation replay totals sum raw undeleted stock rows directly. They must recompose through current stock-owner lineage before preserving derivative stock visibility or replaying a "fresh" stock snapshot.
- Do not restore returned stock from committed reservation evidence unless current SQL warehouse, variant, and stock ownership still prove inventory authority.

## Validation

- `CommerceHubPaymentRecoveryAuthorityServiceTests`
- `InventoryConsistencyServiceTests`
- `CheckoutConcurrencyHardeningSourceTests`
- `ReserveOrderItemAsync_ShouldRejectInactiveWarehouseStockAuthority`
- `CommitDeliveryAsync_ShouldAllowCommittedStockAfterProductUnpublish`
- `ReceiveReturnAsync_ShouldRejectInactiveWarehouseReplay`
- `ReceiveReturnAsync_ShouldRejectSupersededVariantReplay`
- `GetAvailableQuantityAsync_IgnoresInactiveWarehouseStockLineage`
- `GetAvailableQuantitiesAsync_IgnoresInactiveWarehouseStockLineage`
- `ReserveAsync_StockSyncOutboxTotal_IgnoresInactiveWarehouseStockLineage`
- `ProductSearchSqlAuthorityOverlay_IgnoresInactiveWarehouseStockLineage`
- `ProductVariantReadRepository_SqlFallback_IgnoresInactiveWarehouseStockLineage`
- `ProductVariantReadRepository_SqlFallback_DefaultVariantSkipsSupersededDefaultOwner`
- `CartService_SqlAuthoritySnapshot_IgnoresInactiveWarehouseStockLineage`
- `AffiliateProductIntelligenceProjectionConsumer_SqlStockSnapshot_IgnoresInactiveWarehouseStockLineage`
- `HandleTerminalFailureAsync_WhenOnlyInactiveWarehouseStockRemains_EmitsZeroAuthoritativeSnapshotReplay`
- `GlobalReliabilityInfrastructureExtractionTests`
- `ReadModelStockConsistencyArchitectureTests`

Focused validation passed on 2026-05-25 with:

`dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj -nologo -v minimal --filter "FullyQualifiedName~CommerceHubPaymentRecoveryAuthorityServiceTests|FullyQualifiedName~InventoryConsistencyServiceTests|FullyQualifiedName~CheckoutConcurrencyHardeningSourceTests.CheckoutCon07_MarketplaceReservationReplay_MustFenceOwnershipOverReservationAndRecovery"`

Focused PASS 3 addendum validation passed on 2026-05-27 with:

`dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj -nologo -v minimal --filter "FullyQualifiedName~ProductVariantMergeSplitAuthorityTests|FullyQualifiedName~GlobalReliabilityInfrastructureExtractionTests"`

Focused PASS 3 SQL fallback/overlay validation passed on 2026-05-27 with:

`dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj -nologo -v minimal --filter "FullyQualifiedName~ProductInventorySqlAuthorityFallbackTests|FullyQualifiedName~ReadModelStockConsistencyArchitectureTests|FullyQualifiedName~GlobalReliabilityInfrastructureExtractionTests"`
