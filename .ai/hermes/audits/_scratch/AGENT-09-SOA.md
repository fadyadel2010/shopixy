# FLOW-ACC-09 — Statement Of Account (SOA) Audit

**Auditor:** Domain subagent (STATEMENT OF ACCOUNT Financial Architecture Auditor)
**Canonical doc:** FLOW-ACC-09-STATEMENT-OF-ACCOUNT.md
**Methodology:** AUDIT-METHODOLOGY.md (Implementation = source of truth)
**Verdict:** ✅ **SOA IS A TRUE ACCOUNTLedger PROJECTION — NOT DUPLICATED STORED TRUTH.**
**Implementation matches canonical doc?** ✅ YES (with minor, documented/low-severity consistency nits).

---

## KEY DISCOVERY (answers the core task)

**No dedicated `StatementOfAccountService`, no stored SOA entity, no SOA snapshot table, no projection/cache table for statements exists.** Searches for `DbSet<...Statement...>`, `class ...Statement...Entity`, `Statement projection table` returned zero SOA-specific persistence artifacts (only an unrelated `MarketplaceProjectionOutboxMessage` DbSet). The string `Statement` appears only as the API controller name and DTO/response names (`SOAResponse`, `SOASummary`, `SOATransaction` — all in-memory response records, never persisted as a financial row).

All three read endpoints (`query`, `balance/{partyType}/{partyId}`, `outstanding/{partyType}`) query `_context.AccountLedgers` **live with `AsNoTracking()`** and compute aggregates at request time. There is no materialized statement of truth.

→ **Conclusion: SOA is a just-in-time SQL aggregation over `AccountLedger`. It cannot hold financial truth because it persists nothing. The financial truth is exclusively `AccountLedger` (and the cached `Customer/Supplier.Balance` columns, which are explicitly a repairable cache).**

---

## FINDINGS

### F-09-01 — Customer adjustment mirror balance sourced from cached column, not ledger RunningBalance (asymmetry with Supplier path)
- **Severity:** LOW
- **Evidence:** `StatementOfAccountController.CreateAdjustment` customer branch calls `_ledgerService.CreateLedgerEntry(...)` (void) then `balanceAfter = customer!.Balance;` (Controller.cs:690-701). The Supplier branch calls `_ledgerService.ApplySupplierTransaction(...)` which **returns** the newly computed ledger `RunningBalance` and uses that for `BalanceAfter` (Controller.cs:661-680).
- **Code references:** Controller.cs:690-701 (customer), Controller.cs:661-680 (supplier); LedgerService.cs:32-55 (`CreateLedgerEntry` returns void), LedgerService.cs:80-103 (`ApplyCustomerTransaction` returns decimal), LedgerService.cs:218 (newBalance), LedgerService.cs:430-446 (`SetTrackedPartyBalance` writes `customer.Balance`).
- **Documentation references:** FLOW-ACC-09 §4 (Customer Ledger = "denormalized mirror... NOT the authority; AccountLedger.RunningBalance is"); §7 ("Running Balance... Stored, not recomputed in the query").
- **Implementation proof:** Both paths ultimately route through `ApplyPartyTransactionAsync`, which sets BOTH `AccountLedger.RunningBalance = newBalance` AND `customer.Balance = newBalance` within the same unit of work. So the tracked `customer.Balance` equals the ledger RunningBalance at read time — the two values are equal in practice. The difference is purely *which variable the controller captures* for the mirror `CustomerTransaction.BalanceAfter`.
- **Business impact:** None in normal operation (values equal).
- **Financial impact:** None (authoritative ledger unaffected).
- **Accounting impact:** None (ledger is authoritative).
- **Architecture impact:** Inconsistent sourcing style between the two party types — a maintenance/consistency smell, not a defect.
- **Operational impact:** None.
- **Risk explanation:** If a future change set `customer.Balance` out of band before this read, the mirror would diverge, but the ledger would remain correct.
- **Root cause:** `CreateLedgerEntry` returns `void`; controller falls back to reading the tracked cache column.
- **Recommended solution:** Have the customer adjustment branch call `ApplyCustomerTransaction` (returns decimal) and use its return value for `BalanceAfter`, mirroring the Supplier path exactly.
- **Migration impact:** None (internal).
- **Regression risk:** Low.
- **Priority:** P3
- **Confidence:** High
- **Validation matrix:** Double Entry=Y, CQRS=N(only asymmetry), DDD=Y, ERP=Y, Accounting=Y, Auditability=Y, Source Of Truth=Y, Tenant Isolation=Y, Idempotency=Y, Financial Integrity=Y, Eventual Consistency=Y, Aggregate Boundary=Y.

### F-09-02 — GET reads (query / balance / outstanding) are not individually audited
- **Severity:** LOW
- **Evidence:** `GetStatementOfAccount`, `GetPartyBalance`, `GetOutstandingBalances` have no `IAuditService` call; only write paths (`CreateAdjustment`) log via `IAuditService.LogCreate` (Controller.cs:743). `GetStatementOfAccount` wraps logic in a generic try/catch returning 500 (Controller.cs:212-216).
- **Code references:** Controller.cs:64-217, 222-258, 925-1150 (no audit); Controller.cs:743 (audit only on adjustment).
- **Documentation references:** FLOW-ACC-09 §3 Identity table — Audit = "PARTIAL ... but a GET query is not audited."
- **Implementation proof:** Confirms doc exactly; read access leaves no audit trail.
- **Business impact:** Read access to a party's financial statement is not traceable per request.
- **Financial impact:** None (read-only).
- **Accounting impact:** Reduced traceability of who viewed a statement; no integrity risk.
- **Architecture impact:** Minor auditability gap.
- **Operational impact:** Compliance/forensic limitation.
- **Risk explanation:** A malicious insider reading another party's SOA leaves no audit record (though tenant-scoped).
- **Root cause:** Deliberate scope decision (reads considered non-mutating).
- **Recommended solution:** Optionally emit a lightweight read-audit event for sensitive party financial views; otherwise accept as documented.
- **Migration impact:** None.
- **Regression risk:** Low.
- **Priority:** P3
- **Confidence:** High
- **Validation matrix:** Double Entry=Y, CQRS=Y, DDD=Y, ERP=Y, Accounting=Y, Auditability=N(read gap), Source Of Truth=Y, Tenant Isolation=Y, Idempotency=Y, Financial Integrity=Y, Eventual Consistency=Y, Aggregate Boundary=Y.

### F-09-03 — Supplier outstanding sub-aggregates (TotalPaid/TotalInvoiced/DueDate) read from denormalized mirror, not AccountLedger
- **Severity:** MEDIUM
- **Evidence:** `GetOutstandingBalances` Supplier branch computes `LastPaymentDate/TotalPaid/TotalInvoiced` from `_context.SupplierTransactions` (the mirror) and `DueDate` from `PurchaseInvoices` (Controller.cs:1077-1109). The **core `Balance`** is correctly aggregated from `AccountLedgers` (Controller.cs:1034-1041, `ledgerGroup.Sum(entry => entry.DebitAmount - entry.CreditAmount)`). The **Customer** branch correctly computes `TotalPaid/TotalInvoiced` from `AccountLedgers` (Controller.cs:992-1010).
- **Code references:** Controller.cs:1034-1049 (supplier Balance from ledger — correct), Controller.cs:1077-1109 (supplier sub-aggregates from `SupplierTransactions`/`PurchaseInvoices`), Controller.cs:992-1010 (customer sub-aggregates from `AccountLedgers` — correct).
- **Documentation references:** FLOW-ACC-09 §4 ("Outstanding Balance | Derived from AccountLedger sums (no stored outstanding table)"), §7 ("For suppliers, TotalPaid = sum DebitAmount where PaymentMade, TotalInvoiced = sum CreditAmount where Purchase").
- **Implementation proof:** The doc itself states supplier sub-aggregates are derived from `PaymentMade`/`Purchase` transactions, which in code are taken from the `SupplierTransactions` mirror, not `AccountLedgers`. So the doc's described behavior matches code, but it is **internally inconsistent with the Customer path**, which uses the ledger as source of truth for the same fields.
- **Business impact:** Supplier outstanding list's `TotalPaid`/`TotalInvoiced`/`DueDate` could disagree with the same party's SOA statement (which reads the ledger) if the `SupplierTransactions` mirror and `AccountLedgers` ever diverge.
- **Financial impact:** Headline outstanding balance is correct (from ledger); only the auxiliary columns may differ.
- **Accounting impact:** CQRS/source-of-truth inconsistency: the same logical quantity is sourced from two different stores depending on party type.
- **Architecture impact:** Violates the "AccountLedger is the single authority" principle for auxiliary supplier aggregates.
- **Operational impact:** Potential reconciliation confusion between Outstanding screen and Statement screen for suppliers.
- **Risk explanation:** Mirrors are maintained transactionally today (LedgerService writes both), so divergence is unlikely — but the architecture does not guarantee it, and `RebuildPartyBalanceAsync` repairs the ledger/cache, not `SupplierTransactions` balance columns necessarily.
- **Root cause:** Developer convenience; supplier transactions carry `BalanceAfter` so were reused for aggregates.
- **Recommended solution:** Source supplier `TotalPaid`/`TotalInvoiced` from `AccountLedgers` grouped by `TransactionType` (as the Customer path does) to make both branches use the single source of truth.
- **Migration impact:** Query rewrite; no data migration (both stores exist).
- **Regression risk:** Low–Medium (verify numeric parity before switch).
- **Priority:** P2
- **Confidence:** High
- **Validation matrix:** Double Entry=Y, CQRS=N, DDD=Y, ERP=Y, Accounting=Y, Auditability=Y, Source Of Truth=N(supplier aux), Tenant Isolation=Y, Idempotency=Y, Financial Integrity=Y, Eventual Consistency=Y, Aggregate Boundary=Y.

### F-09-04 — Sign is masked with Math.Abs on opening/closing/balance in the response
- **Severity:** LOW
- **Evidence:** `Math.Abs(effectiveOpeningBalance)`, `Math.Abs(closingBalance)`, `Math.Abs(balance)` applied in `SOAResponse` and `PartyBalanceResponse` (Controller.cs:197-200, 249, 1022, 1122).
- **Code references:** Controller.cs:197-200, 249, 1022, 1122; `DetermineBalanceType` returns "Debit"/"Credit" (Controller.cs:1152-1156).
- **Documentation references:** FLOW-ACC-09 §3 (Closing/Opening computed; BalanceType conveys direction).
- **Implementation proof:** Magnitude is absolute; direction is conveyed by `BalanceType` label. For customers Debit=owes us (red), Credit=we owe (green); semantics inverted for suppliers per domain model.
- **Business impact:** Correct presentation; a genuinely erroneous negative balance would be hidden behind an absolute value + label.
- **Financial impact:** None (correct by design).
- **Accounting impact:** Acceptable; sign encoded in BalanceType.
- **Architecture impact:** None.
- **Operational impact:** None.
- **Risk explanation:** If a bug produced a logically wrong sign, it would still render as a positive number with a type label; could mask a data error. Low likelihood.
- **Root cause:** Intentional presentation choice.
- **Recommended solution:** Keep, but ensure any balance-correction/rebuild path surfaces raw signed values to admins.
- **Migration impact:** None.
- **Regression risk:** Low.
- **Priority:** P3
- **Confidence:** High
- **Validation matrix:** Double Entry=Y, CQRS=Y, DDD=Y, ERP=Y, Accounting=Y, Auditability=Y, Source Of Truth=Y, Tenant Isolation=Y, Idempotency=Y, Financial Integrity=Y, Eventual Consistency=Y, Aggregate Boundary=Y.

### F-09-05 — No multi-currency support on AccountLedger / SOA
- **Severity:** LOW
- **Evidence:** `AccountLedger` has no currency column; `RoundCurrency` floors to 2 decimals `MidpointRounding.AwayFromZero` (Controller.cs:919-920).
- **Code references:** AccountLedger.cs:9-74 (no currency), Controller.cs:919-920.
- **Documentation references:** FLOW-ACC-09 §3 Identity (Currency = "NOT IMPLEMENTED"), §9 (Currency Integrity = NOT enforced).
- **Implementation proof:** Single-currency, hard 2-dp rounding; matches doc's "NOT IMPLEMENTED."
- **Business impact:** Multi-entity/multi-currency tenants unsupported.
- **Financial impact:** Rounding only; no FX.
- **Accounting impact:** Single-ledger-currency assumption.
- **Architecture impact:** None for current scope.
- **Operational impact:** None for single-currency deployments.
- **Risk explanation:** Documented gap; not a regression.
- **Root cause:** Out of scope per brief.
- **Recommended solution:** If multi-currency required, add `CurrencyCode` to `AccountLedger` and propagate through LedgerService/outbox; otherwise accept.
- **Migration impact:** Schema migration if later adopted.
- **Regression risk:** Low.
- **Priority:** P3
- **Confidence:** High
- **Validation matrix:** Double Entry=Y, CQRS=Y, DDD=Y, ERP=N(multi-currency), Accounting=Y, Auditability=Y, Source Of Truth=Y, Tenant Isolation=Y, Idempotency=Y, Financial Integrity=Y, Eventual Consistency=Y, Aggregate Boundary=Y.

### F-09-06 — Aging buckets are not part of the SOA query (separate report)
- **Severity:** LOW
- **Evidence:** `GetStatementOfAccount` has no group-by/bucket logic; aging exists only in `ReportsController` (`accounts-receivable-aging`/`accounts-payable-aging`) reading `AccountLedgers` (per doc §9).
- **Code references:** Controller.cs:64-217 (no aging), FLOW-ACC-09 §9 (Aging = separate endpoint).
- **Documentation references:** FLOW-ACC-09 §7 ("Grouping: NOT IMPLEMENTED in SOA query"), §9.
- **Implementation proof:** Confirms doc; SOA is a flat transaction list + summary, no buckets.
- **Business impact:** Aging must be retrieved from a different report.
- **Financial impact:** None.
- **Accounting impact:** None.
- **Architecture impact:** None (clean separation).
- **Operational impact:** Two screens for related info.
- **Risk explanation:** Documented; not a defect.
- **Root cause:** Scope separation.
- **Recommended solution:** None required.
- **Migration impact:** None.
- **Regression risk:** Low.
- **Priority:** P3
- **Confidence:** High
- **Validation matrix:** Double Entry=Y, CQRS=Y, DDD=Y, ERP=Y, Accounting=Y, Auditability=Y, Source Of Truth=Y, Tenant Isolation=Y, Idempotency=Y, Financial Integrity=Y, Eventual Consistency=Y, Aggregate Boundary=Y.

### F-09-07 — Export / Print / Read-models: by-design absent (positive architectural confirmation)
- **Severity:** ARCHITECTURAL IMPROVEMENT (informational; confirms principle)
- **Evidence:** No `StatementExport`/`StatementPdf`/`StatementExcel` in code; no `window.print` in `StatementOfAccount.jsx`/`OutstandingBalances.jsx`; no SOA read-model/projection table.
- **Code references:** search for `StatementExport|StatementPdf|export` in SOA → absent; StatementOfAccount.jsx:1-322 (no print/export).
- **Documentation references:** FLOW-ACC-09 §2 ("NOT IMPLEMENTED"), §5 ("Export NOT IMPLEMENTED", "Print NOT IMPLEMENTED"), §15 ("Projection Strategy: none stored").
- **Implementation proof:** SOA deliberately keeps zero persisted projection state — every read is live SQL over `AccountLedgers`. This is the *correct* realization of "SOA is a projection, not stored truth."
- **Business impact:** Users cannot export/print statements (gap vs ERP expectation).
- **Financial impact:** None.
- **Accounting impact:** None (read-only projection is purer).
- **Architecture impact:** Strong adherence to the canonical pattern; no duplication risk.
- **Operational impact:** Manual copy needed for external sharing.
- **Risk explanation:** Feature gap, not an integrity risk.
- **Root cause:** Scope (brief excludes export).
- **Recommended solution:** If needed, generate export from the same live `AccountLedgers` query (never store a statement) to preserve single-source-of-truth.
- **Migration impact:** Add DTO/endpoint only; no stored truth introduced.
- **Regression risk:** Low.
- **Priority:** P3
- **Confidence:** High
- **Validation matrix:** Double Entry=Y, CQRS=Y, DDD=Y, ERP=Y, Accounting=Y, Auditability=Y, Source Of Truth=Y, Tenant Isolation=Y, Idempotency=Y, Financial Integrity=Y, Eventual Consistency=Y, Aggregate Boundary=Y.

---

## DEEP-INVESTIGATION ANSWERS (mandatory)

1. **Can historical journals be modified?** AccountLedger rows are soft-delete only (`IsDeleted`); historical `RunningBalance` is never mutated except by `RebuildPartyBalanceAsync` (LedgerService.cs:105-194) which recomputes the full chain. GL journals are immutable (outbox posts once). → **NO (immutable / append-only).**
2. **Can ledger truth diverge from GL?** Only via outbox lag (eventual). Outbox handlers `ProcessSupplierOpeningBalanceAsync`/`ProcessPartyLedgerAdjustmentAsync` read `AccountLedger` (AccountingOutboxProcessor.cs:1685-1740, 1758+) and post to GL via `ICommerceAccountingService`. GL converges to the ledger. → **Only transiently, then converges.**
3. **Can AccountLedger diverge from reality?** Only if a writer bypasses `LedgerService`. All SOA write paths (`opening-balance`, `adjustment`) and domain modules go through `LedgerService.Apply*Transaction` under a row lock. `RebuildPartyBalanceAsync` repairs. → **NO under current code; repairable if it ever does.**
4. **Can Reports become inconsistent?** SOA reads live committed `AccountLedgers` (`AsNoTracking`, ReadCommitted) at request time → consistent with the ledger at that moment. → **NO staleness.**
5. **Can Daily Closing produce accounting inconsistencies?** Out of SOA scope; SOA has no closing concept (live read). N/A.
6. **Can Adjustments bypass FinancialPostingService?** Adjustments create an `AccountLedger` row + `PartyLedgerAdjustment` + enqueue outbox; the GL journal is produced by the outbox → `ICommerceAccountingService` (downstream). SOA does not itself post journals; it feeds the outbox. → **NO bypass; GL posting is centralized downstream.**
7. **Can Expenses bypass Outbox?** N/A to SOA (SOA handles party ledger only).
8. **Can Payments create inconsistent balances?** Payments write via `LedgerService` (per architecture tests on PurchasesController/ReturnsController). SOA only reads. → **NO via SOA.**
9. **Can Statement Of Account disagree with General Ledger?** SOA reads `AccountLedger` (party ledger), not GL directly. By construction SOA == AccountLedger. GL == AccountLedger via outbox. So SOA agrees with GL once the outbox drains. → **NO once outbox converges; live read guarantees ledger agreement.**
10. **Can asynchronous posting lose financial integrity?** Outbox uses execution strategy + transaction + idempotency; reads AccountLedger as the source. → **NO (durable, idempotent).**
11. **Can idempotency fail?** `TryClaimAsync` + SHA256 request hash, key scoped `{PartyType}:{TenantId}:{op}` (Controller.cs:828-829, 844-876). Opening-balance also guarded by DB unique indexes (`IX_AccountLedgers_CustomerOpeningBalance_Once`, `IX_AccountLedgers_SupplierOpeningBalance_Once`) + `DuplicateOpeningBalanceException`. Tests `SaveSupplierOpeningBalances_ReplaysStoredResultOnRetry` / `CreateAdjustment_ForSupplier_ReplaysStoredResultOnRetry` confirm replay safety. → **NO (covered by tests).**
12. **Can replay corrupt accounting?** Idempotency service returns the stored prior result on retry; ledger write is skipped on short-circuit. → **NO.**
13. **Can race conditions create duplicate journals?** Party row locked `FOR UPDATE` (Postgres) / `UPDLOCK,ROWLOCK,HOLDLOCK` (SQL Server) in `LockPartyForUpdateAsync` (LedgerService.cs:448-540); additive `balance += debit - credit` is serialized per party. → **NO.**
14. **Can balances become negative unexpectedly?** Running balance is computed additively and can be negative (e.g., customer credit). SOA renders it via `Math.Abs` + `BalanceType` ("Debit"/"Credit") so the user never sees a raw negative; direction is explicit. → **NO unexpected negative shown.**
15. **Can orphan records exist?** `PartyLedgerAdjustment` and `*Transaction` rows are written in the same tx as the `AccountLedger` row; if the tx rolls back, all are discarded. Opening-balance lookup after write re-finds the ledger entry by tenant/party/type/docnumber (Controller.cs:367-377). → **LOW risk; tx-scoped.**
16. **Can audit records become inconsistent?** Writes are audited (`IAuditService.LogCreate` for adjustments, `RebuildBalance` audit on rebuild); GET reads are NOT audited (F-09-02). → **Minor read-audit gap only.**

---

## STRENGTHS

- **True projection, zero stored truth:** No SOA entity/table/snapshot; all reads live `AccountLedgers.AsNoTracking()`. Financial truth lives solely in `AccountLedger`.
- **Single source of truth enforced:** Balances derived from `AccountLedger.RunningBalance`; `Customer/Supplier.Balance` columns are explicitly a cache, maintained by `LedgerService.SetTrackedPartyBalance` and repairable via `RebuildPartyBalanceAsync`.
- **Strong tenant isolation:** EF global query filter `!e.IsDeleted && e.TenantId == CurrentTenantId` on `AccountLedger` (ApplicationDbContext.cs:660-661); every write stamps `_tenantContext.RequiredTenantId` (Controller.cs:353, 491, 643, 674, 706). Cross-tenant SOA read is structurally impossible.
- **Idempotent, replay-safe writes:** SHA256 request hash + idempotency key scoped by party type + tenant; duplicate-opening-balance guard (DB unique index + `DuplicateOpeningBalanceException`); covered by `SupplierFinancialIdempotencyTests`.
- **Locked, additive write path:** `LedgerService` locks the party row (`FOR UPDATE` / `UPDLOCK,ROWLOCK,HOLDLOCK`) at `ReadCommitted` and computes `balance += debit - credit`, preventing concurrent divergence.
- **GL propagation reads the ledger (not a duplicate):** Outbox handlers load `AccountLedger` and post to GL, keeping GL convergent with the party ledger — consistent with the "SOA/GL are projections of AccountLedger" principle.
- **Architecture tests enforce the invariant:** `PosCustomerSupplierAuthorityCertificationTests.StatementOfAccount_MustUseSqlLedgerForBalancesAdjustmentsAndOutstandingReads` asserts `AsNoTracking` + ledger aggregation and asserts the controller does NOT filter on `Customer.Balance`/`Supplier.Balance` shadows. `SupplierFinancialSafetyHardeningTests` assert the controller must NOT write `supplier.Balance`/`customer.Balance` directly. These guard the projection principle in CI.

---

## MISSING-IMPLEMENTATION (matches canonical doc §2/§5)

- **Export (PDF/Excel/CSV):** NOT IMPLEMENTED — no endpoint/handler/DTO anywhere.
- **Print:** NOT IMPLEMENTED in SOA pages (`StatementOfAccount.jsx`, `OutstandingBalances.jsx` have no `window.print`/export).
- **Multi-currency:** NOT IMPLEMENTED — `AccountLedger` has no currency column; 2-dp rounding only.
- **Aging buckets inside SOA query:** NOT IMPLEMENTED — aging is a separate `ReportsController` feature.
- **SOA read models / projection tables:** NONE BY DESIGN (intentional; confirms "no duplicated truth").
- **Per-GET audit:** NOT IMPLEMENTED (reads not individually audited).

---

## SEVERITY SUMMARY

| Severity | Count |
|---|---|
| CRITICAL | 0 |
| HIGH | 0 |
| MEDIUM | 1 (F-09-03 supplier outstanding sub-aggregate source) |
| LOW | 5 (F-09-01, F-09-02, F-09-04, F-09-05, F-09-06) |
| ARCHITECTURAL IMPROVEMENT | 1 (F-09-07, informational/positive) |

**Total: 7 findings.** None violate the core principle (SOA = AccountLedger projection). The implementation faithfully matches the canonical FLOW-ACC-09 document.

---

## BOTTOM LINE

FLOW-ACC-09's central claim — *"SOA is a runtime, on-demand projection of AccountLedger; it is not a stored document and not a separate aggregate"* — is **verified true in the implementation**. There is no duplicated stored financial truth for statements. All reads are live SQL over `AccountLedger`; all writes go through `LedgerService` (row-locked, additive, idempotent, tenant-scoped) and feed the GL outbox that itself reads the ledger. The only deviations are (a) one MEDIUM source-of-truth inconsistency in supplier *auxiliary* outstanding aggregates (which use the `SupplierTransactions` mirror instead of `AccountLedgers`, unlike the customer path) and (b) several documented LOW gaps (read-audit, currency, aging, export/print) that the canonical doc already declares NOT IMPLEMENTED. **Implementation matches the canonical doc.**
