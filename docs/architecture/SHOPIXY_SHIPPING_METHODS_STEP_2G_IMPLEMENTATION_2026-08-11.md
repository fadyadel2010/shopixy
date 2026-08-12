# SHOPIXY Shipping Methods Step 2G Implementation

Date: 2026-08-11

Final Certification: PASS WITH EXTERNAL VERIFICATION BLOCKED

## 1. Executive Summary

Implemented Shipping Method -> Fulfillment Warehouse Authority.

`ShippingMethod.FulfillmentWarehouseId` is now the method-level fulfillment origin. Storefront preview admits only active methods whose configured tenant warehouse is active, stock-capable for the cart, and carrier-ready where applicable. Storefront selection submits only `shippingMethodId`; the server creates or refreshes a `WarehouseResolution` for the method's configured warehouse. Final checkout validates that the submitted resolution warehouse still equals the selected method warehouse before reservation/order/shipment propagation.

## 2. Architecture Implemented

Implemented flow:

```text
ShippingMethod.FulfillmentWarehouseId
  -> Storefront customer selects shippingMethodId
  -> WarehouseResolution.WarehouseId
  -> explicit inventory reservation
  -> Order.WarehouseId
  -> OrderItem.WarehouseId
  -> Shipment.OriginWarehouseId
```

No split fulfillment, routing engine, winner policy, geographic routing, customer warehouse selection, carrier redesign, payment change, R2 change, or dynamic-field change was implemented.

## 3. Domain Changes

- `PointOfSale.Core/Entities/ShippingMethod.cs`
  - Class: `ShippingMethod`
  - Property: `Guid? FulfillmentWarehouseId`
  - Navigation: `Warehouse? FulfillmentWarehouse`
  - Behavior: nullable for legacy rows; means "tenant-owned warehouse from which this shipping method fulfills orders."

## 4. EF Model

- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
  - Entity: `ShippingMethod`
  - Index: `IX_ShippingMethods_TenantId_FulfillmentWarehouseId`
  - Relationship: `(TenantId, FulfillmentWarehouseId) -> Warehouses(TenantId, Id)`
  - Delete behavior: `Restrict`
  - Tenant isolation: composite FK prevents cross-tenant method-to-warehouse references.

## 5. Migration

- Added migration:
  - `PointOfSale.Infrastructure/Migrations/20260811160600_SHOPIXY_ShippingMethodFulfillmentWarehouseAuthority.cs`
  - Designer snapshot updated:
    `PointOfSale.Infrastructure/Migrations/20260811160600_SHOPIXY_ShippingMethodFulfillmentWarehouseAuthority.Designer.cs`
  - Model snapshot updated:
    `PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- Migration operations:
  - Adds nullable `ShippingMethods.FulfillmentWarehouseId`.
  - Adds non-unique `IX_ShippingMethods_TenantId_FulfillmentWarehouseId`.
  - Adds composite tenant-safe FK to `Warehouses(TenantId, Id)`.
  - Does not backfill existing rows.
- Generated idempotent SQL:
  - `docs/architecture/SHOPIXY_SHIPPING_METHODS_STEP_2G_IDEMPOTENT_MIGRATION_2026-08-11.sql`

## 6. Admin API

- `PointOfSale.API/Controllers/ShippingMethodsController.cs`
  - DTO: `ShippingMethodDto.FulfillmentWarehouseId`
  - DTO: `CreateShippingMethodDto.FulfillmentWarehouseId`
  - Method: `CreateShippingMethod`
  - Method: `UpdateShippingMethod`
  - Method: `MapToDto`
  - Method: `ValidateFulfillmentWarehouseAsync`
- Validation:
  - Empty GUID rejected.
  - Missing/deleted warehouse rejected.
  - Inactive warehouse rejected.
  - Lookup is tenant-scoped through `TenantId`.
  - Display name or metadata is never accepted as authority.

## 7. Admin UI

- `PointOfSale.Web/src/modules/shipping-administration/ShippingAdministrationWorkspace.jsx`
  - Passes existing warehouse API data into `ShippingMethodsTab`.
- `PointOfSale.Web/src/modules/shipping-administration/components/ShippingMethodsTab.jsx`
  - Adds `Fulfillment Warehouse` selector.
  - Displays active warehouses by name and optional code.
  - Shows `Not configured` for legacy null methods.
  - Preselects the only active warehouse for new method creation without persisting until save.
  - Sends `fulfillmentWarehouseId` on create/update.

## 8. Manual Fulfillment

- `PointOfSale.API/Services/Storefront/WarehouseResolutionService.cs`
  - Method: `ValidateSelectedShippingMethodAuthorityAsync`
  - Manual carrier code returns the configured method warehouse directly.
  - Manual does not require `CarrierAccount` or `CarrierWarehouseAssignment`.

## 9. External Carrier Fulfillment

- `PointOfSale.API/Services/Storefront/WarehouseResolutionService.cs`
  - Method: `ValidateSelectedShippingMethodAuthorityAsync`
  - External carriers require:
    - method-level `FulfillmentWarehouseId`;
    - active same-tenant warehouse;
    - enabled legacy `CarrierConfiguration`;
    - active/ready `CarrierAccount`;
    - enabled origin-ready `CarrierWarehouseAssignment` for that exact warehouse.
- `CarrierWarehouseAssignment` remains carrier-account origin readiness, not method origin authority.

## 10. Storefront Preview

- `PointOfSale.API/Controllers/StorefrontShippingMethodsController.cs`
  - Method: `PreviewShippingMethods`
  - Uses `IWarehouseResolutionService.LoadPreviewCartAsync` to validate session/cart ownership without creating a warehouse resolution.
  - Calls `IShippingMethodService.GetShippingMethodsAvailabilityForConfiguredWarehousesAsync`.
- `PointOfSale.API/Services/Storefront/ShippingMethodService.cs`
  - Method: `GetShippingMethodsAvailabilityForConfiguredWarehousesAsync`
  - Filters active methods by configured warehouse, active warehouse, cart stock, and external carrier readiness.
  - Returns customer-facing shipping method DTOs only. Warehouse IDs are not exposed.

## 11. Shipping Selection

- `PointOfSale.API/DTOs/Storefront/ShippingMethodDto.cs`
  - DTO: `ShippingMethodSelectionRequestDto`
  - DTO: `ShippingMethodSelectionResponseDto`
- `PointOfSale.API/Controllers/StorefrontShippingMethodsController.cs`
  - Endpoint: `POST /api/storefront/shipping-methods/select`
  - Method: `SelectShippingMethod`
  - Accepts `checkoutSessionId` and `shippingMethodId`; does not accept `warehouseId`.
- `PointOfSale.API/Services/Storefront/WarehouseResolutionService.cs`
  - Method: `ResolveForSelectedShippingMethodAsync`
  - Creates/reuses `WarehouseResolution` for the selected method's configured warehouse.

## 12. WarehouseResolution Repositioning

- `PointOfSale.API/Services/Storefront/WarehouseResolutionService.cs`
  - Constant: `STOREFRONT_SHIPPING_METHOD_FULFILLMENT_WAREHOUSE_V1`
  - Method: `BuildResolutionInputForWarehouseAsync`
  - Resolution hash includes `ShippingMethodId` and `FulfillmentWarehouseId`.
  - Existing status, version, expiry, lock, immutable authority, and final validation remain.
  - No `ShippingMethodId` column was added to `WarehouseResolution`; the input hash/snapshot records the selected method context.

## 13. Checkout Finalization

- `PointOfSale.API/Services/Storefront/WarehouseResolutionService.cs`
  - Method: `ValidateSubmittedResolutionAsync`
  - Revalidates selected method, active configured warehouse, carrier readiness, stock, and confirms `resolution.WarehouseId == method.FulfillmentWarehouseId`.
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
  - Existing Step 2F final transaction still calls `ValidateSubmittedResolutionAsync(lockForCheckout: true)`.

## 14. Inventory Reservation

- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
  - Method: `ValidateAndReserveInventoryAsync`
  - Storefront checkout continues passing the explicit resolved warehouse.
  - Null-warehouse reservation fallback is not used in this path.

## 15. Order/Shipment Propagation

- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
  - Sets `order.WarehouseId = finalWarehouseResolution.WarehouseId`.
  - Sets each Storefront `orderItem.WarehouseId = finalWarehouseResolution.WarehouseId`.
- `PointOfSale.API/Services/Shipping/ShipmentService.cs`
  - Existing Step 2F behavior derives `OriginWarehouseId` from `order.WarehouseId`.

## 16. Idempotency

- `PointOfSale.API/Services/Checkout/CheckoutCartIdempotencySnapshotBuilder.cs`
  - Existing Step 2F resolution ID/version/warehouse participation remains.
- Step 2G additionally puts `ShippingMethodId` into the `WarehouseResolution` input hash/snapshot, preventing different selected methods from sharing a method-selection resolution accidentally.

## 17. Tenant Isolation

- Admin warehouse lookup uses `TenantId`.
- Storefront method selection loads `ShippingMethod`, `Warehouse`, `CarrierConfiguration`, `CarrierAccount`, and `CarrierWarehouseAssignment` by current tenant.
- Database composite FK enforces tenant consistency for `ShippingMethod.FulfillmentWarehouseId`.
- Storefront never accepts `warehouseId` as authority.

## 18. Backward Compatibility

- Existing shipping methods remain valid rows with nullable `FulfillmentWarehouseId`.
- Legacy null methods remain visible in Admin as `Not configured`.
- Legacy null methods are excluded from Storefront preview and cannot create a Storefront warehouse resolution.
- No unsafe backfill was performed.

## 19. Tests

Backend focused tests:

- `PointOfSale.Tests/Architecture/ShopixyShippingMethodFulfillmentWarehouseAuthorityTests.cs`
- `PointOfSale.Tests/Architecture/ShopixyWarehouseResolutionAuthorityCertificationTests.cs`

Frontend focused tests:

- `PointOfSale.Storefront/src/commerceAuthority.certification.test.ts`
- `PointOfSale.Web/src/modules/shipping-administration/ShippingAdministrationWorkspace.test.tsx`

## 20. Build Verification

Executed:

- `dotnet build PointOfSale.API\PointOfSale.API.csproj --no-restore`: PASS with existing warnings.
- `dotnet build PointOfSale.sln --no-restore`: PASS, 0 warnings in incremental build.
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~ShopixyWarehouseResolutionAuthorityCertificationTests|FullyQualifiedName~ShopixyShippingMethodFulfillmentWarehouseAuthorityTests"`: PASS, 9/9.
- `npm test -- --run src/commerceAuthority.certification.test.ts` in `PointOfSale.Storefront`: PASS, 29/29.
- `npm test -- --run src/modules/shipping-administration/ShippingAdministrationWorkspace.test.tsx` in `PointOfSale.Web`: fork-worker timeout before tests; rerun with `npx vitest run src/modules/shipping-administration/ShippingAdministrationWorkspace.test.tsx --pool=threads`: PASS, 13/13.
- `npm run build` in `PointOfSale.Storefront`: PASS with existing Vite/Browserslist/chunk warnings.
- `npm run build` in `PointOfSale.Web`: PASS.

## 21. PostgreSQL Verification

Executed:

- `dotnet ef migrations list --project PointOfSale.Infrastructure\PointOfSale.Infrastructure.csproj --startup-project PointOfSale.API\PointOfSale.API.csproj --context ApplicationDbContext --no-build`: PASS; Step 2F and Step 2G migrations are pending.
- `dotnet ef migrations has-pending-model-changes --project PointOfSale.Infrastructure\PointOfSale.Infrastructure.csproj --startup-project PointOfSale.API\PointOfSale.API.csproj --context ApplicationDbContext --no-build`: PASS, no pending model changes.
- `dotnet ef migrations script --idempotent ...`: first attempt to `C:\tmp` failed with sandbox access denied; regenerated successfully to `docs/architecture/SHOPIXY_SHIPPING_METHODS_STEP_2G_IDEMPOTENT_MIGRATION_2026-08-11.sql`.

Not performed:

- Scratch PostgreSQL application/catalog inspection. No disposable PostgreSQL runtime was used in this pass.

## 22. Unrelated Existing Failures

- Admin Web Vitest default fork pool timed out before tests executed, matching a known prior environment issue. Threads pool passed.
- Existing compiler/Vite warnings remain unrelated.
- The worktree already contained extensive Step 2A-2F and dynamic checkout/R2 changes before Step 2G.

## 23. Worktree Safety

Baseline before Step 2G:

- `git status --short`: heavily dirty with modified shipping/checkout/R2/frontend/test files and untracked Step 2A-2G evidence files.
- `git diff --stat`: 43 tracked files, 2010 insertions, 221 deletions.

Final after Step 2G:

- `git status --short`: still heavily dirty; new Step 2G migration, test, SQL, and implementation report added.
- `git diff --stat`: 46 tracked files, 2581 insertions, 227 deletions, plus untracked migration/test/report/SQL files.

No reset, clean, checkout, restore, stash, commit, push, or unrelated revert was performed.

## 24. Remaining External Verification

- Apply pending Step 2F and Step 2G migrations to an isolated PostgreSQL database and inspect catalog constraints/indexes/history.
- Run true PostgreSQL race tests for concurrent method selection, stale resolution, stock contention, and carrier readiness changes.
- Perform live Storefront/Admin API smoke verification only when explicitly requested and with live dependencies available.

## 25. Final Certification

PASS WITH EXTERNAL VERIFICATION BLOCKED

Implementation, focused tests, builds, EF pending-model verification, migration inspection, and idempotent SQL generation are complete. Remaining blockers are external PostgreSQL/live verification and broader race/integration evidence, not missing source implementation.
