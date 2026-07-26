# SHOPIXY ERP - MASTER FINANCIAL ARCHITECTURE AUDIT

**Peer-reviewed Accounting Architecture Audit - Payments, Expenses, Financial Reports, Statement Of Account, Daily Closing, Account Adjustments**
**Audience:** ERP Chief Architect, Financial Systems Architect, CTO, External Accounting Auditor, Technical Due-Diligence Team
**Date:** 2026-07-20
**Peer Review Date:** 2026-07-20
**Method:** Implementation is the source of truth. Canonical documentation and the prior audit were treated as hypotheses and corrected where source code contradicted them.
**Scope:** FLOW-ACC-06 (Payments), FLOW-ACC-07 (Expenses), FLOW-ACC-08 (Financial Reports), FLOW-ACC-09 (Statement Of Account), FLOW-ACC-10 (Daily Closing), FLOW-ACC-11 (Account Adjustments).

---

## 1. EXECUTIVE SUMMARY

The Shopixy ERP accounting core remains **architecturally strong and materially disciplined**. The peer review verified a single protected double-entry posting engine, immutable journal behavior, durable accounting outbox semantics, strong tenant-write isolation, and clean read-side reporting surfaces.

The peer review also found that the original audit overstated several payment and expense conclusions. The most important correction is that the original CRITICAL finding for collected-order cancellation is **incorrect** in the current implementation: collected/recognized payment cancellation is blocked or collapsed, and refunds have their own accounting path. Order refunds are not missing accounting; `RecordRefundAsync` enqueues refund accounting and `CommerceAccountingService.PostEcommerceRefundAsync` posts a refund journal.

No implementation-backed CRITICAL finding remains after peer review. The remaining material risks are governance and reconciliation gaps: manual corrections lack role/policy authorization, supplier payments have no explicit reversal/void workflow, expenses have no `FinancialIntegrityService` link scan, and async-posted flows are not uniformly surfaced as GL-converged to operators.

**Original audit count inconsistency:** the original report stated both 62 total findings and 66 reviewed findings. This peer review treated all identifiable 66 domain/cross-flow items as reviewed.

**Peer-reviewed classifications:** 53 VERIFIED, 11 PARTIALLY VERIFIED, 2 INCORRECT, 0 UNPROVEN, 0 OUT OF SCOPE.
**Active severity after peer review:** 0 CRITICAL, 3 HIGH, 27 MEDIUM, 15 LOW, 13 ARCHITECTURAL IMPROVEMENT, with duplicate/corrected items folded into their parent cross-flow findings.

**Headline verdict:** Approve for Small/Medium Business only with remediation of HIGH governance items and MEDIUM reconciliation/convergence items before financial-audit reliance. Not yet suitable for external audit reliance until expense reconciliation, supplier-payment reversal/void policy, role authorization, and period-lock ownership are closed.

---

## 2. OVERALL FINANCIAL HEALTH SCORE

**Peer-reviewed score: 78 / 100**

| Dimension | Weight | Score | Rationale |
|---|---:|---:|---|
| Double-entry correctness | 20 | 19 | `FinancialPostingService` owns production journal/ledger creation; posting lines reject non-positive amounts; journals are immutable and reversal-only. |
| Source-of-truth integrity | 20 | 15 | Strong GL foundation; expense and supplier-payment reconciliation coverage remains incomplete. |
| Tenant isolation | 10 | 9 | Strong write isolation and global filters; several read surfaces still rely on filters without explicit tenant predicates. |
| Idempotency | 10 | 10 | HTTP idempotency, outbox unique key, journal idempotency key, mutation versions, and posting double checks are verified. |
| Auditability | 10 | 7 | Strong journal trail; correction authorization, read audit, closure-audit durability, and write-time journal links need hardening. |
| CQRS / DDD boundaries | 10 | 8 | Financial Reports/SOA/Daily Closing are read-side projections; supplier aux aggregates still use a mirror. |
| Async pipeline reliability | 10 | 6 | Outbox is durable and critical events are retryable; convergence/reconciliation signals are uneven. |
| Control and governance | 10 | 4 | Role authorization and period-lock ownership remain the largest governance gaps. |

**Interpretation:** The architecture is not suffering systemic double-entry failure. Its main weaknesses are governance, uniform reconciliation, and operator visibility into async GL convergence.

---

## 3. ARCHITECTURE DIAGRAM

```
                          WRITE SIDE
   Controllers / lifecycle services
      SalesController | SuppliersController | ExpensesController
      StatementOfAccountController | CashboxController | PaymentLifecycleService
                |                         |
                | party ledger sync        | GL async/sync depending flow
                v                         v
        LedgerService              AccountingOutboxService
        AccountLedger              AccountingOutboxMessage
        Customer/Supplier cache            |
                                          v
                                  AccountingOutboxProcessor
                                  CommerceAccountingService
                                  FinancialPostingService
                                          |
                                          v
                                  GENERAL LEDGER
                                  JournalEntry + LedgerEntry

                          READ SIDE
   StatementOfAccountController -> AccountLedger
   FinancialReportsController -> LedgerEntry / Account
   ReportsController daily close -> LedgerEntry
   Cashbox close -> ledger-derived expected cash + non-atomic audit
```

**Verified ownership facts:**
- `FinancialPostingService` is the protected production journal creator; `ApplicationDbContext` blocks unauthorized journal/ledger mutation outside `LedgerMutationScope`.
- Journals are immutable; reversals create new journals through `ReverseJournalAsync`.
- `AccountingOutbox` is durable; critical financial event types include POS, expenses, order payments, refunds, and settlements.
- `AccountLedger.RunningBalance` is party ledger authority; `Customer/Supplier.Balance` is a cache; `SupplierTransactions.BalanceAfter` remains a supplier auxiliary mirror.
- SOA, Financial Reports, and Daily Closing write no GL state.
- Cashbox entity balance fields are obsolete shadows; expected close cash is ledger-derived.

---

## 4. CRITICAL FINDINGS

No CRITICAL finding survived implementation-backed peer review.

### C-01 / F-06-02 - Collected-order cancellation posts no reversal journal
- **Classification:** INCORRECT
- **Previous severity:** CRITICAL
- **New severity:** Removed as defect
- **Evidence:** `PaymentLifecycleService.HandleFailureAsync` collapses stale provider failures and returns without mutation for non-pending/successful terminal orders (`PaymentLifecycleService.cs:2235-2244`, `:2291-2323`, `:3335-3369`). Admin cancellation blocks orders with recognized money and directs operators to refund handling (`AdminOrderService.cs:525-529`, `:867-905`, `:913-918`).
- **Correction:** The implementation does not support the original conclusion that collected orders are cancelled/failed while leaving posted GL unreversed. The correct architectural rule is: recognized-payment cancellation is blocked/collapsed; refund lifecycle must be used.
- **Recommendation:** Keep the guard. Add a regression test asserting collected/paid/settled orders cannot pass through failure/cancel mutation paths.

### C-02 / CV-02 - Cancelled/refunded payment posts no reversal journal
- **Classification:** PARTIALLY VERIFIED
- **Previous severity:** CRITICAL
- **New severity:** MEDIUM for ERP supplier-payment reversal capability; order-refund portion removed
- **Evidence:** Order refunds are implemented: `PaymentLifecycleService.RecordRefundAsync` validates refund amount and enqueues accounting (`PaymentLifecycleService.cs:888-985`), post-commit refund posting exists (`:5374-5459`), `CommerceAccountingService.PostEcommerceRefundAsync` posts `SourceType = "Order_Refund"` (`CommerceAccountingService.cs:1231-1275`), and `AccountingOutboxProcessor` handles `EcommerceRefund` (`AccountingOutboxProcessor.cs:2413-2477`). Supplier payment creation exists (`SuppliersController.cs:155-340`; `CommerceAccountingService.cs:369-421`), but no `ReverseSupplierPayment`, `CancelSupplierPayment`, or supplier-payment reversal outbox path was found.
- **Impact:** This is a missing ERP supplier-payment void/reversal workflow, not proven current GL corruption. If a supplier payment must be unwound operationally, the system lacks a first-class symmetric accounting path.
- **Recommendation:** Add an explicit supplier-payment void/reversal command that creates a reversal/compensating journal through existing posting/outbox infrastructure. Do not use the order refund architecture as evidence of a missing order-payment reversal defect.

---

## 5. HIGH FINDINGS

### H-01 / F-06-01 - Audit scope/file-map mismatch for FLOW-ACC-06
- **Classification:** VERIFIED
- **Severity:** HIGH
- **Evidence:** `.ai/hermes/accounts/FLOW-ACC-06-PAYMENTS.md:6` scopes out `PaymentLifecycleService`, gateway adapters, and CommerceHub payments. The original audit used those runtimes for multiple FLOW-ACC-06 conclusions.
- **Impact:** Findings must not mix ERP party payments and CommerceHub/order-payment runtime without explicit scope labeling.
- **Recommendation:** Keep ERP party payments and order/CommerceHub payment runtime in separate canonical flows and audit sections.

### H-04 / F-11-04 - No role-based authorization on account adjustments
- **Classification:** VERIFIED
- **Severity:** HIGH
- **Evidence:** `StatementOfAccountController` has class-level `[Authorize]` only (`StatementOfAccountController.cs:24-25`); `CreateAdjustment` has no role/policy attribute (`:534-537`).
- **Impact:** Any authenticated user who can reach the endpoint can post a party-balance correction, subject only to idempotency and validation guards.
- **Recommendation:** Add a policy such as `AccountingAdjustmentPolicy` aligned with existing project roles (`Owner`, `Admin`, `Accountant`, or equivalent), and keep idempotency/reconciliation unchanged.

### H-06 / CV-03 - Manual financial corrections lack consistent role authorization
- **Classification:** VERIFIED
- **Severity:** HIGH
- **Evidence:** `ExpensesController` has only `[Authorize]` (`ExpensesController.cs:21-24`), and `ApproveExpense` has no role/policy (`:402-403`). `StatementOfAccountController.CreateAdjustment` likewise lacks role/policy authorization. Supplier payment and cashbox flows use role restrictions (`SuppliersController.cs:155-157`; `CashboxController.cs:335-337`, `:677-679`).
- **Impact:** Manual correction paths are weaker than payment and treasury paths, creating a segregation-of-duties gap.
- **Recommendation:** Apply a shared accounting-governance authorization policy to expense approval and party adjustments. Avoid broad rewrites; use controller attributes or endpoint policy filters.

---

## 6. MEDIUM FINDINGS

### H-02 / F-07-01 - Negative/zero expense amount accepted before posting
- **Classification:** PARTIALLY VERIFIED
- **Previous severity:** HIGH
- **New severity:** MEDIUM
- **Evidence:** `CreateExpenseDto.Amount` and `UpdateExpenseDto.Amount` have no `[Range]` validation (`ExpenseDTOs.cs:49-78`), and the controller persists raw amounts (`ExpensesController.cs:208-215`, `:299-301`). However, `PostingLine.Debit/Credit` reject `amount <= 0` (`PostingRequest.cs:84-88`, `:101-105`), and posting validation rejects invalid debit/credit lines (`FinancialPostingService.cs:420-431`).
- **Corrected impact:** Invalid amounts can persist in operational `Expenses`, distort operational expense reports, and cause outbox posting failure/divergence. They do not post as wrong-sign GL journals through the current posting engine.
- **Recommendation:** Add DTO and server-side guards rejecting `Amount <= 0` before persistence.

### H-03 / F-11-03 - Party balance commits synchronously while adjustment GL posts asynchronously
- **Classification:** PARTIALLY VERIFIED
- **Previous severity:** HIGH
- **New severity:** MEDIUM
- **Evidence:** `LedgerService.ApplyPartyTransactionAsync` writes `AccountLedger` and cache synchronously (`LedgerService.cs:196-239`), while adjustment GL is posted by the outbox processor (`AccountingOutboxProcessor.cs:1758-1793`). `FinancialIntegrityService.ValidateAdjustmentLinksAsync` detects missing journal links (`FinancialIntegrityService.cs:1003-1079`).
- **Corrected impact:** This is an eventual-consistency exposure with passive detection, not an undetected steady-state source-of-truth break.
- **Recommendation:** Surface unposted adjustment status and alert on outbox/journal-link failures.

### H-05 / CV-01 - Expense GL posting lacks `FinancialIntegrityService` link validation
- **Classification:** PARTIALLY VERIFIED
- **Previous severity:** HIGH/P0
- **New severity:** MEDIUM/P1
- **Evidence:** `FinancialIntegrityService.ValidateAsync` has no expense-link validation (`FinancialIntegrityService.cs:191-280`), while supplier payment, adjustment, and purchase return link checks exist (`:938-1187`). Expense posting is async via outbox (`AccountingOutboxService.cs:478-501`; `AccountingOutboxProcessor.cs:2110-2163`). Expense events are critical financial-truth events and remain retryable rather than terminally dropped (`AccountingOutboxProcessor.cs:50-73`, `:559-564`; `AccountingOutboxMessage.cs:248-260`).
- **Corrected impact:** The issue is not total absence of safety net. The outbox is durable and retryable, but there is no scheduled integrity scan proving each approved expense has an outbox row and posted `JournalEntry` with `SourceType = "Expense"`.
- **Recommendation:** Add `ValidateExpenseLinksAsync` using outbox `EventType = "PosExpense"` and journal `SourceType = "Expense"`; alert on orphaned approved expenses.

### H-07 / CV-04 - Validation gaps across expense, party-floor, and cashbox close
- **Classification:** PARTIALLY VERIFIED
- **Previous severity:** HIGH
- **New severity:** MEDIUM
- **Evidence:** Expense amount lacks input validation, but GL posting rejects non-positive lines. `CreateAdjustment` rejects non-positive adjustment amounts (`StatementOfAccountController.cs:580-586`), and supplier payments reject non-positive amount (`SuppliersController.cs:191-198`). Party running balances have no configurable floor (`LedgerService.cs:217-239`), and cashbox `ActualCash` accepts negative values (`CashboxDTOs.cs:25-29`; `CashboxController.cs:513-557`).
- **Corrected impact:** The systemic issue is uneven validation, not universal negative-amount acceptance.
- **Recommendation:** Add targeted guards: expense amount > 0, cashbox actual cash >= 0, optional party-floor policy by party/account type.

### H-08 / CV-06 - Async GL posting model has uneven convergence visibility
- **Classification:** PARTIALLY VERIFIED
- **Previous severity:** HIGH
- **New severity:** MEDIUM
- **Evidence:** Expenses and adjustments post through async outbox paths; POS sale posting is synchronous in the request path. Online/order payments are not blind: order/refund posting statuses are updated on success/failure (`AccountingOutboxProcessor.cs:2676-2727`, `:3084-3144`) and downstream gates consult posting status (`CarrierShipmentService.cs:637-642`; `AffiliateService.cs:1447-1457`). Expense flow lacks the same integrity scan/status exposure.
- **Corrected impact:** Convergence safeguards exist, but are inconsistent by flow.
- **Recommendation:** Standardize a minimal convergence contract: each async financial source should expose posted/failed/pending status or be covered by `FinancialIntegrityService`.

### M-01 / F-09-03 - Supplier outstanding aux data comes from supplier mirror/business tables
- **Classification:** VERIFIED
- **Severity:** MEDIUM
- **Evidence:** Supplier balance is ledger-sourced, but `TotalPaid`, `TotalInvoiced`, and `DueDate` use `SupplierTransactions` and `PurchaseInvoices` (`StatementOfAccountController.cs:1032-1049`, `:1077-1109`). Customer aux aggregates use `AccountLedgers` (`:991-1010`).
- **Recommendation:** Source supplier aux aggregates from `AccountLedger` where possible, or label them as operational invoice/payment aggregates.

### M-02 / F-10-01 - Concurrent cashbox close race
- **Classification:** VERIFIED
- **Severity:** MEDIUM
- **Evidence:** `CloseCashbox` starts a default transaction and reads/guards `IsActive` without row lock or conditional update (`CashboxController.cs:496-511`), then commits inactive state (`:519-526`).
- **Recommendation:** Use conditional update `WHERE IsActive = 1` or stricter isolation/row lock and treat 0 affected rows as already closed.

### M-03 / F-10-02 - Z-Report counts reversed source journals at face value
- **Classification:** VERIFIED
- **Severity:** MEDIUM
- **Evidence:** Daily close queries include `JournalStatus.Reversed` sales/returns lines (`ReportsController.cs:369-427`). Reversal journals use `SourceType = "Reversal"` (`FinancialPostingService.cs:278-296`), so the original `SalesInvoice` subtotal can remain counted while the reversal is not part of that subtotal.
- **Recommendation:** Exclude reversed source journals from category subtotals or include reversal journals consistently.

### M-04 / F-10-03 - Cashbox closure audit is post-commit and non-atomic
- **Classification:** VERIFIED
- **Severity:** MEDIUM
- **Evidence:** Cashbox close commits state first (`CashboxController.cs:525-526`), then awaits audit logging (`:528-548`). `AuditService.EnqueueLog` persists immediately but falls back to an in-memory queue on failure (`AuditService.cs:163-180`; `AuditQueue.cs:15-27`).
- **Correction:** The prior phrase "fire-and-forget" was inaccurate; the real issue is post-commit best-effort audit with non-durable fallback.
- **Recommendation:** Persist the closure audit in the close transaction or use a durable audit outbox.

### M-05 / F-11-05 - No party-balance floor policy for adjustments
- **Classification:** VERIFIED
- **Severity:** MEDIUM
- **Evidence:** `LedgerService` computes running balance without a floor (`LedgerService.cs:196-239`). Adjustment amount itself is positive-validated, but the resulting party balance is not floor-validated.
- **Recommendation:** Add configurable party-balance floor rules only where business policy requires them.

### M-06 / F-11-06 - Adjustments are not linked to GL by stored journal id
- **Classification:** VERIFIED
- **Severity:** MEDIUM
- **Evidence:** `PartyLedgerAdjustment` has no `JournalEntryId` (`PartyLedgerAdjustment.cs:10-31`). Link validation reconstructs by `SourceType = "PartyLedgerAdjustment"` and `SourceId` (`FinancialIntegrityService.cs:1036-1041`).
- **Recommendation:** Store posted `JournalEntryId` when the outbox post succeeds, or expose derived link status without changing historical rows.

### M-07 / F-11-07 - No adjustment approval/reversal/cancellation/deletion lifecycle
- **Classification:** VERIFIED
- **Severity:** MEDIUM
- **Evidence:** `PartyLedgerAdjustment` has no status/lifecycle fields (`PartyLedgerAdjustment.cs:10-31`); adjustment controller exposes create but no lifecycle commands (`StatementOfAccountController.cs:531-762`).
- **Recommendation:** Add a small reversal/void workflow if adjustments can be operationally undone; otherwise document compensating-entry policy.

### M-08 / F-11-08 - No accounting-period guard on adjustments
- **Classification:** VERIFIED
- **Severity:** MEDIUM
- **Evidence:** `CreateAdjustment` has no period/lock check before ledger/outbox writes (`StatementOfAccountController.cs:535-726`), and no `IAccountingPeriodLock`/period service was found.
- **Recommendation:** Add a shared accounting-period service at write boundaries, not in read-only reports.

### M-09 / F-07-02 - Expense create lacks existence/funding-target validation
- **Classification:** VERIFIED
- **Severity:** MEDIUM
- **Evidence:** Expense create/update assigns category, supplier, cashbox, and bank IDs without existence validation (`ExpensesController.cs:208-220`, `:299-307`). Posting resolves funding later (`CommerceAccountingService.cs:804-810`, `:1505-1518`).
- **Recommendation:** Validate category, supplier, payment method, and funding target before persistence; return 400 instead of relying on FK/posting failures.

### M-10 / F-07-03 - Expense reads rely on global tenant filter only
- **Classification:** VERIFIED
- **Severity:** MEDIUM
- **Evidence:** Global filter exists (`ApplicationDbContext.cs:579-583`). `GetExpenses`, `GetExpense`, and `GetReport` lack explicit tenant predicates (`ExpensesController.cs:76-82`, `:144-150`, `:905-910`), while write paths using `IgnoreQueryFilters()` reapply tenant predicates (`:287-291`, `:359-363`, `:425-429`).
- **Recommendation:** Add explicit `TenantId == RequiredTenantId` predicates to expense reads for defense in depth.

### M-11 / F-07-05 - Expense report can diverge from GL/P&L during async lag
- **Classification:** VERIFIED
- **Severity:** MEDIUM
- **Evidence:** Expense report reads operational `Expenses` (`ExpensesController.cs:905-989`), while P&L reads ledger entries (`FinancialReportService.cs:193-270`). Expense posting is async.
- **Recommendation:** Label the expense report as operational/pre-GL or expose GL-posted/converged state.

### M-12 / F-08-06 - No `from <= to` validation on financial reports
- **Classification:** VERIFIED
- **Severity:** MEDIUM
- **Evidence:** `NormalizeToUtc` normalizes only date kind (`FinancialReportsController.cs:46-54`), and endpoints pass inverted ranges through (`:64-75`, `:131-142`, `:171-182`, `:228-249`).
- **Recommendation:** Reject inverted ranges at the controller boundary.

### M-13 / F-08-07 - No closed-period governance owner
- **Classification:** VERIFIED
- **Severity:** MEDIUM
- **Evidence:** Financial reports read arbitrary ledger ranges (`FinancialReportService.cs:56-62`, `:216-223`, `:297-303`). No period-lock service was found. This is a write-governance gap, not a report mutability bug.
- **Recommendation:** Add period-lock checks to all financial write paths.

### M-14 / F-08-08 - Multi-currency not supported in core ledger/SOA reports
- **Classification:** VERIFIED
- **Severity:** MEDIUM
- **Evidence:** `LedgerEntry` has decimal amounts without currency (`LedgerEntry.cs:40-59`), and SOA/report DTOs carry no currency (`StatementOfAccountDTOs.cs:15-96`).
- **Recommendation:** Treat as a target-market scope decision; do not implement unless multi-currency tenants are required.

### M-15 / F-08-09 - Retained earnings/profit carry-forward not implemented
- **Classification:** VERIFIED
- **Severity:** MEDIUM
- **Evidence:** Balance sheet reads raw asset/liability/equity balances (`FinancialReportService.cs:288-369`), and account codes include opening balance equity but no retained earnings account (`IAccountResolver.cs:66-67`).
- **Recommendation:** Add year-end closing journal only when fiscal-year close is in scope.

### M-16 / F-06-03 - Webhook idempotency/logging is route-specific and inconsistent
- **Classification:** PARTIALLY VERIFIED
- **Previous severity:** MEDIUM
- **New severity:** MEDIUM architectural consistency item
- **Evidence:** The original claim that Stripe/Paymob have zero `PaymentWebhookLog` references is false: Stripe and Paymob payment services use webhook logs (`StripePaymentService.cs:1090-1218`; `PaymobPaymentService.cs:803-1000`). Fawry persists `PaymentWebhookLog` (`FawryWebhookController.cs:361-425`), and unique provider-event indexing exists (`ApplicationDbContext.cs:735-740`). CommerceHub paths use different logging/replay records (`CommerceHubPaymentService.cs:1028-1037`, `:1218-1289`; `ApplicationDbContext.cs:2438-2444`, `:2466-2468`).
- **Recommendation:** Standardize webhook idempotency semantics and naming across routes; do not state Stripe/Paymob lack durable logging categorically.

### M-17 / CV-05 - Tenant-isolation read/write asymmetry is broader than expenses
- **Classification:** VERIFIED
- **Severity:** MEDIUM
- **Evidence:** Financial reports explicitly filter tenant (`FinancialReportService.cs:56-62`, `:137-155`, `:297-303`). Expense reads, SOA reads, daily close, and some cashbox reads rely primarily on global filters (`ExpensesController.cs:76-82`, `:905-910`; `StatementOfAccountController.cs:72-75`, `:227-230`, `:949-956`; `ReportsController.cs:369-445`).
- **Recommendation:** Add explicit tenant predicates to high-value read surfaces while retaining global filters.

### M-18 / CV-07 - Party/GL decoupling boundary is real and should be documented
- **Classification:** VERIFIED
- **Severity:** MEDIUM
- **Evidence:** POS cash/card sales debit cash/clearing and credit sales; only credit sales debit AR (`CommerceAccountingService.cs:89-177`). Adjustments post to AR/AP plus adjustment accounts (`:505-552`).
- **Recommendation:** Document which party-ledger entries are informational versus GL-control-account affecting.

### M-19 / CV-08 - Party balance has authoritative ledger, cache, and supplier mirror
- **Classification:** VERIFIED
- **Severity:** MEDIUM
- **Evidence:** `AccountLedger.RunningBalance` is authority (`AccountLedger.cs:52-55`), `LedgerService` maintains party cache (`LedgerService.cs:217-239`, `:430-445`), and supplier aux uses `SupplierTransactions` (`StatementOfAccountController.cs:1077-1093`). `LedgerService.RebuildTrackedBalancesAsync` recalculates existing supplier transaction balances (`LedgerService.cs:409-423`), so the earlier repair-gap wording was overstated.
- **Recommendation:** Make AccountLedger the display source for party balances and label supplier transaction balances as auxiliary operational data.

### M-20 / CV-09 - Reversed/not-yet-posted reporting semantics differ by surface
- **Classification:** VERIFIED
- **Severity:** MEDIUM
- **Evidence:** Financial reports net posted and reversed ledger entries (`FinancialReportService.cs:56-62`, `:216-223`); daily close counts reversed source journals in subtotals (`ReportsController.cs:369-427`); expense reports read operational expenses before GL convergence (`ExpensesController.cs:905-989`).
- **Recommendation:** Standardize reversal and pre-GL disclosure conventions.

### M-21 / CV-11 - Accounting-period guard has no owner
- **Classification:** VERIFIED
- **Severity:** MEDIUM
- **Evidence:** No period-lock service was found; adjustments and expenses do not check closed periods before writes.
- **Recommendation:** Add one shared period service invoked by financial write paths.

### M-22 / F-10-04 - Negative `ActualCash` accepted on close
- **Classification:** VERIFIED
- **Severity:** LOW as domain item; folded into M-07/CV-04 systemic validation
- **Evidence:** `CloseCashboxRequest.ActualCash` is unconstrained (`CashboxDTOs.cs:25-29`), and close accepts it in variance calculation (`CashboxController.cs:513-557`).

### M-23 / F-09-01 - Customer adjustment `BalanceAfter` sourced from cached column
- **Classification:** VERIFIED
- **Severity:** LOW as domain item; folded into M-19/CV-08
- **Evidence:** Party cache and ledger running balance are separately maintained by `LedgerService`; supplier and customer balance-after capture remains inconsistent enough to justify the CV-08 ownership cleanup.

---

## 7. LOW FINDINGS

- **L-01 / F-09-02 - Classification: VERIFIED.** SOA read endpoints are not individually audited; writes are audited. Keep as low unless read-access audit is a compliance requirement.
- **L-02 / F-09-04 - Classification: VERIFIED.** SOA masks sign with `Math.Abs` and returns `BalanceType`; this is acceptable if UI always renders direction.
- **L-03 / F-09-05 - Classification: VERIFIED.** SOA has no currency fields; overlaps multi-currency scope item.
- **L-04 / F-09-06 - Classification: VERIFIED.** Aging is not in SOA query; separate aging report exists. Treat as design choice.
- **L-05 / F-07-04 - Classification: PARTIALLY VERIFIED.** `ExpensesController` has no direct ledger writes, but the earlier "zero JournalEntries references" wording was imprecise because one read-only `JournalEntries.AnyAsync` exists (`ExpensesController.cs:544-555`).
- **L-06 / F-07-06 - Classification: VERIFIED.** No explicit expense void endpoint exists; update/delete/reject mutation outbox and `ProcessExpenseReversalAsync` provide accounting reversal mechanics. This is UX/API clarity, not integrity failure.
- **L-07 / F-07-07 - Classification: VERIFIED.** Expense `ReferenceNumber` is caller-provided and non-unique; no generated expense number exists.
- **L-08 / F-08-10 - Classification: VERIFIED.** Cash Flow investing/financing sections are empty placeholders (`FinancialReportService.cs:441-452`).
- **L-09 / F-08-11 - Classification: VERIFIED.** `AccountSummary.IsTreasury` is projected from `Account.IsSystem`; the later DTO mapping copies that projected value, it does not recover `account.IsTreasury` (`FinancialReportService.cs:565-590`).
- **L-10 / F-08-12 - Classification: VERIFIED.** Financial report access parameters are not persisted for audit; controller has no audit service integration.
- **L-11 / F-10-04 - Classification: VERIFIED.** Negative `ActualCash` accepted; no GL mutation, but closure data can be nonsensical.
- **L-12 / F-11-12 - Classification: VERIFIED.** Backend requires `Idempotency-Key` for adjustment creation (`StatementOfAccountController.cs:539-546`); frontend `soaApi.createAdjustment` does not provide one (`soaApi.js:29-35`; `Adjustments.jsx:107-112`).
- **L-13 / F-11-09/10/11 - Classification: VERIFIED.** Positive controls remain: adjustment idempotency, reconciliation checks for several flows, and tenant-scoped financial report tests.
- **L-14 / F-06-04 - Classification: VERIFIED.** Settlement batch/order orphan risk remains low because settlement mutation and accounting outbox are transactional and critical settlement event failures remain retryable.
- **L-15 / CV-12 - Classification: PARTIALLY VERIFIED.** Webhook logging/idempotency is heterogeneous, but the categorical Fawry-vs-Stripe/Paymob absence claim was incorrect.

---

## 8. ARCHITECTURAL IMPROVEMENTS

These are not defects. They are hardening opportunities or verified strengths.

- **AI-01 / F-11-01, F-11-02 - Classification: VERIFIED.** Adjustments create new journals through `FinancialPostingService` and do not mutate historical journals. Keep.
- **AI-02 / F-11-14 - Classification: VERIFIED.** Dedicated GL adjustment accounts 6120/6130 keep manual corrections separable. Keep.
- **AI-03 / F-08-01,02,03,04,05,14 - Classification: VERIFIED.** Financial Reports are ledger-only read projections; `FinancialReportService` implements `ILedgerOnlyService`, uses ledger sources, and is guarded by ledger-isolation tests.
- **AI-04 / F-09-07 - Classification: VERIFIED.** SOA is a live `AccountLedger` projection with no persisted statement state. Keep.
- **AI-05 / F-10-05 - Classification: VERIFIED.** Daily Closing is read-only from ledger and does not post GL. Optional discrepancy alerting remains an enhancement.
- **AI-06 / F-06-05 - Classification: VERIFIED.** Document two-tier party/GL decoupling for POS cash/card customer entries.
- **AI-07 / F-08-13 - Classification: VERIFIED.** Use half-open opening-balance date predicate for timestamp robustness.
- **AI-08 / F-11-13 - Classification: VERIFIED.** Add read-only adjustment history endpoint/UI if operators need adjustment review.
- **AI-09 / CV-10 - Classification: VERIFIED.** Expose outbox-lag/GL-converged indicator for async financial sources.
- **AI-10 / F-07-06 - Classification: VERIFIED.** Optional explicit expense void endpoint for operator clarity; current reversal mechanics are acceptable.
- **AI-11 - Classification: VERIFIED.** Preserve `ApplicationDbContext` journal/ledger mutation guard through `LedgerMutationScope`.
- **AI-12 - Classification: VERIFIED.** Preserve non-terminal retry behavior for critical accounting outbox events.
- **AI-13 - Classification: VERIFIED.** Add regression tests for collected-order cancel/failure guards and refund accounting to prevent recurrence of the rejected C-01 assumption.

---

## 9. CROSS-FLOW INCONSISTENCIES

| # | Inconsistency | Finding | Classification | Severity |
|---:|---|---|---|---|
| 1 | Supplier payment lacks explicit reversal/void workflow; order refund accounting is implemented | CV-02 / C-02 | PARTIALLY VERIFIED | MEDIUM |
| 2 | Expense GL posting lacks `FinancialIntegrityService` link scan | CV-01 | PARTIALLY VERIFIED | MEDIUM |
| 3 | Manual corrections lack consistent role authorization | CV-03 / F-11-04 | VERIFIED | HIGH |
| 4 | Validation gaps exist, but not across every claimed path | CV-04 / F-07-01 | PARTIALLY VERIFIED | MEDIUM |
| 5 | Async GL convergence visibility is uneven | CV-06 | PARTIALLY VERIFIED | MEDIUM |
| 6 | Party balance has authoritative ledger, cache, and supplier mirror | CV-08 / F-09-01/03 | VERIFIED | MEDIUM |
| 7 | Party/GL reconciliation boundary is real but under-documented | CV-07 / F-06-05 | VERIFIED | MEDIUM |
| 8 | Reversed/not-yet-posted semantics differ across reporting surfaces | CV-09 / F-10-02 | VERIFIED | MEDIUM |
| 9 | Tenant read/write asymmetry is broader than originally stated | CV-05 / F-07-03 | VERIFIED | MEDIUM |
| 10 | Accounting-period lock has no shared owner | CV-11 | VERIFIED | MEDIUM |
| 11 | Webhook logging/idempotency semantics are heterogeneous | CV-12 / F-06-03 | PARTIALLY VERIFIED | LOW |
| 12 | No shared outbox-drained/GL-converged operator signal | CV-10 | VERIFIED | LOW |

---

## 10. MISSING FEATURES AND DESIGN DISTINCTIONS

The following are implementation-backed gaps. They are not automatically bugs unless their absence violates current accounting correctness or required controls.

| Item | Classification | Type | Peer-reviewed note |
|---|---|---|---|
| Supplier-payment void/reversal workflow | PARTIALLY VERIFIED | Missing feature / governance control | Needed if supplier payments can be operationally cancelled or refunded. |
| Order refund accounting | INCORRECT | Implemented feature | `RecordRefundAsync` and `PostEcommerceRefundAsync` provide accounting path. |
| Expense reconciliation scan | PARTIALLY VERIFIED | Architectural hardening | Add integrity scan; outbox retry already exists. |
| Role authorization on expense approval and adjustments | VERIFIED | Implementation defect / governance | High priority control gap. |
| Expense amount and cashbox `ActualCash` validation | PARTIALLY VERIFIED | Implementation defect | Add targeted guards. |
| Party negative-balance floor | VERIFIED | Policy decision | Implement only if business policy requires. |
| Accounting period lock | VERIFIED | Architectural governance feature | Should be shared by all financial write paths. |
| Adjustment lifecycle/history | VERIFIED | Missing workflow | Add if corrections require approval/reversal/review. |
| Retained earnings closing | VERIFIED | Fiscal close feature | Required for fiscal-year financial statements. |
| Cash Flow investing/financing | VERIFIED | Report completeness feature | Required only if those activities exist in target tenants. |
| Multi-currency | VERIFIED | Scope decision | Do not implement unless target market requires it. |
| Expense receipt upload/category subaccounts/expense numbers | VERIFIED | Operational enhancement | Not a correctness bug. |
| Frontend adjustment idempotency key | VERIFIED | Contract defect | Shipped UI cannot call endpoint successfully without supplying key. |
| Webhook logging standardization | PARTIALLY VERIFIED | Architecture consistency | Existing logs differ by route/runtime. |

---

## 11. FALSE POSITIVES AND CORRECTIONS

The original "0 findings rejected" conclusion is no longer accurate after source-code peer review.

- **C-01 rejected:** collected/recognized-payment cancellation is blocked or collapsed, not posted as a failed/cancelled order without reversal.
- **Order-refund portion of C-02 rejected:** order refund accounting exists and posts through the accounting outbox/GL path.
- **H-02 corrected:** negative/zero expenses can persist but do not post wrong-sign GL because posting lines reject non-positive amounts.
- **H-05 corrected:** expenses lack a `FinancialIntegrityService` link scan, but critical outbox retry/idempotency already provides part of the safety net.
- **M-16/CV-12 corrected:** Stripe/Paymob are not categorically missing durable webhook logs; logging/idempotency is heterogeneous by route/runtime.
- **M-04 corrected:** cashbox closure audit is awaited, not fire-and-forget; the risk is post-commit non-atomicity and non-durable fallback.
- **M-19 corrected:** supplier transaction balances are recalculated by rebuild for existing rows; the remaining issue is source-of-truth split.
- **L-09 corrected:** the `IsTreasury` mapping remains wrong through projection/copy; line `:589` does not recover the entity `IsTreasury` flag.

---

## 12. ARCHITECTURE STRENGTHS

- **Single protected double-entry engine.** Production `JournalEntries.Add` and `LedgerEntries.Add` are centralized in `FinancialPostingService`; `ApplicationDbContext` blocks unauthorized journal/ledger creation unless `LedgerMutationScope` is active.
- **Immutable journals and reversal-only correction.** `ReverseJournalAsync` creates new journals, and DbContext guards block deletes and unauthorized identity/mutation changes.
- **Durable accounting outbox.** Critical event types include POS, expenses, online payments, refunds, and settlements; critical failures remain retryable instead of being terminally dropped.
- **Strong idempotency.** Outbox and journal unique idempotency indexes, HTTP idempotency, mutation versions, and posting double checks reduce duplicate financial postings.
- **Tenant isolation.** Global tenant filters and tenant save interceptor protect multi-tenant data; explicit tenant predicates remain recommended on high-value read surfaces.
- **Clean read models.** Financial Reports, SOA, and Daily Closing do not mutate GL.
- **Ledger-derived cashbox close.** Cashbox expected cash is derived from ledger, not obsolete shadow balance fields.
- **Adjustment posting pattern.** Adjustments use the existing posting/outbox infrastructure and dedicated adjustment accounts.

---

## 13. RECOMMENDED REFACTORING ROADMAP

### Immediate (P0/P1 - before external audit reliance)
1. **CV-03 / F-11-04 - Role authorization.** Add accounting-governance policy to `CreateAdjustment` and `ApproveExpense`.
2. **CV-01 - Expense integrity coverage.** Add `ValidateExpenseLinksAsync` with alerting for approved expenses missing outbox/journal linkage.
3. **CV-02 - Supplier payment reversal/void.** Add explicit supplier-payment unwind command if business operations allow cancellation/refund of supplier payments.

### Short-term (P1/P2 - convergence and validation)
4. **CV-04 - Targeted validation.** Reject non-positive expense amounts, negative close cash, and invalid funding targets before persistence.
5. **CV-06 / CV-10 - Convergence visibility.** Add per-source GL-posted/failed/pending indicators or a shared outbox-lag endpoint.
6. **CV-11 - Period lock.** Add shared period-lock service at financial write boundaries.
7. **F-10-03 - Closure audit durability.** Move closure audit into transaction or durable audit outbox.

### Medium-term (P2 - consistency and ownership)
8. **CV-08 / F-09-03 - Party balance ownership.** Prefer `AccountLedger` for party display aggregates; label supplier mirror data as operational.
9. **CV-05 - Explicit tenant predicates.** Add predicates to expense, SOA, daily close, and cashbox read surfaces.
10. **CV-09 - Reporting semantics.** Standardize reversed and pre-GL handling across reports.
11. **F-08-09 - Retained earnings close.** Add only if fiscal-year closing is in target scope.
12. **F-06-03 / CV-12 - Webhook consistency.** Standardize webhook logging/idempotency semantics across routes.

### Long-term (P3 - scope-dependent completeness)
13. **F-11-07 - Adjustment lifecycle and history.** Add approval/reversal/history UI if operationally required.
14. **F-08-08 / F-09-05 - Multi-currency.** Implement only if target tenants require it.
15. **F-10-05 - Daily-closing discrepancy workflow.** Add alert/event/suspense-account handling if business process requires formal discrepancy management.
16. **Canonical docs.** Split ERP party payments and order/CommerceHub payments into separate canonical flow maps.

---

## 14. FINAL VERDICT

| Profile | Peer-reviewed verdict | Rationale |
|---|---|---|
| Small Business | APPROVED WITH REMEDIATION | Double-entry foundation is strong; close role auth, validation, and expense scan before relying on formal books. |
| Medium Business | CONDITIONAL | Requires governance and convergence visibility improvements before scale. |
| Enterprise | NOT YET | Needs period lock, supplier-payment reversal policy, retained earnings close, stronger read defense-in-depth, and possibly multi-currency. |
| Internal Financial Audit | CONDITIONAL | GL engine is strong, but expense reconciliation and manual-correction authorization gaps must be remediated. |
| External Audit | NOT YET | No active CRITICAL defect remains, but governance, period lock, reconciliation coverage, and audit durability are not external-audit ready. |
| High-volume Retail | CONDITIONAL/NOT YET | Async convergence visibility and retry monitoring must be operationalized. |
| Multi-tenant SaaS | CONDITIONAL | Tenant isolation is strong, but explicit read predicates should be added to high-value read surfaces. |
| Mission Critical | NOT YET | Requires durable audit, outbox-lag operator signal, period lock, and uniform correction controls. |

**Bottom line:** The prior report correctly identified a strong accounting skeleton, but overstated payment reversal and negative-expense GL risks. The peer-reviewed position is more precise: the system is fundamentally sound, with no surviving CRITICAL implementation-backed accounting defect, but it is not yet governance-complete or externally audit-ready.

---

## 15. PEER REVIEW SUMMARY

**Total findings reviewed:** 66 identifiable domain/cross-flow findings and items.
**Verified:** 53.
**Partially verified:** 11.
**Incorrect:** 2.
**Unproven:** 0.
**Out of scope:** 0.
**Downgraded:** 7 severity/conclusion downgrades.
**Upgraded:** 0 severity upgrades.
**Removed:** 2 unsupported defect conclusions: collected-order cancellation without reversal, and missing order-refund accounting.
**Added:** 0 new defect findings; added stronger implementation evidence for journal mutation guards, critical outbox retry, order cancellation guards, and refund accounting.
**Overall confidence:** High. The material corrections are based on direct implementation evidence across controllers, lifecycle services, posting service, outbox processor, entities, DbContext guards, and tests.

**External ERP architecture review suitability:** Yes, with caveat. This report is now suitable as a basis for external ERP architecture review because unsupported CRITICAL claims were removed, severities were aligned to implementation evidence, missing features were separated from defects, and recommendations reuse existing architecture. It is not a certification of audit readiness; it is an architecture review identifying the remaining controls needed before external financial-audit reliance.

---

*Peer review source of truth: implementation. Changelog: `.ai/hermes/audits/ACCOUNTING-ARCHITECTURE-AUDIT-CHANGELOG.md`.*
