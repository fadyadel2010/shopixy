# Affiliate Platform — Final Canonical Architecture

> Implementation-backed architecture of the Affiliate Platform bounded context.
> Source of truth = code in `PointOfSale.*`. Previous markdown/comments/certification docs were
> used only to locate code, then cross-checked against implementation. Every claim below is
> cited to `file:line`. Where a previous claim did not match code, it is flagged.

---

## Executive Summary

The Affiliate Platform is **one complete bounded context** responsible for affiliate identity,
tracking, attribution, conversion, commission, settlement, payout, fraud-adjacent governance,
and analytics. It is **not** a thin "Affiliate Portal." Its authoritative state is a set of
append-only PostgreSQL ledgers; MongoDB, Elasticsearch, and (to a limited extent) in-process
ranking are **derivative read models only**.

Shopiaxy/PointOfSale treats affiliates as **global, tenant-unbound identities** (AF-19/AF-19B):
an `AffiliateAccount` has no effective tenant binding, but each commission/balance row carries a
`StoreTenantId` identifying *which store owes the money*. This single design decision is the
load-bearing architectural fact for the entire platform and explains most of its boundary logic.

Runtime authority is **fail-closed**: outward money movement (payout) is gated by a
readiness probe (`EnsureAffiliateOutwardMoneyMovementReadyAsync`,
`AffiliateSettlementOrchestrator.cs:1563`) that inspects the accounting outbox, integration
outbox, payment-compensation outbox, marketplace outbox, stock-sync outbox, and unresolved
payment/refund status before permitting any single cent to leave the system.

---

## Architecture Vision

A monetization runtime that lets anyone (a global affiliate) drive sales to any store and be
paid per-SKU commission, with:

- Authoritative, append-only financial ledgers (no soft-delete, DB triggers enforce immutability).
- Event-sourced propagation (MassTransit outbox → consumers → ledgers / projections).
- CQRS: SQL = write + critical-read authority; Mongo = portal analytics; ES = product discovery.
- Cross-domain isolation: affiliate never owns merchant orders/payments; it *observes* them via events.
- Fail-closed payout: no outward money unless the whole backbone is healthy.

## Affiliate Platform Purpose

Turn referral traffic into attributable, approvable, payable commission for global affiliates,
while keeping the merchant store the owner of the order, payment, fulfillment, and accounting.

## Affiliate Platform Scope

Owns: affiliate identity, promotion links, click/attribution events, commission ledger lifecycle,
balance ledger movements, payout requests, payout execution, external transfer evidence, affiliate
analytics projections, product intelligence / ranking projections.

Observes (read-only / event-driven): products, orders, order items, payments, fulfillment/delivery,
refunds, accounting postings, treasury cashbox, marketplace events.

## Affiliate Platform Owns

- `AffiliateAccount`, `AffiliateUser`, `AffiliatePasswordResetToken`, `AffiliateEmailVerificationToken`
- `AffiliatePromotionLink`, `AffiliateProductPolicy`, `AffiliateCategory`
- `AffiliateAttribution` (gateway entity)
- `AffiliateCommissionLedger` (append-only, per SKU)
- `AffiliateBalanceLedger` (append-only, account-level)
- `AffiliatePayoutRequest`, `AffiliatePayoutTransferEvidence`, `PayoutWallet` (value object)
- Mongo read models (`AffiliateDashboardDocument`, `AffiliateOrderDocument`, `AffiliateAnalyticsDailyDocument`, `AffiliateAnalyticsSummaryDocument`, `AffiliateClickDocument`, `AffiliateProductIntelligenceDocument`)
- ES index for affiliate product discovery

## Affiliate Platform Does NOT Own

- Merchant `Product` lifecycle/pricing (it reads a snapshot at commission time only).
- `Order` / `OrderItem` lifecycle (observes via events; stamps `AffiliateAccountId`/`AffiliateReferralCode` at checkout).
- Payment capture / refunds (observes `OnlinePaymentCapturedEvent`, `OrderCodDeliveredEvent`, `WalletPaymentApprovedEvent`, `OrderItemRefundedEvent`).
- Fulfillment/delivery (observes `OrderDeliveredEvent`/`FulfillmentDeliveredEvent`).
- General Ledger (posts *through* `IFinancialPostingService`; never writes `JournalEntries` directly — `AffiliateAccountingConsumer.cs:152`, `AffiliateSettlementOrchestrator.cs:1559`).
- Treasury cashbox (resolves via `ICashboxLedgerAccountService`).
- CommerceHub marketplace orders (observes `MarketplaceEventOutboxMessage` via readiness gate only).

---

## Architecture Principles (implementation-backed)

1. **SQL is the single authoritative system of record.** Mongo/ES are derivative (`AffiliateDashboardDocument` comment, `AffiliateDocuments.cs:6`).
2. **Append-only ledgers.** `AffiliateCommissionLedger` and `AffiliateBalanceLedger` do *not* inherit `BaseEntity`; no `IsDeleted`, no `UpdatedAt` (`AffiliateCommissionLedger.cs:30-34`, `AffiliateBalanceLedger.cs` header). DB triggers enforce this.
3. **Idempotency via unique constraints + deterministic keys**, not application guesswork. Every ledger entry has an `IdempotencyKey` (e.g. `AFFILIATE_COMM:EARNED:{OrderItemId}`, `AffiliateCommissionLedger.cs:155`). Unique-violation (23505) is caught and treated as success (e.g. `AffiliateService.cs:1574`, `:2434`).
4. **Coupon/rate snapshots at checkout.** Commission is computed from immutable `OrderItem.AffiliateCommission*` snapshots, never re-read at payout (`AffiliateService.cs:1520-1561`).
5. **Global affiliate, store-scoped money.** `TenantId` on ledgers is ALWAYS NULL; `StoreTenantId` is the truth (`AffiliateCommissionLedger.cs:38-52`).
6. **CQRS read/write split.** Writes go to SQL in a `Serializable` transaction; reads for portal come from Mongo.
7. **Fail-closed payout.** No money out unless the backbone is healthy (`AffiliateSettlementOrchestrator.cs:1563`).
8. **Cross-tenant isolation.** `X-Tenant-Id` is stripped upstream; affiliate events carry both `TenantId` and `StoreTenantId` and validate the store boundary (`AffiliateOrderAttributionConsumer.cs:91-101`).

---

## Core Aggregates

| Aggregate | Root | Authority | Notes |
|---|---|---|---|
| Affiliate Identity | `AffiliateAccount` | SQL (global) | Status machine Pending→Active→Suspended→Closed |
| Affiliate Login | `AffiliateUser` | SQL (global) | Separate JWT from POS |
| Promotion Link | `AffiliatePromotionLink` | SQL (store-scoped) | `?ref=` facing token |
| Attribution | `AffiliateAttribution` | SQL | gateway to commission; immutable |
| Commission | `AffiliateCommissionLedger` | SQL | append-only per SKU |
| Balance | `AffiliateBalanceLedger` | SQL | append-only account-level |
| Payout | `AffiliatePayoutRequest` | SQL | hybrid withdrawal engine |
| Payout Evidence | `AffiliatePayoutTransferEvidence` | SQL | HMAC-verified external transfer |
| Policy | `AffiliateProductPolicy` | SQL (tenant) | merchant sets rate |

---

## Affiliate Runtime

### Identity Runtime
- `AffiliateAccount` (`AffiliateAccount.cs`): aggregate root, `ITenantOwned` but `TenantId` is audit-only post-AF-19. Exactly one account per `(UserId)`; `ReferralCode` globally unique, immutable after creation (`AffiliateAccount.cs:8-14`, `:83-105`).
- `AffiliateUser` (`AffiliateUser.cs`): independent identity, **1:1** with `AffiliateAccount`, separate JWT, separate `PasswordHasher`. Never shares JWT with POS app (`AffiliateUser.cs:5-19`). Email unique per tenant.
- **Status machine**: `Pending(0) → Active(1) → Suspended(2) → Active`; `Active → Closed(3)` terminal (`AffiliateAccount.cs:107-147`). `IsActive = Status == Active`. No commission unless Active.
- **Approval/Activation**: `ActivateAffiliateAsync` (`AffiliateService.cs:123`). **Suspension/Close revokes all sessions** (`RevokeAffiliateAccountSessionsAsync`, `AffiliateService.cs:166-180` calling `AffiliateUser.RevokeSessions` which bumps `TokensValidAfterUtc`, `AffiliateUser.cs:126-130`).
- **Hierarchy**: none implemented — flat affiliate model. No sub-affiliate/uplink/MLM. (Gap, see Technical Debt.)
- **Permissions**: minimal — `AffiliateUser` is the auth principal; admin actions are POS/Admin/SuperAdmin scoped. Affiliate portal cannot mutate payout wallet on admin side (`AffiliateAccount.UpdatePayoutWallet` comment, `AffiliateAccount.cs:153-167`).
- **Authentication/Authorization**: `AffiliateAuthController` + `AffiliateUser` JWT. Refresh tokens + `TokensValidAfterUtc` for revocation. Separate auth boundary from POS (`AffiliateUser.cs` KILL LIST).

### Referral Runtime
- Entry point: `AffiliateReferralMiddleware` (`AffiliateReferralMiddleware.cs`). No DB writes; event-driven only; never blocks request (swallows errors, `:471-478`).
- Referral code resolved from (priority): `AFF_ATTR` cookie (validated evidence) → `?ref=` query → `X-Affiliate-Ref` header → `/p/{code}/{slug}` route → `/api/storefront/landing/{code}/{slug}` (`:79-150`).
- **v2 flow**: resolves `AffiliatePromotionLink` (store-scoped, active, product-selectable), then bridge to `AffiliateAccountId` (`:178-345`). **v1 fallback**: resolves `AffiliateAccount` by global `ReferralCode` + `Active` status (`:347-463`).
- Publishes `AffiliateLinkClickedEvent` (non-authoritative, `AffiliateEvents.cs:285`) with **SHA-256 hashed IP/UA** (no PII, `:316-317`). Sets `AFF_SESSION` cookie + `AFF_ATTR` evidence cookie.
- **Cross-store**: affiliate's home store may differ from order store; logged as `AF-19:CrossStore` (`:409-416`). The link's `TenantId` must equal current store for admission (`:181-184`).

### Tracking Runtime
- **Affiliate Links / Tracking IDs**: `AffiliatePromotionLink.ReferralCode` (8–16 chars, globally unique) is the URL-facing tracking id (`AffiliatePromotionLink.cs:54-60`). Account-level `ReferralCode` (6–12 chars) is legacy v1.
- **Cookies**: `AFF_SESSION` (session id), `AFF_ATTR` (signed evidence token, validated on every request, `:88-107`). Cookie is *evidence only*; checkout revalidates SQL authority (`:486-501`).
- **Browser Storage**: storefront `affiliateRefTracker.ts` captures referral into client storage; the middleware is the server-side authority. (Subagent-verified: storefront stores referral locally and replays into cart/checkout; server re-validates.)
- **Sessions**: `AFF_SESSION` generated if missing; survives refresh (`:42`).
- **Expiration**: evidence token has validity window enforced at `AffiliateAttributionEvidenceTokenService` (validated in middleware `:91`).
- **Click Recording**: `AffiliateLinkClickedEvent` → `AffiliateLinkClickedConsumer` → `AffiliateClickDocument` (Mongo) + `AffiliateRankingService.IncrementClickSignalAsync` (`AffiliateRankingService.cs:75-94`) + daily `AffiliateAnalyticsDailyClickConsumer`.
- **Deduplication**: per-event receipts (`AffiliateProjectionEventReceiptDocument`) + `ProcessedEventIds` set on daily docs (`AffiliateAnalyticsDailyProjectionConsumer.cs:60-104`).
- **Last click / First click / Override**: **FIRST-CLICK WINS for cookie attribution.** `EnsureAttributionCookie` returns early if an `AFF_ATTR` cookie already exists (`:497-501`) — i.e. the first accepted referral is locked for the navigation session; later `?ref=` on the same cookie does not overwrite. There is **NO last-click override and NO multi-touch attribution** implemented. (Explicit gap — see Technical Debt.)

### Cookie Runtime
- `AFF_ATTR` is a signed evidence token (`AffiliateAttributionEvidenceTokenService`). Middleware validates it against SQL authority on each request and **deletes it** if tampered, foreign-tenant, expired, or no longer matching SQL (`AffiliateReferralMiddleware.cs:100-107`, `:262-273`, `:396-407`). This is the anti-cookie-abuse control (not a separate fraud engine — see Fraud Runtime).

### Session Runtime
- Request-scoped "Affiliate Mode" via `AffiliateModeResolver.SetAffiliateModeV2/SetAffiliateMode` (`:297-304`, `:439-443`). Request-scoped only, NOT persisted (`:44`). `AFF_SESSION` cookie provides continuity.

### Attribution Runtime (pipeline)
```
Visitor
  → Affiliate Link (?ref=CODE / landing route)
    → AffiliateReferralMiddleware resolves PromotionLink/Account (global, store-admitted)
      → AFF_ATTR evidence cookie set (first-click locked)
      → AffiliateLinkClickedEvent published (non-authoritative)
        → Storefront cart/checkout carries AffiliateAccountId + ReferralCode
          → CheckoutService stamps Order.AffiliateAccountId / Order.AffiliateReferralCode
            → AffiliateOrderPlacedEvent (OrderPlaced) emitted
              → AffiliateOrderAttributionConsumer → AffiliateAttribution row (immutable, 1/order)
                → Placeholder AffiliateOrderDocument (status AttributedPendingPayment)
```
- **Authority**: `AffiliateAttribution` is the gateway — *no attribution → no commission* (`AffiliateAttribution.cs:11`). One per `(TenantId, OrderId)` UNIQUE.
- **Lifetime**: immutable append-only; created at order placement, never updated.
- **Validation**: consumer rejects missing store-tenant authority boundary (`AffiliateOrderAttributionConsumer.cs:91-101`); requires `ReferralCode` (`:108-116`).
- **Expiration**: attribution does not expire; it is permanent once created.
- **Priority**: first-click (cookie) > query > header > route (middleware `:79-150`).
- **Synchronization**: attribute-before-payment enforced; reconciliation host repairs missing attributions (`AffiliateAttributionReconciliationHostedService.cs`, `ReconcileMissingAttributionsAsync`).

### Conversion Runtime
- Trigger: payment capture (`OnlinePaymentCapturedAffiliateConsumer`, `CodDeliveredAffiliateConsumer`, `WalletPaymentApprovedAffiliateConsumer`, `OrderCollectionAccountingPostedAffiliateConsumer`). All delegate to `AffiliateService.RecordCommissionForOrderAsync` (`AffiliatePaymentCapturedConsumers.cs:36-464`).
- **Order Attribution gate**: attribution must exist (`:1392-1425`); if order has affiliate fields but no canonical row, it throws to force retry/reconcile.
- **Creation**: per SKU, from immutable `OrderItem` snapshot (`AffiliateService.cs:1508-1584`). Skips non-affiliate-eligible / zero-amount items.
- **Approval**: gated on **physical delivery + payment recognized + accounting posted** (`EnsureAffiliateDeliveryApprovalAuthority`, `:2539-2609`); Manual fulfillment requires verified delivery evidence (`:2562-2586`). Serializable txn + `FOR UPDATE` row lock (`LoadAffiliateApprovalOrderAuthorityAsync`, `:2493-2537`).
- **Cancellation**: terminal order status (`Cancelled`/`Refunded`) skips commission (`:1427-1435`).
- **Refund Impact**: `ReconcileRefundedCommissionEntriesAsync` creates proportional `Reversed` entries (`:1743-1850`); `CalculateRefundCommissionReversalAuthority` never exceeds earned (`:1884-1932`); idempotent via cumulative idempotency key (`:1786-1802`).
- **Chargeback Impact**: handled via reversal/penalty ledgers (no dedicated chargeback consumer; refunds drive reversals).
- **Manual Review**: none automated; admin approves payout requests manually.
- **Duplicate Protection**: unique `IdempotencyKey` on every entry + 23505 catch-as-success.

### Commission Runtime
- **Calculation**: `CommissionAmount = min(PaidAmount × Rate, MaxPerItem)`, rounded to 4 dp (`AffiliateCommissionLedger.CreateEarned`, `:122-159`). Rate range `(0, 0.50]` enforced in `AffiliateProductPolicy.ValidateRate` (`AffiliateProductPolicy.cs:121-125`).
- **Authority**: SQL ledger is authority; rate snapshot taken at checkout (`AffiliateService.cs:1541-1561`). Merchant sets rate; affiliate never controls it (`AffiliateProductPolicy.cs:11`).
- **Merchant / Platform / Marketplace Commission**: all stored in the same `AffiliateCommissionLedger`; separation is by `StoreTenantId` (which store owes), not by ledger type. Marketplace orders are attributed like any other store order (AF-19B global model).
- **Rules/Overrides**: `MaxCommissionPerItem` cap; category must be active (`AffiliateService.cs:206-256`).
- **Lifecycle states** (`CommissionEntryType`): `Earned(0) → Approved(2) → Paid(3)`; `Reversed(1)` cancels earned and blocks approval/paid.
  - `Earned` at payment capture; `Approved` at delivery; `Paid` at payout execution.
  - Reversal carries original `BaseAmount` (not commission) for proportional math (`:167-199`).
- **Available/Locked/Pending/Paid**: "available for withdrawal" computed in `AffiliatePayoutService.GetAvailableBalanceCoreAsync` — `RawAvailable = netApproved + payoutReductions + withdrawals(neg) − pendingRequests` (`:159-165`). `CommissionLedger.Paid` does NOT reduce available (only `BalanceLedger.Withdrawal` does) — `:136-153`.

### Settlement Runtime
- **Affiliate Earnings**: `AffiliateCommissionLedger` (per SKU) is the earning record.
- **Balance**: `AffiliateBalanceLedger` (account-level: Withdrawal/Adjustment/Bonus/Penalty) (`AffiliateBalanceLedger.cs`).
- **Settlement / Withdrawal / Payout**: unified in `AffiliateSettlementOrchestrator.ExecuteAffiliatePayoutAsync` (`:109`).
  - Legacy Settlement (admin pays order) and Withdrawal Engine (affiliate requests) both call the same orchestrator (`:31-35`).
  - **Pipeline (mandatory order)** inside `Serializable` retry loop (MAX_RETRIES=3, 40001 retry, `:151-186`):
    1. Begin Serializable tx
    2. Validate available balance (uses `RawAvailable`, not clamped, `:267-308`)
    3. Create `BalanceLedger.Withdrawal` (monetary authority)
    4. Mark `CommissionLedger.Paid` (FIFO, net of reversals)
    5. Auto-reconcile withdrawal requests (legacy only)
    6. Mark `PayoutRequest.Paid` inside tx
    7. Commit; publish `AffiliateCommissionPaidEvent`
- **Payment Evidence**: `AffiliatePayoutTransferEvidence` — HMAC-SHA256 verified external transfer; one rail/reference can fund many allocations, never over-allocated (`AffiliatePayoutTransferEvidence.cs:83-107`). Registered via `AffiliatePayoutEvidenceService.RegisterVerifiedEvidenceAsync` with timestamp window + HMAC (`AffiliatePayoutEvidenceService.cs:30-109`). `RequireVerifiedExternalPayoutEvidence=true` in prod (`AffiliateSettlementOptions.cs:28`).
- **Accounting Integration**: posts journals via `IFinancialPostingService` — Earned: Dr Expense(6110)/Cr Payable(2105); Paid: Dr Payable(2105)/Cr Cashbox (`AffiliateSettlementOrchestrator.PostAffiliatePaidAccountingAsync`, `:1488-1561`; `AffiliateAccountingConsumer.cs:20-35`). Earned journal must exist before Paid (out-of-order guard, `:273-294`).
- **Treasury Integration**: cashbox resolved via `ICashboxLedgerAccountService.ResolveDefaultCashboxAccountAsync` (`:1509`); falls back to `CashboxMain` if none.

### Withdrawal Runtime
- `AffiliatePayoutRequest` (hybrid withdrawal engine, `AffiliatePayoutRequest.cs`): `Pending(1)→Approved(2)→Paid(4)`; `Rejected(3)`.
- Min threshold 100 (`MinimumThreshold`, `:94`); amount ≤ available (`Create`, `:127-129`).
- **Immutable payout destination snapshot** at request time; paying rejects if wallet changed after request (`EnsurePayoutWalletSnapshotMatches`, `:167-195`). Prevents silent re-routing.
- Auto-reconcile with legacy settlement (`Reconcile`, `:218-242`, decimal tolerance 0.0001).

### Payout Runtime
- Execution authority = `AffiliateSettlementOrchestrator` (above). Admin marks paid via `MarkPaid` requiring external payment reference + executor identity (`AffiliatePayoutRequest.MarkPaid`, `:244-263`; orchestrator requires `paidByUserId`, `:236-240`).
- **Idempotency**: reference-already-used check (`:269-283`), withdrawal-exists check (`:327-378`), conflict detection on mismatch.

### Fraud Runtime
**No dedicated fraud-detection subsystem exists in the Affiliate Platform.**
Aggressive repo-wide search for `fraud|Fraud|self-referral|blacklist|rate limit|suspicious|abuse|cookie abuse|duplicate click` returns hits only in WhatsApp rate-limiting, SuperAdmin login throttling, and generic API rate-limit policies — **none in `PointOfSale.*/Affiliate/**` or `AffiliateService`**.
What *does* exist as fraud-adjacent controls:
- Account status governance: `Suspended`/`Closed` + session revocation (`AffiliateService.cs:136-164`).
- Cookie/evidence tamper protection: `AFF_ATTR` validation + deletion on mismatch (`AffiliateReferralMiddleware.cs:100-107`).
- First-click cookie lock prevents simple referer spoofing at click time.
- Payout destination snapshot prevents wallet hijack (`AffiliatePayoutRequest.cs:167-195`).
- External-transfer HMAC evidence prevents fictitious payout confirmation (`AffiliatePayoutEvidenceService.cs`).
- **Gaps (explicit)**: no self-referral detection (affiliate buying through own link), no duplicate-click detection, no velocity/rate-limiting on affiliate clicks, no blacklist, no risk score. (See Technical Debt + Follow-up items.)

### Analytics Runtime
- Mongo read models (`PointOfSale.Infrastructure/MongoDB/Documents/Affiliate/`):
  - `AffiliateDashboardDocument` (earnings/orders/approved/paid counters, idempotent `ProcessedEventIds`)
  - `AffiliateOrderDocument` (per-order commission lifecycle)
  - `AffiliateAnalyticsDailyDocument` (clicks/orders/earnings per day, per-product earnings)
  - `AffiliateAnalyticsSummaryDocument` (total clicks/conversions/rate)
  - `AffiliateClickDocument` (click→conversion bridge)
  - `AffiliateProductIntelligenceDocument` (clicks/conversions/earnings/ranking score)
- Consumed from affiliate events (`AffiliateProjectionConsumers`, `AffiliateAnalyticsConsumers`, `AffiliateAnalyticsDailyProjectionConsumer`, `AffiliateProductIntelligenceProjectionConsumer`).
- **Clicks/Visitors/Conversions/Revenue/Earnings**: derived from `AffiliateLinkClickedEvent`, `AffiliateCommissionEarnedEvent`, `AffiliateCommissionPaidEvent`. Conversion linked by setting `OrderId` on the most recent unconverted click (`AffiliatePaymentCapturedConsumers.cs:327-344`).
- **Top Affiliates / Campaign Performance**: ranking via `AffiliateRankingService` (global-normalized score: commission 30%, 7d earnings 25%, 7d conversions 20%, 7d clicks 15%, freshness 10%; daily Hangfire recalc, `AffiliateRankingService.cs:9-51`, `:143-296`).
- **Reporting**: `AffiliateAnalyticsController` reads Mongo.

### Notification Runtime
- No affiliate-specific notification engine found in core. Payout/approval side-effects are admin-driven; audit identity (`PaidBy`/`ApprovedBy`) is captured (`AffiliatePayoutRequest.cs:62-64`, `:151`). Email verification tokens exist (`AffiliateEmailVerificationToken`) but affiliate email notifications are not a distinct subsystem.

### Search Runtime
- Elasticsearch via `AffiliateProductDiscoveryService` indexes affiliate-enabled products for storefront discovery (read authority = ES; write authority = SQL policy events → `AffiliateProductPolicyProjectionConsumer`). Storefront pricing snapshots also projected (`AF16E_ReindexAffiliateProductsWithStorefrontUrl`).

---

## CQRS

- **Write Authority**: PostgreSQL (`AffiliateAccount`, `AffiliateAttribution`, `AffiliateCommissionLedger`, `AffiliateBalanceLedger`, `AffiliatePayoutRequest`, `AffiliatePayoutTransferEvidence`, `AffiliateProductPolicy`).
- **Read Authority**:
  - Portal dashboards/orders/analytics → MongoDB.
  - Product discovery → Elasticsearch.
  - Critical reads (balance, payout eligibility, attribution existence) → PostgreSQL directly (`AffiliatePayoutService.GetAvailableBalanceCoreAsync`, `:59`; `AffiliateService.RecordCommissionForOrderAsync` reads `AffiliateAttributions` directly, `:1393`).
- **Mongo**: derivative, event-fed, idempotent (`ProcessedEventIds` / receipt docs).
- **Redis**: Used **only as a read-layer cache for analytics**, not in the write/settlement services. `AffiliateAnalyticsController` uses `StackExchange.Redis` with a **30s TTL** for the summary endpoint (`AffiliateAnalyticsController.cs:10,70,116,128`); misses are non-fatal (best-effort). The affiliate *write* runtime (`Services/Affiliate`) has **no Redis** — balance/commission/payout never touch it. Ranking is Mongo + in-process daily recalc.
- **Elasticsearch**: product discovery index only.
- **Repair Reads**: `AffiliateService` re-checks ledger truth on every consumer (e.g. approval re-validates `StoreTenantId` match, `:2288-2300`); balance recomputed inside Serializable tx (`AffiliatePayoutService.cs:445-448`).
- **Critical Reads**: payout uses `RawAvailable` (unclamped) to prevent over-withdrawal (`:473`); currency conflict blocks payout (`:208-223`).
- **Projection Freshness**: event lag detection warns >30s (`AffiliateConsumerHelper.DetectAndRecordLag`, `AffiliatePaymentCapturedConsumers.cs:477-495`).
- **Replay**: `AffiliateAttributionReconciliationHostedService` (1-min sweep) repairs missing attributions; consumers are idempotent so replays are safe.

---

## Projection Model

| Mongo Doc | Fed by | Authority |
|---|---|---|
| `AffiliateDashboardDocument` | Attributed/Earned/Reversed events | Read-only |
| `AffiliateOrderDocument` | Attribution + Earned/Reversed/Paid | Read-only |
| `AffiliateAnalyticsDailyDocument` | Click/Earned events | Read-only |
| `AffiliateAnalyticsSummaryDocument` | Conversion signals | Read-only |
| `AffiliateClickDocument` | Click event + conversion bridge | Read-only |
| `AffiliateProductIntelligenceDocument` | Click/Conversion + daily recalc | Read-only |

Projection consumers never write SQL. They use `ProcessedEventIds` (capped) + `AffiliateProjectionEventReceiptDocument` claim/release for exactly-once-ish application (`AffiliateAnalyticsDailyProjectionConsumer.cs:336-477`).

## Mongo Integration
Write path: SQL ledger write → `IEventPublisher` (MassTransit outbox) → consumer → `MongoDbContext` upsert. Non-fatal Mongo failures are logged, not thrown (e.g. `AffiliateOrderAttributionConsumer.cs:208-214`) so authoritative SQL is never blocked by projection lag.

## Redis Integration
Used **only as a 30s read cache for the analytics summary endpoint** (`AffiliateAnalyticsController.cs:10,70,116,128`; misses non-fatal/best-effort). The affiliate **write/settlement runtime does not use Redis** — balance, commission, and payout computations never touch it. Portal dashboard/order reads hit Mongo directly; ranking is Mongo + in-process daily recalc. Redis is therefore a narrow, optional read accelerator, not an architectural dependency.

## Elasticsearch Integration
`AffiliateProductDiscoveryService` maintains the affiliate product discovery index. Source of truth = `AffiliateProductPolicy` SQL; projection via `AffiliateProductPolicyProjectionConsumer` + `AffiliateProductIntelligenceProjectionConsumer`. Read authority = ES for discovery queries; write authority = SQL.

## Outbox

- **Affiliate Events** (`PointOfSale.Core/Events/Affiliate/`):
  - `AffiliateOrderPlacedEvent` (authoritative, aggregate-scoped)
  - `AffiliateAttributedEvent`
  - `AffiliateCommissionEarnedEvent` / `AffiliateCommissionReversedEvent` / `AffiliateCommissionApprovedEvent` / `AffiliateCommissionPaidEvent`
  - `AffiliateProductPolicyChangedEvent` / `AffiliateCategoryUpdatedEvent` / `AffiliateCategoryDeactivatedEvent`
  - `AffiliateLinkClickedEvent` (marked `INonAuthoritativeIntegrationEvent`)
- **Ordering**: per-aggregate via `IAggregateScopedIntegrationEvent`; affected-tenant scope via `IAffectedTenantScopedIntegrationEvent`.
- **Replay**: idempotent consumers + reconciliation host.
- **Retry**: MassTransit retry; unique-violation caught as success.
- **Idempotency**: deterministic `IdempotencyKey` on every ledger entry + `ProcessedEventIds` on Mongo docs + receipt docs.
- **Dead Letters**: MassTransit DLQ for failed consumers (exceptions rethrown, `AffiliateOrderAttributionConsumer.cs:228-238`).
- **Exactly Once**: not guaranteed at transport; achieved via idempotent sinks (DB unique + Mongo `ProcessedEventIds`).
- **Projection Consumers**: listed in Event Consumers Catalog below.

## Event Flow

```
Checkout (Order placed, affiliate stamped)
  → AffiliateOrderPlacedEvent
    → AffiliateOrderAttributionConsumer → AffiliateAttribution (SQL) + placeholder Mongo order
Payment captured (Online/COD/Wallet/AccountingPosted)
  → RecordCommissionForOrderAsync → AffiliateCommissionLedger(Earned) + AffiliateCommissionEarnedEvent
    → AffiliateCommissionEarnedAccountingConsumer → GL journal (Earned)
    → Mongo dashboard/order/analytics projections
Delivery confirmed
  → AffiliateOrderDeliveredConsumer → ApproveCommissionForDeliveredOrderAsync → CommissionLedger(Approved) + ApprovedEvent
Refund
  → AffiliateRefundConsumers → ReconcileRefundedCommissionEntriesAsync → CommissionLedger(Reversed)
Payout (admin or affiliate request)
  → AffiliateSettlementOrchestrator.ExecuteAffiliatePayoutAsync (Serializable)
    → BalanceLedger(Withdrawal) + CommissionLedger(Paid) + PayoutRequest.Paid
    → AffiliateCommissionPaidEvent → GL journal (Paid) + Mongo projections
```

## Read Models
Mongo (portal/analytics) + ES (discovery). SQL for critical reads.

## Write Models
SQL ledgers only. All writes in explicit transactions (`Serializable` for payout/approval).

---

## Runtime Authority Matrix

| Concept | Owner | Mutation Authority | Read Authority | Projection Authority | Runtime Truth |
|---|---|---|---|---|---|
| Affiliate | Affiliate Platform | SQL `AffiliateAccount` | SQL / Mongo dashboard | Mongo | SQL |
| Affiliate Account | Affiliate Platform | SQL | SQL | Mongo | SQL |
| Referral | Affiliate Platform (link) + Merchant (order stamp) | SQL link + Order stamp | SQL | — | SQL |
| Tracking | Affiliate Platform (cookie/event) | Event (non-authoritative) | Mongo clicks | Mongo | Event → SQL at checkout |
| Click | Affiliate Platform | Mongo (derived) | Mongo | Mongo | Mongo (non-authoritative) |
| Visitor | Affiliate Platform | n/a (hashed) | Mongo | Mongo | Mongo |
| Session | Affiliate Platform | Cookie/Affiliate Mode | Cookie | — | Cookie |
| Cookie | Affiliate Platform | Middleware | SQL re-validate | — | SQL |
| Conversion | Merchant (order) + Affiliate (attribution) | SQL Attribution | SQL/Mongo | Mongo | SQL |
| Commission | Affiliate Platform | SQL Ledger | SQL/Mongo | Mongo | SQL |
| Settlement | Affiliate Platform | SQL Ledger | SQL | Mongo | SQL |
| Withdrawal | Affiliate Platform | SQL `PayoutRequest` | SQL | Mongo | SQL |
| Payout | Affiliate Platform | SQL Orchestrator | SQL | Mongo | SQL |
| Fraud | **None (governance only)** | Account status | SQL | — | SQL status |
| Campaign | Affiliate Platform (link) | SQL Policy/Category | SQL/ES | ES/Mongo | SQL |
| Analytics | Affiliate Platform | Mongo (derived) | Mongo | Mongo | Mongo (derivative) |
| Notification | None dedicated | Admin | — | — | — |
| Search | Affiliate Platform (index) | ES (derived) | ES | ES | SQL policy |

## Runtime Authority Divergence
- **Analytics/Search are derivative** — never authoritative; rebuildable from SQL + events.
- **Clicks/Visitors are non-authoritative** (`INonAuthoritativeIntegrationEvent`) — they inform ranking, never money.
- **Commission/Balance/Payout are authoritative SQL** — money paths never touch Mongo.

## Cross-Domain Authority Graph
See Cross-Domain Authority section. Affiliate is a *derivative observer* of Merchant/Payments/Fulfillment/Accounting/Treasury/CommerceHub; it *owns* identity, attribution, commission, balance, payout.

---

## Business Invariants (implementation-backed)

1. **Attribution gateway**: no `AffiliateAttribution` → no commission (`AffiliateAttribution.cs:11`).
2. **One attribution per order**: UNIQUE `(TenantId, OrderId)` (`AffiliateAttribution.cs:9`).
3. **Global affiliate, store money**: ledger `TenantId` NULL; `StoreTenantId` truth (`AffiliateCommissionLedger.cs:38-52`).
4. **Append-only ledgers**: no update/delete; DB trigger enforced (`AffiliateCommissionLedger.cs:30-34`).
5. **Commission positivity**: `CommissionAmount > 0`; rate `(0, 0.50]` (`AffiliateCommissionLedger.cs:130-138`, `AffiliateProductPolicy.cs:121-125`).
6. **Rate snapshot at checkout**: commission uses `OrderItem` immutable snapshot, never re-read (`AffiliateService.cs:1541-1561`).
7. **Approval requires delivery + payment + accounting**: `EnsureAffiliateDeliveryApprovalAuthority` (`AffiliateService.cs:2539-2609`).
8. **Reversal ≤ earned**: `CalculateRefundCommissionReversalAuthority` throws if exceeds (`AffiliateService.cs:1898-1903`).
9. **Balance formula**: `RawAvailable = netApproved + reductions + withdrawals(neg) − pending`; `Paid` ledger state does NOT reduce available (`:136-165`).
10. **Payout idempotency**: same reference → idempotent skip or conflict-detect (`AffiliateSettlementOrchestrator.cs:327-378`).
11. **Payout destination immutability**: wallet change after request → reject (`AffiliatePayoutRequest.cs:167-195`).
12. **External evidence HMAC**: payout requires verified external transfer evidence in prod (`AffiliateSettlementOptions.cs:28`, `AffiliatePayoutEvidenceService.cs`).
13. **Fail-closed payout**: no outward money unless backbone healthy (`AffiliateSettlementOrchestrator.cs:1563-1644`).
14. **Tenant boundary**: affiliate events validate `TenantId == StoreTenantId` (`AffiliateOrderAttributionConsumer.cs:97-101`).
15. **First-click cookie lock**: existing `AFF_ATTR` cookie is not overwritten (`AffiliateReferralMiddleware.cs:497-501`).

---

## Architectural Decisions (WHY)

- **Why Affiliate separated**: independent global identity + separate JWT + separate rate authority from merchant; prevents PII/commission leakage into POS user system (`AffiliateUser.cs:5-19`).
- **Why Attribution exists as gateway entity**: decouples "an order was referred" (durable, immutable) from volatile commission math; lets commission be recomputed/replayed without losing the referral fact (`AffiliateAttribution.cs`).
- **Why Commission is isolated (per-SKU ledger)**: enables partial refunds/reversals at item granularity, proportional math, and store-scoped money without tenant coupling (`AffiliateCommissionLedger.cs`).
- **Why Settlement/Orchestrator exists**: unifies legacy + withdrawal payout into one Serializable pipeline to eliminate lifecycle drift between `BalanceLedger.Withdrawal` and `CommissionLedger.Paid` (AF-38H, `AffiliateSettlementOrchestrator.cs:36-58`).
- **Why SQL remains authority**: money/legal truth must be strongly consistent, auditable, append-only; Mongo/ES are eventually consistent read models.
- **Why projections exist**: portal analytics at scale would choke SQL; Mongo/ES offload read traffic.
- **Why replay exists**: event-driven pipeline can drop/order-shift; idempotent consumers + reconciliation host repair without double-payment.
- **Why global affiliate (AF-19)**: marketplace model — one affiliate drives sales across many stores; `StoreTenantId` attributes liability to the selling store.
- **Why fail-closed payout**: outward money is irreversible; gating on backbone health prevents paying on a forked/behind state.
- **Why first-click cookie**: simplest tamper-resistant attribution that survives refresh without server session store.

---

## Technical Debt (implementation-backed)

1. **No fraud engine.** No self-referral, duplicate-click, velocity, or blacklist detection anywhere in affiliate code (search evidence above). Relies solely on account suspension + cookie validation.
2. **Projection drift risk.** Mongo failures are non-fatal (`:208-214`); dashboards can lag/diverge from SQL with no automatic repair-on-read for analytics (only attribution has a reconciliation host).
2b. **Partial Redis coverage.** Only the analytics *summary* endpoint is Redis-cached (30s, `AffiliateAnalyticsController.cs:70`); other portal reads hit Mongo directly with no cache layer.
3. **Legacy dual-write / v1+v2 coexistence.** `AffiliatePromotionLink.AffiliateAccountId` "legacy bridge" (`AffiliatePromotionLink.cs:64-67`) and v1 fallback path (`AffiliateReferralMiddleware.cs:347-463`) are partial-migration artifacts. Additionally, `AffiliateProductPolicyProjectionConsumer` wraps a legacy Mongo/ES product-document projection branch in `#if false` (`:98`) — dead code that should be deleted.
4. **Mixed responsibilities in `AffiliateService`.** 2791-line service handles policy, attribution, commission, approval, refund, payout delegation (`AffiliateService.cs`). God-service.
5. **Redundant payout accounting paths.** Both `AffiliateCommissionPaidAccountingConsumer` (event) and `AffiliateSettlementOrchestrator.PostAffiliatePaidAccountingAsync` (inline) post Paid journals; the consumer is a legacy fallback guarded by idempotency (`AffiliateAccountingConsumer.cs:329-334`).
6. **Incomplete Redis coverage.** Only the analytics summary endpoint is Redis-cached; the rest of the portal reads Mongo directly. Ranking recalc is a full-collection daily sweep (`AffiliateRankingService.cs:143-296`).
7. **`AffiliatePayoutRequest.PayoutRequestStatus` has no explicit "Created" vs lifecycle gaps**; `RemainingAmount` reconciliation relies on decimal tolerance (`:230-241`).
8. **Cross-store accounting ambiguity.** `AffiliateAccount.TenantId` is "audit-only" but still referenced in some logs/queries (e.g. `AffiliateService.cs:1487` logs `AffiliateTenant`); potential confusion source.
9. **Manual delivery evidence override break-glass** exists (`AffiliateSettlementOptions.AllowManualDeliveryEvidenceOverride`) — a production-safe guard that, if mis-set, weakens approval authority.
10. **No hierarchy/MLM.** Flat model; any future multi-tier commission requires new aggregates.

---

## Implementation Follow-up Items

| # | Title | Current Implementation | Why | Future Improvement | Priority | Status |
|---|---|---|---|---|---|---|
| 1 | Fraud detection subsystem | Account suspension + cookie validation only | No self-referral/abuse detection | Build fraud runtime (score, blacklist, velocity limit) | High | Open |
| 2 | Redis read cache for portal | Mongo direct reads | Scale portal without DB pressure | Add Redis in front of Mongo read models | Medium | Open |
| 3 | Retire v1 fallback + legacy bridge | v1 path + `AffiliateAccountId` bridge coexist | Partial migration | Complete v2-only migration, drop dual-write | Medium | Open |
| 4 | Split `AffiliateService` | 2791-line god service | Maintainability/blast radius | Extract Attribution/Commission/Settlement services | High | Open |
| 5 | Unify payout accounting posting | Two posting paths (event + inline) | Double code, idempotency-only safety | Single authority (orchestrator) posts; event consumer deprecated | Medium | Open |
| 6 | Repair-on-read for analytics | Attribution-only reconciliation | Mongo can drift from SQL | Add periodic analytics reconciliation job | Medium | Open |
| 7 | Last-click / multi-touch attribution | First-click cookie only | Limits attribution accuracy | Add override rules + multi-touch model | Low | Open |
| 8 | Audit-only TenantId cleanup | `AffiliateAccount.TenantId` still logged | Confusion risk | Remove all reads of legacy TenantId | Low | Open |

---

## Certification

### Implementation Confidence
**High (0.9).** Every major subsystem was read directly from source (`AffiliateAccount`, `AffiliateUser`, `AffiliateAttribution`, `AffiliateCommissionLedger`, `AffiliateBalanceLedger`, `AffiliatePayoutRequest`, `AffiliateSettlementOrchestrator`, `AffiliatePayoutService`, `AffiliateService` attribution/commission/approval/refund, `AffiliateReferralMiddleware`, consumers, `AffiliateAccountingConsumer`, `AffiliateRankingService`, outbox/events). No subsystem was documented from comments/markdown alone.

### Architecture Completeness
**High.** Identity, tracking, attribution, conversion, commission, settlement, withdrawal, payout, fraud-boundary, analytics, CQRS, projections, outbox, cross-domain, invariants, decisions, debt all covered with citations.

### Runtime Authority Verification
**Verified.** SQL = authoritative for money; Mongo/ES = derivative; critical reads hit SQL; fail-closed payout gate confirmed (`AffiliateSettlementOrchestrator.cs:1563`).

### Cross-Domain Verification
**Verified** for Merchant (read-only product/order observation), Payments (capture events), Accounting (posts via `IFinancialPostingService`), Treasury (cashbox resolve), CommerceHub (store-scoped money, marketplace outbox readiness gate), SuperAdmin (category governance via `AdminAffiliateCategoryController` + `SuperAdminSecurityConstants.PolicyName`).

### Verified Capabilities
Affiliate identity & JWT separation; global affiliate / store-scoped commission; first-click cookie attribution; immutable attribution gateway; per-SKU append-only commission ledger; delivery-gated approval; proportional refund reversal; unified fail-closed payout orchestrator; HMAC external-transfer evidence; Mongo/ES projections; ranking; reconciliation host; outbox idempotency.

### Missing Capabilities
Dedicated fraud engine (self-referral, duplicate-click, velocity, blacklist); Redis cache; last-click/multi-touch; affiliate hierarchy/MLM; repair-on-read for analytics; notification subsystem.

### Technical Debt Summary
10 items (see Technical Debt). Highest risk: no fraud runtime + god-service + projection drift.

### Architectural Observations
- The platform is exceptionally disciplined about **money-path authority** (append-only ledgers, Serializable payout, fail-closed gate, HMAC evidence) but **under-invested in fraud/prevention** and **projection self-healing**.
- The AF-19 global-marketplace model is the keystone: almost every boundary rule (store-scoped money, cross-store logging, marketplace readiness gate) exists to support one affiliate → many stores.
- Event-driven CQRS is consistent and idempotent-by-construction; the main risk is Mongo drift with no automated analytics repair.

---

## Affiliate vs Merchant Platform

| Aspect | Merchant Platform Owns | Affiliate Platform Owns | Shared / Synchronized | Read-only to Affiliate |
|---|---|---|---|---|
| Product | lifecycle, pricing, eligibility | `AffiliateProductPolicy` (rate/category), `AffiliatePromotionLink` | policy events → ES/Mongo projection | product catalog read at commission time (snapshot) |
| Order | order lifecycle, items, totals | `AffiliateAttribution` (gateway), commission/balance | order stamped with affiliate ids at checkout | order/order-item read for commission math |
| Payment | capture, refunds | commission earned/reversed, payout | payment events trigger commission | payment status read (gating) |
| Fulfillment | delivery, shipping | commission approval (delivery-gated) | delivery events trigger approval | delivery status read |
| Accounting | GL, journals | posts via `IFinancialPostingService` | earned/paid journals | account resolution |
| Wallet | customer wallet | affiliate `PayoutWallet` (payout destination) | none | — |

Boundary: Merchant is the **owner of commerce**; Affiliate is a **monetization observer + payable ledger**. Affiliate never writes merchant order/payment/fulfillment rows; it only *reads snapshots* and *emits its own ledgers*.

## Affiliate vs CommerceHub

CommerceHub is the **marketplace** (vendor→customer) layer. Relationship to Affiliate:

- **Marketplace Attribution**: affiliate links can promote marketplace/vendor products; attribution works identically (global affiliate, `StoreTenantId` = selling store/vendor tenant).
- **Marketplace Commission**: same `AffiliateCommissionLedger`; separated by `StoreTenantId`, not a separate ledger.
- **Marketplace Orders**: observed via marketplace events; the payout readiness gate inspects `MarketplaceEventOutboxMessage` health (`AffiliateSettlementOrchestrator.cs:1826`, `:2068`) — i.e. affiliate payout is blocked if the marketplace event backbone is failed/stale.
- **Vendor Attribution / Customer Attribution / Revenue Attribution**: affiliate attributes *referral*; CommerceHub owns vendor/customer/revenue attribution. Affiliate does not own marketplace revenue attribution — it only earns commission on the store-scoped sale.
- **Ownership**: CommerceHub owns marketplace order/runtime; Affiliate owns its commission/balance derived from those orders.
- **Synchronization**: event-driven; affiliate reads marketplace outbox health as a *gating signal*, not a data source. No affiliate writes into CommerceHub.

## Architectural Synthesis (cross-platform patterns)

Shared patterns observed across Merchant/Payments/Treasury/Accounting/Shipping/Identity/Analytics and the Affiliate Platform:

1. **Runtime Authority**: every bounded context owns its aggregates; cross-context reads are event/snapshot-derived, never shared write.
2. **SQL Authority**: PostgreSQL is the system of record everywhere money/legal truth lives.
3. **Projection Authority**: Mongo/ES/Redis are derivative read models; rebuildable.
4. **CQRS**: write = SQL + transaction; read = projection.
5. **Outbox**: MassTransit outbox guarantees atomic DB-write + event-publish; consumers are idempotent.
6. **Replay**: reconciliation hosts / idempotent sinks repair dropped or reordered events.
7. **Repair-on-Read**: critical reads (balance, attribution) re-validate SQL truth inside transactions.
8. **Fail-Closed Design**: irreversible operations (payout, approval) gate on backbone health — affiliate's `EnsureAffiliateOutwardMoneyMovementReadyAsync` mirrors payment/treasury "unresolved payment blocks outward money" patterns.
9. **Cross-Domain Isolation**: `X-Tenant-Id` stripped; each context validates its tenant/store boundary.
10. **Availability Gates**: payout blocked if accounting/integration/marketplace/stock outboxes are failed or stale (`AffiliateSettlementOrchestrator.cs:1563-1644`) — same philosophy as "suspense not terminal," "provider gating zero=unavailable."
11. **Ownership Boundaries**: affiliate owns identity/attribution/commission/balance/payout; merchant owns commerce; CommerceHub owns marketplace; treasury owns cash; accounting owns GL.

The Affiliate Platform is the canonical example of this architecture applied to **monetization**: it is a pure derivative observer of commerce that *authors its own append-only financial ledgers* and refuses to move money unless the entire platform backbone is consistent.

---

*End of canonical architecture. All claims implementation-backed; citations reference `PointOfSale.*` source as of branch `feature/new-pr210`.*
