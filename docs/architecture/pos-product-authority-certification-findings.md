# PASS P1-EA POS Product Authority Certification

Date: 2026-06-07

## Verdict

POS Product Authority is production-certifiable for the inspected runtime after hardening.
PostgreSQL tenant-owned Product, ProductVariant, Category, Unit, and ProductUnit state is
authoritative. DTO fields, route IDs, query parameters, frontend state, MongoDB, Redis,
Elasticsearch, and event payloads remain intent, cache, or projection evidence only.

## Product Ownership Findings

- Products are tenant-owned through `Product.TenantId`, the mandatory tenant query filter,
  trusted `ITenantContext.RequiredTenantId`, and `TenantSaveChangesInterceptor`.
- Product create assigns the trusted tenant. Product reads, updates, deletes, barcode lookup,
  search, and unit binding remain subordinate to the current tenant query filter.
- Route and DTO values do not select or mutate another tenant's product ownership.
- No product ownership transfer path was found. Ownership drift is blocked at save time.

## Product Lifecycle Findings

- Product deletion is soft deletion in SQL and now atomically sets the product inactive,
  offline, and archived.
- Product deletion now also soft-deletes and deactivates every current child variant and emits
  product and variant deletion/archive events before commit.
- Deleted products remain excluded by SQL query filters. No product restore endpoint was found.
- Draft, archived, inactive, offline, or deleted products fail public visibility recomposition.

## Variant Findings

- Variant ownership is SQL-bound through tenant/product foreign-key lineage and current semantic
  ownership state.
- Superseded variants are now excluded from POS quick search, quick access, list, and detail reads.
- Simple products now reject additional variant creation, non-default variant mutation, and
  default-variant reassignment.
- Product deletion retires child variants, preventing live orphan variants after parent deletion.
- Existing SQL unique indexes protect current variant SKU, barcode, attribute-combination, and
  default-variant ownership.

## Category And Unit Findings

- Product create/update requires `CategoryLineageAuthority.RequireLiveCategoryAsync`, proving the
  full same-tenant, non-deleted, non-cyclic SQL category lineage.
- Product-unit creation now requires an active same-tenant SQL Unit.
- Product-unit read/mutation query filtering now proves both parent Product tenant and Unit tenant.
- SQL indexes now enforce one live `(ProductId, UnitId)` binding, one live base unit, and one live
  default unit per product.

## SKU And Barcode Findings

- Current variant SKU and barcode ownership remains protected by SQL unique indexes.
- Simple-product SKU/barcode identity remains mirrored through its SQL default variant.
- Barcode validation and generation now check Product, current ProductVariant, and ProductUnit
  identifiers.
- ProductUnit barcodes now have a filtered unique index.
- Migration `20260607190000_PosProductAuthorityHardening` adds rollout preflight plus PostgreSQL
  advisory-lock triggers across Products, ProductVariants, and ProductUnits, closing concurrent
  cross-table barcode collision races.

## Visibility Findings

- SQL Product lifecycle state remains decisive: active, published, online-visible, non-deleted
  product state plus valid lineage is required for public visibility.
- Hidden, archived, inactive, and deleted products cannot be restored by stale projection state.
- Authorized product update may intentionally publish a product; DTO or frontend state alone does
  not create visibility without the committed SQL transition.

## Search And Query Findings

- Product list, lookup, ID lookup, barcode lookup, quick search, and quick access remain tenant
  scoped through SQL query filters.
- Category and active filters narrow current tenant SQL results and cannot widen tenant scope.
- POS quick search and quick access now exclude merged, split, detached, or otherwise superseded
  variant identities.
- Route IDs and query parameters cannot bypass current tenant product or variant ownership.

## Adversarial Findings

- Cross-tenant product/variant/category/unit IDs collapse to not-found or invalid authority.
- DTO category tampering fails live SQL lineage validation.
- DTO unit tampering fails active same-tenant Unit validation.
- Simple-product variant injection and default reassignment now fail closed.
- SKU/barcode collision attempts converge through SQL uniqueness, current ownership checks, and
  PostgreSQL cross-table barcode serialization.
- Deleted-parent variant guessing no longer returns a live child after product deletion.

## Runtime Hardening Performed

- Retired all child variants during product deletion.
- Removed superseded semantic variants from POS search, quick access, list, and detail paths.
- Blocked additional/non-default variant manipulation for simple products.
- Required active same-tenant Units for ProductUnit binding.
- Added explicit Product/Unit tenant lineage to ProductUnit query filters and mutations.
- Extended barcode authority to ProductUnits and barcode generation.
- Added ProductUnit binding/base/default/barcode SQL uniqueness.
- Added migration preflight and advisory-lock barcode authority triggers.

## Exact Runtime Files Modified

- `PointOfSale.API/Controllers/ProductsController.cs`
- `PointOfSale.API/Controllers/ProductVariantsController.cs`
- `PointOfSale.API/Controllers/UnitsController.cs`
- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
- `PointOfSale.Infrastructure/Reliability/ProductVariantCloneImportAuthority.cs`
- `PointOfSale.Infrastructure/Migrations/20260607190000_PosProductAuthorityHardening.cs`
- `PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/PosProductAuthorityCertificationTests.cs`
- `ProductVariantCloneImportAuthorityTests.ProductUnitBarcodeAuthority_BlocksProductVariantAndSiblingUnitCollisions`

## Verification

- API build passed with zero errors.
- 11 focused PASS P1-EA and clone/import authority tests passed.
- 51 existing category lineage, category tenant scope, variant lifecycle, merge/split, and data
  authority tests passed.
- A broader source-freeze run exposed three unrelated existing failures in
  `CrossSystemCriticalRiskHardeningTests`; none were caused by the P1-EA runtime behavior.

## Remaining Debt

- Apply and verify migration `20260607190000_PosProductAuthorityHardening` in staging before rollout.
- Run a PostgreSQL-backed concurrency drill for simultaneous Product, ProductVariant, and
  ProductUnit barcode claims.
- Add production parity monitoring for SQL product/variant/unit identifier and lifecycle drift.
- Product and variant identifiers are globally unique today, which is stricter than tenant-local
  uniqueness; changing that policy requires a separately certified migration.

## Required Answers

- Can Tenant A access Tenant B products? **No.**
- Can product ownership drift? **No, not through inspected runtime paths.**
- Can deleted products reappear? **No.**
- Can hidden products become visible? **Only through an authorized committed SQL publication
  transition; stale/frontend/projection state cannot make them visible.**
- Can variants survive parent deletion? **No after this hardening.**
- Can invalid categories attach? **No.**
- Can invalid units attach? **No.**
- Can duplicate SKUs occur? **No for current sellable product/variant authority.**
- Can duplicate barcodes occur? **No for live Product, current ProductVariant, and ProductUnit
  authority after the migration is applied.**
- Can route/query tampering bypass authority? **No.**
- Were runtime code changes required? **Yes.**
- Is POS Product Authority production-certifiable? **Yes, conditional on applying the included
  migration before deployment.**
