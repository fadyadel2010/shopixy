import api from './api'

const buildMutationKey = (prefix, data) => {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
        return `${prefix}-${crypto.randomUUID()}`
    }

    const source = `${Date.now()}-${Math.random()}-${JSON.stringify(data ?? {})}`
    let hash = 0
    for (let index = 0; index < source.length; index += 1) {
        hash = ((hash << 5) - hash + source.charCodeAt(index)) | 0
    }
    return `${prefix}-${Date.now()}-${Math.abs(hash)}`
}

// Statement of Account API
export const soaApi = {
    // Get statement of account
    getStatement: async (filters, options = {}) => {
        const response = await api.post('/StatementOfAccount/query', filters, {
            signal: options.signal,
        })
        return response.data
    },

    // Get party balance
    getPartyBalance: async (partyType, partyId, options = {}) => {
        const response = await api.get(`/StatementOfAccount/balance/${partyType}/${partyId}`, {
            signal: options.signal,
        })
        return response.data
    },

    // Create opening balance
    createOpeningBalance: async (data, options = {}) => {
        const response = await api.post('/StatementOfAccount/opening-balance', data, {
            signal: options.signal,
        })
        return response.data
    },

    // Create adjustment
    createAdjustment: async (data, options = {}) => {
        const response = await api.post('/StatementOfAccount/adjustment', data, {
            headers: {
                'Idempotency-Key': options.idempotencyKey ?? buildMutationKey('soa-adjustment', data),
            },
            signal: options.signal,
        })
        return response.data
    },

    // Get outstanding balances
    getOutstandingBalances: async (partyType, options = {}) => {
        const page = options.page ?? 1
        const pageSize = options.pageSize ?? 50
        const search = options.search?.trim()

        const response = await api.get(`/StatementOfAccount/outstanding/${partyType}`, {
            params: {
                page,
                pageSize,
                search: search || undefined,
            },
            signal: options.signal,
        })
        return {
            items: response.data,
            totalCount: Number(response.headers['x-total-count'] ?? response.data?.length ?? 0),
            totalPages: Number(response.headers['x-total-pages'] ?? 0),
            page: Number(response.headers['x-page'] ?? page),
            pageSize: Number(response.headers['x-page-size'] ?? pageSize),
            totalDebit: Number(response.headers['x-total-debit'] ?? 0),
            totalCredit: Number(response.headers['x-total-credit'] ?? 0),
        }
    },
}

// Payments API
export const paymentsApi = {
    // Receive payment from customer
    receivePayment: async (data, options = {}) => {
        const response = await api.post('/Payments/receive', data, {
            signal: options.signal,
        })
        return response.data
    },

    // Make payment to supplier
    makePayment: async (data, options = {}) => {
        const response = await api.post('/Payments/make', data, {
            signal: options.signal,
        })
        return response.data
    },

    // Get customer payment history
    getCustomerPayments: async (customerId, options = {}) => {
        const response = await api.get(`/Payments/customer/${customerId}`, {
            signal: options.signal,
        })
        return response.data
    },

    // Get supplier payment history
    getSupplierPayments: async (supplierId, options = {}) => {
        const response = await api.get(`/Payments/supplier/${supplierId}`, {
            signal: options.signal,
        })
        return response.data
    },
}

export default { soaApi, paymentsApi }
