/**
 * Unit Tests for Session Routes
 * 
 * Tests all session management endpoints with various scenarios.
 */

import request from 'supertest';
import express, { Express } from 'express';
import { SessionManager } from '../../services/SessionManager';
import { createSessionRoutes } from '../sessions';
import { SessionMode, ConnectionStatus } from '../../models/Session';

// Mock dependencies
jest.mock('redis', () => ({
  createClient: jest.fn(() => ({
    connect: jest.fn().mockResolvedValue(undefined),
    quit: jest.fn().mockResolvedValue(undefined),
    get: jest.fn().mockResolvedValue(null),
    set: jest.fn().mockResolvedValue('OK'),
    del: jest.fn().mockResolvedValue(1),
    exists: jest.fn().mockResolvedValue(0)
  }))
}));

jest.mock('@whiskeysockets/baileys', () => ({
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
    ubuntu: jest.fn(() => ['Ubuntu', '20.04', '1.0'])
  }
}));

jest.mock('qrcode', () => ({
  toDataURL: jest.fn().mockResolvedValue('data:image/png;base64,mockQRCode')
}));

jest.mock('../../utils/logger', () => ({
  createLogger: jest.fn(() => ({
    info: jest.fn(),
    debug: jest.fn(),
    warn: jest.fn(),
    error: jest.fn()
  }))
}));

describe('Session Routes', () => {
  let app: Express;
  let sessionManager: SessionManager;

  beforeEach(async () => {
    sessionManager = new SessionManager();
    await sessionManager.initialize();

    app = express();
    app.use(express.json());
    app.use('/sessions', createSessionRoutes(sessionManager));
  });

  afterEach(async () => {
    await sessionManager.shutdown();
  });

  describe('POST /sessions/:tenantId/connect', () => {
    it('should create a new session', async () => {
      const response = await request(app)
        .post('/sessions/test-tenant-1/connect')
        .send({ mode: 'DEDICATED' })
        .expect(200);

      expect(response.body.success).toBe(true);
      expect(response.body.session).toBeDefined();
      expect(response.body.session.tenantId).toBe('test-tenant-1');
      expect(response.body.session.mode).toBe('DEDICATED');
    });

    it('should return existing session if already exists', async () => {
      // Create session first
      await request(app)
        .post('/sessions/test-tenant-2/connect')
        .send({ mode: 'DEDICATED' });

      // Request again
      const response = await request(app)
        .post('/sessions/test-tenant-2/connect')
        .send({ mode: 'DEDICATED' })
        .expect(200);

      expect(response.body.success).toBe(true);
      expect(response.body.session.tenantId).toBe('test-tenant-2');
    });

    it('should default to DEDICATED mode if not specified', async () => {
      const response = await request(app)
        .post('/sessions/test-tenant-3/connect')
        .send({})
        .expect(200);

      expect(response.body.session.mode).toBe('DEDICATED');
    });

    it('should handle POOL mode', async () => {
      const response = await request(app)
        .post('/sessions/test-tenant-4/connect')
        .send({ mode: 'POOL' })
        .expect(200);

      expect(response.body.session.mode).toBe('POOL');
    });

    it('should reject empty tenant ID', async () => {
      const response = await request(app)
        .post('/sessions/ /connect')
        .send({ mode: 'DEDICATED' })
        .expect(400);

      expect(response.body.success).toBe(false);
      expect(response.body.error).toContain('Tenant ID is required');
    });
  });

  describe('GET /sessions/:tenantId/qr', () => {
    it('should return QR code if available', async () => {
      // Create session with QR code
      const session = await sessionManager.getOrCreateSession('test-tenant-5', SessionMode.DEDICATED);
      session.qrCode = 'data:image/png;base64,testQRCode';
      session.status = ConnectionStatus.QR_PENDING;

      const response = await request(app)
        .get('/sessions/test-tenant-5/qr')
        .expect(200);

      expect(response.body.success).toBe(true);
      expect(response.body.qrCode).toBe('data:image/png;base64,testQRCode');
      expect(response.body.expiresIn).toBe(60);
    });

    it('should return 404 if session not found', async () => {
      const response = await request(app)
        .get('/sessions/non-existent-tenant/qr')
        .expect(404);

      expect(response.body.success).toBe(false);
      expect(response.body.error).toContain('Session not found');
    });

    it('should reject QR request for POOL sessions', async () => {
      await sessionManager.getOrCreateSession('test-tenant-6', SessionMode.POOL);

      const response = await request(app)
        .get('/sessions/test-tenant-6/qr')
        .expect(400);

      expect(response.body.success).toBe(false);
      expect(response.body.error).toContain('not available for pool sessions');
    });

    it('should return 404 if QR code not available', async () => {
      await sessionManager.getOrCreateSession('test-tenant-7', SessionMode.DEDICATED);

      const response = await request(app)
        .get('/sessions/test-tenant-7/qr')
        .expect(404);

      expect(response.body.success).toBe(false);
      expect(response.body.error).toContain('QR code not available');
    });

    it('should reject empty tenant ID', async () => {
      const response = await request(app)
        .get('/sessions/ /qr')
        .expect(400);

      expect(response.body.success).toBe(false);
      expect(response.body.error).toContain('Tenant ID is required');
    });
  });

  describe('GET /sessions/:tenantId/status', () => {
    it('should return session status', async () => {
      const session = await sessionManager.getOrCreateSession('test-tenant-8', SessionMode.DEDICATED);

      const response = await request(app)
        .get('/sessions/test-tenant-8/status')
        .expect(200);

      expect(response.body.success).toBe(true);
      expect(response.body.status).toBe(session.status);
      expect(response.body.lastUsedAt).toBeDefined();
    });

    it('should return 404 if session not found', async () => {
      const response = await request(app)
        .get('/sessions/non-existent-tenant/status')
        .expect(404);

      expect(response.body.success).toBe(false);
      expect(response.body.error).toContain('Session not found');
    });

    it('should reject empty tenant ID', async () => {
      const response = await request(app)
        .get('/sessions/ /status')
        .expect(400);

      expect(response.body.success).toBe(false);
      expect(response.body.error).toContain('Tenant ID is required');
    });
  });

  describe('POST /sessions/:tenantId/disconnect', () => {
    it('should disconnect existing session', async () => {
      await sessionManager.getOrCreateSession('test-tenant-9', SessionMode.DEDICATED);

      const response = await request(app)
        .post('/sessions/test-tenant-9/disconnect')
        .expect(200);

      expect(response.body.success).toBe(true);

      // Verify session is removed
      const session = sessionManager.getSession('test-tenant-9');
      expect(session).toBeUndefined();
    });

    it('should handle disconnecting non-existent session gracefully', async () => {
      const response = await request(app)
        .post('/sessions/non-existent-tenant/disconnect')
        .expect(200);

      expect(response.body.success).toBe(true);
    });

    it('should reject empty tenant ID', async () => {
      const response = await request(app)
        .post('/sessions/ /disconnect')
        .expect(400);

      expect(response.body.success).toBe(false);
      expect(response.body.error).toContain('Tenant ID is required');
    });
  });
});
