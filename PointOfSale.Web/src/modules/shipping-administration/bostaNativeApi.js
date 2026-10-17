import api from '../../services/api'

const base = accountId => `/admin/shipping/carrier-accounts/${accountId}/bosta`
export const loadBostaNativeGeography = async (accountId, cityId) => (await api.get(`${base(accountId)}/native-geography`, { params: cityId ? { cityId } : {}, headers: { 'Cache-Control': 'no-store' } })).data
export const validateBostaNativeDestination = async (accountId, cityId, districtId) => (await api.post(`${base(accountId)}/destinations/validate`, { cityId, districtId })).data
export const saveBostaNativeDestination = async (accountId, shipmentId, cityId, districtId) => (await api.put(`${base(accountId)}/destinations/${shipmentId}`, { cityId, districtId })).data
export const readBostaNativeDestination = async shipmentId => (await api.get(`/admin/shipments/${shipmentId}/bosta-destination`, { headers: { 'Cache-Control': 'no-store' } })).data
