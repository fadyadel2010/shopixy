# PASS C2 CommerceHub Catalog Currency Authority

Date: 2026-06-23

## Verdict

CommerceHub Catalog Currency Authority is production-certifiable for the inspected runtime paths. CommerceHub owns catalog currency truth, projection, formatting, and presentation through the platform EGP constant. Tenant, storefront, vendor, browser, locale, DTO, cache, and projection evidence cannot select a catalog currency.

## Runtime Findings

- Catalog lists, cards, grids, homepage/featured/related recommendations, search results, autocomplete, category results, product details, variant changes, price ranges, sale/override prices, and shipping-price presentation are served from SQL-recomposed EGP catalog authority and formatted only by the CommerceHub formatter.
- `CommerceHubSearchSqlAuthorityOverlay` excludes listings whose source tenant price currency is not EGP and stamps every surviving public document with `FinancialCurrencyGuard.PlatformBaseCurrencyCode`.
- `CommerceHubQueryService` recomposes list, detail, search, suggestion, and autocomplete candidates before DTO mapping. MongoDB and Elasticsearch are derivative candidates, never price/currency truth.
- The customer client now rejects a list, detail, search, autocomplete, or zero-result-suggestion DTO containing missing or non-EGP catalog currency before it enters React Query or presentation. The card no longer echoes a raw DTO currency label.
- `formatCurrency` accepts only EGP and fixes the currency display locale to `en-EG`; a caller-supplied locale can change number glyphs only, not currency code or currency selection.
- Catalog responses use `no-store`; React Query can retain only previously admitted EGP DTOs. Local/session storage and cart history are revalidated or normalized through their separate backend pricing boundary and cannot revive a non-EGP catalog presentation.

## Failure And Adversarial Findings

- 401/403/404/409/422 responses do not provide a currency fallback. Missing, malformed, forged, mixed, USD, EUR, or SAR currency evidence is rejected at the client admission boundary or excluded by the SQL overlay.
- Variant selection changes only the EGP amount. It has no variant currency field and cannot alter the detail currency.
- Price filters and sorting operate on EGP SQL-visible prices. Browser locale, URL filters, cache, BFCache, and DevTools data cannot establish an alternate catalog currency.
- Projection replay, repair, reindex, and search synchronization converge through the SQL overlay and the EGP-only projection consumer; non-EGP candidates are withheld from public projection/search output.

## Runtime Hardening Performed

- Added a fail-closed catalog DTO currency-admission helper.
- Applied it to product lists, details, search, autocomplete, and zero-result recommendations before query-cache admission.
- Removed the raw product-card currency echo and prevented a forged detail DTO from being added to cart.

## Exact Runtime Files Modified

- `PointOfSale.CommerceHub/src/utils/commerceHubCurrencyAuthority.ts`
- `PointOfSale.CommerceHub/src/utils/index.ts`
- `PointOfSale.CommerceHub/src/services/commercehub-service.ts`
- `PointOfSale.CommerceHub/src/commerce/components/ProductCard.tsx`
- `PointOfSale.CommerceHub/src/pages/ProductDetailsPage.tsx`

## Regression Freeze

- `PointOfSale.Tests/Architecture/CommerceHubCatalogCurrencyAuthorityCertificationTests.cs`

## Explicit Answers

- Who owns CommerceHub Catalog Currency Truth? CommerceHub platform runtime, via `FinancialCurrencyGuard.PlatformBaseCurrencyCode` (`EGP`).
- Can catalog display USD, EUR, SAR, tenant currency, or storefront currency? No.
- Can a formatter override CommerceHub currency? No.
- Can browser locale affect catalog currency? No; it cannot change EGP selection.
- Can projections contain non-EGP public catalog currency? No inspected projection/replay path permits it.
- Can cache restore non-EGP presentation? No; client DTO admission fails closed and public reads recompose SQL EGP authority.
- Can mixed-currency evidence become catalog truth? No.
- Were runtime changes required? Yes, to close raw product-card DTO echo and reject invalid catalog DTOs before cache admission.
- Is CommerceHub Catalog Currency Authority production-certifiable? Yes, for the inspected runtime paths.
