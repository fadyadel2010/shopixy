# FLOW-ACC-09 — Statement Of Account (POS Admin) Architecture

> Canonical, implementation-backed architecture reference for the Statement Of Account (SOA) domain.
> Source of truth: the Shopixy source tree. Every claim below is cited to a file:line.
> Status legend: **IMPLEMENTED** | **PARTIALLY IMPLEMENTED** | **NOT IMPLEMENTED**.

Scope: Customer Statement, Supplier Statement, Party Ledger, Statement Runtime, Outstanding Balance, Running Balance, Browser Testing.
Excluded (per brief): General Ledger Reports, Trial Balance, P&L, Balance Sheet, Cash Flow, CommerceHub Statements.

---

## 1. Executive Summary

**What is Statement Of Account?**
A runtime, on-demand projection of a party's (Customer or Supplier) financial movement, computed live from the unified `AccountLedger` party ledger. It is not a stored document and not a separate aggregate. The SOA is read/derived; the financial truth lives in `AccountLedger` rows.

**Who owns it?**
- Controller: `StatementOfAccountController` (`PointOfSale.API/Controllers/StatementOfAccountController.cs:25`) — read (query/balance/outstanding) + write (opening-balance/adjustment) entry point.
- Mutation engine: `LedgerService` (`PointOfSale.API/Services/LedgerService.cs:13`) — solely owns the running-balance write path and the row-level party lock.
- Domain entities: `AccountLedger` (unified party ledger), `CustomerTransaction`, `SupplierTransaction`, `PartyLedgerAdjustment` (all `PointOfSale.Core/Entities`).

**What does it own?**
- Live reading and aggregation of `AccountLedger` per party/date-range.
- Opening-balance seeding and manual adjustments (Credit/Debit Note + Credit/Debit Adjustment).
- Outstanding-balance enumeration of all parties with non-zero balance.

**What does it NOT own?**
- It does NOT own balances. Balances are owned by `AccountLedger.RunningBalance` and the cached `Customer.Balance` / `Supplier.Balance` columns (maintained by `LedgerService`).
- It does NOT own the General Ledger. SOA reads the Party Ledger only. GL posting is a downstream concern driven by the Accounting Outbox (see §11).
- It does NOT own Customer/Supplier master data, Sales, Purchases, Payments — those modules *write* into `AccountLedger` via `LedgerService`, but SOA does not own them.

**Source of truth:** `AccountLedger` (the Party Ledger). SOA is a read projection over it.

---

## 2. Discovery (Implemented Components)

| Component | Kind | Evidence |
|---|---|---|
| `StatementOfAccountController` | API controller | `Controllers/StatementOfAccountController.cs:25` |
| `SOAFilterRequest` / `SOAResponse` / `SOASummary` / `SOATransaction` / `CreateOpeningBalanceRequest` / `CreateAdjustmentRequest` / `PartyLedgerAdjustmentResponse` / `PartyBalanceResponse` | DTOs | `DTOs/StatementOfAccountDTOs.cs` |
| `AccountLedger` (Party Ledger) | Entity | `Core/Entities/AccountLedger.cs:9` |
| `CustomerTransaction` | Entity | `Core/Entities/CustomerTransaction.cs:9` |
| `SupplierTransaction` | Entity | `Core/Entities/SupplierTransaction.cs:9` |
| `PartyLedgerAdjustment` | Entity (manual adjustment audit) | `Core/Entities/PartyLedgerAdjustment.cs:10` |
| `ILedgerService` / `LedgerService` | Service (running balance + locks) | `Services/LedgerService.cs:13` |
| `IAccountingOutboxService` (`EnqueueSupplierOpeningBalanceAsync`, `EnqueuePartyLedgerAdjustmentAsync`) | Outbox (GL projection trigger) | `Core/Interfaces/Accounting/IAccountingOutboxService.cs:95,100` |
| `AccountingOutboxProcessor` (handlers `SupplierOpeningBalance`, `PartyLedgerAdjustment`) | Background projection to GL | `Infrastructure/Accounting/AccountingOutboxProcessor.cs:57-58,723-724,1685,1758` |
| Tenant query filter on `AccountLedger` | DB config | `Infrastructure/Data/ApplicationDbContext.cs:660-661` |
| Frontend: `StatementOfAccount.jsx`, `OutstandingBalances.jsx`, `Adjustments.jsx`, `soaApi.js` | Browser | `PointOfSale.Web/src/pages/...`, `PointOfSale.Web/src/services/soaApi.js` |

**Endpoints (all IMPLEMENTED):**
- `POST /api/StatementOfAccount/query` — statement with summary + paginated transactions (`StatementOfAccountController.cs:63`)
- `GET /api/StatementOfAccount/balance/{partyType}/{partyId}` — current party balance (`StatementOfAccountController.cs:222`)
- `POST /api/StatementOfAccount/opening-balance` — seed opening balance (`StatementOfAccountController.cs:263`)
- `POST /api/StatementOfAccount/adjustment` — Credit/Debit Note/Adjustment (`StatementOfAccountController.cs:534`)
- `GET /api/StatementOfAccount/outstanding/{partyType}` — non-zero balances list (`StatementOfAccountController.cs:925`)

**NOT IMPLEMENTED (searched, absent):**
- `StatementExport` / `StatementPdf` / `StatementExcel` — no code anywhere. `SOA-MODULE-GUIDE.md:357-364` lists PDF/email/CSV export as "Future Enhancements".
- No print function in `StatementOfAccount.jsx` or `OutstandingBalances.jsx` (grep for `window.print`/`export`/`pdf` → only the module `export default`).
- No grouping, no multi-currency column, no aging buckets inside the SOA query (aging is a separate report — see §7/§9).

---

## 3. Statement Identity

| Identity attribute | Implemented? | Evidence / behavior |
|---|---|---|
| Statement Type (Customer/Supplier) | IMPLEMENTED | `SOAFilterRequest.PartyType` (`DTOs/StatementOfAccountDTOs.cs:7`); filter `a.PartyType == request.PartyType` (`Controller.cs:74`) |
| Customer / Supplier target | IMPLEMENTED | `PartyId` optional; when set → `a.PartyId == request.PartyId.Value` (`Controller.cs:78-81`) |
| Date Range | IMPLEMENTED | `StartDate`/`EndDate` → `TransactionDate >= /> <=` (`Controller.cs:84-92`) |
| Opening Balance | IMPLEMENTED (derived) | = last `AccountLedger.RunningBalance` with `TransactionDate < StartDate` (`Controller.cs:95-107`) — i.e. read from the ledger, not a stored field on the statement |
| Closing Balance | IMPLEMENTED (computed) | `effectiveOpeningBalance + totalDebit - totalCredit` (`Controller.cs:152-155`) |
| Running Balance | IMPLEMENTED | `SOATransaction.RunningBalance = a.RunningBalance` straight from `AccountLedger` (`Controller.cs:126`) |
| Outstanding Balance | IMPLEMENTED (separate endpoint) | `GET .../outstanding/{partyType}` enumerates non-zero balances (`Controller.cs:925`) |
| Currency | NOT IMPLEMENTED | `AccountLedger` has no currency column; `RoundCurrency` hard-floors to 2 decimals (`Controller.cs:919-920`). Single-currency only. |
| Filters | IMPLEMENTED | PartyType, PartyId, StartDate, EndDate, pagination, and (outstanding) `search` by name (ILike) (`Controller.cs:937-969`) |
| Export | NOT IMPLEMENTED | no endpoint/handler/DTO |
| Audit | PARTIAL | Adjustments call `IAuditService.LogCreate` (`Controller.cs:743`); opening balance enqueues outbox; but a GET query is not audited. `RebuildPartyBalanceAsync` writes a `RebuildBalance` audit (`LedgerService.cs:148`). |

---

## 4. Domain Ownership

| Concept | Owning component | Evidence |
|---|---|---|
| Statement (the read projection) | `StatementOfAccountController` (computed, not stored) | `Controller.cs:64-217` |
| Party Ledger (financial truth) | `AccountLedger` entity + `LedgerService` writers | `AccountLedger.cs:9`; `LedgerService.cs:196-278` |
| Customer Transactions | `CustomerTransaction` entity, written by `LedgerService` + domain modules | `CustomerTransaction.cs:9`; `Controller.cs:488-503` |
| Supplier Transactions | `SupplierTransaction` entity, written by `LedgerService` + domain modules | `SupplierTransaction.cs:9`; `Controller.cs:350-365` |
| Outstanding Balance | Derived from `AccountLedger` sums (no stored outstanding table) | `Controller.cs:947-964` |
| Running Balance | Owned by `AccountLedger.RunningBalance`, computed in `LedgerService.ApplyPartyTransactionAsync` | `LedgerService.cs:218,231` |
| Opening Balance | Seed via controller → `LedgerService.Apply*Transaction(OpeningBalance)` | `Controller.cs:335,474` |
| Closing Balance | Computed in query from aggregates + opening | `Controller.cs:152-155` |
| Statement Export | NOT OWNED (not implemented) | — |
| Read Models | None — SOA reads live `AccountLedger` (`AsNoTracking`) | `Controller.cs:72-74` |

---

## 5. Lifecycle (Implemented Stages)

| Stage | Implemented? | Evidence |
|---|---|---|
| Generate (read projection) | IMPLEMENTED | `GetStatementOfAccount` builds SOAResponse on demand (`Controller.cs:64`) |
| Calculate (opening/debit/credit/closing) | IMPLEMENTED | `Controller.cs:95-155` |
| Read | IMPLEMENTED | `query`, `balance`, `outstanding` all live reads |
| Refresh | PARTIAL | No explicit "refresh" — data is always live. A `RebuildPartyBalanceAsync` exists to repair `Customer/Supplier.Balance` from the ledger (`LedgerService.cs:105-194`) but is not triggered by SOA reads |
| Export | NOT IMPLEMENTED | — |
| Print | NOT IMPLEMENTED (in SOA) | (Aging report has print; SOA does not) |
| Archive | NOT IMPLEMENTED | `AccountLedger.IsDeleted` soft-delete exists (`ApplicationDbContext.cs:661`) but no statement archive concept |

**Write lifecycle (opening balance & adjustment):** HTTP → controller validates + claims idempotency key → `BeginTransactionAsync(ReadCommitted)` → load party `FOR UPDATE` → `LedgerService.Apply*Transaction` (writes `AccountLedger`, updates `Customer/Supplier.Balance`) → add `CustomerTransaction`/`SupplierTransaction`/`PartyLedgerAdjustment` → `IAccountingOutboxService.Enqueue*` → `SaveChangesAsync` → complete idempotency → commit (`Controller.cs:263-762`, `LedgerService.cs:208-245`).

---

## 6. Aggregate Analysis

- **Is Statement an aggregate?** NO. There is no `Statement` entity or repository. The SOA is a computed response (`SOAResponse`). The true aggregate root is the **Party** (`Customer` / `Supplier`); `AccountLedger` is its event-sourced-style ledger of financial mutations.
- **Party Ledger aggregate:** `AccountLedger` is a child collection keyed by `(TenantId, PartyType, PartyId)`. Mutated only through `LedgerService.ApplyPartyTransactionAsync` under a row lock.
- **Customer Ledger / Supplier Ledger:** `CustomerTransaction` / `SupplierTransaction` are denormalized mirrors of `AccountLedger` per party type (each write adds a row — `Controller.cs:350,488`). They carry `BalanceAfter` but are NOT the authority; `AccountLedger.RunningBalance` is.
- **Outstanding Balance:** not an aggregate — a query result over grouped `AccountLedger` sums (`Controller.cs:947-964`).
- **Running Balance:** value-object owned by `AccountLedger`.
- **Read Models / Exports:** none exist for SOA.

---

## 7. Statement Behavior (Implementation-Backed)

**Customer Statement** (`PartyType.Customer`): reads `AccountLedger` where `PartyType==Customer`; debit increases receivable, credit decreases it (`SOA-MODULE-GUIDE.md:35-39`).

**Supplier Statement** (`PartyType.Supplier`): credit increases payable (`we owe`), debit decreases it (`SOA-MODULE-GUIDE.md:41-45`).

**Opening Balance:** seeded once per party. Guard: `customer.OpeningBalanceAmount.HasValue || existingOpeningBalance` → 409 Conflict (`Controller.cs:319,459`). Also enforced inside `LedgerService.EnsureOpeningBalanceNotAlreadyAppliedAsync` (throws `DuplicateOpeningBalanceException`) (`LedgerService.cs:319-358`).

**Closing Balance:** `effectiveOpeningBalance (= openingBalance + openingBalanceAdjustment) + totalDebit - totalCredit` (`Controller.cs:148-155`). Note opening-balance transactions are *excluded* from `TotalDebit`/`TotalCredit` (`Controller.cs:139-144`) but their value is folded into the opening figure.

**Running Balance:** taken verbatim from `AccountLedger.RunningBalance` (`Controller.cs:126`). Stored, not recomputed in the query.

**Outstanding Balance:** `GET .../outstanding/{partyType}` joins `Customers`/`Suppliers` to a grouped `AccountLedger` sum, keeps only `Balance != 0`. For customers, `TotalPaid` = sum of `CreditAmount` where `PaymentReceived`, `TotalInvoiced` = sum `DebitAmount` where `Sale`. For suppliers, `TotalPaid` = sum `DebitAmount` where `PaymentMade`, `TotalInvoiced` = sum `CreditAmount` where `Purchase`; `DueDate` derived from `PurchaseInvoices` (`Controller.cs:945-1129`).

**Filtering:** PartyType, PartyId, StartDate, EndDate (`Controller.cs:74-92`); outstanding also `search` (name ILike) (`Controller.cs:966-969`).

**Sorting:** transactions ordered `TransactionDate, CreatedAt` (`Controller.cs:113-115`); outstanding ordered `Name, Id` (`Controller.cs:983-984`).

**Grouping:** NOT IMPLEMENTED in SOA query (no group-by in response). The aging *report* groups by bucket but is a separate endpoint.

**Payment History:** NOT inside SOA; supplied by `GET /api/Payments/customer/{id}` and `/supplier/{id}` (`soaApi.js:82-95`).

**Invoice History:** exposed indirectly as Sale/Purchase ledger rows in the statement; no dedicated invoice endpoint in SOA.

**Adjustments:** CreditNote/DebitNote/CreditAdjustment/DebitAdjustment only (validated `Controller.cs:568-578`). Each writes a `PartyLedgerAdjustment` (intent/audit), a ledger entry, a `*Transaction` row, and an outbox message.

---

## 8. Financial Behavior

**Financial Truth:** `AccountLedger` (Party Ledger). Each row is immutable-ish (soft-delete only); `RunningBalance` is the authoritative running total per party.

**Party Truth:** `Customer.Balance` / `Supplier.Balance` columns are a *cache* of the latest `AccountLedger.RunningBalance`, maintained transactionally by `LedgerService.SetTrackedPartyBalance` (`LedgerService.cs:430-446`). They can diverge and be repaired via `RebuildPartyBalanceAsync` (`LedgerService.cs:105`).

**Ledger Source:** SOA reads `AccountLedger` directly (live, `AsNoTracking`) (`Controller.cs:72`). It does NOT read `GeneralLedgerReport` / GL tables.

**Running Balance Calculation:** `newBalance = currentBalance + debitAmount - creditAmount`, where `currentBalance` = last `AccountLedger.RunningBalance` for the party (or 0) (`LedgerService.cs:217-218,280-317`). Stored on the new ledger row.

**Opening Balance:** a `TransactionType.OpeningBalance` ledger row written via `LedgerService` (`Controller.cs:335,474`); its `RunningBalance` becomes the baseline.

**Closing Balance:** derived in query (§7).

**Outstanding Calculation:** `SUM(DebitAmount - CreditAmount)` over the party's `AccountLedger` rows (`Controller.cs:955,1040`).

**Customer Balance / Supplier Balance:** `GET .../balance/...` = `SUM(DebitAmount - CreditAmount)` from `AccountLedger` (`Controller.cs:227-230`) — recomputed live, NOT read from the cached `Customer.Balance` column.

**Read Model Strategy:** NONE. Every SOA read is a live SQL aggregation over `AccountLedger`. There is no materialized statement, no cache, no projection table specific to SOA.

---

## 9. Constraints (Implementation-Backed Rules)

| Rule | Evidence |
|---|---|
| Tenant Isolation (mandatory) | Global query filter `!e.IsDeleted && e.TenantId == CurrentTenantId` on `AccountLedger` (`ApplicationDbContext.cs:660-661`); writes stamp `TenantId = _tenantContext.RequiredTenantId` (`Controller.cs:353,491`). Cross-tenant SOA read is impossible under the filter. |
| Party Isolation | Every SOA query scopes `PartyType` + `PartyId` (`Controller.cs:74-81`); outstanding joins within one party type only (`Controller.cs:945-1049`). |
| Single Opening Balance per Party | 409 if `OpeningBalanceAmount.HasValue` or an `OpeningBalance` ledger row exists (`Controller.cs:319,459`); `DuplicateOpeningBalanceException` (`LedgerService.cs:319-358`). |
| Running Balance Integrity | Computed under a row lock (`FOR UPDATE` / `UPDLOCK,ROWLOCK,HOLDLOCK`) at `ReadCommitted` (`LedgerService.cs:208-211,448-540`); `RebuildPartyBalanceAsync` can recompute the full chain if corrupted (`LedgerService.cs:366-428`). |
| Outstanding Integrity | Only `Balance != 0` parties returned (`Controller.cs:958,1043`); aggregated from live ledger. |
| Date Validation | Range filters applied on `TransactionDate`; opening balance uses `< StartDate` for baseline (`Controller.cs:84-107`). No explicit date-ordering validation beyond query predicates. |
| Currency Integrity | NOT enforced — no currency column; amounts rounded to 2 dp (`Controller.cs:919-920`). Multi-currency is NOT IMPLEMENTED. |
| Idempotency (writes) | `IFinancialMutationIdempotencyService.TryClaimAsync` + SHA256 request hash per tenant (`Controller.cs:284-288,548,844-876`); keys scoped `PARTYTYPE:TenantId:OperationId` (`Controller.cs:828-829`). |
| Adjustment Type Whitelist | Only CreditNote/DebitNote/CreditAdjustment/DebitAdjustment accepted (`Controller.cs:568-578`). |
| Aging | NOT part of SOA query. Aging buckets (Current/1-30/31-60/61-90/90+) exist only in `ReportsController` (`accounts-receivable-aging`, `accounts-payable-aging`) reading `AccountLedgers` (`ReportsController.cs:654-705`). SOA itself does not age. |

---

## 10. SQL Transactions

- **Reads live?** YES. `GetStatementOfAccount` queries `_context.AccountLedgers` with `AsNoTracking()` (`Controller.cs:72-74`). No snapshot, no cache, no projection table.
- **Reads snapshots/caches/projections?** NO. Pure live read of the Party Ledger.
- **Uses transactions?** Write paths only: `BeginTransactionAsync` in controller + `LedgerService` (when no ambient tx). Reads are implicit.
- **Isolation level:** `IsolationLevel.ReadCommitted` for write transactions (`LedgerService.cs:117,210`).
- **Consistency guarantees:** Within a write, the party row is locked (`FOR UPDATE` on Postgres, `UPDLOCK,ROWLOCK,HOLDLOCK` on SQL Server — `LedgerService.cs:472-540`) so concurrent mutations to the same party are serialized; running balance stays additive. Reads see committed data (ReadCommitted) — therefore SOA is **eventually consistent** w.r.t. the cached `Customer/Supplier.Balance` column only if a rebuild is needed, but the ledger itself is the consistent source.
- **Is Statement eventually consistent?** The projection is computed at read time from committed `AccountLedger` rows, so it is **consistent with the ledger at read moment** (no staleness). The only "eventual" part is GL propagation via the outbox (§11).

---

## 11. Events

| Event | Implemented? | Evidence |
|---|---|---|
| Statement Refresh | NOT IMPLEMENTED | no event; read is on-demand live |
| Projection Updates (to GL) | IMPLEMENTED (downstream) | `IAccountingOutboxService.EnqueueSupplierOpeningBalanceAsync` / `EnqueuePartyLedgerAdjustmentAsync` (`IAccountingOutboxService.cs:95,100`); processor posts to GL via `ICommerceAccountingService` (`AccountingOutboxProcessor.cs:723-724,1740,1760`) |
| Cache Updates | PARTIAL | `Customer.Balance`/`Supplier.Balance` updated synchronously inside the write tx (`LedgerService.cs:430-446`); a separate `RebuildPartyBalanceAsync` exists for repair (`LedgerService.cs:105`) |
| Outstanding Updates | IMPLICIT | Outstanding is computed live, so it "updates" automatically as ledger rows are added; no dedicated event |

The outbox messages are `SupplierOpeningBalance` and `PartyLedgerAdjustment` (`AccountingOutboxProcessor.cs:57-58`) — these feed the General Ledger, NOT a SOA read model (SOA has none).

---

## 12. Runtime Architecture (Implementation-Backed)

Read path:
```
Browser (StatementOfAccount.jsx)
  → soaApi.getStatement(filters)            PointOfSale.Web/src/services/soaApi.js:6
  → POST /api/StatementOfAccount/query       Controller.cs:63
  → _context.AccountLedgers (AsNoTracking)   Controller.cs:72
       (global tenant filter applied)         ApplicationDbContext.cs:660
  → aggregate Opening/Debit/Credit/Closing    Controller.cs:95-155
  → SOAResponse (paginated SOATransaction[])  Controller.cs:190-208
  → Browser table + summary cards            StatementOfAccount.jsx:177-309
```

Write path (opening balance / adjustment):
```
Browser (Adjustments.jsx / opening-balance form)
  → soaApi.createOpeningBalance / createAdjustment   soaApi.js:22,30
  → POST /api/StatementOfAccount/opening-balance | /adjustment
  → IFinancialMutationIdempotencyService.TryClaimAsync   Controller.cs:284,551
  → BeginTransactionAsync(ReadCommitted)                Controller.cs:283,550
  → LedgerService.ApplyCustomer/SupplierTransaction      Controller.cs:335,474,661,690
       → lock party FOR UPDATE                           LedgerService.cs:448-540
       → newBalance = prev + debit - credit             LedgerService.cs:218
       → AccountLedger.RunningBalance = newBalance      LedgerService.cs:231
       → Customer/Supplier.Balance = newBalance         LedgerService.cs:430-446
  → Add CustomerTransaction/SupplierTransaction/PartyLedgerAdjustment
  → IAccountingOutboxService.Enqueue*                   Controller.cs:384,721
  → SaveChangesAsync → CompleteAsync(idempotency) → Commit
```

Outstanding path:
```
Browser (OutstandingBalances.jsx)
  → soaApi.getOutstandingBalances(partyType)   soaApi.js:38
  → GET /api/StatementOfAccount/outstanding/{partyType}
  → join Customers/Suppliers ⨝ grouped AccountLedger(Balance != 0)   Controller.cs:947-964
  → X-Total-Count / X-Total-Debit / X-Total-Credit headers           Controller.cs:1136-1141
```

---

## 13. Browser Test Specification

Frontend under test: `PointOfSale.Web/src/pages/StatementOfAccount.jsx`, `OutstandingBalances.jsx`, `Adjustments.jsx`; service `soaApi.js`.

### Scenarios
1. **Load Customer Statement** — select Customer + party, click Load → summary cards (Opening/Debit/Credit/Closing) + transaction table render. Assert `summary.closingBalance == opening + totalDebit - totalCredit` (`StatementOfAccount.jsx:177-219`).
2. **Load Supplier Statement** — switch PartyType to Supplier; same assertions, balance semantics inverted (`SOA-MODULE-GUIDE.md:41-45`).
3. **Date-range filter** — set Start/End; assert every returned `transaction.transactionDate` ∈ [start,end] (`Controller.cs:84-92`).
4. **Pagination** — statement with >50 txns; assert page controls appear and `totalPages == ceil(totalCount/50)` (`StatementOfAccount.jsx:286-308`, `Controller.cs:207`).
5. **Outstanding list** — open OutstandingBalances; assert only parties with non-zero balance listed; `X-Total-Debit`/`X-Total-Credit` headers present (`Controller.cs:958,1136-1141`).
6. **Outstanding search** — type a name fragment; assert filtered by ILike (`Controller.cs:966-969`).
7. **Running balance monotonicity** — for a single party, assert each row's `runningBalance` equals previous `runningBalance + debit - credit` (within date order) (`LedgerService.cs:218`).
8. **Opening balance once** — attempt second opening balance for same party → expect 409 (`Controller.cs:319,459`).
9. **Adjustment** — post a CreditNote; assert new ledger row + `PartyLedgerAdjustmentResponse` returned; balance moves (`Controller.cs:534-762`).

### Assertions
- Closing = Opening + Debit − Credit (with OB excluded from Debit/Credit) — `Controller.cs:148-155`.
- `runningBalance` on each row equals stored `AccountLedger.RunningBalance` — `Controller.cs:126`.
- Balance color: Debit=red, Credit=green — `StatementOfAccount.jsx:88-92,196-218`.

### Negative Cases
- No party selected → warning toast, no request (`StatementOfAccount.jsx:44-47`).
- Opening balance amount ≤ 0 → 400 (`Controller.cs:268-271`).
- Duplicate opening balance → 409 Conflict.
- Cross-tenant: a token from tenant A cannot see tenant B's ledger (global filter) — `ApplicationDbContext.cs:660`.
- Invalid adjustment type → 400 (`Controller.cs:568-578`).

### Running Balance / Outstanding / History / Filtering
- Running balance: see scenario 7.
- Outstanding: scenario 5/6; assert `Balance != 0` and `Math.Abs` applied (`Controller.cs:1022,1122`).
- History: Sale/Purchase/Payment/Return rows appear in table; payment history via `Payments` API (out of SOA page).
- Filtering: party type, party, dates, search.

### Export / Print
- **NOT IMPLEMENTED** — there is no export (PDF/Excel/CSV) and no print button in `StatementOfAccount.jsx` or `OutstandingBalances.jsx`. Tests should assert absence (or, if added later, verify the generated blob). The aging report (`AccountsReceivableAging.jsx`) has print + CSV — out of SOA scope.

---

## 14. Domain Ownership Matrix

| Domain Concept | Owner | Stored? | Notes |
|---|---|---|---|
| Statement (projection) | `StatementOfAccountController` | No (computed) | live read of `AccountLedger` |
| Party Ledger | `AccountLedger` + `LedgerService` | Yes | financial truth |
| Customer Ledger | `CustomerTransaction` | Yes (mirror) | not authoritative |
| Supplier Ledger | `SupplierTransaction` | Yes (mirror) | not authoritative |
| Outstanding Balance | query over `AccountLedger` | No | `Controller.cs:925` |
| Running Balance | `AccountLedger.RunningBalance` | Yes | `LedgerService.cs:231` |
| Opening Balance | `AccountLedger` (OpeningBalance row) | Yes | one per party |
| Closing Balance | computed in query | No | `Controller.cs:152-155` |
| Statement Export | — | N/A | NOT IMPLEMENTED |
| Read Models | — | N/A | none for SOA |
| Customer/Supplier.Balance (cache) | `LedgerService` | Yes | repairable via rebuild |

---

## 15. Canonical Pattern

SOA = **CQRS-style read projection over a unified Party Ledger**, with a **locked, additive write path**:

- One unified `AccountLedger` holds all party mutations (Customer + Supplier) as append-only rows with a stored `RunningBalance`.
- Writes go only through `LedgerService`, which locks the party row and computes `balance += debit - credit`, keeping the cached `Customer/Supplier.Balance` in sync and emitting GL outbox events.
- Reads (`query`/`balance`/`outstanding`) aggregate `AccountLedger` live with `AsNoTracking` under the tenant global filter — no cache, no projection table, no snapshot.
- "Statement" is not an entity; it is the on-demand assembled `SOAResponse`.

---

## 16. Documentation Validation (Misconceptions Corrected)

- ❌ Statement owns balances. → ✅ `AccountLedger` (via `LedgerService`) owns balances (`LedgerService.cs:218,430`).
- ❌ Statement is financial truth. → ✅ Statement is a projection over `AccountLedger` (`Controller.cs:72`).
- ❌ Statement reads General Ledger. → ✅ Statement reads the Party Ledger (`AccountLedger`) only (`Controller.cs:72`).
- ❌ Customer/Supplier Balance is financial truth. → ✅ It is a cache; `AccountLedger.RunningBalance` is truth; rebuild exists (`LedgerService.cs:105,430`).
- ❌ SOA supports export/PDF/Excel. → ✅ NOT IMPLEMENTED (no code; "Future Enhancements" in `SOA-MODULE-GUIDE.md:357`).
- ❌ SOA supports printing. → ✅ NOT IMPLEMENTED in SOA pages.
- ❌ SOA supports multi-currency. → ✅ NOT IMPLEMENTED; single currency, 2-dp rounding (`Controller.cs:919`).
- ❌ SOA computes aging. → ✅ Aging is a separate `ReportsController` feature reading `AccountLedgers` (`ReportsController.cs:654`); SOA query has no buckets.
- ❌ Opening balance can be set repeatedly. → ✅ Once per party, enforced 409 (`Controller.cs:319,459`).
- ❌ Adjustments accept any transaction type. → ✅ Only 4 whitelisted types (`Controller.cs:568-578`).
- ❌ SOA uses cached balances for the balance endpoint. → ✅ `GET .../balance` recomputes `SUM(Debit-Credit)` live (`Controller.cs:227-230`).

---

## 17. Final Architecture Conclusion

- **Statement Authority:** `AccountLedger` (Party Ledger) is the single authority. The SOA controller is a read/write facade, not an owner.
- **Source Of Truth:** live `AccountLedger` rows under the tenant global query filter.
- **Projection Strategy:** none stored — SOA is a just-in-time SQL projection (`AsNoTracking` aggregation). No read models, no caches.
- **Runtime:** Browser → `soaApi` → `StatementOfAccountController` → `AccountLedgers` (live) → `SOAResponse`. Writes → `LedgerService` (row-locked, `ReadCommitted`) → `AccountLedger` + party `Balance` cache + GL outbox.
- **Aggregate Ownership:** Party (Customer/Supplier) is the aggregate; `AccountLedger` is its ledger; `Statement` is not an aggregate.
- **Caching Strategy:** only the `Customer.Balance`/`Supplier.Balance` columns are cached (maintained by `LedgerService`, repairable via `RebuildPartyBalanceAsync`). All SOA reads bypass this cache and aggregate the ledger.
- **Integrity Strategy:** row-level party lock (`FOR UPDATE` / `UPDLOCK,ROWLOCK,HOLDLOCK`) + `ReadCommitted` + additive running-balance math + duplicate-opening-balance guard + SHA256 idempotency keys per tenant.
- **Audit Strategy:** manual adjustments logged via `IAuditService.LogCreate` (`Controller.cs:743`); balance rebuilds logged as `RebuildBalance` (`LedgerService.cs:148`); opening-balance/adjustment mutations enqueued to the Accounting Outbox for GL posting (`Controller.cs:384,721`). GET queries are not individually audited.
- **Tenant Isolation:** enforced by the EF global query filter on `AccountLedger` (`ApplicationDbContext.cs:660`) plus `RequiredTenantId` stamping on every write — cross-tenant SOA access is structurally impossible.
- **Eventual Consistency:** SOA reads are consistent with committed ledger state at read time. The only eventual leg is GL propagation through the outbox (downstream, not part of the statement projection).

---

*Evidence basis: `PointOfSale.API/Controllers/StatementOfAccountController.cs`, `PointOfSale.API/DTOs/StatementOfAccountDTOs.cs`, `PointOfSale.API/Services/LedgerService.cs`, `PointOfSale.Core/Entities/{AccountLedger,CustomerTransaction,SupplierTransaction,PartyLedgerAdjustment}.cs`, `PointOfSale.Core/Interfaces/Accounting/IAccountingOutboxService.cs`, `PointOfSale.Infrastructure/Accounting/AccountingOutboxProcessor.cs`, `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`, `PointOfSale.Web/src/pages/{StatementOfAccount,OutstandingBalances,Adjustments}.jsx`, `PointOfSale.Web/src/services/soaApi.js`, `SOA-MODULE-GUIDE.md`.*
