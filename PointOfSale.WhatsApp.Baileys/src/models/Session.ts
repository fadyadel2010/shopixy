import { WASocket } from '@whiskeysockets/baileys';

/**
 * Session mode enumeration
 */
export enum SessionMode {
  DEDICATED = 'DEDICATED',
  POOL = 'POOL'
}

/**
 * Connection status enumeration
 * 
 * PHASE 1: Extended with POISONED and LOGGED_OUT states
 * to enable deterministic session lifecycle control.
 */
export enum ConnectionStatus {
  CONNECTED = 'connected',
  DISCONNECTED = 'disconnected',
  QR_PENDING = 'qr_pending',
  CONNECTING = 'connecting',
  /** Auth state is invalid - requires fresh QR pairing */
  POISONED = 'poisoned',
  /** User explicitly logged out - requires fresh QR pairing */
  LOGGED_OUT = 'logged_out'
}

/**
 * WhatsApp session model
 * 
 * PHASE 1: Extended with reconnect tracking and state timestamps
 * to enable reconnect limits and stale session cleanup.
 * 
 * PHASE 2: Extended with correlation IDs for traceability:
 * - sessionId: UUID per connect attempt
 * - socketId: Unique identifier for current socket
 * - authStateHash: Non-reversible hash for auth state tracking
 */
export interface WhatsAppSession {
  tenantId: string;
  mode: SessionMode;
  status: ConnectionStatus;
  socket: WASocket | null;
  qrCode: string | null;
  lastUsedAt: Date;
  createdAt: Date;
  poolMemberId?: string;

  // PHASE 1: Reconnect control
  /** Number of consecutive reconnect attempts */
  reconnectAttempts: number;
  /** Timestamp of last reconnect attempt */
  lastReconnectAt: Date | null;

  // PHASE 1: State timestamps for stale session cleanup
  /** Timestamp when current status was entered */
  statusChangedAt: Date;

  // PHASE 1: Socket version for ownership tracking
  /** Increments each time a new socket is created */
  socketVersion: number;

  // PHASE 2: Correlation & Traceability
  /** UUID generated per connect attempt for end-to-end tracing */
  sessionId: string;
  /** Unique identifier for current socket instance */
  socketId: string | null;
  /** Non-reversible hash of auth state for tracking (first 8 chars of SHA256) */
  authStateHash: string | null;
  /** Last error message for debugging */
  lastError: string | null;
  /** Timestamp when socket became connected (for lifetime tracking) */
  connectedAt: Date | null;
  /** Connected WhatsApp identity attested by the live Baileys socket. */
  connectedPhone: string | null;
}

/**
 * Session metadata stored in Redis
 * 
 * PHASE 1: Extended with reconnect tracking for persistence across restarts
 * PHASE 2: Extended with correlation IDs for traceability
 */
export interface SessionMetadata {
  tenantId: string;
  mode: SessionMode;
  status: ConnectionStatus;
  lastUsedAt: string; // ISO8601
  createdAt: string; // ISO8601
  poolMemberId?: string;

  // PHASE 1: Reconnect control (persisted)
  reconnectAttempts?: number;
  lastReconnectAt?: string | null; // ISO8601
  statusChangedAt?: string; // ISO8601
  socketVersion?: number;

  // PHASE 2: Correlation & Traceability (persisted)
  sessionId?: string;
  authStateHash?: string | null;
  lastError?: string | null;
  connectedAt?: string | null; // ISO8601
  connectedPhone?: string | null;
}

/**
 * Pool member configuration
 */
export interface PoolMember {
  id: string;
  phoneNumber: string;
  activeConnections: number;
  maxConnections: number;
  status: 'active' | 'inactive' | 'maintenance';
}

/**
 * Pool configuration
 */
export interface PoolConfiguration {
  size: number;
  members: PoolMember[];
  routingStrategy: 'LEAST_LOADED' | 'ROUND_ROBIN';
}
