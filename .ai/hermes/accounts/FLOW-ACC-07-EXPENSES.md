# FLOW-ACC-07 — Expenses (POS Admin) Architecture Discovery

> Canonical, implementation-backed architecture reference for the Shopixy ERP **Expenses** domain (Expense Creation, Expense Categories, Treasury Funding, Financial Posting, Party Resolution, Journal Posting, Runtime, Browser Testing).
> Every conclusion below is traced to a file:line in the live codebase. Where behavior is absent it is explicitly marked **NOT IMPLEMENTED**. No generic ERP behavior was assumed.

> **SCOPE BOUNDARY (per task):** Covers POS-admin Expenses only. Excludes: Purchase Invoices (FLOW-PURCHASES), Payroll, Inventory Costing, and CommerceHub Expenses (`CommerceHubFinancialLedgerService` promotion-expense accounting is a separate runtime). General Ledger internals are referenced only where required (see FLOW-ACC-04). Opening Balance covered in FLOW-ACC-05. Treasury/Cashbox/Bank funding accounts are covered in FLOW-ACC-02.

## Canonical source files (all evidence drawn from these)

- `PointOfSale.Core/Entities/Expense.cs` — expense record (table `Expenses`)
- `PointOfSale.Core/Entities/ExpenseCategory.cs` — expense category (table `ExpenseCategories`)
- `PointOfSale.Core/Interfaces/Accounting/IAccountResolver.cs` — `AccountCodes` constants (`Expenses = "6100"`, `VisaClearing = "1110"`, `BankAccount = "1030"`, `CashboxMain = "1010"`)
- `PointOfSale.API/Controllers/ExpensesController.cs` — `api/expenses` (create/read/update/delete/approve/report)
- `PointOfSale.API/Controllers/ExpenseCategoriesController.cs` — `api/expensecategories` (CRUD + toggle)
- `PointOfSale.API/DTOs/ExpenseDTOs.cs` — request/response records
- `PointOfSale.Infrastructure/Accounting/AccountingOutboxService.cs` — `EnqueuePosExpenseAsync`, `EnqueueExpenseReversalAsync`
- `PointOfSale.Infrastructure/Accounting/AccountingOutboxProcessor.cs` — `PosExpense` / `ExpenseReversal` dispatch + `ProcessPosExpenseAsync` / `ProcessExpenseReversalAsync`
- `PointOfSale.Infrastructure/Accounting/CommerceAccountingService.cs` — `PostPosExpenseAsync` (builds the journal)
- `PointOfSale.Infrastructure/Accounting/FinancialPostingService.cs` — double-entry `PostJournalAsync`
- `PointOfSale.Infrastructure/Accounting/FinancialReportService.cs` — Trial Balance / P&L / Balance Sheet (ledger-only)
- `PointOfSale.Tests/Architecture/ExpenseFinancialSafetyHardeningTests.cs`, `PointOfSale.Tests/ExpenseControllerIdempotencySafetyTests.cs` — safety invariants

---

## 1. Executive Summary

**What is an Expense?** A `Expense` is a POS-admin operating cost record (`PointOfSale.Core/Entities/Expense.cs`). It is a business-side record of money spent (cash/visa/bank) classified by a tenant-owned `ExpenseCategory`. It carries approval state and a monotonic `AccountingMutationVersion` that drives ledger posting through the outbox.

**Who owns it?** The `ExpensesController` orchestrates every write. There is **no** `ExpenseService` / `IExpenseService` aggregate root. The controller calls `IAccountingOutboxService`; the background `AccountingOutboxProcessor` calls `CommerceAccountingService.PostPosExpenseAsync`, which calls `FinancialPostingService.PostJournalAsync`.

**What does it own?** Its own immutable-ish record (amount, method, category, supplier, cashbox/bank, reference, approval, mutation version) and exactly one accounting-outbox message per mutation.

**What does it NOT own?**
- It does **not** own balances. The controller explicitly comments: *"DO NOT mutate balances directly - expense posting must stay on the outbox/ledger path."* (`ExpensesController.cs:243`).
- It does **not** own the General Ledger (owned by `FinancialPostingService`/`JournalEntry`).
- It does **not** own the funding account balance — it only references a CashboxId/BankId; posting debits `Expenses` (6100) and credits the resolved funding/clearing account.
- It does **not** own the Expense Report — the report is a read projection over the `Expenses` business table (`ExpensesController.GetReport`, `ExpensesController.cs:905-989`).

**Financial source of truth:** The **General Ledger** (`JournalEntries` + `LedgerEntries` + `LedgerAccounts.CurrentBalance`) is the double-entry financial truth. The `Expenses` business table is the operational record; it is *not* a financial authority. Profit & Loss consumes the GL (`FinancialReportService.GetProfitAndLossAsync`, `FinancialReportService.cs:193-271`), **not** the `Expenses` table.

---

## 2. Discovery — Implemented Components

| Component | Type | Evidence | Status |
|---|---|---|---|
| `Expense` | Entity / table `Expenses` | `Entities/Expense.cs` | **Implemented** |
| `ExpenseCategory` | Entity / table `ExpenseCategories` | `Entities/ExpenseCategory.cs` | **Implemented** |
| `GET /api/expenses` | API (list, paged, filters) | `ExpensesController.cs:61` | **Implemented** |
| `GET /api/expenses/{id}` | API (read) | `ExpensesController.cs:141` | **Implemented** |
| `POST /api/expenses` | API (create) | `ExpensesController.cs:183` | **Implemented** |
| `PUT /api/expenses/{id}` | API (update) | `ExpensesController.cs:264` | **Implemented** |
| `DELETE /api/expenses/{id}` | API (soft delete) | `ExpensesController.cs:336` | **Implemented** |
| `POST /api/expenses/{id}/approve` | API (approve/reject) | `ExpensesController.cs:402` | **Implemented** |
| `POST /api/expenses/report` | API (report read model) | `ExpensesController.cs:905` | **Implemented** |
| `GET/POST/PUT/DELETE /api/expensecategories` | API (category CRUD) | `ExpenseCategoriesController.cs:28-147` | **Implemented** |
| `PATCH /api/expensecategories/{id}/toggle-status` | API | `ExpenseCategoriesController.cs:149` | **Implemented** |
| `IAccountingOutboxService.EnqueuePosExpenseAsync` | Outbox enqueue | `AccountingOutboxService.cs:478` | **Implemented** |
| `IAccountingOutboxService.EnqueueExpenseReversalAsync` | Outbox enqueue (reverse) | `AccountingOutboxService.cs:503` | **Implemented** |
| `AccountingOutboxProcessor` `PosExpense`/`ExpenseReversal` | Background dispatch | `AccountingOutboxProcessor.cs:61-62, 727-728` | **Implemented** |
| `CommerceAccountingService.PostPosExpenseAsync` | GL posting builder | `CommerceAccountingService.cs:772` | **Implemented** |
| `FinancialPostingService.PostJournalAsync` | Double-entry engine | (FLOW-ACC-04) | **Implemented** |
| Expense Receipt upload endpoint | API | no `IFormFile` handler / no upload route in `ExpensesController.cs` | **NOT IMPLEMENTED** (field only) |
| Expense-level id/credit/debit reversal endpoint | API | no `reverse`/`void` route | **NOT IMPLEMENTED** (handled via outbox mutation) |
| Expense Archive / Restore | Behavior | not in controller | **NOT IMPLEMENTED** |
| Expense Attachment model (separate) | Entity | `ReceiptPath` string only | **NOT IMPLEMENTED** |
| Expense Analytics | Read model | only `report` aggregation | **PARTIAL** (report only; no dedicated analytics) |
| Expense number / reference generator | Behavior | `ReferenceNumber` is a free-text nullable input | **NOT IMPLEMENTED** (no system-generated expense number) |

---

## 3. Expense Identity

| Attribute | Field | Evidence |
|---|---|---|
| Identity | `Id` (Guid PK) | `Expense.cs` via `BaseEntity` |
| Tenant ownership | `TenantId` (Guid, NOT NULL) | `Expense.cs:13`; `ITenantOwned` |
| Expense Category | `ExpenseCategoryId` (Guid, Required) + nav `Category` | `Expense.cs:16-19` |
| Expense date | `ExpenseDate` (DateTime, Required, UTC) | `Expense.cs:21-22`; controller forces `DateTimeKind.Utc` (`ExpensesController.cs:213`) |
| Amount | `Amount` (decimal(18,2), Required) | `Expense.cs:24-26` |
| Description | `Description` (string(200), Required) | `Expense.cs:28-30` |
| Notes | `Notes` (string?, optional) | `Expense.cs:32` |
| Payment Method | `PaymentMethod` (string(50), default `"Cash"`) | `Expense.cs:34-36` — values: Cash, Card/Visa, BankTransfer, Cheque (per comment) |
| Supplier | `SupplierId` (Guid?, optional) + nav `Supplier` | `Expense.cs:38-41` |
| Funding — Cashbox | `CashboxId` (Guid?, optional) + nav `Cashbox` | `Expense.cs:43-46` |
| Funding — Bank | `BankId` (Guid?, optional) + nav `Bank` | `Expense.cs:48-51` |
| Receipt | `ReceiptPath` (string?(500), optional) | `Expense.cs:53-54` |
| Reference Number | `ReferenceNumber` (string?(100), optional, free text) | `Expense.cs:56-57` |
| Recurring | `IsRecurring` (bool) + `RecurringFrequency` (string?(50)) | `Expense.cs:59-62` |
| Approval required | `RequiresApproval` (bool, default false) | `Expense.cs:64` |
| Approval state | `IsApproved` (bool), `ApprovedBy` (Guid?), `ApprovedAt` (DateTime?) | `Expense.cs:66-70` |
| Mutation version | `AccountingMutationVersion` (int) | `Expense.cs:72-76` |
| Audit | `CreatedAt`, `UpdatedAt` (via `BaseEntity`); soft delete `IsDeleted` (via `BaseEntity`) | controller sets `CreatedAt`/`UpdatedAt` (`ExpensesController.cs:229, 311, 374`) |

**Expense Number:** NOT IMPLEMENTED — there is no system-generated expense number. `ReferenceNumber` is a nullable free-text input provided by the caller (`CreateExpenseDto.ReferenceNumber`, `ExpenseDTOs.cs:59`).

**Relationships:** `Expense → ExpenseCategory` (many-to-one, required ref but category independently owned); `Expense → Supplier` (optional); `Expense → Cashbox` (optional, funding); `Expense → Bank` (optional, funding). A `JournalEntry` with `SourceType = "Expense"`, `SourceId = Expense.Id` is the financial projection (`AccountingOutboxProcessor.ProcessPosExpenseAsync:2123-2163`; `HasAnyExpenseJournalAsync:544-556`).

---

## 4. Domain Ownership

| Domain concept | Owner | Evidence |
|---|---|---|
| Expense record | `ExpensesController` (write), `Expenses` table (store) | `ExpensesController.cs` |
| Expense Category | `ExpenseCategoriesController` + `ExpenseCategories` table (independent tenant aggregate) | `ExpenseCategoriesController.cs` |
| Funding Account (Cashbox/Bank) | Treasury domain (`Cashbox`/`Bank` entities); Expense only holds FK | `Expense.cs:43-51`; funding resolved via `ResolveExpenseCashboxAccountCodeAsync` `CommerceAccountingService.cs:1505` |
| Expense Ledger (journal) | `FinancialPostingService` / `JournalEntry` | `CommerceAccountingService.cs:864` → `PostJournalAsync` |
| Party Ledger (Supplier) | **NOT participated by Expense** — no `AccountLedger`/`PartyLedgerAdjustment` write for expenses | no call in `ExpensesController`; `PostPosExpenseAsync` posts only `Expenses` vs funding account |
| Journal | `FinancialPostingService` (double-entry) | `CommerceAccountingService.cs:864` |
| Financial Posting | `CommerceAccountingService` (builder) + `FinancialPostingService` (engine) | `CommerceAccountingService.cs:772-877` |
| Reports | `ExpensesController.GetReport` (business-table projection) | `ExpensesController.cs:905-989` |
| Analytics (P&L, Trial Balance, Balance Sheet) | `FinancialReportService` (ledger-only) | `FinancialReportService.cs:41-369` |

---

## 5. Lifecycle

| Stage | Implemented? | Evidence |
|---|---|---|
| Create | ✅ | `POST /api/expenses` → `ExpensesController.CreateExpense` `:183-262` |
| Validate | ⚠️ Partial | Only idempotency-key + request-hash validation. **No** positive-amount check, **no** category-existence check, **no** supplier-existence check, **no** funding-availability check at controller level (see §9). DTO has no `[Range]`/`[Required(Amount)]` (`ExpenseDTOs.cs:49-63`). |
| Approve | ✅ | `POST /api/expenses/{id}/approve` `:402-488`. Auto-approves on create when `RequiresApproval == false` (`:225-227`). |
| Post (to GL) | ✅ (async, outbox) | `EnqueuePosExpenseAsync` `:236`; processed by `ProcessPosExpenseAsync` `:2110-2164` |
| Read | ✅ | `GET /api/expenses`, `GET /api/expenses/{id}` `:61, :141` |
| Search | ✅ | `GetExpenses` filters (category, paymentMethod, date range, isApproved) `:84-97`; report filters `:912-928` |
| Update | ✅ | `PUT /api/expenses/{id}` `:264-334` (resets `UpdatedAt`, bumps mutation version + enqueues reversal when ledger-affecting) |
| Delete | ✅ | `DELETE /api/expenses/{id}` `:336-400` — **soft delete** (`IsDeleted = true`), not physical |
| Reverse | ✅ (indirect) | No direct reverse endpoint; update/delete/approve-withdraw enqueues `ExpenseReversal` which reverses prior journal(s) and optionally reposts (`ProcessExpenseReversalAsync` `:2166-2259`) |
| Void | ❌ | No `void` route or status |
| Archive / Restore | ❌ | Not implemented |
| Idempotency | ✅ | `Idempotency-Key`/`X-Request-Id` header + `SalesRequestIdempotency` table, 3-retry claim loop (`ExpensesController.cs:574-709`) |

**Approval gating of posting:** `ShouldAccountExpense` returns `expense.IsApproved && !expense.IsDeleted` (`ExpensesController.cs:558-559`). On create, an expense is only enqueued when auto-approved or later approved. `ProcessPosExpenseAsync` additionally skips posting if `IsDeleted || !IsApproved` (`:2152-2161`).

---

## 6. Aggregate Analysis

- **Expense** is its own aggregate with a monotonic `AccountingMutationVersion` (`Expense.cs:72-76`). Each mutation that affects the ledger bumps the version and emits an outbox message (`:315, :453, :378`).
- **ExpenseCategory** is a **separate tenant-owned aggregate** (`ExpenseCategory.cs`); it owns its own name/description/active state and a one-to-many to expenses. Deletion is blocked while expenses reference it (`ExpenseCategoriesController.cs:133-138`).
- **Funding** (Cashbox/Bank) is owned by the Treasury domain, NOT the Expense aggregate. Expense only stores foreign keys and the posting resolves the *account code* (`CommerceAccountingService.cs:1505-1518`), it never mutates the cashbox/bank balance directly.
- **Journal / GL** is owned by `FinancialPostingService` — the Expense aggregate never writes ledger rows directly.
- **Reports / Read Models** are projections: the `report` endpoint reads the `Expenses` business table (`:908-988`); financial statements read the GL (`FinancialReportService.cs`).
- **Attachments** are not an aggregate — only a `ReceiptPath` string column.
- **Approvals** are a property/state inside the Expense aggregate, not a separate aggregate.

---

## 7. Expense Behavior (implementation-backed)

**Expense Creation** (`CreateExpense`, `:183-262`): builds `Expense` with `TenantId = RequiredTenantId` (`:211`), `AccountingMutationVersion = request.RequiresApproval ? 0 : 1` (`:207`), auto-approves when approval not required (`:225-227`). Enqueues `PosExpense` outbox message if `ShouldAccountExpense` (approved & not deleted) (`:234-241`).

**Expense Categories** (`ExpenseCategoriesController.cs`): CRUD + `toggle-status`. Name uniqueness enforced within tenant (`:70-73, :109-112`). Active flag `IsActive` is metadata only — categories still used after deactivation.

**Funding Source / Payment Methods** (`PostPosExpenseAsync`, `CommerceAccountingService.cs:804-850`):
- **Cash** → funding account = resolved cashbox account code (`ResolveExpenseCashboxAccountCodeAsync`, `:1505-1518`; falls back to default cashbox account if `CashboxId` null).
- **Visa / Card** → funding account = `AccountCodes.VisaClearing` (`"1110"`).
- **BankTransfer** → funding account = `AccountCodes.BankAccount` (`"1030"`).
- **Cheque / anything else** → `CreateInvalidPaymentMethodException` thrown (`:809, :849, :1550-1556`). Supported set is exactly `{Cash, Visa, Card, BankTransfer}`.

**Journal construction** (`:817-862`):
- **Debit** `AccountCodes.Expenses` (`"6100"`, general expense account) for `expense.Amount`.
- **Credit** the resolved funding account for `expense.Amount`.
- `SourceType = "Expense"`, `SourceId = expense.Id`, `SourceReference = ReferenceNumber ?? Id`.
- `IdempotencyKey = "EXPENSE:{tenant}:{expenseId}:{operationKey}"` (`:1531-1539`).

**Supplier Expense:** `SupplierId` is optional (`:38`). When present, the supplier Name is denormalized into the DTO (`:115, :161, :509`). **Expense does NOT write the Party Ledger** for the supplier — no `AccountLedger`/`PartyLedgerAdjustment` is created. Supplier linkage is for tracking/reporting only.

**Internal Expense:** any expense without a `SupplierId` is an internal/operating expense; posting is identical (Debit Expenses 6100 / Credit funding).

**Reference Numbers:** free-text `ReferenceNumber` (nullable). Used only in journal description and `SourceReference` (`:858, :830-845`). No uniqueness, no generator.

**Approval:** `RequiresApproval` flag; if false → auto-approved on create. `POST .../approve` flips `IsApproved`, sets `ApprovedBy`/`ApprovedAt`. Rejecting a previously-approved expense enqueues `ExpenseReversal` (`:451-471`).

**Status:** No enum/state-machine. State is captured by two booleans `RequiresApproval` + `IsApproved` + `IsDeleted` (soft-delete) + `AccountingMutationVersion`. There is **no** `ExpenseStatus` enum, no `Pending`/`Posted`/`Reversed` status field.

---

## 8. Financial Behavior

**Journal Creation:** `PostPosExpenseAsync` builds a `PostingRequest` and calls `FinancialPostingService.PostJournalAsync` (`CommerceAccountingService.cs:852-864`). This is the **only** path that creates ledger rows for an expense.

**Debit / Credit:**
- Debit: `Expenses` account (6100), amount = expense amount.
- Credit: funding account (Cashbox account / VisaClearing 1110 / BankAccount 1030), amount = expense amount.
- It is a balanced double-entry of equal magnitude.

**Expense Account (6100):** resolved as `AccountCodes.Expenses` (`:802`). No per-category or per-supplier sub-account — all expenses post to the single general expense account.

**Funding Account:** resolved per payment method (§7). For Cash, the specific cashbox's ledger account code is resolved (`ResolveCashboxAccountAsync`, `:1509-1514`); if no `CashboxId`, the default cashbox account is used (`:1517`).

**Balance Updates:** The funding/cashbox/bank balance is **not** updated by the Expense write path. The GL `LedgerAccounts.CurrentBalance` is updated by `FinancialPostingService` when the journal posts. The controller comment at `:243` is explicit: *"DO NOT mutate balances directly."*

**Ledger Ownership:** `FinancialPostingService` owns ledger rows. Expense owns none.

**Financial Truth:** the General Ledger. `ProcessPosExpenseAsync` is idempotent/replay-safe via `AccountingMutationVersion` and skips stale messages (`:2130-2161`).

**Party Participation:** **None.** Expense does not post to the Party Ledger / `AccountLedgers`. A supplier-linked expense does not create a supplier payable or receivable entry.

**General Ledger Participation:** Full. Every approved, non-deleted expense produces a `SourceType="Expense"` journal. That journal feeds Trial Balance (`:56-117`), Profit & Loss Expense lines (`:216-270`, `AccountType.Expense`), and — because the funding leg hits Asset accounts (Cashbox 101x / Bank 1030 / VisaClearing 1110) — the Balance Sheet (`:277-369`).

---

## 9. Constraints (implementation-backed)

| Rule | Enforced? | Evidence |
|---|---|---|
| Tenant isolation | ✅ | Every write reads `TenantId = _tenantContext.RequiredTenantId`; queries filter on `TenantId`; `IgnoreQueryFilters` calls re-apply tenant predicate (`:291, :363, :429, :552`) |
| Idempotency (header) | ✅ | `Idempotency-Key`/`X-Request-Id` required for mutations; pattern `^[A-Za-z0-9-]+$`, max 100 (`ExpensesController.cs:26-33, 574-616`); stored in `SalesRequestIdempotency` |
| Idempotency (ledger) | ✅ | outbox idempotency key + journal idempotency key `EXPENSE:{tenant}:{id}:{op}`; processor skips stale version (`:2130-2161`) |
| Positive amount | ❌ NOT enforced | No `[Range]` / `> 0` check in DTO (`ExpenseDTOs.cs:49-63`) or controller. Only the GL posting would later surface balance oddities. |
| Existing category | ❌ NOT validated at create | Controller does **not** verify `ExpenseCategoryId` exists; FKs enforced by DB only. DTO marks it `[Required]` shape but no existence check. |
| Existing funding account | ⚠️ Partial | Resolved at posting time (`ResolveExpenseCashboxAccountCodeAsync`); if account code missing, `PostJournalAsync` fails → outbox retry. No pre-check at create. |
| Existing supplier | ❌ NOT validated | `SupplierId` optional; no existence check. |
| Funding availability | ❌ NOT checked | No balance pre-check on cashbox/bank before posting. |
| Valid payment method | ✅ (at posting) | `PostPosExpenseAsync` throws `CreateInvalidPaymentMethodException` for anything other than Cash/Visa/Card/BankTransfer (`:809, :849, :1550-1556`) |
| Approved before post | ✅ | `ShouldAccountExpense` (`:558-559`); processor skips `!IsApproved || IsDeleted` (`:2152-2161`) |
| Category deletion guard | ✅ | Blocked if expenses reference it (`ExpenseCategoriesController.cs:133-138`) |
| Report row cap | ✅ | `MaxExpenseReportItems = 1000`; sets `X-Result-Truncated` header (`:30, :935-947`) |
| Mutation version monotonicity | ✅ | Version bumped on approve/withdraw/update/delete when ledger-affecting (`:315, :378, :453`); processor enforces exact match, skips stale (`:2130-2161, :2188-2197`) |

---

## 10. SQL Transactions

| Aspect | Behavior | Evidence |
|---|---|---|
| Isolation / Atomicity | Each mutation wraps business write + outbox enqueue + idempotency in one `BeginTransactionAsync` (`:193, :274, :346, :412`); committed in `CompleteExpenseIdempotencyAsync` (`:841-858`) |
| Rollback | On exception: `transaction.RollbackAsync` + `_context.ChangeTracker.Clear()` (`:257-258, :329-330, :395-396, :483-484`) |
| Outbox boundary | `EnsureTransactionalEnqueueBoundary` throws if outbox enqueue happens without an active DB transaction (`:867-889`). Enqueue saves immediately inside the caller's transaction (`:749-752`) |
| Ledger posting | Runs in the **background processor** under its own execution strategy + transaction (`ProcessExpenseReversalAsync` `:2215-2258`); separate from the API transaction |
| Concurrency | `AccountingMutationVersion` provides optimistic replay-safety; stale messages are skipped, not re-applied (`:2130-2161, :2188-2197`) |
| Retries | Outbox has retry/attempt-count fields; `ProcessExpenseReversalAsync` wraps reversal loop + repost in a single transaction to avoid partial unwind (`:2211-2258`) |
| Expense persistence | `_context.Expenses.Add` + `SaveChangesAsync` inside the API transaction (`:232, :245`) |

---

## 11. Events

| Event | Type / Outbox | Consumer | Evidence |
|---|---|---|---|
| Expense posted | `PosExpense` | `ProcessPosExpenseAsync` → `PostPosExpenseAsync` | `AccountingOutboxService.cs:478-501`; dispatch `:727`; handler `:2110` |
| Expense mutation/reversal | `ExpenseReversal` | `ProcessExpenseReversalAsync` (reverse old journals + optionally repost) | `AccountingOutboxService.cs:503-526`; dispatch `:728`; handler `:2166` |
| Idempotency key | `EXPENSE:{tenant}:{expenseId}:{operationKey}` (post); `EXPENSE_REVERSAL:{tenant}:{expenseId}:{operationKey}` (reverse) | — | `:512, :581-584` |
| Outbox table | `AccountingOutboxMessages` (tenant-scoped, idempotency-key unique) | `AccountingOutboxProcessor` (background) | `EnqueueMessageAsync` `:683-759` |
| Audit trail | `CommerceAccountingAudit` for `Expense`/`ExpenseMutation` | `CreateAuditTrailAsync` `:2612-2640` |
| Metrics | `PosExpense`/`ExpenseReversal` are **not** wired to metrics (no-op case) | `RecordSuccessMetric` `:2664-2668` | **Implemented but no metric emitted** |
| Synchronous consumers | None beyond GL posting | — | |

**Synchronization strategy:** Transactional Outbox (PHASE 3.5, `AccountingOutboxService.cs:15-22`). Business mutation and outbox row are atomic in the API transaction; the background processor guarantees delivery to the GL.

---

## 12. Runtime Architecture (implementation-backed)

```
Expense Request (POST /api/expenses, Idempotency-Key required)
   ↓  [ExpensesController.CreateExpense, BeginTransaction]
Validation (idempotency key + request hash; tenant = RequiredTenantId)
   ↓
Expense Record persisted (_context.Expenses.Add + SaveChangesAsync)   [ExpensesController.cs:232-245]
   ↓  if ShouldAccountExpense (IsApproved && !IsDeleted)
IAccountingOutboxService.EnqueuePosExpenseAsync
   → AccountingOutboxMessages row (EXPENSE:{tenant}:{id}:vN)            [AccountingOutboxService.cs:478-501]
   ↓  [transaction Commit in CompleteExpenseIdempotencyAsync]
────────────── background boundary ──────────────
AccountingOutboxProcessor  (EventType = "PosExpense")                  [AccountingOutboxProcessor.cs:727]
   ↓
ProcessPosExpenseAsync (re-load Expense, version check, skip if !approved/deleted) [cs:2110-2164]
   ↓
CommerceAccountingService.PostPosExpenseAsync                         [CommerceAccountingService.cs:772-877]
   - Debit  Expenses (6100)   {amount}
   - Credit funding account (Cashbox/VisaClearing 1110/BankAccount 1030) {amount}
   ↓
FinancialPostingService.PostJournalAsync  → JournalEntry (SourceType="Expense") [cs:864]
   ↓
General Ledger (LedgerEntries + LedgerAccounts.CurrentBalance)
   ↓
Financial Reports (ledger-only):
   - Trial Balance        [FinancialReportService.cs:41]
   - Profit & Loss (Expense lines)   [cs:193]
   - Balance Sheet (Asset funding leg) [cs:277]

Read Models (NOT financial truth):
   - Expense Report  → ExpensesController.GetReport (reads Expenses table) [cs:905]
   - Expense Categories → ExpenseCategoriesController
```

**Update / Delete / Reject path:** bumps `AccountingMutationVersion`, enqueues `ExpenseReversal` (`EXPENSE_REVERSAL:...`). Processor `ProcessExpenseReversalAsync` reverses prior `Posted` `SourceType="Expense"` journals (`:2199-2210`) and, if still active & approved, reposts a fresh journal under the new operation key (`:2233-2242`). All inside one execution-strategy transaction (`:2215-2258`).

---

## 13. Browser Test Specification

**Scenarios**
1. Create Cash expense (category + amount + Cash + cashbox) → 201; expense appears in list; after outbox drain, a `SourceType="Expense"` journal exists debiting 6100 and crediting the cashbox account.
2. Create Visa/Card expense → journal credits VisaClearing (1110).
3. Create BankTransfer expense → journal credits BankAccount (1030).
4. Create with `RequiresApproval=true` → `IsApproved=false`; approval required before any journal posts.
5. Approve (`POST /api/expenses/{id}/approve` `{approved:true}`) → journal posts.
6. Reject an approved expense (`{approved:false}`) → prior journal reversed; net zero GL impact.
7. Update expense amount → mutation version bumps; old journal reversed, new reposted.
8. Delete expense (soft) → `IsDeleted=true`; journal reversed.
9. Expense Categories CRUD + toggle-status; duplicate name rejected; delete blocked while expenses exist.
10. Report endpoint with filters; confirm `X-Result-Truncated` when > 1000 rows.

**Assertions**
- `TenantId` on every created row equals the authenticated tenant; cross-tenant read returns 404/empty.
- GL stays balanced: Debit(6100) == Credit(funding) per expense.
- Profit & Loss Expense total == sum of approved, non-deleted expenses posted in range (derived from GL, not `Expenses` table).
- `AccountingMutationVersion` increments exactly once per ledger-affecting mutation.

**Negative Cases**
- Missing/!mismatched `Idempotency-Key`/`X-Request-Id` → 400 (`ExpensesController.cs:583-596`).
- Replay same idempotency key + same payload → returns stored response (no duplicate).
- Replay same key + **different** payload → 400 "Idempotency key reuse with different payload" (`:680-686`).
- Payment method `Cheque`/invalid → posting throws `CreateInvalidPaymentMethodException`; outbox marks failed/retries.
- Duplicate category name → 400 (`:70-73`).
- Delete category with expenses → 400 (`:133-138`).
- Create expense with **negative/zero amount** → **currently accepted** (no server guard); test should flag this as a control gap (§9).

**Journal Validation**
- Exactly one journal per approved expense; `SourceType="Expense"`, `SourceId=expense.Id`.
- No journal for unapproved or soft-deleted expenses (processor skips, `:2152-2161`).
- Reversal uses `ReverseJournalAsync` and preserves operation-key lineage (`:2199-2210, :2225`).

**Expense Validation**
- `ExpenseCategoryId`, `ExpenseDate`, `Amount`, `Description`, `PaymentMethod` required (`Expense.cs` `[Required]`).
- `PaymentMethod` default `"Cash"` when omitted (`:36`).

**Funding Validation**
- Cash with `CashboxId` → that cashbox's ledger account is credited; null `CashboxId` → default cashbox account (`:1507-1517`).
- No funding-availability check — overdraft is permitted at the API layer.

**Report Validation**
- Totals equal filtered `Expenses` sums (`:960-986`); `ExpensesByCategory` / `ExpensesByPaymentMethod` dictionaries present.

---

## 14. Domain Ownership Matrix

| Concept | Owning component | Store | Notes |
|---|---|---|---|
| Expense record | `ExpensesController` | `Expenses` | soft-delete; mutation version |
| Expense Category | `ExpenseCategoriesController` | `ExpenseCategories` | independent tenant aggregate |
| Expense Report | `ExpensesController.GetReport` | reads `Expenses` | read model only |
| Funding (Cashbox/Bank) | Treasury domain | `Cashboxes`/`Banks` | Expense holds FK only |
| Funding Account (GL) | `CommerceAccountingService` (resolve) + `FinancialPostingService` | `LedgerAccounts` | 101x/1030/1110 |
| Expense GL Journal | `FinancialPostingService` | `JournalEntries`/`LedgerEntries` | `SourceType="Expense"` |
| Party Ledger (Supplier) | **Not owned by Expense** | `AccountLedgers` | Expense does NOT post here |
| Financial Posting | `CommerceAccountingService` + `FinancialPostingService` | — | double-entry |
| Outbox | `AccountingOutboxService` + `AccountingOutboxProcessor` | `AccountingOutboxMessages` | transactional outbox |
| Financial Statements | `FinancialReportService` | reads GL | ledger-only |

---

## 15. Canonical Pattern

The Expense domain follows the **Shopixy Transactional-Outbox / Ledger-Projection** pattern (same as Sales, Purchases, Payments):
1. Controller validates idempotency + tenant, persists the business record inside one DB transaction, and enqueues an outbox message — **never touching balances**.
2. A background processor reloads the aggregate, performs a `AccountingMutationVersion` replay-safety check, and (if approved & not deleted) calls `CommerceAccountingService` to build a double-entry `PostingRequest`.
3. `FinancialPostingService` is the sole writer of `JournalEntries`/`LedgerEntries`. The GL is the financial truth; the `Expenses` table is operational.
4. Reports over `Expenses` are operational read models; Profit & Loss / Trial Balance / Balance Sheet are computed ledger-only by `FinancialReportService`.

Expense is an **aggregate** (its own versioned entity), but it is **not** a financial authority. Funding accounts, party ledger, and GL are all external aggregates it coordinates through the outbox.

---

## 16. Documentation Validation (correcting misconceptions)

- ❌ "When expense payment method is Cash, deduct from selected cashbox / create CashboxTransaction record."
  ✅ The Expense write path does **not** mutate cashbox balances or create `CashboxTransaction`. Posting only creates a GL journal (Debit 6100 / Credit cashbox account). (`ExpensesController.cs:243`; `PostPosExpenseAsync:804-864`; no `CashboxService` call in controller.)
- ❌ "System updates cashbox/bank balance" on create.
  ✅ Balances are updated only by `FinancialPostingService` when the journal posts to the GL. (`CommerceAccountingService.cs:864`.)
- ❌ "System creates financial ledger entry (AccountLedger)" synchronously on create.
  ✅ The ledger entry is created **asynchronously** by the outbox processor, and only for approved, non-deleted expenses. (`EnqueuePosExpenseAsync:236`; `ProcessPosExpenseAsync:2152-2163`.)
- ❌ "Expense owns balances."
  ✅ General Ledger / `LedgerAccounts.CurrentBalance` owns balances.
- ❌ "Expense Report owns financial truth."
  ✅ The report reads the `Expenses` business table; financial truth is the GL (`FinancialReportService`).
- ❌ "Expense directly updates ledger."
  ✅ Expense posts through `FinancialPostingService` via the outbox (`CommerceAccountingService.PostPosExpenseAsync` → `PostJournalAsync`).
- ❌ "Expense Category owns accounting."
  ✅ Category is metadata (name/description/active); all expenses post to the single `Expenses` (6100) account regardless of category.
- ❌ "Expense requires a Supplier / Supplier creates a payable."
  ✅ `SupplierId` is optional and supplier-linked expenses do **not** post to the Party Ledger. No payable/receivable entry is created.
- ❌ "Expenses participate in the Party Ledger."
  ✅ They do not. GL-only (Expenses 6100 vs funding asset).
- ❌ "There is an Expense status enum / number generator."
  ✅ State is two booleans + soft-delete + mutation version; `ReferenceNumber` is free-text, no system number.

---

## 17. Final Architecture Conclusion

- **Expense Authority:** `ExpensesController` is the write authority for the `Expenses` operational record. There is no `ExpenseService` aggregate root.
- **Posting Authority:** `CommerceAccountingService.PostPosExpenseAsync` builds the posting; `FinancialPostingService` is the sole ledger writer. Driven by the transactional outbox.
- **Financial Truth:** The General Ledger (`JournalEntries` + `LedgerEntries` + `LedgerAccounts.CurrentBalance`). The `Expenses` table is operational, not financial.
- **Runtime:** API transaction (persist + outbox enqueue + idempotency) → background `AccountingOutboxProcessor` → `PostPosExpenseAsync` → `PostJournalAsync` → GL → ledger-only financial reports.
- **Aggregate Ownership:** Expense = its own versioned aggregate; Category = separate tenant aggregate; Funding/Treasury = external; GL/Journal = external (`FinancialPostingService`); Party Ledger = **not** involved.
- **Transaction Model:** One DB transaction per API mutation (business + outbox + idempotency atomic); separate transaction in the processor for reversal/repost. Outbox guarantees at-least-once delivery; idempotency + `AccountingMutationVersion` guarantee exactly-once ledger effect.
- **Projection Strategy:** Operational read models (`ExpensesController.GetReport`) read the business table; financial statements (`FinancialReportService`) read the GL only.
- **Integrity Strategy:** `AccountingMutationVersion` replay-safety (stale messages skipped), outbox idempotency keys, journal idempotency keys, tenant predicate re-applied after `IgnoreQueryFilters`, and explicit "do not mutate balances directly" guard.
- **Audit Strategy:** `CommerceAccountingAudit` row written for `Expense`/`ExpenseMutation` (`:2612-2640`); `CreatedAt`/`UpdatedAt` on the record; soft-delete via `IsDeleted`.

### Important Investigation Goals — Direct Answers
- Is Expense an Aggregate? **Yes** (versioned entity, `AccountingMutationVersion`).
- Does Expense generate JournalEntry? **Yes**, asynchronously via outbox → `PostPosExpenseAsync` → `PostJournalAsync` (`SourceType="Expense"`).
- Does Expense use AccountingOutbox? **Yes** (`EnqueuePosExpenseAsync` / `EnqueueExpenseReversalAsync`).
- Does Expense use FinancialPostingService? **Yes** (through `CommerceAccountingService`).
- Does Expense update Party Ledger? **No.**
- Does Expense require Supplier? **No** (`SupplierId` optional).
- Does Expense require Expense Category? **FK is Required** (`ExpenseCategoryId` non-null), but no existence check at API layer.
- Does Expense support Cash? **Yes** (resolves cashbox/default cashbox account).
- Does Expense support Bank? **Yes** (BankTransfer → BankAccount 1030).
- Does Expense support Visa? **Yes** (Visa/Card → VisaClearing 1110).
- Does Expense support Attachments? **Partial** — `ReceiptPath` string only; no upload endpoint.
- Does Expense support Approval? **Yes** (`RequiresApproval`/`IsApproved` + approve endpoint).
- Does Expense support Status? **Partial** — two booleans + soft-delete + mutation version; no status enum.
- Can Expense be updated? **Yes** (`PUT`).
- Can Expense be deleted? **Yes** (soft delete).
- Can Expense be reversed? **Yes** (via `ExpenseReversal` outbox mutation; no direct endpoint).
- Can Expense be voided? **No.**
- Is Expense immutable after posting? **No** — it can be updated/deleted/rejected, each triggering a reversal+repost through the outbox.
- Is Expense idempotent? **Yes** (idempotency-key header + request hash + ledger idempotency key + mutation version).
- Does Expense participate in Profit & Loss? **Yes** (Expenses 6100 → P&L Expense lines).
- Does Expense participate in Trial Balance? **Yes** (GL).
- Does Expense participate in Balance Sheet? **Yes** (funding leg hits Asset accounts).
