import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createRef, forwardRef, useImperativeHandle } from 'react'
import type React from 'react'
import ProductForm from './ProductForm'

const apiGetMock = vi.hoisted(() => vi.fn())
const apiPostMock = vi.hoisted(() => vi.fn())
const apiPutMock = vi.hoisted(() => vi.fn())
const variantsHasPendingChangesMock = vi.hoisted(() => vi.fn())
const variantsPrepareDraftMock = vi.hoisted(() => vi.fn())
const variantsGetLastErrorMock = vi.hoisted(() => vi.fn())
const showSuccessMock = vi.hoisted(() => vi.fn())
const showErrorMock = vi.hoisted(() => vi.fn())
const confirmMock = vi.hoisted(() => vi.fn().mockResolvedValue(true))
const productAffiliateSettingsCardMock = vi.hoisted(() => vi.fn(() => <div>Affiliate Marketing Card</div>))

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
        t: (key: string, fallback?: string) => {
            const labels: Record<string, string> = {}

            return labels[key] || fallback || key
        },
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
            hasPendingChanges: variantsHasPendingChangesMock,
            prepareProductAuthoringDraft: variantsPrepareDraftMock,
            getLastError: variantsGetLastErrorMock,
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

function mockVariantProductReads(variants = [{
    id: 'variant-1',
    sku: 'VAR-1',
    barcode: 'BAR-VAR-1',
    price: 10,
    onlinePrice: 10,
    costPrice: 5,
    isDefault: true,
    isActive: true,
    attributes: [{ name: 'Color', value: 'Red' }, { name: 'Size', value: 'M' }],
}]) {
    apiGetMock.mockImplementation((url: string) => {
        if (url === '/categories') return Promise.resolve({ data: [{ id: 'category-1', name: 'Apparel' }] })
        if (url === '/units') return Promise.resolve({ data: [{ id: 'unit-piece', name: 'Piece', symbol: 'pc' }] })
        if (url === '/admin/store-config') return Promise.resolve({ data: {} })
        if (url === '/products/product-1') {
            return Promise.resolve({
                data: {
                    name: 'Variant Product',
                    categoryId: 'category-1',
                    taxRate: 10,
                    priceIncludesTax: false,
                    trackStock: false,
                    isActive: true,
                    hasVariants: true,
                    publishStatus: 1,
                    isOnlineVisible: true,
                    isSearchable: true,
                    isFeatured: false,
                    rowVersion: '7',
                    variants,
                    units: [],
                },
            })
        }

        return Promise.resolve({ data: [] })
    })
}

describe('product affiliate entry presentation', () => {
    beforeEach(() => {
        apiGetMock.mockReset()
        apiPostMock.mockReset()
        apiPostMock.mockResolvedValue({ data: { id: 'product-new' } })
        apiPutMock.mockReset()
        apiPutMock.mockResolvedValue({ data: { id: 'product-1', rowVersion: '8' } })
        variantsHasPendingChangesMock.mockReset()
        variantsHasPendingChangesMock.mockReturnValue(false)
        variantsPrepareDraftMock.mockReset()
        variantsPrepareDraftMock.mockResolvedValue({
            authoringVariants: [{
                variantId: 'variant-1',
                price: 12,
                onlinePrice: 12,
                costPrice: 5,
                isDefault: true,
                isActive: true,
                attributes: { Color: 'Red' },
            }],
        })
        variantsGetLastErrorMock.mockReset()
        variantsGetLastErrorMock.mockReturnValue('')
        showSuccessMock.mockClear()
        showErrorMock.mockClear()
        confirmMock.mockReset()
        confirmMock.mockResolvedValue(true)
        productAffiliateSettingsCardMock.mockClear()
        mockProductReads()
    })

    it('never renders the standalone affiliate settings component for any role', async () => {
        render(<ProductForm productId="product-1" mode="workspace" />)

        await waitFor(() => expect(apiGetMock).toHaveBeenCalledWith('/products/product-1'))
        await screen.findByText('Product media')

        expect(screen.queryByText('Affiliate Marketing Card')).not.toBeInTheDocument()
        expect(productAffiliateSettingsCardMock).not.toHaveBeenCalled()
    })

    it('performs only product-authoring calls on an ordinary update with no affiliate mutation', async () => {
        const { container } = render(<ProductForm productId="product-1" mode="modal" onSave={vi.fn()} />)

        await waitFor(() => expect(apiGetMock).toHaveBeenCalledWith('/products/product-1'))
        await waitFor(() => expect(apiGetMock).toHaveBeenCalledWith('/products/product-1/variants'))
        fireEvent.change(container.querySelector('input[type="text"]')!, {
            target: { value: 'Affiliate-free update' },
        })

        fireEvent.click(screen.getByRole('button', { name: 'products.update' }))

        await waitFor(() => {
            expect(apiPutMock).toHaveBeenCalledTimes(1)
            expect(apiPutMock).toHaveBeenCalledWith('/products/product-1', expect.objectContaining({
                name: 'Affiliate-free update',
            }), expect.anything())
        })

        expect(apiPostMock).not.toHaveBeenCalled()

        const allRequestUrls = [
            ...apiGetMock.mock.calls.map((call: unknown[]) => call[0]),
            ...apiPostMock.mock.calls.map((call: unknown[]) => call[0]),
            ...apiPutMock.mock.calls.map((call: unknown[]) => call[0]),
        ].filter((url): url is string => typeof url === 'string')

        expect(allRequestUrls.length).toBeGreaterThan(0)
        expect(allRequestUrls.filter((url) => `${url}`.includes('/affiliate'))).toEqual([])
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

        fireEvent.click(screen.getByRole('button', { name: 'products.addProductUnit' }))
        fireEvent.change(container.querySelectorAll('select')[3], {
            target: { value: 'unit-piece' },
        })
        fireEvent.change(container.querySelectorAll('input[type="number"]')[3], {
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

        const payload = apiPostMock.mock.calls[0][1]
        expect(payload).not.toHaveProperty('purchasePrice')
        expect(payload).not.toHaveProperty('salePrice')
        // WAVE 3: simple products carry an explicit null storefront price
        // (null = storefront uses the POS price).
        expect(payload.onlinePrice).toBeNull()
    })

    it('separates variant create from product unit pricing and creates the parent as draft', async () => {
        const onSave = vi.fn()
        const { container } = render(<ProductForm mode="modal" onSave={onSave} />)

        await screen.findByText('products.basicInfo')
        fireEvent.click(screen.getByLabelText(/products.variantProduct/))

        expect(screen.queryByText('products.productUnits')).not.toBeInTheDocument()
        expect(screen.queryByText('products.purchasePrice')).not.toBeInTheDocument()
        expect(screen.queryByText('products.sellingPrice')).not.toBeInTheDocument()
        expect(screen.getByText('products.variantCreatePricingDeferred')).toBeInTheDocument()

        fireEvent.change(container.querySelector('input[type="text"]')!, {
            target: { value: 'Variant Shirt' },
        })
        fireEvent.change(container.querySelector('select')!, {
            target: { value: 'category-1' },
        })

        fireEvent.click(screen.getByRole('button', { name: 'products.create' }))

        await waitFor(() => {
            expect(apiPostMock).toHaveBeenCalledWith('/products', expect.objectContaining({
                name: 'Variant Shirt',
                categoryId: 'category-1',
                hasVariants: true,
                sku: null,
                barcode: null,
                publishStatus: 0,
                productUnits: null,
                variants: [],
            }), expect.objectContaining({
                headers: expect.objectContaining({
                    'Idempotency-Key': expect.stringContaining('product-authoring:create:'),
                }),
            }))
        })

        expect(screen.queryByText('products.activePricedVariantRequired')).not.toBeInTheDocument()
        expect(onSave).toHaveBeenCalledWith(expect.objectContaining({
            id: 'product-new',
            hasVariants: true,
        }))
    })

    it('hides Product Units for existing variant products', async () => {
        mockVariantProductReads()

        render(<ProductForm productId="product-1" mode="modal" onSave={vi.fn()} />)

        await waitFor(() => expect(apiGetMock).toHaveBeenCalledWith('/products/product-1'))
        await screen.findByText('Product variants')

        expect(screen.queryByText('products.productUnits')).not.toBeInTheDocument()
        expect(screen.queryByText('products.productUnitsValid')).not.toBeInTheDocument()
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
        fireEvent.click(screen.getByRole('button', { name: 'products.addProductUnit' }))
        fireEvent.change(container.querySelectorAll('select')[3], {
            target: { value: 'unit-piece' },
        })
        fireEvent.change(container.querySelectorAll('input[type="number"]')[3], {
            target: { value: '12' },
        })

        fireEvent.click(screen.getByRole('button', { name: 'products.create' }))
        await waitFor(() => expect(apiPostMock).toHaveBeenCalledTimes(1))
        const firstKey = apiPostMock.mock.calls[0][2].headers['Idempotency-Key']

        fireEvent.click(screen.getByRole('button', { name: 'products.create' }))
        await waitFor(() => expect(apiPostMock).toHaveBeenCalledTimes(2))

        expect(apiPostMock.mock.calls[1][2].headers['Idempotency-Key']).toBe(firstKey)
        const payload = apiPostMock.mock.calls[1][1]
        expect(payload).not.toHaveProperty('purchasePrice')
        expect(payload).not.toHaveProperty('salePrice')
        expect(payload.onlinePrice).toBeNull()
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
        const payload = apiPutMock.mock.calls[0][1]
        expect(payload).not.toHaveProperty('purchasePrice')
        expect(payload).not.toHaveProperty('salePrice')
        // WAVE 3: the loaded default-variant onlinePrice (12) round-trips.
        expect(payload.onlinePrice).toBe(12)
    })

    it('skips product authoring PUT when an existing product is submitted without changes', async () => {
        render(<ProductForm productId="product-1" mode="modal" onSave={vi.fn()} />)

        await waitFor(() => expect(apiGetMock).toHaveBeenCalledWith('/products/product-1'))
        await waitFor(() => expect(apiGetMock).toHaveBeenCalledWith('/products/product-1/variants'))

        fireEvent.click(screen.getByRole('button', { name: 'products.update' }))

        await waitFor(() => expect(showSuccessMock).toHaveBeenCalledWith('No changes to save.'))
        expect(apiPutMock).not.toHaveBeenCalled()
        expect(apiPostMock).not.toHaveBeenCalled()
    })

    it('executes product authoring PUT when product unit values change', async () => {
        const { container } = render(<ProductForm productId="product-1" mode="modal" onSave={vi.fn()} />)

        await waitFor(() => expect(apiGetMock).toHaveBeenCalledWith('/products/product-1'))
        await waitFor(() => expect(apiGetMock).toHaveBeenCalledWith('/products/product-1/variants'))

        fireEvent.change(container.querySelectorAll('input[type="number"]')[3], {
            target: { value: '13' },
        })
        fireEvent.click(screen.getByRole('button', { name: 'products.update' }))

        await waitFor(() => {
            expect(apiPutMock).toHaveBeenCalledWith('/products/product-1', expect.objectContaining({
                productUnits: [expect.objectContaining({
                    unitId: 'unit-piece',
                    sellingPrice: 13,
                })],
            }), expect.anything())
        })
    })

    it('keeps Arabic identity optional and reveals it on demand', async () => {
        render(<ProductForm mode="modal" onSave={vi.fn()} />)

        await screen.findByText('products.basicInfo')
        expect(screen.queryByLabelText('products.productNameAr')).not.toBeInTheDocument()
        expect(screen.getByRole('button', { name: '+ products.addArabicTranslation' })).toBeInTheDocument()

        fireEvent.click(screen.getByRole('button', { name: '+ products.addArabicTranslation' }))

        expect(screen.getByLabelText('products.productNameAr')).toBeInTheDocument()
        expect(screen.getByText('products.arabicFallbackHint')).toBeInTheDocument()
    })

    it('suggests and sanitizes a create slug but submits null after the user clears it', async () => {
        const { container } = render(<ProductForm mode="modal" onSave={vi.fn()} />)

        await screen.findByText('products.basicInfo')
        fireEvent.change(screen.getByLabelText('products.productName *'), {
            target: { value: 'Blue Widget XL!' },
        })
        const slugInput = screen.getByPlaceholderText('product-url-slug')
        expect(slugInput).toHaveValue('blue-widget-xl')

        fireEvent.change(slugInput, { target: { value: '' } })
        fireEvent.change(container.querySelector('select')!, { target: { value: 'category-1' } })
        const publishStatusSelect = Array.from(container.querySelectorAll('select'))
            .find((select) => Array.from(select.options).some((option) => option.textContent === 'products.statusDraft'))!
        fireEvent.change(publishStatusSelect, { target: { value: '0' } })
        fireEvent.click(screen.getByRole('button', { name: 'products.create' }))

        await waitFor(() => expect(apiPostMock).toHaveBeenCalledWith(
            '/products',
            expect.objectContaining({ name: 'Blue Widget XL!', slug: null }),
            expect.anything(),
        ))
    })

    it('skips product authoring PUT for whitespace-only differences', async () => {
        const { container } = render(<ProductForm productId="product-1" mode="modal" onSave={vi.fn()} />)

        await waitFor(() => expect(apiGetMock).toHaveBeenCalledWith('/products/product-1'))
        await waitFor(() => expect(apiGetMock).toHaveBeenCalledWith('/products/product-1/variants'))

        fireEvent.change(container.querySelector('input[type="text"]')!, {
            target: { value: '  Test   Product  ' },
        })
        fireEvent.click(screen.getByRole('button', { name: 'products.update' }))

        await waitFor(() => expect(showSuccessMock).toHaveBeenCalledWith('No changes to save.'))
        expect(apiPutMock).not.toHaveBeenCalled()
    })

    it('skips product authoring PUT for null versus empty optional fields', async () => {
        apiGetMock.mockImplementation((url: string) => {
            if (url === '/categories') return Promise.resolve({ data: [{ id: 'category-1', name: 'Apparel' }] })
            if (url === '/units') return Promise.resolve({ data: [{ id: 'unit-piece', name: 'Piece', symbol: 'pc' }] })
            if (url === '/admin/store-config') return Promise.resolve({ data: {} })
            if (url === '/products/product-1') {
                return Promise.resolve({
                    data: {
                        name: 'Test Product',
                        nameAr: null,
                        categoryId: 'category-1',
                        description: null,
                        descriptionAr: null,
                        taxRate: 10,
                        priceIncludesTax: false,
                        trackStock: true,
                        isActive: true,
                        slug: null,
                        metaTitle: null,
                        metaDescription: null,
                        metaKeywords: null,
                        weight: null,
                        length: null,
                        width: null,
                        height: null,
                        lowStockThreshold: null,
                        brand: null,
                        manufacturer: null,
                        tags: null,
                        videoUrl: null,
                        hasVariants: false,
                        rowVersion: '7',
                        variants: [],
                        units: [{
                            id: 'product-unit-1',
                            unitId: 'unit-piece',
                            conversionFactor: 1,
                            purchasePrice: null,
                            sellingPrice: 12,
                            barcode: null,
                            isBaseUnit: true,
                            isDefault: true,
                        }],
                    },
                })
            }
            if (url === '/products/product-1/variants') {
                return Promise.resolve({ data: [{ id: 'variant-default', isDefault: true, sku: '', barcode: '' }] })
            }

            return Promise.resolve({ data: {} })
        })

        render(<ProductForm productId="product-1" mode="modal" onSave={vi.fn()} />)

        await waitFor(() => expect(apiGetMock).toHaveBeenCalledWith('/products/product-1'))
        await waitFor(() => expect(apiGetMock).toHaveBeenCalledWith('/products/product-1/variants'))
        fireEvent.click(screen.getByRole('button', { name: 'products.update' }))

        await waitFor(() => expect(showSuccessMock).toHaveBeenCalledWith('No changes to save.'))
        expect(apiPutMock).not.toHaveBeenCalled()
    })

    it('executes product authoring PUT when variant price changes', async () => {
        variantsHasPendingChangesMock.mockReturnValue(true)
        variantsPrepareDraftMock.mockResolvedValue({
            authoringVariants: [{
                variantId: 'variant-1',
                sku: 'VAR-1',
                barcode: 'BAR-VAR-1',
                price: 14,
                onlinePrice: 10,
                costPrice: 5,
                isDefault: true,
                isActive: true,
                attributes: { Color: 'Red', Size: 'M' },
            }],
        })
        mockVariantProductReads()

        render(<ProductForm productId="product-1" mode="modal" onSave={vi.fn()} />)

        await waitFor(() => expect(apiGetMock).toHaveBeenCalledWith('/products/product-1'))
        await screen.findByText('Product variants')
        fireEvent.click(screen.getByRole('button', { name: 'products.update' }))

        await waitFor(() => {
            expect(apiPutMock).toHaveBeenCalledWith('/products/product-1', expect.objectContaining({
                variants: [expect.objectContaining({
                    variantId: 'variant-1',
                    price: 14,
                })],
            }), expect.anything())
        })
    })

    it('skips product authoring PUT for equivalent ordering-only variant drafts', async () => {
        variantsHasPendingChangesMock.mockReturnValue(true)
        variantsPrepareDraftMock.mockResolvedValue({
            authoringVariants: [{
                variantId: 'variant-1',
                sku: 'VAR-1',
                barcode: 'BAR-VAR-1',
                price: 10,
                onlinePrice: 10,
                costPrice: 5,
                isDefault: true,
                isActive: true,
                attributes: { Size: 'M', Color: 'Red' },
            }],
        })
        mockVariantProductReads()

        render(<ProductForm productId="product-1" mode="modal" onSave={vi.fn()} />)

        await waitFor(() => expect(apiGetMock).toHaveBeenCalledWith('/products/product-1'))
        await screen.findByText('Product variants')
        fireEvent.click(screen.getByRole('button', { name: 'products.update' }))

        await waitFor(() => expect(showSuccessMock).toHaveBeenCalledWith('No changes to save.'))
        expect(apiPutMock).not.toHaveBeenCalled()
    })

    it('surfaces nested product authoring concurrency conflict details', async () => {
        apiPutMock.mockRejectedValueOnce({
            response: {
                status: 409,
                data: {
                    code: 'CONFLICT',
                    message: 'Product authoring concurrency conflict.',
                    data: {
                        status: 'ConcurrencyConflict',
                        errors: [
                            {
                                field: 'expectedVersion',
                                message: 'Product has been changed by another transaction.',
                            },
                        ],
                    },
                },
            },
        })
        const { container } = render(<ProductForm productId="product-1" mode="modal" onSave={vi.fn()} />)

        await waitFor(() => expect(apiGetMock).toHaveBeenCalledWith('/products/product-1'))
        await waitFor(() => expect(apiGetMock).toHaveBeenCalledWith('/products/product-1/variants'))
        fireEvent.change(container.querySelector('input[type="text"]')!, {
            target: { value: 'Updated Product' },
        })

        fireEvent.click(screen.getByRole('button', { name: 'products.update' }))

        expect(await screen.findByText('This product changed while you were editing it. Refresh the product before retrying.')).toBeInTheDocument()
    })

    it.each([
        ['PRODUCT_SKU_CONFLICT', 'variants.sku', 'SKU is already assigned to another product variant.'],
        ['PRODUCT_BARCODE_CONFLICT', 'variants.barcode', 'Barcode is already assigned to another product variant.'],
        ['PRODUCT_ATTRIBUTE_CONFLICT', 'variants.attributes', 'A product variant with the same attribute combination already exists.'],
    ])('surfaces the precise server message for %s (Wave 4)', async (code, field, serverMessage) => {
        apiPutMock.mockRejectedValueOnce({
            response: {
                status: 409,
                data: {
                    message: 'Product authoring identity conflict.',
                    status: 'Conflict',
                    errors: [{ code, field, message: serverMessage }],
                },
            },
        })
        const { container } = render(<ProductForm productId="product-1" mode="modal" onSave={vi.fn()} />)

        await waitFor(() => expect(apiGetMock).toHaveBeenCalledWith('/products/product-1'))
        await waitFor(() => expect(apiGetMock).toHaveBeenCalledWith('/products/product-1/variants'))
        fireEvent.change(container.querySelector('input[type="text"]')!, {
            target: { value: 'Updated Product' },
        })

        fireEvent.click(screen.getByRole('button', { name: 'products.update' }))

        expect(await screen.findByText(serverMessage)).toBeInTheDocument()
        expect(screen.queryByText('This product changed while you were editing it. Refresh the product before retrying.')).toBeNull()
    })

    it('generates English simple SKUs as before and Arabic simple SKUs from unique draft identities', async () => {
        const first = render(<ProductForm mode="modal" />)
        await screen.findByText('products.basicInfo')
        fireEvent.change(screen.getByLabelText('products.productName *'), { target: { value: 'Blue Widget' } })
        fireEvent.click(screen.getByRole('button', { name: 'Generate SKU' }))
        const skuInput = first.container.querySelector('input[id$="-sku"]') as HTMLInputElement
        expect(skuInput.value).toBe('BLUE-WIDGET')
        fireEvent.change(screen.getByLabelText('products.productName *'), { target: { value: 'حاسوب Pro 16' } })
        fireEvent.click(screen.getByRole('button', { name: 'Generate SKU' }))
        const firstArabicSku = skuInput.value
        expect(firstArabicSku).toMatch(/^PX-[A-F0-9]{32}$/)
        first.unmount()

        const second = render(<ProductForm mode="modal" />)
        await screen.findByText('products.basicInfo')
        fireEvent.change(screen.getByLabelText('products.productName *'), { target: { value: 'حاسوب Pro 16' } })
        fireEvent.click(screen.getByRole('button', { name: 'Generate SKU' }))
        expect((second.container.querySelector('input[id$="-sku"]') as HTMLInputElement).value).not.toBe(firstArabicSku)
    })

    it('retries an authoritative SKU collision using a new idempotency key and candidate', async () => {
        apiPostMock.mockReset()
            .mockRejectedValueOnce({ response: { status: 409, data: { errors: [{ code: 'PRODUCT_SKU_CONFLICT' }] } } })
            .mockResolvedValueOnce({ data: { id: 'product-new' } })
        const { container } = render(<ProductForm mode="modal" />)
        await screen.findByText('products.basicInfo')
        fireEvent.change(screen.getByLabelText('products.productName *'), { target: { value: 'منتج' } })
        fireEvent.click(screen.getByRole('button', { name: 'Generate SKU' }))
        fireEvent.change(container.querySelector('select')!, { target: { value: 'category-1' } })
        const publishStatusSelect = Array.from(container.querySelectorAll('select'))
            .find(select => Array.from(select.options).some(option => option.textContent === 'products.statusDraft'))!
        fireEvent.change(publishStatusSelect, { target: { value: '0' } })
        fireEvent.click(screen.getByRole('button', { name: 'products.create' }))
        await waitFor(() => expect(apiPostMock).toHaveBeenCalledTimes(2))
        const [first, second] = apiPostMock.mock.calls
        expect(first[1].sku).toMatch(/^PX-[A-F0-9]{32}$/)
        expect(second[1].sku).toMatch(/^PX-[A-F0-9]{32}$/)
        expect(first[1].sku).not.toBe(second[1].sku)
        expect(first[2].headers['Idempotency-Key']).not.toBe(second[2].headers['Idempotency-Key'])
        expect((container.querySelector('input[id$="-sku"]') as HTMLInputElement).value).toBe(second[1].sku)
    })

    it('surfaces post-matrix variant validation at the parent form and prevents the parent update', async () => {
        variantsHasPendingChangesMock.mockReturnValue(true)
        variantsPrepareDraftMock.mockResolvedValue({
            authoringVariants: [{
                variantId: 'variant-1',
                price: 0,
                costPrice: 5,
                isDefault: true,
                isActive: true,
                attributes: { Color: 'Red' },
            }],
        })
        apiGetMock.mockImplementation((url: string) => {
            if (url === '/categories') return Promise.resolve({ data: [{ id: 'category-1', name: 'Apparel' }] })
            if (url === '/units') return Promise.resolve({ data: [{ id: 'unit-piece', name: 'Piece', symbol: 'pc' }] })
            if (url === '/admin/store-config') return Promise.resolve({ data: {} })
            if (url === '/products/product-1') {
                return Promise.resolve({
                    data: {
                        name: 'Variant Product',
                        categoryId: 'category-1',
                        taxRate: 10,
                        priceIncludesTax: false,
                        trackStock: false,
                        isActive: true,
                        hasVariants: true,
                        publishStatus: 1,
                        isOnlineVisible: true,
                        isSearchable: true,
                        isFeatured: false,
                        rowVersion: '7',
                        variants: [{
                            id: 'variant-1',
                            price: 10,
                            costPrice: 5,
                            isDefault: true,
                            isActive: true,
                            attributes: [{ name: 'Color', value: 'Red' }],
                        }],
                        units: [],
                    },
                })
            }

            return Promise.resolve({ data: [] })
        })

        render(<ProductForm productId="product-1" mode="modal" onSave={vi.fn()} />)

        await waitFor(() => expect(apiGetMock).toHaveBeenCalledWith('/products/product-1'))
        await screen.findByText('Product variants')
        fireEvent.click(screen.getByRole('button', { name: 'products.update' }))

        await waitFor(() => expect(variantsPrepareDraftMock).toHaveBeenCalled())
        expect(await screen.findByText('At least one active priced variant is required before publication.')).toBeInTheDocument()
        expect(apiPutMock).not.toHaveBeenCalled()
    })

    it('owns the product update when saving a pending variant draft', async () => {
        const onSave = vi.fn()
        variantsHasPendingChangesMock.mockReturnValue(true)
        variantsPrepareDraftMock.mockResolvedValue({
            authoringVariants: [{
                variantId: 'variant-1',
                price: 14,
                costPrice: 6,
                isDefault: true,
                isActive: true,
                attributes: { Color: 'Blue' },
            }],
        })
        apiGetMock.mockImplementation((url: string) => {
            if (url === '/categories') return Promise.resolve({ data: [{ id: 'category-1', name: 'Apparel' }] })
            if (url === '/units') return Promise.resolve({ data: [{ id: 'unit-piece', name: 'Piece', symbol: 'pc' }] })
            if (url === '/admin/store-config') return Promise.resolve({ data: {} })
            if (url === '/products/product-1') {
                return Promise.resolve({
                    data: {
                        name: 'Variant Product',
                        categoryId: 'category-1',
                        taxRate: 10,
                        priceIncludesTax: false,
                        trackStock: false,
                        isActive: true,
                        hasVariants: true,
                        publishStatus: 1,
                        isOnlineVisible: true,
                        isSearchable: true,
                        isFeatured: false,
                        rowVersion: '7',
                        variants: [{
                            id: 'variant-1',
                            price: 10,
                            costPrice: 5,
                            isDefault: true,
                            isActive: true,
                            attributes: [{ name: 'Color', value: 'Red' }],
                        }],
                        units: [{
                            id: 'legacy-unit-1',
                            unitId: 'unit-piece',
                            conversionFactor: 1,
                            sellingPrice: 10,
                            isBaseUnit: true,
                            isDefault: true,
                        }],
                    },
                })
            }

            return Promise.resolve({ data: [] })
        })

        render(<ProductForm productId="product-1" mode="modal" onSave={onSave} />)

        await waitFor(() => expect(apiGetMock).toHaveBeenCalledWith('/products/product-1'))
        await screen.findByText('Product variants')
        fireEvent.click(screen.getByRole('button', { name: 'products.update' }))

        await waitFor(() => {
            expect(variantsPrepareDraftMock).toHaveBeenCalled()
            expect(apiPutMock).toHaveBeenCalledWith('/products/product-1', expect.objectContaining({
                variants: [expect.objectContaining({
                    variantId: 'variant-1',
                    price: 14,
                    attributes: { Color: 'Blue' },
                })],
            }), expect.objectContaining({
                headers: expect.objectContaining({
                    'Idempotency-Key': expect.stringContaining('product-authoring:product-1:'),
                    'If-Match': '"7"',
                }),
            }))
            expect(onSave).toHaveBeenCalledWith(expect.objectContaining({
                id: 'product-1',
                rowVersion: '8',
                hasVariants: true,
            }))
        })
    })

    it('locks duplicate submits synchronously and sends one semantic update', async () => {
        let resolveUpdate: ((value: unknown) => void) | undefined
        apiPutMock.mockImplementationOnce(() => new Promise((resolve) => {
            resolveUpdate = resolve
        }))
        const { container } = render(<ProductForm productId="product-1" mode="modal" onSave={vi.fn()} />)

        await waitFor(() => expect(apiGetMock).toHaveBeenCalledWith('/products/product-1'))
        fireEvent.change(container.querySelector('input[type="text"]')!, {
            target: { value: 'One update only' },
        })

        const updateButton = screen.getByRole('button', { name: 'products.update' })
        fireEvent.click(updateButton)
        fireEvent.click(updateButton)

        await waitFor(() => expect(apiPutMock).toHaveBeenCalledTimes(1))
        resolveUpdate?.({ data: { id: 'product-1', rowVersion: '8' } })
        await waitFor(() => expect(showSuccessMock).toHaveBeenCalled())
    })

    it('treats core product edits as unsaved changes and uses the existing discard confirmation', async () => {
        const ref = createRef<{ hasPendingChanges: () => boolean; confirmDiscardPendingChanges: () => Promise<boolean> }>()
        render(<ProductForm ref={ref} productId="product-1" mode="modal" onSave={vi.fn()} />)

        const nameInput = await screen.findByLabelText('products.productName *')
        await waitFor(() => expect((nameInput as HTMLInputElement).value).toBe('Test Product'))
        expect(ref.current?.hasPendingChanges()).toBe(false)

        fireEvent.change(nameInput, { target: { value: 'Unsaved product name' } })
        expect(ref.current?.hasPendingChanges()).toBe(true)
        await expect(ref.current?.confirmDiscardPendingChanges()).resolves.toBe(true)
        expect(confirmMock).toHaveBeenCalledTimes(1)
    })

    it('reinitializes all identity and draft state when the route changes products or returns to create', async () => {
        apiGetMock.mockImplementation((url: string) => {
            if (url === '/categories') return Promise.resolve({ data: [{ id: 'category-1', name: 'Apparel' }] })
            if (url === '/units') return Promise.resolve({ data: [{ id: 'unit-piece', name: 'Piece', symbol: 'pc' }] })
            if (url === '/admin/store-config') return Promise.resolve({ data: {} })
            if (url.endsWith('/variants')) return Promise.resolve({ data: [] })
            if (url === '/products/product-1' || url === '/products/product-2') {
                const suffix = url.endsWith('product-1') ? 'A' : 'B'
                return Promise.resolve({
                    data: {
                        name: `Product ${suffix}`,
                        categoryId: 'category-1',
                        taxRate: 10,
                        priceIncludesTax: false,
                        trackStock: true,
                        isActive: true,
                        hasVariants: false,
                        rowVersion: suffix === 'A' ? '7' : '11',
                        variants: [],
                        units: [],
                    },
                })
            }
            return Promise.resolve({ data: {} })
        })

        const onSave = vi.fn()
        const { rerender } = render(<ProductForm productId="product-1" mode="modal" onSave={onSave} />)
        expect(await screen.findByDisplayValue('Product A')).toBeInTheDocument()

        rerender(<ProductForm productId="product-2" mode="modal" onSave={onSave} />)
        expect(await screen.findByDisplayValue('Product B')).toBeInTheDocument()
        expect(screen.queryByDisplayValue('Product A')).not.toBeInTheDocument()

        rerender(<ProductForm mode="modal" onSave={onSave} />)
        await waitFor(() => expect(screen.getByLabelText('products.productName *')).toHaveValue(''))
        expect(screen.queryByDisplayValue('Product B')).not.toBeInTheDocument()
    })

    it('ignores a completed save response after the form route changes products', async () => {
        let resolveProductOneUpdate: ((value: unknown) => void) | undefined
        apiPutMock.mockImplementationOnce(() => new Promise((resolve) => {
            resolveProductOneUpdate = resolve
        }))
        apiGetMock.mockImplementation((url: string) => {
            if (url === '/categories') return Promise.resolve({ data: [{ id: 'category-1', name: 'Apparel' }] })
            if (url === '/units') return Promise.resolve({ data: [{ id: 'unit-piece', name: 'Piece', symbol: 'pc' }] })
            if (url === '/admin/store-config') return Promise.resolve({ data: {} })
            if (url.endsWith('/variants')) return Promise.resolve({ data: [] })
            if (url === '/products/product-1' || url === '/products/product-2') {
                const isProductOne = url.endsWith('product-1')
                return Promise.resolve({
                    data: {
                        name: isProductOne ? 'Product A' : 'Product B',
                        categoryId: 'category-1',
                        taxRate: 10,
                        priceIncludesTax: false,
                        trackStock: true,
                        isActive: true,
                        hasVariants: false,
                        rowVersion: isProductOne ? '7' : '11',
                        variants: [],
                        units: [],
                    },
                })
            }
            return Promise.resolve({ data: {} })
        })

        const onSave = vi.fn()
        const { rerender } = render(<ProductForm productId="product-1" mode="modal" onSave={onSave} />)
        const nameInput = await screen.findByDisplayValue('Product A')
        fireEvent.change(nameInput, { target: { value: 'Product A updated' } })
        fireEvent.click(screen.getByRole('button', { name: 'products.update' }))
        await waitFor(() => expect(apiPutMock).toHaveBeenCalledTimes(1))

        rerender(<ProductForm productId="product-2" mode="modal" onSave={onSave} />)
        expect(await screen.findByDisplayValue('Product B')).toBeInTheDocument()

        resolveProductOneUpdate?.({ data: { id: 'product-1', rowVersion: '8' } })

        await waitFor(() => expect(screen.getByDisplayValue('Product B')).toBeInTheDocument())
        expect(onSave).not.toHaveBeenCalled()
        expect(showSuccessMock).not.toHaveBeenCalled()
        expect(screen.queryByDisplayValue('Product A updated')).not.toBeInTheDocument()
    })

    it('preserves product template and AI metadata independently of online visibility', async () => {
        apiGetMock.mockImplementation((url: string) => {
            if (url === '/categories') return Promise.resolve({ data: [{ id: 'category-1', name: 'Apparel' }] })
            if (url === '/units') return Promise.resolve({ data: [{ id: 'unit-piece', name: 'Piece', symbol: 'pc' }] })
            if (url === '/admin/store-config') return Promise.resolve({ data: {} })
            if (url === '/products/product-1/variants') return Promise.resolve({ data: [] })
            if (url === '/products/product-1') {
                return Promise.resolve({
                    data: {
                        name: 'AI Product',
                        categoryId: 'category-1',
                        taxRate: 10,
                        priceIncludesTax: false,
                        trackStock: true,
                        isActive: true,
                        hasVariants: false,
                        publishStatus: 0,
                        isOnlineVisible: true,
                        productTemplate: 'editorial',
                        aiGenerated: true,
                        aiMetadata: { source: 'admin_ai_product_generator', score: 9 },
                        rowVersion: '7',
                        variants: [],
                        units: [],
                    },
                })
            }
            return Promise.resolve({ data: {} })
        })

        render(<ProductForm productId="product-1" mode="modal" onSave={vi.fn()} />)
        await screen.findByDisplayValue('AI Product')
        fireEvent.click(screen.getByLabelText('products.isOnlineVisible'))
        fireEvent.change(screen.getByLabelText('products.productName *'), { target: { value: 'AI Product Updated' } })
        fireEvent.click(screen.getByRole('button', { name: 'products.update' }))

        await waitFor(() => expect(apiPutMock).toHaveBeenCalledWith(
            '/products/product-1',
            expect.objectContaining({
                isOnlineVisible: false,
                productTemplate: 'editorial',
                aiMetadata: { source: 'admin_ai_product_generator', score: 9 },
            }),
            expect.anything(),
        ))
    })

    it('rehydrates and submits Arabic product content through the authoritative DTO fields', async () => {
        apiGetMock.mockImplementation((url: string) => {
            if (url === '/categories') return Promise.resolve({ data: [{ id: 'category-1', name: 'Apparel' }] })
            if (url === '/units') return Promise.resolve({ data: [{ id: 'unit-piece', name: 'Piece', symbol: 'pc' }] })
            if (url === '/admin/store-config') return Promise.resolve({ data: {} })
            if (url === '/products/product-1/variants') return Promise.resolve({ data: [] })
            if (url === '/products/product-1') {
                return Promise.resolve({
                    data: {
                        name: 'English name',
                        nameAr: 'اسم عربي',
                        categoryId: 'category-1',
                        description: 'English description',
                        descriptionAr: 'وصف عربي',
                        taxRate: 10,
                        priceIncludesTax: false,
                        trackStock: true,
                        isActive: true,
                        hasVariants: false,
                        rowVersion: '7',
                        variants: [],
                        units: [],
                    },
                })
            }
            return Promise.resolve({ data: {} })
        })

        render(<ProductForm productId="product-1" mode="modal" onSave={vi.fn()} />)

        const arabicName = await screen.findByLabelText('products.productNameAr')
        expect(arabicName).toHaveValue('اسم عربي')
        expect(screen.getByLabelText('products.descriptionAr')).toHaveValue('وصف عربي')
        fireEvent.change(arabicName, { target: { value: 'اسم عربي محدث' } })
        fireEvent.click(screen.getByRole('button', { name: 'products.update' }))

        await waitFor(() => expect(apiPutMock).toHaveBeenCalledWith(
            '/products/product-1',
            expect.objectContaining({
                nameAr: 'اسم عربي محدث',
                descriptionAr: 'وصف عربي',
            }),
            expect.anything(),
        ))
    })

    it('never renders raw backend diagnostics for authoring failures', async () => {
        apiPutMock.mockRejectedValueOnce({
            response: {
                status: 500,
                data: { message: 'SqlException SELECT * FROM Products tenant=secret' },
            },
        })
        render(<ProductForm productId="product-1" mode="modal" onSave={vi.fn()} />)

        const nameInput = await screen.findByLabelText('products.productName *')
        await waitFor(() => expect(nameInput).toHaveValue('Test Product'))
        fireEvent.change(nameInput, { target: { value: 'Safe failure' } })
        fireEvent.click(screen.getByRole('button', { name: 'products.update' }))

        const alert = await screen.findByRole('alert')
        expect(alert).toHaveTextContent('products.failedToSave')
        await waitFor(() => expect(alert).toHaveFocus())
        expect(document.body.textContent).not.toMatch(/SqlException|SELECT \*|tenant=secret/)
    })
})
