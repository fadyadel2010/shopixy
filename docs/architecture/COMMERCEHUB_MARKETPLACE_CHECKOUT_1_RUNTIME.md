# COMMERCEHUB-MARKETPLACE-CHECKOUT-1 - Runtime Certification

Date: 2026-07-09
Scope: CommerceHub Marketplace Runtime only
Status: RUNTIME CERTIFIED

## Executive Summary

CommerceHub Marketplace Runtime was reviewed only as the marketplace planning and orchestration layer above the already-certified Reference Checkout Engine and CommerceHub checkout/order/payment/shipping authority graph.

No true runtime inconsistency was found. No production runtime, backend, or frontend code was modified. Focused certification tests were added to freeze the inspected runtime contract.

Certification decision: Marketplace Runtime is certified as a planning layer that owns marketplace context, merchant grouping, merchant partition planning, split preview planning, settlement candidate planning, commission candidate planning, marketplace analytics, and marketplace audit only. It does not create a separate checkout runtime, payment runtime, shipping runtime, order runtime, shipment runtime, inventory runtime, warehouse runtime, carrier mapping authority, tax authority, coupon authority, or customs authority.

## Runtime Overview

The certified runtime remains:

```text
Marketplace context and merchant planning
  -> certified CommerceHub cart and checkout request
  -> CommerceHubCheckoutService
  -> serializable checkout transaction
  -> immutable marketplace order and sub-order evidence
  -> certified payment, shipping, order, shipment, inventory, warehouse, settlement, analytics, and audit consumers
```

Marketplace Runtime may prepare and display merchant grouping, merchant partition, split preview, marketplace context, settlement candidate, commission candidate, analytics, and audit evidence. Durable checkout effects enter the existing CommerceHub checkout runtime only.

## Authority Matrix

| Concern | Owner | Marketplace Runtime Role |
|---|---|---|
| Marketplace Context | Marketplace Runtime | Owns current marketplace context evidence. |
| Merchant Grouping | Marketplace Runtime | Owns grouping plan over backend-admitted marketplace/cart evidence. |
| Merchant Partition Planning | Marketplace Runtime | Owns partition plan by merchant/store. |
| Merchant Allocation Planning | Marketplace Runtime | Owns candidate allocation planning from current listing/store evidence. |
| Split Preview Planning | Marketplace Runtime | Owns preview evidence before checkout commit. |
| Settlement Candidate Planning | Marketplace Runtime | Owns planning evidence only. |
| Commission Candidate Planning | Marketplace Runtime | Owns candidate planning only; final snapshots remain checkout/financial evidence. |
| Marketplace Analytics | Marketplace Runtime | Owns derivative analytics over backend-issued evidence. |
| Marketplace Audit | Marketplace Runtime | Owns append-only marketplace decision evidence. |
| Checkout Runtime | CommerceHub Checkout / Reference Checkout Engine | Reused only. |
| Checkout Recovery | Checkout recovery/idempotency authority | Reused only. |
| Checkout Transaction | CommerceHubCheckoutService | Reused only. |
| Shipping Runtime | Shipping Platform / CommerceHubShippingService | Reused only. |
| Payment Runtime | CommerceHubPaymentService and payment lifecycle authority | Reused only. |
| Order Runtime | CommerceHub order authority | Reused only. |
| Shipment Runtime | Shipment authority | Reused only. |
| Inventory Runtime | Inventory authority | Reused only. |
| Warehouse Runtime | Warehouse/origin authority | Reused only. |
| Carrier Mapping | Geographic/Carrier Mapping authority | No ownership. |
| Taxes | Tax authority where implemented | No ownership. |
| Coupons | Coupon authority | No ownership. |
| Customs | Customs Evidence and Shipment Customs Snapshot authority | No ownership. |

## Reuse Matrix

| Required Reuse | Result |
|---|---|
| Checkout Runtime | Reused through `CommerceHubCheckoutService`; no separate marketplace checkout runtime exists. |
| Checkout Recovery | Reused through request-hash-bound idempotency and persisted checkout responses. |
| Checkout Idempotency | Reused through `CommerceHubCheckoutIdempotency`, idempotency key normalization, request hash checks, and replay waits. |
| Checkout Transaction | Reused through the existing serializable checkout transaction. |
| Shipping Runtime | Reused through `CommerceHubShippingService` and shipping assignment boundary. |
| Payment Runtime | Reused through `CommerceHubPaymentService` and `CommerceHubPaymentLifecycleAuthority`. |
| Order Runtime | Reused through CommerceHub marketplace order/sub-order SQL authority and order management services. |
| Shipment Runtime | Reused through the certified shipment authority; no marketplace shipment runtime is created. |
| Inventory Runtime | Reused through inventory reservation authority inside checkout. |
| Warehouse Runtime | Reused through existing warehouse/origin/shipment authorities. |
| Settlement Runtime | Reused as ledger/settlement authority; marketplace emits candidate planning only. |

## Marketplace Runtime Pipeline

```text
Resolve marketplace context
  -> admit current customer/session/cart/listing evidence
  -> group lines by merchant/store
  -> create merchant partitions
  -> calculate split preview evidence
  -> prepare candidate settlement and commission evidence
  -> enter certified checkout runtime
```

The runtime fails closed when marketplace context, merchant identity, listing, customer, cart, or partition evidence cannot be admitted from current backend authority.

## Merchant Partition Pipeline

```text
Cart lines
  -> current SQL listing/product/variant/store validation
  -> merchant grouping by store
  -> duplicate allocation detection
  -> cross-store merchant rejection
  -> split preview
  -> checkout request
```

The durable split is persisted only by the certified checkout transaction. `CommerceHubCheckoutService` builds `CommerceHubMarketplaceOrder`, `CommerceHubSubOrder`, and `CommerceHubSubOrderItem` evidence under order lifecycle, shipping assignment, financial workflow, and payment lifecycle mutation boundaries.

## Split Planning Pipeline

```text
Merchant partitions
  -> current shipping calculation per store
  -> requested selection drift comparison
  -> commission candidate planning
  -> final checkout transaction
  -> immutable sub-order shipping and commission snapshots
```

Split planning is preview/candidate evidence until checkout commits. Shipping totals aggregate persisted sub-order shipping evidence only. Commission snapshots exclude shipping from the commission base and are persisted on sub-orders/items by checkout/financial workflow authority.

## Failure Matrix

| Failure | Certified Behavior |
|---|---|
| Invalid Merchant | Fail closed before checkout commit. |
| Inactive Merchant | Fail closed through current vendor/order-acceptance authority. |
| Removed Listing | Fail closed through current listing/product/variant revalidation. |
| Cross-store Merchant | Fail closed through listing/product/variant tenant/store parity checks. |
| Invalid Marketplace Context | Fail closed before planning or checkout admission. |
| Partition Replay | Fail closed through request-hash-bound checkout idempotency and cart locking. |
| Duplicate Merchant Allocation | Fail closed through grouping and unique marketplace order/sub-order constraints. |
| Split Planning Drift | Fail closed with shipping/pricing refresh conflict before persistence. |

## Security Review

Merchant spoofing is blocked by current SQL listing, product, variant, tenant/store, and vendor governance checks. Marketplace context spoofing is blocked because browser state, routes, DTO replay, cache, projections, and analytics are not authority sources. Tenant isolation remains backend-owned through trusted session/tenant context, row predicates, vendor authority, and immutable sub-order lineage.

Replay protection is provided by checkout locks, `CommerceHubCheckoutIdempotency`, request hash validation, persisted response replay, and serializable transaction boundaries. Immutable snapshots are persisted on marketplace orders, sub-orders, sub-order items, shipping assignment fields, payment snapshots, commission snapshots, and financial evidence. Marketplace audit evidence remains append-only decision evidence and cannot become checkout, payment, order, shipping, shipment, inventory, warehouse, tax, coupon, carrier mapping, or customs authority.

## Regression Coverage

Focused certification coverage added:

- `PointOfSale.Tests/Architecture/CommerceHubMarketplaceCheckoutRuntimeCertificationTests.cs`

Existing reused coverage remains part of the certification basis:

- `CommerceHubCheckoutCertificationTests`
- `CommerceHubCheckoutIdempotencyArchitectureTests`
- `CommerceHubMarketplaceOrderAuthorityCertificationTests`
- `MarketplaceSplitOrderAuthorityCertificationTests`
- `ShippingSplitOrderAuthorityCertificationTests`
- `ShippingMethodSelectionAuthorityCertificationTests`
- `ShippingCostAuthorityCertificationTests`
- `CommerceHubPaymentAuthorityCertificationTests`
- `CommerceHubSettlementCertificationTests`
- `CommerceHubFinalProductionArchitectureCertificationTests`

## Certification Decision

Marketplace Runtime is certified.

The runtime preserves Marketplace as planning/orchestration above the certified checkout engine. It reuses checkout recovery, checkout idempotency, checkout transaction, shipping, payment, order, shipment, inventory, warehouse, and settlement authorities. It does not duplicate checkout, payment, shipping, order, shipment, inventory, warehouse, carrier mapping, tax, coupon, or customs runtime.

No `.ai-context` update was required because no new permanent cross-platform architecture truth was discovered.

## Success Answers

- Reviewed ONLY Marketplace Runtime? **Yes.**
- Runtime modified? **No.**
- Backend modified? **No production backend code modified.**
- Frontend modified? **No production frontend code modified.**
- Marketplace Runtime Certified? **Yes.**
- Merchant Grouping Certified? **Yes.**
- Split Planning Certified? **Yes.**
- Checkout Runtime Reused? **Yes.**
- Shipping Runtime Reused? **Yes.**
- Payment Runtime Reused? **Yes.**
- Order Runtime Reused? **Yes.**
- Settlement Planning Certified? **Yes.**
- Marketplace owns Checkout? **No.**
- Marketplace owns Payment? **No.**
- Marketplace owns Shipment? **No.**
- Marketplace owns Inventory? **No.**
- Architecture preserved? **Yes.**
- Backward compatible? **Yes.**
- Production-certifiable runtime? **Yes, for the inspected Marketplace Runtime planning layer; operational rollout evidence remains inherited from the certified dependent runtimes.**
- Recommend next phase: **CommerceHub Marketplace Checkout API/frontend certification for marketplace grouping, split preview, merchant summaries, marketplace notices, attribution, analytics, and audit, with no duplicate checkout/payment/shipping/order runtime.**
