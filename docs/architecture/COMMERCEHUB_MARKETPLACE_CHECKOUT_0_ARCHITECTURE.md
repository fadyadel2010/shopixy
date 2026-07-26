# COMMERCEHUB-MARKETPLACE-CHECKOUT-0 - Architecture Certification

Date: 2026-07-09
Scope: CommerceHub Marketplace Checkout only
Status: ARCHITECTURE CERTIFIED

## Executive Summary

CommerceHub Marketplace Checkout was reviewed as an architecture layer over the already-certified Reference Checkout Engine and existing CommerceHub authority graph.

This certification reviewed only Marketplace Checkout. POS Admin Checkout, Affiliate Checkout, POS Sales Checkout, Shipping, Payment, Orders, Inventory, Warehouse, Taxes, Coupons, Carrier Mapping, Customs, and provider execution were treated as frozen dependencies and were not redesigned.

No true architecture inconsistency was found. Runtime code, backend code, frontend code, APIs, Shipping, Payment, Orders, Inventory, and Checkout execution were not modified. This document is the only certification output.

Certification decision: CommerceHub Marketplace Checkout architecture is certified as a marketplace planning and presentation layer that reuses the certified checkout, shipping, payment, order, inventory, warehouse, and settlement authorities.

## Preparation Evidence

Mandatory frozen context reviewed:

- All `.ai-context` files present in this workspace.
- `POSADMIN_CHECKOUT_FINAL_END_TO_END_DEVELOPMENT_CERTIFICATION_FREEZE.md`.
- `AFFILIATE_CHECKOUT_FINAL_END_TO_END_DEVELOPMENT_CERTIFICATION_FREEZE.md`.
- `SHIPPING_PLATFORM_ARCHITECTURE_CERTIFICATION_FREEZE.md`.
- `POSADMIN_SHIPPING_FINAL_END_TO_END_DEVELOPMENT_CERTIFICATION_FREEZE_UPDATED.md`.
- `FEDEX_FINAL_END_TO_END_DEVELOPMENT_CERTIFICATION_FREEZE_UPDATED.md`.
- CommerceHub architecture certifications ARCH-1 through ARCH-6.
- CommerceHub checkout, marketplace order, split-order, payment, settlement, shipping-method, shipping-cost, and shipping split-order authority findings.

All certified documents remain frozen. This certification does not supersede or weaken any prior authority freeze.

## Architecture Overview

Marketplace Checkout is not a new checkout engine. It is the CommerceHub marketplace-specific layer that prepares marketplace context before and around the certified checkout runtime.

The certified architecture is:

```text
Customer
  -> Marketplace Cart
  -> Merchant Partition
  -> Reference Checkout Runtime
  -> Payment Authority
  -> Order Authority
  -> Shipment Authority
  -> Settlement Authority
```

Marketplace may group cart lines by merchant, validate merchant eligibility as candidate planning evidence, preview split-order results, attribute marketplace context, and plan commission or settlement candidates. The actual checkout transaction remains owned by the certified checkout runtime and CommerceHub checkout authority.

Marketplace does not own the checkout session, payment session, shipping runtime, carrier selection, order creation runtime, shipment runtime, inventory reservation, warehouse routing, tax calculation, coupon lifecycle, carrier mapping, or customs admission.

## Authority Matrix

| Concern | Owner | Marketplace Role |
|---|---|---|
| Marketplace context | Marketplace | Owns marketplace attribution and context evidence. |
| Marketplace cart grouping | Marketplace | Owns merchant grouping candidate model. |
| Merchant partitioning | Marketplace | Owns partition planning by current merchant/store evidence. |
| Split order planning | Marketplace | Owns planned partition preview before checkout commit. |
| Merchant allocation | Marketplace | Owns candidate allocation evidence from current listing/store authority. |
| Settlement candidate planning | Marketplace | Owns planning evidence only; ledger settlement remains settlement authority. |
| Commission candidate planning | Marketplace | Owns candidate planning; commission snapshots and ledger effects remain backend financial authority. |
| Marketplace analytics | Marketplace | Owns derivative analytics only. |
| Marketplace audit | Marketplace | Owns append-only marketplace decision/audit evidence. |
| Checkout runtime | Reference Checkout Engine / CommerceHub Checkout Authority | Marketplace reuses only. |
| Checkout session | Reference Checkout Engine | Marketplace does not own. |
| Shipping runtime | Shipping Platform and CommerceHub Shipping services | Marketplace reuses only. |
| Carrier selection/execution | Shipping Platform and carrier adapters | Marketplace does not own. |
| Payment runtime/session/execution | CommerceHub Payment Authority | Marketplace reuses only. |
| Order runtime/lifecycle | CommerceHub Order Authority | Marketplace reuses only. |
| Shipment runtime/lifecycle | Shipment Authority | Marketplace reuses only. |
| Inventory reservation | Inventory Authority | Marketplace does not own. |
| Warehouse routing/origin | Warehouse and Shipping Platform authorities | Marketplace does not own. |
| Taxes | Tax authority where implemented | Marketplace does not own. |
| Coupons/promotions | Coupon and promotion authorities | Marketplace does not own. |
| Carrier mapping | Geographic/Carrier Mapping authority | Marketplace does not own. |
| Customs | Customs Evidence and Shipment Customs Snapshot authorities | Marketplace does not own. |

## Reuse Matrix

| Required Reuse | Certification Result |
|---|---|
| Checkout Runtime | Reused. Marketplace does not introduce a duplicate checkout engine. |
| Checkout APIs | Reused. Marketplace must submit through certified checkout API boundaries. |
| Checkout Recovery | Reused. Recovery re-enters checkout/payment/order authority. |
| Checkout Frontend | Reused. Marketplace may add grouping/preview presentation only. |
| Shipping Platform | Reused. Marketplace does not own shipping execution. |
| Shipping Administration | Reused as POS Admin Shipping control plane. |
| Carrier Providers | Reused through the Shipping Platform adapter boundary. |
| Payment Runtime | Reused through CommerceHub Payment Authority. |
| Order Runtime | Reused through CommerceHub Order Authority. |
| Inventory Runtime | Reused through Inventory Authority. |
| Warehouse Runtime | Reused through Warehouse/Shipping origin authority. |

## Marketplace Responsibilities

Marketplace owns only the marketplace-specific planning and evidence layer:

- Marketplace context and attribution.
- Merchant grouping and merchant badges.
- Merchant eligibility as candidate planning evidence.
- Merchant partition planning.
- Split-order preview before checkout commit.
- Merchant allocation planning.
- Settlement candidate planning.
- Marketplace commission candidate planning.
- Marketplace notices and summaries.
- Marketplace analytics.
- Marketplace audit.

Marketplace must re-enter current backend authority before any durable effect. It may not accept frontend grouping, route state, cache, projections, browser storage, stale DTOs, or analytics as checkout, payment, order, shipment, inventory, warehouse, tax, coupon, carrier, or customs truth.

## Split Order Model

The certified split model is:

```text
One Cart
  -> Multiple Merchants
  -> Merchant Partitions
  -> Checkout
  -> One Customer Experience
  -> N Merchant Orders
  -> N Shipments
  -> Settlement
```

Marketplace owns split planning and preview only. Actual marketplace order and sub-order creation remain the responsibility of CommerceHub Checkout and Order Authority inside the certified serializable checkout transaction.

After checkout commit, `CommerceHubMarketplaceOrders`, `CommerceHubSubOrders`, and `CommerceHubSubOrderItems` own persisted marketplace order identity, sub-order identity, immutable item snapshots, immutable financial snapshots, and immutable shipping assignment evidence. Downstream Payment, Shipment, Refund, Return, Vendor Earnings, Settlement, Payout, and Reconciliation authorities consume those snapshots as evidence without taking upstream ownership.

## Settlement Model

Marketplace may plan settlement candidates and commission candidates from merchant partitions and order evidence. It does not settle money.

Settlement authority remains CommerceHub finance and ledger authority:

- Settlement is EGP-only where certified.
- Settlement uses ledger-backed SQL truth.
- Vendor earnings are created by financial workflow from paid/delivered sub-order evidence.
- Payout eligibility and execution remain payout authority.
- Reconciliation may detect and route anomalies only; it cannot create healthy settlement truth.

Marketplace settlement planning is therefore candidate evidence until the financial workflow, ledger, settlement, payout, and reconciliation authorities accept it.

## Checkout Integration

Marketplace Checkout integrates by handing marketplace cart and partition evidence into the certified checkout runtime.

Checkout remains the cart-to-order transition owner. The certified checkout runtime:

- Authenticates the customer.
- Locks customer, cart, and idempotency scope.
- Revalidates current listing, product, variant, category, vendor, payment method, shipping, promo, price, stock, and governance authority.
- Recomputes shipping per store with cache bypass.
- Uses request-hash-bound idempotency.
- Runs the final transaction under serializable SQL semantics.
- Creates the marketplace order, sub-orders, item snapshots, financial snapshots, shipping snapshots, inventory reservations, idempotency response, and order-created event.

Marketplace may not create an alternate checkout page, checkout session model, duplicate checkout submission endpoint, recovery runtime, or order creation path.

## Shipping Integration

Marketplace Checkout reuses the certified Shipping Platform and CommerceHub shipping authorities.

Marketplace may display merchant-grouped shipping summaries and split-order shipping previews. Actual shipping method eligibility, deterministic method selection, shipping cost calculation, shipping assignment persistence, carrier provider invocation, shipment creation, carrier mapping, origin/destination snapshots, customs snapshots, label/tracking evidence, and shipment lifecycle remain outside Marketplace ownership.

Shipping execution follows the frozen platform rule:

```text
Order/Shipment SQL authority
  -> Origin Snapshot
  -> Destination Snapshot
  -> Carrier Mapping Snapshot
  -> Customs Snapshot when applicable
  -> Carrier-neutral DTOs
  -> Carrier Adapter
  -> Response Admission
  -> ShipmentLifecycleTransitionPolicy
```

Marketplace cannot select carriers, price shipping, execute carrier calls, or recover shipment authority.

## Payment Integration

Marketplace Checkout reuses CommerceHub Payment Authority.

Marketplace may show payment-method availability and merchant-grouped financial summaries as backend-issued presentation evidence. Payment initiation, payment session creation, provider handoff, provider callback/webhook verification, amount and currency checks, payment lifecycle, unresolved provider evidence, compensation, refund interaction, and reconciliation remain owned by the certified Payment Authority.

Marketplace cannot execute payments, create payment sessions, persist provider artifacts as authority, mark orders paid, release escrow, or create settlement truth.

## Future Extension Points

Future Marketplace Checkout extensions are allowed only if they preserve the certified reuse model:

- Merchant badges and trust indicators.
- Merchant grouping UI.
- Split-order preview and merchant summaries.
- Marketplace notices.
- Marketplace attribution and audit enhancements.
- Marketplace analytics over backend-issued evidence.
- Settlement and commission candidate planning improvements.

Future extensions that require new shipment-per-sub-order behavior must add explicit CommerceHub sub-order shipment lineage and a focused certification before rollout. They must not reuse generic shipment authority as an inferred split-fulfillment model.

Future Marketplace Checkout changes that touch checkout, shipping, payment, order, inventory, warehouse, settlement, carrier mapping, customs, frontend authority, recovery, replay, projections, cache, or analytics must update the relevant frozen architecture documents and authority context files together.

## Certification Decision

Marketplace Checkout architecture is certified.

The architecture preserves the already-certified Reference Checkout Engine, CommerceHub Checkout Authority, Shipping Platform, Payment Authority, Order Authority, Inventory Authority, Warehouse authority, Shipment Authority, and Settlement Authority.

No runtime, backend, frontend, API, Shipping, Payment, Orders, Inventory, Warehouse, carrier, customs, tax, coupon, or settlement redesign was required.

Operational rollout remains a separate phase. Production certification for runtime launch still depends on the operational gates inherited from checkout, payment, shipping, provider, settlement, monitoring, alerting, recovery-drill, browser-chaos, data, rollback, and support-runbook certifications.

## Success Answers

- Reviewed ONLY Marketplace Checkout? **Yes.**
- Runtime modified? **No.**
- Backend modified? **No production backend code modified.**
- Frontend modified? **No production frontend code modified.**
- Marketplace Architecture Certified? **Yes.**
- Split Order Certified? **Yes, as marketplace split planning plus certified CommerceHub checkout/order split creation.**
- Checkout Reused? **Yes.**
- Shipping Reused? **Yes.**
- Payment Reused? **Yes.**
- Order Runtime Reused? **Yes.**
- Marketplace owns Checkout? **No.**
- Marketplace owns Payment? **No.**
- Marketplace owns Shipment? **No.**
- Marketplace owns Inventory? **No.**
- Architecture preserved? **Yes.**
- Production-certifiable architecture? **Yes, architecture is production-certifiable; operational rollout evidence remains separate.**
- Recommend next phase: **CommerceHub Marketplace Checkout runtime/API/frontend certification only for marketplace grouping, split preview, merchant summaries, marketplace notices, attribution, analytics, and audit, while reusing the certified checkout, shipping, payment, order, inventory, warehouse, shipment, and settlement authorities.**
