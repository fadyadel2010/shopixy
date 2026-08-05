import api from '../../services/api'
import { superAdminApiClient } from '../../super-admin/core/superAdminApiClient'

const endpoints = {
    Tenant: { kind: 'tenant', path: '/admin/footer' },
    Global: { kind: 'super', path: '/storefront/footer' },
    Marketplace: { kind: 'super', path: '/marketplace/footer' },
}

function requireEndpoint(scope) {
    const endpoint = endpoints[scope]
    if (!endpoint) throw new Error('Footer authoring scope is invalid.')
    return endpoint
}

export async function loadFooterConfiguration(scope) {
    const endpoint = requireEndpoint(scope)
    if (endpoint.kind === 'tenant') {
        const response = await api.get(endpoint.path, {
            headers: { 'Cache-Control': 'no-store', Pragma: 'no-cache' },
        })
        return response.data
    }
    const response = await superAdminApiClient.get(endpoint.path)
    return response.data
}

export async function saveFooterConfiguration(scope, request) {
    const endpoint = requireEndpoint(scope)
    if (endpoint.kind === 'tenant') {
        const response = await api.put(endpoint.path, request)
        return response.data
    }
    const response = await superAdminApiClient.put(endpoint.path, request)
    return response.data
}
