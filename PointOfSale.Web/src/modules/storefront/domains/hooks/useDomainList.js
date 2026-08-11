/**
 * D04: Domain List Hook
 *
 * Loads a paginated, filtered, sorted list of domains via useDomainReadQuery.
 * Handles search, type filter, state filter, sort, pagination.
 *
 * GET /api/admin/storefront/domains
 * Query params: page, pageSize, search, type, state, sortBy, sortDir
 */
import { useState, useCallback } from 'react'
import { domainManagementService } from '../services/domainManagementService'
import { useDomainReadQuery } from './useDomainReadQuery'

const DEFAULT_PAGE = 1
const DEFAULT_PAGE_SIZE = 20

export function useDomainList(initialParams = {}) {
    const [params, setParams] = useState({
        page: initialParams.page ?? DEFAULT_PAGE,
        pageSize: initialParams.pageSize ?? DEFAULT_PAGE_SIZE,
        search: initialParams.search ?? '',
        sortBy: initialParams.sortBy ?? 'hostname',
        sortDir: initialParams.sortDir ?? 'asc',
        type: initialParams.type ?? '',
        state: initialParams.state ?? '',
    })

    const fetchFn = useCallback(() => {
        const queryParams = {
            page: params.page,
            pageSize: params.pageSize,
        }
        if (params.search) queryParams.search = params.search
        if (params.sortBy) queryParams.sortBy = params.sortBy
        if (params.sortDir) queryParams.sortDir = params.sortDir
        if (params.type) queryParams.type = params.type
        if (params.state) queryParams.state = params.state
        return domainManagementService.getDomainList(queryParams)
    }, [params])

    const isEmpty = (data) => !data || !data.items || data.items.length === 0

    const queryResult = useDomainReadQuery(fetchFn, isEmpty, [params])

    const updateParams = useCallback((newParams) => {
        setParams((prev) => ({ ...prev, ...newParams }))
    }, [])

    const setSearch = useCallback((search) => {
        setParams((prev) => ({ ...prev, search, page: DEFAULT_PAGE }))
    }, [])

    const setTypeFilter = useCallback((type) => {
        setParams((prev) => ({ ...prev, type, page: DEFAULT_PAGE }))
    }, [])

    const setStateFilter = useCallback((stateFilter) => {
        setParams((prev) => ({ ...prev, state: stateFilter, page: DEFAULT_PAGE }))
    }, [])

    const setSort = useCallback((sortBy, sortDir) => {
        setParams((prev) => ({ ...prev, sortBy, sortDir }))
    }, [])

    const goToPage = useCallback((page) => {
        setParams((prev) => ({ ...prev, page }))
    }, [])

    return {
        ...queryResult,
        params,
        setSearch,
        setTypeFilter,
        setStateFilter,
        setSort,
        goToPage,
        updateParams,
    }
}

export default useDomainList
