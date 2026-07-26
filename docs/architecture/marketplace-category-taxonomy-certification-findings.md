# POS-ADMIN-CERTIFICATION-01 PASS 12.2 Marketplace Category Mapping & Taxonomy Authority

Date: 2026-05-31

## Scope

This pass certifies CommerceHub marketplace taxonomy after PASS 12.1 merchant publication intent. Category mapping is classification authority only. Marketplace visibility remains current SQL listing, product, variant, CommerceHub category, tenant, vendor-governance, pricing, stock, media, and review authority plus marketplace governance.

## 1. Taxonomy Inventory Findings

CommerceHub taxonomy is SQL-owned by `CommerceHubCategories` and `CommerceHubCategoryAttributes`. Category lineage is enforced by `CommerceHubCategoryLineageAuthority`, which accepts only active categories whose parent chain is present, active, non-self-referential, and acyclic.

Listing classification is stored on SQL `CommerceHubListings.CommerceHubCategoryId`. Mongo `CommerceHubProductDocument.CategoryId/CategoryName/CategoryPath`, Elasticsearch `commercehub_products` category fields, homepage category references, suggestions, autocomplete buckets, and filter metadata are derivative.

## 2. Category Mapping Findings

Publish and update handlers validate live CommerceHub category lineage before changing a listing category. Mapping is tenant-safe because the listing remains tenant-owned by `{ TenantId, VariantId }`, while the category taxonomy is global platform SQL.

Invalid, stale, inactive, orphaned, or cyclic categories now fail closed at merchant picker, attribute lookup, publish/update validation, public search overlay, homepage sanitation, and reindex/stale-delete paths.

## 3. Category Lineage Findings

Lineage resolution is SQL-derived through `CommerceHubCategoryLineageAuthority.LoadLiveSnapshotAsync`. The helper rejects empty category ids, inactive rows, missing parents, self-reference, and cycles. Category path strings in Mongo/search are recomputed from the accepted SQL path and cannot restore lineage.

## 4. Merchant Category Selection Findings

POS Admin uses `api/commercehub/categories/tree` and `api/commercehub/categories/{categoryId}/attributes`. Runtime hardening now filters the category tree through live SQL lineage and returns `404` for attributes when the category lineage is not live. Direct publish/update still revalidates the submitted category server-side.

## 5. Search And Filter Findings

CommerceHub list/search recomposes returned listings through `CommerceHubSearchSqlAuthorityOverlay`. Totals and category facets are built from SQL-visible returned items, and autocomplete/suggestion categories are filtered through live SQL taxonomy. Raw Elasticsearch or cached category buckets are candidates only.

## 6. Attribute Dependency Findings

Attributes are SQL-owned by `CommerceHubCategoryAttributes` and are bound to the selected live category. `CommerceHubAttributeService` now fails validation when the category lineage is not live, preventing stale attributes from being normalized for a deleted or invalid category. Filterable/searchable attributes remain derivative filter metadata and should be the focus of PASS 12.3.

## 7. Recovery And Rebuild Findings

CommerceHub projection, marketplace homepage projection, reindex, search health, and auto-healing paths discover candidates from MongoDB/search state but converge through current SQL listing/category/product/variant/tenant/vendor authority. Missing or invalid taxonomy collapses to delete, suppress, not-found, or stale search deletion.

## 8. Search Index Findings

Elasticsearch category fields are derivative. `CommerceHubSearchSqlAuthorityOverlay` reloads the SQL listing and live CommerceHub category snapshot before indexing. Reindex deletes stale search documents absent from current SQL-visible listing identities and verifies counts against SQL-visible listings.

## 9. Auditability Findings

Category mappings are observable through SQL listing state, listing lifecycle events, structured publish/update logs, search sync status, reindex stale-delete logs, CommerceHub search health, auto-healing diagnostics, and marketplace governance audit when category visibility interacts with vendor governance.

No separate immutable taxonomy mutation ledger exists.

## 10. Chaos Findings

The reviewed chaos cases collapse to current SQL authority:

- category deletion during publish: publish/update reject non-live lineage.
- category removal during reindex: overlay returns missing authority and stale search delete removes documents.
- lineage repair during recovery: recovery recomputes from current SQL snapshots.
- category migration during replay: replay payload category fields are evidence only and overlay recomposes current SQL.
- category disablement during listing update: update and attribute validation fail closed.

## 11. Runtime Hardening Performed

- `CommerceHubCategoriesController.GetCategoryTree` now filters active category rows through `CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync`.
- `CommerceHubCategoriesController.GetCategoryAttributes` now returns `404` when category lineage is not live.
- `CommerceHubAttributeService.ValidateAndNormalizeAsync` now fails closed when category lineage is not live.
- `MarketplaceCategoryTaxonomyCertificationTests` freezes the reviewed taxonomy, mapping, lineage, filter, attribute, recovery, search-index, and documentation boundaries.

## 12. Monitoring Improvements

No new telemetry backend was introduced. PASS 12.2 relies on existing structured publish/update logs, projection/search sync status, search health checks, auto-healing diagnostics, reindex stale-delete counts, operational convergence telemetry, and governance audit logs.

## 13. Governance Updates

PASS 12.2 establishes CommerceHub category mapping as classification authority only. Future category mapping, taxonomy management, attribute schema, filter generation, homepage category, suggestion, autocomplete, reindex, recovery, cache-warmer, feed/export, support, analytics, or global-script work must declare SQL taxonomy authority, lineage behavior, attribute binding, stale-delete behavior, auditability, regression tests, and docs in the same change.

## 14. Honest Remaining Debt

- CommerceHub taxonomy has no dedicated admin taxonomy workflow, immutable taxonomy-change ledger, generated taxonomy registry, or universal SQL-vs-derivative parity scanner.
- Attribute schema drift is only bounded by current validation and stored listing schema snapshots; PASS 12.3 should certify attribute poisoning, schema evolution, attribute filter generation, and attribute search indexing more deeply.
- Category tree and attribute endpoints are SQL-filtered but remain eventually consistent with frontend local state.
- Future bulk taxonomy import, localization, support scripts, feed/export, reporting, ranking, analytics, and global operations remain path-specific certification debt.
