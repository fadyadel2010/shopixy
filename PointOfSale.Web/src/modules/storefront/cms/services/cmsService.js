import api from '../../../../services/api'
import {
    admitNavigationSnapshot,
    admitNavigationTargets,
    mapNavigationDraftToRequest,
} from './navigationAuthoringContract'

const noStoreOptions = {
    headers: {
        'Cache-Control': 'no-store',
        Pragma: 'no-cache',
    },
}

export function extractCmsConflict(error) {
    const response = error?.response
    if (!response || response.status !== 409 || response.data?.code !== 'CONCURRENCY_CONFLICT') {
        return null
    }

    const latestVersion = response.data?.latestVersion ?? response.data?.LatestVersion ?? null

    return {
        code: response.data?.code || 'CONCURRENCY_CONFLICT',
        message: response.data?.message || 'This content was updated by another admin. Reload the latest version before saving again.',
        latestVersion,
    }
}

function admitCmsPreviewAccess(response) {
    const slug = typeof response?.slug === 'string' ? response.slug.trim() : ''
    const previewToken = typeof response?.previewToken === 'string' ? response.previewToken.trim() : ''
    const expiresAtUtc = typeof response?.expiresAtUtc === 'string' ? response.expiresAtUtc : ''
    const expiresAtMs = expiresAtUtc ? Date.parse(expiresAtUtc) : Number.NaN

    if (!slug || !previewToken || !Number.isFinite(expiresAtMs) || expiresAtMs <= Date.now()) {
        throw new Error('CMS preview access was rejected by backend preview admission.')
    }

    return {
        slug,
        previewToken,
        expiresAtUtc,
    }
}

export const cmsService = {
    async listPages() {
        const response = await api.get('/admin/pages', noStoreOptions)
        return response.data
    },

    async getPage(pageId) {
        const response = await api.get(`/admin/pages/${pageId}`, noStoreOptions)
        return response.data
    },

    async getVersionHistory(pageId) {
        const response = await api.get(`/admin/pages/${pageId}/versions`, noStoreOptions)
        return response.data
    },

    async getPageBySlug(slug) {
        const response = await api.get(`/admin/cms/pages/by-slug/${slug}`, noStoreOptions)
        return response.data
    },

    async createPage(pageData) {
        const response = await api.post('/admin/pages', pageData)
        return response.data
    },

    async updatePage(pageId, pageData) {
        const response = await api.put(`/admin/pages/${pageId}`, pageData)
        return response.data
    },

    async publishPage(pageId, expectedVersion) {
        const response = await api.post(`/admin/pages/${pageId}/publish`, undefined, {
            headers: { 'If-Match': `"${expectedVersion}"` },
        })
        return response.data
    },

    async rollbackPage(pageId, version, expectedVersion, reason = '') {
        const response = await api.post(`/admin/pages/${pageId}/rollback`, {
            version,
            reason,
        }, {
            headers: { 'If-Match': `"${expectedVersion}"` },
        })
        return response.data
    },

    async previewPage(pageId) {
        const response = await api.get(`/admin/pages/${pageId}/preview`, noStoreOptions)
        return response.data
    },

    async createPreviewLink(pageId) {
        const response = await api.post(`/admin/pages/${pageId}/preview-link`, undefined, noStoreOptions)
        return admitCmsPreviewAccess(response.data)
    },

    async deletePage(pageId, expectedVersion) {
        await api.delete(`/admin/pages/${pageId}`, {
            headers: { 'If-Match': `"${expectedVersion}"` },
        })
    },

    async seedDefaultPages() {
        const response = await api.post('/admin/cms/seed')
        return response.data
    },

    async getNavigation(options = {}) {
        const response = await api.get('/admin/cms/navigation', options)
        return admitNavigationSnapshot(response.data)
    },

    async getNavigationTargets(options = {}) {
        const response = await api.get('/admin/cms/navigation/targets', options)
        return admitNavigationTargets(response.data)
    },

    async updateNavigation(navigationSnapshot, options = {}) {
        const navigationData = mapNavigationDraftToRequest(navigationSnapshot)
        const response = await api.put('/admin/cms/navigation', navigationData, {
            ...options,
            headers: {
                ...options.headers,
                'If-Match': `"${navigationData.rowVersion}"`,
            },
        })
        return admitNavigationSnapshot(response.data)
    },

    async publishNavigation(rowVersion, options = {}) {
        const response = await api.post('/admin/cms/navigation/publish', undefined, {
            ...options,
            headers: {
                ...options.headers,
                'If-Match': `"${rowVersion}"`,
            },
        })
        return admitNavigationSnapshot(response.data)
    },

}

export default cmsService
