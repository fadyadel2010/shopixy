# Tracking & Attribution Runtime — FINAL CANONICAL ARCHITECTURE

> Implementation-backed reverse engineering. Implementation is the ONLY source of truth.
> Every claim is cited to `file:line`. Prior markdown, comments, DTO/controller/service names, and
> marketing terminology are secondary and were re-verified against code.
> Repo: PointOfSale / Shopixy (.NET 9 monorepo). Backend authority lives in `PointOfSale.API`,
> `PointOfSale.Core`, `PointOfSale.Infrastructure`; frontends (`PointOfSale.Storefront`,
> `PointOfSale.Web`) are read-only emitters of canonical events.

---

## Executive Summary

The "Tracking & Attribution Runtime" is **NOT a browser-pixel / cookie / client-side tracking
runtime**. Reverse engineering the implementation shows it is, in reality, a **server-side,
multi-tenant Event Collection + Analytics Dispatch + Marketing Attribution runtime** whose job is to:

1. Ingest **POS-native canonical analytics events** from the storefront (never raw provider events);
2. **Durably persist** them to a hot store (Redis) and a cold store (MongoDB) with idempotency by `EventId`;
3. **Capture first-touch / last-touch marketing attribution** from UTM parameters on the first PageView
   and link it to purchases via `OrderAttribution`;
4. **Fan out** a curated subset of events to external providers — **Google Analytics 4 (Measurement
   Protocol) and Meta Conversions API (server-side) only** — plus an internal Admin Realtime dashboard;
5. **Never emit PurchaseCompleted from the client** — purchase events are emitted authoritatively by the
   `OrderPaidEvent` consumer, gated on `PaymentStatus` truth, so analytics revenue cannot diverge from
   accounting.

Key real-world findings (all evidence-backed):

- **No Snapchat, LinkedIn, Pinterest, Google Tag Manager, TikTok runtime provider, or Custom Pixel exist.**
  Only GA4 + Meta CAPI are implemented as external dispatchers. TikTok remains a pixel-value override
  placeholder (`PixelProvider.TikTok`, `ProductPixelOverride.ApplyToTikTok`) but is **not** an
  `AnalyticsProvider` and has no dispatcher class.
- **There are zero browser pixels.** No `gtag.js`, `fbq`, Meta Pixel script, GA tag, or GTM on the
  storefront. The frontend only POSTs canonical JSON events to `/api/storefront/analytics/event` and
  `/events`. All provider dispatch is server-side.
- **Client-submitted monetary `Value` is nulled on ingest.** Only the server-emitted purchase event
  carries `Value = order.Total` (resolved from SQL). Analytics therefore cannot invent revenue.
- **Revenue/purchase truth for dashboards is read from PostgreSQL ledger entries**, not from Mongo/Redis
  projections (`StorefrontAnalyticsAuthorityReader`).
- **Attribution is UTM + referrer based only.** No click IDs (`fbclid`/`gclid`/`ttclid`/`msclkid`) are
  captured anywhere. Only `FirstTouch` and `LastTouch` are implemented; Linear/TimeDecay/PositionBased/
  DataDriven are absent.
- **Dispatch is fire-and-forget with no retry/backoff/circuit-breaker/dead-letter.** Provider failures
  are logged and dropped.
- **There is no Privacy/Consent runtime.** No GDPR/CCPA/consent-mode/cookie-banner code exists. IP is
  anonymized (last octet zeroed) and PII is SHA-256 hashed before Meta CAPI, but there is no consent
  gate, no opt-in/opt-out, and no data-retention enforcement beyond store TTLs.

**Runtime classification (supported by evidence):** This is fundamentally a **Server-Side Event
Collection + Analytics Measurement + Marketing Attribution runtime** — a *hybrid* of Event Collection,
Analytics, Measurement, and Attribution, with a thin (GA4+Meta only) provider-adaptation layer. It is
**NOT** a tracking-pixel runtime, NOT a customer-journey/identity-graph runtime, and NOT a conversion-API
ownership runtime for payments.

---

## Architecture Vision

The system is designed around one principle repeated across the code: **POS owns the event schema;
providers only receive a curated, transformed projection of it — and never see the storefront directly.**
(`AnalyticsEvent.cs` doc; `AnalyticsProvider.cs` doc; `AnalyticsEventValidator.cs` "NEVER TRUST
FRONTEND FOR" block.)

- Canonical event → validated server-side → durably stored (Redis hot + Mongo cold) → attributed →
  dispatched to enabled providers in priority order, each failure isolated.
- Purchase truth flows *backwards* from the payment lifecycle: `OrderPaidEvent` →
  `AuthoritativePurchaseAnalyticsConsumer` → `AnalyticsEventReceivedEvent`. The storefront can never
  declare a purchase.
- Attribution truth is derived from the same canonical events (UTM on PageView; order linkage on
  PurchaseCompleted) and stored immutably.

---

## Runtime Purpose

Collect, store, attribute, and forward storefront behavioral events into (a) internal realtime/historical
analytics and (b) a small set of external marketing platforms — while keeping every monetary/revenue
figure anchored to SQL commerce + accounting truth.

Evidence:
- `PointOfSale.Core/Analytics/AnalyticsEvent.cs:3-23` — "single source of truth for all storefront
  analytics"; idempotent by `EventId`, tenant-isolated by `TenantId`.
- `PointOfSale.Core/Events/AnalyticsEvents.cs:5-24` — event pipeline: controller → validator → EventBus →
  consumer → Mongo (cold durable fence) + Redis (hot projection).
- `PointOfSale.Infrastructure/Consumers/AuthoritativePurchaseAnalyticsConsumer.cs:14-18` — purchase events
  come from the server, not the client.

## Runtime Scope

In scope: event ingestion validation, hot/cold storage, session/visitor counting, UTM first/last-touch
attribution, order attribution, purchase/revenue funnels, GA4 + Meta CAPI server-side dispatch, per-tenant
pixel value override, realtime admin dashboard feeds.

Out of scope (owned by peer runtimes): payment lifecycle, order creation, inventory, shipping, accounting
GL, identity issuance, affiliate attribution (separate bounded context), and external provider-side
processing (Meta/GA own what they do with the data).

## Runtime Owns

- The canonical `AnalyticsEvent` schema (`PointOfSale.Core/Analytics/AnalyticsEvent.cs`).
- Redis hot store (`PointOfSale.Infrastructure/Redis/Services/AnalyticsHotStoreService.cs`) — TTL-based
  realtime counters/events.
- MongoDB cold store `analytics_events` collection
  (`PointOfSale.Infrastructure/MongoDB/Services/AnalyticsColdStoreService.cs`) — append-only.
- Attribution contexts (`AttributionContext`) and order attributions (`OrderAttribution`)
  (`PointOfSale.Infrastructure/Attribution/*`, `PointOfSale.Infrastructure/MongoDB/Documents/*`).
- Per-tenant provider *settings* (`TenantAnalyticsSettings`) and *pixel value override*
  (`PixelTrackingConfig`, `ProductPixelOverride`) — config only; not the secrets' lifecycle.

## Runtime Does NOT Own

- **Purchase / Payment truth.** Purchase events are emitted only by
  `AuthoritativePurchaseAnalyticsConsumer` from `OrderPaidEvent`, gated on current SQL order state
  `PaymentStatus.Collected` or `PaymentStatus.Settled` plus active tenant status
  (`AuthoritativePurchaseAnalyticsConsumer.cs:34-58`). The Storefront controller explicitly rejects client `PurchaseCompleted`
  (`StorefrontAnalyticsController.cs:117-121`).
- **Revenue / Net revenue.** Read from PostgreSQL `LedgerEntries` (`EcommerceSales` account,
  `Posted`/`Reversed` journals) via `StorefrontAnalyticsAuthorityReader`
  (`StorefrontAnalyticsAuthorityReader.cs:37-92`). Mongo/Redis must never replace this for financial
  metrics (`:31-34`).
- **Monetary Value on client events.** Nulled on ingest (`AnalyticsEventValidator.cs:293-295`).
- **Affiliate attribution / commission.** Separate `PointOfSale.Core/Entities/Affiliate/AffiliateAttribution.cs`
  and `PointOfSale.Infrastructure/Consumers/Affiliate/*` — a different bounded context (click IDs there
  are affiliate click IDs, not marketing click IDs).
- **Identity truth, inventory, shipping, payment processing, accounting GL.**

---

## Architecture Principles

1. **POS-native canonical schema, provider-agnostic.** `AnalyticsEventType` forbids GA-style / UI-only
   events; providers are an enum + dispatcher mapping (`AnalyticsEventType.cs:6-14`,
   `AnalyticsProvider.cs:6-10`).
2. **Server-side dispatch only; no browser pixels.** GA4 + Meta CAPI documented as "NO gtag.js / NO Meta
   Pixel on frontend" (`GoogleAnalyticsDispatcher.cs:16-19`, `MetaConversionsApiDispatcher.cs:17-22`).
3. **Never trust the frontend for identity, IP, UA, auth state, or money.** `AnalyticsEventValidator.cs:18-24`;
   `CustomerId` from JWT, `IpAddress`/`UserAgent` from server context, `Value = null`
   (`AnalyticsEventValidator.cs:146-172`, `:283-302`).
4. **Idempotency by EventId, tenant isolation by TenantId.** Rejected if `TenantId == Guid.Empty`
   (`AnalyticsEventConsumer.cs:76-80`, `AnalyticsEvent.cs:32-37`).
5. **Failure isolation across stores and providers.** MongoDB is the durable replay fence and is written
   before Redis projection; a duplicate Mongo insert stops projection/dispatch, Redis failure is non-critical,
   and dispatch failure must not block storage/attribution (`AnalyticsEventConsumer.cs`,
   `AnalyticsDispatcherPipeline.cs:9-14`).
6. **Revenue anchored to SQL.** Authoritative purchase emission + ledger-backed revenue reads.
7. **Immutable attribution.** `OrderAttribution` immutable after creation
   (`PointOfSale.Core/Analytics/Attribution/OrderAttribution.cs:6-13`).
8. **Privacy-by-construction (limited):** IP anonymized, Meta PII SHA-256 hashed, GA
   `NonPersonalizedAds = true`. No consent gate exists (see gaps).

---

---

## Core Discovery — What Tracking / Attribution / Analytics / Conversion REALLY are here

- **Tracking** = durable capture of POS-native canonical `AnalyticsEvent`s from the storefront
  (PageView, ProductView, AddToCart, etc.) into Redis + Mongo, plus internal counters
  (`AnalyticsEventConsumer.cs`, `AnalyticsHotStoreService.cs`, `AnalyticsColdStoreService.cs`).
- **Attribution** = derivation of marketing source/medium/campaign from UTM (first PageView) and the
  immutable link of an order to that source at purchase time (`AttributionContextResolver.cs`,
  `OrderAttributionResolver.cs`). UTM + referrer only; no click IDs.
- **Analytics** = the read/measurement layer over the stored events: funnels, sessions, visitors,
  realtime dashboard, and revenue/purchase reporting sourced from SQL (`AnalyticsFunnelService.cs`,
  `StorefrontAnalyticsAuthorityReader.cs`).
- **Conversion** = the subset of canonical events mapped to provider "conversion" events (GA `purchase`,
  Meta `Purchase`) plus the internal `PurchaseCompleted` concept. Conversion is *declared by the server*
  from `OrderPaidEvent`, never by the client.
- **They are NOT separate runtimes.** They are stages of ONE pipeline: a single canonical event is
  stored, attributed, and (if mapped) dispatched. Attribution and analytics are derived *projections* of
  the same stored events, not independent systems.

Conclusion: **one runtime, multiple derived projections.** Tracking (capture) → Attribution (derive
source) → Analytics/Conversion (measure + forward). Nested/projection-based, not independent.

---

## Pixel Runtime (Provider Implementation Truth)

Search across the repo confirms the *only* implemented provider dispatchers are in
`PointOfSale.Infrastructure/Analytics/Dispatchers/`:
- `GoogleAnalyticsDispatcher.cs` (GA4 Measurement Protocol)
- `MetaConversionsApiDispatcher.cs` (Meta Conversions API)
- `AdminRealtimeDispatcher.cs` (internal realtime dashboard — not an external provider)

Dispatchers present: `AdminRealtimeDispatcher.cs`, `AnalyticsDispatcherPipeline.cs`,
`AnalyticsSettingsService.cs`, `GoogleAnalyticsDispatcher.cs`, `IAnalyticsDispatcher.cs`,
`IAnalyticsSettingsService.cs`, `MetaConversionsApiDispatcher.cs`. **There is NO `TikTokDispatcher.cs`,
`SnapchatDispatcher.cs`, `LinkedInDispatcher.cs`, `PinterestDispatcher.cs`, or `GtmDispatcher.cs`.**

| Provider | Implementation status | Evidence |
|---|---|---|
| Meta Pixel (browser) | NOT implemented | "NO Meta Pixel on frontend" `MetaConversionsApiDispatcher.cs:18` |
| Meta Conversions API (server) | IMPLEMENTED | `MetaConversionsApiDispatcher.cs` (full) |
| TikTok Pixel (browser) | NOT implemented | no frontend pixel |
| TikTok Events API | CONFIG PLACEHOLDER ONLY — no runtime provider, settings, or dispatcher | `PixelProvider.TikTok` in `PixelTrackingConfig.cs:91`; no dispatcher file; `AnalyticsProvider` intentionally excludes TikTok |
| Snapchat | NOT implemented | 0 dispatcher; not in `AnalyticsProvider` enum |
| Google Analytics (browser gtag) | NOT implemented | "NO gtag.js on storefront" `GoogleAnalyticsDispatcher.cs:17` |
| Google Analytics 4 (Measurement Protocol, server) | IMPLEMENTED | `GoogleAnalyticsDispatcher.cs` |
| Google Tag Manager | NOT implemented | 0 references in code |
| LinkedIn Insight | NOT implemented | not in enum; 0 dispatcher |
| Pinterest | NOT implemented | not in enum; 0 dispatcher |
| Custom Pixel | NOT implemented | not in enum |

**Authoritative provider set = {GA4 Measurement Protocol, Meta Conversions API} (+ internal Admin
Realtime).** TikTok is a pixel-value override placeholder only, not a dispatch runtime provider.

---

## Tracking Configuration Runtime

Per-tenant provider settings: `TenantAnalyticsSettings` (`PointOfSale.Core/Analytics/TenantAnalyticsSettings.cs`):
- `GoogleAnalyticsSettings { Enabled, MeasurementId, ApiSecret (encrypted), DebugMode }` (`:43-67`)
- `MetaAnalyticsSettings { Enabled, PixelId, AccessToken (encrypted), TestEventCode }` (`:78-100`)
- Keyed by `TenantId` (`:17`). **Per-tenant, NOT per-storefront** — no storefront key exists on this entity.

Pixel value override config: `PixelTrackingConfig` (`PointOfSale.Core/Analytics/PixelTrackingConfig.cs`),
`ITenantOwned` keyed by `TenantId` (`:14-21`): `IsEnabled`, `Strategy` (Percentage|Fixed), `Value`,
`AppliesTo` (JSON array of `PixelProvider`). Per-product override: `ProductPixelOverride`
(`PointOfSale.Core/Analytics/ProductPixelOverride.cs`), keyed by `TenantId`+`ProductId`, with
`ApplyToMeta`/`ApplyToGoogle`/`ApplyToTikTok` booleans.

Secrets / security:
- Stored encrypted at rest via ASP.NET **Data Protection API** (`AnalyticsSettingsService.cs:2,33,38,301-332`):
  `ApiSecret` and `AccessToken` are `Protect()`ed before persistence, `Unprotect()`ed on read; purpose
  `"PointOfSale.Analytics.Settings"`.
- Cached in Redis, invalidated on change (`TenantAnalyticsSettings.cs:5-11`).
- **Storage form:** provider credentials live in a JSON-blob table `TenantAnalyticsConfigurations`
  (`TenantAnalyticsConfiguration` entity, `SettingsJson` column) — `AnalyticsSettingsService.cs:73,107-121,343-347`
  — NOT a column-per-secret table. Pixel value-override config lives separately in `PixelTrackingConfigs`
  (`PixelTrackingConfig.cs:14`; `AddPixelTrackingConfig.sql:4-14`) and does NOT hold secrets.
- Documented "Never logged or exposed in API responses" (`TenantAnalyticsSettings.cs:7-10`).

Sandbox / Production:
- GA4 `DebugMode` routes to `/debug/mp/collect` (`GoogleAnalyticsDispatcher.cs:156-157`).
- Meta `TestEventCode` routes to test events (`MetaConversionsApiDispatcher.cs:180-183`).
- **No formal Sandbox/Production environment switch** beyond these debug flags; no separate "sandbox
  credential set" in code.

Enable/Disable: per-provider `Enabled` flag, enforced by each dispatcher's `IsEnabled()`
(`GoogleAnalyticsDispatcher.cs:65-72`, `MetaConversionsApiDispatcher.cs:63-70`). Disabled/misconfigured
providers return `Skipped` (no throw).

Validation: tenant settings validated at dispatch (`AnalyticsDispatcherPipeline.cs:60`).

Rotation: **No secret-rotation logic found.** Data Protection keys are platform-managed; no app-level
rotation/versioning of `ApiSecret`/`AccessToken` beyond re-save.

---

---

## Customer Event Runtime (events that REALLY exist)

The canonical enum `AnalyticsEventType` (`PointOfSale.Core/Analytics/AnalyticsEventType.cs`) defines the
complete, authoritative event set. Implementation-backed events:

| Event | Exists | Maps to | Notes |
|---|---|---|---|
| PageView (1) | YES | GA `page_view`, Meta `PageView` | required PageUrl |
| ProductImpression (2) | YES | GA `view_item_list` | ProductId required; NOT sent to Meta (`MetaConversionsApiDispatcher.cs:266`) |
| ProductView (3) | YES | GA `view_item`, Meta `ViewContent` | ProductId required |
| SearchPerformed (4) | YES | GA `search` | SearchQuery required; NOT sent to Meta |
| FilterApplied (5) | YES | GA `select_item` | Filters required; NOT sent to Meta |
| AddToCart (6) | YES | GA `add_to_cart`, Meta `AddToCart` | ProductId/Qty required; client Value is nulled |
| RemoveFromCart (7) | YES | GA `remove_from_cart` | NOT sent to Meta |
| BeginCheckout (8) | YES | GA `begin_checkout`, Meta `InitiateCheckout` | client Value is nulled |
| PurchaseCompleted (9) | YES | GA `purchase`, Meta `Purchase` | **server-only** (OrderPaidEvent); server Value = order.Total |
| PaymentFailed (10) | YES | GA `purchase_error` | NOT sent to Meta |
| CheckoutFinalizeFailure (11) | YES | (internal) | storefront fidelity signal |
| AuthStateDegraded (12) | YES | (internal) | fidelity signal |
| CheckoutSelectionInvalidated (13) | YES | (internal) | fidelity signal |
| CartStateMismatchDetected (14) | YES | (internal) | fidelity signal |
| GuestPaymentRecoverySuccess (15) | YES | (internal) | fidelity signal |
| CartUnexpectedClearBlocked (16) | YES | (internal) | fidelity signal |
| TrackingTokenRemovedFromUrl (17) | YES | (internal) | fidelity signal |
| AuthDegradedRecovery (18) | YES | (internal) | fidelity signal |
| CheckoutStateLeakPrevented (19) | YES | (internal) | fidelity signal |

NOT present (from the prompt's list): Wishlist, Lead, Contact, Subscribe, CouponApplied,
ShippingSelected, PaymentSelected, Refund, Login, Registration. These are not in the enum and have no
dispatch mapping. (Refund/Lead/etc. are owned by other runtimes or absent.)

Note: events 10–19 are **storefront-fidelity / security signals**, not marketing events; most are
not forwarded to providers. This confirms the runtime doubles as a *storefront behavior telemetry* sink.

Validation of required fields: `AnalyticsEventValidator.ValidatePayload`
(`AnalyticsEventValidator.cs:197-268`). Payload `Value` always nulled for client events
(`:283-302`). Timestamp bounds: not future (> +5 min), not older than 24h
(`:42, :79-86`).

---

## Commerce Runtime Integration (what this runtime touches)

- **Identity:** `CustomerId` is taken from JWT by the controller; guest events use `SessionId`
  (`AnalyticsEventValidator.cs:146-153`). No identity issuance; no anonymous→known stitching found.
- **Storefront:** the ONLY emitter. `PointOfSale.Storefront/src/analytics/*` POSTs canonical events
  (`AnalyticsClient.ts`). Storefront is read-only for analytics and never declares purchases.
- **Builder/CMS:** not referenced by analytics ingestion.
- **Cart:** session-scoped; cart events (AddToCart/RemoveFromCart) are emitted from storefront. No cart
  ownership by this runtime.
- **Checkout:** BeginCheckout emitted from storefront; checkout *authority* belongs to
  `CheckoutService`. This runtime only records the event.
- **Payment:** this runtime does NOT process payment. It **consumes** `OrderPaidEvent`
  (`AuthoritativePurchaseAnalyticsConsumer.cs:18`) to emit the authoritative PurchaseCompleted.
- **Order:** purchase event links to `OrderId`; revenue read from order/ledger SQL.
- **Inventory:** not read by analytics (dispatch may read `OrderItems` for pixel value override only,
  `GoogleAnalyticsDispatcher.cs:325-329`).
- **Shipping:** not an analytics event. (Shipment *tracking* is a different domain — shipping carrier
  tracking numbers, not marketing tracking.)
- **Reporting:** dashboards read derived data; revenue from `StorefrontAnalyticsAuthorityReader` (SQL).
- **Affiliate:** separate bounded context; affiliate click IDs are unrelated to marketing attribution.
- **CommerceHub:** separate marketplace; analytics here is storefront/merchant tenant-scoped.
- **Notifications / Treasury:** not invoked by this runtime.

---

## Event Authority (who may declare what)

| Event / concept | Authoritative owner | Evidence |
|---|---|---|
| PageView, ProductView, AddToCart, BeginCheckout, Search, etc. (pre-purchase behavior) | Storefront (client) via `StorefrontAnalyticsController` | `StorefrontAnalyticsController.cs:102-237` |
| PurchaseCompleted | **Server** — `AuthoritativePurchaseAnalyticsConsumer` from `OrderPaidEvent` | `AuthoritativePurchaseAnalyticsConsumer.cs:18,34-101`; client rejected at `StorefrontAnalyticsController.cs:117-121` |
| Payment Success | Payment lifecycle (`IPaymentLifecycleService` emits `OrderPaidEvent`) | `OrderPaidEvent` in `PointOfSale.Core/Events/PaymentEvents.cs:84` |
| Refund | Payment/Order lifecycle; NOT an analytics event | absent from enum |
| Delivery | Shipping runtime; NOT analytics | absent |
| Customer Registration / Login | Identity runtime; NOT analytics events | absent from enum |
| Lead | NOT modeled | absent |
| Search / Product View / Cart Update / Wishlist | Storefront client (behavioral) | enum + validator |
| Attribution (source/medium) | This runtime, derived from UTM on PageView + OrderAttribution at purchase | `AttributionContextResolver.cs`, `OrderAttributionResolver.cs` |

---

## Runtime Authority Divergence (Browser vs Backend, Pixel vs Truth)

- **Browser Event != Business Event.** Client can emit `BeginCheckout`/`AddToCart` freely, but a
  *purchase* is only real when `OrderPaidEvent` fires with a settled payment status. Client
  `PurchaseCompleted` is rejected (`StorefrontAnalyticsController.cs:117-121`).
- **Frontend != Backend on money.** Client `Value` is nulled; backend purchase event supplies
  `order.Total` and `PixelValueResolver` may override it (`AnalyticsEventValidator.cs:293-295`,
  `AuthoritativePurchaseAnalyticsConsumer.cs:86-96`).
- **Pixel != Conversion API.** N/A as separate paths — both are server-side dispatchers; the "pixel"
  concept (browser) does not exist. Meta CAPI is the only Meta path.
- **Purchase != Payment.** Purchase = behavioral/analytics signal; Payment = lifecycle truth. Analytics
  purchase is *derived from* payment (`OrderPaidEvent`), never the reverse.
- **Checkout != Order.** BeginCheckout is a behavioral event; Order is created by checkout authority.
- **Delivery != Purchase.** Delivery is shipping-owned; not in analytics.
- **Refund != Cancellation.** Neither is an analytics event. Current purchase emission skips orders whose
  current SQL `PaymentStatus` is `Refunded` or `PartiallyRefunded`; net revenue effects are reflected by
  SQL ledger reads, not analytics projections. There is still no explicit refund/reversal provider signal
  in this runtime (see Debt).
- **Tracking != Reporting.** Tracking stores raw events; Reporting (revenue) reads SQL ledger, explicitly
  not the analytics stores (`StorefrontAnalyticsAuthorityReader.cs:31-34`).
- **Analytics != Accounting.** Revenue analytics is a *read projection* of posted ledger entries; the
  runtime never writes GL.
- **Storefront != Business Truth.** Storefront can under/over-report behavior; server re-derives purchase
  and revenue from SQL.

---

---

## Browser Tracking Runtime (what the frontend actually does)

There is **no browser tracking runtime** in the pixel sense. The storefront emits canonical events only:

- `PointOfSale.Storefront/src/analytics/AnalyticsClient.ts` — POSTs to
  `/api/storefront/analytics/event` and `/events`; includes `X-Session-Id` (and optional
  `X-Customer-Id`) headers; 10s timeout; respects 429.
- `PointOfSale.Storefront/src/analytics/AnalyticsSession.ts` — `SessionId` from shared bootstrap
  sessionManager; survives reloads; rotates after 30 min inactivity; NOT authentication.
- `PointOfSale.Storefront/src/analytics/AnalyticsContextResolver.ts` + `AnalyticsEventBuilder.ts` —
  build the canonical event; UTM is passed through the PageUrl query string (extracted server-side).
- **Cookies:** only the bootstrap session cookie; no tracking/analytics cookies, no `fp`/fingerprint
  cookie, no `_fbp`/`_fbc`.
- **Local Storage:** `analytics_last_activity` timestamp only (`AnalyticsSession.ts:18`). No identity
  graph, no cross-device, no anonymous-id stitching.
- **Consent:** none. No cookie banner, no opt-in gating the analytics POST.
- **Fingerprint:** not implemented (no fingerprinting code found).

Conclusion: the "browser" side is a thin canonical-event emitter keyed by a server-issued `SessionId`.

---

## Server Tracking Runtime

- **Server-side only.** No client pixels. All provider dispatch is server-side
  (`GoogleAnalyticsDispatcher.cs:16-19`, `MetaConversionsApiDispatcher.cs:17-22`).
- **Conversion API / backend events:** GA4 Measurement Protocol and Meta CAPI are the backend event
  channels.
- **Webhook / Message Bus:** ingestion uses MassTransit (`AnalyticsEventReceivedEvent` published by
  controller, consumed by `AnalyticsEventConsumer`); purchase emission subscribes to `OrderPaidEvent`.
- **Event Replay:** broker-level retry (MassTransit exponential, 5 retries)
  (`AnalyticsEventConsumer.cs:299-303`). Mongo idempotency makes replays safe.
- **Retry / Dead Letter / Recovery:** **NONE at the provider-dispatch level.** Dispatch is fire-and-forget
  inside a 30s linked CTS; failures are logged and dropped
  (`AnalyticsEventConsumer.cs:142-154`, `AnalyticsDispatcherPipeline.cs:46-98`). No Polly/circuit-breaker/
  no dead-letter queue found (grep: 0 hits for `RetryAsync|DeadLetter|Polly|CircuitBreaker`).
  → If GA4/Meta is down, the event is stored (durable) but the provider never receives it; no
  compensation. This is a delivery-guarantee gap (see Debt).

---

## Attribution Runtime

- **Models implemented:** `FirstTouch` and `LastTouch` only
  (`PointOfSale.Core/Analytics/Attribution/AttributionModel.cs:17-29`). Linear / TimeDecay /
  PositionBased / DataDriven are documented as "FUTURE (NOT MVP)" and are absent from code.
- **UTM:** `utm_source/medium/campaign/content/term` extracted from PageUrl query string at validation
  (`AnalyticsEventValidator.cs:341-369`) and stored on the canonical event context.
- **Campaign / Source / Medium / Content / Term:** captured via UTM (`:166-170`).
- **Session Attribution:** `AttributionContext` is captured on the first PageView per session; default
  `source=direct, medium=none` (`AttributionContext.cs:96-108`, `AttributionContextResolver.cs:110-153`).
- **Referrer inference:** if no UTM, source/medium inferred from referrer host (search engines →
  `organic`; social networks → `social`; else `referral`)
  (`AttributionContextResolver.cs:173-212`, known-engine lists `:47-56`).
- **Order Attribution:** on `PurchaseCompleted`, both FirstTouch and LastTouch `OrderAttribution` records
  are created, immutable, linked by `OrderId`+`SessionId`
  (`OrderAttributionResolver.cs:62-170`, `OrderAttribution.cs:6-13`).
- **Affiliate Attribution:** separate bounded context (`AffiliateAttribution`), uses its own click IDs —
  NOT part of this marketing attribution runtime.
- **Click IDs (fbclid/gclid/ttclid/msclkid/_fbc/_fbp):** **NOT captured anywhere** (grep: 0 hits outside
  affiliate click IDs). Attribution is UTM + referrer only.
- **Cross-device / identity stitching:** NOT implemented.

Storage: `AttributionContextStore` writes Redis (hot, 30-day TTL) + MongoDB (cold, append-only)
(`AttributionContextStore.cs:13-23,95-105`); `OrderAttributionStore` writes MongoDB
(`OrderAttributionStore.cs:116` InsertOne). First/Last touch resolved by querying both stores
(`AttributionContextStore.cs:129-195`).

---

## Deduplication Runtime

- **Event ID:** `AnalyticsEvent.EventId` is the idempotency key
  (`AnalyticsEvent.cs:28-30`). Empty EventId is rejected/regenerated
  (`AnalyticsEvents.cs:37-43`).
- **Dedup enforcement (cold):** MongoDB unique index `(TenantId, EventId)`
  (`AnalyticsColdStoreService.cs:44-46`); duplicate insert returns `AlreadyExists`
  (`:114-121`). Consumer short-circuits on `AlreadyExists` (`:90-97`).
- **Dedup enforcement (hot):** Redis `analytics:tenant:{tid}:processed:{eventId}` marker, 7-day TTL
  (`AnalyticsHotStoreService.cs:32,117-120`; `AnalyticsEventConsumer.cs:48,120-124`).
- **Idempotency window:** 7 days (`AnalyticsEventConsumer.cs:48`).
- **Duplicate Purchase:** prevented two ways — (1) deterministic `EventId = SHA256("purchase-completed:
  {orderId}")` so the same order always yields the same event id
  (`AuthoritativePurchaseAnalyticsConsumer.cs:104-108`); (2) `OrderAttributionResolver.HasAttributionAsync`
  guards re-attribution (`:97-108`).
- **Retry / Replay:** broker retry re-delivers; Mongo unique index makes it safe
  (`AnalyticsEventConsumer.cs:299-303`).
- **Out-of-order / Late events:** consumer processes in arrival order; Mongo stores all; no strict
  ordering guarantee beyond idempotency. Late client events older than 24h are rejected at validation
  (`AnalyticsEventValidator.cs:42,83-86`). Server purchase events use `PaidAt` as OccurredAtUtc
  (`AuthoritativePurchaseAnalyticsConsumer.cs:69`).

---

## Conversion Runtime

- **Purchase:** canonical `PurchaseCompleted` → GA `purchase` + Meta `Purchase`. Server-emitted only.
- **Lead / Registration / Checkout / Subscription / Phone Call / Contact / Custom / Offline / Server /
  Enhanced Conversion:** **NOT implemented** as distinct conversion concepts. Only the generic event→
  provider mapping above exists. `BeginCheckout` is the closest "checkout" signal; no Subscription/Call/
  Contact events.
- **Server Conversion:** GA4 Measurement Protocol and Meta CAPI *are* server conversions; this is the
  entire "server conversion" surface.
- **Enhanced Conversion:** not implemented (no hashed email/phone sent to Google enhanced-conversion
  endpoint). Meta CAPI hashes `SessionId` only (external_id), not email/phone
  (`MetaConversionsApiDispatcher.cs:192-211`).

---

## Analytics Runtime

- **Funnels:** `AnalyticsFunnelService` defines system funnels (Purchase: PageView→ProductView→AddToCart→
  BeginCheckout→PurchaseCompleted; Discovery; Checkout) and computes them from stored events
  (`AnalyticsFunnelService.cs:15-63`). Funnel *steps* are the canonical enum values.
- **Sessions / Visitors / Returning Visitors / Bounce / Engagement:** partially implemented. Session is
  defined by `SessionId` (30-min inactivity, frontend); `AnalyticsSessionJourneyService` derives journey
  from stored events. Returning-visitor/bounce/engagement metrics are NOT clearly implemented as
  first-class computed metrics (no dedicated computation found beyond counters). Flag as partial.
- **Revenue / AOV / LTV / CAC / ROAS:** revenue read from SQL ledger (`StorefrontAnalyticsAuthorityReader`);
  AOV derivable; **LTV/CAC/ROAS are NOT computed in code** (no such service found) — would require cost
  data the runtime does not ingest.
- **Attribution Reports:** `AdminAnalyticsAttributionController` + `OrderAttribution` store provide
  source/campaign ROI by reading Mongo `OrderAttribution` documents.
- **Customer Journey:** `AnalyticsSessionJourneyService` + Mongo session index
  (`AnalyticsColdStoreService.cs:60-65`).
- **Realtime:** `AdminRealtimeDispatcher` (priority 0) feeds Redis counters for the admin dashboard
  (`AdminRealtimeDispatcher.cs`).

---

---

## Privacy Runtime

**No dedicated Privacy/Consent runtime exists.** Evidence: grep for `Consent|CookieBanner|OptIn|OptOut|
consent_mode|Gdpr|Ccpa` across `PointOfSale.API`, `PointOfSale.Infrastructure`, `PointOfSale.Core`,
`PointOfSale.Storefront/src` returns only unrelated WhatsApp rate-limit matches — no consent code.

Measures present (privacy-by-construction, not consent-driven):
- **IP anonymization:** last octet zeroed (IPv4), last segment zeroed (IPv6)
  (`AnalyticsEventValidator.cs:505-525`). Applied server-side before storage/dispatch.
- **Meta PII hashing:** `SessionId` SHA-256 hashed as `external_id`; IP already anonymized; UA passed
  (`MetaConversionsApiDispatcher.cs:188-211,350-354`). No email/phone hashing.
- **GA non-personalized ads:** `NonPersonalizedAds = true` (`GoogleAnalyticsDispatcher.cs:167`).
- **Secret encryption:** Data Protection API at rest (`AnalyticsSettingsService.cs:301-332`).
- **No PII in canonical payload:** payload guards strip provider-specific fields
  (`AnalyticsPayloadGuard` referenced at `StorefrontAnalyticsController.cs:150`).

Gaps (evidence-backed):
- **No consent gate / opt-in / opt-out.** Analytics fire regardless of user choice.
- **No GDPR/CCPA data-subject controls.** No erasure, no portability, no lawful-basis check.
- **No cookie banner / consent mode.** No `gtag('consent')` (and no gtag at all).
- **No data-retention policy enforcement** beyond store TTLs (Redis 24h–7d, Mongo unbounded append-only,
  AttributionContext 30d). Mongo `analytics_events` has no documented purge/retention job.

---

## Event Pipeline (REAL, implementation-backed)

```
Storefront (AnalyticsClient.ts)
   │  POST /api/storefront/analytics/event|events   (X-Session-Id, X-Customer-Id, X-Tenant-Id)
   ▼
StorefrontAnalyticsController.SubmitEvent            (StorefrontAnalyticsController.cs:102)
   │  class [EnableRateLimiting("analytics")]; batch capped at MaxBatchSize=50 (:52-53,:67,:251-277)
   │  - ResolveTenantId (X-Tenant-Id stripped from client, service-resolved)   (:111)
   │  - REJECT PurchaseCompleted from client                          (:117-121)
   │  - Rate limit (customer|session|ip)                              (:134)
   │  - Payload guard (size/PII)                                      (:150)
   │  - Currency resolve (tenant base currency, NOT EGP-only)        (:165, AnalyticsTenantCurrencyResolver.cs:38-45)
   │  - AnalyticsEventValidator.Validate  (server-side identity/IP/UA, null Value, UTM extract, bounds)  (:180)
   ▼
AnalyticsEventReceivedEvent.Create(event)            (AnalyticsEvents.cs:35)
   │  PublishAsync → MassTransit outbox (durable)    (:204, controller SaveChangesAsync :205)
   ▼
AnalyticsEventConsumer.Consume                        (AnalyticsEventConsumer.cs:70)
   │  1. ValidateTenantId (reject Guid.Empty)        (:76-80)
   │  2. Mongo cold store (idempotent, unique idx)   (:89-104)
   │  3. Redis hot store + counters + processed mark (:106-132)
   │  4. DispatchToAllAsync (fire-and-forget, 30s)   (:142-154)  ──┐
   │  5. ProcessAttributionAsync                      (:156-165)  │
   ▼                                                     │         │
AnalyticsDispatcherPipeline.DispatchToAllAsync        │         │
   │  for each enabled IAnalyticsDispatcher (priority)│         │
   ├─ AdminRealtimeDispatcher (0)  → Redis            │         │
   ├─ GoogleAnalyticsDispatcher (10) → GA4 MP         │         │
   └─ MetaConversionsApiDispatcher (20) → Meta CAPI   │         │
                                                         │         │
OrderPaidEvent ──▶ AuthoritativePurchaseAnalyticsConsumer ──▶ AnalyticsEventReceivedEvent
   (payment lifecycle)        (AuthoritativePurchaseAnalyticsConsumer.cs:18)   (server-side purchase)
```
Authoritative purchase branch (server-only):
```
Payment lifecycle → OrderPaidEvent (current SQL order is Collected/Settled and tenant is Active)
   → AuthoritativePurchaseAnalyticsConsumer (deterministic EventId = SHA256(purchase-completed:orderId))
   → AnalyticsEventReceivedEvent → same consumer pipeline above
```

---

## Provider Adapter Runtime

`IAnalyticsDispatcher` contract (`IAnalyticsDispatcher.cs`): stateless, never mutates events, never blocks
>5s, checks `IsEnabled`, returns `AnalyticsDispatchResult`, MUST NOT throw.

| Adapter | Endpoint | Auth | Mapping | Retry/Backoff | Health |
|---|---|---|---|---|---|
| GoogleAnalyticsDispatcher | `https://www.google-analytics.com/mp/collect` (+ `/debug/mp/collect` if DebugMode) | `measurement_id`+`api_secret` in URL | switch on `AnalyticsEventType` → GA4 event name | **none** (fire-and-forget, 5s timeout) | none (logs only) |
| MetaConversionsApiDispatcher | `graph.facebook.com/v18.0/{pixelId}/events?access_token=` | PixelId + AccessToken in URL | switch → Meta event; `event_id`=EventId for CAPI dedup; PII SHA-256 | **none** (5s timeout) | none (logs only) |
| AdminRealtimeDispatcher | Redis hot store | internal | increments realtime counters | best-effort | n/a (always enabled) |
| TikTok | **no class** | — | — | — | — |

Evidence: `GoogleAnalyticsDispatcher.cs:46-48,65-72,154-158,172-311`;
`MetaConversionsApiDispatcher.cs:44-46,63-70,152-155,213-274,350-354`;
`AnalyticsDispatcherPipeline.cs:46-98` (never throws, logs failures).

**No retry, rate-limit backoff, circuit breaker, or dead-letter** at the adapter or pipeline level. A
provider HTTP failure is recorded via `AnalyticsDispatchResult.Fail` and logged; the durable event remains
in Mongo but the provider is not retried. (Search: 0 hits for Polly/RetryAsync/DeadLetter/CircuitBreaker
in `PointOfSale.Infrastructure/Analytics`.)

---

## Runtime State Machines (implementation-backed)

| State machine | Implementation | Evidence |
|---|---|---|
| Tracking/Ingest state | linear: Received → Validated → Published → Stored(hot+cold) → Dispatched; failed-store throws to broker retry; dispatch failure is terminal (logged) | `StorefrontAnalyticsController.cs:108-237`, `AnalyticsEventConsumer.cs:70-166` |
| Consent state | **NONE** | no consent code |
| Conversion state | PurchaseCompleted only enters via OrderPaidEvent gated on PaymentStatus; deterministic idempotent | `AuthoritativePurchaseAnalyticsConsumer.cs:34-101` |
| Sync/Provider state | per-dispatcher enabled flag; transient Success/Skipped/Fail result; no persistent state | `IAnalyticsDispatcher.IsEnabled`, `AnalyticsDispatchResult` |
| Attribution state | Context captured on first PageView; immutable after session; OrderAttribution immutable | `AttributionContextResolver.cs`, `OrderAttribution.cs:6-13` |
| Retry state | MassTransit broker exponential retry (5) for storage failures; no provider retry | `AnalyticsEventConsumer.cs:299-303` |

---

## Runtime Authority Matrix

| Concept | Owner (writer of truth) | Reader | Projection/Store | Notes |
|---|---|---|---|---|
| Session | Storefront (SessionId issuer) | Analytics | Redis+Mongo | 30-min inactivity rotation |
| Visitor | Identity (CustomerId) / anonymous SessionId | Analytics | Mongo | no stitching |
| Customer | Identity runtime | Analytics (read) | — | CustomerId from JWT |
| Campaign/Source/Medium | This runtime (derived from UTM) | Attribution store | Mongo+Redis | UTM+referrer only |
| Conversion | This runtime (from OrderPaidEvent) | GA/Meta + dashboards | external+Mongo | server-only |
| Purchase | Payment/Order lifecycle | Analytics (consume) | — | not authored by analytics |
| Revenue | Accounting GL (ledger) | Analytics (read via AuthorityReader) | SQL (authoritative) | `StorefrontAnalyticsAuthorityReader` |
| Order Value | Order (SQL) | Analytics (read) | SQL | Value nulled on client ingest |
| Refund | Payment/Order lifecycle | (none in analytics) | — | not an analytics event |
| Product View / Search / Cart / Wishlist | Storefront client | Analytics | Redis+Mongo | behavioral |
| Lead | NOT modeled | — | — | absent |
| Pixel config | This runtime (config) | Dispatchers | SQL (`TenantAnalyticsSettings`,`PixelTrackingConfig`) | encrypted secrets |
| Attribution | This runtime | Attribution controllers | Mongo+Redis | immutable |
| Consent | **NO OWNER** | — | — | not implemented |
| Analytics (derived metrics) | This runtime (projections) | Dashboards | Redis+Mongo+SQL | non-authoritative vs business truth |

---

---

## CQRS

- Write model: canonical AnalyticsEvent ingested -> AnalyticsEventReceivedEvent published -> consumer writes Mongo (cold durable fence), then Redis (hot projection), then attribution stores. Single write path, no separate command model beyond the controller/validator.
- Read model: dashboards/funnels read from Redis (realtime) and Mongo (historical); revenue reads from SQL via StorefrontAnalyticsAuthorityReader. Read models are derived projections.
- Redis (hot): TTL caches - events 24h, processed/idempotency 7d, counters 25h, recent lists 1h (AnalyticsHotStoreService.cs:31-34). Purpose: realtime dashboard.
- Mongo (cold): append-only analytics_events, attribution_contexts, order_attributions (AnalyticsColdStoreService.cs:11-21; indexes :37-89). Purpose: historical analytics + attribution.
- SQL: TenantAnalyticsSettings, PixelTrackingConfig, ProductPixelOverride (config truth); LedgerEntries/Orders (revenue/purchase truth).
- Outbox: the canonical generic outbox carries AnalyticsEventReceivedEvent (AnalyticsEvents.cs); IdempotencyKey = "analytics:{eventId}" (:52). Durable publish + SaveChangesAsync (StorefrontAnalyticsController.cs:203-205).
- Replay: broker retry replays events; Mongo unique index makes replay idempotent. No separate replay job for analytics found.
- Repair: hot store is best-effort (Redis failure non-critical); cold store is the durable source.
- Projection: AnalyticsColdStoreService.MapToDocument/MapToEvent projects canonical -> Mongo doc (:295-409). Attribution store projects context -> Redis+Mongo (AttributionContextStore.cs:95-105).
- Canonical Events: AnalyticsEvent is the single canonical event; AnalyticsEventReceivedEvent is the integration envelope.

---

## Snapshot Runtime (immutable snapshots)

- Campaign / UTM / Click ID: UTM captured into AttributionContext at first PageView; immutable after session (AttributionContext.cs:15-23,73-81). No click-ID snapshot (not captured).
- Order Attribution: OrderAttribution immutable after creation (OrderAttribution.cs:6-13); both FirstTouch + LastTouch stored for comparison (OrderAttributionResolver.cs:110-159).
- Purchase Attribution: same as OrderAttribution.
- Revenue Snapshot: derived on read from SQL ledger (no stored snapshot; computed by StorefrontAnalyticsAuthorityReader.GetCommerceSnapshotAsync).
- Conversion Snapshot: OrderAttribution + the durable PurchaseCompleted event serve as the conversion snapshot.
- Customer Snapshot: not a dedicated snapshot; customer journey reconstructed from Mongo events by SessionId/CustomerId.
- Consent Snapshot: NONE (no consent).

---

## Cross Runtime Authority Graph

| Peer | Reads from analytics | Writes used by analytics | Events to analytics | Runtime authority | Projection | Ownership |
|---|---|---|---|---|---|---|
| Identity | - | CustomerId (JWT) | - | Identity owns CustomerId | n/a | analytics reads only |
| Storefront | - | emits events | AnalyticsEventReceivedEvent | Storefront owns emission | Redis+Mongo | analytics consumes |
| Builder/CMS | - | - | - | - | - | no interaction |
| Cart | - | cart events | - | Cart owns state | - | analytics records events |
| Checkout | - | BeginCheckout event | - | Checkout authority owns order | - | analytics records |
| Payment | OrderPaidEvent | - | OrderPaidEvent | Payment lifecycle owns truth | n/a | analytics consumes purchase |
| Order | order/ledger SQL | OrderId link | - | Order/Accounting own | SQL | analytics reads revenue |
| Inventory | OrderItems (pixel value) | - | - | Inventory owns | - | read-only for override |
| Shipping | - | - | - | Shipping owns | - | no analytics event |
| Reporting | dashboards | revenue (SQL) | - | Reporting reads projections | Redis+Mongo+SQL | non-authoritative |
| Affiliate | - | - | - | Affiliate owns its attribution | separate Mongo | bounded context apart |
| CommerceHub | - | - | - | CommerceHub separate | - | tenant-scoped only |
| Notifications | - | - | - | Notifications own | - | no interaction |
| Treasury | - | - | - | Treasury own | - | no interaction |
| Search | search events | - | - | Search owns | - | analytics records |

---

## Business Invariants (implementation-backed)

1. Tenant isolation is mandatory. TenantId == Guid.Empty -> event rejected (AnalyticsEventConsumer.cs:76-80, AnalyticsEvent.cs:32-37).
2. PurchaseCompleted must be server-emitted. Client submission rejected (StorefrontAnalyticsController.cs:117-121).
3. Purchase signal requires current settled payment and active tenant authority. OrderPaidEvent consumer only emits when the current SQL order is Collected/Settled and the tenant is Active (AuthoritativePurchaseAnalyticsConsumer.cs:34-58).
4. Client money is never trusted. Value nulled on client ingest (AnalyticsEventValidator.cs:293-295); server supplies order.Total.
5. Revenue analytics anchored to SQL ledger. Mongo/Redis must not replace ledger for financial metrics (StorefrontAnalyticsAuthorityReader.cs:31-34).
6. Idempotency by EventId. Duplicate (TenantId, EventId) rejected by Mongo unique index (AnalyticsColdStoreService.cs:44-46).
7. Order attribution immutable & idempotent. Re-attribution guarded (OrderAttributionResolver.cs:97-108); deterministic purchase EventId (AuthoritativePurchaseAnalyticsConsumer.cs:104-108).
8. Attribution defaults to direct. Missing context -> source=direct, medium=none (OrderAttributionResolver.cs:126-131).
9. Storage failure isolation. Mongo cold storage is the durable replay/idempotency fence; duplicate cold-store writes stop further processing, cold-store failure throws to broker retry, and Redis projection failure is non-critical (AnalyticsEventConsumer.cs:89-132).
10. Provider failure isolation. Dispatch failure never blocks storage/attribution (AnalyticsDispatcherPipeline.cs:9-14,46-98).
11. Secrets encrypted at rest. ApiSecret/AccessToken protected via Data Protection (AnalyticsSettingsService.cs:301-332).
12. IP anonymized; Meta PII hashed. (AnalyticsEventValidator.cs:505-525, MetaConversionsApiDispatcher.cs:188-211).

---

## Architectural Decisions (WHY - implementation evidence)

- Server-side dispatch only (no browser pixels): avoids tag-manager drift, keeps POS as the single event owner, and lets the backend enforce validation/encryption. Explicit in dispatcher docs (GoogleAnalyticsDispatcher.cs:16-19, MetaConversionsApiDispatcher.cs:17-22).
- Reject client PurchaseCompleted: prevents revenue inflation/fraud from spoofed client purchases; purchase truth is derived from the payment lifecycle (StorefrontAnalyticsController.cs:117-121, AuthoritativePurchaseAnalyticsConsumer.cs).
- Null client Value: analytics revenue must equal accounting revenue, not self-reported totals (AnalyticsEventValidator.cs:293-295).
- Durable outbox + idempotent Mongo: survives broker/crash replays without double-count (AnalyticsEvents.cs:35-54, AnalyticsColdStoreService.cs:44-46).
- Fire-and-forget dispatch: ingestion latency must not depend on external provider availability; durability is in Mongo, not in provider receipt (AnalyticsDispatcherPipeline.cs:46-98).
- First/Last touch only: MVP scope; complex models deferred (AttributionModel.cs:6-15).
- Pixel value override: merchants can report modified values to ad platforms for privacy/strategy without affecting orders/finance (PixelTrackingConfig.cs:6-13).
- SQL-ledger revenue reads: analytics dashboards must not show numbers that diverge from posted accounting (StorefrontAnalyticsAuthorityReader.cs:31-34).

---

## Technical Debt (implementation-backed)

1. No provider retry / dead-letter. GA4/Meta outages drop events permanently (no compensation). Evidence: no retry/Polly/dead-letter in PointOfSale.Infrastructure/Analytics; pipeline logs-and-drops (AnalyticsDispatcherPipeline.cs:80-98).
2. TikTok remains a pixel-value override placeholder. `PixelProvider.TikTok` and `ProductPixelOverride.ApplyToTikTok` exist, but TikTok is not an `AnalyticsProvider` and has no dispatcher.
3. No Privacy/Consent runtime. No GDPR/CCPA/consent-mode/opt-out. Legal exposure for EU/CA traffic. Evidence: 0 consent hits.
4. No explicit refund/reversal analytics or provider signal. Refunded/partially refunded current SQL orders are no longer emitted as new PurchaseCompleted events, but there is still no dedicated analytics refund event or provider reversal dispatch.
5. No LTV/CAC/ROAS computation. Revenue/attribution exist, but cost data is never ingested, so ROAS/CAC are unimplemented despite being named in the prompt.
6. Funnels/bounce/returning-visitor metrics partial. Session journey exists; bounce/engagement not first-class computed metrics.
7. No data-retention enforcement on Mongo analytics_events (append-only, unbounded).
8. Meta CAPI sends only hashed SessionId, not email/phone - limits Meta match quality / enhanced attribution.
9. Pixel override "Fixed" semantics ambiguous for multi-qty line items (PixelValueResolver.cs:180-204 contains explicit uncertainty comments).
10. Attribution is session-scoped, not cross-device. A customer on two devices yields two attribution contexts; no identity stitching.

---

## Implementation Follow-up Items

| Item | Priority | Status |
|---|---|---|
| Add provider retry/backoff + dead-letter for GA4/Meta dispatch | High | Not started (no code) |
| Document or remove TikTok pixel-value placeholder | Medium | Override config present, no runtime provider |
| Add consent gate (GDPR/CCPA/consent-mode) before analytics emission | High | Not started |
| Add explicit refund/reversal analytics signal if provider adjustment becomes product scope | Medium | Not started |
| Ingest ad cost for ROAS/CAC | Medium | Not started |
| Mongo retention/purge job | Medium | Not started |
| Enhanced conversions (hashed email/phone to GA/Meta) | Low | Not started |
| Cross-device identity stitching for attribution | Low | Not started |

---

## Runtime Classification

This runtime is fundamentally a Server-Side Event Collection + Analytics Measurement + Marketing Attribution runtime - a hybrid of Event Collection, Analytics, Measurement, and Attribution, with a thin (two-provider) server-side dispatch adapter.

Evidence:
- Event Collection: canonical AnalyticsEvent ingest + durable Redis/Mongo stores (AnalyticsEventConsumer.cs, AnalyticsColdStoreService.cs).
- Analytics: funnels, sessions, realtime dashboard, SQL-ledger revenue reads (AnalyticsFunnelService.cs, StorefrontAnalyticsAuthorityReader.cs, AdminRealtimeDispatcher.cs).
- Measurement: GA4 Measurement Protocol + Meta CAPI dispatch, server-side (GoogleAnalyticsDispatcher.cs, MetaConversionsApiDispatcher.cs).
- Attribution: UTM first/last-touch + immutable order attribution (AttributionContextResolver.cs, OrderAttributionResolver.cs).
- NOT a pixel/browser-tracking runtime (no pixels), NOT a customer-journey identity-graph runtime (no stitching), NOT a payment/conversion-ownership runtime (purchase truth is the payment lifecycle's).

It is a support runtime for marketing measurement: it transforms customer interactions and authoritative commerce events into accurate, deduplicated, privacy-aware (anonymized IP, hashed PII, encrypted secrets), multi-provider signals - while deliberately re-anchoring revenue/purchase to SQL truth so business truth is preserved across every participating runtime.

---

## Final Architecture Question

"How does Shopixy transform customer interactions and authoritative commerce events into accurate, deduplicated, privacy-aware, multi-provider marketing and analytics signals while preserving business truth across every participating runtime?"

1. Canonical capture (one schema): The storefront emits only POS-native AnalyticsEvents (AnalyticsClient.ts, AnalyticsEvent.cs); providers never see the storefront and never define the schema (AnalyticsProvider.cs:6-10).
2. Server-side validation & truth re-derivation: The controller/validator nulls client money, takes CustomerId from JWT, captures IP/UA server-side, extracts UTM, enforces 24h/+5m bounds, and rejects client purchases (AnalyticsEventValidator.cs:146-172,283-302; StorefrontAnalyticsController.cs:117-121).
3. Durable, deduplicated storage: Events go through the outbox to a consumer that writes Mongo (cold durable replay fence) before Redis (hot projection); duplicate EventIds stop further projection/dispatch (AnalyticsEventConsumer.cs:48,89-132; AnalyticsColdStoreService.cs:44-46).
4. Authoritative purchase: Purchases are emitted only by AuthoritativePurchaseAnalyticsConsumer from OrderPaidEvent, gated on current SQL PaymentStatus Collected/Settled and active tenant status, with a deterministic EventId (AuthoritativePurchaseAnalyticsConsumer.cs:34-108) - so analytics purchase truth is derived from payment/order truth.
5. Attribution as a derived projection: UTM on first PageView -> AttributionContext; purchase -> immutable First/Last-touch OrderAttribution (AttributionContextResolver.cs, OrderAttributionResolver.cs). No click IDs; no cross-device stitching.
6. Privacy-aware by construction: IP anonymized (:505-525), Meta PII SHA-256 hashed (MetaConversionsApiDispatcher.cs:188-211), GA NonPersonalizedAds=true (GoogleAnalyticsDispatcher.cs:167), secrets encrypted at rest (AnalyticsSettingsService.cs:301-332). (Caveat: no consent gate - see Debt.)
7. Multi-provider, failure-isolated dispatch: AnalyticsDispatcherPipeline fans out to GA4 MP + Meta CAPI (+ internal Admin Realtime) in priority order; each dispatcher checks enablement, maps the canonical event to the provider schema, and fails isolated (logged, never throws) (AnalyticsDispatcherPipeline.cs:46-98; GoogleAnalyticsDispatcher.cs:172-311; MetaConversionsApiDispatcher.cs:213-274).
8. Business truth preserved at the reporting edge: Revenue/purchase dashboards read from PostgreSQL ledger entries, never from Mongo/Redis projections (StorefrontAnalyticsAuthorityReader.cs:31-34).

Where implementation diverges from the prompt's assumptions (implementation wins):
- "Pixel Runtime" (Meta/TikTok/Snap/LinkedIn/Pinterest/GTM/Custom) -> only GA4 + Meta CAPI are real; TikTok is a phantom; the rest do not exist. There are no browser pixels at all.
- "Click IDs (fbclid/gclid/ttclid)" -> not captured. Attribution is UTM + referrer only.
- "Consent / GDPR / CCPA / cookie banner" -> not implemented.
- "Lead / Registration / Refund / Wishlist / Subscription / Contact" events -> not in the canonical enum.
- "Retry / Dead Letter / Circuit Breaker" for providers -> not implemented; dispatch is fire-and-forget.

---

## Certification

- Confidence: High on architecture, pipeline, attribution, authority boundaries, and provider set (all read from primary C# source with file:line citations).
- Completeness: High for the implemented surface; the named-but-absent features (TikTok, Snap, LinkedIn, Pinterest, GTM, consent, click IDs, Lead/Refund events, retry/dead-letter) are explicitly marked as NOT implemented with 0-match evidence.
- Verified present: canonical event schema, outbox+idempotent storage, server-only purchase emission, UTM first/last-touch attribution, GA4+Meta dispatch, encrypted secrets, IP anonymization, SQL-ledger revenue reads.
- Missing/assumed-absent (evidence: 0 matches or no dispatcher class): TikTok dispatch, Snapchat, LinkedIn, Pinterest, GTM, Custom Pixel, browser pixels, consent/GDPR/CCPA, click-ID capture, provider retry/dead-letter, LTV/CAC/ROAS computation, cross-device stitching.
- Technical debt carried: no provider retry/dead-letter; no explicit refund/reversal analytics signal; no consent gate; TikTok pixel-value placeholder without dispatcher; partial funnel/engagement metrics; unbounded Mongo retention.
