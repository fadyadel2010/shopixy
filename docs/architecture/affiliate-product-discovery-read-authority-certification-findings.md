# PASS AF-PA4.1 Affiliate Product Discovery and Read Authority Certification

Certification date: 2026-06-25

Status: Production-certifiable after backend read-admission hardening.

## Scope

This pass certifies only Affiliate Product Discovery and Eligible Product Read Authority: browse products, search, categories, filters, pagination, sorting, eligible product APIs, projections, read models, normalization, lifecycle loss, cache/failure behavior, and adversarial replay. Product selection, promotion links, and attribution were intentionally out of scope.

## Findings

- Discovery is backend-owned. The frontend submits search, category, filter, sort, and pagination candidates only.
- Product admission occurs after derivative index/projection candidate lookup and before response projection, inside backend SQL recomposition.
- Authenticated legacy discovery uses Elasticsearch candidates, then recomposes every candidate through current SQL product lifecycle, storefront visibility, pricing, stock, active tenant, active affiliate policy, and active affiliate category authority.
- Enhanced portal discovery uses Mongo affiliate-intelligence candidates, then recomposes every list/detail/category-count candidate through the same current SQL product, pricing, stock, tenant, policy, and active category authority.
- Public affiliate product discovery already used active category and SQL product recomposition; this pass aligned authenticated discovery with that boundary.
- Search, category filters, keyword filters, sorting, and pagination are backend-side candidate selectors only. They cannot admit a product without the backend recomposition boundary.
- Frontend state, URL params, React state, browser history, local storage, failed reads, and stale responses cannot fabricate discoverable products; failed reads render empty/error presentation.
- Projection and Elasticsearch/Mongo documents remain derivative. Stale projections may nominate candidates, but current SQL recomposition suppresses hidden, disabled, archived, deleted, unpublished, unpriced, out-of-stock, inactive-category, inactive-tenant, or disabled-policy products.

## Runtime Hardening Performed

- `AffiliateProductDiscoveryService` now joins `AffiliateProductPolicies` to active, non-deleted `AffiliateCategories` during authenticated discovery SQL recomposition.
- `AffiliatePortalController.RecomposeAffiliateIntelligenceDocumentBySqlAuthorityAsync` now requires active, non-deleted affiliate category authority before list/detail/category-count discovery responses can include a product.
- `AffiliateDiscoveryController` commission enrichment now uses the same active category policy evidence, preventing raw enabled policy rows from producing commission presentation after category loss.

## Regression Freeze

- `PointOfSale.Tests/Affiliate/AffiliateProductEligibilityLifecycleCertificationTests.cs` now freezes authenticated discovery, enhanced portal discovery, and legacy commission enrichment active-category read admission.

## Explicit Answers

- Who owns Product Discovery? Backend SQL authority plus backend read services own discovery admission.
- Can frontend fabricate discoverable products? No.
- Can search reveal ineligible products? No; search can only nominate candidates that backend SQL recomposition admits.
- Can stale products survive eligibility removal? Not as accepted discovery results after current backend read recomposition.
- Can hidden products remain visible? No; hidden/unpublished/inactive/deleted/unpriced/out-of-stock products fail backend recomposition.
- Can projection become authority? No; projection is candidate evidence only.
- Where does Product Admission occur? In backend SQL recomposition after Elasticsearch/Mongo candidate lookup and before API response projection.
- Were runtime changes required? Yes.
- Is Affiliate Product Discovery production-certifiable? Yes, after this hardening.
