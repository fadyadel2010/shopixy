# FLOW-05 — Purchase Return Business Flow: Implementation-Backed Canonical Architecture

> Authority rule: Implementation is the ONLY source of truth. Every claim below is cited to
> `file:line` in the live backend. Controllers, DTOs, comments, and naming are evidence only — where
> they contradict runtime behavior, behavior wins. This document describes ONE complete business
> transaction: the POS **purchase return** (returning goods to a supplier against a previously
> **completed** purchase invoice), executed by
> `PointOfSale.API/Controllers/PurchaseReturnsController.cs` `CreatePurchaseReturn` (line 199).
>
> Verification note: all line numbers below were confirmed by reading the live backend on the
> `feature/new-pr210` branch. Repo root: `C:\Users\fadya\source\repos\PointOfSale`.
>
> Companion skill: `shopixy-purchase-return-notes` (condensed map). Where this doc and that skill
> diverge, this doc is authoritative because it is verified directly against the sources cited.

---

## 0. Executive Summary

### What is a Purchase Return?

A **Purchase Return** is the POS operation of sending goods back to a supplier against a previously
received/completed **purchase invoice**, removing them from warehouse stock, reducing the amount the
merchant owes the supplier (supplier payable), and posting the financial journal that reverses the
inventory asset and the supplier-payable liability. In the data model it is a `Return` aggregate with
`ReturnType = PurchaseReturn` (`Return.cs:20`, `ReturnType.cs:5`), containing `ReturnItem` lines
(`ReturnItem.cs`).

It is NOT a cancellation/void of the original purchase. The original `PurchaseInvoice` is left intact
and immutable except for a monotonic `AccountingMutationVersion` counter and a `Status` transition
(see §8). The return is a **separate, additive** document
(`PurchaseReturnsController.cs:594` `_context.Returns.Add(purchaseReturn)`).

### Why does it exist?

To reverse the economic effect of a completed purchase (damaged goods, wrong goods, over-shipment)
while preserving auditability: the original purchase must remain the historical record, and the
reversal must be independently traceable. Shopixy therefore models the return as a **new authoritative
document** that *compensates* the purchase's inventory, supplier-ledger, and financial effects — not a
destructive edit of the purchase.

### How is it implemented? (the 15 runtime facts)

1. **Two-phase commit, not one.** The return document + stock + supplier ledger are committed
   synchronously inside ONE SQL transaction (`PurchaseReturnsController.cs:231` `BeginTransactionAsync`,
   commit at `:667`). The **accounting journals** are posted in a SEPARATE transaction — synchronously
   attempted *after* commit (`:671` `_commerceAccounting.PostPurchaseReturnAsync`), with the **outbox
   worker** as the recovery path. This is the defining architectural fact of the flow.
2. **The return is a COMPENSATION transaction, not a reverse-transaction.** Evidence: accounting uses
   `PostPurchaseReturnAsync` → `PostJournalAsync` (`CommerceAccountingService.cs:722,759`) — a *fresh,
   balanced* journal keyed by idempotency `PURCHASE_RETURN:{tenant}:{returnId}` (`:745`), NOT
   `ReverseJournalAsync`. The original purchase journal is never mutated. See §7.
3. **Idempotency is MANDATORY at the boundary.** The request MUST carry `Idempotency-Key` (or
   `X-Request-Id`); if absent → `400` (`:812-818`). The key (scoped `SUPPLIER:{tenant}:{key}`) is
   claimed via `INSERT ... ON CONFLICT (TenantId,Key) DO NOTHING`
   (`:734-739` `TryClaimPurchaseReturnIdempotencyAsync`) and the byte-exact response is stored
   (`:652-655`).
4. **The original invoice row is write-locked for the whole return tx** (`FOR UPDATE` Postgres /
   `UPDLOCK,ROWLOCK,HOLDLOCK` SQL Server, `LoadOriginalPurchaseInvoiceForReturnMutationAsync`
   `:938-988`). This is the PR-CON-01 fence: competing returns against the same invoice revalidate
   against committed history instead of a stale pre-return snapshot.
5. **Stock is DECREASED from the same variant in the SAME warehouse** the original invoice used.
   Decremented via `variantStock.Quantity -= line.BaseQuantity` (`:542`) AFTER acquiring a
   deterministic `(WarehouseId, VariantId)` lock (`LockMultipleStocksAsync` `:448`,
   `CreateIfMissing:true`). Supplier+Warehouse CANNOT be redirected on a return — they MUST equal the
   original invoice's (`:270-278`).
6. **Cost is re-weighted by WMA, not restored to a prior snapshot.** The remaining inventory value is
   backed out using `Product.PurchasePrice * totalStock` minus `BaseCost * returnedBaseQty`, then
   divided by remaining stock (`:533-539`). `WMA`, recomputed, not rolled back. See §11/§14.
7. **No return state machine.** `Return` has NO status column (`Return.cs:9-52`). "State" = parent
   `PurchaseInvoice.Status` (`PartiallyReturned`/`FullyReturned`, `:590-592`) + outbox `ProcessedAtUtc`.
   No Rejected/Cancelled states; returns are IMMUTABLE once committed (no update/delete endpoint).
8. **Fail-open on accounting, fail-closed on data.** If the synchronous accounting post fails AFTER
   the local data commit, the return is already durable; the error is logged and the outbox worker
   retries (`:674-682`). If any data validation/locks fail BEFORE commit, the whole transaction rolls
   back (every branch does `await transaction.RollbackAsync()` then returns an error).
9. **Stock sync (Mongo/ES) happens ONLY after commit.** Events go to `StockSyncOutboxMessages`
   (`:659-665` `PublishCommittedStockEventsAsync`) and are invisible to projections until the
   post-commit publish succeeds. Uncommitted returns never reach projections.
10. **The `Return` table is SHARED with sales returns.** Purchase returns key off
    `OriginalPurchaseInvoiceId` (PurchaseInvoice FK, `Return.cs:33`); `OriginalInvoiceId` (SalesInvoice
    FK, `:25`) stays NULL for them. Both are filtered by `ReturnType` (`GetPurchaseReturns`
    `:91`, `:151`).
11. **A Debit Note is NOT issued.** The supplier payable reduction is posted via `LedgerService`
    (supplier ledger + `AccountLedger` + `SupplierTransaction`) AND the accounting journal debits
    `SupplierPayables` (`:748-750`). There is no `DebitNote` entity, table, or document number.
12. **`RefundToCash` / `RefundAmount` on the DTO are INERT for purchase returns.** No cash/card refund
    payout path exists (contrast Sales Return's `PosRefund`/`RefundMethod`). The response echoes
    `RefundAmount = TotalAmount` (`:648`) but NOTHING refunds cash — only the supplier payable is
    reduced. Do not document a refund payout.
13. **Mutation version is the idempotency/replay fence for accounting.** `AccountingMutationVersion++`
    on the invoice (`:582`) is carried into the outbox payload (`:627-631`). The outbox worker SKIPS a
    stale message when `message version != invoice.AccountingMutationVersion`
    (`AccountingOutboxProcessor.cs:1859-1869`). No double post.
14. **Ledger-only services are forbidden from reading Returns.** `ForbiddenBusinessDbSets.Names`
    includes `"Returns"`, `"ReturnItems"`, `"SupplierTransactions"`, `"SupplierPayables"` (business
    tables) — financial reporting must read the immutable ledger (`JournalEntries`/`LedgerEntries`).
15. **CommerceHub vendor returns are a DIFFERENT capability.** `PosAdminReturnsController` +
    `CommerceHubReturnStatus` (PickupScheduled/PickedUp/InTransit/DeliveredToVendor/Confirmed/Failed)
    are NOT purchase returns. Do not conflate (see §16 cross-reference).

### How does it differ from Purchase Invoice / Sales Return / Inventory Adjustment?

| Dimension | Purchase Invoice | Purchase Return (this doc) | Sales Return | Inventory Adjustment |
|---|---|---|---|---|
| Direction | +stock, +payable (debit Inventory, credit SupplierPayables/Cash/Card) | −stock, −payable (debit SupplierPayables, credit Inventory) | +stock (restore to customer), −receivable or pay customer | ±stock only |
| New doc? | `PurchaseInvoice` | `Return` (`PurchaseReturn`) | `Return` (`SalesReturn`) | no document; direct stock mutation |
| Supplier effect | create/raise payable | reduce payable (debit) | none (customer side) | none |
| Accounting service | `PostPosPurchaseAsync` | `PostPurchaseReturnAsync` | `PostPosRefundAsync` | none (no journal) |
| Cost handling | sets `PurchasePrice` | re-weights `PurchasePrice` (WMA) | re-weights `PurchasePrice` (WMA) | may set `CostPrice` directly |
| Outbox event | `PosPurchase` | `PurchaseReturn` | `PosRefund` | none (or stock sync only) |
| Supplier+Warehouse redirectable? | chosen at creation | LOCKED to original invoice (`:270-278`) | LOCKED to original invoice | chosen at creation |

---

## 1. Discovery Phase (raw search evidence)

Searched the whole solution for the canonical names. Actual artifacts found (verified by `rg`):

- Controller: `PointOfSale.API/Controllers/PurchaseReturnsController.cs` (whole capability lives here).
- DTOs: `PointOfSale.API/DTOs/PurchaseReturnReportDto.cs`; request/response records in
  `PurchaseReturnsController.cs:1083-1123`.
- Ledger sink: `PointOfSale.API/Services/LedgerService.cs` (`ApplySupplierTransaction` `:57`).
- Accounting post: `PointOfSale.Infrastructure/Accounting/CommerceAccountingService.cs:722`
  (`PostPurchaseReturnAsync`).
- Outbox enqueue: `PointOfSale.Infrastructure/Accounting/AccountingOutboxService.cs:221`
  (`EnqueuePurchaseReturnAsync`).
- Outbox process: `PointOfSale.Infrastructure/Accounting/AccountingOutboxProcessor.cs:1831`
  (`ProcessPurchaseReturnAsync`), dispatch at `:726`.
- Entities: `PointOfSale.Core/Entities/Return.cs`, `ReturnItem.cs`,
  `PointOfSale.Core/Entities/PurchaseReturnRequestIdempotency.cs`.
- Report: `PointOfSale.API/Controllers/ReportsController.cs:2027` (`GetPurchaseReturnsReport`).
- Stock mutation: `PointOfSale.API/Services/Inventory/ProductVariantStockMutationSupport.cs`
  (`:216` `PublishCommittedStockEventsAsync`).
- Enums: `ReturnType.cs`, `TransactionType.cs:8` (`PurchaseReturn`),
  `InvoiceStatus.cs:5-9` (`PartiallyReturned`/`FullyReturned`), `StockMovementType.cs`
  (`PurchaseReturn`).

NOT found (so do not assume they exist): `DebitNote`, `SupplierReturn`, `ReturnToSupplier`,
`GoodsReturn`, `PurchaseRefund`, `PurchaseReversal` (the latter is a separate *invoice*
reversal/cancel path, `AccountingOutboxService.cs:121` `EnqueuePurchaseReversalAsync`, NOT a return),
`VendorReturn`, standalone `PurchaseReturnService`/`PurchaseReturnApplicationService`.

---

## 2. Business Goal

### Business Actor
- **System actor**: the POS operator (auth roles `Owner,Admin,Manager`,
  `PurchaseReturnsController.cs:198` `[Authorize(Roles = "Owner,Admin,Manager")]`).
- **External party**: the **Supplier** — but only as a passive ledger/counterparty; the return is
  initiated by the merchant against that supplier.

### Business Trigger
- A completed purchase invoice (`PurchaseInvoice.Status` in `{Draft, Completed}`, NOT
  `Cancelled`/`FullyReturned`) where goods are sent back to the supplier. UI loads the invoice via
  `GET /api/PurchaseReturns/invoice/{invoiceId}` (`:162`) then POSTs the return.

### Business Preconditions
1. `Idempotency-Key` (or `X-Request-Id`) header present, ≤200 chars, ≤200 char attempt id
   (`:792-834`).
2. `OriginalInvoiceId` provided (`:251-255`).
3. Invoice exists, `IsDeleted=false` (`:257-262`), and `Status` not `Cancelled`/`FullyReturned`
   (`:264-268`).
4. Request `SupplierId`+`WarehouseId` EXACTLY equal the invoice's (`:270-278`).
5. Supplier row exists (`:280-285`); Warehouse exists and `IsActive` (`:287-292`).
6. Per line: product was received on the invoice (`:332-339`); variant was received (`:362-371`);
   unit matches a received unit (`:379-386`); no duplicate `(ProductId,VariantId)` line (`:373-377`);
   cumulative returned base qty ≤ received base qty (`:394-409`).

### Business Completion Conditions
- Return row + items persisted; stock decremented; `PurchasePrice` re-weighted; supplier ledger
  debited; invoice `Status` flipped `PartiallyReturned`↔`FullyReturned` and `AccountingMutationVersion++`;
  outbox message enqueued; idempotency row completed (`:594-658`). Commit at `:667`. Synchronous
  accounting post attempted (`:671`); outbox row marked processed on success (`:688-694`).

### Business Failure Conditions
- Any precondition above unmet → rollback + 400/409 (see §13 for codes).
- Supplier ledger post fails inside tx → rollback (handled by `LedgerService.ApplyPartyTransactionAsync`
  catch `:268-277`).
- Data exception after lock acquisition → rollback + 500 (`:710-720`).
- Accounting post fails AFTER commit → return durable, logged, outbox retries (`:674-682`). NOT a
  business failure of the return itself.

### Business Invariants
- I-1: A return can NEVER increase what the merchant owes the supplier — it only DEBITS the payable
  (`:614-615` `DebitAmount = TotalAmount`, `CreditAmount = 0`).
- I-2: Cumulative returned base qty per `(invoice,product,variant)` ≤ received base qty (`:402`).
- I-3: SupplierId+WarehouseId are immutable lineage, equal to the original invoice (`:270-278`).
- I-4: Stock never goes negative — `AvailableQuantity ≥ BaseQuantity` enforced before decrement
  (`:484-494`); WMA recompute guards `remainingStockQuantity > 0` (`:533`).
- I-5: The return is additive & immutable — no update/delete endpoint exists.

---

## 3. Runtime Collaboration (every participating runtime)

| Runtime | Role in Purchase Return | Evidence |
|---|---|---|
| **Procurement** | Owns the original `PurchaseInvoice`; is write-locked and has its `Status`/`AccountingMutationVersion` mutated. Does NOT own the return. | `:257`, `:582`, `:590-592` |
| **Inventory** | `ProductVariantStocks` decremented; `StockMovements` appended (`PurchaseReturn`); `Product.PurchasePrice` re-weighted; stock-change event queued. | `:542`, `:557-575`, `:533-539`, `:659` |
| **Accounting** | `CommerceAccountingService.PostPurchaseReturnAsync` posts the journal (debit SupplierPayables / credit Inventory). Runs AFTER data commit. | `:671`, `CommerceAccountingService.cs:722` |
| **Supplier Ledger** | `LedgerService.ApplySupplierTransaction` debits payable; writes `AccountLedger` + `Supplier.Balance` + `SupplierTransaction`. Runs INSIDE the data tx. | `:597-622`, `LedgerService.cs:57` |
| **Treasury** | NOT involved. No cash/card refund. (Contrast Sales Return.) | DTO `RefundToCash`/`RefundAmount` inert, see §6 |
| **Warehouse** | Validated (`IsActive`) and locked as the stock location; cannot be redirected. | `:287-292`, `:448` |
| **Catalog** | `Products`/`ProductVariants` locked for cost recompute (`LockPurchaseCostProductsAsync` `:1004`); `PurchasePrice` re-weighted. | `:446`, `:533-539` |
| **Analytics** | `ReportsController.GetPurchaseReturnsReport` reads Returns (consistent read tx). Mongo/ES projections fed by post-commit `StockSyncOutboxMessages`. | `:2027`, `:659` |
| **Outbox Worker** | `AccountingOutboxProcessor` consumes `PurchaseReturn` messages; posts journal or SKIPS stale version. | `AccountingOutboxProcessor.cs:726,1831` |

---

## 4. Execution Timeline (reverse-engineered, every step)

```
[HTTP] POST /api/PurchaseReturns  (Owner/Admin/Manager)  + Idempotency-Key header
  │
  ├─ Resolve idempotency context (header required, ≤200 chars) ............ :792
  ├─ Build request fingerprint (deterministic hash of payload) ........... :842
  ├─ Find existing idempotency row → replay/stored-response or 409 ....... :209, :783
  │
  ├─ BEGIN TX ............................................................. :231
  ├─ TryClaim idempotency (INSERT ... ON CONFLICT DO NOTHING) ............ :233, :734
  │
  ├─ Validate OriginalInvoiceId present .................................. :251
  ├─ Load original invoice FOR UPDATE (serializes concurrent returns) .... :257, :938
  ├─ Invoice not Cancelled/FullyReturned else 409 ........................ :264
  ├─ SupplierId+WarehouseId == invoice's else 400 ........................ :270
  ├─ Supplier exists ...................................................... :280
  ├─ Warehouse exists & IsActive .......................................... :287
  │
  ├─ For each requested line:
  │     product received on invoice? ...................................... :332
  │     resolve/validate variant (mandatory or single-distinct) ........... :341
  │     unit matches a received unit? ..................................... :379
  │     duplicate (Product,Variant)? ...................................... :373
  │     cumulative returned baseQty ≤ received baseQty? ................... :394
  │     compute authoritative subtotal/tax/unitprice/taxrate .............. :411-420
  │
  ├─ Lock products (FOR UPDATE) for cost recompute ....................... :446, :1004
  ├─ Lock variant stocks (deterministic order, CreateIfMissing) .......... :448
  │
  ├─ For each pending line:
  │     AvailableQuantity ≥ BaseQuantity else 400 ......................... :484
  │     build ReturnItem (CostPriceSnapshot/CostAmountSnapshot) ........... :500-518
  │     re-weight Product.PurchasePrice (WMA) ............................. :533-539
  │     decrement variantStock.Quantity ................................... :542
  │     queue ProductVariantStockChangedEvent ............................. :546
  │     append StockMovement (PurchaseReturn) ............................. :557
  │
  ├─ Sum header subtotal/tax/total (round 2dp) ............................ :578-580
  ├─ Invoice.AccountingMutationVersion++ ................................. :582
  ├─ Flip Invoice.Status Partially/FullyReturned ......................... :590
  ├─ Add Return + items ................................................... :594
  ├─ LedgerService.ApplySupplierTransaction (DEBIT payable) .............. :597
  ├─ Add SupplierTransaction ............................................. :607-622
  ├─ AccountingOutbox.EnqueuePurchaseReturnAsync ......................... :627
  ├─ SaveChanges + PublishCommittedStockEvents (StockSyncOutbox) ......... :658-665
  │
  ├─ COMMIT TX ............................................................ :667
  │
  ├─ (post-commit) PostPurchaseReturnAsync → journal ...................... :671
  │       success → MarkSynchronousOutboxProcessed (JournalId) ........... :688
  │       fail    → log; outbox worker retries (return already durable) ... :674
  │
  └─ return 200 + PurchaseReturnResponse
```

The outbox worker (separate process/transaction) later: loads the `PurchaseReturn` outbox message,
re-checks `invoice.AccountingMutationVersion == message.AggregateVersion`, and either SKIPs (stale) or
posts the journal and marks processed (`AccountingOutboxProcessor.cs:1831-1904`).

---

## 5. Critical Architecture Questions

### 5.1 Reverse Transaction? Compensation? Hybrid? Direct Mutation?

**Answer: COMPENSATION transaction (a fresh, balanced journal), with direct mutations to the
operational aggregates (stock, supplier balance, invoice status).**

- The accounting leg is compensation: `PostPurchaseReturnAsync` builds a NEW `PostingRequest`
  (SourceType `"PurchaseReturn"`, fresh idempotency key `PURCHASE_RETURN:{tenant}:{id}`) debiting
  `SupplierPayables` and crediting `Inventory` (`CommerceAccountingService.cs:737-757`). It does NOT
  call any `ReverseJournal`/`ReversePurchase` of the original purchase journal.
- The operational leg IS direct mutation: stock is decremented in place (`:542`), supplier balance is
  debited in place (`:597`), invoice status/version mutated in place (`:582`,`:590`). These are not
  "reverse" entries — they are the authoritative new state.
- Therefore the flow is a **hybrid**: direct-mutation operational side + compensation journal on the
  financial side. It is NOT a ledger reverse-transaction.

### 5.2 Does it modify the Purchase Invoice? Create a new document? Debit Note? Supplier Credit?

- **Modifies the Purchase Invoice**: YES, minimally and monotonically — `Status`
  (`PartiallyReturned`/`FullyReturned`) and `AccountingMutationVersion++` (`:582`,`:590`). No line
  items or amounts on the invoice are edited.
- **Creates a new document**: YES — a `Return` aggregate (`:594`) with `ReturnType=PurchaseReturn` and
  `OriginalPurchaseInvoiceId` lineage (`:310`).
- **Debit Note**: NO. No `DebitNote` entity/table/service/document-number exists. The supplier
  reduction is modeled purely as a ledger debit + accounting journal (§5.1).
- **Supplier Credit**: NOT in the "credit memo" sense. It is a **payable DEBIT** (reducing what is
  owed), via `SupplierTransaction.DebitAmount = TotalAmount` (`:614`). The word "credit" in accounting
  appears only as the Inventory side of the journal (credit Inventory). There is no supplier-credit
  document.

### 5.3 How is inventory cost handled?

**Weighted Moving Average (WMA) — recomputed forward, NOT restored to a prior snapshot.**

- Per line, the cost basis used is `BaseCost = subtotalPerBaseUnit` (net purchase unit price at return
  time, derived from the original invoice's net/total: `:411-414`). Note: this is the **net purchase
  price**, not the stored `Variant.CostPrice`.
- Re-weight formula (`:533-539`):
  ```
  remainingInventoryValue = (Product.PurchasePrice * totalStockQuantity) - (BaseCost * returnedBaseQty)
  if remainingStockQuantity > 0:
      Product.PurchasePrice = Max(0, remainingInventoryValue / remainingStockQuantity)
  ```
- The return's own immutable cost is frozen at `ReturnItem.CostPriceSnapshot` / `CostAmountSnapshot`
  (`:514-515`) — used for the return's own valuation, never to roll `Product.PurchasePrice` back.
- No FIFO layer; no snapshot-restore; no separate cost ledger. Cost lives on `Product.PurchasePrice`
  only (and `Variant.CostPrice`, which is NOT updated by purchase returns — see §14 gap).

---

## 6. Aggregate Analysis

| Aggregate | Mutation | Evidence |
|---|---|---|
| `Return` (PurchaseReturn) | **Created** (never updated/deleted) | `:594`, no PUT/DELETE endpoint |
| `ReturnItem` | **Created** | `:500` |
| `PurchaseInvoice` | `Status` set, `AccountingMutationVersion++` | `:582`,`:590` |
| `Product` | `PurchasePrice` re-weighted (WMA) | `:533-539` |
| `ProductVariantStock` | `Quantity -= BaseQuantity`, `UpdatedAt` | `:542-543` |
| `StockMovement` | **Created** (`PurchaseReturn`) | `:557` |
| `Supplier` | `Balance` reduced (debit) via ledger | `LedgerService.cs:442-445` |
| `AccountLedger` | **Created** (supplier debit) | `LedgerService.cs:220-238` |
| `SupplierTransaction` | **Created** (debit) | `:607-622` |
| `AccountingOutboxMessage` | **Created** (`PurchaseReturn`) | `:627` |
| `StockSyncOutboxMessage` | **Created** (post-commit) | `:659` |
| `PurchaseReturnRequestIdempotency` | **Created/Updated** (claim + complete) | `:734`, `:652` |

Note: `PurchaseReturnRequestIdempotency` is a request-level idempotency record, NOT a purchase-order
aggregate. Mutating it does not mutate business state.

---

## 7. Inventory Analysis

- **Warehouse validation**: warehouse must exist and `IsActive` (`:287-292`); the return's warehouse is
  forced to the invoice's warehouse (`:270-278`) — no redirection.
- **Variant validation**: `ProductVariantId` mandatory; if omitted, the product must have exactly one
  distinct received variant (`:341-360`); the resolved variant must have been received on the invoice
  (`:362-371`).
- **Quantity reduction**: `variantStock.Quantity -= line.BaseQuantity` (`:542`); in **base units**
  (`BaseQuantity = Quantity * ConversionFactor`, `:392`).
- **Cost recalculation**: WMA re-weight on `Product.PurchasePrice` (`:533-539`).
- **Inventory valuation**: the asset side is credited in the journal by `TotalAmount`
  (`CommerceAccountingService.cs:752-754`). The stock value on hand implicitly drops with the quantity.
- **Stock movement**: a `StockMovement` row with `MovementType = PurchaseReturn`,
  `QuantityChange = -BaseQuantity`, `QuantityAfter = variantStock.Quantity`, `Price` =
  unit price per base unit (`:557-574`).
- **Negative inventory protection**: `variantStock.AvailableQuantity < line.BaseQuantity` → 400
  (`:484-494`). `AvailableQuantity = Quantity − Reserved`. Researcher must have physical+available
  stock.
- **Warehouse ownership**: stock is tenant-scoped (`RequiredTenantId`) and locked by
  `(WarehouseId, VariantId)`; cross-warehouse routing is impossible because warehouse is pinned to the
  invoice.

---

## 8. Supplier Ledger Analysis

- **Supplier balance**: reduced by `ApplySupplierTransaction(supplier.Id, …, TransactionType.PurchaseReturn,
  debit=TotalAmount, credit=0, …)` (`:597-605`). Inside `LedgerService.ApplyPartyTransactionAsync` the
  new balance = `currentBalance + debit − credit` (`:218`), so debiting reduces the payable.
- **Supplier payable**: the amount owed the supplier drops by `TotalAmount`.
- **Debit note**: NONE (see §5.2).
- **Settlement**: unaffected by the return itself; future `SupplierPayment` will settle the now-lower
  payable (separate capability, `PostSupplierPaymentAsync`, `CommerceAccountingService.cs:369`).
- **Ledger entries**: one `AccountLedger` (debit SupplierPayables, `LedgerService.cs:220-238`) and one
  `SupplierTransaction` (debit, `:607-622`), both with `RunningBalance`/`BalanceAfter` tracked.
- **Running balance**: `AccountLedger.RunningBalance` is recomputed additively from prior ledger rows
  (in-flight ChangeTracker then DB, `LedgerService.cs:280-317`); `Supplier.Balance` is set to the new
  value (`:442-445`).
- **Outstanding payable**: reduced by `TotalAmount`; observable in `Suppliers.Balance`.

---

## 9. Financial Analysis

- **Cash refund / Card refund**: NONE. `RefundToCash`/`RefundAmount` on `CreatePurchaseReturnRequest`
  are inert (`:1096-1098`); the response sets `RefundAmount = TotalAmount` (`:648`) only as an echo —
  no treasury movement occurs.
- **Supplier credit / payable reduction**: the only financial effect on the supplier side is a payable
  DEBIT (§8).
- **Debit note**: NONE.
- **Treasury / Cashbox / Bank**: NOT touched. Contrast with purchase (cash/card split posts to cashbox/
  Visa) and with sales return (cash/card refund). Purchase return leaves treasury untouched.
- **Accounting journals**: ONE journal (on the post-commit path) — `PostPurchaseReturnAsync`:
  - DEBIT `SupplierPayables` = `Return.TotalAmount`
  - CREDIT `Inventory` = `Return.TotalAmount`
  (`CommerceAccountingService.cs:746-756`).
- **VAT / Tax / Discount**: tax is carried per return line (`TaxAmount`, `TaxRate`) and rolled into
  `TotalAmount`; the journal posts the **gross** `TotalAmount` (tax-inclusive), not a net split. There
  is NO separate VAT journal line and NO discount modeling — the report hardcodes `DiscountReversed = 0`
  (`:2128`) and `NetReturnAmount == GrossReturnAmount` (`:2074`,`:2129`).

---

## 10. SQL Transaction Analysis

- **Transaction boundary**: ONE explicit transaction wraps validation → stock decrement → cost reweight
  → ledger debit → outbox enqueue → `SaveChanges` → stock-sync enqueue → commit
  (`:231` begin, `:667` commit). The accounting journal post is OUTSIDE this transaction (`:671`).
- **Isolation level**: the controller does not set an explicit isolation level, so it inherits the
  provider default (ReadCommitted for SQL Server / Postgres). Locking is achieved explicitly via
  `FOR UPDATE` (Postgres) / `UPDLOCK,ROWLOCK,HOLDLOCK` (SQL Server) hints on the invoice and product
  rows (`:942-963`, `:1010-1041`).
- **Lock ordering**: (1) idempotency claim row (unique `TenantId,Key`); (2) original invoice `FOR
  UPDATE`; (3) products `FOR UPDATE` in `ORDER BY id` (`:1004-1006` — deterministic order avoids
  deadlock); (4) variant stocks via `LockMultipleStocksAsync` (deterministic `(WarehouseId,VariantId)`
  ordering). All ordering is deterministic → no classic AB/BA deadlock between returns.
- **Rollback**: any pre-commit validation or lock failure calls `transaction.RollbackAsync()` then
  returns an error (every branch `:253`,`:260`,`:266`,`:274`,`:283`,`:291`,`:328`,`:337`,`:355`,`:369`,
  `:376`,`:385`,`:404`,`:487`). The outer catch rolls back if `!transactionCommitted` and clears the
  ChangeTracker (`:710-716`).
- **Recovery**: post-commit, the accounting post is best-effort; failure is logged and recovered by the
  outbox worker (`:674-682`). The outbox row is transactional with the business data (enqueue inside the
  tx, `AccountingOutboxService.cs:749` "Save immediately while the caller's explicit transaction is
  active").
- **Atomic guarantees**: within the tx, all operational + supplier-ledger + outbox writes commit or roll
  back together. The accounting journal is NOT part of that atomic guarantee by design (dual-write with
  outbox recovery). This is the deliberate "fail-open on accounting, fail-closed on data" posture (§0
  fact 8).

---

## 11. Event Analysis

- **Published events**: `ProductVariantStockChangedEvent` — created post-commit and written to
  `StockSyncOutboxMessages` (`:659-665`, `ProductVariantStockMutationSupport.cs:216-248`).
- **Accounting outbox**: `AccountingOutboxMessage` event `"PurchaseReturn"`, key
  `PURCHASE_RETURN:{tenant}:{returnId}`, payload `{ReturnId, OriginalInvoiceId, MutationVersion}`,
  `AggregateVersion = invoice.AccountingMutationVersion`
  (`AccountingOutboxService.cs:221-239`).
- **Stock synchronization**: `StockSyncOutboxMessages` row carries the serialized
  `ProductVariantStockChangedEvent`; a downstream worker/projector syncs Mongo/ES. `PublishedAtUtc` stays
  NULL until the post-commit publish (proven by concurrency test: uncommitted returns never reach
  projections).
- **Analytics**: `ReportsController.GetPurchaseReturnsReport` reads `Returns`/`ReturnItems` directly
  (consistent read tx, `:2033`). Mongo/ES projections feed the catalog/warehouse views.
- **Mongo projections / Inbox**: standard CQRS outbox pattern; no inbound "inbox" for purchase returns
  specifically — the `StockSyncOutboxMessages` is the outbound projection buffer.

---

## 12. Cost Lifecycle Analysis (mandatory)

**How a Purchase Return affects Weighted Moving Average, `Product.PurchasePrice`, future sales cost,
future returns, and inventory valuation — implementation only.**

1. **WMA math** (`:411-414`, `:533-539`): the return removes `returnedBaseQty` units at the net purchase
   cost `BaseCost` from the pool. New `PurchasePrice = Max(0, (oldPP * totalStock − BaseCost *
   returnedBaseQty) / (totalStock − returnedBaseQty))`. This is a true WMA continuation — the return is
   treated as consuming units at the *current* average cost, not at the original line cost.
   - Subtlety: `BaseCost` is computed from the **original invoice's net unit price**
     (`(Σ(Total−Tax))/receivedBaseQty`, `:411-414`), which is the blended purchase price, not the
     live `Product.PurchasePrice`. So the backing-out cost and the pool cost can differ slightly; the
     formula still yields a forward WMA.
2. **`Product.PurchasePrice`**: mutated in place, forward-only. Never "restored" to a pre-return value.
3. **Future sales cost (COGS)**: future sales use the *new* lower (or unchanged) `PurchasePrice` for
   COGS, so returning high-cost units rationally pulls the average down if `BaseCost` < current PP, or
   up if `BaseCost` > current PP.
4. **Future returns**: a subsequent return re-applies the same WMA formula against the then-current
   `PurchasePrice` (partial returns supported, `:590` PartiallyReturned).
5. **Inventory valuation**: the stock quantity drops; the `Inventory` account is credited by `TotalAmount`
   in the journal (`:752-754`). The book value of remaining inventory = `PurchasePrice × remainingQty`
   (post-reweight).
6. **`Variant.CostPrice` is NOT updated** by purchase returns (only on product/variant edit,
   `ProductVariantService.cs`). For variant products, `Variant.CostPrice` can drift from `PurchasePrice`
   after a return — see §14 cross-capability gap.

---

## 13. Business Rules (implementation-enforced)

| # | Rule | Evidence | Failure |
|---|---|---|---|
| BR1 | Idempotency-Key (or X-Request-Id) required, ≤200 | `:792-834` | 400 |
| BR2 | Idempotency-Key == X-Request-Id when both present | `:798-806` | 400 |
| BR3 | OriginalInvoiceId required | `:251-255` | 400 |
| BR4 | Invoice exists & not deleted | `:257-262` | 400 |
| BR5 | Invoice not Cancelled/FullyReturned | `:264-268` | 409 |
| BR6 | SupplierId+WarehouseId == invoice's | `:270-278` | 400 |
| BR7 | Supplier row exists | `:280-285` | 400 |
| BR8 | Warehouse exists & IsActive | `:287-292` | 400 |
| BR9 | Product was received on invoice | `:332-339` | 400 |
| BR10 | Variant received on invoice (or single-distinct) | `:341-371` | 400 |
| BR11 | Return unit matches a received unit | `:379-386` | 400 |
| BR12 | No duplicate (Product,Variant) line | `:373-377` | 400 |
| BR13 | Cumulative returned baseQty ≤ received baseQty | `:394-409` | 400 "Cannot return more than received quantity" |
| BR14 | Available stock ≥ returned baseQty (reserved counts) | `:484-494` | 400 "Insufficient stock … Available: X, Requested: Y" |
| BR15 | Mutually exclusive idempotency key w/ different fingerprint | `:212-218`, `:758-764` | 409 |
| BR16 | Idempotency key reused w/ same fingerprint & completed → replay stored response | `:220-223`, `:766-769` | 200 (replay) |
| BR17 | Amounts rounded 2dp (AwayFromZero); rate 6dp | `:1050-1054` | — |
| BR18 | TotalAmount = Subtotal + TaxAmount (authoritative) | `:578-580` | — |
| BR19 | Return immutable (no PUT/DELETE) | controller has only GET/POST | — |
| BR20 | `RefundToCash`/`RefundAmount` ignored | `:1096-1098` unused in flow | — |

---

## 14. Failure Analysis

- **Duplicate return** (same key, different fingerprint) → 409 (`:212-218`). Same key+same fingerprint
  and completed → replay 200 (`:220-223`). Same key in-flight → 409 "already being processed"
  (`:225-228`, `:771`).
- **Accounting failure** (post-commit journal post throws): return already committed & durable; error
  logged; outbox worker retries (`:674-682`). The return is NOT rolled back.
- **Supplier failure** (supplier row missing): caught pre-commit, rollback, 400 (`:280-285`). If
  `LedgerService` throws mid-tx → `ApplyPartyTransactionAsync` rolls back its own inner tx and
  rethrows; outer catch rolls back (`:268-277`, `:710-716`).
- **Inventory failure** (insufficient available stock): pre-commit validation, rollback, 400
  (`:484-494`).
- **Network failure**: the data tx either commits or rolls back atomically. If the client disconnects
  after commit but before the response, idempotency replay (BR16) returns the stored 200 on retry.
- **Rollback / Recovery**: outbox row is written inside the data tx, so a rolled-back return has NO
  orphan outbox row. A committed return with a failed accounting post is healed by the worker.
- **Concurrency**: the original invoice is `FOR UPDATE`/`UPDLOCK` for the whole tx (`:938-988`), so two
  concurrent returns against the same invoice serialize; the second re-validates cumulative returned
  qty against committed history (`:394-399`), preventing over-return. Deterministic lock ordering
  (`:1004`, `LockMultipleStocksAsync`) prevents deadlock.

---

## 15. Performance Analysis

- **Round trips**: within the tx the controller issues multiple queries: invoice load (1), per-line
  product load (N), per-line prior-return aggregation (N SUMs), product-stock totals (1 groupby),
  variant/product dictionary loads (2), stock lock (1), ledger post (1), outbox enqueue (1). For a
  return with K lines this is ~`2N + 6` queries. Acceptable for typical K but not bulk-optimized.
- **Locks**: invoice row locked for the tx duration; K product rows locked; K variant-stock rows locked.
  Hot-path contention occurs when many returns hit the same invoice simultaneously — serialized by the
  invoice lock (intended).
- **Hot paths**: `LoadOriginalPurchaseInvoiceForReturnMutationAsync` (invoice `FOR UPDATE`) is the
  serialization point. The supplier-ledger post (`ApplyPartyTransactionAsync`) also locks the supplier
  row (`LedgerService.cs:463`).
- **Scalability**: returns are tenant-scoped and invoice-scoped; cross-tenant/cross-invoice returns do
  not contend. The accounting post is async/offloaded to the worker, keeping the request path short.
- **Deferred work**: accounting journal post (can be worker-retried), Mongo/ES stock sync (worker),
  report aggregation (on demand). All deferred work is outbox-driven and idempotent.

---

## 16. Security Analysis

- **Permissions**: `POST` requires `Owner,Admin,Manager` (`:198`); `GET` requires only `[Authorize]`
  (`:26`, `:63`, `:133`, `:162`). No self-service supplier access.
- **Tenant isolation**: every aggregate carries `TenantId`; queries filter by
  `_tenantContext.RequiredTenantId`; `X-Tenant-Id` is stripped and `RequiredTenantId` enforced. The
  idempotency key is scoped `SUPPLIER:{tenant}:{key}` (`:839-840`) so a key in tenant A cannot collide
  with tenant B. Stock locks are tenant-scoped (`:448-451`).
- **Supplier ownership**: the return's supplier is taken from the invoice (`:312`), not the request; the
  request `SupplierId` must match (`:270`) — a caller cannot return against a supplier they don't own
  the invoice for. Supplier row existence is re-checked (`:280`).
- **Warehouse ownership**: warehouse pinned to the invoice (`:270-278`, `:313`); cannot redirect stock to
  another warehouse.
- **Fraud prevention**: BR13/BR14 prevent returning more than received or more than in stock; BR6
  prevents misattributing the return; the immutable, additive `Return` + `StockMovement` + ledger trail
  makes tampering detectable.
- **Replay prevention**: mandatory idempotency key + fingerprint; duplicate-with-different-fingerprint →
  409 (BR15). The outbox `PURCHASE_RETURN:{tenant}:{id}` key prevents double journal posting; the worker
  SKIPs stale mutation versions (§0 fact 13).
- **Idempotency**: dual layer — (a) request idempotency (`PurchaseReturnRequestIdempotency`) returns the
  stored 200 on replay; (b) accounting idempotency via outbox key + `AggregateVersion` skip.

---

## 17. Hermes Browser Test Specification (deterministic)

### Preconditions
- Tenant T with `Owner` role session.
- Supplier S (active) and Warehouse W (IsActive).
- Purchase invoice INV (Status `Completed`) with one line: Product P, Variant V, Unit U,
  `Quantity=10`, `ConversionFactor=1`, net unit `Total−Tax = 100`, `Tax = 14`, so received base qty = 10.
- `Products.PurchasePrice(P) = 100` initially; `ProductVariantStocks(W,V).Quantity = 50`,
  `Reserved = 0` (Available = 50).
- `Suppliers.Balance(S) = 1000` (payable owed).

### User Journey
1. `GET /api/PurchaseReturns/invoice/{INV}` → 200, returns INV detail.
2. `POST /api/PurchaseReturns` with header `Idempotency-Key: k1`, body
   `{ SupplierId:S, WarehouseId:W, OriginalInvoiceId:INV, ReturnDate:now, Items:[{ProductId:P,
   ProductVariantId:V, UnitId:U, Quantity:4, UnitPrice:100, TaxRate:14}], Reason:"defective" }`
   → 200, `ReturnNumber` allocated.

### Expected Runtime State (post-commit)
- Return created with `ReturnType=PurchaseReturn`, `OriginalPurchaseInvoiceId=INV`, `TotalAmount=456`
  (4×100 + 4×14).
- `PurchaseInvoice.Status = PartiallyReturned` (4 < 10 returned), `AccountingMutationVersion = 1`.
- Outbox `AccountingOutboxMessage` event `PurchaseReturn`, key `PURCHASE_RETURN:T:{returnId}`,
  `AggregateVersion=1`.
- Accounting journal posted: DEBIT SupplierPayables 456 / CREDIT Inventory 456 (sync or via worker).

### Expected Database State
- `Returns`: 1 row; `ReturnItems`: 1 row with `BaseQuantity=4`, `CostPriceSnapshot=100`,
  `CostAmountSnapshot=400`.
- `ProductVariantStocks(W,V).Quantity = 46`; `StockMovements`: 1 row `PurchaseReturn`,
  `QuantityChange=-4`, `QuantityAfter=46`.
- `Suppliers.Balance(S) = 1000 − 456 = 544`; `AccountLedgers`: 1 debit row (SupplierPayables) running
  balance 544; `SupplierTransactions`: 1 debit row `BalanceAfter=544`.
- `Products.PurchasePrice(P)`: remaining value = `100×50 − 100×4 = 4600`; remaining stock = 46;
  `PurchasePrice = 4600/46 ≈ 100` (unchanged in this symmetric case).

### Expected Inventory State
- WMA recomputed forward; stock reduced by 4 base units in warehouse W, variant V only. No other
  warehouse/variant touched.

### Expected Supplier Ledger
- Payable reduced by 456; running balance 544; one debit `SupplierTransaction`.

### Expected Accounting State
- One journal: DEBIT SupplierPayables 456, CREDIT Inventory 456. No cash/card movement.

### Negative Tests
- N1: No `Idempotency-Key` → 400.
- N2: `OriginalInvoiceId` missing → 400.
- N3: Return `Quantity=20` (> received 10) → 400 "Cannot return more than received quantity".
- N4: `WarehouseId` different from INV's → 400 "Supplier and warehouse must match…".
- N5: Return against `Cancelled` invoice → 409 "cannot accept another return".
- N6: Stock only 3 available (set `Quantity=3`) → 400 "Insufficient stock … Available: 3, Requested: 4".
- N7: Re-POST same `Idempotency-Key: k1` + same body → 200 replay (stored response).
- N8: Re-POST `k1` with different `Quantity` → 409 "Idempotency-Key was already used for a different
  purchase return request."
- N9: `Role=Staff` (no Owner/Admin/Manager) → 403.

### Assertions
- After N7, exactly ONE `Return` row exists for `k1` (no duplicate document).
- `Suppliers.Balance` monotonic-decreasing per return; never increases.
- `Products.PurchasePrice` after any sequence of returns + new purchases equals the live WMA
  (recompute from `StockMovements` if needed).
- Outbox `PurchaseReturn` message processed exactly once (no duplicate journal) — verified by
  `AggregateVersion` skip logic.

---

## 18. Runtime Collaboration Summary

- **Procurement** supplies the locked, authoritative invoice and receives a status/version bump — it is
  the *source* document, not the owner of the return.
- **Inventory** is the most mutated runtime (stock −, WMA reweight, movement row, sync event).
- **Supplier Ledger** is mutated *inside* the data tx (debit payable) — it is on the atomic path.
- **Accounting** is *outside* the data tx (post-commit journal), recovered by the outbox worker.
- **Warehouse** is validated and pinned; **Catalog** is locked for cost recompute.
- **Treasury** is explicitly NOT involved (no refund).
- **Analytics** reads committed Returns (consistent read) and consumes Mongo/ES projections fed by the
  post-commit stock-sync outbox.
- **Outbox Worker** is the recovery/guarantee runtime for the financial leg and the projection sync.

The defining boundary: **operational + supplier-ledger = one atomic tx; accounting + projections =
deferred, outbox-guaranteed, fail-open.**

---

## 19. Return Symmetry Analysis (Purchase Invoice ↓ Purchase Return)

| Aspect | Purchase Invoice (`PostPosPurchaseAsync`) | Purchase Return (`PostPurchaseReturnAsync`) |
|---|---|---|
| Stock | `+Quantity` (receive) | `−Quantity` (send back) |
| Cost | sets/raises `PurchasePrice` (WMA in) | re-weights `PurchasePrice` (WMA out) |
| Supplier ledger | CREDIT payable (or cash/card split) | DEBIT payable |
| Accounting journal | DEBIT Inventory / CREDIT SupplierPayables (+Cash/Card) | DEBIT SupplierPayables / CREDIT Inventory |
| Outbox event | `PosPurchase` | `PurchaseReturn` |
| New document? | `PurchaseInvoice` | `Return` |
| Idempotency/version | `PURCHASE:{tenant}:{invId}`; invoice version bumped on post | `PURCHASE_RETURN:{tenant}:{returnId}`; invoice `AccountingMutationVersion++` per return |
| Treasury | Cash/Card may be used (split) | NEVER |
| Shared services | `CommerceAccountingService`, `AccountingOutbox`, `LedgerService` | same |
| Different aggregates | mutates `PurchaseInvoice` | mutates `Return` + `PurchaseInvoice` (status only) |

**Exact reversals**: the journal is the accounting mirror image (Inventory/Cash ↔ SupplierPayables swap
sides, but Inventory is always the inventory leg). **Compensation behavior**: the return does NOT undo
the purchase journal; it posts an independent offsetting journal. **Cost differences**: purchase sets the
average; return continues the average (no restore). **Supplier-ledger differences**: purchase creates the
liability; return reduces it. **Inventory differences**: opposite sign on quantity.

---

## 20. Canonical Pattern (concise)

| Concern | Strategy |
|---|---|
| **Document Strategy** | New additive `Return` (immutable, no PUT/DELETE); original `PurchaseInvoice` only gets `Status` + `AccountingMutationVersion`. |
| **Inventory Strategy** | Direct variant-stock decrement in base units; deterministic `(Warehouse,Wariant)` lock; WMA reweight of `Product.PurchasePrice`; `StockMovement` audit row; post-commit sync event. |
| **Cost Strategy** | Forward WMA (recompute), frozen cost snapshot on `ReturnItem`; no restore, no FIFO. |
| **Supplier Strategy** | Payable DEBIT via `LedgerService` inside the data tx; no debit-note document; warehouse/supplier pinned to invoice. |
| **Accounting Strategy** | Compensation journal (DEBIT SupplierPayables / CREDIT Inventory) posted AFTER commit; outbox-guaranteed; idempotent by `PURCHASE_RETURN:{tenant}:{id}`. |
| **Recovery Strategy** | Dual-write outbox inside the data tx + worker retry; stale-version SKIP; fail-open on accounting, fail-closed on data. |
| **Concurrency Strategy** | Invoice `FOR UPDATE`/`UPDLOCK` serialization + deterministic product/stock lock ordering; cumulative-returned revalidation prevents over-return. |
| **Pricing Authority** | `Product.PurchasePrice` is the WMA authority; `Variant.CostPrice` is NOT updated by returns (drift gap, §14). |
| **Transaction Strategy** | One atomic tx for operational+ledger+outbox; accounting deferred. No explicit isolation level (provider default + explicit row locks). |
| **Idempotency Strategy** | Mandatory request idempotency (`PurchaseReturnRequestIdempotency`, fingerprint+stored response) AND accounting outbox idempotency (`AggregateVersion` skip). |

---

## 21. Documentation Validation (correct misleading docs)

The following documented/assumed statements are WRONG against implementation:

1. ❌ "Purchase Return issues a Debit Note / Supplier Credit memo." → No `DebitNote` entity exists;
   only a payable DEBIT + journal (§5.2, §8, §9).
2. ❌ "Purchase Return has a status/state machine (Pending/Approved/Rejected)." → `Return` has NO status
   column; state = invoice `Status` + outbox processed (§0 fact 7, `Return.cs`).
3. ❌ "Purchase Return refunds cash/card to the supplier." → `RefundToCash`/`RefundAmount` are inert;
   treasury untouched (§0 fact 12, §9).
4. ❌ "There is a Purchase Return Service / application service." → The whole capability lives in
   `PurchaseReturnsController.cs`; no separate service (skill header, discovery §1).
5. ❌ "Returning goods restores the previous Weighted Moving Average." → WMA is recomputed forward; the
   prior average is NOT restored (§5.3, §12).
6. ❌ "CommerceHub vendor returns are purchase returns." → Different capability
   (`PosAdminReturnsController`, `CommerceHubReturnStatus`); do not conflate (§0 fact 15).
7. ❌ "The purchase returns report's `CostReversed` reflects the return's recorded cost." →
   `GetPurchaseReturnsReport` computes `CostReversed = item.BaseQuantity * item.Product.PurchasePrice`
   using the LIVE product price (`ReportsController.cs:2075`), NOT the immutable
   `ReturnItem.CostAmountSnapshot` (`:515`). After later purchases change `PurchasePrice`, the report
   DIVERGES from the authoritative return snapshot. Authoritative cost = `ReturnItem.CostAmountSnapshot`.
8. ❌ "Purchase returns model discounts." → `DiscountReversed` hardcoded 0; `NetReturnAmount ==
   GrossReturnAmount` (`:2074`,`:2128-2129`).
9. ❌ "The return modifies the original purchase invoice lines/amounts." → Only `Status` and
   `AccountingMutationVersion` change (`:582`,`:590`).

---

## 22. Final Architecture Question — answered with evidence

**How does Shopixy return inventory to suppliers while preserving inventory consistency, supplier
balance correctness, cost correctness, accounting integrity, recovery, auditability, runtime boundaries,
and scalability?**

1. **Inventory consistency** — Stock is decremented in base units inside one atomic tx after a
   deterministic `(Warehouse,Variant)` lock; `AvailableQuantity ≥ BaseQuantity` is enforced before the
   decrement (`:448`,`:484-494`,`:542`). The warehouse/variant is pinned to the original invoice
   (`:270-278`), so stock always leaves the exact location it entered. A `StockMovement` row records the
   immutable delta (`:557`).
2. **Supplier balance correctness** — The payable is reduced by a DEBIT inside the same tx via
   `LedgerService.ApplySupplierTransaction`, which locks the supplier row and recomputes
   `Supplier.Balance` additively from the ledger (`:597`, `LedgerService.cs:215,218,442-445`). BR13/BR14
   guarantee we never return more than received/in stock, so the payable can never be over-reduced
   relative to goods.
3. **Cost correctness** — `Product.PurchasePrice` is re-weighted by forward WMA using the return's cost
   basis, never restored (`:533-539`, §12). The return's own cost is frozen immutably in
   `ReturnItem.CostAmountSnapshot` (`:514-515`) for its own valuation and audit.
4. **Accounting integrity** — A balanced compensation journal (DEBIT SupplierPayables / CREDIT Inventory)
   is posted with a unique idempotency key `PURCHASE_RETURN:{tenant}:{id}`
   (`CommerceAccountingService.cs:745-756`). The original purchase journal is never mutated (§5.1).
5. **Recovery** — The accounting post is deferred and outbox-guaranteed: the outbox row is written
   atomically with the business data (`:627`, `AccountingOutboxService.cs:749`); if the synchronous post
   fails it is retried by the worker (`:674-682`); a *stale* mutation version causes a SKIP, never a
   double post (`AccountingOutboxProcessor.cs:1859-1869`). Fail-open on accounting, fail-closed on data.
6. **Auditability** — The `Return` is additive and immutable (no update/delete); every mutation leaves a
   trail: `Return`+`ReturnItem`, `StockMovement`, `AccountLedger`, `SupplierTransaction`, outbox message,
   and stock-sync message. Ledger-only services are *forbidden* from reading the mutable business tables
   (`ForbiddenBusinessDbSets.Names` includes `Returns`/`ReturnItems`/`SupplierTransactions`), enforcing
   that financial truth comes from the immutable ledger.
7. **Runtime boundaries** — Operational + supplier-ledger + outbox = one atomic tx; accounting journal +
   Mongo/ES projection sync = post-commit, outbox-driven. Treasury is explicitly out of scope (no refund).
   This cleanly separates the "must be consistent now" path from the "eventually consistent" path.
8. **Scalability** — Returns are tenant- and invoice-scoped; the invoice `FOR UPDATE` lock serializes
   only same-invoice concurrency (intended), while deterministic lock ordering prevents deadlock
   (`:1004`, `LockMultipleStocksAsync`). The heavy accounting/projection work is offloaded to background
   workers, keeping the request path short and horizontally scalable.

Every statement above is cited to implementation; where a doc/comment/UI label disagrees, implementation
wins (see §21).
