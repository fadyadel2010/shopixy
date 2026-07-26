# GEO-4 - Geographic Runtime Authority Certification

Status: Runtime authority certification freeze pending approval  
Date: 2026-07-05  
Governing architecture: `ADR-GEO-001_CANONICAL_GEOGRAPHIC_IDENTITY.md`, `GEO-1.5_GEOGRAPHIC_DOMAIN_BOUNDARIES.md`, `GEO-2_CANONICAL_GEOGRAPHIC_SQL_MODEL.md`, and `GEO-3_GEOGRAPHIC_CATALOG_GOVERNANCE_AUTHORITY.md`  
Runtime impact: none  
Backend impact: none  
Frontend impact: none  
Checkout impact: none  
Shipping runtime impact: none  
Carrier adapter impact: none  
EF impact: none  
Migration impact: none

## Executive Summary

Geographic runtime authority is certified as a published-SQL authority model.
Runtime consumers may read only active published catalog releases and active
published carrier geographic mapping releases. Drafts, candidates, raw sync
evidence, validation output, unpublished overrides, rejected reviews, and
browser/offline state never participate in runtime resolution.

Runtime Geographic authority is owned by the Geographic platform domain:

- Platform Geographic Catalog owns canonical place identity, hierarchy,
  country profiles, localization, aliases, releases, publication state,
  rollback activation, audit, synchronization admission, and read authority.
- Provider Geographic Mapping owns carrier/provider/environment/account scoped
  provider identifiers through immutable published mapping releases.
- Address Admission owns runtime validation of selected canonical lineage and
  address detail against the published catalog.
- Business domains own their address-bearing lifecycles and immutable admitted
  snapshots only.

Checkout, Shipment, Carrier Adapters, Search, Mongo, Redis, browser state,
offline packages, autocomplete indexes, and provider APIs are not Geographic
Authority. They may submit candidates, read published evidence, cache or
project it, present it, or snapshot admitted results according to their domain
boundary. They cannot create, infer, repair, publish, recover, or override
canonical place identity or provider mapping authority.

They are not Geographic Authority.
Carrier adapters never infer Geographic Authority.

No business runtime implementation, EF entity, migration, API, controller,
Checkout UI, carrier adapter, or projection implementation is authorized by
GEO-4. No runtime hardening was required because this phase found no current
runtime implementation path for Geographic authority; the capability remains a
future architecture and implementation gate.

## Runtime Authority Matrix

| Runtime | Identity owner | Authority and validation owner | Persistence owner | Publication owner | Recovery and audit owner | Projection, cache, sync owner | Runtime reads | Runtime writes |
|---|---|---|---|---|---|---|---|---|
| Platform Geographic Catalog | Geographic Catalog | Geographic hierarchy policy, country profile policy, catalog release validation | PostgreSQL catalog release tables | Platform Geographic Publication Operations | Published catalog SQL plus `GeographicAudit` | Projections and caches derive from published catalog; sync is candidate evidence only | Published catalog release only | Platform Admin governed drafts and publication only |
| Carrier Geographic Mapping | Provider Geographic Mapping | Mapping validation for provider, environment, account, hierarchy, cardinality, freshness, and scope | PostgreSQL mapping release tables | Platform Geographic Publication Operations | Published mapping SQL plus `GeographicAudit` | Provider sync creates candidates only; caches derive from release keys | Published mapping release only | Platform Admin governed mapping drafts and publication only |
| Country Profiles | Geographic Catalog | Country profile publication validation | PostgreSQL country profile release data | Catalog release publication | Published catalog SQL and audit | Localized/profile projections are derivative | Published profile in active catalog release | Platform Admin catalog publication only |
| Localization | Geographic Catalog | Preferred-name and language coverage validation | PostgreSQL place names tied to releases | Catalog release publication | Published catalog SQL and audit | Search/autocomplete labels derive from published names | Published names and fallback policy | Platform Admin catalog publication only |
| Aliases | Geographic Catalog | Alias ambiguity and target validation | PostgreSQL aliases tied to releases | Catalog release publication | Published catalog SQL and audit | Search/autocomplete consume aliases as candidates only | Candidate search reads from published aliases | Platform Admin catalog publication only |
| Catalog Releases | Geographic Catalog | Publication validation, approval separation, checksum, lock, hierarchy integrity | PostgreSQL immutable catalog releases and publications | Platform Geographic Publication Operations | Rollback activates prior published SQL release; audit is append-only | Release-keyed projections and cache invalidation derive from publication | Active published release pointer only | Publication operators through governed workflow |
| Mapping Releases | Provider Geographic Mapping | Mapping publication validation, provider graph, scope, freshness, checksum, lock | PostgreSQL immutable mapping releases and publications | Platform Geographic Publication Operations | Rollback activates prior published SQL mapping release; audit is append-only | Release-keyed mapping caches derive from publication | Active published mapping release pointer only | Publication operators through governed workflow |
| Publication | Geographic Catalog and Provider Geographic Mapping | Four-eyes approval, validation snapshot, checksum, publication lock | PostgreSQL publication records and current pointers | Platform Geographic Publication Operations | Audit records every activation, supersede, rollback, archive | Publishes invalidation events; projections rebuild from SQL | Reads approved immutable release | Writes only publication records and active release pointers |
| Rollback | Published SQL release authority | Target release compatibility validation | PostgreSQL publication history and active pointer | Platform Geographic Publication Operations | Recovery re-enters prior immutable SQL release, never cache | Emits invalidation; caches rebuild under older release key | Prior published release only | Writes rollback publication event and active pointer |
| Search Projection | Geographic Catalog | Search admission validates published release, locale, scope, checksum | Derivative search index only | Derived from catalog publication | Rebuild from published SQL catalog | Elasticsearch/search index/autocomplete are derivative only | Published IDs, names, aliases, release evidence | Projection writer only from published SQL |
| Provider Resolution | Provider Geographic Mapping | CarrierGeographicMappingService validates published mapping release and canonical lineage | Mapping result snapshot on shipment registration | Mapping release publication | Recovery re-enters published mapping SQL | Redis mapping cache is release-keyed derivative | Published mapping release only | No runtime mapping mutation; shipment snapshots resolved result |
| Checkout Geographic Resolution | Geographic Address Admission | Address Admission validates selected IDs, hierarchy, country profile, catalog release | Checkout owns candidates; Order owns admitted snapshot | Catalog release publication only | Recovery revalidates candidates through published SQL | Browser/offline/autocomplete packages are candidates only | Published catalog/profile reads | Candidate writes only; no Geographic authority writes |
| Shipment Geographic Resolution | Shipment domain snapshots admitted geography; Geographic owns identity and mapping | Address Admission and Provider Mapping validate before carrier call | Shipment owns immutable origin/destination/return/mapping snapshots | Catalog and mapping releases | Recovery reloads order/shipment snapshot and re-enters published mapping SQL | Carrier adapter/cache/provider payload are derivative evidence | Published catalog/mapping and immutable shipment snapshot | Shipment writes immutable snapshot only after admission |
| Warehouse Geographic Resolution | Warehouse domain owns address lifecycle; Geographic owns admission | Address Admission validates operational location | Warehouse address version and shipment origin snapshots | Catalog release publication | Recovery revalidates current warehouse address version through published SQL | Projections/cache derive from admitted version | Published catalog/profile reads | Warehouse writes address versions only after admission |
| Affiliate Geographic Resolution | Checkout/Order own address candidate/snapshot; Affiliate owns attribution only | Address Admission validates affiliate checkout candidate | Order snapshot after checkout admission | Catalog release publication | Recovery revalidates checkout candidate through published SQL | Affiliate analytics/search are derivative | Published catalog/profile reads and order snapshots | Affiliate never writes Geographic authority |
| CommerceHub Geographic Resolution | CommerceHub owns channel address lifecycle and order/suborder snapshots; Geographic owns admission | Address Admission validates channel candidate and split-order destination | CommerceHub order/suborder snapshots | Catalog release publication | Recovery revalidates candidates or reads immutable snapshots through SQL truth | CommerceHub projections/search/cache are derivative | Published catalog/profile reads and snapshots | CommerceHub writes snapshots only after admission |
| Returns Geographic Resolution | Return domain owns return-origin/destination snapshots; Geographic owns admission and mapping | Address Admission and Provider Mapping validate return shipment geography | Return/shipment immutable snapshots | Catalog and mapping releases | Recovery re-enters published SQL and existing snapshots | Projections/cache derive from snapshots | Published catalog/mapping and immutable snapshots | Return writes snapshots only after admission |
| Analytics Geographic Dimensions | Geographic Catalog owns dimensions; business domains own event/snapshot source | Analytics validates only published IDs/snapshot evidence | Analytics projection only | Derived from catalog publication | Rebuild from SQL snapshots and published catalog | Analytics stores are derivative only | Published IDs and immutable business snapshots | Projection writes only; no authority writes |

## Read Authority

Runtime read authority begins only at an active published catalog release or an
active published carrier geographic mapping release.

Allowed runtime reads:

- catalog children, place details, country profiles, localization, aliases, and
  search candidates from the active published catalog release;
- provider mapping results from the active published mapping release for the
  selected provider, environment, account scope, operation, and canonical
  lineage;
- immutable business snapshots already admitted by Geographic and owned by
  their business aggregate;
- derivative Redis, Mongo, Elasticsearch, autocomplete, and offline package
  reads only after release/checksum admission and only as acceleration or
  candidate presentation.

Forbidden runtime reads:

- drafts;
- unpublished validation output;
- raw provider sync payloads;
- unreviewed sync candidates;
- rejected or deferred candidates;
- unpublished overrides;
- stale cache entries without release agreement;
- provider API live lookup as mapping authority;
- Provider API live lookup as mapping authority;
- browser storage or URL state as authority.

## Write Authority

Runtime write authority is split by ownership:

- Geographic writes catalog and mapping authority only through governed
  Platform Admin draft, validation, approval, publication, rollback, and audit
  workflows.
- Address-owning domains write candidate address state and durable admitted
  address versions under their own lifecycle authority.
- Orders, shipments, returns, CommerceHub, warehouse, and affiliate-related
  flows write immutable admitted snapshots only after Geographic admission.
- Shipment registration writes the exact mapping release and provider
  identifiers used for external registration only after Provider Geographic
  Mapping resolution succeeds.

No runtime consumer may write canonical place identity, hierarchy, localization,
aliases, country profiles, catalog releases, mapping releases, mapping
publication state, or mapping freshness authority.

## Publication Authority

Runtime publication order is fixed:

```text
Published Release
  -> Runtime Resolution
  -> Projection
  -> Cache
  -> Presentation
```

Drafts must never participate in runtime admission, provider resolution,
search projection, cache population, offline package generation, or
presentation as accepted authority.

Publication authority requires:

- immutable release;
- approved validation snapshot;
- approval separation;
- publication lock;
- checksum agreement;
- active release pointer update in SQL;
- append-only audit;
- invalidation for derivative projections and caches.

## Recovery Authority

Runtime recovery must always re-enter:

```text
Published Catalog
Published Mapping
SQL Truth
```

Recovery must never re-enter Redis, Mongo, Elasticsearch, offline packages,
browser state, provider payloads, autocomplete indexes, or search projections
as authority.

Allowed recovery shapes:

- reload active published catalog release from SQL;
- reload active published mapping release from SQL;
- reload immutable order, shipment, return, CommerceHub, warehouse, or address
  version snapshots from their SQL authority;
- rebuild derivative projections and caches from SQL;
- revalidate checkout or offline candidates against SQL before admission.

Forbidden recovery shapes:

- repair a missing place from text, alias, coordinate, provider label, or cache;
- resolve provider IDs from live provider API without a published mapping
  release;
- rewrite historical snapshots after catalog rename, mapping rollback,
  provider response, tracking event, or projection rebuild;
- promote stale offline package content into accepted identity.

## Projection Authority

Projection is derivative only.

Projection admission requires:

- active published catalog or mapping release;
- release ID and checksum agreement;
- SQL source read;
- no draft or candidate rows;
- no provider API as source of accepted mapping authority;
- no mutation back into catalog, mapping, or business snapshots.

Projection may withhold, rebuild, invalidate, refetch, and present. It cannot
create, infer, repair, publish, roll back, or synchronize authority.

## Cache Authority

Redis, Mongo, Elasticsearch, offline packages, search indexes, autocomplete
indexes, browser memory, React state, local storage, session storage, and
provider runtime responses are derivative only.

Cache admission rules:

- cache keys must include release identity where authority depends on release;
- cache values must be invalidated on publication, supersede, rollback, or
  checksum mismatch;
- cache miss or cache loss falls back to SQL;
- stale release disagreement fails closed;
- cache rebuild reads SQL truth only.

Cache can accelerate reads and candidate presentation. Cache cannot decide
identity, validation, publication, rollback, recovery, synchronization, or
mapping resolution.

## Synchronization Authority

Synchronization imports evidence only.

Provider sync is evidence intake only.

Provider sync may:

- fetch provider catalog evidence;
- store bounded, redacted raw evidence;
- create sync runs;
- create candidates;
- calculate diffs;
- propose mappings;
- carry forward mappings only when provider stable ID, parent, scope,
  semantics, and release policy agree.

Provider sync may not:

- publish automatically;
- mutate canonical places directly;
- mutate published mapping releases;
- update runtime caches as authority;
- resolve runtime shipment mappings before review, approval, and publication.

Runtime synchronization reads only published releases.

## Cross-domain Authority

| Domain | Allowed relationship | Forbidden relationship |
|---|---|---|
| Shipping | Consumes admitted origin/destination/return snapshots and resolved mapping release evidence before adapter invocation | Owning catalog identity or provider mapping release authority |
| Checkout | Owns transient address candidates and submits selected canonical IDs for admission | Creating place identity, provider IDs, or accepted address authority from browser state |
| Orders | Owns immutable admitted shipping/billing snapshots | Rewriting snapshots from catalog rename, saved address edit, tracking, provider payload, projection, or cache |
| Warehouses | Owns operational address lifecycle and admitted versions | Mutating catalog, mappings, or historical shipment origins |
| Affiliate | Owns attribution only and reads order geography as context | Mutating checkout address, catalog identity, mapping, or snapshots |
| CommerceHub | Owns channel address lifecycle and marketplace order/suborder snapshots | Owning platform catalog, provider mappings, or cross-vendor geographic scope |
| Returns | Owns return address/shipment snapshots after admission | Rewriting original order/shipment geography or resolving mappings from cache/provider text |
| Analytics | Consumes canonical IDs, release evidence, and immutable snapshots as dimensions | Resolving, repairing, or mutating identity |
| Search | Projects published labels, aliases, IDs, and release evidence | Becoming catalog, localization, alias, or publication authority |
| Carrier Adapters | Serialize resolved mapping evidence and address text required by the wire contract | Lookup, infer, create, repair, synchronize, or publish geographic mappings |
| Provider APIs | Supply sync evidence or carrier response evidence | Owning canonical or provider mapping authority |

Geographic remains the authority across all cross-domain interactions.

## Fail-Closed Runtime Conditions

Runtime resolution, publication, projection, cache admission, and presentation
must fail closed for:

- Unknown Place;
- Unknown Mapping;
- Unpublished Release;
- Stale Release;
- Broken Hierarchy;
- Broken Mapping;
- Missing Localization;
- Invalid Country Profile;
- Publication Conflict;
- Checksum Mismatch;
- inactive, retired, deprecated-without-approved-successor, or wrong-release
  place identity;
- missing, ambiguous, inactive, stale, wrong-provider, wrong-environment,
  wrong-account, wrong-tenant, or hierarchy-invalid provider mapping;
- stale offline package or cache release mismatch;
- projection sourced from draft/candidate/sync evidence;
- autocomplete ambiguity promoted as identity.

Fail closed means the runtime withholds admission, provider registration,
projection, cache population, or presentation of accepted authority. It may
return structured failure evidence and request review or revalidation.

## Risk Analysis

| Risk | Status | Mitigation |
|---|---|---|
| Runtime consumer reads draft catalog or mapping data | Mitigated by design | Published-release-only runtime reads and projection admission. |
| Checkout becomes Geographic Authority | Mitigated by design | Checkout owns candidates only and must re-enter Address Admission. |
| Shipment or carrier adapter infers provider geography | Mitigated by design | Provider resolution occurs before adapter invocation from published mapping SQL. |
| Search, Mongo, Redis, autocomplete, or offline package becomes authority | Mitigated by design | Derivative-only cache/projection rule with release/checksum admission and SQL recovery. |
| Provider API becomes runtime mapping authority | Mitigated by design | Provider sync is evidence intake only; runtime consumes published mapping releases. |
| Recovery rebuilds from cache | Mitigated by design | Recovery must re-enter Published Catalog, Published Mapping, and SQL Truth. |
| Missing localization blocks necessary presentation | Mitigated by design | Runtime fails closed or applies approved fallback policy from published country profile; it never fabricates names. |
| Later implementation accidentally weakens authority | Residual | Architecture regression tests freeze runtime invariants before implementation. |
| No runtime implementation exists yet | Accepted | GEO-4 certifies authority architecture only; production activation requires later implementation and runtime proof. |

## Final Recommendation

Approve GEO-4 as the Geographic Runtime Authority certification freeze. The
runtime authority model is production-certifiable as architecture, but not
production active because no runtime, EF, migration, service, API, Checkout,
Shipping, provider adapter, projection, or cache implementation exists in this
phase.

Proceed next to **GEO-5 - Geographic Runtime Implementation Plan** only after
GEO-1, GEO-1.5, GEO-2, GEO-3, and GEO-4 are approved. GEO-5 should plan the
implementation sequence for read admission, projection admission, publication
validation, runtime guards, recovery re-entry, architecture tests, service
contracts, and operational proof while preserving the existing ban on Checkout
UI, EF entities, migrations, carrier adapters, and business runtime changes
unless explicitly authorized by that phase.

## Explicit Success Answers

- Reviewed ONLY Geographic Runtime: **Yes**.
- Runtime files modified: **No**.
- Backend files modified: **No runtime backend files modified**.
- Frontend files modified: **No**.
- Architecture files added: **Yes**,
  `docs/architecture/GEO-4_GEOGRAPHIC_RUNTIME_AUTHORITY_CERTIFICATION.md`.
- Architecture tests added: **Yes, architecture regression only**,
  `PointOfSale.Tests/Architecture/GeographicRuntimeAuthorityCertificationTests.cs`.
- Authority violations found: **No current runtime authority violation found**.
  Geographic runtime implementation is not yet active; the reviewed scope is
  future runtime responsibility under frozen GEO architecture.
- Runtime hardening required: **No**. No business logic implementation was
  required or performed.
- Production-certifiable: **Yes as architecture; not production active until
  implementation and runtime proof exist**.
- Recommend next phase: **GEO-5 - Geographic Runtime Implementation Plan**.
