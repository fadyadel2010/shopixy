import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'

const mockGetDashboardSummary = vi.hoisted(() => vi.fn())

vi.mock('../services/domainManagementService', () => ({
    domainManagementService: {
        getDashboardSummary: mockGetDashboardSummary,
    },
}))

vi.mock('../../../../services/api', () => ({
    getSafeApiErrorMessage: (err: { response?: { data?: { message?: string } } }) => err?.response?.data?.message || 'Request failed',
}))

import { useDomainDashboard } from './useDomainDashboard.js'
import { LOAD_STATES } from './useDomainLoadState.js'

describe('useDomainDashboard', () => {
    beforeEach(() => {
        mockGetDashboardSummary.mockReset()
    })

    afterEach(() => {
        vi.restoreAllMocks()
    })

    it('returns loading state initially', () => {
        mockGetDashboardSummary.mockReturnValue(new Promise(() => {}))

        const { result } = renderHook(() => useDomainDashboard())

        expect(result.current.state).toBe(LOAD_STATES.LOADING)
        expect(result.current.isLoading).toBe(true)
    })

    it('returns success state with data on successful fetch', async () => {
        const mockData = {
            totalDomains: 3,
            sslExpiringSoon: 1,
            pendingVerification: 1,
            draftCount: 0,
            primaryDomain: { id: '1', hostName: 'shop.example.com' },
            recentDomains: [{ id: '1', hostName: 'shop.example.com' }],
        }

        mockGetDashboardSummary.mockResolvedValue(mockData)

        const { result } = renderHook(() => useDomainDashboard())

        await waitFor(() => {
            expect(result.current.isSuccess).toBe(true)
        })

        expect(result.current.data).toEqual(mockData)
    })

    it('returns empty state when totalDomains is 0', async () => {
        mockGetDashboardSummary.mockResolvedValue({
            totalDomains: 0,
            sslExpiringSoon: 0,
            pendingVerification: 0,
            draftCount: 0,
            primaryDomain: null,
            recentDomains: [],
        })

        const { result } = renderHook(() => useDomainDashboard())

        await waitFor(() => {
        expect(result.current.isEmpty).toBe(true)
    })
    })

    it('returns error state on API failure', async () => {
        mockGetDashboardSummary.mockRejectedValue({
            response: { status: 500, data: { message: 'Server error' } },
        })

        const { result } = renderHook(() => useDomainDashboard())

        await waitFor(() => {
            expect(result.current.isError).toBe(true)
        })

        expect(result.current.error).toBe('Server error')
    })

    it('returns unauthorized state on 401/403', async () => {
        mockGetDashboardSummary.mockRejectedValue({
            response: { status: 401, data: { message: 'Unauthorized' } },
        })

        const { result } = renderHook(() => useDomainDashboard())

        await waitFor(() => {
            expect(result.current.isUnauthorized).toBe(true)
        })
    })

    it('returns not-found state on 404', async () => {
        mockGetDashboardSummary.mockRejectedValue({
            response: { status: 404, data: { message: 'Not found' } },
        })

        const { result } = renderHook(() => useDomainDashboard())

        await waitFor(() => {
            expect(result.current.isNotFound).toBe(true)
        })
    })

    it('provides a refetch function', async () => {
        mockGetDashboardSummary.mockResolvedValue({
            totalDomains: 1,
            sslExpiringSoon: 0,
            pendingVerification: 0,
            draftCount: 0,
            primaryDomain: null,
            recentDomains: [],
        })

        const { result } = renderHook(() => useDomainDashboard())

        await waitFor(() => {
            expect(result.current.refetch).toBeDefined()
        })

        result.current.refetch()

        await waitFor(() => {
            expect(mockGetDashboardSummary).toHaveBeenCalledTimes(2)
        })
    })
})
