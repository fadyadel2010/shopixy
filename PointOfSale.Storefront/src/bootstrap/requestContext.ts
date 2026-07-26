/**
 * Request Context
 * 
 * Provides centralized access to request headers and bootstrap context.
 * Used by shared clients to attach request-scoped headers after bootstrap.
 * 
 * Single source of truth for:
 * - Session ID
 * - Customer ID (when authenticated)
 * 
 * PHASE 10.4: Cart Ownership Migration
 * - Customer ID is set by authStore on login
 * - Customer ID is cleared by authStore on logout
 * - All API calls include X-Customer-Id when authenticated
 * 
 */

import { isTenantResolved, getTenantId } from './tenantResolver';
import { getSessionId } from './sessionManager';

/**
 * Request context headers
 */
export interface RequestHeaders {
    'X-Tenant-Id': string;
    'X-Session-Id': string;
    'X-Customer-Id'?: string;
}

// Customer ID state (set by authStore on login/logout)
let currentCustomerId: string | null = null;

/**
 * Set customer ID for authenticated requests
 * Called by authStore on login success
 */
export function setCustomerId(id: string | null): void {
    currentCustomerId = id;
    if (import.meta.env.DEV) {
        if (id) {
            console.log('[RequestContext] Customer ID set:', id.substring(0, 8) + '...');
        } else {
            console.log('[RequestContext] Customer ID cleared');
        }
    }
}

/**
 * Get current customer ID
 */
export function getCustomerId(): string | null {
    return currentCustomerId;
}

/**
 * Check if user is authenticated (has customer ID)
 */
export function isAuthenticated(): boolean {
    return currentCustomerId !== null;
}

/**
 * Get request headers for API calls
 * Includes X-Tenant-Id and X-Customer-Id when authenticated
 * Throws if bootstrap readiness checks have not completed
 */
export function getRequestHeaders(): RequestHeaders {
    if (!isTenantResolved()) {
        throw new Error(
            '[RequestContext] Cannot get request headers - tenant not resolved. ' +
            'Ensure bootstrap() completes before making API calls.'
        );
    }

    const headers: RequestHeaders = {
        'X-Tenant-Id': getTenantId(),
        'X-Session-Id': getSessionId(),
    };

    // Include customer ID if authenticated
    // This is CRITICAL for cart ownership - without this, cart operations
    // will use session ownership even for logged-in users
    if (currentCustomerId) {
        headers['X-Customer-Id'] = currentCustomerId;
    }

    return headers;
}

/**
 * Check if request context is ready
 */
export function isRequestContextReady(): boolean {
    return isTenantResolved();
}
