/**
 * Bootstrap Error Types
 * 
 * Defines error types for bootstrap failures.
 * Each error type maps to a specific error screen.
 */

/**
 * Bootstrap error codes - map to specific error screens
 */
export enum BootstrapErrorCode {
    /** Tenant could not be resolved from subdomain or env */
    TENANT_RESOLUTION_FAILED = 'TENANT_RESOLUTION_FAILED',

    /** Store config returned 404 or tenant doesn't exist */
    STORE_NOT_FOUND = 'STORE_NOT_FOUND',

    /** Store is disabled/unavailable */
    STORE_UNAVAILABLE = 'STORE_UNAVAILABLE',

    /** Network error during bootstrap */
    NETWORK_ERROR = 'NETWORK_ERROR',

    /** Unknown/unexpected error */
    UNKNOWN_ERROR = 'UNKNOWN_ERROR',
}

/**
 * Bootstrap error with context
 */
export interface BootstrapError {
    code: BootstrapErrorCode;
    message: string;
    /** Original error for debugging */
    cause?: unknown;
    /** Whether the error is retryable */
    retryable: boolean;
}

/**
 * Create a bootstrap error
 */
export function createBootstrapError(
    code: BootstrapErrorCode,
    message: string,
    options?: { cause?: unknown; retryable?: boolean }
): BootstrapError {
    return {
        code,
        message,
        cause: options?.cause,
        retryable: options?.retryable ?? false,
    };
}

/**
 * Check if an error is a network error
 */
export function isNetworkError(error: unknown): boolean {
    if (error instanceof Error) {
        // Axios network error
        if (error.message === 'Network Error') return true;
        // Fetch abort/timeout
        if (error.name === 'AbortError') return true;
        // Generic connection issues
        if (error.message.includes('ECONNREFUSED')) return true;
        if (error.message.includes('ETIMEDOUT')) return true;
    }
    return false;
}

/**
 * Map API error to bootstrap error
 */
export function mapApiErrorToBootstrapError(
    status: number | undefined,
    apiCode: string | undefined,
    cause?: unknown
): BootstrapError {
    // Network error (no status)
    if (status === undefined) {
        return createBootstrapError(
            BootstrapErrorCode.NETWORK_ERROR,
            'Unable to connect to the server. Please check your internet connection.',
            { cause, retryable: true }
        );
    }

    // Tenant/Store not found
    if (status === 404) {
        return createBootstrapError(
            BootstrapErrorCode.STORE_NOT_FOUND,
            'This store could not be found. Please check the URL.',
            { cause, retryable: false }
        );
    }

    // Invalid tenant header
    if (status === 400 && (apiCode === 'TENANT_HEADER_MISSING' || apiCode === 'INVALID_TENANT_ID')) {
        return createBootstrapError(
            BootstrapErrorCode.TENANT_RESOLUTION_FAILED,
            'Unable to identify the store. Please check the URL.',
            { cause, retryable: false }
        );
    }

    // Server errors are retryable
    if (status >= 500) {
        return createBootstrapError(
            BootstrapErrorCode.NETWORK_ERROR,
            'The server is temporarily unavailable. Please try again.',
            { cause, retryable: true }
        );
    }

    // Unknown error
    return createBootstrapError(
        BootstrapErrorCode.UNKNOWN_ERROR,
        'An unexpected error occurred. Please try again.',
        { cause, retryable: true }
    );
}
