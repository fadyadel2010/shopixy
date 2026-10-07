/**
 * Property-Based Tests for MessageSender
 * 
 * These tests validate universal correctness properties for message sending
 * using fast-check.
 */

import fc from 'fast-check';
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

describe('MessageSender Property Tests', () => {
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

  /**
   * Property 7: Phone Number Validation
   * 
   * For any string that does not match E.164 format (e.g., missing +, invalid 
   * length, non-numeric), the Baileys service should reject the message send 
   * request with a validation error.
   * 
   * Validates: Requirements 3.1
   */
  describe('Property 7: Phone Number Validation', () => {
    // Generator for invalid phone numbers
    const invalidPhoneGenerator = fc.oneof(
      fc.constant(''), // Empty
      fc.constant('1234567890'), // Missing +
      fc.constant('+'), // Just +
      fc.constant('+0123456789'), // Starts with 0
      fc.constant('+abc123'), // Contains letters
      fc.constant('+1'), // Too short
      fc.constant('+' + '1'.repeat(16)), // Too long (>15 digits)
      fc.string().filter(s => !/^\+[1-9]\d{1,14}$/.test(s)), // Random invalid
      fc.constant('123-456-7890'), // Contains dashes
      fc.constant('+1 234 567 890'), // Contains spaces
      fc.constant('(123) 456-7890') // Contains parentheses
    );

    it('should reject invalid phone numbers', async () => {
      // Feature: whatsapp-baileys-migration, Property 7: Phone Number Validation
      await fc.assert(
        fc.asyncProperty(
          fc.uuid(), // tenantId
          invalidPhoneGenerator,
          fc.string({ minLength: 1, maxLength: 100 }), // message text
          async (tenantId, phone, text) => {
            const result = await messageSender.sendTextMessage({
              tenantId,
              phone,
              text,
              type: MessageType.TEXT
            });

            // Should fail
            expect(result.success).toBe(false);
            // Should have error message
            expect(result.error).toBeDefined();
            expect(result.error).toBeTruthy();
            // Should not have message ID
            expect(result.messageId).toBeUndefined();
          }
        ),
        { numRuns: 100 }
      );
    });

    // Generator for valid E.164 phone numbers
    const validPhoneGenerator = fc.tuple(
      fc.constantFrom('1', '44', '55', '91', '86', '81', '49', '33', '39', '7'),
      fc.integer({ min: 100000000, max: 9999999999 })
    ).map(([countryCode, number]) => `+${countryCode}${number}`);

    it('should accept valid E.164 phone numbers', async () => {
      await fc.assert(
        fc.asyncProperty(
          fc.uuid(),
          validPhoneGenerator,
          fc.string({ minLength: 1, maxLength: 100 }),
          async (tenantId, phone, text) => {
            // Create a connected session with mock socket
            const session = await sessionManager.getOrCreateSession(tenantId, SessionMode.DEDICATED);
            session.status = ConnectionStatus.CONNECTED;
            session.socket = {
              sendMessage: jest.fn().mockResolvedValue({
                key: { id: 'mock-message-id-123' }
              })
            } as any;

            const result = await messageSender.sendTextMessage({
              tenantId,
              phone,
              text,
              type: MessageType.TEXT
            });

            // Phone validation should pass (though send might fail for other reasons)
            // If it fails, it should not be due to phone validation
            if (!result.success && result.error) {
              expect(result.error).not.toContain('Invalid phone number');
              expect(result.error).not.toContain('E.164');
            }
          }
        ),
        { numRuns: 50 }
      );
    });
  });

  /**
   * Property 8: Successful Send Includes Message ID
   * 
   * For any successful message send operation, the response should include 
   * a non-null, non-empty message ID.
   * 
   * Validates: Requirements 3.3
   */
  describe('Property 8: Successful Send Includes Message ID', () => {
    it('should include message ID on successful send', async () => {
      // Feature: whatsapp-baileys-migration, Property 8: Successful Send Includes Message ID
      await fc.assert(
        fc.asyncProperty(
          fc.uuid(),
          fc.tuple(
            fc.constantFrom('1', '44', '55'),
            fc.integer({ min: 1000000000, max: 9999999999 })
          ).map(([cc, num]) => `+${cc}${num}`),
          fc.string({ minLength: 1, maxLength: 100 }),
          fc.string({ minLength: 10, maxLength: 50 }), // message ID
          async (tenantId, phone, text, messageId) => {
            // Create connected session with mock socket
            const session = await sessionManager.getOrCreateSession(tenantId, SessionMode.DEDICATED);
            session.status = ConnectionStatus.CONNECTED;
            session.socket = {
              sendMessage: jest.fn().mockResolvedValue({
                key: { id: messageId }
              })
            } as any;

            const result = await messageSender.sendTextMessage({
              tenantId,
              phone,
              text,
              type: MessageType.TEXT
            });

            // If successful, must have message ID
            if (result.success) {
              expect(result.messageId).toBeDefined();
              expect(result.messageId).toBeTruthy();
              expect(typeof result.messageId).toBe('string');
              expect(result.messageId!.length).toBeGreaterThan(0);
              expect(result.messageId).toBe(messageId);
            }
          }
        ),
        { numRuns: 100 }
      );
    });

    it('should not include message ID on failed send', async () => {
      await fc.assert(
        fc.asyncProperty(
          fc.uuid(),
          fc.tuple(
            fc.constantFrom('1', '44', '55'),
            fc.integer({ min: 1000000000, max: 9999999999 })
          ).map(([cc, num]) => `+${cc}${num}`),
          fc.string({ minLength: 1, maxLength: 100 }),
          async (tenantId, phone, text) => {
            // Create connected session with mock socket that fails
            const session = await sessionManager.getOrCreateSession(tenantId, SessionMode.DEDICATED);
            session.status = ConnectionStatus.CONNECTED;
            session.socket = {
              sendMessage: jest.fn().mockRejectedValue(new Error('Send failed'))
            } as any;

            const result = await messageSender.sendTextMessage({
              tenantId,
              phone,
              text,
              type: MessageType.TEXT
            });

            // If failed, should not have message ID
            if (!result.success) {
              expect(result.messageId).toBeUndefined();
            }
          }
        ),
        { numRuns: 50 }
      );
    });
  });

  /**
   * Property 9: Failed Send Includes Error Reason
   * 
   * For any failed message send operation, the response should include 
   * a non-null, non-empty error message describing the failure reason.
   * 
   * Validates: Requirements 3.4
   */
  describe('Property 9: Failed Send Includes Error Reason', () => {
    it('should include error message on failed send', async () => {
      // Feature: whatsapp-baileys-migration, Property 9: Failed Send Includes Error Reason
      await fc.assert(
        fc.asyncProperty(
          fc.uuid(),
          fc.tuple(
            fc.constantFrom('1', '44', '55'),
            fc.integer({ min: 1000000000, max: 9999999999 })
          ).map(([cc, num]) => `+${cc}${num}`),
          fc.string({ minLength: 1, maxLength: 100 }),
          fc.string({ minLength: 5, maxLength: 100 }), // error message
          async (tenantId, phone, text, errorMessage) => {
            // Create connected session with mock socket that fails
            const session = await sessionManager.getOrCreateSession(tenantId, SessionMode.DEDICATED);
            session.status = ConnectionStatus.CONNECTED;
            session.socket = {
              sendMessage: jest.fn().mockRejectedValue(new Error(errorMessage))
            } as any;

            const result = await messageSender.sendTextMessage({
              tenantId,
              phone,
              text,
              type: MessageType.TEXT
            });

            // If failed, must have error message
            if (!result.success) {
              expect(result.error).toBeDefined();
              expect(result.error).toBeTruthy();
              expect(typeof result.error).toBe('string');
              expect(result.error!.length).toBeGreaterThan(0);
            }
          }
        ),
        { numRuns: 100 }
      );
    });

    it('should include descriptive error when a missing session cannot reconnect', async () => {
      const connectSpy = jest.spyOn(sessionManager, 'connectSession');

      await fc.assert(
        fc.asyncProperty(
          fc.uuid(),
          fc.tuple(
            fc.constantFrom('1', '44', '55'),
            fc.integer({ min: 1000000000, max: 9999999999 })
          ).map(([cc, num]) => `+${cc}${num}`),
          fc.string({ minLength: 1, maxLength: 100 }),
          async (tenantId, phone, text) => {
            connectSpy.mockRejectedValueOnce(new Error('Session not connected'));
            const result = await messageSender.sendTextMessage({
              tenantId,
              phone,
              text,
              type: MessageType.TEXT
            });
            await sessionManager.closeSession(tenantId);

            expect(result.success).toBe(false);
            expect(result.error).toBeDefined();
            expect(result.error).toContain('not connected');
          }
        ),
        { numRuns: 50 }
      );
      connectSpy.mockRestore();
    });

    it('should include descriptive error for session not connected', async () => {
      const connectSpy = jest.spyOn(sessionManager, 'connectSession');

      await fc.assert(
        fc.asyncProperty(
          fc.uuid(),
          fc.tuple(
            fc.constantFrom('1', '44', '55'),
            fc.integer({ min: 1000000000, max: 9999999999 })
          ).map(([cc, num]) => `+${cc}${num}`),
          fc.string({ minLength: 1, maxLength: 100 }),
          fc.constantFrom(
            ConnectionStatus.DISCONNECTED,
            ConnectionStatus.QR_PENDING,
            ConnectionStatus.CONNECTING
          ),
          async (tenantId, phone, text, status) => {
            // Create session but not connected
            const session = await sessionManager.getOrCreateSession(tenantId, SessionMode.DEDICATED);
            session.status = status;
            connectSpy.mockRejectedValueOnce(new Error('Session not connected'));

            const result = await messageSender.sendTextMessage({
              tenantId,
              phone,
              text,
              type: MessageType.TEXT
            });
            await sessionManager.closeSession(tenantId);

            expect(result.success).toBe(false);
            expect(result.error).toBeDefined();
            expect(result.error).toContain('not connected');
          }
        ),
        { numRuns: 50 }
      );
      connectSpy.mockRestore();
    });
  });

  /**
   * Additional Property: Timeout Handling
   * 
   * Message sends that exceed the timeout should fail with timeout error.
   */
  describe('Property: Timeout Handling', () => {
    it('should timeout if send takes too long', async () => {
      await fc.assert(
        fc.asyncProperty(
          fc.uuid(),
          fc.tuple(
            fc.constantFrom('1', '44', '55'),
            fc.integer({ min: 1000000000, max: 9999999999 })
          ).map(([cc, num]) => `+${cc}${num}`),
          fc.string({ minLength: 1, maxLength: 100 }),
          fc.integer({ min: 10, max: 100 }), // short timeout
          async (tenantId, phone, text, timeout) => {
            // Create session with slow mock socket
            const session = await sessionManager.getOrCreateSession(tenantId, SessionMode.DEDICATED);
            session.status = ConnectionStatus.CONNECTED;
            session.socket = {
              sendMessage: jest.fn().mockImplementation(() => 
                new Promise(resolve => setTimeout(resolve, timeout + 100))
              )
            } as any;

            const result = await messageSender.sendTextMessage({
              tenantId,
              phone,
              text,
              type: MessageType.TEXT,
              timeout
            });

            expect(result.success).toBe(false);
            expect(result.error).toBeDefined();
            expect(result.error).toContain('timeout');
          }
        ),
        { numRuns: 30 }
      );
    }, 30000); // Increase test timeout
  });
});
