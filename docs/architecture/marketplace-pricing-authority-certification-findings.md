# POS-ADMIN-CERTIFICATION-01 PASS 12.4 Marketplace Pricing Authority

## 1. Pricing Inventory Findings

Marketplace customer-visible item price is owned by current SQL product variant pricing. The reviewed sources are product-level legacy `Price` / `OnlinePrice`, variant `Price` / `OnlinePrice`, CommerceHub listing `CommerceHubPrice`, Mongo `CommerceHubProductDocument.Price`, Elasticsearch CommerceHub search price fields, cart price snapshots, checkout expected unit prices, affiliate public price ranges, storefront promotion overlays, shipping markup, and CommerceHub promo-code commission discounts.

The authority owner for CommerceHub item price is SQL `ProductVariants.EffectiveOnlinePrice`. Calculation is performed by backend CommerceHub cart/checkout/query/projection paths. Enforcement is provided by publish/update validation, SQL listing constraints, `ApplicationDbContext` save guards, CommerceHub projection SQL reloads, and `CommerceHubSearchSqlAuthorityOverlay`.

## 2. Final Price Authority Findings

Final CommerceHub item price has one authority path: current SQL variant online price when present and positive, otherwise current SQL variant price. Checkout recomputes this price from SQL and rejects stale client expected prices. Cart sanitization refreshes snapshots from SQL. Search and Mongo projection recomposition use the same variant price through the SQL overlay.

## 3. Marketplace Override Findings

Marketplace listing price overrides are intentionally disabled. `CommerceHubListing` rejects non-null `CommerceHubPrice`, publish/update handlers reject override input, `ApplicationDbContext` clears legacy values before persistence, the SQL check constraint requires `CommerceHubPrice IS NULL`, and Mongo/search projections clear `CommerceHubPriceOverride`. Existing override columns remain compatibility baggage, not active authority.

## 4. Variant Pricing Findings

Variant price changes propagate through `VariantPriceUpdatedEvent`, but the consumer reloads current SQL variant authority and requires matching projection mutation version before updating CommerceHub projections. Lower-version or stale delivery is rejected, and Mongo price metadata tracks `LastPriceEventVersion` / timestamp to prevent out-of-order resurrection.

## 5. Promotion Findings

Storefront product promotions are not CommerceHub numeric price authority. `StorefrontPromotionPriceComposer` is documented as storefront-only. CommerceHub projection responds to promotion events only by reasserting base SQL variant prices on affected listing documents. CommerceHub promo codes can discount commission/subtotal calculations in cart/checkout workflows, but they do not mutate listing item price authority.

## 6. Merchant Governance Findings

Merchant publish/update requests may classify listings and provide attributes, but they cannot set marketplace item price. Merchant pricing changes must occur through the POS product variant pricing authority. Listing publication remains separately governed by vendor, category, product, tenant, variant, shipping, stock, and search visibility authority.

## 7. Search And Discovery Pricing Findings

MongoDB and Elasticsearch prices are derivative. CommerceHub search sync calls `CommerceHubSearchSqlAuthorityOverlay`, which recomposes current SQL listing/product/variant/category/tenant/stock/media/pricing authority and deletes search documents when SQL authority is missing. Price facets and stats are discovery metadata, not price authority.

## 8. Replay And Recovery Findings

Projection replay is SQL-first and version-aware on reviewed price paths. Runtime hardening in this pass completed CommerceHub projection fences after SQL-rejected variant-price events and after promotion-triggered base-price refresh. Duplicate replay now collapses after successful fail-closed or no-op refresh decisions instead of repeatedly reacquiring the same event.

## 9. Auditability Findings

Publish/update handlers log CommerceHub listing requests and persisted metadata. Cart and checkout paths log pricing recalculation outcomes and reject stale expected prices with explicit error codes. Projection consumers log SQL authority rejection, stale event rejection, duplicate replay collapse, and search sync failure.

## 10. Chaos Findings

Price update during reindex converges through the SQL overlay. Promotion expiry during replay reasserts SQL base price, not a stale promotional item price. Variant deletion or invalid lineage causes search overlay deletion/suppression. Override resurrection is blocked by entity validation, save guards, SQL constraint, and projection clearing.

## 11. Runtime Hardening Performed

- `CommerceHubProjectionConsumer` now completes the variant-price projection fence when current SQL authority rejects a price event.
- `CommerceHubProjectionConsumer` now completes the promotion projection fence after SQL-derived promotion/base-price refresh.
- `MarketplacePricingAuthorityCertificationTests` freezes the reviewed pricing, override, variant, promotion, search, replay, and documentation boundaries.

## 12. Monitoring Improvements

No new telemetry backend was introduced. The pass relies on existing structured logs, `ProjectionConsumerEventFence` / `ProjectionEventFence` replay telemetry, CommerceHub search sync status, search health checks, and existing price-convergence test harness markers.

## 13. Governance Updates

PASS 12.4 establishes CommerceHub item price as SQL variant-pricing authority only. Future marketplace price override, promotion, cart, checkout, search, discovery, affiliate, reindex, recovery, cache-warmer, support, analytics, ranking, feed/export, or global-script paths must prove current SQL variant price authority and must not use merchant listing fields, MongoDB, Elasticsearch, Redis, client snapshots, or promotion overlays as final item price authority.

## 14. Honest Remaining Debt

The legacy `CommerceHubPrice` column and `CommerceHubPriceOverride` projection field still exist for compatibility, though they are constrained and cleared. No immutable pricing ledger, universal SQL-vs-derivative price parity scanner, dedicated marketplace pricing dashboard, full chaos harness, or path-specific certification for future feed/export/support/ranking/global-script pricing surfaces was introduced.
