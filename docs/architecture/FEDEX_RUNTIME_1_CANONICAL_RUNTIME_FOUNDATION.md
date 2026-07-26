# FEDEX-RUNTIME-1 - Canonical FedEx Runtime Foundation

Date: 2026-07-07
Scope: FedEx runtime foundation only
Status: COMPLETE

## Executive Summary

FedEx is now introduced as a first-class Shipping Platform provider at the runtime foundation layer only.

This phase registers the canonical carrier code, adapter skeleton, capability declaration, official endpoint configuration, OAuth abstraction, and dependency injection entries. It does not implement shipment creation, tracking, labels, cancellation, pickup, service availability, address validation, webhooks, OAuth token requests, request serialization, response parsing, business logic, geography, customs, checkout, POS Admin, Storefront, CommerceHub, or operational rollout.

All FedEx runtime operations fail closed with `FEDEX_NOT_YET_CERTIFIED`. No FedEx HTTP execution is possible from the new adapter foundation.

## Carrier Registration

FedEx is registered in `CarrierCodes` as:

```csharp
public const string FedEx = "fedex";
```

`CarrierCodes.AllCodes` includes `FedEx`, making it a canonical provider identity rather than an ad hoc string.

## Carrier Definition

`FedExCarrierDefinition` declares the official future capability surface without certifying runtime execution:

| Capability | Declaration | Runtime status |
|---|---:|---|
| Prepaid | Yes | Not yet certified |
| Tracking | Yes | Not yet certified |
| Labels | Yes | Not yet certified |
| Cancellation | Yes | Not yet certified |
| Pickup scheduling | Yes | Not yet certified |
| Webhooks | Yes | Not yet certified |
| COD | No | Not implemented |
| Quotes | No | Not implemented in this foundation |

The definition uses no country rollout list. Geography remains outside this phase.

## Adapter Structure

`FedExAdapter` implements `IShippingCarrierAdapter` and exposes `CarrierCode = "fedex"` and `DisplayName = "FedEx"`.

Implemented methods are intentionally fail-closed:

- `GetQuoteAsync`
- `CreateShipmentAsync`
- `GetShipmentByCarrierRequestIdAsync`
- `CancelShipmentAsync`
- `GetTrackingAsync`
- `ValidateCredentialsAsync`
- `GetSupportedServiceLevels`
- `ParseWebhook`

Additional FedEx placeholders are present for labels, pickup, service availability, and address validation. They also fail closed.

## OAuth Foundation

`IFedExOAuthClient` and `FedExOAuthClient` define the official authentication contract surface only.

The abstraction models:

- Client ID / API key
- Client secret / secret key
- Grant type
- Optional child key / child secret
- Token result fields: access token, token type, expiration, scope

`FedExOAuthClient.GetAccessTokenAsync` does not request a token. It returns `FEDEX_NOT_YET_CERTIFIED`.

## Endpoint Configuration

`FedExOptions` freezes the official endpoint surface from FEDEX-1:

| Concern | Official configuration |
|---|---|
| Sandbox base URL | `https://apis-sandbox.fedex.com/` |
| Production base URL | `https://apis.fedex.com/` |
| OAuth token | `POST /oauth/token` |
| Token lifetime | `3600` seconds |
| Create shipment | `POST /ship/v1/shipments` |
| Validate shipment | `POST /ship/v1/shipments/packages/validate` |
| Retrieve async shipment | `POST /ship/v1/shipments/results` |
| Cancel shipment | `PUT /ship/v1/shipments/cancel` |
| Track by tracking number | `POST /track/v1/trackingnumbers` |
| Pickup availability | `POST /pickup/v1/pickups/availabilities` |
| Create pickup | `POST /pickup/v1/pickups` |
| Cancel pickup | `PUT /pickup/v1/pickups/cancel` |
| Service/package options | `POST /availability/v1/packageandserviceoptions` |
| Special service options | `POST /availability/v1/specialserviceoptions` |
| Transit times | `POST /availability/v1/transittimes` |
| Address validation | `POST /address/v1/addresses/resolve` |

These are configuration constants only. No HTTP method execution is implemented.

## Capability Matrix

| Operation | Foundation status |
|---|---|
| Shipment creation | Fail closed |
| Tracking | Fail closed |
| Labels | Fail closed |
| Cancellation | Fail closed |
| Pickup | Fail closed |
| Service availability | Fail closed |
| Address validation | Fail closed |
| Webhooks | Fail closed by returning no parse result |
| OAuth token request | Fail closed |
| HTTP execution | Not implemented |

## Fail Closed Strategy

Every unsupported FedEx operation returns one of the following outcomes:

- `FEDEX_NOT_YET_CERTIFIED`
- `null` webhook parse result for unadmitted webhook payloads
- empty service levels for runtime-advertised service options
- `null` carrier-request lookup result to prevent duplicate reconstruction

The adapter does not accept guessed payloads, dummy responses, fake labels, fake tracking, fabricated service levels, or inferred webhook events.

## Regression Tests

Updated and added coverage in:

- `PointOfSale.Tests/Architecture/FedExOfficialContractCompatibilityTests.cs`
- `PointOfSale.Tests/Architecture/CarrierIntegrationAuthorityCertificationTests.cs`

Verified:

- FedEx carrier code registration
- DI registration for `FedExOptions`, `IFedExOAuthClient`, `FedExOAuthClient`, and `FedExAdapter`
- Registry discovery through `ShippingCarrierRegistry`
- Capability declaration
- Official sandbox and production endpoint configuration
- OAuth configuration surface
- Fail-closed adapter operations
- No `HttpClient`, `IHttpClientFactory`, or `SendAsync` dependency in FedEx adapter/OAuth foundation

## Verification

Command:

```powershell
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~FedExOfficialContractCompatibilityTests|FullyQualifiedName~CarrierIntegrationAuthorityCertificationTests" -nologo -v minimal
```

Result: PASS, 14 passed, 0 failed.

Existing unrelated compiler/analyzer warnings remain.

## Final Recommendation

Proceed next to a FedEx request-contract compatibility phase that freezes field-level request/response schemas from official FedEx documentation or portal collections. Do not activate production FedEx traffic until OAuth execution, request serialization, response admission, lifecycle/status mapping, labels, cancellation, pickup, service availability, address validation, webhook trust, geography, customs, operational credentials, and sandbox/live smoke tests are separately certified.

## Explicit Success Answers

- Reviewed ONLY FedEx Runtime Foundation? Yes.
- Runtime implemented? Yes, foundation only.
- Carrier registered? Yes.
- DI registered? Yes.
- OAuth surface created? Yes.
- HTTP implemented? No.
- Shipment implemented? No.
- Tracking implemented? No.
- Labels implemented? No.
- Fail Closed preserved? Yes.
- Backward compatible? Yes; existing carriers remain unchanged.
- Production active? No.
- Recommend next phase: FedEx request-contract compatibility and field-level schema freeze before any HTTP runtime implementation.
