# POS-ADMIN-CERTIFICATION-01 PASS 14.1

# CommerceHub Product Publication Lifecycle Certification

## 1. Lifecycle Inventory

The certified lifecycle is:

`SQL product and current variant created -> merchant selects current variant -> live CommerceHub category and attributes validated -> optional merchant price override saved -> SQL listing created or republished -> listing event projected to MongoDB -> current SQL authority overlaid -> Elasticsearch indexed -> public query/detail/homepage candidates recomposed -> cart repriced from SQL -> checkout locks and validates SQL listing/variant/stock/price authority -> purchase created -> merchant unpublish or governance hide -> projection/search delete or suppression -> replay/reindex/auto-healing recompose SQL authority -> conditional governance restore only when current SQL authority remains valid`.

SQL listing rows may remain as administrative history after withdrawal. Derivative state is never publication authority.

## 2. Frontend Findings

`CommerceHubSettings.jsx` exposes variant listing publication, category selection, dynamic SQL-schema-backed attributes, optional merchant price override, mutation version round-trip, update, and unpublish. No tenant bulk publication path or hidden legacy publication control was found. Backend validation remains authoritative.

## 3. Publication Findings

Merchant publish, update, republish, and unpublish are tenant-owned intent mutations. Publish/update require vendor governance, active shipping, live category lineage, current public product lineage, and current public variant lineage. Unpublish only reduces visibility and now remains available after variant deletion or semantic replacement.

## 4. Ownership Findings

Listings remain tenant-bound through trusted tenant context, explicit tenant predicates, `CommerceHubTenantIsolationGuard`, and the SQL unique boundary `{ TenantId, VariantId }`. Recovery uses tenant-scoped listing identity where tenant evidence exists.

## 5. Category Findings

Classification is SQL-authoritative through `CommerceHubCategories` and `CommerceHubCategoryLineageAuthority`. Invalid, removed, cyclic, inactive, or orphaned category lineage blocks publish/update and collapses public visibility during overlay, query, recovery, and reindex.

## 6. Attribute Findings

Attributes are SQL-schema-bound metadata. Merchant input is normalized and rejected when unsupported. Required fields, types, and select options are validated. Projection/search sanitation strips deleted schema keys so historical JSON cannot recreate filter authority.

## 7. Pricing Findings

PASS 13.7 remains authoritative. `CommerceHubPriceAuthority` is the sole final price resolver. Merchant overrides persist with actor metadata and mutation version, but runtime use requires the default-off rollout policy. Cart, checkout, projection, SQL overlay, reindex, and recovery all use the centralized resolver.

## 8. Variant Findings

Variant identity and inventory lineage remain SQL-derived. Publish/update, cart, checkout, overlay, and recovery reject deleted, inactive, detached, or semantically superseded variants. Deleted variants cannot resurrect through projection or search.

## 9. Visibility Findings

Public visibility requires current SQL listing publication, active tenant, eligible vendor governance, published online product, current public variant lineage, live category lineage, current pricing, and current stock/media authority. Governance may suppress visibility while preserving conditional restore evidence.

## 10. Search And Discovery Findings

MongoDB and Elasticsearch are derivative. Search, detail hydration, autocomplete, suggestions, homepage/discovery candidates, reindex, and auto-healing are recomposed or filtered through current SQL CommerceHub visibility authority. Stale documents converge to suppression or deletion.

## 11. Recovery And Replay Findings

Projection consumers use event fences, SQL listing reloads, stale-event checks, and SQL overlays. Reindex performs SQL overlay indexing plus stale Elasticsearch deletion and visible-count verification. Auto-healing uses tenant-scoped identities and SQL overlay recovery. Removed authority cannot be recreated from MongoDB, Elasticsearch, or event payloads.

## 12. Governance Findings

Merchant owns publication intent and merchant price overrides. Marketplace governance owns visibility, suppression, vendor suspension, tenant suppression, emergency price-override disablement, and non-destructive rollback. Governance cannot approve, reject, create, or edit merchant prices.

## 13. Chaos Findings

The reviewed cases fail closed: stale listing replay, cross-tenant publication, suspended listing restore, orphan category or attribute metadata, stale price override replay, deleted or replaced variant replay, stale Mongo projection, stale Elasticsearch document, reindex resurrection, and auto-healing resurrection.

## 14. Monitoring Findings

Operators retain structured listing logs, immutable price-override events, governance audit logs, projection fences, search sync status, stale-delete counts, CommerceHub search health, platform startup validation, auto-healing diagnostics, price override resolver counters, and convergence telemetry. Deployment-specific dashboards and alerts remain operational work.

## 15. Runtime Hardening Performed

`UnpublishFromCommerceHubHandler` now resolves the tenant-owned listing first and permits withdrawal after catalog variant deletion or replacement. It uses explicit tenant predicates with `IgnoreQueryFilters()`, clears stale governance restore intent through `listing.Unpublish()`, emits the convergence event, and uses a fail-closed zero event price when variant authority no longer exists.

## 16. Regression Freeze Added

`CommerceHubPublicationLifecycleCertificationTests` freezes merchant intent, tenant ownership, deleted-variant withdrawal, frontend controls, taxonomy, attributes, centralized pricing, variant lineage, SQL overlay visibility, projection fencing, reindex stale deletion, auto-healing recomposition, cart, checkout, and documentation.

## 17. Architecture Updates

The `.ai-context` authority files record PASS 14.1 as the end-to-end CommerceHub publication lifecycle freeze.

## 18. Remaining Debt

The listing lifecycle remains boolean-based instead of a full moderation state machine. There is no tenant bulk publication workflow, immutable general publication-decision ledger, universal SQL-vs-derivative parity scanner, generated lifecycle registry, full chaos harness, or deployment-specific dashboard and alert pack. Future feed/export, support, ranking, localization, analytics, cache-warmer, and global-script paths remain path-specific certification debt.

## Explicit Answers

1. Can a merchant fully manage a CommerceHub listing? Yes, for the certified single-listing workflow: publish, update, republish, override price, remove override, and unpublish.
2. Who owns publication intent? The tenant merchant.
3. Who owns pricing? The tenant merchant through SQL variant pricing and optional PASS 13.7 CommerceHub overrides.
4. Who owns visibility? Marketplace governance constrained by current SQL lineage.
5. Can suppressed listings resurrect? Not through reviewed paths; restore is conditional on current SQL authority.
6. Can deleted variants resurrect? No.
7. Can deleted categories resurrect? No.
8. Can replay/recovery resurrect removed listings? No.
9. Are any frontend controls inconsistent with backend authority? No reviewed active control is inconsistent. Frontend validation is advisory; backend authority wins.
10. Is the publication lifecycle production-ready? Yes for the reviewed single-listing lifecycle, with the remaining operational and future-path debt above.
