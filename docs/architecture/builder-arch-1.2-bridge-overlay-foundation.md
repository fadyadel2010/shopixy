# BUILDER-ARCH-1 / ARCH-1.2 — Theme Runtime Bridge & Overlay Foundation

Initiative: Theme-Native Homepage Builder
Phase: ARCH-1.2 (runtime infrastructure only — NO canvas migration, NO shim deletion, NO theme changes)
Date: 2026-07-27
Basis: ARCH-1.1 canonical architecture (immutable), ADR-BUILDER-001 (Option A).

## 1. Executive Summary

This phase implements the presentation-agnostic runtime foundation that ARCH-1.1 M1/M2 will consume: Bridge Protocol v1, the parent-side EditorBridge, the storefront-side ChildRuntimeBridge, the framework-free OverlayRuntime, and BridgeDiagnostics — all with deterministic certification tests (41 passing). Nothing in this phase knows about sections, themes, homepages, Pearl, products, or the Builder. The Homepage Builder, Mobile Builder, AI Editor, Visual Diff, Marketplace Preview, and a future SDK are all just hosts of these modules.

Constraint compliance: BuilderCanvas untouched; Homepage Builder untouched; shims untouched; theme rendering untouched. `git status` shows only additive `editor-bridge/` directories, tests, and docs.

## 2. Architecture & Files

```
PointOfSale.Web/src/editor-bridge/            (parent / host side)
  protocol.ts        Bridge Protocol v1 (canonical contract)
  diagnostics.ts     BridgeDiagnostics (Module 5)
  EditorBridge.ts    EditorBridge + createIframeTransport (Module 1)
  OverlayRuntime.ts  OverlayRuntime + RectStore/SelectionStore/
                     CoordinateMapper/AnchorResolver/ViewportTracker (Module 3)
  index.ts           public API barrel (the ONLY sanctioned import path)
  editorBridge.certification.test.ts      24 tests
  protocolParity.certification.test.ts     2 tests (byte-parity rule)

PointOfSale.Storefront/src/editor-bridge/     (child / runtime side)
  protocol.ts        byte-identical copy (certified by parity test)
  diagnostics.ts     byte-identical copy
  ChildRuntimeBridge.ts   Module 2
  index.ts           public API barrel
  childRuntimeBridge.certification.test.ts 15 tests
```

Duplication rule: the protocol is the boundary; the two apps NEVER import each other. `protocol.ts`/`diagnostics.ts` are byte-identical in both apps and a certification test fails the build if they drift.

## 3. Public APIs (no implementation leakage)

EditorBridge (parent):
- `new EditorBridge({transport, expectedOrigin, hostKind, requestedCapabilities, expectedTenantId?, timing?, now?, setTimer?, clearTimer?})`
- `connect()`, `shutdown(reason)`, `getState()`, `getGrantedCapabilities()`, `getRevisions() -> {latest, applied, inSync}`
- Commands: `applyConfig(document) -> revision`, `setSelection(anchorId|null)`, `scrollTo(anchorId, behavior?, block?)`, `setViewport(widthPx, scale?)`
- Events: `on('stateChanged'|'ready'|'handshakeError'|'message'|'revisionApplied'|'peerLost'|'failed', fn)`
- `diagnostics: BridgeDiagnostics`
- Transport is abstract (`MessageTransport`); `createIframeTransport(iframe, childOrigin)` is the production binding. Fakes power tests — the engine is DOM-free.

ChildRuntimeBridge (storefront):
- `new ChildRuntimeBridge({expectedParentOrigin, tenantId?, applyDocument, onSelectionSet?, root?, windowRef?, documentRef?})`
- `start()`, `stop()`, `isConnected()`, `publishRects()` (test hook)
- `applyDocument(document, revision)` is the SINGLE integration point with the runtime; the bridge never inspects the document (presentation-agnostic). Anchors discovered solely via `data-section-id` / `data-section-type` (constants `ANCHOR_ATTRIBUTE`, `ANCHOR_KIND_ATTRIBUTE`) — matching the ARCH-1.1 Theme Contract; the bridge never interprets them.

OverlayRuntime (parent, framework-free — APIs only, zero rendering):
- `attach(bridge)`, `detach()`, `getLifecycle() -> detached|active|degraded`, `onLifecycle(fn)`
- `rects: RectStore` — revision-tagged rect cache, stale marking, scroll-delta compensation between snapshots
- `selection: SelectionStore` — selected + hovered anchor ids (hover mirrored from child; selection HOST-owned via `applyHostSelection`)
- `mapper: CoordinateMapper` — child-viewport <-> overlay-space transforms (frame offset + scale, invertible)
- `viewport: ViewportTracker` — frame offset, editor zoom, child viewport size
- `anchors: AnchorResolver` — `resolveRect`, `resolveToolbar(anchorId, toolbarHeight)` (above/flip-below), `resolveDropIndicator(pointerY) -> {index,y,x,width}` (midpoint rule), `hitTest(point)`
- All stores expose `getState()/subscribe()` (useSyncExternalStore-compatible). No React anywhere.

BridgeDiagnostics (both ends):
- `snapshot() -> {bridgeVersion, peerVersion, state, epoch, handshakeLog[<=50], latency{last,avg,max,samples}, sent, received, droppedForeign, droppedStaleEpoch, droppedStaleRevision, duplicatesIgnored, unknownTypesIgnored, retries, reconnects, lastError, healthy}`

## 4. Protocol Specification (v1)

Envelope (every message): `{marker:'shopixy.editor-bridge', v:'1.0', type, seq, ackOf?, epoch, sentAt, payload}`.
- `seq`: per-sender monotonic; the idempotency key for retries.
- `epoch`: increments per (re)connect; receivers drop non-handshake messages from stale epochs.
- Extension namespace: any `x.*` type flows through untouched; unknown core types are counted, never fatal.

Message catalog (fields documented in `protocol.ts` JSDoc):
- Lifecycle: `hello` (P→C: protocolVersion, requestedCapabilities, hostKind, expectedTenantId?, heartbeatIntervalMs) · `ready` (C→P: protocolVersion, grantedCapabilities, runtime{tenantId?, revision}) · `handshake.error` (VERSION_MISMATCH | TENANT_MISMATCH | ORIGIN_REJECTED | CAPABILITY_REQUIRED) · `heartbeat` · `ack` (ackOf) · `shutdown` (reason).
- State sync (P→C): `config.apply` {revision, document<opaque>} · `selection.set` {anchorId|null} · `scroll.to` {anchorId, behavior, block} · `viewport.set` {widthPx, scale?}.
- Runtime events (C→P): `config.applied` {revision} · `rects.changed` {revision, scrollX/Y, viewportW/H, rects[{anchorId, anchorKind?, x, y, width, height, inViewport, order}]} · `selection.changed` {anchorId|null} (PROPOSAL — host decides) · `hover.changed` · `scroll.changed` · `viewport.changed` · `navigation.blocked` {href} · `runtime.warning` {scope, anchorId?, message} · `runtime.error` {scope, message, recoverable}.

Versioning & capabilities: version = MAJOR.MINOR. MAJOR mismatch → refused. MINOR mismatch → connect with capability intersection (`rects, selection, hover, scroll, viewport, navigation-blocking, diagnostics`, open string set).

Guarantees:
- Transport: at-most-once, ordered per sender (postMessage semantics).
- Commands (`hello, config.apply, selection.set, scroll.to, viewport.set`): at-least-once via bounded retry (ackTimeout 2s ×3, identical envelope/seq), idempotent receive (seq dedupe + re-ack), → effectively once.
- State: revision monotonicity on `config.apply`, `config.applied`, `rects.changed`; stale revisions dropped and counted. Retries + monotonicity = convergent last-writer state.
- Backpressure: `rects.changed` and `hover.changed` coalesced (one per animation frame / no consecutive duplicates); consumers get last-write-wins snapshots, never deltas that can be missed.
- Timeouts: handshake 10s; ack 2s; heartbeat every 5s, peer-lost at 15s; reconnect backoff 500ms→10s (5 attempts) then FAILED. All overridable and injectable for tests.
- Error propagation: `runtime.error{recoverable:false}` or command-retry exhaustion → parent state FAILED → OverlayRuntime lifecycle `degraded` (fail closed per ARCH-1.1 P5).

## 5. Store Ownership

| Store | Location | Owns | Explicitly does NOT own |
|---|---|---|---|
| Bridge session state (seq/epoch/pending/revisions) | EditorBridge + ChildRuntimeBridge internals | delivery, ordering, health | any document content |
| Overlay stores (RectStore, SelectionStore, ViewportTracker) | OverlayRuntime | rect cache, hover, selected-anchor MIRROR, transforms | selection DECISIONS, document truth |
| Builder store (existing `builderStore.js`) | future host (unchanged this phase) | candidates, undo, dirty, selection decisions | rendering, rect math |
| Runtime store (Storefront `storeConfigStore`) | storefront | admitted config for rendering | editor state |
No duplicated ownership: selection decision lives ONLY in the host; the overlay mirrors; the child proposes.

## 6. Failure Modes & Recovery

| Failure | Detection | Recovery |
|---|---|---|
| Iframe reload | heartbeats stop → peerLost | parent auto-reconnects with backoff; child's fresh instance answers the new `hello`; parent re-issues `config.apply` (latest revision) after ready |
| Token expiration | child boot fails → no `ready` / `runtime.error` at boot | handshake timeout → reconnect; host refreshes token and reloads iframe (host policy, outside bridge) |
| Runtime crash | `runtime.error{recoverable:false}` or heartbeat loss | parent FAILED → overlay degraded → host shows fail-closed UI |
| Lost heartbeat (parent view) | 15s deadline | reconnect ladder, then FAILED |
| Lost heartbeat (child view) | 15s deadline | child drops editor affordances, keeps rendering, awaits new hello |
| Bridge version mismatch | handshake compare | `handshake.error VERSION_MISMATCH`, fail closed, operator message |
| Unknown message | type not recognized | ignored + counted (`unknownTypesIgnored`); `x.*` passed to subscribers |
| Duplicate message | seq dedupe set | ignored; ack-required duplicates re-acked idempotently |
| Out-of-order revision | revision comparison | dropped + counted; child re-confirms current revision so the host settles |
| Large payload | structured-clone cost only (no chunking in v1) | rAF coalescing bounds frequency; certification includes rapid-update case; chunking reserved for v1.x via `x.*` |
| Slow runtime | ack within 2s but `config.applied` later | host reads `getRevisions().inSync` to show syncing state |
| Network reconnect | N/A (same-machine postMessage) | covered by iframe reload path |

## 7. Certification Plan & Results

41 automated certification tests, all passing (deterministic: manual timers + fake transports/DOM; no sleeps):
- Web `editorBridge.certification.test.ts` (24): protocol validation; version compatibility; capability intersection; handshake success; version/tenant/handshake.error refusals; origin rejection; handshake-timeout reconnect ladder + exhaustion; revision monotonicity + inSync; bounded at-least-once retry with constant seq; ack latency capture; duplicate-receive idempotency; stale-epoch drop; heartbeat peer-lost + recovery; unknown-type tolerance; overlay activate/degrade (fail closed); rect snapshot ingest + stale marking; scroll-delta compensation; coordinate transforms (offset+scale, inverse); toolbar flip-below; drop-indicator midpoints; hit testing; hover mirroring vs host-owned selection; clean detach.
- Web `protocolParity.certification.test.ts` (2): byte-identical protocol.ts and diagnostics.ts across apps.
- Storefront `childRuntimeBridge.certification.test.ts` (15): ready + capability grant; version/tenant refusal; origin ignore; reconnect via second hello (epoch supersede); opaque apply + ack + applied + monotonicity; duplicate-seq re-ack without re-apply; applier failure → recoverable runtime.error; rect snapshots (order, kind, inViewport); click → selection proposal; navigation blocking; hover coalescing; scroll.to; selection.set mirror hook; unknown/extension tolerance.
- Regression: existing `src/architecture-tests` suite still passes (32/32). `tsc --noEmit` clean in both apps.
Environment-specific certifications deferred to M2 (they need a real iframe): memory-leak soak, theme-switch under embed, true viewport resize. Tracked as M2 entry criteria.

## 8. ADR Update & Readiness

ADR-BUILDER-001 amendment (ARCH-1.2): Bridge Protocol v1 is hereby FROZEN as specified in `protocol.ts` (M0 gate satisfied). Additions before v2 must use MINOR + capabilities or the `x.*` namespace. The byte-parity certification is the enforcement mechanism for the no-shared-package rule.

Implementation readiness for ARCH-1.1 M1/M2:
- M1 needs only: storefront `editorMode` bootstrap branch that constructs `ChildRuntimeBridge` with `applyDocument = store.setConfig + applyTheme` and calls `start()`.
- M2 needs only: host page that creates `createIframeTransport` + `EditorBridge` + `OverlayRuntime.attach`, and a React overlay that subscribes to the stores.
- No foundation module requires changes for either phase; both integration points are single constructor calls.

## 9. Final Certification

**FOUNDATION APPROVED.**

All five modules are implemented, internally consistent, mutually certified (parent and child suites exercise the same protocol from both ends plus byte-parity), presentation-agnostic (zero references to sections/themes/builder concepts — anchors are opaque ids), and reusable by any future host. Constraints honored: no canvas migration, no shim deletion, no theme-rendering changes, no Builder modification. ARCH-1.1 M1 may begin.
