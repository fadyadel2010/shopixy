/**
 * ARCH-1.4R Review Closure — regression tests for the independent forensic
 * review findings (P0 listener/timer leak, P1 tenant fail-open, P1 async
 * apply revision race). Node-run with injected fakes, same pattern as
 * childRuntimeBridge.certification.test.ts.
 */
import { describe, expect, it, vi } from 'vitest';
import {
    BRIDGE_MESSAGE_MARKER,
    BRIDGE_PROTOCOL_VERSION,
    type BridgeEnvelope,
} from './protocol';
import { ANCHOR_ATTRIBUTE, ChildRuntimeBridge } from './ChildRuntimeBridge';

function makeCountingHarness(options?: { tenantId?: string | undefined; applyDocument?: (doc: unknown, revision: number) => Promise<void> }) {
    const winListeners = new Map<string, Set<(event: unknown) => void>>();
    const docListeners = new Map<string, Set<(event: unknown) => void>>();
    const parentMessages: BridgeEnvelope<string, unknown>[] = [];
    const parentOrigin = 'https://admin.example';

    const parentWindow = {
        postMessage: (message: unknown) => {
            parentMessages.push(message as BridgeEnvelope<string, unknown>);
        },
    } as unknown as Window;

    const root = {
        querySelectorAll: () => [] as unknown as NodeListOf<HTMLElement>,
    } as unknown as HTMLElement;

    const track = (map: Map<string, Set<(event: unknown) => void>>) => ({
        add(type: string, handler: (event: unknown) => void) {
            if (!map.has(type)) map.set(type, new Set());
            map.get(type)!.add(handler);
        },
        remove(type: string, handler: (event: unknown) => void) {
            map.get(type)?.delete(handler);
        },
    });

    const winTrack = track(winListeners);
    const docTrack = track(docListeners);

    const win = {
        innerWidth: 1200,
        innerHeight: 800,
        scrollX: 0,
        scrollY: 0,
        addEventListener: (type: string, handler: (event: unknown) => void) => winTrack.add(type, handler),
        removeEventListener: (type: string, handler: (event: unknown) => void) => winTrack.remove(type, handler),
    } as unknown as Window;

    const doc = {
        body: root,
        addEventListener: (type: string, handler: (event: unknown) => void) => docTrack.add(type, handler),
        removeEventListener: (type: string, handler: (event: unknown) => void) => docTrack.remove(type, handler),
    } as unknown as Document;

    const applyDocument = options?.applyDocument
        ? vi.fn(options.applyDocument)
        : vi.fn(async () => undefined);

    const bridge = new ChildRuntimeBridge({
        expectedParentOrigin: parentOrigin,
        tenantId: options && 'tenantId' in options ? options.tenantId : 'tenant-1',
        applyDocument,
        root,
        windowRef: win,
        documentRef: doc,
    });
    bridge.start();

    let parentSeq = 0;
    function fromParent(type: string, payload: unknown, opts?: { epoch?: number }): void {
        for (const handler of winListeners.get('message') ?? []) {
            handler({
                origin: parentOrigin,
                source: parentWindow,
                data: {
                    marker: BRIDGE_MESSAGE_MARKER,
                    v: BRIDGE_PROTOCOL_VERSION,
                    type,
                    seq: ++parentSeq,
                    epoch: opts?.epoch ?? 3,
                    sentAt: 0,
                    payload,
                },
            });
        }
    }

    function hello(overrides?: Record<string, unknown>, opts?: { epoch?: number }): void {
        fromParent('hello', {
            protocolVersion: BRIDGE_PROTOCOL_VERSION,
            requestedCapabilities: ['rects', 'selection', 'hover'],
            hostKind: 'review-closure-test',
            expectedTenantId: 'tenant-1',
            heartbeatIntervalMs: 60_000,
            ...overrides,
        }, opts);
    }

    const count = (map: Map<string, Set<unknown>>, type: string) => map.get(type)?.size ?? 0;

    return {
        bridge,
        parentMessages,
        fromParent,
        hello,
        applyDocument,
        winCount: (type: string) => count(winListeners, type),
        docCount: (type: string) => count(docListeners, type),
        last: (type: string) => [...parentMessages].reverse().find((m) => m.type === type),
    };
}

describe('P0 closure: session listener and timer hygiene', () => {
    it('repeated hellos never stack scroll/resize/click/pointer listeners', () => {
        const h = makeCountingHarness();
        h.hello();
        h.hello({}, { epoch: 4 });
        h.hello({}, { epoch: 5 });
        expect(h.winCount('scroll')).toBe(1);
        expect(h.winCount('resize')).toBe(1);
        expect(h.docCount('click')).toBe(1);
        expect(h.docCount('pointerover')).toBe(1);
    });

    it('shutdown removes all per-session listeners', () => {
        const h = makeCountingHarness();
        h.hello();
        h.fromParent('shutdown', { reason: 'host-closed' });
        expect(h.winCount('scroll')).toBe(0);
        expect(h.winCount('resize')).toBe(0);
        expect(h.docCount('click')).toBe(0);
        expect(h.docCount('pointerover')).toBe(0);
        // process-lifetime message listener stays for a future hello
        expect(h.winCount('message')).toBe(1);
    });

    it('stop removes session AND process listeners', () => {
        const h = makeCountingHarness();
        h.hello();
        h.bridge.stop();
        expect(h.winCount('scroll')).toBe(0);
        expect(h.docCount('click')).toBe(0);
        expect(h.winCount('message')).toBe(0);
    });
});

describe('P1 closure: tenant fail-closed handshake', () => {
    it('rejects hello when host expects a tenant but the runtime has none', () => {
        const h = makeCountingHarness({ tenantId: undefined });
        h.hello({ expectedTenantId: 'tenant-1' });
        const error = h.last('handshake.error');
        expect(error).toBeDefined();
        expect((error!.payload as { code: string }).code).toBe('TENANT_MISMATCH');
        expect(h.last('ready')).toBeUndefined();
        expect(h.bridge.isConnected()).toBe(false);
    });

    it('still accepts matching tenants', () => {
        const h = makeCountingHarness();
        h.hello();
        expect(h.last('ready')).toBeDefined();
        expect(h.bridge.isConnected()).toBe(true);
    });
});

describe('P1 closure: async apply revision safety (latest-wins)', () => {
    it('an older async apply never overwrites a newer committed revision', async () => {
        const resolvers = new Map<number, () => void>();
        const h = makeCountingHarness({
            applyDocument: (_doc, revision) =>
                new Promise<void>((resolve) => resolvers.set(revision, resolve)),
        });
        h.hello();

        h.fromParent('config.apply', { revision: 1, document: { pages: {} } });
        h.fromParent('config.apply', { revision: 2, document: { pages: {} } });

        // Let the serialized chain drain. Because revision 2 arrived before
        // the chain dequeued revision 1, latest-wins must SKIP revision 1
        // entirely (never even call the applier for it).
        await new Promise((r) => setTimeout(r, 0));
        expect(resolvers.has(1)).toBe(false);
        expect(resolvers.has(2)).toBe(true);
        resolvers.get(2)!();
        await new Promise((r) => setTimeout(r, 0));

        const confirmations = h.parentMessages
            .filter((m) => m.type === 'config.applied')
            .map((m) => (m.payload as { revision: number }).revision);
        // Rev 1 completion must NOT commit after rev 2 was received:
        // only revision 2 may be the final applied confirmation.
        expect(confirmations[confirmations.length - 1]).toBe(2);
        expect(confirmations).not.toContain(1);
    });

    it('duplicate/stale revisions are dropped and re-confirmed with the applied revision', async () => {
        const h = makeCountingHarness();
        h.hello();
        h.fromParent('config.apply', { revision: 5, document: { pages: {} } });
        await new Promise((r) => setTimeout(r, 0));
        h.fromParent('config.apply', { revision: 5, document: { pages: {} } });
        await new Promise((r) => setTimeout(r, 0));
        const confirmations = h.parentMessages
            .filter((m) => m.type === 'config.applied')
            .map((m) => (m.payload as { revision: number }).revision);
        expect(confirmations).toEqual([5, 5]);
        expect(h.applyDocument).toHaveBeenCalledTimes(1);
    });
});

// keep unused import referenced (protocol constants used above); anchor attr kept
// for parity with the certification harness pattern.
void ANCHOR_ATTRIBUTE;
