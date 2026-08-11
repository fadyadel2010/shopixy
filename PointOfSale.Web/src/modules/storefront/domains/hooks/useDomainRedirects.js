/**
 * D07: Redirects Hook
 *
 * Loads redirect rules for a domain via useDomainReadQuery.
 *
 * GET /api/admin/storefront/domains/{id}/redirects
 */
import { useCallback } from 'react'
import { domainManagementService } from '../services/domainManagementService'
import { useDomainReadQuery } from './useDomainReadQuery'

export function useDomainRedirects(domainId) {
    const fetchFn = useCallback(() => {
        if (!domainId) return null
        return domainManagementService.getDomainRedirects(domainId)
    }, [domainId])

    const isEmpty = (data) => !data || data.length === 0

    return useDomainReadQuery(fetchFn, isEmpty, [domainId])
}

export default useDomainRedirects
