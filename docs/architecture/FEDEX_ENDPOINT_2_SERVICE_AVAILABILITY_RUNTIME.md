# FEDEX-ENDPOINT-2 - Service Availability Runtime

Date: 2026-07-08
Scope: FedEx Service Availability endpoint execution only
Status: IMPLEMENTED

## Executive Summary

FEDEX-ENDPOINT-2 implements only the FedEx Service Availability endpoint
runtime.

The runtime consumes `CarrierNeutralServiceAvailabilityRequest`, maps admitted
origin and destination snapshots plus canonical shipment characteristics into
the official FedEx Service Availability request through
`FedExRequestBuilders.ServiceAvailability`, delegates OAuth and HTTP to the
existing FedEx transport, admits the FedEx response through
`FedExResponseAdmission.AdmitServiceAvailability`, and emits
`CarrierNeutralServiceAvailabilityResponse` evidence.

No carrier selection, shipment creation, tracking, labels, pickup, pricing,
checkout, routing, warehouse logic, SQL write, frontend, or business decision
behavior was added.

Official source reviewed:

- FedEx Service Availability API documentation:
  `https://developer.fedex.com/api/en-us/catalog/service-availability/v1/docs.html`

## Pipeline

`CarrierNeutralServiceAvailabilityRequest`

-> official FedEx Service Availability request builder

-> OAuth runtime via `IFedExTransportClient`

-> HTTP transport to `POST /availability/v1/transittimes`

-> FedEx Service Availability admission

-> `CarrierNeutralServiceAvailabilityResponse`

## Request Mapping

Input authority is carrier-neutral only:

- origin and destination snapshots are validated by
  `CarrierNeutralEndpointContractValidator`;
- the FedEx request consumes canonical street, city, postal code, ISO-2 country
  code, and administrative-area code from admitted snapshots;
- weight, package count, and optional dimensions are mapped from
  `CarrierNeutralShipmentCharacteristics`;
- declared value remains validated carrier-neutral evidence and is not used to
  invent unsupported FedEx fields in the current official request DTO;
- required carrier-neutral service identifiers are mapped only to the FedEx
  carrier-code constraint needed by the official request;
- mixed Express/Ground carrier-code requests fail closed because FedEx states
  multiple carrier codes cannot be specified in a single request;
- no provider DTO is accepted as input;
- no missing city, postal code, street, country, unit, weight, or dimension is
  invented.

## Admission

FedEx Service Availability response admission is provider-evidence only.

The runtime fails closed on:

- malformed JSON;
- unexpected root, output, service, alert, transit, or restriction schema;
- missing transaction ID;
- missing service evidence;
- unknown FedEx service identifiers;
- unknown transit units;
- invalid transit ranges;
- malformed timestamps;
- malformed alert objects;
- invalid carrier-neutral provider evidence;
- invalid carrier-neutral response DTO;
- transport failure.

Admitted FedEx services are mapped to carrier-neutral service identifiers,
transit estimates, provider warnings, provider evidence, and requested-service
unavailability restrictions. The response does not select a carrier or service,
price a shipment, mutate SQL, or advance shipment lifecycle.

## Authority Matrix

| Concern | Owner | FEDEX-ENDPOINT-2 role |
|---|---|---|
| Endpoint request/response contract | Shipping Platform | Consumes and emits carrier-neutral DTOs |
| Origin/destination snapshots | Shipping Platform admitted evidence | Consumes only |
| Geographic identity | Geographic Platform | Not mutated |
| FedEx service evidence | FedEx | Provider evidence only |
| Transit estimate evidence | FedEx | Provider evidence only |
| OAuth | Existing FedEx OAuth runtime | Reused through transport |
| HTTP execution | Existing FedEx transport | Reused |
| Response admission | FedEx admission boundary | Reused and extended for Service Availability |
| Carrier selection | Shipping Platform business domains | Not implemented |
| Pricing/checkout/routing | Existing backend domains | Not touched |
| Shipment lifecycle | Shipment platform | Not touched |
| SQL truth | Existing backend domains | Not touched |

## Failure Matrix

| Failure | Result |
|---|---|
| Invalid neutral request | Fail before provider transport |
| Missing origin/destination snapshot evidence | Fail before provider transport |
| Missing FedEx-required street/city/postal/country evidence | Fail before provider transport |
| Unknown neutral service identifier | Fail before provider transport |
| Mixed FedEx carrier-code categories | Fail before provider transport |
| Transport failure | Fail closed, no DTO emitted |
| Malformed FedEx JSON | Admission rejection |
| Unexpected FedEx schema | Admission rejection |
| Unknown FedEx service type | Admission rejection |
| Unknown transit unit | Admission rejection |
| Invalid transit range/date | Admission rejection |
| Invalid neutral response | Admission rejection |
| Shipment/SQL/routing/business mutation pressure | Not implemented |

## Regression Coverage

Updated focused coverage:

- `CarrierNeutralEndpointContractsCertificationTests`
- `FedExEndpointRuntimeIntegrationTests`
- `FedExResponseAdmissionTests`
- `FedExOfficialContractCompatibilityTests`

Coverage verifies:

- carrier-neutral request consumption;
- official FedEx request builder usage;
- OAuth delegation through transport request evidence;
- HTTP transport delegation to `FedExOptions.TransitTimesPath`;
- FedEx Service Availability admission delegation;
- carrier-neutral response DTO emission;
- fail-closed malformed JSON;
- fail-closed unexpected schema;
- fail-closed unknown FedEx service identifiers;
- fail-closed unknown transit units;
- no Shipment mutation;
- no SQL access;
- no routing, carrier selection, pricing, checkout, warehouse, label, pickup, or
  frontend behavior.

## Verification

Executed focused verification:

```powershell
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter FullyQualifiedName~FedExResponseAdmissionTests -nologo -v minimal
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter FullyQualifiedName~FedExEndpointRuntimeIntegrationTests -nologo -v minimal
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter FullyQualifiedName~CarrierNeutralEndpointContractsCertificationTests -nologo -v minimal
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter FullyQualifiedName~FedExOfficialContractCompatibilityTests -nologo -v minimal
```

Results:

- `FedExResponseAdmissionTests`: passed, 14 tests.
- `FedExEndpointRuntimeIntegrationTests`: passed, 7 tests.
- `CarrierNeutralEndpointContractsCertificationTests`: passed, 6 tests.
- `FedExOfficialContractCompatibilityTests`: passed, 31 tests.

## Final Recommendation

Approve FEDEX-ENDPOINT-2 as the FedEx Service Availability endpoint runtime.

Recommended next phase: keep the FedEx architecture frozen through
FEDEX.FINAL unless a separately scoped endpoint phase is approved. Do not
extend this work into carrier selection, pricing, checkout, shipment creation,
labels, pickup, frontend, SQL mutation, or business-rule behavior.

## Explicit Success Answers

- Reviewed ONLY Service Availability Runtime? **Yes.**
- Runtime modified? **Yes, Service Availability endpoint runtime only.**
- Backend modified? **Yes, FedEx adapter/admission/request DTO tests/docs only.**
- Frontend modified? **No.**
- Service Availability integrated? **Yes.**
- Carrier-neutral contracts consumed? **Yes.**
- Official request builder used? **Yes.**
- Transport reused? **Yes.**
- Admission reused? **Yes, with Service Availability admission added to the existing boundary.**
- Carrier selection implemented? **No.**
- Business logic added? **No.**
- Shipment Runtime modified? **No.**
- SQL modified? **No.**
- Architecture preserved? **Yes.**
- Backward compatible? **Yes for existing shipment, tracking, label, pickup, quote, and placeholder entrypoints; the neutral Service Availability overload now executes.**
- Production-certifiable? **Yes for Service Availability runtime; operational credentials and live provider proof remain deployment gates.**
- Recommend next phase: **FEDEX.FINAL architecture certification or separately approved endpoint phase without expanding this runtime scope.**
