# CommerceHub Reviews & Ratings Authority Certification Findings

Date: 2026-06-11

Status: production-certifiable for inspected runtime paths after hardening.

## Review Findings

- Review identity is SQL-owned by `ProductReviews` with a live `(ProductId, UserId)` uniqueness boundary.
- Review ownership comes from authenticated CommerceHub customer identity and SQL `ProductReviews.UserId`, not DTOs or routes.
- Runtime hardening prevents public review DTOs from being served from Mongo review documents as final authority.

## Eligibility Findings

- Review creation requires current SQL CommerceHub customer identity and delivered SQL marketplace order-item ownership for the same customer/product.
- Customers cannot review products they never purchased on the inspected create path.
- Deleted order/sub-order/item rows are excluded from eligibility.

## Rating Findings

- Rating values are constrained to 1-5 by entity validation and SQL check constraint.
- Public rating summaries now derive from current non-deleted SQL approved reviews instead of Mongo `ProductReviewSummaries`.
- Search overlay rating fields already recompute from current SQL approved reviews.

## Moderation Findings

- Moderation status is SQL `ProductReviewStatus` authority.
- Auto-moderation only approves pending verified non-suspicious reviews; Super Admin service owns explicit approve/reject.
- No vendor moderation authority was found on inspected CommerceHub review paths.

## Publication Findings

- Public review visibility now requires current SQL approved, non-deleted review state and current marketplace publication presence.
- Pending, rejected, deleted, stale projected, or poisoned review documents cannot become public review read authority on inspected paths.

## Projection Findings

- Mongo review documents, Mongo summary documents, CommerceHub product rating fields, Redis review caches, Elasticsearch rating fields, and review events are derivative only.
- Runtime hardening changed update/reject/delete projection replay to reload current SQL review state, upsert only currently approved reviews, otherwise delete projected review documents, and rebuild aggregates from SQL.

## Catalog/Search Findings

- CommerceHub catalog/search rating display derives from SQL-approved review recomposition through `CommerceHubSearchSqlAuthorityOverlay`.
- Suppressed, rejected, pending, or deleted reviews cannot affect inspected search rating fields.

## Isolation Findings

- Customer mutation APIs resolve customer ownership from the `CustomerJwt` claim and SQL review ownership predicates.
- Public review reads are product-scoped and now SQL-approved only. Tenant-specific product visibility remains bounded by existing CommerceHub catalog/search authority.

## Concurrency Findings

- Duplicate live reviews are blocked by SQL unique index plus duplicate preflight and unique-violation handling.
- Event consumers remain idempotent through `MarketplaceIdempotentConsumer` and projection replay recomposes current SQL review truth.

## Historical Findings

- Review history is preserved in SQL review rows and status/update timestamps.
- Stale projection replay cannot resurrect public review documents after SQL deletion/rejection/pending state on inspected paths.

## Adversarial Findings

- Fake reviews are blocked by delivered SQL purchase eligibility.
- Rating inflation is blocked by one live review per customer/product and SQL-approved aggregate recomposition.
- Rating suppression through vendor moderation was not found; vendors do not own moderation.
- Projection/search poisoning is contained by SQL read recomposition and search overlay recomposition.

## Runtime Hardening Performed

- `MarketplaceProductReviewService.GetApprovedReviewsAsync` now computes public review pages directly from current SQL approved reviews instead of Mongo review documents or Redis caches.
- `MarketplaceProductReviewService.GetRatingSummaryAsync` now builds rating summaries directly from current SQL approved reviews instead of Mongo summary documents or Redis caches.
- Public review/rating reads fail closed when current SQL marketplace publication is absent.
- `MarketplaceProductReviewProjectionService` update/reject/delete handlers now reconcile current SQL review state before Mongo/search mutation instead of trusting event `PreviousStatus`.

## Runtime Files Modified

- `PointOfSale.API/Services/MarketplaceReviews/MarketplaceProductReviewService.cs`
- `PointOfSale.Infrastructure/Services/MarketplaceReviews/MarketplaceProductReviewBackboneServices.cs`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/CommerceHubReviewsRatingsAuthorityCertificationTests.cs`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
- `docs/architecture/commercehub-reviews-ratings-authority-certification-findings.md`

## Certification Answers

- Who owns review authority? PostgreSQL `ProductReviews` plus SQL customer/order/catalog authority.
- Can customers review products they never purchased? No, creation requires delivered SQL order-item ownership.
- Can duplicate reviews occur? Live duplicates are blocked by SQL uniqueness and runtime duplicate handling.
- Can ratings inflate? Not on inspected paths; aggregates derive from approved SQL reviews only.
- Can vendors suppress reviews? No inspected vendor moderation/suppression authority exists.
- Can projections become authority? No; reads and replay now recompose from SQL review truth.
- Can search use suppressed reviews? No; search overlay uses only approved non-deleted SQL reviews.
- Can tenant leakage occur? No inspected M9 tenant leakage path was found; public reads are SQL-approved product-scoped and catalog visibility remains governed by existing CommerceHub catalog/search authority.
- Were runtime code changes required? Yes.
- Is CommerceHub Reviews & Ratings Authority production-certifiable? Yes, for inspected runtime paths.
