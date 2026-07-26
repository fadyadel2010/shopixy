# GEO-10 — Provider Mapping Coverage & Production Readiness Certification

Status: **NOT PRODUCTION-CERTIFIABLE**  
Audit date: 2026-07-05  
Scope: implemented carrier-provider mapping coverage and runtime production readiness only

## Executive Summary

The implemented provider inventory is Manual, Bosta, Aramex, Mylerz, and DHL.
The GEO-9 runtime boundary is structurally correct: carrier-managed shipment
registration resolves provider geography before adapter I/O, persists immutable
shipment mapping evidence, and recovery reuses that evidence. Adapters do not
read the canonical catalog, SQL, aliases, hierarchy, localization, drafts, or
unpublished mapping candidates.

The platform is **not production-certifiable for external carriers**.

Repository and migration evidence contains no registered `CarrierProvider`
seed, no canonical country/place seed, no published carrier mapping release,
and no published mapping rows for Bosta, Aramex, Mylerz, or DHL. Therefore
repository-proven published mapping coverage is 0% for every external provider
and every environment/account scope. The administration runtime can stage and
publish data, but it has no provider-registration operation; provider rows must
already exist before mapping drafts can be created.

Official-contract compatibility also remains uncertified for Aramex, Mylerz,
and DHL. Bosta's create contract is conditionally compatible when governed
city/district mapping evidence is supplied. Aramex's implemented JSON/WCF
surface has not been certified against the frozen official WSDL/XSD. Mylerz
has no accepted public technical authority. DHL uses the correct MyDHL product
family and address shape, but the adapter has not been contract-certified
against the frozen 3.3.1 OAS and its account/country validation requirements.

One real runtime defect was found and hardened: mapping resolution selected a
release by `EffectiveFromUtc` and ignored append-only activation history. A
rollback could therefore activate an older release while runtime continued to
resolve the newer release. Resolution now requires an activated mapping
publication and selects the latest activation event, including rollback.

## Provider Inventory

| Provider | Adapter | External network | Geographic contract |
|---|---|---:|---|
| Manual | `ManualCarrierDefinition` | No | No provider mapping required |
| Bosta | `BostaAdapter` | Yes | Egypt; `districtId`, or `cityId` plus `districtName`; optional `zoneId` |
| Aramex | `AramexAdapter` | Yes | Text address with city, country code, postal code; official location/WSDL validation remains relevant |
| Mylerz | `MylerzAdapter` | Yes | Implemented text city/country/postal shape; official partner contract unavailable |
| DHL | `DhlAdapter` | Yes | MyDHL postal address: address lines, city, ISO country, postal code subject to country/product rules |

No ShipBlu, FedEx, UPS, or custom carrier adapter was found. They were not
audited.

## Coverage Matrix

Coverage is calculated from repository-controlled publication artifacts:
published and activated releases plus reviewed active mappings for
`ShipmentRegistration`. No such data artifact exists. A separately populated
production database was not supplied as audit evidence and cannot be presumed.

| Provider | Country scope | Supported adapter | Published mapping | Coverage | Missing coverage | Blocking | Warning / recommendation |
|---|---|---:|---:|---:|---|---:|---|
| Manual | Tenant operating countries | Yes | Not applicable | N/A | None; no provider identity | No | Keep Manual outside external mapping resolution |
| Bosta | Egypt | Yes | No evidence | 0% | Country, administrative area, locality/city, district, optional zone; sandbox and production tenant-account releases | **Yes** | Register provider, import/review Egypt coverage, publish per environment/account, prove Bosta alternative address rule |
| Aramex | Account-enabled countries | Yes | No evidence | 0% | All enabled origin/destination countries and required locality/administrative validation | **Yes** | Freeze official WSDL/XSD and certify each enabled country/account |
| Mylerz | Unknown from accepted official authority | Yes | No evidence | 0% | Country scope, city/area identifiers or accepted text rules, environments, account scope | **Yes** | Obtain vendor-issued current contract before publishing any mapping |
| DHL | MyDHL account-enabled countries | Yes | No evidence | 0% | All enabled countries, locality/postal requirements, sandbox and production account releases | **Yes** | Validate against MyDHL 3.3.1 OAS/reference data and publish only account-enabled country coverage |

Administrative-area, locality, district, and zone percentages cannot honestly
be subdivided because the denominator (published canonical catalog membership)
and numerator (activated reviewed mapping rows) are both absent from repository
data. The certifiable aggregate is nevertheless 0%, because no executable
published mapping evidence exists.

## Compatibility Matrix

| Provider | Official baseline | Required geographic/address evidence | Current runtime | Verdict |
|---|---|---|---|---|
| Manual | Internal no-op contract | Existing shipment address snapshot only | No external registration | **Compatible** |
| Bosta | `/api/v2`; create uses `apiVersion=1` | First line and recipient; `districtId`, or `cityId` + `districtName`; optional zone/coordinates | Fields are populated only from resolved shipment mapping evidence; adapter fails closed if the required alternative is absent | **Conditionally compatible; data blocked** |
| Aramex | Official `service_1_0` WSDL/XSD authority | Account credentials plus address lines, city, country code, postal code and provider validation | JSON/WCF-style endpoints and request DTO implemented; no current WSDL conformance proof | **Not certified** |
| Mylerz | Vendor/onboarding package required | Unknown | Assumed `/v2` REST surface with bearer auth and text address | **Not certifiable** |
| DHL | MyDHL 3.3.1 OAS/reference data | Address lines, city, ISO country, postal rules, account/product/customs fields | Correct product base URLs and Basic auth family; no 3.3.1 schema/reference-data certification | **Not certified** |

Resolved mapping items are currently forwarded as carrier-neutral evidence.
Bosta consumes city/district/zone legacy fields. Aramex, Mylerz, and DHL
continue to serialize text address fields and do not consume provider mapping
IDs. That is acceptable only where the certified official contract requires
text rather than provider IDs; it does not substitute for contract
certification or published country coverage.

## Geographic and Operation Coverage

Mapping resolution is invoked only for carrier-managed shipment registration
with operation key `ShipmentRegistration`. Quote, tracking, cancellation, and
credential-validation operations do not resolve canonical geography.

- Shipment registration: resolution boundary exists and runs before adapter I/O.
- Recovery/retry: immutable shipment mapping snapshot is reused.
- Tracking/cancellation: use admitted carrier shipment/tracking identity, not
  geographic mapping.
- Quote operations: currently use address text. This audit does not certify
  provider quote geography where a provider requires canonical/provider IDs.
- Manual shipment creation: intentionally bypasses external mapping/network I/O.

## Environment and Account Scope Coverage

Runtime resolution requires an exact match on:

- provider;
- Sandbox or Production;
- `TenantProviderAccount`;
- account scope ID equal to shipment tenant ID;
- tenant ID;
- operation `ShipmentRegistration`;
- country;
- effective and freshness windows;
- reviewed or manually approved active rows.

This is fail-closed and prevents cross-environment and cross-account fallback.
It also means every tenant/provider account needs explicit Sandbox and
Production releases where both are used. No such releases are present.

## Carrier Mapping Releases, Publication, and Activation

The administration runtime supports draft creation, mapping staging,
validation-state publication, rollback publication events, audit, sync runs,
candidates, and overrides.

Findings:

1. No repository seed/import artifact registers any implemented provider.
2. No repository artifact publishes a mapping release or mapping row.
3. No provider-registration administration endpoint exists.
4. Runtime previously trusted release status/time and ignored publication
   activation history.
5. Runtime now joins mapping releases to activated publication events and
   selects the latest activation, so rollback becomes effective at resolution.
6. Publication concurrency locking and explicit supersession of earlier
   publication rows are not implemented. Latest activation ordering prevents
   wrong-release resolution, but concurrent publication governance still needs
   operational proof before rollout.

## Carrier Resolution and Registration

`CarrierShipmentService` is the only business runtime mapping consumer. It:

1. requires immutable shipment canonical country/catalog evidence;
2. derives environment and tenant-provider-account scope from trusted SQL
   configuration and shipment tenant;
3. requests published mapping resolution;
4. persists release, scope, time, and provider items on the shipment;
5. writes success/failure Geographic audit evidence;
6. invokes the adapter only after successful resolution;
7. skips external I/O on mapping failure.

Manual fulfillment does not enter carrier registration and remains a valid
no-mapping provider.

## Forbidden Authority Verification

For all five adapters, static inspection and regression coverage confirm:

- no canonical catalog read;
- no EF or SQL lookup;
- no alias resolution;
- no hierarchy resolution;
- no localization resolution;
- no draft or sync-candidate resolution;
- no unpublished-release resolution;
- no direct `ICarrierGeographicMappingService` use.

The adapters are wire serializers. Resolution remains in
`CarrierShipmentService` and `CarrierGeographicMappingService`.

## Fail-Closed Matrix

| Condition | Result |
|---|---|
| Missing provider registration | `UnknownCarrierProvider`; adapter I/O skipped |
| Missing mapping/release | `PublishedMappingReleaseRequired` or `MissingRequiredProviderMapping`; adapter I/O skipped |
| Inactive provider | Treated as unknown |
| Inactive/expired mapping | Excluded |
| Stale release | `StaleMappingRelease` |
| Unpublished release | Excluded |
| Release without activated publication | Excluded after GEO-10 hardening |
| Wrong release after rollback | Latest activated publication selected after GEO-10 hardening |
| Wrong provider | Provider and mapping provider IDs must match |
| Wrong country | Mapping country must equal shipment country |
| Wrong environment | Exact Sandbox/Production match required |
| Wrong account/tenant scope | Exact kind, account ID, and tenant ID required |
| Ambiguous type mapping | `AmbiguousProviderMapping` |
| Partial required type set | Rejected when required provider-place types are supplied |
| Missing canonical shipment identity | `CARRIER_CANONICAL_GEOGRAPHY_REQUIRED` |
| Malformed/mismatched recovery snapshot | `CARRIER_GEOGRAPHIC_SNAPSHOT_INVALID` |

Residual warning: shipment registration currently supplies no
`RequiredProviderPlaceTypes`. Bosta independently enforces its alternative
district rule in the adapter. Other providers have no certified required-type
policy. This is not safe to broaden by guesswork; required types must come from
frozen official provider contracts before activation.

## Gap Analysis

### Blocking Issues

1. **Zero published mapping coverage** for every external provider.
2. **No repository-proven provider registration** and no provider-registration
   administration operation.
3. **No production database evidence** proving catalog, provider, release,
   publication, activation, country, environment, tenant-account, and mapping
   row counts.
4. **Aramex contract not certified** against the official WSDL/XSD.
5. **Mylerz contract unknowable** without vendor-issued documentation.
6. **DHL adapter not certified** against MyDHL 3.3.1 OAS/reference data.
7. **Bosta coverage absent**, despite a conditionally compatible request
   contract.

### Warnings

- Quote geography is text-based and outside the GEO-9 mapping operation.
- Required provider place-type policy is not modeled for Aramex/Mylerz/DHL.
- Publication locking/supersession needs deployment-level concurrency proof.
- Provider documentation for Bosta and Aramex has weak public lifecycle
  governance; repeat contract checks before release.
- Runtime audit evidence exists, but there is no coverage-report query/export
  proving denominators and missing nodes.

## Runtime Hardening

Modified:

- `PointOfSale.Core/Interfaces/Geographic/GeographicContracts.cs`
- `PointOfSale.Infrastructure/Geographic/GeographicRepository.cs`

Change:

- exposed mapping publication read authority through the repository;
- required activated mapping-publication evidence;
- selected the most recently activated publication, preserving rollback
  semantics and preventing effective-date selection of the wrong release.

No adapter, frontend, database schema, migration, or provider contract was
changed.

## Regression Tests

Added:

- `PointOfSale.Tests/Architecture/ProviderMappingCoverageProductionReadinessTests.cs`

Updated:

- `PointOfSale.Tests/Architecture/CarrierGeographicResolutionIntegrationTests.cs`
  to require activated-publication evidence in the existing GEO-9 freeze.

Frozen invariants:

- resolution requires latest activated publication and honors rollback;
- no migration silently seeds provider/mapping authority;
- implemented adapters remain isolated from catalog, SQL, alias, hierarchy,
  and mapping service authority.

Existing GEO-9 tests continue to freeze resolver-before-adapter placement,
scope/country/ambiguity checks, immutable recovery evidence, and provider-ID
isolation.

## Verification

Repository-local verification includes:

- implemented adapter/registry inventory;
- static provider endpoint, authentication, request-address, and operation
  trace;
- provider/mapping/catalog seed search;
- mapping administration and publication trace;
- mapping resolver and shipment registration/recovery trace;
- adapter forbidden-authority scan;
- focused GEO-10/GEO-9/Geographic test execution;
- solution build.

Verification result:

- Test-project build: passed with existing warnings, 0 errors.
- Focused GEO-10/GEO-9/Geographic slice: **48 passed, 0 failed**.

No live production database or provider credentials were supplied. This report
therefore refuses to infer deployed coverage or successful provider calls.

## Final Recommendation

Do not activate external carrier shipment registration in production.

Before a production go-live decision:

1. obtain/freeze accepted Aramex, Mylerz, and DHL contract artifacts;
2. register exactly the implemented providers through a governed deployment
   artifact or approved administration capability;
3. publish the canonical country catalogs;
4. import/review/publish complete per-country, per-environment,
   per-tenant-provider-account `ShipmentRegistration` mappings;
5. run a SQL coverage report proving required-level denominators, missing
   nodes, ambiguity, parent integrity, freshness, publication activation, and
   rollback;
6. execute credentialed sandbox contract tests, then controlled production
   smoke tests;
7. repeat GEO-10 certification against those deployment artifacts.

The next phase should be **provider documentation/contract closure and governed
mapping-data rollout**, not GEO-11 and not an architecture redesign.

## Production Readiness Verdict

**Manual: production-certifiable for no-op/manual fulfillment.**

**Bosta, Aramex, Mylerz, DHL: NOT production-certifiable.**

**Geographic Platform for all implemented providers: NOT
production-certifiable.**

## Explicit Success Answers

- Reviewed ONLY Provider Mapping Coverage? **Yes.** Adjacent runtime and
  provider contracts were inspected only where necessary to certify mapping
  compatibility.
- Runtime files modified: **Yes — 2**, limited to activated publication /
  rollback-correct resolution.
- Backend files modified: **Yes — 2 runtime files plus 1 regression test.**
- Frontend files modified: **No.**
- Providers audited: **Manual, Bosta, Aramex, Mylerz, DHL.**
- Coverage complete? **No. External-provider repository-proven published
  coverage is 0%.**
- Compatibility complete? **No. Manual is compatible; Bosta is conditional;
  Aramex, Mylerz, and DHL remain uncertified.**
- Blocking production issues? **Yes.**
- Runtime hardening required? **Yes; completed for publication activation and
  rollback-correct release selection.**
- Production-certifiable? **No, except Manual fulfillment.**
- Recommend next phase: **Close provider contract evidence and execute governed
  provider registration/catalog/mapping publication rollout, then rerun
  GEO-10. Do not begin GEO-11.**
