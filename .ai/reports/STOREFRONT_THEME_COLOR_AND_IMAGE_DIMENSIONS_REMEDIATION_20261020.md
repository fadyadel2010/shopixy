# Storefront theme/color and image-dimension remediation — 2026-10-20

## A. Executive verdict

**CONDITIONAL — the shared Storefront color/appearance system and the shared media sizing system are consistent across all 10 supported runtime themes and both Appearance presets, verified by automated tests and headed-browser runs against the final code. It is not an unconditional certification of every legacy color consumer, image placement, or checkout state.** Final results and remaining exceptions are recorded below as verification completes.

Evidence: `.ai/evidence/storefront-theme-images-20261020/`. No tester-machine access, database replacement, staging, or commits.

## B. Baseline and root causes

### Actual configuration and architecture

- Admin: `http://localhost:5173/`; Storefront: `http://ahramstore.localhost:5174/`. Nissan was not used.
- Tenant: AhramStore, `c9c5c1ae-949e-498e-bc63-07242daeb49d`.
- Initial runtime theme: **Alizon** (`storefrontTheme=alizon`); legacy `themeId=default`.
- Initial draft/live color preset: **Default Modern** (`default`). Both materialized palettes: primary `#2563eb`, secondary `#0f172a`, background `#ffffff`, text `#020617`, muted `#64748b`; Inter, 16px, 8px radius.
- Initial concurrency RowVersion: **10663116**; StoreConfig history current version: **345**. These are distinct version mechanisms.
- `original-configuration.json` contains the read-only configuration, original serialized overrides, preset inventory, and history captured before mutations. `workspace-before.json` records existing work and SHA-256 hashes.

Canonical selectable runtime themes, from `theme-authority/shopixy-theme-authority.json`: **Modern, Minimal, Fashion, Pearl, Phoenix Sports, Hype Electronics, Alizon, Sharp Jewelry, Cosmetics, Seven Womenwear**. Experimental **Electronics** is disabled/hidden, not selectable. Its shared hero renderer was updated defensively but is not counted as a supported theme.

The two Colors & Typography **presets** are not two additional runtime themes: Default Modern and Dark Minimal. Dark Minimal defaults are primary `#3b82f6`, secondary `#1e293b`, background `#0f172a`, text `#f8fafc`, muted `#94a3b8`, Inter/16px/12px.

The existing contract has **one tenant Appearance draft/live branch shared across runtime themes**, not independently saved palettes per runtime theme. Projection order is canonical runtime-theme defaults → non-default color preset → customized legacy tenant values → explicit draft/live Appearance overrides. Existing merchant overrides intentionally remain authoritative when previewing another runtime theme. No per-theme database fields were invented.

Runtime resolution: `ThemeRegistry`/`ThemeResolver`/`ThemeContext` → layout/section resolvers → presentation-only theme renderers and shared commerce runtime. CSS projection is `theme/cssVariables.ts`/`applyTheme.ts`; accepted runtime refresh reprojects the complete token set.

Appearance editor: `ThemeEditorPage.jsx`. Save uses the supported overrides endpoint with `If-Match`; publication promotes Appearance only and creates history. Discard restores the **last accepted saved draft**, not necessarily live Appearance. Save failures preserve the local candidate; edit-generation protection preserves newer edits during in-flight saves. Marketplace preview links are signed/read-only. The authenticated draft iframe admits parent origin, nonce, and tenant; it is not a public draft endpoint. Version/history/concurrency implementation was retained.

### Confirmed causes

1. Tailwind colors were `var(--color-...)` strings without alpha placeholders. Computed baselines showed `/opacity` utilities such as `bg-background/95` and `border-muted/15` did not resolve as intended. Merchant palette changes therefore interacted with missing surfaces/borders.
2. Purchase profiles and several shared/theme components hardcoded white/light surfaces or dark text while other descendants consumed the merchant palette. Dark Minimal product pages visibly mixed white panels with light muted/body text. Alizon's hardcoded red header ignored Appearance primary changes.
3. Variant CSS consumed a separate, unpopulated naming system (`--primary-color`, `--text-color`, etc.), falling back to unrelated colors.
4. The Colors editor's illustrative preview assigned its header to primary and prices to secondary, unlike actual runtime roles. It did not render the actual Storefront. A real authenticated runtime preview is now included alongside accurately labeled role samples.
5. Product images commonly used `object-cover`, cropping products. Image elements remained in normal flow inside preferred-ratio wrappers; intrinsic/min-content contributions were not consistently eliminated. CMS content images explicitly used unbounded `h-auto`.
6. Headed post-change assertions exposed mobile gallery overflow: the thumbnail strip's min-content width widened an implicit mobile grid track. Explicit zero-minimum tracks and shrink-safe wrappers fix that shared layout cause.
7. Featured carousel children had only a minimum width, allowing content-dependent card sizing. They now have a stable responsive carousel-item contract.
8. The first settled contrast run exposed two further shared causes: themed category/product sections still painted a hardcoded light canvas (white/cream/slate) while their headings consumed Appearance's text role, so the Dark Minimal palette produced unreadable headings and 1:1 contrast; and shared badge/CTA components paired a primary fill with a literal `text-white` instead of the derived contrasting foreground.

### Reproduction limits, not claimed causes

The supplied iPhone 18 record is not present in the inspected AhramStore sample. Existing iPhone 17 Pro Max has **no media records**. Existing CERT Media products have media metadata, but legacy upload URLs fail in this development environment. The real homepage image is a portrait Amazon asset (1210×1500). These facts do not prove the original iPhone screenshot's exact runtime state. Extreme ratios are tested with explicitly identified task-owned image fixtures; no product records or original uploads are replaced.

`source-inventory.json` records all 41 image-related source paths found, theme defaults, and workflow details. Baseline screenshots include all 10 themes × both presets × desktop/mobile, plus product-detail views. Baseline detail sampling includes related-product images; it is not misrepresented as a populated iPhone gallery.

## C. Implementation

### Theme semantics

Stored roles remain primary, secondary, background, text, muted. Derived presentation-only roles are `on-primary`, `on-secondary`, `on-text`, readable primary/secondary **text** accents, surface, subtle, and border. No derived value is persisted over merchant colors.

- Primary/secondary fills preserve the chosen hex colors. Their contrasting foreground is selected independently.
- Accent text may use a readable shade against the page canvas; it does not change the saved fill.
- Missing/invalid fields use established fallback values. Every application writes the complete owned token/channel set, avoiding stale derived tokens.
- Text/muted contrast warnings are actionable and non-blocking; merchant-selected text colors are not silently rewritten.
- Runtime themes retain their layout, typography, and decorative identity. Explicit authored hero/announcement colors remain section-owned; changing page background must not silently overwrite them.

### Changed files and reasons

| Files | Reason |
|---|---|
| `theme-authority/appearanceRoles.ts` | Shared pure presentation/contrast policy used by Admin and Storefront. |
| `PointOfSale.Storefront/src/theme/cssVariables.ts`, `applyTheme.ts` | Complete semantic token and RGB-channel projection; remove redundant body-style writes. |
| `PointOfSale.Storefront/tailwind.config.js`, `src/styles/index.css` | Alpha-compatible semantic utilities; accessible shared focus/selection/badge roles. |
| `src/storefront/products/purchaseAppearance.ts`, `purchaseExperience.ts` | Preserve each purchase skin's geometry/type while binding its named color slots to Appearance; fix mobile min-content tracks. |
| `src/storefront/themes/hype/layout/AnnouncementMarquee.tsx` | The marquee derives its foreground from its own fill (authored fill/text still authoritative) instead of forcing white, so it stays readable on any merchant primary. |
| `src/components/theme-components/ThemeComponents.tsx` | Semantic shared headings/actions; centralized bounded product frame. Runtime-owned card actions explicitly opt out of theme-specific button colors so solid/outline/hover classes cannot collide. Badges pair each fill with its contrasting role foreground. |
| `src/components/common/Button.tsx` | Role-correct contrasting solid/outline actions. |
| `src/components/products/ProductCardRuntime.tsx`, `ProductCardRenderers.tsx` | Contained photos, semantic panel/price/action roles, stable shared card frame. |
| `src/components/products/ProductMediaViewer.tsx`, `ProductMediaGallery.tsx`, `ProductGalleryModal.tsx` | Bounded main viewer, contained thumbnails, min-width-safe scrolling, constrained fullscreen content. |
| `src/components/cart/CartItemRow.tsx`, `src/components/checkout/OrderSummary.tsx`, `src/components/search/{SearchModal,SmartSearchAutocomplete}.tsx`, `src/components/order/OrderItemsList.tsx`, `src/auth/components/OrderHistorySection.tsx`, `src/auth/pages/WishlistPage.tsx` | Keep existing square thumbnail bounds, prevent product cropping, and make wishlist source dimensions non-contributing. |
| `src/components/products/VariantSelector.css` | Consume actual Storefront variables; preserve existing option behavior and theme geometry. |
| `src/components/media/MediaFrame.tsx`, `media.css`, `index.ts` | Shared opt-in role-specific media containment; no global all-image crop rule. |
| `src/components/sections/heroBannerTypes.ts` | Typed authored hero contract, replacing existing `any` in edited hero renderers. |
| `src/storefront/themes/{modern,minimal,fashion,pearl,phoenix,hype,alizon,sharp,cosmetics,seven,electronics}/sections/hero-banner.tsx` | Bounded media and existing fit policies; OptimizedImage fallback/loading; bound full-bleed hero viewport scale. |
| `src/components/sections/defaults/{image-block,banner,featured-products}.tsx`, `src/components/cms/CmsPage.tsx` | Bounded authored/CMS/banner media; consistent carousel sizing. |
| `src/components/sections/defaults/{hero-banner,newsletter-signup}.tsx` | Primary fills use the derived contrasting foreground; the newsletter form uses role-based field colors and a typed props contract. |
| `src/storefront/themes/minimal/layout/{header,footer}.tsx` | Remove light-only neutral chrome conflicts. |
| `src/storefront/themes/alizon/layout/{header.tsx,alizonTokens.ts}` | Bind the current tenant's header/canvas/accent to Appearance; remove a previous important utility. |
| `src/storefront/themes/hype/layout/{header.tsx,footer.tsx,hypeTokens.ts}`, `src/storefront/themes/cosmetics/layout/{header.tsx,footer.tsx,cosmeticsTokens.ts}`, `src/storefront/themes/seven/layout/{header,footer}.tsx` | Correct additional verified hardcoded chrome/accent consumers; use contrasting role foregrounds without changing merchant fills. Seven keeps its primary-colored chrome; Cosmetics keeps secondary-colored footer/announcement roles. |
| `src/components/layout/Footer.tsx`, `src/storefront/themes/{pearl,sharp,phoenix,fashion}/layout/footer.tsx` | Increase verified low-contrast footer-label foreground opacity; preserve decorative theme identity. |
| `src/components/data/CategoryNav.tsx` | Remove assumed negative outer gutters and constrain category scrolling to its parent. |
| `src/storefront/themes/{modern,minimal,fashion,pearl,phoenix,hype,alizon,sharp,cosmetics,seven}/sections/{category-grid,product-grid}.tsx` | Put merchant body/muted/accent roles on the merchant canvas instead of a fixed cream/white/slate band, so Appearance background changes reach every themed section. The experimental `electronics` theme keeps its dark authored band. |
| `src/storefront/themes/fashion/sections/category-grid.tsx` | Remove the forced white heading override that made the section unreadable on the merchant canvas. |
| `src/storefront/commerce/CommerceBrowsingExperience.tsx` | Cosmetics canvas follows Appearance; allow its compact catalog heading to wrap. |
| `src/auth/pages/{LoginPage,RegisterPage}.tsx`, `src/auth/components/{LoginForm,RegisterForm}.tsx` | Palette-aware forms, fields, text, loading and footer surfaces. Existing registration behavior edits preserved. |
| `PointOfSale.Web/src/modules/storefront/components/AppearancePreview.jsx`, `pages/ThemeEditorPage.jsx` | Accurate semantic role sample, contrast warnings, real authenticated Storefront draft preview; persistence safeguards unchanged. |
| Theme/media/editor regression tests, task browser scripts and quality-closeout scripts | Behavioral, layout, preservation, workflow and evidence checks. |

Paths beginning `src/` in this table are under `PointOfSale.Storefront/`. The task-owned scripts use the `storefront-theme-images-20261020-` prefix. No backend source, schema, catalog, prices, or merchant asset files were edited.

### Image contracts

| Role | Contract | Fit |
|---|---|---|
| Product cards | Square; carousel items have stable width and cannot shrink by image content | Contain |
| Product main gallery | Square, centered width `min(100%,600px,65svh)` | Contain; existing intentional hover zoom remains clipped |
| Product thumbnails | Existing fixed square controls | Contain |
| Split hero | Theme's authored media ratio, maximum `min(560px,65svh)` | Existing contained themes retain contain; decorative themes intentionally cover |
| Full-bleed Fashion/Seven/Cosmetics hero | Responsive viewport-bounded minimum scale; absolutely positioned image cannot grow layout | Intentional cover, centered focal point |
| Banner | Stable 21:9 media area, max 400px | Intentional cover |
| CMS/content image | Supported authored ratio (safe 16:9 fallback), max 640px | Contain |
| Category media | Shared role supports 4:3 | Intentional cover; current theme category cards mostly use decorative art/icons rather than stored images |

Images are absolutely contained within explicit opt-in frames, preserving space before loading and after errors. OptimizedImage's lazy/eager loading, sizes, alt text, decode behavior and two-stage error fallback remain available. No source-file resizing or universal `cover` was used.

## D. Test evidence

### Automated quality gates

Latest completed results:

- Storefront: **593 passed / 74 files** (`storefront-final-regression-verified.log`). Includes theme/appearance, card/gallery, homepage/runtime, checkout and existing registration regressions. Added tests cover safe legacy-dark-canvas fallbacks, complete light/dark/light token replacement, untouched unrelated variables, gutter-independent category containment, per-theme section canvases, badge foreground pairing, announcement foreground derivation, and custom button/link palette isolation. The new action test was initially written for unavailable jsdom/Testing Library dependencies; it was corrected to use this project's existing server-rendered test pattern without adding dependencies. The shared default hero banner's pre-existing `any` typing was replaced with the shared `HeroBannerProps` contract while that file was already being edited.
- Admin Storefront modules: **367 passed / 39 files** with two workers (`admin-regression-serial.log`), re-run against the final code. Focused Appearance/marketplace/runtime-preview suite also passed. A concurrent earlier run hit two unrelated Domain-wizard timing failures; its log is retained, not hidden or fixed by changing Domain tests.
- Backend pure theme-token/preview-token suite: **17 passed**, no database provisioning (`backend-tests-flat.log`, TRX). Earlier isolated artifact layouts caused source-root discovery failures; flattening task-owned build output fixed the runner layout without editing test assertions.
- Additional backend source-authority suites: **7 passed, 1 failed** (`backend-architecture.log`, TRX). The retained failure is `CertificationArtifacts_MustRecordP2GcTruth`: historical `.ai/evidence/pos-admin-quality-20261015/browser-theme-live.json` is missing. No historical report/evidence/assertion was changed or fabricated to satisfy it.
- Admin production build/typecheck passed. Latest Storefront production build (including typecheck) passed (`storefront-final-build-verified.log`). Initial task-caused type/contract/test-harness failures were corrected; all intermediate logs remain.
- Focused JS/TS/JSX lint: **0 errors, 11 existing warnings** across 95 scoped files; includes task browser scripts and shared role policy. CSS has no configured dedicated lint gate. `git diff --check` passed for scoped implementation paths.

Only task-related source assertions were updated: Alizon/Hype now assert semantic roles rather than literal theme colors, the featured carousel now asserts fixed-width/shrink-safe classes rather than the old minimum-only inline width, the Hype marquee now asserts the Appearance-driven default instead of the orange token, and new tests assert per-theme section canvases, badge foreground pairing, and announcement foreground derivation. No unrelated historical assertions were weakened.

### Actual headed browser verification

Reused registered `opencode-ui-test`, headed Microsoft Edge, in-memory isolated profile. Production builds were completed. The local Storefront Vite process was subsequently restarted explicitly after browser CSS inspection proved its old Tailwind JIT process omitted newly configured semantic utilities despite current JavaScript/token values. Only the verified Node/Vite process listening on port 5174 was stopped; it was restarted from the primary workspace on the same strict port. The running API was not restarted or modified, and it was never modified at all by this task.

- `before-default-matrix.json`, `before-dark-matrix.json`: all 10 themes, actual signed backend previews, 1440×1000 and 390×844; DOM/style/natural-size evidence plus screenshots.
- `after-dark-matrix.json`: first full 190-record route/viewport/RTL run; caught shared mobile gallery overflow.
- `after-dark-final-matrix.json`: gallery overflow fixed; caught an intermittent Cosmetics catalog-heading overflow, subsequently given wrapping.
- Required viewport matrix: 1440×1000, 1280×800, 768×900, 390×844 on homepage/product detail; desktop/mobile on catalog, cart, checkout, login/register. Arabic direction is set through the actual language store and verified after refresh, not a spoofed DOM attribute.
- Fixture, workflow, public persistence, contrast, and final restoration results are appended during final verification below. Screenshots alone are not treated as assertions.
- `after-fixture-final.json`: **280/280 passed** — 10 themes × home/detail × desktop/mobile × portrait, landscape, square, transparent-padding, 12000×8000, failed and delayed image scenarios. Same-role frame dimensions remain identical across source ratios; delayed image completion does not change measured frame sizes; no horizontal overflow. The final harness explicitly waits for real product API completion and forces image loading only to test all rendered cards (including below-fold fallbacks). Earlier harness attempts compared skeleton/data-loading states or stalled lazy placeholders; those logs are retained and are not presented as application layout failures.
- A later fixture re-run on the final code stopped twice on page-load waits (a 30s `h1` visibility wait on Alizon mobile, then the Admin session restore exceeding the script's timeout while the datastore was slow) — both are harness timeouts, not sizing, overflow or layout assertions, and the first one carried an empty image diagnostic. The waits were raised and the run then completed **280/280** on the final code (`final-fixtures.json`).

No disposable replacement database was introduced. Database-provisioning integration suites were not launched; the backend suite above is pure and the appearance persistence workflow uses the configured development API.

## E. Configuration and data integrity

Original live Appearance and theme remain recorded in `original-configuration.json`. QA uses narrowly scoped Appearance draft writes, signed read-only previews, and the existing `If-Match` workflow. Any publication must be Appearance-only and is restored through the same supported versioned boundary; whole-store rollback is avoided so unrelated drafts/content cannot be overwritten.

No product catalog records, variant prices, original image sources, identity, domains, navigation, or homepage content records are changed to manufacture image cases. Fixtures are test-owned image bytes, explicitly separated from genuine asset results.

The workspace contains extensive pre-existing source edits, untracked evidence and tracked deleted test artifacts. These were not reset, stashed, cleaned, staged or committed. Hash verification identifies only two intentionally extended pre-existing files (`RegisterForm.tsx`, `RegisterPage.tsx`) plus this task's own script; existing registration behavioral changes remain and their tests pass. Final preservation/readback evidence is appended below.

## F. Remaining limitations / release scope

- Exact supplied iPhone 18/tall-upload behavior cannot be claimed reproduced where that record/image is absent. Failed development upload URLs are a separate asset availability problem, not repaired by changing catalog data.
- Theme-owned fixed decorative chrome and explicit section colors are not automatically remapped indiscriminately. Some legacy consumers outside the corrected shared paths still require a complete per-state accessibility audit; do not infer WCAG conformance from pure-role tests.
- Image/gradient overlay contrast requires pixel/manual review; DOM solid-background calculations cannot certify textured imagery.
- Nonempty, provider-specific checkout/payment/OTP/error states and purchase completion require separate explicit evidence; empty checkout rendering is not a completed purchase test.
- Banner/CMS/category roles not present in the tenant's current authored homepage cannot be represented as verified live placements merely because their shared component tests/build passed.
- Existing affiliate landing templates and receipt/proof-upload images are distinct roles; not certified by the product/homepage matrix.
- No claim is made about an unreproduced initial-load flicker.

## G. Rechecking and evidence navigation

From `PointOfSale.Storefront/`, run `npx vitest run --maxWorkers=2` and `npm run build`. From the repository root, run `node tests/quality-closeout/storefront-theme-images-20261020-lint.mjs` and `python tests/quality-closeout/storefront-theme-images-20261020-preservation.py --verify`.

The browser scripts require the existing authenticated **AhramStore** Admin session on Colors & Typography. Run them using the registered headed `opencode-ui-test` session. Default-palette scripts are read-only except local preview/temporary cart workflows; `dark-draft.js` explicitly saves a reversible Appearance draft and must be followed by `after-matrix.js`, whose `finally` restores the original exact draft with `If-Match`. Do not publish or blindly replay the publication workflow against a merchant store. Capture a fresh original configuration/history before any authorized future persistence test. `restore.js` is the standalone restoration path if a run is interrupted. `python tests/quality-closeout/storefront-theme-images-20261020-evidence-index.py` prints the pass/fail status of every evidence artifact, and `storefront-theme-images-20261020-closeout.py` regenerates `final-summary.json` and `configuration-integrity-final.json` from them.

Note for re-runs: when the datastore is slow, the Admin session restore can take over a minute, so the browser scripts wait generously for `#appearance-primary`. A timeout in that wait is a harness condition, not a product result.

Representative before/after screenshots (the JSON matrices, not these images alone, carry the assertions):

| Theme | Before mobile homepage | After Default mobile | After Dark Minimal mobile |
|---|---|---|---|
| Modern | [Before](../evidence/storefront-theme-images-20261020/before-default-modern-390-home.png) | [Default](../evidence/storefront-theme-images-20261020/after-default-modern-390-home.png) | [Dark](../evidence/storefront-theme-images-20261020/after-dark-minimal-modern-390-home.png) |
| Minimal | [Before](../evidence/storefront-theme-images-20261020/before-default-minimal-390-home.png) | [Default](../evidence/storefront-theme-images-20261020/after-default-minimal-390-home.png) | [Dark](../evidence/storefront-theme-images-20261020/after-dark-minimal-minimal-390-home.png) |
| Fashion | [Before](../evidence/storefront-theme-images-20261020/before-default-fashion-390-home.png) | [Default](../evidence/storefront-theme-images-20261020/after-default-fashion-390-home.png) | [Dark](../evidence/storefront-theme-images-20261020/after-dark-minimal-fashion-390-home.png) |
| Pearl | [Before](../evidence/storefront-theme-images-20261020/before-default-pearl-390-home.png) | [Default](../evidence/storefront-theme-images-20261020/after-default-pearl-390-home.png) | [Dark](../evidence/storefront-theme-images-20261020/after-dark-minimal-pearl-390-home.png) |
| Phoenix | [Before](../evidence/storefront-theme-images-20261020/before-default-phoenix-390-home.png) | [Default](../evidence/storefront-theme-images-20261020/after-default-phoenix-390-home.png) | [Dark](../evidence/storefront-theme-images-20261020/after-dark-minimal-phoenix-390-home.png) |
| Hype | [Before](../evidence/storefront-theme-images-20261020/before-default-hype-390-home.png) | [Default](../evidence/storefront-theme-images-20261020/after-default-hype-390-home.png) | [Dark](../evidence/storefront-theme-images-20261020/after-dark-minimal-hype-390-home.png) |
| Alizon | [Before](../evidence/storefront-theme-images-20261020/before-default-alizon-390-home.png) | [Default](../evidence/storefront-theme-images-20261020/after-default-alizon-390-home.png) | [Dark](../evidence/storefront-theme-images-20261020/after-dark-minimal-alizon-390-home.png) |
| Sharp | [Before](../evidence/storefront-theme-images-20261020/before-default-sharp-390-home.png) | [Default](../evidence/storefront-theme-images-20261020/after-default-sharp-390-home.png) | [Dark](../evidence/storefront-theme-images-20261020/after-dark-minimal-sharp-390-home.png) |
| Cosmetics | [Before](../evidence/storefront-theme-images-20261020/before-default-cosmetics-390-home.png) | [Default](../evidence/storefront-theme-images-20261020/after-default-cosmetics-390-home.png) | [Dark](../evidence/storefront-theme-images-20261020/after-dark-minimal-cosmetics-390-home.png) |
| Seven | [Before](../evidence/storefront-theme-images-20261020/before-default-seven-390-home.png) | [Default](../evidence/storefront-theme-images-20261020/after-default-seven-390-home.png) | [Dark](../evidence/storefront-theme-images-20261020/after-dark-minimal-seven-390-home.png) |

Other examples: [real local editor candidate](../evidence/storefront-theme-images-20261020/after-editor-live-local-preview.png), [portrait gallery fixture](../evidence/storefront-theme-images-20261020/after-fixture-alizon-detail-portrait.png), [restored public homepage](../evidence/storefront-theme-images-20261020/final-restored-storefront.png). Desktop, RTL, catalog, cart, checkout, auth and remaining fixture screenshots use the same descriptive naming in the evidence directory.

## Final verification addendum

- **Appearance workflows passed:** `workflow-and-restoration.json` records primary-only edits, save/reopen and actual signed runtime readback for all 10 themes, local discard, transport-failed save with the candidate retained, Appearance-only publication, actual public runtime tokens, and supported restoration.
- **Restoration confirmed:** initial history **345**, QA publication **346**, restored history **347**; initial RowVersion **10663116**, restored **11118831** at that readback. Both exact original serialized override strings match, both materialized palettes match, active Alizon matches, and every other configuration field matches except the expected concurrency/timestamp fields (`configuration-integrity.json`).
- **Local preview isolation passed:** `editor-preview-isolation.json` confirms unsaved primary `#6d28d9` reaches the real Storefront iframe, other color roles remain unchanged, Admin's own computed colors/root variables do not change, and Discard restores `#2563eb` without a write.
- **Latest Default Modern palette matrix passed:** `closeout-default-verified.json` contains **190 records, zero failures**; it is superseded by `final-default-matrix.json` above, which additionally asserts the live palette tokens per route.
- **Authored-content preview passed:** `content-preview-final.json` measures **80 scenarios** (10 themes × 2 widths × portrait/landscape/square/12000×8000 task-owned images) through the real authenticated Admin preview and the shared section renderer: identical frame dimensions across source ratios, `contain` fit, absolutely positioned media, bounded height, and no horizontal overflow. No content was saved or published and no catalog record was touched. The harness needed three fixes to be reliable (a `frameLocator` has no `evaluate`, the preview re-renders on every applied candidate, and element screenshots can detach); all three are harness-only changes.
- **Commerce checks passed to the shipping form:** `commerce-regression-final.json` records real UI add-to-cart for the existing simple product, square contained 80px cart media, populated actual checkout fields at desktop/mobile (no mobile overflow), real variant selection changing the media URL, and search modal results with contained 48px thumbnails. The original empty cart is restored. No order/payment was submitted and no catalog/stock/price records were edited.
- Initial contrast output (`contrast-default.json`) includes pre-settlement measurements during CSS transitions and some early fallback-layout renders; it is diagnostic, not the final accessibility verdict. Final runs wait for admitted theme configuration, fonts, and finite animations before measuring.
- A long-running Admin tab eventually returned `TENANT_REQUIRED` after its in-memory access token aged. Reloading through the application's existing authentication bootstrap restored the same AhramStore session without an API restart, credentials in evidence, or tenant-header bypass. Subsequent long matrices reload Admin between theme batches; earlier runner/screenshot/auth failures are retained as diagnostics.
- **Runtime stylesheet freshness:** a diagnostic Seven header had correct `--color-on-primary=#ffffff` but inherited dark text because `.text-on-primary` was absent from the old dev stylesheet. After the scoped Vite restart, the actual header computed white on the unchanged blue fill and the utility was present (`seven-foreground-{diagnostic,restarted}.png`). Pre-restart contrast data is not used to certify the final runtime.
- **Latest Default Modern palette matrix passed:** `final-default-matrix.json` contains **190 records, zero failures**, including all 10 actual selected themes, all required routes/viewports, and 10 Arabic refresh checks. Assertions compare document scroll width with actual client width, not the wider window including the scrollbar. Theme configuration, product data, fonts and finite animations are admitted/settled before measurement, and the live palette tokens are asserted against the editor's own values on every route. This supersedes earlier intermediate matrix attempts.
- **Latest Dark Minimal palette matrix passed:** `final-dark-matrix.json` contains **190 records, zero failures**, measured from a reversible Appearance **draft only** (no publication). The script restored the original draft in its `finally` block using the accepted `If-Match` value; the restored draft/live override strings, `default` preset, Alizon theme and history 347 are all recorded in the same file.
- **Final Default solid-background audit:** `contrast-default-final.json` checks **1,070 direct-text samples on 40 theme/routes** and reports **zero** solid-background exceptions. Five gradient/composited-background candidates remain flagged for manual pixel review. This is a DOM solid-background measurement only: it skips aggregate parent text and disabled controls and does not model every inherited opacity, placeholder, focus, hover or validation state, so it is not a blanket WCAG conformance claim.
- **Final Dark Minimal solid-background audit:** `contrast-dark-final.json` checks **984 direct-text samples on 40 theme/routes** with **zero** solid-background exceptions; ten gradient/composited-background candidates remain for manual review.
- **Contrast work driven by the audit.** The first settled Default run (retained as `contrast-default-pre-section-closeout.json`) still had 36 exception occurrences: muted category chips on the derived surface (4.37:1), the Hype orange announcement/duplicated marquee (3.14:1), and mixed badge foregrounds. The Dark Minimal run additionally exposed 55 exceptions where themed sections still used a hardcoded light canvas while their headings used Appearance's light text, plus a 1:1 heading on the merchant canvas. Fixes: themed category/product sections now use the merchant canvas (`bg-background`) with role-based headings, eyebrows, sub-titles and actions (`text-text`, `text-primary-text`, `text-secondary-text`); shared badges pair their fill with `text-on-primary`/`text-on-text`; the Hype announcement derives its foreground from its own fill (explicit authored text still wins); the category chip uses the page canvas; and the newsletter CTA uses `text-on-primary`. The Fashion category header no longer forces white text onto the merchant canvas. New regression tests assert section canvases and badge foregrounds per theme, and the announcement's foreground rule.
- The extra unsaved authored-content preview attempt initially waited on a detached/lazy image node and timed out (`content-preview.json`, `content-preview-retry.json`). It did not save/publish content or edit catalog records. The corrected harness then passed 80/80 as recorded above. Live CMS/banner placements still cannot be represented as verified in this tenant, because the tenant's authored homepage does not contain them; the shared contract has automated tests only.
- **Infrastructure outage during the Dark Minimal pass (external, not caused by this change).** The API began returning HTTP 500 for every endpoint. The API's own stack trace identified the cause as `StackExchange.Redis.RedisConnectionException` against the configured datastore host `192.168.1.5:6379` — that host had dropped off the network. The Dark Minimal run therefore failed at a Pearl route, and its automatic draft restoration failed with the same 500, leaving the tenant's Appearance **draft** on Dark Minimal. Live Appearance was never changed by that pass. After the datastore host returned, the exact original override string was written back through the supported `If-Match` overrides endpoint and verified byte-identical for both draft and live, with the original `default` preset and Alizon theme (`draft-restored-after-outage.json`, `draft exact match: true`, `live exact match: true`). History remained at 347. The failed run and the partial-state matrix are retained as `final-dark-matrix.json`; the outage window is not presented as a product defect.

## H. Closeout state

- **Final configuration readback:** `final-readback.json` shows the public AhramStore runtime on Alizon with the original palette (`primary #2563eb`, `secondary #0f172a`, `background #ffffff`, `text #020617`, `muted #64748b`, `on-primary #ffffff`), an empty cart, and no horizontal overflow. `configuration-integrity-final.json` reports **no** changed configuration field other than the expected concurrency/timestamp fields, with the exact original draft and live override strings and the original active theme.
- **Final verification totals on the final code:** Default matrix **190/190**, Dark Minimal matrix **190/190**, image fixtures **280/280**, authored-content preview **80/80**, Default contrast **0** solid exceptions, Dark Minimal contrast **0** solid exceptions, commerce workflow **passed**, public readback **passed**.
- **Workspace preservation:** `workspace-preservation.json` lists only `RegisterForm.tsx`, `RegisterPage.tsx` (intentionally extended with palette-aware styles only; existing registration behavior and its tests are intact) plus this task's own baseline script. Nothing was staged, committed, reset, or cleaned, and the extensive pre-existing modifications and untracked evidence in the workspace were left untouched.
- **All source changes remain uncommitted and unstaged** (`git diff --cached` is empty; the last commit is the pre-existing `5bd9bfc`), as required. `final-summary.json` records the SHA-256 of the 99 verified source files so a reviewer can confirm the evidence matches the code on disk.

## Verdict

**CONDITIONAL — the shared Storefront color/appearance system and the shared media sizing system are now consistent across all 10 supported runtime themes and both Appearance presets, with automated and headed-browser evidence on the final code. It is not an unconditional certification.** The remaining gaps are the ones listed in section F: absent live CMS/banner/category placements in this tenant, provider-specific checkout/payment states that were not exercised, pixel-level review of gradient/image backgrounds, the missing iPhone 18 asset, failing legacy upload URLs, and the external Redis outage that interrupted one Dark Minimal pass. The DOM solid-background contrast audits report zero exceptions on both presets, but that measurement does not model every inherited opacity, focus, hover, placeholder or validation state, so it is not a blanket WCAG conformance claim.
