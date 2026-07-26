# Sub-Agent 6 — Browser Knowledge / Hermes / Testing / Automation Engineering Backlog

**Domain:** Browser Knowledge, Hermes Browser Ontology, Page Archetypes, Task Routing, Testing/QA Automation
**Base path:** `.ai/hermes/`
**Compiled from (all read):** `HERMES_BROWSER_ONTOLOGY.md`, `HERMES_BROWSER_ONTOLOGY_v1_RELEASE.md`, `PAGE_ARCHETYPE_DISCOVERY.md`, `ARCHETYPE_RELATIONSHIPS.md`, `TASK_ROUTING.md`, the nine archetype contracts, `browser-knowledge/*.yaml` (19 files), `_scratch/order-*.md`, `audits/_scratch/*` (AGENT-06..11, CROSS-VALIDATION, FALSE-POSITIVE, AUDIT-METHODOLOGY), `audits/ACCOUNTING-ARCHITECTURE-AUDIT.md`, `audits/ACCOUNTING-ARCHITECTURE-REMEDIATION-REPORT.md`, cross-cutting surveys `02-08`, `INDEX.md`.

**Scope of extraction:** ONLY unresolved items — open bugs, architecture issues, missing features, technical debt, documentation inconsistencies, governance/monitoring gaps, and open questions in the *browser-knowledge / Hermes / testing / automation* surface. Completed/implemented/resolved findings from the remediation report are EXCLUDED, except where they reveal a *still-open* structural or documentation problem.

---

## CONFLICTS (Cross-document contradictions)

### CONFLICT: Browser-Knowledge YAMLs violate the ontology's "browser-observable only / declare-only-delta" mandate
- Conflicting Documents: `HERMES_BROWSER_ONTOLOGY.md` §6–7 vs `browser-knowledge/FLOW-ACC-*.yaml`, `browser-knowledge/FLOW-01..05*.yaml`
- Explanation: The ontology whitepaper and `ARCHETYPE_RELATIONSHIPS.md` §3 are explicit: Browser Knowledge is "the thinnest layer," declares ONLY page-specific delta, MUST NOT assert backend internals, and reasons ONLY over browser-observable state (DOM, network, storage, console, navigation, visual). Every one of the 19 YAMLs instead embeds deep backend implementation detail: SQL table names (`AccountLedger`, `LedgerJournalEntries`, `LedgerLines`), EF entities (`PartyLedgerAdjustment`, `CustomerTransaction`), service classes (`FinancialPostingService`, `LedgerService`, `CommerceAccountingService`), transaction semantics (`FOR UPDATE / UPDLOCK,ROWLOCK,HOLDLOCK`), outbox mechanics, and `runtime_models`/`runtimes` sequences of internal services. This is the opposite of "declare only the delta."
- Possible Cause: The `shopixy-browser-knowledge` skill that generated the YAMLs ingested implementation-backed accounting discovery docs (`accounts/FLOW-ACC-*.md`) and copied their internals rather than producing true archetype-deltas.
- Recommended Resolution: Either (a) rewrite the YAMLs to honor the ontology (route, archetype reference, page-specific fields, NON-EXISTENCE assertions, browser assertions only, no `runtime_models`/`runtimes`/`expected_runtime_after_success` backend sections), or (b) formally amend the ontology to permit a separate "implementation-backed" YAML tier and rename/scope these files so they are not mistaken for Hermes browser-test deltas.

### CONFLICT: Browser-Knowledge YAMLs never declare an `archetype` and never supply a true delta
- Conflicting Documents: `PAGE_ARCHETYPE_DISCOVERY.md` §8 ("Create a thin Browser-Knowledge YAML that declares `archetype: <name>` and supplies only page-specific deltas") vs all 19 `browser-knowledge/*.yaml` (grep confirms ZERO `archetype:` declarations)
- Explanation: The discovery doc defines the intended YAML contract precisely (Section 8.1–8.4, with worked examples). None of the produced YAMLs declare an archetype, none reference a canonical archetype document, and none are "thin" — they are 312–400 line monoliths. Hermes, per the ontology, is supposed to inherit behavior from the referenced archetype; with no archetype declared, the inheritance contract is unfulfilled and the files cannot be consumed as designed.
- Possible Cause: The generation skill produced standalone knowledge files, not ontology-compliant deltas.
- Recommended Resolution: Add an `archetype:` key + page-specific delta to every YAML, or remove them from the "Browser Knowledge" layer and reclassify as implementation reference docs.

### CONFLICT: YAML `Source of truth` headers cite non-existent canonical files for FLOW-01..05
- Conflicting Documents: `browser-knowledge/FLOW-01-SALES-KNOWLEDGE.yaml` (et al.) header `# Source of truth: .ai/hermes/accounts/FLOW-01-SALES.md` vs actual repository layout
- Explanation: The five sales/purchase YAMLs cite `accounts/FLOW-01-SALES.md`, `accounts/FLOW-02-CREDIT-SALES.md`, `accounts/FLOW-03-SALES-RETURN.md`, `accounts/FLOW-04-PURCHASE.md`, `accounts/FLOW-05-PURCHASE-RETURN.md`. None of these files exist in `accounts/`. The real canonical docs live elsewhere: `business-flows/FLOW-01-CASH-SALE.md`, `platform/FLOW-02-deferred-payment-credit-sale-canonical.md`, `business-flows/FLOW-03-SALES-RETURN.md`, `business-flows/FLOW-04-PURCHASE-INVOICE.md`, `business-flows/FLOW-05-PURCHASE-RETURN.md`. Every one of the five source-of-truth citations is broken.
- Possible Cause: Hard-coded path assumption in the generation skill (assumed an `accounts/` home for all FLOW docs).
- Recommended Resolution: Correct the header paths to point at the real canonical files; add a CI/lint check that every `Source of truth:` link resolves to an existing file.

### CONFLICT: Accounting audit "0 false positives" (FALSE-POSITIVE.md) reversed by peer review (ACCOUNTING-ARCHITECTURE-AUDIT.md)
- Conflicting Documents: `audits/_scratch/AGENT-FALSE-POSITIVE.md` ("No genuine false positives. Every finding is implementation-backed. SURVIVES: 54") vs `audits/ACCOUNTING-ARCHITECTURE-AUDIT.md` §11 ("The original '0 findings rejected' conclusion is no longer accurate… 2 INCORRECT… 7 downgrades… corrected evidence for H-02, H-05, M-16, M-04, M-19, L-09")
- Explanation: The false-positive elimination subagent concluded all 54 findings survived. The master peer-reviewed audit explicitly rejects two as incorrect (C-01 collected-order cancel with no reversal; order-refund accounting "missing") and corrects the evidence/severity of many others (e.g., Stripe/Paymob DO have webhook logs; cashbox closure audit is awaited-not-fire-and-forget; negative expenses do NOT post wrong-sign GL). The two reports contradict each other on the validity of the same findings.
- Possible Cause: The false-positive agent did not re-verify against the broader implementation (it only re-read cited lines) and the master audit performed deeper code review.
- Recommended Resolution: Reconcile the two reports; mark the 2 rejected findings and the corrected severities as authoritative in the consolidated audit; do not let the "0 false positives / 54 survive" claim propagate.

### CONFLICT: FLOW-ACC-06 canonical scope vs audit task file-list (scope mismatch acknowledged but unresolved)
- Conflicting Documents: `accounts/FLOW-ACC-06-PAYMENTS.md:6` (explicitly scopes OUT order-payment runtime) vs `audits/_scratch/AGENT-06-PAYMENTS.md` (audited the Order-Payment runtime exclusively)
- Explanation: The canonical doc excludes `PaymentLifecycleService`, gateway adapters, and CommerceHub payments, yet the audit task mapped exactly those files to it. The auditor flagged this as F-06-01/H-01 (HIGH) and recommended splitting ERP-party-payments from the Order/CommerceHub payment runtime into separate canonical flows/doc maps. The remediation report lists this as "VERIFIED, DOCUMENTATION DEFERRED" with remaining work "Split FLOW-ACC-06 docs by ERP party payments vs CommerceHub/order payments" — i.e., still unresolved.
- Possible Cause: Knowledge-base doc boundary and audit task generation were not synchronized.
- Recommended Resolution: Execute the documented split; create separate canonical flow docs (ERP party payments vs order/CommerceHub payment runtime) and update TASK_ROUTING accordingly.

### CONFLICT: Cross-cutting survey precedence vs ontology precedence
- Conflicting Documents: `INDEX.md` "Authority Precedence" (`.ai-context/*` › Domain Guide › `docs/architecture/*` › Source code › README) vs `AUDIT-METHODOLOGY.md` ("IMPLEMENTATION is ALWAYS the source of truth… if documentation and implementation disagree, IMPLEMENTATION WINS")
- Explanation: The Knowledge-Base index places source code *below* documentation and `.ai-context/*`, while the audit methodology (which the accounting audits followed) places implementation *above* documentation. For an agent reasoning about browser/QA behavior these two precedence rules point in opposite directions when doc and code disagree.
- Possible Cause: Different documents written for different audiences (navigational index vs audit methodology) without reconciling precedence.
- Recommended Resolution: Document a single, explicit precedence rule distinguishing "durable architecture QA truth" (where `.ai-context`/guides win) from "implementation-discrepancy investigation" (where code wins), or merge into one statement.

### CONFLICT: `ARCHETYPE_RELATIONSHIPS.md` says knowledge layer is "future `*KNOWLEDGE.yaml`" but they now exist and violate the layer's own rules
- Conflicting Documents: `ARCHETYPE_RELATIONSHIPS.md` §1 ("Knowledge layer — future `*KNOWLEDGE.yaml` … declare only page-specific deltas") vs the existing `browser-knowledge/*.yaml` (monolithic, backend-internal)
- Explanation: The relationship spec anticipated a clean future knowledge layer; the layer now physically exists but does not conform to the described shape, so the document's description of the layer is now inaccurate.
- Possible Cause: Layer shipped before its conformance was validated against the spec.
- Recommended Resolution: Update the relationship/ontology docs to describe the *actual* YAML shape, or fix the YAMLs (see CONFLICT 1/2).

### CONFLICT: INDEX.md "Authority Precedence" omits the `.ai/hermes/audits/` and `accounts/` trees and the `browser-knowledge` layer entirely
- Conflicting Documents: `INDEX.md` (Knowledge Map + reading order covers only 02–08 core guides + systems + certifications) vs actual repo (contains `accounts/`, `browser-knowledge/`, `audits/`, `business-flows/`, `platform/`, `systems/`, `_scratch/`)
- Explanation: The index's reading order and knowledge map do not mention the accounting flow docs, the browser-knowledge YAMLs, or the audit tree. Hermes has no canonical routing instruction telling it these layers exist or which wins when they disagree with a core guide.
- Possible Cause: Index predates the accounting/browser-knowledge/audit layers.
- Recommended Resolution: Extend INDEX.md and TASK_ROUTING.md to register `accounts/`, `browser-knowledge/`, `business-flows/`, `platform/`, and `audits/` as first-class layers with explicit precedence among them.

---

## ENGINEERING BACKLOG (HER-###)

### HER-001: Browser-Knowledge YAMLs embed backend implementation internals (ontology violation)
- Severity: Architectural Improvement
- Category: Documentation Inconsistency
- Description: All 19 YAMLs contain SQL table names, EF entity names, service class names, locking hints, and outbox internals under `runtime_models`/`runtimes`/`expected_runtime_after_success`. The ontology (§6–7) mandates browser-observable-only, delta-only knowledge. This makes the files unusable as Hermes test deltas and bloats them 10–20x.
- Recommendation: Strip backend internals; keep route, endpoints, fields, browser assertions, NON-EXISTENCE claims. Re-scope the generation skill.
- Complexity: XL
- Dependencies: CONFLICT-1, CONFLICT-2
- Affected Modules: browser-knowledge/*, shopixy-browser-knowledge skill
- Source Documents: HERMES_BROWSER_ONTOLOGY.md:84-92; browser-knowledge/FLOW-ACC-04-GENERAL-LEDGER-KNOWLEDGE.yaml:240-274
- Evidence: "Browser Knowledge is the thinnest layer… declare only the delta" (ontology §6); YAML `runtimes:` block lists `StatementOfAccountController → LedgerService → Party Balance Update → Accounting Outbox`.
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: yes

### HER-002: YAMLs never declare an archetype or supply a true delta
- Severity: High
- Category: Architecture Issue
- Description: grep across all 19 YAMLs returns ZERO `archetype:` declarations. The ontology's inheritance contract (load archetype, apply delta) is unmet. Hermes cannot inherit generic behavior and will either ignore the file or treat it as a standalone spec.
- Recommendation: Add `archetype:` + page delta to each YAML per PAGE_ARCHETYPE_DISCOVERY §8.4 examples, or reclassify files.
- Complexity: L
- Dependencies: CONFLICT-2
- Affected Modules: browser-knowledge/*, PAGE_ARCHETYPE_DISCOVERY.md §8
- Source Documents: PAGE_ARCHETYPE_DISCOVERY.md:697-721; ARCHETYPE_RELATIONSHIPS.md:22-26
- Evidence: "Create a thin Browser-Knowledge YAML that declares `archetype: <name>` and supplies only page-specific deltas" — none of the 19 files do.
- Implementation Required: yes
- Architecture Only: yes
- Documentation Only: yes

### HER-003: Broken `Source of truth` citation paths in FLOW-01..05 YAMLs
- Severity: Medium
- Category: Documentation Inconsistency
- Description: FLOW-01-SALES, FLOW-02-CREDIT-SALES, FLOW-03-SALES-RETURN, FLOW-04-PURCHASE, FLOW-05-PURCHASE-RETURN YAMLs cite `accounts/FLOW-0X-*.md` which do not exist. Real docs are in `business-flows/` and `platform/`. A Hermes agent following the header cannot locate the canonical source.
- Recommendation: Fix the five header paths; add a link-checker CI step over all `.ai/hermes` `Source of truth:` / cross-references.
- Complexity: S
- Dependencies: none
- Affected Modules: browser-knowledge/FLOW-0[1-5]*.yaml
- Source Documents: browser-knowledge/FLOW-01-SALES-KNOWLEDGE.yaml:2 (vs actual business-flows/FLOW-01-CASH-SALE.md)
- Evidence: `ls accounts/ | grep FLOW-0` → only FLOW-ACC-* present; `business-flows/` holds the real FLOW-01/03/04/05 docs.
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### HER-004: Reconcile contradictory audit verdicts (FALSE-POSITIVE vs master audit)
- Severity: High
- Category: Documentation Inconsistency
- Description: AGENT-FALSE-POSITIVE claims all 54 findings survive with 0 rejected; the master ACCOUNTING-ARCHITECTURE-AUDIT rejects 2 as INCORRECT and downgrades/corrects 7+. The two disagree on validity/severity of the same items. Left unreconciled, downstream consumers cannot trust either severity table.
- Recommendation: Publish a single consolidated finding register; mark C-01 and order-refund portion of C-02 as REJECTED; adopt corrected severities (H-02→MEDIUM, H-05→MEDIUM, M-16 heterogeneous-not-absent, M-04 awaited-not-fire-and-forget, M-19 partial, L-09 still-wrong).
- Complexity: M
- Dependencies: CONFLICT-4
- Affected Modules: audits/ACCOUNTING-ARCHITECTURE-AUDIT.md, audits/_scratch/AGENT-FALSE-POSITIVE.md
- Source Documents: audits/_scratch/AGENT-FALSE-POSITIVE.md:7-15 vs audits/ACCOUNTING-ARCHITECTURE-AUDIT.md:398-409
- Evidence: "0 findings rejected" (FALSE-POSITIVE) vs "2 INCORRECT … 7 downgrades" (master audit §15).
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### HER-005: FLOW-ACC-06 canonical scope/runtime split still deferred
- Severity: High
- Category: Documentation Inconsistency
- Description: The ERP party-payment vs Order/CommerceHub payment-runtime split was flagged HIGH (F-06-01/H-01) and remains "VERIFIED, DOCUMENTATION DEFERRED" with no split executed. TASK_ROUTING and the canonical doc still conflate the two, risking mis-routed Hermes audits.
- Recommendation: Create separate canonical flow docs; update TASK_ROUTING matrix to route order/CommerceHub payments to the new doc and ERP party payments to FLOW-ACC-06.
- Complexity: M
- Dependencies: CONFLICT-5
- Affected Modules: accounts/FLOW-ACC-06-PAYMENTS.md, TASK_ROUTING.md
- Source Documents: accounts/FLOW-ACC-06-PAYMENTS.md:6; audits/ACCOUNTING-ARCHITECTURE-REMEDIATION-REPORT.md:206-211
- Evidence: "Split FLOW-ACC-06 docs by ERP party payments vs CommerceHub/order payments" listed as remaining work, status deferred.
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### HER-006: Single, unambiguous authority-precedence rule across KB and audit methodology
- Severity: Medium
- Category: Documentation Inconsistency
- Description: INDEX.md precedence (`.ai-context` › Guide › docs › code › README) conflicts with AUDIT-METHODOLOGY ("implementation always wins"). An agent resolving a doc-vs-code disagreement gets opposite instructions.
- Recommendation: Define two clearly-labeled modes (durable-QA-truth vs implementation-discrepancy-investigation) or unify into one ordered rule with documented exceptions.
- Complexity: S
- Dependencies: CONFLICT-6
- Affected Modules: INDEX.md, audits/_scratch/AUDIT-METHODOLOGY.md
- Source Documents: INDEX.md:291-309; AUDIT-METHODOLOGY.md:7-9
- Evidence: INDEX precedence puts code at #4; methodology says code wins over docs.
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### HER-007: Knowledge-Base index does not register accounting/browser-knowledge/audit layers
- Severity: Medium
- Category: Documentation Inconsistency
- Description: INDEX.md reading order and Knowledge Map cover only core guides 02–08 + systems + certifications. The `accounts/`, `browser-knowledge/`, `business-flows/`, `platform/`, and `audits/` trees are absent, so Hermes has no routing instruction for them and no precedence when they disagree with a core guide.
- Recommendation: Extend INDEX.md and TASK_ROUTING.md to register these layers with explicit intra-layer precedence.
- Complexity: M
- Dependencies: CONFLICT-8
- Affected Modules: INDEX.md, TASK_ROUTING.md
- Source Documents: INDEX.md:30-437; ls of `.ai/hermes/` top-level
- Evidence: INDEX "Core Guides" lists 02–08 only; repo contains accounts/, browser-knowledge/, audits/, business-flows/, platform/.
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### HER-008: No Hermes browser-test coverage for the 14 Transaction / 38 Report / 13 Workflow accounting surfaces
- Severity: High
- Category: Known Limitation
- Description: PAGE_ARCHETYPE_DISCOVERY classifies 145 routes (incl. 14 Transaction, 38 Report, 13 Workflow pages touching accounting: POS, PurchaseInvoice, ManualPayments, BalanceTransfer, StatementOfAccount, Expenses, Adjustments, DailyClose, AR/AP aging, OutstandingBalances, treasury statements). The `browser-knowledge/` layer contains only 19 YAMLs (11 accounting flows + 4 master-data + 4 purchase/sales + 1 leftover), leaving the majority of accounting surfaces with NO Hermes browser-knowledge file at all.
- Recommendation: Generate ontology-compliant (`archetype:` + delta) YAMLs for the remaining unmatched accounting surfaces; prioritize Transaction/Workflow money-movement pages.
- Complexity: XL
- Dependencies: HER-001, HER-002
- Affected Modules: browser-knowledge/, PAGE_ARCHETYPE_DISCOVERY.md §7.2
- Source Documents: PAGE_ARCHETYPE_DISCOVERY.md:670-681; browser-knowledge/ file listing
- Evidence: 145 classified routes vs 19 YAMLs; accounting Transaction/Workflow pages (POS, DailyClose, Adjustments, PurchaseReturns) are only partially covered.
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: yes

### HER-009: Master-data YAMLs (MD-01..04) and FLOW-ACC-05/06/07 also violate the delta/observable rule; no coverage for MD domains in archetype discovery caveats
- Severity: Medium
- Category: Documentation Inconsistency
- Description: MD-01-PRODUCT, MD-02-WAREHOUSE-INVENTORY, MD-03-CUSTOMER, MD-04-SUPPLIER YAMLs, plus FLOW-ACC-05/06/07, repeat the same backend-internal pattern (services, tables, locks, outbox). They are CRUD/Dashboard/Transaction surfaces that should be thin archetype deltas.
- Recommendation: Apply the same remediation as HER-001/002 to these files.
- Complexity: L
- Dependencies: HER-001, HER-002
- Affected Modules: browser-knowledge/MD-0[1-4]*.yaml, FLOW-ACC-05/06/07*.yaml
- Source Documents: browser-knowledge/MD-01-PRODUCT-KNOWLEDGE.yaml:240-280 (runtimes)
- Evidence: MD YAMLs contain `runtime_models:` service chains identical in shape to the ACC YAMLs.
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: yes

### HER-010: `ARCHETYPE_RELATIONSHIPS.md` description of the knowledge layer is now stale
- Severity: Low
- Category: Documentation Inconsistency
- Description: The relationship spec describes the knowledge layer as a "future" thin-delta tier; the layer exists but is monolithic/backend-heavy, so the doc misdescribes current reality (CONFLICT-7).
- Recommendation: After HER-001/002 fix the YAMLs, re-validate and update the relationship doc's §1 wording.
- Complexity: S
- Dependencies: HER-001, HER-002, CONFLICT-7
- Affected Modules: ARCHETYPE_RELATIONSHIPS.md §1
- Source Documents: ARCHETYPE_RELATIONSHIPS.md:20-26
- Evidence: "future `*KNOWLEDGE.yaml`" vs existing non-conforming files.
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### HER-011: Remediation report deferred governance/reconciliation items are unresolved and owned by no tracker
- Severity: High
- Category: Governance Concern
- Description: The remediation report verified-and-deferred 14 items (e.g., M-02 cashbox close race, M-04 non-atomic closure audit, M-05 party-floor, M-06 stored JournalEntryId link, M-07 adjustment lifecycle, M-08/M-13/M-21 period-lock owner, M-01/M-19 party-balance ownership, M-11/CV-10 outbox-lag signal, M-16/CV-12 webhook standardization). Each carries "Remaining Work" but none is filed as a tracked backlog item; they risk indefinite deferral.
- Recommendation: Promote every "VERIFIED, DEFERRED" item with remaining work into this backlog (or a governance tracker) with owner/priority; close only on implementation.
- Complexity: M
- Dependencies: none
- Affected Modules: audits/ACCOUNTING-ARCHITECTURE-REMEDIATION-REPORT.md §Verified But Deferred
- Source Documents: audits/ACCOUNTING-ARCHITECTURE-REMEDIATION-REPORT.md:132-232
- Evidence: 14 deferred items each end with "Remaining Work:" but no issue/ticket reference.
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### HER-012: Full solution test suite fails; accounting-targeted tests pass in isolation — no triage owner
- Severity: Medium
- Category: Validation Gap
- Description: Remediation report: targeted accounting tests 20/20 pass and build passes, but full `dotnet test PointOfSale.sln` fails with "many unrelated pre-existing/non-accounting failures" (AdminRouteAuthorization hardening, CommerceHub freezes, SQLite migration syntax in EcommerceRefundLifecycleTests, storefront/shipping/CommerceHub integration tests). The failures are acknowledged but untriaged.
- Recommendation: File the full-suite failures as a separate triage backlog; add a CI gate that runs the accounting-targeted filter green while the broader suite is repaired.
- Complexity: M
- Dependencies: none
- Affected Modules: PointOfSale.Tests, CI pipeline
- Source Documents: audits/ACCOUNTING-ARCHITECTURE-REMEDIATION-REPORT.md:18-20,254-263
- Evidence: "Full solution tests: Failed with many unrelated pre-existing/non-accounting failures."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### HER-013: No outbox-drained / GL-converged operator signal (async visibility gap)
- Severity: Medium
- Category: Monitoring Gap
- Description: CV-10 / M-11 (VERIFIED, deferred). Async-posted flows (expenses, adjustments, supplier payments) have no endpoint surfacing whether the outbox has drained for a tenant/entity. Reads cannot distinguish "outbox lag" from "real divergence." Remediation added the expense scan but not the signal.
- Recommendation: Expose `GET /ledger/consistency/{tenant}` (outbox lag, last-posted, unposted-adjustment indicator) consumed by reports/reconciliation UI.
- Complexity: M
- Dependencies: HER-011
- Affected Modules: FinancialIntegrityService, reporting UI
- Source Documents: audits/_scratch/AGENT-CROSS-VALIDATION.md:CV-10; ACCOUNTING-ARCHITECTURE-AUDIT.md:M-11
- Evidence: "No endpoint surfaces whether the outbox has drained for a tenant/entity, and no per-tenant lag metric exists."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### HER-014: Accounting-period / locked-period guard has no shared owner (governance gap)
- Severity: Medium
- Category: Governance Concern
- Description: CV-11 / M-08/M-13/M-21 (VERIFIED, deferred). No `IAccountingPeriodLock`/`IAccountingPeriodService` is invoked by any financial write path. Adjustments and expenses do not check closed periods; reports state period-lock "belongs to the posting engine" but it is not implemented anywhere. Who owns the lock is undefined.
- Recommendation: Introduce a single `IAccountingPeriodService.IsClosed(tenant, date)` invoked by all financial write paths (expenses, adjustments, payments, POS sale) before commit.
- Complexity: L
- Dependencies: HER-011
- Affected Modules: all financial write controllers/services
- Source Documents: audits/_scratch/AGENT-CROSS-VALIDATION.md:CV-11; ACCOUNTING-ARCHITECTURE-AUDIT.md:M-21
- Evidence: "No shared `IAccountingPeriodLock` service is invoked by any financial write path."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### HER-015: Supplier-payment void/reversal workflow missing (governance control gap)
- Severity: Medium
- Category: Missing Feature
- Description: C-02 / CV-02 (PARTIALLY VERIFIED, MEDIUM; order-refund portion rejected as false positive). `CommerceAccountingService` has `PostSupplierPaymentAsync` but no `ReverseSupplierPaymentAsync`/`CancelSupplierPaymentAsync`; `SuppliersController` has no cancel/refund endpoint. If a supplier payment is operationally unwound, no symmetric accounting path exists. Deferred in remediation.
- Recommendation: Add a first-class supplier-payment reversal/void command posting through existing posting/outbox infrastructure.
- Complexity: L
- Dependencies: HER-011
- Affected Modules: CommerceAccountingService, SuppliersController
- Source Documents: audits/ACCOUNTING-ARCHITECTURE-AUDIT.md:C-02; ACCOUNTING-ARCHITECTURE-REMEDIATION-REPORT.md:134-140
- Evidence: "Supplier payment creation exists… but no `ReverseSupplierPayment`, `CancelSupplierPayment`, or supplier-payment reversal outbox path was found."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### HER-016: Manual correction endpoints lacked consistent role authorization (corrected post-remediation but residual policy debt)
- Severity: Medium
- Category: Security Concern
- Description: F-11-04/H-04 and CV-03/H-06 were IMPLEMENTED in remediation (role attributes added to `CreateAdjustment`, `ApproveExpense`, cashbox close). However the remediation notes a residual: "Replace role string with shared policy if/when the platform formalizes accounting policies" and "Shared named policy remains recommended." SoD is now enforced by string roles, not a named `AccountingAdjustmentPolicy`.
- Recommendation: Introduce a shared `AccountingGovernancePolicy` (or equivalent) and replace scattered role-string attributes across correction endpoints.
- Complexity: M
- Dependencies: none
- Affected Modules: StatementOfAccountController, ExpensesController, CashboxController
- Source Documents: audits/ACCOUNTING-ARCHITECTURE-REMEDIATION-REPORT.md:24-46 (Remaining Work)
- Evidence: "Remaining Work: Replace role string with shared policy if/when the platform formalizes accounting policies."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### HER-017: Frontend does not send required `Idempotency-Key` for adjustment creation (endpoint unusable from shipped UI)
- Severity: Medium
- Category: Bug
- Description: F-11-12/L-12 (VERIFIED). `StatementOfAccountController.CreateAdjustment` requires the `Idempotency-Key` header (400 if absent), but `soaApi.createAdjustment` does not set it. The adjustment endpoint is currently non-functional from the browser as shipped.
- Recommendation: Generate a client-side UUID and send `Idempotency-Key` in `soaApi.createAdjustment` (and any other Hermes-driven UI path).
- Complexity: S
- Dependencies: none
- Affected Modules: PointOfSale.Web/src/services/soaApi.js, Adjustments.jsx
- Source Documents: audits/_scratch/AGENT-11-ADJUSTMENTS.md:F-11-12; ACCOUNTING-ARCHITECTURE-AUDIT.md:L-12
- Evidence: "server requires header (`:539-546`); frontend `soaApi.createAdjustment` does not provide one."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### HER-018: Party-balance ownership split (authoritative ledger + cache + supplier mirror)
- Severity: Medium
- Category: Architecture Issue
- Description: CV-08 / F-09-01 / F-09-03 / M-01 / M-19 (VERIFIED, partially-deferred). `AccountLedger.RunningBalance` is authority; `Customer/Supplier.Balance` is a cache; `SupplierTransactions.BalanceAfter` is a third mirror used for supplier outstanding aux aggregates. Customer path sources aux from `AccountLedgers`, supplier path from the mirror — inconsistent. Remediation added tenant predicates but full ownership cleanup remains.
- Recommendation: Make `AccountLedger` the single source for ALL party aggregates (customer + supplier); deprecate `SupplierTransactions.BalanceAfter` as aux source; unify `BalanceAfter` capture (use `Apply*Transaction` return value everywhere).
- Complexity: L
- Dependencies: HER-011
- Affected Modules: StatementOfAccountController, LedgerService
- Source Documents: audits/_scratch/AGENT-CROSS-VALIDATION.md:CV-08; ACCOUNTING-ARCHITECTURE-AUDIT.md:M-19
- Evidence: "Supplier outstanding sub-aggregates… read from `SupplierTransactions` mirror, not `AccountLedgers`" while customer branch uses `AccountLedgers`.
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### HER-019: Reversed / pre-GL reporting semantics differ across surfaces
- Severity: Medium
- Category: Known Limitation
- Description: CV-09 / M-20 / F-10-02 (VERIFIED). Financial Reports net `Reversed` correctly; Daily-Closing Z-Report double-counted reversed source journals in category subtotals (partially fixed in remediation — posted-only now, but standardization across all surfaces still open); Expense report shows approved-but-not-yet-GL-posted rows. Three different consistency conventions for the same underlying state.
- Recommendation: Standardize reversal and pre-GL disclosure conventions across Reports, Daily Closing, and Expense report; document the chosen rule once.
- Complexity: M
- Dependencies: HER-011
- Affected Modules: FinancialReportService, ReportsController, ExpensesController
- Source Documents: audits/_scratch/AGENT-CROSS-VALIDATION.md:CV-09; ACCOUNTING-ARCHITECTURE-AUDIT.md:M-20
- Evidence: "three reporting surfaces apply three different consistency semantics to the same underlying reversal/lag state."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### HER-020: Cashbox concurrent-close race (no pessimistic lock / conditional update)
- Severity: Medium
- Category: Bug
- Description: F-10-01 / M-02 (VERIFIED, deferred). `CloseCashbox` reads `IsActive` then updates without row lock or conditional `WHERE IsActive=1`; two overlapping closes each pass the guard, producing duplicate/ambiguous closure audit records (no financial corruption, but audit-integrity risk).
- Recommendation: Use `UPDATE … WHERE Id=@id AND IsActive=1` (treat 0 affected rows as already closed) or `RepeatableRead`/pessimistic lock on close.
- Complexity: M
- Dependencies: HER-011
- Affected Modules: CashboxController
- Source Documents: audits/_scratch/AGENT-10-DAILYCLOSING.md:F-10-01; ACCOUNTING-ARCHITECTURE-REMEDIATION-REPORT.md:142-148
- Evidence: "Two concurrent requests both read `IsActive=true`, both set `false`, both commit."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### HER-021: Cashbox closure audit is post-commit, non-atomic (can commit with no audit record)
- Severity: Medium
- Category: Auditability Gap
- Description: F-10-03 / M-04 (VERIFIED, deferred; master audit corrected "fire-and-forget" wording to "awaited, non-durable fallback"). `CloseCashbox` commits state, THEN awaits audit; `AuditService.EnqueueLog` falls back to an in-memory queue on failure — a closure can persist with no audit row.
- Recommendation: Persist closure audit inside the close transaction (same `_context`/scope) or via a durable audit outbox; alert if in-memory queue depth grows.
- Complexity: M
- Dependencies: HER-011
- Affected Modules: CashboxController, AuditService
- Source Documents: audits/_scratch/AGENT-10-DAILYCLOSING.md:F-10-03; ACCOUNTING-ARCHITECTURE-AUDIT.md:M-04
- Evidence: "closure audit remains post-commit and best-effort… non-durable fallback queue."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### HER-022: Adjustments lack stored JournalEntryId link and lifecycle (passive reconciliation only)
- Severity: Medium
- Category: Architecture Issue
- Description: F-11-06/M-06 (party GL link) and F-11-07/M-07 (approval/reversal/cancellation lifecycle) VERIFIED, deferred. `PartyLedgerAdjustment` has no `JournalEntryId` and no status/lifecycle fields; linkage is reconstructed by `SourceType`/`SourceId` string match via `ValidateAdjustmentLinksAsync`, run on a schedule. No in-system undo of an erroneous adjustment.
- Recommendation: Store posted `JournalEntryId` on successful posting; add a reversal/void workflow or formally document compensating-entry-only policy + UI path.
- Complexity: L
- Dependencies: HER-011
- Affected Modules: PartyLedgerAdjustment entity, AccountingOutboxProcessor, FinancialIntegrityService
- Source Documents: audits/_scratch/AGENT-11-ADJUSTMENTS.md:F-11-06,F-11-07; ACCOUNTING-ARCHITECTURE-REMEDIATION-REPORT.md:166-180
- Evidence: "`PartyLedgerAdjustment` has no `JournalEntryId`… Link validation reconstructs by `SourceType`/`SourceId`."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### HER-023: No universal negative-amount / party-floor validation policy (uneven boundary validation)
- Severity: Medium
- Category: Validation Gap
- Description: CV-04 / H-07 / M-07 (PARTIALLY VERIFIED). Remediation implemented expense-amount>0 and cashbox ActualCash>=0 guards, but the party running-balance floor (F-11-05/M-05) remains deferred ("business policy absent") and validation is still per-path ad hoc rather than a shared `IAccountingValidationPolicy`.
- Recommendation: Centralize signed-amount + party-floor validation invoked by all financial write paths; define configurable party-floor rules where business requires.
- Complexity: M
- Dependencies: HER-011
- Affected Modules: LedgerService, ExpenseDTOs, CashboxDTOs, all financial write controllers
- Source Documents: audits/_scratch/AGENT-CROSS-VALIDATION.md:CV-04; ACCOUNTING-ARCHITECTURE-REMEDIATION-REPORT.md:72-82,158-164
- Evidence: "Party running balances have no configurable floor… cashbox `ActualCash` accepts negative values [now guarded]; policy remains deferred."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### HER-024: Tenant-isolation read/write asymmetry broader than Expenses (defense-in-depth gap)
- Severity: Medium
- Category: Security Concern
- Description: CV-05 / M-17 / F-07-03 (VERIFIED, partially-implemented in remediation). Explicit tenant predicates were added to some high-value read surfaces, but the pattern (reads trust global filter, writes add explicit predicate) remains uneven across SOA, daily-close, cashbox, and non-accounting read surfaces. A global-filter regression would leak reads.
- Recommendation: Continue the explicit `TenantId == RequiredTenantId` predicate pattern across ALL read surfaces (accounting and non-accounting); add a CI test asserting read queries carry an explicit tenant predicate or justification.
- Complexity: L
- Dependencies: HER-011
- Affected Modules: StatementOfAccountController, ReportsController, CashboxController, ExpensesController, non-accounting read controllers
- Source Documents: audits/_scratch/AGENT-CROSS-VALIDATION.md:CV-05; ACCOUNTING-ARCHITECTURE-REMEDIATION-REPORT.md:96-106
- Evidence: "Expense reads, SOA reads, daily close, and some cashbox reads rely primarily on global filters."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### HER-025: Webhook idempotency/logging semantics heterogeneous across providers/routes
- Severity: Low
- Category: Known Limitation
- Description: CV-12 / F-06-03 / M-16 (PARTIALLY VERIFIED — master audit corrected the categorical "Stripe/Paymob have zero logs" claim; they DO log, but semantics/naming differ per route/runtime). No uniform durable webhook-log contract.
- Recommendation: Standardize webhook idempotency semantics and naming across all payment/provider controllers; persist a durable `PaymentWebhookLog` with unique `(TenantId, ProviderEventId)` at every webhook entry.
- Complexity: M
- Dependencies: HER-011
- Affected Modules: Fawry/Stripe/Paymob/CommerceHub webhook controllers
- Source Documents: audits/_scratch/AGENT-06-PAYMENTS.md:F-06-03; ACCOUNTING-ARCHITECTURE-AUDIT.md:M-16
- Evidence: "logging/idempotency is heterogeneous by route/runtime" (categorical absence claim corrected).
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### HER-026: Financial report `from <= to` range validation implemented, but no common date-range validator elsewhere
- Severity: Low
- Category: Technical Debt
- Description: M-12/F-08-06 (IMPLEMENTED in remediation for financial reports) — inverted ranges now rejected. Remaining work: "Optional common date-range validator" not done; other date-range report surfaces may still silently return empty sets on inversion.
- Recommendation: Extract a shared `ValidateDateRange(from,to)` used by all reporting endpoints.
- Complexity: S
- Dependencies: none
- Affected Modules: FinancialReportsController + other report controllers
- Source Documents: audits/ACCOUNTING-ARCHITECTURE-REMEDIATION-REPORT.md:108-118
- Evidence: "Remaining Work: Optional common date-range validator."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### HER-027: Multi-currency, retained-earnings close, cash-flow investing/financing, aging-in-SOA, export/print — documented NOT-IMPLEMENTED gaps with no decision record
- Severity: Low
- Category: Future Enhancement
- Description: F-08-08/F-09-05 (multi-currency), F-08-09 (retained earnings), F-08-10 (cash-flow sections), F-09-06 (aging in SOA), F-09-07/F-07 (export/print) are all VERIFIED "NOT IMPLEMENTED" with remediation note "implement only if target market requires." There is no recorded scope decision (which tenants/markets need them), so they linger as open questions.
- Recommendation: Capture an explicit scope decision per gap (required / rejected / future) signed by product; close as documented-decision where not required.
- Complexity: S
- Dependencies: none
- Affected Modules: FinancialReportService, SOA controller, Expense controller
- Source Documents: audits/ACCOUNTING-ARCHITECTURE-AUDIT.md:M-14,M-15,M-16(low),F-08-10; REMEDIATION:213-232
- Evidence: "Treat as a target-market scope decision; do not implement unless multi-currency tenants are required."
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### HER-028: "0 false positives / 66 survive" claim in FALSE-POSITIVE report is itself stale and should be retracted
- Severity: Low
- Category: Documentation Inconsistency
- Description: AGENT-FALSE-POSITIVE.md headline ("NO genuine false positives. Every finding is implementation-backed… 54 SURVIVES") is contradicted by the master audit which rejects 2 and corrects 7. The false-positive report was not updated after peer review.
- Recommendation: Append a retraction/correction note to AGENT-FALSE-POSITIVE.md pointing to the master audit §11; or fold it into the consolidated register (HER-004).
- Complexity: S
- Dependencies: HER-004
- Affected Modules: audits/_scratch/AGENT-FALSE-POSITIVE.md
- Source Documents: audits/_scratch/AGENT-FALSE-POSITIVE.md:7-15
- Evidence: Same findings later marked INCORRECT/PARTIALLY VERIFIED in master audit.
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### HER-029: Ontology has no mechanism to detect non-conforming Browser-Knowledge YAMLs (no validation/lint)
- Severity: Medium
- Category: Validation Gap
- Description: The ontology mandates delta-only/archetype-referencing YAMLs and "no shadow architecture," but nothing validates that a YAML actually conforms (no schema check for `archetype:`, no detector for backend-internal sections like `runtime_models`/`runtimes`/`expected_runtime_after_success`, no link-checker for `Source of truth:`). Non-conforming files (HER-001/002/003) shipped undetected.
- Recommendation: Add a CI lint over `browser-knowledge/**.yaml` enforcing: required `archetype:` key; absence of forbidden backend-internal keys; all `Source of truth:` paths resolve; required sections (route, browser_assertions, NON-EXISTENCE) present.
- Complexity: M
- Dependencies: HER-001, HER-003
- Affected Modules: shopixy-browser-knowledge skill, CI
- Source Documents: HERMES_BROWSER_ONTOLOGY.md:75-79 (MUST NOT duplicate / declare only delta); browser-knowledge/*.yaml (violations present)
- Evidence: 19 YAMLs with `runtimes:`/`runtime_models:` blocks and zero `archetype:` keys passed whatever generation step produced them.
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### HER-030: `_scratch/` and `audits/_scratch/` working files are unmanaged (no consolidation/retention policy)
- Severity: Low
- Category: Governance Concern
- Description: `_scratch/order-*.md` (3 large evidence reports) and `audits/_scratch/AGENT-*.md` (11 audit drafts + cross-validation + false-positive + methodology) are raw working artifacts left in the KB. They contain overlapping/competing claims (e.g., the false-positive vs master-audit conflict) and no pointer to the consolidated `audits/ACCOUNTING-ARCHITECTURE-AUDIT.md`. A future agent may read a stale draft as truth.
- Recommendation: Add a README/INDEX in `_scratch/` and `audits/_scratch/` stating these are superseded by the consolidated audit; or move them to an archive. Establish a retention policy (scratch = ephemeral, delete after consolidation).
- Complexity: S
- Dependencies: HER-004
- Affected Modules: _scratch/, audits/_scratch/
- Source Documents: directory listings of _scratch/ and audits/_scratch/
- Evidence: 11 AGENT-*.md drafts coexist with the master audit that overrides several of their conclusions.
- Implementation Required: no
- Architecture Only: no
- Documentation Only: yes

### HER-031: Order CQRS/projection governance findings (from _scratch evidence reports) are unowned by the accounting audit
- Severity: Medium
- Category: Risk
- Description: `_scratch/order-cqrs-crossdomain.md` surfaces real risks outside the accounting audit's scope: (a) no compile/runtime invariant enforces `OrderDocument.Total == SQL Order.Total` (projection can drift between events); (b) `CommerceHubMarketplaceOrder` read model can linger after storefront order tombstone (rebuild returns NotFound, not delete); (c) commission double-source race (event projection vs SQL-authority repair) with a noted stale-repair edge; (d) no canonical `OrderStatus` state machine (transition rules duplicated across `OrderStatusSyncService` and `OrderFulfillmentService`). These are testing/automation-relevant but were never folded into any tracked backlog.
- Recommendation: File these as a separate order/projection engineering backlog (or hand to the Orders domain owner); add a projection-total convergence test and a canonical `OrderStatus.CanTransitionTo`.
- Complexity: L
- Dependencies: none
- Affected Modules: OrderProjectionConsumer, StorefrontOrderProjectionRepairService, OrderStatusSyncService, OrderFulfillmentService
- Source Documents: _scratch/order-cqrs-crossdomain.md §Authority divergences/risks, §Missing evidence
- Evidence: "No compile-time or runtime invariant enforces projection total == SQL total"; "marketplace order read model can linger after storefront order tombstone."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### HER-032: Order fulfillment governance gaps (from _scratch evidence report) unowned
- Severity: Medium
- Category: Risk
- Description: `_scratch/order-fulfillment-shipping.md` flags: (a) `add_fulfillment_ownership_snapshot.sql` trigger + CHECK constraint (enforcing `FulfillmentMode` immutability) are NOT wired into DbPatcher/CI — enforcement depends on an out-of-band script; EF model snapshot lacks the trigger/CHECK; (b) no customer/storefront order-cancellation code path exists (only merchant + automatic); (c) `OrderFulfillmentCancelledEvent` (OrderFulfillmentEvents.cs:217) appears unused; (d) naming mismatch `OrderDeliveredEventConsumer.cs` class is `FulfillmentDeliveredEventConsumer`.
- Recommendation: Wire the fulfillment-ownership SQL script into deployment/CI; verify trigger application in target envs; confirm intentional absence of customer-cancel path; remove or wire the unused event; fix the misleading filename.
- Complexity: M
- Dependencies: none
- Affected Modules: OrderFulfillmentService, DbPatcher, OrderShipmentOrchestrationConsumer
- Source Documents: _scratch/order-fulfillment-shipping.md §3 (Flag), §8 (flags)
- Evidence: "No grep hit wires `add_fulfillment_ownership_snapshot.sql` into DbPatcher/CI… EF model snapshot does not include the trigger or the CHECK constraint."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

### HER-033: Order payment/financial runtime gaps (from _scratch evidence report) unowned
- Severity: Medium
- Category: Risk
- Description: `_scratch/order-payment-financial.md` flags: (a) chargeback has NO storefront `Order` handling in `PaymentLifecycleService` (only CommerceHub runtimes) — a storefront chargeback transition could not be located; (b) two `RecordRefundAsync` APIs coexist (storefront vs CommerceHub overload) and must not be conflated; (c) no dedicated "overpayment" suspense bucket — amount mismatch (any direction) is the only suspense trigger. These are automation/testing blind spots.
- Recommendation: File as order-payment backlog; add a storefront chargeback path or explicitly document its non-existence; document the two `RecordRefundAsync` boundaries; consider a distinct overpayment suspense classification.
- Complexity: M
- Dependencies: none
- Affected Modules: PaymentLifecycleService, CommerceHubFinancialLedgerService
- Source Documents: _scratch/order-payment-financial.md §Missing evidence (1,2,3,5)
- Evidence: "no storefront `Order` chargeback transition method in this service"; "two `RecordRefundAsync` APIs coexist."
- Implementation Required: yes
- Architecture Only: no
- Documentation Only: no

---

## Summary of counts
- CONFLICTS: 8
- HER issues: 33 (Critical 0, High 6 [HER-002,004,005,008,011 + HER-? governance], Medium 18, Low 9; remainder Architectural/Enhancement)
- Excluded as resolved-by-remediation: F-07-01 (negative expense amount), F-11-04 (adjustment role auth), CV-01 (expense integrity scan), CV-03 (correction role auth), F-10-04 (negative ActualCash), F-07-02 (expense existence validation), F-07-03/M-17 (explicit tenant predicates — partial), F-08-06 (report date-range), F-10-02 (Z-report reversal — partial), C-01 & order-refund portion of C-02 (false positives). These are NOT re-listed as open.

## Note on resolved-vs-unresolved
Items HER-011 through HER-025 and HER-031 through HER-033 are the *still-open* portions of findings the remediation report marked "VERIFIED, DEFERRED" or "PARTIALLY IMPLEMENTED" — i.e., the code fix may exist for part of the issue but the structural/governance closure (tracking, ownership, standardization, signal exposure) remains. They are included because the task asks for unresolved issues, and each carries explicit "Remaining Work" with no completion.
