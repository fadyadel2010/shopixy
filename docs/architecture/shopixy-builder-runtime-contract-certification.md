# Shopixy Builder Runtime Contract Certification

Date: 2026-07-28
Milestone: R6.5 Builder Runtime Contract Certification

## 1. Builder Runtime Contract Summary

Builder and Storefront Runtime now share the section settings contract through
`theme-authority/shopixy-theme-authority.json`.

Canonical owner:

- Section inventory: `sectionContracts`
- Builder defaults and schema: derived by `PointOfSale.Web/src/modules/storefront/builder/sectionContractRegistry.js`
- Backend allowed section types and setting keys: derived by `PointOfSale.Core/Constants/StorefrontSectionContracts.cs`
- Runtime parser and strict validation: `PointOfSale.Storefront/src/storefront/runtime/sectionContract.ts`

## 2. Contract Inventory

| Section | Contract properties |
| --- | --- |
| `hero-banner` | `title`, `subtitle`, `ctaText`, `ctaLink`, `backgroundImage`, `backgroundColor`, `overlayOpacity`, `textColor`, `alignment`, `height` |
| `product-grid` | `title`, `source`, `productIds`, `columns`, `rows`, `showPrice`, `showRating`, `showAddToCart` |
| `category-grid` | `title`, `source`, `columns`, `categoryIds`, `showProductCount`, `cardStyle` |
| `subcategory-grid` | `title`, `columns`, `showProductCount`, `cardStyle` |
| `text-block` | `heading`, `content`, `alignment`, `backgroundColor`, `textColor`, `maxWidth` |
| `image-block` | `images`, `layout`, `aspectRatio`, `fullWidth` |
| `video-block` | `videoUrl`, `autoplay`, `showControls` |
| `newsletter-signup` | `heading`, `subheading`, `buttonText`, `backgroundColor`, `textColor`, `showName` |
| `testimonials` | `heading`, `testimonials`, `layout`, `showRating`, `autoplay` |
| `featured-products` | `title`, `source`, `productIds`, `limit`, `showPrice`, `showRating`, `displayStyle`, `showBadge`, `badgeText` |
| `product-hero` | `showDescription`, `showVariantSelector`, `showAddToCart`, `showSku`, `showStockStatus` |
| `product-gallery` | `title`, `showThumbnails`, `maxImages` |
| `product-specs` | `title`, `useDescription`, `items` |
| `product-video` | `title`, `videoUrl`, `autoplay`, `showControls` |
| `customer-reviews` | `title`, `reviews` |
| `related-products` | `title`, `limit`, `showPrice`, `showRating` |

Each field in the manifest declares: builder key, field type, default value,
enum bounds where applicable, renderer consumer, and visual effect.

## 3. Builder Runtime Contract Matrix

| Contract step | Implementation |
| --- | --- |
| Builder field | `sectionContractRegistry.js` maps manifest fields to `SchemaSettingsForm` fields |
| Schema definition | `theme-authority/shopixy-theme-authority.json.sectionContracts[*].fields` |
| Serialized JSON | Builder section `settings` object uses the manifest key verbatim |
| Backend validation | `AdminStoreConfigService` uses `StorefrontSectionContracts` for supported section types, keys, and enums |
| Runtime parser | `materializeSectionSettings` and `validateSectionSettingsStrict` |
| Runtime type | `SectionContract`, `SectionContractField`, and presentation models |
| Presentation model | `RuntimeSectionContainer` maps section settings to `ProductGridPresentationModel`, `CategoryGridPresentationModel`, and `ProductHeroPresentationModel` |
| Renderer consumer | Manifest `rendererConsumer`; tests assert non-empty ownership for every property |
| DOM | Renderer tests certify display-style class changes |

## 4. Property Trace Report

Canonical trace for every property:

Builder schema from manifest -> Builder section `settings` JSON -> Backend
normalization from manifest -> Storefront DTO settings -> Runtime strict parser
from manifest -> Presentation model or renderer props -> Section renderer -> DOM.

Any unknown, missing, or enum-mismatched field now fails certification tests.

## 5. Contract Drift Report

Closed:

- `featured-products.displayStyle` was builder/runtime-visible but omitted from backend persistence allow-list. It is now present in the canonical manifest and backend contract.
- `product-grid.productSource` was a legacy builder-only meaning with no runtime consumer. Builder/demo defaults now use canonical `source`.
- Backend unknown fields previously stripped silently. They now fail closed.
- Backend enum mismatches previously fell back silently. They now fail closed.

Observation:

- Some older component-local `settingsSchema` exports remain in builder section component files. The active registry no longer consumes them; it overwrites defaults/schema from the canonical manifest at registration time. They should be removed in a cleanup pass to eliminate dead duplicate text surfaces.

## 6. Display Style Certification

`featured-products.displayStyle` trace:

Builder field: manifest field `displayStyle`.
Persistence: backend normalization preserves `displayStyle`.
Serialization: JSON key remains `displayStyle`.
Runtime parsing: strict parser requires `displayStyle`.
Presentation model: `RuntimeSectionContainer` maps it to `layout`.
Renderer: `FeaturedProducts` consumes `displayStyle`.
Visual output: `grid` renders grid classes; `carousel` renders horizontal overflow classes.

No alias is accepted. Missing `displayStyle` fails. Invalid enum values fail.

## 7. Schema Ownership Review

Canonical owner is `theme-authority/shopixy-theme-authority.json`.

Builder UI, backend validation, runtime parsing, and certification tests now
derive from that manifest. New section settings must be added there first.

## 8. Tests Added

- `PointOfSale.Tests/Architecture/StoreBuilderRuntimeContractCertificationTests.cs`
- `PointOfSale.Web/src/modules/storefront/builder/builderRuntimeContract.certification.test.ts`
- `PointOfSale.Storefront/src/storefront/runtime/sectionContract.certification.test.tsx`

## 9. Files Changed

- `theme-authority/shopixy-theme-authority.json`
- `PointOfSale.Core/Constants/StorefrontSectionContracts.cs`
- `PointOfSale.API/Services/Admin/AdminStoreConfigService.cs`
- `PointOfSale.Web/src/modules/storefront/builder/sectionContractRegistry.js`
- `PointOfSale.Web/src/modules/storefront/builder/sectionRegistry.js`
- `PointOfSale.Web/src/modules/storefront/components/builder/SchemaSettingsForm.jsx`
- `PointOfSale.Web/src/modules/storefront/components/sections/ProductGrid.jsx`
- `PointOfSale.Web/src/modules/storefront/config/demoSections.js`
- `PointOfSale.Storefront/src/storefront/runtime/sectionContract.ts`
- `PointOfSale.Storefront/src/components/sections/homepageSections.ts`
- `PointOfSale.Storefront/src/storefront/runtime/containers/RuntimeSectionContainer.tsx`
- Certification tests listed above.

## 10. Remaining Contract Risks

- Existing persisted tenant pages with partial/legacy settings may now fail strict validation until migrated to the canonical settings shape.
- Dead component-local schema exports should be deleted or rewritten to import from the canonical contract.

## 11. Final Certification

PASS WITH OBSERVATIONS.

Justification: canonical ownership, backend validation, runtime parsing, and CI
tests now detect drift. Display Style is certified end-to-end. Observations are
limited to legacy persisted data migration risk and dead duplicate schema text
that is no longer consumed by the active builder registry.
