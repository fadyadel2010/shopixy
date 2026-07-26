# POS Admin — Domain Architecture (Implementation-Backed Canonical Domain Map)

> Implementation is the ONLY source of truth. Every claim below is traced to `file:line`
> in the backend (`PointOfSale.API`, `PointOfSale.Core`, `PointOfSale.Infrastructure`) or
> the POS Admin frontend (`PointOfSale.Web`). Prior markdown, the sidebar labels, React
> component names, DTOs, API routes, and comments are secondary and were re-verified
> against code. Where implementation contradicts the brief's assumptions, the implementation
> wins — and the contradiction is recorded in the **Documentation Validation** section.

---

## 1. Executive Summary

POS Admin is **not a runtime, not a backend service, and not a separate deployment**. It is a
**tenant-scoped business workspace** — a single React SPA (`PointOfSale.Web`) authenticated with
the POS operator JWT (`Jwt`/`PointOfSaleAPI` scheme, `PointOfSale.API/Program.cs:2277,2281`,
`AuthController` `UserRole` Owner/Admin/Manager/Cashier `PointOfSale.API/Controllers/AuthController.cs:104`)
that presents one navigation tree (`PointOfSale.Web/src/components/Sidebar.jsx`) whose entries are
**links into many heterogeneous backend runtimes**. The workspace is a *composition shell*: it does
not own aggregates, it does not own persistence, it does not own authority. It initiates intent and
reads projections. The authoritative write model for every domain the shell touches lives in

- a single PostgreSQL/SQL-Server database (`ApplicationDbContext`),
- surfaced by dedicated backend runtimes (Controllers + Services) in `PointOfSale.API`,
- projected to MongoDB / Redis / Elasticsearch by shared MassTransit consumers.

Therefore the correct classification is: **POS Admin = a multi-runtime Business Workspace / tenant
operational composition shell sitting on top of a shared SQL authority + a shared projection
backbone.** It is closer to an "ERP-style back office" in *function* (it unifies retail ERP,
accounting/treasury, inventory, procurement, CRM-party, catalog, commerce, marketplace, affiliate,
and services-marketplace operations for one tenant) but it is *not* itself an ERP engine — the ERP
behaviour is owned by the underlying runtimes (POS Runtime, Treasury/Accounting Runtime,
Inventory Runtime, Procurement Runtime, CRM-party Runtime, Catalog Runtime, CommerceHub Runtime,
Affiliate Platform, Services Marketplace Runtime). POS Admin is the **unifying presentation and
command surface** for all of them.

Key verified facts that drive this conclusion:
- One SPA hosts the shell + embedded runtime surfaces: `App.jsx` imports both POS Admin pages
  (`/pos`, `/products`, `/treasury`, `/opening-balance/*`, `/ecommerce/*`) AND SuperAdmin pages
  (`/super-admin/*`, `App.jsx:138-159`) and the CommerceHub vendor module and Affiliate portal and
  Services Marketplace — i.e. POS Admin is the *tenant* workspace inside a shared host, not a
  standalone app.
- Every write controller used by POS Admin lives in `PointOfSale.API/Controllers` (e.g.
  `SalesController`, `PurchasesController`, `CashboxController`, `CustomersController`,
  `SuppliersController`, `CategoriesController`, `ProductsController`, `UnitsController`,
  `WarehousesController`, `BanksController`, `AdminInventoryController`, `AdminAccountingController`,
  `AdminFinanceController`, `AdminStoreConfigController`, `AdminCmsPagesController`,
  `Controllers/Admin/WhatsAppAdminController.cs`). Each writes its own SQL aggregate.
- The shell is gated by role (`user?.role === 'Owner'|'Admin'|'SuperAdmin'|'Accountant'`,
  `Sidebar.jsx:264-287`) and by CommerceHub vendor *capabilities* computed at login
  (`canAccessCommerceHubVendorNavigationCapability`, `Sidebar.jsx:7,307-308`), proving the shell
  merely exposes capabilities owned by other runtimes; it holds none itself.
- There is **no `PosAdmin` aggregate, no `PosAdminDbContext`, no POSIX` event bus, no POS-Admin
  schema** anywhere in the backend. POS Admin has zero dedicated persistence.

---

## 2. POS Admin Purpose

**Why POS Admin exists:** to give one authenticated tenant operator (Owner/Admin/Manager/Cashier,
plus finance-restricted roles Accountant/SuperAdmin for the Finance Control Center) a single,
role-filtered, capability-filtered workspace to operate the entire tenant business: configure the
catalog/units/warehouses, register parties (customers/suppliers/sales reps), seed opening balances,
run daily retail (sales/returns/purchases/purchase-returns/cashbox), manage inventory, run the
tenant ledger/treasury, publish to CommerceHub, manage the storefront, run affiliate & services
marketplace operations, and read operational/analytical reports.

**Who uses it:** tenant operators authenticated via the POS-operator JWT realm
(`AuthController.Login` → `GenerateTokens`, `PointOfSale.API/Controllers/AuthController.cs:266-336`;
`UserRole` Owner/Admin/Manager/Cashier). Finance-restricted areas additionally require
`Accountant`/`SuperAdmin` (`Sidebar.jsx:264-278`).

**Responsibilities inside POS Admin (the shell):**
- Render a single navigation tree and route to the correct backend runtime (`Sidebar.jsx`).
- Enforce client-side role/capability visibility (`Sidebar.jsx:297-324`).
- Carry the operator JWT; resolve tenant from the server (`TenantResolutionMiddleware` strips
  `X-Tenant-Id`, `PointOfSale.API/Middleware/TenantResolutionMiddleware.cs` per
  `platform-architecture-reverse-engineering` reference).
- Initiate commands (POST/PUT) that the owning runtime executes atomically.
- Read read-models (SQL reports + Mongo/Redis/ES projections).

**Responsibilities intentionally delegated outside POS Admin (to owning runtimes):**
- All SQL authority and transactional consistency — owned by the lifecycle controllers/services.
- Payment capture/refund webhooks — `IPaymentLifecycleService` + `PaymentWebhooksController` /
  `CommerceHubPaymentsController` (POS Admin only *configures* `TenantPaymentMethod`,
  `payments` capability surfaces `commercehub.payments.view`).
- Notification *sending* — WhatsApp account *config* is owned by POS Admin
  (`Controllers/Admin/WhatsAppAdminController.cs` via `IWhatsAppAccountService`), but dispatch is
  a separate outbound-delivery runtime (`whatsapp-runtime-final-architecture`).
- Storefront rendering — read-only; POS Admin only builds/publishes (`AdminStoreConfigController`,
  `AdminCmsPagesController`).
- Platform governance — SuperAdmin runtime, a *different* JWT realm
  (`SuperAdminJwt`, `PointOfSale.API/Program.cs:2356`), even though its pages are hosted in the
  same SPA.

---

## 3. Domain Discovery — Runtimes Behind the POS Admin UI

The sidebar (`Sidebar.jsx:45-294`) enumerates the workspace. Each entry maps to a runtime below.
"Runtime" = the backend bounded context that owns the SQL authority for that screen's data.

| Sidebar section | Screen path | Owning runtime (backend) | Authority evidence |
|---|---|---|---|
| Dashboard | `/` | Read-only operational aggregate (DashboardController) | `DashboardController.cs` |
| Database → Customers | `/customers` | CRM-party Runtime | `CustomersController.cs`, `Customer` entity |
| Database → Suppliers | `/suppliers` | CRM-party Runtime | `SuppliersController.cs`, `Supplier` entity |
| Database → Sales Reps | `/sales-representatives` | CRM-party Runtime (attribution) | `SalesRepresentativesController.cs`, `SalesRepresentative` |
| Database → Categories | `/categories` | Catalog Runtime | `CategoriesController.cs`, `Category` |
| Database → Units | `/units` | Catalog Runtime (uom) | `UnitsController.cs`, `Unit` |
| Database → Warehouses | `/warehouses` | Inventory Runtime (location) | `WarehousesController.cs`, `Warehouse` |
| Database → Banks | `/banks` | Treasury/Accounting Runtime (config) | `BanksController.cs`, `BankAccount` |
| Database → Products | `/products` | Catalog Runtime (+ Variant/Stock) | `ProductsController.cs`,`ProductVariantsController.cs` |
| Accounts → Main/Sub | `/financial-accounts*` | Treasury/Accounting Runtime (CoA) | `AdminFinanceController.cs`, `Account` |
| Accounts → Balance Transfer | `/balance-transfer` | Treasury/Accounting Runtime | `TreasuryController.cs`/`TreasuryOperationService` |
| Opening Balance → Cashbox | `/opening-balance/cashbox` | Treasury/Accounting + POS Cashbox Runtime | `CashboxController.SetOpeningBalance` `:1212` |
| Opening Balance → Customers | `/opening-balance/customers` | Treasury/Accounting (party ledger) | `CustomersController` `/opening-balances` |
| Opening Balance → Suppliers | `/opening-balance/suppliers` | Treasury/Accounting (party ledger) | `SuppliersController` `/opening-balances` |
| Opening Balance → Warehouse Stock | `/opening-balance/warehouse-stock` | Inventory Runtime (bootstrap stock) | `WarehousesController.GetOpeningStock` `:423`+`AdjustStockAsync` |
| Transactions → Sales (POS) | `/pos` | **POS Runtime** | `SalesController.cs` |
| Transactions → Sales Returns | `/sales-returns` | **POS Runtime** | `SalesReturnsController.cs` |
| Transactions → Purchases | `/purchase-invoice` | **Procurement Runtime (POS)** | `PurchasesController.cs` |
| Transactions → Purchase Returns | `/purchase-returns` | **Procurement Runtime (POS)** | `PurchaseReturnsController.cs` |
| Treasury → Dashboard/Hub/Accounts | `/treasury/*` | Treasury/Accounting Runtime | `TreasuryController.cs`,`AdminAccountingController.cs` |
| Treasury → Payments/Adjustments/Outstanding | `/payments`,`/adjustments`,`/outstanding-balances` | Treasury/Accounting + CRM-party Runtime | `AdminFinanceController.cs`,`LedgerService` |
| Inventory → Balance/Transfers/Barcode | `/inventory`,`/stock-transfers`,`/barcode-printing` | Inventory Runtime | `AdminInventoryController.cs`,`StockTransfersController.cs` |
| Expenses → Categories/Movement/Report | `/expense-categories`,`/expenses`,`/reports/expenses` | Treasury/Accounting (expense sub-ledger) | `ExpensesController.cs`,`ExpenseCategoriesController.cs` |
| Account Statement → SOA/Bank/Visa | `/statement-of-account`,`/reports/bank-statement`,`/reports/visa-statement` | Treasury/Accounting (party + clearing) | `StatementOfAccountController.cs` |
| E-commerce → Orders | `/ecommerce/orders` | CommerceHub Order Runtime (tenant vendor view) | `CommerceHubOrdersController.cs` (cap `commercehub.orders.view`) |
| E-commerce → Blocked Phones | `/ecommerce/blocked-phones` | CommerceHub governance (tenant) | `AdminPhonesController.cs` |
| E-commerce → WhatsApp | `/ecommerce/whatsapp` | WhatsApp Account Config Runtime | `Controllers/Admin/WhatsAppAdminController.cs` |
| E-commerce → Cart Recovery | `/ecommerce/cart-recovery` | Storefront Cart Recovery Runtime | `AdminAbandonedCartRecoveryController.cs` |
| E-commerce → Abandoned Checkouts | `/ecommerce/abandoned-checkouts` | Storefront Checkout Runtime | `AdminAbandonedCheckoutsController.cs` |
| E-commerce → Shipping Setup | `/admin/shipping/setup` | Shipping/Carrier Runtime | `AdminShippingAdministrationController.cs` |
| E-commerce → Online Customers | `/ecommerce/online-customers` | Storefront Customer Identity Runtime | `OnlineCustomersAdminController.cs` |
| E-commerce → Auth Providers | `/ecommerce/auth-providers` | Storefront Identity Runtime | `StorefrontAuthProvidersAdminController.cs` |
| E-commerce → Payment Methods | `/ecommerce/payment-methods` | Payment Config Runtime (tenant) | `PaymentMethodsAdminController.cs` |
| E-commerce → Manual Payments | `/ecommerce/manual-payments` | Payment Config Runtime | `AdminManualPaymentController.cs` |
| E-commerce → Coupons/Promotions/Smart Offers | `/ecommerce/coupons`,`/promotions`,`/smart-offers` | Promotion/Coupon Runtime (CommerceHub-scoped) | `CouponsController.cs`,`PromotionsController.cs`,`AdminSmartOffersController.cs` |
| E-commerce → Commerce AI | `/ecommerce/commerce-ai` | Commerce AI Runtime | `AdminCommerceAiController.cs`,`AdminAiProductGeneratorController.cs` |
| E-commerce → Pixel Tracking/Analytics | `/analytics/*`,`/settings/analytics` | Tracking & Attribution (Marketing Analytics) Runtime | `AdminAnalyticsController.cs`,`AdminAnalyticsAttributionController.cs` |
| Services Marketplace | `/marketplace` | **Services Marketplace Runtime** (internal POS-to-POS B2B) | `MarketplaceController.cs` (POS Admin only, per memory/architecture rules) |
| CommerceHub → Publishing/Finance/Earnings/Debt | `/admin/commercehub`,`/commercehub/*` | **CommerceHub Runtime** (tenant vendor module) | `AdminCommerceHubController.cs`,`AdminCommerceHubFinanceController.cs`,`CommerceHubController.cs` |
| Affiliate Management | `/admin/affiliates` | **Affiliate Platform** (tenant operational) | `AdminAffiliateController.cs`,`AdminAffiliateCategoryController.cs` |
| Storefront Builder | `/storefront*` | Storefront Builder/Publisher Runtime | `AdminStoreConfigController.cs`,`AdminCmsPagesController.cs`,`AdminStorefrontFooterController.cs` |
| Reports | `/sales-history`,`/reports/*`,`/sales-rep-reports` | Read-only reporting (SQL) | `ReportsController.cs`,`SalesRepReportsController.cs` |
| Finance Control Center | `/treasury/trial-balance`,`/profit-loss`,`/balance-sheet`,`/general-ledger`,`/cash-flow`,`/clearing-aging`,`/transfers` | Treasury/Accounting (ledger-only reports) | `AdminAccountingController.cs`,`FinancialReportService` |
| Settings | `/settings` | Tenant Config Runtime (KV) | `SettingsController.cs` (tenant-scoped `ITenantContext.RequiredTenantId`) |

**Conclusion:** POS Admin is a **composition shell over ~18 distinct backend runtimes**, not a
single runtime. Several of those runtimes are *not* "POS" at all — they are tenant views into
CommerceHub, Affiliate, Services Marketplace, Storefront, and Tracking/Attribution runtimes that
happen to be reachable from the same workspace because the tenant operator is also a vendor/affiliate/
provider participant.

---

## 4. Runtime Classification (every runtime behind the shell)

Each runtime below is the backend bounded context the shell delegates to. For the full per-runtime
internals, see the sibling platform docs (cited). This map gives the POS-Admin-centric view.

### 4.1 POS Runtime (retail execution)
- Purpose: in-person sales/returns/purchases/purchase-returns/cashbox, atomic SQL, synchronous
  accounting post.
- Aggregate Roots: `SalesInvoice`, `PurchaseInvoice`, `Return`, `Cashbox`, `CashboxTransaction`,
  `ProductVariantStock`/`StockMovement` (POS mutation).
- Owned entities/VOs: `SalesInvoiceItem`, `SalesInvoicePayment` (entity exists, unused on sale path),
  `ReturnItem`, `RefundMethod`, `StockMovement`.
- Commands: `CreateSale`, `FinalizeSalesReturn`, `CreatePurchase`, `CreatePurchaseReturn`,
  `OpenCashbox`, `CloseCashbox`, `CashIn/Out`, `RecordExpense`.
- Queries: `GetReceipt`, `GetCashboxReport`, `Dashboard` (POS slice).
- Events: **none emitted from the POS mutation path** — `_eventPublisher` is injected into
  `SalesController` but never called (`pos-runtime-reverse-engineering` reference, verified gap).
- Persistence: PostgreSQL (`SalesInvoices`, `PurchaseInvoices`, `Returns`, `Cashboxes`,
  `CashboxTransactions`, `ProductVariantStocks`, `StockMovements`). No Mongo projection for POS
  (reports read SQL directly).
- Projection: none (SQL-authority, no projection) for the cash/ledger-critical part.
- Lifecycle: single SQL transaction per operation; accounting via outbox (synchronous post + worker
  recovery).
- Authority: POS controllers write `SalesInvoices`+`ProductVariantStocks`+`AccountingOutboxMessages`.
- Consistency: serializable-ish single tx; deterministic stock lock order `(WarehouseId,VariantId)`.
- Recovery: idempotency `(TenantId,Key,Operation)`+RequestHash; accounting-outbox versioning.
- Evidence: `pos-runtime-final-architecture.md`; `SalesController.cs`, `SalesReturnsController.cs`,
  `PurchasesController.cs`, `PurchaseReturnsController.cs`, `CashboxController.cs`.

### 4.2 Treasury & Accounting Runtime (tenant GL)
- Purpose: double-entry tenant GL; treasury is a view layer on the ledger; opening balances;
  party ledgers; financial reports.
- Aggregate Roots: `Account` (CoA node), `JournalEntry` (immutable balanced doc), `LedgerEntry`,
  `TreasuryAccount` (view), `AccountLedger` (party ledger authority), `CustomerTransaction`/
  `SupplierTransaction` (operational projection), `PartyLedgerAdjustment` (intent), `Expense`.
- Commands: `PostJournal`, `ReverseJournal`, `SetOpeningBalance` (cashbox/customer/supplier),
  `RecordExpense` (approved), `TreasuryTransfer/Deposit/Withdraw`, `SettleClearing`.
- Queries: Trial Balance, P&L, Balance Sheet, Cash Flow, General Ledger, Clearing Aging, SOA,
  Treasury Dashboard — all **live SQL**, no Mongo/Redis/ES for tenant GL.
- Events: downstream of other runtimes via the shared outbox (`AccountingOutboxMessage`);
  `CommerceHubLedgerPosting` is the CommerceHub→GL bridge.
- Persistence: PostgreSQL `JournalEntries`/`LedgerEntries`/`Accounts`/`TreasuryAccounts`/
  `AccountLedgers`/`Expenses`.
- Authority: `FinancialPostingService.PostJournalAsync` is the ONLY balancer; treasury inflow/outflow
  authority validators forbid arbitrary money; legacy `TreasuryTransactions` write-blocked by DB
  triggers (`treasury-accounting-architecture.md`).
- Recovery: idempotency key + `Serializable` re-check; reversal journals only.
- Evidence: `treasury-accounting-architecture.md`; `FinancialPostingService.cs`,
  `Account.cs`, `JournalEntry.cs`, `LedgerEntry.cs`, `TreasuryAccount.cs`, `AccountLedger.cs`,
  `LedgerService.cs`.

### 4.3 Inventory Runtime
- Purpose: stock truth per `(WarehouseId,VariantId)`, transfers, opening stock bootstrap, valuation.
- Aggregate Roots: `ProductVariantStock`, `StockMovement` (append-only), `Warehouse`, `StockTransfer`.
- Commands: `AdjustStockAsync`, `TransferStock`, `SetOpeningStock`.
- Queries: balance, movements, valuation, opening-stock status.
- Events: `StockSyncOutboxMessage` (committed stock); consumers republish to search/inventory
  projections.
- Persistence: PostgreSQL `ProductVariantStocks`, `StockMovements`, `Warehouses`,
  `StockTransfers`.
- Authority: SQL is stock truth; `ProductVariantStockMutationSupport.LockMultipleStocksAsync`
  deterministic lock.
- Consistency: direct-lock mutation at POS; reservation (TTL) only at Storefront/CommerceHub.
- Evidence: `inventory-runtime-final-architecture.md`; `AdminInventoryController.cs`,
  `WarehousesController.cs`, `StockTransfersController.cs`, `ProductVariantStockMutationSupport.cs`.

### 4.4 Catalog Runtime
- Purpose: products, variants, categories, units — the merchant catalog.
- Aggregate Roots: `Product`, `ProductVariant`, `Category`, `Unit`.
- Commands: CRUD product/variant/category/unit; publish to CommerceHub (event).
- Queries: catalog browse (Mongo projection + SQL-authority overlay for storefront).
- Events: `ProductCreatedEvent`/`UpdatedEvent`/`DeletedEvent` → `ProductCreatedEventConsumer`
  upserts `ProductDocument` (Mongo) idempotently; `ProductSqlAuthorityProjectionWriter` tombstones
  when SQL no longer proves `IsActive && IsOnlineVisible` (fail-closed).
- Persistence: PostgreSQL `Products`/`ProductVariants`/`Categories`/`Units`; Mongo `products`
  projection; ES product index.
- Authority: SQL catalog authority; ES single authority for search (no Mongo fallback → 503).
- Evidence: `product-catalog-architecture.md`, `product-composition-runtime-final-architecture.md`;
  `ProductsController.cs`, `ProductVariantsController.cs`, `CategoriesController.cs`,
  `UnitsController.cs`.

### 4.5 CRM-Party Runtime
- Purpose: customers, suppliers, sales reps, and their balances/opening balances.
- Aggregate Roots: `Customer`, `Supplier`, `SalesRepresentative`.
- Commands: register party; `SetOpeningBalance` (posts to `AccountLedger` + `CustomerTransaction`/
  `SupplierTransaction` via `LedgerService`, `LedgerService.cs:216-356`); adjustments.
- Queries: party statement (`StatementOfAccountController`), outstanding balances.
- Events: party-balance adjustments enqueue `AccountingOutboxMessage`
  (`IAccountingOutboxService.EnqueuePartyLedgerAdjustmentAsync`).
- Persistence: PostgreSQL `Customers`/`Suppliers`/`SalesRepresentatives` + `AccountLedgers` +
  `CustomerTransactions`/`SupplierTransactions`.
- Authority: party identity + balance owned here; GL control accounts remain accounting authority.
- Evidence: `CustomersController.cs`, `SuppliersController.cs`, `SalesRepresentativesController.cs`,
  `LedgerService.cs`, `StatementOfAccountController.cs`.

### 4.6 Procurement Runtime (POS purchases)
- Purpose: supplier purchases + purchase returns, payable-side mirror of POS sale.
- Aggregate Roots: `PurchaseInvoice`, `Return` (PurchaseReturn), `SupplierPayment`.
- Commands: `CreatePurchase`, `CreatePurchaseReturn`, `PostSupplierPayment`.
- Persistence: PostgreSQL `PurchaseInvoices`/items/payments, `Returns`, `AccountingOutboxMessages`.
- Authority: writes its own SQL; posts to GL via outbox (`PostPosPurchase`/`PostPurchaseReturn`/
  `PostSupplierPayment`).
- Evidence: `PurchasesController.cs`, `PurchaseReturnsController.cs`,
  `treasury-accounting-architecture.md` §5.4.

### 4.7 CommerceHub Runtime (tenant vendor module)
- Purpose: the tenant's *vendor* participation in the global marketplace — publish listings,
  receive orders, finance dashboard, earnings, debt repayment.
- Aggregate Roots (CommerceHub-owned): `CommerceHubListing`, `CommerceHubMarketplaceOrder`/
  `CommerceHubSubOrder`, `CommerceHubFinancialTransaction`, `CommerceHubPayout`,
  `CommerceHubVendorEarnings`, `CommissionRule`.
- Commands: publish/unpublish listing; fulfill order; finance control (owned by SuperAdmin, not POS
  Admin — POS Admin only *views* `/commercehub/finance`, `/earnings`, `/payments`).
- Queries: `CommerceHubOrdersController` (cap `commercehub.orders.view`), `AdminCommerceHubController`,
  `AdminCommerceHubFinanceController`.
- Events: marketplace events; tenant GL fed via `CommerceHubLedgerPosting` outbox.
- Persistence: PostgreSQL `CommerceHub*` tables; Mongo `CommerceHubProducts` projection; ES marketplace
  index.
- Authority: CommerceHub SQL; POS Admin is a *tenant-vendor view*, gated by capability
  `canAccessCommerceHubVendorNavigationCapability`.
- Evidence: `commercehub-final-architecture.md`; `CommerceHubController.cs`,
  `CommerceHubOrdersController.cs`, `AdminCommerceHubController.cs`,
  `AdminCommerceHubFinanceController.cs`, `Sidebar.jsx:199-206`.

### 4.8 Affiliate Platform (tenant operational)
- Purpose: the tenant's affiliate program — manage affiliates, categories, payouts, readiness.
- Aggregate Roots: `AffiliateAccount`, `AffiliateUser`, `AffiliateCategory`, `AffiliatePayout`.
- Commands: `AdminAffiliateController`, `AdminAffiliateCategoryController`,
  `AdminAffiliateReadinessController` (Owner/Admin only, `Sidebar.jsx:208-215`).
- Queries: affiliate analytics, payout evidence.
- Events: affiliate attribution (separate bounded context from tenant marketing attribution).
- Persistence: PostgreSQL `Affiliate*` + Mongo affiliate docs.
- Authority: Affiliate Platform owns; POS Admin is an operational management surface (not the
  affiliate portal storefront).
- Evidence: `affiliate-platform-final-architecture.md`; `AdminAffiliateController.cs`,
  `AdminAffiliateCategoryController.cs`.

### 4.9 Services Marketplace Runtime (internal POS-to-POS B2B)
- Purpose: internal POS-to-POS B2B service requests/chat/completion/ratings. **Not** CommerceHub,
  not Storefront, not Affiliate; not governed by CommerceHub Super Admin.
- Aggregate Roots: `ServiceRequest`, `ServiceRequestMessage`, `MarketplaceProfile`
  (provider/consumer participation).
- Commands: create/accept/complete service request; chat; rate.
- Queries: `/marketplace` (gated `requiresMarketplaceActor`, `Sidebar.jsx:186-193`,
  `isServiceMarketplaceWorkflowActor`).
- Persistence: PostgreSQL `ServiceRequests`/`Marketplace*`; Mongo projection.
- Authority: Services Marketplace owns; never owns payments/accounting/inventory/orders.
- Evidence: `service-marketplace-participation-runtime-final-architecture.md`; `MarketplaceController.cs`,
  `Sidebar.jsx:186-193`.

### 4.10 Storefront Builder / Publisher Runtime
- Purpose: write `StoreConfig`/`CmsPage`/navigation/footer/theme (draft→live); read-only storefront.
- Aggregate Roots: `StoreConfig` (one per tenant, draft/live theme), `CmsPage`, `StorefrontFooterSection`.
- Commands: `AdminStoreConfigController`, `AdminCmsPagesController`, `AdminStorefrontFooterController`.
- Events: `StoreConfig{Updated,Published,...}`, `StorefrontCmsPage{Published,Deleted}Event`,
  `NavigationPublishedEvent` → Mongo `storefront_configs`/`storefront_pages`/`storefront_navigation`
  + Redis invalidation, fail-closed.
- Persistence: PostgreSQL `StoreConfigs`/`CmsPages`/footer; Mongo projections; Redis caches.
- Authority: Builder is the only writer; the public Storefront never writes truth.
- Evidence: `storefront-builder-runtime-map.md` (via canonical-architecture-doc.md);
  `AdminStoreConfigController.cs`, `AdminCmsPagesController.cs`.

### 4.11 Promotion / Coupon / Smart Offer Runtime
- Purpose: tenant storefront offers. **Not** invoked by the POS sale path (POS prices are client
  inline). Surfaced in POS Admin under E-commerce because the tenant operator manages them.
- Aggregate Roots: `Promotion`, `Coupon`/`CouponUsage`, `SmartOffer`.
- Commands: `CouponsController`, `PromotionsController`, `AdminSmartOffersController`.
- Persistence: PostgreSQL + Mongo read models.
- Authority: CommerceHub/storefront-scoped; POS Admin is a management UI only.
- Evidence: `coupons`/`promotions` capability notes; `pos-runtime-reverse-engineering` (POS does
  not invoke promo/coupon).

### 4.12 Payment Config Runtime (tenant)
- Purpose: configure tenant payment methods; **not** capture/refund (that is `IPaymentLifecycleService`).
- Aggregate Roots: `TenantPaymentMethod` (encrypted `ConfigEncrypted`).
- Commands: `PaymentMethodsAdminController`, `AdminManualPaymentController`.
- Events: `PaymentMethod{Created,Updated,Enabled,Disabled,Deleted}Event` → Mongo projection.
- Persistence: PostgreSQL `TenantPaymentMethods`; Mongo projection.
- Authority: config-only in admin scope.
- Evidence: `pos-admin-write-runtime.md` (PAYMENT CONFIG section); `PaymentMethodsAdminController.cs`.

### 4.13 Shipping / Carrier Runtime
- Purpose: carrier config + fulfillment lifecycle. POS Admin surfaces shipping setup
  (`/admin/shipping/setup`, `AdminShippingAdministrationController`).
- Aggregate Roots: `ShippingMethod`, `CarrierConfiguration` (encrypted creds),
  `Shipment` (fulfillment subsystem).
- Events: `ShippingMethodUpdatedEvent`/`DeletedEvent` → Mongo storefront shipping projection.
- Persistence: PostgreSQL + Mongo projection.
- Evidence: `shipping-fulfillment-runtime-final-architecture.md`,
  `shipping-management-findings.md`; `AdminShippingAdministrationController.cs`,
  `ShippingMethodsController.cs`.

### 4.14 WhatsApp Account Config Runtime
- Purpose: configure WhatsApp Business accounts (QR connect, enable/disable) — **config only**;
  sending is a separate outbound-delivery runtime.
- Aggregate Roots: `WhatsAppAccount`, `WhatsAppMessageTemplate`.
- Commands: `Controllers/Admin/WhatsAppAdminController.cs` via `IWhatsAppAccountService`
  (Create/Update/Delete/GenerateQr/Connect/Enable/Disable).
- Persistence: PostgreSQL `WhatsAppAccounts` (per-channel toggles); the *sending* runtime owns its
  own delivery state.
- Authority: config owner; dispatch authority resides in the WhatsApp outbound runtime.
- Evidence: `Controllers/Admin/WhatsAppAdminController.cs`; `whatsapp-runtime-final-architecture.md`.

### 4.15 Storefront Identity / Online Customer Runtime
- Purpose: storefront customer accounts, auth providers, online-customer admin.
- Aggregate Roots: `OnlineCustomer` (separate realm from `CommerceHubCustomer`/`Customer`),
  `StorefrontAuthProvider`.
- Commands: `OnlineCustomersAdminController`, `StorefrontAuthProvidersAdminController`,
  `StorefrontOnlineCustomerController`.
- Persistence: PostgreSQL `OnlineCustomers`; `StorefrontJwt` realm.
- Evidence: `identity-access-management` reference; `OnlineCustomersAdminController.cs`.

### 4.16 Tracking & Attribution (Marketing Analytics) Runtime
- Purpose: server-side GA4 MP + Meta CAPI dispatch; UTM attribution; purchase emitted server-only.
- Aggregate Roots: `AnalyticsEvent` (ingest), `AnalyticsEventDocument` (Mongo/Redis),
  `CampaignCost`.
- Commands/Queries: `AdminAnalyticsController`, `AdminAnalyticsAttributionController`,
  `AdminCampaignCostController`, `AdminAnalyticsFunnelsController`.
- Events: `AuthoritativePurchaseAnalyticsConsumer` emits `PurchaseCompleted` from `OrderPaidEvent`
  (TikTok is a PHANTOM provider — enum only, no dispatcher).
- Persistence: Redis hot + Mongo cold; revenue dashboards read PostgreSQL `LedgerEntries`
  (never Mongo/Redis for money).
- Authority: analytics crown-jewel = SQL ledger; no consent runtime.
- Evidence: `tracking-attribution-runtime-map.md`; `AdminAnalyticsController.cs`.

### 4.17 Commerce AI Runtime
- Purpose: AI product generation / explanations.
- Aggregate Roots: `CommerceAi*`, prompt/template config.
- Commands: `AdminCommerceAiController`, `AdminAiProductGeneratorController`,
  `AdminAiExplanationController`.
- Persistence: PostgreSQL + Mongo (`commerce-ai` columns, `add_commerce_ai_columns.sql`).
- Evidence: `commerce-ai*` docs; `AdminCommerceAiController.cs`.

### 4.18 Tenant Config Runtime
- Purpose: tenant-scoped KV settings.
- Aggregate Roots: `SystemSetting` (optional `TenantId`), `Tenant`.
- Commands: `SettingsController` (requires `ITenantContext.RequiredTenantId` — tenant-scoped, NOT
  SuperAdmin).
- Persistence: PostgreSQL `SystemSettings`, `Tenants`.
- Evidence: `SettingsController.cs`; `superadmin-platform-final-architecture.md` (tenant settings
  are NOT SuperAdmin-owned).

### 4.19 Reporting Runtime (read-only)
- Purpose: operational reports over business tables; financial reports over the ledger.
- Aggregate Roots: none (read-only projections of SQL).
- Commands: none (GET only).
- Persistence: reads PostgreSQL directly.
- Evidence: `ReportsController.cs`, `SalesRepReportsController.cs`, `FinancialReportService`.

> Note: SuperAdmin Platform is reachable from the SAME SPA (`/super-admin/*`, `App.jsx:138-159`) but
> is a **different JWT realm** (`SuperAdminJwt`) and a different bounded context — it is NOT part of
> POS Admin's tenant workspace. It is listed here only to mark the boundary.

---

## 5. Workspace Composition Analysis (sidebar → runtime)

The sidebar (`Sidebar.jsx`) is the authoritative inventory of the workspace. Each top-level section
is **not** a runtime — it is a *navigation grouping* that fans out to one or more owning runtimes:

- **Dashboard / Reports / Finance Control Center** → read-only shells over Reporting + Treasury/
  Accounting runtimes (no ownership).
- **Database (Customers/Suppliers/Reps/Categories/Units/Warehouses/Banks/Products)** → a *mix*:
  CRM-party (customers/suppliers/reps), Catalog (categories/units/products), Inventory (warehouses),
  Treasury-config (banks). One section, four runtimes.
- **Accounts / Opening Balance / Treasury / Account Statement / Expenses** → Treasury & Accounting
  Runtime (+ POS Cashbox for cashbox opening balance). These are the financial spine.
- **Transactions (POS sales/returns/purchases/purchase-returns)** → POS Runtime + Procurement
  Runtime.
- **E-commerce** → a *composition of external runtimes the tenant also participates in*: CommerceHub
  Order view, Storefront Cart Recovery/Checkout, Shipping, Storefront Identity, Payment Config,
  Promotion/Coupon/Smart Offer, Commerce AI, Tracking & Attribution, WhatsApp Config. Each gated by
  a `commercehub.*` capability (`Sidebar.jsx:161-183`, `STORE_ADMIN_NAVIGATION_CAPABILITY_BY_PATH`
  `:9-19`).
- **Services Marketplace / CommerceHub / Affiliate Management / Storefront** → separate bounded
  contexts (Services Marketplace internal B2B; CommerceHub vendor module; Affiliate Platform;
  Storefront Builder) embedded as tabs because the same operator wears multiple hats.
- **Settings** → Tenant Config Runtime.

**Conclusion:** POS Admin is a **shared workspace / composition shell**. No sidebar section is a
runtime of its own. The shell's only "logic" is navigation, role/capability filtering
(`Sidebar.jsx:297-324`), and JWT carriage. Authority, persistence, and events all live in the
owning runtimes.

---

## 6. Screen Ownership Matrix (representative; full set in §3)

For every screen: Owning Runtime, Aggregate Root, Read Model, Write Model, Source of Truth,
Bounded Context, Cross-runtime deps, Synchronization, Events, Projection.

| Screen | Owning Runtime | Agg Root | Read Model | Write Model | SoT | X-runtime deps | Sync | Events | Projection |
|---|---|---|---|---|---|---|---|---|---|
| `/pos` (sales) | POS | `SalesInvoice` | SQL + receipt DTO | SQL tx | PG | Customer (optional), ProductVariant, Warehouse | outbox→GL | none emitted | none (SQL) |
| `/sales-returns` | POS | `Return` | SQL | SQL tx | PG | original `SalesInvoice` (lock) | outbox→GL | none | none |
| `/purchase-invoice` | Procurement(POS) | `PurchaseInvoice` | SQL | SQL tx | PG | Supplier, ProductVariant | outbox→GL | none | none |
| `/cashbox` (open) | POS+Treasury | `Cashbox` | SQL | SQL tx | PG | TreasuryAccount(1010) | ledger account ensure | none | none |
| `/opening-balance/cashbox` | Treasury+POS | `Cashbox`.`OpeningBalanceAmount` | SQL | SQL | PG | ledger | audit | none | none |
| `/opening-balance/customers` | Treasury+CRM | `AccountLedger`/`CustomerTransaction` | SQL | SQL+outbox | PG | GL control acct | outbox→GL | `AccountingOutboxMessage` | none |
| `/opening-balance/suppliers` | Treasury+CRM | `AccountLedger`/`SupplierTransaction` | SQL | SQL+outbox | PG | GL | outbox→GL | `AccountingOutboxMessage` | none |
| `/opening-balance/warehouse-stock` | Inventory | `ProductVariantStock` | SQL | `AdjustStockAsync` | PG | ProductVariant | stock-sync outbox | `StockSyncOutboxMessage` | inventory projection |
| `/products` | Catalog | `Product`/`ProductVariant` | Mongo+SQL-overlay | SQL | PG | Inventory (stock), Search | event→Mongo/ES | `Product*Event` | Mongo `products`, ES |
| `/categories` | Catalog | `Category` | SQL | SQL | PG | Product (FK) | — | category events | — |
| `/units` | Catalog | `Unit` | SQL | SQL | PG | ProductVariant | — | — | — |
| `/warehouses` | Inventory | `Warehouse` | SQL | SQL | PG | Cashbox (per-wh cashbox acct) | ledger acct ensure | — | — |
| `/banks` | Treasury | `BankAccount`(1030) | SQL | SQL | PG | TreasuryAccount | — | — | — |
| `/financial-accounts` | Treasury | `Account` (CoA) | SQL | SQL | PG | JournalEntry | — | — | — |
| `/treasury/*` (reports) | Treasury | `JournalEntry`/`LedgerEntry` | SQL (live) | — | PG | — | — | — | none (SQL) |
| `/inventory` | Inventory | `ProductVariantStock` | SQL | — | PG | — | — | — | none (SQL) |
| `/stock-transfers` | Inventory | `StockTransfer` | SQL | SQL | PG | Warehouse×2 | stock-sync | `StockSyncOutboxMessage` | inventory projection |
| `/expenses` | Treasury | `Expense` | SQL | SQL (approval) | PG | GL (outflow auth) | outbox→GL | `AccountingOutboxMessage` | none |
| `/statement-of-account` | Treasury+CRM | `AccountLedger` | SQL | — | PG | GL control acct | — | — | none |
| `/ecommerce/orders` | CommerceHub (vendor view) | `CommerceHubMarketplaceOrder` | SQL/Mongo | — (view) | PG/CH | Finance | — | CH events | Mongo `CommerceHubProducts` |
| `/ecommerce/whatsapp` | WhatsApp Config | `WhatsAppAccount` | SQL | SQL | PG | WhatsApp delivery runtime | — | — | — |
| `/ecommerce/coupons` | Promotion/Coupon | `Coupon` | SQL/Mongo | SQL | PG | Storefront | event | coupon events | Mongo |
| `/commercehub/finance` | CommerceHub (vendor view) | `CommerceHubFinancialTransaction` | SQL | — (SuperAdmin owns writes) | PG | GL (outbox) | outbox→GL | CH ledger posting | Mongo |
| `/admin/affiliates` | Affiliate | `AffiliateAccount` | SQL/Mongo | SQL | PG | Payout wallet | events | affiliate events | Mongo |
| `/marketplace` | Services Marketplace | `ServiceRequest` | SQL/Mongo | SQL | PG | — | events | SR events | Mongo |
| `/storefront` (builder) | Storefront Builder | `StoreConfig` | SQL | SQL tx | PG | ThemePreset (system) | event→Mongo/Redis | `StoreConfig*Event` | Mongo `storefront_configs`+Redis |
| `/analytics/attribution` | Tracking & Attribution | `AnalyticsEvent`/`CampaignCost` | Mongo/Redis/PG | SQL ingest | PG/Mongo | Order (purchase) | consumer | `PurchaseCompleted`(server) | Mongo/Redis |
| `/settings` | Tenant Config | `SystemSetting`/`Tenant` | SQL | SQL | PG | — | — | — | — |

---

## 7. Aggregate Discovery (true Aggregate Roots owned behind the shell)

Verified Aggregate Roots (each SQL-authoritative, each owned by exactly one runtime — the "true
aggregate root" test from the Shared Kernel analysis):

- `SalesInvoice` (POS) · `PurchaseInvoice` (Procurement/POS) · `Return` (POS) · `Cashbox` (POS)
- `ProductVariantStock` / `StockMovement` (Inventory) · `Warehouse` (Inventory)
- `Product` / `ProductVariant` (Catalog) · `Category` (Catalog) · `Unit` (Catalog)
- `Customer` / `Supplier` / `SalesRepresentative` (CRM-party)
- `Account` / `JournalEntry` / `LedgerEntry` / `TreasuryAccount` / `AccountLedger` /
  `CustomerTransaction` / `SupplierTransaction` / `Expense` (Treasury/Accounting)
- `CommerceHubListing` / `CommerceHubMarketplaceOrder` / `CommerceHubFinancialTransaction` /
  `CommerceHubPayout` (CommerceHub)
- `AffiliateAccount` / `AffiliateUser` / `AffiliateCategory` (Affiliate)
- `ServiceRequest` / `MarketplaceProfile` (Services Marketplace)
- `StoreConfig` / `CmsPage` / `StorefrontFooterSection` (Storefront Builder)
- `Promotion` / `Coupon` / `SmartOffer` (Promotion/Coupon)
- `TenantPaymentMethod` (Payment Config) · `ShippingMethod` / `CarrierConfiguration` (Shipping)
- `WhatsAppAccount` (WhatsApp Config) · `OnlineCustomer` (Storefront Identity)
- `AnalyticsEvent` / `CampaignCost` (Tracking & Attribution) · `SystemSetting`/`Tenant` (Tenant Config)

Aggregates that look like roots but are NOT (documentation-divergence findings):
- `TreasuryTransaction` / `VisaTransaction` / `CashboxTransaction` (legacy) — write-blocked; the
  *real* cash truth is the ledger (`treasury-accounting-architecture.md` §2.5). `CashboxTransaction`
  is still written by POS for operational history but is NOT the accounting authority.
- `SalesInvoicePayment` — entity exists but is **unused** on the POS sale path (payments recorded
  via `CustomerTransaction` ledger + accounting journal; `pos-runtime-final-architecture.md`).
- `Tax`, `NotificationConfig`, `Localization` — **not entities**; realized as `SystemSetting` rows,
  encrypted config columns, and per-field `Ar` columns (`pos-admin-write-runtime.md` pitfalls).

---

## 8. Runtime Dependency Graph

Independence vs dependence (verified):

- **POS Runtime** depends on: Inventory (stock lock), Treasury/Accounting (outbox→GL), CRM-party
  (optional customer), Catalog (product/variant read). It does NOT depend on CommerceHub/Affiliate/
  Storefront/Search at write time.
- **Treasury/Accounting** is the hub: nearly every other runtime (POS, Procurement, CRM-party
  opening balances, Expenses, CommerceHub finance, Affiliate payout, Storefront payments) feeds it
  via the outbox. It depends on nothing write-side except its own `FinancialPostingService`.
- **Inventory** depends on Catalog (variant) and POS/Storefront (callers). Independent read.
- **Catalog** depends on Inventory (stock) and Search (projection). Independent write.
- **CommerceHub** depends on Treasury/Accounting (outbox→GL), Catalog (listing source), Search
  (marketplace index). POS Admin is a *view* into it.
- **Affiliate / Services Marketplace / Storefront** are independent bounded contexts; POS Admin is a
  management surface that calls their tenant-scoped APIs.
- **Tracking & Attribution** depends on Orders/Payments (server-only purchase) and reads the ledger
  for revenue; independent write of its own events.

Dependency direction (who can operate alone):
- Treasury/Accounting, Catalog, Inventory, CRM-party, POS, Procurement = **tenant-core, can operate
  a retail business without CommerceHub/Affiliate/Marketplace**.
- CommerceHub, Affiliate, Services Marketplace, Storefront, Tracking & Attribution = **participation
  runtimes**; the tenant can run POS+Inventory+Accounting with zero of them, but they extend reach.

Text graph (→ means "depends on / feeds"):

```
POS ─────────────┐
Procurement(POS)─┤
CRM-party(OB)────┤
Expenses ────────┼─▶ Treasury/Accounting (GL hub) ◀─ CommerceHub ─┐
Inventory ───────┤                            ▲                   │
Catalog ─────────┘                            │                   │
                                             │                   │
Storefront ──▶ Catalog/Inventory/Search       └──▶ Affiliate (payout▶GL)
Services Marketplace (independent)            Tracking&Attribution ─▶ Orders/Ledger
CommerceHub (vendor view from POS Admin)
```

---

## 9. Event Architecture

POS Admin initiates commands; the **owning runtimes** publish events. The shell itself publishes
nothing.

- **Generic outbox backbone** (`OutboxPublisherJob`, every 5s, Hangfire; `OutboxMessage.cs`,
  `EventVersion`, `AggregateId`, `MaxRetries=5`): critical events ordered by
  `(TenantId,AggregateId,EventVersion)`; fail-closed operator reconciliation on critical poison
  (`RequiresOperatorReconciliation`); 7-day retention.
- **Accounting outbox** (`AccountingOutboxMessage` + `AccountingOutboxProcessor`): POS/Procurement/
  CRM-party/Expense/CommerceHub post balanced journals to the GL. Stale `AccountingMutationVersion`
  skipped; critical escalates.
- **Catalog projection events**: `ProductCreated/Updated/DeletedEvent` → `ProductCreatedEventConsumer`
  (Mongo upsert idempotent by `(TenantId,ProductId)`) + `ProductSqlAuthorityProjectionWriter`
  (tombstone on fail-closed visibility).
- **Storefront config/CMS/nav/footer events**: `StoreConfig*Event`, `StorefrontCmsPage*Event`,
  `NavigationPublishedEvent`, `StorefrontFooterUpdatedEvent` → Mongo + Redis invalidation, all
  guarded by `TenantLineageAuthority.IsActiveTenantAsync` (fail-closed).
- **Shipping events**: `ShippingMethodUpdated/DeletedEvent` → Mongo storefront shipping projection.
- **Payment config events**: `PaymentMethod*Event` → Mongo.
- **Stock-sync events**: `StockSyncOutboxMessage` → inventory/search projection.
- **POS sale/return/purchase**: **emit NO domain events** from the mutation path (verified gap) —
  POS analytics read SQL directly, not via Mongo projection.
- **CommerceHub ledger posting**: `CommerceHubLedgerPosting` bridges CH operational finance → tenant GL.
- **Analytics purchase**: `AuthoritativePurchaseAnalyticsConsumer` emits `PurchaseCompleted` from
  `OrderPaidEvent` (server-only; client purchase rejected).

Idempotency: unique constraint + financial fingerprint (rejects key-collision with divergent
fingerprint; allows exact replay). Replay: outbox critical-ordering prevents stale versions
overtaking newer state. Repair-on-read: SQL-authority overlay re-fetches truth on not-found/stale;
Mongo docs carry monotonic `ProjectionMutationVersion` + checksum; stale events rejected by
optimistic `ReplaceOneAsync` filter.

---

## 10. Persistence Architecture (who owns what)

- **PostgreSQL (single `ApplicationDbContext`)** — authoritative write model for EVERY domain
  behind POS Admin. Tenant isolation is DB-enforced (`ITenantOwned` + `TenantSaveChangesInterceptor`
  blocks mismatched `TenantId`; `X-Tenant-Id` stripped by `TenantResolutionMiddleware`).
- **MongoDB** — derivative read projections: `products`, `CommerceHubProducts`, `storefront_configs`,
  `storefront_pages`, `storefront_navigation`, `storefront_footer`, `orders` (CommerceHub), affiliate
  docs, services-marketplace docs, analytics cold store, commerce-ai docs. All hydrated by consumers
  that RE-FETCH SQL (never trust event payload).
- **Redis** — caches: navigation/footer/CMS invalidation, analytics hot store, rate-limit counters,
  refresh-token/jti blacklists, SuperAdmin login limiter.
- **Elasticsearch** — single authority for product/marketplace SEARCH (no Mongo fallback → 503 on
  failure); SQL-authority overlay re-derives visibility before presentation.
- **S3 / Cloudflare R2** — media bytes (`R2StorageService`); SQL holds `ProductMedia` metadata.
- **Projections** — owned by the consuming runtime's projector (e.g. catalog projector owns
  `products`; storefront projector owns `storefront_configs`). POS cash/ledger data has NO projection
  (SQL-only).

---

## 11. Authority Matrix (per business concept)

| Concept | Authoritative owner | POS Admin role |
|---|---|---|
| Customer / Supplier / Sales Rep | CRM-party Runtime | register + opening balance UI |
| Product / Variant / Category / Unit | Catalog Runtime | authoring UI |
| Inventory / Warehouse / Stock | Inventory Runtime | ops UI |
| Sales / Purchase / Return / Cashbox | POS Runtime | execution UI |
| Ledger / Journal / Account / Treasury / Expense | Treasury/Accounting Runtime | read + configure UI |
| Opening Balances (cashbox/customer/supplier/stock) | Treasury/Accounting + owning runtime | bootstrap UI |
| CommerceHub Listing / Order / Finance | CommerceHub Runtime (finance writes = SuperAdmin) | vendor view UI |
| Affiliate Account / Payout | Affiliate Platform | management UI |
| Service Request / Marketplace Profile | Services Marketplace Runtime | provider/consumer UI |
| Storefront Config / CMS / Footer / Theme | Storefront Builder Runtime | publish UI |
| Promotion / Coupon / Smart Offer | Promotion/Coupon Runtime | management UI |
| Payment Method (config) | Payment Config Runtime | config UI (capture = Payment Lifecycle) |
| Shipping Method / Carrier | Shipping Runtime | config UI |
| WhatsApp Account (config) | WhatsApp Config Runtime | config UI (send = delivery runtime) |
| Online Customer / Auth Provider | Storefront Identity Runtime | admin UI |
| Analytics / Attribution / Campaign Cost | Tracking & Attribution Runtime | dashboard UI |
| Commerce AI | Commerce AI Runtime | generator UI |
| Tenant Settings | Tenant Config Runtime | settings UI |
| Tax / Notification config / Localization | NOT entities (SystemSetting / config cols / Ar cols) | UI only |

Cross-runtime authority rule: a tenant is the isolation boundary AND the governed "vendor"; SQL is
authority, JWT claims are evidence (fail-closed re-validation per realm). SuperAdmin owns platform
authority over domain SQL but does NOT own domain data; POS Admin owns none.

---

## 12. Cross-Runtime Interaction

How runtimes communicate (verified, no assumption):

- **Direct SQL (same DbContext):** POS → Inventory (`ProductVariantStockMutationSupport` locks
  `ProductVariantStocks` in the same tx), POS → CRM-party (optional `CustomerId` read),
  Treasury → everything (outbox posts to `JournalEntries` in the same ambient tx).
- **Repository / Application Service:** consumers re-fetch via repositories (`StorefrontConfigProjectionConsumer`
  uses `TenantLineageAuthority.LoadCurrentStoreConfigProjectionAuthorityAsync`).
- **Domain Service:** `FinancialPostingService` (GL), `TreasuryOperationService`, `LedgerService`
  (party balances), `InventoryConsistencyService` (reservation).
- **Event (MassTransit outbox):** catalog/storefront/shipping/payment/config/stock-sync events.
- **Projection (Mongo/ES/Redis):** read models hydrated by consumers.
- **Integration Service:** POS Admin pages call the owning controller's REST API over HTTPS with the
  operator JWT; CommerceHub/Affiliate/Services-Marketplace tenant views call their own tenant-scoped
  controllers in-process (shared `ApplicationDbContext`), not via HTTP/gRPC.
- **NOT used:** POS Admin does not call CommerceHub via HTTP for its own tenant data — the vendor
  views are in-process controllers. Cross-tenant calls are forbidden (tenant interceptor).

---

## 13. Business Lifecycle Analysis (one merchant, runtime ownership per step)

Merchant Created → `TenantRegistrationService` (Tenant runtime; `PendingSetup`).
↓ Identity → POS operator `User` (AuthController); optional `OnlineCustomer`, `CommerceHubCustomer`,
  `AffiliateUser` (separate realms).
↓ Configuration → `SettingsController` (Tenant Config); `SystemSetting` KV.
↓ Categories/Units → Catalog Runtime (`CategoriesController`,`UnitsController`).
↓ Products → Catalog Runtime (`ProductsController`); publishes `ProductCreatedEvent` → Mongo/ES.
↓ Warehouses → Inventory Runtime (`WarehousesController`); per-warehouse cashbox ledger account ensured.
↓ Opening Balances → Treasury/Accounting + owning runtime: cashbox (`CashboxController.SetOpeningBalance`),
  customer/supplier (`CustomersController`/`SuppliersController` `/opening-balances` → `LedgerService`
  → `AccountLedger` + outbox→GL), warehouse stock (`WarehousesController.GetOpeningStock` + `AdjustStockAsync`
  → `StockSyncOutboxMessage`).
↓ Customers/Suppliers → CRM-party Runtime (registration + balances).
↓ Purchasing → Procurement Runtime (`PurchasesController`); stock +→; outbox→GL (Payable).
↓ Inventory → Inventory Runtime (receipt increments `ProductVariantStock`; movements audit).
↓ Sales → POS Runtime (`SalesController`); stock −; ledger (receivable if credit); outbox→GL (Sale).
↓ Payments → if storefront/CommerceHub: `IPaymentLifecycleService`; POS: inline validation + ledger.
↓ Accounting → Treasury/Accounting (`FinancialPostingService.PostJournalAsync`) balanced journals.
↓ CommerceHub Projection → CommerceHub Runtime publishes listing; Mongo `CommerceHubProducts` + ES.
↓ Affiliate Projection → Affiliate Platform; Mongo affiliate docs; attribution separate context.
↓ Analytics → Tracking & Attribution consumes `OrderPaidEvent` → `PurchaseCompleted` (server-only).
↓ Recovery → outbox worker (`OutboxPublisherJob`, 5s), accounting outbox processor, auto-healing queue,
  manual SuperAdmin reindex/replay.

Failure/recovery per step: every financial posting is idempotent (key + version); POS mutations roll
back whole tx on any exception; unresolved captured payment blocks outward money (fail-closed);
critical poison → `RequiresOperatorReconciliation`.

---

## 14. Opening Balance Architecture

**Is Opening Balance a wizard? A runtime? A bootstrap?** Implementation says: **it is NOT a single
runtime and NOT a wizard microservice.** It is a **set of bootstrap WRITE screens in the POS Admin
shell that delegate to the owning runtimes' existing aggregates**, each posting to the same SQL
authority + the same accounting outbox. There is no `OpeningBalance` aggregate, no opening-balance
schema, no opening-balance event bus.

Four opening-balance screens, each mapped:

1. **Cashbox Opening Balance** (`/opening-balance/cashbox`) — owner **POS Cashbox + Treasury**.
   `CashboxController.SetOpeningBalance` (`:1212`) sets `Cashbox.OpeningBalanceAmount`/Date and, if
   `>0`, cash-ins to treasury (ledger account `1010-*`). Persistence: `Cashboxes` + ledger. Events:
   none (audit only). Authority: cashbox ledger account. Recovery: re-open idempotent per cashbox.
2. **Customer Opening Balance** (`/opening-balance/customers`) — owner **Treasury/Accounting + CRM-party**.
   `CustomersController` `/opening-balances` → `LedgerService.ApplyCustomerTransaction` (`:216-356`)
   writes `AccountLedger` (type `OpeningBalance`) + `CustomerTransaction`; enqueues
   `AccountingOutboxMessage` (`EnqueuePartyLedgerAdjustmentAsync`) → GL (`PostPartyLedgerAdjustmentAsync`
   debits/credits AR). Idempotency: `IX_AccountLedgers_CustomerOpeningBalance_Once` blocks duplicate
   active rows. Persistence: `AccountLedgers`+`CustomerTransactions`+`AccountingOutboxMessages`.
3. **Supplier Opening Balance** (`/opening-balance/suppliers`) — owner **Treasury/Accounting + CRM-party**
   (payable mirror of #2). Same mechanism, `SupplierPayables` control account.
4. **Warehouse Opening Stock** (`/opening-balance/warehouse-stock`) — owner **Inventory Runtime**.
   `WarehousesController.GetOpeningStock` (`:423`) lists variants + opening status; `AdjustStockAsync`
   seeds `ProductVariantStock` with a `StockMovement` (Opening type) and publishes
   `StockSyncOutboxMessage`. Persistence: `ProductVariantStocks`+`StockMovements`+stock-sync outbox.
   Authority: inventory SQL. Recovery: stock-sync replay.

**Conclusion:** Opening Balance = **bootstrap write surface** (hybrid of Treasury/Accounting +
Inventory + POS Cashbox), NOT a runtime. It reuses each runtime's aggregate + the shared outbox; it
introduces zero new authority. Each screen fails-closed (duplicate-opening-balance guard, ledger
recompute, stock lock).

---

## 15. Shared Kernel Analysis

- **Shared (same row, multiple readers):** `Tenant` (isolation boundary for all); `Product`/
  `ProductVariant` (Catalog writes, Inventory/POS/CommerceHub/Storefront read); `Customer` (CRM-party
  writes, POS reads optional, Treasury reads for SOA); `Account`/`JournalEntry` (Treasury writes, all
  reports read); `StoreConfig` (Storefront Builder writes, Storefront reads).
- **Duplicated (distinct realms, NOT shared):** `Customer` (tenant POS party) vs `OnlineCustomer`
  (storefront realm) vs `CommerceHubCustomer` (marketplace realm) — three separate aggregates, linked
  at runtime by `CanonicalIdentityReconciliationHostedService` (repair, not one table).
- **Projections (read models, fail-closed):** `products`, `CommerceHubProducts`, `storefront_configs`,
  `storefront_pages`, `storefront_navigation`, `storefront_footer`, `orders`, affiliate docs,
  services-marketplace docs, analytics docs — all Mongo, all SQL-rehydrated.
- **Integration models:** `AccountingOutboxMessage`, `OutboxMessage`, `StockSyncOutboxMessage` — the
  integration glue between write runtimes and projections/GL.
- **True Aggregate Roots:** the §7 list — each owned by exactly one runtime, each SQL-authoritative.

---

## 16. Security Architecture

- **Tenant isolation:** `ITenantContext.RequiredTenantId` (throws if unset); `X-Tenant-Id` stripped,
  re-resolved from `Host`/claim; `TenantSaveChangesInterceptor` blocks mismatched `TenantId`;
  `IsActive && Status==Active && DeletedAtUtc==null` required. Cross-tenant leak = CRITICAL.
- **Permission boundaries:** NO stored permission system. Enforcement = scheme + role claim +
  SQL authority + governance gates. Capability strings (e.g. `commercehub.orders.view`) are UI hints
  only, computed at login; server re-checks (`Sidebar.jsx:307` `canAccessCommerceHubVendorNavigationCapability`).
- **Role boundaries (POS operator realm):** Owner/Admin/Manager/Cashier (+ Accountant/SuperAdmin for
  Finance Control Center). Each endpoint lists roles explicitly (`[Authorize(Roles=...)]`). No role
  hierarchy engine. Ghost `Accountant` role literal appears in some metadata (drift, not model).
- **Ownership boundaries:** a tenant is the isolation boundary AND the governed "vendor". POS Admin
  operators act within their tenant; they cannot cross tenants.
- **Super Admin boundaries:** SuperAdmin is a SEPARATE realm (`SuperAdminJwt`, `Program.cs:2356`),
  cryptographically isolated, even though its pages share the SPA. SuperAdmin writes domain SQL
  authority under governance but owns no domain data.
- **Runtime boundaries:** each runtime validates its own aggregate + tenant scope; the GL enforces
  inflow/outflow authority so no runtime can credit cash arbitrarily; legacy treasury tables are
  DB-trigger write-blocked.

---

## 17. Runtime Timeline (one complete merchant lifecycle)

See §13 (Business Lifecycle Analysis). Per step: Runtime, Owner, Authority, Persistence,
Synchronization, Consistency, Events, Failure, Recovery are enumerated there. Summary: every step is
a single runtime owning its SQL aggregate; financial steps converge on the Treasury/Accounting GL via
the outbox; recovery is idempotent by key+version; projection sync is fail-closed SQL-rehydration.

---

## 18. Final Runtime Classification (POS Admin as composition shell)

POS Admin
├── POS Runtime (sales/returns/purchases/purchase-returns/cashbox) — retail execution
├── Procurement Runtime (POS purchases) — payable-side
├── Inventory Runtime (stock/warehouse/transfers/opening-stock)
├── Catalog Runtime (product/variant/category/unit)
├── CRM-Party Runtime (customer/supplier/sales-rep + balances)
├── Treasury & Accounting Runtime (GL/treasury/expense/opening-balances/reports)
├── CommerceHub Runtime (tenant vendor module: listings/orders/finance view)
├── Affiliate Platform (tenant operational management)
├── Services Marketplace Runtime (internal POS-to-POS B2B)
├── Storefront Builder / Publisher Runtime (config/CMS/footer/theme)
├── Promotion / Coupon / Smart Offer Runtime (tenant offers)
├── Payment Config Runtime (tenant methods; capture = Payment Lifecycle, separate)
├── Shipping / Carrier Runtime (config + fulfillment)
├── WhatsApp Account Config Runtime (config; send = outbound delivery, separate)
├── Storefront Identity Runtime (online customers / auth providers)
├── Tracking & Attribution (Marketing Analytics) Runtime
├── Commerce AI Runtime
├── Tenant Config Runtime (SystemSetting KV)
├── Reporting Runtime (read-only SQL)
└── (SuperAdmin Platform — separate JWT realm, shared SPA, NOT part of POS Admin)

POS Admin itself owns: **navigation, role/capability filtering, JWT carriage, and command routing** —
and **zero aggregates, zero persistence, zero authority**.

---

## 19. Final Architecture Answer

**"How does Shopixy organize an enterprise-grade POS Admin that unifies ERP, Commerce, Inventory,
Accounting, Marketplace, Affiliate, Storefront, AI, Search, Analytics, Treasury, and every other
business capability into a single business workspace while preserving bounded contexts, runtime
ownership, authority boundaries, transactional consistency, scalability, and long-term
maintainability?"**

Implementation-backed answer:

1. **One workspace, many runtimes (composition shell, not a monolith).** POS Admin is a single React
   SPA (`PointOfSale.Web`) that presents one role/capability-filtered navigation tree
   (`Sidebar.jsx`) and routes each screen to the backend runtime that owns its data. It unifies the
   capabilities by *exposing* them, not by *absorbing* them. This preserves bounded contexts: the
   POS sale logic stays in `SalesController`, the GL stays in `FinancialPostingService`, the catalog
   stays in `ProductsController`, etc. — POS Admin adds no competing logic.
2. **One SQL authority, derivative projections.** Every runtime writes PostgreSQL as its single
   system of record (`ApplicationDbContext`). Mongo/Redis/Elasticsearch are projections hydrated by
   MassTransit consumers that RE-FETCH SQL (never trust the event payload), fail-closed on unproven
   authority (`treasury-accounting-architecture.md`, `pos-admin-write-runtime.md`). This gives
   scalability (read scale-out via projections) without sacrificing the source of truth.
3. **Authority boundaries enforced in code, not in the shell.** Tenant isolation is DB-enforced
   (`ITenantOwned` + `TenantSaveChangesInterceptor` + stripped `X-Tenant-Id`); money authority is
   enforced by the GL's inflow/outflow validators + DB triggers; claims are evidence, SQL decides
   (fail-closed re-validation per realm). POS Admin cannot bypass any of this because it owns no
   write path of its own.
4. **Transactional consistency via the outbox.** All cross-runtime effects (GL postings, projections,
   marketplace listings) flow through the generic outbox (`OutboxPublisherJob`, critical-ordered,
   idempotent by key+version, fail-closed operator reconciliation) and the accounting outbox. This
   keeps each runtime's write atomic while still synchronizing the others — consistency without
   distributed transactions.
5. **Capability-based composition for participation runtimes.** CommerceHub/Affiliate/Services
   Marketplace/Storefront/Tracking surfaces appear in the same shell because the operator is also a
   vendor/affiliate/provider; visibility is gated by computed `commercehub.*` capabilities
   (`Sidebar.jsx:307`), so the shell composes without coupling the runtimes.
6. **Maintainability through single-owner aggregates.** Each Aggregate Root is owned by exactly one
   runtime (§7); there are no shared-write "god" tables except the tenant boundary and the GL (which
   is intentionally the convergence point). New capabilities (AI, Smart Offers, Services Marketplace)
   are added as new runtimes + new controllers + new projections, not by bloating POS Admin.

Net: Shopixy achieves the unification by making POS Admin a **thin, authority-less composition shell**
over a **federated set of SQL-authoritative runtimes** connected by an **idempotent outbox + fail-closed
projections**. The workspace feels like one ERP; the implementation is a bounded-context federation.

---

## 20. Documentation Validation

The brief made several assumptions. Each is checked against implementation:

| Brief assumption | Implementation verdict | Evidence |
|---|---|---|
| "POS Admin is an ERP / Commerce Backoffice / Administration Portal / Runtime" | **Composition shell / Business Workspace** over ~18 runtimes. It is NOT a runtime, has no aggregates/persistence of its own. | `Sidebar.jsx`; controllers each own their SQL; no `PosAdmin` entity/DbContext/outbox. |
| "Discover runtimes: Identity, Customer, Supplier, Sales Rep, Catalog, Category, Units, Inventory, Warehouse, Procurement, Purchasing, Sales, Returns, Accounting, Ledger, CoA, Treasury, Cashbox, Banks, Taxes, Pricing, Product, CommerceHub, Affiliate, Marketplace, Service Marketplace, Storefront, CMS, Media, Search, Notifications, Analytics, Recovery, Payments, Shipping, AI, Facebook, SEO" | Mostly correct **as runtimes behind the shell**, but: (a) Taxes/Pricing/Notifications/Localization/Media/SEO are **NOT first-class runtimes** — they are `SystemSetting` rows / config columns / per-field `Ar` columns / `ProductMedia`+R2 / SEO controller, not aggregates; (b) "Facebook" is not a runtime (WhatsApp is the only outbound channel config; Meta CAPI is an analytics dispatcher); (c) Identity is **five separate realms** (POS User, OnlineCustomer, CommerceHubCustomer, AffiliateUser, SuperAdminUser), not one. | `pos-admin-write-runtime.md` pitfalls; `identity-access-management` reference; `tracking-attribution-runtime-map.md` (TikTok phantom, no Facebook runtime). |
| "Opening Balance is a Wizard / Bootstrap Runtime / Accounting Runtime / Inventory Runtime / Hybrid" | **Hybrid bootstrap write surface**, NOT a runtime/wizard. Four screens delegate to Treasury/Accounting + Inventory + POS Cashbox existing aggregates + shared outbox. | `CashboxController.SetOpeningBalance:1212`; `LedgerService.cs:216-356`; `WarehousesController.GetOpeningStock:423`+`AdjustStockAsync`; `IX_AccountLedgers_CustomerOpeningBalance_Once`. |
| "POS Admin owns persistence / authority" | **False.** POS Admin owns no persistence/authority. Every write is a backend runtime's SQL aggregate. | All controllers own their tables; POS Admin is `PointOfSale.Web` only; no backend `PosAdmin` project. |
| "Payments / Treasury / Accounting are separate screens owned by POS Admin" | Screens exist in the shell, but **authority** is the Treasury/Accounting Runtime (`FinancialPostingService`); payment *capture/refund* is `IPaymentLifecycleService` (not in POS Admin scope). POS Admin only *configures* payment methods. | `treasury-accounting-architecture.md`; `pos-admin-write-runtime.md` PAYMENT CONFIG. |
| "CommerceHub / Affiliate / Marketplace / Storefront are 'sections' of POS Admin" | They are **separate bounded contexts** reachable from the shell because the operator is also a vendor/affiliate/provider; gated by capabilities; CommerceHub finance *writes* are owned by SuperAdmin, not POS Admin. | `Sidebar.jsx:199-230`; `commercehub-final-architecture.md`; `superadmin-platform-final-architecture.md`. |
| "SEO / Facebook / Notifications as runtimes" | SEO = `SeoController` (tenant config); Facebook = absent (Meta CAPI is analytics only); Notifications = config rows + separate sending runtime, no `NotificationConfig` entity. | `pos-admin-write-runtime.md` NOTIFICATION CONFIG / LOCALIZATION pitfalls; `tracking-attribution-runtime-map.md`. |

Every assumption that implied POS Admin *owns* logic/persistence/authority was **incorrect**; the
implementation shows POS Admin is a **composition shell**. Assumptions about the *existence* of the
underlying runtimes (POS, Inventory, Catalog, Treasury/Accounting, CommerceHub, Affiliate, Services
Marketplace, Storefront, Tracking/Attribution, Shipping, AI) were **correct** and are documented
above with file:line evidence. Assumptions about Taxes/Pricing/Notifications/Localization/Facebook/SEO
as runtimes were **incorrect** — they are configuration surfaces or absent, not aggregates.

---

*End of document. Every claim above is implementation-backed; citations use the canonical
`path:line` convention and were verified against the backend and `PointOfSale.Web` source. Where a
prior doc was cited, it was itself implementation-verified (the sibling `*-final-architecture.md`
platform docs). No assumption was preserved where implementation contradicted it.*
