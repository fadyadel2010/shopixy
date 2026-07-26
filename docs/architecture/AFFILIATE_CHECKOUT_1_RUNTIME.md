# AFFILIATE-CHECKOUT-1 Runtime Certification

Date: 2026-07-08
Scope: Affiliate Runtime, Referral Runtime, Affiliate Attribution Runtime, Commission Snapshot Runtime, and Affiliate Session Runtime only
Status: RUNTIME CERTIFIED

## Executive Summary

Affiliate Runtime was reviewed only as an attribution/runtime overlay on the already-certified POS Admin Storefront Checkout.

No true Affiliate runtime inconsistency was found. Production runtime, backend code, frontend code, checkout orchestration, payment orchestration, shipping orchestration, order creation, shipment lifecycle, inventory reservation, warehouse authority, coupons, promotions, taxes, geography, carrier mapping, and customs were not modified.

Certification decision: **Affiliate Runtime is certified**. Affiliate evidence enters the existing checkout transaction as referral, attribution, and commission snapshot evidence only. It does not create a separate checkout runtime, transaction, payment flow, shipping flow, order flow, reservation flow, shipment flow, inventory flow, or warehouse flow.

## Runtime Overview

Affiliate Runtime begins when a storefront request carries referral evidence through query, header, route, or protected first-click cookie. `AffiliateReferralMiddleware` validates the evidence against current tenant, affiliate account, promotion link, product, active policy/category, positive price, and available inventory evidence before setting request-scoped `AffiliateModeResolver` context.

Checkout remains the final authority. `CheckoutService` revalidates shipping, payment, pricing, discounts, affiliate account, promotion link, affiliate user, commission policy/category, order, inventory reservation, payment intent authority, idempotency, and outbox evidence inside the existing final checkout transaction. Commission snapshots are persisted on admitted order lines only after that final admission path succeeds.

Commission events run downstream from canonical attribution, immutable order-line commission snapshots, payment authority, order authority, and accounting authority.

## Reuse Matrix

| Platform capability | Affiliate runtime behavior | Certified |
|---|---|---|
| Checkout Runtime | Reused through `CheckoutService`; no Affiliate checkout service/controller found. | Yes |
| Checkout Recovery | Reused through existing checkout/session/idempotency recovery. Affiliate cannot recover truth from browser, Redis, Mongo, or analytics state. | Yes |
| Checkout Idempotency | Reused through checkout request idempotency coordinator and request/cart snapshot hashing. | Yes |
| Checkout Transaction | Reused. Affiliate revalidation, attribution row, commission snapshots, order, inventory binding, payment intent authority, and outbox staging occur in the existing transaction. | Yes |
| Shipping Runtime | Reused through checkout shipping validation and frozen Shipping Platform. Affiliate does not select, price, create, register, or recover shipping. | Yes |
| Payment Runtime | Reused through checkout/payment services. Affiliate does not create payment intents or verify payment. | Yes |
| Order Runtime | Reused through checkout-created SQL order snapshots. Affiliate stores attribution evidence only. | Yes |
| Shipment Runtime | Reused/deferred. Shipment orchestration remains after authoritative confirmation/payment. Affiliate does not create or mutate shipments. | Yes |
| Inventory Runtime | Reused through checkout inventory reservation authority. Affiliate line admission may check availability but does not reserve stock. | Yes |
| Warehouse Runtime | Reused as inventory/fulfillment evidence only. Affiliate does not own warehouse stock, origin, or routing. | Yes |

## Authority Matrix

| Concern | Owner | Affiliate authority |
|---|---|---|
| Referral validation | Affiliate Runtime | Owns admission of referral evidence against SQL affiliate/link authority. |
| Affiliate Context | Affiliate Runtime | Owns request-scoped backend context through middleware/resolver. |
| Affiliate Attribution | Affiliate Runtime plus checkout transaction | Owns order-bound attribution evidence after checkout admission. |
| Affiliate Session | Affiliate Runtime | Owns affiliate click/session cookie evidence for analytics only. |
| Click identity | Affiliate Runtime | Owns hashed click/session evidence. |
| Commission candidate resolution | Affiliate Runtime inside checkout | Owns policy/category candidate resolution for admitted checkout lines only. |
| Commission snapshot evidence | Checkout transaction consuming Affiliate policy | Immutable order-line evidence after final checkout admission. |
| Commission events | Affiliate Runtime | Scheduled/recorded after order/payment/accounting gates. |
| Analytics | Affiliate Runtime | Projection/presentation evidence only. |
| Audit | Affiliate Runtime | Append-only affiliate evidence. |
| Checkout, shipping, payment, orders, shipment, inventory, warehouse, coupons, promotions, taxes, geography, carrier mapping, customs | Existing certified authorities | No Affiliate ownership. |

## Affiliate Runtime Pipeline

```text
Referral evidence
  -> protected evidence validation / current SQL referral admission
  -> request-scoped Affiliate Context
  -> cart line affiliate candidate admission
  -> existing Storefront Checkout transaction
  -> final affiliate authority revalidation with row locks where supported
  -> order attribution snapshot
  -> immutable order-line commission snapshots
  -> existing payment/order/shipment downstream flow
```

Fail-closed admission was verified for tampered or expired evidence, foreign tenant cookie evidence, inactive/deleted affiliate, inactive/deleted promotion link, wrong-store promotion link, stale affiliate account/link/user authority, ambiguous referral governance, inactive/missing commission policy/category, stale product lifecycle, replayed duplicate attribution, and invalid immutable snapshot shape.

## Commission Pipeline

```text
Admitted checkout order line
  -> immutable commission snapshot fields on OrderItem
  -> canonical AffiliateAttribution row
  -> payment authority collected/settled gate
  -> accounting posted/not-required gate
  -> idempotent AffiliateCommissionLedger earned entry
  -> earned/reversed/approved/paid events
```

Commission recording uses `AffiliateCommissionLedger.CreateEarnedFromSnapshot`. It does not recalculate from current affiliate policy, promotion link, category, frontend state, analytics, or projection state. Later affiliate suspension does not erase an already admitted candidate; deleted/missing affiliate identity fails closed for commission recording.

## Failure Matrix

| Failure | Runtime result |
|---|---|
| Invalid affiliate | Middleware/checkout skips or rejects affiliate context; no attribution/commission. |
| Inactive affiliate | Fails referral/checkout authority revalidation. |
| Expired referral evidence | Protected cookie validation fails and cookie is deleted. |
| Deleted promotion link | Link admission fails; no attribution. |
| Wrong store | Promotion link tenant mismatch fails closed. |
| Inactive campaign/link | Active link/policy/category checks fail. |
| Inactive commission policy | No commission snapshot or checkout conflict on ambiguity. |
| Stale affiliate context | Final checkout SQL revalidation throws stale authority errors. |
| Replay | Unique attribution and ledger idempotency constraints prevent duplicates. |
| Duplicate attribution | Unique `(TenantId, OrderId)` attribution constraint and append-only guard block mutation/duplication. |

## Security Review

- Referral tampering: protected attribution cookie uses ASP.NET Data Protection and validates tenant, affiliate identity, normalized code, and expiration.
- Affiliate spoofing: frontend cannot set `AffiliateModeResolver`; checkout resolves backend middleware context and SQL authority.
- Session isolation: affiliate session cookie is analytics evidence only; checkout revalidates SQL authority.
- Tenant isolation: protected evidence is tenant-bound; promotion links are store-scoped for storefront admission.
- Commission replay: ledger idempotency key is globally unique; attribution is one per tenant/order.
- Immutable snapshots: order-item commission snapshot check constraint requires complete valid snapshots or none.
- Audit evidence: attribution and commission events are append-only evidence after checkout/order admission.

## Regression Coverage

Added focused regression guard:

```text
PointOfSale.Tests/Architecture/AffiliateCheckoutRuntimeCertificationTests.cs
```

Coverage verifies:

- referral admission and protected evidence fail-closed behavior;
- affiliate context is backend/request-scoped;
- commission snapshots are immutable checkout evidence;
- Affiliate reuses the checkout transaction and checkout idempotency;
- no duplicate Affiliate checkout runtime/controller/service is registered;
- no Affiliate-owned payment, shipping, order, shipment, inventory, or warehouse runtime surface is introduced.

Existing related coverage remains applicable:

- `AffiliateAttributionCertificationTests`
- `AffiliateSessionAuthorityCertificationTests`
- `AffiliateCommissionCandidatePendingAuthorityCertificationTests`
- `AffiliateCommissionCertificationTests`
- `AffiliateCommissionRecoveryCertificationTests`
- `AffiliateFinancialRecoveryReplayLedgerIntegrityCertificationTests`
- POS Admin Checkout, Shipping, Payment, Order, Shipment, Inventory, and Warehouse certification suites

## Certification Decision

Affiliate Runtime is **RUNTIME CERTIFIED**.

No production runtime fix was required. The existing implementation preserves Affiliate as attribution, context, commission snapshot, event, analytics, and audit authority only, while reusing the certified checkout, shipping, payment, order, shipment, inventory, and warehouse runtimes.

## Explicit Success Answers

- Reviewed ONLY Affiliate Runtime? **Yes.**
- Runtime modified? **No production runtime modified.**
- Backend modified? **Only focused backend architecture tests and this certification document were added. No production backend code modified.**
- Frontend modified? **No.**
- Affiliate Runtime Certified? **Yes.**
- Checkout Runtime Reused? **Yes.**
- Shipping Runtime Reused? **Yes.**
- Payment Runtime Reused? **Yes.**
- Order Runtime Reused? **Yes.**
- Commission Snapshot Certified? **Yes.**
- Affiliate owns Checkout? **No.**
- Affiliate owns Payment? **No.**
- Affiliate owns Shipment? **No.**
- Affiliate owns Inventory? **No.**
- Architecture preserved? **Yes.**
- Backward compatible? **Yes.**
- Production-certifiable runtime? **Yes, for Affiliate Runtime. Production rollout still depends on the already-frozen operational gates of checkout, shipping, payment, order, shipment, inventory, and warehouse authorities.**
- Recommend next phase: **Affiliate operational rollout evidence: execute referral-to-paid-commission smoke tests through the certified POS Admin Storefront Checkout with real shipping/payment/order gates and recorded monitoring/audit evidence.**

## AI Context Update Decision

No `.ai-context` files were updated.

Reason: this pass discovered no new permanent cross-platform architecture truth. It confirmed the already-frozen rule that Affiliate Runtime is an attribution and commission-evidence overlay on the certified checkout/runtime authorities.
