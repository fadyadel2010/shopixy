/**
 * Pool Manager Service
 * 
 * Manages a shared pool of WhatsApp sessions for free-tier tenants.
 * Handles pool initialization, member management, and least-loaded routing.
 * 
 * Key responsibilities:
 * - Initialize fixed pool of WhatsApp sessions on startup
 * - Track active connections per pool member
 * - Route messages to least-loaded pool member
 * - Store tenant-to-pool-member mappings in Redis
 * - Handle pool member failures
 */

import { RedisClientType } from 'redis';
import { PoolMember, PoolConfiguration } from '../models/Session';
import { config } from '../config';
import { createLogger } from '../utils/logger';

const logger = createLogger('PoolManager');

/**
 * Pool Manager
 */
export class PoolManager {
  private poolMembers: Map<string, PoolMember>;
  private poolConfiguration: PoolConfiguration;

  constructor(private redis: RedisClientType) {
    this.poolMembers = new Map();
    this.poolConfiguration = {
      size: config.pool.size,
      members: [],
      routingStrategy: 'LEAST_LOADED'
    };
  }

  /**
   * Initialize the pool with configured members
   */
  async initialize(): Promise<void> {
    try {
      logger.info({ poolSize: config.pool.size }, 'Initializing pool');

      // Parse pool members from environment variable
      const poolMembersConfig = process.env.POOL_MEMBERS || '';
      
      if (poolMembersConfig) {
        // Format: "member1:+1234567890,member2:+9876543210"
        const memberConfigs = poolMembersConfig.split(',');
        
        for (const memberConfig of memberConfigs) {
          const [id, phoneNumber] = memberConfig.split(':');
          if (id && phoneNumber) {
            const member: PoolMember = {
              id: id.trim(),
              phoneNumber: phoneNumber.trim(),
              activeConnections: 0,
              maxConnections: config.pool.maxConnectionsPerMember,
              status: 'active'
            };
            this.poolMembers.set(member.id, member);
            this.poolConfiguration.members.push(member);
          }
        }
      }

      // If no members configured, create default pool members
      if (this.poolMembers.size === 0) {
        logger.warn('No pool members configured, creating default pool');
        for (let i = 1; i <= config.pool.size; i++) {
          const member: PoolMember = {
            id: `pool-member-${i}`,
            phoneNumber: `+pool${i}`, // Placeholder - needs real phone numbers
            activeConnections: 0,
            maxConnections: config.pool.maxConnectionsPerMember,
            status: 'active'
          };
          this.poolMembers.set(member.id, member);
          this.poolConfiguration.members.push(member);
        }
      }

      logger.info(
        { memberCount: this.poolMembers.size, members: Array.from(this.poolMembers.keys()) },
        'Pool initialized'
      );
    } catch (error) {
      logger.error({ err: error }, 'Failed to initialize pool');
      throw error;
    }
  }

  /**
   * Get pool configuration
   */
  getPoolConfiguration(): PoolConfiguration {
    return this.poolConfiguration;
  }

  /**
   * Get all pool members
   */
  getPoolMembers(): PoolMember[] {
    return Array.from(this.poolMembers.values());
  }

  /**
   * Get a specific pool member
   */
  getPoolMember(memberId: string): PoolMember | undefined {
    return this.poolMembers.get(memberId);
  }

  /**
   * Select least-loaded pool member for a tenant
   * Implements least-loaded selection algorithm
   */
  async selectPoolMember(tenantId: string): Promise<PoolMember | null> {
    try {
      // Check if tenant already has a pool member assigned (within TTL)
      const existingMapping = await this.getTenantPoolMapping(tenantId);
      if (existingMapping) {
        const member = this.poolMembers.get(existingMapping);
        if (member && member.status === 'active') {
          logger.debug({ tenantId, memberId: existingMapping }, 'Using existing pool member mapping');
          return member;
        }
      }

      // Find least-loaded active pool member
      let leastLoadedMember: PoolMember | null = null;
      let minConnections = Infinity;

      for (const member of this.poolMembers.values()) {
        if (member.status === 'active' && member.activeConnections < member.maxConnections) {
          if (member.activeConnections < minConnections) {
            minConnections = member.activeConnections;
            leastLoadedMember = member;
          }
        }
      }

      if (!leastLoadedMember) {
        logger.warn({ tenantId }, 'No available pool members');
        return null;
      }

      // Store tenant-to-pool-member mapping in Redis (1 hour TTL)
      await this.setTenantPoolMapping(tenantId, leastLoadedMember.id);

      logger.info(
        { tenantId, memberId: leastLoadedMember.id, activeConnections: leastLoadedMember.activeConnections },
        'Selected pool member'
      );

      return leastLoadedMember;
    } catch (error) {
      logger.error({ err: error, tenantId }, 'Failed to select pool member');
      return null;
    }
  }

  /**
   * Increment active connections for a pool member
   */
  incrementConnections(memberId: string): void {
    const member = this.poolMembers.get(memberId);
    if (member) {
      member.activeConnections++;
      logger.debug({ memberId, activeConnections: member.activeConnections }, 'Incremented connections');
    }
  }

  /**
   * Decrement active connections for a pool member
   */
  decrementConnections(memberId: string): void {
    const member = this.poolMembers.get(memberId);
    if (member && member.activeConnections > 0) {
      member.activeConnections--;
      logger.debug({ memberId, activeConnections: member.activeConnections }, 'Decremented connections');
    }
  }

  /**
   * Mark a pool member as failed
   */
  markMemberFailed(memberId: string): void {
    const member = this.poolMembers.get(memberId);
    if (member) {
      member.status = 'inactive';
      logger.warn({ memberId }, 'Pool member marked as failed');
    }
  }

  /**
   * Mark a pool member as active
   */
  markMemberActive(memberId: string): void {
    const member = this.poolMembers.get(memberId);
    if (member) {
      member.status = 'active';
      logger.info({ memberId }, 'Pool member marked as active');
    }
  }

  /**
   * Get tenant-to-pool-member mapping from Redis
   */
  private async getTenantPoolMapping(tenantId: string): Promise<string | null> {
    const key = `whatsapp:pool:mapping:${tenantId}`;
    return await this.redis.get(key);
  }

  /**
   * Set tenant-to-pool-member mapping in Redis (1 hour TTL)
   */
  private async setTenantPoolMapping(tenantId: string, memberId: string): Promise<void> {
    const key = `whatsapp:pool:mapping:${tenantId}`;
    await this.redis.set(key, memberId, {
      EX: 60 * 60 // 1 hour TTL
    });
  }

  /**
   * Clear tenant-to-pool-member mapping
   */
  async clearTenantPoolMapping(tenantId: string): Promise<void> {
    const key = `whatsapp:pool:mapping:${tenantId}`;
    await this.redis.del(key);
  }

  /**
   * Get pool statistics
   */
  getPoolStatistics(): {
    totalMembers: number;
    activeMembers: number;
    inactiveMembers: number;
    totalConnections: number;
    averageLoad: number;
  } {
    let activeMembers = 0;
    let inactiveMembers = 0;
    let totalConnections = 0;

    for (const member of this.poolMembers.values()) {
      if (member.status === 'active') {
        activeMembers++;
      } else {
        inactiveMembers++;
      }
      totalConnections += member.activeConnections;
    }

    const averageLoad = activeMembers > 0 ? totalConnections / activeMembers : 0;

    return {
      totalMembers: this.poolMembers.size,
      activeMembers,
      inactiveMembers,
      totalConnections,
      averageLoad
    };
  }
}
