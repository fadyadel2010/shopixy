import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from '../../../../i18n/config'
import DomainListPage from './DomainListPage'
import DomainsOverviewPage from './DomainsOverviewPage'

const mocks = vi.hoisted(() => ({ sort: vi.fn(), navigate: vi.fn(), archive: vi.fn(), refetch: vi.fn() }))
vi.mock('../../authoringPlatform', async importOriginal => ({
    ...await importOriginal<Record<string, unknown>>(), useAuthoringGuard: () => ({ requestNavigation: mocks.navigate }),
}))
vi.mock('../hooks', () => ({
    useDomainList: () => ({ data: { items: [{ id: 'owned', hostName: 'shop.example.test', domainType: 'Alias', lifecycleState: 'Verified', sslStatus: 'Pending', version: 1 }], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 }, params: { sortBy: 'hostname', sortDir: 'asc' }, isSuccess: true, setSort: mocks.sort, setSearch: vi.fn(), goToPage: vi.fn(), refetch: mocks.refetch }),
    useDomainMutations: () => ({ archiveDomain: mocks.archive, isMutating: false }),
    useDomainDashboard: () => ({ data: { totalDomains: 1, draftCount: 1, pendingVerification: 0, sslExpiringSoon: 0, recentDomains: [{ id: 'owned', hostName: 'shop.example.test', lifecycleState: 'Draft' }] } }),
}))

describe('Domain portfolio contracts', () => {
    beforeEach(async () => { vi.resetAllMocks(); await i18n.changeLanguage('en') })
    afterEach(cleanup)
    it('sends the backend sort names, not the DTO property names', () => {
        render(<DomainListPage />)
        fireEvent.click(screen.getByRole('button', { name: 'Sort by Type' }))
        expect(mocks.sort).toHaveBeenCalledWith('type', 'asc')
        fireEvent.click(screen.getByRole('button', { name: 'Sort by Status' }))
        expect(mocks.sort).toHaveBeenCalledWith('status', 'asc')
        fireEvent.click(screen.getByRole('button', { name: 'Sort by Created' }))
        expect(mocks.sort).toHaveBeenCalledWith('created', 'asc')
        expect(screen.queryByRole('button', { name: 'Sort by SSL' })).not.toBeInTheDocument()
    })
    it('opens a listed domain and does not offer promotion before SSL activation', () => {
        render(<DomainListPage />)
        fireEvent.click(screen.getByRole('button', { name: 'shop.example.test' }))
        expect(mocks.navigate).toHaveBeenCalledWith('/storefront/domains/owned')
        fireEvent.click(screen.getByRole('button', { name: 'Row actions for shop.example.test' }))
        expect(screen.queryByRole('button', { name: 'Promote to Primary' })).not.toBeInTheDocument()
    })
    it('handles a rejected list command without an unhandled promise and leaves confirmation available', async () => {
        mocks.archive.mockRejectedValue(new Error('Unavailable'))
        render(<DomainListPage />)
        fireEvent.click(screen.getByRole('button', { name: 'Row actions for shop.example.test' }))
        fireEvent.click(screen.getByRole('button', { name: 'Archive' }))
        fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Archive' }))
        await waitFor(() => expect(mocks.archive).toHaveBeenCalledTimes(1))
        expect(screen.getByRole('dialog')).toBeInTheDocument()
        expect(mocks.refetch).not.toHaveBeenCalled()
    })
    it('describes saved domains rather than asserting DNS/TLS connectivity and wires quick actions', () => {
        render(<DomainsOverviewPage />)
        expect(screen.queryByText(/1 domains connected/)).not.toBeInTheDocument()
        expect(screen.getByText(/does not prove.*DNS.*TLS/i)).toBeInTheDocument()
        fireEvent.click(screen.getByRole('button', { name: 'Review SSL' }))
        expect(mocks.navigate).toHaveBeenCalledWith('/storefront/domains/list')
        fireEvent.click(screen.getByRole('button', { name: 'Manage Redirects' }))
        expect(mocks.navigate).toHaveBeenCalledTimes(2)
    })
})
