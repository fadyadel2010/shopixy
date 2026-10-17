import api from './api'

const route = '/treasury/carrier-remittances'
export const getCarrierRemittanceOrders = async (page = 1, signal) =>
    (await api.get(`${route}/eligible-orders`, { params: { page }, signal, headers: { 'Cache-Control': 'no-store' } })).data
export const receiveCarrierCash = async request => (await api.post(route, request)).data
export const getCarrierRemittance = async (operationId, signal) =>
    (await api.get(`${route}/${operationId}`, { signal, headers: { 'Cache-Control': 'no-store' } })).data

const nonempty = value => typeof value === 'string' && value.trim().length > 0
const currencyCode = value => typeof value === 'string' && /^[A-Z]{3}$/.test(value)
const validTime = value => nonempty(value) && Number.isFinite(new Date(value).getTime())

export function admitCarrierSelection(data) {
    if (!data || !Array.isArray(data.orders) || data.orders.length > 100 || !currencyCode(data.currency) ||
        data.destinationAccountCode !== '1010' || new Set(data.orders.map(o => o?.orderId)).size !== data.orders.length ||
        data.orders.some(o => !o || !nonempty(o.orderId) || !nonempty(o.orderReference) ||
            !Number.isFinite(o.amount) || o.amount <= 0 || o.currency !== data.currency ||
            !nonempty(o.carrierCode) || !nonempty(o.configurationId) || !nonempty(o.accountId) ||
            !['Sandbox', 'Production'].includes(o.environment))) {
        throw new Error('Unverified carrier selection')
    }
    return data
}

export function admitCarrierReceipt(result, request, expected) {
    if (!result || result.outcome !== 'Posted' || result.accountingOutcome !== 'Posted' || !result.journalId || !result.journalReference ||
        result.confirmationBasis !== 'OperatorAttestedPhysicalReceipt' || result.receiptChannel !== 'PhysicalMerchantCash' ||
        result.destinationAccountCode !== '1010' || !nonempty(result.destinationAccountId) || !Number.isFinite(result.receivedAmount) ||
        !(result.receivedAmount > 0) || result.receivedAmount !== result.grossAmount || result.fees !== 0 ||
        !currencyCode(result.currency) || !result.originalOperatorId || !validTime(result.recordedAtUtc) || !validTime(result.postedAtUtc) || !validTime(result.receivedAtUtc) ||
        !nonempty(result.carrierCode) || !nonempty(result.configurationId) || !nonempty(result.accountId) || !['Sandbox', 'Production'].includes(result.environment) ||
        result.operationId !== request.operationId || !Array.isArray(result.orderIds) ||
        result.orderIds.length < 1 || result.orderIds.length > 100 || result.orderIds.some(id => !nonempty(id)) || new Set(result.orderIds).size !== result.orderIds.length ||
        (request.receiptReference && result.receiptReference !== request.receiptReference.trim().normalize('NFKC')) ||
        (request.receivedAt && new Date(result.receivedAtUtc).getTime() !== new Date(request.receivedAt).getTime()) ||
        (request.orderIds && [...result.orderIds].sort().join(',') !== [...request.orderIds].sort().join(',')) ||
        (expected && ['receivedAmount', 'currency', 'carrierCode', 'configurationId', 'accountId', 'environment'].some(key => result[key] !== expected[key]))) {
        throw new Error('Unverified carrier receipt result')
    }
    return result
}
