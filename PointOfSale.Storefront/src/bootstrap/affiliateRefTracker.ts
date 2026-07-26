/**
 * AF-12 + AF-27E: Affiliate Referral Code Tracker
 * 
 * Captures the affiliate referral code from multiple sources and stores it
 * for the duration of the browser session. The httpClient attaches it as
 * `X-Affiliate-Ref` header on every API request so the backend middleware
 * can activate Affiliate Mode.
 * 
 * AF-27E: Now supports THREE sources:
 * 1. Query parameter: ?ref=CODE (legacy flow)
 * 2. Route parameter: /p/{referralCode}/{slug} (landing page flow)
 * 3. Session storage: Persisted from previous navigation
 * 
 * WHY THIS IS NEEDED:
 * The storefront is a SPA. When a user navigates to /products?ref=SOSO2248,
 * the SPA makes API calls like GET /api/storefront/products/discover which
 * do NOT include the ?ref= query parameter. The backend middleware only sees
 * the API request URL, not the browser URL. Without this module, the middleware
 * never activates Affiliate Mode.
 * 
 * FLOW:
 * 1. User clicks affiliate link: https://store.example.com/p/CODE/product-slug
 * 2. SPA loads, bootstrap calls initAffiliateRefTracker()
 * 3. This module reads referralCode from route or ?ref= from URL and stores it in sessionStorage
 * 4. httpClient attaches X-Affiliate-Ref header on all subsequent API requests
 * 5. Backend AffiliateReferralMiddleware reads the header and activates Affiliate Mode
 * 
 * SECURITY:
 * - The header only HINTS to the middleware which ref code to validate
 * - The middleware STILL validates the code against the database
 * - A fake header will be rejected by the middleware (AffiliateAccount not found)
 * - Affiliate Mode is ALWAYS server-side — never trusted from frontend
 * 
 * SESSION PERSISTENCE:
 * - Stored in sessionStorage (survives page refresh, dies on tab close)
 * - NOT localStorage (we don't want ref to persist across sessions)
 */

const SESSION_KEY = 'affiliate_ref_code';

let currentRefCode: string | null = null;

/**
 * Initialize Affiliate Ref Tracker.
 * 
 * Call this during bootstrap BEFORE httpClient starts making requests.
 * AF-27E: Reads referral code from THREE sources (priority order):
 * 1. Query parameter: ?ref=CODE
 * 2. Route parameter: /p/{referralCode}/{slug}
 * 3. Session storage: Persisted from previous navigation
 */
export function initAffiliateRefTracker(): void {
    let refFromUrl: string | null = null;

    // PRIORITY 1: Check current URL for ?ref= parameter (legacy flow)
    const urlParams = new URLSearchParams(window.location.search);
    refFromUrl = urlParams.get('ref')?.trim() || null;

    // PRIORITY 2: AF-27E - Check route for /p/{referralCode}/{slug} pattern
    if (!refFromUrl) {
        const path = window.location.pathname;
        const match = path.match(/^\/p\/([^\/]+)\//);
        if (match && match[1]) {
            refFromUrl = match[1].trim();
            if (import.meta.env.DEV) {
                console.log('[AffiliateRef] Captured ref code from route /p/:', refFromUrl);
            }
        }
    }

    if (refFromUrl) {
        // New ref code from URL — store it
        currentRefCode = refFromUrl;
        try {
            sessionStorage.setItem(SESSION_KEY, refFromUrl);
        } catch {
            // sessionStorage unavailable (private browsing, etc.) — continue with in-memory
        }

        if (import.meta.env.DEV) {
            console.log('[AffiliateRef] Captured ref code:', refFromUrl);
        }
    } else {
        // PRIORITY 3: No ref in URL — check sessionStorage for existing session
        try {
            currentRefCode = sessionStorage.getItem(SESSION_KEY);
        } catch {
            currentRefCode = null;
        }

        if (import.meta.env.DEV && currentRefCode) {
            console.log('[AffiliateRef] Restored ref code from session:', currentRefCode);
        }
    }
}

/**
 * Get the current affiliate referral code.
 * Returns null if not in affiliate mode.
 */
export function getAffiliateRefCode(): string | null {
    return currentRefCode;
}

/**
 * Check if the current session is in affiliate referral mode.
 */
export function isAffiliateRefActive(): boolean {
    return currentRefCode !== null && currentRefCode.length > 0;
}

/**
 * Clear the affiliate referral code.
 * Used when the user explicitly navigates away from affiliate mode.
 */
export function clearAffiliateRef(): void {
    currentRefCode = null;
    try {
        sessionStorage.removeItem(SESSION_KEY);
    } catch {
        // Ignore
    }

    if (import.meta.env.DEV) {
        console.log('[AffiliateRef] Ref code cleared');
    }
}
