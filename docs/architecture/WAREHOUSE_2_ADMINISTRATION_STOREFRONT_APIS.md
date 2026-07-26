# WAREHOUSE-2 Administration and Storefront API Certification

Date: 2026-07-09

Scope: Warehouse Administration APIs, Warehouse Storefront API consumption paths, Reservation APIs, Allocation APIs, Receiving APIs, Transfer APIs, Returns APIs, Warehouse Health APIs, Warehouse Assignment APIs

Frozen dependencies reviewed: `WAREHOUSE_0_AUTHORITY_ARCHITECTURE.md`, `WAREHOUSE_1_RUNTIME_AUTHORITY.md`, `POSADMIN_CHECKOUT_FINAL_END_TO_END_DEVELOPMENT_CERTIFICATION_FREEZE.md`, `AFFILIATE_CHECKOUT_FINAL_END_TO_END_DEVELOPMENT_CERTIFICATION_FREEZE.md`, `COMMERCEHUB_MARKETPLACE_CHECKOUT_FINAL_END_TO_END_DEVELOPMENT_CERTIFICATION_FREEZE.md`, and `SHIPPING_PLATFORM_ARCHITECTURE_CERTIFICATION_FREEZE.md`.

Persistent authority context reviewed: `.ai-context/ARCHITECTURE_TRUTH.md`, `.ai-context/CODEX_RULES.md`, `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`, `.ai-context/KNOWN_RISKS.md`, `.ai-context/LESSON_UPDATE_RULES.md`, `.ai-context/LOCAL_DEV_STRIPE_HANDOFF_2026-05-12.md`, and `.ai-context/PROJECT_CONTEXT.md`.

## Executive Summary

Warehouse API authority was reviewed only at the Administration, Storefront, reservation, allocation, receiving, transfer, returns, health, and assignment API surface.

Certification decision: **Warehouse Administration and Storefront APIs are certified as thin authority boundaries after one API delegation correction.**

One true API authority inconsistency was found: `WarehousesController.SaveOpeningStock` performed opening-balance admission and inventory ledger mutation directly in the controller. The fix was limited to that inconsistency: the endpoint now delegates to `IInventoryService.SaveOpeningStockAsync`, and the existing mutation behavior was moved behind the Inventory Runtime service boundary without changing the Warehouse Runtime design, Inventory Runtime design, Checkout, Shipping, or frontend.

No new permanent cross-platform architecture truth was discovered, so `.ai-context` files were not modified.

## Administration APIs

`WarehousesController` remains the Warehouse Administration boundary for warehouse CRUD, activation state, stock reads, direct adjustment admission, direct transfer admission, and opening-stock submission.

Certified after correction:

- Warehouse CRUD remains tenant-scoped and role-protected where mutating.
- Warehouse stock reads expose current inventory evidence only.
- Direct adjustment and direct transfer endpoints retain idempotency, request fingerprinting, tenant scoping, and delegation to Inventory Runtime mutation services.
- Opening-stock submission now delegates to `IInventoryService.SaveOpeningStockAsync`.
- Controller-owned opening-balance stock movement creation was removed.

Warehouse health, capacity, configuration, and assignment evidence remains API/read-model or service-delegated evidence. No controller-owned warehouse policy or capacity decision was added.

## Storefront APIs

Storefront product, funnel, cart, and checkout-facing paths consume availability, reservation, warehouse evidence, fulfillment readiness, and origin evidence only through backend service boundaries.

Certified properties:

- Storefront reads can expose availability evidence.
- Storefront does not select warehouse.
- Storefront does not mutate inventory.
- Storefront does not allocate fulfillment.
- Storefront final checkout re-enters backend reservation authority before durable order creation.

## Reservation APIs

Reservation create, commit, release, expiration, status, and recovery remain backend-owned and service-delegated.

Certified evidence:

- POS Admin checkout uses variant stock reservation repository methods.
- CommerceHub checkout calls `IInventoryConsistencyService.ReserveOrderItemAsync`.
- Unscoped CommerceHub requests pass `WarehouseId: null`, leaving warehouse selection to Warehouse/Inventory authority.
- Commit and release re-enter runtime services.
- Expiration status is resolved from persisted reservation state and TTL.

## Allocation APIs

Warehouse selection, allocation preview, allocation admission, and allocation status remain Warehouse Runtime decisions exposed as evidence only.

Certified evidence:

- Allocation uses backend stock availability and deterministic warehouse ordering.
- Allocation conflicts fail closed when current eligible stock cannot satisfy the request.
- Checkout, Marketplace, Storefront, and Shipping do not select warehouse.
- Inventory contributes ledger quantities but does not become standalone warehouse-selection authority.

## Receiving APIs

Opening balance, purchase receiving, inspection, put away, availability, and stock-adjustment admission remain delegated.

Certified after correction:

- Opening-stock mutation is now in `InventoryService.SaveOpeningStockAsync`.
- Stock adjustments continue to use `IInventoryService.AdjustStockAsync`.
- Inventory mutation remains behind locked `ProductVariantStock` rows and stock movement evidence.
- Controllers do not own ledger mutation decisions.

## Transfer APIs

Transfer create, approve, reject, complete, rollback behavior, and history remain orchestration boundaries.

Certified evidence:

- Create transfer validates request shape and workflow admission, then persists transfer workflow evidence.
- Approval and rejection are state-transition orchestration.
- Completion delegates quantitative movement to `IInventoryService.TransferStockAsync`.
- Transfer history is read-model evidence.
- Rollback remains transaction-scoped.

## Returns APIs

Customer return, vendor return, receipt, restock, damaged, scrap, and audit decisions remain backend-owned.

Certified evidence:

- CommerceHub return receipt delegates to `IInventoryConsistencyService.ReceiveReturnAsync`.
- Return receipt and override flows append audit evidence.
- POS sales and purchase returns remain tenant-scoped, idempotent, invoice-lineage-bound workflows with inventory mutation through runtime stock helpers and stock movement evidence.
- Payment/refund and accounting paths remain outside Warehouse authority.

## Security Review

Certified controls:

- Tenant isolation uses trusted `ITenantContext.RequiredTenantId` and tenant-scoped queries.
- Mutating administration endpoints require role authorization.
- Replay protection uses idempotency keys, request IDs, request fingerprints, persisted idempotency rows, and stored responses where implemented.
- Correlation IDs are normalized through request metadata and trace IDs.
- API error normalization uses `ApiErrorResponseNormalizationFilter` and the unified `ApiErrorResponse` envelope for non-success object results.
- Secret safety is preserved because Warehouse APIs do not expose carrier credentials, payment secrets, webhook secrets, or provider credentials.
- PII exposure is limited to existing warehouse, customer, supplier, invoice, and return presentation fields; no new PII-bearing response was added.

## API Contracts

Certified properties:

- Warehouse DTOs are warehouse-neutral and carrier-neutral.
- Reservation and allocation evidence does not duplicate Checkout DTO ownership.
- Shipping consumes admitted origin and fulfillment evidence; Warehouse APIs do not duplicate Shipping carrier DTOs.
- Responses remain version-safe because the correction preserves the existing opening-stock JSON response shape.
- Unified error normalization remains the API-wide model.

## Regression Coverage

Focused Warehouse API certification tests were added in `WarehouseAdministrationStorefrontApiCertificationTests`.

Coverage verifies:

- Administration API delegation.
- Opening-stock mutation delegation.
- Reservation API delegation.
- Allocation API delegation.
- Receiving API delegation.
- Transfer API orchestration.
- Returns API delegation.
- Storefront API evidence consumption.
- No opening-stock business mutation inside the controller.
- Tenant/idempotency/correlation security evidence.
- Unified error model registration.
- WAREHOUSE-2 certification document freeze.

Validation command:

```powershell
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~WarehouseAuthorityArchitectureCertificationTests|FullyQualifiedName~WarehouseAdministrationStorefrontApiCertificationTests" -nologo -v minimal
```

## Certification Decision

- Reviewed ONLY Warehouse APIs? **Yes.**
- Runtime modified? **Yes, narrowly: existing opening-stock mutation logic was moved behind `IInventoryService`; no Warehouse Runtime redesign, Inventory Runtime redesign, or behavior redesign was performed.**
- Backend modified? **Yes, one API authority inconsistency was fixed.**
- Frontend modified? **No.**
- Warehouse Administration APIs Certified? **Yes.**
- Storefront APIs Certified? **Yes.**
- Reservation APIs Certified? **Yes.**
- Allocation APIs Certified? **Yes.**
- Receiving APIs Certified? **Yes.**
- Transfer APIs Certified? **Yes.**
- Returns APIs Certified? **Yes.**
- API Delegation Certified? **Yes.**
- Unified Error Model Certified: **Yes.**
- Security Certified? **Yes.**
- Architecture preserved? **Yes.**
- Backward compatible? **Yes.**
- Production-certifiable APIs: **Yes.**
- Recommend next phase: **WAREHOUSE-3 Operational Readiness Certification for dashboards, alerts, live WMS/receiving/transfer/return drills, warehouse health evidence, runbooks, rollback proof, and production rollout gates.**
