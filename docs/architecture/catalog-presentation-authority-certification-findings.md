# PASS SF-PA2 Catalog Presentation Authority Certification Findings

Date: 2026-06-28

## Certification Result

Catalog Presentation Authority is production-certifiable after runtime cache-admission hardening.

Backend SQL product, category, tenant, variant, pricing, stock, media, promotion, and tombstone authority exclusively owns catalog visibility and presentation-admissible catalog state. MongoDB product/category documents, Elasticsearch search hits, Redis cached DTOs, browser state, React state, URL filters, and frontend components are derivative candidates only.

## Runtime Findings

- Catalog Authority begins at current backend SQL plus tenant/category/product service re-entry: SQL `Products`, `Categories`, tenant lineage, `ProductSearchSqlAuthorityOverlay`, `CategoryLineageAuthority`, `StorefrontProductService`, `StorefrontHomeService`, `StorefrontCategoryService`, `ProductSearchService`, `ProductDiscoveryService`, and `StorefrontSearchController`.
- Visibility Authority is backend-owned: public product presentation requires active tenant lineage, non-deleted SQL product, `IsActive`, `IsOnlineVisible`, `PublishStatus.Published`, live category lineage, public/current variants when variants exist, current price, and current stock/backorder calculation.
- Admission Authority is backend-owned: product detail/list, homepage cards, curated products, discovery, search, header suggest, and category navigation are admitted only after current backend recomposition.
- Projection remains derivative: MongoDB, Elasticsearch, and Redis can provide candidate IDs or stale DTO envelopes, but current SQL overlay and category lineage decide whether anything reaches customer presentation.
- Search remains derivative: `/api/storefront/search`, `/api/storefront/search/suggest`, and product discovery read Elasticsearch candidates and then recompose or filter against current SQL authority before returning DTOs.
- Category presentation is SQL-lineage-owned: storefront category tree and category-by-slug reads are built from current SQL category lineage, and orphan/deleted/hidden parent chains fail closed.
- Recovery converges to backend catalog: Redis flush, Mongo rebuild, search rebuild, and server restart re-enter SQL authority or fail closed with unavailable/empty presentation rather than restoring projection truth.

## Runtime Hardening Performed

- Closed stale Redis catalog survival for product detail reads by recomposing cached `ProductReadDto` hits through `ProductSearchSqlAuthorityOverlay` before returning.
- Closed stale Redis catalog survival for the legacy product-list endpoint by recomposing cached `ProductListItemDto` hits through current SQL authority before returning.
- Slug detail reads now suppress cached products whose current backend slug no longer matches the requested slug.
- Affiliate-mode product cache hits now revalidate current backend affiliate eligibility before presentation.

## Files Modified

- `PointOfSale.API/Services/Storefront/StorefrontProductService.cs`
- `PointOfSale.Tests/Architecture/StorefrontCatalogPresentationAuthorityCertificationTests.cs`
- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
- `docs/architecture/catalog-presentation-authority-certification-findings.md`

## Boundary Review

| Boundary | Authority Owner | Recovery Source | Failure Behaviour |
| --- | --- | --- | --- |
| Catalog Boundary | Backend SQL product/category/tenant authority | SQL + guarded projection rebuild | Withhold/404/503 |
| Visibility Boundary | SQL product state, publication, active/hidden/deleted, live category lineage | SQL overlay | Hidden/deleted/unpublished products suppressed |
| Projection Boundary | Backend projection writers after SQL authority | SQL recomposition | Projection cannot widen visibility |
| Search Boundary | Elasticsearch candidates plus SQL overlay admission | SQL overlay and reindex | Search failure returns unavailable; stale hits suppressed |
| Presentation Boundary | Backend DTO admission, frontend rendering only | Backend refetch | Frontend cannot fabricate catalog items |
| Recovery Boundary | SQL source of truth plus projection rebuild | SQL | Redis/Mongo/search loss converges or fails closed |

## Catalog Pipeline

SQL `Products` / `Categories` / `Tenants`

↓

Catalog Authority: current backend SQL service/overlay admission

↓

Projection: MongoDB product/category read models, Elasticsearch indexes, Redis DTO caches

↓

Search: Elasticsearch candidate retrieval only

↓

DTO Admission: `ProductSearchSqlAuthorityOverlay`, `CategoryLineageAuthority`, storefront service recomposition

↓

Presentation: Storefront React pages/cards/search/category UI

## Cross-System Findings

Storefront, Search, Commerce AI, Affiliate, CommerceHub, Analytics, SEO, and Recommendations must consume catalog as backend-admitted current evidence. Projection consumers may copy and rank catalog data, but they cannot become authority or expose stale hidden/deleted products after SQL authority changes.

## Explicit Answers

- Who owns Catalog Authority? Backend SQL and backend catalog/read services.
- Who owns Product Visibility? Backend SQL product state plus live category and tenant lineage.
- Who owns Published State? Backend SQL `Product.PublishStatus`.
- Can frontend expose hidden products? No.
- Can Search expose deleted products? No; search hits are recomposed against SQL and deleted products are suppressed.
- Can Redis become authority? No.
- Can Mongo become authority? No.
- Can stale catalog survive recovery? No; it must re-enter SQL authority, converge, or fail closed.
- Where does Catalog Authority begin? Current backend SQL product/category/tenant authority at service/overlay re-entry.
- Where does Projection begin? After SQL authority commits and projection writers copy backend truth to Mongo/Search/Redis.
- Where does Presentation begin? After DTO admission returns backend-accepted catalog evidence to the frontend.
- Were runtime changes required? Yes.
- Is Catalog Presentation production-certifiable? Yes, after the hardening above.
