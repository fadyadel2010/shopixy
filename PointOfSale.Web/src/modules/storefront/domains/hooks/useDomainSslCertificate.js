/**
 * D06: Canonical SSL Certificate Hook
 *
 * Loads safe DOMAIN-5.8 certificate metadata for a domain.
 *
 * GET /api/admin/storefront/domains/{id}/ssl/certificate
 */
import { useCallback } from 'react'
import { domainManagementService } from '../services/domainManagementService'
import { useDomainReadQuery } from './useDomainReadQuery'

export function useDomainSslCertificate(domainId) {
    const fetchFn = useCallback(() => {
        if (!domainId) return null
        return domainManagementService.getDomainSslCertificate(domainId)
    }, [domainId])

    return useDomainReadQuery(fetchFn, null, [domainId])
}

export default useDomainSslCertificate
