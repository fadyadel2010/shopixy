# FEDEX-3 - Geographic Compatibility

Date: 2026-07-07
Scope: FedEx geographic compatibility only
Status: COMPLETE

## Executive Summary

FedEx geographic requirements are fully modeled by the certified Canonical
Geographic Platform and frozen Shipping Platform.

FedEx does not require a new Geographic domain. FedEx requires shipper and
recipient address fields, service-availability address evidence, and optional
Address Validation evidence. Those map to existing platform concepts:

- Shipment Origin Snapshot
- Shipment Destination Snapshot
- Carrier Mapping Snapshot
- Canonical Country
- Canonical Administrative Area
- Canonical Locality
- Canonical Postal Code
- Canonical Street Address
- Canonical Address Evidence

One FedEx-specific request-contract compatibility issue was found and hardened:
the standalone FedEx contract layer treated postal code as universally required
for FedEx Address Validation. That conflicts with the platform rule that postal
requirements are country-profile authority and with FedEx Address Validation as
suggestion/evidence only. The fix is limited to FedEx contract validation:
Address Validation accepts street/country evidence without turning postal code
or provider-standardized output into canonical truth. Shipment, pickup, and
service-availability DTO validation remain fail-closed under their existing
required address shape.

No Geography redesign, Shipping Platform redesign, Carrier Mapping redesign,
runtime execution, HTTP transport, OAuth execution, frontend change, SQL
authority change, or adapter business logic was introduced.

## Official Geographic Contract

Official FedEx sources reviewed:

- FedEx Ship API documentation:
  https://developer.fedex.com/api/en-us/catalog/ship/v1/docs.html
- FedEx Address Validation API documentation:
  https://developer.fedex.com/api/en-us/catalog/address-validation/v1/docs.html
- FedEx Service Availability API documentation:
  https://developer.fedex.com/api/en-us/catalog/service-availability/v1/docs.html

Official FedEx geographic/address fields represented in the contract layer:

| FedEx field | Platform source |
|---|---|
| `streetLines` | Canonical street address snapshot |
| `city` | Canonical locality display snapshot |
| `stateOrProvinceCode` | Canonical administrative-area snapshot, serialized as provider field |
| `postalCode` | Canonical postal code under country profile policy |
| `countryCode` | Canonical country ISO-2 snapshot |
| `residential` | Provider/address evidence only |

FedEx Ship API requires shipper and recipient information for shipment create
and validation. FedEx also documents that shipment validation does not validate
street address details. FedEx Service Availability validates service/package
availability from shipment and recipient address inputs and can reject invalid
City, State/Province, and ZIP/Postal combinations.

## Origin Compatibility

FedEx consumes only the existing `ShipmentOriginDto` carried by
`CreateShipmentRequest.Origin`.

Origin authority remains:

- Warehouse-owned admitted address evidence.
- Shipment-owned immutable origin snapshot.
- Carrier-neutral DTO passed to the adapter.
- Snapshot-only recovery through `OriginSnapshotJson`.

FedEx owns no warehouse, country, locality, postal, origin, or address truth.
The current FedEx adapter remains fail-closed and contains no origin lookup,
normalization, SQL read, catalog read, mapping read, or repair path.

## Destination Compatibility

FedEx consumes only the shipment destination snapshot copied from the admitted
order destination.

Destination serialization uses the existing carrier-neutral create request:

- `RecipientAddress` from shipment destination street snapshot.
- `RecipientAddressLine2` from destination street line 2.
- `RecipientCity` from destination locality display snapshot.
- `RecipientStateOrProvinceCode` from administrative-area display snapshot.
- `RecipientCountry` from resolved canonical country ISO-2.
- `RecipientZipCode` from the persisted order/shipment postal evidence.

FedEx does not read Checkout, Customer Address, live Geographic Catalog,
provider responses, Redis, Mongo, browser state, aliases, or current catalog
names to create destination geography.

## Carrier Mapping Compatibility

FedEx is registered as a provider identity, so the existing GEO-9 carrier
geographic resolution boundary applies before any future FedEx external
registration.

FedEx consumes only:

- Published Carrier Mapping.
- Shipment Carrier Mapping Snapshot.
- Provider IDs resolved by `CarrierGeographicMappingService`.
- Carrier Mapping Release evidence.

Provider IDs remain isolated to Shipment registration evidence. They are not
present in FedEx request-contract DTOs, Orders, Checkout DTOs, Customer
Addresses, address books, carrier configuration, or generic addresses.

## Address Validation Boundary

FedEx Address Validation is treated only as:

- Provider Evidence.
- Address Suggestion.
- Validation Evidence.

It must never become:

- Canonical Geographic Truth.
- Customer Address Authority.
- Shipment Address Authority.
- SQL Authority.

The FedEx contract hardening keeps Address Validation from imposing universal
postal-code authority and from promoting provider-normalized output into
canonical address admission. Any future FedEx Address Validation response must
be admitted only as evidence/suggestion and must re-enter the canonical
Geographic Address Admission boundary before durable use.

## Country Rules

FedEx `countryCode` maps to canonical ISO-2 country from the admitted platform
snapshot. Unknown, unsupported, inactive, unpublished, or non-ISO country
evidence fails closed before provider I/O through the existing Geographic
country profile and shipment snapshot boundaries.

FedEx owns no Country Catalog and cannot introduce country support by adapter
logic.

## Postal Rules

Canonical postal policy remains Geographic Country Profile authority:

- Required postal code: enforced by Geographic admission/profile policy.
- Optional postal code: preserved when present.
- Not-used/forbidden postal code: not invented by FedEx.
- Invalid postal schema: fails closed before accepted shipment authority.

FedEx Service Availability may reject invalid City, State/Province, and
ZIP/Postal combinations as provider evidence. That provider rejection does not
rewrite canonical postal truth.

## Provider Mapping

FedEx provider geographic identifiers, if future official contract evidence
requires them, must enter only through published carrier mapping releases and
shipment registration mapping snapshots.

No FedEx provider place ID is stored or serialized today. No FedEx adapter code
queries Geographic SQL, aliases, hierarchy, localization, mapping releases, or
sync candidates.

## Geographic Authority Matrix

| Concern | Authority | FedEx role |
|---|---|---|
| Country Catalog | Geographic Platform | None |
| Administrative Hierarchy | Geographic Platform | None |
| Warehouse | Warehouse domain | None |
| Customer Address | Customer/Checkout domains plus Geographic admission | None |
| Carrier Mapping | Published Geographic mapping releases | Consumer only |
| Shipment Origin | Shipment origin snapshot | Consumer only |
| Shipment Destination | Shipment destination snapshot | Consumer only |
| Address Truth | Geographic admission and owning business snapshot | None |
| SQL Truth | PostgreSQL platform authority | None |
| Provider Address Validation | FedEx provider evidence | Suggestion/evidence only |

## Breaking Issues

| Issue | Severity | Status |
|---|---:|---|
| FedEx runtime remains fail-closed; no shipment HTTP execution exists | Blocking for FedEx production activation | Accepted from FEDEX-RUNTIME-1/FEDEX-2 |
| FedEx Address Validation contract validation required postal code universally | Medium geographic compatibility issue | Fixed in FedEx contract layer |
| No published FedEx provider mapping coverage is proven | Blocking for production external registration | Operational/data rollout issue |
| FedEx provider IDs are not currently required by implemented contract layer | None | Existing carrier mapping boundary still applies if future official fields require IDs |

## Regression Coverage

Updated:

- `PointOfSale.Tests/Architecture/FedExOfficialContractCompatibilityTests.cs`
- `PointOfSale.Tests/Architecture/CarrierGeographicResolutionIntegrationTests.cs`

Coverage freezes:

- Origin Snapshot consumption.
- Destination Snapshot consumption.
- Carrier Mapping consumption before adapter invocation.
- Provider ID isolation to Shipment evidence.
- Address Validation as provider evidence only.
- Country, state/province, postal, and street serialization.
- No Geographic authority, SQL access, mapping lookup, or business logic inside
  the FedEx adapter.

## Verification

Focused verification command:

```powershell
dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~FedExOfficialContractCompatibilityTests|FullyQualifiedName~CarrierGeographicResolutionIntegrationTests" -nologo -v minimal
```

Result: PASS, 26 passed, 0 failed.

Existing unrelated compiler/analyzer warnings remain.

## Final Recommendation

Proceed to the next FedEx phase only after this geographic boundary remains
green. The recommended next phase is FedEx runtime authority / adapter execution
planning, still fail-closed, with no production activation until provider
mapping coverage, origin coverage, destination coverage, OAuth execution,
request serialization admission, response admission, status mapping, labels,
customs where applicable, and sandbox/live smoke tests are separately
certified.

## Explicit Success Answers

- Reviewed ONLY FedEx Geography? **Yes.**
- Runtime modified? **No FedEx runtime execution modified; contract validation
  only.**
- Backend modified? **Yes, FedEx contract validation and architecture tests
  only.**
- Frontend modified? **No.**
- Origin Snapshot consumed? **Yes.**
- Destination Snapshot consumed? **Yes.**
- Carrier Mapping consumed? **Yes, through existing GEO-9 boundary before
  adapter invocation.**
- Address Validation treated as Provider Evidence only? **Yes.**
- Business logic inside adapter? **No.**
- SQL inside adapter? **No.**
- Lossless geographic serialization? **Yes for canonical snapshot-to-contract
  fields currently modeled: street, locality/city, administrative area,
  country ISO-2, and postal evidence.**
- Provider IDs leaked outside Shipment? **No.**
- Production compatible? **No. FedEx runtime remains fail-closed and published
  FedEx mapping/operational coverage is not proven.**
- Does FedEx require any new Geographic Platform capability? **No. The
  certified Geographic Platform fully models FedEx geographic requirements.**
- Recommend next phase: **FedEx runtime authority / adapter execution planning
  with credentialed operational proof later; do not redesign Geography.**

## AI Context Update Decision

No `.ai-context` files were updated. FEDEX-3 found and fixed a FedEx-specific
contract-boundary issue only. It did not discover new permanent cross-provider
architectural truth.
