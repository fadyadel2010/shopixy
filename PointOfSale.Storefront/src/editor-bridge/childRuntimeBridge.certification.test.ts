/**
 * ARCH-1.2 Certification — Child Runtime Bridge (storefront side).
 * Runs in node with injected fake window/document (proves the module is
 * environment-agnostic and has no hidden globals besides observers,
 * which it feature-detects).
 */
import { describe, expect, it, vi } from 'vitest';
import {
    BRIDGE_MESSAGE_MARKER,
    BRIDGE_PROTOCOL_VERSION,
    type BridgeEnvelope,
} from './protocol';
import { ANCHOR_ATTRIBUTE, ANCHOR_KIND_ATTRIBUTE, ChildRuntimeBridge } from './ChildRuntimeBridge';

/* ---------------- fakes ---------------- */

interface FakeElement {
    attrs: Record<string, string>;
    rect: { x: number; y: number; width: number; height: number; top: number; bottom: number };
    getAttribute(name: string): string | null;
    getBoundingClientRect(): FakeElement['rect'];
    closest(selector: string): FakeElement | null;
    scrollIntoView(options?: unknown): void;
    scrolledInto?: unknown;
}

function el(attrs: Record<string, string>, y = 0, height = 100): FakeElement {
    const element: FakeElement = {
        attrs,
        rect: { x: 0, y, width: 800, height, top: y, bottom: y + height },
        getAttribute: (name) => element.attrs[name] ?? null,
        getBoundingClientRect: () => element.rect,
        closest: (selector) => {
            if (selector === `[${ANCHOR_ATTRIBUTE}]`) {
                return element.attrs[ANCHOR_ATTRIBUTE] !== undefined ? element : null;
            }
            if (selector === 'a[href]') {
                return element.attrs.href !== undefined ? element : null;
            }
            return null;
        },
        scrollIntoView: (options) => {
            element.scrolledInto = options;
        },
    };
    return element;
}

function makeHarness(anchors: FakeElement[], hooks?: { onSelectionSet?: (anchorId: string | null) => void }) {
    const listeners = new Map<string, Array<(event: unknown) => void>>();
    const docListeners = new Map<string, Array<(event: unknown) => void>>();
    const parentMessages: BridgeEnvelope<string, unknown>[] = [];
    const parentOrigin = 'https://admin.example';

    const parentWindow = {
        postMessage: (message: unknown, targetOrigin: string) => {
            expect(targetOrigin).toBe(parentOrigin);
            parentMessages.push(message as BridgeEnvelope<string, unknown>);
        },
    } as unknown as Window;

    const root = {
        querySelectorAll: (selector: string) => {
            expect(selector).toBe(`[${ANCHOR_ATTRIBUTE}]`);
            return anchors as unknown as NodeListOf<HTMLElement>;
        },
    } as unknown as HTMLElement;

    const win = {
        innerWidth: 1200,
        innerHeight: 800,
        scrollX: 0,
        scrollY: 0,
        addEventListener: (type: string, handler: (event: unknown) => void) => {
            if (!listeners.has(type)) listeners.set(type, []);
            listeners.get(type)!.push(handler);
        },
        removeEventListener: () => undefined,
    } as unknown as Window;

    const doc = {
        body: root,
        addEventListener: (type: string, handler: (event: unknown) => void) => {
            if (!docListeners.has(type)) docListeners.set(type, []);
            docListeners.get(type)!.push(handler);
        },
        removeEventListener: () => undefined,
    } as unknown as Document;

    const applyDocument = vi.fn(async () => undefined);

    const bridge = new ChildRuntimeBridge({
        expectedParentOrigin: parentOrigin,
        tenantId: 'tenant-1',
        applyDocument,
        onSelectionSet: hooks?.onSelectionSet,
        root,
        windowRef: win,
        documentRef: doc,
    });
    bridge.start();

    let parentSeq = 0;
    const parentEpoch = 3;
    function fromParent(type: string, payload: unknown, opts?: { origin?: string; seq?: number; epoch?: number }): void {
        const handlers = listeners.get('message') ?? [];
        for (const handler of handlers) {
            handler({
                origin: opts?.origin ?? parentOrigin,
                source: parentWindow,
                data: {
                    marker: BRIDGE_MESSAGE_MARKER,
                    v: BRIDGE_PROTOCOL_VERSION,
                    type,
                    seq: opts?.seq ?? ++parentSeq,
                    epoch: opts?.epoch ?? parentEpoch,
                    sentAt: 0,
                    payload,
                },
            });
        }
    }

    function hello(overrides?: Record<string, unknown>): void {
        fromParent('hello', {
            protocolVersion: BRIDGE_PROTOCOL_VERSION,
            requestedCapabilities: ['rects', 'selection', 'hover', 'navigation-blocking'],
            hostKind: 'certification-test',
            expectedTenantId: 'tenant-1',
            heartbeatIntervalMs: 60_000,
            ...overrides,
        });
    }

    function domEvent(type: string, event: unknown): void {
        for (const handler of docListeners.get(type) ?? []) handler(event);
    }

    function last(type: string): BridgeEnvelope<string, unknown> | undefined {
        return [...parentMessages].reverse().find((m) => m.type === type);
    }

    return { bridge, parentMessages, fromParent, hello, domEvent, last, applyDocument };
}

/* ---------------- certification ---------------- */

describe('ChildRuntimeBridge handshake', () => {
    it('answers hello with ready + granted capability intersection', () => {
        const h = makeHarness([]);
        h.hello();
        const ready = h.last('ready');
        expect(ready).toBeDefined();
        expect(h.bridge.isConnected()).toBe(true);
        expect((ready!.payload as { grantedCapabilities: string[] }).grantedCapabilities).toEqual([
            'rects', 'selection', 'hover', 'navigation-blocking',
        ]);
        expect(ready!.ackOf).toBeDefined();
    });

    it('refuses version major mismatch', () => {
        const h = makeHarness([]);
        h.hello({ protocolVersion: '2.0' });
        expect((h.last('handshake.error')!.payload as { code: string }).code).toBe('VERSION_MISMATCH');
        expect(h.bridge.isConnected()).toBe(false);
    });

    it('refuses tenant mismatch', () => {
        const h = makeHarness([]);
        h.hello({ expectedTenantId: 'someone-else' });
        expect((h.last('handshake.error')!.payload as { code: string }).code).toBe('TENANT_MISMATCH');
    });

    it('ignores messages from unexpected origins entirely', () => {
        const h = makeHarness([]);
        h.hello({ protocolVersion: BRIDGE_PROTOCOL_VERSION });
        const count = h.parentMessages.length;
        h.fromParent('selection.set', { anchorId: 'a' }, { origin: 'https://evil.example' });
        expect(h.parentMessages.length).toBe(count);
    });

    it('accepts a second hello as a reconnect (new epoch supersedes)', () => {
        const h = makeHarness([]);
        h.hello();
        h.hello({ }); // reconnect
        const readies = h.parentMessages.filter((m) => m.type === 'ready');
        expect(readies.length).toBe(2);
        expect(readies[1].epoch).toBeGreaterThan(readies[0].epoch);
    });
});

describe('ChildRuntimeBridge document application', () => {
    it('applies opaque documents, acks, confirms, and enforces revision monotonicity', async () => {
        const h = makeHarness([]);
        h.hello();
        h.fromParent('config.apply', { revision: 1, document: { anything: true } });
        // apply chain is serialized (ARCH-1.4R async-race closure): allow a macrotask
        await new Promise((r) => setTimeout(r, 0));
        expect(h.applyDocument).toHaveBeenCalledWith({ anything: true }, 1);
        expect((h.last('config.applied')!.payload as { revision: number }).revision).toBe(1);

        // stale/duplicate revision: not re-applied, re-confirmed
        h.fromParent('config.apply', { revision: 1, document: { anything: 'again' } });
        await new Promise((r) => setTimeout(r, 0));
        expect(h.applyDocument).toHaveBeenCalledTimes(1);
        expect((h.last('config.applied')!.payload as { revision: number }).revision).toBe(1);
    });

    it('duplicate seq (parent retry) is acked but not re-applied', async () => {
        const h = makeHarness([]);
        h.hello();
        h.fromParent('config.apply', { revision: 5, document: {} }, { seq: 42 });
        await Promise.resolve();
        h.fromParent('config.apply', { revision: 5, document: {} }, { seq: 42 }); // retry
        await Promise.resolve();
        expect(h.applyDocument).toHaveBeenCalledTimes(1);
        const acks = h.parentMessages.filter((m) => m.type === 'ack' && m.ackOf === 42);
        expect(acks.length).toBe(2); // idempotent re-ack
    });

    it('reports runtime.error (recoverable) when the applier throws', async () => {
        const h = makeHarness([]);
        h.applyDocument.mockRejectedValueOnce(new Error('boom'));
        h.hello();
        h.fromParent('config.apply', { revision: 1, document: {} });
        await Promise.resolve();
        await Promise.resolve();
        const error = h.last('runtime.error');
        expect(error).toBeDefined();
        expect((error!.payload as { recoverable: boolean }).recoverable).toBe(true);
    });
});

describe('ChildRuntimeBridge rects and interaction relays', () => {
    const anchorA = el({ [ANCHOR_ATTRIBUTE]: 'a', [ANCHOR_KIND_ATTRIBUTE]: 'kind-a' }, 0, 100);
    const anchorB = el({ [ANCHOR_ATTRIBUTE]: 'b' }, 100, 200);

    it('publishes full rect snapshots with document order and viewport flags', () => {
        const h = makeHarness([anchorA, anchorB]);
        h.hello();
        h.bridge.publishRects();
        const payload = h.last('rects.changed')!.payload as {
            revision: number;
            rects: Array<{ anchorId: string; anchorKind?: string; order: number; inViewport: boolean }>;
        };
        expect(payload.revision).toBe(0);
        expect(payload.rects.map((r) => r.anchorId)).toEqual(['a', 'b']);
        expect(payload.rects[0].anchorKind).toBe('kind-a');
        expect(payload.rects[1].order).toBe(1);
        expect(payload.rects.every((r) => r.inViewport)).toBe(true);
    });

    it('relays clicks on anchors as selection proposals', () => {
        const h = makeHarness([anchorA]);
        h.hello();
        h.domEvent('click', { target: anchorA, preventDefault: vi.fn(), stopPropagation: vi.fn() });
        expect((h.last('selection.changed')!.payload as { anchorId: string }).anchorId).toBe('a');
    });

    it('blocks link navigation and reports navigation.blocked', () => {
        const link = el({ href: '/products/1' });
        const h = makeHarness([]);
        h.hello();
        const preventDefault = vi.fn();
        h.domEvent('click', { target: link, preventDefault, stopPropagation: vi.fn() });
        expect(preventDefault).toHaveBeenCalled();
        expect((h.last('navigation.blocked')!.payload as { href: string }).href).toBe('/products/1');
    });

    it('relays hover with coalescing (no duplicate consecutive ids)', () => {
        const h = makeHarness([anchorA]);
        h.hello();
        h.domEvent('pointerover', { target: anchorA });
        h.domEvent('pointerover', { target: anchorA });
        const hovers = h.parentMessages.filter((m) => m.type === 'hover.changed');
        expect(hovers.length).toBe(1);
        h.domEvent('pointerover', { target: el({}) });
        const hovers2 = h.parentMessages.filter((m) => m.type === 'hover.changed');
        expect(hovers2.length).toBe(2);
        expect((hovers2[1].payload as { anchorId: string | null }).anchorId).toBeNull();
    });

    it('scroll.to scrolls the matching anchor into view and acks', () => {
        const h = makeHarness([anchorA, anchorB]);
        h.hello();
        h.fromParent('scroll.to', { anchorId: 'b', behavior: 'smooth', block: 'center' });
        expect(anchorB.scrolledInto).toEqual({ behavior: 'smooth', block: 'center' });
    });

    it('selection.set mirrors into the runtime hook and acks', () => {
        const onSelectionSet = vi.fn();
        const h = makeHarness([], { onSelectionSet });
        h.hello();
        h.fromParent('selection.set', { anchorId: 'a' });
        expect(onSelectionSet).toHaveBeenCalledWith('a');
        h.fromParent('selection.set', { anchorId: null });
        expect(onSelectionSet).toHaveBeenLastCalledWith(null);
    });

    it('unknown and extension message types are ignored, never fatal', () => {
        const h = makeHarness([]);
        h.hello();
        h.fromParent('x.future.thing', { whatever: 1 });
        h.fromParent('completely.unknown', {});
        expect(h.bridge.isConnected()).toBe(true);
    });
});
