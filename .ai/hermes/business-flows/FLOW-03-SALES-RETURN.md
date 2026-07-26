# FLOW-03 — Sales Return Business Flow: Implementation-Backed Canonical Architecture

> Authority rule: Implementation is the ONLY source of truth. Every claim below is cited to
> `file:line` in the live backend. Controllers, DTOs, comments, and naming are evidence only — where
> they contradict runtime behavior, behavior wins. This document describes ONE complete business
> transaction: the POS **sales return** (a customer/merchant returning goods previously sold via a
> completed sales invoice), executed by
> `PointOfSale.API/Controllers/SalesReturnsController.cs` `CreateSalesReturn` (line 202).
>
> Verification note: all line numbers below were confirmed by reading the live backend on the
> `feature/new-pr210` branch. Repo root: `C:\Users\fadya\source\repos\PointOfSale`.

---

## 0. Executive Summary

### What is a Sales Return?

A **Sales Return** is the POS operation of taking goods back from a customer against a previously
**completed** sales invoice, restoring them to warehouse stock, reversing the customer's receivable
balance (or paying the customer out), and posting the financial reversal journals. In the data model
it is a `Return` aggregate with `ReturnType = SalesReturn` (`Return.cs:20`, `ReturnType.cs:5-6`),
containing `ReturnItem` lines (`ReturnItem.cs`).

It is NOT a cancellation/void of the original sale. The original `SalesInvoice` is left intact and
immutable except for a monotonic `AccountingMutationVersion` counter (see §8). The return is a
**separate, additive** document (`SalesReturnsController.cs:866` `_context.Returns.Add(salesReturn)`).

### Why does it exist?

To reverse the economic effect of a completed sale while preserving auditability: the original sale
must remain the historical record, and the reversal must be independently traceable. Shopixy
therefore models the return as a **new authoritative document** that *compensates* the sale's
inventory, financial, and ledger effects — not a destructive edit of the sale.

### How is it implemented? (the 14 runtime facts)

1. **Two-phase commit, not one.** The return document + stock + ledger are committed synchronously
   inside ONE SQL transaction (`SalesReturnsController.cs:246` `BeginTransactionAsync`, commit at
   `:977`). The **accounting journals** are posted in a SEPARATE transaction — synchronously attempted
   after commit (`:979` `TryCompleteSynchronousRefundAccountingAsync`), with the **outbox** as the
   recovery path. This is the defining architectural fact of the flow.
2. **The return is a COMPENSATION transaction, not a reverse-transaction.** Evidence: accounting uses
   `PostPosRefundAsync` → `PostJournalAsync` (`CommerceAccountingService.cs:561,688`) — a *fresh,
   balanced* journal keyed by idempotency `REFUND:{tenant}:{returnId}` (`:684`), NOT
   `ReverseJournalAsync`. The original sale journal is never mutated. See §7 (Critical Architecture
   Question).
3. **Idempotency is mandatory at the boundary.** The request SHOULD carry `Idempotency-Key` (or
   `X-Request-Id`); if absent, an `AUTO:`-prefixed deterministic hash is generated from the payload
   (`SalesReturnsController.cs:292-302`, `BuildSalesReturnRequestFingerprint` `:1459`). The key is
   claimed via an `INSERT ... ON CONFLICT DO NOTHING` row in `SalesRequestIdempotency`
   (`:1013-1018`) and the byte-exact response is stored (`:971-976`).
4. **The original invoice row is write-locked for the whole return tx** (`FOR UPDATE` /
   `UPDLOCK,ROWLOCK,HOLDLOCK`, `LoadOriginalInvoiceForReturnMutationAsync` `:1107-1144`). This is the
   SR-CON-01 fence: competing returns against the same invoice revalidate against committed history
   instead of a stale pre-return snapshot (`:423-426`).
5. **Stock is RESTORED to the same variant in the SAME warehouse** the return is posted to. Restored
   via `variantStock.Quantity += line.BaseQuantity` (`:758-761`) AFTER acquiring a deterministic
   `(WarehouseId, VariantId)` lock (`LockMultipleStocksAsync` `:683`, locking impl
   `ProductVariantStockMutationSupport.cs:292`). No different-warehouse or different-variant routing.
6. **Negative inventory is impossible.** Stock restore only *increases* quantity, and
   `ValidateQuantityChange` only guards decreases (`:364-386`); the restore path never triggers a
   negative guard. There is no `allowNegativeStock` risk on returns.
7. **Client pricing is IGNORED — server recomputes.** Any `UnitPrice`/`TaxRate` the client sends that
   differs from the original-invoice snapshot is dropped and logged
   (`:534-559`, `ignoredClientOverrides`). The refund total is derived entirely from the original
   `SalesInvoice` line snapshots — **price-tampering prevention**.
8. **Cannot return more than sold.** Per-line guard:
   `priorBaseQuantity + alreadyRequestedBaseQuantity + currentBaseQuantity > snapshot.OriginalBaseQuantity`
   → 400 (`:524-532`). Whole-invoice guard:
   `priorReturnedTotal + salesReturn.TotalAmount > originalInvoiceTotal` → 400 (`:804-810`). Both are
   computed from committed `ReturnItems` (`ReturnItems` query `:472-478`), revalidated under the
   invoice lock.
9. **Only `Completed` invoices can be returned** (`:438-441`), and the `CustomerId` must match the
   original invoice's (`OriginalInvoiceId` required `:408-411`, customer match `:443-446`).
10. **Refund money is governed by `RefundMethod` ∈ {Cash, Card, Mixed, AccountCredit}**
    (`RefundMethod.cs:3-9`). Cash/Card/Mixed must allocate the **full** return total to payout
    (`:844-850`); `AccountCredit` leaves the whole amount as customer credit (`creditToCustomerAmount =
    salesReturn.TotalAmount`, `:840-843`).
11. **Ledger is the authority, not a cashbox entity.** Customer balance mutation flows through
    `_ledgerService.CreateLedgerEntry` (`PartyType.Customer`, `TransactionType.SalesReturn`, debit 0 /
    credit `creditToCustomerAmount`, `:870-879`), which locks the party row
    (`LedgerService.cs:215` `LockPartyForUpdateAsync`) and writes the new `RunningBalance`
    (`:218-239`). `CustomerTransaction` rows are appended for history only, capturing
    `BalanceAfter` (`:881-896`).
12. **Accounting journals are two balanced postings** (when funded by cash/card): (a) Revenue reversal
    — Debit `PosSales`, Credit `Cashbox`/`VisaClearing`/`AccountsReceivable`
    (`CommerceAccountingService.cs:644-686`); (b) Optional COGS reversal — Debit `Inventory`, Credit
    `CostOfGoodsSold`, only if `returnCost > 0` (`:690-711`, `CalculateReturnCost` `:1468`). Both are
    balanced by `FinancialPostingService` (`PostJournalAsync`, idempotent, Serializable).
13. **Outbox row is staged INSIDE the return tx, then the worker is the recovery path.** The
    `AccountingOutboxMessage` (`EventType = "PosRefund"`) is enqueued at `:899-906` (persisted inside
    the same tx via `EnqueueMessageAsync` → `SaveChangesAsync` `AccountingOutboxService.cs:747-752`)
    and marked processed synchronously if the post succeeds (`:1608-1606` /
    `TryCompleteSynchronousRefundAccountingAsync` `:1608`). If the synchronous post fails, the row is
    left pending and the background `AccountingOutboxProcessor` retries (`PosRefund` is a
    *critical financial truth* event, `AccountingOutboxProcessor.cs:59`).
14. **No DELETE / CANCEL endpoint exists.** The controller only exposes `GET` list/get, `GET invoice`,
    and `POST` create (`SalesReturnsController.cs:72,143,167,201`). A return cannot be voided through
    the API — corrections are made by creating another return or a forward adjustment. (Searched:
    no `HttpDelete`/`CancelSalesReturn`/`DeleteSalesReturn` present.)

---

## 1. Business Goal (implementation-derived)

- **Business Purpose:** Take goods back against a completed sale, restore warehouse stock, correct the
  customer's receivable/credit balance, pay out cash/card if requested, and post balanced reversal
  journals — atomically for the document/stock/ledger, and durably for the accounting.
- **Business Actor:** A tenant operator with a POS-operator JWT (`[Authorize]`, controller `:28`). Not a
  customer. The customer is referenced by `CustomerId` for balance purposes only.
- **Business Preconditions:**
  - A valid idempotency context (`Idempotency-Key`/`X-Request-Id` or auto-hash) — `:279-302`.
  - `OriginalInvoiceId` present (`:408`) and resolves to a `SalesInvoice` (`:428`).
  - Original invoice `Status == Completed` (`:438`).
  - `request.WarehouseId` resolves to an **active, tenant-owned** warehouse
    (`LoadActiveWarehouseAsync` `:1098-1105` — `TenantId == RequiredTenantId && IsActive && !IsDeleted`).
  - `CustomerId` matches the original invoice's `CustomerId` (`:443`).
  - At least one return item with `Quantity > 0` (`:499-501`, `:574-577`).
  - Per-line return qty ≤ remaining sold qty (`:524-532`); total refund ≤ original invoice total
    (`:804-810`).
  - If payout is partial/zero and `creditToCustomerAmount > 0`, a Customer must exist (`:852-858`).
- **Business Trigger:** HTTP `POST /api/salesreturns` from the POS client
  (`PointOfSale.Web/src/pages/SalesReturns.jsx`, `ReturnModal.jsx`).
- **Business Completion Conditions:** The return transaction commits (`:977`) and the client receives
  `200 OK` with `SalesReturnResponse` (ReturnId, ReturnNumber, TotalAmount, RefundAmount, RefundMethod,
  CashAmount, CardAmount, CustomerCreditAmount) at `:957-996`.
- **Business Failure Conditions:** Any `BadRequest`/`Conflict`/`500` returned before commit, or the
  catch-all rollback at `:614-627`. Every pre-commit validation returns `BadRequest` with a message; a
  divergent idempotency payload → `Conflict` (`:227-233`). Idempotency key reused with same payload →
  stored `200` response replayed (`:235-238`).
- **Business Invariants (code-enforced):**
  - `debits == credits` in every journal (`FinancialPostingService` balance check).
  - Return qty ≤ sold qty, per line and per invoice (`:524-532`, `:804-810`).
  - Refund total ≤ original invoice total (`:804-810`).
  - Cash/Card/Mixed payouts must equal the full return total (`:844-850`); `AccountCredit` = full credit
    (`:840-843`).
  - Server-side price authority: client prices are ignored (`:534-559`).
  - Stock `Quantity` only increases on a return (no negative-stock path).
  - Idempotency payload hash must match the bound key (`:227`, `:1063`).

---

## 2. Runtime Collaboration (reverse-engineered)

Sales Return is a SINGLE-request, single-runtime (POS/API) transaction for the document + stock +
ledger, then a SEPARATE accounting post. The "runtimes" it collaborates with are in-process
services/aggregates resolved inside `SalesReturnsController` + `CommerceAccountingService` +
`FinancialPostingService` + `LedgerService` + `ProductVariantStockMutationSupport` +
`AccountingOutboxService` + `AccountingOutboxProcessor` (background).

| Runtime | Role in this flow | Evidence |
|---|---|---|
| POS / API (`SalesReturnsController`) | Orchestrator; owns the return tx boundary, validation, stock restore, ledger entry, outbox enqueue, synchronous accounting attempt. | `:202-997` |
| Inventory (`ProductVariantStockMutationSupport`) | Locks stock rows in deterministic `(WarehouseId,VariantId)` order, restores `Quantity`, emits `StockSyncOutboxMessage` for Mongo sync. | `:683`, `:758-792`, `:949-955`, `ProductVariantStockMutationSupport.cs:216-249` |
| Accounting (`CommerceAccountingService` + `FinancialPostingService`) | Posts the revenue-reversal and (optional) COGS-reversal journals; idempotent; Serializable. | `CommerceAccountingService.cs:561-720`, `FinancialPostingService.cs:39-131` |
| Treasury / Cashbox (`CashboxLedgerAccountService`) | Resolves the per-warehouse **cashbox GL account code** that funded-cash refunds Credit. The cashbox *entity* balance is never written. | `CommerceAccountingService.cs:621-626,1486` |
| Ledger (`LedgerService`) | Mutates `customer.Balance` and appends `AccountLedger` (authoritative). | `:870-896`, `LedgerService.cs:215-239` |
| CRM (`Customer`) | Referenced for ownership validation + balance credit; no CRM write beyond balance. | `:443-458`, `:868-896` |
| Catalog (`Product`/`ProductVariant`) | Source of variant resolution, cost basis (`CostPriceSnapshot`), and default-variant fallback. | `:668-687`, `:1180`, `:1434-1457` |
| Analytics / Projections | READ SQL directly. No Mongo/ES/Redis domain event is emitted by the return for reporting; only the `StockSyncOutboxMessage` (inventory sync) is produced (`:949-955`). | `:949-955` |
| Outbox (`AccountingOutboxService` / `AccountingOutboxProcessor`) | `PosRefund` message staged in the return tx; background worker retries if the synchronous post failed. | `:899-906`, `AccountingOutboxProcessor.cs:59,1795` |

---

## 3. Execution Timeline (reverse-engineered, every step)

1. **HTTP POST** `/api/salesreturns` with `CreateSalesReturnRequest` (`:202`).
2. **Legacy contract guard** — reject old `refundFromCash`/`refundAmount` payloads (`:204-208`,
   `:344-359`).
3. **Non-empty items guard** (`:210-213`).
4. **Idempotency resolution** — `Idempotency-Key`/`X-Request-Id` normalized, `AUTO:` hash fallback
   (`:215-218`, `:272-318`, `:1459-1487`).
5. **Idempotency pre-check** — if an `SalesRequestIdempotency` record exists: divergent hash →
   `Conflict`; completed → replay stored `200`; in-flight → `Conflict` (`:224-244`).
6. **Begin return SQL transaction** (`:246`).
7. **Claim idempotency row** `INSERT ... ON CONFLICT DO NOTHING`
   (`TryClaimSalesReturnIdempotencyAsync` `:247-255`, `:1013-1086`). Short-circuit on conflict.
8. **Load + WRITE-LOCK original invoice** `FOR UPDATE` (`:426`, `:1107-1144`).
9. **Validate invoice** — exists, has items, `Status == Completed`, `CustomerId` matches
   (`:428-446`); `originalInvoiceTotal > 0` (`:457-461`).
10. **Build original-invoice line snapshots** — group by `(ProductId, EffectiveVariantId)`, enforce a
    single pricing/cost snapshot per key (else "process manually" error), resolve default variant for
    legacy products (`:463-470`, `:1173-1257`).
11. **Load prior returns** for this invoice and sum `priorBaseQuantitiesByKey` + `priorReturnedTotal`
    from committed `ReturnItems` (`:472-491`).
12. **Per requested line** — resolve snapshot, require matching unit, compute `currentBaseQuantity`,
    enforce `prior + requested + current ≤ original` (`:497-572`); **drop & log client price overrides**
    (`:534-559`); build `PendingReturnLine` with server-prorated discount/tax (`:561-571`).
13. **Prorate invoice-level discount** across lines (`:579-612`).
14. **Allocate return number** `DocumentNumberingSupport.AllocateReturnNumberAsync` (`:646-649`).
15. **Create `Return` + lock stock** `LockMultipleStocksAsync` (deterministic order) (`:651-686`).
16. **Per line:** recompute net subtotal (reject if negative, `:697-703`); build `ReturnItem` with
    `CostPriceSnapshot`/`CostAmountSnapshot` (`:706-727`); **restore stock**
    `variantStock.Quantity += BaseQuantity` (`:758-761`); append `StockMovement`
    (`MovementType = SalesReturn`, `:774-792`).
17. **Recompute + guard return total** — `TotalAmount > 0` (`:795-802`);
    `priorReturnedTotal + TotalAmount ≤ originalInvoiceTotal` (`:804-810`).
18. **Resolve refund allocation** via `RefundAllocationSupport.Resolve` (`:361-388`, `:812-820`);
    guards: payout ≥ 0, ≤ invoice total, ≤ return total, cash/card/mixed must equal full total,
    `AccountCredit` ⇒ full customer credit (`:822-858`).
19. **Bump invoice version** `originalInvoice.AccountingMutationVersion++` (`:863-864`) — the bounded
    historical authority for return mutations.
20. **Add `Return`** to context (`:866`).
21. **Ledger + CustomerTransaction (if credit > 0)** — `CreateLedgerEntry(Customer, SalesReturn,
    debit 0, credit creditToCustomerAmount)` + history row (`:868-896`).
22. **Enqueue `PosRefund` outbox** inside the SAME tx (`:899-906`,
    `AccountingOutboxService.EnqueuePosRefundAsync` → `SaveChangesAsync` `:752`).
23. **Audit log** (`:908-940`).
24. **Persist return number + stock events** — `SaveReturnChangesWithRetryAsync` (`:942-947`);
    `PublishCommittedStockEventsAsync` writes `StockSyncOutboxMessage` rows (`:949-955`).
25. **Build response + store idempotency response** (`:957-976`).
26. **SaveChanges + COMMIT return tx** (`:976-977`).
27. **Synchronous accounting attempt (post-commit)** — `TryCompleteSynchronousRefundAccountingAsync`
    calls `CommerceAccountingService.PostPosRefundAsync` in a fresh transaction; on success marks the
    outbox row processed; on failure leaves it pending for the worker (`:979-996`, `:1608-1646`).
28. **Return 200** to client (`:996`).
29. **(Background, if needed)** `AccountingOutboxProcessor.ProcessPosRefundAsync` reloads the return,
    posts the journals idempotently, marks the outbox processed (`:1795-1829`).

---

## 4. Critical Architecture Question

**Is Sales Return a Reverse Transaction, a Compensation Transaction, or Hybrid?**

**Answer: HYBRID — but leaning COMPENSATION.** Concretely:

- **Inventory / Ledger / Document:** COMPENSATION. The original `SalesInvoice` is never edited (except
  a monotonic version counter). A new `Return` aggregate is created (`:866`); stock is *increased*
  (`:758`); the customer ledger gets a new *credit* entry of type `SalesReturn` (`:870-879`). These
  are additive, forward documents — not in-place reversals.
- **Accounting:** COMPENSATION, explicitly NOT a journal reversal. `PostPosRefundAsync`
  (`CommerceAccountingService.cs:561`) posts **fresh, balanced journals** via `PostJournalAsync`
  (`:688,710`). It does **not** call `ReverseJournalAsync` (which exists in the codebase and IS used by
  `SaleReversal`/`PurchaseReversal`/`ExpenseReversal` — `AccountingOutboxProcessor.cs:719,721,728`,
  `FinancialPostingService.cs:256`). A return's journal idempotency key is
  `REFUND:{tenant}:{returnId}` (`:684`), distinct from the sale's journal — proving it is a new posting,
  never an undo of the original.
- **The one "reverse" flavor** is semantic only: the *signs* of the revenue journal are inverted
  relative to the sale (Debit `PosSales` instead of Credit; Credit `Cashbox`/`Visa`/`AR` instead of
  Debit) — `CommerceAccountingService.cs:644-686` vs the sale's revenue journal. But this is a
  deliberately reconstructed compensating entry, not a call to `ReverseJournalAsync`.
- **Why compensation and not reversal?** The original sale journal must remain immutable for audit; a
  return can be partial (multiple returns summing to ≤ invoice total, `:804-810`), so a single
  "reverse the whole journal" would be wrong. Compensation lets each return post only its own prorated
  amount independently and idempotently.

**Conclusion:** Sales Return = *compensation transaction* at the document/inventory/ledger/accounting
layer, implemented as additive forward documents + fresh balanced journals, with the original sale left
untouched (apart from a version bump). It is **not** a reverse transaction.

---

## 5. Aggregate Analysis

| Aggregate | Mutation | Evidence |
|---|---|---|
| `Return` (SalesReturn) | **Created** (never updated/deleted via API). | `:651-666`, `:866` |
| `ReturnItem` | **Created** per line (immutable cost snapshots). | `:706-727` |
| `SalesInvoice` (original) | **Read-locked**; `AccountingMutationVersion++` only (`:863`). No financial fields mutated. | `:1107-1144`, `:863-864` |
| `ProductVariantStock` | **Restored**: `Quantity += BaseQuantity` under lock. | `:683`, `:758-761` |
| `StockMovement` | **Created** (`MovementType = SalesReturn`). | `:774-792` |
| `Customer` (balance) | **Mutated** via ledger (balance += credit). | `:868-896`, `LedgerService.cs:218-239` |
| `AccountLedger` | **Created** (customer credit entry). | `:870-879` |
| `CustomerTransaction` | **Created** (history, `BalanceAfter`). | `:881-896` |
| `AccountingOutboxMessage` | **Created** (`PosRefund`); later **marked processed**. | `:899-906`, `:1605` |
| `StockSyncOutboxMessage` | **Created** (inventory sync). | `:949-955` |
| `JournalEntry` / `LedgerEntry` (GL) | **Created** (revenue + optional COGS reversal). | `CommerceAccountingService.cs:688,710` |
| `AuditLog` | **Created**. | `:908-940` |
| `SalesRequestIdempotency` | **Created/claimed** (idempotency). | `:1013-1049`, `:971-976` |

No aggregate is deleted or hard-reversed. Everything is forward, additive, append-only.

---

## 6. Inventory Analysis

- **Stock restored?** Yes — `variantStock.Quantity += line.BaseQuantity` (`:758-761`).
- **Same warehouse?** Yes — restored to `request.WarehouseId`, the warehouse the return is posted to
  (`:661`, `:677-682`, `:778`). No cross-warehouse rerouting.
- **Same variant?** Yes — restored to `line.Snapshot.EffectiveVariantId`, derived from the original
  invoice line (or default variant for legacy products) (`:668-687`, `:1173-1257`).
- **Variant validation?** Yes — stock rows locked with `CreateIfMissing: true`
  (`:676-686`) and `LockMultipleStocksAsync` (`:292`); if a locked row is missing after lock, it throws
  (`:750-756`). Returned unit must equal the original sale unit (`:510-516`).
- **Serial numbers / Batch tracking?** Not in this flow — `ReturnItem` carries no serial/batch fields
  (`ReturnItem.cs`); stock restore is quantity-only on `ProductVariantStock`.
- **Cost restoration?** Yes, **immutably**: `CostPriceSnapshot` and `CostAmountSnapshot` are snapshotted
  at return time from the original sale line (`:720-723`, `:1434-1457`) and used by
  `CalculateReturnCost` for the COGS reversal (`CommerceAccountingService.cs:1468-1484`). Cost basis is
  the original sale's cost, NOT re-fetched live price.
- **FIFO / Average Cost?** Neither is recomputed on return. The returned cost is the **original
  line's cost snapshot**; the COGS reversal simply Debits `Inventory` / Credits `CostOfGoodsSold` by
  that exact amount (`:690-711`). Inventory valuation method (FIFO/avg) lives in the cost engine
  elsewhere; the return does not re-derive it.
- **Negative inventory effects?** None. Restoring only increases `Quantity`; the negative-stock guard
  (`ValidateQuantityChange`, `ProductVariantStockMutationSupport.cs:364-386`) only fires on decreases.

---

## 7. Financial Analysis

- **Cash refunded?** Yes — `RefundMethod.Cash` → `CashAmount = TotalAmount`, posted as Credit to the
  per-warehouse cashbox GL account (`:366-370`, `CommerceAccountingService.cs:619-626,652-658`).
- **Card refunded?** Yes — `RefundMethod.Card` → Credit to `VisaClearing`
  (`:371-375`, `:660-666`).
- **Wallet refunded?** No direct wallet refund in this endpoint. `AccountCredit` credits the customer's
  **receivable ledger balance** (`:840-843`, `:868-896`), not a wallet.
- **Partial refund?** Supported in two senses: (a) **partial quantity** — multiple returns allowed up
  to the sold total (`:524-532`, `:804-810`); (b) **partial payout** — `AccountCredit` leaves the whole
  amount as customer credit even when a cash/card portion is also paid? No: cash/card/mixed MUST equal
  the full total (`:844-850`); only `AccountCredit` leaves it all as credit. There is no "pay half
  cash, half credit" mix — the split is payout-vs-credit by method.
- **Refund method?** `RefundMethod` enum {Cash, Card, Mixed, AccountCredit}
  (`RefundMethod.cs:3-9`); resolved by `RefundAllocationSupport.Resolve` (`:361-388`,
  `RefundAllocation.cs:59-110`).
- **Cashbox mutation?** The **cashbox entity balance is NOT written** by the return (consistent with
  FLOW-01: ledger is authoritative). Only the cashbox **GL account** is credited in the journal.
- **Treasury mutation?** Treasury.GL cashbox account is credited for cash refunds; `VisaClearing` for
  card. No outbound money movement is initiated by the return (no gateway call) — the payout is a GL
  booking; actual cash disbursement is an operational concern outside this tx.
- **Journal reversal?** No — see §4. Fresh compensating journals via `PostJournalAsync`.
- **Credit Note / Debit Note?** Not generated by this endpoint. `CreditNote`/`DebitNote` exist only in
  the Statement-of-Account adjustment flow (`StatementOfAccountController.cs:568-626`), not in sales
  returns.

---

## 8. Ledger Analysis

- **Customer balance:** Mutated only if `creditToCustomerAmount > 0` and a customer exists
  (`:852-858`, `:868-896`). The ledger entry is `Debit 0 / Credit creditToCustomerAmount` of type
  `SalesReturn`, which **increases** the customer's credit balance (reduces what they owe / builds
  store credit) because `newBalance = currentBalance + debit - credit` (`LedgerService.cs:218`).
- **Ledger entries:** One `AccountLedger` row per return with a credit (`:870-879`); plus the GL
  `JournalEntry`/`LedgerEntry` rows from the accounting post (§7).
- **Outstanding balances:** Recomputed as `RunningBalance` on the customer ledger
  (`LedgerService.cs:280-317` prefers in-flight tracked balances so multiple entries in one UoW stay
  additive).
- **Refund entries:** The cash/card portion is a GL journal Credit to cashbox/Visa (not a customer
  ledger debit) — the customer ledger only carries the *credit* (unpaid) portion.
- **Reverse / compensation entries:** Compensation only (§4). No reverse ledger entry is written
  against the original sale's ledger row.
- **Tenant scoping:** Every ledger/balance write carries `TenantId = _tenantContext.RequiredTenantId`
  (`:870-896`); the party row is locked inside the tenant scope (`LedgerService.cs:215`).

---

## 9. SQL Transaction Analysis

- **Transaction boundary (return doc):** `BeginTransactionAsync` at `:246`; `SaveChangesAsync` +
  `CommitAsync` at `:976-977`. Everything from invoice lock → stock restore → ledger → outbox → stock
  sync messages lives in THIS one transaction.
- **Isolation level:** The return tx uses the provider default (ReadCommitted) — the explicit
  `BeginTransactionAsync()` at `:246` takes no isolation argument. The **invoice row is write-locked**
  with `FOR UPDATE` / `UPDLOCK,ROWLOCK,HOLDLOCK` (`:1114-1132`), and stock rows with the same
  (`ProductVariantStockMutationSupport.cs:408-431`). The **accounting post** runs in its OWN transaction
  at **Serializable** (`FinancialPostingService.cs:97`).
- **Lock ordering:** (1) `SalesInvoices` row locked first and held for the whole return tx (SR-CON-01,
  `:423-426`); (2) stock rows locked in deterministic `(WarehouseId, VariantId)` order
  (`LockMultipleStocksAsync` `:301-304`); (3) customer party row locked in
  `ApplyPartyTransactionAsync` (`:215`). Deterministic ordering prevents deadlocks.
- **Rollback:** Any exception before commit → `transaction.RollbackAsync()` (`:614-627` catch-all).
  Idempotency claim conflict → rollback + short-circuit (`:251-255`).
- **Recovery:** If the synchronous accounting post fails *after* the return committed, the `PosRefund`
  outbox row remains pending and the background `AccountingOutboxProcessor` retries
  (`TryCompleteSynchronousRefundAccountingAsync` catch `:1635-1645`; worker `:1795-1829`). The outbox
  row is enqueued **inside** the return tx (`:899-906`, `AccountingOutboxService.cs:747-752`), so it
  can never be "document committed but outbox lost."
- **Atomic guarantees:** Document/stock/ledger/outbox are atomic (one commit). Accounting is
  *eventually* consistent via the outbox + idempotent re-post (`PostJournalAsync` double-checks
  idempotency inside its own Serializable tx, `FinancialPostingService.cs:106-131`). The
  `AccountingMutationVersion` bump (`:863`) gives the outbox a monotonic invoice-version stream for
  ordering/deferral (`AccountingOutboxProcessor.cs:84-107,230-234`).

---

## 10. Event Analysis

- **Published events (in-transaction):** `StockSyncOutboxMessage` rows for `ProductVariantStockChangedEvent`
  (inventory → Mongo sync), written by `PublishCommittedStockEventsAsync` (`:949-955`,
  `ProductVariantStockMutationSupport.cs:216-249`). `AccountingOutboxMessage` (`PosRefund`) (`:899-906`).
  `AuditLog` (`:908-940`).
- **Outbox:** `AccountingOutboxService.EnqueuePosRefundAsync` → `EnqueueMessageAsync` persists the row
  in the caller's tx (`AccountingOutboxService.cs:188-219,683-759`). `PosRefund` is in
  `_criticalFinancialTruthEventTypes` (`:59`) so it is never dropped as "stale."
- **Inbox:** None for this flow (no inbound event consumption by the return path).
- **Projection updates:** Inventory projection updated via `StockSyncOutboxMessage` →
  `StockSyncOutboxWorker`/`Processor` (consumed by Mongo catalog). Accounting projections (GL) updated
  by the journal post. **Analytics reads SQL directly** — the return emits no analytics-specific
  projection event.
- **Reports:** `SalesReturnReport` / `StatementOfAccount` read SQL (`SalesReturnsController.cs:143`
  single-get, `PointOfSale.Web/src/pages/SalesReturnReport.jsx`). The SOA reflects the customer ledger
  credit.

---

## 11. Business Rules (implementation-extracted)

1. Original invoice **required** (`:408-411`).
2. Invoice must be **`Completed`** (`:438-441`).
3. **Customer must match** the original invoice (`:443-446`).
4. **Warehouse must be active + tenant-owned** (`:1098-1105`).
5. **Cannot return more than sold**, per line (`:524-532`) and per invoice total (`:804-810`); prior
   returns are summed from committed `ReturnItems` (`:472-491`).
6. **Total refund ≤ original invoice total** (`:804-810`).
7. **Refund amount ≥ 0**, ≤ invoice total, ≤ return total (`:822-837`).
8. Cash/Card/Mixed payouts **must equal** the full return total (`:844-850`); `AccountCredit` = full
   credit (`:840-843`).
9. **Server-authoritative pricing** — client `UnitPrice`/`TaxRate` overrides are ignored & logged
   (`:534-559`).
10. **Unit must match** original sale unit (`:510-516`).
11. **Single pricing/cost snapshot per invoice line** — heterogeneous splits force manual processing
    (`:1225-1236`).
12. **Net refund subtotal cannot go negative** (`:697-703`); **return total must be > 0** (`:799-802`).
13. **Idempotency enforced** — divergent payload on a reused key → `Conflict` (`:227-233`, `:1063`);
    same payload → replayed `200` (`:235-238`).
14. **Legacy refund fields rejected** (`refundFromCash`/`refundAmount`) (`:344-359`).
15. **At least one item, qty > 0** (`:210-213`, `:499-501`, `:574-577`).
16. **Customer required if any unpaid credit** (`:852-858`).
17. **Default variant resolution** for legacy products without explicit variant (`:1180-1201`).

---

## 12. Failure Analysis

- **Duplicate return (same payload):** Idempotency key → stored `200` replayed; no double document
  (`:224-244`, `:971-976`).
- **Duplicate return (divergent payload, same key):** `Conflict` (`:227-233`, `:1063-1069`).
- **Network failure mid-request:** Outer catch rolls back the return tx (`:614-627`); client retries
  with same idempotency key → safe replay.
- **Inventory mismatch:** Stock restore only *increases*; the only stock failure is a missing locked row
  → `InvalidOperationException` (`:750-756`) → rollback.
- **Accounting failure (after commit):** Synchronous post throws → caught, outbox left pending, worker
  retries (`:1635-1645`). Return doc already committed; accounting eventually consistent.
- **Ledger failure:** `LedgerService` runs inside the return tx; any failure rolls back the whole
  return (`:268-277`).
- **Concurrency (two returns, same invoice):** Both take the `FOR UPDATE` invoice lock (SR-CON-01);
  the second waits, then re-validates `priorReturnedTotal` against the first's committed `ReturnItems`
  — over-return is prevented (`:423-426`, `:472-491`, `:804-810`).
- **Rollback:** Total for pre-commit errors; post-commit accounting failures are recovered, not rolled
  back.
- **Recovery:** Outbox + idempotent re-post + `AccountingMutationVersion` deferral
  (`AccountingOutboxProcessor.cs:230-234,1795-1829`).

---

## 13. Performance Analysis

- **Database round trips:** One tx with: invoice lock+load, prior-return queries (2), variant loads
  (2), stock lock loop (N rows), ledger, customer, outbox, audit, stock-sync (x2 SaveChanges), final
  SaveChanges — roughly 10–15 queries. The accounting post is a **second** tx (extra round trips).
- **Locks:** Invoice row (`FOR UPDATE`) held for the whole return tx; N stock rows locked
  deterministically; customer party row. All ordered to avoid deadlocks.
- **Hot paths:** High-volume return spikes contend on the same `(invoiceId)` lock (intended — serializes
  returns per invoice) and per-variant stock locks.
- **Scaling bottlenecks:** Synchronous accounting post adds latency on the request path; mitigated by
  falling back to the async worker. Document-number allocation (`AllocateReturnNumberAsync`) and
  `SaveReturnChangesWithRetryAsync` add a retry round trip.
- **Deferred work:** Accounting journal post (async via outbox), inventory Mongo sync (async via
  `StockSyncOutboxWorker`), analytics (SQL reads).

---

## 14. Security Analysis

- **Permissions:** `[Authorize]` on the controller (`:28`) — any authenticated tenant operator JWT.
  No finer role check in this controller (role gating is enforced by the auth/JWT layer upstream).
- **Tenant isolation:** Every query/filter uses `_tenantContext.RequiredTenantId`
  (`:654,677,777,884,910` etc.); warehouse must match tenant (`:1102`); invoice lock reapplies tenant
  predicate (`:1117`); stock lock reapplies tenant predicate
  (`ProductVariantStockMutationSupport.cs:411,425`). Cross-tenant return is impossible — `RequiredTenantId`
  fails closed.
- **Warehouse ownership:** `LoadActiveWarehouseAsync` enforces `warehouse.TenantId == RequiredTenantId`
  (`:1102`).
- **Invoice ownership:** Invoice is loaded with `TenantId = RequiredTenantId` and write-locked
  (`:1117-1119`); customer must match (`:443`).
- **Fraud prevention:** Server-authoritative pricing (§11 #9), over-return guards (§11 #5/#6), payout
  guards (§11 #8), immutable cost snapshots (§6), full audit log (`:908-940`).
- **Duplicate prevention:** Idempotency key + request hash (§11 #13).
- **Replay protection:** Idempotency row claimed with `ON CONFLICT DO NOTHING`; stored response
  replayed; accounting re-post is idempotent by `REFUND:{tenant}:{returnId}`
  (`FinancialPostingService.cs:56-131`).

---

## 15. Hermes Browser Test Specification

### Preconditions
- Tenant T with operator JWT (Owner role).
- Active warehouse W (TenantId = T).
- Product P with variant V, default unit U, cost basis C, sale price S, tax rate t.
- Completed sales invoice I for customer Cu: 2×P @ S, tax t, total = 2·S·(1+t). Stock of V in W = 10.
- Customer Cu has a prior receivable balance B0 (from a credit sale) so a ledger credit is observable.

### User Journey
1. Operator opens return modal for invoice I (`GET /api/salesreturns/invoice/{I}`).
2. Operator enters 1×P, reason "defective", `RefundMethod = Cash`, `Idempotency-Key = k1`.
3. `POST /api/salesreturns`.

### Runtime Interaction
- Controller locks I (`FOR UPDATE`), sums prior returns (0), validates qty ≤ 2, restores stock, posts
  ledger credit (if `AccountCredit`) or payout, enqueues `PosRefund`, commits, attempts synchronous
  accounting post.

### Expected Database State (after commit)
- `Returns`: 1 row, `ReturnType=SalesReturn`, `TotalAmount = S·(1+t)`, `WarehouseId=W`,
  `OriginalInvoiceId=I`.
- `ReturnItems`: 1 row, `ProductVariantId=V`, `BaseQuantity=1`, `CostAmountSnapshot=C·1`.
- `ProductVariantStocks` (W,V): `Quantity = 11` (10 + 1).
- `StockMovements`: 1 row, `MovementType=SalesReturn`, `QuantityChange=1`, `QuantityAfter=11`.
- `AccountingOutboxMessages`: 1 row `EventType=PosRefund`, `IdempotencyKey=REFUND:T:{returnId}`,
  `ProcessedAtUtc` set (synchronous success) OR pending (worker will process).
- `JournalEntries`: revenue reversal (Debit `PosSales` S·(1+t), Credit cashbox S·(1+t)) + (if C>0)
  COGS reversal (Debit `Inventory` C, Credit `COGS` C).
- `AccountLedgers` (Cu): 1 row `TransactionType=SalesReturn`, `CreditAmount = creditToCustomerAmount`.
- `SalesInvoices` (I): `AccountingMutationVersion = 1`.

### Expected UI State
- Return list shows new `ReturnNumber`; detail shows items, total, refund method, amounts.
- Inventory page for V@W shows quantity 11.
- SOA for Cu shows the new credit entry and updated balance.

### Expected Financial State
- If `Cash`: cashbox GL credited S·(1+t); customer ledger credit = 0 (fully paid out).
- If `AccountCredit`: customer ledger credit = S·(1+t); cashbox unchanged.
- Customer balance B0 adjusted by +creditToCustomerAmount.

### Expected Inventory State
- V@W quantity restored to 11; `StockMovement` audit present.

### Negative Tests
- Return 3×P → 400 "Cannot return more than sold" (`:524-532`).
- Return on a `Draft` invoice → 400 "Only completed sales invoices can be returned" (`:438-441`).
- Different `CustomerId` → 400 "Customer must match" (`:443-446`).
- Same `Idempotency-Key k1` with different items → 409 (`:227-233`).
- Same `Idempotency-Key k1` same payload → replayed 200, no duplicate `Return` (`:235-238`).

### Assertions
- Exactly one `Return` per idempotency key.
- `Σ ReturnItems.BaseQuantity per (P,V) ≤ original sold BaseQuantity` across all returns for I.
- `debits == credits` in every journal.
- `ProductVariantStocks.Quantity ≥ 0` always.
- `AccountingOutboxMessage.PosRefund` eventually processed exactly once (idempotent re-post).

---

## 16. Runtime Collaboration Summary

- **POS/API** owns the request, validation, and the return-document transaction.
- **Inventory** restores variant stock in the same warehouse and emits the inventory-sync outbox.
- **Accounting** posts fresh compensating journals (revenue + optional COGS) idempotently and
  Serializable.
- **Treasury/Cashbox** is touched only as a GL account (cashbox/Visa) — no entity balance write, no
  outbound money.
- **CRM/Customer** balance is compensated via the ledger; no other CRM mutation.
- **Catalog** is read-only source of variant/cost; default-variant fallback for legacy lines.
- **Analytics** reads SQL; no dedicated projection event from the return.
- **Outbox** guarantees the accounting post is durable and retry-safe; the worker is the recovery path.

---

## 17. Documentation Validation

- **Assumption "returns reverse the sale journal"** → FALSE. Implementation posts a *new* compensating
  journal (`PostPosRefundAsync`), never `ReverseJournalAsync`. (§4)
- **Assumption "return edits the original invoice"** → FALSE. Invoice is read-locked and only gets a
  `AccountingMutationVersion++` (`:863`); no financial field is mutated.
- **Assumption "refund can be partial cash + partial credit"** → FALSE. Cash/Card/Mixed must equal the
  full total; only `AccountCredit` leaves it as credit (`:840-850`).
- **Assumption "negative stock possible on return"** → FALSE. Returns only increase stock (§6).
- **Assumption "a Credit Note is issued"** → FALSE. No note is generated by this endpoint (§7).
- **Assumption "delete/cancel return exists"** → FALSE. No DELETE endpoint (§0 #14).
- Existing module guides (`RETURNS-MODULE-GUIDE.md`, `RETURNS-QUICK-REFERENCE.md`) describe UI/legacy
  intent and are SECONDARY to this implementation-backed analysis; where they imply reversal/delete
  semantics, this doc (implementation) wins.

---

## 18. Final Architecture Question

**How does Shopixy reverse a completed sale while preserving inventory consistency, financial
correctness, accounting integrity, ledger correctness, customer balance, runtime boundaries, recovery,
and scalability?**

Shopixy does **not** reverse the sale. It issues a **compensation transaction** — a new, additive
`Return` document — and reconstructs the inverse economic effect on each axis, leaving the original
sale intact for audit:

- **Inventory consistency:** Stock is restored to the *same* variant in the *same* warehouse under a
  deterministic `(WarehouseId,VariantId)` lock, increasing `Quantity` (no negative path)
  (`:683`,`:758-761`). The original cost basis is snapshotted immutably for the later COGS reversal
  (`:720-723`, `:1468-1484`).
- **Financial correctness:** Refund total is **server-recomputed** from the original invoice snapshots;
  client prices are dropped (`:534-559`). Over-return and over-refund are blocked per line, per
  invoice, and per payout (`:524-532`,`:804-810`,`:822-858`).
- **Accounting integrity:** Two **fresh, balanced** journals (revenue reversal + optional COGS
  reversal) are posted idempotently under Serializable isolation; the original sale journal is never
  touched (`CommerceAccountingService.cs:561-720`, `FinancialPostingService.cs:39-131`). Because returns
  can be partial, compensation (not `ReverseJournalAsync`) is the only correct model.
- **Ledger correctness:** The customer balance is compensated via a `SalesReturn` credit ledger entry
  under a pessimistic party lock, with `RunningBalance` recomputed additively
  (`:868-896`, `LedgerService.cs:215-317`).
- **Customer balance:** Preserved as a first-class ledger mutation; `AccountCredit` builds store credit,
  cash/card settles the payable leg through the GL (§7).
- **Runtime boundaries:** Document/stock/ledger live in ONE return transaction (ReadCommitted + explicit
  row locks); accounting is a SEPARATE transaction (Serializable) attempted synchronously and recovered
  asynchronously (`:246`,`:977`,`:979-996`, `FinancialPostingService.cs:97`).
- **Recovery:** The `PosRefund` outbox row is written **inside** the return tx and is a *critical
  financial truth* event; if the synchronous post fails, the background `AccountingOutboxProcessor`
  retries idempotently, ordered/deferred by `AccountingMutationVersion`
  (`:899-906`, `AccountingOutboxProcessor.cs:59,230-234,1795-1829`). Idempotency (request hash +
  stored response + journal idempotency key) makes every retry safe.
- **Scalability:** Per-invoice `FOR UPDATE` lock serializes competing returns (correctness over
  throughput); stock locks are deterministically ordered to avoid deadlocks; deferred work (accounting
  post, Mongo inventory sync, analytics) is pushed off the request path via outboxes.

**One-line verdict:** Shopixy reverses a completed sale by **compensating it** — a new authoritative
`Return` document restores stock, credits the ledger, and posts fresh balanced reversal journals through
an idempotent outbox — never by editing or reversing the original sale.
