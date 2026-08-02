# RECOVERY-0 Catalog Ownership

## Architecture Before

`PointOfSale.Storefront/src/pages/ProductListPage.tsx` had two mutually exclusive render paths. When `config.pages.product.sections.length > 0`, the page rendered `SectionRenderer` output only. The canonical catalog branch that contained search, filters, sorting, category navigation, product count, grid, pagination, loading state, and empty state did not render.

That made reusable Home/product shelf sections act as page owners on `/products`.

## Architecture After

`/products` has one runtime owner: `ProductListPage`.

The page now renders:

1. `Layout`
2. Optional configured sections in the `top` slot
3. The canonical catalog owned by `ProductListPage`
4. Optional configured sections in the `bottom` slot

Configured sections are still rendered with `SectionRenderer`; they are content only. They no longer replace the catalog.

## Ownership Table

| Runtime responsibility | Owner after Sprint 0 | Source |
| --- | --- | --- |
| `/products` page lifecycle | `ProductListPage` | `PointOfSale.Storefront/src/pages/ProductListPage.tsx` |
| Discovery state | `useProductListDiscovery` inside `ProductListPage` | `PointOfSale.Storefront/src/pages/ProductListPage.tsx` |
| Search | `ProductListPage` | `PointOfSale.Storefront/src/pages/ProductListPage.tsx` |
| Filters | `ProductListPage` | `PointOfSale.Storefront/src/pages/ProductListPage.tsx` |
| Sorting | `ProductListPage` | `PointOfSale.Storefront/src/pages/ProductListPage.tsx` |
| Category navigation | `ProductListPage` | `PointOfSale.Storefront/src/pages/ProductListPage.tsx` |
| Breadcrumbs | `ProductListPage` | `PointOfSale.Storefront/src/pages/ProductListPage.tsx` |
| Product count | `ProductListPage` | `PointOfSale.Storefront/src/pages/ProductListPage.tsx` |
| Grid | `ProductListPage` | `PointOfSale.Storefront/src/pages/ProductListPage.tsx` |
| Pagination | `ProductListPage` | `PointOfSale.Storefront/src/pages/ProductListPage.tsx` |
| Reusable configured sections | `SectionRenderer` as embedded content | `PointOfSale.Storefront/src/pages/ProductListPage.tsx`, `PointOfSale.Storefront/src/components/sections/SectionRenderer.tsx` |
| Product shelf View All href | Runtime presentation model | `PointOfSale.Storefront/src/storefront/runtime/containers/RuntimeSectionContainer.tsx`, `PointOfSale.Storefront/src/storefront/presentation/models.ts` |

## Files Changed

- `PointOfSale.Storefront/src/pages/ProductListPage.tsx`
- `PointOfSale.Storefront/src/pages/productCatalogSections.ts`
- `PointOfSale.Storefront/src/pages/productCatalogSections.test.ts`
- `PointOfSale.Storefront/src/pages/ProductListPage.catalogOwnership.test.ts`
- `PointOfSale.Storefront/src/storefront/presentation/models.ts`
- `PointOfSale.Storefront/src/storefront/runtime/containers/RuntimeSectionContainer.tsx`
- `PointOfSale.Storefront/src/storefront/runtime/sectionContract.certification.test.tsx`
- `PointOfSale.Storefront/src/components/sections/defaults/product-grid.tsx`
- `PointOfSale.Storefront/src/components/sections/defaults/featured-products.tsx`
- `PointOfSale.Storefront/src/components/sections/defaults/related-products.tsx`
- `PointOfSale.Storefront/src/storefront/themes/modern/sections/product-grid.tsx`
- `PointOfSale.Storefront/src/storefront/themes/minimal/sections/product-grid.tsx`
- `PointOfSale.Storefront/src/storefront/themes/cosmetics/sections/product-grid.tsx`
- `PointOfSale.Storefront/src/storefront/themes/electronics/sections/product-grid.tsx`
- `PointOfSale.Web/src/modules/storefront/builder/builderRuntimeContract.certification.test.ts`
- `theme-authority/shopixy-theme-authority.json`

Note: `theme-authority/shopixy-theme-authority.json` already contained unrelated uncommitted theme changes in the worktree. Sprint 0 only aligned product detail `layoutVariant` enum options with source-backed `alizon` and `sharp` templates/profiles so Builder/Product Details certification could pass.

## Regression Coverage

- Products without sections: `productCatalogSections.test.ts`
- Products with sections: `productCatalogSections.test.ts`
- Multiple configured sections: `productCatalogSections.test.ts`
- Canonical catalog always exists: `ProductListPage.catalogOwnership.test.ts`
- Home page section ownership unchanged: `ProductListPage.catalogOwnership.test.ts`
- Product Details section ownership unchanged: `ProductListPage.catalogOwnership.test.ts`
- Builder runtime compatibility: `builderRuntimeContract.certification.test.ts`
- View All suppression: `sectionContract.certification.test.tsx`
- Theme product shelf actions model-owned: `sectionContract.certification.test.tsx`

## Risks

| Risk | Level | Mitigation |
| --- | --- | --- |
| Existing product page sections now render around the catalog instead of replacing it | Medium | This is the intended recovery; sections default to the bottom slot unless a supported placement value requests top. |
| Themes without a product shelf View All action remain without one | Low | No theme behavior was added where it did not already exist. |
| Builder contract enum update could expose Alizon/Sharp product layout options | Low | Source already declares supported templates/profiles for those variants. |
| Product discovery timing issues remain | Medium | Sprint 0 intentionally did not modify discovery, APIs, PostgreSQL, MongoDB, or Elasticsearch. |

## Validation

Passed:

- `PointOfSale.Storefront`: `npm test -- --run src/pages/productCatalogSections.test.ts src/pages/ProductListPage.catalogOwnership.test.ts src/storefront/runtime/sectionContract.certification.test.tsx`
- `PointOfSale.Web`: `npm test -- --run src/modules/storefront/builder/builderRuntimeContract.certification.test.ts`
- `PointOfSale.Storefront`: `npm run build`
- `PointOfSale.Web`: `npm run build`

Build warnings were pre-existing Vite chunking/dynamic-import warnings and stale Browserslist data. No Sprint 0 validation failed.

## Certification

Sprint 0 restores canonical catalog ownership for `/products`.

`ProductListPage` now always owns the canonical catalog controls and discovery render lifecycle. Reusable sections remain supported through `SectionRenderer`, but only as optional page content. Product shelf "View All" navigation is suppressed on `/products` through runtime model ownership instead of theme hardcoding.

No backend APIs, Product Discovery, PostgreSQL, MongoDB, Elasticsearch, routing, Builder removal, or section removal changes were made.
