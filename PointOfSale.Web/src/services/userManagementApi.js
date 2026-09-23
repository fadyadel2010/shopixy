import api from './api'

const BASE = '/admin/users'

const withVersion = (version) => (version ? { headers: { 'If-Match': `"${version}"` } } : undefined)

export const listPosUsers = (params, config) =>
    api.get(BASE, { params, ...(config || {}) })

export const getPosUser = (userId, config) =>
    api.get(`${BASE}/${userId}`, config)

export const getPosUserRoles = (config) =>
    api.get(`${BASE}/roles`, config)

export const getPosUserAudit = (userId, params, config) =>
    api.get(`${BASE}/${userId}/audit`, { params, ...(config || {}) })

export const invitePosUser = (payload) =>
    api.post(`${BASE}/invitations`, payload)

export const resendPosUserInvitation = (userId, version) =>
    api.post(`${BASE}/${userId}/invitation/resend`, null, withVersion(version))

export const revokePosUserInvitation = (userId, reason, version) =>
    api.delete(`${BASE}/${userId}/invitation`, {
        params: reason ? { reason } : undefined,
        ...(withVersion(version) || {}),
    })

export const updatePosUserProfile = (userId, payload, version) =>
    api.patch(`${BASE}/${userId}/profile`, payload, withVersion(version))

export const changePosUserRole = (userId, role, version) =>
    api.put(`${BASE}/${userId}/role`, { role }, withVersion(version))

export const changePosUserStatus = (userId, isActive, reason, version) =>
    api.put(`${BASE}/${userId}/status`, { isActive, reason }, withVersion(version))

export const issuePosUserPasswordReset = (userId, version) =>
    api.post(`${BASE}/${userId}/password-reset`, null, withVersion(version))

export const revokePosUserSession = (userId, reason, version) =>
    api.post(`${BASE}/${userId}/session/revoke`, null, {
        params: reason ? { reason } : undefined,
        ...(withVersion(version) || {}),
    })

export const transferPosOwnership = (targetUserId, targetVersion, currentOwnerVersion) =>
    api.post(`${BASE}/ownership-transfer`, { targetUserId, targetVersion }, withVersion(currentOwnerVersion))

export const acceptPosUserInvitation = (token, password) =>
    api.post('/auth/user-invitations/accept', { token, password })

export const completePosPasswordReset = (token, password) =>
    api.post('/auth/password-reset/complete', { token, password })
