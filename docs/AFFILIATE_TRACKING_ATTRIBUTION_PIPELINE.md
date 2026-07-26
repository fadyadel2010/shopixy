# Affiliate Tracking, Attribution, Session/Cookie/Click & Conversion/Commission Pipeline

Implementation-backed reference. All citations are `file:line` against the current `PointOfSale` repo.
Scope: server-side tracking/attribution/conversion runtime (`.cs`). UI/React is noted only where it seeds the pipeline.

---

## 1. Tracking Pipeline (entry → middleware)

### 1.1 Referral entry middleware — `PointOfSale.API/Middleware/AffiliateReferralMiddleware.cs`

**Activation & tenant boundary**
- Runs as ASP.NET middleware, only on storefront paths (no admin/api) — class doc `AffiliateReferralMiddleware.cs:42`.
- Bails out immediately if no tenant resolved (`tenantId == Guid.Empty`) — `:71-77`.
- **No DB writes inside middleware** — event-driven only; failures are swallowed (never block the request) — `:39`, `:471-478`.

**Referral code sources (priority order)** — `:79-150`
1. `AFF_ATTR` cookie (protected evidence token, see §5). Validated first; if it decrypts+validates → `rawRef` from token, `refSource = "Cookie:first-click-protected"` — `:88-107`.
2. Query param `?ref=` (`RefQueryParam = "ref"`) — `:109-115`.
3. Header `X-Affiliate-Ref` (`RefHeaderName`, SPA flow) — `:116-122`.
4. Route param `/p/{referralCode}/{slug}` — `:130-137`.
5. Route param `/api/storefront/landing/{referralCode}/{slug}` — `:140-148`.

Priority is enforced by `if (rawRef == null && …)` guards, so an existing valid `AFF_ATTR` cookie **wins over** a later `?ref=` (first-click semantics, see §7).

**Affiliate resolution — two flows**
- **V2 (promotion link) flow** — `:200-346`. Looks up `AffiliatePromotionLinks` **scoped to the current tenant** (`l.TenantId == currentTenantId && l.IsActive && !l.IsDeleted`) — `:178-194`. Product must pass "storefront admission" `IsProductCurrentlyAffiliateSelectableAsync` (published, active, affiliate policy + active category, positive price, available inventory) — `:202-227`, `:553-593`. Resolves `AffiliateAccountId` (link direct, or via `AffiliateUser` bridge) — `:240-251`. Validates account `Active && !IsDeleted` — `:275-294`.
- **V1 fallback flow** — `:347-463`. Looks up `AffiliateAccounts` **globally** (`IgnoreQueryFilters`, no tenant filter) by `ReferralCode`, `Status == Active` — `:371-376`. Cross-store activation logged when `affiliateAccount.TenantId != tenantId` — `:410-416`.

**Affiliate Mode (request-scoped, in-memory only)** — `AffiliateModeResolver`
- V2: `AffiliateModeResolver.SetAffiliateModeV2(...)` stores `AffiliateAccountId`, `ReferralCode`, `PromotionLinkId`, `AffiliateUserId`, `StoreId` in `HttpContext.Items` — `:297-304`, `AffiliateModeResolver.cs:164-185`.
- V1: `SetAffiliateMode(...)` stores `AffiliateAccountId` + `ReferralCode` — `:439-443`, `AffiliateModeResolver.cs:142-154`.
- Mode is **never** trusted from the frontend and is **not** persisted — `AffiliateModeResolver.cs:18-28`.

**Click event (fire-and-forget)**
- Publishes `AffiliateLinkClickedEvent` with `SessionId`, `IpHash`, `UserAgent` (both hashed), `ProductId`, `ReferralCode`, `OccurredAtUtc` — `:319-331` (v2) / `:424-436` (v1).
- Metrics: `RecordAffiliateModeActivationAsync` — `:334-338`, `:458-462`.

**PII handling**
- IP → SHA-256 hex (`HashIp`) — `:690-695`; User-Agent → SHA-256 hex (`HashUserAgent`, GDPR AUDIT FIX F-SEC2) — `:702-707`. No raw PII stored.

---

## 2. Cookie / Session / Click Storage

| Token | Name | Set where | Scope / Lifetime | Purpose |
|---|---|---|---|---|
| Session | `AFF_SESSION` (`CookieName`) | `EnsureSession` `:522-540` | `HttpOnly`, `Secure`, `SameSite=Lax`, `MaxAge=30d`, `Path=/` | Stable per-browser session id used for click de-dupe/`AffiliateLinkClickedEvent.SessionId` |
| Attribution (evidence) | `AFF_ATTR` (`AttributionCookieName`) | `EnsureAttributionCookie` `:489-517` | same cookie options, `MaxAge=30d` | Data-protection token of first accepted referral (TenantId, AffiliateAccountId, ReferralCode, PromotionLinkId, ExpiresAtUtc). Set **once** — early return if already present — `:497-501`. |
| Affiliate Mode | `HttpContext.Items` | `AffiliateModeResolver.SetAffiliateMode[V2]` | request-scoped only | Drives downstream visibility/checkout; not persisted — `AffiliateModeResolver.cs:18` |

**Middleware guards on the `AFF_ATTR` cookie** (re-validation against live SQL authority every request):
- Tamper/foreign-tenant/expired → delete cookie + warn — `:100-107`.
- V2: if protected `PromotionLinkId`/`AffiliateAccountId` no longer match live SQL → delete + skip — `:262-273`.
- V1: if protected evidence no longer matches account → delete + skip — `:396-407`.
- If promotion link now inactive but cookie was valid → delete — `:349-358`, `:382-387`.

**Storefront client storage** — `PointOfSale.Storefront/src/bootstrap/affiliateRefTracker.ts`
- `sessionStorage` key `affiliate_ref_code` — `:39`.
- Sources: `?ref=` (`:57`), route `/p/{referralCode}/{slug}` (`:60-69`), else restore from `sessionStorage` (`:85-89`).
- **sessionStorage by design** (survives refresh, dies on tab close); explicitly NOT `localStorage` — `:34-37`.
- The `httpClient` attaches the code as the `X-Affiliate-Ref` header on every API request so the SPA can re-activate affiliate mode on follow-up calls (middleware still re-validates) — `:24-32`.

**Landing page** — `PointOfSale.Storefront/src/pages/AffiliateLandingPage.tsx`
- Route `/p/:referralCode/:slug` — `:47-49`.
- `handleBuyNow` passes `referralCode` into `startFunnel(...)` only when `data.affiliate.isValid && ctaConfig.includeAffiliateAttribution !== false` (graceful degradation on suspension) — `:115-132`. Referral propagates to cart/checkout via the funnel endpoint (§3/§4).

---

## 3. Affiliate Links — Generation & Resolution

### 3.1 Generation — `PointOfSale.API/Controllers/AffiliatePromotionLinksController.cs`
- `POST /api/affiliate/promotion-links`, policy `AffiliateOnly` — `:56-70`.
- `TenantId` (the store) required in body; `AffiliateUserId` resolved from JWT claims — `:59-70`.
- Product must pass `IsProductCurrentlyAffiliateSelectableAsync` before a link can be created — `:80-81`.
- **One link per (AffiliateUser, Store, Product)**; idempotent reactivation if existing link inactive — `:84-99`.
- `GenerateUniqueReferralCodeAsync` produces a 10-char A-Z0-9 code, **checking BOTH `AffiliatePromotionLinks` and `AffiliateAccounts`** to prevent v1/v2 code collisions; falls back to 14 chars — `:224-249`, `:251-260`.
- Links are **global** but **store-scoped** via `TenantId`; cross-store listing allowed for global affiliates — `:23-24`, `:139-144`.
- `PromotionUrl = {storefrontBaseUrl}/products/{slug}?ref={referralCode}` is server-built (frontend must not synthesize authority) — `:427-429`.

### 3.2 Resolution (consumer side)
- Middleware V2: `?ref=` / route / `X-Affiliate-Ref` → tenant-scoped `AffiliatePromotionLinks` lookup — `AffiliateReferralMiddleware.cs:178-194`.
- Middleware V1 fallback: global `AffiliateAccounts` lookup — `:371-376`.
- Funnel/checkout re-resolves from `HttpContext.Items` (`AffiliateModeResolver.GetAffiliateAccountId/ReferralCode/PromotionLinkId`) — `StorefrontFunnelController.cs:188-190`.

---

## 4. Attribution Pipeline (Visitor → Link → Landing → Cookie → Session → Checkout → Order → Attribution)

1. **Visitor clicks** `…/products/{slug}?ref=CODE` or `/p/CODE/slug` → middleware resolves affiliate (V2 tenant-scoped or V1 global), sets `AFF_ATTR` (first-click, 30d) + `AFF_SESSION`, activates request-scoped Affiliate Mode, publishes `AffiliateLinkClickedEvent` — `AffiliateReferralMiddleware.cs:200-463`.
2. **Landing / SPA** captures code (`sessionStorage` + `X-Affiliate-Ref`); `AffiliateLandingPage` passes it to `startFunnel` — `affiliateRefTracker.ts`, `AffiliateLandingPage.tsx:115-132`.
3. **Funnel** stamps affiliate into the **Redis cart** (anti-tampering, `cart.AffiliateAccountId/ReferralCode`, `AffiliateSource="Funnel"`) and re-stamps into `HttpContext.Items` — `StorefrontFunnelController.cs:281-309`; also appends `&ref=` to checkout URL as fallback — `:319-321`.
4. **Checkout** resolves affiliate authority with strict priority (PRIORITY 0 = server-stamped cart metadata > PRIORITY 1 = middleware `HttpContext.Items` > PRIORITY 2 = request DTO `ref`) — `CheckoutService.cs:5660-5700`. **Cart-stamped value wins to prevent ref-hijacking** — `:5666-5670`.
5. **Order stamp** — `order.AffiliateAccountId/ReferralCode/PromotionLinkId` set from resolved authority — `CheckoutService.cs:5750-5752` (and again at `:5852-5854`).
6. **Immutable commission snapshot** stamped per order item at checkout (rate, base amount, commission amount, captured-at) — `CheckoutService.cs:6879-6885`; constrained by `CK_OrderItems_AffiliateCommissionSnapshot_Valid` (rate ≤ 0.5, amounts > 0) — migration `S66…:184`.
7. **Attribution row created inline** in the same transaction as the order (atomic) — `CheckoutService.cs:5925-5967` (v2 `CreateV2` / v1 `Create` `AffiliateAttribution`).
8. **Alternatively** (event-driven) `AttributeOrderToAffiliateAsync` — `AffiliateService.cs:737-1001` — creates the `AffiliateAttribution`, validates authority, publishes `AffiliateAttributedEvent` — `:969-985`.

---

## 5. Conversion / Commission Pipeline — `PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs`

### 5.1 Attribution — `AttributeOrderToAffiliateAsync` `:737-1001`
- TenantId derived from the `Order` itself (called from MassTransit consumer, no HTTP tenant) — `:745-750`.
- **Authority mismatch guards** (throw, never silently re-attribute to wrong affiliate): event vs order `AffiliateAccountId` — `:759-764`; event vs order referral code — `:766-771`; event vs order `PromotionLinkId` — `:779-789`.
- **Self-purchase block** (`EnsureAffiliateSelfPurchaseBlockedAsync`) by matching buyer email/phone to affiliate owner — `:848`, `:1003+`.
- **Idempotency**: existing attribution with same affiliate/code/link → returns existing — `:791-823`. Concurrent unique-violation (23505) → reload + re-validate, return winner — `:881-963`.

### 5.2 Commission recording — `RecordCommissionForOrderAsync` `:1373-1673`
- Attribution must exist; if order has affiliate snapshot but no attribution row → throws (retry until reconciled) — `:1393-1425`.
- **Terminal-status skip**: `Cancelled`/`Refunded` orders → no pending commission — `:1427-1435`.
- **Payment authority gate** `HasPendingCommissionPaymentAuthority` — only `Collected`/`Settled`/`PartiallyRefunded` — `:1437-1445`, `:1675-1680`.
- **Accounting gate**: `FinancialPostingStatus` must be `Posted`/`NotRequired`, else throw `AffiliateCommissionDeferredException` — `:1447-1458`.
- **Affiliate gone/deleted** → skip (later suspension does NOT erase admitted candidate) — `:1477-1484`; cross-tenant commission logged — `:1487-1494`.
- **Per-item immutable snapshot**: commission computed from `AffiliateCommission*Snapshot` captured at checkout (base = `item.Total`), requires `AffiliateCommissionEligible` + valid snapshots — `:1520-1561`. Snapshot values come from `AffiliateCommissionLedger.CreateEarnedFromSnapshot` — `:1552-1561`.
- **Idempotent insert**: Postgres 23505 → detach + skip — `:1574-1584`.
- **Outbox**: `AffiliateCommissionEarnedEvent` staged before commit — `:1591-1618`; then reconciles refunds/deliveries already in flight — `:1621-1622`.

### 5.3 Refund / chargeback reversal
- `ReconcileRefundedCommissionEntriesAsync` `:1743-1850` — for items with `RefundedAmount > 0`, builds proportional reversal ledger entries.
- `CalculateRefundCommissionReversalAuthority` `:1884-1932` — proportional by refunded base; **guards reversal cannot exceed earned** (`AFFILIATE_COMMISSION_REVERSAL_EXCEEDS_EARNED`) — `:1898-1903`; `isFinalReversal` snaps remainder — `:1920-1924`.
- `BuildRefundReversalIdempotencyKey` = `AFFILIATE_COMM:REVERSED:{ref}:{orderItemId}:{base:F4}` — `:1934-1938`.
- `ReverseCommissionForOrderItemAsync` `:2103-2213` — explicit reversal entry; idempotent via 23505 + idempotency key — `:2186-2195`; publishes `AffiliateCommissionReversedEvent` — `:2204-2213`.
- Refund reconciliation also runs inside `RecordCommissionForOrderAsync` so late refunds are caught on re-run — `:1621`, `:1743-1850`.

### 5.4 Approval lifecycle
- `ApproveCommissionForOrderAsync` `:2236-2243` → `ApproveCommissionForDeliveredOrderAsync` `:2245+`.
- Runs in `Serializable` transaction — `:2261`.
- **StoreTenantId mismatch** across ledger entries vs order tenant → reject whole batch — `:2290-2300`.
- Only `Earned` entries approved; reversed items skipped — `:2302-2339`.
- **Idempotency**: already-approved items skipped — `:2341-2346`; **concurrent-consumer guard** re-queries `Approved` rows before commit to avoid duplicate on `IdempotencyKey AFFILIATE_COMM:APPROVED:{OrderItemId}` — `:2354-2398`.
- Publishes `AffiliateCommissionApprovedEvent`.
- Delivery-triggered approval also reconciled at record time via `ReconcileDeliveredCommissionApprovalsAsync` — `:1942-2034`.

### 5.5 Idempotency & deduplication primitives
- `IsUniqueViolation` = Postgres `SqlState == "23505"` — `:2746-2749`.
- Used at every ledger write: attribution (`:881`), earned (`:1574`), reversal (`:1813`, `:2186`), approval (`:1998`, `:2434`).
- Self-purchase block + authority mismatch checks prevent paying the wrong affiliate.

---

## 6. Attribution Evidence Token — `PointOfSale.API/Security/AffiliateAttributionEvidenceTokenService.cs`
- `AffiliateAttributionEvidence` record = `{ TenantId, AffiliateAccountId, ReferralCode, PromotionLinkId, ExpiresAtUtc }` — `:21-26`.
- `AttributionWindow = 30 days` — `:30`.
- **Create** `:48-79`: rejects empty tenant/account/code; normalizes code (trim+upper); serializes JSON and **`IDataProtector.Protect`** with key `"AffiliateAttributionEvidenceTokenService.v1"` — `:42-43`, `:78`.
- **TryValidate** `:81-120`: `Unprotect`; rejects if `TenantId` mismatch, empty account, **expired** (`ExpiresAtUtc <= now`), or code mismatch; swallows `CryptographicException`/`JsonException`/`FormatException` as invalid (tamper/foreign-tenant/expired → cookie deleted upstream).
- Cookie is **evidence only**; checkout/attribution always re-validates live SQL authority before stamping the order — middleware doc `:486-488`, guards `:262-273` / `:396-407`.

---

## 7. First-click vs Last-click / Override Rules

- **First-click IS implemented** for the `AFF_ATTR` attribution cookie: `EnsureAttributionCookie` sets the token **once** and returns early if it already exists — `AffiliateReferralMiddleware.cs:497-501`. Because the cookie is checked **before** `?ref=`/header/route (`:88-107` with `if (rawRef == null && …)` guards at `:109-150`), a later referral click in the same browser **does NOT override** the originally captured (first) referral. Source is tagged `"Cookie:first-click-protected"` — `:98`.
- The token (30-day, tenant-bound, signed) is re-validated against live SQL on every request, so it cannot be tampered with or pointed at a foreign tenant — `AffiliateAttributionEvidenceTokenService.cs:81-120`, `AffiliateReferralMiddleware.cs:100-107,262-273,396-407`.
- **Explicit last-click override of an already-captured attribution is NOT implemented** in the commission path. There is no code that replaces an existing `AffiliateAttribution`/`order.AffiliateAccountId` with a newer referral code. Once the order is stamped (§4 step 5-7), the attribution is fixed; `AttributeOrderToAffiliateAsync` throws on a *different* affiliate rather than overriding — `AffiliateService.cs:824-826,955-962`.
- **Analytics only**: `AdminAnalyticsAttributionController` exposes `FirstTouch` vs `LastTouch` (`AttributionModel`, default `LastTouch`) purely as **reporting models** over stored attribution context — `AdminAnalyticsAttributionController.cs:51,117,206-209`. This is a read-only analytics comparison and does **not** affect which affiliate is paid.
- **No multi-touch / weighted / time-decay commission model** exists in the paying pipeline — commission is single-attribution, first-click (cookie) locked, SQL-revalidated.

---

## 8. Quick file map

| Concern | File | Key lines |
|---|---|---|
| Referral middleware / cookie / session / click | `PointOfSale.API/Middleware/AffiliateReferralMiddleware.cs` | 50-53, 79-150, 178-463, 489-540, 690-707 |
| Affiliate Mode (request-scoped) | `PointOfSale.API/Helpers/AffiliateModeResolver.cs` | 142-185 |
| Storefront ref capture | `PointOfSale.Storefront/src/bootstrap/affiliateRefTracker.ts` | 39-95 |
| Landing page → funnel | `PointOfSale.Storefront/src/pages/AffiliateLandingPage.tsx` | 47-49, 115-132 |
| Link generation/resolution | `PointOfSale.API/Controllers/AffiliatePromotionLinksController.cs` | 56-124, 224-249, 427-429 |
| Funnel → cart stamp | `PointOfSale.API/Controllers/StorefrontFunnelController.cs` | 188-309 |
| Checkout authority + order stamp | `PointOfSale.API/Services/Checkout/CheckoutService.cs` | 5660-5700, 5750-5752, 5925-5967, 6879-6885 |
| Attribution / commission / refund / approve | `PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs` | 737-1001, 1373-1673, 1743-2213, 2236-2405, 2746-2749 |
| Evidence token | `PointOfSale.API/Security/AffiliateAttributionEvidenceTokenService.cs` | 21-26, 30, 48-120 |
| Analytics attribution models (reporting only) | `PointOfSale.API/Controllers/AdminAnalyticsAttributionController.cs` | 51, 117, 206-209 |
