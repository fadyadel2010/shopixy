# PASS SF-PA3.4 Product Pricing Presentation Authority Certification

Date: 2026-06-28

## Certification Result

PointOfSale.Storefront Product Pricing Presentation is production-certifiable after backend pricing-presentation DTO hardening.

Backend Storefront product read services own Current Price, Variant Price, Compare-at Price, Discounted Price, Discount Badge, Promotion Result, Pricing Composition, DTO admission, projection policy, persistence, failure behavior, and recovery. Storefront frontend owns formatting, currency display, loading, selection candidates, and rendering only.

## Runtime Findings

- Product detail reads enter `StorefrontProductsController.GetProductBySlug` or `GetProductById`, then `StorefrontProductService`.
- Variant pricing resolution enters `StorefrontProductsController.ResolveProductVariant`, then `StorefrontProductService.ResolveProductVariantAsync`, which re-enters current product detail authority before returning the resolved variant.
- Redis and MongoDB are candidate/read-model layers. Cached product detail/list DTOs re-enter `ProductSearchSqlAuthorityOverlay.ApplyAsync` before response admission.
- Storefront search/discovery product cards recompose Elasticsearch candidates through current SQL authority before returning visible product items.
- Prior gap: Storefront React product hero and product card components calculated discounted prices and derived percentage badges from discount type/value fields.
- Hardening: Storefront product and variant DTOs now include backend-composed `pricing` evidence with `currentPrice`, `compareAtPrice`, `discountedPrice`, and `discountBadge`. Storefront product cards and Product Hero render those values directly and no longer calculate discounts.

## Pricing Pipeline

Resolved Variant

-> Backend Pricing Authority (`ProductSearchSqlAuthorityOverlay`, Storefront read services, promotion evidence)

-> Pricing DTO (`ProductPricingPresentationDto` / `ProductDiscoveryPricingDto`)

-> DTO Admission (Storefront service/controller response after current SQL re-entry)

-> Storefront Presentation (`ProductHero`, product cards, formatting only)

Pricing Authority begins at current backend SQL/service re-entry. Pricing Composition begins in backend Storefront DTO mapping. Storefront presentation begins only after admitted pricing DTOs reach React.

## Boundary Review

| Boundary | Authority Owner | Recovery Source | Failure Behaviour |
| --- | --- | --- | --- |
| Pricing | Backend SQL/product/variant/promotion authority | SQL/service recomposition | 404/409/503 or withheld pricing |
| Currency | Backend StoreConfig/Tenant currency read authority | Store config reread | Formatting fails closed when missing |
| Discount | Backend promotion evidence and pricing DTO composition | SQL/projection recomposition | No badge/discount if not admitted |
| Composition | Storefront backend services and DTO mappers | Current SQL overlay | Frontend cannot compose pricing |
| Presentation | Storefront React components | Backend refetch | Loading/error/withhold only |
| Recovery | Backend SQL plus guarded projection rebuild | SQL, then derivative projections | Redis/Mongo/Search/browser state cannot become authority |
| Projection | Mongo/Redis/Search copies after backend authority | SQL rebuild/re-entry | Stale projection suppressed or recomposed |

## Runtime Hardening Performed

- Added backend-composed pricing presentation DTOs for Storefront product detail, variant resolution, list/card DTOs, and discovery DTOs.
- Populated pricing presentation after Storefront current SQL/product/promotion authority re-entry.
- Removed Product Detail React discount calculation from `useVariantSelector` and `product-hero`.
- Removed Storefront product-card discount calculation and discount-badge derivation.
- Added architecture regression freeze for product pricing presentation authority.

## Explicit Answers

- Did this review only PointOfSale.Storefront runtime? Yes; backend review was limited to Storefront-consumed product pricing read APIs/services.
- Who owns Current Price? Backend Storefront product pricing authority.
- Who owns Variant Price? Backend Storefront variant/product SQL authority and resolver.
- Who owns Compare Price? Backend pricing presentation DTO composition.
- Who owns Currency? Backend StoreConfig/Tenant currency authority; frontend only formats/displays it.
- Who owns Pricing Composition? Backend Storefront services and DTO mappers.
- Can Storefront frontend calculate prices? No.
- Can frontend derive discounts? No.
- Can stale pricing survive recovery? No; it must re-enter backend authority or fail closed.
- Can Redis become Pricing Authority? No.
- Where does Pricing Authority begin? Current backend SQL/service re-entry.
- Where does Pricing Composition begin? Backend Storefront DTO mapping after authority re-entry.
- Where does Storefront presentation begin? React rendering after admitted DTOs.
- Were runtime changes required? Yes.
- Is PointOfSale.Storefront Product Pricing Presentation production-certifiable? Yes.
