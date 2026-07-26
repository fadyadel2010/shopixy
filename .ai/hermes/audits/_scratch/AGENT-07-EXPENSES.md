# AGENT-07-EXPENSES — FLOW-ACC-07 Expenses Financial Audit

**Auditor:** Independent Expenses Domain Auditor (subagent)
**Methodology:** AUDIT-METHODOLOGY.md (Implementation = source of truth)
**Canonical doc:** FLOW-ACC-07-EXPENSES.md
**Audit date:** 2026-07-20

## ⭐ CENTRAL QUESTION — DOES EXPENSE POSTING BYPASS FinancialPostingService / Outbox?

**ANSWER: NO BYPASS. The expense posting path is fully outbox-driven and goes through FinancialPostingService.**

Verified runtime path (implementation, not doc):
```
ExpensesController.CreateExpense/Update/Delete/Approve
  → _accountingOutbox.EnqueuePosExpenseAsync / EnqueueExpenseReversalAsync
      (AccountingOutboxService.cs:478, :503)
  → AccountingOutboxMessages row (idempotency key EXPENSE:{tenant}:{id}:{op})
  → [BACKGROUND] AccountingOutboxProcessor.DispatchMessageAsync ("PosExpense"/"ExpenseReversal")
      (AccountingOutboxProcessor.cs:727-728)
  → ProcessPosExpenseAsync / ProcessExpenseReversalAsync (:2110, :2166)
  → CommerceAccountingService.PostPosExpenseAsync (:772)
  → FinancialPostingService.PostJournalAsync (:864 → FinancialPostingService.cs:39)
  → JournalEntries + LedgerEntries + LedgerAccounts.CurrentBalance (General Ledger)
```

**No** `JournalEntries.Add`, `LedgerEntries.Add`, or `AccountLedgers.Add` exists anywhere in
`ExpensesController.cs` (searched entire file) or in the controller→outbox path. The controller
explicitly comments `// DO NOT mutate balances directly` (ExpensesController.cs:243). The only ledger
writer is `FinancialPostingService`. Confirmed: search for `RecordExpenseAsync|_context.JournalEntries.Add|
_context.LedgerEntries.Add|.AccountLedgers.Add` across the whole repo returned 0 hits.

---

## FINDINGS

### F-07-01 — Negative / zero expense amount accepted (no validation) → wrong-sign GL entry
- **Severity:** HIGH
- **Priority:** P1
- **Confidence:** High
- **Evidence:**
  - `CreateExpenseDto.Amount` / `UpdateExpenseDto.Amount` are plain `decimal` with NO `[Range]`/`[Required]`-on-value (ExpenseDTOs.cs:52, :68).
  - `ExpensesController.CreateExpense` assigns `Amount = request.Amount` with no sign check (:214); `UpdateExpense` same (:301).
  - `CommerceAccountingService.PostPosExpenseAsync` posts whatever `expense.Amount` is: `PostingLine.Debit(acct, expense.Amount)` (:820) and `PostingLine.Credit(acct, expense.Amount)` (:827-845).
  - `FinancialPostingService` only rejects if a `PreventNegative` account would go negative; `Expenses (6100)` is not flagged PreventNegative, so a negative amount posts as a balanced (negative-sum) journal.
- **Implementation proof:** A `Amount = -100` Cash expense posts Debit Expenses(6100)=-100 / Credit Cashbox=-100. A negative debit credits the expense account (understating expense / overstating profit) and effectively records the cash movement as income/refund — inverting the economic event.
- **Business impact:** Finance sees expenses as income; bogus refunds inflate cash.
- **Financial/Accounting impact:** P&L Expense lines corrupted; GL unbalanced in economic (not arithmetic) sense.
- **Architecture impact:** Violates "Every financial mutation must preserve double-entry accounting" intent (sign convention).
- **Root cause:** Missing positive-amount validation at DTO and controller layers.
- **Recommended solution:** Add `[Range(0.01, double.MaxValue)]` to `Amount` in `CreateExpenseDto`/`UpdateExpenseDto` and a guard in the controller; reject zero/negative before persist.
- **Regression risk:** Low (pure additive validation).
- **Validation matrix:** Double Entry=PARTIAL (balanced but wrong sign), Accounting=FAIL, Auditability=OK, SourceOfTruth=OK, TenantIsolation=OK, Idempotency=OK, FinancialIntegrity=FAIL.

### F-07-02 — No category / supplier existence or funding-availability check at create
- **Severity:** MEDIUM
- **Priority:** P2
- **Confidence:** High
- **Evidence:**
  - Controller never queries `ExpenseCategory` existence for `ExpenseCategoryId` (Create:208-230). FK only enforced by DB.
  - `SupplierId` optional, never existence-checked (:218).
  - No cashbox/bank balance pre-check. Funding resolved lazily at posting (`ResolveExpenseCashboxAccountCodeAsync`, CommerceAccountingService.cs:1505-1518); if account-code missing, `PostJournalAsync` throws → outbox retry loop.
- **Implementation proof:** Create accepts a non-existent `ExpenseCategoryId` (DB FK violation surfaces as 500, not clean 400) or an orphaned supplier id; overdraft permitted at API layer.
- **Business impact:** Poor UX / unhandled FK 500s; possible posting retries on missing account.
- **Root cause:** Validation scope limited to idempotency + tenant only.
- **Recommended solution:** Validate category existence (return 400); validate supplier if supplied; warn (not block) on insufficient funding.
- **Regression risk:** Low.
- **Matrix:** Accounting=PARTIAL, Auditability=OK, SourceOfTruth=OK, TenantIsolation=OK.

### F-07-03 — Read endpoints rely solely on global query filter for tenant isolation
- **Severity:** MEDIUM
- **Priority:** P2
- **Confidence:** High
- **Evidence:**
  - `GetExpenses` (:76) and `GetReport` (:908) query `_context.Expenses` with NO explicit `TenantId` predicate; they rely on the EF Core global `HasQueryFilter` for `ITenantOwned`.
  - Write paths DO explicitly re-apply `TenantId == _tenantContext.RequiredTenantId` after `IgnoreQueryFilters()` (:291, :363, :429).
  - `GetReport` only touches `RequiredTenantId` in the truncation-warning log, not in the query.
- **Implementation proof:** If the tenant global filter is ever disabled/changed, expense reads would cross tenants; writes are protected but reads are not.
- **Business impact:** Latent cross-tenant read leakage if model filter regresses.
- **Root cause:** Inconsistent isolation strategy (explicit on writes, implicit on reads).
- **Recommended solution:** Add explicit `.Where(e => e.TenantId == _tenantContext.RequiredTenantId)` to read queries for defense-in-depth.
- **Regression risk:** Low.
- **Matrix:** TenantIsolation=PARTIAL (reads), SourceOfTruth=OK.

### F-07-04 — Canonical doc / treasury cert claim of "COMMENTED legacy writes" is STALE
- **Severity:** LOW
- **Priority:** P3
- **Confidence:** High
- **Evidence:**
  - Canonical doc §4.1 references `TREASURY_AUDIT_CERTIFICATION.md` which marks `ExpensesController` legacy writes as "✅ COMMENTED" (TREASURY_AUDIT_CERTIFICATION.md:100).
  - Actual `ExpensesController.cs` contains NO commented-out legacy writes and NO `JournalEntries`/`LedgerEntries`/`AccountLedgers`/`CashboxTransaction` references (searched). It is already fully migrated to the outbox.
- **Implementation proof:** The migration is complete; the "commented legacy" label no longer describes reality.
- **Business impact:** Misleading audit trail for future maintainers.
- **Root cause:** Doc not updated after cleanup.
- **Recommended solution:** Update canonical doc §4.1 / treasury cert to "✅ MIGRATED (no legacy code present)".
- **Regression risk:** None.
- **Matrix:** All OK.

### F-07-05 — Expense Report totals can diverge from GL/P&L (temporal, by-design)
- **Severity:** MEDIUM
- **Priority:** P2
- **Confidence:** High
- **Evidence:**
  - `GetReport` (:905-989) sums the operational `Expenses` business table (approved/pending/soft-deleted filtered only by flags), NOT the GL.
  - An approved expense is enqueued to the outbox but posts asynchronously; until the processor drains, the expense is in the report totals but NOT yet in P&L/Trial Balance.
  - Conversely, a soft-deleted expense with a pending reversal still shows in `Expenses` until `IsDeleted` filter + reversal complete.
- **Implementation proof:** `totalExpenses`/`approvedExpenses` (GetReport:960-966) read `Expenses.Amount`; P&L reads `LedgerEntries`. The two can disagree during outbox lag.
- **Business impact:** Reconciliation confusion between operational report and financial statements during processing windows.
- **Root cause:** Operational projection vs ledger projection are eventually consistent, not synchronous.
- **Recommended solution:** Document the lag; optionally exclude not-yet-GL-posted expenses, or add an outbox-drained indicator.
- **Regression risk:** Low.
- **Matrix:** SourceOfTruth=PARTIAL, EventualConsistency=EXPECTED.

### F-07-06 — No direct expense reversal / void endpoint (only via outbox mutation)
- **Severity:** LOW (ARCHITECTURAL IMPROVEMENT)
- **Priority:** P3
- **Confidence:** High
- **Evidence:** No `reverse`/`void` route in `ExpensesController`. Reversal is achieved by update/delete/approve-withdraw enqueuing `ExpenseReversal` (controller :316, :379, :466; processor :2166).
- **Implementation proof:** Works correctly (reverse + repost under one transaction, processor :2215-2258) but there is no explicit operator "void" action.
- **Recommended solution:** Optionally add an explicit `POST /api/expenses/{id}/void` for operator clarity; current design is acceptable.
- **Matrix:** All OK.

### F-07-07 — No system-generated expense number; ReferenceNumber is free-text, non-unique
- **Severity:** LOW
- **Priority:** P3
- **Confidence:** High
- **Evidence:** `Expense.cs:56-57` `ReferenceNumber` nullable free text; `CreateExpenseDto.ReferenceNumber` (:59) no generator; used only in journal description/`SourceReference` (CommerceAccountingService.cs:858).
- **Implementation proof:** Duplicate/empty ReferenceNumbers allowed; no audit-friendly sequential expense number.
- **Recommended solution:** Generate `EXP-YYYYMMDD-NNNN` server-side if empty; enforce optional uniqueness per tenant.
- **Matrix:** All OK.

---

## STRENGTHS
1. **Clean outbox path, no bypass** — Controller never writes ledger rows directly; sole writer is `FinancialPostingService` (verified by search).
2. **Double-entry preserved** — `PostPosExpenseAsync` builds balanced Debit(Expenses 6100)/Credit(funding) lines; `FinancialPostingService.PostJournalAsync` enforces balance under Serializable isolation.
3. **Multi-layer idempotency** — (a) `Idempotency-Key`/`X-Request-Id` header + request-hash + `SalesRequestIdempotency` 3-retry claim loop; (b) outbox idempotency key; (c) journal idempotency key `EXPENSE:{tenant}:{id}:{op}`; (d) `AccountingMutationVersion` replay-safety with stale-message SKIP (processor :2130-2161, :2188-2197).
4. **Immutable journals** — Historical journals never mutated; reversals create new `ReverseJournalAsync` journals (processor :2225).
5. **Atomic reversal+repost** — `ProcessExpenseReversalAsync` wraps the reversal loop and repost in one execution-strategy transaction (:2215-2258), preventing partial unwind.
6. **Strong tenant isolation on writes** — `RequiredTenantId` set on every write; `IgnoreQueryFilters` paths re-apply tenant predicate (:291, :363, :429, :552).
7. **Approval gating** — `ShouldAccountExpense` (approved && !deleted) plus processor skip guard (:2152-2161) prevents unapproved/deleted expenses from hitting the GL.
8. **Valid payment-method enforcement at posting** — `CreateInvalidPaymentMethodException` for anything outside {Cash, Visa, Card, BankTransfer} (CommerceAccountingService.cs:809, :849, :1550-1556).
9. **Category deletion guard** — blocked while expenses reference it (ExpenseCategoriesController.cs:133-138).
10. **Report row cap** — `MaxExpenseReportItems = 1000` + `X-Result-Truncated` header (:30, :935-947).

---

## DEEP-INVESTIGATION ANSWERS (methodology § mandatory)
- **Can historical journals be modified?** NO — journals are immutable; reversals create new reversal journals (ReverseJournalAsync).
- **Can ledger truth diverge from GL?** NO — GL *is* the ledger; `FinancialPostingService` is sole writer.
- **Can AccountLedger diverge from reality?** N/A — Expense does NOT post to AccountLedger/Party Ledger (by design; only GL funding-asset leg).
- **Can Reports become inconsistent?** YES (temporally) — operational `Expenses` report vs GL/P&L during outbox lag (F-07-05).
- **Can Daily Closing produce accounting inconsistencies?** Not from expenses specifically; expense journals are balanced.
- **Can Adjustments bypass FinancialPostingService?** N/A for expenses; expense mutations go through `CommerceAccountingService` → `PostJournalAsync`.
- **Can Expenses bypass Outbox?** **NO** — verified; the only ledger path is the transactional outbox.
- **Can Payments create inconsistent balances?** Out of scope (FLOW-ACC payments); expense funding leg is balanced.
- **Can Statement Of Account disagree with General Ledger?** Expense does not participate in Party Ledger, so no SOA impact.
- **Can asynchronous posting lose financial integrity?** NO — exactly-once ledger effect guaranteed by idempotency keys + `AccountingMutationVersion`; outbox = at-least-once delivery.
- **Can idempotency fail?** Low risk — 4 layers; replay with different payload → 400 "Idempotency key reuse with different payload" (:680-686).
- **Can replay corrupt accounting?** NO — stale messages skipped via version check (:2130-2161, :2188-2197).
- **Can race conditions create duplicate journals?** NO — `FinancialPostingService` in-transaction idempotency re-check + unique constraint + Serializable isolation.
- **Can balances become negative unexpectedly?** Only if a `PreventNegative` account (e.g. cashbox) is over-credited; expense debit is into Expenses(6100) which is not PreventNegative.
- **Can orphan records exist?** Outbox messages can remain if processor repeatedly fails, but they are recovery evidence (doc §9.2).
- **Can audit records become inconsistent?** `CommerceAccountingAudit` written for Expense/ExpenseMutation (canonical doc §11 cites `CreateAuditTrailAsync:2612-2640`) — claimed; not independently re-read in this pass but consistent with pattern. `CreatedAt`/`UpdatedAt`/`IsDeleted` present.

---

## MISSING IMPLEMENTATION (per canonical doc, confirmed)
- Expense receipt upload endpoint — NOT IMPLEMENTED (ReceiptPath string only).
- Direct expense reversal/void endpoint — NOT IMPLEMENTED (handled via outbox mutation).
- Expense archive/restore — NOT IMPLEMENTED.
- Separate attachment model — NOT IMPLEMENTED.
- Dedicated expense analytics — PARTIAL (report only).
- System-generated expense number — NOT IMPLEMENTED.
- Per-category / per-supplier sub-accounts — NOT IMPLEMENTED (all post to Expenses 6100).
- Party Ledger / payable for supplier-linked expenses — NOT IMPLEMENTED by design.
- Positive-amount validation — NOT IMPLEMENTED (F-07-01, real control gap).

---

## VALIDATION MATRIX (summary)
| Principle | Status |
|---|---|
| Double Entry | OK (balanced) — sign convention gap F-07-01 |
| CQRS | OK (write via outbox, read from business table / GL) |
| DDD | OK (Expense aggregate + Category separate aggregate) |
| ERP | OK |
| Accounting | OK (GL source of truth) — F-07-01 sign risk |
| Auditability | OK (audit trail + soft delete + version) |
| Source Of Truth | OK (GL) |
| Tenant Isolation | OK on writes; reads rely on global filter (F-07-03) |
| Idempotency | STRONG (4 layers) |
| Financial Integrity | OK — F-07-01 sign risk |
| Eventual Consistency | EXPECTED (outbox) |
| Aggregate Boundary | OK |

---

## SEVERITY COUNTS
- CRITICAL: 0
- HIGH: 1 (F-07-01)
- MEDIUM: 3 (F-07-02, F-07-03, F-07-05)
- LOW: 3 (F-07-04, F-07-06, F-07-07)

## DOES IMPLEMENTATION MATCH THE CANONICAL DOC?
**MOSTLY YES.** The canonical doc's central claims — outbox-driven posting, no direct ledger writes, double-entry via `FinancialPostingService`, strong idempotency, immutable journals, tenant isolation on writes, approval gating — are all **accurate and verified in code**. The doc's own "NOT IMPLEMENTED" gaps (positive-amount validation, category/supplier existence checks, funding availability, no reversal endpoint, no expense number) were independently confirmed. One doc inaccuracy found (F-07-04): the "COMMENTED legacy writes" claim is stale — the controller is already fully migrated with no legacy code. No bypass of `FinancialPostingService`/`Outbox` exists (the central risk is ABSENT).
