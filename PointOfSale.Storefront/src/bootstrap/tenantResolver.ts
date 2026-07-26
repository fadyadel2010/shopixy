/**
 * Tenant Resolver
 * 
 * Resolves the Tenant ID using a priority-based approach:
 * 1. Subdomain (production mode)
 * 2. Environment variable VITE_DEFAULT_TENANT_ID (DEV only)
 * 3. Fail with TenantResolutionError
 * 
 * NO backend calls to guess tenants.
 * NO hardcoded Tenant IDs (except for dev fallback via env).
 */

import { BootstrapErrorCode, createBootstrapError, type BootstrapError } from './bootstrapErrors';

/**
 * Tenant resolution result
 */
export type TenantResolutionResult = {
    success: true;
    tenantId: string;
    source: 'subdomain' | 'env' | 'query';
} | {
    success: false;
    error: BootstrapError;
};

/**
 * Valid UUID v4 pattern
 */
const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

/**
 * Simple slug pattern for tenant subdomains
 */
const SLUG_PATTERN = /^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$/i;

/**
 * Reserved subdomains that should not be treated as tenants
 */
const RESERVED_SUBDOMAINS = new Set([
    'www',
    'api',
    'admin',
    'app',
    'static',
    'cdn',
    'mail',
    'smtp',
    'ftp',
    'localhost',
]);

/**
 * Check if a string is a valid tenant identifier
 * Accepts either a UUID or a slug
 */
function isValidTenantId(value: string): boolean {
    return UUID_PATTERN.test(value) || SLUG_PATTERN.test(value);
}

/**
 * Extract subdomain from hostname
 * 
 * Examples:
 * - store1.example.com → 'store1'
 * - store1.localhost → 'store1'
 * - store1.localhost:5174 → 'store1'
 * - example.com → null
 * - localhost → null
 * - localhost:5174 → null
 */
function extractSubdomain(hostname: string): string | null {
    // Remove port if present
    const host = hostname.split(':')[0];

    // Split by dots
    const parts = host.split('.');

    // localhost without subdomain
    if (parts.length === 1 && parts[0] === 'localhost') {
        return null;
    }

    // subdomain.localhost
    if (parts.length === 2 && parts[1] === 'localhost') {
        const subdomain = parts[0];
        if (!RESERVED_SUBDOMAINS.has(subdomain.toLowerCase())) {
            return subdomain;
        }
        return null;
    }

    // subdomain.domain.tld (e.g., store1.example.com)
    if (parts.length >= 3) {
        const subdomain = parts[0];
        if (!RESERVED_SUBDOMAINS.has(subdomain.toLowerCase())) {
            return subdomain;
        }
    }

    return null;
}

/**
 * Resolve tenant from query parameter (DEV only)
 * Useful for testing: ?tenant=xxx
 */
function resolveFromQueryParam(): string | null {
    if (!import.meta.env.DEV) {
        return null;
    }

    const params = new URLSearchParams(window.location.search);
    const tenant = params.get('tenant');

    if (tenant && isValidTenantId(tenant)) {
        return tenant;
    }

    return null;
}

/**
 * Resolve tenant from subdomain
 */
function resolveFromSubdomain(): string | null {
    const subdomain = extractSubdomain(window.location.hostname);

    if (subdomain && !RESERVED_SUBDOMAINS.has(subdomain.toLowerCase())) {
        return subdomain;
    }

    return null;
}

/**
 * Resolve tenant from environment variable (DEV only)
 */
function resolveFromEnv(): string | null {
    const envTenantId = import.meta.env.VITE_DEFAULT_TENANT_ID || import.meta.env.VITE_TENANT_ID;

    if (envTenantId && isValidTenantId(envTenantId)) {
        return envTenantId;
    }

    return null;
}

/**
 * Resolve tenant ID using priority order:
 * 1. Query parameter (DEV only) - for testing
 * 2. Subdomain
 * 3. Environment variable (DEV only)
 * 4. Fail
 */
export function resolveTenant(): TenantResolutionResult {
    // 1. Query parameter (DEV only)
    if (import.meta.env.DEV) {
        const queryTenant = resolveFromQueryParam();
        if (queryTenant) {
            if (import.meta.env.DEV) {
                console.log('[TenantResolver] Resolved from query param:', queryTenant);
            }
            return { success: true, tenantId: queryTenant, source: 'query' };
        }
    }

    // 2. Subdomain
    const subdomainTenant = resolveFromSubdomain();
    if (subdomainTenant) {
        if (import.meta.env.DEV) {
            console.log('[TenantResolver] Resolved from subdomain:', subdomainTenant);
        }
        return { success: true, tenantId: subdomainTenant, source: 'subdomain' };
    }

    // 3. Environment variable (DEV only fallback)
    if (import.meta.env.DEV) {
        const envTenant = resolveFromEnv();
        if (envTenant) {
            console.log('[TenantResolver] Resolved from env variable:', envTenant);
            return { success: true, tenantId: envTenant, source: 'env' };
        }
    }

    // 4. Cannot resolve tenant
    if (import.meta.env.DEV) {
        console.error('[TenantResolver] Failed to resolve tenant');
        console.error('[TenantResolver] Hostname:', window.location.hostname);
        console.error('[TenantResolver] Try: ?tenant=<uuid> or set VITE_DEFAULT_TENANT_ID');
    }

    return {
        success: false,
        error: createBootstrapError(
            BootstrapErrorCode.TENANT_RESOLUTION_FAILED,
            'Unable to identify the store. Please check the URL.',
            { retryable: false }
        ),
    };
}

/**
 * Current resolved tenant state
 * Set during bootstrap, never changes after
 */
let resolvedTenant: string | null = null;

/**
 * Get the resolved tenant ID
 * Throws if tenant was not resolved during bootstrap
 */
export function getTenantId(): string {
    if (!resolvedTenant) {
        throw new Error(
            '[TenantResolver] Tenant not resolved. This is a developer error - ' +
            'ensure bootstrap() completes before making API calls.'
        );
    }
    return resolvedTenant;
}

/**
 * Check if tenant is resolved
 */
export function isTenantResolved(): boolean {
    return resolvedTenant !== null;
}

/**
 * Set the resolved tenant (called by bootstrap)
 * Can only be set once unless overwrite=true is used by a controlled preview surface.
 */
export function setResolvedTenant(tenantId: string, options?: { overwrite?: boolean }): void {
    if (resolvedTenant !== null && resolvedTenant !== tenantId && !options?.overwrite) {
        console.warn('[TenantResolver] Attempted to set tenant ID twice. Ignoring.');
        return;
    }

    resolvedTenant = tenantId;
}

/**
 * Clear resolved tenant (for testing only)
 */
export function clearResolvedTenant(): void {
    if (import.meta.env.DEV) {
        resolvedTenant = null;
    }
}
