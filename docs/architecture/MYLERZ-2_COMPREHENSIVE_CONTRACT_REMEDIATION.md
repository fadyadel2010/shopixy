# Mylerz comprehensive contract review and remediation

Review date: 2026-10-11. This document supersedes MYLERZ-1's public-document
discovery conclusion, not its operational containment decision.

## Vendor evidence and applicability

Only public documentation/website GETs were made. No authentication, lookup,
tracking, quote, shipment, or cancellation API operation was called.

| Evidence | Verified facts | Applicability limits |
| --- | --- | --- |
| https://www.mylerz.com/ | Corporate site links https://mylerz.net/login; describes multi-country logistics and merchant dashboards. | Marketing and tracking UI text do not establish API enums, account entitlements, or country coverage. |
| https://mylerz.net/login | Vendor-linked merchant portal, public application shell. | No login attempted; no merchant-specific onboarding contract obtained. `/Help` returned an empty document. |
| https://test.egypt.mobile.mylerz.com/Help | Public Egypt **test mobile** ASP.NET Help, operational courier/package APIs; pages label many operations “No documentation available.” | Not an Egypt merchant integration contract. Copyright 2026 is not a schema release date. |
| https://test.egypt.mobile.mylerz.com/Help/Api/POST-api-Account-login | JSON UserName/Password; response documented as generic HttpResponseMessage. | Does not establish merchant OAuth, token payload, lifetime, or account permissions. |
| https://test.egypt.mobile.mylerz.com/Help/Api/POST-api-package-GetPackageDetails | MobileDTO request Barcode/ReferenceNumber/MerchantId; Value/CoreValue/IsErrorState/ErrorDescription/ErrorMetadata wrapper, package and piece identities. | No merchant lookup authority or documented uniqueness/absence semantics. |
| https://integration.jordan.mylerz.net/swagger/ui/index → /swagger/docs/v1 | Vendor-domain integration Swagger 2.0; host explicitly Jordan; API title MYLERZ.Integration.Api, version v1. | Jordan is not evidence of Egypt account applicability. |
| https://integration.mylerz.net/swagger/ui/index → /swagger/docs/v1 | Public integration documentation under the vendor-linked mylerz.net domain; self-declared host integration.mylerz.net, HTTPS, same API title/version. | Discovery was a documentation-path probe under the verified vendor domain; corporate-site link to this Swagger was not found. Country, production/test designation and the project's merchant account applicability are not specified. |

These are live vendor-hosted documents, not proof of the latest accepted Egypt
merchant release. No dated release history, deprecation/migration notice or
account-issued integration package was obtained in this bounded discovery.

### Integration-host schema observations (not executable Egypt assumptions)

- `POST /token`: application/x-www-form-urlencoded; required grant_type with
  default `password`, username/password form fields. 200 is described only as a
  string; 400 invalid request and 401 unauthorized. No access-token object,
  expires_in, refresh-token or concurrent-refresh semantics are documented.
  Operation headers describe Authorization as “access token”; Swagger UI's
  generic api_key query widget is not proof of an authentication protocol.
- `POST /api/Orders/AddOrders`: array of OrderDTO. Required Description,
  Service_Type, Service, Service_Category, Payment_Type, COD_Value,
  Customer_Name, Mobile_No and Street. Patterns: DTD/DTC/CTD/CTC;
  SD/ND; DELIVERY/RETURN/PERSONAL DELIVERY; PP/COD/CC. These patterns do not
  establish a Shopixy service mapping or merchant entitlement.
- Reference/Reference2/Reference3/Reference4 and Customer_ReferenceNumber:
  maximum 150. Description 1000, notes 500, customer 200, mobile 20, street 500.
  WarehouseName, Total_Weight, Currency, Country and Pieces are present.
  Piece Weight and Dimensions are strings; measurement units/format and
  authorized merchant WarehouseName binding remain unproven.
- Create result: Value → AddOrderResponse with PickupOrderCode,
  PickupDateTime, Packages, ErrorCode/ErrorMessage. Packages contain packageNo,
  Reference/Reference2, BarCode, Status, Pieces and per-package errors. Root
  IsErrorState/ErrorDescription/ErrorMetadata also exist. No explicit batch
  atomicity, partial-success semantics or complete correlation guarantees.
  Pickup-order identity is not automatically shipment/barcode identity.
- `POST /api/packages/TrackPackages`: array of PackageCodeRefDTO; Barcode
  pattern `^\d{14}$`, ReferenceNumber max 50, PostalUPNumber max 13. Returned
  Value is an array with Barcode/RefNumber/PostalUPNumber and TrackLog.
  TrackLog has StatusCode, StatusEnName/StatusArName, ChangedDate and TimeZone.
  No authoritative canonical lifecycle mapping or TimeZone interpretation.
  The 150-character create reference limit differs from the 50-character lookup
  field limit: the existing 70-character `ship-<tenant>-<shipment>` must not be
  presumed a usable lookup key or deduplication guarantee.
- GetPackageStatus/GetPackageDetails are AWB-query operations with returned
  barcode and error fields. GetAWB returns byte-string evidence, GetExpectedCharges
  has a separate schema, and CancelPackage names a package mutation. Their
  existence does not certify Shopixy's old endpoints, labels, rates, lookup,
  target-bound cancellation, or account access. Cancellation stays Unsupported.
- No proven provider-enforced idempotency, absence-authorized re-creation,
  merchant-reference reconciliation, timezone policy, webhook authenticity,
  retry/rate-limit or token-rejection mutation replay contract.

## Compact defect matrix

| Finding | Official evidence | Affected source path | Severity | Correction | Regression test |
| --- | --- | --- | --- | --- | --- |
| Dormant guessed `/v2` hosts, bearer/API-secret headers, snake-case schemas, fallback prices/statuses presented as an integration | Vendor integration schemas above differ; Egypt applicability incomplete | Infrastructure/Shipping/Adapters/MylerzAdapter.cs | High latent maintenance risk; currently unreachable | Replace speculative implementation with explicit contract-unavailable operations, preserve Unsupported cancellation, explicit null lookup; no HTTP client construction | MylerzOfficialContractCompatibilityTests and MylerzContainmentRuntimeTests |
| Definition claims COD/prepaid/tracking/webhooks/labels/quotes/cancellation and Egypt coverage despite no executable contract | Marketing/schema presence does not establish end-to-end support | Infrastructure/Shipping/Adapters/MylerzCarrierDefinition.cs; CarrierCapabilityRegistry.cs | Medium | No operational capabilities/services/coverage/environment claimed | Definition/capability runtime assertions |
| Unavailable Mylerz credentials released for Tracking/AdminValidation/Cancellation; empty requirements report Available and selected environment as verified | Token response/environment/account details unproven | Infrastructure/Shipping/CarrierIntegrationAuthority.cs | Medium | Deny Mylerz credential resolution for all purposes; explicit contract blocker, Unverified availability and no executable environment | Every purpose, empty/nonempty credentials, tenant isolation, validation/webhook/quote callers |
| Legacy stored capability flags leak support claims to admin projections | Same missing executable Egypt contract | CarrierIntegrationAuthority.cs; API/Controllers/AdminCarrierConfigController.cs | Medium | Project effective Mylerz support as false while retaining stored settings | Runtime configuration and controller projection tests |
| Prior Mylerz attempts stop at empty service catalog and hide unresolved registration; timestamp-only evidence also needs containment | No authoritative lookup or deduplication semantics established | Infrastructure/Shipping/CarrierShipmentService.cs and .MylerzContainment.cs | High | Recognize durable prior attempt/identity evidence before service admission; preserve request/attempt evidence, mark reconciliation required, never create/complete from historical identity | Production shared caller redelivery, timestamp-only/snapshot/identity evidence tests |
| Fixed webhook controller comment asserts HMAC without evidence | No established signing contract | API/Controllers/ShippingWebhooksController.cs | Low | Describe reserved blocked route without an authentication assertion | Existing parser/trust caller regression coverage |
| Empty service catalog invents a valid standard service on admin writes | No admitted Mylerz services | API/Controllers/ShippingMethodsController.cs | Medium | Reject Mylerz service writes with an empty catalog | Production controller rejects before persistence/event publication |
| Failed reconciliation is logged/counted as completed/recovered because Reconciled means attempted | No provider lookup established; production returns Success=false, Reconciled=true | API/Consumers/OrderShipmentOrchestrationConsumer.cs; Services/Shipping/ShipmentAuthorityRecoveryHostedService.cs | Medium | Only Success counts as completion; unresolved diagnostics remain visible | Real orchestration consumer and recovery batch over the production Mylerz worker |

## Production paths inspected

Adapter/definition/DI and named HTTP policy; ShippingCarrierRegistry;
CarrierCapabilityRegistry; CarrierIntegrationAuthority (encrypted credentials,
tenant-bound configuration, fingerprinted validation, service allow-list and
webhook trust); IShippingCarrierAdapter default lookup; CarrierShipmentService
legacy creation, worker creation, durable request claim, response admission,
registration persistence/reconciliation, tracking/lifecycle and webhook admission;
TrackingSyncJob; ShipmentExternalRegistrationRequestedConsumer;
API ShipmentService; OrderShipmentOrchestrationConsumer and
ShipmentAuthorityRecoveryHostedService; AdminCarrierConfigController;
ShippingMethodsController; ShippingAdministrationApiService;
ShippingWebhookRequestPipeline and ShippingWebhooksController;
Storefront/checkout/CommerceHub/warehouse shipping readiness callers;
ShippingQuoteService and StorefrontShippingQuotesController.

Existing MYLERZ-1 and carrier forensic/cancellation reports were checked against
current source. Existing no-outbound tests remain containment tests, not positive
contract fixtures. Other-carrier deterministic tests using the code `mylerz`
do not certify the production Mylerz adapter.

### Integrity conclusions

New production Mylerz creation is blocked before durable attempt claiming by
the empty service catalog and unavailable central readiness. Direct adapter
calls fail with MYLERZ_OFFICIAL_CONTRACT_UNAVAILABLE. Therefore there is no
executed Mylerz payload, nested success admission, label, rate, token cache,
transport retry or lifecycle mapping to certify. Shared neutral result admission
and SQL ownership are not a substitute for provider identity admission.

Consumer retry/redelivery and periodic recovery call the shared worker; unresolved
Mylerz evidence must never authorize another creation, even when credentials,
service policy or origin facts change. Request IDs are correlation, not proven
provider deduplication. Registration persistence retries remain local persistence
only. Tracking returns before SQL mutation when provider/credential admission
fails; webhook parser and central trust both reject Mylerz. Customer prices remain
owned by the purchased order/shipping method; quote compatibility does not
authorize checkout. No financial, inventory, COD settlement or cancellation
authority is expanded.

## Capability disposition / remaining evidence

Creation, authentication validation, tracking, labels, rates, services, pickup,
webhooks, COD/prepaid shipping and geographical availability: **blocked pending
applicable official evidence and merchant entitlement**. Lookup/reconciliation:
**unsupported; uncertain attempts remain unresolved**. Cancellation:
**Unsupported**, retaining the completed cancellation phase boundary.

Activation needs vendor-confirmed Egypt hosts/environments and current account
package, token payload/expiry, account/warehouse binding, request measurements,
COD request semantics, service entitlement, package correlation/partial outcomes,
status/timezone meanings and reconciliation limits. No configuration toggle can
provide that evidence. Passing local tests/builds does not mean production-ready.

Deferred UI gap: admin configuration DTOs retain historical HasApiKey/IsConfigured
storage facts. The later frontend review must ensure these never override
ProductionCapability=Unavailable, unsupported connection testing, empty services
and effective false support flags. No browser/UI certification is claimed here.

## Verification

Implemented corrections in the matrix. The blocked adapter now has no transport,
speculative wire models, status aliases, prices, transit promises or signature
algorithm. It retains the public DI constructor and Unsupported cancellation.
The shared registration guard also recognizes response-only and mixed-case
historical evidence. It does not overwrite request IDs/snapshots/fingerprints,
attempt count/timestamps, historical identity, completion timestamp, shipment
lifecycle, order tracking, or money. Only reconciliation state/diagnostics and
UpdatedAt change. `Reconciled=true` means the reconciliation path was examined;
the explicit `Success=false` remains authoritative for completion.

Generic deterministic registration fixtures were renamed to the test-only code
`test-registration`; they cannot be confused with the production Mylerz adapter.
Previously existing DHL and cancellation-phase edits were preserved.

| Check | Result | Evidence |
| --- | --- | --- |
| Fresh backend/test build and focused production-path regressions | **94 passed, 0 failed, 0 skipped** | `C:\Users\fadya\AppData\Local\Temp\opencode\mylerz-remediation.trx` counters inspected |
| Shared/cross-carrier regressions | **316 passed, 1 failed, 0 skipped** | `C:\Users\fadya\AppData\Local\Temp\opencode\mylerz-shared-regressions.trx` counters inspected |
| Scoped tracked-file `git diff --check` | Passed | No whitespace errors; Git emitted LF/CRLF informational warnings |

The shared failure is
`CarrierGeographicResolutionIntegrationTests.ProviderIdentifiers_ArePersistedOnlyAsShipmentRegistrationEvidence`:
its unchanged source reads
`PointOfSale.Infrastructure/Migrations/20260705200000_Geo9CarrierGeographicResolution.cs`,
which is absent from the production migration directory and exists under
`docs/recovery/archive/RECOVERY-4_PHASE_A/PointOfSale.Infrastructure/Migrations/`.
This stale historical-migration assertion is unrelated to Mylerz corrections;
no production migration was restored or modified to satisfy it.

An initial Debug test build could not copy DLLs locked by the running API process.
Verification used the separate `MylerzVerification` configuration. Intermediate
test failures exposed a missed raw-entity admin projection and were corrected;
the final focused result above covers both admin projections. Existing compiler
warnings were emitted; this is not a warning-free or full-suite certification.

Focused command:

```powershell
dotnet test "PointOfSale.Tests/PointOfSale.Tests.csproj" -c MylerzVerification --no-restore --filter "FullyQualifiedName~MylerzOfficialContractCompatibilityTests|FullyQualifiedName~MylerzContainmentRuntimeTests|FullyQualifiedName~CarrierIntegrationAuthorityRemediationTests|FullyQualifiedName~ShipmentExternalRegistrationSafetyTests|FullyQualifiedName~CarrierCancellationEvidenceTests|FullyQualifiedName~ShippingMethodsCarrierAuthorityTests" --logger "trx;LogFileName=mylerz-remediation.trx" --results-directory "C:\Users\fadya\AppData\Local\Temp\opencode" -v:quiet -p:WarningLevel=0
```

Shared/cross-carrier command:

```powershell
dotnet test "PointOfSale.Tests/PointOfSale.Tests.csproj" -c MylerzVerification --no-build --no-restore --filter "FullyQualifiedName~BostaEndpointSurfaceTests|FullyQualifiedName~AramexOfficialContractCompatibilityTests|FullyQualifiedName~DhlOfficialContractCompatibilityTests|FullyQualifiedName~FedExTransportRuntimeTests|FullyQualifiedName~FedExResponseAdmissionTests|FullyQualifiedName~FedExEndpointRuntimeIntegrationTests|FullyQualifiedName~CarrierIntegrationAuthorityCertificationTests|FullyQualifiedName~ShipmentLifecycleAuthorityCertificationTests|FullyQualifiedName~CarrierNeutralEndpointContractsCertificationTests|FullyQualifiedName~CarrierGeographicResolutionIntegrationTests|FullyQualifiedName~ProviderMappingCoverageProductionReadinessTests|FullyQualifiedName~ShipmentExternalRegistrationRequestedConsumerTests|FullyQualifiedName~ShippingWebhookTenantResolutionTests" --logger "trx;LogFileName=mylerz-shared-regressions.trx" --results-directory "C:\Users\fadya\AppData\Local\Temp\opencode" -v:minimal
```

All provider transports in executed tests were mocked or rejected before client
construction. EF InMemory exercised production pre-admission/recovery paths;
no positive provider contract, relational concurrency, live account entitlement,
browser experience or production readiness is certified by these results.
