# Merchant Platform — Final Canonical Architecture

Implementation-backed architecture specification for the Merchant Platform bounded context
(POS Admin + Merchant Storefront) of Shopixy / PointOfSale.

> Every statement in this document is derived from the C# implementation under
> `PointOfSale.Core/`, `PointOfSale.Infrastructure/`, and `PointOfSale.API/`. Where a
> conclusion is supported, the `file:line` evidence is cited. The terminology-only System
> Guides under `.ai/hermes/systems/pos-admin/` and `.ai/hermes/systems/storefront/` are
> treated as secondary sources; every normative claim was re-verified against code. Prior
> certification `.md` files in `docs/architecture/` were NOT treated as authoritative — they
> are referenced only where they independently confirm a code finding.
>
> Canonical paths:
> - Core aggregates: `PointOfSale.Core/Entities/`
> - Service contracts: `PointOfSale.Core/Interfaces/`
> - Domain events: `PointOfSale.Core/Events/`
> - Outbox entities: `PointOfSale.Core/Entities/{OutboxMessage,AccountingOutboxMessage,MarketplaceEventOutboxMessage,MarketplaceProjectionOutboxMessage,StockSyncOutboxMessage,PaymentCancellationOutboxMessage,CommerceHubPaymentCompensationOutboxMessage,MarketplaceEventOutboxDeadLetterArchive}.cs`
> - Domain services: `PointOfSale.API/Services/`
> - MassTransit projection consumers: `PointOfSale.Infrastructure/Consumers/`
> - SQL authority overlay / repair-on-read: `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs`
> - Outbox publisher: `PointOfSale.Infrastructure/Jobs/OutboxPublisherJob.cs`
> - DbContext: `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`

This document synthesizes four independent reverse-engineering passes (POS Admin
merchandising write runtimes, POS Admin operations/content write runtimes, Merchant
Storefront runtime, and Cross-Domain / Outbox / CQRS / Search / Technical-Debt) into ONE
coherent architecture and explains not only HOW the Merchant Platform works, but WHY it was
architected this way.

---

## Executive Summary

The Merchant Platform is the **merchant-facing bounded context** of Shopixy. It is NOT
CommerceHub, NOT SuperAdmin, and NOT the Marketplace. It is the platform a merchant uses to
build and operate their own store: a back-office (POS Admin) and a customer-facing storefront
(Merchant Storefront), treated here as ONE architectural system sharing a single SQL
authority, a single outbox backbone, and a single derivative-projection pipeline.

Reverse-engineered facts (all code-backed):

- **PostgreSQL is the single authoritative write model.** Every Merchant Platform mutation —
  catalog, pricing, inventory, orders, customers, CMS, config — is committed to PostgreSQL
  through EF Core `ApplicationDbContext`. Mongo, Elasticsearch, and Redis are **derivative
  projections only**. They are normally written by MassTransit consumers and may also be
  rebuilt by explicit SQL-authority repair/sync paths that re-read PostgreSQL before writing
  derivative state (`ProductCreatedEventConsumer.cs:27`, `ProductSqlAuthorityProjectionWriter.cs:42`,
  `CmsPageProjectionConsumer.cs:19`, `StorefrontConfigProjectionConsumer.cs:31`,
  `DiscountReadModelRepairService.cs:54`).
- **Publication is event-driven through a transactional outbox.** Domain commands stage
  `OutboxMessage` rows in the same DbContext transaction as the SQL mutation
  (`MassTransitEventPublisher.cs:124`, `OutboxPublisherJob.cs:26`); a Hangfire worker drains
  them to RabbitMQ every 5 seconds (`OutboxPublisherJob.cs:24`). Critical events are ordered
  by monotonic `EventVersion`, gated, and — on exhaustion — kept operator-visible and slow
  replayed rather than dropped (`OutboxPublisherJob.cs:146-236`).
- **Storefront reads are projection-candidate + SQL-reauthorization.** Derivative stores
  (Mongo/ES/Redis) are used only to *find candidates and accelerate reads*; **every public
  visibility decision and every financial/stock decision is recomputed from SQL** before
  being served or committed. The universal repair-on-read primitive is
  `ProductSearchSqlAuthorityOverlay.ApplyAsync`
  (`ProductSearchSqlAuthorityOverlay.cs:23-344`), invoked by product, home, discovery,
  search, and SEO reads.
- **Checkout is fail-closed and SQL-authoritative.** Prices and stock are re-validated
  against `ProductVariants` / `ProductVariantStocks` (NOT projections) and reserved inside a
  single PostgreSQL order-creation transaction
  (`CheckoutService.cs:64-132, 4047-4096, 5114-5294, 1295-1347`).
- **Visibility is a SQL conjunction**, re-verified at projection time — never a naked UI
  toggle. `IsDeleted=false AND IsActive=true AND IsOnlineVisible=true AND
  PublishStatus=Published` is the gate, enforced both in the write path
  (`ProductsController.cs:845-856`) and again in the projection writers
  (`ProductSqlAuthorityProjectionWriter.cs:94`, `ProductVisibilityLifecycleProjectionConsumer.cs:130-136`).
- **Orders and payment/fulfillment status are immutable-by-design.** `Order.PaymentStatus`
  can only be changed by `IPaymentLifecycleService` and `Order.FulfillmentStatus` only by
  `IOrderFulfillmentService`; direct assignment is blocked at runtime by
  `SaveChangesInterceptor`s (`Order.cs:108-149`). Completed orders reverse only via
  `OrderRefund`/`OrderRefundLine` (`OrderRefund.cs:6`, never in-place edits).

---

## Architecture Vision

### Merchant Platform Purpose

The Merchant Platform turns a tenant's operational intent (catalog, pricing, inventory,
customers, content, configuration) into (a) a merchant-operated back office (POS Admin) and
(b) a tenant-branded customer commerce experience (Merchant Storefront). It is implemented as
a single bounded context that:

- **owns the operational intent** for one tenant — it is the entry point and orchestration
  surface for mutations,
- **writes durable business truth to PostgreSQL** through lifecycle-owning services,
- **emits transactional outbox events** that hydrate Mongo/ES/Redis read models and
  cross-domain integrations,
- **defers cross-cutting lifecycles** (payment truth, order/fulfillment lifecycle, ledger /
  accounting, marketplace, shipping carrier execution) to their owning services.

Evidence: `ProductsController.CreateProduct` commits SQL + outbox atomically
(`ProductsController.cs:379-447`); `CheckoutService` creates the order in a single
PostgreSQL transaction and routes payment through `IPaymentLifecycleService`
(`CheckoutService.cs:121-129`); `AdminOrderService` blocks terminal-state edits and defers
to dedicated endpoints (`AdminOrderService.cs:391-407`).

### Merchant Platform Scope

Within the Storefront, POS Admin, CommerceHub, Payments, Treasury, Identity, Shipping,
Analytics, Search, and SuperAdmin surfaces, the Merchant Platform owns:

- The **tenant back-office and point of sale** (`PointOfSale.Web`) over `api/admin/*`.
- The **tenant-branded customer storefront** (`PointOfSale.Storefront`) over `api/storefront/*`.
- The **tenant's own catalog, pricing, inventory, orders, customers, CMS, and store
  configuration** as first-class SQL aggregates.
- The **publishing pipeline** that projects that tenant truth into derivative read models.

### What Merchant Platform Owns

Implemented runtime truth owned by Merchant Platform services (SQL tables):

| Concept | Owner (service) | SQL table / aggregate |
|---|---|---|
| Product | `ProductsController` (+ `ProductVariantService`) | `Products`, `ProductVariants` |
| Variant / SKU | `ProductVariantService` | `ProductVariants` |
| Category | `CategoriesController` | `Categories` |
| Brand / Collection | string column on `Product` (no entity) | `Products.Brand` |
| Unit | `ProductsController` (+ `UnitsController`) | `Units`, `ProductUnits` |
| Price | Product/Variant columns | `Products.OnlinePrice/SalePrice`, `ProductVariants.*Price` |
| Inventory / Stock | `InventoryService` (+ `CheckoutService` reservation) | `ProductVariantStocks`, `StockMovements`, `InventoryReservations` |
| Warehouse | `WarehousesController` | `Warehouses`, `WarehouseStock` |
| Coupon | `CouponsController` | `Coupons`, `CouponUsages` |
| Promotion | `PromotionsController` | `Promotions`, `PromotionUsages` |
| SmartOffer | `AdminSmartOffersController` | `SmartOffers` |
| Order (e-commerce) | `AdminOrderService` (+ `CheckoutService` create) | `Orders`, `OrderItems`, `OrderRefunds` |
| Customer / Supplier | `CustomersController` (+ `LedgerService`) | `Customers`, `Suppliers`, `AccountLedgers`, `PartyLedgerAdjustments` |
| Tax | `TaxCalculationService` (config) | `SystemSettings` (`Tax.Enable`/`Tax.Rate`), `Tenants.Country` |
| Shipping method | `ShippingMethodsController` | `ShippingMethods`, `CarrierConfigurations` |
| Payment method config | `PaymentMethodsAdminController` | `TenantPaymentMethods` |
| Store config / Theme | `AdminStoreConfigService` | `StoreConfigs`, `ThemePresets` |
| CMS pages | `AdminCmsService` | `CmsPages` |
| Navigation / Footer | `AdminStoreConfigService` | `StoreConfig.CustomizationJson`, `StorefrontFooterSections` |
| Media | `ProductMediaService` / `R2StorageService` | `ProductMedia` + R2 object store |
| Analytics events | `AnalyticsEventConsumer` | Mongo `analytics` + Redis hot store |
| Checkout session | `CheckoutSessionService` | `CheckoutSessions` |
| Cart | `CartService` | Redis ONLY (coupon state in `StorefrontCartCouponStates`) |

### What Merchant Platform Does NOT Own

Reverse-engineered from service dependencies, interceptors, and guards:

- **Payment lifecycle truth** — owned by `IPaymentLifecycleService`
  (`PointOfSale.Core/Interfaces/Payments/IPaymentLifecycleService.cs:11-15`). Direct
  `PaymentStatus` writes are blocked by `PaymentStatusSaveChangesInterceptor`
  (`Order.cs:108-149`). POS Admin only *configures* payment methods.
- **Order / fulfillment lifecycle truth** — `FulfillmentStatus` only via
  `IOrderFulfillmentService` (interceptor at `Order.cs:182-219`). Carrier execution is
  Shipping-owned.
- **Ledger / accounting postings** — emitted by the Merchant Platform as
  `AccountingOutboxMessage` but posted by the Treasury/Accounting owning service
  (`AccountingOutboxProcessor.cs:50-73`).
- **Marketplace / CommerceHub** — CommerceHub reads tenant catalog/inventory as a *consumer*;
  the Merchant Platform does not own marketplace orders, listings, payouts, or vendor
  earnings.
- **Cross-tenant data** — every read and write is `TenantId`-scoped; `ITenantContext`
  (`PointOfSale.Core/Interfaces/ITenantContext.cs:8`) enforces "necessary-not-sufficient"
  tenancy and `X-Tenant-Id` is stripped at the boundary.
- **Notification transport** — Merchant Platform stores channel/template *config*; the
  sending runtime (WhatsApp Baileys, email dispatch) is a separate domain.

---

## Architecture Principles

1. **Single SQL authority.** All durable truth is PostgreSQL. Writes never target Mongo/ES/Redis.
2. **Transactional outbox.** Business mutation and event stage share one DbContext transaction
   (`MassTransitEventPublisher.cs:124`, `OutboxPublisherJob.cs:491-498`).
3. **EventVersion is the ordering key, not time.** Monotonic aggregate version prevents stale
   events overtaking newer state (`OutboxPublisherJob.cs:601-659`, `OutboxMessage.cs:56`).
4. **Repair-on-read (SQL reauthorization).** Derivative projections are never trusted for
   public visibility or financial truth; SQL is re-imposed at read time
   (`ProductSearchSqlAuthorityOverlay.cs:85-344`).
5. **Fail-closed projection pipeline.** Unproven SQL authority → tombstone/delete, not serve.
   Broken pipeline → 503, not stale data (`CommerceHubProjectionReadinessService.cs:17-131`).
6. **Lifecycle encapsulation by interceptor.** Payment/fulfillment status mutation is allowed
   only through their lifecycle services (`Order.cs:108-219`).
7. **Critical reads are SQL-fresh.** Price/stock/coupon/order-ownership at checkout and in
   customer reads are recomputed from SQL (`CheckoutService.cs:4047-4096`,
   `StorefrontOrdersReadController.cs:91-125`).
8. **Tenant isolation everywhere.** `TenantId` on every aggregate, every event, every
   projection; consumer TenantId validation rejects missing/empty tenant
   (`ProductCreatedEventConsumer.cs:61-73`).

---

## Core Aggregates

The Merchant Platform aggregates live in `PointOfSale.Core/Entities/` and are mapped in
`ApplicationDbContext` (`PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`). They
implement `ITenantOwned` (or `TenantEntity`) and `IProjectionVersioned` (monotonic
`ProjectionMutationVersion`). Key aggregates: `Product`, `ProductVariant`, `Category`,
`Unit`, `ProductUnit`, `Warehouse`, `ProductVariantStock`, `StockMovement`,
`InventoryReservation`, `Coupon`, `Promotion`, `SmartOffer`, `Order`, `OrderItem`,
`OrderRefund`, `Customer`, `Supplier`, `ShippingMethod`, `CarrierConfiguration`,
`TenantPaymentMethod`, `StoreConfig`, `CmsPage`, `ProductMedia`, `ThemePreset`,
`CheckoutSession`, `StorefrontCartCouponState`, plus outbox entities.

---

## Merchant Runtime

The Merchant Platform runtime is the union of two surfaces — **POS Admin** (merchant
back-office, `api/admin/*`, served by `PointOfSale.Web`) and **Merchant Storefront**
(tenant-branded customer commerce, `api/storefront/*`, served by `PointOfSale.Storefront`) —
sharing one SQL authority, one outbox backbone, and one derivative-projection pipeline.
Together they form a single merchant-operated system: POS Admin authors truth, Storefront
presents and collects intent against that truth, and both converge on PostgreSQL.

---

## POS Admin Runtime

POS Admin is the tenant back-office / point-of-sale surface. It owns the *operational intent*
for a tenant: catalog setup, pricing, inventory, purchasing, warehouse state, customer /
supplier records, POS sales and returns, CMS publication, store configuration, tenant-scoped
projection repair, and reporting. Crucially, "owns the responsibility" means POS Admin is the
*entry point and orchestration surface*; the durable truth is always written by lifecycle
owners in PostgreSQL.

### Catalog (`api/admin/products`, `api/admin/categories`, `api/admin/units`, `api/admin/warehouses`)

- **Write authority:** `ProductsController` (no `ProductService` exists —
  `ProductsController.cs:262` create, `:480` update, `:813` soft-delete) and
  `ProductVariantService` (variants). SQL aggregates: `Product`, `ProductVariant`,
  `ProductVariantAttribute`, `ProductMedia`, `ProductUnit`, `Unit`, `Category`, `Warehouse`,
  `ProductVariantStock`.
- **Brand / Collection are NOT entities** — `Brand` is a plain string column on `Product`
  (`Product.cs`; used at `ProductsController.cs:366,420`). No `Brand`/`Collection` SQL table exists.
- **Phase 11.5 variant rule:** if `HasVariants=true`, the product is a container (barcode/SKU
  null, prices 0, `TrackStock=false`); variants own the commerce fields
  (`ProductsController.cs:328-345`). Every product gets a default variant
  (`EnsureDefaultVariantAlignedAsync`, `:388-390`).
- **Events:** `ProductCreatedEvent` / `ProductUpdatedEvent` / `ProductDeletedEvent` /
  `ProductArchivedEvent` / `ProductPublishStatusChangedEvent` / `ProductVariant*Event` /
  `ProductStockChangedEvent` / `ProductPriceChangedEvent` / `ProductMediaChangedEvent`.
- **Visibility authority is SQL**, not a UI toggle (see Visibility Conjunction below).

### Pricing

- **SQL price authority:** prices are columns, not a separate aggregate. `Product.SalePrice`
  (POS/legacy, `Product.cs:61`), `Product.OnlinePrice` (nullable, **storefront source of
  truth**, falls back to `SalePrice`, `Product.cs:67` + `ProductEvents.cs:45-50`),
  `Product.PurchasePrice`, `PriceIncludesTax`, `TaxRate`; per-unit `ProductUnit.SalePrice`;
  variant-level prices on `ProductVariant` projected via `ProductVariantProjectionConsumer`.
- **No price-list / offer-price aggregate** exists. Discount pricing is modeled via
  Promotions, Coupons, SmartOffers.
- **Pricing services are read/compute only:** `StorefrontPricingService`,
  `TaxCalculationService`, `ShippingCalculationService` read projections/SQL; price mutation
  happens only through the Product/Variant write paths
  (`PointOfSale.API/Services/Pricing/`).

### Inventory

- **Stock authority:** `ProductVariantStock` is the ONLY mutable stock source of truth
  (`InventoryService.cs:15`): `Quantity`, `ReservedQuantity`, `DamagedQuantity`,
  `AvailableQuantity = Quantity - ReservedQuantity`, `ProjectionMutationVersion`.
- **Append-only ledger:** `StockMovement` snapshots every change
  (`StockMovement.cs:15`). Reservations: `InventoryReservation` (order-scoped,
  `RemainingReservedQuantity` monotonic).
- **`InventoryService.AdjustStockAsync`** locks the stock row `FOR UPDATE` before mutation to
  serialize writers and prevent lost updates (`InventoryService.cs:51-92`).
- **Reservation / release / commit** happen at checkout/payment via `CheckoutService` +
  `CheckoutInventoryAuthorityGuard` (loads reservations `FOR UPDATE`, asserts
  `ReservedQuantity >= required`, `CheckoutInventoryAuthorityGuard.cs:204-298`).
- **`InventoryConsistencyService`** (CommerceHub) reconciles marketplace reservations against
  SQL stock authority.

### Coupon / Promotion / SmartOffer

- **Coupon** (`CouponsController`): `Coupon` aggregate with `NormalizedCode`, discount,
  usage, date windows, `RowVersion`. Creates/updates/deletes publish `Coupon*Event` BEFORE
  `SaveChangesAsync` (`CouponsController.cs:264,358,406,460`). Validation/usage is SQL-backed
  (`ICouponValidationService`), not projection-trusted.
- **Promotion** (`PromotionsController`): `Promotion` aggregate with `ProjectionMutationVersion`
  monotonic guard (`Promotion.cs:147,154-163`). Publishes `Promotion*Event` before save.
  Compute engine `PromotionEngine` is read/apply only.
- **SmartOffer** (`AdminSmartOffersController`): `SmartOffer` can mint/update/remove a generated
  coupon as part of the SmartOffer SQL mutation; those real coupon lifecycle changes publish the
  derived `Coupon*Event` before commit. The admin generated-coupon sync endpoint is not a fake
  coupon lifecycle publisher: it calls `DiscountReadModelRepairService.RebuildCouponsAsync` for
  scoped SQL-authority projection repair (`AdminSmartOffersController.cs:328-354`,
  `DiscountReadModelRepairService.cs:54-146`).

### Orders / Sales (e-commerce Order aggregate)

- **Owner:** `AdminOrderService` (`AdminOrderService.cs:335-472`). Terminal states are blocked
  here and forced onto dedicated endpoints (`:391-407`); order immutable after
  `Delivered`/`Completed` (`:383-389`).
- **This `Order` is the e-commerce order**, distinct from POS `SalesInvoice`
  (`Order.cs:8-11`). `CheckoutService` *creates* the order; `AdminOrderService` operates it.
- **Payment status encapsulation:** `PaymentStatus` only via `IPaymentLifecycleService`
  (interceptor at `Order.cs:108-149`). **Fulfillment status** only via
  `IOrderFulfillmentService` (`:182-219`).
- **Reversal via returns:** no in-place financial edit; `OrderRefund`/`OrderRefundLine`
  (`OrderRefund.cs:6`, negative amounts rejected).
- **Projections:** `OrderProjectionConsumer` → Mongo `orders`;
  `AuthoritativePurchaseAnalyticsConsumer` re-reads SQL `Orders` to emit truthful purchase
  events.

### Customers / Suppliers / Party Ledger

- `Customer`/`Supplier` balance ledgers maintained by `LedgerService` + `IPartyBalanceMaintenanceService`
  (`LedgerService.cs:13`); manual adjustments via `PartyLedgerAdjustment`. Balances are
  authoritative in SQL `AccountLedger`/`Customer.Balance`; no storefront Mongo projection for
  party ledgers.

### Tax

- **No Tax aggregate.** Config-driven from `SystemSettings` keys `Tax.Enable`/`Tax.Rate` +
  `StorefrontPricingOptions.CountryTaxRates` + `Tenants.Country`, resolved by
  `TaxCalculationService` (`TaxCalculationService.cs:53-100`). Frozen per-order into
  `Order.TaxAmount` (`Order.cs:411`). No Mongo projection.

### Shipping (method config + carrier config)

- `ShippingMethod` + `CarrierConfiguration` (encrypted creds, full carrier graph).
  `ShippingMethodProjectionConsumer` re-fetches SQL and tombstones if `IsDeleted`
  (`ShippingMethodProjectionConsumer.cs:130-142`). **Fulfillment lifecycle is cross-domain**
  (carrier execution owned by Shipping).

### Payment Configuration (config only)

- `TenantPaymentMethod` (encrypted `ConfigEncrypted`, COD/STRIPE/PAYMOB). POS Admin configures
  only; lifecycle is a separate domain. `PaymentMethodProjectionConsumer` re-fetches SQL,
  tombstones if deleted, upserts to Mongo (no secrets, `Program.cs:1733`).

### Store Settings

- `StoreConfig` (one per tenant, draft/live theme versioning, `StorefrontStatus`,
  `CustomizationJson`). `AdminStoreConfigService` publishes/rolls back with concurrency lock +
  email-verified gate + version snapshots. `StorefrontConfigProjectionConsumer` →
  `storefront_configs` + Redis invalidation (`StorefrontConfigProjectionConsumer.cs:31-385`).

### CMS

- `CmsPage` (tenant-owned SQL aggregate, versioned, publication flags
  `IsDeleted/IsDraft/IsPublished/PublishedAt`) implements `ITenantOwned` and is protected by the
  global EF filter `!IsDeleted && TenantId == CurrentTenantId` (`CmsPage.cs:6`,
  `ApplicationDbContext.cs:663`). Navigation/Footer live as JSON inside `StoreConfig.CustomizationJson` plus
  `StorefrontFooterSection`/`Link`/`SocialLink` tables. `CmsPageProjectionConsumer` →
  `storefront_pages`, `NavigationProjectionConsumer` → `storefront_navigation`,
  `StorefrontFooterProjectionConsumer` → `storefront_footer`; all **fail closed** on unproven
  SQL authority (`CmsPageProjectionConsumer.cs:304-347`, `NavigationProjectionConsumer.cs:225-248`,
  `StorefrontFooterProjectionConsumer.cs:257-285`).

### Media

- `ProductMedia` metadata row (SQL) + externally stored media URL/object references. Product media
  admin writes metadata through `ProductMediaService`; R2/S3 storage services exist for object
  storage paths, but the current product-media runtime is metadata/URL-authoritative rather than a
  universal file-upload aggregate. `ProductMediaChangedEvent` → search-index consumer.

### Theme

- `ThemePreset` is system-level immutable, shared across tenants. Tenant selection/overrides
  live in `StoreConfig` (`ThemePresetKey`/`ThemeLivePresetKey` + draft/live JSON, versioned
  publish). Embedded into `storefront_configs` by `StorefrontConfigProjectionConsumer`
  (`:190-203`). No separate theme Mongo collection.

### Localization

- **No standalone localization service/resx.** Realized as per-field bilingual columns
  (`NameAr`, `DescriptionAr`, `TitleAr`, `LabelAr`, `DisplayNameAr`, …) projected verbatim
  into storefront docs. (A Marketplace translation subsystem exists in an adjacent context.)

### Notification Config (preferences/templates, NOT sending)

- **No `NotificationConfig` entity.** Realized as `WhatsAppAccount` channel toggles,
  `WhatsAppMessageTemplate`, `TenantAutomationSettings` (default-off), and `SystemSetting`
  rows. The sending runtime is separate (see Cross-Domain).

### Analytics

- `AnalyticsEventConsumer` (Redis hot → Mongo cold, idempotent, failures non-blocking —
  `AnalyticsEventConsumer.cs:31-37`) writes the read stores.
  `AuthoritativePurchaseAnalyticsConsumer` re-reads SQL `Orders` (payment-status authority)
  and emits the server-truth `PurchaseCompleted` event, replacing client-reported completion
  (`:18-67`). Admin/Storefront analytics controllers only READ the Mongo/Redis stores.

---

## Storefront Runtime

The Merchant Storefront is the tenant-branded customer surface. It collects and presents
customer intent; backend Core authority admits, recomputes, persists, and reconciles.
**Storefront never owns products or any durable business truth** — it reads projections and
re-authorizes them against SQL.

### Central mechanism: SQL reauthorization (repair-on-read)

`ProductSearchSqlAuthorityOverlay.ApplyAsync` (`ProductSearchSqlAuthorityOverlay.cs:23-344`)
is the universal repair-on-read primitive. It re-loads `Products` + `Variants` + `Stocks` +
`Media` + live `Categories` + `Promotions` from SQL (AsNoTracking, IgnoreQueryFilters) and
**overwrites** the Mongo/ES candidate's price, stock, variant, promotion, media, category,
and visibility fields. It returns `null` (→ suppress/delete) when tenant is inactive
(`:92-95`), SQL product missing/deleted (`:129-132`), or category lineage broken
(`:134-143`). It forces `IsOnlineVisible = IsOnlineVisible && PublishStatus == Published`
(`:170-172`). Invoked by product, home, discovery, search, and SEO reads.

### Read authority per concept

| Concept | Storefront read source | Authority overlay / critical revalidation | Evidence |
|---|---|---|---|
| Product | Mongo candidate → Redis cache | `ProductSearchSqlAuthorityOverlay.ApplyAsync` | `StorefrontProductService.cs:73-128,655-810` |
| Variant | Mongo candidate | same overlay (loads `Variants` w/ stocks) | `ProductSearchSqlAuthorityOverlay.cs:231-267` |
| Category | Mongo tree → SQL lineage | `CategoryLineageAuthority` | `StorefrontCategoriesController.cs`, `CategoryLineageAuthority.cs` |
| Brand | SQL-derived inside overlay | `product.Brand` from SQL | `ProductSearchSqlAuthorityOverlay.cs:160` |
| Price | Mongo price → overwritten by SQL | overlay sets price from variant | `ProductSearchSqlAuthorityOverlay.cs:309-325` |
| Promotion | Mongo badge → recomputed from SQL | `PromotionVisibilityProjectionSupport` | `ProductSearchSqlAuthorityOverlay.cs:327-341` |
| Coupon | **SQL** `StorefrontCartCouponStates` | revalidated per response | `StorefrontCartController.cs:1357-1469` |
| Inventory/Stock | Mongo stock → overwritten by SQL sum | `SumCurrentVariantStock` | `ProductSearchSqlAuthorityOverlay.cs:346-359` |
| Order | Mongo `orders` → SQL repair gate | `TryRepairOrderProjectionAsync` | `StorefrontOrdersReadController.cs:91-125` |
| Customer | **SQL** `OnlineCustomers` | `GetProfileAsync`; blocks if `!IsActive` | `StorefrontAuthController.cs:417-441` |
| CMS Page | **SQL** `CmsPages` | `IsPublished && !IsDraft && PublishedAt!=null` | `StorefrontPagesController.cs:94-130` |
| Navigation | **SQL** `StoreConfig.CustomizationJson` | `RecomposeNavigationFromSqlAuthorityAsync` | `StorefrontNavigationController.cs:46-203` |
| Theme/StoreConfig | **SQL** `StoreConfigs` | "tenant PostgreSQL config is authoritative" | `StorefrontStoreConfigController.cs:21-56` |
| Media | Mongo candidate → SQL reauthorization | overlay rebuilds media from SQL | `ProductSearchSqlAuthorityOverlay.cs:217-229` |
| SEO | Redis→Mongo→SQL overlay | `LoadCurrentVisibleProductBySlugAsync` | `SeoService.cs:1183-1211` |
| Search | **Elasticsearch** → SQL re-filter | `FilterSearchItemsBySqlAuthorityAsync` | `StorefrontSearchController.cs:142-500` |
| Review | ES candidate; SQL marketplace gate | projection readiness check | `CommerceHubProjectionReadinessService.cs:83-131` |

**Critical vs derivative reads:**
- **CRITICAL (SQL-fresh):** price & stock at checkout, order existence/ownership, coupon
  admission, customer profile, published/tenant-active gates.
- **DERIVATIVE (Mongo/ES/Redis browsing):** product cards, homepage sections, search ranking,
  category tree, SEO copy — always re-authorized by SQL before shown.

### Search

- `StorefrontSearchController` (`/api/storefront/search`, AllowAnonymous): unified `Search`
  queries **Elasticsearch only** via `IUnifiedSearchIndexService.SearchAsync`, then
  `FilterSearchItemsBySqlAuthorityAsync` re-filters by SQL (tenant-active, product
  visible, category live, CMS published) (`StorefrontSearchController.cs:70-500`).
- Discovery (browse) `discover` is ES → `RecomposeDiscoveryItemsFromSqlAuthorityAsync`
  (`ProductDiscoveryService.cs:86-556`). ES disabled → empty (degraded, no SQL fallback for
  listing).
- **Out-of-stock is NOT filtered at the search layer** — stock is re-derived from SQL;
  availability is shown but not excluded. Unpublished drafts excluded by
  `PublishStatus == Published` in the overlay.

### Checkout (critical price/stock revalidation)

- `CheckoutService` (explicit authority guarantee `:64-132`): "A Product is NEVER sold. A
  ProductVariant (SKU) is ALWAYS sold." Step model: Redis cart snapshot → **SQL price
  validation** → SQL shipping/payment → **SQL inventory reservation** → PostgreSQL order →
  payment intent (no gateway call).
- **SQL price revalidation** `LoadAuthoritativeCheckoutVariantLookupAsync` queries
  `dbContext.ProductVariants` (AsNoTracking), `EffectivePrice = OnlinePrice ?? Price`,
  throws on inactive/not-visible/not-published/lineage-broken/inactive-variant
  (`:3992-4096`). Optional `FOR UPDATE` row locks on commit (`:4098-4158`).
- **Inventory reservation** from `ProductVariantStocks` under SQL locks; on failure → 409; on
  order-fail → rollback + release (`:5114-5294`, `:1341-1396`).
- **Coupon/promotion admission** re-derived from SQL; price-change detection returns to client
  if SQL differs from cart price (`:4016-4034`).
- **CheckoutSession** is SQL (`CheckfrontSessionService` ↔ `CheckoutSessions`);
  `CheckoutSessionController` loads authoritative cart from Redis, validates ownership via
  claims, **ignores `X-Customer-Id` header to prevent spoofing** (`:55-63`).
- Sensitive checkout proof projection uses `CheckoutSessionProofEvent`, which is explicitly
  `ISensitiveIntegrationEvent` and `INonAuthoritativeIntegrationEvent` so it can bypass outbox
  persistence without being mistaken for checkout business truth (`CheckoutSessionEvents.cs:67`).

### Cart

- `StorefrontCartController` / `CartService`: **"Redis is the ONLY storage for cart state"**
  (`:35,50`); TTL 24h reset on write. Redis unavailable → **503** (no SQL fallback).
- Ownership: authenticated customer (`CustomerId` from claims, tenant mismatch → 401) vs guest
  (`X-Session-Id`). Coupon *state* is SQL (`StorefrontCartCouponStates`), revalidated every
  cart response; guest carts cannot hold coupons.
- AddItem validation reads SQL `ProductVariants` (IgnoreQueryFilters) for variant liveness /
  affiliate price presence (`:2164-2178`).

### Customer Session / Auth

- Completely separate from POS auth. `OnlineCustomer` entity, separate JWT secret/issuer/
  audience; Storefront JWTs cannot call POS APIs (`StorefrontAuthController.cs:15-36`, scheme
  `StorefrontJwt`). Refresh token in HttpOnly cookie; JWT blacklist for revoked tokens.
  `me` blocks deactivated customers (`IsActive` check, `:417-441`).

### Pricing Display / Inventory Display

- Pricing shown in DTOs is built from the Mongo `ProductDocument` **after the SQL overlay
  rewrites price** (variant-centric `EffectivePrice`, else product-level
  `StorefrontEffectivePrice`). Promotion badges sourced from SQL via
  `PromotionVisibilityProjectionSupport` (`:327-341`).
- Stock shown in browsing is SQL-derived `Σ(Quantity − ReservedQuantity)` across active
  warehouses (`:346-359`), overwriting `document.StockQuantity`. At checkout, stock is
  re-validated and **reserved** in `ProductVariantStocks` — the hard gate; browsing display is
  advisory.

### Fallback / Repair-on-read

1. **Per-read SQL overlay** on every product/category/search/SEO candidate (suppress if SQL
   says not-active/visible/deleted).
2. **Order projection repair-on-read:** `StorefrontOrdersReadController` loads SQL
   `LoadOrderAuthoritySnapshotAsync` to prove existence/ownership, then
   `TryRepairOrderProjectionAsync` before trusting Mongo; repair-fail → 503.
3. **ES → Mongo fallback** (best-sellers) still re-authorized by SQL.
4. **Redis → 503** (no SQL fallback for cart).
5. **Projection-readiness gate (fail-closed):** `CommerceHubProjectionReadinessService.
   EnsurePublicProductReadModelsReadyAsync` checks the outbox tables for blocked/stale
   deliveries (>15 min or ≥3 failures) and throws `ProjectionReadModelUnavailableException` →
   503, refusing stale public reads (`CommerceHubProjectionReadinessService.cs:17-131`, called
   at `StorefrontProductsController.cs:110,199,266,402,645,837`).

### Availability Gates

- **Tenant active:** `TenantLineageAuthority.IsActiveTenantAsync` (SQL `Tenants`
  `IsActive && Status==Active && DeletedAtUtc==null`, `:27-46`).
- **Product published/visible:** `IsActive && IsOnlineVisible && PublishStatus==Published`
  (overlay `:170-172`).
- **Category live in lineage:** `CategoryLineageAuthority.IsLiveInLineage` walks parent chain,
  rejects deleted/orphaned categories.
- **CMS page published:** `!IsDeleted && IsPublished && !IsDraft && PublishedAt != null`.
- **Storefront live status:** navigation suppressed unless `StoreConfig.StorefrontStatus=="live"`.
- **Variant active & priced > 0:** checkout rejects otherwise.
- **Customer active:** auth `me` blocks deactivated customers.
- **Stock > 0:** advisory at browse; hard at checkout via reservation.

### Failure Handling / Caching

- **Redis cache** (`IRedisJsonCacheService`): product detail/list/home/SEO cached with TTLs;
  populated **only with SQL-accepted DTOs**; cache failures are non-fatal
  (`StorefrontProductService.cs:21-31,73-112`).
- **Cache invalidation** `ProductCacheInvalidationConsumer`: listens to product
  create/update/delete/stock/price/publish events, invalidates tenant caches, resolves
  **current SQL slug** before invalidating, dedup via `ProjectionConsumerEventFence`
  (`ProductCacheInvalidationConsumer.cs:37-293`).
- **Projection freshness gate:** outbox-blocked → 503 (unavailability over stale data).

### SEO / SEO Runtime

- `SeoController` / `SitemapController` / `SeoService`: read flow Redis → Mongo → SQL overlay.
  Product SEO `Index = IsActive && IsOnlineVisible` (noindex for unpublished, `SeoService.cs:229`).
  Category SEO `Index=true`; CMS SEO publication-filtered. Sitemap/robots cached (6h/12h).
  `MetaTitle`/`MetaDescription` copied from SQL into the Mongo doc by the overlay.

---

## Catalog Runtime

See POS Admin → Catalog. SQL aggregates `Product`/`ProductVariant`/`Category`/`Unit`.
Write authority `ProductsController` + `ProductVariantService`. Every mutation emits an
`OutboxMessage` staged in the same transaction. Projection writers recompose from SQL and
tombstone when SQL no longer proves visibility. Brand/Collection are not entities.

## Inventory Runtime

See POS Admin → Inventory. `ProductVariantStock` is the singular mutable stock truth;
`StockMovement` is append-only; `InventoryReservation` is order-scoped. `InventoryService`
serializes writers via row locks. `CheckoutService` (and CommerceHub checkout) reserve and
release. `StockSyncOutboxMessage` carries warehouse-scoped stock sync; the
`StockSyncOutboxProcessor` suppresses stale replays when SQL inventory lineage is lost
(`StockSyncOutboxProcessor.cs:421-477`).

## Pricing Runtime

Product/Variant prices are SQL columns; `OnlinePrice` is storefront truth (falls back to
`SalePrice`). No price-list aggregate; discounts via Coupons/Promotions/SmartOffers.
`StorefrontPricingService`/`TaxCalculationService`/`ShippingCalculationService` compute only.
Final checkout price = SQL variant `OnlinePrice ?? Price` + SQL tax + SQL shipping + SQL
coupon/promotion re-validation. Tax is config-driven and frozen per-order.

## Promotion Runtime

`Promotion` aggregate with monotonic `ProjectionMutationVersion`; `PromotionEngine` applies
winning promotion at read time from SQL (`ProductSearchSqlAuthorityOverlay.cs:327-341`).
`PromotionProjectionConsumer` updates Mongo `products`/`categories` with promo badges and
rejects events not SQL-backed.

## Coupon Runtime

`Coupon` aggregate; SQL-backed validation/usage (`ICouponValidationService`). Storefront
coupon admission is SQL (`StorefrontCartCouponStates`), revalidated each response; guest carts
cannot hold coupons. `CouponProjectionConsumer` → Mongo `CouponDocument` with stale-guard +
tombstone-on-delete.

## Checkout Runtime

`CheckoutService` (8807 lines) is the isolated, fail-closed checkout authority. It re-validates
price/stock from SQL `ProductVariants`/`ProductVariantStocks` (never projections), reserves
stock in a single PostgreSQL order-creation transaction, routes payment through
`IPaymentLifecycleService`, and compensates (release reservation) on failure
(`CheckoutService.cs:64-132, 4047-5294, 1295-1396`). Cart state is Redis-only; checkout
identity from claims (header spoofing rejected).

## Order Runtime

`Order` (e-commerce) aggregate owned by `CheckoutService` (create) + `AdminOrderService`
(operate). Immutable after `Delivered`/`Completed`; reversal via `OrderRefund`.
`PaymentStatus`/`FulfillmentStatus` mutation allowed only via their lifecycle services
(interceptor-encapsulated). Projects to Mongo `orders`; repaired on read via
`TryRepairOrderProjectionAsync`.

## Customer Runtime

POS Admin `Customer`/`Supplier` (party ledger, SQL `AccountLedger`) vs Storefront
`OnlineCustomer` (separate identity/JWT). Storefront customer profile is SQL-read and blocks
deactivated users.

## Payment Runtime

Merchant Platform **configures** payment methods (`TenantPaymentMethod`) but does NOT own
payment lifecycle. `Order.PaymentStatus` mutateable only by `IPaymentLifecycleService`
(`Order.cs:108-149`). Outbound money is gated fail-closed by the lifecycle + accounting outbox
(money events retry forever, never terminal-fail).

## Shipping Runtime

`ShippingMethod`/`CarrierConfiguration` are tenant SQL config projected to Mongo. The
**fulfillment lifecycle** (carrier execution, tracking, delivery→payment settlement) is a
cross-domain concern owned by Shipping; COD delivery completion is lifecycle-owned.

## Tax Runtime

Config-driven (`SystemSettings` + `CountryTaxRates` + `Tenants.Country`), resolved by
`TaxCalculationService`, frozen per-order into `Order.TaxAmount`. No aggregate, no projection.

## CMS Runtime

`CmsPage` (SQL, tenant-owned, globally tenant-filtered, versioned, publication-flagged) +
navigation/footer JSON in `StoreConfig`. Projected to Mongo
`storefront_pages`/`storefront_navigation`/`storefront_footer`, all **fail-closed** if SQL
authority unproven. Storefront reads CMS pages from SQL with publication filter.

## Media Runtime

`ProductMedia` metadata (SQL) + URL/object references for externally stored media. Product media
metadata mutation is owned by `ProductMediaService`; R2/S3 services are storage infrastructure, not
a separate media truth store. `ProductMediaChangedEvent` → search-index consumer.

## Theme Runtime

`ThemePreset` (system, immutable) + tenant overrides in `StoreConfig` (draft/live versioned).
Embedded into `storefront_configs` by `StorefrontConfigProjectionConsumer`. No separate
collection.

## SEO Runtime

Redis→Mongo→SQL overlay. `Index = IsActive && IsOnlineVisible` (noindex for unpublished).
Sitemap/robots cached. SEO copy sourced from SQL via overlay.

## Search Runtime

Elasticsearch is the search index (single authority for *search*), hydrated from Mongo after
SQL overlay. `ProductSearchIndexConsumer` enforces Mongo-before-ES ordering via
`ProjectionNotReadyException` retry. Storefront search re-filters candidates by SQL authority.
Fail-closed: ES failure → `StorefrontSearchUnavailableException` → 503 (no silent empty
results). `ElasticsearchDriftReconciliationJob` reconciles SQL↔ES drift.

## Notification Runtime

Merchant Platform stores notification *config* (`WhatsAppAccount`, `WhatsAppMessageTemplate`,
`TenantAutomationSettings`, `SystemSetting`s). The sending runtime (WhatsApp Baileys, email)
is separate; notification events are explicitly non-authoritative
(`IntegrationEventAuthority.NonAuthoritativeEventTypeNames`, `IntegrationEventAuthority.cs:39-52`).

## Analytics Runtime

`AnalyticsEventConsumer` writes Redis hot + Mongo cold for client-originated analytics telemetry
(idempotent, failures non-blocking). `AuthoritativePurchaseAnalyticsConsumer` emits server-truth
purchase events from SQL `Orders`; only purchase-completion analytics are SQL-derived business
truth. Read models are consumed by Admin/Storefront analytics controllers.

## Store Settings Runtime

`StoreConfig` (one per tenant; draft/live; `CustomizationJson`; `StorefrontStatus`; theme
versioning). `AdminStoreConfigService` publish/rollback with lock + email gate + version
snapshots. Current admin read behavior lazily initializes a default `StoreConfig` when missing;
subsequent lifecycle changes remain service-owned SQL mutations. `StorefrontConfigProjectionConsumer`
→ `storefront_configs` + Redis invalidation, fails closed on unproven lineage.

---

## CQRS Model

- **Write authority = SQL only.** No controller writes Mongo/ES/Redis as business truth. Lifecycle
  services (`IPaymentLifecycleService`, `IOrderFulfillmentService`) further encapsulate specific
  mutations. Explicit repair/admin sync paths may write derivative stores only after re-reading
  SQL authority.
- **Read authority = Mongo/ES/Redis** (derivative). Every read path re-overlays SQL truth at
  query time (`ProductSearchSqlAuthorityOverlay`, `StorefrontOrdersReadController` repair).
- **Repair reads vs critical reads:** repair-on-read reimposes SQL on derivative candidates;
  critical reads (checkout price/stock, order ownership, coupon admission, customer profile)
  are recomputed from SQL directly.
- **EventVersion monotonic ordering** prevents stale events overtaking newer state across
  POS/Storefront/Marketplace/Affiliate/Accounting (`OutboxMessage.cs:56`,
  `OutboxPublisherJob.cs:601-659`, `AccountingOutboxProcessor.cs:78-107`,
  `MarketplaceEventOutboxProcessor.cs:404-436`, `StockSyncOutboxProcessor.cs:479-501`).
- **Projection rebuild / replay:** repair-on-read + `ProductSqlAuthorityProjectionWriter` +
  `ElasticsearchDriftReconciliationJob` + hosted reconciliation services
  (`StorefrontOrderProjectionReconciliationService`, `CommerceHubProjectionReconciliationService`).

## Projection Model

Merchant Platform projections are SQL-recomposed at write time (consumers and approved repair
paths re-fetch SQL, not trust event payload) and SQL-reauthorized at read time (overlay).
Collections:

- Mongo `products` (embedded `Variants[]`, `Media[]`, stock, promo badges)
- Mongo `categories`, `CouponDocument`, `PromotionDocument`, `orders`, `storefront_configs`,
  `storefront_navigation`, `storefront_pages`, `storefront_footer`, `analytics`
- Elasticsearch `products` search index (+ `marketplace_services`, CommerceHub index)
- Redis caches (store config, promo-ranking, product list/detail, navigation, cart)

## Mongo Integration

Mongo is the **read-model store** for storefront product catalog and content. Writes are
idempotent upserts by `(TenantId, key)` performed by MassTransit consumers plus explicit
SQL-authority repair/sync services. Consumers acquire a `ProjectionEventFence`, re-fetch SQL
authority, and run `DerivativeProjectionMutationGuard.RequireReviewedMutation(FailsClosedWhenAuthorityMissing:true)`;
repair paths use `SqlTruthRebuildGuard` / SQL-authority rebuild semantics.
Mongo is "READ-ONLY source for Storefront"; PostgreSQL is "write-only for variants"
(`ProductVariantProjectionConsumer.cs:31-32`).

## Redis Integration

Hot caches for store config, promotion ranking, product list/detail, navigation, cart.
Populated only with SQL-accepted DTOs; invalidated explicitly on mutation
(`ProductCacheInvalidationConsumer`, `ProductVariantProjectionConsumer`). Cart state is
**Redis-only** (503 if down).

## Elasticsearch Integration

Search index hydrated from Mongo after SQL overlay. `ProductSearchIndexConsumer` enforces
Mongo→ES ordering. Stale-event rejection at the ES write side
(`ProductSearchIndexService.IndexAsync` refuses to overwrite a newer doc, `:54-67`). Drift
reconciliation job enforces zero drift.

## Outbox

Eight outbox entities, each with a worker/processor and fail-closed behavior:

| Outbox | Purpose | Processor | Fail-closed |
|---|---|---|---|
| `OutboxMessage` | Generic integration events | `OutboxPublisherJob` (Hangfire 5s) → RabbitMQ | Critical poison → operator reconciliation + slow replay; non-critical terminal-fail |
| `AccountingOutboxMessage` | Financial postings | `AccountingOutboxWorker` → `AccountingOutboxProcessor` | Money events retry forever (non-terminal) |
| `MarketplaceEventOutboxMessage` | Marketplace backbone events | `MarketplaceEventOutboxWorker` → `MarketplaceEventOutboxProcessor` | Kept alive; dead-letters to archive |
| `MarketplaceProjectionOutboxMessage` | Durable post-commit read-model handoff | `MarketplaceProjectionOutboxWorker` → `MarketplaceProjectionOutboxProcessor` | Terminal after retries |
| `StockSyncOutboxMessage` | Warehouse-scoped stock sync | `StockSyncOutboxWorker` → `StockSyncOutboxProcessor` | Kept alive; suppression when lineage lost |
| `PaymentCancellationOutboxMessage` | Payment-cancellation ops | `PaymentCancellationOutboxWorker` → `PaymentCancellationOutboxProcessor` | Terminal; lease collapses overlap |
| `CommerceHubPaymentCompensationOutboxMessage` | Provider compensation commands | `CommerceHubPaymentCompensationOutboxWorker` | Routes to operator reconciliation |
| `MarketplaceEventOutboxDeadLetterArchive` | Audit archive of failed marketplace rows | n/a | — |

`OutboxPublisherJob` uses pessimistic locking (`FOR UPDATE` on Npgsql, `UPDLOCK` on SqlServer),
critical/non-critical split (`IntegrationEventAuthority`), tenant-scope enforcement
(`EnsureOutboxAuthorityScope`), and `EventVersion` ordering. Critical poison rows are kept
operator-visible and slow-replayed (15-min cadence), never dropped.

## Event Flow

```
POS Admin / Storefront command
   │  (ApplicationDbContext transaction)
   ├─ SQL mutation committed
   └─ OutboxMessage row staged (same tx)        ← atomic
          │
   OutboxPublisherJob (Hangfire, 5s, FOR UPDATE)
          │  version-ordered, tenant-scoped, critical/non-critical split
          ▼
   RabbitMQ  ──►  MassTransit Consumers (idempotent, ProjectionEventFence)
                      ├─ Mongo read-model upsert (re-fetch SQL, fail-closed)
                      ├─ Elasticsearch index (Mongo-gated, SQL overlay)
                      ├─ Redis cache invalidation
                      └─ Cross-domain events (AccountingOutbox, MarketplaceOutbox, …)
```

## Read Models

Mongo (product/catalog/content/orders/analytics), Elasticsearch (search), Redis (config/cart
hot caches). All business read models are derivative; product/order/config/checkout-critical reads
are SQL-reauthorized or SQL-fresh at read time. General analytics telemetry remains a derivative
event store, with purchase completion re-derived from SQL order authority.

## Write Models

PostgreSQL (`ApplicationDbContext`) — the only write model. Mutations go through lifecycle
services; outbox events are staged atomically.

---

## Runtime Authority Matrix

For every major concept: **Owner** (who mutates SQL), **Mutation Authority** (service),
**Read Authority** (store used by Storefront), **Projection Authority** (consumer), **Runtime
Truth** (SQL).

| Concept | Owner | Mutation Authority | Read Authority (Storefront) | Projection Authority | Runtime Truth |
|---|---|---|---|---|---|
| Product | `ProductsController` | SQL `Products` | Mongo+SQL overlay | `Product*Consumer` | SQL |
| Variant | `ProductVariantService` | SQL `ProductVariants` | Mongo+SQL overlay | `ProductVariantProjectionConsumer` | SQL |
| Category | `CategoriesController` | SQL `Categories` | Mongo+SQL lineage | (denormalized) | SQL |
| Brand | `ProductsController` | SQL `Products.Brand` | SQL overlay | (denorm) | SQL |
| Inventory/Stock | `InventoryService`/`CheckoutService` | SQL `ProductVariantStocks` | SQL sum (overlay) | `ProductStock*Consumer` | SQL |
| Price | Product/Variant write | SQL columns | SQL overlay | `Product*Consumer` | SQL |
| Promotion | `PromotionsController` | SQL `Promotions` | SQL overlay | `PromotionProjectionConsumer` | SQL |
| Coupon | `CouponsController` | SQL `Coupons` | SQL `StorefrontCartCouponStates` | `CouponProjectionConsumer` | SQL |
| Order | `CheckoutService`/`AdminOrderService` | SQL `Orders` | Mongo+SQL repair | `OrderProjectionConsumer` | SQL |
| Customer (POS) | `CustomersController` | SQL `Customers` | SQL ledger | — | SQL |
| OnlineCustomer | Storefront auth | SQL `OnlineCustomers` | SQL | — | SQL |
| Cart | `CartService` | Redis | Redis ONLY | — | Redis |
| Checkout | `CheckoutService` | SQL `Orders`+reservations | SQL (critical) | — | SQL |
| Tax | `TaxCalculationService` | `SystemSettings` | SQL compute | — | SQL/config |
| CMS Page | `AdminCmsService` | SQL `CmsPages` | SQL | `CmsPageProjectionConsumer` | SQL |
| Navigation | `AdminStoreConfigService` | SQL `StoreConfig` | SQL | `NavigationProjectionConsumer` | SQL |
| Theme | `AdminStoreConfigService` | SQL `StoreConfig`/`ThemePresets` | SQL | `StorefrontConfigProjectionConsumer` | SQL |
| Media | `ProductMediaService` | SQL `ProductMedia` metadata + object/URL reference | SQL overlay | `ProductMediaChangedEventConsumer` | SQL metadata + external object store |
| SEO | `SeoService` | (derived) | Redis→Mongo→SQL | — | SQL |
| Review | (marketplace) | CommerceHub | ES+SQL gate | — | SQL (CommerceHub) |
| Store Settings | `AdminStoreConfigService` | SQL `StoreConfigs` | SQL | `StorefrontConfigProjectionConsumer` | SQL |
| Notification | admin config | SQL channel/template rows | (send-time) | — | SQL (config) |
| Analytics | `AnalyticsEventConsumer` / purchase authority consumer | Mongo+Redis telemetry; SQL-derived purchase events | Mongo+Redis | `AnalyticsEventConsumer` | Mixed: telemetry read model + SQL-derived purchases |
| Search | (indexer) | Mongo←SQL | Elasticsearch | `ProductSearchIndexConsumer` | SQL |

## Runtime Authority Divergence

- **Storefront vs POS Admin:** both read the same SQL truth; Storefront additionally
  SQL-reauthorizes projections, POS Admin authors them. No divergence in truth — only in
  read path strictness (Storefront repair-on-read; POS Admin reads SQL directly).
- **Projection lag:** Mongo/ES can temporarily lag SQL (outbox latency). Repair-on-read and the
  readiness gate close the gap; critical reads bypass projections entirely.

## Cross-Domain Authority Graph

| Peer | Merchant Platform reads | Merchant Platform writes/emits | Owns | Defers |
|---|---|---|---|---|
| CommerceHub | tenant catalog/inventory as consumer | `MarketplaceEventOutboxMessage`, `CommerceHubLedgerPosting` | its marketplace orders/listings/payouts | catalog/inventory truth (reads SQL) |
| Payments (`IPaymentLifecycleService`) | — | payment-cancellation outbox | method *config* | payment lifecycle/capture/refund |
| Treasury/Accounting | — | `AccountingOutboxMessage` (PosSale/Purchase/Refund/OnlinePayment/…) | business events | ledger postings |
| Shipping/Fulfillment | shipping method config | shipment events | method/carrier *config* | fulfillment lifecycle, carrier execution |
| Identity | — | — | storefront `OnlineCustomer` | JWT issuance/validation (defense-in-depth `IsActive` only) |
| Affiliate | `IsAffiliateEnabled` in search | affiliate events, policy/intelligence projections | links/attribution/settlement orchestration | affiliate platform truth |
| Notifications | — | notification events (non-authoritative) | channel/template *config* | sending transport |
| SuperAdmin | — | — | platform recovery/seed | global ES index control (`403` for tenants) |

Cross-domain integration is event-driven via the outbox; money events are fail-closed
(retry forever); reconciliation hosteded services repair projection drift
(`CommerceHubProjectionReconciliationService`, `StorefrontOrderProjectionReconciliationService`,
`AffiliateAttributionReconciliationHostedService`, `CanonicalIdentityReconciliationHostedService`).

---

## Business Invariants

1. **Single SQL authority.** No derivative store is ever a write target
   (`ProductVariantProjectionConsumer.cs:31-32`).
2. **Transactional outbox atomicity.** Outbox row atomic with SQL mutation
   (`MassTransitEventPublisher.cs:124,324-339`).
3. **Visibility is a SQL conjunction** re-verified at projection time — never a UI toggle
   (`ProductSqlAuthorityProjectionWriter.cs:94`, `ProductVisibilityLifecycleProjectionConsumer.cs:130-136`).
4. **Repair-on-read.** Update/lifecycle consumers rebuild from current SQL, not event body
   (`ProductUpdatedEventConsumer.cs:75-83`).
5. **Monotonic EventVersion** prevents stale-event overtaking
   (`OutboxPublisherJob.cs:601-659`).
6. **Fail-closed projections.** Unproven SQL authority → tombstone/delete
   (`CmsPageProjectionConsumer.cs:304-347`).
7. **Checkout revalidates price/stock from SQL** and reserves in one transaction
   (`CheckoutService.cs:4047-5294`).
8. **Order immutability.** No in-place financial edit; reversal via `OrderRefund`
   (`OrderRefund.cs:6`); `PaymentStatus`/`FulfillmentStatus` interceptor-encapsulated
   (`Order.cs:108-219`).
9. **Storefront never owns products.** It reads projections + overlays SQL
   (`StorefrontProductsController.cs:481`, `ProductSearchSqlAuthorityOverlay.cs:85-344`).
10. **Cart is Redis-only**; Redis down → 503 (`:144-151`).
11. **Coupon admission is SQL**, revalidated per response (`StorefrontCartController.cs:1357-1469`).
12. **Projection breakage → 503**, not stale data (`CommerceHubProjectionReadinessService.cs:17-131`).

---

## Architectural Decisions (WHY)

- **Why PostgreSQL remains authority:** one source of truth avoids split-brain; outbox
  guarantees durability; `SqlTruthRebuildGuard` forces explicit authority on every rebuild
  (`SqlTruthRebuildGuard.cs:27`).
- **Why MongoDB exists:** rich nested product documents (embedded variants) with fast reads;
  CQRS read/write separation ("MongoDB READ-ONLY for Storefront; PostgreSQL write-only for
  variants", `ProductVariantProjectionConsumer.cs:31-32`).
- **Why Elasticsearch exists:** search intelligence in analyzers (multilingual Arabic+Latin),
  not app code ("NO translation dictionaries. NO data duplication", `SearchReindexController.cs:24-26`);
  explicitly not source of truth (`ProductSearchIndexConsumer.cs:30-33`).
- **Why Redis exists:** hot caches for stable config + cart state; avoid re-reading
  Mongo/SQL per request; invalidated explicitly on mutation.
- **Why outbox + two-stage projection queue:** domain transaction must not block on
  Mongo/ES latency; durable post-commit handoff survives crashes; `ProjectionEventFence`
  dedupes at-least-once redelivery (`MarketplaceIdempotentConsumer.cs:48-141`).
- **Why orders/events immutable / fail-closed:** losing a financial/marketplace event is worse
  than a backlog; critical poison kept operator-visible + slow-replayed; money events retry
  forever (`OutboxPublisherJob.cs:33,146-236`, `AccountingOutboxMessage.cs:252-261`).
- **Why storefront never owns products:** tenant isolation + single writer prevents concurrent
  admin/storefront catalog edits clobbering each other; defensive tenant check in hydration
  (`ProductSearchService.cs:183`).
- **Why EventVersion, not time:** wall-clock time unreliable across distributed writers;
  monotonic version guarantees causal ordering even with out-of-order delivery
  (`MarketplaceEventOutboxProcessor.cs:409-412`, `StockSyncOutboxProcessor.cs:484-488`).

---

## Technical Debt (implementation-backed)

1. **Duplicate authority / dual-ownership.**
   - "AffiliateAccountId dual-write bridge" copies `AffiliateAccountId` across two tables
     (`AffiliatePromotionLinksController.cs:104`).
   - Two parallel event fences: `ProjectionEventFence` (generic) and `ProjectionConsumerEventFence`
     (CommerceHub/Affiliate) — drift risk.
   - Two SQL-authority overlays (`ProductSearchSqlAuthorityOverlay`, `CommerceHubSearchSqlAuthorityOverlay`)
     with overlapping re-derivation that can diverge.

2. **Projection drift / hidden coupling.**
   - `ProductVariantProjectionConsumer` is 1744 lines — one consumer owning create/update/delete/
     stock + cache-invalidation + ES index (mixed responsibilities).
   - Mongo `ProductDocument` carries `ProjectionMutationVersion`/`LastStockEventVersion`; the
     stock sync processor constantly re-checks `ProjectionMutationVersion > message.EventVersion`
     because Mongo and SQL versions drift (`StockSyncOutboxProcessor.cs:460-465`).
   - `ElasticsearchDriftReconciliationJob` exists *because* SQL↔ES drift is a recurring condition.

3. **Legacy / TODO / deferred (real markers).**
   - **JWT deactivation gap (live security gap):** "TODO (Phase 10.3.2): Deactivation must also
     revoke/invalidate JWTs" (`OnlineCustomersAdminController.cs:31,371,430`,
     `StorefrontOnlineCustomerController.cs:22-23,116`). Deactivating an online customer does NOT
     currently revoke active JWTs — only a defense-in-depth `IsActive` check exists.
   - `legacyEventType` dual matching in `MarketplaceIdempotentConsumer` (legacy event-naming
     compatibility carried forward).

4. **Manual synchronization / ad-hoc SQL risk.**
    - Runbook warns against direct SQL to mutate payment/order truth
      (`docs/operations/operational-recovery-runbooks.md:212`).
    - Multiple root-level recovery scripts (`af16e-reindex-products.ps1`, `af17a-reindex-trigger.sql`,
      `fix-elasticsearch-*.ps1`) indicate manual ES/Mongo reindex is a routine operational crutch.
    - Explicit SQL-authority projection repair/sync paths are valid derivative-store writers, but
      they must remain constrained to SQL re-read semantics (`DiscountReadModelRepairService.cs:54-146`).

5. **Runtime ambiguity / partial migration.**
   - **Two relational providers** (Npgsql PostgreSQL AND SqlServer) maintained throughout
     (`FOR UPDATE` vs `UPDLOCK` paths, `OutboxPublisherJob.cs:489-509`). Doubles query-correctness risk.
   - `ProjectionEventFence` opens a raw connection for `pg_try_advisory_lock`; no-ops the lock on
     non-relational providers — behavior diverges by provider.
   - `ManualWallet` proof lifecycle split between application-owned runtime and lifecycle mutation
     (`IPaymentLifecycleService.cs:51-58`) — partially migrated payment path.

6. **Dead / questionable code.**
    - `MarketplaceProjectionOutboxProcessor.ProcessMessageAsync` orders the *projection* queue by
      `OccurredAtUtc` only (deliberate, to avoid backlog amplification) while the *event* queue
      orders by version — two different ordering contracts, easy to misread.

7. **Side-effecting reads / operational reconciliation.**
   - `AdminStoreConfigService.GetStoreConfigAsync` lazily creates a default `StoreConfig` when one
     is missing. This is compatibility initialization, but it is a GET-side SQL mutation.
   - `WhatsAppAdminController.CheckStatus` polls the transport and persists changed account status.
     This is operational reconciliation, but it is also a GET-side mutation.

---

## Implementation Follow-up Items

| # | Title | Current Implementation | Why | Future Improvement | Priority | Status |
|---|---|---|---|---|---|---|
| 1 | JWT revocation on customer deactivation | `IsActive` defense-in-depth check only; TODOs at 5+ sites | Deactivated customers keep valid JWTs until expiry | Add token-version/`jti` revocation list checked at auth middleware | **High** | Open (live gap) |
| 2 | Collapse dual event fences | `ProjectionEventFence` + `ProjectionConsumerEventFence` | Two fence mechanisms for same purpose → drift | Unify on `ProjectionEventFence` | Medium | Open |
| 3 | Unify SQL-authority overlays | `ProductSearchSqlAuthorityOverlay` + `CommerceHubSearchSqlAuthorityOverlay` | Overlapping re-derivation can diverge | Extract shared overlay core, parameterize by boundary | Medium | Open |
| 4 | Split `ProductVariantProjectionConsumer` | 1744-line consumer | Mixed responsibilities, hard to maintain | Decompose into focused consumers (stock / variant / cache) | Medium | Open |
| 5 | Resolve dual provider (SqlServer) | Npgsql + SqlServer dialect paths everywhere | Doubles query-correctness risk; fence no-ops on SqlServer | Pick Npgsql as sole provider; remove SqlServer branches | Medium | Open |
| 6 | Eliminate manual reindex crutches | Root-level `*.ps1`/`*.sql` reindex scripts | Manual ES/Mongo repair is routine ops | Strengthen drift reconciliation + readiness gate; document only | Low | Open |
| 7 | Clarify `ManualWallet` migration | Proof lifecycle split app-owned vs lifecycle | Partially migrated payment path | Complete migration into `IPaymentLifecycleService` | Low | Open |
| 8 | Dedupe affiliate dual-write bridge | `AffiliateAccountId` copied across tables | Redundant ownership | Single source of `AffiliateAccountId` | Low | Open |
| 9 | Convert side-effecting GETs | Store config default initialization and WhatsApp status reconciliation can mutate SQL from GET paths | Hidden mutation paths complicate authority review | Move to explicit POST/init/sync endpoints or document compatibility contract per endpoint | Medium | Open |
| 10 | Normalize source-reading architecture tests | Several tests hard-code repo-relative paths from output directories | Isolated build/test output fails despite valid code | Centralize on `CertificationSource` / `POINT_OF_SALE_SOLUTION_ROOT` | Medium | Open |
| 11 | Refresh legacy test fixtures | Inventory and checkout-session fixtures omit current required tenant/ownership state | Tests fail against strengthened co-ownership/currency guards | Seed tenant currency, warehouses, variants, and ownership edges explicitly | Medium | Open |
| 12 | Update EventBackbone executable | `PointOfSale.EventBackbone.Tests` targets stale processor constructor | Architecture executable no longer compiles | Rewire test host to current `MarketplaceEventOutboxProcessor` dependencies | Medium | Open |

---

## Certification

- **Implementation Confidence:** High with observations. Every normative claim traces to `file:line` in
  `PointOfSale.Core/`, `PointOfSale.Infrastructure/`, or `PointOfSale.API/`. Core primitives
  (outbox, overlay, fence, interceptors, checkout) were directly read and verified, not just
  agent-reported.
- **Architecture Completeness:** High. All 30+ required runtime sections documented; CQRS,
  projection model, outbox, cross-domain graph, invariants, decisions, and debt covered.
- **Runtime Authority Verification:** Verified with observations — SQL is the sole business write
  authority; derivative stores are projection/repair-only and business-critical reads are
  SQL-reauthorized or SQL-fresh.
- **Storefront Verification:** Verified — projection-candidate + SQL-reauthorization pattern
  confirmed across product/home/search/SEO/checkout/cart/customer/order reads.
- **Cross-Domain Verification:** Verified — CommerceHub/Payments/Treasury/Shipping/Identity/
  Affiliate/Notifications/SuperAdmin boundaries confirmed via events, interceptors, and
  reconciliation services.
- **Verified Capabilities:** transactional outbox + version ordering; repair-on-read overlay;
  fail-closed projections and readiness gate; interceptor-encapsulated payment/fulfillment
  status; isolated fail-closed checkout; tenant isolation throughout.
- **Missing Capabilities:** (none blocking) — noted debt items are enhancements/hardening/test-harness
  updates, not missing runtime capabilities. Tax/Localization/Notification-Config have no dedicated
  aggregate (by design, config-driven / column-driven) — documented as such.
- **Technical Debt Summary:** 12 debt items; 1 is a live security gap (JWT revocation), several are
  medium-risk authority-review/test-harness concerns, and the remainder are maintainability/coupling/
  partial-migration items.
- **Architectural Observations:** The Merchant Platform is a coherent, fail-closed CQRS system
  where PostgreSQL authority + transactional outbox + repair-on-read overlay + version-ordered
  delivery produce strong consistency without distributed writes. The dominant pattern
  ("projection-candidate + SQL-reauthorization") is applied uniformly across Storefront reads
  and projection consumers, and is the key to its correctness under multi-tenant,
  multi-surface operation.

---

*Generated by implementation inspection. Every claim cites `path:line`. UI/React components and
prior markdown/docs were intentionally excluded as non-authoritative per scope. Intermediate
evidence reports: `docs/MERCHANDISING_WRITE_RUNTIMES_AND_PROJECTION_PIPELINE.md`,
`docs/architecture/MERCHANT_STOREFRONT_RUNTIME_REVERSE_ENGINEERING.md`,
`docs/REVERSE_ENGINEERED_POS_ADMIN_OPERATIONS.md`,
`REVERSE_ENGINEERED_AUTHORITY_CQRS_SEARCH_DEBT.md`.*
