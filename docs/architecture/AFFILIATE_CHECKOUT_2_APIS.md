# AFFILIATE-CHECKOUT-2 API Certification

Date: 2026-07-08
Scope: Affiliate API surface only
Status: API CERTIFIED

## Executive Summary

Affiliate APIs were reviewed only as referral, session/context, attribution, commission-evidence, analytics, audit, affiliate identity, promotion-link, and payout-evidence surfaces.

No true Affiliate API inconsistency was found. No production runtime, backend, or frontend code was modified. Focused source-level certification tests were added to freeze the API boundary.

Certification decision: **Affiliate APIs are certified**. Affiliate APIs do not duplicate checkout execution and do not introduce Affiliate-owned checkout submission, recovery, pricing, shipping selection, payment execution, order creation, shipment creation, inventory reservation, or warehouse routing. Checkout execution remains reused through the already-certified POS Admin Storefront Checkout APIs.

## API Inventory

| Surface | Route | Authority |
|---|---|---|
| Affiliate auth | `api/affiliate/auth/*` | Affiliate identity, verified login, refresh/logout, email/password reset audit |
| Affiliate portal | `api/affiliate/*` | Affiliate profile, read-only commission evidence, payout request intent, landing/discovery presentation |
| Affiliate analytics | `api/affiliate/analytics/*` | Affiliate-scoped analytics projections and click evidence |
| Affiliate discovery | `api/affiliate/discovery` | Authenticated affiliate product discovery presentation |
| Promotion links | `api/affiliate/promotion-links/*` | Store-scoped referral/promotion-link creation and deactivation |
| Payout evidence | `api/admin/affiliates/payout-evidence/verify` | Admin-verified affiliate payout transfer evidence |
| Admin affiliate | `api/admin/affiliates/*` | Affiliate lifecycle, commission approval, payout orchestration, admin evidence views |
| Admin product affiliate | `api/admin/products/{productId}/affiliate/*` | Affiliate product policy/category/template administration |
| Admin readiness | `api/admin/affiliate/readiness` | Affiliate feature readiness evidence |
| Admin categories | `api/admin/affiliate/categories/*` | Affiliate marketplace taxonomy administration |
| Public affiliate stores | `api/public/affiliate/*` | Public affiliate store discovery |
| Public affiliate products | `api/public/affiliate/products/*` | Public affiliate product discovery presentation |
| Admin attribution analytics | `api/admin/analytics/attribution/*` | Attribution analytics reads |
| Storefront funnel | `api/storefront/funnel/start` | Affiliate context/cart-stamp entry point; returns certified checkout URL |

## Controller Review

Affiliate controllers remain bounded to Affiliate authority:

- `AffiliateReferralMiddleware` validates referral evidence and sets request-scoped `AffiliateModeResolver` context.
- `StorefrontFunnelController` delegates cart mutation to `ICartService`, stamps affiliate evidence, and returns `/checkout?cartId=...`; it does not submit checkout.
- `AffiliatePromotionLinksController` admits referral links after current affiliate/product/policy/category/stock visibility checks.
- `AffiliatePortalController`, `AffiliateAnalyticsController`, `AffiliateDiscoveryController`, `PublicAffiliateController`, and `PublicAffiliateProductsController` are read or affiliate-evidence presentation surfaces.
- `AdminAffiliateController` delegates lifecycle, approval, payout, and settlement authority to Affiliate services/orchestrator. The legacy reconciliation endpoint is disabled.
- `AffiliatePayoutEvidenceController` delegates verified evidence registration to `IAffiliatePayoutEvidenceService`.

No Affiliate controller injects `ICheckoutService`, creates checkout sessions, creates payment intents, creates shipments, reserves inventory, or creates orders.

## DTO Review

Affiliate request DTOs admit only Affiliate-owned candidates:

- referral or promotion-link candidates;
- affiliate identity/session/auth candidates;
- product policy/category/template administration candidates;
- payout request/evidence candidates;
- analytics/discovery filters;
- funnel context candidates.

Affiliate response DTOs may display backend-issued product, order, payment-method label, inventory availability, and commission amounts only as presentation/evidence. They are not accepted back as checkout, pricing, shipping, payment, order, inventory, or warehouse truth.

No Affiliate request DTO admits checkout totals, shipping method authority, payment intent authority, order total authority, inventory reservation authority, or warehouse routing authority.

## Authority Matrix

| Concern | Affiliate API authority | Certified |
|---|---:|---:|
| Referral admission | Owns | Yes |
| Affiliate identity/session | Owns | Yes |
| Affiliate context | Owns request-scoped evidence | Yes |
| Attribution | Owns evidence; checkout commits order-bound attribution | Yes |
| Commission evidence | Owns snapshots/events/ledger evidence after checkout/order/payment gates | Yes |
| Analytics/audit | Owns derivative analytics and audit evidence | Yes |
| Checkout submission | No ownership | Yes |
| Checkout recovery/session | No ownership | Yes |
| Checkout pricing | No ownership | Yes |
| Shipping selection/pricing | No ownership | Yes |
| Payment execution | No ownership | Yes |
| Order creation | No ownership | Yes |
| Shipment creation | No ownership | Yes |
| Inventory reservation | No ownership | Yes |
| Warehouse routing | No ownership | Yes |

## Reuse Matrix

| Checkout capability | Affiliate API behavior |
|---|---|
| Submit order | Reused through `CheckoutController` and `ICheckoutService.SubmitOrderAsync`. |
| Pricing preview | Reused through POS Admin Storefront Checkout pricing APIs. |
| Checkout session/recovery | Reused through `CheckoutSessionController`. |
| Payment handoff | Reused through checkout/payment authority. |
| Shipping admission | Reused through checkout/shipping authority. |
| Order creation | Reused through checkout transaction. |
| Shipment lifecycle | Reused through shipment orchestration after checkout/order/payment gates. |
| Affiliate funnel | Creates/reuses cart context only, then redirects to certified checkout. |

## Security Review

- Referral spoofing: referral evidence is validated by middleware and final checkout SQL revalidation.
- Affiliate spoofing: affiliate APIs resolve affiliate identity from affiliate JWT claims and backend SQL; frontend cannot set accepted `AffiliateModeResolver` authority.
- Tenant isolation: admin APIs consume trusted tenant context; promotion links bind store/product scope; public endpoints avoid tenant authority in request DTOs.
- Session isolation: affiliate refresh tokens are hashed, rotated, and scoped to affiliate identity; funnel session ownership rejects authenticated customer mismatch.
- Replay protection: attribution and ledger idempotency constraints remain active; refresh-token replay is audited and rejected.
- Secrets/provider leakage: auth responses do not return raw refresh tokens; payout evidence uses references/signatures, not provider authority.

## Regression Coverage

Added:

```text
PointOfSale.Tests/Architecture/AffiliateCheckout2ApiCertificationTests.cs
```

Coverage verifies:

- Affiliate controllers do not expose duplicate checkout endpoints.
- Affiliate APIs do not inject or call checkout, payment, shipment, inventory reservation, or warehouse routing authority.
- Affiliate mutation endpoints delegate to Affiliate services/orchestrators or cart context only.
- Affiliate request DTOs do not accept checkout/payment/shipping/order/inventory truth.
- Affiliate identity, tenant, session, referral, payout evidence, and replay boundaries remain guarded.
- POS Admin Checkout APIs remain the only checkout execution surface reused by Affiliate flows.

## Verification

Reviewed required context:

- all files under `.ai-context`;
- `AFFILIATE_CHECKOUT_0_ARCHITECTURE.md`;
- `AFFILIATE_CHECKOUT_1_RUNTIME.md`;
- `POSADMIN_CHECKOUT_FINAL_END_TO_END_DEVELOPMENT_CERTIFICATION_FREEZE.md`;
- existing Affiliate certification tests and API-surface tests.

Reviewed API source:

- Affiliate controllers;
- Affiliate middleware/resolver;
- Affiliate services and DTO surfaces where relevant;
- checkout controller/session/service reuse points.

No `.ai-context` files were updated because this pass discovered no new permanent cross-platform architecture truth.

## Certification Decision

Affiliate APIs are **API CERTIFIED**.

Affiliate APIs remain an attribution/context/evidence layer. Checkout execution remains reused from the certified POS Admin Storefront Checkout APIs.

## Explicit Success Answers

- Reviewed ONLY Affiliate APIs? **Yes.**
- Runtime modified? **No.**
- Backend modified? **Only focused backend certification tests and this document were added. No production backend code modified.**
- Frontend modified? **No.**
- Affiliate APIs Certified? **Yes.**
- Checkout APIs Reused? **Yes.**
- Affiliate Context APIs Certified? **Yes.**
- Commission APIs Certified? **Yes.**
- Business logic inside controllers? **No checkout business logic. Affiliate controllers perform admission/response shaping and delegate Affiliate mutations to services/orchestrators.**
- Checkout authority introduced? **No.**
- Architecture preserved? **Yes.**
- Backward compatible? **Yes.**
- Production-certifiable APIs? **Yes, for Affiliate API authority. Production rollout remains governed by existing operational gates for checkout, payment, shipping, order, shipment, inventory, and warehouse.**
- Recommend next phase: **Affiliate operational rollout evidence: execute referral/funnel-to-checkout-to-paid-commission smoke tests through the certified POS Admin Storefront Checkout APIs with recorded audit, monitoring, and payout-evidence verification.**
