# FALSE-POSITIVE ELIMINATION REPORT — Shopixy ERP Accounting Audit (6 Domains)

**Agent:** False-Positive Elimination Subagent
**Date:** 2026-07-20
**Method:** Every finding's cited file:line was re-read against live source. A finding SURVIVES only if the cited code actually supports the claim. REJECTED only for: assumption-based, contradicted-by-implementation, or no-evidence-at-cited-location.

## TOTALS
- Total findings reviewed: **54** (F-06: 5, F-07: 7, F-08: 14, F-09: 7, F-10: 5, F-11: 14* — see note)
- SURVIVES: **54**
- REJECTED: **0**
- Notable evidence nuances / corrections-to-record: **3** (F-07-04, F-08-11, F-06-01)

\*F-11 enumerated F-11-01..F-11-14 = 14 findings (the severity table in the source said "TOTAL:12" but 14 distinct F-IDs exist; all 14 reviewed).

**Conclusion: NO genuine false positives. Every finding is implementation-backed.** The audit reports are unusually clean — auditors cited real code and the claims hold. The only issues are (a) two findings whose *evidence phrasing* is slightly over-broad, and (b) F-06-01/F-06-02 being out-of-canonical-scope (process/provenance notes, not defects). None rejected.

---

## AGENT-06 PAYMENTS (5 findings)

- **F-06-01 — Scope mismatch (files = Order-Payment runtime scoped OUT by FLOW-ACC-06 §6)** → SURVIVES (as a process/provenance finding, not a code defect). VERIFIED: `FLOW-ACC-06-PAYMENTS.md:6` explicitly EXCLUDES `PaymentLifecycleService` + gateway adapters; the task's file list nonetheless mapped to them. Valid audit-process note. Confidence High. NOTE: this is documentation/provenance, not a financial defect — keep tagged `ORDER-PAYMENT-RUNTIME (out-of-canonical-scope)`.

- **F-06-02 — Cancelling/failing a collected order posts NO reversal journal (GL divergence)** → SURVIVES (CRITICAL). VERIFIED `PaymentLifecycleService.cs:2212-2488` `HandleFailureAsync`: sets `Order.PaymentStatus`/`Order.Status`, releases inventory/coupons, publishes events, `SaveChangesAsync` (:2432), commits (:2439). **Zero** calls to `_accountingOutbox`, `_commerceAccounting`, `ReverseJournalAsync`, or `PostJournalAsync` anywhere in the method. Grep confirms no accounting call in the failure path. The CRITICAL claim is implementation-backed. NOTE: subject is Order-Payment runtime (out of FLOW-ACC-06 ERP-party scope per :6) — classify as TRUE financial-integrity defect of that runtime, mis-scoped only as to which canonical doc owns it.

- **F-06-03 — Webhook idempotency asymmetric (Fawry persists log; Stripe/Paymob do not)** → SURVIVES (MEDIUM). VERIFIED `FawryWebhookController.cs:361-370` persists `PaymentWebhookLog` + dedup by `ProviderEventId`; `StripeWebhooksController.cs` has **0** references to `PaymentWebhookLog` (grep). Claim holds. Confidence Medium (downstream CommerceHub dedup mitigates).

- **F-06-04 — Settlement batch/order orphan risk + no reconcile guard** → SURVIVES (LOW). VERIFIED `AccountingOutboxProcessor.cs:50-71` `_criticalFinancialTruthEventTypes` includes `GatewaySettlement`/`CodSettlement`; `PaymentSettlementOrder.cs` FK to batch. Critical outbox guarantees eventual posting. Claim holds; correctly filed as LOW/sound.

- **F-06-05 — POS cash/card customer AccountLedger has no GL AR movement** → SURVIVES (ARCH-IMPROVEMENT). VERIFIED `CommerceAccountingService.PostPosSaleAsync:90-156`: Cash/Card/Default DEBIT cashbox/VisaClearing (no AR 1410); only Credit (`:135-143`) DEBITs `AccountsReceivable`. Party sub-ledger decoupled for cash/card by design. Claim holds.

---

## AGENT-07 EXPENSES (7 findings)

- **F-07-01 — Negative/zero expense amount accepted (no validation)** → SURVIVES (HIGH). VERIFIED `ExpensesController.cs:214` (`Amount = request.Amount`) and `:301` (update) with no sign check; DTO has no `[Range]`. DOWNSTREAM `PostPosExpenseAsync` posts whatever `Amount` is. Claim holds. Confidence High.

- **F-07-02 — No category/supplier/funding-existence check at create** → SURVIVES (MEDIUM). VERIFIED no `ExpenseCategories` query in create path; `SupplierId` optional; funding resolved lazily at posting. Claim holds.

- **F-07-03 — Read endpoints rely solely on global query filter for tenant isolation** → SURVIVES (MEDIUM). VERIFIED `GetExpenses:76`/`GetReport:908` query `_context.Expenses` with no explicit `TenantId` predicate; writes re-apply `RequiredTenantId` after `IgnoreQueryFilters`. Claim holds (reads implicit).

- **F-07-04 — Canonical "COMMENTED legacy writes" label is STALE** → SURVIVES (LOW) **with evidence nuance**. CONCLUSION is TRUE: controller has no commented-out legacy write code; doc label no longer describes reality. **EVIDENCE CORRECTION:** the finding states there are "NO `JournalEntries`/`LedgerEntries`/`AccountLedgers`/`CashboxTransaction` references" — this is imprecise. `ExpensesController.cs:548` contains a *read-only* `await _context.JournalEntries.IgnoreQueryFilters().AsNoTracking().AnyAsync(...)` (in `HasAnyExpenseJournalAsync`), which is not a write. No `LedgerEntries`/`AccountLedgers`/`CashboxTransaction` *write* references exist. Finding survives; evidence should be scoped to "no direct ledger-write references (one read-only journal existence check at :548 exists)."

- **F-07-05 — Expense Report totals can diverge from GL/P&L (temporal)** → SURVIVES (MEDIUM). VERIFIED `GetReport:908-989` sums `_context.Expenses.Amount` (operational table), NOT `LedgerEntries`; `totalExpenses`/`approvedExpenses` at :960-963. Claim holds — operational vs ledger are eventually consistent.

- **F-07-06 — No direct expense reversal/void endpoint** → SURVIVES (LOW/ARCH-IMPROVEMENT). VERIFIED no reverse/void route; reversal via update/delete/approve enqueuing `ExpenseReversal`. Claim holds.

- **F-07-07 — No system-generated expense number; ReferenceNumber free-text** → SURVIVES (LOW). VERIFIED `Expense.cs`/`CreateExpenseDto` `ReferenceNumber` is nullable free text, no generator. Claim holds.

---

## AGENT-08 FINANCIAL REPORTS (14 findings)

- **F-08-01 — Reports strictly read-only (no write side-effects)** → SURVIVES (ARCH-IMPROVEMENT, positive). VERIFIED `grep -c "SaveChanges|_db.Add|_db.Update|_db.Remove|ExecuteUpdate|ExecuteDelete"` in `FinancialReportService.cs` = **0**; `FinancialReportsController.cs` SaveChanges = **0**. All GET, `AsNoTracking`. Claim holds.

- **F-08-02 — Reports read GL (not denormalized `Account.CurrentBalance`)** → SURVIVES (ARCH-IMPROVEMENT, positive). VERIFIED `FinancialReportService.cs:56-321` aggregate `LedgerEntries`; BS uses ledger (:297-321), not `Account.CurrentBalance`. Claim holds.

- **F-08-03 — Tenant isolation at query level** → SURVIVES (ARCH-IMPROVEMENT, positive). VERIFIED every query filters `TenantId`; `RequiredTenantId` injected, never client param. Claim holds.

- **F-08-04 — Ledger-only sourcing enforced by CI** → SURVIVES (ARCH-IMPROVEMENT, positive). VERIFIED `FinancialReportService : ILedgerOnlyService`; `ForbiddenBusinessDbSets` + `LedgerIsolationTests`. Claim holds.

- **F-08-05 — Recompute under RepeatableRead** → SURVIVES (ARCH-IMPROVEMENT, positive). VERIFIED `BeginConsistentReadTransactionAsync` opens `RepeatableRead` (`FinancialReportService.cs:650-658`). Claim holds.

- **F-08-06 — No `from <= to` date-range validation** → SURVIVES (MEDIUM). VERIFIED `FinancialReportsController.cs:46-54` normalizes to UTC, never validates order; predicate `>= from && <= to` returns empty on inversion. Claim holds.

- **F-08-07 — No period/closed-period/fiscal lock** → SURVIVES (MEDIUM). VERIFIED no `Period`/`FiscalPeriod`/`ClosedPeriod` entity referenced in report queries. Claim holds (documented gap).

- **F-08-08 — No multi-currency** → SURVIVES (MEDIUM). VERIFIED `LedgerEntry.DebitAmount/CreditAmount` are `decimal(18,4)`, no currency column; report DTOs carry no currency. Claim holds.

- **F-08-09 — Retained Earnings / profit carry-forward NOT implemented** → SURVIVES (MEDIUM). VERIFIED `FinancialReportService.cs:289-369` `GetBalanceSheetAsync` aggregates raw `AccountType.Equity` ledger balances only; no closing-entry / roll-forward logic anywhere. Claim holds (documented gap).

- **F-08-10 — Cash Flow Investing/Financing sections empty** → SURVIVES (LOW). VERIFIED `FinancialReportService.cs:388-453` Investing/Financing returned empty; only Operating computed. Claim holds (documented gap).

- **F-08-11 — AccountSummary `IsTreasury` mapped from `Account.IsSystem`** → SURVIVES (LOW) **with evidence nuance**. CONCLUSION (naming/semantic mismatch exists) is TRUE, but EVIDENCE is over-generalized. VERIFIED: `FinancialReportService.cs:576` `IsTreasury = a.IsSystem` (in one projection branch) — real. BUT `:589` `IsTreasury = account.IsTreasury` reads the account's *own* `IsTreasury` field (correct). So the mismatch is partial (only one branch). Finding survives; evidence should cite only line 576, not both.

- **F-08-12 — Report parameters not persisted for audit** → SURVIVES (LOW). VERIFIED reports embed `GeneratedAtUtc`/`TenantId` but no separate report-request audit store; controller logs only on exception. Claim holds.

- **F-08-13 — GL opening-balance uses `fromDate.AddDays(-1)`** → SURVIVES (ARCH-IMPROVEMENT, informational). VERIFIED `FinancialReportService.cs:145,391,622` use `AddDays(-1)`; `JournalDate` stored as `.Date`. Correct but fragile. Claim holds.

- **F-08-14 — Reversed journals included (net-correct by design)** → SURVIVES (ARCH-IMPROVEMENT, positive). VERIFIED filters accept `Reversed` alongside `Posted`; reversal creates offsetting entries that net to zero. Claim holds.

---

## AGENT-09 SOA (7 findings)

- **F-09-01 — Customer adjustment mirror balance from cached column, not RunningBalance** → SURVIVES (LOW). VERIFIED `StatementOfAccountController.cs:701` `balanceAfter = customer!.Balance;` (customer branch) vs `:661/:680` supplier branch uses `ApplySupplierTransaction` return. Both ultimately route through `ApplyPartyTransactionAsync` setting both; values equal in practice. Claim holds (maintenance smell).

- **F-09-02 — GET reads not individually audited** → SURVIVES (LOW). VERIFIED `IAuditService.LogCreate` only at `:743` (write/adjustment); GET endpoints (:64-217, :222-258, :925-1150) have no audit call. Claim holds.

- **F-09-03 — Supplier outstanding sub-aggregates read from `SupplierTransactions` mirror, not `AccountLedgers`** → SURVIVES (MEDIUM). VERIFIED customer branch `:992-1010` computes `TotalPaid/TotalInvoiced` from `AccountLedgers`; supplier branch `:1077-1088` reads `SupplierTransactions` (mirror). Core `Balance` at `:1034-1040` correctly from `AccountLedgers`. Inconsistent sourcing between party types. Claim holds.

- **F-09-04 — Sign masked with Math.Abs** → SURVIVES (LOW). VERIFIED `Math.Abs` on opening/closing/balance in responses (`Controller.cs:197-200,249,1022,1122`). Claim holds (by design).

- **F-09-05 — No multi-currency on AccountLedger/SOA** → SURVIVES (LOW). VERIFIED `AccountLedger` no currency column; `RoundCurrency` 2-dp. Claim holds (documented gap).

- **F-09-06 — Aging buckets not in SOA query** → SURVIVES (LOW). VERIFIED SOA has no group-by/bucket; aging is separate `ReportsController` endpoints. Claim holds (documented).

- **F-09-07 — Export/Print/read-models absent by design** → SURVIVES (ARCH-IMPROVEMENT, positive). VERIFIED no export/print; SOA is live SQL over `AccountLedgers`, no stored projection. Claim holds.

---

## AGENT-10 DAILY CLOSING (5 findings)

- **F-10-01 — Concurrent close race (no pessimistic lock)** → SURVIVES (MEDIUM). VERIFIED `CashboxController.CloseCashbox`: `BeginTransactionAsync` at `:496` with default isolation; `IsActive` guard at `:508-511` (no UPDLOCK); flip+commit `:519-526`. Two concurrent requests can both pass guard → duplicate audit rows. No financial corruption. Claim holds.

- **F-10-02 — Z-Report counts Reversed journals at face value in breakdowns** → SURVIVES (MEDIUM). VERIFIED `ReportsController.cs:369-419` breakdown sums `DebitAmount` for `SalesInvoice` entries where status is `Posted||Reversed` (`:372-373`); only `SourceType=="Return"` (`:394`) is netted, not reversals of `SalesInvoice`. `CashSales` `:417-419` adds face value. Headline `ExpectedClosingCash` (`:511-528`) uses `BalanceAfter` (net of reversal) → correct; only *breakdown subtotals* can overstate. Claim holds.

- **F-10-03 — Closure audit fire-and-forget (can commit with no audit record)** → SURVIVES (MEDIUM). VERIFIED `CloseCashbox` commits state tx `:525-526` then `await LogCashboxAuditAsync(...)` `:528`; `AuditService.EnqueueLog` wraps insert in new scope + in-memory fallback queue. Audit not part of close tx. Claim holds.

- **F-10-04 — Server accepts negative `ActualCash`** → SURVIVES (LOW). VERIFIED `request.ActualCash` used at `:516`/`:545`/`:554` with no server-side sign check (client-only validation). Claim holds.

- **F-10-05 — No automated reconciliation control/alerting** → SURVIVES (ARCH-IMPROVEMENT). VERIFIED `discrepancyDetected` computed `:543-547`, recorded in audit only; no downstream enforcement. By-design (reports never mutate state). Claim holds.

---

## AGENT-11 ADJUSTMENTS (14 findings)

- **F-11-01 — Adjustments create new journals via FinancialPostingService** → SURVIVES (ARCH-IMPROVEMENT, positive). VERIFIED `CommerceAccountingService.PostPartyLedgerAdjustmentAsync` → `PostJournalAsync`; new `JournalEntry`/`LedgerEntry` created. Claim holds.

- **F-11-02 — No mutation of historical journals by adjustments** → SURVIVES (ARCH-IMPROVEMENT, positive). VERIFIED only `MarkReversed`/`MarkAsReversal` exist; `ReverseJournalAsync` not wired to adjustments; no PUT/DELETE adjustment routes. Claim holds.

- **F-11-03 — Two-phase balance/journal can diverge if worker permanently fails** → SURVIVES (HIGH). VERIFIED party `AccountLedger` row written synchronously (`LedgerService.cs:220-238` `_context.AccountLedgers.Add` at `:238`) + `SaveChangesAsync` (controller `:726`) before async GL post (worker `:1758-1793`). If worker lost, party truth ≠ GL. Claim holds.

- **F-11-04 — No role-based authorization on adjustment creation** → SURVIVES (HIGH). VERIFIED `[Authorize]` only at `StatementOfAccountController.cs:24`; `CreateAdjustment` (`:535`) has no `Roles=`/policy. Any authenticated user can post. Claim holds.

- **F-11-05 — No negative-balance guard on party running balance** → SURVIVES (MEDIUM). VERIFIED `LedgerService.ApplyPartyTransactionAsync:218` `newBalance = currentBalance + debitAmount - creditAmount` with no floor; GL `PreventNegative` (`FinancialPostingService.cs:566-605`) guards only GL accounts, not party columns. Claim holds.

- **F-11-06 — Adjustments not reconciled/linked to GL at write time** → SURVIVES (MEDIUM). VERIFIED create persists records + outbox but no `JournalEntryId` FK; linkage reconstructed only by `ValidateAdjustmentLinksAsync`. Claim holds.

- **F-11-07 — No approval/reversal/cancellation/deletion workflow** → SURVIVES (MEDIUM). VERIFIED `PartyLedgerAdjustment` has no `Status`; no such endpoints; `ReverseJournalAsync` not wired. Documented NOT-IMPLEMENTED. Claim holds.

- **F-11-08 — No accounting-period / locked-period guard** → SURVIVES (MEDIUM). VERIFIED `CreateAdjustment` performs no period/lock check; `JournalDate = UtcNow`; no `IsPeriodLocked`/`IsClosed` call (grep). Documented NOT-IMPLEMENTED. Claim holds.

- **F-11-09 — Reconciliation safety net exists (`ValidateAdjustmentLinksAsync`)** → SURVIVES (LOW, positive). VERIFIED `FinancialIntegrityService.cs:1003-1076` three-way linkage check. Claim holds.

- **F-11-10 — Idempotency multi-layered and strong** → SURVIVES (LOW, positive). VERIFIED HTTP header + claim + outbox key + posting double-check + DB unique index. Claim holds.

- **F-11-11 — Tenant isolation multi-layer** → SURVIVES (LOW, positive). VERIFIED entity `ITenantOwned`, global filter, `RequiredTenantId` at create, posting request carries tenant. Claim holds.

- **F-11-12 — Frontend does not send required Idempotency-Key header** → SURVIVES (LOW). VERIFIED server requires header (`:539-546`); doc §13 notes frontend omits it (endpoint unusable from shipped UI). Claim holds.

- **F-11-13 — No adjustment history/list/audit UI** → SURVIVES (ARCH-IMPROVEMENT). VERIFIED `Adjustments.jsx` only create modal + live balance; no GET list endpoint. Claim holds.

- **F-11-14 — Adjustment GL accounts dedicated/separable** → SURVIVES (ARCH-IMPROVEMENT, positive). VERIFIED post to `6120`/`6130` vs `1410`/`2010`. Claim holds.

---

## EVIDENCE-NUANCE SUMMARY (survives, but record the correction)
1. **F-07-04** — survives; "no JournalEntries references" is false (read-only `.AnyAsync` at `:548`); no *write* references exist.
2. **F-08-11** — survives; mismatch is partial — only `:576` maps `IsTreasury = a.IsSystem`; `:589` correctly reads `account.IsTreasury`.
3. **F-06-01 / F-06-02** — survive, but are ORDER-PAYMENT-RUNTIME subject, explicitly OUT of FLOW-ACC-06 §6 ERP-party scope. F-06-01 is a process/provenance note (not a defect). F-06-02 is a TRUE financial-integrity defect of that runtime, just owned by a different canonical flow.

## REJECTED FINDINGS
**None.**
