# FLOW-ACC-11 — Account Adjustments (POS Admin) Architecture Discovery

Status: IMPLEMENTATION-BACKED (reverse engineered from source)
Authority scope: Account Adjustments / Financial / Ledger / Manual / Balance / Correction Entries
Excluded by mandate: Statement Of Account (SOA read), Inventory/Stock Adjustments, CommerceHub Settlement, Daily Closing

Evidence convention used below:
- `IMPL`  = implemented and verified from source
- `PART`  = partially implemented
- `NONE`  = not implemented (no source evidence)

Every claim cites file:line.

---

## 1. Executive Summary

**What is Account Adjustment?**
A Party Ledger Adjustment is a manual, tenant-scoped financial correction against a Customer or Supplier party balance. It is implemented as the entity `PartyLedgerAdjustment` (PointOfSale.Core/Entities/PartyLedgerAdjustment.cs:10) plus a journal-posting pipeline that writes a new double-entry journal through the shared `FinancialPostingService`.

**Who owns it?**
- Entity + HTTP surface: `StatementOfAccountController` (PointOfSale.API/Controllers/StatementOfAccountController.cs), live under route `api/StatementOfAccount`.
- Posting engine: `CommerceAccountingService.PostPartyLedgerAdjustmentAsync` (PointOfSale.Infrastructure/Accounting/CommerceAccountingService.cs:505).
- Ledger mutation: `ILedgerService` (PointOfSale.API/Services/LedgerService.cs:13).

**What does it own?**
- A durable business record (`PartyLedgerAdjustment`) describing the intent of a balance mutation.
- A reference number, notes, idempotency key, and a single signed amount.

**What does it NOT own?**
- It does NOT own the general ledger journal lines — those are created by `FinancialPostingService` (PointOfSale.Infrastructure/Accounting/FinancialPostingService.cs:39).
- It does NOT own party balances — the running balance lives in `AccountLedger` / `Customer.Balance` / `Supplier.Balance`, maintained by `LedgerService` (PointOfSale.API/Services/LedgerService.cs).
- It does NOT modify any pre-existing journal or ledger entry. It creates a NEW compensating journal (see §7, §16).
- No approval, reversal, write-off, reclassification, export, or print workflow exists for it (see §5, §7, §9).

**What is the source of truth?**
- PostgreSQL `PartyLedgerAdjustments` is the durable intent record (migration PointOfSale.Infrastructure/Migrations/20260408193000_AddSupplierPaymentsAndPartyLedgerAdjustments.cs:16).
- The GL truth is `LedgerJournalEntries` + `LedgerLines` produced by `FinancialPostingService`; party/balance truth is `AccountLedger` + `Customer`/`Supplier` balance columns. The adjustment is a *source* of a journal, not the journal itself.

---

## 2. Discovery (implemented components only)

### Entities
- `PartyLedgerAdjustment` — PointOfSale.Core/Entities/PartyLedgerAdjustment.cs:10
  - `Guid TenantId`, `PartyType PartyType`, `Guid PartyId`, `TransactionType TransactionType`,
    `DateTime AdjustmentDate`, `decimal Amount`, `string IdempotencyKey`, `string ReferenceNumber`,
    `string? Notes`. Inherits `BaseEntity` (Id, CreatedAt, UpdatedAt, IsDeleted) and `ITenantOwned`.
  - Documented purpose (lines 6-9): "Durable record for manual customer/supplier statement adjustments. Stores the business intent behind a ledger mutation for audit and replay."

### Enums
- `TransactionType` — PointOfSale.Core/Enums/TransactionType.cs:3. Adjustment-valid values used:
  - `CreditNote`, `DebitNote`, `CreditAdjustment`, `DebitAdjustment` (lines 12-15).
- `PartyType` — Customer / Supplier (used as the adjustment target scope).

### Controllers
- `StatementOfAccountController` — PointOfSale.API/Controllers/StatementOfAccountController.cs:25
  - `[ApiController] [Route("api/[controller]")] [Authorize]` — all endpoints require auth + tenant context.
  - Endpoints discovered (verbatim attributes):
    - `POST query` → GetStatementOfAccount (line 63) — SOA read (excluded domain, listed for context)
    - `GET balance/{partyType}/{partyId}` → GetPartyBalance (line 222)
    - `POST opening-balance` → CreateOpeningBalance (line 263) — Opening Balance, NOT an adjustment
    - `POST adjustment` → CreateAdjustment (line 534) — **the only adjustment write endpoint**
    - `GET outstanding/{partyType}` → GetOutstandingBalances (line 925)
  - **There is NO** `GET adjustment`, `GET adjustment/{id}`, `PUT adjustment`, `DELETE adjustment`,
    `POST adjustment/reverse`, `POST adjustment/approve`, `POST adjustment/cancel` endpoint. Verified by enumerating every `[Http...]` attribute in the controller.

### Services
- `ILedgerService` / `LedgerService` — PointOfSale.API/Services/LedgerService.cs:13
  - `CreateLedgerEntry(...)` (line 32) — used for Customer path.
  - `ApplySupplierTransaction(...)` (line 57) — used for Supplier path; returns `balanceAfter`.
  - `ApplyCustomerTransaction(...)` (line 81) — returns `customer.Balance` (line 701 reads `customer!.Balance`).
- `ICommerceAccountingService` / `CommerceAccountingService` — PointOfSale.Infrastructure/Accounting/CommerceAccountingService.cs
  - `PostPartyLedgerAdjustmentAsync(PartyLedgerAdjustment, ct)` (line 505).
  - `IsDebitAdjustment(TransactionType)` private (line 1541).
- `IFinancialPostingService` / `FinancialPostingService` — PointOfSale.Infrastructure/Accounting/FinancialPostingService.cs:25
  - `PostJournalAsync(PostingRequest, ct)` (line 39) — writes the journal + lines.
- `IAccountingOutboxService` / `AccountingOutboxService` — PointOfSale.Infrastructure/Accounting/AccountingOutboxService.cs
  - `EnqueuePartyLedgerAdjustmentAsync(tenantId, adjustmentId, ct)` (line 181).
  - `EnqueueMessageAsync(...)` private (line 683) writes `AccountingOutboxMessages` row atomically.
- `AccountingOutboxProcessor` — PointOfSale.Infrastructure/Accounting/AccountingOutboxProcessor.cs
  - message type `"PartyLedgerAdjustment"` registered (line 58), dispatched (line 724).
  - `ProcessPartyLedgerAdjustmentAsync(...)` (line 1758).
  - `LoadPartyLedgerAdjustmentForAccountingPostAsync(...)` (line 1226) — `FOR UPDATE` (PgSQL) / `UPDLOCK, ROWLOCK, HOLDLOCK` (SQL Server).
- `IFinancialMutationIdempotencyService` — used via `TryClaimAsync` / `CompleteAsync` in controller.
- `IAuditService` — `_auditService.LogCreate("PartyLedgerAdjustment", ...)` (controller line 743).

### DTOs (PointOfSale.API/DTOs/StatementOfAccountDTOs.cs)
- `CreateAdjustmentRequest` (line 60): `PartyType`, `PartyId`, `TransactionType AdjustmentType`,
  `decimal Amount`, `string? ReferenceNumber`, `string? Notes`.
- `PartyLedgerAdjustmentResponse` (line 70): `Id`, `PartyType`, `PartyId`, `PartyName`,
  `TransactionType AdjustmentType`, `DateTime AdjustmentDate`, `decimal Amount`,
  `string ReferenceNumber`, `string? Notes`, `decimal BalanceAfter`, `string IdempotencyKey`.

### DB / Migrations
- Table `PartyLedgerAdjustments` — migration 20260408193000 (up: lines 16-36).
  - Columns: Id (uuid PK), TenantId, PartyType (int), PartyId (uuid), TransactionType (int),
    AdjustmentDate (timestamptz), Amount (numeric(18,2)), IdempotencyKey (varchar(100)),
    ReferenceNumber (varchar(100)), Notes (varchar(1000) null), CreatedAt, UpdatedAt, IsDeleted (bool).
  - Unique index `IX_PartyLedgerAdjustments_TenantId_IdempotencyKey` (lines 73-77).
  - Composite index `IX_PartyLedgerAdjustments_TenantId_PartyType_PartyId_AdjustmentDate` (lines 79-82).
- `ApplicationDbContext` registration — PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:
  - `DbSet<PartyLedgerAdjustment> PartyLedgerAdjustments` (line 123).
  - Global query filter `!e.IsDeleted && e.TenantId == CurrentTenantId` (line 678).
  - Fluent config: table name, key, unique (TenantId, IdempotencyKey), composite index, `Amount` precision 18,2, IdempotencyKey max 100 (lines 2154-2163).

### Account codes (PointOfSale.Core/Interfaces/Accounting/IAccountResolver.cs:44 `AccountCodes`)
- `AccountsReceivable = "1410"`, `AccountsReceivableAdjustments = "6120"`.
- `SupplierPayables = "2010"`, `AccountsPayableAdjustments = "6130"`.

### Frontend (PointOfSale.Web)
- `src/pages/Adjustments.jsx` — Account Adjustments page (title `accounts.accountAdjustments`, line 133).
  - Allows: select Customer/Supplier, pick party, view current balance, open modal, submit a NEW adjustment.
  - Exposes only the four adjustment types and an amount + notes form (lines 92-122).
- `src/components/AdjustmentModal.jsx` — create-only modal (line 4). Fields: `adjustmentType`
  (CreditNote/DebitNote/CreditAdjustment/DebitAdjustment), `amount`, `notes` (lines 27-31).
- `src/services/soaApi.js` — `createAdjustment` → `POST /StatementOfAccount/adjustment` (line 30);
  no reverse/approve/list/delete call exists in the client.
- **Browser supports CREATE only.** No list grid, no history view, no approve/reverse UI exists.

---

## 3. Account Adjustment Identity

Reverse engineered from `PartyLedgerAdjustment` + `CreateAdjustmentRequest` + `PartyLedgerAdjustmentResponse`.

| Identity attribute | Source | Notes |
|---|---|---|
| Adjustment Id | BaseEntity.Id (Guid) | Surrogate PK |
| Tenant | `TenantId` (PartyLedgerAdjustment.cs:15) | Required; global query filter enforces isolation |
| Adjustment Type | `TransactionType` (validated to CreditNote/DebitNote/CreditAdjustment/DebitAdjustment) | Controller lines 568-571 |
| Source Account (party) | `PartyType` + `PartyId` (entity lines 17-18) | Customer or Supplier only |
| Target Account (GL) | Derived at posting: control vs adjustment account | CommerceAccountingService.cs:509-520 |
| Amount | `Amount` (entity line 21), rounded via `RoundCurrency` | Controller line 648; must be > 0 (line 580) |
| Reference | `ReferenceNumber` (entity line 29) | Auto prefix CN/DN/CA/DA + timestamp, or trimmed caller value (controller 633-635) |
| Status | NONE | No status field/enum on the entity or DTO |
| Created By | NONE explicit | Audit `LogCreate` records actor indirectly (controller 743); no `CreatedBy` column |
| Approved By | NONE | No approval concept |
| Audit | `IAuditService.LogCreate` on create (controller 743) | Audit record, not on the entity |
| Idempotency | `IdempotencyKey` (entity line 27) + HTTP `Idempotency-Key` header | Header required (controller 539-546) |
| Notes | `Notes` (entity line 30, max 1000) | Required on frontend (AdjustmentModal notes `required`) |

**Adjustment Reason:** `Notes` is the only free-text reason field (entity line 30). There is NO `AdjustmentReason` enum/entity — NOT IMPLEMENTED.

---

## 4. Domain Ownership

| Concern | Owner (implementation) | Evidence |
|---|---|---|
| Adjustment record | `PartyLedgerAdjustment` entity + `StatementOfAccountController.CreateAdjustment` | PartyLedgerAdjustment.cs:10; controller 534 |
| Journal (GL) | `FinancialPostingService.PostJournalAsync` | FinancialPostingService.cs:39 |
| Ledger lines | `FinancialPostingService` (LedgerJournalEntry + LedgerLines) | FinancialPostingService.cs:205-221 |
| Party balance | `LedgerService` (AccountLedger running balance) + Customer/Supplier.Balance | LedgerService.cs:32,57,81; controller 661,701 |
| Approval | NONE | No endpoint/service/method |
| Audit | `IAuditService.LogCreate` (create only) | controller 743 |
| History | NONE (no adjustment history endpoint; only soft-delete + audit log) | No `GetAdjustment(s)` endpoint |
| Correction | Implemented AS the adjustment itself (new journal) | CommerceAccountingService.cs:505 |

---

## 5. Lifecycle (implemented stages only)

Enumerated from `CreateAdjustment` (controller 534-761) and the outbox processor (1758-1793).

1. **Create (IMPL)** — `POST /api/StatementOfAccount/adjustment`.
   - Idempotency-Key header required + validated `^[A-Za-z0-9-]+$`, max 100 (controller 27-31, 539-546).
   - `BuildAdjustmentRequestHash` + `TryClaimAsync(operation:"SOA_ADJUSTMENT_CREATE")` inside a DB transaction (controller 548-555, 550).
   - Validations: type ∈ {CreditNote, DebitNote, CreditAdjustment, DebitAdjustment} (568-571); `Amount > 0` (580-587); party exists & not deleted (589-615).
   - Builds `PartyLedgerAdjustment` (640-654), `Amount = RoundCurrency(request.Amount)` (648).
   - Applies ledger: Supplier → `ApplySupplierTransaction` + add `SupplierTransaction`; Customer → `CreateLedgerEntry` + read `customer.Balance` + add `CustomerTransaction` (659-719).
   - `EnqueuePartyLedgerAdjustmentAsync` (721-724) → outbox row.
   - `SaveChangesAsync` (726) commits adjustment + ledger + transaction + outbox atomically.
   - `IAuditService.LogCreate("PartyLedgerAdjustment", ...)` (743).
   - Returns `PartyLedgerAdjustmentResponse` with `BalanceAfter` + `IdempotencyKey` (728-754).
   - On any exception: `transaction.RollbackAsync` (758).

2. **Validate (IMPL)** — type/amount/party validation inline (controller 567-615). Idempotency claim (551-555). Journal-level double-entry + balance validation occurs later in `FinancialPostingService` (see §9).

3. **Approve (NONE)** — No approval stage, status field, approver, or endpoint exists.

4. **Post (IMPL, asynchronous)** — The adjustment does NOT post synchronously. It enqueues an outbox message; a background worker posts the journal.
   - `ProcessPartyLedgerAdjustmentAsync` loads the adjustment `FOR UPDATE` (1226-1270), calls `CommerceAccountingService.PostPartyLedgerAdjustmentAsync` (1777), commits inside an execution-strategy transaction (1767-1792).
   - `PostPartyLedgerAdjustmentAsync` builds a `PostingRequest` (SourceType `"PartyLedgerAdjustment"`, IdempotencyKey `PARTY_ADJUSTMENT:{TenantId}:{AdjustmentId}`) and calls `PostJournalAsync` (CommerceAccountingService.cs:539-551).

5. **Reverse (NONE for adjustments)** — No reverse endpoint, no reverse method for `PartyLedgerAdjustment`. The generic `ReverseJournalAsync` exists in `FinancialPostingService` (line 275) but is NOT wired to adjustments.

6. **Cancel (NONE)** — No cancel endpoint/method.

7. **Delete (NONE)** — No delete endpoint. Entity supports soft-delete (`IsDeleted`) via global filter, but no code path sets it for adjustments.

8. **Archive (NONE)** — No archive concept.

---

## 6. Aggregate Analysis

- **Adjustment aggregate root:** `PartyLedgerAdjustment` is an independent aggregate (own table, own Id, own tenant-scoped idempotency). It is NOT part of the Customer/Supplier aggregate — it references `PartyId` by value only.
- **Journal aggregate:** Owned by `FinancialPostingService` (LedgerJournalEntry + LedgerLines). The adjustment references it via `SourceType="PartyLedgerAdjustment"`, `SourceId=adjustment.Id` — a one-way lineage, not containment.
- **Ledger / Balance aggregate:** Owned by `LedgerService` and the `AccountLedger` / `Customer` / `Supplier` balance columns. The adjustment is a *source event* that triggers a balance delta; it does not own the resulting balance.
- **History / Audit aggregate:** Audit is a side-effect (`LogCreate`), not an owned child of the adjustment.
- **Conclusion:** Adjustment is an Entity and an Aggregate Root for its own intent record. It is a *source* for Journal and Ledger aggregates but owns neither. This matches the canonical rule: "Adjustments create new financial postings" (§16).

---

## 7. Adjustment Behavior (implementation-backed)

### Manual Adjustment (IMPL)
The `POST adjustment` endpoint is the manual adjustment. Frontend `Adjustments.jsx` + `AdjustmentModal.jsx` provide a human-driven create form. No automated/scheduled adjustment exists.

### Balance Correction (IMPL, via new journal)
A correction is recorded by creating a `PartyLedgerAdjustment` whose `TransactionType` is a note/adjustment, which posts a balancing journal against the control account and the dedicated *Adjustments* GL account (6120 / 6130). It corrects the party balance by adding a new ledger entry — it never edits the prior ledger entry.

### Write-off (NONE)
No `WriteOff`, `WriteOffAsync`, or write-off transaction type exists. `TransactionType` enum has no WriteOff member (TransactionType.cs:3-18). NOT IMPLEMENTED.

### Correction Entry (IMPL = the adjustment journal itself)
The "correction" is the journal produced by `PostPartyLedgerAdjustmentAsync`. Lines:
- Debit adjustment (DebitNote/DebitAdjustment): `DR controlAccount`, `CR <Adjustments>Account` (CommerceAccountingService.cs:531-536).
- Credit adjustment (CreditNote/CreditAdjustment): `DR <Adjustments>Account`, `CR controlAccount` (533-536).
  where controlAccount = AccountsReceivable (1410) for Customer, SupplierPayables (2010) for Supplier; adjustments account = 6120 / 6130 respectively (509-520).

### Reversal (NONE for adjustments)
No adjustment reversal path. Generic `ReverseJournalAsync` (FinancialPostingService.cs:275) is not exposed for `PartyLedgerAdjustment`. To undo an adjustment, the implementation provides NO supported path — a correcting adjustment would have to be entered manually (no code guarantees this).

### Reclassification (NONE)
No reclassification endpoint/method/enum. NOT IMPLEMENTED.

### Journal Posting (IMPL)
Posted asynchronously via outbox → `PostPartyLedgerAdjustmentAsync` → `PostJournalAsync`. The journal is double-entry, balanced, tenant-scoped, and immutable after posting (per FinancialPostingService guarantees, lines 19-23).

### Approval (NONE)
No approval gate. The adjustment is posted to the GL on creation (after outbox worker runs) with no human approval step.

---

## 8. Financial Behavior

- **Financial Truth:** PostgreSQL `LedgerJournalEntries` + `LedgerLines` (produced by `FinancialPostingService`). Party/balance truth: `AccountLedger` and `Customer.Balance`/`Supplier.Balance`.
- **Ledger Truth:** `LedgerService` maintains running balance in `AccountLedger` and updates `Customer.Balance`/`Supplier.Balance` (LedgerService.cs; controller 661/701). The adjustment itself stores only `Amount` and does not hold a balance.
- **Adjustment Posting:** Two-phase. Phase 1 (sync, in HTTP request): write `PartyLedgerAdjustment` + `AccountLedger` delta + `SupplierTransaction`/`CustomerTransaction` + outbox row, all in one DB transaction (controller 550, 656, 661/690, 721, 726). Phase 2 (async, worker): outbox processor posts the GL journal (AccountingOutboxProcessor.cs:1758).
- **Journal Creation:** `PostPartyLedgerAdjustmentAsync` → `PostJournalAsync` with `SourceType="PartyLedgerAdjustment"` (CommerceAccountingService.cs:543).
- **Balance Updates:** Driven by `LedgerService` at create time (synchronous), independent of the GL journal post. The GL journal posts the same economic event into the chart-of-accounts adjustment accounts.
- **Read Model Strategy:** SOA reads query `AccountLedgers` live (`AsNoTracking`) with opening-balance + running-balance computation (controller 72-155). Balances are recomputed from ledger rows, not cached on the adjustment. The adjustment has no dedicated read model/list endpoint.

---

## 9. Constraints (implementation-backed rules)

| # | Rule | Evidence |
|---|---|---|
| 1 | Idempotency-Key HTTP header required, pattern `^[A-Za-z0-9-]+$`, max 100 | controller 27-31, 539-546 |
| 2 | Per-(Tenant,IdempotencyKey) unique claim (`SOA_ADJUSTMENT_CREATE`) | controller 549-555; migration index lines 73-77 |
| 3 | AdjustmentType must be CreditNote/DebitNote/CreditAdjustment/DebitAdjustment | controller 567-571 |
| 4 | Amount must be > 0 | controller 580-587; frontend `amount <= 0` guard (Adjustments.jsx:102) |
| 5 | Party must exist and not be deleted | controller 589-615 |
| 6 | Amount rounded via `RoundCurrency` to 2 decimals; column precision 18,2 | controller 648; migration line 25; ApplicationDbContext.cs:2162 |
| 7 | Tenant isolation enforced by global query filter + RequiredTenantId | ApplicationDbContext.cs:678; entity ITenantOwned |
| 8 | ReferenceNumber max 100, Notes max 1000 | migration lines 27-28 |
| 9 | GL posting requires balanced double-entry + active same-tenant accounts | FinancialPostingService.cs:19-23, 160 |
| 10 | Outbox enqueue is atomic with business mutation (same transaction) | AccountingOutboxService.cs:749-752 |
| 11 | Outbox replay locks the adjustment row `FOR UPDATE` / `UPDLOCK` | AccountingOutboxProcessor.cs:1226-1270 |
| 12 | Notes required on frontend | AdjustmentModal.jsx notes `required` (line 110) |

**Locked Period:** NOT IMPLEMENTED — no accounting period lock check exists in the adjustment path.
**Duplicate Protection:** Provided only via the (Tenant, IdempotencyKey) unique index + idempotency-claim service. No separate "duplicate adjustment" business rule.
**Negative Amount:** Rejected (rule 4). The generic "prevent negative balance" check in `FinancialPostingService.ValidateNoNegativeBalances` also applies at journal post (line 160) for flagged accounts.

---

## 10. SQL Transactions

- **Reads live?** Yes. SOA `query` reads `AccountLedgers` live (controller 72). The outbox processor reloads the adjustment row live under lock (AccountingOutboxProcessor.cs:1226).
- **Writes journals?** Yes — asynchronously via `PostPartyLedgerAdjustmentAsync` → `PostJournalAsync` (CommerceAccountingService.cs:551; FinancialPostingService.cs:39).
- **Writes ledger?** Yes — synchronously via `LedgerService` (AccountLedger + Customer/Supplier balance) at create time (controller 661/690).
- **Uses transactions?** Yes.
  - Create: `await _context.Database.BeginTransactionAsync` wraps claim + writes + outbox (controller 550).
  - Post: outbox processor wraps load + `PostPartyLedgerAdjustmentAsync` in an execution-strategy transaction with `BeginTransactionAsync` (AccountingOutboxProcessor.cs:1767-1792).
- **Isolation level:**
  - Adjustment load for post uses `FOR UPDATE` (PgSQL) / `UPDLOCK, ROWLOCK, HOLDLOCK` (SQL Server) — pessimistic row lock (AccountingOutboxProcessor.cs:1238-1254).
  - `RebuildPartyBalanceAsync` uses `IsolationLevel.ReadCommitted` (LedgerService.cs:117) — for context only.
- **Consistency guarantees:** Outbox pattern guarantees the GL journal is eventually posted exactly once (idempotency key `PARTY_ADJUSTMENT:{TenantId}:{AdjustmentId}`, CommerceAccountingService.cs:547; outbox idempotency at enqueue, AccountingOutboxService.cs:709-728). Business mutation + outbox row are atomic (AccountingOutboxService.cs:749-752).

---

## 11. Events

| Event | Implementation | Evidence |
|---|---|---|
| Adjustment Created | Outbox message `PartyLedgerAdjustment` enqueued + `IAuditService.LogCreate` | controller 721-724, 743; outbox type registered AccountingOutboxProcessor.cs:58 |
| Adjustment Posted | GL journal posted by worker; journal `SourceType="PartyLedgerAdjustment"` | CommerceAccountingService.cs:543, 551 |
| Adjustment Reversed | NONE | No reverse path |
| Approval | NONE | No approval |
| Audit | `LogCreate("PartyLedgerAdjustment", id, "...", response, docNumber)` | controller 743-748 |
| Accounting Outbox | `AccountingOutboxMessages` row; processed by `AccountingOutboxProcessor.ProcessPartyLedgerAdjustmentAsync` | AccountingOutboxService.cs:181, 734-752; processor 1758 |

Payload of the outbox message (AccountingOutboxProcessor.cs:1765): `{ "AdjustmentId": <guid> }`. The worker reloads the full adjustment and posts from it (does not trust the payload alone).

---

## 12. Runtime Architecture (implementation-backed)

```
Browser (Adjustments.jsx + AdjustmentModal.jsx)
  │  POST /api/StatementOfAccount/adjustment  (Idempotency-Key header required)
  ▼
StatementOfAccountController.CreateAdjustment  (StatementOfAccountController.cs:534)
  │  ├─ ResolveFinancialIdempotencyKey + TryClaimAsync("SOA_ADJUSTMENT_CREATE")
  │  ├─ Validate type ∈ {CreditNote,DebitNote,CreditAdjustment,DebitAdjustment}, Amount>0, party exists
  │  ├─ Build PartyLedgerAdjustment (RoundCurrency)
  │  ├─ ILedgerService.ApplySupplierTransaction / CreateLedgerEntry   (LedgerService.cs:57/32)
  │  │     └─ writes AccountLedger delta + updates Customer/Supplier.Balance
  │  ├─ Add SupplierTransaction / CustomerTransaction
  │  ├─ IAccountingOutboxService.EnqueuePartyLedgerAdjustmentAsync    (AccountingOutboxService.cs:181)
  │  │     └─ writes AccountingOutboxMessages row (atomic with above)
  │  ├─ SaveChangesAsync  (single DB transaction, controller:550,726)
  │  └─ IAuditService.LogCreate("PartyLedgerAdjustment", ...)         (controller:743)
  ▼
[HTTP response PartyLedgerAdjustmentResponse with BalanceAfter]  ← Browser
  ▼
AccountingOutboxProcessor.ProcessPartyLedgerAdjustmentAsync      (AccountingOutboxProcessor.cs:1758)
  │  ├─ LoadPartyLedgerAdjustmentForAccountingPostAsync (FOR UPDATE / UPDLOCK)
  │  └─ ICommerceAccountingService.PostPartyLedgerAdjustmentAsync   (CommerceAccountingService.cs:505)
  │        └─ FinancialPostingService.PostJournalAsync              (FinancialPostingService.cs:39)
  │              └─ LedgerJournalEntry + LedgerLines (double-entry, balanced, immutable)
  ▼
Ledger (chart of accounts): 1410/2010 control  ↔  6120/6130 adjustments
```

Note: Journal posting is ASYNCHRONOUS and decoupled via the outbox. The browser receives the adjustment + balance immediately; the GL journal lands shortly after via the worker.

---

## 13. Browser Test Specification (implemented scenarios only)

Derived strictly from `Adjustments.jsx`, `AdjustmentModal.jsx`, `soaApi.js`, and `CreateAdjustment`.

### Scenarios
1. **Open Adjustments page** — `GET` parties (`/Customers` or `/Suppliers`), render party dropdown. Assert title `accounts.accountAdjustments` (Adjustments.jsx:133).
2. **Select party → balance loads** — `soaApi.getPartyBalance(partyType, partyId)` → `GET /StatementOfAccount/balance/{partyType}/{partyId}` (soaApi.js:14). Assert `partyBalance.balance` and `balanceType` render (Adjustments.jsx:182-207).
3. **Create adjustment (happy path)** — fill modal (type, amount>0, notes), submit → `soaApi.createAdjustment` → `POST /StatementOfAccount/adjustment` with `Idempotency-Key` header (soaApi.js:30; frontend has no header injection shown — **server requires it**, see assertion N3). Assert success toast `accounts.adjustmentCreated` (Adjustments.jsx:113) and balance refresh (line 115).
4. **Adjustment types exposed** — CreditNote, DebitNote, CreditAdjustment, DebitAdjustment (AdjustmentModal.jsx:27-32).

### Assertions
- A valid adjustment returns `PartyLedgerAdjustmentResponse` with `Id`, `ReferenceNumber` (CN/DN/CA/DA prefix), `Amount`, `BalanceAfter`, `IdempotencyKey`.
- Party balance changes by the adjustment amount after submit (balance re-fetched).
- Submission lock prevents double-submit (`submissionLockRef`, Adjustments.jsx:93-97, 119).

### Negative Cases
- **N1 Amount ≤ 0** — frontend blocks (`amount <= 0` → `treasury.invalidAmount`, Adjustments.jsx:102-105). Server also returns 400 "Adjustment amount must be greater than zero." (controller 580-587).
- **N2 Missing/invalid party** — server 400 "Customer/Supplier not found" (controller 589-615).
- **N3 Missing Idempotency-Key header** — server 400 "{IdempotencyKeyHeader} header is required..." (controller 539-546). **This is a server-only gate; the current frontend `soaApi.createAdjustment` does not set the header — test must confirm header is added or endpoint fails.**
- **N4 Invalid adjustment type** — server 400 "Invalid adjustment type" (controller 567-578).
- **N5 Duplicate (same Idempotency-Key)** — idempotency claim short-circuits (controller 557-561).

### Manual Adjustment
Covered by scenario 3 (modal-driven manual entry). No automated/API-only manual path beyond the same endpoint.

### Correction
Entering a CreditNote/DebitNote/CreditAdjustment/DebitAdjustment against a party is the correction mechanism. Assert the resulting balance delta and (after worker) a new GL journal in accounts 1410/2010 ↔ 6120/6130.

### Approval
**NOT IMPLEMENTED** — no approve UI/endpoint. Browser test: assert there is NO approve button/state in `Adjustments.jsx`/`AdjustmentModal.jsx`.

### Reverse
**NOT IMPLEMENTED** — no reverse UI/endpoint. Browser test: assert there is NO reverse action for an existing adjustment.

### History
**NOT IMPLEMENTED as a dedicated view** — `Adjustments.jsx` shows no adjustment list/grid; only the live party balance. Browser test: assert no adjustment history table is rendered on this page. Audit trail exists server-side via `IAuditService` only.

---

## 14. Domain Ownership Matrix

| Capability | Implemented? | Owner / Location |
|---|---|---|
| Create adjustment | IMPL | StatementOfAccountController.CreateAdjustment (534) |
| Validate adjustment | IMPL | controller 567-615 |
| Approve adjustment | NONE | — |
| Post GL journal | IMPL (async) | CommerceAccountingService.PostPartyLedgerAdjustmentAsync (505) + FinancialPostingService (39) |
| Update party balance | IMPL (sync) | LedgerService (32/57/81) |
| Reverse adjustment | NONE | — (generic ReverseJournalAsync not wired) |
| Cancel adjustment | NONE | — |
| Delete adjustment | NONE | — (soft-delete column exists, unused) |
| Archive adjustment | NONE | — |
| Write-off | NONE | — |
| Reclassification | NONE | — |
| History (read) | NONE (UI); audit log only | IAuditService (743) |
| Audit | IMPL (create) | IAuditService.LogCreate (743) |
| Export | NONE | — |
| Printing | NONE | — |
| Pagination | PART (SOA query only; no adjustment list) | controller 63-217 |
| Multi-Currency | NONE | PartyLedgerAdjustment has no currency field; uses tenant ledger currency (RoundCurrency) |
| Idempotency | IMPL | Idempotency-Key header + (Tenant,IdempotencyKey) unique (controller 539; migration 73-77) |
| Tenant isolation | IMPL | ITenantOwned + global filter (678) |
| Accounting Outbox | IMPL | AccountingOutboxService (181) + Processor (1758) |
| Frontend create | IMPL | Adjustments.jsx / AdjustmentModal.jsx |
| Frontend list/history/approve/reverse | NONE | — |

---

## 15. Canonical Pattern

Account Adjustment is a **manual, tenant-scoped, source-event aggregate** (`PartyLedgerAdjustment`) that:

1. Is created via `POST /api/StatementOfAccount/adjustment` under strict idempotency + validation.
2. Synchronously writes its intent record, a live `AccountLedger` balance delta, and a party transaction row inside one DB transaction, plus an outbox message.
3. Asynchronously posts a **new** double-entry GL journal (control account ↔ dedicated adjustments account 6120/6130) through the shared `FinancialPostingService` — exactly once, via the accounting outbox.
4. Owns NONE of the ledger/journal/balance truth; it is a *source* of those truths.

It follows the same financial posting pipeline as every other POS financial mutation (sale, payment, expense, refund). It is NOT a ledger-editor and NOT an approval-gated workflow.

---

## 16. Documentation Validation (misconceptions corrected)

- ❌ Adjustments modify old journal/ledger entries.
  ✅ Adjustments create a NEW journal + ledger entry (CommerceAccountingService.cs:505-551; LedgerService create paths).
- ❌ Adjustments own party balances.
  ✅ `LedgerService` / `AccountLedger` / `Customer.Balance` / `Supplier.Balance` own balances (LedgerService.cs; controller 661/701). The adjustment stores only `Amount`.
- ❌ Adjustments bypass accounting.
  ✅ Adjustments go through `PostPartyLedgerAdjustmentAsync` → `FinancialPostingService.PostJournalAsync` (double-entry, balanced, immutable).
- ❌ Adjustments require approval before posting.
  ✅ No approval stage exists; posting is automatic after outbox processing.
- ❌ Adjustments support reversal / cancellation / deletion.
  ✅ None of these paths are implemented.
- ❌ Adjustments are multi-currency.
  ✅ No currency field on the entity; uses tenant ledger currency via `RoundCurrency`.
- ❌ Adjustments have a status / lifecycle state machine.
  ✅ No status field or state machine; created → (outbox) → posted, with no intermediate states.
- ❌ There is an adjustment list / history / audit UI.
  ✅ Frontend only creates; audit is server-side `LogCreate` only.

---

## 17. Final Architecture Conclusion

- **Adjustment Authority:** `StatementOfAccountController.CreateAdjustment` is the sole write authority. `PartyLedgerAdjustment` is the durable intent record.
- **Source Of Truth:** `PartyLedgerAdjustments` (intent) → `AccountLedger` + `Customer`/`Supplier` balance (party truth) → `LedgerJournalEntries`/`LedgerLines` (GL truth). The adjustment is a *source*, not the truth holder.
- **Runtime:** Browser → Controller (sync write + outbox) → AccountingOutboxProcessor (async) → CommerceAccountingService → FinancialPostingService → Journal/Ledger. Decoupled via transactional outbox.
- **Aggregate Ownership:** Adjustment is its own aggregate root; Journal, Ledger, and Balance are separate aggregates it references by lineage (`SourceType`/`SourceId`), never owns.
- **Audit:** Create-only audit via `IAuditService.LogCreate`. No update/delete/reverse audit because those operations do not exist.
- **Financial Integration:** Fully integrated into the shared double-entry posting pipeline; uses dedicated GL adjustment accounts (6120/6130) to keep corrections visible and separable from operational control accounts (1410/2010).
- **Integrity Strategy:** Idempotency-Key header + (Tenant, IdempotencyKey) unique index + outbox exactly-once posting + pessimistic row lock on replay (`FOR UPDATE`/`UPDLOCK`) + balanced double-entry enforcement in `FinancialPostingService`. Tenant isolation enforced by global query filter.
- **Gaps (NOT IMPLEMENTED, explicitly):** Approval, Reversal, Cancellation, Deletion, Write-off, Reclassification, Multi-Currency, Export, Printing, Adjustment List/History UI, Locked-Period guard.

---

### Evidence index (file:line)
- PartyLedgerAdjustment.cs:10,15,17-30
- TransactionType.cs:3-18
- StatementOfAccountController.cs:22-24, 63, 222, 263, 534-761, 743, 800-862, 925
- StatementOfAccountDTOs.cs:60-83
- LedgerService.cs:13, 32, 57, 81, 117
- CommerceAccountingService.cs:505-559, 509-520, 531-536, 539-551, 1541-1548
- FinancialPostingService.cs:19-23, 25, 39, 160, 205-221, 275
- AccountingOutboxService.cs:181-186, 683-758
- AccountingOutboxProcessor.cs:54-60, 58, 724, 1226-1270, 1758-1793
- ApplicationDbContext.cs:123, 678-680, 2154-2163
- IAccountResolver.cs (AccountCodes):56, 60, 83, 84
- Migration 20260408193000:16-36, 73-82
- Frontend: Adjustments.jsx:1-264 (esp. 92-122, 133, 182-207); AdjustmentModal.jsx:4-145; soaApi.js:14-35
