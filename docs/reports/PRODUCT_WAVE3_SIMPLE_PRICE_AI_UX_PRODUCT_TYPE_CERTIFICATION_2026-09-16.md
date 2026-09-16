# PRODUCT WAVE 3 — SIMPLE PRICE / AI UX / PRODUCT TYPE CERTIFICATION (2026-09-16)

Date (UTC): 2026-09-16
Verdict: **CONDITIONAL PASS** (see §30; all implementation, automated-test, build,
and regression gates pass — only live-environment browser/tenant gates remain,
explicitly unavailable in this execution environment).
Commit: none (per wave policy — changes left uncommitted in the working tree).

---

## 1. Executive Verdict

**CONDITIONAL PASS.** Simple-product storefront pricing works end-to-end against
the existing price authority with no schema migration; AI generation is locked
against duplicate/stale application with accessible status; product-type UX keeps
the proven 2-choice model on create and locks conversion on edit with a backend
backstop. 103/103 focused backend tests and 64/64 related frontend tests pass;
API Debug + Release and Web production builds pass; `git diff --check` is clean;
no Wave 1/2/2A regression was introduced (every residual failure was proven to
fail identically at pristine HEAD). No commit was made, per policy.

## 2. Scope

Wave 3 only: (A) simple-product storefront pricing, (B) AI-generate
loading/interaction lock, (C) product-type simplification. Untouched: Wave 1
(identity/slug/localization/visibility), Wave 2/2A (variant persistence, SKU
integrity, Option Enter, deletion safety), Treasury, accounting/GL, inventory
valuation, settlement, historical data. No direct SQL business writes.

## 3. Discovery Summary

Full discovery precedes this report:
`docs/reports/PRODUCT_WAVE3_DISCOVERY_2026-09-16.md` (15 sections). Decisive
findings: (1) the simple storefront-price column already exists
(`ProductVariant.OnlinePrice` on the simple default variant) but was suppressed
(mirror forced `null`; reader ignored variants) — so no migration is needed;
(2) the AI endpoint is DTO-only (no product side effects), so hardening is
modal-local; (3) no `ProductType` enum exists — the model is binary
(`HasVariants`) plus orthogonal `TrackStock`, so simplification is lock + copy,
not removal.

## 4. Simple Product Pricing

Simple products now author three channel prices: Purchase price (default-unit
`PurchasePrice` → variant `CostPrice`), POS selling price (default-unit
`SellingPrice` → variant `Price`), and online price (default-variant
`OnlinePrice`, new). UI: "Storefront Price" input in the Product Units section
(`ProductForm.jsx`), loaded from the default variant, saved as authoring
payload `onlinePrice`. Empty/null/zero = "sell online at the POS price".

## 5. Multi-Variant Pricing

Unchanged. Per-variant `Price` (POS) / `OnlinePrice` (online) →
`EffectiveOnlinePrice`; container fields stay zeroed; variant authoring,
publication gates, cart variant enforcement, and checkout revalidation are
untouched. Multi-variant regression suites pass (§28).

## 6. Storefront Price Authority

Server-side only. Authoring input `onlinePrice` is validated (`>= 0`) and
persisted to the simple default variant by `ProductAuthoringOrchestrator`
(`ApplySimpleProductOnlinePrice`, changed-field tracked so variant events carry
it). Readers resolve via `CatalogPricingAuthority.ResolveSimpleOnlinePrice`
and `ProductStorefrontVisibilityAuthority.ResolveSimpleProductPrice` (prefer
default-variant effective online price, fall back to `DefaultUnit.SalePrice`).
Cart-simple (`productPurchasability.Price`) and checkout (SQL variant effective
price) converge on the same value. POS paths (`SalesController`,
`OrdersController` manual split) still read unit truth — never overwritten.

## 7. Price Validation

Negative online price rejected at authoring
(`PRODUCT_VARIANT_ONLINE_PRICE_INVALID`, field `onlinePrice`;
`NormalizeSimpleOnlinePrice` additionally throws). Null/zero = POS fallback.
`decimal(18,2)` persistence; `CurrencyHelper.Round` (2dp AwayFromZero) for
display; tenant-scoped currency; tax/promotion interaction unchanged
(promotions overlay the resolved price). No new financial policy invented.

## 8. AI Architecture

Unchanged backend: `AiProductGeneratorModal` → `POST api/admin/ai/products/generate`
→ DTO-only response → empty-fields-only apply (`applyAiGeneratedData`) → human
save persists. No product writes, no idempotency redesign needed. Hardening is
confined to `AiProductGeneratorModal.jsx`, reusing the in-repo
`createLatestRequestGuard` helper.

## 9. AI Loading UX

Generation shows `role="status"` ("AI generation is in progress. The form is
locked until it completes."), `aria-busy` on the form, spinner in the submit
button ("Generating…"), all conflicting inputs disabled (idea, language,
country, tone, counts, generate, apply). Close/Cancel stay enabled (no browser
freeze) — staleness is handled by identity, not trapping. Focus moves to the
preview heading on success; errors use `role="alert"`.

## 10. AI Duplicate Protection

Early return while `loading` plus disabled submit covers keyboard/assistive
double activation — exactly one POST per generation (asserted). Draft applies
exactly once per generation (`appliedGenerationRef`), so repeated Apply
activation cannot double-fill the form.

## 11. AI Failure/Retry

Stale finishers never clear a newer generation's loading state
(`isLatestRequest` check in `finally`). Failure restores all controls, announces
via `role="alert"`, and retry re-issues cleanly (asserted: fail → retry →
success, error cleared). Close/unmount invalidates the guard, so late responses
are discarded (asserted: close → reopen → resolve → no repopulation).

## 12. Product Type Inventory

Confirmed: no `ProductType` enum; sole axis `Product.HasVariants`
(false = Simple, true = Variant container); orthogonal `TrackStock`
(service/non-stock behavior), `AllowBackorder`, `PublishStatus`,
`IsOnlineVisible`; `ProductTemplate` display-only. UI already exposes exactly 2
choices (create radiogroup + list filter). Nothing was deleted or renamed.

## 13. Product Type Domain Mapping

Per discovery §9 (unchanged by implementation): Simple = product + 1 default
variant + unit price authority, mirrored SKU/barcode, default-variant stock;
Variant container = priceless/stockless shell with N priced/stocked variants;
service/non-stock = `TrackStock=false` flag, not a type. Conversion flips
rewire SKU/stock/price ownership — hence the edit lock (§14).

## 14. Product Type Simplification

Create flow keeps the 2-card radiogroup with sharpened consequence copy
(Simple: "purchase price, POS price and an optional separate storefront
price"; Variants: "each variant gets its own SKU, stock, POS price and
storefront price"; en+ar locales). Edit flow locks both radios
(`disabled` + amber notice "Product type is fixed after creation…"). Backend
backstop: `ExecuteUpdateAsync` rejects `HasVariants` flips while Wave 2A
blockers exist or a variant→simple flip would strand >1 live variant
(`PRODUCT_TYPE_CONVERSION_UNSUPPORTED`, added to the frontend safe-error list);
fresh products without history may still convert.

## 15. Existing Data Safety

All pre-Wave-3 simple products have default-variant `OnlinePrice=null`, so the
reader fallback reproduces today's price bit-for-bit — no migration, backfill,
or historical rewrite (EF model check: zero entity/DbContext/configuration/
migration changes). No `HasVariants` value is rewritten; the flip guard only
rejects, never converts. Wave 2/2A authorities (SKU uniqueness, default-variant
alignment, clone/import ownership, deletion protection, semantic lifecycle)
untouched.

## 16. Implementation

Backend: `CatalogPricingAuthority` (+`NormalizeSimpleOnlinePrice`,
+`ResolveSimpleOnlinePrice`); `ProductPricingCompatibilityMirror` (stops
nulling `OnlinePrice`, keeps Price/CostPrice mirroring);
`ProductAuthoringValidator` (negative simple online price rejected);
`ProductAuthoringOrchestrator` (+`ApplySimpleProductOnlinePrice` on
create+update, +flip guard on update);
`ProductStorefrontVisibilityAuthority.ResolveSimpleProductPrice` (prefer
default-variant effective price, unit fallback); `ProductsController` record
comments; `ProductCreatedEventConsumer` comment. Frontend: `ProductForm.jsx`
(storefront field, payload, validation, pixel price, type lock, safe-list),
`AiProductGeneratorModal.jsx` (guard, locks, status/alert, focus,
single-apply), en/ar locale keys + sharpened choice copy.

## 17. Automated Tests

Backend `PointOfSale.Tests/ProductWave3/SimpleProductStorefrontPricingTests.cs`
(20 tests: normalize/resolve, mirror preservation + POS independence,
validator accept/reject, reader configured/fallback/multi-variant, rounding).
Frontend `AiProductGeneratorModal.test.tsx` (6: status+lock, single POST,
success restore + single apply, failure restore + retry, stale-after-close
ignored, idea required) and `ProductFormWave3.test.tsx` (6: field present,
persist + POS independence, edit load, null fallback, 2 choices on create,
edit lock + notice). Updated to the Wave 3 contract: 3 payload assertions in
`ProductForm.test.tsx`, 2 reader tests in
`ProductVariantLifecycleAuthorityTests.cs`, 2 affiliate snapshot tests.

## 18. Test Quality

Tests assert persisted/state-transition behavior, not mere existence:
mirror test asserts `Price`/`CostPrice` mirrored while `OnlinePrice: 80`
survives (fails pre-fix where it was nulled); reader tests assert resolved
prices (110 vs fallback); validator tests assert rejection codes; AI tests
assert POST call counts, disabled states, control restoration, and stale
ignorance; type tests assert radio disabled state and payload content.
Pre-fix-failing coverage: the 4 updated characterization tests failed before
the fix and pass after — the intended contract change.

## 19. Build

API Debug: 0 errors. API Release: 0 errors. Web production (`vite build`):
success. `git diff --check`: clean. EF model check: no entity/DbContext/
configuration/migration diff (migration correctly not required). Storefront
production build not run — Storefront untouched by this wave.

## 20. Browser Setup

No live browser certification was performed: this execution environment
provides no browser tooling or running tenant (AhramStore), and wave policy
forbids touching historical products. The UI states below are therefore
verified by automated component tests (jsdom) rather than live runs, and are
called out as the remaining gate in §30.

## 21. Simple Product Pricing Browser

Not run live (see §20). Component-level equivalents pass: field renders for
simple create; explicit 80 persists alongside unit 100 in the POST payload;
edit loads 80 from the default variant; empty field sends null (POS fallback);
variant-mode hides the field. API/DB/Storefront reconciliation logic is covered
by authority/reader unit tests (§17).

## 22. AI Browser Certification

Not run live (see §20). Component-level equivalents pass for every required
behavior: immediate loading status, duplicate-click single POST, conflicting
controls disabled, accessible `role=status`/`role=alert`, single apply,
success/failure restoration, retry, stale-after-close ignorance.

## 23. Product Type Browser Certification

Not run live (see §20). Component-level equivalents pass: exactly 2 choices
enabled on create; both radios disabled with the fixed-type notice on edit;
backend flip guard unit-covered via contract (rejection path returns
`PRODUCT_TYPE_CONVERSION_UNSUPPORTED`).

## 24. Responsive Certification

Not run live (see §20). Implementation reuses existing responsive primitives
(`grid md:grid-cols-2`, `rounded-xl`, standard `label`/`input` classes, existing
modal layout); no new layout system introduced. RTL: new strings ship in both
`en.json` and `ar.json` (i18n certification tests pass, including key parity);
inputs inherit document direction; no horizontal-overflow structures added.

## 25. Arabic/RTL Certification

Not run live (see §20). All six new keys plus both revised choice hints have
Arabic translations (`ar.json`); `i18nCertification.test.ts` passes. No
hardcoded user-facing English added except the AI modal's pre-existing literal
style, which this wave did not expand (status/error strings follow the file's
existing literal convention).

## 26. API/DB Reconciliation

Reconciliation by authority (no live DB writes performed): UI `onlinePrice` →
API `Create/UpdateProductRequest.OnlinePrice` → command `Pricing.OnlinePrice`
→ validated → simple default variant `OnlinePrice` (SQL authority) →
projections carry `Variants[].OnlinePrice` (overlay recomposes from SQL;
variant created/updated events publish with changed fields) → storefront
reader/cart/checkout resolve the same effective price. POS price path
(unit `SellingPrice` → variant `Price` → POS/mirror) is disjoint and verified
independent by test. No duplicate AI application possible (single-apply +
human-save-only persistence).

## 27. Tenant Isolation

No tenant-scoping change: the online price lives on the tenant-owned variant
row (`TenantId` PK/FK + unique indexes unchanged); readers resolve within the
tenant's own document/rows; AI generation is tenant-contextualized
(`RequiredTenantId`) and DTO-only; no cross-tenant cache keys introduced
(cart currency lock untouched). Deletion/flip guards scope by `tenantId`.

## 28. Wave 1/2/2A Regression

Passing: 103/103 focused backend set (Wave 3 + lifecycle authority + affiliate
snapshots + deletion safety + authoring policy/mapper/variant-identity +
authoring boundary gates); 95/95 broad `ProductAuthoring` suite; 130/134
deletion/clone/merge/lifecycle batch (4 failures proven identical at pristine
HEAD — frozen-doc drift in promotion/shipment/smart-offer/service-marketplace,
untouched domains); 64/64 related frontend files. Every other residual failure
(coupon atomicity/revalidation SQLite `btrim`, theme/checkout storefront-file
gates, overlay-text governance, legacy-endpoint-deprecated CON_* integration,
`ApplyNoStoreHeaders` drift) was proven byte-identical at pristine HEAD via
stash-baseline runs — zero regressions introduced by Wave 3. Slug authority,
variant persistence, SKU uniqueness, Option Enter, and deletion safety behavior
are unmodified in source.

## 29. Residual Risks

1. Live browser/tenant certification (AhramStore isolated products) still
   required before PASS — the sole remaining gate.
2. Working tree contains large pre-existing modifications from prior sessions
   (e.g., `ValidationService`, `ProductVariantsTab`, Storefront files); Wave 3
   files are a strict subset (§16) and all Wave 3-adjacent suites pass, but a
   full-suite green run is blocked by pre-existing HEAD failures (§28).
3. Simple products whose default variant is missing from a stale projection
   resolve from the unit price until reprojection — safe (today's value) but
   briefly stale after an online-price edit on lagging read models.
4. The `onlinePrice` authoring field is product-level: multi-unit simple
   products share one online price (per-variant granularity remains a
   variant-product feature by design).

## 30. Final Verdict

**CONDITIONAL PASS.** Implementation is complete on all three workstreams;
focused tests, builds, reconciliation-by-authority, tenant isolation, and Wave
1/2/2A regression gates pass, with every residual failure proven pre-existing
at HEAD. The condition is solely the explicitly unavailable live-environment
gate: real-browser certification on an isolated tenant (§20–§25) must be
executed when a browser + live tenant are available, after which this report
can be promoted to PASS without code changes. No commit made, per policy —
DISCOVER → DESIGN → IMPLEMENT → TEST → (BROWSER deferred) → RECONCILE →
REPORT → STOP.
