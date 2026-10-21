import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import ProductDrawer from './ProductDrawer'
import LinksTab from './LinksTab'
import DiscoverTabEnhanced from './DiscoverTabEnhanced'

const api = vi.hoisted(() => ({ getProductIntelligence: vi.fn(), createPromotionLink: vi.fn(), listPromotionLinks: vi.fn(), deactivatePromotionLink: vi.fn(), discoverProductsIntelligence: vi.fn(), getDiscoverCategories: vi.fn() }))
vi.mock('../../../../services/affiliateApi', () => ({ default: api }))
vi.mock('../../i18n/LanguageContext', () => ({ useLanguage: () => ({ t: (key: string) => key, isRTL: false }) }))
const product = (id = 'product-a') => ({ productId: id, tenantId: `merchant-${id}`, name: id, price: 25.75, currency: 'EGP', images: [], variants: [], totalAvailableStock: 19 })
const link = { id: 'link-a', productId: 'product-a', productName: 'Saved product', storeName: 'Saved merchant', referralCode: 'LINK123456', isActive: true, promotionUrl: 'https://merchant.example.test/products/a?ref=LINK123456', createdAt: '2026-10-21T00:00:00Z' }
const deferred = <T,>() => { let resolve!: (value: T) => void; const promise = new Promise<T>(r => { resolve = r }); return { resolve, promise } }

describe('Promotion workflow', () => {
    beforeEach(() => {
        Object.values(api).forEach(mock => mock.mockReset())
        api.getProductIntelligence.mockImplementation(id => Promise.resolve(product(id)))
        api.listPromotionLinks.mockResolvedValue([])
        api.getDiscoverCategories.mockResolvedValue([])
        api.discoverProductsIntelligence.mockResolvedValue({ items: [], totalCount: 0 })
    })

    it('browses without writing, creates explicitly, and retries failures', async () => {
        api.createPromotionLink.mockRejectedValueOnce(new Error('Unavailable')).mockResolvedValue(link)
        render(<ProductDrawer productId="product-a" onClose={() => {}} />)
        await screen.findByText('product-a')
        expect(api.createPromotionLink).not.toHaveBeenCalled()
        fireEvent.click(screen.getByRole('button', { name: 'portal.discoverTab.promoteAction' }))
        await screen.findByRole('alert')
        expect(screen.getByTestId('promotion-url')).not.toHaveTextContent(link.promotionUrl)
        fireEvent.click(screen.getByRole('button', { name: 'portal.discoverTab.promoteAction' }))
        await waitFor(() => expect(screen.getByTestId('promotion-url')).toHaveTextContent(link.promotionUrl))
        expect(api.createPromotionLink).toHaveBeenLastCalledWith('merchant-product-a', 'product-a')
        expect(screen.getByText('19 portal.drawer.units')).toBeInTheDocument()
    })

    it('blocks rapid repeated clicks while creation is pending', async () => {
        const pending = deferred<typeof link>(); api.createPromotionLink.mockReturnValue(pending.promise)
        render(<ProductDrawer productId="product-a" onClose={() => {}} />)
        const button = await screen.findByRole('button', { name: 'portal.discoverTab.promoteAction' })
        fireEvent.click(button); fireEvent.click(button)
        expect(api.createPromotionLink).toHaveBeenCalledTimes(1)
        expect(screen.getByRole('button', { name: 'portal.drawer.generating' })).toBeDisabled()
        await act(async () => pending.resolve(link))
    })

    it('does not let a late owned-link read erase a newly persisted link', async () => {
        const pending = deferred<unknown[]>(); api.listPromotionLinks.mockReturnValue(pending.promise)
        api.createPromotionLink.mockResolvedValue(link)
        render(<ProductDrawer productId="product-a" onClose={() => {}} />)
        fireEvent.click(await screen.findByRole('button', { name: 'portal.discoverTab.promoteAction' }))
        await waitFor(() => expect(screen.getByTestId('promotion-url')).toHaveTextContent(link.promotionUrl))
        await act(async () => pending.resolve([]))
        expect(screen.getByTestId('promotion-url')).toHaveTextContent(link.promotionUrl)
    })

    it('discards stale detail and promotion responses when product context changes', async () => {
        const pending = deferred<typeof link>(); api.createPromotionLink.mockReturnValue(pending.promise)
        const view = render(<ProductDrawer productId="product-a" onClose={() => {}} />)
        fireEvent.click(await screen.findByRole('button', { name: 'portal.discoverTab.promoteAction' }))
        view.rerender(<ProductDrawer productId="product-b" onClose={() => {}} />)
        await screen.findByText('product-b')
        await act(async () => pending.resolve(link))
        expect(screen.getByTestId('promotion-url')).not.toHaveTextContent(link.promotionUrl)
    })

    it('restores a backend-issued owned link without generating a replacement', async () => {
        api.listPromotionLinks.mockResolvedValue([link])
        render(<ProductDrawer productId="product-a" onClose={() => {}} />)
        await waitFor(() => expect(screen.getByTestId('promotion-url')).toHaveTextContent(link.promotionUrl))
        expect(api.createPromotionLink).not.toHaveBeenCalled()
        expect(screen.getByRole('link', { name: 'portal.drawer.visitStorefront' })).toHaveAttribute('href', link.promotionUrl)
    })

    it('shows retryable My Links failure, not coming-soon or an empty registry', async () => {
        api.listPromotionLinks.mockRejectedValueOnce(new Error('Unavailable')).mockResolvedValue([link])
        render(<LinksTab />)
        await screen.findByRole('alert')
        expect(screen.queryByText('portal.linksTab.noLinks')).not.toBeInTheDocument()
        expect(screen.queryByText('portal.linksTab.comingSoon')).not.toBeInTheDocument()
        fireEvent.click(screen.getByRole('button', { name: 'portal.discoverTab.retry' }))
        await screen.findByText('Saved product')
        expect(screen.getByText('Saved merchant')).toBeInTheDocument()
        expect(screen.getByRole('link', { name: 'portal.drawer.visitStorefront' })).toHaveAttribute('href', link.promotionUrl)
    })

    it('shows a genuine empty link list separately', async () => {
        render(<LinksTab />)
        await screen.findByText('portal.linksTab.noLinks')
        expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    })

    it('uses selected merchant for discovery and counts, retaining the global actor contract', async () => {
        render(<MemoryRouter initialEntries={['/affiliate/portal?tab=discover&storeTenantId=merchant-b']}><DiscoverTabEnhanced /></MemoryRouter>)
        await screen.findByText('portal.discoverTab.noProducts')
        expect(api.getDiscoverCategories).toHaveBeenCalledWith('merchant-b')
        expect(api.discoverProductsIntelligence).toHaveBeenCalledWith(expect.objectContaining({ storeTenantId: 'merchant-b', page: 1 }))
        expect(api.createPromotionLink).not.toHaveBeenCalled()
    })

    it('distinguishes discovery failure from an empty category and retries', async () => {
        api.discoverProductsIntelligence.mockRejectedValueOnce(new Error('Unavailable')).mockResolvedValue({ items: [], totalCount: 0 })
        render(<MemoryRouter><DiscoverTabEnhanced /></MemoryRouter>)
        await screen.findByText('portal.discoverTab.loadError')
        expect(screen.queryByText('portal.discoverTab.noProducts')).not.toBeInTheDocument()
        fireEvent.click(screen.getByRole('button', { name: 'portal.discoverTab.retry' }))
        await screen.findByText('portal.discoverTab.noProducts')
    })

    it('exposes a failed category request separately and retries', async () => {
        api.getDiscoverCategories.mockRejectedValueOnce(new Error('Unavailable')).mockResolvedValue([{ id: 'category', slug: 'electronics', name: 'Electronics', productCount: 0 }])
        render(<MemoryRouter><DiscoverTabEnhanced /></MemoryRouter>)
        await screen.findByText('portal.discoverTab.categoryError')
        fireEvent.click(screen.getByRole('button', { name: 'portal.discoverTab.retry' }))
        expect((await screen.findByText('Electronics', {}, { timeout: 5000 })).closest('button')).toHaveTextContent('0')
        expect(screen.queryByText('portal.discoverTab.categoryError')).not.toBeInTheDocument()
    })

    it('ignores a detail response for the previously selected product', async () => {
        const stale = deferred<ReturnType<typeof product>>()
        api.getProductIntelligence.mockReturnValueOnce(stale.promise).mockResolvedValue(product('product-b'))
        const view = render(<ProductDrawer productId="product-a" onClose={() => {}} />)
        view.rerender(<ProductDrawer productId="product-b" onClose={() => {}} />)
        await screen.findByText('product-b')
        await act(async () => stale.resolve(product('product-a')))
        expect(screen.queryByText('product-a')).not.toBeInTheDocument()
        expect(api.createPromotionLink).not.toHaveBeenCalled()
    })
})
