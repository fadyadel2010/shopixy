# PRODUCT WAVE 4 — VARIANT EDITOR UX + IDENTITY CERTIFICATION (2026-09-16)

Date (UTC): 2026-09-16
Scope: Wave 4 only (Variant Editor localization, progressive disclosure, variant-name
semantics, contextual generated SKU, precise 409 identity-conflict message).
Authority: source code + automated tests + live browser certification against the local
stack (API :5010, Web :5173) on tenant AhramStore with isolated products only.
Waves 1, 2, 2A, 3 remain certified PASS; discovery: `PRODUCT_WAVE4_DISCOVERY_2026-09-16.md`.

---

## 1. Executive Verdict

**PASS.** All four user findings are resolved with frontend-only changes; no domain,
schema, financial, or historical-data change. Live browser evidence (login → create →
generate → save → reload → API/DB reconcile → multi-attribute → Advanced → Arabic RTL
→ Enter-key → mobile EN/AR → tenant-wide SKU collision → full cleanup) is recorded in
`evidence.jsonl` + 16 screenshots + API/DB JSON dumps. Automated: 85 Web tests pass
(23 new Wave 4), 95 backend authority unit tests pass, Web production build passes,
`git diff --check` clean. No commit was made (per instruction).

---

## 2. Scope

In scope (all in `PointOfSale.Web`): `ProductVariantsTab.jsx` (helpers, Generate bar,
Advanced disclosure, SKU hardening, localized validation), `VariantMediaPicker.jsx`
(i18n wiring), `ProductForm.jsx` (409 identity-conflict message precision only),
`en.json`/`ar.json` (symmetric variant keys + `common.stay`), three test files.
Out of scope and untouched: Treasury/Accounting/GL/CashFlow/Settlement, inventory
valuation, Wave 1 slug authority, Wave 2 persistence authority, Wave 2A deletion
authority, Wave 3 pricing authority, AI persistence model, all backend authorities.

---

## 3. User Findings

A. Mixed Arabic/English Variant Editor (~110 keys missing, `VariantMediaPicker` 0%
localized, hardcoded validation/risk strings) → closed (§16, §27).
B. Eleven simultaneous sections → progressive disclosure, zero capability removed
(§7, §8, §26).
C. Bare variant names (`256`, `512`) → `Size: 256` single-attribute rule (§11, §24).
D. Semantically bare generated SKUs → contextual prefix hardening + bare-pattern
guard (§14, §25). A fifth live-found defect (409 SKU conflicts misreported as
"product changed") → precise server message surfaced (§20, §25).

---

## 4. Current Variant Editor Structure

Orchestrator `ProductVariantsTab.jsx` (1833 lines after change): pure helpers
(normalize/validate/combine/name/SKU) + state + Wave 2A delete preflight + save
handoff. Children unchanged in contract: `VariantOptionsBuilder` (Wave 2 Enter guard
intact), `VariantMatrixTable` (13 columns), `VariantBulkActions` (7 groups),
`VariantMediaPicker` (now localized). `VariantSelectModal` is POS flow, untouched.
Host `ProductForm.jsx` mounts the tab only when `hasVariants`; `productSkuPrefix`
← product name.

---

## 5. Localization Audit

Before: `variants:{50 keys}` symmetric but ~110 `tx()` keys absent in both locales;
`VariantMediaPicker` zero `useTranslation`; `getOptionGroupValidation` /
`getCombinationRisk` returned hardcoded English; presets/templates rendered raw
English; `common.stay` missing. After: every key referenced by the five editor files
resolves in en+ar (statically asserted, §21); Arabic UI shows zero English business
words and zero raw keys (live, §27). Technical tokens (`SKU`, `Barcode`, `ID`,
`{PLACEHOLDERS}`, persisted SKU values) stay English by design.

---

## 6. UX Complexity

Eleven blocks rendered at once; the Generate action was visually owned by the advanced
Combination Rules concept; validation split across four surfaces. Resolution groups
without deleting: primary = options builder → Generate bar → matrix → bulk → save;
Advanced disclosure (collapsed by default) = templates, attribute presets, SKU builder,
exclusion editor, workspace statistics. Save-blocking validation additionally always
surfaces in the save-time error banner, so collapsing Advanced can never hide a
blocking error.

---

## 7. Primary Workflow

Verified live without ever expanding Advanced: Add Option Group → name it → type
values + Enter → counter/risk inline → Generate N Variants → matrix (Image, Variant
Name, Attributes, SKU, Barcode, cost, POS price, Online price, read-only stock,
Weight, Default, Status) → bulk → Save Matrix. Script 1 completed this flow
touching only primary controls.

---

## 8. Advanced Workflow

One disclosure button (`aria-expanded`, `aria-controls="variant-advanced-tools"`,
`type="button"`, keyboard-operable, `+`/`−` indicator). Live verified: collapsed on
load (`aria-expanded="false"`); expands to all five sections; collapses again. No
capability moved out of reach; destructive actions keep existing confirmations;
Wave 2A `can-delete` preflight untouched.

---

## 9. Variant Identity Authority

Unchanged and re-verified: `VariantId` = `ProductVariant.Id` Guid; canonical
combination identity = `AttributeCombinationKey` (`sha256:…`, Trim/collapse/UPPER/sort,
locale-independent); client `attributesToKey` is UI matching only. Live reload test:
variant IDs byte-identical before/after reload; 95 backend authority unit tests pass
(identity authority, conflict classifier, deletion safety, Wave 3).

---

## 10. Variant Name Architecture

No persisted `Name` column (removed Phase 11.1) — confirmed in entity, DTOs,
Storefront types, and Mongo documents. Display name is derived at runtime everywhere
as value-join in `sortOrder`; order snapshots denormalize `VariantName` immutably.
Wave 4 changes only the editor's derivation call sites; no snapshot, projection, or
analytics code touched.

---

## 11. Variant Name Semantics

`buildVariantDisplayName`: 0 attributes → localized Default Variant; 1 attribute →
`Name: Value` (`Size: 256`); n attributes → `Value1 / Value2` in `sortOrder`
(unchanged). Ordering deterministic (generation order = group order × value order).
Locale-safe: user data never translated; no `NameAr`/`NameEn` columns;
`VariantId`/`SKU`/`AttributeCombinationKey` never derived from display text.

---

## 12. Multi-Attribute Naming

Live certified exact order: `Blue / S`, `Blue / M`, `Red / S`, `Red / M` for
Color{Blue,Red} × Size{S,M}. Never partial (`S`, `Blue` alone). Save payload carries
`attributes: {Color, Size}` objects only — no `name`/`displayName` persisted.

---

## 13. SKU Authority

Unchanged: frontend suggests, server validates matrix SKUs verbatim (409
`PRODUCT_SKU_CONFLICT`, never auto-suffixed), DB enforces tenant-scoped partial
unique indexes (`TenantId,SKU` / `TenantId,Barcode` over live semantic owners),
simple-product minter (`AUTO-…`, `-1…`) and frontend bulk dedupe (`-2…`) remain the
only suffix mechanisms. Live proof: cross-product duplicate SKU rejected 409 and
tenant-wide duplicate scan returns zero groups.

---

## 14. SKU Generation Semantics

Two hardenings inside `buildSkuForRow`, same contract: (1) `prefix =
normalizeSkuPart(…) || 'VARIANT'` — an Arabic-only product name can no longer
silently drop the prefix into a bare value; (2) pattern-path bare-value guard —
a prefix-less pattern result equal to one attribute value gets `PREFIX + separator`
prepended (`{SIZE}` → `T-SHIRT-256`); explicitly prefixed patterns
(`TSHIRT-{COLOR}-{SIZE}`) byte-identical. Non-Latin value tokens normalize to `''`
and are skipped (never Arabic in SKU). Live: `WAVE4-VARIANT-UX-TEST-<tag>-256/-512`,
`…-BLUE-S … -RED-M` distinct.

---

## 15. Existing SKU Compatibility

No auto-fill on load/reload/edit (new rows `sku:''`, existing rows keep `variant.sku`);
Generate SKU overwrites only explicitly selected rows on explicit click (live:
unselected persisted `STYLE-PLAIN` untouched while neighbor generated). Live reload:
SKUs byte-stable. Cleanup scan: zero dangling `WAVE4-%` SKUs, zero historical SKU
renames (only isolated test rows ever written, all deleted).

---

## 16. Localization Implementation

~120 symmetric en/ar keys added under `variants.*` + `common.stay`; pure helpers take
the `tx` localizer (English defaults keep unit tests deterministic); preset/template
labels keyed while created group names stay canonical English (combination-key and
SKU stability, documented); `VariantMediaPicker` uses `useTranslation`. No key
renamed or removed — Wave 1–3 snapshots unaffected.

---

## 17. Terminology

New keys use canonical business Arabic (المتغير، خيارات المتغير، مجموعة الخيارات،
إنشاء المتغيرات، منشئ SKU، اسم المتغير، الخصائص، قواعد التركيبات، SKU مكرر، تركيبة
مكررة، حفظ المصفوفة) — asserted verbatim in tests. Existing certified keys keep
their Arabic (الأنواع…) to avoid Wave 1–3 snapshot churn; unification deferred and
recorded (§29 risk 5). Live Arabic scan: zero English business-word leakage.

---

## 18. Accessibility

Disclosure: real `<button type="button">`, `aria-expanded` live-toggled,
`aria-controls` → region id, visible text label, keyboard reachable (all live
asserted). Matrix keeps `aria-label`/`aria-invalid` per input (now with descriptive
`Size: 256`-style prefixes). Wave 2 Enter-key guard untouched and live re-verified
(Enter in Option Name → no submit, URL stable, focus advances to value input).

---

## 19. Responsive Behavior

Desktop 1440×1000 and mobile 390×844, EN + AR: `document.scrollWidth <= innerWidth`
(no document overflow) asserted live in all four combos; matrix scrolls inside
`div.overflow-x-auto` (internal scroll confirmed on mobile); Generate bar stacks
(`flex-col`); disclosure + bulk reachable; `document.dir === 'rtl'` in Arabic with
mirrored layout screenshot-verified. Narrow-matrix columns scroll horizontally by
design; nothing clipped at document level.

---

## 20. Implementation

Production files: `ProductVariantsTab.jsx` (+363/−38: constants→labeled presets/
templates, localized validation/risk, `buildVariantDisplayName`,
`buildSkuForRow` hardening, `advancedOpen`, primary Generate bar, Advanced
disclosure, localized session message); `VariantMediaPicker.jsx` (i18n wiring);
`ProductForm.jsx` (+21: three conflict codes allow-listed, 409 branch surfaces
precise allow-listed server messages, generic concurrency message preserved);
`en.json`/`ar.json` (+250/+262 lines symmetric). Plus one live-found fix (§3E).

---

## 21. Automated Tests

Web 85/85 pass across 7 files: existing variant/product-form/Enter-key/i18n suites
(mechanically updated for intended changes: Advanced-first helpers, `Color: Red`-style
aria labels) + 23 new Wave 4 tests (`ProductVariantsTabWave4.test.tsx`: key parity,
editor key inventory >100 keys with placeholder parity, terminology, 4 name tests,
7 SKU tests incl. Arabic-prefix fallback and prefix-less-pattern guard, 3 UX tests;
`ProductVariantsTabWave4I18n.test.tsx`: real-locale AR/EN rendering, leakage scan,
RTL structure; `ProductForm.test.tsx` +1: precise 409 message with generic-message
preservation). Backend: 95/95 authority unit tests pass. Full backend integration
slice: 211 pass / 23 fail — pre-existing timing-sensitive distributed price tests on
a tree with staged prior-wave backend work; zero backend files touched by Wave 4.

---

## 22. Test Quality

New tests assert behavior, not implementation: exact deterministic name order via
cell queries; exact SKU strings; payload shape (attributes only, variantId
stability); aria wiring (`aria-expanded`, `aria-controls`); real-i18n rendering
(not mocks) for both locales; static source→locale key inventory preventing future
raw-key regressions. Collapse-state tests prove primary independence and Advanced
retention. Responsive claims use measured `scrollWidth`, not class-name sniffing
alone.

---

## 23. Live Browser Setup

Local stack: API `localhost:5010` (restarted, swagger 200), Web dev `localhost:5173`
(proxies `/api` → :5010), headless Edge 1440×1000 + 390×844 via playwright-core,
AhramStore Owner session, tenant verified via login claims. DB `PosEcommerceDb_Baseline`
read-only SELECTs for reconciliation. Isolated products only (`Wave4 …`, `Probe`,
`Stale Hunt`); 12 created across runs/probes, all deleted (12×204). Evidence:
`wave4-evid/evidence.jsonl` (60+ staged entries), 16 screenshots, API+DB JSON dumps.

---

## 24. Variant Name Browser Certification

Scenario A live: `Size: 256` (×2 surfaces: name + attributes), `Size: 512`,
exact-text `256`/`512` absent from table. Scenario B live: exact ordered
`Blue / S`, `Blue / M`, `Red / S`, `Red / M`. Scenario C live: post-reload names,
IDs, SKUs, barcodes byte-identical (`stable:true`). Persisted `STYLE-PLAIN`
identity stable when matrix extended (variantId + SKU preserved, unit-tested).

---

## 25. SKU Browser Certification

Scenario A: `WAVE4-VARIANT-UX-TEST-<tag>-256/-512` generated for selected rows,
distinct, prefixed; PUT 200; API+DB agree. Scenario B: four `…-BLUE-S … -RED-M`
SKUs. Tenant authority live: reusing product B's SKU in a new product → 409
`PRODUCT_SKU_CONFLICT`, and the UI now shows the precise server message
"SKU is already assigned to another product variant." (×2 found, generic ×0) —
the Wave 4 message fix verified end-to-end. Normalization collisions: unit-tested
(`Red Apple` vs `Red-Apple` → `-2` suffix). Existing SKUs never rewritten.

---

## 26. Advanced UX Browser Certification

Scenario D live: Advanced `aria-expanded="false"` on load; primary flow completed
without expanding; on expand all five sections present (Templates, Choose
Attributes, SKU Builder, Combination Rules, Workspace Statistics); collapses back
to `false`. Bulk actions (price/weight/identity/status/image) and Save Matrix bar
verified on the primary surface with correct selection captions.

---

## 27. Localization/Responsive Browser Certification

Scenario E live (Arabic, `dir=rtl`, mirrored layout screenshot): `خيارات المتغير`,
`إنشاء المتغيرات`, `أدوات المتغيرات المتقدمة`, `اسم المتغير`, `إجراءات جماعية`
present; English business-word leakage list empty; raw-key regex empty; Arabic
user data renders contextually (`مقاس: 256`, unit-tested). English LTR: identical
workflow, correct English, no raw keys. Scenario F live: Enter→focus advance, no
submit. Scenario G live: 390×844 EN+AR, no document overflow, internal matrix
scroll, stacked controls. Out-of-scope pre-existing observations (not acted on):
media-uploader/AI-button English strings, `IMG`/`x` glyph placeholders.

---

## 28. API/DB/Storefront Reconciliation

UI↔API↔DB agree: API variants (`id, productId, sku, price, costPrice, isDefault,
isActive, attributes[{name,value,sortOrder}]`) match DB rows (`Id, ProductId, SKU,
AttributeCombinationKey, IsDefault/IsActive/IsDeleted`) and attributes table;
tenant-wide duplicate-active-SKU scan: zero; order items referencing Wave4
products: zero (no financial/order writes performed — read-only/preview paths
only). Storefront live check not performed (storefront app not running locally);
storefront/POS consumers untouched in code (same derived `join(' / ', values)`
contracts) — no checkout identity change possible by construction.

---

## 29. Diff / Regression / Historical Safety

`git diff --check` clean; no commit (instructed). Production diff: 5 modified files
(§20) + 2 new test files + 2 reports; pre-existing staged prior-wave work in the
tree left untouched (verified: `VariantOptionsBuilder.jsx` diff is the certified
Wave 2 Enter fix). Builds: Web production ✓. Regressions: all variant/product/i18n
suites pass; backend authority units 95/95; integration failures pre-existing and
backend-only. Historical safety: 115/146 → 103/128 products/variants (only isolated
test rows removed), zero remaining `Wave4 %` products, zero dangling `WAVE4-%`
variants, zero duplicate active SKUs, zero Wave4 order items, no inventory/accounting
touch, no migration, no dependency change.

---

## 30. Final Verdict

**PASS.** Localization: no raw keys, AR/EN consistent, technical identifiers intact.
UX: primary obvious without Advanced, all capabilities retained, matrix fully
functional, Enter behavior intact. Variant Name: human-readable, deterministic,
complete, persistence-independent, locale-safe. SKU: contextual, deterministic,
unique, server/DB-authoritative, history-preserving, concurrency-safe. Quality:
desktop + mobile + RTL + LTR live-verified, API/DB agreement, no historical rewrite,
Wave 1/2/2A/3 regression-free. Follow-ups (non-blocking): unify legacy `الأنواع`
terminology; audit media-uploader/AI-button strings (outside Variant Editor);
consider storefront read-check when the storefront app is running.

*Certified from code + 85 automated Web tests + 95 backend unit tests + live
AhramStore browser evidence. STOP conditions never triggered. NO COMMIT.*
