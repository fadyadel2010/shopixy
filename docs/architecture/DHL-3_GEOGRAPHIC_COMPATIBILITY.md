# DHL-3 - Geographic Compatibility

Date: 2026-07-06  
Scope: DHL geographic consumption only  
Governing architecture: ADR-GEO-001, GEO-1.5 through GEO-10, SHIP-ORIGIN-1, DHL-1, and DHL-2

## Executive Summary

DHL geographic consumption is structurally aligned after one runtime hardening change.

`CarrierShipmentService` constructs the carrier-neutral `CreateShipmentRequest` only after acquiring the Shipment Origin Snapshot, Destination Geographic Snapshot, Carrier Mapping Snapshot, and Customs Snapshot when required. The DHL adapter does not query SQL, use repositories, call Geographic services, resolve mappings, inspect catalog rows, or serialize provider geographic identifiers.

One DHL geographic authority violation was found and fixed: `ValidateCredentialsAsync` performed a MyDHL `address-validate` probe using hard-coded Egypt country/postal data. That was a provider geographic lookup and guessed geographic evidence inside the adapter. Credential validation now checks only credential completeness locally and performs no geographic network probe.

## Canonical Geography Consumption

The approved source chain remains:

```text
Shipment Origin Snapshot
Shipment Destination Geographic Snapshot
Carrier Mapping Snapshot
    -> CreateShipmentRequest
    -> DHL adapter serialization
    -> MyDHL POST /shipments
```

DHL receives carrier-neutral DTO fields. The adapter is a wire serializer and does not own origin, destination, provider mapping, place identity, hierarchy, country support, postal rules, or address admission.

## Origin Consumption

Origin is resolved before adapter invocation by `ResolveOrRestoreShipmentOriginAsync`.

The request fields sent to DHL are populated from `ShipmentOriginDto`:

| DHL request fact | Carrier-neutral source |
|---|---|
| shipper name/company | `origin.ContactName` |
| shipper street | `origin.Street` |
| shipper city/locality text | `origin.LocalityDisplayName`, then administrative area/country display fallback |
| shipper country | `origin.CountryIso2` |
| shipper phone | `origin.ContactPhone` |
| origin DTO | `Origin = origin` |

Carrier configuration pickup text is not used for DHL origin construction.

## Destination Consumption

Destination is copied from the immutable shipment destination snapshot before carrier invocation.

The request fields sent to DHL are populated from shipment snapshot columns:

| DHL request fact | Carrier-neutral source |
|---|---|
| receiver street | `shipment.DestinationStreet` |
| receiver address line 2 | `shipment.DestinationStreet2` |
| receiver city/locality | `shipment.DestinationLocalityDisplayName` |
| receiver state/province display | `shipment.DestinationAdministrativeAreaDisplayName` |
| receiver country | resolved carrier mapping country ISO-2 from `geographicResolution.CountryIso2` |
| receiver postal code | shipment/order persisted shipping zip evidence |
| receiver name/phone/email | shipment/order recipient evidence |

The DHL adapter does not look up, normalize, or guess any destination place.

## Carrier Mapping Consumption

Carrier mapping is resolved by `CarrierGeographicMappingService` through `CarrierShipmentService` before adapter invocation and persisted as `CarrierGeographicMappingSnapshotJson`.

DHL does not currently serialize provider geographic identifiers in its MyDHL shipment request. That is compatible only because the certified MyDHL request shape uses postal address text and ISO country fields rather than provider place IDs. Provider mapping evidence is still required before external carrier registration by the platform boundary and remains shipment-owned forensic evidence.

Provider IDs do not originate in DHL and do not leak outside the Shipment mapping snapshot.

## Serialization Matrix

| Geographic/address field | DHL serialization | Source authority | Notes |
|---|---|---|---|
| Country | `countryCode` | origin snapshot ISO-2 and resolved destination country ISO-2 | No hard-coded country remains in DHL credential validation. |
| Province / State | receiver address state/province text where applicable | destination snapshot administrative-area display | DHL request model currently omits a dedicated shipper state field. |
| City | `cityName` | origin snapshot display, destination snapshot locality display | No city lookup or guessing inside DHL. |
| District | not serialized as a dedicated DHL field | destination snapshot evidence retained by shipment | MyDHL request shape inspected here has no dedicated district provider ID field. |
| Postal Code | `postalCode` | persisted shipment/order postal evidence | DHL does not invent postal code. |
| Street | `addressLine1` | origin snapshot street, destination snapshot street | No normalization inside DHL. |
| Building | not serialized separately | admitted snapshot evidence where modeled | No invented building value. |
| Coordinates | not serialized | admitted snapshot evidence where modeled | DHL adapter does not use coordinates. |
| Address Lines | `addressLine1`, `addressLine2` | origin/destination snapshot detail | `addressLine3` remains unused. |
| Recipient | contact `fullName`, `companyName` | shipment/order recipient evidence | No lookup. |
| Phone | contact `phone` | origin snapshot and shipment/order recipient evidence | No normalization inside DHL. |
| Provider IDs | not serialized by DHL | Carrier Mapping Snapshot only | DHL adapter does not read `ResolvedProviderGeography`. |

## Contract Compatibility

The DHL MyDHL v3.3.1 shipment-create address contract is compatible with current DHL serialization for the certified subset:

- postal address fields are populated from admitted shipment evidence;
- ISO country codes are supplied by origin/destination resolution boundaries;
- no provider geographic ID is required by the implemented DHL request shape;
- international customs serialization remains governed by DHL-2 and is outside this geographic audit except for country-code validation.

Credential validation no longer uses the MyDHL `address-validate` endpoint because that introduced hard-coded geography into the adapter.

## Breaking Issues

Found and fixed:

1. `DhlAdapter.ValidateCredentialsAsync` called `address-validate?countryCode=EG&postalCode=11511`.
2. That call was a geographic provider lookup inside the adapter.
3. The hard-coded Egypt/postal evidence violated the DHL-3 no-guessing and no-adapter-geographic-lookup rules.

No other DHL geographic authority violation was found.

## Runtime Hardening

Modified:

- `PointOfSale.Infrastructure/Shipping/Adapters/DhlAdapter.cs`
- `PointOfSale.Tests/Infrastructure/DhlOfficialContractCompatibilityTests.cs`
- `PointOfSale.Tests/Architecture/DhlGeographicCompatibilityTests.cs`

Change:

- removed DHL credential-validation geographic provider lookup;
- credential validation now fails closed on missing MyDHL key, secret, or account number;
- credential validation now returns structural success without network I/O when credential fields are complete;
- added regression coverage proving DHL does not perform address validation/geographic lookup and does not serialize provider geographic identifiers.

No Shipping redesign, Geography redesign, Carrier Resolution redesign, Warehouse redesign, Checkout redesign, Customs redesign, Product redesign, POS Admin, EF migration, SQL change, or frontend work was performed.

## Regression Tests

Regression coverage freezes:

- DHL adapter has no `ApplicationDbContext`, Geographic service, mapping service, catalog, admission, SQL, or `address-validate` dependency;
- DHL create request is constructed only after origin and carrier geographic resolution;
- origin request fields come from `ShipmentOriginDto`;
- destination request fields come from shipment destination snapshot fields;
- destination country comes from resolved carrier geography evidence;
- provider geographic identifiers are not serialized by DHL;
- DHL credential validation performs no geographic address lookup.

## Verification

Executed:

- `dotnet build PointOfSale.sln --no-restore`: PASS, 0 warnings, 0 errors.
- `dotnet build PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore`: PASS, existing warnings, 0 errors.
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter "FullyQualifiedName~DhlOfficialContractCompatibilityTests|FullyQualifiedName~DhlGeographicCompatibilityTests" -nologo -v minimal --no-build --no-restore`: PASS, 14/14.
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter FullyQualifiedName~Geographic -nologo -v minimal --no-build --no-restore`: PASS, 51/51.
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter FullyQualifiedName~Shipping -nologo -v minimal --no-build --no-restore`: 82 passed, 5 failed.

The five Shipping-filter failures match existing non-DHL-geography failures already documented in DHL-2:

- `ShippingWebhookTrustPipelineTests.ProcessWebhookAsync_ValidSignature_ProcessesWebhook_AndPersistsCompletedLog`;
- `ShippingWebhookTrustPipelineTests.ProcessWebhookAsync_UppercaseHexSignature_IsAcceptedCaseInsensitively`;
- `CommerceHubFinanceServiceTests.CreateRefundAsync_ShippingOnlyRefund_ShouldNotReverseCommission`;
- `ShipmentExternalRegistrationSafetyTests.ExecuteCarrierForExistingShipmentAsync_RetryAfterSuccess_DoesNotCreateDuplicateCarrierShipment`;
- `ShipmentExternalRegistrationSafetyTests.ExecuteCarrierForExistingShipmentAsync_WhenExternalSuccessWasNotPersisted_ReconcilesByCarrierRequestIdWithoutDuplicateCreate`.

## Risks

- DHL live sandbox replay was not performed in this phase.
- DHL production activation still depends on provider-country/account coverage and mapping rollout evidence from GEO-10.
- DHL adapter credential validation is now structural only; a future DHL credential-certification phase should use an official non-geographic account/credential endpoint if DHL publishes one.
- MyDHL may impose account/country/postal/product constraints that require live certification; DHL-3 does not infer those rules inside the adapter.

## Final Recommendation

Approve DHL-3 geographic compatibility after the hardening change.

DHL is production-compatible for geographic authority consumption as a serializer of admitted shipment evidence, conditional on the existing broader blockers: DHL sandbox/live contract proof, provider-country/account coverage, and published carrier mapping rollout evidence.

Recommended next phase: DHL credentialed sandbox certification and provider-country rollout, limited to live MyDHL replay and coverage proof. Do not redesign Geography, Shipment, Carrier Resolution, Warehouse, Checkout, Customs, Product, or POS Admin.

## Explicit Success Answers

- Reviewed ONLY DHL Geography? **Yes.**
- Runtime modified? **Yes, DHL credential-validation hardening only.**
- Backend modified? **Yes, DHL adapter plus focused backend/architecture tests.**
- Frontend modified? **No.**
- Origin Snapshot consumed? **Yes.**
- Destination Snapshot consumed? **Yes.**
- Carrier Mapping Snapshot consumed? **Yes, before DHL adapter invocation; DHL does not serialize provider IDs.**
- Business logic inside adapter? **NO.**
- SQL inside adapter? **NO.**
- Lossless geographic serialization? **Yes for DHL-supported modeled address fields; unsupported dedicated district/building/coordinate fields are not guessed.**
- Provider IDs leaked outside Shipment? **NO.**
- Production compatible? **Yes for DHL geographic authority consumption; broader DHL production rollout remains conditional on sandbox/account/country/mapping evidence.**
- Recommend next phase: **DHL credentialed sandbox certification and provider-country rollout.**

## AI Context Update Decision

No `.ai-context` files were found in the workspace. No architecture-truth context files were updated. DHL-3 discovered a provider-specific runtime authority violation and hardening, not a new permanent cross-system architecture truth.
