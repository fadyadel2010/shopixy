# DHL-2 - International Request Contract and Customs Integration

Date: 2026-07-06  
Scope: DHL international shipment request construction only  
Official authority: DHL Express MyDHL REST API v3.3.1, released 2026-06-28

## Executive Summary

DHL international shipment construction previously failed closed for every international shipment, even when `CreateShipmentRequest.Customs` contained the canonical `CarrierNeutralCustomsDto`. That was safe under DHL-1, but it became stale after the Customs Platform runtime was implemented.

Runtime hardening was applied only inside the DHL adapter. International DHL create requests now consume the admitted carrier-neutral Customs DTO, validate snapshot completeness and version, and serialize item-level customs data into `content.exportDeclaration.lineItems` before the MyDHL `POST /shipments` call.

Domestic DHL shipment creation remains unchanged except for preserving prior DHL-1 hardening: no customs declaration is emitted for domestic traffic, and optional declared value remains omitted when absent.

## Canonical Customs Consumption

Approved source chain:

```text
Shipment Customs Snapshot
  -> CarrierNeutralCustomsDto
  -> DHL request serialization
  -> MyDHL POST /shipments
```

The DHL adapter consumes only `CreateShipmentRequest.Customs` for international customs fields. It does not read Product, Checkout, Order, SQL, carrier metadata, provider responses, or configuration to fill HS code, commodity, value, origin country, manufacture country, quantity, weight, units, or currency.

Fail-closed DHL validation now rejects before provider I/O when:

- Customs DTO is missing or has no lines;
- snapshot IDs are empty;
- snapshot version or DTO version is not `1`;
- origin or destination country is not ISO-2 shaped;
- shipment gross weight is missing or uses an unsupported weight unit;
- line HS code, commodity, country of origin, declared value, currency, quantity, or net weight is missing or invalid;
- country of manufacture is present but not ISO-2 shaped;
- line currencies differ;
- quantity or weight units cannot be mapped to admitted DHL serialization values.

## Request Construction

International DHL creation now builds:

- `content.isCustomsDeclarable = true`;
- `content.declaredValue` from the canonical customs DTO line declared values;
- `content.declaredValueCurrency` from the canonical customs DTO line currency after enforcing one currency across lines;
- `content.exportDeclaration.lineItems[]` from admitted snapshot lines only;
- item sequence numbers generated from DTO line order for provider array serialization only.

Domestic DHL creation continues to build:

- `content.isCustomsDeclarable = false`;
- no `exportDeclaration`;
- no declared value or declared value currency unless the existing domestic request explicitly supplies a declared value.

## Serialization Matrix

| DHL field | Source | Serialization | Fail-closed behavior |
|---|---|---|---|
| `content.isCustomsDeclarable` | Origin/destination country comparison | Boolean | Domestic unchanged; international requires customs DTO |
| `content.declaredValue` | `CarrierNeutralCustomsDto.Lines[].DeclaredValue` | JSON decimal number | Missing/non-positive line value rejected |
| `content.declaredValueCurrency` | `CarrierNeutralCustomsDto.Lines[].Currency` | ISO currency string | Missing/invalid/mixed currency rejected |
| `content.exportDeclaration.lineItems[].number` | DTO line order | 1-based integer | Serializer-owned array sequence only |
| `lineItems[].description` | `CommodityDescription` | String | Missing/blank rejected |
| `lineItems[].price` | `DeclaredValue` | JSON decimal number | Missing/non-positive rejected |
| `lineItems[].quantity.value` | `Quantity` | JSON decimal number | Missing/non-positive rejected |
| `lineItems[].quantity.unitOfMeasurement` | `QuantityUnit` | DHL unit code, currently `PCS` for piece units | Unknown units rejected |
| `lineItems[].commodityCodes[].typeCode` | DHL contract constant | `outbound` | No customs authority implied |
| `lineItems[].commodityCodes[].value` | `HsCode` | String | Missing/blank rejected |
| `lineItems[].manufacturerCountry` | `CountryOfManufactureIso2` | ISO-2 string, omitted when null | Invalid present value rejected |
| `lineItems[].weight.netValue` | `NetWeight` | JSON decimal number | Missing/non-positive rejected |
| `lineItems[].weight.grossValue` | `NetWeight` | JSON decimal number | No separate line gross weight exists in DTO; uses admitted line net weight rather than guessing another value |
| `lineItems[].weight.unitOfMeasurement` | `NetWeightUnit` | `metric` for kg/g, `imperial` for lb/oz | Unknown units rejected |
| optional unknown enum fields | none | Omitted | No guessed DHL enum serialization |
| null fields | DTO nulls / optional request data | Omitted by `JsonIgnoreCondition.WhenWritingNull` | Required fields rejected before serialization |

Money and weight use .NET `decimal` JSON serialization. Dates remain existing DHL request strings in `yyyy-MM-ddTHH:mm:ss GMT+00:00` format. No custom decimal rounding or culture formatting was introduced.

## Contract Compatibility

The MyDHL REST v3.3.1 contract requires declarable non-document shipments to include item-level export declaration data. DHL-1 correctly blocked international creation when no authoritative customs data existed. DHL-2 updates that behavior now that the canonical Customs Platform emits `CarrierNeutralCustomsDto` from the shipment-owned immutable snapshot.

The adapter remains a serializer. It does not own customs admission, publication, product classification, invoice policy, geographic country support, or shipment snapshot creation.

## Breaking Issues

Issue found:

- DHL international create still rejected all international shipments with `DHL_CUSTOMS_DATA_REQUIRED` and ignored `CreateShipmentRequest.Customs`.

Impact:

- Canonical Customs DTO consumption was absent.
- `CarrierNeutralCustomsDto -> DHL Request` was not implemented.

Resolution:

- DHL international create now builds `exportDeclaration.lineItems` from the canonical DTO and fails closed when the DTO cannot be serialized safely.

## Runtime Hardening

Modified:

- `PointOfSale.Infrastructure/Shipping/Adapters/DhlAdapter.cs`
- `PointOfSale.Tests/Infrastructure/DhlOfficialContractCompatibilityTests.cs`

Hardening added:

- `DHL_CUSTOMS_SNAPSHOT_MISSING` for absent/empty customs DTO;
- `DHL_CUSTOMS_VERSION_MISMATCH` for unsupported snapshot/DTO versions or empty snapshot identity;
- `DHL_CUSTOMS_SNAPSHOT_INCOMPLETE` for missing required customs facts;
- `DHL_CUSTOMS_ENUM_UNSUPPORTED` for unknown unit mappings;
- international `content.exportDeclaration.lineItems` serialization from snapshot DTO only;
- tests for successful international customs serialization and incomplete snapshot rejection.

No Customs redesign, Product redesign, Shipment redesign, Checkout redesign, Carrier Resolution redesign, POS Admin, UI, EF migration, or frontend work was performed.

## Regression Tests

Focused DHL regression coverage now freezes:

- domestic request shape remains non-customs and omits optional value/notification fields when absent;
- international shipment without customs DTO fails before network I/O;
- international shipment with canonical customs DTO serializes DHL `exportDeclaration.lineItems`;
- incomplete international customs DTO fails before network I/O;
- prior DHL-1 contract hardening remains covered.

Relevant Customs tests continue to freeze snapshot admission, DTO emission, published evidence usage, recovery, workflow, and fail-closed behavior.

## Verification

Executed:

- `dotnet build PointOfSale.sln --no-restore`: **PASS**, 75 existing warnings, 0 errors.
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter FullyQualifiedName~DhlOfficialContractCompatibilityTests -nologo -v minimal --no-build --no-restore`: **PASS**, 10/10.
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter FullyQualifiedName~Customs -nologo -v minimal --no-build --no-restore`: **PASS**, 28/28.
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter FullyQualifiedName~Shipping -nologo -v minimal --no-build --no-restore`: **82 passed, 5 failed**.

The five broad Shipping-filter failures are outside DHL international request construction:

- `ShippingWebhookTrustPipelineTests.ProcessWebhookAsync_ValidSignature_ProcessesWebhook_AndPersistsCompletedLog`;
- `ShippingWebhookTrustPipelineTests.ProcessWebhookAsync_UppercaseHexSignature_IsAcceptedCaseInsensitively`;
- `CommerceHubFinanceServiceTests.CreateRefundAsync_ShippingOnlyRefund_ShouldNotReverseCommission`;
- `ShipmentExternalRegistrationSafetyTests.ExecuteCarrierForExistingShipmentAsync_RetryAfterSuccess_DoesNotCreateDuplicateCarrierShipment`;
- `ShipmentExternalRegistrationSafetyTests.ExecuteCarrierForExistingShipmentAsync_WhenExternalSuccessWasNotPersisted_ReconcilesByCarrierRequestIdWithoutDuplicateCreate`.

The two `ShipmentExternalRegistrationSafetyTests` failures match the DHL-1 residual origin-admission failures and are unrelated to DHL request serialization. The webhook and CommerceHub finance failures are also outside this DHL-2 scope.

## Risks

- DHL live sandbox replay was not performed; deterministic tests freeze serialization shape but do not prove account/country acceptance.
- `CarrierNeutralCustomsDto` does not model every possible DHL optional customs document/invoice/reference field. Optional DHL fields are omitted rather than guessed.
- Line-level gross weight is not separately modeled in the DTO. DHL serialization uses the admitted line net weight for both net and gross item weight to avoid inventing another value; if DHL account/country policy requires distinct gross weight, the canonical DTO must add admitted evidence in a future Customs phase before DHL can serialize it.
- Quantity unit mapping is intentionally narrow. Unknown units fail closed until admitted and certified.
- Provider mapping coverage remains a separate GEO-10 production readiness blocker.

## Final Recommendation

Approve DHL-2 as the minimal international request/customs serialization hardening required after Customs Platform implementation.

DHL is structurally compatible for international request construction when a complete admitted `CarrierNeutralCustomsDto` is supplied. Production activation still requires credentialed DHL sandbox replay, account-enabled country coverage, and provider mapping rollout evidence.

Recommended next phase: **DHL credentialed sandbox certification and provider-country rollout**, limited to replaying certified request payloads against DHL test credentials and proving account/country/mapping coverage. Do not redesign Customs, Product, Shipping, Checkout, or Carrier Resolution.

## Explicit Success Answers

- Reviewed ONLY DHL International Request? **Yes.** Mandatory Customs, SHIP-ORIGIN, and GEO documents were read as frozen architecture; runtime review was limited to DHL international request construction and serialization.
- Runtime modified? **Yes.** DHL adapter only.
- Backend modified? **Yes.** DHL adapter plus focused DHL backend tests.
- Frontend modified? **No.**
- Canonical Customs consumed? **Yes.** `CreateShipmentRequest.Customs` / `CarrierNeutralCustomsDto` now drives international DHL customs serialization.
- Serializer only? **Yes.** The adapter validates and serializes; it does not admit or invent customs authority.
- Business logic inside adapter? **NO.** No HS code, commodity, value, country, currency, quantity, weight, or units are guessed.
- Lossless serialization? **Yes for modeled required customs facts.** Unsupported or unmodeled required facts fail closed rather than being guessed.
- Domestic flow unchanged? **Yes.** Domestic requests still do not emit customs declarations.
- International compatible? **Yes, conditionally on complete admitted Customs Snapshot DTO and DHL/account country acceptance.**
- Production compatible? **Conditionally.** Runtime serialization is compatible; production rollout still requires DHL sandbox/live evidence and provider mapping coverage.
- Recommend next phase: **DHL credentialed sandbox certification and provider-country rollout.**

## AI Context Update Decision

No `.ai-context` files were found in the workspace. No architecture-truth context files were updated. DHL-2 discovered provider-specific serialization implementation truth, not new cross-system architecture truth.
