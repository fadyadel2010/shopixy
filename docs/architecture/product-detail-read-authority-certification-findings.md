# PASS SF-PA3.2 Product Detail Read Authority Certification

Date: 2026-06-28

## Certification Result

Product Detail Read Authority is production-certifiable after Storefront presentation fallback hardening.

Backend SQL/service read composition owns Storefront Product Detail, Description, Brand/Category evidence, Specifications and Attributes where exposed, SEO metadata, Media References, Variant Metadata, Related read candidates, DTO admission, projection policy, persistence, failure behavior, and recovery. Frontend owns presentation, formatting, loading, navigation, skeleton UI, variant-option selection state, gallery active-image state, quantity UI state, and rendering only.

## Runtime Findings

- Storefront product detail starts at `StorefrontProductsController.GetProductBySlug` or `GetProductById`, then enters `StorefrontProductService`.
- Redis and MongoDB are candidate/read-model layers only. Cache hits and Mongo reads re-enter `ProductSearchSqlAuthorityOverlay.ApplyAsync` through `ApplyCurrentSqlProductDetailAuthorityAsync` before DTO return.
- Stale slug, hidden/deleted/unpublished product, category lineage loss, affiliate-mode mismatch, Redis replay, Mongo rebuild lag, and search replay fail closed or converge through backend SQL recomposition.
- Frontend `ProductDetailsPage` renders accepted `ProductReadDto` fields through `SectionRenderer` and does not fabricate product description, specification rows, or video media references when backend detail has none.

## Boundary Review

| Boundary | Authority Owner | Recovery Source | Failure Behavior |
| --- | --- | --- | --- |
| Detail | Backend SQL plus product read services | SQL/service recomposition | 404 or unavailable |
| Description | Backend product/detail DTO composition | SQL/Mongo-derived backend read | Withhold empty detail |
| Brand/Category | Backend category/tenant/product lineage | SQL lineage overlay | Suppress or 404 |
| Specifications | Backend DTO composition where exposed | Backend read service | Withhold absent backend evidence |
| Attributes | Backend variant metadata | SQL-backed variant composition | Withhold invalid/missing options |
| Media | Backend media references | SQL-backed media recomposition | Render placeholder only, not media truth |
| SEO | Backend SEO DTO/detail fields | Backend read service | Use admitted title or generic document shell only |
| Composition | Backend read services | Current SQL re-entry | Fail closed |
| Presentation | Frontend React components | Refetch admitted DTO | Loading/error/empty UI |
| Recovery | Backend SQL/service authority | SQL, then projections rebuilt from SQL | Redis/Search/Mongo cannot become authority |

## Detail Pipeline

Product Identity

-> Backend Read Composition (`StorefrontProductService`, SQL overlay)

-> DTO Admission (`ProductReadDto`, `CommerceHubProductDetailDto`)

-> Presentation (`ProductDetailsPage`, gallery/tabs/variant controls)

Detail Authority begins at current backend SQL/product read service re-entry. Composition begins inside backend read services and DTO mapping. Presentation begins only after accepted DTOs reach React.

## Regression Freezes Added

- `StorefrontCatalogPresentationAuthorityCertificationTests.ProductDetailFieldsMustBeBackendComposedAndFrontendPresentationOnly`

## Runtime Hardening Performed

- Removed Storefront section-config specification rows from `product-specs`; only backend description-derived detail can render there.
- Removed Storefront section-config video URL fallback from `product-video`; video media references can render only from admitted product DTO media.
- Added architecture regression coverage proving Storefront product detail fields remain backend-composed and frontend-presented only.

## Exact Runtime Files Modified

- `PointOfSale.Storefront/src/components/sections/defaults/product-specs.tsx`
- `PointOfSale.Storefront/src/components/sections/defaults/product-video.tsx`
- `PointOfSale.Tests/Architecture/StorefrontCatalogPresentationAuthorityCertificationTests.cs`
- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Explicit Certification Answers

- Who owns Product Detail? Backend SQL and backend read services.
- Who owns Description? Backend product/detail composition.
- Who owns Brand? Backend product/category/tenant detail composition; frontend cannot redefine it.
- Who owns Specifications? Backend DTO composition where exposed.
- Who owns Attributes? Backend variant/product metadata composition.
- Who owns Read Composition? Backend read services.
- Can frontend compose Product Detail? No.
- Can stale Detail survive recovery? No, detail must re-enter backend SQL/service authority or fail closed.
- Can Redis become Detail Authority? No.
- Can Search become Detail Authority? No.
- Where does Detail Authority begin? Current backend SQL plus backend read-service re-entry.
- Where does Composition begin? Backend read composition and DTO mapping.
- Where does Presentation begin? After DTO admission in frontend rendering.
- Were runtime changes required? Yes, one frontend fallback was removed and a regression freeze was added.
- Is Product Detail Read Authority production-certifiable? Yes.
