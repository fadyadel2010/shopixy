# WAREHOUSE-0 Authority Architecture Certification

Date: 2026-07-09
Scope: Warehouse, Inventory, Reservation, Allocation, Receiving, Transfers, Returns, Fulfillment, Warehouse Policies, Warehouse Assignment
Status: CERTIFIED - ARCHITECTURE FREEZE

## Executive Summary

Warehouse and Fulfillment Authority was reviewed against the frozen POS Admin Checkout, Affiliate Checkout, CommerceHub Marketplace Checkout, Shipping Platform, POS Admin Shipping, and FedEx certifications, plus the persistent `.ai-context` authority files.

Certification decision: **Warehouse Authority is certified as the fulfillment-decision authority and Inventory is certified as the quantitative ledger authority.**

No true architecture inconsistency was found. Runtime, backend production code, and frontend production code were not modified. No new permanent cross-platform architecture truth was discovered, so the `.ai-context` files were not modified.

Warehouse remains responsible for fulfillment decisions: warehouse identity, policy, capacity, availability, reservation, allocation, fulfillment planning, picking/packing planning, receiving, transfers, returns, cycle count, replenishment, health, and audit.

Inventory remains responsible for quantitative stock ledger state: on hand, reserved, available, damaged, incoming, picking, packing, in transit, returned, stock history, and audit.

Checkout, Shipping, Orders, Payments, Pricing, Coupons, Taxes, Settlement, and Carrier execution remain outside Warehouse and Inventory authority.

## Warehouse Authority

Warehouse owns only the operational decision boundary for fulfillment readiness.

Warehouse owns:

- Warehouse Identity.
- Warehouse Policies.
- Warehouse Capacity.
- Warehouse Availability.
- Reservation Authority.
- Allocation Authority.
- Fulfillment Planning.
- Picking Planning.
- Packing Planning.
- Receiving.
- Transfers.
- Returns.
- Cycle Count.
- Replenishment.
- Warehouse Health.
- Warehouse Audit.

Warehouse never owns:

- Checkout.
- Payment.
- Shipping Carrier.
- Shipment Execution.
- Order Lifecycle.
- Pricing.
- Coupons.
- Taxes.
- Settlement.

The certified authority path is:

```text
Checkout
  -> Reservation Request
  -> Warehouse Authority
  -> Inventory Ledger
  -> Shipping Platform
  -> Carrier
  -> Shipment
```

Warehouse decides whether fulfillment can be reserved, allocated, picked, packed, received, transferred, returned, or replenished. Shipping decides carrier execution after consuming admitted origin, destination, mapping, customs, shipment, and carrier configuration evidence.

## Inventory Authority

Inventory owns only quantitative ledger truth.

Inventory owns:

- Stock Ledger.
- Inventory Transactions.
- On Hand.
- Reserved.
- Available.
- Damaged.
- Incoming.
- Picking.
- Packing.
- In Transit.
- Returned.
- Inventory History.
- Inventory Audit.

Inventory never owns:

- Checkout.
- Warehouse Selection.
- Carrier Selection.
- Payment.
- Shipment.

Current implementation evidence includes variant/warehouse stock rows, inventory reservation rows, stock movements, warehouse stock projections, stock sync outbox lineage, reservation TTL, commit/release paths, and transfer/adjustment stock movements. These are quantitative ledger artifacts. They must not be promoted into checkout, carrier selection, shipment lifecycle, pricing, payment, or order lifecycle authority.

## Authority Matrix

| Concern | Authority | Warehouse Role | Inventory Role | Explicit Non-Owner |
|---|---|---|---|---|
| Warehouse identity | Warehouse | Owns tenant warehouse identity and active/default state | None | Checkout, Shipping, Marketplace |
| Warehouse policy | Warehouse | Owns operational fulfillment policy | Reads policy only when required | Checkout, Payment |
| Capacity and availability | Warehouse | Decides readiness and candidate availability | Supplies ledger quantities | Checkout, Shipping Carrier |
| Reservation | Warehouse Authority | Admits reservation requests and chooses reservable stock | Records reserved quantity and reservation rows | Checkout, Marketplace |
| Allocation | Warehouse Authority | Assigns fulfillment source and pick/pack plan | Reflects quantitative state | Inventory alone, Shipping |
| Stock ledger | Inventory | Consumes ledger evidence | Owns on hand/reserved/available/movement truth | Checkout, Warehouse selection |
| Receiving | Warehouse | Owns inspection, put away, availability decision | Records incoming/on-hand movement | Payments, Shipping |
| Transfer | Warehouse | Owns reserve, ship, transit, receive, availability workflow | Records transfer movements and quantities | Checkout, Carrier |
| Return | Warehouse | Owns customer/vendor return disposition, restock/scrap path | Records returned/damaged/restocked/scrapped quantities | Payments, Shipment |
| Shipment execution | Shipping Platform | Provides admitted origin/fulfillment evidence only | None | Warehouse, Inventory |
| Carrier selection | Shipping Platform | No carrier authority | No carrier authority | Warehouse, Inventory |
| Checkout | Checkout | Receives reservation result only | Receives ledger reservation request only | Warehouse, Inventory |
| Marketplace grouping | Marketplace | Receives merchant context and performs warehouse authority decisions | Supplies stock ledger evidence | Marketplace |

## Warehouse Lifecycle

Warehouse lifecycle is:

```text
Identity
  -> Policy
  -> Capacity / Availability
  -> Reservation
  -> Allocation
  -> Pick Plan
  -> Pack Plan
  -> Fulfillment Ready
  -> Shipment handoff
  -> Audit / Health
```

Shipment handoff is not carrier execution. The Shipping Platform consumes admitted fulfillment evidence, origin evidence, destination evidence, carrier mapping evidence, customs evidence when applicable, and shipment state before invoking a carrier.

## Reservation Model

Reservation begins only when Checkout or an operational workflow submits a backend reservation request. Checkout is never stock authority; it may request reservation and fail closed when reservation cannot be admitted.

Certified reservation properties:

- Reservation is backend-owned.
- Reservation can be scoped to a warehouse or use warehouse authority to locate eligible stock.
- Reservation writes quantitative reserved evidence into Inventory ledger state.
- Reservation carries order, order-item, variant, warehouse, quantity, source, event, and expiry evidence where modeled.
- Reservation expiry or release re-enters Inventory ledger authority.
- Payment or fulfillment commit consumes reservation evidence and converts reserved quantity to committed movement only through backend services.

CommerceHub Marketplace Checkout currently passes merchant/store context and calls backend inventory reservation through `IInventoryConsistencyService.ReserveOrderItemAsync`. POS Admin Storefront Checkout uses `IProductVariantStockRepository` reservation APIs. Both are reservation consumers, not inventory or warehouse owners.

## Allocation Model

Allocation is the Warehouse decision that turns availability into a fulfillment plan.

Certified allocation properties:

- Allocation selects fulfillment source and readiness.
- Allocation may support single-warehouse, multi-warehouse, cross-warehouse, partial, and split fulfillment.
- Inventory contributes ledger quantities but does not own warehouse selection.
- Shipping consumes the selected origin/readiness evidence but does not allocate stock.
- Marketplace contributes merchant context and partitioning but does not choose the warehouse.

## Receiving Model

Receiving lifecycle is:

```text
Opening Balance
  -> Purchase Receipt
  -> Receiving
  -> Inspection
  -> Put Away
  -> Availability
```

Opening balance and purchase receipt create inventory movement evidence. Warehouse owns receiving, inspection, put-away, and availability decisions. Inventory records the quantitative on-hand, incoming, damaged, and available effects.

## Transfer Model

Transfer lifecycle is:

```text
Reserve
  -> Ship
  -> Transit
  -> Receive
  -> Available
```

Warehouse owns source/destination warehouse workflow, approval, completion, and readiness. Inventory records transfer-out, in-transit where modeled, transfer-in, reserved, and available quantities. Checkout and Shipping do not create transfer authority.

## Returns Model

Return lifecycle is:

```text
Customer / Vendor Return
  -> Inspection
  -> Restock / Damaged / Scrap
  -> Inventory Movement
  -> Audit
```

Warehouse owns return disposition for physical goods. Inventory records returned, damaged, restocked, scrapped, or unavailable quantities. Payments own refund lifecycle; Orders own order lifecycle; Shipping owns return-shipment execution where applicable. None of those domains become Warehouse or Inventory authority.

## Marketplace Integration

Marketplace does not choose warehouse.

Marketplace provides:

- Merchant Context.
- Merchant Partition.
- Store/vendor grouping evidence.
- Split preview and presentation evidence.

Warehouse Authority performs:

- Warehouse selection.
- Reservation.
- Allocation.
- Fulfillment planning.

CommerceHub Checkout may validate cart/listing/product/payment/shipping/pricing evidence and request reservation, but its final transaction does not make Marketplace the warehouse owner. Split-order planning remains a marketplace/order concern; warehouse fulfillment decisions remain Warehouse Authority and ledger effects remain Inventory.

## Checkout Integration

POS Admin Storefront Checkout, Affiliate Checkout, and CommerceHub Marketplace Checkout remain checkout authorities only.

Checkout may:

- Submit reservation requests.
- Submit inventory-adjustment intent through the backend authority boundary where operationally allowed.
- Fail closed on reservation, availability, or stale-pricing conflict.
- Persist immutable order snapshots after backend admission.

Checkout may not:

- Mutate stock directly.
- Select warehouse as its own authority.
- Allocate fulfillment independently.
- Become Inventory ledger authority.
- Become Warehouse policy or capacity authority.

Affiliate Checkout reuses POS Admin Storefront Checkout and never owns Warehouse or Inventory. Marketplace Checkout contributes merchant context and calls backend reservation; it never chooses warehouse or mutates stock directly.

## Shipping Integration

Shipping owns carrier execution, not fulfillment decisions.

Shipping may consume:

- Admitted origin/warehouse evidence.
- Fulfillment readiness evidence.
- Shipment origin snapshot.
- Shipment destination snapshot.
- Carrier mapping snapshot.
- Customs snapshot when applicable.
- Carrier-neutral DTOs.

Shipping may not:

- Own Warehouse.
- Own Inventory ledger.
- Allocate stock.
- Reserve stock.
- Choose warehouse as business authority.
- Recover fulfillment authority from provider responses, cache, projections, or carrier payloads.

The Shipping Platform freeze remains authoritative for carrier execution: adapters serialize admitted DTO evidence and return provider evidence only; platform services admit carrier results and apply shipment lifecycle policy.

## Future Extension Points

The architecture supports:

- Multi-Warehouse.
- Cross-Warehouse Reservation.
- Partial Fulfillment.
- Split Fulfillment.
- Marketplace Fulfillment.
- Carrier Selection after fulfillment readiness.
- Future WMS integration.

Future WMS integration must be an external operational system consuming and returning admitted warehouse/fulfillment evidence. A WMS must not become Checkout, Payment, Orders, Shipping Carrier, or Inventory ledger authority unless a later certification explicitly changes ownership.

## Certification Decision

Warehouse Authority Certified: **Yes.**

Inventory Authority Certified: **Yes.**

Reservation Authority Certified: **Yes.**

Allocation Authority Certified: **Yes.**

Checkout Integration Certified: **Yes.**

Shipping Integration Certified: **Yes.**

Marketplace Integration Certified: **Yes.**

Architecture preserved: **Yes.**

Backward compatible: **Yes.**

Production-certifiable architecture: **Yes, as an architecture certification. Operational proof remains separate.**

Runtime modified: **No.**

Backend modified: **No production backend code modified.**

Frontend modified: **No.**

## Explicit Success Answers

- Reviewed ONLY Warehouse Authority? **Yes.**
- Runtime modified? **No.**
- Backend modified? **No production backend code modified.**
- Frontend modified? **No.**
- Warehouse Authority Certified? **Yes.**
- Inventory Authority Certified? **Yes.**
- Reservation Authority Certified? **Yes.**
- Allocation Authority Certified? **Yes.**
- Checkout Integration Certified? **Yes.**
- Shipping Integration Certified? **Yes.**
- Marketplace Integration Certified? **Yes.**
- Warehouse owns Checkout? **No.**
- Warehouse owns Shipping? **No.**
- Inventory owns Warehouse Selection? **No.**
- Architecture preserved? **Yes.**
- Backward compatible? **Yes.**
- Production-certifiable architecture? **Yes, architecture-certifiable; operational rollout evidence remains separate.**
- Recommend next phase: **WAREHOUSE-1 Runtime Authority Certification for warehouse selection, allocation, reservation admission, receiving, transfer, return disposition, and WMS integration readiness with focused runtime evidence.**

