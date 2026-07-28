# BUILDER-ARCH-1 / ARCH-1.4 — Editing Parity & Legacy Decommission Readiness

Status: **FINAL — NOT READY (parity not yet achieved; deletion blocked)**
Date: 2026-07-27
Initiative: BUILDER-ARCH-1 (Theme-Native Homepage Builder)
Prior phases (frozen): ARCH-1.0 (audit, APPROVED), ARCH-1.1 (canonical architecture / ADR-BUILDER-001, APPROVED), ARCH-1.2 (Bridge + Overlay + Diagnostics, FOUNDATION APPROVED), ARCH-1.3 (Theme-Native Canvas, INTEGRATION APPROVED WITH OBSERVATIONS).

Evidence discipline: every load-bearing claim below was re-verified against live source in this session (file:line citations refer to the working tree at commit lineage `ed7ffda`). No claim is carried forward from prior .md documents without re-reading the implementation. Gaps are marked, never fabricated.

Verification runs executed this session (2026-07-27):

| Suite | Command | Result |
|---|---|---|
| Web bridge + canvas certification | `npx vitest run src/editor-bridge src/modules/storefront/components/builder/themeNativeCanvas.certification.test.ts` | 3 files, **39/39 passed** |
| Storefront bridge + editor-mode certification | `npx vitest run src/editor-bridge src/editor-mode` | 2 files, **25/25 passed** |
| Web architecture regression tests | `npx vitest run src/architecture-tests` | 2 files, **32/32 passed** |
| Storefront type gate | `npx tsc --noEmit -p tsconfig.json` | exit 0 (clean) |

---

## 1. Executive Summary

The Theme-Native Canvas (`PointOfSale.Web/src/modules/storefront/components/builder/ThemeNativeCanvas.jsx`, 375 lines) is architecturally sound, fully bridge-driven, fail-closed, and **superior to the legacy synthetic canvas in rendering fidelity, viewport emulation, scroll synchronization, hover feedback, and failure recovery**. All 96 certification/regression tests pass and the frozen ARCH-1.2 foundation is intact (protocol byte-parity test green).

However, ARCH-1.4's mandate is *editing* parity, and the evidence shows the native canvas has **not yet absorbed the structural-editing surface of the legacy canvas**. Six capabilities that are Native in the legacy path are **Missing** in the flag-ON path:

1. Drag-from-palette section insertion into the canvas (no droppable targets over the iframe).
2. Drag reordering of existing sections.
3. Drop indicator rendering (`AnchorResolver.resolveDropIndicator` exists in the frozen overlay foundation, `OverlayRuntime.ts:261-286`, but has **zero call sites** in `ThemeNativeCanvas.jsx`).
4. Per-section toolbar *actions* (hide/show, duplicate, delete, edit) — the native toolbar chrome renders only a type label (`ThemeNativeCanvas.jsx:357-364`).
5. Keyboard-sensor reordering (dnd-kit `KeyboardSensor` binds only to synthetic sortable cards).
6. Empty-state / "Load Demo" affordance inside the canvas.

All of these are **additive host-side work**: overlay chrome buttons wired to existing `useBuilderStore` actions, plus a pointer-capture drop layer that translates overlay coordinates through the already-shipped `CoordinateMapper`/`resolveDropIndicator`. **No Bridge, Overlay Runtime, protocol, theme-contract, StoreConfig, or backend change is required to close any gap.** That is the central good news of this phase: parity closure is confined to one host component and does not touch anything frozen.

Because Gate A (Editing Parity) fails, Gates E and F cannot pass, and the legacy synthetic canvas **must not be deleted yet**. The deletion inventory (Section 8) is complete and mechanical; once the six gaps close and the deferred live drills (visual pixel diff, performance capture, stability soak — all deferred since ARCH-1.3) execute, deletion becomes the low-risk operation this initiative demands.

**Final certification: NOT READY** — with a short, fully-scoped path to PARITY CERTIFIED (estimated 3–5 engineering days of host-side chrome work + 2–3 days of live certification drills).

---

## 2. Parity Matrix

Legend — Classification: Native / Partial / Missing / Regressed · Verdict vs legacy: Equivalent / Superior / (gap).
"Host-level" means the capability lives in `StorefrontBuilderPage.jsx` or the builder store and is shared by both canvases unchanged.

| # | Capability | Legacy canvas | Theme-Native canvas | Class (native path) | Verdict | Evidence |
|---|---|---|---|---|---|---|
| 1 | Canvas loading | Synthetic cards after config admission | iframe + bridge handshake, connecting overlay, config-gated | Native | Equivalent | `BuilderCanvas.jsx:189-201`; `ThemeNativeCanvas.jsx:101-170,324-331` |
| 2 | Theme rendering | Web-side shim copies (`ThemeContext.tsx`, `StorefrontLayout.tsx`, Web `SectionRenderer.tsx`) | Real storefront runtime; applier = production `setConfig` + `applyTheme` | Native | **Superior** | `BuilderThemeProvider.jsx:2`; `editorMode.ts:64-76` |
| 3 | Selection | Card onClick → store | Child click proposal → host decides → `selection.set` mirror + overlay rect | Native | Equivalent (stronger ownership model) | `ThemeNativeCanvas.jsx:127-138,183-189`; protocol `SelectionChangedPayload` (`protocol.ts:196-200`) |
| 4 | Hover | CSS group-hover on cards only | Child `hover.changed` (rAF-coalesced) → overlay hover rect | Native | **Superior** (real-runtime hover) | `ChildRuntimeBridge.ts:469`; `ThemeNativeCanvas.jsx:231-233,335-345` |
| 5 | Toolbar (label/positioning) | Card header bar | `resolveToolbar` anchored chrome with above/below flip | Native | Equivalent | `OverlayRuntime.ts:244-258`; `ThemeNativeCanvas.jsx:234,357-364` |
| 6 | Toolbar actions (hide/duplicate/delete/edit) | Eye/Copy/Trash/Settings buttons on every card | **Label only — no action buttons** | **Missing** | gap G1 | Legacy `BuilderCanvas.jsx:68-97`; native `ThemeNativeCanvas.jsx:357-364` |
| 7 | Settings panel | Shared right panel (schema-first, editor fallback) | Same (unchanged, canvas-independent) | Native | Equivalent | `SettingsPanel.jsx:12-95`; `StorefrontBuilderPage.jsx:830` |
| 8 | Live settings updates | Store → synthetic re-render | Store → `config.apply` (revision-monotonic, ack'd) → real re-render + fresh rects | Native | **Superior** (renders truth) | `ThemeNativeCanvas.jsx:176-180`; `protocol.ts:131-141` |
| 9 | Drag (existing section) | dnd-kit `useSortable` per card | **No drag surface over iframe** | **Missing** | gap G2 | Legacy `BuilderCanvas.jsx:27,213-228`; native: no dnd integration in `ThemeNativeCanvas.jsx` |
| 10 | Drop (from palette) | Palette `useDraggable` → `handleDragEnd` `palette-*` branch | **No droppable target in native canvas** | **Missing** | gap G2 | `SectionPalette.jsx:20-26`; `StorefrontBuilderPage.jsx:444-449` |
| 11 | Reordering | `reorderSections(oldIndex,newIndex)` via sortable | **Missing UI** (store action exists and works) | **Missing** | gap G2 | `StorefrontBuilderPage.jsx:452-458` |
| 12 | Keyboard navigation (reorder) | `KeyboardSensor` + `sortableKeyboardCoordinates` | **Missing** (sensors bind to synthetic cards only) | **Missing** | gap G3 | `StorefrontBuilderPage.jsx:97-100` |
| 13 | Undo | Ctrl+Z → store `undo()` (50-deep stack) | Same host shortcut; native re-renders via config.apply | Native | Equivalent | `StorefrontBuilderPage.jsx:420-423`; `builderStore.js:29-30,211+` |
| 14 | Redo | Ctrl+Y → store `redo()` | Same | Native | Equivalent | `StorefrontBuilderPage.jsx:424-427` |
| 15 | Autosave | 1500 ms debounced `persistPageLayout` with abort/row-version guards | Same (host-level) | Native | Equivalent | `StorefrontBuilderPage.jsx:378-393` |
| 16 | Draft semantics | Draft-only edits until publish; If-Match rowVersion admission | Same (host-level) | Native | Equivalent | `StorefrontBuilderPage.jsx:128-169,303-376` |
| 17 | Publish | `publishPage` + modal + admitted response | Same (host-level) | Native | Equivalent | `StorefrontBuilderPage.jsx:528-569` |
| 18 | Rollback | Version history modal → `rollback` → reload | Same (host-level) | Native | Equivalent | `StorefrontBuilderPage.jsx:571-613` |
| 19 | Viewport switching | **None in legacy canvas** | Desktop/tablet/mobile switch → frame width + `viewport.set` | Native | **Superior** (net-new) | `ThemeNativeCanvas.jsx:25-29,191-197,249-267`; `ChildRuntimeBridge.ts:225-234` |
| 20 | Theme switching | Focus/visibility reload of workspace; shim re-render | Same reload path; native re-applies real theme via config.apply applier | Native | **Superior** (real tokens) | `StorefrontBuilderPage.jsx:285-301`; `editorMode.ts:74-75` |
| 21 | Header/Footer | Web shim `StorefrontLayout` copy | Real storefront layout inside iframe | Native | **Superior** | Legacy `BuilderCanvas.jsx:17,209`; native renders actual runtime |
| 22 | Scroll synchronization | N/A (cards scroll in host) | `scroll.changed` rect shifting + host `syncOffset` (ResizeObserver + scroll listener) | Native | **Superior** (net-new) | `OverlayRuntime.ts:112-125,377-379`; `ThemeNativeCanvas.jsx:200-223` |
| 23 | Zoom | None | Foundation ready (`CoordinateMapper.setScale`, `ViewportTracker.setScale`) — **no host UI** | Partial | Equivalent (both lack UI; native has plumbing) | `OverlayRuntime.ts:181-188,326-329` |
| 24 | Section insertion | Palette drag or Load Demo | Only Load Demo path reaches store; no in-canvas insertion | **Missing** | gap G2 | see rows 10-11 |
| 25 | Section deletion | Card Trash button → `removeSection` | **No UI**; store action shared | **Missing** | gap G1 | Legacy `BuilderCanvas.jsx:83-89` |
| 26 | Section duplication | Card Copy button → `duplicateSection` | **No UI** | **Missing** | gap G1 | Legacy `BuilderCanvas.jsx:76-82` |
| 27 | Visibility toggle | Card Eye button → `toggleSection`; disabled overlay | **No UI**; child correctly renders disabled sections as hidden (real behavior) | **Missing** (UI) | gap G1 (note: hidden sections vanish from rects — see G5) | Legacy `BuilderCanvas.jsx:69-75,112-119`; Storefront `SectionRenderer.tsx:81` |
| 28 | Anchor positioning | N/A (synthetic) | `data-section-id` anchors → rAF-coalesced rects → `CoordinateMapper` overlay space | Native | **Superior** | `SectionRenderer.tsx:125`; `OverlayRuntime.ts:89-106,173-209` |
| 29 | Drop indicator | Implicit via dnd-kit sortable gap | `resolveDropIndicator` shipped but **unused by host** | **Missing** (wiring) | gap G2 | `OverlayRuntime.ts:261-286`; zero call sites in `ThemeNativeCanvas.jsx` |
| 30 | Performance | Synthetic React re-render per edit | rAF-coalesced rects, revision monotonicity, last-write-wins backpressure | Native (measured drills deferred) | Expected Superior — see Section 6 | `protocol.ts:19-28` |
| 31 | Accessibility | Buttons/titles on cards; dnd-kit keyboard support | Viewport buttons + reconnect buttons titled; iframe has `title`; overlay is `pointer-events-none` decorative | Partial | gap G4 (keyboard + SR parity for toolbar once G1 lands) | `ThemeNativeCanvas.jsx:254-266,314-322,334` |
| 32 | Failure recovery | None (synthetic cannot fail) | Degraded banner, explicit error text, never synthetic fallback (P5), last frame stays visible | Native | **Superior** (net-new) | `ThemeNativeCanvas.jsx:10-13,236,291-309` |
| 33 | Reconnect | N/A | Bridge backoff schedule + manual Reload (frameEpoch full session rebuild) + re-push config & selection on `ready` | Native | **Superior** (net-new) | `protocol.ts:295-302`; `ThemeNativeCanvas.jsx:119-126,240-244` |
| 34 | Error presentation | Host-level red banners (load/save/publish) | Same host banners + canvas-local amber degraded banner + stale "Syncing…" badge | Native | **Superior** | `StorefrontBuilderPage.jsx:764-819`; `ThemeNativeCanvas.jsx:291-309,365-369` |

Score: 21 Native (8 Superior, 13 Equivalent) · 2 Partial · 6 Missing · **0 Regressed**. Nothing that works in the native path is worse than legacy; the gaps are all absences, not regressions.

---

## 3. Legacy Gap Analysis

All fixes below are **additive host-side chrome** in `ThemeNativeCanvas.jsx` (plus small extracted subcomponents). None require touching the frozen bridge, overlay runtime, protocol, theme contracts, section contract, StoreConfig, backend APIs, or theme packages/SDK.

### G1 — Section action toolbar (hide / duplicate / delete / edit)
- **Behavior**: Legacy shows Eye/Copy/Trash/Settings per card (`BuilderCanvas.jsx:68-97`). Native toolbar chrome renders only the section type label (`ThemeNativeCanvas.jsx:357-364`).
- **Impact**: HIGH — merchants cannot manage sections without the settings panel / legacy fallback.
- **Root cause**: ARCH-1.3 scope deliberately shipped read-only chrome (selection/hover) first.
- **Fix**: extend the toolbar chrome (already positioned by `AnchorResolver.resolveToolbar`) with buttons calling the existing store actions `toggleSection`, `duplicateSection`, `removeSection`, `selectSection` — the exact functions legacy uses (`BuilderCanvas.jsx:175-177,222-224`). Toolbar div gains `pointer-events-auto`.
- **Priority**: P0 · **Risk**: Low (pure host UI over existing store actions) · **Owner**: Web builder team · **Effort**: 0.5–1 day.

### G2 — Drag & drop: insertion, reordering, drop indicator
- **Behavior**: Legacy uses dnd-kit sortable cards + palette draggables; page-level `handleDragEnd` performs `addSection` / `reorderSections` (`StorefrontBuilderPage.jsx:434-459`). Native canvas exposes no droppable/drag surface; the iframe swallows pointer events; `resolveDropIndicator` (`OverlayRuntime.ts:261-286`) is shipped, unit-certified in ARCH-1.2, and unused.
- **Impact**: HIGH — core structural editing absent in flag-ON path.
- **Root cause**: deferred from ARCH-1.3 (documented observation); overlay drop layer was foundation-only.
- **Fix** (all host-side):
  1. Transparent drop-capture layer over the iframe, activated only while a dnd-kit drag is active (`pointer-events-auto` during drag; otherwise none — hover/selection continue to flow from child events).
  2. On drag-over: `overlay.anchors.resolveDropIndicator(pointerY)` → render the indicator line at `{x, y, width}`; store the insertion `index`.
  3. On drop: palette item → `addSection(type, index, sectionRegistry)`; existing section (drag initiated from the G1 toolbar's grip) → `reorderSections(oldIndex, index)`.
  4. Existing `config.apply` effect (`ThemeNativeCanvas.jsx:176-180`) already propagates the result to the real runtime; revision monotonicity handles rapid drags.
- **Priority**: P0 · **Risk**: Medium (pointer-capture layering vs iframe; mitigated by drag-active gating) · **Owner**: Web builder team · **Effort**: 2–3 days.

### G3 — Keyboard reordering / navigation
- **Behavior**: Legacy inherits dnd-kit `KeyboardSensor` reordering (`StorefrontBuilderPage.jsx:97-100`). Native has no keyboard path to move sections.
- **Impact**: MEDIUM (accessibility & power users). **Root cause**: same as G2.
- **Fix**: once G1 toolbar exists, add Move-Up/Move-Down buttons (or Alt+Arrow handlers on the focused toolbar) calling `reorderSections`. This is simpler and more screen-reader-friendly than replicating dnd-kit keyboard coordinates over an iframe.
- **Priority**: P1 · **Risk**: Low · **Owner**: Web builder team · **Effort**: 0.5 day (rides on G1).

### G4 — Accessibility of overlay chrome
- **Behavior**: overlay is decorative (`pointer-events-none`, no ARIA); legacy cards were focusable buttons.
- **Impact**: MEDIUM. **Root cause**: chrome minimalism in ARCH-1.3.
- **Fix**: G1 toolbar buttons get `aria-label`s (EN/AR), focus ring, roving tab order across sections; selected-rect div gets `role="presentation"`; add an offscreen live region announcing selection changes.
- **Priority**: P1 · **Risk**: Low · **Owner**: Web builder team · **Effort**: 0.5–1 day.

### G5 — Hidden (disabled) sections invisible in native canvas
- **Behavior**: Legacy renders disabled sections dimmed with a "Hidden" badge (`BuilderCanvas.jsx:112-119`). The real runtime correctly renders nothing for `!section.enabled` (`Storefront SectionRenderer.tsx:81`), so the anchor produces no rect and the section cannot be re-selected/re-enabled from the canvas.
- **Impact**: MEDIUM — visibility toggle becomes a one-way door unless the user finds the section elsewhere.
- **Root cause**: faithful production rendering (which is the point) removes the editing affordance legacy faked.
- **Fix**: host-side "hidden sections" tray in the canvas chrome (list chips derived from `sections.filter(s => !s.enabled)`) with show/select actions. Zero child changes; zero synthetic rendering (the tray is editor chrome, not a section preview — P5 respected).
- **Priority**: P1 · **Risk**: Low · **Owner**: Web builder team · **Effort**: 0.5 day.

### G6 — Empty state / Load Demo inside canvas
- **Behavior**: Legacy shows an in-canvas empty state with a Load Demo button (`BuilderCanvas.jsx:127-162,210-211`). Native shows whatever the runtime renders for an empty page.
- **Impact**: LOW. **Fix**: when `sections.length === 0`, render a host chrome panel over the frame offering Load Demo (`onLoadDemo` prop already flows into `BuilderCanvas`, currently unused by the native branch: `BuilderCanvas.jsx:167,189-201`).
- **Priority**: P2 · **Risk**: Low · **Effort**: 0.25 day.

### G7 — Zoom UI (both canvases lack it)
- Not a parity gap (legacy has none), but foundation exists (`CoordinateMapper.setScale`). Defer; do not block gates.

---

## 4. Remaining Legacy Dependencies (classification)

| Artifact | Location | Classification | Rationale |
|---|---|---|---|
| Synthetic renderer path in canvas | `BuilderCanvas.jsx:203-266` (flag-OFF branch) | **Delete** (after Gates A–F) | Superseded by native canvas; preserved verbatim only as rollback |
| Web `SectionRenderer` shim | `PointOfSale.Web/src/components/sections/SectionRenderer.tsx` | **Keep (for now) / Deferred delete** | Still used by legacy branch, `PreviewModal.jsx:12`, and Super-Admin `HomepageBuilderPreview.tsx:4` — the last is OUTSIDE this initiative's scope and blocks full deletion |
| `StorefrontLayout` shim | `PointOfSale.Web/src/storefront/layout/StorefrontLayout.tsx` | **Deferred delete** | Used by legacy branch + `HomepageBuilderPreview.tsx:6` |
| `ThemeContext` shim | `PointOfSale.Web/src/storefront/themes/ThemeContext.tsx` | **Deferred delete** | Used by `BuilderThemeProvider.jsx:2` and `PreviewModal.jsx:11` |
| `BuilderThemeProvider` | `components/builder/BuilderThemeProvider.jsx` | **Replace → Delete** | Native path still imports ONLY `normalizeStoreConfig` from it (`ThemeNativeCanvas.jsx:21`); extract that pure function to its own module, then the provider dies with the legacy branch |
| `rendererPreview` entries + `*Preview` components | `builder/sectionRegistry.js` (10 entries: lines 58,80,98,115,134,151,167,186,204,225) + `components/sections/*` Preview exports | **Delete** | Only consumed via the synthetic path; `types/builder.js:24` typedef updated alongside |
| Preview Cards (SortableSectionCard) | `BuilderCanvas.jsx:23-122` | **Delete** | Whole synthetic card tree |
| `PreviewModal` synthetic rendering | `PreviewModal.jsx` (uses shim ThemeProvider + SectionRenderer) | **Replace** | Should become a theme-native preview (same iframe pattern with preview config) in cutover Stage 4; until then Keep |
| Feature flag `builder.themeNativeCanvas` | `themeNativeCanvasFlag.js` (45 lines) + gate at `BuilderCanvas.jsx:189` | **Delete last** | Flag is the rollback mechanism through GA; removed in Final Cleanup only |
| dnd-kit sensors & DndContext for synthetic cards | `StorefrontBuilderPage.jsx:97-100,433-459,727-733` | **Keep (re-targeted)** | The palette + G2 drop layer keep using dnd-kit; only sortable-card wiring is deleted |
| Legacy-only CSS | Tailwind utility classes inline in deleted JSX | **Delete implicitly** | No standalone dead CSS files found (`AuthProvidersPage.css` is unrelated) |
| Certification test source-string rules referencing legacy | `themeNativeCanvas.certification.test.ts` (flag-ON path bans SectionRenderer/sectionRegistry) | **Replace at M5** | Re-freeze rules after deletion (skill already notes re-freezing at M5) |
| ARCH-1.3 flag documentation | `docs/architecture/builder-arch-1.3-theme-native-canvas-integration.md` flag/rollback sections | **Keep** (historical) + add deprecation note at Final Cleanup |
| Super-Admin `HomepageBuilderPreview` | `src/super-admin/homepage/` | **Keep — OUT OF SCOPE** | Separate Super-Admin surface with its own synthetic preview; consumes the shims. Flag as follow-up initiative; it is the only thing preventing shim deletion |
| Temporary compat code in child | none found | — | `editorMode.ts` is production-shaped; no shims on the storefront side |
| TODOs in bridge/canvas code | none found (grep for TODO in editor-bridge/, editor-mode/, builder/ returned none load-bearing) | — | |

Key structural finding: the shim trio (`ThemeContext`, `StorefrontLayout`, Web `SectionRenderer`) has exactly **three consumer families**: legacy canvas branch, PreviewModal, Super-Admin homepage preview. Deleting the first two is within BUILDER-ARCH-1; the third needs a one-line decision from the Super-Admin owners (adopt native preview or accept shim ownership transfer).

---

## 5. Visual Parity Certification (Gate B)

Design: same admitted StoreConfig rendered through (a) legacy synthetic canvas, (b) theme-native canvas, (c) real storefront at its own URL; pixel-compare (b)↔(c) and structurally compare (a)↔(c).

**What is certifiable today from source (static evidence):**
- The native canvas and the production storefront execute the *identical* code path: the editor-mode applier is `useStoreConfigStore.getState().setConfig(config); applyTheme(config.theme)` (`editorMode.ts:74-75`), the same store/theme entry points production bootstrap uses (ARCH-1.3 doc + `App.tsx:75` hook placement verified). There is no editor-specific rendering branch in the child; `SectionRenderer.tsx` renders the same DOM with only a passive `data-section-id` attribute (`SectionRenderer.tsx:125`), which was part of the theme contract since ARCH-1.1. Therefore (b) ≡ (c) **by construction**, up to viewport width.
- The legacy canvas, by contrast, provably diverges from the real storefront: shim `ThemeContext`/`StorefrontLayout` copies, card chrome wrapping every section, `space-y-4 p-4 md:p-6` spacing injection (`BuilderCanvas.jsx:214`), and preview-only product substitution (`BuilderCanvas.jsx:183-185`). Legacy is the synthetic presentation being retired; it is not, and never was, pixel-truthful.
- "No synthetic presentation remains" in the flag-ON path is machine-enforced: the certification test bans `SectionRenderer`/`sectionRegistry` imports from the native path (`themeNativeCanvas.certification.test.ts`, 39/39 green this session).

**What remains EXECUTION-PENDING (deferred since ARCH-1.3, still required before Gate B PASS):**
- Live 2-server Playwright run: load same tenant config in native canvas + raw storefront; screenshot diff at 1280/768/375 px for layout, spacing, typography, theme tokens, header, footer, section order, responsive layout, theme assets.
- RTL (Arabic) pass of the same matrix.

Gate B status: **CONDITIONAL PASS (by construction) — live pixel run pending**. No architectural risk; purely an execution task listed in the cutover plan Stage 0.

---

## 6. Performance Certification (Gate C)

No live measurement environment was exercised this phase (two dev servers + browser profiling are canary-stage tasks, deferred since ARCH-1.3). What the source certifies:

| Dimension | Legacy | Native | Static verdict |
|---|---|---|---|
| Bootstrap | Synthetic React tree, immediate | iframe boot of full storefront + handshake (10 s deadline, `protocol.ts:300`) | Native strictly slower to first paint — bounded, with connecting overlay UX (`ThemeNativeCanvas.jsx:324-331`). Acceptable by design |
| Selection latency | Local setState | postMessage round trip + overlay rect lookup (already-cached rects, `RectStore.getRect` O(1)) | Sub-frame expected; measure at canary |
| Drag latency | dnd-kit local | N/A until G2 lands; drop indicator math is O(sections) per pointer move (`OverlayRuntime.ts:261-286`) | Measure with G2 |
| Rect updates | N/A | Child rAF-coalesced full snapshots; last-write-wins; revision-tagged staleness (`protocol.ts:26-28`, `RectStore.ingestSnapshot` monotonicity `OverlayRuntime.ts:90`) | Designed for 100+ sections: one snapshot per frame regardless of section count |
| Memory | One React tree | Host tree + iframe runtime | Native higher by one storefront runtime; soak test pending (leak vectors reviewed: all listeners/disposers torn down in effect cleanup `ThemeNativeCanvas.jsx:160-167`, `OverlayRuntime.detach` clears subscribers) |
| Bridge overhead | — | Heartbeat 5 s, ack-required commands with 3 retries (`protocol.ts:295-302`) — negligible steady-state traffic | PASS by inspection |
| Scroll | Host-native | `scroll.changed` rect shifting without waiting for next snapshot (`OverlayRuntime.ts:112-125`) + passive host listener | PASS by inspection |
| Rapid editing | Debounced store | Revision monotonicity drops stale applies; stale rects flagged, not mis-rendered (`ThemeNativeCanvas.jsx:174-180,365-369`) | PASS by inspection |

Gate C status: **PENDING — design certified, live capture (bootstrap ms, selection/drag latency, memory over 30-min session, 100-section fixture) scheduled for canary Stage 0. No red flags found in source.**

---

## 7. Stability Certification (Gate D)

Executed this session (simulated/unit level — all green):
- 39 Web bridge/canvas certification tests: handshake, version/tenant mismatch refusal, ack retry/idempotency, epoch fencing on reconnect, revision monotonicity, degraded-state transitions, selection-proposal rules.
- 25 Storefront tests: fail-closed activation (missing/malformed/non-http origin → no bridge), applier rejection of non-StoreConfig candidates (previous document stays rendered), navigation blocking, heartbeat deadline behavior.
- Protocol byte-parity: PASS (frozen contract intact across both apps).
- Storefront `tsc --noEmit`: clean.

Reviewed-by-inspection behaviors backing the stability scenarios:
- **Reconnect storms**: backoff schedule 500→10 000 ms (`protocol.ts:301`); epoch increments fence stale messages (`protocol.ts:63-65`); on every `ready` the host re-pushes full document + selection (`ThemeNativeCanvas.jsx:119-126`) so recovery is stateless.
- **Iframe reloads**: `frameEpoch` key remount rebuilds the entire session; manual Reload affordance always visible (`ThemeNativeCanvas.jsx:240-244,279-287,315`).
- **Rapid saves**: host-level abortable, request-id-guarded save pipeline unchanged (`StorefrontBuilderPage.jsx:303-376`).
- **Undo stress**: bounded 50-entry stacks (`builderStore.js:50`); each undo is just another config.apply.
- **Token renewal / auth**: pending live drill (deferred since ARCH-1.3 — themePreviewToken expiry mid-session).
- **Memory leaks**: disposer discipline verified in source; long-session soak pending.

Gate D status: **PENDING — unit/simulated stability green; live drills (reconnect storm, 4-hour soak, token expiry, rapid theme switching under embed) scheduled for canary Stage 0.**

---

## 8. Cleanup Readiness — Deletion Inventory (inventory ONLY; nothing deleted)

### D1 — Deleted outright at Legacy Removal (Stage 6)
| Item | File / lines | Notes |
|---|---|---|
| Synthetic canvas branch | `BuilderCanvas.jsx:203-266` + `SortableSectionCard` (23-122) + `EmptyCanvasState` (127-162, superseded by G6 chrome) | After removal `BuilderCanvas.jsx` collapses to the native return or is deleted with `ThemeNativeCanvas` promoted in `components/builder/index.js` |
| Legacy imports in canvas | `BuilderCanvas.jsx:6-18` (dnd-kit sortable, sectionRegistry, shims, product preview) | |
| `BuilderThemeProvider` component | `components/builder/BuilderThemeProvider.jsx:39-74` | PRE-STEP: extract `normalizeStoreConfig` (lines 7-37) to `storeConfigNormalization.js`; update `ThemeNativeCanvas.jsx:21` import |
| `rendererPreview` registry field | `builder/sectionRegistry.js` lines 58, 80, 98, 115, 134, 151, 167, 186, 204, 225 + typedef `types/builder.js:24` | |
| 10 `*Preview` components | `components/sections/{HeroBanner,ProductGrid,CategoryGrid,SubcategoryGrid,TextBlock,ImageBlock,VideoBlock,NewsletterSignup,Testimonials,FeaturedProducts}.jsx` Preview exports + `components/sections/index.js:2-9` re-exports | Editor exports in the same files REMAIN (settings panel uses them, `SettingsPanel.jsx:43,77-78`) |
| Sortable-card DnD wiring | `StorefrontBuilderPage.jsx:452-458` reorder-over-card branch (re-targeted to G2 drop layer indices) | DndContext + sensors stay for palette/G2 |
| Legacy drag overlay cards | `BuilderCanvas.jsx:247-264` | Replaced by G2 indicator |

### D2 — Replaced before deletion
| Item | Replacement |
|---|---|
| `PreviewModal.jsx` synthetic rendering (shim ThemeProvider + Web SectionRenderer, lines 11-13) | Theme-native preview iframe (reuse `buildEditorFrameUrl` pattern with preview config, read-only) — Stage 4 |
| `themeNativeCanvas.certification.test.ts` source-string freeze rules | Re-frozen M5 rules asserting legacy symbols no longer exist |

### D3 — Deferred (blocked by out-of-scope consumer)
| Item | Blocker |
|---|---|
| `src/storefront/themes/ThemeContext.tsx` | `PreviewModal` (until D2) — after D2, only Super-Admin remains via StorefrontLayout chain |
| `src/storefront/layout/StorefrontLayout.tsx` | `super-admin/homepage/HomepageBuilderPreview.tsx:6` |
| `src/components/sections/SectionRenderer.tsx` + `homepageSections.ts` | `HomepageBuilderPreview.tsx:4-5` + its test |
| `HomepageBuilderPreview.*` (Super-Admin synthetic preview) | Separate initiative; decision owner: Super-Admin team |

Note: `src/storefront/products/ProductTemplateRegistry.ts` is NOT deletable — used by `ProductForm.jsx:12` and `StorefrontBuilderPage.jsx:45` for template management, independent of rendering.

### D4 — Deleted at Final Cleanup (Stage 7, last)
| Item | File |
|---|---|
| Feature flag module | `components/builder/themeNativeCanvasFlag.js` (whole file) |
| Flag gate + early return | `BuilderCanvas.jsx:187-201` |
| Flag exports | `components/builder/index.js` flag re-exports |
| `VITE_BUILDER_THEME_NATIVE_CANVAS` env references | env files / deployment config |
| Flag sections in docs | deprecation notes added to `builder-arch-1.3-*.md` (docs kept as history) |

### D5 — Never delete (production system going forward)
`PointOfSale.Web/src/editor-bridge/*` (5 modules + 3 test files), `PointOfSale.Storefront/src/editor-bridge/*`, `PointOfSale.Storefront/src/editor-mode/*`, `ThemeNativeCanvas.jsx`, palette, settings panel, store, page shell, storefront `SectionRenderer.tsx` (real one), `data-section-id` anchors.

Estimated net deletion at completion: ~1,200+ lines of synthetic presentation code and 3 shim files out of `PointOfSale.Web`, with zero storefront-side deletions (the child never had shims).

---

## 9. Migration Readiness & Cutover Plan

Preconditions for Stage 1: G1–G6 implemented; Gates A–F all PASS.

| Stage | Scope | Flag state | Rollback |
|---|---|---|---|
| 0. Certification drills | Live Playwright visual diff (Gate B), perf capture (Gate C), stability drills incl. token renewal + soak (Gate D) on staging | ON for test tenants only | n/a |
| 1. Canary | 1–2 internal tenants, production | localStorage 'on' per tenant operator | Flag → 'off' (instant, per ARCH-1.3 design `themeNativeCanvasFlag.js:8-13`) |
| 2. Internal | All internal/staff tenants | env `VITE_BUILDER_THEME_NATIVE_CANVAS=true` on internal deploy | env → false; redeploy |
| 3. Pilot | 5–10 % consenting merchant tenants | env true + per-tenant opt-out via localStorage 'off' | per-tenant 'off' or env rollback |
| 4. GA | Default ON for all; PreviewModal replaced (D2) | env true everywhere | env false (single deploy) |
| 5. Legacy Freeze | Legacy branch code-frozen; any bug in flag-OFF path fixed only by rolling forward | ON | last stage where 'off' is honored |
| 6. Legacy Removal | Execute D1 + D2 test re-freeze; architecture tests re-frozen (M5) | Flag still exists, forced-ON semantics | git revert of removal commit |
| 7. Final Cleanup | Execute D4 (flag deletion) + D3 items whose blockers cleared; docs deprecation notes | Flag gone | git revert |

Rollback exists at every stage; through Stage 5 it is a configuration change, after Stage 5 it is a git revert of an isolated mechanical commit (which is exactly the "deletion as low-risk mechanical operation" bar).

---

## 10. Risk Register

| ID | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| R1 | Drop-capture layer (G2) conflicts with child hover/selection events | Medium | Medium | Layer active only during drag; certified by new interaction tests before Stage 1 |
| R2 | Iframe pointer-event swallowing breaks dnd-kit drop detection | Medium | High for G2 | Drop layer sits above iframe with `pointer-events-auto` while dragging (standard iframe-DnD pattern); fall back to click-to-place insertion if untenable |
| R3 | Live pixel diff exposes storefront CSS depending on top-level browsing context (vh units, position:fixed) | Low | Medium | Gate B run at Stage 0; any finding is a storefront bug (real runtime = truth), fixed in storefront, not bridged around |
| R4 | Memory growth in long sessions (iframe + host) | Low | Medium | Stage 0 soak; disposer discipline already verified in source |
| R5 | themePreviewToken expiry mid-session degrades silently | Medium | Medium | Stage 0 token-renewal drill; degraded banner + reconnect affordance already ship |
| R6 | Super-Admin preview blocks shim deletion indefinitely | High | Low (scoped) | D3 explicitly transfers ownership decision; BUILDER-ARCH-1 closes without it |
| R7 | Hidden-section tray (G5) drifts toward synthetic previews | Low | High (P5 violation) | Chrome renders name/type chips only — codify in G5 review + M5 source-string rule |
| R8 | Legacy branch bit-rots during pilot | Medium | Low | Stage 5 freeze policy; architecture tests keep both paths compiling until removal |

---

## 11. Recommendations

1. **Execute G1+G3+G4 as one workstream** (toolbar chrome, ~2 days including a11y) and **G2 as a second** (~3 days); G5/G6 ride along (~1 day). Total parity closure ≈ one engineering week in `ThemeNativeCanvas.jsx` + small extracted modules. Zero frozen-surface changes.
2. **Extract `normalizeStoreConfig`** out of `BuilderThemeProvider.jsx` now (pre-step, 15 min) so the native path stops importing a legacy-classified file.
3. **Run Stage 0 drills immediately after G-closure** — they have been deferred twice (ARCH-1.2, ARCH-1.3); Gates B/C/D cannot be signed by inspection alone a third time.
4. **Open a one-pager with the Super-Admin team** on `HomepageBuilderPreview` (adopt native preview vs. take ownership of the shims). This is the only cross-team dependency in the entire decommission.
5. **Re-freeze architecture tests at M5** (removal commit): flip source-string rules from "flag-ON path must not import legacy" to "legacy symbols must not exist".
6. Do not add any capability to the bridge protocol for parity closure — none of G1–G6 needs one. If anyone proposes a protocol change during closure, that is scope error by definition.

---

## 12. Gate Summary & Final Certification

| Gate | Subject | Status | Reason |
|---|---|---|---|
| A | Editing parity | **FAIL** | 6 Missing capabilities (G1–G6): toolbar actions, DnD insertion/reordering, drop indicator wiring, keyboard reorder, hidden-section affordance, empty state |
| B | Visual parity | CONDITIONAL PASS | Equivalence by construction (shared runtime code path, machine-enforced no-synthetic rule); live pixel run pending (Stage 0) |
| C | Performance | PENDING | Design certified (rAF coalescing, monotonicity, O(1) rect lookup); live capture pending (Stage 0) |
| D | Stability | PENDING | 96/96 unit/simulated certifications green; live drills (soak, token renewal, reconnect storm) pending (Stage 0) |
| E | Legacy independence | **FAIL (mechanically close)** | Native path still imports `normalizeStoreConfig` from `BuilderThemeProvider.jsx` (`ThemeNativeCanvas.jsx:21`); flag default is OFF; PreviewModal still synthetic |
| F | Deletion readiness | **FAIL (by dependency)** | Blocked by A and E; inventory itself (Section 8) is complete and mechanical |

### FINAL CERTIFICATION: **NOT READY**

The foundation and integration layers are certified, frozen, and healthy (0 regressions, 0 protocol drift, 96/96 tests green). The native canvas is superior wherever it has a capability at all. But editing parity — the specific mandate of this phase — is not achieved: a merchant on the flag-ON path today cannot insert, reorder, duplicate, delete, or hide sections from the canvas. Deleting the legacy Builder now would delete capabilities, not just code.

Deletion readiness criteria for the next assessment (ARCH-1.4R):
1. G1–G6 closed with certification tests (Gate A).
2. Stage 0 drills executed with recorded artifacts (Gates B, C, D).
3. `normalizeStoreConfig` extraction + PreviewModal replacement plan committed (Gate E).
4. Super-Admin shim decision recorded (unblocks D3 or formally descopes it).

When those four items land, deletion is a single mechanical commit with a git-revert rollback — which is the confidence bar this initiative defined.

---
*BUILDER-ARCH-1 · ARCH-1.4 · Principal Platform Validation Architect · All claims verified against live source 2026-07-27.*
