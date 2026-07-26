# PASS C1 CommerceHub Currency Entry Authority

Date: 2026-06-23

## Verdict

CommerceHub Currency Entry Authority is production-certifiable for the inspected runtime boundaries. CommerceHub's currency truth is the platform constant `FinancialCurrencyGuard.PlatformBaseCurrencyCode` (`EGP`), not a POS, vendor, storefront, browser, cache, route, or DTO-provided value.

`Tenant.Currency` remains the source denomination of the catalog because the current catalog model has no independent `Product.Currency` or `ProductVariant.Currency`. It is admissibility evidence only: a non-EGP source-priced product is rejected before CommerceHub listing creation, publication, republishing, bulk publication, or listing update. It never becomes CommerceHub currency.

## Currency Entry Inventory

| Entry surface | Runtime boundary | Result |
|---|---|---|
| Vendor/POS/default currency settings | `Tenant.Currency` | Can describe the source catalog price denomination only; cannot set CommerceHub truth. |
| Storefront configuration currency | `StoreConfigUpdatedEvent` / projection replay | Cannot set a CommerceHub document currency; replay converges documents to EGP. |
| Product/variant price publication | Publish/update handlers | Current tenant source currency must be EGP before a listing mutation. |
| Bulk publish/update/remove override | `AdminCommerceHubController` | Per-listing source-currency check; failed rows do not mutate. |
| Price override | Listing price authority | Amount-only; no currency field exists to forge. It resolves under EGP after admission. |
| Catalog projection, reindex, repair, auto-heal | SQL overlay and projection consumer | Non-EGP source-priced listings are excluded; surviving document currency is EGP. |
| Cart/checkout and persisted cart JSON | Cart/checkout services | Request/store/cache currency is discarded or normalized; only EGP is emitted and order/sub-order currency is EGP. |
| Browser cache/local storage/session/history/BFCache | CommerceHub client cart mapping/bootstrap | Persisted candidates are normalized to EGP and backend cart/checkout revalidate current SQL authority. |

## Findings

- Vendor, POS, storefront, and default settings cannot override CommerceHub currency. They do not provide a CommerceHub currency setter or accepted CommerceHub currency payload field.
- A forged `currency` field on publication, cart, or checkout intent does not alter the server currency: publication requests have no accepted currency input; cart sanitization reloads listing/variant/tenant authority; checkout derives EGP server-side.
- Missing, malformed, conflicting, USD, EUR, and mixed source-currency evidence fail closed at publication and bulk mutation boundaries. Non-EGP source-price evidence also makes the SQL search overlay return no public CommerceHub document.
- Imported, synchronization, onboarding, and vendor-enrollment paths do not write `CommerceHubListing` directly. The inspected listing write paths are the two handlers and tenant-scoped bulk operations; projections and reindexing only recompose from current SQL authority.
- Existing legacy/stale Mongo, Elasticsearch, local cart, route, and cache currency candidates cannot restore non-EGP admission: query/detail/search, cart, checkout, and projection replay all re-enter the SQL/platform boundary.
- Failure handling is fail closed: invalid publication input returns validation failure, unavailable/changed cart inventory or source currency fails checkout/cart validation, and stale/forged storage is sanitized then revalidated by backend reads. 401/403/404/409 do not expose a currency-authority fallback.

## Runtime Hardening Present

- `CommerceHubPublicationCurrencyPolicy` gates publish, republish, update, and bulk operations before a visibility-increasing or price-override mutation.
- Cart, checkout, projection replay, and SQL overlay stamp `FinancialCurrencyGuard.PlatformBaseCurrencyCode` and reject/exclude non-EGP source-price admission.
- CommerceHub frontend currency helpers and cart persistence use the EGP constant; unsupported response currencies are withheld rather than promoted to authority.
- Vendor financial, settlement, payout, refund, and ledger paths retain EGP guardrails through `FinancialCurrencyGuard.RequireEgpCurrency` and EGP DTO defaults.

## Exact Files Modified in This C1 Pass

- `PointOfSale.Tests/Architecture/CommerceHubPublicationCurrencyValidationTests.cs` — updated the currency-policy freeze for the current UI condition, which includes the independent catalog-management capability gate.
- `docs/architecture/commercehub-currency-entry-authority-certification-findings.md` — this certification record.

## Regression Freezes

- `CommerceHubCurrencyAuthorityCertificationTests`
- `CommerceHubPublicationCurrencyValidationTests`
- `CommerceHubPriceOverrideCurrencyCertificationTests`

Focused verification: `dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --no-restore --filter "FullyQualifiedName~CommerceHubCurrencyAuthorityCertificationTests|FullyQualifiedName~CommerceHubPublicationCurrencyValidationTests|FullyQualifiedName~CommerceHubPriceOverrideCurrencyCertificationTests"` passed 8/8.

## Explicit Certification Answers

- Who owns CommerceHub Currency Truth? CommerceHub platform runtime via `FinancialCurrencyGuard.PlatformBaseCurrencyCode` (`EGP`).
- Can POS currency become CommerceHub currency? No; it is source-price admissibility evidence only.
- Can storefront currency become CommerceHub currency? No.
- Can vendor currency override CommerceHub currency? No.
- Can USD products enter CommerceHub? No.
- Can EUR products enter CommerceHub? No.
- Can mixed-currency products enter CommerceHub? No.
- Can imported products bypass currency validation? No inspected import/sync writer bypass exists; replay/reindex recomposes current SQL authority.
- Can cache restore invalid currency state? No.
- Can non-EGP currency enter CommerceHub? No, on the inspected runtime boundaries.
- Were runtime changes required in this C1 pass? No additional runtime change was required; the inspected runtime hardening was already present. The regression freeze was corrected to reflect the current UI authorization predicate.
- Is CommerceHub Currency Entry Authority production-certifiable? Yes, for the inspected runtime paths.

## Architecture Rule

CommerceHub must keep EGP as its exclusive currency truth. POS/storefront/vendor settings and source catalog denomination may be used only to reject incompatible source prices; they must never be copied, selected, cached, replayed, or rendered as CommerceHub currency authority. Any future product import, ERP connector, catalog ingestion, direct listing writer, migration, repair, reindex, cache warmer, or external sync must pass the same EGP admission boundary and add a focused regression test.
