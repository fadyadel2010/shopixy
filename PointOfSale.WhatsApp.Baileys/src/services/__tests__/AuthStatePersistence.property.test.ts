/**
 * Property-Based Tests for AuthStatePersistence
 * 
 * These tests validate universal correctness properties for auth state
 * encryption, storage, and retrieval using fast-check.
 */

import fc from 'fast-check';
import { AuthStatePersistence } from '../AuthStatePersistence';
import { createClient, RedisClientType } from 'redis';

jest.mock('@whiskeysockets/baileys', () => ({
  BufferJSON: {
    replacer: (_key: string, value: unknown) => value,
    reviver: (_key: string, value: unknown) => value
  }
}));

// Mock Redis
jest.mock('redis', () => {
  const mockData = new Map<string, string>();
  
  return {
    createClient: jest.fn(() => ({
      connect: jest.fn().mockResolvedValue(undefined),
      quit: jest.fn().mockResolvedValue(undefined),
      get: jest.fn((key: string) => Promise.resolve(mockData.get(key) || null)),
      set: jest.fn((key: string, value: string, _options?: any) => {
        mockData.set(key, value);
        return Promise.resolve('OK');
      }),
      del: jest.fn((key: string) => {
        mockData.delete(key);
        return Promise.resolve(1);
      }),
      exists: jest.fn((key: string) => Promise.resolve(mockData.has(key) ? 1 : 0))
    }))
  };
});

describe('AuthStatePersistence Property Tests', () => {
  let redis: RedisClientType;
  let authPersistence: AuthStatePersistence;
  const encryptionSecret = 'test-encryption-secret-minimum-32-characters-long';

  beforeEach(async () => {
    redis = createClient();
    await redis.connect();
    authPersistence = new AuthStatePersistence(redis, encryptionSecret);
  });

  afterEach(async () => {
    await redis.quit();
  });

  /**
   * Property 6: Auth State Persistence Round-Trip
   * 
   * For any authenticated session, persisting the auth state to Redis and then 
   * retrieving it should produce equivalent authentication data 
   * (encryption/decryption round-trip).
   * 
   * Validates: Requirements 2.5, 9.1, 9.5
   */
  describe('Property 6: Auth State Persistence Round-Trip', () => {
    // Custom generator for auth state data
    const authStateGenerator = fc.record({
      creds: fc.record({
        noiseKey: fc.record({
          private: fc.uint8Array({ minLength: 32, maxLength: 32 }),
          public: fc.uint8Array({ minLength: 32, maxLength: 32 })
        }),
        signedIdentityKey: fc.record({
          private: fc.uint8Array({ minLength: 32, maxLength: 32 }),
          public: fc.uint8Array({ minLength: 32, maxLength: 32 })
        }),
        signedPreKey: fc.record({
          keyPair: fc.record({
            private: fc.uint8Array({ minLength: 32, maxLength: 32 }),
            public: fc.uint8Array({ minLength: 32, maxLength: 32 })
          }),
          keyId: fc.integer({ min: 1, max: 1000000 })
        }),
        registrationId: fc.integer({ min: 1, max: 16777215 }),
        advSecretKey: fc.string({ minLength: 32, maxLength: 32 }),
        me: fc.record({
          id: fc.string({ minLength: 10, maxLength: 20 }),
          name: fc.string({ minLength: 1, maxLength: 50 })
        })
      }),
      keys: fc.record({
        preKeys: fc.dictionary(
          fc.integer({ min: 1, max: 100 }).map(String),
          fc.record({
            private: fc.uint8Array({ minLength: 32, maxLength: 32 }),
            public: fc.uint8Array({ minLength: 32, maxLength: 32 })
          })
        ),
        sessions: fc.dictionary(
          fc.string({ minLength: 10, maxLength: 30 }),
          fc.object()
        ),
        senderKeys: fc.dictionary(
          fc.string({ minLength: 10, maxLength: 30 }),
          fc.object()
        )
      })
    });

    it('should preserve auth state through save and load cycle', async () => {
      // Feature: whatsapp-baileys-migration, Property 6: Auth State Persistence Round-Trip
      await fc.assert(
        fc.asyncProperty(
          fc.uuid(), // tenantId
          authStateGenerator,
          async (tenantId, authState) => {
            // Save auth state
            await authPersistence.saveAuthState(tenantId, authState);

            // Load auth state
            const loaded = await authPersistence.loadAuthState(tenantId);

            // Should not be null
            expect(loaded).not.toBeNull();

            // Should match original (deep equality)
            expect(JSON.stringify(loaded)).toBe(JSON.stringify(authState));
          }
        ),
        { numRuns: 100 }
      );
    });

    it('should handle multiple save/load cycles', async () => {
      await fc.assert(
        fc.asyncProperty(
          fc.uuid(),
          fc.array(authStateGenerator, { minLength: 1, maxLength: 5 }),
          async (tenantId, authStates) => {
            // Save and load multiple times
            for (const authState of authStates) {
              await authPersistence.saveAuthState(tenantId, authState);
              const loaded = await authPersistence.loadAuthState(tenantId);
              
              expect(loaded).not.toBeNull();
              expect(JSON.stringify(loaded)).toBe(JSON.stringify(authState));
            }
          }
        ),
        { numRuns: 50 }
      );
    });

    it('should handle concurrent save operations for different tenants', async () => {
      await fc.assert(
        fc.asyncProperty(
          fc.array(
            fc.tuple(fc.uuid(), authStateGenerator),
            { minLength: 2, maxLength: 10 }
          ),
          async (tenantAuthPairs) => {
            // Ensure unique tenant IDs
            const uniquePairs = Array.from(
              new Map(tenantAuthPairs.map(([id, auth]) => [id, auth])).entries()
            );

            // Save all concurrently
            await Promise.all(
              uniquePairs.map(([tenantId, authState]) =>
                authPersistence.saveAuthState(tenantId, authState)
              )
            );

            // Load all and verify
            for (const [tenantId, originalAuth] of uniquePairs) {
              const loaded = await authPersistence.loadAuthState(tenantId);
              expect(loaded).not.toBeNull();
              expect(JSON.stringify(loaded)).toBe(JSON.stringify(originalAuth));
            }
          }
        ),
        { numRuns: 30 }
      );
    });
  });

  /**
   * Property 24: Session Metadata Completeness
   * 
   * For any session, all required metadata fields should be present and valid
   * after persistence.
   * 
   * Validates: Requirements 9.3
   */
  describe('Property 24: Session Metadata Completeness', () => {
    it('should preserve all auth state fields', async () => {
      await fc.assert(
        fc.asyncProperty(
          fc.uuid(),
          fc.record({
            creds: fc.object(),
            keys: fc.object()
          }),
          async (tenantId, authState) => {
            await authPersistence.saveAuthState(tenantId, authState);
            const loaded = await authPersistence.loadAuthState(tenantId);

            expect(loaded).not.toBeNull();
            expect(loaded).toHaveProperty('creds');
            expect(loaded).toHaveProperty('keys');
          }
        ),
        { numRuns: 100 }
      );
    });
  });

  /**
   * Property 25: Corrupted Auth State Handling
   * 
   * For any corrupted or invalid auth state, the system should handle it
   * gracefully by returning null and cleaning up the corrupted data.
   * 
   * Validates: Requirements 9.4
   */
  describe('Property 25: Corrupted Auth State Handling', () => {
    it('should return null for non-existent auth state', async () => {
      await fc.assert(
        fc.asyncProperty(
          fc.uuid(),
          async (tenantId) => {
            const loaded = await authPersistence.loadAuthState(tenantId);
            expect(loaded).toBeNull();
          }
        ),
        { numRuns: 50 }
      );
    });

    it('should handle corrupted JSON gracefully', async () => {
      await fc.assert(
        fc.asyncProperty(
          fc.uuid(),
          async (tenantId) => {
            // Manually insert corrupted data
            const key = `whatsapp:auth:${tenantId}`;
            await redis.set(key, 'corrupted-json-data', { EX: 3600 });

            // Should return null and clean up
            const loaded = await authPersistence.loadAuthState(tenantId);
            expect(loaded).toBeNull();

            // Should have deleted the corrupted data
            const exists = await authPersistence.hasAuthState(tenantId);
            expect(exists).toBe(false);
          }
        ),
        { numRuns: 50 }
      );
    });

    it('should handle invalid encryption data gracefully', async () => {
      await fc.assert(
        fc.asyncProperty(
          fc.uuid(),
          async (tenantId) => {
            // Manually insert invalid encrypted data
            const key = `whatsapp:auth:${tenantId}`;
            const invalidData = JSON.stringify({
              iv: 'invalid-iv',
              encryptedData: 'invalid-data',
              version: 1
            });
            await redis.set(key, invalidData, { EX: 3600 });

            // Should return null and clean up
            const loaded = await authPersistence.loadAuthState(tenantId);
            expect(loaded).toBeNull();
          }
        ),
        { numRuns: 50 }
      );
    });
  });

  /**
   * Property 26: Disconnect Cleanup Persistence
   * 
   * For any session that is explicitly disconnected, the auth state should
   * be removed from Redis.
   * 
   * Validates: Requirements 9.6
   */
  describe('Property 26: Disconnect Cleanup Persistence', () => {
    it('should delete auth state on explicit disconnect', async () => {
      await fc.assert(
        fc.asyncProperty(
          fc.uuid(),
          fc.record({
            creds: fc.object(),
            keys: fc.object()
          }),
          async (tenantId, authState) => {
            // Save auth state
            await authPersistence.saveAuthState(tenantId, authState);

            // Verify it exists
            let exists = await authPersistence.hasAuthState(tenantId);
            expect(exists).toBe(true);

            // Delete auth state (simulating disconnect)
            await authPersistence.deleteAuthState(tenantId);

            // Verify it's deleted
            exists = await authPersistence.hasAuthState(tenantId);
            expect(exists).toBe(false);

            // Loading should return null
            const loaded = await authPersistence.loadAuthState(tenantId);
            expect(loaded).toBeNull();
          }
        ),
        { numRuns: 100 }
      );
    });

    it('should handle deleting non-existent auth state', async () => {
      await fc.assert(
        fc.asyncProperty(
          fc.uuid(),
          async (tenantId) => {
            // Delete non-existent auth state should not throw
            await expect(
              authPersistence.deleteAuthState(tenantId)
            ).resolves.not.toThrow();
          }
        ),
        { numRuns: 50 }
      );
    });
  });

  /**
   * Additional Property: Encryption Uniqueness
   * 
   * Encrypting the same data multiple times should produce different
   * ciphertext (due to random IV).
   */
  describe('Property: Encryption Uniqueness', () => {
    it('should produce different ciphertext for same plaintext', async () => {
      await fc.assert(
        fc.asyncProperty(
          fc.uuid(),
          fc.record({
            creds: fc.object(),
            keys: fc.object()
          }),
          async (tenantId, authState) => {
            // Save twice
            await authPersistence.saveAuthState(tenantId, authState);
            const encrypted1 = await redis.get(`whatsapp:auth:${tenantId}`);

            await authPersistence.saveAuthState(tenantId, authState);
            const encrypted2 = await redis.get(`whatsapp:auth:${tenantId}`);

            // Encrypted data should be different (different IV)
            expect(encrypted1).not.toBe(encrypted2);

            // But decrypted data should be the same
            const loaded = await authPersistence.loadAuthState(tenantId);
            expect(JSON.stringify(loaded)).toBe(JSON.stringify(authState));
          }
        ),
        { numRuns: 50 }
      );
    });
  });

  /**
   * Additional Property: Tenant Isolation
   * 
   * Auth state for different tenants should be completely isolated.
   */
  describe('Property: Tenant Isolation', () => {
    it('should isolate auth state between tenants', async () => {
      await fc.assert(
        fc.asyncProperty(
          fc.uuid(),
          fc.uuid(),
          fc.record({ creds: fc.object(), keys: fc.object() }),
          fc.record({ creds: fc.object(), keys: fc.object() }),
          async (tenantId1, tenantId2, authState1, authState2) => {
            // Skip if tenant IDs are the same
            fc.pre(tenantId1 !== tenantId2);

            // Save different auth states for different tenants
            await authPersistence.saveAuthState(tenantId1, authState1);
            await authPersistence.saveAuthState(tenantId2, authState2);

            // Load and verify isolation
            const loaded1 = await authPersistence.loadAuthState(tenantId1);
            const loaded2 = await authPersistence.loadAuthState(tenantId2);

            expect(JSON.stringify(loaded1)).toBe(JSON.stringify(authState1));
            expect(JSON.stringify(loaded2)).toBe(JSON.stringify(authState2));
            expect(JSON.stringify(loaded1)).not.toBe(JSON.stringify(loaded2));
          }
        ),
        { numRuns: 50 }
      );
    });
  });
});
