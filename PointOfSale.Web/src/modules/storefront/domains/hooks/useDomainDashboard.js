/**
 * D01: Domain Dashboard Hook
 *
 * Loads the domain management dashboard summary via useDomainReadQuery.
 *
 * GET /api/admin/storefront/domains/dashboard
 */
import { useCallback } from 'react'
import { domainManagementService } from '../services/domainManagementService'
import { useDomainReadQuery } from './useDomainReadQuery'

export function useDomainDashboard() {
    const fetchFn = useCallback(() => domainManagementService.getDashboardSummary(), [])

    const isEmpty = (data) => !data || data.totalDomains === 0

    return useDomainReadQuery(fetchFn, isEmpty, [])
}

export default useDomainDashboard
