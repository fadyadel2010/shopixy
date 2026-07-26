# POSADMIN-SHIPPING-2 - Administration APIs

Date: 2026-07-07  
Scope: POS Admin Shipping Administration API surface only  
Status: Implemented

## Executive Summary

POSADMIN-SHIPPING-2 exposes the certified Shipping Administration Runtime
through POS Admin authenticated REST APIs. The implementation adds only backend
API contracts, a controller, an API application service, validation, role
authorization, tenant-scoped queries, and focused regression tests.

No frontend, Shipment Runtime, tracking, labels, checkout, warehouse lifecycle,
Carrier Mapping editing, Geography, Customs, OAuth execution, HTTP provider
transport, or carrier adapter implementation was added or modified.

## API Surface

All endpoints are under `api/admin/shipping` and require
`Owner,Admin,Manager`.

The surface exposes:

- supported carrier list;
- carrier account list/get/create/update;
- activate, suspend, disable, and retire commands;
- connection test command through the existing runtime validation boundary;
- credential reference command and masked credential-reference read model;
- environment selection;
- warehouse assignment;
- domestic and international policy settings;
- service-level enablement;
- health read/evaluate;
- append-only administration audit reads.

## Endpoint Matrix

| Endpoint | Method | Purpose | Mutates audit |
|---|---:|---|---:|
| `/api/admin/shipping/supported-carriers` | GET | List certified registered carriers and service levels | No |
| `/api/admin/shipping/carrier-accounts` | GET | List tenant carrier accounts | No |
| `/api/admin/shipping/carrier-accounts/{id}` | GET | Get tenant carrier account details | No |
| `/api/admin/shipping/carrier-accounts` | POST | Create carrier account | Yes |
| `/api/admin/shipping/carrier-accounts/{id}` | PUT | Update account-level settings currently limited to environment | Yes when changed |
| `/api/admin/shipping/carrier-accounts/{id}/credential-reference` | PUT | Select active secret reference metadata | Yes |
| `/api/admin/shipping/carrier-accounts/{id}/environment` | PUT | Select Sandbox or Production | Yes |
| `/api/admin/shipping/carrier-accounts/{id}/warehouse-assignments/{warehouseId}` | PUT | Assign warehouse readiness evidence | Yes |
| `/api/admin/shipping/carrier-accounts/{id}/policies/domestic` | PUT | Set domestic policy | Yes |
| `/api/admin/shipping/carrier-accounts/{id}/policies/international` | PUT | Set international policy and customs readiness evidence | Yes |
| `/api/admin/shipping/carrier-accounts/{id}/service-levels/{code}` | PUT | Enable or disable service level | Yes |
| `/api/admin/shipping/carrier-accounts/{id}/connection-tests` | POST | Run runtime connection validation evidence | Yes |
| `/api/admin/shipping/carrier-accounts/{id}/activate` | POST | Activate after fail-closed readiness gates | Yes |
| `/api/admin/shipping/carrier-accounts/{id}/suspend` | POST | Suspend active account | Yes |
| `/api/admin/shipping/carrier-accounts/{id}/disable` | POST | Disable eligible account | Yes |
| `/api/admin/shipping/carrier-accounts/{id}/retire` | POST | Retire disabled account | Yes |
| `/api/admin/shipping/carrier-accounts/{id}/health` | GET | Read health state | No |
| `/api/admin/shipping/carrier-accounts/{id}/health/evaluate` | POST | Evaluate runtime health from readiness evidence | Yes when runtime health changes |
| `/api/admin/shipping/audit` | GET | Read append-only admin audit | No |

## Commands

Implemented API command DTOs:

- `CreateCarrierAccountCommand`
- `UpdateCarrierAccountCommand`
- `CredentialReferenceCommand`
- `EnvironmentCommand`
- `WarehouseAssignmentCommand`
- `PolicyCommand`
- `ServiceLevelCommand`
- `ConnectionTestCommand`
- `ReadinessEvidenceCommand`

Commands are translated to the frozen `ICarrierAccountAdministrationRuntime`.
They do not call Shipment services, Tracking services, Label services, Order
services, Checkout services, carrier shipment services, or Carrier Mapping
mutation services.

## Queries

Implemented query responses:

- `SupportedShippingCarrierDto`
- `CarrierAccountAdministrationDto`
- `CarrierCredentialReferenceDto`
- `CarrierWarehouseAssignmentDto`
- `CarrierPolicyDto`
- `ShippingAdministrationServiceLevelDto`
- `CarrierConnectionTestDto`
- `CarrierAdministrationAuditDto`
- `ShippingAdministrationResultDto`

Read queries are tenant-scoped and no-store. They do not append audit.

## DTO Contracts

Administration DTOs expose only administration state and masked metadata.

DTOs do not expose:

- plaintext secrets;
- secret reference values;
- provider IDs;
- shipment snapshots;
- carrier request identity;
- tracking state;
- labels;
- Carrier Mapping internals;
- Customs evidence internals;
- runtime shipment internals.

Credential reference reads expose only ID, masked fingerprint, state, and
expiry metadata.

## Authorization

The controller uses:

```csharp
[Route("api/admin/shipping")]
[Authorize(Roles = "Owner,Admin,Manager")]
```

This admits POS Admin users only. Super Admin, Storefront customer JWT,
Affiliate JWT, and CommerceHub customer routes are not used. Tenant scope comes
from trusted `ITenantContext.RequiredTenantId`.

## Validation

The API and runtime reject:

- unknown carrier;
- unknown carrier account;
- unknown environment;
- unknown warehouse;
- unknown policy scope or mode;
- unknown service level;
- unknown credential reference;
- illegal state transition;
- missing activation gates.

Activation fails closed when readiness evidence is unknown or false for carrier
mapping or provider capability, and when runtime reports missing credentials,
environment, successful connection test, warehouse assignment, origin readiness,
customs readiness for enabled international policy, or supported service-level
capability.

## Audit

Every mutation is dispatched through the Administration Runtime, which appends
administration audit. Read endpoints query only and do not mutate audit.

Audit remains administration-only and stores no plaintext secrets.

## Authority Matrix

| API concern | May mutate | Must not mutate |
|---|---|---|
| Carrier Account | Administration Runtime tables | Shipment, Orders, Checkout |
| Credential Reference | Credential-reference metadata only | Plaintext secret exposure or secret-vault internals |
| Environment | Carrier account environment | Provider base URL constants or HTTP transport |
| Warehouse Assignment | Carrier-to-warehouse assignment evidence | Warehouse lifecycle or address admission |
| Domestic Policy | Admin carrier policy | Checkout pricing or shipping calculation truth |
| International Policy | Admin policy and customs readiness flag | Customs Evidence governance or snapshots |
| Service Levels | Admin allowlist after adapter capability check | Provider service invention |
| Connection Test | Runtime validation evidence | Shipment creation or provider transport implementation |
| Health | Admin health/readiness record | Tracking, labels, shipment status, provider truth |
| Audit | Administration audit | Shipment audit or provider payload truth |

## Regression Coverage

Added `PointOfSale.Tests/Architecture/PosAdminShippingAdministrationApiTests.cs`.

Coverage freezes:

- POS Admin route and role authorization;
- Super Admin exclusion by route/policy shape;
- DTO contract secret/provider/runtime exclusion;
- command dispatch through Administration Runtime;
- read query no-audit behavior;
- append-only mutation audit behavior;
- validation for unknown environment, warehouse, policy, service level, and
  credential reference;
- activation fail-closed on unknown readiness;
- no Shipment Runtime or carrier adapter API mutation.

## Verification

Implemented backend files only:

- `PointOfSale.API/Controllers/AdminShippingAdministrationController.cs`
- `PointOfSale.API/DTOs/ShippingAdministration/ShippingAdministrationDtos.cs`
- `PointOfSale.API/Services/Admin/IShippingAdministrationApiService.cs`
- `PointOfSale.API/Services/Admin/ShippingAdministrationApiService.cs`
- `PointOfSale.API/Program.cs` DI registration
- `PointOfSale.Tests/Architecture/PosAdminShippingAdministrationApiTests.cs`
- this report

No `.ai-context` update was required because API implementation discovered no
new permanent cross-platform architecture truth.

## Final Recommendation

Approve POSADMIN-SHIPPING-2 as the backend Administration API surface for POS
Admin Shipping.

Recommended next phase: operational API rollout and authorization assignment
verification with real tenant roles and readiness data. Frontend, provider HTTP
transport, Shipment Runtime changes, Carrier Mapping editing, Geography,
Customs, Checkout, Tracking, and Labels remain separate phases.

## Explicit Success Answers

- Reviewed ONLY Administration APIs? **Yes.**
- Runtime modified? **No.**
- Backend modified? **Yes.**
- Frontend modified? **No.**
- Controllers implemented? **Yes.**
- Commands implemented? **Yes.**
- Queries implemented? **Yes.**
- Authorization implemented? **Yes.**
- Validation implemented? **Yes.**
- Audit integrated? **Yes.**
- Shipment Runtime modified? **No.**
- Carrier Adapters modified? **No.**
- Architecture preserved? **Yes.**
- Backward compatible? **Yes; additive POS Admin API surface.**
- Production-certifiable? **Yes, as an administration API surface. Production carrier activation remains conditional on credential, mapping, origin, customs, and provider operational evidence.**
- Recommend next phase: **Operational API rollout and role/readiness-data verification.**
