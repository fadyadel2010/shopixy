/**
 * Telemetry service for tracking errors and application events
 */
import { useAuthStore } from '../store/authStore'
import { appendCsrfHeader } from '../utils/csrf'

/**
 * Hash a value using SHA-256 to create a non-reversible identifier
 * @param {string} value - The value to hash
 * @returns {Promise<string>} - The hashed value as a hex string
 */
const hashValue = async (value) => {
    if (!value) return null
    
    try {
        const encoder = new TextEncoder()
        const data = encoder.encode(value)
        const hashBuffer = await crypto.subtle.digest('SHA-256', data)
        const hashArray = Array.from(new Uint8Array(hashBuffer))
        return hashArray.map(b => b.toString(16).padStart(2, '0')).join('')
    } catch {
        // If hashing fails, return null (don't send userId)
        return null
    }
}

/**
 * Track an exception/error
 * @param {Error} error - The error object
 * @param {Object} context - Additional context (componentStack, user info, etc.)
 */
export const trackException = async (error, context = {}) => {
    const errorData = {
        message: error.message,
        stack: error.stack,
        name: error.name,
        timestamp: new Date().toISOString(),
        url: window.location.origin + window.location.pathname,
        userAgent: navigator.userAgent,
        ...context,
    }

    // Get user context if available
    // NOTE: userId is hashed to prevent PII logging
    try {
        const user = useAuthStore.getState().user

        if (user?.tenantId) errorData.tenantId = user.tenantId
        
        // Hash userId to create a non-reversible identifier
        if (user?.id) {
            const hashedUserId = await hashValue(user.id)
            if (hashedUserId) errorData.hashedUserId = hashedUserId
        }
    } catch {
        // Telemetry context is optional and cannot become actor or tenant authority.
    }

    // Send to backend error tracking endpoint
    sendErrorToBackend(errorData).catch(() => {
        // Silently fail if backend is unavailable
    })

    // In development, also log to console
    if (import.meta.env.DEV) {
        console.error('Telemetry tracked exception:', errorData)
    }
}

/**
 * Send error data to backend
 * @param {Object} errorData - The error data to send
 */
const sendErrorToBackend = async (errorData) => {
    try {
        const headers = appendCsrfHeader({
            'Content-Type': 'application/json',
        }, 'post')

        await fetch('/api/telemetry/errors', {
            method: 'POST',
            headers,
            credentials: 'include',
            body: JSON.stringify(errorData),
            // Don't wait for response, fire and forget
            keepalive: true,
        })
    } catch {
        // Silently fail - don't throw errors from error tracking
    }
}

export default {
    trackException,
}
