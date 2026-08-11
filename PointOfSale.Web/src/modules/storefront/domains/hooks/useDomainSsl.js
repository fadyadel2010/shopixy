/**
 * D06: SSL Management Hook
 *
 * Loads SSL certificate details for a domain via useDomainReadQuery.
 *
 * GET /api/admin/storefront/domains/{id}/ssl
 */
import { useCallback } from 'react'
import { domainManagementService } from '../services/domainManagementService'
import { useDomainReadQuery } from './useDomainReadQuery'

export function useDomainSsl(domainId) {
    const fetchFn = useCallback(() => {
        if (!domainId) return null
        return domainManagementService.getDomainSsl(domainId)
    }, [domainId])

    return useDomainReadQuery(fetchFn, null, [domainId])
}

export default useDomainSsl
