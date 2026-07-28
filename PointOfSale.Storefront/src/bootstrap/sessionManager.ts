/**
 * Session Manager
 * 
 * Manages guest session ID for the storefront.
 * - Generates X-Session-Id once
 * - Persists in tenant-scoped localStorage
 * - Reuses across reloads
 * - Never regenerates unless explicitly cleared
 */

import { getTenantScopedStorageKey, tryGetTenantScopedStorageKey } from './tenantScopedStorage';

const SESSION_STORAGE_KEY = 'storefront_session_id';

/**
 * Generate a UUID v4
 */
function generateUUID(): string {
    // Use crypto.randomUUID if available (modern browsers)
    if (typeof crypto !== 'undefined' && crypto.randomUUID) {
        return crypto.randomUUID();
    }

    // Fallback for older browsers
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
        const r = (Math.random() * 16) | 0;
        const v = c === 'x' ? r : (r & 0x3) | 0x8;
        return v.toString(16);
    });
}

/**
 * In-memory session IDs by tenant-scoped key (for when localStorage is unavailable)
 */
const memorySessionIds = new Map<string, string>();

/**
 * Check if localStorage is available
 */
function isLocalStorageAvailable(): boolean {
    try {
        const test = '__storage_test__';
        localStorage.setItem(test, test);
        localStorage.removeItem(test);
        return true;
    } catch {
        return false;
    }
}

function getSessionStorageKey(): string {
    return getTenantScopedStorageKey(SESSION_STORAGE_KEY);
}

function tryGetSessionStorageKey(): string | null {
    return tryGetTenantScopedStorageKey(SESSION_STORAGE_KEY);
}

/**
 * Get or create session ID
 * 
 * Priority:
 * 1. Return existing session from localStorage (if valid)
 * 2. Return existing session from memory (fallback)
 * 3. Generate new session and persist
 */
export function getSessionId(): string {
    const storageKey = getSessionStorageKey();

    // Try tenant-scoped localStorage first
    if (isLocalStorageAvailable()) {
        const stored = localStorage.getItem(storageKey);
        if (stored) {
            const normalizedStored = stored.trim();
            
            // Validate stored session ID format to prevent malformed values from poisoning runtime state
            if (!isValidSessionId(normalizedStored)) {
                console.warn(
                    '[SessionManager] Invalid stored session ID detected. Clearing and regenerating:',
                    normalizedStored.substring(0, 8) + '...'
                );
                
                // Clear invalid session from tenant-scoped localStorage
                try {
                    localStorage.removeItem(storageKey);
                } catch (error) {
                    console.warn('[SessionManager] Failed to clear invalid session from localStorage:', error);
                }
                
                const memorySessionId = memorySessionIds.get(storageKey);
                if (memorySessionId && !isValidSessionId(memorySessionId)) {
                    memorySessionIds.delete(storageKey);
                }
                
                // Fall through to regeneration logic below (or return valid memorySessionId)
            } else {
                if (import.meta.env.DEV) {
                    console.log('[SessionManager] Using stored session:', normalizedStored.substring(0, 8) + '...');
                }
                return normalizedStored;
            }
        }
    }

    // Try memory fallback
    const memorySessionId = memorySessionIds.get(storageKey);
    if (memorySessionId) {
        return memorySessionId;
    }

    // Generate new session
    const newSessionId = generateUUID();

    if (import.meta.env.DEV) {
        console.log('[SessionManager] Generated new session:', newSessionId.substring(0, 8) + '...');
    }

    // Persist to tenant-scoped localStorage
    if (isLocalStorageAvailable()) {
        try {
            localStorage.setItem(getSessionStorageKey(), newSessionId);
        } catch (error) {
            console.warn('[SessionManager] Failed to persist session to localStorage:', error);
        }
    }

    // Always keep in memory as backup
    memorySessionIds.set(storageKey, newSessionId);

    return newSessionId;
}

/**
 * Validate that a session ID matches the UUID v4 format
 */
function isValidSessionId(sessionId: string): boolean {
    // UUID v4 format: xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx
    const uuidV4Regex = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
    return uuidV4Regex.test(sessionId);
}

/**
 * Force the storefront session ID to a recovered value.
 * This lets a guest cart be restored on a different device.
 */
export function setSessionId(sessionId: string): void {
    const normalizedSessionId = sessionId.trim();
    if (!normalizedSessionId) {
        return;
    }

    // Validate session ID format to prevent malformed payloads from poisoning session state
    if (!isValidSessionId(normalizedSessionId)) {
        console.warn(
            '[SessionManager] Rejected invalid session ID format. Expected UUID v4:',
            normalizedSessionId.substring(0, 8) + '...'
        );
        return;
    }

    if (isLocalStorageAvailable()) {
        try {
            localStorage.setItem(getSessionStorageKey(), normalizedSessionId);
        } catch (error) {
            console.warn('[SessionManager] Failed to persist recovered session to localStorage:', error);
        }
    }

    memorySessionIds.set(getSessionStorageKey(), normalizedSessionId);

    if (import.meta.env.DEV) {
        console.log('[SessionManager] Session overridden from recovery link:', normalizedSessionId.substring(0, 8) + '...');
    }
}

/**
 * Clear session (for explicit logout or testing)
 */
export function clearSession(): void {
    if (isLocalStorageAvailable()) {
        try {
            const storageKey = tryGetSessionStorageKey();
            if (storageKey) {
                localStorage.removeItem(storageKey);
            }
        } catch (error) {
            console.warn('[SessionManager] Failed to clear session from localStorage:', error);
        }
    }
    const storageKey = tryGetSessionStorageKey();
    if (storageKey) {
        memorySessionIds.delete(storageKey);
    }

    if (import.meta.env.DEV) {
        console.log('[SessionManager] Session cleared');
    }
}

/**
 * Check if session exists
 */
export function hasSession(): boolean {
    if (isLocalStorageAvailable()) {
        const storageKey = tryGetSessionStorageKey();
        return storageKey ? localStorage.getItem(storageKey) !== null : false;
    }
    const storageKey = tryGetSessionStorageKey();
    return storageKey ? memorySessionIds.has(storageKey) : false;
}

/**
 * Initialize session on app start
 * Ensures a session exists before any API calls
 */
export function initializeSession(): string {
    const sessionId = getSessionId();

    if (import.meta.env.DEV) {
        console.log('[SessionManager] Session initialized');
    }

    return sessionId;
}
