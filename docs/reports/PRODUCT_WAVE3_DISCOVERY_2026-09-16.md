# PRODUCT WAVE 3 — DISCOVERY REPORT (2026-09-16)

Date (UTC): 2026-09-16
Scope: Wave 3 only (A. simple-product storefront pricing, B. AI-generate UX lock, C. product-type simplification).
Authority: source code + domain contracts. Waves 1, 2, 2A certified PASS and are not modified by discovery.

---

## 1. Executive Decision

1. **Part A — feasible WITHOUT schema migration.** The storefront-price column for
   simple products already exists: `ProductVariant.OnlinePrice` on the simple
   product's single default variant. It is currently suppressed (forced to `null`
   by `ProductPricingCompatibilityMirror.MirrorSimpleDefaultVariantFromDefaultUnit`
   and never authored), and the simple-product storefront reader
   (`ProductStorefrontVisibilityAuthority.ResolveSimpleProductPrice`) ignores
   variants entirely. The minimal safe change is: author `OnlinePrice` on the
   simple default variant, preserve it in the mirror, and prefer it in the
   simple-product reader with fallback to `DefaultUnit.SalePrice`. No new column,
   no migration, no historical rewrite.
2. **Part B — feasible as frontend-only hardening.** The AI endpoint is DTO-only
   (no `Product` write; persistence happens on human save). The gap is entirely
   in `AiProductGeneratorModal.jsx`: no request identity, no stale-response
   guard, conflicting controls stay enabled, no accessible status. Fix in the
   modal (+ reuse the existing `createLatestRequestGuard` helper).
3. **Part C — feasible as UX-only simplification.** There is NO `ProductType`
   enum; the only product-type axis is `Product.HasVariants` (`false` = Simple,
   `true` = Variant container) plus the orthogonal `TrackStock` flag. The create
   UI already exposes exactly 2 choices. Simplification = keep the 2 choices,
   clarify consequences, and lock the type switch on EDIT (with a backend
   defense-in-depth guard against destructive `HasVariants` flips on products
   with protected history). No domain concept is deleted or renamed.
4. **STOP conditions not triggered.** A safe place for the simple storefront
   price exists; no materially-different hidden types were found; the AI endpoint
   has no server-side product side effects (DTO-only), so no idempotency
   redesign is required.

---

## 2. Simple Product Pricing Architecture

- `Product` (`PointOfSale.Core/Entities/Product.cs:52-68`): `PurchasePrice`,
  `SalePrice`, `OnlinePrice` are **legacy mirrors only**. Simple products
  (`HasVariants=false`) own exactly one default variant + `ProductUnit` rows.
- `ProductUnit` (`PointOfSale.Core/Entities/ProductUnit.cs:9-43`): `PurchasePrice`,
  `SellingPrice` (both nullable), `ConversionFactor`, `IsDefault`, `IsBaseUnit`.
  **There is no per-unit online/storefront price column.** The default
  `ProductUnit.SellingPrice` is the canonical simple-product price authority
  (`CatalogPricingAuthority.ResolveSimpleSellingPrice`, `Core/Pricing/CatalogPricingAuthority.cs:10-11`).
- `ProductVariant` (`PointOfSale.Core/Entities/ProductVariant.cs:59-79`): `Price`
  (POS), `OnlinePrice` (storefront, null/0 falls back to `Price`),
  `CostPrice`, `IsDefault`, computed `EffectiveOnlinePrice` (`:192-194`).
- Simple-product write path: `ProductAuthoringOrchestrator` create/update →
  `ApplyAuthoredProductUnits` + `ProductVariantService.EnsureDefaultVariantAlignedAsync`
  → `ProductPricingCompatibilityMirror.MirrorSimpleDefaultVariantFromDefaultUnit`
  copies unit `SellingPrice→variant.Price`, unit `PurchasePrice→variant.CostPrice`,
  and **forces `variant.OnlinePrice=null`** (`Core/Pricing/ProductPricingCompatibilityMirror.cs:42-47`);
  `CreateDefaultVariantEntityAsync` also seeds `OnlinePrice=null`
  (`API/Services/ProductVariantService.cs:412-447`).
- Purchase (cost) source: `PurchasesController.cs:216-243` writes
  `variant.CostPrice` via `VariantCostAuthority` (weighted moving average,
  `Core/Inventory/VariantCostAuthority.cs:5-44`) + mirror; returns via
  `PurchaseReturnsController.cs:718`.
- POS (admin/POS) price source: `SalesController.cs:2642-2654`
  `ResolveAuthoritativeSaleUnitPrice` = `productUnit.SellingPrice ?? variant.Price × conv`
  (server-owned, client price ignored, `:1270-1289`). Manual orders:
  `OrdersController.cs:2039-2052` simple branch uses
  `ResolveSimpleSellingPrice(configuredUnit)`.
- Storefront simple price source (today): `ProductDocument.DefaultUnit.SalePrice`
  (projected from `ProductUnit.SellingPrice` by
  `ProductSearchSqlAuthorityOverlay.cs:240-264` and `ProductCreatedEventConsumer.cs:206-274`).
  Reader: `ProductStorefrontVisibilityAuthority.ResolveSimpleProductPrice`
  (`Infrastructure/MongoDB/Documents/ProductStorefrontVisibilityAuthority.cs:5-6`).
  **POS and Storefront share one value for simple products — this is the gap.**
- Currency: tenant `Tenant.Currency`; `FinancialCurrencyGuard` (EGP base for
  CommerceHub only); `CurrencyHelper.Round` = 2dp AwayFromZero
  (`Core/Helpers/CurrencyHelper.cs:14-15`). Tax: `TaxCalculationService` chain
  (system → tenant country → default); POS lines use `product.TaxRate` /
  `PriceIncludesTax`. Discounts/promotions are display-only overlays on
  `StorefrontVisiblePrice` (`StorefrontPromotionPriceComposer.cs:11-70`).

## 3. Multi-Variant Pricing Architecture

- Authority is per-variant: `Price` (POS), `OnlinePrice` (storefront),
  `EffectiveOnlinePrice = OnlinePrice>0 ? OnlinePrice : Price`
  (`ProductVariant.cs:192-194`, `CatalogPricingAuthority.cs:19-20`).
- Container `Product` fields are zeroed/nulled for `HasVariants=true`
  (`ProductAuthoringOrchestrator.cs:1253-1262`).
- Storefront: `Evaluate` variant branch (`VisibilityAuthority.cs:11-32`) prices
  the selected priced-active variant, `RequiresVariantSelection=true`,
  container `CanPurchase=false`; `EvaluateVariant` (`:47-68`) per-variant.
- Cart (multi): variant resolved by `VariantId`, price =
  `EvaluateVariant(...).Price` (`CartService.cs:583-598`); checkout revalidates
  from SQL `variant.OnlinePrice>0 ? OnlinePrice : Price`
  (`CheckoutService.cs:5136-5138`) into `OrderItem.UnitPrice/CostPrice`
  (`:6871-6875`). Frontend parity: `StorefrontDtos.cs:86-92`,
  `product.ts:69-98`.
- Authoring: `POST/PUT api/products/{id}/variants`
  (`ProductVariantsController.cs:257,621-639`) with `OnlinePrice` validation
  (negative rejected) and `PRODUCT_VARIANT_ONLINE_PRICE_INVALID` policy
  (`ProductAuthoringInfrastructurePolicies.cs:96-102`).

## 4. Storefront Price Authority

| Shape | POS authority | Storefront authority (today) | Storefront authority (Wave 3 target) |
|---|---|---|---|
| Simple | `ProductUnit.SellingPrice` (default unit) | `DefaultUnit.SalePrice` (same value — no split) | default-variant `EffectiveOnlinePrice` (`OnlinePrice>0 ? OnlinePrice : Price`), fallback `DefaultUnit.SalePrice` |
| Multi | per-variant `Price` | per-variant `EffectiveOnlinePrice` | unchanged |

- Server authority for checkout/cart revalidation is SQL `ProductVariant`
  (`CheckoutService.cs:5123-5151`); Mongo/Redis are never price authority at
  checkout. The event-driven Mongo projection already carries
  `Variants[].Price/OnlinePrice` (overlay `:285-316`; variant created/updated
  events `:1566,1588` in the orchestrator), so preferring the simple default
  variant's effective price in `ProductStorefrontVisibilityAuthority` flows to
  cart (`CartService.cs:594` simple branch via `productPurchasability.Price`),
  product DTOs, affiliate pricing (`TryResolveAffiliatePublicPricing`), and
  feeds (which already use variant `EffectivePrice`) with no new plumbing.
- Frontend must not become pricing authority: the authoring UI sends a
  *requested* `onlinePrice`; the backend validates (`>= 0`), persists to the
  default variant, and all readers re-resolve server-side. POS paths
  (`SalesController`, `OrdersController` manual split) are untouched and keep
  reading the unit price.

## 5. Price Validation

Existing canonical rules (reused, not invented):
- Negative: rejected — variant `Price/CostPrice/OnlinePrice < 0`
  (`ProductAuthoringInfrastructurePolicies.cs:88-110`,
  codes `PRODUCT_VARIANT_PRICE_INVALID`, `PRODUCT_VARIANT_ONLINE_PRICE_INVALID`,
  `PRODUCT_VARIANT_COST_PRICE_INVALID`). The simple-product `onlinePrice`
  request field gets the same `>= 0` rule (null/0 = "use POS price").
- Zero/null: valid, means fallback (`OnlinePrice null/0 → Price`), which
  preserves backward compatibility for all existing simple products.
- Precision/rounding: EF `decimal(18,2)` (`ProductConfiguration.cs:23-27`,
  `ProductVariantConfiguration.cs:38-46`); display rounding
  `CurrencyHelper.Round` (2dp AwayFromZero). No write-time rounding is added
  (matches existing variant behavior).
- Currency: tenant-scoped; no cross-currency conversion in this wave.
- Tax/discounts: unchanged interaction (tax computed downstream;
  promotions overlay the resolved storefront price).
- Minimum price: `> 0` required only at publication time for the POS-side
  default-unit price (existing `ValidateSimplePublication`,
  `ProductForm.jsx:1124`); storefront price is optional.

## 6. AI Generation Architecture

- Button: `ProductWorkspacePage.jsx:125-132` opens `AiProductGeneratorModal`
  (`isOpen/onClose/onApply`); apply path
  `productFormRef.current?.applyAiGeneratedData(...)` (`:78-86`).
- Request: `AiProductGeneratorModal.jsx:92-99`
  `api.post('/admin/ai/products/generate', {productIdea, language, country, tone, featureCount, faqCount})`;
  axios 30s timeout (`services/api.js:12-13,33-36`); POST is never auto-retried
  (`IDEMPOTENT_RETRY_METHODS`, `api.js:18,188-202`).
- API: `AdminAiProductGeneratorController.cs:28-35`
  `[Authorize(Owner,Admin,Manager)] POST api/admin/ai/products/generate` →
  `AdminProductGeneratorService.GenerateProductAsync` (OpenRouter-compatible,
  `CommerceAIOptions`: disabled by default, 30s configured / 60s HttpClient /
  one server-side retry up to 120s on timeout, `AdminProductGeneratorService.cs:176-210`);
  rate-limited (Redis, fail-open) with `429+Retry-After`; error contract
  `{error, message, retryAfterSeconds}` (`:53-65`), `504` on timeout.
- Response: strict 12-required-field JSON parse (`:326-368`); frontend previews
  name/slug/description/brand/manufacturer/tags/SEO/features/faq
  (`AiProductGeneratorModal.jsx:271-384`).
- Apply: `ProductForm.jsx:701-780` `applyAiGeneratedData` fills **empty fields
  only** (features/faqs/aiMetadata overwritten by design), marks slug manually
  edited, toasts success vs "already have values". Persistence happens only on
  human save (`:322-358` → `Create/UpdateProductRequest` →
  `ProductAuthoringCommandMapper` → `ProductAuthoringOrchestrator`).
  **The AI endpoint itself never writes a `Product` — no idempotency redesign needed.**

## 7. AI Loading Risk

Confirmed gaps in `AiProductGeneratorModal.jsx:1-111`:
1. Duplicate submit: only `disabled={loading}` on the submit button (`:244-250`);
   no early-return guard, no idempotency key; rapid double activation (e.g.
   Enter key + click) can issue two POSTs.
2. Conflicting controls stay enabled while generating (idea textarea, selects,
   number inputs, close/cancel) — user can change the request mid-flight or
   close and lose/overlap results.
3. No request identity/cancellation: no `AbortController`, no generation
   counter, no mounted guard — in-flight `await api.post` still calls
   `setGeneratedProduct/setError/setLoading` after close/unmount or after a
   newer request started (last-write-wins stale overwrite).
4. No accessible status: no `role="status"`/`aria-live`, no `aria-busy`;
   error box has no `role="alert"`; no focus management on completion.
5. `useEffect(:58-66)` resets state when closed but does not invalidate
   in-flight requests, so a late response can repopulate a reset/closed modal.
- Existing in-repo pattern to reuse: `createLatestRequestGuard` /
  `useLatestRequestGuard` (`Web/src/hooks/useLatestRequestGuard.js`, tested in
  `useLatestRequestGuard.test.ts`); `ProductForm` already uses a generation
  guard for product loads (`ProductForm.jsx:587-631`) — the same idea applies
  to the AI modal.

## 8. Product Type Inventory

- **No `ProductType` enum exists** (verified: `Core/Enums/*` holds only
  `PublishStatus` and `ProductVariantSemanticLifecycleState`; `Grep ProductType`
  hits only Aramex shipping strings and the `HasVariants`-based authoring
  policy). Wave 2A report states the same.
- The ONLY product-model axis: `Product.HasVariants` (`Core/Entities/Product.cs:98-113`,
  default `false`) + orthogonal flags `TrackStock` (`:96`, service/non-stock
  switch), `AllowBackorder` (`:205`), `PublishStatus` (`:180`),
  `IsOnlineVisible` (`:185`); `ProductTemplate` (`:282`) is display-only.
- Exposed UI: exactly **2 choices** — `Products.jsx:962-964` filter
  (all/simple/variant) and `ProductForm.jsx:1381-1421` creation radiogroup
  (Simple Product vs Variant Product). No physical/digital/bundle/service
  options exist anywhere. i18n: `en.json:3832-3834`, `ar.json:3335-3336`.
- Backend enforcement of the 2-model world: `ProductVariantsController.cs:162-172`
  forbids extra variants on simple products; `EnsureDefaultVariantAlignedAsync`
  (`ProductVariantService.cs:204-305`) guarantees exactly one default variant;
  `ValidateProductType` (`ProductAuthoringInfrastructurePolicies.cs:231-234,426-451`)
  rejects shape violations; publication gates differ per shape (`:347-424`).

## 9. Product Type Domain Mapping

- **Simple (`HasVariants=false`)**: Product + exactly 1 `IsDefault` variant
  (`SortOrder=0`) + default/base `ProductUnit`s (price authority); SKU/barcode
  mirrored product↔default-variant (tenant-unique); inventory on default-variant
  stocks (`TrackStock=false` = untracked/service-like); pricing =
  unit `SellingPrice` (POS+storefront today); storefront single-price, no
  selection; POS direct-add; purchasing/sales resolve the default variant.
- **Variant container (`HasVariants=true`)**: container holds no price/stock/SKU
  (`Purchase=Sale=0, Online/Barcode/SKU=null, TrackStock=false`,
  `Orchestrator.cs:1253-1262`); N variants each with SKU/price/stock;
  per-variant `Price/OnlinePrice→EffectiveOnlinePrice`; storefront requires
  variant selection; POS forces the variant modal; purchasing/sales/checkout
  require explicit `VariantId`.
- **Service/non-stock is NOT a type**: `TrackStock=false` / `AllowBackorder=true`
  only change stock guards (`VisibilityAuthority.cs:94-98`).
- Conversion risk (why C6 matters): flipping `HasVariants` on edit rewires
  SKU/stock/price ownership. Wave 2A's omission guard
  (`ThrowOnProtectedAuthoredVariantOmissionsAsync`) only fires when
  `variantsSubmitted`; a variant→simple flip that omits variants without
  submitting them bypasses it — hence the edit-lock + backend flip guard.

## 10. Existing Data Safety

- All existing simple products have default-variant `OnlinePrice=null` ⇒ new
  reader fallback reproduces today's price bit-for-bit; no migration, no
  backfill, no historical rewrite.
- `HasVariants` values are never rewritten by Wave 3 (frontend edit-lock;
  backend flip guard only *rejects* dangerous flips, never auto-converts).
- Wave 2/2A authorities untouched: SKU uniqueness
  (`ProductVariantConfiguration.cs:94-113`), default-variant alignment,
  clone/import ownership, deletion protection, semantic lifecycle.
- Treasury/accounting/GL/inventory-valuation files are out of scope and
  untouched; no direct SQL business writes.

## 11. UX Simplification Candidate

- Keep the existing 2-card radiogroup on CREATE (already minimal), with
  sharpened consequence copy:
  - Simple Product → "One sellable item. Purchase price, POS price and an
    optional separate storefront price."
  - Product with Variants → "Multiple option combinations. Each variant gets
    its own SKU, stock, POS price and storefront price."
- On EDIT (`productId` set): render the type section read-only (radios
  disabled) with the notice "Product type is fixed after creation to protect
  pricing, stock and order history." — casual dropdown conversion becomes
  impossible; intentional conversions go through support/API with the backend
  guard as backstop.
- Backend backstop: reject `HasVariants` flips on update when the product has
  protected history (`ProductVariantDeletionProtectionAuthority`, same blocker
  contract as Wave 2A) or, for variant→simple, more than one live variant;
  fresh products without history may still convert.
- Terminology stays `Simple Product` / `Variant Product` (matches existing
  `simpleProductType`/`variantProductType` i18n and domain language).

## 12. Cross-Wave Dependencies

- Wave 1: slug authority, localization fallbacks, manual visibility —
  untouched; the new storefront-price field is orthogonal (price, not identity).
  Slug tests/regression re-run.
- Wave 2: variant persistence, SKU integrity, Option-Group Enter — untouched;
  simple storefront price reuses the variant row without altering SKU/option
  behavior. `EnsureDefaultVariantAlignedAsync` keeps its contract (plus
  OnlinePrice preservation, which is strictly less destructive).
- Wave 2A: deletion safety — untouched and additionally reused (flip guard
  delegates to `GetBlockersAsync`); conversion can no longer smuggle variant
  deletion past the omission guard.

## 13. Findings

1. Simple products lack a storefront/POS price split; the column exists but is
   suppressed (mirror nulls it; reader ignores it).
2. Cart-simple vs checkout-simple currently agree (both unit-derived); the fix
   must move BOTH to the variant-effective price — achieved by changing the
   shared reader (`ResolveSimpleProductPrice`) since cart-simple uses
   `productPurchasability.Price` and checkout uses SQL variant effective price.
3. `request.OnlinePrice` ("legacy compatibility input") is the natural simple
   storefront-price input — no DTO shape change needed.
4. AI risk is modal-local; endpoint is DTO-only and safe.
5. Product types are already binary; the work is locking + copy, not removal.

## 14. Proposed Implementation

- **A1** `CatalogPricingAuthority`: add `NormalizeSimpleOnlinePrice(decimal?)`
  (null/0→null, negative→throw, keep scale) and
  `ResolveSimpleOnlinePrice(defaultVariant, defaultUnit)` (variant-effective
  else unit price).
- **A2** `ProductPricingCompatibilityMirror.MirrorSimpleDefaultVariantFromDefaultUnit`:
  stop writing `OnlinePrice` (preserve); keep Price/CostPrice mirroring.
- **A3** `ProductAuthoringValidator`: reject negative simple `Pricing.OnlinePrice`
  (`PRODUCT_VARIANT_ONLINE_PRICE_INVALID`, field `onlinePrice`).
- **A4** `ProductAuthoringOrchestrator` (create + update): after
  `EnsureDefaultVariantAlignedAsync`, when `!HasVariants`, apply normalized
  `command.Pricing.OnlinePrice` to the default variant (changed-field tracked
  so variant events/projections carry it).
- **A5** `ProductStorefrontVisibilityAuthority.ResolveSimpleProductPrice`:
  prefer simple default-variant effective price, fallback `DefaultUnit.SalePrice`.
- **A6** `ProductsController` request comments: document `OnlinePrice` as the
  simple-product storefront price.
- **A7** `ProductForm.jsx`: `simpleProductData.storefrontPrice`, load from
  default-variant `onlinePrice`, "Storefront price" input in the simple pricing
  UI with fallback hint, `onlinePrice` in the authoring payload.
- **B** `AiProductGeneratorModal.jsx`: request-guard (counter + invalidate on
  close/unmount), early-return on double submit, disable conflicting controls
  while `loading`, `role="status"` progress message + `role="alert"` errors +
  focus management, single-apply guard.
- **B2** `ProductWorkspacePage.jsx`: no logic change (modal owns the lock);
  verified no second submission path.
- **C1** `ProductForm.jsx`: disable type radios on edit + fixed-type notice;
  sharpen create-mode consequence copy.
- **C2** `ProductAuthoringOrchestrator.ExecuteUpdateAsync`: reject destructive
  `HasVariants` flips (protected history or variant→simple with >1 live
  variant) with `PRODUCT_TYPE_CONVERSION_UNSUPPORTED`; add code to the
  frontend safe-list.
- Tests: backend (authority/mirror/validator/visibility) + frontend
  (ProductForm pricing + type lock, AI modal lock/stale/a11y).

## 15. Acceptance Matrix

| # | Check | Owner |
|---|---|---|
| A1 | Simple product shows Purchase / POS / Storefront price inputs | ProductForm |
| A2 | Storefront price persists (variant `OnlinePrice`) and reloads | API + UI |
| A3 | POS price independent (unit `SellingPrice` / variant `Price` unchanged) | Sales/Orders |
| A4 | Storefront/cart/checkout use the configured storefront price | Reader/Cart/Checkout |
| A5 | Multi-variant pricing regression-free | Existing suites |
| A6 | Negative rejected; null/0 = POS fallback; 2dp rounding preserved | Validator/Authority |
| A7 | Existing products load identically (fallback path) | Backward compat |
| B1–B7 | One request per generation; dup-click blocked; loading + disabled controls; success/failure restore; stale ignored; `role=status/alert` | AI modal |
| C1–C5 | 2 choices on create; type locked on edit with reason; no data migration; flips with history rejected; Wave 2/2A suites green | Type UX + guard |
