/**
 * D06: Canonical SSL Operations Hook
 *
 * Loads paged DOMAIN-5.8 certificate operation history for a domain.
 *
 * GET /api/admin/storefront/domains/{id}/ssl/operations
 */
import { useCallback, useState } from 'react'
import { domainManagementService } from '../services/domainManagementService'
import { useDomainReadQuery } from './useDomainReadQuery'

const DEFAULT_PAGE = 1
const DEFAULT_PAGE_SIZE = 20

export function useDomainSslOperations(domainId, initialPage = DEFAULT_PAGE, initialPageSize = DEFAULT_PAGE_SIZE) {
    const [page, setPage] = useState(initialPage)
    const [pageSize] = useState(initialPageSize)

    const fetchFn = useCallback(() => {
        if (!domainId) return null
        return domainManagementService.getDomainSslOperations(domainId, { page, pageSize })
    }, [domainId, page, pageSize])

    const isEmpty = useCallback((result) => !result || !Array.isArray(result.items) || result.items.length === 0, [])
    const queryResult = useDomainReadQuery(fetchFn, isEmpty, [domainId, page, pageSize])

    return {
        ...queryResult,
        page,
        pageSize,
        setPage,
        nextPage: () => setPage((current) => current + 1),
        previousPage: () => setPage((current) => Math.max(1, current - 1)),
    }
}

export default useDomainSslOperations
