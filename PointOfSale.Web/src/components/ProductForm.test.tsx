import { render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type React from 'react'
import ProductForm from './ProductForm'

const apiGetMock = vi.hoisted(() => vi.fn())
const productAffiliateSettingsCardMock = vi.hoisted(() => vi.fn(() => <div>Affiliate Marketing Card</div>))
const useAuthStoreMock = vi.hoisted(() => vi.fn((selector: (state: unknown) => unknown) => selector({
    user: { role: 'Admin' },
})))

vi.mock('../services/api', () => ({
    default: {
        get: apiGetMock,
        post: vi.fn(),
        put: vi.fn(),
    },
}))

vi.mock('../store/authStore', () => ({
    useAuthStore: useAuthStoreMock,
}))

vi.mock('react-i18next', () => ({
    useTranslation: () => ({
        t: (key: string, fallback?: string) => {
            const labels: Record<string, string> = {
                'affiliate.title': 'Affiliate Marketing',
                'products.marketplaceCategory': 'Commission and marketplace category settings.',
            }

            return labels[key] || fallback || key
        },
    }),
}))

vi.mock('../common/confirm/ConfirmDialogProvider', () => ({
    useConfirmDialog: () => ({ confirm: vi.fn().mockResolvedValue(true) }),
}))

vi.mock('../contexts/SettingsContext', () => ({
    useSettings: () => ({ formatCurrency: (value: number) => `${value}` }),
}))

vi.mock('../components/ProductMediaManager', () => ({
    default: () => <div>Product media</div>,
}))

vi.mock('../components/ProductVariantsTab', () => ({
    default: () => <div>Product variants</div>,
}))

vi.mock('../components/ProductPixelOverrideTab', () => ({
    default: () => <div>Pixel override</div>,
}))

vi.mock('./SidebarPanel', () => ({
    default: ({ title, children }: { title: string, children: React.ReactNode }) => (
        <section aria-label={title}>{children}</section>
    ),
}))

vi.mock('./ProductAffiliateSettingsCard', () => ({
    default: productAffiliateSettingsCardMock,
}))

vi.mock('@/storefront/products/ProductTemplateRegistry', () => ({
    getProductTemplateOptions: () => [{ value: 'default', label: 'Default' }],
}))

function mockProductReads() {
    apiGetMock.mockImplementation((url: string) => {
        if (url === '/categories') return Promise.resolve({ data: [] })
        if (url === '/units') return Promise.resolve({ data: [] })
        if (url === '/admin/store-config') return Promise.resolve({ data: {} })
        if (url === '/products/product-1') {
            return Promise.resolve({
                data: {
                    name: 'Test Product',
                    taxRate: 10,
                    priceIncludesTax: false,
                    trackStock: true,
                    isActive: true,
                    hasVariants: false,
                    variants: [],
                },
            })
        }
        if (url === '/products/product-1/variants') return Promise.resolve({ data: [] })

        return Promise.resolve({ data: {} })
    })
}

describe('product affiliate entry presentation', () => {
    beforeEach(() => {
        apiGetMock.mockReset()
        productAffiliateSettingsCardMock.mockClear()
        useAuthStoreMock.mockImplementation((selector: (state: unknown) => unknown) => selector({
            user: { role: 'Admin' },
        }))
        mockProductReads()
    })

    it('shows the product affiliate entry panel for Owner/Admin presentation roles', async () => {
        render(<ProductForm productId="product-1" mode="workspace" />)

        expect(await screen.findByLabelText('Affiliate Marketing')).toBeInTheDocument()
        expect(productAffiliateSettingsCardMock.mock.calls[0]?.[0]).toEqual(
            expect.objectContaining({ productId: 'product-1' })
        )
    })

    it('withholds the product affiliate entry panel for lower presentation roles', async () => {
        useAuthStoreMock.mockImplementation((selector: (state: unknown) => unknown) => selector({
            user: { role: 'Cashier' },
        }))

        render(<ProductForm productId="product-1" mode="workspace" />)

        await waitFor(() => expect(apiGetMock).toHaveBeenCalledWith('/products/product-1'))
        expect(screen.queryByLabelText('Affiliate Marketing')).not.toBeInTheDocument()
        expect(screen.queryByText('Affiliate Marketing Card')).not.toBeInTheDocument()
        expect(productAffiliateSettingsCardMock).not.toHaveBeenCalled()
    })
})
