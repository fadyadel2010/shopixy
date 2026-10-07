import { createClient, RedisClientType } from 'redis';
import makeWASocket, {
  DisconnectReason,
  useMultiFileAuthState,
  ConnectionState,
  Browsers
} from '@whiskeysockets/baileys';
import QRCode from 'qrcode';
import { Boom } from '@hapi/boom';
import * as crypto from 'crypto';
import { WhatsAppSession, SessionMode, ConnectionStatus, SessionMetadata } from '../models/Session';
import { config } from '../config';
import { createLogger } from '../utils/logger';
import { AuthStatePersistence, AuthStateData } from './AuthStatePersistence';
import { metricsService } from './MetricsService';
import { AdmissionController } from './AdmissionController';
import { selfHealingService } from './SelfHealingService';
import { RateLimiter } from './RateLimiter';
import * as fs from 'fs';

const logger = createLogger('SessionManager');

/**
 * PHASE 1: Session Manager with Foundation Stabilization
 * PHASE 2: Observability, Control & Operability
 * PHASE 3: Reliability, Self-Healing & Scale Readiness
 * 
 * Phase 1 Pillars:
 * 1. Single Source of Truth (Redis-only auth)
 * 2. Auth Poisoning & Error Classification
 * 3. Reconnect Control (no infinite loops)
 * 4. Socket Ownership & Send Safety
 * 5. Stale Session & QR Cleanup
 * 
 * Phase 2 Pillars:
 * 1. Prometheus Metrics
 * 2. Correlation & Traceability (sessionId, socketId, authStateHash)
 * 3. Health & Control Endpoints (data for)
 * 4. Operational Safety (data for alerts)
 * 
 * Phase 3 Pillars:
 * 1. Self-Healing Actions (auto-reset poisoned, stuck QR)
 * 2. Rate & Load Safety (integrated with RateLimiter)
 * 3. Session Admission Control (limit CONNECTING/QR_PENDING)
 * 4. Failure Domain Isolation (per-tenant limits)
 */
export class SessionManager {
  private sessions: Map<string, WhatsAppSession>;
  private redis: RedisClientType;
  private cleanupInterval: NodeJS.Timeout | null;
  private staleSessionInterval: NodeJS.Timeout | null;
  private metricsInterval: NodeJS.Timeout | null;
  private authPersistence: AuthStatePersistence;

  // PHASE 1: Track in-progress connections to prevent race conditions
  private connectingLocks: Map<string, Promise<void>> = new Map();

  // PHASE 1: Jitter constants for staggered reconnection (prevents WhatsApp ban)
  private readonly MIN_RECONNECT_DELAY_MS = 5000;  // 5 seconds minimum
  private readonly MAX_RECONNECT_JITTER_MS = 30000; // 30 seconds maximum jitter

  // ═══════════════════════════════════════════════════════════════════════════
  // PILLAR 2: AUTH POISONING & ERROR CLASSIFICATION
  // ═══════════════════════════════════════════════════════════════════════════
  private readonly AUTH_INVALIDATING_STATUS_CODES = [401, 403, 405, 410, 411, 428];

  // ═══════════════════════════════════════════════════════════════════════════
  // PILLAR 3: RECONNECT CONTROL
  // ═══════════════════════════════════════════════════════════════════════════
  private readonly MAX_RECONNECT_ATTEMPTS = 5;
  private readonly RECONNECT_RESET_MS = 60 * 1000; // Reset counter after 60 seconds

  // ═══════════════════════════════════════════════════════════════════════════
  // PILLAR 5: STALE SESSION CLEANUP
  // ═══════════════════════════════════════════════════════════════════════════
  private readonly STALE_SESSION_TIMEOUT_MS = 30 * 1000; // 30 seconds
  private readonly STALE_SESSION_CHECK_INTERVAL_MS = 10 * 1000; // Check every 10 seconds

  // ═══════════════════════════════════════════════════════════════════════════
  // PHASE 2: METRICS UPDATE INTERVAL
  // ═══════════════════════════════════════════════════════════════════════════
  private readonly METRICS_UPDATE_INTERVAL_MS = 5 * 1000; // Update metrics every 5 seconds

  // ═══════════════════════════════════════════════════════════════════════════
  // PHASE 3: ADMISSION CONTROLLER & RATE LIMITER
  // ═══════════════════════════════════════════════════════════════════════════
  private admissionController: AdmissionController;
  private rateLimiter: RateLimiter | null = null;

  constructor() {
    this.sessions = new Map();
    this.redis = createClient({
      socket: {
        host: config.redis.host,
        port: config.redis.port,
        connectTimeout: 5000
      },
      password: config.redis.password,
      database: config.redis.db,
      disableOfflineQueue: true
    });
    this.redis.on('error', (error) => {
      logger.error({ err: error }, 'Redis client error');
    });
    this.cleanupInterval = null;
    this.staleSessionInterval = null;
    this.metricsInterval = null;
    this.authPersistence = new AuthStatePersistence(
      this.redis,
      config.security.authStateEncryptionSecret,
      config.security.saltPrefix
    );
    // PHASE 3: Initialize admission controller
    this.admissionController = new AdmissionController();
  }

  /**
   * Initialize the session manager
   */
  async initialize(): Promise<void> {
    try {
      // Connect to Redis
      await this.redis.connect();
      logger.info('Connected to Redis');

      // PHASE 3: Initialize rate limiter after Redis is connected
      this.rateLimiter = new RateLimiter(this.redis);
      logger.info('RateLimiter initialized');

      // Start cleanup job (idle sessions)
      this.startCleanupJob();
      logger.info('Session cleanup job started');

      // PHASE 1: Start stale session cleanup job
      this.startStaleSessionCleanupJob();
      logger.info('Stale session cleanup job started');

      // PHASE 2: Start metrics update job
      this.startMetricsUpdateJob();
      logger.info('Metrics update job started');

      // PHASE 3: Start self-healing service
      selfHealingService.start(
        (tenantId: string) => this.forceResetSession(tenantId),
        () => this.getActiveSessions()
      );
      logger.info('SelfHealingService started');

    } catch (error) {
      logger.error({ err: error }, 'Failed to initialize SessionManager');
      throw error;
    }
  }

  /**
   * Shutdown the session manager
   */
  async shutdown(): Promise<void> {
    try {
      // PHASE 3: Stop self-healing service
      selfHealingService.stop();

      // Stop cleanup jobs
      if (this.cleanupInterval) {
        clearInterval(this.cleanupInterval);
        this.cleanupInterval = null;
      }
      if (this.staleSessionInterval) {
        clearInterval(this.staleSessionInterval);
        this.staleSessionInterval = null;
      }
      if (this.metricsInterval) {
        clearInterval(this.metricsInterval);
        this.metricsInterval = null;
      }

      // Close all sessions
      for (const [tenantId, _session] of this.sessions.entries()) {
        await this.closeSession(tenantId);
      }

      // Disconnect from Redis
      await this.redis.quit();
      logger.info('SessionManager shutdown complete');

    } catch (error) {
      logger.error({ err: error }, 'Error during SessionManager shutdown');
      throw error;
    }
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // PHASE 2: CORRELATION ID HELPERS
  // ═══════════════════════════════════════════════════════════════════════════

  /**
   * Generate a new session ID (UUID)
   */
  private generateSessionId(): string {
    return crypto.randomUUID();
  }

  /**
   * Generate a socket ID (shorter, human-readable)
   */
  private generateSocketId(): string {
    return `sock_${Date.now().toString(36)}_${Math.random().toString(36).substr(2, 6)}`;
  }

  /**
   * Generate a non-reversible hash of auth state (for tracking without exposing secrets)
   */
  private generateAuthStateHash(authState: AuthStateData | null): string | null {
    if (!authState || !authState.creds) {
      return null;
    }
    try {
      const dataToHash = JSON.stringify(authState.creds);
      const hash = crypto.createHash('sha256').update(dataToHash).digest('hex');
      return hash.substring(0, 8); // First 8 chars for brevity
    } catch {
      return null;
    }
  }

  /**
   * Create a child logger with correlation IDs
   */
  private createSessionLogger(session: WhatsAppSession): ReturnType<typeof createLogger> {
    return createLogger('SessionManager').child({
      tenantId: session.tenantId,
      sessionId: session.sessionId,
      socketId: session.socketId,
      socketVersion: session.socketVersion
    });
  }

  /**
   * Create or retrieve a session for a tenant
   * 
   * PHASE 2: Sessions now include correlation IDs
   */
  async getOrCreateSession(tenantId: string, mode: SessionMode): Promise<WhatsAppSession> {
    // Check if session already exists in memory
    let session = this.sessions.get(tenantId);

    if (session) {
      // Update last used timestamp
      session.lastUsedAt = new Date();
      await this.saveSessionMetadata(session);

      logger.debug({
        tenantId,
        sessionId: session.sessionId,
        mode: session.mode,
        status: session.status
      }, 'Retrieved existing session');
      return session;
    }

    // Try to restore from Redis
    const metadata = await this.loadSessionMetadata(tenantId);
    if (metadata) {
      const now = new Date();
      session = {
        tenantId: metadata.tenantId,
        mode: metadata.mode,
        status: metadata.status,
        socket: null, // Socket will be created when connecting
        qrCode: null,
        lastUsedAt: new Date(metadata.lastUsedAt),
        createdAt: new Date(metadata.createdAt),
        poolMemberId: metadata.poolMemberId,
        // PHASE 1: Restore reconnect tracking
        reconnectAttempts: metadata.reconnectAttempts ?? 0,
        lastReconnectAt: metadata.lastReconnectAt ? new Date(metadata.lastReconnectAt) : null,
        statusChangedAt: metadata.statusChangedAt ? new Date(metadata.statusChangedAt) : now,
        socketVersion: metadata.socketVersion ?? 0,
        // PHASE 2: Restore or generate correlation IDs
        sessionId: metadata.sessionId ?? this.generateSessionId(),
        socketId: null, // Socket not yet connected
        authStateHash: metadata.authStateHash ?? null,
        lastError: metadata.lastError ?? null,
        connectedAt: metadata.connectedAt ? new Date(metadata.connectedAt) : null,
        connectedPhone: metadata.connectedPhone ?? null
      };

      this.sessions.set(tenantId, session);
      
      // ═══════════════════════════════════════════════════════════════════════════
      // SOCKET RECREATION ON RESTORE
      // If session was "connected" before service restart, recreate the socket
      // ═══════════════════════════════════════════════════════════════════════════
      if (metadata.status === ConnectionStatus.CONNECTED) {
        logger.info({
          tenantId,
          sessionId: session.sessionId,
          mode: session.mode
        }, 'Restored session from Redis - recreating socket for connected session');
        
        // Recreate socket asynchronously (don't block the restore)
        const restoredSession = session; // Capture for closure
        this.recreateSocketForRestoredSession(tenantId, restoredSession).catch((error) => {
          logger.error({
            err: error,
            tenantId,
            sessionId: restoredSession.sessionId
          }, 'Failed to recreate socket for restored session');
          
          // Mark session as disconnected if socket recreation fails
          restoredSession.status = ConnectionStatus.DISCONNECTED;
          restoredSession.lastError = `Socket recreation failed: ${error.message}`;
          this.saveSessionMetadata(restoredSession).catch((saveErr) => {
            logger.error({ err: saveErr, tenantId }, 'Failed to save session metadata after socket recreation failure');
          });
        });
      } else {
        logger.info({
          tenantId,
          sessionId: session.sessionId,
          mode: session.mode,
          status: session.status
        }, 'Restored session from Redis');
      }
      
      return session;
    }

    // Create new session with all tracking fields
    const now = new Date();
    session = {
      tenantId,
      mode,
      status: ConnectionStatus.DISCONNECTED,
      socket: null,
      qrCode: null,
      lastUsedAt: now,
      createdAt: now,
      // PHASE 1: Initialize reconnect tracking
      reconnectAttempts: 0,
      lastReconnectAt: null,
      statusChangedAt: now,
      socketVersion: 0,
      // PHASE 2: Initialize correlation IDs
      sessionId: this.generateSessionId(),
      socketId: null,
      authStateHash: null,
      lastError: null,
      connectedAt: null,
      connectedPhone: null
    };

    this.sessions.set(tenantId, session);
    await this.saveSessionMetadata(session);

    logger.info({
      tenantId,
      sessionId: session.sessionId,
      mode
    }, 'Created new session');
    return session;
  }

  /**
   * Get a session by tenant ID
   */
  getSession(tenantId: string): WhatsAppSession | undefined {
    return this.sessions.get(tenantId);
  }

  /**
   * Connect a session to WhatsApp
   * 
   * PHASE 3: Now includes admission control checks
   */
  async connectSession(tenantId: string): Promise<void> {
    // Check if connection is already in progress
    const existingLock = this.connectingLocks.get(tenantId);
    if (existingLock) {
      logger.debug({ tenantId }, 'Connection already in progress, waiting for existing lock');
      await existingLock;
      return;
    }

    const session = this.sessions.get(tenantId);
    if (!session) {
      throw new Error(`Session not found for tenant: ${tenantId}`);
    }

    // If already connected, do nothing
    if (session.socket && session.status === ConnectionStatus.CONNECTED) {
      logger.debug({ tenantId, sessionId: session.sessionId }, 'Session already connected');
      return;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PHASE 3 PILLAR 3: ADMISSION CONTROL
    // Check if we can accept this connection before proceeding
    // ═══════════════════════════════════════════════════════════════════════════
    const admissionResult = this.admissionController.checkConnectingAdmission(
      tenantId,
      this.getActiveSessions()
    );

    if (!admissionResult.admitted) {
      logger.warn({
        tenantId,
        sessionId: session.sessionId,
        reason: admissionResult.reason,
        currentCount: admissionResult.currentCount,
        limit: admissionResult.limit
      }, 'PILLAR 3: Connection admission denied');

      // Record last error for visibility
      session.lastError = `Admission denied: ${admissionResult.reason}`;
      await this.saveSessionMetadata(session);

      throw new Error(admissionResult.reason || 'Connection admission denied - system at capacity');
    }

    // PHASE 1: Check if session is in POISONED or LOGGED_OUT state
    if (session.status === ConnectionStatus.POISONED || session.status === ConnectionStatus.LOGGED_OUT) {
      logger.info({
        tenantId,
        sessionId: session.sessionId,
        status: session.status
      }, 'Session in terminal state, cleaning up before new connection');
      await this.cleanupPoisonedSession(tenantId);

      // PHASE 3: Clear self-healing tracking for this tenant
      selfHealingService.clearAutoResetTracking(tenantId);
    }

    // PHASE 2: Generate new session ID for this connect attempt
    session.sessionId = this.generateSessionId();

    // Create a lock and store it
    const connectionPromise = this.doConnectSession(tenantId, session);
    this.connectingLocks.set(tenantId, connectionPromise);

    try {
      await connectionPromise;
    } finally {
      // Always release the lock
      this.connectingLocks.delete(tenantId);
    }
  }

  /**
   * PHASE 1: Cleanup a poisoned or logged-out session
   */
  private async cleanupPoisonedSession(tenantId: string): Promise<void> {
    logger.info({ tenantId }, 'Cleaning up poisoned session state');

    const session = this.sessions.get(tenantId);
    if (session) {
      // Reset session state
      session.status = ConnectionStatus.DISCONNECTED;
      session.socket = null;
      session.qrCode = null;
      session.reconnectAttempts = 0;
      session.lastReconnectAt = null;
      session.statusChangedAt = new Date();
      session.socketVersion++;
      // PHASE 2: Clear correlation IDs for fresh start
      session.socketId = null;
      session.authStateHash = null;
      session.lastError = null;
      session.connectedAt = null;
    }

    // Delete ALL auth state
    await this.deleteAllAuthState(tenantId);

    logger.info({ tenantId }, 'Poisoned session cleanup complete');
  }

  /**
   * PHASE 1: Delete ALL auth state (Redis + disk)
   */
  private async deleteAllAuthState(tenantId: string): Promise<void> {
    // Delete from Redis
    try {
      await this.authPersistence.deleteAuthState(tenantId);
      logger.debug({ tenantId }, 'Deleted Redis auth state');
    } catch (err) {
      logger.error({ err, tenantId }, 'Failed to delete Redis auth state');
    }

    // Delete from disk
    const authDir = `./auth_states/${tenantId}`;
    try {
      if (fs.existsSync(authDir)) {
        fs.rmSync(authDir, { recursive: true, force: true });
        logger.debug({ tenantId }, 'Deleted disk auth state');
      }
    } catch (err) {
      logger.error({ err, tenantId }, 'Failed to delete disk auth state');
    }
  }

  /**
   * Internal connection logic
   * 
   * PHASE 2: Now tracks socket IDs and auth state hashes
   */
  private async doConnectSession(tenantId: string, session: WhatsAppSession): Promise<void> {
    // PHASE 2: Generate socket ID for this connection attempt
    const socketId = this.generateSocketId();
    session.socketId = socketId;

    const sessionLogger = this.createSessionLogger(session);

    try {
      sessionLogger.info({ mode: session.mode }, 'Connecting session');

      // PHASE 1: Update status with timestamp
      this.updateSessionStatus(session, ConnectionStatus.CONNECTING);
      await this.saveSessionMetadata(session);

      const authDir = `./auth_states/${tenantId}`;

      // ═══════════════════════════════════════════════════════════════════════
      // PILLAR 1: SINGLE SOURCE OF TRUTH ENFORCEMENT
      // ═══════════════════════════════════════════════════════════════════════
      const redisAuthState = await this.authPersistence.loadAuthState(tenantId);

      // PHASE 2: Track auth state hash for debugging
      session.authStateHash = this.generateAuthStateHash(redisAuthState);

      if (!redisAuthState) {
        // NO Redis auth state = NO valid auth state
        if (fs.existsSync(authDir)) {
          sessionLogger.warn('PILLAR 1: No Redis auth state but disk state exists - deleting stale disk auth');
          fs.rmSync(authDir, { recursive: true, force: true });
        }
        sessionLogger.info('PILLAR 1: No auth state - fresh QR pairing will be required');
      } else {
        sessionLogger.info({ authStateHash: session.authStateHash }, 'PILLAR 1: Found valid auth state in Redis');
      }

      // Load auth state from disk (Baileys multi-file auth state)
      const { state, saveCreds } = await useMultiFileAuthState(authDir);

      // If we have Redis auth state, restore it to the disk state
      if (redisAuthState) {
        try {
          // FULL REPLACEMENT, not merge - Redis is authoritative
          if (redisAuthState.creds) {
            Object.keys(state.creds).forEach(key => delete (state.creds as Record<string, unknown>)[key]);
            Object.assign(state.creds, redisAuthState.creds);
          }
          if (redisAuthState.keys) {
            Object.assign(state.keys, redisAuthState.keys);
          }
          sessionLogger.info('PILLAR 1: Restored auth state from Redis (authoritative source)');
        } catch (error) {
          sessionLogger.error({ err: error }, 'Failed to restore auth state from Redis, starting fresh');
          await this.deleteAllAuthState(tenantId);
          fs.rmSync(authDir, { recursive: true, force: true });
        }
      }

      // PHASE 1: Increment socket version for ownership tracking
      session.socketVersion++;
      const currentSocketVersion = session.socketVersion;

      // Create Baileys socket
      const socket = makeWASocket({
        auth: state,
        browser: Browsers.windows('Chrome'),
        syncFullHistory: false,
        logger: createLogger(`Baileys:${tenantId}`),
        connectTimeoutMs: 20000,
        retryRequestDelayMs: 2000
      });

      // Store socket reference
      session.socket = socket;

      // Handle connection updates
      socket.ev.on('connection.update', async (update: Partial<ConnectionState>) => {
        // PHASE 1: Socket ownership check
        if (session.socketVersion !== currentSocketVersion) {
          sessionLogger.warn({
            expectedVersion: currentSocketVersion,
            actualVersion: session.socketVersion
          }, 'PILLAR 4: Ignoring connection.update for stale socket');
          return;
        }
        await this.handleConnectionUpdate(tenantId, update, currentSocketVersion, socketId);
      });

      // Handle credentials update (save to both disk and Redis)
      socket.ev.on('creds.update', async () => {
        // PHASE 1: Socket ownership check
        if (session.socketVersion !== currentSocketVersion) {
          sessionLogger.warn('PILLAR 4: Ignoring creds.update for stale socket');
          return;
        }

        // Save to disk first
        await saveCreds();

        // Then save to Redis (authoritative)
        try {
          const authStateData: AuthStateData = {
            creds: state.creds,
            keys: state.keys
          };
          await this.authPersistence.saveAuthState(tenantId, authStateData);

          // PHASE 2: Update auth state hash
          session.authStateHash = this.generateAuthStateHash(authStateData);

          sessionLogger.debug({ authStateHash: session.authStateHash }, 'Auth state persisted to Redis');
        } catch (error) {
          sessionLogger.error({ err: error }, 'Failed to persist auth state to Redis');
        }
      });

      sessionLogger.info({ socketVersion: currentSocketVersion }, 'Socket created, waiting for connection');

    } catch (error) {
      // PHASE 2: Record last error
      session.lastError = error instanceof Error ? error.message : String(error);

      sessionLogger.error({ err: error }, 'Failed to connect session');
      this.updateSessionStatus(session, ConnectionStatus.DISCONNECTED);
      session.socket = null;
      session.socketId = null;
      await this.saveSessionMetadata(session);

      // If auth state is corrupted, clean it up
      if (error instanceof Error && error.message.includes('auth')) {
        sessionLogger.warn('Auth state may be corrupted, cleaning up');
        await this.deleteAllAuthState(tenantId);
      }

      throw error;
    }
  }

  /**
   * Recreate socket for a restored session that was previously connected
   * This handles service restarts where session metadata persists but sockets are lost
   */
  private async recreateSocketForRestoredSession(tenantId: string, session: WhatsAppSession): Promise<void> {
    const sessionLogger = this.createSessionLogger(session);
    
    try {
      sessionLogger.info('Recreating socket for restored connected session');
      
      // Generate new socket ID for this recreation
      const socketId = this.generateSocketId();
      session.socketId = socketId;
      session.socketVersion++;
      const currentSocketVersion = session.socketVersion;
      
      const authDir = `./auth_states/${tenantId}`;
      
      // Load auth state from Redis (authoritative source)
      const redisAuthState = await this.authPersistence.loadAuthState(tenantId);
      
      if (!redisAuthState) {
        throw new Error('No auth state found in Redis - cannot recreate socket');
      }
      
      sessionLogger.info({ authStateHash: this.generateAuthStateHash(redisAuthState) }, 'Found auth state in Redis');
      
      // Load auth state from disk (Baileys multi-file auth state)
      const { state, saveCreds } = await useMultiFileAuthState(authDir);
      
      // Restore Redis auth state to disk state (Redis is authoritative)
      if (redisAuthState.creds) {
        Object.keys(state.creds).forEach(key => delete (state.creds as Record<string, unknown>)[key]);
        Object.assign(state.creds, redisAuthState.creds);
      }
      if (redisAuthState.keys) {
        Object.assign(state.keys, redisAuthState.keys);
      }
      
      sessionLogger.info('Auth state restored from Redis');
      
      // Create Baileys socket
      const socket = makeWASocket({
        auth: state,
        browser: Browsers.windows('Chrome'),
        syncFullHistory: false,
        logger: createLogger(`Baileys:${tenantId}`),
        connectTimeoutMs: 20000,
        retryRequestDelayMs: 2000
      });
      
      // Store socket reference
      session.socket = socket;
      
      // Handle connection updates
      socket.ev.on('connection.update', async (update: Partial<ConnectionState>) => {
        // Socket ownership check
        if (session.socketVersion !== currentSocketVersion) {
          sessionLogger.warn({
            expectedVersion: currentSocketVersion,
            actualVersion: session.socketVersion
          }, 'Ignoring connection.update for stale socket (recreated)');
          return;
        }
        await this.handleConnectionUpdate(tenantId, update, currentSocketVersion, socketId);
      });
      
      // Handle credentials update
      socket.ev.on('creds.update', async () => {
        // Socket ownership check
        if (session.socketVersion !== currentSocketVersion) {
          sessionLogger.warn('Ignoring creds.update for stale socket (recreated)');
          return;
        }
        
        // Save to disk first
        await saveCreds();
        
        // Then save to Redis (authoritative)
        try {
          const authStateData: AuthStateData = {
            creds: state.creds,
            keys: state.keys
          };
          await this.authPersistence.saveAuthState(tenantId, authStateData);
          
          // Update auth state hash
          session.authStateHash = this.generateAuthStateHash(authStateData);
          
          sessionLogger.debug({ authStateHash: session.authStateHash }, 'Auth state persisted to Redis (recreated socket)');
        } catch (error) {
          sessionLogger.error({ err: error }, 'Failed to persist auth state to Redis (recreated socket)');
        }
      });
      
      sessionLogger.info({ socketVersion: currentSocketVersion, socketId }, 'Socket recreated successfully - waiting for connection');
      
      // Save updated session metadata
      await this.saveSessionMetadata(session);
      
    } catch (error) {
      sessionLogger.error({ err: error }, 'Failed to recreate socket for restored session');
      
      // Mark session as disconnected
      session.status = ConnectionStatus.DISCONNECTED;
      session.socket = null;
      session.socketId = null;
      session.lastError = error instanceof Error ? error.message : 'Socket recreation failed';
      
      await this.saveSessionMetadata(session);
      
      throw error;
    }
  }

  /**
   * Handle connection state updates from Baileys
   * 
   * PHASE 2: Now emits metrics and includes correlation IDs in logs
   */
  private async handleConnectionUpdate(
    tenantId: string,
    update: Partial<ConnectionState>,
    socketVersion: number,
    socketId: string
  ): Promise<void> {
    const session = this.sessions.get(tenantId);
    if (!session) {
      logger.warn({ tenantId, socketId }, 'Received connection update for non-existent session');
      return;
    }

    // PHASE 1: Socket ownership verification
    if (session.socketVersion !== socketVersion) {
      logger.warn({
        tenantId,
        sessionId: session.sessionId,
        expectedVersion: socketVersion,
        actualVersion: session.socketVersion
      }, 'PILLAR 4: Rejecting connection.update from stale socket version');
      return;
    }

    const sessionLogger = this.createSessionLogger(session);
    const { connection, lastDisconnect, qr } = update;

    // Handle QR code generation
    if (qr) {
      try {
        // Generate base64 QR code
        const qrBase64 = await QRCode.toDataURL(qr);
        session.qrCode = qrBase64;
        this.updateSessionStatus(session, ConnectionStatus.QR_PENDING);
        // PHASE 1: Reset reconnect attempts on QR generation (fresh start)
        session.reconnectAttempts = 0;
        session.lastReconnectAt = null;
        await this.saveSessionMetadata(session);

        // PHASE 2: Metrics for QR generation
        metricsService.recordQrGeneration(tenantId);

        sessionLogger.info('QR code generated - fresh pairing in progress');
      } catch (error) {
        session.lastError = error instanceof Error ? error.message : 'QR generation failed';
        sessionLogger.error({ err: error }, 'Failed to generate QR code');
      }
    }

    // Handle connection state changes
    if (connection === 'close') {
      const boomError = lastDisconnect?.error as Boom;
      const statusCode = boomError?.output?.statusCode;
      const errorMessage = boomError?.message || 'Unknown error';

      // PHASE 2: Record last error
      session.lastError = `${errorMessage} (code: ${statusCode})`;

      // PHASE 2: Calculate session lifetime for metrics
      if (session.connectedAt) {
        const lifetimeSeconds = (Date.now() - session.connectedAt.getTime()) / 1000;
        metricsService.recordSessionLifetime(tenantId, lifetimeSeconds, errorMessage);
      }

      // PHASE 2: Record socket disconnect metric
      const disconnectReason = DisconnectReason[statusCode] || 'UNKNOWN';
      metricsService.recordSocketDisconnect(tenantId, disconnectReason, statusCode);

      // Check for ECONNRESET specifically
      if (errorMessage.includes('ECONNRESET')) {
        metricsService.recordSocketDisconnect(tenantId, 'ECONNRESET', statusCode);
      }

      // ═══════════════════════════════════════════════════════════════════════
      // PILLAR 2: AUTH POISONING DETECTION
      // ═══════════════════════════════════════════════════════════════════════
      const isAuthInvalidating = this.AUTH_INVALIDATING_STATUS_CODES.includes(statusCode);

      sessionLogger.info({
        statusCode,
        errorMessage,
        isAuthInvalidating,
        disconnectReason
      }, 'Connection closed');

      if (isAuthInvalidating) {
        // ═════════════════════════════════════════════════════════════════════
        // PILLAR 2: POISONED AUTH - NUCLEAR CLEANUP
        // ═════════════════════════════════════════════════════════════════════
        sessionLogger.warn({ statusCode }, 'PILLAR 2: Auth-invalidating disconnect - deleting ALL auth state');

        // PHASE 2: Record poisoned session metric
        metricsService.recordSessionPoisoned(tenantId, statusCode);

        // Delete ALL auth state immediately
        await this.deleteAllAuthState(tenantId);

        // Transition to POISONED state
        session.socket = null;
        session.socketId = null;
        session.qrCode = null;
        session.connectedAt = null;
        session.authStateHash = null;

        // Choose appropriate terminal state
        if (statusCode === DisconnectReason.loggedOut) {
          this.updateSessionStatus(session, ConnectionStatus.LOGGED_OUT);
          sessionLogger.info('PILLAR 2: Session marked as LOGGED_OUT - requires fresh QR');
        } else {
          this.updateSessionStatus(session, ConnectionStatus.POISONED);
          sessionLogger.info('PILLAR 2: Session marked as POISONED - requires fresh QR');
        }

        // Delete session metadata to force completely fresh start on next connect
        const sessionKey = `whatsapp:session:${tenantId}`;
        await this.redis.del(sessionKey);

        // Keep in memory so we can provide status, but DO NOT reconnect
        await this.saveSessionMetadata(session);

        sessionLogger.info('PILLAR 2: Session reset complete - next /connect will emit fresh QR');

        // DO NOT RECONNECT
        return;
      }

      // ═════════════════════════════════════════════════════════════════════════
      // PILLAR 3: RECONNECT CONTROL (for non-auth-invalidating disconnects)
      // ═════════════════════════════════════════════════════════════════════════
      const now = Date.now();

      // Reset counter if enough time has passed since last reconnect
      if (session.lastReconnectAt &&
        (now - session.lastReconnectAt.getTime()) > this.RECONNECT_RESET_MS) {
        sessionLogger.debug('PILLAR 3: Resetting reconnect counter (stability window passed)');
        session.reconnectAttempts = 0;
        // PHASE 3: Also reset self-healing cooldown
        selfHealingService.resetReconnectCooldown(tenantId);
      }

      // Check if we've exceeded max reconnect attempts
      if (session.reconnectAttempts >= this.MAX_RECONNECT_ATTEMPTS) {
        sessionLogger.error({
          attempts: session.reconnectAttempts,
          maxAttempts: this.MAX_RECONNECT_ATTEMPTS
        }, 'PILLAR 3: Max reconnect attempts exceeded - stopping reconnection');

        session.socket = null;
        session.socketId = null;
        session.qrCode = null;
        session.connectedAt = null;
        this.updateSessionStatus(session, ConnectionStatus.DISCONNECTED);
        await this.saveSessionMetadata(session);

        // DO NOT reconnect - require manual intervention
        return;
      }

      // Increment reconnect counter
      session.reconnectAttempts++;
      session.lastReconnectAt = new Date();
      session.socket = null;
      session.socketId = null;
      session.connectedAt = null;

      // PHASE 2: Record reconnect attempt metric
      metricsService.recordReconnectAttempt(tenantId);

      this.updateSessionStatus(session, ConnectionStatus.DISCONNECTED);
      await this.saveSessionMetadata(session);

      // ═════════════════════════════════════════════════════════════════════════
      // PHASE 3 PILLAR 1: RECONNECT STORM SUPPRESSION
      // Check with self-healing service if reconnect should be allowed
      // ═════════════════════════════════════════════════════════════════════════
      const reconnectCheck = selfHealingService.shouldAllowReconnect(session);

      if (!reconnectCheck.allowed) {
        sessionLogger.warn({
          cooldownMs: reconnectCheck.cooldownMs,
          reason: reconnectCheck.reason
        }, 'PHASE 3: Reconnect suppressed by self-healing service');

        // Update last error for visibility
        session.lastError = reconnectCheck.reason || 'Reconnect suppressed - cooldown active';
        await this.saveSessionMetadata(session);

        // Schedule reconnect after cooldown expires
        const delayMs = reconnectCheck.cooldownMs || 60000;
        setTimeout(() => {
          const currentSession = this.sessions.get(tenantId);
          if (!currentSession) return;
          if (currentSession.status === ConnectionStatus.POISONED ||
            currentSession.status === ConnectionStatus.LOGGED_OUT) return;

          this.connectSession(tenantId).catch((error) => {
            logger.error({ err: error, tenantId }, 'PHASE 3: Delayed reconnection failed');
          });
        }, delayMs);

        return;
      }

      // Schedule reconnection with jitter
      const jitterMs = this.MIN_RECONNECT_DELAY_MS + Math.random() * this.MAX_RECONNECT_JITTER_MS;

      sessionLogger.info({
        delayMs: Math.round(jitterMs),
        attempt: session.reconnectAttempts,
        maxAttempts: this.MAX_RECONNECT_ATTEMPTS
      }, 'PILLAR 3: Scheduling reconnection with jitter');

      setTimeout(() => {
        // Double-check session still exists and is in correct state
        const currentSession = this.sessions.get(tenantId);
        if (!currentSession) {
          logger.debug({ tenantId }, 'Session no longer exists, skipping reconnect');
          return;
        }
        if (currentSession.status === ConnectionStatus.POISONED ||
          currentSession.status === ConnectionStatus.LOGGED_OUT) {
          logger.debug({ tenantId, status: currentSession.status }, 'Session in terminal state, skipping reconnect');
          return;
        }

        this.connectSession(tenantId).catch((error) => {
          logger.error({ err: error, tenantId, sessionId: currentSession.sessionId }, 'PILLAR 3: Reconnection failed');
        });
      }, jitterMs);

    } else if (connection === 'open') {
      // Successfully connected
      session.qrCode = null;
      session.lastUsedAt = new Date();
      session.connectedAt = new Date();
      const connectedJid = session.socket?.user?.id ?? null;
      const connectedDigits = connectedJid?.split('@')[0]?.split(':')[0]?.replace(/\D/g, '') ?? '';
      session.connectedPhone = connectedDigits ? `+${connectedDigits}` : null;
      session.lastError = null;
      // PHASE 1: Reset reconnect attempts on successful connection
      session.reconnectAttempts = 0;
      session.lastReconnectAt = null;
      // PHASE 3: Reset self-healing cooldown on successful connection
      selfHealingService.resetReconnectCooldown(tenantId);

      this.updateSessionStatus(session, ConnectionStatus.CONNECTED);
      await this.saveSessionMetadata(session);

      sessionLogger.info('Session connected successfully');
    }
  }

  /**
   * PHASE 1: Update session status with timestamp tracking
   */
  private updateSessionStatus(session: WhatsAppSession, newStatus: ConnectionStatus): void {
    const oldStatus = session.status;
    session.status = newStatus;
    session.statusChangedAt = new Date();

    if (oldStatus !== newStatus) {
      logger.debug({
        tenantId: session.tenantId,
        sessionId: session.sessionId,
        from: oldStatus,
        to: newStatus
      }, 'Session status transition');
    }
  }

  /**
   * Close a session (explicit disconnect - removes auth state)
   */
  async closeSession(tenantId: string): Promise<void> {
    const session = this.sessions.get(tenantId);
    if (!session) {
      logger.warn({ tenantId }, 'Attempted to close non-existent session');
      return;
    }

    const sessionLogger = this.createSessionLogger(session);

    try {
      // Close socket if exists
      if (session.socket) {
        try {
          await session.socket.logout();
        } catch (err) {
          sessionLogger.debug({ err }, 'Error during socket logout (may be already closed)');
        }
        session.socket = null;
        session.socketVersion++;
        session.socketId = null;
      }

      // Update status
      this.updateSessionStatus(session, ConnectionStatus.LOGGED_OUT);
      session.qrCode = null;
      session.reconnectAttempts = 0;
      session.lastReconnectAt = null;
      session.connectedAt = null;
      session.authStateHash = null;

      // Delete ALL auth state (explicit disconnect)
      await this.deleteAllAuthState(tenantId);

      // Delete session metadata from Redis
      const sessionKey = `whatsapp:session:${tenantId}`;
      await this.redis.del(sessionKey);
      sessionLogger.debug('Session metadata deleted from Redis');

      // Remove from memory
      this.sessions.delete(tenantId);

      sessionLogger.info('Session closed and auth state deleted');

    } catch (error) {
      sessionLogger.error({ err: error }, 'Error closing session');
      throw error;
    }
  }

  /**
   * PHASE 2: Force reset a session (admin operation)
   * Deletes all state and forces fresh QR on next connect
   */
  async forceResetSession(tenantId: string): Promise<void> {
    const session = this.sessions.get(tenantId);

    logger.info({ tenantId, sessionId: session?.sessionId }, 'ADMIN: Force resetting session');

    // PHASE 2: Record admin force reset metric
    metricsService.recordAdminForceReset(tenantId);

    // Delete socket if exists
    if (session?.socket) {
      try {
        session.socketVersion++;
        session.socket.end(undefined);
      } catch (err) {
        logger.debug({ err, tenantId }, 'Error ending socket during force reset');
      }
    }

    // Delete ALL auth state
    await this.deleteAllAuthState(tenantId);

    // Delete session metadata from Redis
    const sessionKey = `whatsapp:session:${tenantId}`;
    await this.redis.del(sessionKey);

    // Remove from memory
    this.sessions.delete(tenantId);

    logger.info({ tenantId }, 'ADMIN: Force reset complete - next /connect will require fresh QR');
  }

  /**
   * Close idle session (preserves auth state for restoration)
   */
  private async closeIdleSession(tenantId: string): Promise<void> {
    const session = this.sessions.get(tenantId);
    if (!session) {
      return;
    }

    const sessionLogger = this.createSessionLogger(session);

    try {
      // Close socket if exists (but don't logout - preserve auth)
      if (session.socket) {
        session.socketVersion++;
        session.socket.end(undefined);
        session.socket = null;
        session.socketId = null;
      }

      // Update status
      this.updateSessionStatus(session, ConnectionStatus.DISCONNECTED);
      session.qrCode = null;
      session.connectedAt = null;

      // Save metadata to Redis (preserves auth state)
      await this.saveSessionMetadata(session);

      // Remove from memory (but auth state remains in Redis)
      this.sessions.delete(tenantId);

      sessionLogger.info('Idle session closed, auth state preserved for restoration');

    } catch (error) {
      sessionLogger.error({ err: error }, 'Error closing idle session');
      throw error;
    }
  }

  /**
   * Get Redis client (for use by other services)
   */
  getRedisClient(): RedisClientType {
    return this.redis;
  }

  /**
   * Get all active sessions
   */
  getActiveSessions(): WhatsAppSession[] {
    return Array.from(this.sessions.values());
  }

  /**
   * Get session count
   */
  getSessionCount(): number {
    return this.sessions.size;
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // PHASE 3: RATE LIMITER & ADMISSION CONTROL ACCESS
  // ═══════════════════════════════════════════════════════════════════════════

  /**
   * Get rate limiter instance
   */
  getRateLimiter(): RateLimiter | null {
    return this.rateLimiter;
  }

  /**
   * Get admission controller instance
   */
  getAdmissionController(): AdmissionController {
    return this.admissionController;
  }

  /**
   * Check session admission (for external use)
   */
  checkSessionAdmission(tenantId: string): { admitted: boolean; reason?: string } {
    return this.admissionController.checkNewSession(tenantId, this.sessions.size);
  }

  /**
   * Check QR pending admission
   */
  checkQrPendingAdmission(tenantId: string): { admitted: boolean; reason?: string } {
    return this.admissionController.checkQrPendingAdmission(tenantId, this.getActiveSessions());
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // PHASE 2: HEALTH & DIAGNOSTICS
  // ═══════════════════════════════════════════════════════════════════════════

  /**
   * Get detailed session information for admin endpoints
   */
  getSessionDetails(tenantId: string): Record<string, unknown> | null {
    const session = this.sessions.get(tenantId);
    if (!session) {
      return null;
    }

    return {
      tenantId: session.tenantId,
      sessionId: session.sessionId,
      socketId: session.socketId,
      mode: session.mode,
      status: session.status,
      hasSocket: session.socket !== null,
      hasQrCode: session.qrCode !== null,
      authStateHash: session.authStateHash,
      reconnectAttempts: session.reconnectAttempts,
      lastReconnectAt: session.lastReconnectAt?.toISOString() ?? null,
      lastUsedAt: session.lastUsedAt.toISOString(),
      createdAt: session.createdAt.toISOString(),
      statusChangedAt: session.statusChangedAt.toISOString(),
      connectedAt: session.connectedAt?.toISOString() ?? null,
      connectedPhone: session.connectedPhone,
      socketVersion: session.socketVersion,
      lastError: session.lastError,
      ageSeconds: (Date.now() - session.createdAt.getTime()) / 1000,
      stuckSeconds: (Date.now() - session.statusChangedAt.getTime()) / 1000
    };
  }

  /**
   * Get all sessions with details for admin listing
   */
  getAllSessionDetails(): Record<string, unknown>[] {
    const sessions: Record<string, unknown>[] = [];
    for (const [tenantId, _session] of this.sessions.entries()) {
      const details = this.getSessionDetails(tenantId);
      if (details) {
        sessions.push(details);
      }
    }
    return sessions;
  }

  /**
   * Get health diagnostics data
   */
  getHealthDiagnostics(): Record<string, unknown> {
    const now = Date.now();
    const sessions = this.getActiveSessions();

    // Count by status
    const statusCount: Record<string, number> = {};
    let stuckCount = 0;
    let reconnectStormCount = 0;
    let poisonedCount = 0;
    let activeSocketCount = 0;

    for (const session of sessions) {
      // Count by status
      statusCount[session.status] = (statusCount[session.status] || 0) + 1;

      // Count stuck sessions (> 30s in transitional state)
      if ((session.status === ConnectionStatus.CONNECTING ||
        session.status === ConnectionStatus.QR_PENDING) &&
        (now - session.statusChangedAt.getTime()) > this.STALE_SESSION_TIMEOUT_MS) {
        stuckCount++;
      }

      // Count reconnect storms (3+ attempts)
      if (session.reconnectAttempts >= 3) {
        reconnectStormCount++;
      }

      // Count poisoned sessions
      if (session.status === ConnectionStatus.POISONED ||
        session.status === ConnectionStatus.LOGGED_OUT) {
        poisonedCount++;
      }

      // Count active sockets
      if (session.socket !== null) {
        activeSocketCount++;
      }
    }

    return {
      totalSessions: sessions.length,
      sessionsByStatus: statusCount,
      stuckSessions: stuckCount,
      reconnectStormsDetected: reconnectStormCount,
      poisonedSessions: poisonedCount,
      activeSockets: activeSocketCount
    };
  }

  /**
   * Save session metadata to Redis
   */
  private async saveSessionMetadata(session: WhatsAppSession): Promise<void> {
    const metadata: SessionMetadata = {
      tenantId: session.tenantId,
      mode: session.mode,
      status: session.status,
      lastUsedAt: session.lastUsedAt.toISOString(),
      createdAt: session.createdAt.toISOString(),
      poolMemberId: session.poolMemberId,
      // PHASE 1: Persist reconnect tracking
      reconnectAttempts: session.reconnectAttempts,
      lastReconnectAt: session.lastReconnectAt?.toISOString() ?? null,
      statusChangedAt: session.statusChangedAt.toISOString(),
      socketVersion: session.socketVersion,
      // PHASE 2: Persist correlation IDs
      sessionId: session.sessionId,
      authStateHash: session.authStateHash,
      lastError: session.lastError,
      connectedAt: session.connectedAt?.toISOString() ?? null,
      connectedPhone: session.connectedPhone
    };

    const key = `whatsapp:session:${session.tenantId}`;
    await this.redis.set(key, JSON.stringify(metadata), {
      EX: 24 * 60 * 60 // 24 hours TTL
    });

    logger.debug({
      tenantId: session.tenantId,
      sessionId: session.sessionId,
      status: session.status
    }, 'Saved session metadata to Redis');
  }

  /**
   * Load session metadata from Redis
   */
  private async loadSessionMetadata(tenantId: string): Promise<SessionMetadata | null> {
    const key = `whatsapp:session:${tenantId}`;
    const data = await this.redis.get(key);

    if (!data) {
      return null;
    }

    try {
      const metadata = JSON.parse(data) as SessionMetadata;
      logger.debug({ tenantId, sessionId: metadata.sessionId, status: metadata.status }, 'Loaded session metadata from Redis');
      return metadata;
    } catch (error) {
      logger.error({ err: error, tenantId }, 'Failed to parse session metadata');
      return null;
    }
  }

  /**
   * Start background cleanup job (idle sessions)
   */
  private startCleanupJob(): void {
    const intervalMs = config.session.cleanupIntervalMinutes * 60 * 1000;

    this.cleanupInterval = setInterval(async () => {
      await this.cleanupIdleSessions();
    }, intervalMs);

    logger.info(
      { intervalMinutes: config.session.cleanupIntervalMinutes },
      'Idle session cleanup job scheduled'
    );
  }

  /**
   * PHASE 1 PILLAR 5: Start stale session cleanup job
   */
  private startStaleSessionCleanupJob(): void {
    this.staleSessionInterval = setInterval(async () => {
      await this.cleanupStaleSessions();
    }, this.STALE_SESSION_CHECK_INTERVAL_MS);

    logger.info(
      { intervalMs: this.STALE_SESSION_CHECK_INTERVAL_MS, timeoutMs: this.STALE_SESSION_TIMEOUT_MS },
      'PILLAR 5: Stale session cleanup job scheduled'
    );
  }

  /**
   * PHASE 2: Start metrics update job
   */
  private startMetricsUpdateJob(): void {
    this.metricsInterval = setInterval(() => {
      this.updateMetrics();
    }, this.METRICS_UPDATE_INTERVAL_MS);

    logger.info(
      { intervalMs: this.METRICS_UPDATE_INTERVAL_MS },
      'PHASE 2: Metrics update job scheduled'
    );
  }

  /**
   * PHASE 2: Update gauge metrics
   */
  private updateMetrics(): void {
    const diagnostics = this.getHealthDiagnostics();

    // Update session state gauges
    metricsService.updateSessionStateMetrics(diagnostics.sessionsByStatus as Record<string, number>);

    // Update stuck session gauge
    metricsService.updateStuckSessionCount(diagnostics.stuckSessions as number);

    // Update reconnect storm gauge
    metricsService.updateReconnectStormCount(diagnostics.reconnectStormsDetected as number);

    // Update active socket gauge
    metricsService.updateActiveSocketCount(diagnostics.activeSockets as number);
  }

  /**
   * PILLAR 5: Cleanup stale sessions
   */
  private async cleanupStaleSessions(): Promise<void> {
    const now = Date.now();
    const sessionsToCleanup: string[] = [];

    for (const [tenantId, session] of this.sessions.entries()) {
      // Check if session is stuck in transitional state
      if (session.status === ConnectionStatus.CONNECTING ||
        session.status === ConnectionStatus.QR_PENDING) {

        const elapsedMs = now - session.statusChangedAt.getTime();

        if (elapsedMs > this.STALE_SESSION_TIMEOUT_MS) {
          sessionsToCleanup.push(tenantId);
        }
      }
    }

    if (sessionsToCleanup.length > 0) {
      logger.warn(
        { count: sessionsToCleanup.length, tenantIds: sessionsToCleanup },
        'PILLAR 5: Found stale sessions to cleanup'
      );

      for (const tenantId of sessionsToCleanup) {
        try {
          await this.forceCleanupSession(tenantId);
        } catch (error) {
          logger.error({ err: error, tenantId }, 'PILLAR 5: Error cleaning up stale session');
        }
      }
    }
  }

  /**
   * PILLAR 5: Force cleanup a stale session
   */
  private async forceCleanupSession(tenantId: string): Promise<void> {
    const session = this.sessions.get(tenantId);
    if (!session) {
      return;
    }

    const sessionLogger = this.createSessionLogger(session);

    sessionLogger.warn({
      status: session.status,
      stuckForMs: Date.now() - session.statusChangedAt.getTime()
    }, 'PILLAR 5: Force cleaning up stale session');

    // Destroy socket
    if (session.socket) {
      try {
        session.socketVersion++;
        session.socket.end(undefined);
      } catch (err) {
        sessionLogger.debug({ err }, 'Error ending stale socket');
      }
      session.socket = null;
      session.socketId = null;
    }

    // If session was in CONNECTING, we may have partial auth state
    if (session.status === ConnectionStatus.CONNECTING) {
      sessionLogger.info('PILLAR 5: Deleting auth state from stale CONNECTING session');
      await this.deleteAllAuthState(tenantId);
    }

    // Reset session state
    session.qrCode = null;
    session.reconnectAttempts = 0;
    session.lastReconnectAt = null;
    session.connectedAt = null;
    this.updateSessionStatus(session, ConnectionStatus.DISCONNECTED);

    // Remove from memory to force fresh start
    this.sessions.delete(tenantId);

    // Delete session metadata
    const sessionKey = `whatsapp:session:${tenantId}`;
    await this.redis.del(sessionKey);

    sessionLogger.info('PILLAR 5: Stale session cleanup complete');
  }

  /**
   * Cleanup idle sessions
   */
  private async cleanupIdleSessions(): Promise<void> {
    const now = new Date();
    const idleTimeoutMs = config.session.idleTimeoutMinutes * 60 * 1000;
    const sessionsToClose: string[] = [];

    // Find idle sessions
    for (const [tenantId, session] of this.sessions.entries()) {
      const idleTimeMs = now.getTime() - session.lastUsedAt.getTime();

      if (idleTimeMs > idleTimeoutMs) {
        sessionsToClose.push(tenantId);
      }
    }

    // Close idle sessions (preserving auth state)
    if (sessionsToClose.length > 0) {
      logger.info(
        { count: sessionsToClose.length, tenantIds: sessionsToClose },
        'Closing idle sessions (auth state preserved)'
      );

      for (const tenantId of sessionsToClose) {
        try {
          await this.closeIdleSession(tenantId);
        } catch (error) {
          logger.error({ err: error, tenantId }, 'Error closing idle session');
        }
      }
    } else {
      logger.debug('No idle sessions to cleanup');
    }
  }
}
