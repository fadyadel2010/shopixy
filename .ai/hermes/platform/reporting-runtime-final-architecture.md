111# Reporting Runtime — FINAL CANONICAL ARCHITECTURE

> Status: IMPLEMENTATION-BACKED. Every claim in this document is cited to `file:line`
> in the Shopixy / PointOfSale backend (C# / .NET, EF Core over PostgreSQL, with one
> reporting surface that also reads MongoDB). Documentation, comments, and prior
> certification markdown are explicitly treated as SECONDARY and have been re-verified
> against code. Where code evidence was unavailable at authoring time, the gap is stated.

---

## Executive Summary

The Reporting Runtime is the **Decision Intelligence Runtime** of Shopixy. It is not a
collection of reports; it is a single bounded context that turns the platform's
authoritative SQL ledgers and business tables into the numbers decision-makers act on:
revenue, profit, balances, valuation, aging, trends, and executive KPIs.

Two architectural facts dominate the entire runtime and were verified against code:

1. **Financial truth is ledger-sourced and enforced.** The core financial reporting
   service (`FinancialReportService`) implements `ILedgerOnlyService`, a marker interface
   whose implementations are blocked by CI/CD architecture tests from touching any mutable
   business table (`SalesInvoices`, `Customers`, `Products`, etc.). Financial reports are
   computed from `LedgerEntries` joined to `JournalEntries` and `Accounts`
   (`PointOfSale.Infrastructure/Accounting/FinancialReportService.cs:24`,
   `PointOfSale.Core/Interfaces/Accounting/ILedgerOnlyService.cs:23`,
   `PointOfSale.Tests/Architecture/LedgerIsolationTests.cs:29`).

2. **Reporting is a read-only SQL-authority runtime that does NOT use Mongo/Redis/ES as a
   source of truth.** The merchant reporting controllers (`ReportsController`,
   `DashboardController`, `FinancialReportsController`, `StatementOfAccountController`,
   `SalesController`) contain zero references to Mongo, Redis, or Elasticsearch
   (verified by grep: 0 hits each). The only caching primitive is `IMemoryCache`, and in
   `DashboardController`/`FinancialReportsController` the cache wrapper is a no-op that
   recomputes from SQL on every request (`DashboardController.cs:600-608`,
   `FinancialReportsController.cs:279-287`).

There is exactly **one** reporting surface that reads MongoDB: the **Storefront Analytics
Dashboard**, and only for behavioral/funnel events (`PageView`, `ProductView`,
`AddToCart`, `BeginCheckout`). Even there, all money metrics are resolved back to the
PostgreSQL ledger via `StorefrontAnalyticsAuthorityReader` (which reads
`AccountCodes.EcommerceSales`), explicitly prohibited from being replaced by Mongo/Redis
projections (`StorefrontAnalyticsDashboardService.cs:26`,
`StorefrontAnalyticsAuthorityReader.cs:31-43`).

The runtime spans six SQL-authoritative read models: the **GL (LedgerEntries)**, the
**party sub-ledger (AccountLedgers)**, **business invoice tables (SalesInvoices /
PurchaseInvoices + items/payments)**, **variant stock (ProductVariantStocks)**,
**stock movements (StockMovements)**, and **storefront commerce (Orders / OrderItems /
CheckoutSessions)** plus the **Mongo analytics events** collection. Each report chooses
its source deliberately; the choice is documented per-report below and enforced where it
is financial.

---

## Architecture Vision

The Reporting Runtime exists to answer one question for every stakeholder — merchant,
customer, supplier, affiliate, CommerceHub vendor, and platform operator — **"what are the
numbers, and can I trust them?"** — from a single source of truth, consistently, without
ever mutating it.

Its vision is realized through four principles (each code-cited in Architecture Principles):

- **One money truth: the immutable ledger.** Financial statements never recompute totals
  from business tables; they aggregate `LedgerEntries` (`FinancialReportService.cs:56`).
- **Operational truth: the system-of-record business tables.** Inventory levels, top
  products, payment-method mix, and AR/AP aging read the operational tables because those
  are the authoritative operational state (`DashboardController.cs:122-176`,
  `ReportsController.cs:654-688`).
- **Two ledgers, not one.** The GL (`LedgerEntries`, immutable, journal-derived) and the
  party sub-ledger (`AccountLedgers`, mutable, carries a precomputed `RunningBalance`) are
  distinct sources with distinct consistency guarantees
  (`PointOfSale.Core/Entities/AccountLedger.cs:55`,
  `FinancialReportService.cs:56`).
- **Read-only by construction.** Reporting services reject writes; the only mutations in
  this context are opening-balance and adjustment *postings* in `StatementOfAccountController`,
  which delegate to `ILedgerService` / the accounting outbox, never to ad-hoc DML
  (`StatementOfAccountController.cs:37,264,535`).

---

## Reporting Runtime Purpose

To provide cross-domain, tenant-scoped, read-only aggregation of the platform's SQL
authority (and, for one surface, behavioral Mongo events) into:

- Business reporting (sales, purchases, inventory, customers, suppliers)
- Financial reporting (trial balance, GL, balance sheet, P&L, cash flow, aging, clearing)
- Analytical reporting (trends, top-N, category/payment breakdowns, funnels)
- Executive decision support (KPI dashboard, daily close, statements)

The runtime is the consumer side of the platform's CQRS split: it reads the write-model
SQL authority (and one Mongo read-model) but produces no write-model mutations of its own
except the explicitly-scoped party-balance postings.

---

## Reporting Runtime Scope

In scope (read surfaces implemented and verified):

- `FinancialReportsController` + `FinancialReportService` — full financial statement set
  (`PointOfSale.API/Controllers/FinancialReportsController.cs`,
  `PointOfSale.Infrastructure/Accounting/FinancialReportService.cs`).
- `ReportsController` — sales analysis/summary/by-category/by-payment-method/returns,
  inventory valuation, product movement, daily close, AR aging, AP aging, purchase
  register, purchases-by-supplier, purchase returns, cost analysis
  (`PointOfSale.API/Controllers/ReportsController.cs`, 2332 lines).
- `DashboardController` — executive KPI dashboard (stats, sales-trend, top-products,
  recent-sales, low-stock, category-sales, payment-methods)
  (`PointOfSale.API/Controllers/DashboardController.cs`, 762 lines).
- `StatementOfAccountController` — party statements, balances, outstanding
  (`PointOfSale.API/Controllers/StatementOfAccountController.cs`, 1157 lines).
- `SalesController` — sales operational reporting (`PointOfSale.API/Controllers/SalesController.cs`).
- `StorefrontAnalyticsDashboardService` + `AdminStorefrontAnalyticsDashboardController` —
  storefront/merchant behavioral + commerce analytics
  (`PointOfSale.API/Services/Analytics/StorefrontAnalyticsDashboardService.cs`).
- CommerceHub financial dashboard (`CommerceHubFinancialDashboardService`) and Affiliate
  reporting (`AffiliateAnalyticsController` et al.) — covered by delegated investigation.

Out of scope (deferred to owning runtimes — see "Does NOT Own"):

- Any write to the GL, journals, or party ledger outside the two explicitly-scoped
  posting endpoints.
- Product/inventory *mutation*, payment lifecycle, order lifecycle, accounting posting
  engine, shipping, attribution — these are upstream producers, not reporting.

---

## Reporting Runtime Owns

The Reporting Runtime **owns the read/aggregation logic and the presentation DTOs** for the
report catalog above. It owns:

- The aggregation queries (GroupBy/Sum in SQL via EF) that turn ledger and business rows
  into report lines (`FinancialReportService.cs:63-80`, `ReportsController.cs:271-280`).
- The report models in `PointOfSale.Core/Models/Accounting/Reports/*` (TrialBalance,
  GL, P&L, BalanceSheet, CashFlow, ClearingAging).
- The KPI computation logic in `DashboardController` (revenue from `AccountCodes.PosSales`,
  inventory value from `ProductVariantStocks.CostPrice`).
- The party-statement read/opening-balance/adjustment *posting* orchestration
  (`StatementOfAccountController`), which wraps the accounting lifecycle.

## Reporting Runtime Does NOT Own

The Reporting Runtime does **not** own any write-model authority. It must defer to:

- **Accounting GL authority** — `ILedgerService` / journal posting owns the ledger; reporting
  only reads it (`FinancialReportService.cs:24` + `ILedgerOnlyService`).
- **Inventory authority** — `ProductVariantStocks` is written by the Inventory runtime;
  reporting reads it for valuation/low-stock (`ReportsController.cs:263-269`,
  `DashboardController.cs:122-134`).
- **Order/Sale/Purchase lifecycle** — invoices, payments, returns are written by their
  lifecycles; reporting reads `SalesInvoices`/`PurchaseInvoices` for operational detail
  (`ReportsController.cs:227-240`, `:2180-2213`).
- **Payment lifecycle** — `IPaymentLifecycleService` owns captured payments; reports read
  resulting ledger/journal rows only.
- **Affiliate attribution/commission engine** — owns conversion→commission; affiliate
  reports read its outputs.
- **CommerceHub financial control** — SuperAdmin/CommerceHub finance owns vendor
  commission/payout; CommerceHub dashboard reads derived rows.
- **Shipping / Treasury / SuperAdmin governance** — upstream producers; reporting reads
  downstream ledger/party rows, never governs them.

---

## Architecture Principles

P1. **Ledger-only financial reporting (enforced — but scoped).** `FinancialReportService`
     implements `ILedgerOnlyService`; architecture tests scan its fields/properties/source for
     any forbidden business DbSet and FAIL the build on violation
     (`ILedgerOnlyService.cs:23`, `LedgerIsolationTests.cs:29-81,111-148,160-190`,
     `ForbiddenBusinessDbSets.cs:17-66`). Allowed DbSets: `Accounts`, `JournalEntries`,
     `LedgerEntries` (`ForbiddenBusinessDbSets.cs:61-66`). **Important qualification (verified):**
     `ILedgerOnlyService` is implemented by **`FinancialReportService` ONLY** — no other class
     in the repo implements it. `ReportsController` is **NOT** ledger-only; it deliberately
     *blends* the ledger (for revenue/COGS/profit) with business tables (`SalesInvoices`,
     `PurchaseInvoices`, `StockMovements`) for quantities/gross/discounts. The strict
     enforceable guarantee therefore covers the financial statements, not the operational
     sales/purchase register reports. Related certification tests:
     `PosFinancialReportingAuthorityCertificationTests`
     (`InventoryValuation_MustUseCurrentInventoryCostAuthority`,
     `PartyAging_MustUseAccountLedgerForReceivablesAndPayables`,
     `DashboardFinancialCards_MustNotUseShadowBalancesOrCachedTotals`,
     `FinancialDashboard_MustNotCacheLedgerAggregates`).

P2. **Aggregate-in-SQL, not in-memory.** Every report groups/sums in the database
     (`AsNoTracking().GroupBy(...).Select(g => g.Sum(...))`) and only materializes report
     lines (`FinancialReportService.cs:63-80`, `ReportsController.cs:271-280`,
     `DashboardController.cs:78-104`). In-memory post-processing is limited to shaping
     (bucketing, running balances).

P3. **Posted/Reversed-only ledger scope.** Financial queries include only
     `JournalStatus.Posted || JournalStatus.Reversed`
     (`FinancialReportService.cs:59-62`); `Reversed` is retained so reversals net correctly
     against the original posted entry.

P4. **Repeatable-read consistent reads for financial reports.** Financial reports open a
     `RepeatableRead` transaction when none is already open
     (`FinancialReportService.cs:650-658`, `ReportsController.cs:2280-2288`).

P5. **Point-in-time balances by re-aggregation, not snapshots.** There is no persisted
     historical snapshot table for financial reports; balances as of a date are recomputed
     by summing all ledger entries with `JournalDate <= asOf`
     (`GetAccountBalanceAsOfAsync`, `FinancialReportService.cs:601-635`; GL opening/closing
     `:145,:173`). (Snapshot persistence is investigated separately; see Implementation
     Follow-up.)

P6. **No Mongo/Redis/ES for money truth.** Merchant reporting reads SQL only (grep: 0 hits
     for Mongo/Redis/ES across `ReportsController`, `DashboardController`,
     `FinancialReportsController`, `StatementOfAccountController`, `SalesController`). The
     storefront surface reads Mongo for *events only* and SQL ledger for *money*
     (`StorefrontAnalyticsAuthorityReader.cs:31-43`).

P7. **Tenant-scoped by mandatory `ITenantContext.RequiredTenantId`.** Every controller
     resolves the tenant from the service layer; `X-Tenant-Id` is never trusted from the
     client (`DashboardController.cs:40`, `FinancialReportsController.cs:40`).

P8. **Read-only except two scoped postings.** `StatementOfAccountController` exposes
     `CreateOpeningBalance` and `CreateAdjustment`, but both delegate to `ILedgerService` +
     `IAccountingOutboxService`, never direct DML
     (`StatementOfAccountController.cs:264,535,37,39`).

P9. **Cache is present but non-authoritative.** `IMemoryCache` is injected in
     `DashboardController`/`FinancialReportsController`, but `GetOrCreateAsync`/
     `GetOrCreateCachedAsync` ignore the cache and call the factory on every request
     (`DashboardController.cs:600-608`, `FinancialReportsController.cs:279-287`). TTL
     constants exist (`DashboardController.cs:18-21`) but are not effectively applied.

P10. **Soft-delete is respected on operational reads.** Inventory/low-stock and top-products
      queries filter `IsActive && !IsDeleted` on warehouse, variant, and product
      (`DashboardController.cs:122-134`, `ReportsController.cs:263-269`).


---

## Financial Reporting Runtime

Core implementation: `FinancialReportService` (implements `IFinancialReportService`,
`ILedgerOnlyService`) and `FinancialReportsController`. Every report aggregates
`LedgerEntries` joined to `JournalEntries` (status Posted|Reversed) and `Accounts`.

### Trial Balance (`GetTrialBalanceAsync`, FinancialReportService.cs:41-117; route
FinancialReportsController.cs:64-76)
- Source tables: `LedgerEntries` → `JournalEntries`, `Accounts` (ledger-only).
- Aggregation: GROUP BY `AccountId, AccountCode, Name, AccountType`; SUM Debit/Credit
  (`:63-80`). `NormalBalance` is derived in-memory from `AccountType` (computed
  `[NotMapped]` property, cannot be used in EF GroupBy — `:53-55`).
- Balance = Debit−Credit (debit-normal) or Credit−Debit (credit-normal).
- Consistency: `RepeatableRead` (`:47`). `report.IsBalanced`/`Variance` checked; warns if
  out of balance (`:109-114`).
- Authority: SQL ledger, immutable. No business tables.

### General Ledger (`GetGeneralLedgerAsync`, FinancialReportService.cs:123-187; route :86-122)
- Source: `LedgerEntries` for one `AccountId` + `LedgerAccounts` for account metadata.
- Opening balance = `GetAccountBalanceAsOfAsync(accountId, fromDate−1)`
  (`:145`, `:601-635`). Closing = balance as of `toDate` (`:173`).
- Each line carries `BalanceAfter` (the ledger's stored running balance, `:168`).
- Consistency: Posted|Reversed only; `RepeatableRead`.

### Profit & Loss / Income Statement (`GetProfitAndLossAsync`,
FinancialReportService.cs:193-271; route :131-143)
- Account types in scope: `Revenue`, `CostOfGoodsSold`, `Expense` (`:206-211`).
- Revenue line amount = Credit−Debit (credit-normal); Expense/COGS = Debit−Credit.
- Splits into `RevenueLines`, `CostOfGoodsSoldLines`, `ExpenseLines`
  (`:267-269`). Gross/Net computed by the model.
- Authority: ledger-only.

### Balance Sheet (`GetBalanceSheetAsync`, FinancialReportService.cs:277-369; route :152-162)
- Account types: `Asset`, `Liability`, `Equity`; all entries with `JournalDate <= asOf`
  (`:297-303`).
- `IsCurrent` derived from account code convention: 1000–1499 current assets, 2000–2499
  current liabilities (`IsCurrent`, `:640-648`).
- Computes `CurrentRatio` (`:337-346`) and `DebtToEquityRatio` (model). `IsBalanced`
  warned if variance (`:361-366`).
- Authority: ledger-only; point-in-time by re-aggregation.

### Cash Flow (`GetCashFlowAsync`, FinancialReportService.cs:375-454; route :171-183)
- Indirect method. Reuses P&L for `NetProfit` (`:388`).
- Opening/closing cash = sum of balances on accounts whose `AccountCode` starts with "10"
  (`:394-400`). Working-capital changes from prefix balances: "12" (AR), "13"
  (Inventory), "20" (AP) (`:406-439`).
- `InvestingActivities`/`FinancingActivities` are **empty lists** (`:451-452`) — only
  operating section is implemented. `NonCashAdjustments` empty (no depreciation accounts
  wired, `:450`). **Gap: investing/financing sections are stubs.**

### Clearing Aging (`GetClearingAgingAsync`, FinancialReportService.cs:460-550; route :192-202)
- Sources `LedgerEntries` where `Account.IsClearing` (`:482`).
- Buckets by journal date: Current (≤7d), 8–14, 15–30, >30 (`:471-515`). All bucket sums
  in SQL (`:500-515`). `PendingAmount` = net balance per account.
- Authority: ledger-only.

### Account Summary / Dashboard (`GetAccountSummariesAsync` :556-592; `dashboard` route
FinancialReportsController.cs:227-277)
- Account summary: balances as of "now" via `GetAccountBalancesAsOfAsync`
  (`:560-562`, `:660-699`).
- `dashboard` endpoint composes P&L + BalanceSheet + ClearingAging (`:247-249`), returns
  revenue/COGS/gross profit/margins/expenses/net profit/assets/liabilities/equity/ratios/
  pending clearing. Cached via `DashboardCacheTtl=30s` **but the cache wrapper is a no-op**
  (`:279-287`) → recomputed every call.

### Accounting Authority summary
- **Single source of financial truth: `LedgerEntries`.** No report reads `SalesInvoices`,
  `PurchaseInvoices`, `Customers`, `Suppliers`, `Products` (enforced by `ILedgerOnlyService`
  + `LedgerIsolationTests`).
- **Consistency model: strong.** `RepeatableRead`, Posted|Reversed scope, re-aggregation
  for point-in-time. No eventual consistency for the GL.

---

## Operational Reporting Runtime

Operational reports live in `ReportsController` and read operational system-of-record
tables (invoices, stock, movements) — NOT the ledger — because the operational question
("how many units sold?", "what's in stock?") is owned by the operational runtimes.

### Daily Close (`GetDailyCloseReport`, ReportsController.cs:346-537)
- Reads `LedgerEntries` for cashbox/clearing/bank/AR accounts by `SourceType`
  (`SalesInvoice`, `Return`, `Expense`, `PurchaseInvoice`, `SupplierPayment`)
  (`:369-492`). Cashbox = accounts with code prefix `AccountCodes.CashboxMain + "-"`
  (`:367`).
- Opening/closing cash = latest `BalanceAfter` per cashbox account before/at day boundary
  (`:494-528`). Movements enumerated by `SourceType` (`:460-492`).
- Authority: ledger for cash truth; classification by `SourceType` string (see debt:
  stringly-typed source types).

### Product Movement (`GetProductMovement`, ReportsController.cs:543-635)
- Reads `StockMovements` (operational) for a product, with opening balance derived from
  `ProductVariantStocks` when no movements exist (`:597-599`).
- Running balance uses `QuantityAfter` from the movement row (`:580`).
- Authority: inventory runtime (`StockMovements` is written by inventory ops).

---

## Inventory Reporting Runtime

### Inventory Valuation (`GetInventoryValuation`, ReportsController.cs:255-340)
- Source: `ProductVariantStocks` (variant-level stock, SQL authority for stock).
- **Cost source = `stock.Variant.CostPrice`** (purchase/replacement cost), NOT average
  cost and NOT the ledger. Value = `(Quantity + DamagedQuantity) * CostPrice`
  (`:277`, `:283`).
- Grouped by `Warehouse.Name` (`:271-280`); low-stock from `ProductVariants` available
  sellable stock (`Quantity − ReservedQuantity <= MinimumStockLevel`) (`:292-307`).
- Respects active/non-deleted warehouse, variant, product (`:263-269`).
- **Cost-basis debt:** valuation uses `CostPrice` (a single cost field), so it reflects
  latest/replacement cost, not a weighted-average or FIFO cost layer. Different from COGS
  in the ledger (which posts at the invoice's unit cost) — see Technical Debt.

### Low Stock (DashboardController `low-stock`, :429-496; ReportsController low-stock items)
- `DashboardController` low-stock: `ProductVariantStocks` where
  `Quantity − ReservedQuantity <= (Product.LowStockThreshold>0 ? threshold : 10)`
  (`:122-134`, `DefaultLowStockThreshold=10` `:21`). Out-of-stock when `Quantity==0`.
- Authority: inventory SQL.

---

## Sales Reporting Runtime

### Sales Analysis (`GetSalesAnalysis`, ReportsController.cs:71-249)
- **Revenue/profit are ledger-sourced:** revenue from `AccountCodes.PosSales` (Credit sum,
  minus Return debits), cost from `AccountCodes.CostOfGoodsSold` (Debit minus return
  reversals) (`:82-100`). `TotalProfit = TotalRevenue − TotalCost` (`:119`).
- Order count = `JournalEntries` with `SourceType=="SalesInvoice"` (`:102-109`).
- **Product detail is invoice-sourced:** `TopProducts` reads `SalesInvoiceItems` where
  `Status==Completed` (`:227-240`) — a deliberate hybrid: ledger for money, invoices for
  item detail.
- Daily series merges revenue/cost/orders per day in memory (`:178-224`).

### Sales Summary (`GetSalesSummary`, ReportsController.cs:896-1145)
- Source: `SalesInvoices` / `SalesInvoiceItems` (operational). Read this section for
  discount/tax/cashier/POS/store breakdowns. (Lineage delegated; see Implementation
  Follow-up for discount/tax source.)

### Sales By Category (`GetSalesByCategoryReport`, ReportsController.cs:1148-1400)
- Source: `SalesInvoiceItems` grouped by `Product.CategoryId/Name`, sums `TotalAmount`,
  `Quantity` (`:519-539` equivalent pattern). Invoice `Status != Cancelled`.

### Sales By Payment Method (`GetSalesByPaymentMethod`, ReportsController.cs:1403-1575)
- Source: `SalesInvoicePayments` grouped by `PaymentMethod` (`:573-587` pattern). Mirrors
  `DashboardController` `payment-methods`.

### Sales Returns (`GetSalesReturnsReport`, ReportsController.cs:1578-1720)
- Source: `Returns`/`ReturnItems` (or SalesInvoice reversal rows). Read section for
  details.

### Sales Rep / Cashier / Store reports
- Cashier/store/day breakdowns are derived in Daily Close (ledger `SourceType`) and
  `SalesController`. POS-specific reporting is operational (SalesInvoices + cashbox ledger).
- **Note (verified):** `SalesController.cs` is **transactional, not reporting** — it
  exposes `CreateSale`/`UpdateSale`/`DeleteSale`/`GetSale`/`GetSales`(paged)/`CreateReturn`/
  `GetReceipt` and uses `ApplicationDbContext` directly (no Mongo/Redis/ES, no analytics
  endpoints). Sales-rep analytics live in a **separate** controller `SalesRepReportsController`
  (route `api/salesrepreports`: `performance-summary`, `{id}/detailed-performance`,
  `commission-report`, `comparison`), which reads `SalesInvoices` directly (SQL, no ledger,
  no cache). The sales-register reports above (`sales-summary`, `sales-by-category`,
  `sales-by-payment-method`) additionally blend ledger-derived revenue/COGS with
  invoice-derived gross/discount/quantity via allocation
  (`ReportsController.cs:955-977,1322-1355,1460-1506`).

#### Purchase-side reporting authority (verified)
Purchase register / by-supplier / returns / cost-analysis read **business tables only**
(`PurchaseInvoices`, `PurchaseInvoiceItems`, `SupplierTransactions`, `Return`); they do NOT
recompute from the ledger. Purchase financials are therefore NOT ledger-authoritative in
these reports (only `FinancialReportService` is). This is a deliberate dual-source split:
financial *statements* are ledger-only; operational *purchase registers* are invoice-only.

---

## Purchase Reporting Runtime

### Purchase Register (`GetPurchaseRegisterReport`, ReportsController.cs:1723-1855)
- Source: `PurchaseInvoices` / `PurchaseInvoiceItems` (operational). Status filter
  `Completed` (`:2182` pattern).

### Purchases By Supplier (`GetPurchasesBySupplierReport`, ReportsController.cs:1858-2023)
- Source: `PurchaseInvoiceItems` joined to `PurchaseInvoices.SupplierId` (`:2184`),
  grouped by supplier.

### Purchase Returns (`GetPurchaseReturnsReport`, ReportsController.cs:2026-2160)
- Source: purchase return rows (operational).

### Cost Analysis (`GetCostAnalysisReport`, ReportsController.cs:2163-2276)
- Source: `PurchaseInvoiceItems` where `PurchaseInvoice.Status==Completed`
  (`:2180-2190`). Computes TotalPurchaseValue, BaseQuantity, min/max `UnitPrice`,
  **AverageCostPerUnit = TotalPurchaseValue / TotalQuantityPurchased** (`:2248`),
  and a cost-variance % per product (`:2249-2251`).
- **Cost source = purchase invoice `UnitPrice`** (operational), distinct from inventory
  valuation's `CostPrice` and from ledger COGS — three different cost notions coexist
  (see Technical Debt).

---

## Accounting Reporting Runtime

"Accounting reporting" == the financial statement set above (Trial Balance, GL, P&L,
Balance Sheet, Cash Flow, Clearing Aging) + the party sub-ledger statement
(`StatementOfAccountController`). The GL side is ledger-only and enforced; the party side
reads `AccountLedgers` (mutable sub-ledger). See Cross-Domain Authority Graph for how the
two ledgers relate.

---

## Customer Reporting Runtime

### Customer Statement / Outstanding / Credit
- `StatementOfAccountController` with `PartyType==Customer` reads `AccountLedgers`
  (`:72-75`), computes opening/closing via `RunningBalance` (`:95-107`, `:154-155`),
  paginated transactions (`:113-130`).
- `GetPartyBalance` sums `DebitAmount − CreditAmount` over `AccountLedgers`
  (`:227-230`). `GetOutstandingBalances` lists all customers with balance (`:925-926`).
- AR Aging (`ReportsController` `accounts-receivable-aging`, `:638-761`) reads
  `AccountLedgers` joined to `Customers`, buckets 0/1–30/31–60/61–90/90+ by
  `TransactionDate` (`:654-688`), applies an `outstandingFactor` from payment ratio
  (`:716-730`).
- **Authority: party sub-ledger (`AccountLedgers`), not the GL.** Customer "balance" is the
  sub-ledger net, which is maintained by the accounting lifecycle (not by reporting).

### Lifetime Value / Frequency / Revenue
- Operational KPIs (total customers, revenue) come from `DashboardController`
  (`Customers.CountAsync`, `:143-146`) and ledger revenue. No dedicated LTV report found in
  primary reads; flagged for follow-up.

---

## Supplier Reporting Runtime

### Supplier Ledger / Outstanding / Purchase Volume / Payments
- `StatementOfAccountController` `PartyType==Supplier` (same logic as customer,
  `:178-187` resolves `Suppliers`).
- AP Aging (`ReportsController` `accounts-payable-aging`, `:767-878`) reads `AccountLedgers`
  joined to `Suppliers`, buckets by `TransactionDate`, `outstandingFactor` from payments
  (`:819-864`).
- Purchase volume = `PurchaseInvoiceItems` (Purchases By Supplier, `:1858-2023`).
- **Authority: party sub-ledger for balances; purchase invoices for volume.**

---

## Affiliate Reporting Runtime

Affiliate reporting is produced by three distinct surfaces with **two different authority
doctrines** (delegated investigation, file:line-cited):

- **Affiliate-owned analytics (display):** `AffiliateAnalyticsController`
  (`Controllers/AffiliateAnalyticsController.cs:36`, route `api/affiliate/analytics`, policy
  `AffiliateOnly`). Its `summary` reads **MongoDB only** (`affiliate_analytics_daily` →
  `AffiliateAnalyticsDailyDocument`, clicks/orders/earnings/conversion from Mongo) and is
  Redis-cached 30s (`AffiliateAnalyticsController.cs:71-129`). The controller doc states
  "All analytics data from MongoDB projections — ZERO SQL for analytics" (`:19-32`) — yet
  it still hits SQL `StoreConfigs` for store-name resolution (`:261-268`).
- **Affiliate portal (self-serve financial authority):** `AffiliatePortalController`
  (`Controllers/AffiliatePortalController.cs:34`). Financial totals come from the **SQL
  commission ledger** `AffiliateCommissionLedger`
  (`Core/Entities/Affiliate/AffiliateCommissionLedger.cs:28`, append-only, `EntryType ∈
  {Earned, Reversed, Approved, Paid}` `:405-411`); MongoDB is display-only
  (`:22-26`). Aggregates **per-currency** to avoid cross-currency addition
  (`:108-110`).
- **Admin affiliate (financial authority):** `AdminAffiliateController`
  (`Controllers/AdminAffiliateController.cs:41`). `AffiliateCommissionLedger` is the
  authority for approval/payout eligibility and payout totals (`:20-22`);
  `BuildAffiliateLedgerTotalsAsync` sums `CommissionAmount` grouped by
  `(AffiliateAccountId, Currency, EntryType)` (`:1877-1950`); NetEarnings = Earned −
  Reversed (`:1936`).

**Commission source:** `AffiliateCommissionLedger.CommissionAmount`, snapshotted at earn
time as `min(BaseAmount × CommissionRate, MaxPerItem)` (entity `:133-135`). **Conversion
attribution source (SQL):** `AffiliateAttribution` (`Core/Entities/Affiliate/AffiliateAttribution.cs:14`),
one per (TenantId, OrderId); the Mongo `affiliate_clicks.OrderId` is a *display* bridge only.
**Settlement:** approve/pay mutations flow only through `IAffiliateService` /
`IAffiliateSettlementOrchestrator` (`:511-524`); pay requires external `PaymentReference` +
cashbox pre-check (`:653-770`).

**Architectural debt (affiliate):** the Mongo `summary` (clicks/orders/earnings/conversion)
and the SQL-ledger `dashboard`/`earnings-summary` are **two engines that can disagree**
during Mongo projection lag, and "earnings" means different things (Mongo daily `Earnings`
vs ledger `Earned` commission) with no reconciliation endpoint (`:100-101`). Conversion-rate
is defined twice (Mongo orders/clicks vs SQL ledger `Earned`). **Authority: affiliate SQL
ledger for money; Mongo is an eventually-consistent projection.** Cross-tenant isolation via
`StoreTenantId` / `TenantId` (Shopixy rules).

---

## CommerceHub Financial Reporting Runtime

CommerceHub financial reporting is a **separate, marketplace-scoped** reporting surface
with its own dedicated ledger (delegated investigation, file:line-cited):

- **Service:** `CommerceHubFinancialDashboardService`
  (`Services/CommerceHub/Finance/CommerceHubFinancialDashboardService.cs:31`) behind
  `ICommerceHubFinancialDashboardService` (`:12`). Admin controller
  `AdminCommerceHubFinanceController` (`Controllers/AdminCommerceHubFinanceController.cs:12`,
  roles Owner/Admin/Accountant) exposes only vendor `dashboard` + `statement` + CSV export
  (`:25-61`). Platform overview `GetPlatformOverviewAsync` (service `:48`) is consumed by
  **SuperAdmin only** (`SuperAdminFinanceOverviewService.cs:40-42`), not the admin route.
- **Source table:** `CommerceHubFinancialTransactions`
  (`Core/Entities/CommerceHubFinance/CommerceHubFinancialTransaction.cs:6`) — a **fourth
  ledger**, separate from merchant `LedgerEntries`/`AccountLedgers`. Fields: `EntityType`
  (Platform/Vendor), `EntityId`, `TransactionType`, `Direction`, `FinancialBucket`
  (Cash/Payable/Receivable), `Amount`, `Currency`, `ReferenceType`, `ReferenceId`.
- **Wallet/debt:** `CommerceHubWalletService` (`:17`) and `CommerceHubVendorDebtService`
  (`:40`) also read **only** `CommerceHubFinancialTransactions` (wallet `:179-208`; debt
  replays events in `(CreatedAtUtc, Id)` order `:263-290`).
- **No Mongo/Redis/ES.** 100% SQL. **No caching** — every call re-aggregates.
- **Currency invariant:** EGP-only. `EnsureSingleCurrency` throws if a query spans >1
  currency (`:863-876`); `RequireEgpCurrency` per field (`:206-210`). This is stricter than
  the merchant ledger (which has no EGP gate).
- **How it differs from merchant financial reporting:** merchant reads `LedgerEntries` +
  `PosSales` + `SalesInvoice*` + inventory (POS/retail, multi-currency); CommerceHub reads a
  **dedicated** ledger with explicit Platform-vs-Vendor partitioning and commission/promotion/
  settlement/payout transaction types (two-sided platform income vs vendor liability).
- **Architectural debt (CommerceHub):** platform overview unreachable from admin controller
  (SuperAdmin-only); vendor "TotalCommissions" is derived by joining **platform** ledger rows
  via `BuildVendorCommissionQuery` (`:547-575`) — commission computed 3 ways (vendor ledger,
  platform joined, debt-event replay); wallet/statement/debt balances each recomputed
  independently from the same table with slightly different filters; single-currency hard
  throw is brittle for a marketplace; "NetProfit"/"commission" mean different things in
  platform vs vendor context (`:203-205` vs `:333`).

**Authority: CommerceHub SQL ledger (`CommerceHubFinancialTransactions`) only; no
projections; EGP-enforced.**

---

## Executive Dashboard Runtime

Implemented in `DashboardController` (`api/dashboard/*`). This is the merchant executive
KPI surface.

- `stats` (`:45-208`): revenue from `LedgerEntries` on `AccountCodes.PosSales`
  (Today/Yesterday/Week/Month/Year/Total, `:70-105`); transaction count from
  `JournalEntries` `SourceType=="SalesInvoice"` (`:107-115`); `totalProducts` from
  `Products` (`:117-120`); low-stock count from `ProductVariantStocks` (`:122-141`);
  `totalCustomers` from `Customers` (`:143-146`); **outstanding receivables from
  `AccountLedgers` (PartyType.Customer)** (`:148-155`); **outstanding payables from
  `AccountLedgers` (PartyType.Supplier)** (`:157-164`); **inventory value from
  `ProductVariantStocks * CostPrice`** (`:166-176`).
- `sales-trend` (`:213-320`): ledger `PosSales` aggregated by day/month.
- `top-products` (`:325-375`): `SalesInvoiceItems` (invoice-sourced).
- `recent-sales` (`:380-424`): `SalesInvoices`.
- `low-stock` (`:429-496`): `ProductVariantStocks`.
- `category-sales` (`:501-550`): `SalesInvoiceItems`.
- `payment-methods` (`:555-598`): `SalesInvoicePayments`.

**Hybrid authority pattern (critical):** KPI *money* (revenue, receivables, payables,
inventory value) is ledger/party-ledger/inventory-stock authoritative; *operational*
breakdowns (top products, categories, payment methods, recent sales) are invoice-sourced.
This means a revenue total and a "top products revenue" can be computed from two different
sources — see Technical Debt / Lineage Matrix.


## KPI Runtime

There is no standalone "KPI service"; KPIs are computed inline in `DashboardController`
and composed in `FinancialReportsController.dashboard`. KPI definitions:

- **Revenue / Today/Week/Month/Year**: `SUM(CreditAmount − DebitAmount)` on
  `LedgerEntries` where `Account.AccountCode == AccountCodes.PosSales`,
  Posted|Reversed, filtered by `JournalDate` bucket
  (`DashboardController.cs:70-105`, `BuildPostedRevenueQuery` `:610-616`).
- **Sales change %**: `(Today − Yesterday)/Yesterday * 100`
  (`DashboardController.cs:178-180`).
- **Inventory Value**: `SUM((Quantity+DamagedQuantity) * Variant.CostPrice)` over
  `ProductVariantStocks` (`DashboardController.cs:166-176`).
- **Outstanding Receivables/Payables**: net of `AccountLedgers` by `PartyType`
  (`DashboardController.cs:148-164`).
- **Low Stock Count**: count of `ProductVariantStocks` below threshold
  (`DashboardController.cs:122-141`).
- **Financial KPIs (gross/net profit, margins, ratios)**: from P&L + BalanceSheet
  (`FinancialReportsController.cs:247-272`).

**Calculation authority:** ledger for money KPIs; operational tables for counts/breakdowns.
**Refresh:** effectively per-request (cache wrapper is a no-op, `DashboardController.cs:600-608`).
**Consistency:** revenue KPI is strongly consistent (ledger, RepeatableRead in the report
layer); operational KPIs are strongly consistent (direct SQL reads, no projection lag).
**Can KPIs differ from reports?** Yes — e.g. dashboard revenue derives from `PosSales`
ledger; `sales-analysis` revenue also derives from `PosSales` ledger (consistent), but
`top-products` revenue derives from `SalesInvoiceItems.TotalAmount` (invoice-sourced,
may differ from ledger if invoices and ledger ever diverge). See Lineage + Decision Matrix.

---

## Aggregation Runtime

All aggregation is **in-SQL via EF Core `GroupBy` + `Sum`/`Count`/`Min`/`Max`**, executed
under `AsNoTracking()` (`FinancialReportService.cs:63-80`, `ReportsController.cs:271-280`,
`DashboardController.cs:78-104`). Patterns observed:

- **SUM**: debit/credit totals, revenue, cost (`FinancialReportService.cs:76-77`).
- **COUNT**: transactions, products, customers, low-stock (`DashboardController.cs:107-146`).
- **AVG**: implied via `Sum/Count` (AOV = revenue/orders, `ReportsController.cs:124`;
  AverageCostPerUnit = value/qty, `ReportsController.cs:2248`).
- **Window/frame functions**: NOT used in reporting queries; running balances come from the
  ledger's stored `BalanceAfter` column (`FinancialReportService.cs:168`) or the party
  ledger's stored `RunningBalance` (`AccountLedger.cs:55`), not SQL window functions.
- **Snapshots / materialized views**: none for financial/operational reports — balances are
  recomputed on read by date-range filtering (see Snapshot Runtime). SQL `VIEW`s for
  reporting: not present in primary reads (flagged).
- **Computed values**: `NormalBalance` derived from `AccountType` in-memory because it is
  `[NotMapped]` and unusable in EF GroupBy (`FinancialReportService.cs:53-55,82-98`).

---

## Snapshot Runtime

**Finding:** The Reporting Runtime does **not** persist historical report snapshots or
materialized aggregate tables for its core reports. Point-in-time values are reconstructed
on every read by re-aggregating `LedgerEntries`/`AccountLedgers` with `JournalDate <= asOf`
(`GetAccountBalanceAsOfAsync`, `FinancialReportService.cs:601-635`; opening/closing in GL
`:145,:173`; AR/AP aging `asOfDate` filter `ReportsController.cs:647-659`).

- **Historical accuracy:** guaranteed by re-aggregation against the immutable ledger
  (`JournalDate <= asOf`, Posted|Reversed). Historical reports CAN change if the underlying
  ledger is corrected (reversal/post), which is the intended accounting behavior, not a bug.
- **Replay / rebuild:** not applicable — there is no snapshot to replay; the ledger itself
  is the replayable source. Daily Close is computed on demand, not stored.
- **Gap (verified):** there are **no persisted snapshot tables** (`KpiSnapshot`/
  `ReportSnapshot`/`DashboardSnapshot` → none) and **no scheduled pre-computation**. Hangfire
  jobs exist only for WhatsApp and Shipping; **no Hangfire job precomputes dashboard/report
  aggregates**. All reporting is computed-on-read from SQL
  (delegated investigation confirmed; `DailyCloseReportDto` is a computed DTO, not persisted).
  The only persisted "snapshot"-like construct is the in-memory `StorefrontCommerceAnalyticsSnapshot`
  result (`StorefrontAnalyticsAuthorityReader.cs:23-29`).

---

## Export Runtime

**Verified (delegated investigation + primary grep):** No third-party export libraries exist
anywhere in the repo (`EPPlus`, `ClosedXML`, `CsvHelper`, `iText7`, `PdfSharp`, `MigraDoc` →
0 matches). The **only** report export is **CSV, hand-built** by
`CommerceHubFinancialDashboardService.ExportVendorStatementCsvAsync` (`:453-531`), which
builds rows with `StringBuilder` + `EscapeCsv` and returns UTF-8 bytes
(`CommerceHubFinanceStatementCsvExport`), served via `File(...)` from
`AdminCommerceHubFinanceController.cs:60` and `SuperAdminVendorFinanceController.cs:268`. It
reads the **CommerceHub financial ledger** (`CommerceHubFinancialTransactions`), NOT the
merchant `AccountLedgers`/GL — a separate (4th) ledger for marketplace/vendor finance.

- **No export exists** for Statement of Account, AR/AP aging, P&L, Trial Balance, Balance
  Sheet, or the merchant dashboard.
- **Large-dataset handling:** the vendor CSV materializes all rows in-memory
  (`StringBuilder`, `:475-526`) — no streaming, no paging. Scaling risk for very large
  vendor statements.
- **Merchant reporting returns JSON DTOs only**; large result sets rely on SQL server-side
  aggregation + pagination (`StatementOfAccountController` `Skip/Take`) rather than an export
  stream.

**Conclusion:** the merchant Reporting Runtime has **no export runtime**; the single CSV
export is a CommerceHub-specific, in-memory, hand-rolled implementation. This is a real gap
(item F7).

---

## CQRS

The Reporting Runtime is the **read side** of the platform's CQRS split.

- **SQL Reads (authoritative):** All merchant reporting reads `ApplicationDbContext`
  (PostgreSQL). Verified zero Mongo/Redis/ES references in `ReportsController`,
  `DashboardController`, `FinancialReportsController`, `StatementOfAccountController`,
  `SalesController` (grep: 0 hits each).
- **Mongo Reads:** Only the Storefront Analytics Dashboard reads Mongo — and only the
  `analytics_events` collection for behavioral funnel events
  (`StorefrontAnalyticsDashboardService.cs:26,37,95-140`). Money is explicitly resolved to
  SQL ledger (`StorefrontAnalyticsAuthorityReader.cs:31-43`).
- **Redis Reads:** none in reporting (verified 0 hits).
- **Elasticsearch Reads:** none in reporting (verified 0 hits). ES is the storefront
  search authority, not a reporting source.
- **Projection Reads:** reporting does not consume CQRS projections (product/catalog Mongo
  docs, listing docs). It reads the write-model SQL directly.
- **Repair-on-Read:** not used by reporting (that pattern belongs to the catalog/search
  read path). Reporting trusts SQL authority directly.
- **Freshness / Consistency:** strong. Reports reflect committed SQL state at read time
  under `RepeatableRead` (financial) or default read committed (operational). There is no
  eventual-consistency window in the SQL reporting path. The only eventual-consistency
  surface is storefront funnel events (Mongo), which are best-effort behavioral metrics,
  explicitly separate from money.

---

## Projection Usage

- **Merchant reporting:** NO projections. Reads write-model SQL (ledger, invoices, stock,
  party ledger, movements).
- **Storefront analytics:** ONE projection — Mongo `analytics_events` (behavioral only).
  All commerce/financial projections are deliberately bypassed in favor of the SQL ledger
  (`StorefrontAnalyticsAuthorityReader.cs:31-43`: "Redis and MongoDB analytics projections
  must never replace this reader for financial metrics").
- **Why projections are avoided for reporting:** the platform treats the SQL write-model as
  the single source of financial/operational truth; replaying projections would introduce a
  consistency gap between reported numbers and the ledger. Reporting chooses correctness
  (read the authority) over latency (read a projection).

---

## Read Models

The Reporting Runtime works over six SQL read models plus one Mongo read model:

1. **General Ledger (`LedgerEntries` + `JournalEntries` + `Accounts`)** — immutable
   financial truth. Source for all financial statements + revenue KPIs.
2. **Party Sub-Ledger (`AccountLedgers`)** — mutable per-party (Customer/Supplier) ledger
   with stored `RunningBalance`. Source for statements, balances, AR/AP aging.
3. **Sales operational (`SalesInvoices`, `SalesInvoiceItems`, `SalesInvoicePayments`)** —
   Source for top-products, category-sales, payment-methods, sales summary/returns, recent
   sales.
4. **Purchase operational (`PurchaseInvoices`, `PurchaseInvoiceItems`)** — Source for
   purchase register, by-supplier, cost analysis.
5. **Inventory (`ProductVariantStocks`, `StockMovements`)** — Source for valuation, low
   stock, product movement.
6. **Storefront commerce (`Orders`, `OrderItems`, `CheckoutSessions`)** — Source for
   storefront top products / abandoned checkouts.
7. **Mongo `analytics_events`** — Source for storefront funnel/visit events only.

Two-ledger model (GL vs party sub-ledger) is the central architectural fact: GL is the
immutable, journal-derived, *derived-on-read* authority; the party sub-ledger is a
*maintained* running balance written by the accounting lifecycle. They are reconciled by
the accounting runtime, not by reporting.


## Runtime Authority Matrix

For each report: Business Owner (who writes the source), SQL Authority (table read),
Projection Authority (none/Mongo), Aggregation Authority (where computed), Runtime Truth
(what the number means).

| Report | SQL Authority (read) | Projection | Aggregation | Runtime Truth |
|---|---|---|---|---|
| Trial Balance | `LedgerEntries`+`JournalEntries`+`Accounts` | none | SQL GroupBy (FinancialReportService.cs:63) | Immutable ledger |
| General Ledger | `LedgerEntries`+`LedgerAccounts` | none | SQL + stored `BalanceAfter` | Immutable ledger |
| P&L / Income Stmt | `LedgerEntries` (Rev/COGS/Exp) | none | SQL GroupBy | Immutable ledger |
| Balance Sheet | `LedgerEntries` (Asset/Liab/Equity) | none | SQL GroupBy + code convention | Immutable ledger |
| Cash Flow | `LedgerEntries` (prefix 10/12/13/20) | none | SQL + P&L reuse | Immutable ledger (operating only) |
| Clearing Aging | `LedgerEntries` (`IsClearing`) | none | SQL buckets | Immutable ledger |
| Dashboard stats | `LedgerEntries`(PosSales)+`AccountLedgers`+`ProductVariantStocks`+`Products`+`Customers` | none | SQL | Hybrid (money=ledger/party/stock) |
| Sales Analysis | `LedgerEntries`(PosSales/COGS) + `SalesInvoiceItems` | none | SQL + in-memory merge | Money=ledger; items=invoice |
| Inventory Valuation | `ProductVariantStocks`+`ProductVariants` | none | SQL GroupBy | Inventory runtime (CostPrice) |
| Product Movement | `StockMovements`+`ProductVariantStocks` | none | SQL | Inventory runtime |
| Daily Close | `LedgerEntries` (cashbox/clearing) | none | SQL | Immutable ledger |
| AR Aging | `AccountLedgers`+`Customers` | none | SQL + factor | Party sub-ledger |
| AP Aging | `AccountLedgers`+`Suppliers` | none | SQL + factor | Party sub-ledger |
| Purchase Register / By Supplier / Cost Analysis | `PurchaseInvoices`+`PurchaseInvoiceItems` | none | SQL GroupBy | Purchase runtime |
| Statement of Account | `AccountLedgers` | none | SQL + `RunningBalance` | Party sub-ledger |
| Storefront Dashboard (events) | `analytics_events` (Mongo) | Mongo | Mongo `$facet` | Behavioral (best-effort) |
| Storefront Dashboard (commerce) | `LedgerEntries`(EcommerceSales)+`Orders`+`OrderItems`+`CheckoutSessions` | none (SQL authority reader) | SQL | Immutable ledger + commerce |

---

## Cross-Domain Authority Graph

Reporting reads from (and is read-only toward) the following peers. "Writes" = reporting
never writes to these domains except the two scoped postings that delegate to the owning
lifecycle.

| Peer | Reads (tables) | Writes | Dependency | Sync model |
|---|---|---|---|---|
| Merchant Platform (POS Admin) | `SalesInvoices`, `Products`, `ProductVariantStocks`, `Customers` | none | Operational source for sales/inventory/customer reports | Real-time SQL read |
| POS Runtime | `SalesInvoices`, cashbox `LedgerEntries` | none | Daily close / sales KPIs | Real-time SQL read |
| Inventory Runtime | `ProductVariantStocks`, `StockMovements` | none | Valuation, low-stock, movement | Real-time SQL read (stock is SQL authority) |
| CommerceHub | `CommerceHubFinancialTransactions` (via CommerceHub dashboard svc) | none | CommerceHub financial dashboard | SQL read (delegated investigation) |
| Accounting | `LedgerEntries`, `JournalEntries`, `Accounts`, `AccountLedgers` | Only via `ILedgerService`+outbox (opening bal / adjustment) | ALL financial + party reports | Real-time SQL read; postings delegated |
| Treasury | `AccountCodes.BankAccount`/`VisaClearing`/`StripeClearing` ledger rows | none | Cash flow, daily close | Real-time SQL read (ledger only) |
| Affiliate | affiliate domain tables (conversions/commissions/wallet) | none | Affiliate reporting | SQL/Mongo read (delegated) |
| Payments | ledger rows posted by `IPaymentLifecycleService` | none | Revenue, cash flow | Reads downstream ledger only |
| Shipping | none directly | none | (no shipping report in primary reads) | n/a |
| SuperAdmin | none directly (SuperAdmin has its own finance overview svc) | none | Platform oversight (separate runtime) | Separate SQL read |

**Key cross-domain rule:** Reporting never reaches *around* a lifecycle owner. It reads the
lifecycle's downstream SQL (ledger/invoice/stock/party rows), never calls the lifecycle to
mutate. The single exception — party opening-balance and adjustment postings in
`StatementOfAccountController` — wraps `ILedgerService` + `IAccountingOutboxService`
(`StatementOfAccountController.cs:37,39,264,535`), i.e. it still defers to the Accounting
owner rather than issuing DML.

---

## Business Invariants

(Each verified against code.)

I1. **Ledger always balances (Trial Balance).** `TrialBalanceReport.IsBalanced` is asserted;
    out-of-balance is logged as a warning, not silently accepted
    (`FinancialReportService.cs:109-114`). The GL is the single financial truth; any report
    reading business tables for money would violate this (blocked by `ILedgerOnlyService`).

I2. **Posted-or-Reversed scope.** Financial aggregates exclude `Draft`/`Failed`/`Pending`
    journals (`FinancialReportService.cs:59-62`). Reversals are retained so they net
    against originals.

I3. **Revenue KPI = ledger `PosSales` net.** Dashboard revenue is `SUM(Credit−Debit)` on
    `AccountCodes.PosSales` (`DashboardController.cs:70-105`). Storefront revenue is the
    analogous `AccountCodes.EcommerceSales` net (`StorefrontAnalyticsAuthorityReader.cs:60-73`).

I4. **Inventory value = stock × CostPrice.** `(Quantity+DamagedQuantity)*CostPrice`
    (`DashboardController.cs:166-176`, `ReportsController.cs:277,283`). Single cost field,
    not averaged.

I5. **Party balance = sub-ledger net.** Customer/Supplier balance = `Debit−Credit` over
    `AccountLedgers`, carried as `RunningBalance` (`AccountLedger.cs:55`,
    `StatementOfAccountController.cs:227-230`). Distinct from GL.

I6. **Soft-deleted rows excluded from operational reports.** Inventory/top-products filter
    `IsActive && !IsDeleted` (`DashboardController.cs:122-134`,
    `ReportsController.cs:263-269,348-349`).

I7. **Cancelled invoices excluded.** `SalesInvoiceItems` reports filter
    `Status != Cancelled` (`DashboardController.cs:349,399,523`).

I8. **Tenant isolation mandatory.** Every report scopes by `TenantId`
    (`DashboardController.cs:40`, `FinancialReportsController.cs:40`).

I9. **AR/AP bucketed by transaction date, net of payments.** Aging uses `outstandingFactor`
    derived from paid/total ratio so partial payments reduce aged buckets
    (`ReportsController.cs:716-730,845-859`).

---

## Architectural Decisions (WHY)

AD1. **Why reports read SQL, not projections.** The platform treats the SQL write-model as
     the single source of financial/operational truth. Reading a Mongo/Redis projection
     would let reported numbers drift from the ledger. Decision: sacrifice read latency for
     correctness (`StorefrontAnalyticsAuthorityReader.cs:31-43` states Mongo/Redis must
     never replace the SQL reader for money).

AD2. **Why the financial service is ledger-only and CI-enforced.** Money must have exactly
     one derivation path. `ILedgerOnlyService` + `ForbiddenBusinessDbSets` +
     `LedgerIsolationTests` make any business-table access in financial reporting a build
     failure (`ILedgerOnlyService.cs:23`, `ForbiddenBusinessDbSets.cs:17-66`,
     `LedgerIsolationTests.cs:29-81`). This prevents "recompute revenue from invoices"
     drift.

AD3. **Why two ledgers (GL + party sub-ledger).** The GL is immutable and journal-derived;
     the party sub-ledger (`AccountLedgers`) carries a maintained `RunningBalance` for fast
     statement rendering and AR/AP aging without re-aggregating the entire GL per party.
     They serve different consistency needs: GL = audit truth; party ledger = operational
     speed (`AccountLedger.cs:55` vs `LedgerEntry.BalanceAfter`).

AD4. **Why reports are (almost) read-only.** Reporting must never be a write path that can
     corrupt the ledger. The only mutations are opening-balance/adjustment postings, which
     delegate to the Accounting lifecycle + outbox (`StatementOfAccountController.cs:264,535`).

AD5. **Why no persisted snapshots.** Financial point-in-time is reconstructed by date-range
     re-aggregation against the immutable ledger, guaranteeing historical accuracy and
     avoiding a second writable store that could diverge
     (`GetAccountBalanceAsOfAsync`, `FinancialReportService.cs:601-635`).

AD6. **Why storefront analytics uses Mongo for events but SQL for money.** Behavioral
     funnel/visit events are high-volume, append-only, and tolerantly eventually-consistent
     — a natural fit for Mongo aggregation (`$facet`). Money must be exact, so it is resolved
     to the SQL ledger (`StorefrontAnalyticsDashboardService.cs:26`,
     `StorefrontAnalyticsAuthorityReader.cs:31-43`).

AD7. **Why the cache wrapper is a no-op.** `GetOrCreateAsync`/`GetOrCreateCachedAsync` call
     the factory unconditionally (`DashboardController.cs:600-608`,
     `FinancialReportsController.cs:279-287`). Interpretation: the team prioritized
     always-fresh numbers over cache hits for financial/stock dashboards; TTL constants are
     declared but not effectively applied (see Technical Debt).

AD8. **Why RepeatableRead for financial reads.** Prevents dirty/non-repeatable reads mid-
     report so a balance sheet and its component lines cannot be computed against two
     different committed states (`FinancialReportService.cs:650-658`).


## Technical Debt

(Documented only where implementation evidence exists in primary reads; subagent-verified
items marked [SUB].)

TD1. **Three different cost notions coexist.**
     - Inventory valuation uses `ProductVariantStocks.Variant.CostPrice`
       (`ReportsController.cs:277,283`; `DashboardController.cs:175`).
     - Ledger COGS posts at the sales invoice's unit cost (`AccountCodes.CostOfGoodsSold`,
       `ReportsController.cs:92-100`).
     - Cost analysis uses purchase invoice `UnitPrice` (`ReportsController.cs:2211-2212`).
     These can disagree (latest cost vs invoice cost vs average). No single cost authority.
     **Priority: High.**

TD2. **Revenue computed from two sources.**
     - Ledger `PosSales` net (`DashboardController.cs:70-105`, `ReportsController.cs:82-90`).
     - Invoice `SalesInvoiceItems.TotalAmount` for top-products/category
       (`DashboardController.cs:345-364`, `ReportsController.cs:227-240,519-539`).
     If ledger and invoices ever diverge (they shouldn't, by design), KPI revenue and
     breakdown revenue disagree. **Priority: Medium** (design intends ledger=truth; the
     invoice reads are for item detail, but the revenue *column* in those DTOs is
     invoice-sourced).

TD3. **No-op cache.** `IMemoryCache` is injected and TTLs declared
     (`DashboardController.cs:18-21`) but `GetOrCreateAsync` ignores the cache
     (`DashboardController.cs:600-608`). Either remove the cache or actually use it; the
     current code implies caching that does not happen. **Priority: Low** (correctness-safe
     but misleading).

TD4. **Stringly-typed `SourceType` coupling.** Daily Close and sales-analysis branch on
     magic strings `"SalesInvoice"`, `"Return"`, `"Expense"`, `"PurchaseInvoice"`,
     `"SupplierPayment"`, `"Order_Payment"`, etc.
     (`ReportsController.cs:374,394,471,475; StorefrontAnalyticsAuthorityReader.cs:37-43`).
     No enum guard; a rename breaks reports silently. **Priority: Medium.**

TD5. **Cash Flow investing/financing stubs.** `InvestingActivities`/`FinancingActivities`
     are empty; `NonCashAdjustments` empty (no depreciation wiring)
     (`FinancialReportService.cs:450-452`). Cash flow is operating-section only.
     **Priority: Medium** (incomplete financial statement).

TD6. **Account-code-prefix magic numbers.** Cash flow and balance-sheet "current" logic
     depend on code prefixes "10"/"12"/"13"/"20" and the 1000–1499/2000–2499 convention
     (`FinancialReportService.cs:394-439,640-648`). Fragile if chart-of-accounts numbering
     changes. **Priority: Medium.**

TD7. **AR/AP aging `outstandingFactor` heuristic.** Buckets are scaled by
     `1 − paid/total`, an approximation that assumes payments apply proportionally across
     all aged buckets (`ReportsController.cs:716-730,845-859`). Edge cases (overpayment,
     credit notes) may mis-age. **Priority: Low/Medium.**

TD8. **No verified export runtime.** Primary reads found no Excel/CSV/PDF export in the
     merchant reporting surface (grep hits only in CommerceHub/SuperAdmin finance services).
     If export exists, it is uncertified here; if not, large reports rely on JSON + manual
     pagination. **Priority: Medium** (gap to close). [SUB to confirm]

TD9. **No persisted dashboard/report snapshot.** All values recomputed per request; under
     load this re-aggregates the full ledger each call (mitigated by `AsNoTracking` + SQL
     aggregation, but no materialized cache). **Priority: Low** (performance, not
     correctness).

TD10. **Storefront vs merchant revenue account split.** Merchant revenue reads
      `AccountCodes.PosSales`; storefront reads `AccountCodes.EcommerceSales`
      (`DashboardController.cs:616` vs `StorefrontAnalyticsAuthorityReader.cs:69`). Two
      accounts, two code paths; cross-channel consolidation is not evidenced in primary
      reads. **Priority: Low.**

TD11. **Cost-field inconsistency across reports.** Inventory valuation uses
      `ProductVariant.CostPrice` (`ReportsController.cs:277,283`); purchase-returns uses
      `Product.PurchasePrice` (`ReportsController.cs:2075`); ledger COGS posts at invoice unit
      cost; cost-analysis uses purchase `UnitPrice`. At least three distinct cost columns
      (`Variant.CostPrice`, `Product.PurchasePrice`, invoice `UnitPrice`) can disagree, and
      `Product.PurchasePrice`/`SalePrice` are marked "legacy" in the entity. **Priority: High.**

TD12. **Four parallel ledgers, separate write paths.** `LedgerEntries` (GL),
      `AccountLedgers` (party), `SupplierTransactions`/`CustomerTransactions` (detail), and
      `CommerceHubFinancialTransactions` (marketplace) are maintained by independent writers.
      Party balances are reconciled only via a manual `RebuildPartyBalanceAsync`
      (`LedgerService.cs:105-194`); supplier outstanding details read `SupplierTransactions`+
      `PurchaseInvoices` (a third model) that can drift from `AccountLedgers`
      (`StatementOfAccountController.cs:1077-1109`). **Priority: High** (reconciliation risk).

TD13. **Cross-source KPIs (storefront + affiliate).** Storefront
      `ConversionRate = SQL Purchases / Mongo Visits` (`StorefrontAnalyticsDashboardService.cs:78-80`);
      affiliate `summary` conversion = Mongo orders/clicks, while authority is the SQL ledger
      `Earned` entries — two definitions that disagree during Mongo lag. **Priority: Medium.**

TD14. **SOA opening-balance double representation.** In `GetStatementOfAccount`, opening-
      balance transactions appear both in the OpeningBalance card and the paginated list
      (excluded from Debit/Credit totals only) — acknowledged UI/consistency ambiguity
      (`StatementOfAccountController.cs:157-172`). **Priority: Low.**

TD15. **CommerceHub commission derived 3 ways.** Vendor commission is computed from vendor
      ledger `Commission`, platform ledger joined by reference (`BuildVendorCommissionQuery`
      `:547-575`), and debt-event replay (`CommerceHubVendorDebtService.cs:263-290`) with
      slightly different filters; plus a single-currency hard throw that fails the whole
      overview on multi-currency data (`EnsureSingleCurrency` `:863-876`). **Priority: Medium.**

---

## Implementation Follow-up Items

| # | Title | Current Implementation | Why | Future Improvement | Priority | Status |
|---|---|---|---|---|---|---|
| F1 | Unify cost authority | Valuation=CostPrice, COGS=invoice unit cost, CostAnalysis=UnitPrice, Returns=PurchasePrice | Four sources drift | Single cost layer (average/FIFO) projected to stock + ledger + returns | High | Open |
| F2 | Single revenue source for breakdowns | Top-products/category revenue from `SalesInvoiceItems.TotalAmount` | Can disagree with ledger KPI | Derive breakdown revenue from ledger `PosSales` by product, or document invoice as canonical for item detail | Medium | Open |
| F3 | Real cache or remove | `GetOrCreateAsync` no-op | Misleading; no perf benefit | Use `IMemoryCache` with the declared TTLs, or delete | Low | Open |
| F4 | Enum-guard SourceType | Magic strings in Daily Close/sales-analysis | Silent break on rename | Introduce `JournalSourceType` enum + compiler checks | Medium | Open |
| F5 | Complete Cash Flow | Investing/Financing/NonCash empty | Incomplete statement | Wire investing/financing from fixed-asset/treasury ledgers; depreciation accounts | Medium | Open |
| F6 | Stabilize CoA-prefix logic | "10/12/13/20" + range convention hardcoded | Fragile to renumbering | Drive from account `IsClearing`/`IsCurrent`/`AccountClass` flags | Medium | Open |
| F7 | Verify/implement export | **Confirmed: only CommerceHub vendor CSV exists** (hand-rolled StringBuilder, in-memory, no library, reads `CommerceHubFinancialTransactions`); NO export for SOA/AR-AP/P&L/TB/dashboard | Large reports unexportable; in-memory CSV risks OOM at scale | Add streaming CSV/Excel (EPPlus/chunked `IAsyncEnumerable`) for all core reports; stream vendor CSV | Medium | Open (verified) |
| F8 | Materialized dashboard cache | Per-request full re-aggregation; **confirmed no Hangfire precompute / no snapshot tables** | Load under many tenants | Precompute KPI snapshots via Hangfire into a read table | Low | Open (verified) |
| F9 | Cross-channel revenue consolidation | PosSales vs EcommerceSales separate | No unified revenue view | Unified revenue report spanning both accounts | Low | Open |
| F10 | Reconcile parallel ledgers | 4 ledgers, manual `RebuildPartyBalanceAsync` only | Drift between party/detail/marketplace ledgers | Single reconciliation job + shared balance writer; remove `SupplierTransactions`/`CustomerTransactions` shadow ledgers | High | Open |
| F11 | Unify conversion-rate definition | Storefront/affiliate use Mongo-based conversion; SQL ledger `Earned` is authority | Disagreement during projection lag | Derive conversion from SQL attribution, not Mongo event counts | Medium | Open |
| F12 | Expose CommerceHub platform overview to admin | Platform overview SuperAdmin-only | Admin blind to platform commission/payables | Add admin platform-overview route or document intent | Low | Open |


## Report Lineage Matrix

"Where does this number actually come from?" — traced to original runtime, SQL tables,
aggregation, and final report.

| Number | Original Runtime | Original SQL Tables | Aggregation Steps | Transformations | Projection? | Final Report |
|---|---|---|---|---|---|---|
| Revenue (merchant) | Accounting (GL posting on sale) | `LedgerEntries`(`AccountCodes.PosSales`) + `JournalEntries` | SUM(Credit−Debit) where Posted\|Reversed | bucket by `JournalDate` | none | Dashboard `stats.TotalRevenue` / `sales-analysis.TotalRevenue` |
| Revenue (storefront) | Accounting (ecommerce sale) | `LedgerEntries`(`EcommerceSales`) | SUM(Credit−Debit) by `SourceId` | net per order | none (Mongo events separate) | Storefront dashboard `Revenue` |
| Profit / Net Profit | Accounting (COGS + revenue posting) | `LedgerEntries`(`PosSales`,`CostOfGoodsSold`) | Revenue−Cost | daily merge in memory | none | `sales-analysis.TotalProfit`, `financial dashboard.NetProfit` |
| Inventory Value | Inventory runtime (stock receipt) | `ProductVariantStocks`(`Quantity+DamagedQuantity`)*`Variant.CostPrice` | SUM | filter active/non-deleted | none | Dashboard `InventoryValue`, `inventory-valuation.TotalInventoryValue` |
| Customer Balance | Accounting (party posting) | `AccountLedgers`(Customer) | SUM(Debit−Credit) | `RunningBalance` carried | none | `statement-of-account`, `dashboard.OutstandingReceivables` |
| Supplier Balance | Accounting (party posting) | `AccountLedgers`(Supplier) | SUM(Credit−Debit) | `RunningBalance` carried | none | `statement-of-account`, `dashboard.OutstandingPayables` |
| Cash Balance | Treasury / cashbox posting | `LedgerEntries`(cashbox prefix) `BalanceAfter` | latest per account | opening/closing | none | `daily-close.OpeningCashBalance/ExpectedClosingCash` |
| Top Product Revenue | Sale lifecycle (invoice) | `SalesInvoiceItems.TotalAmount` | SUM by Product | grouped | none | Dashboard `top-products`, `sales-analysis.TopProducts` |
| AR Aging buckets | Accounting (party posting) | `AccountLedgers`(Customer) | group by `TransactionDate` range | `outstandingFactor` scaling | none | `accounts-receivable-aging` |
| GL line balance | Accounting (journal) | `LedgerEntries` | Debit−Credit by normal balance | `NormalBalance` from `AccountType` | none | `general-ledger`, `trial-balance` |
| Storefront visits/funnel | Storefront event ingest | Mongo `analytics_events` | `$facet` group/count | event-type counts | **Mongo** | Storefront dashboard `Visits`/`Funnel` |
| Purchase cost variance | Purchase lifecycle (invoice) | `PurchaseInvoiceItems.UnitPrice` | MIN/MAX/AVG | variance % | none | `cost-analysis` |

**Critical lineage divergence:** "Revenue" appears from BOTH the ledger (`PosSales`) and the
invoice items (`TotalAmount`). The dashboard KPI uses the ledger; top-products/category use
the invoice. They share the same sale but different derivation paths (see TD2).

---

## Decision Matrix

Reverse-engineered reporting decisions and their failure behavior (evidence-backed).

| Decision | Business Rule | Authority | Failure Behaviour | Implementation Evidence |
|---|---|---|---|---|
| Can reports read Mongo? | Only storefront behavioral events | Mongo `analytics_events` (events only) | Money never from Mongo | `StorefrontAnalyticsDashboardService.cs:26,95-140`; `StorefrontAnalyticsAuthorityReader.cs:31-43` |
| Can reports read Redis? | No | — | n/a (0 hits) | grep: 0 hits in reporting controllers |
| Can reports read Elasticsearch? | No (ES = search authority) | — | n/a (0 hits) | grep: 0 hits in reporting controllers |
| Can reports calculate profit from projections? | No | SQL ledger | blocked by `ILedgerOnlyService` | `ILedgerOnlyService.cs:23`, `LedgerIsolationTests.cs:29-81` |
| Can historical reports change? | Yes (if ledger corrected) | Immutable ledger re-aggregation | intended accounting behavior | `GetAccountBalanceAsOfAsync` `FinancialReportService.cs:601-635` |
| Can deleted records disappear? | Yes (soft-delete filtered) | `IsActive && !IsDeleted` | excluded from operational reports | `DashboardController.cs:122-134`, `ReportsController.cs:263-269` |
| Can inventory valuation be recalculated? | Yes (always on read) | `ProductVariantStocks` | recomputed per request | `ReportsController.cs:263-283` |
| Can KPIs differ from reports? | Yes (hybrid sources) | ledger vs invoice | possible divergence | TD2; `DashboardController.cs:70-105` vs `:345-364` |
| Can reports ignore soft-deleted rows? | Yes (operational) | business tables | filtered | `ReportsController.cs:348-349` |
| Can financial reports read business tables? | No (enforced) | ledger only | CI/CD build fails | `ForbiddenBusinessDbSets.cs:17-66`, `LedgerIsolationTests.cs` |
| Can a report mutate the ledger? | No (except 2 scoped postings) | Accounting lifecycle | delegated to `ILedgerService`+outbox | `StatementOfAccountController.cs:264,535` |
| Are reversed journals included? | Yes (net correctly) | Posted\|Reversed | reversals offset originals | `FinancialReportService.cs:59-62` |

---

## Executive Reporting Flow (one complete trace)

POS Sale → Inventory → Accounting → Ledger → Reporting SQL → Aggregation → Dashboard →
Executive KPI → Export.

1. **POS Sale (POS Runtime).** Merchant rings a sale; `SalesInvoice` created, stock
   decremented by Inventory runtime (SQL authority `ProductVariantStocks`).
   *Runtime owner: POS/Inventory. SQL authority: `SalesInvoices`, `ProductVariantStocks`.
   Projection: none. Aggregation: n/a. Consistency: strong. Replay: n/a. Failure: sale
   fails closed if stock/reservation fails.*

2. **Inventory (Inventory Runtime).** `ProductVariantStocks.Quantity` reduced;
   `StockMovements` appended. *Runtime owner: Inventory. SQL authority:
   `ProductVariantStocks`, `StockMovements`. Projection: none. Consistency: strong.*

3. **Accounting (Accounting Lifecycle).** On sale completion, the accounting lifecycle
   posts a `JournalEntry` (SourceType `SalesInvoice`) with `LedgerEntries` to
   `AccountCodes.PosSales` (credit) + COGS/AR/cashbox. *Runtime owner: Accounting
   (`ILedgerService`). SQL authority: `JournalEntries`, `LedgerEntries`. Projection: none.
   Consistency: Posted\|Reversed. Replay: via outbox. Failure: fail-closed (unresolved
   payment blocks outward money per Shopixy rules).*

4. **Ledger (GL).** `LedgerEntries` are immutable, journal-derived, carry `BalanceAfter`.
   *Runtime owner: Accounting. SQL authority: `LedgerEntries`. Consistency: immutable.*

5. **Reporting SQL (FinancialReportService / DashboardController).** Reads
   `LedgerEntries` for `PosSales`; `RepeatableRead`. *Runtime owner: Reporting. SQL
   authority: ledger (enforced). Projection: none. Aggregation: SQL GroupBy. Consistency:
   strong. Replay: n/a (reads authority). Failure: returns 500 on exception
   (`FinancialReportsController.cs:289-301`).*

6. **Aggregation.** SUM(Credit−Debit) by day/account; KPI computed
   (`DashboardController.cs:70-105`). *Runtime owner: Reporting. Aggregation: SQL.
   Consistency: strong.*

7. **Dashboard (Executive KPI).** `stats` returns revenue/total products/low stock/
   customers/receivables/payables/inventory value (`DashboardController.cs:182-197`).
   *Runtime owner: Reporting. Consistency: strong (cache no-op).*

8. **Executive KPI → Export.** KPI presented; export path not verified in primary reads
   (TD8/F7). *Runtime owner: Reporting. Failure: if export absent, JSON only.*

Cross-cutting: every stage is tenant-scoped (`ITenantContext.RequiredTenantId`); the ledger
is the single financial truth; no stage reads a projection for money.

---

## Architectural Synthesis

The Reporting Runtime is best understood not as a pile of endpoints but as the **read
surface of the platform's CQRS split, specialized for decision intelligence**. Its
coherence comes from five cross-cutting patterns verified across every peer runtime:

1. **SQL Authority is the only financial truth.** Whether merchant (POS Admin), CommerceHub
   (vendor finance), or Storefront (ecommerce), every money number resolves to the
   PostgreSQL ledger. The storefront surface makes this explicit: behavioral events live in
   Mongo, but `StorefrontAnalyticsAuthorityReader` forbids Mongo/Redis from substituting for
   the SQL ledger on financial metrics (`StorefrontAnalyticsAuthorityReader.cs:31-43`). This
   is the same "PostgreSQL authoritative; Mongo/Redis/ES derivative" rule documented in the
   Shopixy platform authority.

2. **CQRS read/write separation, enforced.** The write side (Accounting lifecycle, Inventory
   runtime, Order/Sale/Purchase lifecycles, Payment lifecycle) owns mutation; the Reporting
   Runtime owns aggregation. Financial reporting goes further: `ILedgerOnlyService` makes a
   business-table read a compile/CI failure (`ForbiddenBusinessDbSets.cs`,
   `LedgerIsolationTests.cs`). This is the strongest form of the platform's "lifecycle
   owners; direct writes violate unless reviewed" rule.

3. **Four-ledger accounting model (not two).** The immutable GL (`LedgerEntries`, derived-
  on-read, RepeatableRead) and the maintained party sub-ledger (`AccountLedgers`,
  `RunningBalance`) are the merchant pair. But two *more* ledgers run in parallel:
  `SupplierTransactions`/`CustomerTransactions` (party *detail*, a third read model that can
  drift from `AccountLedgers` — `StatementOfAccountController.cs:1077-1109`) and
  `CommerceHubFinancialTransactions` (the marketplace/vendor ledger, fully separate from the
  merchant GL). Party balances are reconciled only by a manual `RebuildPartyBalanceAsync`
  (`LedgerService.cs:105-194`). The Reporting Runtime reads whichever ledger owns the
  question — but the *platform* carries four independent balance writers with no continuous
  reconciliation (see TD12/F10). This is the single largest structural risk in the runtime.
4. **Read-only runtime (with two scoped exceptions).** Reporting does not corrupt the data it
  describes. The only mutations — party opening-balance and adjustment postings — delegate
  to the Accounting lifecycle + outbox, preserving fail-closed, lifecycle-owned mutation
  (`StatementOfAccountController.cs:264,535`).

5. **Decision intelligence over UI.** The runtime answers business questions (revenue,
   profit, balances, valuation, aging, trends, KPIs) from one source each, aggregated in
   SQL, tenant-scoped, and consistent. Where it reads operational tables (invoices, stock),
   it does so because those are the operational authority for the question asked — never to
   recompute money that the ledger already owns.

**Why it was architected this way:** the platform's risk model is financial correctness. A
reporting number that disagrees with the ledger is worse than a slow report. So the design
trades read latency (per-request re-aggregation, no-op cache) for single-source truth
(ledger-only financial reporting, SQL-authority storefront money, no projection reads for
money). The resulting architecture is verbose at the edges (many controllers, many DTOs)
but coherent at the center: **one ledger, one truth, read-only aggregation, tenant-scoped,
projection-free for money.**

---

## Certification

- **Confidence:** High for merchant financial/operational/executive reporting (read directly
  from `FinancialReportService`, `ReportsController`, `DashboardController`,
  `StatementOfAccountController`, `SalesController`, `StorefrontAnalyticsDashboardService`,
  `StorefrontAnalyticsAuthorityReader`, `ILedgerOnlyService`, `ForbiddenBusinessDbSets`,
  `LedgerIsolationTests`).
- **Completeness:** Core merchant reporting = complete and primary-read. Affiliate reporting,
  CommerceHub financial dashboard, export, and snapshot specifics were **delegated to
  background subagents and are now merged** (Affiliate: dual Mongo/SQL-engine with
  `AffiliateCommissionLedger` authority; CommerceHub: dedicated `CommerceHubFinancialTransactions`
  ledger, EGP-only; Export: only CommerceHub hand-rolled CSV confirmed; Snapshot: confirmed
  none + no Hangfire precompute). Remaining partial reads: full Sales Summary
  discount/tax/cashier/store breakdown internals (referenced by line range, not every line).
- **Verified:** ledger-only enforcement (`ILedgerOnlyService` scoped to `FinancialReportService`
  only), zero Mongo/Redis/ES in the five merchant reporting controllers, two/ four-ledger
  model, point-in-time re-aggregation, hybrid KPI authority, tenant scoping, soft-delete
  filtering, no-op cache, no export runtime for merchant reports, no persisted snapshots.
- **Missing / to verify (residual):** (a) full Sales Summary discount/tax/cashier/store
  breakdown *internals* (line ranges cited, not every allocation line); (b) live
  behavior/numbers not executed (static reverse-engineering only — no running instance).
- **Technical debt summary:** TD1 (cost notions), TD2 (dual revenue source), TD11 (cost-field
  inconsistency Variant.CostPrice vs Product.PurchasePrice vs invoice UnitPrice), TD12 (four
  parallel ledgers, manual-only reconciliation), TD15 (CommerceHub commission 3-way derivation)
  are the highest-impact items; all are correctness-drift or structural risks, not live
  incorrectness in the common path.
