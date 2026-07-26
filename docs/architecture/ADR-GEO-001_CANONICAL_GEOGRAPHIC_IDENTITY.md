# ADR-GEO-001 — Canonical Geographic Identity

- Status: Proposed — architecture approval required
- Date: 2026-07-05
- Decision scope: platform Geographic Identity only
- Next phase: GEO-2 — Canonical Geographic SQL Model, only after approval
- Runtime impact in this phase: none

## Decision

Geographic Identity shall become a first-class, provider-independent platform
domain backed by PostgreSQL. It shall own the canonical place hierarchy,
localized names and aliases, hierarchy validation, structured address binding,
catalog versions, and provider geographic mappings.

Checkout and every other channel shall submit canonical place identities plus
human address details. Orders, shipments, returns, warehouses, stores, and
other business aggregates shall retain immutable geographic snapshots for
historical use. Carrier adapters shall receive provider identifiers resolved
before adapter invocation by a SQL-backed Carrier Geographic Mapping Service.
Adapters, Checkout, browser state, MongoDB, Redis, carrier metadata, and
configuration dictionaries shall never own or infer mappings.

This ADR designs that target. It does not authorize entities, migrations,
services, APIs, DTOs, controller changes, adapter changes, or frontend changes.

## Why this is a platform domain

Geography is shared reference identity, not a Shipping sub-feature. Shipping is
the first consumer that proves stable identity is mandatory, but the same place
hierarchy is required by Checkout, Orders, CommerceHub, split orders, Returns,
Warehouses, Taxes, Analytics, Search, Storefront, POS, Affiliate storefronts,
store/branch administration, and future countries and providers.

Shipping owns the shipment's admitted destination snapshot. It does not own the
meaning of Egypt, Cairo Governorate, Cairo, Nasr City, or a provider's code for
those places. Those meanings must be reusable across domains and channels.

## Frozen evidence and constraints

This decision preserves PCC-0, PCC-1, BOSTA-1, BOSTA-2, BOSTA-2.5, SF-SHP2,
SF-SHP3, SF-SHP4, and SF-SHP5.

- `AddressDto` currently captures street, optional second line, city, optional
  state, optional ZIP, and ISO country text.
- Storefront additionally captures a free-text `area`, but final checkout
  `AddressDto` and `Order` have no district identity; the value is lost.
- `CheckoutSession` persists city, area, address, and country as recovery
  candidates only.
- `CheckoutService` flattens street lines and writes string fields to `Order`.
- `ShipmentService` and `CarrierShipmentService` copy those strings to
  `Shipment`; shipment coordinates are nullable and are not populated by this
  pipeline.
- Both carrier request builders leave `RecipientCityId`,
  `RecipientDistrictId`, `RecipientDistrictName`, and `RecipientZoneId`
  unpopulated.
- Bosta correctly fails closed unless it receives `districtId`, or `cityId`
  plus `districtName`; optional `zoneId` is provider evidence.
- Aramex, Mylerz, and DHL currently serialize free-text address shapes, but
  that is not proof of canonical identity or future compatibility.
- `Customer`, `Supplier`, and `Warehouse` each have only one free-text address.
- `CarrierConfiguration` has free-text pickup address, city, and country.
- `CommerceHubMarketplaceOrder` and its Mongo projection have address line,
  city, and optional area, but no country, postal code, or stable place keys.
- SQL `Order` has independent shipping and billing text snapshots.
- Mongo order and abandoned-checkout documents contain derivative free-text
  copies. No Redis geographic authority or mapping model was found.
- Existing shipping certifications require backend SQL authority, current
  tenant/order/shipping-method admission, replay safety, and fail-closed
  provider execution. Geographic resolution must preserve those rules.

## Current end-to-end trace

```text
Customer input
  free-text city / area / street / country
        |
        v
Checkout and CheckoutSession
  candidates; no stable place identity or hierarchy validation
        |
        v
Order
  street flattened; area lost; city/state/postal/country copied as strings
        |
        v
Shipment
  strings copied; coordinates normally null
        |
        v
CarrierShipmentService
  provider-neutral location-id fields left null
        |
        v
Adapter
  Bosta fails closed; other providers transmit text
        |
        v
Tracking / Returns / Analytics
  use shipment/order text or derivative projections; identity cannot be
  reliably joined, localized, remapped, or recovered
```

Identity is currently never created. Text is normalized lightly, copied,
flattened, or lost. Consequently later tracking, return, tax, and analytics
flows cannot recover the intended place without guessing.

## Ubiquitous language

- **Place**: a stable node in the provider-independent geographic hierarchy.
- **Place type**: country, first-level subdivision, city, district, or zone.
- **Canonical identity**: the immutable internal `PlaceId`, never a name.
- **Address**: structured delivery/contact detail bound to canonical places.
- **Address binding**: validated association between address details and one
  coherent place lineage.
- **Snapshot**: immutable historical copy of admitted place IDs, display names,
  codes, catalog version, and address details on a business aggregate.
- **Provider mapping**: versioned association from a canonical place to a
  provider's place identifier/type, optionally scoped by environment/account.
- **Alias**: normalized searchable name; never identity.
- **Catalog release**: immutable published version of canonical catalog data.
- **Mapping release**: immutable published set of mappings usable at runtime.

Use `AdministrativeArea` in the model rather than `State` or `Governorate`.
Localization may display “Governorate”, “State”, “Province”, or the appropriate
country term. Do not encode Egypt's terminology into the global type system.

## Canonical model

### GeographicPlace

One adjacency-list aggregate root represents all hierarchy nodes:

| Attribute | Purpose |
|---|---|
| `Id` | immutable UUID identity |
| `Type` | `Country`, `AdministrativeArea`, `City`, `District`, `Zone` |
| `ParentId` | immediate canonical parent; null only for country |
| `CountryId` | denormalized lineage key for validation and indexed reads |
| `CanonicalCode` | internal stable code; ISO code where applicable |
| `ExternalStandard` / `ExternalStandardCode` | optional ISO 3166, postal, or other recognized standard |
| `CentroidLatitude` / `CentroidLongitude` | optional catalog reference point, not an address coordinate |
| `TimeZoneId` | optional IANA time zone |
| `Status` | draft, active, deprecated, merged, retired |
| `SuccessorPlaceId` | explicit replacement for merged/deprecated nodes |
| `ValidFromUtc` / `ValidToUtc` | temporal catalog validity |
| `CatalogVersion` | published release that introduced the row version |
| audit/provenance | source, reviewed by/at, created/updated timestamps |

The parent chain is canonical. A district cannot merely carry an unrelated city
field. Valid type transitions are country → administrative area → city →
district → zone, while country profiles may explicitly skip unsupported levels.
Zones are canonical only when they have business meaning across providers or
platform capabilities. A provider-only service zone belongs in mapping data,
not the canonical hierarchy.

### GeographicPlaceName

Names are separate, versioned children:

| Attribute | Purpose |
|---|---|
| `PlaceId` | owner |
| `LanguageTag` | BCP 47 tag such as `ar-EG`, `en`, `fr` |
| `ScriptCode` | optional ISO 15924 script |
| `Name` | display value |
| `NormalizedName` | Unicode-normalized search key |
| `NameKind` | official, short, historic, transliteration |
| `IsPreferred` | one preferred name per place/language |

### GeographicPlaceAlias

Aliases support search, legacy matching, spelling variants, and transliteration.
Aliases produce candidate sets only. An ambiguous alias must never auto-resolve
an address or provider mapping.

### Address

An address is not a child place. It is structured detail bound to a validated
lineage:

- `CountryId` — required.
- `AdministrativeAreaId` — required when the country profile requires it.
- `CityId` — required for shippable and operational addresses.
- `DistrictId` — required when country/carrier eligibility requires it.
- `ZoneId` — optional unless a platform policy requires it.
- `PostalCode` — country-profile validated; optional where postal systems do
  not apply.
- `StreetLine1` — required for physical delivery.
- `StreetLine2` — optional.
- `Building`, `Floor`, `Apartment`, `Landmark`, `DeliveryInstructions` —
  optional independent text fields.
- `Latitude`, `Longitude` — optional coordinate pair.
- `CoordinateSource`, `CoordinateAccuracyMeters`, `GeocodedAtUtc` — required
  provenance when coordinates exist.
- `RawInput` — optional protected evidence for support/migration, not identity.
- `CatalogVersion`, `ValidationStatus`, `ValidatedAtUtc` — admission evidence.

Country, administrative area, city, district, and zone names are not writable
address fields. They are resolved from the selected IDs. Address normalization
normalizes spacing, Unicode, country/postal formats, and structured lines; it
must not silently change selected place identity.

### Address ownership and reuse

The Geographic domain owns validation rules and canonical bindings, not every
business address lifecycle.

- A reusable `PartyAddress`/address-book record may be owned by Customer,
  Vendor, Store, Branch, Warehouse, or Supplier and reference an admitted
  address value.
- Checkout address remains a candidate until admitted.
- Orders, shipments, returns, payments/tax evidence, and split orders store
  immutable snapshots, not live references whose later edits could rewrite
  history.
- A saved address edit creates a new version. It never mutates prior order or
  shipment snapshots.

## Recommended aggregate boundaries

### Geographic Catalog aggregate

Root: `GeographicPlace`.

Owns parent membership, lifecycle, names, aliases, country profile, and catalog
publication. Changes are platform-admin operations. Tenant users cannot create
canonical places.

### Address Binding value/aggregate

Root: the owning party/address-book record where durable, or an admitted value
inside Checkout where transient.

Owns address detail, selected place lineage, coordinate provenance, validation
status, and version. It cannot alter the catalog.

### Provider Geographic Mapping aggregate

Root: `ProviderGeographicMappingRelease`; children are mapping entries.

Owns provider code, provider place type/id/code/name, canonical place binding,
provider parent binding, environment/account scope, status, source, effective
period, validation, and published version. It cannot alter the canonical
catalog.

### Business snapshots

Order, Shipment, Return, CommerceHub suborder, tax, and payment aggregates own
their immutable snapshots after admission. They consume Geographic Identity;
they do not become catalog or mapping authority.

## Recommended SQL model for GEO-2

### Catalog tables

1. `GeographicPlaces`
2. `GeographicPlaceNames`
3. `GeographicPlaceAliases`
4. `GeographicCountryProfiles`
5. `GeographicCatalogReleases`
6. `GeographicCatalogChangeLedger`

### Address tables

7. `PartyAddresses`
8. `PartyAddressVersions`

Use an explicit owner type only if the platform's polymorphic ownership policy
permits it; otherwise use join tables such as `CustomerAddresses`,
`WarehouseAddresses`, `StoreAddresses`, and `BranchAddresses` referencing a
shared immutable address version. Do not create unenforced polymorphic UUID
foreign keys.

### Provider mapping tables

9. `CarrierGeographicMappingReleases`
10. `CarrierGeographicMappings`
11. `CarrierGeographicSyncRuns`
12. `CarrierGeographicSyncCandidates`
13. `CarrierGeographicMappingChangeLedger`

### Snapshot columns or owned tables

Add an immutable `GeographicAddressSnapshot` shape to:

- Orders, independently for shipping and billing.
- Shipments, independently for origin, destination, and return destination.
- CommerceHub marketplace orders/suborders.
- Returns/return shipments.
- Warehouses, stores, branches, vendor fulfillment locations, and carrier
  pickup origins where historical execution requires a snapshot.

The snapshot includes all selected internal IDs, resolved display names/codes,
structured address details, coordinates/provenance, and catalog version.
Shipment registration additionally snapshots mapping release ID and each
provider place identifier actually used.

### Keys, constraints, and indexes

- Primary keys: UUIDs generated by backend authority.
- `GeographicPlaces`: unique active `(ParentId, Type, CanonicalCode)` and unique
  country ISO code where standard is ISO 3166-1.
- Check constraints for allowed parent/type relationships, coordinate ranges,
  validity windows, and successor not equal to self.
- Deferred cycle prevention or hierarchy closure validation at publication.
- `GeographicPlaceNames`: unique preferred `(PlaceId, LanguageTag)`; indexed
  normalized name with parent/country filters.
- `GeographicPlaceAliases`: indexed `(CountryId, LanguageTag,
  NormalizedAlias)`; never globally unique.
- Provider mapping: unique published provider identity over `(ProviderCode,
  Environment, AccountScope, ProviderPlaceType, ProviderPlaceId,
  MappingReleaseId)` and unique canonical mapping where provider cardinality is
  declared one-to-one. Many-to-one/one-to-many mappings require an explicit
  mapping-group policy, never duplicate rows by accident.
- Provider parent mapping must belong to the same provider/scope/release.
- Address lineage foreign keys must all reference active or historically valid
  catalog rows and form one ancestor chain at admission.
- GIST/geospatial indexes are optional in GEO-2; add only for an approved
  proximity query. Coordinates are not required to establish identity.
- Tenant indexes belong on tenant-owned address books and snapshots. The global
  canonical catalog is platform-scoped; account-specific mapping overrides are
  explicitly tenant/account scoped.

Do not use JSON as primary catalog, address, or mapping authority. JSON may
retain provider payload evidence in sync staging or immutable audit records.

## Recommended services

### GeographicCatalogQueryService

Provides versioned, localized, parent-scoped reads and search. Country,
administrative-area, city, district, and zone results carry stable IDs,
localized names, type, parent ID, status, and catalog version.

### GeographicHierarchyPolicy

Validates type transitions, ancestry, country-specific required levels,
retirement/successor rules, and selected lineage consistency.

### GeographicAddressAdmissionService

Accepts place IDs and address details; normalizes text; validates hierarchy,
country profile, postal format, coordinate pair/provenance, required fields,
and current catalog version; emits an admitted address value or structured
failure. It never guesses from names.

### GeographicSearchService

Searches preferred names and aliases within an explicit country/parent scope.
It may rank candidates but may not convert ambiguity into identity.

### GeographicSnapshotFactory

Creates immutable, localized-but-self-describing snapshots from admitted
identity. Historical snapshots remain renderable if a catalog place is later
renamed, merged, or retired.

### CarrierGeographicMappingService

Input:

- provider code and operation;
- environment and provider account scope;
- canonical lineage IDs;
- mapping freshness policy;
- optional required provider place types.

Output:

- one immutable mapping result containing provider identifiers/names/types,
  mapping release, source, verification, and effective timestamps; or
- a fail-closed reason: missing, ambiguous, inactive, stale, invalid hierarchy,
  wrong environment/account, or unpublished.

It resolves from published SQL state and may use a derivative cache. It runs in
`CarrierShipmentService` before adapter invocation. The adapter only serializes
the resolved result.

### GeographicCatalogAdministrationService

Stages imports and reviewed edits, validates graph integrity and localization,
publishes immutable catalog releases, manages deprecation/merge successors,
and emits change events.

### CarrierGeographicSynchronizationService

Imports provider catalogs when official provider endpoints/files exist,
preserves raw evidence, computes diffs, proposes candidates, validates provider
parent graphs, and publishes only reviewed mapping releases. Mylerz remains
manual/vendor-document sourced until authoritative technical material exists.

## Provider mapping architecture

```text
Canonical address/place IDs
        |
        v
CarrierShipmentService
        |
        v
CarrierGeographicMappingService ----> published SQL mapping release
        |
        +--> Bosta city/district/zone IDs
        +--> DHL locality/region codes when required
        +--> Aramex location/region/zone codes when required
        +--> Mylerz city/area codes when authoritative contracts require them
        |
        v
Resolved mapping snapshot
        |
        v
Adapter wire serialization only
```

Mappings are keyed by stable canonical `PlaceId`, never localized name. A
provider mapping can target a different granularity, but that mismatch must be
explicit. For example, multiple canonical districts may map to one carrier
zone through separate reviewed entries or a mapping group. No adapter switch
statement, hardcoded dictionary, fuzzy name match, or coordinate guess is
permitted.

“Configuration-only provider” means geographic onboarding and mapping changes
can be performed through governed catalog/mapping data once an adapter already
supports that provider's wire contract. It does not mean a fundamentally new
API contract can be integrated without adapter code.

## Checkout evolution

Checkout becomes progressively hierarchical and provider-independent.

| Field | Target control | Required | Reason |
|---|---|---:|---|
| Country | searchable select | yes | stable country identity and country profile |
| State/Governorate | searchable dependent select | country policy | first-level hierarchy; localized label |
| City | searchable dependent select | yes | stable fulfillment identity |
| Area/District | searchable dependent select | country/carrier eligibility policy | needed by Bosta and precise delivery |
| Zone | hidden unless platform-relevant; dependent select when exposed | usually no | avoid exposing provider-only zones |
| Postal code | formatted text with country validation | country policy | postal codes are user-entered address data |
| Street line 1 | free text | yes | premises detail is not catalog identity |
| Street line 2 | free text | no | additional delivery detail |
| Building | free text | no, country policy may require | structured local delivery detail |
| Floor | free text | no | structured local delivery detail |
| Apartment | free text | no | structured local delivery detail |
| Landmark | free text | no | useful where formal street addressing is weak |
| Delivery notes | free text | no | instructions, never identity |
| Coordinates | map/geolocation assist with explicit consent | no | precision aid with provenance, never identity |

The current editable free-text `City`, `State`, `Area`, and `Country` disappear
as authoritative submission fields. Their display labels remain derived from
selected canonical IDs. Street, building, floor, apartment, landmark, postal
code, and instructions remain text because cataloging individual premises
would be false precision.

The hierarchy is progressive:

1. Select/search country.
2. Select/search an allowed child administrative area when applicable.
3. Select/search city within current ancestry.
4. Select/search district within the city when applicable.
5. Show zone only when it is a canonical platform concept.

Changing an ancestor clears descendants. Saved or recovered addresses are
revalidated against the current catalog and visibly require review if their
place was retired, merged, or version-invalid. Offline clients may search a
signed/versioned local catalog snapshot and submit IDs, but backend admission
against SQL remains decisive.

Checkout must not ask which carrier will be used and must never show “Bosta
City”, “DHL Region”, “Aramex Zone”, or “Mylerz Area”.

## Admin UX

Provide separate workspaces:

1. **Geographic Catalog** — hierarchy tree, multilingual names, aliases,
   status, source, catalog release, merge/deprecate workflow, and integrity
   validation.
2. **Carrier Mappings** — provider/environment/account filters, canonical and
   provider trees side by side, unmapped/ambiguous/stale queues, effective
   dates, source evidence, review/approve/publish workflow, and coverage
   metrics by required level.
3. **Synchronization** — last successful fetch, source version/hash, staged
   diff, additions/renames/moves/removals, validation failures, publish/rollback
   controls, and provider endpoint health.
4. **Operational Locations** — warehouse, store, branch, vendor fulfillment,
   return, and carrier pickup addresses using the same canonical picker.
5. **Migration Review** — unresolved legacy text grouped by normalized lineage,
   candidate matches with confidence as review aids, bulk approval only for
   identical reviewed evidence, and explicit “manual-only/unresolved” status.

Provider mappings require four-eyes approval for production publication.
Published releases are immutable; corrections create a new release. Admin
search and suggestions remain candidates until an authorized reviewer commits
them.

## Synchronization and offline model

### Canonical catalog

- Seed from authoritative public standards and country-specific official data.
- Import to staging; never overwrite published rows in place.
- Normalize Unicode and searchable aliases while preserving official spelling.
- Validate uniqueness, cycles, parent/type transitions, localized preferred
  names, successor links, and referential integrity.
- Publish an immutable catalog release and change event.

### Provider catalogs and mappings

- Fetch provider data with conditional requests/version/hash where available.
- Store the raw response as bounded audit evidence in the sync run.
- Stage provider nodes and diff against the last published release.
- Never auto-publish name-based matches.
- Auto-carry an existing mapping only when provider stable ID, scope, parent,
  and semantics are unchanged.
- Require review for additions, removals, reparenting, split/merge, ID changes,
  and ambiguous names.
- Keep the previous release usable until the new release is published.
- Rollback selects a prior immutable release; it does not edit history.

### Cache

- SQL published releases are authority.
- Redis may cache catalog children/search results and mapping results keyed by
  catalog/mapping release, locale, provider, environment, account scope, and
  canonical place ID.
- Publication changes the release key and emits invalidation. TTL is a safety
  bound, not freshness authority.
- Cache miss or Redis outage falls back to SQL.
- Mongo/Elasticsearch may project localized place labels and IDs for analytics
  and search, but cannot resolve or validate identity.

### Offline mode

- Distribute a signed catalog package with release ID, country scope, localized
  names, aliases, hierarchy, checksum, issued/expiry timestamps.
- The client records selected IDs and package release, not provider IDs.
- Backend validates submitted IDs and ancestry against SQL on reconnection.
- Carrier mapping resolution uses the last approved published SQL release and
  therefore does not require a live provider lookup.
- If policy marks the mapping release too stale, shipment registration pauses
  before the external call; it does not guess.

## Cross-domain impact

| Domain | Required architectural relationship |
|---|---|
| Shipping | owns origin/destination/return snapshots and consumes mapping results before adapters |
| CommerceHub | same canonical checkout picker and snapshots per marketplace order/suborder/store |
| Affiliate | storefront channel uses the same Checkout identity; affiliate attribution does not own geography |
| Orders | immutable shipping/billing geographic snapshots; no live address mutation |
| Warehouse | structured operational/dispatch/return address bound to canonical hierarchy |
| Returns | return-origin and destination snapshots; return carrier mapping resolved independently |
| Taxes | consumes admitted destination/place IDs and effective tax jurisdiction rules; never infers from labels |
| Analytics | dimensions keyed by canonical IDs/catalog version; localized labels derivative |
| Search | indexes canonical IDs and localized labels; does not become catalog authority |
| Storefront | hierarchical candidate capture, address book, recovery revalidation |
| POS | customer/supplier/store/branch/warehouse address capture uses the same picker; offline package supported |
| Admin | governed catalog, mapping, sync, coverage, and migration workspaces |
| Payments | billing snapshot consumes admitted identity; payment provider address mapping remains separate integration evidence |
| Carrier tracking | retains shipment snapshot and provider mapping release used at creation; status events cannot rewrite geography |

Split orders copy the admitted customer destination to each suborder while each
shipment resolves mappings for its chosen carrier and origin. A carrier change
therefore creates a new mapping snapshot without changing the order's canonical
destination.

## Migration strategy

### Phase 0 — approve architecture

Approve this ADR. No implementation begins before approval.

### GEO-2 — canonical SQL model

Design exact DDL, EF boundaries, constraints, indexes, tenancy, release
semantics, and snapshot storage. This remains a design gate unless separately
authorized for implementation.

### Phase 1 — catalog and mapping foundation

Create global country/place catalog, localization, release workflow, provider
mapping tables, admin staging, and read-only services. Seed Egypt first while
ensuring the model is country-neutral.

### Phase 2 — legacy discovery and assisted backfill

- Inventory distinct normalized country/state/city/area/postal combinations
  from orders, shipments, checkout sessions, CommerceHub, warehouses, carrier
  pickups, stores, branches, customers, and suppliers.
- Preserve raw values and counts.
- Generate candidate matches only.
- Auto-bind only exact, unique, hierarchy-valid matches approved by migration
  policy; route all ambiguity to review.
- Mark unresolved rows `LegacyUnresolved`; never fabricate IDs.

### Phase 3 — dual read and dual snapshot

New writes require canonical identity for enabled countries/channels and also
retain legacy display columns for compatibility. Reads prefer new snapshots
when present and fall back to legacy text. Existing historical rows remain
valid and are not rewritten merely because a place is renamed.

### Phase 4 — channel rollout

Roll out Admin operational locations, Storefront Checkout, CommerceHub,
customer address books, POS, and returns behind country/tenant capability
gates. Revalidate recovered sessions and saved addresses.

### Phase 5 — carrier mapping activation

Publish reviewed Bosta mappings, verify required coverage, resolve mapping
before carrier calls, and rerun BOSTA-2. Activate other provider mapping
requirements independently. Preserve Bosta fail-closed behavior throughout.

### Phase 6 — legacy retirement

Stop accepting authoritative free-text place names after adoption thresholds,
operational coverage, reconciliation, rollback rehearsal, and compatibility
telemetry pass. Keep immutable legacy text for historical presentation and
forensics according to retention policy.

## Backward compatibility

- Existing orders/shipments remain readable and executable only under explicit
  legacy/manual policies; they do not acquire invented IDs.
- API versions during transition may accept the legacy address shape only for
  tenants/countries still in compatibility mode.
- Responses continue to include human display fields, derived from snapshots,
  while new versions add canonical IDs and catalog version.
- Legacy `area`, `state`, and city strings can be retained as raw migration
  evidence but cannot satisfy a provider-ID requirement.
- Manual fulfillment may accept unresolved legacy snapshots when no external
  carrier/tax rule requires canonical identity.
- External carrier creation requiring mappings fails closed before network I/O.
- Mapping release rollback does not rewrite already registered shipments;
  those retain the exact provider IDs/release used.

## Failure policy

Fail before checkout admission or carrier network I/O on:

- missing required identity level;
- selected nodes outside one hierarchy;
- inactive/retired place without an accepted successor flow;
- unknown or unsupported catalog release;
- ambiguous alias/name candidate;
- missing, ambiguous, stale, unpublished, wrong-environment, or wrong-account
  provider mapping;
- provider parent mapping inconsistent with the canonical/provider hierarchy;
- invalid country postal format where required;
- incomplete or out-of-range coordinate pair.

Do not repair failures from free text, coordinates, cache, provider response,
`CarrierMetadata`, Mongo, Elasticsearch, or analytics.

## Security, privacy, and tenancy

- The base catalog is platform-global and read-only to tenants.
- Tenant/account mapping overrides are isolated and can only narrow/override
  within explicit provider account scope.
- Full addresses and precise coordinates are personal data. Minimize exposure,
  encrypt/protect according to the platform data policy, redact logs/webhook
  evidence, and apply purpose-based authorization and retention.
- Public search returns place catalog data, never party addresses.
- Sync credentials remain in carrier credential authority; sync evidence must
  redact secrets and personal data.
- Every catalog/mapping publication and manual override is durably audited.

## Observability and acceptance criteria

Measure:

- catalog coverage by country/type/language;
- address admission failures by non-PII reason;
- legacy unresolved counts by source/channel;
- provider mapping coverage by provider/environment/account/required level;
- stale/missing/ambiguous mapping failures;
- sync age, source hash/version, diff size, validation errors, and publish age;
- cache hit/fallback without treating cache as authority;
- shipment registrations blocked before provider calls;
- snapshot/mapping release distribution for forensics.

Before carrier activation, prove hierarchy validation, localization fallback,
ambiguous search rejection, release immutability, offline SQL resolution,
cache-loss fallback, provider/account/environment isolation, sync replay
idempotency, mapping rollback, historical snapshot stability, and
Storefront/CommerceHub parity.

## Risks

| Risk | Mitigation |
|---|---|
| Wrong legacy auto-match | exact unique hierarchy policy plus human review; unresolved is valid |
| Global catalog scope leaks tenant overrides | separate platform catalog from scoped mapping overrides |
| Over-modeling every locality | fixed canonical types plus country profiles; provider-only nodes remain mappings |
| Catalog rename rewrites history | immutable business snapshots |
| Provider drift | staged sync, immutable releases, freshness policy, coverage telemetry |
| Checkout friction | searchable progressive controls, saved addresses, locale-aware aliases |
| Large catalog/search cost | parent-scoped indexes, release-keyed cache, optional dedicated search projection |
| Coordinates mistaken for identity | provenance and explicit rule that coordinates are supplemental |
| Mapping cardinality mismatch | explicit mapping groups/policies and reviewed hierarchy |
| Sensitive address leakage | purpose authorization, redaction, retention, no public address search |
| Premature “configuration-only” claim | configuration-only applies after wire adapter compatibility exists |

## Alternatives considered

### Keep free text and special-case Bosta

Rejected. It cannot validate hierarchy, support reliable analytics/tax/returns,
or generalize to future provider IDs. It would make Checkout or the adapter
guess identity.

### Put provider IDs in Checkout

Rejected. It couples the customer to a carrier that can change after checkout,
pollutes address books, and violates provider independence.

### Let each adapter own dictionaries

Rejected. Mappings would be duplicated, unversioned, hard to synchronize,
impossible to govern centrally, and unavailable for preflight eligibility.

### Store mappings in `CarrierConfiguration` JSON or `CarrierMetadata`

Rejected. Those are tenant configuration/opaque shipment evidence boundaries,
not normalized reference authority; they cannot enforce hierarchy,
uniqueness, release, or cross-provider governance.

### Use coordinates as the canonical key

Rejected. Coordinates may be missing or imprecise, describe a point rather
than administrative identity, and do not supply provider identifiers.

### Use provider catalog as the canonical catalog

Rejected. It gives a carrier authority over Checkout, other providers, taxes,
analytics, and countries it does not model.

### Live provider lookup during checkout or shipment creation

Rejected as authority. It prevents offline operation, introduces availability
and drift into checkout, and still cannot provide cross-provider identity.
Provider lookups belong to governed synchronization.

### Separate tables per hierarchy level

Not recommended. They offer stricter static FKs but make countries with missing
or additional administrative levels awkward, multiply services and migrations,
and complicate future place types. One typed adjacency model with publication
validation and country profiles is more extensible.

## Explicit answers

### Should Geographic Identity become a platform domain?

**Yes.** Evidence spans Checkout, Orders, Shipment, Warehouses, carrier pickup,
CommerceHub, Returns, Taxes, Analytics, Search, Storefront, POS, and Admin.
Shipping exposed the defect, but no single consumer can own shared place truth.

### Should Checkout become hierarchical?

**Yes.** Current free-text city/area/state values cannot prove membership and
`Area` is lost before `Order`. Progressive country → administrative area →
city → district selection preserves stable identity while street/premises
details remain free text.

### Should Shipping Providers consume only mappings?

**Yes, for provider geographic identity.** `CarrierShipmentService` must resolve
canonical snapshot IDs through the mapping service before adapter invocation.
Adapters receive a resolved mapping and serialize it; they never infer or own
it. Providers still consume ordinary address text required by their wire
contracts.

### Should geographic mappings be SQL-backed?

**Yes.** Required uniqueness, hierarchy, tenancy/account scope, effective
dates, review, immutable releases, offline operation, audit, and fail-closed
resolution require durable relational authority. Redis and Mongo remain
derivative.

### Should every provider become configuration-only?

**No, not absolutely.** Geographic catalog/mapping onboarding should be
configuration-and-data driven after the provider adapter supports the wire
contract. A new authentication scheme, endpoint, request/response contract, or
operation still requires certified adapter work. Calling all future providers
configuration-only would erase a real integration boundary.

### Can Bosta, DHL, Aramex, and Mylerz reuse the same canonical model?

**Yes.** All can consume the same canonical country/administrative-area/city/
district/zone identity and structured address snapshot. Each provider gets its
own versioned mapping projection and required-level policy. Bosta currently
proves the need for city/district IDs; DHL, Aramex, and Mylerz may continue to
use text for current contracts while retaining the same canonical source and
gaining mappings when their certified contracts require them.

## Final recommendation

Approve Geographic Identity as a platform domain with:

1. one global typed place hierarchy and localized names/aliases;
2. structured addresses bound to canonical IDs;
3. immutable business snapshots;
4. SQL-backed, release-based carrier geographic mappings;
5. provider-independent hierarchical Checkout;
6. governed catalog/provider synchronization with offline SQL resolution;
7. fail-closed handling of missing, ambiguous, stale, or invalid identity.

If this ADR is approved, proceed to **GEO-2 — Canonical Geographic SQL Model**.
GEO-2 should specify exact tables, constraints, indexes, tenancy, release
semantics, and migration DDL design. Runtime implementation remains a later,
separately approved phase.
