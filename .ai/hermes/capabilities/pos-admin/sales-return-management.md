# Sales Return Management

Reverse-engineered from implementation. PostgreSQL is the authoritative source of truth; Mongo, Redis, Elasticsearch, MassTransit projections are derivative. This is POS Sales Return (`ReturnType.SalesReturn`). It is NOT Purchase Return, NOT Checkout Refund, NOT Storefront/CommerceHub return.

## Purpose

Sales Return Management reverses goods a customer previously bought against a completed sales invoice (a POS sale) and restores the customer's position. It exists to:

- Take goods back from a customer for an unwanted/defective/oversold line without mutating the original sale ledger beyond a version bump on the invoice.
- Restore on-hand warehouse stock for the returned variant(s) (stock INCREASES — the inverse of a sale).
- Return money to the customer via cash, card, mixed, or customer-account credit.
- Post a correlated accounting journal that reverses revenue recognition and (if cost data exists) reverses COGS + restores inventory.
- Provide an auditable, idempotent, concurrency-safe record of goods returned by a customer.

What it owns:
- The `Return` row (`ReturnType = SalesReturn`) and its `ReturnItem` lines.
- The sales-return stock restoration into `ProductVariantStocks` (variant-centric, SQL-authoritative).
- The `StockMovement` rows of type `SalesReturn`.
- The customer ledger entry + `CustomerTransaction` recording the (credit) balance adjustment when money is not paid out immediately.
- The accounting outbox message + journal posting (revenue reversal; optional COGS/inventory reversal).
- The idempotency record in `SalesRequestIdempotency` (shared table; filtered by `Operation = "SALES_RETURN_CREATE"`).
- The `ReturnNumber` document number.
- The `AuditLogs` entry ("Create" / "SalesReturn").

What it does NOT own:
- The original sales invoice amounts, payments, or settlement — it only reads them and bumps `SalesInvoice.AccountingMutationVersion`.
- Actual outbound cash/card disbursement mechanics beyond the accounting journal. (Refund is modelled as a customer payout allocation; the cashbox journal posts a credit to the warehouse cashbox/Visa clearing. Physical cash handling is the till's domain, governed separately by payment authority — unresolved captured-payment blocks outward money.)
- Purchase returns, CommerceHub customer/vendor returns, or marketplace returns (separate `ReturnType.PurchaseReturn`, `CommerceHubReturnRequest`, `ReturnManagementService`/`CommerceHubReturnStatus`).
- Any status/state transition on the parent `SalesInvoice` — unlike Purchase Return, a Sales Return does NOT flip `SalesInvoice.Status` to PartiallyReturned/FullyReturned. The invoice is left at `Completed`; only `AccountingMutationVersion` is incremented.

## Business Scope

Implemented and owned by Sales Return:
- Sales Return (full entity, `ReturnType.SalesReturn`).
- Partial Return (one invoice may absorb multiple returns until sold quantity exhausted).
- Full Return (cumulative returned base quantity can reach the originally sold base quantity on a line). There is no separate "full return" marking on the Return row — it is derived from quantity bounds.
- Return Validation (invoice exists, `Status == Completed`, customer matches, product was on invoice, variant on invoice, unit on invoice, quantity not exceeded, total not exceeded).
- Original Invoice Validation (the `SalesInvoice` is loaded with a pessimistic write lock for the whole transaction — see SR-CON-01 fence).
- Customer Validation (request `CustomerId` must equal the invoice's `CustomerId`; the customer row must still exist when a credit is owed).
- Warehouse Validation (warehouse must exist and be `IsActive`; variant stock lives there).
- Quantity Validation (base-unit quantity against cumulative returned quantity per product+variant on the invoice, and against the original sold quantity).
- Inventory Restore (variant stock INCREASED; no reservation/available-stock check on return — returns only add stock, never block on availability).
- Refund Calculation (prorated from original invoice line economics; client UnitPrice/TaxRate overrides are IGNORED and the server's invoice values are authoritative).
- Customer Ledger (when refund is not fully paid out immediately — AccountCredit or partial payout — a credit is posted to the customer ledger).
- Accounting Reversal (outbox → journal: Debit PosSales, Credit cashbox/Visa/AccountsReceivable; optional Debit Inventory / Credit COGS).
- Receipt (a `ReceiptModal` is shown on the frontend after a successful return — presentation only).
- Return Number (allocated via `DocumentNumberingSupport.AllocateReturnNumberAsync`).
- Audit Trail (ledger entry + customer transaction + stock movement + outbox + `AuditLogs` row; all tenant-scoped).
- Attachments: NOT implemented (no attachment field/DTO). Reason + Notes carry textual context.
- Return Status: There is NO separate Sales Return status enum. The `Return` entity has no status column. The Return is immutable once committed (no PUT/DELETE endpoints exist). The lifecycle is observed via outbox processing (`ProcessedAtUtc`/`JournalId`).

Explicitly NOT in scope (proven absent):
- No return approval/rejection/cancellation workflow. No `CommerceHubReturnStatus` transitions. Those belong to CommerceHub customer/vendor returns (`PosAdminReturnsController`, `ReturnManagementService`, `CommerceHubReturnStatus`).
- No "Created → Completed → Cancelled → Rejected" state machine on the `Return` row.
- No parent `SalesInvoice.Status` transition on return (contrast with Purchase Return which flips to PartiallyReturned/FullyReturned). Sales returns only bump `AccountingMutationVersion`.

## Authority Boundaries

Authoritative (source of truth, SQL/PostgreSQL):
- Sales Return truth: `Returns` + `ReturnItems` tables (TenantId-scoped).
- Original Sale: `SalesInvoices` + `SalesInvoiceItems` (read; `AccountingMutationVersion` mutated only).
- Return Quantity: `ReturnItem.Quantity` (display unit) and `ReturnItem.BaseQuantity` (base units).
- Refund Amount: computed in the controller from original invoice proportions (`return.Subtotal/TaxAmount/TotalAmount`); then split into cash/card payout vs customer credit per `RefundMethod`.
- Inventory Restore: `ProductVariantStocks.Quantity` (row locked via `LockMultipleStocksAsync`, `CreateIfMissing: true`), authoritative. Returns only INCREASE stock.
- Customer Ledger: `Customers.Balance` (locked) + `AccountLedgers` + `CustomerTransactions`.
- Accounting Journal: `CommerceAccountingService.PostPosRefundAsync` via `IPostingService`; journal id captured in outbox.
- Warehouse: `Warehouses` (active flag checked).
- Receipt: presentation only (frontend).
- Analytics: `ReportsController.GetSalesReturnsReport` aggregates from `Returns`/`ReturnItems`.

Projection (derivative; never authoritative):
- Mongo `ProductVariantStockChangedEvent` consumers (product/stock/variant projections, affiliate + commercehub projections).
- Elasticsearch product/stock search index (updated via the same stock-change event).
- Any read model built from `Returns` (reports, list views).

Evidence:
- `Return` is `ITenantOwned`; `SalesRequestIdempotency` is `ITenantOwned` and keyed by `(TenantId, Key, Operation)` with `Operation = "SALES_RETURN_CREATE"`.
- Outbox messages are idempotency-keyed (`REFUND:{TenantId}:{ReturnId}`).
- `Returns`/`ReturnItems` are listed in `ForbiddenBusinessDbSets.Names` — ledger-only services must not read them; financial truth is in the immutable ledger, not in `Returns`.
- Stock sync outbox rows (`StockSyncOutboxMessages`) are created but left un-published until AFTER commit (proven by concurrency tests: `PublishedAtUtc` is null right after the return).

Presentation:
- React pages `SalesReturns.jsx`, `SalesReturnReport.jsx` are operational surfaces only.
- Controllers expose: `GET /api/salesreturns`, `GET /api/salesreturns/{id}`, `GET /api/salesreturns/invoice/{invoiceId}`, `POST /api/salesreturns`, and the report `GET /api/reports/sales-returns` (`ReportsController.GetSalesReturnsReport`).

## Sales Return Lifecycle

Reverse-engineered from `SalesReturnsController.CreateSalesReturn` plus the outbox processor:

1. Request `POST /api/salesreturns` with optional `Idempotency-Key` (and optional `X-Request-Id`, `X-Payment-Attempt-Id`). Missing keys are auto-derived from a deterministic fingerprint (`AUTO:<hash>`). Legacy payload fields `refundFromCash`/`refundAmount` are rejected with 400.
2. Idempotency resolution: if both `Idempotency-Key` and `X-Request-Id` are supplied they must match. Fingerprint computed via `DeterministicRequestHasher`. If an existing completed record with same key but different fingerprint → 409; same fingerprint → replay the stored 200 response.
3. Idempotency claim: `SalesRequestIdempotency` row inserted ON CONFLICT DO NOTHING (Postgres) / `WHERE NOT EXISTS ... UPDLOCK,HOLDLOCK` (SQL Server), `(TenantId, Key, Operation)` unique, `Operation = "SALES_RETURN_CREATE"`. On conflict with a different hash → 409; on conflict with a completed response → replay; on conflict still in-flight → 409 "already being processed".
4. Open SQL transaction. Original sales invoice loaded with `FOR UPDATE` (Postgres) / `UPDLOCK, ROWLOCK, HOLDLOCK` (SQL Server) — pessimistic lock (SR-CON-01) so competing returns against the same invoice serialize and revalidate against committed history.
5. Validation gate (all within the transaction; rollback on failure):
   - `OriginalInvoiceId` required.
   - Invoice must exist, not deleted, and `Status == Completed` (only completed sales invoices can be returned).
   - Invoice must have items.
   - Invoice `TotalAmount > 0`.
   - Request `CustomerId` must equal invoice `CustomerId`.
   - Each item: product must have been sold on the invoice; variant (if not supplied, must be the single distinct variant on the invoice, else 400); unit must match the original sale unit; no "return more than sold" per (product, variant); cumulative returned base qty ≤ originally sold base qty; total refund ≤ original invoice total including prior returns.
6. Build original-invoice snapshots keyed by `(ProductId, EffectiveVariantId)`. If a product line has multiple/inconsistent pricing or cost snapshots → fail ("Process this return manually"). Client UnitPrice/TaxRate are compared and, if they differ, silently ignored (server values win) and logged.
7. Lock variant stock rows (`LockMultipleStocksAsync`, determinism order WarehouseId then VariantId, `CreateIfMissing: true`). Returns INCREASE stock — NO available-stock/negative-stock check on the return path (inventory restores, it never blocks).
8. For each line: compute prorated subtotal/discount/tax; INCREASE `ProductVariantStocks.Quantity`; build `StockMovement` (`SalesReturn`, positive `QuantityChange`); build pending stock-change event. Snapshot `CostPriceSnapshot`/`CostAmountSnapshot` on the line.
9. Reconcile header totals; enforce `TotalAmount > 0` and `priorReturnedTotal + newTotal ≤ originalInvoiceTotal`. Bump `originalInvoice.AccountingMutationVersion++`; capture `refundMutationVersion`.
10. Resolve refund allocation via `RefundAllocationSupport.Resolve(RefundMethod, CashAmount, CardAmount)`. Rules: Cash → positive cash, zero card; Card → positive card, zero cash; Mixed → positive both; AccountCredit → zero both. Cash/Card/Mixed MUST allocate the FULL calculated return total; AccountCredit routes the WHOLE total to customer credit. If payout < total and method is not AccountCredit → 400. If a credit is owed but no customer → 400.
11. If a customer credit is owed and `customer != null`: post `LedgerService.CreateLedgerEntry` (PartyType.Customer, TransactionType.SalesReturn, debit 0, credit `creditToCustomerAmount`) AND write a `CustomerTransaction` (credit, `BalanceAfter = customer.Balance` snapshot at write time).
12. Enqueue accounting outbox (`PosRefund` event, idempotency `REFUND:{TenantId}:{ReturnId}`, carries `refundMutationVersion`). The outbox write happens INSIDE the business transaction.
13. Write `AuditLogs` row (Create/SalesReturn) with full after-data JSON (amounts, method, items, paymentAttemptId, ignoredClientOverrides, isAutoCalculated).
14. Allocate `ReturnNumber`; `DocumentNumberingSupport.SaveReturnChangesWithRetryAsync`; save changes; store idempotency response (200) + payload.
15. Commit SQL transaction. THEN (after durability): `PublishCommittedStockEventsAsync` (Mongo/ES sync) and call `CommerceAccountingService.PostPosRefundAsync` SYNCHRONOUSLY. If sync post succeeds → mark outbox processed with `JournalId`. If sync post fails → log + outbox worker retries (return is already durable).
16. Return 200 with `SalesReturnResponse` (ReturnId, ReturnNumber, TotalAmount, RefundAmount, RefundMethod, CashAmount, CardAmount, CustomerCreditAmount, IsAutoCalculated=true).

Outbox worker path (if the sync post did not mark processed): `AccountingOutboxProcessor` dispatch → `ProcessPosRefundAsync` → load return + resolve allocation from payload → `PostPosRefundAsync` → journal posted → mark processed.

## Implementation Discovery

Frontend:
- `PointOfSale.Web/src/pages/SalesReturns.jsx` — create/list UI. Default refund method is `accountCredit`. Draft quantity is clamped to `originalQuantity`. Unit price and tax rate are locked to original values; any mismatch is blocked client-side. `buildIdempotencyHeaders` + `X-Payment-Attempt-Id` are attached to the POST.
- `PointOfSale.Web/src/pages/SalesReturnReport.jsx` — analytics report view.
- `App.jsx` routes include `/sales-returns`, `/sales-return-report`.

Controllers (API layer):
- `PointOfSale.API/Controllers/SalesReturnsController.cs` — the entire Sales Return capability lives here (no separate application service). Endpoints:
  - `GET /api/salesreturns` — paged list filtered by search/customerId/startDate/endDate, returns only `ReturnType == SalesReturn`.
  - `GET /api/salesreturns/{id}` — single return, `ReturnType == SalesReturn`.
  - `GET /api/salesreturns/invoice/{invoiceId}` — loads original `SalesInvoice` for the return UI.
  - `POST /api/salesreturns` — the mutation. `[Authorize]` only (no role restriction in this controller, unlike Purchase Returns which requires Owner/Admin/Manager).
  - `ReportsController.GetSalesReturnsReport` — analytics (date range default last 30 days; filters customer/warehouse/product/category/salesRep/paymentMethod).

Application Services:
- None dedicated. Controller injects: `ApplicationDbContext`, `ITenantContext`, `ILedgerService`, `IEventPublisher`, `IAccountingOutboxService`, `ICommerceAccountingService`.
- `LedgerService.CreateLedgerEntry` / `ApplyPartyTransactionAsync` — writes `AccountLedger` + updates `Customers.Balance` (locked FOR UPDATE). Customer credit only.
- `CommerceAccountingService.PostPosRefundAsync` — builds `PostingRequest` reversing revenue (Debit PosSales; Credit cashbox/VisaClearing/AccountsReceivable by method) + optional COGS reversal (Debit Inventory, Credit COGS) using `CalculateReturnCost`.
- `AccountingOutboxService.EnqueuePosRefundAsync` — inserts `AccountingOutboxMessages` row (event `PosRefund`).
- `ProductVariantStockMutationSupport` — `LockMultipleStocksAsync`, `BuildPendingStockChange`, `PublishCommittedStockEventsAsync` (stock lock + Mongo/ES event publishing).

Entities (SQL):
- `Return` (ITenantOwned): TenantId, ReturnNumber, ReturnDate, ReturnType, OriginalInvoiceId (SalesInvoice FK — the authoritative sales FK), OriginalInvoiceNumber, CustomerId?, WarehouseId, Subtotal, TaxAmount, TotalAmount, Reason?, Notes?, Items. (NB: `Return.OriginalPurchaseInvoiceId` exists too but stays null for sales returns.)
- `ReturnItem`: ReturnId, ProductId, ProductVariantId?, UnitId?, Quantity, ConversionFactor, BaseQuantity, UnitPrice, TaxRate, TaxAmount, TotalAmount, CostPriceSnapshot?, CostAmountSnapshot?, Reason?.
- `SalesRequestIdempotency` (ITenantOwned): TenantId, Key, Operation (`"SALES_RETURN_CREATE"`), RequestHash, ResponseStatusCode?, ResponseJson?, CompletedAtUtc?, ClaimToken?, ClaimTimestampUtc?.
- `SalesInvoice`: Status (InvoiceStatus), AccountingMutationVersion, Items, CustomerId, WarehouseId, PaymentMethod.
- `Customer`, `Warehouse`, `ProductVariantStock`, `StockMovement`, `AccountLedger`, `CustomerTransaction`, `AccountingOutboxMessages`.

Database Tables:
- `Returns`, `ReturnItems` (FK ReturnItems→Returns ON DELETE CASCADE), `SalesRequestIdempotency` (unique on TenantId+Key+Operation), `SalesInvoices` (now has `AccountingMutationVersion`), `ProductVariantStocks`, `StockMovements`, `AccountLedgers`, `CustomerTransactions`, `AccountingOutboxMessages`, `StockSyncOutboxMessages` (Mongo sync).
- `StockMovement` rows for returns use `StockMovementType.SalesReturn`.
- `AccountLedger` / `CustomerTransaction` rows for the credit leg use `TransactionType.SalesReturn`.

Repositories / Integration:
- EF Core `ApplicationDbContext` directly (no repository abstraction for the mutation).
- Warehouse Integration: warehouse existence + active check; variant stock held in `ProductVariantStocks` keyed by (WarehouseId, VariantId), restored on return.
- Customer Integration: customer match (request CustomerId == invoice CustomerId) + balance via `LedgerService` for the credit leg.
- Inventory Integration: `ProductVariantStocks.Quantity` INCREASED; `StockMovement` recorded (positive change). NO reservation/available-stock gate on the return path.
- Accounting Integration: outbox → `CommerceAccountingService.PostPosRefundAsync` posts journal reversing revenue (Debit PosSales; Credit cashbox/Visa/AR) + optional COGS/inventory reversal.
- Outbox: `AccountingOutboxMessages` (event `PosRefund`, idempotency `REFUND:{TenantId}:{ReturnId}`, payload {ReturnId, OriginalInvoiceId, RefundMethod, CashAmount, CardAmount, MutationVersion}).
- MassTransit: `ProductVariantStockChangedEvent` published after commit → Mongo + ES consumers.
- Mongo: `StockSyncOutboxMessages` → variant/product projections.
- Redis: cache for product/stock read models (derivative), not authoritative.
- Elasticsearch: product/stock search index updated by the stock-change event.

## Business Rules

All verified in `SalesReturnsController.CreateSalesReturn` / `FinalizeSalesReturnAsync` and tests:

1. Idempotency is enforced via `SalesRequestIdempotency` (key + fingerprint). If `Idempotency-Key` and `X-Request-Id` both supplied they must match. Missing → auto `AUTO:<hash>` key. Replay with same key+fingerprint returns stored 200; different fingerprint → 409; in-flight → 409.
2. Original sales invoice required; must exist (not deleted) and `Status == Completed`. Drafts/Cancelled invoices cannot be returned.
3. Request `CustomerId` MUST equal the invoice's `CustomerId` ("Customer must match the original invoice").
4. Invoice total must be > 0; invoice must have items.
5. Each returned product must have been SOLD on the original invoice (matched by ProductId); variant must have been sold (if not supplied, must resolve to the single distinct variant on the invoice, else 400); unit must match a sold unit.
6. No "return more than sold": cumulative returned base quantity for (invoice, product, variant) + this request ≤ originally sold base quantity → 400 "Cannot return more than sold quantity".
7. Total refund bound: `priorReturnedTotal + newTotal ≤ originalInvoiceTotal` → 400 "Calculated refund total exceeds the original invoice total after prior returns".
8. Computed net subtotal cannot go negative (per line) → 400. Calculated return total must be > 0 → 400.
9. Refund allocation rules (`RefundAllocationSupport`): Cash → positive cash + 0 card; Card → positive card + 0 cash; Mixed → both positive; AccountCredit → both 0. Cash/Card/Mixed allocators MUST allocate the FULL `return.TotalAmount` to payout; otherwise 400. AccountCredit routes the entire total to customer credit.
10. If a customer credit remains (`TotalAmount - approvedPayout > 0`) and there is no customer (walk-in, null) → 400 "A customer account is required when the refund is not fully paid out immediately".
11. Variant-centric: stock is RESTORED to `ProductVariantStocks`; `ProductVariantId` effectively mandatory (inferred when unique); `BaseQuantity = Quantity * ConversionFactor`. Stock rows are created if missing (`CreateIfMissing: true`).
12. Inventory restore NEVER checks available stock / never blocks on availability — returns only add stock. This is the inverse of a sale/purchase-return which deduct stock and gate on availability.
13. Client UnitPrice/TaxRate are authoritative-overridden: the server recomputes from the original invoice line. Any client override is ignored, logged (warning), and listed in `ignoredClientOverrides` in the audit JSON. The refund is "system-calculated" (`IsAutoCalculated = true`).
14. Pricing/cost snapshot consistency: if an invoice line has multiple inconsistent per-base-unit price/discount/tax/cost snapshots → 400 "Process this return manually". Cost basis for the return line comes from the original `SalesInvoiceItem.CostPriceSnapshot`/`CostAmountSnapshot` (prorated), falling back to variant/product cost.
15. `SalesInvoice.AccountingMutationVersion` is incremented once per committed return (monotonic, auditable downstream). The invoice `Status` is NOT changed.
16. Concurrency serialization: original invoice locked pessimistically (SR-CON-01); concurrent returns against the same invoice are serialized; overlapping uncommitted state is invisible to other readers.
17. Fail-closed on validation/data: any validation failure or unexpected exception rolls back the transaction (500 on unexpected). Sync accounting post failure does NOT fail the return (return already committed) — logged, outbox retries.
18. Rounding: currency rounded to 2dp (MidpointRounding.AwayFromZero); per-base-unit rates to 6dp.
19. Max 50 items per return request (`MAX_SALES_RETURN_ITEMS`). `RefundMethod` is required on the request; legacy `refundFromCash`/`refundAmount` payload fields are rejected.
20. The `Return` row is immutable once committed — there is no update or delete endpoint in this controller.

## Sales Return State Machine

There is NO explicit state machine on the `Return` entity (no Status column). The discoverable lifecycle is expressed through outbox processing and the parent invoice version:

Completed Sales Invoice
  ↓ (POST /api/salesreturns, validated + stock restored + ledger credit + outbox enqueued)
Return Committed (Returns row written, invoice AccountingMutationVersion++)
  ↓ (cumulative returned base qty reaches sold base qty per line — "full return" condition, derived)
Full Return Reached (no status change on the row; cumulative quantity bound only)
  ↓ (outbox processed → journal posted)
Accounting Posted (PosSales debited; cashbox/Visa/AR credited; optional Inventory debited / COGS credited)

Outbox sub-states (per message): pending → (skipped if stale/legacy) → processed (JournalId set). A stale message (mutationVersion mismatch) is skipped, not errored.

InvoiceStatus enum (authoritative): Draft, Completed, Cancelled, PartiallyReturned, FullyReturned. Sales returns do NOT use PartiallyReturned/FullyReturned — those are Purchase Return lifecycle states. The sales invoice stays `Completed` after a return.

There is no Rejected/Cancelled state for a sales return itself. Once committed, a sales return is immutable.

## Return Calculation

Prorated from the original invoice line(s) in `FinalizeSalesReturnAsync` / `CreateSalesReturnInternal`:

For each returned line:
- Resolve matching original invoice items by `(ProductId, EffectiveVariantId)`.
- `baseQuantity = itemRequest.Quantity * conversionFactor` (conversionFactor from the matched original unit; default 1).
- `totalReturnedBaseQuantity` = sum of all prior `ReturnItem.BaseQuantity` for (invoice, product, variant) + this request's base quantity. Must be ≤ `originalSoldBaseQuantity`.
- `originalInvoiceSubtotal = Σ originalInvoice.Subtotal`; `priorReturnedGrossSubtotal` = prior returns' gross; `invoiceDiscountShares` = AllocateAmountAcrossLines of the remaining invoice discount across current lines (`CalculateCumulativeProrated`).
- Per line: `grossSubtotal = Round(UnitPrice * Quantity)`; `lineDiscount = cumulative prorated discount`; `lineTax = cumulative prorated tax`; `netSubtotal = Round(grossSubtotal - lineDiscount - invoiceDiscountShare)`; `totalAmount = Round(netSubtotal + lineTax)`.
- `ReturnItem.CostPriceSnapshot = snapshot.CostPriceSnapshot`; `CostAmountSnapshot = Round(CostPriceSnapshot * baseQuantity)`.
- Header totals: `Subtotal = Round(Σ netSubtotals)`, `TaxAmount = Round(Σ line taxes)`, `TotalAmount = Round(Subtotal + TaxAmount)`.

Refund allocation:
- `refundAllocation` from `RefundMethod` + CashAmount + CardAmount (resolved only after `TotalAmount` is known).
- `approvedPayoutAmount = CashAmount + CardAmount`.
- `creditToCustomerAmount = Round(TotalAmount - approvedPayoutAmount)`.
  - AccountCredit → `creditToCustomerAmount = TotalAmount` (method asserts payout 0).
  - Cash/Card/Mixed → `creditToCustomerAmount = 0` and payout MUST equal TotalAmount else 400.

IMPORTANT report caveat (implementation-backed): `ReportsController.GetSalesReturnsReport` computes `CostOfReturnedItems = item.BaseQuantity * item.Product.PurchasePrice` using the *current* product `PurchasePrice`, NOT the immutable `CostAmountSnapshot` stored on the return line. After later purchases change `Product.PurchasePrice`, the report's cost-reversed figure diverges from the authoritative return snapshot (`ReturnItem.CostAmountSnapshot`). The authoritative figure is `CostAmountSnapshot`.

The report also hardcodes `DiscountReturned = 0` and treats `NetReturnAmount == GrossReturnAmount == item.TotalAmount` (the per-line discount is not separately surfaced in the report DTO).

## Refund Behavior

Reverse-engineered from `RefundAllocationSupport` + `FinalizeSalesReturnAsync` + `PostPosRefundAsync`:

- AccountCredit: entire `TotalAmount` becomes a customer-ledger credit. No cash/card payout. `creditToCustomerAmount = TotalAmount`; `CustomerTransaction` + `AccountLedger` written with credit = TotalAmount, debit 0. Default method on the frontend.
- Cash: payout = TotalAmount via warehouse cashbox. `CashAmount = TotalAmount`, `CardAmount = 0`. Accounting: Credit warehouse cashbox account (resolved per-warehouse via `ICashboxLedgerAccountService`).
- Card: payout = TotalAmount via Visa clearing. `CardAmount = TotalAmount`, `CashAmount = 0`. Accounting: Credit `AccountCodes.VisaClearing`.
- Mixed: payout = TotalAmount split between cash and card (both positive). Accounting: Credit cashbox + Credit VisaClearing.
- Partial payout (cash/card/mixed) < TotalAmount is NOT allowed — those methods must allocate the full total; the remainder would have to be AccountCredit, which is a distinct method. So effectively a single return is either fully paid-out OR fully customer-credit; there is no "partial payout + partial credit" hybrid in one return beyond what AccountCredit itself expresses (AccountCredit = full credit, payout 0).
- Customer credit leg (when `creditToCustomerAmount > 0`): requires a real customer (invoice `CustomerId` set). Posts `TransactionType.SalesReturn` credit to `AccountLedger` and `CustomerTransaction`.
- Rounding: currency 2dp AwayFromZero.
- Idempotency of the journal: `REFUND:{TenantId}:{ReturnId}` exactly once — the synchronous post marks the outbox processed; the outbox worker is the retry path.

## Accounting

Reverse-engineered from `CommerceAccountingService.PostPosRefundAsync`:

- Revenue reversal journal (always): `PostingRequest` SourceType "Return", IdempotencyKey `REFUND:{TenantId}:{ReturnId}`:
  - Debit `AccountCodes.PosSales` for `return.TotalAmount`.
  - Credit the warehouse cashbox account (per-warehouse) for `CashAmount` (if > 0).
  - Credit `AccountCodes.VisaClearing` for `CardAmount` (if > 0).
  - Credit `AccountCodes.AccountsReceivable` for `customerCreditAmount` (if > 0) — mirrors the customer-ledger credit.
- COGS/inventory reversal journal (only if `returnCost > 0`, i.e. cost snapshots exist): SourceType "Return_COGS", IdempotencyKey `REFUND_COGS:{TenantId}:{ReturnId}`:
  - Debit `AccountCodes.Inventory` for `returnCost`.
  - Credit `AccountCodes.CostOfGoodsSold` for `returnCost`.
- `returnCost = CalculateReturnCost(return)` = Σ `ReturnItem.CostAmountSnapshot` (fallback to variant/product cost * base qty). This RESTORES inventory value — the inverse of the sale's COGS journal.
- Mutation version: `refundMutationVersion` (the bumped `SalesInvoice.AccountingMutationVersion`) is carried on the outbox message for downstream ordering/replay.
- Idempotency: journal keys are deterministic; the posting service de-dupes on `IdempotencyKey`. The outbox row is marked processed with the resulting `JournalId` (synchronous path) or by the worker (retry path).
- Fail-open on accounting: if the synchronous `PostPosRefundAsync` fails, the return is already committed; the failure is logged and the outbox worker retries. A return can therefore exist without a journal until the worker catches up.

## Cross-System Effects

Sales:
- Original `SalesInvoice` amounts/payments are NOT changed. Only `AccountingMutationVersion` increments. `Status` stays `Completed` (contrast with Purchase Return flipping to PartiallyReturned/FullyReturned).
- A sales return is independent of the original sale's lifecycle; multiple returns can reference one completed invoice up to the sold quantity.

Inventory / Products:
- `ProductVariantStocks.Quantity` INCREASES by base quantity (per warehouse/variant, locked). Returns never reduce stock.
- `StockMovement` row of type `SalesReturn` appended (positive `QuantityChange`, `QuantityAfter` snapshot; `Price` = line `CostPriceSnapshot`).
- No `Product` average-cost re-weighting happens on sales return (contrast with Purchase Return, which re-weights `PurchasePrice`). Sales return only restores stock quantity + (optionally) posts the COGS/inventory journal.

Warehouse:
- No warehouse mutation other than the variant-stock INCREASE in the (invoice-mandated) warehouse. Warehouse must be active.

Customers:
- `Customers.Balance` changed only when there is a customer credit (`creditToCustomerAmount > 0`). `AccountLedger` + `CustomerTransaction` (TransactionType.SalesReturn) written. Walk-in (null customer) returns skip the ledger entirely.

Payments / Treasury:
- The journal credits the per-warehouse cashbox (cash) or Visa clearing (card). Physical cash disbursement is the till's domain. Unresolved captured-payment authority still governs outbound money; a sales return does not itself execute a provider refund.
- No direct payment record/refund-payment entity is written by this capability.

Accounting:
- Outbox message `PosRefund` enqueued; processor posts journal(s): Debit PosSales; Credit cashbox/Visa/AR; optional Debit Inventory / Credit COGS. Journal id stored on outbox.

Analytics / Notifications:
- Report endpoint aggregates from `Returns`/`ReturnItems`. No notification/email/WhatsApp is sent by this capability.

Search:
- Elasticsearch product/stock index updated via the `ProductVariantStockChangedEvent` published post-commit (derivative).

Identity:
- TenantId enforced via `ITenantContext.RequiredTenantId` on every write; cross-tenant leak prevented by TenantId scoping + unique idempotency key per tenant.

CommerceHub / Affiliate / Storefront / Purchase:
- Unaffected directly. `ReturnType` discriminator keeps sales vs purchase returns separate; shared `Return`/`ReturnItem` tables but sales returns key off `OriginalInvoiceId` (SalesInvoice FK) while purchase returns use `OriginalPurchaseInvoiceId`. CommerceHub returns (`CommerceHubReturnRequest`, `ReturnManagementService`, `CommerceHubReturnStatus`) are a different domain.

## Validation Checklist

After return created (committed):
- `Returns` row exists with `ReturnType=SalesReturn`, correct TenantId, allocated `ReturnNumber`.
- `ReturnItems` count matches request; `BaseQuantity = Quantity * ConversionFactor`; `CostPriceSnapshot`/`CostAmountSnapshot` set.
- `SalesInvoice.AccountingMutationVersion` incremented; `SalesInvoice.Status` still `Completed`.
- `SalesRequestIdempotency` row for `(TenantId, Key, "SALES_RETURN_CREATE")` has `ResponseStatusCode=200` + `ResponseJson`.

After inventory restored:
- `ProductVariantStocks.Quantity` INCREASED by total base quantity; `StockMovement` (`SalesReturn`, positive change) written.
- Mongo `StockSyncOutboxMessages` row exists (published after commit) for the variant.

After customer ledger update (credit leg only):
- `CustomerTransaction` (SalesReturn, credit = `creditToCustomerAmount`) written; `AccountLedger` (customer credit) written; `Customers.Balance` increased by the credit.

After refund allocation:
- `approvedPayoutAmount` (Cash+Card) + `creditToCustomerAmount` = `TotalAmount`. Cash/Card/Mixed pay the full total; AccountCredit pays 0 and credits the full total.

After accounting reversal:
- `AccountingOutboxMessages` row for event `PosRefund`, idempotency `REFUND:{TenantId}:{ReturnId}`, `ProcessedAtUtc` + `JournalId` set (eventually). Journal(s): Debit PosSales; Credit cashbox/Visa/AR; optional Debit Inventory / Credit COGS.

After audit:
- `AuditLogs` row (Create/SalesReturn) present with after-data JSON incl. ignoredClientOverrides, amounts, method, items, paymentAttemptId, isAutoCalculated.

## Common Failure Scenarios

1. Return exceeds sold quantity → 400 "Cannot return more than sold quantity". Cause: cumulative `BaseQuantity` > originally sold. Check prior `ReturnItems` for the invoice/product/variant.
2. Refund total exceeds invoice total (incl. prior returns) → 400 "Calculated refund total exceeds the original invoice total after prior returns". Check cumulative prior returns.
3. Negative computed subtotal → 400 "Computed refund subtotal became negative". Check discount/invoice-discount proration on the original invoice.
4. Customer mismatch → 400 "Customer must match the original invoice". The return must be for the same customer as the sale.
5. Invoice not Completed (Draft/Cancelled) → 400 "Only completed sales invoices can be returned".
6. Product/variant/unit not on original invoice → 400. Check the original `SalesInvoiceItems`.
7. Duplicate idempotency key, different fingerprint → 409. Re-submit with a fresh key or identical payload.
8. Legacy refund payload (`refundFromCash`/`refundAmount`) → 400 "Legacy refund payload fields ... no longer supported".
9. Cash/Card/Mixed that does not allocate the full total → 400 "Cash, card, and mixed refunds must allocate the full calculated return total." Use AccountCredit for non-payout.
10. Credit owed but no customer → 400 "A customer account is required when the refund is not fully paid out immediately". A walk-in return must be fully paid out (cash/card/mixed) or credit is impossible.
11. Accounting post fails after commit → return is still committed; error logged; outbox worker retries. Symptom: return exists but no journal. Check `AccountingOutboxMessages` (event `PosRefund`).
12. Inconsistent pricing/cost snapshot on the invoice line → 400 "Process this return manually". The line has multiple incompatible per-base-unit snapshots.
13. Return "stuck" (uncommitted) → externally unobservable because uncommitted state is invisible until commit (serialized + isolated). If a request hangs, it is a lock/contention issue on the original invoice row (SR-CON-01).

Investigation order (where Hermes looks first):
1. `SalesRequestIdempotency` (was it claimed/completed? what status code? key+fingerprint).
2. `Returns` + `ReturnItems` (was the return actually written? amounts correct? cost snapshots?).
3. `SalesInvoices` (`AccountingMutationVersion`, `Status`) — did the parent invoice bump version but stay Completed?
4. `ProductVariantStocks` + `StockMovements` (was stock restored/increased? movement present?).
5. `CustomerTransactions` + `AccountLedgers` + `Customers.Balance` (credit leg applied?).
6. `AccountingOutboxMessages` (event `PosRefund`) → journal posted? `JournalEntries`/`LedgerEntries`.
7. Mongo `StockSyncOutboxMessages` + ES index (projection lag).

## Debugging Guide

Start at `SalesReturnsController.CreateSalesReturn` (single method + `FinalizeSalesReturnAsync` own the whole capability).

- When to check Original Invoice: wrong invoice, not Completed, version drift, total bound exceeded. Loaded via `LoadOriginalInvoiceForReturnMutationAsync` (pessimistic lock, SR-CON-01). Note: `Status` does NOT change on a sales return — only `AccountingMutationVersion` increments.
- When to check Inventory: stock not restored/increased, missing `StockMovement` (`SalesReturn`), wrong `QuantityAfter`. Returns only ADD stock; there is no available-stock gate. Use `ProductVariantStockMutationSupport`.
- When to check Customer Ledger: credit not posted, missing `CustomerTransaction`/`AccountLedger`, balance not increased. Only applies when `creditToCustomerAmount > 0` and a customer exists. `LedgerService.CreateLedgerEntry` (TransactionType.SalesReturn).
- When to check Accounting: no journal, wrong debit/credit accounts, outbox not processed. `CommerceAccountingService.PostPosRefundAsync` posts Debit PosSales / Credit cashbox-or-Visa-or-AR (+ optional Debit Inventory / Credit COGS). Verify `AccountingOutboxMessages` (`PosRefund`, idempotency `REFUND:{TenantId}:{ReturnId}`) and `JournalEntries`.
- When to check Refund: payout vs credit split wrong, method mismatch. `RefundAllocationSupport.Resolve` enforces the method rules; `FinalizeSalesReturnAsync` enforces full allocation for Cash/Card/Mixed.
- When to check Outbox: `AccountingOutboxMessages` event `PosRefund`, idempotency `REFUND:{TenantId}:{ReturnId}`. Stale/legacy messages are skipped (mutationVersion). Failed messages retried by `AccountingOutboxProcessor`.
- When to check Projection (Mongo/ES): only after SQL truth is confirmed correct. Stock sync driven by `ProductVariantStockChangedEvent` published post-commit; lag here does not affect financial truth.

Key invariant: `ProductVariantStocks` is SQL-authoritative and INCREASED by returns (never gated). `Returns`/`ReturnItems` are SQL-authoritative but live in `ForbiddenBusinessDbSets` — ledger-only services must read the immutable ledger, not `Returns`. Mongo/ES/Redis are derivative — rebuild from SQL, never trust them as source.

## Related Knowledge

Core Guides (implementation, not docs):
- `SalesReturnsController.cs` (entire capability), `Return.cs`, `ReturnItem.cs`, `SalesRequestIdempotency.cs`, `RefundAllocation.cs`, `LedgerService.cs`, `CommerceAccountingService.PostPosRefundAsync`, `AccountingOutboxService.EnqueuePosRefundAsync`, `AccountingOutboxProcessor.ProcessPosRefundAsync`, `ProductVariantStockMutationSupport`, `DocumentNumberingSupport`.

System Guides:
- Sales / Sales Invoice authority; Customer ledger & SOA; Accounting outbox & journal posting; Inventory (variant stock) authority; Multi-tenancy (ITenantContext); Cashbox ledger accounts (per-warehouse).

Related Capabilities:
- Sales Management (`SalesController`, shares `SalesInvoice` read + `AccountingMutationVersion` bump; distinct from return accounting).
- Purchase Return Management (`ReturnType.PurchaseReturn`, separate controller; deducts stock, re-weights product cost, flips invoice status — the inverse in several ways).
- Customer / Statement of Account (ledger).
- CommerceHub Returns (`ReturnManagementService`, `CommerceHubReturnStatus`, `PosAdminReturnsController`) — marketplace/vendor returns, NOT POS sales returns.

Related Domains:
- Inventory, Warehouse, Customer, Accounting/Ledger, Payments (separate authority), Search (ES), Product projections (Mongo).

## Documentation Validation

Findings (implementation-backed, vs. any docs/README which are treated as stale):

- MISSING documentation: No separate application service exists for sales returns — the entire capability lives in `SalesReturnsController`. Docs implying a "Sales Return Service" would be wrong.
- INCORRECT assumption: There is NO Sales Return status state machine. The `Return` entity has no Status column. Lifecycle is expressed only via outbox processing and the parent invoice `AccountingMutationVersion`. Any doc showing Return states (Created/Completed/Rejected/Cancelled) is incorrect for sales returns.
- INCORRECT authority (vs Purchase Return): A Sales Return does NOT flip `SalesInvoice.Status` to PartiallyReturned/FullyReturned. The invoice stays `Completed`. Only `AccountingMutationVersion` increments. Purchase Return transitions invoice status; Sales Return does not. Conflating the two is a real bug risk.
- INCORRECT assumption (refund): Sales returns DO support real refunds — AccountCredit, Cash, Card, Mixed. The refund allocation is enforced (Cash/Card/Mixed must allocate the full total; AccountCredit credits the whole amount to the customer ledger). This is richer than Purchase Return, which has no cash refund.
- INCORRECT authority (inventory direction): Sales returns RESTORE (increase) variant stock. This is the inverse of a sale and the inverse of a Purchase Return (which deducts stock). Returns never gate on available stock.
- INCORRECT authority (product cost): Sales returns do NOT re-weight `Product.PurchasePrice`. Purchase Return does. Do not assume sales return touches product average cost.
- HIDDEN behavior: Client-supplied `UnitPrice`/`TaxRate` are AUTHORITATIVE-OVERRIDDEN — the server recomputes from the original invoice line, ignores client values, logs a warning, and records `ignoredClientOverrides` in the audit JSON. The refund is always `IsAutoCalculated = true`.
- HIDDEN behavior: Accounting posting is dual-path — synchronous post right after commit (marks outbox processed) OR outbox worker retry. A failed sync post does NOT fail the return; the return is already durable. Fail-open-on-accounting (but fail-closed-on-data).
- HIDDEN behavior: Stock sync to Mongo/ES happens only AFTER the SQL commit, and the `StockSyncOutboxMessages` row is left un-published (`PublishedAtUtc=null`) until the post-commit publish — so uncommitted returns are invisible to projections.
- HIDDEN behavior: Sales returns require a real customer ONLY when a credit is owed. Walk-in (null customer) returns must be fully paid out (cash/card/mixed) or they 400.
- INCONSISTENCY (report vs. return): `GetSalesReturnsReport` computes `CostOfReturnedItems = item.BaseQuantity * item.Product.PurchasePrice` using the *current* product purchase price, while the return line stores an immutable `CostAmountSnapshot = BaseQuantity * original cost`. After later purchases change `PurchasePrice`, the report's cost-reversed figure diverges from the authoritative return snapshot. The authoritative figure is `ReturnItem.CostAmountSnapshot`.
- INCONSISTENCY: `SalesReturnReportDto.DiscountReturned` is hardcoded to 0 and `NetReturnAmount == GrossReturnAmount == item.TotalAmount`. Per-line discount is not separately surfaced in the report.
- ARCHITECTURE: `Returns`/`ReturnItems` are in `ForbiddenBusinessDbSets.Names` — financial reporting must read from the immutable ledger, not from `Returns`. Confirmed by `ForbiddenBusinessDbSets`.
- AUTHORITY split: Sales returns use `OriginalInvoiceId` (SalesInvoice FK); `OriginalPurchaseInvoiceId` stays null. Purchase returns use `OriginalPurchaseInvoiceId` (PurchaseInvoice FK); `OriginalInvoiceId` stays null. Mixing these up is a real bug risk on the shared `Return`/`ReturnItem` tables.
- CommerceHub returns (`PosAdminReturnsController`, `CommerceHubReturnStatus` with PickupScheduled/PickedUp/InTransit/DeliveredToVendor/Confirmed/Failed) are a DIFFERENT capability and must not be conflated with POS sales returns.

## Quality Requirements — Coverage Confirmation

This document allows Hermes to:
- Understand Sales Return Management (purpose, scope, authority boundaries).
- Navigate the implementation (`SalesReturnsController` is the single owning file; plus LedgerService, CommerceAccountingService, AccountingOutbox{Service,Processor}, ProductVariantStockMutationSupport, RefundAllocation).
- Execute return workflows (POST /api/salesreturns with Idempotency-Key; required fields; prorated calculation; refund method rules).
- Validate return behavior (checklist above; invoice version, stock restore, ledger credit, outbox, journal).
- Debug return issues (ordered investigation path; common failures).
- Understand inventory restoration (variant stock INCREASE, no availability gate, StockMovement of type SalesReturn).
- Understand customer ledger behavior (credit leg only when a credit is owed and a customer exists; AccountLedger + CustomerTransaction).
- Understand refund behavior (AccountCredit/Cash/Card/Mixed; full-allocation rule; credit remainder).
- Understand accounting reversal (outbox → Debit PosSales / Credit cashbox-Visa-AR; optional Debit Inventory / Credit COGS).
- Understand authority boundaries (SQL truth vs Mongo/ES/Redis projections; ForbiddenBusinessDbSets; invoice stays Completed).

---

## Output Footer (task-mandated)

- Summary of discoveries: Sales Return is an idempotent, concurrency-serialized, single-transaction capability owned entirely by `SalesReturnsController.CreateSalesReturn`/`FinalizeSalesReturnAsync`. It reverses a completed POS sale by (1) validating invoice/status/customer/product/variant/unit/quantity/total bounds against a pessimistic-locked original invoice, (2) locking and INCREASING variant stock + writing a `StockMovement` (SalesReturn) + restoring inventory value in the COGS journal, (3) posting a customer-ledger credit + `CustomerTransaction` when money is not paid out immediately, (4) enqueuing an accounting outbox that reverses revenue (Debit PosSales; Credit cashbox/Visa/AR) and optionally reverses COGS (Debit Inventory; Credit COGS), and (5) bumping the invoice `AccountingMutationVersion` while leaving `Status = Completed`. Mongo/ES sync happens post-commit via `ProductVariantStockChangedEvent`. The refund is always system-calculated; client UnitPrice/TaxRate are overridden by server invoice values.

- Sales Return lifecycle discovered: Request(+Idempotency-Key) → Idempotency claim → Open tx + lock invoice (FOR UPDATE) → Validate (status/customer/product/variant/unit/qty/total) → Build snapshots + Lock variant stock → Restore stock + StockMovement + cost snapshot → Resolve refund allocation + customer-ledger credit → Enqueue accounting outbox + bump invoice version → Commit → Publish stock event + sync accounting post → (outbox worker retry if needed) → Journal posted.

- Sales Return state machine discovered: No state column on `Return`. Observable states via outbox: committed (Returns row written, invoice `AccountingMutationVersion++`, Status stays Completed) → accounting pending → accounting processed (JournalId set; or skipped if stale). Not Rejected/Cancelled (immutable once committed). `SalesInvoice.Status` does NOT transition (contrast with Purchase Return).

- Business rules discovered: mandatory idempotency (key+fingerprint, AUTO fallback); original invoice required & `Completed`; request CustomerId == invoice CustomerId; product/variant/unit must be on the invoice; no return-more-than-sold (cumulative base qty ≤ sold); total refund ≤ invoice total incl. prior returns; net subtotal/total > 0; refund method rules (Cash→+cash/0 card, Card→+card/0 cash, Mixed→both +, AccountCredit→both 0; Cash/Card/Mixed must allocate full total); customer credit requires a real customer; variant-centric stock RESTORE (CreateIfMissing, no availability gate); cost snapshotted from original line; invoice `AccountingMutationVersion++` only (Status unchanged); pessimistic invoice lock serializes concurrent returns; fail-closed on validation, fail-open on post-commit accounting; 2dp/6dp rounding; max 50 items; legacy refund fields rejected; immutable Return row.

- Refund behavior discovered: AccountCredit = full customer-ledger credit (payout 0); Cash = full payout via per-warehouse cashbox; Card = full payout via Visa clearing; Mixed = full payout split cash+card. Cash/Card/Mixed MUST allocate the entire calculated return total; AccountCredit routes the whole total to credit. A single return is either fully paid-out or fully customer-credit. Customer credit leg posts `TransactionType.SalesReturn` to `AccountLedger`+`CustomerTransaction` only when a credit is owed and a customer exists.

- Accounting behavior discovered: outbox `PosRefund` (idempotency `REFUND:{TenantId}:{ReturnId}`) → journal(s): Debit PosSales for TotalAmount; Credit warehouse cashbox (CashAmount) / Credit VisaClearing (CardAmount) / Credit AccountsReceivable (customerCreditAmount); optional second journal (when cost snapshots exist) Debit Inventory / Credit COGS for `returnCost = Σ CostAmountSnapshot`. `refundMutationVersion` carried for ordering. Dual-path post (sync + outbox retry); failure does not fail the return.

- Cross-system dependencies discovered: Sales (invoice version bump only; Status unchanged; amounts/payments untouched), Inventory (variant stock RESTORED + positive StockMovement; no product cost re-weight), Warehouse (active check + stock location), Customers (ledger credit + transaction + balance when credit owed), Accounting (outbox → journal), Mongo/ES (post-commit stock event), Identity (TenantId scoping). Payments/treasury: journal credits cashbox/Visa only; no provider refund executed. CommerceHub/Affiliate/Storefront/Purchase unaffected.

- Documentation inconsistencies found: no separate return service exists; no return status state machine; Sales Return does NOT change SalesInvoice.Status (Purchase Return does — easy to conflate); Sales Return restores (increases) stock, never gates on availability (inverse of sale/purchase-return); Sales Return does NOT re-weight Product.PurchasePrice (Purchase Return does); client UnitPrice/TaxRate are authoritatively overridden; report's CostOfReturnedItems uses live Product.PurchasePrice not the stored CostAmountSnapshot; DiscountReturned hardcoded 0; Returns/ReturnItems in ForbiddenBusinessDbSets; OriginalInvoiceId vs OriginalPurchaseInvoiceId discriminator split; CommerceHub returns conflated with sales returns.

- Hidden implementation behavior found: dual-path accounting post (sync + outbox retry) that does not fail the return on post failure; stock sync only after commit with un-published outbox row; system-calculated refund with client price overrides ignored+logged; customer required only when a credit is owed; SR-CON-01 pessimistic invoice lock serializes concurrent returns; Return row immutable (no PUT/DELETE).

- Confirmation that only this file was created: TRUE. Only `.ai/hermes/capabilities/pos-admin/sales-return-management.md` was created.

- Confirmation that no other files were modified: TRUE. No existing file was patched, edited, or deleted.

- Confirmation that no memory writes occurred: TRUE. No memory tool calls were made.
