# GEO-5 - Canonical Geographic Runtime Implementation

Status: Foundational runtime implemented  
Date: 2026-07-05  
Governing architecture: `ADR-GEO-001_CANONICAL_GEOGRAPHIC_IDENTITY.md`, `GEO-1.5_GEOGRAPHIC_DOMAIN_BOUNDARIES.md`, `GEO-2_CANONICAL_GEOGRAPHIC_SQL_MODEL.md`, `GEO-3_GEOGRAPHIC_CATALOG_GOVERNANCE_AUTHORITY.md`, and `GEO-4_GEOGRAPHIC_RUNTIME_AUTHORITY_CERTIFICATION.md`

## Executive Summary

GEO-5 implements the approved Geographic Identity foundation as runtime code.
The implementation is intentionally limited to platform Geographic domain
models, EF configuration, an additive schema migration, repository/service
contracts, fail-closed foundational service implementations, and regression
tests.

No Checkout UI, Checkout runtime, Shipping runtime, carrier adapter,
Storefront, Order, Warehouse, Affiliate, CommerceHub, Search, Redis, Mongo,
Elasticsearch, or external provider integration was implemented.

## Runtime Components Added

- Core Geographic entity models under `PointOfSale.Core/Entities/Geographic`.
- Core service and repository contracts under `PointOfSale.Core/Interfaces/Geographic`.
- EF configurations under `PointOfSale.Infrastructure/Data/Configurations/Geographic`.
- `ApplicationDbContext` `DbSet` registration and foundational immutability guards.
- Foundational SQL-backed repository and fail-closed service implementations under `PointOfSale.Infrastructure/Geographic`.
- Additive migration `20260705170009_InitialGeographicRuntimeFoundation`.
- Architecture/runtime regression tests in `GeographicRuntimeFoundationImplementationTests`.

## Entities Added

Catalog:

- `GeographicPlace`
- `GeographicPlaceLevel`
- `GeographicPlaceHierarchyEdge`
- `GeographicPlaceName`
- `GeographicPlaceAlias`
- `GeographicCountryProfile`
- `GeographicCatalogRelease`
- `GeographicCatalogReleasePlace`
- `GeographicCatalogReleaseEdge`
- `GeographicCatalogPublication`

Mapping and sync:

- `CarrierProvider`
- `CarrierGeographicMappingRelease`
- `CarrierGeographicMapping`
- `CarrierGeographicMappingGroup`
- `CarrierGeographicMappingPublication`
- `CarrierGeographicSyncRun`
- `CarrierGeographicSyncCandidate`
- `CarrierGeographicOverride`

Lineage, admission, normalization, and audit:

- `PlaceMergeHistory`
- `PlaceDeprecation`
- `AddressAdmissionEvidence`
- `AddressNormalizationRule`
- `GeographicAudit`

## EF Configurations

The EF model includes:

- primary keys for all entities;
- composite keys for release membership tables;
- uniqueness on release numbers, release keys, stable place keys, country ISO codes, provider identities, mapping identities, normalization rule versions, merge/deprecation records, and provider group keys;
- check constraints for status values, place kinds, parent/country rules, validity windows, coordinate ranges, confidence ranges, publication kinds, environment/scope values, and provider mapping windows;
- release relationships using restrictive delete behavior;
- provider/environment/account/tenant scoped mapping indexes;
- search and hierarchy indexes for names, aliases, parent/child edges, and country profiles;
- PostgreSQL `xmin` concurrency tokens for catalog and mapping releases;
- append-only guards for publications, admission evidence, merge/deprecation lineage, and `GeographicAudit`;
- published-release immutability guards for catalog and mapping release rows.

## Migration Summary

Migration added:

- `PointOfSale.Infrastructure/Migrations/20260705170009_InitialGeographicRuntimeFoundation.cs`

The migration creates only Geographic foundation tables and indexes. It does
not alter, drop, rename, or add columns to existing business tables.

The initial EF-generated migration was rejected because it included unrelated
pending model drift. It was replaced with a manual additive GEO-only migration.

## Service Contracts

Contracts added:

- `IGeographicRepository`
- `IGeographicCatalogService`
- `IGeographicResolver`
- `IGeographicPublicationService`
- `ICarrierGeographicMappingService`
- `IGeographicSynchronizationService`
- `IAddressAdmissionService`

Foundational implementations added:

- `GeographicRepository`
- `GeographicCatalogService`
- `GeographicResolver`
- `CarrierGeographicMappingService`
- `GeographicPublicationService`
- `GeographicSynchronizationService`

Publication and rollback implementations intentionally fail closed with
foundation-only failure codes. They do not publish automatically and do not
implement admin workflows.

## Authority Preservation

GEO-4 rules are preserved:

- runtime reads require `Published` catalog releases;
- provider mappings require `Published` mapping releases;
- stale mapping releases fail closed;
- Address Admission records evidence only after published catalog validation;
- provider IDs remain isolated to mapping tables;
- no provider IDs were added to Checkout, Orders, Customers, saved addresses, address books, warehouses, stores, branches, carrier configuration, or generic address records;
- no carrier adapter performs geographic lookup or inference;
- publication and rollback are not automatic;
- sync runs/candidates remain evidence, not authority;
- Redis, Mongo, Elasticsearch, Search, browser state, and provider APIs were not implemented or used.

## Regression Tests

Added:

- `PointOfSale.Tests/Architecture/GeographicRuntimeFoundationImplementationTests.cs`

The tests freeze:

- required entity registration;
- service contract surface;
- additive geographic-only migration behavior;
- published-release-only runtime guard strings;
- no Checkout, Shipping, carrier adapter, or business address model coupling in GEO-5 contracts/migration.

Existing GEO architecture tests remain in place:

- `GeographicSqlModelArchitectureTests`
- `GeographicCatalogGovernanceAuthorityTests`
- `GeographicRuntimeAuthorityCertificationTests`

## Verification

Completed:

- `dotnet build -nologo -v minimal --no-restore`
- `dotnet build PointOfSale.Infrastructure\PointOfSale.Infrastructure.csproj -nologo -v minimal --no-restore`
- `dotnet build PointOfSale.API\PointOfSale.API.csproj -nologo -v minimal --no-restore`
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter FullyQualifiedName~Geographic -nologo -v minimal --no-build --no-restore`
- `dotnet ef migrations list --project PointOfSale.Infrastructure\PointOfSale.Infrastructure.csproj --startup-project PointOfSale.API\PointOfSale.API.csproj --context ApplicationDbContext --no-build`
- `dotnet ef migrations script 20260531170000_ActivateCommerceHubPriceOverrides 20260705170009_InitialGeographicRuntimeFoundation --project PointOfSale.Infrastructure\PointOfSale.Infrastructure.csproj --startup-project PointOfSale.API\PointOfSale.API.csproj --context ApplicationDbContext --no-build --output C:\tmp\geo5-migration.sql`

Result:

- Build passed with existing warnings.
- Focused Geographic tests passed: 21 passed, 0 failed.
- EF migration discovery includes `20260705170009_InitialGeographicRuntimeFoundation`.
- Migration script generation succeeded to `C:\tmp\geo5-migration.sql`.
- Destructive SQL scan found no `DROP TABLE`, `DROP COLUMN`, `ALTER TABLE ... DROP`,
  `DELETE FROM`, or `TRUNCATE` statements in the generated script.

## Risks

- Production catalog administration, publication locks, approval separation,
  seed data, provider sync import, and admin APIs are not implemented in this
  foundation phase.
- No Checkout, Order, Shipment, Warehouse, Store, CommerceHub, Affiliate, or
  Return snapshot integration exists yet.
- No carrier mapping data exists, so provider-ID-dependent carrier execution
  remains fail-closed.
- The manual migration intentionally avoids unrelated EF model drift; future EF
  migration generation should be checked carefully for non-GEO changes.

## Final Recommendation

GEO-5 is production-ready as a foundation only. It creates the canonical
Geographic runtime substrate without activating business integrations.

Recommended next phase: **GEO-6 - Geographic Governance and Publication
Workflow Implementation**, limited to Platform Admin catalog/mapping draft,
validation, approval, publication, rollback, audit, and seed/import tooling.
Checkout and Shipping integration should remain blocked until governed
published catalog and mapping data exist.

## Explicit Success Answers

- Runtime files modified: **Yes**, Geographic foundation runtime files only.
- Backend files modified: **Yes**, Core/Infrastructure domain and EF files only.
- Frontend files modified: **No**.
- Entities added: **Yes**, Geographic catalog, release, publication, mapping, sync, override, admission, normalization, and audit entities.
- Configurations added: **Yes**, EF configurations for all Geographic entities.
- Migration added: **Yes**, additive GEO-only migration.
- Services added: **Yes**, service contracts and foundational fail-closed implementations.
- Tests added: **Yes**, architecture/runtime foundation tests.
- Authority preserved: **Yes**, published SQL releases remain the only runtime authority.
- Backward compatible: **Yes**, no existing business schema was changed by the manual migration.
- Production ready as foundation: **Yes**, not active for business integrations.
- Recommend next phase: **GEO-6 - Geographic Governance and Publication Workflow Implementation**.
