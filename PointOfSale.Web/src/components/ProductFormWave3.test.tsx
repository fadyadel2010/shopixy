import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createRef, forwardRef, useImperativeHandle } from 'react'
import type React from 'react'
import ProductForm from './ProductForm'

const apiGetMock = vi.hoisted(() => vi.fn())
const apiPostMock = vi.hoisted(() => vi.fn())
const apiPutMock = vi.hoisted(() => vi.fn())
const showSuccessMock = vi.hoisted(() => vi.fn())
const showErrorMock = vi.hoisted(() => vi.fn())
const confirmMock = vi.hoisted(() => vi.fn().mockResolvedValue(true))

vi.mock('../services/api', () => ({
    default: {
        get: apiGetMock,
        post: apiPostMock,
        put: apiPutMock,
    },
    getSafeApiErrorMessage: (_error: unknown, fallback: string) => fallback,
}))

vi.mock('../components/Toast', () => ({
    useToast: () => ({
        showSuccess: showSuccessMock,
        showError: showErrorMock,
    }),
}))

vi.mock('react-i18next', () => ({
    useTranslation: () => ({
        t: (key: string, fallback?: string) => fallback || key,
    }),
}))

vi.mock('../common/confirm/ConfirmDialogProvider', () => ({
    useConfirmDialog: () => ({ confirm: confirmMock }),
}))

vi.mock('../contexts/SettingsContext', () => ({
    useSettings: () => ({ formatCurrency: (value: number) => `${value}` }),
}))

vi.mock('../components/ProductMediaManager', () => ({
    default: () => <div>Product media</div>,
}))

vi.mock('../components/ProductVariantsTab', () => ({
    default: forwardRef((_props: unknown, ref) => {
        useImperativeHandle(ref, () => ({
            hasPendingChanges: () => false,
            prepareProductAuthoringDraft: () => Promise.resolve(null),
            getLastError: () => '',
        }))

        return <div>Product variants</div>
    }),
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
    default: () => <div>Affiliate Marketing Card</div>,
}))

vi.mock('@/storefront/products/ProductTemplateRegistry', () => ({
    getProductTemplateOptions: () => [{ id: 'default', name: 'Default' }],
}))

function mockSimpleCreateReads() {
    apiGetMock.mockImplementation((url: string) => {
        if (url === '/categories') return Promise.resolve({ data: [{ id: 'category-1', name: 'Apparel' }] })
        if (url === '/units') return Promise.resolve({ data: [{ id: 'unit-piece', name: 'Piece', symbol: 'pc' }] })
        if (url === '/admin/store-config') return Promise.resolve({ data: {} })
        return Promise.resolve({ data: {} })
    })
}

function mockSimpleEditReads(storefrontPrice: number | null = 80) {
    apiGetMock.mockImplementation((url: string) => {
        if (url === '/categories') return Promise.resolve({ data: [{ id: 'category-1', name: 'Apparel' }] })
        if (url === '/units') return Promise.resolve({ data: [{ id: 'unit-piece', name: 'Piece', symbol: 'pc' }] })
        if (url === '/admin/store-config') return Promise.resolve({ data: {} })
        if (url === '/products/product-1') {
            return Promise.resolve({
                data: {
                    name: 'Simple Product',
                    categoryId: 'category-1',
                    taxRate: 10,
                    priceIncludesTax: false,
                    trackStock: true,
                    isActive: true,
                    hasVariants: false,
                    publishStatus: 1,
                    isOnlineVisible: true,
                    isSearchable: true,
                    isFeatured: false,
                    rowVersion: '7',
                    variants: [],
                    units: [{
                        id: 'product-unit-1',
                        unitId: 'unit-piece',
                        conversionFactor: 1,
                        purchasePrice: 60,
                        sellingPrice: 100,
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
                    price: 100,
                    onlinePrice: storefrontPrice,
                    costPrice: 60,
                    isDefault: true,
                    isActive: true,
                }],
            })
        }
        return Promise.resolve({ data: {} })
    })
}

describe('ProductForm simple storefront pricing (Wave 3)', () => {
    beforeEach(() => {
        apiGetMock.mockReset()
        apiPostMock.mockReset()
        apiPostMock.mockResolvedValue({ data: { id: 'product-new' } })
        apiPutMock.mockReset()
        apiPutMock.mockResolvedValue({ data: { id: 'product-1', rowVersion: '8' } })
        showSuccessMock.mockClear()
        showErrorMock.mockClear()
        confirmMock.mockReset()
        confirmMock.mockResolvedValue(true)
    })

    it('exposes a storefront price field for simple products on create', async () => {
        mockSimpleCreateReads()
        render(<ProductForm mode="modal" onSave={vi.fn()} />)

        await screen.findByText('products.basicInfo')

        expect(screen.getByLabelText('products.storefrontPrice')).toBeInTheDocument()
    })

    it('persists an explicit storefront price while POS unit pricing stays independent', async () => {
        mockSimpleCreateReads()
        const { container } = render(<ProductForm mode="modal" onSave={vi.fn()} />)

        await screen.findByText('products.basicInfo')
        fireEvent.change(container.querySelector('input[type="text"]')!, {
            target: { value: 'Wave3 Simple' },
        })
        fireEvent.change(container.querySelector('select')!, {
            target: { value: 'category-1' },
        })

        fireEvent.click(screen.getByRole('button', { name: 'products.addProductUnit' }))
        fireEvent.change(container.querySelectorAll('select')[3], {
            target: { value: 'unit-piece' },
        })
        // Unit POS selling price stays 100 ...
        fireEvent.change(container.querySelectorAll('input[type="number"]')[3], {
            target: { value: '100' },
        })
        // ... while the storefront price is configured independently.
        fireEvent.change(screen.getByLabelText('products.storefrontPrice'), {
            target: { value: '80' },
        })

        fireEvent.click(screen.getByRole('button', { name: 'products.create' }))

        await waitFor(() => {
            expect(apiPostMock).toHaveBeenCalledWith('/products', expect.objectContaining({
                onlinePrice: 80,
                productUnits: [
                    expect.objectContaining({
                        unitId: 'unit-piece',
                        sellingPrice: 100,
                    }),
                ],
            }), expect.anything())
        })
    })

    it('loads the stored storefront price from the default variant on edit', async () => {
        mockSimpleEditReads(80)
        render(<ProductForm productId="product-1" mode="modal" onSave={vi.fn()} />)

        await waitFor(() => expect(apiGetMock).toHaveBeenCalledWith('/products/product-1/variants'))

        expect(await screen.findByLabelText('products.storefrontPrice')).toHaveValue(80)
    })

    it('sends a null storefront price when the field is left empty (POS fallback)', async () => {
        mockSimpleEditReads(null)
        const { container } = render(<ProductForm productId="product-1" mode="modal" onSave={vi.fn()} />)

        await waitFor(() => expect(apiGetMock).toHaveBeenCalledWith('/products/product-1/variants'))
        const storefrontInput = await screen.findByLabelText('products.storefrontPrice')
        expect((storefrontInput as HTMLInputElement).value).toBe('')

        fireEvent.change(container.querySelector('input[type="text"]')!, {
            target: { value: 'Touched name' },
        })
        fireEvent.click(screen.getByRole('button', { name: 'products.update' }))

        await waitFor(() => {
            expect(apiPutMock).toHaveBeenCalledWith('/products/product-1', expect.objectContaining({
                onlinePrice: null,
            }), expect.anything())
        })
    })
})

describe('ProductForm product type simplification (Wave 3)', () => {
    beforeEach(() => {
        apiGetMock.mockReset()
        apiPostMock.mockReset()
        apiPostMock.mockResolvedValue({ data: { id: 'product-new' } })
        apiPutMock.mockReset()
        apiPutMock.mockResolvedValue({ data: { id: 'product-1', rowVersion: '8' } })
        showSuccessMock.mockClear()
        showErrorMock.mockClear()
    })

    it('offers exactly the two product-type choices on create', async () => {
        mockSimpleCreateReads()
        const { container } = render(<ProductForm mode="modal" onSave={vi.fn()} />)

        await screen.findByText('products.basicInfo')

        const radios = container.querySelectorAll('input[name="product-type"]')
        expect(radios).toHaveLength(2)
        radios.forEach((radio) => expect(radio).not.toBeDisabled())
    })

    it('locks the product type on edit and explains why', async () => {
        mockSimpleEditReads(80)
        const { container } = render(<ProductForm productId="product-1" mode="modal" onSave={vi.fn()} />)

        await waitFor(() => expect(apiGetMock).toHaveBeenCalledWith('/products/product-1/variants'))

        const radios = container.querySelectorAll('input[name="product-type"]')
        expect(radios).toHaveLength(2)
        radios.forEach((radio) => expect(radio).toBeDisabled())
        expect(screen.getByText('products.productTypeLocked')).toBeInTheDocument()
    })
})
