# SHOPIXY Shipping Methods Step 2F Forensic Implementation Report

Date: 2026-08-11

## Baseline

The Step 2E design report is the authority for this implementation:

- `docs/architecture/SHOPIXY_SHIPPING_METHODS_STEP_2E_WAREHOUSE_RESOLUTION_DESIGN_2026-08-11.md`

Initial worktree state before Step 2F edits:

- Existing dirty files were present in shipping administration, Storefront shipping, checkout store tests, and architecture reports.
- No reset, checkout, clean, commit, or push was performed.
- Step 2F edits must preserve existing dirty work and avoid broad unrelated refactors.

Current production risk confirmed from source:

- Storefront shipping methods can be requested with no warehouse authority.
- The legacy shipping endpoint accepts an optional `warehouseId` query value.
- Final Storefront checkout does not require `CheckoutSessionId`, `WarehouseResolutionId`, or resolution version.
- Checkout inventory reservation currently calls `ReserveAsync(..., warehouseId: null, ...)`.
- `ProductVariantStockRepository` can spread a null-warehouse reservation across warehouses in deterministic lock order.
- Storefront order creation does not set `Order.WarehouseId` or `OrderItem.WarehouseId`.
- `ShippingMethodService` currently excludes Manual Fulfillment when a warehouse filter is provided because it requires carrier assignments for all returned methods.

## Actual Runtime Call Graph

Current Storefront checkout path:

```text
Storefront checkout page
  -> CheckoutSessionController.CreateOrResume
  -> CheckoutSessionService.CreateOrResumeSessionWithOutcomeAsync
  -> StorefrontShippingMethodsController.GetShippingMethods
  -> ICartService.GetCartAsync
  -> ShippingMethodService.GetShippingMethodsAvailabilityAsync
  -> ShippingMethodService.GetShippingMethodsAsync
  -> checkoutStore submits CheckoutRequestDto
  -> CheckoutController.Checkout
  -> CheckoutService.ProcessCheckoutAsync
  -> GetCartSnapshotAsync
  -> CheckoutCartIdempotencySnapshotBuilder.BuildHash
  -> CheckoutRequestIdempotencyCoordinator.TryClaimAsync
  -> ValidateShippingMethodAsync
  -> RevalidateCheckoutAuthorityInsideTransactionAsync
  -> ValidateAndReserveInventoryAsync
  -> ProductVariantStockRepository.ReserveAsync(warehouseId: null)
  -> Order and OrderItem creation
  -> InventoryReservation binding
  -> OrderCreated event
  -> shipment orchestration
```

Current warehouse-related Storefront paths:

- Shipping method filtering can use a client-supplied `warehouseId` query parameter.
- Inventory reservation can use `warehouseId = null`, allowing repository-level selection.
- Shipment origin derives later from order/shipping authority and must not become a resolver.
- Carrier assignment eligibility exists only after a warehouse is supplied.

## Files Inspected

- `PointOfSale.Core/Entities/CheckoutSession.cs`
- `PointOfSale.Infrastructure/Services/CheckoutSessionService.cs`
- `PointOfSale.API/Controllers/CheckoutSessionController.cs`
- `PointOfSale.API/Controllers/CheckoutController.cs`
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.API/Services/Checkout/CheckoutCartIdempotencySnapshotBuilder.cs`
- `PointOfSale.API/Services/Checkout/CheckoutRequestIdempotencyCoordinator.cs`
- `PointOfSale.API/Services/Checkout/ICheckoutService.cs`
- `PointOfSale.Infrastructure/Data/Repositories/ProductVariantStockRepository.cs`
- `PointOfSale.Core/Entities/ProductVariantStock.cs`
- `PointOfSale.Core/Entities/Order.cs`
- `PointOfSale.Core/Entities/OrderItem.cs`
- `PointOfSale.Core/Entities/Shipment.cs`
- `PointOfSale.API/Services/Storefront/ShippingMethodService.cs`
- `PointOfSale.API/Services/Storefront/IShippingMethodService.cs`
- `PointOfSale.API/Controllers/StorefrontShippingMethodsController.cs`
- `PointOfSale.Core/Entities/ShippingAdministration/CarrierAccountRuntimeEntities.cs`
- `PointOfSale.Core/Entities/Warehouse.cs`
- `PointOfSale.Core/Constants/CarrierCodes.cs`
- `PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs`
- `PointOfSale.API/DTOs/Storefront/ShippingMethodDto.cs`
- `PointOfSale.API/DTOs/Cart/CartDtos.cs`
- `PointOfSale.API/Services/Cart/ICartService.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
- `PointOfSale.API/Program.cs`
- `PointOfSale.Storefront/src/store/checkoutStore.ts`
- `PointOfSale.Storefront/src/components/checkout/ShippingStep.tsx`

## Authority Boundaries

- `CheckoutSession` is the parent and caller/cart scope, not the warehouse authority.
- `WarehouseResolution` will be the sole warehouse authority for Storefront checkout.
- Cart ownership remains authoritative through existing tenant, owner, session, and customer validation.
- Product and variant stock policy remains authoritative through SQL `Product`, `ProductVariant`, and `ProductVariantStock`.
- Shipping method definition remains SQL authority at final checkout and projection/read service for Storefront listing.
- `CarrierWarehouseAssignment` constrains external carrier eligibility after resolution, not warehouse selection.
- Shipment creation may validate order origin but must not choose a different warehouse.

## Implementation Plan

1. Add `WarehouseResolution` entity and status/source enums.
2. Map it in EF with tenant query filter, relationships, indexes, and filtered uniqueness for active statuses.
3. Add `IWarehouseResolutionService` to resolve exact-one-candidate only, fail on zero or multiple candidates, invalidate stale rows, and lock final checkout resolution.
4. Add `POST /api/storefront/shipping-methods/preview` accepting `checkoutSessionId` only as authority input.
5. Use server-owned `WarehouseResolution.WarehouseId` to filter shipping methods.
6. Fix Manual Fulfillment eligibility under warehouse filtering.
7. Extend checkout request DTO and checkout idempotency snapshot/hash with resolution ID, warehouse ID, and version.
8. Revalidate and lock `WarehouseResolution` inside the existing serializable order transaction.
9. Reserve inventory with explicit resolved warehouse ID.
10. Set `Order.WarehouseId` and each Storefront `OrderItem.WarehouseId`.
11. Add focused regression tests where feasible without altering unrelated behavior.
12. Generate an additive migration and verify pending model changes.

## Risks

- The current checkout service is large and carries multiple authority validations in one class, so edits must stay narrow.
- Existing dirty Storefront and shipping files overlap Step 2F.
- Scratch PostgreSQL availability is not proven yet.
- The final full test matrix is broad; unrelated failures must be classified, not papered over.
- Multiple-candidate warehouse selection is intentionally a business conflict, not a selectable implementation detail.

## Exact Changes

- Added `WarehouseResolution` as a dedicated domain authority with status/source enums.
- Added EF mapping, query filter, filtered active uniqueness, composite tenant/session and tenant/warehouse FKs, and migration `20260811142427_SHOPIXY_WarehouseResolutionAuthority`.
- Added `IWarehouseResolutionService` / `WarehouseResolutionService` with exact-one-candidate resolution, stale input hashing, invalidation, final locking, carrier eligibility validation, and ordered marking.
- Added `POST /api/storefront/shipping-methods/preview`; the Storefront preview request carries only `checkoutSessionId`, not warehouse authority.
- Fixed Manual Fulfillment filtering so manual methods do not require carrier accounts or warehouse assignments after a warehouse is resolved.
- Extended Storefront checkout request, idempotency snapshot/hash, and checkout validation with resolution ID, warehouse ID, and resolution version.
- Updated checkout final transaction to lock the submitted resolution, validate current stock/method/warehouse state, reserve by explicit warehouse ID, and stamp `Order.WarehouseId` plus every Storefront `OrderItem.WarehouseId`.
- Updated Storefront checkout state to store resolution metadata, call preview by session ID, refresh preview on cart authority changes, and submit resolution metadata.
- Added targeted certification/idempotency tests for resolution authority, preview integration, final checkout propagation, manual behavior, migration protections, and hash stability.

## Verification Performed

- `dotnet build PointOfSale.API/PointOfSale.API.csproj --no-restore`: pass.
- `dotnet build PointOfSale.sln --no-restore`: pass with existing warnings.
- `dotnet build PointOfSale.Tests/PointOfSale.Tests.csproj --no-restore`: pass with existing warnings.
- `dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --no-build --filter "FullyQualifiedName~CheckoutCartIdempotencySnapshotBuilderTests|FullyQualifiedName~ShopixyWarehouseResolutionAuthorityCertificationTests"`: 10 passed.
- `npm run build` in `PointOfSale.Storefront`: pass with existing Vite warnings.
- `npm run build` in `PointOfSale.Web`: pass.
- `dotnet ef migrations has-pending-model-changes --project PointOfSale.Infrastructure/PointOfSale.Infrastructure.csproj --startup-project PointOfSale.API/PointOfSale.API.csproj --no-build`: no pending model changes.
- Applied migration chain through `20260811142427_SHOPIXY_WarehouseResolutionAuthority` to scratch PostgreSQL database `shopixy_step2f_scratch_20260811173244`.
- Verified scratch PostgreSQL catalogs contain `WarehouseResolutions`, PK, check constraints, composite tenant FKs, active unique partial index, secondary indexes, and EF migration history entry.

## Verification Plan

- Run focused backend tests for the new resolution service, preview endpoint behavior, and checkout propagation where practical.
- Run `dotnet build PointOfSale.API/PointOfSale.API.csproj`.
- Run `dotnet build PointOfSale.sln`.
- Run Storefront and Web production builds if package state permits.
- Generate and apply migration to an isolated scratch PostgreSQL database if infrastructure is available.
- Run EF pending model change verification.
- Record all blocked verification explicitly in the final Step 2F implementation report.
