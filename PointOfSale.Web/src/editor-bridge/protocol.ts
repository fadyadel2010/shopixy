/**
 * Editor Bridge Protocol v1 — BUILDER-ARCH-1 / ARCH-1.2 foundation.
 *
 * CANONICAL CONTRACT between an embedding editor host (parent) and the
 * storefront runtime (child). Presentation-agnostic: this module knows
 * nothing about sections, themes, homepages, or any business concept.
 *
 * DUPLICATION RULE: this file exists byte-identically in
 *   PointOfSale.Web/src/editor-bridge/protocol.ts
 *   PointOfSale.Storefront/src/editor-bridge/protocol.ts
 * per ADR-BUILDER-001 (no cross-app imports; the protocol IS the boundary).
 * A certification test asserts byte equality. Change both or neither.
 *
 * VERSIONING: BRIDGE_PROTOCOL_VERSION is MAJOR.MINOR.
 *  - MAJOR mismatch  -> connection refused (handshake error VERSION_MISMATCH).
 *  - MINOR mismatch  -> connect; both sides use the intersection of
 *    negotiated capabilities. Unknown message types are ignored (never fatal).
 *
 * DELIVERY GUARANTEES:
 *  - Transport: postMessage — at-most-once, ordered per sender pair.
 *  - Commands carry `seq`; acknowledged commands (ackOf) are AT-LEAST-ONCE
 *    via sender retry, therefore handlers MUST be idempotent per (seq).
 *  - State-carrying messages (config.apply, rects.changed) carry `revision`;
 *    receivers MUST drop payloads whose revision < last applied revision
 *    (revision monotonicity). Retries + monotonicity = effectively-once state.
 *  - Backpressure: high-frequency events (rects.changed, hover.changed) are
 *    coalesced per animation frame by the child; parents must tolerate
 *    dropped intermediate values (last-write-wins semantics).
 */

export const BRIDGE_PROTOCOL_VERSION = '1.0';
export const BRIDGE_MESSAGE_MARKER = 'shopixy.editor-bridge';

/** Extension namespace prefix: forward-compatible message families.
 *  Any type beginning with `x.` is an extension; unknown extensions are
 *  ignored by both sides without warning-level diagnostics. */
export const BRIDGE_EXTENSION_PREFIX = 'x.';

/** Capabilities negotiable at handshake. Extensible: unknown strings are
 *  preserved but only the intersection is considered active. */
export type BridgeCapability =
    | 'rects'
    | 'selection'
    | 'hover'
    | 'scroll'
    | 'viewport'
    | 'navigation-blocking'
    | 'diagnostics'
    | (string & {});

/** Envelope carried by EVERY bridge message, both directions. */
export interface BridgeEnvelope<TType extends string = string, TPayload = unknown> {
    /** Constant marker so unrelated postMessage traffic is ignored. */
    marker: typeof BRIDGE_MESSAGE_MARKER;
    /** Protocol version of the SENDER ("MAJOR.MINOR"). */
    v: string;
    /** Message type (see BridgeMessage union). */
    type: TType;
    /** Monotonically increasing per-sender sequence number (1-based). */
    seq: number;
    /** When set, this message acknowledges the peer message with seq === ackOf. */
    ackOf?: number;
    /** Sender epoch: increments on every (re)connect; receivers drop
     *  messages from stale epochs after a reconnect. */
    epoch: number;
    /** Milliseconds since UNIX epoch at send time (diagnostics/latency). */
    sentAt: number;
    payload: TPayload;
}

/* ------------------------------------------------------------------ */
/* Handshake / lifecycle                                              */
/* ------------------------------------------------------------------ */

/** parent -> child. Opens (or re-opens) a session. Retried until `ready`. */
export interface HelloPayload {
    protocolVersion: string;
    /** Capabilities the parent wants to use. */
    requestedCapabilities: BridgeCapability[];
    /** Opaque host identity, e.g. 'homepage-builder', 'visual-diff'. */
    hostKind: string;
    /** Tenant the parent believes it is editing; child MUST refuse
     *  (handshake error TENANT_MISMATCH) if its own tenant differs. */
    expectedTenantId?: string;
    /** Heartbeat interval the parent will use, in ms. */
    heartbeatIntervalMs: number;
}

/** child -> parent. Answers `hello`; session becomes CONNECTED. */
export interface ReadyPayload {
    protocolVersion: string;
    /** Capabilities the child actually supports (intersection wins). */
    grantedCapabilities: BridgeCapability[];
    /** Opaque runtime identity data. NOT presentation data. */
    runtime: {
        tenantId?: string;
        /** Active runtime document revision at ready time (0 = boot config). */
        revision: number;
    };
}

/** Either direction. Fatal handshake refusal; session -> FAILED. */
export interface HandshakeErrorPayload {
    code: 'VERSION_MISMATCH' | 'TENANT_MISMATCH' | 'ORIGIN_REJECTED' | 'CAPABILITY_REQUIRED';
    message: string;
    /** Sender's protocol version, for operator display. */
    protocolVersion: string;
}

/** Either direction. Liveness probe; peer replies with plain `ack`. */
export interface HeartbeatPayload {
    /** Local session uptime ms (diagnostics only). */
    uptimeMs: number;
}

/** Either direction. Generic acknowledgement (ackOf REQUIRED on envelope). */
export type AckPayload = Record<never, never>;

/** Either direction. Orderly teardown; receiver stops timers, session -> CLOSED. */
export interface ShutdownPayload {
    reason: 'host-closed' | 'runtime-unloading' | 'superseded' | 'error';
}

/* ------------------------------------------------------------------ */
/* State synchronization (parent -> child)                            */
/* ------------------------------------------------------------------ */

/** parent -> child. Apply a candidate document. OPAQUE to the bridge:
 *  `document` is whatever the runtime's own admission accepts. The bridge
 *  never inspects it (presentation-agnostic rule). Ack = config.applied. */
export interface ConfigApplyPayload {
    /** Monotonic candidate revision (parent-owned counter). */
    revision: number;
    /** Opaque runtime document (e.g. a store config). Never inspected. */
    document: unknown;
}

/** child -> parent. Confirms revision applied & rendered (post-commit). */
export interface ConfigAppliedPayload {
    revision: number;
}

/** parent -> child. Mirror the host's selection into the runtime. */
export interface SelectionSetPayload {
    /** Anchor id (data-anchor-id / data-section-id) or null to clear. */
    anchorId: string | null;
}

/** parent -> child. Scroll an anchor into view. */
export interface ScrollToPayload {
    anchorId: string;
    behavior: 'auto' | 'smooth';
    block: 'start' | 'center' | 'nearest';
}

/** parent -> child. Editor viewport emulation (responsive preview). */
export interface ViewportSetPayload {
    widthPx: number;
    /** Optional scale hint the child may apply to its root. */
    scale?: number;
}

/* ------------------------------------------------------------------ */
/* Runtime events (child -> parent)                                   */
/* ------------------------------------------------------------------ */

/** Serializable anchor rectangle in CHILD viewport coordinates (CSS px). */
export interface AnchorRect {
    anchorId: string;
    /** Opaque anchor kind (e.g. a section type). Bridge never interprets it. */
    anchorKind?: string;
    x: number;
    y: number;
    width: number;
    height: number;
    /** True when at least partially inside the child viewport. */
    inViewport: boolean;
    /** Document order index among reported anchors. */
    order: number;
}

/** child -> parent. Full snapshot (not a delta) of all anchor rects,
 *  coalesced per animation frame. Tagged with the applied revision so the
 *  parent can discard rects describing an outdated document. */
export interface RectsChangedPayload {
    revision: number;
    /** Child scroll offsets at measure time. */
    scrollX: number;
    scrollY: number;
    /** Child viewport size at measure time. */
    viewportWidth: number;
    viewportHeight: number;
    rects: AnchorRect[];
}

/** child -> parent. User clicked an anchor (selection PROPOSAL — the
 *  parent owns selection and answers with selection.set). */
export interface SelectionChangedPayload {
    anchorId: string | null;
}

/** child -> parent. Pointer entered/left an anchor. Coalesced. */
export interface HoverChangedPayload {
    anchorId: string | null;
}

/** child -> parent. Child scrolled (parent may reposition chrome
 *  eagerly before the next rects snapshot arrives). Coalesced. */
export interface ScrollChangedPayload {
    scrollX: number;
    scrollY: number;
}

/** child -> parent. Child viewport resized. */
export interface ViewportChangedPayload {
    viewportWidth: number;
    viewportHeight: number;
}

/** child -> parent. In-page navigation was intercepted in editor mode. */
export interface NavigationBlockedPayload {
    href: string;
}

/** child -> parent. Non-fatal runtime problem (scoped error boundary etc.). */
export interface RuntimeWarningPayload {
    scope: string;
    anchorId?: string;
    message: string;
}

/** child -> parent. Fatal runtime failure; parent should fail closed. */
export interface RuntimeErrorPayload {
    scope: string;
    message: string;
    recoverable: boolean;
}

/* ------------------------------------------------------------------ */
/* Message union & constructors                                       */
/* ------------------------------------------------------------------ */

export type BridgeMessage =
    | BridgeEnvelope<'hello', HelloPayload>
    | BridgeEnvelope<'ready', ReadyPayload>
    | BridgeEnvelope<'handshake.error', HandshakeErrorPayload>
    | BridgeEnvelope<'heartbeat', HeartbeatPayload>
    | BridgeEnvelope<'ack', AckPayload>
    | BridgeEnvelope<'shutdown', ShutdownPayload>
    | BridgeEnvelope<'config.apply', ConfigApplyPayload>
    | BridgeEnvelope<'config.applied', ConfigAppliedPayload>
    | BridgeEnvelope<'selection.set', SelectionSetPayload>
    | BridgeEnvelope<'selection.changed', SelectionChangedPayload>
    | BridgeEnvelope<'hover.changed', HoverChangedPayload>
    | BridgeEnvelope<'rects.changed', RectsChangedPayload>
    | BridgeEnvelope<'scroll.to', ScrollToPayload>
    | BridgeEnvelope<'scroll.changed', ScrollChangedPayload>
    | BridgeEnvelope<'viewport.set', ViewportSetPayload>
    | BridgeEnvelope<'viewport.changed', ViewportChangedPayload>
    | BridgeEnvelope<'navigation.blocked', NavigationBlockedPayload>
    | BridgeEnvelope<'runtime.warning', RuntimeWarningPayload>
    | BridgeEnvelope<'runtime.error', RuntimeErrorPayload>
    | BridgeEnvelope<string, unknown>; // `x.*` extension namespace

export type BridgeMessageType = BridgeMessage['type'];

/** Commands that REQUIRE an acknowledgement and are retried until acked. */
export const ACK_REQUIRED_TYPES: readonly string[] = [
    'hello',
    'config.apply',
    'selection.set',
    'scroll.to',
    'viewport.set',
];

/** Messages exempt from epoch checks (they establish/refuse the epoch). */
export const EPOCH_EXEMPT_TYPES: readonly string[] = ['hello', 'ready', 'handshake.error'];

/** Timing knobs shared by both bridge ends. */
export interface BridgeTimings {
    heartbeatIntervalMs: number;
    /** Missed-heartbeat window before the peer is declared lost. */
    heartbeatTimeoutMs: number;
    /** Per-command ack timeout before a retry is issued. */
    ackTimeoutMs: number;
    /** Max retries per ack-required command before session -> FAILED. */
    maxCommandRetries: number;
    /** Handshake overall deadline. */
    handshakeTimeoutMs: number;
    /** Backoff schedule for reconnect attempts (ms). */
    reconnectBackoffMs: readonly number[];
}

/** Default timing constants (overridable per bridge instance). */
export const BRIDGE_DEFAULTS: BridgeTimings = {
    heartbeatIntervalMs: 5_000,
    heartbeatTimeoutMs: 15_000,
    ackTimeoutMs: 2_000,
    maxCommandRetries: 3,
    handshakeTimeoutMs: 10_000,
    reconnectBackoffMs: [500, 1_000, 2_000, 5_000, 10_000],
};

export function parseProtocolVersion(version: string): { major: number; minor: number } | null {
    const match = /^(\d+)\.(\d+)$/.exec(version);
    if (!match) return null;
    return { major: Number(match[1]), minor: Number(match[2]) };
}

/** MAJOR must match exactly; MINOR may differ (capability intersection). */
export function isProtocolCompatible(local: string, remote: string): boolean {
    const a = parseProtocolVersion(local);
    const b = parseProtocolVersion(remote);
    return a !== null && b !== null && a.major === b.major;
}

export function intersectCapabilities(
    requested: readonly BridgeCapability[],
    supported: readonly BridgeCapability[]
): BridgeCapability[] {
    const supportedSet = new Set(supported);
    return requested.filter((capability) => supportedSet.has(capability));
}

/** Structural validation of an inbound raw message. Returns null when the
 *  value is not a bridge message at all (foreign postMessage traffic). */
export function asBridgeMessage(value: unknown): BridgeMessage | null {
    if (typeof value !== 'object' || value === null) return null;
    const candidate = value as Partial<BridgeEnvelope>;
    if (candidate.marker !== BRIDGE_MESSAGE_MARKER) return null;
    if (typeof candidate.v !== 'string') return null;
    if (typeof candidate.type !== 'string' || candidate.type.length === 0) return null;
    if (typeof candidate.seq !== 'number' || !Number.isFinite(candidate.seq)) return null;
    if (typeof candidate.epoch !== 'number' || !Number.isFinite(candidate.epoch)) return null;
    if (typeof candidate.sentAt !== 'number') return null;
    if (!('payload' in candidate)) return null;
    return candidate as BridgeMessage;
}

export function isExtensionType(type: string): boolean {
    return type.startsWith(BRIDGE_EXTENSION_PREFIX);
}
