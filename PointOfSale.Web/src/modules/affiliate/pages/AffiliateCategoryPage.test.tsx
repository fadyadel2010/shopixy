import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router-dom'

const discoverPublicProductsMock = vi.hoisted(() => vi.fn())

vi.mock('../lib/publicDiscoverApi', async (importOriginal) => ({
    ...(await importOriginal()),
    discoverPublicProducts: discoverPublicProductsMock,
}))

vi.mock('../i18n/LanguageContext', () => ({
    useLanguage: () => ({ t: (key: string) => key, dir: 'ltr', isRTL: false }),
}))

vi.mock('../components/LanguageSwitcher', () => ({ default: () => <div /> }))
vi.mock('../components/ThemeSwitcher', () => ({ default: () => <div /> }))

vi.mock('framer-motion', async (importOriginal) => ({
    ...(await importOriginal()),
    motion: {
        div: ({ children, ...props }: { children?: React.ReactNode }) => <div {...props}>{children}</div>,
    },
}))

import AffiliateCategoryPage from './AffiliateCategoryPage'

const publicProduct = (overrides = {}) => ({
    productId: 'p-1',
    slug: 'nokia-g22',
    name: 'Nokia G22',
    description: 'Public description',
    images: ['https://cdn.test/nokia.jpg'],
    price: 4999,
    currency: 'EGP',
    storeName: 'Electro Store',
    storeSlug: 'electro-store',
    storefrontUrl: 'https://electro.test/p/nokia-g22',
    categoryName: 'Electronics',
    categorySlug: 'electronics',
    ...overrides,
})

function renderPage(route = '/affiliate/category/electronics') {
    return render(
        <MemoryRouter initialEntries={[route]}>
            <Routes>
                <Route path="/affiliate/category/:slug" element={<AffiliateCategoryPage />} />
            </Routes>
        </MemoryRouter>,
    )
}

describe('AF-39 AffiliateCategoryPage anonymous public discovery', () => {
    beforeEach(() => {
        discoverPublicProductsMock.mockReset()
        discoverPublicProductsMock.mockResolvedValue({ items: [], totalCount: 0, totalPages: 0 })
        vi.stubGlobal(
            'fetch',
            vi.fn().mockResolvedValue({
                json: async () => [{ id: 'c-1', name: 'Electronics', slug: 'electronics', productCount: 1 }],
            }),
        )
    })

    it('requests the anonymous public list with the category filter and a public-safe sort', async () => {
        discoverPublicProductsMock.mockResolvedValue({
            items: [publicProduct()],
            totalCount: 1,
            totalPages: 1,
        })

        renderPage()

        await screen.findByText('Nokia G22')

        expect(discoverPublicProductsMock).toHaveBeenCalledWith(
            expect.objectContaining({
                page: 1,
                pageSize: 20,
                categorySlug: 'electronics',
                sort: 'newest',
            }),
        )
    })

    it('clamps commission sorts from the URL to the anonymous default and never offers them', async () => {
        discoverPublicProductsMock.mockResolvedValue({ items: [publicProduct()], totalCount: 1, totalPages: 1 })

        const { container } = renderPage('/affiliate/category/electronics?sort=commission_desc')

        await screen.findByText('Nokia G22')

        expect(discoverPublicProductsMock).toHaveBeenLastCalledWith(
            expect.objectContaining({ sort: 'newest' }),
        )

        const optionValues = Array.from(
            container.querySelectorAll('#af33-sort-select option'),
        ).map(o => (o as HTMLOptionElement).value)
        expect(optionValues).not.toContain('commission_desc')
        expect(optionValues).not.toContain('commission_asc')
        expect(optionValues).toContain('newest')
        expect(optionValues).toContain('price_asc')
    })

    it('renders no commission UI when the public DTO carries no commission fields', async () => {
        discoverPublicProductsMock.mockResolvedValue({
            items: [publicProduct()],
            totalCount: 1,
            totalPages: 1,
        })

        const { container } = renderPage()

        await screen.findByText('Nokia G22')

        expect(container.textContent).not.toMatch(/earn .+% commission/i)
        expect(container.textContent).not.toContain('NaN')
        expect(screen.getByRole('link', { name: 'Nokia G22' })).toHaveAttribute(
            'href',
            '/affiliate/products/p-1',
        )
    })

    it('preserves pagination through the public client', async () => {
        discoverPublicProductsMock.mockResolvedValue({
            items: [publicProduct()],
            totalCount: 45,
            totalPages: 3,
        })

        renderPage()

        await screen.findByText('Nokia G22')
        expect(screen.getByText(/Page 1 of 3/)).toBeInTheDocument()

        fireEvent.click(screen.getByRole('button', { name: 'Next' }))

        await waitFor(() =>
            expect(discoverPublicProductsMock).toHaveBeenLastCalledWith(
                expect.objectContaining({ page: 2 }),
            ),
        )
        expect(await screen.findByText(/Page 2 of 3/)).toBeInTheDocument()
    })

    it('keeps the error and retry flow on public-client failures without leaking payloads', async () => {
        const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {})
        discoverPublicProductsMock
            .mockRejectedValueOnce(Object.assign(new Error('Request failed with status code 401'), { response: { status: 401 } }))
            .mockResolvedValue({ items: [publicProduct()], totalCount: 1, totalPages: 1 })

        renderPage()

        expect(await screen.findByText('Failed to load products')).toBeInTheDocument()

        // Public failures stay in the UI contract and never log response/auth data.
        expect(errorSpy).not.toHaveBeenCalled()

        fireEvent.click(screen.getByRole('button', { name: 'Retry' }))

        expect(await screen.findByText('Nokia G22')).toBeInTheDocument()
        expect(discoverPublicProductsMock).toHaveBeenCalledTimes(2)
        errorSpy.mockRestore()
    })

    it('shows the empty state when the category has no admitted products', async () => {
        discoverPublicProductsMock.mockResolvedValue({ items: [], totalCount: 0, totalPages: 0 })

        renderPage()

        expect(await screen.findByText('No products in this category yet')).toBeInTheDocument()
    })
})
