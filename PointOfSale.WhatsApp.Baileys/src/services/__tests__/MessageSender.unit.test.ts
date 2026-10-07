/**
 * Unit Tests for MessageSender
 * 
 * These tests validate specific scenarios and edge cases for message sending.
 */

import { MessageSender } from '../MessageSender';
import { SessionManager } from '../SessionManager';
import { PoolManager } from '../PoolManager';
import { MessageType } from '../../models/Message';
import { ConnectionStatus, SessionMode } from '../../models/Session';

// Mock dependencies
jest.mock('redis', () => ({
  createClient: jest.fn(() => ({
    on: jest.fn(),
    connect: jest.fn().mockResolvedValue(undefined),
    quit: jest.fn().mockResolvedValue(undefined),
    get: jest.fn().mockResolvedValue(null),
    set: jest.fn().mockResolvedValue('OK'),
    del: jest.fn().mockResolvedValue(1),
    exists: jest.fn().mockResolvedValue(0)
  }))
}));

jest.mock('@whiskeysockets/baileys', () => ({
  __esModule: true,
  default: jest.fn(() => ({
    ev: {
      on: jest.fn()
    },
    sendMessage: jest.fn(),
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
  },
  proto: {
    WebMessageInfo: {}
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

describe('MessageSender Unit Tests', () => {
  let sessionManager: SessionManager;
  let messageSender: MessageSender;

  beforeEach(async () => {
    sessionManager = new SessionManager();
    await sessionManager.initialize();
    messageSender = new MessageSender(
      sessionManager,
      new PoolManager(sessionManager.getRedisClient())
    );
  });

  afterEach(async () => {
    await sessionManager.shutdown();
  });

  describe('Phone Number Validation', () => {
    it('should reject empty phone number', async () => {
      const result = await messageSender.sendTextMessage({
        tenantId: 'test-tenant',
        phone: '',
        text: 'Test message',
        type: MessageType.TEXT
      });

      expect(result.success).toBe(false);
      expect(result.error).toContain('required');
    });

    it('should reject phone without + prefix', async () => {
      const result = await messageSender.sendTextMessage({
        tenantId: 'test-tenant',
        phone: '1234567890',
        text: 'Test message',
        type: MessageType.TEXT
      });

      expect(result.success).toBe(false);
      expect(result.error).toContain('E.164');
    });

    it('should reject phone starting with +0', async () => {
      const result = await messageSender.sendTextMessage({
        tenantId: 'test-tenant',
        phone: '+0123456789',
        text: 'Test message',
        type: MessageType.TEXT
      });

      expect(result.success).toBe(false);
      expect(result.error).toContain('E.164');
    });

    it('should reject phone with letters', async () => {
      const result = await messageSender.sendTextMessage({
        tenantId: 'test-tenant',
        phone: '+1abc567890',
        text: 'Test message',
        type: MessageType.TEXT
      });

      expect(result.success).toBe(false);
      expect(result.error).toContain('E.164');
    });

    it('should accept valid E.164 phone', async () => {
      const session = await sessionManager.getOrCreateSession('test-tenant', SessionMode.DEDICATED);
      session.status = ConnectionStatus.CONNECTED;
      session.socket = {
        sendMessage: jest.fn().mockResolvedValue({
          key: { id: 'msg-123' }
        })
      } as any;

      const result = await messageSender.sendTextMessage({
        tenantId: 'test-tenant',
        phone: '+1234567890',
        text: 'Test message',
        type: MessageType.TEXT
      });

      expect(result.success).toBe(true);
      expect(result.messageId).toBe('msg-123');
    });
  });

  describe('Session Validation', () => {
    it('should fail if a missing session cannot reconnect', async () => {
      const connectSpy = jest.spyOn(sessionManager, 'connectSession')
        .mockRejectedValueOnce(new Error('Session not connected'));

      const result = await messageSender.sendTextMessage({
        tenantId: 'non-existent-tenant',
        phone: '+1234567890',
        text: 'Test message',
        type: MessageType.TEXT
      });

      expect(result.success).toBe(false);
      expect(result.error).toContain('not connected');
      connectSpy.mockRestore();
    });

    it('should fail if session not connected', async () => {
      await sessionManager.getOrCreateSession('test-tenant', SessionMode.DEDICATED);

      const result = await messageSender.sendTextMessage({
        tenantId: 'test-tenant',
        phone: '+1234567890',
        text: 'Test message',
        type: MessageType.TEXT
      });

      expect(result.success).toBe(false);
      expect(result.error).toContain('not connected');
    });

    it('should fail if socket not available', async () => {
      const session = await sessionManager.getOrCreateSession('test-tenant', SessionMode.DEDICATED);
      session.status = ConnectionStatus.CONNECTED;
      session.socket = null;

      const result = await messageSender.sendTextMessage({
        tenantId: 'test-tenant',
        phone: '+1234567890',
        text: 'Test message',
        type: MessageType.TEXT
      });

      expect(result.success).toBe(false);
      expect(result.error?.toLowerCase()).toContain('socket not available');
    });
  });

  describe('Successful Message Send', () => {
    it('should send message successfully', async () => {
      const session = await sessionManager.getOrCreateSession('test-tenant', SessionMode.DEDICATED);
      session.status = ConnectionStatus.CONNECTED;
      session.socket = {
        sendMessage: jest.fn().mockResolvedValue({
          key: { id: 'message-id-123' }
        })
      } as any;

      const result = await messageSender.sendTextMessage({
        tenantId: 'test-tenant',
        phone: '+1234567890',
        text: 'Hello World',
        type: MessageType.TEXT
      });

      expect(result.success).toBe(true);
      expect(result.messageId).toBe('message-id-123');
      expect(result.error).toBeUndefined();
    });

    it('should call socket.sendMessage with correct parameters', async () => {
      const session = await sessionManager.getOrCreateSession('test-tenant', SessionMode.DEDICATED);
      session.status = ConnectionStatus.CONNECTED;
      const mockSendMessage = jest.fn().mockResolvedValue({
        key: { id: 'msg-123' }
      });
      session.socket = { sendMessage: mockSendMessage } as any;

      await messageSender.sendTextMessage({
        tenantId: 'test-tenant',
        phone: '+1234567890',
        text: 'Test message',
        type: MessageType.TEXT
      });

      expect(mockSendMessage).toHaveBeenCalledWith(
        '1234567890@s.whatsapp.net',
        { text: 'Test message' }
      );
    });
  });

  describe('Failed Message Send', () => {
    it('should handle socket error', async () => {
      const session = await sessionManager.getOrCreateSession('test-tenant', SessionMode.DEDICATED);
      session.status = ConnectionStatus.CONNECTED;
      session.socket = {
        sendMessage: jest.fn().mockRejectedValue(new Error('Network error'))
      } as any;

      const result = await messageSender.sendTextMessage({
        tenantId: 'test-tenant',
        phone: '+1234567890',
        text: 'Test message',
        type: MessageType.TEXT
      });

      expect(result.success).toBe(false);
      expect(result.error).toContain('Network error');
      expect(result.messageId).toBeUndefined();
    });

    it('should handle missing message ID in response', async () => {
      const session = await sessionManager.getOrCreateSession('test-tenant', SessionMode.DEDICATED);
      session.status = ConnectionStatus.CONNECTED;
      session.socket = {
        sendMessage: jest.fn().mockResolvedValue({
          key: {} // No id
        })
      } as any;

      const result = await messageSender.sendTextMessage({
        tenantId: 'test-tenant',
        phone: '+1234567890',
        text: 'Test message',
        type: MessageType.TEXT
      });

      expect(result.success).toBe(false);
      expect(result.error).toContain('no message ID');
    });
  });

  describe('Timeout Handling', () => {
    it('should timeout if send takes too long', async () => {
      const session = await sessionManager.getOrCreateSession('test-tenant', SessionMode.DEDICATED);
      session.status = ConnectionStatus.CONNECTED;
      session.socket = {
        sendMessage: jest.fn().mockImplementation(() => 
          new Promise(resolve => setTimeout(resolve, 200))
        )
      } as any;

      const result = await messageSender.sendTextMessage({
        tenantId: 'test-tenant',
        phone: '+1234567890',
        text: 'Test message',
        type: MessageType.TEXT,
        timeout: 50
      });

      expect(result.success).toBe(false);
      expect(result.error).toContain('timeout');
    }, 10000);

    it('should use default timeout if not specified', async () => {
      const session = await sessionManager.getOrCreateSession('test-tenant', SessionMode.DEDICATED);
      session.status = ConnectionStatus.CONNECTED;
      session.socket = {
        sendMessage: jest.fn().mockResolvedValue({
          key: { id: 'msg-123' }
        })
      } as any;

      const result = await messageSender.sendTextMessage({
        tenantId: 'test-tenant',
        phone: '+1234567890',
        text: 'Test message',
        type: MessageType.TEXT
        // No timeout specified
      });

      expect(result.success).toBe(true);
    });
  });

  describe('Message Types', () => {
    it('should handle TEXT message type', async () => {
      const session = await sessionManager.getOrCreateSession('test-tenant', SessionMode.DEDICATED);
      session.status = ConnectionStatus.CONNECTED;
      session.socket = {
        sendMessage: jest.fn().mockResolvedValue({
          key: { id: 'msg-123' }
        })
      } as any;

      const result = await messageSender.sendMessage({
        tenantId: 'test-tenant',
        phone: '+1234567890',
        text: 'Test',
        type: MessageType.TEXT
      });

      expect(result.success).toBe(true);
    });

    it('should return not implemented for MEDIA type', async () => {
      const result = await messageSender.sendMessage({
        tenantId: 'test-tenant',
        phone: '+1234567890',
        text: 'Test',
        type: MessageType.MEDIA
      });

      expect(result.success).toBe(false);
      expect(result.error).toContain('not yet implemented');
    });

    it('should return not implemented for TEMPLATE type', async () => {
      const result = await messageSender.sendMessage({
        tenantId: 'test-tenant',
        phone: '+1234567890',
        text: 'Test',
        type: MessageType.TEMPLATE
      });

      expect(result.success).toBe(false);
      expect(result.error).toContain('not yet implemented');
    });
  });

  describe('Edge Cases', () => {
    it('should handle empty message text', async () => {
      const session = await sessionManager.getOrCreateSession('test-tenant', SessionMode.DEDICATED);
      session.status = ConnectionStatus.CONNECTED;
      session.socket = {
        sendMessage: jest.fn().mockResolvedValue({
          key: { id: 'msg-123' }
        })
      } as any;

      const result = await messageSender.sendTextMessage({
        tenantId: 'test-tenant',
        phone: '+1234567890',
        text: '',
        type: MessageType.TEXT
      });

      // Should still attempt to send
      expect(result.success).toBe(true);
    });

    it('should handle very long message text', async () => {
      const session = await sessionManager.getOrCreateSession('test-tenant', SessionMode.DEDICATED);
      session.status = ConnectionStatus.CONNECTED;
      session.socket = {
        sendMessage: jest.fn().mockResolvedValue({
          key: { id: 'msg-123' }
        })
      } as any;

      const longText = 'a'.repeat(10000);
      const result = await messageSender.sendTextMessage({
        tenantId: 'test-tenant',
        phone: '+1234567890',
        text: longText,
        type: MessageType.TEXT
      });

      expect(result.success).toBe(true);
    });

    it('should handle special characters in message', async () => {
      const session = await sessionManager.getOrCreateSession('test-tenant', SessionMode.DEDICATED);
      session.status = ConnectionStatus.CONNECTED;
      session.socket = {
        sendMessage: jest.fn().mockResolvedValue({
          key: { id: 'msg-123' }
        })
      } as any;

      const result = await messageSender.sendTextMessage({
        tenantId: 'test-tenant',
        phone: '+1234567890',
        text: '!@#$%^&*()_+-=[]{}|;:,.<>?',
        type: MessageType.TEXT
      });

      expect(result.success).toBe(true);
    });

    it('should handle unicode characters', async () => {
      const session = await sessionManager.getOrCreateSession('test-tenant', SessionMode.DEDICATED);
      session.status = ConnectionStatus.CONNECTED;
      session.socket = {
        sendMessage: jest.fn().mockResolvedValue({
          key: { id: 'msg-123' }
        })
      } as any;

      const result = await messageSender.sendTextMessage({
        tenantId: 'test-tenant',
        phone: '+1234567890',
        text: '你好世界 🌍 مرحبا بالعالم',
        type: MessageType.TEXT
      });

      expect(result.success).toBe(true);
    });
  });
});
