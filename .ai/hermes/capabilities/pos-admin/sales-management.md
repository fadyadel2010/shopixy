# POS Sales Management — Capability Reverse Engineering

Source of truth: implementation in `PointOfSale.API/Controllers/SalesController.cs`,
`PointOfSale.API/Controllers/SalesReturnsController.cs`, `PointOfSale.Core/Entities/SalesInvoice*.cs`,
`PointOfSale.Infrastructure/Accounting/CommerceAccountingService.cs`,
`PointOfSale.API/Services/Inventory/ProductVariantStockMutationSupport.cs`, and supporting types.
This is NOT Storefront Orders, NOT Checkout, NOT CommerceHub Orders, NOT Customer Ecommerce. POS Sales is an
independent ERP capability with its own tables, controllers, accounting outbox events, inventory mutations,
and return flow.

All facts below are proven by the implementation at the time of this reverse-engineering pass. Where the
implementation contradicts the existing certification doc (`docs/architecture/pos-sales-authority-certification-findings.md`),
the implementation wins and the contradiction is reported in Documentation Validation.

--------------------------------------------------------------------------------
# Purpose
--------------------------------------------------------------------------------

POS Sales exists to record a cashier-driven point-of-sale transaction at a physical/warehouse terminal:
select products (variants, units), price them, apply discounts/tax, deduct inventory from a specific
warehouse, capture payment (cash/card/mixed), print a receipt, post the financial journals, and — if
needed — reverse the transaction through a sales return. It is the bounded historical authority for the
sale's own line items, totals, inventory movement, customer receivable, and accounting journals.

Business problems it solves:
- Authoritative recording of an in-store sale as a single committed ERP event.
- SQL-authoritative pricing/tax/discount (client-supplied totals are recomputed, never trusted).
- SQL-authoritative inventory deduction keyed to a warehouse + variant.
- Customer receivables tracking for on-account sales.
- Financial ledger recognition (revenue + COGS) for the sale.
- Safe reversal of completed sales via a separate returns authority (no destructive rewrite).

What it owns:
- The `SalesInvoice` aggregate (header + `SalesInvoiceItem` lines) and its immutable invoice number.
- The line-level cost-basis snapshots used for COGS and margin.
- The inventory deduction (`StockMovement` of type `Sale`) and its `ProductVariantStock` mutation.
- The customer ledger entry / `CustomerTransaction` for the sale (when a customer is attached).
- The accounting journals posted through `ICommerceAccountingService.PostPosSaleAsync`.
- The `SalesRequestIdempotency` claim that makes create/update idempotent.
- The sales return intent (`Return` of type `SalesReturn`) and the bounded refund accounting.

--------------------------------------------------------------------------------
# Business Scope
--------------------------------------------------------------------------------

Reverse engineered entities/operations that belong to POS Sales:

- POS Sale (a `SalesInvoice` created via `SalesController.CreateSale`, POST `api/sales`).
- Sale line item (`SalesInvoiceItem`): product, variant, unit, quantity, conversion factor, base quantity,
  unit price, line discount, tax rate, tax amount, total, and immutable cost snapshots.
- Invoice numbering (`INV-yyyyMMdd-NNNN` via the `PosSalesInvoiceNumberSeq` sequence on Postgres,
  fallback to locked max-suffix scan otherwise).
- Customer selection (optional `CustomerId`; when present the full amount is debited to the customer
  ledger and any payment credits the receivable). Walk-in = no customer (null `CustomerId`).
- Sales representative attribution (optional `SalesRepresentativeId`).
- Warehouse selection (required; the inventory is deducted from this warehouse only).
- Pricing: server-authoritative. `PrepareSaleItemsAsync` resolves the unit price from the product unit's
  configured `SellingPrice`, else from `variant.Price * conversionFactor`. Client `UnitPrice`/`TaxRate`
  are overwritten.
- Discount: per-line item discount (must be ≤ line subtotal) plus invoice-level percentage or fixed
  discount (`ApplySaleTotals`), bounded so discountable subtotal never goes negative.
- Tax: per-line `TaxRate`. If `Product.PriceIncludesTax` is true, tax is 0 on the line; otherwise
  `taxableAmount * TaxRate / 100`.
- Inventory deduction: `ProductVariantStock.Quantity -= baseQuantity` for each line, locked deterministically.
- Receipt: read-only projection `ReceiptData` from `GET api/sales/receipt/{id}` (no separate receipt
  persistence; it is derived from the invoice on demand).
- Payment at sale: Cash, Card, Mixed (server-split). Captured inside the create transaction.
- Customer ledger + transaction rows for sale/return when a customer is present.
- Sales return: separate bounded authority `SalesReturnsController` (POST `api/salesreturns`), creates a
  `Return` of type `SalesReturn` and restores inventory.
- Accounting posting: revenue journal + COGS journal via `CommerceAccountingService.PostPosSaleAsync`.
- Idempotency / replay safety: `SalesRequestIdempotency` per tenant/key/operation/hash.

NOT owned by POS Sales (proven by absence):
- No "held sale" / "draft sale" persistence. The `InvoiceStatus.Draft` value exists in the enum but is
  NEVER assigned by the API. `UpdateSale` requires `Status == Draft` to rewrite, but since sales are
  created `Completed` and never transition to `Draft`, the rewrite path is effectively dead in practice.
  Held sales are a FRONTEND-ONLY concept stored in browser `localStorage` (`utils/localStorage.js`
  `saveHeldSale`/`getHeldSales`); they never touch the server. Treat "Held Sale" as client cart state, not
  a server entity.
- No Shift entity, no Opening/Closing balance, no Cash Drawer entity in the POS Sales path.
  `PointOfSale.Core/Entities/Cashbox.cs` and `CashboxController` exist but are a SEPARATE treasury
  capability; a POS sale does NOT create a `CashboxTransaction`. Cash sales post to a warehouse-scoped
  cashbox ledger ACCOUNT (`ResolveWarehouseCashboxAccountCodeAsync`), not to the `Cashbox` table.
- No Terminal entity. The "terminal" is an operational surface in the Web UI (`POS.jsx`), not a server
  authority.
- No standalone payment-application endpoint. There is no incremental "apply payment to invoice" API;
  payment is settled at sale creation. The `SalesInvoicePayment` table/entity exists (DbSet
  `SalesInvoicePayments`) but is NEVER written by the sale or return paths — payments are represented by
  the invoice header `PaymentMethod`/`PaidAmount`/`CashAmount`/`CardAmount` and by customer-ledger
  transactions. Any consumer assuming `SalesInvoicePayment` rows are populated will be wrong.
- No offline queue on the server. Offline support, if any, is client-side only.
- No status transition to Cancelled / PartiallyReturned / FullyReturned. The `InvoiceStatus` enum has
  those values but the implementation NEVER assigns them. A completed sale is reversed only by creating a
  separate `Return`, not by flipping the invoice status. `Status == Completed` is the only observed value.
- No WhatsApp / notification / email dispatch on sale. No `IEventPublisher` publish occurs in the sale or
  return path (the field is injected but unused in these controllers).

--------------------------------------------------------------------------------
# Authority Boundaries
--------------------------------------------------------------------------------

- Sales Truth: `SalesInvoice` + `SalesInvoiceItem` rows in PostgreSQL. Authoritative.
- Receipt: Derived projection (`ReceiptData`) of the PostgreSQL invoice. Presentation only; not persisted
  as its own table.
- Invoice Number: Assigned by `GenerateInvoiceNumberAsync` (Postgres sequence `PosSalesInvoiceNumberSeq`,
  else locked max-suffix). Authoritative, monotonic per day per tenant.
- Inventory Deduction: `ProductVariantStock` row + `StockMovement` (type `Sale`). Authoritative
  (PostgreSQL). Source of truth for stock is `ProductVariantStock`, not a denormalized column.
- Pricing: Recomputed server-side from `Product`/`ProductVariant`/`ProductUnit`. DTO `UnitPrice`,
  `TaxRate` are EVIDENCE, overwritten by server.
- Customer: `Customers` table; a sale debits the customer receivable via `LedgerService`. Balance is
  rebuilt from ledger history, not from a stored customer balance field.
- Payment: Settled at sale creation; recorded on the invoice header + customer ledger. No separate payment
  table is written. Payment allocation/cash-change is server-validated.
- Accounting: `JournalEntry` rows posted by `CommerceAccountingService` (idempotent by
  `SALE:{TenantId}:{InvoiceId}[:opKey]`). Authoritative.
- Shift: NOT a POS Sales concept. Out of scope / not owned.
- Cash Drawer / Cashbox: Separate treasury capability. POS Sales posts cash to a cashbox LEDGER ACCOUNT,
  not to a `Cashbox`/drawer record. Authority for cash drawers lives in `CashboxController`.
- Warehouse: Selected per sale; the sale deducts from exactly that warehouse. `Warehouse` is authoritative
  for location; the sale only references it.
- Analytics: POS sale rows feed analytics only through downstream projections; the sale itself does not
  write analytics.
- Notifications: Not produced by POS Sales.

Evidence vs Authority (fail-closed rule): any client-supplied total, unit price, tax rate, mixed-payment
split, or return price/tax is EVIDENCE ONLY and is recomputed/overwritten or rejected. The SQL
recomposition + row locks + idempotency + accounting-outbox are the AUTHORITY.

--------------------------------------------------------------------------------
# Sales Lifecycle (reverse engineered)
--------------------------------------------------------------------------------

CreateSale (POST api/sales)
  1. Require an idempotency key: `X-Payment-Attempt-Id` or `X-Pos-Cart-Id` (one is mandatory).
  2. Claim `SalesRequestIdempotency` row inside the transaction (request-hash compare; replay returns
     stored response).
  3. Validate warehouse (active, same tenant) and optional customer (active, same tenant).
  4. Generate invoice number.
  5. `PrepareSaleItemsAsync`: server-resolve unit price/tax, validate unit conversion, validate line
     discount ≤ line subtotal.
  6. Lock stock rows deterministically (WarehouseId, VariantId), validate available qty (no negative,
     allowNegativeStock:false), deduct `BaseQuantity`, append `StockMovement` (Sale).
  7. `ApplySaleTotals`: compute subtotal, line discounts, tax, invoice discount, total.
  8. `ValidateAndNormalizePaymentAmounts`: enforce payment method rules; set PaidAmount/CashAmount/CardAmount.
  9. If customer present: `LedgerService.CreateLedgerEntry` (Sale debit) + `CustomerTransaction`; if a
     payment is made, a PaymentReceived credit entry is also written (clamped to invoice total).
 10. Enqueue accounting outbox (`EnqueuePosSaleAsync`) and synchronously `PostPosSaleAsync`; mark outbox
     processed in same transaction (background worker is only recovery).
 11. Publish committed stock-sync outbox events (`ProductVariantStockChangedEvent`).
 12. Complete idempotency + commit. Audit log written after commit (best-effort).
 13. Status is set to `Completed` immediately. The sale is complete in one transaction.

Return (POST api/salesreturns) — separate authority, does NOT mutate the original invoice status:
  - Write-lock original invoice, require `Status == Completed`, customer match, total > 0.
  - Build original-invoice line snapshots (variant/unit/price/tax/cost); ignore client price/tax overrides.
  - Cumulatively bound returned base qty and total against prior SQL returns for that invoice.
  - Restore inventory (StockMovement type `SalesReturn`), optionally credit customer ledger.
  - Enqueue `EnqueuePosRefundAsync`; synchronous refund accounting; advance invoice
    `AccountingMutationVersion`.

Delete (DELETE api/sales/{id}, Owner/Admin): only allowed when `CanDeleteSalesInvoiceAsync` passes —
i.e. NOT Completed (Draft only, but Draft is never produced) and no associated returns. In practice
completed invoices CANNOT be deleted; they are reversed via returns. Delete reverts stock + customer
ledger and removes the invoice.

Read: GET api/sales/{id}, GET api/sales (paged), GET api/sales/receipt/{id}.

--------------------------------------------------------------------------------
# Implementation Discovery
--------------------------------------------------------------------------------

Frontend (operational surface, NOT authority):
- `PointOfSale.Web/src/pages/POS.jsx`, `usePOSCart.js`: cart, customer select, discount, payment modal.
- `HoldSalesModal.jsx` + `utils/localStorage.js`: held sales are browser-localStorage only.
- `SalesHistory.jsx`, `SaleDetailsModal.jsx`, `SalesReturns.jsx`: read/return UIs.
- `PaymentModal.jsx`, `OpenCloseCashboxModal.jsx`: payment entry and the separate cashbox UI.
These call the `api/sales` and `api/salesreturns` endpoints. They are presentation; all business rules
are enforced server-side.

Controllers / Endpoints:
- `SalesController` (route `api/sales`):
    - POST            -> CreateSale            (idempotency key required)
    - PUT {id}        -> UpdateSale            (requires Status==Draft; not reachable in practice)
    - DELETE {id}     -> DeleteSale            (Owner/Admin; validation + posted-journal guard)
    - GET {id}/can-delete -> CanDeleteSale     (Owner/Admin/Manager)
    - GET {id}        -> GetSale
    - GET             -> GetSales              (paged, search, date range)
    - POST {id}/return -> ProcessReturn        (RETIRED: returns 410; use api/salesreturns)
    - GET receipt/{id}-> GetReceipt
- `SalesReturnsController` (route `api/salesreturns`):
    - GET, GET {id}, GET invoice/{invoiceId}, POST -> CreateSalesReturn (hardened, idempotent).
- `SalesRepresentativesController` (`api/salesrepresentatives`) and `SalesRepReportsController`
  (`api/salesrepreports`): related but separate capabilities (rep master data + rep reporting), not the
  sale transaction itself.

Application / POS / Sales / Receipt Services:
- `SalesController` + `SalesReturnsController` contain the sale/return orchestration inline (no separate
  "SaleService" façade). They use:
  - `ILedgerService` (`LedgerService`): creates sale/return ledger entries and rebuilds party balance.
  - `IPartyBalanceMaintenanceService` (same `LedgerService`): `RebuildPartyBalanceAsync` after reverts.
  - `ISaleFinancialIntegrityService` (`SaleFinancialIntegrityService`): validates that no posted journals
    or unsafe accounting-outbox rows block a sale mutation (delete/update). Uses explicit row locks and a
    shared lock-order contract with `OrderStatusSyncService` to avoid deadlocks.
  - `IValidationService` (`ValidationService`): `CanDeleteSalesInvoiceAsync` (blocks Completed + returns).
  - `IAccountingOutboxService`: `EnqueuePosSaleAsync` / `EnqueueSaleReversalAsync` / `EnqueuePosRefundAsync`.
  - `ICommerceAccountingService` (`CommerceAccountingService`): `PostPosSaleAsync` (revenue + COGS journals).
  - `IFinancialPostingService`: actual journal insert (idempotent by key).
  - `IEventPublisher`: injected but UNUSED in sale/return paths.
  - `IAuditService`: best-effort audit log after commit.
  - `ProductVariantStockMutationSupport` (internal static): the inventory mutation authority.

Entities (PostgreSQL tables):
- `SalesInvoices` (SalesInvoice): TenantId, InvoiceNumber, InvoiceDate, CustomerId, WarehouseId, Status,
  Subtotal, DiscountAmount, DiscountPercentage, TaxAmount, TotalAmount, PaidAmount, PaymentMethod,
  CashAmount, CardAmount, Notes, SalesRepresentativeId, AccountingMutationVersion.
- `SalesInvoiceItems` (SalesInvoiceItem): line fields + BaseQuantity + CostPriceSnapshot + CostAmountSnapshot.
- `SalesInvoicePayments` (SalesInvoicePayment): DbSet exists but is NEVER populated by the sale/return flow.
- `SalesRequestIdempotency` (SalesRequestIdempotency): tenant/key/operation/requestHash + claim token +
  stored response. The replay-safety anchor.
- `Returns` + `ReturnItems`: the sales-return aggregate (ReturnType = SalesReturn).
- `StockMovements`, `ProductVariantStocks`: inventory side effects.
- `AccountLedgers`, `CustomerTransactions`, `JournalEntries`, `AccountingOutboxMessages`: financial side
  effects.
- `AuditLogs`: best-effort audit rows.

Repositories: standard EF `ApplicationDbContext` sets; no custom repo abstraction for the sale write path.

Inventory Integration:
- `ProductVariantStockMutationSupport.LockMultipleStocksAsync` locks rows in deterministic (WarehouseId,
  VariantId) order, validates available quantity with `allowNegativeStock:false`, then mutates. Every
  mutation appends a `StockMovement` and stages a `StockSyncOutboxMessage` (`PublishCommittedStockEventsAsync`)
  BEFORE commit. The outbox is later consumed by `ProductStockChangedConsumer` (Mongo projection) and
  `ProductStockPriceSearchIndexConsumer` (search index) — those are downstream projections, not POS Sales.

Accounting Integration:
- `CommerceAccountingService.PostPosSaleAsync` posts TWO journals:
    - Journal 1 (SourceType `SalesInvoice`, key `SALE:{Tenant}:{Invoice}`): debit Cashbox-ledger account
      (cash/mixed) or VisaClearing (card) or AccountsReceivable (credit path — but credit is rejected at
      sale create, so only cash/card/mixed reach posting), credit `PosSales` revenue.
    - Journal 2 (SourceType `SalesInvoice_COGS`, key `COGS:{Tenant}:{Invoice}`): debit `CostOfGoodsSold`,
      credit `Inventory`, only when total cost > 0 (from line `CostAmountSnapshot`).
  The request executor posts synchronously and marks the outbox processed; the background
  `IAccountingOutboxProcessor` is recovery only.

Payment Integration:
- Payment is captured at sale creation. `ValidateAndNormalizePaymentAmounts` enforces:
    - Cash: paidAmount may exceed total (tendered cash); change = paid - total; no card portion.
    - Card: paidAmount must EQUAL total; no cash portion.
    - Mixed: cashAmount + cardAmount must EQUAL total (±0.01); both parts recorded.
    - Credit / BankTransfer: rejected by `EnsureSupportedPaymentMethod` (throws ValidationException).
- No external payment gateway, no Stripe/Paymob, no wallet/gift-card/loyalty provider is touched by POS
  Sales. Those belong to Checkout/Storefront.

Warehouse Integration:
- Warehouse is required and validated as active + same-tenant. Inventory is deducted from that warehouse
  only. Returns restore to the warehouse passed on the return request (must be valid).

Shift / Cash Drawer:
- No shift, no opening/closing balance, no cash-drawer entity in the POS Sales code path. Cash sales post
  to a warehouse-scoped cashbox LEDGER ACCOUNT (`ResolveWarehouseCashboxAccountCodeAsync`). The physical
  cashbox drawer lifecycle is owned by `CashboxController`/`Cashbox` — a separate capability.

Outbox / MassTransit / Mongo / Redis / Elasticsearch:
- Accounting outbox: `AccountingOutboxMessages` (PostgreSQL). Request path marks processed synchronously;
  background processor recovers.
- Stock sync outbox: `StockSyncOutboxMessages` (PostgreSQL) -> MassTransit event
  `ProductVariantStockChangedEvent` -> Mongo product-stock projection + Elasticsearch stock/price index.
  These are derivatives; POS Sales owns the PostgreSQL mutation, not the projections.
- Redis: not written by the sale path directly (no caching of sale truth observed).
- No direct POS-Sales Elasticsearch write; search is fed via the stock-sync consumer.

--------------------------------------------------------------------------------
# Business Rules (implementation-backed)
--------------------------------------------------------------------------------

1. Idempotency key mandatory for create: `X-Payment-Attempt-Id` or `X-Pos-Cart-Id`; format `^[A-Za-z0-9-]+$`,
   max 100 chars. Missing key -> 400.
2. Replay safety: tenant+key+operation+requestHash; a matching completed hash returns the stored response;
   a different hash on the same key -> 400 "Idempotency key reuse with different payload"; an in-flight
   claim (age < 5 min) -> 409 SALE_IN_PROGRESS. UPDATE also supports hash-recovery across keys.
3. Warehouse must be active and same-tenant, or 400 "Warehouse not found".
4. Customer, if supplied, must be active and same-tenant, or 400 "Customer not found".
5. Pricing is server-authoritative: unit price from `ProductUnit.SellingPrice` or `variant.Price *
   conversionFactor`; tax from `product.TaxRate`; `PriceIncludesTax` zeros the line tax.
6. Each line needs a resolvable, active variant; missing/!active variant -> 400.
7. Unit must be a configured `ProductUnit` (or the product's default unit) with positive conversion
   factor; otherwise 400 (no silent factor-1 fallback).
8. Per-line discount must be ≤ line subtotal; invoice discount must be ≥ 0 and ≤ discountable subtotal;
   otherwise ValidationException.
9. Inventory: stock locked deterministically; available quantity must satisfy `BaseQuantity`
   (`allowNegativeStock:false`); insufficient -> 409 with available/requested quantities.
10. Payment method restricted to Cash/Card/Mixed. Credit/BankTransfer -> ValidationException (fails closed).
11. Mixed payment: cash + card must equal server total (±0.01). Card must equal total. Cash may exceed
    total (change). Customer-ledger payment credit is clamped to min(paid, total).
12. Accounting failure is fatal: if `PostPosSaleAsync` fails, the whole create throws and rolls back
    (sale is not committed without its journals).
13. Returns require original invoice `Status == Completed`, customer match, total > 0, and original items
    present.
14. Return lines ignore client-supplied price/tax (server overrides from snapshot) and log a warning.
15. Return quantity cannot exceed sold base quantity (cumulative across prior returns); return total cannot
    exceed original invoice total (cumulative across prior returns).
16. Refund allocation must be full payout for Cash/Card/Mixed; `AccountCredit` credits the customer and
    requires a customer; partial payout that is neither full nor account-credit is rejected.
17. Delete blocked for Completed invoices (validation `INVOICE_POSTED`) and for invoices with returns
    (`INVOICE_HAS_RETURNS`). Owner/Admin only. A posted-journal guard (`PostedJournalsExistException`)
    further blocks delete after journals exist.
18. Cross-tenant isolation is enforced by tenant query filters + explicit same-tenant helper checks +
    tenant co-ownership guards; X-Tenant-Id is stripped and `RequiredTenantId` is used.
19. Currency rounded to 2 dp (MidpointRounding.AwayFromZero) at every total/payment boundary.
20. `AccountingMutationVersion` is a monotonic version on the invoice; every return (and update, if
    reachable) increments it so downstream accounting intents are auditable and stale outbox messages are
    rejected.

--------------------------------------------------------------------------------
# Sales State Machine (reverse engineered)
--------------------------------------------------------------------------------

Observed implementation reality (proven):
- Created  -> `InvoiceStatus.Completed` immediately and permanently. There is NO Draft transition in the
  running system (Draft is only checked as an input precondition for the unreachable UpdateSale rewrite).
- Completed -> reversed ONLY by creating a `Return` (SalesReturn). The original invoice `Status` is NOT
  changed to Cancelled/PartiallyReturned/FullyReturned by the return flow (those enum values are never
  assigned anywhere in the sale/return code). So a returned sale still reads `Completed`.

Enum declares (but unused in mutation): Draft, Completed, Cancelled, PartiallyReturned, FullyReturned.

Therefore the effective machine for the running system is:

    (create, idempotent) ──▶ Completed ──▶ (sales return) ──▶ Completed [+ Return row, stock restored]
                            │
                            └──▶ (Owner/Admin delete, only if NOT Completed & no returns) ──▶ Deleted

There is NO Held/Draft/Voided/Cancelled terminal state produced by the API. "Held" is client localStorage.
"Void" does not exist as a server state. Do NOT model POS Sales as Draft→Held→Completed→Cancelled→Voided;
that lifecycle is not implemented.

--------------------------------------------------------------------------------
# Payment Behavior (reverse engineered)
--------------------------------------------------------------------------------

- Payments are settled AT SALE CREATION, inside the same transaction as inventory + accounting. There is no
  deferred/partial payment endpoint for POS sales in this capability.
- Supported methods: Cash, Card, Mixed. (`Credit` and `BankTransfer` are rejected — fail closed.)
- Cash: customer may tender more than total; `ChangeAmount = PaidAmount - TotalAmount` is returned in the
  response (computed, not stored separately). No negative stock, no negative payment.
- Card: `PaidAmount` must equal `TotalAmount`.
- Mixed: `CashAmount + CardAmount == TotalAmount` (±0.01); both stored on the header.
- Customer on-account: if `CustomerId` is set, the full `TotalAmount` is debited to the customer ledger as
  a `Sale` transaction, and any cash/card collected is credited as `PaymentReceived` (clamped to total).
  So a "credit sale" is expressed as: customer attached + a later return/settlement; the API's
  `PaymentMethod.Credit` is not an accepted sale method.
- For returns: refund method is Cash / Card / Mixed / AccountCredit. Cash/Card/Mixed must refund the full
  calculated return total; AccountCredit credits the customer and requires a customer account. The return
  does NOT re-open the invoice.
- No gift card, wallet, loyalty-point, or store-credit tender is handled by POS Sales. Those are
  Checkout/Storefront concerns.

--------------------------------------------------------------------------------
# Accounting (reverse engineered)
--------------------------------------------------------------------------------

Revenue recognition:
- `PostPosSaleAsync` posts a revenue journal (SourceType `SalesInvoice`, idempotency
  `SALE:{Tenant}:{Invoice}`). Debit side depends on method: Cash/Mixed->warehouse cashbox ledger account;
  Card->VisaClearing. Credit side: `PosSales` revenue account for `TotalAmount`.

COGS / Inventory cost:
- A second journal (SourceType `SalesInvoice_COGS`, key `COGS:{Tenant}:{Invoice}`) debits `CostOfGoodsSold`
  and credits `Inventory` by the summed line `CostAmountSnapshot` (only if > 0). The cost basis is the
  IMMUTABLE per-line snapshot captured at sale time (`CostPriceSnapshot`/`CostAmountSnapshot`), so margin
  is historical, not re-derived from current product cost.

Tax posting:
- Tax is included in the revenue amount (`TotalAmount` already includes tax). No separate tax-jurisdiction
  journal was observed beyond the revenue line; tax is a component of the sale total, not a split
  liability journal in the inspected path.

Cash drawer / shift:
- Cash sales debit the warehouse cashbox LEDGER ACCOUNT (resolved per warehouse), not a `Cashbox` row.
  There is no shift-close or drawer reconciliation inside POS Sales.

Supplier independence:
- POS Sales posts to `Inventory`/`CostOfGoodsSold` (COGS) but does not touch supplier payables or purchase
  accounting. Supplier/Purchase is a separate capability.

Mutation versioning / fail-closed:
- `AccountingMutationVersion` increments per return (and per update if reachable). Accounting outbox rows
  carry the version; `SaleFinancialIntegrityService` refuses a mutation while posted journals exist
  (delete with `JournalPolicy.RejectExisting`) or while an outbox row is claimed/retrying/failed.
- Synchronous posting + same-transaction outbox-mark-processed means the background worker is recovery, not
  a second financial authority. Replays of the outbox are idempotent by journal idempotency key.

--------------------------------------------------------------------------------
# Cross-System Effects (reverse engineered)
--------------------------------------------------------------------------------

What POS Sales CHANGES (owns):
- `SalesInvoices`, `SalesInvoiceItems`: created/updated (Completed only).
- `ProductVariantStocks`: decremented on sale, incremented on return (warehouse-scoped, variant-scoped).
- `StockMovements`: appended (Sale on sale, SalesReturn on return).
- `AccountLedgers` + `CustomerTransactions`: customer receivable for sale/return (if customer attached).
- `JournalEntries`: revenue + COGS for the sale; refund reversal for returns.
- `AccountingOutboxMessages`: enqueued + synchronously marked processed.
- `SalesRequestIdempotency`: claim row per create/update/return.
- `AuditLogs`: best-effort sale/return audit rows.
- `Returns` + `ReturnItems`: created on return.
- `StockSyncOutboxMessages`: staged for stock sync.

What POS Sales does NOT change (proven):
- Products: read-only pricing/unit/variant reference; no product mutation.
- Pricing: reads product/unit/variant prices; does not write pricing rules.
- Customers: only mutates the customer LEDGER balance (via ledger service), not customer master attributes.
- Payments (gateway/Checkout): no payment-intent, no Stripe/Paymob, no wallet. Payment is local cash/card.
- Warehouse master: read-only reference.
- Analytics: no direct analytics write; feeds only via downstream projections.
- Notifications / WhatsApp: none emitted from the sale/return path.
- Search (Elasticsearch): not written directly; fed via stock-sync consumer.
- Identity / Auth: only reads current user id/name for audit; no identity mutation.
- CommerceHub / Affiliate / Storefront / Orders: independent. POS Sales shares NO lifecycle with them. A
  POS sale does not create a CommerceHub order, an affiliate commission, or a storefront order.
- Returns: a POS return is a `Return` (SalesReturn); it does NOT mutate the original invoice status and
  does NOT create a negative `SalesInvoice` (the legacy negative-invoice return is retired/410).

Hidden coupling to watch:
- Inventory projection (Mongo/ES) is eventually consistent via the stock-sync outbox. If a sale commits but
  the outbox consumer lags, search/ Mongo stock can temporarily disagree with PostgreSQL
  `ProductVariantStock`. PostgreSQL is the authority; projections are derivatives.

--------------------------------------------------------------------------------
# Validation Checklist (operational)
--------------------------------------------------------------------------------

After sale created:
- `SalesInvoice.Status == Completed`; `InvoiceNumber` matches `INV-yyyyMMdd-NNNN`; exactly one idempotency
  claim row exists for the key with a stored 200 response.
After inventory deducted:
- `ProductVariantStock.Quantity` decreased by `BaseQuantity` per line; a `StockMovement` of type `Sale`
  exists per line; `StockSyncOutboxMessage` staged.
After receipt issued:
- `GET api/sales/receipt/{id}` returns totals equal to the invoice (`Total == TotalAmount`,
  `Change == PaidAmount - TotalAmount`). Receipt is derived, so any mismatch means the invoice row is wrong.
After payment:
- Header `PaidAmount`/`CashAmount`/`CardAmount` consistent with method rules; if customer attached, a
  `CustomerTransaction` Sale debit + (if paid) PaymentReceived credit exist; ledger balance rebuilt.
After accounting:
- Two `JournalEntry` rows exist (SalesInvoice revenue + SalesInvoice_COGS) with status Posted; accounting
  outbox row for `SALE:{Tenant}:{Invoice}` is `ProcessedAtUtc` set; `PostingResult.JournalId` non-empty.
After return:
- `Return` (SalesReturn) created with `ReturnNumber`; stock restored (`SalesReturn` movement); customer
  ledger credited if account-credit; invoice `AccountingMutationVersion` incremented; `AccountingOutboxMessages`
  for `PosRefund` processed; original invoice `Status` remains `Completed`.
After cancellation / delete:
- Only if invoice was not Completed and had no returns: stock reverted, customer ledger entries removed,
  balance rebuilt, invoice soft-deleted.

--------------------------------------------------------------------------------
# Common Failure Scenarios (reverse engineered)
--------------------------------------------------------------------------------

Sale duplicated:
- Cause: client retries without the same idempotency key, or reuses a key with a different payload.
- Investigation: check `SalesRequestIdempotency` for the key/operation; a same-hash row with a stored
  response means a legitimate replay (safe). A different-hash reuse is rejected (400). Two distinct invoices
  with the same `InvoiceNumber` would indicate a sequence/outbox breakdown (very unlikely on Postgres).

Receipt duplicated / wrong:
- Receipt is a pure projection of the invoice; "duplicate receipt" is a client print concern, not a data
  duplication. If totals look wrong, the invoice row itself is wrong (recompute path bug).

Inventory mismatch (sale deducted but stock wrong):
- Cause: concurrent sale without lock (should be prevented by `LockMultipleStocksAsync`); or outbox consumer
  lag making Mongo/ES disagree with PostgreSQL.
- Investigation first: PostgreSQL `ProductVariantStocks` + `StockMovements` (authority). Then
  `StockSyncOutboxMessages` for failed/unprocessed rows.

Cash drawer mismatch:
- POS Sales does NOT write the `Cashbox` table; cash posts to a cashbox ledger ACCOUNT. If a physical
  drawer count disagrees, investigate `CashboxController`/Cashbox lifecycle, not `SalesController`. The sale
  only guarantees the ledger-account debit.

Payment mismatch:
- Cash over-tender change is computed (`PaidAmount - TotalAmount`). If paid ≠ expected, check
  `ValidateAndNormalizePaymentAmounts` rules and the original request payload vs stored invoice header.

Accounting mismatch:
- If revenue/COGS journals missing: check `AccountingOutboxMessages` for the invoice; if processed but no
  journal, the synchronous post succeeded yet journal insert failed (should have rolled back the sale). If
  an outbox row is claimed/retrying/failed, `SaleFinancialIntegrityService` will block mutation until
  reconciled.

Wrong customer / wrong warehouse:
- Both are validated as active + same-tenant; a wrong customer/warehouse in a committed invoice means the
  idempotency replay returned a stored response for a different intent (payload-hash differs would have
  been rejected). Inspect the idempotency `RequestHash`.

Held sale lost:
- Held sales live in browser `localStorage`; clearing the browser loses them. There is NO server-held-sale
  recovery. This is by design (client cart state).

Shift closed / drawer:
- Not a POS Sales concept; irrelevant to sale integrity.

--------------------------------------------------------------------------------
# Debugging Guide (where to look first)
--------------------------------------------------------------------------------

1. Receipt discrepancy -> read the `SalesInvoice` + `SalesInvoiceItem` rows (receipt is derived from them).
2. Inventory discrepancy -> PostgreSQL `ProductVariantStocks` and `StockMovements` (authority); then
   `StockSyncOutboxMessages` for consumer lag/failure.
3. Payment discrepancy -> invoice header `PaymentMethod`/`PaidAmount`/`CashAmount`/`CardAmount` +
   `CustomerTransactions` (if customer) + `ValidateAndNormalizePaymentAmounts` rules.
4. Accounting discrepancy -> `JournalEntries` (SourceType `SalesInvoice` / `SalesInvoice_COGS`) +
   `AccountingOutboxMessages` (EventType `PosSale`) processed state.
5. Shift / drawer discrepancy -> OUT OF SCOPE for POS Sales; go to `CashboxController`/`Cashbox`. POS Sales
   only debits a cashbox ledger account.
6. Outbox / replay -> `SalesRequestIdempotency` (request hash, stored response) for create/update;
   `AccountingOutboxMessages` for financial replay; `StockSyncOutboxMessages` for inventory projection.
7. Mutation blocked (delete/update fails) -> `SaleFinancialIntegrityService`: check for posted
   `JournalEntries` (SalesInvoice/SalesInvoice_COGS) or unsafe `AccountingOutboxMessages`
   (claimed/retrying/failed).
8. Return problems -> `Returns`/`ReturnItems` (SalesReturn) + prior-return cumulative bounds + original
   invoice snapshot; client price/tax overrides are ignored (check warning logs).

--------------------------------------------------------------------------------
# Related Knowledge
--------------------------------------------------------------------------------

Core Guides (in-repo):
- `docs/architecture/pos-sales-authority-certification-findings.md` (authority certification; see
  Documentation Validation for deltas).
- `.ai-context/PROJECT_CONTEXT.md`, `.ai-context/ARCHITECTURE_TRUTH.md`,
  `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`, `.ai-context/KNOWN_RISKS.md`.

System Guides (sibling capabilities in this folder):
- `pos-admin/checkout-management.md` (distinct: Checkout/Storefront, not POS Sales)
- `pos-admin/inventory-management.md` (shared `ProductVariantStock` authority)
- `pos-admin/payment-management.md` (gateway payments; separate from POS cash/card-at-sale)
- `pos-admin/purchase-management.md`, `pos-admin/purchase-return-management.md` (mirror capabilities)
- `pos-admin/order-management.md` (CommerceHub/Storefront orders; independent lifecycle)
- `pos-admin/product-management.md`, `pos-admin/pricing-management.md`

Related Capabilities:
- Cashbox / Treasury (cash drawer lifecycle) — separate.
- Sales Representatives (master data + reports) — separate.
- Sales Returns — implemented inside POS Sales authority (this file).
- Accounting / Ledger / Journal posting — consumed via `ICommerceAccountingService`.
- Inventory projection (Mongo/ES) — downstream consumer of stock-sync outbox.

Related Domains:
- Warehouse, Product/Variant/Unit, Customer ledger, Accounting outbox, Stock sync outbox.

--------------------------------------------------------------------------------
# Documentation Validation
--------------------------------------------------------------------------------

Comparison against `docs/architecture/pos-sales-authority-certification-findings.md` (the only existing
POS-Sales doc) and the rest of the repo. Implementation wins on every conflict.

1. MISSING DOCUMENTATION — Sales state machine is misrepresented by implication. The doc states
   "Completed invoices cannot be rewritten" and implies a Draft→Completed flow, but the implementation
   NEVER produces a Draft sale and NEVER transitions an invoice to Cancelled/PartiallyReturned/
   FullyReturned. The real machine is: create→Completed→(return creates a separate Return, invoice stays
   Completed). The enum's other statuses are dead. This should be documented explicitly so future work does
   not assume a Void/Cancel transition exists.

2. INCORRECT SALES RULE (doc vs impl) — Doc says "`PaymentMethod.Credit` and deferred sale execution fail
   closed." Accurate that Credit fails closed, but the doc omits that `BankTransfer` ALSO fails closed
   (only Cash/Card/Mixed are accepted by `EnsureSupportedPaymentMethod`). Also the "deferred sale execution"
   framing implies Credit is a planned deferred-payment mode; in the running code Credit is simply an
   unsupported enum value at sale creation, not a deferred-execution feature.

3. HIDDEN IMPLEMENTATION BEHAVIOR — `SalesInvoicePayment` table exists (DbSet `SalesInvoicePayments`) but is
   NEVER written by the sale or return paths. Any reader assuming payments are recorded as
   `SalesInvoicePayment` rows will be wrong; payment is captured only on the invoice header + customer
   ledger. This is undocumented and surprising.

4. HIDDEN IMPLEMENTATION BEHAVIOR — Held Sales / Resume Sale are NOT server entities. The Web UI
   (`HoldSalesModal`, `usePOSCart`, `utils/localStorage.saveHeldSale`) keeps held sales in browser
   `localStorage` only. There is no server "held sale" table, no resume endpoint, and no server authority.
   Calling held sales a POS Sales capability overstates server ownership.

5. HIDDEN IMPLEMENTATION BEHAVIOR — Shift / Cash Drawer / Opening-Closing balance are NOT part of POS Sales.
   Cash sales post to a warehouse cashbox LEDGER ACCOUNT, not to the `Cashbox` table. The `Cashbox`
   entity/controller is a separate treasury capability. Documentation should not imply POS Sales owns shift
   or drawer reconciliation.

6. INCORRECT LIFECYCLE ASSUMPTION — The legacy `POST api/sales/{id}/return` endpoint is RETIRED (returns
   410 GONE) and its `#if false` body is dead. All returns go through `api/salesreturns`. Any doc/procedure
   referencing the legacy endpoint is outdated.

7. ARCHITECTURE INCONSISTENCY — `IEventPublisher` is injected into `SalesController`/`SalesReturnsController`
   but is UNUSED in both. The sale/return path emits NO domain event through it; the only event emission is
   the stock-sync outbox (`ProductVariantStockChangedEvent`) via `ProductVariantStockMutationSupport`. Any
   architecture diagram claiming POS Sales publishes a sale domain event through `IEventPublisher` is wrong.

8. MISSING AUTHORITY BOUNDARY — The doc lists "Sales Invoice Lifecycle" as certifiable but does not state
   that the invoice `Status` is effectively immutable at `Completed` and that returns do not flip it. The
   authority boundary for "is this sale returned?" is the existence of `Return` rows (cumulative bounds),
   not the invoice `Status` field. This distinction matters for any report/query that filters on
   `InvoiceStatus`.

9. DOC OUTDATED — `pos-sales-authority-certification-findings.md` predates the immutable cost-basis
   snapshots migration (`20260517120000_PosSaleImmutableCostBasisSnapshots`) and the accounting mutation
   version (`S28_PosSalesAccountingMutationVersion`). COGS now uses per-line immutable `CostAmountSnapshot`,
   and `AccountingMutationVersion` governs stale outbox rejection. These are load-bearing and undocumented
   in the cert findings.

No documentation was rewritten; the above are implementation-backed findings only.

--------------------------------------------------------------------------------
# Summary of Discoveries
--------------------------------------------------------------------------------

- POS Sales is an independent ERP capability with its own controllers (`SalesController`,
  `SalesReturnsController`), tables, accounting outbox events, and inventory mutations — separate from
  Checkout, Storefront Orders, CommerceHub Orders, and Affiliate.
- A sale is created, priced, discounted, tax-applied, inventory-deducted, payment-captured, ledger-posted,
  and accounting-posted in ONE idempotent transaction; it lands in `InvoiceStatus.Completed` immediately.
- Pricing, tax, and payment allocation are SERVER-AUTHORITATIVE; client DTO values are recomputed or
  rejected (fail closed).
- Inventory deduction is warehouse+variant scoped via `ProductVariantStockMutationSupport` with deterministic
  row locks and `allowNegativeStock:false`.
- Accounting posts two journals (revenue + COGS) idempotently; COGS uses an IMMUTABLE per-line cost snapshot;
  posting is synchronous with same-transaction outbox marking (background worker = recovery only).
- Returns are a SEPARATE bounded authority that creates a `Return` (SalesReturn), restores stock, optionally
  credits the customer, advances `AccountingMutationVersion`, and does NOT change the original invoice
  status.

Sales lifecycle discovered:
  create(idempotent) -> Completed -> (sales return) -> Completed [+Return row, stock restored];
  delete only if not Completed & no returns -> Deleted.
  No Draft/Held/Void/Cancelled terminal state is produced by the server.

Sales state machine discovered:
  Effective states in the running system: Completed (only produced), Deleted (rare, gated).
  Enum declares Draft/Cancelled/PartiallyReturned/FullyReturned but none are assigned by the code.

Business rules discovered:
  idempotency-key mandatory; replay-safe by tenant/key/operation/hash; warehouse+customer active same-tenant;
  server pricing; unit conversion required; line+invoice discount bounded; stock locked non-negative;
  payment method limited to Cash/Card/Mixed (Credit/BankTransfer rejected); mixed must equal total;
  accounting failure rolls back the sale; returns bounded by prior returns; delete blocked for
  Completed/returned invoices; all currency rounded to 2dp.

Payment behavior discovered:
  settled at sale creation; Cash (over-tender change), Card (exact), Mixed (cash+card==total); customer
  on-account via ledger; refunds Cash/Card/Mixed/AccountCredit; no gateway/wallet/gift-card/loyalty.

Accounting behavior discovered:
  revenue journal (cashbox ledger acct or VisaClearing debit, PosSales credit) + COGS journal
  (COGS debit, Inventory credit from immutable snapshot); tax inside total; cash posts to cashbox ledger
  account not Cashbox table; versioned outbox; fail-closed on posted journals / unsafe outbox.

Cross-system dependencies discovered:
  CHANGES: SalesInvoices/Items, ProductVariantStocks, StockMovements, AccountLedgers/CustomerTransactions,
  JournalEntries, AccountingOutboxMessages, SalesRequestIdempotency, AuditLogs, Returns/ReturnItems,
  StockSyncOutboxMessages.
  DOES NOT CHANGE: Products (master), Pricing rules, Customer master, Payment gateways, Warehouse master,
  Analytics, Notifications/WhatsApp, Search (direct), Identity, CommerceHub, Affiliate, Storefront, Orders.
  Inventory Mongo/ES projections are derivatives fed via the stock-sync outbox (eventually consistent).

Documentation inconsistencies found:
  1) Sales state machine misrepresented (no Draft/Cancel transition actually occurs).
  2) Doc omits that BankTransfer also fails closed and that Credit is simply unsupported, not "deferred".
  3) `SalesInvoicePayment` table exists but is never written (hidden behavior).
  4) Held/Resume sales are client localStorage only, not a server capability (hidden behavior).
  5) Shift/Cash Drawer are not owned by POS Sales; cash posts to a cashbox ledger account (hidden behavior).
  6) Legacy `POST api/sales/{id}/return` is retired (410); returns use `api/salesreturns`.
  7) `IEventPublisher` injected but unused in sale/return paths (architecture inconsistency).
  8) Invoice `Status` is effectively immutable at Completed; "returned?" is answered by `Return` rows, not
     `Status` (missing authority boundary).
  9) Cert doc predates immutable cost-basis snapshots + AccountingMutationVersion (outdated).

Hidden implementation behavior found:
  - `SalesInvoicePayment` never populated.
  - Held sales are browser-localStorage only.
  - No Shift/Drawer/Cashbox write in the sale path; cash -> cashbox ledger account.
  - `IEventPublisher` unused; only stock-sync outbox emits events.
  - `InvoiceStatus` other than Completed is dead.
  - CreateSale blocks until accounting posts (sale not committed without journals).
  - Returns ignore client price/tax and advance `AccountingMutationVersion` without flipping invoice status.

Confirmation that only this file was created:
  Created exactly one file:
  `.ai/hermes/capabilities/pos-admin/sales-management.md`
  No other file in the repository was created, modified, or deleted by this task.

Confirmation that no other files were modified:
  This task performed read-only investigation (file reads + grep/terminal read commands). No edits, no
  writes, no deletes, no `git` mutations were applied to any repository file other than the single new
  capability document above.

Confirmation that no memory writes occurred:
  No memory tool calls were made. No `memory` writes, no skill writes, no session-memory updates.
