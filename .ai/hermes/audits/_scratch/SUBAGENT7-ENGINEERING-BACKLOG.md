I have now read the full assigned FILE SET (core guides 02–08, GLOSSARY, MENTAL_MODEL, CROSS_SYSTEM_CERTIFICATION, the 5 certification docs, all 6 system READMEs, the architecture/ and accounts/ financial docs, and every file under `_scratch/` and `audits/`). I also cross-checked the audit corpus, ran targeted greps to verify broken references and the spurious "R12" citation, and confirmed the audit/remediation status of each finding. Below is the exhaustive engineering backlog of unresolved documentation, cross-document-consistency, naming, and technical-debt issues.

---

# Shopixy `.ai/hermes` — Documentation / Consistency / Naming / Technical-Debt Backlog

> Scope: cross-cutting documentation quality only. Completed/resolved items (per `ACCOUNTING-ARCHITECTURE-REMEDIATION-REPORT.md`) are excluded; where code was fixed but docs were not, that is logged as a *stale-doc* inconsistency, not a code defect.

### DOC-001: Dangling `docs/architecture/*` references across core guides and system READMEs
- Severity: High
- Category: Documentation Inconsistency
- Description: Core guides (02,03,04,05,06), `04-payments.md` source list, and multiple system READMEs reference `docs/architecture/*` certification-findings files, but no `docs/` directory exists inside `.ai/hermes`. The KB instead uses `accounts/`, `architecture/`, `audits/`. Every such reference is unresolvable from the KB.
- Recommendation: Either relocate referenced files into the KB under a consistent path (e.g. `architecture/`), or rewrite all references to the actual in-KB locations; add a CI link-checker.
- Complexity: L
- Dependencies: DOC-002, DOC-004
- Affected Modules: 02-architecture, 03-inventory, 04-payments, 05-orders, 06-security-identity, 04-payments source list, all system READMEs
- Source Documents: `02-architecture.md:252-257`; `04-payments.md:472-501`; `03-inventory.md:413-450`; terminal `find` (no `docs/` dir present)
- Evidence: "No `docs/` subdirectory exists in KB; `grep` shows 40+ `docs/architecture/...` strings with zero matching files."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### DOC-002: Dangling `*.certification-findings.md` references (files absent from KB)
- Severity: Medium
- Category: Documentation Inconsistency
- Description: Numerous core guides, audits, and AGENT scratch files cite `<name>-certification-findings.md` (e.g. `payment-end-to-end-authority-certification-findings.md`). None of these files exist anywhere in `.ai/hermes`.
- Recommendation: Remove dead citations or import the referenced findings into the KB; enforce reference integrity in CI.
- Complexity: M
- Dependencies: DOC-001
- Affected Modules: 02-08 core guides, audits/* , accounts/FLOW-ACC-*
- Source Documents: `03-inventory.md:426-430`; `04-payments.md:496-500`; terminal `find . -name "*certification-findings*"` → 0 results
- Evidence: "find returned 0 certification-findings files; guides list 8+ such references each."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### DOC-003: `.ai-context/*` cited as highest authority but not part of the KB
- Severity: High
- Category: Governance Concern
- Description: `INDEX.md` Authority Precedence and every core guide name `.ai-context/ARCHITECTURE_TRUTH.md`, `PROJECT_CONTEXT.md`, `KNOWN_RISKS.md`, `CODEX_RULES.md`, `CROSS_SYSTEM_AUTHORITY_HARDENING.md` as the supreme truth. These files live outside `.ai/hermes` and are not readable from the KB itself, creating an unverifiable external-authority dependency and a single point of documentation failure.
- Recommendation: Mirror the authoritative `.ai-context/*` excerpts into the KB (or document their external location + retrieval step explicitly) so the KB is self-contained and auditable.
- Complexity: L
- Dependencies: none
- Affected Modules: INDEX, 02-08 core guides, all audits
- Source Documents: `INDEX.md:293-309`; `02-architecture.md:252-256`
- Evidence: "INDEX: '1. .ai-context/*' is highest authority; source files list points outside .ai/hermes."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### DOC-004: `INDEX.md` stale relative to actual KB tree and status
- Severity: High
- Category: Documentation Inconsistency
- Description: `INDEX.md` reading order lists only 02–08 and its knowledge map omits GLOSSARY, MENTAL_MODEL, CROSS_SYSTEM_CERTIFICATION, `accounts/`, `architecture/`, `audits/`, `platform/`, `business-flows/`, `capabilities/`, `master-data/`, and all `ARCHETYPE_*`/`HERMES_BROWSER_ONTOLOGY*` files. Its status line says "Core Complete; Systems In Progress" though `systems/` is fully populated and `platform/` is extensive.
- Recommendation: Regenerate INDEX from the real tree; add the missing top-level sections; correct the status line; include GLOSSARY/MENTAL_MODEL/CROSS_SYSTEM_CERTIFICATION in the reading order.
- Complexity: M
- Dependencies: DOC-012, DOC-013
- Affected Modules: INDEX.md
- Source Documents: `INDEX.md:265-437`; terminal `find` full tree
- Evidence: "INDEX reading order = 02..08 only; tree contains 80+ files across 12 top-level dirs not mentioned."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### DOC-005: Inconsistent severity / priority vocabulary across the KB
- Severity: Medium
- Category: Documentation Inconsistency
- Description: At least four incompatible schemes coexist: (a) `CODEX_Rules` `CONFIRMED_RISK/THEORETICAL_RISK/MITIGATED_RISK/ACCEPTED_TRADEOFF` (only `02-architecture.md`); (b) audit `CRITICAL/HIGH/MEDIUM/LOW/ARCHITECTURAL IMPROVEMENT`; (c) `FLOW-ACC-*` `F-xx` with `P0–P3`; (d) ad-hoc QA-Philosophy language. No shared severity glossary exists, so triage across docs is ambiguous.
- Recommendation: Define one canonical severity/priority vocabulary in GLOSSARY or a `CONTRIBUTING` doc and map each legacy scheme to it; normalize new docs.
- Complexity: M
- Dependencies: DOC-012
- Affected Modules: 02-architecture, audits/*, accounts/FLOW-ACC-*, 08-qa-philosophy
- Source Documents: `02-architecture.md:191-202`; `audits/ACCOUNTING-ARCHITECTURE-AUDIT.md:23`; `accounts/FLOW-ACC-11-ACCOUNT-ADJUSTMENTS.md` (F-11-x / P1–P3)
- Evidence: "grep CONFIRMED_RISK → only 02-architecture.md; audits use CRITICAL/HIGH/...; FLOW-ACC uses F-xx/Px."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### DOC-006: Ambiguous "README is STALE" warning vs authoritative system READMEs
- Severity: Medium
- Category: Documentation Inconsistency
- Description: Core guides repeatedly state "The README is STALE and must not be trusted for current behavior." This refers to the PointOfSale repo README, but `systems/*/README.md` are explicitly granted authoritative system-guide status by INDEX/TASK_ROUTING. A reader can misread the warning as covering the (authoritative) system READMEs.
- Recommendation: Replace "the README" with "the repository top-level README (not the systems/* system guides)" everywhere; or drop the blanket line.
- Complexity: S
- Dependencies: CONFLICT-002
- Affected Modules: 02-architecture, 03-inventory, 04-payments, 05-orders, 06-security-identity
- Source Documents: `02-architecture.md:11`; `03-inventory.md:7`; `05-orders.md:7`
- Evidence: "02-architecture.md:11 'The README is STALE ... Do not trust it for current behavior.'"
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### DOC-007: Audit cites non-existent rule "R12" (transaction reordering owned by Payments)
- Severity: Medium
- Category: Documentation Inconsistency
- Description: `_scratch/order-payment-financial.md` attributes an ordering rule to "R12 (transaction reordering is owned by Payments)". No rule "R12" exists in `02-architecture.md`, `04-payments.md`, `05-orders.md`, or anywhere in the KB (verified by grep). The citation is spurious and undermines audit credibility.
- Recommendation: Remove the R12 reference or, if a real rule exists in an external source, quote it verbatim with its true ID/location.
- Complexity: S
- Dependencies: none
- Affected Modules: `_scratch/order-payment-financial.md`, audits
- Source Documents: `_scratch/order-payment-financial.md` (R12 mention); terminal grep "R12|reorder" → only `MD-01-PRODUCT-KNOWLEDGE.yaml` and `FLOW-05-PURCHASE-RETURN.md` (unrelated BR12)
- Evidence: "grep across KB for R12 → no transaction-reordering rule; the cited 'R12' does not exist."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### DOC-008: "Service Marketplace" vs "CommerceHub" naming/terminology drift
- Severity: Medium
- Category: Naming
- Description: `certification/04-pos-admin-service-marketplace.md` and `systems/service-marketplace/README.md` correctly distinguish Service Marketplace (internal POS-to-POS) from CommerceHub (multi-vendor marketplace). However the INDEX knowledge map and several docs loosely group "marketplace" governance language, and the two are easily conflated by readers (the service-marketplace guide must repeatedly assert "It is not CommerceHub").
- Recommendation: Add an explicit "Service Marketplace ≠ CommerceHub" callout to INDEX and GLOSSARY; standardize the term "marketplace" to "CommerceHub marketplace" where intended.
- Complexity: S
- Dependencies: DOC-012
- Affected Modules: INDEX, GLOSSARY, certification/04, systems/service-marketplace
- Source Documents: `systems/service-marketplace/README.md:1-15`; `INDEX.md:343-357`; `certification/04-pos-admin-service-marketplace.md:25`
- Evidence: "Service Marketplace README: 'It is not CommerceHub. It is not Storefront. It is not customer-facing.'"
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### DOC-009: Canonical `accounts/FLOW-ACC-*` docs unmapped in INDEX/TASK_ROUTING
- Severity: Medium
- Category: Documentation Inconsistency
- Description: The `accounts/FLOW-ACC-01..11-*.md` canonical financial-flow docs are referenced by the audits but are absent from INDEX reading order and from TASK_ROUTING legends. Auditors and agents cannot discover them through the official navigation map.
- Recommendation: Add `accounts/` (and `audits/`) to INDEX reading order and assign TASK_ROUTING legend codes.
- Complexity: S
- Dependencies: DOC-004, DOC-013
- Affected Modules: INDEX, TASK_ROUTING, accounts/*
- Source Documents: `INDEX.md:265-275`; `TASK_ROUTING.md` (legends); terminal tree (accounts/ present)
- Evidence: "TASK_ROUTING legends cover INV/PAY/ORD/SEC/OVW/QA/S-* only; no ACCOUNTS/AUDITS codes."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### DOC-010: Stale canonical/audit docs describe control gaps already fixed by remediation
- Severity: High
- Category: Documentation Inconsistency
- Description: `ACCOUNTING-ARCHITECTURE-REMEDIATION-REPORT.md` marks as IMPLEMENTED: adjustment/expense role authorization (H-04/H-06), expense non-positive amount validation (H-02), expense integrity link scan (H-05), cashbox/expense boundary validation (H-07), tenant predicates on reads (M-09/M-10 partial), report date-range validation (M-12). Yet `FLOW-ACC-11`, `FLOW-ACC-07`, `FLOW-ACC-08`, `FLOW-ACC-06`, and the AGENT-* audit files still describe these as current open gaps, and `FLOW-ACC-11 §1/§5` still states "no approval stage / no role authorization." Docs were not updated after the code fixes.
- Recommendation: Re-run the canonical/audit docs through the remediation report; strike or re-label each IMPLEMENTED finding as "RESOLVED 2026-07-20" and keep only DEFERRED items as open.
- Complexity: M
- Dependencies: none
- Affected Modules: accounts/FLOW-ACC-06..11, audits/_scratch/AGENT-*, ACCOUNTING-ARCHITECTURE-REMEDIATION-REPORT.md
- Source Documents: `audits/ACCOUNTING-ARCHITECTURE-REMEDIATION-REPORT.md:24-130`; `accounts/FLOW-ACC-11-ACCOUNT-ADJUSTMENTS.md:93-106` (still "no role"); `accounts/FLOW-ACC-07-EXPENSES.md:36-52` (still "no validation")
- Evidence: "Remediation: H-04/H-06 IMPLEMENTED; FLOW-ACC-11 still says 'No role-based authorization on adjustment creation'."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### DOC-011: `financial-architecture.md` treated as authoritative yet scored FAIL in-KB
- Severity: High
- Category: Documentation Inconsistency
- Description: `02-architecture.md` and `ACCOUNTING-ARCHITECTURE-AUDIT.md` cite `architecture/financial-architecture.md` as the canonical financial architecture doc, but the in-KB `audits/financial-completeness-audit.md` returns VERDICT: FAIL (incomplete/overbroad; "one EGP can move without being explained"). The KB simultaneously relies on and fails the same document.
- Recommendation: Reconcile by either (a) downgrading the doc's authority to "draft, not yet complete" in 02/audit, or (b) completing the doc per the completeness audit's 16 recommendations, then re-scoring. Track via CONFLICT-001.
- Complexity: L
- Dependencies: CONFLICT-001
- Affected Modules: 02-architecture, architecture/financial-architecture, audits/financial-completeness-audit, audits/ACCOUNTING-ARCHITECTURE-AUDIT
- Source Documents: `02-architecture.md:7`; `audits/financial-completeness-audit.md:14,549`; `architecture/financial-architecture.md:10`
- Evidence: "financial-completeness-audit: 'VERDICT: FAIL ... it cannot prove that one Egyptian Pound cannot move anywhere without being explained.'"
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### DOC-012: GLOSSARY / MENTAL_MODEL / CROSS_SYSTEM_CERTIFICATION absent from INDEX reading order
- Severity: Low
- Category: Documentation Inconsistency
- Description: These three core assigned docs are not listed in INDEX reading order, knowledge map, or "Domain Guides (Future)" — yet they are central terminology/mental-model/consistency artifacts. Readers following INDEX will never reach them.
- Recommendation: Insert GLOSSARY (first), MENTAL_MODEL, and CROSS_SYSTEM_CERTIFICATION into the reading order and knowledge map.
- Complexity: S
- Dependencies: DOC-004
- Affected Modules: INDEX
- Source Documents: `INDEX.md:265-275,313-358`; (files present: GLOSSARY.md, MENTAL_MODEL.md, CROSS_SYSTEM_CERTIFICATION.md)
- Evidence: "INDEX reading order lines 265-275 list 02-08 only; no mention of GLOSSARY/MENTAL_MODEL/CROSS_SYSTEM."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### DOC-013: TASK_ROUTING.md legends incomplete for actual KB tree
- Severity: Low
- Category: Documentation Inconsistency
- Description: TASK_ROUTING legends cover INV/PAY/ORD/SEC/OVW/QA + S-*/C-* only. They omit `accounts/` (FLOW-ACC-*), `audits/`, `architecture/`, `platform/`, `business-flows/`, `capabilities/`, `master-data/`. Agents using the router cannot resolve those paths to legends.
- Recommendation: Extend the legend table with codes for every top-level directory in the tree.
- Complexity: S
- Dependencies: DOC-004, DOC-009
- Affected Modules: TASK_ROUTING
- Source Documents: `TASK_ROUTING.md` (legend section); terminal tree
- Evidence: "Legends: ARCH/INV/PAY/ORD/SEC/OVW/QA/S-ST..S-SA/C-ST..C-SA/C-X; no ACCOUNTS/AUDITS/PLATFORM/etc."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### DOC-014: Inconsistent version/date stamping and "Core Complete" labeling
- Severity: Low
- Category: Documentation Inconsistency
- Description: INDEX declares "Core Version: v1.0 / Status: Core Complete" while audits and remediation reports are dated 2026-07-20 and reference ongoing deferred work and a separate `financial-completeness-audit` not mentioned by INDEX. No single source-of-truth version stamp ties the docs to the code state.
- Recommendation: Add a KB-level CHANGELOG with dates and a "last-reviewed-against-code" stamp per doc; retire the stale "Core Complete" banner or qualify it.
- Complexity: S
- Dependencies: DOC-004
- Affected Modules: INDEX, all docs
- Source Documents: `INDEX.md:433-437`; `audits/ACCOUNTING-ARCHITECTURE-REMEDIATION-REPORT.md:3`
- Evidence: "INDEX: 'Core Version: v1.0; Status: Core Complete; Systems In Progress' vs audit dates 2026-07-20 with deferred items."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### DOC-015: Webhook idempotency/logging heterogeneity not reconciled in canonical doc
- Severity: Medium
- Category: Known Limitation
- Description: Fawry persists a durable `PaymentWebhookLog` with `ProviderEventId` dedup; Stripe/Paymob and CommerceHub/runtime paths use different logs/replay entities and indexes (F-06-03, CV-12, M-16/L-15). The canonical `04-payments.md` states webhooks "must be authenticated, tenant-resolved, idempotent, audited" but does not document the per-provider heterogeneity, so the stated guarantee is unevenly implemented.
- Recommendation: Document the actual per-provider webhook idempotency model in `04-payments.md`; flag standardization as a roadmap item.
- Complexity: M
- Dependencies: none
- Affected Modules: 04-payments, audits/_scratch/AGENT-06-PAYMENTS, ACCOUNTING-ARCHITECTURE-AUDIT (CV-12)
- Source Documents: `accounts/FLOW-ACC-06` findings F-06-03; `audits/ACCOUNTING-ARCHITECTURE-AUDIT.md:238` (M-16 PARTIALLY VERIFIED)
- Evidence: "F-06-03: 'StripeWebhooksController.cs has zero references to PaymentWebhookLog' while Fawry persists one."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### DOC-016: No shared accounting-period / fiscal lock service
- Severity: High
- Category: Technical Debt
- Description: No period-lock or closed-period service exists; financial write paths (adjustments F-11-08, expenses M-21) lack period checks. Reports are read-only so the lock belongs to the write engine, which does not yet enforce it. Verified across audits as DEFERRED/open.
- Recommendation: Implement a shared `PeriodLockService` invoked from all financial write boundaries (expenses, adjustments, settlements, daily close).
- Complexity: L
- Dependencies: none
- Affected Modules: FinancialPostingService, StatementOfAccountController, ExpensesController, ReportsController
- Source Documents: `audits/ACCOUNTING-ARCHITECTURE-AUDIT.md:369` (CV-11 VERIFIED MEDIUM); `accounts/FLOW-ACC-11-ACCOUNT-ADJUSTMENTS.md:146-156` (F-11-08)
- Evidence: "M-21/F-11-08: 'No shared accounting-period lock service was found and financial writes lack period checks.'"
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### DOC-017: Supplier-payment reversal / void workflow missing
- Severity: Medium
- Category: Known Limitation
- Description: Order refund accounting exists (`RecordRefundAsync`/`PostEcommerceRefundAsync`), but supplier payments have no explicit cancel/refund/reversal command or outbox path (C-02/CV-02). Remediation report keeps this DEFERRED, not implemented.
- Recommendation: Add a first-class supplier-payment void/reversal command through existing posting/outbox infrastructure (do not emulate order refunds blindly).
- Complexity: L
- Dependencies: DOC-016
- Affected Modules: SuppliersController, SupplierPayment, AccountingOutbox
- Source Documents: `audits/ACCOUNTING-ARCHITECTURE-AUDIT-CHANGELOG.md:46-52` (C-02 PARTIALLY VERIFIED); `audits/ACCOUNTING-ARCHITECTURE-REMEDIATION-REPORT.md:134-141`
- Evidence: "C-02: 'no supplier-payment reversal command or outbox path found.'"
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### DOC-018: Party sub-ledger dual-authority model under-emphasized in 02-architecture & GLOSSARY
- Severity: High
- Category: Technical Debt
- Description: `LedgerService`/`AccountLedger` directly mutate `Customer.Balance`/`Supplier.Balance` outside `FinancialPostingService` (a live, documented party sub-ledger). `02-architecture.md` and GLOSSARY frame SQL/GL as the single truth and only lightly note the party sub-ledger, while `financial-completeness-audit` shows this path can move money unexplained. Reconciliation/debugging risk for anyone trusting only the GL mental model.
- Recommendation: Document the two-tier model (GL vs party sub-ledger) as a first-class architecture section in 02-architecture and GLOSSARY, with the drift/debugging rules from `financial-completeness-audit` §15.
- Complexity: M
- Dependencies: DOC-011
- Affected Modules: 02-architecture, GLOSSARY, LedgerService, AccountLedger
- Source Documents: `audits/financial-completeness-audit.md:30,463`; `02-architecture.md:16-20`
- Evidence: "financial-completeness-audit #1: 'LedgerService writes AccountLedger, mutates Customer.Balance and Supplier.Balance ... bypasses FinancialPostingService because it is not the GL ledger.'"
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### DOC-019: Adjustments lack approval/reversal lifecycle, GL linkage, and party-balance floor
- Severity: High
- Category: Technical Debt
- Description: `PartyLedgerAdjustment` has no status/lifecycle, no approval/reversal/cancellation (F-11-07), no stored `JournalEntryId` linkage (F-11-06), and no negative-balance floor policy (F-11-05). These remain VERIFIED/open in the peer-reviewed audit and are DEFERRED in remediation. (Role auth H-04 was fixed — see DOC-010.)
- Recommendation: Add adjustment lifecycle + stored GL journal linkage + configurable party-balance floor; surface unposted adjustments in UI.
- Complexity: L
- Dependencies: DOC-016
- Affected Modules: StatementOfAccountController, PartyLedgerAdjustment, LedgerService
- Source Documents: `accounts/FLOW-ACC-11-ACCOUNT-ADJUSTMENTS.md:108-156` (F-11-05/06/07)
- Evidence: "F-11-07: 'No ApproveAdjustment/ReverseAdjustment/CancelAdjustment/delete endpoints or service methods.'"
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### DOC-020: No shared outbox-lag / GL-convergence operator signal
- Severity: Medium
- Category: Known Limitation
- Description: Async GL convergence is unevenly exposed; there is no single operator-facing endpoint/metric showing per-source posted/failed/pending outbox state (CV-06/CV-10). Operators cannot tell when a derivative view has converged to ledger truth.
- Recommendation: Add a shared outbox-lag / accounting-convergence health endpoint consumed by dashboards.
- Complexity: M
- Dependencies: none
- Affected Modules: AccountingOutboxProcessor, dashboards, CommerceHubFinance
- Source Documents: `audits/ACCOUNTING-ARCHITECTURE-AUDIT.md:364` (CV-10 VERIFIED LOW); `:362` (CV-06 MEDIUM)
- Evidence: "CV-10: 'No shared outbox-drained/GL-converged operator signal' — VERIFIED."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### DOC-021: Cashbox closure audit is fire-and-forget with in-memory fallback
- Severity: Medium
- Category: Risk
- Description: `CloseCashbox` commits state then calls `AuditService.EnqueueLog` in a separate scope; on failure it falls back to an in-memory `ConcurrentQueue` that is lost on process exit. A closure can therefore commit with no durable audit record (F-10-03/M-04). Remediation keeps this DEFERRED.
- Recommendation: Persist closure audit inside the close transaction or via a durable audit outbox; alert on queue depth.
- Complexity: M
- Dependencies: none
- Affected Modules: CashboxController, AuditService, AuditQueue
- Source Documents: `accounts/FLOW-ACC-10-DAILY-CLOSING.md:72-90` (F-10-03); `audits/ACCOUNTING-ARCHITECTURE-AUDIT-CHANGELOG.md:142-148`
- Evidence: "F-10-03: 'if both the immediate SQL write and the retry queue fail, the closure is recorded in state but leaves no audit trail.'"
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### DOC-022: Multi-currency unsupported across ledger / SOA / reports
- Severity: Medium
- Category: Known Limitation
- Description: `LedgerEntry`, `AccountLedger`, and report DTOs have no currency column; single base-currency only (F-08-08/F-09-05/M-14). Documented as a scope decision, but it is a platform-wide limitation that blocks multi-entity/multi-currency tenants.
- Recommendation: If target tenants require it, add `CurrencyCode` to `AccountLedger`/`LedgerEntry` and propagate through `LedgerService`/outbox/report partitioning.
- Complexity: XL
- Dependencies: none
- Affected Modules: LedgerEntry, AccountLedger, FinancialReportService, SOA
- Source Documents: `accounts/FLOW-ACC-08-FINANCIAL-REPORTS.md:128-135` (F-08-08); `accounts/FLOW-ACC-09-STATEMENT-OF-ACCOUNT.md:103-118` (F-09-05)
- Evidence: "F-08-08: 'LedgerEntry.DebitAmount/CreditAmount are decimal(18,4) with no currency.'"
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### DOC-023: Retained earnings / fiscal-year close not implemented
- Severity: Medium
- Category: Known Limitation
- Description: Balance Sheet reads raw Equity ledger balances; no retained-earnings roll-forward / year-end closing entry exists (F-08-09/F-15). A dated Balance Sheet will not show accumulated retained earnings unless an explicit Equity account holds it.
- Recommendation: Implement a year-end closing journal (new journal via `FinancialPostingService`) moving P&L net to a Retained Earnings account — must go through the write path, not reports.
- Complexity: L
- Dependencies: DOC-016
- Affected Modules: FinancialReportService, BalanceSheetReport
- Source Documents: `accounts/FLOW-ACC-08-FINANCIAL-REPORTS.md:139-147` (F-08-09)
- Evidence: "F-08-09: 'Balance Sheet Equity section = raw AccountType.Equity ledger balances only ... no closing-entry / retained-earnings roll-forward.'"
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### DOC-024: Financial read endpoints rely on EF global filter only for tenant isolation
- Severity: Medium
- Category: Risk
- Description: Expense, SOA, cashbox, and daily-close reads depend solely on the EF global `HasQueryFilter` for tenant scope; writes re-apply explicit `(Id,TenantId)` predicates after `IgnoreQueryFilters()`. A regression in the global filter would leak reads cross-tenant (F-07-03/M-17/CV-05). Remediation made this PARTIALLY IMPLEMENTED only.
- Recommendation: Add explicit `RequiredTenantId` predicates to all high-value financial read surfaces (defense-in-depth), matching the write pattern.
- Complexity: M
- Dependencies: none
- Affected Modules: ExpensesController, StatementOfAccountController, ReportsController, CashboxController
- Source Documents: `accounts/FLOW-ACC-07-EXPENSES.md:69-82` (F-07-03); `audits/ACCOUNTING-ARCHITECTURE-AUDIT.md:368` (CV-05 VERIFIED MEDIUM)
- Evidence: "F-07-03: 'GetExpenses and GetReport query _context.Expenses with NO explicit TenantId predicate; they rely on the EF Core global HasQueryFilter.'"
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### DOC-025: Supplier outstanding sub-aggregates sourced from denormalized mirror
- Severity: Medium
- Category: Technical Debt
- Description: Customer outstanding `TotalPaid/TotalInvoiced` are computed from `AccountLedgers`; the Supplier branch computes the same fields from the `SupplierTransactions` mirror + `PurchaseInvoices`. The two party types use different sources for the same logical quantity, violating the "AccountLedger is the single authority" principle (F-09-03/M-01). VERIFIED/open.
- Recommendation: Source supplier aux aggregates from `AccountLedgers` grouped by `TransactionType`, mirroring the customer path.
- Complexity: M
- Dependencies: DOC-018
- Affected Modules: StatementOfAccountController
- Source Documents: `accounts/FLOW-ACC-09-STATEMENT-OF-ACCOUNT.md:63-81` (F-09-03)
- Evidence: "F-09-03: 'The doc's described behavior matches code, but it is internally inconsistent with the Customer path.'"
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### DOC-026: Affiliate MassTransit accounting consumers vs orchestrator relationship undocumented
- Severity: Medium
- Category: Risk
- Description: `AffiliateAccountingConsumer` (earned/paid GL journals) and `AffiliateSettlementOrchestrator` both post affiliate GL; the code comments describe one as legacy fallback, but no doc explains the ownership boundary, creating double-posting risk (financial-completeness-audit #4/#5, risk #3).
- Recommendation: Document which path is authoritative for affiliate GL posting and explicitly mark the legacy/fallback path; reconcile in `architecture/financial-architecture.md`.
- Complexity: M
- Dependencies: DOC-011
- Affected Modules: AffiliateAccountingConsumer, AffiliateSettlementOrchestrator, CommerceAccountingService
- Source Documents: `audits/financial-completeness-audit.md:140-148,465`
- Evidence: "financial-completeness-audit #5: 'It also creates a possible duplicate/legacy path relationship with AffiliateSettlementOrchestrator that must be documented.'"
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### DOC-027: TreasuryTransferService vs TreasuryOperationService overlap under-documented
- Severity: Low
- Category: Documentation Inconsistency
- Description: `TreasuryTransferService` (`TransferAsync`/`SettleClearingAsync`/`DepositToBankAsync`) posts directly through `IFinancialPostingService`, separate from `TreasuryOperationService`. The canonical financial doc mentions Treasury via `TreasuryOperationService` only, omitting the transfer service (financial-completeness-audit #4.4).
- Recommendation: Document `TreasuryTransferService` as a distinct GL-posting authority in `architecture/financial-architecture.md`.
- Complexity: S
- Dependencies: DOC-011
- Affected Modules: TreasuryTransferService, TreasuryOperationService
- Source Documents: `audits/financial-completeness-audit.md:130-138` (#4.4)
- Evidence: "#4.4: 'TreasuryTransferService implements TransferAsync, SettleClearingAsync, and DepositToBankAsync directly through FinancialPostingService ... The document's claim that Treasury is represented by TreasuryOperationService is incomplete.'"
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### DOC-028: Fail-closed opening-balance / inventory-adjustment behavior not consistently stated
- Severity: Medium
- Category: Documentation Inconsistency
- Description: Cashbox non-zero opening balance is rejected, bank opening-balance writes return 410 Gone, and `PostInventoryAdjustmentAsync` fails closed until an authoritative SQL workflow exists. These fail-closed behaviors are documented in `financial-architecture.md` but are NOT consistently surfaced in `02-architecture.md`, GLOSSARY, or the relevant `FLOW-ACC-*` docs, so operators may expect these paths to create money (financial-completeness-audit #6/#7/#13).
- Recommendation: Add a unified "Disabled / fail-closed financial paths" section referenced from 02-architecture, GLOSSARY, and each affected FLOW-ACC doc.
- Complexity: S
- Dependencies: DOC-011, DOC-018
- Affected Modules: 02-architecture, GLOSSARY, CashboxController, BanksController, FinancialPostingService
- Source Documents: `audits/financial-completeness-audit.md:150-166,443`; `architecture/financial-architecture.md:264`
- Evidence: "#7: 'Bank opening balance legacy endpoint is disabled with 410 Gone' — not in 02-architecture."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### DOC-029: Manual payment / wallet review architecture under-documented
- Severity: Medium
- Category: Documentation Inconsistency
- Description: `ManualPaymentOptions`, `AdminManualPaymentController`, manual wallet approval/rejection, proof cleanup (`ManualWalletProofCleanupJob`), and runtime certification flags exist but are under-covered in the canonical financial doc (financial-completeness-audit #8). `04-payments.md` covers evidence rules but not the gated review/cleanup architecture.
- Recommendation: Document the manual-payment authority, feature gates, review certification, proof lifecycle, and cleanup job in `architecture/financial-architecture.md` and `04-payments.md`.
- Complexity: M
- Dependencies: DOC-011
- Affected Modules: AdminManualPaymentController, ManualPaymentOptions, ManualWalletProofCleanupJob
- Source Documents: `audits/financial-completeness-audit.md:167-175` (#8)
- Evidence: "#8: 'The architecture document mentions wallet approval, but does not document the manual-payment authority, feature gates, review certification, proof lifecycle, or cleanup path.'"
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### DOC-030: Frontend missing Idempotency-Key header for adjustment creation
- Severity: Medium
- Category: Technical Debt
- Description: `StatementOfAccountController.CreateAdjustment` requires the `Idempotency-Key` header (400 if absent), but the shipped `soaApi.createAdjustment`/UI does not send it (F-11-12/L-12). The endpoint is currently unusable from the browser as shipped — a contract gap, not merely a doc gap.
- Recommendation: Add `Idempotency-Key` (client-generated UUID) to the frontend adjustment call; document the contract in `FLOW-ACC-11`.
- Complexity: S
- Dependencies: DOC-010
- Affected Modules: soaApi.js, Adjustments.jsx, StatementOfAccountController
- Source Documents: `accounts/FLOW-ACC-11-ACCOUNT-ADJUSTMENTS.md:183-192` (F-11-12); `audits/ACCOUNTING-ARCHITECTURE-AUDIT-CHANGELOG.md:318-324`
- Evidence: "F-11-12: 'the server returns 400 if absent ... endpoint is currently unusable from the shipped UI until the header is added.'"
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### DOC-031: Financial read/access audit gaps
- Severity: Low
- Category: Known Limitation
- Description: Financial report generation has no per-request audit store (F-08-12); SOA GET reads (`query`/`balance`/`outstanding`) are not individually audited (F-09-02). "Who ran which report / viewed which statement" is not independently traceable beyond app logs.
- Recommendation: Optionally emit structured audit events on successful report generation and sensitive party-financial reads where regulatory audit-trail is required.
- Complexity: S
- Dependencies: none
- Affected Modules: FinancialReportsController, StatementOfAccountController
- Source Documents: `accounts/FLOW-ACC-08-FINANCIAL-REPORTS.md:172-179` (F-08-12); `accounts/FLOW-ACC-09-STATEMENT-OF-ACCOUNT.md:43-61` (F-09-02)
- Evidence: "F-08-12: 'NO separate report-request audit store recording who requested which report with which parameters.'"
- Implementation Required: no
- Architecture Only: no
- Documentation Only: no

### DOC-032: `IsTreasury` DTO field mapped from `Account.IsSystem`
- Severity: Low
- Category: Naming
- Description: `FinancialReportService.GetAccountSummariesAsync` sets `IsTreasury = a.IsSystem`; the DTO field name implies a narrower "treasury" concept while the source is the broad system-account flag (F-08-11/L-09). Confirmed naming/semantics mismatch.
- Recommendation: Rename DTO field to `IsSystem`, or introduce a real `IsTreasury` flag on `Account` if treasury distinction is needed.
- Complexity: S
- Dependencies: none
- Affected Modules: FinancialReportService, IFinancialReportService, Account
- Source Documents: `accounts/FLOW-ACC-08-FINANCIAL-REPORTS.md:161-168` (F-08-11)
- Evidence: "F-08-11: 'IsTreasury = a.IsSystem ... The DTO field is named IsTreasury but is populated from Account.IsSystem.'"
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### DOC-033: Discounts / coupons / promotions not traced to revenue/discount accounts
- Severity: Medium
- Category: Documentation Inconsistency
- Description: Coupons/promotions/discounts change invoice/order totals and therefore revenue recognition, but the canonical financial doc barely mentions them and does not trace their relationship to a discount contra-revenue account or stale-client total recomputation (financial-completeness-audit #7/#9). Tax is likewise not represented in the reviewed account template.
- Recommendation: Document the discount/coupon/promotion financial effect and trace it to the correct GL accounts in `architecture/financial-architecture.md`; state tax handling explicitly.
- Complexity: M
- Dependencies: DOC-011
- Affected Modules: Checkout, CommerceAccountingService, chart-of-accounts
- Source Documents: `audits/financial-completeness-audit.md:177-185,398-399` (#7, #9)
- Evidence: "#9: 'Discounts/promotions/coupons affect net sale totals but are not traced to revenue/discount accounts; no clear discount contra-revenue account was found.'"
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

---

### CONFLICT-001: Two in-KB audits disagree on financial-architecture documentation readiness
- Conflicting Documents: `audits/ACCOUNTING-ARCHITECTURE-AUDIT.md` (peer-reviewed FINAL VERDICT) vs `audits/financial-completeness-audit.md`
- Explanation: The peer-reviewed accounting audit concludes "No surviving CRITICAL accounting defect ... suitable as an external ERP architecture review input" (with the caveat of governance/reconciliation gaps). The independent completeness audit returns VERDICT: **FAIL** for the canonical `financial-architecture.md`, stating it is incomplete and overbroad and that money can move unexplained. Both live in the same KB and assess the same financial documentation/implementation.
- Possible Cause: Different scopes — the accounting audit peer-review corrected overstated payment findings and judged the *double-entry engine*; the completeness audit judged the *canonical financial doc's coverage* of every authority/path. The discrepancy is a scope/labeling mismatch, not necessarily a factual contradiction.
- Recommended Resolution: Either (a) explicitly scope the accounting audit as "engine correctness" and the completeness audit as "doc coverage," and state both verdicts side-by-side in INDEX, or (b) complete `financial-architecture.md` per the 16 recommendations and re-score. Do not let agents cite one verdict while ignoring the other.

### CONFLICT-002: "README is STALE" warning vs authoritative system READMEs
- Conflicting Documents: `02-architecture.md` / `03-inventory.md` / `04-payments.md` / `05-orders.md` / `06-security-identity.md` ("the README is STALE ... do not trust it") vs `INDEX.md` / `TASK_ROUTING.md` (granting `systems/*/README.md` authoritative system-guide status)
- Explanation: The core guides' blanket "README is STALE" statement refers to the PointOfSale repository top-level README, but the word "README" is also the exact filename of the authoritative `systems/*/README.md` system guides. A reader following the core guides can distrust the very system guides the INDEX declares authoritative.
- Possible Cause: Terminology collision; the core guides were written before/independent of the `systems/` README convention and never qualified which README they meant.
- Recommended Resolution: Replace every "the README" with "the repository top-level README (distinct from the authoritative `systems/<name>/README.md` system guides)" and add a one-line clarification in INDEX.

### CONFLICT-003: Party sub-ledger is either a secondary note or a primary authority, depending on the doc
- Conflicting Documents: `02-architecture.md` + `audits/ACCOUNTING-ARCHITECTURE-AUDIT.md` (GL/`FinancialPostingService` as the authoritative model, party sub-ledger as a light secondary note) vs `audits/financial-completeness-audit.md` (party `AccountLedger`/`LedgerService` is a live first-class financial authority that, undocumented, makes the canonical doc FAIL)
- Explanation: The architecture-centric docs frame the GL as the single truth and mention the party sub-ledger only parenthetically. The completeness audit treats the same sub-ledger as a co-equal authority that must be fully documented; its absence is scored as a documentation failure. The two positions disagree on whether the dual-ledger is a primary architecture that requires first-class documentation.
- Possible Cause: `02-architecture.md` predates the detailed financial audits; the completeness audit (later, 2026-07-20) surfaced the sub-ledger as material but the core doc was not updated.
- Recommended Resolution: Upgrade `02-architecture.md` and GLOSSARY to present the two-tier (GL + party sub-ledger) model explicitly (see DOC-018), and have the completeness audit's recommendations folded into the canonical `financial-architecture.md` (see DOC-011).

---

## Summary

- **What I did:** Read the entire assigned FILE SET — core guides `02`–`08`, `GLOSSARY`, `MENTAL_MODEL`, `CROSS_SYSTEM_CERTIFICATION`, the 5 `certification/*` docs, all 6 `systems/*/README.md`, `architecture/financial-architecture.md`, all 11 `accounts/FLOW-ACC-*` canonical financial docs, and every file under `_scratch/` and `audits/` (including the 6 AGENT-* sub-audits, the accounting audit + changelog + remediation report, and the independent `financial-completeness-audit`). I cross-checked the audit corpus against the remediation report to separate *resolved* from *open* items, and ran targeted `grep`/`find` verification for broken references and the spurious "R12" citation.
- **What I found (exhaustive backlog):** 33 documentation/consistency/naming/technical-debt issues (`DOC-001`–`DOC-033`) plus 3 cross-document conflicts (`CONFLICT-001`–`003`). Headline themes: (1) pervasive **dangling references** to `docs/architecture/*` and `*.certification-findings.md` that do not exist in the KB; (2) **INDEX/TASK_ROUTING staleness** — they omit ~12 top-level directories and the GLOSSARY/MENTAL_MODEL/CROSS_SYSTEM docs; (3) an **unresolved external-authority dependency** on `.ai-context/*` not present in the KB; (4) **stale canonical/audit docs** describing control gaps already fixed by the 2026-07-20 remediation; (5) the **two accounting audits disagree** on whether the financial documentation is audit-ready (CONFLICT-001); (6) genuine **platform-wide technical debt** still open (no period lock, supplier-payment reversal missing, adjustment lifecycle/linkage gaps, closure-audit durability, multi-currency, retained earnings, uneven tenant-isolation on reads, affiliate/treasury doc gaps).
- **Files I created/modified:** None in the repo. This is a read-only analysis deliverable; the backlog above is the output.
- **Issues encountered:** `search_files` fails on the hidden `.ai` directory (returned 0 / IO errors), so I used `terminal` `grep`/`find` for all content search — successful. No blockers to producing the backlog.

Note: Per the task's "ignore completed work" rule, items the remediation report marks **IMPLEMENTED** (adjustment/expense role auth, expense amount validation, expense integrity link scan, report date-range validation, partial tenant predicates) are excluded as code defects; where their *docs* were not updated, that surfaces as `DOC-010` (stale docs) rather than a code item.