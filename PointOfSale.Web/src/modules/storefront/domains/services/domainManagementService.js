/**
 * Domain Management Service
 * DOMAIN-3.1-B: Storefront Domain Management — READ-ONLY API client.
 * DOMAIN-3.1-C: Storefront Domain Management — WRITE PATH (mutations).
 *
 * Interacts with AdminDomainsController endpoints.
 *
 * D03 (AddDomainWizardPage) uses the create and verification commands below.
 */

import api from '../../../../services/api'

const withNoStoreHeaders = (options = {}) => ({
    ...options,
    headers: {
        ...(options.headers || {}),
        'Cache-Control': 'no-store',
        Pragma: 'no-cache',
    },
})

export const domainManagementService = {
    // =========================================================================
    // READ endpoints (DOMAIN-3.1-B)
    // =========================================================================

    /**
     * D01: Dashboard summary — primary domain, counts, recent domains.
     * GET /api/admin/storefront/domains/dashboard
     */
    async getDashboardSummary(options = {}) {
        const response = await api.get(
            '/admin/storefront/domains/dashboard',
            withNoStoreHeaders(options),
        )
        return response.data
    },

    /**
     * D04: Paginated, filtered, sorted domain list.
     * GET /api/admin/storefront/domains
     */
    async getDomainList(params = {}, options = {}) {
        const response = await api.get(
            '/admin/storefront/domains',
            withNoStoreHeaders({ params, ...options }),
        )
        return response.data
    },

    /**
     * D02: Domain detail — single domain with all related info.
     * GET /api/admin/storefront/domains/{id}
     */
    async getDomainDetail(domainId, options = {}) {
        const response = await api.get(
            `/admin/storefront/domains/${domainId}`,
            withNoStoreHeaders(options),
        )
        return response.data
    },

    /**
     * D05: DNS verification details for a domain.
     * GET /api/admin/storefront/domains/{id}/verification
     */
    async getDomainVerification(domainId, options = {}) {
        const response = await api.get(
            `/admin/storefront/domains/${domainId}/verification`,
            withNoStoreHeaders(options),
        )
        return response.data
    },

    /**
     * D06: SSL certificate details for a domain.
     * GET /api/admin/storefront/domains/{id}/ssl
     */
    async getDomainSsl(domainId, options = {}) {
        const response = await api.get(
            `/admin/storefront/domains/${domainId}/ssl`,
            withNoStoreHeaders(options),
        )
        return response.data
    },

    async getDomainSslCertificate(domainId, options = {}) {
        const response = await api.get(
            `/admin/storefront/domains/${domainId}/ssl/certificate`,
            withNoStoreHeaders(options),
        )
        return response.data
    },

    async getDomainSslOperations(domainId, params = {}, options = {}) {
        const response = await api.get(
            `/admin/storefront/domains/${domainId}/ssl/operations`,
            withNoStoreHeaders({ params, ...options }),
        )
        return response.data
    },

    async getDomainSslOperation(domainId, operationId, options = {}) {
        const response = await api.get(
            `/admin/storefront/domains/${domainId}/ssl/operations/${operationId}`,
            withNoStoreHeaders(options),
        )
        return response.data
    },

    /**
     * D07: Redirect rules sourced from a domain.
     * GET /api/admin/storefront/domains/{id}/redirects
     */
    async getDomainRedirects(domainId, options = {}) {
        const response = await api.get(
            `/admin/storefront/domains/${domainId}/redirects`,
            withNoStoreHeaders(options),
        )
        return response.data
    },

    /**
     * D08: Paginated audit history for a domain.
     * GET /api/admin/storefront/domains/{id}/history
     */
    async getDomainHistory(domainId, params = {}, options = {}) {
        const response = await api.get(
            `/admin/storefront/domains/${domainId}/history`,
            withNoStoreHeaders({ params, ...options }),
        )
        return response.data
    },

    /**
     * D09: Activity dashboard — recent background job activity.
     * GET /api/admin/storefront/domains/activity
     */
    async getActivitySummary(params = {}, options = {}) {
        const response = await api.get(
            '/admin/storefront/domains/activity',
            withNoStoreHeaders({ params, ...options }),
        )
        return response.data
    },

    // =========================================================================
    // WRITE endpoints (DOMAIN-3.1-C)
    // Architecture Law: UI ↓ Typed API Client ↓ Controller
    // This is the typed API client layer.
    // =========================================================================

    /**
     * CreateDomain command.
     * POST /api/admin/storefront/domains
     *
     * @param {Object} request - { hostName, domainType, canonicalPolicy, redirectType, redirectTargetDomainId }
     * @returns {Promise<Object>} DomainMutationResponse
     */
    async createDomain(request, options = {}) {
        const response = await api.post(
            '/admin/storefront/domains',
            request,
            { ...options },
        )
        return response.data
    },

    /**
     * UpdateDomain command (partial update).
     * PATCH /api/admin/storefront/domains/{id}
     *
     * @param {Guid} domainId
     * @param {Object} request - { hostName?, canonicalPolicy?, redirectType?, redirectTargetDomainId?, expectedVersion? }
     * @returns {Promise<Object>} DomainMutationResponse
     */
    async updateDomain(domainId, request, options = {}) {
        const response = await api.patch(
            `/admin/storefront/domains/${domainId}`,
            request,
            { ...options },
        )
        return response.data
    },

    /**
     * StartDomainVerification command.
     * PUT /api/admin/storefront/domains/{id}/verification/start
     *
     * @param {Guid} domainId
     * @param {Object} request - { verificationMethod?, expectedVersion? }
     * @returns {Promise<Object>} DomainMutationResponse
     */
    async startDomainVerification(domainId, request = {}, options = {}) {
        const response = await api.put(
            `/admin/storefront/domains/${domainId}/verification/start`,
            request,
            { ...options },
        )
        return response.data
    },

    /**
     * VerifyNow command.
     * PUT /api/admin/storefront/domains/{id}/verification/verify
     *
     * @param {Guid} domainId
     * @param {Object} request - { expectedVersion? }
     * @returns {Promise<Object>} DomainVerificationCommandResponse
     */
    async verifyDomainNow(domainId, request = {}, options = {}) {
        const response = await api.put(
            `/admin/storefront/domains/${domainId}/verification/verify`,
            request,
            { ...options },
        )
        return response.data
    },

    /**
     * RetryVerification command.
     * PUT /api/admin/storefront/domains/{id}/verification/retry
     *
     * @param {Guid} domainId
     * @param {Object} request - { expectedVersion? }
     * @returns {Promise<Object>} DomainVerificationCommandResponse
     */
    async retryDomainVerification(domainId, request = {}, options = {}) {
        const response = await api.put(
            `/admin/storefront/domains/${domainId}/verification/retry`,
            request,
            { ...options },
        )
        return response.data
    },

    /**
     * RefreshVerificationToken command.
     * PUT /api/admin/storefront/domains/{id}/verification/token
     *
     * @param {Guid} domainId
     * @param {Object} request - { verificationMethod?, expectedVersion? }
     * @returns {Promise<Object>} DomainVerificationCommandResponse
     */
    async refreshVerificationToken(domainId, request = {}, options = {}) {
        const response = await api.put(
            `/admin/storefront/domains/${domainId}/verification/token`,
            request,
            { ...options },
        )
        return response.data
    },

    async provisionSslCertificate(domainId, request = {}, options = {}) {
        const response = await api.post(
            `/admin/storefront/domains/${domainId}/ssl/provision`,
            request,
            { ...options },
        )
        return response.data
    },

    async activateSslCertificate(domainId, request = {}, options = {}) {
        const response = await api.post(
            `/admin/storefront/domains/${domainId}/ssl/activate`,
            request,
            { ...options },
        )
        return response.data
    },

    async renewSslCertificate(domainId, request = {}, options = {}) {
        const response = await api.post(
            `/admin/storefront/domains/${domainId}/ssl/renew`,
            request,
            { ...options },
        )
        return response.data
    },

    async revokeSslCertificate(domainId, request = {}, options = {}) {
        const response = await api.post(
            `/admin/storefront/domains/${domainId}/ssl/revoke`,
            request,
            { ...options },
        )
        return response.data
    },

    async retrySslOperation(domainId, request = {}, options = {}) {
        const response = await api.post(
            `/admin/storefront/domains/${domainId}/ssl/retry`,
            request,
            { ...options },
        )
        return response.data
    },

    async refreshSslCertificate(domainId, request = {}, options = {}) {
        const response = await api.post(
            `/admin/storefront/domains/${domainId}/ssl/refresh`,
            request,
            { ...options },
        )
        return response.data
    },

    /**
     * PromotePrimaryDomain command.
     * PUT /api/admin/storefront/domains/{id}/primary
     * Transactional: atomically promotes target, demotes current primary.
     *
     * @param {Guid} domainId
     * @param {Object} request - { expectedVersion?, previousPrimaryExpectedVersion? }
     * @returns {Promise<Object>} DomainMutationResponse
     */
    async promotePrimaryDomain(domainId, request = {}, options = {}) {
        const response = await api.put(
            `/admin/storefront/domains/${domainId}/primary`,
            request,
            { ...options },
        )
        return response.data
    },

    /**
     * ArchiveDomain command.
     * PUT /api/admin/storefront/domains/{id}/archive
     *
     * @param {Guid} domainId
     * @param {Object} request - { reason?, expectedVersion }
     * @returns {Promise<Object>} DomainMutationResponse
     */
    async archiveDomain(domainId, request = {}, options = {}) {
        const response = await api.put(
            `/admin/storefront/domains/${domainId}/archive`,
            request,
            { ...options },
        )
        return response.data
    },

    /**
     * RestoreDomain command.
     * PUT /api/admin/storefront/domains/{id}/restore
     *
     * @param {Guid} domainId
     * @param {Object} request - { reason?, expectedVersion }
     * @returns {Promise<Object>} DomainMutationResponse
     */
    async restoreDomain(domainId, request = {}, options = {}) {
        const response = await api.put(
            `/admin/storefront/domains/${domainId}/restore`,
            request,
            { ...options },
        )
        return response.data
    },

    /**
     * DeleteDomain command (soft-delete ONLY).
     * DELETE /api/admin/storefront/domains/{id}
     *
     * @param {Guid} domainId
     * @param {Object} request - { reason?, expectedVersion }
     * @returns {Promise<Object>} DomainMutationResponse
     */
    async deleteDomain(domainId, request = {}, options = {}) {
        const response = await api.delete(
            `/admin/storefront/domains/${domainId}`,
            { ...withNoStoreHeaders(options), data: request },
        )
        return response.data
    },
}

export default domainManagementService
