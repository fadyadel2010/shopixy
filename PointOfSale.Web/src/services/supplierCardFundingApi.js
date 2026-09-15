import api, { getSafeApiErrorMessage } from './api'

export const SUPPLIER_CARD_ROLES = ['Owner', 'Admin', 'Manager']
export const SUPPLIER_CARD_ADMIN_ROLES = ['Owner', 'Admin']

export const canAdministerSupplierCards = (role) => SUPPLIER_CARD_ADMIN_ROLES.includes(role)
export const canAccessSupplierCards = (role) => SUPPLIER_CARD_ROLES.includes(role)

export const formatSupplierCardLabel = (instrument) => {
    if (!instrument) return ''
    const maskedIdentifier = instrument.last4 ? ` ****${instrument.last4}` : ''
    return `${instrument.displayName}${maskedIdentifier}`
}

export const purchaseRequiresSupplierCardInstrument = ({
    paymentMethod,
    paidAmount,
    initialPaymentMethod,
    cardAmount,
}) => {
    if (paymentMethod === 'Card') return true
    if (paymentMethod === 'Mixed') return Number(cardAmount) > 0
    return paymentMethod === 'Credit'
        && Number(paidAmount) > 0
        && (initialPaymentMethod === 'Card'
            || (initialPaymentMethod === 'Mixed' && Number(cardAmount) > 0))
}

export const getSupplierCardAuthorityFields = (required, instrument) => ({
    fundingInstrumentId: required && instrument ? instrument.id : null,
    fundingInstrumentVersion: required && instrument ? instrument.authorityVersion : null,
})

export const createSupplierCardOperationId = (prefix = 'supplier-card') => {
    const suffix = typeof globalThis.crypto?.randomUUID === 'function'
        ? globalThis.crypto.randomUUID()
        : `${Date.now()}-${Math.random().toString(36).slice(2, 10)}`
    return `${prefix}-${suffix}`
}

export const resolveSupplierCardAttempt = (previousAttempt, fingerprint, prefix) => (
    previousAttempt?.fingerprint === fingerprint
        ? previousAttempt
        : { fingerprint, idempotencyKey: createSupplierCardOperationId(prefix) }
)

const ERROR_PATTERNS = [
    { pattern: /insufficient funded card balance/i, key: 'insufficientBalance' },
    { pattern: /fundinginstrumentid is required|fundinginstrumentversion is required/i, key: 'instrumentRequired' },
    { pattern: /authority changed|changed concurrently|concurrent financial mutation/i, key: 'staleMutation' },
    { pattern: /idempotency-key is already bound|duplicate request/i, key: 'duplicateRequest' },
    { pattern: /funding source/i, key: 'fundingSourceUnavailable' },
    { pattern: /ledger account|account identity|account authority/i, key: 'wrongAccountAuthority' },
    { pattern: /unavailable for this tenant|active instrument authority was not found|instrument.*not found|instrument.*deleted/i, key: 'instrumentUnavailable' },
]

export const getSupplierCardErrorMessage = (error, t, fallbackKey = 'supplierCards.operationFailed') => {
    const code = error?.response?.data?.code
    const backendMessage = error?.response?.data?.message || ''

    if (code === 'INSUFFICIENT_FUNDS') {
        return t('supplierCards.errors.insufficientFunds')
    }

    if (code === 'SUPPLIER_CARD_AUTHORITY_INVALID') {
        const match = ERROR_PATTERNS.find(item => item.pattern.test(backendMessage))
        return t(`supplierCards.errors.${match?.key || 'instrumentUnavailable'}`)
    }

    const match = ERROR_PATTERNS.find(item => item.pattern.test(backendMessage))
    if (match) return t(`supplierCards.errors.${match.key}`)
    if (error?.response?.status === 403) return t('supplierCards.errors.permissionDenied')
    if (error?.response?.status === 404) return t('supplierCards.errors.instrumentNotFound')
    if (error?.response?.status >= 500) return t('supplierCards.errors.serviceUnavailable')

    return getSafeApiErrorMessage(error, t(fallbackKey))
}

export const supplierCardFundingApi = {
    list: async ({ activeOnly = false, signal } = {}) => {
        const response = await api.get('/supplier-card-funding-instruments', {
            params: activeOnly ? { activeOnly: true } : undefined,
            signal,
        })
        return Array.isArray(response.data) ? response.data : []
    },

    listFundingSources: async ({ signal } = {}) => {
        const response = await api.get('/supplier-card-funding-instruments/funding-sources', { signal })
        return Array.isArray(response.data) ? response.data : []
    },

    create: async (data) => {
        const response = await api.post('/supplier-card-funding-instruments', data)
        return response.data
    },

    update: async (id, data) => {
        const response = await api.put(`/supplier-card-funding-instruments/${id}`, data)
        return response.data
    },

    fund: async (id, data, idempotencyKey) => {
        const response = await api.post(
            `/supplier-card-funding-instruments/${id}/fundings`,
            data,
            { headers: { 'Idempotency-Key': idempotencyKey } },
        )
        return response.data
    },

    statement: async (id, { from, to, signal } = {}) => {
        const response = await api.get(`/supplier-card-funding-instruments/${id}/statement`, {
            params: { from: from || undefined, to: to || undefined },
            signal,
        })
        return response.data
    },
}

export default supplierCardFundingApi
