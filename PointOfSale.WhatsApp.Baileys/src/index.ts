import express from 'express';
import { config, validateConfig } from './config';
import { logger } from './utils/logger';
import { SessionManager } from './services/SessionManager';
import { MessageSender } from './services/MessageSender';
import { PoolManager } from './services/PoolManager';
import { metricsService } from './services/MetricsService';
import { createSessionRoutes } from './routes/sessions';
import { createMessageRoutes } from './routes/messages';
import { createAdminRoutes } from './routes/admin';
import { apiKeyAuth } from './middleware/auth';

// Global service instances
let sessionManager: SessionManager;
let messageSender: MessageSender;
let poolManager: PoolManager;

/**
 * PHASE 1: Foundation Stabilization
 * PHASE 2: Observability, Control & Operability
 * 
 * Initialize and start the Baileys WhatsApp service
 */
async function main() {
  try {
    // Validate configuration
    validateConfig();
    logger.info('Configuration validated successfully');

    // Initialize session manager
    sessionManager = new SessionManager();
    await sessionManager.initialize();
    logger.info('SessionManager initialized');

    // Initialize pool manager
    poolManager = new PoolManager(sessionManager.getRedisClient());
    await poolManager.initialize();
    logger.info('PoolManager initialized');

    // Initialize message sender
    messageSender = new MessageSender(sessionManager, poolManager);
    logger.info('MessageSender initialized');

    // Create Express app
    const app = express();

    // Middleware
    app.use(express.json());

    // Request logging middleware
    app.use((req, _res, next) => {
      logger.info({
        method: req.method,
        path: req.path,
        ip: req.ip
      }, 'Incoming request');
      next();
    });

    // PHASE 2: Prometheus metrics endpoint (before auth for scraping)
    app.get('/metrics', async (_req, res) => {
      try {
        const metrics = await metricsService.getMetrics();
        res.set('Content-Type', metricsService.getContentType());
        res.send(metrics);
      } catch (error) {
        logger.error({ err: error }, 'Failed to get metrics');
        res.status(500).send('Failed to collect metrics');
      }
    });

    // SECURITY: API Key authentication for all non-public endpoints
    // This MUST be before all routes (except health and metrics which are exempt)
    app.use(apiKeyAuth);

    // Health check endpoint
    // PHASE 1: Extended with session state breakdown for diagnostics
    // PHASE 2: Enhanced with diagnostics and version info
    // PHASE 3: Self-healing and rate limiting status
    app.get('/health', (_req, res) => {
      const poolStats = poolManager.getPoolStatistics();
      const diagnostics = sessionManager.getHealthDiagnostics();
      const redisReady = sessionManager.getRedisClient().isReady;

      res.status(redisReady ? 200 : 503).json({
        status: redisReady ? 'healthy' : 'unhealthy',
        version: 'PHASE_3_RELIABLE', // Version marker for tracking
        redis: redisReady ? 'ready' : 'unavailable',
        activeSessions: sessionManager.getSessionCount(),
        sessionsByStatus: diagnostics.sessionsByStatus,
        poolSessions: poolStats.totalConnections,
        poolMembers: poolStats.activeMembers,
        uptime: process.uptime(),
        // PHASE 2: Additional diagnostics
        stuckSessions: diagnostics.stuckSessions,
        reconnectStormsDetected: diagnostics.reconnectStormsDetected,
        poisonedSessions: diagnostics.poisonedSessions,
        activeSockets: diagnostics.activeSockets
      });
    });

    // PHASE 2: Readiness probe (for Kubernetes/container orchestration)
    app.get('/ready', async (_req, res) => {
      try {
        const redis = sessionManager.getRedisClient();
        if (!redis.isReady) {
          res.status(503).json({ ready: false, reason: 'Redis client is not ready' });
          return;
        }

        const ping = await Promise.race([
          redis.ping(),
          new Promise<string>((_, reject) =>
            setTimeout(() => reject(new Error('Redis readiness check timed out')), 2000))
        ]);
        if (ping !== 'PONG') {
          res.status(503).json({ ready: false, reason: 'Redis not responding' });
          return;
        }
        res.json({ ready: true });
      } catch (error) {
        res.status(503).json({
          ready: false,
          reason: error instanceof Error ? error.message : 'Unknown error'
        });
      }
    });

    // PHASE 2: Liveness probe
    app.get('/live', (_req, res) => {
      res.json({ live: true, uptime: process.uptime() });
    });

    // Session routes
    app.use('/sessions', createSessionRoutes(sessionManager));

    // Message routes
    app.use('/messages', createMessageRoutes(messageSender));

    // PHASE 2: Admin routes
    app.use('/admin', createAdminRoutes(sessionManager));

    // Error handling middleware
    app.use((err: Error, req: express.Request, res: express.Response, _next: express.NextFunction) => {
      logger.error({ err, path: req.path }, 'Request error');
      res.status(500).json({
        success: false,
        error: 'Internal server error'
      });
    });

    // Start server
    app.listen(config.port, () => {
      logger.info(`Baileys WhatsApp service listening on port ${config.port}`);
      logger.info(`Environment: ${config.nodeEnv}`);
      logger.info('Phase: PHASE_3_RELIABLE');
      logger.info('Endpoints:');
      logger.info('  - GET  /health     (Health check)');
      logger.info('  - GET  /ready      (Readiness probe)');
      logger.info('  - GET  /live       (Liveness probe)');
      logger.info('  - GET  /metrics    (Prometheus metrics)');
      logger.info('  - POST /sessions/:tenantId/connect');
      logger.info('  - GET  /sessions/:tenantId/qr');
      logger.info('  - GET  /sessions/:tenantId/status');
      logger.info('  - POST /sessions/:tenantId/disconnect');
      logger.info('  - POST /messages/send');
      logger.info('  - GET  /admin/sessions');
      logger.info('  - GET  /admin/sessions/:tenantId');
      logger.info('  - POST /admin/sessions/:tenantId/force-reset');
      logger.info('  - GET  /admin/diagnostics');
      logger.info('  - GET  /admin/alerts');
    });

    // Graceful shutdown
    const shutdown = async () => {
      logger.info('Shutdown signal received, cleaning up...');

      try {
        await sessionManager.shutdown();
        logger.info('SessionManager shutdown complete');
        process.exit(0);
      } catch (error) {
        logger.error({ err: error }, 'Error during shutdown');
        process.exit(1);
      }
    };

    process.on('SIGTERM', shutdown);
    process.on('SIGINT', shutdown);

    // Global error handlers to prevent crashes from socket errors (ECONNRESET, etc.)
    process.on('uncaughtException', (error: Error) => {
      // Log but don't crash for connection reset errors - these are expected
      if (error.message?.includes('ECONNRESET') || error.message?.includes('EPIPE')) {
        logger.warn({ err: error }, 'Socket connection error (non-fatal, continuing)');
      } else {
        logger.error({ err: error }, 'Uncaught exception (non-fatal, continuing)');
      }
    });

    process.on('unhandledRejection', (reason: unknown, promise: Promise<unknown>) => {
      logger.error({ reason, promise }, 'Unhandled promise rejection (non-fatal, continuing)');
    });

  } catch (error) {
    logger.error({ err: error }, 'Failed to start service');
    process.exit(1);
  }
}

// Start the service
main();
