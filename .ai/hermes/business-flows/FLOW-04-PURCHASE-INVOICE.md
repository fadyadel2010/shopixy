# FLOW-04 — Purchase Invoice Business Flow: Implementation-Backed Canonical Architecture

> Authority rule: Implementation is the ONLY source of truth. Every claim below is cited to
> `file:line` in the backend. Controllers, DTOs, comments, UI labels, and naming are evidence
> only — where they contradict runtime behavior, behavior wins. This document describes ONE
> complete business transaction: the POS Purchase Invoice, executed by
> `PointOfSale.API/Controllers/PurchasesController.cs` `CreatePurchase` (line 93).
>
> Repo root: `C:\Users\fadya\source\repos\PointOfSale`.
> Sibling docs: FLOW-01 Cash Sale, FLOW-03 Sales Return (same directory).

---

## 0. Executive Summary

### What is a Purchase Invoice?

A **Purchase Invoice** is the inbound-inventory acquisition primitive of Shopixy: a merchant
operator records goods received *from a supplier* into a specific warehouse, in a SINGLE
synchronous HTTP call to `POST /api/purchases` (`PurchasesController.CreatePurchase`,
`PurchasesController.cs:93`). In one PostgreSQL transaction it: creates the invoice header +
line items, increments variant stock, recomputes weighted-average product cost, writes stock
movements, posts the supplier ledger (accounts payable), records optional immediate payment,
and posts the full double-entry accounting journal. The document is created directly in
`InvoiceStatus.Completed` (`PurchasesController.cs:169`) — there is no Draft → Post lifecycle.

### Why does it exist?

It is the *opposite mutation* of the Cash Sale (FLOW-01): where a sale **decrements** stock and
**credits** revenue/cashbox, a purchase **increments** stock and **credits** supplier payables
(and/or cashbox). It is how inventory *cost* enters the system — the weighted-average
`Product.PurchasePrice` computed here (`PurchasesController.cs:200-208`) becomes the COGS basis
that future sales and returns consume.

### How is it implemented? (runtime facts)

1. **One transaction.** `BeginTransactionAsync()` at `PurchasesController.cs:95` (PostgreSQL
   default ReadCommitted); commit happens inside `CompletePurchaseIdempotencyAsync`
   (`PurchasesController.cs:522`). Inventory, cost, ledger, and accounting journal all live in
   this one transaction.
2. **Idempotency is mandatory.** `Idempotency-Key` (or `X-Request-Id`) header is REQUIRED
   (`PurchasesController.cs:547-553`); claim row inserted with `ON CONFLICT DO NOTHING`
   (`:434-439`), fingerprinted by a deterministic request hash (`:577-585`).
3. **Direct mutation, not event-sourced.** State is mutated in place; recovery is via
   compensating revert (`RevertPurchaseEffects`, `:1381`), not replay.
4. **Cost = Weighted Moving Average** on `Product.PurchasePrice` (`:200-208`).
5. **Inventory increment is inside the transaction, before commit** (`:230`).
6. **Supplier ledger** posted via `ILedgerService.ApplySupplierTransaction` (`:275`).
7. **Accounting journal** posted synchronously via `ICommerceAccountingService.PostPosPurchaseAsync`
   (`:343`) inside the same transaction, with an outbox row marked processed (`:355`).
8. **Cashbox capacity validated** before any mutation when cash is used (`:145`, `:1720`).

### How it differs from sibling flows

| Aspect            | Purchase Invoice          | Cash Sale (FLOW-01)      | Sales Return (FLOW-03)   | Purchase Return          | Inventory Adjustment     |
|-------------------|---------------------------|--------------------------|--------------------------|--------------------------|--------------------------|
| Stock direction   | **+increment** (`:230`)   | −decrement               | +increment               | −decrement               | ±either                  |
| Cost effect       | **Sets WMA cost** (`:203`)| Consumes cost            | Reuses snapshot cost     | Reverses WMA cost        | May revalue              |
| Counterparty      | **Supplier** (payable)    | Customer (receivable/cash)| Customer (refund)       | Supplier (debit note)    | None                     |
| Accounting Dr/Cr  | **Dr Inventory 1310**     | Dr Cash / Cr Revenue     | Cr Revenue reversal      | Cr Inventory             | Adjustment account       |
| Movement type     | `StockMovementType.Purchase` (`:240`) | `Sale`       | `SalesReturn`            | `PurchaseReturn`         | `Adjustment`             |
| Lifecycle         | Created `Completed` (`:169`)| Completed              | Completed                | Completed                | Completed                |

---

## 1. Discovery Phase (what exists vs. what does not)

Solution-wide search results (implementation, not docs):

**Exists (authoritative):**
- `PurchasesController` — `PointOfSale.API/Controllers/PurchasesController.cs` (the whole flow).
- `PurchaseInvoice` / `PurchaseInvoiceItem` / `PurchaseInvoicePayment` entities —
  `PointOfSale.Core/Entities/PurchaseInvoice.cs`, `PurchaseInvoiceItem.cs`,
  `PurchaseInvoicePayment.cs`.
- `PurchaseFinancialIntegrityService` (`IPurchaseFinancialIntegrityService`) —
  `PointOfSale.API/Services/PurchaseFinancialIntegrityService.cs`.
- `PurchasePaymentSplitter` — `PointOfSale.API/Services/PurchasePaymentSplitter.cs`.
- `PurchaseRequestIdempotency` / `PurchaseReturnRequestIdempotency` entities.
- `SupplierTransaction`, `Supplier`, `SupplierPayment` (supplier ledger).
- `CommerceAccountingService.PostPosPurchaseAsync` — `PointOfSale.Infrastructure/Accounting/CommerceAccountingService.cs:217`.
- `AccountingOutboxService.EnqueuePosPurchaseAsync` / `EnqueuePurchaseReversalAsync`.
- `PurchaseReturnsController` (separate flow — reversal document).
- Frontend `PurchaseInvoice.jsx`, `PurchaseReturns.jsx`, `PurchasesBySupplierReport.jsx`.

**Does NOT exist as distinct concepts (implementation collapses them):**
- **No `PurchaseOrder`** — there is no PO → approval → receipt lifecycle. The Purchase Invoice
  *is* the goods-receipt event. No `GoodsReceipt`, `GRN`, `ReceiveInventory`, or `InventoryReceipt`
  type exists; receiving is inlined into `CreatePurchase`.
- **No `CostSnapshot` entity for purchases** — cost is a scalar (`Product.PurchasePrice`),
  weighted-average, mutated in place (not a snapshot/FIFO layer table).
- **No `AccountsPayable`/`SupplierBalance` aggregate** — payables are derived from
  `SupplierTransaction` running balance + the GL `SupplierPayables` account (code `2010`).
- **No `PurchaseJournal`/`PurchaseAccounting` module** — accounting is the shared
  `CommerceAccountingService` double-entry engine used by all commerce flows.

Conclusion: Shopixy models procurement as a **single-document, receive-on-invoice** pattern.
"Purchase" = "goods received + supplier invoiced" fused into one atomic transaction.

---

## 2. Business Goal

- **Business Actor:** Merchant staff with role `Owner`, `Admin`, or `Manager`
  (`[Authorize(Roles = "Owner,Admin,Manager")]`, `PurchasesController.cs:92`). Delete requires
  `Owner,Admin` (`:1279`).
- **Business Trigger:** `POST /api/purchases` with a `CreatePurchaseRequest` (supplier, warehouse,
  items, payment) and a mandatory `Idempotency-Key` header.
- **Business Preconditions:**
  - Supplier exists (`:109-114`).
  - Warehouse exists AND `IsActive` (`:117-122`).
  - Each item: product exists (`:638`), a variant resolves (`:685-691`), `UnitCost > 0`
    (`:693-699`), unit (if given) is bound to the product (`:656-662`).
  - If cash is used, cashbox account has sufficient balance (`:145`, `:1740`).
- **Business Completion Conditions:** invoice persisted `Completed`, stock incremented, WMA cost
  updated, supplier ledger + `SupplierTransaction` written, accounting journal posted & outbox row
  processed, idempotency record marked `Completed`, transaction committed (`:508-524`).
- **Business Failure Conditions:** any validation failure short-circuits with the transaction
  committed but recording the failure response in the idempotency row (`CompletePurchaseIdempotencyAsync`);
  any *exception* triggers `transaction.RollbackAsync()` + `ChangeTracker.Clear()` and HTTP 500
  (`:401-407`).
- **Business Invariants:**
  - `TotalAmount = Subtotal - Discount + Tax`; `RemainingAmount = TotalAmount - PaidAmount`
    (computed, `PurchaseInvoice.cs:64`).
  - `PaidAmount ≤ TotalAmount` (`PurchasePaymentSplitter.cs:44-47`).
  - `Cash + Card = PaidAmount` when split provided (`PurchasePaymentSplitter.cs:65`;
    re-checked in accounting at `CommerceAccountingService.cs:272`).
  - Journal must balance (Dr Inventory = Cr Cash+Card+Payables) — enforced by posting engine.
  - `TenantId` NOT NULL, stamped from `ITenantContext.RequiredTenantId` (`:162`).

---

## 3. Runtime Collaboration

| Runtime            | Role in Purchase Invoice                                   | Evidence |
|--------------------|-----------------------------------------------------------|----------|
| **Procurement**    | `PurchasesController` orchestrates the whole document      | `:93`    |
| **Inventory**      | `ProductVariantStockMutationSupport` locks + mutates stock, emits `StockMovement` | `:179`, `:230`, `:233` |
| **Catalog/Cost**   | `Product.PurchasePrice` WMA recompute                      | `:200-208` |
| **Supplier Ledger**| `ILedgerService.ApplySupplierTransaction` + `SupplierTransaction` rows | `:275`, `:285` |
| **Accounting**     | `ICommerceAccountingService.PostPosPurchaseAsync` (double-entry) | `:343` |
| **Accounting Outbox**| `IAccountingOutboxService.EnqueuePosPurchaseAsync` then mark-processed | `:336`, `:355` |
| **Treasury**       | Cashbox capacity read via `ITreasuryReadService` + `ICashboxLedgerAccountService` | `:145`, `:1733-1738` |
| **Party Balance**  | `IPartyBalanceMaintenanceService.RebuildPartyBalanceAsync` on revert | `:1499` |
| **Audit**          | `IAuditService.LogCreate/LogDelete`                        | `:377`, `:1329` |
| **Events/Analytics**| `ProductVariantStockMutationSupport.PublishCommittedStockEventsAsync` (post-commit) | `:379-385` |
| **Warehouse**      | Warehouse validation + per-warehouse cashbox account resolution | `:117`, `:294` |

---

## 4. Execution Timeline (reverse-engineered, `CreatePurchase`)

```
POST /api/purchases
  │
  ├─ BeginTransactionAsync (ReadCommitted)                         :95
  ├─ TryClaimPurchaseIdempotencyAsync (INSERT ON CONFLICT)         :96 / :434
  │     └─ short-circuit if replay / in-progress / fingerprint mismatch
  ├─ Validate Supplier                                             :109
  ├─ Validate Warehouse (must be active)                           :117
  ├─ PreparePurchaseItemsAsync (product/variant/unit, UnitCost>0)  :124 / :630
  ├─ LockPurchaseCostProductsAsync (FOR UPDATE on Products)        :134 / :716
  ├─ CalculatePurchaseFinancialSummary (subtotal/tax/total)        :135 / :762
  ├─ ResolvePurchasePaymentSplit (cash/card/credit)                :136 / :1951
  ├─ ValidateCashboxCapacityAsync (if cash > 0)                    :145 / :1720
  ├─ GenerateInvoiceNumberAsync (PG sequence nextval)              :157 / :1608
  ├─ new PurchaseInvoice { Status = Completed }                    :159
  ├─ LockMultipleStocksAsync (row locks, create-if-missing)        :179
  ├─ LoadProductStockTotalsAsync (pre-mutation totals)             :185
  │
  ├─ FOREACH item:                                                 :188
  │     ├─ baseUnitCost = UnitCost / conversionFactor              :200
  │     ├─ WMA: Product.PurchasePrice = (Σqty·oldCost + qty·newCost)/(Σqty+qty)  :203
  │     ├─ add PurchaseInvoiceItem                                 :211
  │     ├─ variantStock.Quantity += baseQuantity   ◄── STOCK UP    :230
  │     ├─ add StockMovement (Purchase, QuantityAfter)             :233
  │     └─ record pending stock-change event                       :254
  │
  ├─ set invoice Subtotal/Tax/Total                                :267
  ├─ _context.PurchaseInvoices.Add(invoice)                        :272
  ├─ ApplySupplierTransaction(Purchase, credit=Total)  ◄── PAYABLE :275
  ├─ add SupplierTransaction (Credit = Total)                      :285
  ├─ IF PaidAmount>0: ApplySupplierTransaction(PaymentMade, debit) :306
  │     └─ add SupplierTransaction (Debit = Paid)                  :316
  ├─ EnqueuePosPurchaseAsync (accounting outbox row)               :336
  ├─ PostPosPurchaseAsync (synchronous double-entry journal)       :343
  │     └─ throw if !Success                                       :349
  ├─ MarkSynchronousPurchaseAccountingOutboxProcessedAsync         :355
  ├─ SaveChangesAsync                                              :374
  ├─ idempotencyRecord.PurchaseInvoiceId = invoice.Id             :375
  ├─ AuditService.LogCreate                                        :377
  ├─ PublishCommittedStockEventsAsync (in-tx, pre-commit build)    :379
  ├─ CompletePurchaseIdempotencyAsync                              :387
  │     ├─ record.Status = Completed + snapshot response           :515
  │     ├─ SaveChangesAsync                                        :521
  │     └─ transaction.CommitAsync()  ◄── COMMIT                   :522
  └─ 200 OK PurchaseResponse
```

Failure path: any thrown exception → `transaction.RollbackAsync()` +
`ChangeTracker.Clear()` → HTTP 500 (`:401-407`). Everything (stock, cost, ledger, journal)
is undone atomically because it all lives in the single DB transaction.

---

## 5. Critical Architecture Questions (answered from implementation)

**Q: Append-only, mutable, event-sourced, compensation, or direct mutation?**
→ **Direct mutation.** The invoice, `Product.PurchasePrice`, and `ProductVariantStock.Quantity`
are updated in place. There is NO event store and NO projection rebuild as the source of truth.
Corrections use **compensation**: `RevertPurchaseEffects` (`:1381`) subtracts stock, restores
prior WMA cost (`:1419-1433`), deletes ledger/supplier rows (`:1487-1495`), and rebuilds the
supplier balance (`:1499`). Update = revert-then-reapply (`:1024-1028` then re-add). Optimistic
concurrency guard: `AccountingMutationVersion` (`PurchaseInvoice.cs:29`; checked `:884`).

**Q: When is inventory increased — before/after commit, inside/outside transaction?**
→ **Inside the transaction, before commit.** `variantStock.Quantity += baseQuantity` at `:230`,
inside the `BeginTransactionAsync`(`:95`)…`CommitAsync`(`:522`) scope. Stock *events* are built
in-tx (`:379`) but represent committed state; the commit follows immediately in
`CompletePurchaseIdempotencyAsync`.

**Q: How is inventory cost established?**
→ **Weighted Moving Average (WMA)** on the scalar `Product.PurchasePrice` (`:200-208`):
```
baseUnitCost = itemRequest.UnitCost / conversionFactor
if (totalStock + baseQuantity > 0)
    Product.PurchasePrice = (totalStock*PurchasePrice + baseQuantity*baseUnitCost)
                            / (totalStock + baseQuantity)
else
    Product.PurchasePrice = baseUnitCost
```
Not FIFO, not standard cost, not a per-lot snapshot. `totalStock` is the sum of variant stock
across the product (`LoadProductStockTotalsAsync`, `:1885`). Cost is per **Product**, not per
variant or per warehouse. Multi-unit purchases are normalized to base units via
`ConversionFactor` before averaging.

---

## 6. Aggregate Analysis

| Aggregate                | Mutation in this flow                                              | Evidence |
|--------------------------|-------------------------------------------------------------------|----------|
| `PurchaseInvoice`        | Created `Completed`; header totals set; `AccountingMutationVersion`| `:159`, `:267`, `:1042` |
| `PurchaseInvoiceItem`    | One per line; snapshots `UnitPrice`, `ConversionFactor`, `BaseQuantity`, tax | `:211` |
| `PurchaseInvoicePayment` | Entity exists, but **not written** by `CreatePurchase`; payment recorded via `SupplierTransaction` instead | entity only |
| `Product`                | `PurchasePrice` WMA recompute                                     | `:203` |
| `ProductVariantStock`    | `Quantity += baseQuantity`                                        | `:230` |
| `StockMovement`          | Inserted, type `Purchase`, price=baseUnitCost, QuantityAfter      | `:233` |
| `SupplierTransaction`    | `Purchase` (credit=Total); optional `PaymentMade` (debit=Paid)    | `:285`, `:316` |
| `AccountLedger`          | Written by `ApplySupplierTransaction`; removed on revert          | `:275`, `:1487` |
| `JournalEntry` (+lines)  | Double-entry journal posted via `PostPosPurchaseAsync`            | `CommerceAccountingService.cs:342-354` |
| `AccountingOutboxMessage`| Enqueued then marked processed (synchronous)                      | `:336`, `:355` |
| `PurchaseRequestIdempotency` | Claim → Completed with response snapshot                      | `:434`, `:515` |

Note the DTO/entity `PurchaseInvoicePayment` and `invoice.Payments` navigation exist but the
create path does not populate them — the **supplier ledger `SupplierTransaction`** is the
authoritative payment record. This is an implementation-vs-model divergence (see §17).

---

## 7. Inventory Analysis

- **Warehouse selection:** explicit `request.WarehouseId`, must be active (`:117`). Cost/stock
  are warehouse-scoped for stock (`ProductVariantStock` per warehouse) but WMA cost is
  **product-global** (`Product.PurchasePrice`).
- **Variant validation:** `ProductVariantStockMutationSupport.ResolveVariantAsync` (`:679`);
  every line must resolve to a variant or the request is rejected (`:685-691`).
- **Quantity increase:** `variantStock.Quantity += baseQuantity` (`:230`); base units =
  `Quantity * ConversionFactor` (`PurchaseInvoiceItem.cs:29`).
- **Cost calculation/updates:** WMA on `Product.PurchasePrice` (`:203`); reverted proportionally
  on revert (`:1419-1433`).
- **Negative inventory:** purchases only *add* stock. Revert enforces no-negative via
  `ValidateQuantityChange(..., allowNegativeStock:false, "PurchaseRevert")` (`:1436-1440`).
- **Stock movements:** `StockMovementType.Purchase` on create (`:240`); `Adjustment` with
  `ReferenceType="PurchaseInvoiceRevert"` + idempotency key on revert (`:1453`, `:1463`).
- **Locking:** `LockMultipleStocksAsync` row-locks each (warehouse,variant) with
  create-if-missing (`:179`); products `FOR UPDATE` locked (`:716`).

---

## 8. Supplier Ledger Analysis

- **Two-leg ledger model.** Every purchase writes a `Purchase` transaction crediting the supplier
  balance by the full `TotalAmount` (we owe supplier), `:275-283`. If any amount is paid
  immediately, a second `PaymentMade` transaction debits the balance by `PaidAmount`, `:306-314`.
- **Running balance:** `ApplySupplierTransaction` returns `BalanceAfter`, stored on each
  `SupplierTransaction` (`:297`, `:328`). The net payable for the invoice = Total − Paid; the
  supplier's outstanding balance is the accumulated running balance.
- **Accounts Payable (GL):** the credit balance also lands in GL account `SupplierPayables`
  (`2010`) via the accounting journal (`CommerceAccountingService.cs:333-340`) — only the unpaid
  portion `TotalAmount - paidPortion` is credited to payables.
- **Settlement model:** later standalone settlement is `SuppliersController.CreateSupplierPayment`
  (`:157`) → `ApplySupplierTransaction` (`:261`) + `EnqueueSupplierPaymentAsync` (`:288`) →
  `PostSupplierPaymentAsync` (Dr Payables / Cr funding account, `CommerceAccountingService.cs:369`).
- **Ledger repair:** on revert, all `AccountLedgers` and `SupplierTransactions` for the invoice
  are deleted (`:1487-1495`) and `RebuildPartyBalanceAsync(Supplier, ...)` recomputes the balance
  from scratch (`:1499`) — fail-closed integrity.

---

## 9. Financial Analysis

- **Payment methods** (`PurchasePaymentSplitter.cs`): `Cash`, `Card`, `Mixed`, `Credit`.
  `BankTransfer` is explicitly rejected for purchase create (`:78-79`, `:100`).
  - **Cash purchase:** Cash=Paid, Card=0 (`:88`, `:97`).
  - **Card purchase:** Card=Paid, Cash=0 (`:89`, `:98`).
  - **Mixed:** must supply both Cash+Card > 0 that sum to Paid (`:76-77`, `:65`).
  - **Credit:** `PaymentMethod.Credit` with `PaidAmount>0` requires `InitialPaymentMethod`
    (Cash/Card) or explicit split (`:84-92`); a pure credit purchase has Paid=0.
- **Supplier payable:** `TotalAmount - PaidAmount` remains on the supplier balance and GL 2010.
- **Journal entries** (`PostPosPurchaseAsync`, `CommerceAccountingService.cs:311-352`):
  - **Debit Inventory (`1310`)** = `invoice.TotalAmount` (full goods value incl. tax) — `:314`.
  - **Credit Cashbox** (per-warehouse account) = cash portion — `:317-323`.
  - **Credit POS Visa (`1020`)** = card portion — `:325-331`.
  - **Credit Supplier Payables (`2010`)** = `Total − paidPortion` — `:333-340`.
  - Journal is balanced: Dr Inventory = Cr(Cash + Card + Payables).
- **Treasury interaction:** read-only pre-check of cashbox capacity (`:1720-1774`); actual cash
  movement is the GL credit to the cashbox account — no separate treasury table write in the
  create path ("legacy treasury tables intentionally ignored",
  `PurchaseFinancialIntegrityService.cs:11-13`).
- **VAT/Tax:** per-line `TaxRate` → `TaxAmount = itemSubtotal * TaxRate/100` (`:772`); summed to
  header `TaxAmount` (`:782`); **included in the Inventory debit** (Total includes tax — tax is
  capitalized into inventory cost, not booked to a separate input-VAT account). This is a
  meaningful accounting choice: **no recoverable input-VAT account is used** (see §17).
- **Discount:** `PurchaseInvoice` has `DiscountAmount`/`DiscountPercentage` fields
  (`PurchaseInvoice.cs:39-44`) but `CreatePurchaseRequest` has **no discount input** and
  `CalculatePurchaseFinancialSummary` never applies discount (`:762-790`). Discount is dead
  schema in this flow (see §17).
- **Freight / additional costs:** **not implemented** — no landed-cost fields or logic exist.
- **Currency / exchange rate:** **not implemented** — single-currency; no FX fields.

---

## 10. SQL Transaction Analysis

- **Boundary:** `BeginTransactionAsync()` at `:95`; commit inside
  `CompletePurchaseIdempotencyAsync` at `:522`. One transaction wraps idempotency claim, stock,
  cost, ledger, supplier transactions, accounting journal, outbox, and audit.
- **Isolation level:** PostgreSQL default **ReadCommitted** (no explicit level passed to
  `BeginTransactionAsync`).
- **Lock ordering (deadlock avoidance):**
  1. Idempotency row (`INSERT ... ON CONFLICT`, `:434`).
  2. Products `FOR UPDATE`, ordered by `Id` (`LockPurchaseCostProductsAsync`, `:718`).
  3. Prepared items sorted by `(Variant.Id, Product.Id)` deterministically (`:710-713`).
  4. Variant stocks locked via `LockMultipleStocksAsync`, distinct by (warehouse,variant) (`:179`).
  5. Invoice number: PG `nextval` sequence (no table lock); non-PG uses tenant `FOR UPDATE` (`:1672`).
- **Rollback:** exception → `RollbackAsync()` + `ChangeTracker.Clear()` (`:403-404`).
- **Recovery / atomic guarantees:** because the journal post is synchronous and inside the
  transaction (`:343`), a failed post throws (`:349-353`) and rolls the entire purchase back —
  no partial "stock in, no journal" state. The outbox row is marked processed within the same
  transaction (`:355`), keeping outbox and journal consistent.

---

## 11. Event Analysis

- **Accounting outbox** (`AccountingOutboxMessage`): `EnqueuePosPurchaseAsync` (`:336`) inserts a
  `PosPurchase` row; it is immediately marked processed after the synchronous journal post
  (`MarkSynchronousPurchaseAccountingOutboxProcessedAsync`, `:355`, `:1776`). On update,
  `EnqueuePurchaseReversalAsync` (`:1206`) writes a `PurchaseReversal` row similarly processed.
  The outbox is the transactional-consistency mechanism, not an async worker path in the happy case
  (the `AccountingOutboxProcessor` handles retries if a row is left unprocessed).
- **Stock events:** `ProductVariantStockMutationSupport.PublishCommittedStockEventsAsync` (`:379`,
  `:1245`) publishes committed stock changes → downstream inventory/analytics/search projections.
- **Inbox:** no purchase-specific inbox; idempotency is the replay guard.
- **Mongo/ES projections:** driven by the published stock events (derivative stores; PostgreSQL
  authoritative per platform rules).
- **Idempotency guarantees:** journal idempotency key
  `PURCHASE:{tenant}:{invoice}[:{operationKey}]` (`CommerceAccountingService.cs:364-367`); outbox
  key includes cash/card/version (`:1834-1849`).

---

## 12. Cost Flow Analysis (mandatory)

- **Where cost originates:** the operator-entered `PurchaseItemRequest.UnitCost` (per selected
  unit), normalized to base units: `baseUnitCost = UnitCost / conversionFactor` (`:200`, `:1070`).
- **Where cost is stored:**
  1. `Product.PurchasePrice` — the **weighted moving average** cost, product-global (`:203`).
  2. `PurchaseInvoiceItem.UnitPrice` + `ConversionFactor` — an **immutable snapshot** of what was
     paid on this line (`:221`, `:219`).
  3. `StockMovement.Price = baseUnitCost` — the per-movement cost record (`:250`).
- **How future sales use it:** sales read `Product.PurchasePrice` as the COGS basis (the WMA is
  the single cost authority consumed by FLOW-01). Because it is a moving average, each purchase
  shifts the cost of *all* existing units of that product.
- **How returns reuse it:** Purchase revert restores the *prior* WMA by removing this line's value:
  `restoredCost = (currentTotalValue − purchaseValue) / remainingQty` (`:1421-1431`) — an
  approximate inverse of the WMA, not an exact replay (acceptable because WMA is path-independent
  in aggregate value, but can drift if intervening purchases changed the average; documented risk).
- **How valuation changes:** inventory GL value increases by `TotalAmount` (Dr Inventory 1310,
  incl. tax) at purchase; the on-hand quantity × WMA is the perpetual valuation.

---

## 13. Business Rules (implementation-enforced)

| Rule | Enforced? | Evidence |
|------|-----------|----------|
| Supplier required & must exist | Yes | `:109-114` |
| Warehouse required & must be active | Yes | `:117-122` |
| Product must exist per line | Yes | `:638-645` |
| Variant must resolve per line | Yes | `:685-691` |
| Unit must be bound to product | Yes | `:656-662` |
| `UnitCost > 0` (zero cost rejected) | Yes | `:693-699` |
| `Quantity ≥ 0.01` | Yes (DataAnnotation) | `PurchaseItemRequest`, `:1988` |
| `TaxRate` 0–100 | Yes (DataAnnotation) | `:1992` |
| `PaidAmount ≥ 0` and `≤ Total` | Yes | `PurchasePaymentSplitter.cs:27,44` |
| Cash+Card = Paid (split integrity) | Yes | `:65`; re-checked accounting `:272` |
| Idempotency key required | Yes | `:547-553` |
| Duplicate request prevention (same key+fingerprint → stored response) | Yes | `:483-493` |
| Different request with same key → 409 | Yes | `:483-489` |
| Invoice number uniqueness | Yes (PG sequence `nextval`) | `:1614`, `:1633` |
| Cashbox sufficient funds for cash | Yes | `:1740-1753` |
| Optimistic concurrency on update | Yes (`ExpectedMutationVersion`) | `:872-896` |
| Returned/cancelled invoice cannot be updated | Yes | `:898-911` |
| Posted-journal invoice cannot be deleted | Yes (`PostedJournalsExistException`) | `:1336-1345`, `PurchaseFinancialIntegrityService.cs:62-65` |
| Duplicate invoice-number prevention (business-level) | No explicit supplier-invoice-number dedupe | not implemented (see §17) |
| Currency / exchange-rate validation | N/A | not implemented |
| Discount validation | N/A | discount not wired into create |

---

## 14. Failure Analysis

- **Duplicate purchase (retry):** same `Idempotency-Key` + same fingerprint → the stored response
  is replayed (`:491-493`); in-progress → 409 "already being processed" (`:496-499`); same key,
  different body → 409 "used for a different request" (`:483-489`).
- **Network failure mid-request:** the transaction never commits → full rollback; the idempotency
  claim row is rolled back too (it lives in the same tx), so a clean retry re-claims.
- **Supplier/warehouse/product/variant not found:** validation short-circuit, `BadRequest`,
  idempotency row completed with the error snapshot (`:111`, `:118`, `:641`, `:687`).
- **Inventory failure (lock/negative):** revert path validates and throws; create path only adds.
- **Accounting failure:** `PostPosPurchaseAsync` returns `!Success` → `throw` → full rollback
  (`:349-353`) — never leaves stock-in-without-journal.
- **Ledger failure:** exception → rollback; on mutation, `RebuildPartyBalanceAsync` repairs.
- **Concurrency:** update requires `ExpectedMutationVersion`; mismatch → 409 (`:884-896`); row
  locks (`FOR UPDATE`) serialize concurrent mutations of the same invoice/products/stocks.
- **Cashbox validation unavailable:** returns 503 `CASHBOX_VALIDATION_UNAVAILABLE`, purchase not
  recorded (`:1763-1770`).
- **Rollback/recovery:** single-transaction atomicity + compensating `RevertPurchaseEffects` for
  update/delete; unprocessed outbox rows are cleaned only if never claimed/attempted
  (`PurchaseFinancialIntegrityService.cs:86-106`) — in-flight rows block mutation (fail closed).

---

## 15. Performance Analysis

- **Round trips:** per-item sequential `FindAsync`/queries in `PreparePurchaseItemsAsync` (`:636`)
  and per-product `FOR UPDATE` in a loop (`:718`) — N+1 shaped; acceptable for typical small
  purchase baskets but a bottleneck for very large invoices.
- **Locks:** products `FOR UPDATE` + per-variant stock row locks held for the whole transaction
  (including the synchronous accounting post) — lock hold time includes journal posting.
- **Hot path:** WMA recompute + stock increment loop (`:188-265`).
- **Scaling bottleneck:** synchronous in-transaction journal posting (`:343`) lengthens the
  transaction; the PG invoice sequence avoids the legacy tenant-lock serialization (`:1612-1616`).
- **Deferred work:** stock-change domain events published after `SaveChanges` (`:379`), still
  in-transaction; downstream projections are async consumers.

---

## 16. Security Analysis

- **Permissions:** create/update `Owner,Admin,Manager` (`:92`, `:848`); delete `Owner,Admin`
  (`:1279`).
- **Tenant isolation:** `TenantId` stamped from `ITenantContext.RequiredTenantId` on invoice,
  items, movements, supplier transactions (`:162`, `:236`, `:288`); all mutation loads scope by
  `TenantId` even under `IgnoreQueryFilters` (`:1516`, `:728`). Idempotency key is tenant-scoped
  (`BuildSupplierMutationIdempotencyKey`, `:574`).
- **Warehouse/supplier ownership:** enforced via tenant-scoped `FindAsync`/queries; cross-tenant
  reference would fail lookups.
- **Fraud / replay prevention:** mandatory idempotency + deterministic fingerprint (`:577`);
  journal idempotency key (`CommerceAccountingService.cs:364`).
- **Idempotency:** claim row + response snapshot; `X-Payment-Attempt-Id` header supported (`:37`,
  `:531`).
- **Log hygiene:** idempotency keys sanitized to mask financial amounts before logging
  (`SanitizeIdempotencyKey`, `:1899`).

---

## 17. Documentation Validation (corrections — implementation wins)

1. **`PurchaseInvoicePayment` / `invoice.Payments` is misleading.** The DTO/entity and navigation
   exist (`PurchaseInvoice.cs:73`) but the create flow records payment via `SupplierTransaction`
   (`PaymentMade`, `:316`), never `PurchaseInvoicePayment`. Payments are NOT stored on the invoice.
2. **`DiscountAmount`/`DiscountPercentage` on `PurchaseInvoice` are dead schema.** No request
   input and no calculation apply them (`:762-790`). The comment "Total = Subtotal - Discount + Tax"
   (`PurchaseInvoice.cs:52`) overstates behavior: effective `Total = Subtotal + Tax`.
3. **Tax is capitalized into inventory, not treated as recoverable input VAT.** The full
   tax-inclusive `TotalAmount` is debited to Inventory (`CommerceAccountingService.cs:314`); there
   is no input-VAT asset account. UI/labels implying VAT recovery are inaccurate.
4. **There is no Purchase Order / Goods Receipt lifecycle.** Any doc/UI implying PO approval or
   separate receiving is aspirational — the invoice IS the receipt (created `Completed`, `:169`).
5. **`InvoiceStatus.Draft` default is never used in create.** Invoices are born `Completed`
   (`:169`); the `Draft` default on the entity (`PurchaseInvoice.cs:24`) is unreachable here.
6. **No supplier-invoice-number duplicate guard.** `InvoiceNumber` is a system-generated
   `PUR-yyyyMMdd-####`; the supplier's own invoice number is not captured or deduplicated.

---

## 18. Hermes Browser Test Specification

Frontend: `PointOfSale.Web/src/pages/PurchaseInvoice.jsx`. API: `POST /api/purchases`.

### Test A — Cash purchase, single item (happy path)
- **Preconditions:** logged in as Owner/Admin/Manager; ≥1 supplier; ≥1 active warehouse; ≥1 product
  with a variant and a unit; cashbox for warehouse funded ≥ line total.
- **User Journey:** open Purchase Invoice → select supplier → select warehouse → add product line
  (qty=10, unitCost=5.00, taxRate=0) → payment method Cash, PaidAmount=50.00 → Save.
- **Expected Runtime State:** 200 OK, `PurchaseResponse` with `InvoiceNumber` `PUR-<date>-####`,
  `TotalAmount=50.00`, `MutationVersion=0`.
- **Expected DB State:** 1 `PurchaseInvoices` row, `Status=Completed`, `Subtotal=50`, `TaxAmount=0`,
  `TotalAmount=50`, `PaidAmount=50`; 1 `PurchaseInvoiceItems` row (`BaseQuantity=10*conv`).
- **Expected Inventory State:** variant `ProductVariantStock.Quantity += 10*conversionFactor`;
  1 `StockMovements` row `MovementType=Purchase`, `Price=5.00/conv`, correct `QuantityAfter`.
- **Expected Cost State:** `Product.PurchasePrice` = WMA per formula (`:203`).
- **Expected Supplier Ledger:** 2 `SupplierTransactions` — `Purchase` (Credit=50) + `PaymentMade`
  (Debit=50); net supplier balance change = 0.
- **Expected Accounting:** 1 balanced journal — Dr Inventory(1310)=50, Cr Cashbox=50; no payables
  line (fully paid). Outbox `PosPurchase` row `ProcessedAtUtc != null`.
- **Assertions:** journal balances; stock delta exact; idempotency row `Completed`.

### Test B — Credit purchase (unpaid)
- **Journey:** payment method Credit, PaidAmount=0, total=100.
- **Expected Ledger:** 1 `SupplierTransaction` `Purchase` (Credit=100); supplier balance +100.
- **Expected Accounting:** Dr Inventory=100, Cr SupplierPayables(2010)=100.
- **Expected DB:** `RemainingAmount=100`.

### Test C — Mixed payment
- **Journey:** Mixed, CashAmount=30, CardAmount=20, PaidAmount=50, total=100.
- **Expected Accounting:** Dr Inventory=100, Cr Cashbox=30, Cr Visa(1020)=20, Cr Payables=50.

### Negative Tests
1. **Missing Idempotency-Key** → 400 (`:547-553`).
2. **UnitCost = 0** → 400 "Unit cost must be greater than zero" (`:693-699`).
3. **Inactive/unknown warehouse** → 400 "Active warehouse not found" (`:117-122`).
4. **Unknown supplier** → 400 "Supplier not found" (`:109-114`).
5. **Cash > cashbox balance** → 400 `INSUFFICIENT_FUNDS` (`:1740-1753`).
6. **Cash+Card ≠ Paid** → 400 "must add up exactly to PaidAmount" (`PurchasePaymentSplitter.cs:65`).
7. **Replay same key+body** → identical stored response, NO duplicate invoice (`:491-493`).
8. **Same key, different body** → 409 (`:483-489`).
9. **Update with stale `ExpectedMutationVersion`** → 409 (`:884-896`).
10. **Delete invoice with posted journal** → 409 `POSTED_JOURNALS_EXIST` (`:1336-1345`).

---

## 19. Runtime Collaboration Summary

Procurement (`PurchasesController`) is the sole orchestrator. Within one PG transaction it drives:
Inventory (lock + increment + StockMovement), Catalog/Cost (WMA on Product), Supplier Ledger
(`ApplySupplierTransaction` + `SupplierTransaction`), Accounting (`PostPosPurchaseAsync` balanced
journal), Accounting Outbox (enqueue + synchronous mark-processed), Treasury (read-only cashbox
capacity check), Warehouse (validation + per-warehouse cashbox account), and Audit. Analytics/search
receive committed stock domain events after `SaveChanges`. No cross-runtime call escapes the
transaction boundary except the async event consumers.

---

## 20. Flow Relationship Analysis (vs FLOW-01 Cash Sale, FLOW-03 Sales Return)

- **Shared architecture:** single-transaction synchronous mutation; mandatory idempotency with
  fingerprint + response snapshot; `ProductVariantStockMutationSupport` for locking + movements +
  events; `CommerceAccountingService` double-entry engine + accounting outbox; `ITenantContext`
  isolation; `AccountingMutationVersion` optimistic concurrency; compensating revert on mutate.
- **Shared services:** `ILedgerService`, `IPartyBalanceMaintenanceService`, `ICommerceAccountingService`,
  `IAccountingOutboxService`, `IAuditService`, `ProductVariantStockMutationSupport`.
- **Shared aggregates:** `Product`, `ProductVariantStock`, `StockMovement`, `JournalEntry`,
  `AccountingOutboxMessage`.
- **Opposite mutations:** Purchase **increments** stock (`:230`) & **credits payables/cashbox**;
  Cash Sale **decrements** stock & credits revenue/debits cash; Sales Return **increments** stock &
  reverses revenue. Counterparty is Supplier (payable) vs Customer (receivable/refund).
- **Cost lifecycle:** Purchase **writes** WMA cost into `Product.PurchasePrice`; Sale/Return
  **consume** it as COGS. Purchase is the cost *source*; sales are cost *sinks*.
- **Inventory lifecycle:** Purchase is the inbound origin; sale is outbound; both use identical
  StockMovement plumbing with different `MovementType`.
- **Accounting lifecycle:** all three post through the same balanced-journal engine; Purchase's
  distinguishing legs are Dr Inventory / Cr Payables (vs Sale's Dr Cash / Cr Revenue).

---

## 21. Canonical Pattern

- **Document Strategy:** single fused document (invoice = goods receipt), born `Completed`.
- **Inventory Strategy:** perpetual, row-locked, increment-in-transaction, per-variant stock.
- **Cost Strategy:** Weighted Moving Average on product-global `Product.PurchasePrice`.
- **Accounting Strategy:** synchronous in-transaction double-entry (Dr Inventory / Cr Cash+Card+Payables).
- **Ledger Strategy:** two-leg supplier ledger (Purchase credit + optional PaymentMade debit) with
  running balance + rebuildable party balance.
- **Recovery Strategy:** single-transaction atomicity + compensating revert; fail-closed on
  in-flight outbox/posted journals.
- **Concurrency Strategy:** `FOR UPDATE` row locks (ordered) + `AccountingMutationVersion` optimistic check.
- **Pricing Authority:** operator-entered `UnitCost` is the sole cost input; server computes WMA.
- **Idempotency Strategy:** mandatory header + tenant-scoped claim + deterministic fingerprint +
  stored response replay; journal + outbox idempotency keys.
- **Transaction Strategy:** one PostgreSQL ReadCommitted transaction spanning inventory, cost,
  ledger, accounting, and outbox.

---

## 22. Final Architecture Question

**How does Shopixy acquire inventory while preserving consistency, supplier-balance correctness,
cost correctness, accounting integrity, recovery, auditability, runtime boundaries, and scalability?**

Shopixy acquires inventory through a **single atomic PostgreSQL transaction** in
`PurchasesController.CreatePurchase` that fuses goods-receipt and supplier invoicing into one
`Completed` document. **Inventory consistency** is guaranteed by ordered `FOR UPDATE` product locks
(`:718`) and per-(warehouse,variant) stock row locks (`:179`), with the increment
(`:230`) committed only if every downstream step succeeds. **Supplier-balance correctness** comes
from the two-leg ledger — a `Purchase` credit for the full total and an optional `PaymentMade` debit
(`:275`, `:306`) — with a rebuildable party balance on revert (`:1499`). **Cost correctness** is
the weighted-moving-average recompute of `Product.PurchasePrice` (`:203`), normalized to base units,
which is the single COGS authority every sale later consumes. **Accounting integrity** is enforced by
posting a balanced double-entry journal *synchronously inside the same transaction* (`:343`); a failed
post throws and rolls back everything, so stock can never enter without a matching journal, and the
outbox row is marked processed in the same commit (`:355`). **Recovery** uses compensation
(`RevertPurchaseEffects`, `:1381`) plus fail-closed guards that refuse to mutate invoices with
in-flight outbox work or posted journals (`PurchaseFinancialIntegrityService.cs:62-106`).
**Auditability** is provided by immutable per-line cost snapshots, `StockMovement` history, supplier
transactions with `BalanceAfter`, and `IAuditService` create/delete logs (`:377`, `:1329`).
**Runtime boundaries** are respected because Procurement orchestrates but delegates to the canonical
Inventory, Ledger, and Accounting services — no runtime writes another's aggregate directly, and the
only work leaving the transaction is async projection events. **Scalability** is served by a
lock-free PostgreSQL sequence for invoice numbering (`:1614`) and tenant-scoped idempotency, bounded
by the deliberate trade-off of synchronous journal posting inside the transaction — chosen so that
financial integrity is never eventually-consistent for the money-moving path.

Implementation always wins.
