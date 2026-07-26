# FLOW-ACC-03 — Internal Balance Transfer (POS Admin) Architecture

**Canonical, implementation-backed architecture reference for Shopixy ERP.**
Implementation is the ONLY source of truth. Every claim cites `file:line`.

Status legend used throughout:
- **IMPLEMENTED** — exists in code, exercised at runtime.
- **PARTIALLY IMPLEMENTED** — exists but with material gaps/limitations evidenced in code.
- **NOT IMPLEMENTED** — searched for; absent.

---

## 1. Executive Summary

**What is Internal Balance Transfer?**
Internal Balance Transfer is the accounting operation that moves money between two *internal* ledger accounts of the same tenant by creating a single balanced double-entry journal (DEBIT destination, CREDIT source). It is NOT a warehouse/stock transfer. It is a treasury/ledger reallocation, not an inflow or outflow.

- Live primary path: `TreasuryOperationService.TransferAsync` → `FinancialPostingService.PostJournalAsync`, invoked from `TreasuryController.LedgerTransfer` (`POST /api/treasury/operations/transfer`) and from `FinancialAccountsController.Transfer` (`POST /api/financialaccounts/transfer`, used by `BalanceTransfer.jsx`). (TreasuryOperationService.cs:181-239; TreasuryController.cs:625-682; FinancialAccountsController.cs:205-332)
- Legacy/secondary path: `TreasuryTransferService.TransferAsync` → `FinancialPostingService.PostJournalAsync`, invoked only from `AdminAccountingController.TreasuryTransfer` (`POST /api/admin/accounting/treasury/transfer`, Admin role). (TreasuryTransferService.cs:40-90; AdminAccountingController.cs:93-113)

**What does it own?**
- Construction of a balanced `PostingRequest` (2 lines) with a deterministic idempotency key and `SourceType = "TreasuryTransfer"`. (TreasuryOperationService.cs:215-232; TreasuryTransferService.cs:67-81)
- Validation that the two accounts are distinct and are active, same-tenant, Asset-type Cashbox/Bank treasury accounts. (TreasuryOperationService.cs:208-212, 298-325; TreasuryTransferService.cs:55-58, 202-229)
- Delegation of the actual journal+ledger write to `IFinancialPostingService`.

**What does it NOT own?**
- It does NOT own balances. Balances live on the `Account` (LedgerAccount) aggregate and are mutated only inside `FinancialPostingService` (`account.ApplyEntry(...)` then `SaveChangesAsync`). (FinancialPostingService.cs:197, 230)
- It does NOT own the ledger write. All INSERTs into `JournalEntries`/`LedgerEntries` happen in `FinancialPostingService.PostJournalInternalAsync`. (FinancialPostingService.cs:170-225)
- It does NOT own the journal-number sequence, idempotency check, balanced-journal check, PreventNegative enforcement, or tenant isolation — all owned by `FinancialPostingService`.
- It does NOT own transfer *status* / lifecycle state machine. There is NO `Transfer` entity and NO `TransferStatus` usage in this flow (see §3). The transfer is represented only by an immutable, Posted journal that can later be Reversed.

**Who owns financial truth?**
`FinancialPostingService` + the `Account`/`JournalEntry`/`LedgerEntry` aggregates are the financial authority. (FinancialPostingService.cs:14-24 — its header documents itself as the "production-grade double-entry posting engine" that guarantees atomicity, idempotency, serializable isolation, immutable journals, balanced entries, negative-balance prevention.)

**What is the source of truth?**
The General Ledger (`JournalEntries` + `LedgerEntries` tables) is the source of truth. Read APIs (`TreasuryReadService`, treasury/transactions, account balances) read from the ledger, not from legacy tables. (TreasuryController.cs:325-339 comments; TreasuryReadService.cs; TreasuryController.cs:350-493)

---

## 2. Discovery

Implemented entities (only what exists):

| Entity / Symbol | Kind | Evidence |
|---|---|---|
| `TreasuryOperationService` (impl of `ITreasuryOperationService`) | Service | TreasuryOperationService.cs:29 |
| `TransferAsync(tenantId, fromCode, toCode, amount, desc, reference, opId, userId)` | Method | TreasuryOperationService.cs:181 |
| `TreasuryTransferService` (impl of `ITreasuryTransferService`) | Service (legacy/Admin) | TreasuryTransferService.cs:18 |
| `ITreasuryTransferService.TransferAsync(tenantId, fromId, toId, amount, reference, transferId)` | Interface method | ITreasuryTransferService.cs:25 |
| `FinancialPostingService` | Posting engine (financial authority) | FinancialPostingService.cs:25 |
| `IFinancialPostingService.PostJournalAsync` | Method | FinancialPostingService.cs:39 |
| `PostingRequest` / `PostingLine` | DTOs (request payload) | TreasuryOperationService.cs:93-108 |
| `JournalEntry` | Aggregate (ledger) | JournalEntry.cs:18 |
| `LedgerEntry` | Aggregate (ledger line) | LedgerEntry.cs:16 |
| `Account` (LedgerAccount) | Aggregate (owns balance) | FinancialPostingService.cs:132, 197 |
| `JournalNumberSequence` | Per-tenant journal-number generator | FinancialPostingService.cs:867-894 |
| `TreasuryAccount` | Treasury authority mapping (Cashbox/Bank/Clearing) | TreasuryOperationService.cs:304-318 |
| `TreasuryAccountType` enum (Cashbox, Bank, Clearing) | Enum | TreasuryOperationService.cs:312 |
| `TreasuryTransferRequest` DTO (TreasuryController) | API DTO | TreasuryDTOs.cs:118 |
| `TreasuryTransferRequest` record (AdminAccountingController) | API DTO | AdminAccountingController.cs:156 |
| `TransferRequestDto` (FinancialAccounts) | API DTO | FinancialAccountsController.cs:205 (used by BalanceTransfer.jsx) |
| `TreasuryAccountOptionDto` | Read DTO | TreasuryDTOs.cs:209 |

**NOT IMPLEMENTED (searched, absent):**
- A `Transfer` entity / `Transfers` table. No class named `Transfer` (only `StockTransfer`, a warehouse entity — unrelated). Search for `class Transfer\b`, `TransferHistory`, `TransferJournal`, `ITransfer` → **0 matches**.
- A `TransferStatus` state machine in this flow. `TransferStatus` enum exists (TransferStatus.cs:3) but is used ONLY by `StockTransfer` (warehouse). Search results: `TransferStatus` appears in `StockTransfersController.cs`, `StockTransfer.cs`, `WarehouseAuthorityArchitectureCertificationTests.cs` — **never in any treasury/balance-transfer code**.
- A dedicated Transfer History / audit table. History = the ledger rows themselves (`JournalEntries`/`LedgerEntries` + `BalanceAfter` per line). LedgerEntry.cs:58.
- A Transfer-specific outbox / event. Transfers reuse the generic accounting outbox (see §11).

---

## 3. Transfer Identity

There is **NO dedicated Transfer identity/row**. The transfer's identity is carried by the `JournalEntry` it produces.

| Field | Value / Source | Evidence |
|---|---|---|
| Primary Key | `JournalEntry.Id` (Guid). No separate transfer id. | JournalEntry.cs:18; returned from `PostJournalAsync` (TreasuryOperationService.cs:232) |
| Transfer Type | `JournalEntry.SourceType = "TreasuryTransfer"` | TreasuryOperationService.cs:39, 219; TreasuryTransferService.cs:25, 71 |
| Transfer Source | `fromAccount` resolved to `AccountId` → CREDIT line. In legacy path, `fromAccountId` passed directly (TreasuryTransferService.cs:79) | TreasuryOperationService.cs:227-229 |
| Transfer Destination | `toAccount` resolved to `AccountId` → DEBIT line. | TreasuryOperationService.cs:227-228 |
| Transfer Status | **NOT APPLICABLE** — no status field. Journal `Status` ∈ {Posted, Reversed}. (JournalStatus.cs) | JournalEntry.cs:98; NOT a transfer status |
| Amount | `decimal amount` (must be > 0). No currency field — currency is NOT modeled on the transfer; the ledger account carries currency context. | TreasuryOperationService.cs:192; ValidateAmount.cs:294 |
| Currency | **NOT IMPLEMENTED** as a transfer property. No currency parameter on `TransferAsync`. (Currency reference table `S40_AddCurrencyReferenceTable` exists in migrations but is not threaded into transfer inputs.) | TreasuryOperationService.cs:181-190 signature |
| Reference | `SourceReference` (free text, optional). E.g. `"SETTLE-ARX-0001"`. | TreasuryOperationService.cs:221; TreasuryTransfers.jsx:174 |
| Tenant Ownership | `JournalEntry.TenantId` (required, non-empty). Posting rejects `TenantId == Guid.Empty`. | FinancialPostingService.cs:372-375; JournalEntry.cs:20 |
| Idempotency | `IdempotencyKey = "TREASURY_TRANSFER:{tenantId}:{operationId}"` (operation path) or `TRANSFER:{tenantId}:{transferId}` (legacy path). Enforced in `FinancialPostingService` (pre-tx + in-tx). | TreasuryOperationService.cs:195; TreasuryTransferService.cs:61; FinancialPostingService.cs:56-73, 106-121 |
| Audit Fields | `JournalEntry.PostedAtUtc`, `CreatedByUserId`, `Description`, `SourceId`, `JournalNumber`. Per-line `BalanceAfter`, `LineNumber`, `LedgerEntry.CreatedAtUtc`. | JournalEntry.cs:41, 144; LedgerEntry.cs:59, 74, 80 |

---

## 4. Transfer Ownership

| Capability | Owner | Evidence |
|---|---|---|
| Transfer **request** intake | API controllers (`TreasuryController`, `FinancialAccountsController`, `AdminAccountingController`) | TreasuryController.cs:625; FinancialAccountsController.cs:205; AdminAccountingController.cs:93 |
| Transfer **validation** (amount > 0, distinct accounts, treasury authority) | Service layer (`TreasuryOperationService.EnsureTreasuryAccountAsync` / `ValidateAmount`; `TreasuryTransferService.RequireTreasuryTypeAsync`) | TreasuryOperationService.cs:292-325; TreasuryTransferService.cs:49-58, 202-229 |
| Movement **authority** (is this a permitted internal move?) | Service layer checks the account is an active same-tenant Asset Cashbox/Bank treasury account. NOTE: a *TreasuryTransfer* SourceType is NOT subject to the certified inflow/outflow authority gating (see §8). | TreasuryOperationService.cs:298-325 |
| Posting **orchestration** | `IFinancialPostingService.PostJournalAsync` (the financial engine) | TreasuryOperationService.cs:232 |
| Journal/ledger **write** | `FinancialPostingService` (owns `JournalEntries`/`LedgerEntries` INSERT + balance mutation) | FinancialPostingService.cs:170-230 |
| Balance **movement** | `Account.ApplyEntry` inside `FinancialPostingService` | FinancialPostingService.cs:197 |
| Journal number **generation** | `FinancialPostingService.GenerateJournalNumberAsync` | FinancialPostingService.cs:867 |
| Idempotency / duplicates | `FinancialPostingService` (unique per tenant on `IdempotencyKey`) | FinancialPostingService.cs:56-121 |
| History / audit | The ledger rows themselves (no separate history table) | LedgerEntry.cs:58 |
| Read models (balances, movements, trial balance) | `ITreasuryReadService` (reads from ledger) | TreasuryController.cs:27, 473-493 |

**Conclusion:** Internal Balance Transfer owns the *intent + validation + orchestration* of an internal money move. It does NOT own the financial write, balances, numbering, idempotency, or history — those belong to the ledger/posting engine.

---

## 5. Transfer Lifecycle

Reverse-engineered permitted stages (only implemented ones):

| Stage | Implemented? | Evidence |
|---|---|---|
| **Create** | YES | Controller accepts request → service builds `PostingRequest` → `PostJournalAsync`. TreasuryOperationService.cs:181-232 |
| **Validate** | YES (pre-posting) | `ValidateAmount`, `EnsureTreasuryAccountAsync`, distinct-account checks. TreasuryOperationService.cs:192-212, 292-325 |
| **Authorize** | PARTIAL | No human approval step. "Authority" = (a) authentication/role (`Owner,Admin,Manager` for operations; `Admin` for legacy) and (b) programmatic treasury-account eligibility check. There is NO approve/reject workflow. TreasuryController.cs:626; AdminAccountingController.cs:13 |
| **Post** | YES | `PostJournalAsync` writes journal + ledger in one serializable tx. FinancialPostingService.cs:90-235 |
| **Complete** | YES (implicit) | On successful commit the journal is in `JournalStatus.Posted` and control returns with `journalId`/`journalNumber`. No separate "Completed" state. JournalEntry.cs:98,215 |
| **Fail** | YES (exception path) | Validation throws (`TreasuryValidationException`, `InsufficientFundsException`, `AccountNotFoundException`, `InactiveAccountException`, `InvalidJournalException`); controller maps to 400/500; outer tx rolls back. FinancialAccountsController.cs:326-331; TreasuryController.cs:531-681 |
| **Reverse** | YES (via generic reversal, not transfer-specific) | `FinancialPostingService.ReverseJournalAsync(journalId, reason, userId)` creates a swapped journal (`SourceType="Reversal"`, `IdempotencyKey="REVERSE:{tenantId}:{journalId}"`), marks original `Reversed`. FinancialPostingService.cs:256-359; JournalEntry.cs:119-135 |
| **Cancel** | NO | No cancel endpoint for a submitted transfer. The only pre-commit cancel is the UI confirmation modal (TreasuryTransfers.jsx:441-453), which simply does not submit. No server-side cancel/void. |
| **Archive** | NO | Journals are immutable, never deleted/archived. Ledger retention only. JournalEntry.cs:11-17 |
| **Retry** | YES (idempotent replay) | Re-sending the same `operationId`/`transferId` returns the existing journal rather than double-posting. FinancialPostingService.cs:65-73, 114-121 |

**Lifecycle shape:** Create → Validate → (role/eligibility authorize) → Post(atomic) → Posted (terminal unless Reversed). Reversal is a separate, generic ledger operation keyed by original journal id.

---

## 6. Aggregate Analysis

```
TreasuryOperationService / TreasuryTransferService  (orchestrator, NOT an aggregate)
        │  builds PostingRequest(SourceType="TreasuryTransfer", 2 balanced lines)
        ▼
IFinancialPostingService (FinancialPostingService)  ← financial authority / aggregate coordinator
        │
        ├── JournalEntry  (aggregate root; owns TenantId, JournalNumber, Status, IdempotencyKey)
        │        │ 1─*
        │        ▼
        │     LedgerEntry  (lines; owns DebitAmount/CreditAmount/BalanceAfter/LineNumber)
        │
        ├── Account (LedgerAccount)  (owns CurrentBalance; mutated via ApplyEntry)
        │
        ├── JournalNumberSequence  (per-tenant next-number source)
        │
        └── TreasuryAccount  (authority mapping: AccountId → Cashbox/Bank/Clearing; read-only check)
```

Who owns what:
- **Transfer orchestrator** owns: intent, validation, idempotency-key construction.
- **FinancialPostingService** owns: the atomic posting transaction, balance mutation, numbering, idempotency check, balanced-journal guarantee, PreventNegative enforcement, reversal.
- **JournalEntry** owns: the immutable record + status (Posted/Reversed).
- **LedgerEntry** owns: per-line debit/credit + `BalanceAfter` snapshot.
- **Account** owns: `CurrentBalance` (the live balance).
- **ITreasuryReadService / Reports** own: read projections (trial balance, general ledger, movements) — derived from the above, not authoritative.

There is **NO Transfer aggregate**. The transfer is a transient command that materializes as a JournalEntry + 2 LedgerEntries.

---

## 7. Transfer Behavior

| Behavior | Implemented? | Evidence / Notes |
|---|---|---|
| Create | YES | TreasuryOperationService.cs:181 |
| Validate | YES | Amount>0, distinct accounts, treasury-account eligibility. TreasuryOperationService.cs:192-212, 298-325 |
| Execute | YES | `PostJournalAsync` inside serializable tx. FinancialPostingService.cs:90-253 |
| Retry | YES | Idempotent replay via same `operationId`. FinancialPostingService.cs:65-73 |
| Reverse | YES (generic) | `ReverseJournalAsync`. FinancialPostingService.cs:256 |
| Cancel | NO | No server cancel; only UI confirmation gating. |
| Search | YES (read side) | Treasury transactions/movements queryable by `SourceType="TreasuryTransfer"` via `ITreasuryReadService`. TreasuryController.cs:114-256, 373-403 |
| History | YES (ledger) | Ledger rows + `BalanceAfter`. LedgerEntry.cs:58 |
| Delete | NO | Journals immutable; no delete. JournalEntry.cs:11-17 |

**Special runtime detail (`FinancialAccountsController.Transfer`):** This endpoint wraps the call in its OWN `BeginTransactionAsync` (FinancialAccountsController.cs:208) and also resolves `FinancialAccount`/`Cashbox` → treasury ledger `AccountCode` via `ResolveLedgerTransferAccountCodeAsync` (FinancialAccountsController.cs:334-359) before calling `TreasuryOperationService.TransferAsync`. It blocks transfers that resolve to the same treasury control account (e.g. two "Visa" financial accounts both map to `PosVisaAccount`) because the net ledger effect would be zero. (FinancialAccountsController.cs:277-281) Note: the outer controller transaction is largely redundant because `PostJournalAsync` opens its own serializable transaction when none is active (FinancialPostingService.cs:83-98); the controller's `SaveChangesAsync` at line 316 has nothing else to persist (the transfer itself is not stored), so the outer tx effectively just wraps the read + the inner posting's commit.

---

## 8. Financial Behavior

**Debit / Credit construction (operation path):**
- DEBIT `toAccount` (money in), CREDIT `fromAccount` (money out). Equal amounts. (TreasuryOperationService.cs:226-229)
- Legacy path is identical. (TreasuryTransferService.cs:78-79)

**Journal creation:** Always exactly 2 lines, balanced, `SourceType="TreasuryTransfer"`. Built by the service, written by `FinancialPostingService`. (TreasuryOperationService.cs:215-232)

**Ledger ownership:** `FinancialPostingService` creates `LedgerEntry` rows and calls `account.ApplyEntry(debit, credit)` then persists. (FinancialPostingService.cs:191-224, 230)

**Balance movement:** Mutated only via `account.ApplyEntry` inside the posting engine; transfer service never touches `Account` balance directly. (FinancialPostingService.cs:197)

**PreventNegative (`NOT IMPLEMENTED` for transfers as a hard stop by the transfer service itself, but ENFORCED by the posting engine):**
- `FinancialPostingService.ValidateNoNegativeBalances` projects each account's resulting balance; if a `PreventNegative` account would go below 0, it throws `InsufficientFundsException`. (FinancialPostingService.cs:566-612)
- Cashbox accounts are seeded with `PreventNegative = true`. (CashboxLedgerAccountService.cs:128; TenantAccountSeederTests.cs:119)
- IMPORTANT: The transfer service does NOT pre-check sufficiency. The `FinancialAccountsController.Transfer` only *logs a warning* if `sourceBalance < amount` (FinancialAccountsController.cs:283-290) — it does NOT block. The actual block happens downstream in the posting engine's PreventNegative check, and only for accounts flagged `PreventNegative`. A non-PreventNegative source can be overdrawn at the ledger level (the engine only guards PreventNegative accounts).

**Financial authority (critical nuance):**
- `FinancialPostingService.ValidateTreasuryInflowAuthorityAsync` and `...OutflowAuthorityAsync` gate postings whose net cash effect is an inflow/outflow against a certified source (SalesInvoice, Order_Payment, GatewaySettlement, PurchaseInvoice, SupplierPayment, Expense, Refund, etc.). (FinancialPostingService.cs:614-861)
- The `switch` in BOTH authority validators has a `_ => null` default that throws "not authorized" for unrecognized sources. (FinancialPostingService.cs:693, 835)
- `SourceType = "TreasuryTransfer"` is **NOT** in either switch. Therefore a transfer has `netCashDecrease` and `netCashIncrease` BOTH evaluated, but since it is an internal move (one cash account debited, another credited) the *net* change on cashbox/bank accounts is zero, so both guards `return` early (lines 636-639, 746-749) without requiring certified authority. **Implementation-backed conclusion:** internal balance transfers are intentionally exempt from certified inflow/outflow authority because they are reallocations, not real money in/out. This is why the transfer path can run without a SalesInvoice/Settlement backing.
- `TreasuryTransferService` (legacy) uses `AccountId` directly and is also not subject to inflow/outflow gating (its SourceType is the same `"TreasuryTransfer"`).

**Transfer consistency:** Achieved because the whole posting (journal + ledger + balance) commits atomically in one serializable transaction. (FinancialPostingService.cs:96-234)

**Idempotency:** Dual-checked (pre-tx and in-tx) on `IdempotencyKey` scoped per tenant; re-play returns existing journal after verifying line/identity match; mismatch throws. (FinancialPostingService.cs:56-121, 526-559)

---

## 9. Constraints (business rules, implementation-backed)

| Rule | Enforcement | Evidence |
|---|---|---|
| Amount must be > 0 | Transfer service throws `TreasuryValidationException`/`ArgumentException` | TreasuryOperationService.cs:292-296; TreasuryTransferService.cs:49-50 |
| Source != Destination (different account id) | Transfer service throws | TreasuryOperationService.cs:208-209; TreasuryTransferService.cs:52-53 |
| Both accounts must be active same-tenant Asset Cashbox/Bank treasury accounts | `EnsureTreasuryAccountAsync` / `RequireTreasuryTypeAsync` throws | TreasuryOperationService.cs:298-325; TreasuryTransferService.cs:202-229 |
| Journal must have ≥ 2 lines, exactly one side per line > 0, debits == credits | `ValidateJournalAsync` throws `InvalidJournalException`/`UnbalancedJournalException` | FinancialPostingService.cs:389-441 |
| Idempotency key required + unique per tenant | `ValidateJournalAsync` + dual existence check | FinancialPostingService.cs:467-470, 56-121 |
| Description required | `ValidateJournalAsync` | FinancialPostingService.cs:477-480 |
| TenantId required (cross-tenant isolation) | `ValidateJournalAsync` + all queries filter `TenantId` | FinancialPostingService.cs:372-375; TreasuryOperationService.cs:201 |
| PreventNegative accounts cannot go negative | `ValidateNoNegativeBalances` throws `InsufficientFundsException` | FinancialPostingService.cs:566-612 |
| No transfer into the same treasury control account (net-zero) | `FinancialAccountsController` blocks resolved-equal codes | FinancialAccountsController.cs:277-281 |
| Role authorization | `[Authorize(Roles="Owner,Admin,Manager")]` (operations) / `[Authorize(Roles="Admin")]` (legacy admin) | TreasuryController.cs:626; AdminAccountingController.cs:13 |
| Rate limiting on financial writes | `[EnableRateLimiting("FinancialWrite")]` | TreasuryController.cs:627 |
| UI never writes balances/ledger directly | Architectural constraint enforced in code (UI only POSTs to operation endpoints) | TreasuryTransfers.jsx:9-18 header; TreasuryIsolationTests.cs:161-193 |

**NOT IMPLEMENTED constraints:**
- Currency enforcement on transfer (no currency param). 
- Per-transfer approval/limits (amount caps, dual control).
- Soft-delete / cancel of a posted transfer (only full reversal).

---

## 10. SQL Transactions

| Aspect | Implementation |
|---|---|
| Transaction scope | `FinancialPostingService.PostJournalInternalAsync` opens its own transaction **iff** none is active (`ownsTransaction = CurrentTransaction == null`). All journal+ledger+balance writes commit together. | FinancialPostingService.cs:83-98, 230-234 |
| Isolation level | **Serializable** (`BeginTransactionAsync(IsolationLevel.Serializable)`). | FinancialPostingService.cs:97, 304 |
| Locks | Accounts loaded **ordered by Id** (`OrderBy(a => a.Id)`) to prevent deadlocks; balances mutated in-tx. | FinancialPostingService.cs:129-136 |
| Rollback | On any exception, `transaction.RollbackAsync` then rethrow. Controller-level try/catch also rolls back its own tx. | FinancialPostingService.cs:244-252; FinancialAccountsController.cs:326-331 |
| Retries | EF `CreateExecutionStrategy()` (retry policy) wraps the inner execution for transient faults. | FinancialPostingService.cs:84-87 |
| Atomicity | All-or-nothing: journal, ledger lines, and `Account.CurrentBalance` in one commit. | FinancialPostingService.cs:186-234 |
| Concurrency | Serializable isolation + ordered account locking + in-transaction idempotency re-check prevents double-post races. | FinancialPostingService.cs:106-121, 129-136 |
| Journal number uniqueness | `JournalNumberSequence.AllocateNext` under the same serializable tx. | FinancialPostingService.cs:867-894 |

**Note on nested transactions:** `FinancialAccountsController.Transfer` opens an outer transaction (FinancialAccountsController.cs:208) but `PostJournalAsync` detects an active transaction and does NOT open its own (ownsTransaction=false), so the inner posting participates in the outer tx. (FinancialPostingService.cs:83) The controller's extra `SaveChangesAsync` (line 316) persists nothing transfer-specific (the transfer is not stored), so the outer tx mainly frames the read+posting.

---

## 11. Events

| Event type | Implemented? | Evidence |
|---|---|---|
| Transfer-specific domain event | NO | No `Transfer*Event` class; transfers are not first-class events. |
| Posting event / outbox | YES (generic) | The ledger write also emits an `AccountingOutboxMessage` (the canonical accounting outbox). Treasury/transfer postings flow through the same outbox as all other postings. | AccountingOutbox references: tools/SIDmsgRunner/Program.cs:68-72; Eventing/AccountingOutboxProcessor.cs; AccountingOutboxMessage.Create |
| Projection events / consumers | YES (generic) | `AccountingOutboxProcessor` publishes; read projections (trial balance, general ledger, treasury movements) are derived from the ledger via `ITreasuryReadService` / projection queries. | TreasuryReadService.cs; TreasuryController.cs:373-493 |
| Webhook / external consumers | NOT in transfer scope | Transfers are internal; no external notification is transfer-specific. |

**Implementation-backed conclusion:** Internal Balance Transfer does NOT have a bespoke event or outbox row. It reuses the unified accounting outbox, meaning a transfer journal is observable to the same projection/consumer pipeline as any other journal. There is no separate "TransferPosted" event.

---

## 12. Runtime Architecture

```
[UI: TreasuryTransfers.jsx | BalanceTransfer.jsx]
        │  POST /api/treasury/operations/transfer  (Owner,Admin,Manager + rate-limit)
        │  POST /api/financialaccounts/transfer    (any authed; resolves FinancialAccount/Cashbox→ledger code)
        ▼
[TreasuryController.LedgerTransfer | FinancialAccountsController.Transfer]
        │  (role auth, DTO validation, resolve account codes, distinct-control-account guard)
        ▼
[TreasuryOperationService.TransferAsync]
        │  ValidateAmount, EnsureTreasuryAccountAsync (Cashbox/Bank, same-tenant, active, Asset)
        │  build PostingRequest{ SourceType="TreasuryTransfer", 2 balanced lines, IdempotencyKey="TREASURY_TRANSFER:{tid}:{opId}" }
        ▼
[IFinancialPostingService.PostJournalAsync]  ◄── financial authority
        │  ├─ idempotency check (pre + in tx)
        │  ├─ ValidateJournalAsync (balanced, ≥2 lines, required fields)
        │  ├─ SERIALIZABLE tx; lock accounts ordered by Id
        │  ├─ ValidateNoNegativeBalances (PreventNegative)
        │  ├─ GenerateJournalNumberAsync
        │  ├─ INSERT JournalEntry + 2× LedgerEntry
        │  ├─ Account.ApplyEntry (balance mutation)
        │  ├─ emit AccountingOutboxMessage
        │  └─ COMMIT
        ▼
[JournalEntry (Posted) + LedgerEntries + updated Account.CurrentBalance]
        │
        ▼
[AccountingOutbox → projections / ITreasuryReadService → Trial Balance / General Ledger / Movements]
```

Legacy/Admin variant (partially parallel):
```
[AdminAccountingController.TreasuryTransfer]  (Admin only)
   → [TreasuryTransferService.TransferAsync] (by AccountId, types Cashbox/Bank)
   → [IFinancialPostingService.PostJournalAsync]  (same engine)
```
`TreasuryTransferService.SettleClearingAsync` / `DepositToBankAsync` exist but the controller endpoints for settle/deposit-via-admin are mostly disabled: `AdminAccountingController.SettleClearing` returns **410 Gone** ("Manual clearing settlement is disabled. Use the certified payment settlement workflow."). (AdminAccountingController.cs:118-127) The live deposit path is `TreasuryController.LedgerDeposit` → `TreasuryOperationService.DepositAsync`. (TreasuryController.cs:504-556; TreasuryOperationService.cs:57-117)

---

## 13. Browser Test Specification

### Screens / Endpoints under test
- `BalanceTransfer.jsx` → `POST /api/financialaccounts/transfer`
- `TreasuryTransfers.jsx` → `POST /api/treasury/operations/transfer`

### Positive scenarios
1. **Valid transfer (TreasuryTransfers):** select distinct from/to ledger accounts (Cashbox↔Bank), amount ≤ source balance, description present → expect 200 with `journalNumber`; balances update; General Ledger shows 1 journal, 2 lines (DEBIT dest, CREDIT src), trial balance unchanged net.
2. **Valid transfer (BalanceTransfer):** move between an Account and a Cashbox → expect 200 + `journalNumber`; source/dest balances update after refresh.
3. **Idempotent replay:** submit same `operationId` twice → second returns SAME `journalNumber`, no duplicate journal, balances unchanged on 2nd call.
4. **Confirmation modal (TreasuryTransfers):** submit shows confirm modal; "Confirm Transfer" posts; "Cancel" does not post. (TreasuryTransfers.jsx:397-458)
5. **Reversal:** via ledger reversal UI/API on the produced journal → original marked `Reversed`, new reversal journal with swapped lines; balances restored.

### Negative cases / assertions
1. **Amount ≤ 0:** UI disables/invalidates (`treasury.invalidAmount`); API → 400. (TreasuryTransfers.jsx:121-124; TreasuryOperationService.cs:294-295)
2. **Same account:** from==to blocked at service (`TreasuryValidationException`) and at UI (`treasury.sameAccount`). (TreasuryOperationService.cs:208-209; TreasuryTransfers.jsx:116-119)
3. **Insufficient balance on PreventNegative source:** UI warns (`treasury.insufficientBalance`) but the authoritative block is the posting engine throwing `InsufficientFundsException` → 400. NOTE: `FinancialAccountsController` only *logs* on shortfall (FinancialAccountsController.cs:283-290); the hard block is in `FinancialPostingService.ValidateNoNegativeBalances`. (FinancialPostingService.cs:566-612)
4. **Inactive / non-treasury / cross-tenant account:** `EnsureTreasuryAccountAsync` throws → 400. (TreasuryOperationService.cs:298-325)
5. **Same treasury control account (FinancialAccount→Visa maps to PosVisa both sides):** blocked by `FinancialAccountsController` before posting. (FinancialAccountsController.cs:277-281)
6. **Unauthenticated / wrong role:** 401/403 (operations require Owner/Admin/Manager; legacy admin requires Admin). (TreasuryController.cs:21,626; AdminAccountingController.cs:13)
7. **Rate limit:** rapid repeats hit `FinancialWrite` limiter → 429. (TreasuryController.cs:627)
8. **Missing OperationId:** `TreasuryTransferRequest.OperationId` is `[Required]` → 400. (TreasuryDTOs.cs:132)

### Business validation to assert
- Trial Balance remains zero-variance after every transfer (TreasuryTransfers.jsx:254-258 guarantee).
- No legacy table write (cashbox/visa balance columns) — balances come only from ledger.
- Journal is immutable (no PUT/DELETE on journals); only reversal allowed.

---

## 14. Domain Ownership Matrix

| Concern | Owner | Notes |
|---|---|---|
| Transfer Identity | `JournalEntry` (SourceType="TreasuryTransfer") | No separate transfer row |
| Transfer Validation (inputs) | `TreasuryOperationService` / `TreasuryTransferService` | amount, distinct, treasury-eligible |
| Transfer Authority (authn/authz) | API `[Authorize]` roles + treasury-account eligibility check | No approval workflow |
| Posting | `IFinancialPostingService` | financial authority |
| Journal | `JournalEntry` aggregate | immutable, Posted/Reversed |
| Ledger | `LedgerEntry` aggregate | lines + BalanceAfter |
| Balances | `Account.CurrentBalance` (mutated in posting engine) | single source |
| Numbering | `JournalNumberSequence` (in posting engine) | per-tenant serial |
| Idempotency | `FinancialPostingService` (IdempotencyKey) | dual-checked |
| PreventNegative | `FinancialPostingService.ValidateNoNegativeBalances` | per-account flag |
| Inflow/Outflow Authority | `FinancialPostingService` (skipped for TreasuryTransfer) | transfers exempt |
| History | Ledger rows (no separate table) | audit via BalanceAfter |
| Reports / Read models | `ITreasuryReadService` | derived, not authoritative |
| Events / Outbox | Generic `AccountingOutboxMessage` | no transfer-specific event |
| Reversal | `FinancialPostingService.ReverseJournalAsync` | generic, journal-keyed |

---

## 15. Canonical Pattern

```
Transfer Request (UI / API)
        ↓  role auth + resolve account codes
Validation (amount>0, distinct, treasury-eligible)
        ↓
Posting Engine (IFinancialPostingService)
        ↓  idempotency + balanced + PreventNegative + serializable tx
Ledger (JournalEntry + LedgerEntries + Account.CurrentBalance)
        ↓  AccountingOutbox
Read Models (Trial Balance / General Ledger / Movements)
```

Internal Balance Transfer is a **thin orchestration layer over the unified ledger posting engine**, not a standalone accounting aggregate.

---

## 16. Documentation Validation (misconceptions corrected)

- ❌ "Internal Balance Transfer owns a `Transfer` table / entity."
  ✅ No `Transfer` entity exists. The transfer is a transient command materialized as a `JournalEntry` (SourceType="TreasuryTransfer") + 2 `LedgerEntry` rows. (search: `class Transfer\b` → 0 matches)

- ❌ "Transfer has a status lifecycle (Pending/Approved/Completed/Cancelled) via `TransferStatus`."
  ✅ `TransferStatus` exists but is used ONLY by warehouse `StockTransfer`. Balance transfer has no status; the journal has `JournalStatus` ∈ {Posted, Reversed}. (TransferStatus.cs:3; used in StockTransfer.cs:20, StockTransfersController.cs)

- ❌ "Transfer writes the Ledger directly."
  ✅ Transfer service only builds a `PostingRequest`; `FinancialPostingService` performs all ledger writes. (TreasuryOperationService.cs:232 vs FinancialPostingService.cs:170-230)

- ❌ "Transfer owns balances."
  ✅ `Account.CurrentBalance` is owned/mutated solely by the posting engine via `ApplyEntry`. (FinancialPostingService.cs:197)

- ❌ "Transfers require certified payment/settlement authority (like inflows/outflows)."
  ✅ Transfers use SourceType="TreasuryTransfer", which is NOT in the inflow/outflow authority switches, so the certified-source gate is bypassed by design (internal reallocation, net cash effect = 0). (FinancialPostingService.cs:614-861, early returns at 636-639 & 746-749)

- ❌ "The UI writes or adjusts balances."
  ✅ UI only POSTs to operation endpoints; balances are ledger-derived. (TreasuryTransfers.jsx:9-18; TreasuryIsolationTests.cs:161-193)

- ❌ "TreasuryTransferService is the primary transfer path."
  ✅ It is the legacy Admin-only path (`AdminAccountingController`, Admin role). The live primary paths are `TreasuryOperationService` (Owner/Admin/Manager) and `FinancialAccountsController.Transfer` (BalanceTransfer.jsx). (AdminAccountingController.cs:93 vs TreasuryController.cs:625 & FinancialAccountsController.cs:205)

- ❌ "Settlement / manual clearing via admin is available."
  ✅ `AdminAccountingController.SettleClearing` returns 410 Gone; manual clearing settlement is disabled. (AdminAccountingController.cs:118-127)

---

## 17. Final Architecture Conclusion

**Transfer Authority.** Internal Balance Transfer has NO independent financial authority. Authority = (1) API role gating (`Owner,Admin,Manager` for operations; `Admin` for legacy) and (2) programmatic treasury-account eligibility (active, same-tenant, Asset, Cashbox/Bank). There is no approval/reject workflow and no certified-source requirement (transfers are exempt from inflow/outflow authority because net cash is unchanged).

**Financial Authority.** Solely `IFinancialPostingService` (`FinancialPostingService`): it owns atomicity, serializable isolation, idempotency, balanced-journal enforcement, PreventNegative, journal numbering, ledger write, and balance mutation. The transfer service is a client of this authority, not the authority itself.

**Runtime Flow.** UI/API → service validates + builds `PostingRequest` (SourceType="TreasuryTransfer", 2 balanced lines, tenant-scoped idempotency key) → `PostJournalAsync` → serializable tx: idempotency check (pre + in-tx), validation, PreventNegative, number alloc, INSERT journal+ledger, `Account.ApplyEntry`, emit accounting outbox → commit → projections (trial balance / GL / movements). Reversal is a generic ledger operation keyed by original journal id.

**Aggregate Ownership.** No Transfer aggregate. Materialized artifacts: `JournalEntry` (root) → `LedgerEntry` (lines, with `BalanceAfter`); `Account` owns `CurrentBalance`; `JournalNumberSequence` owns numbering; `TreasuryAccount` is a read-only authority mapping. History = the ledger itself.

**Transaction Model.** Single SERIALIZABLE transaction per posting (opened by the engine when none active; participates in an outer controller tx otherwise), with ordered-by-Id account locking, EF retry strategy, and dual idempotency guarding. All-or-nothing; journals immutable; only reversal (new journal) undoes.

**Projection Strategy.** Read models (balances, movements, trial balance, general ledger) are derived from `JournalEntries`/`LedgerEntries` via `ITreasuryReadService`, never from legacy tables. Transfers surface in those projections filtered by `SourceType="TreasuryTransfer"`. No transfer-specific event/outbox — the unified `AccountingOutboxMessage` carries the posting to consumers.

---

*Generated by reverse-engineering the Shopixy implementation. Every section is backed by the cited `file:line` evidence. Items marked NOT IMPLEMENTED were searched for and absent in the codebase at the time of writing.*
