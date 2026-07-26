# POS-ADMIN-CERTIFICATION-01 PASS 12.5

# Variant & Inventory Marketplace Certification

## 1. Variant Inventory Findings

Variant identity, SKU, barcode, variant attributes, semantic current ownership, default-variant selection, and product lineage are SQL authority through `ProductVariants`, `ProductVariantAttributes`, SQL uniqueness constraints, and `ProductVariantLifecycleAuthority`. Inventory authority is SQL `ProductVariantStocks`, `Warehouses`, `InventoryReservations`, and `StockMovements`, constrained by `ProductVariantInventoryLineageAuthority`.

Calculation owners are backend SQL services: `ProductVariantStockRepository`, `InventoryConsistencyService`, CommerceHub cart/checkout services, stock-sync outbox processing, and SQL search overlays. Enforcement owners are the lifecycle/lineage helpers, EF queries with `IgnoreQueryFilters` plus explicit tenant predicates, serializable checkout/reservation transactions, event inboxes, projection fences, and stale-version filters.

No duplicate marketplace variant or stock authority was certified. CommerceHub listings reference current SQL variants; MongoDB, Elasticsearch, Redis, cart snapshots, event payloads, and replay inputs are derivative evidence only.

## 2. Variant Authority Findings

Variant public authority remains SQL-owned by `ProductVariantLifecycleAuthority.WherePubliclyVisibleLineage`. Public CommerceHub publish/update, search overlay, projection, cart pricing, and checkout now bind to current variant lifecycle semantics before preserving visibility or allowing purchase. Superseded, detached, deleted, inactive, unpublished, offline, or invalid-lineage variants fail closed.

## 3. Inventory Authority Findings

Inventory remains SQL-authoritative through current same-tenant variant/product/warehouse/stock/reservation lineage. `WhereCurrentInventoryStockOwner`, `WhereInventoryReservableLineage`, `FilterCurrentInventoryStockOwnersAsync`, and current consumable reservation filters prevent inactive warehouses, stale stock-owner rows, expired reservations, superseded variants, and orphan inventory from becoming available stock.

## 4. Marketplace Availability Findings

CommerceHub availability is calculated from current SQL listing, tenant, product, variant, category, pricing, stock, and reservation authority. Cart and checkout request payloads are stale-client evidence; backend pricing and stock are recomputed before response or order creation. Checkout reserves SQL stock inside a serializable boundary and fails closed when current stock lineage cannot satisfy requested quantity.

## 5. Search And Discovery Findings

Search/discovery inventory is derivative. `CommerceHubSearchSqlAuthorityOverlay` recomposes current SQL listing/product/variant/category/stock/price/media/review authority before indexing or serving Mongo-backed detail/list data. `AvailableStock`, `InStock`, stock timestamps, and stock event versions in MongoDB and Elasticsearch are projection metadata only.

## 6. Cart And Checkout Findings

Cart sanitation, cart pricing, and checkout validation recompute product/variant/publication/price/stock from SQL and reject stale listing, product, variant, price, shipping, vendor-governance, or stock evidence. Runtime hardening in this pass routes CommerceHub cart variant loading and checkout variant loading through `WherePubliclyVisibleLineage` so semantic supersession and deleted replacement rules are enforced consistently before stock validation.

## 7. Replay And Recovery Findings

Stock sync, projection replay, checkout idempotency replay, marketplace payment recovery, reindex, and auto-healing remain SQL-derived convergence paths. Inventory event inboxes collapse duplicate reservation/commit/release/return work; projection fences collapse duplicate stock projection delivery; current SQL stock snapshots win over event payload stock quantities.

## 8. Merchant Variant Governance Findings

Merchant publication intent and listing edits may reference variants only while current SQL listing/product/variant/category/vendor authority accepts them. Merchant variant edits, deletion, SKU changes, replacement, merge, or split do not create marketplace visibility authority; stale listing variants converge by suppression, unpublish, search delete, or failed checkout.

## 9. Auditability Findings

Inventory mutations create `InventoryEventInbox`, `InventoryReservation`, and `StockMovement` evidence with event ids, source, correlation, order/line references, stock after-values, and reservation ids. Variant and stock projection drift is observable through stock timestamps, event versions, stock-sync outbox messages, projection fences, operational convergence telemetry, and existing readiness/health checks.

## 10. Chaos Findings

Reviewed chaos cases fail closed: deleted/superseded variants are rejected by public lifecycle lineage; stale warehouse or stock-owner rows are filtered out; stale reservations are not consumable; replayed stock events reload SQL stock authority; reindex deletes or suppresses missing SQL authority; checkout performs final SQL stock reservation before order creation.

## 11. Runtime Hardening Performed

- CommerceHub cart pricing and sanitation now use `WherePubliclyVisibleLineage` before accepting variants.
- CommerceHub checkout variant authority loading now uses `WherePubliclyVisibleLineage` before price, stock, and order-line validation.
- PASS 12.5 source-freeze tests were added in `MarketplaceVariantInventoryCertificationTests`.

## 12. Monitoring Improvements

No new telemetry primitive was introduced. This pass certifies the existing observability boundary: `OperationalConvergenceTelemetry`, inventory event inbox rows, stock movements, stock-sync outbox rows, projection fences, stock timestamps, event versions, search sync metadata, and readiness checks.

## 13. Governance Updates

The `.ai-context` files now record PASS 12.5 as the freeze boundary for CommerceHub variant and inventory authority. Future marketplace variant, stock, cart, checkout, search, discovery, replay, recovery, feed/export, cache-warmer, support, analytics, ranking, localization, or global-script changes must prove current SQL variant and inventory lineage and update tests/docs in the same change.

## 14. Honest Remaining Debt

No universal SQL-vs-derivative stock parity scanner, immutable inventory ledger, generated variant registry, dedicated stock-drift dashboard, full chaos harness, or certification for future feed/export/support/reporting/global-script inventory writers was introduced. Existing read-model lag remains an accepted eventual-consistency tradeoff, not a second inventory authority.
