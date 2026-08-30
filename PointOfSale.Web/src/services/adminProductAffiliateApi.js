/**
 * AF-4C + AF-30A: Admin Product Affiliate API Service
 *
 * Admin-only endpoints for managing per-product affiliate settings.
 * Role-gated: Owner, Admin only.
 *
 * SECURITY:
 * - CommissionRate is only visible to Owner/Admin
 * - Public APIs never expose commission rate
 *
 * AF-30A CHANGES:
 * - enableWithCategory() is the PRIMARY method for enabling affiliate (requires CategoryId)
 * - enable() is DEPRECATED (kept for backward compatibility)
 * - updateCategory() added for category reassignment
 * - getCategoryTree() added for category dropdown population
 */
import api from './api'
import { AFFILIATE_ADMIN_READ_HEADERS } from './adminAffiliateApi'

const affiliateConfigurationReadOptions = (options = {}) => ({
    ...options,
    headers: {
        ...AFFILIATE_ADMIN_READ_HEADERS,
        ...(options.headers || {}),
    },
})

function readPaginationHeaders(headers) {
    const parseHeaderCount = (name, fallback) => {
        const raw = headers?.[name]
        const normalized = Array.isArray(raw) ? raw[0] : raw
        const parsed = Number.parseInt(normalized, 10)
        return Number.isFinite(parsed) && parsed >= 0 ? parsed : fallback
    }

    return {
        totalCount: parseHeaderCount('x-total-count', 0),
        page: parseHeaderCount('x-page', 1),
        pageSize: parseHeaderCount('x-page-size', 20),
        totalPages: parseHeaderCount('x-total-pages', 0),
    }
}

const adminProductAffiliateApi = {
    // ─── READ ───────────────────────────────────

    /**
     * Shopixy Affiliate Refactor A/C: bounded affiliate products list.
     * Server-owned search + filters + pagination; tenant is never supplied by the browser.
     *
     * @param {number} page
     * @param {number} pageSize
     * @param {{search?: string, affiliateStatus?: 'all'|'enabled'|'disabled', productStatus?: 'all'|'active'|'inactive', categoryId?: string|null}} [params]
     * @returns {Promise<{items: Array<object>, totalCount: number, page: number, pageSize: number, totalPages: number}>}
     */
    listProducts: async (page = 1, pageSize = 20, params = {}) => {
        const {
            search = '',
            affiliateStatus = 'all',
            productStatus = 'all',
            categoryId = null,
        } = params || {}

        const response = await api.get('/admin/affiliate/products', affiliateConfigurationReadOptions({
            params: {
                page,
                pageSize,
                ...(search && { search }),
                ...(affiliateStatus && affiliateStatus !== 'all' && { affiliateStatus }),
                ...(productStatus && productStatus !== 'all' && { productStatus }),
                ...(categoryId && { categoryId }),
            },
        }))

        const pagination = readPaginationHeaders(response?.headers)

        return {
            items: Array.isArray(response?.data) ? response.data : [],
            totalCount: pagination.totalCount,
            page: pagination.page,
            pageSize: pagination.pageSize,
            totalPages: pagination.totalPages,
        }
    },

    /**
     * Get affiliate settings for a product
     * @param {string} productId
     * @returns {Promise<{productId: string, isEnabled: boolean, isPolicyEnabled: boolean, commissionRate: number|null, maxCommissionPerItem: number|null, templateType: string, affiliateCategoryId: string|null}>}
     */
    getSettings: (productId) =>
        api.get(`/admin/products/${productId}/affiliate`, affiliateConfigurationReadOptions()).then(r => r.data),

    /**
     * AF-30A GAP-3 FIX: Get category tree for dropdown population.
     * Returns hierarchical tree: Main (Level 1) → Subcategory (Level 2).
     * Includes inactive categories (admin needs to see them for management).
     * 
     * @returns {Promise<Array<{id: string, name: string, slug: string, level: number, isActive: boolean, displayOrder: number, subcategories: Array}>>}
     */
    getCategoryTree: () =>
        api.get('/admin/affiliate/categories/tree', affiliateConfigurationReadOptions()).then(r => r.data),

    // ─── WRITE ──────────────────────────────────

    /**
     * AF-30A GAP-1 FIX: Atomically enable affiliate with category assignment.
     * This is the PRIMARY method for enabling affiliate marketing.
     * 
     * @param {string} productId
     * @param {number} commissionRate - Decimal fraction, e.g. 0.10 = 10%
     * @param {string} categoryId - GUID of the marketplace category (REQUIRED)
     * @param {number|null} maxCommissionPerItem - Optional cap
     * @returns {Promise<{isEnabled: boolean, isPolicyEnabled: boolean, commissionRate: number, maxCommissionPerItem: number|null, templateType: string, affiliateCategoryId: string}>}
     */
    enableWithCategory: (productId, commissionRate, categoryId, maxCommissionPerItem = null) =>
        api.post(`/admin/products/${productId}/affiliate/enable-with-category`, {
            commissionRate,
            categoryId,
            maxCommissionPerItem
        }).then(r => r.data),

    /**
     * @deprecated Use enableWithCategory() instead. This endpoint requires 
     * a category to be already assigned and will fail on new products.
     */
    enable: (productId, commissionRate, maxCommissionPerItem = null) =>
        api.post(`/admin/products/${productId}/affiliate/enable`, {
            commissionRate,
            maxCommissionPerItem
        }).then(r => r.data),

    /**
     * Disable affiliate for a product (soft disable).
     * Does NOT remove category assignment — re-enabling will restore it.
     * @param {string} productId
     * @returns {Promise<{isEnabled: boolean, isPolicyEnabled: boolean, commissionRate: number|null, maxCommissionPerItem: number|null, templateType: string, affiliateCategoryId: string|null}>}
     */
    disable: (productId) =>
        api.post(`/admin/products/${productId}/affiliate/disable`).then(r => r.data),

    /**
     * AF-30A GAP-5 FIX: Update the marketplace category assigned to this product.
     * Cannot set null while affiliate is enabled.
     *
     * @param {string} productId
     * @param {string|null} categoryId - GUID or null (only null when disabled)
     * @returns {Promise<{isEnabled: boolean, isPolicyEnabled: boolean, commissionRate: number, maxCommissionPerItem: number|null, templateType: string, affiliateCategoryId: string|null}>}
     */
    updateCategory: (productId, categoryId) =>
        api.post(`/admin/products/${productId}/affiliate/category`, { categoryId }).then(r => r.data),

    /**
     * Update the given affiliate landing page template for a product
     * @param {string} productId
     * @param {string} templateType
     */
    updateTemplate: (productId, templateType) =>
        api.post(`/admin/products/${productId}/affiliate/template`, { templateType }).then(r => r.data)
}

export default adminProductAffiliateApi
