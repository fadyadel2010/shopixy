/**
 * Bootstrap Module Index
 * 
 * Exports all bootstrap-related functionality.
 */

// Main bootstrap
export {
    bootstrap,
    resetBootstrap,
    isBootstrapped,
    getBootstrapState,
    BootstrapErrorCode,
    type BootstrapResult,
    type BootstrapError,
} from './bootstrap';

// Tenant resolver
export {
    resolveTenant,
    getTenantId,
    isTenantResolved,
    setResolvedTenant,
    clearResolvedTenant,
    type TenantResolutionResult,
} from './tenantResolver';

// Session manager
export {
    getSessionId,
    setSessionId,
    initializeSession,
    clearSession,
    hasSession,
} from './sessionManager';

// Request context
export {
    getRequestHeaders,
    isRequestContextReady,
    setCustomerId,
    getCustomerId,
    isAuthenticated,
    type RequestHeaders,
} from './requestContext';

// Error utilities
export {
    createBootstrapError,
    mapApiErrorToBootstrapError,
    isNetworkError,
} from './bootstrapErrors';

// AF-12: Affiliate referral code tracking
export {
    initAffiliateRefTracker,
    getAffiliateRefCode,
    isAffiliateRefActive,
    clearAffiliateRef,
} from './affiliateRefTracker';
