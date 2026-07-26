import dotenv from 'dotenv';

// Load environment variables
dotenv.config();

/**
 * Application configuration
 */
export const config = {
  // Server
  port: parseInt(process.env.PORT || '3000', 10),
  nodeEnv: process.env.NODE_ENV || 'development',

  // Redis
  redis: {
    host: process.env.REDIS_HOST || 'localhost',
    port: parseInt(process.env.REDIS_PORT || '6379', 10),
    password: process.env.REDIS_PASSWORD || undefined,
    db: parseInt(process.env.REDIS_DB || '0', 10)
  },

  // Session
  session: {
    // AUDIT FIX: Increased from 10 minutes to 24 hours (1440 minutes)
    // Sessions stay connected longer for production use
    // Auth state is preserved in Redis regardless of this setting
    idleTimeoutMinutes: parseInt(process.env.SESSION_IDLE_TIMEOUT_MINUTES || '1440', 10),
    cleanupIntervalMinutes: parseInt(process.env.SESSION_CLEANUP_INTERVAL_MINUTES || '30', 10)
  },

  // Pool
  pool: {
    size: parseInt(process.env.POOL_SIZE || '10', 10),
    maxConnectionsPerMember: parseInt(process.env.POOL_MAX_CONNECTIONS_PER_MEMBER || '50', 10)
  },

  // Security
  security: {
    authStateEncryptionSecret: process.env.AUTH_STATE_ENCRYPTION_SECRET || 'change-this-in-production-to-a-secure-random-string',
    apiKey: process.env.BAILEYS_API_KEY || '',
    // Per-tenant salt prefix for key derivation (prevents rainbow table attacks)
    saltPrefix: process.env.AUTH_SALT_PREFIX || 'whatsapp-auth-v2-'
  },

  // Logging
  logging: {
    level: process.env.LOG_LEVEL || 'info'
  }
};

/**
 * Validate configuration
 */
export function validateConfig(): void {
  if (config.port < 1 || config.port > 65535) {
    throw new Error('Invalid PORT configuration');
  }

  if (config.session.idleTimeoutMinutes < 1) {
    throw new Error('SESSION_IDLE_TIMEOUT_MINUTES must be at least 1');
  }

  if (config.pool.size < 1) {
    throw new Error('POOL_SIZE must be at least 1');
  }

  if (config.nodeEnv === 'production' && config.security.authStateEncryptionSecret === 'change-this-in-production-to-a-secure-random-string') {
    throw new Error('AUTH_STATE_ENCRYPTION_SECRET must be set in production');
  }

  if (config.security.authStateEncryptionSecret.length < 32) {
    throw new Error('AUTH_STATE_ENCRYPTION_SECRET must be at least 32 characters');
  }

  // SECURITY: API key is REQUIRED in production to prevent unauthorized access
  if (config.nodeEnv === 'production' && !config.security.apiKey) {
    throw new Error('BAILEYS_API_KEY must be set in production');
  }

  if (config.nodeEnv === 'production' && config.security.apiKey.length < 32) {
    throw new Error('BAILEYS_API_KEY must be at least 32 characters');
  }
}
