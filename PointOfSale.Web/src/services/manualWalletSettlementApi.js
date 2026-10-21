import api from './api'

const base = '/payments/wallet/manual-settlements'
export const getManualWalletPayments = async (page = 1, signal) =>
    (await api.get(`${base}/eligible-payments`, { params: { page }, signal })).data
export const previewManualWalletSettlement = async (orderIds) =>
    (await api.post(`${base}/preview`, { orderIds })).data
export const settleManualWallet = async request => (await api.post(base, request)).data
export const getManualWalletSettlement = async (id, signal) => (await api.get(`${base}/${id}`, { signal })).data
