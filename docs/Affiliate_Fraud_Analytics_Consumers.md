# Affiliate Platform — Fraud, Analytics Runtime & Event Consumer Catalog

> Canonical architecture doc. Implementation is the ONLY source of truth; every claim cites `file:line`.
> Scope: `PointOfSale.Core`, `PointOfSale.Infrastructure`, `PointOfSale.API`. UI/React excluded.

---

## 1. Fraud Runtime

### VERDICT: NO dedicated fraud-detection subsystem exists in the Affiliate Platform.

A repo-wide search for `fraud|Fraud|self-referral|SelfReferral|blacklist|BlackList|rate limit|RateLimit|suspicious|Suspicious|abuse|Abuse|duplicate click|cookie abuse` returned **zero** matches inside the affiliate domain (`PointOfSale.Core/Entities/Affiliate`, `PointOfSale.Infrastructure/Consumers/Affiliate`, `PointOfSale.Infrastructure/MongoDB/Documents/Affiliate`). The only repo-wide hits for those terms are:
- API-level **auth rate limiting** on controller endpoints (`[EnableRateLimiting(...)]`) — e.g. `AffiliateAuthController.cs:96,228,...` (`AffiliateAuthRateLimit`), `AuthController.cs`, `CheckoutController.cs`, `CommerceHubCartController.cs`. This is generic HTTP throttling, **not** affiliate fraud scoring.
- CMS boilerplate text `"All orders are subject to availability, fraud screening, and final operational approval."` at `AdminCmsPagesController.cs:835` — static copy, no backing logic.
- A comment about `"Duplicate clicks suppressed via unique index"` in `AffiliateAnalyticsConsumers.cs:16,25` — this is **idempotency**, not fraud detection (see below).

### What IS implemented (fraud-adjacent controls, all manual or integrity-focused)

| Mechanism | Where | Nature |
|---|---|---|
| Account status lifecycle | `AffiliateAccount.cs:8-12, 108-147` | **The ONLY abuse-control lever.** Statuses: `Pending → Active → Suspended → Active`; `Active → Closed` (terminal). `Suspend(reason)` / `Close(reason)` require a human-supplied reason. No automated trigger — fully **manual admin** action. Suspended/Closed accounts cannot earn commission (`IsActive => Status == Active`, `AffiliateAccount.cs:45`). |
| Duplicate-click suppression | `AffiliateAnalyticsConsumers.cs:16,25,85-92` | Unique compound index `(TenantId, SessionId, ProductId)` on `affiliate_clicks`. Prevents the *same session* re-counting the *same product* click. This is analytics idempotency, **not** bot/fraud detection. |
| IP hashed, not stored | `AffiliateAnalyticsDocuments.cs:55-59` | `IpHash` is SHA-256 of client IP — PII protection, not fraud signal. No IP-reputation or velocity check. |
| Manual-payment verification gate | `AffiliateAnalyticsConsumers.cs:215-225`, `AffiliatePaymentCapturedConsumers.cs:207-382` (AF-36) | Wallet/COD/manual orders do **not** count as conversions or fire ranking signals until an admin approves the payment (`WalletPaymentApprovedEvent`). This is payment-verification, a fraud-adjacent control, but there is no automated suspicious-order scoring. |
| Tenant-authority validation | e.g. `AffiliateProjectionConsumers.cs:49-65,184-189` | Events rejected if `TenantId`/`StoreTenantId`/`EventVersion` are ambiguous or stale — guards against misattribution from bad events, not malicious affiliates. |
| Event-lag telemetry | `AffiliateConsumerHelper.DetectAndRecordLag` (`AffiliatePaymentCapturedConsumers.cs:477-495`) | Observability only (>30s lag warning). No automated response. |

### What is NOT implemented (explicitly absent)

- ❌ `FraudScore` / risk score — no such field on `AffiliateAccount` or any affiliate document.
- ❌ Blacklist (IP, device, affiliate, or customer) — no blacklist entity, table, or collection.
- ❌ Self-referral detection — an affiliate can transact through their own referral code; `AffiliateOrderAttributionConsumer.cs:64-238` attributes on `ReferralCode` alone with **no** check that `UserId` (buyer) ≠ `UserId` (affiliate owner).
- ❌ Suspicious-account / suspicious-order flags — none.
- ❌ Abuse / velocity / click-fraud heuristics — none (unique-index dedup is the only click guard).
- ❌ Cookie-stuffing / cookie-abuse detection — none.
- ❌ Automated suspension — `Suspend`/`Close` are only ever called by admin code paths; no event consumer or service auto-suspends.

**Conclusion:** Fraud is handled *only* by manual admin suspension/closure of the `AffiliateAccount` (`AffiliateAccount.cs:120-147`) and manual payment approval for wallet/COD orders. There is no automated fraud engine, scoring, blacklisting, or self-referral prevention.

---

## 2. Analytics Runtime

### Read paths (Controller)
`AffiliateAnalyticsController.cs` (`[Authorize(Policy="AffiliateOnly")]`, `:33`) — **read-only GET**, zero SQL for analytics except resolving `StoreConfig` names for `top-stores` (`:261-268`). All KPIs sourced from MongoDB daily projections; 30s Redis cache on `/summary` (`:70-129`).

| Endpoint | Source doc / query | `file:line` |
|---|---|---|
| `GET /summary?range` | `AffiliateAnalyticsDailyDocument` summed over `>= sinceDate`; returns `totalClicks, totalOrders, totalEarnings, conversionRate` | `:60-132` |
| `GET /trend?range` | daily time-series grouped by `Date` | `:139-174` |
| `GET /top-products?range` | `ProductEarnings[]` aggregated by `ProductId` | `:181-218` |
| `GET /top-stores?range` | grouped by `TenantId` (+ SQL `StoreConfigs` for names) | `:225-280` |
| `GET /clicks?page` | raw `AffiliateClickDocument` paginated | `:286-324` |

### Write paths (Consumers → MongoDB)

**`AffiliateAnalyticsDocuments.cs`**
- `AffiliateClickDocument` (`affiliate_clicks`) — `:28-78`. Append-only click record. Fields: `TenantId, AffiliateAccountId, ReferralCode, ProductId, SessionId, UserAgent, IpHash (SHA-256), ClickedAtUtc` + conversion bridge `OrderId, ConvertedAtUtc`. Unique index `(TenantId, SessionId, ProductId)` (`:25`).
- `AffiliateAnalyticsSummaryDocument` (`affiliate_analytics_summaries`) — `:91-127`. Per `(TenantId, AffiliateAccountId)`. `TotalClicks, TotalConversions, ConversionRate, TopProducts[]`.
- `TopProductSubDocument` — `:129-140`.

**`AffiliateAnalyticsDailyDocument.cs`** (`affiliate_analytics_daily`, AF-24) — `:29-72`. One doc per `(AffiliateAccountId, TenantId, Date)`, `Id = "{AffiliateAccountId}:{TenantId}:{yyyy-MM-dd}"`. Counters: `Clicks, Orders, Earnings (decimal)`, plus `ProductEarnings[]` (`DailyProductEarning`, `:74-97`) and idempotency `ProcessedEventIds`.

**Compute logic**
- **Clicks / TopProducts:** `AffiliateLinkClickedConsumer` (`AffiliateAnalyticsConsumers.cs:27-176`) — inserts click doc (dup-suppressed), `$inc TotalClicks`, maintains `TopProducts`. Also `AffiliateAnalyticsDailyClickConsumer` (`AffiliateAnalyticsDailyProjectionConsumer.cs:25-107`) `$inc Clicks` per day.
- **Conversions / ConversionRate:** `AffiliateClickConversionConsumer` (`:190-331`) links most-recent unconverted click → order, `$inc TotalConversions` + recomputes `ConversionRate = TotalConversions/TotalClicks`. Gated: skipped if `RequiresPaymentVerification` (`:218-225`); for wallet orders the deferred signal fires from `WalletPaymentApprovedAffiliateConsumer` (`AffiliatePaymentCapturedConsumers.cs:292-382`).
- **Orders / Earnings / per-product earnings:** `AffiliateAnalyticsDailyEarningConsumer` (`AffiliateAnalyticsDailyProjectionConsumer.cs:113-334`) — `$inc Orders, Earnings`, maintains `ProductEarnings` (with race-safe upsert, `:246-333`). Rejects non-positive commission (`:143-150`).
- **Idempotency infra:** `AffiliateProjectionEventReceiptDocument` + `AffiliateAnalyticsProjectionReceipt` (`:336-434`), `AffiliateAnalyticsDailyAtomicUpdate.ApplyOnceAsync` (`:436-477`) — durable per-event receipts + `ProcessedEventIds` set ensure exactly-once apply.

**Computed metrics:** clicks, visitors (sessions implied via clicks), conversions, conversion rate, revenue/earnings (commission), top products, top stores, daily trend. Note: "revenue" here is **commission earnings**, not order GMV (GMV lives in `AffiliateOrderDocument.PaidAmount`). Visitor-level dedup is session+product only, not true unique-visitor counting.

---

## 3. Event Consumers Catalog

All consumers live in `PointOfSale.Infrastructure/Consumers/Affiliate/`. 11 files, 38 `IConsumer` class implementations (some classes handle multiple event interfaces). "Authoritative" = writes SQL ledger / domain state; "Projection" = writes MongoDB read-model only.

### `AffiliateOrderAttributionConsumer.cs`
- **`AffiliateOrderAttributionConsumer`** `:43` — trigger `AffiliateOrderPlacedEvent`. **Authoritative.** Calls `AffiliateService.AttributeOrderToAffiliateAsync` → creates `AffiliateAttribution` (SQL) + emits `AffiliateAttributedEvent`; also writes a placeholder `AffiliateOrderDocument` (`Status=AttributedPendingPayment`) to Mongo for immediate portal visibility (`:163-207`). Idempotent.

### `AffiliatePaymentCapturedConsumers.cs` (5 classes)
- **`OnlinePaymentCapturedAffiliateConsumer`** `:36` — trigger `OnlinePaymentCapturedEvent`. **Authoritative.** Delegates to `AffiliateService.RecordCommissionForOrderAsync` (SQL ledger). Full telemetry + lag detection.
- **`CodDeliveredAffiliateConsumer`** `:122` — trigger `OrderCodDeliveredEvent`. **Authoritative.** Same delegation (COD collected at delivery).
- **`WalletPaymentApprovedAffiliateConsumer`** `:207` — trigger `WalletPaymentApprovedEvent`. **Authoritative + Projection.** Records commission (SQL) AND fires deferred conversion + ranking signals (links click→order in Mongo, `$inc TotalConversions`) that were gated at attribution (AF-36, `:292-382`).
- **`OrderCollectionAccountingPostedAffiliateConsumer`** `:398` — trigger `OrderCollectionAccountingPostedEvent`. **Authoritative.** Replays commission recording at the accounting-posted boundary to prevent capture consumers outrunning ledger truth.
- **`OrderCompletedAffiliateConsumer`** `:534` — trigger `OrderCompletedEvent`. **Authoritative.** Delegates to `AffiliateService` for completion-stage commission handling.

### `AffiliateSettlementConsumers.cs`
- **`AffiliateOrderDeliveredConsumer`** `:21` — triggers `OrderDeliveredEvent` + `FulfillmentDeliveredEvent`. **Authoritative.** On delivery, calls `AffiliateService.ApproveCommissionForDeliveredOrderAsync` to approve earned commission for payout. Telemetry + lag (`:65-128`).

### `AffiliateRefundConsumers.cs`
- **`OrderRefundedAffiliateConsumer`** `:33` — trigger `OrderRefundedEvent`. **Authoritative.** Loads order + refund lines, iterates items, calls `ReverseCommissionForOrderItemAsync` per item (item-level granularity bridge). Idempotent via reversal key (`:185-189`).
- **`OrderItemRefundedAffiliateConsumer`** `:204` — trigger `OrderItemRefundedEvent`. **Authoritative.** Precise per-item commission reversal with idempotency key (`:277-286`).

### `AffiliateAccountingConsumer.cs`
- **`AffiliateCommissionEarnedAccountingConsumer`** `:46` — trigger `AffiliateCommissionEarnedEvent`. **Authoritative (GL).** Posts journal: Debit `6110` (Affiliate Marketing Expense) / Credit `2105` (Affiliate Payable). Idempotency `AFFILIATE_COMM_EARNED:{TenantId}:{OrderId}` (`:130`).
- **`AffiliateCommissionPaidAccountingConsumer`** `:205` — trigger `AffiliateCommissionPaidEvent`. **Authoritative (GL).** Posts journal: Debit `2105` / Credit Cash (`1010`/cashbox). **Out-of-order guard**: throws (retry) if Earned journal missing (`:272-294`). Partial payouts blocked.

### `AffiliateProjectionConsumers.cs` (5 classes)
- **`AffiliateAttributedProjectionConsumer`** `:25` — trigger `AffiliateAttributedEvent`. **Projection.** Upserts `AffiliateDashboardDocument` + placeholder `AffiliateOrderDocument`. Authority-validated (`:49-65`).
- **`AffiliateCommissionEarnedProjectionConsumer`** `:161` — trigger `AffiliateCommissionEarnedEvent`. **Projection.** Updates `AffiliateOrderDocument` (status `Earned`), `AffiliateDashboardDocument` (`$inc TotalEarned/NetEarnings/TotalOrders`), `AffiliateEarningsSummaryDocument` (per-currency). Independent idempotency per doc (`:243-316`).
- **`AffiliateCommissionReversedProjectionConsumer`** `:349` — trigger `AffiliateCommissionReversedEvent`. **Projection.** Marks item `isReversed`, recomputes status `Reversed`/`PartiallyReversed`/`Earned`; adjusts dashboard + summary `TotalReversed`/`-NetEarnings`.
- **`AffiliateCommissionApprovedProjectionConsumer`** `:529` — trigger `AffiliateCommissionApprovedEvent`. **Projection.** Marks commission approved in `AffiliateOrderDocument` + dashboard/summary `TotalApproved`.
- **`AffiliateCommissionPaidProjectionConsumer`** `:760` — trigger `AffiliateCommissionPaidEvent`. **Projection.** Marks paid; adjusts `TotalPaid`/net in dashboard + summary.

### `AffiliateAnalyticsConsumers.cs`
- **`AffiliateLinkClickedConsumer`** `:27` — trigger `AffiliateLinkClickedEvent`. **Projection.** Inserts `AffiliateClickDocument` (dup-suppressed), `$inc TotalClicks`, updates `TopProducts`, increments product-intelligence click signal.
- **`AffiliateClickConversionConsumer`** `:190` — trigger `AffiliateAttributedEvent`. **Projection.** Links click→order, `$inc TotalConversions` + recomputes `ConversionRate` (skipped when `RequiresPaymentVerification`).

### `AffiliateAnalyticsDailyProjectionConsumer.cs`
- **`AffiliateAnalyticsDailyClickConsumer`** `:25` — trigger `AffiliateLinkClickedEvent`. **Projection.** `$inc Clicks` on daily doc; durable receipt + `ProcessedEventIds` idempotency.
- **`AffiliateAnalyticsDailyEarningConsumer`** `:113` — trigger `AffiliateCommissionEarnedEvent`. **Projection.** `$inc Orders, Earnings` + per-product earnings (race-safe). Rejects non-positive commission.

### `AffiliateCategoryUpdatedConsumer.cs`
- **`AffiliateCategoryUpdatedConsumer`** `:30` — triggers `AffiliateCategoryUpdatedEvent` + `AffiliateCategoryDeactivatedEvent`. **Projection.** Bulk `UpdateMany` on `AffiliateProductIntelligenceDocument` to sync category name/slug (or nullify on deactivation). Fenced for idempotency (`:143-244`).

### `AffiliateProductPolicyProjectionConsumer.cs` (15 event interfaces)
- **`AffiliateProductPolicyProjectionConsumer`** `:29` — triggers `AffiliateProductPolicyChangedEvent` plus `ProductMediaChangedEvent, ProductUpdatedEvent, ProductDeletedEvent, ProductArchivedEvent, ProductPublishStatusChangedEvent, ProductPriceChangedEvent, ProductStockChangedEvent, ProductVariantStockChangedEvent, PromotionCreated/Updated/Deleted/ExpiredEvent, StoreProfileUpdatedEvent, TenantCurrencyChangedEvent`. **Projection.** Maintains `public_affiliate_products` (Mongo) + `public_affiliate_stores` read models and Elasticsearch product index for storefront discovery. (Note: the legacy Mongo/ES policy-branch body is wrapped in `#if false` at `:98`, so live path uses `ConsumeAffiliateProductPolicyChangedFromSqlAsync`.)

### `AffiliateProductIntelligenceProjectionConsumer.cs` (11 event interfaces)
- **`AffiliateProductIntelligenceProjectionConsumer`** `:50` — triggers `ProductCreatedEvent, ProductUpdatedEvent, ProductMediaChangedEvent, ProductDeletedEvent, ProductArchivedEvent, ProductPublishStatusChangedEvent, ProductStockChangedEvent, ProductVariantStockChangedEvent, AffiliateProductPolicyChangedEvent, StoreProfileUpdatedEvent, TenantCurrencyChangedEvent`. **Projection.** Builds/maintains `AffiliateProductIntelligenceDocument` (rich discovery read-model: price, stock, variants, commission rate, store context). SQL re-read for stock/price authority before visibility changes; `ProcessedEventIds` (capped 500) for idempotency.

---

## Summary of gaps (flagged)
- **No fraud engine** — only manual `Suspend`/`Close` (`AffiliateAccount.cs:120-147`) + manual wallet/COD payment approval. No scoring, blacklist, self-referral, or abuse detection anywhere in the affiliate domain.
- **No automated unique-visitor metric** — "visitors" is approximated by session-scoped click dedup, not true distinct-user counting.
- **Analytics "revenue" = commission earnings**, not order GMV (GMV available separately via `AffiliateOrderDocument.PaidAmount`).
