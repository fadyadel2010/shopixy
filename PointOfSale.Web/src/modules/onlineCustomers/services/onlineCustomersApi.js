/**
 * Online Customers API Service
 * Phase P1: POS Admin - Online Customers Management (Storefront)
 * 
 * ARCHITECTURAL RULES:
 * - Uses existing POS api.js client (auth + tenant context)
 * - Calls Admin APIs only - NO Storefront APIs
 * - PostgreSQL is source of truth for writes
 * - MongoDB provides read-optimized views
 * - All actions are auditable
 * 
 * SECURITY:
 * - Tenant scope comes from authenticated backend context
 * - Role validation on server side
 * - All responses tenant-isolated
 */

import api from '../../../services/api'

/**
 * Online Customers Admin API Service
 */
export const onlineCustomersApi = {
    // =========================================================================
    // ONLINE CUSTOMERS LIST & DETAILS
    // =========================================================================

    /**
     * Get paginated list of online customers
     * @param {Object} params - Query parameters
     * @param {number} params.page - Page number (1-indexed)
     * @param {number} params.pageSize - Items per page
     * @param {string} params.search - Search term (email, display name)
     * @param {boolean} params.isActive - Filter by active status
     * @param {string} params.provider - Filter by auth provider
     * @returns {Promise<Object>} Paginated customer list
     */
    async getOnlineCustomers(params = {}) {
        const response = await api.get('/admin/online-customers', { params })
        return response.data
    },

    /**
     * Get single online customer details
     * @param {string} customerId - Customer GUID
     * @returns {Promise<Object>} Customer details
     */
    async getOnlineCustomer(customerId) {
        const response = await api.get(`/admin/online-customers/${customerId}`)
        return response.data
    },

    // =========================================================================
    // ACCOUNT STATUS ACTIONS
    // =========================================================================

    /**
     * Activate an online customer account
     * Enables login for the customer
     * @param {string} customerId - Customer GUID
     * @returns {Promise<void>}
     */
    async activateCustomer(customerId) {
        await api.post(`/admin/online-customers/${customerId}/activate`)
    },

    /**
     * Deactivate an online customer account
     * CRITICAL: This will:
     * - Block all future logins
     * - Revoke all refresh tokens
     * - Blacklist all active JWTs
     * @param {string} customerId - Customer GUID
     * @param {string} reason - Reason for deactivation (required for audit)
     * @returns {Promise<void>}
     */
    async deactivateCustomer(customerId, reason) {
        await api.post(`/admin/online-customers/${customerId}/deactivate`, { reason })
    },

    // =========================================================================
    // SESSION MANAGEMENT
    // =========================================================================

    /**
     * Get all sessions for a customer
     * Each session = one refresh token = one logged-in device
     * @param {string} customerId - Customer GUID
     * @param {boolean} includeRevoked - Include revoked sessions (default: true)
     * @returns {Promise<Object>} Session list with total count
     */
    async getCustomerSessions(customerId, includeRevoked = true) {
        const response = await api.get(`/admin/online-customers/${customerId}/sessions`, {
            params: { includeRevoked }
        })
        return response.data
    },

    /**
     * Revoke a single session
     * CRITICAL: Immediate effect - customer is logged out from that device
     * @param {string} sessionId - Session GUID (= refresh token ID)
     * @param {string} reason - Optional reason for audit
     * @returns {Promise<Object>} Result with affected count
     */
    async revokeSession(sessionId, reason) {
        const response = await api.post(`/admin/online-customers/sessions/${sessionId}/revoke`, {
            reason
        })
        return response.data
    },

    /**
     * Revoke ALL sessions for a customer
     * CRITICAL: Force logout from ALL devices immediately
     * @param {string} customerId - Customer GUID
     * @param {string} reason - Reason for bulk revocation (required for audit)
     * @returns {Promise<Object>} Result with affected count
     */
    async revokeAllSessions(customerId, reason) {
        const response = await api.post(`/admin/online-customers/${customerId}/revoke-sessions`, {
            reason
        })
        return response.data
    },

    // =========================================================================
    // WISHLIST (Read-Only)
    // Phase 10.3.6 - Admin Wishlist Visibility
    // =========================================================================

    /**
     * Get a customer's wishlist (read-only admin view)
     * For customer insight, marketing, and fraud detection
     * @param {string} customerId - Customer GUID
     * @returns {Promise<Object>} Wishlist with items and count
     */
    async getCustomerWishlist(customerId) {
        const response = await api.get(`/admin/online-customers/${customerId}/wishlist`)
        return response.data
    },
}

export default onlineCustomersApi
