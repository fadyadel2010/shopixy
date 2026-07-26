# FLOW-ACC-10 — Daily Closing — Independent Implementation Audit

**Auditor:** Agent-10 (Daily Closing Financial Architecture Auditor)
**Date:** 2026-07-20
**Canonical doc:** `FLOW-ACC-10-DAILY-CLOSING.md` (implementation-backed)
**Methodology:** `AUDIT-METHODOLOGY.md` (followed exactly; implementation = source of truth)
**Result:** Implementation MATCHES the canonical doc. Daily Closing is **read-only/derived** by construction — the close command posts **no journal**, and the Z-Report is a **live CQRS projection of the ledger**. No unjournaled financial mutation exists. A few additional subtle issues (not emphasized in the doc) were found and recorded below.

---

## 0. Verdict (answers to the key discovery questions)

**(a) Is daily closing READ/report only, or does it create journals/ledger entries?**
READ-ONLY at the financial layer. The close command (`CashboxController.CloseCashbox`) flips only the `Cashbox.IsActive` state flag and writes an `AuditLogs` row. It performs **zero** `PostJournalAsync` / ledger writes. The Z-Report (`ReportsController.GetDailyCloseReport`) is `AsNoTracking`, purely aggregating `LedgerEntries`. **No journals are created by either path.**
*Evidence:* `CashboxController.cs:516-526` (difference computed, `SaveChangesAsync` on Cashbox row only, no treasury/journal call); `ReportsController.cs:346-537` (all queries `.AsNoTracking()`); no `PostJournal`/`_treasuryOperationService` reference in the close block.

**(b) Does it reconcile cashbox totals to GL / AccountLedger?**
Yes — by derivation, not by assertion. `ExpectedClosingCash` is computed as the sum of the last `BalanceAfter` across all `1010-*` cashbox ledger accounts (`ReportsController.cs:511-528`), and the close-time `expectedBalance` is read from the ledger via `GetCashboxLedgerBalanceAsync` (`CashboxController.cs:515`, → `TreasuryReadService.GetAccountBalanceAsync`). So the report is a faithful projection of ledger truth. There is **no separate reconciliation control/assertion** (see F-10-05).

**(c) Does it enforce double-entry if it posts anything?**
N/A — it posts nothing. The only mutations are the `Cashbox` state flag and an audit row, neither of which is a financial movement, so double-entry is not applicable and cannot be violated at close.

**(d) Is it idempotent / re-runnable safely?**
Financially idempotent — re-running close cannot double-post because no journal is created. However, audit-trail idempotency is weak (see F-10-01): two overlapping closes each pass the `!IsActive` guard before either commits, producing two audit rows. The state flip is harmless both times.

**Cashbox owns balances?** NO. `Cashbox.CurrentBalance`/`ExpectedBalance` are `[Obsolete]` shadow fields explicitly forbidden (`Cashbox.cs:23,50`); all balances are ledger-derived (`CashboxController.cs:1400-1416`). Verified.

---

## 1. Findings

### F-10-01 — Concurrent close race: no pessimistic lock; overlapping closures pass the guard twice
- **Severity:** MEDIUM
- **Title:** Two simultaneous `POST /cashbox/{id}/close` calls can both pass `!cashbox.IsActive` and both commit, producing duplicate/ambiguous closure audit records (no financial corruption).
- **Evidence:** `CloseCashbox` opens `BeginTransactionAsync` with **no explicit isolation level** (defaults to ReadCommitted) and reads `Cashbox` at `CashboxController.cs:500-501` without an update lock. The `IsActive` guard is at `:508-511`. Two concurrent requests both read `IsActive=true`, both set `false`, both commit.
- **Code references:** `CashboxController.cs:496` (BeginTransactionAsync, default isolation), `:500-511` (read + guard, no UPDLOCK), `:519-526` (flip + commit), `:528-548` (audit).
- **Documentation references:** Canonical doc §9 constraint 6, §10 (isolation level default), §17. The doc notes "no double-entry consistency risk at close time" (true) but does not flag the closure-state race.
- **Implementation proof:** Transaction does not take a row lock before validating state; the read and the guard are not atomic w.r.t. a concurrent transaction. Result: redundant audit rows, `IsActive=false` both times (idempotent state). No ledger mutation occurs, so **no accounting inconsistency**.
- **Business impact:** Ambiguous audit trail; two "CashboxClose" rows for one physical close; managers cannot deterministically tell which is authoritative.
- **Financial impact:** None (no balance change, no journal).
- **Accounting impact:** None to GL/ledger.
- **Architecture impact:** Weak concurrency control on a state transition; provenance/auditability degraded.
- **Operational impact:** Duplicate closure entries in audit reports; possible confusion during dispute resolution.
- **Risk explanation:** Low financial risk, moderate audit-integrity risk. A genuine race condition per methodology's HIGH definition, but downgraded to MEDIUM here because **no financial truth is affected** — only audit duplication.
- **Root cause:** No `IsolationLevel` + no `UPDLOCK`/`SELECT ... FOR UPDATE` on the cashbox read inside the close transaction.
- **Recommended solution:** Use `BeginTransactionAsync(IsolationLevel.RepeatableRead)` or take a pessimistic lock (`_context.Cashboxes.FromSqlInterpolated($"SELECT ... WHERE Id={id} FOR UPDATE")` or EF `WithLock`) before the guard; or guard with a conditional `UPDATE ... WHERE Id=@id AND IsActive=1` and treat 0 affected rows as "already closed."
- **Migration impact:** None.
- **Regression risk:** Low.
- **Priority:** P2
- **Confidence:** High

### F-10-02 — Z-Report counts Reversed-status journals at face value in subtotal breakdowns
- **Severity:** MEDIUM
- **Title:** `GetDailyCloseReport` includes journals with `JournalStatus.Reversed` in CashSales/CardSales/breakdowns at original (non-netted) amounts, so a fully-reversed sale still inflates the breakdown subtotals even though the authoritative `ExpectedClosingCash` stays correct.
- **Evidence:** The report filters `entry.JournalEntry.Status == JournalStatus.Posted || entry.JournalEntry.Status == JournalStatus.Reversed` (`ReportsController.cs:372-373, 391-393, 443-444, 497-499, 514-516`). `CashSales` sums `DebitAmount` where the account is a cashbox prefix (`:417-419`), and there is no subtraction for a corresponding reversal/return within the same SalesInvoice source.
- **Code references:** `ReportsController.cs:369-419` (sales breakdown), `:494-528` (opening/closing balance — the authoritative projection).
- **Documentation references:** Canonical doc §12 (Z-Report aggregation), §7. Doc describes the projection as ledger-derived but does not call out reversal double-count in the subtotal category.
- **Implementation proof:** A sale journal (status `Posted`) later reversed (status `Reversed`) remains matched by the `Reversed` branch. `CashSales` adds its `DebitAmount` with no offset. The `Return` collection only captures `SourceType == "Return"`, not reversals of `SalesInvoice`. Hence the breakdown can overstate cash/by-category totals relative to the net ledger position. Note: `ExpectedClosingCash` (`:511-528`, last `BalanceAfter`) is unaffected because `BalanceAfter` already reflects the reversal — so the **headline reconciliation number is correct**, only the *category subtotals* can misstate.
- **Business impact:** Management reviewing the Z-Report category breakdown may see inflated sales vs. actual cash drawer; mismatch between "Cash Sales" line and the reconciled expected drawer.
- **Financial impact:** None to GL (ledger is authoritative and correct). Reporting-only distortion.
- **Accounting impact:** Breakdown subtotals can disagree with the net ledger movement; the GL is not corrupted.
- **Architecture impact:** CQRS read model subtly inconsistent with its own authoritative projection.
- **Operational impact:** Reconciliation disputes; users may "correct" by posting manual adjustments that create *new* inconsistencies.
- **Risk explanation:** Medium — can mislead operators but cannot corrupt accounting truth.
- **Root cause:** Reversal modeled as a separate `Reversed`-status journal counted at face value instead of netting reversed sales against a return/sales-reversal ledger entry.
- **Recommended solution:** Either (1) exclude `Reversed` from the breakdown and rely on the reversing entry (a `Return`/negative movement) to net it, or (2) explicitly subtract reversals. Add a reconciliation assertion: `OpeningCashBalance + Σ(CashIn) − Σ(CashOut) == ExpectedClosingCash` to surface breakdown drift.
- **Migration impact:** None (read-only report).
- **Regression risk:** Low.
- **Priority:** P2
- **Confidence:** Medium (would require confirming reversal ledger shape; strongly suggested by the `Reversed` filter + face-value summation).

### F-10-03 — Closure audit persistence is fire-and-forget; a closure can commit with NO audit record
- **Severity:** MEDIUM
- **Title:** `LogCashboxAuditAsync` → `AuditService.EnqueueLog` runs in a **separate scope/transaction** (fire-and-forget) after the close transaction commits; if both the immediate SQL write and the retry queue fail, the closure is recorded in state but leaves **no audit trail** — a silent audit-integrity gap.
- **Evidence:** `CloseCashbox` commits the state transaction (`:525-526`) and *then* `await LogCashboxAuditAsync(...)` (`:528`). `AuditService.EnqueueLog` (`:163-180`) wraps the insert in a new `IServiceScope` and, on failure, enqueues to an in-memory `_queue` (`:172-178`); if the host process exits or the queue retries also fail, the record is lost.
- **Code references:** `CashboxController.cs:525-548` (commit-then-audit), `AuditService.cs:163-180` (separate scope + fallback queue).
- **Documentation references:** Canonical doc §11 explicitly states audit runs in "a separate scope/transaction ... fire-and-forget queue with retry" — confirmed. The doc treats this as acceptable; this finding raises the failure edge.
- **Implementation proof:** The audit write is not part of the close transaction; the state flip is durable regardless of audit success. The fallback is an in-memory `ConcurrentQueue`, not durable across restarts.
- **Business impact:** A closed cashbox could have no corresponding `CashboxClose` audit row; compliance/forensic gap.
- **Financial impact:** None to balances.
- **Accounting impact:** Audit lineage incomplete for a financial-state transition.
- **Architecture impact:** Audit durability weaker than transactional state durability (cross-scope, best-effort).
- **Operational impact:** Missing closure records during incidents/outages.
- **Risk explanation:** Medium — loss of closure evidence is an audit-integrity violation (methodology: "audit integrity violation" can be CRITICAL; here it is partial/edge, hence MEDIUM).
- **Root cause:** Best-effort out-of-band audit write with non-durable fallback queue.
- **Recommended solution:** Persist the closure audit inside the close transaction (same `_context`/scope), or use a durable outbox for audit events; alert if the in-memory queue depth grows.
- **Migration impact:** Minor (audit schema unchanged).
- **Regression risk:** Low.
- **Priority:** P2
- **Confidence:** High

### F-10-04 — Server accepts negative `ActualCash` on close (validation only on client)
- **Severity:** LOW
- **Title:** `CloseCashboxRequest.ActualCash` has no server-side non-negative validation; only the front-end modal rejects negatives (`OpenCloseCashboxModal.jsx:73-76`). A direct API call with a negative value is stored and surfaced as a negative "difference."
- **Evidence:** No validation attribute on `ActualCash`; `CloseCashbox` uses `request.ActualCash` directly at `CashboxController.cs:516` with no sign check. Canonical doc §13 negative-case says "server should also reject negative."
- **Code references:** `CashboxController.cs:486-489,516`; `CashboxDTOs.cs` (CloseCashboxRequest); `OpenCloseCashboxModal.jsx:73-76` (client-only).
- **Documentation references:** Canonical doc §13 negative cases (expected-but-not-implemented).
- **Implementation proof:** Negative value flows into `difference = ActualCash - expectedBalance` and into the audit `afterData` with no server rejection.
- **Business impact:** Erroneous closure data if a client bypasses UI validation; corrupt "difference" signal.
- **Financial impact:** None (no balance change).
- **Accounting impact:** None to ledger.
- **Operational impact:** Data-quality noise in audit/closing reports.
- **Risk explanation:** Low — requires API abuse or broken client; no accounting effect.
- **Root cause:** Missing server-side guard.
- **Recommended solution:** Add `if (request.ActualCash < 0) return BadRequest(...)` in `CloseCashbox`.
- **Migration impact:** None.
- **Regression risk:** Low.
- **Priority:** P3
- **Confidence:** High

### F-10-05 — No automated reconciliation control / discrepancy alerting at close
- **Severity:** ARCHITECTURAL IMPROVEMENT
- **Title:** The reconciliation is purely informational. A detected discrepancy (`discrepancyDetected = |difference| > 0.01m`) is recorded in audit but triggers **no control**: no approval, no suspense-account posting, no alert, no lock.
- **Evidence:** `CashboxController.cs:543-547` computes `discrepancyDetected` and returns/audits it; nothing else consumes it. Canonical doc §3 (Approval NOT IMPLEMENTED), §9 (no lock/approval).
- **Code references:** `CashboxController.cs:543-547`.
- **Documentation references:** Canonical doc §3, §7, §9 (approval/lock NOT IMPLEMENTED — consistent).
- **Implementation proof:** `discrepancyDetected` is a boolean on the response/audit only; no downstream enforcement path exists.
- **Business impact:** Cash shortages/overages are silently acknowledged but never actioned in-system.
- **Financial impact:** None (by design — no auto-posting).
- **Accounting impact:** None.
- **Architecture impact:** Reconciliation is read-only by design (matches principles); this is an improvement opportunity, not a defect.
- **Operational impact:** Manual follow-up required; risk of unactioned discrepancies.
- **Risk explanation:** Low; intentional design per architecture principles (Reports never mutate state).
- **Root cause:** Deliberate read-only reconciliation (correct per principles); missing optional control layer.
- **Recommended solution:** Optional: emit a domain event on `discrepancyDetected` for alerting; add a discrepancy threshold policy; consider a managed "cash discrepancy" suspense account via `TreasuryOperationService` if business requires booked over/short.
- **Migration impact:** None.
- **Regression risk:** Low.
- **Priority:** P3
- **Confidence:** High

---

## 2. Strengths (verified in source)

1. **Zero unjournaled financial mutation at close** — `CloseCashbox` writes only the `Cashbox` state row + audit; confirmed no `PostJournal`/`TreasuryOperationService` in the close block. (`CashboxController.cs:516-526`)
2. **Cashbox does not own balances** — `CurrentBalance`/`ExpectedBalance` are `[Obsolete]` and explicitly forbidden; all balances ledger-derived. (`Cashbox.cs:23,50`; `CashboxController.cs:1400-1416`)
3. **Z-Report is a clean CQRS read model** — fully `AsNoTracking`, live ledger projection, `RepeatableRead` consistent read (`ReportsController.cs:353`, `:369-528`).
4. **Tenant isolation enforced** — global query filters `e.TenantId == CurrentTenantId` on both `JournalEntry` and `LedgerEntry` (`ApplicationDbContext.cs:868-873`); close uses `_tenantContext.RequiredTenantId` (`CashboxController.cs:1402-1414`). No cross-tenant leakage possible in the report queries.
5. **Immutable financial truth** — `SaveChangesAsync` guards reject deletes and protect `JournalEntry`/`LedgerEntry` identity and lineage (`ApplicationDbContext.cs:6189-6270`).
6. **Historical journals immutable** — no close/adjustment path mutates posted journals.
7. **Canonical doc is accurate** — every load-bearing claim in `FLOW-ACC-10-DAILY-CLOSING.md` was independently re-verified against source and holds.

---

## 3. Missing implementation (confirmed NOT IMPLEMENTED, consistent with canonical doc)

- **Approval** of closure (role-gated second actor) — NOT IMPLEMENTED.
- **Reopen** distinct endpoint — NOT IMPLEMENTED (re-open via `open`).
- **Treasury / cashbox / business-day lock** — NOT IMPLEMENTED.
- **Shift / Business-Day aggregate** — NOT IMPLEMENTED (day is a query window only).
- **Multi-currency per cashbox** — NOT IMPLEMENTED (base-currency scoped).
- **Closing export endpoint** — NOT IMPLEMENTED (print only).
- **Closing/Reconciliation domain events / Outbox** — NOT IMPLEMENTED in close path.
- **`CashboxTransaction` write path** — entity configured but never written at runtime; reads project from ledger (confirmed via `TreasuryOperationJournalingTests.cs:15`).

These gaps are **documented as intentional** in the canonical doc and represent deliberate adherence to the architecture principles ("Reports never mutate state", "Cashbox never owns balances"), not defects.

---

## 4. Mandatory Deep-Investigation Answers

- **Can historical journals be modified?** NO. `ApplicationDbContext.SaveChangesAsync` guards immutability: journal/ledger identity and lineage are immutable post-posting; deletes rejected (`ApplicationDbContext.cs:6189-6270`).
- **Can ledger truth diverge from GL?** NO via daily closing. The ledger *is* the GL here; closing neither mutates it nor double-posts. (Only report *breakdown subtotals* can drift via F-10-02; the authoritative `ExpectedClosingCash` projection is correct.)
- **Can AccountLedger diverge from reality?** NO via daily closing (no mutations to it).
- **Can Reports become inconsistent?** RISK (F-10-02): subtotal breakdowns can overstate when reversals are counted at face value; the headline reconciliation number stays correct.
- **Can Daily Closing produce accounting inconsistencies?** NO financial inconsistency — it posts no journal and mutates no balance.
- **Can Adjustments bypass FinancialPostingService?** N/A — no adjustment path in closing.
- **Can Expenses bypass Outbox?** N/A to closing.
- **Can Payments create inconsistent balances?** N/A to closing.
- **Can Statement Of Account disagree with GL?** N/A to closing.
- **Can asynchronous posting lose financial integrity?** N/A — closing has no async posting.
- **Can idempotency fail?** Financial idempotency holds (no journal to duplicate). Audit idempotency weak (F-10-01 duplicate rows; F-10-03 possible loss).
- **Can replay corrupt accounting?** N/A.
- **Can race conditions create duplicate journals?** NO journals at close. Only duplicate *audit* rows possible (F-10-01).
- **Can balances become negative unexpectedly?** NO via closing.
- **Can orphan records exist?** NO.
- **Can audit records become inconsistent?** RISK — F-10-03 (loss on closure) and F-10-01 (duplication on concurrent close).

## 5. Required Validation Matrix

| Question | Answer |
|---|---|
| Double Entry | YES (preserved — no mutation; posting paths all use FinancialPostingService) |
| CQRS | YES (read model projections; separate write side) |
| DDD | YES (Cashbox aggregate = state only; Treasury/ledger = money) |
| ERP | YES |
| Accounting | YES (ledger-authoritative) |
| Auditability | PARTIAL (F-10-03 loss risk; F-10-01 duplication) |
| Source Of Truth | YES (General Ledger) |
| Tenant Isolation | YES (global filters, RequiredTenantId) |
| Idempotency | PARTIAL (financial YES; audit weak) |
| Financial Integrity | YES (no unjournaled mutation) |
| Eventual Consistency | N/A (no async in close) |
| Aggregate Boundary | YES (correctly split) |

## 6. Conclusion

Daily Closing is correctly **read-only/derived**: the close command posts **no journal** and mutates **no balance** (only the `Cashbox.IsActive` flag + an audit row), and the Z-Report is a live, ledger-sourced CQRS projection. Cashbox does **not** own balances; reconciliation to GL is achieved by derivation (`ExpectedClosingCash` = last `BalanceAfter` per `1010-*` account). Tenant isolation, journal/ledger immutability, and CQRS boundaries are all intact. The implementation **matches the canonical doc**. The only deviations from an idealized design are non-financial: a closure-state race without a pessimistic lock (F-10-01), reversal-at-face-value in report subtotals (F-10-02), best-effort audit durability (F-10-03), missing server-side negative-amount guard (F-10-04), and the absence of any automated discrepancy control (F-10-05, by design). None create accounting corruption.
