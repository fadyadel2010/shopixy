# POS-ADMIN-CERTIFICATION-01 PASS 12.3 Marketplace Attribute & Filter Governance

## 1. Attribute Inventory Findings

CommerceHub marketplace attributes are SQL-owned classification metadata. `CommerceHubCategoryAttributes` defines category-bound attribute names, types, required flags, filterability, searchability, options, and display order. `CommerceHubListings.AttributesJson` stores normalized listing values, `AttributeSchemaSnapshotJson` records the schema observed at publish/update, Mongo `CommerceHubProductDocument.Attributes` is a projection, and Elasticsearch `attributes.*` is derivative search metadata.

## 2. Attribute Binding Findings

Attribute validation is bound to live CommerceHub category lineage through `CommerceHubCategoryLineageAuthority`. Publish/update handlers require a live category before listing mutation and call `CommerceHubAttributeService.ValidateAndNormalizeAsync` before storing values. Facet definitions now load only from live SQL category ids.

## 3. Attribute Poisoning Findings

The reviewed poisoning gap was unknown merchant-provided attributes being ignored instead of rejected. That allowed payloads with unsupported keys to pass validation silently. Runtime hardening now rejects unsupported keys as validation errors, preventing arbitrary attribute injection at the publish/update boundary.

## 4. Schema Evolution Findings

Attribute schema snapshots remain historical evidence, not indexing authority. Deleted or renamed attributes can remain in stored listing JSON, so derivative projection/reindex paths must not treat those stored keys as current filter authority. The SQL search overlay now strips stored attributes that are not present in the current category attribute schema before Mongo/search mutation.

## 5. Search And Facet Findings

Search filters are parsed from query parameters and JSON using normalized keys, but final acceptance is now bounded by current SQL facet definitions. Unsupported filter keys fail closed to an empty search result instead of querying arbitrary `attributes.*` fields. Public response facets continue to be recomposed from SQL-visible returned items.

## 6. Indexing Findings

Elasticsearch and Mongo attribute projections remain derivative. `CommerceHubSearchSqlAuthorityOverlay` recomposes current listing/product/variant/category authority and now normalizes `AttributesJson` against current SQL category attributes before assigning `document.AttributesJson` and `document.Attributes`.

## 7. Replay And Recovery Findings

Projection replay, auto-healing, and reindex paths already pass through SQL overlays before preserving public CommerceHub search visibility. With current-schema attribute sanitation in the overlay, replay cannot resurrect deleted or unsupported attributes into Mongo/Elasticsearch even when old listing rows still contain historical keys.

## 8. Merchant Input Findings

POS Admin sends `AttributesJson` as merchant intent. Backend validation owns acceptance. Required fields, type checks, select options, key normalization, category lineage, and unknown-key rejection are enforced server-side before listing mutation.

## 9. Auditability Findings

Publish/update handlers log listing attribute requests and persisted normalized attributes. Unknown attribute submissions now emit a warning and fail validation. Search requests with unsupported attribute filters emit a bounded warning before returning no matches.

## 10. Chaos Findings

Attribute deletion during reindex converges by stripping the deleted key from derived documents. Invalid category lineage still fails attribute validation. Unsupported filter replay cannot broaden search results because unsupported keys fail closed before Elasticsearch or Mongo filter execution.

## 11. Runtime Hardening Performed

- `CommerceHubAttributeService` now rejects unknown submitted attributes.
- `CommerceHubQueryService` now loads facet definitions only from live SQL category lineage.
- `CommerceHubQueryService` now fails closed for unsupported attribute filters.
- `CommerceHubSearchSqlAuthorityOverlay` now strips stored attributes absent from current SQL category schema before projection/indexing.
- `MarketplaceAttributeFilterGovernanceCertificationTests` freezes the reviewed boundaries.

## 12. Monitoring Improvements

No new telemetry backend was introduced. The pass uses existing structured warnings for rejected unknown attributes and unsupported filters, existing projection/search sync health checks, reindex diagnostics, and operational convergence telemetry.

## 13. Governance Updates

PASS 12.3 establishes attributes as category-bound classification/search metadata only. Future attribute schema, filter generation, search indexing, projection, recovery, replay, feed/export, cache-warmer, analytics, ranking, or support paths must prove current SQL category attribute authority before accepting, filtering, faceting, or indexing attributes.

## 14. Honest Remaining Debt

No immutable attribute schema ledger, generated attribute registry, admin schema migration workflow, historical listing backfill, universal SQL-vs-index parity scanner, or full chaos harness was introduced. Existing historical listing rows may still retain obsolete JSON in SQL, but reviewed public projection/index/filter paths no longer treat obsolete keys as current filter authority.
