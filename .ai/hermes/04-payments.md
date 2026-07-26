# Shopixy Payments — Senior QA Onboarding Library (04)

Scope: Durable payment business rules for QA of the PointOfSale / Shopixy payment platform.
This is the authoritative onboarding reference for financial-correctness QA. It is assembled from the
`.ai-context` authority files and `docs/architecture` certification/freeze documents. It intentionally
contains **only durable business rules**, not temporary implementation details.

Authority precedence used while writing this document (and the rule you must use when the docs disagree):
`.ai-context/*` (repository-wide SQL/tenant/lifecycle truth) > `PAYMENT_REFERENCE_FREEZE.md` (cross-provider
freeze) > canonical Payment Runtime / Domain certifications (`PAYMENT_0_7`, `PAYMENT_0_8`, `PAYMENT_RUNTIME_FINAL_CERTIFICATION`)
> provider-specific certifications > code comments. Where two documents disagree, the conflict is called out
explicitly under "Conflicts" inside the relevant section.

---

# Purpose

Establish a single QA source of truth for how payments must behave so financial state can never be silently
corrupted by: stale projections, provider callbacks, frontend state, duplicate delivery, out-of-order events,
cross-tenant leakage, or premature certainty about external money.

QA's job in payments is not to verify that a happy path "works." It is to prove the system **fails closed** when
money truth is uncertain, ambiguous, mismatched, or late — and that no replay, duplicate, or projection can ever
create, resurrect, or double-count money.

---

# Business Goals

- PostgreSQL is the single system of record for payment, refund, settlement, and accounting truth.
- Every payment mutation crosses an authorized lifecycle boundary (`IPaymentLifecycleService`) and the SQL commit boundary.
- Provider external execution is accepted only as **evidence**, never as local finality.
- External money uncertainty is modeled explicitly (blocked / processing / unknown-outcome / reconciliation-required / suspense), never guessed.
- Integrity is preferred over availability when backlog scope or external money truth is uncertain.
- Idempotency, duplicate collapse, and reconciliation keep the system financially safe under replay, retries, and chaos.
- No cross-tenant leakage: tenant scope is derived from trusted backend resolution, never from client/provider claims.

---

# Payment Authority

Payment is provider-neutral. Stripe, Paymob, Fawry, Manual Wallet, Vodafone Cash, and COD are **provider
implementations / workflow modes** that contribute evidence and capabilities. They are not the platform truth.

- The platform payment aggregate is the **Payment Session** (runtime name `PaymentIntent`). It owns tenant, order
  reference, amount, currency, provider, method, expiry, idempotency, handoff evidence, status, and audit lineage.
- Identity, tenant, payable reference, original amount, original currency, and initial order binding are **immutable after admission**.
- `IPaymentLifecycleService` is the **single durable writer** of payment and order-payment lifecycle state.
  Provider adapters, runtime bridges, webhook adapters, controllers, background jobs, projections, and frontend code are **never** lifecycle writers.
- The canonical Payment Runtime is pure and in-memory: it performs admission → verification → decision → state
  transition → transaction → settlement → runtime result, deterministically, for identical immutable input. It performs no persistence, no provider execution, no policy, no accounting, no money movement.
- The Runtime is **architecture-frozen**. No provider may add a provider status, credential, SDK type, wire contract,
  transport rule, persistence concern, or business rule to the Runtime. Providers adapt to the Runtime; the Runtime never adapts to a provider.

**Conflicts / boundary notes:**
- The frozen Payment Runtime is **certified but not yet activated** against production payment flows. The disabled
  `IPaymentRuntime` shell and feature flags remain disabled. This document captures the *authoritative model*; live
  provider runtime behavior still routes through the existing SQL-backed lifecycle services.
- `PAYMENT_REFERENCE_FREEZE` certifies **Stripe** and **Paymob** only as *Reference Providers* for **POS Admin + Storefront**.
  **Marketplace / CommerceHub is explicitly excluded and uncertified** by the reference status. Treat any
  CommerceHub/Marketplace payment path as a separate, lower-assurance surface and QA it for the additional
  unresolved-webhook / suspense / payout-readiness rules below.

---

# Payment Lifecycle

Canonical provider-neutral states (every provider maps into this vocabulary; provider statuses are evidence only):

`Draft, Pending, ActionRequired, AwaitingConfirmation, Authorized, Confirmed, Captured, Collected, Settled,
PartiallyRefunded, Refunded, Cancelled, Expired, Failed, Rejected, Quarantined, ReconciliationRequired`.

Terminal (obligation closed): `Cancelled, Expired, Failed, Rejected, Refunded`.
Terminal for positive value but still open to refund/adjustment/reconciliation: `Settled`.
Non-terminal: everything else (`Quarantined` and `ReconciliationRequired` are intentionally non-terminal and recoverable).

Forbidden transitions (unless expressed as a new Refund/Adjustment/Reversal/Recovery/Reconciliation decision):
- `Failed/Rejected/Cancelled/Expired/Refunded` → any success/confirm/capture/collect/authorize/settle state.
- `Settled` → `Pending/ActionRequired/AwaitingConfirmation/Authorized/Confirmed/Captured/Collected`.
- `Captured/Confirmed/Collected/Settled` → `Cancelled` or `Expired`.
- `Authorized` → `Confirmed` without capture/sale/collection evidence.
- `Pending/ActionRequired` → `Settled` without confirmed/captured/collected value.
- Any state → a provider-specific state.
- Any state → payment success from frontend SDK result, redirect result, browser state, cache, projection, or raw provider payload alone.
- Any state → finality from delivery evidence alone unless explicitly admitted as collection evidence by Payment.
- Any state → amount/currency-changing state without an immutable adjustment/reversal/refund transaction.

Retry is allowed only while non-terminal or explicitly recoverable. `Expired`/`Failed`/`Rejected`/`Cancelled` sessions
may only spawn a **new** Payment Session (or a certified recovery reclassification), never a second finality on the old one.

COD rule: delivery and cash collection are **separate facts**. Delivery alone must not silently become collection.
COD collection evidence must be admitted by Payment before financial recognition.

---

# Provider Responsibilities

Providers own only their own external side effects: sessions, references, redirects, QR, wallet approvals,
transaction ids, webhooks, status records, refunds, captures, voids, settlement reports. They do **not** own platform
payment state. Provider data enters as evidence only.

- Provider adapters are protocol/translation boundaries. They must NOT resolve/select/decrypt credentials, write SQL,
  own lifecycle progression, evaluate eligibility/policy, or treat provider acceptance/redirect/SDK success as finality.
- Webhook/Callback Evidence Adapters authenticate and translate inbound evidence only. They must NOT create decisions,
  transitions, transactions, settlements, or lifecycle mutations, and must never make an unknown event executable by guessing a canonical status.
- Advanced operations (capture, refund, partial refund, void/cancel, retrieve) use a provider-specific adapter that is
  **translation-only** until separately certified as an application command that proves tenant/actor authority, provider
  capability, current SQL payment/refund authority, idempotency, durable pre-provider checkpoint, exact Runtime outcome
  admission, and mutation only through the single lifecycle writer.
- Tenant configuration and credentials are **application authority**, resolved per operation before Runtime/provider/
  signature execution. Missing, deleted, disabled, malformed, cross-tenant, environment-mismatched, or capability-ineligible
  configuration **fails closed**. Global credentials may not be used as a silent tenant fallback.
- Each provider crossing the external boundary must persist a durable local authority row, idempotency identity, routing
  identity, and minimum reconciliation metadata **before** the external mutation. If provider acceptance is indeterminate,
  retry is blocked and the session enters `Quarantined` or `ReconciliationRequired`; it must not recreate a second external session.

Reference-provider status (authoritative from `PAYMENT_REFERENCE_FREEZE`):
- Stripe: REFERENCE PROVIDER — CERTIFIED for POS Admin + Storefront. Marketplace/CommerceHub excluded.
- Paymob: REFERENCE PROVIDER — CERTIFIED for POS Admin + Storefront. Marketplace/CommerceHub excluded.
- Fawry / Manual Payment / Cash On Delivery: architecture-adapted workflow models; live processing requires separately
  certified adapter/signature/operational work (especially Fawry official signature + per-tenant webhook HMAC).

---

# SQL Authority

- PostgreSQL is the authoritative write model for orders, payments, inventory, accounting, refunds, vendor balances, and payouts.
- The **SQL commit boundary** is the canonical durability boundary for all of the above.
- MongoDB, Redis, Elasticsearch are derivative read/performance layers — **never** authoritative business state.
- `IPaymentLifecycleService` + `PaymentStatusSaveChangesInterceptor` guard payment state transitions. Direct SQL/bulk
  mutation of payment/order financial state bypasses these guards and is an architectural violation.
- Payment callback/settlement truth is PostgreSQL lifecycle state. Provider webhook timing, callback retry state,
  settlement jobs, queue replay, Mongo order docs, Redis cache, notifications, affiliate/accounting/export projections
  are **not** payment status authority.
- Replay/recovery/rebuild must converge from current locked SQL authority. Stale callback or payment event payloads must
  not restore collected/settled/completed visibility when current SQL says `Failed/Expired/Cancelled/PartiallyRefunded/
  Refunded/deleted` or otherwise no longer owns that visibility.
- A delayed success after terminal SQL truth is external evidence / manual-review / suspense territory, **not** payment
  resurrection. A delayed failure after successful SQL collection/settlement collapses to current SQL truth.
- Critical storefront/CommerceHub order reads may self-repair from SQL before serving, and must prefer the freshest
  matching projection when duplicates exist. A missing Mongo projection is never proof the order does not exist.

---

# Webhook Rules

- Webhook/Callback controllers are tenantless at the HTTP host layer when sent to the shared API hostname. They must be
  marked `[AllowAnonymousTenant]` so generic tenant middleware does not reject the callback before the webhook trust pipeline runs.
- Ingress order is fixed: bounded raw request ingress → server-authoritative tenant resolution → current tenant config +
  webhook capability admission → bounded body/rate controls → official signature/HMAC verification → provider event
  classification → durable replay lookup/reservation → application Outcome Admission → Webhook Evidence Adapter →
  PaymentRuntimeOrchestrator → exact required Runtime outcome validation → `PaymentLifecycleService` → webhook audit +
  replay completion + SQL/outbox commit.
- Signature/HMAC verification is mandatory and provider-specific:
  - Stripe: official SDK `EventUtility.ConstructEvent` over raw payload + `Stripe-Signature` header; invalid/missing/expired timestamps fail closed.
  - Paymob: HMAC-SHA512 over the official transaction field concatenation; invalid/missing/malformed HMAC rejected.
  - Fawry: SHA-256 `messageSignature` over the official Server Notification V2 fields (fawryRefNumber, merchantRefNumber,
    paymentAmount, orderAmount, orderStatus, paymentMethod, paymentReferenceNumber, secureKey), fixed-time comparison; secure key is explicit input, never looked up by the adapter.
- Unknown, malformed, unauthenticated, unsupported, disputed, stale, duplicate, or non-executable events do **not** acquire
  lifecycle authority. An authenticated provider-negative outcome is still not a positive financial decision; it may be
  routed to the single lifecycle writer as negative evidence, never reinterpreted as success.
- Webhook adapters admit evidence only. They must never mutate lifecycle, persist replay/duplicate records, or decide final payment state.
- Replay/duplicate collapse: stable provider+provider-event identity, DB uniqueness for the durable replay fence,
  deterministic canonical evidence identity/hash, and terminal-state collapse. Duplicate/late webhooks collapse to current
  SQL authority (no second transition, transaction, settlement, refund, capture, or provider session).
- Unmatched inbound CommerceHub provider webhooks that cannot be linked to a durable local payment row must still persist
  unresolved external-money evidence into SQL (`CommerceHubUnresolvedPaymentWebhook`) **before** the request is allowed to
  fail retryable — unresolved cash truth must have a durable reconciliation anchor.

---

# Payment Evidence Rules

- Evidence is any external/manual/provider/shipping/driver/admin fact: webhook, callback, status pull, hosted return,
  frontend SDK result, receipt, proof upload, reviewer decision, carrier/driver collection evidence, refund response,
  settlement report. **Evidence is never Payment by itself.** It becomes payment-relevant only after verification + lifecycle admission.
- Frontend SDK result, iframe return, redirect return, QR, receipt, local/browser state, cache, projection, and raw
  provider payload replay are **presentation evidence only**.
- `ProviderEvidence` carries deterministic identity, provider reference, correlation, tenant/payment/action lineage, type,
  outcome, amount/currency where relevant, occurred-at/received-at, idempotency/duplicate key, payload hash/reference, and actor identity.
- Verification (backend) authenticates, normalizes, deduplicates, orders, scope-checks, and amount/currency-validates
  evidence before it can influence lifecycle. Amount/currency mismatches **fail closed** to `Quarantined` or `Failed`.
- Manual proof upload (Manual Wallet / Vodafone Cash / bank transfer / cash deposit) is evidence only until backend
  review and lifecycle admission. Admin approval/rejection is backend-bound intent; Payment owns the final decision.
- COD collection evidence (ShipmentDelivered / driver cash collected) is shipping evidence until Payment admits it as
  collection. Delivery ≠ collection.
- Late/ contradictory external-money evidence is preserved and routed to unresolved/suspense/manual reconciliation per
  certified policy. It must not fabricate local success, discard external-money evidence, or reopen a duplicate capture lane.

---

# Reconciliation Rules

- Reconciliation is **derivative**. It owns mismatch detection, mismatch/exception lineage, recovery/compensation
  orchestration, and reconciliation history. It does **not** own cash, inflows, outflows, settlements, refunds, payouts, accounting, or analytics.
- Reconciliation derives from authoritative SQL financial truth. Provider evidence cannot create money or override SQL.
  Bank evidence cannot create settlement or override ledger truth.
- Mismatch resolution cannot mutate money directly. Reconciliation records findings, invalidates derivative aggregate
  rows (dashboard acceleration only — never financial authority), alerts operators, or queues certified recovery work.
- Recovery re-enters certified SQL authority and must fail closed. Stale `Processing` payouts are quarantined into
  `ReconciliationRequired` under a serializable SQL transaction and audit lineage. Storefront unresolved-payment recovery
  re-enters `IPaymentLifecycleService`; CommerceHub suspense recovery re-enters accounting outbox only after current SQL
  paid-payment/order/tenant/amount proof.
- CommerceHub unresolved-payment recovery matches **only** current SQL paid payments with provider identity and **exact
  amount/currency** before suspense-clearance accounting is queued. When a suspense-routed webhook is later matched to a
  paid local payment row, suspense clearance + marketplace hold release commit in the **same** local authority transaction.
- Historical reconciliation reports are append-only; resolved mismatches cannot resurrect into money mutation. Stale
  candidates must reload current SQL before any recovery action; current terminal/mismatched/missing/uncertain state
  collapses to no-op, manual review, quarantine, or reconciliation-required.
- **Reconciliation currency authority (CommerceHub): the only authoritative reconciliation currency is `EGP`.**
  Reconciliation cannot accept USD/EUR/SAR or any non-EGP group as healthy; non-EGP ledger reference groups are counted as
  reconciliation anomalies. Recovery/replay/repair/rebuild/ledger/projection/cache all must prove EGP or fail closed.
  POS/storefront/tenant/vendor currency settings do **not** participate in CommerceHub reconciliation currency authority.
- **Conflict documentation:** storefront/tenant currency authority (`Tenant.Currency`, bootstrap `EGP`) is a *different*
  surface from CommerceHub reconciliation currency (`EGP` via `FinancialCurrencyGuard.PlatformBaseCurrencyCode`). Never
  assume a tenant's configured currency makes a non-EGP CommerceHub reconciliation legitimate. Super Admin payout/settlement
  surfaces must be **currency-scoped**: mixed-currency queues fail closed into per-currency summaries; a single combined
  payout total across currencies must never be fabricated.

---

# Refund Rules

- Refund creation and completion are PostgreSQL-authoritative for internal order, payment, vendor earning, platform cash,
  and ledger effects.
- Provider/manual external refund execution is outside the SQL transaction boundary and modeled as execution evidence,
  unknown outcome, or `ReconciliationRequired` until local financial recognition is durably completed.
- A refund requires state `Confirmed, Captured, Collected, Settled,` or `PartiallyRefunded`. Amount cannot exceed refundable balance.
  Partial refund → `PartiallyRefunded`; full refund → `Refunded`.
- `CommerceHubRefundStatus.ReconciliationRequired` is the explicit refund state for external refund execution that may
  already have happened while local financial recognition is incomplete.
- Manual refund payout confirmation is external-money evidence. Once an operator confirms money was sent and supplies a
  transfer reference, later local persistence/ledger/projection failure must **not** convert that refund to a terminal safe-failed state.
- A failed local write after confirmed manual refund transfer is **not** proof of external non-execution.
- `Failed` is safe only when external non-execution is known/proven or when external handoff never occurred.
- `Processing` and `ReconciliationRequired` refunds are intentionally counted as active refund capacity and readiness blockers.
- Refund-induced payment-status changes are lifecycle mutations. The payment lifecycle boundary stays active until those
  changes and their ledger effects are persisted, including recovery paths that resume a previously persisted refund.
- Post-payout refund execution is outward money movement and remains blocked until CommerceHub financial ledger entries
  affecting vendor/platform have accounting coverage or explicit reconciliation.
- Refund accounting recomposes from current SQL `OrderRefund` rows before journal generation. Payload refund amount/context
  is evidence only; partial/full refund replay must not override current refund lineage or create a duplicate refund journal.
- Gateway refund execution must run the final CommerceHub readiness gate **inside** the serializable refund-claim transaction
  before moving the refund into processing and before crossing the external provider boundary.
- **Settlement membership authority (for post-settlement refunds):** current non-deleted `PaymentSettlementBatch` plus live
  SQL `PaymentSettlementOrders` lineage. `PaymentSettlementBatch.OrderCount` is recorded metadata only — not posting authority.
  A post-settlement refund must prove the refunding order still belongs to the current settlement batch lineage (header count
  matches current mappings) before bank-account refund posting is allowed.

---

# Charge Registration Rules

"Charge registration" = the creation/registration of the authoritative payment obligation (Payment Session / `PaymentIntent`)
and its amount/currency/provider/method registration before any external provider boundary is crossed.

- The backend creates or replays the Payment Session idempotently after payable admission. Storefront cannot create a payment
  session, checkout session, payment attempt, provider session, recovery token, or merchant reference.
- Backend owns payment intent identity, amount, expected amount, currency, customer/cart/order candidate, tenant scope,
  gateway handoff, status, and expiration. Frontend owns only loading, navigation, selected candidate, retry/refresh, and admitted DTO presentation.
- Authority begins only after backend tenant/session/customer/cart/order/payment-method admission, expected-pricing and SQL
  order authority, backend SQL `PaymentIntent` authority, and backend-issued checkout token / proof-session artifact.
- Financial authority cannot be altered by a provider: backend order totals, expected-pricing stale detection,
  payment-intent amount/currency, provider amount/currency validation, and amount-mismatch quarantine remain decisive.
- Storefront DTO admission must reject malformed identity/amount/status evidence, missing/malformed cart binding, missing
  payment intent identity, missing checkout token, missing/expired expiration, mixed-provider artifacts, partial artifacts,
  provider mismatch, and non-HTTPS provider URLs.
- Recovery URL/token is a lookup/access candidate only — stripped from the URL, not stored as payment authority, and cannot
  retry without current backend `canRetryPayment` authority.
- Active Storefront/CommerceHub payment recovery contexts intentionally return `null`; no payment session, token, or client
  secret is persisted, restored from history, or cross-tab replayed. Expired/completed/failed/revoked sessions do not remain
  active through frontend state.
- For Fawry, the backend deterministically generates the reference code with provider intent, amount/currency, and expiration,
  and resolves tenant from stored reference-code evidence for webhook HMAC. For Paymob, backend creates order/payment-key;
  duplicate active intents collapse through idempotency + SQL uniqueness. For Stripe, backend creates the PaymentIntent from
  SQL order amount/currency only, encrypts the client secret, and persists provider identity to SQL.

---

# Duplicate Prevention Rules

- Duplicate webhooks/callbacks/redirects/uploads/checkout/payment-intents/verifications/rebuilds all re-enter backend
  SQL/payment authority or fail closed — none can succeed twice.
- Webhook/callback collapse uses SQL webhook logs, idempotency keys, terminal-state collapse, and lifecycle idempotency.
- Refund-id and settlement-id replay collapse before any second payment-visibility transition can be staged.
- Payment runtime prior-history replay (duplicate or out-of-order evidence) fails closed and cannot create a second
  financial artifact (transaction/settlement/capture/void/provider session/external operation).
- CommerceHub unresolved-payment reconciliation: when a suspense-routed webhook is matched to a paid local payment, the
  clearance + hold release commit in the same transaction so a duplicate capture lane is never reopened.
- Recovery/duplicate workers, delayed retries, and stale reconciliation loops collapse when the locked SQL row is no longer
  in the recoverable state (CHECKOUT-CON-14).

---

# Idempotency Rules

- Checkout and payout/refund creation flows use idempotency keys for client/API retry safety.
- Webhook processing uses durable logs + duplicate detection. Inventory mutation records event inbox state and rejects conflicting replays.
- Outbox consumers and projection handlers must be idempotent. `ProcessedEvents` allows the same delivered message to be safely ignored after successful handling.
- External transfer/refund handoffs use deterministic correlation or provider idempotency keys to reduce double execution risk.
- Idempotency guarantees **safe replay, not instantaneous consistency**.
- Idempotency cleanup may remove only unbound/uncompleted claim records owned by the current claim token. Once a request is
  bound to SQL business authority, cleanup must preserve that binding and rely on replay/lifecycle compensation/reconciliation.
- Serializable retry paths that call `ChangeTracker.Clear()` must reload/reattach their owned idempotency-claim rows before
  relying on in-transaction `SaveChanges` to preserve the durable bind.
- Storefront checkout binds `OrderId`/`CompletedAtUtc` only after coordinator-owned claim rehydration or tracked-entity reuse
  has re-proven claim-token ownership inside the current EF unit of work.

---

# Accounting Integration

- The immutable double-entry ledger in PostgreSQL is the sole authoritative source for financial reporting.
- Payment lifecycle state transitions are sealed behind lifecycle services and SaveChanges interceptors; direct writes are architectural violations.
- Accounting outbox records are staged from the authoritative SQL workflow; SQL commit makes the business mutation durable;
  accounting outbox processing posts immutable ledger entries in PostgreSQL.
- Ledger truth is SQL truth, not message-delivery truth. Failed critical accounting outbox rows remain live
  recovery/readiness blockers and must not be removed by time-based cleanup before posting or explicit reconciliation.
- Payment/refund/settlement paths that change visible payment status must stage canonical payment-status fanout from the
  lifecycle owner and preserve refund-id/settlement-id replay collapse. No consumer may infer a competing status transition from payloads.
- `AccountingLedgerAuthority` governs whether replayed collection/refund/settlement evidence may generate a journal. Current
  `Orders`, `OrderRefunds`, `PaymentSettlementBatches`, existing `JournalEntries`, idempotency keys, and reconciliation state
  decide post-once / return-existing / collapse-no-op / retry-for-reconciliation.
- Stale online/wallet/COD collection replay must not recognize revenue after current SQL payment truth is
  `Failed/Expired/Rejected/AmountMismatch/pending/deleted` or not currently COD-delivered where COD delivery is required.
- Outward money movement (refunds/payouts) is gated by readiness checks that **fail closed** when critical accounting,
  compensation, refund, payout, or integration backlog is unresolved.
- Inward external-cash recognition (e.g., approved vendor repayment proof) is not outward money movement; already-received
  cash evidence must be posted into SQL ledger truth with duplicate-evidence, serializable, and financial-safety checks.
- Scope-less unresolved captured-payment or provider-refund rows are **globally blocking external-money ambiguity**. If SQL
  contains unresolved external-money truth with no durable tenant scope, CommerceHub readiness gates fail closed until
  reconciliation materializes scope or clears the ambiguity. A non-empty base `TenantId` is valid scope; explicit
  affected-store arrays are additive; an empty affected-store array must not broaden a different base tenant into a platform-wide blocker.

---

# Business Invariants

1. Providers never own platform Payment state. Provider evidence is never Payment by itself.
2. Checkout never knows provider finality and must not branch into provider-owned authority.
3. Frontend SDK/iframe/redirect/reference/QR/receipt/local state is presentation evidence only.
4. Payment owns verification before lifecycle mutation. The lifecycle service owns status transitions; direct mutation is forbidden.
5. Order owns order lifecycle, not payment finality. Shipping owns delivery/collection evidence, not payment collection state.
6. Administration submits backend-bound review intent; it does not bypass Payment lifecycle or own provider-evidence finality.
7. COD delivery and cash collection are separate facts unless a certified rule defines one admitted evidence event as both.
8. Webhooks must be authenticated, tenant-resolved, idempotent, audited, and lifecycle-admitted.
9. Amount/currency mismatches fail closed, quarantine, or reject through Payment.
10. Refund/capture/void/cancellation/settlement/recovery must re-enter Payment authority.
11. Projections, caches, search, browser storage, provider payload replay, and mutation responses are never Payment recovery authority.
12. Sensitive provider credentials and secure keys remain backend-only.
13. `Quarantined` and `ReconciliationRequired` are recoverable non-terminal states, not limbo to be silently cleared.
14. CommerceHub reconciliation currency is `EGP` only; non-EGP groups are anomalies, not healthy reconciliations.
15. A successful SQL commit is the point the system considers an internal business mutation authoritative.
16. At-least-once delivery is accepted; consumers must be idempotent. No global total order is assumed across tenants/aggregates.

---

# Never Assume

- Never assume a provider "succeeded" webhook means local payment is paid. It is evidence until lifecycle admission.
- Never assume a frontend `succeeded`/`processing`/redirect completion is financial success. Stripe.js confirmation always returns `pending` to the frontend.
- Never assume a missing Mongo projection means the order/payment does not exist.
- Never assume a cached/Redis/stale snapshot reflects current money truth.
- Never assume a `Pending` + `OnHold` CommerceHub state is retryable customer payment state — unresolved captured-payment
  evidence makes it `PAYMENT_RECONCILIATION_REQUIRED` / manual-review, not retryable.
- Never assume a tenant's configured currency legitimizes a non-EGP CommerceHub reconciliation.
- Never assume a provider-negative (declined/failed) callback is harmless — it is still not a positive decision and must be routed, not dropped.
- Never assume recovery can reconstruct primary payment truth from browser state, cache, projection docs, stale queue
  payloads, or an old callback. Recovery uses current SQL authority and the original provider identity.
- Never assume a delayed success after a terminal SQL state can "catch up" — it becomes suspense/manual-review, not resurrection.
- Never assume the frozen Payment Runtime is live. It is certified but disabled; live flows use the SQL-backed lifecycle services.
- Never assume Marketplace/CommerceHub payment is covered by the Stripe/Paymob reference-provider certification. It is explicitly excluded.
- Never assume `PaymentSettlementBatch.OrderCount` is settlement-membership authority. Current `PaymentSettlementOrders` lineage is.

---

# QA Checklist

**Authoritative lifecycle**
- [ ] Every payment status mutation goes through `IPaymentLifecycleService` + interceptor; no direct SQL/bulk mutation of payment/order financial state.
- [ ] Canonical state machine respected: no forbidden transition (e.g., `Settled` → `Pending`, terminal → success, amount change without transaction).
- [ ] `Quarantined` / `ReconciliationRequired` handled as recoverable, never silently cleared.

**Provider evidence**
- [ ] Webhook controller is `[AllowAnonymousTenant]` and performs server-authoritative tenant resolution.
- [ ] Signature/HMAC verified with official provider method (Stripe SDK / Paymob HMAC-SHA512 / Fawry SHA-256 fixed-time).
- [ ] Invalid/missing/expired signature and malformed payload fail closed.
- [ ] Webhook adapter creates no decision/transaction/settlement/lifecycle mutation.
- [ ] Provider-negative outcome routed as negative evidence, never reinterpreted as success.

**Duplicate / replay / idempotency**
- [ ] Duplicate webhook/callback/redirect/upload/checkout cannot succeed twice.
- [ ] Refund-id / settlement-id replay collapse before second visibility transition.
- [ ] Idempotency cleanup preserves bound SQL authority; never deletes business row.
- [ ] Prior-history replay cannot create a second financial artifact.

**Amount / currency**
- [ ] Amount/currency mismatch → `Quarantined`/`Failed`, never silent acceptance.
- [ ] CommerceHub reconciliation currency is `EGP` only; non-EGP groups are anomalies.
- [ ] Mixed-currency payout/settlement queue fails closed into per-currency summaries.

**Refund**
- [ ] Refund only from `Confirmed/Captured/Collected/Settled/PartiallyRefunded`; amount ≤ refundable balance.
- [ ] `Failed` refund only when external non-execution proven.
- [ ] Manual refund confirmation + transfer reference never downgraded to safe-failed by a later local write failure.
- [ ] Gateway refund runs readiness gate inside the serializable refund-claim transaction.
- [ ] Post-settlement refund proves current settlement-batch lineage membership.

**Recovery / reconciliation**
- [ ] Recovery re-enters `IPaymentLifecycleService` / accounting outbox, never direct state mutation.
- [ ] Stale `Processing` payout → `ReconciliationRequired` under serializable transaction.
- [ ] Unmatched CommerceHub webhook persists unresolved evidence into SQL before retryable failure.
- [ ] Suspense clearance + marketplace hold release commit in same transaction when matched.

**Readiness / outward money**
- [ ] Outward money (refund/payout) blocked while critical accounting/compensation/refund/payout/integration backlog unresolved.
- [ ] Scope-less unresolved captured-payment/refund row fails closed globally until scope reconciled.

**Tenancy / isolation**
- [ ] Tenant scope from trusted backend resolution, never raw `X-Tenant-Id`/client/provider claim.
- [ ] No cross-tenant leakage in payment/reconciliation/payout paths.

**Charge registration**
- [ ] Backend owns PaymentIntent identity/amount/currency/status/expiration; frontend cannot create sessions.
- [ ] Storefront DTO admission rejects mixed-provider/malformed/expired artifacts.
- [ ] Recovery token not stored as authority; no retry without backend `canRetryPayment`.

---

# Common Failure Scenarios

- **Late provider success after terminal local failure:** arrives as external evidence → suspense / manual-review /
  `ReconciliationRequired`, not payment resurrection. QA: assert no second `Collected`/`Confirmed`/`Captured` transition.
- **Late provider failure after local success:** collapses to current SQL truth; local collected/settled stays. QA: assert
  no revert of terminal success from a negative callback.
- **Duplicate webhook delivery:** collapses via SQL webhook log + idempotency + terminal-state collapse. QA: deliver same
  event id twice; assert exactly one lifecycle transition and one transaction.
- **Out-of-order evidence:** newer prior evidence present → verification failure, no downstream artifacts. QA: deliver old
  evidence after new; assert old is ignored.
- **Amount/currency mismatch:** `Quarantined` or `Failed`. QA: tamper provider amount; assert no confirmation and explicit quarantine.
- **Stale projection after SQL commit:** not a defect if recovery/reconciliation exist and SQL truth intact. QA: verify
  storefront repair path reloads SQL and does not resurrect stale completion.
- **Unmatched CommerceHub callback:** persisted as unresolved SQL evidence, not dropped. QA: send webhook with no matching
  local payment; assert `CommerceHubUnresolvedPaymentWebhook` row exists and readiness is blocked.
- **Stale `Processing` payout:** quarantined to `ReconciliationRequired` under serializable transaction. QA: assert no silent fail.
- **Non-EGP CommerceHub reconciliation group:** counted as anomaly; never reconciled as healthy. QA: inject EUR group; assert anomaly + fail closed.
- **Mixed-provider frontend artifacts:** Storefront DTO admission rejects. QA: mix Stripe + Paymob artifacts; assert rejection.
- **Expired/revoked session replay via storage/cache:** no restoration of authority. QA: replay expired session from
  LocalStorage; assert no active payment authority.

---

# High Risk Areas

- SQL authority commit succeeding while projection/outbox delivery lags or fails (accepted tradeoff; verify rebuild paths).
- Payment-provider races: late captures, duplicate callbacks, compensation/outbox ordering.
- Provider-confirmed cash arriving before normal local convergence → suspense/reconciliation (high-sensitivity).
- Refund and payout flows in explicit `ReconciliationRequired` states.
- Inventory reservation cleanup vs unresolved external payment truth (cleanup is conservative; must not release stock when provider cash authority is unresolved).
- Tenant isolation leakage via host resolution, event scope, cache keying, or projection bugs.
- Tenant co-ownership violations: a current-tenant financial/inventory row pointing at another tenant's party, warehouse,
  product, variant, unit, invoice, or account.
- Large authoritative backlog causing fail-closed readiness gates and tenant-level operational freezes.
- CommerceHub / Marketplace payment paths: **explicitly excluded** from Stripe/Paymob reference-provider certification;
  unresolved-webhook / suspense / payout-readiness rules are first-class blockers here.
- Scope-less unresolved captured-payment / provider-refund rows: globally blocking external-money ambiguity.
- Governance/operational drift around already-hardened primitives (a failing architecture guard is itself a real defect).
- CommerceHub reconciliation currency sovereignty: any path that could accept/restore non-EGP must fail closed.

---

# Source Documents

Authoritative (read first / conflicts resolved toward these):
- `.ai-context/ARCHITECTURE_TRUTH.md` — repository-wide SQL authority, lifecycle, financial flow, reconciliation, tenant rules.
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md` — hardening decisions, CHECKOUT-CON / PAYMENT-CON / ACCOUNTING-CON rules.
- `.ai-context/PROJECT_CONTEXT.md` — subsystem ownership, financial/inventory authority boundaries.
- `.ai-context/CODEX_RULES.md` — non-negotiable rules (source-of-truth, shadow authorities, reconciliation awareness, payment lineage 9I).
- `.ai-context/KNOWN_RISKS.md` — confirmed/mitigated/accepted risk vocabulary and entries.

Cross-provider freeze:
- `docs/architecture/PAYMENT_REFERENCE_FREEZE.md` — frozen architecture; Stripe/Paymob reference-provider status; Marketplace excluded.

Canonical Payment domain / runtime:
- `docs/architecture/PAYMENT_0_7_CANONICAL_PAYMENT_DOMAIN.md`
- `docs/architecture/PAYMENT_0_8_CANONICAL_RUNTIME_MODEL.md`
- `docs/architecture/PAYMENT_RUNTIME_FINAL_CERTIFICATION.md`
- `docs/architecture/PAYMENT_RUNTIME_3_ACTIONS_AND_EVIDENCE.md`
- `docs/architecture/PAYMENT_RUNTIME_4_VERIFICATION.md`
- `docs/architecture/PAYMENT_RUNTIME_8_SETTLEMENT_RUNTIME.md`

Payment policy:
- `docs/architecture/PAYMENT_1_1_POLICY_FRAMEWORK.md`
- `docs/architecture/PAYMENT_1_2_POLICY_EVALUATION_PIPELINE.md`

Authority / certification findings:
- `docs/architecture/payment-end-to-end-authority-certification-findings.md`
- `docs/architecture/payment-session-authority-certification-findings.md`
- `docs/architecture/payment-method-authority-certification-findings.md`
- `docs/architecture/stripe-payment-authority-certification-findings.md`
- `docs/architecture/paymob-payment-authority-certification-findings.md`
- `docs/architecture/manual-payment-authority-certification-findings.md`
- `docs/architecture/fawry-payment-authority-certification-findings.md`

Webhook evidence adapters:
- `docs/architecture/STRIPE_4_WEBHOOK_EVIDENCE_ADAPTER.md`
- `docs/architecture/PAYMOB_4_WEBHOOK_EVIDENCE_ADAPTER.md`
- `docs/architecture/FAWRY_4_WEBHOOK_EVIDENCE_ADAPTER.md`

Reconciliation:
- `docs/architecture/treasury-reconciliation-authority-certification-findings.md`
- `docs/architecture/reconciliation-currency-authority-certification-findings.md`
- `docs/architecture/commercehub-payout-reconciliation-certification-findings.md`

Provider certifications (POS Admin + Storefront surface):
- `docs/architecture/STRIPE_FINAL_RECERTIFICATION.md`, `STRIPE_FINAL_CERTIFICATION.md`, `STRIPE_FINAL_CLOSURE.md`
- `docs/architecture/PAYMOB_FINAL_RECERTIFICATION.md`, `PAYMOB_FINAL.md`, `PAYMOB_CERTIFICATION_CLOSURE_*.md`
- `docs/architecture/FAWRY_FINAL_RECERTIFICATION.md`, `FAWRY_FINAL_UPDATED_RECERTIFICATION.md`, `FAWRY_CERTIFICATION_CLOSURE_*.md`

Note: This document is a durable QA onboarding reference. Update it only when a certified architecture/freeze document
changes an authority boundary; do not add temporary implementation details or code-line citations as if they were truth.
