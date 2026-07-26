# Payment Management (POS Admin capability)

Reverse-engineered from implementation. Implementation is the authority; `docs/architecture/*`
findings are secondary. Where they disagree, implementation wins and the disagreement is
reported in "Documentation Validation" at the end.

Scope of this document: the operator-facing Payment Management capability for **POS Admin**
(tenant business operator surface). This capability owns *tenant* payment truth for Storefront
and POS sales. CommerceHub marketplace payments are a SEPARATE authority owned by CommerceHub
payment services (see "Authority Boundaries" and "Cross-System Effects"); this doc covers how the
CommerceHub payment boundary relates to POS Admin, but does not conflate the two.

---

## Purpose

Payment Management exists to **recognize money safely and authoritatively** for orders placed
through Storefront (customer checkout), POS counters, and COD/delivery flows, then to **settle**
that money into the tenant ledger and **enable refunds/cancellations** without double-counting,
without leaking tenant data, and without letting the UI, provider redirects, or webhook replays
fabricate financial truth.

Business problem it solves:
- Money can arrive through many channels (Stripe, Paymob, Fawry, COD, manual wallet uploads) with
  asynchronous, replayable, sometimes contradictory callbacks.
- The platform must never recognize revenue on a state that is not durable, never refund money it
  did not collect, never collect twice, and never let a browser/proxy claim payment finality.
- Payment outcome must drive order/fulfillment state and inventory commitment deterministically
  while remaining auditable for accounting/settlement.

What it owns:
- The authoritative `Order.PaymentStatus` for tenant orders (Storefront/POS/COD/Wallet).
- The `PaymentIntent` child record of an order (payment attempt authority).
- Authorization, capture/collection, settlement, refund, and expiration of *tenant* payments.
- The journal postings (via the accounting outbox) that recognize money.
- Idempotency and duplicate/replay protection for payment and refund outcomes.
- The suspense-ledger routing for unresolved/mismatched provider callbacks.

What it does NOT own:
- CommerceHub marketplace payment/settlement/payout truth (owned by CommerceHub finance/payment
  services; EGP-sovereign).
- Affiliate commission calculation or payout (it only emits payment-captured events that Affiliate
  consumes; Affiliate is the owner of commission).
- Inventory quantities (Payment commits/releases reservations through the inventory authority; it
  does not own stock truth).
- Customer identity/session, catalog, pricing (pricing is recomputed at checkout; payment uses the
  recorded amount).
- Shipping/fulfillment state (fulfillment is a separate lifecycle that consumes payment evidence).
- Search/Elastic, Mongo projections (derivative read models).

---

## Business Scope

### Belongs to Payment Management (implementation-proven)

- **Payment Intent** — `PaymentIntent` (child of `Order`, `ITenantOwned`). Stores amount, currency,
  method, provider, reference code, provider intent/payment ids, encrypted client secret, webhook
  counters, manual-wallet proof fields, and `InventoryCommitted` flag. Created before/at checkout.
- **Payment Lifecycle** — `IPaymentLifecycleService` is the ONLY writer of `Order.PaymentStatus`.
  Direct assignment is blocked at runtime by `PaymentStatusSaveChangesInterceptor`.
- **Payment Status (order-level)** — `PaymentStatus` enum (`PointOfSale.Core.Enums`), PostgreSQL is
  truth. States: Pending, Authorized, Collected, Settled, PartiallyRefunded, Refunded,
  AwaitingVerification, Rejected, Failed, Expired, AmountMismatch (+ obsolete Paid/PartiallyPaid,
  + dormant runtime-only states, see "Payment State Machine").
- **Intent Status** — `PaymentIntentStatus` enum: Pending, Authorized, Paid, Failed, Cancelled,
  Expired, Skipped (COD), AwaitingVerification, Rejected, AmountMismatch, Suspicious.
- **Authorization** — `HandlePaymentResultAsync` with `Outcome=Authorized`; two-step online flow.
  `Pending → Authorized`. No journal yet.
- **Capture / Collection** — `Outcome=Succeeded` (webhook/COD delivery/wallet approval).
  `Pending/Authorized → Collected`. Journal: DEBIT clearing / CREDIT revenue. This is the ONLY state
  that recognizes revenue. Triggers inventory reservation commit, coupon/offer usage confirmation,
  order status advance, and the success outbox + domain events.
- **Settlement** — `RecordGatewaySettlementAsync` / `RecordCodSettlementAsync`.
  `Collected → Settled`. Journal: DEBIT bank (net) / DEBIT fees / CREDIT clearing. Settlement batches
  (`PaymentSettlementBatch` + `PaymentSettlementOrder` lineage) are the authority; the batch
  `OrderCount` denormalized field is metadata only.
- **Refunds / Partial Refunds** — `RecordRefundAsync`. `Collected/Settled/PartiallyRefunded →
  Refunded` (full) or `PartiallyRefunded`. Journal: DEBIT revenue / CREDIT clearing. Allocation is
  item-level (`OrderRefund` + `OrderRefundLine`). Amount + tolerance validated against remaining
  refundable amount.
- **Payment Providers (tenant, online + manual)** — Stripe, Paymob, Fawry, COD (`Cod`), Manual
  Wallet (`ManualWallet`). Provider is selected per order at checkout; configured tenant-side via
  `TenantPaymentMethod` (`Code`, `IsEnabled`, `SupportsOnline`, `SupportsOffline`, `IsSandbox`,
  `Fee`, min/max order amount, encrypted config).
- **COD** — No provider session. Intent status `Skipped`. `Pending → Collected` only after
  *delivery* (`MarkCodDeliveredAsync`, requires `FulfillmentStatus == Delivered`). THEN
  `Collected → Settled` on shipping-company deposit (`RecordCodSettlementAsync`).
- **Manual Wallet Payments** — Customer uploads proof (Vodafone Cash / Instapay, etc.). Intent
  `Pending → AwaitingVerification` (admit). Admin approves (`AwaitingVerification → Collected`,
  journal) or rejects (`→ Rejected`, no journal). Proof image has a retention/expiry policy
  (`ProofImageExpiresAtUtc`, cleanup job).
- **Payment Verification** — Provider signature verification (Stripe-Signature, Paymob HMAC, etc.),
  idempotency-key dedup on webhook, and manual admin verification for wallet proofs.
- **Payment Evidence** — `PaymentWebhookLog` records every webhook (signature-verified flag,
  processing status, error, replay counters). `CommerceHubUnresolvedPaymentWebhook` and the suspense
  ledger handle unresolved callbacks. Provider callbacks are **evidence only**, never truth.
- **Payment Reconciliation** — Late-success-after-release quarantine, admin-cancellation-pending
  quarantine, amount/currency authority mismatch quarantine (`AmountMismatch`/`Suspicious`/`ManualReview`).
  `StorefrontUnresolvedPaymentReconciliationJob` resolves suspended webhooks.
- **Accounting Posting** — Every money-recognizing transition enqueues an `AccountingOutboxMessage`
  inside the same SQL transaction (transactional outbox). Post-commit best-effort external posting;
  outbox retries if posting fails.
- **Payment Expiration** — `MarkPaymentExpiredAsync` (reservation cleanup / late window).
  `Pending/Authorized/AwaitingVerification → Expired`. No journal; releases reservations + coupons.
- **Payment Retry / Idempotency** — Webhook idempotency keys, `PaymentIntent.IdempotencyKey`,
  duplicate-refund hash detection, replay collapse in the lifecycle service, and the runtime
  decision/replay guards (see hidden behavior).
- **Currency / Amount Validation** — Provider amount/currency are AUTHORITATIVE for webhook sources;
  mismatch → quarantine. Non-webhook sources (COD/wallet) fall back to intent Amount/Currency.
  All currency rounding 2dp `MidpointRounding.AwayFromZero` (`CurrencyHelper.Round`).
- **Payment Notifications** — WhatsApp/messaging payment approval/rejection/approval-requested
  consumers (`WhatsAppPayment*Consumer`) are downstream evidence consumers, not owners.
- **Payment Projection** — Mongo/Redis/Elastic read models are derivative. `CanonicalPaymentStatusResolver`
  maps internal state → storefront contract (PENDING/PROCESSING/MANUAL_REVIEW/FAILED/PAID).
- **CommerceHub Payments (boundary only)** — POS Admin operates the CommerceHub *vendor* surface; the
  marketplace payment truth is owned by CommerceHub payment services, EGP-only currency.
- **Affiliate (trigger only)** — Payment capture emits `OnlinePaymentCapturedEvent`,
  `OrderCodDeliveredEvent`, `WalletPaymentApprovedEvent`; CommerceHub payment capture emits
  `AffiliatePaymentCaptured`-style consumers. Affiliate owns commission.

### Does NOT belong

- CommerceHub marketplace settlement/payout finality (CommerceHub finance).
- Super Admin marketplace governance / refund execution (separate authority).
- Inventory truth, pricing recomputation, shipping/fulfillment mutation, search indexing.
- Customer/store identity, session validity (evidence only).

---

## Authority Boundaries

| Concern | Authority (writes truth) | Projection / Evidence | Presentation |
|---|---|---|---|
| Order PaymentStatus | `IPaymentLifecycleService` (SQL) via interceptor guard | `PaymentIntent`, `PaymentWebhookLog` | Storefront/CommerceHub/Admin DTOs |
| Payment Intent | Checkout + lifecycle service (SQL) | Mongo read models | Admin/Storefront views |
| Money Collection | lifecycle `HandleSuccessAsync` → accounting outbox (SQL) | `AccountingOutboxMessage` | ledger |
| Settlement | `RecordGatewaySettlementAsync`/`RecordCodSettlementAsync` (SQL) | `PaymentSettlementBatch`/`PaymentSettlementOrder` | finance UI |
| Refund | `RecordRefundAsync` + `OrderRefund` (SQL) | `OrderRefundLine` | admin refund UI |
| Accounting Journal | Accounting outbox processor (same tx) | outbox rows | ledger |
| Order Status | order lifecycle (`OrderStatusSyncService` derives from payment/fulfillment); payment advances it but does NOT own it | — | — |
| Checkout | CheckoutService (creates intent) | Redis cart (NOT authority) | storefront |
| Inventory Reservation | Inventory authority; payment commits/releases via it | `StockMovements` | — |
| Notifications | Notification/WhatsApp services (consume events) | — | WhatsApp/messages |
| Analytics | Analytics consumers (consume events) | — | dashboards |
| CommerceHub Payment | CommerceHub payment services (SQL, EGP) | `CommerceHubPayment` | vendor UI |
| Affiliate Commission Trigger | Payment emits capture events; **Affiliate owns commission** | — | affiliate UI |
| Search | Search indexing (consume events) | Elasticsearch (derivative) | storefront search |
| Storefront Projection | Storefront read models (SQL-recomposed) | Mongo/Redis | storefront |

Key distinction: **Authority = SQL truth mutated only through lifecycle/order/settlement/refund
services.** **Projection = Mongo/Redis/Elastic re-derived documents.** **Evidence = provider
webhooks, browser outcomes, callbacks, logs — never truth.** **Presentation = DTOs/UI that re-fetch
backend state and may not fabricate lifecycle.**

---

## Payment Lifecycle (implementation)

```
CheckoutService creates Order + PaymentIntent (Pending)
        │  (COD: intent status = Skipped, no provider session)
        ▼
Provider session / intent created (Stripe/Paymob/Fawry)  OR  Manual wallet upload
        ▼
Provider webhook / callback arrives at PaymentWebhooksController  (AllowAnonymous, tenant resolved
        from stored record, X-Tenant-Id untrusted, signature verified)
        │
        ▼
IPaymentService.ProcessWebhookAsync → provider adapter → IPaymentLifecycleService
        │
        ├─ Authorized  → Pending → Authorized   (no journal)
        ├─ Succeeded   → Pending/Authorized → Collected  (JOURNAL + commit inventory + confirm
        │                                                     coupon/offer + advance order + events)
        ├─ Failed/Cancelled/Rejected/Expired → terminal (no journal, release reservation/coupon)
        └─ AmountMismatch → Quarantined (ManualReview / AmountMismatch) — manual review
        ▼
Accounting outbox enqueued IN SAME TX  (OnlinePayment / EcommerceRefund / GatewaySettlement /
        CodSettlement) → external ledger posting (best-effort, outbox retries)
        ▼
Settlement (later batch): Collected → Settled  (Gateway: net bank deposit; COD: shipping deposit)
        ▼
Refund (admin/provider): Collected/Settled → Refunded | PartiallyRefunded (item-level allocation)
        ▼
Order synchronization: order status/financial state derived (Confirmed/Delivered from payment+
        fulfillment); inventory confirmed (committed) or released (failed/expired)
        ▼
Projection: Mongo/Redis/Elastic re-composed from SQL; Storefront canonical status = PAID/PROCESSING/
        MANUAL_REVIEW/FAILED/PENDING; customer sees payment in order history.
```

Terminal money-recognized states: `Collected`, `Settled`, `Paid` (obsolete alias of Collected).
`PaymentStatusAuthority.IsPaidState` treats Collected/Paid/Settled as paid.

---

## Implementation Discovery (responsibilities, no code dumps)

### Frontend (evidence/presentation only)
- Storefront `features/checkout/paymentFlow.ts`, `PaymentStep.tsx`, `paymentSessionAuthority.ts`:
  present provider outcomes, poll backend, strip provider tokens from URL, re-fetch before action.
- CommerceHub `features/checkout/activePaymentRecoveryContext.ts`, `OrderPaymentRecoveryPage.tsx`:
  recovery hand-off only; no authority.
- POS Admin / Web `super-admin/payments/*`: monitoring + admin refund/payout-review surfaces.
- Web `payment/*` and `super-admin/refunds/*`: lifecycle authority validation (rejects contradictions).

### Controllers / Endpoints
- `PaymentWebhooksController` — `POST api/payments/webhooks/{provider}` (AllowAnonymous,
  AllowAnonymousTenant). Signature verified per provider. Returns 200 only for processed/duplicate;
  401 invalid signature; 500 on unresolved failure (provider retries). Tenant resolved from stored
  payment record; supplied `X-Tenant-Id` is a mismatch check only.
- `PaymentLifecycleController`, `PaymentMethodsAdminController`, `StorefrontPaymentMethodsController`,
  `AdminManualPaymentController`, `CommerceHubPaymentsController` — operational surfaces that call
  lifecycle/provider services; none mutate `PaymentStatus` directly.
- `PaymentService` (app service) — orchestrates intent creation/resume, webhook dispatch, COD confirm,
  manual confirm/reject, status queries. Idempotent intent creation with replay detection.

### Application Services
- `IPaymentService` / `PaymentService` — webhook pipeline + intent orchestration.
- `StripePaymentService`, `PaymobPaymentService`, `FawryPaymentAdapter`, `ManualPayment*` — provider
  adapters. Each verifies signature, resolves tenant, dedups via idempotency key, then calls the
  lifecycle service. Provider browser outcomes are non-final.
- `CommerceHubPaymentService`, `CommerceHubPaymentLifecycleAuthority`, `CommerceHubPaymentRecoveryAuthorityService`
  — CommerceHub marketplace payment boundary (EGP). Same lifecycle owner pattern but separate SQL
  entities (`CommerceHubPayment`, `CommerceHubMarketplaceOrder`).

### Payment Services (lifecycle owner)
- `IPaymentLifecycleService` (`PaymentLifecycleService`, ~6877 lines) — the authoritative payment
  state machine. All status transitions, journal outbox enqueue, inventory commit/release, coupon/
  offer confirmation, suspense routing, quarantine decisions, settlement, refunds, idempotency.

### Provider Adapters
- Stripe: intent-based, multi-tenant config, webhooks are sole truth, idempotent, secrets decrypted
  on demand, client_secret encrypted at rest (`ClientSecretEncrypted`).
- Paymob: HMAC-verified webhook + callback, refunds reconciled cumulatively (provider refunded amount
  minus local `OrderRefunds`), idempotency key includes refund suffix when `IsRefunded`.
- Fawry: reference/offline payment adapter; maps provider status REFUNDED/PARTIAL_REFUNDED; runtime
  bridge used for evidence/advanced operations.
- COD: no provider session; lifecycle driven by delivery + shipping settlement.
- ManualWallet: proof upload + admin verification (foundation/runtime bridge + evidence adapter).

### Payment Policies / Authority
- `PaymentStatusAuthority.IsPaidState` — paid-state check.
- `CanonicalPaymentStatusResolver` — internal → storefront contract mapping (terminal states win).
- `OrderOperationalFinancialAuthority` — gating outbound promotion on paid + posted collection
  accounting (used by order/fulfillment, not payment directly).
- `PaymentRuntimeFeatureFlags` — feature flags; ALL OFF by default (runtime is dormant in prod).

### Entities (SQL truth)
- `PaymentIntent` (child of Order), `Order.PaymentStatus`, `OrderRefund`/`OrderRefundLine`,
  `PaymentSettlementBatch`/`PaymentSettlementOrder`, `PaymentWebhookLog`,
  `PaymentCancellationOutboxMessage`, `CommerceHubUnresolvedPaymentWebhook`, `TenantPaymentMethod`.
- CommerceHub: `CommerceHubPayment`, `CommerceHubPaymentItem`, `CommerceHubPaymentAttempt`,
  `CommerceHubPaymentLog`, `CommerceHubRefund*`, `CommerceHubVendorPaymentRequest/Proof/Method/AuditLog`.

### Database / Outbox
- PostgreSQL = single source of truth. `AccountingOutboxMessage` = transactional outbox for journal
  postings (enqueued inside the payment tx). `PaymentCancellationOutboxMessage` = deferred inventory
  release for canceled paid orders. MassTransit Outbox publishes integration events in same tx.
- Background: `AccountingOutboxWorker`, `PaymentCancellationOutboxWorker`,
  `StorefrontUnresolvedPaymentReconciliationJob`, `CommerceHubUnresolvedPaymentReconciliationHostedService`,
  `ManualPaymentOperationalReadiness`, proof-image retention cleanup.

### Mongo / Redis / MassTransit
- Mongo/Redis/Elastic = derivative read models (SQL-recomposed). MassTransit outbox = event delivery.
- Consumers: `AffiliatePaymentCapturedConsumers` (OnlinePaymentCaptured/CodDelivered/WalletApproved),
  `WhatsAppPayment*Consumer`, `PaymentMethodProjectionConsumer`, analytics/authoritative-purchase
  consumers. All are evidence/downstream, not owners.

---

## Business Rules (implementation-backed)

1. **Lifecycle is the only writer.** `Order.PaymentStatus` direct assignment is blocked by
   `PaymentStatusSaveChangesInterceptor`; mutations go through `IPaymentLifecycleService` or an
   authorized context scope.
2. **Revenue recognized only at Collected.** Journal DEBIT clearing / CREDIT revenue on collection.
   Authorized, AwaitingVerification, Rejected, Failed, Expired, AmountMismatch create NO journal.
3. **Provider amount/currency authoritative for webhooks.** For `GatewayWebhook`/`StripeWebhook`/
   `PaymobWebhook` sources, missing/invalid `ProviderAmount`/`ProviderCurrency` → quarantine
   (`AmountMismatch`/`ManualReview`). Intent Amount/Currency are ignored for those sources.
4. **Non-webhook sources use intent amount.** COD delivery, wallet approval/rejection, admin
   cancellation, reservation cleanup use Amount/Currency (provider fields optional, take precedence
   if present).
5. **Idempotency / replay collapse.** Webhook idempotency keys; `PaymentIntent.IdempotencyKey`;
   terminal-state replay returns `WasIdempotent=true, StateChanged=false`; duplicate refunds detected
   by hash + amount + currency; settlement duplicate with identical content is a no-op, with
   differing content is rejected (`duplicate_settlement_mismatch`).
6. **Fail closed on mismatch.** Late provider success after inventory release, or after admin
   cancellation queued, or after reservation expired → quarantine for manual reconciliation
   (`LATE_PROVIDER_SUCCESS_RECONCILIATION_REQUIRED`, `ADMIN_CANCELLATION_PENDING_RECONCILIATION_REQUIRED`).
7. **COD gating.** `MarkCodDeliveredAsync` requires `FulfillmentStatus == Delivered`; settlement
   requires all orders `Collected` and gross amount == Σ order total (currency-matched), tenant-matched,
   non-duplicate.
8. **Settlement membership authority = `PaymentSettlementOrder` lineage**, NOT the batch `OrderCount`
   denormalized field (which is metadata only). Mismatch fails closed.
9. **Refund rules.** Allowed from `Collected/Settled/PartiallyRefunded`. Refund amount must be > 0,
   ≤ remaining refundable (with `CurrencyHelper` tolerance). Full refund when `alreadyRefunded +
   this >= refundable - tolerance`. Post-settlement refunds use a different accounting context.
   Refund reverses affiliate commission (`ReconcileAffiliateCommissionReversalsForRefundAsync`).
10. **Inventory commit on collection, release on failure/expiry.** `InventoryCommitted` flag on intent
    prevents double-commit on webhook retry; reservation commit is recoverable on replay.
11. **Coupon/offer usage confirmed on success, released on failure/cancellation.** Confirmed via
    `_couponValidationService.ConfirmCouponUsageAsync` / `ConfirmOfferPromotionUsageAsync`.
12. **Tenant isolation mandatory.** `PaymentIntent.TenantId` NOT NULL; webhook tenant resolved from
    stored record; supplied header untrusted; cross-tenant reads return 404/empty.
13. **Wallet proof retention.** Manual wallet proof image expires (`ProofImageExpiresAtUtc`) and is
    hard-deleted by cleanup; admission/approval validate expiry + retention authority.
14. **Manual review is not terminal money.** `AwaitingVerification`/`ManualReview`/`Quarantined`/
    `ReconciliationRequired` block fulfillment and recognition until resolved.
15. **Accounting posted before/with mutation.** Settlement and refund paths call
    `EnsureCollectionAccountingPostedFor*` to guarantee the collection journal exists before settling/
    refunding (fail closed if missing).

---

## Payment State Machine (implementation)

### Order-level `PaymentStatus` (PRODUCTION — `PointOfSale.Core.Enums.PaymentStatus`)
The doc "lifecycle vocabulary" lists `CashOnDelivery`, `PartiallyPaid`, `Paid` as live states.
Implementation check: `CashOnDelivery` is **NOT assigned anywhere in production code** (only appears
in a migration column name `IsCashOnDelivery`, unrelated to this enum). `Paid` and `PartiallyPaid`
are `[Obsolete]` aliases of `Collected` (same value 2) / legacy and are not the active target of
new transitions. The active machine is:

```
Pending
  → Authorized        (online 2-step pre-auth; no journal)
  → Collected         (online capture / COD delivered / wallet approved)  [JOURNAL]
  → Failed            (terminal; no journal; release reservation)
  → Rejected          (wallet rejected; no journal)
  → Expired           (window passed; no journal; release reservation+coupon)
  → AmountMismatch     (quarantine; manual review; no journal)

Authorized
  → Collected         (capture)  [JOURNAL]
  → Failed / Expired  (terminal)

AwaitingVerification (wallet)
  → Collected         (admin approve)  [JOURNAL]
  → Rejected          (admin reject; no journal)

Collected
  → Settled           (gateway/COD settlement)  [JOURNAL]
  → Refunded          (full refund)  [JOURNAL]
  → PartiallyRefunded (partial refund)  [JOURNAL]
  → Failed / Expired  (only via late-quarantine / cancellation reconciliation)

Settled
  → Refunded / PartiallyRefunded   [JOURNAL]

PartiallyRefunded
  → Refunded          (subsequent full refund)  [JOURNAL]

Refunded / Failed / Rejected / Expired / AmountMismatch = terminal (money-recognized only for Refunded).
```

### Intent-level `PaymentIntentStatus`
`Pending → Authorized → Paid`; `Pending → AwaitingVerification → (Collected intent)`; `Pending →
Skipped` (COD); `→ Failed / Cancelled / Expired / Rejected / AmountMismatch / Suspicious`.

### CommerceHub `CommerceHubPaymentRecordStatus`
`Pending → Paid / Failed / Ignored / RefundRequired / Refunded / RefundFailed` (separate authority,
EGP-only). `NeedsManualCurrencyReview` flag exists for currency mismatches.

### Dormant runtime state machine (`Core.Payments.Runtime.Domain.PaymentStatus`)
A SECOND, much larger `PaymentStatus` enum (27 states: Created, ActionRequired, AwaitingCustomer,
AwaitingProvider, Confirmed, Captured, Approved, Completed, ManualReview, Recovery, SettlementPending,
RefundPending, Verified, …) plus `PaymentStateTransitionRuntime` (legal-transition table, replay/
ownership/uniqueness guards) exists in `Core.Payments.Runtime`. **It is gated behind
`PaymentRuntimeFeatureFlags` whose defaults are ALL FALSE** (`NewPaymentRuntime`, `NewPolicyEngine`,
`DualRunPayments`, `ReplayEngine`, `ProviderAdapterV2` = false). In production the runtime is
**dormant**; the `Enums.PaymentStatus` + `IPaymentLifecycleService` machine above is authoritative.
The runtime is consumed via provider "bridge"/"evidence adapter" wrappers (Fawry, Paymob, Manual)
but does not drive production order `PaymentStatus`.

---

## Provider Discovery (per implementation)

### Stripe (`PaymentProvider.Stripe`)
- Responsibilities: intent creation (`CreatePaymentIntentAsync`), status inquiry, webhook processing
  (`ProcessWebhookAsync`, `Stripe-Signature` HMAC). Multi-tenant config resolver.
- Authority: webhooks are SOLE truth for status. Client secret encrypted at rest; decrypted on demand.
- Capabilities: redirect/embedded intent, async confirmation, refunds via webhook.
- Verification: `Stripe-Signature` header; tenant resolved from stored record.
- Webhook behavior: idempotent; 200 on duplicate; 500 on unresolved (retry).
- Refund support: yes (provider refund reconciled through lifecycle).
- Settlement: net bank deposit via `RecordGatewaySettlementAsync`.
- Failure handling: stale failure after success collapsed; invalid signature → 401.

### Paymob (`PaymentProvider.Paymob`)
- Responsibilities: intent (`CreatePaymentIntentAsync`), status, HMAC webhook + callback
  (`ValidateCallbackAsync`), refund reconciliation.
- Verification: HMAC over canonical payload (`VerifyHmacSignature`/`VerifyCallbackHmac`).
- Refund: **cumulative delta** = provider refunded amount − local `OrderRefunds` already recorded;
  only reconciles when `CanReconcileProviderRefund` (Collected/Settled/PartiallyRefunded/Refunded).
  Idempotency key adds refund suffix when `IsRefunded`.
- Settlement: gateway settlement batch.
- Failure: late/voided/refunded flags respected; duplicate webhook collapsed.

### Fawry (`PaymentProvider.Fawry`)
- Responsibilities: reference/offline payment init (`InitializePaymentAsync`), status mapping
  (REFUNDED → Refunded, PARTIAL_REFUNDED → PartiallyRefunded), runtime bridge + evidence adapter.
- Authority: offline reference code; customer pays at Fawry; status inquiry / callback drives lifecycle.
- Verification: provider status mapping; runtime evidence adapter wraps outcomes.
- Refund: provider-driven status mapping.
- Settlement: gateway settlement.

### COD (`PaymentProvider.Cod`)
- No provider session. Intent status `Skipped`. Lifecycle driven by delivery (`MarkCodDeliveredAsync`,
  requires `FulfillmentStatus == Delivered`) then shipping-company deposit (`RecordCodSettlementAsync`).
- Authority: POS/delivery operator + shipping settlement file.

### Manual Wallet (`PaymentProvider.ManualWallet`)
- Responsibilities: customer uploads proof (Vodafone Cash/Instapay/etc.); admin verifies.
- Flow: `CreateManualWalletPaymentIntentAsync` (Pending) → `AdmitManualWalletReviewAsync`
  (AwaitingVerification) → `ApproveWalletPaymentAsync` (Collected, journal) / `RejectWalletPaymentAsync`
  (Rejected, no journal).
- Authority: admin decision; proof image retention/expiry enforced; suspense/quarantine on mismatch.
- Verification: admin review + proof retention policy. NOT automatic provider confirmation.

### Shared behavior
- All providers converge to `IPaymentLifecycleService` — the provider never writes `Order.PaymentStatus`
  directly.
- `X-Tenant-Id` header is never trusted for resolution (mismatch check only).
- Provider callbacks = evidence; amount/currency from provider are authoritative for online webhooks.

---

## Cross-System Effects

- **Orders:** collection advances `Order.Status` (Pending→Confirmed for online; COD adds Delivered on
  delivery). `Order.IsPaid`, `PaidAt` set on collection. Order status is DERIVED by order lifecycle
  from payment + fulfillment; payment does not own order status.
- **Checkout:** creates the `PaymentIntent`; Redis cart is NOT authority. Re-checkout/idempotency
  resumes the existing intent.
- **Inventory:** collection commits reservations (`CommitOrderReservationsAsync`); failure/expiry/
  cancellation releases them. `InventoryCommitted` flag prevents double-commit on retry.
- **Pricing/Coupons:** coupon + offer usage confirmed on success, released on failure/cancel/expiry.
- **Customers:** customer sees payment in order history via storefront projection; canonical status
  maps to PAID/PROCESSING/MANUAL_REVIEW/FAILED/PENDING.
- **Storefront:** read models recomposed from SQL; provider/browser outcomes are non-final.
- **CommerceHub:** separate payment authority (EGP). POS Admin vendor surface submits intent;
  CommerceHub payment services own truth. Unresolved CommerceHub webhooks reconciled by a dedicated
  hosted service. CommerceHub currency mismatch → `NeedsManualCurrencyReview`.
- **Affiliate:** capture events (`OnlinePaymentCapturedEvent`, `OrderCodDeliveredEvent`,
  `WalletPaymentApprovedEvent`) trigger commission; refund reverses commission. Affiliate owns
  commission, not payment.
- **Accounting:** every money-recognizing transition enqueues an `AccountingOutboxMessage` in the same
  tx (OnlinePayment, EcommerceRefund, GatewaySettlement, CodSettlement). Posting is best-effort;
  outbox retries.
- **Analytics:** purchase/authoritative-analytics consumers read capture events.
- **Notifications:** WhatsApp payment approval/rejection/requested consumers (downstream evidence).
- **Shipping:** COD settlement depends on shipping-company deposit; COD delivery gates collection.
- **Identity:** none owned by payment; tenant/actor resolved server-side.

---

## Validation Checklist (operational)

After **authorization**: order `PaymentStatus=Authorized`, intent `Authorized`, `AuthorizedAt` set,
no journal, inventory still reserved (not committed), checkout state `PaymentPending`.

After **collection (online/COD/wallet)**: `PaymentStatus=Collected`, intent `Paid`, `CapturedAt`,
`IsPaid=true`, `PaidAt`, journal posted (outbox `PROCESSED`), inventory committed
(`InventoryCommitted=true`), coupon/offer confirmed, order advanced, `OnlinePaymentCapturedEvent`
published, accounting outbox enqueued.

After **settlement**: `PaymentStatus=Settled`, `PaymentSettlementBatch` + `PaymentSettlementOrder`
lineage present (NOT just `OrderCount`), settlement journal posted, `GatewaySettlementCompletedEvent`/
`CodSettlementCompletedEvent` published.

After **refund**: `OrderRefund` + `OrderRefundLine` rows, new `PaymentStatus=Refunded` (full) or
`PartiallyRefunded`, refund journal posted (pre/post-settlement context), `OrderRefundedEvent`
published, affiliate commission reversal reconciled, idempotency key prevents duplicate.

After **partial refund**: `PartiallyRefunded`; subsequent full refund flips to `Refunded`; remaining
refundable approaches 0 within tolerance.

After **failure**: `PaymentStatus=Failed` (or Rejected/Expired), no journal, reservation released,
coupon released, `OnlinePaymentFailedEvent`/`WalletPaymentRejectedEvent` published, intent terminal.

After **expiration**: `Expired`, reservations + coupons released, no journal, intent terminal.

After **COD confirmation**: requires `FulfillmentStatus=Delivered`; then `Collected`.

After **manual wallet verification**: `AwaitingVerification` after admit; `Collected` after approve
(journal) or `Rejected` after reject (no journal); proof retention validated.

After **webhook processing**: `PaymentWebhookLog` `ProcessingStatus=Completed`, signature verified,
idempotency key present; duplicate → 200 no-op; unresolved → suspense routing + reconciliation job.

---

## Common Failure Scenarios

1. **Payment succeeded but order not updated** — Cause: webhook 500 / outbox not committed / replay
   collapsed incorrectly. Investigate: `PaymentWebhookLog` (ProcessingError), `PaymentIntent` vs
   `Order.PaymentStatus`, accounting outbox status. First look: webhook log + lifecycle replay guards.
2. **Duplicate payment** — Cause: webhook retried without idempotency, or two providers. Investigate:
   idempotency key, `PaymentIntent.ProviderPaymentId`, lifecycle `WasIdempotent` flag.
3. **Duplicate webhook** — Expected no-op (200). If double-posted: check idempotency key + intent state.
4. **Wrong payment status** — Cause: stale frontend cache, or terminal-state resurrection attempt.
   Investigate: SQL `Order.PaymentStatus` truth; frontend re-fetches backend.
5. **Settlement missing** — Cause: batch not created, or `PaymentSettlementOrder` lineage missing while
   `OrderCount` set. Investigate: `PaymentSettlementBatch` + `PaymentSettlementOrder`; NOT `OrderCount`.
6. **Accounting missing** — Cause: outbox failed to post, or collection journal not enqueued before
   settlement/refund. Investigate: `AccountingOutboxMessage` status; `EnsureCollectionAccountingPosted*`.
7. **Refund failed / partially applied** — Cause: amount > remaining refundable, or partial then full.
   Investigate: `OrderRefund`/`OrderRefundLine`, `ResolveRefundableAmount` vs `ResolveAlreadyRefundedAmount`,
   tolerance.
8. **Amount/currency mismatch** — Cause: provider amount ≠ intent. Result: `AmountMismatch`/`Suspicious`
   quarantine (manual review). Investigate: provider `ProviderAmount`/`ProviderCurrency` vs intent.
9. **Webhook replay** — Cause: provider retry. Expected: collapsed via decision id / idempotency.
   Investigate: `PaymentWebhookLog.WebhookEventCount`, lifecycle replay guards.
10. **Provider timeout** — Cause: provider slow. Payment stays `Pending`/`Authorized`; reservation
    cleanup may expire it (`MarkPaymentExpiredAsync`).
11. **CommerceHub mismatch** — Cause: EGP vs tenant currency, or `NeedsManualCurrencyReview`.
    Investigate: `CommerceHubPayment.Currency`, `NeedsManualCurrencyReview`, unresolved webhook table.
12. **Affiliate not triggered** — Cause: capture event not published, or commission service down.
    Investigate: `OnlinePaymentCapturedEvent` published? Affiliate consumer ran? (Affiliate owns
    commission; payment only emits the trigger.)

---

## Debugging Guide

- **Start:** SQL `Order.PaymentStatus` + `PaymentIntent` (state, amounts, provider ids, `InventoryCommitted`).
  This is the single source of truth.
- **SQL when:** any status dispute, missing journal suspicion, settlement/refund lineage question.
- **PaymentIntent when:** tracing which provider/attempt, webhook counters, proof fields, expiration.
- **Accounting when:** missing/duplicate journal, settlement/refund posting. Check `AccountingOutboxMessage`
  (status, idempotency key) + `EnsureCollectionAccountingPosted*` paths.
- **Provider logs when:** signature failure (401), provider amount mismatch, webhook 500, replay.
  Check `PaymentWebhookLog` (SignatureVerified, ProcessingError, WebhookEventCount).
- **Webhooks when:** payment not updated though provider says paid. Check controller 200/401/500,
  idempotency, suspense routing, `StorefrontUnresolvedPaymentReconciliationJob`.
- **Outbox when:** event not propagated to Affiliate/WhatsApp/Analytics/Search. Check MassTransit
  outbox + `AccountingOutboxMessage`.
- **Mongo when:** customer/storefront shows stale/wrong payment. Recompose from SQL; missing Mongo doc
  is NOT proof the order/payment doesn't exist. Check SQL-recomposition path.
- **Storefront when:** UI shows wrong status. Confirm `CanonicalPaymentStatusResolver` mapping and
  that the DTO re-fetched backend truth (frontend cannot fabricate lifecycle).

---

## Related Knowledge

Core Guides / System Guides (repo):
- `.ai/hermes/capabilities/pos-admin/order-management.md` (four state machines, cancellation, shipment→order derivation)
- `.ai/hermes/capabilities/pos-admin/inventory-management.md` (reservation commit/release, cleanup fail-closed)
- `.ai/hermes/capabilities/pos-admin/pricing-management.md` (coupon/offer confirmation, currency rounding)
- `.ai/hermes/capabilities/pos-admin/product-management.md`
- `.ai/hermes/systems/commercehub/README.md` (CommerceHub payment boundary, EGP)
- `.ai/hermes/systems/affiliate/README.md` (commission trigger, reversal)
- `.ai/hermes/systems/storefront/README.md` (projection, canonical status)
- `.ai/hermes/systems/pos-admin/README.md`

Related Capabilities / Domains:
- Order Management, Inventory Management, Checkout, Pricing, CommerceHub Finance, Affiliate,
  Accounting/Settlement, Notifications (WhatsApp), Search/Analytics.

---

## Documentation Validation (implementation vs docs/architecture)

Only implementation-backed findings:

1. **PaymentStatus vocabulary mismatch (HIGH).** `docs/architecture/payment-lifecycle-authority-certification-findings.md`
   lists live states `CashOnDelivery`, `PartiallyPaid`, `Paid` as part of the active vocabulary.
   Implementation (`PointOfSale.Core.Enums.PaymentStatus`): `CashOnDelivery` is **never assigned** in
   production code (only an unrelated migration column `IsCashOnDelivery`); `Paid`/`PartiallyPaid` are
   `[Obsolete]` aliases of `Collected` (value 2) / legacy. The active production machine uses
   Pending/Authorized/Collected/Settled/Refunded/PartiallyRefunded/AwaitingVerification/Rejected/Failed/
   Expired/AmountMismatch. Doc over-states legacy/dormant states as live.

2. **Second dormant PaymentStatus enum undocumented in the lifecycle doc (MEDIUM).** A 27-state
   `Core.Payments.Runtime.Domain.PaymentStatus` + `PaymentStateTransitionRuntime` exists but is gated
   behind `PaymentRuntimeFeatureFlags` (all FALSE). It is not mentioned in the lifecycle findings as
   dormant; a reader could mistake it for the active machine. (The onboarding skill notes the runtime
   is "certified but currently DISABLED" — this is confirmed: all feature flags default false.)

3. **Settlement membership authority (HIGH, already known conflict).** Findings elsewhere state
   `PaymentSettlementBatch.OrderCount` is metadata, live `PaymentSettlementOrder` lineage is authority.
   Confirmed in `RecordGatewaySettlementAsync`/`RecordCodSettlementAsync` (both validate via lineage,
   gross amount, and reject `duplicate_settlement_mismatch`). Consistent with the known PASS-4 override.

4. **CommerceHub currency (HIGH, confirmed).** `CommerceHubPayment.Currency` defaults to `"EGP"` and
   `RequireCommerceHubPaymentCurrency` enforces EGP-sovereign; tenant `Tenant.Currency` cannot override.
   Findings confirm. No discrepancy, but worth restating: POS Admin tenant currency ≠ CommerceHub EGP.

5. **Amount/currency authority for webhooks (consistent).** Implementation quarantines missing
   provider amount/currency for webhook sources; doc "PAYMENT-CON" intent matches. Confirmed.

6. **"Manual review is not terminal money" (consistent).** `AwaitingVerification`/`ManualReview`/
   `Quarantined`/`ReconciliationRequired` block recognition. Confirmed by lifecycle + resolver logic.

7. **Hidden behavior not surfaced in docs (MEDIUM):** late-provider-success-after-release and
   admin-cancellation-pending quarantines (`LATE_PROVIDER_SUCCESS_RECONCILIATION_REQUIRED`,
   `ADMIN_CANCELLATION_PENDING_RECONCILIATION_REQUIRED`) are implemented fail-closed paths that the
   prose findings mention only generically; the exact codes/fields are implementation-only.

8. **Webhook 500 vs 200 semantics (consistent, important):** controller returns 200 only for
   processed/duplicate, 401 invalid signature, 500 unresolved (provider retries). Doc "fail closed"
   stance matches. Confirmed.

No architecture contradictions found that would change operational decisions beyond #1, #2, #3.

---

## End-of-task attestation

- Created ONLY: `.ai/hermes/capabilities/pos-admin/payment-management.md`
- No other files modified, created, or deleted.
- No memory writes performed.
- No code generated, no features implemented, no refactors.

### Summary of discoveries
Payment Management owns tenant order `PaymentStatus` truth through `IPaymentLifecycleService`
(webhook/COD/wallet/convergence), with PostgreSQL as the single source of truth, a transactional
accounting outbox for journal postings, and fail-closed quarantine for amount/currency mismatches and
late successes. Providers (Stripe, Paymob, Fawry, COD, ManualWallet) converge to the lifecycle owner;
none write `PaymentStatus` directly. CommerceHub and Affiliate are separate authorities triggered by
capture events. The new runtime state machine is dormant (feature flags off).

### Payment lifecycle discovered
Checkout → PaymentIntent (Pending) → provider session/webhook → Authorized → Collected (journal +
inventory commit + coupon confirm + order advance + events) → Settled (batch) → Refunded/
PartiallyRefunded. COD adds delivery + shipping settlement; ManualWallet adds admin verification.

### Payment state machine discovered
PRODUCTION = `PointOfSale.Core.Enums.PaymentStatus` (Pending/Authorized/Collected/Settled/Refunded/
PartiallyRefunded/AwaitingVerification/Rejected/Failed/Expired/AmountMismatch; Paid/PartiallyPaid
obsolete). DORMANT = `Core.Payments.Runtime.Domain.PaymentStatus` (27 states, feature-flagged off).

### Providers discovered
Stripe (intent+HMAC, webhooks sole truth), Paymob (HMAC+cumulative refund reconciliation), Fawry
(offline reference + runtime bridge), COD (delivery+shipping settlement), ManualWallet (proof upload +
admin verify). All converge to lifecycle owner.

### Business rules discovered
Lifecycle-only writer; revenue at Collected; provider amount/currency authoritative for webhooks;
idempotency/replay collapse; fail-closed quarantine on mismatch/late-success/cancellation-pending;
COD delivery+settlement gating; settlement lineage authority; refund amount/tolerance + affiliate
reversal; inventory commit/release; coupon confirm/release; tenant isolation; wallet proof retention;
accounting posted before settlement/refund.

### Cross-system dependencies discovered
Orders (derived status), Checkout (intent), Inventory (commit/release), Pricing (coupon/offer),
Customers/Storefront (projection), CommerceHub (EGP boundary), Affiliate (capture trigger + reversal),
Accounting (outbox), Analytics/Notifications/Search (event consumers).

### Documentation inconsistencies found
(1) `CashOnDelivery`/`PartiallyPaid`/`Paid` listed as live but are obsolete/dormant; (2) dormant 27-state
runtime enum not flagged as disabled in lifecycle doc; (3) settlement `OrderCount` vs lineage (known,
confirmed); (4) CommerceHub EGP confirmed; (5) late-success/admin-cancel quarantine codes are
implementation-only hidden behavior.

### Hidden implementation behavior found
- Late-provider-success and admin-cancellation-pending quarantines with specific failure codes.
- `InventoryCommitted` flag prevents double-commit on webhook retry.
- `WasIdempotent`/`StateChanged` replay collapse in lifecycle.
- Webhook `X-Tenant-Id` untrusted; tenant resolved from stored record.
- `AccountIsPaid = Items.All(IsFullyPaid)` computed at collection.
- Suspense-ledger routing for unresolved webhooks + `StorefrontUnresolvedPaymentReconciliationJob`.
- Post-commit best-effort external accounting posting (outbox retries on failure).
- Runtime state machine + feature flags all default false (dormant in prod).

### Confirmation
Only `.ai/hermes/capabilities/pos-admin/payment-management.md` was created. No other files were
modified or created. No memory writes occurred.
