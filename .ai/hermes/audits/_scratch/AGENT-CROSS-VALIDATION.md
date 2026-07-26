# MASTER CROSS-VALIDATION REPORT — Shopixy ERP Accounting Architecture Audit

**Agent:** Master Cross-Validation Agent (independent consolidation of AGENT-06..11 + shared impl)
**Date:** 2026-07-20
**Inputs:** AGENT-06 (Payments), AGENT-07 (Expenses), AGENT-08 (Reports), AGENT-09 (SOA),
AGENT-10 (DailyClosing), AGENT-11 (Adjustments) + shared implementation:
`FinancialPostingService.cs`, `CommerceAccountingService.cs`, `AccountingOutboxProcessor.cs`,
`AccountLedger.cs`, `LedgerService.cs`, `FinancialIntegrityService.cs`.
**Methodology:** Implementation = source of truth. Every cross-flow finding re-verified against code I
read (file:line cited). Where I cite a subagent's finding, I independently confirmed the load-bearing
claim in the shared implementation.

---

## 0. EXECUTIVE SUMMARY

The six domain audits are individually sound and mostly faithful to their canonical docs. The
**cross-flow** picture, however, exposes a systemic weakness that no single auditor could see:
**the financial-integrity / reconciliation safety net is NOT uniform across flows, and the
sync-vs-async GL-posting split is inconsistent**, so party/GL truth can silently diverge in entire
flows with no detection — and at least one in-scope financial correction path can permanently
overstate the GL.

**Cross-flow finding counts by severity**

| Severity | Count | IDs |
|---|---|---|
| CRITICAL | 1 | CV-02 |
| HIGH | 3 | CV-01, CV-03, CV-06 |
| MEDIUM | 6 | CV-04, CV-05, CV-07, CV-08, CV-09, CV-11 |
| LOW | 2 | CV-10, CV-12 |
| **TOTAL** | **12** | |

Plus the 6 domain audits' own 34 findings are acknowledged but not re-counted here (this report adds
the *cross-cutting* layer on top of them).

---

## 1. CROSS-FLOW FINANCIAL PIPELINE CONSISTENCY ANALYSIS

Intended pipeline (per methodology): **Payments → Statement(Of Account) → Reports → Daily Closing →
Adjustments → General Ledger**, all converging on `FinancialPostingService` (GL journal owner) and
`AccountLedger` (party balance source of truth), bridged by the transactional `AccountingOutbox`.

### 1.1 How each flow commits financial truth (verified)

| Flow | Party balance (`AccountLedger`) | GL journal | Posting path | Reconciliation check? |
|---|---|---|---|---|
| POS Sale (credit) | SYNC (`SalesController` writes Sale+PaymentReceived) | SYNC (`PostPosSaleAsync`, also outbox idempotent re-run) | `SalesController`→`CommerceAccountingService.PostPosSaleAsync` | `ValidatePosSaleCustomerAccountingCoverageAsync` ✅ |
| POS Sale (cash/card) | SYNC (informational, net-zero entries) | SYNC (no AR line; Dr Cash/Cr Sales) | same | same ✅ |
| Expense | none (no party ledger) | **ASYNC** (outbox → `ProcessPosExpenseAsync`) | `ExpensesController`→outbox→`FinancialPostingService` | ❌ **NONE** |
| Adjustment | SYNC (`LedgerService`) | **ASYNC** (outbox → `PostPartyLedgerAdjustmentAsync`) | `StatementOfAccountController`→outbox→`FinancialPostingService` | `ValidateAdjustmentLinksAsync` ✅ |
| Supplier payment | SYNC (party ledger) | **ASYNC** (outbox → `PostSupplierPaymentAsync`) | `SuppliersController`→outbox→`FinancialPostingService` | `ValidateSupplierPaymentLinksAsync` ✅ |
| Online/Order payment | n/a (order status) | **ASYNC** (outbox) | `PaymentLifecycleService`→outbox→`CommerceAccountingService` | ❌ **NONE** |
| Purchase return | SYNC (party ledger) | ASYNC (outbox) | outbox→`FinancialPostingService` | `ValidatePurchaseReturnLinksAsync` ✅ |
| Daily Closing | none (state flag only) | none (read-only) | `CashboxController.CloseCashbox` (no journal) | n/a |

### 1.2 Key pipeline conclusions

- **The pipeline does NOT treat financial truth uniformly.** POS sale commits both tiers atomically
  (zero divergence window); expense/adjustment/supplier-payment/order-payment commit the GL
  asynchronously, creating a divergence window between the sync event and the async GL post.
- **The divergence window is only *monitored* for three of the five async flows** (adjustment,
  supplier payment, purchase return). **Expenses and online/order payments have NO integrity check at
  all** (see CV-01, CV-02). So the methodology's implicit assumption "the outbox + integrity service
  cover all flows" is FALSE.
- **Cancellation/refund reversal is absent** in the order-payment runtime (F-06-02) AND there is no
  reversal method for in-scope ERP party payments (`CommerceAccountingService` has `PostSupplierPaymentAsync`
  at `:369` but no `ReverseSupplierPaymentAsync`/`CancelSupplierPaymentAsync`/`RefundCustomerPaymentAsync`),
  so a cancelled supplier payment would also leave the GL control accounts un-reversed.
- **Two-tier party/GL decoupling** (POS cash/card customer `AccountLedger` entries are informational
  and NOT posted to GL AR — F-06-05) is internally consistent *per transaction type* but the
  reconciliation boundary is never declared in one place, and the SOA supplier-aux aggregates use a
  *third* store (`SupplierTransactions` mirror) instead of `AccountLedger` (F-09-03).

---

## 2. CROSS-FLOW FINDINGS (full schema)

### CV-01 — Expense GL posting has NO reconciliation/integrity safety net (systemic blind spot)
- **Severity:** HIGH
- **Domains touched:** Expenses (07), Shared/Integrity (cross-cutting). Provenance: **cross-boundary
  financial-integrity risk** the canonical docs scope out but the audit must surface.
- **Evidence:** `FinancialIntegrityService.ValidateAsync` (`:191-296`) runs 14 checks:
  Stored-balances-vs-ledger, Supplier-opening-balance uniqueness/fields, Party-transactions-vs-ledger,
  Ledger-entries-vs-party-transactions, POS-sale coverage, **Supplier-payment coverage**, **Adjustment
  coverage**, Purchase-return coverage, Duplicate journals, Duplicate outbox, Legacy refund outbox,
  Shared cash migration, Mutation idempotency — **NONE for Expenses**. `rg -c "Expense"`
  `FinancialIntegrityService.cs` = **0**. Contrast: adjustments are protected by
  `ValidateAdjustmentLinksAsync` (`:1003-1076`).
- **Implementation proof:** Expenses post asynchronously via the outbox (`EnqueuePosExpenseAsync`,
  `AccountingOutboxService.cs:478` → `ProcessPosExpenseAsync`, `CommerceAccountingService.cs:772` →
  `FinancialPostingService.PostJournalAsync`). If the outbox worker loses/drops an expense message,
  the operational `Expenses` table shows the expense (and `GetReport` totals it — F-07-05) but **no GL
  P&L entry ever lands, and no integrity check flags the orphan**. The expense is silently missing
  from the financial statements.
- **Business impact:** Unbooked expenses → overstated profit; undetectable without manual GL-vs-Expenses
  reconciliation.
- **Financial/Accounting impact:** GL can diverge from operational reality for an entire flow with zero
  alarm. Direct violation of "financial truth must never be duplicated / must remain auditable."
- **Root cause:** `FinancialIntegrityService` was built per-flow (payments, adjustments, returns) and
  Expenses were never added.
- **Recommended solution:** Add `ValidateExpenseLinksAsync` (mirror `ValidateAdjustmentLinksAsync`):
  for every approved, non-deleted `Expense`, assert a matching `AccountingOutboxMessage`
  (`EXPENSE:{tenant}:{id}:*`) and a posted `JournalEntry` (`SourceType=PosExpense`). Wire into
  `ValidateAsync`.
- **Priority:** P0. **Confidence:** High (verified by code read + grep).
- **Validation matrix:** DoubleEntry=partial, SourceOfTruth=violated, FinancialIntegrity=violated,
  Auditability=partial, TenantIsolation=ok, Idempotency=ok, EventualConsistency=risk.

### CV-02 — Cancelled/refunded payment posts no reversal journal; same reversal plumbing is ABSENT for in-scope ERP party payments
- **Severity:** CRITICAL
- **Domains touched:** Payments/Order (06, out-of-scope runtime) + Supplier Payments (in-scope,
  inferred from CommerceAccountingService). Provenance: **out-of-canonical-scope runtime behavior that
  is a genuine financial-integrity risk the audit MUST surface**, plus an in-scope capability gap.
- **Evidence:** F-06-02 confirms `PaymentLifecycleService.HandleFailureAsync` (`:2212`) posts no
  reversal for a collected-then-cancelled order. Independent check of in-scope ERP reversal capability:
  `CommerceAccountingService.cs` defines `PostSupplierPaymentAsync` (`:369`) but **no**
  `ReverseSupplierPaymentAsync` / `CancelSupplierPaymentAsync` / `RefundCustomerPaymentAsync`
  (grep across repo). `SuppliersController.cs` has payment-create endpoints (`:124,156,343,409,597,639`)
  but **no cancel/refund/reverse** endpoint. `FinancialPostingService.ReverseJournalAsync` (`:256`)
  exists generically but is wired only to certain flows, not party payments/adjustments.
- **Implementation proof:** A supplier payment posts `Dr Cash/Bank, Cr AccountsPayable(2010)` via
  `PostSupplierPaymentAsync`. If that payment is later cancelled/refunded, there is **no code path that
  posts the offsetting `Dr AP(2010), Cr Cash/Bank` reversal** — because the reversal method and the
  cancel endpoint do not exist. So the GL permanently shows the AP reduction even after the payment is
  unwound. For order payments (out-of-scope) the same gap is real and confirmed (F-06-02).
- **Assessment of scope question (c):** F-06-02's *order-cancellation* instance is outside
  FLOW-ACC-06's documented scope, but it is squarely within the **audit's financial-integrity
  mandate** (methodology: "Can GL diverge from reality?" / "source-of-truth violation = CRITICAL"). The
  *root cause* — absence of a reversal journal for any payment that is later unwound — is **not**
  limited to the out-of-scope runtime; it also affects in-scope ERP party payments. So the gap is both
  a cross-boundary risk AND an in-scope latent defect.
- **Business/Financial impact:** Overstated revenue (order) / understated payable (supplier); GL never
  equals economic reality after a cancellation.
- **Root cause:** Reversal journal was only ever built for the adjustment/return paths; payment-cancel
  was assumed to occur pre-collection (reservation release sufficient).
- **Recommended solution:** Implement `ReverseSupplierPaymentAsync` + `RefundCustomerPaymentAsync`
  (and wire order-cancel to `ReverseJournalAsync` per F-06-02). Adopt a single
  `IPaymentReversalService` so every payment type has a symmetric reverse path.
- **Priority:** P0. **Confidence:** High (order instance confirmed by F-06-02; in-scope absence
  confirmed by grep of CommerceAccountingService + SuppliersController).
- **Validation matrix:** DoubleEntry=broken, SourceOfTruth=violated, FinancialIntegrity=violated,
  Auditability=partial, CQRS=ok, EventualConsistency=risk.

### CV-03 — Manual financial corrections lack role-based authorization across domains (systemic SoD gap)
- **Severity:** HIGH
- **Domains touched:** Expenses (07), Adjustments (11). Provenance: **in-scope** (both domains are
  canonical FLOW-ACC-07 / FLOW-ACC-11).
- **Evidence:** `ExpensesController.cs:23` → only `[Authorize]` (class-level); no `[Authorize(Roles=…)]`
  on `CreateExpense`/`ApproveExpense`/`UpdateExpense`/`DeleteExpense`. `StatementOfAccountController.cs:24`
  → only `[Authorize]`; `CreateAdjustment` (`:534`) has no role check (F-11-04). **Contrast:** `SuppliersController.cs`
  enforces `Owner,Admin,Manager` on every payment endpoint (`:124,156,343,409,597,639`); `CashboxController`
  enforces `Owner,Admin` on open/close (`:336,678,1211`).
- **Implementation proof:** Any authenticated user (e.g. a cashier) within a tenant can (a) create an
  approved expense and (b) post an immediate party-balance adjustment — both are manual financial
  corrections with no segregation-of-duties control. Only the *payment* and *cashbox* write paths gate
  by role.
- **Business/Financial impact:** Fraud / error exposure; manual corrections become GL truth with no
  four-eyes control. Audit `LogCreate` records the actor but cannot prevent the action.
- **Root cause:** Adjustments/expenses modeled as self-service; role gating applied inconsistently per
  controller (payment/cashbox got it, corrections did not).
- **Recommended solution:** Add a uniform `RequireRole("Accountant","Admin")` (or `AdjustmentPolicy`)
  to `CreateAdjustment`, `ApproveExpense`, and any manual-correction endpoint; centralize via a base
  attribute/filter so corrections are never role-less.
- **Priority:** P1. **Confidence:** High (verified by reading controller attributes).
- **Validation matrix:** Authorization=violated, Auditability=partial, FinancialIntegrity=risk,
  TenantIsolation=ok.

### CV-04 — Systemic missing negative-amount validation across write paths
- **Severity:** HIGH (systemic pattern)
- **Domains touched:** Expenses (07), Adjustments (11), DailyClosing (10). Provenance: **in-scope**,
  cross-domain pattern none of the three auditors elevated to a systemic finding.
- **Evidence:** Expense `Amount` accepts ≤0 (F-07-01, `CreateExpenseDto.cs:52,68` no range). Party
  running balance has no floor (F-11-05, `LedgerService.cs:218-239` no `PreventNegative` check on
  `Customer.Balance`/`Supplier.Balance`). `CloseCashboxRequest.ActualCash` accepts negatives
  server-side (F-10-04, `CashboxController.cs:516`). Only GL accounts flagged `PreventNegative` are
  guarded (`FinancialPostingService.cs:566-605`, line `584 if (!account.PreventNegative) continue;`).
- **Implementation proof:** Three independent write paths each accept a value with the wrong sign and
  post it (expenses invert the P&L sign; party balances can go unexpectedly negative; a negative
  ActualCash corrupts the closure "difference"). The GL-level `PreventNegative` guard does NOT protect
  party balances or expense amounts.
- **Business/Financial impact:** Wrong-sign GL entries; silent over-correction of party balances;
  corrupt closure data.
- **Recommended solution:** Centralize a signed-amount + party-floor validation policy
  (`IAccountingValidationPolicy`) invoked by all financial write paths; do not rely on per-DTO
  ad-hoc guards.
- **Priority:** P1. **Confidence:** High.
- **Validation matrix:** Validation=violated, DoubleEntry=partial, FinancialIntegrity=risk.

### CV-05 — Tenant-isolation read/write asymmetry is a system-wide smell, concentrated in Expenses
- **Severity:** MEDIUM
- **Domains touched:** Expenses (07), and architecturally all read surfaces. Provenance: **in-scope**
  (F-07-03) raised it for Expenses; cross-validation shows it is the *only* domain with the asymmetry
  but the pattern (reads trust the global filter, writes add defense-in-depth) is systemic.
- **Evidence:** F-07-03: `ExpensesController.GetExpenses`/`GetReport` (`:76`, `:908`) rely solely on the
  EF global `HasQueryFilter` for `ITenantOwned`; writes re-apply `TenantId==RequiredTenantId` after
  `IgnoreQueryFilters` (`:291,363,429`). By contrast Reports (F-08-03) and DailyClosing (F-10) inject
  `RequiredTenantId` explicitly into every read query. `FinancialIntegrityService` reconciliation reads
  (`ReconcileHistoricalBalancesAsync`, `:47-137`) also rely only on the global filter.
- **Implementation proof:** If the global query filter is ever disabled/regressed, expense *reads* and
  the *reconciliation service itself* would cross tenants, while writes stay protected. The asymmetry
  is a latent cross-tenant read-leak concentrated in Expenses but the same implicit-trust pattern
  exists in the integrity service.
- **Recommended solution:** Add explicit `TenantId == RequiredTenantId` predicates to all expense read
  queries (defense-in-depth), and have `FinancialIntegrityService` read via a tenant-explicit path.
- **Priority:** P2. **Confidence:** High.
- **Validation matrix:** TenantIsolation=partial (reads), SourceOfTruth=ok.

### CV-06 — Inconsistent sync/async GL-posting model creates uneven divergence exposure (architectural)
- **Severity:** HIGH
- **Domains touched:** ALL (06/07/09/10/11 + shared). Provenance: **cross-boundary architectural
  inconsistency**.
- **Evidence/Implementation proof:** (see §1.1 table) POS sale commits party+GL synchronously (no
  window). Expense, Adjustment, Supplier payment, Online/Order payment commit GL asynchronously. The
  *monitoring* of that window is itself inconsistent: adjustments/supplier-payment/purchase-return have
  integrity checks; **expenses and online/order payments do not** (CV-01, CV-02). Therefore the
  "eventual convergence" guarantee is NOT equally strong across flows — it degrades silently for
  expenses and order payments.
- **Business/Financial impact:** Reconciliation confidence is flow-dependent; operators cannot assume
  "outbox will fix it" uniformly.
- **Recommended solution:** Declare one canonical posting model (sync-commit-both OR async-with-uniform
  integrity-check) and apply it to every flow; make the integrity check mandatory for any flow that
  async-posts to GL.
- **Priority:** P1. **Confidence:** High.
- **Validation matrix:** EventualConsistency=inconsistent, FinancialIntegrity=risk, CQRS=ok.

### CV-07 — Two-tier party/GL decoupling is internally consistent per-type but the reconciliation boundary is undocumented and inconsistently covered
- **Severity:** MEDIUM
- **Domains touched:** Payments (06), SOA (09), Adjustments (11). Provenance: **in-scope** (theme b).
- **Evidence:** F-06-05: POS cash/card customer `AccountLedger` entries are informational and NOT posted
  to GL AR(1410); only credit sales post `Dr AR(1410), Cr Sales`. SOA = pure `AccountLedger` projection
  (F-09). Adjustments post to dedicated `6120/6130` against `1410/2010` (`CommerceAccountingService.cs:509-536`).
  The three write paths therefore land on *three different GL accounts* for "party" activity
  (AR 1410 for credit sales, none for cash/card, 6120/6130 for adjustments) and the party sub-ledger
  only fully reconciles to GL for credit sales.
- **Implementation proof:** The reconciliation boundary (which transaction types post to AR vs not) is
  real and by design, but **no single doc/code artifact declares it**, and `ReconcileHistoricalBalancesAsync`
  checks `Customer.Balance`/`Supplier.Balance` cache vs `AccountLedger` but does NOT compare either to
  the GL control accounts. A future automation assuming `AR(1410) == Σ party AccountLedger` will raise
  false "divergence" findings (exactly what F-06-05 warns about).
- **Recommended solution:** Document the per-type reconciliation boundary in one place (canonical doc
  + a code comment in `CommerceAccountingService`); optionally add a reconciliation check that
  explicitly *excludes* cash/card POS from the AR-vs-subledger assertion.
- **Priority:** P2. **Confidence:** High.
- **Validation matrix:** SourceOfTruth=ok(by design), CQRS=ok, Accounting=partial(docs).

### CV-08 — "Party balance truth" has three representations → ownership/source-of-truth conflict
- **Severity:** MEDIUM
- **Domains touched:** SOA (09), Adjustments (11), Supplier Payments. Provenance: **in-scope**,
  ownership/source-of-truth conflict spanning domains.
- **Evidence:** (1) `AccountLedger.RunningBalance` = authority (F-09). (2) `Customer.Balance` /
  `Supplier.Balance` cached columns, maintained by `LedgerService.SetTrackedPartyBalance`, reconciled by
  `FinancialIntegrityService.ReconcileHistoricalBalancesAsync` (`:31-189`). (3) `SupplierTransactions`
  mirror — used for supplier *outstanding aux* aggregates (`TotalPaid`/`TotalInvoiced`/`DueDate`) in
  `GetOutstandingBalances` (F-09-03, `StatementOfAccountController.cs:1077-1109`), unlike the customer
  branch which reads `AccountLedgers`. Additionally F-09-01: the customer adjustment `BalanceAfter` is
  sourced from the cached column while the supplier branch sources from `RunningBalance`.
- **Implementation proof:** The same logical quantity (party balance / its aux aggregates) is sourced
  from three different stores depending on party type and code path. Today all three are maintained
  transactionally so they agree, but **the architecture does not guarantee it**, and `RebuildPartyBalanceAsync`
  repairs (1)+(2) but not necessarily (3)'s `BalanceAfter` columns.
- **Business/Financial impact:** Supplier outstanding screen can disagree with the supplier SOA if the
  mirror ever diverges; ownership of "authoritative aux aggregate" is undefined.
- **Recommended solution:** Make `AccountLedger` the single source for ALL party aggregates (customer
  and supplier); deprecate `SupplierTransactions.BalanceAfter` as a source for outstanding aux; unify the
  `BalanceAfter` capture (use `ApplyCustomerTransaction`/`ApplySupplierTransaction` return value
  everywhere — F-09-01 fix).
- **Priority:** P2. **Confidence:** High.
- **Validation matrix:** SourceOfTruth=conflict, CQRS=violated, DDD=leak, AggregateBoundary=leak.

### CV-09 — Inconsistent treatment of "Reversed"/"not-yet-posted" across the three reporting surfaces
- **Severity:** MEDIUM
- **Domains touched:** Reports (08), DailyClosing (10), Expenses (07). Provenance: **in-scope**,
  reporting consistency spanning flows.
- **Evidence:** Financial Reports net `Reversed` journals correctly (F-08-14, `FinancialReportService.cs:59-62`
  etc.). Daily Closing Z-Report **double-counts** `Reversed` at face value in `CashSales`/breakdown
  subtotals (F-10-02, `ReportsController.cs:372-373,417-419`). Expense operational report shows
  approved-but-not-yet-GL-posted expenses (F-07-05). So three reporting surfaces apply *three different*
  consistency semantics to the same underlying reversal/lag state.
- **Implementation proof:** A sale reversed after close still inflates the Z-Report `CashSales` line
  (though `ExpectedClosingCash` stays correct, F-10-02); a P&L nets it; an Expense report may show it
  pre-posting. A user comparing Z-Report vs P&L vs Expense report sees unexplained gaps with no single
  rule.
- **Recommended solution:** Standardize reversal handling: either exclude `Reversed` from all breakdown
  subtotals (rely on the reversing entry to net) or subtract it uniformly; document the chosen
  convention once and apply to Z-Report + Expense report.
- **Priority:** P2. **Confidence:** Medium (Z-Report reversal shape strongly indicated; cross-surface
  inconsistency confirmed).
- **Validation matrix:** CQRS=inconsistent, SourceOfTruth=ok, EventualConsistency=risk.

### CV-10 — No shared "outbox drained / GL converged" signal for operators
- **Severity:** LOW (ARCHITECTURAL IMPROVEMENT)
- **Domains touched:** ALL async flows. Provenance: cross-boundary.
- **Evidence:** Each domain independently documents "eventual consistency" (F-07-05, F-11-03, etc.) but
  no endpoint surfaces whether the outbox has drained for a tenant/entity, and no per-tenant lag metric
  exists. Reads cannot distinguish "outbox lag" from "real divergence."
- **Recommended solution:** Expose an outbox-lag / last-posted indicator (e.g.
  `GET /ledger/consistency/{tenant}`) consumed by reports and the reconciliation UI.
- **Priority:** P3. **Confidence:** High.

### CV-11 — Accounting-period / locked-period guard is owned by no flow (inconsistent control)
- **Severity:** MEDIUM
- **Domains touched:** Adjustments (11), Expenses (07), Payments, Reports (08). Provenance: **in-scope**,
  ownership conflict.
- **Evidence:** Adjustments enforce NO period lock (F-11-08, `CreateAdjustment` has no
  `IsPeriodLocked`/`IsClosed` check). Expenses audit found no period-lock reference. Reports explicitly
  state period-lock belongs to the posting engine and is NOT IMPLEMENTED (F-08-07). DailyClosing is
  read-only. So "who owns the accounting period lock?" is **undefined** — if one flow later enforces it,
  the others will not, reopening closed books inconsistently.
- **Implementation proof:** No shared `IAccountingPeriodLock` service is invoked by any financial write
  path; each domain treats period-lock as someone else's concern.
- **Recommended solution:** Introduce a single `IAccountingPeriodService.IsClosed(tenant, date)` invoked
  by ALL financial write paths (expenses, adjustments, payments, POS sale) before commit.
- **Priority:** P2. **Confidence:** High (negative: absence confirmed across domains).
- **Validation matrix:** SourceOfTruth=ok, FinancialIntegrity=risk, CQRS=ok, AggregateBoundary=ok.

### CV-12 — Durable webhook/audit idempotency asymmetry mirrors the read/write protection asymmetry pattern
- **Severity:** LOW
- **Domains touched:** Payments (06, out-of-scope runtime), Expenses (07). Provenance:
  cross-boundary (order-payment runtime) + architectural pattern.
- **Evidence:** F-06-03: only Fawry persists a `PaymentWebhookLog`; Stripe/Paymob rely on downstream
  state. This is the same shape as F-07-03 (reads trust implicit filter) and CV-05: **one side gets
  explicit durable protection, the other gets none.** Systemic pattern of asymmetric guarantees.
- **Recommended solution:** Persist a `PaymentWebhookLog` (unique `ProviderEventId` per tenant) at the
  entry of all webhook controllers.
- **Priority:** P2. **Confidence:** Medium.

---

## 3. DUPLICATION / OWNERSHIP / SOURCE-OF-TRUTH CONFLICT MATRIX

| Concern | Representations / owners | Conflict? | Finding |
|---|---|---|---|
| Party running balance | `AccountLedger.RunningBalance` (authority) vs `Customer.Balance`/`Supplier.Balance` (cache) vs `SupplierTransactions.BalanceAfter` (mirror) | YES — 3 stores; supplier aux uses mirror not ledger | CV-08, F-09-01, F-09-03 |
| GL reconciliation coverage | `FinancialIntegrityService` checks: payments✅, adjustments✅, returns✅, POS✅, **expenses❌, order-payments❌** | YES — uneven | CV-01, CV-06 |
| Reversal journal on cancel/refund | `ReverseJournalAsync` exists; wired for returns/adjustments? NO for payments (order + supplier) | YES — missing for payments | CV-02, F-06-02 |
| Accounting-period lock | No owner; each domain assumes another enforces it | YES — undefined ownership | CV-11 |
| Negative-amount policy | GL `PreventNegative` flag guards GL only; party balances + expense amounts + ActualCash unguarded | YES — inconsistent | CV-04 |
| Role authorization on corrections | Payments/Cashbox enforce roles; Expenses/Adjustments do not | YES — inconsistent SoD | CV-03 |
| "Reversed" semantics in reads | Reports net; Z-Report double-counts; Expense report shows pre-post | YES — 3 conventions | CV-09 |
| Tenant isolation on reads | Writes explicit; Expenses reads + integrity service implicit (global filter) | YES — asymmetry | CV-05, F-07-03 |
| Posting model (sync vs async) | POS sale sync; others async; monitoring uneven | YES — inconsistent | CV-06 |

---

## 4. ANSWERS TO THE SIX KEY CROSS-CUTTING THEMES

**(a) SYNC/ASYNC FINANCIAL TRUTH DIVERGENCE.**
Confirmed systemic. POS sale commits both tiers synchronously (safe). Expense / Adjustment / Supplier
payment / Online-payment commit GL asynchronously. The reconciliation safety net
(`FinancialIntegrityService.ValidateAsync`) covers supplier payments, adjustments, POS sales and
purchase returns — but **NOT expenses and NOT online/order payments** (CV-01, CV-02). So the "GL vs
party/operational truth" can diverge with **no detection** for an entire revenue/expense-affecting
flow. `FinancialIntegrityService` does NOT cover expenses equally (0 "Expense" references).

**(b) SCOPE/SOURCE-OF-TRUTH ON PARTY BALANCES.**
Confirmed internally consistent per transaction type: POS cash/card `AccountLedger` entries are
informational and not posted to GL AR (F-06-05); credit sales post to AR(1410); adjustments post to
6120/6130. SOA is a pure `AccountLedger` projection (F-09). The two-tier model is sound **but the
reconciliation boundary is never declared in one place and is not uniformly checked** (CV-07). The
party-balance truth itself has three representations with inconsistent ownership (CV-08).

**(c) F-06-02 SCOPE ASSESSMENT.**
F-06-02 (order cancel posts no reversal) is outside FLOW-ACC-06's documented scope, but it is a genuine
financial-integrity risk the audit must surface (GL overstates revenue). More importantly, the *root
cause* — absence of any payment-reversal journal — also affects **in-scope ERP party payments**:
`CommerceAccountingService` has `PostSupplierPaymentAsync` but no `ReverseSupplierPaymentAsync`/
`CancelSupplierPaymentAsync`/`RefundCustomerPaymentAsync`, and `SuppliersController` has no
cancel/refund endpoint. So the same reversal gap exists in-scope as a latent defect. **Verdict: surface
it; it is both a cross-boundary risk and an in-scope capability gap (CV-02).**

**(d) NEGATIVE-BALANCE POLICY.**
Confirmed systemic missing-validation pattern: expense amount (F-07-01), party running balance floor
(F-11-05), and `ActualCash` (F-10-04) all accept wrong-sign values; only GL `PreventNegative` accounts
are guarded. Three independent write paths, one systemic gap (CV-04).

**(e) TENANT ISOLATION.**
Confirmed: Expenses reads rely only on the EF global filter while writes add explicit predicates
(F-07-03). Other domains (Reports F-08-03, DailyClosing F-10) inject `RequiredTenantId` explicitly.
The asymmetry is concentrated in Expenses but the implicit-trust pattern also appears in
`FinancialIntegrityService` reconciliation reads (CV-05). No other domain exhibits the read/write
asymmetry today, but the pattern is a system smell.

**(f) AUTHORIZATION.**
Confirmed systemic missing-authorization on manual corrections: Adjustments (`[Authorize]` only,
F-11-04) and Expenses (`[Authorize]` only, `ExpensesController.cs:23`, including `ApproveExpense`)
lack role checks, while Supplier payments and Cashbox enforce `Owner,Admin[,Manager]`. So manual
financial corrections (expenses approve + adjustments) are the weak link in SoD (CV-03).

---

## 5. TOP SYSTEMIC ARCHITECTURAL RISK (single most important)

**The financial-integrity / reconciliation safety net is not uniform across flows, and the
sync-vs-async GL-posting split is inconsistent — so GL can silently diverge from economic reality for
entire flows (Expenses, Online/Order payments) with no detection, while at the same time cancelled/
refunded payments post no reversal journal (CV-02) so the GL can also permanently overstate
revenue/payables. The net effect: the "General Ledger is the single source of truth" principle is
enforced for some flows and silently violated for others, with no cross-flow alarm.**

---

## 6. CONSOLIDATED REMEDIATION PRIORITY (cross-flow)

- **P0:** CV-02 (payment reversal journal, in-scope + out-of-scope), CV-01 (add expense integrity check).
- **P1:** CV-03 (role auth on corrections), CV-04 (centralized signed-amount/party-floor validation),
  CV-06 (uniform posting+monitoring model).
- **P2:** CV-05, CV-07, CV-08, CV-09, CV-11, CV-12.
- **P3:** CV-10.

---

*All cross-flow findings re-verified against implementation (file:line cited). Subagent findings F-06-02,
F-07-01/03/05, F-09-01/03, F-10-02/04, F-11-03/04/05/08 were independently confirmed against the shared
files `FinancialIntegrityService.cs`, `CommerceAccountingService.cs`, `AccountLedger.cs`,
`LedgerService.cs`, and the relevant controllers.*
