/**
 * Overlay Runtime — ARCH-1.2 Module 3.
 *
 * Framework-free overlay foundation. Exposes stores + pure APIs ONLY:
 * rect cache, selection state, hover state, coordinate transforms,
 * anchor resolution (toolbar anchors, drop indicators), viewport scaling,
 * lifecycle. Renders NOTHING. Knows nothing about sections/themes.
 *
 * Consumers (React overlay in a later phase, mobile editor, plugins)
 * subscribe to the runtime and draw their own chrome.
 */

import type { EditorBridge } from './EditorBridge';
import type {
    AnchorRect,
    HoverChangedPayload,
    RectsChangedPayload,
    ScrollChangedPayload,
    SelectionChangedPayload,
    ViewportChangedPayload,
} from './protocol';

/* ---------------- primitives ---------------- */

export interface OverlayPoint {
    x: number;
    y: number;
}

export interface OverlayRect extends OverlayPoint {
    width: number;
    height: number;
}

type Subscriber = () => void;

/** Minimal external-store base (compatible with useSyncExternalStore). */
abstract class MiniStore<TState> {
    protected state: TState;
    private subscribers = new Set<Subscriber>();

    constructor(initial: TState) {
        this.state = initial;
    }

    getState(): TState {
        return this.state;
    }

    subscribe(subscriber: Subscriber): () => void {
        this.subscribers.add(subscriber);
        return () => this.subscribers.delete(subscriber);
    }

    protected setState(next: TState): void {
        if (Object.is(next, this.state)) return;
        this.state = next;
        for (const subscriber of [...this.subscribers]) subscriber();
    }
}

/* ---------------- RectStore ---------------- */

export interface RectStoreState {
    /** Anchor rects in CHILD viewport coordinates, keyed by anchorId. */
    rects: ReadonlyMap<string, AnchorRect>;
    /** Document-order anchor ids. */
    order: readonly string[];
    /** Revision the rects describe. */
    revision: number;
    /** True when rects describe an older revision than the latest applied. */
    stale: boolean;
    childScroll: OverlayPoint;
    childViewport: { width: number; height: number };
}

export class RectStore extends MiniStore<RectStoreState> {
    constructor() {
        super({
            rects: new Map(),
            order: [],
            revision: 0,
            stale: false,
            childScroll: { x: 0, y: 0 },
            childViewport: { width: 0, height: 0 },
        });
    }

    ingestSnapshot(payload: RectsChangedPayload, latestAppliedRevision: number): void {
        if (payload.revision < this.state.revision) return; // monotonicity
        const rects = new Map<string, AnchorRect>();
        const order = [...payload.rects]
            .sort((a, b) => a.order - b.order)
            .map((rect) => {
                rects.set(rect.anchorId, rect);
                return rect.anchorId;
            });
        this.setState({
            rects,
            order,
            revision: payload.revision,
            stale: payload.revision < latestAppliedRevision,
            childScroll: { x: payload.scrollX, y: payload.scrollY },
            childViewport: { width: payload.viewportWidth, height: payload.viewportHeight },
        });
    }

    markStale(latestAppliedRevision: number): void {
        this.setState({ ...this.state, stale: this.state.revision < latestAppliedRevision });
    }

    applyScroll(payload: ScrollChangedPayload): void {
        const deltaX = payload.scrollX - this.state.childScroll.x;
        const deltaY = payload.scrollY - this.state.childScroll.y;
        if (deltaX === 0 && deltaY === 0) return;
        const rects = new Map<string, AnchorRect>();
        for (const [anchorId, rect] of this.state.rects) {
            rects.set(anchorId, { ...rect, x: rect.x - deltaX, y: rect.y - deltaY });
        }
        this.setState({
            ...this.state,
            rects,
            childScroll: { x: payload.scrollX, y: payload.scrollY },
        });
    }

    applyViewport(payload: ViewportChangedPayload): void {
        this.setState({
            ...this.state,
            childViewport: { width: payload.viewportWidth, height: payload.viewportHeight },
        });
    }

    getRect(anchorId: string): AnchorRect | null {
        return this.state.rects.get(anchorId) ?? null;
    }
}

/* ---------------- SelectionStore ---------------- */

export interface SelectionState {
    selectedAnchorId: string | null;
    hoveredAnchorId: string | null;
}

export class SelectionStore extends MiniStore<SelectionState> {
    constructor() {
        super({ selectedAnchorId: null, hoveredAnchorId: null });
    }

    select(anchorId: string | null): void {
        if (this.state.selectedAnchorId === anchorId) return;
        this.setState({ ...this.state, selectedAnchorId: anchorId });
    }

    hover(anchorId: string | null): void {
        if (this.state.hoveredAnchorId === anchorId) return;
        this.setState({ ...this.state, hoveredAnchorId: anchorId });
    }

    clear(): void {
        this.setState({ selectedAnchorId: null, hoveredAnchorId: null });
    }
}

/* ---------------- CoordinateMapper ---------------- */

/**
 * Maps CHILD viewport coordinates -> PARENT overlay coordinates.
 * frameOffset = iframe position inside the overlay's positioning context.
 * scale = editor zoom applied to the iframe (viewport scaling).
 */
export class CoordinateMapper {
    private frameOffset: OverlayPoint = { x: 0, y: 0 };
    private scale = 1;

    setFrameOffset(offset: OverlayPoint): void {
        this.frameOffset = offset;
    }

    setScale(scale: number): void {
        if (scale <= 0 || !Number.isFinite(scale)) throw new Error('Scale must be a positive finite number.');
        this.scale = scale;
    }

    getScale(): number {
        return this.scale;
    }

    toOverlayPoint(point: OverlayPoint): OverlayPoint {
        return {
            x: this.frameOffset.x + point.x * this.scale,
            y: this.frameOffset.y + point.y * this.scale,
        };
    }

    toOverlayRect(rect: OverlayRect): OverlayRect {
        const origin = this.toOverlayPoint(rect);
        return { ...origin, width: rect.width * this.scale, height: rect.height * this.scale };
    }

    /** Inverse mapping (parent overlay point -> child viewport point). */
    toChildPoint(point: OverlayPoint): OverlayPoint {
        return {
            x: (point.x - this.frameOffset.x) / this.scale,
            y: (point.y - this.frameOffset.y) / this.scale,
        };
    }
}

/* ---------------- AnchorResolver ---------------- */

export interface ToolbarAnchor {
    anchorId: string;
    /** Overlay-space position for a toolbar; placement tells which edge. */
    position: OverlayPoint;
    placement: 'above' | 'below';
    anchorRect: OverlayRect;
}

export interface DropIndicator {
    /** Insertion index in document order (0..n). */
    index: number;
    /** Overlay-space y coordinate for a horizontal indicator line. */
    y: number;
    x: number;
    width: number;
}

export class AnchorResolver {
    constructor(
        private readonly rectStore: RectStore,
        private readonly mapper: CoordinateMapper
    ) {}

    /** Overlay-space rect of an anchor, or null when unknown. */
    resolveRect(anchorId: string): OverlayRect | null {
        const rect = this.rectStore.getRect(anchorId);
        if (!rect) return null;
        return this.mapper.toOverlayRect(rect);
    }

    /** Toolbar anchor above the target; flips below when clipped at top. */
    resolveToolbar(anchorId: string, toolbarHeight: number, viewportTop = 0): ToolbarAnchor | null {
        const overlayRect = this.resolveRect(anchorId);
        if (!overlayRect) return null;
        const aboveY = overlayRect.y - toolbarHeight;
        const placement: 'above' | 'below' = aboveY < viewportTop ? 'below' : 'above';
        return {
            anchorId,
            placement,
            position: {
                x: overlayRect.x,
                y: placement === 'above' ? aboveY : overlayRect.y + overlayRect.height,
            },
            anchorRect: overlayRect,
        };
    }

    /** Drop indicator for a pointer at overlay-space y. Uses anchor midpoints. */
    resolveDropIndicator(pointerOverlayY: number): DropIndicator | null {
        const { order } = this.rectStore.getState();
        if (order.length === 0) return null;

        let index = order.length;
        let lineY: number | null = null;
        let x = 0;
        let width = 0;

        for (let i = 0; i < order.length; i += 1) {
            const overlayRect = this.resolveRect(order[i]);
            if (!overlayRect) continue;
            x = overlayRect.x;
            width = overlayRect.width;
            const midpoint = overlayRect.y + overlayRect.height / 2;
            if (pointerOverlayY < midpoint) {
                index = i;
                lineY = overlayRect.y;
                break;
            }
            lineY = overlayRect.y + overlayRect.height;
        }

        if (lineY === null) return null;
        return { index, y: lineY, x, width };
    }

    /** Anchor at an overlay-space point (topmost by document order), or null. */
    hitTest(point: OverlayPoint): string | null {
        const { order } = this.rectStore.getState();
        for (let i = order.length - 1; i >= 0; i -= 1) {
            const rect = this.resolveRect(order[i]);
            if (!rect) continue;
            if (
                point.x >= rect.x &&
                point.x <= rect.x + rect.width &&
                point.y >= rect.y &&
                point.y <= rect.y + rect.height
            ) {
                return order[i];
            }
        }
        return null;
    }
}

/* ---------------- ViewportTracker ---------------- */

export interface ViewportState {
    /** iframe offset within overlay positioning context. */
    frameOffset: OverlayPoint;
    scale: number;
    childViewport: { width: number; height: number };
}

export class ViewportTracker extends MiniStore<ViewportState> {
    constructor(private readonly mapper: CoordinateMapper) {
        super({ frameOffset: { x: 0, y: 0 }, scale: 1, childViewport: { width: 0, height: 0 } });
    }

    setFrameOffset(offset: OverlayPoint): void {
        this.mapper.setFrameOffset(offset);
        this.setState({ ...this.getState(), frameOffset: offset });
    }

    setScale(scale: number): void {
        this.mapper.setScale(scale);
        this.setState({ ...this.getState(), scale });
    }

    setChildViewport(width: number, height: number): void {
        this.setState({ ...this.getState(), childViewport: { width, height } });
    }
}

/* ---------------- OverlayRuntime ---------------- */

export type OverlayLifecycle = 'detached' | 'active' | 'degraded';

/**
 * Composition root for the overlay foundation. Wires a bridge to the
 * stores; exposes APIs only. UI layers subscribe and render chrome.
 */
export class OverlayRuntime {
    readonly rects = new RectStore();
    readonly selection = new SelectionStore();
    readonly mapper = new CoordinateMapper();
    readonly viewport = new ViewportTracker(this.mapper);
    readonly anchors = new AnchorResolver(this.rects, this.mapper);

    private lifecycle: OverlayLifecycle = 'detached';
    private lifecycleSubscribers = new Set<(lifecycle: OverlayLifecycle) => void>();
    private disposers: Array<() => void> = [];

    getLifecycle(): OverlayLifecycle {
        return this.lifecycle;
    }

    onLifecycle(subscriber: (lifecycle: OverlayLifecycle) => void): () => void {
        this.lifecycleSubscribers.add(subscriber);
        return () => this.lifecycleSubscribers.delete(subscriber);
    }

    /** Attach to a bridge. Idempotent; call detach() before re-attaching. */
    attach(bridge: EditorBridge): void {
        if (this.lifecycle !== 'detached') throw new Error('OverlayRuntime is already attached.');

        this.disposers.push(
            bridge.on('message', (message) => {
                switch (message.type) {
                    case 'rects.changed': {
                        const payload = message.payload as RectsChangedPayload;
                        this.rects.ingestSnapshot(payload, bridge.getRevisions().applied);
                        this.viewport.setChildViewport(payload.viewportWidth, payload.viewportHeight);
                        return;
                    }
                    case 'scroll.changed':
                        this.rects.applyScroll(message.payload as ScrollChangedPayload);
                        return;
                    case 'viewport.changed': {
                        const payload = message.payload as ViewportChangedPayload;
                        this.rects.applyViewport(payload);
                        this.viewport.setChildViewport(payload.viewportWidth, payload.viewportHeight);
                        return;
                    }
                    case 'selection.changed':
                        // Proposal only — host decides; store mirrors nothing here.
                        return;
                    case 'hover.changed':
                        this.selection.hover((message.payload as HoverChangedPayload).anchorId);
                        return;
                    default:
                        return;
                }
            }),
            bridge.on('revisionApplied', (revision) => this.rects.markStale(revision)),
            bridge.on('stateChanged', (state) => {
                if (state === 'connected') this.setLifecycle('active');
                else if (state === 'failed' || state === 'reconnecting') this.setLifecycle('degraded');
            }),
            bridge.on('failed', () => this.setLifecycle('degraded'))
        );

        if (bridge.getState() === 'connected') this.setLifecycle('active');
    }

    detach(): void {
        for (const dispose of this.disposers) dispose();
        this.disposers = [];
        this.selection.clear();
        this.setLifecycle('detached');
    }

    /** Host-side selection decision (the single owner remains the host). */
    applyHostSelection(anchorId: string | null): void {
        this.selection.select(anchorId);
    }

    /** Convenience: forward a child selection PROPOSAL to a host decision fn. */
    static isSelectionProposal(messageType: string): boolean {
        return messageType === 'selection.changed';
    }

    private setLifecycle(lifecycle: OverlayLifecycle): void {
        if (this.lifecycle === lifecycle) return;
        this.lifecycle = lifecycle;
        for (const subscriber of [...this.lifecycleSubscribers]) subscriber(lifecycle);
    }
}

export type { SelectionChangedPayload };
