/**
 * Message type enumeration
 */
export enum MessageType {
  TEXT = 'text',
  MEDIA = 'media',
  TEMPLATE = 'template'
}

/**
 * Message send request
 */
export interface MessageSendRequest {
  tenantId: string;
  phone: string;
  text: string;
  type: MessageType;
}

/**
 * Message send response
 * 
 * PHASE 3: Added retryAfterMs for rate limiting feedback
 */
export interface MessageSendResponse {
  success: boolean;
  messageId?: string;
  error?: string;
  retryAfterMs?: number; // PHASE 3: Seconds to wait before retrying (if rate limited)
}

/**
 * QR code response
 */
export interface QrCodeResponse {
  success: boolean;
  qrCode?: string;
  expiresIn?: number;
  error?: string;
}

/**
 * Status response
 */
export interface StatusResponse {
  success: boolean;
  status?: string;
  lastUsedAt?: string;
  error?: string;
}

/**
 * Disconnect response
 */
export interface DisconnectResponse {
  success: boolean;
  error?: string;
}

/**
 * Health check response
 */
export interface HealthResponse {
  status: 'healthy' | 'unhealthy';
  activeSessions: number;
  poolSessions: number;
  uptime: number;
}
