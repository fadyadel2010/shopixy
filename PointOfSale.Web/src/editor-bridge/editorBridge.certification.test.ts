/**
 * ARCH-1.2 Certification — Editor Bridge (parent) + Overlay Runtime.
 * Deterministic: fake transport + manual timers. No DOM required.
 */
import { describe, expect, it } from 'vitest';
import {
    BRIDGE_MESSAGE_MARKER,
    BRIDGE_PROTOCOL_VERSION,
    asBridgeMessage,
    intersectCapabilities,
    isProtocolCompatible,
    type BridgeEnvelope,
} from './protocol';
import { EditorBridge, type MessageTransport } from './EditorBridge';
import { OverlayRuntime } from './OverlayRuntime';

/* ---------- harness ---------- */

class ManualTimers {
    private timers = new Map<number, { fn: () => void; at: number }>();
    private nextId = 1;
    nowMs = 0;

    set = (fn: () => void, ms: number): unknown => {
        const id = this.nextId++;
        this.timers.set(id, { fn, at: this.nowMs + ms });
        return id;
    };

    clear = (handle: unknown): void => {
        this.timers.delete(handle as number);
    };

    advance(ms: number): void {
        this.nowMs += ms;
        for (const [id, timer] of [...this.timers]) {
            if (timer.at <= this.nowMs) {
                this.timers.delete(id);
                timer.fn();
            }
        }
    }
}

class FakeChild {
    sentByParent: BridgeEnvelope<string, unknown>[] = [];
    private handler: ((message: unknown, origin: string) => void) | null = null;
    private seq = 1000;
    epoch = 1;
    origin = 'https://storefront.example';

    transport: MessageTransport = {
        send: (message) => this.sentByParent.push(message as BridgeEnvelope<string, unknown>),
        subscribe: (handler) => {
            this.handler = handler;
            return () => {
                this.handler = null;
            };
        },
    };

    emit(type: string, payload: unknown, opts?: { ackOf?: number; epoch?: number; seq?: number; origin?: string }): void {
        this.handler?.(
            {
                marker: BRIDGE_MESSAGE_MARKER,
                v: BRIDGE_PROTOCOL_VERSION,
                type,
                seq: opts?.seq ?? ++this.seq,
                epoch: opts?.epoch ?? this.epoch,
                sentAt: 0,
                payload,
                ...(opts?.ackOf !== undefined ? { ackOf: opts.ackOf } : {}),
            },
            opts?.origin ?? this.origin
        );
    }

    lastParentMessage(type: string): BridgeEnvelope<string, unknown> | undefined {
        return [...this.sentByParent].reverse().find((m) => m.type === type);
    }

    replyReady(bridgeEpochFromHello?: number): void {
        const hello = this.lastParentMessage('hello');
        this.epoch = bridgeEpochFromHello ?? hello?.epoch ?? 1;
        this.emit(
            'ready',
            {
                protocolVersion: BRIDGE_PROTOCOL_VERSION,
                grantedCapabilities: ['rects', 'selection', 'hover'],
                runtime: { tenantId: 'tenant-1', revision: 0 },
            },
            { ackOf: hello?.seq }
        );
    }
}

function createBridge(overrides?: Partial<ConstructorParameters<typeof EditorBridge>[0]>) {
    const timers = new ManualTimers();
    const child = new FakeChild();
    const bridge = new EditorBridge({
        transport: child.transport,
        expectedOrigin: child.origin,
        hostKind: 'certification-test',
        requestedCapabilities: ['rects', 'selection', 'hover', 'scroll'],
        expectedTenantId: 'tenant-1',
        now: () => timers.nowMs,
        setTimer: timers.set,
        clearTimer: timers.clear,
        ...overrides,
    });
    return { bridge, child, timers };
}

/* ---------- protocol unit certification ---------- */

describe('Protocol v1', () => {
    it('accepts only correctly-marked envelopes', () => {
        expect(asBridgeMessage({ foo: 1 })).toBeNull();
        expect(asBridgeMessage(null)).toBeNull();
        expect(asBridgeMessage('x')).toBeNull();
        expect(
            asBridgeMessage({
                marker: BRIDGE_MESSAGE_MARKER,
                v: '1.0',
                type: 'ack',
                seq: 1,
                epoch: 1,
                sentAt: 0,
                payload: {},
            })
        ).not.toBeNull();
    });

    it('major must match, minor may differ', () => {
        expect(isProtocolCompatible('1.0', '1.9')).toBe(true);
        expect(isProtocolCompatible('1.0', '2.0')).toBe(false);
        expect(isProtocolCompatible('1.0', 'nope')).toBe(false);
    });

    it('capability negotiation is an intersection', () => {
        expect(intersectCapabilities(['rects', 'hover', 'x-future'], ['rects', 'selection'])).toEqual(['rects']);
    });
});

/* ---------- handshake / lifecycle ---------- */

describe('EditorBridge handshake', () => {
    it('connects after ready and negotiates capabilities', () => {
        const { bridge, child } = createBridge();
        bridge.connect();
        expect(bridge.getState()).toBe('connecting');
        child.replyReady();
        expect(bridge.getState()).toBe('connected');
        expect(bridge.getGrantedCapabilities()).toEqual(['rects', 'selection', 'hover']);
    });

    it('fails on protocol major mismatch', () => {
        const { bridge, child } = createBridge();
        bridge.connect();
        const hello = child.lastParentMessage('hello');
        child.epoch = hello?.epoch ?? 1;
        child.emit('ready', {
            protocolVersion: '2.0',
            grantedCapabilities: [],
            runtime: { revision: 0 },
        });
        expect(bridge.getState()).toBe('failed');
    });

    it('fails on tenant mismatch', () => {
        const { bridge, child } = createBridge();
        bridge.connect();
        const hello = child.lastParentMessage('hello');
        child.epoch = hello?.epoch ?? 1;
        child.emit('ready', {
            protocolVersion: BRIDGE_PROTOCOL_VERSION,
            grantedCapabilities: [],
            runtime: { tenantId: 'other-tenant', revision: 0 },
        });
        expect(bridge.getState()).toBe('failed');
    });

    it('fails closed when a tenant is expected but the runtime reports none (ARCH-1.4R review closure)', () => {
        const { bridge, child } = createBridge();
        bridge.connect();
        const hello = child.lastParentMessage('hello');
        child.epoch = hello?.epoch ?? 1;
        child.emit('ready', {
            protocolVersion: BRIDGE_PROTOCOL_VERSION,
            grantedCapabilities: [],
            runtime: { revision: 0 }, // no tenantId
        });
        expect(bridge.getState()).toBe('failed');
    });

    it('surfaces handshake.error and fails closed', () => {
        const { bridge, child } = createBridge();
        let received: string | null = null;
        bridge.on('handshakeError', (payload) => {
            received = payload.code;
        });
        bridge.connect();
        child.emit('handshake.error', {
            code: 'TENANT_MISMATCH',
            message: 'no',
            protocolVersion: BRIDGE_PROTOCOL_VERSION,
        });
        expect(received).toBe('TENANT_MISMATCH');
        expect(bridge.getState()).toBe('failed');
    });

    it('drops messages from unexpected origins', () => {
        const { bridge, child } = createBridge();
        bridge.connect();
        child.emit('ready', {
            protocolVersion: BRIDGE_PROTOCOL_VERSION,
            grantedCapabilities: [],
            runtime: { revision: 0 },
        }, { origin: 'https://evil.example' });
        expect(bridge.getState()).toBe('connecting');
        expect(bridge.diagnostics.snapshot().droppedForeign).toBe(1);
    });

    it('reconnects with backoff on handshake timeout, then fails after exhaustion', () => {
        const { bridge, timers } = createBridge({
            timing: { handshakeTimeoutMs: 100, reconnectBackoffMs: [50, 50] },
        });
        bridge.connect();
        timers.advance(100); // timeout 1 -> reconnecting
        expect(bridge.getState()).toBe('reconnecting');
        timers.advance(50); // retry 1 (connecting again)
        timers.advance(100); // timeout 2
        timers.advance(50); // retry 2
        timers.advance(100); // timeout 3 -> exhausted
        expect(bridge.getState()).toBe('failed');
        expect(bridge.diagnostics.snapshot().reconnects).toBe(2);
    });
});

/* ---------- revision synchronization & guarantees ---------- */

describe('EditorBridge revisions and delivery guarantees', () => {
    function connected() {
        const ctx = createBridge();
        ctx.bridge.connect();
        ctx.child.replyReady();
        return ctx;
    }

    it('config.apply carries monotonic revisions and tracks in-sync state', () => {
        const { bridge, child } = connected();
        const r1 = bridge.applyConfig({ any: 'doc' });
        const r2 = bridge.applyConfig({ any: 'doc2' });
        expect([r1, r2]).toEqual([1, 2]);
        expect(bridge.getRevisions()).toMatchObject({ latest: 2, applied: 0, inSync: false });

        child.emit('config.applied', { revision: 2 });
        expect(bridge.getRevisions()).toMatchObject({ latest: 2, applied: 2, inSync: true });

        // stale applied confirmation is dropped
        child.emit('config.applied', { revision: 1 });
        expect(bridge.getRevisions().applied).toBe(2);
        expect(bridge.diagnostics.snapshot().droppedStaleRevision).toBe(1);
    });

    it('retries unacked commands then fails (at-least-once + bounded)', () => {
        const { bridge, child, timers } = createBridge({
            timing: { ackTimeoutMs: 100, maxCommandRetries: 2 },
        });
        bridge.connect();
        child.replyReady();
        const sentBefore = child.sentByParent.length;
        bridge.applyConfig({ doc: 1 });
        timers.advance(100); // retry 1
        timers.advance(100); // retry 2
        timers.advance(100); // exhausted -> failed
        const applies = child.sentByParent.slice(sentBefore).filter((m) => m.type === 'config.apply');
        expect(applies.length).toBe(3); // original + 2 retries, identical envelopes
        expect(new Set(applies.map((m) => m.seq)).size).toBe(1); // same seq = idempotency key
        expect(bridge.getState()).toBe('failed');
    });

    it('acks resolve pending commands and record latency', () => {
        const { bridge, child, timers } = connected();
        bridge.applyConfig({ doc: 1 });
        const apply = child.lastParentMessage('config.apply');
        timers.advance(30);
        child.emit('ack', {}, { ackOf: apply?.seq });
        timers.advance(5_000); // no retry should fire
        expect(bridge.getState()).toBe('connected');
        expect(bridge.diagnostics.snapshot().latency.samples).toBeGreaterThan(0);
    });

    it('ignores duplicate child messages (idempotent receive)', () => {
        const { bridge, child } = connected();
        let selections = 0;
        bridge.on('message', (m) => {
            if (m.type === 'selection.changed') selections += 1;
        });
        child.emit('selection.changed', { anchorId: 'a' }, { seq: 7777 });
        child.emit('selection.changed', { anchorId: 'a' }, { seq: 7777 }); // duplicate
        expect(selections).toBe(1);
        expect(bridge.diagnostics.snapshot().duplicatesIgnored).toBe(1);
    });

    it('drops stale-epoch messages after reconnect', () => {
        const { bridge, child } = connected();
        let events = 0;
        bridge.on('message', () => {
            events += 1;
        });
        child.emit('hover.changed', { anchorId: 'x' }, { epoch: child.epoch - 1 });
        expect(events).toBe(0);
        expect(bridge.diagnostics.snapshot().droppedStaleEpoch).toBe(1);
    });

    it('declares peer lost when heartbeats stop, and recovers via reconnect', () => {
        const { bridge, child, timers } = createBridge({
            timing: { heartbeatIntervalMs: 100, heartbeatTimeoutMs: 300, reconnectBackoffMs: [10] },
        });
        bridge.connect();
        child.replyReady();
        let lost = false;
        bridge.on('peerLost', () => {
            lost = true;
        });
        timers.advance(300); // no child heartbeat/ack arrives
        expect(lost).toBe(true);
        expect(bridge.getState()).toBe('reconnecting');
        timers.advance(10); // backoff elapses -> new hello
        child.replyReady();
        expect(bridge.getState()).toBe('connected');
    });

    it('unknown message types are counted, never fatal (forward compatibility)', () => {
        const { bridge, child } = connected();
        child.emit('totally.unknown', { data: 1 });
        child.emit('x.plugin.thing', { data: 2 });
        expect(bridge.getState()).toBe('connected');
        expect(bridge.diagnostics.snapshot().unknownTypesIgnored).toBe(1); // x.* is not counted
    });
});

/* ---------- overlay runtime ---------- */

describe('OverlayRuntime', () => {
    function activeOverlay() {
        const ctx = createBridge();
        const overlay = new OverlayRuntime();
        overlay.attach(ctx.bridge);
        ctx.bridge.connect();
        ctx.child.replyReady();
        return { ...ctx, overlay };
    }

    const rect = (anchorId: string, y: number, order: number) => ({
        anchorId,
        x: 10,
        y,
        width: 300,
        height: 100,
        inViewport: true,
        order,
    });

    it('activates on connect and degrades on failure (fail closed)', () => {
        const { overlay, child } = activeOverlay();
        expect(overlay.getLifecycle()).toBe('active');
        child.emit('ready', { protocolVersion: '9.0', grantedCapabilities: [], runtime: { revision: 0 } });
        expect(overlay.getLifecycle()).toBe('degraded');
    });

    it('ingests rect snapshots with monotonic revisions and stale marking', () => {
        const { overlay, child, bridge } = activeOverlay();
        bridge.applyConfig({ doc: 1 }); // latest = 1
        child.emit('rects.changed', {
            revision: 0,
            scrollX: 0,
            scrollY: 0,
            viewportWidth: 1200,
            viewportHeight: 800,
            rects: [rect('a', 0, 0), rect('b', 100, 1)],
        });
        expect(overlay.rects.getState().order).toEqual(['a', 'b']);

        child.emit('config.applied', { revision: 1 });
        expect(overlay.rects.getState().stale).toBe(true); // rects describe rev 0

        child.emit('rects.changed', {
            revision: 1,
            scrollX: 0,
            scrollY: 0,
            viewportWidth: 1200,
            viewportHeight: 800,
            rects: [rect('b', 0, 0), rect('a', 100, 1)],
        });
        expect(overlay.rects.getState().stale).toBe(false);
        expect(overlay.rects.getState().order).toEqual(['b', 'a']);
    });

    it('applies scroll deltas to cached rects between snapshots', () => {
        const { overlay, child } = activeOverlay();
        child.emit('rects.changed', {
            revision: 0, scrollX: 0, scrollY: 0, viewportWidth: 1200, viewportHeight: 800,
            rects: [rect('a', 500, 0)],
        });
        child.emit('scroll.changed', { scrollX: 0, scrollY: 200 });
        expect(overlay.rects.getRect('a')?.y).toBe(300);
    });

    it('maps child coordinates through frame offset and scale', () => {
        const { overlay } = activeOverlay();
        overlay.viewport.setFrameOffset({ x: 50, y: 20 });
        overlay.viewport.setScale(0.5);
        expect(overlay.mapper.toOverlayRect({ x: 100, y: 200, width: 300, height: 100 })).toEqual({
            x: 100, y: 120, width: 150, height: 50,
        });
        expect(overlay.mapper.toChildPoint({ x: 100, y: 120 })).toEqual({ x: 100, y: 200 });
    });

    it('resolves toolbar anchors with flip-below when clipped', () => {
        const { overlay, child } = activeOverlay();
        child.emit('rects.changed', {
            revision: 0, scrollX: 0, scrollY: 0, viewportWidth: 1200, viewportHeight: 800,
            rects: [rect('top', 10, 0), rect('mid', 400, 1)],
        });
        expect(overlay.anchors.resolveToolbar('top', 40)?.placement).toBe('below');
        expect(overlay.anchors.resolveToolbar('mid', 40)?.placement).toBe('above');
        expect(overlay.anchors.resolveToolbar('missing', 40)).toBeNull();
    });

    it('resolves drop indicators by anchor midpoints and hit-tests points', () => {
        const { overlay, child } = activeOverlay();
        child.emit('rects.changed', {
            revision: 0, scrollX: 0, scrollY: 0, viewportWidth: 1200, viewportHeight: 800,
            rects: [rect('a', 0, 0), rect('b', 100, 1), rect('c', 200, 2)],
        });
        expect(overlay.anchors.resolveDropIndicator(10)?.index).toBe(0);
        expect(overlay.anchors.resolveDropIndicator(160)?.index).toBe(2);
        expect(overlay.anchors.resolveDropIndicator(999)?.index).toBe(3);
        expect(overlay.anchors.hitTest({ x: 20, y: 150 })).toBe('b');
        expect(overlay.anchors.hitTest({ x: 999, y: 999 })).toBeNull();
    });

    it('mirrors hover from child; selection stays host-owned', () => {
        const { overlay, child } = activeOverlay();
        child.emit('hover.changed', { anchorId: 'a' });
        expect(overlay.selection.getState().hoveredAnchorId).toBe('a');
        child.emit('selection.changed', { anchorId: 'a' }); // proposal only
        expect(overlay.selection.getState().selectedAnchorId).toBeNull();
        overlay.applyHostSelection('a'); // host decision
        expect(overlay.selection.getState().selectedAnchorId).toBe('a');
    });

    it('detaches cleanly (no leaked subscriptions)', () => {
        const { overlay, child } = activeOverlay();
        overlay.detach();
        expect(overlay.getLifecycle()).toBe('detached');
        child.emit('hover.changed', { anchorId: 'zzz' });
        expect(overlay.selection.getState().hoveredAnchorId).toBeNull();
    });
});
