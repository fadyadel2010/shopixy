/**
 * D05: DNS Verification Hook
 *
 * Loads DNS/HTTP verification details for a domain via useDomainReadQuery.
 *
 * GET /api/admin/storefront/domains/{id}/verification
 */
import { useCallback } from 'react'
import { domainManagementService } from '../services/domainManagementService'
import { useDomainReadQuery } from './useDomainReadQuery'

export function useDomainVerification(domainId) {
    const fetchFn = useCallback(() => {
        if (!domainId) return null
        return domainManagementService.getDomainVerification(domainId)
    }, [domainId])

    return useDomainReadQuery(fetchFn, null, [domainId])
}

export default useDomainVerification
