# POS Runtime — Final Canonical Architecture

> Implementation-backed architecture of the Point of Sale Runtime.
> Implementation is the ONLY source of truth. Every claim is cited to `file:line`.
> Prior markdown / certification documents are treated as secondary and were re-verified
> against the backend before inclusion. Where implementation evidence is absent, the
> section states a VERIFIED GAP.

---

## Executive Summary

The POS Runtime is the **transactional execution engine** of Shopixy for in-person retail:
over-the-counter sales, supplier purchases, customer returns/refunds, cash-drawer (cashbox)
operations, and the ledger/accounting postings those operations generate. It is NOT a screen
and NOT a Storefront variant — it is a distinct bounded context that writes its own SQL
authority (`SalesInvoice`, `PurchaseInvoice`, `Return`, `Cashbox`, `CashboxTransaction`,
`StockMovement`) and posts to the shared accounting outbox.

The defining architectural fact, verified in code, is that **POS execution does not reuse the
Storefront/CommerceHub `CheckoutService` pipeline**. POS sales/returns/purchases are executed
by dedicated controllers (`SalesController`, `SalesReturnsController`, `PurchasesController`,
`CashboxController`) that:

1. Run the entire mutation inside **one PostgreSQL/SQL-Server transaction** (`SalesController.cs:131`,
   `SalesReturnsController.cs` wrapped in `BeginTransactionAsync`).
2. Mutate stock **directly** under a deterministic, deadlock-safe row lock
   (`ProductVariantStockMutationSupport.LockMultipleStocksAsync`, `SalesController.cs:202-207`)
   — there is **no inventory-reservation step** in the POS path (unlike Storefront, which uses
   `InventoryReservation` with a 10-minute TTL: `CheckoutService.cs:271`, `:5118`).
3. Enforce financial integrity with **fail-closed guards**: claims are evidence only, SQL state
   decides (`PosSessionAuthority.cs:46-124`); over-return is blocked by summing prior returns
   (`SalesReturnsController.cs:472-491`, `:804-810`); pending/failed accounting outbox rows block
   invoice mutation (`SaleFinancialIntegrityService.cs:110-140`).
4. Stage financial postings into the **accounting outbox** and post them **synchronously inside
   the request**, marking the outbox row processed in the same transaction so the background
   worker is only a recovery path, never a second financial authority
   (`SalesController.cs:406-425`).

The POS Runtime shares the platform's CQRS/Outbox/Projection backbone (Mongo, Redis,
Elasticsearch, `OutboxMessage`/`AccountingOutboxMessage`/`StockSyncOutboxMessage`) but, as
verified below, most POS entities are **not projected to Mongo at all** — they are read back
from SQL. This is a deliberate "SQL authority, no projection" stance for the cash/ledger-critical
parts of POS.

---

## Architecture Vision

One bounded context that executes retail transactions **safely and atomically**, where:

- SQL is the single runtime truth for every financial and inventory mutation.
- The cashier client supplies prices/discounts/tax (the POS terminal is the price source); the
  server enforces totals, stock, over-return limits, and accounting postings.
- Every outward money movement is gated by a fail-closed reconciliation state.
- Recovery is idempotent by construction (`SalesRequestIdempotency`, fingerprint hashing,
  accounting-outbox versioning).

---

## POS Runtime Purpose

Execute in-person retail transactions (sales, purchases, returns, refunds, cash handling) with
atomic SQL authority and synchronous accounting posting, under a fail-closed session and
financial-integrity model.

## POS Runtime Scope

In scope (verified code ownership):

- Sales: `SalesController`, `SalesInvoice`/`SalesInvoiceItem`/`SalesInvoicePayment`.
- Purchases (supplier procurement): `PurchasesController`, `PurchaseInvoice`/items/payments.
- Returns & Refunds: `SalesReturnsController`, `PurchaseReturnsController`, `Return`/`ReturnItem`,
  `RefundMethod` allocation.
- Cashbox / Shift: `CashboxController`, `Cashbox`, `CashboxTransaction`.
- Inventory mutation at POS: `ProductVariantStockMutationSupport` (direct lock + decrement/increment).
- Payment validation: inline in `SalesController`/`PurchasesController` (`ValidateAndNormalizePaymentAmounts`).
- Accounting posting: `IAccountingOutboxService` (PosSale/PosPurchase/PosRefund/PosExpense) +
  `CommerceAccountingService`.
- POS session authority: `PosSessionAuthority`.

## POS Runtime Owns

| Aggregate | SQL Table(s) | Owner (write) |
|---|---|---|
| Sale | `SalesInvoices`, `SalesInvoiceItems`, `SalesInvoicePayments` | `SalesController` (`PointOfSale.API/Controllers/SalesController.cs`) |
| Purchase | `PurchaseInvoices`, items, payments | `PurchasesController` |
| Return/Refund | `Returns`, `ReturnItems` | `SalesReturnsController` / `PurchaseReturnsController` |
| Cashbox | `Cashboxes`, `CashboxTransactions` | `CashboxController` |
| Stock movement (POS) | `ProductVariantStocks`, `StockMovements`, `StockSyncOutboxMessages` | POS controllers via `ProductVariantStockMutationSupport` |
| Sale accounting | `AccountingOutboxMessages`, `JournalEntries` | `AccountingOutboxProcessor` + `CommerceAccountingService` |

## POS Runtime Does NOT Own

- **Pricing engine** (`StorefrontPricingService`, `Pricing/`), **Promotion engine**
  (`PromotionEngine`), **Coupon engine** (`CouponUsageService`, `StorefrontCouponService`):
  POS sale controllers do **not inject or call** these services (verified:
  `SalesController.cs:48-90` constructor injects only ledger/integrity/accounting/audit/eventpublisher;
  no pricing/promo/coupon service is referenced anywhere in `SalesController.cs` or
  `PurchasesController.cs`). POS receives `UnitPrice`, `DiscountAmount`, `TaxRate` inline from the
  request and validates the computed total (`ApplySaleTotals`, `SalesController.cs:2534-2564`).
- **Inventory reservation** (`InventoryReservation`): written only by Storefront/CommerceHub
  `CheckoutService` (`CheckoutService.cs:5118`, `CommerceHubCheckoutService.cs`). POS does not create
  reservations.
- **Storefront checkout / cart projection / CommerceHub marketplace** — separate contexts.
- **Treasury / Accounting GL write** — POS enqueues to the accounting outbox; it never writes
  `JournalEntry` directly (comments: `SalesController.cs:427` "DO NOT mutate balances directly",
  `PurchasesController.cs:372` "treasury posting is handled through the outbox").
- **Customer/merchant identity lifecycle, product catalog authoring, shipping** — deferred to
  owning services (POS only reads `Customer`, `ProductVariant`, `Warehouse`).

---

## Architecture Principles (code-cited)

1. **SQL is the single runtime truth.** Every financial/inventory mutation is a SQL row, committed
   in one transaction. No projection is the source of truth for a POS write
   (`SalesController.cs:131-464` entire create-sale transaction; `SalesReturnsController.cs:402-977`).
2. **Claims are evidence, SQL state decides.** `PosSessionAuthority` re-queries `Users`,
   tenant lineage, and governance status after JWT validation, using fixed-time comparison of the
   session binding (`PosSessionAuthority.cs:46-124`, `:133-144`). A valid token with a rotated/
   banned/tenant-mismatched actor is denied (fail-closed).
3. **Fail-closed outward money.** Pending/failed/claimed accounting outbox rows block invoice
   mutation (`SaleFinancialIntegrityService.cs:110-140`). Over-return is blocked by summing prior
   returns against the original total (`SalesReturnsController.cs:472-491`, `:804-810`).
4. **Idempotency by fingerprint.** Sale/return/purchase requests are claimed via
   `(TenantId, Key, Operation)` unique constraint + `RequestHash`; a key bound to a different
   payload is rejected, an exact replay returns the stored response
   (`SalesController.cs:513-560`, `SalesReturnsController.cs:999-1057`).
5. **Synchronous post + outbox recovery.** Accounting is posted inside the request and the outbox
   row marked processed in the same transaction; the background worker is recovery-only
   (`SalesController.cs:406-425`; `AccountingOutboxProcessor.cs:952-1011` stale-version skip).
6. **Deterministic lock ordering prevents deadlocks.** Multi-row stock locks are ordered by
   `(WarehouseId, VariantId)` (`ProductVariantStockMutationSupport.cs:300-304`).
7. **Negative-stock protection inside the lock.** `ValidateAvailableQuantity(..., allowNegativeStock:false)`
   throws `InsufficientStockException` after the lock is held (`SalesController.cs:228-232`;
   `ProductVariantStockMutationSupport.cs:ValidationAvailableQuantity`).
8. **Monotonic mutation versioning for replay correctness.** `SalesInvoice.AccountingMutationVersion`
   is incremented exactly once per return (`SalesReturnsController.cs:863-864`) so downstream intent
   is auditable and stale outbox versions are skipped (`AccountingOutboxProcessor.cs:971-993`).
9. **Repair-on-read / SQL-authority ledger for cashbox.** Cashbox comments explicitly forbid using
   the cached `CurrentBalance`/`ExpectedBalance` columns and mandate recomputation from the ledger
   (`CashboxController.cs:67-68` "DO NOT use CurrentBalance — use ledger", `:68-69` "DO NOT use
   ExpectedBalance — use ledger").

---

## Core Aggregates

| Aggregate | Entity file | Key fields |
|---|---|---|
| Sale | `PointOfSale.Core/Entities/SalesInvoice.cs` | `TenantId`, `InvoiceNumber`, `InvoiceStatus`, `Subtotal`, `DiscountAmount`, `TaxAmount`, `TotalAmount`, `PaidAmount`, `RemainingAmount`, `PaymentMethod`, `CashAmount`, `CardAmount`, `AccountingMutationVersion`, `SalesRepresentativeId` |
| Sale item | `SalesInvoiceItem.cs` | `ProductId`, `ProductVariantId`, `UnitId`, `Quantity`, `BaseQuantity`, `UnitPrice`, `DiscountAmount`, `TaxRate`, `TaxAmount`, `CostPriceSnapshot` |
| Purchase | `PurchaseInvoice.cs` | supplier invoice, mirror of sale but payable-side |
| Return | `Return.cs` / `ReturnItem.cs` | `ReturnType` (SalesReturn/PurchaseReturn), `OriginalInvoiceId`, `TotalAmount`, refund allocation |
| Cashbox | `Cashbox.cs` | `CurrentBalance`, `OpeningBalance`, `ExpectedBalance`, `IsActive`, `OpenedAt`, `OpenedByUserId` |
| Cashbox tx | `CashboxTransaction.cs` | cash-in/out/expense movements |
| Stock | `ProductVariantStock.cs`, `StockMovement.cs` | per `(WarehouseId, VariantId)` quantity; movements are append-only history |

---

## Cart Runtime

POS cart behaviour is intentionally thin. The POS terminal sends an opaque `X-Pos-Cart-Id`
header that is used **only as an idempotency key**, not as a server-side cart aggregate that the
sale reads line-items from. (Verified: `SalesController.cs:37` `PosCartIdHeader`, `:101` param,
`:115-122` the cart id is normalized into `createIdempotencyKey`; the sale itself reads line items
from `request.Items`, never from a cart entity.)

- **Cart Creation / Mutation / Validation / State / Expiration / Hold / Resume / Cancel / Merge**:
  Server-side POS cart lifecycle (hold/resume/merge/expiration) is **NOT implemented** for the POS
  sale path. The `CartService` (`PointOfSale.API/Services/Cart/CartService.cs`) exists but is the
  **Storefront cart** (per its DTOs `PointOfSale.API/DTOs/Cart`), not the POS cart. POS line items
  travel in the `CreateSaleRequest.Items` payload and are validated inline
  (`PrepareSaleItemsAsync`, `SalesController.cs:194-199`).
- **Persistence / Authority**: The POS "cart" is the uncommitted request body held by the cashier
  client. SQL authority for a sale begins at `SalesInvoices` insert. Server-side POS cart state is a
  VERIFIED GAP.
- **Implication**: POS does not implement the hold/resume/cancel/merge semantics described in the
  brief; those are storefront-cart concepts not present in the POS execution path.

---

## Cart Line Runtime

POS cart lines are the `SaleItemRequest` elements of `CreateSaleRequest` (`SalesController.cs:~2883`).
Each line carries `ProductId`, `ProductVariantId`, `UnitId`, `Quantity`, `UnitPrice`,
`DiscountAmount`, `TaxRate`. The server resolves the variant, locks stock, validates availability,
and snapshots cost price (`ResolveSaleCostPriceSnapshot`, `SalesController.cs:2505-2515`). There is
no separate server-side cart-line aggregate for POS.

---

## Pricing Runtime

POS **does not invoke the pricing engine**. Prices arrive inline from the client and the server
enforces arithmetic consistency only.

- **Base Price / Sale Price / Price Override / Manual Override / Tier / Customer / Wholesale
  Pricing**: Not computed by POS. The brief's pricing concepts (tier, customer, wholesale) live in
  `StorefrontPricingService` / `Pricing/` and are **not referenced** by `SalesController` or
  `PurchasesController`. POS accepts whatever `UnitPrice` the terminal sends.
- **Tax Inclusive / Tax Exclusive**: Handled per line via `product.PriceIncludesTax`
  (`SalesController.cs:237` `var itemTax = product.PriceIncludesTax ? 0 : (taxableAmount * TaxRate/100)`).
  The product flag drives whether tax is added on top.
- **Priority**: N/A for POS (single inline price, no engine priority).
- **Authority**: The cashier terminal is the de-facto price source; server authority is the
  validated, persisted `SalesInvoiceItem.UnitPrice`. This is a deliberate design (POS is a
  human-operated terminal) but means **price-integrity depends on the client** — documented as
  technical debt (see below).

---

## Promotion Runtime

Not used in the POS sale path. `PromotionEngine` (`PointOfSale.API/Services/Promotions/PromotionEngine.cs`)
and `PromoService`/`PromoCalculationEngine` (`CommerceHub`) are storefront/CommerceHub-scoped. No
promotion evaluation, auto-apply, stacking, or exclusivity logic executes during a POS sale. Any
promotion discount in POS is a manually supplied `DiscountAmount` on the request. VERIFIED GAP for
the brief's Promotion evaluation/priority/stacking requirements in the POS context.

---

## Coupon Runtime

Not used in the POS sale path. `CouponUsageService`, `CouponCustomerValidationRules`,
`StorefrontCouponService`, and `Coupon`/`CouponUsage`/`PromoCode` entities are storefront-scoped.
POS does not validate coupon eligibility, usage limits, expiration, or restrictions during a sale.
VERIFIED GAP for the brief's Coupon validation/eligibility/usage-limits/expiration requirements in
the POS context.

---

## Customer Runtime

POS attaches an optional `CustomerId` to a sale (`SalesController.cs:158-167`). When present, the
server creates ledger entries (`CustomerTransaction`) and updates `customer.Balance` via
`ILedgerService.CreateLedgerEntry` (`SalesController.cs:327-397`). Credit sales (sell-on-account)
are supported: `PaymentMethod.Credit` exists (`PaymentMethod.cs:9`) and a sale can be recorded with
`PaidAmount < TotalAmount`, leaving a receivable (the ledger `Sale` debit minus `PaymentReceived`
credit). Refunds can credit the customer account (`RefundMethod.AccountCredit`,
`SalesReturnsController.cs:840-858`). Customer read authority = SQL `Customers`.

---

## Payment Runtime

POS payment validation is inline in the controllers (`ValidateAndNormalizePaymentAmounts`,
`SalesController.cs:2590-2675`):

- **Cash**: `PaymentMethod.Cash` — `PaidAmount >= TotalAmount` required; change =
  `PaidAmount - TotalAmount` (`SalesController.cs:2646-2651`, `:446`). Overpayment is allowed
  (change returned); underpayment rejected.
- **Card**: `PaymentMethod.Card` — `PaidAmount == TotalAmount` required (`:2666-2669`).
- **Bank**: `PaymentMethod.BankTransfer` exists in the enum (`PaymentMethod.cs:7`) but
  `EnsureSupportedPaymentMethod` rejects it for a direct POS sale (`SalesController.cs:2580-2588`
  allows only Mixed/Cash/Card). Bank transfer is therefore not accepted as a direct POS tender in
  the sale path (VERIFIED GAP / by-design restriction).
- **Wallet**: not a POS tender in the sale path.
- **Split Payments**: `PaymentMethod.Mixed` requires `CashAmount + CardAmount == TotalAmount`
  within 0.01 (`:2609-2631`); ledger records the split note (`:364-367`).
- **Credit Sales**: supported via `PaymentMethod.Credit`/receivable ledger; the enum value exists
  and the ledger path handles partial payment.
- **Change Calculation**: `ChangeAmount = PaidAmount - TotalAmount` (`SalesController.cs:446`,
  `:1633`, `:2479`).
- **Overpayment / Underpayment**: overpayment → change; underpayment (cash < total, or card != total)
  → `ValidationException` (fail-closed).
- **Payment Validation / Authority**: server-side normalization rejects invalid splits/amounts
  (`:2597-2674`). Outward money (refund) is gated by `SaleFinancialIntegrityService` and the
  accounting outbox; POS does **not** use `IPaymentLifecycleService` (that is CommerceHub/storefront).
  Note: a sale does **not** insert a `SalesInvoicePayment` row — payment is recorded via the
  `CustomerTransaction` ledger path and the accounting journal; `PaidAmount`/`CashAmount`/`CardAmount`
  are denormalized columns on `SalesInvoice` (`SalesInvoice.cs:53-66`; `SalesController.cs:343-396`;
  `SalesInvoicePayment.cs` entity exists but is unused on the sale path — verified: no INSERT in
  `SalesController.cs`).
- **Note on refunds (outward money)**: refund allocation resolved by `RefundAllocationSupport.Resolve`
  (`SalesReturnsController.cs:377-388`); methods Cash/Card/Mixed/AccountCredit
  (`RefundMethod.cs:5-8`). Full-payout methods must allocate the entire return total; otherwise
  `AccountCredit` is required (`:844-850`).

---

## Inventory Runtime Integration

POS mutates inventory **directly and synchronously** inside the sale/return transaction:

- **Availability / Variant Stock**: `ProductVariantStock` per `(WarehouseId, VariantId)`.
- **Warehouse Selection**: required on the request; validated `LoadActiveWarehouseAsync`
  (`SalesController.cs:150-155`).
- **Negative Stock**: protected — `ValidateAvailableQuantity(..., allowNegativeStock:false)`
  throws `InsufficientStockException` (`SalesController.cs:228-232`;
  `ProductVariantStockMutationSupport.cs`).
- **Reservation**: **absent in POS**. POS uses `LockMultipleStocksAsync` + immediate decrement, not
  `InventoryReservation`. (Storefront uses reservations with a 10-min TTL; POS does not.)
- **Validation Timing**: inside the transaction, after the lock is acquired (`:202-232`).
- **Revalidation**: the original invoice is write-locked for the full return transaction
  (`LoadOriginalInvoiceForReturnMutationAsync`, `SalesReturnsController.cs:423-426` "SR-CON-01
  authoritative fence") so competing returns revalidate against committed history.
- **Failure Behaviour**: any stock/validation failure rolls back the whole transaction
  (`SalesController.cs:465-510`).

---

## Stock Reservation Runtime

- Storefront/CommerceHub: `InventoryReservation` with TTL + `InventoryReservationCleanupJob`
  (releases expired reservations via payment lifecycle, `InventoryReservationCleanupJob.cs:58-233`).
- POS: **no reservation runtime**. Stock is decremented/incremented under a transaction lock and a
  `StockMovement` append-only audit row is written (`SalesController.cs:269-304` sale;
  `SalesReturnsController.cs:758-792` return). This is a deliberate simplification for
  human-operated, single-actor POS counters (no concurrent checkout race within one cashier
  session), but it means POS stock is not "reserved" — it is "deducted". Documented as an
  architectural divergence, not a gap.

---

## Checkout Runtime

POS checkout is **not** the `CheckoutService` pipeline. There is a dedicated, single-transaction
POS checkout inside `SalesController.CreateSale`:

```
request (line items + prices + payment)
  ↓  idempotency claim (X-Pos-Cart-Id / X-Payment-Attempt-Id)   SalesController.cs:130-144
  ↓  warehouse validation                                        SalesController.cs:150-155
  ↓  customer validation (optional)                              SalesController.cs:158-167
  ↓  invoice number generation                                   SalesController.cs:170, :2677-2712
  ↓  prepare sale items (variant resolution, cost snapshot)      SalesController.cs:194-208
  ↓  LOCK stock rows (deterministic order)                       SalesController.cs:202-207
  ↓  per-line: validate availability, tax, totals               SalesController.cs:209-305
  ↓  ApplySaleTotals (discount %, amount, tax)                   SalesController.cs:307-312, :2534-2564
  ↓  ValidateAndNormalizePaymentAmounts (cash/card/mixed)        SalesController.cs:314-322, :2590-2675
  ↓  insert SalesInvoice + items                                 SalesController.cs:324
  ↓  IF customer: ledger Sale + PaymentReceived (CustomerTransaction) SalesController.cs:327-397
  ↓  enqueue accounting outbox (PosSale) + synchronous post      SalesController.cs:406-425
  ↓  publish StockSyncOutboxMessage (committed stock events)     SalesController.cs:429-435
  ↓  complete idempotency + commit transaction                   SalesController.cs:437-463
  ↓  audit log                                                   SalesController.cs:450-461
```

- **Runtime Owner**: `SalesController` (POS), not `CheckoutService` (storefront).
- **Authority**: SQL (`SalesInvoices` + `ProductVariantStocks` + `AccountingOutboxMessages`).
- **Failure Behaviour**: full rollback on any exception (`SalesController.cs:465-510`); idempotency
  short-circuits replays to the stored response (`:140-144`).
- **Replay / Recovery / Idempotency**: `(TenantId, Key, Operation)` unique claim + `RequestHash`
  fingerprint; exact replay returns stored response, divergent payload rejected
  (`:513-560`).

---

## Receipt Runtime

- **Receipt Generation**: `SalesController.GetReceipt(Guid id)` (`SalesController.cs:2447`) builds a
  `ReceiptData` DTO (with `InvoiceNumber`, `WarehouseName/Address`, `CustomerName`, `Items`,
  `Subtotal`, `Discount`, `Tax`, `Total`, `Paid`, `Change=Paid−Total`, `PaymentMethod`,
  `SalesController.cs:2447-2484`, `ReceiptData` at `:2925`). The output is a **JSON DTO only — no
  formatting/rendering**; printing is client-side (React SPA).
- **Invoice Number**: `GenerateInvoiceNumberAsync` (`SalesController.cs:2677`) emits
  `INV-yyyyMMdd-NNNN` (`:2683`), using PostgreSQL `nextval('"PosSalesInvoiceNumberSeq"')`
  (`:2734`) with a tenant-locked fallback max-suffix scan + `FOR UPDATE`/`UPDLOCK` race protection
  (`LockTenantForInvoiceNumberAsync`, `:2686`, `:2762-2787`). Returns get a `-R` suffix
  (`SalesController.cs:2181-2188` legacy path; `AllocateReturnNumberAsync` `SalesReturnsController.cs:646-649`).
- **Thermal Printing**: **VERIFIED GAP** — repo search for `escpos|thermal|printer|QzTray|StarPRNT|
  RawPrinter|PrintReceipt` returns 0 `.cs` hits; no printing NuGet package. Printing is client-side.
- **Reprint / Duplicate Prevention**: there is **no `/reprint` endpoint**. A reprint is simply a
  second `GET api/Sales/receipt/{id}` (idempotent read). Duplicate *sale* prevention is via the sale
  idempotency key (`X-Pos-Cart-Id`/`X-Payment-Attempt-Id`) + request-hash fingerprint
  (`SalesController.cs:122-712`): same key+payload returns the stored `SaleResponse`; same key +
  different payload → `400` ("Idempotency key reuse with different payload", `:560-568`).

---

## Printing Runtime

Server-side printing is not implemented (no thermal/receipt printer integration in
`PointOfSale.API`). The POS terminal renders `ReceiptData`. VERIFIED GAP for server-side printing.
(Printing Runtime in the brief maps to the client; runtime behaviour is the `ReceiptData` read.)

---

## Shift Runtime

Implemented as the **Cashbox** aggregate (`CashboxController`):

- **No `Shift` entity exists.** The "shift" lifecycle is modeled purely as the Cashbox open/close
  cycle (`Cashbox.IsActive`). The only `Shift`/`OpenDrawer`/`CloseDrawer` references in the repo are a
  migration *comment* describing legacy Treasury types (`PointOfSale.Infrastructure/Data/Migrations/20260208180000_BlockLegacyCashboxVisaWrites.cs:51`). 
- **Shift Open**: `OpenCashbox` (`CashboxController.cs:337`) sets `IsActive=true`, `OpenedAt`,
  `OpenedByUserId`, `OpeningBalance`, rejects a non-zero opening balance (float-transfer required,
  `:347-354`), and is idempotent-guarded against an already-open box (`:32-34` "Cashbox is already
  open", `:399-402` sets flags). Cash-in to treasury only if `OpeningBalance > 0` (`:379-397`).
- **Shift Close**: `CloseCashbox` (`CashboxController.cs:486`). Sets `IsActive=false` (`:34`),
  computes `expectedBalance = GetCashboxLedgerBalanceAsync(cashbox.Id)` (`:30`),
  `difference = request.ActualCash - expectedBalance` (`:31`), and flags `discrepancyDetected` when
  `|difference| > 0.01m` (`:62`). There is **no approval flow** — close only records the variance and
  returns `{expectedBalance, actualBalance, difference, discrepancyDetected}` (`:65-70`). No threshold
  action, approver, or pending/approved status exists.
- **Cash Drawer**: `Cashbox` is the drawer; `CashboxTransaction` (enum `CashboxTransactionType`:
  Sale/SaleRefund/PurchasePayment/.../Expense) records every movement. Cash flows go through
  `ITreasuryOperationService.CashInAsync/CashOutAsync` (account codes `CashFloatReserve`,
  `TreasurySuspense`, `Expenses`, `CashboxController.cs:38-41`).
- **Expected Cash / Actual Cash / Variance**: `Cashbox.ExpectedBalance` and `CurrentBalance` are
  `[Obsolete]` shadow fields explicitly forbidden for business logic
  (`Cashbox.cs:23` `[Obsolete("...DO NOT use CurrentBalance; use ledger APIs instead.")]`,
  `:50` same for `ExpectedBalance`; `CashboxController.cs:170`, `:403-404` "DO NOT use ... - use
  ledger"). Expected cash is the **ledger balance**, actual cash is client-supplied
  (`CashboxController.cs:30-31`), variance threshold = 1 cent.
- **Approval**: `OpenCashbox`/`CashIn`/`SetOpeningBalance` are `[Authorize(Roles="Owner,Admin")]`
  (`CashboxController.cs:336`, `:678`, `:1211`). `CashOut` and `RecordExpense` are role-gated only by
  `[Authorize]` (any authenticated user) — a separation-of-duties weakness (see Technical Debt).

---

## Cashier Runtime

- **Cashier Identity / Session**: The POS actor is a `User` with role Owner/Admin/Manager. Session
  authority is `PosSessionAuthority`, which validates the JWT's `pos_session` binding claim against
  current SQL `Users`/`Tenant`/governance state (`PosSessionAuthority.cs:46-124`). The cashier is
  identified by `User.FindFirstValue(ClaimTypes.NameIdentifier)` and recorded on returns as
  `CreatedBy` (`SalesReturnsController.cs:644-665`).
- There is **no separate "Cashier" aggregate** — cashiers are `Users` with POS access. The
  `SalesRepresentative` entity exists (`SalesInvoice.SalesRepresentativeId`) for attribution but is
  distinct from the authenticated operator.

---

## Return Runtime

Implemented in `SalesReturnsController.CreateSalesReturnInternal` / `FinalizeSalesReturnAsync`
(`SalesReturnsController.cs:402-997`):

- **Invoice Lookup**: `OriginalInvoiceId` required; original loaded and write-locked
  (`LoadOriginalInvoiceForReturnMutationAsync`, `:423-426`, `:426`).
- **Partial Return / Full Return**: any subset of lines/quantities; enforced that
  `priorReturnedTotal + thisReturn <= originalInvoiceTotal` (`:804-810`). Full return = sum equals
  total.
- **Refund**: `RefundAllocation` resolved (`Cash`/`Card`/`Mixed`/`AccountCredit`),
  `:361-388`, `:812-858`.
- **Inventory Restoration**: returned quantity is added back to `ProductVariantStock` under lock,
  with a `StockMovement` of type `SalesReturn` (`:758-792`, `:949-955` publishes stock-sync outbox).
- **Ledger Reversal**: a `CustomerTransaction` of type `SalesReturn` credits the customer
  (`:868-897`).
- **Server-authoritative refund pricing**: the refund amount is recomputed from a snapshot of the
  **original invoice** (`BuildOriginalInvoiceSnapshotsAsync`, `:463`); client-supplied `UnitPrice`/
  `TaxRate` overrides on the return request are **ignored and logged** (`:495`, `:538-549`
  "Ignored client refund pricing override…"). This is the inverse of the sale path, where the server
  accepts the cashier-supplied price — the return is server-trusted so a refund cannot be inflated.
- **Payment Reversal**: refund payout allocated; accounting outbox `PosRefund` enqueued and posted
  synchronously (`:899-906`, `:979-983`). The payout is **ledger/journal-only**: `PostPosRefundAsync`
  "creates a reversal of the original sale" (`ICommerceAccountingService.cs:130`) — there is **no
  physical cashbox debit or payout-disbursement table** on refund (verified: no cashbox mutation in
  the return flow). If the synchronous post fails, the outbox row is left pending for worker retry
  (fail-closed — the return is committed but money is not double-paid),
  `SalesReturnsController.cs:1635-1644`.
- **Business Rules**: only `Completed` invoices returnable (`:438-441`); customer must match
  (`:443-446`); refund total must be > 0 (`:799-802`); full-payout methods must allocate the entire
  return total (`:844-850`).

---

## Refund Runtime

- **Refund Method**: `RefundMethod` enum Cash/Card/Mixed/AccountCredit (`RefundMethod.cs:5-8`).
- **Refund Payout Security**: refund accounting is posted synchronously and the outbox row marked
  processed; if the synchronous post fails, the outbox row is left pending for retry/reconciliation
  (fail-closed — the return is committed but money is not double-paid)
  (`SalesReturnsController.cs:1608-1646` `TryCompleteSynchronousRefundAccountingAsync`).
- **Refund Financial Decision**: `RefundAllocationSupport.Resolve` validates cash/card split;
  `AccountCredit` leaves the balance on the customer ledger (`:840-858`).
- Note: POS refund uses `ICommerceAccountingService.PostPosRefundAsync` + the shared accounting
  outbox — it does **not** use the CommerceHub `RefundExecutionService`/`CommerceHubRefundService`
  (those are marketplace-scoped).

---

## Recovery Runtime

- **Crash Recovery / Cart Recovery / Interrupted Checkout**: POS sale is a single SQL transaction;
  any crash before commit rolls back (`SalesController.cs:131`, `:465-510`). There is no separate
  "cart recovery" because POS has no server-side cart. VERIFIED GAP for POS-specific cart recovery.
- **Partial Payment**: POS requires full payment at sale time (cash overpay = change; card must equal
  total). No partial-payment-to-be-completed-later flow exists in the POS sale path.
- **Retry / Idempotency**: `(TenantId, Key, Operation)` unique claim + `RequestHash`
  (`SalesController.cs:513-560`); replays return the stored response; divergent payload rejected.
  The accounting outbox uses `AccountingMutationVersion` so stale replay is skipped
  (`AccountingOutboxProcessor.cs:971-993`).

---

## Analytics Runtime

POS analytics are read from SQL via reporting controllers (`ReportsController`,
`SalesRepReportsController`, `DashboardController`) over `SalesInvoices`/`Returns`/`CashboxTransactions`.
There is a dedicated `AuthoritativePurchaseAnalyticsConsumer` (SQL-authoritative purchase analytics
projection) and generic `AnalyticsEventConsumer`, but POS sales are **not emitted as analytics
domain events** from the sale path (the sale path does not call `_eventPublisher` for the
transaction). POS analytics therefore read SQL directly rather than via a Mongo projection.
VERIFIED GAP: no POS-specific Mongo analytics projection / no sale-domain event emission from the
POS sale path (contrast Storefront/CommerceHub which emit analytics events).

---

## CQRS

- **Write Authority**: SQL (`SalesInvoices`, `PurchaseInvoices`, `Returns`, `Cashboxes`,
  `ProductVariantStocks`, `JournalEntries` via outbox). Single transaction per operation.
- **Read Authority**: SQL, read directly by the controllers (no Mongo read model for sales/returns/
  cashbox). Receipt/report reads query SQL (`SalesController.GetReceipt`, `:2447-2484`;
  `CashboxController.GetSummary`/`GetDailyReport`).
- **Mongo**: POS sales/returns/cashbox are **not projected to Mongo** (no consumer found; verified
  by scanning `Consumers/` — `AuthoritativePurchaseAnalyticsConsumer` is the only POS-adjacent one,
  and it is analytics, not the operational aggregate).
- **Redis**: used for read caches elsewhere; POS operational reads are SQL.
- **Elasticsearch**: not used for POS operational reads.
- **Projection**: N/A for POS operational aggregates (SQL-authority, no projection). Stock moves are
  projected to search via `ProductStockChangedConsumer`/`ProductStockPriceSearchIndexConsumer` (shared
  product projection), not a POS-specific projection.
- **Repair-on-Read**: cashbox explicitly recomputes from ledger (`CashboxController.cs:67-69`).
- **Critical Reads**: stock availability and customer balance are read inside the transaction under
  lock (not from a projection).
- **Projection Freshness / Replay**: POS does not depend on projection freshness for correctness.

---

## Projection Model

POS operational aggregates have **no Mongo projection** — they are SQL-authority and read back from
SQL. The only POS-adjacent projection is purchase analytics (`AuthoritativePurchaseAnalyticsConsumer`).
This is a deliberate "no projection for cash/ledger-critical POS data" stance: correctness depends on
SQL, not on eventual projection convergence.

---

## Mongo Integration

Used by the platform backbone (product/catalog/order/affiliate projections) but **not** by POS
operational aggregates. POS does not write a `SalesInvoiceDocument`/`CashboxDocument` to Mongo.
VERIFIED: scanning `Consumers/` shows no POS sale/return/cashbox projection consumer.

## Redis Integration

General platform read-cache; POS operational reads are SQL-authoritative. No POS-specific Redis
model identified.

## Elasticsearch Integration

Single authority for product search (storefront). Not used for POS operational reads. POS stock
changes feed the shared product stock search index (`ProductStockChangedConsumer`) but that is a
catalog projection, not a POS read model.

---

## Outbox

POS uses two outbox mechanisms:

1. **Accounting Outbox** (`AccountingOutboxMessages` + `IAccountingOutboxService`):
   - Events: `PosSale`, `PosPurchase`, `PosRefund`, `PosExpense`, `PurchaseReturn`
     (`IAccountingOutboxService.cs:37-123`; `AccountingOutboxProcessor.cs:52-59`, `:718-725`).
   - Ordering: versioned by `AccountingMutationVersion`; stale versions skipped
     (`AccountingOutboxProcessor.cs:971-993`).
   - Retry: generic `OutboxMessage.MaxRetries = 5` (`OutboxPublisherJob.cs:83`); critical events block
     the aggregate stream and escalate to `RequiresOperatorReconciliation` on exhaustion, then stay on
     a **15-minute slow replay** so a transient outage cannot desync POS/accounting
     (`OutboxPublisherJob.cs:33`, `:146-236`).
   - Replay / Idempotency: outbox row marked processed in the same transaction as the synchronous
     post (`SalesController.cs:418-425`); processor skips if version mismatched.
   - Dead Letters: `RequiresOperatorReconciliation` flag for exhausted critical events
     (`OutboxPublisherJob.cs:183-223`); non-critical exhaustion is terminal-dropped.
2. **Stock Sync Outbox** (`StockSyncOutboxMessages`): committed stock events staged inside the sale/
   return transaction (`ProductVariantStockMutationSupport.PublishCommittedStockEventsAsync`,
   `SalesController.cs:429-435`; `SalesReturnsController.cs:949-955`). `MaxRetries = 8`
   (`StockSyncOutboxMessage.cs:162`); warehouse-scoped; canonical order
   `EventVersion→StockUpdatedAtUtc→OccurredAtUtc→CreatedAt→Id`.

POS does **not** use the generic `OutboxMessage`/MassTransit event publisher for the sale
transaction (the `_eventPublisher` field is injected but never called in the POS sale/return path —
verified by grep of `SalesController.cs`/`SalesReturnsController.cs`: declaration-only, zero `.Publish` calls).

---

## Event Flow

```
POS sale/return/purchase (SQL tx)
   ├─> AccountingOutboxMessages (PosSale/PosPurchase/PosRefund)  [staged + synchronously posted]
   │       └─> AccountingOutboxProcessor (Hangfire 5s)  ─> JournalEntries (CommerceAccountingService)
   ├─> StockSyncOutboxMessages (ProductVariantStockChangedEvent) [staged in tx]
   │       └─> stock sync worker ─> product stock search index (shared)
   └─> AuditLogs (append-only audit)  [SalesController.cs:450-461]
```
No POS sale-domain events are published to the MassTransit/`OutboxMessage` bus from the sale path.

## Read Models

SQL (direct). Receipts/reports/dashboards query `SalesInvoices`, `Returns`, `CashboxTransactions`.

## Write Models

SQL aggregates inside one transaction, plus the two outboxes staged in the same transaction.

---

## Runtime Authority Matrix

| Concept | Owner (write) | Mutation Authority | Read Authority | Projection Authority | Runtime Truth |
|---|---|---|---|---|---|
| Cart (POS) | cashier client | client-held request | n/a (no server cart) | none | request body (VERIFIED GAP for server cart) |
| Cart Line (POS) | `SalesController` | controller | SQL at sale time | none | `SalesInvoiceItem` |
| Product | Catalog (deferred) | Catalog service | SQL `Products` | Mongo product doc | SQL |
| Variant | Catalog (deferred) | Catalog service | SQL `ProductVariants` | Mongo | SQL |
| Price | cashier terminal (POS) | controller accepts inline | SQL `SalesInvoiceItem.UnitPrice` | none | persisted invoice price |
| Promotion | PromotionEngine (storefront) | not used by POS | n/a | n/a | VERIFIED GAP in POS |
| Coupon | Coupon service (storefront) | not used by POS | n/a | n/a | VERIFIED GAP in POS |
| Customer | Customer service (deferred) | POS reads/ledger only | SQL `Customers` | none (POS) | SQL |
| Inventory | Inventory service (deferred) | POS mutates directly | SQL `ProductVariantStocks` (locked) | none (POS) | SQL |
| Reservation | Storefront `CheckoutService` | not used by POS | n/a | n/a | VERIFIED GAP in POS |
| Payment | `SalesController`/ledger | controller + `ILedgerService` | SQL `SalesInvoicePayments`/`CustomerTransactions` | none | SQL |
| Invoice | `SalesController`/`PurchasesController` | controller | SQL | none | SQL |
| Receipt | `SalesController.GetReceipt` | read-only derive | SQL | none | SQL |
| Shift/Cashbox | `CashboxController` | controller + ledger | SQL `Cashboxes` (recomputed) | none | SQL ledger |
| Cashier | `PosSessionAuthority` + `Users` | identity service | SQL `Users` | none | SQL |
| Return | `SalesReturnsController` | controller | SQL `Returns` | none | SQL |
| Refund | `SalesReturnsController` + accounting outbox | controller + outbox | SQL + `JournalEntries` | none | SQL |
| Analytics | reporting controllers | SQL reads | SQL | (purchase analytics consumer) | SQL |

---

## Cross-Domain Authority Graph

POS integrates (reads/writes/events) with:

- **Merchant Platform / Catalog**: reads `Product`, `ProductVariant`, `Warehouse` (SQL). Does not
  author catalog.
- **Inventory**: POS **writes** `ProductVariantStocks` directly (not via Inventory service API);
  emits `StockSyncOutboxMessages` → shared stock search index. Inventory service owns the schema;
  POS owns the mutation under lock.
- **Pricing / Promotion / Coupon**: POS **does not call** these engines (verified). Price integrity
  delegated to the cashier client.
- **Treasury / Accounting**: POS **writes** `AccountingOutboxMessages` only; `AccountingOutboxProcessor`
  + `CommerceAccountingService` post `JournalEntries`. POS never writes GL directly.
- **Payments lifecycle (`IPaymentLifecycleService`)**: NOT used by POS (that is CommerceHub/storefront).
  POS payment is inline ledger + accounting outbox.
- **Shipping**: not used by POS.
- **CommerceHub / Affiliate / SuperAdmin**: not invoked by POS sale path.
- **Analytics**: POS reads SQL for reports; does not emit analytics domain events.
- **Notifications (WhatsApp etc.)**: not emitted from POS sale path (verified by grep).

**Ownership**: POS owns its own SQL aggregates + the two outboxes. It defers catalog authoring,
pricing/promo/coupon computation, payment-lifecycle orchestration, GL posting, and shipping to
their owning services. Synchronization is one-directional: POS → stock-sync outbox → search index;
POS → accounting outbox → GL.

---

## Business Invariants (implementation-backed)

1. **Single transaction per sale/return/purchase** — all mutations commit or roll back together
   (`SalesController.cs:131`; `SalesReturnsController.cs` transaction).
2. **Stock never goes negative at POS** — `allowNegativeStock:false` + lock
   (`SalesController.cs:228-232`).
3. **Over-return is impossible** — `priorReturnedTotal + returnTotal <= originalInvoiceTotal`
   (`SalesReturnsController.cs:804-810`); only `Completed` invoices returnable (`:438-441`).
4. **Idempotent sales** — same key + same payload replays stored response; divergent payload rejected
   (`SalesController.cs:513-560`).
5. **Accounting posted exactly once** — synchronous post + outbox row marked processed in same tx;
   stale versions skipped (`AccountingOutboxProcessor.cs:971-993`).
6. **Pending/failed accounting blocks mutation** — fail-closed financial gate
   (`SaleFinancialIntegrityService.cs:110-140`).
7. **Session authority is fail-closed** — claims evidence only; SQL decides
   (`PosSessionAuthority.cs:46-124`).
8. **Cashbox balance is ledger-derived**, not the cached column (`CashboxController.cs:67-69`).
9. **Deterministic lock order** prevents stock deadlocks (`ProductVariantStockMutationSupport.cs:300-304`).
10. **Refund must allocate full return total** for cash/card/mixed; otherwise AccountCredit
    (`:844-850`).

---

## Architectural Decisions (WHY)

- **Why SQL remains authority**: POS is cash/ledger-critical; eventual projections would risk
  selling out-of-stock or double-posting. Stock and balance are read under lock from SQL
  (`SalesController.cs:202-232`).
- **Why reservation is absent in POS**: a human cashier operates one counter; the race that
  storefront faces (concurrent anonymous checkouts) does not apply. POS deducts immediately under a
  transaction lock instead of reserving-then-committing. (Storefront keeps `InventoryReservation`
  with TTL + cleanup job.)
- **Why inventory is revalidated on return**: the original invoice is write-locked for the whole
  return so concurrent returns can't both pass the over-return check against a stale snapshot
  (`SalesReturnsController.cs:423-426`).
- **Why projections (mostly) don't exist for POS**: correctness must not depend on projection
  convergence; the cashbox explicitly recomputes from ledger (`CashboxController.cs:67-69`).
- **Why Outbox exists**: to guarantee accounting/journal posting exactly once despite crashes, while
  letting the request post synchronously so latency is low and the worker is only recovery
  (`SalesController.cs:406-425`).
- **Why replay exists**: `AccountingMutationVersion` lets the processor skip stale messages and
  prevents a slow out-of-order event from corrupting GL (`AccountingOutboxProcessor.cs:971-993`).
- **Why Fail-Closed design exists**: outward money (refunds) and structurally important mutations
  (sales) must never proceed on uncertain state — pending/failed outbox, banned tenant, rotated
  session all block (`SaleFinancialIntegrityService.cs:110-140`, `PosSessionAuthority.cs:102-122`).
- **Why POS doesn't reuse `CheckoutService`**: POS is a different trust/UX model (authenticated
  cashier, inline prices, immediate stock deduction, synchronous accounting). Forcing it through the
  storefront pipeline would couple cash-integrity to e-commerce concerns.

---

## Technical Debt (implementation-backed)

1. **Pricing/Promotion/Coupon not enforced server-side in POS** — price integrity depends on the
   cashier client (`SalesController.cs` takes inline `UnitPrice`). Risk: mispricing/discount fraud
   at the terminal. (Evidence: no pricing/promo/coupon service injected in `SalesController.cs:48-90`.)
2. **No server-side POS cart** — hold/resume/merge/cancel/expiration semantics absent; POS "cart" is
   the client request body. Risk: lost-cart on client crash, no server recovery.
3. **`InvoiceStatus.PartiallyReturned`/`FullyReturned` defined but never set** — the return path only
   increments `AccountingMutationVersion` and checks `Status == Completed`; it never transitions the
   invoice to partially/full-returned (`SalesReturnsController.cs:438`, `:863-864`; enum
   `InvoiceStatus.cs:8-9`). Reports relying on those statuses will be wrong.
4. **BankTransfer tender rejected for direct POS sale** — enum has it but `EnsureSupportedPaymentMethod`
   allows only Cash/Card/Mixed (`SalesController.cs:2580-2588`). Inconsistent with the enum.
5. **POS sale path doesn't emit domain events** — `_eventPublisher` injected but unused in sale/return
   (`SalesController.cs` grep). Analytics/realtime features that depend on sale events won't fire for
   POS. This is also why there is no POS Mongo projection.
6. **Cashbox cached balance columns are dead/trust-breaking** — `CurrentBalance`/`ExpectedBalance`
   are `[Obsolete]` and code forbids using them (`Cashbox.cs:23,50`; `CashboxController.cs:170,403-404`);
   drift risk if another caller reads them.
7. **Cash-out has no privilege gate** — `CashOut`/`RecordExpense` are only `[Authorize]` (any
   authenticated user) while `CashIn`/`OpenCashbox` require `Owner,Admin` (`CashboxController.cs:678`
   vs `:807,:942`). Separation-of-duties weakness: a cashier can take money out but not put it in.
8. **Duplicate pricing / hidden coupling** — `CommerceAccountingService.PostPosSaleAsync` is shared
   with CommerceHub financial posting; POS and marketplace post through the same journal logic,
   which couples POS GL shape to marketplace evolution (`CommerceAccountingService.cs:47`).
8. **Partial migration** — legacy treasury tables still referenced in
   `SaleFinancialIntegrityService.RevertLegacyTreasuryEffectsAsync` (`:12-13` "Legacy treasury tables
   are intentionally ignored"), indicating an incomplete migration off legacy treasury.

---

## Implementation Follow-up Items

| Title | Current Implementation | Why | Future Improvement | Priority | Status |
|---|---|---|---|---|---|
| Enforce pricing server-side in POS | Inline `UnitPrice` from client | Terminal trust model | Invoke `StorefrontPricingService`/price authority for POS or add a POS price policy | High | Open |
| Server-side POS cart | Client request body only | Thin MVP | Add held-cart aggregate with resume/merge/cancel | Medium | Open |
| Set return statuses | Only `AccountingMutationVersion++` | Speed | Transition invoice to `PartiallyReturned`/`FullyReturned` | Medium | Open |
| Allow BankTransfer tender | Rejected in `EnsureSupportedPaymentMethod` | Unclear | Decide policy; if allowed, extend validation | Low | Open |
| Emit POS sale events | `_eventPublisher` unused | Simplicity | Publish `SaleCreatedEvent`/`ReturnCreatedEvent` for analytics/realtime | Medium | Open |
| Remove dead cashbox balance columns | `CurrentBalance`/`ExpectedBalance` forbidden | Legacy | Drop columns or make them authoritative + maintained | Low | Open |
| Complete treasury migration | Legacy tables ignored in integrity service | Partial migration | Finish cutover; remove legacy revert path | Low | Open |

---

## Certification

- **Implementation Confidence**: HIGH for Sales / Purchases / Returns / Refunds / Cashbox / Stock /
  Accounting-outbox / Session-authority (read directly from controllers + services with file:line).
- **Architecture Completeness**: HIGH for the execution engine; MEDIUM for Cart (server-side gap),
  Promotion/Coupon (not used), Printing (client-side), Analytics (SQL-read only).
- **Runtime Authority Verification**: verified that POS owns its SQL aggregates and defers
  pricing/promo/coupon/payment-lifecycle/GL to owning services.
- **Cross-Domain Verification**: verified POS→Inventory (direct stock write + stock-sync outbox),
  POS→Accounting (accounting outbox), POS reads Catalog/Warehouse; does NOT call Pricing/Promo/Coupon/
  PaymentLifecycle/CommerceHub/Affiliate/SuperAdmin.
- **Verified Capabilities**: atomic sales, returns with inventory restoration + ledger reversal,
  synchronous accounting posting with outbox recovery, fail-closed session + financial gates,
  idempotent execution, cashbox open/close/cash-in/out/expense, mixed/cash/card tender with change,
  credit sales + account-credit refunds.
- **Missing Capabilities (VERIFIED GAPS)**: server-side POS cart (hold/resume/merge); promotion &
  coupon evaluation in POS; bank-transfer/wallet POS tender; server-side thermal printing; POS sale
  domain-event emission + Mongo analytics projection; offline sales / offline queue / sync
  (no offline mode in `SalesController`/`CashboxController`/`PurchasesController`/`SalesReturnsController`).
- **Technical Debt Summary**: client-trusted pricing, no POS cart, unused return statuses, rejected
  bank tender, no POS events, dead cashbox columns, shared CommerceHub accounting coupling, partial
  treasury migration.
- **Architectural Observations**: POS is a deliberately "SQL-authority, no-projection, synchronous-
  post" slice of the platform — the opposite end of the spectrum from Storefront's
  reservation+event+projection model. This is coherent for a cash-critical, human-operated context,
  but it leaves pricing integrity and cart resilience as the two largest open risks.

---

## POS Transaction Lifecycle (one complete retail sale)

```
Cashier Login / Session
  Runtime Owner: PosSessionAuthority (+ JWT)
  Runtime Authority: SQL Users/Tenant/Governance (fail-closed)
  SQL Authority: Users, TenantLineage, UserGovernanceProfiles
  Projection Authority: none
  Events Published: none (rejected -> 401)
  Events Consumed: none
  Synchronization: token validated against SQL on every request (Program.cs:2305-2321)
  Failure Behaviour: deny (rotated/banned/mismatch) -> context.Fail
  Replay: n/a  Idempotency: session binding claim

Shift Validation (cashbox open)
  Runtime Owner: CashboxController.OpenCashbox
  Runtime Authority: ledger-derived (not cached column)
  SQL Authority: Cashboxes
  Projection Authority: none
  Events Published: none
  Failure Behaviour: "already open" rejected
  Replay/Idempotency: n/a

Customer Selection (optional)
  Runtime Owner: SalesController (LoadActiveCustomerAsync)
  SQL Authority: Customers
  Projection Authority: none
  Failure Behaviour: "Customer not found" -> rollback

Barcode Scan / Variant Resolution
  Runtime Owner: SalesController.PrepareSaleItemsAsync
  SQL Authority: ProductVariants, Products
  Read Authority: SQL (NOT projection)
  Failure Behaviour: unresolved variant -> rollback

Pricing
  Runtime Owner: cashier client supplies UnitPrice; server validates totals
  SQL Authority: SalesInvoiceItem.UnitPrice (persisted)
  Note: pricing ENGINE not invoked (technical debt)

Promotion / Coupon
  Runtime Owner: none in POS (VERIFIED GAP)

Inventory Validation + Lock
  Runtime Owner: ProductVariantStockMutationSupport.LockMultipleStocksAsync
  SQL Authority: ProductVariantStocks (locked, deterministic order)
  Projection Authority: none
  Failure Behaviour: InsufficientStockException -> rollback (Conflict 409)
  Revalidation: re-checked inside lock

Stock Deduction
  Runtime Owner: SalesController (variantStock.Quantity -= qty)
  SQL Authority: ProductVariantStocks + StockMovements (append)
  Events Published: StockSyncOutboxMessage (staged in tx)

Payment
  Runtime Owner: SalesController.ValidateAndNormalizePaymentAmounts
  SQL Authority: SalesInvoicePayments (ledger)
  Failure Behaviour: underpay/invalid split -> ValidationException
  Change: PaidAmount - TotalAmount

Invoice
  Runtime Owner: SalesController (SalesInvoices insert)
  SQL Authority: SalesInvoices + Items
  Invoice Number: INV-yyyyMMdd-NNNN

Accounting (Ledger + GL)
  Runtime Owner: IAccountingOutboxService.EnqueuePosSaleAsync + CommerceAccountingService.PostPosSaleAsync
  SQL Authority: AccountingOutboxMessages + JournalEntries
  Events Published: PosSale outbox (synchronous post; worker = recovery)
  Failure Behaviour: post failure -> throw -> rollback
  Replay: version-skip (AccountingMutationVersion)
  Idempotency: outbox marked processed in same tx

Inventory Update (committed stock event)
  Runtime Owner: ProductVariantStockMutationSupport.PublishCommittedStockEventsAsync
  SQL Authority: StockSyncOutboxMessages
  Projection Authority: shared product stock search index (consumer)

Outbox (stock sync)
  Events Published: ProductVariantStockChangedEvent -> search index

Receipt
  Runtime Owner: SalesController.GetReceipt (on demand)
  Read Authority: SQL SalesInvoice
  Duplicate Prevention: idempotent read

Analytics
  Read Authority: SQL reports (no POS event emitted)

Projection Refresh
  POS operational aggregates: none (SQL-authority, no Mongo projection)
```

---

## Architectural Synthesis

The POS Runtime is the platform's **fail-closed, SQL-authoritative, synchronous-post** extreme.
Where Storefront/CommerceHub use reservation + event bus + Mongo/Redis/ES projections + asynchronous
reconciliation, POS deliberately collapses that to: one SQL transaction, direct stock mutation under
lock, synchronous accounting posting with an outbox that exists purely for crash recovery, and no
operational projection.

The shared architectural patterns it participates in (and how POS expresses them):

- **Runtime Authority**: POS owns its aggregates; every mutation is SQL. It reads (does not own)
  Catalog/Variant/Warehouse/Customer.
- **SQL Authority**: absolute for POS. Stock, balance, cashbox all read under lock or recomputed from
  ledger.
- **CQRS**: present only as the accounting outbox + stock-sync outbox (write-side staging); the
  read-side projection is intentionally absent for POS operational data.
- **Outbox**: two outboxes (accounting, stock-sync). Both staged inside the business transaction;
  accounting is posted synchronously so the worker is recovery-only.
- **Projection**: cashbox repair-on-read from ledger; otherwise no POS projection.
- **Replay**: `AccountingMutationVersion` monotonic stream; stale messages skipped.
- **Repair-on-Read**: cashbox balance recomputed from ledger (`CashboxController.cs:67-69`).
- **Fail-Closed Design**: `PosSessionAuthority` (session), `SaleFinancialIntegrityService` (pending
  outbox), over-return guard, negative-stock guard.
- **Cross-Domain Isolation**: POS never calls Pricing/Promo/Coupon/PaymentLifecycle/CommerceHub/
  Affiliate/SuperAdmin. It integrates only via SQL reads and the two outboxes.
- **Availability Gates**: session binding, tenant-lineage, governance status, negative-stock,
  over-return — all gate the transaction before commit.
- **Ownership Boundaries**: POS writes its own SQL + outboxes; defers GL posting to
  `AccountingOutboxProcessor`+`CommerceAccountingService`, stock-search projection to the shared
  consumer, catalog authoring to the Catalog service.

The single most important architectural observation: **POS trades the rich CQRS/projection machinery
of the rest of the platform for simplicity and cash-integrity**. That trade is correct for a
human-operated, single-actor counter, but it concentrates risk in two places the rest of the platform
has solved: (1) **price integrity is client-trusted** (no pricing engine in POS), and (2) **there is
no server-side cart** (no hold/resume/recovery). Both are the top follow-up items.
