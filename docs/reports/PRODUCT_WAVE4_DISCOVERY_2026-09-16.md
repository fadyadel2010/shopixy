# PRODUCT WAVE 4 — DISCOVERY REPORT (2026-09-16)

Date (UTC): 2026-09-16
Scope: Wave 4 only (Variant Editor UX + identity clarity: localization, progressive
disclosure, variant-name semantics, contextual generated SKU).
Authority: source code + domain contracts. Waves 1, 2, 2A, 3 certified PASS and are
not modified by discovery. No production writes were performed.

---

## 1. Executive Decision

1. **Feasible as frontend-only change. No migration. No domain rewrite.**
   All four findings (mixed language, section overload, bare variant names, bare
   generated SKUs) live in `PointOfSale.Web` (`ProductVariantsTab.jsx` +
   4 child components). No `Product`/`ProductVariant` schema change is required:
   there is deliberately **no persisted variant `Name` column** (removed in
   `DB/migrations/Phase11_1_ProductVariants.sql:26-35`), and every consumer
   derives the display name at runtime.
2. **Variant-name fix = display derivation only.** Change `getRowName`
   (`ProductVariantsTab.jsx:391-397`) so a single-attribute row renders
   `Name: Value` (e.g. `Size: 256`) while multi-attribute rows keep the canonical
   `Value1 / Value2` join (e.g. `Blue / S`). `VariantId`, `SKU`, and
   `AttributeCombinationKey` are untouched; ordering follows existing `sortOrder`.
3. **SKU fix = prefix hardening only, inside the existing builder contract.**
   The non-pattern path already prefixes (`PREFIX-value…`), but two holes produce
   bare value-only SKUs: (a) an Arabic/un-normalizable product name normalizes to
   `''` so the prefix silently disappears (`normalizeSkuPart` at `:67-71` +
   use at `:948`); (b) a custom pattern without a prefix token (e.g. `{SIZE}`)
   emits the raw value. Both are closed inside `buildSkuForRow` with no change to
   server/DB authority and no regeneration of persisted SKUs.
4. **UX simplification = progressive disclosure, zero capability removal.**
   Primary (always visible): options builder → generate bar → matrix → bulk →
   save. Advanced (collapsed disclosure, keyboard-accessible): templates, attribute
   presets, SKU builder, combination-rule exclusions, workspace statistics. The
   Generate action is lifted out of Combination Rules so the primary flow never
   requires opening Advanced.
5. **Localization = add the missing symmetric en/ar keys (~110) + wire
   `VariantMediaPicker` to i18n + localize the currently-hardcoded validation/risk
   strings via the existing `tx(key, fallback)` pattern.** No key renames, no
   namespace moves, so Wave 1/2/2A/3 keys are unaffected.
6. **STOP conditions not triggered.** No historical migration is needed (name is
   derived, SKUs are never auto-rewritten); SKU context (`productSkuPrefix` ←
   product name) is available; no capability must be removed; no unknown
   downstream consumer of a persisted name exists (there is no persisted name).

---

## 2. Current Variant Editor Structure

Orchestrator (all state + validation + generation + save handoff):

- `PointOfSale.Web/src/components/ProductVariantsTab.jsx:470,1405` —
  `forwardRef ProductVariantsTab({productId, forceMatrixMode, productSkuPrefix,
  onVariantDraftChange, onSaveVariantDraft})`, 1742 lines. Exposes
  `hasPendingChanges() / prepareProductAuthoringDraft() / getLastError()`
  (`:1373-1387`). Mounted by `ProductForm.jsx:1871-1878` inside
  `WorkspaceSection#product-variants`, only when `hasVariants`.
- Pure helpers at top of file (`:63-468`): `normalizeText`, `normalizeSkuPart`,
  `normalizeToken`, `attributesToKey` (client-side key, NOT the server key),
  `getOptionGroupValidation`, `getCombinationRisk`, `generateCombinations`,
  `buildMatrixRow`, `getRowName`, `buildSkuForRow` (`:946-977`), validation,
  bulk handlers (`:1107-1266`), Wave 2A delete preflight (`:1287-1329`).

Children / shared:

- `VariantOptionsBuilder.jsx:4-250` — option-group editor + counter/risk panel.
  Contains the Wave 2 Enter-key guard (`:112-127`, `:174-179`).
- `VariantMatrixTable.jsx:36-354` — matrix table (13 data columns + select).
- `VariantBulkActions.jsx:5-231` — bulk panel (price/weight/identity/status/image).
- `VariantMediaPicker.jsx:14-157` — shared gallery picker (used by matrix
  `:345-351` and bulk `:219-228`). **Zero `useTranslation` today.**
- `VariantSelectModal.jsx:18-186` — NOT the editor; POS sale/purchase picker.
- `utils/productAuthoringVariants.js:37-53` — `toProductAuthoringVariant()`
  payload mapper (no name generation; sends `sku/barcode/price…/attributes`).
- `services/variantStockApi.js:8-12` — inventory mutation only; editor shows
  stock read-only (Wave 2 certified).

Specs: `ProductVariantsTab.test.tsx:119-710`, `VariantOptionsBuilder.test.tsx:27`
(Enter-key), `admin/help/helpArticles/product-variants.md:1-47` (options →
matrix → price/SKU/barcode/status).

---

## 3. Visible Sections

In DOM order (`ProductVariantsTab.jsx:1405-1739`):

1. Header — `variants.productVariants` + `variants.matrixDescription` (`:1407-1412`).
2. Error banner (`:1414-1418`).
3. Variant Templates — `VARIANT_TEMPLATES` (`:14-20`): T-Shirt/Shoes/Electronics/
   Food/Furniture; `handleApplyTemplate` (`:866-877`).
4. 2-col grid: Choose Attributes — `ATTRIBUTE_PRESETS` (`:13`):
   Color/Size/Material/Capacity/Flavor (`:1445-1478`) + SKU Builder —
   `{prefix, separator, casing, pattern}` (`:498-503`), preview (`:1524-1535`).
5. `VariantOptionsBuilder` — group cards + risk/counter
   (`combinationCount :589-592`, limits SAFE 100 / INFO 300 / WARNING 1000).
6. 2-col grid: Combination Rules — exclusions + `Generate {{count}} Variants`
   (`:1553-1614`, handler `:922-944`) + Workspace Statistics — 9 tiles + Live
   Validation (`:1616-1649`, `rowValidationSummary :979-1046`).
7. Matrix zone — legacy banner (`:1654-1677`), `VariantMatrixTable`
   (`:1680-1689`), empty state (`:1698-1704`).
8. `VariantBulkActions` (`:1706-1712`).
9. Sticky Save Matrix bar, only with unsaved changes (`:1714-1735`).

Gating: `!productId → variants.saveProductFirst` (`:1389-1395`); `loading`
spinner (`:1397-1403`); `isMatrixMode = forceMatrixMode || groups>0 ||
variants>1` (`:603`).

Matrix columns (`VariantMatrixTable.jsx:111-158,230-335`): select | Image |
Variant Name (+New/Legacy badges) | Attributes (`Name: Value, …`) | SKU |
Barcode | Purchase Price | Selling Price | Online Price (Uses-POS-price
placeholder) | Available Stock (read-only + `stockManagedOutsideAuthoring`) |
Weight | Default | Status.

---

## 4. Shared Components

- `VariantMediaPicker` (matrix + bulk): dialog, gallery fetch
  `GET /admin/products/:id/media`, Primary/Selected badges, clear/done.
- `ProductMediaManager` (gallery source of truth, outside editor).
- `ConfirmDialogProvider` (large-matrix confirm `:930-941`, delete-selected
  confirm `:1249-1258`, navigation guard `:787-803`).
- `services/api.js` (variants CRUD, `/can-delete` preflight, barcode minter).
- `tx(key, fallback)` pattern (`t(key)===key ? fallback : t(key)`) in 4/6 editor
  files; `VariantSelectModal` uses `t()` directly (fully keyed).
- i18n config `src/i18n/config.js`: `en+ar`, `supportedLngs:['en','ar']`,
  `fallbackLng:'en'`; audit script
  `PointOfSale.Web/scripts/i18n-certification-audit.mjs` only regexes `\bt(`
  and therefore **does not see `tx()` usages** (blind spot for this wave).

---

## 5. Localization Inventory

Present: `en.json variants:{50 keys}` (`:1725-1776`) + `ar.json variants:{50 keys}`
(`:4692-4743`); key sets identical. `variantSelectModal:{8 keys}` both locales —
fully used, no gap.

Missing in BOTH locales (Arabic UI falls back to English `tx` fallback) —
~110 keys across:

- `ProductVariantsTab.jsx`: `unsavedChangesTitle`, `common.stay`,
  `exclusionNeedsTwoValues`, `largeGenerationConfirm`, `largeVariantMatrix`,
  `largeGenerationRequiresConfirmation`, `duplicateCombination`, `duplicateSku`,
  `duplicateBarcode`, `purchasePriceRequired`, `priceRequired`, `invalidWeight`,
  `invalidBulkValue`, `productSaveUnavailable`, `matrixDescription`,
  `variantTemplates(+Hint)`, `chooseAttributes(+Hint)`, `skuBuilder(+Hint)`,
  `skuPrefix`, `uppercase/lowercase/preserveCase`, `skuPatternPlaceholder`,
  `skuPreview(+Empty)`, `combinationRules(+Hint)`, `generateVariantCount`,
  `anyValue`, `addExclusionRule`, `workspaceStatistics`, `generatedVariants`,
  `missingPurchasePrice/SellingPrice`, `missingSku`, `duplicateSku/Barcode/
  CombinationsShort`, `liveValidation`, `legacyRowVisible/HiddenSingle/Multiple`,
  `hide/showLegacyRows`, `allLegacyRowsHidden`, `unsavedMatrixBanner`,
  `saveMatrixHint`, `saveMatrix` (+ new Wave 4 keys, §14).
- `VariantOptionsBuilder.jsx`: `optionBuilder(+Description/HowTo)`, `addOption`,
  `optionName(+Placeholder)`, `optionValues`, `optionValuePlaceholder`,
  `duplicateAttributeValues`, `emptyAttributeValues`, `variantsToGenerate`,
  `variantExplosionThresholds` (only `common.add/remove` exist).
- `VariantMatrixTable.jsx`: `variantName`, `weight`, `changeImage`,
  `selectImage`, `galleryImage`, `legacyImage`, `noImage`, `newVariant`,
  `legacyVariant`, `stockManagedOutsideAuthoring`.
- `VariantBulkActions.jsx`: all 26 captions (`oneSelected`, `countSelected`,
  `bulkActions`, `setCostPrice/setPrice/increaseByPercent/setWeight`,
  `identityTools`, `generateSku/Barcode(CountSelected)`, `variantStatus`,
  `variantImages`, `set/clearImage(CountSelected)`, …).
- `VariantMediaPicker.jsx`: 100% unlocalized (title, hint, badges, empty state,
  clear/done, load-failure message).
- Pure-function hardcoded English: `getOptionGroupValidation` messages
  (`:133,137,152,157`), `getCombinationRisk` labels/messages (`:230-260`),
  `getRowName` fallback `Default Variant` (`:393,461`), session-expiry message
  (`:1359`), `ATTRIBUTE_PRESETS`/`VARIANT_TEMPLATES` rendered raw (`:1438,1473`).

Terminology drift (editor vs POS): en `variants.price=POS Price` but code
fallbacks mix `Purchase Price / Cost Price / Selling Price / POS Price`;
ar `variants.*` uses `نوع/الأنواع` while POS/help use `المتغير`; SKU has 5+
Arabic renderings (`رمز المنتج`, `كود الصنف`, `رمز الصنف (SKU)`, bare `SKU`).
Attribute≠Option vocabulary is mixed (`Attributes / Choose Attributes /
Variant Options / Option Name / Combination Rules`).

Do-not-translate (kept): `SKU`, `Barcode`, `ID`, `API`, brand/provider names,
technical identifiers, persisted SKU values.

---

## 6. Variant Naming Architecture

- **No persisted `Name` field.** `Core/Entities/ProductVariant.cs:27-57` has
  `SKU/Barcode/AttributeCombinationKey/Price/OnlinePrice/CostPrice/IsDefault/
  IsActive/…` — no `Name/Title/DisplayName`. `ProductVariantDto`
  (`ProductVariantsController.cs:2181-2201`), Storefront DTOs
  (`StorefrontDtos.cs:65-134`, `product.ts:69-98`), and Mongo
  `ProductVariantDocument.cs:65-72` likewise carry attributes only.
- **Display name is derived everywhere as `join(' / ', values)** in
  `sortOrder`: `CommerceHubCheckoutService.cs:2332-2344`,
  `CommerceHubCartService.cs:1169-1181`, `CartService.cs:3599-3604`,
  `CheckoutSessionController.cs:724-735`, `checkoutStore.ts:318-329`,
  `OrderSummary.tsx:117-120`, `ProductVariantsTab.jsx:391-397` (`getRowName`),
  `VariantSelectModal.jsx:38-44`. Admin inventory shows
  `Product.Name + ' — ' + join(', ', values)` (`AdminInventoryService.cs:118-120`).
- **Order snapshots** denormalize `OrderItem.VariantName` (`Red / XL`) +
  `VariantAttributesJson` (`Core/Entities/OrderItem.cs:40-41,66-77`); projections
  checksum it (`CommerceHubOrderProjectionChecksum.cs:67`). Historical snapshots
  are immutable by design.
- **Anomaly (out of Wave 4 scope, not to be “fixed” here):**
  `AdminCommerceHubController.cs:722` exposes `AttributeCombinationKey`
  (`sha256:…`) as `VariantName` in one admin read path.
- Consequence for this wave: single-attribute rows render the bare value
  (`256`, `512`) because the join has one element. The fix must be display-only.

---

## 7. SKU Generation Architecture

- **Frontend (suggestion only):** `buildSkuForRow(row, index)` (`:946-977`):
  `prefix = normalizeSkuPart(skuBuilder.prefix || productSkuPrefix || 'VARIANT')`
  (`ProductForm.jsx:1875` passes `formData.name` as `productSkuPrefix`);
  tokens `{PREFIX, INDEX, NORMALIZED_ATTR_NAME → normalized value}`; pattern path
  with `{TOKEN}` substitution or default `[prefix, …valueParts].join(separator)`;
  casing upper/lower/preserve; preview of first 2 rows (`:1048-1051`).
- **Holes producing bare SKUs:** (a) Arabic/non-Latin product name →
  `normalizeSkuPart` returns `''` → prefix silently empty → `256`; (b) custom
  pattern without a prefix token (e.g. `{SIZE}`) → raw value `256`.
- **Server (authority):** matrix SKUs are taken **verbatim** and collisions throw
  `PRODUCT_SKU_CONFLICT` 409 — never auto-suffixed
  (`ProductAuthoringOrchestrator.cs:1645-1687,1827-1861`). Only the simple-product
  path mints (`ResolveUniqueSkuAsync`: `AUTO-<id>` fallback, `${base}-${n}`
  loop, `ProductVariantCloneImportAuthority.cs:14-46`).
- **Frontend bulk dedupe** (`:1187-1221`): stable matrix `{INDEX}`, overwrite of
  selected rows only, case-insensitive `-2, -3…` suffix vs server/DB
  case-sensitive `Ordinal`/collation with `-1, -2…` — frontend is a convenience;
  server+DB win races (`23505 → 409`,
  `ProductAuthoringDatabaseConflictClassifier.cs:12-35`).

---

## 8. Identity Authority

- `VariantId` = `ProductVariant.Id` (Guid, `BaseEntity.cs:6-8`), assigned
  `Guid.NewGuid()` on create; Mongo `VariantId`, cart/checkout events, and
  `OrderItem.VariantId` all reference it. **Wave 4 must never change it.**
- Canonical combination identity =
  `ProductVariantAttributeCombinationKey.Compute()` (`Core/Helpers/…:6-62`):
  trim → collapse whitespace → `ToUpperInvariant` → sort Ordinal → length-prefixed
  payload → `sha256:<64hex>` (71 chars, `MaxLength=80`); empty set → `null`
  (single default). **Locale-independent by construction.**
- Client `attributesToKey` (`:80-89`, `name=value|…` lowercased, sortOrder-based)
  is a UI matching key only — never persisted, never compared server-side.
- Intra-request guards: `ProductAuthoringVariantIdentityAuthority.cs:29-94`
  (duplicate combination/SKU/barcode, Ordinal, trim, skip blank).

---

## 9. SKU Authority

- EF canonical (`ProductVariantConfiguration.cs:94-113`,
  `ApplicationDbContext.cs:1858-1865`, snapshot + baseline migration):
  `IX_ProductVariants_SKU UNIQUE (TenantId, SKU)
  WHERE IsDeleted=false AND SemanticLifecycleState=0 AND SKU IS NOT NULL`
  (barcode analogous); `(TenantId, ProductId, AttributeCombinationKey)` unique;
  `(TenantId, ProductId, IsDefault)` unique. Products mirror
  `IX_Products_TenantId_SKU`. NULLs unlimited; deleted/superseded rows free the
  value by design.
- Every ownership check is tenant-scoped (`TenantId` leading column;
  `CloneImportAuthority.cs:65-106`, `Orchestrator.cs:1988-2080`).
- Server validation: intra-request duplicates → 409; cross-entity ownership →
  409 `VariantIdentityConflict`/authoring conflict codes; `DbUpdateException`
  `23505` → typed 409 (`ProductAuthoringDatabaseConflictClassifier`).
- Legacy drift (informational): `DB/snapshots/schema-23-7-2026.sql` non-unique
  `(SKU) WHERE IsDeleted=false` is superseded by the tenant+semantic EF model;
  `TestAuthController.cs:670-707` remediates live drift. No action for Wave 4.

---

## 10. Existing Product Data Compatibility

- Persisted SKUs are **overwritten only via explicit authored values**
  (`SetVariantIfChanged(SKU, authoredVariant.Sku)`, `Orchestrator.cs:1859-1861`)
  or intentional lifecycle transitions (merge/split clears, delete frees).
  Wave 4 adds **no auto-fill on load/reload**: new rows keep `sku:''`
  (`buildMatrixRow :435-450`), existing rows keep `variant.sku`
  (`:417-434`), drafts merge over base (`:452-456`).
- Reload/edit stability: rows re-match by client key → `VariantId`-keyed rows
  (`getCombinationKeyFromVariant :383-389`); option-structure edits orphan rows
  into hidden-by-default legacy rows that are **excluded from submit**
  (`submittedMatrixRows :734-736`, Wave 2 Finding 4) — Wave 4 keeps this.
- Legacy compatibility: `parseVariantNameAttributes` (`:288-314`) still parses
  old `Name`-style strings into `Option N` attributes; `canReuseLegacyDefaultVariant`
  (`:399-401`) reuses the legacy default slot. Untouched.
- Wave 2A delete protection (`can-delete` preflight, fail-closed) is the gating
  authority for omissions — untouched.
- Simple-product mirror (`product.SKU ← default variant`, Wave 3 pricing
  authority) is untouched; variant-matrix SKUs never flow into it.

---

## 11. UX Complexity Analysis

Eleven blocks render simultaneously (§3): templates, presets, SKU builder,
options builder, generate (buried inside Combination Rules), exclusions, 9-tile
statistics + live validation, legacy banner, 13-column matrix, 7-group bulk
panel, save bar. Cognitive costs:

- A first-time user must parse builder mechanics, SKU pattern tokens, exclusion
  semantics, statistics tiles, identity tools, and legacy cleanup **before**
  generating the first variant.
- The single most important action (**Generate**) is visually owned by an
  advanced concept (**Combination Rules**).
- Validation is split across builder messages, statistics tiles, inline row
  tinting, and the save-time banner — the save-time banner is the only one that
  can never be hidden.
- Nothing can be deleted to fix this (all of it is certified capability);
  grouping + collapsed-by-default Advanced is the only compatible tool.

---

## 12. Primary Workflow

Always visible, top-to-bottom, no Advanced interaction required:

1. **Define options** — `VariantOptionsBuilder` (name group → type values →
   Enter/Add; counter + risk + blocking messages inline).
2. **Generate** — primary Generate bar (`Generate {{count}} Variants`,
   large-matrix confirm preserved, blocked-state explanation).
3. **Review / edit** — matrix (Image, Variant Name, Attributes, SKU, Barcode,
   cost, POS price, Online price, stock read-only, Weight, Default, Status;
   horizontal scroll on narrow screens) + `VariantBulkActions` + sticky
   Save Matrix bar.

---

## 13. Advanced Workflow

One collapsed disclosure — `Advanced Variant Tools` (`aria-expanded`,
keyboard-operable, never auto-opened):

- Variant Templates · Choose Attributes (preset shortcuts) · SKU Builder
  (prefix/separator/casing/pattern + preview) · Combination Rules (exclusion
  editor; Generate lives in primary) · Workspace Statistics (+ Live Validation
  summary; row-level + save-time validation stay in primary).

Destructive/protected actions (delete-selected with history preflight,
legacy cleanup) keep their existing confirmations and are never silent.

---

## 14. Proposed Localization Model

- Keep `tx(key, fallback)` + `t(key)===key` semantics; **add the missing keys
  symmetrically** in `en.json`/`ar.json` under `variants.*` (+ `common.stay`).
  No renames, no removals. `VariantMediaPicker` adopts `useTranslation`.
- Pure helpers (`getOptionGroupValidation`, `getCombinationRisk`) receive the
  `tx` localizer (defaulting to identity English for tests) instead of returning
  hardcoded English.
- Preset/template **labels** get keys (`attributePreset_Color…`,
  `template_Electronics…`); the **created group names stay canonical English**
  (`Color`, `Size`…) because they flow into `AttributeCombinationKey`
  normalization and SKU tokens — Arabic display text must never enter SKU
  semantics (§16). Documented, not silent.
- Terminology for NEW keys follows the wave spec (المتغير / خيارات المتغير /
  مجموعة الخيارات / قيمة الخيار / إنشاء المتغيرات / منشئ SKU / اسم المتغير /
  الخصائص / قواعد التركيبات / SKU مكرر / تركيبة مكررة); existing certified keys
  keep their current Arabic (الأنواع…) to avoid regressing Wave 1–3 snapshots —
  full terminology unification is deferred and recorded as a risk (§17).
- Technical tokens stay English: `SKU`, `Barcode`, `ID`, `API`, `{PLACEHOLDERS}`,
  persisted SKU values, brand names.

---

## 15. Proposed Variant Name Model

- `buildVariantDisplayName(attributes, fallback)` (renamed semantics of
  `getRowName`, same call sites):
  - 0 attributes → `fallback ?? Default Variant` (localized `variants.defaultVariant`).
  - 1 attribute → `Name: Value` (e.g. `Size: 256`) — the Wave 4 clarity fix.
  - n attributes → `Value1 / Value2 / …` in `sortOrder` (e.g. `Blue / S`,
    `Blue / M`, `Red / S`, `Red / M`) — byte-identical to today.
- Deterministic: input order is `sortOrder` (generation order = option-group
  order × value order); no locale input (user-entered names/values only).
- Locale-safe: no persisted `Name`/`NameAr`/`NameEn` columns; Arabic UI renders
  the same canonical string (user data is not translated); `VariantId`, `SKU`,
  `AttributeCombinationKey` never derived from display text.
- Storefront/POS already render `join(' / ', values)` from attributes; a
  single-attribute storefront label gains context via `Product.Name + variant
  label` composition at the call site (no snapshot change). No order-snapshot,
  projection, or analytics change.

---

## 16. Proposed SKU Model

- Keep the certified contract: frontend suggests, server validates verbatim,
  DB enforces tenant-scoped uniqueness, existing suffix authorities
  (`-2…` frontend bulk, `-1…` server simple-minter) stay the only dedupe
  mechanisms.
- Two minimal hardenings inside `buildSkuForRow`:
  1. `prefix = normalizeSkuPart(…) || 'VARIANT'` — an Arabic-only product name
     (or empty prefix) can no longer silently drop the prefix.
  2. Bare-value guard on the pattern path: if the result contains no separator
     and equals a single attribute-value token (case-insensitive), prepend
     `PREFIX + separator`. Explicit prefixed patterns (`TSHIRT-{COLOR}-{SIZE}`)
     are byte-identical to today.
- Never derive SKU from translated labels; non-Latin value tokens normalize to
  `''` and are skipped (stable, readable, collision-safe via existing suffix).
- Generate-SKU still overwrites **only explicitly selected rows** on explicit
  click; load/reload/edit never auto-fills; persisted SKUs are never renamed.

---

## 17. Risks and Compatibility

1. Single-attribute display names change (`256` → `Size: 256`) in the editor
   matrix + row-derived aria-labels. Persisted data identical; two existing test
   expectations (`Red: …` labels) must be updated — mechanical, no behavior loss.
2. Moving Generate/presets/templates/statistics into primary+Advanced changes
   DOM order (exclusion `select` indices) and requires tests to expand Advanced
   first. No handler/validation logic moves.
3. Collapsed Advanced must not hide save-blocking validation: Live Validation
   summary duplicates the save-time banner content; row tinting + banner stay in
   primary. Verified by test.
4. Frontend SKU casing/collation stays case-insensitive-stricter than
   server/DB (pre-existing, Wave 2 certified) — unchanged by this wave.
5. Terminology split (existing `الأنواع` vs new `المتغير`) is a known,
   documented inconsistency; unifying old keys is deferred to avoid Wave 1–3
   snapshot churn.
6. No migration, no API change, no Treasury/Accounting/GL/CashFlow/Settlement/
   inventory-valuation touch, no slug/persistence/deletion/pricing/AI-model
   change.

---

## 18. Acceptance Matrix

| # | Requirement (wave spec §20) | Discovery verdict | Implementation pointer |
|---|---|---|---|
| 1–4 | en/ar keys resolve, no raw keys, no EN leakage in AR | ~110 keys missing; closable symmetrically | §14 + new `*.test` |
| 5–9 | descriptive deterministic names; order stable; locale-safe; history stable | derived-only; §15 rule | `getRowName` → `buildVariantDisplayName` |
| 10–12 | no bare `256`; distinct contextual SKUs; normalization collisions | two holes found; closable in builder | §16 hardenings |
| 13–17 | existing SKU preserved; suffix authority; server 409; tenant scope; concurrency | authorities intact; no new system | regression tests only |
| 18–21 | primary usable w/o Advanced; Advanced retained; matrix functional; Enter-key intact | §12/§13 split; guard untouched | UX tests |
| 22–25 | desktop/mobile, RTL/LTR | responsive via existing Tailwind + horizontal table scroll | manual cert matrix |
| A–G | browser scenarios (names, SKUs, reload, Advanced, i18n, Enter, mobile) | no backend dependency; isolated products | live cert (AhramStore) |
| — | UI↔API↔DB agreement; no historical rewrite | no persistence change by construction | reconciliation queries |

---

*End of Wave 4 discovery. Implementation may proceed within §§12–16 only.*
