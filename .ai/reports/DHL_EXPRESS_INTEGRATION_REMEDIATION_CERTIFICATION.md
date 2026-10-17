# DHL Express Adapter Contract Correction and Integration Certification

## 1. Verdict and execution identity

**PASS: bounded production-code remediation and fresh local/mock regression verification. NOT DHL account, sandbox, production, or end-to-end merchant-operation certification. DHL creation/quote/admission activation remains blocked pending remote account certification.**

- Executed 2026-10-11 directly in MAIN/PRIMARY Windows: `C:\Users\fadya\source\repos\PointOfSale`.
- Repository root verified by `git rev-parse --show-toplevel`; branch `main`; HEAD `256339621c0ea49f3602097cdacdf89ffe71754f`, unchanged at completion.
- Initial `git status --short` showed extensive pre-existing modified/untracked application, financial, cancellation, tracking, frontend, migration, report, and artifact work, plus deleted tracked temporary artifacts. No reset, checkout, cleanup, staging, or commit was performed.
- Initial DHL diff contained an existing `Disposition = CarrierCancellationDisposition.Unsupported` change. That change is preserved and is **not counted as DHL remediation implemented here**.
- Intentional edits here: four production files, three test files, this report. Build/test output is isolated under ignored `.artifacts/dhl-express/`.
- Read all 608 lines of `.ai/reports/SHIPPING_CARRIERS_END_TO_END_FORENSIC_AUDIT.md`; rechecked DHL H3/H12 and related creation/readiness findings against current production source and fresh official documentation. Historical audit binaries and cancellation closeout results were not used as proof of the corrected DHL behavior.
- No subagents; no migrations; no application restart or provider activation; no MAIN business database access; no DHL sandbox/production operation. Public developer-portal documentation/OpenAPI metadata GETs only.

## 2. Fresh official contract evidence

Retrieved official sources on 2026-10-11:

1. <https://developer.dhl.com/api-reference/dhl-express-mydhl-api>
2. <https://developer.dhl.com/sites/default/files/2026-10/dpdhl-express-api-3.3.4.yaml>

The portal's actual OpenAPI link points to the second URL. The downloaded document identifies `openapi: 3.0.0`, `info.version: 3.3.4`, `x-release-date: 2026-10-11`. This supersedes the forensic audit's 3.3.3 reference and older internal 3.3.1 comments for this comparison.

| Official operation/schema | Verified contract relevant to correction |
|---|---|
| `POST /rates`; `supermodelIoLogisticsExpressRates.properties.products.items.properties.totalPrice` | `currencyType` selects BILLC/PULCL/BASEC; monetary currency is `priceCurrency`; `price` is a required number. Category is not ISO currency. |
| `GET /shipments/{shipmentTrackingNumber}/tracking` | Supports `trackingView`, `levelOfDetail`, and `requestGMTOffsetPerEvent`. The single-shipment route explicitly supports requesting per-event offsets. |
| `supermodelIoLogisticsExpressTrackingResponse` | Shipment `status` is a string (example `Success`). Scan fields are `date`, `time`, `GMTOffset`, `typeCode`, `description`, and array `serviceArea`. |
| Official single-shipment tracking example | PU pickup; AF arrival at sort facility; PL processed; DF departed facility; RR customs-clearance update; CR clearance processing complete; AR arrival at delivery facility; WC with delivery courier; OK delivered. RR is not a physical return. |
| `POST /shipments` | Published creation success is HTTP 201. Response headers include `Message-Reference` described as customer-provided or automatically generated correlation. |
| `supermodelIoLogisticsExpressCreateShipmentResponse` | `shipmentTrackingNumber` identifies shipment; `dispatchConfirmationNumber` identifies pickup booking. `packages[].trackingNumber` identifies pieces. Documents require `imageFormat`, `content`, `typeCode`; optional `packageReferenceNumber`. |
| `supermodelIoLogisticsExpressReference` | Reference value max 35 characters; includes OID and CU; excludes the adapter's old AAO type. |
| Create-request export line item | Quantity is integer 1..1,000,000,000. Manufacturer country and weight are required. Weight contains net/gross values and forbids additional properties; no line-weight `unitOfMeasurement`. Units come from shipment content. |
| Pickup API | Pickup create/update/delete exists. No published shipment-delete operation established. Shipment creation can explicitly set `pickup.isRequested=false`. |
| Portal authentication/environments | Preemptive Basic Auth; production `https://express.api.dhl.com/mydhlapi`; test `/mydhlapi/test`; test tracking is restricted to listed fixed waybills, not arbitrary newly created test shipments. |

No consulted source establishes provider-side exactly-once POST handling merely from Message-Reference or customer references. No remittance/account financial authority was inferred from these logistics schemas.

## 3. Exact production methods reviewed

### DHL adapter: `PointOfSale.Infrastructure/Shipping/Adapters/DhlAdapter.cs`

Reviewed complete production file, including:

- `GetQuoteAsync`
- `CreateShipmentAsync`
- `GetTrackingAsync`
- `ValidateCredentialsAsync`
- `GetSupportedServiceLevels`
- `ParseWebhook`
- `CancelShipmentAsync` (unsupported contract only; no execution/closeout work)
- `TryMapDhlStatus`, `GetBaseUrl`, `HasRequiredCredentials`, `AddAuthHeaders`
- `TryBuildExportDeclaration`, `IsIso2`, `IsCurrency`, `TryMapDhlQuantityUnit`, `TryMapDhlWeightUnit`
- `MapServiceLevelToProduct`, `MapDhlProductToServiceLevel`, `GetServiceName`
- `GetJsonOptions`, `TryParseError`, dormant `ValidateWebhookSignature`
- New admission helpers `ReconciliationRequired`, `IsTrackingNumber`, `IsPieceIdentifier`, `IsPdfLabel`, `TryParseScanTimestamp`, `TryConvertToKg`, `SupportsOfficialWebhookContract`
- Internal rate, creation/piece/document, customs, and tracking response/request models.

### Capability, credentials and shared admission

| Production source | Reviewed methods/surfaces |
|---|---|
| `Infrastructure/Shipping/CarrierCapabilityRegistry.cs` | `BuildCapability`, DHL Partial/LocalOnly, credentials, service levels, disabled webhook declaration |
| `Infrastructure/Shipping/Adapters/DhlCarrierDefinition.cs` | COD/prepaid/tracking/labels/quotes/pickup/webhook/cancellation capability properties |
| `Infrastructure/Shipping/CarrierIntegrationAuthority.cs` | `ResolveCredentialsAsync`, `GetIntegrationReadinessAsync`, `GetServiceLevelReadinessAsync`, `ValidateAndStoreIntegrationAsync`, `ValidateResolvedCredentialsAsync`, `BuildReadinessAsync`, `BuildCredentials`, configuration fingerprint and purpose predicates; webhook capability/trust resolution |
| `Infrastructure/Shipping/CarrierShipmentService.cs` | `CreateShipmentWithCarrierAsync`, `ExecuteCarrierForExistingShipmentAsync`, durable request claim/recovery, `ReconcilePendingCarrierRequestAsync`, `ApplySuccessfulCarrierRegistration`, `PersistSuccessfulCarrierRegistrationAsync`, `ValidateCarrierCreateResultForPersistence`, identifier/URL/base64/format/JSON/status validators, `SyncTrackingAsync`, tracking current-authority revalidation/locks, `GetLabelAsync` |
| `Infrastructure/Shipping/IShippingCarrierAdapter.cs` | Neutral requests/results and default `GetShipmentByCarrierRequestIdAsync` null lookup |
| `Core/Policies/ShipmentLifecycleTransitionPolicy.cs` | `CanTransition`; strict canonical lifecycle remains authoritative |
| `API/Program.cs` | Actual `CreateExternalHttpRetryPolicy`, `ConfigureExternalHttpClient`, named DHL client timeout/registration |

Paths in this table abbreviate the `PointOfSale.` project prefix only.

## 4. Confirmed defects and actual production corrections

| Defect | Correction in actual production source | Regression evidence |
|---|---|---|
| Tracking expected a status object, timestamp/statusCode and one service-area object | `DhlShipmentTracking.Status` is string; `DhlTrackingEvent` uses date/time/GMTOffset/typeCode and service-area list. Removed unused old tracking-status object model. `GetTrackingAsync` requests all checkpoints and offsets on the official single-shipment route. | Official delivered/facility/physical checkpoint tests; legacy object/timestamp fixture rejected |
| AF/PL mapped ReadyForPickup, RR Returned, CR OutForDelivery, AR PickedUp | `TryMapDhlStatus` admits verified codes only: PU PickedUp; AF/PL/DF/RR/CR/AR InTransit; WC OutForDelivery; OK Delivered. Removed unverified aliases/checkpoint guesses. | Six individual transit-code cases, three physical-code cases, unknown rejection |
| Missing scan timestamp invented `DateTime.UtcNow`; naive ordering could select wrong current status | Strict invariant date/time/offset parsing to UTC; missing/invalid offset/date/time rejected. Current status derives from UTC-latest admitted scan; conflicting statuses at latest timestamp rejected. Array locations retained. | Five timestamp rejection cases; cross-offset chronological ordering; conflicting-latest test |
| Tracking selected first shipment and trimmed response identity | Requires exactly one shipment and exact requested AWB identity; validates admitted input and uses escaped path component. | Mismatched identity, duplicate-shipment rejection |
| Rate mapper used currencyType as currency; missing money invented zero/USD | `DhlPrice.Price` nullable; added `PriceCurrency`. `GetQuoteAsync` selects exactly one BILLC entry, requires explicit nonnegative amount and three ASCII-letter monetary currency, rejects absent/ambiguous/invalid billing price or unsupported product. Explicit supplied zero is distinguishable from omitted price. | BILLC selection after BASEC entry returns 123.45 EGP; seven invalid-money cases return no quotes |
| Create success admitted any 2xx/unbound response | Requires HTTP 201 and exact single Message-Reference echo. Stable 32-hex SHA-256-derived correlation is sent from durable CarrierRequestId. Unbound/202/invalid responses never return successful identity/label. | Missing/mismatched header, 200/202, response identity tests |
| Any nonblank create identifier accepted; piece cardinality absent | Standard 10-digit AWB admission, expected single-package response count, valid unique alphanumeric piece identifiers. Invalid/missing identity requires reconciliation. | Malformed AWB and extra-package cases |
| Pickup confirmation used as shipment ExternalReferenceId | ExternalReferenceId is AWB; dispatch confirmation retained only in metadata. | Pickup booking cannot replace shipment identity |
| Label was optional; format hardcoded without validating returned document | Requires exactly one returned label, PDF imageFormat, nonempty bounded base64 content with PDF header/end marker, and matching optional package reference. Failure is reconciliation-required because POST may already have created shipment. | Missing label, invalid base64, non-PDF, wrong format, wrong package reference |
| AAO customer reference not in current schema; actual durable IDs exceed reference max35 | Replaced with official OID containing same 32-character durable correlation; order CU reference over35 rejected before network. | Real-length ship-tenant-shipment request ID is hashed, not truncated or sent as AAO |
| Customs grams/imperial values sent numerically under metric shipment, and forbidden weight-unit field serialized | Convert kg/g/lb/oz to kg at 0.001 precision, reject conversion to nonpositive value, remove line-weight unit field, send admitted net weight without inventing gross weight. | 500g serializes as 0.5 metric kg; no line-weight unit/gross field |
| Required manufacturer missing; fractional quantity and mismatched customs route accepted | Require admitted manufacturer country, integer bounded quantity, matching origin/destination snapshot. No manufacturer/route facts fabricated. | Three customs rejection cases before HTTP |
| Unsupported COD/service request silently became ordinary prepaid/default product | Direct create rejects COD, same_day and unknown services; quote rejects COD/non-single-package/invalid weight-country request. Removed same_day catalog claim and unknown-product express fallback. | Three create feature rejection cases; capability containment |
| Nominal 30x20x10 dimensions substituted for missing package evidence | Quote DTO now exposes optional neutral LengthCm/WidthCm/HeightCm. DHL quote/create require all positive actual dimensions and serialize supplied values; missing dimensions fail before HTTP. Other adapters' behavior is unchanged. | Three missing-create-dimension cases and missing-quote-dimension rejection |
| Requested insurance/dangerous-goods handling silently ignored | Create rejects positive requested insurance; export-declaration qualification rejects DangerousGoods=true before HTTP. No ordinary shipment is substituted for those unsupported services. | Insurance and dangerous-goods preflight rejection cases |
| Dormant guessed webhook parser could accept payload despite unsupported capability | Adapter itself now returns null behind unconditional unimplemented-contract gate, in addition to existing capability/trust blocking. | Known and unknown status webhook rejection |
| Local-only credential validation could satisfy executable DHL readiness | DHL-only `BuildReadinessAsync` adds `DHL_REMOTE_ACCOUNT_CERTIFICATION_REQUIRED` for verified-connection purposes while capability is not Remote. Local success/fingerprint cannot enable quote, creation, registration, or storefront/checkout/CommerceHub admission. | Local-success readiness and foreign-tenant configuration test |

### POST retry finding: evidence of an existing correct boundary

An automatic DHL POST transport retry defect **does not exist in the current named-client source**. `Program.CreateExternalHttpRetryPolicy` retries GET/HEAD/OPTIONS; mutations retry only with Idempotency-Key. DHL creation sets no such key, and Message-Reference/OID must never be treated as one.

The new regression invokes the actual compiled API startup policy method and executes its policy with simulated 503 and 429 responses: exactly one attempt each. Adapter timeout/ambiguous-response tests independently show one send and unsuccessful result. `CreateShipmentAsync` now returns `DHL_CREATE_RECONCILIATION_REQUIRED` for timeout/network/unexpected failures, 5xx/408, invalid successful responses, identity, and label failures.

Shared recovery already persists attempt ownership before HTTP and uses `ReconcilePendingCarrierRequestAsync` instead of blindly recreating. `ExecuteCarrierForExistingShipmentAsync` now uses the existing read/tracking credential purpose only for DHL registrations that already have durable external identity or a prior recorded attempt. This permits existing-evidence reuse and fail-closed lookup while keeping new POSTs behind ExternalRegistration readiness. It never converts a prior attempt into permission to recreate.

New InMemory tests use the **real DhlAdapter** under LocalOnly capability: a prior attempt stays ReconciliationRequired, attempt count remains1, no completion/status promotion, and zero HTTP calls; a new attempt is blocked by readiness with attempt count0 and zero HTTP calls. Existing purchased-snapshot/reuse tests also pass. These are isolated recovery/containment tests, not remote account evidence.

### Exact intentional files changed

Production:

1. `PointOfSale.Infrastructure/Shipping/Adapters/DhlAdapter.cs`
2. `PointOfSale.Infrastructure/Shipping/CarrierIntegrationAuthority.cs` (DHL-specific readiness blocker only)
3. `PointOfSale.Infrastructure/Shipping/IShippingCarrierAdapter.cs` (additive neutral optional quote dimensions; existing cancellation/result work preserved)
4. `PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs` (`ExecuteCarrierForExistingShipmentAsync` DHL replay-only credential-purpose selection)

Regression tests:

1. `PointOfSale.Tests/Infrastructure/DhlOfficialContractCompatibilityTests.cs`
2. `PointOfSale.Tests/Shipping/CarrierIntegrationAuthorityRemediationTests.cs`
3. `PointOfSale.Tests/Shipping/ShipmentExternalRegistrationSafetyTests.cs`

Certification:

1. `.ai/reports/DHL_EXPRESS_INTEGRATION_REMEDIATION_CERTIFICATION.md`

Existing cancellation disposition, tenant filters, tracking concurrency changes, registration/cancellation barriers, payment/ledger/refund lineage, Manual COD/remittance work, migrations and frontend changes were preserved.

## 5. Fresh builds, discovery and execution

SDK `9.0.308`; target net9.0; VSTest `17.14.1`, xUnit adapter `2.8.2`.

The first build restored/compiled the current Core, Infrastructure, API and Tests into isolated artifacts. The final build was after all production and test edits:

```powershell
dotnet build "PointOfSale.Tests/PointOfSale.Tests.csproj" --artifacts-path ".artifacts/dhl-express" --no-restore --no-incremental -v minimal "-clp:ErrorsOnly;Summary"
```

**Build succeeded: 452 warnings, 0 errors; final elapsed 00:00:23.58.** Warnings are not suppressed or represented as a warning-free build. The earlier full-output build identified all four rebuilt project outputs; isolated test DLL is `.artifacts/dhl-express/bin/PointOfSale.Tests/debug/PointOfSale.Tests.dll`.

Fresh final discovery:

```powershell
dotnet test "PointOfSale.Tests/PointOfSale.Tests.csproj" --artifacts-path ".artifacts/dhl-express" --no-build --no-restore --list-tests --filter "FullyQualifiedName~Dhl|FullyQualifiedName~CarrierIntegrationAuthorityRemediationTests|FullyQualifiedName~ShipmentExternalRegistrationSafetyTests|FullyQualifiedName~ShipmentExternalRegistrationRequestedConsumerTests|FullyQualifiedName~CarrierIntegrationStateMachineConvergenceTests|FullyQualifiedName~CarrierIntegrationAuthorityCertificationTests|FullyQualifiedName~ShippingMethodsCarrierAuthorityTests|FullyQualifiedName~T10SettlementContainmentTests"
```

Discovery listed the concrete theory cases and named tests below from the fresh isolated binary; it was not inferred from source filenames.

Final executions:

```powershell
dotnet test "PointOfSale.Tests/PointOfSale.Tests.csproj" --artifacts-path ".artifacts/dhl-express" --no-build --no-restore --filter "FullyQualifiedName~DhlOfficialContractCompatibilityTests|FullyQualifiedName~DhlRuntimeAuthorityTests|FullyQualifiedName~DhlGeographicCompatibilityTests|FullyQualifiedName~CarrierIntegrationAuthorityRemediationTests|FullyQualifiedName~ShipmentExternalRegistrationSafetyTests" --logger "console;verbosity=normal" --logger "trx;LogFileName=dhl-final.trx" --results-directory ".artifacts/dhl-express/results"

dotnet test "PointOfSale.Tests/PointOfSale.Tests.csproj" --artifacts-path ".artifacts/dhl-express" --no-build --no-restore --filter "FullyQualifiedName~ShipmentExternalRegistrationRequestedConsumerTests|FullyQualifiedName~CarrierIntegrationStateMachineConvergenceTests|FullyQualifiedName~CarrierIntegrationAuthorityCertificationTests|FullyQualifiedName~ShippingMethodsCarrierAuthorityTests|FullyQualifiedName~T10SettlementContainmentTests" --logger "console;verbosity=normal" --logger "trx;LogFileName=dhl-boundaries.trx" --results-directory ".artifacts/dhl-express/results"
```

| Final class | Executed | Passed | Failed/skipped |
|---|---:|---:|---:|
| DhlOfficialContractCompatibilityTests | 71 | 71 | 0/0 |
| DhlRuntimeAuthorityTests | 5 | 5 | 0/0 |
| DhlGeographicCompatibilityTests | 3 | 3 | 0/0 |
| CarrierIntegrationAuthorityRemediationTests | 9 | 9 | 0/0 |
| ShipmentExternalRegistrationSafetyTests | 5 | 5 | 0/0 |
| ShipmentExternalRegistrationRequestedConsumerTests | 1 | 1 | 0/0 |
| CarrierIntegrationStateMachineConvergenceTests | 8 | 8 | 0/0 |
| CarrierIntegrationAuthorityCertificationTests | 10 | 10 | 0/0 |
| ShippingMethodsCarrierAuthorityTests | 5 | 5 | 0/0 |
| T10SettlementContainmentTests | 8 | 8 | 0/0 |
| **Disjoint final total** | **125** | **125** | **0/0** |

TRX counters inspected independently:

- `.artifacts/dhl-express/results/dhl-final.trx`: total/executed/passed93, failed/error/timeout/aborted/notExecuted0; Completed. Console total time8.4484s.
- `.artifacts/dhl-express/results/dhl-boundaries.trx`: total/executed/passed32, failed/error/timeout/aborted/notExecuted0; Completed. Console total time7.3868s.
- Earlier intermediate `.artifacts/dhl-express/results/dhl-contract.trx`:75/75 passed. This is historical within this session, not added to the final125 count. Final-named TRX files were overwritten by the last current-binary execution; their present counters above are authoritative.

### Individual DHL adapter test-method results

All rows below were individually reported Passed by the final runner and recorded in `dhl-final.trx`. Theory counts are concrete executions; parameter strings, including full JSON, are preserved in TRX definitions/results.

| DhlOfficialContractCompatibilityTests method | Cases/result |
|---|---|
| CreateShipment_UsesOfficialEndpointAndRequestShape | 1 PASS |
| CreateShipment_InternationalWithoutCustomsEvidenceFailsBeforeNetwork | 1 PASS |
| CreateShipment_InternationalWithCanonicalCustomsSerializesExportDeclaration | 1 PASS |
| CreateShipment_InternationalWithIncompleteCustomsFailsBeforeNetwork | 1 PASS |
| CancelShipment_FailsClosedWithoutCallingUnpublishedRoute | 1 PASS |
| Definition_DoesNotAdvertiseUnimplementedOrUnpublishedCapabilities | 1 PASS |
| Quote_ProviderFailureDoesNotInventPrice | 1 PASS |
| Tracking_DeliveredCodeMapsToDelivered | 1 PASS |
| Tracking_UnknownStatusFailsClosed | 1 PASS |
| Webhook_UnknownStatusFailsClosed | 1 PASS |
| Tracking_MismatchedShipmentIdentityFailsClosed | 1 PASS |
| ValidateCredentials_DoesNotPerformGeographicAddressLookup | 1 PASS |
| Tracking_OfficialFacilityAndCustomsScansAreTransit_NotReturnPickupOrDelivery | AF/PL/RR/CR/AR/DF: 6 PASS |
| Tracking_OfficialPhysicalCheckpointsMapCanonically | PU/WC/OK: 3 PASS |
| Tracking_InvalidOrUnzonedScanCannotInventTimestamp | Missing date, invalid calendar date, invalid time, missing offset, invalid offset: 5 PASS |
| Tracking_OrdersByUtcScanTimeAcrossServiceAreas | 1 PASS |
| Tracking_LegacyAssumedShapeAndMissingPhysicalEvidenceAreRejected | Object status, legacy scan, Failure status, no scans: 4 PASS |
| Quote_UsesBillingPriceCurrencyRatherThanCategoryOrArrayOrder | 1 PASS |
| Quote_MissingInvalidOrAmbiguousMoneyDoesNotBecomeFreeUsdQuote | Empty, missing price, missing currency, category as currency, negative, no BILLC, duplicate BILLC: 7 PASS |
| CreateShipment_UnboundResponseCannotRegisterShipment | Missing/mismatched correlation: 2 PASS |
| CreateShipment_InvalidIdentityPieceOrLabelIsNeverAdmitted | Missing label, invalid base64, non-PDF, wrong format, wrong piece, extra package, malformed AWB: 7 PASS |
| CreateShipment_PickupBookingCannotReplaceShipmentIdentity | 1 PASS |
| CreateShipment_AmbiguousHttpOutcomeIsNotRetriedOrAdmitted | 200/202/500: 3 PASS |
| CreateShipment_TimeoutIsReconciliationRequiredAndNeverRetried | 1 PASS |
| CreateShipment_UnsupportedFeaturesFailBeforeNetwork | COD/same_day/unknown/insurance: 4 PASS |
| CreateShipment_CustomsGramWeightsAreConvertedToShipmentMetricUnits | 1 PASS |
| CreateShipment_ContradictoryOrIncompleteCustomsCannotBeFabricated | Fractional quantity/missing manufacturer/route mismatch/dangerous goods: 4 PASS |
| CreateShipment_DurableReferenceFitsOfficialContractWithoutTruncatingIdentity | 1 PASS |
| CreateShipment_ActualApiPostPolicyDoesNotReplayOnTransientStatus | 503/429: 2 PASS |
| Tracking_DuplicateShipmentsAndConflictingLatestScansFailClosed | 1 PASS (both scenarios asserted) |
| Webhook_KnownStatusStillBlockedWithoutOfficialContract | 1 PASS |
| CreateShipment_MissingActualDimensionsCannotBecomeNominalPackage | Missing length/width/height: 3 PASS |
| Quote_MissingDimensionsCannotInventRatedPackage | 1 PASS |

Additional new individual results: `CarrierIntegrationAuthorityRemediationTests.Dhl_LocalCredentialSuccessCannotCertifyRemoteAccountOrEnableCreation` PASS; `ShipmentExternalRegistrationSafetyTests.Dhl_PriorAmbiguousAttemptUsesRealAdapterFailClosedLookupAndNeverReposts` PASS; `ShipmentExternalRegistrationSafetyTests.Dhl_NewAttemptCannotBypassLocalOnlyReadiness` PASS.

Negative regressions assert observable adapter rejection, empty money/identity/label outputs, no outbound calls for unsupported input, exact canonical scan outcomes, and/or actual compiled policy attempt counts. They are not report-marker tests. Legacy positive fixtures were corrected to documented field names, HTTP201, piece information, imageFormat and mocked Message-Reference echo; the short encoded PDF fixture tests envelope admission, not full document rendering.

## 6. Preservation and activation evidence

- `git diff --check` completed successfully, with no whitespace errors. Git printed LF-to-CRLF notices for existing working-copy files; those are not failed checks.
- Branch/HEAD rechecked unchanged; `git diff --cached --name-only` empty. Nothing staged or committed.
- The new readiness test proves that even successful local credential validation and matching fingerprint cannot remove `DHL_REMOTE_ACCOUNT_CERTIFICATION_REQUIRED`; another tenant has no configuration authority.
- Actual production capability remains Partial/LocalOnly. No runtime activation was necessary to verify isolated rebuilt adapter/policy behavior, and no MAIN process was stopped/restarted. No claim that a running MAIN listener has loaded these binaries.
- DHL pickup remains unimplemented and unrequested; shipment cancellation unsupported; webhook capability/trust and direct parser blocked; COD request/collection and automatic remittance not enabled. Delivered tracking leaves CodCollected false.
- Shared tenant SQL filters, post-observation requalification, registration attempt/recovery ownership and registration/cancellation coordination were not weakened. Shared optional-label admission remains generic; DHL-specific mandatory label admission happens before successful result is returned.
- T10 controller/mock/source containment tests passed. Manual COD, separately implemented Manual Carrier Remittance, financial posting, refund lineage, generic Clearing transfer authority and all unrelated working-tree changes were not edited by this task. These financial mechanisms are not newly positively certified by these DHL tests.
- No frontend files changed in this task; frontend/browser verification condition did not apply. Existing user frontend work remains present.

## 7. Remaining limitations and external certification boundary

1. **No credentialed DHL execution.** Actual Basic authentication, account ownership/entitlement, lane/product availability, account currency, response correlation echo, labels, pickup/delivery or provider fee billing are unverified externally. A local success is not account certification. The readiness blocker must not be removed merely because mocks pass.
2. **Correlation is not immutable account binding or idempotency.** Message-Reference binds this synchronous response to the durable request under a mocked contract. The standard create schema does not require customer-reference/account echoes. No lookup/reconciliation executor was invented. Lost/rejected POST outcomes can represent externally created shipments and require separate verified reconciliation, not blind resend.
3. **Intentionally narrow admission.** Tracking admits nine documented checkpoint codes and requires valid GMTOffset from the single-shipment endpoint. Unknown checkpoints, missing offsets, ambiguous latest scans, duplicate shipments and unadmitted identities fail closed. Ten-digit AWBs, alphanumeric pieces, one package and one PDF label are this adapter's admitted subset, stricter than some broadly typed OpenAPI fields. More products/document/piece formats need separately verified mappings.
4. **Pricing remains an estimate.** BILLC amount/currency mapping is corrected; ASCII currency shape is not ledger-currency or actual-fee authority. Only P/H/N product mappings are admitted. Responses with unsupported products or no unambiguous BILLC fail closed. Existing estimated-day ranges are not certified delivery facts; canonical checkout/financial boundaries remain separate. Multi-package/COD quotes are blocked. Missing actual dimensions no longer produce nominal rates or shipments.
5. **Creation is not exhaustive market/customs certification.** Actual package dimensions must be supplied from admitted package evidence; existing shared callers that lack such dimensions receive fail-closed DHL preflight rejection. This task adds neutral quote transport fields, not a package-evidence persistence/UI workflow or migration. Customs unit/schema/quantity/manufacturer/route corrections are mock verified; unit-price versus whole-line declared-value semantics, complete invoice evidence, account/market business rules and full international customs execution still require qualification before activation. Insurance/dangerous-goods handling is blocked, not implemented. No unsupported financial authority follows from declared customs value.
6. **PDF envelope only.** Base64 size, declared format, header/end marker and optional piece reference are checked. This is not a full PDF parser, renderer, barcode/AWB-content or visual print certification. Standalone label generation/refetch/download UX and proof-of-delivery executor remain outside this adapter correction.
7. **Shared catch-up limitation remains.** Current `SyncTrackingAsync` still applies the latest status under strict canonical transitions rather than replaying admitted ordered history. Missed intermediate pickup/transit progression can therefore be held by shared admission even with corrected DHL scans. Shared lifecycle/tenant/concurrency behavior was preserved; this report does not reuse the audit's obsolete unbound-job claims as current DHL evidence.
8. **No positive finance or merchant data test.** Tests use HTTP handlers, actual compiled policy with delegate responses, mocks and isolated EF InMemory stores. No MAIN order/shipment was created, cancelled, tracked, settled, remitted or refunded; no live account/flag/schema was mutated.

**Final deliverable:** `.ai/reports/DHL_EXPRESS_INTEGRATION_REMEDIATION_CERTIFICATION.md`.
