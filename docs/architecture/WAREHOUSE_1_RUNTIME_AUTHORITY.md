# WAREHOUSE-1 Runtime Authority Certification

Date: 2026-07-09

Scope: Warehouse Runtime, Reservation Runtime, Allocation Runtime, Receiving Runtime, Transfer Runtime, Returns Runtime, Inventory Admission, Warehouse Selection, Warehouse Policies, Warehouse Health

Frozen dependencies reviewed: `WAREHOUSE_0_AUTHORITY_ARCHITECTURE.md`, `POSADMIN_CHECKOUT_FINAL_END_TO_END_DEVELOPMENT_CERTIFICATION_FREEZE.md`, `AFFILIATE_CHECKOUT_FINAL_END_TO_END_DEVELOPMENT_CERTIFICATION_FREEZE.md`, `COMMERCEHUB_MARKETPLACE_CHECKOUT_FINAL_END_TO_END_DEVELOPMENT_CERTIFICATION_FREEZE.md`, `SHIPPING_PLATFORM_ARCHITECTURE_CERTIFICATION_FREEZE.md`, and `FEDEX_FINAL_END_TO_END_DEVELOPMENT_CERTIFICATION_FREEZE_UPDATED.md`.

Persistent authority context reviewed: `.ai-context/ARCHITECTURE_TRUTH.md`, `.ai-context/CODEX_RULES.md`, `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`, `.ai-context/KNOWN_RISKS.md`, `.ai-context/LESSON_UPDATE_RULES.md`, `.ai-context/LOCAL_DEV_STRIPE_HANDOFF_2026-05-12.md`, and `.ai-context/PROJECT_CONTEXT.md`.

Certification decision: **Warehouse Runtime is certified as the operational fulfillment authority. Inventory Runtime remains certified as the quantitative ledger authority.**

## Executive Summary

Warehouse Runtime remains the authority for operational fulfillment decisions: warehouse selection, reservation admission, allocation decisioning, receiving workflow, transfer workflow, return disposition, policy enforcement, health, and audit.

Inventory Runtime remains the authority for stock quantities and movement evidence: on hand, reserved, available, damaged, incoming, picking, packing, transit, returned, stock movement rows, reservation rows, and ledger audit.

No Checkout, Shipping, Marketplace, Payment, Order, Settlement, or carrier runtime was redesigned or modified. No true runtime inconsistency was found. Runtime code is frozen.

## Runtime Overview

Runtime evidence confirms the WAREHOUSE-0 authority split:

- Warehouse decides whether fulfillment can proceed and which warehouse evidence is admissible.
- Inventory records quantitative ledger effects and rejects invalid quantity mutations.
- Checkout submits reservation requests and consumes reservation results.
- Shipping consumes admitted origin, fulfillment readiness, and reservation evidence.
- Marketplace supplies merchant context and partitioning; Warehouse Runtime performs warehouse selection, reservation, and allocation.

Implementation evidence includes `WarehousesController`, `StockTransfersController`, `InventoryService`, `InventoryConsistencyService`, `ProductVariantStockRepository`, `ReturnManagementService`, and focused architecture/runtime tests.

## Reservation Runtime

Reservation admission is backend-owned and fail closed.

Certified properties:

- Reservation requests reject non-positive quantities.
- Reservation replay is guarded by tenant-scoped `InventoryEventInbox` event idempotency.
- Duplicate event IDs with conflicting payloads are rejected.
- Reservation TTL is persisted through `ExpiresAtUtc`.
- Reservation commit and release re-enter Inventory Runtime through repository/service methods.
- Reservation expiration is resolved from current reservation state and TTL.
- Reservation audit is represented by inventory event inbox rows, stock movements, reservation event IDs, correlation IDs, source, and movement metadata.
- Checkout requests reservation but does not mutate stock directly.
- Warehouse admits reservation by selecting eligible current stock rows.
- Inventory records reserved quantity in stock and reservation rows.

## Allocation Runtime

Allocation remains the Warehouse Runtime decision that turns admissible availability into a fulfillment source or plan.

Certified properties:

- Warehouse selection is performed by backend warehouse/inventory authority, never by Checkout, Shipping, Inventory alone, or Marketplace.
- Unscoped Marketplace reservation requests pass `WarehouseId: null`; Warehouse Runtime selects eligible stock deterministically by available quantity, warehouse id, and row id.
- Scoped reservation requests honor warehouse evidence only after tenant/current lineage admission.
- Partial and split allocation are supported by per-line reservation rows across eligible stock rows.
- Cross-warehouse fulfillment is represented by transfer and reservation evidence, not by Shipping or Checkout authority.
- Allocation conflicts fail closed when eligible current stock cannot satisfy requested quantity.
- Marketplace contributes store/merchant context only and does not select a warehouse.

## Receiving Runtime

Receiving remains Warehouse-owned for inspection, put-away, and availability decisions; Inventory records quantitative effects.

Certified properties:

- Opening balance and stock adjustment requests enter through warehouse administration endpoints.
- Receiving and adjustment mutations call `InventoryService.AdjustStockAsync`.
- Inventory Runtime locks stock rows before quantity mutation.
- Negative resulting inventory is rejected.
- Stock movements record tenant, warehouse, product, variant, movement type, quantity after, available after, reserved after, damaged after, reference, notes, and timestamp evidence.
- Committed stock events are published after persistence.

## Transfer Runtime

Transfer remains Warehouse-owned for reserve, ship, transit, receive, commit, rollback, and audit workflow.

Certified properties:

- Transfer creation validates item presence, positive quantity, tenant-owned warehouses, active warehouses, and distinct source/target warehouses.
- Transfer creation locks source stock for availability proof before creating the transfer.
- Transfer approval/rejection is state-machine guarded.
- Transfer completion requires approved status.
- Transfer completion calls `InventoryService.TransferStockAsync`.
- Inventory Runtime locks source and target stock rows deterministically and rejects unavailable source stock.
- Transfer commit writes transfer-out and transfer-in stock movements.
- Transfer rollback is transaction-scoped: failures roll back controller transaction work before commit.
- Transfer audit includes transfer status, actor, timestamps, movement rows, references, idempotency, and committed stock sync events.

## Returns Runtime

Returns remain Warehouse-owned for physical receipt, inspection, restock, damaged, scrap/unavailable disposition, and audit. Payment/refund lifecycles remain outside Warehouse.

Certified properties:

- Return workflow is created only from approved refund state requiring return handling.
- Return pickup, transit, delivery, failure, receipt, and override transitions are backend-owned.
- Receipt confirmation validates vendor/return authority and uses serialized retry.
- Receipt admission calls `IInventoryConsistencyService.ReceiveReturnAsync`.
- Resellable receipts increase sellable stock through inventory admission.
- Damaged receipts increase damaged bucket through inventory admission.
- Cumulative received quantity cannot exceed delivered/order authority.
- Return audit is append-only through return audit logs with actor, source, correlation, status, and quantity details.

## Inventory Admission

Inventory Runtime owns only quantitative ledger mutation:

- Ledger mutation.
- Stock movement.
- On hand.
- Reserved.
- Available.
- Damaged.
- Incoming.
- Picking.
- Packing.
- Transit.
- Returned.

Warehouse Runtime owns decisions. Inventory Runtime owns quantities.

Inventory admission rejects:

- Negative mutation outcomes.
- Non-existent source stock for decrease/transfer.
- Missing or stale inventory lineage.
- Expired, released, or over-consumed reservations.
- Return receipts exceeding delivered quantity.
- Duplicate event replay with payload conflict.
- Tenant or warehouse spoofing through tenant-scoped queries and lineage filters.

## Checkout Integration

Checkout is a reservation consumer only.

Certified properties:

- Checkout submits reservation through backend inventory/warehouse authority.
- Checkout does not mutate stock directly as its own authority.
- Checkout does not allocate warehouse as its own authority.
- Checkout consumes reservation result and fails closed when reservation cannot be admitted.
- Checkout release/commit paths re-enter inventory reservation APIs.

## Shipping Integration

Shipping consumes admitted fulfillment evidence only.

Certified properties:

- Shipping consumes origin warehouse, fulfillment readiness, and reservation evidence.
- Shipping owns shipment identity, lifecycle, carrier execution, provider evidence, labels, tracking, and shipment snapshots.
- Shipping never reserves inventory.
- Shipping never allocates warehouse.
- Shipping never mutates inventory ledger.
- Carrier runtimes consume admitted origin snapshots and do not become Warehouse or Inventory authority.

## Marketplace Integration

Marketplace contributes merchant context and partitioning only.

Certified properties:

- Marketplace provides merchant context, merchant partition, store grouping, and checkout evidence.
- Warehouse Runtime performs warehouse selection, reservation, and allocation.
- Marketplace never chooses warehouse.
- Marketplace never mutates inventory.
- Marketplace checkout calls backend reservation services and fails closed on reservation conflict.

## Failure Matrix

| Failure | Certified behavior |
| --- | --- |
| Unknown Warehouse | Fail closed through tenant warehouse lookup or stock-row absence. |
| Inactive Warehouse | Fail closed before transfer creation. |
| Negative Inventory | Fail closed through stock mutation validation. |
| Reservation Conflict | Fail closed with no reservation admitted. |
| Allocation Conflict | Fail closed when current eligible stock is insufficient. |
| Expired Reservation | Excluded from active/consumable reservation flows. |
| Transfer Conflict | Fail closed through idempotency, state validation, row locking, and transaction rollback. |
| Receiving Conflict | Fail closed through stock mutation validation and transaction rollback. |
| Return Conflict | Fail closed when return state, vendor authority, quantity, or inventory admission is invalid. |
| Warehouse Spoofing | Fail closed through tenant-scoped warehouse and stock queries. |
| Inventory Spoofing | Fail closed through current product/variant/warehouse lineage filters. |
| Duplicate Reservation | Idempotent replay returns already-handled result or rejects payload conflict. |
| Reservation Replay | Tenant/event inbox gate prevents duplicate mutation. |

## Security Review

Certified controls:

- Reservation replay is guarded by event inbox idempotency.
- Duplicate reservation payload conflict is rejected.
- Reservation TTL is stored and enforced.
- Warehouse spoofing is blocked by tenant-scoped warehouse and stock reads.
- Inventory spoofing is blocked by current lineage filters and tenant-owned stock rows.
- Tenant isolation is enforced by trusted tenant context and tenant-scoped queries.
- Stock movements are immutable ledger evidence for quantitative changes.
- Structured audit exists through stock movements, inventory events, idempotency records, return audit logs, status timestamps, actor IDs, correlation IDs, and source fields.

## Regression Coverage

Focused Warehouse Runtime certification tests were added to `WarehouseAuthorityArchitectureCertificationTests`.

Coverage verifies:

- Reservation admission.
- Allocation authority.
- Receiving runtime.
- Transfer runtime.
- Return runtime.
- Inventory admission.
- Checkout integration.
- Shipping integration.
- Marketplace integration.
- No authority violations.

Validation command:

```powershell
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~WarehouseAuthorityArchitectureCertificationTests" -nologo -v minimal
```

## Certification Decision

- Reviewed ONLY Warehouse Runtime? **Yes.**
- Runtime modified? **No.**
- Backend modified? **No.**
- Frontend modified? **No.**
- Warehouse Runtime Certified? **Yes.**
- Reservation Runtime Certified? **Yes.**
- Allocation Runtime Certified? **Yes.**
- Receiving Runtime Certified? **Yes.**
- Transfer Runtime Certified? **Yes.**
- Returns Runtime Certified? **Yes.**
- Inventory Admission Certified? **Yes.**
- Checkout Integration Certified? **Yes.**
- Shipping Integration Certified? **Yes.**
- Marketplace Integration Certified? **Yes.**
- Warehouse owns Checkout? **No.**
- Inventory owns Warehouse Selection? **No.**
- Architecture preserved? **Yes.**
- Backward compatible? **Yes.**
- Production-certifiable runtime? **Yes.**
- Recommend next phase: **WAREHOUSE-2 Operational Readiness Certification for observability, WMS handoff readiness, warehouse health dashboards, live transfer/receiving runbooks, and production rollout gates.**
