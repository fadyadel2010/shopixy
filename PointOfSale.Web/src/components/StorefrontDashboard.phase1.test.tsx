import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import StorefrontStatusCard from './StorefrontStatusCard'
import WishlistAnalyticsCard from './WishlistAnalyticsCard'

const mocks = vi.hoisted(() => ({ get: vi.fn(), config: vi.fn(), navigate: vi.fn(), t: (_key: string, fallback: string) => fallback }))
const user = { id: '11111111-1111-4111-8111-111111111111', tenantId: '22222222-2222-4222-8222-222222222222', role: 'Owner' }
vi.mock('../store/authStore', () => ({ useAuthStore: (selector?: (state: { user: typeof user }) => unknown) => selector ? selector({ user }) : { user } }))
vi.mock('../services/api', () => ({ default: { get: mocks.get } }))
vi.mock('../modules/storefront/services', () => ({ storeConfigService: { getStoreConfig: mocks.config } }))
vi.mock('../contexts/SettingsContext', () => ({ useSettings: () => ({ settings: { storefrontUrl: 'https://example.test' }, formatCurrency: (price: number, currency: string) => `${currency} ${price}` }) }))
vi.mock('react-i18next', () => ({ useTranslation: () => ({ i18n: { language: 'en' }, t: mocks.t }) }))
vi.mock('react-router-dom', () => ({ useNavigate: () => mocks.navigate }))

const emptyAnalytics = { totalWishlistItems: 0, totalCustomersWithWishlists: 0, averageItemsPerWishlist: 0, topProducts: [], recentActivity: { itemsAddedLast7Days: 0 } }

describe('Storefront dashboard truthful read and recovery states', () => {
    beforeEach(() => vi.resetAllMocks())
    afterEach(cleanup)

    it('reports a failed status read as unavailable, not an unconfigured store, and retries', async () => {
        mocks.config.mockRejectedValueOnce({ response: { status: 503 } }).mockResolvedValueOnce({ ...user, tenantId: user.tenantId, storeName: 'AhramStore', themeId: 'default', storefrontStatus: 'live', rowVersion: '2' })
        render(<StorefrontStatusCard />)
        expect(await screen.findByText('Unable to load storefront status')).toBeInTheDocument()
        expect(screen.queryByText('Not configured')).not.toBeInTheDocument()
        fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
        expect(await screen.findByText('AhramStore')).toBeInTheDocument()
        fireEvent.click(screen.getByRole('button', { name: /Manage/ }))
        expect(mocks.navigate).toHaveBeenCalledWith('/storefront/status')
    })

    it('keeps the empty analytics card refreshable', async () => {
        mocks.get.mockResolvedValue({ data: emptyAnalytics })
        render(<WishlistAnalyticsCard />)
        await screen.findByText('No wishlist activity yet')
        fireEvent.click(screen.getByRole('button', { name: 'Refresh' }))
        await screen.findByText('No wishlist activity yet')
        expect(mocks.get).toHaveBeenCalledTimes(2)
    })

    it('reports analytics failure visibly and recovers from a retry', async () => {
        mocks.get.mockRejectedValueOnce({ response: { status: 503 } }).mockResolvedValueOnce({ data: emptyAnalytics })
        render(<WishlistAnalyticsCard />)
        await screen.findByText('Unable to load wishlist analytics')
        fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
        expect(await screen.findByText('No wishlist activity yet')).toBeInTheDocument()
    })

    it('ignores an older analytics response when overlapping refreshes finish out of order', async () => {
        let first!: (value: unknown) => void
        let second!: (value: unknown) => void
        mocks.get.mockResolvedValueOnce({ data: emptyAnalytics })
            .mockImplementationOnce(() => new Promise(resolve => { first = resolve }))
            .mockImplementationOnce(() => new Promise(resolve => { second = resolve }))
        render(<WishlistAnalyticsCard />)
        await screen.findByText('No wishlist activity yet')
        const refresh = screen.getByRole('button', { name: 'Refresh' })
        act(() => { fireEvent.click(refresh); fireEvent.click(refresh) })
        await act(async () => second({ data: { ...emptyAnalytics, totalWishlistItems: 9, totalCustomersWithWishlists: 3, averageItemsPerWishlist: 3 } }))
        await act(async () => first({ data: emptyAnalytics }))
        expect(screen.getByText('9')).toBeInTheDocument()
        expect(screen.queryByText('No wishlist activity yet')).not.toBeInTheDocument()
    })
})
