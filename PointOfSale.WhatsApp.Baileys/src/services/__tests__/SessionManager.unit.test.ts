/**
 * Unit Tests for SessionManager
 * 
 * These tests validate specific scenarios and edge cases.
 */

import { SessionManager } from '../SessionManager';
import { SessionMode, ConnectionStatus } from '../../models/Session';

// Mock Redis and Baileys dependencies
jest.mock('redis', () => ({
  createClient: jest.fn(() => ({
    on: jest.fn(),
    connect: jest.fn().mockResolvedValue(undefined),
    quit: jest.fn().mockResolvedValue(undefined),
    get: jest.fn().mockResolvedValue(null),
    set: jest.fn().mockResolvedValue('OK'),
    del: jest.fn().mockResolvedValue(1)
  }))
}));

jest.mock('@whiskeysockets/baileys', () => ({
  __esModule: true,
  default: jest.fn(() => ({
    ev: {
      on: jest.fn()
    },
    logout: jest.fn().mockResolvedValue(undefined)
  })),
  useMultiFileAuthState: jest.fn(() => ({
    state: {},
    saveCreds: jest.fn()
  })),
  DisconnectReason: {
    loggedOut: 401
  },
  Browsers: {
    ubuntu: jest.fn(() => ['Ubuntu', '20.04', '1.0']),
    windows: jest.fn(() => ['Windows', 'Chrome', '1.0'])
  }
}));

jest.mock('qrcode', () => ({
  toDataURL: jest.fn().mockResolvedValue('data:image/png;base64,mockQRCode')
}));

jest.mock('../../utils/logger', () => ({
  createLogger: jest.fn(() => ({
    child: jest.fn().mockReturnThis(),
    info: jest.fn(),
    debug: jest.fn(),
    warn: jest.fn(),
    error: jest.fn()
  }))
}));

describe('SessionManager Unit Tests', () => {
  let sessionManager: SessionManager;

  beforeEach(async () => {
    sessionManager = new SessionManager();
    await sessionManager.initialize();
  });

  afterEach(async () => {
    await sessionManager.shutdown();
  });

  describe('Session Creation', () => {
    it('should create a new session with correct initial state', async () => {
      const tenantId = 'test-tenant-1';
      const mode = SessionMode.DEDICATED;

      const session = await sessionManager.getOrCreateSession(tenantId, mode);

      expect(session.tenantId).toBe(tenantId);
      expect(session.mode).toBe(mode);
      expect(session.status).toBe(ConnectionStatus.DISCONNECTED);
      expect(session.socket).toBeNull();
      expect(session.qrCode).toBeNull();
      expect(session.lastUsedAt).toBeInstanceOf(Date);
      expect(session.createdAt).toBeInstanceOf(Date);
    });

    it('should create sessions with different modes', async () => {
      const dedicatedSession = await sessionManager.getOrCreateSession(
        'tenant-dedicated',
        SessionMode.DEDICATED
      );
      const poolSession = await sessionManager.getOrCreateSession(
        'tenant-pool',
        SessionMode.POOL
      );

      expect(dedicatedSession.mode).toBe(SessionMode.DEDICATED);
      expect(poolSession.mode).toBe(SessionMode.POOL);
    });
  });

  describe('Session Retrieval', () => {
    it('should retrieve an existing session', async () => {
      const tenantId = 'test-tenant-2';
      await sessionManager.getOrCreateSession(tenantId, SessionMode.DEDICATED);

      const retrieved = sessionManager.getSession(tenantId);

      expect(retrieved).toBeDefined();
      expect(retrieved?.tenantId).toBe(tenantId);
    });

    it('should return undefined for non-existent session', () => {
      const retrieved = sessionManager.getSession('non-existent-tenant');
      expect(retrieved).toBeUndefined();
    });

    it('should update lastUsedAt on retrieval', async () => {
      const tenantId = 'test-tenant-3';
      const session1 = await sessionManager.getOrCreateSession(tenantId, SessionMode.DEDICATED);
      const firstUsedAt = session1.lastUsedAt;

      // Wait a bit
      await new Promise(resolve => setTimeout(resolve, 10));

      const session2 = await sessionManager.getOrCreateSession(tenantId, SessionMode.DEDICATED);
      const secondUsedAt = session2.lastUsedAt;

      expect(secondUsedAt.getTime()).toBeGreaterThanOrEqual(firstUsedAt.getTime());
    });
  });

  describe('Session Closure', () => {
    it('should close an existing session', async () => {
      const tenantId = 'test-tenant-4';
      await sessionManager.getOrCreateSession(tenantId, SessionMode.DEDICATED);

      await sessionManager.closeSession(tenantId);

      const retrieved = sessionManager.getSession(tenantId);
      expect(retrieved).toBeUndefined();
    });

    it('should not throw when closing non-existent session', async () => {
      await expect(
        sessionManager.closeSession('non-existent-tenant')
      ).resolves.not.toThrow();
    });
  });

  describe('Session Count', () => {
    it('should return 0 for empty session manager', () => {
      expect(sessionManager.getSessionCount()).toBe(0);
    });

    it('should return correct count after creating sessions', async () => {
      await sessionManager.getOrCreateSession('tenant-1', SessionMode.DEDICATED);
      await sessionManager.getOrCreateSession('tenant-2', SessionMode.DEDICATED);
      await sessionManager.getOrCreateSession('tenant-3', SessionMode.POOL);

      expect(sessionManager.getSessionCount()).toBe(3);
    });

    it('should decrease count after closing session', async () => {
      await sessionManager.getOrCreateSession('tenant-1', SessionMode.DEDICATED);
      await sessionManager.getOrCreateSession('tenant-2', SessionMode.DEDICATED);

      expect(sessionManager.getSessionCount()).toBe(2);

      await sessionManager.closeSession('tenant-1');

      expect(sessionManager.getSessionCount()).toBe(1);
    });
  });

  describe('Active Sessions', () => {
    it('should return empty array when no sessions exist', () => {
      const activeSessions = sessionManager.getActiveSessions();
      expect(activeSessions).toEqual([]);
    });

    it('should return all active sessions', async () => {
      await sessionManager.getOrCreateSession('tenant-1', SessionMode.DEDICATED);
      await sessionManager.getOrCreateSession('tenant-2', SessionMode.POOL);

      const activeSessions = sessionManager.getActiveSessions();

      expect(activeSessions).toHaveLength(2);
      expect(activeSessions.map(s => s.tenantId)).toContain('tenant-1');
      expect(activeSessions.map(s => s.tenantId)).toContain('tenant-2');
    });
  });

  describe('Initialization and Shutdown', () => {
    it('should initialize successfully', async () => {
      const newManager = new SessionManager();
      await expect(newManager.initialize()).resolves.not.toThrow();
      await newManager.shutdown();
    });

    it('should shutdown successfully', async () => {
      const newManager = new SessionManager();
      await newManager.initialize();
      await expect(newManager.shutdown()).resolves.not.toThrow();
    });

    it('should close all sessions on shutdown', async () => {
      const newManager = new SessionManager();
      await newManager.initialize();

      await newManager.getOrCreateSession('tenant-1', SessionMode.DEDICATED);
      await newManager.getOrCreateSession('tenant-2', SessionMode.DEDICATED);

      expect(newManager.getSessionCount()).toBe(2);

      await newManager.shutdown();

      expect(newManager.getSessionCount()).toBe(0);
    });
  });

  describe('Edge Cases', () => {
    it('should handle empty tenant ID gracefully', async () => {
      const session = await sessionManager.getOrCreateSession('', SessionMode.DEDICATED);
      expect(session.tenantId).toBe('');
    });

    it('should handle very long tenant IDs', async () => {
      const longTenantId = 'a'.repeat(1000);
      const session = await sessionManager.getOrCreateSession(longTenantId, SessionMode.DEDICATED);
      expect(session.tenantId).toBe(longTenantId);
    });

    it('should handle special characters in tenant ID', async () => {
      const specialTenantId = 'tenant-!@#$%^&*()_+-=[]{}|;:,.<>?';
      const session = await sessionManager.getOrCreateSession(specialTenantId, SessionMode.DEDICATED);
      expect(session.tenantId).toBe(specialTenantId);
    });
  });
});
