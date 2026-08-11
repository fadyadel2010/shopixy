/**
 * D02: Domain Detail Hook
 *
 * Loads detailed information for a single domain by ID via useDomainReadQuery.
 *
 * GET /api/admin/storefront/domains/{id}
 */
import { useCallback } from 'react'
import { domainManagementService } from '../services/domainManagementService'
import { useDomainReadQuery } from './useDomainReadQuery'

export function useDomainDetail(domainId) {
    const fetchFn = useCallback(() => {
        if (!domainId) return null
        return domainManagementService.getDomainDetail(domainId)
    }, [domainId])

    return useDomainReadQuery(fetchFn, null, [domainId])
}

export default useDomainDetail
