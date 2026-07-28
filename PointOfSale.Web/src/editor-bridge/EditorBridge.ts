/**
 * Editor Bridge (parent side) — ARCH-1.2 Module 1.
 *
 * Framework-free session engine over a MessageTransport (postMessage in
 * production, fakes in tests). Owns: handshake, version + capability
 * negotiation, origin validation, heartbeat, reconnect, ack/retry,
 * revision monotonicity, message routing, timeout detection, diagnostics.
 *
 * Contains ZERO business logic and ZERO presentation knowledge.
 */

import {
    ACK_REQUIRED_TYPES,
    BRIDGE_DEFAULTS,
    BRIDGE_MESSAGE_MARKER,
    BRIDGE_PROTOCOL_VERSION,
    EPOCH_EXEMPT_TYPES,
    asBridgeMessage,
    intersectCapabilities,
    isExtensionType,
    isProtocolCompatible,
    type BridgeCapability,
    type BridgeEnvelope,
    type BridgeMessage,
    type ConfigAppliedPayload,
    type HandshakeErrorPayload,
    type ReadyPayload,
} from './protocol';
import { BridgeDiagnostics } from './diagnostics';

export type BridgeState =
    | 'idle'
    | 'connecting'
    | 'connected'
    | 'reconnecting'
    | 'failed'
    | 'closed';

/** Transport abstraction: postMessage in production, fake in tests. */
export interface MessageTransport {
    /** Send a structured-cloneable message to the peer. */
    send(message: unknown): void;
    /** Subscribe to inbound messages; returns unsubscribe. `origin` must be
     *  the transport-level origin of the sender ('' when not applicable). */
    subscribe(handler: (message: unknown, origin: string) => void): () => void;
}

export interface EditorBridgeOptions {
    transport: MessageTransport;
    /** Exact origin the child is expected to post from. REQUIRED. */
    expectedOrigin: string;
    hostKind: string;
    requestedCapabilities: BridgeCapability[];
    expectedTenantId?: string;
    timing?: Partial<typeof BRIDGE_DEFAULTS>;
    /** Injectable clock/timers for deterministic tests. */
    now?: () => number;
    setTimer?: (fn: () => void, ms: number) => unknown;
    clearTimer?: (handle: unknown) => void;
}

export interface BridgeEventMap {
    stateChanged: BridgeState;
    ready: ReadyPayload;
    handshakeError: HandshakeErrorPayload;
    message: BridgeMessage;
    revisionApplied: number;
    peerLost: void;
    failed: string;
}

type Listener<T> = (payload: T) => void;

interface PendingCommand {
    envelope: BridgeEnvelope<string, unknown>;
    retriesLeft: number;
    timer: unknown;
}

export class EditorBridge {
    private readonly options: EditorBridgeOptions;
    private readonly timing: typeof BRIDGE_DEFAULTS;
    private readonly now: () => number;
    private readonly setTimer: (fn: () => void, ms: number) => unknown;
    private readonly clearTimer: (handle: unknown) => void;
    readonly diagnostics = new BridgeDiagnostics(BRIDGE_PROTOCOL_VERSION);

    private state: BridgeState = 'idle';
    private epoch = 0;
    private seq = 0;
    private peerEpoch: number | null = null;
    private lastPeerSeq = 0;
    private processedPeerSeqs = new Set<number>();
    private grantedCapabilities: BridgeCapability[] = [];
    private latestRevision = 0;
    private appliedRevision = 0;
    private pending = new Map<number, PendingCommand>();
    private sentAtBySeq = new Map<number, number>();
    private heartbeatTimer: unknown = null;
    private heartbeatDeadline: unknown = null;
    private handshakeTimer: unknown = null;
    private reconnectAttempt = 0;
    private unsubscribe: (() => void) | null = null;
    private listeners = new Map<keyof BridgeEventMap, Set<Listener<never>>>();

    constructor(options: EditorBridgeOptions) {
        this.options = options;
        this.timing = { ...BRIDGE_DEFAULTS, ...options.timing };
        this.now = options.now ?? (() => Date.now());
        this.setTimer = options.setTimer ?? ((fn, ms) => setTimeout(fn, ms));
        this.clearTimer = options.clearTimer ?? ((handle) => clearTimeout(handle as number));
    }

    /* ---------------- public API ---------------- */

    getState(): BridgeState {
        return this.state;
    }

    getGrantedCapabilities(): readonly BridgeCapability[] {
        return this.grantedCapabilities;
    }

    getRevisions(): { latest: number; applied: number; inSync: boolean } {
        return {
            latest: this.latestRevision,
            applied: this.appliedRevision,
            inSync: this.latestRevision === this.appliedRevision,
        };
    }

    on<K extends keyof BridgeEventMap>(event: K, listener: Listener<BridgeEventMap[K]>): () => void {
        if (!this.listeners.has(event)) this.listeners.set(event, new Set());
        const bucket = this.listeners.get(event) as Set<Listener<BridgeEventMap[K]>>;
        bucket.add(listener);
        return () => bucket.delete(listener);
    }

    connect(): void {
        if (this.state === 'connected' || this.state === 'connecting') return;
        this.epoch += 1;
        this.resetSessionState();
        this.transition(this.reconnectAttempt > 0 ? 'reconnecting' : 'connecting');
        if (!this.unsubscribe) {
            this.unsubscribe = this.options.transport.subscribe((raw, origin) => this.onRaw(raw, origin));
        }
        this.diagnostics.logHandshake('hello.sent', `epoch=${this.epoch}`);
        this.sendEnvelope('hello', {
            protocolVersion: BRIDGE_PROTOCOL_VERSION,
            requestedCapabilities: this.options.requestedCapabilities,
            hostKind: this.options.hostKind,
            expectedTenantId: this.options.expectedTenantId,
            heartbeatIntervalMs: this.timing.heartbeatIntervalMs,
        });
        this.handshakeTimer = this.setTimer(() => this.onHandshakeTimeout(), this.timing.handshakeTimeoutMs);
    }

    /** Apply an opaque candidate document. Returns the assigned revision. */
    applyConfig(document: unknown): number {
        this.latestRevision += 1;
        this.sendEnvelope('config.apply', { revision: this.latestRevision, document });
        return this.latestRevision;
    }

    setSelection(anchorId: string | null): void {
        this.sendEnvelope('selection.set', { anchorId });
    }

    scrollTo(anchorId: string, behavior: 'auto' | 'smooth' = 'smooth', block: 'start' | 'center' | 'nearest' = 'center'): void {
        this.sendEnvelope('scroll.to', { anchorId, behavior, block });
    }

    setViewport(widthPx: number, scale?: number): void {
        this.sendEnvelope('viewport.set', { widthPx, scale });
    }

    shutdown(reason: 'host-closed' | 'superseded' | 'error' = 'host-closed'): void {
        if (this.state === 'closed') return;
        try {
            this.sendEnvelope('shutdown', { reason }, { skipAck: true });
        } finally {
            this.teardown('closed');
        }
    }

    /* ---------------- internals ---------------- */

    private resetSessionState(): void {
        this.peerEpoch = null;
        this.lastPeerSeq = 0;
        this.processedPeerSeqs.clear();
        for (const command of this.pending.values()) this.clearTimer(command.timer);
        this.pending.clear();
        this.clearHeartbeat();
    }

    private transition(state: BridgeState): void {
        if (this.state === state) return;
        this.state = state;
        this.diagnostics.setState(state, this.epoch);
        this.emit('stateChanged', state);
    }

    private emit<K extends keyof BridgeEventMap>(event: K, payload: BridgeEventMap[K]): void {
        const bucket = this.listeners.get(event);
        if (!bucket) return;
        for (const listener of [...bucket]) (listener as Listener<BridgeEventMap[K]>)(payload);
    }

    private sendEnvelope(type: string, payload: unknown, opts?: { ackOf?: number; skipAck?: boolean }): void {
        this.seq += 1;
        const envelope: BridgeEnvelope<string, unknown> = {
            marker: BRIDGE_MESSAGE_MARKER,
            v: BRIDGE_PROTOCOL_VERSION,
            type,
            seq: this.seq,
            epoch: this.epoch,
            sentAt: this.now(),
            payload,
            ...(opts?.ackOf !== undefined ? { ackOf: opts.ackOf } : {}),
        };
        this.sentAtBySeq.set(envelope.seq, envelope.sentAt);
        if (this.sentAtBySeq.size > 500) {
            const oldest = this.sentAtBySeq.keys().next().value as number;
            this.sentAtBySeq.delete(oldest);
        }
        this.options.transport.send(envelope);
        this.diagnostics.record('sent');
        if (!opts?.skipAck && ACK_REQUIRED_TYPES.includes(type)) {
            this.trackPending(envelope);
        }
    }

    private trackPending(envelope: BridgeEnvelope<string, unknown>): void {
        const command: PendingCommand = {
            envelope,
            retriesLeft: this.timing.maxCommandRetries,
            timer: this.setTimer(() => this.onAckTimeout(envelope.seq), this.timing.ackTimeoutMs),
        };
        this.pending.set(envelope.seq, command);
    }

    private onAckTimeout(seq: number): void {
        const command = this.pending.get(seq);
        if (!command) return;
        if (command.retriesLeft <= 0) {
            this.pending.delete(seq);
            this.fail(`Command ${command.envelope.type} (seq ${seq}) unacknowledged after retries.`);
            return;
        }
        command.retriesLeft -= 1;
        this.diagnostics.record('retries');
        this.options.transport.send(command.envelope); // idempotent per (epoch, seq)
        command.timer = this.setTimer(() => this.onAckTimeout(seq), this.timing.ackTimeoutMs);
    }

    private onHandshakeTimeout(): void {
        if (this.state === 'connected') return;
        this.scheduleReconnect('handshake timeout');
    }

    private scheduleReconnect(reason: string): void {
        this.diagnostics.logHandshake('reconnect.scheduled', reason);
        const backoffs = this.timing.reconnectBackoffMs;
        if (this.reconnectAttempt >= backoffs.length) {
            this.fail(`Reconnect attempts exhausted (${reason}).`);
            return;
        }
        const delay = backoffs[this.reconnectAttempt];
        this.reconnectAttempt += 1;
        this.diagnostics.record('reconnects');
        this.transition('reconnecting');
        this.setTimer(() => {
            if (this.state === 'reconnecting') {
                this.transition('idle');
                this.connect();
            }
        }, delay);
    }

    private fail(message: string): void {
        this.diagnostics.setError(message);
        this.teardown('failed');
        this.emit('failed', message);
    }

    private teardown(finalState: BridgeState): void {
        this.resetSessionState();
        if (this.handshakeTimer) this.clearTimer(this.handshakeTimer);
        if (this.unsubscribe) {
            this.unsubscribe();
            this.unsubscribe = null;
        }
        this.transition(finalState);
    }

    private clearHeartbeat(): void {
        if (this.heartbeatTimer) this.clearTimer(this.heartbeatTimer);
        if (this.heartbeatDeadline) this.clearTimer(this.heartbeatDeadline);
        this.heartbeatTimer = null;
        this.heartbeatDeadline = null;
    }

    private startHeartbeat(): void {
        this.clearHeartbeat();
        const tick = () => {
            this.sendEnvelope('heartbeat', { uptimeMs: 0 }, { skipAck: true });
            this.heartbeatTimer = this.setTimer(tick, this.timing.heartbeatIntervalMs);
        };
        this.heartbeatTimer = this.setTimer(tick, this.timing.heartbeatIntervalMs);
        this.armHeartbeatDeadline();
    }

    private armHeartbeatDeadline(): void {
        if (this.heartbeatDeadline) this.clearTimer(this.heartbeatDeadline);
        this.heartbeatDeadline = this.setTimer(() => {
            this.emit('peerLost', undefined as never);
            this.scheduleReconnect('heartbeat lost');
        }, this.timing.heartbeatTimeoutMs);
    }

    private onRaw(raw: unknown, origin: string): void {
        if (origin !== '' && origin !== this.options.expectedOrigin) {
            this.diagnostics.record('droppedForeign');
            return;
        }
        const message = asBridgeMessage(raw);
        if (!message) {
            this.diagnostics.record('droppedForeign');
            return;
        }
        this.diagnostics.record('received');

        if (!EPOCH_EXEMPT_TYPES.includes(message.type)) {
            if (this.peerEpoch === null || message.epoch !== this.peerEpoch) {
                this.diagnostics.record('droppedStaleEpoch');
                return;
            }
            if (this.processedPeerSeqs.has(message.seq)) {
                this.diagnostics.record('duplicatesIgnored');
                if (ACK_REQUIRED_TYPES.includes(message.type)) {
                    this.sendEnvelope('ack', {}, { ackOf: message.seq, skipAck: true });
                }
                return;
            }
            this.processedPeerSeqs.add(message.seq);
            if (this.processedPeerSeqs.size > 2_000) {
                this.processedPeerSeqs = new Set([...this.processedPeerSeqs].slice(-1_000));
            }
            this.lastPeerSeq = Math.max(this.lastPeerSeq, message.seq);
        }

        if (message.ackOf !== undefined) this.resolveAck(message.ackOf);
        this.route(message);
    }

    private resolveAck(ackOf: number): void {
        const command = this.pending.get(ackOf);
        if (command) {
            this.clearTimer(command.timer);
            this.pending.delete(ackOf);
        }
        const sentAt = this.sentAtBySeq.get(ackOf);
        if (sentAt !== undefined) this.diagnostics.addLatencySample(this.now() - sentAt);
    }

    private route(message: BridgeMessage): void {
        switch (message.type) {
            case 'ready':
                this.onReady(message as BridgeEnvelope<'ready', ReadyPayload>);
                return;
            case 'handshake.error': {
                const payload = message.payload as HandshakeErrorPayload;
                this.diagnostics.logHandshake('handshake.error', payload.code);
                this.emit('handshakeError', payload);
                this.fail(`Handshake refused: ${payload.code}`);
                return;
            }
            case 'heartbeat':
                this.armHeartbeatDeadline();
                this.sendEnvelope('ack', {}, { ackOf: message.seq, skipAck: true });
                return;
            case 'ack':
                this.armHeartbeatDeadline();
                return;
            case 'config.applied': {
                const payload = message.payload as ConfigAppliedPayload;
                if (payload.revision < this.appliedRevision) {
                    this.diagnostics.record('droppedStaleRevision');
                    return;
                }
                this.appliedRevision = payload.revision;
                this.emit('revisionApplied', payload.revision);
                this.emit('message', message);
                return;
            }
            case 'shutdown':
                this.teardown('closed');
                return;
            default:
                if (isExtensionType(message.type)) {
                    this.emit('message', message);
                    return;
                }
                // Known-or-unknown runtime events flow to subscribers; unknown
                // core types are counted but never fatal (forward compat).
                if (!this.isKnownEventType(message.type)) this.diagnostics.record('unknownTypesIgnored');
                this.emit('message', message);
        }
    }

    private isKnownEventType(type: string): boolean {
        return [
            'rects.changed',
            'selection.changed',
            'hover.changed',
            'scroll.changed',
            'viewport.changed',
            'navigation.blocked',
            'runtime.warning',
            'runtime.error',
        ].includes(type);
    }

    private onReady(message: BridgeEnvelope<'ready', ReadyPayload>): void {
        const payload = message.payload;
        if (!isProtocolCompatible(BRIDGE_PROTOCOL_VERSION, payload.protocolVersion)) {
            this.diagnostics.logHandshake('version.mismatch', payload.protocolVersion);
            this.fail(`Protocol major mismatch: local ${BRIDGE_PROTOCOL_VERSION}, remote ${payload.protocolVersion}.`);
            return;
        }
        if (this.options.expectedTenantId) {
            // Fail closed: mismatch AND missing runtime tenant both refuse.
            if (payload.runtime.tenantId !== this.options.expectedTenantId) {
                this.fail(
                    payload.runtime.tenantId
                        ? `Tenant mismatch: expected ${this.options.expectedTenantId}.`
                        : `Tenant missing: host expects ${this.options.expectedTenantId} but the runtime reported none.`
                );
                return;
            }
        }
        if (this.handshakeTimer) this.clearTimer(this.handshakeTimer);
        this.peerEpoch = message.epoch;
        this.reconnectAttempt = 0;
        this.grantedCapabilities = intersectCapabilities(
            this.options.requestedCapabilities,
            payload.grantedCapabilities
        );
        this.diagnostics.setPeerVersion(payload.protocolVersion);
        this.diagnostics.logHandshake('ready.received', `caps=${this.grantedCapabilities.join(',')}`);
        this.sendEnvelope('ack', {}, { ackOf: message.seq, skipAck: true });
        this.transition('connected');
        this.startHeartbeat();
        this.emit('ready', payload);
    }
}

/** Production transport over an iframe's contentWindow. */
export function createIframeTransport(
    iframe: HTMLIFrameElement,
    childOrigin: string,
    listenWindow: Window = window
): MessageTransport {
    return {
        send(message) {
            iframe.contentWindow?.postMessage(message, childOrigin);
        },
        subscribe(handler) {
            const onMessage = (event: MessageEvent) => {
                if (event.source !== iframe.contentWindow) return;
                handler(event.data, event.origin);
            };
            listenWindow.addEventListener('message', onMessage);
            return () => listenWindow.removeEventListener('message', onMessage);
        },
    };
}
