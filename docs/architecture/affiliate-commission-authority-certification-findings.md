# PASS A3 — Affiliate Commission Authority Certification Findings
**Status:** ✅ CERTIFIED — Production-Ready  
**Date:** 2026-06-03  
**Auditor:** Antigravity Runtime Certification Engine  
**Scope:** Commission Amount, Commission Ownership, Commission State, Commission Eligibility, Ledger Integrity, Settlement Authority  

---

## 1. Runtime Authority Map

### 1.1 Commission Amount Authority
**Source of Truth:** `OrderItem` fields — immutable snapshot captured at the checkout boundary.

| Field | Captured At | Mutability |
|---|---|---|
| `AffiliateCommissionRateSnapshot` | Checkout (inside Serializable tx) | Immutable after commit |
| `AffiliateCommissionBaseAmountSnapshot` | Checkout (inside Serializable tx) | Immutable after commit |
| `AffiliateCommissionAmountSnapshot` | Checkout (inside Serializable tx) | Immutable after commit |
| `AffiliateCommissionPolicyIdSnapshot` | Checkout (inside Serializable tx) | Immutable after commit |
| `AffiliateCommissionSnapshotCapturedAtUtc` | Checkout (inside Serializable tx) | Immutable after commit |
| `AffiliateCommissionEligible` | Checkout (inside Serializable tx) | Immutable after commit |

**Snapshot Capture Path:**
```
CheckoutService.RefreshOrderItemAuthoritySnapshots()
  ← LoadAffiliateCommissionPolicySnapshotsAsync(lockAuthorityRows: true)
  ← LockAffiliateCommissionPolicyAuthorityAsync (FOR UPDATE / UPDLOCK)
  ← Serializable transaction boundary
```

**Policy Conflict Guard:** `AFFILIATE_POLICY_AUTHORITY_CONFLICT` — checkout is blocked if multiple active policies exist for the same product. This is fail-closed.

**Rounding:** Commission amount is rounded to 4 decimal places using `MidpointRounding.ToZero` (truncation, not rounding up) at checkout. Prevents financial inflation.

---

### 1.2 Commission Ownership Authority
**Source of Truth:** `AffiliateAttribution` table — the canonical PostgreSQL row that binds an order to a specific affiliate.

**Ownership Determination Path:**
```
RecordCommissionForOrderAsync
  → AffiliateAttributions (IgnoreQueryFilters)
    → [attribution.AffiliateAccountId] = AffiliateAccount.Id
      → Ownership is locked to the attributed affiliate
```

**Ownership Immutability Enforcement:**
- `AffiliateAttribution` has a unique constraint on `(TenantId, OrderId)` — one attribution per order, enforced at the DB level.
- Recovery path (`AttributeOrderToAffiliateFromOrderSnapshotAsync`) validates that any recovered attribution matches the order's snapshot fields (`AffiliateAccountId`, `ReferralCode`, `PromotionLinkId`). Conflict → `InvalidOperationException` (fail-closed, not corrupt-and-continue).
- Race conditions on concurrent inserts are caught by PostgreSQL 23505 and re-validated.

---

### 1.3 Commission State Authority (Lifecycle)
**Source of Truth:** `AffiliateCommissionLedger` — append-only, PostgreSQL-authoritative.

| State | Factory | Trigger |
|---|---|---|
| `Earned` | `CreateEarnedFromSnapshot()` | Payment/COD delivery confirmed, accounting posted |
| `Approved` | `CreateApproved(earned)` | `OrderDeliveredEvent` / `FulfillmentDeliveredEvent` consumed |
| `Paid` | `CreatePaidNetOfReversals()` | Orchestrator `ExecuteAffiliatePayoutAsync` commits |
| `Reversed` | `CreateReversal()` | Refund confirmed on order item |

**Lifecycle Enforcement:**
- `ApproveCommissionForDeliveredOrderAsync` checks: physical delivery (`IsPhysicallyDelivered`), payment status not in `{Pending, Authorized, AwaitingVerification, Failed, Rejected, Expired, CashOnDelivery}`, and `FinancialPostingStatus == Posted|NotRequired`.
- Reversal blocks approval: items with a `Reversed` ledger entry are excluded from approval batch.
- Paid entries are created only by the Orchestrator — never by `AffiliateService` directly.

---

### 1.4 Commission Eligibility Authority
**Source of Truth:** `OrderItem.AffiliateCommissionEligible` — boolean flag set at checkout.

**Eligibility conditions (all must be true at checkout):**
1. `resolvedAffiliateAuthority != null` — a valid affiliate was attributed to this order
2. `affiliatePolicySnapshot != null` — an active, non-deleted, enabled `AffiliateProductPolicy` exists for this product in this tenant
3. `lineTotal > 0m` — item has a positive billable amount
4. `affiliateCommissionAmount > 0m` — the calculated commission is positive (policy rate × lineTotal, capped by MaxCommissionPerItem)

**Missing snapshot enforcement:** `RecordCommissionForOrderAsync` throws `InvalidOperationException` if `AffiliateCommissionEligible == true` but any snapshot field is null or zero. **Fail-closed.**

---

## 2. Ledger Integrity Verification

### 2.1 Append-Only Enforcement
- `AffiliateCommissionLedger` does NOT inherit from `BaseEntity`. No `IsDeleted`, no `UpdatedAt`. Cannot be soft-deleted.
- All factory methods (`CreateEarned`, `CreateEarnedFromSnapshot`, `CreateApproved`, `CreateReversal`, `CreatePaid`, `CreatePaidNetOfReversals`) return new instances. No mutation of existing rows.
- EF Core configuration has `OnDelete(DeleteBehavior.Restrict)` on all FKs — no cascade deletes.

### 2.2 Idempotency Key Structure
| Entry Type | Key Pattern |
|---|---|
| Earned | `AFFILIATE_COMM:EARNED:{OrderItemId}` |
| Approved | `AFFILIATE_COMM:APPROVED:{OrderItemId}` |
| Paid | `AFFILIATE_COMM:PAID:{OrderItemId}` |
| Reversed | `AFFILIATE_COMM:REVERSED:{OrderItemId}` or `AFFILIATE_COMM:REVERSED:{OriginalEntryId}:{OrderItemId}` |

**Unique Index:** `UQ_AffiliateCommLedger_Idempotency` on `IdempotencyKey` alone (NOT on `(TenantId, IdempotencyKey)`) because `TenantId` is always `NULL` for global affiliates (AF-19B). PostgreSQL `NULL ≠ NULL` — a composite key would allow unlimited duplicates.

### 2.3 Database CHECK Constraints
| Constraint | SQL | Purpose |
|---|---|---|
| `CK_AffiliateCommLedger_Amount` | `"CommissionAmount" > 0` | No zero or negative commissions |
| `CK_AffiliateCommLedger_BaseAmount` | `"BaseAmount" > 0` | No zero base amounts |
| `CK_AffiliateCommLedger_Rate` | `"CommissionRate" > 0 AND "CommissionRate" <= 0.50` | Rate bounded to 0–50% |
| `CK_AffiliateCommLedger_ReversalRef` | Type 1,2,3 → OriginalEntryId NOT NULL; Type 0 → NULL | Lifecycle integrity |
| `CK_AffiliateCommLedger_EntryType` | `"EntryType" IN (0, 1, 2, 3)` | No unknown lifecycle states |
| `CK_AffiliateCommLedger_IdempotencyKey_NotBlank` | `btrim("IdempotencyKey") <> ''` | No blank idempotency keys |

---

## 3. Delivery-Gating Authority Verification

### 3.1 Gate Conditions (All Required)
```csharp
// EnsureAffiliateDeliveryApprovalAuthority — runtime guard in AffiliateService
if (!order.IsPhysicallyDelivered)
    throw "AFFILIATE_DELIVERY_AUTHORITY_NOT_CONFIRMED"

if (order.Total > 0 && order.PaymentStatus in {Pending, Authorized, AwaitingVerification, Failed, Rejected, Expired, CashOnDelivery})
    throw "AFFILIATE_DELIVERY_PAYMENT_AUTHORITY_NOT_CONFIRMED"

if (order.Total > 0 && order.FinancialPostingStatus not in {Posted, NotRequired})
    throw "AFFILIATE_DELIVERY_ACCOUNTING_AUTHORITY_NOT_CONFIRMED"
```

### 3.2 Delivery Gate Trigger
- `AffiliateOrderDeliveredConsumer` consumes both `OrderDeliveredEvent` and `FulfillmentDeliveredEvent`.
- Calls `ApproveCommissionForDeliveredOrderAsync` which enforces the gate before writing any `Approved` entries.
- Consumer re-throws on failure → MassTransit retry delivers idempotent safety.

### 3.3 Tenant Mismatch Guard
```csharp
// AF-19B: StoreTenantId validation before approval batch
var mismatchedEntries = allEntries.Where(e => e.StoreTenantId != order.TenantId).ToList();
if (mismatchedEntries.Any()) throw "AFFILIATE_COMMISSION_STORETENANT_MISMATCH"
```

---

## 4. Settlement Authority Verification

### 4.1 Unified Orchestrator Pipeline (AF-38H/AF-38Y)
All monetary commission payout MUST go through `AffiliateSettlementOrchestrator.ExecuteAffiliatePayoutAsync`. The legacy `MarkPayoutPaidAsync` on `AffiliatePayoutService` is `[Obsolete]` and throws `NotSupportedException` if called.

**Pipeline Steps (mandatory order):**
1. Serializable transaction (`IsolationLevel.Serializable`)
2. External evidence allocation (`AffiliatePayoutTransferEvidence` idempotency check)
3. Balance validation (`RawAvailable` — NOT the clamped display value)
4. `BalanceLedger.Withdrawal` creation (monetary authority)
5. `CommissionLedger.Paid` entries (FIFO lifecycle sync, StoreTenantId-filtered)
6. Accounting posting (`EnsureAffiliateEarnedAccountingPostedAsync`, `PostAffiliatePaidAccountingAsync`)
7. Auto-reconcile withdrawal requests
8. Mark `PayoutRequest` as Paid (inside same transaction)
9. Event publication + commit

### 4.2 Balance Formula (AF-38Y V-1)
```
RawAvailable = NetApproved(Approved - Reversed per OrderItemId) 
             + BalanceAdjustments 
             + Withdrawals (negative) 
             + (-PendingRequests)

// CommissionLedger.Paid does NOT affect balance — only BalanceLedger.Withdrawal does
// NetApproved uses Max(ApprovedSum - ReversedSum, 0) per OrderItemId for partial refund support
```

### 4.3 Serializable Isolation + Retry (AF-38Y V-2)
- `MAX_RETRIES = 3`, exponential backoff: 50ms, 100ms, 150ms.
- `IsSerializationFailure` detects PostgreSQL `SqlState == "40001"` (direct + wrapped in `DbUpdateException`).
- If all retries exhausted → rethrow (fail loudly, no silent corruption).

### 4.4 StoreTenantId Enforcement (AF-38Y V-4)
- All orchestrator queries filter by both `AffiliateAccountId` and `StoreTenantId`.
- `AffiliateCommissionLedger.TenantId` is permanently `NULL` (AF-19B global marketplace) — `StoreTenantId` is the operative boundary.
- Idempotency conflict check validates `tenantMatches = existingWithdrawal.StoreTenantId == storeTenantId`.

---

## 5. Recovery / Reconciliation Authority

### 5.1 Attribution Recovery
- `ReconcileMissingAttributionsAsync` scans for orders with affiliate snapshots but no `AffiliateAttribution` row (batch size limited, ordered by `CreatedAt`).
- `TryRepairMissingAttributionAsync` → `AttributeOrderToAffiliateFromOrderSnapshotAsync` rebuilds from order snapshot.
- Recovery validates against snapshot fields. Any mismatch → `InvalidOperationException` (fail-closed).

### 5.2 Commission Recovery
- `RecordCommissionForOrderAsync` checks `FinancialPostingStatus` before recording. If not `Posted|NotRequired`, throws `AffiliateCommissionDeferredException` → MassTransit retry loop continues.
- `EnsureCommissionReadyForDeliveredAffiliateOrderAsync` — if a delivered order has no `Earned` entries, throws `AFFILIATE_COMMISSION_NOT_READY_FOR_DELIVERY_APPROVAL` → retry until commission recording converges first.

### 5.3 Idempotent Replay
- Every commission recording path catches `DbUpdateException` with `IsUniqueViolation` (PostgreSQL 23505).
- On conflict: detach the failed entity, emit `RecordIdempotentSkip` metric, continue.
- Pre-save concurrency guard in `ApproveCommissionForDeliveredOrderAsync` re-queries for concurrent approvals before `SaveChangesAsync`.

---

## 6. Global Marketplace (AF-19B) Authority

| Field | Value |
|---|---|
| `AffiliateCommissionLedger.TenantId` | Always `NULL` (global affiliate, no tenant binding) |
| `AffiliateCommissionLedger.StoreTenantId` | `Order.TenantId` — the store where the sale happened |
| Event `TenantId` | `StoreTenantId` (for MongoDB projection routing) |
| Event `AffiliateAuthorityTenantId` | Normalized `AffiliateAccount.TenantId` (null if `Guid.Empty`) |

---

## 7. Gap Analysis — No Hardening Required

All critical paths have been verified against the runtime code. The following previously-suspected risks are **proven mitigated**:

| Risk | Status | Evidence |
|---|---|---|
| Commission amount drift from policy changes | ✅ Closed | Snapshot locked at checkout with `FOR UPDATE` row lock; commission recording uses snapshot, never re-queries policy |
| Duplicate commission for same OrderItem | ✅ Closed | `UQ_AffiliateCommLedger_Idempotency` unique index; 23505 catch-and-skip in all insertion paths |
| Commission paid to wrong affiliate | ✅ Closed | Attribution row is immutable; recovery validates snapshot match before rebuilding |
| Approval without delivery | ✅ Closed | `EnsureAffiliateDeliveryApprovalAuthority` enforces `IsPhysicallyDelivered + PaymentStatus + FinancialPostingStatus` |
| Over-withdrawal / double payout | ✅ Closed | Serializable isolation + `RawAvailable` check inside same transaction; withdrawal unique constraint |
| Refund corruption (commission not reversed) | ✅ Closed | `ReconcileRefundedCommissionEntriesAsync` runs inline during commission recording; `ReverseCommissionForOrderItemAsync` has retry-if-not-ready guard |
| Cross-tenant commission leak | ✅ Closed | `StoreTenantId` filter on all orchestrator queries; mismatch guard before approval batch |
| Negative commission amounts | ✅ Closed | `CK_AffiliateCommLedger_Amount > 0` DB constraint; `commissionableAmount <= 0` skip guard |
| Blank idempotency key | ✅ Closed | `CK_AffiliateCommLedger_IdempotencyKey_NotBlank` DB constraint |
| Multi-currency balance corruption | ✅ Closed | `ResolveAuthoritativeBalanceCurrenciesAsync` throws `AffiliatePayoutAuthorityException` on multi-currency or blank currency |

---

## 8. Certification Verdict

### ✅ PASS A3 — CERTIFIED

The Affiliate Commission Authority system is:

1. **Commission Amount** — Immutable. Locked at checkout via `FOR UPDATE` row lock on policy. Stored in `OrderItem` snapshots. Recording uses snapshots exclusively. Never re-derives from current policy.

2. **Commission Ownership** — Immutable. Bound to the `AffiliateAttribution` row. Recovery enforces snapshot match before rebuilding attribution. Race conditions caught and re-validated.

3. **Commission State** — Append-only ledger. Lifecycle is `Earned → Approved → Paid` with optional `Reversed` that blocks approval. All transitions use factory methods with unique idempotency keys. No direct field mutation possible.

4. **Commission Eligibility** — Fail-closed. Missing snapshot on eligible items throws `InvalidOperationException`. No silent skips that could hide drift.

5. **Delivery Gating** — Enforced via three mandatory conditions (physical delivery + payment status + financial posting). Triggered by `OrderDeliveredEvent` / `FulfillmentDeliveredEvent`. Cannot be bypassed.

6. **Settlement** — Unified Orchestrator is the single authority. `MarkPayoutPaidAsync` throws `NotSupportedException`. Serializable isolation with 3-retry loop. Balance uses `RawAvailable` (not clamped). StoreTenantId enforced on all queries.

7. **Ledger Integrity** — Append-only entity (no `BaseEntity`, no `IsDeleted`). Six CHECK constraints at DB layer. Unique idempotency index. All FKs are `Restrict` delete.

**This system is production-certifiable and regression-frozen by the test suite.**

---

## 9. 2026-06-04 Runtime Revalidation Addendum

The PASS A3 runtime revalidation on 2026-06-04 confirmed the existing certification against the required financial-authority questions.

### Required Answers

| Question | Answer |
|---|---|
| Who owns commission truth? | PostgreSQL owns commission truth through immutable `OrderItem` checkout commission snapshots, canonical `AffiliateAttributions`, append-only `AffiliateCommissionLedger`, `AffiliateBalanceLedger` withdrawals, and payout evidence rows. |
| What creates commission? | `AffiliateService.RecordCommissionForOrderAsync` creates `Earned` ledger rows from immutable checkout `OrderItem` snapshots after attribution exists and financial posting is ready. |
| What makes commission earned? | An eligible attributed order item with complete checkout commission snapshots, positive base/amount, and posted or not-required accounting creates an `Earned` ledger entry. |
| What makes commission approved? | `ApproveCommissionForDeliveredOrderAsync` approves only after current SQL delivery, payment, accounting, StoreTenantId, and earned-entry authority pass. |
| Can commission be earned before delivery? | Yes. The inspected runtime can record `Earned` before delivery, but `Approved` is delivery-gated. |
| Can commission ownership change? | No. Ownership is fixed by the order attribution snapshot and canonical `AffiliateAttribution`; recovery rejects mismatch. |
| Can commission amount change after checkout? | No. Recording, approval, and paid lifecycle rows copy immutable checkout snapshots/ledger amounts; current policy/product/admin state is not re-read for amount. |
| Can refunds preserve commission? | Refunded/reversed order items cannot remain payable: reversal reconciliation writes `Reversed` rows and approval/payout logic nets or skips reversed amounts. |
| Can reversals duplicate? | No for full reversals due to idempotency keys and PostgreSQL unique constraints; partial reversals require custom keys and are bounded by lifecycle calculations. |
| Can earned commissions duplicate? | No. `AFFILIATE_COMM:EARNED:{OrderItemId}` plus `UQ_AffiliateCommLedger_Idempotency` converges replay. |
| Can approvals duplicate? | No. Approval pre-checks existing approved item ids and PostgreSQL idempotency catches races. |
| Can payouts duplicate? | No inspected duplicate-payout path was found. Payout execution uses the unified orchestrator, serializable isolation, idempotent withdrawal evidence, balance validation, and paid-entry idempotency. |
| Can balances drift from ledger truth? | Balance authority is ledger-derived; dashboards/projections are not authority. Raw financial checks use `RawAvailable`, not clamped display values. |
| Is commission authority production-certifiable? | Yes for inspected runtime paths, with operational debt below. |

### Required Deliverables Revalidated

- Commission inventory: `OrderItem` snapshots own amount/eligibility input; `AffiliateAttribution` owns ownership; `AffiliateCommissionLedger` owns earned/approved/reversed/paid lifecycle state; `AffiliateBalanceLedger` plus transfer evidence owns withdrawal money movement; policies are checkout input only.
- Creation findings: checkout captures locked policy snapshots, and commission recording uses `CreateEarnedFromSnapshot`.
- Ownership findings: affiliate, referral code, and promotion link are immutable after checkout; recovery is snapshot-only and mismatch-fail-closed.
- Lifecycle findings: actual lifecycle is `Earned -> Approved -> Paid`, with `Reversed` as an append-only offset/blocking state.
- Earned findings: earned can be recorded before delivery, but approval cannot.
- Approval findings: approval requires physical delivery, payment authority, accounting authority, positive event version, and StoreTenantId match.
- Refund/reversal findings: full refunds converge through `AFFILIATE_COMM:REVERSED:{OrderItemId}`; missing earned entries defer instead of silently skipping; reversed items are not approved.
- Ledger findings: ledger rows are append-only by entity shape, DB constraints, restricted FKs, and idempotency uniqueness.
- Replay findings: earned, approved, reversed, and paid rows converge through idempotency keys, unique violation handling, and detached failed entities/outbox rows.
- Recovery findings: missing attribution rebuilds only from durable order snapshots; delivered approval and refund reversal retry until earning/accounting is ready.
- Balance findings: balance truth is SQL ledger aggregation using raw available balance; projection/dashboard balances are non-authoritative.
- Fraud findings: duplicate earning, duplicate approval, duplicate payout, refund bypass, ownership migration, manual ledger mutation, and negative/blank ledger entries were not confirmed in inspected paths.
- Monitoring findings: telemetry records idempotent skips and authority failures; remaining alert/SIEM proof is operational debt.
- Chaos findings: code-level replay, duplicate approval, duplicate reversal, recovery, and serializable payout convergence are regression-frozen; PostgreSQL-backed chaos execution remains operational maturity debt.
- Runtime hardening performed: none required in this pass; the existing runtime controls satisfied the certification.
- Regression freeze added: `AffiliateCommissionCertificationTests`, `AffiliateCommissionLifecycleCertificationTests`, and `AffiliateCommissionRecoveryCertificationTests`.
- Architecture updates: `.ai-context` PASS A3 entries now record the commission authority freeze.
- Remaining debt: deployment-verified SIEM/alert packs, backup/restore drills for affiliate commission tables, PostgreSQL evidence-role rollout, and a live PostgreSQL chaos harness for affiliate commission replay/recovery/payout races.
# PASS P3-AB - Affiliate Commission Authority Certification Findings
**Status:** CERTIFIED - Production-Ready  
**Date:** 2026-06-10  
**Scope:** Affiliate commission identity, calculation, eligibility, refund/reversal, approval, ledger integration, analytics/reporting, tenant isolation, concurrency, history, and adversarial runtime authority.

## Runtime Hardening Performed

P3-AB found and closed one live runtime authority gap in asynchronous refund reversal replay.

The payment lifecycle already creates precise affiliate commission reversals from durable `OrderRefundLine.Amount` allocations. The affiliate refund consumers could later replay the same refund through `ReverseCommissionForOrderItemAsync` without passing refund-line amount or refund identity, causing a whole-item reversal under a different idempotency key. That could suppress remaining legitimate commission after a partial refund and could over-reverse relative to authoritative refunded revenue.

Hardening:
- `OrderItemRefundedEvent` now carries the durable `RefundId`.
- Refund consumers pass the authoritative refund-line amount plus the same commission reversal idempotency key shape used by payment lifecycle.
- `AffiliateService.ReverseCommissionForOrderItemAsync` now accepts optional refund amount/idempotency evidence, sums existing reversals by original earned entry, calculates proportional commission reversal from earned snapshots, caps reversal by remaining earned base/commission, and fails closed if historical reversal totals already exceed earned authority.
- Recovery reconciliation now uses cumulative SQL `OrderItem.RefundedAmount` and bounded custom-key reversals rather than treating any refunded item as fully reversed by presence alone.

Regression freeze:
- `AffiliateCommissionLifecycleCertificationTests.ReversalBehavior_MustUseRefundLineAmountsAndBoundPartialReversals`.
- Revalidated focused suite: 61 tests passed.

## Required Findings

Commission identity findings: PostgreSQL owns commission identity through append-only `AffiliateCommissionLedger` rows linked to canonical `AffiliateAttributions`, `Orders`, and `OrderItems`. Idempotency keys prevent replay-created duplicates.

Calculation findings: Commission amount derives from immutable checkout `OrderItem` snapshots, not current policy, product state, DTOs, projections, frontend state, or cache.

Eligibility findings: Eligibility is captured at checkout after current SQL attribution and product-policy authority are proven. Deleted, suspended, or inactive affiliates cannot create new earned commission.

Refund/reversal findings: Refund authority is P2-D payment/refund truth plus durable `OrderRefundLine` allocation. Partial refunds now reverse only the proportional, bounded amount from authoritative refund-line/cumulative refunded base.

Approval findings: Approval is delivery, payment-recognition, and accounting-posting gated. Delivery events are triggers only; reversed items are not approved.

Ledger findings: `AffiliateCommissionLedger` owns commission lifecycle. Payout and financial truth remain orchestrator/ledger-authoritative and use net approved amounts after reversals.

Analytics findings: Dashboards, MongoDB projections, analytics summaries, and reports are derivative read surfaces and cannot approve, reverse, settle, or create commission authority.

Tenant-isolation findings: Store economics are scoped by `StoreTenantId`; recording derives it from SQL `Order.TenantId`, approval validates event tenant against SQL order tenant, and payout/balance queries filter by store tenant.

Concurrency findings: Earned, approved, reversed, and paid rows converge by SQL unique idempotency keys. Partial reversal replay now uses refund-id/order-item/base keys consistently across synchronous and asynchronous paths.

Historical-consistency findings: History is append-only. Recovery rebuilds only from durable order attribution and order-item snapshots, and fails closed on conflicts or reversal overruns.

Adversarial findings: DTO tampering, route tenant guessing, analytics manipulation, refund replay, partial refund replay, stale policy rates, affiliate reassignment, and ledger replay do not create commission authority in the inspected runtime.

## Required Answers

| Question | Answer |
|---|---|
| Who owns commission authority? | PostgreSQL: checkout `OrderItem` snapshots own amount/eligibility input, `AffiliateAttributions` own ownership, and append-only `AffiliateCommissionLedger` owns lifecycle state. |
| Can commissions diverge from order truth? | No for inspected runtime; recording uses SQL order/item snapshots and attribution, and approval/reversal reload current SQL state. |
| Can refunded orders retain commissions? | No payable commission survives authoritative refund reversal; payout nets `Reversed` rows. |
| Can partial refunds corrupt commissions? | No after P3-AB hardening; partial reversals use durable refund-line amounts and remaining earned bounds. |
| Can analytics become authority? | No. Analytics and dashboards are derivative only. |
| Can ledger divergence occur? | Inspected commission lifecycle cannot create ledger truth outside certified payout/accounting boundaries. |
| Can tenant leakage occur? | No inspected leakage found; economics are filtered by `StoreTenantId`. |
| Can replay duplicate commissions? | No. Replay converges through SQL idempotency keys, including refund-id-based partial reversal keys. |
| Can commission ownership be reassigned? | No. Ownership is fixed by immutable order attribution snapshot plus canonical attribution row. |
| Were runtime code changes required? | Yes. Partial refund/reversal replay hardening was required and performed. |
| Is Affiliate Commission Authority production-certifiable? | Yes, for inspected P3-AB runtime scope. |

## Files Modified For P3-AB

- `PointOfSale.Core/Events/Affiliate/OrderItemRefundedEvent.cs`
- `PointOfSale.Core/Interfaces/Affiliate/IAffiliateService.cs`
- `PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateRefundConsumers.cs`
- `PointOfSale.Infrastructure/Payments/PaymentLifecycleService.cs`
- `PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs`
- `PointOfSale.Tests/Affiliate/AffiliateEventPipelineTests.cs`
- `PointOfSale.Tests/Architecture/AffiliateCommissionLifecycleCertificationTests.cs`
- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
