import api from '../../../services/api'

/**
 * API service for managing Storefront Authentication Providers.
 * 
 * ARCHITECTURAL RULES:
 * - POS is the ONLY admin for provider settings
 * - Secrets are encrypted at rest - never returned by API
 * - Changes apply instantly (no restart required)
 * 
 * Phase: P2 - Storefront Auth Provider Management
 */

const BASE_URL = '/admin/storefront/auth-providers'

/**
 * Gets all authentication provider settings for the tenant.
 * @returns {Promise<{providers: Array}>} List of provider settings
 */
export async function getAuthProviders() {
    const response = await api.get(BASE_URL)
    return response.data
}

/**
 * Gets settings for a specific authentication provider.
 * @param {string} provider - Provider name (Google or Facebook)
 * @returns {Promise<Object>} Provider settings
 */
export async function getAuthProvider(provider) {
    const response = await api.get(`${BASE_URL}/${provider}`)
    return response.data
}

/**
 * Updates settings for an authentication provider.
 * @param {string} provider - Provider name (Google or Facebook)
 * @param {Object} settings - Settings to update
 * @param {string} [settings.clientId] - OAuth Client ID
 * @param {string} [settings.clientSecret] - OAuth Client Secret (only if changing)
 * @param {string} [settings.additionalScopes] - Additional OAuth scopes
 * @returns {Promise<Object>} Update result with updated settings
 */
const revisionHeaders = revision => ({ headers: { 'If-Match': `"${revision}"` } })

export async function updateAuthProvider(provider, settings, revision) {
    const response = await api.put(`${BASE_URL}/${provider}`, settings, revisionHeaders(revision))
    return response.data
}

/**
 * Enables an authentication provider.
 * Requires ClientId and ClientSecret to be configured first.
 * @param {string} provider - Provider name (Google or Facebook)
 * @returns {Promise<Object>} Enable result
 */
export async function enableAuthProvider(provider, revision) {
    const response = await api.post(`${BASE_URL}/${provider}/enable`, {}, revisionHeaders(revision))
    return response.data
}

/**
 * Disables an authentication provider.
 * SECURITY: Disabling blocks login attempts IMMEDIATELY.
 * @param {string} provider - Provider name (Google or Facebook)
 * @param {string} [reason] - Optional reason for audit
 * @returns {Promise<Object>} Disable result
 */
export async function disableAuthProvider(provider, reason = null, revision) {
    const response = await api.post(`${BASE_URL}/${provider}/disable`, { reason }, revisionHeaders(revision))
    return response.data
}

/**
 * Rotates the client secret for an authentication provider.
 * SECURITY: Old secret becomes invalid immediately.
 * @param {string} provider - Provider name (Google or Facebook)
 * @param {string} clientSecret - New client secret
 * @returns {Promise<Object>} Rotation result
 */
export async function rotateSecret(provider, clientSecret, revision) {
    const response = await api.post(`${BASE_URL}/${provider}/rotate-secret`, { clientSecret }, revisionHeaders(revision))
    return response.data
}

export default {
    getAuthProviders,
    getAuthProvider,
    updateAuthProvider,
    enableAuthProvider,
    disableAuthProvider,
    rotateSecret
}
