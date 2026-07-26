# WAREHOUSE-SHIPPING-1 Warehouse Shipping Assignment

Date: 2026-07-09
Scope: Warehouse-to-Shipping assignment authority only
Decision: DEVELOPMENT CERTIFIED after one backend authority correction

## Executive Summary

Warehouse-to-shipping assignment authority was reviewed across Warehouse Shipping Assignment, Warehouse Shipping Policies, Warehouse Shipping APIs, Warehouse Shipping Administration, Shipping Method Assignment, Carrier Availability Filtering, and Storefront Shipping Eligibility.

One true authority inconsistency was found and fixed: storefront shipping-method reads could expose all active tenant shipping methods without consuming a selected warehouse assignment. The correction adds warehouse-scoped filtering to `ShippingMethodService` when `warehouseId` is supplied. Carrier-backed methods are now visible only when an active, ready carrier account has an enabled, origin-ready `CarrierWarehouseAssignment` for that warehouse. Missing warehouse, missing assignment, disabled assignment, missing origin readiness, inactive carrier account, or non-ready carrier account fails closed to no methods.

No shipping runtime, carrier runtime, checkout runtime, warehouse runtime, or inventory runtime was modified.

## Architecture

Current implemented authority model:

```text
Warehouse
  -> CarrierWarehouseAssignment
  -> Active/Ready CarrierAccount
  -> ShippingMethod.CarrierCode
  -> Storefront-visible shipping method eligibility
```

`ShippingMethod` does not select a warehouse. It remains tenant shipping configuration with carrier/service metadata. Warehouse assignment authority is consumed by the shipping-method read boundary when the selected warehouse is known.

## Assignment Model

Confirmed implemented relationship:

```text
Warehouse 1 -> * CarrierWarehouseAssignment * -> 1 CarrierAccount
CarrierAccount.CarrierCode -> ShippingMethod.CarrierCode
```

Assignment currently includes:

- Enabled: `CarrierWarehouseAssignment.IsEnabled`
- Origin readiness: `OriginReadinessPublished`
- Readiness evidence reference: `ReadinessEvidenceReference`
- Carrier account lifecycle: `CarrierAccount.State`, `OperationalState`
- Supported services: `CarrierServiceLevelEnablement`

Fields such as priority, origin address, default carrier, weight limits, country/region restrictions, and effective dates are not implemented as warehouse-method assignment fields in this phase.

## Administration

POS Admin Shipping Administration contains carrier account warehouse assignment controls and delegates mutations through `AdminShippingAdministrationController -> ShippingAdministrationApiService -> CarrierAccountAdministrationRuntime`.

Verified:

- Assign warehouse.
- Validate tenant-owned active warehouse.
- Enable assignment on create/update.
- Origin readiness evidence.
- Service-level enablement.
- Policies.
- No carrier-specific frontend decision logic.

Not implemented in the Warehouse Details page: a dedicated Shipping Assignment tab that directly assigns `ShippingMethod` rows to a warehouse.

## Storefront Flow

Certified backend flow when a warehouse is supplied:

```text
Checkout selected warehouse
  -> GET /api/storefront/shipping-methods?warehouseId={warehouseId}
  -> ShippingMethodService
  -> active tenant shipping methods
  -> warehouse carrier assignment filter
  -> calculated prices
  -> customer
```

Storefront must not display carrier-backed methods for a selected warehouse unless the warehouse assignment exists and is ready.

## Shipping Integration

Shipping Platform remains carrier-neutral and does not hardcode carrier eligibility. The corrected storefront method read consumes warehouse assignment data before presenting methods. Shipment creation still consumes the order-bound shipping method and does not accept caller carrier substitution.

## Marketplace Integration

Marketplace shipping remains backend-owned through current SQL shipping method/cost resolution. The warehouse assignment rule is compatible with merchant-specific warehouses because assignments are tenant and warehouse scoped.

## FedEx Integration

FedEx visibility is now warehouse-scoped on the corrected storefront method read:

- FedEx shipping method exists and is active.
- FedEx carrier account is active and ready.
- FedEx carrier warehouse assignment exists.
- Assignment is enabled.
- Origin readiness is published.

FedEx must not appear globally for a selected warehouse without this assignment evidence.

## Security Review

Verified:

- Tenant isolation on warehouse, assignment, carrier account, and shipping method reads.
- Assignment authorization remains in POS Admin Shipping Administration.
- Audit is append-only through `CarrierAdministrationAudit`.
- Storefront controller returns normalized `ApiErrorResponse` for tenant/session failures.
- Structured logs include tenant, warehouse, and correlation evidence.

## Failure Matrix

| Condition | Result |
|---|---|
| Warehouse disabled/missing | Fail closed: no warehouse-filtered methods |
| Carrier account disabled/retired/suspended/not ready | Fail closed for that carrier |
| Assignment missing | Fail closed |
| Assignment disabled | Fail closed |
| Origin readiness missing | Fail closed |
| Service unavailable | Carrier service-level gate remains provider/admin controlled |
| Warehouse mismatch | Fail closed through tenant and warehouse predicates |
| Tenant mismatch | Fail closed through tenant predicates and storefront tenant validation |
| FedEx not assigned to warehouse | FedEx method not displayed |

## Regression Coverage

Focused certification tests added/updated:

- `ShippingMethodServiceCurrencyConsistencyTests.GetShippingMethodsAsync_WithWarehouse_FiltersCarrierBackedMethodsByEnabledOriginReadyAssignment`
- `ShippingMethodServiceCurrencyConsistencyTests.GetShippingMethodsAsync_WithWarehouseAndNoAssignment_FailsClosed`
- `ShippingMethodEligibilityAuthorityCertificationTests.WarehouseSelectedStorefrontReads_MustConsumeWarehouseCarrierAssignments`

Command:

```powershell
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~ShippingMethodServiceCurrencyConsistencyTests|FullyQualifiedName~ShippingMethodEligibilityAuthorityCertificationTests" -nologo -v minimal
```

Result:

- Passed: 9
- Failed: 0
- Skipped: 0

## Certification Decision

Warehouse Shipping Assignment Certified: Yes, for the implemented backend carrier-backed assignment boundary.

Administration Certified: Yes, for POS Admin Shipping Administration carrier warehouse assignment.

Storefront Filtering Certified: Yes, when selected warehouse evidence is supplied to the storefront shipping-method read.

Shipping Platform Integration Certified: Yes.

Marketplace Integration Certified: Yes, compatible with tenant/warehouse-scoped carrier exposure.

FedEx Integration Certified: Yes, FedEx is not globally exposed for selected-warehouse reads.

API Certified: Yes. Controllers remain thin and delegate to services/runtime.

Authority preserved: Yes.

Backward compatible: Yes. Legacy reads without `warehouseId` preserve tenant-wide active method behavior.

Production-certifiable: Development certifiable; operational rollout still requires real warehouse assignment data, carrier readiness, mapping/origin evidence, monitoring, and smoke tests.

Recommended next phase: implement a dedicated Warehouse Details shipping assignment administration tab if direct warehouse-to-shipping-method UX is required beyond the current carrier-account assignment workflow.
