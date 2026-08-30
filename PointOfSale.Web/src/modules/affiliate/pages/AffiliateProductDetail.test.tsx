import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router-dom'

const discoverPublicProductDetailMock = vi.hoisted(() => vi.fn())

vi.mock('../lib/publicDiscoverApi', async (importOriginal) => ({
    ...(await importOriginal()),
    discoverPublicProductDetail: discoverPublicProductDetailMock,
}))

vi.mock('@/utils/htmlSanitizer', () => ({
    sanitizeRichTextHtml: (value: string) => value,
}))

import AffiliateProductDetail from './AffiliateProductDetail'

const publicDetail = (overrides = {}) => ({
    productId: 'p-1',
    slug: 'nokia-g22',
    name: 'Nokia G22',
    description: '<p>Public description</p>',
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

function renderPage(productId = '11111111-1111-1111-1111-111111111111') {
    return render(
        <MemoryRouter initialEntries={[`/affiliate/products/${productId}`]}>
            <Routes>
                <Route path="/affiliate/products/:slug" element={<AffiliateProductDetail />} />
            </Routes>
        </MemoryRouter>,
    )
}

describe('AF-39 AffiliateProductDetail anonymous public contract', () => {
    beforeEach(() => {
        discoverPublicProductDetailMock.mockReset()
    })

    it('resolves the item through the canonical anonymous detail client and renders sanitized presentation', async () => {
        discoverPublicProductDetailMock.mockResolvedValue(publicDetail())

        const { container } = renderPage()

        expect(await screen.findByText('Nokia G22')).toBeInTheDocument()

        expect(discoverPublicProductDetailMock).toHaveBeenCalledWith(
            '11111111-1111-1111-1111-111111111111',
            expect.objectContaining({ signal: expect.any(AbortSignal) }),
        )
        expect(container.querySelector('img')?.getAttribute('src')).toBe('https://cdn.test/nokia.jpg')
        expect(container.textContent).toContain('4999 EGP')
        expect(container.textContent).toContain('Electronics')
        expect(container.textContent).toContain('Sold by')

        // Public DTO carries no commission — nothing commission-bearing may render.
        expect(container.textContent).not.toContain('NaN')
        expect(discoverPublicProductDetailMock.mock.calls[0].length).toBe(2)
    })

    it('keeps the failure state when the public detail route rejects (e.g. legacy 404/401)', async () => {
        const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {})
        discoverPublicProductDetailMock.mockRejectedValue(
            Object.assign(new Error('Request failed with status code 404'), { response: { status: 404 } }),
        )

        renderPage()

        expect(await screen.findByText(/Failed to load product details\./)).toBeInTheDocument()

        expect(errorSpy).not.toHaveBeenCalled()
        errorSpy.mockRestore()
    })
})
