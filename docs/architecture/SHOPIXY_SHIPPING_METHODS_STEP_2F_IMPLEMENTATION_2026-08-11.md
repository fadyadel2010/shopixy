# SHOPIXY Step 2F Implementation - Warehouse Resolution Authority

Date: 2026-08-11

## 1. Executive Summary

Implemented a Storefront checkout warehouse authority centered on `WarehouseResolution`. The browser no longer selects or submits authoritative `warehouseId`; shipping preview and final checkout use server-owned resolution state. The implementation follows the Step 2E constraint: exactly one eligible warehouse resolves, zero eligible warehouses fails, and multiple eligible warehouses fails with no invented winner policy.

Certification: PASS

## 2. Step 2E Compliance Matrix

- Dedicated authority: `WarehouseResolution` added and EF mapped.
- No `CheckoutSession.WarehouseId` authority: not added.
- Exact-one policy: implemented in `WarehouseResolutionService`.
- Zero candidates: `NO_ELIGIBLE_WAREHOUSE`.
- Multiple candidates: `WAREHOUSE_SELECTION_POLICY_REQUIRED`.
- No silent fallback: final checkout revalidates resolved warehouse and fails on stale/insufficient stock.
- Preview contract: `POST /api/storefront/shipping-methods/preview` accepts `checkoutSessionId`.
- Manual fulfillment: allowed without carrier account/assignment after resolved warehouse.
- External carriers: require matching active/ready carrier warehouse assignment.
- Final checkout propagation: resolution warehouse -> order -> order items -> shipment origin via existing order warehouse flow.

## 3. Exact Files Changed

Step 2F files include:

- `PointOfSale.Core/Entities/WarehouseResolution.cs`
- `PointOfSale.Core/Enums/WarehouseResolutionEnums.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
- `PointOfSale.Infrastructure/Migrations/20260811142427_SHOPIXY_WarehouseResolutionAuthority.cs`
- `PointOfSale.Infrastructure/Migrations/20260811142427_SHOPIXY_WarehouseResolutionAuthority.Designer.cs`
- `PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- `PointOfSale.API/Services/Storefront/WarehouseResolutionService.cs`
- `PointOfSale.API/Controllers/StorefrontShippingMethodsController.cs`
- `PointOfSale.API/DTOs/Storefront/ShippingMethodDto.cs`
- `PointOfSale.API/Services/Storefront/ShippingMethodService.cs`
- `PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs`
- `PointOfSale.API/Services/Checkout/CheckoutService.cs`
- `PointOfSale.API/Services/Checkout/CheckoutCartIdempotencySnapshotBuilder.cs`
- `PointOfSale.API/Controllers/CheckoutController.cs`
- `PointOfSale.API/Program.cs`
- `PointOfSale.Storefront/src/store/checkoutStore.ts`
- `PointOfSale.Storefront/src/pages/CheckoutPage.tsx`
- `PointOfSale.Storefront/src/types/checkout.ts`
- `PointOfSale.Tests/Architecture/ShopixyWarehouseResolutionAuthorityCertificationTests.cs`
- `PointOfSale.Tests/CheckoutCartIdempotencySnapshotBuilderTests.cs`
- `PointOfSale.Tests/Security/StorefrontTenantIsolationTests.cs`
- `docs/architecture/SHOPIXY_SHIPPING_METHODS_STEP_2F_FORENSIC_IMPLEMENTATION_REPORT_2026-08-11.md`

Pre-existing dirty files outside this Step 2F scope were preserved.

## 4. Domain/Database Changes

`WarehouseResolution` stores tenant, session, cart, nullable warehouse until resolution, status/source enums, resolution version, algorithm code, deterministic input hash, optional safe snapshot JSON, failure/invalidation metadata, lock/order timestamps, and converted order.

EF adds:

- `DbSet<WarehouseResolution>`
- tenant query filter
- active partial unique index on `{TenantId, CheckoutSessionId}` for `Resolved` and `Locked`
- composite tenant FKs for checkout session, warehouse, and converted order
- check constraints for positive version and warehouse-required resolved states
- indexes for tenant/session/cart/warehouse/expiry/hash access

Cart is not an EF parent table in the inspected Storefront path, so cart tenant/owner protection is enforced in service validation against authoritative cart DTO/session data.

## 5. Warehouse Resolution Algorithm

Algorithm code: `STOREFRONT_SINGLE_WAREHOUSE_EXACT_ONE_V1`.

The resolver loads the owned checkout session, authoritative cart, stock-tracked non-backorder variants, active same-tenant warehouses, and grouped per-warehouse stock. Candidates must fulfill every required stock-tracked variant. Exactly one candidate resolves; zero or multiple candidates create failed rows and throw deterministic business errors.

## 6. Shipping Preview Contract

`POST /api/storefront/shipping-methods/preview`

Request: `{ checkoutSessionId }`

Response includes tenant evidence, `warehouseResolutionId`, `resolutionVersion`, `status`, `expiresAtUtc`, and eligible shipping methods. The legacy GET can remain, but Storefront checkout now uses preview.

## 7. Storefront Integration

Storefront creates/resumes checkout session, posts preview by session ID, stores resolution metadata, renders server-returned methods, refreshes on cart id/last-updated changes, and submits checkout with session ID, resolution ID, resolution version, and selected method ID. It does not submit `warehouseId`.

## 8. Final Checkout Propagation

Checkout validates and locks the submitted resolution inside the existing serializable order transaction. It stamps `Order.WarehouseId` and every Storefront `OrderItem.WarehouseId` from `WarehouseResolution.WarehouseId`.

## 9. Reservation Behavior

Storefront inventory reservation now passes explicit resolved warehouse ID to `ProductVariantStockRepository.ReserveAsync`. Null warehouse reservation is not used in the Storefront checkout path. Stock disappearance at the resolved warehouse fails with `INSUFFICIENT_STOCK_AT_RESOLVED_WAREHOUSE`.

## 10. Carrier/Manual Behavior

Manual fulfillment remains eligible for a resolved active warehouse without carrier account, carrier assignment, credentials, or external readiness. External carriers require matching same-tenant enabled assignment for the resolved warehouse and active/ready carrier account with canonical carrier code.

## 11. Security/Tenant Isolation

The service validates tenant, session ownership, cart ownership, resolution ownership, resolved warehouse tenant/active state, selected shipping method, and carrier assignment. Browser-supplied warehouse authority is not accepted. Logs avoid customer PII and use IDs/error codes.

## 12. Idempotency/Concurrency

`CheckoutCartIdempotencySnapshotBuilder` version advanced to include checkout session, warehouse resolution ID, resolved warehouse ID, and resolution version. Final checkout locks the resolution row with PostgreSQL `FOR UPDATE`; duplicate active resolution prevention is backed by a partial unique index.

## 13. Tests

Added/updated:

- `ShopixyWarehouseResolutionAuthorityCertificationTests`
- `CheckoutCartIdempotencySnapshotBuilderTests`
- constructor fixture update in `StorefrontTenantIsolationTests`

Targeted result: 10 tests passed.

## 14. Build Verification

- API project: pass.
- Solution build: pass with existing warnings.
- Test project build: pass with existing warnings.
- Storefront build: pass with existing Vite warnings.
- Web build: pass.

## 15. EF Migration Verification

Migration generated: `20260811142427_SHOPIXY_WarehouseResolutionAuthority`.

`dotnet ef migrations has-pending-model-changes`: no pending model changes.

## 16. Scratch DB Verification

Scratch database: `shopixy_step2f_scratch_20260811173244`.

The full migration chain applied through Step 2F. Catalog verification confirmed:

- `WarehouseResolutions` table exists.
- PK exists.
- Check constraints exist.
- Composite tenant FKs exist for checkout session, warehouse, and converted order.
- Active unique partial index exists.
- Secondary indexes exist.
- EF migration history contains `20260811142427_SHOPIXY_WarehouseResolutionAuthority`.

Initial scratch connection failed SSL handshake; retry with `SSL Mode=Disable` succeeded against the isolated scratch database.

## 17. Unrelated Failures

No unrelated failing tests were observed in the targeted run. Existing compiler/Vite warnings remain and were not modified.

## 18. Worktree Hygiene

Pre-work status showed many existing modified/untracked files from prior SHOPIXY shipping work. No commit, push, reset, clean, or unrelated checkout was performed. A temporary verifier under `.codex-temp` was removed after scratch verification.

## 19. Remaining Gaps

The broad behavioral concurrency matrix was represented by focused certification/idempotency tests rather than full end-to-end race fixtures. Cart same-tenant database FK is not available because the inspected Storefront cart authority is not an EF parent table; service-level cart/session ownership validation enforces that boundary.

## 20. Final Certification

PASS

The implementation establishes one Storefront warehouse authority:

`WarehouseResolution.WarehouseId -> shipping eligibility -> inventory reservation -> Order.WarehouseId -> OrderItem.WarehouseId -> Shipment.OriginWarehouseId`

No client warehouse authority, no arbitrary winner policy, and no silent fallback were introduced.
