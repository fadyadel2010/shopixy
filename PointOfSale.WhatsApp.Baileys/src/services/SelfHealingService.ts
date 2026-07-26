/**
 * Self-Healing Service
 * 
 * PHASE 3: Reliability, Self-Healing & Scale Readiness
 * 
 * Implements PILLAR 1: Self-Healing Actions
 * 
 * Automatic recovery actions:
 * - Auto-reset POISONED sessions (if not reset in last 10 minutes)
 * - Auto-reset stuck QR_PENDING sessions (> 60 seconds)
 * - Reconnect storm suppression with exponential cooldown
 * 
 * The system fixes itself without human intervention.
 */

import { ConnectionStatus, WhatsAppSession } from '../models/Session';
import { createLogger } from '../utils/logger';
import { metricsService } from './MetricsService';
import { Counter, Gauge } from 'prom-client';

const logger = createLogger('SelfHealingService');

// ═══════════════════════════════════════════════════════════════════════════════
// SELF-HEALING METRICS
// ═══════════════════════════════════════════════════════════════════════════════

const selfHealingActionsTotal = new Counter({
    name: 'whatsapp_self_healing_actions_total',
    help: 'Total number of self-healing actions taken',
    labelNames: ['action_type'],
    registers: [metricsService.getRegistry()]
});

const selfHealingPoisonedAutoReset = new Counter({
    name: 'whatsapp_self_healing_poisoned_auto_reset_total',
    help: 'Total number of poisoned sessions auto-reset',
    labelNames: ['tenant_id'],
    registers: [metricsService.getRegistry()]
});

const selfHealingStuckQrAutoReset = new Counter({
    name: 'whatsapp_self_healing_stuck_qr_auto_reset_total',
    help: 'Total number of stuck QR sessions auto-reset',
    labelNames: ['tenant_id'],
    registers: [metricsService.getRegistry()]
});

const selfHealingReconnectSuppressed = new Counter({
    name: 'whatsapp_self_healing_reconnect_suppressed_total',
    help: 'Total number of reconnects suppressed due to storm',
    labelNames: ['tenant_id'],
    registers: [metricsService.getRegistry()]
});

const selfHealingPendingCooldowns = new Gauge({
    name: 'whatsapp_self_healing_pending_cooldowns',
    help: 'Number of sessions currently in cooldown',
    registers: [metricsService.getRegistry()]
});

// ═══════════════════════════════════════════════════════════════════════════════
// SELF-HEALING CONFIGURATION
// ═══════════════════════════════════════════════════════════════════════════════

export interface SelfHealingConfig {
    // Poisoned session auto-reset
    poisonedAutoResetEnabled: boolean;
    poisonedAutoResetDelayMs: number;  // Wait before auto-reset (10 minutes default)

    // Stuck QR auto-reset
    stuckQrAutoResetEnabled: boolean;
    stuckQrThresholdMs: number;  // 60 seconds default

    // Reconnect storm suppression
    reconnectStormEnabled: boolean;
    reconnectStormThreshold: number;  // 3 attempts triggers cooldown
    reconnectCooldownBaseMs: number;  // Base cooldown (multiplied exponentially)
    reconnectCooldownMaxMs: number;   // Max cooldown cap

    // Check interval
    checkIntervalMs: number;
}

const DEFAULT_CONFIG: SelfHealingConfig = {
    // Poisoned session auto-reset after 10 minutes
    poisonedAutoResetEnabled: true,
    poisonedAutoResetDelayMs: 10 * 60 * 1000, // 10 minutes

    // Stuck QR auto-reset after 60 seconds
    stuckQrAutoResetEnabled: true,
    stuckQrThresholdMs: 60 * 1000, // 60 seconds

    // Reconnect storm: exponential cooldown after 3 attempts
    reconnectStormEnabled: true,
    reconnectStormThreshold: 3,
    reconnectCooldownBaseMs: 30 * 1000, // 30 seconds base
    reconnectCooldownMaxMs: 15 * 60 * 1000, // 15 minutes max

    // Check every 10 seconds
    checkIntervalMs: 10 * 1000
};

// ═══════════════════════════════════════════════════════════════════════════════
// COOLDOWN TRACKING
// ═══════════════════════════════════════════════════════════════════════════════

interface CooldownEntry {
    tenantId: string;
    cooldownUntil: number;
    attempts: number;
    reason: string;
}

// ═══════════════════════════════════════════════════════════════════════════════
// SELF-HEALING SERVICE
// ═══════════════════════════════════════════════════════════════════════════════

export class SelfHealingService {
    private config: SelfHealingConfig;
    private checkInterval: NodeJS.Timeout | null = null;

    // Track sessions that were recently auto-reset to prevent loops
    private recentAutoResets: Map<string, number> = new Map();

    // Track reconnect cooldowns per tenant
    private reconnectCooldowns: Map<string, CooldownEntry> = new Map();

    // Callback functions (set by SessionManager)
    private forceResetCallback: ((tenantId: string) => Promise<void>) | null = null;
    private getSessionsCallback: (() => WhatsAppSession[]) | null = null;

    constructor(config?: Partial<SelfHealingConfig>) {
        this.config = { ...DEFAULT_CONFIG, ...config };
        logger.info({ config: this.config }, 'SelfHealingService initialized');
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // LIFECYCLE
    // ═══════════════════════════════════════════════════════════════════════════

    /**
     * Start the self-healing service
     */
    start(
        forceResetCallback: (tenantId: string) => Promise<void>,
        getSessionsCallback: () => WhatsAppSession[]
    ): void {
        this.forceResetCallback = forceResetCallback;
        this.getSessionsCallback = getSessionsCallback;

        this.checkInterval = setInterval(() => {
            this.runHealingCycle().catch(err => {
                logger.error({ err }, 'Self-healing cycle failed');
            });
        }, this.config.checkIntervalMs);

        logger.info({ intervalMs: this.config.checkIntervalMs }, 'Self-healing service started');
    }

    /**
     * Stop the self-healing service
     */
    stop(): void {
        if (this.checkInterval) {
            clearInterval(this.checkInterval);
            this.checkInterval = null;
        }
        logger.info('Self-healing service stopped');
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MAIN HEALING CYCLE
    // ═══════════════════════════════════════════════════════════════════════════

    /**
     * Run one healing cycle
     */
    private async runHealingCycle(): Promise<void> {
        if (!this.getSessionsCallback || !this.forceResetCallback) {
            return;
        }

        const sessions = this.getSessionsCallback();
        const now = Date.now();

        // Clean up old auto-reset tracking (older than 15 minutes)
        for (const [tenantId, resetTime] of this.recentAutoResets.entries()) {
            if (now - resetTime > 15 * 60 * 1000) {
                this.recentAutoResets.delete(tenantId);
            }
        }

        // Clean up expired cooldowns
        for (const [tenantId, entry] of this.reconnectCooldowns.entries()) {
            if (now > entry.cooldownUntil) {
                this.reconnectCooldowns.delete(tenantId);
            }
        }

        // Update metrics
        selfHealingPendingCooldowns.set(this.reconnectCooldowns.size);

        // Run healing checks
        for (const session of sessions) {
            await this.checkAndHealSession(session, now);
        }
    }

    /**
     * Check and heal a single session
     */
    private async checkAndHealSession(session: WhatsAppSession, now: number): Promise<void> {
        const tenantId = session.tenantId;

        // Skip if recently auto-reset
        if (this.recentAutoResets.has(tenantId)) {
            return;
        }

        // Check 1: Auto-reset POISONED sessions
        if (this.config.poisonedAutoResetEnabled) {
            await this.checkPoisonedSession(session, now);
        }

        // Check 2: Auto-reset stuck QR_PENDING sessions
        if (this.config.stuckQrAutoResetEnabled) {
            await this.checkStuckQrSession(session, now);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // POISONED SESSION AUTO-RESET
    // ═══════════════════════════════════════════════════════════════════════════

    /**
     * Check and auto-reset POISONED sessions
     */
    private async checkPoisonedSession(session: WhatsAppSession, now: number): Promise<void> {
        if (session.status !== ConnectionStatus.POISONED &&
            session.status !== ConnectionStatus.LOGGED_OUT) {
            return;
        }

        const tenantId = session.tenantId;
        const stuckDuration = now - session.statusChangedAt.getTime();

        // Only reset if stuck for longer than the configured delay
        if (stuckDuration < this.config.poisonedAutoResetDelayMs) {
            return;
        }

        logger.info({
            tenantId,
            sessionId: session.sessionId,
            status: session.status,
            stuckForMs: stuckDuration
        }, 'PILLAR 1: Auto-resetting POISONED session');

        try {
            await this.forceResetCallback!(tenantId);
            this.recentAutoResets.set(tenantId, now);

            selfHealingActionsTotal.inc({ action_type: 'poisoned_auto_reset' });
            selfHealingPoisonedAutoReset.inc({ tenant_id: tenantId });

            logger.info({ tenantId }, 'PILLAR 1: POISONED session auto-reset complete');
        } catch (error) {
            logger.error({ err: error, tenantId }, 'Failed to auto-reset POISONED session');
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // STUCK QR AUTO-RESET
    // ═══════════════════════════════════════════════════════════════════════════

    /**
     * Check and auto-reset stuck QR_PENDING sessions
     */
    private async checkStuckQrSession(session: WhatsAppSession, now: number): Promise<void> {
        if (session.status !== ConnectionStatus.QR_PENDING) {
            return;
        }

        const tenantId = session.tenantId;
        const stuckDuration = now - session.statusChangedAt.getTime();

        // Only reset if stuck for longer than threshold
        if (stuckDuration < this.config.stuckQrThresholdMs) {
            return;
        }

        logger.info({
            tenantId,
            sessionId: session.sessionId,
            stuckForMs: stuckDuration
        }, 'PILLAR 1: Auto-resetting stuck QR_PENDING session');

        try {
            await this.forceResetCallback!(tenantId);
            this.recentAutoResets.set(tenantId, now);

            selfHealingActionsTotal.inc({ action_type: 'stuck_qr_auto_reset' });
            selfHealingStuckQrAutoReset.inc({ tenant_id: tenantId });

            logger.info({ tenantId }, 'PILLAR 1: Stuck QR session auto-reset complete');
        } catch (error) {
            logger.error({ err: error, tenantId }, 'Failed to auto-reset stuck QR session');
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // RECONNECT STORM SUPPRESSION
    // ═══════════════════════════════════════════════════════════════════════════

    /**
     * Check if reconnect should be allowed for a session
     * Called by SessionManager before scheduling reconnect
     */
    shouldAllowReconnect(session: WhatsAppSession): { allowed: boolean; cooldownMs?: number; reason?: string } {
        if (!this.config.reconnectStormEnabled) {
            return { allowed: true };
        }

        const tenantId = session.tenantId;
        const now = Date.now();

        // Check if in cooldown
        const cooldown = this.reconnectCooldowns.get(tenantId);
        if (cooldown && now < cooldown.cooldownUntil) {
            const remainingMs = cooldown.cooldownUntil - now;

            selfHealingReconnectSuppressed.inc({ tenant_id: tenantId });

            return {
                allowed: false,
                cooldownMs: remainingMs,
                reason: `Reconnect storm detected: in cooldown for ${Math.round(remainingMs / 1000)}s (attempt ${cooldown.attempts})`
            };
        }

        // Check if threshold exceeded
        if (session.reconnectAttempts >= this.config.reconnectStormThreshold) {
            // Calculate exponential cooldown
            const exponent = session.reconnectAttempts - this.config.reconnectStormThreshold;
            const cooldownMs = Math.min(
                this.config.reconnectCooldownBaseMs * Math.pow(2, exponent),
                this.config.reconnectCooldownMaxMs
            );

            // Set cooldown
            this.reconnectCooldowns.set(tenantId, {
                tenantId,
                cooldownUntil: now + cooldownMs,
                attempts: session.reconnectAttempts,
                reason: 'reconnect_storm'
            });

            selfHealingActionsTotal.inc({ action_type: 'reconnect_storm_cooldown' });
            selfHealingReconnectSuppressed.inc({ tenant_id: tenantId });

            logger.warn({
                tenantId,
                sessionId: session.sessionId,
                attempts: session.reconnectAttempts,
                cooldownMs
            }, 'PILLAR 1: Reconnect storm detected, applying exponential cooldown');

            return {
                allowed: false,
                cooldownMs,
                reason: `Reconnect storm detected: ${session.reconnectAttempts} attempts, cooldown ${Math.round(cooldownMs / 1000)}s`
            };
        }

        return { allowed: true };
    }

    /**
     * Reset reconnect cooldown for a tenant (e.g., after successful connection)
     */
    resetReconnectCooldown(tenantId: string): void {
        if (this.reconnectCooldowns.has(tenantId)) {
            this.reconnectCooldowns.delete(tenantId);
            logger.debug({ tenantId }, 'Reconnect cooldown reset');
        }
    }

    /**
     * Clear auto-reset tracking for a tenant
     */
    clearAutoResetTracking(tenantId: string): void {
        this.recentAutoResets.delete(tenantId);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // DIAGNOSTICS
    // ═══════════════════════════════════════════════════════════════════════════

    /**
     * Get self-healing status
     */
    getStatus(): Record<string, unknown> {
        const now = Date.now();

        const cooldowns = Array.from(this.reconnectCooldowns.entries()).map(([tenantId, entry]) => ({
            tenantId,
            remainingMs: Math.max(0, entry.cooldownUntil - now),
            attempts: entry.attempts,
            reason: entry.reason
        }));

        const recentResets = Array.from(this.recentAutoResets.entries()).map(([tenantId, resetTime]) => ({
            tenantId,
            resetAgoMs: now - resetTime
        }));

        return {
            enabled: {
                poisonedAutoReset: this.config.poisonedAutoResetEnabled,
                stuckQrAutoReset: this.config.stuckQrAutoResetEnabled,
                reconnectStormSuppression: this.config.reconnectStormEnabled
            },
            thresholds: {
                poisonedResetDelayMs: this.config.poisonedAutoResetDelayMs,
                stuckQrThresholdMs: this.config.stuckQrThresholdMs,
                reconnectStormThreshold: this.config.reconnectStormThreshold
            },
            currentState: {
                activeCooldowns: cooldowns,
                recentAutoResets: recentResets
            }
        };
    }

    /**
     * Get configuration
     */
    getConfig(): SelfHealingConfig {
        return { ...this.config };
    }
}

// Export singleton
export const selfHealingService = new SelfHealingService();
