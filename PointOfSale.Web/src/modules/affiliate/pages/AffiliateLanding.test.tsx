import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, useLocation } from 'react-router-dom'

const discoverPublicProductsMock = vi.hoisted(() => vi.fn())

vi.mock('../lib/publicDiscoverApi', async (importOriginal) => ({
    ...(await importOriginal()),
    discoverPublicProducts: discoverPublicProductsMock,
}))

vi.mock('../i18n/LanguageContext', () => ({
    useLanguage: () => ({ t: (key: string) => key, dir: 'ltr', isRTL: false }),
}))

vi.mock('../components/LanguageSwitcher', () => ({ default: () => null }))
vi.mock('../components/ThemeSwitcher', () => ({ default: () => null }))
vi.mock('./landing/CategoriesSection', () => ({ default: () => null }))

vi.mock('framer-motion', async (importOriginal) => ({
    ...(await importOriginal()),
    motion: {
        div: ({ children }: { children?: React.ReactNode }) => <div>{children}</div>,
        h1: ({ children }: { children?: React.ReactNode }) => <h1>{children}</h1>,
        p: ({ children }: { children?: React.ReactNode }) => <p>{children}</p>,
    },
}))

import AffiliateLanding from './AffiliateLanding'

function LocationProbe() {
    return <div data-testid="location">{useLocation().pathname}{useLocation().search}</div>
}

const publicProduct = {
    productId: '11111111-1111-1111-1111-111111111111',
    slug: 'nokia-n73',
    name: 'Nokia N73',
    images: ['https://cdn.test/nokia.jpg'],
    price: 1500,
    currency: 'EGP',
    storeName: 'Electronics Store',
    categoryName: 'Electronics',
}

describe('AffiliateLanding canonical public discovery boundary', () => {
    beforeEach(() => {
        discoverPublicProductsMock.mockReset()
        discoverPublicProductsMock.mockResolvedValue({ items: [publicProduct], totalCount: 1, totalPages: 1 })
    })

    it('renders featured products from the same anonymous SQL-admitted client as category pages', async () => {
        const { container } = render(
            <MemoryRouter initialEntries={['/affiliate']}>
                <AffiliateLanding />
            </MemoryRouter>,
        )

        expect(await screen.findByRole('link', { name: 'Nokia N73' })).toHaveAttribute(
            'href',
            '/affiliate/products/11111111-1111-1111-1111-111111111111',
        )
        expect(discoverPublicProductsMock).toHaveBeenCalledWith(expect.objectContaining({
            page: 1,
            pageSize: 12,
            signal: expect.any(AbortSignal),
        }))
        expect(screen.getByRole('img', { name: 'Nokia N73' })).toHaveAttribute('src', publicProduct.images[0])
        expect(container.textContent).not.toContain('estimatedCommission')
        expect(container.textContent).not.toMatch(/\d+% commission/i)
    })

    it('delegates session authority to the portal while preserving the product search contract', async () => {
        render(
            <MemoryRouter initialEntries={['/affiliate']}>
                <AffiliateLanding />
                <LocationProbe />
            </MemoryRouter>,
        )

        await screen.findByRole('link', { name: 'Nokia N73' })
        fireEvent.click(document.querySelector('#landing-promote-11111111-1111-1111-1111-111111111111')!)

        await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent(
            '/affiliate/portal?tab=discover&search=Nokia%20N73',
        ))
    })
})
