# SHOPIXY Shipping Methods Step 2D Forensic Audit

Date: 2026-08-11
Scope: Warehouse context and fulfillment origin only
Mode: Forensic audit only. No implementation performed.

## Executive Verdict

NO CENTRAL WAREHOUSE RESOLUTION AUTHORITY FOUND for Storefront checkout.

The current Storefront order flow does not know the fulfillment warehouse before
shipping methods are loaded, does not send a warehouse through checkout, does not
persist `Order.WarehouseId`, and does not copy inventory reservation warehouse
selection into `Order.WarehouseId`, `OrderItem.WarehouseId`, or
`Shipment.OriginWarehouseId`.

The only Storefront runtime component that currently chooses a warehouse-like
value is the inventory reservation repository. When checkout passes a null
warehouse selector, `ProductVariantStockRepository.ReserveAsync` reserves stock
from tenant stock rows ordered by `WarehouseId` and then stock row `Id`
(`PointOfSale.Infrastructure/Data/Repositories/ProductVariantStockRepository.cs:81`,
`:136`, `:177`, `:904`, `:925`, `:946`). That is a deterministic lock/allocation
order, not a fulfillment-origin authority.

If the same product exists in Alexandria and Cairo and the customer orders one
unit, the current Storefront checkout winner is whichever tenant stock row sorts
first by `WarehouseId`, then `Id`, among current inventory stock owners with
available quantity. No code was found that selects Alexandria versus Cairo using
customer address, delivery zone, shipping method, carrier assignment, default
warehouse, proximity, cost, or operator choice.

## Prior Reports Read

Historical evidence reviewed:

- `docs/architecture/SHOPIXY_SHIPPING_METHODS_STEP_2C_IMPLEMENTATION_2026-08-11.md`
- `docs/architecture/SHOPIXY_SHIPPING_METHODS_STEP_2B_IMPLEMENTATION_2026-08-11.md`
- `docs/architecture/SHOPIXY_SHIPPING_METHODS_FORENSIC_AUDIT_2026-08-11.md`
- Dynamic checkout forensic/final certification reports were searched for
  checkout/order-flow findings.

Historical reports are consistent with the current source on the major boundary:
Step 2C hardened `CarrierAccount -> CarrierWarehouseAssignment -> Warehouse`
administration and deliberately deferred checkout warehouse context.

Current source remains authoritative for all findings below.

## Current Runtime Flow

Actual Storefront checkout flow reconstructed from source:

1. Storefront loads cart. The Redis cart DTO has no warehouse context.
2. Storefront loads shipping methods from `GET /api/storefront/shipping-methods`
   with no `warehouseId` query argument
   (`PointOfSale.Storefront/src/store/checkoutStore.ts:1725`, `:1730`;
   `PointOfSale.Storefront/src/pages/CheckoutPage.tsx:233`;
   `PointOfSale.Storefront/src/components/checkout/ShippingStep.tsx:565`).
3. Customer selects `shippingMethodId`.
4. Storefront submits checkout with cart, address, shipping method, payment,
   pricing, wallet, and custom fields. It does not submit warehouse context
   (`PointOfSale.Storefront/src/store/checkoutStore.ts:2401`).
5. Backend snapshots the cart, claims idempotency, admits checkout address
   geography, recalculates pricing, validates shipping and payment
   (`PointOfSale.API/Services/Checkout/CheckoutService.cs:849`, `:1131`).
6. Backend validates the shipping method against SQL and legacy
   `CarrierConfiguration` for non-manual carriers
   (`CheckoutService.cs:5327`, `:5347`).
7. Inside a serializable order transaction, backend revalidates checkout
   authority, refreshes order snapshots, then reserves inventory
   (`CheckoutService.cs:6370`, `:6420`).
8. Inventory reservation is called with `warehouseId: null`
   (`CheckoutService.cs:5764`).
9. Repository reserves one or more `InventoryReservation` rows with concrete
   `WarehouseId` values from stock rows (`ProductVariantStockRepository.cs:169`,
   `:177`).
10. Order is saved, then reservations are bound to order item IDs
    (`CheckoutService.cs:6430`, `:6433`;
    `ProductVariantStockRepository.cs:764`, `:836`, `:846`).
11. Binding does not assign `Order.WarehouseId` or `OrderItem.WarehouseId`.
12. Shipment creation later creates a `Shipment` without `OriginWarehouseId`
    (`PointOfSale.API/Services/Shipping/ShipmentService.cs:170`).
13. Carrier external registration later attempts to derive origin from
    `Order.WarehouseId`; if the order warehouse is null, origin resolution fails
    closed (`PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs:3883`).

## Order Warehouse Model

Findings:

- `Order` has nullable `WarehouseId`
  (`PointOfSale.Core/Entities/Order.cs:651`).
- `OrderItem` has nullable `WarehouseId`
  (`PointOfSale.Core/Entities/OrderItem.cs:52`).
- `Order` stores nullable `ShippingMethodId`
  (`PointOfSale.Core/Entities/Order.cs:357`), `FulfillmentMode`
  (`Order.cs:266`), and `FulfillmentCarrierCode` (`Order.cs:277`).
- `Order.WarehouseId` is configured as a warehouse FK with `OnDelete(SetNull)`
  (`PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:1907`).
- `OrderItem.WarehouseId` is also a warehouse FK with `OnDelete(SetNull)`
  (`ApplicationDbContext.cs:1986`).
- EF immutability guards include `Order.WarehouseId`
  (`ApplicationDbContext.cs:3870`) and `OrderItem.WarehouseId`
  (`ApplicationDbContext.cs:3872`-`:3879`).
- Admin/manual order creation accepts `CreateOrderDto.WarehouseId` and
  `CreateOrderItemDto.WarehouseId`
  (`PointOfSale.API/Controllers/OrdersController.cs:2953`, `:2961`) and assigns
  them to order/order items (`OrdersController.cs:382`, `:396`).
- Storefront checkout creates `Order` and `OrderItem` objects without assigning
  warehouse fields (`CheckoutService.cs:6064`, `:6118`, `:6301`-`:6333`).
- Admin order detail DTO exposes `WarehouseName`, not `WarehouseId`
  (`OrdersController.cs:2744`).

Conclusion: `Order.WarehouseId` is modeled as a single default fulfillment
warehouse and is immutable after creation, but Storefront checkout does not
populate it. It can be authoritative only for flows that set it at creation.

## Cart Warehouse Context

Findings:

- Storefront `CartDto` and `CartItemDto` contain cart ownership, items, totals,
  available stock, coupons, and idempotency fields, but no warehouse field
  (`PointOfSale.API/DTOs/Cart/CartDtos.cs:7`, `:357`).
- Storefront cart service persists cart items without warehouse/allocation fields
  (`PointOfSale.API/Services/Cart/CartService.cs:611`-`:635`).
- Storefront cart stock availability is aggregate per variant across stock rows:
  grouped by variant and summed as `Quantity - ReservedQuantity`
  (`CartService.cs:2427`).
- Storefront cart lines do not contain warehouse allocations.
- POS cart is separate and does have required `WarehouseId`
  (`PointOfSale.Core/Entities/PosCart.cs:10`;
  `PointOfSale.API/DTOs/PosCart/PosCartDtos.cs:9`).
- POS cart service loads/validates the selected warehouse and stock against
  `cart.WarehouseId` (`PointOfSale.API/Services/Pos/PosCartRuntimeService.cs:60`,
  `:326`, `:341`, `:542`).

Conclusion: Storefront cart has no persisted warehouse context. POS cart does,
but that authority is not used by Storefront checkout.

## Checkout Session Context

Findings:

- `CheckoutSession` stores cart ID, customer/contact/address fields, cart
  snapshot, currency, selected shipping method, payment method, wallet fields,
  status, attribution, and conversion fields. It has no warehouse field
  (`PointOfSale.Core/Entities/CheckoutSession.cs:18`, `:115`).
- `CreateCheckoutSessionRequest`, `UpdateCheckoutSessionRequest`, and recovery
  DTOs have no warehouse field
  (`PointOfSale.API/DTOs/Checkout/CheckoutSessionDtos.cs:7`, `:35`, `:128`).
- Dynamic checkout reports and source show the session is recovery/autosave
  authority, not inventory reservation or warehouse authority.

Conclusion: checkout sessions cannot currently stabilize a warehouse decision
between method preview and submit.

## Inventory Availability And Reservation

Findings:

- Storefront availability reads aggregate variant availability and does not
  return warehouse candidates or a chosen warehouse.
- `InventoryReservation` requires `TenantId`, `OrderId`, `OrderItemId`,
  `ProductId`, `VariantId`, and `WarehouseId`
  (`PointOfSale.Core/Entities/InventoryReservation.cs:6`-`:15`).
- Reservation has no `CartId`.
- Reservation configuration indexes
  `(TenantId, OrderItemId, VariantId, WarehouseId)` and
  `(TenantId, Status, ExpiresAtUtc)`
  (`PointOfSale.Infrastructure/Data/Configurations/InventoryReservationConfiguration.cs:50`,
  `:60`).
- Storefront checkout calls `ReserveAsync(..., warehouseId: null, ...)`
  (`CheckoutService.cs:5764`).
- When `warehouseId` is null, repository loads all tenant stock rows for the
  variant and orders them by `WarehouseId`, then `Id`
  (`ProductVariantStockRepository.cs:904`, `:925`, `:946`).
- It sums total availability across rows and can reserve across multiple stock
  rows if one warehouse cannot cover the full quantity
  (`ProductVariantStockRepository.cs:132`, `:146`-`:177`).
- Reservation binding assigns reservations to order item IDs but preserves
  each reservation's original `WarehouseId`
  (`ProductVariantStockRepository.cs:836`, `:846`-`:855`).

Conclusion: reservation stores concrete warehouse IDs, but the reservation
warehouse selection is repository-local stock allocation. It is not promoted to
order authority and can produce multiple warehouses for one order item/order.

## Shipping Method Eligibility

Endpoint:

- `GET /api/storefront/shipping-methods` accepts optional `[FromQuery] Guid?
  warehouseId` and passes it to the service after loading the cart subtotal and
  weight (`PointOfSale.API/Controllers/StorefrontShippingMethodsController.cs:69`,
  `:96`-`:101`).

Service behavior:

- Empty GUID warehouse is rejected with an empty list
  (`PointOfSale.API/Services/Storefront/ShippingMethodService.cs:61`).
- If `warehouseId` is absent, the service returns active tenant methods from
  cache/repository and does not apply warehouse assignment filtering
  (`ShippingMethodService.cs:71`-`:119`).
- If `warehouseId` is present, the service loads active methods from repository
  and applies `ApplyWarehouseAssignmentFilterAsync`
  (`ShippingMethodService.cs:120`-`:122`, `:183`).
- The filter first validates that the warehouse is active, not deleted, and
  belongs to the tenant (`ShippingMethodService.cs:195`-`:204`).
- It joins `CarrierWarehouseAssignment` to `CarrierAccount` and requires same
  tenant, matching `assignment.WarehouseId`, assignment enabled, origin readiness
  published, non-deleted assignment/account, active account state, and ready
  operational state (`ShippingMethodService.cs:215`-`:227`).
- Manual carrier codes are excluded from the allowed set and from filtered
  results (`ShippingMethodService.cs:230`-`:250`).

Eligibility state table:

| State | Result |
| --- | --- |
| `warehouseId` absent | Active tenant methods are returned from cache/repository. No `CarrierWarehouseAssignment` filter. Manual and external methods can appear if active. |
| `warehouseId = Guid.Empty` | Empty list. |
| `warehouseId` not active/not tenant/not found | Empty list, fail closed. |
| Valid warehouse, no enabled ready external assignments | Empty list. |
| Valid warehouse, Bosta account active/ready with enabled origin-ready assignment | Bosta shipping methods can appear; manual is excluded. |
| Valid warehouse, Aramex/FedEx without ready assignment | Those carrier methods are excluded. |

Conclusion: warehouse-aware shipping eligibility exists only when a warehouse ID
is supplied. Current Storefront does not supply one, so checkout cannot rely on
this path today.

## Shipping Method And Order Relationship

Findings:

- Checkout request requires `ShippingMethodId`
  (`PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs:31`).
- Checkout request has no `WarehouseId`.
- Order persists `ShippingMethodId`, shipping method name, shipping cost,
  estimated delivery, `FulfillmentMode`, and `FulfillmentCarrierCode`
  (`CheckoutService.cs:6064`, `:6118`).
- Shipping method validation checks SQL `ShippingMethod` and legacy
  `CarrierConfiguration` for non-manual carriers
  (`CheckoutService.cs:5327`, `:5347`).
- Shipping method selection does not influence `Order.WarehouseId`.
- `Order.WarehouseId` does not influence shipping method validation in final
  checkout because it is not set and no warehouse argument is available.

Conclusion: current architecture has independent constraints. Shipping method
selection controls fulfillment mode/carrier identity, while inventory
reservation independently picks stock warehouses. There is no enforced
`Warehouse -> ShippingMethod -> Reservation -> Order` chain.

## Fulfillment And Shipment Origin

Findings:

- `Shipment` has nullable `ShippingMethodId`
  (`PointOfSale.Core/Entities/Shipment.cs:146`) and nullable
  `OriginWarehouseId` (`Shipment.cs:236`).
- Shipment creation validates the requested shipping method matches the order's
  stored `ShippingMethodId`; callers cannot substitute another method
  (`PointOfSale.API/Services/Shipping/ShipmentService.cs:129`).
- Shipment creation validates non-manual carriers through legacy
  `CarrierConfiguration`, not `CarrierAccount` or `CarrierWarehouseAssignment`
  (`ShipmentService.cs:151`).
- Shipment creation does not set `OriginWarehouseId`
  (`ShipmentService.cs:170`-`:227`).
- Carrier external registration calls origin resolution. If no stored origin
  snapshot exists, it requires `order.WarehouseId`; null/empty returns null
  (`PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs:3843`,
  `:3883`).
- Origin resolution loads the active same-tenant warehouse, requires warehouse
  name/phone/address, requires admitted warehouse address evidence, builds a
  `ShipmentOriginDto`, writes `shipment.OriginWarehouseId` and origin snapshot
  fields, and saves them (`CarrierShipmentService.cs:3892`-`:4005`).
- After successful external registration, the service must not reconstruct
  origin from live warehouse or carrier configuration
  (`CarrierShipmentService.cs:3876`).

Conclusion: `Shipment.OriginWarehouseId` is derived from `Order.WarehouseId`,
not from reservation and not from `CarrierWarehouseAssignment`. For Storefront
orders with null `Order.WarehouseId`, carrier origin registration fails closed.

## CarrierWarehouseAssignment Interaction

The relationship:

`CarrierAccount -> CarrierWarehouseAssignment -> Warehouse`

participates in:

- Admin carrier-account warehouse assignment and readiness hardening.
- Storefront shipping method listing only when a `warehouseId` query parameter is
  supplied.

It does not currently participate in:

- Storefront warehouse resolution.
- Storefront checkout final shipping method validation.
- Storefront inventory reservation.
- `Order.WarehouseId` assignment.
- Shipment origin derivation.
- Carrier external shipment execution.

If `Order.WarehouseId = Alexandria` and `ShippingMethod.CarrierCode = bosta`,
no final checkout source path was found that verifies Bosta has an enabled,
origin-ready `CarrierWarehouseAssignment` for Alexandria. The only equivalent
check is in Storefront shipping method listing with a supplied `warehouseId`
(`ShippingMethodService.cs:215`-`:227`), and the current Storefront caller does
not supply that value.

## Multi-Warehouse Orders

Current model is contradictory:

- `Order.WarehouseId` models one default fulfillment warehouse.
- `Shipment.OriginWarehouseId` models one shipment origin.
- `OrderItem.WarehouseId` exists, but Storefront does not populate it.
- `InventoryReservation` can create multiple rows for one order/order item with
  different `WarehouseId` values if quantity spans stock rows.

Source-supported verdict:

SINGLE-WAREHOUSE ORDER MODEL exists at the order/shipment authority layer, but
Storefront reservation can allocate across multiple warehouses without promoting
that allocation to order/shipment authority.

No complete multi-warehouse fulfillment model was found for Storefront orders
such as:

`Order -> Alexandria allocation + Cairo allocation -> separate fulfillment/shipment origins`

## Customer Address, Zones, And Pricing

Findings:

- Checkout address geography is admitted and snapshotted onto the order
  (`CheckoutService.cs:949`, `:6064` onward).
- Shipment destination fields derive from order shipping geography
  (`ShipmentService.cs:170`-`:227`).
- No source path was found where customer city, area, postal code, shipping zone,
  delivery zone, warehouse radius, or warehouse service area selects the
  fulfillment warehouse.
- Storefront shipping method price calculation uses method base rate, per-kg
  rate, free-shipping threshold, cart subtotal, and total weight
  (`PointOfSale.API/Services/Storefront/ShippingMethodService.cs:420`;
  `PointOfSale.API/Services/Pricing/ShippingCalculationService.cs:80`).
- No warehouse-specific storefront shipping pricing was found in the shipping
  methods list path.

Conclusion: address/geography currently affects destination snapshots and some
checkout validation surfaces, not warehouse selection. Storefront shipping
method pricing is method-global plus weight/subtotal.

## Tenant Isolation

Storefront:

- Storefront checkout does not accept warehouse IDs, so storefront users cannot
  directly submit a foreign warehouse ID in checkout.
- Storefront shipping method warehouse filter validates warehouse tenant,
  active, and non-deleted state before using `CarrierWarehouseAssignment`
  (`ShippingMethodService.cs:195`-`:204`).
- Inventory reservation stock queries always include `TenantId`
  (`ProductVariantStockRepository.cs:904`, `:925`, `:943`).
- Shipment origin loads `Warehouse` by `order.WarehouseId` and same shipment
  tenant (`CarrierShipmentService.cs:3892`-`:3900`).
- `CarrierWarehouseAssignment` filter requires both assignment and account to
  match tenant (`ShippingMethodService.cs:215`-`:227`).

No source-proven P0 cross-tenant Storefront warehouse exploit was found in this
audit. Admin/manual order warehouse validation should be separately verified if
that API is considered in scope for cross-tenant warehouse assignment, because
admin DTOs accept warehouse IDs directly.

## Idempotency And Concurrency

Findings:

- Checkout has cart snapshot hashing, persistent idempotency claim/replay, and
  duplicate active order guards before final order creation
  (`CheckoutService.cs:849`-`:930`, `:948`).
- Final order creation runs in a serializable transaction
  (`CheckoutService.cs:6358`-`:6362`).
- Stock reservation uses mutation transactions and row locks/UPDLOCK where
  provider-specific SQL is available (`ProductVariantStockRepository.cs:904`,
  `:925`).
- Reservation ordering is stable for a given current stock set because rows are
  ordered by `WarehouseId`, then `Id` (`ProductVariantStockRepository.cs:136`,
  `:946`).
- Warehouse stability is not guaranteed across checkout preview and submit,
  because preview has no warehouse and final reservation sees current stock.
- Shipping method selection can change between preview and submit; final checkout
  revalidates shipping method active/carrier configuration but not warehouse
  assignment for the reserved warehouse.

Split-brain risks:

- Reservation says Alexandria, `Order.WarehouseId` is null.
- Reservation says Alexandria, later shipment origin cannot resolve.
- Shipping method was listed without warehouse filtering, while inventory later
  reserved from a warehouse that may not have that carrier assignment.
- One order item can have reservations in Alexandria and Cairo, while order and
  shipment have only one nullable warehouse authority.

## Data Model And Migration Assessment

Static model assessment:

- `Order.WarehouseId`: sufficient only for a single-warehouse order model after a
  central resolver sets it at order creation. It is not sufficient in current
  Storefront flow because it is nullable and not populated.
- `InventoryReservation.WarehouseId`: necessary but not sufficient. It records
  stock allocation rows, not a canonical fulfillment decision, and has no
  `CartId`.
- `Shipment.OriginWarehouseId`: sufficient as a shipment-origin snapshot result
  once `Order.WarehouseId` is populated and warehouse address admission exists.
  It is not independently resolved.
- `CheckoutSession.WarehouseId`: currently absent. Some server-side checkout
  session or warehouse-resolution record is needed if shipping methods must be
  filtered before final checkout submit.
- `Cart.WarehouseId`: absent. It may not be needed if a separate
  server-owned warehouse resolution/session authority owns the decision, but cart
  stock display cannot currently explain warehouse-specific availability.
- `OrderLine warehouse allocation`: required only if the target architecture
  supports multi-warehouse orders. Current order/shipment authority is
  single-warehouse.
- `WarehouseResolution` entity: recommended if the platform must answer who
  chose the warehouse, when, by what algorithm/version, and keep retries stable.

Classification:

- API propagation gap: Storefront does not send or receive warehouse context.
- Service orchestration gap: checkout does not resolve warehouse before shipping
  method validation or reservation.
- Domain-model gap: reservation can be multi-warehouse while order/shipment
  authority is single-warehouse.
- Requires schema change: likely yes if a durable checkout/session warehouse
  decision or warehouse-resolution audit record is required.
- Requires architectural redesign: yes if multi-warehouse Storefront orders are
  required.

## Legacy Data

Static migrations/model show nullable `Orders.WarehouseId` and nullable
`Shipments.OriginWarehouseId` in migration snapshots, including
`PointOfSale.Infrastructure/Migrations/20260723214642_Baseline_Recovery3.cs:6899`
and current model snapshot entries for `OriginWarehouseId`.

Live database verification was not performed in this audit. Therefore actual
counts of:

- orders with null `WarehouseId`
- reservations with null/invalid warehouse references
- shipments with null `OriginWarehouseId`
- legacy shipping methods
- existing warehouse assignments

are unavailable.

No data was modified.

## Test Forensics

Correct or useful tests:

- `PointOfSale.Tests/Architecture/ShippingMethodEligibilityAuthorityCertificationTests.cs`
  certifies that warehouse-selected Storefront shipping method reads consume
  `CarrierWarehouseAssignment`.
- `PointOfSale.Tests/Storefront/ShippingMethodServiceCurrencyConsistencyTests.cs`
  covers warehouse-filtered carrier assignment inclusion/exclusion.
- `PointOfSale.Tests/Architecture/ShipmentOriginAuthorityTests.cs` certifies
  shipment origin snapshot and carrier-request origin DTO behavior.
- POS cart tests cover POS warehouse-selected stock flows, but those are not
  Storefront checkout flows.

Missing tests:

- Storefront checkout sets `Order.WarehouseId`.
- Storefront checkout copies reservation warehouse to order/order item.
- Storefront shipping methods are requested with a resolved warehouse.
- Final checkout validates selected carrier assignment for the same warehouse
  used by reservation/order.
- Shipment origin derives successfully for Storefront carrier orders.
- Reservation/order/shipment warehouse consistency under concurrency/retry.
- Single-warehouse rejection when inventory would require multiple warehouses.

Stale or contradictory tests/docs:

- Warehouse authority architecture tests assert `WarehouseId: null` in some
  checkout paths while also treating warehouse authority as fulfillment decision
  authority. Current Storefront source still sends null, and repository-local
  inventory allocation is the only chooser.

No tests were modified or run.

## Direct Answers

Question 1: Who currently chooses the fulfillment warehouse?

No central authority. For Storefront checkout, the inventory repository chooses
reservation stock rows by `WarehouseId`, then stock row `Id`, when no warehouse
is supplied. That is not a fulfillment warehouse decision.

Question 2: At what exact point is it chosen?

During `ValidateAndReserveInventoryAsync` inside final order creation, after
shipping/payment validation and after order/order item snapshots are built, but
before order save/commit.

Question 3: Where is the chosen WarehouseId persisted?

Only in `InventoryReservation.WarehouseId`. Storefront checkout does not persist
it to `Order.WarehouseId`, `OrderItem.WarehouseId`, or `Shipment.OriginWarehouseId`.

Question 4: Does Checkout know the warehouse before shipping methods are loaded?

No.

Question 5: Can shipping methods be filtered correctly before warehouse selection?

Only if a warehouse is supplied. Current Storefront does not supply one, so the
current runtime cannot filter correctly by warehouse assignment before selection.

Question 6: Does inventory reservation use the same warehouse?

There is no same warehouse authority to compare against. Reservation uses its own
repository allocation.

Question 7: Does `Order.WarehouseId` use the same warehouse?

No. Storefront orders leave it null.

Question 8: Does `Shipment.OriginWarehouseId` use the same warehouse?

For carrier registration it derives from `Order.WarehouseId`. Since Storefront
orders leave `Order.WarehouseId` null, it does not derive from reservation.

Question 9: Does `CarrierWarehouseAssignment` validate carrier eligibility for
that warehouse?

Only in the Storefront shipping method listing when `warehouseId` is supplied.
Not in final checkout, not in reservation, and not in shipment carrier execution.

Question 10: Is there one warehouse authority or multiple competing authorities?

There is no single authority. There are disconnected partial authorities:
shipping method listing can use supplied warehouse assignment, inventory
reservation allocates by stock row ordering, order can store one warehouse if
set by other flows, and shipment origin derives from order warehouse.

## Defect Classification

### P1: Storefront carrier orders have no origin authority

Evidence: Storefront checkout does not set `Order.WarehouseId`; shipment origin
resolution requires it (`CarrierShipmentService.cs:3883`).

Impact: Carrier-managed shipments can be created but fail external registration
because origin cannot be resolved.

Recommended fix: resolve and persist the canonical warehouse before order
creation; derive shipment origin from the same warehouse. Schema change likely
needed if the decision must be stored before submit.

### P1: Shipping method eligibility and inventory reservation can diverge

Evidence: Storefront loads shipping methods without `warehouseId`
(`checkoutStore.ts:1730`), while inventory reservation later chooses warehouse
from stock row ordering (`ProductVariantStockRepository.cs:136`, `:177`).

Impact: Customer can select a carrier method that is not assigned/ready for the
warehouse actually reserved.

Recommended fix: warehouse resolution must precede shipping method listing, and
final checkout must revalidate carrier assignment for the resolved warehouse.
Schema change likely if resolution must be durable.

### P1: Reservation/order/shipment warehouse split-brain

Evidence: `InventoryReservation.WarehouseId` is set, but
`Order.WarehouseId`, `OrderItem.WarehouseId`, and shipment origin are not copied
from it in Storefront checkout.

Impact: fulfillment, shipment origin, and inventory ledger can disagree or be
unable to proceed.

Recommended fix: define one authority and enforce propagation:
resolution -> reservation -> order -> shipment. Schema change depends on whether
single-warehouse or multi-warehouse orders are supported.

### P1: Multi-warehouse reservation contradicts single-origin shipment model

Evidence: reservation can split across stock rows; order/shipment expose one
warehouse/origin.

Impact: a single Storefront order can reserve across warehouses but has no
representable fulfillment/shipment topology.

Recommended fix: either enforce single-warehouse resolution before reservation
or redesign for order-line allocations and multiple shipments. Multi-warehouse
support requires schema and orchestration redesign.

### P2: Storefront API propagation gap

Evidence: checkout request, checkout session, cart DTO, and Storefront store
contain no warehouse field.

Impact: UI and API cannot carry or display the warehouse decision.

Recommended fix: add a server-owned checkout/session warehouse authority, not a
client-trusted arbitrary warehouse ID. Schema change likely.

### P2: Admin exposure gap

Evidence: order detail exposes `WarehouseName` but not `WarehouseId`
(`OrdersController.cs:2744`).

Impact: operators cannot reliably audit the exact warehouse identity from that
DTO surface.

Recommended fix: expose immutable warehouse identity in admin read models after
the write-side authority is corrected. No schema change if using existing
`Order.WarehouseId`.

### P2: Warehouse-specific pricing gap

Evidence: Storefront shipping method pricing uses base rate, per-kg rate,
free-shipping threshold, subtotal, and weight, with no warehouse input.

Impact: introducing warehouse routing can change cost assumptions, but current
pricing cannot represent that.

Recommended fix: decide whether warehouse affects shipping price; if yes, make
pricing consume the same resolved warehouse. Schema change depends on pricing
model.

### P3: Test/documentation gaps

Evidence: tests certify pieces of shipping eligibility, shipment origin, and
inventory reservation, but no test certifies a unified Storefront warehouse
authority.

Impact: future changes can preserve existing partial tests while still leaving
warehouse split-brain.

Recommended fix: add end-to-end authority tests after architecture is selected.
No schema change solely for tests.

## Recommended Future Architecture

For the current single-origin shipment model, the minimum coherent flow should be:

`Cart -> Warehouse Resolution -> Shipping Methods -> Checkout -> Reservation -> Order -> Shipment`

Required authority rules:

1. Warehouse resolution is server-side and tenant-scoped.
2. The resolution result is durable before shipping methods are presented, either
   in `CheckoutSession` or a dedicated `WarehouseResolution` record.
3. The result records who/what decided it: customer, server algorithm, admin
   override, version, inputs, and timestamp.
4. Shipping method listing filters external carriers through
   `CarrierWarehouseAssignment` for that resolved warehouse.
5. Final checkout revalidates the same warehouse and selected shipping method in
   the same transaction window.
6. Inventory reservation receives the resolved warehouse explicitly; null
   warehouse reservation should not be used for Storefront single-warehouse
   checkout.
7. `Order.WarehouseId` is set at creation from the resolution authority.
8. `Shipment.OriginWarehouseId` derives only from `Order.WarehouseId` and the
   admitted warehouse address snapshot.
9. If multi-warehouse fulfillment is required, do not stretch
   `Order.WarehouseId`. Add explicit order-line allocation and shipment-per-origin
   architecture instead.

Do not use `ShippingMethod` as the warehouse authority. It can constrain carrier
eligibility for a resolved warehouse, but it should not decide stock origin.

