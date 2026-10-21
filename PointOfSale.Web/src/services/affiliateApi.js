/**
 * AF-4 / AF-5: Affiliate Portal API Service
 * AF-16: Payout Wallet Management
 *
 * RULES:
 * - All endpoints are read-only (GET) except wallet update
 * - Financial data comes from MongoDB projections
 * - Discovery data comes from Elasticsearch
 * - No business logic in this layer
 * - No commission rate ever exposed in discovery
 */
import { affiliateAxios } from './affiliateAuthApi'

const affiliateAuthorityReadConfig = {
    headers: {
        'Cache-Control': 'no-store',
        Pragma: 'no-cache',
    },
}

export const affiliateApi = {
    // Debug endpoint to check JWT claims
    debugClaims: () =>
        affiliateAxios.get('/affiliate/debug-claims').then(r => r.data),

    getDashboard: () =>
        affiliateAxios.get('/affiliate/dashboard', affiliateAuthorityReadConfig).then(r => r.data),

    getOrders: (page = 1, pageSize = 20, status = '') =>
        affiliateAxios.get('/affiliate/orders', { params: { page, pageSize, ...(status && { status }) } }).then(r => r.data),

    getEarningsSummary: () =>
        affiliateAxios.get('/affiliate/earnings-summary', affiliateAuthorityReadConfig).then(r => r.data),

    getProfile: () =>
        affiliateAxios.get('/affiliate/profile', affiliateAuthorityReadConfig).then(r => r.data),

    register: (displayName) =>
        affiliateAxios.post('/affiliate/register', { displayName }).then(r => r.data),

    // ─── AF-16: PAYOUT WALLET MANAGEMENT ─────────────────────────────

    /**
     * Update affiliate's payout wallet information.
     * @param {string} walletNumber - Wallet number (6-32 chars, alphanumeric)
     * @param {string} walletType - Wallet type (VodafoneCash, OrangeMoney, Bank, etc.)
     */
    updatePayoutWallet: (walletNumber, walletType) =>
        affiliateAxios.put('/affiliate/profile/payout-wallet', { walletNumber, walletType }).then(r => r.data),

    // ─── AF-5: Product Discovery ──────────────────────────────────

    /**
     * Discover products available for affiliate promotion.
     * All results are guaranteed affiliate-enabled, active, and in stock.
     * No commission rate is ever returned.
     *
     * @param {Object} params
     * @param {string} [params.q] - Search query
     * @param {string} [params.categoryId] - Category filter (GUID)
     * @param {number} [params.page=1] - Page number
     * @param {number} [params.pageSize=20] - Items per page
     * @returns {Promise<{page: number, pageSize: number, total: number, items: Array}>}
     */
    discoverProducts: ({ q, categoryId, page = 1, pageSize = 20 } = {}) =>
        affiliateAxios.get('/affiliate/discovery', {
            params: {
                ...(q && { q }),
                ...(categoryId && { categoryId }),
                page,
                pageSize
            }
        }).then(r => r.data),

    // ─── AF-22: PRODUCT INTELLIGENCE (DISCOVER & LANDING) ─────────────

    /**
     * AF-22: Discover products with enriched intelligence.
     * Returns full product details, inventory, variants, store context, and commission intelligence.
     *
     * @param {Object} params
     * @param {string} [params.search] - Search query
     * @param {number} [params.minCommissionRate] - Minimum commission rate filter
     * @param {boolean} [params.inStockOnly=false] - Filter to in-stock products only
     * @param {string} [params.storeTenantId] - Store tenant ID filter
     * @param {string} [params.categorySlug] - AF-32: Category slug filter
     * @param {string} [params.sort] - AF-33: Sort parameter (commission_desc|commission_asc|newest|price_desc|price_asc)
     * @param {number} [params.page=1] - Page number
     * @param {number} [params.pageSize=20] - Items per page
     * @returns {Promise<{items: Array, page: number, pageSize: number, totalCount: number, totalPages: number}>}
     */
    discoverProductsIntelligence: ({ search, minCommissionRate, inStockOnly = false, storeTenantId, categorySlug, sort, page = 1, pageSize = 20 } = {}) =>
        affiliateAxios.get('/affiliate/discover/products', {
            params: {
                ...(search && { search }),
                ...(minCommissionRate != null && { minCommissionRate }),
                inStockOnly,
                ...(storeTenantId && { storeTenantId }),
                ...(categorySlug && { categorySlug }),
                ...(sort && { sort }),
                page,
                pageSize
            }
        }).then(r => r.data),

    /**
     * AF-32: Get active main categories for filter bar.
     * Returns [{id, name, slug, productCount, isActive}]
     */
    getDiscoverCategories: (storeTenantId) =>
        affiliateAxios.get('/affiliate/discover/categories', { params: { ...(storeTenantId && { storeTenantId }) } }).then(r => r.data),

    /**
     * AF-22: Get full product intelligence details.
     * Returns complete product information including variants and stock.
     *
     * @param {string} productId - Product GUID
     * @returns {Promise<Object>}
     */
    getProductIntelligence: (productId) =>
        affiliateAxios.get(`/affiliate/discover/products/${productId}`).then(r => r.data),

    // ─── AF-8 + AF-24: Analytics ─────────────────────────────────────

    /**
     * AF-24: Get analytics summary with KPIs (clicks, orders, earnings, conversion rate).
     * @param {number} [range=30] - Number of days to aggregate
     */
    getAnalyticsSummary: (range = 30) =>
        affiliateAxios.get('/affiliate/analytics/summary', { ...affiliateAuthorityReadConfig, params: { range } }).then(r => r.data),

    /**
     * AF-24: Get daily trend data for charts.
     * @param {number} [range=30] - Number of days
     * @returns {Promise<Array<{date: string, clicks: number, orders: number, earnings: number}>>}
     */
    getAnalyticsTrend: (range = 30) =>
        affiliateAxios.get('/affiliate/analytics/trend', { params: { range } }).then(r => r.data),

    /**
     * AF-24: Get top earning products.
     * @param {number} [range=30] - Number of days
     * @param {number} [limit=10] - Max results
     */
    getAnalyticsTopProducts: (range = 30, limit = 10) =>
        affiliateAxios.get('/affiliate/analytics/top-products', { params: { range, limit } }).then(r => r.data),

    /**
     * AF-24: Get top earning stores.
     * @param {number} [range=30] - Number of days
     * @param {number} [limit=10] - Max results
     */
    getAnalyticsTopStores: (range = 30, limit = 10) =>
        affiliateAxios.get('/affiliate/analytics/top-stores', { params: { range, limit } }).then(r => r.data),

    /**
     * AF-8: Get paginated click history.
     * @param {number} [page=1]
     * @param {number} [pageSize=20]
     */
    getAnalyticsClicks: (page = 1, pageSize = 20) =>
        affiliateAxios.get('/affiliate/analytics/clicks', { params: { page, pageSize } }).then(r => r.data),

    // ─── AF-15: Promotion Links ──────────────────────────────────────

    /**
     * Create or retrieve a promotion link for a specific product.
     * @param {string} tenantId - Store ID
     * @param {string} productId - Product ID
     */
    createPromotionLink: (tenantId, productId) =>
        affiliateAxios.post('/affiliate/promotion-links', { tenantId, productId }, affiliateAuthorityReadConfig).then(r => r.data),

    listPromotionLinks: () =>
        affiliateAxios.get('/affiliate/promotion-links', affiliateAuthorityReadConfig).then(r => r.data),

    deactivatePromotionLink: (promotionLinkId) =>
        affiliateAxios.delete(`/affiliate/promotion-links/${promotionLinkId}`, affiliateAuthorityReadConfig).then(r => r.data),

    // ─── AF-38: PAYOUTS / WITHDRAWALS ──────────────────────────────────

    /**
     * AF-38: Get available balance and payout request history.
     * @param {number} [page=1]
     * @param {number} [pageSize=20]
     */
    getPayouts: (page = 1, pageSize = 20) =>
        affiliateAxios.get('/affiliate/payouts', { ...affiliateAuthorityReadConfig, params: { page, pageSize } }).then(r => r.data),

    /**
     * AF-38: Request a withdrawal.
     * @param {number} amount - Must be >= 100 and <= availableForWithdrawal
     */
    requestPayout: (amount) =>
        affiliateAxios.post('/affiliate/payouts/request', { amount }, affiliateAuthorityReadConfig).then(r => r.data),
}

export default affiliateApi
