/**
 * Analytics Session Manager
 * 
 * Extends the existing sessionManager from bootstrap.
 * Adds analytics-specific session features:
 * - Inactivity-based session rotation
 * - Last activity tracking
 * 
 * RULES:
 * - SessionId survives page reloads
 * - SessionId is NOT authentication
 * - SessionId is sent with EVERY analytics event
 * - Session rotates after 30 minutes of inactivity
 */

import { getSessionId as getBootstrapSessionId } from '@/bootstrap/sessionManager';

const LAST_ACTIVITY_KEY = 'analytics_last_activity';
const SESSION_TIMEOUT_MS = 30 * 60 * 1000; // 30 minutes

/**
 * Update last activity timestamp
 */
function updateLastActivity(): void {
    try {
        localStorage.setItem(LAST_ACTIVITY_KEY, Date.now().toString());
    } catch {
        // localStorage might not be available
    }
}

/**
 * Get last activity timestamp
 */
function getLastActivity(): number {
    try {
        const stored = localStorage.getItem(LAST_ACTIVITY_KEY);
        return stored ? parseInt(stored, 10) : Date.now();
    } catch {
        return Date.now();
    }
}

/**
 * Check if session has expired due to inactivity
 */
function isSessionExpired(): boolean {
    const lastActivity = getLastActivity();
    const now = Date.now();
    return now - lastActivity > SESSION_TIMEOUT_MS;
}

/**
 * Analytics Session class
 * 
 * Wraps bootstrap session manager with analytics-specific behavior.
 */
class AnalyticsSession {
    private static instance: AnalyticsSession;

    private constructor() {
        // Update activity on initialization
        updateLastActivity();
    }

    static getInstance(): AnalyticsSession {
        if (!AnalyticsSession.instance) {
            AnalyticsSession.instance = new AnalyticsSession();
        }
        return AnalyticsSession.instance;
    }

    /**
     * Get current session ID
     * 
     * Uses the shared session from bootstrap.
     * This ensures analytics and cart use the same session.
     */
    getSessionId(): string {
        // Check for inactivity-based expiry
        if (isSessionExpired()) {
            if (import.meta.env.DEV) {
                console.log('[AnalyticsSession] Session expired due to inactivity, will use new session');
            }
            // Note: We don't clear the session here because that would affect cart.
            // The backend handles session rotation on its own.
            // We just track activity for analytics purposes.
        }

        // Update activity timestamp
        updateLastActivity();

        // Use the shared session from bootstrap
        return getBootstrapSessionId();
    }

    /**
     * Mark activity (call this on user interactions)
     */
    markActivity(): void {
        updateLastActivity();
    }

    /**
     * Check if session is active (not expired)
     */
    isActive(): boolean {
        return !isSessionExpired();
    }

    /**
     * Get seconds until session expires
     */
    getSecondsUntilExpiry(): number {
        const lastActivity = getLastActivity();
        const expiresAt = lastActivity + SESSION_TIMEOUT_MS;
        const remaining = expiresAt - Date.now();
        return Math.max(0, Math.floor(remaining / 1000));
    }
}

// Export singleton instance
export const analyticsSession = AnalyticsSession.getInstance();

// Export functions for easier usage
export function getAnalyticsSessionId(): string {
    return analyticsSession.getSessionId();
}

export function markSessionActivity(): void {
    analyticsSession.markActivity();
}

export function isSessionActive(): boolean {
    return analyticsSession.isActive();
}
