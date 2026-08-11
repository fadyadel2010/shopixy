/**
 * useDomainMutations hook
 * DOMAIN-3.1-C: Storefront Domain Management — WRITE PATH mutations.
 *
 * Provides transactional mutation hooks for all six domain management commands:
 *   createDomain, updateDomain, archiveDomain, restoreDomain,
 *   deleteDomain, promotePrimaryDomain.
 *
 * Architecture Law: UI ↓ Typed API Client ↓ Controller.
 * This hook wraps the domainManagementService with:
 *   - Loading/error state management
 *   - Optimistic concurrency version tracking
 *   - User-facing error classification (4xx → user messages, 5xx → server error)
 *   - Callback hooks for success/failure (for refetching parent data)
 *
 * Concurrency Strategy:
 *   - Uses the Version field from DomainDetailDto as optimistic concurrency token
 *   - On 409 conflict, returns { isConcurrencyConflict: true, serverVersion }
 *   - Caller should refetch and retry if needed
 */

import { useState, useCallback } from 'react'
import domainManagementService from '../services/domainManagementService'
import { getSafeApiErrorMessage } from '../../../../services/api'

/**
 * @typedef {Object} MutationState
 * @property {boolean} isMutating - true while a mutation is in flight
 * @property {string|null} error - user-facing error message
 * @property {boolean} isConcurrencyConflict - true on 409 conflict with version mismatch
 * @property {number|null} serverVersion - the server's current version when a conflict occurs
 * @property {string|null} errorCode - backend error code (e.g. "DOMAIN_NOT_FOUND", "DOMAIN_CONFLICT")
 */

/**
 * @param {Object} options
 * @param {Object} [options.mutationOptions] - { onSuccess, onError } callbacks
 * @returns {MutationState & Object} state + mutation functions
 */
export function useDomainMutations({ mutationOptions = {} } = {}) {
    const [isMutating, setIsMutating] = useState(false)
    const [error, setError] = useState(null)
    const [errorCode, setErrorCode] = useState(null)
    const [isConcurrencyConflict, setIsConcurrencyConflict] = useState(false)
    const [serverVersion, setServerVersion] = useState(null)
    const [lastMutation, setLastMutation] = useState(null)
    const [mutationStatus, setMutationStatus] = useState('idle')

    const { onSuccess, onError } = mutationOptions

    /**
     * Execute a mutation with full error handling and state management.
     * @param {Function} fn - async function that calls the API
     * @param {string} operationName - for logging
     * @param {Object} [options] - { suppressError } to prevent error callback
     * @returns {Promise<*>} - returns the API response data
     */
    const executeMutation = useCallback(async (fn, operationName, options = {}) => {
        setIsMutating(true)
        setError(null)
        setErrorCode(null)
        setIsConcurrencyConflict(false)
        setServerVersion(null)
        setLastMutation(operationName)
        setMutationStatus('loading')

        try {
            const result = await fn()
            setMutationStatus('success')

            if (onSuccess) {
                onSuccess(result, operationName)
            }

            return result
        } catch (err) {
            const safeMessage = getSafeApiErrorMessage(err, `Failed to ${operationName}`)

            // Classify error based on HTTP status
            const status = err?.response?.status
            const apiError = err?.response?.data

            if (status === 404) {
                setError('The domain could not be found. It may have been deleted.')
                setErrorCode('DOMAIN_NOT_FOUND')
                setMutationStatus('domain-failure')
            } else if (status === 409) {
                setIsConcurrencyConflict(true)
                setErrorCode(apiError?.code || 'CONFLICT')
                setMutationStatus('domain-failure')

                // Try to extract server version from error details
                if (apiError?.serverVersion) {
                    setServerVersion(apiError.serverVersion)
                }

                if (apiError?.message) {
                    setError(apiError.message)
                } else {
                    setError('A conflict occurred — another user may have modified this domain.')
                }
            } else if (status === 400) {
                setError(safeMessage)
                setErrorCode(apiError?.code || 'VALIDATION_ERROR')
                setMutationStatus('domain-failure')
            } else if (status === 401 || status === 403) {
                setError('You are not authorized to perform this action.')
                setErrorCode('UNAUTHORIZED')
                setMutationStatus('domain-failure')
            } else if (status === 422 || status === 429) {
                setError(apiError?.message || safeMessage)
                setErrorCode(apiError?.code || apiError?.failureReason || 'VERIFICATION_FAILED')
                setMutationStatus('domain-failure')
            } else if (status >= 500) {
                setError(safeMessage)
                setErrorCode(apiError?.code || 'INFRASTRUCTURE_ERROR')
                setMutationStatus('infrastructure-error')
            } else {
                setError(safeMessage)
                setErrorCode('INTERNAL_ERROR')
                setMutationStatus('infrastructure-error')
            }

            if (!options.suppressError && onError) {
                onError(err, operationName)
            }

            throw err
        } finally {
            setIsMutating(false)
        }
    }, [onError, onSuccess])

    // =========================================================================
    // Command: CreateDomain
    // =========================================================================
    const createDomain = useCallback(async (request, mutationOptions = {}) => {
        return executeMutation(
            () => domainManagementService.createDomain(request),
            'createDomain',
            mutationOptions,
        )
    }, [executeMutation])

    // =========================================================================
    // Command: UpdateDomain
    // =========================================================================
    const updateDomain = useCallback(async (domainId, request, mutationOptions = {}) => {
        return executeMutation(
            () => domainManagementService.updateDomain(domainId, request),
            'updateDomain',
            mutationOptions,
        )
    }, [executeMutation])

    // =========================================================================
    // Command: StartDomainVerification
    // =========================================================================
    const startDomainVerification = useCallback(async (domainId, request = {}, mutationOptions = {}) => {
        return executeMutation(
            () => domainManagementService.startDomainVerification(domainId, request),
            'startDomainVerification',
            mutationOptions,
        )
    }, [executeMutation])

    const verifyDomainNow = useCallback(async (domainId, request = {}, mutationOptions = {}) => {
        return executeMutation(
            () => domainManagementService.verifyDomainNow(domainId, request),
            'verifyDomainNow',
            mutationOptions,
        )
    }, [executeMutation])

    const retryDomainVerification = useCallback(async (domainId, request = {}, mutationOptions = {}) => {
        return executeMutation(
            () => domainManagementService.retryDomainVerification(domainId, request),
            'retryDomainVerification',
            mutationOptions,
        )
    }, [executeMutation])

    const refreshVerificationToken = useCallback(async (domainId, request = {}, mutationOptions = {}) => {
        return executeMutation(
            () => domainManagementService.refreshVerificationToken(domainId, request),
            'refreshVerificationToken',
            mutationOptions,
        )
    }, [executeMutation])

    const provisionSslCertificate = useCallback(async (domainId, request = {}, mutationOptions = {}) => {
        return executeMutation(
            () => domainManagementService.provisionSslCertificate(domainId, request),
            'provisionSslCertificate',
            mutationOptions,
        )
    }, [executeMutation])

    const activateSslCertificate = useCallback(async (domainId, request = {}, mutationOptions = {}) => {
        return executeMutation(
            () => domainManagementService.activateSslCertificate(domainId, request),
            'activateSslCertificate',
            mutationOptions,
        )
    }, [executeMutation])

    const renewSslCertificate = useCallback(async (domainId, request = {}, mutationOptions = {}) => {
        return executeMutation(
            () => domainManagementService.renewSslCertificate(domainId, request),
            'renewSslCertificate',
            mutationOptions,
        )
    }, [executeMutation])

    const revokeSslCertificate = useCallback(async (domainId, request = {}, mutationOptions = {}) => {
        return executeMutation(
            () => domainManagementService.revokeSslCertificate(domainId, request),
            'revokeSslCertificate',
            mutationOptions,
        )
    }, [executeMutation])

    const retrySslOperation = useCallback(async (domainId, request = {}, mutationOptions = {}) => {
        return executeMutation(
            () => domainManagementService.retrySslOperation(domainId, request),
            'retrySslOperation',
            mutationOptions,
        )
    }, [executeMutation])

    const refreshSslCertificate = useCallback(async (domainId, request = {}, mutationOptions = {}) => {
        return executeMutation(
            () => domainManagementService.refreshSslCertificate(domainId, request),
            'refreshSslCertificate',
            mutationOptions,
        )
    }, [executeMutation])

    // =========================================================================
    // Command: PromotePrimaryDomain (transactional)
    // =========================================================================
    const promotePrimaryDomain = useCallback(async (domainId, request = {}, mutationOptions = {}) => {
        return executeMutation(
            () => domainManagementService.promotePrimaryDomain(domainId, request),
            'promotePrimaryDomain',
            mutationOptions,
        )
    }, [executeMutation])

    // =========================================================================
    // Command: ArchiveDomain
    // =========================================================================
    const archiveDomain = useCallback(async (domainId, request = {}, mutationOptions = {}) => {
        return executeMutation(
            () => domainManagementService.archiveDomain(domainId, request),
            'archiveDomain',
            mutationOptions,
        )
    }, [executeMutation])

    // =========================================================================
    // Command: RestoreDomain
    // =========================================================================
    const restoreDomain = useCallback(async (domainId, request = {}, mutationOptions = {}) => {
        return executeMutation(
            () => domainManagementService.restoreDomain(domainId, request),
            'restoreDomain',
            mutationOptions,
        )
    }, [executeMutation])

    /**
     * @param {Guid} domainId
     * @param {Object} request - { reason? }
     * @param {Object} mutationOptions
     * @returns {Promise<Object>} DomainMutationResponse
     */
    const deleteDomain = useCallback(async (domainId, request = {}, mutationOptions = {}) => {
        return executeMutation(
            () => domainManagementService.deleteDomain(domainId, request),
            'deleteDomain',
            mutationOptions,
        )
    }, [executeMutation])

    /**
     * Reset error state (e.g. after user dismisses an error).
     */
    const clearError = useCallback(() => {
        setError(null)
        setErrorCode(null)
        setIsConcurrencyConflict(false)
        setServerVersion(null)
        setMutationStatus('idle')
    }, [])

    return {
        // State
        isMutating,
        error,
        mutationError: error,
        errorCode,
        isConcurrencyConflict,
        serverVersion,
        lastMutation,
        mutationStatus,

        // Mutations
        createDomain,
        updateDomain,
        startDomainVerification,
        verifyDomainNow,
        retryDomainVerification,
        refreshVerificationToken,
        provisionSslCertificate,
        activateSslCertificate,
        renewSslCertificate,
        revokeSslCertificate,
        retrySslOperation,
        refreshSslCertificate,
        archiveDomain,
        restoreDomain,
        deleteDomain,
        promotePrimaryDomain,

        // Helpers
        clearError,
    }
}

export default useDomainMutations
