# FLOW-01 — Cash Sale Business Flow: Implementation-Backed Canonical Architecture

> Authority rule: Implementation is the ONLY source of truth. Every claim below is cited to
> `file:line` in the backend. Controllers, DTOs, comments, and naming are evidence only — where
> they contradict runtime behavior, behavior wins. This document describes ONE complete business
> transaction: the POS cash sale, executed by `PointOfSale.API/Controllers/SalesController.cs`
> `CreateSale` (line 98).
>
> Verification note: all line numbers below were confirmed by reading the live backend.
> Repo root: `C:\Users\fadya\source\repos\PointOfSale`.

---

## 0. Executive Summary

### What is a Cash Sale?

A **Cash Sale** is a Point-of-Sale retail transaction where a merchant operator, at a physical
counter, sells one or more product variants from a warehouse and receives payment immediately in
cash, card, or a cash+card mix. It is executed by a SINGLE synchronous HTTP call to
`POST /api/sales` (`SalesController.CreateSale`, `SalesController.cs:98`). The entire transaction —
invoice creation, stock decrement, ledger/customer-balance mutation, and the full double-entry
accounting journal — is committed atomically inside ONE PostgreSQL transaction.

### Why does it exist?

It is the operational cash-register primitive of Shopixy's merchant platform. Unlike CommerceHub
marketplace orders (which are asynchronously fulfilled, shipped, and settled across vendors) or the
Storefront checkout (which creates a CommerceHub order and reserves stock), the POS cash sale is an
**immediate, synchronous, fully-settled** mutation of inventory + financial state. There is no
payment-gateway round trip, no fulfillment lifecycle, and no outward money movement: the cash is
booked to a GL cashbox account at the moment of sale.

### How does Shopixy execute it? (the 12 runtime facts)

1. **One transaction.** Begins at `SalesController.cs:131` (`_context.Database.BeginTransactionAsync()`,
   PostgreSQL default ReadCommitted) and commits at `SalesController.cs:937` — the WHOLE sale lives
   in this transaction, including the accounting journal.
2. **Idempotency is mandatory and enforced at the boundary.** The request MUST carry
   `X-Payment-Attempt-Id` or `X-Pos-Cart-Id` (lines 100-128). A deterministic request hash
   (`BuildSalesRequestHash`, line 130) plus a claim-token + byte-exact stored response
   (`CompleteSalesIdempotencyAsync`, line 905) makes retries safe.
3. **Pessimistic stock locking, not optimistic.** Stock rows are locked with
   `LockMultipleStocksAsync` (`SalesController.cs:202`; impl `ProductVariantStockMutationSupport.cs:292`),
   which locks in deterministic (WarehouseId, VariantId) order to prevent deadlocks.
4. **Negative stock is impossible two ways.** App guard `ValidateAvailableQuantity(allowNegativeStock:false)`
   at `SalesController.cs:228`, AND a DB CHECK constraint `CK_ProductVariantStocks_Quantity_NonNegative`
   (`"Quantity" >= 0`, confirmed in migration `20260419175541...Designer.cs:9316`).
5. **The ledger is the accounting authority, not CashboxTransaction.** `_eventPublisher` is injected
   (line 58) but NEVER used in `SalesController`. The cash impact is posted via a GL journal to a
   per-warehouse **cashbox ledger account** (`CommerceAccountingService.ResolveWarehouseCashboxAccountCodeAsync`,
   `CommerceAccountingService.cs:1486` → `CashboxLedgerAccountService.ResolveWarehouseCashboxAccountAsync`,
   `CashboxLedgerAccountService.cs:299`).
6. **Cashbox entity balance is derived, never written by the sale.** `CashboxController.cs:23` states
   "DO NOT use CurrentBalance/ExpectedBalance for validation or business logic. Ledger is authoritative."
7. **Two journals are posted synchronously.** Revenue journal (debit cashbox/Visa/AR, credit PosSales)
   + optional COGS journal (debit COGS, credit Inventory), `CommerceAccountingService.PostPosSaleAsync`
   (`CommerceAccountingService.cs:47`). Each journal is balanced by `FinancialPostingService` Rule 4
   (`FinancialPostingService.cs:437`).
8. **Accounting outbox is staged inside the sale tx, then marked processed in the SAME tx.** The
   synchronous post is the executor; the background worker is ONLY a recovery path
   (`SalesController.cs:400-425`, `MarkSynchronousSaleAccountingOutboxProcessedAsync` line 1823).
9. **Customer balance flows through the ledger, not a separate balance table.** For a sale to a
   customer, `_ledgerService.CreateLedgerEntry` updates `customer.Balance` internally
   (`SalesController.cs:330`, `LedgerService.ApplyPartyTransactionAsync`); `CustomerTransaction` rows
   are appended for history only, capturing the post-mutation `BalanceAfter`
   (`SalesController.cs:343-357, 382-396`).
10. **POS sale emits NO Mongo/ES/Redis domain events.** The only out-of-transaction artifacts are
    (a) the `StockSyncOutboxMessage` rows for inventory sync (`SalesController.cs:431`) and
    (b) the `AccountingOutboxMessage` row (already marked processed). No `ProductDocument`/search
    projection is touched by a POS sale — analytics reads SQL directly.
11. **Credit is NOT a valid POS payment method.** `EnsureSupportedPaymentMethod` (`SalesController.cs:2580`)
    accepts only `Cash`, `Card`, `Mixed`. The prompt's "Credit Sale" does not exist on this endpoint.
12. **Failure rolls back the ENTIRE transaction.** Every catch block calls `transaction.RollbackAsync()`
    and `_context.ChangeTracker.Clear()` (`SalesController.cs:467, 482, 502`), so a stock, payment,
    or accounting failure leaves zero partial state.

---

## 1. Business Goal (implementation-derived)

- **Business Purpose:** Record an immediate, fully-paid retail sale at a register; decrement warehouse
  stock; update the customer's receivable/paid balance; book cash to the tenant's cashbox GL account;
  and post a balanced accounting journal — atomically.
- **Business Actor:** A tenant operator with role `Owner/Admin/Manager/Cashier` (POS-operator JWT,
  `AuthController.cs:104`). Not a customer.
- **Business Preconditions:**
  - A valid idempotency header (X-Payment-Attempt-Id or X-Pos-Cart-Id) — enforced lines 124-128.
  - `WarehouseId` resolves to an active warehouse (`LoadActiveWarehouseAsync`, line 150).
  - Every item's `ProductId`/`ProductVariantId` resolves (`PrepareSaleItemsAsync`,
    lines 1116-1254); a default or explicit unit with `ConversionFactor > 0` exists.
  - Sufficient stock (after lock) for every line (`ValidateAvailableQuantity`, line 228).
  - Payment method ∈ {Cash, Card, Mixed} (`EnsureSupportedPaymentMethod`, line 2580).
  - If `CustomerId` supplied, it resolves to an active customer (line 161).
- **Business Trigger:** HTTP `POST /api/sales` from the POS client (PointOfSale.Web or the cashier
  SPA) with a populated cart.
- **Business Completion Conditions:** The transaction commits (line 937) and the client receives
  `200 OK` with `SaleResponse` (InvoiceId, InvoiceNumber, TotalAmount, PaidAmount, ChangeAmount)
  at lines 440-448.
- **Business Failure Conditions:** Any `BadRequest`/`Conflict`/`500` returned in the catch blocks
  (lines 465-510) — all with full rollback. Idempotency key reuse with divergent payload → 400
  (line 565).
- **Business Invariants (code-enforced):**
  - `debits == credits` in every journal (`FinancialPostingService.cs:437`).
  - Stock `Quantity >= 0` (app guard + DB CHECK).
  - `Invoice.TotalAmount` recomputed authoritatively server-side; client prices are overwritten by
    `ResolveAuthoritativeSaleUnitPrice` and `product.TaxRate` (lines 1220-1238) — **price tampering
    is prevented**.
  - Cash payment must cover total (`roundedPaidAmount < roundedTotalAmount` → 400, line 2646).
  - Mixed split must equal total within 0.01 (line 2626).
  - Idempotency payload hash must match the bound key (line 557).

---

## 2. Runtime Collaboration (reverse-engineered)

POS sale is a SINGLE-request, single-runtime (POS) transaction. It does NOT fan out to separate
microservices at write time. The "runtimes" it collaborates with are in-process services/aggregates
resolved inside `SalesController` + `CommerceAccountingService` + `LedgerService` + `CashboxLedgerAccountService`.

| Runtime (logical) | Concrete owner in code | Purpose | Aggregate Owner | Read Model | Write Model | Authority | Tx Participation | Sync |
|---|---|---|---|---|---|---|---|---|
| POS Runtime | `SalesController` + `ApplicationDbContext` | Execute the sale, own `SalesInvoice`/`SalesInvoiceItem`/`StockMovement`/`CustomerTransaction` | `SalesInvoice` (agg root) | `SalesInvoice` (SQL) | `SalesInvoice`, `SalesInvoiceItem`, `StockMovement`, `CustomerTransaction` | SQL (single DbContext) | Owns the transaction (begin 131 / commit 937) | Synchronous |
| Inventory Runtime | `ProductVariantStockMutationSupport` + `ProductVariantStocks` | Lock & decrement variant stock; emit stock-sync outbox | `ProductVariantStock` | `ProductVariantStock` (SQL) | `ProductVariantStock.Quantity` | SQL (pessimistic lock) | Inside POS tx | Synchronous lock (202) |
| Treasury / Cashbox | `CashboxLedgerAccountService` | Resolve which GL account receives cash (per-warehouse cashbox account) | `TreasuryAccount` (GL) | `TreasuryAccount` | (read-only resolution; balance mutated by journal) | SQL read | Read inside POS tx (via accounting) | Synchronous read |
| Accounting Runtime | `CommerceAccountingService` + `FinancialPostingService` + `AccountingOutboxService` | Post balanced journals; stage+mark outbox | `JournalEntry`, `LedgerAccount`, `AccountingOutboxMessage` | `JournalEntry` (SQL) | `JournalEntry`, `LedgerAccount.CurrentBalance` | SQL | Inside POS tx (411-425) | Synchronous |
| CRM-Party Runtime | `LedgerService` + `Customers` | Update customer balance via ledger; append `CustomerTransaction` | `Customer`, `CustomerTransaction` | `Customer.Balance` (SQL) | `Customer.Balance`, `CustomerTransaction` | SQL (ledger authoritative) | Inside POS tx (330-396) | Synchronous |
| Catalog Runtime | `Products`/`ProductVariants`/`ProductUnits` (read) | Resolve product/variant/unit + authoritative price/tax | (read-only) | `Products` | — | SQL read | Read inside POS tx (1125-1247) | Synchronous |
| Inventory Sync (outbox) | `StockSyncOutboxProcessor` (background) | Replicate `ProductVariantStock` change to downstream read models | `StockSyncOutboxMessage` | downstream projections | — | Outbox (SQL) | OUTSIDE POS tx (deferred) | Async, background |

KEY: There is no cross-process call on the write path. All authoritative mutations happen inside the
one PostgreSQL transaction owned by `SalesController`. The only deferred work is the
`StockSyncOutboxProcessor` (inventory read-model replication) and the (already-processed)
`AccountingOutboxMessage` recovery row.

## 3. Complete Execution Timeline (implementation — verbatim path)

```
POS client POST /api/sales
  headers: X-Payment-Attempt-Id | X-Pos-Cart-Id  (SalesController.cs:100-128)
    ↓
[1] Validate idempotency key format; require ≥1 key            (lines 104-128)
[2] Build deterministic request hash                            (line 130)
[3] Begin SQL transaction (ReadCommitted)                      (line 131)
[4] TryClaimSalesIdempotencyAsync — insert claim w/ token,
    or short-circuit on HIT / CONFLICT                          (line 139, 513-615)
    ↓ (on conflict) ROLLBACK + 400 "Idempotency key reuse"
    ↓ (on hit) ROLLBACK + stored 200 response
[5] EnsureSupportedPaymentMethod (Cash|Card|Mixed only)         (line 147, 2580)
[6] LoadActiveWarehouseAsync — 400 if missing                   (line 150)
[7] LoadActiveCustomerAsync (if CustomerId) — 400 if missing    (line 161)
[8] GenerateInvoiceNumberAsync                                  (line 170)
[9] PrepareSaleItemsAsync:
      batch-load Products, ProductUnits, resolve Variants       (lines 1116-1247)
      overwrite UnitPrice = authoritative (variant.Price*conv)   (line 1220, 2566)
      TaxRate = product.TaxRate (authoritative)                 (line 1222)
      reject discount > line subtotal                            (line 1226)
[10] LockMultipleStocksAsync — pessimistic lock, ordered        (line 202, 292)
[11] PER ITEM:
      - variantStock == null → ROLLBACK + 400 "Insufficient stock" (line 219)
      - ValidateAvailableQuantity(allowNegative:false)          (line 228)
      - compute itemSubtotal/tax/total                           (lines 234-238)
      - variantStock.Quantity -= baseQuantity                    (line 270)
      - add StockMovement (Sale, -qty)                           (line 272)
      - stage pendingStockChange                                 (line 294)
[12] ApplySaleTotals (subtotal/discount/tax/total)              (line 307, 2534)
[13] ValidateAndNormalizePaymentAmounts (cash≥total, mixed=split) (line 314, 2590)
[14] _context.SalesInvoices.Add(invoice)                        (line 324)
[15] IF customer:
      - CreateLedgerEntry(Sale)  → debit customer, update Balance (line 330)
      - add CustomerTransaction (Sale, BalanceAfter)             (line 343)
      - IF paid>0: CreateLedgerEntry(PaymentReceived) credit     (line 369)
      - add CustomerTransaction (PaymentReceived)                (line 382)
[16] EnqueuePosSaleAsync → AccountingOutboxMessage row           (line 406, 39)
[17] PostPosSaleAsync → Revenue journal (debit cashbox/visa/AR,
      credit PosSales) + optional COGS journal                   (line 411, 47)
        ↳ throws if !Success → caught → ROLLBACK + 500           (line 412-416)
[18] MarkSynchronousSaleAccountingOutboxProcessedAsync
      (mark outbox row Processed + JournalId, same tx)           (line 418, 1823)
[19] PublishCommittedStockEventsAsync → StockSyncOutboxMessage   (line 431, 216)
[20] CompleteSalesIdempotencyAsync:
      verify claim token, snapshot response JSON,
      SaveChangesAsync + CommitAsync                             (line 437, 905-937)
[21] Audit log (fire-and-forget, wrapped in try)                (line 450)
[22] 200 OK SaleResponse                                         (line 463)
```

OUTSIDE the transaction (deferred, background):
```
StockSyncOutboxProcessor.ProcessPendingMessagesAsync
  → reads StockSyncOutboxMessage (not yet PublishedAtUtc)
  → replicates ProductVariantStock change to downstream read models
  → InventoryEventInbox dedup (unique UX_InventoryEventInbox_TenantId_EventId)
```

## 4. Business Rule Discovery (every rule traced to code)

| # | Rule | Implementation evidence |
|---|---|---|
| 1 | Idempotency header required | `SalesController.cs:124-128` (400 if null) |
| 2 | Idempotency key format validated | `ValidateSalesIdempotencyKey` line 942 (regex+length) |
| 3 | Same key + different payload rejected | line 557-568 (400 "reuse with different payload") |
| 4 | Replay returns byte-exact prior response | `CompleteSalesIdempotencyAsync` line 905-937 |
| 5 | Payment method only Cash/Card/Mixed | `EnsureSupportedPaymentMethod` line 2580-2588 |
| 6 | Warehouse must exist & active | `LoadActiveWarehouseAsync` line 150, 1264 |
| 7 | Customer (if given) must exist & active | `LoadActiveCustomerAsync` line 161, 1256 |
| 8 | Product must exist | `PrepareSaleItemsAsync` line 1151 (400 "Product not found") |
| 9 | Variant required | line 1212 (400 "select a variant") |
| 10 | Unit conversion valid (>0) | line 1175, 1198 |
| 11 | Authoritative price (client price overwritten) | `ResolveAuthoritativeSaleUnitPrice` line 2566; applied 1220 |
| 12 | Authoritative tax rate (product.TaxRate) | line 1222 |
| 13 | Discount ≤ line subtotal | line 1226 |
| 14 | Invoice-level discount valid & ≤ subtotal | `ApplySaleTotals` line 2534-2563 (throws ValidationException) |
| 15 | Stock lock acquired before mutation | `LockMultipleStocksAsync` line 202, 292 |
| 16 | Stock cannot go negative (app) | `ValidateAvailableQuantity(allowNegativeStock:false)` line 228 |
| 17 | Stock cannot go negative (DB) | CHECK `CK_ProductVariantStocks_Quantity_NonNegative` |
| 18 | Quantity positive | derived from `request.Quantity * ConversionFactor` |
| 19 | Cash paid ≥ total | `ValidateAndNormalizePaymentAmounts` line 2646 (400) |
| 20 | Mixed split == total (±0.01) | line 2626 (400) |
| 21 | No card on cash / no cash on card | lines 2636, 2656 |
| 22 | Journal must balance (debit==credit) | `FinancialPostingService.cs:437` |
| 23 | Journal line one-sided, positive | `FinancialPostingService.cs:404-431` |
| 24 | Ledger accounts must exist & active | `FinancialPostingService.cs:443-458` |
| 25 | Cashbox GL account must resolve | `CashboxLedgerAccountService.Resolve...` → else NoCashboxAccountException (line 375) |
| 26 | Tenant scoping mandatory | `_tenantContext.RequiredTenantId` on every aggregate (e.g. line 176) |

## 5. Runtime Ownership (per business step)

| Step | Owning Runtime | Aggregate Root | App Service | Domain Service | Infra Service | Repository | Persistence | Authority |
|---|---|---|---|---|---|---|---|---|
| Validate/idempotency | POS | `SalesRequestIdempotency` | `SalesController` | — | `ProductVariantStockMutationSupport` (helpers) | EF DbSet | SQL | SQL |
| Warehouse/customer validate | POS + CRM | `Warehouse`/`Customer` | `SalesController` | — | — | EF DbSet | SQL | SQL |
| Prepare items | Catalog+POS | `Product`/`ProductVariant`/`ProductUnit` | `SalesController.PrepareSaleItemsAsync` | — | — | EF DbSet | SQL (read) | SQL |
| Stock lock+decrement | Inventory | `ProductVariantStock` | — | `ProductVariantStockMutationSupport` | — | EF DbSet | SQL (pessimistic) | SQL |
| Stock movement | Inventory | `StockMovement` | — | — | — | EF DbSet | SQL | SQL |
| Invoice | POS | `SalesInvoice`/`SalesInvoiceItem` | `SalesController` | — | — | EF DbSet | SQL | SQL |
| Ledger/customer bal | CRM | `Customer`/`CustomerTransaction`/`LedgerEntry` | `LedgerService` | — | — | EF DbSet | SQL | SQL (ledger authoritative) |
| Accounting journals | Accounting | `JournalEntry`/`LedgerAccount` | `CommerceAccountingService` | `FinancialPostingService` | `AccountingOutboxService` | EF DbSet | SQL | SQL |
| Cashbox cash | Treasury | `TreasuryAccount` (GL) | `CashboxLedgerAccountService` (resolve) | — | — | EF DbSet | SQL | SQL (GL balance) |
| Inventory sync | Inventory (outbox) | `StockSyncOutboxMessage` | `StockSyncOutboxProcessor` | — | — | EF DbSet | SQL→read models | Outbox |


## 6. Aggregate Mutation Analysis

All mutations below occur INSIDE the single transaction (begin 131 / commit 937).

### 6.1 SalesInvoice (aggregate root)
- Before: does not exist (new GUID, line 175).
- After: `Status=Completed`, `PaymentMethod`, `CashAmount`/`CardAmount`, `TotalAmount`,
  `InvoiceNumber`, `InvoiceDate=UtcNow`, `AccountingMutationVersion` (used for outbox, line 409).
- Mutation: `_context.SalesInvoices.Add(invoice)` line 324.
- Version/Concurrency: EF concurrency via row version if configured; idempotency claim prevents
  duplicate inserts.
- Rollback: whole-tx rollback on any failure.

### 6.2 SalesInvoiceItem
- One per request line (line 242). Carries `UnitPrice`, `TaxRate`, `TaxAmount`, `DiscountAmount`,
  `TotalAmount`, `BaseQuantity`, `CostPriceSnapshot`, `CostAmountSnapshot` (cost snapshot for COGS).
- After: child of invoice; never mutated post-commit by this flow.

### 6.3 ProductVariantStock
- Before: `Quantity = Q0`. After: `Quantity = Q0 - baseQuantity` (line 270).
- Locking: PESSIMISTIC — `LockMultipleStocksAsync` (line 202) → `LockStockForMutationAsync`
  (row-level lock, ordered by (WarehouseId, VariantId) for deadlock prevention, line 300-304).
- Concurrency: lock-based, not optimistic; the lock is held until commit.
- Negative prevention: `ValidateAvailableQuantity(allowNegativeStock:false)` line 228 + DB CHECK
  `CK_ProductVariantStocks_Quantity_NonNegative`.
- Rollback: tx rollback restores Q0.

### 6.4 StockMovement
- New row per line, `MovementType=Sale`, `QuantityChange=-baseQuantity`, `QuantityAfter`,
  `ReferenceId=invoice.Id`, `ReferenceLineId=invoiceItem.Id` (line 272). Operational history only.

### 6.5 CustomerTransaction (+ Customer.Balance)
- IF customer present: one `Sale` transaction (debit full amount) capturing `BalanceAfter` AFTER
  the ledger update (line 343-357), plus one `PaymentReceived` transaction if paid>0 (line 382-396).
- `BalanceAfter` is read from the in-memory `customer.Balance` AFTER `CreateLedgerEntry` mutated it
  (comment line 341/380) — i.e. the ledger service is the writer of `Customer.Balance`.
- The `CustomerTransaction` rows are APPEND-ONLY history; they are NOT the authority.

### 6.6 JournalEntry / LedgerAccount
- Revenue journal: lines = [debit cashbox/Visa/AR, credit PosSales] (CommerceAccountingService
  line 87-175). COGS journal (if cost>0): [debit COGS, credit Inventory] (line 185-201).
- `LedgerAccount.CurrentBalance` updated inside `PostJournalAsync` (FinancialPostingService).
- Version: outbox `AggregateVersion = invoice.AccountingMutationVersion` (line 409).

### 6.7 AccountingOutboxMessage
- INSERTed by `EnqueuePosSaleAsync` (line 406) with `EventType="PosSale"`, `BusinessEntityId=invoice.Id`,
  `IdempotencyKey="SALE:{tenant}:{invoice}"` (AccountingOutboxService.cs:45).
- Then `MarkProcessed(JournalId)` in the SAME tx (line 1878) — so the background worker never
  double-posts; it only recovers if the synchronous post failed (fail-closed reconciliation).

### 6.8 StockSyncOutboxMessage
- INSERTed by `PublishCommittedStockEventsAsync` (line 431 → ProductVariantStockMutationSupport.cs:246).
- Consumed asynchronously by `StockSyncOutboxProcessor` to replicate stock to downstream read models.

## 7. Financial Flow

### 7.1 Cash received
- Captured as `invoice.CashAmount`/`CardAmount`/`PaidAmount`, normalized by
  `ValidateAndNormalizePaymentAmounts` (line 314, 2590). For Cash sale, CashAmount == PaidAmount
  (line 2651). Change = `PaidAmount - TotalAmount` (line 446) — returned to customer physically,
  NOT booked (it is outside the GL; change is a cash-handling concept, not an accounting entry).

### 7.2 Journal entries (double-entry)
Two journals posted synchronously by `PostPosSaleAsync` (CommerceAccountingService.cs:47):

JOURNAL 1 — Revenue Recognition:
- Cash sale:  DEBIT  <per-warehouse cashbox GL account>  TotalAmount
              CREDIT PosSales (AccountCodes.PosSales)     TotalAmount
- Card sale:  DEBIT  VisaClearing                          TotalAmount
              CREDIT PosSales                              TotalAmount
- Mixed:      DEBIT  cashbox GL (CashAmount) + VisaClearing (CardAmount)
              CREDIT PosSales                              TotalAmount
- (Credit is unreachable here — rejection at line 2580.)

JOURNAL 2 — COGS (only if `CalculateTotalCost(invoice) > 0`, line 182):
- DEBIT  CostOfGoodsSold
- CREDIT Inventory
  using `CostAmountSnapshot` summed across items.

### 7.3 Control / GL accounts
- Cashbox account is RESOLVED per warehouse: `ResolveWarehouseCashboxAccountCodeAsync`
  (CommerceAccountingService.cs:1486) → `CashboxLedgerAccountService.ResolveWarehouseCashboxAccountAsync`
  (CashboxLedgerAccountService.cs:299) → resolves the `TreasuryAccount` (Type=Cashbox) linked to the
  warehouse's cashbox, else the default cashbox account, else generic, else `NoCashboxAccountException`.
- Other accounts: `VisaClearing`, `PosSales`, `CostOfGoodsSold`, `Inventory` resolved via
  `_accountResolver.GetByCodesAsync` (CommerceAccountingService.cs:78).

### 7.4 Customer balance
- For a sale to a customer: `LedgerService.CreateLedgerEntry(Sale, debit=TotalAmount)` increments
  `customer.Balance` (the customer owes the full amount) — line 330.
- If paid now: `CreateLedgerEntry(PaymentReceived, credit=min(Paid,Total))` reduces
  `customer.Balance` — line 369. Net effect: balance increases by `Total - paid` (outstanding AR).
- `ResolveCustomerLedgerPaymentAmount` (line 2521) = `min(PaidAmount, TotalAmount)` — so overpayment
  (cash change) does NOT reduce balance below zero.

### 7.5 Treasury / cashbox effect
- The cashbox's visible balance is the GL `TreasuryAccount.CurrentBalance` for the resolved cashbox
  account, updated by JOURNAL 1's debit. There is NO `CashboxTransaction` row written by the sale.
  (`CashboxController` explicitly defers balance to the ledger, line 23/170.)

### 7.6 Double-entry validation
- `FinancialPostingService` Rule 4: `totalDebits == totalCredits` else `UnbalancedJournalException`
  (line 437-441). Rule 2/3: each line one-sided and positive (line 404-431).

### 7.7 Recovery
- If `PostPosSaleAsync` returns `!Success`, `CreateSale` throws → caught → full ROLLBACK (line 412-416).
- If the synchronous post SUCCEEDS but commit fails, the `AccountingOutboxMessage` remains
  unprocessed → the background `AccountingOutboxProcessor` replays `PostPosSaleAsync` idempotently
  (outbox idempotency key `SALE:{tenant}:{invoice}`, AccountingOutboxService.cs:45) — fail-closed:
  unresolved posting blocks downstream reconciliation, not silent loss.

## 8. Inventory Flow

- **Variant selected:** `ProductVariantId` supplied per line; resolved authoritatively
  (`PrepareSaleItemsAsync` → `ResolveVariantAsync`, line 1140).
- **Warehouse selected:** single `request.WarehouseId` for the whole invoice (line 180); all stock
  locks target that warehouse.
- **Inventory lock:** PESSIMISTIC row lock via `LockMultipleStocksAsync` (line 202), ordered
  (WarehouseId, VariantId) for deadlock prevention (ProductVariantStockMutationSupport.cs:300-304).
  `CreateIfMissing:false` (line 206) → if no stock row exists, `variantStock==null` → 400
  "Insufficient stock" (line 219).
- **Stock mutation:** `variantStock.Quantity -= baseQuantity` (line 270) inside the lock.
- **Stock movement:** `StockMovement` row `MovementType=Sale`, `QuantityChange=-baseQuantity`
  (line 272).
- **Valuation:** COGS uses `CostAmountSnapshot` captured at sale time
  (`ResolveSaleCostAmountSnapshot`, line 240) — snapshot, not live revaluation.
- **FIFO/LIFO/Average:** NOT implemented in the POS sale. Cost is a simple per-variant snapshot
  (`variant.CostPrice` via `ResolveSaleCostPriceSnapshot`, line 239, 2505). No lot tracking on this path.
- **Reservation:** NONE on the POS sale path. POS sells directly from on-hand `Quantity`; it does
  not use `InventoryReservations`. (Reservations belong to Storefront/CommerceHub checkout via
  `InventoryConsistencyService`.)
- **Negative stock prevention:** app guard (line 228) + DB CHECK `CK_ProductVariantStocks_Quantity_NonNegative`.
- **Recovery:** tx rollback restores quantity; `StockSyncOutboxMessage` is written only AFTER the
  commit path's stock mutation, so a rolled-back sale emits no stock-sync message.


## 9. SQL Transaction Analysis

- **Begin:** `SalesController.cs:131` — `await using var transaction = await _context.Database.BeginTransactionAsync();`
  No explicit isolation level → **PostgreSQL default ReadCommitted** (no global override found in
  Program.cs/DbContext; Npgsql leaves the server default). Inventory consistency is delivered by the
  PESSIMISTIC row lock (`LockMultipleStocksAsync`), NOT by Serializable isolation.
- **Commit:** `CompleteSalesIdempotencyAsync` → `await transaction.CommitAsync();` (line 937),
  after the response snapshot is persisted to the idempotency record (line 936).
- **Everything INSIDE the transaction:**
  - Idempotency claim insert/verify (line 139, 598).
  - Warehouse/customer/product/variant/unit reads (lines 150-161, 1125-1247).
  - Stock locks + decrement (lines 202-270).
  - StockMovement inserts (line 272).
  - SalesInvoice + SalesInvoiceItem insert (lines 324).
  - Ledger entries + CustomerTransaction inserts (lines 330-396).
  - AccountingOutboxMessage insert (line 406).
  - **Both accounting journals** (PostPosSaleAsync, line 411) — i.e. the financial post is INSIDE the tx.
  - Outbox mark-processed (line 418).
  - StockSyncOutboxMessage insert (line 431).
  - Idempotency response snapshot + SaveChanges (line 936).
- **Everything OUTSIDE the transaction (deferred):**
  - Audit log (line 450, wrapped in try/catch — failures don't affect the result).
  - `StockSyncOutboxProcessor` replicating stock to downstream read models (background).
  - Any `AccountingOutboxProcessor` recovery replay (background; normally a no-op since row is processed).
- **Atomic guarantees:** All authoritative state (invoice, stock, ledger, journals, outbox) commits
  or rolls back together. Stock-sync + audit are the only non-authoritative, best-effort artifacts.
- **Lock ordering:** `LockMultipleStocksAsync` sorts targets by (WarehouseId, VariantId) ascending
  (ProductVariantStockMutationSupport.cs:300-304) → deterministic acquisition prevents deadlocks
  across concurrent multi-item sales.
- **Deadlock prevention:** deterministic lock order + per-row lock held to commit. (Contrast:
  CommerceHub/SuperAdmin paths that need write-skew protection use Serializable explicitly, e.g.
  `SuperAdminRefundsService.cs:165`; POS sale relies on ReadCommitted + row locks.)
- **Rollback behavior:** every catch (ValidationException 465, InsufficientStockException 480,
  Exception 500) calls `transaction.RollbackAsync()` then `_context.ChangeTracker.Clear()` to drop
  tracked entities so the controller can return a clean error. Idempotency short-circuits also
  rollback before returning (lines 142, 153, 164, 197, 221).

## 10. Event Flow

**Critical finding: the POS cash sale publishes NO MassTransit/domain events to Mongo/ES/Redis.**
`_eventPublisher` is injected into `SalesController` (line 58) but never invoked anywhere in the
file (verified: only 2 references, both declaration/injection). The only "events" are SQL-backed
outbox rows.

| Artifact | Produced at | Consumed by | Ordering | Idempotency | Notes |
|---|---|---|---|---|---|
| `AccountingOutboxMessage` (EventType=PosSale) | line 406 | `AccountingOutboxProcessor` (background) | by (TenantId, AggregateId, EventVersion) | key `SALE:{tenant}:{invoice}` (AccountingOutboxService.cs:45) | Marked processed IN the sale tx (line 418); worker is recovery only. |
| `StockSyncOutboxMessage` | line 431 | `StockSyncOutboxProcessor` (background) | StockUpdatedAtUtc→OccurredAtUtc→CreatedAt→Id (StockSyncOutboxProcessor.cs:50-54) | `InventoryEventInbox` UX_TenantId_EventId dedup | Replicates variant stock to downstream read models. |

- **Versioning:** outbox carries `AggregateVersion = invoice.AccountingMutationVersion` (line 409);
  stale versions cannot overtake newer state (generic outbox ordering).
- **Replay:** idempotent by idempotency key; `MarkSynchronousSaleAccountingOutboxProcessedAsync`
  (line 1823) guards against double-processing by asserting `JournalId` equality.
- **Critical events / operator reconciliation:** a failed/unprocessed critical accounting outbox row
  triggers fail-closed operator reconciliation (generic outbox semantics) — outward money is never
  silently lost.
- **Mongo / Redis / ES updates:** NONE triggered by this code path. POS analytics reads SQL directly
  (`LedgerEntries`); there is no `SalesInvoice` Mongo projection or search index written by the sale.

## 11. Projection Flow (exact)

| Projection | Updated by POS cash sale? | Evidence |
|---|---|---|
| Mongo — ProductDocument (catalog) | NO | sale touches only ProductVariantStock; product doc is catalog-owned. |
| Mongo — SalesInvoice doc | NO | no `SalesInvoice` Mongo projection exists in this path; reads are SQL (`GetSale` line 2040). |
| Redis — cache invalidation | NO | no Redis write/invalidation on sale path. |
| Elasticsearch — search index | NO | search is catalog/storefront-owned; POS sale does not touch it. |
| Analytics (SQL LedgerEntries) | Indirectly, immediately | analytics reads `LedgerEntries`/`SalesInvoices` SQL directly; nothing to project. |
| Reports (SQL) | Indirectly, immediately | reports query SQL aggregates. |
| CommerceHub projection | NO | POS sale does not write CommerceHub orders. |
| Affiliate projection | NO | POS sale is not attributed (affiliate attribution is a separate bounded context). |
| Storefront projection | NO | storefront reads its own order/catalog projections, not POS invoices. |

**Why no projections are updated:** The POS sale is a SQL-authoritative operational write. The
architecture's projection pipeline (MassTransit consumers → Mongo/ES) is driven by DOMAIN EVENTS,
and the POS sale emits none. The only downstream propagation is the inventory `StockSyncOutboxMessage`
(which feeds inventory read models for other runtimes), and even that is async.


## 12. UI Flow (implementation-supported, frontend-verified)

The POS client is a React SPA (`PointOfSale.Web` — the "POS Admin" composition shell) calling
`POST /api/sales`. There is **no separate cashier frontend**; the cashier flow lives entirely in
`PointOfSale.Web/src` (the storefront SPA `PointOfSale.Storefront` is customer-facing only). The POS
sale screen is `pages/POS.jsx` (route `/pos`, `App.jsx:240`; sidebar `nav.sales`, `Sidebar.jsx:98`).
The authoritative backend contract is the `CreateSaleRequest` DTO (`SalesController.cs:2875`) and the
response `SaleResponse` (lines 440-448). Frontend evidence below (verified by subagent read of the
SPA).

**Components:** `pages/POS.jsx` (1299 lines orchestrator), `BarcodeScanner.jsx`, `ProductSearch.jsx`,
`QuickProductGrid.jsx`, `CartItem.jsx`, `PaymentModal.jsx`, `ReceiptModal.jsx`,
`CustomerSelectModal.jsx`, `HoldSalesModal.jsx`, `InvoiceSearchModal.jsx`, `VariantSelectModal.jsx`,
cart hook `hooks/usePOSCart.js`.

**User actions / keyboard shortcuts** (`POS.jsx:739-808`): F1 focus barcode, F2 focus search,
F3 customer, **F4 hold sale**, F5 retrieve held, F6 search sales, **F9 process payment**
(`openPaymentModal`), Ctrl+Z undo, Ctrl+Y redo, Esc clear cart, Delete remove last item.

**Barcode scanning:** `BarcodeScanner.jsx:9-36` matches exact product/variant barcode or SKU,
calls `searchPosProducts` (`:90`); Enter → `searchProduct` → `onProductFound` → `addPreparedProductToCart`
→ `usePOSCart.addItem` (`usePOSCart.js:147`). Variants: >1 variant forces `VariantSelectModal`
(`POS.jsx:652-668`).

**Dialogs / modals:**
- Customer selection: `CustomerSelectModal.jsx` (F3 / header) → `handleCustomerSelect` (`POS.jsx:685`).
- Payment method: `PaymentModal.jsx:567-586` — three buttons **Cash / Card / Mixed**. Cash-received
  input `:597-611`; **live change-due** `change = max(0, cash - total)` computed `:199-201`, rendered
  `:612-616`. Mixed shows read-only card portion `:620-633`. Optional sales-rep `AsyncEntitySelect`
  `:542-560`.
- Confirm dialog: `ConfirmModal` nested in `PaymentModal.jsx:665-707` (`buildPaymentBreakdown`
  `:92-128`), triggered by submit → `setPendingConfirmation` `:499`.
- Invoice-level discount: %/fixed selector `POS.jsx:1129-1161`, clamped by `clampDiscountValue` (`:168`).

**Loading / validation:**
- Pre-checkout stock revalidation `revalidateCheckoutStock` (`POS.jsx:296-349`) before opening payment
  modal; button shows `t('pos.checkingStock')` `:1201`.
- Client validation `getSaleValidationError` (`POS.jsx:192-254`): warehouse required, empty cart,
  total ≤ 0, invalid discount, per-item stock (`t('pos.insufficientStock')` `:249`); payment button
  disabled when invalid (`:1197`).
- `PaymentModal.getValidationMessage` `:220-293` (warehouse/items/total/discount/cash-too-low/invalid
  mixed); error banner `role="alert"` `:635-643`; submit disabled while busy `:656`; `submissionLockRef`
  prevents double-submit `:366`.
- API error mapping `getSaleSubmissionErrorMessage` `PaymentModal.jsx:31-40`; 409 `SALE_IN_PROGRESS`
  conflict caught `:42-60`. Backend messages ("Warehouse not found", "Invalid payment", etc.) surfaced
  via `getSafeApiErrorMessage` `:39` → toast.

**Success path:** `handlePaymentComplete` (`POS.jsx:810-839`) guarded by
`attemptId === currentPaymentAttemptRef.current` (`:811`) to dedupe replays; success toast
`t('pos.saleCompleted', {invoice})` `:824`; `resetPOS()` clears cart `:821`; `invalidatePosRuntimeCache`
`:823`. Receipt fetched `GET /Sales/receipt/{invoiceId}` (`PaymentModal.jsx:429`); `ReceiptModal`
auto-opens (`POS.jsx:833-838`), renders items/totals/payment/change and **auto-prints** after 500ms
(`window.print()`, `ReceiptModal.jsx:26,41-46`), supports 80mm/A4. Inventory count on cart items
refreshed via `revalidatePosCartStock`→`syncStockLevels` (`usePOSCart.js:439-472`).

**Cash balance display:** NOT in the POS sale screen (grep zero). Cash drawer / opening balance /
cash-balance figures live in separate `pages/Cashbox.jsx` and cashbox modals; `DailyCloseReport`
aggregates `cashSales` (`DailyCloseReport.jsx:104`). This corroborates the backend finding that the
cashbox is a GL-derivative, not a per-sale UI state.

**Hold / Recall invoice:** `usePOSCart.holdSale` (`usePOSCart.js:475-501`) persists held sale to
localStorage; `handleHoldSale` (`POS.jsx:694-712`, F4) → toast `t('pos.saleHeld')`. Recall:
`HoldSalesModal.jsx` lists held sales (`getHeldSales`), Retrieve loads into cart (`POS.jsx:714-722`,
F5). Edit existing: `InvoiceSearchModal` (F6) → `handleSelectInvoice` (`POS.jsx:841-959`) loads sale
into editable cart (`editingSaleId`/`editingMutationVersion`); payment becomes `PUT` update
(`PaymentModal.jsx:384-387`). Note: held/recalled sales are CLIENT-SIDE cart state — the backend only
sees `X-Pos-Cart-Id` (idempotency); there is no "held invoice" SQL aggregate.

**Idempotency headers (client generation):** `utils/requestSafety.js:1-6`
`buildIdempotencyHeaders(idempotencyKey, paymentAttemptId, posCartId)` emits `Idempotency-Key`,
`X-Request-Id`, and conditionally `X-Payment-Attempt-Id` + `X-Pos-Cart-Id`.
- `paymentAttemptId` = `buildPaymentAttemptId()` = `pos-payment-attempt-<ts>-<rand>` (`POS.jsx:36`),
  fresh per checkout at `openPaymentModal` (`:371`) into `currentPaymentAttemptRef`.
- `posCartId` = `buildPosCartId()` = `pos-cart-<ts>-<rand>` (`POS.jsx:37`), assigned `:370` / on cart
  hydrate `:275` / recall `:715,883`.
- Sent in `PaymentModal.submitPayment` on both `POST /Sales` and `PUT /Sales/{id}`
  (`PaymentModal.jsx:385-390`). `X-Pos-Cart-Id` is OMITTED when editing an existing sale
  (`POS.jsx:1225-1226`). Replay guard: `handlePaymentComplete` ignores callbacks whose `attemptId` ≠
  current ref (`POS.jsx:811`); `completedPaymentAttemptRef` ensures single reset/receipt (`:815-825`).

The backend ENCODES the following contract (authoritative):

- **User actions / required inputs** (`CreateSaleRequest`, line 2875):
  - `Items[]` (ProductId, ProductVariantId, UnitId?, Quantity, UnitPrice, DiscountAmount, TaxRate)
  - `WarehouseId` (required), `CustomerId?`, `SalesRepresentativeId?`
  - `PaymentMethod` (Cash/Card/Mixed), `PaidAmount`, `CashAmount?`, `CardAmount?`
  - `DiscountPercentage?`, `DiscountAmount?`, `Notes?`
  - Headers: `X-Payment-Attempt-Id` OR `X-Pos-Cart-Id` (idempotency; lines 100-128).
- **Validation messages surfaced** (backend returns, client renders): "Warehouse not found" (154),
  "Customer not found" (165), "Insufficient stock ... Available: 0 ... Requested: N" (222), "Product
  not found" (1154), "Unit is not configured" (1170), "Default unit is not configured" (1193),
  "No variant found ... select a variant" (1215), "Discount exceeds line subtotal" (1229),
  "Invalid sale discounts" (2547), "Invalid payment" (2607/2638/2668), "Idempotency key reuse with
  different payload" (567), "Either X-Payment-Attempt-Id or X-Pos-Cart-Id header is required" (127).
- **Success result** (`SaleResponse`, lines 440-448): `InvoiceId`, `InvoiceNumber`, `TotalAmount`,
  `PaidAmount`, `ChangeAmount` (= PaidAmount - TotalAmount), message "Sale completed successfully".
  The client uses `ChangeAmount` to drive the cash-drawer/change display and receipt.
- **Receipt printing:** driven by `InvoiceNumber` + totals returned; the backend emits no receipt,
  but the response carries everything the client needs to render/print.
- **Hold / Recall invoice:** NOT part of `CreateSale`. The request hash (line 130) and stored
  idempotency response (line 928) support safe retry, but there is no "hold cart" aggregate in this
  path — hold/recall is a client-side cart concept keyed by `X-Pos-Cart-Id` (which the backend only
  uses for idempotency, not storage).
- **Barcode scanning:** the client resolves scanned barcodes to `ProductId`/`ProductVariantId` before
  calling `CreateSale`; the backend validates the resolved IDs (no barcode string is accepted directly
  by `CreateSale`).
- **Cash drawer:** the `ChangeAmount` in the response is what the client presents to the operator to
  open the drawer / return change. No server-side cash-drawer state is maintained by the sale.
- **Loading / progress:** the client shows a pending state until the `200`/`4xx`/`5xx` returns; the
  backend performs the whole flow synchronously (no streaming).
- **Keyboard shortcuts:** not enforced server-side; any shortcut is a client concern.

## 13. Failure Analysis (every failure path, with recovery)

| Failure | Trigger (code) | Behavior | Recovery |
|---|---|---|---|
| Invalid idempotency header | line 108-118 | 400 "Invalid idempotency key" | client resends valid key |
| Missing idempotency header | line 125-127 | 400 "Either X-... required" | client adds header |
| Key reuse, different payload | line 557-568 | 400 "Idempotency key reuse" | client must use new key |
| Idempotency HIT (retry) | line 572-580 | returns stored 200 response (rollback first) | transparent to client |
| Unsupported payment method | line 2580-2588 | ValidationException→400 | client picks Cash/Card/Mixed |
| Warehouse missing | line 151-154 | rollback + 400 | fix warehouse |
| Customer missing | line 162-166 | rollback + 400 | fix customer |
| Product missing | PrepareSaleItemsAsync 1151 | rollback + 400 | fix product |
| Variant missing | 1212 | rollback + 400 | select variant |
| Unit misconfigured | 1175/1198 | rollback + 400 | configure unit |
| Discount > subtotal | 1226/2547 | rollback + 400 | fix discount |
| Stock row absent | 219 | rollback + 400 "Insufficient stock" | restock |
| Insufficient stock | 228 (guard) or DB CHECK | rollback + 409 Conflict | restock / reduce qty |
| Cash paid < total | 2646 | rollback + 400 | collect more |
| Mixed split mismatch | 2626 | rollback + 400 | fix split |
| Accounting post failed | 412-416 | throw → catch → rollback + 500 | outbox recovery replays |
| Claim token stolen | 916-925 | rollback + 500 (concurrency) | retry with new attempt id |
| Generic exception | 500-509 | rollback + 500 | inspect logs; idempotent retry |
| Network interruption mid-tx | connection drop | PostgreSQL aborts tx → rollback | client retries with same idempotency key → HIT |
| Projection (stock-sync) failure | background processor | event stays unprocessed; retry/DLQ | `StockSyncOutboxProcessor` retries with backoff |
| Outbox (accounting) failure | background processor | row unprocessed → fail-closed reconciliation | `AccountingOutboxProcessor` replays idempotently |

**Recovery principle:** the synchronous post is the source of truth; outboxes are recovery only.
Idempotency makes the client's "submit again" safe — a post-commit network drop yields the SAME
200 on retry (stored response, line 928-937).

## 14. Performance Analysis

- **DB round trips inside tx:** (1) idempotency claim; (2) warehouse load; (3) customer load (opt);
  (4) invoice-number generation; (5) batch product load; (6) batch unit load; (7) per-line variant
  resolve (sequential, line 1137-1146 — explicitly to avoid EF concurrency); (8) stock locks (one
  per distinct (WarehouseId,VariantId)); (9) ledger entries (2); (10) outbox insert; (11) accounting
  journals (2 PostJournalAsync, each reads accounts + posts); (12) stock-sync outbox; (13) idempotency
  snapshot SaveChanges + commit. Roughly 15-25 round trips for a typical multi-line sale.
- **Lock duration:** stock row locks held from `LockMultipleStocksAsync` (line 202) until
  `CommitAsync` (line 937) — the dominant lock window. Kept short by doing all reads/computation
  before the lock and committing promptly after mutation.
- **Transaction duration:** synchronous, includes accounting journal posting — the slowest in-tx step
  is the two `PostJournalAsync` calls (GL balance updates + account validation).
- **Expensive operations:** per-line sequential variant resolution (deliberate, line 1135);
  `GenerateInvoiceNumberAsync` may take a sequence lock.
- **Deferred work:** stock-sync replication (background), audit log (best-effort), outbox recovery
  (background, normally no-op).
- **Background jobs:** `StockSyncOutboxProcessor`, `AccountingOutboxProcessor` (Hangfire/5s generic
  outbox). These scale independently of the sale request.
- **Projection latency:** NONE on the write path; read models update only via the async stock-sync
  (eventual, seconds).
- **Scaling bottlenecks:** single-row stock locks per variant → high-contention SKUs serialize; the
  synchronous accounting post adds latency to every sale. Horizontal scale needs per-warehouse
  partitioning of cashbox GL accounts (already per-warehouse, CommerceAccountingService.cs:1486).

## 15. Security Analysis

- **Permission checks:** endpoint is under the POS-operator JWT (`Jwt`/`PointOfSaleAPI` scheme); roles
  Owner/Admin/Manager/Cashier. (AuthController.cs:104; Program.cs POS auth.) No separate
  cash-sale-specific policy found beyond the operator JWT + role.
- **Tenant validation:** `_tenantContext.RequiredTenantId` stamped on every aggregate
  (e.g. line 176); `X-Tenant-Id` header is STRIPPED and never trusted — service resolves tenant.
  DB-enforced via `ITenantOwned` + `TenantSaveChangesInterceptor`.
- **Warehouse ownership:** `LoadActiveWarehouseAsync` filters by TenantId + active (line 1264) — a
  cross-tenant warehouse id is rejected (returns null → 400).
- **Customer ownership:** `LoadActiveCustomerAsync` filters by TenantId (line 1260) — cross-tenant
  customer rejected.
- **Price tampering prevention:** server OVERRIDES client `UnitPrice` and `TaxRate` with authoritative
  values from `ProductVariant`/`Product` (`ResolveAuthoritativeSaleUnitPrice` line 2566; applied
  line 1220-1238). Client-supplied prices are accepted into the request object only to be replaced.
- **Payment integrity:** cash must cover total (line 2646); mixed split must equal total (line 2626);
  change is derived, not supplied (`ChangeAmount = Paid - Total`, line 446).
- **API validation:** Fluent/DataAnnotations on `CreateSaleRequest`; extensive business validation in
  `PrepareSaleItemsAsync` + `ApplySaleTotals` + `ValidateAndNormalizePaymentAmounts`.
- **Replay protection:** idempotency key + deterministic request hash; replay returns the stored
  byte-exact response (line 928). A key bound to a DIFFERENT payload is rejected (line 557) —
  prevents a reused attempt-id from resolving to another sale.
- **Idempotency:** mandatory header; claim-token verified at completion (line 916) prevents a stolen
  claim from committing.
- **Fraud prevention (cash):** the cashbox balance is GL-derived and reconciled via journals; physical
  cash handling (drawer, change) is a client concern and is NOT a server-side control in this path.


## 16. Sequence Diagram (implementation-backed)

```
POS Client (React SPA)
   │  POST /api/sales  [X-Payment-Attempt-Id | X-Pos-Cart-Id]
   ▼
SalesController.CreateSale                         ── begins tx (131)
   │
   ├─► SalesRequestIdempotency  (claim + hash)     (139, 513)
   ├─► LoadActiveWarehouseAsync                       (150)
   ├─► LoadActiveCustomerAsync  (opt)                 (161)
   ├─► GenerateInvoiceNumberAsync                     (170)
   ├─► PrepareSaleItemsAsync                          (1116)
   │      ├─ Products (batch)                         (1125)
   │      ├─ ProductUnits (batch)                     (1130)
   │      └─ ProductVariant (per line, resolved)       (1140)
   ├─► ProductVariantStockMutationSupport
   │      .LockMultipleStocksAsync  (PESSIMISTIC)      (202, 292)
   │
   │   FOR EACH ITEM:
   │      ├─ ValidateAvailableQuantity(allowNegative:false)  (228)
   │      ├─ variantStock.Quantity -= baseQty         (270)
   │      └─ StockMovement (Sale, -qty)               (272)
   │
   ├─► ApplySaleTotals + ValidateAndNormalizePaymentAmounts  (307, 314)
   ├─► SalesInvoices.Add(invoice)                     (324)
   │
   ├─► [IF customer] LedgerService.CreateLedgerEntry  (330)  ── updates Customer.Balance
   │      └─ CustomerTransaction (Sale)               (343)
   │      └─ [IF paid] CreateLedgerEntry(PaymentReceived) (369)
   │            └─ CustomerTransaction (PaymentReceived) (382)
   │
   ├─► AccountingOutboxService.EnqueuePosSaleAsync    (406)  ── AccountingOutboxMessage row
   ├─► CommerceAccountingService.PostPosSaleAsync      (411)
   │      ├─ CashboxLedgerAccountService.ResolveWarehouseCashboxAccountAsync (1486→299)
   │      ├─ FinancialPostingService.PostJournalAsync (Revenue)   (177)
   │      └─ FinancialPostingService.PostJournalAsync (COGS, opt)  (201)
   ├─► MarkSynchronousSaleAccountingOutboxProcessedAsync       (418) ── outbox row Processed+JournalId
   ├─► ProductVariantStockMutationSupport.PublishCommittedStockEventsAsync (431) ── StockSyncOutboxMessage
   ├─► CompleteSalesIdempotencyAsync (snapshot + Commit)        (937)
   │
   ▼  200 OK SaleResponse (InvoiceNumber, Totals, ChangeAmount)
POS Client
   │
   └─ (async, background) StockSyncOutboxProcessor ──► downstream inventory read models
   └─ (async, background) AccountingOutboxProcessor ──► recovery only (row already processed)

NOTE: No Mongo/ES/Redis consumer is invoked. No IEventPublisher call occurs. The only
cross-runtime propagation is the inventory StockSyncOutboxMessage.
```

## 17. Runtime Collaboration Summary (POS ↔ peers)

- **POS ↔ Inventory:** In-process, pessimistic locked mutation of `ProductVariantStock`
  (`LockMultipleStocksAsync`). Inventory does NOT own the sale; it owns the stock aggregate. Stock
  replication to other runtimes happens only via `StockSyncOutboxMessage` (async).
- **POS ↔ Treasury:** POS does NOT write a `CashboxTransaction`. It resolves the cashbox's GL
  account via `CashboxLedgerAccountService` and debits it through the accounting journal. Treasury's
  cashbox balance is a GL-derivative (`CashboxController.cs:23`: ledger authoritative).
- **POS ↔ Accounting:** The core collaboration. `CommerceAccountingService.PostPosSaleAsync` posts
  balanced journals inside the POS transaction; `AccountingOutboxService` stages+marks the outbox row.
  Accounting owns `JournalEntry`/`LedgerAccount`; POS calls it synchronously.
- **POS ↔ CRM-Party:** Via `LedgerService.CreateLedgerEntry`, which mutates `Customer.Balance` and
  appends `CustomerTransaction`. CRM owns the party; POS drives it through the ledger service.
- **POS ↔ Catalog:** Read-only resolution of product/variant/unit + authoritative price/tax. Catalog
  is never written by a sale.
- **POS ↔ Analytics:** NONE at write time. Analytics reads `LedgerEntries`/`SalesInvoices` SQL
  directly; no event/projection.
- **POS ↔ CommerceHub:** NONE. POS sale writes no CommerceHub order. (CommerceHub is a separate
  marketplace bounded context with its own order/fulfillment/financial lifecycle.)
- **POS ↔ Affiliate:** NONE. POS sales are not attributed; affiliate attribution is a separate
  bounded context triggered by storefront/marketplace conversions, not POS counters.


## 18. Hermes Browser Test Specification

### 18.1 Preconditions
- Tenant exists and `RequiredTenantId` resolves (operator JWT with role Cashier/Manager/Owner).
- A warehouse `W1` (active, owned by tenant) exists.
- A cashbox linked to `W1`'s `TreasuryAccount` (Type=Cashbox) exists, OR a default cashbox account
  exists (else `NoCashboxAccountException` at CommerceAccountingService.cs:1491→375).
- Product `P1` with a default unit (ConversionFactor>0) and variant `V1` (CostPrice set) exists.
- `ProductVariantStock(W1, V1).Quantity = 10` (DB CHECK >=0).
- Customer `C1` (active, Balance=0) exists (optional; test both with and without).
- GL accounts exist: PosSales, VisaClearing (if card/mixed), CostOfGoodsSold, Inventory.
- Idempotency header generator available: send `X-Payment-Attempt-Id = <uuid>` (or `X-Pos-Cart-Id`).

### 18.2 User Journey (deterministic)
1. Operator opens POS sale screen; scans/selects `P1`/`V1`, qty = 2.
2. Selects warehouse `W1`.
3. Selects customer `C1` (optional).
4. Chooses PaymentMethod = Cash; enters PaidAmount = TotalAmount (cash covers total).
5. Submits `POST /api/sales` with header `X-Payment-Attempt-Id: <uuid>`.
6. Expects 200 + SaleResponse { InvoiceNumber, TotalAmount, PaidAmount, ChangeAmount }.

### 18.3 Expected Runtime Interaction
- SalesController.CreateSale (tx begin 131) → PrepareSaleItemsAsync → LockMultipleStocksAsync →
  LedgerService (if C1) → AccountingOutboxService.Enqueue → CommerceAccountingService.PostPosSaleAsync
  (journals) → MarkProcessed → PublishCommittedStockEventsAsync → Commit (937).

### 18.4 Expected Database State (post-commit, SQL)
- `SalesInvoices`: 1 row, Status=Completed, InvoiceNumber set, TotalAmount=T, PaymentMethod=Cash.
- `SalesInvoiceItems`: 1 row (qty 2, cost snapshot, tax).
- `ProductVariantStocks(W1,V1).Quantity = 8` (10 - 2).
- `StockMovements`: 1 row, MovementType=Sale, QuantityChange=-2, ReferenceId=InvoiceId.
- IF C1: `Customers.Balance` increased by (T - paid); `CustomerTransactions`: 1 (Sale) + 1 (PaymentReceived if paid>0).
- `JournalEntries`: Revenue journal (debit cashbox GL T, credit PosSales T) + COGS journal if cost>0.
- `LedgerAccounts`: cashbox GL CurrentBalance += T; Inventory -= cost; COGS += cost; PosSales += T.
- `AccountingOutboxMessages`: 1 row EventType=PosSale, BusinessEntityId=InvoiceId, ProcessedAtUtc set, JournalId set.
- `StockSyncOutboxMessages`: 1 row for the stock event, Eventually PublishedAtUtc set by background processor.

### 18.5 Expected UI Result
- Toast/notification "Sale completed successfully".
- Receipt with InvoiceNumber, line items, TotalAmount, PaidAmount, ChangeAmount.
- Inventory count for V1 shows 8 (after stock-sync propagates, or via SQL read).
- Cashbox balance display increases by T (derived from GL).
- Reports (sales summary) reflect the new invoice on next SQL query.

### 18.6 Negative Test Cases (deterministic)
| Case | Setup | Expected |
|---|---|---|
| Out of stock | V1.Quantity=1, qty=2 | 409 Conflict "Insufficient stock" + rollback; quantity unchanged |
| Closed/invalid warehouse | WarehouseId=unknown | 400 "Warehouse not found"; no rows written |
| Invalid customer | CustomerId=unknown | 400 "Customer not found"; no rows written |
| Negative/discount>subtotal | DiscountAmount>line subtotal | 400 "Discount exceeds line subtotal"; rollback |
| Cash < total | PaidAmount < Total | 400 "Invalid cash payment"; rollback |
| Mixed mismatch | Cash+Card != Total | 400 "Invalid payment split"; rollback |
| Duplicate submit (same key) | Resend same X-Payment-Attempt-Id | 200 with SAME stored response (idempotency HIT) |
| Key reuse diff payload | Same key, different cart | 400 "Idempotency key reuse with different payload" |
| Credit method | PaymentMethod=Credit | 400 "Unsupported payment method" (EnsureSupportedPaymentMethod line 2580) |
| Connection loss mid-tx | Kill conn after commit start | PostgreSQL rollback; retry with same key → HIT/200 |
| Permission denied | JWT without POS role | 401/403 at auth filter |

### 18.7 Assertions (deterministic, for Hermes)
1. `SELECT count(*) FROM "SalesInvoices" WHERE "InvoiceNumber"='<N>'` == 1.
2. `ProductVariantStocks.Quantity` for (W1,V1) == 8 (was 10).
3. `StockMovements` row exists with MovementType=2 (Sale) and QuantityChange=-2.
4. IF C1: `Customers."Balance"` == expected (T - paid).
5. `JournalEntries` sum(debit)==sum(credit) for SourceId=InvoiceId (double-entry holds).
6. `AccountingOutboxMessages` row has ProcessedAtUtc NOT NULL and JournalId NOT NULL.
7. `LedgerAccounts` cashbox GL CurrentBalance delta == T.
8. On duplicate submit: response JSON byte-equal to first; no second SalesInvoice row.
9. On out-of-stock: no SalesInvoice row AND quantity unchanged (==1).
10. No `CashboxTransactions` row is created by the sale (verify count unchanged).

## 19. Documentation Validation (prompt assumptions vs implementation)

| Prompt assumption | Implementation verdict | Evidence |
|---|---|---|
| "Cash sale" differs from "Credit Sale" on POS | WRONG: there is NO POS credit sale. `CreateSale` REJECTS `PaymentMethod.Credit` (only Cash/Card/Mixed). `Credit` is used only for *supplier purchases* (`SuppliersController.cs:200`, `ReportsController.cs:1784`). | `EnsureSupportedPaymentMethod` `SalesController.cs:2580` |
| "Cashbox open" is a precondition | NOT ENFORCED: no open-cashbox check on the sale; cashbox balance is GL-derived and the cashier UI shows no cash-balance (it lives in `Cashbox.jsx`). | `CashboxController.cs:23` "Ledger is authoritative"; zero open-check in `CreateSale`; subagent frontend grep (POS.jsx) |
| Sale updates a "CashboxTransaction" | WRONG: no `CashboxTransaction` written; cash booked to GL cashbox account via journal. | zero `CashboxTransaction` refs in `CreateSale`; `CommerceAccountingService.cs:93-102` |
| Sale emits domain events / updates Mongo/ES | WRONG: `_eventPublisher` injected but never called; no Mongo/ES write. (It DOES emit committed-stock events via `StockSyncOutboxMessage` + audit log + the `AccountingOutboxMessage`.) | only 2 refs to `_eventPublisher` (decl/inject); consumers on POS sale = none for Mongo/ES |
| "Projection updates" happen on sale | WRONG for Mongo/ES/Redis; only async inventory `StockSyncOutboxMessage` propagates. | `PublishCommittedStockEventsAsync` line 431 |
| Discount/coupon/promo engines invoked | NOT on POS path: prices arrive inline & are overwritten; coupons are storefront/CommerceHub-scoped. | `SalesController` injects no pricing/promo service |
| Inventory uses reservation | WRONG for POS: sells directly from on-hand `Quantity` via lock+decrement; reservations belong to storefront/`CommerceHub`. | no `InventoryReservations` touched in `CreateSale` |
| Accounting is "posted later via outbox worker" | PARTLY WRONG: posted SYNCHRONOUSLY in the sale tx; outbox is recovery-only. | lines 406-425 + `MarkSynchronousSaleAccountingOutboxProcessedAsync` |
| Generic "aggregate mutations" include optimistic locking | MIXED: stock uses PESSIMISTIC row lock, not optimistic; plus DB CHECK. | `LockMultipleStocksAsync` line 202; CHECK constraint |
| CommerceHub uses "a different accounting outbox" | WRONG: CommerceHub reuses the SAME `AccountingOutboxMessages` table + `AccountingOutboxProcessor`; only the `eventType` differs (`CommerceHubLedgerPosting` vs POS `PosSale`). | `PointOfSale.API/Services/CommerceHub/FinancialLedger/CommerceHubFinancialLedgerService.cs:161` (`RecordOrderAsync`) + `:745-755` (outbox `CommerceHubLedgerPosting`); `AccountingOutboxProcessor.cs:740,921` |
| Storefront Checkout is "the same kind of sale" | WRONG: it writes the `Order`/`OrderItem` aggregate (not `SalesInvoice`), defers GL posting to payment capture, and reserves stock (not lock-decrement). | `CheckoutService.cs:5427,5703,5453,5842`; `ReserveAsync` `:5135` |

### 19.1 Three-way comparison (POS Cash Sale vs Storefront Checkout vs CommerceHub Order)

| Dimension | POS Cash/Card/Mixed Sale | Storefront Checkout | CommerceHub Order |
|---|---|---|---|
| Owner | `SalesController.CreateSale` (`:98`,`:147`,`:411`) | `CheckoutService` (`:5427`) | `CommerceHubCheckoutService` (`:769`) + `OrderManagement` + `FinancialWorkflow` |
| Aggregate written | `SalesInvoice`/`SalesInvoiceItem` (`:173`) | `Order`/`OrderItem` (`:5427`,`:5703`) — NOT `SalesInvoice` | `CommerceHubMarketplaceOrder`/`SubOrder`/`SubOrderItem` (`:1806`,`:1836`,`:1872`) |
| Financial posting | `_commerceAccounting.PostPosSaleAsync` **synchronous** + outbox staged (`:406`,`:411`) | Payment intent only; GL posted at capture (`:5453`,`:5842`) — NOT `PostPosSaleAsync` | `CommerceHubFinancialLedgerService.RecordOrderAsync` → `CommerceHubFinancialTransaction` + outbox `CommerceHubLedgerPosting` (`:724-755`) |
| Outbox/processor | `AccountingOutboxMessages`, `AccountingOutboxProcessor` (SourceType `SalesInvoice`) | separate payment-capture outbox | **SAME** `AccountingOutboxMessages`/`Processor` (`:740`,`:921`) — SourceType `CommerceHubLedgerPosting` |
| Stock mutation | `ProductVariantStockMutationSupport` lock/decrement (`:202-294`,`:431`) | `_variantStockRepository.ReserveAsync` reservation (`:5135`) | `IInventoryConsistencyService.ReserveOrderItemAsync` (`:737`) |
| Domain events | committed stock events + audit (`:431`,`:452`) | `OrderCreatedEvent` (`:8002`), attribution, coupons | `CommerceHubMarketplaceOrderCreatedEvent` (`:868`), vendor earnings |
| Credit/on-account | **rejected** (`:2580`) | n/a (online payment) | n/a (online payment) |

**Key corrections to the prompt's framing:**
1. **"POS Credit Sale" does not exist** — `PaymentMethod.Credit` is hard-rejected in
   `SalesController.EnsureSupportedPaymentMethod` (`:2580-2588`); `Credit` is only used for *supplier
   purchases*. No invoice-on-account path in POS.
2. **CommerceHub does NOT use a separate accounting outbox** — it reuses the same
   `AccountingOutboxMessages` table/processor as POS; only `eventType` + ledger entity differ.
3. **Storefront Checkout does not create `SalesInvoice`** and does not call `PostPosSaleAsync`; it
   writes the `Order` aggregate and posts finance only on later payment capture, and reserves stock
   rather than lock-decrementing it.

Corrected architecture: A cash sale is a SQL-authoritative, single-transaction, synchronous mutation
of the SalesInvoice, ProductVariantStock, Customer ledger balance, and GL journal, with the
accounting post executed INLINE (outbox as recovery), and inventory replication deferred to an
outbox processor. The cashbox is a GL account, not a transactional entity in this flow. It is
structurally distinct from Storefront `Order` (reserve + deferred capture) and CommerceHub
`MarketplaceOrder` (lifecycle + vendor finance) — three different aggregates, three different stock
mechanisms, one shared accounting outbox engine.


## 20. Final Architecture Question — Answered

**"How does Shopixy execute a complete cash sale while preserving inventory consistency, financial
correctness, accounting integrity, transactional guarantees, runtime boundaries, projection
consistency, recovery, scalability, and deterministic business behavior?"**

Shopixy executes a cash sale as a **single synchronous PostgreSQL transaction** owned by
`SalesController.CreateSale` (`SalesController.cs:98`, tx begin `:131`, commit `:937`). Within that
one transaction it performs every authoritative mutation, so atomicity is guaranteed by the database,
not by orchestration:

1. **Inventory consistency** — achieved by PESSIMISTIC row locking
   (`LockMultipleStocksAsync`, `:202` → `ProductVariantStockMutationSupport.cs:292`), acquired in
   deterministic (WarehouseId, VariantId) order to prevent deadlocks, followed by
   `ValidateAvailableQuantity(allowNegativeStock:false)` (`:228`) and a DB-level
   `CK_ProductVariantStocks_Quantity_NonNegative` CHECK. Stock can never go negative and concurrent
   sales serialize on the locked row.

2. **Financial correctness** — payment amounts are server-normalized
   (`ValidateAndNormalizePaymentAmounts`, `:314`/`:2590`): cash must cover total, mixed split must
   equal total, and client-supplied prices/tax are OVERWRITTEN with authoritative catalog values
   (`ResolveAuthoritativeSaleUnitPrice`, `:2566`; `product.TaxRate`, `:1222`). Change due is derived
   (`Paid - Total`, `:446`), never trusted from the client.

3. **Accounting integrity** — the full double-entry is posted INLINE via
   `CommerceAccountingService.PostPosSaleAsync` (`:411` → `CommerceAccountingService.cs:47`), which
   writes a balanced Revenue journal (debit cashbox/Visa/AR per payment method, credit PosSales) and,
   when cost data exists, a COGS journal (debit COGS, credit Inventory). `FinancialPostingService`
   enforces `debits == credits` (`:437`) and one-sided positive lines (`:404`). The cashbox is a GL
   account resolved per warehouse (`CashboxLedgerAccountService.ResolveWarehouseCashboxAccountAsync`,
   `CashboxLedgerAccountService.cs:299`); no `CashboxTransaction` is written.

4. **Transactional guarantees** — begin/commit bracket the invoice, stock, ledger, and BOTH journals.
   Any failure rolls back the entire transaction and clears the change tracker (`:467/:482/:502`).
   PostgreSQL ReadCommitted + row locks deliver the isolation needed without Serializable overhead.

5. **Runtime boundaries** — what looks like many runtimes is actually in-process services over one
   `ApplicationDbContext`. POS owns SalesInvoice/StockMovement; Inventory owns ProductVariantStock
   (via the mutation-support service); Treasury resolves the cashbox GL account; Accounting owns
   JournalEntry/LedgerAccount; CRM-Party owns Customer balance (via LedgerService). Each boundary is
   a method call, not an HTTP hop — there is no distributed write at sale time.

6. **Projection consistency** — the POS sale emits NO domain events. Mongo/ES/Redis are NOT updated.
   The only deferred artifact is the `StockSyncOutboxMessage` (`:431`), consumed asynchronously by
   `StockSyncOutboxProcessor` to replicate stock to downstream read models. Analytics and reports read
   SQL (`LedgerEntries`/`SalesInvoices`) directly, so they are consistent by construction. This is
   deliberate: projections are event-driven, and the sale publishes no events.

7. **Recovery** — idempotency is mandatory (`X-Payment-Attempt-Id`/`X-Pos-Cart-Id`, `:100-128`) and
   enforced by a claim-token + deterministic request hash + byte-exact stored response
   (`:905-937`). A retry after a network drop returns the identical 200. The accounting outbox row is
   staged inside the tx and marked processed in the SAME tx (`:406-425`); the background
   `AccountingOutboxProcessor` is a recovery path only and replays idempotently. Fail-closed
   reconciliation ensures unresolved postings block downstream money, never silently drop it.

8. **Scalability** — the write path is a single DB transaction with stock locks held to commit; this
   serializes contention on the same (warehouse, variant) but allows independent SKUs/warehouses to
   proceed in parallel. Cashbox GL accounts are already per-warehouse
   (`CommerceAccountingService.cs:1486`), enabling per-warehouse throughput. Deferred work
   (stock-sync, audit, outbox recovery) runs in background Hangfire jobs, scaling independently of the
   sale request.

9. **Deterministic business behavior** — every rule is code-enforced and tenant-scoped
   (`_tenantContext.RequiredTenantId` on every aggregate; `X-Tenant-Id` stripped). Prices, tax,
   discounts, stock, and payment are validated server-side; the response is a pure function of the
   validated request. The same request hash + key always yields the same invoice.

**In one sentence:** Shopixy's cash sale is a SQL-authoritative, single-transaction, synchronously
accounted, pessimistically-locked, idempotent mutation of invoice+stock+ledger+journal, with the
cashbox modeled as a GL account and all cross-runtime propagation deferred to an inventory outbox —
delivering atomicity, double-entry integrity, and deterministic recovery without distributed writes
or projections.

---
*End of FLOW-01 canonical architecture. Every claim cited to file:line in the live backend. Where
prior documentation, DTOs, or naming implied otherwise, implementation wins.*
