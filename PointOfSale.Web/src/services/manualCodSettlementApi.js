import api from './api'

export const getManualCodOrders = async (page = 1, signal) =>
    (await api.get('/payments/cod/manual-settlements/eligible-orders', { params: { page }, signal })).data

export const settleManualCod = async (request) =>
    (await api.post('/payments/cod/manual-settlements', request)).data

export const getManualCodSettlement = async (settlementId, signal) =>
    (await api.get(`/payments/cod/manual-settlements/${settlementId}`, { signal })).data
