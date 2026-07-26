# Purchase Return Management

Reverse-engineered from implementation. PostgreSQL is the authoritative source of truth; Mongo, Redis, Elasticsearch, MassTransit projections are derivative.

## Purpose

Purchase Return Management reverses goods previously received from a supplier against a completed purchase invoice. It exists to:

- Correct over-receipt, defect, or wrong-supply situations with a supplier without touching the original purchase ledger beyond a status/version change.
- Reduce on-hand warehouse stock for the returned variant(s).
- Reduce what the business owes the supplier (supplier payable/receivable balance).
- Post a correlated accounting journal (debit supplier payables, credit inventory).
- Provide an auditable, idempotent, concurrency-safe record of goods sent back to a vendor.

What it owns:
- The `Return` row (ReturnType = PurchaseReturn) and its `ReturnItem` lines.
- The purchase-return stock deduction from `ProductVariantStocks` (variant-centric, SQL-authoritative).
- The `StockMovement` rows of type `PurchaseReturn`.
- The supplier ledger entry + `SupplierTransaction` recording the payable reduction.
- The accounting outbox message + journal posting (debit SupplierPayables / credit Inventory).
- The idempotency record in `PurchaseReturnRequestIdempotency`.

What it does NOT own:
- The original purchase invoice amounts or payments (it only reads them and flips the invoice `Status` between PartiallyReturned / FullyReturned and bumps `AccountingMutationVersion`).
- Actual cash disbursement to the supplier (the journal posts a payable reduction; outbound payment is a separate Payment lifecycle — see Shopixy rules: unresolved captured-payment blocks outward money).
- Sales returns, CommerceHub customer/vendor returns, or marketplace returns (separate `ReturnType.SalesReturn`, `CommerceHubReturnRequest`, `ReturnManagementService`/`CommerceHubReturnStatus`).
- Inventory valuation truth for *other* tenants; this is strictly TenantId-scoped.

## Business Scope

Implemented and owned by Purchase Return:
- Purchase Return (full entity, `ReturnType.PurchaseReturn`).
- Partial Return (one invoice may absorb multiple returns until received quantity exhausted).
- Full Return (invoice transitions to `FullyReturned`).
- Return Validation (invoice exists, not Cancelled/FullyReturned, supplier+warehouse match, product was on invoice, variant on invoice, unit on invoice, quantity not exceeded, stock available).
- Supplier Validation (supplier must match original invoice; supplier row must still exist).
- Warehouse Validation (warehouse must match original invoice; warehouse must be active).
- Return Quantity Validation (base-unit quantity against cumulative returned quantity per product+variant on the invoice, and available variant stock).
- Inventory Reversal (variant stock decremented; product average `PurchasePrice` re-weighted; `StockMovement` written).
- Supplier Balance Reversal (supplier ledger debit of return total; `SupplierTransaction` written; supplier.Balance reduced).
- Accounting Reversal (outbox → journal: Debit SupplierPayables, Credit Inventory).
- Stock Movement (explicit `StockMovement` of `PurchaseReturn` type).
- Return Reason / Notes (free text, length-capped).
- Attachments: NOT implemented in the controller (no attachment field/DTO). Reason+Notes carry the textual context.
- Audit Trail: Purchase Return writes ledger + supplier transaction + stock movement + outbox, all tenant-scoped; there is no dedicated Return audit table beyond these.
- Return Status: There is NO separate Purchase Return status enum. The lifecycle is expressed via the parent purchase invoice `InvoiceStatus` (PartiallyReturned / FullyReturned) and the outbox `ProcessedAtUtc`/`JournalId`. The `Return` entity itself has no status column.
- Return Calculation: returned-line value is proportioned from the original invoice line economics (see RETURN CALCULATION).
- RefundAmount / RefundToCash: present on the request DTO but are NOT consumed by the controller for posting. The accounting path posts a payable reduction only; cash refund is not executed by this capability.

Explicitly NOT in scope (proven absent):
- No return approval/rejection workflow, no cancellation endpoint, no `CommerceHubReturnStatus` transitions. Those belong to CommerceHub customer/vendor returns (`PosAdminReturnsController`, `ReturnManagementService`, `CommerceHubReturnStatus`).
- No return "Created → Completed" state machine on the `Return` row.
- No payment execution, no refunds to cash/card.

## Authority Boundaries

Authoritative (source of truth, SQL/PostgreSQL):
- Purchase Return truth: `Returns` + `ReturnItems` tables (TenantId-scoped).
- Original Purchase: `PurchaseInvoices` + `PurchaseInvoiceItems` (read; status/version mutated).
- Return Amount: computed in `PurchaseReturnsController.CreatePurchaseReturn` from original invoice proportions; stored on `Return.Subtotal/TaxAmount/TotalAmount`.
- Return Quantity: `ReturnItem.Quantity` (display unit) and `ReturnItem.BaseQuantity` (base units).
- Inventory Reduction: `ProductVariantStocks.Quantity` (row locked FOR UPDATE / UPDLOCK), authoritative.
- Warehouse: `Warehouses` (active flag checked).
- Supplier Balance: `Suppliers.Balance` (locked) + `AccountLedgers` + `SupplierTransactions`.
- Accounting Journal: external posting service (`ICommerceAccountingService.PostPurchaseReturnAsync`); journal id captured in outbox.
- Stock Movement: `StockMovements` table.

Projection (derivative; never authoritative):
- Mongo `ProductVariantStockChangedEvent` consumers (`ProductVariantProjectionConsumer`, `ProductStockChangedConsumer`, `ProductStockPriceSearchIndexConsumer`, `AffiliateProductIntelligenceProjectionConsumer`, `AffiliateProductPolicyProjectionConsumer`, `CommerceHubProjectionConsumer`).
- Elasticsearch product/stock search index (updated via the same stock-change event).
- Any read model built from `Returns` (e.g., reports).

Evidence:
- `Return` is `ITenantOwned`; `PurchaseReturnRequestIdempotency` is `ITenantOwned`.
- Outbox messages are idempotency-keyed (`PURCHASE_RETURN:{TenantId}:{ReturnId}`).
- Stock sync outbox rows (`StockSyncOutboxMessages`) are created but left un-published until after commit (proven by concurrency test: `PublishedAtUtc` is null right after return).

Presentation:
- React pages `PurchaseReturns.jsx`, `PurchaseReturnReport.jsx` are operational surfaces only. Controllers expose: `GET /api/PurchaseReturns`, `GET /api/PurchaseReturns/{id}`, `GET /api/PurchaseReturns/invoice/{invoiceId}`, `POST /api/PurchaseReturns`, and the report `GET .../reports/purchase-returns` (`ReportsController.GetPurchaseReturnsReport`).

## Purchase Return Lifecycle

Reverse-engineered from `PurchaseReturnsController.CreatePurchaseReturn` plus the outbox processor and integration tests:

1. Request received with mandatory `Idempotency-Key` (and optional `X-Request-Id`, `X-Payment-Attempt-Id`). Fingerprint computed from payload.
2. Idempotency claim: `PurchaseReturnRequestIdempotency` row inserted ON CONFLICT DO NOTHING (TenantId+Key unique). If an existing completed record with same key but different fingerprint → 409; same fingerprint → replay stored 200 response.
3. Open SQL transaction. Original purchase invoice loaded with `FOR UPDATE` (Postgres) / `UPDLOCK,ROWLOCK,HOLDLOCK` (SQL Server) — pessimistic lock so concurrent returns serialize.
4. Validation gate (all within transaction, rollback on failure):
   - OriginalInvoiceId required.
   - Invoice must exist and not be `Cancelled` or `FullyReturned`.
   - Requested SupplierId and WarehouseId must equal invoice's.
   - Supplier row must exist; Warehouse must exist and be active.
   - Each item: product must have been received on the invoice; variant (if not supplied, must be uniquely inferable from invoice); unit must match a received unit; no duplicate (product,variant) lines; cumulative base quantity returned ≤ originally received.
5. Lock product rows (`FOR UPDATE`) and variant stock rows (`LockMultipleStocksAsync`, CreateIfMissing: true). Available stock (`AvailableQuantity = Quantity - ReservedQuantity`) must be ≥ return base quantity, else BadRequest.
6. For each line: compute proportioned subtotal/tax/unit price; decrement variant stock; re-weight product `PurchasePrice`; build `StockMovement` (`PurchaseReturn`) and pending stock-change event.
7. Recompute invoice status: if cumulative returned base quantity ≥ total received base quantity → `FullyReturned`, else `PartiallyReturned`. Bump `AccountingMutationVersion++`.
8. Write `Return` + `ReturnItem`s. Apply supplier transaction (ledger debit of TotalAmount, supplier balance reduced). Write `SupplierTransaction`.
9. Enqueue accounting outbox (`PurchaseReturn` event, idempotency key `PURCHASE_RETURN:{TenantId}:{ReturnId}`, carries mutationVersion). Balances are NOT mutated directly — outbox/ledger path only.
10. Allocate return number; save changes; store idempotency response (200) + ReturnId.
11. Commit SQL transaction. THEN (after durability): publish `ProductVariantStockChangedEvent` (Mongo/ES sync); call `ICommerceAccountingService.PostPurchaseReturnAsync` synchronously. If sync post fails → log + outbox worker retries. If success → mark outbox processed with JournalId.
12. Return 200 with `PurchaseReturnResponse` (ReturnId, ReturnNumber, TotalAmount, RefundAmount).

Outbox worker path (if sync post did not mark processed): `AccountingOutboxProcessor` dispatch → `ProcessPurchaseReturnAsync` → version check (stale message skipped) → `PostPurchaseReturnAsync` → journal posted.

## Implementation Discovery

Frontend:
- `PointOfSale.Web/src/pages/PurchaseReturns.jsx`, `PurchaseReturnReport.jsx`, `components/ReturnModal.jsx` — operational UI only.
- `App.jsx` routes: `/reports/purchase-return-report`.

Controllers (API layer):
- `PointOfSale.API/Controllers/PurchaseReturnsController.cs` — the entire Purchase Return capability lives here (no separate application service). Endpoints:
  - `GET /api/PurchaseReturns` (paged, filter by search/supplier/date range), owner role not required (just `[Authorize]`).
  - `GET /api/PurchaseReturns/{id}` — returns only if `ReturnType == PurchaseReturn`.
  - `GET /api/PurchaseReturns/invoice/{invoiceId}` — loads original purchase invoice for return UI.
  - `POST /api/PurchaseReturns` — `[Authorize(Roles="Owner,Admin,Manager")]`. The mutation.
  - `ReportsController.GetPurchaseReturnsReport` — analytics/report (date range default last 30 days; filters supplier/warehouse/product/category/subcategory).
- `PosAdminReturnsController.cs` — NOT purchase returns. It is CommerceHub vendor return lifecycle (schedule-pickup, mark-picked-up, in-transit, delivered, confirm-receipt). Different domain.

Endpoints / Request DTOs:
- `CreatePurchaseReturnRequest`: SupplierId, WarehouseId, ReturnDate, OriginalInvoiceId, Items[] (each: ProductId, ProductVariantId?, UnitId?, Quantity, UnitPrice, TaxRate, Reason), Reason?, Notes?, RefundToCash, RefundAmount.
- `PurchaseReturnItemRequest`: validated Quantity in [0.01, max], TaxRate in [0,100], UnitPrice ≥ 0.
- `PurchaseReturnResponse`: ReturnId, ReturnNumber, TotalAmount, RefundAmount, Message.

Application Services:
- None dedicated. Controller injects: `ApplicationDbContext`, `ITenantContext`, `ILedgerService`, `IEventPublisher`, `IAccountingOutboxService`, `ICommerceAccountingService`.
- `LedgerService.ApplySupplierTransaction` — writes `AccountLedger` + updates `Supplier.Balance` (locked).
- `CommerceAccountingService.PostPurchaseReturnAsync` — builds `PostingRequest` (Debit SupplierPayables, Credit Inventory) and posts journal via `IPostingService`.
- `AccountingOutboxService.EnqueuePurchaseReturnAsync` — inserts `AccountingOutboxMessages` row.
- `ProductVariantStockMutationSupport` — `LockMultipleStocksAsync`, `BuildPendingStockChange`, `PublishCommittedStockEventsAsync` (stock lock + event publishing).

Entities (SQL):
- `Return` (ITenantOwned): TenantId, ReturnNumber, ReturnDate, ReturnType, OriginalInvoiceId (SalesInvoice FK, legacy), OriginalPurchaseInvoiceId (purchase FK, authoritative for PR), OriginalInvoiceNumber, CustomerId?, SupplierId?, WarehouseId, Subtotal, TaxAmount, TotalAmount, Reason?, Notes?, Items.
- `ReturnItem`: ReturnId, ProductId, ProductVariantId?, UnitId?, Quantity, ConversionFactor, BaseQuantity, UnitPrice, TaxRate, TaxAmount, TotalAmount, CostPriceSnapshot?, CostAmountSnapshot?, Reason?.
- `PurchaseReturnRequestIdempotency` (ITenantOwned): TenantId, Key, PurchaseAttemptId?, RequestFingerprint, ReturnId?, ResponseStatusCode?, ResponseJson?, CompletedAtUtc?.
- `PurchaseInvoice`: Status (InvoiceStatus), AccountingMutationVersion (added 2026-04-03), Items.
- `Supplier`, `Warehouse`, `ProductVariantStock`, `StockMovement`, `AccountLedger`, `SupplierTransaction`, `AccountingOutboxMessages`.

Database Tables:
- `Returns`, `ReturnItems` (FK ReturnItems→Returns ON DELETE CASCADE), `PurchaseReturnRequestIdempotency` (unique IX on TenantId+Key), `PurchaseInvoices` (now has `AccountingMutationVersion`), `ProductVariantStocks`, `StockMovements`, `AccountLedgers`, `SupplierTransactions`, `AccountingOutboxMessages`, `StockSyncOutboxMessages` (Mongo sync).

Repositories / Integration:
- EF Core `ApplicationDbContext` directly (no repository abstraction for the mutation).
- Warehouse Integration: warehouse existence + active check; variant stock held in `ProductVariantStocks` keyed by (WarehouseId, VariantId).
- Supplier Integration: supplier match + balance via `LedgerService`.
- Inventory Integration: `ProductVariantStocks.Quantity` decremented; `Product.PurchasePrice` re-weighted using the line `BaseCost`; `StockMovement` recorded.
- Accounting Integration: outbox → `CommerceAccountingService.PostPurchaseReturnAsync` posts journal Debit SupplierPayables / Credit Inventory for `TotalAmount`.
- Outbox: `AccountingOutboxMessages` (event `PurchaseReturn`, idempotency `PURCHASE_RETURN:{TenantId}:{ReturnId}`, payload {ReturnId, OriginalInvoiceId, MutationVersion}).
- MassTransit: `ProductVariantStockChangedEvent` published after commit → Mongo + ES consumers. (Purchase return does NOT publish a domain event via `IEventPublisher` for the return itself — the event publisher is injected but unused in the create path; only the stock sync event is published.)
- Mongo: `StockSyncOutboxMessages` → variant/product projections.
- Redis: used as cache for product/stock read models (derivative), not authoritative.
- Elasticsearch: product/stock search index updated by stock-change event.

## Business Rules

All verified in `PurchaseReturnsController.CreatePurchaseReturn` and tests:

1. Idempotency is mandatory: `Idempotency-Key` header required (or `X-Request-Id`); max 200 chars; if both provided they must match. Replay with same key+fingerprint returns stored 200; different fingerprint → 409.
2. Original invoice required; must exist and not be `Cancelled` or `FullyReturned`.
3. Supplier + Warehouse of the return MUST equal the original invoice's (implicit supplier/warehouse authority — returns cannot be redirected).
4. Supplier must still exist (authoritative); Warehouse must exist and be `IsActive`.
5. Each returned product must have been received on the original invoice (matched by ProductId); variant must have been received (if not supplied, must be the single distinct variant on the invoice, else 400).
6. Return unit must match a unit received on the original invoice line.
7. No duplicate (ProductId, ProductVariantId) lines within one return request.
8. Quantity bound: cumulative returned base quantity for (invoice, product, variant) must NOT exceed originally received base quantity → "Cannot return more than received quantity" 400.
9. Available-stock bound: `AvailableQuantity (Quantity - ReservedQuantity)` of the variant stock must be ≥ return base quantity → "Insufficient stock" 400.
10. Variant-centric: stock is deducted from `ProductVariantStocks`; `ProductVariantId` effectively mandatory (inferred when unique); `BaseQuantity = Quantity * ConversionFactor`.
11. Cost basis: line `BaseCost` is used to snapshot `CostPriceSnapshot`/`CostAmountSnapshot` and to re-weight `Product.PurchasePrice` (remaining inventory value / remaining stock). Note: `BaseCost` originates from `variantStock`/product cost context — see RETURN CALCULATION caveat.
12. Invoice status transition: `FullyReturned` when all received qty returned, else `PartiallyReturned`. `AccountingMutationVersion` incremented on every return.
13. Supplier balance reduced by `TotalAmount` (debit). Ledger + SupplierTransaction written.
14. Accounting via outbox only — no direct balance mutation outside the ledger/outbox path.
15. Concurrency serialization: original invoice locked pessimistically; concurrent returns against the same invoice are serialized; overlapping uncommitted state is not visible to other readers (snapshot before commit).
16. Fail-closed: any validation failure rolls back the transaction; unexpected exception rolls back and returns 500; sync accounting post failure does NOT fail the return (return already committed) — logged, outbox retries.
17. Rounding: currency rounded to 2dp (MidpointRounding.AwayFromZero); rate to 6dp.

## Purchase Return State Machine

There is NO explicit state machine on the `Return` entity. The discoverable lifecycle is expressed through the parent invoice `InvoiceStatus` and outbox processing:

Completed Purchase Invoice
  ↓ (POST /api/PurchaseReturns, validated + stock locked + ledger debit + outbox enqueued)
Return Committed (Returns row written, invoice → PartiallyReturned)
  ↓ (cumulative returned base qty reaches received base qty)
Invoice → FullyReturned
  ↓ (outbox processed → journal posted)
Accounting Posted (SupplierPayables debited, Inventory credited)

Outbox sub-states (per message): pending → (skipped if stale/legacy) → processed (JournalId set). A stale message (mutationVersion mismatch) is skipped, not errored.

InvoiceStatus enum (authoritative): Draft, Completed, Cancelled, PartiallyReturned, FullyReturned.

There is no Rejected/Cancelled state for a purchase return itself. Once committed, a purchase return is immutable (no update/delete endpoint exists in this controller).

## Return Calculation

Proportioned from the original invoice line(s) in `CreatePurchaseReturn`:

For each returned line:
- Resolve matching original invoice items by (ProductId, effective VariantId), ordered by CreatedAt then Id.
- `baseQuantity = itemRequest.Quantity * conversionFactor` (conversionFactor from the matched original unit, default 1).
- `totalReturnedBaseQuantity` = sum of all prior `ReturnItem.BaseQuantity` for (invoice, product, variant) + this request's base quantity. Must be ≤ `originalReceivedBaseQuantity` (sum of original line BaseQuantity, falling back to Quantity*ConversionFactor).
- `originalSubtotal = Σ(originalItem.TotalAmount - originalItem.TaxAmount)`.
- `originalTax = Σ(originalItem.TaxAmount)`.
- `subtotalPerBaseUnit = originalSubtotal / originalReceivedBaseQuantity`.
- `taxPerBaseUnit = originalTax / originalReceivedBaseQuantity`.
- `authoritativeSubtotal = Round( baseQuantity * subtotalPerBaseUnit )`.
- `authoritativeTax = Round( baseQuantity * taxPerBaseUnit )`.
- `authoritativeUnitPrice = Round( authoritativeSubtotal / itemRequest.Quantity )`.
- `authoritativeTaxRate = authoritativeSubtotal > 0 ? Round( authoritativeTax*100 / authoritativeSubtotal ) : 0`.
- Line total = `Round( subtotal + tax )`.
- Header totals: `Subtotal = Round(Σ line subtotals)`, `TaxAmount = Round(Σ line taxes)`, `TotalAmount = Round(Subtotal + TaxAmount)`.

Cost side:
- `ReturnItem.CostPriceSnapshot = line.BaseCost`; `CostAmountSnapshot = Round( baseQuantity * BaseCost )`.
- `Product.PurchasePrice` re-weighted: `remainingInventoryValue = (Product.PurchasePrice * totalStockQuantity) - (BaseCost * baseQuantity)`; if remaining stock > 0, `PurchasePrice = Max(0, remainingInventoryValue / remainingStockQuantity)`.

IMPORTANT caveat (implementation-backed): `line.BaseCost` is the variant/product cost pulled from the stock context at mutation time (not snapshotted from the original purchase invoice cost). The report (`GetPurchaseReturnsReport`) computes `CostReversed = item.BaseQuantity * item.Product.PurchasePrice` using the *current* product `PurchasePrice`, NOT the immutable `CostAmountSnapshot` stored on the return line — so the report's cost reversal can diverge from the return's stored cost snapshot after subsequent purchases change `PurchasePrice`. This is a real, documented inconsistency (see DOCUMENTATION VALIDATION).

RefundAmount / RefundToCash: accepted on the request but NOT used anywhere in the create path or accounting post. The return posts a payable reduction (debit supplier), not a cash refund.

## Cross-System Effects

Purchases:
- Original `PurchaseInvoice.Status` flips PartiallyReturned ↔ FullyReturned.
- `PurchaseInvoice.AccountingMutationVersion` increments per return (optimistic-concurrency guard for the accounting outbox).
- Original invoice amounts/payments are NOT changed.

Products / Inventory:
- `ProductVariantStocks.Quantity` decreases by base quantity (per warehouse/variant, locked).
- `Product.PurchasePrice` re-weighted (average-cost style) using the return line cost.
- `StockMovement` row of type `PurchaseReturn` appended (QuantityChange negative, QuantityAfter snapshot).
- Mongo + Elasticsearch projections updated via `ProductVariantStockChangedEvent` published after commit.

Warehouse:
- No warehouse mutation other than the variant-stock decrement in the (invoice-mandated) warehouse. Warehouse must be active.

Suppliers:
- `Supplier.Balance` reduced by `TotalAmount` (debit). `AccountLedger` + `SupplierTransaction` (TransactionType.PurchaseReturn) written.

Accounting:
- Outbox message `PurchaseReturn` enqueued; processor posts journal: Debit `SupplierPayables`, Credit `Inventory`, amount = `TotalAmount`. Journal id stored on outbox.

Payments:
- No payment effect. Outbound cash to supplier is a separate Payment lifecycle (governed by payment authority; unresolved captured payment blocks outward money). RefundToCash flag is inert.

Analytics / Notifications:
- Report endpoint aggregates from `Returns`/`ReturnItems` (no notification/email sent by this capability).
- No Hermes/WhatsApp/notification integration in the purchase-return path.

Search:
- Elasticsearch product/stock index updated via the stock-change event (derivative).

Identity:
- TenantId enforced via `ITenantContext.RequiredTenantId` on every write; cross-tenant leak prevented by TenantId scoping + unique idempotency key per tenant.

Sales / Sales Returns:
- Unaffected. `ReturnType` discriminator keeps purchase vs sales returns separate; shared `Return`/`ReturnItem` tables but distinct `OriginalPurchaseInvoiceId` vs `OriginalInvoiceId` (SalesInvoice FK).

## Validation Checklist

After return created (committed):
- `Returns` row exists with `ReturnType=PurchaseReturn`, correct TenantId, ReturnNumber allocated.
- `ReturnItems` count matches request; BaseQuantity = Quantity*ConversionFactor.
- Original invoice `AccountingMutationVersion` incremented; `Status` = PartiallyReturned or FullyReturned.
- `ProductVariantStocks.Quantity` reduced by total base quantity; `StockMovement` (`PurchaseReturn`) written with negative change.

After inventory reduced:
- Variant stock available quantity ≥ 0; `Product.PurchasePrice` re-weighted (if remaining stock > 0).
- Mongo `StockSyncOutboxMessages` row exists (published-after-commit) for the variant.

After accounting reversal:
- `AccountingOutboxMessages` row for event `PurchaseReturn`, idempotency `PURCHASE_RETURN:{TenantId}:{ReturnId}`, `ProcessedAtUtc` + `JournalId` set.
- Journal: Debit SupplierPayables, Credit Inventory, amount = TotalAmount.

After supplier balance update:
- `SupplierTransaction` (PurchaseReturn) written; `AccountLedger` (supplier debit) written; `Supplier.Balance` reduced by TotalAmount.

After full return:
- Invoice `Status = FullyReturned`; any further return attempt → 409 "cannot accept another return".

After partial return:
- Invoice `Status = PartiallyReturned`; additional returns allowed until received qty exhausted.

## Common Failure Scenarios

1. Return exceeds received quantity → 400 "Cannot return more than received quantity". Cause: cumulative BaseQuantity > original received. Check prior `ReturnItems` for the invoice/product/variant.
2. Insufficient stock → 400 "Insufficient stock ... Available: X, Requested: Y". Cause: `ProductVariantStocks.AvailableQuantity < baseQuantity` (reserved stock counts against you). Check reservations/other concurrent movements.
3. Supplier mismatch / Warehouse mismatch → 400 "Supplier and warehouse must match the original purchase invoice." Returns cannot be redirected.
4. Original invoice Cancelled/FullyReturned → 409 "cannot accept another return."
5. Product/variant/unit not on original invoice → 400. Check the original `PurchaseInvoiceItems`.
6. Duplicate (Product, Variant) line in one request → 400.
7. Duplicate idempotency key, different fingerprint → 409. Re-submit with a fresh key or identical payload.
8. Accounting post fails after commit → return is still committed; error logged; outbox worker retries. Symptom: return exists but no journal. Check `AccountingOutboxMessages` (event `PurchaseReturn`) for unprocessed/failed rows.
9. Stale outbox message → skipped silently (mutationVersion mismatch). Symptom: journal not posted for an old message; by design.
10. Return "stuck" (uncommitted) → impossible to observe externally because uncommitted state is invisible until commit (serialized + isolated). If a request hangs, it is a lock/contention issue on the original invoice row.

Investigation order (where Hermes looks first):
1. `PurchaseReturnRequestIdempotency` (was it claimed/completed? what status code?).
2. `Returns` + `ReturnItems` (was the return actually written? amounts correct?).
3. `PurchaseInvoices` (Status, AccountingMutationVersion) — did the parent invoice transition correctly?
4. `ProductVariantStocks` + `StockMovements` (was stock deducted? movement present?).
5. `SupplierTransactions` + `AccountLedgers` + `Suppliers.Balance` (payable reduction applied?).
6. `AccountingOutboxMessages` (event `PurchaseReturn`) → journal posted? `JournalEntries`/`LedgerEntries`.
7. Mongo `StockSyncOutboxMessages` + ES index (projection lag).

## Debugging Guide

Start at `PurchaseReturnsController.CreatePurchaseReturn` (single method owns the whole capability).

- When to check Original Purchase: wrong invoice, Cancelled/FullyReturned, status not transitioning, mutationVersion drift. Load via `LoadOriginalPurchaseInvoiceForReturnMutationAsync` (pessimistic lock).
- When to check Warehouse: 400 warehouse mismatch / inactive; variant stock available quantity; stock not decremented.
- When to check Inventory: "Insufficient stock", negative available, `Product.PurchasePrice` drift, missing `StockMovement`.
- When to check Supplier Ledger: balance not reduced, missing `SupplierTransaction`/ledger, wrong debit amount. `LedgerService.ApplySupplierTransaction`.
- When to check Accounting: no journal, wrong debit/credit accounts, outbox not processed. `CommerceAccountingService.PostPurchaseReturnAsync` posts Debit SupplierPayables / Credit Inventory. Verify `AccountingOutboxMessages` (`PurchaseReturn`) and `JournalEntries`.
- When to check Stock Movements: confirm `StockMovementType.PurchaseReturn` rows with negative `QuantityChange` and correct `QuantityAfter`.
- When to check Outbox: `AccountingOutboxMessages` event `PurchaseReturn`, idempotency `PURCHASE_RETURN:{TenantId}:{ReturnId}`. Stale/legacy messages are skipped (mutationVersion). Failed messages retried by `AccountingOutboxProcessor`.
- When to check Projection (Mongo/ES): only after SQL truth is confirmed correct. Stock sync driven by `ProductVariantStockChangedEvent` published post-commit; lag here does not affect financial truth.

Key invariant: `ProductVariantStocks` is SQL-authoritative. `Returns`/`ReturnItems` are SQL-authoritative. Mongo/ES/Redis are derivative — rebuild from SQL, never trust them as source.

## Related Knowledge

Core Guides (implementation, not docs):
- `PurchaseReturnsController.cs`, `Return.cs`, `ReturnItem.cs`, `PurchaseReturnRequestIdempotency.cs`, `LedgerService.cs`, `CommerceAccountingService.PostPurchaseReturnAsync`, `AccountingOutboxService.EnqueuePurchaseReturnAsync`, `AccountingOutboxProcessor.ProcessPurchaseReturnAsync`, `ProductVariantStockMutationSupport`.

System Guides:
- Purchasing / Purchase Invoice authority; Supplier ledger & SOA; Accounting outbox & journal posting; Inventory (variant stock) authority; Multi-tenancy (ITenantContext).

Related Capabilities:
- Purchase Management (shares `PurchaseInvoice` read + status mutation; distinct from return accounting).
- Supplier Management / Statement of Account (ledger).
- Sales Return Management (`ReturnType.SalesReturn`, separate controller + cost proration).
- CommerceHub Returns (`ReturnManagementService`, `CommerceHubReturnStatus`, `PosAdminReturnsController`) — marketplace/vendor returns, NOT POS purchase returns.

Related Domains:
- Inventory, Warehouse, Supplier, Accounting/Ledger, Payments (separate), Search (ES), Product projections (Mongo).

## Documentation Validation

Findings (implementation-backed, vs. any docs/README which are treated as stale):

- MISSING documentation: No separate application service exists for purchase returns — the entire capability lives in `PurchaseReturnsController`. Documentation implying a "Purchase Return Service" would be wrong.
- INCORRECT assumption: There is NO Purchase Return status state machine. The `Return` entity has no Status column. Lifecycle is expressed only via the parent `PurchaseInvoice.Status` (PartiallyReturned/FullyReturned) and outbox processing. Any doc showing Return states (Created/Completed/Rejected/Cancelled) is incorrect for purchase returns.
- INCORRECT authority: `RefundToCash` / `RefundAmount` on the DTO are inert for purchase returns — no cash refund is executed; the accounting posts a supplier payable reduction only. Docs implying refund payout would be wrong.
- HIDDEN behavior: Accounting posting is dual-path — synchronous post right after commit (marks outbox processed) OR outbox worker retry. A failed sync post does NOT fail the return; the return is already durable. This fail-open-on-accounting (but fail-closed-on-data) behavior is easy to miss.
- HIDDEN behavior: Stock sync to Mongo/ES happens only AFTER the SQL commit, and the `StockSyncOutboxMessages` row is left un-published (`PublishedAtUtc=null`) until the post-commit publish — so uncommitted returns are invisible to projections (proven by concurrency test).
- HIDDEN behavior: The `Return` discriminator shares one table with sales returns; purchase returns key off `OriginalPurchaseInvoiceId` (a PurchaseInvoice FK) while `OriginalInvoiceId` (SalesInvoice FK) stays null for them. Mixing these up is a real bug risk.
- INCONSISTENCY (report vs. return): `GetPurchaseReturnsReport` computes `CostReversed = item.BaseQuantity * item.Product.PurchasePrice` using the *current* product purchase price, while the return line stores an immutable `CostAmountSnapshot = BaseQuantity * BaseCost` (line cost at return time). After later purchases change `PurchasePrice`, the report's cost reversal diverges from the authoritative return snapshot. The authoritative figure is `ReturnItem.CostAmountSnapshot`.
- INCONSISTENCY: `PurchaseReturnReportDto.DiscountReversed` is hardcoded to 0 and `NetReturnAmount` equals `GrossReturnAmount` (TotalAmount). Discounts are not modelled on purchase returns.
- ARCHITECTURE: `Returns`/`ReturnItems`/`PurchaseInvoices` are in `ForbiddenBusinessDbSets` for ledger-only services — financial reporting must read from the immutable ledger, not from Returns. Confirmed by `ForbiddenBusinessDbSets.Names`.
- CommerceHub returns (`PosAdminReturnsController`, `CommerceHubReturnStatus` with PickupScheduled/PickedUp/InTransit/DeliveredToVendor/Confirmed/Failed) are a DIFFERENT capability and must not be conflated with POS purchase returns.

## Quality Requirements — Coverage Confirmation

This document allows Hermes to:
- Understand Purchase Return Management (purpose, scope, authority boundaries).
- Navigate the implementation (`PurchaseReturnsController` is the single owning file; plus LedgerService, CommerceAccountingService, AccountingOutbox{Service,Processor}, ProductVariantStockMutationSupport).
- Execute return workflows (POST /api/PurchaseReturns with Idempotency-Key; required fields; proportioned calculation).
- Validate return behavior (checklist above; invoice status, stock, ledger, outbox, journal).
- Debug return issues (ordered investigation path; common failures).
- Understand inventory reversal (variant stock decrement + PurchasePrice re-weight + StockMovement).
- Understand supplier ledger reversal (debit of TotalAmount, SupplierTransaction + AccountLedger + Supplier.Balance).
- Understand accounting reversal (outbox → Debit SupplierPayables / Credit Inventory journal).
- Understand authority boundaries (SQL truth vs Mongo/ES/Redis projections; ForbiddenBusinessDbSets).

---

## Output Footer (task-mandated)

- Summary of discoveries: Purchase Return is an idempotent, concurrency-serialized, single-transaction capability owned entirely by `PurchaseReturnsController.CreatePurchaseReturn`. It reverses goods against a completed purchase invoice by (1) validating invoice/supplier/warehouse/product/variant/unit/quantity, (2) locking and decrementing variant stock + re-weighting product cost + writing a StockMovement, (3) debiting the supplier ledger/balance + SupplierTransaction, (4) enqueuing an accounting outbox that posts Debit SupplierPayables / Credit Inventory, and (5) flipping the invoice to PartiallyReturned/FullyReturned with an incremented AccountingMutationVersion. Mongo/ES sync happens post-commit via ProductVariantStockChangedEvent.

- Purchase Return lifecycle discovered: Request(+Idempotency-Key) → Idempotency claim → Open tx + lock invoice → Validate → Lock product+variant stock → Decrement stock + StockMovement + re-weight cost → Flip invoice status + bump version → Write Return + ledger debit + SupplierTransaction → Enqueue accounting outbox → Commit → Publish stock event + sync accounting post → (outbox worker retry if needed) → Journal posted.

- Purchase Return state machine discovered: No state column on Return. Observable states via parent invoice: Completed → (return committed) PartiallyReturned → (fully returned) FullyReturned. Outbox: pending → processed (or skipped if stale). Not Rejected/Cancelled (immutable once committed).

- Business rules discovered: mandatory idempotency key; original invoice required & not Cancelled/FullyReturned; supplier+warehouse must match invoice; product/variant/unit must be on invoice; no duplicate (product,variant) lines; cumulative returned base qty ≤ received; available variant stock ≥ return qty; variant-centric stock deduction; supplier balance reduced by TotalAmount; accounting via outbox only; pessimistic invoice lock serializes concurrent returns; fail-closed on validation, fail-open on post-commit accounting (outbox retries); currency 2dp / rate 6dp rounding.

- Return calculation behavior discovered: per-line value proportioned from original invoice line economics (subtotal/tax per base unit); BaseQuantity = Quantity*ConversionFactor; header totals summed + rounded; cost snapshotted (CostPriceSnapshot/CostAmountSnapshot) and used to re-weight Product.PurchasePrice; RefundAmount/RefundToCash accepted but inert.

- Cross-system dependencies discovered: Purchases (invoice status/version), Inventory (variant stock + PurchasePrice + StockMovement), Warehouse (active check + stock location), Suppliers (ledger/balance/transaction), Accounting (outbox → journal), Mongo/ES (post-commit stock event), Identity (TenantId scoping). Payments/Sales/CommerceHub returns are NOT affected.

- Documentation inconsistencies found: no separate return service exists; no return status state machine; RefundToCash/RefundAmount are inert; report's CostReversed uses live PurchasePrice not the stored CostAmountSnapshot; DiscountReversed hardcoded 0; CommerceHub returns conflated with purchase returns.

- Hidden implementation behavior found: dual-path accounting post (sync + outbox retry) that does not fail the return on post failure; stock sync only after commit with un-published outbox row; OriginalPurchaseInvoiceId vs OriginalInvoiceId discriminator split; Returns/ReturnItems in ForbiddenBusinessDbSets (ledger-only services must not read them).

- Confirmation that only this file was created: TRUE. Only `.ai/hermes/capabilities/pos-admin/purchase-return-management.md` was created.

- Confirmation that no other files were modified: TRUE. No existing file was patched, edited, or deleted.

- Confirmation that no memory writes occurred: TRUE. No memory tool calls were made.
