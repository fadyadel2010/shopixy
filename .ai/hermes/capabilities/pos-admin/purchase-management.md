# Purchase Management Capability (POS Admin)

Reverse-engineered from implementation. Implementation is authoritative; documentation is secondary and any disagreement is resolved in favor of the code.

> CRITICAL OVERRIDE — THIS IS NOT THE SALES/CHECKOUT LIFECYCLE.
> There is NO Draft → Approval → Receiving → Completion pipeline for purchases.
> A purchase is created atomically as `Completed` with inventory, supplier ledger, and
> accounting posted in a single transaction. The lifecycle below is the one the code proves.

================================================================
# Purpose
================================================================

Purchase Management records goods received from a supplier into a warehouse, increases
stock, updates product cost (average purchase price), opens a supplier payable, and posts
the accounting journal — all as one transactional operation.

Business problem it solves:
- Capture supplier deliveries (inbound inventory) with cost and tax.
- Keep stock authoritative in the selected warehouse.
- Maintain the supplier's running payable balance (what we owe the supplier).
- Produce double-entry accounting (Inventory asset up, Supplier Payables up, cash/card down).
- Allow reversing a purchase via Purchase Return (partial or full) or deletion of a
  not-yet-posted (draft) invoice.

What it owns:
- The PurchaseInvoice aggregate (header + items) and its monetary totals.
- The authoritative stock increase in ProductVariantStocks for the chosen warehouse.
- The product average PurchasePrice recalculation.
- SupplierTransactions (Purchase / PaymentMade / PurchaseReturn) and supplier balance.
- The accounting outbox intent + journal for PosPurchase / PurchaseReversal / PurchaseReturn.
- StockMovement ledger rows (Purchase / PurchaseReturn / PurchaseInvoiceRevert movements).

================================================================
# Business Scope
================================================================

IMPLEMENTATION-PROVEN CAPABILITIES
-----------------------------------
- Purchase Invoice (PurchaseInvoice entity) — the core inbound document.
- Purchase Invoice Items (PurchaseInvoiceItem) — product/variant/unit/quantity/cost/tax per line.
- Supplier selection (SupplierId) — validated to exist; cannot change identity on return.
- Warehouse selection (WarehouseId) — validated active; single warehouse per invoice.
- Unit handling — each line may specify a Unit; BaseQuantity = Quantity * ConversionFactor
  is what actually enters stock. Default unit/conversion factor resolved if none supplied.
- Purchase cost — per-line UnitPrice (cost per selected unit). Base cost = UnitCost / ConversionFactor.
- Tax — per-line TaxRate (0–100); line tax = subtotal * rate / 100.
- Discounts — PurchaseInvoice.DiscountAmount / DiscountPercentage exist on the entity, but
  NO DISCOUNT IS COMPUTED OR APPLIED anywhere in the purchase create/update code path.
  (See "Hidden implementation behavior".)
- Additional expenses — NOT implemented. There is no landed-cost / freight / expense line on
  a purchase. (Costing section explains the only cost math present.)
- Currency / Exchange Rate — NOT implemented. Purchases are single-currency, in the tenant's
  currency. No FX conversion exists in the purchase path.
- Payment at receipt — PaidAmount (cash/card/mixed/credit), recorded as a separate supplier
  PaymentMade transaction and as a cash/card credit leg in the journal.
- DueDate — optional, persisted on the invoice (added by migration 20251205180157).
- Invoice number — generated from a DB sequence (Postgres) or tenant-locked legacy counter
  (SqlServer); format PUR-yyyyMMdd-NNNN.
- Purchase Return (Return with ReturnType = PurchaseReturn) — partial or full reversal of a
  received invoice; reduces stock, reduces supplier payable, posts PurchaseReturn journal.
- Delete (cancel) of a DRAFT purchase — only allowed before it is Completed/posted.
- Idempotent create / update / delete / return via Idempotency-Key header.

WHAT PURCHASE MANAGEMENT DOES NOT OWN
-------------------------------------
- Purchase Orders / Purchase Requests / approval workflow: NONE EXIST in code. The
  `PurchaseRequestIdempotency` table is request-level idempotency, not a purchase-order document.
- Multi-stage receiving / partial receiving at receipt time: NONE. The entire ordered quantity
  is received into stock immediately and atomically on create. "Partial" only exists on the
  RETURN side (partial vs full return).
- Supplier master data (CRUD) — owned by Suppliers capability.
- Warehouse master data — owned by Warehouse capability.
- Supplier payments made outside an invoice (SupplierPayment) — a separate capability; a purchase
  only records the initial PaymentMade at receipt.
- Product pricing/sell price — purchase only writes Product.PurchasePrice (cost), never sell price.
- Sales / Storefront / Checkout — unrelated; do not reuse their lifecycle.
- Inventory reservation / available-stock consumption by sales — purchase only adds to stock;
  it does not reserve.

================================================================
# AUTHORITY BOUNDARIES
================================================================

AUTHORITY (system of record / writes truth)
--------------------------------------------
- PurchaseInvoice (+ Items) — PostgreSQL (ApplicationDbContext). Single source of truth.
- Stock quantity — ProductVariantStocks (PostgreSQL). Purchase writes it directly under row lock.
- Product cost — Product.PurchasePrice (PostgreSQL). Recalculated by purchase math.
- Supplier balance — SupplierTransactions + ILedgerService.ApplySupplierTransaction (PostgreSQL).
- Accounting journal — JournalEntries via ICommerceAccountingService / FinancialPostingService.
- Outbox intent — AccountingOutboxMessages (PostgreSQL), processed by AccountingOutboxProcessor.
- Tenant — ITenantContext.RequiredTenantId; every write is tenant-scoped. Cross-tenant = CRITICAL.

PROJECTION (derivative, eventually consistent)
----------------------------------------------
- MongoDB read models — synced from ProductVariantStockChangedEvent (published after commit).
- Elasticsearch — search index (not purchase-specific in this path).
- Redis — caches; never authoritative.
- Analytics — NOTE: `AuthoritativePurchaseAnalyticsConsumer` listens to `OrderPaidEvent` and
  emits `PurchaseCompleted` analytics for STOREFRONT ORDERS, not POS purchases. POS purchase
  posting does NOT emit those analytics. Do not assume purchases feed storefront analytics.
- Reports (PurchaseRegisterReport, PurchasesBySupplierReport, PurchaseReturnReport) read
  PostgreSQL directly (projection = same DB, but reporting-only; they never mutate).

EVIDENCE (audit / immutable record)
-----------------------------------
- StockMovements (Purchase / PurchaseReturn / PurchaseInvoiceRevert movement types).
- SupplierTransactions (every supplier balance delta).
- AccountingOutboxMessages + CommerceAccountingAudits (financial posting evidence).
- Audit log (IAuditService.LogCreate/LogDelete) for PurchaseInvoice.
- Idempotency records (PurchaseRequestIdempotency, PurchaseReturnRequestIdempotency) capture the
  exact request fingerprint + stored response for replay safety.

PRESENTATION (UI only, safe to change)
--------------------------------------
- React pages: PurchaseInvoice.jsx, PurchaseReturns.jsx, PurchasePrintModal.jsx, and the three
  report pages. These are operational surfaces; the capability is valid without them.

================================================================
# PURCHASE LIFECYCLE (implementation-proven)
================================================================

The create path is ATOMIC. There is no staged progression.

  POST /api/purchases  ──►  PurchaseInvoice created with Status = Completed
        │
        ├─ Validate Supplier exists
        ├─ Validate Warehouse exists AND IsActive
        ├─ Validate items (product exists, unit bound, variant resolvable, UnitCost > 0)
        ├─ Lock Products (FOR UPDATE) for cost recalculation
        ├─ Compute financials (subtotal, tax, total)
        ├─ Resolve payment split (cash/card/mixed/credit) + validate
        ├─ Validate cashbox capacity (if cash paid)
        ├─ Allocate invoice number (DB sequence)
        ├─ Recalculate Product.PurchasePrice (weighted average)
        ├─ Add BaseQuantity to ProductVariantStocks (warehouse lock)
        ├─ Write StockMovement (Purchase)
        ├─ Write SupplierTransaction Purchase (credit = we owe)
        ├─ If PaidAmount>0: write SupplierTransaction PaymentMade (debit)
        ├─ Enqueue + SYNCHRONOUSLY post accounting journal (PosPurchase)
        ├─ Mark outbox row processed (with journal id)
        └─ Commit; publish ProductVariantStockChangedEvent(s)

  Lifecycle as observable states:
        (created as Completed)  ──►  Completed
                                      │
                                      ├─ Update (PUT)  ──►  Completed (new AccountingMutationVersion,
                                      │                     old effects reverted then re-applied)
                                      │
                                      ├─ Partial Return ──►  PartiallyReturned
                                      │
                                      ├─ Full Return    ──►  FullyReturned
                                      │
                                      └─ Delete (only if still Draft/i.e. NOT Completed) ──► removed

  NOTE: Because create always sets Completed, the only way to reach "Draft" is if you never
  successfully completed a create (it is never persisted as Draft). The InvoiceStatus.Draft
  value exists in the enum but is NOT assigned by any purchase code. See state machine.

================================================================
# IMPLEMENTATION DISCOVERY
================================================================

FRONTEND
--------
- PointOfSale.Web/src/pages/PurchaseInvoice.jsx — create/edit purchase, print.
- PointOfSale.Web/src/pages/PurchaseReturns.jsx — create purchase return.
- PointOfSale.Web/src/pages/PurchasePrintModal.jsx — print/proof.
- PointOfSale.Web/src/pages/PurchaseRegisterReport.jsx, PurchasesBySupplierReport.jsx,
  PurchaseReturnReport.jsx — reporting only (no writes).
- PointOfSale.Web/src/admin/help/helpArticles/purchase-invoice.md, purchase-returns.md — help text.

CONTROLLERS / ENDPOINTS
-----------------------
PurchasesController (route api/purchases), all [Authorize]:
- POST            /api/purchases                       (Owner,Admin,Manager) — create (atomic → Completed)
- GET             /api/purchases/{id}                  — get invoice + items + supplier/warehouse
- PUT             /api/purchases/{id}                  (Owner,Admin,Manager) — update (rewrite)
- DELETE          /api/purchases/{id}                  (Owner,Admin)         — delete (draft only)
- GET             /api/purchases                       — paged list (search by invoice # or supplier)
- GET             /api/purchases/{id}/can-delete       (Owner,Admin,Manager) — pre-check delete
PurchaseReturnsController (route api/purchasereturns), all [Authorize]:
- GET             /api/purchasereturns                 — paged list (+ supplier/date filters)
- GET             /api/purchasereturns/{id}            — get return
- GET             /api/purchasereturns/invoice/{id}    — load original invoice for return UI
- POST            /api/purchasereturns                 (Owner,Admin,Manager) — create return

APPLICATION SERVICES
--------------------
- PurchasesController (logic lives in the controller; large, ~2000 lines).
- PurchasePaymentSplitter — resolves cash/card split from PaymentMethod + amounts.
- PurchaseFinancialIntegrityService — guards purchase mutation/revert: blocks if posted journals
  exist (RejectExisting policy) or if outbox work is claimed/retrying/failed. Legacy treasury
  tables are intentionally ignored (fail-closed on ledger + outbox only).
- ValidationService.CanDeletePurchaseInvoiceAsync — delete gate (status + returns check).
- IValidationService.ValidatePurchaseInvoiceAsync — used elsewhere; mirrors controller checks.
- ILedgerService.ApplySupplierTransaction — authoritative supplier balance mutation.
- IPartyBalanceMaintenanceService.RebuildPartyBalanceAsync — rebuilds supplier balance after revert.

RECEIVING / INVENTORY INTEGRATION
---------------------------------
- ProductVariantStockMutationSupport.LockMultipleStocksAsync — row-locks ProductVariantStocks
  per (WarehouseId, VariantId) with CreateIfMissing.
- ProductVariantStockMutationSupport.PublishCommittedStockEventsAsync — emits
  ProductVariantStockChangedEvent after commit (Mongo/Redis/ES projections).
- StockMovement rows recorded with MovementType = Purchase / PurchaseReturn / Adjustment
  (revert). Price on the movement = base unit cost.
- LoadProductStockTotalsAsync — sums ProductVariantStocks across variants of a product
  (used for cost recalculation).

WAREHOUSE INTEGRATION
--------------------
- WarehouseId validated to exist and IsActive (else 400).
- All stock mutations are warehouse-scoped (the chosen warehouse only).
- Cashbox account resolved per warehouse for cash payment capacity + journal credit leg
  (ICashboxLedgerAccountService.ResolveWarehouseCashboxAccountAsync).

SUPPLIER INTEGRATION
-------------------
- SupplierId validated to exist.
- SupplierTransactions written (Purchase credit, PaymentMade debit, PurchaseReturn debit).
- ILedgerService maintains running BalanceAfter per supplier.
- On revert/return, supplier balance rebuilt via RebuildPartyBalanceAsync.

ACCOUNTING INTEGRATION
----------------------
- IAccountingOutboxService.EnqueuePosPurchaseAsync / EnqueuePurchaseReversalAsync /
  EnqueuePurchaseReturnAsync — enqueue outbox intent.
- ICommerceAccountingService.PostPosPurchaseAsync — posts the journal SYNCHRONOUSLY inside the
  request (CREDIT Inventory asset; DEBIT cash/card box; CREDIT Supplier Payables for the unpaid
  remainder). Returns JournalId.
- PurchasesController then marks the outbox row Processed (with JournalId) in the same request
  (the "synchronous completion" pattern, PHASE LCR).
- AccountingOutboxProcessor also has ProcessPosPurchaseAsync / ProcessPurchaseReversalAsync /
  ProcessPurchaseReturnAsync handlers — these are the asynchronous fallback / replay path and
  apply STALE-VERSION SKIP: if message mutation version != invoice.AccountingMutationVersion,
  the message is skipped (idempotent).
- PurchaseReturn journal: DEBIT Supplier Payables, CREDIT Inventory.

ENTITY / DB TABLES
------------------
- PurchaseInvoices (TenantId NOT NULL; AK_PurchaseInvoices_TenantId_Id alternate key;
  global query filter IsDeleted=false).
- PurchaseInvoiceItems (check constraints: Quantity>0, ConversionFactor>0, BaseQuantity>0,
  UnitPrice>0, TaxRate 0..100).
- PurchaseInvoicePayments (entity exists; NOT written by purchase create — the buy-side payment
  is recorded as a SupplierTransaction PaymentMade, not into this table in the create path).
- SupplierTransactions, AccountLedgers, JournalEntries, AccountingOutboxMessages.
- ProductVariantStocks, StockMovements, Products (PurchasePrice).
- PurchaseRequestIdempotency, PurchaseReturnRequestIdempotency (request idempotency, not documents).
- Migration S78 (20260508170000) added AccountingMutationVersion; migration 20260408103000 added
  purchase_invoice_seq; migration 20251205180157 added DueDate; 20260403 idempotency SQL.

BACKGROUND JOBS / OUTBOX / MASSITRANSIT / MONGO / REDIS / ES
------------------------------------------------------------
- Outbox: AccountingOutboxMessages is the durable intent; AccountingOutboxProcessor (MassTransit
  consumer / hosted worker) posts to the ledger. Purchase uses the synchronous-post-then-mark-
  processed pattern, so under normal operation the journal is posted before the HTTP response.
- Mongo/Redis/ES: downstream projections consume ProductVariantStockChangedEvent (published after
  commit). They are NEVER authoritative for purchase truth.
- Analytics consumer `AuthoritativePurchaseAnalyticsConsumer` is for STOREFRONT order payments,
  NOT POS purchases (see Authority Boundaries / Hidden behavior).

================================================================
# BUSINESS RULES (implementation-backed)
================================================================

SUPPLIER
- Supplier must exist (404/BadRequest "Supplier not found" otherwise).
- On purchase return, SupplierId AND WarehouseId MUST equal the original invoice's (400 otherwise).

WAREHOUSE
- Warehouse must exist AND IsActive (400 "Active warehouse not found" otherwise).
- Entire invoice is bound to ONE warehouse; stock is added only there.

ITEMS / PRODUCTS
- Every item's Product must exist.
- If UnitId supplied, the unit must be bound to that product (ProductUnits); else default unit's
  conversion factor is used.
- Variant must resolve (ProductVariantStockMutationSupport.ResolveVariantAsync); a product without
  a resolvable variant is rejected.
- UnitCost (per selected unit) MUST be > 0 (400 otherwise). Entity check: UnitPrice > 0.

IDEMPOTENCY / DEDUP
- Idempotency-Key header is REQUIRED for create/update/return (400 if missing).
- Same key with a DIFFERENT request fingerprint → 409 Conflict ("already used for a different ...").
- Same key with SAME fingerprint and prior response → stored response replayed (no double effect).
- Idempotency key scoped per tenant + supplier: "SUPPLIER:{TenantId}:{key}".

FINANCIAL / PAYMENT
- PaidAmount cannot be negative; cannot exceed TotalAmount (controller + SaveChanges guard).
- Payment split rules (PurchasePaymentSplitter): Cash→cash only, Card→card only, Mixed→both>0,
  Credit→optional InitialPaymentMethod or explicit cash/card split; BankTransfer unsupported.
- Cashbox capacity check: if cash paid, warehouse cashbox available balance must cover the cash
  portion; otherwise 400 INSUFFICIENT_FUNDS. On validation failure, purchase is NOT recorded
  (fail closed — 503 if treasury read throws).
- Total = Sum(line subtotal + line tax). Subtotal = Sum(Quantity*UnitCost). Tax = subtotal*rate/100
  rounded to 2dp (MidpointRounding.AwayFromZero).

COSTING
- Product.PurchasePrice updated as weighted average using TOTAL product stock across all variants
  of the product (see Costing section).

UPDATE (PUT)
- ExpectedMutationVersion required; mismatch → 409 (stale client). Prevents lost updates.
- Cannot update an invoice that has purchase returns OR is PartiallyReturned/FullyReturned/Cancelled.
- Update reverts ALL prior effects (stock, cost, supplier ledger, outbox intent) then re-applies
  with AccountingMutationVersion incremented; enqueues PurchaseReversal + synchronous PostPosPurchase
  (operationKey = "purchase-mutation-{idempotencyId}").

DELETE
- Only allowed when status is NOT Completed (i.e., was never successfully posted). Completed →
  "INVOICE_POSTED" rejection; use purchase return instead.
- Blocked if any PurchaseReturn exists ("INVOICE_HAS_RETURNS").
- Blocked if posted journals exist (PostedJournalsExistException → 409 POSTED_JOURNALS_EXIST).
- On delete: reverts stock + cost, removes AccountLedgers + SupplierTransactions for that invoice,
  then rebuilds supplier balance. JournalPolicy = RejectExisting.

RETURN (Purchase Return)
- OriginalInvoiceId required.
- Original invoice must not be Cancelled or FullyReturned (409 "cannot accept another return").
- Return quantity per (product, variant, unit) cannot exceed originally received (sum of prior
  returns + this request <= original received BaseQuantity) → 400.
- Available stock (Quantity − Reserved) must cover the return quantity (400 INSUFFICIENT_STOCK).
- Return unit must match a unit received on the original invoice.
- Duplicate (product, variant) lines in one return rejected.
- Return amounts are recomputed authoritatively from the original invoice lines (not from client
  UnitPrice/TaxRate blindly) — per-base-unit subtotal & tax derived from original, then scaled.
- Full vs partial determined by total returned BaseQuantity vs total received BaseQuantity →
  sets original invoice Status = FullyReturned or PartiallyReturned.

FAIL CLOSED
- Any uncaught exception in create/update → transaction rolled back, ChangeTracker cleared, 500.
- Cashbox validation unavailable → 503, purchase not recorded.
- Outbox work claimed/retrying/failed → mutation/revert blocked until reconciled.
- Outbox processor skips stale-version messages (no double post).

================================================================
# PURCHASE STATE MACHINE (implementation-proven states)
================================================================

InvoiceStatus enum: Draft, Completed, Cancelled, PartiallyReturned, FullyReturned.

Transitions observed in code:
  (none) ──create──► Completed          [CreatePurchase sets Status = Completed unconditionally]
  Completed ──PUT──► Completed          [update; AccountingMutationVersion++]
  Completed ──partial return──► PartiallyReturned
  Completed ──full return──►    FullyReturned
  Completed ──DELETE (only if NOT Completed)──► (deleted row)
  Cancelled ──(read-only guard)──► blocks delete & return   [never assigned by purchase code]

KEY FINDINGS:
- Draft is NEVER assigned by any purchase code (verified: no `= InvoiceStatus.Draft`).
- Cancelled is NEVER assigned by any purchase code (verified: no `= InvoiceStatus.Cancelled`).
  It is only READ as a guard condition. No purchase path transitions an invoice into Cancelled.
  "Cancellation" of a purchase = DELETE of a not-yet-Completed invoice, OR a Purchase Return of a
  Completed one. So the enum value Cancelled is effectively dead for purchases.
- There is no Approved / Receiving / Pending state.
- PartiallyReturned / FullyReturned are terminal w.r.t. further returns (full return blocks more;
  partial still allows more until fully returned).

================================================================
# COSTING (implementation-proven)
================================================================

METHOD: Weighted Average Cost per PRODUCT (across all variants/warehouses of that product),
tracked in Product.PurchasePrice. NOT FIFO, NOT LIFO, NOT per-warehouse, NOT per-variant.

ON CREATE / UPDATE (each item):
  baseUnitCost   = UnitCost / ConversionFactor
  totalStock     = SUM of ProductVariantStocks.Quantity across ALL variants of the product (pre-add)
  IF (totalStock + baseQuantity) > 0:
      Product.PurchasePrice = ((totalStock * oldPurchasePrice) + (baseQuantity * baseUnitCost))
                              / (totalStock + baseQuantity)
  ELSE:
      Product.PurchasePrice = baseUnitCost

ON REVERT (delete/update-before-reapply, each item):
  Using product-level totals:
  remainingQty  = totalStockQty - item.BaseQuantity
  IF remainingQty > 0:
      restoredCost = (currentTotalValue - purchaseValue) / remainingQty
      IF restoredCost > 0: Product.PurchasePrice = restoredCost
  (else PurchasePrice left unchanged)

ON RETURN (each returned line):
  remainingStockQty = totalStockQty - returnedBaseQty
  IF remainingStockQty > 0:
      remainingValue = (PurchasePrice * totalStockQty) - (line.BaseCost * returnedBaseQty)
      Product.PurchasePrice = MAX(0, remainingValue / remainingStockQty)

LANDED COST / ADDITIONAL EXPENSES: NOT implemented. Only the line UnitCost feeds cost.
SUPPLIER DISCOUNT: NOT implemented. (DiscountAmount/Percentage columns exist but are unused.)
TAX: per-line, included in the invoice total and in the return recomputation; tax is NOT part of
  Product.PurchasePrice (price is cost-only).
CURRENCY / EXCHANGE RATE: NOT implemented. Single currency; no conversion in the purchase path.

RETURN AMOUNT AUTHORITATIVENESS:
  Return line subtotal/tax are derived from the ORIGINAL invoice line proportions
  (subtotalPerBaseUnit, taxPerBaseUnit), scaled by returned BaseQuantity — not from client input.
  This keeps the supplier-credit and inventory reduction consistent with what was originally booked.

================================================================
# CROSS-SYSTEM EFFECTS (implementation-proven)
================================================================

PRODUCTS
- Product.PurchasePrice recalculated (weighted average). Sell price untouched.
- Product row locked (FOR UPDATE) during cost recalculation.

INVENTORY (ProductVariantStocks)
- BaseQuantity ADDED on create/update; SUBTRACTED on return; SUBTRACTED on delete-revert.
- Warehouse-scoped. Row-locked per (WarehouseId, VariantId).
- Available/Reserved/Damaged snapshots copied into StockMovement.
- ProductVariantStockChangedEvent published AFTER commit → Mongo/Redis/ES projections updated.

PRICING
- No effect on sell price. Only cost (PurchasePrice) changes.

WAREHOUSE
- Stock only changes in the selected warehouse. No cross-warehouse movement.
- Cashbox account balance is NOT mutated by purchase code directly; the journal credits the
  warehouse cashbox account via the ledger. Cashbox CAPACITY is validated pre-write.

SUPPLIERS
- SupplierTransactions appended: Purchase (credit=owe), PaymentMade (debit), PurchaseReturn (debit).
- Running BalanceAfter maintained by ILedgerService. AccountLedgers rows written.
- On revert/return, supplier balance rebuilt via RebuildPartyBalanceAsync.

ACCOUNTING
- Journal posted (synchronous in request; outbox also enqueued for replay safety):
  PosPurchase:  DEBIT Inventory (Total), CREDIT Cashbox (cash), CREDIT Visa (card),
                CREDIT Supplier Payables (unpaid remainder).
  PurchaseReversal (on update): reverses prior PurchaseInvoice journals then re-posts new.
  PurchaseReturn: DEBIT Supplier Payables, CREDIT Inventory.
- CommerceAccountingAudits recorded per posting.

PAYMENTS
- Initial payment at receipt recorded as SupplierTransaction PaymentMade + journal cash/card leg.
- Separate capability (SupplierPayment) handles later manual payments; not part of purchase create.

ANALYTICS
- POS purchase posting does NOT emit PurchaseCompleted analytics. The
  AuthoritativePurchaseAnalyticsConsumer reacts to OrderPaidEvent (storefront orders only).
- Generic metrics: AccountingOutboxProcessor increments PosPurchasesPosted counter.

NOTIFICATIONS
- None in the purchase create/return path itself (no WhatsApp/email triggered by purchase posting).

SEARCH (Elasticsearch)
- No purchase-specific indexing in this path; only stock-event projections feed search indirectly.

IDENTITY / TENANT
- Every entity is TenantId-scoped; ITenantContext.RequiredTenantId enforced. Cross-tenant write is
  blocked by PosTenantForeignKeyCoOwnershipGuard at SaveChanges.

ORDERS / SALES / RETURNS (storefront)
- No coupling. Purchase returns are ReturnType.PurchaseReturn; sales returns are SalesReturn
  (distinct entity discriminator, distinct accounting).

================================================================
# VALIDATION CHECKLIST (operational)
================================================================

AFTER PURCHASE CREATED (Status=Completed):
- PurchaseInvoice row exists with Status=Completed, generated InvoiceNumber (PUR-yyyymmdd-NNNN).
- Each item's BaseQuantity added to ProductVariantStocks in the chosen warehouse.
- StockMovement (Purchase) rows exist with Price = base unit cost.
- Product.PurchasePrice reflects new weighted average.
- SupplierTransaction Purchase (credit) + PaymentMade (if paid) exist; supplier BalanceAfter correct.
- AccountingOutboxMessages row (PosPurchase) present AND marked Processed with a JournalId.
- JournalEntries posted: Inventory debited, cash/card/ payable credited.
- ProductVariantStockChangedEvent published (projections updated).

AFTER UPDATE (PUT):
- AccountingMutationVersion incremented.
- Prior stock/cost/supplier-ledger/outbox effects reverted, then new ones applied.
- New PurchaseReversal + new PosPurchase (operationKey) outbox rows; old journals reversed.
- Supplier balance rebuilt/consistent.

AFTER PARTIAL RETURN:
- Stock reduced by returned BaseQuantity; StockMovement (PurchaseReturn) written.
- SupplierTransaction PurchaseReturn (debit) written; payable reduced.
- PurchaseReturn journal posted (DEBIT payable, CREDIT inventory).
- Original invoice Status = PartiallyReturned.
- Return amount derived from original proportions.

AFTER FULL RETURN:
- As above; original invoice Status = FullyReturned. Further returns blocked.

AFTER DELETE (draft only):
- PurchaseInvoice row removed (soft? hard — controller uses Remove, global filter IsDeleted).
- Stock reverted, cost restored, AccountLedgers + SupplierTransactions for invoice removed.
- RebuildPartyBalanceAsync reconciles supplier.

AFTER COMPLETION / CANCELLATION:
- "Completion" = the create itself. There is no separate completion step.
- "Cancellation" = delete (draft) or return (completed). Verify no posted journal remains orphaned.

================================================================
# COMMON FAILURE SCENARIOS (implementation-proven)
================================================================

DUPLICATE PURCHASE
- Cause: client retries without Idempotency-Key, or reuses key with different body.
- Symptom: two invoices, or 409 "already used for a different ...".
- Investigate: PurchaseRequestIdempotency for the key; confirm fingerprint match; check stored response.

INVENTORY NOT INCREASED
- Cause: create rolled back (uncaught exception → 500, ChangeTracker cleared) OR stock event
  projection lag (Mongo/Redis show old value though Postgres is correct).
- Investigate: Postgres ProductVariantStocks first; then StockMovements; then ProductVariantStockChangedEvent
  delivery; then check the HTTP response (500 = rolled back).

WRONG WAREHOUSE
- Cause: request WarehouseId invalid/inactive (400) or UI passed wrong id. Stock is warehouse-scoped;
  wrong warehouse means stock landed elsewhere.
- Investigate: PurchaseInvoice.WarehouseId; ProductVariantStocks by (warehouse, variant); revert via delete/return + recreate.

WRONG COST
- Cause: weighted-average uses TOTAL product stock across ALL variants/warehouses (by design).
  A large prior stock base dilutes the new cost. Or UnitCost was entered wrong.
- Investigate: Product.PurchasePrice vs computed weighted average; confirm ConversionFactor on the unit.

ACCOUNTING MISMATCH
- Cause: outbox worker posted a stale-version message (should be skipped), or journal post failed
  (logged, return committed) and outbox retry pending.
- Investigate: JournalEntries (SourceType=PurchaseInvoice, SourceId=invoiceId); AccountingOutboxMessages
  (EventType PosPurchase/PurchaseReversal/PurchaseReturn, ProcessedAtUtc, IsFailed, AttemptCount);
  CommerceAccountingAudits; verify Inventory/Payable/Cashbox legs sum to Total.

SUPPLIER MISMATCH
- Cause: return submitted with SupplierId/WarehouseId != original (400). Or supplier deleted
  (return → "Original purchase supplier is no longer authoritative").
- Investigate: Return.SupplierId vs original PurchaseInvoice.SupplierId; SupplierTransactions.BalanceAfter;
  RebuildPartyBalanceAsync if ledger drifted.

PARTIAL RETURN MISMATCH / OVER-RETURN
- Cause: sum of returns exceeds received quantity → 400. Or Available stock < return qty → 400.
- Investigate: ReturnItems where Return.OriginalPurchaseInvoiceId = invoiceId; compare to
  PurchaseInvoiceItem.BaseQuantity; check variant stock AvailableQuantity.

PURCHASE STUCK (cannot delete/update)
- Cause: status=Completed (must use return) OR posted journals exist (POSTED_JOURNALS_EXIST) OR
  outbox work claimed/retrying/failed (PurchaseFinancialIntegrityService blocks mutation).
- Investigate: invoice.Status; JournalEntries posted for invoice; AccountingOutboxMessages
  (IsFailed/ClaimedUntilUtc/AttemptCount/LastError).

================================================================
# DEBUGGING GUIDE (where to look first)
================================================================

1. START: Read the PurchaseInvoice row (Status, TotalAmount, PaidAmount, WarehouseId, SupplierId,
   AccountingMutationVersion, InvoiceNumber). This is the single source of truth.
2. SUPPLIER: If balances look wrong → SupplierTransactions for SupplierId + RebuildPartyBalanceAsync;
   check AccountLedgers. Look when a payable seems too high/low after a purchase/return.
3. WAREHOUSE: If stock is in the wrong place → confirm WarehouseId; check ProductVariantStocks by
   (warehouse, variant). Stock is Warehouse-scoped, never moved.
4. INVENTORY: If quantities are off → StockMovements (MovementType Purchase/PurchaseReturn/
   Adjustment) for ReferenceId = invoice/return id; then ProductVariantStocks; then confirm the
   ProductVariantStockChangedEvent was consumed by projections.
5. COST: If PurchasePrice is wrong → recompute weighted average using TOTAL product stock (all
   variants/warehouses). Remember cost math is product-level, not warehouse/variant-level.
6. ACCOUNTING: If ledger is unbalanced → JournalEntries (SourceType=PurchaseInvoice) + outbox
   (EventType PosPurchase/PurchaseReversal/PurchaseReturn) + CommerceAccountingAudits. Verify
   Inventory/Payable/Cashbox legs. Outbox processor skips stale versions by design.
7. OUTBOX: If a purchase seems recorded but not posted, or double-posted → AccountingOutboxMessages
   (ProcessedAtUtc, IsFailed, ClaimedUntilUtc, AttemptCount, IdempotencyKey, AggregateVersion).
   The synchronous path normally posts before the response; the worker is the replay/fallback.
8. PROJECTION: If UI/search shows stale stock but Postgres is correct → it is a Mongo/Redis/ES
   projection lag from ProductVariantStockChangedEvent; fix the projection, not the purchase.

================================================================
# RELATED KNOWLEDGE
================================================================

CORE GUIDES (look up in repo docs / .ai context):
- Supplier Management capability (supplier master + ledger).
- Inventory / Stock Management capability (ProductVariantStocks, StockMovements).
- Accounting & Outbox capability (JournalEntries, AccountingOutboxMessages, AccountingOutboxProcessor).
- Warehouse Management capability.
- Tenant isolation rules (ITenantContext.RequiredTenantId; cross-tenant = CRITICAL).

SYSTEM GUIDES:
- README (note: stale per project rules; implementation wins).
- docs/TREASURY_AUDIT_CERTIFICATION.md (mentions EnqueuePosPurchaseAsync migration).
- PHASE-3-IMPLEMENTATION-COMPLETE.md (posting pipeline overview).

RELATED CAPABILITIES:
- Purchase Returns (this document's return section; ReturnType.PurchaseReturn).
- Supplier Payments (separate; later manual settlement of payables).
- Sales Invoices / Storefront Orders / Checkout — DO NOT reuse their lifecycle for purchases.
- Expenses (PostPosExpenseAsync) — separate outbound-cost document.

RELATED DOMAINS:
- Product cost (PurchasePrice), Inventory valuation, Supplier payable, Double-entry ledger,
  Idempotency (request-level), Event-driven projections (Mongo/Redis/ES).

================================================================
# DOCUMENTATION VALIDATION (implementation vs docs)
================================================================

REPORTED ONLY (no doc rewritten):

MISSING DOCUMENTATION
- No implementation-backed document describing that purchases are created atomically as Completed
  (no draft/approval/receiving stages). The help article implies an editable "saved invoice" but
  does not state there is no approval/receiving workflow.
- No doc explains AccountingMutationVersion / stale-version outbox skip (critical for debugging
  double-posting).
- No doc explains that Product.PurchasePrice is a PRODUCT-LEVEL weighted average across all
  variants/warehouses (a common misread is per-warehouse or per-variant cost).

OUTDATED / INCORRECT RULES
- The InvoiceStatus enum includes Draft and Cancelled, but NEITHER is ever assigned by purchase
  code. Any doc implying purchases can be "drafted" or "cancelled" into those states is incorrect;
  cancellation = delete (draft) or return (completed).
- DiscountAmount/DiscountPercentage columns exist on PurchaseInvoice but are NEVER computed or
  applied in the purchase path — docs/UI implying purchase-level discounts are misleading.
- Currency/ExchangeRate and Additional Expenses / Landed Cost are absent from the implementation
  despite being typical ERP purchase features; docs should not assume them.

ARCHITECTURE INCONSISTENCIES
- `AuthoritativePurchaseAnalyticsConsumer` name suggests it handles POS purchases, but it only
  consumes OrderPaidEvent (storefront orders) and emits PurchaseCompleted analytics. POS purchases
  do NOT feed this analytics path. Naming is misleading.
- "Cancelled" status in the enum is effectively dead for purchases (only read as a guard).
- PurchaseInvoicePayments entity exists but the buy-side payment at receipt is recorded as a
  SupplierTransaction (PaymentMade), not into PurchaseInvoicePayments in the create path.

HIDDEN IMPLEMENTATION BEHAVIOR
- Purchases are SYNCHRONOUSLY posted to accounting inside the HTTP request (PHASE LCR), then the
  outbox row is marked processed. The outbox/worker is a replay/fallback, not the primary path.
- Create locks Products and ProductVariantStocks with FOR UPDATE / UPDLOCK to prevent race-induced
  cost/stock corruption.
- Return amounts are AUTHORITATIVELY recomputed from original invoice proportions, ignoring client
  UnitPrice/TaxRate for valuation (client values still accepted but overwritten).
- Cashbox capacity is pre-validated; failure fails CLOSED (purchase not recorded; 503 if treasury
  read errors).
- Idempotency keys are sanitized in logs to avoid leaking cash/card amounts.
- Update is a full rewrite: it reverts all prior effects then re-applies, incrementing
  AccountingMutationVersion (not an incremental patch).

INCORRECT LIFECYCLE ASSUMPTIONS
- Do NOT assume Purchase follows Sales/Checkout lifecycle (Draft→Approval→Receiving→Completed).
  The proven lifecycle is: create→Completed, then Update/Return/Delete(draft).

================================================================
# QUALITY SUMMARY (end of document)
================================================================

This document lets Hermes:
- Understand Purchase Management as an atomic, completed-on-create inbound document.
- Navigate the implementation (controllers, services, outbox, accounting, inventory, supplier).
- Execute purchase workflows (create, update, return, delete-draft) with correct rules.
- Validate purchase behavior (state machine, costing, accounting legs, idempotency).
- Debug purchase issues (stock, cost, supplier, accounting, outbox, projection) with a clear
  investigation order.
- Understand supplier interactions (ledger, payable, return debit).
- Understand inventory impact (warehouse-scoped BaseQuantity, stock events).
- Understand costing (product-level weighted average; no FIFO/LIFO/landed-cost/currency).
- Understand accounting interaction (Inventory/Payable/Cashbox journal; synchronous + outbox).

================================================================
# OUTPUT CERTIFICATIONS
================================================================

- Summary of discoveries: Purchase Management is an atomic, completed-on-create inbound document.
  No draft/approval/receiving stages. It increases warehouse stock, recalculates product-level
  weighted-average cost, opens a supplier payable, and posts a double-entry journal synchronously
  (with outbox replay safety). Returns are partial/full reversals; delete only undoes a non-posted
  invoice. Strong idempotency, row-level locking, and fail-closed guards throughout.

- Purchase lifecycle discovered:
  create → Completed; Completed →(PUT) Completed (new version); Completed →(partial return) PartiallyReturned;
  Completed →(full return) FullyReturned; (not-Completed) →(DELETE) removed.

- Purchase state machine discovered: Draft [never assigned], Completed [create], PartiallyReturned
  [partial return], FullyReturned [full return], Cancelled [never assigned; read-only guard].
  No Approved/Receiving/Pending states.

- Business rules discovered: supplier/warehouse existence+active; product/variant/unit validation;
  UnitCost>0; required Idempotency-Key + fingerprint dedup; payment split by method; cashbox capacity
  fail-closed; total=subtotal+tax; update requires ExpectedMutationVersion + no prior returns; delete
  blocked if Completed/has-returns/posted-journals; return bounds (<=received, available stock, same
  supplier/warehouse/unit); authoritative return amounts.

- Costing behavior discovered: Product-level weighted average across ALL variants/warehouses in
  Product.PurchasePrice. Revert/return restore cost proportionally. No FIFO/LIFO/landed-cost/
  currency/exchange/discount applied. Return valuation derived from original invoice proportions.

- Cross-system dependencies discovered: Products (PurchasePrice), Inventory (ProductVariantStocks +
  StockMovements + events → Mongo/Redis/ES), Warehouse (stock scope + cashbox capacity/account),
  Suppliers (ledger + payable + rebuild), Accounting (JournalEntries + outbox + audits), Payments
  (initial PaymentMade only), Analytics (storefront orders only — NOT purchases), Identity/Tenant
  (RequiredTenantId enforced). No coupling to Sales/Storefront/Checkout lifecycle.

- Documentation inconsistencies found: Draft/Cancelled enum states never assigned by purchase code;
  discount columns unused; no currency/landed-cost; AuthoritativePurchaseAnalyticsConsumer misnamed
  (storefront orders only); PurchaseInvoicePayments not written on create; help article omits the
  no-approval/no-receiving reality; missing docs on AccountingMutationVersion + product-level cost.

- Hidden implementation behavior found: synchronous accounting post inside request (PHASE LCR) with
  outbox marked processed; row-level locks on Products + Stocks; return amounts authoritatively
  recomputed; fail-closed cashbox; sanitized idempotency keys in logs; update = full revert+reapply;
  outbox processor skips stale mutation versions.

- Confirmation that only this file was created: YES. Created exactly one file:
  .ai/hermes/capabilities/pos-admin/purchase-management.md

- Confirmation that no other files were modified: YES. No source, config, doc, or other file was
  changed, refactored, or generated.

- Confirmation that no memory writes occurred: YES. No memory tool calls were made.
