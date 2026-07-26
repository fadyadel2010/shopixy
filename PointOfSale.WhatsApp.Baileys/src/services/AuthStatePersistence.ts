/**
 * Auth State Persistence Service
 * 
 * Handles encryption, storage, and retrieval of WhatsApp authentication state
 * in Redis. Auth state contains sensitive credentials and must be encrypted
 * before storage.
 * 
 * Key responsibilities:
 * - Encrypt auth state before persisting to Redis
 * - Decrypt auth state when loading from Redis
 * - Handle corrupted or invalid auth state gracefully
 * - Clean up auth state on session disconnect
 */

import { createCipheriv, createDecipheriv, randomBytes, scryptSync } from 'crypto';
import { RedisClientType } from 'redis';
import { createLogger } from '../utils/logger';
import { BufferJSON } from '@whiskeysockets/baileys';

const logger = createLogger('AuthStatePersistence');

/**
 * Auth state data structure
 */
export interface AuthStateData {
  creds: any;
  keys: any;
}

/**
 * Encrypted auth state stored in Redis
 */
interface EncryptedAuthState {
  iv: string; // Initialization vector (hex)
  encryptedData: string; // Encrypted JSON (hex)
  version: number; // Encryption version for future compatibility
}

/**
 * Auth State Persistence Service
 */
export class AuthStatePersistence {
  private readonly ALGORITHM = 'aes-256-gcm';
  private readonly KEY_LENGTH = 32; // 256 bits
  private readonly IV_LENGTH = 16; // 128 bits
  private readonly AUTH_TAG_LENGTH = 16; // 128 bits
  private readonly ENCRYPTION_VERSION = 1;
  private readonly TTL_SECONDS = 30 * 24 * 60 * 60; // 30 days

  /**
   * SECURITY: Per-tenant encryption key cache.
   * Each tenant gets a unique encryption key derived from the master secret + tenant-specific salt.
   * This prevents rainbow table attacks and ensures tenant isolation.
   */
  private encryptionKeyCache: Map<string, Buffer> = new Map();
  private masterSecret: string;
  private saltPrefix: string;

  constructor(
    private redis: RedisClientType,
    encryptionSecret: string,
    saltPrefix: string = 'whatsapp-auth-v2-'
  ) {
    this.masterSecret = encryptionSecret;
    this.saltPrefix = saltPrefix;
  }

  /**
   * SECURITY: Derive per-tenant encryption key using tenant-specific salt.
   * This ensures each tenant has a unique encryption key even with the same master secret.
   * Keys are cached for performance (scrypt is intentionally slow).
   */
  private getEncryptionKey(tenantId: string): Buffer {
    // Check cache first
    const cachedKey = this.encryptionKeyCache.get(tenantId);
    if (cachedKey) {
      return cachedKey;
    }

    // Derive new key using tenant-specific salt
    // Salt format: prefix + tenantId (prevents rainbow table attacks across tenants)
    const salt = `${this.saltPrefix}${tenantId}`;
    const key = scryptSync(this.masterSecret, salt, this.KEY_LENGTH);

    // Cache the key (scrypt is expensive, ~100ms per derivation)
    this.encryptionKeyCache.set(tenantId, key);

    logger.debug({ tenantId }, 'Derived new encryption key for tenant');
    return key;
  }

  /**
   * Save auth state to Redis with encryption
   */
  async saveAuthState(tenantId: string, authState: AuthStateData): Promise<void> {
    try {
      logger.debug({ tenantId }, 'Saving auth state');

      // Serialize auth state to JSON using BufferJSON (handles binary data in Baileys v7)
      const jsonData = JSON.stringify(authState, BufferJSON.replacer);


      // Encrypt the data with tenant-specific key
      const encrypted = this.encrypt(tenantId, jsonData);

      // Store in Redis
      const key = this.getAuthStateKey(tenantId);
      await this.redis.set(key, JSON.stringify(encrypted), {
        EX: this.TTL_SECONDS
      });

      logger.info({ tenantId }, 'Auth state saved successfully');
    } catch (error) {
      logger.error({ err: error, tenantId }, 'Failed to save auth state');
      throw new Error(`Failed to save auth state for tenant ${tenantId}: ${error}`);
    }
  }

  /**
   * Load auth state from Redis with decryption
   */
  async loadAuthState(tenantId: string): Promise<AuthStateData | null> {
    try {
      logger.debug({ tenantId }, 'Loading auth state');

      // Retrieve from Redis
      const key = this.getAuthStateKey(tenantId);
      const data = await this.redis.get(key);

      if (!data) {
        logger.debug({ tenantId }, 'No auth state found in Redis');
        return null;
      }

      // Parse encrypted data
      const encrypted = JSON.parse(data) as EncryptedAuthState;

      // Validate version
      if (encrypted.version !== this.ENCRYPTION_VERSION) {
        logger.warn(
          { tenantId, version: encrypted.version },
          'Auth state version mismatch, discarding'
        );
        await this.deleteAuthState(tenantId);
        return null;
      }

      // Decrypt the data with tenant-specific key
      const decryptedJson = this.decrypt(tenantId, encrypted);

      // Parse JSON using BufferJSON (handles binary data in Baileys v7)
      const authState = JSON.parse(decryptedJson, BufferJSON.reviver) as AuthStateData;


      logger.info({ tenantId }, 'Auth state loaded successfully');
      return authState;
    } catch (error) {
      logger.error({ err: error, tenantId }, 'Failed to load auth state, discarding');

      // Delete corrupted auth state
      await this.deleteAuthState(tenantId).catch((deleteError) => {
        logger.error({ err: deleteError, tenantId }, 'Failed to delete corrupted auth state');
      });

      return null;
    }
  }

  /**
   * Delete auth state from Redis
   */
  async deleteAuthState(tenantId: string): Promise<void> {
    try {
      logger.debug({ tenantId }, 'Deleting auth state');

      const key = this.getAuthStateKey(tenantId);
      await this.redis.del(key);

      logger.info({ tenantId }, 'Auth state deleted successfully');
    } catch (error) {
      logger.error({ err: error, tenantId }, 'Failed to delete auth state');
      throw new Error(`Failed to delete auth state for tenant ${tenantId}: ${error}`);
    }
  }

  /**
   * Check if auth state exists for a tenant
   */
  async hasAuthState(tenantId: string): Promise<boolean> {
    try {
      const key = this.getAuthStateKey(tenantId);
      const exists = await this.redis.exists(key);
      return exists === 1;
    } catch (error) {
      logger.error({ err: error, tenantId }, 'Failed to check auth state existence');
      return false;
    }
  }

  /**
   * Encrypt data using AES-256-GCM with tenant-specific key
   */
  private encrypt(tenantId: string, plaintext: string): EncryptedAuthState {
    // Generate random IV
    const iv = randomBytes(this.IV_LENGTH);

    // Get tenant-specific encryption key
    const encryptionKey = this.getEncryptionKey(tenantId);

    // Create cipher
    const cipher = createCipheriv(this.ALGORITHM, encryptionKey, iv);

    // Encrypt data
    let encrypted = cipher.update(plaintext, 'utf8', 'hex');
    encrypted += cipher.final('hex');

    // Get auth tag
    const authTag = cipher.getAuthTag();

    // Combine encrypted data and auth tag
    const encryptedData = encrypted + authTag.toString('hex');

    return {
      iv: iv.toString('hex'),
      encryptedData,
      version: this.ENCRYPTION_VERSION
    };
  }

  /**
   * Decrypt data using AES-256-GCM with tenant-specific key
   */
  private decrypt(tenantId: string, encrypted: EncryptedAuthState): string {
    // Parse IV
    const iv = Buffer.from(encrypted.iv, 'hex');

    // Split encrypted data and auth tag
    const encryptedData = encrypted.encryptedData.slice(0, -this.AUTH_TAG_LENGTH * 2);
    const authTag = Buffer.from(
      encrypted.encryptedData.slice(-this.AUTH_TAG_LENGTH * 2),
      'hex'
    );

    // Get tenant-specific encryption key
    const encryptionKey = this.getEncryptionKey(tenantId);

    // Create decipher
    const decipher = createDecipheriv(this.ALGORITHM, encryptionKey, iv);
    decipher.setAuthTag(authTag);

    // Decrypt data
    let decrypted = decipher.update(encryptedData, 'hex', 'utf8');
    decrypted += decipher.final('utf8');

    return decrypted;
  }

  /**
   * Get Redis key for auth state
   */
  private getAuthStateKey(tenantId: string): string {
    return `whatsapp:auth:${tenantId}`;
  }
}
