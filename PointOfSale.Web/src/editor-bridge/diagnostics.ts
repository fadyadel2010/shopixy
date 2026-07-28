/**
 * Bridge Diagnostics — ARCH-1.2 Module 5.
 * Framework-free, presentation-agnostic. Collected by both bridge ends
 * (each end owns its own diagnostics instance).
 */

export interface BridgeDiagnosticsSnapshot {
    bridgeVersion: string;
    /** negotiated peer version, once known */
    peerVersion: string | null;
    state: string;
    epoch: number;
    handshakeLog: readonly HandshakeLogEntry[];
    /** rolling latency stats from heartbeat/ack round-trips (ms) */
    latency: { lastMs: number | null; avgMs: number | null; maxMs: number | null; samples: number };
    sent: number;
    received: number;
    droppedForeign: number;
    droppedStaleEpoch: number;
    droppedStaleRevision: number;
    duplicatesIgnored: number;
    unknownTypesIgnored: number;
    retries: number;
    reconnects: number;
    lastError: string | null;
    healthy: boolean;
}

export interface HandshakeLogEntry {
    at: number;
    event: string;
    detail?: string;
}

const MAX_HANDSHAKE_LOG = 50;
const MAX_LATENCY_SAMPLES = 100;

export class BridgeDiagnostics {
    private readonly bridgeVersion: string;
    private peerVersion: string | null = null;
    private state = 'idle';
    private epoch = 0;
    private readonly log: HandshakeLogEntry[] = [];
    private readonly latencies: number[] = [];
    private counters = {
        sent: 0,
        received: 0,
        droppedForeign: 0,
        droppedStaleEpoch: 0,
        droppedStaleRevision: 0,
        duplicatesIgnored: 0,
        unknownTypesIgnored: 0,
        retries: 0,
        reconnects: 0,
    };
    private lastError: string | null = null;

    constructor(bridgeVersion: string) {
        this.bridgeVersion = bridgeVersion;
    }

    record(event: keyof BridgeDiagnostics['counters']): void {
        this.counters[event] += 1;
    }

    logHandshake(event: string, detail?: string): void {
        this.log.push({ at: Date.now(), event, detail });
        if (this.log.length > MAX_HANDSHAKE_LOG) this.log.shift();
    }

    setState(state: string, epoch: number): void {
        this.state = state;
        this.epoch = epoch;
    }

    setPeerVersion(version: string): void {
        this.peerVersion = version;
    }

    addLatencySample(ms: number): void {
        this.latencies.push(ms);
        if (this.latencies.length > MAX_LATENCY_SAMPLES) this.latencies.shift();
    }

    setError(message: string): void {
        this.lastError = message;
    }

    snapshot(): BridgeDiagnosticsSnapshot {
        const samples = this.latencies.length;
        const avg = samples ? this.latencies.reduce((sum, value) => sum + value, 0) / samples : null;
        const max = samples ? Math.max(...this.latencies) : null;
        return {
            bridgeVersion: this.bridgeVersion,
            peerVersion: this.peerVersion,
            state: this.state,
            epoch: this.epoch,
            handshakeLog: [...this.log],
            latency: {
                lastMs: samples ? this.latencies[samples - 1] : null,
                avgMs: avg,
                maxMs: max,
                samples,
            },
            ...this.counters,
            lastError: this.lastError,
            healthy: this.state === 'connected' && this.lastError === null,
        };
    }
}
