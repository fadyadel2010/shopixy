# PASS M6 CommerceHub Catalog Authority Certification Findings

Scope: CommerceHub listings, catalog publication, moderation/governance, visibility, search eligibility, pricing visibility, inventory visibility, vendor-product linkage, and catalog lineage.

Status: CommerceHub Catalog Authority is production-certifiable for inspected runtime paths after bulk publication hardening.

## Listing Findings

PostgreSQL `CommerceHubListings` owns listing identity, publication intent, category linkage, vendor ownership, product/variant linkage, price-override metadata, governance restore intent, and listing lineage. Listing identity is server-generated and tenant-bound. SQL uniqueness on `(TenantId, VariantId)` prevents duplicate live listing identity for a vendor variant.

## Vendor-Linkage Findings

Vendor-product linkage is current SQL tenant/product/variant/listing lineage. Single publish/update and bulk visibility-increasing paths require trusted tenant context, current vendor governance, current same-tenant product and variant authority, public variant lineage, live category authority, EGP currency policy, and active shipping. Vendors cannot claim foreign variants or products through DTOs, listing IDs, route IDs, projections, or search documents.

## Publication Findings

Publication is SQL `CommerceHubListings.IsPublished`, but public visibility requires the full SQL overlay: published listing, active tenant, marketplace-eligible vendor, published/online product, current variant lineage, live category, pricing authority, and inventory authority. Runtime hardening aligned merchant bulk `publish`, `remove_override`, and category update behavior with the single-listing gates so bulk replay cannot set publication truth from stale listing state.

## Moderation Findings

Marketplace moderation/governance is SuperAdmin/vendor-governance SQL authority. Suppression uses `HideByGovernance`, preserving merchant restore intent separately. Restore requires current SQL vendor, product, variant, and category authority; failed restore clears stale restore intent and records evidence. Merchant publish/update remains subordinate to vendor eligibility.

## Visibility Findings

Customer visibility is derivative. `CommerceHubSearchSqlAuthorityOverlay`, `CommerceHubQueryService`, homepage/vendor visibility filters, projection consumers, reindex, and auto-healing recompose from SQL before serving, indexing, restoring, or retaining visibility. Suspended, banned, unapproved, inactive, deleted, unpublished, invalid-category, stale-variant, or non-EGP listings fail closed.

## Pricing/Inventory Findings

Displayed CommerceHub pricing derives from `CommerceHubPriceAuthority` over current SQL variant price and disabled/controlled listing override metadata. Inventory visibility derives from current SQL stock owner lineage through `WhereCurrentInventoryStockOwner`; stale Mongo/search/cache stock cannot create availability.

## Search Findings

Elasticsearch and MongoDB are derivative only. Search/list/detail/suggestion/autocomplete reads call the SQL overlay before returning products. Projection and reindex paths call the same overlay and delete stale search documents absent from SQL-visible authority. Search replay cannot resurrect a removed listing.

## Isolation Findings

Tenant and vendor isolation are enforced by trusted tenant context on merchant mutation, tenant predicates on listing lookup, SQL tenant/product/variant joins, vendor authority checks, and tenant-scoped projection/search identities. Foreign listing IDs fail not-found or fail overlay authority.

## Concurrency Findings

Duplicate listing creation collapses through SQL uniqueness on `(TenantId, VariantId)`. Projection/search replay collapses through `ProjectionConsumerEventFence`, freshness markers, SQL reloads, and stale document deletion. Price-override mutation versioning rejects stale listing-update intent.

## Historical Findings

Historical listing rows may remain administrative lineage after unpublish or governance suppression, but derivative public state is rebuilt from current SQL. Stale restore intent is cleared when current authority no longer permits visibility.

## Adversarial Findings

Listing spoofing, replay, resurrection, vendor product reassignment, approval bypass, moderation bypass, suspension bypass, search poisoning, projection poisoning, DTO tampering, route tampering, cross-tenant access, and cross-vendor mutation are blocked or fail closed on inspected paths by SQL authority, tenant context, governance checks, current product/variant/category lineage, overlay recomposition, projection fences, and reindex stale deletion.

## Runtime Hardening Performed

- Added vendor governance and active-shipping gates to merchant bulk visibility-increasing listing operations.
- Added current live category validation for bulk publish/remove-override/update operations.
- Added same-product and semantic-current variant checks to bulk publication authority.
- Preserved unpublish as a visibility-reducing operation available for cleanup.

## Exact Runtime Files Modified

- `PointOfSale.API/Controllers/AdminCommerceHubController.cs`
- `PointOfSale.Tests/Admin/AdminCommerceHubScopeTests.cs`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/CommerceHubCatalogAuthorityCertificationTests.cs`
- Updated `PointOfSale.Tests/Architecture/CommerceHubBulkOperationsCertificationTests.cs`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`

## Explicit Answers

- Who owns catalog authority? PostgreSQL `CommerceHubListings` plus current SQL product, variant, category, tenant, vendor-governance, pricing, and inventory authority.
- Can listings be replayed? No. Replay converges through SQL uniqueness, current SQL reloads, projection fences, and stale search deletion.
- Can vendors claim foreign products? No.
- Can publication bypass approval? No after bulk hardening; publication requires current vendor marketplace eligibility.
- Can suspended vendors remain visible? No inspected public path permits it.
- Can pricing diverge from product truth? No inspected public path; pricing is resolved by `CommerceHubPriceAuthority` from current SQL authority.
- Can inventory visibility diverge from inventory truth? No inspected public path; inventory is recomposed from current SQL stock-owner lineage.
- Can search become authority? No.
- Can tenant leakage occur? No inspected catalog path permits it.
- Were runtime code changes required? Yes, for bulk publication authority hardening.
- Is CommerceHub Catalog Authority production-certifiable? Yes, for inspected runtime paths.

## Remaining Debt

Operational production proof remains outside the repository: deployment migration verification, SQL/Mongo/Elasticsearch parity monitoring, alert packs, and PostgreSQL-backed concurrency/chaos runs for high-volume publication, governance restore, projection replay, and reindex races.
