/**
 * Child Runtime Bridge — ARCH-1.2 Module 2. Runs INSIDE the storefront.
 *
 * Responsibilities: answer handshake, report readiness, measure anchor
 * rects (ResizeObserver + IntersectionObserver + scroll), relay selection/
 * hover proposals, block navigation in editor mode, apply opaque candidate
 * documents through an injected applier.
 *
 * Contains ZERO builder logic and ZERO presentation knowledge. Anchors are
 * discovered purely via the `data-section-id` attribute contract
 * (ARCH-1.1 Theme Contract rule 3); this module never interprets them.
 */

import {
    ACK_REQUIRED_TYPES,
    BRIDGE_DEFAULTS,
    BRIDGE_MESSAGE_MARKER,
    BRIDGE_PROTOCOL_VERSION,
    EPOCH_EXEMPT_TYPES,
    asBridgeMessage,
    intersectCapabilities,
    isProtocolCompatible,
    type AnchorRect,
    type BridgeCapability,
    type BridgeEnvelope,
    type BridgeMessage,
    type ConfigApplyPayload,
    type HelloPayload,
    type ScrollToPayload,
    type SelectionSetPayload,
    type ViewportSetPayload,
} from './protocol';

export const ANCHOR_ATTRIBUTE = 'data-section-id';
export const ANCHOR_KIND_ATTRIBUTE = 'data-section-type';

const CHILD_CAPABILITIES: BridgeCapability[] = [
    'rects',
    'selection',
    'hover',
    'scroll',
    'viewport',
    'navigation-blocking',
    'diagnostics',
];

export interface ChildRuntimeBridgeOptions {
    /** Exact origin of the embedding host. REQUIRED (origin validation). */
    expectedParentOrigin: string;
    /** Tenant this runtime is serving (echoed at ready; hello may refuse). */
    tenantId?: string;
    /**
     * Opaque document applier — the ONLY integration point with the runtime.
     * Receives the candidate document; resolves when committed & rendered.
     * The bridge never inspects the document (presentation-agnostic rule).
     */
    applyDocument: (document: unknown, revision: number) => Promise<void> | void;
    /** Optional selection mirror (e.g. sets renderer's selectedSectionId). */
    onSelectionSet?: (anchorId: string | null) => void;
    /** Root to observe for anchors. Defaults to document.body. */
    root?: HTMLElement;
    windowRef?: Window;
    documentRef?: Document;
}

interface Timers {
    raf: number | null;
    heartbeat: ReturnType<typeof setTimeout> | null;
    heartbeatDeadline: ReturnType<typeof setTimeout> | null;
}

export class ChildRuntimeBridge {
    private readonly options: ChildRuntimeBridgeOptions;
    private readonly win: Window;
    private readonly doc: Document;

    private parentWindow: Window | null = null;
    private seq = 0;
    private epoch = 0;
    private parentEpoch: number | null = null;
    private processedParentSeqs = new Set<number>();
    private appliedRevision = 0;
    private grantedCapabilities: BridgeCapability[] = [];
    private heartbeatIntervalMs = BRIDGE_DEFAULTS.heartbeatIntervalMs;
    private started = false;
    private connected = false;

    private resizeObserver: ResizeObserver | null = null;
    private intersectionObserver: IntersectionObserver | null = null;
    private mutationObserver: MutationObserver | null = null;
    private timers: Timers = { raf: null, heartbeat: null, heartbeatDeadline: null };
    private rectsDirty = false;
    /** Process-lifetime disposers (message + beforeunload). Removed by stop() only. */
    private disposers: Array<() => void> = [];
    /** Per-session disposers (scroll/resize/click/pointer). Removed on every session teardown. */
    private sessionDisposers: Array<() => void> = [];
    /** Highest config.apply revision received (async-race guard). */
    private latestReceivedRevision = 0;
    /** Serializes async appliers so documents commit in arrival order. */
    private applyChain: Promise<void> = Promise.resolve();

    constructor(options: ChildRuntimeBridgeOptions) {
        this.options = options;
        this.win = options.windowRef ?? window;
        this.doc = options.documentRef ?? document;
    }

    /** Begin listening for a host handshake. Safe to call once at bootstrap. */
    start(): void {
        if (this.started) return;
        this.started = true;
        const onMessage = (event: MessageEvent) => this.onRaw(event);
        this.win.addEventListener('message', onMessage);
        this.disposers.push(() => this.win.removeEventListener('message', onMessage));

        const onUnload = () => this.sendToParent('shutdown', { reason: 'runtime-unloading' });
        this.win.addEventListener('beforeunload', onUnload);
        this.disposers.push(() => this.win.removeEventListener('beforeunload', onUnload));
    }

    stop(): void {
        this.teardownSession();
        for (const dispose of this.disposers) dispose();
        this.disposers = [];
        this.started = false;
        this.connected = false;
    }

    isConnected(): boolean {
        return this.connected;
    }

    /* ---------------- inbound ---------------- */

    private onRaw(event: MessageEvent): void {
        if (event.origin !== this.options.expectedParentOrigin) return; // origin validation
        const message = asBridgeMessage(event.data);
        if (!message) return;

        if (message.type === 'hello') {
            this.onHello(message as BridgeEnvelope<'hello', HelloPayload>, event.source as Window);
            return;
        }

        if (!this.connected) return;
        if (!EPOCH_EXEMPT_TYPES.includes(message.type)) {
            if (message.epoch !== this.parentEpoch) return; // stale epoch
            if (this.processedParentSeqs.has(message.seq)) {
                // duplicate (retry) — re-ack idempotently, do not re-apply
                if (ACK_REQUIRED_TYPES.includes(message.type)) {
                    this.sendToParent('ack', {}, { ackOf: message.seq });
                }
                return;
            }
            this.processedParentSeqs.add(message.seq);
            if (this.processedParentSeqs.size > 2_000) {
                this.processedParentSeqs = new Set([...this.processedParentSeqs].slice(-1_000));
            }
        }

        this.route(message);
    }

    private onHello(message: BridgeEnvelope<'hello', HelloPayload>, source: Window): void {
        const payload = message.payload;
        this.parentWindow = source;

        if (!isProtocolCompatible(BRIDGE_PROTOCOL_VERSION, payload.protocolVersion)) {
            this.sendToParent('handshake.error', {
                code: 'VERSION_MISMATCH',
                message: `Runtime speaks ${BRIDGE_PROTOCOL_VERSION}; host speaks ${payload.protocolVersion}.`,
                protocolVersion: BRIDGE_PROTOCOL_VERSION,
            });
            return;
        }
        if (
            payload.expectedTenantId &&
            payload.expectedTenantId !== this.options.tenantId
        ) {
            // Fail closed: when the host expects a tenant, a runtime with a
            // DIFFERENT tenant *or NO resolved tenant* must both refuse.
            this.sendToParent('handshake.error', {
                code: 'TENANT_MISMATCH',
                message: this.options.tenantId
                    ? 'Runtime tenant does not match the host expectation.'
                    : 'Host expects a tenant but the runtime has none resolved.',
                protocolVersion: BRIDGE_PROTOCOL_VERSION,
            });
            return;
        }

        // New epoch supersedes any previous session (reconnect support):
        // tear the old session down FIRST so listeners/timers never stack.
        this.teardownSession();
        this.parentEpoch = message.epoch;
        this.epoch += 1;
        this.processedParentSeqs.clear();
        this.connected = true;
        this.heartbeatIntervalMs = payload.heartbeatIntervalMs || BRIDGE_DEFAULTS.heartbeatIntervalMs;
        this.grantedCapabilities = intersectCapabilities(payload.requestedCapabilities, CHILD_CAPABILITIES);

        this.sendToParent(
            'ready',
            {
                protocolVersion: BRIDGE_PROTOCOL_VERSION,
                grantedCapabilities: this.grantedCapabilities,
                runtime: { tenantId: this.options.tenantId, revision: this.appliedRevision },
            },
            { ackOf: message.seq }
        );

        this.setupObservers();
        this.startHeartbeat();
        this.scheduleRects();
    }

    private route(message: BridgeMessage): void {
        switch (message.type) {
            case 'config.apply':
                void this.onConfigApply(message as BridgeEnvelope<'config.apply', ConfigApplyPayload>);
                return;
            case 'selection.set': {
                this.sendToParent('ack', {}, { ackOf: message.seq });
                const payload = message.payload as SelectionSetPayload;
                this.options.onSelectionSet?.(payload.anchorId);
                return;
            }
            case 'scroll.to': {
                this.sendToParent('ack', {}, { ackOf: message.seq });
                const payload = message.payload as ScrollToPayload;
                const target = this.findAnchorElement(payload.anchorId);
                target?.scrollIntoView({ behavior: payload.behavior, block: payload.block });
                return;
            }
            case 'viewport.set': {
                this.sendToParent('ack', {}, { ackOf: message.seq });
                const payload = message.payload as ViewportSetPayload;
                this.sendToParent('viewport.changed', {
                    viewportWidth: payload.widthPx,
                    viewportHeight: this.win.innerHeight,
                });
                this.scheduleRects();
                return;
            }
            case 'heartbeat':
                this.armHeartbeatDeadline();
                this.sendToParent('ack', {}, { ackOf: message.seq });
                return;
            case 'ack':
                this.armHeartbeatDeadline();
                return;
            case 'shutdown':
                this.connected = false;
                this.teardownSession();
                return;
            default:
                return; // unknown / extension types: ignored, never fatal
        }
    }

    private async onConfigApply(message: BridgeEnvelope<'config.apply', ConfigApplyPayload>): Promise<void> {
        const { revision, document: candidate } = message.payload;
        this.sendToParent('ack', {}, { ackOf: message.seq });
        if (revision <= this.appliedRevision || revision <= this.latestReceivedRevision) {
            // Out-of-order or duplicate revision: drop (monotonicity), but
            // re-confirm so the host can settle.
            this.sendToParent('config.applied', { revision: this.appliedRevision });
            return;
        }
        this.latestReceivedRevision = revision;

        // Serialize appliers (async-safe): each apply awaits the previous
        // one, and a completion only commits if its revision is still the
        // latest received (latest-wins; stale completions are ignored).
        this.applyChain = this.applyChain.then(async () => {
            if (revision !== this.latestReceivedRevision) {
                // A newer document arrived while queued — skip this one.
                return;
            }
            try {
                await this.options.applyDocument(candidate, revision);
                if (revision !== this.latestReceivedRevision) {
                    // Superseded during an async apply — do not commit.
                    return;
                }
                this.appliedRevision = revision;
                this.sendToParent('config.applied', { revision });
                this.scheduleRects();
            } catch (error) {
                this.sendToParent('runtime.error', {
                    scope: 'config.apply',
                    message: error instanceof Error ? error.message : 'Failed to apply candidate document.',
                    recoverable: true,
                });
            }
        });
        await this.applyChain;
    }

    /* ---------------- outbound ---------------- */

    private sendToParent(type: string, payload: unknown, opts?: { ackOf?: number }): void {
        if (!this.parentWindow) return;
        this.seq += 1;
        const envelope: BridgeEnvelope<string, unknown> = {
            marker: BRIDGE_MESSAGE_MARKER,
            v: BRIDGE_PROTOCOL_VERSION,
            type,
            seq: this.seq,
            epoch: this.epoch,
            sentAt: Date.now(),
            payload,
            ...(opts?.ackOf !== undefined ? { ackOf: opts.ackOf } : {}),
        };
        this.parentWindow.postMessage(envelope, this.options.expectedParentOrigin);
    }

    /* ---------------- heartbeat ---------------- */

    private startHeartbeat(): void {
        if (this.timers.heartbeat) clearTimeout(this.timers.heartbeat);
        const tick = () => {
            this.sendToParent('heartbeat', { uptimeMs: 0 });
            this.timers.heartbeat = setTimeout(tick, this.heartbeatIntervalMs);
        };
        this.timers.heartbeat = setTimeout(tick, this.heartbeatIntervalMs);
        this.armHeartbeatDeadline();
    }

    private armHeartbeatDeadline(): void {
        if (this.timers.heartbeatDeadline) clearTimeout(this.timers.heartbeatDeadline);
        this.timers.heartbeatDeadline = setTimeout(() => {
            // Host lost: keep rendering, drop editor affordances, await new hello.
            this.connected = false;
            this.teardownSession();
        }, BRIDGE_DEFAULTS.heartbeatTimeoutMs);
    }

    /* ---------------- anchors & observation ---------------- */

    private getRoot(): HTMLElement {
        return this.options.root ?? this.doc.body;
    }

    private findAnchorElements(): HTMLElement[] {
        return Array.from(this.getRoot().querySelectorAll<HTMLElement>(`[${ANCHOR_ATTRIBUTE}]`));
    }

    private findAnchorElement(anchorId: string): HTMLElement | null {
        return this.findAnchorElements().find((el) => el.getAttribute(ANCHOR_ATTRIBUTE) === anchorId) ?? null;
    }

    private setupObservers(): void {
        this.teardownObservers();

        if (typeof ResizeObserver !== 'undefined') {
            this.resizeObserver = new ResizeObserver(() => this.scheduleRects());
        }
        if (typeof IntersectionObserver !== 'undefined') {
            this.intersectionObserver = new IntersectionObserver(() => this.scheduleRects());
        }
        if (typeof MutationObserver !== 'undefined') {
            this.mutationObserver = new MutationObserver(() => {
                this.observeAnchors();
                this.scheduleRects();
            });
            this.mutationObserver.observe(this.getRoot(), { childList: true, subtree: true });
        }
        this.observeAnchors();

        const onScroll = () => {
            this.sendToParent('scroll.changed', { scrollX: this.win.scrollX, scrollY: this.win.scrollY });
            this.scheduleRects();
        };
        const onResize = () => {
            this.sendToParent('viewport.changed', {
                viewportWidth: this.win.innerWidth,
                viewportHeight: this.win.innerHeight,
            });
            this.scheduleRects();
        };
        const onClickCapture = (event: Event) => this.onDocumentClick(event);
        const onPointerOver = (event: Event) => this.onPointerOver(event);

        this.win.addEventListener('scroll', onScroll, { passive: true });
        this.win.addEventListener('resize', onResize);
        this.doc.addEventListener('click', onClickCapture, true);
        this.doc.addEventListener('pointerover', onPointerOver, true);

        this.sessionDisposers.push(
            () => this.win.removeEventListener('scroll', onScroll),
            () => this.win.removeEventListener('resize', onResize),
            () => this.doc.removeEventListener('click', onClickCapture, true),
            () => this.doc.removeEventListener('pointerover', onPointerOver, true)
        );
    }

    private observeAnchors(): void {
        if (!this.resizeObserver && !this.intersectionObserver) return;
        this.resizeObserver?.disconnect();
        this.intersectionObserver?.disconnect();
        for (const element of this.findAnchorElements()) {
            this.resizeObserver?.observe(element);
            this.intersectionObserver?.observe(element);
        }
    }

    private teardownObservers(): void {
        this.resizeObserver?.disconnect();
        this.intersectionObserver?.disconnect();
        this.mutationObserver?.disconnect();
        this.resizeObserver = null;
        this.intersectionObserver = null;
        this.mutationObserver = null;
        if (this.timers.raf !== null && typeof cancelAnimationFrame !== 'undefined') {
            cancelAnimationFrame(this.timers.raf);
            this.timers.raf = null;
        }
    }

    /**
     * Full per-session teardown: observers + session DOM listeners +
     * heartbeat timers + dirty state. Called on second hello (reconnect),
     * shutdown, heartbeat loss, and stop. Leaves process-lifetime
     * listeners (message/beforeunload) intact so a new hello can arrive.
     */
    private teardownSession(): void {
        this.teardownObservers();
        for (const dispose of this.sessionDisposers) dispose();
        this.sessionDisposers = [];
        if (this.timers.heartbeat) {
            clearTimeout(this.timers.heartbeat);
            this.timers.heartbeat = null;
        }
        if (this.timers.heartbeatDeadline) {
            clearTimeout(this.timers.heartbeatDeadline);
            this.timers.heartbeatDeadline = null;
        }
        this.rectsDirty = false;
    }

    /** Coalesce rect snapshots to one per animation frame (backpressure). */
    private scheduleRects(): void {
        if (!this.connected) return;
        this.rectsDirty = true;
        if (this.timers.raf !== null) return;
        const flush = () => {
            this.timers.raf = null;
            if (!this.rectsDirty || !this.connected) return;
            this.rectsDirty = false;
            this.publishRects();
        };
        if (typeof requestAnimationFrame !== 'undefined') {
            this.timers.raf = requestAnimationFrame(flush);
        } else {
            setTimeout(flush, 16);
        }
    }

    /** PUBLIC for deterministic tests: force an immediate snapshot. */
    publishRects(): void {
        if (!this.parentWindow) return;
        const viewportWidth = this.win.innerWidth;
        const viewportHeight = this.win.innerHeight;
        const rects: AnchorRect[] = this.findAnchorElements().map((element, order) => {
            const rect = element.getBoundingClientRect();
            return {
                anchorId: element.getAttribute(ANCHOR_ATTRIBUTE) ?? '',
                anchorKind: element.getAttribute(ANCHOR_KIND_ATTRIBUTE) ?? undefined,
                x: rect.x,
                y: rect.y,
                width: rect.width,
                height: rect.height,
                inViewport: rect.bottom > 0 && rect.top < viewportHeight,
                order,
            };
        });
        this.sendToParent('rects.changed', {
            revision: this.appliedRevision,
            scrollX: this.win.scrollX,
            scrollY: this.win.scrollY,
            viewportWidth,
            viewportHeight,
            rects,
        });
    }

    /* ---------------- interaction relays ---------------- */

    private onDocumentClick(event: Event): void {
        const target = event.target as HTMLElement | null;
        if (!target) return;

        // Navigation blocking: swallow link navigation in editor mode.
        const link = target.closest('a[href]');
        if (link) {
            event.preventDefault();
            event.stopPropagation();
            this.sendToParent('navigation.blocked', { href: link.getAttribute('href') ?? '' });
        }

        const anchor = target.closest<HTMLElement>(`[${ANCHOR_ATTRIBUTE}]`);
        this.sendToParent('selection.changed', {
            anchorId: anchor?.getAttribute(ANCHOR_ATTRIBUTE) ?? null,
        });
    }

    private lastHoverId: string | null = null;

    private onPointerOver(event: Event): void {
        const target = event.target as HTMLElement | null;
        const anchor = target?.closest<HTMLElement>(`[${ANCHOR_ATTRIBUTE}]`) ?? null;
        const anchorId = anchor?.getAttribute(ANCHOR_ATTRIBUTE) ?? null;
        if (anchorId === this.lastHoverId) return; // coalesce
        this.lastHoverId = anchorId;
        this.sendToParent('hover.changed', { anchorId });
    }
}
