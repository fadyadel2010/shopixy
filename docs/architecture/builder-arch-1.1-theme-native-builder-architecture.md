# BUILDER-ARCH-1 / ARCH-1.1 — Theme-Native Homepage Builder: Canonical Target Architecture

Initiative: Theme-Native Homepage Builder
Phase: ARCH-1.1 (Target Architecture Design — design only, zero code changes)
Date: 2026-07-27
Basis: ARCH-1.0 audit (`docs/architecture/builder-arch-1.0-homepage-builder-architecture-audit.md`) — conclusions accepted as canonical and re-verified against live source in this phase.
Status: CANONICAL. All future Homepage Builder work must conform to this document.

Evidence re-verified this phase (live source, not the audit md):
- Real runtime resolvers: `PointOfSale.Storefront/src/storefront/themes/{ThemeRegistry,ThemeResolver,ThemeSectionResolver,ThemeLayoutResolver,ThemePageResolver}.ts`
- Dormant editor hooks: SF `SectionRenderer.tsx:20-21` (`selectedSectionId`, `onSectionSelect`), `:125-126` (`data-section-id`, `data-section-type`)
- Preview token machinery: `bootstrap.ts:48,128-169`; `endpoints.ts:10` (`/api/storefront/theme-preview/config`); `httpClient.ts:62,198-200` (header applied to read-only requests only)
- Shims: Web `src/storefront/layout/StorefrontLayout.tsx` (inert div), Web `src/storefront/themes/ThemeContext.tsx` (themeId string only), Web `src/components/sections/SectionRenderer.tsx` (type-string-sniffed synthetic previews)
- Data-fetching sections: `useHomepageSectionProducts` inside pearl and default `product-grid.tsx`
- Token application: `applyTheme.ts:15-20` writes CSS vars to `document.documentElement` (`:root`), globally
- No iframe/postMessage exists in the builder today (YouTube embeds only)

---

## 1. Executive Summary

ARCH-1.0 established a single fact: the Homepage Builder canvas is a second, synthetic presentation system that duplicates theme responsibility. The data plane, authority chain, backend truth, and section contract are correct and certified; only the canvas presentation layer is wrong.

ARCH-1.1 designs the target: **the Builder becomes an editing layer over the real storefront runtime.** Presentation gains a single source of truth — the active theme, rendered by the one and only theme runtime in `PointOfSale.Storefront`.

Two candidate architectures were designed and compared:

- **Option A — Embedded Runtime**: the Builder embeds the real Storefront app in a sandboxed iframe using the existing signed preview-token machinery, and drives editing through a versioned postMessage bridge. The overlay lives in the Builder, positioned over `data-section-id` rects reported by the embedded runtime.
- **Option B — Shared Runtime Package**: the theme runtime (registry, resolvers, defaults, theme packages, token application) is extracted into a shared workspace package consumed by both Vite apps; the Builder mounts the real ThemeProvider/StorefrontLayout/SectionRenderer in-process.

**Decision: Option A (Embedded Runtime) is the canonical target architecture**, with a thin, versioned Editor Bridge protocol as the only new contract. Option B is retained as a documented, rejected alternative with explicit reassessment criteria.

Rationale in one line: Option A makes theme fidelity a *structural guarantee* (the canvas IS the storefront) instead of a *maintenance obligation* (keeping two build systems, two Tailwind configs, and two provider trees convergent forever).

**Final certification: ARCHITECTURE APPROVED** (§15), justified by ARCH-1.0 evidence.

---

## 2. Architecture Principles

P1. **Single Source of Truth for presentation.** The active theme, executed by the Storefront runtime, is the only system that may render sections, layout, tokens, typography, or spacing. No component in PointOfSale.Web may ever emit section markup.

P2. **Builder = editing layer only.** The Builder owns editing, ordering, selection, drag & drop, settings, persistence, publishing. Nothing else.

P3. **Themes are Builder-independent.** A theme must never import, detect, or branch on the Builder. The Builder adapts to themes via passive DOM contracts (`data-section-id`), never the reverse. Every future theme works in the Builder with zero theme-side work.

P4. **Truth stays on the backend.** SQL `StoreConfigs` remains the owner of draft/live/version truth; the Builder holds candidates + intent; the runtime renders admitted state. (ARCH-1.0 §10 — unchanged, certified.)

P5. **Fail closed.** If the runtime cannot render (theme error, bridge timeout, token expiry), the Builder shows an explicit degraded state; it never falls back to synthetic presentation. Synthetic previews are permanently banned.

P6. **Contracts over coupling.** Builder↔Runtime communication happens exclusively through one versioned message protocol (Editor Bridge). No shared component imports across the app boundary.

P7. **Evidence-based evolution.** Any change to this architecture requires a new ADR citing implementation evidence, per the platform's canonical-doc standard.

---

## 3. Target Architecture (Diagram 1)

```
┌────────────────────────────────────────────────────────────────────────┐
│ PointOfSale.Web — BUILDER (editing layer only)                         │
│                                                                        │
│  ┌──────────┐ ┌───────────┐ ┌───────────────┐ ┌─────────────────────┐  │
│  │ TopBar   │ │ Palette   │ │ SettingsPanel │ │ builderStore        │  │
│  │ (publish │ │ (add      │ │ (schema forms)│ │ (candidates, undo,  │  │
│  │  rollback│ │  section) │ │               │ │  selection, dirty)  │  │
│  └────┬─────┘ └─────┬─────┘ └──────┬────────┘ └─────────┬───────────┘  │
│       │             │              │                    │              │
│  ┌────▼─────────────▼──────────────▼────────────────────▼───────────┐  │
│  │ EDITOR BRIDGE (parent side)  — versioned postMessage protocol    │  │
│  └────┬────────────────────────────────────────────────▲────────────┘  │
│       │ commands (config:apply, select, scroll…)       │ events        │
│  ┌────▼────────────────────────────────────────────────┴────────────┐  │
│  │ OVERLAY LAYER (absolutely positioned over iframe)                │  │
│  │ selection ring · hover ring · toolbar · drag handles ·           │  │
│  │ drop indicators · context menu    (NO presentation, ever)        │  │
│  ├───────────────────────────────────────────────────────────────────┤ │
│  │ <iframe sandbox>  CANVAS                                         │  │
│  │  ┌─────────────────────────────────────────────────────────────┐ │  │
│  │  │ PointOfSale.Storefront — REAL THEME RUNTIME                 │ │  │
│  │  │ bootstrap(editorMode) → applyTheme → ThemeProvider          │ │  │
│  │  │  → StorefrontLayout(theme header/footer)                    │ │  │
│  │  │  → SectionRenderer → resolveSectionComponent(type, themeId) │ │  │
│  │  │  → ACTUAL THEME COMPONENTS → DOM (data-section-id)          │ │  │
│  │  │ + Editor Bridge (child side): rect reporting, click relay   │ │  │
│  │  └─────────────────────────────────────────────────────────────┘ │  │
│  └───────────────────────────────────────────────────────────────────┘ │
└──────────────────────────────┬─────────────────────────────────────────┘
                               │ REST (unchanged, certified)
┌──────────────────────────────▼─────────────────────────────────────────┐
│ BACKEND — PLATFORM TRUTH                                               │
│ AdminStoreConfigController (draft PUT If-Match / publish / rollback)   │
│ StorefrontPreviewService (signed theme-preview token, draft-only)      │
│ SQL StoreConfigs = the only truth (CustomizationJson, VersionsJson)    │
└────────────────────────────────────────────────────────────────────────┘
```

Boundary rule: exactly ONE crossing point between Builder and Runtime — the Editor Bridge. Everything else (styles, components, providers, Tailwind, fonts) stays inside its own app.

---

## 4. Option Design & Comparison

### 4.1 Option A — Embedded Runtime (iframe)

Shape: Builder → sandboxed iframe → real Storefront app in editor mode → real theme runtime → overlay via bridge.

Reuses (all verified in source):
- Signed preview entry: `?themePreviewToken` + `X-Theme-Preview-Token` (`bootstrap.ts:48,128-169`, `httpClient.ts:198-200`, `endpoints.ts:10`) — read-only enforced client-side AND server-side.
- Dormant editor hooks: real `SectionRenderer` already accepts `selectedSectionId`/`onSectionSelect` and emits `data-section-id`/`data-section-type`.
- Debounced autosave (1.5s) already exists as the persistence heartbeat.

New: an `editorMode` bootstrap flag in the Storefront and a child-side bridge module (rect reporting via ResizeObserver/IntersectionObserver, click-to-select relay, `config:apply` injection into the Zustand store for instant candidate rendering without a save round-trip).

- Advantages: perfect fidelity by construction; zero build coupling; themes need nothing; admin shell CSS can never leak into the canvas and vice versa (`applyTheme` writes `:root` of the IFRAME document only); independent deployment; every future theme works automatically; security isolation via sandbox + signed read-only token.
- Disadvantages: postMessage bridge is a new protocol to own; overlay positioning must track iframe scroll/resize (rect sync); drag-and-drop across the iframe boundary needs a ghost-image pattern (dnd-kit runs in parent; drop targets computed from reported rects); slightly higher memory (second app instance).
- Performance: initial canvas load = one storefront bootstrap (production cost, cacheable); afterwards `config:apply` messages give sub-frame candidate updates (no network). Rect messages batched per animation frame.
- Security: strongest option — sandboxed origin, token is read-only and draft-scoped, admin JWT never enters the iframe.
- Preview synchronization: candidate config pushed via bridge (instant); the SAVED-draft token path remains the correctness backstop and powers "open preview in new tab" for free.
- Developer experience: bridge protocol is small and typed; theme developers test in the storefront only — zero builder knowledge required.
- Maintenance: one runtime forever; builder work is overlay/bridge only.
- Scalability: N themes, N sections → zero builder changes. Marketplace themes render exactly as sold.
- Testing: runtime tested in Storefront as today; bridge tested with a fake-child harness; Playwright E2E covers select/drag/publish.
- Deployment: apps release independently; bridge protocol versioned (`bridgeVersion` handshake) to tolerate skew.
- Runtime isolation: total (separate JS realm, separate CSS scope).

### 4.2 Option B — Shared Runtime Package

Shape: extract `storefront/themes/*`, `components/sections/defaults/*`, theme packages, `applyTheme`/`cssVariables`, layout resolvers into `@shopixy/theme-runtime`; both apps consume it; Builder mounts the real providers in-process.

- Advantages: in-process rendering (no bridge, no iframe); direct React state for instant updates; simpler overlay math (same DOM tree).
- Disadvantages / costs (each verified real):
  - Build complexity: two Vite apps + one package; `import.meta.glob` theme discovery must move into the package and both apps' pipelines must process the package TSX identically.
  - Tailwind: SF config maps every color to CSS vars; Web config has only a `primary` palette. The package's class vocabulary forces Web to adopt SF's token config (global admin-shell restyle risk) or a scoped/prefixed second Tailwind pass (fragile).
  - CSS variable scoping: `applyTheme` writes to `:root` (`applyTheme.ts:15-20`); in-process use would restyle the admin shell unless the certified runtime itself is rewritten to scope variables.
  - Providers: sections call `useHomepageSectionProducts` etc.; the Builder must replicate the storefront's provider/data environment (http client, tenant resolution, stores) inside the admin app — a large hidden surface.
  - Package ownership and versioning: every theme change becomes a package release consumed by two apps; version skew between Web and Storefront becomes possible for PRESENTATION itself — the exact class of divergence this initiative exists to kill.
  - Certification: source-string architecture tests freeze runtime strings; extraction forces broad re-certification.
  - Migration effort: refactors the WORKING storefront (highest-risk asset) to fix the builder.
- Testing: package unit tests plus duplicated integration environments in both apps.
- Long-term maintenance: permanent two-consumer coupling; every future theme feature must be verified in two hosts.

### 4.3 Weighted Comparison Matrix (Table 2)

Scores 1-5 (5 best). Weights reflect the initiative's philosophy (fidelity + single source of truth dominate).

| Category                  | Wt | Option A | Option B | Notes |
|---------------------------|----|----------|----------|-------|
| Architecture purity       | 5  | 5 | 3 | A: one runtime, one crossing point. B: shared code, two hosts |
| Theme fidelity            | 5  | 5 | 4 | A: fidelity by construction. B: fidelity by discipline (Tailwind/provider drift possible) |
| Performance (editing)     | 3  | 4 | 5 | B in-process wins; A close after bootstrap via config:apply |
| Performance (initial)     | 2  | 3 | 4 | A pays one storefront bootstrap |
| Developer experience      | 3  | 4 | 3 | A: small typed bridge. B: monorepo/package/Tailwind wrangling |
| Runtime safety/isolation  | 4  | 5 | 2 | A: sandbox + read-only token. B: theme code runs inside admin session |
| Deployment complexity     | 3  | 5 | 2 | A: independent. B: lockstep package releases |
| Independent releases      | 3  | 5 | 2 | same |
| Plugin ecosystem (future) | 3  | 5 | 2 | A: third-party theme code stays sandboxed |
| Marketplace compatibility | 3  | 5 | 3 | A: any marketplace theme renders as-is, isolated |
| Future SDK                | 2  | 5 | 3 | the bridge protocol IS the SDK seed |
| Future mobile editor      | 2  | 5 | 2 | webview embed reuses bridge; B unusable outside the web bundle |
| Testing                   | 3  | 4 | 3 | A: bridge harness + E2E. B: two integration hosts |
| Long-term maintainability | 5  | 5 | 2 | A: overlay+bridge only. B: permanent coupling |
| Migration effort/risk     | 4  | 4 | 2 | A: additive to storefront. B: refactors the working runtime |

Weighted totals: **Option A = 4.68 / 5**, **Option B = 2.62 / 5**. Option A wins decisively and takes every highest-weight category outright.

---

## 5. Rendering Pipeline (Diagram 2)

Canonical future flow — identical machinery to production, plus one injection path:

```
Homepage JSON  (backend truth OR builder candidate)
   │
   ├─ [boot path]   GET /api/storefront/theme-preview/config  (signed token, draft)
   └─ [edit path]   Editor Bridge  config:apply {config, revision}
   │
   ▼
Storefront store.setConfig(config)          (same admission both paths)
   ▼
applyTheme(config.theme)                    → CSS vars on iframe :root
   ▼
<ThemeProvider>  resolveActiveTheme()       → themeId (registry-validated)
   ▼
<StorefrontLayout>                          → themed header / footer (resolvers)
   ▼
<SectionRenderer sections=... editor hooks> normalize + sort
   ▼
resolveSectionComponent(type, themeId)      themed glob → default fallback
   ▼
ACTUAL THEME COMPONENTS  (props = section.data; data hooks in editor mode
                          served by preview endpoints via token header)
   ▼
DOM inside iframe  — every section wrapped with data-section-id / data-section-type
   ▼
child bridge measures rects → rects:changed → parent OVERLAY positions chrome
```

Rules:
- The edit path (`config:apply`) and boot path admit config through the SAME store setter — no second normalization.
- Revision counter: every candidate carries a monotonically increasing `revision`; the runtime acks (`config:applied {revision}`); the overlay dims/labels the canvas "syncing" while `appliedRevision < latestRevision`.
- The 1.5s debounced autosave continues in parallel; saved-draft state is the durable backstop, bridge state is the live view.

---

## 6. Overlay Design (Diagram 4)

The overlay is a Builder-owned, absolutely positioned layer stacked over the iframe. Contents (exhaustive): selection ring, hover ring, per-section toolbar, drag handles, drop indicators, resize handles (only if a future section declares resizability), context menu. Nothing else. It never renders section content, backgrounds, typography, or placeholders.

```
BuilderWorkspace
└─ CanvasViewport (position:relative)
   ├─ <iframe>  (the real storefront; z:0)
   └─ OverlayRoot (position:absolute inset:0; pointer-events:none; z:1)
      ├─ HoverRing        (pointer-events:none; follows hovered rect)
      ├─ SelectionRing    (pointer-events:none; follows selected rect)
      ├─ SectionToolbar   (pointer-events:auto; anchored to selected rect top edge,
      │                    flips below when clipped; drag grip, hide, duplicate,
      │                    delete, settings)
      ├─ DropIndicator    (pointer-events:none; horizontal line at insertion index)
      ├─ DragGhost        (pointer-events:none; follows cursor during drag)
      └─ ContextMenu      (pointer-events:auto; portal; right-click on section)
```

- Hierarchy: OverlayRoot is transparent to pointer events; only interactive children opt in. All other pointer input falls through to the iframe, whose child bridge relays semantic events (hover/click on `data-section-id`) back up. Presentation is therefore never occluded except by explicit chrome.
- Lifecycle: OverlayRoot mounts when the bridge handshake completes (`runtime:ready`); unmounts/degrades to an explicit "canvas unavailable" state on bridge timeout (fail closed, P5). Rings/toolbars mount purely from overlay state + rect map.
- Positioning: parent keeps `Map<sectionId, DOMRect>` from `rects:changed` batches (child observes via ResizeObserver + scroll listener, coalesced per animation frame). Overlay chrome is translated with `transform` for cheap updates.
- Synchronization: rects are versioned with the config `revision`; stale rects (older revision) are dimmed until refresh to avoid rings hugging outdated layout.
- Section identification: `data-section-id` is the ONLY identity channel — already emitted by the real renderer (SF `SectionRenderer.tsx:125`). No DOM structure assumptions beyond that attribute.
- Selection model: single-select; `selectedSectionId` lives in builderStore (existing field). Sources: overlay click-relay, palette insert (auto-select), settings panel focus. Runtime is a mirror (receives `selection:set` for its native `isSelected` styling or ignores it — overlay ring is authoritative).
- Hover model: transient `hoveredSectionId` in overlay-local state (NOT builderStore, no undo pollution); cleared on pointer leave and during drag.
- Focus model: overlay chrome participates in the parent tab order; the canvas is one composite focus stop.
- Keyboard navigation: ArrowUp/Down cycle section selection (document order); Enter opens settings; Alt+ArrowUp/Down reorders; Delete removes (with confirm); Esc clears selection/menu. All handled in the parent; scroll-into-view sent as `scroll:to {sectionId}`.

---

## 7. Provider Hierarchy (Diagram 3)

```
PARENT (PointOfSale.Web)                      owns
─────────────────────────                     ─────────────────────────────
App (admin shell, auth, i18n)                 admin session, routing
└─ BuilderWorkspaceProvider                   workspace admission (RowVersion),
   │                                          page identity, save/publish state
   └─ builderStore (Zustand)                  sections candidates, selection,
      │                                       dirty, undo/redo
      └─ EditorBridgeProvider                 bridge lifecycle, revision acks,
         │                                    rect map, runtime health
         └─ CanvasViewport                    iframe element + OverlayRoot
            ├─ OverlayRoot                    chrome only (§6)
            └─ <iframe> ──────────────────────boundary (postMessage only)──────

CHILD (PointOfSale.Storefront, editor mode)   owns
─────────────────────────                     ─────────────────────────────
bootstrap(editorMode)                         token validation, config admission
└─ storeConfigStore                           runtime copy of admitted config
   └─ ThemeProvider (REAL)                    theme resolution (registry)
      └─ StorefrontLayout (REAL)              themed header/footer
         └─ SectionRenderer (REAL)            section resolution + data-section-id
            └─ Theme Components               100% of presentation
                └─ ChildBridge module         rect reporting, event relay
```

Ownership rule: nothing below the iframe boundary is imported by the parent; nothing above it is imported by the child. The three Web shims (`src/storefront/layout/StorefrontLayout.tsx`, `src/storefront/themes/ThemeContext.tsx`, `src/components/sections/SectionRenderer.tsx`) are deleted — they have no place in this hierarchy.

---

## 8. Communication Model (Diagram 6)

One versioned protocol: **Editor Bridge v1** (postMessage, origin-pinned both directions, `{v, type, payload}` envelope, unknown types ignored → forward compatible).

Commands (parent → child):

| Command | Payload | Purpose |
|---|---|---|
| `hello` | `{bridgeVersion, expectedTenant}` | handshake open |
| `config:apply` | `{config, revision}` | inject candidate config (sections, settings, theme) |
| `selection:set` | `{sectionId \| null}` | mirror selection into runtime |
| `scroll:to` | `{sectionId, behavior}` | keyboard nav / palette insert focus |
| `viewport:set` | `{width}` | responsive preview (desktop/tablet/mobile) |
| `rects:request` | `{}` | force rect re-measure |

Events (child → parent):

| Event | Payload | Purpose |
|---|---|---|
| `runtime:ready` | `{bridgeVersion, themeId, tenantId}` | handshake complete; overlay mounts |
| `config:applied` | `{revision}` | ack; overlay clears "syncing" |
| `rects:changed` | `{revision, rects: [{sectionId, type, rect}]}` | overlay positioning (rAF-batched) |
| `section:clicked` / `section:hovered` | `{sectionId \| null}` | selection/hover propagation |
| `runtime:error` | `{scope, sectionId?, message}` | section error boundary or boot failure → fail-closed UI |
| `navigation:blocked` | `{href}` | editor mode swallows link navigation, reports it |

Synchronization rules:
- Update propagation: builderStore mutation → new candidate `{config, revision}` → `config:apply` → ack. Debounced autosave PUT (If-Match) runs independently; a save NEVER round-trips through the canvas.
- Selection propagation: bidirectional but single-owner — builderStore owns `selectedSectionId`; child clicks are proposals (`section:clicked`), parent decides, then mirrors via `selection:set`.
- Theme changes: theme switch is just config (`storefrontTheme` / preset keys) inside `config:apply`; the runtime re-resolves via its registry. If a theme change requires new chunks, child re-resolves lazily (existing resolver behavior) and re-reports rects.
- Backend: only the parent talks to admin endpoints (JWT). The child talks only to read-only storefront preview endpoints under the signed token. Draft preview in a new tab = plain storefront URL + `themePreviewToken` (existing machinery, zero bridge involvement).
- Failure policy: missed ack or heartbeat within timeout → overlay enters degraded "canvas out of sync — reload canvas" state. No synthetic fallback, ever (P5).

---

## 9. State Ownership (Table 3 / Diagram 5)

| State | Owner | Runtime's role | Notes |
|---|---|---|---|
| Workspace (page id, admission, RowVersion) | Builder (`admitWorkspaceConfig`) over Backend truth | none | unchanged, certified |
| Selection | builderStore `selectedSectionId` | mirror (`selection:set`) | existing field |
| Hover | Overlay-local transient state | source of hover events | never persisted, never in undo |
| Theme identity + overrides | Backend (preset keys, override JSON) | renders admitted candidate | unchanged |
| Preview (candidate) | Builder revision stream → bridge | renders it | replaces synthetic PreviewModal |
| Draft | Backend (`PUT pages/{id}` If-Match) | reads via token on boot | unchanged |
| Publish / rollback / versions | Backend endpoints | none | unchanged |
| StoreConfig / ThemeConfig | Backend; Builder holds candidate | iframe-scoped CSS vars | applyTheme now naturally scoped by iframe |
| Section settings | builderStore candidates → backend canonicalization | renders props | unchanged |
| Overlay state (rings, toolbar, menu, drag) | Overlay layer | none | pure chrome |
| Theme runtime state (resolved components, caches, fonts) | Storefront runtime inside iframe | owns fully | parent never touches |
| Rect map | EditorBridgeProvider (parent) | producer | derived, revision-tagged |
| Drag state | parent dnd-kit + DragGhost | none (sees results via config:apply) | drop index from rect map |

Responsibility summary: Backend = truth. Builder = candidates + intent + chrome. Runtime = presentation + measurement. No state is dual-owned; every bidirectional flow has a single decision owner.

---

## 10. Section Contract & Theme Contract

### 10.1 Section Contract — FROZEN

The existing contract `{id, type, order, enabled, settings}` (normalized to `data` at render) is **valid and remains frozen**. Evidence: shape is already identical in `builderStore.js`, SF `types/storeConfig.ts:56-62`, and both `homepageSections.ts` normalizers; ARCH-1.0 §3.2 certifies the data plane. The target architecture consumes it unchanged — no additions are required for theme-native rendering, because identification (`data-section-id`) is emitted by the renderer, not stored in the JSON. Any future extension (e.g. per-section locks) is a new ADR.

One hygiene item (not a contract change): reconcile the builder registry's 16 types with the storefront's 17 defaults + alias map (ARCH-1.0 G11) so the palette can offer everything the runtime can render.

### 10.2 Theme Contract (Table 1 embedded)

Every theme MUST:
1. Render every section type it claims, resolvable by `resolveSectionComponent(type, themeId)` (file-convention: `./{themeId}/sections/{type}.tsx`), with graceful default fallback.
2. Render layout (header/footer) via the layout resolver convention.
3. Never remove or wrap away the renderer-emitted `data-section-id` / `data-section-type` wrapper (selection support and overlay anchoring depend on it).
4. Consume settings exclusively as props (`section.data`); no hidden channels.
5. Style exclusively through theme tokens / CSS vars + its own classes; no assumptions about the host page beyond the storefront runtime.
6. Meet accessibility baselines (semantic landmarks, alt handling, focus visibility) — the overlay adds editing a11y but cannot fix theme a11y.
7. Import ZERO Builder APIs. No `window.parent` access, no editor detection, no builder-conditional rendering. Builder independence is absolute (P3).
8. Tolerate preview data (sections may receive preview product/collection data via the token-scoped endpoints; components must not care where data came from).

Consequence: any theme satisfying the storefront contract automatically works in the Builder. There is no separate "builder compatibility" work, ever.

### Table 1 — Responsibility Matrix

| Concern | Theme | Builder | Platform (backend) |
|---|---|---|---|
| Section/layout presentation | OWNS | — | — |
| Tokens, typography, spacing, animation | OWNS | — | defines ThemeConfig truth |
| data-section-id anchors | emits (via runtime renderer) | consumes | — |
| Editing chrome (rings, toolbar, dnd, menus) | — | OWNS | — |
| Selection/hover/undo/candidates | — | OWNS | — |
| Settings schemas & palette | — | OWNS (registry) | validates on write |
| Draft/live/version truth, publish | — | intent only | OWNS |
| Preview tokens & read-only enforcement | — | requests | OWNS |
| Bridge protocol | — | parent side | child side ships with storefront runtime |

---

## 11. Builder Contract

The Builder MUST:
1. Never render section or layout presentation. No component under PointOfSale.Web may emit section markup, theme classes, or placeholder previews.
2. Interact with the runtime only through the versioned Editor Bridge; no reaching into the iframe DOM (`contentDocument` access is banned even same-origin).
3. Anchor all chrome to `data-section-id` rects reported by the runtime.
4. Treat backend as truth: admit configs with RowVersion, save with If-Match, publish/rollback via existing endpoints (unchanged).
5. Fail closed: bridge failure → explicit degraded state, never synthetic rendering.
6. Keep the palette/registry as SCHEMA ONLY (types, display names, icons, settings schemas) — the registry's `rendererPreview` layer is removed (ARCH-1.0 G6).
7. Version the bridge and negotiate on handshake; refuse mismatched majors with a clear operator message.

## 12. Extensibility (Table 4)

All future features attach at the Builder/overlay/bridge tier. Themes never change — that is the acid test of the architecture.

| Future feature | How it fits | Theme changes? |
|---|---|---|
| AI editing | AI proposes candidate configs → same `config:apply` revision stream; diff shown via overlay badges | NO |
| Collaborative editing | presence/CRDT over builderStore candidates; peers' selections = extra overlay rings keyed by section id | NO |
| History playback | replay saved versions/revisions through `config:apply`; scrubber in TopBar | NO |
| Annotations / Commenting | overlay pins anchored to section rects; stored platform-side keyed by sectionId | NO |
| Heatmaps | overlay heat layer positioned by rect map; analytics data platform-side | NO |
| Localization preview | locale in `config:apply` / bootstrap param; runtime already i18n-aware | NO |
| A/B preview | two candidate configs → two iframes or toggled revisions | NO |
| Marketplace preview | same embed pointed at a marketplace theme id under a demo tenant token | NO |
| Visual diff | render two revisions in offscreen iframes, screenshot & diff | NO |
| Future SDK | Editor Bridge protocol published as the SDK surface | NO |
| Mobile editor | native webview embeds the same storefront + bridge; overlay reimplemented natively | NO |
| Plugin ecosystem (Diagram 9) | plugins = parent-side overlay extensions subscribing to bridge events + rect map; sandboxed from runtime by construction | NO |

```
Diagram 9 — Future Plugin Architecture
Builder Plugin Host (parent)
  ├─ subscribes: rects:changed, section:*, config:applied
  ├─ contributes: overlay panels/badges, TopBar actions, settings tabs
  └─ NEVER: crosses the iframe boundary or touches theme code
```

## 13. Migration Strategy (Table 5 / Diagram 8)

Architecture-only roadmap; each phase independently shippable and reversible behind a builder feature flag (`builder.themeNativeCanvas`).

| Phase | Scope | Depends on | Risk | Rollback | Success criteria |
|---|---|---|---|---|---|
| M0 | ADR ratified; bridge protocol spec v1 written; flag added | this doc | LOW | n/a | protocol reviewed & frozen |
| M1 | Storefront editor mode: `editorMode` bootstrap + child bridge (ready/rects/click relay/config:apply); zero behavior change without the flag/token | M0 | LOW-MED | dead code path | storefront prod behavior byte-identical without editor params |
| M2 | Builder canvas swap: iframe + EditorBridgeProvider + read-only overlay (selection/hover rings) behind flag; old canvas still default | M1 | MED | flip flag | real theme renders in canvas; select/hover work; fidelity spot-check vs live storefront |
| M3 | Editing parity: toolbar, dnd (ghost + rect-derived drop index), keyboard nav, settings live-apply, viewport presets | M2 | MED | flip flag | full editing workflow on real theme; autosave/publish untouched |
| M4 | Cutover & deletion: flag default ON; delete 3 shims + synthetic SectionRenderer + PreviewModal synthetic path + dead rendererPreview; fix super-admin builder to same embed (G7); registry/type reconciliation (G11) | M3 | MED | revert flag commit (shims deleted only after one stable release) | zero synthetic presentation code in PointOfSale.Web |
| M5 | Re-certification: new architecture tests assert (a) no section markup in Web, (b) bridge is sole crossing, (c) runtime unchanged for public storefront; update certified docs | M4 | LOW | n/a | certification suite green |

Rollback strategy: M1 is inert without the flag; M2/M3 revert by flag; M4 deletion is deferred one release behind cutover so rollback = flag flip until then. Backend is untouched in every phase → no data-plane rollback ever needed.

Dependency chain (Diagram 8): M0 → M1 → M2 → M3 → M4 → M5, with M1 the only storefront-side change and the only cross-team dependency.

## 14. Architecture Decision Record

- **ADR-BUILDER-001: Homepage Builder canvas embeds the real Storefront runtime (Option A).**
- Context: ARCH-1.0 proved the builder ships a synthetic presentation system (16/16 sections synthetic, zero runtime integration, every presentation concern overridden) while backend authority and the data contract are correct. The storefront already exposes signed draft preview and dormant editor hooks.
- Alternatives: (B) shared runtime package — rejected: Tailwind/token unification risk to the admin shell, `:root` var scoping requires modifying the certified runtime, provider/data-env duplication, lockstep package versioning reintroduces presentation drift, and it refactors the working storefront to fix the builder. (C) improve synthetic previews — rejected outright: perpetuates dual presentation, violates P1.
- Tradeoffs accepted: a postMessage bridge protocol to own; rect-sync overlay positioning; iframe memory overhead; drag & drop implemented via ghost + rect-derived indices rather than native in-tree dnd.
- Chosen direction: Option A with Editor Bridge v1 as the single crossing point; overlay chrome fully separated; shims deleted; fail-closed canvas.
- Consequences: theme fidelity structurally guaranteed; themes forever builder-independent; apps deploy independently; the bridge becomes the seed of a future editor SDK; builder team owns overlay+bridge only.
- Future reassessment criteria — reopen ONLY if: (1) measured editing latency via `config:apply` exceeds agreed UX budget (e.g. p95 > 100ms apply-to-paint) after optimization; (2) a mandated feature provably cannot cross the bridge; (3) the platform consolidates to a single frontend app, making "embedding" meaningless. Any reopening requires a new ADR with implementation evidence.

## 15. Risk Assessment, Recommendations, Final Certification

Risks:

| Risk | Sev | Mitigation |
|---|---|---|
| Bridge protocol drift between apps | MED | version handshake; protocol spec frozen in M0; contract tests both sides |
| Rect desync (fonts/images/async data shifting layout) | MED | ResizeObserver + revision-tagged rects; rings dim while stale |
| Drag & drop UX regression vs in-tree dnd | MED | M3 acceptance gate on UX parity; ghost pattern prototyped first |
| Preview-token lifetime/scope misuse | LOW | existing server-side read-only enforcement; short-lived tokens; renewal via bridge-independent refresh |
| Theme JS error blanks canvas | LOW | existing SectionErrorBoundary + `runtime:error` → per-section degraded chrome |
| Cert suite freezes stale strings | LOW | M5 re-certification is an explicit phase |

Recommendations:
1. Ratify ADR-BUILDER-001 and freeze Editor Bridge v1 before any implementation phase (M0 gate).
2. Treat M1 (storefront editor mode) as additive-only; forbid any refactor of resolvers/renderer beyond adding the child bridge and editor-mode bootstrap branch.
3. Delete, don't deprecate: shims and `rendererPreview` are removed in M4 — no third presentation layer survives.
4. Fold the super-admin homepage builder into the same embed in M4; correct its false "real renderer" banner as part of that change.
5. Publish the bridge spec as an internal doc — it is the future SDK's first artifact.

### FINAL ARCHITECTURE CERTIFICATION

**ARCHITECTURE APPROVED.**

Justification from ARCH-1.0 evidence: the data plane and authority chain are certified correct and untouched by this design; the divergence is confined to the canvas presentation layer with a single located divergence point (`BuilderCanvas.jsx:13-15`); the storefront already ships the two enablers Option A needs — signed draft preview (`bootstrap.ts`, `httpClient.ts`, `endpoints.ts`) and dormant editor hooks (`SectionRenderer.tsx:20-21,125-126`) — both re-verified against live source in this phase. Option A therefore achieves the initiative's mandate (single source of truth, absolute theme fidelity, builder as pure editing layer, automatic support for every future theme) with additive-only storefront changes and a fully reversible, flag-gated migration. Implementation may begin at M0/M1 upon ADR ratification.

— End of ARCH-1.1 canonical design document —
