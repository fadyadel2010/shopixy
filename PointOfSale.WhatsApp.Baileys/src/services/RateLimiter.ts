/**
 * Rate Limiter Service
 * 
 * PHASE 3: Reliability, Self-Healing & Scale Readiness
 * 
 * Implements PILLAR 2: Rate & Load Safety
 * 
 * Features:
 * - Per-tenant message rate limiting
 * - Global message burst limiting
 * - QR generation rate limiting
 * - Token bucket algorithm with sliding window
 * 
 * All rate limits are enforced BEFORE socket interaction to prevent
 * overwhelming WhatsApp and triggering bans.
 */

import { RedisClientType } from 'redis';
import { createLogger } from '../utils/logger';
import { metricsService } from './MetricsService';
import { Counter, Gauge } from 'prom-client';

const logger = createLogger('RateLimiter');

// ═══════════════════════════════════════════════════════════════════════════════
// RATE LIMIT METRICS
// ═══════════════════════════════════════════════════════════════════════════════

const rateLimitRejectedTotal = new Counter({
    name: 'whatsapp_rate_limit_rejected_total',
    help: 'Total number of requests rejected by rate limiting',
    labelNames: ['tenant_id', 'limit_type'],
    registers: [metricsService.getRegistry()]
});

const rateLimitCurrentUsage = new Gauge({
    name: 'whatsapp_rate_limit_current_usage',
    help: 'Current rate limit usage (percentage of limit)',
    labelNames: ['tenant_id', 'limit_type'],
    registers: [metricsService.getRegistry()]
});

// ═══════════════════════════════════════════════════════════════════════════════
// RATE LIMIT CONFIGURATION
// ═══════════════════════════════════════════════════════════════════════════════

export interface RateLimitConfig {
    // Per-tenant message rate limit
    messagesPerMinute: number;
    messagesPerHour: number;

    // Global burst protection
    globalMessagesPerSecond: number;
    globalMessagesPerMinute: number;

    // QR generation limits
    qrGenerationsPerMinute: number;
    qrGenerationsPerHour: number;

    // Connect/reconnect limits
    connectsPerMinute: number;
}

const DEFAULT_CONFIG: RateLimitConfig = {
    // Per-tenant: 30 msgs/min, 500 msgs/hour (WhatsApp-safe defaults)
    messagesPerMinute: 30,
    messagesPerHour: 500,

    // Global: 10 msgs/sec, 200 msgs/min (system-wide protection)
    globalMessagesPerSecond: 10,
    globalMessagesPerMinute: 200,

    // QR: 5/min, 20/hour (prevent QR spam)
    qrGenerationsPerMinute: 5,
    qrGenerationsPerHour: 20,

    // Connect: 10/min per tenant (prevent reconnect storms)
    connectsPerMinute: 10
};

// ═══════════════════════════════════════════════════════════════════════════════
// RATE LIMIT RESULT
// ═══════════════════════════════════════════════════════════════════════════════

export interface RateLimitResult {
    allowed: boolean;
    limitType?: string;
    currentUsage?: number;
    limit?: number;
    retryAfterMs?: number;
    reason?: string;
}

// ═══════════════════════════════════════════════════════════════════════════════
// RATE LIMITER SERVICE
// ═══════════════════════════════════════════════════════════════════════════════

export class RateLimiter {
    private config: RateLimitConfig;
    private redis: RedisClientType;

    // In-memory fallback for when Redis is unavailable
    private localCounters: Map<string, { count: number; resetAt: number }> = new Map();

    constructor(redis: RedisClientType, config?: Partial<RateLimitConfig>) {
        this.redis = redis;
        this.config = { ...DEFAULT_CONFIG, ...config };

        logger.info({ config: this.config }, 'RateLimiter initialized');
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MESSAGE RATE LIMITING
    // ═══════════════════════════════════════════════════════════════════════════

    /**
     * Check if a message can be sent (all rate limits)
     */
    async checkMessageRateLimit(tenantId: string): Promise<RateLimitResult> {
        try {
            // Check 1: Global burst limit (per second)
            const globalSecondResult = await this.checkLimit(
                'global:messages:second',
                this.config.globalMessagesPerSecond,
                1000, // 1 second window
                'global_second'
            );
            if (!globalSecondResult.allowed) {
                rateLimitRejectedTotal.inc({ tenant_id: 'global', limit_type: 'global_second' });
                return globalSecondResult;
            }

            // Check 2: Global minute limit
            const globalMinuteResult = await this.checkLimit(
                'global:messages:minute',
                this.config.globalMessagesPerMinute,
                60000, // 1 minute window
                'global_minute'
            );
            if (!globalMinuteResult.allowed) {
                rateLimitRejectedTotal.inc({ tenant_id: 'global', limit_type: 'global_minute' });
                return globalMinuteResult;
            }

            // Check 3: Per-tenant minute limit
            const tenantMinuteResult = await this.checkLimit(
                `tenant:${tenantId}:messages:minute`,
                this.config.messagesPerMinute,
                60000,
                'tenant_minute'
            );
            if (!tenantMinuteResult.allowed) {
                rateLimitRejectedTotal.inc({ tenant_id: tenantId, limit_type: 'tenant_minute' });
                return tenantMinuteResult;
            }

            // Check 4: Per-tenant hour limit
            const tenantHourResult = await this.checkLimit(
                `tenant:${tenantId}:messages:hour`,
                this.config.messagesPerHour,
                3600000, // 1 hour window
                'tenant_hour'
            );
            if (!tenantHourResult.allowed) {
                rateLimitRejectedTotal.inc({ tenant_id: tenantId, limit_type: 'tenant_hour' });
                return tenantHourResult;
            }

            // All limits passed - increment all counters
            await this.incrementAllMessageCounters(tenantId);

            return { allowed: true };
        } catch (error) {
            // On error, use local fallback (fail-open with logging)
            logger.warn({ err: error, tenantId }, 'Rate limit check failed, using fallback');
            return this.checkLocalFallback(`tenant:${tenantId}:messages`, this.config.messagesPerMinute, 60000);
        }
    }

    /**
     * Increment all message counters after successful rate limit check
     */
    private async incrementAllMessageCounters(tenantId: string): Promise<void> {
        const now = Date.now();
        const pipeline = this.redis.multi();

        // Global counters
        const globalSecondKey = `ratelimit:global:messages:second:${Math.floor(now / 1000)}`;
        const globalMinuteKey = `ratelimit:global:messages:minute:${Math.floor(now / 60000)}`;
        pipeline.incr(globalSecondKey);
        pipeline.expire(globalSecondKey, 2);
        pipeline.incr(globalMinuteKey);
        pipeline.expire(globalMinuteKey, 120);

        // Tenant counters
        const tenantMinuteKey = `ratelimit:tenant:${tenantId}:messages:minute:${Math.floor(now / 60000)}`;
        const tenantHourKey = `ratelimit:tenant:${tenantId}:messages:hour:${Math.floor(now / 3600000)}`;
        pipeline.incr(tenantMinuteKey);
        pipeline.expire(tenantMinuteKey, 120);
        pipeline.incr(tenantHourKey);
        pipeline.expire(tenantHourKey, 7200);

        await pipeline.exec();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // QR GENERATION RATE LIMITING
    // ═══════════════════════════════════════════════════════════════════════════

    /**
     * Check if QR generation is allowed for tenant
     */
    async checkQrRateLimit(tenantId: string): Promise<RateLimitResult> {
        try {
            // Check minute limit
            const minuteResult = await this.checkLimit(
                `tenant:${tenantId}:qr:minute`,
                this.config.qrGenerationsPerMinute,
                60000,
                'qr_minute'
            );
            if (!minuteResult.allowed) {
                rateLimitRejectedTotal.inc({ tenant_id: tenantId, limit_type: 'qr_minute' });
                return minuteResult;
            }

            // Check hour limit
            const hourResult = await this.checkLimit(
                `tenant:${tenantId}:qr:hour`,
                this.config.qrGenerationsPerHour,
                3600000,
                'qr_hour'
            );
            if (!hourResult.allowed) {
                rateLimitRejectedTotal.inc({ tenant_id: tenantId, limit_type: 'qr_hour' });
                return hourResult;
            }

            // Increment counters
            await this.incrementQrCounters(tenantId);

            return { allowed: true };
        } catch (error) {
            logger.warn({ err: error, tenantId }, 'QR rate limit check failed, using fallback');
            return this.checkLocalFallback(`tenant:${tenantId}:qr`, this.config.qrGenerationsPerMinute, 60000);
        }
    }

    private async incrementQrCounters(tenantId: string): Promise<void> {
        const now = Date.now();
        const pipeline = this.redis.multi();

        const minuteKey = `ratelimit:tenant:${tenantId}:qr:minute:${Math.floor(now / 60000)}`;
        const hourKey = `ratelimit:tenant:${tenantId}:qr:hour:${Math.floor(now / 3600000)}`;

        pipeline.incr(minuteKey);
        pipeline.expire(minuteKey, 120);
        pipeline.incr(hourKey);
        pipeline.expire(hourKey, 7200);

        await pipeline.exec();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // CONNECT RATE LIMITING
    // ═══════════════════════════════════════════════════════════════════════════

    /**
     * Check if connect/reconnect is allowed for tenant
     */
    async checkConnectRateLimit(tenantId: string): Promise<RateLimitResult> {
        try {
            const result = await this.checkLimit(
                `tenant:${tenantId}:connect:minute`,
                this.config.connectsPerMinute,
                60000,
                'connect_minute'
            );

            if (!result.allowed) {
                rateLimitRejectedTotal.inc({ tenant_id: tenantId, limit_type: 'connect_minute' });
                return result;
            }

            // Increment counter
            const now = Date.now();
            const key = `ratelimit:tenant:${tenantId}:connect:minute:${Math.floor(now / 60000)}`;
            await this.redis.incr(key);
            await this.redis.expire(key, 120);

            return { allowed: true };
        } catch (error) {
            logger.warn({ err: error, tenantId }, 'Connect rate limit check failed, using fallback');
            return this.checkLocalFallback(`tenant:${tenantId}:connect`, this.config.connectsPerMinute, 60000);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // CORE RATE LIMIT CHECK
    // ═══════════════════════════════════════════════════════════════════════════

    /**
     * Core sliding window rate limit check using Redis
     */
    private async checkLimit(
        keyPrefix: string,
        limit: number,
        windowMs: number,
        limitType: string
    ): Promise<RateLimitResult> {
        const now = Date.now();
        const windowKey = Math.floor(now / windowMs);
        const key = `ratelimit:${keyPrefix}:${windowKey}`;

        const currentCount = await this.redis.get(key);
        const count = currentCount ? parseInt(currentCount, 10) : 0;

        // Update usage metric
        const usage = (count / limit) * 100;
        rateLimitCurrentUsage.set({ tenant_id: keyPrefix.split(':')[1] || 'global', limit_type: limitType }, usage);

        if (count >= limit) {
            // Calculate when the window resets
            const windowStart = windowKey * windowMs;
            const windowEnd = windowStart + windowMs;
            const retryAfterMs = windowEnd - now;

            return {
                allowed: false,
                limitType,
                currentUsage: count,
                limit,
                retryAfterMs,
                reason: `Rate limit exceeded: ${count}/${limit} (${limitType})`
            };
        }

        return { allowed: true, currentUsage: count, limit };
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // LOCAL FALLBACK (when Redis is unavailable)
    // ═══════════════════════════════════════════════════════════════════════════

    /**
     * Check rate limit using local in-memory counter (fallback)
     */
    private checkLocalFallback(key: string, limit: number, windowMs: number): RateLimitResult {
        const now = Date.now();
        const entry = this.localCounters.get(key);

        if (!entry || now > entry.resetAt) {
            // New window
            this.localCounters.set(key, { count: 1, resetAt: now + windowMs });
            return { allowed: true };
        }

        if (entry.count >= limit) {
            return {
                allowed: false,
                limitType: 'local_fallback',
                currentUsage: entry.count,
                limit,
                retryAfterMs: entry.resetAt - now,
                reason: `Rate limit exceeded (local fallback): ${entry.count}/${limit}`
            };
        }

        entry.count++;
        return { allowed: true };
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // UTILITY METHODS
    // ═══════════════════════════════════════════════════════════════════════════

    /**
     * Get current rate limit status for a tenant
     */
    async getRateLimitStatus(tenantId: string): Promise<Record<string, unknown>> {
        const now = Date.now();

        try {
            const [
                messageMinute,
                messageHour,
                qrMinute,
                qrHour,
                connectMinute
            ] = await Promise.all([
                this.redis.get(`ratelimit:tenant:${tenantId}:messages:minute:${Math.floor(now / 60000)}`),
                this.redis.get(`ratelimit:tenant:${tenantId}:messages:hour:${Math.floor(now / 3600000)}`),
                this.redis.get(`ratelimit:tenant:${tenantId}:qr:minute:${Math.floor(now / 60000)}`),
                this.redis.get(`ratelimit:tenant:${tenantId}:qr:hour:${Math.floor(now / 3600000)}`),
                this.redis.get(`ratelimit:tenant:${tenantId}:connect:minute:${Math.floor(now / 60000)}`)
            ]);

            return {
                tenantId,
                messages: {
                    minute: { current: parseInt(messageMinute || '0', 10), limit: this.config.messagesPerMinute },
                    hour: { current: parseInt(messageHour || '0', 10), limit: this.config.messagesPerHour }
                },
                qr: {
                    minute: { current: parseInt(qrMinute || '0', 10), limit: this.config.qrGenerationsPerMinute },
                    hour: { current: parseInt(qrHour || '0', 10), limit: this.config.qrGenerationsPerHour }
                },
                connect: {
                    minute: { current: parseInt(connectMinute || '0', 10), limit: this.config.connectsPerMinute }
                }
            };
        } catch (error) {
            logger.error({ err: error, tenantId }, 'Failed to get rate limit status');
            return { tenantId, error: 'Failed to retrieve rate limit status' };
        }
    }

    /**
     * Reset rate limits for a tenant (admin operation)
     */
    async resetTenantLimits(tenantId: string): Promise<void> {
        const now = Date.now();
        const keys = [
            `ratelimit:tenant:${tenantId}:messages:minute:${Math.floor(now / 60000)}`,
            `ratelimit:tenant:${tenantId}:messages:hour:${Math.floor(now / 3600000)}`,
            `ratelimit:tenant:${tenantId}:qr:minute:${Math.floor(now / 60000)}`,
            `ratelimit:tenant:${tenantId}:qr:hour:${Math.floor(now / 3600000)}`,
            `ratelimit:tenant:${tenantId}:connect:minute:${Math.floor(now / 60000)}`
        ];

        for (const key of keys) {
            await this.redis.del(key);
        }

        logger.info({ tenantId }, 'Rate limits reset for tenant');
    }

    /**
     * Get configuration
     */
    getConfig(): RateLimitConfig {
        return { ...this.config };
    }
}
