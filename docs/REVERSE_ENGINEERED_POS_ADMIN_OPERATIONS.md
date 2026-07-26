# POS Admin — Operations + Content Write Runtimes (Reverse-Engineered)

> Source of truth: implementation only. All claims cite `path:line`. Reads performed against the repo at `C:/Users/fadya/source/repos/PointOfSale` (sln root: `PointOfSale.Core`, `PointOfSale.Infrastructure`, `PointOfSale.API`).
>
> Architecture in one line: **PostgreSQL = authoritative write model** for every domain below. Mongo/Redis/Elasticsearch = derivative read projections, hydrated by MassTransit consumers reading SQL (never trusting event payloads — they re-fetch SQL authority). Cross-tenant isolation via `TenantId` (`ITenantOwned` / `TenantEntity`).

---

## 0. Shared runtime primitives (apply to every domain)

- Multi-tenancy: `Order.cs:18`, `Customer.cs:14`, `Supplier.cs:15`, `ShippingMethod.cs:14`, `CarrierConfiguration.cs:27`, `TenantPaymentMethod.cs:28`, `StoreConfig.cs:24`, `CmsPage.cs:9`, `ProductMedia.cs:8`, `PartyLedgerAdjustment.cs:15`.
- Event authority model: `PointOfSale.Core/Events/IntegrationEventAuthority.cs` + markers `IAuthoritativeIntegrationEvent` (e.g. `OrderEvents.cs:13,69,98,116,128,138`), `IAggregateScopedIntegrationEvent` (`CmsPageEvents.cs:93`, `CacheInvalidationEvents.cs:5`), `INonAuthoritativeIntegrationEvent` (`CacheInvalidationEvents.cs:42`), `IAuthoritativeIntegrationEvent` used by `AuthoritativePurchaseAnalyticsConsumer` (`AuthoritativePurchaseAnalyticsConsumer.cs:18,67`).
- Projection freshness/authority guard: every consumer acquires a `ProjectionEventFence` (`CmsPageProjectionConsumer.cs:59`, `StorefrontConfigProjectionConsumer.cs:149`, `NavigationProjectionConsumer.cs:84`, `StorefrontFooterProjectionConsumer.cs:49`) and runs `DerivativeProjectionMutationGuard.RequireReviewedMutation(...)` before any Mongo/Redis write, with `FailsClosedWhenAuthorityMissing: true` (e.g. `StorefrontConfigProjectionConsumer.cs:454-480`, `CmsPageProjectionConsumer.cs:472-499`). If SQL authority cannot be proven, the projection **fails closed** (deletes the Mongo doc + Redis key): `StorefrontConfigProjectionConsumer.cs:418-441`, `NavigationProjectionConsumer.cs:225-248`, `StorefrontFooterProjectionConsumer.cs:257-285`, `CmsPageProjectionConsumer.cs:304-347`.
- Idempotency/outbox: mutations commit SQL + stage event via `IEventPublisher` inside one transaction (e.g. `AdminOrderService.cs:441-457`); MassTransit outbox guarantees exactly-once relative to projection fence.

---

## 1. ORDERS / SALES (e-commerce Order aggregate)

### Owner service
- **`AdminOrderService`** (`PointOfSale.API/Services/Admin/AdminOrderService.cs`) — POS Admin order lifecycle owner for e-commerce `Order` (NOT the POS `SalesInvoice`; see note).
- Generic status edits: `UpdateOrderStatusAsync` (`AdminOrderService.cs:335-472`) — but lifecycle terminal states are **blocked here** and forced onto dedicated endpoints (`AdminOrderService.cs:391-407`: throws `InvalidAdminOrderStateException` for `Cancelled/Shipped/Delivered/Completed/Refunded`).
- Cancellation: `CancelOrderAsync` (`AdminOrderService.cs:475+`) — requires reason (`AdminOrderService.cs:487-493`), rejects immutability after `Delivered/Completed` (`AdminOrderService.cs:517-520`).
- Order immutable after `Delivered`/`Completed`: `AdminOrderService.cs:383-389`.

### SQL aggregate / tables
- `Order` entity: `PointOfSale.Core/Entities/Order.cs:12` (`BaseEntity, ITenantOwned, IProjectionVersioned`). DbSet `Orders` (`ApplicationDbContext.cs:162`). Line items `OrderItems` (`:163`), `OrderRefunds` (`:164`), `OrderRefundLines` (`:165`), `OrderRequestIdempotency` (`:166`).
- **This is the e-commerce order aggregate**, explicitly separate from POS `SalesInvoice` (`Order.cs:8-11` doc comment "separate from POS SalesInvoice"). POS sales write to `SalesInvoice`/`SalesInvoiceItem` (`SalesInvoice.cs:9`).

### Lifecycle state machine
- Checkout lifecycle: `CheckoutState` enum + `TransitionCheckoutState(...)` (`Order.cs:106, 656-670`) with validated transitions `IsValidCheckoutTransition` (`Order.cs:672-689`): `Draft→PendingPaymentIntent→PaymentPending→Paid` (terminal `Paid`/`Cancelled` cannot advance). `Failed/Expired` can re-enter `PendingPaymentIntent`.
- Operational status: `OrderStatus Status` (`Order.cs:100`), fulfillment status `FulfillmentStatus` (`:200-218`, change-tracking via `AcceptFulfillmentStatusChange`).
- **Payment status encapsulation**: `PaymentStatus` can ONLY be changed by `IPaymentLifecycleService` — enforced at runtime by `PaymentStatusSaveChangesInterceptor` (`Order.cs:108-121, 149`). Direct assignment throws.
- **Fulfillment ownership immutability**: `FulfillmentMode` + `FulfillmentCarrierCode` snapshotted at creation, immutable, gate who controls fulfillment (Manual=admin via `IOrderFulfillmentService`; Carrier=carrier webhooks only) (`Order.cs:236-289`).

### Order immutability / reversal via returns
- No in-place mutation of financials after `Delivered/Completed`. Reversal is achieved via **`OrderRefund`** (`PointOfSale.Core/Entities/OrderRefund.cs:6`, `ITenantOwned`) + `OrderRefundLine` (`OrderRefundLine.cs:6` — negative amounts rejected at `:18-33`). `RefundId`, `IsFullRefund`, `FinancialPostingStatus` ledger posting fields (`:15-27`). Refunds map back to `OrderItemId` (`:20-25`).
- `Returns`/`ReturnItems` (`ApplicationDbContext.cs:117-118`) are the POS-side reversal aggregate (`Return.cs`, `ReturnItem.cs`) — distinct from e-commerce `OrderRefund`.

### Events fired
- `OrderCreatedEvent` (`OrderEvents.cs:13`), `OrderStatusChangedEvent` (`:69`), `OrderAcceptedEvent`/`OrderAwaitingEvidenceEvent`/`OrderOperationalReviewQueuedEvent`/`OrderRejectedEvent` (`:87-93`), `OrderPaymentStatusChangedEvent` (`:98`), `OrderShippedEvent` (`:116`), `OrderDeliveredEvent` (`:128`), `OrderCancelledEvent` (`:138`). All but some are `IAuthoritativeIntegrationEvent`.
- `AdminOrderService` publishes `OrderStatusChangedEvent` inside the commit boundary (`AdminOrderService.cs:444-455`).

### Projections written
- `OrderProjectionConsumer` → Mongo `orders` collection (`OrderProjectionConsumer.cs:50,65` → `OrderDocument`).
- `OrderDeliveredEventConsumer`, `OrderStatusSyncConsumer` + `OrderStatusSyncService` (cross-system sync, not a read model).
- `AuthoritativePurchaseAnalyticsConsumer` (`AuthoritativePurchaseAnalyticsConsumer.cs:18`) reacts to `OrderPaidEvent`, re-fetches SQL `Orders` with payment-status authority (`OrderPaidEvent`→ `Orders` filtered to `Collected/Settled/PartiallyRefunded/Refunded` at `:37-49`), and emits authoritative `AnalyticsEvent.PurchaseCompleted` — replaces client-reported purchase completion.

---

## 2. CUSTOMERS / SUPPLIERS / PARTY LEDGER

### Owner
- `CustomersController` (`PointOfSale.API/Controllers/CustomersController.cs:23`) backed by `ILedgerService`/`IPartyBalanceMaintenanceService` = `LedgerService` (`PointOfSale.API/Services/LedgerService.cs:13`).
- Suppliers: `SuppliersController` (not in task scope detail; `Supplier.cs`/`SupplierPayment.cs`/`SupplierTransactions.cs` exist).

### SQL aggregates / tables
- `Customer` (`Customer.cs:8`, `ITenantOwned`): balance fields `Balance` (`:25`), `OpeningBalanceAmount` (`:30`), `BalanceType` "Debit/Credit" (`:35`), `OpeningBalanceDate`. Navigations: `SalesInvoices`, `Transactions` (`CustomerTransaction`), `Addresses` (`:45-47`). DbSet `Customers` (`ApplicationDbContext.cs:47`).
- `Supplier` (`Supplier.cs:9`): ledger-sign `Balance` (`:36`), `OpeningBalanceAmount`, `BalanceType`, `PaymentTermsDays` (`:56`), nav to `SupplierPayments`/`SupplierTransactions`. DbSet `Suppliers` (`:50`), `SupplierPayments` (`:105`).
- Address modeled as event-sourced versions: `CustomerAddress` + `CustomerAddressVersion` (`Customer.cs:50-92`) — geographic catalog snapshot admission.
- `PartyLedgerAdjustment` (`PartyLedgerAdjustment.cs:10`, `ITenantOwned`): durable manual adjustment record (party type, txn type, idempotency key, reference number) for audit/replay. DbSet not separately listed but entity exists; written by ledger service.

### Ledger / balance maintenance
- `ILedgerService` + `IPartyBalanceMaintenanceService` (`LedgerService.cs:13`) maintain `Customer`/`Supplier` balances; `CustomersController` reads balance from `AccountLedgers` (`CustomersController.cs:90`) and seeds opening balance `Balance = 0` + `OpeningBalance*` at creation (`:158,222-229`).
- `CustomerTransaction` (`:121`), `SupplierTransaction` (`:122`), `AccountLedger` (`AccountLedger.cs`), `PartyLedgerAdjustment` form the party ledger.

### Events / projections
- Customer/supplier mutations are financial mutations; no dedicated Mongo read model found in scope — balances are authoritative in SQL `AccountLedger`/`Customer.Balance`. (Party transactions are NOT part of storefront Mongo projections.)

---

## 3. TAX

### Configuration model — NOTE: no dedicated Tax aggregate/table
Tax is **config-driven, not an aggregate**. Authority lives in two places:
1. **Per-tenant `SystemSetting` rows** with keys `"Tax.Enable"` and `"Tax.Rate"` (`TaxCalculationService.cs:53-59`). `SystemSetting` entity (`SystemSetting.cs:6`, `TenantId` nullable at `:13`, `Key`/`Value` strings `:15-16`). DbSet not separately enumerated but `SystemSetting` is a `BaseEntity`.
2. **Global `StorefrontPricingOptions.CountryTaxRates`** (appsettings) + `DefaultTaxRate` (`TaxCalculationService.cs:28, 83-88`).

### Owner service
- `ITaxCalculationService` → `TaxCalculationService` (`PointOfSale.API/Services/Pricing/TaxCalculationService.cs:9,24`). Resolves rate precedence: tenant disabled → `0` (`:61-66`); tenant `"Tax.Rate"` setting (`:68-79`); else tenant country (`:81-86` from `Tenants.Country`); else global default (`:88`). Returns `TaxComputationResult(Rate, Amount, Source, CountryCode)` (`:18-22`). Percentage rates normalized ≥1 → fractional (`:104-106`).

### SQL authority
- Read-only from `SystemSettings` + `Tenants.Country` (`TaxCalculationService.cs:53-100`). Tax is persisted per-order as denormalized columns `TaxAmount` on `Order` (`Order.cs:411`), `PurchaseInvoiceItems.TaxRate`/`TaxAmount` with CHECK constraints `CK_..._TaxRate_Valid` (`ApplicationDbContext.cs:1697,1706-1707`), `ReturnItems.TaxRate`/`TaxAmount` (`:1706-1707`), `Order.TaxAmount` precision (`:1774`).

### Events / projections
- None. Tax is computed at checkout/order time and frozen into the order's `TaxAmount`/`PricingSnapshotJson` (`Order.cs:411,426`). No Mongo projection.

---

## 4. SHIPPING (method config + carrier config)

### Entities / SQL tables
- `ShippingMethod` (`ShippingMethod.cs:8`, `ITenantOwned`): `BaseRate` (`:34`), `PerKgRate` (`:39`), `FreeShippingThreshold` (`:44`), `EstimatedDeliveryDaysMin/Max` (`:49-54`), `IsActive` (`:59`), `SortOrder`, `AvailableCountries` JSON (`:70`), `CarrierCode` (`:82`, links to carrier adapter), `ServiceLevel` (`:89`), `TrackingUrlTemplate` (`:94`). DbSet `ShippingMethods` (`ApplicationDbContext.cs:211`).
- `CarrierConfiguration` (`CarrierConfiguration.cs:22`, `ITenantOwned`): one per carrier per tenant; `ApiKey/ApiSecret/AccountId/ApiUrl` encrypted (`:56-72`), capabilities `SupportsCod/SupportsTracking/SupportsWebhooks/SupportsLabels/SupportsQuotes/SupportsCancellation` (`:86-112`), `Configuration` JSON (`:124`), service levels (`:174-179`), pricing overrides `UseCarrierRates/FixedShippingCost/MarkupPercentage/FreeShippingThreshold` (`:189-205`), validation telemetry `LastValidatedAtUtc/LastValidationSucceeded` (`:224-229`). DbSet `CarrierConfigurations` (`:218`) plus full carrier admin graph: `CarrierAccounts`, `CarrierCredentialReferences`, `CarrierWarehouseAssignments`, `CarrierAccountPolicies`, `CarrierServiceLevelEnablements`, `CarrierConnectionTestRecords`, `CarrierAdministrationAudit` (`:219-225`), and geographic mapping tables (`:251-258`).

### Owner
- `ShippingMethodsController` (`PointOfSale.API/Controllers/ShippingMethodsController.cs`) + `AdminShipmentService` (`PointOfSale.API/Services/Admin/AdminShipmentService.cs`) + `ShippingAdministrationApiService` (`PointOfSale.API/Services/ShippingAdministration/ShippingAdministrationApiService.cs`). `AdminCarrierConfigController` for carrier config.

### Events fired
- `ShippingMethodUpdatedEvent` (`ShippingMethodEvents.cs:16`), `ShippingMethodDeletedEvent` (`:37`). Published by `ShippingMethodsController` on create/update/delete/soft-delete (`ShippingMethodsController.cs:186,265,297,369,375,414-434`).

### Projections written
- `ShippingMethodProjectionConsumer` (`PointOfSale.Infrastructure/Consumers/ShippingMethodProjectionConsumer.cs:32-33`) → re-fetches SQL `ShippingMethods` (`:112-117`), tombstones if `IsDeleted` (`:130-142`), else `UpsertIfFreshAsync` to Mongo storefront shipping collection (`:150`).
- Fulfillment lifecycle is **cross-domain** (noted, not deep-dived): `Shipment` (`ApplicationDbContext.cs:214`), `CarrierConfiguration` graph, `ShipmentEvents.cs`, `Shipment*NotificationConsumer`s, `ShippingAdministration` namespace.

---

## 5. PAYMENT CONFIGURATION (merchant method/gateway config — config only)

### Entity / SQL table
- `TenantPaymentMethod` (`TenantPaymentMethod.cs:22`, `ITenantOwned`): `Code` (COD/STRIPE/PAYMOB) unique per tenant (`:35`), `DisplayName[Ar]` (`:41,47`), `IsEnabled` (`:63`), `SortOrder`, `SupportsOnline/SupportsOffline` (`:76,83`), `IsSandbox` (`:89`), **`ConfigEncrypted`** — secrets encrypted via Data Protection API, never returned/logged (`:92-104`), `Icon`, `MinOrderAmount/MaxOrderAmount`, `Fee/FeeType` (`:116-132`), `Version` for projection ordering (`:144`). DbSet `TenantPaymentMethods` (`ApplicationDbContext.cs:333`). Also `PointOfSale.Core.Entities.Accounting.PaymentMethod` (`:412`).

### Owner
- `PaymentMethodsAdminController` (`PointOfSale.API/Controllers/PaymentMethodsAdminController.cs`) — POS Admin is the ONLY controller (enable/disable/config). Storefront is read-only. **No payment-provider integration in this entity** (`TenantPaymentMethod.cs:15`).

### Events / projections
- `PaymentMethodCreatedEvent`, `PaymentMethodUpdatedEvent`, `PaymentMethodEnabledEvent`, `PaymentMethodDisabledEvent`, `PaymentMethodDeletedEvent` (`PaymentMethodProjectionConsumer.cs:30-34`). 
- `PaymentMethodProjectionConsumer` (`:30+`) → re-fetches SQL authority; if missing → rejected (`:164-178`); tombstones if `IsDeleted` (`:184-186`); else `UpsertDocument` to Mongo (`:189-194,356-390`).

### Important boundary
- **Payment LIFECYCLE** (capture, refund execution, webhooks) is owned by `IPaymentLifecycleService` / `PaymentLifecycleController` / `PaymentWebhooksController` — a SEPARATE domain. POS Admin here ONLY configures the method; `Order.PaymentStatus` is writable solely by that lifecycle service (`Order.cs:108-121`). `PaymentLifecycleEvents.cs` / `PaymentEvents.cs` are out of scope.

---

## 6. STORE SETTINGS (tenant/store config + projection)

### Entity / SQL table
- `StoreConfig` (`StoreConfig.cs:16`, `ITenantOwned`): **one per tenant** (`:11-14`). Embeds Theme (`StoreThemeConfig` `:46`,`193-199`), Behavior (`StoreBehaviorConfig` `:52`,`267-283` e.g. `EnableGuestCheckout`), `StorefrontStatus` live/maintenance/disabled (`:59`), `CommerceAi*` governance (`:143-187`), `PromotionRankingConfig` (`:130`,`304-448`), plus **draft/live theme versioning**: `ThemePresetKey`/`ThemeLivePresetKey` (`:87,93`), `ThemeDraftOverridesJson`/`ThemeLiveOverridesJson` (`:98,103`), `VersionsJson`/`LiveVersion` (`:109,115`), optimistic `RowVersion` (`:123`), `CustomizationJson` (`:82` — pages + advanced theme + navigation live/draft). DbSet `StoreConfigs` (`ApplicationDbContext.cs:294`) + `StoreConfigAuditEntries` (`:297`).

### Owner
- `AdminStoreConfigService` (`PointOfSale.API/Services/Admin/AdminStoreConfigService.cs`) — publish/rollback with `IPublishLockService` (concurrency lock `:111,339-340`), email-verified publish gate (`:182`), version snapshots (`CreateAndAppendVersionSnapshot` `:402,622`), audit (`AuditActionTypes.PagePublished` `:417`, `ThemePublished` `:640`).

### Events fired
- `StoreConfigUpdatedEvent` (`StoreConfigEvents.cs:12`), `StoreConfigCreatedEvent` (`:38`), `StoreConfigPublishedEvent` (`:65`, carries `ConfigVersion`/`Version`), `StoreConfigRolledBackEvent` (`:84`). Also `NavigationPublishedEvent` + `StorefrontFooterUpdatedEvent` published by `AdminStoreConfigService`/`AdminCmsService` (`AdminStoreConfigService.cs:670-681` staging `NavigationPublishedEvent`).

### Projections written
- `StorefrontConfigProjectionConsumer` (`StorefrontConfigProjectionConsumer.cs:31-35`) → Mongo `storefront_configs` (`:57`). Loads SQL authority via `TenantLineageAuthority.LoadCurrentStoreConfigProjectionAuthorityAsync` (`:170-187`), resolves live theme preset from `ThemePresets` SQL (`:193-199`), maps to `StorefrontConfigDocument` (with currency + smart offers + `ThemePresetKey` = `ThemeLivePresetKey` `:190-203`), fails closed if lineage unproven (`:418-441`), then **invalidates Redis** store-config cache (`:375-385`).

---

## 7. CMS (pages, navigation, footer, content blocks)

### Entities / SQL tables
- `CmsPage` (`CmsPage.cs:5`): `PageGroupId` (`:8`), `TenantId` (`:9`), `Slug`/`Title`/`Content`/`ContentType` (`:10-13`), **versioning** `Version`/`SchemaVersion` (`:14-15`), **publication flags** `IsDeleted`/`IsDraft`/`IsPublished`/`PublishedAt` (`:16-19`), SEO `MetaTitle/Description/Keywords/OpenGraphImageUrl` (`:20-23`). DbSet `CmsPages` (`ApplicationDbContext.cs:83`). Also `MarketplaceCmsPage` (`:84`).
- **Navigation & Footer** are NOT separate tables — they live as JSON inside `StoreConfig.CustomizationJson` (`NavigationProjectionConsumer.cs:128,281-321` parses `navigation.live.header/footer`). Dedicated footer tables exist: `StorefrontFooterSection`/`StorefrontFooterLink`/`StorefrontSocialLink` (`ApplicationDbContext.cs:86-88`) + `MarketplaceFooterSection/Link/SocialLink` (`:89-91`).
- Banners/content blocks: realized as `HomepageSection` entity (`HomepageSection.cs`) + `HomepageSectionEvents.cs`, distinct from CMS; not in this sub-agent's deep scope beyond noting `CmsPageEvents.cs` references banners inline.

### Owner
- `AdminCmsService` (`PointOfSale.API/Services/Admin/AdminCmsService.cs`) + `IAdminCmsService`; `CmsPageService` (`PointOfSale.API/Services/Cms/CmsPageService.cs`); `AdminStorefrontFooterService`; `MarketplaceCms` services.

### Events fired
- `StorefrontCmsPagePublishedEvent` (`CacheInvalidationEvents.cs:5`) + `StorefrontCmsPageDeletedEvent` (`:33`) — consumed for Mongo. Also `CmsPagePublishedEvent`/`CmsPageRolledBackEvent` (`CmsPageEvents.cs:13,53`) and `NavigationPublishedEvent` (`CmsPageEvents.cs:93`). `AdminCmsService` stages `NavigationPublishedEvent` on publish (`AdminStoreConfigService.cs:664-688`).

### Projections written
- `CmsPageProjectionConsumer` (`CmsPageProjectionConsumer.cs:19-21`) → Mongo `storefront_pages` (`:35`). **Fail-closed authority check**: re-fetches SQL `CmsPages` requiring `!IsDeleted && IsPublished && !IsDraft && PublishedAt != null` (`:87-93`); if not proven → tombstone (`:304-347`). Handles legacy (slug-keyed) vs modern (`PageGroupId`-keyed) pages, stale-slug cleanup (`:167-183`).
- `NavigationProjectionConsumer` (`NavigationProjectionConsumer.cs:27-28`) → Mongo `storefront_navigation` (`:44`), extracts live nav from `CustomizationJson`, defaults if absent (`:281-342,431-457`), invalidates Redis nav/footer/menu/header caches (`:462-476`).
- `StorefrontFooterProjectionConsumer` (`StorefrontFooterProjectionConsumer.cs:14`) → Mongo `storefront_footer` (`:28`), re-fetches SQL `StorefrontFooterSections`+`StorefrontSocialLinks` (`:112-127`), fails closed on inactive tenant (`:257-285`).
- All three guard with `ProjectionEventFence` + freshness filter + reviewed-mutation guard (e.g. `CmsPageProjectionConsumer.cs:472-499` overlays `StorefrontCms|StoreMetadata|Tombstone`).

---

## 8. MEDIA (entity, upload/config, storage)

### Entity / SQL table
- `ProductMedia` (`ProductMedia.cs:5`, `ITenantOwned`): `ProductId`, `MediaType`, `Url`, `Provider`, `Position`, `CreatedAtUtc` (`:7-14`). DbSet `ProductMedia` (`ApplicationDbContext.cs:53`). (Refund evidence media: `CommerceHubRefundEvidenceMedia.cs`.)

### Owner
- `IMediaService` (`PointOfSale.Core/Interfaces/IMediaService.cs:7`) — "Single source of truth for media uploads. All file uploads MUST go through this service" — `UploadImageAsync`/`UploadAttachmentAsync`/`DeleteMediaAsync` (`:9-11`).
- Implementation: `R2StorageService : IR2StorageService` (`PointOfSale.API/Services/R2StorageService.cs:58,78`) — Cloudflare **R2 / S3-compatible** (`using Amazon.S3` `:2`, `IAmazonS3` `:75`), `CreateUploadUrlAsync`/`GetObjectMetadataAsync`/`DeleteObjectAsync`/`UploadFileAsync` (`:12-49`).
- HTTP entry: `ProductMediaController` (`PointOfSale.API/Controllers/ProductMediaController.cs:11`) → `IProductMediaService` (`ProductMediaService.cs:20`); `ImageService` (`ImageService.cs:5,38,152`) wraps `UploadImageAsync`.

### SQL authority + storage
- Metadata row written to `ProductMedia` (SQL); bytes stored in R2 object store (`R2StorageService`). `ProductMediaChangedEvent` (`ProductMediaChangedEvent.cs`) notifies search-index consumer.

### Events / projections
- `ProductMediaChangedEvent` → `ProductMediaChangedEventConsumer` (search index). No dedicated Mongo read model for media beyond product documents.

---

## 9. THEME (entity/config + projection)

### Entity / SQL table
- `ThemePreset` (`ThemePreset.cs:10`) — **system-level immutable preset, shared across all tenants** (`:6-9`): `Key` (`:17`), `Name`, `IsRtl` (`:23`), `BaseThemeJson` (`:29`), `PreviewImageUrl`. DbSet `ThemePresets` (`ApplicationDbContext.cs:300`).
- Tenant-specific theme selection lives in `StoreConfig`: `ThemeId` (`:40`), `ThemePresetKey`/`ThemeLivePresetKey` (`:87,93`), draft/live override JSON (`:98,103`), versioned publish (`:109,115`) — see §6.

### Owner
- Theme publishing owned by `AdminStoreConfigService.PublishThemeAsync` (`AdminStoreConfigService.cs:581-656`) — applies draft→live preset, appends version snapshot (`:622`), audit (`:640`).

### SQL authority + projection
- `StorefrontConfigProjectionConsumer` resolves `ThemeLivePresetKey` → loads `ThemePresets` SQL and embeds into `StorefrontConfigDocument` (`StorefrontConfigProjectionConsumer.cs:190-203`). No separate theme Mongo collection; theme is part of `storefront_configs`.

---

## 10. LOCALIZATION

### Runtime
- **No standalone `LocalizationService`/resx/resource file** exists in this bounded context (grep for `localiz`/`.resx` across `PointOfSale.Core`+`PointOfSale.API` returns nothing). Localization in the Merchant Platform is realized two ways:
  1. **Per-field bilingual columns** on content entities: e.g. `ShippingMethod.NameAr` (`ShippingMethod.cs:24`), `TenantPaymentMethod.DisplayNameAr/DescriptionAr` (`TenantPaymentMethod.cs:47,57`), `StorefrontFooterSection.TitleAr`/`StorefrontFooterLink.LabelAr` (consumed in `StorefrontFooterProjectionConsumer.cs:132-148`), navigation `LabelAr` (`NavigationProjectionConsumer.cs:406`).
  2. **Marketplace translation subsystem** (adjacent context, noted not deep-dived): `MarketplaceCategoryTranslation` (`MarketplaceCategoryTranslation.cs`), `MarketplaceCategoryTranslationService`, `MarketplaceProductLocalizationService`, `MarketplaceTranslationCacheService` (`PointOfSale.API/Services/MarketplaceLocalization/`).
- Language routing at analytics level is hard-coded `"server"` (`AuthoritativePurchaseAnalyticsConsumer.cs:81`) — storefront request language is captured elsewhere (storefront runtime, out of POS Admin scope).

### SQL authority + projections
- Bilingual fields are columns on the respective SQL aggregates; projected verbatim into Mongo storefront docs (`storefront_footer`, `storefront_navigation`, `storefront_configs`).

---

## 11. NOTIFICATION CONFIG (preferences/templates config — NOT sending)

> No entity named `NotificationConfig`/`NotificationPreference` exists (grep negative). The "notification config" domain is realized as **channel + template configuration** backed by SQL:

### Entities / SQL tables
- `WhatsAppAccount` (`WhatsAppAccount.cs:9`, `TenantEntity`): per-channel toggles `UseForOrderConfirmation` (`:34`), `UseForOtpVerification` (`:39`), `UseForPaymentApproval` (`:44`), `UseForAbandonedCartRecovery` (`:50`), `Enabled` (`:29`), `Status` QR/pending (`:24`), `UpdatedAtUtc`. DbSet presence implied by `WhatsApp*` entities.
- `WhatsAppMessageTemplate` (`WhatsAppMessageTemplate.cs:5`, `TenantEntity`): `Type` (OrderConfirmation/OTP) (`:12`), `Language` en/ar (`:17`), `Content` with `{{variables}}` (`:22`), `IsActive` (`:24`), `IsDefault` (`:29`).
- `WhatsAppRateLimit` (`WhatsAppRateLimit.cs`) — rate-limit config.
- `TenantAutomationSettings` (`TenantAutomationSettings.cs:20`, `TenantEntity`): abandoned-cart recovery automation config — `EnableAbandonedRecovery` (`:27`, default **false**), `FirstMessageDelayMinutes`/`SecondMessageDelayMinutes` (`:37,48`), working-hours window (`:57,66`), `N8nWebhookApiKey` (`:73`). Explicitly "admin-controlled, feature-gated, safe defaults disabled" (`:7-18`).
- Email/SMTP + other channel config: stored as `SystemSetting` rows (generic key/value, `SystemSetting.cs`) or tenant fields (`Tenant.ContactEmail` `Tenant.cs:42`, `StorefrontBaseUrl` `:100`). No dedicated EmailConfig aggregate found.

### Owner
- WhatsApp: WhatsApp admin services (out of deep scope). Automation: `TenantAutomationSettings` managed via admin settings controller. These configure; the **notification SENDING** domain (WhatsApp Baileys transport, email dispatch) is separate and not part of POS Admin write runtime beyond config rows.

### Events / projections
- No dedicated Mongo projection for notification config. Channel/template rows are authoritative in SQL and consumed at send-time by the (separate) notification-sending runtime.

---

## 12. ANALYTICS (event ingestion + read models)

### Ingestion pipeline (write side)
- Storefront emits `AnalyticsEventReceivedEvent` (`AnalyticsEvents.cs:25`) → **`AnalyticsEventConsumer`** (`PointOfSale.Infrastructure/Consumers/AnalyticsEventConsumer.cs:38`). Pipeline (`:13-37`): Redis hot store → Mongo cold store → attribution engine → dispatcher fan-out (GA4/Meta/Admin). Rules: idempotent by EventId (`:48`), Redis failure must NOT block Mongo (`:31-33`), dispatch/attribution failures must NOT block storage (`:34-35`), tenant isolation enforced (Guid.Empty rejected, `:36-37`).
- **Authoritative server-side purchase events**: `AuthoritativePurchaseAnalyticsConsumer` (`AuthoritativePurchaseAnalyticsConsumer.cs:18`) consumes `OrderPaidEvent`, re-reads SQL `Orders` with payment-status authority (`Collected/Settled/PartiallyRefunded/Refunded`, `:37-49`) + active-tenant check (`:51-56`), then emits `AnalyticsEvent.PurchaseCompleted` (`:67`) — server truth replaces client-reported purchase.

### Read models (what is READ vs WRITTEN)
- **WRITTEN** (append-only cold store): `AnalyticsEventDocument` (`PointOfSale.Infrastructure/MongoDB/Documents/AnalyticsEventDocument.cs:21`) — `EventId`, `TenantId`, `EventType`, `OccurredAtUtc`, `ReceivedAtUtc`, `Actor` (customer/session/auth), `Context` (page/UA/IP/device), `Payload` (orderId/value/extra). Indexed by TenantId+OccurredAtUtc and TenantId+EventType (`:9-18`); optional TTL for GDPR.
- **WRITTEN** (hot store): Redis `IAnalyticsHotStoreService` (referenced `AnalyticsEventConsumer.cs:40`) — recent events, fast path.
- **READ**: Admin analytics dashboards — `AdminAnalyticsController`, `AdminAnalyticsFunnelsController`, `AdminAnalyticsSessionsController`, `AdminAnalyticsAttributionController`, `AdminAnalyticsAlertsController`, `AdminStorefrontAnalyticsDashboardController`, storefront `StorefrontAnalyticsController`, plus `AffiliateAnalyticsController`. These query the Mongo cold store + Redis hot store + dispatcher pipelines; they do not write the authoritative events.
- Config: `TenantAnalyticsConfigurations` DbSet (`ApplicationDbContext.cs:338`) — per-tenant analytics dispatcher configuration (e.g., GA4/Meta destinations), managed by admin.

### Key architectural point
- Analytics **writes** are dual-store (Redis + Mongo) and idempotent; **read** models are the Mongo/Redis stores consumed by Admin/Storefront analytics controllers. The single authoritative purchase signal is server-emitted by `AuthoritativePurchaseAnalyticsConsumer` from SQL `Orders` authority — never client-trust.

---

## Appendix — Consumer → Mongo collection map (all in `PointOfSale.Infrastructure/Consumers/`)

| Consumer | SQL authority | Mongo collection | Redis |
|---|---|---|---|
| `OrderProjectionConsumer` | `Orders` | `orders` (`OrderDocument`) | — |
| `CmsPageProjectionConsumer` | `CmsPages` | `storefront_pages` | invalidate |
| `NavigationProjectionConsumer` | `StoreConfig.CustomizationJson` | `storefront_navigation` | nav/footer/menu/header |
| `StorefrontFooterProjectionConsumer` | `StorefrontFooterSections`/`StorefrontSocialLinks` | `storefront_footer` | — |
| `StorefrontConfigProjectionConsumer` | `StoreConfig`+`ThemePresets`+`Tenants` | `storefront_configs` | store-config |
| `ShippingMethodProjectionConsumer` | `ShippingMethods` | storefront shipping | invalidate |
| `PaymentMethodProjectionConsumer` | `TenantPaymentMethods` | storefront payment methods | — |
| `AuthoritativePurchaseAnalyticsConsumer` | `Orders`+`Tenants` | (emits AnalyticsEvent) | — |
| `AnalyticsEventConsumer` | (event payload) | `analytics` (cold) + Redis (hot) | Redis hot |

All consumers: acquire `ProjectionEventFence`, re-fetch SQL, run `DerivativeProjectionMutationGuard.RequireReviewedMutation` with `HasSqlAuthority:true, FailsClosedWhenAuthorityMissing:true`, apply freshness filter (`LastEventId/LastEventOccurredAtUtc/LastEventVersion`), and tombstone/delete on unproven authority.

## Appendix — Key files
- Entities: `PointOfSale.Core/Entities/{Order,Customer,Supplier,PartyLedgerAdjustment,ShippingMethod,CarrierConfiguration,TenantPaymentMethod,StoreConfig,CmsPage,ProductMedia,ThemePreset,SystemSetting,WhatsAppAccount,WhatsAppMessageTemplate,TenantAutomationSettings,OrderRefund,OrderRefundLine,Tenant}.cs`
- Events: `PointOfSale.Core/Events/{OrderEvents,OrderFulfillmentEvents,CmsPageEvents,CacheInvalidationEvents,StoreConfigEvents,ShippingMethodEvents,PaymentMethodEvents,AnalyticsEvents,TenantCurrencyChangedEvent}.cs`
- Consumers: `PointOfSale.Infrastructure/Consumers/*ProjectionConsumer.cs`, `AuthoritativePurchaseAnalyticsConsumer.cs`, `AnalyticsEventConsumer.cs`
- Services: `PointOfSale.API/Services/Admin/{AdminOrderService,AdminStoreConfigService,AdminCmsService,AdminStorefrontFooterService,AdminShipmentService}.cs`, `Pricing/TaxCalculationService.cs`, `LedgerService.cs`, `R2StorageService.cs`, `ProductMediaService.cs`, `Cms/CmsPageService.cs`
- Controllers: `PointOfSale.API/Controllers/{ShippingMethodsController,CustomersController,ProductMediaController,PaymentMethodsAdminController,AdminStoreConfigController,AdminCmsPagesController}.cs`
- DbContext: `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
