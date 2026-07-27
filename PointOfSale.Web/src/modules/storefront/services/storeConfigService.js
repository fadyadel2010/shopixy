/**
 * Store Config Service
 * Phase 9.8: POS Integration - Unified Admin Experience
 * 
 * Uses the existing POS api.js client for auth and tenant context
 */

import api from '../../../services/api'

const withNoStoreHeaders = (options = {}) => ({
    ...options,
    headers: {
        ...(options.headers || {}),
        'Cache-Control': 'no-store',
        Pragma: 'no-cache',
    },
})

/**
 * Store Config API Service
 * Interacts with AdminStoreConfigController endpoints
 * 
 * CRITICAL: updatePageLayout saves DRAFT ONLY
 * CRITICAL: publishPage promotes draft to live
 */
export const storeConfigService = {
    /**
     * Get store configuration
     */
    async getStoreConfig(options = {}) {
        const response = await api.get('/admin/store-config', withNoStoreHeaders(options))
        return response.data
    },

    /**
     * Get storefront runtime configuration
     * Used by the builder preview to hydrate runtime-only fields like currency.
     */
    async getRuntimeStoreConfig(options = {}) {
        const response = await api.get('/storefront/store-config', withNoStoreHeaders(options))
        return response.data
    },

    /**
     * Update store configuration
     */
    async updateStoreConfig(config, options = {}) {
        const rowVersion = typeof config?.rowVersion === 'string' ? config.rowVersion.trim() : ''
        const response = await api.put('/admin/store-config', config, {
            ...options,
            headers: {
                ...(options.headers || {}),
                ...(rowVersion ? { 'If-Match': `"${rowVersion}"` } : {}),
            },
        })
        return response.data
    },

    /**
     * Update storefront operational status without replacing unrelated config domains.
     */
    async updateStorefrontStatus(statusConfig, options = {}) {
        const rowVersion = typeof statusConfig?.rowVersion === 'string' ? statusConfig.rowVersion.trim() : ''
        const response = await api.put('/admin/store-config/status', statusConfig, {
            ...options,
            headers: {
                ...(options.headers || {}),
                ...(rowVersion ? { 'If-Match': `"${rowVersion}"` } : {}),
            },
        })
        return response.data
    },

    /**
     * Update page layout (DRAFT ONLY)
     * CRITICAL: This does NOT affect live storefront
     */
    async updatePageLayout(pageId, pageConfig, options = {}) {
        const response = await api.put(`/admin/store-config/pages/${pageId}`, pageConfig, options)
        return response.data
    },

    /**
     * Publish page to live
     * CRITICAL: This affects the live storefront
     */
    async publishPage(pageId, options = {}) {
        const response = await api.post(`/admin/store-config/pages/${pageId}/publish`, undefined, options)
        return response.data
    },

    /**
     * Delete a stored product template.
     */
    async deleteProductTemplate(templateId, options = {}) {
        const response = await api.delete(`/admin/store-config/product-templates/${templateId}`, options)
        return response.data
    },

    /**
     * Get theme presets
     */
    async getThemePresets() {
        const response = await api.get('/admin/store-config/themes/presets')
        return response.data
    },

    /**
     * Select theme preset
     */
    async selectThemePreset(presetKey, options = {}) {
        const response = await api.put(`/admin/store-config/theme/preset/${presetKey}`, undefined, options)
        return response.data
    },

    /**
     * Update theme overrides
     */
    async updateThemeOverrides(overrides, options = {}) {
        const response = await api.put('/admin/store-config/theme/overrides', overrides, options)
        return response.data
    },

    /**
     * Activate storefront runtime theme without replacing unrelated store configuration.
     */
    async activateTheme(themeId, options = {}) {
        const response = await api.post('/admin/store-config/theme/activate', { themeId }, options)
        return response.data
    },

    /**
     * Publish theme
     */
    async publishTheme(options = {}) {
        const response = await api.post('/admin/store-config/theme/publish', undefined, options)
        return response.data
    },

    async createThemePreviewLink(themeId) {
        const response = await api.post(`/admin/store-config/theme/preview-link/${encodeURIComponent(themeId)}`)
        return response.data
    },

    /**
     * Get preview configuration (DRAFT content)
     * CRITICAL: Never touches MongoDB/Redis
     */
    async getPreviewConfig(options = {}) {
        const response = await api.get('/storefront/config/preview', withNoStoreHeaders({
            params: { preview: true },
            ...options,
        }))
        return response.data
    },

    /**
     * Get real version history from the backend.
     * @returns {Promise<{versions: import('../types/builder').VersionHistoryEntry[]}>}
     */
    async getVersionHistory(options = {}) {
        const response = await api.get('/admin/store-config/versions', withNoStoreHeaders(options))
        return response.data
    },

    /**
     * Rollback to a previous version
     */
    async rollback(version, request, options = {}) {
        const response = await api.post(`/admin/store-config/rollback/${version}`, request, options)
        return response.data
    },
}

export default storeConfigService
