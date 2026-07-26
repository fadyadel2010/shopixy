# PASS SF-PA3.3 Product Variant Resolution Authority Certification

Date: 2026-06-28

## Certification

PointOfSale.Storefront Product Variant Resolution is production-certifiable after Storefront backend-resolution hardening.

Backend SQL plus Storefront backend read/resolution services own Variant Resolution, Variant Identity, Variant Lineage, SKU Resolution, Default Variant, Option Validation, Combination Validation, Publication/Visibility, DTO Admission, persistence, projection policy, failure behavior, and recovery. Storefront frontend owns option selection candidates, loading, gallery selection, quantity presentation, navigation, and display only.

## Runtime Findings

- `ProductSearchSqlAuthorityOverlay` rebuilds Storefront product variant evidence from current SQL `ProductVariants`, attributes, stocks, product lineage, tenant lineage, category lineage, publication state, semantic ownership, and active state before public DTO mapping.
- `StorefrontProductService.GetProductByIdAsync` maps detail DTOs only after current backend authority re-entry and cache/projection recomposition.
- Prior gap: `PointOfSale.Storefront/src/hooks/useVariantSelector.ts` matched selected option values, SKU values, and default variant fallbacks against the DTO variant array in React, allowing browser DTO state to resolve variant identity.
- Hardening: `StorefrontProductsController.ResolveProductVariant` and `StorefrontProductService.ResolveProductVariantAsync` now accept selected option candidates only, re-enter current backend detail authority, perform exact backend option-combination resolution, reject ambiguous/invalid combinations, and return an admitted `StorefrontVariantResolutionResponse`.
- Storefront `useVariantSelector` now posts `selectedOptions` to the backend resolver and uses only `response.variant` as resolved variant identity. Option availability is display-only and no longer locally combination-derived.

## Pipeline

Product Identity -> Selected Option Candidates -> Backend Variant Resolution -> Resolved Variant DTO -> DTO Admission -> Storefront Presentation.

Variant Resolution begins in `StorefrontProductService.ResolveProductVariantAsync` after `GetProductByIdAsync` re-enters current SQL-backed detail authority.

Resolved Variant begins only when the backend resolver returns one exact active option-combination match or backend default variant for an empty candidate set.

Storefront presentation begins only after `StorefrontVariantResolutionResponse.Variant` reaches the frontend.

## Boundaries

- Variant Boundary: backend SQL `ProductVariants`, current semantic owner, tenant/product lineage. Recovery source: SQL. Failure: no variant DTO.
- Resolution Boundary: `StorefrontProductService.ResolveProductVariantAsync`. Recovery source: current detail authority. Failure: 409/no resolution.
- Lineage Boundary: SQL tenant/product/variant/category joins and overlays. Recovery source: SQL. Failure: 404/409/withhold.
- SKU Boundary: SQL variant row, copied into admitted DTO only. Recovery source: SQL. Failure: withhold SKU.
- Presentation Boundary: Storefront `ProductHero`, `VariantSelector`, and `useVariantSelector`. Recovery source: backend resolver refetch. Failure: disabled add-to-cart and invalid combination copy.
- Projection Boundary: Mongo, Redis, Elasticsearch, search DTOs. Recovery source: SQL overlays/rebuilds. Failure: stale projection suppressed.
- Recovery Boundary: refresh, remount, restart, Redis flush, Mongo/search rebuild. Recovery source: backend SQL/service re-entry. Failure: fail closed until authority is available.

## Regression Freeze

- `PointOfSale.Tests/Architecture/StorefrontCatalogPresentationAuthorityCertificationTests.cs`

## Modified Runtime Files

- `PointOfSale.API/DTOs/Storefront/StorefrontDtos.cs`
- `PointOfSale.API/Services/Storefront/IStorefrontProductService.cs`
- `PointOfSale.API/Services/Storefront/StorefrontProductService.cs`
- `PointOfSale.API/Controllers/StorefrontProductsController.cs`
- `PointOfSale.Storefront/src/services/api/endpoints.ts`
- `PointOfSale.Storefront/src/services/api/storefrontApi.ts`
- `PointOfSale.Storefront/src/types/product.ts`
- `PointOfSale.Storefront/src/hooks/useVariantSelector.ts`
- `PointOfSale.Storefront/src/components/sections/defaults/product-hero.tsx`
