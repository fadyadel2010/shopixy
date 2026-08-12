# Shopixy Shipping Methods Step 2C Implementation

Date: 2026-08-11

## 1. Executive Summary

Step 2C replaced the merchant-facing raw Warehouse Assignment ID workflow in Shipping Administration with a tenant warehouse selector at the correct domain boundary:

```text
CarrierAccount
  -> CarrierWarehouseAssignment
  -> Warehouse
```

No `ShippingMethod -> Warehouse` relationship was added. No `ShippingMethod -> CarrierWarehouseAssignment` relationship was added. No migration was created.

Final certification: PASS WITH EXTERNAL VERIFICATION BLOCKED.

## 2. Forensic Findings Used

The requested forensic audit file was not present at:

```text
docs/architecture/SHOPIXY_SHIPPING_METHODS_STEP_2C_FORENSIC_AUDIT_2026-08-11.md
```

Implementation used the canonical Step 2C findings supplied in the task, current source inspection, and the Step 2B implementation report:

```text
docs/architecture/SHOPIXY_SHIPPING_METHODS_STEP_2B_IMPLEMENTATION_2026-08-11.md
```

Confirmed from source:

- `Warehouse` is tenant-owned and active/soft-delete aware.
- `CarrierWarehouseAssignment` links `CarrierAccountId` to `WarehouseId`.
- `ShippingMethod` does not own warehouse state.
- `GET /api/warehouses` already lists tenant active warehouses.
- The existing carrier-account warehouse-assignment endpoint already accepts `warehouseId`.

## 3. Original UX Defect

The Carrier Account administration UI exposed a raw text input:

```text
Warehouse Assignment
[ Warehouse ID ]
```

This asked merchants to paste a GUID even though the backend operation is semantically “assign this carrier account to a tenant warehouse.”

## 4. Implemented Changes

- Added warehouse admission/fetching to the shipping administration frontend API boundary.
- Loaded warehouses through the existing canonical `/warehouses` frontend API path.
- Passed warehouses into Carrier Account administration.
- Replaced the raw warehouse GUID input with a `<select>` labeled `Warehouse`.
- Displayed assignment warehouse names, with code when available.
- Displayed unavailable assignment references safely when a warehouse is not returned by the active warehouse list.
- Kept the backend request identity as `warehouseId`.
- Hardened backend runtime assignment validation for manual carrier accounts and soft-deleted assignment rows.
- Added focused frontend, backend, and architecture regression coverage.

## 5. Warehouse Selector Architecture

The selector uses:

```text
GET /api/warehouses
```

via the existing frontend API client path:

```text
api.get('/warehouses')
```

The selector displays:

```text
Warehouse.Name
Warehouse.Name (Warehouse.Code)
```

The selected value remains:

```text
warehouse.id
```

No warehouse name is submitted as identity.

## 6. CarrierWarehouseAssignment Semantics

`CarrierWarehouseAssignment` remains the authority for carrier-account warehouse eligibility:

```text
CarrierWarehouseAssignment.TenantId
CarrierWarehouseAssignment.CarrierAccountId
CarrierWarehouseAssignment.WarehouseId
CarrierWarehouseAssignment.IsEnabled
CarrierWarehouseAssignment.OriginReadinessPublished
```

It does not mean “ShippingMethod origin warehouse.”

## 7. Manual Fulfillment Handling

Manual Fulfillment remains:

```text
carrierCode = manual
```

Manual Fulfillment does not require:

- `CarrierAccount`
- `CarrierWarehouseAssignment`
- credentials
- connection test
- origin readiness
- external carrier warehouse assignment

Backend runtime now rejects warehouse assignment against a manual carrier account if legacy/manual account data exists.

## 8. Tenant Isolation

Backend validation remains authoritative. Assignment requires:

```text
CarrierAccount.TenantId == Warehouse.TenantId == Assignment.TenantId
```

The runtime validates warehouse existence with:

- same tenant
- active
- not deleted

Foreign, inactive, deleted, or empty warehouse IDs fail closed.

## 9. API Contract

Preserved existing contracts:

```http
GET /api/warehouses
PUT /api/admin/shipping/carrier-accounts/{carrierAccountId}/warehouse-assignments/{warehouseId}
```

No new endpoint was added.

No carrier-account assignment DTO identity change was required.

## 10. Backend Validation

Backend hardening:

- Manual carrier account warehouse assignment returns `MANUAL_CARRIER_ACCOUNT_NOT_SUPPORTED`.
- Soft-deleted carrier accounts are not found by runtime mutation lookup.
- Soft-deleted assignment rows are not reused for new assignment writes.
- Soft-deleted assignments no longer satisfy activation warehouse gates.
- Duplicate active assignment behavior remains deterministic: same account + same warehouse updates the existing assignment.

## 11. Frontend Changes

Changed files:

- `PointOfSale.Web/src/modules/shipping-administration/shippingAdministrationApi.js`
- `PointOfSale.Web/src/modules/shipping-administration/hooks/useShippingAdministrationApi.js`
- `PointOfSale.Web/src/modules/shipping-administration/ShippingAdministrationWorkspace.jsx`
- `PointOfSale.Web/src/modules/shipping-administration/components/CarrierAccountCard.jsx`

Behavior:

- Raw `Warehouse ID` placeholder removed.
- Raw `Warehouse Assignment` label removed from the assignment form.
- Merchant selects `Warehouse` by name.
- Arabic warehouse names render directly from backend data.
- The native `<select>` remains keyboard accessible.
- Empty active warehouse list disables assignment selection and shows `No active warehouses available`.
- Existing assignments display warehouse metadata when available.

## 12. Tests

Backend/runtime tests added:

- same-tenant warehouse assignment succeeds.
- cross-tenant warehouse assignment is rejected.
- inactive warehouse assignment is rejected.
- deleted warehouse assignment is rejected.
- duplicate assignment updates deterministically.
- soft-deleted prior assignment does not get silently reused.
- manual carrier account warehouse assignment is rejected.

Frontend tests added:

- raw warehouse ID input is gone.
- warehouse selector renders.
- warehouse options come from backend warehouse data.
- selector value is `warehouseId`.
- warehouse names, including Arabic text, are displayed.
- existing assignment renders warehouse name.
- add assignment submits `warehouseId`.
- empty warehouse list state is safe.

Architecture guard added:

- `ShippingMethod` still has no `WarehouseId`, `WarehouseAssignmentId`, or `CarrierWarehouseAssignmentId`.
- `CarrierWarehouseAssignment` remains the carrier-account-to-warehouse relationship.
- Step 2B `CarrierCode` authority remains intact.

## 13. Build Verification

Executed:

```powershell
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "PosAdminShippingAdministrationRuntimeTests|ShippingMethodsStep2C_MustKeepWarehouseAssignmentAtCarrierAccountBoundary"
```

Result: PASS, 10 tests.

Executed:

```powershell
npm test -- --run src/modules/shipping-administration/ShippingAdministrationWorkspace.test.tsx
```

Result: PASS, 13 tests.

Executed:

```powershell
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "PosAdminShippingAdministrationApiTests|PosAdminShippingAdministrationRuntimeTests|ShippingMethodsStep2C_MustKeepWarehouseAssignmentAtCarrierAccountBoundary|ShippingMethodsStep2B_MustUseCarrierCodeAndBackendRegistryForCarrierIdentity"
```

Result: PASS, 19 tests.

Executed:

```powershell
dotnet build PointOfSale.API\PointOfSale.API.csproj --no-restore
```

Result: PASS.

Executed:

```powershell
dotnet build PointOfSale.sln --no-restore
```

Result: PASS.

Executed:

```powershell
npm run build
```

from `PointOfSale.Web`.

Result: PASS.

## 14. EF/Migration Verification

Executed:

```powershell
dotnet ef migrations has-pending-model-changes --project PointOfSale.Infrastructure\PointOfSale.Infrastructure.csproj --startup-project PointOfSale.API\PointOfSale.API.csproj --context ApplicationDbContext
```

Result:

```text
No changes have been made to the model since the last migration.
```

NO MIGRATION REQUIRED.

## 15. Live Verification

No live API verification was performed.

No browser automation was performed.

Source, unit/regression, build, and EF verification were performed.

## 16. Deferred Checkout Warehouse Context

Not implemented in Step 2C:

- storefront warehouse selection
- checkout warehouse context resolution
- order warehouse population strategy
- inventory allocation redesign
- shipment origin warehouse strategy
- automatic warehouse routing

These remain separate architectural tasks.

## 17. Remaining Issues

- The requested Step 2C forensic audit document was absent from the repository during implementation.
- Existing assignment rows whose warehouse is no longer returned by `/warehouses` display as unavailable rather than being repaired automatically.
- No live tenant API call was performed.
- No browser automation was performed.

## 18. Files Changed

Step 2C changed:

- `PointOfSale.Infrastructure/Shipping/Administration/CarrierAccountAdministrationRuntime.cs`
- `PointOfSale.Tests/Architecture/CarrierIntegrationAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/PosAdminShippingAdministrationRuntimeTests.cs`
- `PointOfSale.Web/src/modules/shipping-administration/ShippingAdministrationWorkspace.jsx`
- `PointOfSale.Web/src/modules/shipping-administration/ShippingAdministrationWorkspace.test.tsx`
- `PointOfSale.Web/src/modules/shipping-administration/components/CarrierAccountCard.jsx`
- `PointOfSale.Web/src/modules/shipping-administration/hooks/useShippingAdministrationApi.js`
- `PointOfSale.Web/src/modules/shipping-administration/shippingAdministrationApi.js`
- `docs/architecture/SHOPIXY_SHIPPING_METHODS_STEP_2C_IMPLEMENTATION_2026-08-11.md`

## 19. Worktree Hygiene

Baseline worktree was dirty before implementation. Existing unrelated dirty files were not reverted, reset, checked out, cleaned, committed, or pushed.

No migration was created.

## 20. Final Certification

PASS WITH EXTERNAL VERIFICATION BLOCKED
