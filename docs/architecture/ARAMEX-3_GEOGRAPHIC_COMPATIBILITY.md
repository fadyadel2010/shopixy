# ARAMEX-3 — Geographic Compatibility

Audit date: 2026-07-06  
Scope: Canonical Geographic Resolution through Aramex shipment registration only

## Executive Summary

The Aramex adapter remains correctly isolated from EF, SQL, the Canonical
Catalog, hierarchy, aliases, localization, drafts, and mapping publication.
Carrier Geographic Resolution runs before adapter I/O and recovery reuses the
immutable shipment mapping snapshot.

The pre-audit registration boundary was nevertheless not geographically
compatible. It resolved provider mappings, then built Aramex destination
country, city, street, and postal data from legacy order/shipment text instead
of the admitted shipment snapshot. It did not bind the mapping release to the
shipment catalog release, did not enforce the published country profile, and
did not pass administrative area/state to Aramex.

Minimal hardening now binds resolution to the exact shipment catalog release,
enforces the active published country profile (supported country, required
delivery levels, street, and postal policy/format), returns canonical ISO2
country evidence, and builds destination street/city/state/country from the
immutable shipment snapshot and resolved evidence.

Aramex is still **not production compatible**. The current model has no
admitted shipment origin/pickup geographic snapshot; origin is taken from
free-text carrier configuration and can still default to Cairo/EG. Building,
company, landmark, and immutable postal evidence are also incomplete in the
carrier request path. GEO-10 additionally proves zero repository-controlled
published Aramex mapping coverage. These are blocking data/persistence gaps,
not adapter lookup defects, and were not papered over with invented authority.

## Geographic Contract Matrix

| Element | Runtime source after hardening | Verdict |
|---|---|---|
| Country | Resolved active country profile ISO2, bound to shipment catalog release | Compatible for destination |
| Administrative area/state/province | Immutable shipment administrative-area display snapshot | Compatible for destination when country profile requires it |
| Locality/city | Immutable shipment locality display snapshot | Compatible for destination |
| District | Canonical shipment lineage plus published provider mapping snapshot; Aramex wire has no separate district field | Compatible as resolution evidence; textual placement policy not implemented |
| Postal code | Legacy shipment `ShippingZipCode`, validated by country profile during resolution | Validation hardened; immutability gap remains |
| Street line 1 | Immutable shipment `DestinationStreet` | Compatible for destination |
| Street line 2 | Immutable shipment `DestinationStreet2` | Compatible for destination |
| Building | Snapshotted but not serialized into Aramex address lines | Breaking gap where required |
| Company | No canonical address-company snapshot; adapter substitutes contact name | Breaking semantic gap |
| Landmark | Snapshotted but not serialized | Compatibility gap where applicable |
| Origin address | Free-text carrier configuration, not admitted shipment geography | Blocking |

## Canonical Compatibility Matrix

| Boundary | Result |
|---|---|
| Canonical country | Verified for destination; exact catalog-release binding added |
| Canonical administrative area | Required published delivery levels are enforced |
| Canonical locality | Snapshot value now supplies Aramex city |
| Canonical district | Preserved in lineage and mapping resolution |
| Address admission | Shipment requires admitted canonical evidence, but existing snapshot completeness guards remain shallow |
| Order snapshot | Copied into shipment by `ShipmentService`; not re-resolved |
| Shipment snapshot | Destination is immutable; origin/pickup snapshot is absent |
| Carrier mapping resolution | Published, activated, active, reviewed, scoped, country-bound, catalog-release-bound |
| Provider mapping snapshot | Persisted only on Shipment and reused for recovery |

## Country Rules Matrix

| Rule | Enforcement |
|---|---|
| Unsupported country | Active country profile required; otherwise `UnsupportedCountry` |
| Required administrative/locality/district levels | Published profile level codes must exist in the selected active lineage |
| Required street | `RequiresStreetLine1` enforced |
| Postal required | `PostalCodeRequired` |
| Postal forbidden | `PostalCodeForbidden` |
| Postal format | Published profile regex enforced as `PostalCodeInvalid` |
| Provider-specific country rules in adapter | None added |
| Draft/unpublished country rules | Not consumed; profile must match mapping release catalog release |

## Carrier Mapping Compatibility

Resolution requires the exact provider, environment, tenant-provider-account,
tenant, operation, country, effective window, freshness window, active
publication, published mapping release, reviewed/approved active rows, and now
the exact shipment catalog release. Cross-country and ambiguous mappings fail
closed. Recovery uses the stored shipment mapping snapshot and does not switch
to a newer release.

Repository-proven Aramex coverage remains 0% per GEO-10. Structural
compatibility is verified; deployed mapping-data compatibility is not.

## Breaking Issues

1. Destination Aramex text previously came from legacy fields after mapping
   resolution. Closed.
2. Mapping release could target a catalog release different from the shipment
   snapshot. Closed.
3. Published country delivery/postal/street policy was not enforced during
   carrier resolution. Closed.
4. Administrative area/state was omitted from both registration builders.
   Closed for destination.
5. Origin/pickup geography has no admitted canonical shipment snapshot.
   Open and blocking.
6. Building and landmark snapshots are not converted into official Aramex
   address lines. Open.
7. Company is not represented by canonical shipment address evidence; the
   adapter substitutes the contact name. Open.
8. Postal code is validated but remains outside the immutable geographic
   snapshot property set. Open.
9. No published Aramex provider/mapping deployment evidence exists. Open and
   blocking.

## Runtime Hardening

Modified:

- `PointOfSale.Core/Interfaces/Geographic/GeographicContracts.cs`
- `PointOfSale.Infrastructure/Geographic/GeographicRepository.cs`
- `PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs`
- `PointOfSale.Tests/Architecture/CarrierGeographicResolutionIntegrationTests.cs`
- `PointOfSale.Tests/Shipping/ShipmentExternalRegistrationSafetyTests.cs`

No Aramex adapter, Checkout, frontend, database schema, migration, tracking,
label, cancellation, or authentication code was changed.

## Regression Tests

The GEO-9 regression freeze now also requires:

- mapping/catalog release equality;
- active supported country profile;
- required published delivery-level enforcement;
- postal required/forbidden/regex enforcement;
- street requirement enforcement;
- destination city/state/country sourcing from resolved shipment evidence;
- absence of the legacy destination-country fallback.

## Verification

| Command | Result |
|---|---|
| `dotnet build PointOfSale.Infrastructure/PointOfSale.Infrastructure.csproj --no-restore` | Passed; 0 errors, existing warnings |
| Focused Aramex/Shipping/Geographic compatibility slice | Passed: 34/34 |
| Relevant Geographic tests | Passed: 47/47 |
| `dotnet build PointOfSale.sln --no-restore` | Passed: 0 warnings, 0 errors |

No live Aramex call was made. No production database or published mapping
artifact was supplied.

## Risks

- Admitting destination geography cannot make a free-text pickup origin
  canonical.
- A profile regex can be operationally invalid even though runtime correctly
  enforces the published value; governance and data certification remain
  required.
- Existing mapping snapshots created before `CountryIso2` was included fail
  closed and require governed re-registration/recovery handling.
- GEO-10 zero-coverage blockers remain unchanged.
- Premises composition must be specified without exceeding Aramex line limits
  before building/landmark can safely be serialized.

## Final Recommendation

Do not activate Aramex shipment registration in production.

Proceed to a narrowly scoped **shipment origin/pickup geographic admission and
snapshot phase**, then publish and certify Aramex country/account mapping data.
That phase must add admitted origin evidence and complete premises/postal
snapshot compatibility without moving lookup or country logic into the
adapter. Rerun ARAMEX-3 and GEO-10 afterward.

## Explicit Success Answers

- Reviewed ONLY Geographic compatibility? **Yes.**
- Runtime files modified: **3**.
- Backend files modified: **3 runtime files plus 2 backend test files**.
- Frontend files modified: **None**.
- Canonical compatibility verified? **Destination path yes; end-to-end no because origin admission is absent**.
- Carrier Mapping compatibility verified? **Structurally yes; deployed coverage no (0% repository evidence)**.
- Country-rule compatibility verified? **Destination admission hardened and verified; origin remains unverified**.
- Provider IDs leaked outside Shipment? **NO**.
- Breaking issues found? **Yes — nine, four closed and five open**.
- Runtime hardening required? **Yes; safe destination/resolution hardening completed**.
- Production compatible? **No**.
- Recommend next phase: **Admitted shipment origin/pickup snapshot and premises/postal completion, followed by governed Aramex mapping rollout and recertification**.
