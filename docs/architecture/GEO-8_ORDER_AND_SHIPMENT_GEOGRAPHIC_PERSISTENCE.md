# GEO-8 - Order and Shipment Geographic Persistence

Status: Implemented  
Date: 2026-07-05  
Governing architecture: ADR-GEO-001 through GEO-7

## Executive Summary

GEO-8 persists admitted Canonical Geographic Identity into business documents. Orders and Shipments are immutable historical documents. They MUST NEVER depend on the live Geographic Catalog. Canonical Geographic Identity is resolved ONLY before persistence. After persistence, runtime consumes snapshots only.

## Runtime Changes

- Orders now store first-class shipping and billing geographic snapshots.
- Shipments now copy the destination geographic snapshot from the order snapshot at shipment creation.
- Customer saved addresses now have immutable admitted versions.
- `ApplicationDbContext` validates snapshots fail closed and audits snapshot/linkage creation.

## Persistence Model

- Additive nullable columns preserve legacy orders and shipments.
- New canonical snapshot columns store admission evidence, catalog release, canonical place IDs, display names, address detail, timestamp, and version.
- Saved addresses use `CustomerAddresses` plus immutable `CustomerAddressVersions`.
- Provider IDs are not added to orders, saved addresses, or generic business documents.

## Order Snapshot

Order shipping and billing snapshots persist `AddressAdmissionEvidenceId`, `CatalogReleaseId`, country, administrative area, locality, district, zone, display names, street, building, floor, apartment, landmark, timestamp, and snapshot version.

## Shipment Snapshot

Shipment destination snapshots are copied from order snapshots. Shipment creation does not call Geographic admission or catalog services and does not re-resolve geography after shipment creation.

## Saved Address Model

Saved customer addresses support versioned admitted address snapshots. Editing a saved address requires a new admitted `CustomerAddressVersion`; existing versions remain immutable for order, shipment, and audit history.

## Versioning Strategy

Snapshot version `1` is the only valid version in this phase. Invalid, missing, or partial snapshot versions fail closed in EF guards and SQL check constraints.

## Authority Matrix

| Concern | Authority |
|---|---|
| Place resolution | Geographic admission before persistence |
| Order history | Order snapshot only |
| Shipment history | Shipment snapshot or copied order snapshot only |
| Saved address edits | New address version only |
| Recovery | SQL snapshots and admission evidence only |
| Frontend | Presentation only |

## Recovery

Recovery restores order snapshots, shipment snapshots, and address admission evidence. Recovery must never re-resolve against a newer catalog release.

## Regression Tests

Added `GeographicPersistenceAuthorityTests` to freeze snapshot persistence, shipment independence, saved address versioning, fail-closed validation, audit hooks, and provider-ID exclusion.

## Verification

Completed verification:

- `dotnet build` passed with existing warnings.
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter FullyQualifiedName~Geographic -nologo -v minimal --no-build --no-restore` passed: 33 passed.
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter FullyQualifiedName~GeographicPersistenceAuthorityTests -nologo -v minimal --no-build --no-restore` passed: 5 passed.
- `dotnet ef migrations list --project PointOfSale.Infrastructure\PointOfSale.Infrastructure.csproj --startup-project PointOfSale.API\PointOfSale.API.csproj --context ApplicationDbContext --no-build` discovered `20260705183000_Geo8OrderShipmentGeographicPersistence`.
- Migration script generation from `20260705170009_InitialGeographicRuntimeFoundation` to `20260705183000_Geo8OrderShipmentGeographicPersistence` succeeded.
- Destructive SQL pattern scan found no `DROP TABLE`, `DROP COLUMN`, `ALTER TABLE ... DROP`, `DELETE FROM`, or `TRUNCATE` statements.

## Risks

- Legacy rows remain without canonical snapshots until admitted through approved migration/review.
- Existing carrier adapter execution still uses existing shipping text and is not redesigned in this phase.

## Final Recommendation

GEO-8 is production-certifiable for persistence authority. Recommend next phase: carrier geographic mapping snapshot integration for shipment registration, without changing order snapshots.

## Explicit Success Answers

- Reviewed ONLY Geographic Persistence runtime? **Yes**.
- Runtime files modified: **Yes**, persistence-only backend runtime.
- Backend files modified: **Yes**.
- Frontend files modified: **No**.
- Order snapshot implemented: **Yes**.
- Shipment snapshot implemented: **Yes**.
- Saved Address admission implemented: **Yes**, as admitted immutable versions.
- Snapshot immutability preserved: **Yes**.
- Provider IDs persisted? **NO**.
- Authority preserved: **Yes**.
- Backward compatible: **Yes**, additive nullable persistence.
- Production-certifiable: **Yes**.
- Recommend next phase: **Carrier mapping snapshot integration at shipment registration**.
