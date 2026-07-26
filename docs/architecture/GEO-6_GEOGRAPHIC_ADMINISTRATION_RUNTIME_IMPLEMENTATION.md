Architecture correction performed.

# GEO-6 - Geographic Administration Runtime Implementation

Status: Implemented with architecture correction  
Date: 2026-07-05  
Governing architecture: `ADR-GEO-001_CANONICAL_GEOGRAPHIC_IDENTITY.md`, `GEO-1.5_GEOGRAPHIC_DOMAIN_BOUNDARIES.md`, `GEO-2_CANONICAL_GEOGRAPHIC_SQL_MODEL.md`, `GEO-3_GEOGRAPHIC_CATALOG_GOVERNANCE_AUTHORITY.md`, `GEO-4_GEOGRAPHIC_RUNTIME_AUTHORITY_CERTIFICATION.md`, and `GEO-5_CANONICAL_GEOGRAPHIC_RUNTIME_IMPLEMENTATION.md`

## Architecture Correction

The previous GEO-6 implementation was architecturally incorrect because it placed Geographic Administration DTOs under the Super Admin namespace and folder:

- `PointOfSale.API/SuperAdmin/DTOs/GeographicAdministrationDtos.cs`

That placement implied Super Admin ownership of Geographic Administration. This violates the approved platform architecture for this ERP.

## Why The Previous Placement Was Wrong

This project does not use a traditional global Super Admin model for ERP operations. POS Admin is the central authority for vendor storefront operations and owns the operational administration surface.

POS Admin owns:

- Products
- Inventory
- Customers
- Orders
- Payments
- Shipping
- Checkout
- Storefront
- Affiliate
- Geographic Catalog
- Carrier Configuration
- Carrier Geographic Mapping
- Carrier Releases
- Carrier Overrides
- Carrier Synchronization
- Publication
- Rollback
- Country Profiles
- Localization
- Aliases
- Address Admission

Super Admin owns only CommerceHub Marketplace management:

- CommerceHub Marketplace
- Marketplace Vendors
- Marketplace Policies
- Marketplace Settlement
- Marketplace Catalog

Super Admin must never own Geographic Catalog, Carrier Mapping, Carrier Administration, Country Profiles, Publication, Rollback, Localization, Address Admission, Shipping, Checkout, Storefront, Affiliate, or Payment.

## Audit Result

The audit found one incorrectly placed Geographic Administration runtime component under Super Admin.

Incorrectly placed components:

- Controllers: 0
- Services: 0
- DTOs: 1
- Authorization policies: 0
- Endpoints: 0
- Permissions: 0

The misplaced DTO file had no controller, service, endpoint, DI registration, or authorization policy consumers. No Super Admin route was active for Geographic Administration.

## What Was Moved

Moved from Super Admin:

- `PointOfSale.API/SuperAdmin/DTOs/GeographicAdministrationDtos.cs`

Moved to POS Admin/API-owned geographic administration DTO namespace:

- `PointOfSale.API/DTOs/GeographicAdministration/GeographicAdministrationDtos.cs`

Namespace changed from:

- `PointOfSale.API.SuperAdmin.DTOs`

To:

- `PointOfSale.API.DTOs.GeographicAdministration`

## POS Admin Runtime Added

GEO-6 adds a POS Admin-owned Geographic Administration runtime surface:

- Controller: `PointOfSale.API/Controllers/AdminGeographicAdministrationController.cs`
- Service: `PointOfSale.API/Services/Admin/GeographicAdministrationService.cs`
- DI registration: `IGeographicAdministrationService -> GeographicAdministrationService`
- Route root: `api/admin/geographic`
- Authorization: `Owner,Admin,Manager`
- Audit actor: `GeographicAuditActorKind.PosAdmin`

Implemented POS Admin administration capabilities:

- Catalog Administration
- Country Profiles
- Hierarchy Administration through place staging
- Localization
- Aliases
- Carrier Mapping Administration
- Carrier Mapping Releases
- Draft lifecycle
- Publication
- Rollback
- Merge evidence
- Split evidence through merge-kind workflow
- Deprecation
- Override Administration
- Sync Candidate Review
- Audit
- Authorization

Publication preserves authority rules:

- Drafts remain isolated.
- Publication requires a validated release.
- Publication requires a different actor from the draft creator.
- Publication writes append-only publication and audit evidence.
- Rollback activates prior published release evidence without rewriting business snapshots.
- Provider synchronization records sync runs and candidates only.

## Explicit Non-Implementation

GEO-6 does not implement:

- Checkout runtime
- Shipping runtime
- Carrier adapters
- CommerceHub runtime
- Storefront runtime
- Affiliate runtime
- Search
- Redis
- Mongo
- Elasticsearch

## Canonical Checkout Rule

The platform has one canonical checkout/address model. Storefront Checkout, Affiliate Checkout, and CommerceHub Checkout may use different UIs, but they must all emit the same canonical checkout DTO and canonical address structure.

No CommerceHub-specific address entity and no Affiliate-specific address entity may be introduced.

## Regression Tests

Added regression coverage:

- `PointOfSale.Tests/Architecture/GeographicAdministrationPosAdminOwnershipTests.cs`

The tests prevent future regressions by asserting:

- Geographic Administration is not placed under `PointOfSale.API/SuperAdmin`.
- Geographic Administration routes use `api/admin/geographic`.
- Geographic Administration uses POS Admin roles: `Owner,Admin,Manager`.
- Geographic Administration does not use `api/super-admin` or `RequireSuperAdmin`.
- DTOs do not use Super Admin namespace placement.
- Audit uses `GeographicAuditActorKind.PosAdmin`.
- Four-eyes publication protection remains present.
- Persistent context freezes POS Admin ownership and Super Admin marketplace-only scope.

## Explicit Success Answers

- Was the previous GEO-6 architecture incorrect? **Yes.**
- Exactly what was corrected? **The misplaced Geographic Administration DTO was moved from Super Admin to POS Admin/API ownership, and the runtime administration surface was implemented under POS Admin routes, services, authorization, and audit.**
- How many controllers moved? **0.**
- How many services moved? **0.**
- How many DTOs moved? **1.**
- How many endpoints moved? **0.**
- How many authorization policies changed? **0 existing policies changed; new POS Admin role authorization was added for the POS Admin controller.**
- Are any Geographic runtime components still under Super Admin? **No.**
- Is the implementation now fully aligned with the platform architecture? **Yes.**
