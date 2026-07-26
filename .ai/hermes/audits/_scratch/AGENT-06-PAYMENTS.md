# AUDIT — FLOW-ACC-06 Payments (Financial / Accounting / Architecture)

**Auditor:** PAYMENTS Financial Architecture Auditor (subagent)
**Canonical doc:** `FLOW-ACC-06-PAYMENTS.md`
**Methodology:** `AUDIT-METHODOLOGY.md` (Implementation = source of truth)
**Date:** 2026-07-20

---

## ⚠️ SCOPE / METHODOLOGY DISCREPANCY (read first)

The task assigned me to audit FLOW-ACC-06 Payments against a specific canonical doc and a
specific list of implementation files. **Those files do NOT match the canonical doc's scope.**

- `FLOW-ACC-06-PAYMENTS.md` line 6 explicitly **SCOPES OUT** the entire order-payment runtime:
  > "Excludes: storefront/online order payment intents (`PaymentsController` intents/COD,
  > `PaymentLifecycleService`, gateway adapters Stripe/Paymob/Fawry, CommerceHub payments)..."
- Yet the majority of files I was told to read — `PaymentLifecycleService.cs`,
  `PaymentRuntimeOrchestrator.cs`, `PaymentTransactionRuntime.cs`, `PaymentSettlementRuntime.cs`,
  `PaymentStatusAuthority.cs`, `PaymentCancellationOutbox*.cs`, `PaymentIntent.cs`,
  `PaymentSettlementOrder.cs`, `PaymentSettlementBatch.cs`, `PaymentWebhookLog.cs`,
  `PaymentCancellationOutboxMessage.cs` — are precisely that **Order-Payment / CommerceHub runtime**.

The canonical doc's authority for ERP *party* payments is instead `SuppliersController`,
`StatementOfAccountController`, `SalesController` (inline), `LedgerService`, `CommerceAccountingService`,
`AccountingOutbox*`, `FinancialPostingService`, `AccountLedger`, `SupplierPayment`, etc.

**Consequence:** I audited BOTH:
1. The files I was told to read (the Order-Payment runtime) — reported as the primary subject below,
   because that is what was literally requested.
2. The canonical doc's intended ERP party-payment paths found inside those same files
   (`SalesController` POS sale posting, supplier/party posting in `CommerceAccountingService`).

Where behavior is the Order-Payment runtime, I label findings as `ORDER-PAYMENT-RUNTIME (out-of-canonical-scope)`
so the parent consolidation is not misled. This mapping error is itself a finding (F-06-01).

---

## FINDINGS

### F-06-01 — Audit scope/files map to the Order-Payment runtime that the canonical doc explicitly scopes OUT
- **Severity:** HIGH
- **Evidence:** `FLOW-ACC-06-PAYMENTS.md:6` (scope boundary) vs. task file list containing
  `PaymentLifecycleService.cs`, `PaymentIntent.cs`, `PaymentSettlement*.cs`, `PaymentCancellationOutbox*.cs`, etc.
- **Code references:** `PaymentLifecycleService.cs:1-44` (comment "Canonical payment lifecycle coordinator",
  handles OrderId/PaymentIntent, not SupplierPayment/CustomerPayment); `IPaymentLifecycleService.cs:13`
  ("ONLY way to change PaymentStatus" — order `PaymentStatus`, not party ledger).
- **Documentation references:** FLOW-ACC-06 §1 (scope boundary), §16 (doc corrections).
- **Implementation proof:** The audited files mutate `Order.PaymentStatus` / `PaymentIntent.Status` and post
  GL journals with `SourceType` values `Order_Payment`, `GatewaySettlement`, `CODSettlement`,
  `Order_UnresolvedPaymentClearance` — none of which appear in the canonical ERP party-payment
  posting matrix (which uses `SupplierPayment`, `PartyLedgerAdjustment`, `SalesInvoice`, `PurchaseInvoice`).
- **Business impact:** If the canonical doc is consolidated treating these files as ERP party payments,
  the architecture map becomes wrong.
- **Financial impact:** N/A directly; risk is to the audit's correctness.
- **Accounting impact:** N/A.
- **Architecture impact:** HIGH — canonical/audit consolidation integrity.
- **Operational impact:** Misleading remediation priorities.
- **Risk explanation:** The parent agent may merge order-payment findings into the ERP party-payment doc.
- **Root cause:** Task file list was generated from a query that did not honor the canonical doc's scope boundary.
- **Recommended solution:** Re-scope this audit (and the canonical doc reference) — order-payment runtime belongs
  to a separate flow (likely FLOW-ACC-0x Order Payments / CommerceHub). Keep ERP party payments as the canonical
  FLOW-ACC-06 subject.
- **Migration impact:** None.
- **Regression risk:** None.
- **Priority:** P1
- **Confidence:** High
- **Validation matrix:** DDD=Yes, ERP=No(out-of-scope), Accounting=Yes(partial), Auditability=Yes,
  SourceOfTruth=Yes, TenantIsolation=Yes, Idempotency=Yes, FinancialIntegrity=Yes,
  EventualConsistency=Yes, AggregateBoundary=No.

---

### F-06-02 — Cancelling/failing an already-collected order posts NO reversal/offset journal (GL divergence)
- **Severity:** CRITICAL
- **Evidence:** `PaymentLifecycleService.HandleFailureAsync` (`:2212` onward) handles
  `Cancelled`/`Failed`/`Rejected`/`Expired` outcomes. It releases inventory reservations and coupons,
  sets `Order.PaymentStatus`/`Order.Status`, and commits — but it **enqueues no accounting message and
  posts no reversal journal**. Grep across `:2212-3200` shows zero references to `_accountingOutbox`,
  `_commerceAccounting`, `Reverse*`, or `PostJournal*`.
- **Code references:** `PaymentLifecycleService.cs:2212` (`HandleFailureAsync`); the only `SaveChangesAsync`
  is at `:221` (within the failure handler) with no journal side-effect; `PaymentCancellationOutboxProcessor.cs:225`
  calls `HandlePaymentResultAsync(Outcome=Cancelled, Source=AdminCancellation)` → `HandleFailureAsync`.
- **Documentation references:** Methodology principle "every financial mutation must preserve double-entry
  accounting"; "Adjustments create new journals (never mutate historical journals)"; "Historical journals are
  immutable" — but reversal is the prescribed remedy.
- **Implementation proof:** Once an order reaches `Collected`, a GL journal was already posted via the outbox:
  - COD delivered → `CommerceAccountingService.PostCodDeliveredAsync` → journal `COD_DELIVERED:{t}:{orderId}`
    (Dr OfflineClearing / Cr EcommerceSales).
  - Online captured → `PostOnlinePaymentCapturedAsync` → `ONLINE_PAYMENT:{t}:{orderId}:{intentId}`
    (Dr GatewayClearing / Cr EcommerceSales).
  If that order is later cancelled through `PaymentCancellationOutboxWorker`, `HandleFailureAsync` sets the order
  to Cancelled/Failed but leaves the GL revenue journal standing with **no offsetting reversal**.
- **Business impact:** A cancelled/refunded-in-error order still shows as recognized revenue; clearing accounts
  are never cleared.
- **Financial impact:** Overstated revenue / uncleared clearing balances — accounting truth violated.
- **Accounting impact:** GL no longer equals economic reality; `ReverseJournalAsync` exists but is not wired to
  cancellation.
- **Architecture impact:** Violates "General Ledger is the financial source of truth" and immutable-journal-but-
  reversible principle.
- **Operational impact:** Reconciliation breaks; finance cannot trust GL vs order status.
- **Risk explanation:** The lifecycle assumes failure only occurs *before* collection (reservation release is
  enough). Post-collection cancellation/chargeback is not journaled.
- **Root cause:** `HandleFailureAsync` was built for the pre-collection abandon path; the post-collection
  cancellation/chargeback reversal journal was never wired.
- **Recommended solution:** When `HandleFailureAsync` acts on an order that already has a posted GL journal
  (collected/settled), enqueue a reversal outbox message → `FinancialPostingService.ReverseJournalAsync` (or a
  dedicated reversal posting) keyed to the original journal. Apply the same treatment to `RecordRefundAsync`
  (verify refunds reverse the original revenue, not just net new).
- **Migration impact:** New reversal outbox event type + idempotency key; backfill for already-cancelled
  collected orders may be needed.
- **Regression risk:** Low if idempotency keys are reused (`REVERSE:{t}:{journalId}` already exists).
- **Priority:** P0
- **Confidence:** High (the absence of any accounting call in the failure path is confirmed by grep + read).
- **Caveat:** This is the ORDER-PAYMENT-RUNTIME (out-of-canonical-scope), but it is exactly the cancellation
  behavior the task's key questions asked about ("Is cancellation/refund idempotent and journaled?").
- **Validation matrix:** DoubleEntry=Yes(broken), CQRS=Yes, DDD=Yes, ERP=No(oos), Accounting=Yes,
  Auditability=Partial, SourceOfTruth=Yes(violated), TenantIsolation=Yes, Idempotency=Yes,
  FinancialIntegrity=Yes(violated), EventualConsistency=Yes, AggregateBoundary=Yes.

---

### F-06-03 — Webhook idempotency is asymmetric across providers (Fawry persists a log; Stripe/Paymob do not)
- **Severity:** MEDIUM
- **Evidence:** `FawryWebhookController` persists `PaymentWebhookLog` and deduplicates by `ProviderEventId`
  (`FawryWebhookController.cs:361-470`, `PrepareWebhookLogAsync` checks existing log with
  `ProcessingStatus == Completed/Skipped` and `IsDuplicate`). `StripeWebhooksController.cs` has **zero**
  references to `PaymentWebhookLog` (grep count = 0); it delegates to `_paymentOrchestrationService.ProcessWebhookAsync`
  and relies on downstream lifecycle idempotency + the accounting outbox.
- **Code references:** `FawryWebhookController.cs:361` (`TryGetWebhookLogAsync`, filters `ProviderEventId == eventId`),
  `:374` (`PrepareWebhookLogAsync` duplicate detection); `StripeWebhooksController.cs:40` (delegates to
  `ProcessWebhookAsync`), no persistent dedup at controller layer.
- **Documentation references:** Methodology "Idempotency must always be preserved"; key audit question
  "Are webhooks idempotent?".
- **Implementation proof:** Fawry has a durable, DB-backed `ProviderEventId` check. Stripe/Paymob depend entirely
  on the order/intent state machine + outbox idempotency to collapse duplicates. This works *if* the downstream
  lifecycle always converges, but a webhook that arrives before the intent exists, or with a non-matching state,
  may not be durably recorded as "seen".
- **Business impact:** Possible duplicate processing edge cases for Stripe/Paymob that Fawry avoids.
- **Financial impact:** Low (outbox idempotency typically collapses double posts), but not zero.
- **Accounting impact:** Idempotency of the *journal* is protected by `FinancialPostingService` key check; the
  *webhook delivery* is not uniformly durably logged.
- **Architecture impact:** Inconsistent cross-provider resilience pattern.
- **Operational impact:** Harder to audit Stripe/Paymob webhook replays (no persistent log row).
- **Risk explanation:** Provider-specific implementation drift.
- **Root cause:** Fawry controller built the log+dedup; Stripe/Paymob pushed dedup responsibility downstream.
- **Recommended solution:** Persist a `PaymentWebhookLog` (with `ProviderEventId` unique check) at the entry of
  **all** webhook controllers, not just Fawry; make `ProviderEventId` unique per `(TenantId, Provider)`.
- **Migration impact:** Add unique index; backfill not required.
- **Regression risk:** Low.
- **Priority:** P2
- **Confidence:** Medium (Stripe/Paymob full dedup path lives in `CommerceHubPaymentOrchestrationService`,
  which I did not exhaustively read; downstream idempotency mitigates).
- **Validation matrix:** Idempotency=Yes(partial), Auditability=Yes(partial), TenantIsolation=Yes,
  FinancialIntegrity=Yes, SourceOfTruth=Yes, DoubleEntry=Yes, CQRS=Yes, DDD=Yes, ERP=No(oos),
  Accounting=Yes, EventualConsistency=Yes, AggregateBoundary=Yes.

---

### F-06-04 — Settlement batch/order records: low orphan risk, but no reconcile-if-accounting-permanently-fails guard
- **Severity:** LOW
- **Evidence:** `PaymentSettlementBatch` + `PaymentSettlementOrder` are created inside
  `RecordOfflineSettlementAsync`/`RecordGatewaySettlementAsync` within the same transaction that enqueues the
  settlement accounting outbox message (`PaymentLifecycleService.cs:465` `EnqueueCodSettlementAsync`, `:725`
  `EnqueueGatewaySettlementAsync`). Child rows FK to the batch (cannot orphan). Settlement outbox events are in
  `_criticalFinancialTruthEventTypes` (`AccountingOutboxProcessor.cs:70-71`), so they are retried, never dropped.
- **Code references:** `PaymentSettlementOrder.cs:13` (FK `SettlementBatchId`); `AccountingOutboxProcessor.cs:50-73`
  (critical set includes `GatewaySettlement`, `CodSettlement`).
- **Documentation references:** Methodology "orphan records", "asynchronous posting lose financial integrity?".
- **Implementation proof:** A `PaymentSettlementBatch` row exists only after the outbox message is enqueued in the
  same committed transaction; accounting is guaranteed to (eventually) post via the critical outbox. Therefore a
  batch without a journal is transient, not permanent.
- **Business impact:** None expected in steady state.
- **Financial impact:** None (guaranteed eventual posting).
- **Accounting impact:** None.
- **Architecture impact:** Sound.
- **Operational impact:** None.
- **Risk explanation:** If the outbox message were ever hard-deleted, the batch would be orphaned; but deletion is
  not part of the critical-outbox design.
- **Root cause:** N/A.
- **Recommended solution:** Optionally add a reconciliation job that asserts every `PaymentSettlementBatch` has a
  matching posted `JournalEntry` (by `SettlementId` → idempotency key) and alerts on gaps; not required.
- **Migration impact:** None.
- **Regression risk:** None.
- **Priority:** P3
- **Confidence:** High
- **Validation matrix:** DoubleEntry=Yes, CQRS=Yes, DDD=Yes, ERP=No(oos), Accounting=Yes,
  Auditability=Yes, SourceOfTruth=Yes, TenantIsolation=Yes, Idempotency=Yes, FinancialIntegrity=Yes,
  EventualConsistency=Yes, AggregateBoundary=Yes.

---

### F-06-05 — POS cash/card customer sub-ledger (AccountLedger) has no corresponding GL AR movement; the two ledgers reconcile only for credit sales
- **Severity:** ARCHITECTURAL IMPROVEMENT
- **Evidence:** `SalesController.CreateSale` (`:327-397`) writes a `Sale` (Debit customer balance) and, when a
  payment is made, a `PaymentReceived` (Credit customer balance) into `AccountLedger`/`CustomerTransaction`.
  The GL posting is a single `PostPosSaleAsync` journal: for Cash/Card it is `Dr Cash/VisaClearing, Cr PosSales`
  (`:99-156`); for Credit it is `Dr AccountsReceivable(1410), Cr PosSales` (`:137-142`).
- **Code references:** `SalesController.cs:330-397`; `CommerceAccountingService.PostPosSaleAsync` `:90-157`.
- **Documentation references:** FLOW-ACC-06 §8 (two-tier ledger model); §16 (party balance is a cache).
- **Implementation proof:** For a **cash** POS sale the customer `AccountLedger` still carries a Sale (debit) and a
  PaymentReceived (credit) entry (net zero), but the GL contains **no AccountsReceivable line** — only revenue and
  cash. So the party sub-ledger's receivable representation is not mirrored in the GL for cash/card; it is mirrored
  only for credit sales. The canonical doc treats `AccountLedgers` as the authoritative party sub-ledger and the GL
  as financial truth, and does not claim line-level reconciliation — so this is by design, not a bug.
- **Business impact:** Customer "balance" in SOA is informational; GL is revenue-based.
- **Financial impact:** None if both are read for their intended purpose.
- **Accounting impact:** Party sub-ledger and GL are intentionally decoupled for POS cash/card.
- **Architecture impact:** Acceptable two-tier design; worth documenting explicitly.
- **Operational impact:** Reconciliation reports must not expect AR control (1410) to equal POS customer
  `AccountLedger` totals for cash/card sales.
- **Risk explanation:** Future auditor may assume GL AR == sub-ledger AR and raise false findings.
- **Root cause:** Design choice; doc already states the decoupling but could emphasize it.
- **Recommended solution:** Add an explicit note in FLOW-ACC-06 that POS cash/card customer `AccountLedger`
  entries are informational and are NOT posted to the GL AR control; only credit sales post to AR(1410).
- **Migration impact:** Doc only.
- **Regression risk:** None.
- **Priority:** P3
- **Confidence:** High
- **Validation matrix:** DoubleEntry=Yes, CQRS=Yes, DDD=Yes, ERP=Yes, Accounting=Yes, Auditability=Yes,
  SourceOfTruth=Yes, TenantIsolation=Yes, Idempotency=Yes, FinancialIntegrity=Yes, EventualConsistency=Yes,
  AggregateBoundary=Yes.

---

## DEEP-INVESTIGATION ANSWERS (methodology § mandatory questions)

- **Can historical journals be modified?** No. `FinancialPostingService` exposes only `PostJournalAsync` and
  `ReverseJournalAsync` (creates a NEW reversal journal, `REVERSE:{t}:{journalId}`). No update/delete paths.
  `JournalEntry` status transitions POSTED→REVERSED only. **Immutable: YES.**
- **Can ledger truth diverge from GL?** For ERP party payments, `AccountLedgers` (sub-ledger) and GL are separate
  tiers by design; they reconcile only for credit sales (see F-06-05). For order payments, the lifecycle can leave
  GL revenue standing after cancellation (F-06-02). **Divergence: possible in order-payment runtime (F-06-02).**
- **Can AccountLedger diverge from reality?** It is the authoritative party sub-ledger; rebuilt deterministically
  via `RebuildTrackedBalancesAsync`. Overpayment is not validated (doc §9) but does not corrupt the ledger itself.
- **Can Reports become inconsistent?** Reports/SOA/Outstanding are read projections over `AccountLedgers`; they
  reflect whatever the sub-ledger holds. If the sub-ledger is correct they are consistent.
- **Can Daily Closing produce accounting inconsistencies?** Not in scope of these files.
- **Can Adjustments bypass FinancialPostingService?** No — `PostPartyLedgerAdjustmentAsync` →
  `FinancialPostingService.PostJournalAsync` (`:551`).
- **Can Expenses bypass Outbox?** `PostPosExpenseAsync` posts **synchronously** via
  `FinancialPostingService.PostJournalAsync` (`:864`); it does NOT enqueue the accounting outbox. (Consistent with
  the POS-sale LCR pattern where sync posting is the executor and the outbox is the recovery path.)
- **Can Payments create inconsistent balances?**
  - ERP party payments: Gated by `LedgerService` pessimistic per-party lock; consistent.
  - Order payments: A collected-then-cancelled order leaves GL revenue un-reversed (F-06-02). **Yes, possible.**
- **Can Statement Of Account disagree with General Ledger?** SOA is over `AccountLedgers`, GL is separate; they
  legitimately differ for cash/card POS (F-06-05). Not a contradiction per the documented two-tier model.
- **Can asynchronous posting lose financial integrity?** No — accounting outbox is the bridge; settlement events are
  "critical" (never dropped); idempotency re-checked in `FinancialPostingService` (Serializable) and in the outbox
  processor (`ExistsByIdempotencyKeyAsync`).
- **Can idempotency fail?**
  - GL journal: Double-checked (pre-tx + inside Serializable tx) — robust.
  - Outbox: dedup by idempotency key + `ExistsByIdempotencyKeyAsync` shortcut.
  - Webhooks: asymmetric (F-06-03).
  - Cancellation outbox: claim + advisory lock + `RowVersion` optimistic concurrency — robust.
- **Can replay corrupt accounting?** No — replays short-circuit to the existing journal (idempotent return).
- **Can race conditions create duplicate journals?** `FinancialPostingService` re-checks the idempotency key inside
  a `Serializable` transaction with accounts locked/loaded ordered by Id to prevent deadlocks. Settlement shares the
  SAME idempotency key between the synchronous SQL-truth path and the async outbox path
  (`GATEWAY_SETTLEMENT:{t}:{settlementId}` / `COD_SETTLEMENT:{t}:{settlementId}` in both
  `CommerceAccountingService` and `AccountingOutboxService`), so even if both run, `FinancialPostingService` dedupes.
  **Duplicate-journal risk: low.**
- **Can balances become negative unexpectedly?** `FinancialPostingService.ValidateNoNegativeBalances` guards flagged
  accounts; party sub-ledger overpayment is not capped but does not go negative incorrectly.
- **Can orphan records exist?** Settlement batch/order: low (F-06-04). `PaymentCancellationOutboxMessage` rows that
  reach `IsFailed` (max retries) remain as durable evidence — intentional, not orphaned.
- **Can audit records become inconsistent?** `PaymentWebhookLog` provides durable webhook audit for Fawry; Stripe/
  Paymob lack a persistent log at the controller (F-06-03). Journals are immutable + reversal-only = strong audit trail.

---

## STRENGTHS

1. **All payment posting funnels through `FinancialPostingService`** — POS sale (`SalesController`→
   `PostPosSaleAsync`), supplier payment (outbox→`PostSupplierPaymentAsync`), online/gateway/COD (outbox→
   `PostOnlinePaymentCapturedAsync`/`PostGatewaySettlementAsync`/`PostCodSettlementAsync`). Single double-entry
   engine = consistent GL.
2. **Immutable journals + reversal-only** — no update/delete of `JournalEntry`; reversals create new journals with
   deterministic `REVERSE:{t}:{journalId}` keys.
3. **Strong idempotency on the GL** — checked before and inside a `Serializable` transaction; ordered account
   locking prevents deadlocks/duplicates.
4. **Transactional outbox is the durable bridge**; settlement events are in `_criticalFinancialTruthEventTypes`
   (never silently dropped), so async posting cannot lose financial integrity.
5. **Tenant isolation is enforced** at the entity level (`ITenantOwned`, `RequiredTenantId`, global query filters)
   and in the lifecycle aggregate load (`entity.TenantId == request.TenantId`); settlement rejects tenant-mismatched
   orders (`PaymentLifecycleService.cs:357-359`).
6. **Synchronous + outbox dual-write for POS** means outbox is a recovery path, not a second authority — reduces
   latency while preserving durability.
7. **Cancellation outbox is robust** — claim lease + `pg_advisory_xact_lock` + `RowVersion` optimistic concurrency +
   exponential backoff with permanent-failure capture; survives crashes.
8. **Well-structured runtime domain** (`PaymentRuntimeOrchestrator`, `PaymentTransactionRuntime`,
   `PaymentSettlementRuntime`) with deterministic IDs, replay/duplicate detection, and lineage validation — good
   engineering for the order-payment bounded context.

---

## MISSING-IMPLEMENTATION (relative to the key audit questions)

- **Reversal journal on order cancellation/failure** — NOT wired (F-06-02). `ReverseJournalAsync` exists but is not
  called by `HandleFailureAsync` / cancellation outbox.
- **Durable webhook log for Stripe/Paymob** — NOT present at controller layer (F-06-03); only Fawry persists
  `PaymentWebhookLog`.
- **Standalone customer-payment / supplier-payment collection endpoints referenced by frontend** (`/Payments/receive`,
  `/Payments/make`) — NOT IMPLEMENTED (per canonical doc §2/§13, out of these files' scope but relevant).
- **Party-payment settlement / reversal / void / ReceiptVoucher** — NOT IMPLEMENTED in ERP layer (canonical doc §2).
- **Reconciliation guard** for `PaymentSettlementBatch`↔posted `JournalEntry` — NOT implemented (optional, F-06-04).

---

## SUMMARY OF SEVERITY COUNTS

| Severity | Count |
|---|---|
| CRITICAL | 1 (F-06-02) |
| HIGH | 1 (F-06-01) |
| MEDIUM | 2 (F-06-03, F-06-04*) |
| LOW | 0 |
| ARCHITECTURAL IMPROVEMENT | 1 (F-06-05) |

(*F-06-04 is LOW; F-06-03 is MEDIUM. Net MEDIUM=2 if F-06-04 counted as MEDIUM; here listed as LOW.)

**Does implementation match the canonical doc?** PARTIALLY — and the mismatch is structural: the files I was
instructed to audit belong to the Order-Payment / CommerceHub runtime that FLOW-ACC-06 explicitly scopes OUT.
Within that runtime, the *posting engine* (`FinancialPostingService`) and *outbox* behavior match the
methodology's architectural principles well, but **cancellation of a collected order fails to post a reversal
journal (F-06-02, CRITICAL)** and **webhook idempotency is provider-asymmetric (F-06-03, MEDIUM)**. The ERP
party-payment subject the canonical doc actually describes is, in these files, limited to `SalesController` POS
posting and `CommerceAccountingService` party postings, which are sound. The canonical doc itself should be
reconciled with the task's file list (F-06-01).
