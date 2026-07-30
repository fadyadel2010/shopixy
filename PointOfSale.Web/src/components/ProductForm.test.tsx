import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type React from 'react'
import ProductForm from './ProductForm'

const apiGetMock = vi.hoisted(() => vi.fn())
const apiPostMock = vi.hoisted(() => vi.fn())
const apiPutMock = vi.hoisted(() => vi.fn())
const productAffiliateSettingsCardMock = vi.hoisted(() => vi.fn(() => <div>Affiliate Marketing Card</div>))
const useAuthStoreMock = vi.hoisted(() => vi.fn((selector: (state: unknown) => unknown) => selector({
    user: { role: 'Admin' },
})))

vi.mock('../services/api', () => ({
    default: {
        get: apiGetMock,
        post: apiPostMock,
        put: apiPutMock,
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
    getProductTemplateOptions: () => [{ id: 'default', name: 'Default' }],
}))

function mockProductReads() {
    apiGetMock.mockImplementation((url: string) => {
        if (url === '/categories') return Promise.resolve({ data: [{ id: 'category-1', name: 'Apparel' }] })
        if (url === '/units') return Promise.resolve({ data: [{ id: 'unit-piece', name: 'Piece', symbol: 'pc' }] })
        if (url === '/admin/store-config') return Promise.resolve({ data: {} })
        if (url === '/products/product-1') {
            return Promise.resolve({
                data: {
                    name: 'Test Product',
                    categoryId: 'category-1',
                    taxRate: 10,
                    priceIncludesTax: false,
                    trackStock: true,
                    isActive: true,
                    hasVariants: false,
                    rowVersion: '7',
                    variants: [],
                    units: [{
                        id: 'product-unit-1',
                        unitId: 'unit-piece',
                        conversionFactor: 1,
                        sellingPrice: 12,
                        isBaseUnit: true,
                        isDefault: true,
                    }],
                },
            })
        }
        if (url === '/products/product-1/variants') {
            return Promise.resolve({
                data: [{
                    id: 'variant-default',
                    sku: 'SKU-1',
                    barcode: 'BAR-1',
                    price: 12,
                    onlinePrice: 12,
                    costPrice: 5,
                    isDefault: true,
                    isActive: true,
                }],
            })
        }

        return Promise.resolve({ data: {} })
    })
}

describe('product affiliate entry presentation', () => {
    beforeEach(() => {
        apiGetMock.mockReset()
        apiPostMock.mockReset()
        apiPostMock.mockResolvedValue({ data: { id: 'product-new' } })
        apiPutMock.mockReset()
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

    it('submits simple product authoring with base and default unit state', async () => {
        const { container } = render(<ProductForm mode="modal" onSave={vi.fn()} />)

        await screen.findByText('products.basicInfo')
        fireEvent.change(container.querySelector('input[type="text"]')!, {
            target: { value: 'Cotton Shirt' },
        })
        fireEvent.change(container.querySelector('select')!, {
            target: { value: 'category-1' },
        })
        fireEvent.change(container.querySelectorAll('input[type="number"]')[1], {
            target: { value: '12' },
        })

        fireEvent.click(screen.getByRole('button', { name: 'products.addProductUnit' }))
        fireEvent.change(container.querySelectorAll('select')[3], {
            target: { value: 'unit-piece' },
        })
        fireEvent.change(container.querySelectorAll('input[type="number"]')[6], {
            target: { value: '12' },
        })

        fireEvent.click(screen.getByRole('button', { name: 'products.create' }))

        await waitFor(() => {
            expect(apiPostMock).toHaveBeenCalledWith('/products', expect.objectContaining({
                name: 'Cotton Shirt',
                categoryId: 'category-1',
                productUnits: [
                    expect.objectContaining({
                        unitId: 'unit-piece',
                        conversionFactor: 1,
                        sellingPrice: 12,
                        isBaseUnit: true,
                        isDefault: true,
                    }),
                ],
            }), expect.objectContaining({
                headers: expect.objectContaining({
                    'Idempotency-Key': expect.stringContaining('product-authoring:create:'),
                }),
            }))
            expect(apiPutMock).not.toHaveBeenCalledWith(
                expect.stringContaining('/variants/'),
                expect.anything(),
                expect.anything(),
            )
        })
    })

    it('allows draft products without publication-ready units', async () => {
        const { container } = render(<ProductForm mode="modal" onSave={vi.fn()} />)

        await screen.findByText('products.basicInfo')
        fireEvent.change(container.querySelector('input[type="text"]')!, {
            target: { value: 'Draft Shirt' },
        })
        fireEvent.change(container.querySelector('select')!, {
            target: { value: 'category-1' },
        })
        const publishStatusSelect = Array.from(container.querySelectorAll('select'))
            .find((select) => Array.from(select.options).some((option) => option.textContent === 'products.statusDraft'))!
        fireEvent.change(publishStatusSelect, {
            target: { value: '0' },
        })

        fireEvent.click(screen.getByRole('button', { name: 'products.create' }))

        await waitFor(() => {
            expect(apiPostMock).toHaveBeenCalledWith('/products', expect.objectContaining({
                name: 'Draft Shirt',
                publishStatus: 0,
                productUnits: [],
            }), expect.objectContaining({
                headers: expect.objectContaining({
                    'Idempotency-Key': expect.stringContaining('product-authoring:create:'),
                }),
            }))
        })
    })

    it('reuses the same idempotency key when retrying the same failed save', async () => {
        apiPostMock
            .mockRejectedValueOnce({ response: { data: { message: 'network timeout after commit' } } })
            .mockResolvedValueOnce({ data: { id: 'product-new' } })
        const { container } = render(<ProductForm mode="modal" onSave={vi.fn()} />)

        await screen.findByText('products.basicInfo')
        fireEvent.change(container.querySelector('input[type="text"]')!, {
            target: { value: 'Retry Shirt' },
        })
        fireEvent.change(container.querySelector('select')!, {
            target: { value: 'category-1' },
        })
        fireEvent.change(container.querySelectorAll('input[type="number"]')[1], {
            target: { value: '12' },
        })
        fireEvent.click(screen.getByRole('button', { name: 'products.addProductUnit' }))
        fireEvent.change(container.querySelectorAll('select')[3], {
            target: { value: 'unit-piece' },
        })
        fireEvent.change(container.querySelectorAll('input[type="number"]')[6], {
            target: { value: '12' },
        })

        fireEvent.click(screen.getByRole('button', { name: 'products.create' }))
        await waitFor(() => expect(apiPostMock).toHaveBeenCalledTimes(1))
        const firstKey = apiPostMock.mock.calls[0][2].headers['Idempotency-Key']

        fireEvent.click(screen.getByRole('button', { name: 'products.create' }))
        await waitFor(() => expect(apiPostMock).toHaveBeenCalledTimes(2))

        expect(apiPostMock.mock.calls[1][2].headers['Idempotency-Key']).toBe(firstKey)
    })

    it('updates an existing product with exactly one canonical product mutation request', async () => {
        const { container } = render(<ProductForm productId="product-1" mode="modal" onSave={vi.fn()} />)

        await waitFor(() => expect(apiGetMock).toHaveBeenCalledWith('/products/product-1'))
        await waitFor(() => expect(apiGetMock).toHaveBeenCalledWith('/products/product-1/variants'))
        fireEvent.change(container.querySelector('input[type="text"]')!, {
            target: { value: 'Updated Product' },
        })

        fireEvent.click(screen.getByRole('button', { name: 'products.update' }))

        await waitFor(() => {
            expect(apiPutMock).toHaveBeenCalledTimes(1)
            expect(apiPutMock).toHaveBeenCalledWith('/products/product-1', expect.objectContaining({
                name: 'Updated Product',
            }), expect.objectContaining({
                headers: expect.objectContaining({
                    'Idempotency-Key': expect.stringContaining('product-authoring:product-1:'),
                    'If-Match': '"7"',
                }),
            }))
        })

        expect(apiPostMock).not.toHaveBeenCalled()
    })
})
