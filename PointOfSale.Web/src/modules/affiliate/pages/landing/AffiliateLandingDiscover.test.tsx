import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter } from 'react-router-dom'

const discoverPublicProductsMock = vi.hoisted(() => vi.fn())

vi.mock('../../lib/publicDiscoverApi', async (importOriginal) => ({
    ...(await importOriginal()),
    discoverPublicProducts: discoverPublicProductsMock,
}))

vi.mock('../../i18n/LanguageContext', () => ({
    useLanguage: () => ({ t: (key: string) => key, dir: 'ltr', isRTL: false }),
}))

vi.mock('framer-motion', async (importOriginal) => ({
    ...(await importOriginal()),
    motion: {
        div: ({ children, ...props }: { children?: React.ReactNode }) => <div {...props}>{children}</div>,
    },
}))

import AffiliateLandingDiscover from './AffiliateLandingDiscover'

function renderDiscover() {
    return render(
        <MemoryRouter>
            <AffiliateLandingDiscover onSignUpClick={() => {}} />
        </MemoryRouter>,
    )
}

const publicProduct = (overrides = {}) => ({
    productId: 'p-1',
    slug: 'nokia-g22',
    name: 'Nokia G22',
    images: ['https://cdn.test/nokia.jpg'],
    price: 4999,
    currency: 'EGP',
    storeName: 'Electro Store',
    ...overrides,
})

describe('AF-39 AffiliateLandingDiscover anonymous public discovery', () => {
    beforeEach(() => {
        discoverPublicProductsMock.mockReset()
        discoverPublicProductsMock.mockResolvedValue({ items: [], totalCount: 0, totalPages: 0 })
    })

    it('uses the anonymous public list contract with no sort and no commission intelligence', async () => {
        discoverPublicProductsMock.mockResolvedValue({
            items: [publicProduct()],
            totalCount: 1,
            totalPages: 1,
        })

        const { container } = renderDiscover()

        expect(await screen.findByText('Nokia G22')).toBeInTheDocument()

        expect(discoverPublicProductsMock).toHaveBeenCalledWith(
            expect.objectContaining({ page: 1, pageSize: 12 }),
        )
        const callArgs = discoverPublicProductsMock.mock.calls[0][0]
        expect(callArgs).not.toHaveProperty('sort')

        // No commission badge/percentage may render from the public DTO.
        expect(container.textContent).not.toMatch(/commission/i)
        expect(container.textContent).not.toContain('NaN')
    })

    it('pages through results via the public client', async () => {
        discoverPublicProductsMock.mockResolvedValue({
            items: [publicProduct()],
            totalCount: 30,
            totalPages: 3,
        })

        renderDiscover()

        await screen.findByText('Nokia G22')

        fireEvent.click(screen.getByRole('button', { name: 'portal.discoverTab.next' }))

        await waitFor(() =>
            expect(discoverPublicProductsMock).toHaveBeenLastCalledWith(
                expect.objectContaining({ page: 2 }),
            ),
        )
    })
})
