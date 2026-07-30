# Product Write Path Runtime Architecture Certification

Date: 2026-07-30

Final certification:

`WRITE PATH CERTIFICATION FAILED`

## Certification Basis

This review treats Product-related writes as any runtime mutation of Product catalog state, Product units, Product variants, Product variant stock, Product media, Product pricing mirrors, product publication/listing state, product affiliate policy, and downstream Product projections/search visibility.

Compilation success and passing tests were not used as proof of correctness. The conclusions below are based on source-code runtime traces from entry points to persistence and downstream effects.

## Canonical Contract Used

The source establishes these canonical authorities:

| Area | Source evidence | Contract observed |
| --- | --- | --- |
| Product authoring | `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:66-96`, `:467-495` | Product create/update/delete/deactivate/unit authoring should enter through the authoring orchestrator, reconstruct facts, run policy, mutate, stage events/outbox, save once, then commit. |
| Simple product pricing | `PointOfSale.Core/Entities/ProductUnit.cs:5-26`, `PointOfSale.Core/Pricing/CatalogPricingAuthority.cs:10-14` | Default `ProductUnit` is the canonical simple-product purchase/sale price source. |
| Variant pricing | `PointOfSale.Core/Entities/ProductVariant.cs:22-73`, `PointOfSale.Core/Pricing/CatalogPricingAuthority.cs:16-23` | `ProductVariant.Price`, `OnlinePrice`, and `CostPrice` are canonical for variant products. |
| Legacy product price fields | `PointOfSale.Core/Entities/Product.cs:54-66`, `PointOfSale.Core/Pricing/ProductPricingCompatibilityMirror.cs:9-68` | `Product.PurchasePrice`, `SalePrice`, and `OnlinePrice` are compatibility mirrors, not independent authority. |
| Inventory | `PointOfSale.API/Services/InventoryService.cs:15-19`, `PointOfSale.API/Services/Inventory/ProductVariantStockMutationSupport.cs:216-250` | `ProductVariantStock` is the stock source of truth; stock events/outbox are produced from committed SQL stock mutations inside a transaction. |
| Projection | `PointOfSale.Tests/Architecture/ProductProjectionGovernanceTests.cs:5-34`, `PointOfSale.Infrastructure/MongoDB/Services/StorefrontProductProjectionMutationBoundary.cs` | Mongo/search write paths are projection infrastructure only, and must not become business authority. |
| CommerceHub listing | `PointOfSale.API/CommerceHub/Handlers/CommerceHubCommandHandlers.cs:181-235`, `:599-638`, `:751-785` | Listing publish/update/unpublish is CommerceHub authority and reads catalog price authority for effective listing prices. |
| Affiliate product policy | `PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs:408-436`, `:626-665` | Affiliate policy is affiliate authority, but it touches Product projection freshness and emits policy events. |

## Runtime Trace Summary

### Product Authoring Paths

Create, update, delete, deactivate, add unit, update unit, and delete unit are canonical Product Authoring paths.

Evidence:

- `ProductsController` is authenticated with `[Authorize]` at `PointOfSale.API/Controllers/ProductsController.cs:30`.
- Create maps DTO to authoring command and calls `_productAuthoringOrchestrator.AuthorAsync(...)` at `ProductsController.cs:286-302`.
- Update maps DTO to authoring command and calls the orchestrator at `ProductsController.cs:316-345`.
- Delete validates deletion and calls the orchestrator at `ProductsController.cs:367-408`.
- Deactivate calls the orchestrator at `ProductsController.cs:425-454`.
- Unit add/update/delete reconstruct unit facts and delegate to the orchestrator at `ProductsController.cs:1353-1448`, `:1472-1564`, and `:1585-1643`.
- The orchestrator performs idempotency admission, reconstruction, policy evaluation, mutation, outbox/event staging, save, commit, and observer callbacks in one transaction at `ProductAuthoringOrchestrator.cs:66-96` and `:467-495`.
- Outbox/domain events are staged before commit at `ProductAuthoringOrchestrator.cs:587-687` and later sections for publish/archive/delete events.

Assessment: `Correct Authority`.

### Legacy Variant Endpoints

Legacy create/update/delete/default variant endpoints are fail-closed before the old direct EF code can execute.

Evidence:

- Create variant returns `LegacyVariantMutationDeprecatedResult()` before direct transaction/write code at `ProductVariantsController.cs:118-140`.
- Update variant returns the same deprecation result at `ProductVariantsController.cs:463-469`.
- Delete variant returns before old `variant.IsDeleted` and `SaveChanges` code at `ProductVariantsController.cs:1056-1170`.
- Set-default variant returns before old default mutation code at `ProductVariantsController.cs:1224-1304`.
- Architecture tests assert these legacy endpoints are deprecated before persistence at `PointOfSale.Tests/Architecture/ProductMutationAuthorityGovernanceTests.cs:33-55`.

Assessment: `Compatibility Authority`, with residual risk because dead direct-write code remains in source.

### Stock and Inventory Paths

Stock writes are not Product Authoring. They mutate `ProductVariantStock` through inventory-specific paths and publish stock sync outbox events.

Evidence:

- Product stock adjustment endpoint is authenticated/authorized at `ProductsController.cs:771-779`, begins a transaction at `:806`, claims idempotency at `:807-817`, locks stock at `:830-835`, mutates `variantStock.Quantity` at `:850-851`, saves and publishes stock events at `:884-888`, completes idempotency and commits at `:899-909`.
- Variant stock update is authenticated/authorized at `ProductVariantsController.cs:1398-1403`, begins a transaction at `:1409`, locks stock at `:1475`, mutates stock at `:1525-1526`, saves at `:1538`, and publishes stock events at `:1553-1558`.
- Warehouse stock adjustment/transfer/opening stock delegate to `InventoryService` and commit around `WarehousesController.cs:188-279`, `:295-402`, and `:489-495`.
- `InventoryService.AdjustStockAsync` mutates `ProductVariantStock.Quantity` at `InventoryService.cs:70`; transfer mutates source and target quantities at `:148-151`; opening stock owns a transaction, mutates quantity, saves, publishes events, and commits at `:227-363`.
- `ProductVariantStockMutationSupport.PublishCommittedStockEventsAsync` requires an active mutation transaction, touches timestamps, saves, creates `StockSyncOutboxMessage`, and saves again at `ProductVariantStockMutationSupport.cs:216-250`; it throws if no current transaction at `:388-390`.

Assessment: `Shared Authority`. This is acceptable only if the canonical contract recognizes Inventory Authority as a separate Product-related write authority. It is not a Product Authoring path.

### Purchases and Purchase Returns

Purchase receipt/update/delete/revert and purchase return are live product-related write paths. They mutate inventory and also mutate canonical variant cost price outside Product Authoring.

Evidence:

- Purchase create/update/delete endpoints are authorized at `PurchasesController.cs:93-95`, `:892-894`, and `:1317-1319`.
- Purchase create begins a transaction and claims idempotency at `PurchasesController.cs:97-101`, locks stock at `:182-187`, writes `variant.CostPrice` through `VariantCostAuthority.ApplyPurchaseReceipt` at `:219-223`, mirrors simple product purchase price at `:224`, mutates stock at `:225-226`, saves at `:369`, publishes stock events at `:374-380`, and completes idempotency/commit at `:382-385` and `:516-517`.
- Purchase update repeats the same cost and stock mutation pattern at `PurchasesController.cs:896-897`, `:1096`, `:1133-1139`, `:1280`, and `:1284-1287`.
- Purchase delete/revert mutates cost through `VariantCostAuthority.ApplyPurchaseReturn`, mirrors simple purchase price, decrements stock, saves, publishes events, and commits at `PurchasesController.cs:1349-1365`, `:1461-1478`, `:1504-1532`.
- Purchase return begins a transaction and idempotency claim at `PurchaseReturnsController.cs:233-243`, locks stock at `:450-453`, mutates `variantEntity.CostPrice` at `:540-544`, mirrors simple product price at `:550-553`, decrements stock at `:556-557`, saves at `:671`, publishes stock events at `:672-678`, and commits at `:680-681`.
- `ProductVariant.CostPrice` is declared canonical for variants at `PointOfSale.Core/Entities/ProductVariant.cs:22-73`.

Assessment: `Wrong Authority` for variant cost price mutation; `Inventory Authority` for stock mutation. This is the primary certification failure.

### Sales and Sales Returns

Sales write stock, not catalog product fields. Sales returns write stock through the canonical sales-return controller. The legacy sales return endpoint is disabled.

Evidence:

- Sale create begins a transaction at `SalesController.cs:146`, locks stock at `:242`, validates availability at `:268`, decrements stock at `:309-310`, enqueues accounting at `:446`, publishes stock events at `:471`, and completes idempotency with save/commit at `:985-986`.
- Sale update begins a transaction at `SalesController.cs:1361`, locks stock at `:1451`, decrements stock at `:1518-1519`, publishes stock events at `:1675`, and completes idempotency at `:1681`.
- Sale delete reverts stock in `RevertSaleEffects`, incrementing quantities at `SalesController.cs:1974-1975`, saving at `:2034`, then publishing/committing at `:1783-1789`.
- Legacy `SalesController.ProcessReturn` returns 410 before disabled old code at `SalesController.cs:2196-2207`.
- Canonical `SalesReturnsController.FinalizeSalesReturnAsync` locks stock at `SalesReturnsController.cs:673-686`, increments stock at `:759-761`, adds movement at `:774-785`, adds the return at `:866`, saves with retry at `:942-947`, publishes stock events at `:949-955`, saves idempotency at `:971-976`, and commits at `:977`.

Assessment: `Inventory Authority` and `Sales Return Authority`. No Product Authoring bypass was found in live sale/return catalog field writes.

### Checkout, Reservation, and CommerceHub Inventory

Checkout and CommerceHub inventory consistency mutate stock through repository/service authority, not Product Authoring.

Evidence:

- Checkout service uses `IProductVariantStockRepository` at `CheckoutService.cs:238` and constructor injection at `:305`.
- `ProductVariantStockRepository.ReserveStockAsync` runs inside `ExecuteWithinMutationTransactionAsync`, mutates `stock.ReservedQuantity`, adds `InventoryReservation`, saves, and enqueues stock sync messages at `ProductVariantStockRepository.cs:112-227`.
- The repository enqueues stock sync outbox messages by touching stock timestamps, saving, constructing events, adding `StockSyncOutboxMessages`, and saving at `ProductVariantStockRepository.cs:1115-1203`.
- The transaction wrapper owns a transaction when no ambient transaction exists at `ProductVariantStockRepository.cs:1274-1289`.
- `InventoryConsistencyService` contains additional stock mutation paths with save and stock sync outbox enqueue points at `InventoryConsistencyService.cs:292-293`, `:392-393`, `:541-542`, `:673-674`, `:1072-1073`, and `:1219-1220`.

Assessment: `Inventory Authority` / `CommerceHub Inventory Authority`. Product-related and legitimate if the canonical contract includes these shared authorities; not Product Authoring.

### CommerceHub Listing Paths

CommerceHub listing publish/update/unpublish are CommerceHub authority paths. They do not mutate SQL Product rows, but they are Product-related publication/listing writes.

Evidence:

- Publish validates attributes, loads listing, resolves effective price through price authority, creates `CommerceHubListing`, adds it to the DbContext, and publishes listing/price override events at `CommerceHubCommandHandlers.cs:181-235`.
- Update calls `listing.Update(...)`, resolves price authority, and publishes listing update events at `CommerceHubCommandHandlers.cs:599-638`.
- Unpublish calls `listing.Unpublish()` and publishes unpublish events at `CommerceHubCommandHandlers.cs:751-785`.
- Admin bulk operation mutates listings directly in the controller with `listing.Unpublish()` / `ApplySafeBulkPublish(...)` and `SaveChangesAsync` at `AdminCommerceHubController.cs:365-494`.

Assessment: `CommerceHub Authority`. Observation: bulk listing mutation is controller-local, with no explicit idempotency/transaction evidence in the inspected path.

### Affiliate Product Policy

Affiliate product policy writes are Affiliate authority. They touch Product projection freshness and emit affiliate policy events.

Evidence:

- Admin product affiliate endpoints delegate to `IAffiliateService` at `AdminProductAffiliateController.cs:171`, `:286`, `:326`, and `:366`.
- `AffiliateService` updates existing policy fields, touches product projection version, publishes an event, saves, and commits at `AffiliateService.cs:408-421`.
- It creates new `AffiliateProductPolicy`, touches product projection version, publishes, saves, and commits at `AffiliateService.cs:432-444`.
- `PublishAffiliateProductPolicyChangedAsync` emits policy data at `AffiliateService.cs:626-665`.

Assessment: `Affiliate Authority`. Observation: this is a Product-related write that mutates Product projection freshness outside Product Authoring.

### Product Media

Media writes are Media authority and directly mutate `ProductMedia` plus Product/Variant projection freshness fields.

Evidence:

- `ProductMediaController` is role-protected at `ProductMediaController.cs:14`, requires idempotency for add at `:54-68`, and wraps add/remove/reorder in transactions at `:66`, `:128`, and `:179`.
- `ProductMediaService.AddMediaAsync` creates `ProductMedia`, adds it, touches product projection freshness, saves, and publishes `ProductMediaChangedEvent` at `ProductMediaService.cs:90-109` and `:289`.
- `RemoveMediaAsync` removes media, clears variant media pointers, touches product projection freshness, and saves at `ProductMediaService.cs:125-181`.
- `ReorderMediaAsync` updates display order, touches product projection freshness, and saves at `ProductMediaService.cs:198-225`.
- `TouchProductForMediaProjection` sets `product.UpdatedAt` at `ProductMediaService.cs:299-301`.

Assessment: `Media Authority` with Product projection marker writes. This is not Product Authoring.

### Projection, Mongo, Search, and Repair

Projection writes are generally infrastructure-only and flow from SQL/outbox/events to Mongo/search.

Evidence:

- `ProductProjectionGovernanceTests` approve projection mutation files and reject business-layer direct Mongo writes at `ProductProjectionGovernanceTests.cs:5-34` and `:46-93`.
- `ProductReadRepository` writes Mongo projections through `ReplaceOneAsync` and targeted `UpdateOneAsync` calls at `ProductReadRepository.cs:194`, `:551`, `:572`, and `:607`.
- `AffiliateProductPolicyProjectionConsumer` updates Product Mongo and public affiliate products as projection at `AffiliateProductPolicyProjectionConsumer.cs:110`, `:521`, `:655`, `:1143`, `:1181`, `:1201`, and `:1409`.
- `ProductSearchSqlAuthorityOverlay` rehydrates search/projection state from SQL authority at `ProductSearchSqlAuthorityOverlay.cs:175`, `:211`, and `:321-329`.
- `DiscountReadModelRepairService` repairs Mongo/search using SQL authority overlay and search indexing/deletion at `DiscountReadModelRepairService.cs:217`, `:295`, `:321`, `:329`, and `:333`.
- `ProductsController.SyncStockToMongo` is repair-style and publishes stock changed events from tenant-scoped SQL stock at `ProductsController.cs:1997-2079`.

Assessment: `Infrastructure Only`. No reverse Mongo/search business authority was found in the inspected paths.

### Development Seed

Development seed code creates Product/ProductUnit rows directly.

Evidence:

- `DevelopmentCommerceHubSeedService` creates `Product`, adds it to the DbContext, creates a default unit, and assigns Product fields at `DevelopmentCommerceHubSeedService.cs:241-288`.

Assessment: `Infrastructure Only` if development-only. `Forbidden` if callable in production.

## Transaction Review

| Path family | Transaction evidence | Commit assessment |
| --- | --- | --- |
| Product Authoring | `ProductAuthoringOrchestrator.cs:467-495` | Exactly one transaction commit. Idempotency, mutation, outbox/event staging, save, and commit occur in one wrapper. |
| Product stock adjustment | `ProductsController.cs:806-909` | One explicit transaction commit. Multiple `SaveChanges` occur before commit, including stock sync event staging and idempotency completion. |
| Variant stock update | `ProductVariantsController.cs:1409-1558` | One explicit transaction visible. Multiple saves before commit are expected because stock sync support stages outbox rows. |
| Warehouse inventory | `WarehousesController.cs:229-279`, `:336-402`; `InventoryService.cs:227-363` | Explicit transactions for adjustment/transfer/opening stock. |
| Purchases | `PurchasesController.cs:97-101`, `:369-385`, `:516-517`, `:896-897`, `:1280-1287`, `:1333-1365` | Transactional, but cost authority is wrong for canonical Product variant cost. |
| Purchase returns | `PurchaseReturnsController.cs:233-243`, `:671-681` | Transactional, but cost authority is wrong for canonical Product variant cost. |
| Sales and sales returns | `SalesController.cs:146`, `:985-986`, `:1361`, `:1789`; `SalesReturnsController.cs:942-977` | Transactional stock mutation paths. Sales return has retry around save and post-commit refund accounting compensation. |
| Checkout stock repository | `ProductVariantStockRepository.cs:112-227`, `:1115-1203`, `:1274-1289` | Repository can own mutation transaction. Outbox is staged before repository commit. |
| Product media | `ProductMediaController.cs:66`, `:128`, `:179`, `:317-318`; `ProductMediaService.cs:90-225` | Controller transaction plus service save. Product projection timestamp is mutated within media transaction. |
| Affiliate policy | `AffiliateService.cs:408-444` | Service owns or joins a transaction and commits if it owns it. |
| CommerceHub listing | `CommerceHubCommandHandlers.cs:181-235`, `:599-638`, `:751-785`; `AdminCommerceHubController.cs:365-494` | Handler paths stage listing events; bulk controller path lacks inspected explicit idempotency/transaction evidence. |
| Projection repair | `ProductsController.cs:2011-2079`; projection services above | Repair paths derive from SQL authority and publish/update projections. |

No evidence was found that Mongo/search acts as a reverse Product authority. The transaction concern is not "SQL was reached"; it is that several Product-related field authorities are distributed across runtime paths.

## Projection Review

The SQL to outbox/event to projection to Mongo/search model is mostly upheld.

Positive evidence:

- Product Authoring stages authoritative events/outbox before commit in `ProductAuthoringOrchestrator.cs:587-687`.
- Stock mutation support creates stock sync outbox rows from SQL stock changes in `ProductVariantStockMutationSupport.cs:216-250`.
- Projection governance tests constrain direct Mongo projection writes to approved infrastructure files at `ProductProjectionGovernanceTests.cs:5-93`.
- Search repair overlays SQL authority before indexing at `ProductSearchSqlAuthorityOverlay.cs:321-329` and `DiscountReadModelRepairService.cs:321-333`.

Observations:

- Product media and affiliate policy mutate Product projection freshness markers (`Product.UpdatedAt` / projection version) outside Product Authoring. These are not Mongo authority violations, but they are Product-row writes outside the canonical Product authoring path.
- `ProductsController.SyncStockToMongo` is a controller-triggered repair path that emits stock projection events from SQL. It has idempotency and a transaction, but it remains a privileged projection-repair entry point.

## Compatibility Review

Compatibility mirrors are centralized and intentionally used by Product Authoring and purchasing flows.

Evidence:

- Product legacy price fields are documented as compatibility mirrors in `Product.cs:54-66`.
- `ProductPricingCompatibilityMirror` centralizes simple Product and default variant mirror logic at `ProductPricingCompatibilityMirror.cs:9-68`.
- Product Authoring applies simple price compatibility mirror through `ProductAuthoringOrchestrator.cs:1185-1193`.
- Purchase receipt/return flows call the mirror when they update variant cost/default unit purchase price at `PurchasesController.cs:219-224`, `:1461-1468`, and `PurchaseReturnsController.cs:540-553`.
- Pricing governance tests explicitly require centralized mirror usage at `PricingAuthorityConsolidationCertificationTests.cs:65-94`.

Compatibility mirror timing is generally before commit. The compatibility problem is not mirror existence; it is that purchase flows mutate canonical variant cost before mirroring, outside Product Authoring.

## Forbidden Write Search Result

Forbidden or hidden write classes searched:

| Search target | Result |
| --- | --- |
| Direct EF mutations in product controllers | Current Product create/update/delete/unit endpoints delegate to Product Authoring. Legacy variant direct writes remain in source but are guarded by early deprecation returns. |
| Controller writes | Live controller-local product-related writes exist for stock adjustment, stock sync repair, warehouse inventory orchestration, and CommerceHub bulk listings. |
| Repository bypass | `ProductVariantStockRepository` directly mutates stock reservations and owns transactions for checkout/reservation workflows. This is inventory authority, not Product Authoring. |
| Mongo business writes | No inspected business controller/service path writes Mongo as Product authority; projection tests police this boundary. |
| Projection writes | Projection writes exist in approved read-model repositories/consumers/repair services. |
| Background service writes | CommerceHub inventory consistency and projection consumers perform Product-related downstream writes. |
| Repair writes | Stock-to-Mongo sync and discount/search repair paths exist; inspected repair paths derive from SQL authority. |
| Legacy endpoints | Variant endpoints and old sales return code are disabled/fail-closed, but legacy write code remains present. |
| Reflection/dynamic writes | No reflection or dynamic Product mutation path was found in inspected source. |
| Hidden SaveChanges | Multiple `SaveChanges` occur inside Product-related transactions, especially stock/outbox/idempotency paths. Multiple saves are not by themselves separate commits when wrapped in a transaction. |

## Architecture Test Review

Existing architecture tests help, but they do not fully certify every Product-related write path.

Positive coverage:

- `ProductMutationAuthorityGovernanceTests.cs:6-29` verifies Product endpoints delegate to Product Authoring and avoid direct Product/ProductUnit/ProductVariant/SaveChanges in those endpoint bodies.
- `ProductMutationAuthorityGovernanceTests.cs:33-69` verifies legacy variant and migration endpoints are deprecated before persistence.
- `ProductMutationAuthorityGovernanceTests.cs:72-118` requires selected infrastructure bypasses and matrix rows to be classified.
- `ProductProjectionGovernanceTests.cs:5-147` constrains projection writers and repository boundary usage.
- `PricingAuthorityConsolidationCertificationTests.cs:8-152` validates canonical pricing declarations, mirror centralization, and CommerceHub price authority use.

Gaps:

- The mutation governance test does not fully enumerate purchases, purchase returns, sales, sales returns, warehouses, checkout stock repository, CommerceHub bulk listing, CommerceHub inventory consistency, media, and affiliate policy as Product-related write paths.
- The tests allow purchase cost mutation/mirroring but do not prove that this is routed through a canonical Product or Pricing command authority.
- Transaction regression coverage is concentrated in Product Authoring and selected stock paths, not every downstream Product-related mutation family.

## Runtime Consistency Review

Runtime consistency is designed around SQL authority flowing downstream:

SQL Product/ProductUnit/ProductVariant/ProductVariantStock writes -> domain/outbox events -> Mongo/read-model projection -> search overlays/indexing -> Affiliate and CommerceHub public surfaces -> checkout/storefront visibility.

Source-backed evidence supports this direction for inspected projection paths. However, consistency certification fails at the authority layer because purchase receipt/return flows mutate canonical variant cost directly, and several other Product-related write paths mutate Product-adjacent state outside Product Authoring. Even if the resulting SQL commits and projections are internally consistent, reaching SQL is not proof of canonical architecture compliance.

## Final Decision

`WRITE PATH CERTIFICATION FAILED`

Reasons:

1. Purchase receipt/update/delete/revert and purchase return mutate canonical `ProductVariant.CostPrice` outside Product Authoring/Pricing command authority.
2. Product-related mutation authority is distributed across Product controllers, purchase controllers, return controllers, warehouse/inventory services, checkout stock repository, CommerceHub services, affiliate service, and media service.
3. Projection boundaries are comparatively strong, but architecture tests do not prevent all Product-related write bypasses or transaction regressions across the full runtime surface.

