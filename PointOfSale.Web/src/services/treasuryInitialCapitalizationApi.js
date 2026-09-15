import api, { getSafeApiErrorMessage } from './api'

export const TREASURY_CAPITALIZATION_DECLARATION = 'TREASURY_CUTOVER_FUNDS_V1'

export const createCapitalizationOperationId = () => {
    if (typeof globalThis.crypto?.randomUUID === 'function') {
        return globalThis.crypto.randomUUID()
    }

    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (character) => {
        const random = Math.floor(Math.random() * 16)
        const value = character === 'x' ? random : (random & 0x3) | 0x8
        return value.toString(16)
    })
}

export const resolveCapitalizationAttempt = (previousAttempt, fingerprint) => (
    previousAttempt?.fingerprint === fingerprint
        ? previousAttempt
        : { fingerprint, operationId: createCapitalizationOperationId() }
)

const ERROR_KEYS = {
    AUTHORIZATION_REQUIRED: 'permissionDenied',
    INITIAL_CAPITALIZATION_ALREADY_EXISTS: 'alreadyExists',
    INITIAL_CAPITALIZATION_NOT_ELIGIBLE: 'notEligible',
    INITIAL_CAPITALIZATION_NOT_FOUND: 'notFound',
    INVALID_TREASURY_DESTINATION: 'invalidDestination',
    INVALID_OPENING_EQUITY: 'openingEquityUnavailable',
    INVALID_CUTOVER_DATE: 'invalidCutoverDate',
    INVALID_ALLOCATION: 'invalidAllocation',
    INSUFFICIENT_FUNDS: 'insufficientFunds',
    IDEMPOTENCY_CONFLICT: 'duplicateRequest',
    CONCURRENCY_CONFLICT: 'staleMutation',
}

export const getCapitalizationErrorMessage = (error, t, fallbackKey = 'treasuryCapitalization.operationFailed') => {
    const code = error?.response?.data?.code
    if (ERROR_KEYS[code]) return t(`treasuryCapitalization.errors.${ERROR_KEYS[code]}`)
    if (error?.response?.status === 403) return t('treasuryCapitalization.errors.permissionDenied')
    if (error?.response?.status === 404) return t('treasuryCapitalization.errors.notFound')
    if (error?.response?.status >= 500) return t('treasuryCapitalization.errors.serviceUnavailable')
    return getSafeApiErrorMessage(error, t(fallbackKey))
}

export const treasuryInitialCapitalizationApi = {
    listDestinations: async ({ signal } = {}) => {
        const response = await api.get('/treasury/initial-capitalizations/destinations', { signal, skipAutomaticRetry: true })
        return Array.isArray(response.data) ? response.data : []
    },

    list: async ({ signal } = {}) => {
        const response = await api.get('/treasury/initial-capitalizations', { signal, skipAutomaticRetry: true })
        return Array.isArray(response.data) ? response.data : []
    },

    create: async (data) => {
        const response = await api.post('/treasury/initial-capitalizations', data)
        return response.data
    },

    reverse: async (id, data) => {
        const response = await api.post(`/treasury/initial-capitalizations/${id}/reverse`, data)
        return response.data
    },
}

export default treasuryInitialCapitalizationApi
