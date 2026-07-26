# PASS P1-ED POS Sales Authority Certification Findings

Date: 2026-06-08

## Verdict

POS Sales Authority is production-certifiable for the inspected runtime paths after hardening. PostgreSQL current tenant, customer, invoice, payment allocation, stock, return, and ledger state remain authoritative. Frontend state, route ids, DTO totals, unit prices, tax rates, cached balances, and replayed requests are evidence only. POS deferred/credit sale execution is not currently a supported mutation; `PaymentMethod.Credit` fails closed. Account-credit sales returns are bounded by original invoice authority.

## Runtime Hardening Performed

- `SalesController` now resolves sale customers through active same-tenant SQL customer authority instead of raw `FindAsync`, so deleted or foreign customers cannot receive new or rewritten POS sales.
- `SalesController` and `SalesReturnsController` now require active same-tenant warehouses before sale consumption or return restoration.
- `SalesController.PrepareSaleItemsAsync` now fails closed when a requested unit or default unit is not a current configured SQL `ProductUnit`, instead of falling back to conversion factor `1`.
- `SalesController.UpdateSale` now rejects non-draft invoice rewrites. Completed invoices must be reversed through sales returns rather than rewritten.
- `SalesReturnsController` now requires the original invoice to be `Completed` before return execution.

## Customer Authority Findings

- Tenant isolation is enforced by tenant query filters and explicit same-tenant helper checks; `ApplicationDbContext` tenant co-ownership guards reject cross-tenant sales invoice/customer edges before commit.
- Deleted customers are excluded by query filters and the new active-customer helper, so deleted customers cannot receive new POS sales.
- Customer balances are mutated through ledger/customer-transaction flows and rebuilt after sale reversal cleanup; DTO balances are not accepted as authority.

## Sales Lifecycle Findings

- Sale creation requires a sales idempotency key, claims a SQL idempotency row inside the transaction, locks stock, recomputes SQL-derived prices/taxes/totals, stages accounting outbox, synchronously posts accounting, stores replay response, and commits as one authority boundary.
- Completed invoices cannot be rewritten after hardening. Delete remains blocked for completed/posted invoices by validation and posted-journal guards.
- Invoice replay collapses through `SalesRequestIdempotency` by tenant/key/operation and request hash.

## Payment Authority Findings

- Supported POS sale payment methods are Cash, Card, and Mixed; `PaymentMethod.Credit` and deferred sale execution fail closed.
- Mixed payment requires cash plus card to exactly equal the server-calculated invoice total.
- Card must equal the server total; Cash may exceed total only as tendered cash/change, while customer-ledger payment credit is clamped to invoice total.
- There is no separate inspected incremental payment-application endpoint for POS sales invoices; duplicate sale payment effects are bounded by sale idempotency and invoice lifecycle guards.

## Inventory Boundary Findings

- Sales inventory mutation uses `ProductVariantStockMutationSupport`, which requires an explicit transaction, validates current product/variant/warehouse lineage through PASS P1-EB authority, locks stock rows deterministically, validates available quantity, and appends stock movements.
- Sales cannot consume missing stock rows or oversell locked available stock.
- Return restoration locks stock through the same P1-EB mutation support and appends movement history.

## Sales Return Findings

- Sales returns require SQL idempotency, write-lock the original invoice, derive product/variant/unit/price/tax/cost snapshots from the original invoice, ignore client price/tax overrides, and cumulatively bound return quantity and total against prior SQL returns.
- Returns cannot exceed sold quantity or original invoice total.
- Return replay collapses through tenant/key/operation idempotency and stored response.

## Financial Authority Findings

- Sale totals are recomputed from authoritative SQL product/unit/variant state plus bounded request discounts; DTO `UnitPrice` and `TaxRate` are overwritten.
- Refund totals derive from historical sale line snapshots and cumulative proration, not DTO totals.
- Accounting outbox messages carry invoice mutation version and are stale-message guarded by locked SQL invoice reads.

## Concurrency Findings

- Create/update sale idempotency claims prevent duplicate sale execution for retried request keys.
- Stock mutation row locks prevent simultaneous sales from double-consuming inventory.
- Sales returns serialize on the original invoice row and re-read prior returns before mutation, preventing concurrent over-return and duplicate inventory restoration.
- Remaining operational debt is broader PostgreSQL-backed chaos evidence for simultaneous sales/returns/payments in production-like load.

## Adversarial Findings

- DTO tampering of totals, unit prices, tax rates, mixed-payment allocation, return prices, route invoice ids, customer ids, product ids, variant ids, and warehouse ids is bounded by current SQL recomposition, row locks, query filters, and tenant co-ownership guards.
- Storefront, CommerceHub, and Affiliate order authority were not certified in this pass and remain deferred to their own scopes.

## Exact Runtime Files Modified

- `PointOfSale.API/Controllers/SalesController.cs`
- `PointOfSale.API/Controllers/SalesReturnsController.cs`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/PosSalesAuthorityCertificationTests.cs`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Remaining Debt

- No dedicated incremental POS payment allocation endpoint was identified in the inspected scope; future payment-application APIs must be separately certified.
- Deferred POS credit sales are not a supported runtime mutation in the inspected controller. If the business needs deferred sale execution, it must be implemented and certified as a separate SQL-authoritative flow.
- Operational monitoring, production reconciliation dashboards, and PostgreSQL-backed sales chaos proof remain launch-readiness debt.
- Storefront Orders, CommerceHub Orders, and Affiliate Orders are deferred authority domains.

## Required Answers

- Can Tenant A use Tenant B customers? No.
- Can deleted customers receive sales? No after hardening.
- Can invoices be replayed? No; replay collapses through SQL idempotency.
- Can payments be applied twice? Not through inspected POS sale execution; replay is idempotency-bounded and no separate incremental POS payment endpoint was certified.
- Can mixed payments exceed invoice totals? No; mixed cash plus card must equal the server total.
- Can sales consume inventory twice? No for inspected idempotent sale execution and locked stock mutation paths.
- Can returns exceed sold quantity? No.
- Can balances drift? Inspected sale and return customer balance effects are ledger/transaction-backed and bounded by SQL-current sale/return authority.
- Can race conditions create financial or inventory drift? Not in inspected sale/return execution paths after row locks, mutation versions, idempotency, and accounting outbox guards.
- Were runtime code changes required? Yes.
- Is Sales Authority production-certifiable? Yes, for inspected POS Sales, POS Payments-at-sale, account-credit returns/customer-balance effects, POS Returns, and POS Invoice Lifecycle. Deferred POS credit sale execution is fail-closed, not a certified supported sale mode.
