# FedEx integration remediation certification

**Reviewed:** 2026-10-11 · **Workspace:** `C:\Users\fadya\source\repos\PointOfSale` · **Branch:** `main`

## Verdict — CONDITIONAL

Confirmed, safely correctable FedEx contract, identity, authentication, transport,
mapping and declaration defects were remediated and locally regression-tested.
The final selected suite passed **137/137**, with zero failures or skips.
**This is not live account or production-route certification.** Canonical FedEx
shipment/quote/checkout activation remains blocked by `NotCertified` and empty
supported service levels. No readiness gate was unlocked.

**Canonical-evidence follow-up:** section 6 records the completed wiring,
**105/105 focused regressions**, and the explicitly user-authorized application of
the minimal warehouse postal-evidence migration. Code-level evidence admission is
connected; external certification and runtime activation remain blocked.

The existing dirty working tree was inspected before edits. The pre-existing
FedEx cancellation disposition and existing Program DI additions were preserved.
Other carrier, parcel, tracking, financial, migration and cancellation work was
preserved. The index was empty initially and remains empty.

## 1. Official contract facts

Public documentation was retrieved from the Developer Portal, its current
canonical documentation links and the OpenAPI resources linked in those pages.
The four detailed resources below declare **OpenAPI 3.0.0, API version 1.0.0**.
This is distinct from catalog display metadata: Ship displays V0/2020-07-27 and
Comprehensive Rate V0/2023-10-16 despite their newer change logs.

| Official source | Relevant contract / date | Production impact |
|---|---|---|
| [Portal](https://developer.fedex.com/api/en-us/home.html), [announcements](https://developer.fedex.com/api/en-us/announcements.html) | Reviewed announcements through October 7, 2026. Freight LTL independence effective June 1, 2026. | No Freight executor exists in this FedEx integration. |
| [Authorization](https://developer.fedex.com/api/en-us/catalog/authorization/docs.html) | Form `POST /oauth/token`; Bearer; standard `expires_in=3600`; project credentials authorize enabled APIs. Additional child credentials depend on customer type. No publication/effective date supplied in narrative. | Existing adapter uses `client_credentials`; corrected cache identity and concurrent refresh. Parent/child onboarding is not certified. |
| [Ship documentation](https://developer.fedex.com/api/en-us/catalog/ship/docs.html), [Ship change log](https://developer.fedex.com/api/en-us/catalog/ship.html), [Ship OpenAPI](https://developer.fedex.com/wirc/json/api_groups/Ship/Shipment-Resource.json) | REST `POST /ship/v1/shipments`; account, parties, pickup/service/packaging, payment, package weights and label specification. Dimensions required for `YOUR_PACKAGING`, integers, schema maximum 999. Customer reference maximum 30 Ground / 40 Express; invoice maximum 30. `LABEL` requests Base64. | Corrected mapping/admission; unsupported combinations fail before transport. Tracking/master/label evidence is required, not HTTP success alone. |
| [Track documentation](https://developer.fedex.com/api/en-us/catalog/track/docs.html), [Track OpenAPI](https://developer.fedex.com/wirc/json/api_groups/Track/TrackingNumbers-Resource.json) | Basic Integrated Visibility REST v1, POST tracking numbers; maximum 30. `trackResults` may contain duplicate matches; `trackingNumberUniqueId`, `derivedStatusCode`, scan timestamps and ancillary response fields are documented. | Exact requested-number binding; multiple matches fail closed; derived scan mapping and explicit offset handling. No account ownership is inferred from public tracking. |
| [Service Availability](https://developer.fedex.com/api/en-us/catalog/service-availability/docs.html), [OpenAPI](https://developer.fedex.com/wirc/json/api_groups/ServiceAvailability/PackageAndServiceOptions-Resource.json) | POST `/availability/v1/transittimes`; `carrierCodes[]`, `recipients[]`, `packagingType`; response `transitTimes[].transitTimeDetails[].commit.transitDays`; day counts are enums. `dateDetail` supplies local date/time without an offset. | Replaced invented singular fields / `services[]` / numeric transit schema. No invented UTC delivery date. |
| [Address Validation](https://developer.fedex.com/api/en-us/catalog/address-validation/docs.html), [OpenAPI](https://developer.fedex.com/wirc/json/api_groups/AddressValidation/Addresses-Resource.json) | POST resolve; maximum 100; resolution tokens, attributes and customer messages. Normalization/classification does not itself prove validity or deliverability. Published country coverage does not include Egypt. | Requests resolution tokens; reads `streetLinesToken` / `postalCodeToken`; requires positive resolution evidence, rejects ambiguous multiplicity/country mismatch, and fails unresolved results. |
| [Comprehensive Rate documentation](https://developer.fedex.com/api/en-us/catalog/comprehensive-rate/docs.html), [change log](https://developer.fedex.com/api/en-us/catalog/comprehensive-rate.html) | Account estimates/One Rate entitlement; `rateDisplayOption` change January 30, 2026; packaging update February 2026. | **Irrelevant to execution:** `GetQuoteAsync` is unsupported and performs no rating HTTP. No price/payment authority changed. |
| [Pickup](https://developer.fedex.com/api/en-us/catalog/pickup/docs.html) | Pickup availability/create/cancel have separate dispatch, confirmation and scheduling contracts. | Adapter scheduling remains unsupported; a shipment pickup type does not dispatch a courier. Corrected false scheduling declaration. |
| [Quotas/rate limits](https://developer.fedex.com/api/en-us/guides/ratelimits.html) | Project 1,400/10 seconds; Track default 100K/day; 429 can mean daily quota until midnight GMT or short-window throttling. OAuth IP penalties can yield 403 for ten minutes. No fixed publication date supplied. | Removed immediate throttling retry. Bounded read-only retries respect Retry-After; absent or >30-second Retry-After returns the failure. Mutation POSTs never retry. These are local policies, not a distributed quota scheduler. |
| [Best practices](https://developer.fedex.com/api/en-us/guides/best-practices.html) | HTTPS, secure/rotatable credentials, token reuse, code-based errors, weights/amounts at most two explicit decimals, integer dimensions. | Credential rotation cannot reuse a stale cache entry; transport is bounded and secrets are not echoed through network exceptions. Measurement precision is validated rather than silently rounded. |
| [Sandbox virtualization](https://developer.fedex.com/api/en-us/guides/sandboxvirtualization.html) | Predefined responses may not vary with inputs; tokens still required; Ship/Track/Rate/Availability/Address/Pickup virtualized. | Neither mocks nor a future virtualized reply establish real route/account entitlement. No sandbox API was called. |

### Applicable 2026 updates

| Published / effective date | Official evidence | Assessment and disposition |
|---|---|---|
| June 4 / July 1, 2026 | [Web Services maintenance-only transition](https://developer.fedex.com/api/en-us/announcements/Act_now_Migrate_to_FedEx_APIs.html) | **Already REST.** No FedEx SOAP/Web Services executor was found. No migration required. The later notice describes temporary continued operation, not unconditional availability or complete retirement on that date. |
| May 30, 2026 production change; June 5 announcement | [Ship change log](https://developer.fedex.com/api/en-us/catalog/ship.html), [regulatory launch](https://developer.fedex.com/api/en-us/announcements/regulatory_api_launch.html), [regulatory compliance](https://developer.fedex.com/api/en-us/catalog/ship/docs.html#regulatory_compliance) | `requestedShipment.customsClearanceDetail.commodities[].regulatoryDetails[]`; CPSC data for applicable U.S. imports and EU_DE_MINIMIS identifiers for relevant low-value B2C EU27 imports, effective July 2026 for the latter. Full CPSC data requires stored Regulatory API profiles; inline disclaim/reference differ. **Blocked:** no FedEx customs/regulatory mapping or verified profiles. Cross-border/customs creation fails before HTTP. Same-country creation outside the reviewed US/CA domestic mapping subset is also blocked. This subset is a local containment policy, not a claim FedEx lacks other routes. |
| January 30, 2026 | [Comprehensive Rate change log](https://developer.fedex.com/api/en-us/catalog/comprehensive-rate.html) | Optional `rateDisplayOption`: LOWER_RATE default, SELECTED_RATES_INCLUDING_F1R, SELECTED_RATES_EXCLUDING_F1R. **Irrelevant:** rating not implemented. |
| Announcement index lists February 23; linked notice and Ship production log date February 24, 2026; Comprehensive Rate log gives February only | [Extra Small Box notice](https://developer.fedex.com/api/en-us/announcements/FedEx_Extra_Small_Box_2_24_2026.html), [Ship log](https://developer.fedex.com/api/en-us/catalog/ship.html) | `FEDEX_EXTRA_SMALL_BOX` for eligible One Rate Express/provided packaging, at most 50 lb. FedEx explicitly says existing integrations need no change unless adopting it. **Irrelevant:** adapter uses YOUR_PACKAGING and does not implement One Rate. No speculative feature added. |
| March 25, 2026 | [AIV timestamp standardization](https://developer.fedex.com/api/en-us/announcements/AIV_Time_Stamp_Normalization.html) | Missing-time AIV eventCreateTime / previousEvents / dateTimeRange becomes midnight UTC. **Irrelevant:** no FedEx webhook/AIV parser or subscription executor; REST scan timestamps are not this contract. |
| August 12, 2026 | [Ship log](https://developer.fedex.com/api/en-us/catalog/ship.html), current [Track documentation](https://developer.fedex.com/api/en-us/catalog/track/docs.html) | Authenticated Delivery introduced for U.S./Canada; Basic Track returns TRACKING.AUTHENTICATEDDELIVERY.ERROR instead of FAD tracking data. **Unsupported:** no FAD special-service mapping or secured AIV flow. Error responses cannot be admitted as delivered. |
| September 21, 2026 notice; full enforcement May 1, 2027 | [ACAS notice](https://developer.fedex.com/api/en-us/announcements/air_cargo_advanced_screening.html) | Additional shipper/consignee data for certain international air shipments. **Blocked** with international mapping; no invented regulatory fields or claimed compliance. |
| January 10/12, 2026 Ship log | [Ship log](https://developer.fedex.com/api/en-us/catalog/ship.html) | German handling icons and Mexican label/AWB terms are provider-generated label changes; those creation lanes remain outside this reviewed adapter mapping subset. No local label-layout rewrite required. |

## 2. Source-code findings and fixes

Path abbreviations: **A** FedExAdapter.cs; **C** FedExContract.cs; **O**
FedExOptions.cs; **R** FedExResponseAdmission.cs; **T** FedExTransport.cs;
**H** new FedExHttpSafety.cs, all under
`PointOfSale.Infrastructure/Shipping/Adapters/`.

| Finding / severity | Root cause and evidence | Changed production path / correction | Regression evidence |
|---|---|---|---|
| Unsafe mutation retry / critical | T retried POST on timeout/network/408/429/5xx and 401. Official customer transaction header is correlation, not provider-enforced deduplication. | T: only mapped read operations retry; shipment/pickup mutations have one attempt, including 401. Program disables shared retries for FedEx clients. | CreationPost tests: failures, malformed/async success and streamed-body timeout each attempt exactly one POST. |
| Credential cache / high | O key omitted secret version, grant and child identity; forced concurrent 401 refresh discarded newer tokens. | O: hashed credential/grant/child tuple plus account/environment/client; rejected-token-aware refresh under per-key semaphore. | Concurrent acquisition/refresh, delayed old 401, secret/child rotation, rejected credentials, invalid tokens and cancellation. |
| Unbounded HTTP / medium | Full body strings and ResponseHeadersRead without a body deadline; free endpoint accepted for certified DTOs; exception text echoed. | H/O/T/Program: 64 KiB OAuth / 8 MiB transport limits; linked 15-second attempt deadline covers body; exact POST/DTO/path binding; official environment base URI; no redirects; sanitized network failures; bounded delayed read retries. | Oversized body, endpoint rejection before OAuth, Retry-After/no-header/daily-quota cases, cancellable waits and body timeout. |
| Tracking identity / high | R checked response IDs internally; A never compared the requested number. | A/R: exact request/response binding, one complete result/one package match, per-result errors rejected. | Internally consistent unrelated response, invalid request identifiers, duplicate results and provider error tests. |
| Tracking lifecycle/time / high | DE/SE prematurely mapped terminal Failed; RS prematurely mapped completed Returned; offsetless/date-only scans assumed UTC. | R: ambiguous exception/return states fail closed; documented derivedStatusCode consumed; explicit timestamp offset required; UTC sort/dedup; terminal outcome requires consistent latest scans. | Exception/return cases, date-only/offsetless/malformed times, derived AR→IT, UTC ordering, duplicate scans and conflicting terminal evidence. |
| Incomplete shipment request / high | PackageCount/COD/customs/insurance could be silently omitted; dimensions clamped/rounded; default scheduled pickup; overlong internal references. | A/C: single domestic prepaid mapping subset only; unsupported services/data blocked; actual positive integer dimensions required, no rounding/defaults; weight/amount precision; explicit configured pickupType; bounded hashed customer reference and validated invoice reference; declared value mapped; current enums and nine-digit account validated. | Missing/negative/fractional/overflow dimensions, package count, COD, cross-border/other-country regulation, insurance, weight precision, account, ASCII, service, pickup and identity guards. |
| Incorrect creation admission / high | Pending status, MIME label format and metadata array were incompatible with shared persistence admission; insufficient piece/document consistency. Tiny response allowlists rejected normal official fields. | A/R: ReadyForPickup, `pdf`, metadata object; exact single-piece/master/service/document binding; Base64 PDF signature and encoded LABEL evidence required; documented response fields admitted. | Shared admission invoked on a real adapter mock result; mismatched master/service/document/piece count and non-PDF/URL-only label rejected. |
| Availability contract / medium | Singular carrierCode/recipient, shipment-only package fields, missing packaging; invented services/numeric transit/deliveryDate response. | A/C/R: official carrierCodes/recipients/packaging and transit address/package shapes; declared value preserved; nested transit schema/enums; unknown day counts stay absent; local date is not converted into UTC. Multi-piece and cross-border combinations blocked. | Official JSON request assertions, transit enum/empty/unknown cases, local date absence, unavailable requested-service restriction. |
| False address validity / high | Invented streetLines/postalCode response fields; equality/classification promoted normalized input to valid. First of multiple returned addresses selected. | A/C/R: request/read official tokens; positive standardized resolution evidence required; unknown remains failure; multiplicity rejected; country bound; suggestion-only warning. | Realistic token/attribute fixtures, unresolved/DPV cases, classification/schema admission checks. |
| False capabilities / medium | Definition advertised webhook/pickup/cancel stubs; registry empty credential descriptors could describe missing credentials as Available. Stale comments denied all HTTP execution. | FedExCarrierDefinition/CarrierCapabilityRegistry/A/O/Program: stubs marked false, three required credential descriptors, truthful selected-runtime comments. NotCertified retained. | Capability/credential/validation gate tests; unsupported operations execute no transport. |

### Actual callers, durable boundary and package provenance

- API shipment callers and orchestration enter `CarrierShipmentService`; it uses
  `CarrierIntegrationAuthority`, the DI `ShippingCarrierRegistry`, tenant carrier
  configuration, canonical origin/geography/customs restoration, durable request
  ID and registration-attempt claim before external mutation. Credentials are
  decrypted server-side by the existing authority. No secret store was changed.
- `ReconcilePendingCarrierRequestAsync` performs lookup and returns
  `EXTERNAL_REGISTRATION_RECONCILIATION_REQUIRED` when unverified; it never posts a
  second creation. FedEx lookup returns null. This boundary was preserved.
  Its persistence behavior was source-reviewed, not database-certified here.
- `SyncTrackingAsync` resolves a tenant shipment/configuration, calls the adapter
  outside its persistence transaction, rechecks current shipment/configuration
  and applies shared lifecycle authority. Adapter identity binding is required
  because the neutral tracking result has no requested identity field.
- Actual packed-parcel evidence exists in `ParcelMeasurement.cs` and
  `ShipmentParcelEvidenceService.cs`: tenant/shipment/order/content-bound,
  measured-actor evidence with restoration validation. Its numeric admission has
  explicitly MyDHL-specific constraints. It was **not reused as FedEx authority**.
  Current shared create paths still have legacy nominal weight, no complete
  FedEx origin postal/state or packed-parcel/durable request snapshot mapping.
  The NotCertified/service-level gates and adapter dimension/request guards
  contain these paths. Production activation requires a separately evidenced
  FedEx mapping of actual package/origin/account intent; this report does not
  declare the existing DHL snapshot boundary sufficient for FedEx.
- Quotes remain unsupported through ShippingQuoteService/checkout callers;
  standalone label fetch, pickup, credential certification and webhooks remain
  blocked. Stored-label retrieval is shared persistence retrieval, not a FedEx
  label-refetch implementation. Typed address/availability methods are adapter
  methods; no normal API/checkout caller was found. No new caller was exposed.

## 3. Automated local/mock results

No selected test instantiates the MAIN API or uses a database or real carrier.
Final assembly was rebuilt from current source in a separate
`FedExRemediation` configuration, preserving running-process binaries.

```powershell
dotnet build "PointOfSale.Tests\PointOfSale.Tests.csproj" -c FedExRemediation --no-restore -v:minimal
dotnet test "PointOfSale.Tests\PointOfSale.Tests.csproj" -c FedExRemediation --no-restore --filter "FullyQualifiedName~FedEx|FullyQualifiedName~CarrierNeutralEndpointContractsCertificationTests|FullyQualifiedName~CarrierIntegrationAuthorityCertificationTests.CarrierCreation_MustUseBackendOrderShippingMethodConfigurationAndSqlPersistence|FullyQualifiedName~CarrierIntegrationAuthorityCertificationTests.CarrierResponseAdmission_MustRejectForgedTrackingLabelsMetadataAndStatusBeforeSqlTruth" --logger "trx;LogFileName=fedex-remediation-final.trx" --results-directory "C:\Users\fadya\AppData\Local\Temp\opencode" -v:minimal -clp:ErrorsOnly
dotnet build "PointOfSale.API\PointOfSale.API.csproj" -c FedExRemediation --no-restore -v:minimal -clp:ErrorsOnly
dotnet build "PointOfSale.Tests\PointOfSale.Tests.csproj" -c FedExRemediation --no-restore -v:minimal -clp:ErrorsOnly
git diff --check
```

| Check | Actual result |
|---|---|
| Initial full test-project build (also Core/Infrastructure/API) | Passed; 0 errors, 460 warnings reported across the existing projects/test suite. |
| First focused run | 122 passed, 3 failed, 0 skipped, total 125. Two new unresolved-address cases exposed neutral Unknown rejection; handling/test expectations corrected to explicit fail-closed. One fixture lacked required U.S. state; fixture corrected. |
| Second focused run | 125 passed, 0 failed/skipped. |
| Final expanded run after further schema/precision/timeout corrections | **137 discovered/executed/passed, 0 failed, 0 skipped**. |
| Final API and test-project incremental builds | Both succeeded; each reported 0 warnings / 0 errors. |
| Diff whitespace check | Passed. Git emitted line-ending advisories, not diff-check errors. |

Final TRX:
`C:\Users\fadya\AppData\Local\Temp\opencode\fedex-remediation-final.trx`.
Inspected final counters: total/executed/passed **137**, failed/error/timeout/
aborted/notExecuted **0**. The two shared source-only checks were selected because
the FedEx result and named HTTP-client composition affect registration admission;
no broad database, cancellation or cross-carrier suite was run.

Changed tests:
- `PointOfSale.Tests/Architecture/FedExOfficialContractCompatibilityTests.cs`
- `PointOfSale.Tests/Architecture/CarrierNeutralEndpointContractsCertificationTests.cs`
- `PointOfSale.Tests/Infrastructure/FedExEndpointRuntimeIntegrationTests.cs`
- `PointOfSale.Tests/Infrastructure/FedExResponseAdmissionTests.cs`
- `PointOfSale.Tests/Infrastructure/FedExTransportRuntimeTests.cs`

Changed production paths: the seven adapter-directory paths A/C/O/R/T/H and
`FedExCarrierDefinition.cs`, `PointOfSale.Infrastructure/Shipping/CarrierCapabilityRegistry.cs`
(FedEx branch only), and `PointOfSale.API/Program.cs` (FedEx comment/HTTP registration only).

## 4. External prerequisites and verification limitations

- Merchant project/API enablement, shipping account binding, environment-specific
  credentials, customer grant type, service/route entitlement, onboarding and
  production approval were not accessed or verified. A token/local syntax check
  cannot prove them. Existing NotCertified operational gates remain authoritative.
- Complete FedEx packed-parcel/origin/account request evidence and approved
  recovery reconciliation remain prerequisites to activation. No schema migration
  or speculative new snapshot design was introduced. Prior uncertain attempts
  must remain reconciliation-required; correlation does not permit recreation.
- Cross-border customs/regulatory, COD, insurance, One Rate/new packaging, FAD,
  AIV/webhooks, pickup, cancellation, quote execution, standalone label fetch,
  async/multi-piece creation and reference recovery remain unsupported or blocked.
- Existing admission intentionally rejects unknown lifecycle codes/ambiguous
  matches; it does not guess a delivery/return/failure status. Unsupported offsetless
  REST scans cannot be made UTC using the unrelated AIV timestamp announcement.
- Public narrative/API-reference fragments sometimes require portal JavaScript.
  The general API reference page did not expose its scan-code/postal tables to
  static retrieval. Detailed Ship/Track/Address/Availability OpenAPI resources
  were accessible. Full regional coverage was not inferred from unavailable tables.
  Ship narrative cancellation wording conflicts with the OpenAPI endpoint's
  before-tender restriction; cancellation remains unsupported. Label URL lifetime
  descriptions vary (12/24 hours); creation requires encoded data instead of relying
  on durable availability of that URL. PDF checks establish Base64/PDF signature,
  not printer rendering or provider label certification.
- This report supersedes historical FedEx development-certification claims only
  as current review evidence; the historical artifacts were preserved.

## 5. Scope confirmation and acceptance

**No real FedEx sandbox or production API requests occurred.** Only public
documentation was fetched. No MAIN business database access/writes, migrations,
test databases, clones, dumps, backups, process stops/restarts, API activation,
merchant shipment mutations, unrelated carrier implementation/report edits,
cancellation closeout/report edits, staging or commits occurred. No subagents
were used. Existing work and the Git index were preserved.

Official generation/updates, executable callers and safe corrections were
reviewed; focused verification and the report are complete. **Production/account
certification and activation remain blocked by the explicit prerequisites above.**

## 6. Canonical shipment evidence wiring addendum — 2026-10-11

### Confirmed gaps and production changes

Current source confirmed the three section-2 gaps. FedEx could still reach both
shared nominal-weight builders; parcel recording/restoration and request snapshots
were DHL-only; warehouse-origin evidence had no durable postal value and the
request used administrative display names instead of canonical state codes.

The production path is now:

```text
ShipmentService canonical order-bound intent / legacy CreateShipmentWithCarrierAsync
→ committed PendingExternalRegistration shipment
→ existing parcel-evidence PUT → ShipmentParcelEvidenceService
→ ShipmentExternalRegistrationRequestedConsumer / existing registration caller
→ ExecuteCarrierForExistingShipmentAsync → ExecuteFedExRegistrationAsync
→ canonical parcel/origin/destination/account admission
→ ClaimShipmentWithEvidenceAsync (attempt + normalized snapshot + SHA-256)
→ FedExAdapter shared official request builder → existing transport/response admission
```

- **Parcel facts:** new `FedExParcelMeasurements` restores the existing
  `ParcelEvidenceJson` format against tenant, shipment, order, item/product/variant/
  quantity contents, recorded actor and UTC recording time. Actor ownership is
  checked in SQL; recording requires an active same-tenant actor with
  `shipping.fulfill`. Exact unit conversions must produce positive integer CM
  dimensions **1–999** and positive KG weight with at most two decimals. No DHL
  range/rounding rule, legacy weight, catalog measurement or invented default is
  used. Missing, malformed, foreign, changed-content, future-dated or unsupported
  measurements reject before claim/HTTP. The recorded input and normalized values
  must agree. DHL's existing measurement implementation remains byte-identical.
- **Origin facts:** the existing order warehouse selection, tenant-owned active
  warehouse, Warehouse-owned admitted `AddressAdmissionEvidence`, country profile,
  canonical administrative place and locality provide origin authority. The origin
  DTO now carries nullable postal and administrative codes inside its existing
  immutable JSON snapshot. FedEx validates the source before freezing a new origin;
  invalid evidence cannot poison an immutable snapshot. Existing origin snapshots
  lacking the required postal fact reject rather than being enriched from config.
- **Destination facts:** shipment destination must match the canonical order's
  admitted address identity, catalog/country/state/locality, street, postal value
  and locality presentation. State/province codes come from active canonical
  administrative places in that country, not display names. US ZIP and Canadian
  postal syntax, supported state/province codes and the existing US/CA domestic
  subset are enforced. Missing geography, cross-border/COD/unsupported input and
  incomplete contact/address fields remain closed through the existing builder.
- **Account facts:** the existing `ICarrierIntegrationAuthority` resolves credentials
  for the tenant/FedEx configuration. Configuration ID, carrier, enabled state,
  nine-digit account, configuration/credential environment, and geographic
  environment/tenant account scope are admitted. The account is never selected by
  shipment input. The claim resolves configuration again and compares account,
  environment and exact official request, including configured pickup type.
- **Durable facts:** existing `CarrierRequestId`, `CarrierRequestSnapshotJson` and
  `CarrierRequestFingerprint` are sufficient; no new shipment/request table or
  request-evidence columns were added. The snapshot includes tenant/shipment/order,
  canonical configuration/account/environment/endpoint, parcel source JSON,
  canonical context, admitted neutral inputs, the complete normalized
  `FedExShipmentRequest`, attempt number and start time. SHA-256 covers the exact
  serialized snapshot. Attempt time is normalized to PostgreSQL microsecond
  precision so restoration compares the actually persisted value. No credential
  secret, OAuth token or raw provider response is stored.
- **Claim and recovery:** the existing conditional SQL claim/cancellation barrier is
  retained. FedEx locks order then shipment, rereads authoritative evidence and
  configuration, and writes attempt/snapshot/fingerprint in the same transaction
  before adapter/OAuth/HTTP execution. Existing external result admission and
  fulfillment persistence remain shared. Replays compare snapshot/context/account/
  environment/fingerprint, but a valid snapshot never authorizes another POST.
  Prior attempt count or timestamp without completion remains reconciliation-required
  even without parcel/snapshot evidence or when new-creation readiness is blocked.
  FedEx reference recovery remains unsupported; no replacement creation is issued.

A directly blocking shared persistence defect was also exposed: geographic order
audit insertion modified the change tracker while enumerating Orders. Materializing
that enumeration with `ToList()` preserves all validation/immutability/audit guards
and permits the canonical admitted order fixture/creation path to persist.

Affected production paths are `CarrierShipmentService.cs`, new
`CarrierShipmentService.FedExEvidence.cs`, new `FedExParcelMeasurements.cs`,
`ShipmentParcelEvidenceService.cs`, `IShippingCarrierAdapter.cs`,
`Adapters/FedExAdapter.cs` (shared builder visibility only),
`Geographic/GeographicRepository.cs`, `Core/Entities/Geographic/GeographicEntities.cs`,
`Data/Configurations/Geographic/GeographicConfigurations.cs`, and the one-line
`Data/ApplicationDbContext.cs` enumeration correction.

### Minimal schema justification and requested MAIN application

The only insufficient persistence was **warehouse postal source evidence**:
`GeographicAddressCandidate.PostalCode` already existed, but geographic admission
discarded it; Warehouse and the origin snapshot had no recoverable authoritative
postal value. An address hash cannot recover a postal code. The smallest correction
is one nullable `PostalCode varchar(32)` on append-only `AddressAdmissionEvidence`.
Only Warehouse admissions populate it. Existing records are not backfilled.

Migration source:
`PointOfSale.Infrastructure/Migrations/20261011180000_FedExWarehousePostalEvidence.cs`.
The existing model snapshot has the same one-property addition. The migration
reuses the preceding generated immutable target model, adding that property only;
offline tests verify operation scope, migration discovery, and exact current-model/
snapshot agreement without connecting to PostgreSQL.

The user's follow-up explicitly authorized applying this migration **after
verification and before this addendum**, superseding the original no-database/
no-migration restriction for this bounded step. A locally built guarded utility
verified MAIN `192.168.1.5:5432/PosEcommerceDb_Baseline`, admitted only the reviewed
AddColumn operation and exactly this pending migration, then executed:

```powershell
dotnet run --project "C:\Users\fadya\AppData\Local\Temp\opencode\FedExPostalMigration.csproj" -c FedExCanonicalEvidence --no-build --no-restore -- --apply
```

- Before: **50 applied**, last `20261011121839_ShipmentParcelRequestEvidence`;
  exactly `20261011180000_FedExWarehousePostalEvidence` pending.
- After: **51 applied**, last `20261011180000_FedExWarehousePostalEvidence`;
  **0 pending**, `HasPendingModelChanges=False`.
- Actual catalog: **nullable character varying(32)** postal column.
- All **18** sorted `(Id,xmin)` table fingerprints/counts exactly unchanged:
  orders/items, shipments, warehouses, address admission, carrier configuration,
  payment/refund/settlement and outbox, ledger, stock, cancellation, carrier
  remittance and collection evidence. Orders **15**, shipments **8**, warehouse
  rows **14**, stock movements **198**; cancellation/remittance/collection rows **0**.
- `AddressAdmissionEvidence` has **0 rows**, and **0 non-null postal values**.
  No address, parcel, configuration or merchant data was fabricated or backfilled.

### Fresh focused verification

```powershell
dotnet build "PointOfSale.Tests/PointOfSale.Tests.csproj" -c FedExCanonicalEvidence --no-restore --no-incremental --disable-build-servers -m:1 -p:UseSharedCompilation=false -v:quiet /clp:ErrorsOnly
```

Final fresh API/Core/Infrastructure/Tests build: **succeeded, 462 warnings, 0 errors,
1m25.16s**. Existing project/test analyzer, nullability and obsolete-API warnings
remain. A separate affected API build also succeeded (**0 warnings, 0 errors**).
No Debug runtime binary or process was activated for this wiring task.

Final focused filter and commands are retained in
`C:\Users\fadya\AppData\Local\Temp\opencode\fedex-canonical-verification.py` and
`fedex-canonical-evidence-final-{discovery,tests,build,api-build}.log`.
Discovery and execution use the fresh `bin/FedExCanonicalEvidence/net9.0` assembly
with `--no-build --no-restore`, not historical FedEx remediation binaries.

| Selected cases | Passed | Failed | Skipped |
|---|---:|---:|---:|
| New `FedExCanonicalEvidenceTests` | **37** | **0** | **0** |
| Existing `ParcelMeasurementTests` | **21** | **0** | **0** |
| Existing external-registration safety tests | **5** | **0** | **0** |
| Canonical origin authority checks | **4** | **0** | **0** |
| Directly affected shared creation/response-admission checks | **2** | **0** | **0** |
| Existing FedEx creation mapping/HTTP-pipeline cases | **20** | **0** | **0** |
| Existing FedEx shipment/label response admission cases | **12** | **0** | **0** |
| Existing FedEx capability/request/readiness/recovery boundaries | **4** | **0** | **0** |
| **Total discovered/executed** | **105** | **0** | **0** |

Final TRX: `C:\Users\fadya\AppData\Local\Temp\opencode\fedex-canonical-evidence-final.trx`;
runner duration **13s**. XML total/executed/passed **105**, every failure/error/
timeout/aborted/notExecuted counter **0**; individual outcomes and discovery counts
were inspected. Tests use isolated EF InMemory persistence and deterministic HTTP
handlers; no provider call or PostgreSQL test database was used. The pre-send
handler reads independently persisted attempt/snapshot/fingerprint, compares the
normalized request to the actual HTTP body, asserts tenant/order/shipment/config/
account/origin/measurement facts and secret absence, and counts requests. Timeout
and rejected response replays assert exactly **one** creation POST. Account/config
changes at claim assert **zero** POSTs and no claimed attempt. Both legacy and
canonical registration paths are exercised; readiness and response guards remain.

Intermediate fixture failures exposed the order-audit enumeration defect and
incorrect fixture attempts to rewrite append-only origin evidence/immutable order
warehouse selection; those fixtures now append new evidence or construct a
contradictory shipment candidate instead of weakening guards. A pre-application
migration-discovery check exposed a duplicated inherited DbContext attribute;
it was corrected and explicit offline discovery coverage added before migration
application. Intermediate runs are not counted as final passing evidence.

### Readiness, preservation and stop

**Code-level wiring is complete for the supported single-parcel domestic mapping;
external account certification and production readiness remain CONDITIONAL.**
The real `NotCertified` capability, empty supported-service list and every
unsupported-operation boundary remain intact. A syntactically valid account,
complete package, normalized snapshot or applied column does not establish account
entitlement. Cross-border/regulatory, COD, multi-piece, quotes, pickup, cancellation,
standalone label refetch and reference recovery remain unsupported/blocked.

The current MAIN database cannot establish a usable warehouse origin: it has **no
address-admission evidence rows**. Existing warehouse street strings cannot supply
missing admitted postal/state facts. Authorized Warehouse-owned geographic
admission and actual shipment-owned measurements must be recorded through their
authorities; this task created neither merchant evidence nor certification data.
Old incomplete snapshots remain closed. Positive provider confirmation and reviewed
reconciliation authority are still external/future blockers. The tests prove local
save ordering and deterministic execution; they do not certify production SQL lock
races or live FedEx account/route entitlement.

No subagents, staging or commits were used. **26 protected existing files** were
compared byte-for-byte with the session baseline, including other carrier adapters,
DHL parcel/request code, FedEx transport/contracts/admission/capability/configuration,
Manual COD/refund lineage, cancellation implementation/report, earlier assertion
corrections and Program.cs. The empty Git index is unchanged; `git diff --check`
passes. No customer shipping-price authority, other carrier implementation,
frontend, cancellation report or unrelated report was edited by this wiring task.

The earlier cancellation-closeout interpretation in this conversation was halted
when the user clarified FedEx scope; its runtime/database/test work is not counted
as FedEx evidence. After that clarification, no API restart or FedEx activation
was performed. The only subsequently authorized MAIN mutation was the migration
above. **No FedEx sandbox/production request or real merchant shipment mutation
occurred. STOP after this bounded wiring, verification, migration and addendum.**

## 7. Canonical warehouse address admission workflow — 2026-10-11

**The previous operator workflow was insufficient. The missing authenticated API
workflow is now implemented and verified; genuine operator data entry remains a
prerequisite. FedEx remains `NotCertified`.** This section supplies the operational
entry point missing from section 6, without creating any real evidence.

### Investigation and bounded implementation

- `PointOfSale.API/Controllers/WarehousesController.cs` and
  `PointOfSale.Web/src/pages/Warehouses.jsx` support tenant warehouse selection and
  street/contact maintenance (`/warehouses`, `GET/PUT /api/warehouses/{id}`), but
  their prior requests contained no canonical geography or postal/admission fields.
- `AdminGeographicAdministrationController` at `/api/admin/geographic` administers
  catalog drafts, country profiles and publication; it did not admit Warehouse-owned
  addresses. No existing operator caller created Warehouse-owned admission evidence.
- The registered `IAddressAdmissionService` → `GeographicResolver` →
  `GeographicRepository.AddAddressAdmissionEvidenceAsync` already appended evidence
  and retained warehouse postal input. Its foundation admission check only counted
  published IDs; country postal policy, place-kind/country and hierarchy validation
  were absent from warehouse admission. Carrier mapping had separate country rules.
- Existing `ApplicationDbContext` append-only guards cover `AddressAdmissionEvidence`
  and `GeographicAudit`; persisted geographic origin fields have immutability guards.
  Those guards are preserved. Corrections append new evidence, never enrich old rows.

Minimal production additions:

1. Partial `WarehousesController.AddressAdmission.cs`, request/response DTOs in
   `DTOs/WarehouseAddressAdmissionDtos.cs`, and scoped
   `Services/Inventory/WarehouseAddressAdmissionService.cs`. The existing controller
   only gains `partial`; Program adds one scoped service registration. Both new
   actions use the existing **`inventory.approve`** management permission in
   `PosEndpointPermissionCatalog`, inherited authentication, current SQL operator
   validation and explicit tenant-owned active/nondeleted warehouse checks.
2. The service confirms the actual saved warehouse street, name and phone, binds
   tenant/owner internally, takes the genuine postal value and selected place IDs,
   and delegates persistence to **the existing `IAddressAdmissionService`**. Admission
   and an actor/reason/correlation/fingerprint-linked geographic audit commit in one
   transaction. A PostgreSQL warehouse row lock serializes this admission with
   warehouse edits. No direct SQL evidence insertion or new persistence model.
3. Warehouse-only checks in `GeographicResolver` enforce active/unretired/current
   published place kinds, same-country primary hierarchy, country/profile ISO
   agreement, required delivery/operations levels, and published postal policy/regex.
   Postal input is trimmed, never invented or case-guessed; it is limited to 32
   characters, rejects interior controls and requires a full regex match with a
   finite timeout. Missing/invalid rules fail closed. Other owner-domain admission
   behavior and other carriers remain unchanged.
4. `Geographic/WarehouseAddressFingerprint.cs` binds the confirmed street/contact,
   tenant/warehouse, lineage and postal input. The existing
   `NormalizationRuleVersion` field identifies this new admission fingerprint.
   A narrowly necessary check in `CarrierShipmentService.FedExEvidence.cs` verifies
   it: otherwise a later warehouse street edit before the first origin snapshot
   could silently reuse earlier address evidence. The new check rejects that case
   before freezing an origin. Historical evidence is not rewritten. FedEx adapter,
   transport, readiness gates, and request-claim requirements are untouched.

### Exact authorized operator procedure

Use an existing valid tenant-bound POS bearer session. **Owner, Admin and Manager**
currently have `inventory.approve`; Cashier and Warehouse roles do not. Both lookup
and admission independently reject missing, foreign, inactive, deleted or
unauthorized SQL operators. Foreign/inactive/deleted warehouses return **404**.

1. Select the correct active warehouse on **`/warehouses`** or obtain its ID with
   **`GET /api/warehouses`**. Verify its genuine contact name, phone and street.
   Correct these through the existing edit UI / **`PUT /api/warehouses/{id}`** if
   necessary. The admission operation requires `streetLine1` to match the saved
   `Address` after trimming; it does not replace warehouse data automatically.
2. Obtain published canonical country identities and country postal rules:
   **`GET /api/warehouses/{id}/address-admission/places`**.
3. Select the actual country, then fetch its administrative children:
   **`GET /api/warehouses/{id}/address-admission/places?countryPlaceId={countryId}`**.
   Choose the genuine state/province by its **canonical code** and ID, not by
   converting a display label. Fetch locality children with the same operation plus
   **`&parentPlaceId={administrativeAreaId}`**. Continue through district/zone when
   required by the profile. The response includes catalog release, place kind,
   canonical code, display name, ISO country, postal policy/regex and required levels.
4. Submit **`POST /api/warehouses/{id}/address-admission`** with JSON:

   ```json
   {
     "streetLine1": "<the warehouse's genuine saved street>",
     "postalCode": "<its actual postal code, matching the published country rule>",
     "countryPlaceId": "<country ID from lookup>",
     "administrativeAreaPlaceId": "<actual state/province ID from lookup>",
     "localityPlaceId": "<actual locality ID from lookup>",
     "districtPlaceId": null,
     "zonePlaceId": null,
     "reason": "<operator's actual verification/admission reason>"
   }
   ```

   This is a placeholder contract, not business data. Supply optional district/zone
   IDs when genuinely applicable/required. Street is at most 300 characters, postal
   at most 32, reason is nonempty and at most 1000. Postal may be omitted only when
   the actual published country policy permits it; FedEx US/CA origin still requires
   a valid explicit postal value and supported canonical state/province code.
5. **200** returns `warehouseId`, `addressAdmissionEvidenceId`, `catalogReleaseId`,
   admitted lineage, original trimmed `postalCode` and SHA-256 fingerprint. Retain
   the returned evidence ID. **400** returns a precise `code` and explanatory
   `message` (e.g. postal required/invalid, hierarchy invalid, missing profile/rules,
   street confirmation required). **403** rejects operator authority. Correct the
   actual facts or catalog prerequisite and submit a new admission; old evidence
   remains immutable. Repeated successful submissions append records.

The existing canonical origin resolver selects the latest admitted Warehouse-owned
record for the shipment/order's authorized warehouse, restores its postal field and
the selected administrative place's canonical code, and freezes the origin. New
admission does not replace a previously frozen shipment origin. An address admission
does not supply destination evidence, packed measurements, account certification or
the durable creation request/attempt; those requirements and `NotCertified` remain.

### Fresh isolated verification and preservation

```powershell
dotnet build "PointOfSale.Tests/PointOfSale.Tests.csproj" -c WarehouseAddressAdmission --no-restore --no-incremental --disable-build-servers -m:1 -p:UseSharedCompilation=false -v:quiet /clp:ErrorsOnly
```

Fresh API/Core/Infrastructure/Tests build: **succeeded, 462 warnings, 0 errors,
1m23.80s**. Build outputs use `WarehouseAddressAdmission`, leaving active Debug
outputs untouched. Focused `dotnet test` discovery/execution use this fresh assembly,
`--no-build --no-restore`, and the following filter:

```text
FullyQualifiedName~WarehouseAddressAdmissionTests|FullyQualifiedName~FedExCanonicalEvidenceTests|FullyQualifiedName~ShipmentOriginAuthorityTests|FullyQualifiedName~GeographicPersistenceAuthorityTests|FullyQualifiedName~PosPermissionAuthorizationTests
```

| Cases | Passed | Failed | Skipped |
|---|---:|---:|---:|
| New actual warehouse API/service/admission workflow regressions | **49** | **0** | **0** |
| Existing canonical FedEx evidence/readiness/claim boundaries | **37** | **0** | **0** |
| Existing canonical POS permission checks | **40** | **0** | **0** |
| Existing origin authority checks | **4** | **0** | **0** |
| Existing geographic persistence authority checks | **5** | **0** | **0** |
| **Final discovered/executed** | **135** | **0** | **0** |

The final TRX counters and every individual outcome were inspected: total/executed/
passed **135**, failure/error/timeout/aborted/notExecuted **0**, runner duration
**12s**. Artifacts under `C:\Users\fadya\AppData\Local\Temp\opencode`:
`warehouse-admission-final.trx`, `warehouse-admission-{build,discovery,tests}.log`,
and `warehouse-admission-verification.py` (exact commands/counter/preservation checks).

Tests call the real MVC actions, warehouse service, geographic authority and EF
repository with isolated synthetic InMemory data. US and CA admissions independently
restore the persisted postal and canonical state through the actual origin resolver.
They cover malformed/missing/unsupported postal/geography, real foreign-tenant
fixtures, inactive/deleted/unauthorized actors and warehouses, lookup lineage,
append-only evidence/audit, correction-by-append, frozen-origin immutability and
changed-street rejection. SQL locking/transaction behavior was reviewed in source;
no live PostgreSQL race or running HTTP server was exercised. The prior standalone
postal-preservation fixture was completed with genuine synthetic profile/hierarchy
prerequisites; its assertions were retained. An intermediate missing import and
overbroad fixture immutability assertion were corrected before this final passing run.

**7,110 protected pre-existing source/report files** compare byte-identical to the
task baseline; existing changes in the seven intentionally extended files were
preserved by bounded patches. The Git index is unchanged and `git diff --check`
passes. No frontend, other carrier, FedEx adapter/readiness, schema/migration or
unrelated report changes were made for this task. No subagents, staging or commits.

### Remaining prerequisites and stop boundary

The operator must supply the **actual warehouse identity, saved genuine street/name/
phone, postal value, published country/state/locality IDs and applicable required
levels, plus a genuine admission reason**. If the currently selected published
catalog lacks the actual country, authoritative state/province codes, primary
hierarchy or country postal/level rules, its authorized geographic administration
workflow must establish those facts first. Nothing here guesses codes, seeds catalog
places, derives postal data from street text, or silently validates incomplete rows.

The zero-row observation in section 6 is historical; this task **did not access the
business database or recheck its current contents**. **No real business data was
fabricated or written; no schema change/migration, backup/clone/test database,
process restart, provider activation or external provider request occurred.** The
source workflow is complete; operator data entry and geographic prerequisites are
the boundary. FedEx production certification remains blocked. **STOP.**
