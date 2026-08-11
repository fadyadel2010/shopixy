/**
 * D08: Domain History Hook
 *
 * Loads paginated audit history for a single domain via useDomainReadQuery.
 *
 * GET /api/admin/storefront/domains/{id}/history
 */
import { useState, useCallback } from 'react'
import { domainManagementService } from '../services/domainManagementService'
import { useDomainReadQuery } from './useDomainReadQuery'

const DEFAULT_PAGE = 1
const DEFAULT_PAGE_SIZE = 50

export function useDomainHistory(domainId, initialPage = DEFAULT_PAGE, initialPageSize = DEFAULT_PAGE_SIZE) {
    const [page, setPage] = useState(initialPage)
    const [pageSize] = useState(initialPageSize)

    const fetchFn = useCallback(() => {
        if (!domainId) return null
        return domainManagementService.getDomainHistory(domainId, { page, pageSize })
    }, [domainId, page, pageSize])

    const isEmpty = (data) => !data || !data.items || data.items.length === 0

    const queryResult = useDomainReadQuery(fetchFn, isEmpty, [page, pageSize, domainId])

    const goToPage = useCallback((newPage) => {
        setPage(newPage)
    }, [])

    return {
        ...queryResult,
        page,
        pageSize,
        goToPage,
    }
}

export default useDomainHistory
