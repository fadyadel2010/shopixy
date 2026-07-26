import api from './api'
import { COMMERCEHUB_AUTHORITY_CURRENCY } from '../utils/commerceHubCurrencyAuthority'

function extractErrorMessage(error, fallbackMessage) {
    return (
        error?.response?.data?.message ||
        error?.response?.data?.error?.message ||
        error?.message ||
        fallbackMessage
    )
}

export function createVendorPaymentIdempotencyKey() {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
        return crypto.randomUUID()
    }

    return `vendor-payment-${Date.now()}-${Math.random().toString(16).slice(2)}`
}

export async function getCommerceHubPaymentsDashboard() {
    try {
        const response = await api.get('/admin/commercehub/payments')
        return response.data
    } catch (error) {
        throw new Error(extractErrorMessage(error, 'Unable to load CommerceHub payments dashboard.'))
    }
}

export async function createCommerceHubPaymentRequest({
    amount,
    methodId,
    transferReference,
    idempotencyKey,
    proofFile,
}) {
    const formData = new FormData()
    formData.append('amount', String(amount))
    formData.append('methodId', methodId)
    formData.append('currency', COMMERCEHUB_AUTHORITY_CURRENCY)
    formData.append('idempotencyKey', idempotencyKey)

    if (transferReference) {
        formData.append('transferReference', transferReference)
    }

    if (proofFile) {
        formData.append('proofFile', proofFile)
    }

    try {
        const response = await api.post('/admin/commercehub/payments', formData, {
            headers: {
                'Content-Type': 'multipart/form-data',
            },
        })

        return response.data
    } catch (error) {
        throw new Error(extractErrorMessage(error, 'Unable to submit the vendor payment request.'))
    }
}

export async function getCommerceHubPaymentProofUrl(paymentRequestId) {
    try {
        const response = await api.get(`/admin/commercehub/payments/${paymentRequestId}/proof-url`)
        return response.data
    } catch (error) {
        throw new Error(extractErrorMessage(error, 'Unable to load payment proof.'))
    }
}
