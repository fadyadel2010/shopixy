# FLOW-ACC-10 — Daily Closing (POS Admin) — Canonical Implementation-Backed Architecture

> Source of truth: the Shopixy implementation at `C:\Users\fadya\source\repos\PointOfSale`.
> Every claim below is cited to a file:line. Where a concept does not exist in the
> implementation it is explicitly marked **NOT IMPLEMENTED**. No ERP/accounting behavior
> was inferred; only code that actually ships is described.

---

## 1. Executive Summary

**What is Daily Closing in Shopixy?**
There is no single "DailyClosing" domain object. The behavior is split across two
distinct, non-overlapping implementation surfaces:

1. **Cashbox Open/Close (physical drawer reconciliation)** — `CashboxController.cs`.
   A cashbox is a physical cash register. An operator "opens" it (zero new cash per a hard
   constraint) and later "closes" it by entering the physical `ActualCash` count. The
   controller computes `ExpectedCash` from the ledger, derives `Difference =
   ActualCash - ExpectedCash`, records an *audit-only* discrepancy, and deactivates the
   cashbox. It does **not** post any journal for the difference and does **not** change
   the ledger balance.
   - Evidence: `CashboxController.cs:485-565` (CloseCashbox), `:516` (difference calc),
     `:519-526` (deactivate, no journal).

2. **Daily Close Z-Report (read-only reporting)** — `ReportsController.GetDailyCloseReport`
   (`ReportsController.cs:346-537`). A per-calendar-day read model computed live from
   posted ledger entries for all cashbox sub-accounts. Opening/closing cash are derived by
   scanning ledger `BalanceAfter`. No write path. The frontend renders this as a printable
   Z-Report (`DailyCloseReport.jsx`).

**Who owns it?**
- Cashbox lifecycle owns reconciliation (open/close/count) → `CashboxController` + `Cashbox` entity.
- Treasury owns the authoritative cash balances → `TreasuryOperationService` / ledger accounts.
- Financial reporting owns the day-aggregate read model → `ReportsController`.

**What does it own?**
The cashbox owns: a physical drawer's open/closed state, who opened it, opening float
metadata, and the *record* of the last closing difference (audit only).

**What does it NOT own?**
- It does NOT own the cash balance (ledger does).
- It does NOT write journals for the closing difference.
- It does NOT own treasury accounts, general ledger, or outbox events.
- It does NOT own approval, reopening, locking, or archiving.

**Source of truth:** The **General Ledger** (ledger accounts `1010-*` cashbox sub-accounts)
is the only source of truth for cash balances. `Cashbox.CurrentBalance` and
`Cashbox.ExpectedBalance` are `[Obsolete]` shadow fields explicitly forbidden for use.
- `Cashbox.cs:23-24`, `:50-51` (Obsolete markers); `CashboxController.cs:170-174` (sets to 0, comments "use ledger");
  `:61-62` DTO comment "DO NOT use entity CurrentBalance"; `:1400-1416` `GetCashboxLedgerBalanceAsync`.

---

## 2. Discovery

### Controllers
| Controller | File | Responsibility |
|---|---|---|
| `CashboxController` | `PointOfSale.API/Controllers/CashboxController.cs` | Cashbox CRUD + Open/Close/CashIn/CashOut/Expense/OpeningBalance + transactions/summary/daily-report |
| `ReportsController` | `PointOfSale.API/Controllers/ReportsController.cs:346-537` | `GET daily-close` Z-Report read model |

### Services
| Service | File | Responsibility |
|---|---|---|
| `ICashboxLedgerAccountService` / `CashboxLedgerAccountService` | `PointOfSale.Infrastructure/Treasury/CashboxLedgerAccountService.cs` | Provisions/resolve dedicated ledger account + `TreasuryAccount` per cashbox |
| `ITreasuryOperationService` / `TreasuryOperationService` | `PointOfSale.Infrastructure/Treasury/TreasuryOperationService.cs` | ALL money mutations → balanced journal entries via `IFinancialPostingService` |
| `ITreasuryReadService` / `TreasuryReadService` | `PointOfSale.Infrastructure/Treasury/TreasuryReadService.cs` | Ledger balance/movement reads (source of truth) |
| `IAuditService` / `AuditService` | `PointOfSale.API/Services/Audit/AuditService.cs` | Persists audit log items for every cashbox operation |

### DTOs / Request & Response
- `CashboxDTOs.cs`: `OpenCashboxRequest`, `CloseCashboxRequest` (has `ActualCash`),
  `CashTransactionRequest`, `ExpenseRequest`, `SetOpeningBalanceRequest`,
  `CashboxResponse`, `CashboxTransactionResponse`, `CashboxSummaryResponse`, `DailyReportResponse`.
- `DailyCloseReportDto.cs`: `DailyCloseReportDto`, `CashMovementDto` (read model only).

### Entities
- `Cashbox` (`PointOfSale.Core/Entities/Cashbox.cs`) — physical drawer state.
- `CashboxTransaction` (`PointOfSale.Core/Entities/CashboxTransaction.cs`) — runtime
  transaction record. **NOTE:** this entity is defined and configured
  (`CashboxTransactionConfiguration.cs`) but is **NOT written by the runtime cashbox path**.
  The controller derives "transactions" from the ledger (`GetCashboxLedgerTransactionsAsync`
  → `ProjectToTreasuryMovements`), not from `Cashbox.Transactions`. Confirmed: the only
  reference to `CashboxTransactions.Add` is a test asserting it must NOT happen
  (`TreasuryOperationJournalingTests.cs:15`).
- Ledger entities (authoritative): `Account`, `TreasuryAccount` (type `Cashbox`),
  `JournalEntry`, `LedgerEntry` (with `BalanceAfter`).

### Enums
- `CashboxTransactionType` (`PointOfSale.Core/Enums/CashboxTransactionType.cs`): Sale,
  SaleRefund, PurchasePayment, PurchaseRefund, ManualCashIn, ManualCashOut, OpeningBalance,
  ClosingBalance, CustomerPayment, SupplierPayment, PaymentReceived, Expense.
  Note: `ClosingBalance` is declared but is **NOT produced** by any runtime path (the close
  call does not emit a ClosingBalance movement — it only flips `IsActive`).

### Frontend
- `PointOfSale.Web/src/pages/DailyCloseReport.jsx` — Z-Report UI, printable
  (`useReactToPrint`), date param, signature lines.
- `PointOfSale.Web/src/components/OpenCloseCashboxModal.jsx` — open/close modal with
  denomination counting, computes `difference = actual - expectedBalance` client-side and
  shows over/short messaging.
- `PointOfSale.Web/src/pages/Cashbox.jsx`, `CashboxOpeningBalance.jsx`,
  `CashboxTransactionModal.jsx` — supporting UI.

**NOT IMPLEMENTED (searched, zero hits):**
- `DailyClosing`, `CloseDay`, `EndOfDay`, `CloseShift`, `ShiftClosing`, `OpenShift`,
  `TreasuryClosing`, `CloseTreasury`, `CashboxClosing`, `DailyClose` (as entity),
  `BusinessDay`/`BusinessDate` (as entities), `CashReconciliation`, `DrawerCount`,
  `DrawerClosing`, `POSClosing`, `SessionClosing`, `ClosingReport`, `ClosingAdjustment`
  entities/services. (Searches across `*.cs` returned 0 result files for these tokens.)
- No "Shift" aggregate exists anywhere.

---

## 3. Daily Closing Identity

| Concept | Implemented? | Evidence |
|---|---|---|
| **Business Day** | PARTIAL — date-only, not an entity | The Z-Report uses `reportDate = date?.Date ?? DateTime.UtcNow.Date` (`ReportsController.cs:355`) and a `[startOfDay, endOfDay]` UTC window. There is no `BusinessDay` entity, no business-date calendar, no "day lock". |
| **Shift** | NOT IMPLEMENTED | No `Shift` type, no open/close shift, no shift operator scoping. Cashbox is opened/closed by a user; there is no per-shift concept. |
| **Cashbox** | IMPLEMENTED | `Cashbox` entity; one physical drawer. |
| **Operator** | IMPLEMENTED (partial) | `OpenedByUserId` on open (`CashboxController.cs:401`); close uses `TryResolveCurrentUserId` (`CashboxController.cs:491`). No operator hand-off model. |
| **Opening Cash** | IMPLEMENTED | `Cashbox.OpeningBalance` + `OpeningBalanceAmount`/`OpeningBalanceDate` (`Cashbox.cs:44,58,63`). Open requires `OpeningBalance == 0` (`CashboxController.cs:347-354`) — new cash must come via treasury transfer, not opening float. |
| **Expected Cash** | IMPLEMENTED (derived) | `expectedBalance = GetCashboxLedgerBalanceAsync(...)` on close (`CashboxController.cs:515`); in Z-Report `ExpectedClosingCash` from ledger `BalanceAfter` (`ReportsController.cs:511-528`). |
| **Actual Cash** | IMPLEMENTED (input) | `CloseCashboxRequest.ActualCash` entered by operator (`CashboxDTOs.cs:27`; `OpenCloseCashboxModal.jsx:88-92`). |
| **Cash Difference** | IMPLEMENTED (audit-only) | `difference = request.ActualCash - expectedBalance` (`CashboxController.cs:516`); surfaced in audit `afterData` and response (`CashboxController.cs:543-547`). **No journal, no balance change.** |
| **Closing Time** | IMPLEMENTED (implicit) | `performedAtUtc = DateTime.UtcNow` at close (`CashboxController.cs:514`). |
| **Status** | IMPLEMENTED | `Cashbox.IsActive` boolean (open/closed) (`Cashbox.cs:29`). |
| **Approval** | NOT IMPLEMENTED | No approval entity, role-gated approval, or approval state machine in the closing path. (Close is `[Authorize]` only; open/cash-in/expense/opening-balance are `Owner,Admin`.) |
| **Audit** | IMPLEMENTED | `LogCashboxAuditAsync` for every operation; persisted to `AuditLogs` (`AuditService.cs:163-180`). |

---

## 4. Domain Ownership

| Element | Owner | Evidence |
|---|---|---|
| Business Day (window) | Reporting read model (`ReportsController`) | `ReportsController.cs:355-357` |
| Shift | **none (NOT IMPLEMENTED)** | no shift type |
| Cashbox | `CashboxController` + `Cashbox` entity | whole controller |
| Closing (open/close) | `CashboxController` | `:335-480` open, `:485-565` close |
| Cash Difference | `CashboxController` (compute) + Audit (record) | `:516`, `:543-547` |
| Closing Report (Z-Report) | `ReportsController` + `DailyCloseReportDto` | `:346-537` |
| Treasury | `TreasuryOperationService` / `TreasuryReadService` (ledger) | `TreasuryOperationService.cs:16` |
| Approval | **none** | NOT IMPLEMENTED |
| Audit | `IAuditService` (cross-cutting) | `AuditService.cs` |

---

## 5. Lifecycle (implemented stages only)

1. **Create cashbox** — `POST /api/cashbox` (`CashboxController.cs:139-216`). Creates
   `Cashbox` (inactive, balances 0) and provisions a dedicated ledger account + `TreasuryAccount`
   via `CashboxLedgerAccountService.EnsureCashboxAccountAsync`.
2. **Set Opening Balance / Open** — `POST {id}/open` (`:335`) or `POST {id}/opening-balance`
   (`:1210`). Constraint: opening float MUST be 0 (`OPENING_FLOAT_TRANSFER_REQUIRED`,
   `:347-354`); authoritative cash arrives via treasury transfer afterwards. Sets
   `IsActive=true`, `OpenedAt`, `OpenedByUserId`. If `OpeningBalance > 0` it posts a
   journal (CashIn to cashbox from `CashFloatReserve`) — only when amount > 0.
3. **Cash movements while open** — Cash sales/refunds/purchases flow in as ledger entries
   from the sales/purchase domains. Manual `cash-in`/`cash-out`/`expense` endpoints
   (`:677`, `:807`, `:942`) each post a balanced journal via `TreasuryOperationService`.
   Each requires `IsActive` (`:700`, `:829`, `:964`).
4. **Close (cash count)** — `POST {id}/close` (`:485`). Operator supplies `ActualCash`.
   Controller reads `expectedBalance` from ledger (`:515`), computes `difference`
   (`:516`), flips `IsActive=false` / clears `OpenedAt`/`OpenedByUserId` (`:519-521`),
   commits, and writes an audit record carrying `difference` + `discrepancyDetected`
   (`:528-548`). **No journal is posted for the difference; the ledger is untouched.**
5. **Daily Close report** — `GET /api/reports/daily-close?date=` (`ReportsController.cs:346`).
   Read-only Z-Report aggregation. Not part of the transactional lifecycle.
6. **Reopen** — **NOT IMPLEMENTED**. There is no reopen endpoint
   (`CashboxController.cs` exposes only `open`/`close`/`opening-balance`; no `reopen`).
   To reopen, the operator calls `open` again (which re-activates and re-sets `OpenedAt`).
7. **Lock** — **NOT IMPLEMENTED**. No treasury lock, no cashbox lock, no business-day lock.
8. **Archive** — **NOT IMPLEMENTED**. Soft-delete only (`DeleteCashbox`, `:278`), gated on
   `!IsActive` (`:296-299`).

---

## 6. Aggregate Analysis

- **Cashbox Aggregate** — `Cashbox` is the aggregate root for physical-drawer state
  (name, warehouse, active flag, opening metadata, who opened). It does NOT contain the
  cash balance (that lives in the ledger).
- **Treasury Aggregate** — owns the cash balance via `Account` + `TreasuryAccount`
  (`CashboxLedgerAccountService.cs:88-159` creates a dedicated `1010-<guid>` ledger account
  and a `TreasuryAccount` of type `Cashbox`). Money mutations are journaled here.
- **Business Day** — NOT an aggregate; it is a query window in the reporting read model.
- **Cash Difference** — NOT an aggregate; it is a derived scalar computed at close time and
  stored only in the audit log entry (`CashboxController.cs:543-547`).
- **Closing Report** — NOT an aggregate; it is a projection (`DailyCloseReportDto`)
  computed live from ledger entries.

**Conclusion:** Daily Closing is **NOT a standalone aggregate**. The "closing" operation is
a *command on the Cashbox aggregate* that, by design, delegates all money truth to the
Treasury/ledger aggregate and records the discrepancy only in the Audit read model.

---

## 7. Closing Behavior (implementation-backed)

- **Open Shift** → open cashbox: `IsActive=true`, `OpenedAt`, `OpenedByUserId`
  (`CashboxController.cs:399-402`). New opening cash forbidden; must transfer from treasury.
- **Close Shift** → close cashbox: `IsActive=false`, `OpenedAt=null`, `OpenedByUserId=null`
  (`:519-521`). Requires `IsActive` (`:508-511`).
- **Cash Count** → operator enters `ActualCash` (counted, optionally via denomination
  breakdown in UI `OpenCloseCashboxModal.jsx:145-192`). Server accepts it as-is; no server-side
  denomination validation.
- **Expected Cash** → ledger balance read at close (`:515`). For Z-Report, `ExpectedClosingCash`
  = last ledger `BalanceAfter` ≤ endOfDay across all `1010-*` accounts (`ReportsController.cs:511-528`).
- **Actual Cash** → `request.ActualCash` (`CashboxDTOs.cs:27`).
- **Difference** → `ActualCash - ExpectedCash` (`:516`). Positive = overage, negative = shortage.
  Frontend labels over (`cashOver`) / short (`cashShort`) (`OpenCloseCashboxModal.jsx:217-219`).
- **Shortage / Overage** → captured **only** as a sign on `difference`; surfaced in audit
  `afterData` (`CashboxController.cs:543-547`) and to the UI message. **No financial posting,
  no suspense account movement, no adjustment journal.**
- **Approval** → NOT IMPLEMENTED. Closing does not require a second actor; it completes within
  the calling user's `[Authorize]` scope. (Open/cash-in/expense require `Owner,Admin`; close does not.)
- **Reconciliation** → the "reconciliation" is the read-only comparison in the Z-Report and
  the close-time difference. There is no reconciliation workflow that mutates state.

---

## 8. Financial Behavior

- **Financial Truth:** General Ledger (`1010-*` cashbox sub-accounts). `Cashbox.CurrentBalance`
  / `ExpectedBalance` are obsolete shadow fields (`Cashbox.cs:23,50`).
- **Treasury Truth:** `TreasuryReadService.GetAccountBalanceAsync` for the cashbox account
  (`CashboxController.cs:1400-1416`). `TreasuryAccount` of type `Cashbox` is the treasury-side
  representation (`CashboxLedgerAccountService.cs:148-159`).
- **Cash Difference:** audit-only scalar (§3, §7). It does NOT change any balance.
- **Journal Posting:** Closing does NOT post a journal. Only *open* (when OpeningBalance > 0)
  and the manual cash-in/out/expense operations post journals, all via
  `TreasuryOperationService` → `IFinancialPostingService.PostJournalAsync`
  (`TreasuryOperationService.cs:110`). The close path has zero `PostJournal` calls
  (verified: `CashboxController.cs` close block contains no `_treasuryOperationService` /
  `PostJournal` invocation).
- **Adjustment:** NOT IMPLEMENTED in the closing path. A discrepancy is recorded in audit and
  returned; it is not auto-adjusted against the ledger. (If an operator must correct cash, they
  use a manual cash-in/out or a treasury transfer — separate commands.)
- **Cashbox Balance:** derived read of ledger `BalanceAfter` at read time
  (`GetCashboxLedgerBalanceAsync`, `:1400-1416`; `BuildCashboxResponse` uses ledger for
  `CurrentBalance`/`ExpectedBalance`, `:1631-1647`).
- **Read Model Strategy:** CQRS-style. Mutations go to ledger (write side). Reads
  (`GetTransactions`, `GetSummary`, `GetDailyReport`, `daily-close` report) project the ledger
  via `ProjectToTreasuryMovements` / `LedgerEntries` queries. The `CashboxTransaction` entity is
  NOT the read source.

---

## 9. Constraints (implementation-backed)

1. **One active cashbox open per operator action is NOT enforced as "one open shift."** Only
   `IsActive` guard exists: you cannot open an already-open cashbox (`:368-371`) and cannot
   transact on a closed one (`:700, :829, :964`). Multiple cashboxes may be open simultaneously
   (no global single-open constraint).
2. **Opening float must be zero** — `OpeningBalance != 0` rejected with
   `OPENING_FLOAT_TRANSFER_REQUIRED` (`:347-354`). Same for `SetOpeningBalance` (`:1222-1229`).
   Rationale: cashboxes must not create cash; authoritative treasury transfer only.
3. **Cash payment method only** for cash-in/cash-out/expense (`:710-713, :839-842, :974-977`).
4. **Sufficient funds** for cash-out/expense: `availableBalance < request.Amount` → rejected
   (`:844-848, :979-983`).
5. **Amount > 0** for cash-in/out/expense (`:705-708, :834-837, :969-972`).
6. **Close requires open** (`!IsActive` → BadRequest, `:508-511`).
7. **Delete requires closed** (`IsActive` → BadRequest, `:296-299`).
8. **No business-day lock / treasury lock / cashbox lock** — NOT IMPLEMENTED.
9. **No approval rules** — NOT IMPLEMENTED.
10. **Multi-currency:** cashbox has no currency field; treasury/ledger operations are
    tenant-base-currency scoped (`FinancialCurrencyGuard.PlatformBaseCurrencyCode`). The cashbox
    close does not handle currency. **Multi-currency per cashbox: NOT IMPLEMENTED.**

---

## 10. SQL Transactions

| Aspect | Finding | Evidence |
|---|---|---|
| Reads live? | Yes — read models query `LedgerEntries`/`TreasuryMovements` live (no snapshot table). | `ReportsController.cs:369-528`, `CashboxController.cs:1460-1467` |
| Writes ledger? | Indirectly — manual operations call `TreasuryOperationService` which posts journals; close itself writes only the `Cashbox` row. | `TreasuryOperationService.cs:110`; close `:525` |
| Writes journals? | Close: **NO**. Open (if amount>0) and cash-in/out/expense: **YES**. | `:379-397` (open journal), `:723` (cash-in), `:858` (cash-out), `:993` (expense); close `:516-526` no journal |
| Uses transactions? | Yes — `BeginTransactionAsync` around create/open/close/set-opening-balance. | `:161, :356, :496, :1231` |
| Isolation level (reads) | `IsolationLevel.RepeatableRead` for report reads. | `ReportsController.cs:2287`, `FinancialReportService.cs:657` |
| Isolation level (writes) | Not explicitly specified on `BeginTransactionAsync` in controller → DB default
  (ReadCommitted). `CashboxLedgerAccountService` uses `ReadCommitted` when it owns the txn. | `CashboxController.cs:161,356,496,1231`; `CashboxLedgerAccountService.cs:61` |
| Consistency | Ledger is authoritative; balances recomputed from `BalanceAfter`. Rollback on any
  treasury exception in open/cash-in/out/expense (`:449-479` etc.). | `:449-479` |

**Note:** The close transaction wraps only the `Cashbox` row update + audit; because no
ledger mutation occurs, there is no double-entry consistency risk at close time.

---

## 11. Events

- **Closing Events:** NOT IMPLEMENTED. `CloseCashbox` emits no domain event, no integration
  event, no outbox message. (Verified: `CashboxController.cs` close path contains zero
  references to `Publisher`/`Outbox`/`EventBus`/`IEvent`.)
- **Cash Difference Events:** NOT IMPLEMENTED. Difference is audit-log data only.
- **Approval Events:** NOT IMPLEMENTED (no approval).
- **Outbox Events:** The closing path does NOT enqueue to `AccountingOutboxMessage`,
  `StockSyncOutboxMessage`, or any outbox. (The outbox is used by sales/payments/stock, not
  cashbox close — confirmed by searching the controller for outbox usage: 0 hits.)
- **Audit persistence:** The only durable side-effect of closing beyond the row flip is an
  `AuditLogs` insert via `AuditService.EnqueueLog` (`AuditService.cs:163-180`), which runs in a
  *separate scope/transaction* from the close transaction (fire-and-forget queue with retry).

---

## 12. Runtime Architecture (implementation-backed)

```
Browser (OpenCloseCashboxModal.jsx)
   │  POST /cashbox/{id}/open   (openingBalance=0)
   ▼
CashboxController.OpenCashbox
   │  BeginTransaction
   │  cashbox.IsActive = true; OpenedAt; OpenedByUserId
   │  (if OpeningBalance>0) TreasuryOperationService.CashInAsync → journal
   │  SaveChanges + Commit
   ▼
AuditService.LogAction → AuditLogs (separate scope)

Browser (OpenCloseCashboxModal.jsx — close mode)
   │  POST /cashbox/{id}/close   { actualCash }
   ▼
CashboxController.CloseCashbox
   │  expectedBalance = TreasuryReadService.GetAccountBalanceAsync (LEDGER)
   │  difference = actualCash - expectedBalance
   │  cashbox.IsActive = false; OpenedAt=null; OpenedByUserId=null
   │  SaveChanges + Commit          ◄── NO journal, NO ledger mutation
   │  returns { expectedBalance, actualBalance, difference, referenceId }
   ▼
AuditService.LogAction(CashboxClose, afterData{difference, discrepancyDetected})
   ▼ (separate scope)
AuditLogs

[During the day — cash mutations]
SalesController / PurchasesController / CashboxController cash-in/out/expense
   ▼
TreasuryOperationService.(CashIn|CashOut|RecordExpense)Async
   ▼
IFinancialPostingService.PostJournalAsync  ──►  JournalEntry + LedgerEntries (BalanceAfter)
   ▼
General Ledger (authoritative cash truth)

[Read side — Z-Report]
Browser (DailyCloseReport.jsx)
   │  GET /reports/daily-close?date=
   ▼
ReportsController.GetDailyCloseReport
   │  BeginConsistentReadTransactionAsync (RepeatableRead)
   │  aggregate LedgerEntries where AccountCode LIKE '1010-%'
   │    → OpeningCashBalance, CashSales, CashReturns, CashExpenses,
   │      CashPurchases, ManualCashIn, ManualCashOut, ExpectedClosingCash,
   │      CardSales/Returns, TotalSales, TotalTransactions, Movements
   ▼
DailyCloseReportDto  ──►  printable Z-Report (useReactToPrint)
```

---

## 13. Browser Test Specification

**Entry points**
- Open/Close modal: `OpenCloseCashboxModal.jsx`.
- Z-Report: `DailyCloseReport.jsx` → `GET /reports/daily-close?date=YYYY-MM-DD`.

**Scenarios & Assertions**

1. **Open cashbox with non-zero opening float → rejected**
   - Action: open with `openingBalance = 100`.
   - Assert: HTTP 400, code `OPENING_FLOAT_TRANSFER_REQUIRED`
     (`CashboxController.cs:347-354`).
2. **Open sets state**
   - After open: `cashbox.IsActive == true`, `OpenedAt` set, `OpenedByUserId` == current user.
3. **Close computes expected from ledger**
   - Seed ledger with known cashbox balance B. Close with `actualCash = B`.
   - Assert: response `expectedBalance == B`, `difference == 0`, `discrepancyDetected == false`.
4. **Close with shortage**
   - `actualCash = B - 5`. Assert: `difference == -5` (negative), UI shows `cashShort`.
5. **Close with overage**
   - `actualCash = B + 5`. Assert: `difference == +5`, UI shows `cashOver`.
6. **Close audit record**
   - Assert an `AuditLogs` row with `ActionType == "CashboxClose"`, `afterData.difference`
     matches, `discrepancyDetected` boolean correct.
7. **Close does NOT move ledger**
   - Capture ledger `BalanceAfter` before and after close for the cashbox account.
   - Assert: unchanged (no journal posted for the difference).
8. **Cannot close an already-closed cashbox**
   - Assert HTTP 400 `Cashbox is not open` (`:508-511`).
9. **Z-Report aggregates correctly**
   - Assert `OpeningCashBalance`, `ExpectedClosingCash`, `CashSales`, `CardSales`,
     `TotalTransactions`, `Movements` match ledger projections for the date.
10. **Print** — `DailyCloseReport.jsx` `useReactToPrint` renders the Z-Report section.

**Negative Cases**
- Close with `actualCash < 0` → expected rejected (UI validates `parsedAmount < 0`,
  `OpenCloseCashboxModal.jsx:73-76`); server should also reject negative.
- Cash-in/cash-out with non-Cash payment method → 400 (`:710-713`).
- Cash-out exceeding balance → 400 `Insufficient cash in cashbox` (`:844-848`).
- Delete an active cashbox → 400 `Cannot delete an active cashbox` (`:296-299`).

**Cash Count**
- Denomination breakdown UI computes total; assert it equals sum of
  `hundreds*100 + fifties*50 + ... + coins` (`OpenCloseCashboxModal.jsx:28-62`). Server accepts
  the final numeric `actualCash` without re-validating denominations.

**Difference / Approval / Reopen**
- Difference is shown but **cannot be approved/rejected in-app** (NOT IMPLEMENTED).
- **Reopen is NOT IMPLEMENTED** as a distinct endpoint; re-opening is done by calling `open`
  again. Assert no `reopen` route exists.

---

## 14. Domain Ownership Matrix

| Domain Element | Owning Component | Write Path | Read Path | Notes |
|---|---|---|---|---|
| Cashbox state (open/closed) | `CashboxController` + `Cashbox` | `CashboxController` | `CashboxController` | `IsActive` flag |
| Cash balance | Treasury / Ledger (`1010-*`) | `TreasuryOperationService` | `TreasuryReadService` | authoritative |
| Cashbox ledger account | `CashboxLedgerAccountService` | on create/open | resolve | dedicated account + TreasuryAccount |
| Expected/Actual cash | `CashboxController` (close) / `ReportsController` (report) | — | ledger query | derived |
| Cash difference | `CashboxController` | audit only | audit log | no journal |
| Daily Close Z-Report | `ReportsController` | — | ledger query | CQRS read model |
| Audit trail | `AuditService` | on every op | `AuditLogs` | cross-cutting |
| Approval | — | NOT IMPLEMENTED | — | — |
| Shift / Business Day entity | — | NOT IMPLEMENTED | report window only | — |
| Treasury lock / Day lock | — | NOT IMPLEMENTED | — | — |

---

## 15. Canonical Pattern

Daily Closing in Shopixy is a **thin command layer over the Treasury/ledger aggregate**:

```
Cashbox (drawer state)  ──open/close──▶  IsActive flag flip
        │
        │ difference = ActualCash − LedgerBalance   (audit only)
        ▼
   AuditLogs  (discrepancy record, no money movement)

Treasury / Ledger  ── authoritative cash truth, mutated only by
   TreasuryOperationService journal postings (sales, cash-in/out, expense, transfer)
        │
        ▼
   Daily Close Z-Report  =  live CQRS projection of ledger (read-only)
```

There is **no DailyClosing aggregate, no journal for differences, no approval, no reopen,
no lock**. Reconciliation is a *reporting* concern, not a *mutating* one.

---

## 16. Documentation Validation (misconceptions corrected)

- ❌ "Daily Closing owns the treasury / cash balance."
  ✅ Treasury (ledger `1010-*` accounts) owns balances. Cashbox only holds open/closed state.
  (`Cashbox.cs:23,50`; `CashboxController.cs:170-174`.)

- ❌ "Daily Closing writes a journal for the cash difference / shortage / overage."
  ✅ The close posts **no journal**. Difference is audit-only
  (`CashboxController.cs:516-526`). Only open (if >0) and manual cash ops post journals.

- ❌ "Daily Closing owns the general ledger / journals."
  ✅ `TreasuryOperationService` → `IFinancialPostingService` owns journals; cashbox close
  never calls it.

- ❌ "Cash Difference changes balances directly."
  ✅ Adjustment is NOT IMPLEMENTED in the close path; the ledger is untouched at close.

- ❌ "There is a Shift / Business Day entity that Daily Closing manages."
  ✅ NOT IMPLEMENTED. No shift, no business-day entity. The day is a query window only
  (`ReportsController.cs:355-357`).

- ❌ "Daily Closing supports approval / reopening / locking."
  ✅ None implemented. Close is final (apart from calling open again). No approval state,
  no treasury/day/cashbox lock.

- ❌ "Closing emits an outbox/integration event."
  ✅ NOT IMPLEMENTED. No outbox/enqueue in the close path.

- ❌ "`Cashbox.CurrentBalance` / `ExpectedBalance` are the source of truth."
  ✅ They are `[Obsolete]` shadow fields; ledger is authoritative
  (`Cashbox.cs:23,50`; `CashboxController.cs:61-62`).

---

## 17. Final Architecture Conclusion

- **Closing Authority:** `CashboxController` is the sole authority for cashbox
  open/close/reconciliation. It holds no money authority.
- **Source Of Truth:** General Ledger (cashbox sub-accounts `1010-*`). Cashbox entity fields
  `CurrentBalance`/`ExpectedBalance` are obsolete.
- **Runtime:** Browser modal → `CashboxController` (open/close) → ledger read for expected,
  flag flip for state, audit write. Day report is a separate CQRS read from the ledger.
- **Aggregate Ownership:** Cashbox aggregate (state only) + Treasury/ledger aggregate (money).
  Daily Closing is NOT a standalone aggregate.
- **Audit:** Every cashbox operation persists an `AuditLogs` row (separate scope) capturing
  `difference` and `discrepancyDetected` at close.
- **Financial Integration:** Cash mutations integrate via `TreasuryOperationService` journal
  postings. The *closing difference itself* integrates with **nothing** — it is not posted,
  not adjusted, not evented.
- **Integrity Strategy:** Ledger-authoritative balances; `RepeatableRead` for report reads;
  transactional open/cash-op/close with rollback on treasury exceptions; obsolete shadow
  balance fields explicitly forbidden; tenant-scoped (`RequiredTenantId`) throughout.

**Open questions resolved from implementation:**
- Is Daily Closing an Aggregate? → **No** (command on Cashbox aggregate).
- Does Business Day exist? → **As entity: No**; as report window: Yes.
- Does Shift exist? → **No**.
- Does Cash Count exist? → **Yes** (operator-entered `ActualCash`, UI denomination helper).
- Does Expected/Actual Cash exist? → **Yes** (derived/input).
- Does Cash Difference exist? → **Yes** (audit-only scalar).
- Does Daily Closing write journals? → **No** (close). Open/cash-ops do.
- Does Daily Closing use Accounting Outbox? → **No**.
- Does it support approval? → **No**.
- Does it support reopening? → **No distinct endpoint** (re-open via `open`).
- Does it lock treasury/cashbox? → **No**.
- Does it support multiple shifts? → **N/A** (no shift concept; multiple cashboxes may be open).
- Does it support exports? → **No export endpoint**; Z-Report supports print (`useReactToPrint`).
- Does it support printing? → **Yes** (Z-Report print).
- Does it support pagination? → Cashbox list & transactions: **Yes** (`PagedResult`,
  `CashboxController.cs:64-111, :570-672`). Daily-close Z-Report: **No** (single-day read).
- Does it support multi-currency? → **No** per-cashbox; base-currency scoped.
- Source of truth? → **General Ledger**.
