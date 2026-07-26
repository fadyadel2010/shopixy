# ACCOUNTING ARCHITECTURE AUDIT - PEER REVIEW CHANGELOG

**Date:** 2026-07-20
**Reviewer role:** Principal ERP Architect, independent peer review
**Source of truth:** Implementation
**Reviewed audit:** `.ai/hermes/audits/ACCOUNTING-ARCHITECTURE-AUDIT.md`

---

## Summary

The prior audit was not rewritten as a new audit; it was corrected in place. Correct implementation-backed findings were preserved. Unsupported, overstated, or stale conclusions were corrected or downgraded.

**Total identifiable findings/items reviewed:** 66
**Verified:** 53
**Partially verified:** 11
**Incorrect:** 2
**Unproven:** 0
**Out of scope:** 0
**Downgraded:** 7
**Upgraded:** 0
**Removed:** 2 unsupported defect conclusions
**Added:** 0 new defect findings
**Overall confidence:** High

---

## Change Entries

### Finding ID: Executive Summary / Totals
- **Previous Assessment:** 2 CRITICAL, 8 HIGH, 24 MEDIUM, 15 LOW, 13 ARCHITECTURAL-IMPROVEMENT; 0 rejected findings; report not externally audit-ready due to 2 CRITICAL/P0 items.
- **New Assessment:** 0 CRITICAL, 3 HIGH, 27 MEDIUM, 15 LOW, 13 ARCHITECTURAL IMPROVEMENT; 2 incorrect defect conclusions removed; externally review-suitable but not audit-ready.
- **Reason:** Peer review disproved the two CRITICAL payment conclusions in their original form and downgraded several overstated HIGH findings based on current safeguards.
- **Evidence:** See C-01, C-02, H-02, H-05, H-07, H-08 entries below.
- **Files Reviewed:** `ACCOUNTING-ARCHITECTURE-AUDIT.md`; implementation files listed in finding-specific entries.
- **Confidence:** High

### Finding ID: C-01 / F-06-02
- **Previous Assessment:** CRITICAL; cancelling/failing an already-collected order posts no reversal journal and leaves GL overstated.
- **New Assessment:** INCORRECT; removed as defect.
- **Reason:** Current implementation blocks or collapses post-collection failure/cancel paths rather than mutating collected orders to failed/cancelled without reversal.
- **Evidence:** `PaymentLifecycleService.HandleFailureAsync` collapses stale provider failures and returns without mutation for non-pending/success terminal states; admin cancellation blocks recognized-money orders and directs refund handling.
- **Files Reviewed:** `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs`; `PointOfSale.API/Services/Admin/AdminOrderService.cs`.
- **Confidence:** High

### Finding ID: C-02 / CV-02
- **Previous Assessment:** CRITICAL; cancelled/refunded payment posts no reversal journal for order payments and ERP party payments.
- **New Assessment:** PARTIALLY VERIFIED; MEDIUM for ERP supplier-payment reversal capability; order-refund portion removed.
- **Reason:** Order refund accounting is implemented. Supplier payments still lack an explicit cancel/refund/reversal workflow.
- **Evidence:** `RecordRefundAsync` validates and enqueues refund accounting; `PostEcommerceRefundAsync` posts `SourceType = "Order_Refund"`; `AccountingOutboxProcessor` handles `EcommerceRefund`; no supplier-payment reversal command or outbox path found.
- **Files Reviewed:** `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs`; `PointOfSale.Infrastructure/Accounting/CommerceAccountingService.cs`; `PointOfSale.Infrastructure/Accounting/AccountingOutboxProcessor.cs`; `PointOfSale.API/Controllers/SuppliersController.cs`; `PointOfSale.Core/Entities/SupplierPayment.cs`.
- **Confidence:** High

### Finding ID: H-01 / F-06-01
- **Previous Assessment:** HIGH; FLOW-ACC-06 scope/file-map mismatch.
- **New Assessment:** VERIFIED; HIGH retained.
- **Reason:** Canonical FLOW-ACC-06 excludes order-payment runtime, yet the original audit used those files for FLOW-ACC-06 conclusions.
- **Evidence:** `.ai/hermes/accounts/FLOW-ACC-06-PAYMENTS.md` excludes `PaymentLifecycleService`, gateway adapters, and CommerceHub payments.
- **Files Reviewed:** `.ai/hermes/accounts/FLOW-ACC-06-PAYMENTS.md`; `ACCOUNTING-ARCHITECTURE-AUDIT.md`.
- **Confidence:** High

### Finding ID: H-02 / F-07-01
- **Previous Assessment:** HIGH; negative expense posts wrong-sign GL entry.
- **New Assessment:** PARTIALLY VERIFIED; MEDIUM.
- **Reason:** Negative/zero amounts can be persisted, but current GL posting factories and posting validation reject non-positive posting lines. The wrong-sign GL conclusion was overstated.
- **Evidence:** Expense DTO/entity/controller lack amount range validation; `PostingLine.Debit/Credit` reject `amount <= 0`; `FinancialPostingService` rejects invalid debit/credit lines.
- **Files Reviewed:** `PointOfSale.API/DTOs/ExpenseDTOs.cs`; `PointOfSale.API/Controllers/ExpensesController.cs`; `PointOfSale.Core/Entities/Expense.cs`; `PointOfSale.Core/Models/Accounting/PostingRequest.cs`; `PointOfSale.Infrastructure/Accounting/FinancialPostingService.cs`; `PointOfSale.Infrastructure/Accounting/CommerceAccountingService.cs`.
- **Confidence:** High

### Finding ID: H-03 / F-11-03
- **Previous Assessment:** HIGH; party balance and GL can diverge if async worker permanently fails.
- **New Assessment:** PARTIALLY VERIFIED; MEDIUM.
- **Reason:** The async divergence window exists, but adjustment link validation and durable outbox reduce the risk compared with the prior conclusion.
- **Evidence:** `LedgerService` writes party ledger/cache synchronously; adjustment GL posts through outbox; `FinancialIntegrityService.ValidateAdjustmentLinksAsync` reconstructs and detects missing journal links.
- **Files Reviewed:** `PointOfSale.API/Services/LedgerService.cs`; `PointOfSale.Infrastructure/Accounting/AccountingOutboxProcessor.cs`; `PointOfSale.API/Services/FinancialIntegrityService.cs`; `PointOfSale.Core/Entities/PartyLedgerAdjustment.cs`.
- **Confidence:** High

### Finding ID: H-04 / F-11-04
- **Previous Assessment:** HIGH; no role-based authorization on adjustments.
- **New Assessment:** VERIFIED; HIGH retained.
- **Reason:** Implementation confirms only class-level authentication on the adjustment controller and no role/policy on `CreateAdjustment`.
- **Evidence:** `[Authorize]` exists at controller level; no role/policy attribute on `CreateAdjustment`.
- **Files Reviewed:** `PointOfSale.API/Controllers/StatementOfAccountController.cs`.
- **Confidence:** High

### Finding ID: H-05 / CV-01
- **Previous Assessment:** HIGH/P0; expense GL posting has no reconciliation/integrity safety net and can silently diverge.
- **New Assessment:** PARTIALLY VERIFIED; MEDIUM/P1.
- **Reason:** `FinancialIntegrityService` lacks an expense link scan, but expenses use a durable critical outbox with retry/non-terminal failure behavior. The issue is incomplete reconciliation coverage, not total absence of safety net.
- **Evidence:** No `ValidateExpenseLinksAsync`; supplier payment, adjustment, and purchase return link scans exist; `PosExpense` and `ExpenseReversal` are critical outbox event types; critical events remain retryable.
- **Files Reviewed:** `PointOfSale.API/Services/FinancialIntegrityService.cs`; `PointOfSale.Infrastructure/Accounting/AccountingOutboxService.cs`; `PointOfSale.Infrastructure/Accounting/AccountingOutboxProcessor.cs`; `PointOfSale.Core/Entities/Accounting/AccountingOutboxMessage.cs`; `PointOfSale.Tests/Architecture/ExpenseFinancialSafetyHardeningTests.cs`.
- **Confidence:** High

### Finding ID: H-06 / CV-03
- **Previous Assessment:** HIGH; manual financial corrections lack role authorization across domains.
- **New Assessment:** VERIFIED; HIGH retained.
- **Reason:** Expense approval and account adjustment paths lack role/policy authorization while supplier payment and cashbox paths use role restrictions.
- **Evidence:** `ExpensesController` and `StatementOfAccountController` class-level `[Authorize]`; no role/policy on `ApproveExpense` or `CreateAdjustment`; supplier and cashbox controllers use role-restricted authorization.
- **Files Reviewed:** `PointOfSale.API/Controllers/ExpensesController.cs`; `PointOfSale.API/Controllers/StatementOfAccountController.cs`; `PointOfSale.API/Controllers/SuppliersController.cs`; `PointOfSale.API/Controllers/CashboxController.cs`.
- **Confidence:** High

### Finding ID: H-07 / CV-04
- **Previous Assessment:** HIGH; systemic missing negative-amount / party-floor validation across write paths.
- **New Assessment:** PARTIALLY VERIFIED; MEDIUM.
- **Reason:** Adjustment and supplier-payment amounts are positive-validated. Expense amount and cashbox actual cash validation gaps remain, and party floor policy remains absent.
- **Evidence:** `CreateAdjustment` rejects non-positive amount; supplier payment rejects non-positive amount; expense DTO lacks range validation; cashbox close request accepts negative actual cash; `LedgerService` has no party-floor rule.
- **Files Reviewed:** `PointOfSale.API/Controllers/StatementOfAccountController.cs`; `PointOfSale.API/Controllers/SuppliersController.cs`; `PointOfSale.API/DTOs/ExpenseDTOs.cs`; `PointOfSale.API/DTOs/CashboxDTOs.cs`; `PointOfSale.API/Controllers/CashboxController.cs`; `PointOfSale.API/Services/LedgerService.cs`.
- **Confidence:** High

### Finding ID: H-08 / CV-06
- **Previous Assessment:** HIGH; inconsistent sync/async model leaves expenses and online/order payments blind.
- **New Assessment:** PARTIALLY VERIFIED; MEDIUM.
- **Reason:** Expense convergence visibility is incomplete, but online/order payments have posting status/failure state and downstream gates. The prior "blind" characterization was overstated.
- **Evidence:** Order/refund posting statuses are updated by `AccountingOutboxProcessor`; downstream shipping/affiliate paths consult financial posting status; expenses lack equivalent integrity scan/status exposure.
- **Files Reviewed:** `PointOfSale.Infrastructure/Accounting/AccountingOutboxProcessor.cs`; `PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs`; `PointOfSale.API/Services/Affiliate/AffiliateService.cs`; `PointOfSale.API/Controllers/ExpensesController.cs`; `PointOfSale.API/Services/FinancialIntegrityService.cs`.
- **Confidence:** High

### Finding ID: M-01 / F-09-03
- **Previous Assessment:** MEDIUM; supplier outstanding sub-aggregates sourced from `SupplierTransactions` mirror.
- **New Assessment:** VERIFIED; MEDIUM retained.
- **Reason:** Implementation confirms supplier aux aggregates are sourced differently from customer aux aggregates.
- **Evidence:** Supplier balance uses ledger; supplier aux uses `SupplierTransactions`/`PurchaseInvoices`; customer aux uses `AccountLedgers`.
- **Files Reviewed:** `PointOfSale.API/Controllers/StatementOfAccountController.cs`.
- **Confidence:** High

### Finding ID: M-02 / F-10-01
- **Previous Assessment:** MEDIUM; concurrent close race.
- **New Assessment:** VERIFIED; MEDIUM retained.
- **Reason:** No pessimistic lock, concurrency token, or conditional update protects `IsActive` close guard.
- **Evidence:** `CloseCashbox` reads and guards `IsActive` inside default transaction, then updates/commits.
- **Files Reviewed:** `PointOfSale.API/Controllers/CashboxController.cs`.
- **Confidence:** High

### Finding ID: M-03 / F-10-02
- **Previous Assessment:** MEDIUM; Z-Report counts reversed journals at face value.
- **New Assessment:** VERIFIED; MEDIUM retained with stronger explanation.
- **Reason:** Daily close subtotals include reversed source journals while reversal journals are categorized separately as `SourceType = "Reversal"`.
- **Evidence:** `ReportsController` includes `JournalStatus.Reversed`; `FinancialPostingService` creates reversal journals with `SourceType = "Reversal"`.
- **Files Reviewed:** `PointOfSale.API/Controllers/ReportsController.cs`; `PointOfSale.Infrastructure/Accounting/FinancialPostingService.cs`.
- **Confidence:** High

### Finding ID: M-04 / F-10-03
- **Previous Assessment:** MEDIUM; closure audit is fire-and-forget and closure can commit with no audit record.
- **New Assessment:** VERIFIED; MEDIUM retained with wording correction.
- **Reason:** Audit is awaited but happens after transaction commit and falls back to an in-memory queue if direct persistence fails.
- **Evidence:** Cashbox close commits before audit call; `AuditService.EnqueueLog` has in-memory fallback.
- **Files Reviewed:** `PointOfSale.API/Controllers/CashboxController.cs`; `PointOfSale.API/Services/Audit/AuditService.cs`; `PointOfSale.API/Services/Audit/AuditQueue.cs`.
- **Confidence:** High

### Finding ID: M-05 / F-11-05
- **Previous Assessment:** MEDIUM; no negative-balance policy on party running balance.
- **New Assessment:** VERIFIED; MEDIUM retained.
- **Reason:** Party ledger running balance can cross any floor; amount validation does not enforce resulting balance policy.
- **Evidence:** `LedgerService` calculates running balance without floor check.
- **Files Reviewed:** `PointOfSale.API/Services/LedgerService.cs`; `PointOfSale.API/Controllers/StatementOfAccountController.cs`.
- **Confidence:** High

### Finding ID: M-06 / F-11-06
- **Previous Assessment:** MEDIUM; adjustments not linked to GL at write time.
- **New Assessment:** VERIFIED; MEDIUM retained.
- **Reason:** Adjustment entity lacks `JournalEntryId`; link is reconstructed post-hoc by source fields.
- **Evidence:** `PartyLedgerAdjustment` fields; `FinancialIntegrityService` source-type/source-id lookup.
- **Files Reviewed:** `PointOfSale.Core/Entities/PartyLedgerAdjustment.cs`; `PointOfSale.API/Services/FinancialIntegrityService.cs`.
- **Confidence:** High

### Finding ID: M-07 / F-11-07
- **Previous Assessment:** MEDIUM; no adjustment approval/reversal/cancellation/deletion workflow.
- **New Assessment:** VERIFIED; MEDIUM retained.
- **Reason:** Entity has no lifecycle status and controller exposes create only.
- **Evidence:** `PartyLedgerAdjustment` has no status/lifecycle fields; adjustment controller has create path but no lifecycle commands.
- **Files Reviewed:** `PointOfSale.Core/Entities/PartyLedgerAdjustment.cs`; `PointOfSale.API/Controllers/StatementOfAccountController.cs`.
- **Confidence:** High

### Finding ID: M-08 / F-11-08
- **Previous Assessment:** MEDIUM; no accounting-period guard on adjustments.
- **New Assessment:** VERIFIED; MEDIUM retained.
- **Reason:** No period-lock service or adjustment period check found.
- **Evidence:** `CreateAdjustment` writes ledger/outbox without period check; source search found no accounting-period lock service.
- **Files Reviewed:** `PointOfSale.API/Controllers/StatementOfAccountController.cs`; repository search for period-lock terms.
- **Confidence:** High

### Finding ID: M-09 / F-07-02
- **Previous Assessment:** MEDIUM; no category/supplier existence or funding availability check at expense create.
- **New Assessment:** VERIFIED; MEDIUM retained with expanded funding-target validation note.
- **Reason:** Create/update assign IDs directly and defer failures to persistence/posting.
- **Evidence:** Expense create/update copies IDs without existence checks; account resolution happens later during posting.
- **Files Reviewed:** `PointOfSale.API/Controllers/ExpensesController.cs`; `PointOfSale.Infrastructure/Accounting/CommerceAccountingService.cs`.
- **Confidence:** High

### Finding ID: M-10 / F-07-03
- **Previous Assessment:** MEDIUM; expense read endpoints rely on EF global tenant filter.
- **New Assessment:** VERIFIED; MEDIUM retained with expanded endpoint list.
- **Reason:** `GetExpense` and helper paths also rely on global filters, not only list/report.
- **Evidence:** Global tenant filter exists; expense reads lack explicit tenant predicates; writes using `IgnoreQueryFilters()` reapply tenant predicate.
- **Files Reviewed:** `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`; `PointOfSale.API/Controllers/ExpensesController.cs`.
- **Confidence:** High

### Finding ID: M-11 / F-07-05
- **Previous Assessment:** MEDIUM; expense report totals can diverge from GL/P&L.
- **New Assessment:** VERIFIED; MEDIUM retained.
- **Reason:** Operational expense report and ledger-sourced P&L use different sources during async lag.
- **Evidence:** Expense report sums `Expenses`; P&L reads `LedgerEntries`.
- **Files Reviewed:** `PointOfSale.API/Controllers/ExpensesController.cs`; `PointOfSale.Infrastructure/Accounting/FinancialReportService.cs`.
- **Confidence:** High

### Finding ID: M-12 / F-08-06
- **Previous Assessment:** MEDIUM; no date-range order validation.
- **New Assessment:** VERIFIED; MEDIUM retained.
- **Reason:** Controller normalizes dates but does not reject inverted ranges.
- **Evidence:** `NormalizeToUtc` only adjusts `DateTimeKind`; report endpoints pass ranges onward.
- **Files Reviewed:** `PointOfSale.API/Controllers/FinancialReportsController.cs`.
- **Confidence:** High

### Finding ID: M-13 / F-08-07
- **Previous Assessment:** MEDIUM; no period/closed-period/fiscal lock on reports.
- **New Assessment:** VERIFIED; MEDIUM retained with scope correction.
- **Reason:** Reports are read-only; the actual defect is absence of period-lock ownership on write paths.
- **Evidence:** Reports query arbitrary date ranges; no period-lock service found.
- **Files Reviewed:** `PointOfSale.Infrastructure/Accounting/FinancialReportService.cs`; repository search for period-lock terms.
- **Confidence:** High

### Finding ID: M-14 / F-08-08
- **Previous Assessment:** MEDIUM; multi-currency not supported.
- **New Assessment:** VERIFIED; MEDIUM retained as scope decision.
- **Reason:** Ledger/SOA/report models lack currency fields.
- **Evidence:** `LedgerEntry` amounts have no currency; SOA DTOs lack currency.
- **Files Reviewed:** `PointOfSale.Core/Entities/Accounting/LedgerEntry.cs`; `PointOfSale.API/DTOs/StatementOfAccountDTOs.cs`.
- **Confidence:** High

### Finding ID: M-15 / F-08-09
- **Previous Assessment:** MEDIUM; retained earnings/profit carry-forward not implemented.
- **New Assessment:** VERIFIED; MEDIUM retained.
- **Reason:** Balance sheet reads raw equity balances and no retained earnings account/closing flow was found.
- **Evidence:** Balance sheet asset/liability/equity logic; account resolver includes opening balance equity but no retained earnings account.
- **Files Reviewed:** `PointOfSale.Infrastructure/Accounting/FinancialReportService.cs`; `PointOfSale.Core/Interfaces/Accounting/IAccountResolver.cs`.
- **Confidence:** High

### Finding ID: M-16 / F-06-03
- **Previous Assessment:** MEDIUM; webhook idempotency asymmetric because Stripe/Paymob have zero `PaymentWebhookLog` references.
- **New Assessment:** PARTIALLY VERIFIED; MEDIUM architecture consistency item.
- **Reason:** Stripe/Paymob payment services do use webhook logs. Heterogeneity remains across route/runtime logging and replay models.
- **Evidence:** Fawry, Stripe, and Paymob all have webhook-log usage in payment paths; CommerceHub paths use different logs/replay records.
- **Files Reviewed:** `PointOfSale.API/Controllers/FawryWebhookController.cs`; `PointOfSale.API/Services/Payments/Stripe/StripePaymentService.cs`; `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs`; `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`; `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs`.
- **Confidence:** High

### Finding ID: M-17 / CV-05
- **Previous Assessment:** MEDIUM; tenant read/write asymmetry in Expenses + integrity service.
- **New Assessment:** VERIFIED; MEDIUM retained and scope expanded.
- **Reason:** SOA, daily close, and some cashbox reads also rely primarily on EF global filters.
- **Evidence:** Financial reports explicitly filter tenant; expense, SOA, and daily-close reads lack explicit tenant predicates.
- **Files Reviewed:** `PointOfSale.Infrastructure/Accounting/FinancialReportService.cs`; `PointOfSale.API/Controllers/ExpensesController.cs`; `PointOfSale.API/Controllers/StatementOfAccountController.cs`; `PointOfSale.API/Controllers/ReportsController.cs`; `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`.
- **Confidence:** High

### Finding ID: M-18 / CV-07
- **Previous Assessment:** MEDIUM; two-tier party/GL decoupling boundary undocumented/inconsistently covered.
- **New Assessment:** VERIFIED; MEDIUM retained.
- **Reason:** Implementation confirms cash/card POS customer ledger entries are not AR-control postings while credit sales are.
- **Evidence:** `CommerceAccountingService` posting differences for cash/card vs credit and adjustments.
- **Files Reviewed:** `PointOfSale.Infrastructure/Accounting/CommerceAccountingService.cs`.
- **Confidence:** High

### Finding ID: M-19 / CV-08
- **Previous Assessment:** MEDIUM; party balance truth has three representations; rebuild repairs ledger/cache but not supplier mirror.
- **New Assessment:** VERIFIED; MEDIUM retained with corrected nuance.
- **Reason:** Source-of-truth split remains, but rebuild does recalculate existing `SupplierTransactions.BalanceAfter`.
- **Evidence:** AccountLedger authority, party cache maintenance, supplier aux mirror usage, rebuild recalculation of supplier transaction balances.
- **Files Reviewed:** `PointOfSale.Core/Entities/AccountLedger.cs`; `PointOfSale.API/Services/LedgerService.cs`; `PointOfSale.API/Controllers/StatementOfAccountController.cs`.
- **Confidence:** High

### Finding ID: M-20 / CV-09
- **Previous Assessment:** MEDIUM; inconsistent treatment of reversed/not-yet-posted across reporting surfaces.
- **New Assessment:** VERIFIED; MEDIUM retained.
- **Reason:** Financial reports, daily close, and expense reports use different conventions.
- **Evidence:** Ledger reports net reversed entries; daily close includes reversed source journals at face value; expense report reads operational pre-GL data.
- **Files Reviewed:** `PointOfSale.Infrastructure/Accounting/FinancialReportService.cs`; `PointOfSale.API/Controllers/ReportsController.cs`; `PointOfSale.API/Controllers/ExpensesController.cs`.
- **Confidence:** High

### Finding ID: M-21 / CV-11
- **Previous Assessment:** MEDIUM; accounting-period lock owned by no flow.
- **New Assessment:** VERIFIED; MEDIUM retained.
- **Reason:** No shared period-lock service was found and financial writes lack period checks.
- **Evidence:** Adjustment and expense write paths lack period checks; source search found no accounting-period service.
- **Files Reviewed:** `PointOfSale.API/Controllers/StatementOfAccountController.cs`; `PointOfSale.API/Controllers/ExpensesController.cs`; repository search for period-lock terms.
- **Confidence:** High

### Finding ID: M-22 / F-10-04
- **Previous Assessment:** LOW domain item folded into HIGH systemic validation gap.
- **New Assessment:** VERIFIED; LOW domain item folded into MEDIUM systemic validation gap.
- **Reason:** The negative `ActualCash` evidence is correct, but systemic severity was downgraded after adjustment/supplier-payment positive validation was verified.
- **Evidence:** `CloseCashboxRequest.ActualCash` unconstrained; close variance calculation accepts it.
- **Files Reviewed:** `PointOfSale.API/DTOs/CashboxDTOs.cs`; `PointOfSale.API/Controllers/CashboxController.cs`.
- **Confidence:** High

### Finding ID: M-23 / F-09-01
- **Previous Assessment:** LOW domain item folded into CV-08.
- **New Assessment:** VERIFIED; LOW retained and folded into CV-08.
- **Reason:** Balance-after/cached-balance asymmetry remains relevant to party-balance ownership cleanup.
- **Evidence:** Party balance cache and ledger running balance are separately maintained by `LedgerService`.
- **Files Reviewed:** `PointOfSale.API/Services/LedgerService.cs`; `PointOfSale.API/Controllers/StatementOfAccountController.cs`.
- **Confidence:** Medium

### Finding ID: L-05 / F-07-04
- **Previous Assessment:** LOW; canonical doc stale, no legacy writes, no journal references except noted read-only check.
- **New Assessment:** PARTIALLY VERIFIED; LOW retained with wording tightened.
- **Reason:** Implementation confirms no direct ledger writes, but there is one read-only `JournalEntries.AnyAsync` reference.
- **Evidence:** Expense controller writes expense state/outbox; one read-only journal existence check exists.
- **Files Reviewed:** `PointOfSale.API/Controllers/ExpensesController.cs`.
- **Confidence:** High

### Finding ID: L-09 / F-08-11
- **Previous Assessment:** LOW; `AccountSummary.IsTreasury` mapped from `Account.IsSystem` at one line, later line correctly reads `IsTreasury`.
- **New Assessment:** VERIFIED; LOW retained, correction strengthened.
- **Reason:** The later line copies the anonymous projection value, so it does not correct the earlier wrong projection.
- **Evidence:** Projection sets `IsTreasury = a.IsSystem`; DTO mapping copies projected `IsTreasury`.
- **Files Reviewed:** `PointOfSale.Infrastructure/Accounting/FinancialReportService.cs`.
- **Confidence:** High

### Finding ID: L-12 / F-11-12
- **Previous Assessment:** LOW; frontend missing idempotency key for adjustment creation.
- **New Assessment:** VERIFIED; LOW retained.
- **Reason:** Backend requires `Idempotency-Key`; frontend adjustment API does not send one.
- **Evidence:** Backend header validation; frontend `soaApi.createAdjustment` and page call omit key.
- **Files Reviewed:** `PointOfSale.API/Controllers/StatementOfAccountController.cs`; `PointOfSale.Web/src/services/soaApi.js`; `PointOfSale.Web/src/pages/Adjustments.jsx`; `PointOfSale.Web/src/services/api.js`.
- **Confidence:** High

### Finding ID: L-15 / CV-12
- **Previous Assessment:** LOW; durable webhook/audit idempotency asymmetry mirrors Fawry vs Stripe/Paymob pattern.
- **New Assessment:** PARTIALLY VERIFIED; LOW retained with corrected evidence.
- **Reason:** Asymmetry exists, but not because Stripe/Paymob categorically lack durable webhook logs.
- **Evidence:** Payment paths use webhook logs; CommerceHub/runtime paths use different logging/replay entities and indexes.
- **Files Reviewed:** `PointOfSale.API/Controllers/FawryWebhookController.cs`; `PointOfSale.API/Services/Payments/Stripe/StripePaymentService.cs`; `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs`; `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`; `PointOfSale.Core/Entities/StripeWebhookReplayRecord.cs`; `PointOfSale.Core/Entities/PaymobWebhookReplayRecord.cs`.
- **Confidence:** High

### Finding ID: AI-11 / Mutation Scope Strength
- **Previous Assessment:** Not separately called out in architecture improvements.
- **New Assessment:** VERIFIED architectural strength added as evidence, not a defect.
- **Reason:** Source review found stronger guardrails than the prior audit described.
- **Evidence:** `ApplicationDbContext` blocks unauthorized journal/ledger creation/mutation unless `LedgerMutationScope` is active.
- **Files Reviewed:** `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`; `PointOfSale.Infrastructure/Accounting/LedgerMutationScope.cs`; `PointOfSale.Infrastructure/Accounting/FinancialPostingService.cs`.
- **Confidence:** High

### Finding ID: AI-12 / Critical Outbox Retry Strength
- **Previous Assessment:** Durable outbox acknowledged, but expense/order conclusions ignored critical retry behavior.
- **New Assessment:** VERIFIED architectural strength added as evidence, not a defect.
- **Reason:** Critical accounting events are retryable and not terminally dropped, reducing severity of several async-divergence claims.
- **Evidence:** Critical event list includes POS, expenses, online payments, refunds, and settlements; critical failures remain non-terminal retryable.
- **Files Reviewed:** `PointOfSale.Infrastructure/Accounting/AccountingOutboxProcessor.cs`; `PointOfSale.Core/Entities/Accounting/AccountingOutboxMessage.cs`; `PointOfSale.Tests/Accounting/AccountingOutboxProcessorTests.cs`.
- **Confidence:** High

### Finding ID: Roadmap
- **Previous Assessment:** Immediate P0 fixes focused on order/payment reversal and expense integrity; recommended wiring `HandleFailureAsync`/`RecordRefundAsync` to `ReverseJournalAsync`.
- **New Assessment:** Roadmap re-prioritized around role authorization, expense integrity scan, supplier-payment reversal/void workflow, targeted validation, convergence visibility, and period lock.
- **Reason:** Order refund accounting already exists and collected-order cancellation is blocked/collapsed. The safer recommendation is incremental reuse of existing outbox/posting infrastructure.
- **Evidence:** Same as C-01, C-02, H-05, H-06, H-07, H-08.
- **Files Reviewed:** `ACCOUNTING-ARCHITECTURE-AUDIT.md`; implementation files listed above.
- **Confidence:** High

### Finding ID: Final Verdict
- **Previous Assessment:** Not approved for external audit/high-volume/mission-critical due to 2 CRITICAL/P0 items.
- **New Assessment:** No surviving CRITICAL accounting defect, but not externally audit-ready due to governance, reconciliation, audit-durability, period-lock, and convergence-visibility gaps.
- **Reason:** Payment CRITICAL findings were disproved/downgraded; remaining blockers are control and architecture-hardening items.
- **Evidence:** Same as C-01, C-02, H-04, H-05, H-06, M-04, M-08, M-21.
- **Files Reviewed:** `ACCOUNTING-ARCHITECTURE-AUDIT.md`; implementation files listed above.
- **Confidence:** High

---

## Files Reviewed

- `.ai/hermes/audits/ACCOUNTING-ARCHITECTURE-AUDIT.md`
- `.ai/hermes/accounts/FLOW-ACC-06-PAYMENTS.md`
- `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs`
- `PointOfSale.API/Services/Admin/AdminOrderService.cs`
- `PointOfSale.Infrastructure/Accounting/CommerceAccountingService.cs`
- `PointOfSale.Infrastructure/Accounting/AccountingOutboxService.cs`
- `PointOfSale.Infrastructure/Accounting/AccountingOutboxProcessor.cs`
- `PointOfSale.Infrastructure/Accounting/FinancialPostingService.cs`
- `PointOfSale.Infrastructure/Accounting/FinancialReportService.cs`
- `PointOfSale.Infrastructure/Accounting/LedgerMutationScope.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
- `PointOfSale.Infrastructure/Data/TenantSaveChangesInterceptor.cs`
- `PointOfSale.Infrastructure/Data/Configurations/AccountingOutboxMessageConfiguration.cs`
- `PointOfSale.Infrastructure/Data/Configurations/JournalEntryConfiguration.cs`
- `PointOfSale.API/Controllers/ExpensesController.cs`
- `PointOfSale.API/Controllers/StatementOfAccountController.cs`
- `PointOfSale.API/Controllers/SuppliersController.cs`
- `PointOfSale.API/Controllers/CashboxController.cs`
- `PointOfSale.API/Controllers/ReportsController.cs`
- `PointOfSale.API/Controllers/FinancialReportsController.cs`
- `PointOfSale.API/Controllers/FawryWebhookController.cs`
- `PointOfSale.API/Services/FinancialIntegrityService.cs`
- `PointOfSale.API/Services/LedgerService.cs`
- `PointOfSale.API/Services/Audit/AuditService.cs`
- `PointOfSale.API/Services/Audit/AuditQueue.cs`
- `PointOfSale.API/Services/Payments/Stripe/StripePaymentService.cs`
- `PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs`
- `PointOfSale.API/Services/CommerceHub/CommerceHubPaymentService.cs`
- `PointOfSale.API/Services/Affiliate/AffiliateService.cs`
- `PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs`
- `PointOfSale.API/DTOs/ExpenseDTOs.cs`
- `PointOfSale.API/DTOs/CashboxDTOs.cs`
- `PointOfSale.API/DTOs/StatementOfAccountDTOs.cs`
- `PointOfSale.Core/Entities/Expense.cs`
- `PointOfSale.Core/Entities/SupplierPayment.cs`
- `PointOfSale.Core/Entities/PartyLedgerAdjustment.cs`
- `PointOfSale.Core/Entities/AccountLedger.cs`
- `PointOfSale.Core/Entities/Accounting/LedgerEntry.cs`
- `PointOfSale.Core/Entities/Accounting/AccountingOutboxMessage.cs`
- `PointOfSale.Core/Models/Accounting/PostingRequest.cs`
- `PointOfSale.Core/Interfaces/Accounting/IAccountResolver.cs`
- `PointOfSale.Core/Entities/StripeWebhookReplayRecord.cs`
- `PointOfSale.Core/Entities/PaymobWebhookReplayRecord.cs`
- `PointOfSale.Web/src/services/soaApi.js`
- `PointOfSale.Web/src/pages/Adjustments.jsx`
- `PointOfSale.Web/src/services/api.js`
- `PointOfSale.Tests/Architecture/ExpenseFinancialSafetyHardeningTests.cs`
- `PointOfSale.Tests/Accounting/AccountingOutboxProcessorTests.cs`
- `PointOfSale.Tests/FinancialReportsControllerTenantScopeTests.cs`
- `PointOfSale.Tests/Architecture/LedgerIsolationTests.cs`
- `PointOfSale.Tests/SupplierFinancialIdempotencyTests.cs`

---

## External Review Suitability

The revised audit is suitable as an external ERP architecture review input because material unsupported CRITICAL findings were removed, severities now reflect implementation safeguards, and missing features are separated from implementation defects. It remains an architecture review, not a financial-audit certification.
