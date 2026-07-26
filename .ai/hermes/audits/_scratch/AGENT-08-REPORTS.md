# FLOW-ACC-08 Financial Reports — Architecture Audit Findings (Agent 08)

**Auditor:** FINANCIAL REPORTS Financial Architecture Auditor (independent subagent)
**Date:** 2026-07-20
**Canonical doc:** `PointOfSale/.ai/hermes/accounts/FLOW-ACC-08-FINANCIAL-REPORTS.md`
**Methodology:** `PointOfSale/.ai/hermes/audits/_scratch/AUDIT-METHODOLOGY.md`
**Principle applied:** Implementation is the source of truth. Every finding backed by real code:line.

---

## 0. Scope & Files Reviewed

Implementation read (full):
- `PointOfSale.API/Controllers/FinancialReportsController.cs` (302 lines)
- `PointOfSale.Infrastructure/Accounting/FinancialReportService.cs` (723 lines)
- `PointOfSale.Core/Interfaces/Accounting/IFinancialReportService.cs`
- `PointOfSale.Core/Interfaces/Accounting/ILedgerOnlyService.cs`
- `PointOfSale.Core/Guards/ForbiddenBusinessDbSets.cs`
- `PointOfSale.Core/Models/Accounting/Reports/` → `TrialBalanceReport.cs`, `ProfitAndLossReport.cs`, `BalanceSheetReport.cs`, `CashFlowReport.cs`, `GeneralLedgerReport.cs`, `ClearingAgingReport.cs`
- `PointOfSale.Tests/Architecture/PosFinancialReportingAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/LedgerIsolationTests.cs`
- `PointOfSale.Tests/FinancialReportsControllerTenantScopeTests.cs`
- `PointOfSale.Core/Entities/Accounting/JournalEntry.cs`, `LedgerEntry.cs`, `Account.cs`

Shared infra referenced (read for tenant/immutability checks): `JournalEntry`, `LedgerEntry`, `Account` (AccountLedger.cs does **not** exist; AccountLedger party balances belong to a different FLOW — see §Deep-Investigation Q below).

---

## 1. Headline Verdict

**The implementation is a faithful, correct read-only projection of the immutable General Ledger and matches the canonical doc closely.** Reports never mutate state, always read the ledger (`LedgerEntries` + joined `Account`/`JournalEntry`), enforce tenant isolation at the query level, and are cleanly separated as the read side (CQRS). CI guards (`ILedgerOnlyService` + `ForbiddenBusinessDbSets` + `LedgerIsolationTests`) make ledger-only sourcing a build-blocker. No CRITICAL or HIGH findings. Remaining findings are MEDIUM/LOW architectural gaps, all pre-acknowledged in the canonical doc as "NOT IMPLEMENTED".

`from<=to` order validation, period/closed-period lock, multi-currency, retained-earnings roll-forward, and Cash-Flow Investing/Financing sections are absent (documented as NOT IMPLEMENTED — no defect, only gap).

---

## 2. Findings

### F-08-01 — Reports are strictly read-only (no write side-effects) ✅ CONFIRMED (ARCHITECTURAL STRENGTH)
- Severity: ARCHITECTURAL IMPROVEMENT (positive finding)
- Evidence: No `SaveChangesAsync`, `SaveChanges()`, `_db.Add`, `_db.Update`, `_db.Remove`, `Entry(...)`, `ExecuteUpdate`, or `ExecuteDelete` anywhere in `FinancialReportService.cs` or `FinancialReportsController.cs`. Every query uses `.AsNoTracking()`.
- Code references: `FinancialReportService.cs:56-58` (`.AsNoTracking()`), `:148-149`, `:217-218`, `:298-299`, `:477-478`, `:565-566`, `:616-617`, `:665-666`. Controller: `FinancialReportsController.cs:64-302` (all GET, only `_reportService` reads).
- Documentation references: FLOW-ACC-08 §0.A, §1 ("no report table, no report store"), §8, §17.
- Implementation proof: All 8 handler methods call `_reportService.*Async(...)` and return `Ok(payload)`; controller holds `IMemoryCache _cache` but never writes to it (`GetOrCreateCachedAsync` at `:279-287` is a no-op wrapper that only calls `factory`).
- Business impact / Financial impact / Accounting impact: Reports cannot corrupt financial data. Reports are pure projections.
- Architecture impact: Clean CQRS read side.
- Risk: None.
- Root cause: N/A (by design).
- Recommended solution: None. Keep as-is.
- Migration impact: None. Regression risk: None.
- Priority: P3. Confidence: High.

Validation matrix: Double Entry=N/A, CQRS=YES, DDD=YES, ERP=YES, Accounting=YES, Auditability=YES, Source Of Truth=YES, Tenant Isolation=YES, Idempotency=N/A, Financial Integrity=YES, Eventual Consistency=NO (read-live), Aggregate Boundary=YES.

---

### F-08-02 — Reports read the General Ledger (single source of truth); never use denormalized `Account.CurrentBalance` for statements ✅ CONFIRMED (ARCHITECTURAL STRENGTH)
- Severity: ARCHITECTURAL IMPROVEMENT (positive finding)
- Evidence: Trial Balance, P&L, Balance Sheet, Cash Flow, Clearing Aging all aggregate `LedgerEntries` filtered by `l.JournalEntry.Status ∈ {Posted, Reversed}`. Balance Sheet explicitly aggregates `LedgerEntries`, NOT `Account.CurrentBalance` (`FinancialReportService.cs:297-321`). `AccountSummary`/`GetAccountSummariesAsync` ALSO derive balance from ledger aggregation (`GetAccountBalancesAsOfAsync`), not from `Account.CurrentBalance` (`FinancialReportService.cs:562, 587`).
- Code references: `FinancialReportService.cs:56-80` (TB), `:216-242` (P&L), `:297-321` (BS), `:660-699` (`GetAccountBalancesAsOfAsync`), `:562-587` (Account Summary).
- Documentation references: FLOW-ACC-08 §1, §8, §14, §16.
- Implementation proof: All report queries `.Where(l => l.TenantId == tenantId && (l.JournalEntry!.Status == Posted || Status == Reversed) ...)`. No reference to `Account.CurrentBalance` in any statement computation.
- Financial impact: No financial-truth duplication; statements cannot disagree with the ledger (modulo the open-period caveat below).
- Architecture impact: Recompute-on-read projection strategy; no stored read model, no snapshot.
- Risk: None.
- Priority: P3. Confidence: High.

---

### F-08-03 — Tenant isolation enforced at query level (RequiredTenantId, never a client param) ✅ CONFIRMED
- Severity: ARCHITECTURAL IMPROVEMENT (positive finding)
- Evidence: Every report query filters `l.TenantId == tenantId` (or `a.TenantId == tenantId`). Controller injects `ITenantContext.RequiredTenantId` (`FinancialReportsController.cs:40`) and passes it to the service; tenant is never accepted as a request parameter.
- Code references: `FinancialReportService.cs:58, 139, 151, 218, 299, 478, 567, 609, 619, 667` (all carry `tenantId`/`TenantId` filter). Controller `:40, 71-75, 95-100, 138-142, 159-161, 179-182, 199-201, 215-217, 235`.
- Documentation references: FLOW-ACC-08 §3 (Tenant), §9.1, §14, §17.
- Implementation proof: `private Guid TenantId => _tenantContext.RequiredTenantId;` — `RequiredTenantId` throws if unset, so a null tenant can never reach a query. Cross-tenant leak is impossible at the SQL predicate level.
- Tests: `FinancialReportsControllerTenantScopeTests.cs:14-41, 43-88` verify the controller passes `RequiredTenantId` into the service for TB and Dashboard.
- Priority: P3. Confidence: High.

---

### F-08-04 — Ledger-only sourcing enforced by CI (ILedgerOnlyService + ForbiddenBusinessDbSets + LedgerIsolationTests) ✅ CONFIRMED
- Severity: ARCHITECTURAL IMPROVEMENT (positive finding)
- Evidence: `FinancialReportService : IFinancialReportService, ILedgerOnlyService` (`FinancialReportService.cs:24`). `ForbiddenBusinessDbSets.Names` lists 23 forbidden business DbSets (`ForbiddenBusinessDbSets.cs:17-56`). `LedgerIsolationTests` (6 facts) fails CI on any reference to forbidden DbSet/namespace (`LedgerIsolationTests.cs:28-228`). `PosFinancialReportingAuthorityCertificationTests` additionally asserts `JournalStatus.Posted`, `RepeatableRead`, `GetAccountBalancesAsOfAsync`, and absence of `_cache.GetOrCreateAsync` / `IsActive` shadow filters (`PosFinancialReportingAuthorityCertificationTests.cs:5-57`).
- Code references: `ILedgerOnlyService.cs:23`, `ForbiddenBusinessDbSets.cs:12-67`, `LedgerIsolationTests.cs:22`, `PosFinancialReportingAuthorityCertificationTests.cs:3`.
- Documentation references: FLOW-ACC-08 §2, §9.2, §16.
- Implementation proof: The service references only `_db.LedgerEntries`, `_db.LedgerAccounts`, and `_db.LedgerAccounts`/joined `Account` — all in `AllowedLedgerDbSets`.
- Priority: P3. Confidence: High.

---

### F-08-05 — Reports recompute under a RepeatableRead transaction; no stale snapshot ✅ CONFIRMED
- Severity: ARCHITECTURAL IMPROVEMENT (positive finding)
- Evidence: `BeginConsistentReadTransactionAsync` opens `BeginTransactionAsync(IsolationLevel.RepeatableRead, ...)` when relational and no outer tx exists (`FinancialReportService.cs:650-658`). Server-side caching disabled (`GetOrCreateCachedAsync` always calls `factory`, `FinancialReportsController.cs:279-287`).
- Code references: `FinancialReportService.cs:47,130,199,282,381,465,560,650-658`; controller `:279-287`.
- Implementation proof: A single report is internally consistent; no materialized view to drift.
- Risk note: Cross-report consistency is NOT guaranteed within one request (each report opens its own tx), but this is acceptable for reporting and documented.
- Priority: P3. Confidence: High.

---

### F-08-06 — No date-range order validation (`from <= to` not enforced) 🟡 MEDIUM (gap, documented as NOT IMPLEMENTED)
- Severity: MEDIUM
- Evidence: Controller normalizes dates to UTC (`NormalizeToUtc`, `FinancialReportsController.cs:46-54`) but never validates `from <= to`. If a client sends `from > to`, the EF predicate `JournalDate >= from && JournalDate <= to` returns an empty set (no crash, no error) — silently producing an empty/zero report rather than rejecting the inverted range.
- Code references: `FinancialReportsController.cs:46-54, 64-76, 131-143, 152-162, 171-183`.
- Documentation references: FLOW-ACC-08 §9.4 ("No range-order validation (no `from<=to` guard) — NOT IMPLEMENTED").
- Implementation proof: No guard; predicate is `>= fromDate && <= toDate`; if `fromDate > toDate` the interval is empty.
- Business impact: Silent empty reports could mislead a user into thinking a period had no activity.
- Accounting impact: Low (no corruption), but reduces report trustworthiness.
- Architecture impact: Minor validation gap on the read side.
- Risk: Medium-low. Root cause: validation omitted.
- Recommended solution: Add `if (from > to) return BadRequest(...)` in `ExecuteReportAsync`/per-endpoint, or swap/validate in controller.
- Migration impact: additive. Regression risk: Low.
- Priority: P2. Confidence: High.

---

### F-08-07 — No period / closed-period / fiscal lock on reports 🟡 MEDIUM (gap, documented as NOT IMPLEMENTED)
- Severity: MEDIUM
- Evidence: Reports read any date range regardless of whether a period is closed (`FinancialReportService.cs:56-62, 216-223, 297-303`). No reference to any `Period`/`FiscalPeriod`/`ClosedPeriod` entity or lock.
- Code references: `FinancialReportService.cs:41-454` (all date filters are open).
- Documentation references: FLOW-ACC-08 §9.5 ("Period Validation — NOT IMPLEMENTED").
- Business impact: A user can run a P&L/Balance Sheet for a closed historical period after adjustments — acceptable for reporting, but there is no "as-was-at-close" guarantee.
- Recommended solution: Out of scope for read-only reports; period locking belongs to the posting engine (FLOW-ACC-04). Document as intentional.
- Priority: P3. Confidence: High.

---

### F-08-08 — Multi-currency not supported (single `decimal(18,4)`, no currency column) 🟡 MEDIUM (gap, documented)
- Severity: MEDIUM
- Evidence: `LedgerEntry.DebitAmount/CreditAmount` are `decimal(18,4)` with no currency (`LedgerEntry.cs:45-52`). No report DTO carries a currency field. Currency is a front-end formatting concern only.
- Code references: `LedgerEntry.cs:45-52`; all `...Report.cs` DTOs have no currency property.
- Documentation references: FLOW-ACC-08 §3 (Currency ❌), §16.
- Business impact: Multi-currency tenants cannot reconcile per-currency; amounts are tenant-base-currency only.
- Recommended solution: If multi-entity/multi-currency is required, add currency to Account/LedgerEntry and partition reports by currency — significant change, currently N/A.
- Priority: P3. Confidence: High.

---

### F-08-09 — Retained Earnings / profit carry-forward NOT implemented 🟡 MEDIUM (gap, documented)
- Severity: MEDIUM
- Evidence: Balance Sheet Equity section = raw `AccountType.Equity` ledger balances only (`FinancialReportService.cs:289-358`). No closing-entry / retained-earnings roll-forward logic exists anywhere.
- Code references: `FinancialReportService.cs:289-369`; `BalanceSheetReport.cs:19-29, 38-48`.
- Documentation references: FLOW-ACC-08 §7 ("Retained Earnings / Profit Carry Forward = NOT IMPLEMENTED"), §16.
- Business impact: A Balance Sheet taken at a date will NOT show accumulated retained earnings from prior periods unless an explicit Equity account holds it; the P&L net profit is not automatically rolled into Equity.
- Recommended solution: Implement a year-end closing entry (new journal via FinancialPostingService) that moves P&L net to a Retained Earnings equity account — must go through the write path, not reports.
- Priority: P2. Confidence: High.

---

### F-08-10 — Cash Flow Investing/Financing sections always empty (placeholder) 🟡 LOW (gap, documented)
- Severity: LOW
- Evidence: `InvestingActivities` and `FinancingActivities` are returned as empty lists; `NonCashAdjustments` empty (`FinancialReportService.cs:450-452`). Only Operating (Net Profit + working-capital deltas from code prefixes `10*`/`12*`/`13*`/`20*`) is computed.
- Code references: `FinancialReportService.cs:388-453`; `CashFlowReport.cs:41, 63, 77`.
- Documentation references: FLOW-ACC-08 §7 (Cash Flow), §16.
- Business impact: Incomplete cash flow for businesses with investing/financing activity; `Reconciles` may still pass because those sections are zero and excluded from `NetCashChange`.
- Note: `Reconciles` (`CashFlowReport.cs:96`) checks `OpeningCash + NetCashChange == ClosingCash`; with empty investing/financing this can pass while understating cash movements.
- Priority: P3. Confidence: High.

---

### F-08-11 — Account Summary `IsTreasury` mapped from `Account.IsSystem` (naming/semantics mismatch) 🟡 LOW
- Severity: LOW
- Evidence: In `GetAccountSummariesAsync`, `IsTreasury = a.IsSystem` (`FinancialReportService.cs:576, 589`). The DTO field is named `IsTreasury` but is populated from `Account.IsSystem`. System accounts include cashbox/bank/clearing, which are treasury-relevant, but the name implies a narrower "treasury" concept.
- Code references: `FinancialReportService.cs:576, 589`; `IFinancialReportService.cs:120` (`AccountSummary.IsTreasury`).
- Documentation references: FLOW-ACC-08 §3/§4 (AccountSummary DTO).
- Risk: Cosmetic/semantic; could confuse consumers expecting a dedicated "treasury" flag.
- Recommended solution: Either rename DTO field to `IsSystem` or introduce a real `IsTreasury` flag on `Account` if treasury distinction is needed.
- Priority: P3. Confidence: High.

---

### F-08-12 — Report parameters not persisted for audit (no report audit log) 🟡 LOW (partial, documented)
- Severity: LOW
- Evidence: Reports embed `GeneratedAtUtc` and `TenantId` (`*Report.cs:12-15`), and GL lines surface `JournalNumber`/`SourceType`/`SourceReference`/audit trail from the ledger. But there is NO separate report-request audit store recording who requested which report with which parameters.
- Code references: `TrialBalanceReport.cs:12-15`; `GeneralLedgerEntry.cs:45-49`; controller `:289-301` (only logs on exception).
- Documentation references: FLOW-ACC-08 §3 (Audit 🟡 Partial), §17 (Audit Strategy).
- Business impact: "Who ran the Balance Sheet as of X for tenant Y" is not independently auditable beyond application logs.
- Recommended solution: Optional — emit a structured audit event on successful report generation if regulatory audit-trail of *report access* is required.
- Priority: P3. Confidence: High.

---

### F-08-13 — General Ledger opening-balance boundary uses `fromDate.AddDays(-1)` (boundary subtlety, safe) ℹ️ ARCHITECTURAL IMPROVEMENT (informational)
- Severity: ARCHITECTURAL IMPROVEMENT
- Evidence: `GetGeneralLedgerAsync` computes opening balance as `GetAccountBalanceAsOfAsync(tenantId, accountId, fromDate.AddDays(-1), ct)` (`FinancialReportService.cs:145`). The helper sums entries with `JournalEntry.JournalDate <= asOfDate` (`FinancialReportService.cs:622`). Because `JournalEntry.JournalDate` is stored as `.Date` (midnight, `JournalEntry.cs:207`), `<= fromDate.AddDays(-1)` is equivalent to `< fromDate`. Correct, but the `-1 day` convention is fragile if any future code stores intra-day journal timestamps.
- Code references: `FinancialReportService.cs:145, 622`; `JournalEntry.cs:207`.
- Recommended solution: Prefer `fromDate.AddTicks(-1)` or a half-open `< fromDate` exclusive predicate to be robust to time components; document the assumption.
- Priority: P3. Confidence: Medium.

---

### F-08-14 — Reversed journals included (net-correct by design) ✅ CONFIRMED (behavior, not defect)
- Severity: ARCHITECTURAL IMPROVEMENT (positive)
- Evidence: Report filters accept `JournalStatus.Reversed` alongside `Posted` (`FinancialReportService.cs:59-60, 152-153, 219-220, 300-301, 479-480, 620-621`). A reversal journal creates offsetting ledger entries, so including both original + reversal nets to zero — correct double-entry behavior.
- Code references: `FinancialReportService.cs:59-62` etc.
- Documentation references: FLOW-ACC-08 §3 (Status filter), §16.
- Note: This matches canonical doc; no issue. Documented for completeness because "reversed" can look like a bug to a casual reviewer.

---

## 3. Deep-Investigation Answers (mandatory)

1. **Can historical journals be modified?** No. `JournalEntry` lines are immutable (`LedgerEntry.cs:11-15`, `JournalEntry.cs:8-17`). Only status transition `Posted -> Reversed` is allowed (`JournalEntry.cs:119-127`); reports never write.
2. **Can ledger truth diverge from GL?** The GL IS the source of truth; reports recompute from it every call (F-08-02). No divergence possible within a single report (RepeatableRead, F-08-05). Cross-report drift only if ledger changes mid-batch (no distributed tx) — acceptable for reporting.
3. **Can AccountLedger diverge from reality?** Out of FLOW-ACC-08 scope. `AccountLedger.cs` does not exist in this repo; party balances (Statement of Account) are covered by a different FLOW (ReportsController aging). This audit only covers `FinancialReportsController`/`FinancialReportService`. No evidence of `AccountLedger` usage here.
4. **Can Reports become inconsistent?** Only across separate report calls if the ledger mutates between them; each individual report is internally consistent. No stored report state to rot.
5. **Can Daily Closing produce accounting inconsistencies?** Not in the reporting surface — reports are read-only. Daily Closing belongs to the posting engine (different FLOW).
6. **Can Adjustments bypass FinancialPostingService?** N/A to reports; reports only read. (Adjustment journals are written by the posting engine per canonical doc.)
7. **Can Expenses bypass Outbox?** N/A — P&L reads `LedgerEntries`, not an Expenses table (F-08-02, FLOW-ACC-08 §16). No business-table access.
8. **Can Payments create inconsistent balances?** N/A to reports.
9. **Can Statement Of Account disagree with General Ledger?** Out of scope (different FLOW). Within FLOW-ACC-08, all statements agree with the ledger by construction.
10. **Can asynchronous posting lose financial integrity?** Reports are decoupled from the outbox/posting pipeline (FLOW-ACC-08 §11). Reports only see `Posted`/`Reversed` journals, so they can never reflect an in-flight/unposted entry.
11. **Can idempotency fail?** N/A to read-only reports. Idempotency keys live on `JournalEntry` (`JournalEntry.cs:82-89`), enforced at post time.
12. **Can replay corrupt accounting?** N/A — reports never write; replay of posting is a write-path concern.
13. **Can race conditions create duplicate journals?** N/A to reports.
14. **Can balances become negative unexpectedly?** `Account.PreventNegative` is enforced by the posting engine (`Account.cs:157-161`), not reports. Reports simply display computed balances.
15. **Can orphan records exist?** Report queries join `Account`/`JournalEntry`; `GetGeneralLedgerAsync` throws `InvalidOperationException` → 404 if the account is missing (`FinancialReportsController.cs:104-112, FinancialReportService.cs:141-142`). No orphan exposure.
16. **Can audit records become inconsistent?** Reports do not create audit records; the ledger is the audit trail. Report-access audit is partial (F-08-12).

---

## 4. Strengths (summary)
- Strict read-only projection: no write calls anywhere; all `AsNoTracking`.
- Single source of truth: every report aggregates `LedgerEntries` filtered to `Posted`/`Reversed`; statements never use denormalized `Account.CurrentBalance`.
- Strong tenant isolation: `TenantId` predicate in every query; tenant supplied by `RequiredTenantId`, never client-controlled.
- Clean CQRS read side: pure query service, recompute-on-read, `RepeatableRead`, no materialized view, no server cache.
- CI-enforced ledger-only sourcing (`ILedgerOnlyService` + `ForbiddenBusinessDbSets` + `LedgerIsolationTests` + certification tests).
- Double-entry verified per report (`IsBalanced`, `Variance`, `Reconciles`); reversed journals handled net-correctly.

---

## 5. Missing / Partial Implementation (recap)
- `from <= to` range validation — NOT IMPLEMENTED (F-08-06).
- Period / closed-period / fiscal lock — NOT IMPLEMENTED (F-08-07).
- Multi-currency — NOT IMPLEMENTED (F-08-08).
- Retained earnings / profit carry-forward — NOT IMPLEMENTED (F-08-09).
- Cash Flow Investing & Financing activities — empty placeholders (F-08-10).
- Report-access audit log — NOT IMPLEMENTED (F-08-12).
- No server-side caching, no snapshots, no pagination, no multi-account/free-text filters, no dynamic grouping — all documented as intentional/by-design.

---

## 6. Severity Count
- CRITICAL: 0
- HIGH: 0
- MEDIUM: 4 (F-08-06, F-08-07, F-08-08, F-08-09)
- LOW: 3 (F-08-10, F-08-11, F-08-12)
- ARCHITECTURAL IMPROVEMENT: 7 (F-08-01, F-08-02, F-08-03, F-08-04, F-08-05, F-08-13, F-08-14)

---

## 7. Does implementation match the canonical doc?
**YES — closely.** All claims in FLOW-ACC-08 (read-only projection, ledger source-of-truth, tenant isolation, CQRS, recompute-on-read, RepeatableRead, CI ledger-only enforcement, and the explicit NOT-IMPLEMENTED list) are corroborated by the live code. No contradiction found. The only nuances: F-08-11 (`IsTreasury` ⇐ `IsSystem` naming) and F-08-13 (opening-balance `-1 day` boundary) are minor correctness/semantics notes not called out in the doc; neither is a defect.
