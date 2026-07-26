/**
 * Unit Tests for AuthStatePersistence
 * 
 * These tests validate specific scenarios and edge cases for auth state
 * encryption, storage, and retrieval.
 */

import { AuthStatePersistence, AuthStateData } from '../AuthStatePersistence';
import { createClient, RedisClientType } from 'redis';

// Mock Redis
jest.mock('redis', () => {
  const mockData = new Map<string, string>();
  
  return {
    createClient: jest.fn(() => ({
      connect: jest.fn().mockResolvedValue(undefined),
      quit: jest.fn().mockResolvedValue(undefined),
      get: jest.fn((key: string) => Promise.resolve(mockData.get(key) || null)),
      set: jest.fn((key: string, value: string, options?: any) => {
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

describe('AuthStatePersistence Unit Tests', () => {
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

  describe('Save Auth State', () => {
    it('should save auth state successfully', async () => {
      const tenantId = 'test-tenant-1';
      const authState: AuthStateData = {
        creds: { test: 'credentials' },
        keys: { test: 'keys' }
      };

      await expect(
        authPersistence.saveAuthState(tenantId, authState)
      ).resolves.not.toThrow();

      const exists = await authPersistence.hasAuthState(tenantId);
      expect(exists).toBe(true);
    });

    it('should overwrite existing auth state', async () => {
      const tenantId = 'test-tenant-2';
      const authState1: AuthStateData = {
        creds: { version: 1 },
        keys: { version: 1 }
      };
      const authState2: AuthStateData = {
        creds: { version: 2 },
        keys: { version: 2 }
      };

      await authPersistence.saveAuthState(tenantId, authState1);
      await authPersistence.saveAuthState(tenantId, authState2);

      const loaded = await authPersistence.loadAuthState(tenantId);
      expect(loaded).toEqual(authState2);
    });

    it('should handle empty auth state', async () => {
      const tenantId = 'test-tenant-3';
      const authState: AuthStateData = {
        creds: {},
        keys: {}
      };

      await authPersistence.saveAuthState(tenantId, authState);
      const loaded = await authPersistence.loadAuthState(tenantId);

      expect(loaded).toEqual(authState);
    });

    it('should handle complex nested auth state', async () => {
      const tenantId = 'test-tenant-4';
      const authState: AuthStateData = {
        creds: {
          noiseKey: { private: Buffer.from('test'), public: Buffer.from('test') },
          signedIdentityKey: { private: Buffer.from('test'), public: Buffer.from('test') },
          registrationId: 12345
        },
        keys: {
          preKeys: { '1': { private: Buffer.from('test'), public: Buffer.from('test') } },
          sessions: { 'session1': { data: 'test' } }
        }
      };

      await authPersistence.saveAuthState(tenantId, authState);
      const loaded = await authPersistence.loadAuthState(tenantId);

      expect(JSON.stringify(loaded)).toBe(JSON.stringify(authState));
    });
  });

  describe('Load Auth State', () => {
    it('should load saved auth state', async () => {
      const tenantId = 'test-tenant-5';
      const authState: AuthStateData = {
        creds: { test: 'credentials' },
        keys: { test: 'keys' }
      };

      await authPersistence.saveAuthState(tenantId, authState);
      const loaded = await authPersistence.loadAuthState(tenantId);

      expect(loaded).toEqual(authState);
    });

    it('should return null for non-existent auth state', async () => {
      const loaded = await authPersistence.loadAuthState('non-existent-tenant');
      expect(loaded).toBeNull();
    });

    it('should return null for corrupted JSON', async () => {
      const tenantId = 'test-tenant-6';
      const key = `whatsapp:auth:${tenantId}`;
      
      await redis.set(key, 'not-valid-json', { EX: 3600 });

      const loaded = await authPersistence.loadAuthState(tenantId);
      expect(loaded).toBeNull();
    });

    it('should return null for invalid encryption format', async () => {
      const tenantId = 'test-tenant-7';
      const key = `whatsapp:auth:${tenantId}`;
      
      const invalidData = JSON.stringify({
        iv: 'short',
        encryptedData: 'invalid',
        version: 1
      });
      await redis.set(key, invalidData, { EX: 3600 });

      const loaded = await authPersistence.loadAuthState(tenantId);
      expect(loaded).toBeNull();
    });

    it('should return null for wrong encryption version', async () => {
      const tenantId = 'test-tenant-8';
      const authState: AuthStateData = {
        creds: { test: 'credentials' },
        keys: { test: 'keys' }
      };

      await authPersistence.saveAuthState(tenantId, authState);

      // Manually modify version
      const key = `whatsapp:auth:${tenantId}`;
      const data = await redis.get(key);
      const parsed = JSON.parse(data!);
      parsed.version = 999;
      await redis.set(key, JSON.stringify(parsed), { EX: 3600 });

      const loaded = await authPersistence.loadAuthState(tenantId);
      expect(loaded).toBeNull();
    });
  });

  describe('Delete Auth State', () => {
    it('should delete existing auth state', async () => {
      const tenantId = 'test-tenant-9';
      const authState: AuthStateData = {
        creds: { test: 'credentials' },
        keys: { test: 'keys' }
      };

      await authPersistence.saveAuthState(tenantId, authState);
      expect(await authPersistence.hasAuthState(tenantId)).toBe(true);

      await authPersistence.deleteAuthState(tenantId);
      expect(await authPersistence.hasAuthState(tenantId)).toBe(false);
    });

    it('should not throw when deleting non-existent auth state', async () => {
      await expect(
        authPersistence.deleteAuthState('non-existent-tenant')
      ).resolves.not.toThrow();
    });
  });

  describe('Has Auth State', () => {
    it('should return true for existing auth state', async () => {
      const tenantId = 'test-tenant-10';
      const authState: AuthStateData = {
        creds: { test: 'credentials' },
        keys: { test: 'keys' }
      };

      await authPersistence.saveAuthState(tenantId, authState);
      const exists = await authPersistence.hasAuthState(tenantId);

      expect(exists).toBe(true);
    });

    it('should return false for non-existent auth state', async () => {
      const exists = await authPersistence.hasAuthState('non-existent-tenant');
      expect(exists).toBe(false);
    });
  });

  describe('Encryption', () => {
    it('should produce different ciphertext for same plaintext', async () => {
      const tenantId = 'test-tenant-11';
      const authState: AuthStateData = {
        creds: { test: 'credentials' },
        keys: { test: 'keys' }
      };

      await authPersistence.saveAuthState(tenantId, authState);
      const encrypted1 = await redis.get(`whatsapp:auth:${tenantId}`);

      await authPersistence.saveAuthState(tenantId, authState);
      const encrypted2 = await redis.get(`whatsapp:auth:${tenantId}`);

      // Different ciphertext due to random IV
      expect(encrypted1).not.toBe(encrypted2);

      // But same plaintext when decrypted
      const loaded = await authPersistence.loadAuthState(tenantId);
      expect(loaded).toEqual(authState);
    });

    it('should use different encryption keys for different secrets', async () => {
      const tenantId = 'test-tenant-12';
      const authState: AuthStateData = {
        creds: { test: 'credentials' },
        keys: { test: 'keys' }
      };

      const persistence1 = new AuthStatePersistence(redis, 'secret1-minimum-32-characters-long-test');
      const persistence2 = new AuthStatePersistence(redis, 'secret2-minimum-32-characters-long-test');

      await persistence1.saveAuthState(tenantId, authState);
      const encrypted1 = await redis.get(`whatsapp:auth:${tenantId}`);

      await persistence2.saveAuthState(tenantId, authState);
      const encrypted2 = await redis.get(`whatsapp:auth:${tenantId}`);

      // Different encryption keys produce different ciphertext
      expect(encrypted1).not.toBe(encrypted2);
    });
  });

  describe('Edge Cases', () => {
    it('should handle empty tenant ID', async () => {
      const authState: AuthStateData = {
        creds: { test: 'credentials' },
        keys: { test: 'keys' }
      };

      await authPersistence.saveAuthState('', authState);
      const loaded = await authPersistence.loadAuthState('');

      expect(loaded).toEqual(authState);
    });

    it('should handle very long tenant IDs', async () => {
      const longTenantId = 'a'.repeat(1000);
      const authState: AuthStateData = {
        creds: { test: 'credentials' },
        keys: { test: 'keys' }
      };

      await authPersistence.saveAuthState(longTenantId, authState);
      const loaded = await authPersistence.loadAuthState(longTenantId);

      expect(loaded).toEqual(authState);
    });

    it('should handle special characters in tenant ID', async () => {
      const specialTenantId = 'tenant-!@#$%^&*()_+-=[]{}|;:,.<>?';
      const authState: AuthStateData = {
        creds: { test: 'credentials' },
        keys: { test: 'keys' }
      };

      await authPersistence.saveAuthState(specialTenantId, authState);
      const loaded = await authPersistence.loadAuthState(specialTenantId);

      expect(loaded).toEqual(authState);
    });

    it('should handle very large auth state', async () => {
      const tenantId = 'test-tenant-13';
      const largeAuthState: AuthStateData = {
        creds: {
          data: 'x'.repeat(100000) // 100KB of data
        },
        keys: {
          data: 'y'.repeat(100000) // 100KB of data
        }
      };

      await authPersistence.saveAuthState(tenantId, largeAuthState);
      const loaded = await authPersistence.loadAuthState(tenantId);

      expect(loaded).toEqual(largeAuthState);
    });
  });
});
