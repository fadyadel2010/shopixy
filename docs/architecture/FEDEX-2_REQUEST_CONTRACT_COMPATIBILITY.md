# FEDEX-2 - Request Contract Compatibility

Date: 2026-07-07
Scope: FedEx request/response DTOs, serialization, deserialization, validation, and request builders only
Status: COMPLETE

## Executive Summary

FEDEX-2 implemented only official FedEx request-contract compatibility.

No FedEx HTTP execution, OAuth token execution, tracking execution, label retrieval,
pickup execution, webhook runtime, backend workflow, frontend surface, geographic
redesign, customs redesign, or Shipping Platform redesign was introduced.

The implementation adds a standalone FedEx contract layer:

- `PointOfSale.Infrastructure/Shipping/Adapters/FedExContract.cs`

This layer is not wired into runtime adapter operations. The existing
`FedExAdapter` still fails closed with `FEDEX_NOT_YET_CERTIFIED` for all runtime
operations.

Final verdict: **REQUEST CONTRACT FROZEN; RUNTIME REMAINS FAIL-CLOSED**.

## Official Request Contract

Official sources reviewed:

- FedEx API Authorization: https://developer.fedex.com/api/en-us/catalog/authorization/v1/docs.html
- FedEx Ship API: https://developer.fedex.com/api/en-us/catalog/ship/v1/docs.html
- FedEx Basic Integrated Visibility / Track API: https://developer.fedex.com/api/en-us/catalog/track/v1/docs.html
- FedEx Pickup Request API: https://developer.fedex.com/api/en-us/catalog/pickup/v1/docs.html
- FedEx Service Availability API: https://developer.fedex.com/api/en-us/catalog/service-availability/v1/docs.html
- FedEx Address Validation API: https://developer.fedex.com/api/en-us/catalog/address-validation/v1/docs.html

Official endpoints remain frozen from FEDEX-1 and FEDEX-RUNTIME-1:

| Capability | Method/path |
|---|---|
| OAuth token | `POST /oauth/token` |
| Create shipment | `POST /ship/v1/shipments` |
| Validate shipment | `POST /ship/v1/shipments/packages/validate` |
| Track by tracking number | `POST /track/v1/trackingnumbers` |
| Pickup availability | `POST /pickup/v1/pickups/availabilities` |
| Create pickup | `POST /pickup/v1/pickups` |
| Address validation | `POST /address/v1/addresses/resolve` |
| Services and packaging | `POST /availability/v1/packageandserviceoptions` |

No public official OpenAPI/Swagger artifact was available in this phase. DTOs
therefore freeze the official public documentation surface and known official
field names only. Unsupported or unknown fields/enums fail closed.

## DTO Matrix

| Area | Implemented DTOs |
|---|---|
| OAuth | `FedExOAuthRequest`, `FedExOAuthTokenResult` |
| Shipment | `FedExShipmentRequest`, `FedExRequestedShipment`, `FedExParty`, `FedExContact`, `FedExAddress`, `FedExShippingChargesPayment`, `FedExPackageLineItem`, `FedExWeight`, `FedExDimensions`, `FedExLabelSpecification` |
| Tracking | `FedExTrackingNumbersRequest`, `FedExTrackingInfo`, `FedExTrackingNumberInfo`, `FedExTrackingResponse` |
| Pickup | `FedExPickupAvailabilityRequest`, `FedExCreatePickupRequest`, `FedExPickupOriginDetail`, `FedExPickupLocation` |
| Address validation | `FedExAddressValidationRequest`, `FedExAddressToValidate` |
| Service availability | `FedExServiceAvailabilityRequest`, `FedExServiceAvailabilityShipment` |
| Responses/errors | `FedExShipmentResponse`, `FedExShipmentOutput`, `FedExTransactionShipment`, `FedExPieceResponse`, `FedExPackageDocument`, `FedExAlert`, `FedExErrorResponse`, `FedExError` |

## Required Fields

OAuth:

- `grant_type`
- `client_id`
- `client_secret`
- `child_key` and `child_secret` for child credential grant types

Shipment creation and shipment validation:

- `labelResponseOptions`
- `accountNumber.value`
- `requestedShipment.shipper.contact`
- `requestedShipment.shipper.address`
- `requestedShipment.recipients`
- `requestedShipment.pickupType`
- `requestedShipment.serviceType`
- `requestedShipment.packagingType`
- `requestedShipment.shippingChargesPayment.paymentType`
- payer account evidence when payment type is not `SENDER`
- `requestedShipment.requestedPackageLineItems.weight`
- `requestedShipment.labelSpecification`

Tracking by tracking number:

- `trackingInfo`
- `trackingInfo[].trackingNumberInfo.trackingNumber`
- maximum 30 tracking numbers per request

Pickup:

- `associatedAccountNumber.value`
- `carrierCode`
- pickup address or origin detail
- pickup location contact/address
- package location
- ready timestamp
- close time

Address validation:

- `addressesToValidate`
- nested `address`
- `streetLines`, `city`, `postalCode`, `countryCode`

Service availability:

- `carrierCode`
- `requestedShipment`
- shipper address
- recipient address
- pickup type

## Optional Fields

Optional DTO fields are serialized only when present:

- contact company name and email
- address state/province and residential indicator
- shipment date stamp
- block insight visibility
- package dimensions
- customer references
- label URL or encoded label in response DTOs
- FedEx alerts/errors/warnings response evidence

## Serialization Rules

- JSON uses exact `JsonPropertyName` attributes for official FedEx field names.
- JSON serialization ignores null optional fields.
- OAuth uses form field names `grant_type`, `client_id`, `client_secret`,
  `child_key`, and `child_secret`.
- Serialization validates before output.
- Unknown enum values fail before serialization.
- Required values are not silently omitted.
- The contract layer contains no `HttpClient`, `SendAsync`, `PostAsync`, or
  transport dependency.

## Validation Rules

Fail closed on:

- missing OAuth client ID, client secret, or grant type
- unknown OAuth grant type
- child credential grant type without child credentials
- child credentials on `client_credentials`
- missing FedEx account number
- missing shipper, recipient, contact, address, package weight, or label
  specification
- unknown service type
- unknown packaging type
- unknown pickup type
- unknown payment type
- unknown carrier code
- non-positive package weight
- non-positive dimensions
- more than 30 tracking numbers in one tracking-number request
- invalid address schema
- invalid shipment schema

Official allowlists are intentionally narrow and live in `FedExContractEnums`.
Adding values requires another contract review against official FedEx
documentation.

## Breaking Issues

| Issue | Severity | Resolution |
|---|---:|---|
| No public official OpenAPI/Swagger artifact confirmed | Medium | Implemented conservative public-doc DTO freeze and fail-closed validation |
| Portal-only interactive request schema may contain additional fields not exposed publicly | Medium | Not guessed; future authenticated portal collection review can extend DTOs |
| Shipment customs details are provider-specific and require separate carrier customs mapping certification | Blocking for international shipment execution | Not implemented in FEDEX-2 |
| Runtime still has no OAuth execution or HTTP transport | Blocking for production FedEx | Preserved intentionally |

## Regression Coverage

Updated:

- `PointOfSale.Tests/Architecture/FedExOfficialContractCompatibilityTests.cs`

Coverage freezes:

- FedEx carrier foundation remains registered but fail-closed.
- FedEx contract layer has no HTTP execution dependency.
- OAuth form serialization uses official field names.
- OAuth grant validation fails closed.
- Shipment JSON serialization uses official property names and nesting.
- Shipment required fields and enum validation fail closed.
- Tracking request schema uses `trackingInfo[].trackingNumberInfo.trackingNumber`.
- Tracking request rejects more than 30 tracking numbers.
- Pickup, address validation, and service availability DTOs serialize official
  root field names.
- Address validation rejects malformed address payloads.
- Shipment response deserialization preserves official tracking/label/alert
  field names.

## Verification

Command:

```powershell
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter FullyQualifiedName~FedExOfficialContractCompatibilityTests -nologo -v minimal
```

Result:

- PASS
- 14 passed
- 0 failed

Existing unrelated compiler/analyzer warnings remain.

## Special Review

Does the official FedEx request contract require any platform capability not
already represented by the certified Shipping Platform?

No.

Shipping Platform fully models the official FedEx request contract:

- OAuth-backed carrier authentication fits Carrier Authentication.
- Shipment creation and validation fit Carrier Registration and pre-submission
  provider validation evidence.
- Tracking fits Shipment Tracking Authority.
- Labels fit carrier response/label admission.
- Pickup fits provider capability/action evidence.
- Address validation fits provider suggestion evidence and does not become
  Geographic Authority.
- Service availability fits carrier availability evidence.
- Customs and international shipment details are represented by the certified
  carrier-neutral Customs boundary and require only future FedEx-specific
  serialization mapping, not a new platform domain.

## AI Context Update Decision

No `.ai-context` files were updated.

FEDEX-2 discovered provider-specific FedEx request-contract details only. It did
not discover a new permanent cross-provider architectural truth.

## Final Recommendation

Proceed to **FEDEX-3 Geographic Compatibility** or a separate authenticated
FedEx portal collection review to expand field-level DTO coverage before any
transport phase.

Do not implement FedEx HTTP execution until OAuth execution, token caching,
request serialization admission, response admission, labels, tracking status
mapping, pickup/service/address operations, customs mapping, account entitlement,
sandbox proof, and operational rollout are separately certified.

## Explicit Success Answers

- Reviewed ONLY FedEx Request Contract? Yes.
- Runtime modified? No runtime behavior modified; contract DTO code and tests only.
- Backend modified? No backend workflow/API behavior modified.
- Frontend modified? No.
- Official DTOs implemented? Yes, for the public official contract surface.
- Serialization implemented? Yes, contract-only JSON/form serialization.
- Validation implemented? Yes, fail-closed contract validation.
- HTTP implemented? No.
- OAuth execution implemented? No.
- Backward compatible? Yes.
- Production active? No.
- Does FedEx require any new platform capability? No.
- Recommend next phase: FEDEX-3 Geographic Compatibility or authenticated portal collection schema review before transport.
