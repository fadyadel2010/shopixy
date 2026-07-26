# BOSTA-2 — Request Contract Compatibility Audit

Audit date: 5 July 2026  
Provider: Bosta only  
Status: COMPLETE

## Executive Summary

The Bosta request surface is **not production compatible**.

Tracking, cancellation, and credential validation are bodyless and remain request-contract compatible after the endpoint corrections frozen by BOSTA-1. Shipment creation contained one HIGH contract incompatibility: Bosta requires `dropOffAddress.districtId`, or both `dropOffAddress.cityId` and `dropOffAddress.districtName`. Runtime instead emitted undocumented `district` and `zone` properties populated with the recipient city.

Minimum hardening now models and serializes the official identifier properties, removes the unknown properties, and fails before the HTTP call when neither official address form is available. The normal `CarrierShipmentService` builders still possess only free-text city/address data, so they cannot create a Bosta-compatible request without a new authoritative city/district capture or resolution path.

Compatibility is **75% (3 of 4 implemented outbound request operations)**. Create is not counted compatible because the production builders cannot currently satisfy its required address identity.

## Reviewed Scope

Reviewed only outbound request generation:

- `BostaAdapter`
- `CarrierShipmentService` create-request builders
- carrier-neutral request DTOs
- Bosta request DTO mapping
- `HttpRequestMessage` and `JsonContent` generation
- serializer options
- bodyless tracking, cancellation, and cities requests
- Bosta-relevant carrier configuration

Excluded: responses, response DTO admission, webhooks, callbacks, status mapping, retry, security, performance, authority, lifecycle, and business behavior.

No `docs/architecture/SF-SHP*` files existed at audit time.

## Official Request Contracts

Official authority:

- Bosta published OpenAPI 3.0.1, version `2.0.0`: `https://docs.bosta.co/api/api.yaml`
- Bosta Create Your First Delivery guide
- Bosta Format Bosta Address guide

Implemented operations:

| Operation | Request contract |
|---|---|
| Create | `POST /deliveries?apiVersion=1`, JSON body `BusinessadddeliveryRequest` |
| Track | `GET /deliveries/business/{trackingNumber}`, no body |
| Cancel | `DELETE /deliveries/business/{trackingNumber}/terminate`, no body |
| Validate reachability | `GET /cities`, no body |

Create requires top-level `type` and `cod`. For delivery type `10`, `dropOffAddress` requires `city`, `firstLine`, and either `districtId` or the pair `cityId` + `districtName`. `receiver` requires `firstName` and `phone`.

## Runtime Request Contracts

Create serializes camelCase JSON with null properties omitted. Runtime supplies:

- `type: 10`
- `specs.packageType: "Parcel"`
- `specs.size: "SMALL"`
- `specs.packageDetails.itemsCount`
- `specs.packageDetails.description`
- `notes`
- numeric `cod`
- `dropOffAddress`
- `receiver`
- `businessReference`
- `allowToOpenPackage: true`
- optional `geoLocation`

Tracking, cancellation, and validation send no request body.

## Request Comparison

| Difference | Before hardening | Official | Severity | Result |
|---|---|---|---|---|
| District identity | `district: <city>` | `districtId`, or `cityId` + `districtName` | HIGH | Unknown field removed; official fields modeled |
| Zone identity | `zone: <city>` | optional `zoneId` | MEDIUM | Corrected to `zoneId` |
| Required address identity availability | No ID/name pair exists in production builder | Required by address schema | HIGH | Fail-closed guard added; production data gap remains |

No request dates, times, arrays, currency property, postal code, weight, dimensions, metadata object, or request-body reference-number arrays are emitted by the implemented create call.

## Serialization Review

| Concern | Runtime | Verdict |
|---|---|---|
| Encoding | `JsonContent.Create` uses JSON UTF-8 content | Compatible |
| Media type | `application/json` | Compatible |
| Naming | Explicit `JsonPropertyName` plus camelCase policy | Compatible |
| Case | Official camelCase names | Compatible after hardening |
| Nulls | `WhenWritingNull` | Compatible for optional properties |
| Numbers | .NET JSON numeric tokens for `decimal`/`int` | Compatible |
| Booleans | JSON booleans | Compatible |
| Dates/times | None emitted | Not applicable |
| Unknown fields | Legacy `district` and `zone` | Removed |
| Size limit | No public Bosta request-size limit was found | Documentation gap |

## Validation Rules

Runtime now refuses create before network I/O unless:

- `RecipientDistrictId` is present; or
- both `RecipientCityId` and `RecipientDistrictName` are present.

It returns `BOSTA_ADDRESS_IDENTITY_REQUIRED`. This prevents sending a predictably invalid payload but does not manufacture or resolve provider identifiers.

Official constraints not locally enforced include COD maximum 30,000 EGP, `firstLine` longer than five characters, supported city/district membership, and district drop-off availability. These remain provider-side validation risks.

## Field Compatibility Matrix

| Field | Official | Runtime after hardening | Verdict |
|---|---|---|---|
| `type` | required number; delivery `10` | `10` | Compatible |
| `cod` | required number; max 30,000 EGP | decimal; zero for non-COD | Structurally compatible; limit not frozen |
| `specs.packageType` | optional documented values | `Parcel` | Compatible |
| `specs.size` | optional documented values | `SMALL` | Compatible |
| `packageDetails.itemsCount` | optional number | integer | Compatible |
| `packageDetails.description` | optional string | string | Compatible |
| `notes` | optional string | empty string when absent | Compatible, unnecessary default |
| `businessReference` | optional string | stable internal request ID | Compatible |
| `allowToOpenPackage` | optional boolean | always `true` | Compatible contract; policy not reviewed |
| `receiver.firstName` | required string | derived from name | Compatible |
| `receiver.lastName` | optional string | empty string for one-part name | Compatible, unnecessary default |
| `receiver.phone` | required string | normalized Egyptian local digits | Compatible with official examples; no formal regex published |
| `receiver.email` | optional string | omitted when null | Compatible |
| `goodsInfo` | optional, recommended for insurance | omitted | MEDIUM optional omission |
| `uniqueBusinessReference` | optional unique string | omitted | LOW optional omission |
| `businessLocationId` | optional for delivery; default location otherwise | omitted | Compatible |

## Enum Compatibility

- `type = 10`: compatible with Deliver.
- `packageType = "Parcel"`: compatible.
- `size = "SMALL"`: compatible.
- No serialized string enum converter is involved.

## Address Compatibility

The official address formats are:

1. `city` + `firstLine` + `districtId`, with optional `zoneId`; or
2. `city` + `firstLine` + `cityId` + `districtName`, with optional `zoneId`.

The Bosta DTO can now represent both. Production builders populate neither because `Order` and `Shipment` retain free-text city/address but no Bosta city/district IDs or district name. Therefore normal create requests fail locally and production create remains unavailable.

Country and postal code are not properties in the official create-delivery address schema and are correctly not serialized. Coordinates are emitted as `geoLocation`; this property is not declared by the current `AddressCreateDelivery` OpenAPI schema and is therefore a LOW unknown optional-field risk. It was left unchanged because Bosta's public schema/documentation is inconsistent and no rejection evidence was established.

## Weight & Dimension Compatibility

Runtime does not serialize `WeightKg` or dimensions. The official general delivery schema makes `specs.weight` optional and declares no dimension object. This is compatible, though weight information is lost.

## Date & Time Compatibility

No create, track, cancel, or cities request emits a date or time. Date, time, and ISO formatting are not applicable.

## Breaking Changes

Breaking request difference found: **YES**.

The pre-hardening address object lacked an officially required identifier form and contained undocumented property names. Bosta documents error codes `3001`–`3004` for invalid city/zone/district/street identity and `3009` for missing city/zone/district identity. No public changelog establishes when this became breaking.

## Runtime Hardening

Modified:

- `PointOfSale.Infrastructure/Shipping/IShippingCarrierAdapter.cs`
- `PointOfSale.Infrastructure/Shipping/Adapters/BostaAdapter.cs`

Changes:

- added carrier-neutral recipient city/district/zone identifier inputs;
- serialized `cityId`, `districtId`, `districtName`, and `zoneId`;
- removed undocumented `district` and `zone`;
- added fail-closed address identity validation.

No refactor, response change, webhook change, lifecycle change, frontend change, or unrelated optimization was made.

## Regression Tests

Updated `PointOfSale.Tests/Infrastructure/BostaEndpointSurfaceTests.cs`.

Two focused tests were added:

- official address identifier names serialize and legacy names do not;
- missing official address identity prevents outbound network I/O.

The existing four endpoint tests continue to pass.

## Verification

Command:

`dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter FullyQualifiedName~BostaEndpointSurfaceTests`

Result: **PASS — 6 passed, 0 failed, 0 skipped**.

The command successfully built Core, Infrastructure, API, and Tests. Existing unrelated warnings remain. No live Bosta call was made.

## Explicit Compatibility Answers

- Missing required fields sent outbound after hardening? **NO**; incomplete create requests fail locally.
- Can the normal production builder create a valid Bosta shipment request? **NO**; it does not possess district ID or city-ID/district-name data.
- Unexpected fields? **LOW residual risk:** optional `geoLocation` is absent from the current published address schema.
- Deprecated fields? **No official deprecation register exists.**
- Wrong enums/casing/number/boolean serialization? **NO evidence found.**
- Wrong nesting? **Address nesting is corrected.**
- Wrong identifiers? **Pre-hardening YES; after hardening identifiers must be explicitly supplied.**
- Wrong phone formatting? **No proven mismatch; official examples accept Egyptian local format.**
- Wrong address mapping? **Pre-hardening YES; serialization fixed, upstream data remains unavailable.**
- Tracking/cancel/validation request contracts compatible? **YES; they are bodyless.**
- Create request contract production compatible? **NO.**

## Compatibility Percentage

**75% — 3 of 4 implemented outbound request operations are production-compatible.**

Create can serialize a compatible request when supplied official address identifiers, but the production service builders do not supply them, so it is not credited.

## Final Verdict

**PRODUCTION COMPATIBLE: NO**

Evidence: Bosta's current OpenAPI and address guide require `districtId`, or `cityId` plus `districtName`. The production runtime stores and maps only free-text city/address. Minimum hardening prevents invalid outbound requests, but a future scoped change must capture or authoritatively resolve Bosta city/district identity before shipment creation.

## Completion Summary

- Runtime files modified: **2**
- Backend files modified: **2**
- Frontend files modified: **0**
- Tests added: **2**
- Compatibility: **75%**
- Breaking Changes Found: **YES — 1 HIGH address-contract break**
- Runtime Hardening Required: **YES — minimum fail-closed hardening completed; upstream identifier support remains required**
- Production Compatible: **NO**

## Next Recommended Certification

BOSTA-3

Response & DTO Compatibility Audit

Status

READY
