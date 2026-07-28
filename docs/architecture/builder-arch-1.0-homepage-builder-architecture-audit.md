# BUILDER-ARCH-1 / ARCH-1.0 — Homepage Builder Architecture Audit

Initiative: Theme-Native Homepage Builder
Phase: ARCH-1.0 (Builder Architecture Audit — investigation only, zero code changes)
Date: 2026-07-27
Branch: feature/pearl-golden-theme (e6a0246)
Method: implementation is the only source of truth; every claim cited to file:line.

---

## 1. Executive Summary

**Primary question — who owns presentation? Answer: the BUILDER owns presentation inside its canvas, in direct violation of the Theme Platform.**

The merchant Homepage Builder (`PointOfSale.Web/src/modules/storefront/*`) does **not** render the theme runtime at all. It ships **name-alike shim modules** (`StorefrontLayout`, `ThemeContext`, `SectionRenderer`) inside `PointOfSale.Web/src/` that mimic the module names of the real theme runtime in `PointOfSale.Storefront`, but reimplement presentation as hardcoded Tailwind placeholders:

- Real runtime: `PointOfSale.Storefront/src/components/sections/SectionRenderer.tsx:48` resolves the actual theme component per section via `resolveSectionComponent(section.type, themeId)` (`ThemeSectionResolver.ts:29-76`, Vite `import.meta.glob('./*/sections/*.tsx')`).
- Builder shim: `PointOfSale.Web/src/components/sections/SectionRenderer.tsx:16-61` renders by **type-string sniffing** (`type.includes('hero')` → hardcoded slate/sky gradient; `includes('category')` → 4 gray boxes; everything else → generic white card). No theme resolution machinery exists anywhere in PointOfSale.Web (grep for `resolveSectionComponent|ThemeSectionResolver|ThemeLayoutResolver` → zero hits).

Consequences match the production observation exactly: two visual systems. The storefront renders Pearl Golden (Newsreader/Jost typography, `pearlTokens.ts` rhythm, 3/4.2 image ratios, hover zooms); the builder renders a synthetic slate/sky/indigo design system that no theme defines.

Two important nuances:
1. **Data/authority is NOT the problem.** All certification docs and architecture tests confirm the backend SQL `StoreConfigs` row owns theme/layout/sections/draft/publish truth, and the builder correctly admits backend DTOs (`admitWorkspaceConfig`, `If-Match` RowVersion). The gap is purely the presentation layer of the canvas.
2. **The storefront runtime already exposes the hooks needed for theme-native editing**: a signed preview mode (`?themePreviewToken` + `X-Theme-Preview-Token`, `bootstrap.ts:43-51`, `endpoints.ts:10`) and dormant editor affordances in the real SectionRenderer (`selectedSectionId` / `onSectionSelect` props with `data-section-id` attributes, `SectionRenderer.tsx:20-21,119-131`) that **no in-repo caller uses today**.

**Final certification: ARCHITECTURE UNDERSTOOD.** Migration to theme-native rendering is assessed as **Medium effort** (§13), dominated by one structural decision: cross-app component sharing vs. iframe-embedding the real storefront.

---

## 2. Current Builder Architecture

Two separate Vite applications, no shared runtime:

| App | Path | Role |
|---|---|---|
| PointOfSale.Storefront | `PointOfSale.Storefront/` | Public storefront; hosts the ONLY real theme runtime (registry, resolvers, theme packages, tokens/CSS vars) |
| PointOfSale.Web | `PointOfSale.Web/` | Admin/POS app; hosts the merchant builder AND super-admin homepage builder; contains shim copies of runtime module names |

- `PointOfSale.Web/vite.config.ts:24-28`: `@` aliases to `PointOfSale.Web/src` only. `package.json` has no dependency on the Storefront project; the only cross-project references in Web src are test files.
- Builder module layout (`PointOfSale.Web/src/modules/storefront/`): `pages/StorefrontBuilderPage.jsx` (orchestrator), `store/builderStore.js` (Zustand), `builder/sectionRegistry.js` (16 section definitions + settings schemas), `components/builder/{BuilderCanvas,BuilderTopBar,SectionPalette,SettingsPanel,SchemaSettingsForm,BuilderThemeProvider,PreviewModal}.jsx`.
- Shim modules inside PointOfSale.Web that impersonate the runtime:
  - `src/storefront/layout/StorefrontLayout.tsx` — 9 lines; bare `<div className="flex min-h-full flex-col bg-background text-text">` where `bg-background`/`text-text` are **undefined** in Web's `tailwind.config.js` (only a `primary` palette) → inert.
  - `src/storefront/themes/ThemeContext.tsx` — 23 lines; exposes only `{ themeId }` string. Applies no CSS variables, loads no fonts, resolves no components.
  - `src/components/sections/SectionRenderer.tsx` — 138 lines of synthetic previews (detail in §8).

---

## 3. Rendering Pipeline (AUDIT 1)

### 3.1 Storefront (reference pipeline — the real one)

```
Tenant URL
  └─ bootstrap()                    bootstrap.ts:91-238
       ├─ resolveTenant()                :132
       ├─ fetchStoreConfig()             :167   GET /api/storefront/store-config
       ├─ applyTheme(config.theme)       :195   → CSS vars on :root (cssVariables.ts:62-70)
       └─ store.setConfig(config)        :205   Zustand
  └─ <ThemeProvider>                ThemeContext.tsx:13-35 → resolveActiveTheme()
       └─ ThemeResolver.ts:29-53    dev override → storefrontTheme → registry default
       └─ ThemeRegistry.ts:17-43    modern | minimal | fashion | pearl
  └─ <StorefrontLayout>             layout/StorefrontLayout.tsx:15-66
       ├─ resolveHeaderComponent()  ThemeLayoutResolver.ts (glob ./{theme}/layout/header.tsx)
       ├─ {children = page}
       └─ resolveFooterComponent()
  └─ HomePage.tsx:34,50-51          config.pages.home.sections → <SectionRenderer>
  └─ SectionRenderer.tsx            normalize+sort (homepageSections.ts:41-58)
       └─ resolveSectionComponent(type, themeId)   ThemeSectionResolver.ts:29-76
            themed glob ./{themeId}/sections/{candidate}.tsx
            fallback   components/sections/defaults/{candidate}.tsx
       └─ <SectionErrorBoundary><ThemeComponent {...section.data}/></...>  → DOM
```

### 3.2 Builder (actual pipeline — synthetic)

```
/storefront/builder?page=home       StorefrontBuilderPage.jsx:103-106
  └─ loadPageData()                 :216-226  getStoreConfig() + getRuntimeStoreConfig()
       ├─ admitWorkspaceConfig()    :141-155  (rowVersion required, :128-131)
       └─ admitSections(config.pages[page].sections)  :171-186, :254
  └─ builderStore (Zustand)         store/builderStore.js:21-31
  └─ <BuilderCanvas>                BuilderCanvas.jsx:185-214
       └─ <BuilderThemeProvider>    BuilderThemeProvider.jsx:39-72 (pushes config into stub store)
            └─ <ThemeProvider>      Web SHIM ThemeContext.tsx — themeId string only
       └─ <StorefrontLayout>        Web SHIM — bare div, inert token classes
       └─ <SortableContext> → <SortableSectionCard> per section  :195-210
            ├─ overlay chrome (drag handle, toolbar) :47-96  ← mixed into card
            └─ <StorefrontSectionRenderer section=.../>  :101
                 = Web SHIM SectionRenderer.tsx:16-61 → SYNTHETIC PREVIEW → DOM
```

**Divergence point:** `BuilderCanvas.jsx:13-15` imports `BuilderThemeProvider`, `SectionRenderer`, `StorefrontLayout` from `@/...` — Web-local shims — instead of the Storefront runtime. Everything downstream of that import is a second presentation system.

Homepage JSON shape is shared: `{id, type, order, enabled, settings}` (`builderStore.js:83-89`; Storefront `types/storeConfig.ts:56-62`), normalized identically to `{...,data}` by each app's `homepageSections.ts`. So the DATA contract is unified; only rendering diverges.

Persistence: debounced 1.5s autosave → `PUT pages/{id}` with `If-Match` (`StorefrontBuilderPage.jsx:332-336,379-393`); publish `POST pages/{id}/publish` (:542-545); rollback (:584-588). Backend response re-admits sections (:348-351).

---

## 4. Provider Hierarchy

```
Builder page (PointOfSale.Web)
└─ StorefrontBuilderPage
   ├─ React-local state: storeConfig, previewConfig, activeId, save/load status (:69-94)
   ├─ DndContext (dnd-kit)
   ├─ BuilderTopBar / SectionPalette / SettingsPanel        (pure builder UI)
   └─ BuilderCanvas
      └─ BuilderThemeProvider (BuilderThemeProvider.jsx:39)
         │  normalizeStoreConfig() merge over DEFAULT_STORE_CONFIG (:7-37)
         │  setResolvedTenant(tenantId) (:52-54)
         │  useStoreConfigStore.setConfig(normalized) (:63)
         └─ ThemeProvider (SHIM)  → provides { themeId } only
            └─ StorefrontLayout (SHIM div)
               └─ SortableContext → SortableSectionCard[] → SectionRenderer (SHIM)
```

Storefront comparison: `App.tsx:158` mounts the real ThemeProvider AFTER `bootstrap()` has applied CSS variables to `:root` — the builder's provider chain never applies any variable, so even the theme's colors/typography tokens are absent from the canvas DOM.

---

## 5. Component Ownership (AUDIT 2)

| Component | Owner class | Evidence / Why |
|---|---|---|
| Header | Theme (storefront only) | `ThemeLayoutResolver.ts` glob `./{theme}/layout/header.tsx`; **absent from builder canvas entirely** (no header/footer sections in `sectionRegistry.js`) |
| Footer | Theme (storefront only) | same; builder edits footer via `cms/pages/StorefrontFooterSettings.jsx`, outside canvas |
| Hero | Theme (SF) / Builder (canvas) | SF: `pearl/sections/hero-banner.tsx`; canvas: hardcoded gradient `Web SectionRenderer.tsx:33-41` |
| Category Grid | Theme (SF) / Builder (canvas) | SF: `pearl/sections/category-grid.tsx`; canvas: 4 gray boxes `:43-52` |
| Product Grid | Theme (SF) / Builder (canvas) | SF: `pearl/sections/product-grid.tsx`; canvas: generic "Product Preview" card `:21-31` |
| Banner / Newsletter / Text / Video / Collection / Testimonials / Image / all others | Shared default (SF) / Builder generic card (canvas) | SF: `components/sections/defaults/*.tsx` (17 files); canvas: fallback white card `:54-60` |
| Product Card | Theme/shared (SF only) | inside theme/default product-grid components; canvas never renders one |
| Category Card | Theme/shared (SF only) | same pattern |
| Toolbar (top bar) | Builder-owned ✓ correct | `BuilderTopBar.jsx` (237 lines) |
| Selection border | Builder-owned ✓ but mixed-in | `BuilderCanvas.jsx:40-43` on the card wrapper |
| Inspector / Settings | Builder-owned ✓ correct | `SettingsPanel.jsx`, `SchemaSettingsForm.jsx` |
| Drag handles | Builder-owned ✓ but mixed-in | `BuilderCanvas.jsx:50-56` inside card header |
| Drop indicators | Builder-owned ✓ | empty-state dashed zones `:151-155,221-225`; dnd-kit `DragOverlay:229-246` |
| Section registry/schemas | Platform (builder side) | `builder/sectionRegistry.js` — 16 types + settings schemas |
| Page JSON / truth | Platform (backend) | SQL `StoreConfigs`, `AdminStoreConfigController` (see §11) |

Classification summary: editing chrome is correctly Builder-owned; **all section/layout presentation inside the canvas is Builder-owned when it should be Theme-owned**; truth is correctly Platform-owned.

---

## 6. Presentation Ownership (AUDIT 5) — Table 1

| Concern | Storefront owner | Builder canvas owner | Builder overrides theme? |
|---|---|---|---|
| Typography | ThemeConfig CSS vars (`cssVariables.ts:67-69`) + theme classes (Pearl Newsreader/Jost, `pearlTokens.ts:20-46`) | Hardcoded `text-4xl font-bold`, `text-xl font-semibold` (Web `SectionRenderer.tsx:37,46,57`) | YES — replaces |
| Colors | ThemeConfig → `--primary` etc. (`cssVariables.ts:62-66`, `tailwind.config.js:7-13`) | Hardcoded `slate-*`/`sky-*`/`indigo-*`; token classes `bg-background/text-text` undefined in Web tailwind | YES — replaces |
| Spacing / section rhythm | Theme (`PEARL_SECTION_PADDING='py-12 md:py-16'`, `pearlTokens.ts:20-23`) | `space-y-4 p-4 md:p-6` between cards (`BuilderCanvas.jsx:196`) | YES |
| Container width | Theme (`PEARL_CONTAINER='mx-auto max-w-7xl px-4'`) + SF tailwind container | `max-w-4xl mx-auto p-6` (`BuilderCanvas.jsx:187`) | YES |
| Cards / buttons / radius | ThemeConfig `--border-radius`, `.card` styles (`styles/index.css:181-187`) | `rounded-xl/2xl/3xl` hardcoded per card | YES |
| Section backgrounds | Section settings mapped by theme components (pearl `hero-banner.tsx:40-43`) | Ignored — settings colors never reach synthetic preview | YES (drops) |
| Hero height | Settings → theme mapping (`hero-banner.tsx:6-16`: small→`min-h-[70vh]`) | Fixed `py-12` gradient block | YES |
| Image ratio | Theme (`aspect-[3/4.2]`, pearl `category-grid.tsx:39-40`) | Fixed `h-20` gray boxes | YES |
| Hover effects / animations | Theme (`group-hover:scale-105 duration-700`; tailwind keyframes; Pearl marquee `AnnouncementMarquee.tsx:94`) | None | YES (drops) |
| Theme tokens | `applyTheme.ts:14-25` sets vars on `:root` | Never applied in Web app | YES (absent) |

Verdict: the Builder overrides or drops **every** presentation concern.

---

## 7. Theme Runtime Integration (AUDIT 3)

**Does the Builder use the same runtime rendering pipeline as the Storefront? NO — zero integration.**

| Runtime element | Storefront | Builder | Integration |
|---|---|---|---|
| ThemeRegistry | `ThemeRegistry.ts:17-43` (4 manifests, validated at load :59-82) | none (Web has only `themeMarketplaceRegistry.js` — catalog metadata, not components) | NONE |
| ThemeResolver | `ThemeResolver.ts:29-53` | shim ThemeContext computes `themeId = config.storefrontTheme || 'modern'` inline | NAME-ONLY |
| ThemeLayoutResolver | glob-based header/footer resolution + default fallback | absent | NONE |
| ThemeSectionResolver | glob-based per-theme section resolution + cache | absent | NONE |
| SectionRenderer | resolves real components, spreads settings as props, error boundary | type-string sniffing → placeholders | NAME-ONLY |
| StorefrontLayout | resolves themed header/footer around children | bare div with inert classes | NAME-ONLY |
| applyTheme / cssVariables | sets `:root` vars from tenant ThemeConfig | never invoked | NONE |

Exact divergence: `BuilderCanvas.jsx:13-15` (imports of the three shims). Theme identity survives as **data** (the builder receives and round-trips `storefrontTheme`, draft/live preset keys, override JSON via `AdminStoreConfigResponse` — `AdminStoreConfigDtos.cs:131-134`) but is never rendered.

---

## 8. Section Rendering Analysis (AUDIT 4) — per section type

Builder registry (`builder/sectionRegistry.js`) defines 16 types: hero-banner(:39), product-grid(:62), category-grid(:84), subcategory-grid(:102), text-block(:119), image-block(:138), video-block(:155), newsletter-signup(:171), testimonials(:190), featured-products(:208), product-hero(:229), product-gallery(:251), product-specs(:269), product-video(:297), customer-reviews(:316), related-products(:344).

| Section type | Canvas renders | Classification |
|---|---|---|
| hero-banner, product-hero | hardcoded slate/sky gradient block (Web `SectionRenderer.tsx:33-41`) | SYNTHETIC PREVIEW |
| product-grid, featured-products, product-gallery, product-specs, product-video, related-products | "Product Preview" card (:21-31) or generic card | SYNTHETIC PREVIEW |
| category-grid, subcategory-grid | 4 gray skeleton boxes (:43-52) | SYNTHETIC PREVIEW |
| text-block, image-block, video-block, newsletter-signup, testimonials, customer-reviews, banner | generic white card printing type + title (:54-60) | SYNTHETIC PREVIEW |

Notable dead code: every registry entry carries a richer `rendererPreview` component (e.g. `HeroBannerPreview`, `components/sections/HeroBanner.jsx:163`) that at least honors settings colors (:183-186) — **but the canvas never uses them**; the only consumer of `rendererPreview` is a JSDoc typedef (`types/builder.js:24`).

Result: 16/16 sections = Synthetic Preview. 0 Actual Theme Components. 0 Wrapped Theme Components.

PreviewModal ("DRAFT PREVIEW") is the same synthetic stack in a resized div (`PreviewModal.jsx:11-12,278-299`, fed by `/storefront/config/preview` via `storeConfigService.js:149-150`) — NOT an embedded storefront; no iframe/postMessage anywhere in the builder (only YouTube embeds: `SchemaSettingsForm.jsx:293`, `sections/VideoBlock.jsx:107`).

Super-admin homepage builder (`src/super-admin/homepage/`): `HomepageBuilderPreview.tsx:4-9,117` imports the SAME Web-local synthetic stack; its banner claims "Previewing the real storefront homepage renderer" (:130-131) — **false in practice**, the renderer is the shim.

---

## 9. Overlay Architecture (AUDIT 6)

| Editing element | Location | Separated or Mixed? |
|---|---|---|
| Selection border | card wrapper classes `border-indigo-500 ring-2 ring-indigo-200` (`BuilderCanvas.jsx:40-43`) | MIXED into presentation wrapper |
| Per-section toolbar (drag grip, hide, duplicate, delete, edit) | absolutely-positioned gradient bar INSIDE each card (`BuilderCanvas.jsx:47-96`) | MIXED — occludes top of section preview |
| Drag overlay | dnd-kit `<DragOverlay>` (`BuilderCanvas.jsx:229-246`) | SEPARATED ✓ |
| Drop indicator | dashed zones for empty canvas only (:151-155,221-225); reorder feedback = transform + `opacity-50` (:30-43) | MIXED (opacity on card) |
| Top toolbar | `BuilderTopBar.jsx` (pages, undo/redo, save state, preview/publish/rollback) | SEPARATED ✓ |
| Inspector | `SettingsPanel.jsx` right rail `w-80` (:22,52) → registry `editor` or `SchemaSettingsForm` (:42-48) | SEPARATED ✓ |
| Palette | `SectionPalette.jsx` (mounted `StorefrontBuilderPage.jsx:824`) | SEPARATED ✓ |
| Context menu | does not exist | n/a |
| Second selection style | `ring-4 ring-sky-200/75` inside Web SectionRenderer (:95) used by super-admin | MIXED |

Assessment: chrome around the canvas is properly layered; chrome **on sections** (header bar, border, disabled overlay `BuilderCanvas.jsx:110-117`) is welded into the same DOM as the section preview. In a theme-native design this must become a positioned overlay layer keyed by `data-section-id` (which the real SectionRenderer already emits — SF `SectionRenderer.tsx:125-126`).

---

## 10. State Ownership (AUDIT 7)

| State | Owner | Evidence |
|---|---|---|
| Page JSON truth (draft/live/versions) | Backend SQL `StoreConfigs.CustomizationJson` + `VersionsJson`/`LiveVersion` | draft cert `:3,111-113`; version cert `:37` |
| Workspace admission | Backend (`AdminStoreConfigResponse` + RowVersion); frontend gate `admitWorkspaceConfig` | `StorefrontBuilderPage.jsx:128-155`; entry cert `:41,64,72` |
| Sections (candidate) | `builderStore.js`: `currentPage, sections, selectedSectionId, isDirty, isSaving, isPublishing, isPreviewMode, undoStack, redoStack` (:21-31); actions `admitSections`(:55, backend hydration) vs `setSections`(:45, dirty candidates), add/remove/duplicate/reorder/updateSettings/toggle (:74-189), undo/redo (:211-242) | store file |
| Selection | `selectedSectionId` in builderStore (:24,:72) | |
| Hover | none in store — CSS `group-hover` only (`BuilderCanvas.jsx:66`) | |
| Drag | React-local `activeId` in `StorefrontBuilderPage.jsx:69-94` + dnd-kit internals | |
| Theme (identity + overrides) | Backend (`ThemePresetKey`, `ThemeLivePresetKey`, override JSON — `AdminStoreConfigDtos.cs:131-134`); builder holds a presentation candidate in `storeConfigStore` | theme cert `:17-19` |
| Preview | Backend `StorefrontPreviewService.GetConfigPreviewAsync` (draft-only, no live fallback); frontend `previewConfig` React state + `isPreviewMode` flag | preview cert `:3,60,69` |
| StoreConfig / ThemeConfig | Backend; builder-side copy in `storeConfigStore.ts` feeding the stub ThemeProvider | `BuilderThemeProvider.jsx:63` |
| Section settings | Candidate edits in builderStore (`updateSectionSettings:173`); acceptance at backend `UpdatePageLayoutAsync` canonicalization | content cert `:36,67` |

State architecture is CLEAN and certified: frontend = candidates + intent; backend = truth. No state changes are needed for a theme-native migration.

---

## 11. Current Architectural Philosophy (AUDIT 8)

**Verdict: Option A — Builder owns presentation; theme supplies (only) data. With a certified Option-B data plane.**

- Presentation: 16/16 synthetic sections (§8), hardcoded builder design system (§6), zero runtime integration (§7) → Option A.
- Data/authority: certifications and source-string architecture tests (`StoreBuilder*CertificationTests.cs`, `StorefrontThemeAuthorityCertificationTests.cs`) freeze "Builder is Presentation Only" w.r.t. TRUTH (end-to-end cert `:3,80-97`) → the intent of the platform is Option B ("Theme owns presentation; Builder overlays editing"), and the certified docs even say "Themes may render truth, but may not create or replace it" (P2-GC `:92-93`).
- Conclusion: the platform's *authority* philosophy is Option B; the builder's *implementation* is Option A. That contradiction IS the architectural concern raised in production. (Not Option C — there is no partial theme rendering to call a hybrid.)

---

## 12. Architectural Gaps (AUDIT 9) — Gap Map & Table 3

| # | Gap | Evidence | Severity |
|---|---|---|---|
| G1 | Duplicate rendering system: Web-local shim SectionRenderer/StorefrontLayout/ThemeContext duplicate runtime names with different behavior | Web `src/storefront/*`, `src/components/sections/SectionRenderer.tsx` vs SF equivalents | CRITICAL |
| G2 | Theme identity loss: no theme component, token, font, or CSS var reaches the canvas | §6, §7 | CRITICAL |
| G3 | Hardcoded builder presentation (slate/sky/indigo system) | Web `SectionRenderer.tsx:24-60`, `BuilderCanvas.jsx:187-196` | CRITICAL |
| G4 | Inert token classes: `bg-background`/`text-text` used but undefined in Web `tailwind.config.js:8-23` | shim `StorefrontLayout.tsx:4`, `PreviewModal.jsx:279` | HIGH (latent bug) |
| G5 | Section settings ignored visually: backgroundColor/height/alignment never affect canvas output | synthetic previews read only title/subtitle | HIGH |
| G6 | Dead `rendererPreview` components in registry — a third, unused presentation layer | `sectionRegistry.js` + `types/builder.js:24` | MEDIUM |
| G7 | Misleading label: super-admin preview claims "real storefront homepage renderer" while rendering the shim | `HomepageBuilderPreview.tsx:130-131` | MEDIUM |
| G8 | Overlay chrome mixed into section markup (header bar, borders) | `BuilderCanvas.jsx:40-96` | MEDIUM |
| G9 | Preview modal is synthetic, not the storefront, despite backend draft-preview endpoint existing | `PreviewModal.jsx` vs `?themePreviewToken` machinery | HIGH |
| G10 | No header/footer in the canvas at all — merchants never see theme layout while editing | sectionRegistry has no layout slots | MEDIUM |
| G11 | Section-type vocabulary drift risk: builder registry (16 types) vs SF defaults (17 files) vs alias map (`homepageSections.ts:15-20`); e.g. SF has `banner` default, builder registry doesn't expose `banner` | file lists §8 vs SF defaults list | LOW-MEDIUM |

### Table 3 — Theme Identity (Storefront vs Builder)

| Feature | Storefront | Builder | Match |
|---|---|---|---|
| Typography (Pearl: Newsreader/Jost) | theme classes + `--font-family` | hardcoded sans defaults | ✗ |
| Colors | tenant CSS vars | slate/sky/indigo | ✗ |
| Section rhythm | `py-12 md:py-16` | `space-y-4 p-4` cards | ✗ |
| Container | `max-w-7xl` | `max-w-4xl` | ✗ |
| Hero | `min-h-[70vh]..screen`, overlay, bg image | fixed gradient block | ✗ |
| Cards (product/category) | theme components, `aspect-[3/4.2]`, hover zoom | gray boxes / text card | ✗ |
| Header/Footer | themed | absent | ✗ |
| Animations | theme keyframes/marquee | none | ✗ |
| Section data contract | `{id,type,order,enabled,settings}` | same | ✓ |
| Theme identity data | live/draft preset keys | round-tripped correctly | ✓ |

---

## 13. Migration Readiness (AUDIT 10) — Table 4

**Overall estimate: MEDIUM effort.** The data plane, authority chain, preview endpoints, and editor hooks already exist; the work is confined to replacing the canvas presentation layer and layering the overlay.

Two viable target shapes (architecture only — no implementation recommended yet):

- **Shape 1 — Embedded storefront (iframe + postMessage).** Builder embeds the real PointOfSale.Storefront app using the existing draft-preview machinery (`?themePreviewToken`, `X-Theme-Preview-Token`, `/api/storefront/theme-preview/config` — `bootstrap.ts:43-51`, `endpoints.ts:10`, `httpClient.ts:198-200`) plus the dormant `selectedSectionId`/`onSectionSelect`/`data-section-id` hooks in the real SectionRenderer (`SectionRenderer.tsx:20-21,119-131`). Overlay stays in the parent, keyed by rect queries over `data-section-id`. Pros: perfect fidelity by construction, zero component sharing, respects the certified boundary (storefront receives only resolved presentation). Cons: postMessage bridge for selection/scroll/drag; draft push channel needed (today the preview token path renders saved draft, so live-typing feedback needs debounced save — which already exists at 1.5s).
- **Shape 2 — Shared component package.** Extract the theme runtime (registry, resolvers, defaults, theme packages, cssVariables/applyTheme) into a shared workspace package consumed by both apps; builder canvas mounts real ThemeProvider + StorefrontLayout + SectionRenderer and overlays editing chrome absolutely. Pros: in-process, instant candidate rendering. Cons: build coupling of two Vite apps, Tailwind config unification (SF token-mapped palette vs Web `primary` palette), data-fetch hooks inside sections (`useHomepageSectionProducts`) need builder-safe providers, and certification tests freeze source strings that would need re-certification.

### Table 4 — Migration Complexity

| Area | Complexity | Risk | Notes |
|---|---|---|---|
| Data contract / page JSON | LOW | LOW | already identical shape both sides |
| State & authority (store, RowVersion, admission) | LOW | LOW | untouched; certified |
| Canvas presentation swap | MEDIUM | MEDIUM | delete shims; adopt real renderer (Shape 1 or 2) |
| Overlay re-layering | MEDIUM | LOW | move chrome to positioned overlay over `data-section-id` rects |
| Theme token application in editor context | MEDIUM | MEDIUM | Shape 2 needs `applyTheme` scoping (canvas-scoped vars, not `:root`, to avoid restyling the admin shell); Shape 1 avoids entirely |
| Live draft feedback | MEDIUM | MEDIUM | Shape 1: save-then-refresh or postMessage config injection; Shape 2: direct |
| Section data fetching inside real components | MEDIUM | MEDIUM | product/category sections fetch live data; need preview/product-template stubs (`getProductTemplatePreviewProduct` already exists — `BuilderCanvas.jsx:16,181-183`) |
| Certification/tests update | LOW-MEDIUM | LOW | source-string tests (`builderWorkspaceAuthority.test.ts` etc.) will need re-freezing; authority semantics unchanged |
| Super-admin homepage builder | LOW | LOW | consumes same stack; inherits fix |

Readiness verdict: **READY to plan.** No authority, backend, or data-model obstacle exists; the migration is a frontend presentation-layer replacement with a clear divergence point (`BuilderCanvas.jsx:13-15`) and pre-built runtime hooks on the storefront side.

---

## 14. Recommendations (Architecture Only)

1. Adopt **Option B** formally: Theme owns 100% of canvas presentation; Builder owns only overlay chrome + candidate state. Prefer Shape 1 (embedded storefront via preview token + postMessage) as the fidelity-guaranteed default; evaluate Shape 2 only if in-canvas latency requirements demand it.
2. Delete the three shims (`Web src/storefront/*`, Web `SectionRenderer.tsx`) as part of the migration — they are the root cause and a naming hazard (G1).
3. Remove or repurpose the dead `rendererPreview` layer (G6) — do not migrate three presentation systems into two.
4. Activate the dormant editor hooks in the real SectionRenderer (`selectedSectionId`/`onSectionSelect`) as the selection contract; extend with hover if needed.
5. Correct the super-admin banner claim (G7) or fold that builder into the same target architecture.
6. Re-certify: add a new architecture certification (e.g. SF-ADM3.x "Builder Theme-Native Rendering") asserting the canvas imports the real runtime and contains no hardcoded section presentation.

---

## 15. Final Certification

**ARCHITECTURE UNDERSTOOD.**

- Primary question answered with evidence: the Builder owns canvas presentation via Web-local shims (Option A implementation) while the platform's certified philosophy and storefront runtime embody Option B.
- Divergence point precisely located: `PointOfSale.Web/src/modules/storefront/components/builder/BuilderCanvas.jsx:13-15`.
- All 10 audits completed; all diagrams/tables produced; every claim implementation-cited.
- No code was changed. Implementation must not begin until a BUILDER-ARCH-1 Phase ARCH-1.1 target-architecture decision (Shape 1 vs Shape 2) is approved.

Evidence sources: direct file reads plus three delegated deep-audit reports (storefront runtime, builder frontend, certification/authority model) archived under
`C:\Users\fadya\AppData\Local\hermes\cache\delegation\subagent-summary-{0,1,2}-20260727_213716_*.txt`.

Known evidence caveats: `StorefrontBuilderPage.runtime.test.tsx` does not exist despite appearing in earlier listings; stale Pearl files (PearlProductCard, pearl banner/newsletter/text-block sections) referenced elsewhere are absent from the working tree.
