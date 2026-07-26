# AFFILIATE-CHECKOUT-0 Architecture Certification

Date: 2026-07-08
Scope: Affiliate Checkout responsibilities only
Status: ARCHITECTURE CERTIFIED

## Executive Summary

Affiliate Checkout was reviewed only as an Affiliate-specific overlay on the already-certified POS Admin Storefront Checkout. The certified checkout runtime, APIs, DTOs, recovery, frontend UX, shipping flow, payment handoff, order creation, and downstream shipment lifecycle remain owned by the POS Admin Storefront Checkout and adjacent frozen authorities.

No true Affiliate Checkout architecture inconsistency was found. Runtime, backend production code, and frontend production code were not modified. This document is the only output of this certification pass.

Affiliate owns referral and attribution context only: Affiliate Context, Referral Validation, Affiliate Attribution, Affiliate Click Identity, Commission Snapshot evidence, Commission Events, Affiliate Session, Affiliate Campaign, Affiliate Analytics, and Affiliate Audit. Affiliate does not own Cart, Checkout Runtime, Checkout Session Runtime, Shipping, Carrier Selection, Shipping Pricing, Payment, Payment Session, Order Creation, Order Lifecycle, Shipment, Inventory, Warehouse, Coupons, Promotions, Taxes, Geography, or Customs.

Certification decision: **Affiliate Checkout architecture is certified** because Affiliate authority enters the checkout path only as backend-validated attribution evidence and immutable commission snapshots, while checkout, shipping, payment, order, and shipment authority remain reused from the frozen certified platform.

## Architecture Overview

Affiliate Checkout is not a separate checkout implementation. It is a storefront attribution overlay that starts with an affiliate link, validates referral evidence through backend Affiliate authority, carries request-scoped Affiliate Context into the certified Storefront Checkout, and snapshots commission eligibility inside the checkout transaction after final SQL revalidation.

Certified flow:

```text
Customer
  -> Affiliate Link
  -> Affiliate Context
  -> Cart intent / cart line affiliate evidence
  -> POS Admin Storefront Checkout
  -> Payment
  -> Order
  -> Commission Snapshot / Commission Events
  -> Shipment
```

Authority flow:

```text
Affiliate Context
  -> POS Admin Checkout Runtime
  -> Payment
  -> Order
  -> Shipment
```

Affiliate never bypasses Checkout authority. Affiliate evidence is admitted or rejected by backend middleware, resolver, SQL affiliate/promotion-link authority, and final checkout transaction revalidation.

## Authority Matrix

| Concern | Owner | Affiliate Role | Certification |
|---|---|---|---|
| Affiliate Context | Affiliate | Owns backend-validated referral context | Certified |
| Referral Validation | Affiliate | Resolves active affiliate/promotion-link evidence | Certified |
| Affiliate Attribution | Affiliate plus checkout transaction | Owns attribution evidence; checkout commits order-bound snapshot | Certified |
| Affiliate Click Identity | Affiliate | Owns click/session/campaign tracking evidence | Certified |
| Commission Snapshot | Checkout transaction consuming Affiliate policy | Affiliate supplies policy context; checkout snapshots immutable order-line evidence | Certified |
| Commission Events | Affiliate | Consumes order/payment/accounting events after checkout/order authority | Certified |
| Cart | Storefront Cart authority | Affiliate line evidence only; no cart ownership | Reused |
| Checkout Runtime | POS Admin Storefront Checkout | Consumer only | Reused |
| Checkout Session Runtime | POS Admin Storefront Checkout | No ownership | Reused |
| Shipping | Shipping Platform / Storefront Shipping | No ownership | Reused |
| Carrier Selection | Shipping Platform | No ownership | Reused |
| Shipping Pricing | Shipping calculation authority | No ownership | Reused |
| Payment | Payment authority | No ownership | Reused |
| Payment Session | Payment authority | No ownership | Reused |
| Order Creation | POS Admin Storefront Checkout / Order authority | Attribution snapshot consumer only | Reused |
| Order Lifecycle | Order authority | Commission event consumer only | Reused |
| Shipment | Shipment authority | No ownership | Reused |
| Inventory / Warehouse | Inventory and Warehouse authorities | No ownership | Reused |
| Coupons / Promotions | Coupon and Promotion authorities | No ownership | Reused |
| Taxes / Geography / Customs | Owning platform authorities | No ownership | Reused |

## Reuse Matrix

| Checkout Capability | Affiliate Behavior |
|---|---|
| Checkout Runtime | Reused. Affiliate does not implement a parallel runtime. |
| Checkout APIs | Reused. Affiliate evidence enters through existing storefront requests and middleware. |
| Checkout DTOs | Reused as candidate/evidence carriers; Affiliate cannot promote DTO fields to authority. |
| Checkout Recovery | Reused. Recovery re-enters checkout/session/cart authority and cannot restore affiliate truth from browser state. |
| Checkout Frontend | Reused. No duplicated Affiliate checkout UI was found. |
| Checkout UX | Reused. Affiliate surfaces provide branding/context/linking only. |
| Checkout Idempotency | Reused. Affiliate does not define a separate idempotency path. |
| Checkout Payment Handoff | Reused. Payment handoff remains backend payment-session authority. |
| Checkout Shipping Flow | Reused. Shipping method/cost/session authority remains backend-owned. |
| Checkout Review | Reused. Review remains checkout presentation of backend evidence. |

## Affiliate Responsibilities

Affiliate owns:

- Affiliate Context and request-scoped affiliate mode.
- Referral validation for affiliate accounts and promotion links.
- Protected first-click attribution evidence and affiliate session/click identity.
- Affiliate campaign/link metadata and analytics projection evidence.
- Affiliate audit events and telemetry.
- Commission candidate inputs through active affiliate product policy and active category evidence.
- Commission events and later commission lifecycle after order/payment/accounting authority confirms eligibility.

Affiliate does not own:

- Cart authority, checkout authority, checkout session authority, shipping authority, payment authority, order authority, shipment authority, inventory authority, warehouse authority, coupon/promotion/tax/geography/customs authority, or provider authority.

## Checkout Integration

Affiliate integration is limited to backend-admitted attribution evidence:

- `AffiliateReferralMiddleware` validates referral links against current SQL affiliate/promotion-link/product authority and sets request-scoped `AffiliateModeResolver` evidence.
- `AffiliateModeResolver` stores request-scoped affiliate identity, referral code, promotion link, affiliate user, store, and display evidence. It is not frontend-controlled authority.
- `CheckoutService` consumes resolved Affiliate evidence, then revalidates affiliate account and promotion-link authority inside the final checkout transaction.
- `LoadAffiliateCommissionPolicySnapshotsAsync` admits per-line commission candidates only after current product lifecycle, enabled policy, active category, and promotion-link product scoping pass.
- `RefreshOrderItemAuthoritySnapshots` writes immutable commission snapshot fields only for admitted order lines.

The certified POS Admin Storefront Checkout remains the final admission boundary for pricing, shipping, payment, inventory, idempotency, order creation, and recovery.

## Shipping Integration

Affiliate has no Shipping ownership. Shipping method selection, shipping session, shipping cost, carrier selection, carrier mapping, shipment creation, tracking, and shipment lifecycle remain governed by the frozen Shipping Platform, Shipping Administration, Shipping Method Final, and Shipment Final certifications.

Affiliate may label the customer journey for attribution, but it cannot calculate shipping, select a carrier, mutate a shipping method, create a shipment, register a carrier shipment, or recover shipping authority.

## Payment Integration

Affiliate has no Payment ownership. Payment method eligibility, payment session creation, provider handoff, payment lifecycle, provider callback/webhook admission, retry, recovery, reconciliation, and financial truth remain governed by Payment Final and POS Admin Storefront Checkout.

Commission recording waits for backend payment/accounting/order gates. Affiliate cannot treat referral evidence, analytics, cart state, browser state, or order header attribution alone as payment confirmation.

## Commission Integration

Commission authority begins as immutable checkout order-line snapshots and continues through Affiliate commission ledger/event processing:

- Checkout snapshots `AffiliateCommissionEligible`, policy id, rate, base amount, commission amount, max-per-item evidence, and capture time on admitted order lines.
- `AffiliateAttributions` binds the order to one canonical affiliate attribution row.
- `RecordCommissionForOrderAsync` consumes the canonical attribution row, immutable order-item snapshots, terminal order/payment/accounting gates, and idempotent ledger constraints.
- Analytics and projection consumers may present commission/campaign results but cannot create commission truth.

## Data Flow

```text
Customer
  -> Affiliate Link
  -> Affiliate Context
  -> Cart
  -> POS Admin Checkout
  -> Payment
  -> Order
  -> Commission Snapshot
  -> Shipment
```

Failure behavior is fail-closed. Invalid referral, inactive affiliate, inactive/deleted promotion link, wrong store promotion link, stale final affiliate authority, inactive policy/category, stale product lifecycle, stale checkout pricing, stale shipping, payment failure, order rollback, or missing canonical attribution prevents commission authority from being created.

## Future Extension Points

Future Affiliate extensions may add:

- Additional campaign dimensions and reporting.
- New affiliate landing page templates.
- Additional analytics projections.
- New commission-event read models.
- Affiliate portal presentation improvements.

Future extensions must not add:

- Affiliate-specific checkout runtime.
- Affiliate-specific shipping UI/runtime.
- Affiliate-specific payment UI/runtime.
- Affiliate-specific order creation/lifecycle.
- Client-side commission calculation authority.
- Recovery from browser, Redis, Mongo, analytics, or projection state as accepted authority.

## Certification Decision

Affiliate Checkout architecture is **CERTIFIED**.

This certification is architecture-only and Affiliate-specific. The POS Admin Storefront Checkout, Shipping, Payment, Order, Shipment, Inventory, Warehouse, Coupons, Promotions, Taxes, Geography, Customs, CommerceHub Marketplace Checkout, and POS Sales Checkout were treated as frozen adjacent authorities and were not redesigned.

## Explicit Success Answers

- Reviewed ONLY Affiliate Checkout? **Yes.**
- Runtime modified? **No.**
- Backend modified? **No production backend code modified.**
- Frontend modified? **No production frontend code modified.**
- Affiliate Architecture Certified? **Yes.**
- Checkout Reused? **Yes.**
- Shipping Reused? **Yes.**
- Payment Reused? **Yes.**
- Order Runtime Reused? **Yes.**
- Affiliate owns Checkout? **No.**
- Affiliate owns Payment? **No.**
- Affiliate owns Shipment? **No.**
- Affiliate owns Inventory? **No.**
- Architecture preserved? **Yes.**
- Production-certifiable architecture? **Yes, for architecture. Operational rollout remains governed by the reused Checkout, Shipping, Payment, Order, and Shipment operational gates.**
- Recommend next phase: **Affiliate Checkout operational evidence and regression certification using the reused POS Admin Storefront Checkout operational gates, focused only on referral-to-commission attribution evidence.**
