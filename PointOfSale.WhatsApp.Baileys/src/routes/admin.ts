/**
 * Admin Routes
 * 
 * PHASE 2: Observability, Control & Operability
 * PHASE 3: Reliability, Self-Healing & Scale Readiness
 * 
 * Implements PILLAR 3: Health & Control Endpoints
 * 
 * Endpoints:
 * - GET /admin/sessions - List all sessions with details
 * - GET /admin/sessions/:tenantId - Get detailed session info
 * - POST /admin/sessions/:tenantId/force-reset - Force reset a session
 * - GET /admin/diagnostics - Get system diagnostics
 * - GET /admin/alerts - Get current alert conditions
 * - GET /admin/self-healing - Get self-healing status (Phase 3)
 * - GET /admin/rate-limits - Get rate limit status (Phase 3)
 * - GET /admin/rate-limits/:tenantId - Get per-tenant rate limit status (Phase 3)
 * - GET /admin/admission - Get admission control status (Phase 3)
 */

import { Router, Request, Response } from 'express';
import { SessionManager } from '../services/SessionManager';
import { selfHealingService } from '../services/SelfHealingService';
import { ConnectionStatus } from '../models/Session';
import { createLogger } from '../utils/logger';

const logger = createLogger('AdminRoutes');

/**
 * PHASE 2: Alert thresholds (conceptual, for checking conditions)
 */
const ALERT_THRESHOLDS = {
    QR_TIMEOUT_RATE_PERCENT: 5,        // Alert if > 5% QR timeouts
    RECONNECT_ATTEMPTS_WARNING: 3,      // Alert if session has > 3 reconnects
    STUCK_SESSION_SECONDS: 30,          // Alert if session stuck > 30s
    ECONNRESET_SPIKE_COUNT: 10          // Alert if > 10 ECONNRESET in short window
};

export function createAdminRoutes(sessionManager: SessionManager): Router {
    const router = Router();

    /**
     * GET /admin/sessions
     * List all sessions with details
     * 
     * Response includes:
     * - All sessions with state, age, reconnectAttempts, lastError
     * - Summary statistics
     */
    router.get('/sessions', async (_req: Request, res: Response): Promise<void> => {
        try {
            logger.debug('Admin: Listing all sessions');

            const sessions = sessionManager.getAllSessionDetails();
            const diagnostics = sessionManager.getHealthDiagnostics();

            res.json({
                success: true,
                totalSessions: sessions.length,
                sessionsByStatus: diagnostics.sessionsByStatus,
                stuckSessions: diagnostics.stuckSessions,
                reconnectStormsDetected: diagnostics.reconnectStormsDetected,
                poisonedSessions: diagnostics.poisonedSessions,
                sessions: sessions.map(session => ({
                    tenantId: session.tenantId,
                    sessionId: session.sessionId,
                    socketId: session.socketId,
                    status: session.status,
                    ageSeconds: session.ageSeconds,
                    stuckSeconds: session.stuckSeconds,
                    reconnectAttempts: session.reconnectAttempts,
                    lastError: session.lastError,
                    authStateHash: session.authStateHash,
                    hasSocket: session.hasSocket,
                    connectedAt: session.connectedAt
                }))
            });
        } catch (error) {
            logger.error({ err: error }, 'Admin: Failed to list sessions');
            res.status(500).json({
                success: false,
                error: error instanceof Error ? error.message : 'Failed to list sessions'
            });
        }
    });

    /**
     * GET /admin/sessions/:tenantId
     * Get detailed information for a specific session
     */
    router.get('/sessions/:tenantId', async (req: Request, res: Response): Promise<void> => {
        const { tenantId } = req.params;

        try {
            logger.debug({ tenantId }, 'Admin: Getting session details');

            const sessionDetails = sessionManager.getSessionDetails(tenantId);

            if (!sessionDetails) {
                res.status(404).json({
                    success: false,
                    error: `Session not found for tenant: ${tenantId}`
                });
                return;
            }

            res.json({
                success: true,
                session: sessionDetails
            });
        } catch (error) {
            logger.error({ err: error, tenantId }, 'Admin: Failed to get session details');
            res.status(500).json({
                success: false,
                error: error instanceof Error ? error.message : 'Failed to get session details'
            });
        }
    });

    /**
     * POST /admin/sessions/:tenantId/force-reset
     * Force reset a session (deletes auth, kills socket, forces fresh QR)
     * 
     * This is the nuclear option for stuck/poisoned sessions.
     */
    router.post('/sessions/:tenantId/force-reset', async (req: Request, res: Response): Promise<void> => {
        const { tenantId } = req.params;

        try {
            logger.warn({ tenantId }, 'ADMIN: Force reset requested');

            // Check if session exists (optional - force reset works even if not in memory)
            const existingSession = sessionManager.getSession(tenantId);
            const previousStatus = existingSession?.status || 'not_in_memory';
            const previousSessionId = existingSession?.sessionId || null;

            // Execute force reset
            await sessionManager.forceResetSession(tenantId);

            res.json({
                success: true,
                message: 'Session force reset complete. Next /connect will require fresh QR scan.',
                previousStatus,
                previousSessionId,
                tenantId
            });

            logger.info({ tenantId, previousStatus }, 'ADMIN: Force reset completed');
        } catch (error) {
            logger.error({ err: error, tenantId }, 'Admin: Failed to force reset session');
            res.status(500).json({
                success: false,
                error: error instanceof Error ? error.message : 'Failed to force reset session'
            });
        }
    });

    /**
     * GET /admin/diagnostics
     * Get comprehensive system diagnostics
     * 
     * Response includes everything needed for operational visibility:
     * - Session state breakdown
     * - Stuck session count
     * - Reconnect storm detection
     * - Poisoned session count
     * - Active socket count
     */
    router.get('/diagnostics', async (_req: Request, res: Response): Promise<void> => {
        try {
            logger.debug('Admin: Getting diagnostics');

            const diagnostics = sessionManager.getHealthDiagnostics();
            const sessions = sessionManager.getActiveSessions();

            // Calculate additional diagnostics
            const now = Date.now();
            const sessionsWithHighReconnects: string[] = [];
            const stuckSessionsList: Array<{ tenantId: string; status: string; stuckForSeconds: number }> = [];
            const poisonedSessionsList: string[] = [];

            for (const session of sessions) {
                // Sessions with high reconnect attempts
                if (session.reconnectAttempts >= ALERT_THRESHOLDS.RECONNECT_ATTEMPTS_WARNING) {
                    sessionsWithHighReconnects.push(session.tenantId);
                }

                // Stuck sessions
                if ((session.status === ConnectionStatus.CONNECTING ||
                    session.status === ConnectionStatus.QR_PENDING) &&
                    (now - session.statusChangedAt.getTime()) > ALERT_THRESHOLDS.STUCK_SESSION_SECONDS * 1000) {
                    stuckSessionsList.push({
                        tenantId: session.tenantId,
                        status: session.status,
                        stuckForSeconds: Math.round((now - session.statusChangedAt.getTime()) / 1000)
                    });
                }

                // Poisoned sessions
                if (session.status === ConnectionStatus.POISONED ||
                    session.status === ConnectionStatus.LOGGED_OUT) {
                    poisonedSessionsList.push(session.tenantId);
                }
            }

            res.json({
                success: true,
                timestamp: new Date().toISOString(),
                summary: diagnostics,
                alerts: {
                    sessionsWithHighReconnects,
                    stuckSessions: stuckSessionsList,
                    poisonedSessions: poisonedSessionsList
                },
                thresholds: ALERT_THRESHOLDS
            });
        } catch (error) {
            logger.error({ err: error }, 'Admin: Failed to get diagnostics');
            res.status(500).json({
                success: false,
                error: error instanceof Error ? error.message : 'Failed to get diagnostics'
            });
        }
    });

    /**
     * GET /admin/alerts
     * Get current alert conditions
     * 
     * Returns a simplified view of potential problems for quick assessment.
     */
    router.get('/alerts', async (_req: Request, res: Response): Promise<void> => {
        try {
            logger.debug('Admin: Checking alerts');

            const diagnostics = sessionManager.getHealthDiagnostics() as Record<string, unknown>;
            const sessions = sessionManager.getActiveSessions();
            const now = Date.now();

            // Build alert list
            const alerts: Array<{
                severity: 'warning' | 'critical';
                type: string;
                message: string;
                affectedSessions?: string[];
                count?: number;
            }> = [];

            // Check for stuck sessions
            const stuckSessions: string[] = [];
            for (const session of sessions) {
                if ((session.status === ConnectionStatus.CONNECTING ||
                    session.status === ConnectionStatus.QR_PENDING) &&
                    (now - session.statusChangedAt.getTime()) > ALERT_THRESHOLDS.STUCK_SESSION_SECONDS * 1000) {
                    stuckSessions.push(session.tenantId);
                }
            }
            if (stuckSessions.length > 0) {
                alerts.push({
                    severity: 'warning',
                    type: 'STUCK_SESSIONS',
                    message: `${stuckSessions.length} session(s) stuck in CONNECTING/QR_PENDING for > ${ALERT_THRESHOLDS.STUCK_SESSION_SECONDS}s`,
                    affectedSessions: stuckSessions,
                    count: stuckSessions.length
                });
            }

            // Check for reconnect storms
            const reconnectStormSessions: string[] = [];
            for (const session of sessions) {
                if (session.reconnectAttempts >= ALERT_THRESHOLDS.RECONNECT_ATTEMPTS_WARNING) {
                    reconnectStormSessions.push(session.tenantId);
                }
            }
            if (reconnectStormSessions.length > 0) {
                alerts.push({
                    severity: reconnectStormSessions.length > 3 ? 'critical' : 'warning',
                    type: 'RECONNECT_STORM',
                    message: `${reconnectStormSessions.length} session(s) with >= ${ALERT_THRESHOLDS.RECONNECT_ATTEMPTS_WARNING} reconnect attempts`,
                    affectedSessions: reconnectStormSessions,
                    count: reconnectStormSessions.length
                });
            }

            // Check for poisoned/logged_out sessions
            const poisonedSessions = (diagnostics.poisonedSessions as number) || 0;
            if (poisonedSessions > 0) {
                const affectedTenants = sessions
                    .filter(s => s.status === ConnectionStatus.POISONED || s.status === ConnectionStatus.LOGGED_OUT)
                    .map(s => s.tenantId);
                alerts.push({
                    severity: poisonedSessions > 5 ? 'critical' : 'warning',
                    type: 'POISONED_AUTH',
                    message: `${poisonedSessions} session(s) in POISONED or LOGGED_OUT state`,
                    affectedSessions: affectedTenants,
                    count: poisonedSessions
                });
            }

            // Determine overall status
            const hasErrors = alerts.some(a => a.severity === 'critical');
            const hasWarnings = alerts.some(a => a.severity === 'warning');
            let overallStatus: 'healthy' | 'warning' | 'critical' = 'healthy';
            if (hasErrors) {
                overallStatus = 'critical';
            } else if (hasWarnings) {
                overallStatus = 'warning';
            }

            res.json({
                success: true,
                status: overallStatus,
                alertCount: alerts.length,
                alerts,
                checkedAt: new Date().toISOString()
            });
        } catch (error) {
            logger.error({ err: error }, 'Admin: Failed to check alerts');
            res.status(500).json({
                success: false,
                error: error instanceof Error ? error.message : 'Failed to check alerts'
            });
        }
    });

    /**
     * GET /admin/self-healing
     * Get self-healing service status (Phase 3)
     */
    router.get('/self-healing', async (_req: Request, res: Response): Promise<void> => {
        try {
            logger.debug('Admin: Getting self-healing status');

            const selfHealingStatus = selfHealingService.getStatus();

            res.json({
                success: true,
                selfHealing: selfHealingStatus
            });
        } catch (error) {
            logger.error({ err: error }, 'Admin: Failed to get self-healing status');
            res.status(500).json({
                success: false,
                error: error instanceof Error ? error.message : 'Failed to get self-healing status'
            });
        }
    });

    /**
     * GET /admin/rate-limits
     * Get global rate limit status (Phase 3)
     */
    router.get('/rate-limits', async (_req: Request, res: Response): Promise<void> => {
        try {
            logger.debug('Admin: Getting rate limit status');

            const rateLimiter = sessionManager.getRateLimiter();
            if (!rateLimiter) {
                res.json({
                    success: true,
                    rateLimiter: null,
                    message: 'Rate limiter not yet initialized'
                });
                return;
            }

            const config = rateLimiter.getConfig();

            res.json({
                success: true,
                rateLimiter: {
                    config,
                    // Global counters would require a separate aggregation from Redis
                }
            });
        } catch (error) {
            logger.error({ err: error }, 'Admin: Failed to get rate limit status');
            res.status(500).json({
                success: false,
                error: error instanceof Error ? error.message : 'Failed to get rate limit status'
            });
        }
    });

    /**
     * GET /admin/rate-limits/:tenantId
     * Get per-tenant rate limit status (Phase 3)
     */
    router.get('/rate-limits/:tenantId', async (req: Request, res: Response): Promise<void> => {
        const { tenantId } = req.params;

        try {
            logger.debug({ tenantId }, 'Admin: Getting tenant rate limit status');

            const rateLimiter = sessionManager.getRateLimiter();
            if (!rateLimiter) {
                res.json({
                    success: true,
                    rateLimiter: null,
                    message: 'Rate limiter not yet initialized'
                });
                return;
            }

            const status = await rateLimiter.getRateLimitStatus(tenantId);

            res.json({
                success: true,
                tenant: status
            });
        } catch (error) {
            logger.error({ err: error, tenantId }, 'Admin: Failed to get tenant rate limit status');
            res.status(500).json({
                success: false,
                error: error instanceof Error ? error.message : 'Failed to get tenant rate limit status'
            });
        }
    });

    /**
     * DELETE /admin/rate-limits/:tenantId
     * Reset rate limits for a tenant (Phase 3 - Admin operation)
     */
    router.delete('/rate-limits/:tenantId', async (req: Request, res: Response): Promise<void> => {
        const { tenantId } = req.params;

        try {
            logger.warn({ tenantId }, 'ADMIN: Resetting rate limits for tenant');

            const rateLimiter = sessionManager.getRateLimiter();
            if (!rateLimiter) {
                res.status(400).json({
                    success: false,
                    error: 'Rate limiter not yet initialized'
                });
                return;
            }

            await rateLimiter.resetTenantLimits(tenantId);

            res.json({
                success: true,
                message: `Rate limits reset for tenant: ${tenantId}`
            });
        } catch (error) {
            logger.error({ err: error, tenantId }, 'Admin: Failed to reset tenant rate limits');
            res.status(500).json({
                success: false,
                error: error instanceof Error ? error.message : 'Failed to reset tenant rate limits'
            });
        }
    });

    /**
     * GET /admin/admission
     * Get admission control status (Phase 3)
     */
    router.get('/admission', async (_req: Request, res: Response): Promise<void> => {
        try {
            logger.debug('Admin: Getting admission control status');

            const admissionController = sessionManager.getAdmissionController();
            const status = admissionController.getAdmissionStatus();

            res.json({
                success: true,
                admission: status
            });
        } catch (error) {
            logger.error({ err: error }, 'Admin: Failed to get admission control status');
            res.status(500).json({
                success: false,
                error: error instanceof Error ? error.message : 'Failed to get admission control status'
            });
        }
    });

    return router;
}
