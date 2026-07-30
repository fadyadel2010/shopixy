# Product Runtime Flow Diagrams

Date: 2026-07-30

Final certification: `WRITE PATH CERTIFICATION FAILED`

## Canonical Product Authoring Flow

```mermaid
flowchart TD
    A["HTTP ProductsController create/update/delete/deactivate/unit endpoint"] --> B["Authorize role"]
    B --> C["DTO request"]
    C --> D["ProductAuthoringCommandMapper"]
    D --> E["ProductAuthoringOrchestrator.AuthorAsync"]
    E --> F["Idempotency admission"]
    F --> G["Reconstruct product facts"]
    G --> H["Policy validation"]
    H --> I["Begin DbTransaction"]
    I --> J["Claim idempotency"]
    J --> K["Mutate Product/ProductUnit/ProductVariant"]
    K --> L["Apply compatibility mirrors"]
    L --> M["Stage domain events and outbox"]
    M --> N["SaveChanges"]
    N --> O["Commit"]
    O --> P["AfterCommit observers"]
    P --> Q["Projection consumers"]
    Q --> R["Mongo Product read model"]
    R --> S["Search and storefront visibility"]
```

Evidence:

- Product endpoints delegate to the authoring orchestrator at `PointOfSale.API/Controllers/ProductsController.cs:286-454` and `:1353-1643`.
- The orchestrator performs admission, reconstruction, policy, transaction, save, commit, and observer callbacks at `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:66-96` and `:467-495`.
- Product events/outbox are staged before commit at `ProductAuthoringOrchestrator.cs:587-687`.

## Inventory and Stock Flow

```mermaid
flowchart TD
    A["Stock write entry: Products, ProductVariants, Warehouses, Sales, Returns, Checkout, CommerceHub"] --> B["Authorize or service context"]
    B --> C["Load ProductVariantStock"]
    C --> D["Lock stock rows"]
    D --> E["Validate quantity and invariants"]
    E --> F["Mutate ProductVariantStock quantity or reserved quantity"]
    F --> G["Add StockMovement or Reservation when applicable"]
    G --> H["SaveChanges inside transaction"]
    H --> I["PublishCommittedStockEventsAsync or repository stock sync enqueue"]
    I --> J["Touch stock timestamps"]
    J --> K["Add StockSyncOutboxMessage"]
    K --> L["SaveChanges"]
    L --> M["Commit"]
    M --> N["Stock projection consumer"]
    N --> O["Mongo/search/storefront stock visibility"]
```

Evidence:

- Product stock adjustment: `PointOfSale.API/Controllers/ProductsController.cs:771-909`.
- Variant stock update: `PointOfSale.API/Controllers/ProductVariantsController.cs:1398-1558`.
- Warehouse inventory service: `PointOfSale.API/Services/InventoryService.cs:36-92`, `:101-194`, `:227-363`.
- Stock event/outbox support: `PointOfSale.API/Services/Inventory/ProductVariantStockMutationSupport.cs:216-250`, `:388-390`.
- Checkout stock repository: `PointOfSale.Infrastructure/Data/Repositories/ProductVariantStockRepository.cs:112-227`, `:1115-1203`, `:1274-1289`.

## Purchase Cost Authority Failure Flow

```mermaid
flowchart TD
    A["Purchase or PurchaseReturn HTTP endpoint"] --> B["Authorize role"]
    B --> C["DTO validation and idempotency"]
    C --> D["Begin DbTransaction"]
    D --> E["Lock ProductVariantStock"]
    E --> F["Apply VariantCostAuthority helper"]
    F --> G["Mutate ProductVariant.CostPrice"]
    G --> H["Mirror simple product purchase price"]
    H --> I["Mutate ProductVariantStock quantity"]
    I --> J["SaveChanges"]
    J --> K["Stock/accounting outbox"]
    K --> L["Commit"]
    L --> M["Projection/search downstream"]
    G -. "Authority failure: canonical ProductVariant cost changed outside Product Authoring/Pricing command boundary" .-> X["Certification failure"]
```

Evidence:

- `ProductVariant.CostPrice` is canonical Product variant cost at `PointOfSale.Core/Entities/ProductVariant.cs:22-73`.
- Purchase create/update/delete-revert write cost at `PointOfSale.API/Controllers/PurchasesController.cs:219-223`, `:1133-1138`, and `:1461-1464`.
- Purchase return writes cost at `PointOfSale.API/Controllers/PurchaseReturnsController.cs:540-544`.
- Simple compatibility mirrors are applied after cost mutation at `PurchasesController.cs:224`, `:1468` and `PurchaseReturnsController.cs:550-553`.

## Projection Flow

```mermaid
flowchart TD
    A["SQL authority tables: Product, ProductUnit, ProductVariant, ProductVariantStock"] --> B["Domain events and outbox rows"]
    B --> C["Projection consumers and repair services"]
    C --> D["ProductReadRepository / projection mutation boundary"]
    D --> E["Mongo Product read model"]
    C --> F["SQL authority overlay"]
    F --> G["Search index"]
    E --> H["Storefront product pages"]
    G --> H
    H --> I["Checkout visibility"]
    E -. "No reverse business authority found" .-> A
```

Evidence:

- Projection writer allowlists and business-layer Mongo write checks are in `PointOfSale.Tests/Architecture/ProductProjectionGovernanceTests.cs:5-93`.
- Mongo Product writes are in projection repository methods at `PointOfSale.Infrastructure/MongoDB/Services/ProductReadRepository.cs:194`, `:551`, `:572`, and `:607`.
- Search overlay rehydrates from SQL authority at `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs:321-329`.
- Repair services use SQL authority overlay before search updates at `PointOfSale.API/Services/ReadModels/DiscountReadModelRepairService.cs:321-333`.

## CommerceHub Listing Flow

```mermaid
flowchart TD
    A["Admin CommerceHub endpoint"] --> B["Command handler"]
    B --> C["Validate listing/product attributes"]
    C --> D["Resolve effective price through CommerceHubPriceAuthority"]
    D --> E["Create/update/unpublish CommerceHubListing"]
    E --> F["Publish listing and price events"]
    F --> G["SaveChanges/commit through handler context"]
    G --> H["Marketplace projection"]
    H --> I["CommerceHub storefront/search"]
```

Evidence:

- Publish flow: `PointOfSale.API/CommerceHub/Handlers/CommerceHubCommandHandlers.cs:181-235`.
- Update flow: `CommerceHubCommandHandlers.cs:599-638`.
- Unpublish flow: `CommerceHubCommandHandlers.cs:751-785`.
- Pricing authority: `PointOfSale.Core/Pricing/CommerceHubPriceAuthority.cs:83-111`.
- Bulk listing observation: `PointOfSale.API/Controllers/AdminCommerceHubController.cs:365-494` mutates listing state in the controller.

## Affiliate Product Policy Flow

```mermaid
flowchart TD
    A["Admin product affiliate endpoint"] --> B["AffiliateService"]
    B --> C["Load Product and AffiliateProductPolicy"]
    C --> D["Create or update policy"]
    D --> E["Touch Product projection freshness"]
    E --> F["Publish AffiliateProductPolicyChangedEvent"]
    F --> G["SaveChanges"]
    G --> H["Commit if service owns transaction"]
    H --> I["Affiliate projection consumer"]
    I --> J["Mongo Product and PublicAffiliateProducts"]
    J --> K["Affiliate storefront visibility"]
```

Evidence:

- Admin endpoints delegate to the affiliate service at `PointOfSale.API/Controllers/AdminProductAffiliateController.cs:171`, `:286`, `:326`, and `:366`.
- Affiliate service create/update/publish/save/commit paths are at `PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs:408-444` and `:626-665`.
- Affiliate projection consumer writes downstream read models at `PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateProductPolicyProjectionConsumer.cs:110`, `:521`, `:655`, `:1143`, `:1181`, `:1201`, and `:1409`.

## Product Media Flow

```mermaid
flowchart TD
    A["ProductMediaController add/remove/reorder"] --> B["Authorize role"]
    B --> C["Idempotency and request validation"]
    C --> D["Begin DbTransaction"]
    D --> E["ProductMediaService"]
    E --> F["Mutate ProductMedia"]
    F --> G["Clear variant media pointer when needed"]
    G --> H["Touch Product.UpdatedAt for projection"]
    H --> I["SaveChanges"]
    I --> J["Publish ProductMediaChangedEvent"]
    J --> K["Complete idempotency"]
    K --> L["Commit"]
    L --> M["Product projection/search media refresh"]
```

Evidence:

- Controller transaction/idempotency points: `PointOfSale.API/Controllers/ProductMediaController.cs:54-68`, `:119-148`, `:167-200`, and `:317-318`.
- Service mutations and projection touch: `PointOfSale.API/Services/ProductMediaService.cs:90-109`, `:125-181`, `:198-225`, `:289-301`.

## Decision Graph

```mermaid
flowchart TD
    A["Does every Product write path follow canonical architecture?"] --> B{"All canonical Product fields written only by correct authority?"}
    B -->|No: ProductVariant.CostPrice written by purchase and purchase return flows| F["WRITE PATH CERTIFICATION FAILED"]
    B -->|Yes| C{"All Product-related stock/media/affiliate/listing writes formally classified and governed?"}
    C -->|No| G["PASS WITH OBSERVATIONS or FAIL depending contract"]
    C -->|Yes| D{"Projection remains one-way SQL/outbox to Mongo/search?"}
    D -->|No| F
    D -->|Yes| E["WRITE PATH CERTIFICATION PASSED"]
```

The selected result is:

`WRITE PATH CERTIFICATION FAILED`

