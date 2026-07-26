# DHL.FINAL - End-to-End Development Certification Freeze

Date: 2026-07-07
Scope: DHL Development Certification only
Final development verdict: **CERTIFIED**

## Executive Summary

The DHL integration is development-complete under the approved platform
architecture.

This freeze reviewed DHL only across official contract compatibility, runtime
authority, geography, customs, origin, carrier resolution, tracking, labels,
credential validation, serialization, recovery, fail-closed behavior, and
focused regression coverage. It did not review or certify sandbox replay,
production rollout, POS Admin, UI, Warehouse redesign, Checkout redesign,
Geography redesign, Customs redesign, or Shipment redesign.

No true DHL development blocker was found. No runtime hardening was required in
this final pass. Operational evidence remains missing, but under the
certification rule it does not downgrade Development Certification.

Final verdicts:

| Verdict | Result |
|---|---|
| Development Certification | **CERTIFIED** |
| Operational Readiness | **READY AFTER OPERATIONAL CHECKLIST** |
| Production Rollout | **BLOCKED BY OPERATIONAL EVIDENCE** |

## Architecture Summary

DHL is a carrier wire adapter behind backend Shipment and Carrier Integration
authority. It serializes admitted platform evidence to MyDHL REST requests and
deserializes provider evidence back to carrier-neutral results.

The certified chain is:

```text
Order / Shipment SQL authority
  -> Shipment Origin Snapshot
  -> Shipment Destination Snapshot
  -> Carrier Mapping Snapshot
  -> Shipment Customs Snapshot when cross-border
  -> Carrier-neutral DTOs
  -> DHL request serialization
  -> MyDHL transport
  -> Backend carrier response admission
  -> ShipmentLifecycleTransitionPolicy
  -> SQL persistence
```

DHL owns none of Shipment, Order, Checkout, Inventory, Warehouse, Product,
Customer, Geography, Origin, Destination, Carrier Mapping, Customs, Shipment
Lifecycle, Financial State, or SQL Truth.

## Runtime Summary

DHL owns only:

- MyDHL Basic authentication header construction.
- MyDHL sandbox and production base URL selection.
- MyDHL rates, shipment creation, and tracking route serialization.
- JSON request serialization and response deserialization.
- HTTP transport through the named DHL client.
- Bounded timeout and retry behavior.
- Official contract parsing for tracking, labels, provider identity, status
  evidence, and customs line-item serialization.

Runtime certification points:

- Named DHL `HttpClient` timeout is 15 seconds.
- Safe GET-like transport participates in shared bounded retry behavior; create
  is not blindly retried as provider idempotency is not certified.
- Shipment cancellation remains unsupported and fails closed.
- Pickup scheduling and webhooks are not advertised as production capabilities.
- Unknown DHL tracking and webhook statuses fail closed.
- Provider responses remain evidence until admitted by backend carrier result
  guards.
- Labels are admitted as provider evidence and remain subject to backend label
  validation.

## Authority Summary

DHL is pure adapter code. It does not query SQL, Geographic services, Customs
admission services, Carrier Mapping services, Product services, Warehouse
services, Checkout state, Inventory, or Shipment Lifecycle policy.

Backend services own:

- carrier selection and tenant carrier configuration;
- credential decryption and enabled/inactive carrier gating;
- shipment identity and idempotency;
- origin, destination, mapping, and customs snapshot acquisition;
- carrier response admission;
- lifecycle transition validation;
- SQL persistence and recovery.

## Geography Summary

DHL consumes Canonical Geography only through the platform-created shipment
evidence supplied before adapter invocation.

Verified consumption:

- origin country, locality, street, and contact come from Shipment Origin
  Snapshot evidence;
- destination street, locality, administrative area display, postal evidence,
  and country come from shipment destination and carrier geographic resolution;
- carrier mapping is resolved and snapshotted before adapter I/O;
- DHL does not query catalogs, aliases, hierarchy, SQL mapping tables, or
  Geographic services;
- DHL does not serialize provider geographic identifiers for its certified
  MyDHL request shape.

GEO-10 mapping publication remains an operational dependency, not a DHL
development blocker.

## Customs Summary

DHL consumes Customs only through `CarrierNeutralCustomsDto` generated from the
Shipment Customs Snapshot.

Verified behavior:

- domestic DHL shipment creation emits no customs declaration;
- cross-border DHL creation requires a complete admitted customs DTO;
- missing, unsupported, incomplete, wrong-version, or unsafe customs evidence
  fails closed before provider I/O;
- DHL serializes admitted customs evidence into MyDHL
  `content.exportDeclaration.lineItems`;
- DHL does not invent HS code, commodity, country of origin, manufacture
  country, declared value, currency, quantity, net weight, gross weight, units,
  commercial invoice evidence, or export evidence.

Optional DHL customs requirements not modeled by the canonical DTO remain
operational or future Customs-domain scope. They are omitted rather than
guessed.

## Shipment Origin Summary

SHIP-ORIGIN-1 is consumed correctly. Carrier configuration pickup text is not
DHL origin authority.

Carrier-managed DHL registration requires:

- order-bound tenant warehouse;
- active admitted warehouse address evidence;
- active canonical country profile and place evidence;
- immutable Shipment Origin Snapshot;
- snapshot reuse during recovery.

Missing or incomplete origin evidence intentionally fails closed before DHL
network I/O. Rollout coverage for all shipping warehouses is operational.

## Carrier Resolution

DHL is registered as carrier code `dhl` and receives a carrier-neutral create
request only after `CarrierShipmentService` resolves the registered adapter,
tenant carrier configuration, origin snapshot, destination snapshot, carrier
mapping snapshot, and customs snapshot where required.

Provider mapping evidence is Shipment-owned forensic evidence. DHL performs no
mapping resolution and no provider geographic lookup.

## Tracking

DHL tracking is polling-based for the certified runtime boundary.

Verified:

- tracking uses a published MyDHL tracking route;
- tracking response identity must match the requested tracking number;
- known DHL statuses map to canonical shipment status evidence only;
- `OK` maps to `Delivered`;
- unknown polling statuses fail closed with unadmitted tracking evidence;
- unknown webhook statuses also fail closed if the generic webhook parser is
  invoked.

Lifecycle mutation remains backend-owned and must pass
`ShipmentLifecycleTransitionPolicy`.

## Labels

DHL label evidence is returned by shipment creation as embedded document
content. The adapter returns label data and format as provider evidence only.
Backend carrier response admission remains responsible for rejecting malformed
label data, unsupported label formats, malformed label URLs, and invalid
provider metadata before SQL persistence.

## Credential Validation

DHL requires MyDHL API key, API secret, and account ID before calls.

Credential validation is intentionally structural in repository evidence. The
former hard-coded geographic `address-validate` probe was removed in DHL-3 and
must not return. Live credential validity, entitlement, account-enabled
countries, account products, limits, and production access require operational
evidence.

## Serialization

DHL serialization is compatible with the certified MyDHL REST 3.3.1 subset:

- Basic authentication;
- fixed sandbox and production MyDHL base URLs;
- `POST /rates`;
- `POST /shipments`;
- tracking polling;
- embedded PDF label evidence;
- domestic non-customs shipment payloads;
- international customs payloads from admitted carrier-neutral DTOs.

Serialization fails closed on missing credentials, invalid shipment request
shape, missing customs DTO, incomplete customs snapshot, unsupported customs
units/enums, unknown statuses, malformed response identity, and unsupported
shipment cancellation.

## Recovery

Recovery is snapshot-only.

DHL recovery reuses:

- shipment identity;
- persisted carrier request identity;
- Shipment Origin Snapshot;
- Shipment Destination Snapshot;
- Carrier Mapping Snapshot;
- Shipment Customs Snapshot for cross-border traffic.

Recovery must not rebuild DHL authority from current Product rows, Checkout,
Warehouse config, current Geographic catalog/mapping release, carrier metadata,
provider responses, labels, invoices, Mongo, Redis, browser state, cache, or
projections.

## Regression Classification

Focused DHL/platform certification command:

```powershell
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter "FullyQualifiedName~DhlOfficialContractCompatibilityTests|FullyQualifiedName~DhlGeographicCompatibilityTests|FullyQualifiedName~DhlRuntimeAuthorityTests|FullyQualifiedName~ProviderMappingCoverageProductionReadinessTests|FullyQualifiedName~ShipmentOriginAuthorityTests|FullyQualifiedName~Customs" -nologo -v minimal --no-restore
```

Result: **PASS**, 56 passed, 0 failed.

Broad Shipping screen:

```powershell
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter FullyQualifiedName~Shipping -nologo -v minimal --no-build --no-restore
```

Result: **FAILED**, 82 passed, 5 failed.

Failure classification:

| Failing regression | Classification | DHL certification impact |
|---|---|---|
| `ShipmentExternalRegistrationSafetyTests.ExecuteCarrierForExistingShipmentAsync_RetryAfterSuccess_DoesNotCreateDuplicateCarrierShipment` | Platform Shared | Shared registration recovery fixture currently fails before/around mandatory origin admission; not DHL-specific |
| `ShipmentExternalRegistrationSafetyTests.ExecuteCarrierForExistingShipmentAsync_WhenExternalSuccessWasNotPersisted_ReconcilesByCarrierRequestIdWithoutDuplicateCreate` | Platform Shared | Shared recovery/idempotency fixture hits warehouse-origin fail-closed prerequisite; not DHL-specific |
| `ShippingWebhookTrustPipelineTests.ProcessWebhookAsync_ValidSignature_ProcessesWebhook_AndPersistsCompletedLog` | Platform Shared | Shared webhook trust pipeline; DHL webhooks are capability-disabled and not production-certified |
| `ShippingWebhookTrustPipelineTests.ProcessWebhookAsync_UppercaseHexSignature_IsAcceptedCaseInsensitively` | Platform Shared | Shared webhook trust pipeline; DHL webhook runtime is outside certified DHL capability |
| `CommerceHubFinanceServiceTests.CreateRefundAsync_ShippingOnlyRefund_ShouldNotReverseCommission` | Out Of Scope | CommerceHub finance/accounting authority selected by `Shipping` test name; unrelated to DHL |

DHL In Scope failures: **None**.

## Operational Separation

Operational evidence must not downgrade Development Certification. The
following remain operational or rollout evidence only:

- DHL sandbox credentials and live replay.
- DHL production account entitlement.
- Account-enabled country, service, product, COD, billing, and rate-limit
  evidence.
- Published DHL carrier mapping releases.
- Warehouse-origin admission coverage.
- Published Customs Evidence coverage for international SKUs.
- Migration deployment proof.
- Smoke tests and observability evidence.
- Production deployment and rollback rehearsal.

These are required before operational activation or production rollout, but
they are not DHL development blockers.

## Known Operational Dependencies

Before enabling DHL traffic:

1. Configure valid encrypted DHL API key, API secret, account ID, environment,
   and tenant carrier config.
2. Apply Geographic, GEO-8, GEO-9, SHIP-ORIGIN-1, CUSTOMS-2, and CUSTOMS-4
   migrations in the target environment.
3. Publish active DHL carrier mapping releases for each tenant-provider-account,
   environment, country, and `ShipmentRegistration` scope.
4. Admit warehouse-origin evidence for every DHL-enabled shipping warehouse.
5. Approve and publish Customs Evidence for every internationally shippable
   product/variant.
6. Run credentialed DHL sandbox create, tracking, label, customs, fail-closed,
   timeout, and recovery smoke tests.
7. Prove production account entitlement and account-enabled service coverage.
8. Keep DHL cancellation, pickup, and webhooks disabled until separately
   certified under official MyDHL contract evidence.
9. Confirm logs, metrics, alerts, secret rotation, and rollback procedure.

## Final Certification

### 1. Development Certification

**CERTIFIED**

DHL development is complete for the approved architecture. The adapter consumes
only admitted platform evidence, owns only MyDHL wire behavior, preserves
adapter purity, fails closed on unsafe input/output, and passes the focused
DHL/platform certification suite. No DHL In Scope regression failure remains.

### 2. Operational Readiness

**READY AFTER OPERATIONAL CHECKLIST**

The runtime is structurally ready for controlled operational validation, but
credentialed DHL sandbox replay, account entitlement proof, mapping publication,
warehouse-origin coverage, customs publication coverage, deployment evidence,
smoke tests, and observability proof are still required.

### 3. Production Rollout

**BLOCKED BY OPERATIONAL EVIDENCE**

Production rollout must wait for operational evidence. The blocker is not
development code; it is missing proof of live DHL credentials, account-enabled
coverage, mapping releases, admitted origins, admitted customs evidence,
migration deployment, smoke tests, monitoring, and rollout approval.

## Success Answers

- Reviewed ONLY DHL Development? **Yes.**
- Runtime modified? **No.**
- Backend modified? **No.**
- Frontend modified? **No.**
- Development Certified? **Yes - CERTIFIED.**
- Operational Ready? **READY AFTER OPERATIONAL CHECKLIST.**
- Production Ready? **BLOCKED BY OPERATIONAL EVIDENCE.**
- Remaining Development Blockers? **None.**
- Remaining Operational Blockers? **Credentialed sandbox replay, production
  account entitlement, mapping publication, warehouse-origin coverage, customs
  evidence coverage, migration deployment evidence, smoke tests, observability,
  and operator rollout proof.**
- Remaining Production Blockers? **Same operational evidence, plus controlled
  production enablement approval and rollback proof.**
- Recommend next phase: **DHL credentialed sandbox certification and controlled
  production rollout evidence collection.**

## AI Context Update Decision

The `.ai-context` files were not updated. DHL.FINAL discovered no new permanent
cross-system architecture truth. It confirmed provider-specific development
completion under the already frozen DHL, Geography, Customs, Shipment Origin,
Carrier Integration, Recovery, and SQL authority boundaries.
