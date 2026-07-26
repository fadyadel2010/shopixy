/**
 * Session Management Routes
 * 
 * PHASE 1: Foundation Stabilization
 * PHASE 2: Observability, Control & Operability
 * PHASE 3: Reliability, Self-Healing & Scale Readiness
 * 
 * Handles HTTP endpoints for WhatsApp session management:
 * - POST /sessions/:tenantId/connect - Create/retrieve session
 * - GET /sessions/:tenantId/qr - Get QR code
 * - GET /sessions/:tenantId/status - Get connection status
 * - POST /sessions/:tenantId/disconnect - Disconnect session
 * 
 * PHASE 1 Changes:
 * - Handle POISONED and LOGGED_OUT states properly
 * - Provide clear status information to clients
 * - Add reconnect tracking in status response
 * 
 * PHASE 2 Changes:
 * - QR generation metrics (attempt, success, timeout)
 * - Correlation IDs in responses
 * - Extended status with sessionId, socketId
 * 
 * PHASE 3 Changes:
 * - QR generation rate limiting
 * - Session admission control
 * - Connect rate limiting
 */

import { Router, Request, Response } from 'express';
import { SessionManager } from '../services/SessionManager';
import { metricsService } from '../services/MetricsService';
import { ConnectionStatus, SessionMode } from '../models/Session';
import { QrCodeResponse, StatusResponse, DisconnectResponse } from '../models/Message';
import { createLogger } from '../utils/logger';

const logger = createLogger('SessionRoutes');

export function createSessionRoutes(sessionManager: SessionManager): Router {
  const router = Router();

  /**
   * POST /sessions/:tenantId/connect
   * Create or retrieve a session for a tenant
   * 
   * PHASE 1: Handles POISONED/LOGGED_OUT states by triggering cleanup
   * PHASE 2: Returns correlation IDs for tracing
   */
  router.post('/:tenantId/connect', async (req: Request, res: Response): Promise<void> => {
    const { tenantId } = req.params;
    const { mode } = req.body;

    try {
      logger.info({ tenantId, mode }, 'Connect session request');

      // Validate tenant ID
      if (!tenantId || tenantId.trim() === '') {
        res.status(400).json({
          success: false,
          error: 'Tenant ID is required'
        });
        return;
      }

      // Validate mode
      const sessionMode = mode === 'POOL' ? SessionMode.POOL : SessionMode.DEDICATED;

      // Get or create session
      const session = await sessionManager.getOrCreateSession(tenantId, sessionMode);

      // Connect session if not already connected
      // PHASE 1: connectSession now handles POISONED/LOGGED_OUT states internally
      // PHASE 3: connectSession now checks admission control
      if (session.status !== ConnectionStatus.CONNECTED) {
        // PHASE 3: Check connect rate limit
        const rateLimiter = sessionManager.getRateLimiter();
        if (rateLimiter) {
          const rateLimitResult = await rateLimiter.checkConnectRateLimit(tenantId);
          if (!rateLimitResult.allowed) {
            logger.warn({
              tenantId,
              reason: rateLimitResult.reason
            }, 'PHASE 3: Connect rate limited');
            res.status(429).json({
              success: false,
              error: rateLimitResult.reason || 'Connect rate limit exceeded',
              retryAfterMs: rateLimitResult.retryAfterMs
            });
            return;
          }
        }

        await sessionManager.connectSession(tenantId);
      }

      // Re-fetch session to get updated status after connection attempt
      const updatedSession = sessionManager.getSession(tenantId);

      res.json({
        success: true,
        session: {
          tenantId: updatedSession?.tenantId ?? session.tenantId,
          mode: updatedSession?.mode ?? session.mode,
          status: updatedSession?.status ?? session.status,
          createdAt: updatedSession?.createdAt ?? session.createdAt,
          lastUsedAt: updatedSession?.lastUsedAt ?? session.lastUsedAt,
          // PHASE 1: Include reconnect info
          reconnectAttempts: updatedSession?.reconnectAttempts ?? 0,
          socketVersion: updatedSession?.socketVersion ?? 0,
          // PHASE 2: Include correlation IDs
          sessionId: updatedSession?.sessionId ?? session.sessionId,
          socketId: updatedSession?.socketId ?? null,
          authStateHash: updatedSession?.authStateHash ?? null
        }
      });
    } catch (error) {
      logger.error({ err: error, tenantId }, 'Failed to connect session');
      res.status(500).json({
        success: false,
        error: error instanceof Error ? error.message : 'Failed to connect session'
      });
    }
  });

  /**
   * GET /sessions/:tenantId/qr
   * Get QR code for session authentication
   * 
   * PHASE 1: Handles terminal states (POISONED/LOGGED_OUT) by triggering fresh QR
   * PHASE 2: Records QR metrics (generation, success, timeout)
   */
  router.get('/:tenantId/qr', async (req: Request, res: Response): Promise<void> => {
    const { tenantId } = req.params;
    const qrStartTime = Date.now();

    try {
      logger.info({ tenantId }, 'QR code request');

      // Validate tenant ID
      if (!tenantId || tenantId.trim() === '') {
        res.status(400).json({
          success: false,
          error: 'Tenant ID is required'
        } as QrCodeResponse);
        return;
      }

      // Get session
      let session = sessionManager.getSession(tenantId);
      if (!session) {
        res.status(404).json({
          success: false,
          error: `Session not found for tenant ${tenantId}. Please call /connect first.`
        } as QrCodeResponse);
        return;
      }

      // Check if session mode is POOL (pool sessions can't generate QR codes)
      if (session.mode === SessionMode.POOL) {
        res.status(400).json({
          success: false,
          error: 'QR code generation not available for pool sessions'
        } as QrCodeResponse);
        return;
      }

      // ═══════════════════════════════════════════════════════════════════════
      // PHASE 3 PILLAR 3: QR ADMISSION CONTROL
      // ═══════════════════════════════════════════════════════════════════════
      const qrAdmissionResult = sessionManager.checkQrPendingAdmission(tenantId);
      if (!qrAdmissionResult.admitted) {
        logger.warn({
          tenantId,
          reason: qrAdmissionResult.reason
        }, 'PHASE 3: QR pending admission denied');
        res.status(503).json({
          success: false,
          error: qrAdmissionResult.reason || 'System at capacity - too many pending QR scans'
        } as QrCodeResponse);
        return;
      }

      // ═══════════════════════════════════════════════════════════════════════
      // PHASE 3 PILLAR 2: QR RATE LIMITING
      // ═══════════════════════════════════════════════════════════════════════
      const rateLimiter = sessionManager.getRateLimiter();
      if (rateLimiter) {
        const qrRateLimitResult = await rateLimiter.checkQrRateLimit(tenantId);
        if (!qrRateLimitResult.allowed) {
          logger.warn({
            tenantId,
            reason: qrRateLimitResult.reason
          }, 'PHASE 3: QR generation rate limited');
          res.status(429).json({
            success: false,
            error: qrRateLimitResult.reason || 'QR generation rate limit exceeded',
            retryAfterMs: qrRateLimitResult.retryAfterMs
          });
          return;
        }
      }

      // PHASE 1: Check for terminal states that need fresh start
      if (session.status === ConnectionStatus.POISONED ||
        session.status === ConnectionStatus.LOGGED_OUT) {
        logger.info({
          tenantId,
          sessionId: session.sessionId,
          status: session.status
        }, 'Session in terminal state, initiating fresh connection');
        await sessionManager.connectSession(tenantId);
      }

      // If session is already connected, no QR needed
      if (session.status === ConnectionStatus.CONNECTED) {
        res.status(200).json({
          success: true,
          qrCode: null,
          alreadyConnected: true,
          expiresIn: 0,
          sessionId: session.sessionId
        });
        return;
      }

      // If session is disconnected, start the connection
      if (session.status === ConnectionStatus.DISCONNECTED) {
        logger.info({ tenantId, sessionId: session.sessionId }, 'Session disconnected, initiating connection');
        await sessionManager.connectSession(tenantId);
      }

      // Poll for QR code with timeout (QR generation is async via event)
      const maxWaitMs = 10000; // PHASE 1: Increased to 10 seconds to account for auth cleanup
      const pollIntervalMs = 500; // Check every 500ms
      const startTime = Date.now();

      while (Date.now() - startTime < maxWaitMs) {
        // Wait for poll interval
        await new Promise(resolve => setTimeout(resolve, pollIntervalMs));

        // Re-fetch session to get updated QR code
        session = sessionManager.getSession(tenantId);

        // PHASE 1: Check if session was removed (stale cleanup)
        if (!session) {
          logger.warn({ tenantId }, 'Session was removed during QR wait (stale cleanup)');
          res.status(409).json({
            success: false,
            error: 'Session was cleaned up. Please call /connect first to create a new session.',
            retryAfterMs: 1000
          });
          return;
        }

        if (session.qrCode) {
          // PHASE 2: Record QR success metric
          const durationSeconds = (Date.now() - qrStartTime) / 1000;
          metricsService.recordQrSuccess(tenantId, durationSeconds);

          logger.info({
            tenantId,
            sessionId: session.sessionId,
            socketVersion: session.socketVersion,
            durationSeconds
          }, 'QR code generated and ready');

          res.json({
            success: true,
            qrCode: session.qrCode,
            expiresIn: 60, // QR codes typically expire in 60 seconds
            socketVersion: session.socketVersion,
            sessionId: session.sessionId
          } as QrCodeResponse);
          return;
        }

        // If session became connected during wait, no QR needed
        if (session.status === ConnectionStatus.CONNECTED) {
          logger.info({ tenantId, sessionId: session.sessionId }, 'Session connected during QR wait');
          res.status(200).json({
            success: true,
            qrCode: null,
            alreadyConnected: true,
            expiresIn: 0,
            sessionId: session.sessionId
          });
          return;
        }

        // PHASE 1: If session entered terminal state during wait, inform client
        if (session.status === ConnectionStatus.POISONED ||
          session.status === ConnectionStatus.LOGGED_OUT) {
          logger.warn({
            tenantId,
            sessionId: session.sessionId,
            status: session.status
          }, 'Session entered terminal state during QR wait');

          // PHASE 2: Record QR failure
          const durationSeconds = (Date.now() - qrStartTime) / 1000;
          metricsService.recordQrFailure(tenantId, durationSeconds);

          res.status(409).json({
            success: false,
            error: `Session entered ${session.status} state. Please call /connect again.`,
            retryAfterMs: 1000,
            sessionId: session.sessionId
          });
          return;
        }

        logger.debug({
          tenantId,
          sessionId: session.sessionId,
          elapsedMs: Date.now() - startTime,
          status: session.status
        }, 'Waiting for QR code...');
      }

      // Timeout - QR code not available yet
      // PHASE 1: Check final session state for better error message
      session = sessionManager.getSession(tenantId);
      const finalStatus = session?.status || 'unknown';

      // PHASE 2: Record QR timeout metric
      const durationSeconds = (Date.now() - qrStartTime) / 1000;
      metricsService.recordQrTimeout(tenantId, durationSeconds);

      logger.warn({
        tenantId,
        sessionId: session?.sessionId,
        waitedMs: Date.now() - startTime,
        finalStatus
      }, 'QR code generation timeout');

      // AUDIT FIX: Changed from 202 to 408 (Request Timeout) for clearer semantics
      res.status(408).json({
        success: false,
        error: `QR code generation timeout (status: ${finalStatus}). Please try again.`,
        retryAfterMs: 2000,
        currentStatus: finalStatus,
        sessionId: session?.sessionId
      });
    } catch (error) {
      // PHASE 2: Record QR failure metric
      const durationSeconds = (Date.now() - qrStartTime) / 1000;
      metricsService.recordQrFailure(tenantId, durationSeconds);

      logger.error({ err: error, tenantId }, 'Failed to get QR code');
      res.status(500).json({
        success: false,
        error: error instanceof Error ? error.message : 'Failed to get QR code'
      } as QrCodeResponse);
    }
  });

  /**
   * GET /sessions/:tenantId/status
   * Get connection status for a session
   * 
   * PHASE 1: Returns extended status information including reconnect tracking
   * PHASE 2: Returns correlation IDs for tracing
   */
  router.get('/:tenantId/status', async (req: Request, res: Response): Promise<void> => {
    const { tenantId } = req.params;

    try {
      logger.debug({ tenantId }, 'Status request');

      // Validate tenant ID
      if (!tenantId || tenantId.trim() === '') {
        res.status(400).json({
          success: false,
          error: 'Tenant ID is required'
        } as StatusResponse);
        return;
      }

      // Get session
      const session = sessionManager.getSession(tenantId);
      if (!session) {
        res.status(404).json({
          success: false,
          error: `Session not found for tenant ${tenantId}`
        } as StatusResponse);
        return;
      }

      // PHASE 1 + PHASE 2: Extended status response with correlation IDs
      res.json({
        success: true,
        status: session.status,
        lastUsedAt: session.lastUsedAt.toISOString(),
        // PHASE 1: Additional diagnostic info
        statusChangedAt: session.statusChangedAt.toISOString(),
        reconnectAttempts: session.reconnectAttempts,
        socketVersion: session.socketVersion,
        hasSocket: session.socket !== null,
        hasQrCode: session.qrCode !== null,
        // PHASE 2: Correlation IDs
        sessionId: session.sessionId,
        socketId: session.socketId,
        authStateHash: session.authStateHash,
        lastError: session.lastError,
        connectedAt: session.connectedAt?.toISOString() ?? null,
        connectedPhone: session.connectedPhone
      });
    } catch (error) {
      logger.error({ err: error, tenantId }, 'Failed to get status');
      res.status(500).json({
        success: false,
        error: error instanceof Error ? error.message : 'Failed to get status'
      } as StatusResponse);
    }
  });

  /**
   * POST /sessions/:tenantId/disconnect
   * Disconnect a session
   */
  router.post('/:tenantId/disconnect', async (req: Request, res: Response): Promise<void> => {
    const { tenantId } = req.params;

    try {
      const session = sessionManager.getSession(tenantId);
      logger.info({
        tenantId,
        sessionId: session?.sessionId
      }, 'Disconnect session request');

      // Validate tenant ID
      if (!tenantId || tenantId.trim() === '') {
        res.status(400).json({
          success: false,
          error: 'Tenant ID is required'
        } as DisconnectResponse);
        return;
      }

      // Close session
      await sessionManager.closeSession(tenantId);

      res.json({
        success: true
      } as DisconnectResponse);
    } catch (error) {
      logger.error({ err: error, tenantId }, 'Failed to disconnect session');
      res.status(500).json({
        success: false,
        error: error instanceof Error ? error.message : 'Failed to disconnect session'
      } as DisconnectResponse);
    }
  });

  return router;
}
