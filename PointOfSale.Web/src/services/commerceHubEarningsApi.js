import api from './api'

function extractErrorMessage(error, fallbackMessage) {
    return (
        error?.response?.data?.message ||
        error?.response?.data?.error?.message ||
        error?.message ||
        fallbackMessage
    )
}

export const COMMERCEHUB_PAYOUT_METHOD_TYPES = [
    'BankTransfer',
    'VodafoneCash',
    'InstaPay',
]

export async function getCommerceHubEarningsDashboard() {
    try {
        const response = await api.get('/commercehub/earnings')
        return response.data
    } catch (error) {
        throw new Error(extractErrorMessage(error, 'Unable to load CommerceHub earnings.'))
    }
}

export async function getVendorPayoutMethods() {
    try {
        const response = await api.get('/vendors/payout-methods')
        return response.data
    } catch (error) {
        throw new Error(extractErrorMessage(error, 'Unable to load vendor payout methods.'))
    }
}

export async function createVendorPayoutMethod(payload) {
    try {
        const response = await api.post('/vendors/payout-methods', payload)
        return response.data
    } catch (error) {
        throw new Error(extractErrorMessage(error, 'Unable to save the payout method.'))
    }
}
