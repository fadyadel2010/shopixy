/**
 * Domain-specific read hook utilities (DOMAIN-3.1-B).
 *
 * Shared helpers for state management and error classification.
 */

import { getSafeApiErrorMessage } from '../../../../services/api'

/**
 * Async data state machine values used across domain read hooks.
 * - idle: initial, never loaded
 * - loading: first load in progress
 * - refreshing: background re-fetch while data is still present
 * - success: data loaded successfully
 * - empty: loaded successfully but no data returned
 * - error: non-auth error during fetch
 * - unauthorized: 401/403 from backend
 * - not-found: 404 from backend
 */
export const LOAD_STATES = {
    IDLE: 'idle',
    LOADING: 'loading',
    REFRESHING: 'refreshing',
    SUCCESS: 'success',
    EMPTY: 'empty',
    ERROR: 'error',
    UNAUTHORIZED: 'unauthorized',
    NOT_FOUND: 'not-found',
}

/**
 * Classifies an API error into one of the LOAD_STATES values.
 * @param {Error} error - The axios error object
 * @returns {string} One of the LOAD_STATES values
 */
export const classifyError = (error) => {
    const status = error?.response?.status

    if (status === 401 || status === 403) {
        return LOAD_STATES.UNAUTHORIZED
    }

    if (status === 404) {
        return LOAD_STATES.NOT_FOUND
    }

    return LOAD_STATES.ERROR
}

/**
 * Extracts a safe error message from an API error.
 * @param {Error} error - The axios error object
 * @param {string} fallback - Fallback message if no backend message
 * @returns {string} Safe error message
 */
export const getDomainErrorMessage = (error, fallback = 'Unable to load data. Please try again.') => {
    return getSafeApiErrorMessage(error, fallback)
}
