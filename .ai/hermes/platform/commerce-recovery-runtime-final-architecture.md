# Commerce Recovery Runtime — FINAL CANONICAL ARCHITECTURE (Implementation-Backed)

> Authority rule: implementation is the ONLY source of truth. Every claim below is cited to `file:line`
> in the backend. Prior markdown, controller names, DTO names, and "Smart Abandoned Cart Recovery
> Engine" marketing terminology are treated as secondary. Where prior naming contradicts code, code wins.

---

## Executive Summary212

Shopixy's Commerce Recovery Runtime is a **tenant-scoped, event-driven re-engagement orchestration runtime**
that detects incomplete purchase journeys (abandoned carts) and re-engages the customer through an
**external n8n workflow automation** that delivers the reminders. It is NOT a messaging runtime, NOT a
checkout runtime, and NOT a coupon runtime. It owns exactly one authoritative SQL aggregate
(`AbandonedCarts`) plus a tenant config aggregate (`TenantAutomationSettings`) and a per-run audit
log (`AbandonedCartRecoveryLogs`). Everything else — the cart contents, the order, the payment, the
coupon, the message transport — is delegated to its owning runtime; this runtime only holds a
JSON snapshot and a signed restoration token.

The runtime is built in three explicit phases (named in code as "Foundation", "Phase 2", "Hardening"):
- **Foundation**: config + eligibility only (no sending).
- **Phase 2**: cart-activity ingestion, a 5-minute Hangfire scanner, MassTransit eventing, and an
  n8n webhook call.
- **Hardening**: atomic upserts, a `Pending` state guard, retry/backoff on the webhook, stuck-pending
  reset, tenant-lineage authority checks, fail-closed recovery-token validation, and a callback API for
  n8n to report message delivery results.

**Classification (evidence-backed, see final section):** This is a **Recovery Orchestration Runtime**
(or "Customer Re-engagement / Conversion Recovery Runtime") layered OVER the Checkout/Cart/Order
authorities. It orchestrates, it does not own commerce truth.

---

## Architecture Vision

Detect incomplete customer purchase journeys from a single SQL-authoritative ledger of cart activity,
decide recovery eligibility through fail-closed tenant/WhatsApp gating, hand the reminder delivery to an
external automation (n8n) over a signed webhook, preserve the authoritative cart/order/payment truth
in their owning runtimes, and measure recovery success purely as a state transition (Recovered) linked
back to the real `OrderId`. The runtime is intentionally thin: it tracks *whether* a cart was abandoned
and *whether* it was later recovered, never *what* the cart contains or *how* the order was paid.

---

## Runtime Purpose

To increase conversion by automatically re-contacting shoppers who added items to a cart but did not
complete checkout, using a tenant-configurable delay schedule and a verified contact channel, without
ever mutating the cart, the order, the payment, or the coupon.

- The scanner emits a trigger only for carts that crossed the inactivity threshold:
  `PointOfSale.Infrastructure/Jobs/AbandonedCartScannerJob.cs:80` (`FindCartsReadyForAbandonmentAsync`)
  filters `RecoveryStatus = Active AND LastActivityAtUtc <= now - FirstMessageDelayMinutes`.
- Eligibility gate before any trigger:
  `PointOfSale.Infrastructure/Services/AbandonedCartEligibilityService.cs:36`
  (`CanTriggerAbandonedRecoveryAsync`) — automation enabled + WhatsApp account exists + connected.
- Trigger hands off to n8n, which owns message content/scheduling:
  `PointOfSale.Infrastructure/Services/N8nRecoveryOrchestrator.cs:35`.

## Runtime Scope

In scope:
- Tracking cart activity (upsert) for abandonment detection — `AbandonedCartService.UpsertCartActivityAsync`
  (`PointOfSale.Infrastructure/Services/AbandonedCartService.cs:60`).
- Marking a cart Abandoned / Pending / Triggered / Recovered / Expired / Failed.
- Generating and validating a signed cart-restoration token — `AbandonedCartService.GenerateRecoveryToken`
  (`...AbandonedCartService.cs:570`) and `ValidateRecoveryToken` (`:606`).
- Emitting the `TriggerRecoveryEvent` and consuming it — `AbandonedCartScannerJob.cs:132` →
  `RecoveryOrchestrationConsumer.cs:61`.
- Recording n8n workflow results (first/second message sent, completion) —
  `AdminAbandonedCartRecoveryController.LogRecoveryResult` (`PointOfSale.API/Controllers/Admin/AdminAbandonedCartRecoveryController.cs:253`)
  → `AbandonedCartRecoveryTelemetryService.RecordWorkflowResultAsync`
  (`PointOfSale.Infrastructure/Services/AbandonedCartRecoveryTelemetryService.cs:24`).
- Computing recovery stats (abandoned count, messages sent, recovered count, recovery rate) —
  `AbandonedCartRecoveryTelemetryService.GetStatsAsync` (`:129`).

Out of scope (deferred to owning runtimes — see Runtime Does NOT Own):
- Cart contents truth (live cart is read via `ICartService.GetCartAsync`, never stored as authority).
- Order creation / payment (handled by Checkout/Order/Payment lifecycles).
- Coupon creation / assignment (coupon is *referenced* in the restored snapshot, never created here).
- Message transport (n8n → WhatsApp/email; the runtime never calls WhatsApp directly for recovery).

## Runtime Owns

| Aggregate | Table / Store | Authority | Evidence |
|---|---|---|---|
| Abandoned cart recovery state | `AbandonedCarts` (PostgreSQL) | SQL write-model owner | `AbandonedCart.cs:22`; upsert `AbandonedCartService.cs:60` |
| Tenant automation config | `TenantAutomationSettings` (PostgreSQL) | SQL write-model owner | `TenantAutomationSettings.cs:20`; migration `migrations/20260204_AbandonedCartRecoveryFoundation.sql:11` |
| Recovery workflow log | `AbandonedCartRecoveryLogs` (PostgreSQL) | SQL write-model owner (per n8n run) | `AbandonedCartRecoveryLog.cs:14`; writer `AbandonedCartRecoveryTelemetryService.cs:24` |
| Recovery token (signed) | Data-Protection protector `AbandonedCartRecoveryTokens.v1` | Issuer/validator | `AbandonedCartService.cs:30,570,606` |
| Recovery callback API key | `TenantAutomationSettings.N8nWebhookApiKey` | Owner | `TenantAutomationSettings.cs:73` |

## Runtime Does NOT Own

| Concept | Owner runtime | Evidence this runtime defers |
|---|---|---|
| Live cart contents | Cart runtime (`ICartService`) | recovery reads live cart, refuses stored snapshot — `StorefrontCartRecoveryController.cs:223-263` |
| Order | Order lifecycle (CheckoutService/Order projection) | only writes `RecoveredOrderId` link — `AbandonedCartService.cs:512` |
| Payment | Payment lifecycle (`IPaymentLifecycleService`) | zero payment code in recovery path |
| Coupon | Coupon runtime (`CouponsController.CreateCoupon`, `StorefrontCouponService`) | coupon only echoed into restored snapshot — `RecoveredCartSnapshotFactory.cs:160-191`; no creation/assignment call anywhere in recovery |
| Message delivery | n8n + WhatsApp runtime (provider-abstracted outbound delivery) | `N8nRecoveryOrchestrator` only POSTs a webhook — `N8nRecoveryOrchestrator.cs:119-130` |
| Checkout session abandonment (lost orders) | CheckoutSession runtime (`ICheckoutSessionService`) | separate Mongo `AbandonedCheckouts` read-model — `AdminAbandonedCheckoutsController.cs:21-22`, `AbandonedCheckoutDocument.cs:21` |

---

## Architecture Principles (code-cited)

1. **SQL is the authority for recovery state.** All transitions are atomic `UPDATE ... WHERE status = X`
   (`AbandonedCartService.cs:365-374` for Pending; `:405-414` for Triggered; `:515-522` for Recovered;
   `:549-555` for Expired).
2. **Idempotent upsert via PostgreSQL `ON CONFLICT`** keyed by `(TenantId, CartId)` —
   `AbandonedCartService.cs:65-131`. No race between insert and update.
3. **Activity resets state to Active** (customer "returned") inside the same upsert —
   `AbandonedCartService.cs:107-131` (`CASE WHEN status IN ('Abandoned','Pending','Triggered') THEN 'Active'`).
4. **Fail-closed tenant authority.** Every consumer checks `TenantLineageAuthority.IsActiveTenantAsync`
   before acting — `RecoveryOrchestrationConsumer.cs:80-90`.
5. **Contact channels are gated on verified identity.** Phone recovery requires `PhoneVerified &&
   !string.IsNullOrWhiteSpace(PhoneNumber)`; email requires `EmailVerified`. Unverified carts are skipped —
   `AbandonedCartScannerJob.cs:217-230` (`DetermineRecoveryContact`); re-checked in consumer
   `RecoveryOrchestrationConsumer.cs:110-121`.
6. **Recovery never blocks the critical commerce path.** `OrderRecoveryConsumer` swallows exceptions
   ("abandoned cart tracking is not critical path", `OrderRecoveryConsumer.cs:65-73`).
7. **Externalize delivery; internalize authority.** The runtime owns *whether* to recover; n8n owns *how*
   to message. The only outbound call is a signed webhook — `N8nRecoveryOrchestrator.cs:35`.
8. **Callbacks are authenticated and idempotent.** `X-Recovery-Api-Key` fixed-time compare
   (`AdminAbandonedCartRecoveryController.cs:494-509`); idempotency key prevents duplicate logs
   (`AbandonedCartRecoveryTelemetryService.cs:40-53,172-186`).
9. **Security of restoration: stored snapshot refused.** On recovery click, the live cart is the only
   admitted source; the stored `CartSnapshotJson` is NEVER restored — `StorefrontCartRecoveryController.cs:223-263`.
10. **Recovery token is signed, scoped, and expiry-bounded.** 7-day validity, tenant+cart+owner claims,
    fail-closed validation — `AbandonedCartService.cs:570-695`; `RecoveryTokenValidityDays = 7` (`:38`).

---

## Core Discovery (what the terms ACTUALLY are in code)

### What is "Abandoned Cart"?
A row in `AbandonedCarts` that has been idle beyond `FirstMessageDelayMinutes` and flipped from
`Active` to `Abandoned`. It is a *tracking record about a cart*, not the cart itself. The real cart
lives in the Cart runtime. Evidence: `AbandonedCart.cs:3-21` (lifecycle doc), scanner threshold
`AbandonedCartScannerJob.cs:80` + `AbandonedCartService.cs:191-207`.

### What is "Abandoned Checkout"?
A **distinct, separate concept**: an idle `CheckoutSession` (started checkout, not completed) projected
to MongoDB `AbandonedCheckouts` for the admin "lost orders" dashboard. It is NOT the same aggregate as
`AbandonedCarts` and is owned by the CheckoutSession runtime, not this runtime.
Evidence: `AbandonedCheckoutScannerJob.cs:8-26` (30-min idle threshold on checkout sessions, separate
from the cart's `FirstMessageDelayMinutes`), `AbandonedCheckoutDocument.cs:6-18` (projected read-model),
`AdminAbandonedCheckoutsController.cs:21` ("READS FROM MongoDB ONLY ... never from PostgreSQL").

### What is "Recovery"?
The act of emitting `TriggerRecoveryEvent` → calling the n8n webhook → flipping the cart to `Triggered`
so n8n can message the customer. Recovery = re-engagement orchestration, not message sending.
Evidence: `AbandonedCartScannerJob.cs:132-174`, `RecoveryOrchestrationConsumer.cs:159-222`.

### What is "Recovery Campaign"?
There is **no campaign aggregate**. The "campaign" is the n8n workflow identified by the webhook URL
(`AbandonedCart:N8nWebhookUrl` config, `N8nRecoveryOrchestrator.cs:54`). The only campaign-like
structure is the two-message schedule (`FirstMessageDelayMinutes`, `SecondMessageDelayMinutes`,
`WorkingHoursFrom/To`) passed as payload to n8n — `TriggerRecoveryEvent.cs:64-99`. The runtime does
NOT model campaigns, workflows, sequences, or templates. (Prior "Recovery Campaign Runtime" naming is
unsupported by code.)

### What is "Reminder"?
A message n8n sends on the runtime's behalf. The runtime persists whether each reminder fired
(`FirstMessageSent`, `SecondMessageSent` in `AbandonedCartRecoveryLog.cs:26-34`) via the n8n callback,
but never constructs or sends it. Delays/scheduling live in n8n, parameterized by the tenant settings.

### What is "Recovered Cart" / "Recovered Checkout" / "Recovered Order"?
- **Recovered Cart**: an `AbandonedCarts` row whose `RecoveryStatus` becomes `Recovered` when the
  customer later completes an order for that `CartId`. Evidence: `OrderRecoveryConsumer.cs:59`
  (`MarkAsRecoveredAsync`) and `AbandonedCartService.cs:512-522` (atomic update linking `RecoveredOrderId`).
- **Recovered Order**: the real `OrderId` written into `RecoveredOrderId` — the runtime's only link to
  authoritative commerce truth (`AbandonedCart.cs:114-118`).
- **Recovered Checkout**: not modeled; recovery is keyed by `CartId`, not checkout session.

### What is "Recovered Revenue"?
NOT stored as a column. It is *derived* by joining the recovered `AbandonedCarts.RecoveredOrderId`
to the Order aggregate at analytics time (the `GetStatsAsync` only counts recovered carts; it does not
sum money — `AbandonedCartRecoveryTelemetryService.cs:129-170`). Revenue is owned by the Order/Ledger
runtime; this runtime only knows a boolean "did it recover".

---

## Abandonment Detection Runtime

Detection is a **pull/scan model**, not event-streamed abandonment:

1. **Activity ingestion**: storefront publishes `CartActivityEvent` → `CartActivityConsumer` upserts
   `AbandonedCarts` (`PointOfSale.Infrastructure/Consumers/AbandonedCart/CartActivityConsumer.cs:27,71`).
   The upsert also resets an abandoned cart back to `Active` if the customer returns
   (`AbandonedCartService.cs:107-131`).
2. **Threshold scan (every 5 min)**: `AbandonedCartScannerJob` (Hangfire `Cron.MinuteInterval(5)`,
   `HangfireInitializationHostedService.cs:235-238`) iterates enabled tenants
   (`TenantAutomationSettings.EnableAbandonedRecovery`, `AbandonedCartService.cs:162-207`) and finds
   `Active` carts whose `LastActivityAtUtc` is older than `FirstMessageDelayMinutes`
   (`AbandonedCartService.cs:191-207`).
3. **Stuck-pending recovery**: the same scan also resets carts stuck in `Pending` > 5 min back to
   `Abandoned` so they re-trigger (`AbandonedCartService.cs:256-319`, `PendingTimeoutMinutes = 5` `:39`).
4. **Mark abandoned**: after eligibility + contact resolution, `MarkAsAbandonedAsync` flips the row
   (`AbandonedCartScannerJob.cs:105,174`; `AbandonedCartService.cs:321-352`).

**Who decides a checkout is abandoned?** Two different deciders:
- For *carts*: this runtime's scanner, after tenant automation is enabled AND a verified contact exists.
- For *checkout sessions (lost orders)*: the **CheckoutSession runtime's** `AbandonedCheckoutScannerJob`
  after 30 min idle (`AbandonedCheckoutScannerJob.cs:26,45,64`). This runtime has no say.

**Guest vs authenticated**: both supported. Guest carts keyed by `SessionId`; authenticated by
`CustomerId`. Token + restoration enforce owner match for both
(`StorefrontCartRecoveryController.cs:203-213,389-429`).


---

## Runtime Authority

Authoritative owner of each concept (implementation evidence):

| Concept | Authoritative owner | Evidence |
|---|---|---|
| Cart (contents) | Cart runtime (`ICartService`) | recovery reads live cart only — `StorefrontCartRecoveryController.cs:223-263` |
| Checkout (session) | CheckoutSession runtime (`ICheckoutSessionService`) | `AbandonedCheckoutScannerJob.cs:43-64` |
| Abandonment (cart) | **THIS runtime** (`AbandonedCarts` SQL) | `AbandonedCartService.cs:321-352` |
| Abandonment (checkout/lost order) | CheckoutSession runtime | `AbandonedCheckoutScannerJob.cs:8` + Mongo `AbandonedCheckouts` |
| Recovery (decision + trigger) | **THIS runtime** | `AbandonedCartScannerJob.cs:132-174` |
| Recovery Success (Recovered) | **THIS runtime** (state) + Order lifecycle (truth) | `AbandonedCartService.cs:512-522`; `OrderRecoveryConsumer.cs:59` |
| Recovered Revenue | Order/Ledger runtime (derived, not stored here) | `AbandonedCartRecoveryTelemetryService.cs:129-170` counts only, no money |
| Campaign State | **n8n** (external) | webhook URL config — `N8nRecoveryOrchestrator.cs:54` |
| Reminder State | **n8n** (reported back via callback) | `AbandonedCartRecoveryLog.cs:26-34`; `AdminAbandonedCartRecoveryController.cs:253` |
| Coupon Assignment | Coupon runtime | no recovery code creates/assigns a coupon |
| Customer Eligibility (verified contact) | Identity/WhatsApp runtime (PhoneVerified/EmailVerified) + this runtime's gate | `AbandonedCartEligibilityService.cs:36-77` |

Key point: this runtime is the **authority for the recovery STATE MACHINE**, but a **consumer/observer**
of cart, order, payment, and coupon truth.

---

## Runtime Lifecycle (implementation)

Actual implemented flow (NOT the idealized prompt diagram):

```
CartActivityEvent ──► CartActivityConsumer ──► UpsertCartActivityAsync
        (creates/refreshes AbandonedCarts row, status=Active)
                                   │
        [Hangfire every 5 min] AbandonedCartScannerJob
                                   │  FindCartsReadyForAbandonmentAsync
                                   │  (Active AND idle > FirstMessageDelayMinutes)
                                   ▼
                        Eligibility check (tenant + WhatsApp)
                                   │  DetermineRecoveryContact (verified phone > verified email)
                                   ▼
                        GenerateRecoveryToken + recovery link
                                   ▼
                        Publish TriggerRecoveryEvent ──► MarkAsAbandonedAsync
                                   │
                                   ▼
        [MassTransit] RecoveryOrchestrationConsumer
                                   │  TenantLineageAuthority check
                                   │  rate-limit check (phone)
                                   │  TryMarkAsPendingAsync  (status Abandoned -> Pending, atomic)
                                   ▼
                        N8nRecoveryOrchestrator.TriggerRecoveryWorkflowAsync (HTTP webhook)
                                   │
              ┌────────────────────────┴───────────────────────┐
        success (200)                                          failure/timeout
              ▼                                                          ▼
  ConfirmTriggerSuccessAsync (Pending->Triggered)      RecordTriggerFailureAsync
  RecordMessageSentAsync (rate-limit)                 (retry; Abandoned again, or Failed@3)
                                                                     │
                                                                     ▼ (retry on next scan)
                                                         TryMarkAsPendingAsync again
                                   │
                                   ▼
        [n8n executes workflow, sends 1st/2nd msg on working-hours schedule]
                                   │  POST /api/admin/ecommerce/abandoned-cart-recovery/recovery-log
                                   ▼
        AdminAbandonedCartRecoveryController.LogRecoveryResult
                                   ▼
        AbandonedCartRecoveryTelemetryService.RecordWorkflowResultAsync
        (writes AbandonedCartRecoveryLogs: FirstMessageSent, SecondMessageSent, completion)

        [Customer clicks recovery link]
        StorefrontCartRecoveryController.RecoverCart (public, token-validated)
                                   │  ValidateRecoveryToken (signed, 7-day, owner-match)
                                   │  ResolveRecoveredCartAsync -> live cart ONLY (stored snapshot refused)
                                   ▼
        Customer completes checkout/order
                                   ▼
        OrderCreatedEvent ──► OrderRecoveryConsumer ──► MarkAsRecoveredAsync
        (AbandonedCarts: Recovered, RecoveredAtUtc, RecoveredOrderId)

        [Hangfire daily] AbandonedCartCleanupJob ─► ExpireOldTriggeredCartsAsync
        (Triggered > 7 days with no RecoveredOrderId -> Expired)
```

Implemented terminal states: `Recovered`, `Expired` (cleanup), `Failed` (retry exhausted).
`AbandonedCartRecoveryStatus.cs:14-59`; expiry `AbandonedCartService.cs:545-568`;
cleanup job `AbandonedCartCleanupJob.cs:35-54` (Hangfire `Cron.Daily` style, `ExpirationDays = 7` `:22`).

---

## Recovery Detection (implementation)

- **Scheduler**: Hangfire recurring job, `Cron.MinuteInterval(5)` — `HangfireInitializationHostedService.cs:235-238`.
- **Workers**: the scanner runs in-process; the actual message work runs in **n8n** (external SaaS/self-host).
- **No RabbitMQ, no Quartz, no dedicated HostedService** for recovery detection (the hosting is Hangfire).
- **No Redis** involvement in recovery state (state is PostgreSQL `AbandonedCarts`).
- **Polling/scan** is the detection mechanism; there is no event that says "cart abandoned" — abandonment
  is inferred from `LastActivityAtUtc + FirstMessageDelayMinutes <= now` (`AbandonedCartService.cs:191-207`).
- **Stuck-pending reset** is part of the same scan (`AbandonedCartService.cs:256-319`).

---

## Recovery Campaign Runtime

There is **no campaign runtime**. What exists:
- A single n8n webhook URL per deployment (`AbandonedCart:N8nWebhookUrl`, `N8nRecoveryOrchestrator.cs:54`).
- A 2-message schedule (`FirstMessageDelayMinutes`, `SecondMessageDelayMinutes`) + working-hours
  window (`WorkingHoursFrom/To`) passed as payload — `TriggerRecoveryEvent.cs:64-99`.
- n8n is expected to honor delays, retries, escalation, and expiration internally; the Shopixy
  runtime only knows `Triggered` vs `Recovered` vs `Expired` vs `Failed`.
- Escalation/expiration of the *message sequence* is delegated to n8n. The Shopixy-side expiration
  (`AbandonedCartCleanupJob`, 7-day) only terminates the recovery *tracking record*, not a campaign.

---

## Communication Runtime

- **Channels**: exactly two are modeled — `phone` (WhatsApp) and `email`
  (`AbandonedCartScannerJob.cs:217-230` `DetermineRecoveryContact`; `TriggerRecoveryEvent.ContactChannel`).
  No SMS, push, browser, or in-app recovery channel in code.
- **Channel priority**: verified phone (WhatsApp) is preferred; verified email is fallback
  (`DetermineRecoveryContact` returns phone first, `:219-227`).
- **Who actually sends**: n8n, via the WhatsApp outbound-delivery runtime (provider-abstracted,
  fail-closed) and/or email. The recovery runtime **never** calls a delivery provider directly.
- **Retry / delivery tracking**: n8n reports per-message results back through the authenticated
  `recovery-log` callback (`AdminAbandonedCartRecoveryController.cs:253`); the runtime stores
  `FirstMessageSent`/`SecondMessageSent` + errors in `AbandonedCartRecoveryLogs`
  (`AbandonedCartRecoveryLog.cs:26-34`). There is no dead-letter or provider fallback inside this runtime.
- **Rate limiting**: phone recovery is rate-limited via `IWhatsAppRateLimitService.CanSendMessageAsync`
  / `RecordMessageSentAsync` before and after the webhook (`RecoveryOrchestrationConsumer.cs:134-215`).

---

## Coupon Runtime Integration

**The recovery runtime does NOT create, assign, or attach coupons.** Evidence:
- A repo-wide search for coupon creation/assignment inside recovery surfaces only the *Coupon runtime*
  (`CouponsController.CreateCoupon`, `StorefrontCartController.GetOrCreateCouponStateAsync`) —
  none invoked by any recovery class.
- The restored cart snapshot **echoes** whatever coupon/promotion was already applied to the
  live cart (`RecoveredCartSnapshotFactory.cs:160-191`, `:193-214`), but the recovery controller
  refuses to restore from the stored snapshot at all (`StorefrontCartRecoveryController.cs:223-263`),
  so even that echo is recomputed from the live cart.
- `AbandonedCart.CartSnapshotJson` is a read-only personalization payload sent to n8n
  (`TriggerRecoveryEvent.cs:60-62`), not a coupon authority.

Conclusion: "Recovery Coupon" / "Campaign Coupon" as a first-class concept is **not implemented**.
If a coupon is offered during recovery, it is authored entirely inside the n8n workflow and applied
through the normal Coupon runtime when the customer checks out — outside this runtime's authority.

---

## Customer Qualification Runtime

Implemented qualification gates (all fail-fast, `AbandonedCartEligibilityService.cs:36-127`):
- **Automation enabled** (tenant setting) — `:43-50`.
- **WhatsApp account exists & enabled for `AbandonedCartRecovery`** — `:52-60`,
  `IWhatsAppFeatureGateService.IsAbandonedCartRecoveryEnabledAsync`.
- **WhatsApp account Connected** — `:62-70`, `WhatsAppAccountStatus.Connected`.
- **Verified contact present**: phone (`PhoneVerified` + non-empty) or email (`EmailVerified` + non-empty)
  — `AbandonedCartScannerJob.cs:217-230`; re-checked in consumer `RecoveryOrchestrationConsumer.cs:110-121`.
- **Not rate-limited / not opted out** (phone) — `RecoveryOrchestrationConsumer.cs:134-144`.
- **Active tenant** — `RecoveryOrchestrationConsumer.cs:80-90` (`TenantLineageAuthority`).
- **Guest vs registered**: both allowed; guest keyed by `SessionId`, registered by `CustomerId`
  (`AbandonedCart.cs:33-39`). No explicit "blocked/returning/recovered" customer entity here —
  those are derived from `RecoveryStatus` over time.

---

## Recovery Authority (who declares each outcome)

| Outcome | Declared by | Evidence |
|---|---|---|
| Recoverable (eligible) | `AbandonedCartEligibilityService` + scanner | `AbandonedCartEligibilityService.cs:36`; `AbandonedCartScannerJob.cs:87-119` |
| Recovered | `OrderRecoveryConsumer` -> `MarkAsRecoveredAsync` | `OrderRecoveryConsumer.cs:59`; `AbandonedCartService.cs:512` |
| Lost / Expired | `AbandonedCartCleanupJob` -> `ExpireOldTriggeredCartsAsync` | `AbandonedCartCleanupJob.cs:44`; `AbandonedCartService.cs:545` |
| Cancelled | Not modeled as a distinct state (terminal `Expired`/`Failed` cover it) | `AbandonedCartRecoveryStatus.cs:14-59` |
| Converted | Same as Recovered (tied to `RecoveredOrderId`) | `AbandonedCart.cs:114-118` |
| Failed (retries exhausted) | `RecordTriggerFailureAsync` at `RetryCount >= MaxRetryCount(3)` | `AbandonedCartService.cs:454-468`; `MaxRetryCount=3` `AbandonedCart.cs:152` |

---

## Recovery State Machine

Implemented states (`AbandonedCartRecoveryStatus.cs:14-59`):

```
Active ──(idle > FirstMessageDelayMinutes)──► Abandoned
Abandoned ──(TryMarkAsPendingAsync, atomic)──► Pending
Pending ──(webhook 200 / ConfirmTriggerSuccessAsync)──► Triggered
Pending ──(webhook fail / RecordTriggerFailureAsync, retry<3)──► Abandoned
Pending ──(stuck >5min reset)──► Abandoned        [AbandonedCartService.cs:256-319]
Abandoned/Failed? ──(retry exhausted)──► Failed   [AbandonedCartService.cs:454-460]
Triggered ──(OrderCreatedEvent -> MarkAsRecoveredAsync)──► Recovered
Triggered ──(TriggeredAtUtc older than 7d, cleanup)──► Expired
```

State transitions are all guarded by atomic `UPDATE ... WHERE status = X` so duplicates/races cannot
double-trigger (`AbandonedCartService.cs:365-374,405-414,515-522,549-555`).

---

## Conversion Runtime

- **Recovered Cart**: `AbandonedCarts.RecoveryStatus = Recovered` (`AbandonedCartService.cs:512`).
- **Recovered Order**: `AbandonedCarts.RecoveredOrderId` link set from `OrderCreatedEvent.CartId`
  (`OrderRecoveryConsumer.cs:45-59`). The order itself is owned by the Order runtime.
- **Recovered Revenue / Recovered Customer / Recovered Conversion**: NOT stored by this runtime.
  Revenue is computed by joining to the Order/Ledger at analytics time (outside this runtime).
  Customer "recovered" is implicit (same `CustomerId`/`SessionId` later completes an order).
- **Conversion attribution**: the runtime links recovery -> order via `CartId` only. There is no
  multi-touch or last-click attribution model here (that belongs to the Tracking/Attribution runtime).

---

## Analytics Runtime

Implemented analytics surface: `AbandonedCartRecoveryTelemetryService.GetStatsAsync`
(`AbandonedCartRecoveryTelemetryService.cs:129-170`) returns:
- `TotalAbandoned` = count where `AbandonedAtUtc != null`.
- `Recovered` = count where `RecoveredAtUtc != null`.
- `MessagesSent` = sum of `FirstMessageSent`+`SecondMessageSent` across `AbandonedCartRecoveryLogs`.
- `RecoveryRate` = `Recovered*100/TotalAbandoned` (rounded 2dp).

**NOT implemented here**: recovery revenue, recovered-orders money, coupon-usage, lost-revenue,
recovery-duration histograms. Those require joining to Order/Ledger/Tracking — this runtime only
publishes counters and the `AbandonedCartRecoveryTelemetry` metrics
(`AbandonedCartRecoveryTelemetry.cs:10-53`: callback success/duplicate/validation/auth/persistence
failures, recovery-token invalid/signature-failed).

---

## Tracking Runtime Integration

- This runtime **publishes** one integration event: `TriggerRecoveryEvent`
  (`AbandonedCartScannerJob.cs:132-165`; consumer `RecoveryOrchestrationConsumer.cs:61`).
- It **consumes** `CartActivityEvent` (ingest) and `OrderCreatedEvent` (recovery mark)
  (`CartActivityConsumer.cs:27`; `OrderRecoveryConsumer.cs:26`).
- It does **not** emit marketing/conversion pixels. Any "purchase -> recovered" analytics attribution is
  the Tracking/Attribution runtime's job, fed by `OrderCreatedEvent`, not by this runtime.
- `AnalyticsEvent`/pixel emission is a separate bounded context (see canonical tracking doc) — no
  recovery-specific event type exists.

---

## Commerce Runtime Integration

| Peer runtime | Interaction | Evidence |
|---|---|---|
| Storefront | Public recovery endpoint + cart-activity events | `StorefrontCartRecoveryController.cs:33,67`; `CartActivityConsumer.cs:27` |
| Identity | Reads `CustomerId`/`SessionId`; verified-contact flags | `AbandonedCart.cs:33-65` |
| Catalog / Inventory / Pricing | NOT touched by recovery (snapshot only, never re-priced) | `StorefrontCartRecoveryController.cs:223-263` (live cart re-read) |
| Cart | Reads live cart for restoration; never writes | `StorefrontCartRecoveryController.cs:232-245` |
| Checkout | Separate `AbandonedCheckout` (lost orders) owned by CheckoutSession runtime | `AbandonedCheckoutScannerJob.cs:8-64` |
| Payment | NOT touched (recovery never charges) | no payment ref in recovery path |
| Order | Consumes `OrderCreatedEvent` to mark Recovered | `OrderRecoveryConsumer.cs:26-59` |
| Shipping | NOT touched | — |
| Notifications | Delegated to n8n + WhatsApp outbound runtime | `N8nRecoveryOrchestrator.cs:35` |
| Coupons | Referenced only (echo of applied coupon) | `RecoveredCartSnapshotFactory.cs:160-191` |
| Promotions | Echoed in restored snapshot | `RecoveredCartSnapshotFactory.cs:193-214` |
| Affiliate | NOT touched (different bounded context) | — |
| CommerceHub | NOT touched (merchant runtime, not marketplace) | — |
| Reporting | Stats read from `AbandonedCarts`/`AbandonedCartRecoveryLogs` SQL | `AbandonedCartRecoveryTelemetryService.cs:129-170` |
| CMS / Builder / AI | NOT touched | — |

---



## Event Runtime

| Event | Direction | Produced by | Consumed by | Evidence |
|---|---|---|---|---|
| `CartActivityEvent` | in | Storefront | `CartActivityConsumer` | `CartActivityConsumer.cs:27,43`; upsert `AbandonedCartService.cs:60` |
| `TriggerRecoveryEvent` | out | `AbandonedCartScannerJob` | `RecoveryOrchestrationConsumer` | scanner `AbandonedCartScannerJob.cs:132-165`; consumer `RecoveryOrchestrationConsumer.cs:61` |
| `OrderCreatedEvent` | in | Order lifecycle | `OrderRecoveryConsumer` | `OrderRecoveryConsumer.cs:26,39,59` |
| n8n callback (`recovery-log` HTTP) | in | n8n | `AdminAbandonedCartRecoveryController` | `AdminAbandonedCartRecoveryController.cs:253` |

NOT present: `CheckoutAbandoned`, `RecoveryStarted`, `ReminderSent`/`ReminderDelivered`/
`ReminderFailed`, `CustomerReturned`, `RecoveryCompleted`, `RecoveryExpired` as first-class
integration events. Reminder delivery is reported via the **authenticated HTTP callback**, not an
event bus. Recovery "started/expired" are internal state transitions, not published events.

---

## Scheduling Runtime

- **Scheduler**: Hangfire recurring job `abandoned-cart-scanner`, `Cron.MinuteInterval(5)`
  (`HangfireInitializationHostedService.cs:235-238`).
- **Cleanup**: `abandoned-cart-cleanup` (daily) → `AbandonedCartCleanupJob` → `ExpireOldTriggeredCartsAsync`
  (`AbandonedCartCleanupJob.cs:35-54`; `ExpirationDays = 7` `:22`).
- **Queue/worker**: MassTransit consumer endpoint `abandoned-cart-recovery-v1` for `OrderRecoveryConsumer`
  (concurrency limit 10, retry 1s/5s/15s — `OrderRecoveryConsumerDefinition.cs:84-103`).
  `RecoveryOrchestrationConsumer` and `CartActivityConsumer` are MassTransit consumers too
  (`Program.cs:1966-1970`).
- **Retry/backoff (webhook)**: Polly `WaitAndRetryAsync(3, 2^n seconds)` inside the orchestrator
  (`N8nRecoveryOrchestrator.cs:101-117`).
- **Retry (state)**: `RecordTriggerFailureAsync` reverts `Pending->Abandoned` and increments `RetryCount`;
  at `>=MaxRetryCount(3)` -> `Failed` (`AbandonedCartService.cs:439-468`).
- **Dead letter / poison**: No DLQ in this runtime; consumer swallows exceptions to avoid redelivery loops
  (`RecoveryOrchestrationConsumer.cs:248-257`; `OrderRecoveryConsumer.cs:65-73`; `CartActivityConsumer.cs:100-114`).
- **Concurrency/idempotency**: atomic `UPDATE ... WHERE status=X`; unique `(TenantId,CartId)` upsert;
  callback idempotency key on `AbandonedCartRecoveryLogs` (`AbandonedCartRecoveryTelemetryService.cs:40-53`).

---

## Persistence Runtime

- **SQL (authoritive write model)**: `AbandonedCarts`, `TenantAutomationSettings`,
  `AbandonedCartRecoveryLogs` — all PostgreSQL (`AbandonedCart.cs:22`; `TenantAutomationSettings.cs:20`;
  `AbandonedCartRecoveryLog.cs:14`; migration `migrations/20260204_AbandonedCartRecoveryFoundation.sql:11`).
- **MongoDB**: NOT used by this runtime for its own state. (Separately, the **CheckoutSession** runtime
  projects `AbandonedCheckouts` to Mongo for the admin lost-orders dashboard —
  `AbandonedCheckoutDocument.cs:21`; `AdminAbandonedCheckoutsController.cs:21`. That is a different
  bounded context.)
- **Redis**: NOT used for recovery state.
- **Read models**: recovery stats are computed directly from SQL (`AbandonedCartRecoveryTelemetryService.cs:129-170`),
  no projection store.
- **Snapshots**: `AbandonedCart.CartSnapshotJson` is a JSON personalization blob sent to n8n
  (`AbandonedCart.cs:67-72`); it is explicitly NOT a restoration authority
  (`StorefrontCartRecoveryController.cs:223-263`).
- **Recovery state**: lives entirely in `AbandonedCarts.RecoveryStatus` (+ timestamps) — `AbandonedCart.cs:88-152`.

---

## CQRS

- **Commands**: `UpsertCartActivityAsync`, `MarkAsAbandonedAsync`, `TryMarkAsPendingAsync`,
  `ConfirmTriggerSuccessAsync`, `RecordTriggerFailureAsync`, `MarkAsRecoveredAsync`,
  `ExpireOldTriggeredCartsAsync`, `RecordWorkflowResultAsync` — all SQL writes on `AbandonedCarts`/
  `AbandonedCartRecoveryLogs` (`AbandonedCartService.cs`, `AbandonedCartRecoveryTelemetryService.cs`).
- **Events**: `CartActivityEvent` (in), `TriggerRecoveryEvent` (out), `OrderCreatedEvent` (in) —
  see Event Runtime table.
- **Queries**: `FindCartsReadyForAbandonmentAsync`, `GetByCartIdAsync`, `GetStatsAsync`,
  `GetEligibilityStatusAsync` — all SQL reads.
- **Projections**: NONE for this runtime's own data. (The separate `AbandonedCheckouts` Mongo doc is a
  projection of the CheckoutSession runtime, not this one.)
- **Replay / Repair / Outbox / Inbox**: NOT implemented inside this runtime. Outbox is the generic
  platform backbone (`OutboxPublisherJob`) used for domain events generally; this runtime's `TriggerRecoveryEvent`
  is published via `IEventPublisher` (`AbandonedCartScannerJob.cs:39,165`) and consumed by MassTransit.
  No dedicated outbox/inbox table or replay logic is specialized for recovery.

---

## Runtime Authority Matrix

| Concept | Owner (mutator) | Reader | Event consumer | Runtime truth |
|---|---|---|---|---|
| Cart | Cart runtime | this runtime (read-only live) | `CartActivityEvent` | Cart runtime |
| Checkout session | CheckoutSession runtime | admin (Mongo) | — | CheckoutSession runtime |
| Abandonment (cart) | **this runtime** | admin/analytics | — | `AbandonedCarts` SQL |
| Recovery decision/trigger | **this runtime** | n8n (via webhook) | `TriggerRecoveryEvent` out | `AbandonedCarts` SQL |
| Campaign | n8n (external) | n8n | — | n8n |
| Reminder | n8n (external) | this runtime (callback) | `recovery-log` HTTP | n8n + `AbandonedCartRecoveryLogs` |
| Coupon | Coupon runtime | this runtime (echo) | — | Coupon runtime |
| Customer (verified contact) | Identity/WhatsApp runtime | this runtime (gate) | — | Identity/WhatsApp runtime |
| Conversion (Recovered) | **this runtime** (state) + Order runtime (order) | analytics | `OrderCreatedEvent` in | both |
| Revenue | Order/Ledger runtime | analytics (join) | — | Order/Ledger runtime |
| Recovered Order | Order runtime | this runtime (link) | `OrderCreatedEvent` | Order runtime |
| Recovered Customer | Identity runtime (derived) | analytics | — | Identity runtime |

---

## Cross Runtime Authority Graph

| Peer | Reads | Writes | Events | Runtime authority | Sync model |
|---|---|---|---|---|---|
| Storefront | recovery token/endpoint | cart-activity events | `CartActivityEvent` (pub) | Storefront owns cart | event + public endpoint |
| Identity | verified flags, CustomerId/SessionId | — | — | Identity owns customer | read-only flags |
| WhatsApp (outbound) | account status/connected | — (n8n calls it) | — | WhatsApp runtime owns send | gate + rate-limit |
| Cart | live cart (restoration) | — | — | Cart owns contents | read-only on recover |
| Checkout | — | — (separate lost-order scanner) | — | CheckoutSession owns | independent scanner |
| Order | RecoveredOrderId link | recovery status | `OrderCreatedEvent` (consume) | Order owns order | event-driven link |
| Payment | — | — | — | Payment owns money | none |
| Coupon | applied-coupon echo | — | — | Coupon owns coupon | read-only echo |
| Notifications | — | — (delegated to n8n) | — | n8n/WhatsApp owns | webhook handoff |
| Tracking/Attribution | — | — | (no recovery event) | Tracking owns pixels | none (OrderCreated feeds it) |
| Reporting | SQL stats | — | — | this runtime (counts) | direct SQL |
| CommerceHub / Affiliate / CMS / Builder / AI | — | — | — | not in recovery scope | none |

---

## Business Invariants (implementation-backed)

1. **Recovery only for opted-in tenants.** `EnableAbandonedRecovery` must be true
   (`AbandonedCartEligibilityService.cs:43-50`; default false `TenantAutomationSettings.cs:27`).
2. **Recovery only over verified contact.** Phone requires `PhoneVerified`+number; email requires
   `EmailVerified` (`AbandonedCartScannerJob.cs:217-230`; consumer `RecoveryOrchestrationConsumer.cs:110-121`).
3. **Abandonment requires inactivity past tenant delay.** `LastActivityAtUtc + FirstMessageDelayMinutes <= now`
   (`AbandonedCartService.cs:191-207`); default 60 min (`TenantAutomationSettings.cs:37`).
4. **No duplicate trigger.** `TryMarkAsPendingAsync` is atomic on `WHERE status='Abandoned'`
   (`AbandonedCartService.cs:365-374`); `TriggerRecoveryEvent` published once per scan hit.
5. **State transitions are atomic and race-free.** All status moves use `UPDATE ... WHERE status=X`
   (`AbandonedCartService.cs:365,405,515,549`).
6. **Activity resets abandonment.** A returning customer flips `Abandoned/Pending/Triggered -> Active`
   (`AbandonedCartService.cs:107-131`).
7. **Recovery token is signed + scoped + 7-day bounded + owner-matched.**
   (`AbandonedCartService.cs:570-695`; `StorefrontCartRecoveryController.cs:114-213`).
8. **Restoration refuses stored snapshot; live cart is the only authority.**
   (`StorefrontCartRecoveryController.cs:223-263`; snapshot validation `:266-387`).
9. **Recovered only if an order actually completes for that cart.** `MarkAsRecoveredAsync` keyed by
   `CartId` from `OrderCreatedEvent`, guarded against terminal states (`AbandonedCartService.cs:512-522`).
10. **Callback integrity.** `recovery-log` requires valid API key (fixed-time) + idempotency key
    (`AdminAbandonedCartRecoveryController.cs:306-333,494-509`; `AbandonedCartRecoveryTelemetryService.cs:40-53`).
11. **Tenant isolation / lineage.** Every consumer verifies `TenantLineageAuthority.IsActiveTenantAsync`
    (`RecoveryOrchestrationConsumer.cs:80-90`); cross-tenant admin callbacks rejected
    (`AdminAbandonedCartRecoveryController.cs:279-294`).
12. **Expiration is time-bounded.** Triggered carts with no recovery within 7 days -> `Expired`
    (`AbandonedCartCleanupJob.cs:22,44`; `AbandonedCartService.cs:545-555`).

---



## Architectural Decisions (WHY — implementation-only)

- **Separate `AbandonedCheckout` (lost orders, Mongo, CheckoutSession-owned) from `AbandonedCart`
  (recovery, SQL, this runtime).** Two different owners, two different stores, two different
  dashboards. The cart-recovery runtime deliberately does NOT read/write checkout sessions.
  (`AbandonedCheckoutScannerJob.cs:8-64` vs `AbandonedCartScannerJob.cs:34-215`).
- **Externalize the campaign engine to n8n.** The runtime's job is *decide + trigger*, not *compose
  messages*. This keeps messaging logic (templating, working-hours, multi-step, A/B) out of the
  C# monolith and lets non-engineers edit flows. The only coupling is a signed webhook +
  a callback API (`N8nRecoveryOrchestrator.cs:35`; `AdminAbandonedCartRecoveryController.cs:239`).
- **SQL-authoritative recovery state with atomic status guards.** Avoids duplicate n8n calls,
  double-messaging, and race conditions under Hangfire+MassTransit concurrency
  (`AbandonedCartService.cs:365-414`).
- **Stored snapshot is NOT a restoration authority.** On recover-click the live cart is re-read and
  validated (totals, owner, expiry) so a tampered/old payload cannot forge a cart or prices
  (`StorefrontCartRecoveryController.cs:223-387`). The `CartSnapshotJson` is only a personalization
  hint for n8n, never re-injected.
- **Signed, owner-scoped, 7-day recovery token** via ASP.NET Data Protection, fail-closed
  validation — a public endpoint (`AllowAnonymous`) must not leak cross-tenant or stale carts
  (`StorefrontCartRecoveryController.cs:32,67,114`; `AbandonedCartService.cs:570-695`).
- **Fail-closed tenant lineage + verified-contact gating.** Recovery is a customer-facing outbound
  action; the runtime refuses to act on inactive tenants or unverified contacts
  (`RecoveryOrchestrationConsumer.cs:80-121`; `AbandonedCartEligibilityService.cs:36-77`).
- **Recovery is non-critical path.** `OrderRecoveryConsumer` swallows errors so a recovery-tracking
  failure can never block an order (`OrderRecoveryConsumer.cs:65-73`).
- **Three-phase rollout (Foundation → Phase2 → Hardening).** Foundation shipped config+eligibility
  with zero sending; Phase2 added detection+trigger; Hardening added atomicity/retry/token security.
  This sequencing let the team prove safety before any customer was messaged.

---

## Technical Debt (evidence-backed)

1. **No revenue in recovery analytics.** `GetStatsAsync` counts recovered carts but cannot report
   recovered revenue without joining Order/Ledger (`AbandonedCartRecoveryTelemetryService.cs:129-170`).
   "Recovered Revenue" is currently unmeasurable from this runtime alone.
2. **No first-class Coupon-in-recovery.** Despite "Recovery Coupon / Campaign Coupon" terminology,
   no coupon is created or assigned by this runtime; any offer is entirely inside n8n + the Coupon
   runtime. Gap between marketing naming and implementation.
3. **No campaign/workflow aggregate.** Schedule is only 2 fixed delays + working-hours. No templates,
   no dynamic content, no per-segment campaigns modeled in code. n8n is the de-facto campaign store.
4. **No DLQ / dead-letter for recovery events.** Consumers swallow exceptions to avoid redelivery
   loops, which means a poison `TriggerRecoveryEvent` is silently dropped
   (`RecoveryOrchestrationConsumer.cs:248-257`). Observability depends on telemetry counters only.
5. **Callback is trusted-bearer (API key) over a public endpoint.** `recovery-log` is `[AllowAnonymous]`
   and gated only by `N8nWebhookApiKey` (`AdminAbandonedCartRecoveryController.cs:239-333`).
   Key compromise = ability to write recovery logs for a tenant. Mitigated by idempotency + fixed-time
   compare, but it is a privileged unauthenticated surface.
6. **Two parallel "abandonment" concepts** (`AbandonedCarts` SQL vs `AbandonedCheckouts` Mongo)
   can confuse operators; they are different owners but share naming.
7. **`AbandonedCart` entity comment still says "Cart is active, customer is shopping"** while the
   runtime also tracks guest `SessionId` carts — docs lag implementation (minor).

---

## Implementation Follow-up Items

| Item | Current state | Priority | Evidence |
|---|---|---|---|
| Add recovered-revenue to stats (join Order/Ledger) | counts only | Medium | `AbandonedCartRecoveryTelemetryService.cs:129-170` |
| Model coupons in recovery (or document n8n-only) | not implemented | Low | no coupon ref in recovery |
| Campaign/template aggregate (or formalize n8n contract) | n8n-only | Medium | `N8nRecoveryOrchestrator.cs:54` |
| DLQ / poison handling for recovery events | swallowed | Medium | `RecoveryOrchestrationConsumer.cs:248-257` |
| Strengthen `recovery-log` auth (mTLS / signed payload) | API-key only | High | `AdminAbandonedCartRecoveryController.cs:239-333` |
| De-duplicate abandonment naming (cart vs checkout) | two concepts | Low | `AbandonedCheckoutScannerJob.cs` vs `AbandonedCartScannerJob.cs` |

---

## Runtime Timeline (one complete customer journey — per-step, implementation-cited)

1. **Customer adds product** → Storefront mutates live cart (`ICartService`). Recovery is NOT involved.
   Owner: Cart runtime. Persistence: Cart store. No recovery event.
2. **Cart Created** → Storefront publishes `CartActivityEvent` (cartId, customerId/sessionId,
   verified phone/email, snapshot). `CartActivityConsumer.cs:27,43,71`. Owner: this runtime
   (tracking) — upsert `AbandonedCarts` (`Active`). Persistence: `AbandonedCarts` SQL.
   Synchronization: `ON CONFLICT (TenantId,CartId) DO UPDATE` (`AbandonedCartService.cs:65-131`).
3. **Checkout Started** → CheckoutSession runtime. OUTSIDE recovery. (A separate `AbandonedCheckouts`
   Mongo projection may later appear via `AbandonedCheckoutScannerJob` after 30-min idle
   `AbandonedCheckoutScannerJob.cs:26,45` — different owner.)
4. **Customer Leaves / inactivity** → `LastActivityAtUtc` stops updating. No event; abandonment is
   *inferred* by the scanner (`AbandonedCartService.cs:191-207`).
5. **Abandonment Detection** → Hangfire `abandoned-cart-scanner` every 5 min
   (`HangfireInitializationHostedService.cs:235-238`) finds `Active` + idle>`FirstMessageDelayMinutes`,
   checks eligibility (`AbandonedCartEligibilityService.cs:36`) and verified contact
   (`AbandonedCartScannerJob.cs:217-230`), then `MarkAsAbandonedAsync`
   (`AbandonedCartService.cs:321-352`). Owner: this runtime. Authority: `AbandonedCarts` SQL.
6. **Recovery Qualification** → tenant automation on + WhatsApp connected + verified contact
   (`AbandonedCartEligibilityService.cs:36-77`; re-checked in consumer
   `RecoveryOrchestrationConsumer.cs:110-121`).
7. **Reminder Campaign handoff** → `GenerateRecoveryToken` (`AbandonedCartService.cs:570`) builds
   `{storefront}/recover-cart/{token}`; scanner publishes `TriggerRecoveryEvent`
   (`AbandonedCartScannerJob.cs:128-165`).
8. **Coupon Assignment** → NONE in this runtime. If any coupon, it is applied later in Checkout via
   the Coupon runtime; recovery only echoes an already-applied code
   (`RecoveredCartSnapshotFactory.cs:160-191`).
9. **Reminder Delivery** → `RecoveryOrchestrationConsumer.TryMarkAsPendingAsync` (`AbandonedCartService.cs:358`)
   → `N8nRecoveryOrchestrator.TriggerRecoveryWorkflowAsync` HTTP webhook (`N8nRecoveryOrchestrator.cs:35,119`)
   → on 200 `ConfirmTriggerSuccessAsync` (`Pending->Triggered`, `AbandonedCartService.cs:403`);
   n8n sends messages on its own schedule. Delivery tracking returns via `recovery-log` callback
   (`AdminAbandonedCartRecoveryController.cs:253`) → `AbandonedCartRecoveryLogs`.
   Failure/Retry: Polly 3x backoff (`N8nRecoveryOrchestrator.cs:101-117`); state retry
   (`AbandonedCartService.cs:439-468`, max 3 → `Failed`).
10. **Customer Returns (clicks link)** → `StorefrontCartRecoveryController.RecoverCart` (public,
    `[AllowAnonymous]`) validates signed token (`AbandonedCartService.cs:606`), refuses stored
    snapshot, re-reads LIVE cart (`StorefrontCartRecoveryController.cs:223-263`). Owner: Cart runtime
    (authority) + this runtime (token). Consistency: owner-match enforced `:203-213`.
11. **Checkout Continues → Payment → Order Created** → Checkout/Order/Payment runtimes (NOT recovery).
    On order, `OrderCreatedEvent` fires.
12. **Recovery Closed (Recovered)** → `OrderRecoveryConsumer` consumes `OrderCreatedEvent`,
    calls `MarkAsRecoveredAsync(CartId, OrderId)` → `AbandonedCarts` `Recovered` +
    `RecoveredOrderId` link (`OrderRecoveryConsumer.cs:59`; `AbandonedCartService.cs:512-522`).
    Owner: this runtime (state) + Order runtime (truth).
13. **(Alt) Expired** → if no order within 7 days, `AbandonedCartCleanupJob.ExpireOldTriggeredCartsAsync`
    flips `Triggered->Expired` (`AbandonedCartCleanupJob.cs:44`; `AbandonedCartService.cs:545-555`).

---

## Runtime Classification

**This runtime is fundamentally a RECOVERY ORCHESTRATION RUNTIME** (a Customer Re-engagement /
Conversion-Recovery runtime) layered observantly OVER the Checkout/Cart/Order/Payment/Coupon
authorities. It is NOT a Checkout runtime, NOT a Campaign runtime, NOT a Marketing-messaging
runtime, and NOT a standalone Commerce runtime.

Evidence for the classification:

- **Orchestration, not ownership**: it decides *whether* to recover and *when* to trigger, then hands
  delivery to n8n (`AbandonedCartScannerJob.cs:132` → `N8nRecoveryOrchestrator.cs:35`). It owns
  zero commerce truth beyond its own `AbandonedCarts` state machine.
- **Re-engagement semantics**: the entire purpose is re-contacting shoppers who left
  (`AbandonedCart.cs:3-21`; eligibility `AbandonedCartEligibilityService.cs:36`).
- **Conversion-recovery measurement**: success = a later `OrderCreatedEvent` for the same `CartId`
  (`OrderRecoveryConsumer.cs:59`), i.e. recovered conversion — not message opens.
- **NOT a campaign runtime**: no campaign/workflow/template aggregate exists in code; the "campaign"
  is the external n8n webhook (`N8nRecoveryOrchestrator.cs:54`). The prompt's "Recovery Campaign
  Runtime" label is unsupported.
- **NOT a marketing-messaging runtime**: it never sends a message; WhatsApp/email delivery is
  delegated (`N8nRecoveryOrchestrator.cs:119-130` only POSTs a webhook). The outbound-delivery
  authority lives in the WhatsApp runtime.
- **Hybrid? Partly**: it spans detection (scanner) + orchestration (event/consumer) + measurement
  (stats) + a public restoration endpoint. But every span is in service of *recovery orchestration*,
  so "Hybrid Recovery Runtime" is a fair secondary label; "pure Marketing/Checkout/Campaign" is wrong.

**Final label:** *Commerce Recovery Orchestration Runtime* (customer re-engagement / conversion
recovery), SQL-authoritative for its own state, externally delegating campaign execution and all
commerce/payment/coupon truth.

---

## Final Architecture Question — answered

> "How does Shopixy detect incomplete customer purchase journeys, determine recovery eligibility,
>  orchestrate re-engagement workflows, preserve authoritative commerce truth, and measure successful
>  recovery without violating the authority of Checkout, Order, Payment, or Marketing runtimes?"

1. **Detect incomplete journeys** by *polling* `AbandonedCarts` for carts idle beyond the
   tenant's `FirstMessageDelayMinutes`, every 5 minutes via Hangfire
   (`AbandonedCartScannerJob.cs:80-207`; `HangfireInitializationHostedService.cs:235-238`).
   Cart activity is ingested from `CartActivityEvent` into the SQL `AbandonedCarts` ledger
   (`CartActivityConsumer.cs:27,71`); abandonment is *inferred*, never asserted by another runtime.
   A *separate* scanner handles truly-started checkouts (lost orders) and is owned by the
   CheckoutSession runtime (`AbandonedCheckoutScannerJob.cs:8-64`).

2. **Determine eligibility** through fail-closed, ordered gates: automation enabled
   (`AbandonedCartEligibilityService.cs:43-50`), a connected WhatsApp account enabled for
   `AbandonedCartRecovery` (`:52-70`), and a *verified* contact (phone > email)
   (`AbandonedCartScannerJob.cs:217-230`; consumer re-check `RecoveryOrchestrationConsumer.cs:110-121`),
   plus active-tenant lineage (`RecoveryOrchestrationConsumer.cs:80-90`) and phone rate-limit/opt-out
   (`:134-144`).

3. **Orchestrate re-engagement** by publishing a single `TriggerRecoveryEvent`
   (`AbandonedCartScannerJob.cs:132-165`) which a MassTransit consumer turns into an atomic
   `Pending` state (`AbandonedCartService.cs:358-397`), then an HTTP webhook to **n8n**
   (`N8nRecoveryOrchestrator.cs:35,119`). n8n owns the actual messaging campaign, schedule,
   retries, and working-hours. Success/failure flow back through an authenticated `recovery-log`
   callback (`AdminAbandonedCartRecoveryController.cs:253`) into `AbandonedCartRecoveryLogs`.

4. **Preserve authoritative commerce truth** by *never* writing it: the live cart is re-read (never
   the stored snapshot) on restoration (`StorefrontCartRecoveryController.cs:223-263`); orders/payments
   remain the Order/Payment lifecycles' domain (this runtime only stores a `RecoveredOrderId` link,
   `AbandonedCartService.cs:512-522`); coupons are echoed, never created
   (`RecoveredCartSnapshotFactory.cs:160-191`); WhatsApp/email sending is delegated to the outbound
   delivery runtime. The restoration token is signed, owner-scoped, 7-day bounded, and fail-closed
   (`AbandonedCartService.cs:570-695`).

5. **Measure successful recovery** purely as a state transition: when the customer later completes an
   order, `OrderCreatedEvent` → `OrderRecoveryConsumer` → `MarkAsRecoveredAsync`
   (`OrderRecoveryConsumer.cs:26-59`), flipping `AbandonedCarts` to `Recovered` and linking
   `RecoveredOrderId`. Stats (`recovery rate = recovered/abandoned`) are computed from SQL
   (`AbandonedCartRecoveryTelemetryService.cs:129-170`). Revenue itself stays in the Order/Ledger
   runtime and is joined at analytics time — this runtime intentionally stores no money.

**Net:** Shopixy isolates recovery as a thin, SQL-authoritative *orchestration* layer that triggers an
external automation and observes the resulting conversion via an order event — so Checkout, Order,
Payment, Coupon, and Marketing/WhatsApp delivery each keep their authority, and recovery can never
mutate commerce truth.

---

## Certification

- **Confidence**: High (every claim traced to backend `file:line`; primary files read directly).
- **Completeness**: High for the cart-recovery path; the *checkout* "abandonment" (lost orders) is
  a separate bounded context and is referenced but not owned here.
- **Verified (with evidence)**: detection model (scanner + threshold), eligibility gates, state machine
  (Active/Abandoned/Pending/Triggered/Recovered/Expired/Failed), n8n webhook handoff, signed
  token + fail-closed restoration, callback auth/idempotency, stats counting, tenant lineage,
  separation from CheckoutSession `AbandonedCheckouts`.
- **Missing / gaps (not fabricated)**: (a) recovered *revenue* not computed in this runtime;
  (b) no coupon creation/assignment in recovery (terminology vs code mismatch);
  (c) no campaign/workflow/template aggregate (n8n-only); (d) no DLQ for recovery events
  (exceptions swallowed); (e) `recovery-log` is an unauthenticated-except-API-key surface.
- **Phantom-terminology avoided**: "Recovery Campaign Runtime", "Recovery Coupon", "ReminderSent event",
  "SMS/Push/Browser channels" are NOT implemented as named; each is either externalized to n8n or
  absent. The implementation wins over the prompt's marketing vocabulary.

*Implementation is the only source of truth. This document supersedes any prior "Smart Abandoned Cart
Recovery Engine" markdown/certification where they conflict with the cited code.*
