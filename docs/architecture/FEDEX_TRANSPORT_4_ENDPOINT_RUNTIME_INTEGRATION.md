# FEDEX-TRANSPORT-4 - Endpoint Runtime Integration

Date: 2026-07-08
Scope: FedEx endpoint runtime composition only
Status: IMPLEMENTED WITH FAIL-CLOSED GAPS

## Executive Summary

FEDEX-TRANSPORT-4 wires the existing certified FedEx runtime components into the adapter endpoint surface without redesigning OAuth, transport, admission, contracts, DTOs, geography, carrier mapping, customs, shipment runtime, business rules, or frontend behavior.

Implemented:

- Create Shipment endpoint composition;
- Track Shipment endpoint composition;
- official request creation through existing `FedExRequestBuilders`;
- OAuth delegation through `FedExTransportRequest.OAuthRequest`;
- HTTP execution through `IFedExTransportClient`;
- response admission through `FedExResponseAdmission`;
- carrier-neutral `CreateShipmentResult` and `ShipmentTrackingResult` returns;
- secret-safe endpoint logging with correlation ID, endpoint, duration, HTTP status, and admission result only;
- focused endpoint integration tests.

Fail-closed by design:

- Address Validation remains unavailable through the current adapter entrypoint because no certified carrier-neutral address-validation request DTO exists on the adapter surface.
- Service Availability remains unavailable because no certified carrier-neutral request DTO and no certified service-availability response admission mapping exists.
- Pickup remains unavailable because request mapping and response admission are not fully certified for endpoint execution.
- Quotes, cancellation, credentials validation, labels, webhooks, and shipment lookup remain outside this phase.

No `.ai-context` files were updated because runtime composition did not discover new permanent cross-provider architecture truth.

## Pipeline Diagram

```text
Carrier-neutral request DTO
  |
  v
Official FedEx request builder
  |
  v
FedExTransportRequest with OAuth evidence
  |
  v
FEDEX-TRANSPORT-1 OAuth Runtime
  |
  v
FEDEX-TRANSPORT-2 HTTP Transport
  |
  v
FEDEX-TRANSPORT-3 Response Admission
  |
  v
Carrier-neutral response DTO
```

## Endpoint Matrix

| Endpoint | Runtime status | Request source | Official builder | Transport | Admission | Response |
|---|---|---|---|---|---|---|
| Create Shipment | Integrated | `CreateShipmentRequest` | `FedExRequestBuilders.CreateShipment` | `IFedExTransportClient` | `AdmitShipment` | `CreateShipmentResult` |
| Track Shipment | Integrated | tracking number | `FedExRequestBuilders.TrackByTrackingNumbers` | `IFedExTransportClient` | `AdmitTracking` | `ShipmentTrackingResult` |
| Address Validation | Fail-closed | Missing certified carrier-neutral adapter DTO | Not executed | Not executed | Not executed | `FedExUnsupportedOperationResult` |
| Service Availability | Fail-closed | Missing certified carrier-neutral adapter DTO | Not executed | Not executed | Missing certified admission | `FedExUnsupportedOperationResult` |
| Pickup | Fail-closed | Incomplete endpoint execution model | Not executed | Not executed | Missing certified admission | `FedExUnsupportedOperationResult` |

## Integration Flow

Create Shipment:

1. Accepts existing carrier-neutral `CreateShipmentRequest`.
2. Requires official FedEx service type rather than mapping from generic service names.
3. Builds `FedExShipmentRequest` through `FedExRequestBuilders.CreateShipment`.
4. Sends `POST /ship/v1/shipments` through `IFedExTransportClient`.
5. Admits only `FedExResponseAdmission.AdmitShipment` output.
6. Returns carrier-neutral result fields from admitted provider evidence only.

Track Shipment:

1. Accepts tracking number evidence through the existing adapter method.
2. Builds `FedExTrackingNumbersRequest` through `FedExRequestBuilders.TrackByTrackingNumbers`.
3. Sends `POST /track/v1/trackingnumbers` through `IFedExTransportClient`.
4. Admits only `FedExResponseAdmission.AdmitTracking` output.
5. Returns carrier-neutral tracking DTO evidence only.

## Delegation Rules

- The adapter does not construct HTTP clients.
- The adapter does not obtain, cache, refresh, parse, or log OAuth tokens.
- The adapter does not interpret raw provider response JSON.
- The adapter calls only certified official request builders for integrated endpoints.
- The adapter calls only certified transport and admission components.
- The adapter returns failure when transport or admission fails.
- The adapter never retries independently of the transport runtime.

## Authority Matrix

| Concern | Owner in this phase | Explicit non-owner |
|---|---|---|
| OAuth token | `IFedExOAuthClient` via transport | `FedExAdapter` |
| HTTP execution | `IFedExTransportClient` | `FedExAdapter` business code |
| Response schema admission | `FedExResponseAdmission` | Transport |
| Shipment creation decision | Existing shipment runtime | FedEx endpoint integration |
| Shipment lifecycle | Existing shipment runtime | FedEx endpoint integration |
| Tracking persistence | Existing shipment/tracking runtime | FedEx endpoint integration |
| Geography | Published mapping/snapshot authorities | FedEx adapter |
| Customs | Customs Evidence and shipment snapshot | FedEx adapter |
| SQL mutation | Existing backend services | FedEx adapter |
| Frontend presentation | Existing frontend boundaries | FedEx adapter |

## Failure Handling

The endpoint runtime fails closed when:

- transport client is unavailable;
- OAuth cannot be delegated through transport;
- official request validation fails;
- required credentials or account evidence is missing;
- official service mapping is missing;
- transport fails;
- admission rejects the provider response;
- carrier-neutral request DTO is missing;
- response admission mapping is missing;
- operation is unsupported by this phase.

No fallback behavior is invented for shipment identifiers, tracking numbers, labels, status, address normalization, service availability, pickup confirmations, geography, customs, routing, or lifecycle.

## Regression Coverage

Added:

- `PointOfSale.Tests/Infrastructure/FedExEndpointRuntimeIntegrationTests.cs`

Updated:

- `PointOfSale.Tests/Infrastructure/FedExResponseAdmissionTests.cs`

Coverage verifies:

- Create Shipment delegates to official request builder, OAuth evidence, transport, and admission.
- Tracking delegates to official request builder, OAuth evidence, transport, and admission.
- Transport failure fails closed.
- Admission failure fails closed.
- Missing address-validation, service-availability, and pickup execution contracts fail closed.
- Adapter contains no SQL, order, inventory, geography mapping service, shipment service, or mutation authority.
- Existing FedEx OAuth, transport, contract, and admission tests remain green.

## Verification

Commands:

```powershell
dotnet build PointOfSale.Infrastructure\PointOfSale.Infrastructure.csproj --no-restore -nologo -v minimal
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter FullyQualifiedName~FedEx -nologo -v minimal
```

Result:

- Infrastructure build: PASS with existing unrelated warnings.
- FedEx-filtered tests: PASS, 53 passed, 0 failed.

## Final Recommendation

Approve FEDEX-TRANSPORT-4 as a narrow endpoint runtime composition for Create Shipment and Track Shipment only.

Recommended next phase: certify carrier-neutral DTOs and response admission for Address Validation and Service Availability, then integrate those endpoints without changing geography, shipment lifecycle, customs, or business-rule authority. Pickup should remain closed until full request and response admission coverage is explicitly certified.

## Explicit Success Answers

- Reviewed ONLY Endpoint Runtime Integration? **Yes.**
- Runtime modified? **Yes, FedEx adapter endpoint composition only.**
- Backend modified? **Yes, backend FedEx adapter and focused backend tests only.**
- Frontend modified? **No.**
- Shipment endpoint integrated? **Yes.**
- Tracking endpoint integrated? **Yes.**
- Address Validation integrated? **No; fail-closed because the current adapter surface has no certified carrier-neutral request DTO.**
- Service Availability integrated? **No; fail-closed because certified request and admission mapping are missing.**
- Pickup integrated? **No; fail-closed because full endpoint execution is not certified.**
- OAuth reused? **Yes.**
- Transport reused? **Yes.**
- Admission reused? **Yes for shipment and tracking.**
- Business logic duplicated? **No.**
- Shipment Runtime modified? **No.**
- Architecture preserved? **Yes.**
- Backward compatible? **Yes.**
- Production-certifiable? **Partially: Create Shipment and Tracking composition are certifiable; address validation, service availability, pickup, and production activation remain gated.**
- Recommend next phase: **FedEx address-validation/service-availability request DTO and response-admission certification, then endpoint integration for those operations.**
