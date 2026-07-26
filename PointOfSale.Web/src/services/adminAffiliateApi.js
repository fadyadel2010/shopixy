/**
 * AF-4B: Admin Affiliate Management API Service
 *
 * All admin-only endpoints for managing affiliates.
 * Role-gated: Owner, Admin only.
 */
import api from './api'

export const AFFILIATE_ADMIN_READ_HEADERS = {
    'Cache-Control': 'no-store',
    Pragma: 'no-cache',
}

const affiliateReadOptions = (options = {}) => ({
    ...options,
    headers: {
        ...AFFILIATE_ADMIN_READ_HEADERS,
        ...(options.headers || {}),
    },
})

const adminAffiliateApi = {
    // ─── READ ────────────────────────────────────────────────────────

    /** List all affiliates with optional filters */
    listAffiliates: (page = 1, pageSize = 20, status = '', search = '') =>
        api.get('/admin/affiliates', affiliateReadOptions({
            params: {
                page,
                pageSize,
                ...(status && { status }),
                ...(search && { search }),
            }
        })).then(r => r.data),

    /** Get single affiliate detail (identity + financial summary + payout wallet) */
    getAffiliateDetail: (id) =>
        api.get(`/admin/affiliates/${id}`, affiliateReadOptions()).then(r => r.data),

    /** Get affiliate orders (paginated, filterable by status) */
    getAffiliateOrders: (id, page = 1, pageSize = 20, status = '') =>
        api.get(`/admin/affiliates/${id}/orders`, affiliateReadOptions({
            params: {
                page,
                pageSize,
                ...(status && { status }),
            }
        })).then(r => r.data),

    /** Get affiliate earnings per currency */
    getAffiliateEarnings: (id) =>
        api.get(`/admin/affiliates/${id}/earnings`, affiliateReadOptions()).then(r => r.data),

    // ─── AF-38C: WITHDRAWAL ISOLATION FIX ──────────────────────────────

    /** 
     * AF-38C: Get withdrawal requests for a SPECIFIC affiliate 
     * DATA ISOLATION: Scoped to single affiliate
     */
    getAffiliateWithdrawals: (affiliateId, status = '', page = 1, pageSize = 20) =>
        api.get(`/admin/affiliates/${affiliateId}/withdrawals`, affiliateReadOptions({
            params: { ...(status && { status }), page, pageSize }
        })).then(r => r.data),

    // ─── WRITE ───────────────────────────────────────────────────────

    /** Approve earned commissions (specific orders or all) */
    approveCommissions: (id, orderIds = [], applyAll = false) =>
        api.post(`/admin/affiliates/${id}/approve`, {
            orderIds: applyAll ? [] : orderIds,
            applyAll,
        }).then(r => r.data),

    /** Record payment for approved commissions */
    recordPayment: (id, orderIds = [], applyAll = false) =>
        api.post(`/admin/affiliates/${id}/pay`, {
            orderIds: applyAll ? [] : orderIds,
            applyAll,
        }).then(r => r.data),

    /** Suspend affiliate account */
    suspendAffiliate: (id, reason) =>
        api.post(`/admin/affiliates/${id}/suspend`, { reason }).then(r => r.data),

    /** Activate (or reactivate) affiliate account */
    activateAffiliate: (id) =>
        api.post(`/admin/affiliates/${id}/activate`).then(r => r.data),

    // ─── AF-38: WITHDRAWAL MANAGEMENT ──────────────────────────────────

    /** List withdrawal requests (filterable by status) */
    listPayoutRequests: (status = '', page = 1, pageSize = 20) =>
        api.get('/admin/affiliates/payouts', affiliateReadOptions({
            params: { ...(status && { status }), page, pageSize }
        })).then(r => r.data),

    /** Approve a pending withdrawal request */
    approvePayoutRequest: (requestId, adminNotes = '') =>
        api.post(`/admin/affiliates/payouts/${requestId}/approve`, { adminNotes }).then(r => r.data),

    /** Reject a pending withdrawal request */
    rejectPayoutRequest: (requestId, adminNotes = '') =>
        api.post(`/admin/affiliates/payouts/${requestId}/reject`, { adminNotes }).then(r => r.data),

    /** Mark an approved withdrawal request as paid */
    markPayoutPaid: (requestId, paymentReference = '', adminNotes = '') =>
        api.post(`/admin/affiliates/payouts/${requestId}/mark-paid`, { paymentReference, adminNotes }).then(r => r.data),
}

export default adminAffiliateApi
