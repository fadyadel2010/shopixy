# Affiliate Participation Runtime — Final Canonical Architecture

> Implementation-backed reverse engineering of how independent Affiliates participate in a POS
> Admin storefront on Shopixy / PointOfSale.
>
> Implementation is the ONLY source of truth. Every claim below is cited to `file:line` in the
> backend C# projects. Prior markdown, certification docs, UI labels, controller names, and DTO
> names were used only to locate code, then cross-checked against the implementation. Where a
> prior assumption contradicted code, the code wins and the contradiction is recorded in
> "Documentation Validation" at the end.

---

## Runtime Classification (Thesis)

This runtime is fundamentally an **Affiliate Participation Runtime** — a hybrid of an
*Affiliate Identity Runtime* (global, tenant-unbound), a *Referral/Attribution Runtime*
(storefront-admitted, first-click-locked), a *Commission Runtime* (event-observed, snapshot-priced),
and a *Financial Payout Runtime* (fail-closed, outward-money-gated), unified by one governing
principle:

> An **independent external actor** (the Affiliate) is **admitted into** a POS Admin storefront
> strictly as a *referral source*. The store retains ownership of the order, payment, fulfillment,
> customer, and accounting. The Affiliate never owns or mutates any of those. Its only write
> authority is its own identity, its own tracking artifacts, and its own append-only money ledgers.
> Everything it "knows" about commerce arrives as events it observes, never as state it controls.

This is **not** a "Referral Marketing feature bolted onto the store," and **not** a "multi-tenant
sub-tenant." It is a *participation* boundary: a global principal that can legally interact with
many stores, but whose authority inside any single store is limited to (a) planting a referral
cookie, (b) claiming attribution on orders the store actually created, and (c) drawing down its
own earned, approved, store-scoped commission — and only when the whole financial backbone proves
healthy (fail-closed).

The single design decision that dictates the entire boundary shape is **Global Affiliate +
Store-Scoped Money (AF-19 / AF-19B)**:

- `AffiliateAccount` is **global** — `TenantId` is audit-only post-AF-19; there is no effective
  tenant binding (`AffiliateAccount.cs:83-105`). One account per `UserId`; `ReferralCode` is
  globally unique and immutable.
- Every financial ledger row (`AffiliateCommissionLedger`, `AffiliateBalanceLedger`) carries
  `TenantId = NULL` (global) and a `StoreTenantId` = the store that OWES the commission
  (`AffiliateCommissionLedger.cs:25`, `:42-52`, `:143-144`).
- Consequence: one affiliate earns across many stores; the money is always attributed to
  `StoreTenantId`, never to the affiliate's "home" tenant.

---

## Executive Summary

The Affiliate Participation Runtime is **one complete bounded context** (not a thin portal) that
owns affiliate identity, referral tracking, click/order attribution, commission calculation,
commission approval, balance accounting, payout request, payout execution, and the derivative
analytics/search projections. Its authoritative state is a family of **append-only PostgreSQL
ledgers**; MongoDB and Elasticsearch are derivative read models only.

Key implementation-backed facts:

1. **The Affiliate is a separate identity from the POS `User`/`Customer`.** `AffiliateUser` is a
   distinct principal 1:1 with `AffiliateAccount`, with its own JWT scheme
   (`AffiliateJwtSettings`), its own `IPasswordHasher<AffiliateUser>`, and `TokensValidAfterUtc`
   revocation. It never shares a JWT or password hash with the POS app
   (`AffiliateUser.cs:9-20`, `:65`, `:126-140`; `AffiliateAuthController.cs:60-69`, `:501-503`).
2. **POS Admin owns the affiliate lifecycle as a governance action.** The storefront/admission
   path only *admits* the affiliate's referral; suspension, activation, closure, and payout are
   administered by POS Admin / SuperAdmin surfaces (`AdminAffiliateController.cs:35-36`).
3. **Referral is first-click-locked and storefront-admitted.** The `AFF_ATTR` evidence cookie is
   written once and never overwritten by a later `?ref=` on the same cookie
   (`AffiliateReferralMiddleware.cs:489-510`). A promotion link is only admitted if its `TenantId`
   equals the current store (`AffiliateReferralMiddleware.cs:182-220`).
4. **Attribution is a gateway, not a side effect.** No `AffiliateAttribution` row → no commission,
   forever (`AffiliateAttribution.cs:9`, `:14`). Exactly one per `(TenantId, OrderId)` UNIQUE.
5. **Commission is computed from an immutable per-SKU snapshot at checkout**, never re-read at
   payout. Rate `(0.0001, 0.50]` is merchant-owned; the affiliate never sets it
   (`AffiliateProductPolicy.cs:121-124`; `AffiliateService.cs:1508-1584`).
6. **Money is fail-closed outward.** `AffiliateSettlementOrchestrator.EnsureAffiliateOutwardMoneyMovementReadyAsync`
   (`AffiliateSettlementOrchestrator.cs:1563`) blocks every payout unless the accounting outbox,
   integration outbox, payment-compensation outbox, marketplace outbox, and stock-sync outbox are
   healthy and no unresolved captured payment / refund exists.
7. **There is no dedicated fraud engine.** Self-referral, duplicate-click, velocity, blacklist,
   and risk-score controls are absent (verified by repo-wide search). The only fraud-adjacent
   controls are status governance, cookie tamper-deletion, first-click lock, payout-wallet
   snapshot, and HMAC external-transfer evidence.
8. **It observes commerce; it does not own it.** Orders, payments, fulfillment, refunds, the GL,
   and the treasury cashbox are written by their own lifecycle owners; the Affiliate runtime
   reacts through MassTransit consumers and posts GL via `IFinancialPostingService` — never writes
   `JournalEntry` directly (`AffiliateAccountingConsumer.cs:46-60`).

---

## Core Discovery — What Is an Affiliate?

| Question | Implementation answer | Evidence |
|---|---|---|
| Tenant? | **No.** Global, `TenantId` audit-only (`AffiliateAccount.cs:98`). | `AffiliateAccount.cs:83-105` |
| Customer? | **No.** Distinct principal; never the `Customer`/`OnlineCustomer` entity. | `AffiliateUser.cs:9-20` |
| User? | **No (not the POS `User`).** Owned by a POS `UserId` via FK, but authenticated through a separate `AffiliateUser` + separate JWT. | `AffiliateAccount.cs:23`, `AffiliateUser.cs:20-45` |
| Separate Entity? | **Yes.** `AffiliateAccount` (identity) + `AffiliateUser` (auth) are first-class aggregates. | `AffiliateAccount.cs:15`, `AffiliateUser.cs:20` |
| Identity Projection? | **No.** It is a real, independently authenticated identity with its own credentials, not a projection of a POS identity. | `AffiliateAuthController.cs:60-69` |
| External Actor? | **Yes.** An independent party who drives traffic to stores and is paid for attributed sales. Globally addressable across all stores. | `AffiliateCommissionLedger.cs:25,48` |

**Relationship to a POS Admin store:** The affiliate is *external to and independent of* any
single POS Admin tenant. It is **admitted** into a storefront's referral funnel (via a
store-scoped `AffiliatePromotionLink` or a global `ReferralCode`), earns **store-scoped** money
(`StoreTenantId`), and is governed by the store (POS Admin) for status and payout. The store is
the host; the affiliate is a permitted participant, not a member of the tenant.

---

## Runtime Purpose

Turn referral traffic driven by an independent global affiliate into **attributable, approvable,
payable, store-scoped commission**, while keeping the merchant store the sole owner of the order,
payment, fulfillment, customer, and accounting. The runtime exists to *monetize participation*
without *transferring authority* over the store's commerce to the participant.

---

## Runtime Scope / Authority / Ownership / Boundaries

### Runtime Owns (authoritative SQL writes)

- Affiliate identity: `AffiliateAccount`, `AffiliateUser`, `AffiliatePasswordResetToken`,
  `AffiliateEmailVerificationToken` (`PointOfSale.Core/Entities/Affiliate/`).
- Referral artifacts: `AffiliatePromotionLink`, `AffiliateProductPolicy`, `AffiliateCategory`.
- Attribution: `AffiliateAttribution` (gateway entity).
- Commission: `AffiliateCommissionLedger` (append-only, per-SKU).
- Balance: `AffiliateBalanceLedger` (append-only, account-level).
- Payout: `AffiliatePayoutRequest`, `AffiliatePayoutTransferEvidence`, `PayoutWallet` (VO).
- Mongo read models: `AffiliateDashboardDocument`, `AffiliateOrderDocument`,
  `AffiliateAnalyticsDailyDocument`, `AffiliateAnalyticsSummaryDocument`, `AffiliateClickDocument`,
  `AffiliateProductIntelligenceDocument`.
- Elasticsearch affiliate product-discovery index.

### Runtime Does NOT Own (intentionally left to other runtimes)

- Merchant `Product` lifecycle/pricing — reads a snapshot at commission time only.
- `Order`/`OrderItem` lifecycle — observes via events; stamps `AffiliateAccountId` /
  `AffiliateReferralCode` at checkout only.
- Payment capture / refunds — observes `OnlinePaymentCapturedEvent`, `OrderCodDeliveredEvent`,
  `WalletPaymentApprovedEvent`, `OrderItemRefundedEvent`.
- Fulfillment/delivery — observes `OrderDeliveredEvent` / `FulfillmentDeliveredEvent`.
- General Ledger — posts *through* `IFinancialPostingService`; never writes `JournalEntry`
  directly (`AffiliateAccountingConsumer.cs:46-60`).
- Treasury cashbox — resolves via `ICashboxLedgerAccountService`.
- CommerceHub marketplace orders — observes only via the readiness gate, never writes.
- Affiliate **status governance entry points** for the *store* — administered by POS Admin /
  SuperAdmin controllers (`AdminAffiliateController.cs`), which call into `IAffiliateService`.


---

## Identity Relationship (POS Admin ↓ Affiliate)

```
POS Admin (tenant)                Affiliate (global, independent)
─────────────────                ───────────────────────────────
owns Product/Pricing             owns ReferralCode (global, immutable)
owns Order/Payment/Customer      owns AffiliateAccount + AffiliateUser
admins Affiliate Status ----------> (Suspend/Activate/Close via AdminAffiliateController)
admits PromotionLink (store scope)
                                    earns StoreTenantId-scoped commission
                                    draws down own balance (fail-closed)
```

- **Same Identity?** No. The affiliate is a distinct, separately-authenticated principal.
- **Separate Identity?** Yes — `AffiliateUser` is 1:1 with `AffiliateAccount`, with its own JWT
  scheme and `PasswordHasher<AffiliateUser>` (`AffiliateUser.cs:9-20`, `:38-45`;
  `AffiliateAuthController.cs:60-69`, `:501-503`).
- **Binding?** A *soft* binding: `AffiliateAccount.UserId` FK → POS `Users.Id` (the human who
  registered), but the affiliate authenticates through `AffiliateUser`, not the POS `User`
  (`AffiliateAccount.cs:23`). The POS `UserId` is an ownership pointer, not a shared login.
- **Reference / Projection / Mapping?** Neither. It is a real identity, not a projection or a
  mapping table. There is, however, an *admission* mapping at referral time: a store-scoped
  `AffiliatePromotionLink` maps (store, affiliate, optional product) → the global account
  (`AffiliateReferralMiddleware.cs:172-345`).
- **Duplicate?** No. Exactly one `AffiliateAccount` per `UserId`; exactly one `AffiliateUser` per
  `AffiliateAccount` (`AffiliateAccount.cs:8-9`, `AffiliateUser.cs:9`).

---

## Authority Discovery (who owns what)

| Concept | Owner | Evidence |
|---|---|---|
| Affiliate Profile (identity) | Affiliate runtime (`AffiliateAccount`) | `AffiliateAccount.cs:15` |
| Affiliate Status | Affiliate runtime, *governed* by POS Admin/SuperAdmin | `AdminAffiliateController.cs:35-36`; `AffiliateAccount.cs:108-147` |
| Referral Code | Affiliate runtime (global, immutable) | `AffiliateAccount.cs:30,100` |
| Referral Attribution | Affiliate runtime (`AffiliateAttribution`) | `AffiliateAttribution.cs:14` |
| Commission | Affiliate runtime (`AffiliateCommissionLedger`) | `AffiliateCommissionLedger.cs:28` |
| Wallet (payout destination) | Affiliate runtime (affiliate-owned, admin-forbidden) | `AffiliateAccount.cs:153-167` |
| Orders | **Merchant / Order runtime** (affiliate only stamps fields) | `CheckoutService.cs:2139,5680-5682` |
| Customers | **Customer runtime** | observed only |
| Payments | **Payment lifecycle (`IPaymentLifecycleService`)** | observed via events |
| Payouts (execution) | Affiliate runtime (`AffiliateSettlementOrchestrator`) | `AffiliateSettlementOrchestrator.cs:109` |
| Financial Metrics (GL) | **Treasury/Accounting** (affiliate posts via service) | `AffiliateAccountingConsumer.cs:46-60` |

---

## Affiliate Lifecycle (implementation-backed)

```
AffiliateAccount created (Pending)
   ↓  AdminAffiliateController / IAffiliateService.ActivateAffiliateAsync
Affiliate Active            (IsActive = Status == Active)
   ↓  Affiliate Referral Link generated (AffiliatePromotionLink) OR global ReferralCode used
Customer visits storefront via ?ref= / landing route
   ↓  AffiliateReferralMiddleware resolves + AFF_ATTR cookie (first-click locked)
Customer adds to cart; cart stamped with affiliate authority (CheckoutService priority 0/1/2)
   ↓  Order placed; Order.AffiliateAccountId / AffiliateReferralCode stamped
AffiliateOrderPlacedEvent
   ↓  AffiliateOrderAttributionConsumer → AffiliateAttribution (gateway, immutable, 1/order)
Payment captured / COD delivered / wallet approved / accounting posted
   ↓  RecordCommissionForOrderAsync → AffiliateCommissionLedger Earned (snapshot-priced)
Physical delivery + payment recognized + accounting posted
   ↓  ApproveCommissionForDeliveredOrderAsync → CommissionLedger Approved
Balance accrues (RawAvailable grows)
   ↓  Affiliate requests payout (AffiliatePayoutRequest, min 100)
PENDING → APPROVED (admin) → PAID
   ↓  AffiliateSettlementOrchestrator.ExecuteAffiliatePayoutAsync (fail-closed, Serializable)
CommissionLedger Paid + BalanceLedger Withdrawal + external HMAC evidence + GL Paid journal
Affiliate Suspended (admin) → RevokeSessions (TokensValidAfterUtc bump)
   ↓  Suspend(); Activate() to reactivate
Affiliate Closed (terminal) → Cancels earning; cannot reactivate
   ↓  Close()
Affiliate Removed — no soft-delete on ledgers; closure/status is the terminal state
```

---

## Runtime State Machines

### 1. AffiliateAccount.Status (identity lifecycle)

`Pending(0) → Active(1) → Suspended(2) → Active`; `Active → Closed(3)` terminal.

- `Activate()`: idempotent; rejects if `Closed` (`AffiliateAccount.cs:108-118`).
- `Suspend(reason)`: only from `Active`; idempotent (`AffiliateAccount.cs:120-132`).
- `Close(reason)`: only from `Active`; idempotent; terminal (`AffiliateAccount.cs:134-147`).
- `IsActive = Status == Active`. **No commission is earned unless Active**
  (`AffiliateAccount.cs:12,45`).

### 2. AffiliateCommissionLedger.EntryType (money lifecycle)

`Earned(0) → Approved(2) → Paid(3)`; `Reversed(1)` cancels earned and blocks Approved/Paid.
(`AffiliateCommissionLedger.cs:16-20`, `:98`.) Reversal carries the original `BaseAmount` for
proportional math (`:101-199`).

### 3. AffiliatePayoutRequest.Status (withdrawal lifecycle)

`Pending(1) → Approved(2) → Paid(4)`; `Rejected(3)`. Min threshold 100; amount ≤ available
(`AffiliatePayoutRequest.cs:8-9`, `:39`, `:94`, `:123-129`). Marking Paid creates a
`BalanceLedger.Withdrawal` (`:15-16`, `:90-116`).

### 4. Attribution existence (boolean gateway)

Created once at order placement; immutable; never updated; never expires
(`AffiliateAttribution.cs:14`, consumer `:90-155`). Absence is permanent and blocks commission.

---

## Affiliate Status (per the prompt's candidate list)

| Status | Exists? | Implementation |
|---|---|---|
| Pending | Yes | `AffiliateStatus.Pending = 0` (initial) (`AffiliateAccount.cs:189`) |
| Active | Yes | `AffiliateStatus.Active = 1`; the only earning state (`AffiliateAccount.cs:190,45`) |
| Suspended | Yes | `AffiliateStatus.Suspended = 2`; revokes all sessions (`AffiliateAccount.cs:191`, `:120-132`; `AffiliateUser.cs:126-140`) |
| Rejected | No (as status) | There is no "Rejected" account status. Payout requests have `Rejected(3)`, but the *account* does not. (Gap vs prompt assumption.) |
| Blocked | No | No "Blocked" enum value; `Suspended` + `Closed` cover it. |
| Archived | No | No "Archived" status; `Closed` is the terminal state. |
| Removed | No (soft) | No deletion; `Closed` is terminal. Ledgers are append-only and never deleted. |
| Disabled | No | No "Disabled" status; `Suspended` is the operative disable. |

**Implementation note:** the account status enum is exactly four values — `Pending, Active,
Suspended, Closed`. The prompt's eight candidate labels collapse to three operative states
(Pending / Active / Suspended) plus one terminal (Closed). "Rejected / Blocked / Archived /
Removed / Disabled" are not implemented as statuses.


---

## Referral Runtime (tracking entry)

Entry point: `AffiliateReferralMiddleware` (`AffiliateReferralMiddleware.cs`). **Event-driven
only; never blocks the request; swallows its own errors** (non-fatal, `:471-478` class behavior).
No SQL writes on the request path — it publishes `AffiliateLinkClickedEvent` and sets cookies.

- **Referral code resolution priority** (`AffiliateReferralMiddleware.cs:79-150`):
  1. `AFF_ATTR` cookie (validated evidence, "Cookie:first-click-protected") — wins if present/valid.
  2. `?ref=` query param.
  3. `X-Affiliate-Ref` header (SPA flow).
  4. `/p/{code}/{slug}` or `/api/storefront/landing/{code}/{slug}` route.
- **v2 flow:** resolves `AffiliatePromotionLink` (store-scoped, active, product-selectable) then
  bridges to `AffiliateAccountId` (`:172-345`). **Store admission:** a promotion link is admitted
  only if `link.TenantId == currentTenantId` (`:182-220`); otherwise rejected.
- **v1 fallback:** resolves `AffiliateAccount` by global `ReferralCode` + `Active` status
  (`:347-463`).
- **Cross-store:** affiliate's home store may differ from the order store; logged as
  `AF-19:CrossStore` (`:409-416`). The *money* is attributed to `StoreTenantId` regardless.
- **Cookie set:** `AFF_SESSION` (session id) + `AFF_ATTR` (signed Data-Protection evidence token,
  30-day, tenant-bound). `AFF_ATTR` is **evidence only**; checkout revalidates SQL authority
  (`:305-345`, `:486-509`).
- **Click event:** `AffiliateLinkClickedEvent` is marked `INonAuthoritativeIntegrationEvent`
  (`AffiliateEvents.cs`), PII (IP/UA) SHA-256 hashed before emit (`:316-317`).
- **Browser capture:** storefront `affiliateRefTracker.ts` writes `sessionStorage` key
  `affiliate_ref_code` (NOT localStorage, by design) and replays into the cart/checkout; the
  *server* middleware remains the authority.

---

## Attribution Runtime (the gateway)

Pipeline (storefront → SQL attribution row):

```
Visitor → Affiliate Link (?ref= / landing)
  → AffiliateReferralMiddleware resolves PromotionLink/Account (global, store-admitted)
    → AFF_ATTR evidence cookie set (first-click locked)
      → AffiliateLinkClickedEvent (non-authoritative, PII-hashed)
        → Storefront cart/checkout carries AffiliateAccountId + ReferralCode
          → CheckoutService stamps Order.AffiliateAccountId / Order.AffiliateReferralCode
            → AffiliateOrderPlacedEvent emitted (StoreTenantId-scoped)
              → AffiliateOrderAttributionConsumer → AffiliateAttribution row (immutable, 1/order)
```

- **Gateway rule:** *no `AffiliateAttribution` → no commission, ever* (`AffiliateAttribution.cs:9`).
- **Uniqueness:** exactly one per `(TenantId, OrderId)` UNIQUE (`AffiliateAttribution.cs:9,14,21`).
- **Store-tenant boundary guard:** consumer rejects the event if `TenantId` is missing or
  `TenantId != StoreTenantId` (`AffiliateOrderAttributionConsumer.cs:91-101`).
- **Required referral code:** event without `ReferralCode` is skipped (`AffiliateOrderAttributionConsumer.cs:108-114`).
- **Expiration:** attribution does **not** expire; permanent once created.
- **First-click vs last-click:** **FIRST-CLICK WINS.** `EnsureAttributionCookie` returns early if
  an `AFF_ATTR` cookie already exists (`:489-510`) — the first accepted referral is locked for the
  navigation session. **NO last-click override and NO multi-touch attribution are implemented.**
- **Synchronization / repair:** `AffiliateAttributionReconciliationHostedService` (BackgroundService,
  1-minute `PeriodicTimer`) calls `ReconcileMissingAttributionsAsync` to repair attributions missed
  by the event path (`AffiliateAttributionReconciliationHostedService.cs:7-32`).

---

## Commission Runtime

- **Calculation:** `CommissionAmount = min(BaseAmount × Rate, MaxPerItem)`, rounded to 4 dp
  (`AffiliateCommissionLedger.CreateEarned`, `:122-159`). `BaseAmount` = `OrderItem.PaidAmount` at
  calculation time (immutable snapshot, `:14`).
- **Authority:** SQL ledger is authority; rate snapshot taken at checkout
  (`AffiliateService.cs:1508-1584`, `:1541-1561`). **Merchant owns the rate**; affiliate never
  controls it. Rate range enforced `(0.0001, 0.50]` (`AffiliateProductPolicy.cs:121-124`).
- **Trigger (conversion):** payment capture / COD delivered / wallet approved / accounting posted →
  delegate to `RecordCommissionForOrderAsync` (`AffiliatePaymentCapturedConsumers.cs:36-464`).
- **Attribution gate:** attribution must exist or it throws to force retry/reconcile
  (`AffiliateService.cs:1392-1425`).
- **Approval:** gated on **physical delivery + payment recognized + accounting posted**
  (`EnsureAffiliateDeliveryApprovalAuthority`, `:2539-2609`); Serializable txn + `FOR UPDATE`
  row lock (`:2493-2537`).
- **Terminal-status skip:** `Cancelled`/`Refunded` orders skip commission (`:1427-1435`).
- **Refund handling:** `ReconcileRefundedCommissionEntriesAsync` creates proportional `Reversed`
  entries; `CalculateRefundCommissionReversalAuthority` never exceeds earned
  (`:1743-1932`); idempotent via cumulative idempotency key.
- **Chargeback handling:** driven by the same reversal/penalty ledger path (no dedicated chargeback
  consumer; refunds drive reversals).
- **Duplicate protection:** unique `IdempotencyKey` on every entry + 23505 catch-as-success.

---

## Financial Runtime

- **Affiliate Earnings** = `AffiliateCommissionLedger` (per-SKU record).
- **Affiliate Balance** = `AffiliateBalanceLedger` (account-level: Withdrawal / Adjustment / Bonus /
  Penalty; withdrawal amounts negative, `:14-22`, `:90-116`).
- **Wallet:** `PayoutWallet` value object on `AffiliateAccount` (AF-16/16B). Affiliate-owned;
  admin can never mutate it (`AffiliateAccount.cs:153-167`).
- **Payouts / Ledger:** unified in `AffiliateSettlementOrchestrator.ExecuteAffiliatePayoutAsync`
  (`:109`). Legacy Settlement (admin pays order) and Withdrawal Engine (affiliate requests) both
  call the same orchestrator (`:31-35`).
- **Balance formula (authoritative):** `RawAvailable = netApproved + payoutReductions +
  withdrawals(neg) − pendingRequests` (`AffiliatePayoutService.GetAvailableBalanceCoreAsync`,
  `:147-160`). `CommissionLedger.Paid` does **NOT** reduce available; only `BalanceLedger.Withdrawal`
  does (`:136-153`). Guards use `RawAvailable` (unclamped) to prevent over-withdrawal.
- **Payout execution pipeline** (mandatory order, inside `Serializable` retry loop,
  `MAX_RETRIES=3`, 40001 retry, `:151-186`): begin tx → validate available (RawAvailable) → create
  `BalanceLedger.Withdrawal` → mark `CommissionLedger.Paid` (FIFO, net of reversals) → auto-reconcile
  legacy requests → mark `PayoutRequest.Paid` → commit → publish `AffiliateCommissionPaidEvent`.
- **External transfer evidence:** `AffiliatePayoutTransferEvidence` — HMAC-SHA256 verified external
  transfer; one rail/reference can fund many allocations, never over-allocated
  (`AffiliatePayoutTransferEvidence.cs:83-107`). `RequireVerifiedExternalPayoutEvidence=true` in prod
  (`AffiliateSettlementOptions.cs:28`).
- **Accounting integration:** posts GL journals via `IFinancialPostingService` — Earned: Dr Expense
  (6110) / Cr Payable (2105); Paid: Dr Payable (2105) / Cr Cashbox. Earned journal MUST exist before
  Paid (out-of-order guard, `AffiliateAccountingConsumer.cs:21-33,46-60`).
- **Multi-currency:** EGP-platform discipline applies via `FinancialCurrencyGuard`; a currency
  conflict blocks payout (`AffiliatePayoutService.cs:208-223`). Ledger amounts are decimal,
  store-scoped.

---

## Orders Runtime Integration

- **Reads/Observes:** `Order`, `OrderItem` lifecycle via events only.
- **Writes (narrow, at checkout):** `CheckoutService` stamps `Order.AffiliateAccountId` and
  `Order.AffiliateReferralCode` from the resolved affiliate authority (`CheckoutService.cs:2139`,
  `:5680-5682`). This is an *admission stamp*, not order ownership.
- **Authority resolution priority at checkout** (`CheckoutService.cs:5592-5682`):
  - PRIORITY 0: AF-27A cart metadata (server-stamped, anti-tampering).
  - PRIORITY 1: `HttpContext.Items` set by `AffiliateReferralMiddleware`.
  - PRIORITY 2: request DTO `AffiliateReferralCode` (SPA checkout).
- **Cross-tenant guard:** if the resolved authority's `AuthorityTenantId != tenantId`, it is logged
  as a cross-tenant signal and the stamp is still applied but flagged (`CheckoutService.cs:5639-5661`).
- **Does NOT:** create orders, capture payments, ship, or refund. Those are Order/Payment/Fulfillment
  lifecycle owners.

---

## Identity Runtime Integration

- **Affiliate Authentication:** separate `AffiliateAuthController` + `AffiliateUser` JWT. Dedicated
  `AffiliateJwtSettings` (cryptographic boundary isolation, `AffiliateAuthController.cs:501-503`).
  Refresh tokens + `TokensValidAfterUtc` for revocation (`AffiliateUser.cs:65,126-140`).
- **Authorization:** `AffiliateUser` is the auth principal; the portal cannot mutate payout wallet
  from admin side (`AffiliateAccount.cs:153-167`). Admin governance actions use POS Admin auth.
- **Session / revocation:** `Suspend`/`Close` revoke ALL affiliate sessions by bumping
  `TokensValidAfterUtc` (`RevokeAffiliateAccountSessionsAsync`, `AffiliateService.cs:166-180`;
  `AffiliateUser.RevokeSessions`, `:126-140`).
- **Tenant resolution:** `ITenantContext.RequiredTenantId` resolves the store; `X-Tenant-Id` is
  stripped and never trusted. The affiliate identity itself is global; only its *money* and *admission*
  are store-scoped.

---

## Storefront Runtime Integration

- **Referral URLs / Landing:** `/p/{code}/{slug}` and `/api/storefront/landing/{code}/{slug}`
  (`AffiliateReferralMiddleware.cs:79-150`); SPA `AffiliateLandingPage.tsx` + `affiliateRefTracker.ts`.
- **Cookie persistence:** `AFF_ATTR` (HttpOnly, Secure, SameSite=Lax, 30d) + `AFF_SESSION`
  (`:489-531`). Storefront `sessionStorage` `affiliate_ref_code` seeds; server revalidates.
- **Tracking:** click → `AffiliateLinkClickedEvent` (non-authoritative, PII-hashed).
- **Checkout / Customer Journey:** cart stamped (priority 0/1/2); order stamped; SQL revalidated.
  The storefront never writes affiliate SQL truth — it only carries the referral into checkout.
- **Discovery:** `AffiliateProductDiscoveryService` (ES) indexes affiliate-enabled products for
  storefront discovery; SQL `AffiliateProductPolicy` is the write authority.


---

## Event Runtime

Authoritative events (`PointOfSale.Core/Events/Affiliate/`):

- `AffiliateOrderPlacedEvent` (aggregate-scoped, carries `StoreTenantId` + `TenantId`).
- `AffiliateAttributedEvent`, `AffiliateCommissionEarnedEvent`, `AffiliateCommissionReversedEvent`,
  `AffiliateCommissionApprovedEvent`, `AffiliateCommissionPaidEvent`.
- `AffiliateProductPolicyChangedEvent`, `AffiliateCategoryUpdatedEvent`,
  `AffiliateCategoryDeactivatedEvent`.
- `AffiliateLinkClickedEvent` — explicitly **`INonAuthoritativeIntegrationEvent`** (reporting only;
  never drives payouts).

Key lifecycle events from the prompt and their implementation:

- `AffiliateCreated` → `AffiliateAccount.Create` (Pending) + `AffiliateUser` registration.
- `ReferralTracked` → `AffiliateLinkClickedEvent` (non-authoritative).
- `OrderAttributed` → `AffiliateAttribution` row (gateway).
- `CommissionCreated` → `RecordCommissionForOrderAsync` (Earned).
- `CommissionApproved` → `ApproveCommissionForDeliveredOrderAsync` (Approved).
- `CommissionPaid` → `AffiliateSettlementOrchestrator` (Paid + Withdrawal + evidence).
- `AffiliateSuspended` / `AffiliateReactivated` → `Suspend()` / `Activate()` + session revocation.

---

## Persistence Runtime

- **SQL (PostgreSQL):** sole authoritative system of record for all write aggregates
  (`AffiliateAccount`, `AffiliateUser`, `AffiliatePromotionLink`, `AffiliateAttribution`,
  `AffiliateCommissionLedger`, `AffiliateBalanceLedger`, `AffiliatePayoutRequest`,
  `AffiliatePayoutTransferEvidence`, `AffiliateProductPolicy`).
- **Append-only ledgers:** `AffiliateCommissionLedger` and `AffiliateBalanceLedger` do **NOT**
  inherit `BaseEntity` (no `IsDeleted`, no `UpdatedAt`); DB triggers enforce immutability
  (`AffiliateCommissionLedger.cs:28-34`; `AffiliateBalanceLedger.cs` header).
- **Redis:** Used **only as a 30s read cache for the analytics summary endpoint**
  (`AffiliateAnalyticsController.cs:10,70,116,128`; misses non-fatal). The affiliate **write /
  settlement runtime does not use Redis** — grep of `Services/Affiliate` for
  `redis|idistributedcache|stackExchange|memorycache` returns **0 hits**. Balance, commission, and
  payout computations never touch Redis.
- **Mongo:** derivative read models only (dashboards, orders, analytics, clicks, product intel).
  Fed by consumers; idempotent via `ProcessedEventIds` + `AffiliateProjectionEventReceiptDocument`.
- **Cookies:** `AFF_SESSION` + `AFF_ATTR` (evidence). Cookie is evidence, not storage of truth.
- **Sessions:** request-scoped "Affiliate Mode" via `AffiliateModeResolver` (not persisted);
  `AFF_SESSION` provides continuity.
- **Elasticsearch:** affiliate product-discovery index only.
- **Read models vs write models:** SQL = write + critical-read authority; Mongo/ES = projections.
  Replays/repairs are idempotent (reconciliation host + receipts).

---

## CQRS Model

- **Commands (write):** register affiliate, activate/suspend/close, generate promotion link, update
  payout wallet, request payout, execute payout, mark paid.
- **Queries (read):** portal dashboards/orders/analytics → Mongo; discovery → ES; critical reads
  (balance, payout eligibility, attribution existence) → PostgreSQL directly
  (`AffiliatePayoutService.GetAvailableBalanceCoreAsync`, `:59`;
  `AffiliateService.RecordCommissionForOrderAsync` reads `AffiliateAttributions` directly, `:1393`).
- **Events:** MassTransit outbox → consumers → ledgers / projections.
- **Outbox:** generic shared outbox (`OutboxMessage`); affiliate publishes via `IEventPublisher`.
  Critical events ordered by (TenantId, AggregateId, EventVersion) so stale versions can't overtake.
- **Inbox / idempotency:** deterministic `IdempotencyKey` on every ledger entry (e.g.
  `AFFILIATE_COMM:EARNED:{OrderItemId}`) + Mongo `ProcessedEventIds` + receipt docs. 23505
  unique-violation caught and treated as success.
- **Replay / Repair:** `AffiliateAttributionReconciliationHostedService` (1-min sweep) repairs
  missing attributions; consumers are idempotent so replays are safe. **No repair-on-read for
  analytics** (gap — Mongo dashboards can drift; only attribution has a reconciliation host).
- **Projections:** event-fed, derivative, never write SQL.

---

## Runtime Authority Matrix

| Concept | Owner (Mutator) | Reader | Event Consumer | Projection Writer | Runtime Truth |
|---|---|---|---|---|---|
| Affiliate identity | Affiliate runtime | Affiliate/Admin | — | — | SQL `AffiliateAccount` |
| Affiliate status | Affiliate runtime (admin-governed) | Admin/Portal | — | — | SQL `AffiliateAccount.Status` |
| Referral code | Affiliate runtime | Middleware/Checkout | — | — | SQL `AffiliateAccount.ReferralCode` |
| Attribution | Affiliate runtime | Attribution consumer | `AffiliateOrderAttributionConsumer` | `AffiliateOrderDocument` | SQL `AffiliateAttribution` |
| Commission | Affiliate runtime | Payout service | Payment/Delivery/Refund consumers | `AffiliateOrderDocument` | SQL `AffiliateCommissionLedger` |
| Affiliate balance | Affiliate runtime | Payout service | Settlement consumer | Dashboard | SQL `AffiliateBalanceLedger` |
| Affiliate wallet | Affiliate (owner) | Portal | — | — | SQL `AffiliateAccount.PayoutWallet*` |
| Affiliate payout | Affiliate runtime | Admin/Portal | `AffiliateSettlementConsumers` | Dashboard | SQL `AffiliatePayoutRequest` |
| Affiliate metrics | Derived | Portal/Analytics | Analytics consumers | Mongo docs | Mongo (derivative) |
| Orders | **Order runtime** | Order runtime | Affiliate (observe) | — | SQL `Orders` |
| Payments | **Payment lifecycle** | Payment runtime | Affiliate (observe) | — | SQL payments |
| Customers | **Customer runtime** | Customer runtime | Affiliate (observe) | — | SQL customers |
| GL / Accounting | **Treasury/Accounting** | Accounting | AffiliateAccountingConsumer (posts via svc) | — | SQL `JournalEntry` (via service) |

---

## Cross Runtime Authority Graph

| Peer Runtime | Reads from Affiliate | Writes to Affiliate | Events to Affiliate | Runtime Authority | Ownership / Sync |
|---|---|---|---|---|---|
| Identity (POS) | — | — | — | Separate JWT; soft FK `UserId` | Affiliate observes nothing; POS owns User |
| Orders | Attribution stamp (read) | Order affiliate fields (stamp) | `AffiliateOrderPlacedEvent` | Orders own lifecycle; affiliate stamps | Event-driven, idempotent |
| Payments | — | — | `OnlinePaymentCapturedEvent`, `WalletPaymentApprovedEvent` | Payments own capture | Affiliate observes; triggers commission |
| Customers | — | — | (none direct) | Customers own identity | Affiliate never owns customer |
| Catalog | Policy read (snapshot) | — | `AffiliateProductPolicyChangedEvent` | Merchant owns catalog/pricing | Rate snapshot at checkout |
| Inventory | — | — | (stock-sync outbox health) | Inventory owns stock | Readiness gate consumes stock-sync health |
| Pricing | Snapshot at checkout | — | — | Merchant owns price | Immutable `OrderItem` snapshot |
| Recovery | — | — | `OrderItemRefundedEvent` | Recovery owns refunds | Affiliate reverses commission |
| Governance (POS Admin) | Status/portal | Affiliate status, payout approve | — | Admin governs affiliate | Synchronous service calls |
| CommerceHub | — | — | Marketplace outbox (health only) | CommerceHub owns marketplace | Readiness gate consumes marketplace health |
| Builder / Storefront | — | — | — | Storefront presents, never writes | Admission only |
| Analytics (tenant) | — | — | — | Separate UTM attribution runtime | **Distinct bounded context — do NOT merge** |
| Notifications | — | — | — | None dedicated | Audit identity captured on payout |

---

## Business Invariants (implementation-backed)

1. **Attribution gateway:** no `AffiliateAttribution` → no commission (`AffiliateAttribution.cs:9`).
2. **One attribution per order:** UNIQUE `(TenantId, OrderId)` (`AffiliateAttribution.cs:14,21`).
3. **Global affiliate; ledger `TenantId` NULL; `StoreTenantId` is truth**
   (`AffiliateCommissionLedger.cs:25,42-52,143-144`).
4. **Append-only ledgers** (no `BaseEntity`; DB trigger enforced)
   (`AffiliateCommissionLedger.cs:28-34`).
5. **Commission positive; rate (0.0001, 0.50]; snapshot at checkout**
   (`AffiliateProductPolicy.cs:121-124`; `AffiliateService.cs:1508-1584`).
6. **Approval requires delivery + payment recognized + accounting posted**
   (`AffiliateService.cs:2539-2609`).
7. **Refund reversal ≤ earned** (proportional; throws if exceeds)
   (`AffiliateService.cs:1743-1932`).
8. **Balance formula** `RawAvailable = netApproved + payoutReductions + withdrawals(neg) −
   pendingRequests`; `Paid` ledger state does NOT reduce available
   (`AffiliatePayoutService.cs:147-160`).
9. **Payout idempotent** (reference-already-used + withdrawal-exists checks)
   (`AffiliateSettlementOrchestrator.cs:269-378`).
10. **Payout destination immutable** (wallet change post-request → reject)
    (`AffiliatePayoutRequest.cs:167-195`).
11. **External transfer evidence HMAC-verified**
    (`RequireVerifiedExternalPayoutEvidence=true`, `AffiliateSettlementOptions.cs:28`;
    `AffiliatePayoutTransferEvidence.cs:83-107`).
12. **Fail-closed payout gate** (`AffiliateSettlementOrchestrator.cs:1563`).
13. **Tenant boundary enforced on events** (`TenantId == StoreTenantId`)
    (`AffiliateOrderAttributionConsumer.cs:91-101`).
14. **First-click cookie lock** (`AffiliateReferralMiddleware.cs:489-510`).
15. **No earning unless `Active`** (`AffiliateAccount.cs:12,45`).
16. **No Redis in the write/settlement path** (grep returns 0 hits in `Services/Affiliate`).

---

## Runtime Timeline (one complete lifecycle, step by step)

| Step | Owner | Authority | Synchronization | Persistence | Consistency | Recovery | Retry | Failure | Events |
|---|---|---|---|---|---|---|---|---|---|
| Affiliate Created | Affiliate runtime | SQL `AffiliateAccount` (Pending) | sync | SQL insert | ACID | — | — | throw on bad code | (none) |
| Referral Link Generated | Affiliate runtime | SQL `AffiliatePromotionLink` | sync | SQL insert (unique code) | ACID | — | — | dup code → 23505 | (none) |
| Customer Click | Middleware (event-only) | `AFF_ATTR` cookie set | async, non-blocking | Cookie + outbox | best-effort | cookie revalidated next req | n/a | swallowed | `AffiliateLinkClickedEvent` |
| Cookie Stored | Middleware | first-click lock | sync write-once | `AFF_ATTR` | evidence | tamper → delete | n/a | delete+reject | (none) |
| Order Created | Order runtime | SQL `Orders` (stamped) | checkout priority 0/1/2 | SQL `Order.AffiliateAccountId` | serializable | reconciliation host | idempotency | throw DomainGuard | `AffiliateOrderPlacedEvent` |
| Attribution | Affiliate runtime | SQL `AffiliateAttribution` | consumer | SQL insert (1/order) | UNIQUE guard | `ReconcileMissingAttributionsAsync` | 23505=success | DLQ | `AffiliateAttributedEvent` |
| Commission Calculated | Affiliate runtime | SQL `AffiliateCommissionLedger` | consumer | SQL append (Earned) | snapshot-priced | idempotency key | 23505=success | retry | `AffiliateCommissionEarnedEvent` |
| Commission Approved | Affiliate runtime | SQL ledger (Approved) | Serializable+FOR UPDATE | SQL update | delivery+payment+acct gated | concurrent-dup guard | retry | throw | `AffiliateCommissionApprovedEvent` |
| Payout Requested | Affiliate runtime | SQL `AffiliatePayoutRequest` (Pending) | sync | SQL insert (min 100) | amount≤available | — | — | reject if over | (none) |
| Payout Completed | Affiliate runtime (orchestrator) | SQL ledgers + evidence | Serializable, MAX_RETRIES=3 | Withdrawal + Paid + evidence | fail-closed gate | 40001 retry | yes | blocked by gate | `AffiliateCommissionPaidEvent` |
| Ledger Updated (GL) | Treasury/Accounting (via svc) | SQL `JournalEntry` (Paid) | consumer | GL post | Earned-journal-prereq guard | out-of-order guard | retry | throw | (none) |
| Affiliate Suspended | Affiliate runtime (admin) | SQL `AffiliateAccount` (Suspended) | sync | SQL update + session revoke | `TokensValidAfterUtc` | — | — | idempotent | (none) |

---

## Runtime Classification (verdict)

**Classification: Affiliate Participation Runtime (Hybrid).**

It is not purely a "Referral Runtime" (attribution is only one phase), not purely a "Commission
Runtime" (commission is downstream of participation), and not purely an "Identity Runtime" (it
mutates money). It is the runtime that **admits an independent global affiliate into a POS Admin
storefront as a bounded participant** and governs the full participation arc: identity → referral →
attribution → commission → approval → payout → governance.

Within it, four sub-runtime identities are provable from code:

- **Affiliate Identity Runtime** — `AffiliateAccount` + `AffiliateUser`, global, separate JWT
  (`AffiliateAccount.cs`, `AffiliateUser.cs`, `AffiliateAuthController.cs`).
- **Referral / Attribution Runtime** — middleware + `AffiliateAttribution` gateway, first-click
  locked, storefront-admitted (`AffiliateReferralMiddleware.cs`, `AffiliateAttribution.cs`).
- **Commission Runtime** — snapshot-priced, event-observed, append-only ledger
  (`AffiliateCommissionLedger.cs`, `AffiliateService.cs`).
- **Financial Payout Runtime** — fail-closed, store-scoped money, HMAC-verified evidence
  (`AffiliateSettlementOrchestrator.cs`, `AffiliatePayoutRequest.cs`,
  `AffiliatePayoutTransferEvidence.cs`).

The unifying runtime class is **Participation**: an external actor is granted a strictly scoped,
observationally-derived authority inside a tenant it does not own. Every boundary rule (global
identity, store-scoped money, gateway attribution, fail-closed payout, no Redis in the write path,
no fraud engine) follows from that single participation contract.

---

## Final Architecture Answer

**"How does Shopixy let independent affiliates participate in a POS Admin storefront while
preserving tenant authority, correctly attributing referrals, calculating commissions, coordinating
payouts, and keeping clear authority boundaries?"**

1. **Tenant authority preserved** because the affiliate is *admitted*, never *merged*. The store
   owns Product, Order, Payment, Customer, Fulfillment, and Accounting. The affiliate only stamps
   two order fields and reacts to events (`CheckoutService.cs:2139,5680-5682`;
   `AffiliateOrderAttributionConsumer.cs:91-101`).
2. **Referrals correctly attributed** via a *gateway* entity: `AffiliateAttribution` is created
   exactly once per order from the storefront-admitted, first-click-locked `AFF_ATTR` evidence; no
   attribution → no commission, permanently (`AffiliateAttribution.cs:9,14`;
   `AffiliateReferralMiddleware.cs:489-510`).
3. **Commissions calculated** from an immutable per-SKU snapshot taken at checkout, priced by a
   merchant-owned rate `(0.0001, 0.50]`, approved only after delivery + payment recognition +
   accounting posting (`AffiliateService.cs:1508-1584,2539-2609`;
   `AffiliateProductPolicy.cs:121-124`).
4. **Payouts coordinated** through one Serializable, fail-closed orchestrator that blocks every
   outward cent unless the accounting/integration/payment/marketplace/stock outboxes are healthy and
   no unresolved payment/refund exists, then writes a `Withdrawal`, marks `Paid`, records HMAC-
   verified external evidence, and posts GL via the financial service
   (`AffiliateSettlementOrchestrator.cs:109,1563,225`;
   `AffiliatePayoutTransferEvidence.cs:83-107`; `AffiliateAccountingConsumer.cs:46-60`).
5. **Authority boundaries kept clear** by the global-identity / store-scoped-money split
   (`TenantId` NULL, `StoreTenantId` truth on every ledger, `AffiliateCommissionLedger.cs:25,42-52`)
   and by the explicit "does NOT own" set (Orders, Payments, Customers, GL, Treasury, Marketplace).
   The affiliate's identity is global and independent; its money is always attributable to the
   specific store that owed it.


---

## Documentation Validation

Each assumption in the task brief is checked against implementation. Where the brief assumed a
thing that the code contradicts, it is corrected below. Implementation wins.

| # | Brief assumption | Implementation verdict | Evidence | Correction |
|---|---|---|---|---|
| 1 | "Affiliate Participation Runtime" is a distinct, named runtime. | No single class/namespace is named that; it is a *classification* of one bounded context (the Affiliate Platform) viewed through the participation lens. | Whole `PointOfSale.*/Affiliate/**` tree | The doc classifies the existing Affiliate Platform as a Participation Runtime; no new artifact was assumed. |
| 2 | Affiliate might be a Tenant / Customer / User. | It is a **separate identity** (global), FK-bound to a POS `UserId` but authenticated via its own `AffiliateUser` + JWT. Not a tenant, customer, or POS user. | `AffiliateAccount.cs:23`; `AffiliateUser.cs:9-20`; `AffiliateAuthController.cs:60-69` | Core Discovery table updated accordingly. |
| 3 | "Affiliate Rejected / Blocked / Archived / Removed / Disabled" are statuses. | Only four statuses exist: `Pending, Active, Suspended, Closed`. "Rejected" exists only on *payout requests*, not accounts. No Blocked/Archived/Removed/Disabled. | `AffiliateAccount.cs:186-192`; `AffiliatePayoutRequest` statuses | Status table collapsed to the four real values. |
| 4 | Referral model includes last-click override and multi-touch. | **First-click only** (`AFF_ATTR` write-once). No last-click override, no multi-touch. | `AffiliateReferralMiddleware.cs:489-510` | Referral/Attribution sections state first-click wins; multi-touch absent. |
| 5 | Attribution may expire. | Attribution never expires; permanent once created. | `AffiliateAttribution.cs:14`; consumer `:90-155` | Stated explicitly. |
| 6 | Affiliate owns Orders / Customers / Payments. | Affiliate only *stamps* two order fields and *observes* orders/payments/customers via events. | `CheckoutService.cs:2139,5680-5682`; consumer catalog | "Does NOT Own" + Authority Matrix corrected. |
| 7 | Affiliate owns its GL / accounting. | Posts GL *through* `IFinancialPostingService`; never writes `JournalEntry` directly. | `AffiliateAccountingConsumer.cs:46-60` | Authority Matrix + Financial sections corrected. |
| 8 | Redis is part of the affiliate runtime. | Redis is used **only** as a 30s analytics-summary read cache. The write/settlement path has **zero** Redis. | grep of `Services/Affiliate` → 0 hits; `AffiliateAnalyticsController.cs:10,70,116,128` | CQRS/Persistence sections state no Redis in write path. |
| 9 | A fraud engine exists. | **None.** No self-referral / duplicate-click / velocity / blacklist / risk-score anywhere in `Affiliate/**`. | repo-wide search (see Fraud gap) | Explicit gap recorded; only fraud-adjacent controls listed. |
| 10 | "Referral Attribution Runtime" and tenant marketing attribution are the same. | They are **separate bounded contexts**: affiliate click IDs (`AffiliateAttribution`) vs UTM (`OrderAttribution`). | skill pitfall; `AffiliateAttribution.cs` vs tracking runtime | Cross-Domain graph flags Analytics as a separate UTM runtime. |
| 11 | Commission can be re-read/re-priced at payout. | Commission is snapshot-priced at checkout from immutable `OrderItem`; never re-read. | `AffiliateService.cs:1508-1584,1541-1561` | Commission section corrected. |
| 12 | Payout can proceed even if backbones are unhealthy. | **Fail-closed**: payout blocked unless accounting/integration/payment/marketplace/stock outboxes healthy and no unresolved payment/refund. | `AffiliateSettlementOrchestrator.cs:1563` | Financial + Timeline corrected. |
| 13 | Affiliate is tenant-scoped. | Global; `TenantId` audit-only; money scoped by `StoreTenantId`. | `AffiliateAccount.cs:83-105`; `AffiliateCommissionLedger.cs:25,42-52` | Thesis + Authority sections corrected. |
| 14 | Admin can change affiliate payout wallet. | Wallet is affiliate-owned; admin-forbidden by domain guard. | `AffiliateAccount.cs:153-167` | Identity/Authority corrected. |

### Assumptions the brief got right (corroborated by code)

- The runtime *is* fundamentally about participation/admission of an external actor — confirmed.
- Attribution is a genuine gateway to commission — confirmed (`AffiliateAttribution.cs:9`).
- CQRS split (SQL authority, Mongo/ES derivative) — confirmed.
- Append-only ledgers and idempotency via 23505 — confirmed.
- Outbox/event-driven propagation — confirmed.

### Known gaps (do not invent fixes)

1. **No fraud engine** (self-referral, duplicate-click, velocity, blacklist, risk score absent).
2. **No Redis in write path** — ranking is Mongo + in-process daily recalc; can lag.
3. **No repair-on-read for analytics** — only attribution has a reconciliation host.
4. **Legacy v1 + v2 dual-write coexistence** — `AffiliatePromotionLink.AffiliateAccountId` bridge
   + middleware v1 fallback are partial-migration artifacts.
5. **God service** — `AffiliateService.cs` (~2.8k lines) owns policy+attribution+commission+
   approval+refund+payout delegation.
6. **Redundant payout accounting** — both event consumer and inline orchestrator post Paid
   journals (idempotency-guarded legacy).
7. **Account status has no "Rejected/Blocked/Archived/Removed/Disabled"** — four values only.

---

## Certification

- **Confidence:** High. All load-bearing claims verified against `file:line` in
  `PointOfSale.Core`, `PointOfSale.API`, and `PointOfSale.Infrastructure`.
- **Completeness:** Covers purpose, scope, authority, ownership, boundaries, lifecycle, state
  machines, status, referral, attribution, commission, financial, orders/identity/storefront
  integration, events, persistence, CQRS, authority matrix, cross-domain graph, invariants,
  timeline, classification, and documentation validation.
- **Verified:** identity separation, global/store-scoped money, attribution gateway, first-click
  lock, fail-closed payout, no Redis in write path, append-only ledgers, idempotency.
- **Missing evidence:** none required to support the conclusions drawn; gaps are explicitly flagged
  rather than fabricated.
- **Debt:** the six gaps above are recorded as implementation debt, not as designed features.
