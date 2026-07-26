# FEDEX-ENDPOINT-1 - Address Validation Runtime

Date: 2026-07-08
Scope: FedEx Address Validation endpoint execution only
Status: IMPLEMENTED

## Executive Summary

FEDEX-ENDPOINT-1 implements only the FedEx Address Validation endpoint runtime.

The runtime consumes `CarrierNeutralAddressValidationRequest`, maps the
destination snapshot to the official FedEx `addressesToValidate` request through
`FedExRequestBuilders.ValidateAddresses`, delegates OAuth and HTTP to the
existing FedEx transport, admits the FedEx response through
`FedExResponseAdmission.AdmitAddressValidation`, and emits
`CarrierNeutralAddressValidationResponse` evidence.

No Shipment creation, tracking, labels, pickup, service availability, business
rules, Geography mutation, Carrier Mapping mutation, Warehouse mutation, SQL
write, frontend, or POS Admin behavior was added.

## Pipeline

`CarrierNeutralAddressValidationRequest`

-> official FedEx request builder

-> OAuth runtime via `IFedExTransportClient`

-> HTTP transport to `POST /address/v1/addresses/resolve`

-> FedEx Address Validation admission

-> `CarrierNeutralAddressValidationResponse`

## Request Mapping

Input authority is carrier-neutral only:

- origin and destination snapshots are validated by
  `CarrierNeutralEndpointContractValidator`;
- the FedEx request consumes destination street lines, city when present, postal
  code when present, ISO-2 country code, and administrative-area code when
  present;
- no provider DTO is accepted as input;
- no address field is invented;
- missing official FedEx street/country evidence fails before transport.

## Admission

FedEx response admission remains provider-evidence only.

The runtime fails closed on:

- malformed JSON;
- unexpected schema;
- missing transaction ID;
- missing `output.resolvedAddresses`;
- missing street or country evidence;
- unknown address classification;
- invalid normalized carrier-neutral response;
- malformed provider evidence JSON;
- transport failure.

The admitted response copies normalized address evidence into a new
carrier-neutral response DTO. It does not mutate stored snapshots or Geography.

## Authority Matrix

| Concern | Owner | FEDEX-ENDPOINT-1 role |
|---|---|---|
| Address truth | Shipping Platform and admitted snapshots | Consumes only |
| Geographic identity | Geographic Platform | Not mutated |
| FedEx normalized result | FedEx | Provider evidence only |
| OAuth | Existing FedEx OAuth runtime | Reused |
| HTTP execution | Existing FedEx transport | Reused |
| Response admission | FedEx admission boundary | Reused and narrowed |
| SQL | Existing backend domains | Not touched |
| Shipment lifecycle | Shipment platform | Not touched |

## Failure Matrix

| Failure | Result |
|---|---|
| Invalid neutral request | Fail before provider transport |
| Missing destination street/country | Fail before provider transport |
| Transport failure | Fail closed, no DTO emitted |
| Malformed FedEx JSON | Admission rejection |
| Unexpected FedEx schema | Admission rejection |
| Unknown classification | Admission rejection |
| Missing resolved address evidence | Admission rejection |
| Invalid neutral normalized response | Admission rejection |
| Geography/SQL/shipment mutation pressure | Not implemented |

## Regression Coverage

Updated focused coverage:

- `CarrierNeutralEndpointContractsCertificationTests`
- `FedExEndpointRuntimeIntegrationTests`
- `FedExResponseAdmissionTests`

Coverage verifies:

- carrier-neutral request consumption;
- official FedEx request builder usage;
- transport delegation;
- admission delegation;
- carrier-neutral response DTO emission;
- fail-closed malformed/unknown provider evidence;
- no Geography mutation;
- no SQL access;
- no Shipment mutation;
- service availability remains closed.

## Verification

Recommended focused commands:

```powershell
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter FullyQualifiedName~FedExEndpointRuntimeIntegrationTests -nologo -v minimal
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter FullyQualifiedName~FedExResponseAdmissionTests -nologo -v minimal
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter FullyQualifiedName~CarrierNeutralEndpointContractsCertificationTests -nologo -v minimal
```

## Final Recommendation

Approve FEDEX-ENDPOINT-1 as the FedEx Address Validation endpoint runtime.

Recommended next phase: FedEx Service Availability endpoint runtime, using the
already frozen carrier-neutral service-availability contract and preserving the
same evidence-only, fail-closed boundary.

## Explicit Success Answers

- Reviewed ONLY Address Validation Runtime? **Yes.**
- Runtime modified? **Yes, Address Validation endpoint runtime only.**
- Backend modified? **Yes, FedEx adapter/admission tests/docs only.**
- Frontend modified? **No.**
- Address Validation integrated? **Yes.**
- Carrier-neutral contracts consumed? **Yes.**
- Official request builder used? **Yes.**
- Transport reused? **Yes.**
- Admission reused? **Yes.**
- Business logic added? **No.**
- Geography mutated? **No.**
- SQL modified? **No.**
- Architecture preserved? **Yes.**
- Backward compatible? **Yes for existing shipment/tracking/pickup/service paths; the neutral address-validation overload now executes.**
- Production-certifiable? **Yes for Address Validation runtime; operational credentials and live provider proof remain deployment gates.**
- Recommend next phase: **FedEx Service Availability endpoint runtime.**
