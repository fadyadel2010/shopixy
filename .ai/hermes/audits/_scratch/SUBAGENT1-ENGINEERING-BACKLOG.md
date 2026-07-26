# Sub-Agent 1 Engineering Backlog — Accounting / Finance / Payments / Treasury

**Domain:** Accounting, Finance, Payments, Treasury (Shopixy `.ai/hermes` knowledge base)
**Date:** 2026-07-21
**Source basis:** All `accounts/`, `audits/`, `architecture/`, `platform/`, `business-flows/`, `_scratch/`, `certification/`, and cross-cutting `0x-*.md` files.
**Exclusion rule:** Items marked **IMPLEMENTED** in `audits/ACCOUNTING-ARCHITECTURE-REMEDIATION-REPORT.md` are excluded (H-04/F-11-04, H-06/CV-03, H-02/F-07-01, H-05/CV-01, H-07/CV-04 incl. M-22/F-10-04, M-09/F-07-02, M-12/F-08-06). Items marked **FALSE POSITIVE** are excluded (C-01/F-06-02 collected-order cancellation; order-refund portion of C-02). PARTIALLY-IMPLEMENTED items are included with their remaining open work only. Strengths, completed work, and pure documentation-glossary notes are excluded unless they describe a real gap.

**Counts:** 71 issues (ACC-001 … ACC-071) + 13 documented conflicts (CONFLICT-001 … CONFLICT-013).

---

## ISSUES

### ACC-001: Supplier-payment void/reversal workflow missing
- Severity: High
- Category: Missing Feature
- Description: `SuppliersController` can create supplier payments, but there is no `ReverseSupplierPayment`, `CancelSupplierPayment`, or supplier-payment reversal outbox path. If a supplier payment must be operationally unwound, there is no first-class symmetric accounting path; the only remedy is GL `ReverseJournalAsync`, unwired to any party-payment endpoint.
- Recommendation: Add an explicit supplier-payment void/reversal command posting a reversal/compensating journal through the existing posting/outbox infrastructure (do not reuse the order-refund architecture).
- Complexity: L
- Dependencies: ACC-003 (period lock), ACC-018 (adjustment reversal pattern)
- Affected Modules: SuppliersController, CommerceAccountingService, AccountingOutboxProcessor
- Source Documents: audits/ACCOUNTING-ARCHITECTURE-AUDIT.md:99-105 (C-02/CV-02); audits/ACCOUNTING-ARCHITECTURE-REMEDIATION-REPORT.md:134-140; accounts/FLOW-ACC-06-PAYMENTS.md:120-179
- Evidence: "Supplier payment creation exists... but no ReverseSupplierPayment, CancelSupplierPayment, or supplier-payment reversal outbox path was found."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-002: Concurrent cashbox close race (no pessimistic/conditional update)
- Severity: Medium
- Category: Concurrency Concern
- Description: `CloseCashbox` reads/guards `IsActive` without a row lock or conditional `WHERE IsActive = true` update, so overlapping closures can both pass the guard, producing duplicate audit rows (no GL corruption, but operational/audit divergence).
- Recommendation: Add provider-aware conditional update or row-version `WHERE IsActive = true` with a 0-affected-rows = already-closed conflict response.
- Complexity: M
- Dependencies: none
- Affected Modules: CashboxController
- Source Documents: audits/ACCOUNTING-ARCHITECTURE-AUDIT.md:182-186 (M-02/F-10-01); audits/ACCOUNTING-ARCHITECTURE-REMEDIATION-REPORT.md:142-148; audits/_scratch/AGENT-10-DAILYCLOSING.md:32-49
- Evidence: "CloseCashbox starts a default transaction and reads/guards IsActive without row lock or conditional update."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-003: Cashbox closure audit is post-commit and non-atomic
- Severity: Medium
- Category: Auditability Gap
- Description: `CloseCashbox` commits state first, then awaits audit logging; `AuditService.EnqueueLog` persists immediately but falls back to an in-memory queue on failure. A closure can commit with no durable audit record.
- Recommendation: Persist the closure audit inside the close transaction or via a durable audit outbox.
- Complexity: M
- Dependencies: none
- Affected Modules: CashboxController, AuditService, AuditQueue
- Source Documents: audits/ACCOUNTING-ARCHITECTURE-AUDIT.md:194-199 (M-04/F-10-03); audits/ACCOUNTING-ARCHITECTURE-REMEDIATION-REPORT.md:150-156; audits/_scratch/AGENT-10-DAILYCLOSING.md:72-107
- Evidence: "Cashbox close commits state first, then awaits audit logging... non-durable fallback."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-004: No configurable party-balance floor policy for adjustments
- Severity: Medium
- Category: Business Rule Gap
- Description: `LedgerService.ApplyPartyTransactionAsync` computes running balance with no floor; `PreventNegative` only guards GL accounts, not party columns. Adjustments can drive a party balance arbitrarily negative.
- Recommendation: Define configurable floor rules per party/account type, then enforce in the party-mutation service (business-policy decision; implement only where required).
- Complexity: M
- Dependencies: none
- Affected Modules: LedgerService, PartyLedgerAdjustment
- Source Documents: audits/ACCOUNTING-ARCHITECTURE-AUDIT.md:201-205 (M-05/F-11-05); audits/ACCOUNTING-ARCHITECTURE-REMEDIATION-REPORT.md:158-164; audits/_scratch/AGENT-11-ADJUSTMENTS.md:108-119
- Evidence: "LedgerService computes running balance without a floor... GL PreventNegative guards only GL accounts, not party columns."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-005: Adjustments not linked to GL by stored journal id
- Severity: Medium
- Category: Reporting Gap
- Description: `PartyLedgerAdjustment` has no `JournalEntryId`; the GL link is reconstructed by `SourceType`/`SourceId`. Adds fragility to reconciliation and traceability.
- Recommendation: Store posted `JournalEntryId` on successful adjustment posting, or expose a derived link status without migrating historical rows.
- Complexity: M
- Dependencies: none
- Affected Modules: PartyLedgerAdjustment, FinancialIntegrityService, AccountingOutboxProcessor
- Source Documents: audits/ACCOUNTING-ARCHITECTURE-AUDIT.md:207-211 (M-06/F-11-06); audits/ACCOUNTING-ARCHITECTURE-REMEDIATION-REPORT.md:166-172; audits/_scratch/AGENT-11-ADJUSTMENTS.md:121-131
- Evidence: "PartyLedgerAdjustment has no JournalEntryId; link validation reconstructs by SourceType/SourceId."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-006: No adjustment approval/reversal/cancellation/deletion lifecycle
- Severity: Medium
- Category: Workflow Gap
- Description: `PartyLedgerAdjustment` has no status/lifecycle fields; the controller exposes create only. Adjustments are compensating entries with no operational undo.
- Recommendation: Add a small reversal/void workflow if adjustments can be operationally undone; otherwise document the compensating-entry policy explicitly.
- Complexity: L
- Dependencies: none
- Affected Modules: PartyLedgerAdjustment, StatementOfAccountController
- Source Documents: audits/ACCOUNTING-ARCHITECTURE-AUDIT.md:213-217 (M-07/F-11-07); audits/ACCOUNTING-ARCHITECTURE-REMEDIATION-REPORT.md:174-180; accounts/FLOW-ACC-11-ACCOUNT-ADJUSTMENTS.md:133-144,445
- Evidence: "PartyLedgerAdjustment has no status/lifecycle fields; adjustment controller exposes create but no lifecycle commands."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-007: No shared accounting-period / locked-period guard
- Severity: Medium
- Category: Governance Concern
- Description: No `IAccountingPeriodLock` or period service exists. Adjustments and expenses post without checking closed periods; reports read any date range. Period-lock ownership belongs to no flow.
- Recommendation: Add a shared accounting-period service invoked at all financial write paths (adjustments, expenses, supplier payments) and reject writes to closed periods.
- Complexity: L
- Dependencies: none
- Affected Modules: StatementOfAccountController, ExpensesController, FinancialPostingService, FinancialReportService, CashboxController
- Source Documents: audits/ACCOUNTING-ARCHITECTURE-AUDIT.md:219-223 (M-08/F-11-08),249-253 (M-13/F-08-07),298-302 (M-21/CV-11); audits/ACCOUNTING-ARCHITECTURE-REMEDIATION-REPORT.md:182-188,198-204
- Evidence: "No period-lock service was found; adjustments and expenses do not check closed periods before writes."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-008: Party-balance source-of-truth split (AccountLedger vs cache vs SupplierTransactions mirror)
- Severity: Medium
- Category: Architecture Issue
- Description: Party balance has three representations — `AccountLedger.RunningBalance` (authority), `Customer`/`Supplier.Balance` (cache), and `SupplierTransactions.BalanceAfter` (supplier auxiliary mirror). Supplier aux aggregates (`TotalPaid`/`TotalInvoiced`/`DueDate`) read from the mirror, unlike the customer path which uses `AccountLedgers`.
- Recommendation: Make `AccountLedger` the party display source and label/limit operational mirrors; finalize ownership cleanup as larger read-model work.
- Complexity: L
- Dependencies: none
- Affected Modules: AccountLedger, Customer/Supplier, SupplierTransactions, StatementOfAccountController
- Source Documents: audits/ACCOUNTING-ARCHITECTURE-AUDIT.md:176-180 (M-01/F-09-03),286-290 (M-19/CV-08),309-312 (M-23/F-09-01); audits/ACCOUNTING-ARCHITECTURE-REMEDIATION-REPORT.md:190-196
- Evidence: "Supplier balance is ledger-sourced, but TotalPaid/TotalInvoiced/DueDate use SupplierTransactions and PurchaseInvoices."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-009: Uneven async GL convergence visibility (no shared GL-converged signal)
- Severity: Medium
- Category: Monitoring Gap
- Description: POS sale posting is synchronous; expenses/adjustments post via async outbox. Order/refund posting statuses are updated, but expense posting lacks the same integrity scan/status exposure. Operators have no universal "outbox drained / GL converged" indicator across async financial sources.
- Recommendation: Standardize a minimal convergence contract (posted/failed/pending status or shared outbox-lag/GL-converged endpoint) for every async financial source.
- Complexity: L
- Dependencies: ACC-001
- Affected Modules: FinancialIntegrityService, Expense posting, AccountingOutboxProcessor, operator dashboards
- Source Documents: audits/ACCOUNTING-ARCHITECTURE-AUDIT.md:168-174 (H-08/CV-06),200-204 (H-08/CV-10); audits/ACCOUNTING-ARCHITECTURE-REMEDIATION-REPORT.md:198-204
- Evidence: "Convergence safeguards exist, but are inconsistent by flow."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-010: Split FLOW-ACC-06 ERP party payments from CommerceHub/order-payment runtime
- Severity: High
- Category: Documentation Inconsistency
- Description: `FLOW-ACC-06-PAYMENTS.md:6` explicitly scopes OUT the order-payment runtime (`PaymentLifecycleService`, gateway adapters, CommerceHub), yet the original audit and task file list used those runtimes for multiple FLOW-ACC-06 conclusions, mixing ERP party payments with order/CommerceHub payment runtime.
- Recommendation: Keep ERP party payments and order/CommerceHub payment runtime in separate canonical flows and audit sections; re-label existing mixed conclusions.
- Complexity: M
- Dependencies: ACC-013
- Affected Modules: Documentation (canonical flows)
- Source Documents: audits/ACCOUNTING-ARCHITECTURE-AUDIT.md:111-116 (H-01/F-06-01); audits/ACCOUNTING-ARCHITECTURE-REMEDIATION-REPORT.md:206-211; audits/_scratch/AGENT-06-PAYMENTS.md:10-35,41-67
- Evidence: "The original audit used those runtimes for multiple FLOW-ACC-06 conclusions."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ACC-011: Multi-currency not supported in core ledger/SOA/reports
- Severity: Medium
- Category: Missing Feature
- Description: `LedgerEntry` and `Account` carry only `decimal(18,4)` with no currency column; SOA/report DTOs carry no currency. Single-currency assumption pervades treasury, reports, and party ledgers.
- Recommendation: Treat as a target-market scope decision; implement only with a tenant currency model and FX policy. Do not implement unless multi-currency tenants are required.
- Complexity: XL
- Dependencies: none
- Affected Modules: LedgerEntry, Account, TreasuryAccount, FinancialReportService, AccountLedger, SOA
- Source Documents: audits/ACCOUNTING-ARCHITECTURE-AUDIT.md:255-259 (M-14/F-08-08),320 (L-03/F-09-05); accounts/FLOW-ACC-08-FINANCIAL-REPORTS.md:108,223; accounts/FLOW-ACC-09-STATEMENT-OF-ACCOUNT.md:77,188; accounts/FLOW-ACC-01-CHART-OF-ACCOUNTS.md:485
- Evidence: "LedgerEntry has decimal amounts without currency; SOA/report DTOs carry no currency."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-012: Retained earnings / profit carry-forward not implemented
- Severity: Medium
- Category: Missing Feature
- Description: No `RetainedEarnings` account template; P&L net profit is never closed into an equity account. Balance sheet equity contains only `OpeningBalanceEquity` (3010). No period/fiscal close.
- Recommendation: Add a year-end closing journal only when fiscal-year close is in target scope.
- Complexity: L
- Dependencies: ACC-007
- Affected Modules: SystemAccountTemplates, FinancialReportService, IAccountResolver
- Source Documents: audits/ACCOUNTING-ARCHITECTURE-AUDIT.md:261-265 (M-15/F-08-09); accounts/FLOW-ACC-01-CHART-OF-ACCOUNTS.md:244; accounts/FLOW-ACC-08-FINANCIAL-REPORTS.md:205,223
- Evidence: "No RetainedEarnings account in SystemAccountTemplates; P&L NetProfit is not closed."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-013: Webhook idempotency/logging is heterogeneous across routes
- Severity: Medium
- Category: Consistency Concern
- Description: Fawry persists a durable `PaymentWebhookLog` with `ProviderEventId` dedup; Stripe/Paymob controllers have zero `PaymentWebhookLog` references at the entry layer (rely downstream on lifecycle + outbox idempotency). Logging idempotency semantics differ by route/runtime (CommerceHub uses different replay records).
- Recommendation: Standardize webhook idempotency semantics and naming; persist a `PaymentWebhookLog` (with unique `ProviderEventId` per tenant+provider) at the entry of all webhook controllers.
- Complexity: M
- Dependencies: none
- Affected Modules: FawryWebhookController, StripeWebhooksController, PaymobPaymentService, CommerceHubPaymentService, AccountingOutbox
- Source Documents: audits/ACCOUNTING-ARCHITECTURE-AUDIT.md:267-272 (M-16/F-06-03),332 (L-15/CV-12); audits/ACCOUNTING-ARCHITECTURE-REMEDIATION-REPORT.md:227-232; audits/_scratch/AGENT-06-PAYMENTS.md:123-156
- Evidence: "StripeWebhooksController.cs has zero references to PaymentWebhookLog; FawryWebhookController persists it."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-014: Explicit tenant predicates missing on high-value read surfaces (partially done)
- Severity: Medium
- Category: Governance Concern
- Description: Remediation added explicit tenant predicates to expense/SOA/cashbox/report reads, but the broader pattern ("defense-in-depth tenant predicates on all non-accounting read surfaces") remains incomplete; several read surfaces still rely on global filters only.
- Recommendation: Continue the same explicit `TenantId == RequiredTenantId` predicate pattern across remaining high-value read surfaces while retaining global filters.
- Complexity: M
- Dependencies: none
- Affected Modules: ExpensesController, StatementOfAccountController, ReportsController, CashboxController, other read controllers
- Source Documents: audits/ACCOUNTING-ARCHITECTURE-AUDIT.md:231-235 (M-10/F-07-03),274-278 (M-17/CV-05); audits/ACCOUNTING-ARCHITECTURE-REMEDIATION-REPORT.md:96-106
- Evidence: "Defense-in-depth tenant predicates were uneven; remediation added them to high-value accounting surfaces only."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-015: Reversed/not-yet-posted reporting semantics differ across surfaces (partially fixed)
- Severity: Medium
- Category: Reporting Gap
- Description: Remediation fixed daily-close reversed-source subtotals, but reversal disclosure is still inconsistent across reporting surfaces (financial reports net posted+reversed; daily close now uses posted only; expense reports read operational expenses before GL convergence). Expense reports can still diverge from GL/P&L during async lag.
- Recommendation: Standardize reversal and pre-GL disclosure conventions across all reporting surfaces; label operational/pre-GL reports.
- Complexity: M
- Dependencies: none
- Affected Modules: ReportsController, FinancialReportService, ExpensesController
- Source Documents: audits/ACCOUNTING-ARCHITECTURE-AUDIT.md:188-192 (M-03/F-10-02),292-296 (M-20/CV-09),237-241 (M-11/F-07-05); audits/ACCOUNTING-ARCHITECTURE-REMEDIATION-REPORT.md:120-130
- Evidence: "Reversal journals use SourceType=Reversal; original SalesInvoice subtotal can remain counted while reversal is not part of that subtotal."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-016: Two-tier party/GL decoupling boundary under-documented
- Severity: Medium
- Category: Documentation Inconsistency
- Description: For POS cash/card sales, the customer `AccountLedger` carries Sale/PaymentReceived entries (net zero) but the GL posts no AR(1410) line — only revenue and cash. AR control (1410) != POS customer `AccountLedger` totals for cash/card. The decoupling is documented but inconsistently emphasized, risking false future findings and reconciliation mistakes.
- Recommendation: Explicitly document which party-ledger entries are informational vs GL-control-account-affecting; add a note in FLOW-ACC-06 that cash/card POS `AccountLedger` is informational.
- Complexity: S
- Dependencies: none
- Affected Modules: SalesController, CommerceAccountingService, AccountLedger, GL
- Source Documents: audits/ACCOUNTING-ARCHITECTURE-AUDIT.md:280-284 (M-18/CV-07); audits/_scratch/AGENT-06-PAYMENTS.md:193-222 (F-06-05)
- Evidence: "For a cash POS sale the customer AccountLedger still carries a Sale and PaymentReceived entry, but the GL contains no AccountsReceivable line."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ACC-017: Expense report can diverge from GL/P&L during async lag
- Severity: Medium
- Category: Reporting Gap
- Description: Expense operational report reads `Expenses` table; P&L reads `LedgerEntries`. Because expense posting is async, the operational expense report can diverge from the GL/P&L until convergence.
- Recommendation: Label the expense report as operational/pre-GL or expose GL-posted/converged state.
- Complexity: S
- Dependencies: ACC-009
- Affected Modules: ExpensesController, FinancialReportService
- Source Documents: audits/ACCOUNTING-ARCHITECTURE-AUDIT.md:237-241 (M-11/F-07-05); audits/_scratch/AGENT-07-EXPENSES.md:98-111
- Evidence: "Expense report reads operational Expenses; P&L reads ledger entries; expense posting is async."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: no

### ACC-018: No CRUD API / POS-Admin UI for canonical LedgerAccounts
- Severity: Medium
- Category: Missing Feature
- Description: Canonical `Account` (table `LedgerAccounts`) has no create/edit/delete/re-parent/toggle-active API or UI. Seeding is backend-only; `AccountCode`/`AccountType` immutable; deletion and re-parenting absent. Operators cannot manage the chart of accounts from POS Admin.
- Recommendation: Decide whether POS-Admin CoA management is in scope; if so, add a guarded CRUD + activation service; otherwise document the seed-only boundary firmly.
- Complexity: L
- Dependencies: none
- Affected Modules: Account, TenantAccountSeeder, AdminAccountingController
- Source Documents: accounts/FLOW-ACC-01-CHART-OF-ACCOUNTS.md:144-148,189-197,481-487
- Evidence: "Canonical LedgerAccounts has no CRUD API and no UI. Creation is seed-only; deletion absent."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-019: No deep-hierarchy / cycle detection for Chart of Accounts
- Severity: Low
- Category: Technical Debt
- Description: `Account.Level` is binary (1 = top, 2 = has-parent); deeper hierarchy (level 3+) is never computed. The no-cycle invariant is stated but not runtime-checked; enforcement relies on the seeder processing parents before children.
- Recommendation: If deep hierarchies are required, compute true depth and add a runtime cycle check; otherwise document the 2-level limit.
- Complexity: M
- Dependencies: none
- Affected Modules: Account, TenantAccountSeeder
- Source Documents: accounts/FLOW-ACC-01-CHART-OF-ACCOUNTS.md:193,205,485
- Evidence: "Level is binary... deeper hierarchy levels (3+) are never computed — no runtime cycle check exists."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-020: No soft-delete/archive state for canonical accounts
- Severity: Low
- Category: Missing Feature
- Description: Canonical `Account` has no `IsDeleted` and no delete/archive method. Deactivation (`IsActive`) exists but has no API/UI toggle. Inactive accounts are rejected at posting only.
- Recommendation: Add an explicit activate/deactivate API if operators need lifecycle control; document the absence otherwise.
- Complexity: S
- Dependencies: ACC-018
- Affected Modules: Account, AdminAccountingController
- Source Documents: accounts/FLOW-ACC-01-CHART-OF-ACCOUNTS.md:114,145,486
- Evidence: "Canonical Account has no IsDeleted and no delete method... flag implemented, toggle NOT implemented."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-021: Cash Flow Investing/Financing sections are empty placeholders
- Severity: Low
- Category: Reporting Gap
- Description: `GetCashFlowAsync` computes only Operating activities; `InvestingActivities` and `FinancingActivities` return empty lists (`:451-452`), so the cash-flow statement is incomplete.
- Recommendation: Populate investing/financing sections only if those activities exist for target tenants; otherwise mark explicitly as out of scope.
- Complexity: M
- Dependencies: none
- Affected Modules: FinancialReportService
- Source Documents: audits/ACCOUNTING-ARCHITECTURE-AUDIT.md:325 (L-08); accounts/FLOW-ACC-01-CHART-OF-ACCOUNTS.md:242; accounts/FLOW-ACC-08-FINANCIAL-REPORTS.md:195
- Evidence: "InvestingActivities and FinancingActivities are empty lists."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-022: Legacy FinancialAccount / FinancialTransaction shadow model coexists with authoritative ledger
- Severity: Medium
- Category: Architecture Issue
- Description: `FinancialAccount`→`FinancialAccounts` and `FinancialTransaction`→`FinancialTransactions` are a parallel/legacy structure with its own CRUD and soft-delete, separate from the authoritative `LedgerAccounts`/`LedgerEntries`. Dual-source-of-truth confusion risk.
- Recommendation: Treat as deprecated; remove or clearly label as legacy to avoid dual-source-of-truth confusion; migrate any needed capability into the canonical model.
- Complexity: L
- Dependencies: none
- Affected Modules: FinancialAccount, FinancialAccountsController, FinancialTransaction
- Source Documents: accounts/FLOW-ACC-01-CHART-OF-ACCOUNTS.md:30-34,88-90,487
- Evidence: "It is NOT the source of truth for balances... treated here as a parallel/legacy structure."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-023: No internal-balance-transfer entity / status lifecycle / cancel
- Severity: Low
- Category: Missing Feature
- Description: Internal Balance Transfer is materialized only as a `JournalEntry` (SourceType="TreasuryTransfer"); there is no `Transfer` entity, no `TransferStatus`, no history table, and no server-side cancel (only UI confirmation). Reversal is a generic ledger operation.
- Recommendation: If transfer auditability/lifecycle is required, add a transfer aggregate with status + cancel path; otherwise document the journal-as-source-of-truth design.
- Complexity: M
- Dependencies: ACC-006
- Affected Modules: TreasuryOperationService, TreasuryTransferService, FinancialAccountsController
- Source Documents: accounts/FLOW-ACC-03-INTERNAL-BALANCE-TRANSFER.md:64-68,123-125,228-231
- Evidence: "No Transfer entity / Transfers table... No transfer-specific outbox / event."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-024: No per-transfer approval / limits / dual control
- Severity: Low
- Category: Governance Concern
- Description: Transfers require only role authorization (Owner/Admin/Manager); there is no approval workflow, amount cap, or dual control. A non-`PreventNegative` source can be overdrawn at the ledger level (the transfer service only logs a warning, the engine only blocks `PreventNegative` accounts).
- Recommendation: Add per-transfer approval/limits if treasury controls require it; ensure non-PreventNegative overdraw is explicitly allowed or blocked by policy.
- Complexity: M
- Dependencies: none
- Affected Modules: TreasuryOperationService, FinancialAccountsController
- Source Documents: accounts/FLOW-ACC-03-INTERNAL-BALANCE-TRANSFER.md:197,228-231
- Evidence: "The FinancialAccountsController.Transfer only logs a warning if sourceBalance < amount — it does NOT block."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-025: Currency not modeled on internal balance transfer
- Severity: Low
- Category: Known Limitation
- Description: The transfer signature carries no currency parameter; currency is not threaded into transfer inputs (single-currency assumption). A `CurrencyReferenceTable` migration exists but is unused for transfers.
- Recommendation: Thread currency into transfer inputs if multi-currency is adopted (see ACC-011); otherwise accept as single-currency limitation.
- Complexity: S
- Dependencies: ACC-011
- Affected Modules: TreasuryOperationService, TreasuryTransferService
- Source Documents: accounts/FLOW-ACC-03-INTERNAL-BALANCE-TRANSFER.md:83-84
- Evidence: "No currency parameter on TransferAsync... Currency reference table exists in migrations but is not threaded into transfer inputs."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-026: No journal create/edit/delete/archive/cancel/restore API or UI
- Severity: Medium
- Category: Missing Feature
- Description: The GL has no create/edit/delete/archive/cancel/restore surface. All journals are immutable; only reversal (new journal) undoes. There is no manual journal entry form.
- Recommendation: If manual GL entries are required (e.g., adjusting entries), add a guarded journal-entry path through `FinancialPostingService`; otherwise document the outbox/treasury/internal-only posting boundary.
- Complexity: L
- Dependencies: none
- Affected Modules: FinancialPostingService, IFinancialPostingService, controllers
- Source Documents: accounts/FLOW-ACC-04-GENERAL-LEDGER.md:169-172,434,544-550
- Evidence: "No journal create/edit/delete/archive/cancel/restore UI or API."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-027: No journal list / search / filter / paging HTTP surface
- Severity: Medium
- Category: Reporting Gap
- Description: `IFinancialPostingService` exposes only get-by-id / by-idempotency-key / exists. There is no journal list/search/filter/paging endpoint; the only GL query surface is per-account General Ledger and aggregate reports.
- Recommendation: Add a guarded journal query/search API (filtered by tenant, source, status, date) if operators need GL browsing/audit.
- Complexity: M
- Dependencies: none
- Affected Modules: IFinancialPostingService, controllers
- Source Documents: accounts/FLOW-ACC-04-GENERAL-LEDGER.md:230,459,546
- Evidence: "There is no journal list/search/filter/paging endpoint... IFinancialPostingService exposes only get-by-id, get-by-idempotency-key, exists."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-028: No event-sourced projection store for GL / statements recomputed per request
- Severity: Medium
- Category: Performance Concern
- Description: Financial statements are recomputed from `LedgerEntries` on every request; `GetOrCreateCachedAsync` is a no-op pass-through (30s memory cache only). No EventStore, no projection rebuild, no materialized statement table.
- Recommendation: Add a materialized/cached statement projection if report latency/load becomes a concern; otherwise accept per-request recompute (currently correct).
- Complexity: L
- Dependencies: none
- Affected Modules: FinancialReportService, FinancialReportsController
- Source Documents: accounts/FLOW-ACC-04-GENERAL-LEDGER.md:315,334,547; accounts/FLOW-ACC-01-CHART-OF-ACCOUNTS.md:315,479
- Evidence: "FinancialReportService recomputes from LedgerEntries on every request; GetOrCreateCachedAsync is a no-op pass-through."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-029: Customer GL posting of opening balance is missing
- Severity: Medium
- Category: Bug
- Description: `PostCustomerOpeningBalanceAsync` does not exist anywhere in the codebase (search returns 0). Customer opening balances are written only to the `AccountLedger` sub-ledger, never reaching the GL.
- Recommendation: Implement `PostCustomerOpeningBalanceAsync` (mirror of `PostSupplierOpeningBalanceAsync`) to post customer opening balances to the GL, or document that customer opening balances are sub-ledger only.
- Complexity: M
- Dependencies: none
- Affected Modules: CustomersController, LedgerService, CommerceAccountingService
- Source Documents: accounts/FLOW-ACC-05-OPENING-BALANCE.md:106
- Evidence: "Customer GL posting of opening balance — PostCustomerOpeningBalanceAsync does not exist anywhere in the codebase (search returns 0)."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-030: No OpeningBalance entity / edit / reverse / delete
- Severity: Low
- Category: Missing Feature
- Description: There is no `OpeningBalance` entity/service; opening balances are persisted as `AccountLedger` rows. Once written they are immutable (no update), with no reverse/recreate/reset/delete path.
- Recommendation: If mutable opening balances are needed, add guarded edit/reverse paths; otherwise document immutability + adjustment-only correction.
- Complexity: M
- Dependencies: none
- Affected Modules: CustomersController, LedgerService
- Source Documents: accounts/FLOW-ACC-05-OPENING-BALANCE.md:104-110,212
- Evidence: "No update path; once written it is immutable... No method, no endpoint, no state for reset/reverse/delete."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-031: Standalone customer/supplier payment collection endpoints not implemented
- Severity: Medium
- Category: Missing Feature
- Description: Frontend `soaApi.js` calls `/Payments/receive` and `/Payments/make`, but no backend routes exist for standalone customer/supplier payment collection. Party payments are created only inline via `SalesController`/`SuppliersController`.
- Recommendation: Either implement the standalone collection endpoints or remove the dead frontend references; align UI and API contract.
- Complexity: M
- Dependencies: none
- Affected Modules: Payments (frontend/backend contract)
- Source Documents: accounts/FLOW-ACC-06-PAYMENTS.md:71-77,145,334
- Evidence: "Standalone customer-payment endpoint /Payments/receive — no backend route; frontend calls it."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-032: No party payment allocation / settlement / voucher
- Severity: Medium
- Category: Missing Feature
- Description: Invoice-level party payment allocation (to `PurchaseInvoiceId`/`SalesInvoiceId`) is NOT IMPLEMENTED — payments are account-level. Party settlement and `ReceiptVoucher`/`PaymentVoucher` numbered documents are also absent (only a free-text `ReferenceNumber`).
- Recommendation: Implement allocation/settlement/voucher generation if required for ERP party-payment completeness; otherwise document as out-of-scope.
- Complexity: L
- Dependencies: none
- Affected Modules: SuppliersController, StatementOfAccountController, Payments
- Source Documents: accounts/FLOW-ACC-06-PAYMENTS.md:74-77,120-121,160-161,175-179,225-226
- Evidence: "Payment Allocation (party/invoice): NOT IMPLEMENTED... Settlement (party): NOT IMPLEMENTED... ReceiptVoucher/PaymentVoucher: NOT IMPLEMENTED."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-033: No direct expense reversal / void endpoint
- Severity: Low
- Category: Missing Feature
- Description: There is no expense-level id/credit/debit reversal or void route; reversal is handled indirectly via outbox mutation (update/delete/reject enqueue `ExpenseReversal`). Operator clarity gap, not an integrity failure.
- Recommendation: Add an explicit expense reversal/void endpoint for operator clarity if desired (current reversal mechanics are acceptable).
- Complexity: S
- Dependencies: none
- Affected Modules: ExpensesController
- Source Documents: accounts/FLOW-ACC-07-EXPENSES.md:63-66,131; audits/_scratch/AGENT-07-EXPENSES.md:113-122
- Evidence: "Expense-level id/credit/debit reversal endpoint — no reverse/void route."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-034: No system-generated expense number (ReferenceNumber free-text, non-unique)
- Severity: Low
- Category: Known Limitation
- Description: `ReferenceNumber` is a nullable free-text input provided by the caller; there is no system-generated expense number. This weakens audit traceability.
- Recommendation: Add a server-generated expense number (sequence) if audit traceability requires it.
- Complexity: S
- Dependencies: none
- Affected Modules: Expense, ExpenseDTOs, ExpensesController
- Source Documents: accounts/FLOW-ACC-07-EXPENSES.md:68,95; audits/_scratch/AGENT-07-EXPENSES.md:122-132
- Evidence: "ReferenceNumber is a free-text nullable input... no system-generated expense number."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-035: No expense receipt upload / attachment model / archive
- Severity: Low
- Category: Missing Feature
- Description: Expense receipt upload has no `IFormFile` handler/route (`ReceiptPath` string only). No separate attachment model, no expense archive/restore.
- Recommendation: Implement receipt upload + attachment entity if expense documentation compliance is required.
- Complexity: M
- Dependencies: none
- Affected Modules: ExpensesController, Expense
- Source Documents: accounts/FLOW-ACC-07-EXPENSES.md:63-66
- Evidence: "Expense Receipt upload endpoint — no IFormFile handler / no upload route."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-036: No per-category / per-supplier expense sub-accounts
- Severity: Low
- Category: Missing Feature
- Description: All expenses post to the single `Expenses` (6100) account; there are no per-category/per-supplier sub-accounts, reducing analytic granularity.
- Recommendation: Add sub-account routing if expense analytics require it.
- Complexity: M
- Dependencies: ACC-018
- Affected Modules: CommerceAccountingService, ExpenseCategory
- Source Documents: audits/_scratch/AGENT-07-EXPENSES.md:174-175; accounts/FLOW-ACC-07-EXPENSES.md:95
- Evidence: "Per-category / per-supplier sub-accounts — NOT IMPLEMENTED (all post to Expenses 6100)."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-037: Statement of Account export / print not implemented
- Severity: Low
- Category: Missing Feature
- Description: No `StatementExport`/`StatementPdf`/`StatementExcel` anywhere; SOA has no print button. Aging report has print/CSV but SOA does not.
- Recommendation: Implement server-side export (PDF/Excel/CSV) and/or print if customer-facing statements are required.
- Complexity: M
- Dependencies: none
- Affected Modules: StatementOfAccountController, SOA frontend
- Source Documents: accounts/FLOW-ACC-09-STATEMENT-OF-ACCOUNT.md:59-60,79,109-111,295,334-335
- Evidence: "StatementExport / StatementPdf / StatementExcel — no code anywhere."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-038: SOA aging buckets / grouping not in query
- Severity: Low
- Category: Reporting Gap
- Description: The SOA query has no group-by; aging buckets are a separate `ReportsController` endpoint. Grouping inside SOA is NOT IMPLEMENTED.
- Recommendation: Document that aging is a separate report; optionally fold aging into SOA if UX requires.
- Complexity: S
- Dependencies: none
- Affected Modules: StatementOfAccountController, ReportsController
- Source Documents: accounts/FLOW-ACC-09-STATEMENT-OF-ACCOUNT.md:146,334; audits/_scratch/AGENT-09-SOA.md:123-139
- Evidence: "Grouping: NOT IMPLEMENTED in SOA query... aging report groups by bucket but is a separate endpoint."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: no

### ACC-039: Statement Of Account GET reads not individually audited
- Severity: Low
- Category: Auditability Gap
- Description: `IAuditService.LogCreate` is called only on adjustment writes; GET endpoints (`/query`, balance, outstanding) have no audit call. Read access is not individually recorded.
- Recommendation: Add read-access audit logging if compliance requires it.
- Complexity: S
- Dependencies: none
- Affected Modules: StatementOfAccountController
- Source Documents: accounts/FLOW-ACC-09-STATEMENT-OF-ACCOUNT.md:188; audits/_scratch/AGENT-09-SOA.md:43-59
- Evidence: "GET endpoints have no audit call."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-040: Customer adjustment mirror balance sourced from cached column
- Severity: Low
- Category: Technical Debt
- Description: Customer adjustment `BalanceAfter` is read from `customer!.Balance` (cache) while the supplier branch uses `ApplySupplierTransaction` return; both ultimately route through `ApplyPartyTransactionAsync` and are equal in practice, but the asymmetry is a maintenance smell.
- Recommendation: Standardize the balance-after sourcing path across party types.
- Complexity: S
- Dependencies: ACC-008
- Affected Modules: StatementOfAccountController, LedgerService
- Source Documents: accounts/FLOW-ACC-09-STATEMENT-OF-ACCOUNT.md:309; audits/_scratch/AGENT-09-SOA.md:23-39
- Evidence: "balanceAfter = customer!.Balance (customer branch) vs supplier branch uses ApplySupplierTransaction return."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-041: SOA sign masked with Math.Abs
- Severity: Low
- Category: Known Limitation
- Description: SOA masks sign with `Math.Abs` on opening/closing/balance and returns `BalanceType`; acceptable if UI always renders direction, but a documentation/UX dependency.
- Recommendation: Ensure UI consistently renders direction; document the sign-masking contract.
- Complexity: S
- Dependencies: none
- Affected Modules: StatementOfAccountController
- Source Documents: accounts/FLOW-ACC-09-STATEMENT-OF-ACCOUNT.md:319; audits/_scratch/AGENT-09-SOA.md:83-99
- Evidence: "SOA masks sign with Math.Abs and returns BalanceType."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: no

### ACC-042: Daily Closing — no shift / business-day entity
- Severity: Low
- Category: Missing Feature
- Description: There is no `Shift` type, no open/close shift, no shift-operator scoping. Cashbox is opened/closed by a user; the business "day" is a query window only.
- Recommendation: Add a shift/business-day aggregate if per-shift accountability is required.
- Complexity: M
- Dependencies: none
- Affected Modules: Cashbox, CashboxController
- Source Documents: accounts/FLOW-ACC-10-DAILY-CLOSING.md:120,139,266,473
- Evidence: "No Shift type, no open/close shift, no shift operator scoping."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-043: Daily Closing — no approval workflow
- Severity: Low
- Category: Governance Concern
- Description: Close is `[Authorize]` only; there is no approval entity, role-gated approval, or approval state machine. Closing completes within a single actor's request.
- Recommendation: Add a second-actor approval step if financial controls require it.
- Complexity: M
- Dependencies: none
- Affected Modules: CashboxController
- Source Documents: accounts/FLOW-ACC-10-DAILY-CLOSING.md:129,145,217,267
- Evidence: "Approval → NOT IMPLEMENTED. Closing does not require a second actor."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-044: Daily Closing — no reopen / lock / archive
- Severity: Low
- Category: Missing Feature
- Description: No reopen endpoint (re-open only via `open`), no treasury/cashbox/business-day lock, no archive concept (only soft-delete gated on no open transactions).
- Recommendation: Add lock/reopen/archive controls if period-control is required (ties to ACC-007 period lock).
- Complexity: M
- Dependencies: ACC-007
- Affected Modules: CashboxController
- Source Documents: accounts/FLOW-ACC-10-DAILY-CLOSING.md:171-175,266-267
- Evidence: "Reopen — NOT IMPLEMENTED... Lock — NOT IMPLEMENTED... Archive — NOT IMPLEMENTED."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-045: Daily Closing — no automated reconciliation / discrepancy alerting
- Severity: Architectural Improvement
- Category: Monitoring Gap
- Description: `discrepancyDetected` is computed and recorded in the audit only; nothing downstream consumes it. No suspense-account handling, alert, or event on discrepancy.
- Recommendation: Add discrepancy alert/event/suspense-account handling if business process requires formal discrepancy management.
- Complexity: M
- Dependencies: none
- Affected Modules: CashboxController, ReportsController
- Source Documents: accounts/FLOW-ACC-10-DAILY-CLOSING.md:238,266; audits/_scratch/AGENT-10-DAILYCLOSING.md:111-128
- Evidence: "discrepancyDetected is computed... returned/audited; nothing else consumes it."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-046: Daily Closing — no closing export / domain events / outbox
- Severity: Low
- Category: Missing Feature
- Description: Closing has print only (no export endpoint); no closing/reconciliation domain events or outbox message is emitted in the close path.
- Recommendation: Add closing export and/or a closing event if downstream systems need notification.
- Complexity: S
- Dependencies: none
- Affected Modules: CashboxController
- Source Documents: accounts/FLOW-ACC-10-DAILY-CLOSING.md:151,293-299
- Evidence: "Closing export endpoint — NOT IMPLEMENTED (print only)... Closing Events: NOT IMPLEMENTED."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-047: Daily Closing — no adjustment in close path
- Severity: Low
- Category: Known Limitation
- Description: A discrepancy is recorded in audit but the ledger is untouched at close; there is no in-close adjustment/correction path. By design (close is read-only reconciliation).
- Recommendation: Document the read-only close contract; add a separate adjustment flow (see ACC-006) if corrections must originate from close.
- Complexity: S
- Dependencies: ACC-006
- Affected Modules: CashboxController
- Source Documents: accounts/FLOW-ACC-10-DAILY-CLOSING.md:238,470
- Evidence: "Adjustment: NOT IMPLEMENTED in the closing path. A discrepancy is recorded in audit."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ACC-048: Adjustments — no AdjustmentReason enum / write-off / reclassification
- Severity: Medium
- Category: Missing Feature
- Description: `Notes` is the only free-text reason; no `AdjustmentReason` enum/entity. No `WriteOff`/write-off transaction type (enum has no member). No reclassification endpoint/method/enum.
- Recommendation: Add structured reason codes, write-off, and reclassification if adjustment semantics require them.
- Complexity: M
- Dependencies: none
- Affected Modules: PartyLedgerAdjustment, StatementOfAccountController
- Source Documents: accounts/FLOW-ACC-11-ACCOUNT-ADJUSTMENTS.md:143,215,227,445
- Evidence: "There is NO AdjustmentReason enum/entity... No WriteOff... No reclassification endpoint."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-049: Adjustments — no dedicated history/list/audit UI
- Severity: Architectural Improvement
- Category: Reporting Gap
- Description: `Adjustments.jsx` shows only the live party balance and a create modal; there is no adjustment list/grid/history table rendered. Server-side audit exists via `IAuditService` only.
- Recommendation: Add a read-only adjustment history endpoint/UI if operators need adjustment review.
- Complexity: M
- Dependencies: none
- Affected Modules: StatementOfAccountController, Adjustments.jsx
- Source Documents: accounts/FLOW-ACC-11-ACCOUNT-ADJUSTMENTS.md:369,445; audits/_scratch/AGENT-11-ADJUSTMENTS.md:194-201
- Evidence: "Adjustments.jsx shows no adjustment list/grid; only the live party balance."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-050: Frontend does not send required Idempotency-Key for adjustment creation
- Severity: Low
- Category: Bug
- Description: The server requires an `Idempotency-Key` header for `CreateAdjustment` (`:539-546`), but the shipped frontend `soaApi.createAdjustment` does not provide one, so the endpoint is unusable from the UI without a key.
- Recommendation: Update the frontend to supply the idempotency key, or relax the requirement if the UI cannot.
- Complexity: S
- Dependencies: none
- Affected Modules: StatementOfAccountController, soaApi.js, Adjustments.jsx
- Source Documents: accounts/FLOW-ACC-11-ACCOUNT-ADJUSTMENTS.md:363; audits/_scratch/AGENT-11-ADJUSTMENTS.md:183-192
- Evidence: "Backend requires Idempotency-Key for adjustment creation; frontend soaApi.createAdjustment does not provide one."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-051: Stale "COMMENTED legacy writes" label in expenses canonical doc
- Severity: Low
- Category: Documentation Inconsistency
- Description: FLOW-ACC-07 describes controller legacy writes as "commented"; in reality the controller is fully migrated with no commented-out legacy write code (only one read-only `JournalEntries.AnyAsync` at `:548` exists). The label no longer describes reality.
- Recommendation: Update FLOW-ACC-07 to remove the stale "commented legacy writes" phrasing.
- Complexity: S
- Dependencies: none
- Affected Modules: Documentation (FLOW-ACC-07)
- Source Documents: audits/_scratch/AGENT-07-EXPENSES.md:84-96; audits/_scratch/AGENT-FALSE-POSITIVE.md:41,148
- Evidence: "The 'COMMENTED legacy writes' claim is STALE — the controller is already fully migrated with no legacy code."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ACC-052: AccountSummary.IsTreasury mapped from Account.IsSystem (partial mismatch)
- Severity: Low
- Category: Bug
- Description: In `FinancialReportService`, one projection branch maps `IsTreasury = a.IsSystem` (`:576`) while another correctly reads `account.IsTreasury` (`:589`). The mismatch is partial (only one branch), causing inconsistent treasury flags in reports.
- Recommendation: Always source `IsTreasury` from `Account.IsTreasury`; remove the `IsSystem` derivation branch.
- Complexity: S
- Dependencies: none
- Affected Modules: FinancialReportService
- Source Documents: audits/ACCOUNTING-ARCHITECTURE-AUDIT.md:326 (L-09); audits/_scratch/AGENT-08-REPORTS.md:161-168; audits/_scratch/AGENT-FALSE-POSITIVE.md:73,149
- Evidence: "IsTreasury = a.IsSystem (in one projection branch) — real. BUT :589 IsTreasury = account.IsTreasury reads the account's own IsTreasury field (correct)."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-053: Report parameters not persisted for audit
- Severity: Low
- Category: Auditability Gap
- Description: Reports embed `GeneratedAtUtc`/`TenantId` but there is no separate report-request audit store; the controller logs only on exception. Report access parameters are not persisted for audit.
- Recommendation: Persist report-access audit records if compliance requires it.
- Complexity: S
- Dependencies: none
- Affected Modules: FinancialReportsController, FinancialReportService
- Source Documents: audits/ACCOUNTING-ARCHITECTURE-AUDIT.md:327 (L-10); accounts/FLOW-ACC-08-FINANCIAL-REPORTS.md:113
- Evidence: "No report-level audit log/store — the ledger itself is the audit trail."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-054: GL opening-balance boundary uses AddDays(-1) (fragile)
- Severity: Architectural Improvement
- Category: Known Limitation
- Description: `FinancialReportService` uses `fromDate.AddDays(-1)` for the GL opening-balance predicate (half-open interval). Correct but fragile; depends on `JournalDate` being stored as `.Date`.
- Recommendation: Use an explicit half-open `JournalDate < fromDate` predicate instead of `AddDays(-1)` to avoid off-by-one fragility.
- Complexity: S
- Dependencies: none
- Affected Modules: FinancialReportService
- Source Documents: audits/_scratch/AGENT-08-REPORTS.md:183-191; accounts/FLOW-ACC-08-FINANCIAL-REPORTS.md:236
- Evidence: "GL opening-balance uses fromDate.AddDays(-1) (boundary subtlety, safe)."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-055: Chargeback not handled in storefront PaymentLifecycleService
- Severity: Medium
- Category: Missing Feature
- Description: Storefront `PaymentLifecycleService` has no chargeback transition method. Chargeback enums/runtimes exist only in the CommerceHub bounded context. A storefront order chargeback would have no lifecycle/accounting handling in the order-payment runtime.
- Recommendation: Add storefront chargeback handling (status transition + accounting reversal) or explicitly document that storefront chargebacks are out of scope and routed via CommerceHub.
- Complexity: L
- Dependencies: ACC-001
- Affected Modules: PaymentLifecycleService, CommerceHub chargeback runtimes
- Source Documents: _scratch/order-payment-financial.md:44
- Evidence: "Chargeback: NOT handled in the storefront PaymentLifecycleService. Grep for chargeback returns nothing."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-056: Two RecordRefundAsync APIs coexist (storefront vs CommerceHub)
- Severity: Low
- Category: Architecture Issue
- Description: `IPaymentLifecycleService.RecordRefundAsync` (storefront) and `ICommerceHubFinancialLedgerService.RecordRefundAsync` (CommerceHub) are different methods on different services. Conflating them risks incorrect refund-accounting wiring.
- Recommendation: Clearly separate and document the two refund APIs; ensure each routes to its correct posting path.
- Complexity: S
- Dependencies: none
- Affected Modules: PaymentLifecycleService, CommerceHubFinancialLedgerService
- Source Documents: _scratch/order-payment-financial.md:138,170
- Evidence: "Two RecordRefundAsync APIs coexist (storefront vs CommerceHub); this report concerns the storefront one."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ACC-057: No dedicated overpayment suspense category
- Severity: Low
- Category: Known Limitation
- Description: Any provider-amount divergence (over- or under-payment) is treated uniformly as `AmountMismatch` and routed to the generic `UnresolvedOnlinePayment` suspense. Over-vs-under is not distinguished; no dedicated overpayment suspense bucket.
- Recommendation: Distinguish overpayment from underpayment in suspense routing if reconciliation needs it.
- Complexity: S
- Dependencies: none
- Affected Modules: PaymentLifecycleService, AccountingOutbox
- Source Documents: _scratch/order-payment-financial.md:164
- Evidence: "There is no explicit 'overpayment' category... over-vs-under is not distinguished in code."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-058: No negative-amount guard on OrderRefund.Amount (entity level)
- Severity: Low
- Category: Validation Gap
- Description: `OrderRefund.Amount` is a plain auto-property with no negative-amount guard on the entity itself. Line amounts are DB-constrained non-negative (`CK_OrderRefundLines_Amount_NonNegative`), and `RecordRefundAsync` rejects zero/negative, but the entity field is unprotected if constructed directly.
- Recommendation: Add an entity-level guard/validation on `OrderRefund.Amount`; rely on the existing DB constraint + service validation as the primary controls.
- Complexity: S
- Dependencies: none
- Affected Modules: OrderRefund, PaymentLifecycleService
- Source Documents: _scratch/order-payment-financial.md:123,132
- Evidence: "No negative-amount guard on OrderRefund.Amount itself (plain auto-property)."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-059: Affiliate accounting under-documented and internally inconsistent
- Severity: Medium
- Category: Documentation Inconsistency
- Description: The financial architecture doc says affiliate earned commissions are only an affiliate-ledger liability with payout handled by the orchestrator, but implementation also has MassTransit `AffiliateCommissionEarnedAccountingConsumer` and `AffiliateCommissionPaidAccountingConsumer` that post GL journals. This is a doc-vs-implementation contradiction plus a dual-posting risk.
- Recommendation: Reconcile the canonical doc with the MassTransit consumers; document exact production ownership to avoid double posting; bridge with `AffiliateSettlementOrchestrator`.
- Complexity: M
- Dependencies: ACC-060
- Affected Modules: Affiliate consumers, AffiliateSettlementOrchestrator, GL
- Source Documents: audits/financial-completeness-audit.md:33,140-159,465,489
- Evidence: "Document says affiliate earned commissions are only an affiliate ledger liability... implementation also has MassTransit consumers which post GL journals."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ACC-060: Documented financial flows/authorities/state-machines missing from architecture doc
- Severity: Medium
- Category: Documentation Inconsistency
- Description: `financial-architecture.md` omits or under-covers several real flows/authorities: party `AccountLedger` direct mutation via `LedgerService`, `TreasuryTransferService` posting paths, affiliate MassTransit accounting consumers, sale/purchase/expense reversal events, manual payment/wallet review, CommerceHub payment lifecycle authority, and disabled/fail-closed paths (cashbox non-zero opening rejected, bank opening disabled, inventory-adjustment accounting disabled).
- Recommendation: Augment the architecture doc with a "Known Direct Financial Mutations Outside GL" section and the missing authorities/events per the completeness-audit recommendations.
- Complexity: L
- Dependencies: ACC-059
- Affected Modules: Documentation (financial-architecture.md)
- Source Documents: audits/financial-completeness-audit.md:51,69-85,91-216,475-492
- Evidence: "Dual-ledger mental model risk — GL ledger and AccountLedger party ledger coexist. Documentation that only mentions GL will mislead."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ACC-061: Party balance drift risk (AccountLedger vs GL outbox divergence)
- Severity: Medium
- Category: Risk
- Description: `LedgerService`/`AccountLedger` party-balance updates and GL outbox posting can diverge if one succeeds and the other fails (two-phase: synchronous party row + async GL). This is the same exposure as adjustment divergence (ACC-005/ACC-009) but broader across all party-ledger flows.
- Recommendation: Add reconciliation/integrity scans covering all party-ledger ↔ GL pairs; surface convergence status.
- Complexity: L
- Dependencies: ACC-005, ACC-009
- Affected Modules: LedgerService, AccountLedger, AccountingOutbox
- Source Documents: audits/financial-completeness-audit.md:464,85
- Evidence: "Party balance drift risk — AccountLedger/Customer/Supplier balance update and GL outbox posting can diverge if one succeeds and the other fails."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-062: Discount/tax accounting opacity
- Severity: Low
- Category: Known Limitation
- Description: Discounts/coupons affect money but lack a traced accounting account in the reviewed documentation; tax handling accounting trace is incomplete in the canonical doc.
- Recommendation: Document the discount/tax GL accounts and posting lines explicitly.
- Complexity: S
- Dependencies: ACC-060
- Affected Modules: CommerceAccountingService, financial-architecture doc
- Source Documents: audits/financial-completeness-audit.md:469
- Evidence: "Discount/tax/accounting opacity risk — discounts/coupons affect money but lack a traced accounting account."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ACC-063: Inventory-adjustment accounting disabled but documented as present
- Severity: Low
- Category: Documentation Inconsistency
- Description: The accounting method for inventory valuation/adjustment is present in code but disabled; coverage must not imply operational support. Operators may expect inventory-adjustment accounting to be active.
- Recommendation: Document the disabled/fail-closed state explicitly in the canonical doc.
- Complexity: S
- Dependencies: ACC-060
- Affected Modules: Inventory accounting, financial-architecture doc
- Source Documents: audits/financial-completeness-audit.md:469,487
- Evidence: "Inventory adjustment gap — accounting method is present but disabled; inventory valuation coverage must not imply operational support."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ACC-064: CommerceHub finance vs tenant accounting boundary must stay separate
- Severity: Low
- Category: Governance Concern
- Description: CommerceHub maintains its own `CommerceHubFinancialTransaction` store, reconciled into the tenant GL via `CommerceHubLedgerPosting`. Vendor earnings/COD debt/settlement/payout readiness are marketplace finance evidence, NOT tenant accounting truth. Risk of conflating the two surfaces.
- Recommendation: Keep the separation explicit in all docs/UI; never present CommerceHub finance as tenant treasury. (Certification already asserts this; reinforce in code/UI labels.)
- Complexity: S
- Dependencies: none
- Affected Modules: CommerceHubFinancialLedgerService, tenant GL, Vendor Module
- Source Documents: certification/02-pos-admin-commercehub.md:84-88,128; accounts/FLOW-ACC-01-CHART-OF-ACCOUNTS.md:317,459
- Evidence: "CommerceHub finance covers marketplace earnings... it does not become POS tenant treasury."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ACC-065: CommerceHub reconciliation currency authority (EGP) vs tenant currency surface
- Severity: Low
- Category: Known Limitation
- Description: CommerceHub reconciliation currency authority is `EGP` (via `FinancialCurrencyGuard.PlatformBaseCurrencyCode`), distinct from storefront/tenant `Tenant.Currency`. Non-EGP ledger reference groups are counted as reconciliation anomalies. Risk of assuming a tenant's configured currency legitimizes a non-EGP CommerceHub reconciliation.
- Recommendation: Document the two distinct currency surfaces; enforce fail-closed per-currency payout/settlement summaries (already specified in 04-payments).
- Complexity: S
- Dependencies: ACC-011
- Affected Modules: FinancialCurrencyGuard, CommerceHub reconciliation, payout/settlement
- Source Documents: 04-payments.md:210-218
- Evidence: "The only authoritative reconciliation currency is EGP... storefront/tenant currency authority is a different surface."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ACC-066: Manual payment/wallet review architecture undocumented
- Severity: Medium
- Category: Documentation Inconsistency
- Description: Storefront Wallet manual review/security flags and manual payment review architecture (feature flags, proof cleanup, approval roles, rejection paths) are under-covered or missing from the canonical finance architecture.
- Recommendation: Add manual payment/wallet review architecture to the canonical doc (authority, feature flags, approval roles, rejection paths).
- Complexity: M
- Dependencies: ACC-060
- Affected Modules: Manual payment review, wallet, financial-architecture doc
- Source Documents: audits/financial-completeness-audit.md:488,507
- Evidence: "Add manual payment/wallet review architecture, feature flags, proof cleanup, approval roles, and rejection paths."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ACC-067: Feature-flag risk — payment runtime defaults false
- Severity: Low
- Category: Risk
- Description: The Payment Runtime defaults to false (feature-flagged off); architecture claims must distinguish implemented code from active runtime behavior. Doc statements about live payment paths can mislead if flag state is ignored.
- Recommendation: Annotate all architecture claims with active-vs-implemented flag state.
- Complexity: S
- Dependencies: ACC-060
- Affected Modules: Payment runtime, financial-architecture doc
- Source Documents: audits/financial-completeness-audit.md:466; 05-orders.md:99-100
- Evidence: "Feature-flag risk — Payment runtime defaults false; architecture claims must distinguish implemented code from active runtime behavior."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ACC-068: Reversal-event omission in documentation
- Severity: Low
- Category: Documentation Inconsistency
- Description: Sale/purchase/expense reversal events are critical financial-truth events but are missing/under-covered in the canonical finance doc, hiding rollback/compensation behavior.
- Recommendation: Document all reversal events (sale, purchase, expense, supplier payment) and their outbox types.
- Complexity: S
- Dependencies: ACC-060
- Affected Modules: financial-architecture doc, AccountingOutbox
- Source Documents: audits/financial-completeness-audit.md:467
- Evidence: "Reversal-event omission risk — Sale/purchase/expense reversals are critical financial truth events; not documenting them hides rollback/compensation behavior."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ACC-069: Aggregate/projection authority risk (CommerceHub finance dashboards)
- Severity: Low
- Category: Risk
- Description: CommerceHub finance dashboards expose balances that are derivative projections; incomplete documentation can cause operators to trust projection balances over the GL.
- Recommendation: Label derivative dashboards clearly as projections; document the authoritative GL source.
- Complexity: S
- Dependencies: ACC-064
- Affected Modules: CommerceHub dashboards, financial-architecture doc
- Source Documents: audits/financial-completeness-audit.md:470
- Evidence: "Aggregate/projection authority risk — CommerceHub finance dashboards expose balances but are derivative."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### ACC-070: No daily-close discrepancy can be approved/rejected in-app
- Severity: Low
- Category: Workflow Gap
- Description: A cash-drawer difference is shown but cannot be approved/rejected within the application; reopening is done only by calling `open`. Operators cannot disposition discrepancies.
- Recommendation: Add a discrepancy disposition workflow or explicitly document the out-of-app handling.
- Complexity: S
- Dependencies: ACC-043, ACC-045
- Affected Modules: CashboxController, Daily Closing UI
- Source Documents: accounts/FLOW-ACC-10-DAILY-CLOSING.md:409-410
- Evidence: "Difference is shown but cannot be approved/rejected in-app (NOT IMPLEMENTED)."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### ACC-071: Multi-currency per cashbox not supported at close
- Severity: Low
- Category: Known Limitation
- Description: Daily close handles only base currency; there is no per-cashbox multi-currency handling at close (consistent with the broader single-currency limitation in ACC-011).
- Recommendation: Tie to ACC-011 multi-currency work; document the base-currency scope at close.
- Complexity: S
- Dependencies: ACC-011
- Affected Modules: CashboxController, Daily Closing
- Source Documents: accounts/FLOW-ACC-10-DAILY-CLOSING.md:270
- Evidence: "close does not handle currency. Multi-currency per cashbox: NOT IMPLEMENTED."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

---

## CONFLICTS (documentation inconsistencies / contradictions)

### CONFLICT-001: FLOW-ACC-06 scope vs order-payment runtime usage
- Conflicting Documents: accounts/FLOW-ACC-06-PAYMENTS.md:6 (scopes OUT order-payment runtime) vs audits/ACCOUNTING-ARCHITECTURE-AUDIT.md & task file list (used PaymentLifecycleService/gateway adapters as FLOW-ACC-06 evidence)
- Explanation: The canonical doc explicitly excludes the order-payment/CommerceHub runtime, yet the audit and task treated those files as the FLOW-ACC-06 subject, mixing ERP party payments with order payments.
- Possible Cause: Task file list generated from a query that did not honor the canonical doc's scope boundary.
- Recommended Resolution: Split ERP party payments and order/CommerceHub payment runtime into separate canonical flows/audit sections (see ACC-010).

### CONFLICT-002: Party-balance source-of-truth split (three representations)
- Conflicting Documents: accounts/FLOW-ACC-01-CHART-OF-ACCOUNTS.md:35 (AccountLedger.RunningBalance = authority) vs accounts/FLOW-ACC-09-STATEMENT-OF-ACCOUNT.md (Customer.Balance cache) vs SupplierTransactions mirror (used for TotalPaid/TotalInvoiced)
- Explanation: Party balance is represented by the authoritative `AccountLedger`, a `Customer`/`Supplier` cached column, and a `SupplierTransactions` mirror; supplier aux aggregates read from the mirror while customer path reads `AccountLedger`.
- Possible Cause: Historical evolution of supplier aux aggregates before the unified party ledger.
- Recommended Resolution: Make `AccountLedger` the single display source; label/limit the cache and mirror as operational (see ACC-008).

### CONFLICT-003: AccountSummary.IsTreasury mapping inconsistency (internal)
- Conflicting Documents: PointOfSale.Infrastructure/Accounting/FinancialReportService.cs:576 (IsTreasury = a.IsSystem) vs :589 (IsTreasury = account.IsTreasury)
- Explanation: One projection branch derives treasury flag from `IsSystem`; the other reads the account's own `IsTreasury`. Results can disagree.
- Possible Cause: Copy/paste projection where semantics differ.
- Recommended Resolution: Always source `IsTreasury` from `Account.IsTreasury` (see ACC-052).

### CONFLICT-004: Affiliate accounting — doc says ledger-liability-only, code posts GL journals
- Conflicting Documents: audits/financial-completeness-audit.md:33 (affiliate commission = affiliate-ledger liability only, payout via orchestrator) vs implementation `AffiliateCommissionEarnedAccountingConsumer`/`AffiliateCommissionPaidAccountingConsumer` (post GL journals)
- Explanation: The architecture doc under-describes affiliate accounting; the implementation has MassTransit GL-posting consumers, creating both a doc contradiction and a dual-posting risk with the payout orchestrator.
- Possible Cause: Doc not updated after affiliate MassTransit consumers were added.
- Recommended Resolution: Reconcile doc with consumers; document production ownership to avoid double posting (see ACC-059).

### CONFLICT-005: Reversed/not-yet-posted treatment differs across reporting surfaces
- Conflicting Documents: accounts/FLOW-ACC-08-FINANCIAL-REPORTS.md (financial reports net posted+reversed) vs accounts/FLOW-ACC-10-DAILY-CLOSING.md:369-427 (Z-Report counts Reversed source journals at face value in breakdowns) vs accounts/FLOW-ACC-07-EXPENSES.md (expense report reads operational pre-GL)
- Explanation: Reversal/disclosure semantics are inconsistent: financial reports net reversals, daily-close subtotals previously counted reversed journals (partially fixed), expense reports read pre-convergence operational data.
- Possible Cause: Independent reporting surfaces built at different times without a shared reversal-disclosure contract.
- Recommended Resolution: Standardize reversal/pre-GL disclosure across all reporting surfaces (see ACC-015).

### CONFLICT-006: Canonical Account vs legacy FinancialAccount "two models" confusion
- Conflicting Documents: accounts/FLOW-ACC-01-CHART-OF-ACCOUNTS.md:28-34 (canonical Account/LedgerAccounts authoritative) vs FinancialAccountsController/FinancialAccount (legacy shadow with AccountNumber + soft-delete)
- Explanation: Two different models/tables coexist; `FinancialAccount` is a legacy shadow with its own CRUD, not the ledger source of truth. Readers may conflate them.
- Possible Cause: Legacy financial-account CRUD predating the canonical CoA.
- Recommended Resolution: Deprecate/label the legacy model clearly; migrate needed capability into the canonical model (see ACC-022).

### CONFLICT-007: Stale "commented legacy writes" label in FLOW-ACC-07
- Conflicting Documents: accounts/FLOW-ACC-07-EXPENSES.md (describes commented legacy writes) vs actual code (controller fully migrated; only a read-only `JournalEntries.AnyAsync` exists at :548)
- Explanation: The doc label no longer reflects reality; no legacy write code remains.
- Possible Cause: Doc not updated after the controller migration.
- Recommended Resolution: Remove the stale label (see ACC-051).

### CONFLICT-008: CommerceHub reconciliation currency (EGP) vs tenant currency surface
- Conflicting Documents: 04-payments.md:210-218 (CommerceHub reconciliation authority = EGP only) vs Tenant.Currency / storefront currency settings (different surface)
- Explanation: The two currency surfaces are distinct but easily conflated; a tenant's configured non-EGP currency does not legitimize a non-EGP CommerceHub reconciliation.
- Possible Cause: Different bounded contexts reuse "currency" terminology.
- Recommended Resolution: Document the two surfaces explicitly; enforce per-currency fail-closed payout/settlement (see ACC-065).

### CONFLICT-009: Two RecordRefundAsync APIs (storefront vs CommerceHub)
- Conflicting Documents: PointOfSale.Core/Payments (IPaymentLifecycleService.RecordRefundAsync) vs PointOfSale.Core/CommerceHub (ICommerceHubFinancialLedgerService.RecordRefundAsync)
- Explanation: Two differently-scoped refund methods share a name; conflation risks incorrect refund-accounting wiring.
- Possible Cause: Separate bounded contexts evolved independent refund APIs.
- Recommended Resolution: Clearly separate and document both APIs (see ACC-056).

### CONFLICT-010: Daily close "Z-Report" reversal counting vs financial-report netting
- Conflicting Documents: accounts/FLOW-ACC-10-DAILY-CLOSING.md (breakdown subtotals counted Reversed at face value) vs accounts/FLOW-ACC-08-FINANCIAL-REPORTS.md (reports net reversals)
- Explanation: Within the same reporting family, the Z-Report breakdown overstated reversed sales while the headline expected cash and the financial reports net correctly. Partially remediated (daily close now uses posted only) but the inconsistency pattern persists for disclosure standardization.
- Possible Cause: Separate report builders without a shared reversal contract.
- Recommended Resolution: Standardize reversal disclosure; verify all subtotal breakdowns exclude/offset reversed journals (see ACC-015).

### CONFLICT-011: Party/GL decoupling for POS cash/card is by-design but under-emphasized
- Conflicting Documents: accounts/FLOW-ACC-06-PAYMENTS.md §8/§16 (party balance is a cache; GL is truth) vs the implicit expectation that SOA AR equals GL AR(1410)
- Explanation: Cash/card POS `AccountLedger` entries are informational and not posted to GL AR; only credit sales post to AR(1410). Reconciliation reports that expect GL AR == sub-ledger AR will be wrong for cash/card.
- Possible Cause: Two-tier model documented but not emphasized at the reconciliation boundary.
- Recommended Resolution: Add explicit note in FLOW-ACC-06 that cash/card POS `AccountLedger` is informational (see ACC-016).

### CONFLICT-012: Supplier-payment reversal documented as absent vs order-refund accounting present
- Conflicting Documents: audits/ACCOUNTING-ARCHITECTURE-AUDIT.md C-02 (order refund accounting IMPLEMENTED; supplier-payment reversal ABSENT) vs the original audit's conflation of "payment reversal" as a single CRITICAL finding
- Explanation: The original audit merged order-refund (implemented) and supplier-payment reversal (missing) into one CRITICAL finding; peer review split them — order refund is implemented (excluded), supplier-payment reversal remains a real MEDIUM gap.
- Possible Cause: Over-broad original finding grouping ERP and order payments.
- Recommended Resolution: Keep the two as separate tracked items; only the supplier-payment reversal (ACC-001) remains open.

### CONFLICT-013: "Collected-order cancellation posts no reversal" — false positive vs true order-payment defect
- Conflicting Documents: audits/ACCOUNTING-ARCHITECTURE-AUDIT.md C-01 (INCORRECT / excluded as defect) vs AGENT-06 F-06-02 (survives as TRUE defect of the Order-Payment runtime, just out-of-FLOW-ACC-06 scope)
- Explanation: Peer review concluded collected/recognized-payment cancellation is blocked/collapsed and refunds use a separate path, so C-01 is a false positive for the ERP party-payment scope. However, the underlying order-payment-runtime behavior (collected-then-cancelled leaves GL revenue un-reversed) is a real defect of that runtime — it is simply owned by a different canonical flow, not "non-existent."
- Possible Cause: Scope boundary between FLOW-ACC-06 ERP party payments and the order-payment runtime.
- Recommended Resolution: Track the order-payment-runtime cancellation/reversal gap under the order-payments domain (not under Sub-Agent 1's accounting scope), and confirm the admin cancel guard + refund path covers it; do not re-open C-01 as an accounting defect.

---

## SECOND-PASS ADDITIONS (re-read, items not captured in first pass)

The following were confirmed during a second review focused on gaps/distinction sections and were appended rather than renumbering the above:

- ACC-018 through ACC-071 already incorporate second-pass material (Chart-of-Accounts CRUD gaps, transfer lifecycle, journal API gaps, opening-balance GL gaps, payment endpoint/allocation gaps, expense/receipt/number gaps, SOA export/audit gaps, daily-closing shift/approval/lock gaps, adjustment reason/write-off gaps, frontend idempotency gap, stale-doc gaps, order-payment chargeback/refund-API/overpayment gaps, and the financial-completeness-audit documentation gaps).
- Conflict set expanded to 13 to cover the false-positive-vs-true-defect nuance (CONFLICT-012, CONFLICT-013) and the Z-Report vs financial-report reversal counting (CONFLICT-010).

No new unresolved code defects were found beyond those enumerated; remaining distinctions in the source docs are explicitly marked "NOT IMPLEMENTED" and are captured above as ACC-### items.

---

## TOP PRIORITIES (by severity + governance/audit-readiness impact)

1. **ACC-001 — Supplier-payment void/reversal workflow missing** (High; governance/reconciliation; blocks external-audit readiness)
2. **ACC-003 — Cashbox closure audit non-atomic** (Medium; audit durability; blocks external-audit readiness)
3. **ACC-007 — No shared accounting-period / locked-period guard** (Medium; governance; blocks external-audit readiness)
4. **ACC-009 — Uneven async GL convergence visibility** (Medium; operator monitoring; blocks external-audit readiness)
5. **ACC-002 — Concurrent cashbox close race** (Medium; concurrency/audit integrity)
6. **ACC-008 — Party-balance source-of-truth split** (Medium; reconciliation correctness)
7. **ACC-010 — FLOW-ACC-06 scope/file-map mismatch** (High; documentation integrity; blocks correct consolidation)
8. **ACC-061 — Party balance drift risk (AccountLedger vs GL outbox)** (Medium; reconciliation safety net)
9. **ACC-011 — Multi-currency unsupported in core ledger/SOA/reports** (Medium; market-scope blocker if multi-currency tenants required)
10. **ACC-059 / ACC-060 — Affiliate accounting doc contradiction + missing documented flows/authorities** (Medium; documentation completeness & dual-posting risk)
