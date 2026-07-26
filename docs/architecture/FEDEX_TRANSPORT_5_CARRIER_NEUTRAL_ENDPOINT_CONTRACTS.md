# FEDEX-TRANSPORT-5 - Carrier-Neutral Endpoint Contracts

Date: 2026-07-08
Scope: Carrier-neutral endpoint contracts for Address Validation and Service Availability only
Status: IMPLEMENTED

## Executive Summary

FEDEX-TRANSPORT-5 closes the FEDEX-TRANSPORT-4 contract gap by introducing
platform-owned, carrier-neutral DTOs for Address Validation and Service
Availability.

The contracts are reusable by FedEx and future carriers. They contain canonical
snapshot evidence, shipment characteristics, supported service identifiers,
warnings, restrictions, transit estimates, confidence, and bounded provider
evidence only.

This phase does not implement FedEx transport, OAuth, HTTP execution, response
admission, shipment creation, tracking, labels, pickup, routing, carrier
selection, SQL persistence, business rules, POS Admin, or frontend behavior.
FedEx now consumes the new request DTOs at the adapter placeholder boundary and
still fails closed before transport until provider-specific request mapping and
response admission are separately certified.

## Address Validation DTO

Request:

- `CarrierNeutralAddressValidationRequest`
- `CarrierNeutralAddressSnapshot Origin`
- `CarrierNeutralAddressSnapshot Destination`
- `CorrelationId`

Snapshot evidence:

- address admission evidence ID;
- catalog release ID;
- canonical country, administrative area, locality, district, and zone place
  IDs;
- country ISO-2;
- administrative area code/display;
- city, district, zone, postal code;
- street lines as snapshot address detail;
- snapshot timestamp and version.

Response:

- `CarrierNeutralAddressValidationResponse`
- validation result;
- warnings;
- optional normalized address evidence;
- provider evidence;
- confidence between zero and one.

The response is evidence only. It never modifies Geographic catalog data and
never overwrites origin, destination, order, shipment, saved-address, or
warehouse snapshots.

## Service Availability DTO

Request:

- `CarrierNeutralServiceAvailabilityRequest`
- origin snapshot;
- destination snapshot;
- `CarrierNeutralShipmentCharacteristics`;
- required carrier-neutral service identifiers;
- correlation ID.

Shipment characteristics:

- weight and unit;
- optional dimensions and unit;
- optional declared value and currency;
- package count.

Response:

- `CarrierNeutralServiceAvailabilityResponse`
- supported services;
- transit estimates;
- provider warnings;
- service restrictions;
- provider evidence.

The response is evidence only. It never routes, selects a carrier, selects a
service, prices shipping, creates a shipment, or mutates SQL authority.

## Authority Matrix

| Concern | Owner | Contract role | Non-owner |
|---|---|---|---|
| Endpoint contracts | Shipping Platform | Defines reusable DTOs and validation | FedEx, DHL, Bosta, Aramex, Mylerz |
| Geographic identity | Geographic Platform and admitted snapshots | Consumed as snapshot evidence | Endpoint response |
| Origin snapshot | Shipment/Warehouse-owned admitted evidence | Consumed only | Provider |
| Destination snapshot | Order/Shipment-owned admitted evidence | Consumed only | Provider |
| Service identifiers | Platform contract enum | Input/evidence only | Provider-specific service names |
| Service availability | Provider evidence admitted through platform contract | Evidence only | Routing and carrier selection |
| Address validation result | Provider evidence admitted through platform contract | Evidence only | Geographic mutation |
| Routing/carrier selection | Existing backend platform domains | Not performed | Contract DTOs |
| Shipment lifecycle | `ShipmentLifecycleTransitionPolicy` and SQL | Not touched | Provider response |
| SQL truth | Existing backend services | Not touched | Endpoint contracts |

## Validation Rules

Fail closed on:

- missing origin;
- missing destination;
- missing country place identity;
- missing country ISO-2;
- missing city/locality evidence;
- invalid snapshot version;
- unknown weight units;
- unknown dimension units;
- non-positive weight;
- non-positive package count;
- invalid dimensions;
- negative declared value;
- declared value without currency;
- unknown currency;
- missing required services;
- unknown service identifiers;
- malformed provider evidence JSON;
- non-object provider evidence JSON;
- missing warning/evidence codes;
- invalid confidence outside `0..1`;
- invalid transit ranges.

## Failure Matrix

| Failure | Result |
|---|---|
| Missing origin/destination | Request validation fails before adapter mapping |
| Invalid snapshot | Request validation fails |
| Unknown units | Request validation fails |
| Invalid dimensions | Request validation fails |
| Negative values | Request validation fails |
| Malformed provider evidence | Response validation fails |
| Unknown service identifier | Request/response validation fails |
| Provider-specific field pressure | Rejected by provider-neutral DTO surface |
| Geography mutation request | Outside contract; forbidden |
| Shipment mutation request | Outside contract; forbidden |
| SQL or business-rule request | Outside contract; forbidden |

## Future Reuse

FedEx, DHL, Bosta, Aramex, Mylerz, UPS, or any later carrier can consume the
same platform contracts. Provider-specific adapters may map these DTOs to their
official wire contracts only after a provider-specific request mapping and
response admission phase is certified.

Future carrier work must preserve:

- snapshot input only;
- evidence output only;
- no provider ownership of contracts;
- no Geographic mutation;
- no Shipment mutation;
- no SQL authority;
- no routing or carrier selection.

## Regression Coverage

Added:

- `PointOfSale.Tests/Architecture/CarrierNeutralEndpointContractsCertificationTests.cs`

Coverage verifies:

- neutral DTO names and placement;
- no FedEx, DHL, Aramex, Bosta, or Mylerz leakage in platform contracts;
- no SQL, Geography repository, carrier mapping service, or shipment service
  dependency in contracts;
- fail-closed request validation;
- fail-closed response provider-evidence validation;
- JSON serialization without provider leakage;
- FedEx consumes the neutral request DTOs but does not execute transport.

## Verification

Focused verification command:

```powershell
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter FullyQualifiedName~CarrierNeutralEndpointContractsCertificationTests -nologo -v minimal
```

Additional recommended FedEx regression command:

```powershell
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter FullyQualifiedName~FedEx -nologo -v minimal
```

## Final Recommendation

Approve FEDEX-TRANSPORT-5 as the platform-owned Address Validation and Service
Availability endpoint contract certification.

Recommended next phase: FedEx-specific request mapping and response admission
for Address Validation and Service Availability, reusing these contracts and
preserving fail-closed provider evidence admission. Do not implement pickup,
labels, routing, business rules, frontend, or production rollout in that phase.

## Explicit Success Answers

- Reviewed ONLY Endpoint Contracts? **Yes.**
- Runtime modified? **No endpoint execution runtime was implemented.**
- Backend modified? **Yes, platform DTO contracts, validation, FedEx placeholder consumption, and focused backend tests only.**
- Frontend modified? **No.**
- Carrier-neutral contracts created? **Yes.**
- Address Validation DTO created? **Yes.**
- Service Availability DTO created? **Yes.**
- Provider-neutral? **Yes.**
- FedEx-specific? **No.**
- Architecture preserved? **Yes.**
- Reusable by all carriers? **Yes.**
- Backward compatible? **Yes. Existing FedEx endpoint behavior remains fail-closed.**
- Production-certifiable? **Yes for contracts; provider-specific endpoint execution remains uncertified.**
- Recommend next phase: **FedEx address-validation and service-availability request mapping plus response-admission certification using these platform contracts.**
