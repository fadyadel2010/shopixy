# SHOPIXY ARCH-2 Machine-Enforced Governance

Status: Permanent architecture enforcement

Scope: Storefront product SQL invariants, product projection admission, storefront runtime authority, source governance, CI/CD gates, and mutation certification.

Non-goals: No new features. No UX changes. No commerce behavior changes.

Final certification: ARCHITECTURE HARDENED

## 1. Database Enforcement Report

| Invariant | Enforcement | Exact Recommendation | Status |
|---|---|---|---|
| ProductUnit active ProductId/UnitId uniqueness | Filtered unique index | Keep `UX_ProductUnits_ProductId_UnitId_Active` where `IsDeleted = false`. | Enforced |
| One active base unit per product | Filtered unique index | Keep `UX_ProductUnits_ProductId_Base_Active` where `IsDeleted = false AND IsBaseUnit = true`. | Enforced |
| One active default unit per product | Filtered unique index | Keep `UX_ProductUnits_ProductId_Default_Active` where `IsDeleted = false AND IsDefault = true`. | Enforced |
| ProductUnit ConversionFactor positive | CHECK constraint | Add `CK_ProductUnits_ConversionFactor_Positive` with `"ConversionFactor" > 0`. | Enforced |
| Base ProductUnit ConversionFactor exactly 1 | CHECK constraint | Add `CK_ProductUnits_BaseUnit_ConversionFactor_One` with `NOT "IsBaseUnit" OR "ConversionFactor" = 1`. | Enforced |
| Historical invalid ProductUnit rows | Migration validation | Migration raises before adding constraints when active invalid rows exist. | Enforced |
| DefaultUnit price for public simple products | Projection admission plus publish-time recommendation | Reject public simple-product projection writes when DefaultUnit is missing or non-positive; add future admin publish validator if publication mutation still allows it. | Enforced at projection |
| StorefrontEffectivePrice persistence | Impossible typed persistence plus static governance | Keep `[BsonIgnore]` computed getter, no setter, no DTO/input/migration column. | Enforced |

Implemented in:

- `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`
- `PointOfSale.Infrastructure/Migrations/20260729120000_StorefrontProductUnitDbEnforcement.cs`
- `PointOfSale.Tests/Architecture/StorefrontArchitectureEnforcementTests.cs`

## 2. Projection Admission Policy

All public product projection upserts must pass `StorefrontProductProjectionAdmissionPolicy` before `IProductReadRepository.UpsertAsync`.

Admission rejects:

- Missing or mismatched TenantId.
- Missing or mismatched ProductId.
- Stale `ReadModelSchemaVersion`.
- Stale `ProjectionVersion`.
- Missing event identity, timestamp, or positive event version.
- Public upsert with invalid publication state.
- Public simple product missing DefaultUnit.
- Public simple product with invalid DefaultUnit id, price, conversion factor, or base conversion factor.
- Variant product missing variants.
- Variant row missing VariantId.
- Variant product with no active priced variant.
- Any attempt to make `StorefrontEffectivePrice` persistable.

Implemented in:

- `PointOfSale.Infrastructure/MongoDB/Services/StorefrontProductProjectionAdmissionPolicy.cs`
- `PointOfSale.Infrastructure/MongoDB/Services/StorefrontProductProjectionMutationBoundary.cs`
- `PointOfSale.Infrastructure/MongoDB/MongoDbServiceCollectionExtensions.cs`

## 3. Static Analysis Rules

| Rule | Machine Gate |
|---|---|
| No duplicated availability logic | Architecture tests require runtime consumers to call `ProductStorefrontVisibilityAuthority` and reject local `active && visible && stock` formulas in governed surfaces. |
| No duplicated price logic | Architecture tests reject frontend discount formulas and cart/runtime fallback formulas outside named pricing authorities. |
| No frontend business authority | Storefront product components/hooks must consume backend `pricing` and `availability` DTOs. |
| No projection authority leakage | Product projection consumers must use `IProductSqlAuthorityProjectionWriter`; direct repository upserts stay behind the mutation boundary. |
| No persisted StorefrontEffectivePrice | Tests require computed `[BsonIgnore]` getter and reject setter/BSON field patterns. |
| No runtime authority bypass | Customer-facing storefront read paths must re-enter `ProductSearchSqlAuthorityOverlay.ApplyAsync` before public use. |

Implemented in:

- `PointOfSale.Tests/Architecture/StorefrontArchitectureEnforcementTests.cs`
- `PointOfSale.Tests/Architecture/StorefrontArchitectureMutationTests.cs`
- Existing ARCH-1 storefront architecture tests.

## 4. CI/CD Enforcement Policy

Every pull request must fail when any of these gates fail:

- Architecture tests.
- Governance/static analysis tests.
- Projection admission tests.
- Architecture mutation tests.
- Duplicate business rule detection.
- Forbidden runtime pattern detection.
- Storefront frontend tests and build.
- Admin frontend tests and build.

Implemented in `.github/workflows/architecture-governance.yml`.

## 5. Mutation Test Report

| Mutation | Expected Kill Switch |
|---|---|
| Persist `StorefrontEffectivePrice` | `StorefrontArchitectureMutationTests.Mutation_PersistStorefrontEffectivePrice_MustBeKilled` plus admission reflection guard. |
| Duplicate availability logic | Runtime/static architecture tests require authority delegation and reject local public formulas. |
| Bypass `ProductStorefrontVisibilityAuthority` | Cart, wishlist, discovery, product runtime tests require named authority calls. |
| Frontend derives availability | Product runtime component/hook tests require DTO availability consumption and reject stock-derived formulas. |
| Projection bypasses SQL overlay | Projection consumers must use `IProductSqlAuthorityProjectionWriter`; writer must use SQL overlay and admission boundary. |

## 6. Permanent Governance Workflow

| Change Type | Mandatory Checks |
|---|---|
| New DTO | Must be presentation-only; no trusted price, stock, availability, subtotal, discount, total, or publication authority inputs. Add architecture test when exposed to storefront. |
| New Storefront endpoint | Must re-enter SQL authority overlay or named runtime authority before customer-facing product data returns. Add endpoint source test. |
| New Projection | Must use SQL recomposition, event freshness, admission policy, tenant/source identity, schema/projection version, and tombstone behavior. |
| New Product Runtime | Must delegate purchasability to `ProductStorefrontVisibilityAuthority` and pricing presentation to backend mappers. |
| New Cart Logic | Must reload current product/variant authority and ignore client/projection prices and stock decisions. |
| New Frontend Product Component | Must consume backend `pricing` and `availability`; may format or disable UI only. Add static test for forbidden formulas. |

## 7. Architecture Enforcement Matrix

| Quality Gate | Enforcement |
|---|---|
| Impossible invalid ProductUnit state | Filtered unique indexes plus CHECK constraints and migration validation. |
| Impossible duplicated authority | Architecture/static tests block known duplicate logic in governed surfaces. |
| Impossible persisted StorefrontEffectivePrice | Computed getter, `[BsonIgnore]`, no setter, admission reflection guard, static tests. |
| Impossible projection authority leakage | Mutation boundary plus admission policy plus projection writer requirement. |
| Impossible frontend business authority | Product runtime static tests require DTO consumption and reject formulas. |
| Impossible bypass of canonical purchasability authority | Runtime tests require `ProductStorefrontVisibilityAuthority`. |
| CI automatically blocks violations | GitHub workflow runs architecture, admission, static, mutation, frontend test, and build gates. |

## 8. Implementation Summary

ARCH-2 moved storefront governance from developer discipline into code, tests, migration validation, and CI policy. The strongest new guarantees are ProductUnit database constraints and pre-persistence projection admission. Static and mutation tests now make forbidden storefront authority duplication fail in CI.

## 9. Final Certification

ARCHITECTURE HARDENED
