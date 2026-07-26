# AGENT-11 — FLOW-ACC-11 Account Adjustments Audit

**Auditor:** Independent domain auditor (subagent)
**Domain:** FLOW-ACC-11 Account Adjustments
**Canonical doc:** `.ai/hermes/accounts/FLOW-ACC-11-ACCOUNT-ADJUSTMENTS.md`
**Methodology:** `.ai/hermes/audits/_scratch/AUDIT-METHODOLOGY.md`
**Source of truth:** Implementation (read directly from source).
**Date:** 2026-07-20

---

## 0. EXECUTIVE VERDICT

**Does the implementation match the canonical doc?** YES — broadly. Adjustments DO create NEW journals via `FinancialPostingService` and DO NOT mutate historical journals or `AccountLedger` entries. The design is internally consistent with the stated canonical pattern (source-event aggregate → sync party-balance delta + outbox → async GL journal via shared posting engine).

**Headline answer to the core question:** Adjustments create a NEW `JournalEntry` + `LedgerEntry` rows through `FinancialPostingService.PostJournalAsync` (called from `CommerceAccountingService.PostPartyLedgerAdjustmentAsync`, invoked by the `AccountingOutboxProcessor` worker). They never update an existing journal or ledger line. The only journal mutation API in the codebase (`MarkReversed`/`MarkAsReversal`) is **not** wired to adjustments.

**Finding counts by severity:**
- CRITICAL: 0
- HIGH: 2
- MEDIUM: 5
- LOW: 3
- ARCHITECTURAL IMPROVEMENT: 2
- TOTAL: 12

---

## 1. END-TO-END ADJUSTMENT FLOW (verified from source)

```
POST /api/StatementOfAccount/adjustment  (Idempotency-Key header required)
  └─ StatementOfAccountController.CreateAdjustment (StatementOfAccountController.cs:534)
       ├─ ResolveFinancialIdempotencyKey() + TryClaimAsync("SOA_ADJUSTMENT_CREATE") (539-555)
       ├─ Validate: type ∈ {CreditNote,DebitNote,CreditAdjustment,DebitAdjustment} (567-571)
       ├─ Validate: Amount > 0 (580-587); party exists (589-615)
       ├─ Build PartyLedgerAdjustment (Add to DbSet) (640-656)
       ├─ ILedgerService.ApplySupplierTransaction / CreateLedgerEntry (661/690)
       │     └─ writes NEW AccountLedger row (LedgerService.cs:238) + updates Customer/Supplier.Balance
       ├─ Add SupplierTransaction / CustomerTransaction (671/703)
       ├─ IAccountingOutboxService.EnqueuePartyLedgerAdjustmentAsync (721-724)  -> AccountingOutboxMessages row
       ├─ SaveChangesAsync — single transaction (550,726)
       └─ IAuditService.LogCreate("PartyLedgerAdjustment", ...) (743)

[ASYNC WORKER]
AccountingOutboxProcessor.ProcessPartyLedgerAdjustmentAsync (1758)
  ├─ LoadPartyLedgerAdjustmentForAccountingPostAsync  (FOR UPDATE / UPDLOCK) (1226-1270)
  └─ ICommerceAccountingService.PostPartyLedgerAdjustmentAsync (1777)
        └─ FinancialPostingService.PostJournalAsync (CommerceAccountingService.cs:551)
              └─ NEW JournalEntry + NEW LedgerEntry rows (FinancialPostingService.cs:170-186, 224)
```

**Proof the journal is NEW (not mutated):**
- `PostJournalAsync` → `PostJournalInternalAsync` (FinancialPostingService.cs:90) only EVER calls `_db.JournalEntries.Add(journal)` (line 186) and `_db.LedgerEntries.Add(entry)` (line 224). There is no UPDATE/Attach path for existing journals in this method; it returns an existing journal only on idempotent replay (lines 65-73, 114-121) without modification.
- `PostPartyLedgerAdjustmentAsync` builds a fresh `PostingRequest` with `SourceType="PartyLedgerAdjustment"`, `SourceId=adjustment.Id`, `IdempotencyKey="PARTY_ADJUSTMENT:{TenantId}:{AdjustmentId}"` (CommerceAccountingService.cs:539-551) and calls `PostJournalAsync`.

---

## 2. FINDINGS

### F-11-01 — Adjustments create new journals via FinancialPostingService (CONFIRMED CORRECT)
- **Severity:** ARCHITECTURAL IMPROVEMENT (positive control; recorded to evidence the principle is met)
- **Evidence:** `CommerceAccountingService.PostPartyLedgerAdjustmentAsync` (505-559) → `PostJournalAsync` (551). `FinancialPostingService.PostJournalInternalAsync` creates `JournalEntry.Create(...)` (170) + per-line `LedgerEntry.CreateDebit/CreateCredit` (191-225).
- **Code refs:** CommerceAccountingService.cs:505-559, 539-551; FinancialPostingService.cs:39, 90, 170, 186, 224.
- **Doc ref:** FLOW-ACC-11 §1, §7, §15, §16.
- **Implementation proof:** The adjustment posts a brand-new balanced double-entry journal (control account ↔ adjustments account 6120/6130) — never touches any prior journal.
- **Business/Financial/Accounting impact:** Confirms adherence to "Adjustments create new journals (never mutate historical journals)." N/A negative.
- **Validation matrix:** Double Entry=YES, CQRS=N/A(uses both), DDD=YES, ERP=YES, Accounting=YES, Auditability=YES, SourceOfTruth=YES, TenantIsolation=YES, Idempotency=YES, FinancialIntegrity=YES, EventualConsistency=YES, AggregateBoundary=YES.
- **Priority:** P3. **Confidence:** High.

### F-11-02 — No mutation of historical journals by adjustments (CONFIRMED CORRECT)
- **Severity:** ARCHITECTURAL IMPROVEMENT
- **Evidence:** The only journal-state mutators in the codebase are `JournalEntry.MarkReversed` (JournalEntry.cs:119) and `MarkAsReversal` (129). `ReverseJournalAsync` (FinancialPostingService.cs:256) is generic and NOT invoked by any adjustment path (grep for `ReverseJournal` shows no caller in the adjustment flow; `PartyLedgerAdjustment` has no `ReversedBy` field). No PUT/DELETE adjustment endpoints exist (controller enumerates only `[HttpPost("adjustment")]` at 534; no update/delete routes).
- **Code refs:** JournalEntry.cs:119-135; StatementOfAccountController.cs:534; FinancialPostingService.cs:256-359.
- **Doc ref:** FLOW-ACC-11 §16 (misconception "adjustments modify old journal/ledger entries").
- **Implementation proof:** Immutability is enforced both by design (no reverse path) and by entity (only reversible via `MarkReversed`, unused for adjustments).
- **Validation matrix:** All YES as F-11-01.
- **Priority:** P3. **Confidence:** High.

### F-11-03 — Two-phase balance/journal posting can diverge if the outbox worker permanently fails
- **Severity:** HIGH
- **Evidence:** Party balance (`AccountLedger` row + `Customer.Balance`/`Supplier.Balance`) is written SYNCHRONOUSLY at create time (LedgerService.cs:220-238, 239; controller 661/690-719), but the GL journal posts ASYNCHRONOUSLY in the worker (AccountingOutboxProcessor.cs:1758-1793). If the worker crashes before posting and never recovers the message, the party ledger (and AR/AP control column via GL) will reflect the adjustment while the GL journal (accounts 1410/2010 ↔ 6120/6130) never lands → GL vs party truth divergence.
- **Code refs:** StatementOfAccountController.cs:661,690,721-726; LedgerService.cs:220-244; AccountingOutboxProcessor.cs:1758-1793; FinancialPostingService.cs:170.
- **Doc ref:** FLOW-ACC-11 §8, §10.
- **Implementation proof:** Synchronous write of `_context.AccountLedgers.Add(ledger)` + `SaveChangesAsync` (controller 726) commits party truth before worker commits GL truth.
- **Business impact:** Reconcile errors; financial statements vs SOA disagreement.
- **Financial/Accounting impact:** GL (control accounts) out of step with `AccountLedger` running totals.
- **Architecture/Operational impact:** Depends on outbox worker reliability + dead-letter handling (not inspected here; outbox enqueue IS atomic per AccountingOutboxService.cs:749-752).
- **Risk:** Silent divergence if worker message is lost/never retried. Mitigation exists: `FinancialIntegrityService.ValidateAdjustmentLinksAsync` flags adjustments missing journals (ORPHAN_ADJUSTMENT) — see F-11-09.
- **Root cause:** Inherent to the intentional two-phase decoupled design; no synchronous GL post for adjustments.
- **Recommended solution:** Ensure the outbox has a dead-letter/retry-with-alarm path and that `ValidateAdjustmentLinksAsync` is on a monitored schedule; consider an "adjustment posted" status flag on `PartyLedgerAdjustment` so the UI/API can surface unposted adjustments.
- **Migration impact:** Low. **Regression risk:** Low. **Priority:** P1. **Confidence:** High.

### F-11-04 — No role-based authorization on adjustment creation (authenticated users only)
- **Severity:** HIGH
- **Evidence:** `StatementOfAccountController` carries only `[Authorize]` (line 24). `CreateAdjustment` has no `[Authorize(Roles=...)]`, no policy, no admin/accountant check. Any authenticated user within the tenant can post a manual, immediate balance correction to any customer/supplier.
- **Code refs:** StatementOfAccountController.cs:24, 534-538.
- **Doc ref:** FLOW-ACC-11 §1 (audit question "who can adjust party ledgers?"), §5 (no approval stage).
- **Implementation proof:** Only authentication + tenant context gate the endpoint; no `Roles`/`Policy`.
- **Business impact:** A low-privilege clerk (e.g., a cashier) can materially alter a party's balance with no approval.
- **Financial impact:** Unauthorized balance manipulations become part of GL truth.
- **Accounting/Audit impact:** Audit `LogCreate` records the actor (controller 743-748) but cannot prevent the action; no segregation of duties.
- **Architecture/Operational impact:** Violates principle of least privilege / SoD for manual financial corrections.
- **Risk:** Fraud / error exposure; no four-eyes control.
- **Root cause:** Adjustments modeled as self-service, same as SOA reads.
- **Recommended solution:** Add a role/policy requirement (e.g., `RequireRole("Accountant","Admin")` or an `AdjustmentPolicy`) to `CreateAdjustment`; document who may adjust. Pair with the absent approval workflow (F-11-07).
- **Migration impact:** Low (add attribute + configure role). **Regression risk:** Low. **Priority:** P1. **Confidence:** High.

### F-11-05 — No negative-balance guard on the party running balance for adjustments
- **Severity:** MEDIUM
- **Evidence:** `ApplyPartyTransactionAsync` (LedgerService.cs:196-244) computes `newBalance = currentBalance + debitAmount - creditAmount` and writes it with NO `PreventNegative`/lower-bound check. The GL-side `ValidateNoNegativeBalances` (FinancialPostingService.cs:566) only guards accounts flagged `PreventNegative` (line 584 `if (!account.PreventNegative) continue;`) and applies to GL control/adjustment accounts, not to the `Customer.Balance`/`Supplier.Balance` columns. A credit adjustment (or over-large debit) can push a party balance negative without interruption.
- **Code refs:** LedgerService.cs:218-239; FinancialPostingService.cs:566-605 (esp. 584).
- **Doc ref:** FLOW-ACC-11 §9 (rule 4 only rejects Amount<=0; negative-balance check "applies at journal post for flagged accounts").
- **Implementation proof:** No floor check on party `newBalance`; only the GL account flag protects GL control accounts.
- **Business impact:** A customer receivable can go negative (credit balance) — may be legitimate, may be an error; no enforced policy.
- **Financial impact:** Negative supplier payable could mask an overpayment/error.
- **Risk:** Silent over-correction; relies on human correctness.
- **Root cause:** Party-balance validation not centralized for adjustments.
- **Recommended solution:** Add a configurable negative-balance policy for party balances (mirror `PreventNegative` semantics for `Customer.Balance`/`Supplier.Balance`), or explicitly assert credit balances are allowed and document it.
- **Migration impact:** Low. **Regression risk:** Medium (could reject previously-accepted credit adjustments). **Priority:** P2. **Confidence:** High.

### F-11-06 — Adjustments are not reconciled/linked to GL automatically at write time (reconciliation is passive/audit-only)
- **Severity:** MEDIUM
- **Evidence:** At create time, the adjustment persists `PartyLedgerAdjustment` + `AccountLedger` + `Supplier/CustomerTransaction` + outbox row (all atomic, controller 550-726) but does NOT verify a GL journal exists. GL linkage is only reconstructed later by `FinancialIntegrityService.ValidateAdjustmentLinksAsync` (FinancialIntegrityService.cs:1003-1076), which flags ORPHAN adjustments, missing outbox messages, and missing journals as integrity issues. This is a reconciliation/audit safety net, not a hard link at write time.
- **Code refs:** StatementOfAccountController.cs:550-726; FinancialIntegrityService.cs:1003-1076.
- **Doc ref:** FLOW-ACC-11 §8 (two-phase), §11.
- **Implementation proof:** No foreign key or `JournalEntryId` stored on `PartyLedgerAdjustment`; linkage is by `SourceType`/`SourceId` string match only.
- **Business/Accounting impact:** Linkage is discoverable post-hoc, not enforced at commit.
- **Risk:** If `ValidateAdjustmentLinksAsync` is not run, divergence (F-11-03) goes unnoticed.
- **Root cause:** Design relies on outbox exactly-once semantics + audit integrity scan.
- **Recommended solution:** Store the posted `JournalEntryId` on `PartyLedgerAdjustment` once the worker posts (close the loop); make the integrity scan scheduled/alerting.
- **Migration impact:** Medium (add column). **Regression risk:** Low. **Priority:** P2. **Confidence:** High.

### F-11-07 — No approval, reversal, cancellation, or deletion workflow for adjustments
- **Severity:** MEDIUM
- **Evidence:** No `ApproveAdjustment`/`ReverseAdjustment`/`CancelAdjustment`/delete endpoints or service methods. `PartyLedgerAdjustment` has no `Status` field (entity lines 10-31). Generic `ReverseJournalAsync` exists (FinancialPostingService.cs:256) but is NOT wired to adjustments.
- **Code refs:** PartyLedgerAdjustment.cs:10-31; StatementOfAccountController.cs (no adjustment PUT/DELETE/approve/reverse routes); FinancialPostingService.cs:256.
- **Doc ref:** FLOW-ACC-11 §5, §7, §14, §17 (explicitly NOT IMPLEMENTED).
- **Implementation proof:** A mistaken adjustment cannot be reversed, cancelled, or deleted through the system — only a *new compensating* adjustment could be entered manually (no guarantee).
- **Business impact:** Erroneous corrections are hard/impossible to undo cleanly; audit trail shows create-only.
- **Financial impact:** No supported unwind path means corrections accumulate as new journals.
- **Risk:** Operational friction; potential for unbalanced manual "fixes."
- **Root cause:** Documented as out-of-scope / not built.
- **Recommended solution:** Either implement `ReverseJournalAsync` wiring for adjustments (post a reversing journal + `MarkReversed`), or formally document that corrections are handled via new adjustments and provide a UI path.
- **Migration impact:** Medium. **Regression risk:** Low. **Priority:** P2. **Confidence:** High.

### F-11-08 — No accounting-period / locked-period guard on adjustments
- **Severity:** MEDIUM
- **Evidence:** `CreateAdjustment` performs no period/lock check before writing `AccountLedger` + enqueuing the GL journal. `JournalDate = adjustmentTimestamp = DateTime.UtcNow` (controller 632, 647) — always "now," so backdated adjustments into a closed period are not possible, but there is also no guard preventing an adjustment during a locked period.
- **Code refs:** StatementOfAccountController.cs:580-619, 632, 647; CommerceAccountingService.cs:542.
- **Doc ref:** FLOW-ACC-11 §9 ("Locked Period: NOT IMPLEMENTED").
- **Implementation proof:** No call to any `IsPeriodLocked`/`IsClosed` check in the path.
- **Business/Financial impact:** Adjustments can be created even when a period is closed for other operations, potentially reopening closed books.
- **Risk:** Period-integrity violation if other flows enforce locks but adjustments do not.
- **Root cause:** Not implemented (documented).
- **Recommended solution:** Enforce the same period-lock check used by other financial writes before accepting an adjustment.
- **Migration impact:** Low. **Regression risk:** Low. **Priority:** P2. **Confidence:** High.

### F-11-09 — Reconciliation safety net exists (ValidateAdjustmentLinksAsync) — POSITIVE
- **Severity:** LOW (strength/observability note)
- **Evidence:** `FinancialIntegrityService.ValidateAdjustmentLinksAsync` (FinancialIntegrityService.cs:1003-1076) checks every `PartyLedgerAdjustment` for (a) a corresponding party transaction (ORPHAN_ADJUSTMENT), (b) an outbox message, (c) a posted `JournalEntry` with `SourceType="PartyLedgerAdjustment"`. Also flags adjustments with missing `IdempotencyKey` (1374-1387).
- **Code refs:** FinancialIntegrityService.cs:1003-1076, 1374-1387.
- **Doc ref:** FLOW-ACC-11 §11.
- **Implementation proof:** Three-way linkage check across PartyLedgerAdjustments ↔ AccountingOutboxMessages ↔ JournalEntries.
- **Impact:** Directly mitigates F-11-03/F-11-06 by detecting divergence; depends on being run on a schedule.
- **Priority:** P3. **Confidence:** High.

### F-11-10 — Idempotency is multi-layered and strong (POSITIVE)
- **Severity:** LOW (strength)
- **Evidence:** (1) HTTP `Idempotency-Key` header required + pattern `^[A-Za-z0-9-]+$`, max 100 (controller 27-31, 539-546). (2) `TryClaimAsync("SOA_ADJUSTMENT_CREATE", scopedKey, requestHash)` short-circuits duplicates (551-561). (3) Outbox idempotency key `PARTY_ADJUSTMENT:{tenant}:{id}` (AccountingOutboxService.cs:183). (4) `FinancialPostingService` double-checks by idempotency key before and inside the transaction (FinancialPostingService.cs:56-63, 106-121) and verifies line/identity match via `EnsureIdempotentRequestMatches` (526-559). (5) DB unique index `IX_PartyLedgerAdjustments_TenantId_IdempotencyKey` (migration 20260408193000:73-77).
- **Code refs:** StatementOfAccountController.cs:27-31,539-561; AccountingOutboxService.cs:183; FinancialPostingService.cs:56-63,106-121,526-559; migration 20260408193000:73-77.
- **Doc ref:** FLOW-ACC-11 §9 (rules 1,2,10,11), §10.
- **Impact:** Replay at any layer (HTTP, outbox, posting) yields exactly-once business effect + exactly-once journal.
- **Priority:** P3. **Confidence:** High.

### F-11-11 — Tenant isolation enforced at multiple layers (POSITIVE)
- **Severity:** LOW (strength)
- **Evidence:** `PartyLedgerAdjustment : ITenantOwned` with `TenantId` (entity 15); global query filter `!e.IsDeleted && e.TenantId == CurrentTenantId` (ApplicationDbContext.cs:678); `_tenantContext.RequiredTenantId` used at create (controller 643); posting request carries `TenantId` (CommerceAccountingService.cs:541); journal/ledger queries filter by tenant (FinancialPostingService.cs:61,134). Outbox load uses `TenantId` predicate (AccountingOutboxProcessor.cs:1242-1256).
- **Code refs:** PartyLedgerAdjustment.cs:15; ApplicationDbContext.cs:678; controller 643; CommerceAccountingService.cs:541; FinancialPostingService.cs:61,134.
- **Doc ref:** FLOW-ACC-11 §9 (rule 7).
- **Impact:** No cross-tenant leakage observed in the adjustment path.
- **Priority:** P3. **Confidence:** High.

### F-11-12 — Frontend does not send the required Idempotency-Key header
- **Severity:** LOW
- **Evidence:** `soaApi.createAdjustment` calls `POST /StatementOfAccount/adjustment` but (per FLOW-ACC-11 §13, N3) does not inject the `Idempotency-Key` header; the server returns 400 if absent (controller 539-546). This is a client/server contract gap, not an accounting-correctness defect — but it means the endpoint is currently unusable from the shipped UI until the header is added.
- **Code refs:** StatementOfAccountController.cs:539-546; soaApi.js (per doc §13).
- **Doc ref:** FLOW-ACC-11 §13 (N3).
- **Implementation proof:** Server gate exists; frontend wiring for the header not present.
- **Business impact:** Adjustments cannot be created from the browser as shipped.
- **Risk:** Functional gap; once header is added, the strong idempotency of F-11-10 applies.
- **Recommended solution:** Add `Idempotency-Key` header in `soaApi.createAdjustment` (generate client-side UUID).
- **Migration impact:** None. **Regression risk:** None. **Priority:** P2. **Confidence:** High.

### F-11-13 — No dedicated adjustment history/list/audit UI (read-model gap)
- **Severity:** ARCHITECTURAL IMPROVEMENT
- **Evidence:** `Adjustments.jsx` renders only a create modal + live party balance; no adjustment grid/list. No `GET adjustment(s)` endpoint (controller enumerates only balance/opening-balance/adjustment-POST/outstanding). Audit exists only server-side via `IAuditService.LogCreate` (controller 743).
- **Code refs:** StatementOfAccountController.cs:534-761; Adjustments.jsx / AdjustmentModal.jsx (per doc §2, §13).
- **Doc ref:** FLOW-ACC-11 §13, §14, §17.
- **Impact:** Operators cannot review past adjustments in-app; must query audit logs / DB directly.
- **Recommended solution:** Add a read-only adjustment history endpoint + UI grid (tenant-scoped, paginated).
- **Priority:** P3. **Confidence:** High.

### F-11-14 — Adjustment GL accounts are dedicated/separable (positive design)
- **Severity:** ARCHITECTURAL IMPROVEMENT
- **Evidence:** Adjustments post to dedicated GL adjustment accounts `AccountsReceivableAdjustments=6120` / `AccountsPayableAdjustments=6130` against control accounts `1410`/`2010` (CommerceAccountingService.cs:509-520, 531-536). This keeps manual corrections visible and separable from operational postings.
- **Code refs:** CommerceAccountingService.cs:509-520, 531-536; IAccountResolver.cs (AccountCodes).
- **Doc ref:** FLOW-ACC-11 §7, §17.
- **Impact:** Strong auditability/segregation of manual corrections in the GL.
- **Priority:** P3. **Confidence:** High.

---

## 3. MANDATORY DEEP-INVESTIGATION QUESTIONS

- **Can historical journals be modified? (immutability)** NO. Adjustments only create new journals. The only mutators (`MarkReversed`/`MarkAsReversal`) are unused for adjustments. (F-11-02)
- **Can ledger truth diverge from GL?** YES, transiently and potentially permanently if the outbox worker loses a message — party `AccountLedger`/`Balance` committed sync, GL journal async (F-11-03, F-11-06). Mitigated by `ValidateAdjustmentLinksAsync` (F-11-09).
- **Can AccountLedger diverge from reality?** Adjustments write a NEW `AccountLedger` row (LedgerService.cs:238) — never mutate a historical one — so "reality" is additive. Divergence risk is from the sync/async split, not from editing history.
- **Can Reports become inconsistent?** Reports read projections; if GL vs party diverge (F-11-03), a GL-based report and an SOA-based report could disagree until reconciled.
- **Can Daily Closing produce accounting inconsistencies?** Not inspected in this domain, but the async GL post means an adjustment created just before closing could post after closing → period mismatch (related to F-11-08 lock gap).
- **Can Adjustments bypass FinancialPostingService?** NO. The GL journal is always created via `FinancialPostingService.PostJournalAsync` (CommerceAccountingService.cs:551). The synchronous party-balance write via `LedgerService` is a separate, intended path (running balance), not a bypass of GL posting.
- **Can Expenses bypass Outbox?** Out of scope (different domain); adjustments GO THROUGH the outbox (controller 721, AccountingOutboxService.cs:181).
- **Can Payments create inconsistent balances?** Out of scope; adjustment path itself keeps party balance + GL consistent in the happy case.
- **Can Statement Of Account disagree with General Ledger?** Possible only via the worker-failure divergence (F-11-03); otherwise SOA (party `AccountLedger`) and GL (control/adjustment accounts) reflect the same event.
- **Can asynchronous posting lose financial integrity?** Only on worker/infra failure (F-11-03). Idempotency + exactly-once outbox design otherwise preserve integrity (F-11-10).
- **Can idempotency fail?** Strongly mitigated by 5 layered controls (F-11-10). Theorical failure only if the `IdempotencyKey` header is reused with different payloads AND the claim service is bypassed — not possible via the public endpoint.
- **Can replay corrupt accounting?** NO — `FinancialPostingService` returns the existing journal on matching idempotency key and validates line/identity match (EnsureIdempotentRequestMatches, 526-559).
- **Can race conditions create duplicate journals?** NO in the GL layer — serializable transaction + in-transaction idempotency re-check (FinancialPostingService.cs:83,97,106-121). In the party-balance layer, `LockPartyForUpdateAsync` + `EnsureOpeningBalanceNotAlreadyAppliedAsync` serialize (LedgerService.cs:215-216).
- **Can balances become negative unexpectedly?** On the GL side, only for non-`PreventNegative` accounts (FinancialPostingService.cs:584). On the party-balance side, NO floor check exists (F-11-05).
- **Can orphan records exist?** Yes, if the worker fails — detected by `ValidateAdjustmentLinksAsync` (F-11-06, F-11-09).
- **Can audit records become inconsistent?** Audit is create-only (LogCreate). No update/delete audit because those ops don't exist. Audit trail is consistent with the (limited) lifecycle.

---

## 4. STRENGTHS

1. Adjustments create NEW journals via the shared `FinancialPostingService` — core principle satisfied (F-11-01).
2. Historical journals/ledger lines are immutable w.r.t. adjustments — no reverse/delete path wired (F-11-02).
3. Dedicated, separable GL adjustment accounts (6120/6130) keep manual corrections auditable (F-11-14).
4. Multi-layered idempotency (HTTP header + claim + outbox key + posting double-check + DB unique index) (F-11-10).
5. Tenant isolation enforced at entity, query-filter, controller, and posting layers (F-11-11).
6. Passive reconciliation safety net (`ValidateAdjustmentLinksAsync`) detects orphans/missing journals/missing outbox (F-11-09).
7. Pessimistic `FOR UPDATE`/`UPDLOCK` on outbox replay prevents concurrent double-post (AccountingOutboxProcessor.cs:1226-1270).

---

## 5. MISSING / NOT-IMPLEMENTED (explicit)

1. **Approval workflow** (no status field, no approver, no endpoint).
2. **Reversal** of an adjustment (generic `ReverseJournalAsync` not wired).
3. **Cancellation / Deletion** (soft-delete column exists, unused).
4. **Write-off** transaction type (no enum member).
5. **Reclassification** (no endpoint/method).
6. **Locked-period / accounting-period guard** on adjustments.
7. **Role-based authorization** beyond `[Authorize]` (F-11-04).
8. **Negative-balance policy** for party running balance (F-11-05).
9. **Adjustment history / list / audit UI** (F-11-13).
10. **Multi-currency** support (no currency field on entity).
11. **Export / Print** of adjustments.
12. **Stored GL JournalEntryId linkage** on the adjustment record (F-11-06).
13. **Frontend Idempotency-Key header** wiring (F-11-12) — endpoint currently unusable from shipped UI.

---

## 6. CONCLUSION

The Account Adjustments implementation is **faithful to the canonical FLOW-ACC-11 doc**. Adjustments correctly act as a *source-event aggregate*: they persist a durable intent record (`PartyLedgerAdjustment`), write a NEW `AccountLedger` delta synchronously, and post a NEW double-entry GL journal asynchronously through `FinancialPostingService` via the transactional outbox. They do NOT mutate historical journals or ledger lines, and immutability/tenancy/idempotency are well enforced.

The principal risks are **operational/governance**, not accounting-correctness defects: (a) a sync/async divergence if the outbox worker fails (HIGH, F-11-03), and (b) the absence of any role-based authorization, so any authenticated user can post immediate manual balance corrections with no approval (HIGH, F-11-04). MEDIUM gaps include no negative-balance policy on party balances (F-11-05), passive-only GL linkage (F-11-06), no reversal/approval lifecycle (F-11-07), and no period-lock guard (F-11-08). The frontend also lacks the required `Idempotency-Key` header (F-11-12), making the shipped UI path currently non-functional.

**Does implementation match canonical doc?** YES — with the doc's "NOT IMPLEMENTED" gaps confirmed accurate and two HIGH-severity governance gaps (worker-failure divergence, missing role authorization) that the doc acknowledges but does not elevate to required controls.

---

*All findings backed by direct source reads (file:line cited). Implementation is the source of truth; no behavior was inferred.*
