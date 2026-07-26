/**
 * Admission Controller Service
 * 
 * PHASE 3: Reliability, Self-Healing & Scale Readiness
 * 
 * Implements PILLAR 3: Session Admission Control
 * 
 * Prevents overload BEFORE it happens by:
 * - Limiting concurrent CONNECTING sessions
 * - Limiting concurrent QR_PENDING sessions
 * - Rejecting new sessions with explicit reasons when limits exceeded
 * 
 * Also implements PILLAR 4: Failure Domain Isolation
 * - One bad tenant cannot affect others
 * - Per-tenant connection quotas
 */

import { ConnectionStatus, WhatsAppSession } from '../models/Session';
import { createLogger } from '../utils/logger';
import { metricsService } from './MetricsService';
import { Counter, Gauge } from 'prom-client';

const logger = createLogger('AdmissionController');

// ═══════════════════════════════════════════════════════════════════════════════
// ADMISSION CONTROL METRICS
// ═══════════════════════════════════════════════════════════════════════════════

const admissionRejectedTotal = new Counter({
    name: 'whatsapp_admission_rejected_total',
    help: 'Total number of session admissions rejected',
    labelNames: ['tenant_id', 'reason'],
    registers: [metricsService.getRegistry()]
});

const admissionCurrentConnecting = new Gauge({
    name: 'whatsapp_admission_connecting_current',
    help: 'Current number of sessions in CONNECTING state',
    registers: [metricsService.getRegistry()]
});

const admissionCurrentQrPending = new Gauge({
    name: 'whatsapp_admission_qr_pending_current',
    help: 'Current number of sessions in QR_PENDING state',
    registers: [metricsService.getRegistry()]
});

const admissionTenantQuotaUsed = new Gauge({
    name: 'whatsapp_admission_tenant_quota_used',
    help: 'Number of sessions per tenant',
    labelNames: ['tenant_id'],
    registers: [metricsService.getRegistry()]
});

// ═══════════════════════════════════════════════════════════════════════════════
// ADMISSION CONTROL CONFIGURATION
// ═══════════════════════════════════════════════════════════════════════════════

export interface AdmissionConfig {
    // Global limits
    maxConcurrentConnecting: number;
    maxConcurrentQrPending: number;
    maxTotalSessions: number;

    // Per-tenant limits (Failure Domain Isolation)
    maxSessionsPerTenant: number;
    maxConnectingPerTenant: number;
    maxQrPendingPerTenant: number;
}

const DEFAULT_CONFIG: AdmissionConfig = {
    // Global limits (prevent system-wide overload)
    maxConcurrentConnecting: 10,
    maxConcurrentQrPending: 15,
    maxTotalSessions: 100,

    // Per-tenant limits (prevent one tenant affecting others)
    maxSessionsPerTenant: 5,
    maxConnectingPerTenant: 2,
    maxQrPendingPerTenant: 3
};

// ═══════════════════════════════════════════════════════════════════════════════
// ADMISSION RESULT
// ═══════════════════════════════════════════════════════════════════════════════

export interface AdmissionResult {
    admitted: boolean;
    reason?: string;
    currentCount?: number;
    limit?: number;
    retryAfterMs?: number;
}

// ═══════════════════════════════════════════════════════════════════════════════
// ADMISSION CONTROLLER SERVICE
// ═══════════════════════════════════════════════════════════════════════════════

export class AdmissionController {
    private config: AdmissionConfig;

    // Track tenant session counts for isolation
    private tenantSessionCounts: Map<string, number> = new Map();
    private tenantConnectingCounts: Map<string, number> = new Map();
    private tenantQrPendingCounts: Map<string, number> = new Map();

    constructor(config?: Partial<AdmissionConfig>) {
        this.config = { ...DEFAULT_CONFIG, ...config };
        logger.info({ config: this.config }, 'AdmissionController initialized');
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SESSION STATE TRACKING
    // ═══════════════════════════════════════════════════════════════════════════

    /**
     * Update session counts based on current sessions
     * Should be called periodically or after session state changes
     */
    updateSessionCounts(sessions: WhatsAppSession[]): void {
        // Reset counts
        this.tenantSessionCounts.clear();
        this.tenantConnectingCounts.clear();
        this.tenantQrPendingCounts.clear();

        let totalConnecting = 0;
        let totalQrPending = 0;

        for (const session of sessions) {
            const tenantId = session.tenantId;

            // Total sessions per tenant
            this.tenantSessionCounts.set(
                tenantId,
                (this.tenantSessionCounts.get(tenantId) || 0) + 1
            );

            // Connecting count
            if (session.status === ConnectionStatus.CONNECTING) {
                totalConnecting++;
                this.tenantConnectingCounts.set(
                    tenantId,
                    (this.tenantConnectingCounts.get(tenantId) || 0) + 1
                );
            }

            // QR Pending count
            if (session.status === ConnectionStatus.QR_PENDING) {
                totalQrPending++;
                this.tenantQrPendingCounts.set(
                    tenantId,
                    (this.tenantQrPendingCounts.get(tenantId) || 0) + 1
                );
            }
        }

        // Update metrics
        admissionCurrentConnecting.set(totalConnecting);
        admissionCurrentQrPending.set(totalQrPending);

        // Update per-tenant metrics
        for (const [tenantId, count] of this.tenantSessionCounts.entries()) {
            admissionTenantQuotaUsed.set({ tenant_id: tenantId }, count);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ADMISSION CHECKS
    // ═══════════════════════════════════════════════════════════════════════════

    /**
     * Check if a new session can be created for a tenant
     */
    checkNewSession(tenantId: string, totalSessions: number): AdmissionResult {
        // Check 1: Global total session limit
        if (totalSessions >= this.config.maxTotalSessions) {
            logger.warn({
                tenantId,
                current: totalSessions,
                limit: this.config.maxTotalSessions
            }, 'PILLAR 3: Global session limit reached');

            admissionRejectedTotal.inc({ tenant_id: tenantId, reason: 'global_session_limit' });

            return {
                admitted: false,
                reason: `System at capacity: ${totalSessions}/${this.config.maxTotalSessions} sessions`,
                currentCount: totalSessions,
                limit: this.config.maxTotalSessions,
                retryAfterMs: 30000
            };
        }

        // Check 2: Per-tenant session limit (Failure Domain Isolation)
        const tenantCount = this.tenantSessionCounts.get(tenantId) || 0;
        if (tenantCount >= this.config.maxSessionsPerTenant) {
            logger.warn({
                tenantId,
                current: tenantCount,
                limit: this.config.maxSessionsPerTenant
            }, 'PILLAR 4: Per-tenant session limit reached');

            admissionRejectedTotal.inc({ tenant_id: tenantId, reason: 'tenant_session_limit' });

            return {
                admitted: false,
                reason: `Tenant quota exceeded: ${tenantCount}/${this.config.maxSessionsPerTenant} sessions`,
                currentCount: tenantCount,
                limit: this.config.maxSessionsPerTenant,
                retryAfterMs: 60000
            };
        }

        return { admitted: true };
    }

    /**
     * Check if a session can transition to CONNECTING state
     */
    checkConnectingAdmission(tenantId: string, sessions: WhatsAppSession[]): AdmissionResult {
        // Update counts first
        this.updateSessionCounts(sessions);

        // Check 1: Global CONNECTING limit
        let currentConnecting = 0;
        for (const session of sessions) {
            if (session.status === ConnectionStatus.CONNECTING) {
                currentConnecting++;
            }
        }

        if (currentConnecting >= this.config.maxConcurrentConnecting) {
            logger.warn({
                tenantId,
                current: currentConnecting,
                limit: this.config.maxConcurrentConnecting
            }, 'PILLAR 3: Global CONNECTING limit reached');

            admissionRejectedTotal.inc({ tenant_id: tenantId, reason: 'global_connecting_limit' });

            return {
                admitted: false,
                reason: `Too many sessions connecting: ${currentConnecting}/${this.config.maxConcurrentConnecting}`,
                currentCount: currentConnecting,
                limit: this.config.maxConcurrentConnecting,
                retryAfterMs: 5000
            };
        }

        // Check 2: Per-tenant CONNECTING limit (Failure Domain Isolation)
        const tenantConnecting = this.tenantConnectingCounts.get(tenantId) || 0;
        if (tenantConnecting >= this.config.maxConnectingPerTenant) {
            logger.warn({
                tenantId,
                current: tenantConnecting,
                limit: this.config.maxConnectingPerTenant
            }, 'PILLAR 4: Per-tenant CONNECTING limit reached');

            admissionRejectedTotal.inc({ tenant_id: tenantId, reason: 'tenant_connecting_limit' });

            return {
                admitted: false,
                reason: `Tenant has too many connecting sessions: ${tenantConnecting}/${this.config.maxConnectingPerTenant}`,
                currentCount: tenantConnecting,
                limit: this.config.maxConnectingPerTenant,
                retryAfterMs: 10000
            };
        }

        return { admitted: true };
    }

    /**
     * Check if a session can transition to QR_PENDING state
     */
    checkQrPendingAdmission(tenantId: string, sessions: WhatsAppSession[]): AdmissionResult {
        // Update counts first
        this.updateSessionCounts(sessions);

        // Check 1: Global QR_PENDING limit
        let currentQrPending = 0;
        for (const session of sessions) {
            if (session.status === ConnectionStatus.QR_PENDING) {
                currentQrPending++;
            }
        }

        if (currentQrPending >= this.config.maxConcurrentQrPending) {
            logger.warn({
                tenantId,
                current: currentQrPending,
                limit: this.config.maxConcurrentQrPending
            }, 'PILLAR 3: Global QR_PENDING limit reached');

            admissionRejectedTotal.inc({ tenant_id: tenantId, reason: 'global_qr_pending_limit' });

            return {
                admitted: false,
                reason: `Too many sessions waiting for QR scan: ${currentQrPending}/${this.config.maxConcurrentQrPending}`,
                currentCount: currentQrPending,
                limit: this.config.maxConcurrentQrPending,
                retryAfterMs: 10000
            };
        }

        // Check 2: Per-tenant QR_PENDING limit (Failure Domain Isolation)
        const tenantQrPending = this.tenantQrPendingCounts.get(tenantId) || 0;
        if (tenantQrPending >= this.config.maxQrPendingPerTenant) {
            logger.warn({
                tenantId,
                current: tenantQrPending,
                limit: this.config.maxQrPendingPerTenant
            }, 'PILLAR 4: Per-tenant QR_PENDING limit reached');

            admissionRejectedTotal.inc({ tenant_id: tenantId, reason: 'tenant_qr_pending_limit' });

            return {
                admitted: false,
                reason: `Tenant has too many pending QR scans: ${tenantQrPending}/${this.config.maxQrPendingPerTenant}`,
                currentCount: tenantQrPending,
                limit: this.config.maxQrPendingPerTenant,
                retryAfterMs: 30000
            };
        }

        return { admitted: true };
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // DIAGNOSTICS
    // ═══════════════════════════════════════════════════════════════════════════

    /**
     * Get current admission status
     */
    getAdmissionStatus(): Record<string, unknown> {
        let totalConnecting = 0;
        let totalQrPending = 0;

        for (const count of this.tenantConnectingCounts.values()) {
            totalConnecting += count;
        }
        for (const count of this.tenantQrPendingCounts.values()) {
            totalQrPending += count;
        }

        return {
            globalLimits: {
                connecting: { current: totalConnecting, limit: this.config.maxConcurrentConnecting },
                qrPending: { current: totalQrPending, limit: this.config.maxConcurrentQrPending },
                totalSessions: { limit: this.config.maxTotalSessions }
            },
            perTenantLimits: {
                maxSessions: this.config.maxSessionsPerTenant,
                maxConnecting: this.config.maxConnectingPerTenant,
                maxQrPending: this.config.maxQrPendingPerTenant
            },
            tenantCounts: Object.fromEntries(this.tenantSessionCounts),
            tenantConnecting: Object.fromEntries(this.tenantConnectingCounts),
            tenantQrPending: Object.fromEntries(this.tenantQrPendingCounts)
        };
    }

    /**
     * Get configuration
     */
    getConfig(): AdmissionConfig {
        return { ...this.config };
    }
}
