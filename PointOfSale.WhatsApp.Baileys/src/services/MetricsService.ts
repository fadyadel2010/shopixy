/**
 * Metrics Service
 * 
 * PHASE 2: Observability, Control & Operability
 * 
 * Implements PILLAR 1: Prometheus-style metrics for:
 * - QR Flow (generation, success, timeout, duration)
 * - Session Lifecycle (state, reconnects, poisoned, lifetime)
 * - Socket Health (disconnects, ECONNRESET, replaced during send)
 * - Messaging (send totals, latency, failures by state)
 * 
 * These metrics enable:
 * - Real-time dashboards
 * - Alerting on anomalies
 * - Forensic debugging
 * - Capacity planning
 */

import { Registry, Counter, Histogram, Gauge, collectDefaultMetrics } from 'prom-client';
import { createLogger } from '../utils/logger';

const logger = createLogger('MetricsService');

// Create a custom registry for our metrics
const register = new Registry();

// Collect default Node.js metrics (memory, CPU, event loop, etc.)
collectDefaultMetrics({ register });

// ═══════════════════════════════════════════════════════════════════════════════
// QR FLOW METRICS
// ═══════════════════════════════════════════════════════════════════════════════

/**
 * Total QR code generation attempts
 */
export const qrGenerationTotal = new Counter({
    name: 'whatsapp_qr_generation_total',
    help: 'Total number of QR code generation attempts',
    labelNames: ['tenant_id'],
    registers: [register]
});

/**
 * Total successful QR generations (QR appeared)
 */
export const qrSuccessTotal = new Counter({
    name: 'whatsapp_qr_success_total',
    help: 'Total number of successful QR code generations',
    labelNames: ['tenant_id'],
    registers: [register]
});

/**
 * Total QR generation timeouts
 */
export const qrTimeoutTotal = new Counter({
    name: 'whatsapp_qr_timeout_total',
    help: 'Total number of QR code generation timeouts',
    labelNames: ['tenant_id'],
    registers: [register]
});

/**
 * QR generation duration in seconds
 */
export const qrDurationSeconds = new Histogram({
    name: 'whatsapp_qr_duration_seconds',
    help: 'Duration of QR code generation in seconds',
    labelNames: ['tenant_id', 'result'],
    buckets: [0.5, 1, 2, 5, 10, 15, 30],
    registers: [register]
});

// ═══════════════════════════════════════════════════════════════════════════════
// SESSION LIFECYCLE METRICS
// ═══════════════════════════════════════════════════════════════════════════════

/**
 * Current session count by state
 */
export const sessionStateGauge = new Gauge({
    name: 'whatsapp_session_state_total',
    help: 'Current number of sessions by state',
    labelNames: ['state'],
    registers: [register]
});

/**
 * Total reconnect attempts
 */
export const sessionReconnectAttemptsTotal = new Counter({
    name: 'whatsapp_session_reconnect_attempts_total',
    help: 'Total number of session reconnect attempts',
    labelNames: ['tenant_id'],
    registers: [register]
});

/**
 * Total sessions that became poisoned
 */
export const sessionPoisonedTotal = new Counter({
    name: 'whatsapp_session_poisoned_total',
    help: 'Total number of sessions that became poisoned',
    labelNames: ['tenant_id', 'status_code'],
    registers: [register]
});

/**
 * Session lifetime in seconds
 */
export const sessionLifetimeSeconds = new Histogram({
    name: 'whatsapp_session_lifetime_seconds',
    help: 'Session lifetime in seconds (time connected before disconnect)',
    labelNames: ['tenant_id', 'disconnect_reason'],
    buckets: [60, 300, 600, 1800, 3600, 7200, 14400, 28800],
    registers: [register]
});

/**
 * Current sessions with high reconnect attempts (potential storm)
 */
export const sessionReconnectStormGauge = new Gauge({
    name: 'whatsapp_session_reconnect_storm_total',
    help: 'Number of sessions with 3+ reconnect attempts (potential storm)',
    registers: [register]
});

/**
 * Sessions stuck in transitional states
 */
export const sessionStuckGauge = new Gauge({
    name: 'whatsapp_session_stuck_total',
    help: 'Number of sessions stuck in CONNECTING or QR_PENDING for > 30 seconds',
    registers: [register]
});

// ═══════════════════════════════════════════════════════════════════════════════
// SOCKET HEALTH METRICS
// ═══════════════════════════════════════════════════════════════════════════════

/**
 * Total socket disconnections by reason
 */
export const socketDisconnectTotal = new Counter({
    name: 'whatsapp_socket_disconnect_total',
    help: 'Total number of socket disconnections',
    labelNames: ['tenant_id', 'reason', 'status_code'],
    registers: [register]
});

/**
 * Total ECONNRESET errors
 */
export const socketEconnresetTotal = new Counter({
    name: 'whatsapp_socket_econnreset_total',
    help: 'Total number of ECONNRESET socket errors',
    labelNames: ['tenant_id'],
    registers: [register]
});

/**
 * Socket replaced during send operations
 */
export const socketReplacedDuringSendTotal = new Counter({
    name: 'whatsapp_socket_replaced_during_send_total',
    help: 'Total number of times socket was replaced during send operation',
    labelNames: ['tenant_id'],
    registers: [register]
});

/**
 * Current active socket count
 */
export const socketActiveGauge = new Gauge({
    name: 'whatsapp_socket_active_total',
    help: 'Current number of active sockets',
    registers: [register]
});

// ═══════════════════════════════════════════════════════════════════════════════
// MESSAGING METRICS
// ═══════════════════════════════════════════════════════════════════════════════

/**
 * Total message send attempts by result
 */
export const messageSendTotal = new Counter({
    name: 'whatsapp_message_send_total',
    help: 'Total number of message send attempts',
    labelNames: ['tenant_id', 'result'],
    registers: [register]
});

/**
 * Message send latency in seconds
 */
export const messageLatencySeconds = new Histogram({
    name: 'whatsapp_message_latency_seconds',
    help: 'Message send latency in seconds',
    labelNames: ['tenant_id', 'result'],
    buckets: [0.1, 0.25, 0.5, 1, 2, 5, 10],
    registers: [register]
});

/**
 * Message failures by session state at time of failure
 */
export const messageFailuresByState = new Counter({
    name: 'whatsapp_message_failures_by_state',
    help: 'Message send failures by session state at time of failure',
    labelNames: ['tenant_id', 'state', 'reason'],
    registers: [register]
});

// ═══════════════════════════════════════════════════════════════════════════════
// OPERATIONAL METRICS
// ═══════════════════════════════════════════════════════════════════════════════

/**
 * Service uptime in seconds
 */
export const serviceUptimeSeconds = new Gauge({
    name: 'whatsapp_service_uptime_seconds',
    help: 'Service uptime in seconds',
    registers: [register]
});

/**
 * Total admin force-reset operations
 */
export const adminForceResetTotal = new Counter({
    name: 'whatsapp_admin_force_reset_total',
    help: 'Total number of admin force-reset operations',
    labelNames: ['tenant_id'],
    registers: [register]
});

// ═══════════════════════════════════════════════════════════════════════════════
// METRICS SERVICE CLASS
// ═══════════════════════════════════════════════════════════════════════════════

/**
 * Metrics Service
 * 
 * Provides access to all Prometheus metrics and helper functions.
 */
export class MetricsService {
    private startTime: number;

    constructor() {
        this.startTime = Date.now();
        logger.info('MetricsService initialized');
    }

    /**
     * Get the Prometheus registry
     */
    getRegistry(): Registry {
        return register;
    }

    /**
     * Get metrics in Prometheus text format
     */
    async getMetrics(): Promise<string> {
        // Update uptime before returning metrics
        serviceUptimeSeconds.set((Date.now() - this.startTime) / 1000);

        return await register.metrics();
    }

    /**
     * Get content type for Prometheus metrics endpoint
     */
    getContentType(): string {
        return register.contentType;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // QR METRICS HELPERS
    // ═══════════════════════════════════════════════════════════════════════════

    /**
     * Record a QR generation attempt
     */
    recordQrGeneration(tenantId: string): void {
        qrGenerationTotal.inc({ tenant_id: tenantId });
    }

    /**
     * Record a successful QR generation
     */
    recordQrSuccess(tenantId: string, durationSeconds: number): void {
        qrSuccessTotal.inc({ tenant_id: tenantId });
        qrDurationSeconds.observe({ tenant_id: tenantId, result: 'success' }, durationSeconds);
    }

    /**
     * Record a QR generation timeout
     */
    recordQrTimeout(tenantId: string, durationSeconds: number): void {
        qrTimeoutTotal.inc({ tenant_id: tenantId });
        qrDurationSeconds.observe({ tenant_id: tenantId, result: 'timeout' }, durationSeconds);
    }

    /**
     * Record a QR generation failure
     */
    recordQrFailure(tenantId: string, durationSeconds: number): void {
        qrDurationSeconds.observe({ tenant_id: tenantId, result: 'failure' }, durationSeconds);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SESSION METRICS HELPERS
    // ═══════════════════════════════════════════════════════════════════════════

    /**
     * Update session state gauges based on current sessions
     */
    updateSessionStateMetrics(stateCount: Record<string, number>): void {
        // Reset all states to 0 first
        const states = ['connected', 'disconnected', 'qr_pending', 'connecting', 'poisoned', 'logged_out'];
        for (const state of states) {
            sessionStateGauge.set({ state }, stateCount[state] || 0);
        }
    }

    /**
     * Record a reconnect attempt
     */
    recordReconnectAttempt(tenantId: string): void {
        sessionReconnectAttemptsTotal.inc({ tenant_id: tenantId });
    }

    /**
     * Record a session becoming poisoned
     */
    recordSessionPoisoned(tenantId: string, statusCode: number): void {
        sessionPoisonedTotal.inc({ tenant_id: tenantId, status_code: String(statusCode) });
    }

    /**
     * Record session lifetime when it disconnects
     */
    recordSessionLifetime(tenantId: string, lifetimeSeconds: number, disconnectReason: string): void {
        sessionLifetimeSeconds.observe(
            { tenant_id: tenantId, disconnect_reason: disconnectReason },
            lifetimeSeconds
        );
    }

    /**
     * Update stuck session count
     */
    updateStuckSessionCount(count: number): void {
        sessionStuckGauge.set(count);
    }

    /**
     * Update reconnect storm session count
     */
    updateReconnectStormCount(count: number): void {
        sessionReconnectStormGauge.set(count);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SOCKET METRICS HELPERS
    // ═══════════════════════════════════════════════════════════════════════════

    /**
     * Record a socket disconnection
     */
    recordSocketDisconnect(tenantId: string, reason: string, statusCode?: number): void {
        socketDisconnectTotal.inc({
            tenant_id: tenantId,
            reason,
            status_code: String(statusCode ?? 0)
        });

        // Track ECONNRESET specifically
        if (reason === 'ECONNRESET') {
            socketEconnresetTotal.inc({ tenant_id: tenantId });
        }
    }

    /**
     * Record socket replaced during send
     */
    recordSocketReplacedDuringSend(tenantId: string): void {
        socketReplacedDuringSendTotal.inc({ tenant_id: tenantId });
    }

    /**
     * Update active socket count
     */
    updateActiveSocketCount(count: number): void {
        socketActiveGauge.set(count);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MESSAGE METRICS HELPERS
    // ═══════════════════════════════════════════════════════════════════════════

    /**
     * Record a message send attempt
     */
    recordMessageSend(tenantId: string, result: 'success' | 'failure', latencySeconds: number): void {
        messageSendTotal.inc({ tenant_id: tenantId, result });
        messageLatencySeconds.observe({ tenant_id: tenantId, result }, latencySeconds);
    }

    /**
     * Record a message failure with session state context
     */
    recordMessageFailureByState(tenantId: string, state: string, reason: string): void {
        messageFailuresByState.inc({ tenant_id: tenantId, state, reason });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // OPERATIONAL METRICS HELPERS
    // ═══════════════════════════════════════════════════════════════════════════

    /**
     * Record an admin force-reset operation
     */
    recordAdminForceReset(tenantId: string): void {
        adminForceResetTotal.inc({ tenant_id: tenantId });
    }
}

// Export singleton instance
export const metricsService = new MetricsService();
