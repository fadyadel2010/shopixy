# POSADMIN-SHIPPING-1 - Administration Runtime

Date: 2026-07-07  
Scope: POS Admin Shipping Administration backend runtime only  
Status: Implemented

## Executive Summary

POSADMIN-SHIPPING-1 implements the backend Administration Runtime for POS Admin
Shipping. It adds tenant carrier account lifecycle, credential-reference
lifecycle, environment selection, warehouse assignment, domestic/international
policy, service-level enablement, connection-test orchestration, activation
gates, health evaluation, and append-only administrative audit.

No frontend, controllers, API endpoints, HTTP provider transport, Shipment
Runtime mutation, carrier adapter mutation, Geography redesign, Customs
redesign, Warehouse redesign, Checkout, Tracking, Labels, or Orders work was
implemented.

## Carrier Account Runtime

Carrier Account is a tenant-scoped administration aggregate. It owns carrier
code, tenant, selected environment, active credential reference, warehouse
assignments, policies, service levels, activation state, operational state,
health state, connection-test records, and administration audit.

Carrier Account does not own shipment, tracking, labels, orders, checkout,
carrier mapping, geography, customs, warehouse lifecycle, provider contracts, or
SQL Shipment truth.

Runtime files:

- `PointOfSale.Core/Entities/ShippingAdministration/CarrierAccountRuntimeEntities.cs`
- `PointOfSale.Core/Interfaces/ShippingAdministration/CarrierAccountRuntimeContracts.cs`
- `PointOfSale.Infrastructure/Shipping/Administration/CarrierAccountAdministrationRuntime.cs`

## State Machine

Implemented states:

```text
Draft
  -> CredentialsPending
  -> ConnectionTestPending
  -> ReadyForActivation
  -> Active
  -> Suspended
  -> Disabled
  -> Retired
```

Supported administrative branches:

- `Active -> Suspended`
- `Suspended -> Active`
- `Active/Suspended/ReadyForActivation -> Disabled`
- `Disabled -> Retired`

Unknown states and illegal transitions fail closed with
`UNKNOWN_STATE_TRANSITION` and append audit evidence.

## Activation Gates

Activation fails closed when any required gate is absent:

- credential reference missing or inactive;
- environment missing;
- successful connection test missing;
- active warehouse assignment missing;
- warehouse-origin readiness evidence missing;
- carrier mapping publication evidence missing or unknown;
- customs readiness missing for enabled international policy;
- provider capability unsupported or unknown;
- enabled service level not supported by the certified adapter boundary.

The runtime consumes readiness evidence; it does not publish carrier mappings,
admit customs, repair origins, or certify provider capability.

## Health Model

Health states are:

- Healthy
- Warning
- Blocked
- Disabled
- Retired

Health derives only from configuration, readiness evidence, credential status,
connection-test records, warehouse assignment, policy, service levels, and
published evidence flags. It never reads Shipment Runtime status, tracking,
labels, carrier responses, or order state as health authority.

## Audit

Shipping Administration audit is append-only and records:

- registration;
- credential reference update and rotation;
- environment change;
- activation;
- suspension;
- disable;
- retirement;
- warehouse assignment;
- policy changes;
- connection tests;
- service-level changes;
- health changes;
- rejected transitions;
- rejected activation gates.

Audit stores actor, tenant, carrier account, action, previous state, next state,
correlation ID, timestamp, and non-secret reason. It stores no plaintext
credentials. EF guards reject audit update and delete.

## Credential Runtime

The runtime stores credential references, masked fingerprints, status, and
expiry metadata. It does not store plaintext secrets in Carrier Account or audit.

Connection-test orchestration invokes only the existing certified adapter
validation boundary: `IShippingCarrierAdapter.ValidateCredentialsAsync`.
Successful validation proves only credential reachability and configuration
validity. It does not prove shipment success, production approval, provider
entitlement, mapping coverage, customs coverage, origin readiness, or
operational readiness.

## Warehouse Assignment Runtime

Carrier Account may assign active tenant warehouses to a carrier account.
Warehouse remains the lifecycle owner. Origin readiness remains published
evidence from SHIP-ORIGIN-1 and Geographic admission; the administration runtime
only consumes the readiness flag/evidence reference as an activation gate.

Unknown, inactive, deleted, or cross-tenant warehouses fail closed with
`UNKNOWN_WAREHOUSE`.

## Policy Runtime

The runtime implements domestic and international policy records. Unknown
policy scope or mode fails closed with `UNKNOWN_POLICY`.

International enablement requires customs readiness evidence before activation.
The runtime does not approve Customs Evidence, publish Customs Evidence, create
Shipment Customs Snapshots, or map provider-specific customs contracts.

## Authority Matrix

| Runtime concern | POS Admin Shipping Runtime owns | Explicitly does not own |
|---|---|---|
| Carrier account | Tenant account lifecycle and activation state | Shipment identity or carrier request identity |
| Credentials | Secret reference selection, rotation metadata, connection-test evidence | Plaintext secrets or vault internals |
| Environment | Sandbox/Production selection | Provider base URL constants or HTTP transport |
| Warehouse assignment | Carrier-to-warehouse eligibility | Warehouse lifecycle or address admission |
| Domestic policy | Tenant carrier eligibility policy | Checkout/order pricing authority |
| International policy | Eligibility and customs-readiness gate | Customs Evidence governance or snapshots |
| Service levels | Tenant allowlist after adapter capability check | Inventing provider services |
| Activation gates | Administrative eligibility evaluation | Geography, mapping, customs, shipment, provider truth |
| Health | Admin health derived from configuration/readiness | Shipment Runtime health or tracking truth |
| Audit | Append-only admin audit | Shipment audit or provider payload truth |

## Breaking Issues

No certified platform redesign was introduced.

Known production blockers remain operational/data blockers inherited from the
frozen platforms:

- external-provider mapping coverage is not production-certifiable per GEO-10;
- warehouse-origin coverage must be admitted and published per SHIP-ORIGIN-1;
- customs coverage is required before cross-border activation;
- provider credentials and account entitlement require operational proof;
- FedEx remains development-certified operational pending and has no provider
  HTTP/OAuth execution.

## Regression Coverage

Added `PointOfSale.Tests/Architecture/PosAdminShippingAdministrationRuntimeTests.cs`.

Coverage freezes:

- Carrier Account lifecycle and illegal transition rejection;
- activation gates and fail-closed unknown readiness;
- credential reference and connection-test orchestration;
- international policy customs readiness gate;
- health transition to Healthy only after gates pass;
- append-only/no-secret audit evidence;
- no Shipment Runtime mutation and no adapter mutation.

## Verification

Implemented backend files only:

- Core entities and contracts;
- Infrastructure administration runtime service;
- EF DbSets/configuration and append-only audit guard;
- DI registration;
- focused runtime tests;
- this architecture report.

No controllers or API endpoints were added. No frontend files were modified. No
Shipment Runtime or carrier adapter files were modified.

## Final Recommendation

Approve POSADMIN-SHIPPING-1 as the backend Administration Runtime foundation.

Recommended next phase: POS Admin Shipping API surface and operational rollout
for this runtime, limited to backend DTOs/endpoints and role assignment. Do not
implement frontend, HTTP provider transport, Shipment Runtime changes, carrier
adapter execution, Geography redesign, Customs redesign, Checkout, Tracking, or
Labels in that phase.

## Explicit Success Answers

- Reviewed ONLY Administration Runtime? **Yes.**
- Runtime modified? **Yes.**
- Backend modified? **Yes.**
- Frontend modified? **No.**
- Carrier Account Runtime implemented? **Yes.**
- State machine implemented? **Yes.**
- Activation gates implemented? **Yes.**
- Health model implemented? **Yes.**
- Audit implemented? **Yes.**
- Shipment Runtime modified? **No.**
- Carrier Adapters modified? **No.**
- Architecture preserved? **Yes.**
- Backward compatible? **Yes; additive backend runtime only.**
- Production-certifiable? **Yes as an administration runtime foundation; production activation remains conditional on mapping, origin, customs, credential, and provider operational evidence.**
- Recommend next phase: **POS Admin Shipping API surface and operational rollout.**
