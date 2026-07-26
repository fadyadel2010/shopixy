# POS-ADMIN-CERTIFICATION-01 PASS 11.3 Marketplace Governance Authority

Date: 2026-05-29

## Scope

This pass certifies CommerceHub marketplace governance after PASS 11.1 super-admin identity/session authority and PASS 11.2 global scope/tenant boundary authority.

Marketplace governance is decision authority. Marketplace visibility remains derivative from current SQL listing, product, vendor, category, and tenant authority. Governance may hide, restore, curate, recommend, or rank candidates, but it may not create public visibility when current SQL authority rejects the entity.

## 1. Governance Inventory Findings

- Listing publication is tenant-owned through `AdminCommerceHubController` and `CommerceHubListing`.
- Vendor suspension, ban, and reactivation are platform governance decisions through `UserGovernanceService` and `CommerceHubVendorVisibilityService`.
- Homepage curation is platform or scoped governance through SQL `HomepageSections`, super-admin section management, and homepage projection.
- Discovery, suggestions, autocomplete, popular terms, ranking, reviews, and search analytics are candidate/ranking inputs through CommerceHub query/search services.
- Promotions and featured placements are placement/ranking metadata only; they do not replace listing/product/vendor/category/tenant authority.
- Enforcement owners are the SQL authority overlays, category lineage helpers, public vendor visibility service, homepage sanitation, query recomposition, reindex/recovery overlay, replay fences, and platform/tenant middleware.

No hidden governance owner was found that intentionally replaces SQL listing/product/vendor/category/tenant authority. Older marketplace certification debt remains path-specific for future feed/export/localization/support/ranking expansion.

## 2. Listing Approval Findings

Tenant listing publish/update now requires current vendor governance eligibility, active shipping, live CommerceHub category lineage, current public variant lineage, and current product publication/online visibility before approval/update succeeds.

Approval does not become visibility authority. Public search/detail/list/homepage reads still pass through `CommerceHubSearchSqlAuthorityOverlay`, public vendor suppression, and category lineage checks.

## 3. Rejection And Suspension Findings

Tenant unpublish removes listing publication intent and emits listing-unpublished convergence events. Vendor suspension and ban hide currently published listings, preserve only prior publication intent through `RestorePublicationOnActivate`, invalidate governance visibility cache, enqueue search verification, and write governance audit metadata.

Rejected, hidden, or suspended content remains governed because reads filter current SQL authority and vendor suppression. Stale search/Mongo/homepage state converges to delete, scrub, or not-found.

## 4. Homepage Governance Findings

Homepage sections are governance records, not visibility authority. Super-admin writes validate curated listing/category references against current SQL listing/product/variant/category/tenant authority and public vendor visibility. Public reads and projections sanitize references again before returning or storing homepage read models.

## 5. Recommendation Findings

No independent personalized recommendation authority was found. Recommendation-like shelves, curated products, popular terms, suggestions, and autocomplete are candidate surfaces. Returned products and terms must survive current SQL overlay and vendor suppression.

## 6. Discovery Findings

CommerceHub browse/search, suggestions, autocomplete, popular queries, zero-result assistance, category facets, and search analytics use Elasticsearch, MongoDB, query logs, and popularity metadata as discovery evidence only. `CommerceHubQueryService` recomposes visible listing items and filters terms/categories through current SQL authority.

## 7. Vendor Governance Findings

Vendor governance is platform-owned through current SQL super-admin authority and SQL `UserGovernanceProfiles`. Public vendor suppression combines current governance hidden states with financial new-order suspension. Vendor reactivation no longer blindly restores prior listing publication intent: it restores only when current SQL tenant, product publication, variant lineage, and CommerceHub category lineage still prove visibility; otherwise it clears stale restore intent.

## 8. Promotion And Featured Content Findings

Promoted, featured, curated, popular, and campaign-style placement states are ranking or placement inputs only. They cannot make deleted listings, unpublished products, invalid categories, inactive tenants, hidden vendors, financially suspended vendors, or invalid variants public on reviewed surfaces.

## 9. Replay And Recovery Findings

Homepage projection uses replay fencing before side effects. Marketplace product projections sanitize references, delete stale read models, and declare homepage/listing SQL authority. CommerceHub reindex and auto-healing use SQL overlays before search mutation and tenant-scoped recovery identity where tenant evidence exists. Alias promotion remains SQL-visible-count verified.

## 10. Auditability Findings

Vendor governance actions write `UserGovernanceAuditLogs` with actor, reason, before/after state, visibility snapshot, and cache invalidation metadata. Super-admin mutation routes are covered by request-level operational audit after current SQL platform actor recomposition. Search/replay/recovery drift is observable through health checks, logs, auto-healing, and convergence telemetry.

## 11. Chaos Findings

The reviewed chaos cases collapse to current SQL authority:

- approval during reindex: listing publish/update requires current SQL authority, and reindex overlays before indexing.
- suspension during replay: vendor hidden state and listing unpublish events cause read suppression and search verification.
- vendor disablement during recovery: public vendor suppression and tenant-scoped auto-healing prevent stale recovery from serving the vendor.
- homepage curation during rebuild: write-time validation, read-time sanitation, and projection sanitation scrub stale references.
- recommendation rebuild during governance changes: suggestions/popular terms are revalidated against live SQL-overlaid listings.

## Runtime Hardening Performed

- Tenant-admin CommerceHub publish/update now validates live CommerceHub category lineage and public product/variant publication authority before listing approval/update.
- Vendor reactivation restore now requires active tenant, published/online product, current public variant, and live CommerceHub category lineage before restoring `IsPublished`.
- Stale governance restore intent is cleared when current SQL authority no longer proves publication eligibility.
- Regression-freeze coverage was added in `MarketplaceGovernanceAuthorityCertificationTests`, with runtime coverage added for stale product publication on vendor reactivation.

## Monitoring Improvements

No new observability backend was introduced. This pass relies on existing governance audit logs, super-admin operational audit middleware, cache invalidation versioning, auto-healing enqueue diagnostics, CommerceHub search sync health, and bounded convergence telemetry.

## Governance Updates

PASS 11.3 defines marketplace governance as explicit decision authority only. Visibility remains current SQL listing/product/vendor/category/tenant authority plus derivative convergence controls. Future marketplace governance, curation, ranking, promotion, recovery, feed/export, support, cache-warmer, or global-script work must update this certification boundary when it touches public visibility.

## Honest Remaining Debt

- No enterprise moderation workflow, SIEM-grade immutable audit store, generated marketplace governance registry, universal SQL-vs-derivative parity scanner, personalized recommendation framework, or adaptive rebuild SLO was added.
- Review export, localization, feed/export, cache warmer, support impersonation, ranking engines, analytics/reporting, and future global runbooks remain path-specific certification debt.
- Existing derivative stores can still lag. The certified guarantee is SQL-authoritative convergence and fail-closed filtering on reviewed paths, not strong consistency.
