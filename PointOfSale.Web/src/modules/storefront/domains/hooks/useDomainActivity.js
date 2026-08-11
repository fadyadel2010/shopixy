/**
 * D09: Domain Activity Hook
 *
 * Loads activity dashboard summary via useDomainReadQuery.
 *
 * GET /api/admin/storefront/domains/activity
 */
import { useCallback } from 'react'
import { domainManagementService } from '../services/domainManagementService'
import { useDomainReadQuery } from './useDomainReadQuery'

const DEFAULT_LIMIT = 20

export function useDomainActivity(limit = DEFAULT_LIMIT) {
    const fetchFn = useCallback(() => domainManagementService.getActivitySummary({ limit }), [limit])

    const isEmpty = (data) => !data || !data.recentActivity || data.recentActivity.length === 0

    return useDomainReadQuery(fetchFn, isEmpty, [limit])
}

export default useDomainActivity
