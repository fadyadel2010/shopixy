/**
 * Unit Tests for Message Routes
 * 
 * Tests message sending endpoint with various scenarios.
 */

import request from 'supertest';
import express, { Express } from 'express';
import { SessionManager } from '../../services/SessionManager';
import { MessageSender } from '../../services/MessageSender';
import { PoolManager } from '../../services/PoolManager';
import { createMessageRoutes } from '../messages';
import { MessageType } from '../../models/Message';
import { SessionMode, ConnectionStatus } from '../../models/Session';

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

describe('Message Routes', () => {
  let app: Express;
  let sessionManager: SessionManager;
  let messageSender: MessageSender;

  beforeEach(async () => {
    sessionManager = new SessionManager();
    await sessionManager.initialize();
    messageSender = new MessageSender(
      sessionManager,
      new PoolManager(sessionManager.getRedisClient())
    );

    app = express();
    app.use(express.json());
    app.use('/messages', createMessageRoutes(messageSender));
  });

  afterEach(async () => {
    await sessionManager.shutdown();
  });

  describe('POST /messages/send', () => {
    it('should send message successfully', async () => {
      // Create connected session with mock socket
      const session = await sessionManager.getOrCreateSession('test-tenant-1', SessionMode.DEDICATED);
      session.status = ConnectionStatus.CONNECTED;
      session.socket = {
        sendMessage: jest.fn().mockResolvedValue({
          key: { id: 'message-id-123' }
        })
      } as any;

      const response = await request(app)
        .post('/messages/send')
        .send({
          tenantId: 'test-tenant-1',
          phone: '+1234567890',
          text: 'Hello World',
          type: MessageType.TEXT
        })
        .expect(200);

      expect(response.body.success).toBe(true);
      expect(response.body.messageId).toBe('message-id-123');
    });

    it('should reject missing tenant ID', async () => {
      const response = await request(app)
        .post('/messages/send')
        .send({
          phone: '+1234567890',
          text: 'Hello World'
        })
        .expect(400);

      expect(response.body.success).toBe(false);
      expect(response.body.error).toContain('Tenant ID is required');
    });

    it('should reject empty tenant ID', async () => {
      const response = await request(app)
        .post('/messages/send')
        .send({
          tenantId: '  ',
          phone: '+1234567890',
          text: 'Hello World'
        })
        .expect(400);

      expect(response.body.success).toBe(false);
      expect(response.body.error).toContain('Tenant ID is required');
    });

    it('should reject missing phone number', async () => {
      const response = await request(app)
        .post('/messages/send')
        .send({
          tenantId: 'test-tenant-2',
          text: 'Hello World'
        })
        .expect(400);

      expect(response.body.success).toBe(false);
      expect(response.body.error).toContain('Phone number is required');
    });

    it('should reject empty phone number', async () => {
      const response = await request(app)
        .post('/messages/send')
        .send({
          tenantId: 'test-tenant-2',
          phone: '  ',
          text: 'Hello World'
        })
        .expect(400);

      expect(response.body.success).toBe(false);
      expect(response.body.error).toContain('Phone number is required');
    });

    it('should reject missing message text', async () => {
      const response = await request(app)
        .post('/messages/send')
        .send({
          tenantId: 'test-tenant-2',
          phone: '+1234567890'
        })
        .expect(400);

      expect(response.body.success).toBe(false);
      expect(response.body.error).toContain('Message text is required');
    });

    it('should reject empty message text', async () => {
      const response = await request(app)
        .post('/messages/send')
        .send({
          tenantId: 'test-tenant-2',
          phone: '+1234567890',
          text: '  '
        })
        .expect(400);

      expect(response.body.success).toBe(false);
      expect(response.body.error).toContain('Message text is required');
    });

    it('should reject invalid message type', async () => {
      const response = await request(app)
        .post('/messages/send')
        .send({
          tenantId: 'test-tenant-2',
          phone: '+1234567890',
          text: 'Hello',
          type: 'INVALID_TYPE'
        })
        .expect(400);

      expect(response.body.success).toBe(false);
      expect(response.body.error).toContain('Invalid message type');
    });

    it('should default to TEXT type if not specified', async () => {
      const session = await sessionManager.getOrCreateSession('test-tenant-3', SessionMode.DEDICATED);
      session.status = ConnectionStatus.CONNECTED;
      session.socket = {
        sendMessage: jest.fn().mockResolvedValue({
          key: { id: 'msg-123' }
        })
      } as any;

      const response = await request(app)
        .post('/messages/send')
        .send({
          tenantId: 'test-tenant-3',
          phone: '+1234567890',
          text: 'Hello'
          // No type specified
        })
        .expect(200);

      expect(response.body.success).toBe(true);
    });

    it('should return 400 for invalid phone number', async () => {
      await sessionManager.getOrCreateSession('test-tenant-4', SessionMode.DEDICATED);

      const response = await request(app)
        .post('/messages/send')
        .send({
          tenantId: 'test-tenant-4',
          phone: 'invalid-phone',
          text: 'Hello'
        })
        .expect(400);

      expect(response.body.success).toBe(false);
      expect(response.body.error).toContain('E.164');
    });

    it('should return 400 if a missing session cannot reconnect', async () => {
      const connectSpy = jest.spyOn(sessionManager, 'connectSession')
        .mockRejectedValueOnce(new Error('Session not connected'));

      const response = await request(app)
        .post('/messages/send')
        .send({
          tenantId: 'non-existent-tenant',
          phone: '+1234567890',
          text: 'Hello'
        })
        .expect(400);

      expect(response.body.success).toBe(false);
      expect(response.body.error).toContain('not connected');
      connectSpy.mockRestore();
    });

    it('should return 400 if session not connected', async () => {
      await sessionManager.getOrCreateSession('test-tenant-5', SessionMode.DEDICATED);

      const response = await request(app)
        .post('/messages/send')
        .send({
          tenantId: 'test-tenant-5',
          phone: '+1234567890',
          text: 'Hello'
        })
        .expect(400);

      expect(response.body.success).toBe(false);
      expect(response.body.error).toContain('not connected');
    });

    it('should handle send failure', async () => {
      const session = await sessionManager.getOrCreateSession('test-tenant-6', SessionMode.DEDICATED);
      session.status = ConnectionStatus.CONNECTED;
      session.socket = {
        sendMessage: jest.fn().mockRejectedValue(new Error('Network error'))
      } as any;

      const response = await request(app)
        .post('/messages/send')
        .send({
          tenantId: 'test-tenant-6',
          phone: '+1234567890',
          text: 'Hello'
        })
        .expect(400);

      expect(response.body.success).toBe(false);
      expect(response.body.error).toContain('Network error');
    });

    it('should handle timeout (5 seconds)', async () => {
      const session = await sessionManager.getOrCreateSession('test-tenant-7', SessionMode.DEDICATED);
      session.status = ConnectionStatus.CONNECTED;
      session.socket = {
        sendMessage: jest.fn().mockImplementation(() => 
          new Promise(resolve => setTimeout(resolve, 6000))
        )
      } as any;

      const response = await request(app)
        .post('/messages/send')
        .send({
          tenantId: 'test-tenant-7',
          phone: '+1234567890',
          text: 'Hello'
        })
        .expect(400);

      expect(response.body.success).toBe(false);
      expect(response.body.error).toContain('timeout');
    }, 10000);

    it('should accept all valid message types', async () => {
      const session = await sessionManager.getOrCreateSession('test-tenant-8', SessionMode.DEDICATED);
      session.status = ConnectionStatus.CONNECTED;
      session.socket = {
        sendMessage: jest.fn().mockResolvedValue({
          key: { id: 'msg-123' }
        })
      } as any;

      // TEXT type
      const textResponse = await request(app)
        .post('/messages/send')
        .send({
          tenantId: 'test-tenant-8',
          phone: '+1234567890',
          text: 'Hello',
          type: MessageType.TEXT
        });

      expect(textResponse.body.success).toBe(true);

      // MEDIA type (not implemented yet, should fail gracefully)
      const mediaResponse = await request(app)
        .post('/messages/send')
        .send({
          tenantId: 'test-tenant-8',
          phone: '+1234567890',
          text: 'Hello',
          type: MessageType.MEDIA
        });

      expect(mediaResponse.body.success).toBe(false);
      expect(mediaResponse.body.error).toContain('not yet implemented');

      // TEMPLATE type (not implemented yet, should fail gracefully)
      const templateResponse = await request(app)
        .post('/messages/send')
        .send({
          tenantId: 'test-tenant-8',
          phone: '+1234567890',
          text: 'Hello',
          type: MessageType.TEMPLATE
        });

      expect(templateResponse.body.success).toBe(false);
      expect(templateResponse.body.error).toContain('not yet implemented');
    });
  });
});
