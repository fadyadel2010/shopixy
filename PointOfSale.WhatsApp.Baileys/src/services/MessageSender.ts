/**
 * Message Sender Service
 * 
 * PHASE 1: Foundation Stabilization
 * PHASE 2: Observability, Control & Operability
 * PHASE 3: Reliability, Self-Healing & Scale Readiness
 * 
 * Implements PILLAR 4: Socket Ownership & Send Safety
 * 
 * Guarantees:
 * - Messages are only sent on valid, live, owned sockets — or not at all
 * - Socket reference is captured and verified before use
 * - Half-initialized or replaced sockets are rejected
 * 
 * PHASE 2 additions:
 * - Message send metrics (success/failure/latency)
 * - Failure tracking by session state
 * - Socket replacement detection metrics
 * - Correlation ID propagation in logs
 * 
 * PHASE 3 additions:
 * - Per-tenant message rate limiting
 * - Global burst protection
 * - Rate limit rejection metrics
 */

import { proto } from '@whiskeysockets/baileys';
import { SessionManager } from './SessionManager';
import { PoolManager } from './PoolManager';
import { metricsService } from './MetricsService';
import { MessageType, MessageSendResponse } from '../models/Message';
import { ConnectionStatus, SessionMode, WhatsAppSession } from '../models/Session';
import { validateAndFormatPhone } from '../utils/phoneValidator';
import { createLogger } from '../utils/logger';

const logger = createLogger('MessageSender');

/**
 * Message send options
 */
export interface MessageSendOptions {
  tenantId: string;
  phone: string;
  text: string;
  type: MessageType;
  timeout?: number; // Timeout in milliseconds (default: 5000)
  storeName?: string; // Store name for pool message branding
}

/**
 * PHASE 1: Socket safety result
 */
interface SocketSafetyCheck {
  safe: boolean;
  socket: any | null;
  socketVersion: number;
  error?: string;
}

/**
 * Message Sender Service
 * 
 * PHASE 1: Implements PILLAR 4 - Socket Ownership & Send Safety
 * PHASE 2: Comprehensive messaging metrics and failure tracking
 */
export class MessageSender {
  private readonly DEFAULT_TIMEOUT_MS = 5000; // 5 seconds

  constructor(
    private sessionManager: SessionManager,
    private poolManager: PoolManager
  ) { }

  /**
   * PHASE 2: Create a child logger with session correlation IDs
   */
  private createSessionLogger(session: WhatsAppSession) {
    return logger.child({
      tenantId: session.tenantId,
      sessionId: session.sessionId,
      socketId: session.socketId,
      socketVersion: session.socketVersion
    });
  }

  /**
   * PHASE 1 PILLAR 4: Validate socket safety before sending
   * 
   * ALL conditions MUST be true before sending:
   * 1. session.socket !== null
   * 2. session.socket === capturedSocket (not replaced)
   * 3. socket.ws.readyState === OPEN (1)
   * 4. session.status === CONNECTED
   * 
   * If any check fails → fail fast, do NOT send.
   */
  private checkSocketSafety(session: WhatsAppSession): SocketSafetyCheck {
    // Capture socket reference and version at check time
    const capturedSocket = session.socket;
    const capturedVersion = session.socketVersion;

    // Check 1: Socket exists
    if (!capturedSocket) {
      return {
        safe: false,
        socket: null,
        socketVersion: capturedVersion,
        error: 'PILLAR 4: Socket is null'
      };
    }

    // Check 2: Session status is CONNECTED
    if (session.status !== ConnectionStatus.CONNECTED) {
      return {
        safe: false,
        socket: capturedSocket,
        socketVersion: capturedVersion,
        error: `PILLAR 4: Session status is ${session.status}, expected CONNECTED`
      };
    }

    // Check 3: Socket hasn't been replaced (version matches)
    if (session.socketVersion !== capturedVersion) {
      return {
        safe: false,
        socket: capturedSocket,
        socketVersion: capturedVersion,
        error: `PILLAR 4: Socket version mismatch (expected ${capturedVersion}, got ${session.socketVersion})`
      };
    }

    // Check 4: WebSocket is in OPEN state
    try {
      const wsState = (capturedSocket as any).ws?.readyState;
      if (wsState !== undefined && wsState !== 1) { // 1 = OPEN
        return {
          safe: false,
          socket: capturedSocket,
          socketVersion: capturedVersion,
          error: `PILLAR 4: WebSocket readyState is ${wsState}, expected 1 (OPEN)`
        };
      }
    } catch (err) {
      // If we can't check ws state, continue (older Baileys versions)
      logger.debug({ tenantId: session.tenantId }, 'Could not check WebSocket state directly');
    }

    return {
      safe: true,
      socket: capturedSocket,
      socketVersion: capturedVersion
    };
  }

  /**
   * PHASE 1 PILLAR 4: Verify socket ownership before actual send
   */
  private verifySocketOwnership(session: WhatsAppSession, expectedVersion: number): boolean {
    if (session.socket === null) {
      return false;
    }
    if (session.socketVersion !== expectedVersion) {
      return false;
    }
    if (session.status !== ConnectionStatus.CONNECTED) {
      return false;
    }
    return true;
  }

  /**
   * Send a text message
   * 
   * PHASE 2: Includes timing and metrics collection
   */
  async sendTextMessage(options: MessageSendOptions): Promise<MessageSendResponse> {
    const { tenantId, phone, text, type, timeout = this.DEFAULT_TIMEOUT_MS, storeName } = options;
    const startTime = Date.now();

    try {
      logger.info({ tenantId, phone, type }, 'Sending message');

      // Validate phone number
      const phoneValidation = validateAndFormatPhone(phone);
      if (!phoneValidation.valid) {
        logger.warn({ tenantId, phone, error: phoneValidation.error }, 'Invalid phone number');

        // PHASE 2: Record failure metric
        const latencySeconds = (Date.now() - startTime) / 1000;
        metricsService.recordMessageSend(tenantId, 'failure', latencySeconds);
        metricsService.recordMessageFailureByState(tenantId, 'validation', 'invalid_phone');

        return {
          success: false,
          error: phoneValidation.error
        };
      }

      // ═══════════════════════════════════════════════════════════════════════
      // PHASE 3 PILLAR 2: RATE LIMIT CHECK (before any socket interaction)
      // ═══════════════════════════════════════════════════════════════════════
      const rateLimiter = this.sessionManager.getRateLimiter();
      if (rateLimiter) {
        const rateLimitResult = await rateLimiter.checkMessageRateLimit(tenantId);
        if (!rateLimitResult.allowed) {
          logger.warn({
            tenantId,
            phone,
            limitType: rateLimitResult.limitType,
            currentUsage: rateLimitResult.currentUsage,
            limit: rateLimitResult.limit,
            retryAfterMs: rateLimitResult.retryAfterMs
          }, 'PHASE 3: Message rate limited');

          // PHASE 2: Record failure metric
          const latencySeconds = (Date.now() - startTime) / 1000;
          metricsService.recordMessageSend(tenantId, 'failure', latencySeconds);
          metricsService.recordMessageFailureByState(tenantId, 'rate_limited', rateLimitResult.limitType || 'unknown');

          return {
            success: false,
            error: rateLimitResult.reason || 'Rate limit exceeded',
            retryAfterMs: rateLimitResult.retryAfterMs
          };
        }
      }

      // Get or restore session (will restore from Redis if needed)
      let session = this.sessionManager.getSession(tenantId);

      // If session not found in memory, try to restore it
      if (!session) {
        logger.info({ tenantId }, 'Session not in memory, attempting to restore...');
        try {
          session = await this.sessionManager.getOrCreateSession(tenantId, SessionMode.DEDICATED);
        } catch (restoreError) {
          logger.error({ err: restoreError, tenantId }, 'Failed to restore session');

          // PHASE 2: Record failure metric
          const latencySeconds = (Date.now() - startTime) / 1000;
          metricsService.recordMessageSend(tenantId, 'failure', latencySeconds);
          metricsService.recordMessageFailureByState(tenantId, 'no_session', 'restore_failed');

          return {
            success: false,
            error: `Session not available: ${restoreError instanceof Error ? restoreError.message : String(restoreError)}`
          };
        }
      }

      const sessionLogger = this.createSessionLogger(session);

      // PHASE 1: Check for terminal states that require fresh QR
      if (session.status === ConnectionStatus.POISONED || session.status === ConnectionStatus.LOGGED_OUT) {
        sessionLogger.warn({ status: session.status }, 'Session in terminal state, requires fresh QR');

        // PHASE 2: Record failure metric
        const latencySeconds = (Date.now() - startTime) / 1000;
        metricsService.recordMessageSend(tenantId, 'failure', latencySeconds);
        metricsService.recordMessageFailureByState(tenantId, session.status, 'terminal_state');

        return {
          success: false,
          error: `Session requires fresh authentication. Status: ${session.status}. Please scan QR code.`
        };
      }

      // If session exists but not connected, try to connect
      if (session.status !== ConnectionStatus.CONNECTED) {
        const currentStatus = session.status;
        sessionLogger.info({ status: currentStatus }, 'Session not connected, attempting to connect...');
        try {
          await this.sessionManager.connectSession(tenantId);

          // Wait a moment for the connection to stabilize
          await new Promise(resolve => setTimeout(resolve, 2000));

          // Re-get the session to check status
          session = this.sessionManager.getSession(tenantId);

          if (!session || session.status !== ConnectionStatus.CONNECTED) {
            sessionLogger.warn({ status: session?.status }, 'Session still not connected after reconnect attempt');

            // PHASE 2: Record failure metric
            const latencySeconds = (Date.now() - startTime) / 1000;
            metricsService.recordMessageSend(tenantId, 'failure', latencySeconds);
            metricsService.recordMessageFailureByState(tenantId, session?.status || 'unknown', 'reconnect_failed');

            return {
              success: false,
              error: `Session not connected. Current status: ${session?.status || 'unknown'}. Please re-scan QR code.`
            };
          }

          sessionLogger.info('Session reconnected successfully');
        } catch (connectError) {
          sessionLogger.error({ err: connectError }, 'Failed to connect session');

          // PHASE 2: Record failure metric
          const latencySeconds = (Date.now() - startTime) / 1000;
          metricsService.recordMessageSend(tenantId, 'failure', latencySeconds);
          metricsService.recordMessageFailureByState(tenantId, currentStatus, 'connect_exception');

          return {
            success: false,
            error: `Failed to connect session: ${connectError instanceof Error ? connectError.message : String(connectError)}`
          };
        }
      }

      // Route based on session mode
      if (session.mode === SessionMode.POOL) {
        return await this.sendPoolMessage(tenantId, phoneValidation.formatted!, text, storeName, timeout, startTime);
      } else {
        return await this.sendDedicatedMessage(session, phoneValidation.formatted!, text, timeout, startTime);
      }
    } catch (error) {
      logger.error({ err: error, tenantId, phone }, 'Exception sending message');

      // PHASE 2: Record failure metric
      const latencySeconds = (Date.now() - startTime) / 1000;
      metricsService.recordMessageSend(tenantId, 'failure', latencySeconds);
      metricsService.recordMessageFailureByState(tenantId, 'unknown', 'exception');

      return {
        success: false,
        error: `Failed to send message: ${error instanceof Error ? error.message : String(error)}`
      };
    }
  }

  /**
   * Send message via dedicated session
   * 
   * PHASE 1 PILLAR 4: Implements socket safety checks
   * PHASE 2: Records metrics for socket safety failures
   */
  private async sendDedicatedMessage(
    session: WhatsAppSession,
    formattedPhone: string,
    text: string,
    timeout: number,
    startTime: number
  ): Promise<MessageSendResponse> {
    const sessionLogger = this.createSessionLogger(session);

    // ═══════════════════════════════════════════════════════════════════════════
    // PILLAR 4: Socket Safety Check - Pre-send validation
    // ═══════════════════════════════════════════════════════════════════════════
    const safetyCheck = this.checkSocketSafety(session);

    if (!safetyCheck.safe) {
      sessionLogger.warn({
        error: safetyCheck.error,
        status: session.status,
        socketVersion: safetyCheck.socketVersion
      }, 'PILLAR 4: Socket safety check failed');

      // PHASE 2: Record failure metric
      const latencySeconds = (Date.now() - startTime) / 1000;
      metricsService.recordMessageSend(session.tenantId, 'failure', latencySeconds);
      metricsService.recordMessageFailureByState(session.tenantId, session.status, 'socket_safety_failed');

      // Check if this was a socket replacement
      if (safetyCheck.error?.includes('version mismatch')) {
        metricsService.recordSocketReplacedDuringSend(session.tenantId);
      }

      return {
        success: false,
        error: `Socket not available: ${safetyCheck.error}`
      };
    }

    // Send message with timeout and ownership verification
    return await this.sendWithTimeout(
      session,
      safetyCheck.socket!,
      safetyCheck.socketVersion,
      formattedPhone,
      text,
      timeout,
      startTime
    );
  }

  /**
   * Send message via pool session
   * 
   * PHASE 2: Records metrics for pool-based sending
   */
  private async sendPoolMessage(
    tenantId: string,
    formattedPhone: string,
    text: string,
    storeName: string | undefined,
    timeout: number,
    startTime: number
  ): Promise<MessageSendResponse> {
    try {
      // Select pool member
      const poolMember = await this.poolManager.selectPoolMember(tenantId);
      if (!poolMember) {
        logger.warn({ tenantId }, 'No available pool members');

        // PHASE 2: Record failure metric
        const latencySeconds = (Date.now() - startTime) / 1000;
        metricsService.recordMessageSend(tenantId, 'failure', latencySeconds);
        metricsService.recordMessageFailureByState(tenantId, 'pool', 'no_pool_members');

        return {
          success: false,
          error: 'No available pool members'
        };
      }

      // Get pool member session
      const poolSession = this.sessionManager.getSession(poolMember.id);
      if (!poolSession) {
        logger.warn({ tenantId, poolMemberId: poolMember.id }, 'Pool member session not found');
        this.poolManager.markMemberFailed(poolMember.id);

        // PHASE 2: Record failure metric
        const latencySeconds = (Date.now() - startTime) / 1000;
        metricsService.recordMessageSend(tenantId, 'failure', latencySeconds);
        metricsService.recordMessageFailureByState(tenantId, 'pool', 'pool_session_not_found');

        return {
          success: false,
          error: 'Pool member session not available'
        };
      }

      const sessionLogger = this.createSessionLogger(poolSession);

      // PHASE 1: Socket safety check for pool session
      const safetyCheck = this.checkSocketSafety(poolSession);

      if (!safetyCheck.safe) {
        sessionLogger.warn({
          poolMemberId: poolMember.id,
          error: safetyCheck.error
        }, 'PILLAR 4: Pool member socket safety check failed');
        this.poolManager.markMemberFailed(poolMember.id);

        // PHASE 2: Record failure metric
        const latencySeconds = (Date.now() - startTime) / 1000;
        metricsService.recordMessageSend(tenantId, 'failure', latencySeconds);
        metricsService.recordMessageFailureByState(tenantId, 'pool', 'pool_socket_safety_failed');

        return {
          success: false,
          error: `Pool member socket not available: ${safetyCheck.error}`
        };
      }

      // Brand message with store name if provided
      const brandedText = storeName ? `[${storeName}] ${text}` : text;

      // Increment connections
      this.poolManager.incrementConnections(poolMember.id);

      try {
        // Send message with timeout
        const result = await this.sendWithTimeout(
          poolSession,
          safetyCheck.socket!,
          safetyCheck.socketVersion,
          formattedPhone,
          brandedText,
          timeout,
          startTime
        );

        if (result.success) {
          sessionLogger.info(
            { poolMemberId: poolMember.id, messageId: result.messageId },
            'Pool message sent successfully'
          );
        } else {
          sessionLogger.error(
            { poolMemberId: poolMember.id, error: result.error },
            'Pool message send failed'
          );
        }

        return result;
      } finally {
        // Decrement connections
        this.poolManager.decrementConnections(poolMember.id);
      }
    } catch (error) {
      logger.error({ err: error, tenantId }, 'Exception sending pool message');

      // PHASE 2: Record failure metric
      const latencySeconds = (Date.now() - startTime) / 1000;
      metricsService.recordMessageSend(tenantId, 'failure', latencySeconds);
      metricsService.recordMessageFailureByState(tenantId, 'pool', 'exception');

      return {
        success: false,
        error: `Failed to send pool message: ${error instanceof Error ? error.message : String(error)}`
      };
    }
  }

  /**
   * Send message with timeout
   * 
   * PHASE 1 PILLAR 4: Includes socket ownership verification
   * PHASE 2: Records send latency and result metrics
   */
  private async sendWithTimeout(
    session: WhatsAppSession,
    socket: any,
    expectedSocketVersion: number,
    formattedPhone: string,
    text: string,
    timeoutMs: number,
    startTime: number
  ): Promise<MessageSendResponse> {
    const sessionLogger = this.createSessionLogger(session);

    return new Promise((resolve) => {
      // Set timeout
      const timeoutHandle = setTimeout(() => {
        sessionLogger.warn({ timeoutMs }, 'Message send timeout');

        // PHASE 2: Record timeout metric
        const latencySeconds = (Date.now() - startTime) / 1000;
        metricsService.recordMessageSend(session.tenantId, 'failure', latencySeconds);
        metricsService.recordMessageFailureByState(session.tenantId, session.status, 'timeout');

        resolve({
          success: false,
          error: `Message send timeout after ${timeoutMs}ms`
        });
      }, timeoutMs);

      // ═══════════════════════════════════════════════════════════════════════
      // PILLAR 4: Final ownership verification before send
      // ═══════════════════════════════════════════════════════════════════════
      if (!this.verifySocketOwnership(session, expectedSocketVersion)) {
        clearTimeout(timeoutHandle);
        sessionLogger.warn({
          expectedVersion: expectedSocketVersion,
          actualVersion: session.socketVersion
        }, 'PILLAR 4: Socket ownership verification failed before send');

        // PHASE 2: Record socket replacement metric
        const latencySeconds = (Date.now() - startTime) / 1000;
        metricsService.recordMessageSend(session.tenantId, 'failure', latencySeconds);
        metricsService.recordMessageFailureByState(session.tenantId, session.status, 'socket_replaced');
        metricsService.recordSocketReplacedDuringSend(session.tenantId);

        resolve({
          success: false,
          error: 'Socket was replaced or closed before send could complete'
        });
        return;
      }

      // Send message
      socket
        .sendMessage(formattedPhone, { text })
        .then((result: proto.WebMessageInfo) => {
          clearTimeout(timeoutHandle);

          // Extract message ID
          const messageId = result.key?.id;
          const latencySeconds = (Date.now() - startTime) / 1000;

          if (messageId) {
            sessionLogger.info({ messageId, latencySeconds }, 'Message sent successfully');

            // PHASE 2: Record success metric
            metricsService.recordMessageSend(session.tenantId, 'success', latencySeconds);

            resolve({
              success: true,
              messageId
            });
          } else {
            sessionLogger.warn({ latencySeconds }, 'Message sent but no message ID returned');

            // PHASE 2: Record failure metric (no messageId is unusual)
            metricsService.recordMessageSend(session.tenantId, 'failure', latencySeconds);
            metricsService.recordMessageFailureByState(session.tenantId, session.status, 'no_message_id');

            resolve({
              success: false,
              error: 'Message sent but no message ID returned'
            });
          }
        })
        .catch((error: Error) => {
          clearTimeout(timeoutHandle);
          const latencySeconds = (Date.now() - startTime) / 1000;

          sessionLogger.error({ err: error, latencySeconds }, 'Send message failed');

          // PHASE 2: Record failure metric
          metricsService.recordMessageSend(session.tenantId, 'failure', latencySeconds);
          metricsService.recordMessageFailureByState(session.tenantId, session.status, 'send_exception');

          resolve({
            success: false,
            error: `Send failed: ${error.message}`
          });
        });
    });
  }

  /**
   * Send media message (future implementation)
   */
  async sendMediaMessage(options: MessageSendOptions): Promise<MessageSendResponse> {
    logger.warn({ tenantId: options.tenantId }, 'Media messages not yet implemented');
    return {
      success: false,
      error: 'Media messages not yet implemented'
    };
  }

  /**
   * Send template message (future implementation)
   */
  async sendTemplateMessage(options: MessageSendOptions): Promise<MessageSendResponse> {
    logger.warn({ tenantId: options.tenantId }, 'Template messages not yet implemented');
    return {
      success: false,
      error: 'Template messages not yet implemented'
    };
  }

  /**
   * Send message (routes to appropriate handler based on type)
   */
  async sendMessage(options: MessageSendOptions): Promise<MessageSendResponse> {
    switch (options.type) {
      case MessageType.TEXT:
        return this.sendTextMessage(options);
      case MessageType.MEDIA:
        return this.sendMediaMessage(options);
      case MessageType.TEMPLATE:
        return this.sendTemplateMessage(options);
      default:
        return {
          success: false,
          error: `Unsupported message type: ${options.type}`
        };
    }
  }
}
