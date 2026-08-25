import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ProductVariantsTab from './ProductVariantsTab'

const apiGetMock = vi.hoisted(() => vi.fn())
const apiPostMock = vi.hoisted(() => vi.fn())
const apiPutMock = vi.hoisted(() => vi.fn())
const apiDeleteMock = vi.hoisted(() => vi.fn())
const saveVariantDraftMock = vi.hoisted(() => vi.fn())

vi.mock('../services/api', () => ({
    default: {
        get: apiGetMock,
        post: apiPostMock,
        put: apiPutMock,
        delete: apiDeleteMock,
    },
}))

vi.mock('../common/confirm/ConfirmDialogProvider', () => ({
    useConfirmDialog: () => ({ confirm: vi.fn().mockResolvedValue(true) }),
}))

vi.mock('../services/posRuntimeCache', () => ({
    invalidatePosRuntimeCache: vi.fn(),
}))

vi.mock('react-i18next', () => ({
    useTranslation: () => ({
        t: (key: string) => key,
    }),
}))

const product = {
    id: 'product-1',
    name: 'T Shirt',
    sku: 'TSHIRT',
    categoryId: 'category-1',
    purchasePrice: 0,
    salePrice: 0,
    priceIncludesTax: false,
    taxRate: 10,
    unit: 'Piece',
    minimumStockLevel: 0,
    isActive: true,
    trackStock: false,
    hasVariants: true,
    publishStatus: 0,
    isOnlineVisible: true,
    isSearchable: true,
    isFeatured: false,
    lowStockThreshold: 10,
    allowBackorder: false,
    weightUnit: 'kg',
    dimensionUnit: 'cm',
    features: [],
    faqs: [],
    rowVersion: '4',
    units: [],
}

function mockReads() {
    apiGetMock.mockImplementation((url: string) => {
        if (url === '/products/product-1/variants') return Promise.resolve({ data: [] })
        if (url === '/products/product-1') return Promise.resolve({ data: product })
        if (url === '/warehouses') return Promise.resolve({ data: [{ id: 'warehouse-1', name: 'Main', isDefault: true }] })
        if (url === '/admin/products/product-1/media') return Promise.resolve({ data: [] })
        return Promise.resolve({ data: [] })
    })
}

function renderVariantTab(props = {}) {
    return render(
        <ProductVariantsTab
            productId="product-1"
            forceMatrixMode
            productSkuPrefix="T Shirt"
            onSaveVariantDraft={saveVariantDraftMock}
            {...props}
        />
    )
}

async function createTwoGeneratedRows(container: HTMLElement) {
    fireEvent.click(await screen.findByLabelText('Color'))
    fireEvent.click(screen.getByLabelText('Size'))

    let valueInputs = Array.from(container.querySelectorAll('input[placeholder="Type a value and press Enter"]'))
    fireEvent.change(valueInputs[0], { target: { value: 'Red' } })
    fireEvent.keyDown(valueInputs[0], { key: 'Enter' })
    valueInputs = Array.from(container.querySelectorAll('input[placeholder="Type a value and press Enter"]'))
    fireEvent.change(valueInputs[0], { target: { value: 'Blue' } })
    fireEvent.keyDown(valueInputs[0], { key: 'Enter' })
    valueInputs = Array.from(container.querySelectorAll('input[placeholder="Type a value and press Enter"]'))
    fireEvent.change(valueInputs[1], { target: { value: 'S' } })
    fireEvent.keyDown(valueInputs[1], { key: 'Enter' })

    await screen.findByText('Red / S')
    await screen.findByText('Blue / S')
}

async function addOptionValue(container: HTMLElement, inputIndex: number, value: string) {
    const valueInputs = Array.from(container.querySelectorAll('input[placeholder="Type a value and press Enter"]')) as HTMLInputElement[]
    fireEvent.change(valueInputs[inputIndex], { target: { value } })
    fireEvent.keyDown(valueInputs[inputIndex], { key: 'Enter' })
}

function fillRequiredPrices(container: HTMLElement) {
    const numericInputs = Array.from(container.querySelectorAll('table input[type="number"]')) as HTMLInputElement[]
    fireEvent.change(numericInputs[0], { target: { value: '5' } })
    fireEvent.change(numericInputs[1], { target: { value: '10' } })
    fireEvent.change(numericInputs[4], { target: { value: '6' } })
    fireEvent.change(numericInputs[5], { target: { value: '12' } })
}

describe('variant authoring workspace', () => {
    beforeEach(() => {
        apiGetMock.mockReset()
        apiPostMock.mockReset()
        apiPutMock.mockReset()
        apiDeleteMock.mockReset()
        apiPutMock.mockResolvedValue({ data: { ...product, rowVersion: '5' } })
        saveVariantDraftMock.mockReset()
        saveVariantDraftMock.mockResolvedValue({ ...product, rowVersion: '5' })
        mockReads()
    })

    it('hands generated variant drafts to the parent product authoring owner', async () => {
        const { container } = renderVariantTab()

        await createTwoGeneratedRows(container)
        fillRequiredPrices(container)

        fireEvent.click(screen.getByRole('button', { name: 'Save Matrix' }))

        await waitFor(() => expect(saveVariantDraftMock).toHaveBeenCalledWith(
            expect.objectContaining({
                authoringVariants: [
                    expect.objectContaining({
                        sku: null,
                        costPrice: 5,
                        price: 10,
                        isDefault: true,
                        attributes: { Color: 'Red', Size: 'S' },
                    }),
                    expect.objectContaining({
                        costPrice: 6,
                        price: 12,
                        attributes: { Color: 'Blue', Size: 'S' },
                    }),
                ],
            })
        ))

        expect(apiPutMock).not.toHaveBeenCalledWith('/products/product-1', expect.anything(), expect.anything())
        expect(apiPostMock).not.toHaveBeenCalledWith(expect.stringContaining('/products/product-1/variants'), expect.anything())
        expect(apiDeleteMock).not.toHaveBeenCalled()
    })

    it('preserves matrix edits when the parent product save adapter fails', async () => {
        saveVariantDraftMock.mockRejectedValueOnce(new Error('At least one active priced variant is required before publication.'))

        const { container } = renderVariantTab()

        await createTwoGeneratedRows(container)
        fillRequiredPrices(container)

        fireEvent.click(screen.getByRole('button', { name: 'Save Matrix' }))

        await waitFor(() => expect(saveVariantDraftMock).toHaveBeenCalled())
        expect(apiPutMock).not.toHaveBeenCalledWith('/products/product-1', expect.anything(), expect.anything())

        expect(await screen.findByText('At least one active priced variant is required before publication.')).toBeInTheDocument()
        expect(screen.getByText('Red / S')).toBeInTheDocument()
        expect(screen.getByText('Blue / S')).toBeInTheDocument()
    })

    it('surfaces nested product authoring concurrency conflict details without clearing matrix rows', async () => {
        saveVariantDraftMock.mockRejectedValueOnce(new Error('Product has been changed by another transaction.'))

        const { container } = renderVariantTab()

        await createTwoGeneratedRows(container)
        fillRequiredPrices(container)

        fireEvent.click(screen.getByRole('button', { name: 'Save Matrix' }))

        expect(await screen.findByText('Product has been changed by another transaction.')).toBeInTheDocument()
        expect(screen.getByText('Red / S')).toBeInTheDocument()
        expect(screen.getByText('Blue / S')).toBeInTheDocument()
    })

    it('blocks duplicate SKU values before save', async () => {
        const { container } = renderVariantTab()

        await createTwoGeneratedRows(container)
        const textInputs = Array.from(container.querySelectorAll('table input[type="text"]')) as HTMLInputElement[]
        fireEvent.change(textInputs[0], { target: { value: 'DUP-SKU' } })
        fireEvent.change(textInputs[2], { target: { value: 'DUP-SKU' } })
        fillRequiredPrices(container)

        fireEvent.click(screen.getByRole('button', { name: 'Save Matrix' }))

        expect((await screen.findAllByText('Duplicate SKU values are not allowed.')).length).toBeGreaterThan(0)
        expect(apiPutMock).not.toHaveBeenCalled()
    })

    it('updates the live variant counter while editing attributes', async () => {
        const { container } = renderVariantTab()

        fireEvent.click(await screen.findByLabelText('Color'))
        fireEvent.click(screen.getByLabelText('Size'))
        await addOptionValue(container, 0, 'Red')
        await addOptionValue(container, 0, 'Blue')
        await addOptionValue(container, 1, 'S')
        await addOptionValue(container, 1, 'M')
        await addOptionValue(container, 1, 'L')

        expect(screen.getByText('Variants to Generate')).toBeInTheDocument()
        expect(screen.getByRole('button', { name: 'Generate 6 Variants' })).toBeInTheDocument()
        expect(await screen.findByText('Blue / L')).toBeInTheDocument()
    })

    it('shows a large variant warning above the safe threshold', async () => {
        const { container } = renderVariantTab()

        fireEvent.click(await screen.findByLabelText('Color'))
        fireEvent.click(screen.getByLabelText('Size'))
        for (let index = 1; index <= 101; index += 1) {
            await addOptionValue(container, 0, `Color ${index}`)
        }
        await addOptionValue(container, 1, 'M')

        expect(screen.getByText('Moderate matrix')).toBeInTheDocument()
        expect(screen.getByRole('button', { name: 'Generate 101 Variants' })).toBeInTheDocument()
    }, 10000)

    it('loads frontend-only variant templates', async () => {
        const { container } = renderVariantTab()

        fireEvent.click(await screen.findByRole('button', { name: 'Electronics' }))

        const optionNameInputs = Array.from(container.querySelectorAll('input[placeholder="e.g. Size, Color, Material"]')) as HTMLInputElement[]
        expect(optionNameInputs.map(input => input.value)).toEqual(['Color', 'Storage'])
    })

    it('detects duplicate attribute values and prevents generation', async () => {
        const { container } = renderVariantTab()

        fireEvent.click(await screen.findByLabelText('Color'))
        fireEvent.click(screen.getByLabelText('Size'))
        await addOptionValue(container, 0, 'Red')
        await addOptionValue(container, 0, 'Red')
        await addOptionValue(container, 1, 'M')

        expect(screen.getByText('Duplicate attribute values are not allowed.')).toBeInTheDocument()
        expect(screen.queryByText('Red / M')).not.toBeInTheDocument()
    })

    it('previews SKU patterns and applies generated SKUs to selected rows', async () => {
        const { container } = renderVariantTab()

        await createTwoGeneratedRows(container)
        fireEvent.change(screen.getByPlaceholderText('Pattern, e.g. TSHIRT-{COLOR}-{SIZE}'), {
            target: { value: 'TSHIRT-{COLOR}-{SIZE}' },
        })

        expect(screen.getByText('TSHIRT-RED-S')).toBeInTheDocument()

        const rowCheckboxes = Array.from(container.querySelectorAll('tbody input[type="checkbox"]')) as HTMLInputElement[]
        fireEvent.click(rowCheckboxes[0])
        fireEvent.click(screen.getByRole('button', { name: 'Generate SKU for 1 Selected' }))

        await waitFor(() => {
            const textInputs = Array.from(container.querySelectorAll('table input[type="text"]')) as HTMLInputElement[]
            expect(textInputs[0].value).toBe('TSHIRT-RED-S')
        })
    })

    it('excludes combinations before they appear in the matrix', async () => {
        const { container } = renderVariantTab()

        fireEvent.click(await screen.findByLabelText('Color'))
        fireEvent.click(screen.getByLabelText('Size'))
        await addOptionValue(container, 0, 'Red')
        await addOptionValue(container, 0, 'Blue')
        await addOptionValue(container, 1, 'S')
        await addOptionValue(container, 1, 'M')

        const selects = Array.from(container.querySelectorAll('select.input')) as HTMLSelectElement[]
        fireEvent.change(selects[2], { target: { value: 'Red' } })
        fireEvent.change(selects[3], { target: { value: 'M' } })
        fireEvent.click(screen.getByRole('button', { name: 'Add Exclusion' }))

        expect(screen.queryByText('Red / M')).not.toBeInTheDocument()
        expect(screen.getByRole('button', { name: 'Generate 3 Variants' })).toBeInTheDocument()
    })

    it('shows live statistics and smart selection captions', async () => {
        const { container } = renderVariantTab()

        await createTwoGeneratedRows(container)

        expect(screen.getByText('Workspace Statistics')).toBeInTheDocument()
        expect(screen.getByText('Missing SKU')).toBeInTheDocument()

        fireEvent.click(container.querySelector('thead input[type="checkbox"]') as HTMLInputElement)

        expect(screen.getByRole('button', { name: 'Delete 2 Selected' })).toBeInTheDocument()
        expect(screen.getByRole('button', { name: 'Enable 2 Selected' })).toBeInTheDocument()
    })

    it('removes direct inventory editing from variant authoring', async () => {
        const { container } = renderVariantTab()

        await createTwoGeneratedRows(container)

        expect(screen.queryByText('Inventory')).not.toBeInTheDocument()
        expect(screen.queryByText('Set Inventory')).not.toBeInTheDocument()
        expect(screen.getAllByText('Managed by inventory workflows').length).toBeGreaterThan(0)
        expect(screen.getByText('Available Stock')).toBeInTheDocument()
    })

    it('shows authoritative inactive state and preserves it during an unrelated pricing save', async () => {
        apiGetMock.mockImplementation((url: string) => {
            if (url === '/products/product-1/variants') {
                return Promise.resolve({
                    data: [{
                        id: 'variant-1',
                        productId: 'product-1',
                        sku: 'TSHIRT-RED',
                        barcode: '1001',
                        price: 400,
                        onlinePrice: null,
                        costPrice: 125.5,
                        weight: null,
                        isDefault: true,
                        isActive: false,
                        totalStock: 3,
                        attributes: [{ name: 'Color', value: 'Red', sortOrder: 0 }],
                    }],
                })
            }
            if (url === '/admin/products/product-1/media') return Promise.resolve({ data: [] })
            return Promise.resolve({ data: [] })
        })

        renderVariantTab()

        const status = await screen.findByLabelText('Red: Status — Inactive')
        expect(status).not.toBeChecked()
        expect(screen.getByText('Status: Inactive')).toBeInTheDocument()
        expect(screen.getByLabelText('Red: POS Price')).toHaveValue(400)
        expect(screen.getByLabelText('Red: Online Price')).toHaveValue(null)
        expect(screen.getByLabelText('Red: Cost Price')).toHaveValue(125.5)

        fireEvent.change(screen.getByLabelText('Red: POS Price'), { target: { value: '500.25' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save Matrix' }))

        await waitFor(() => expect(saveVariantDraftMock).toHaveBeenCalledWith(
            expect.objectContaining({
                authoringVariants: [expect.objectContaining({
                    variantId: 'variant-1',
                    price: 500.25,
                    onlinePrice: null,
                    costPrice: 125.5,
                    isActive: false,
                })],
            })
        ))
    })

    it('preserves zero separately from null and allows Online Price to satisfy pricing', async () => {
        apiGetMock.mockImplementation((url: string) => {
            if (url === '/products/product-1/variants') {
                return Promise.resolve({
                    data: [{
                        id: 'variant-zero',
                        productId: 'product-1',
                        sku: 'ZERO-ONLINE',
                        barcode: '',
                        price: 0,
                        onlinePrice: 12.75,
                        costPrice: 0,
                        isDefault: true,
                        isActive: true,
                        totalStock: 0,
                        attributes: [{ name: 'Plan', value: 'Online', sortOrder: 0 }],
                    }],
                })
            }
            if (url === '/admin/products/product-1/media') return Promise.resolve({ data: [] })
            return Promise.resolve({ data: [] })
        })

        renderVariantTab()

        expect(await screen.findByLabelText('Online: POS Price')).toHaveValue(0)
        expect(screen.getByLabelText('Online: Online Price')).toHaveValue(12.75)
        expect(screen.getByLabelText('Online: Cost Price')).toHaveValue(0)

        fireEvent.change(screen.getByLabelText('Online: Online Price'), { target: { value: '0' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save Matrix' }))

        await waitFor(() => expect(saveVariantDraftMock).toHaveBeenCalledWith(
            expect.objectContaining({
                authoringVariants: [expect.objectContaining({
                    price: 0,
                    onlinePrice: 0,
                    costPrice: 0,
                })],
            })
        ))
    })

    it('ignores a late variant response from the previously opened product', async () => {
        let resolveProductA: ((value: unknown) => void) | undefined
        const productAResponse = new Promise((resolve) => {
            resolveProductA = resolve
        })

        apiGetMock.mockImplementation((url: string) => {
            if (url === '/products/product-a/variants') return productAResponse
            if (url === '/products/product-b/variants') {
                return Promise.resolve({
                    data: [{
                        id: 'variant-b',
                        productId: 'product-b',
                        sku: 'PRODUCT-B',
                        price: 20,
                        onlinePrice: null,
                        costPrice: 10,
                        isDefault: true,
                        isActive: true,
                        attributes: [{ name: 'Product', value: 'B', sortOrder: 0 }],
                    }],
                })
            }
            if (url.includes('/media')) return Promise.resolve({ data: [] })
            return Promise.resolve({ data: [] })
        })

        const view = render(
            <ProductVariantsTab
                productId="product-a"
                forceMatrixMode
                onSaveVariantDraft={saveVariantDraftMock}
            />
        )
        view.rerender(
            <ProductVariantsTab
                productId="product-b"
                forceMatrixMode
                onSaveVariantDraft={saveVariantDraftMock}
            />
        )

        expect(await screen.findByLabelText('B: POS Price')).toHaveValue(20)

        resolveProductA?.({
            data: [{
                id: 'variant-a',
                productId: 'product-a',
                sku: 'PRODUCT-A',
                price: 30,
                onlinePrice: null,
                costPrice: 15,
                isDefault: true,
                isActive: false,
                attributes: [{ name: 'Product', value: 'A', sortOrder: 0 }],
            }],
        })

        await waitFor(() => expect(screen.queryByLabelText('A: POS Price')).not.toBeInTheDocument())
        expect(screen.getByLabelText('B: POS Price')).toHaveValue(20)
    })

    it('blocks negative weight before save', async () => {
        const { container } = renderVariantTab()

        await createTwoGeneratedRows(container)
        fillRequiredPrices(container)
        const numericInputs = Array.from(container.querySelectorAll('table input[type="number"]')) as HTMLInputElement[]
        fireEvent.change(numericInputs[3], { target: { value: '-0.5' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save Matrix' }))

        expect((await screen.findAllByText('Weight cannot be negative.')).length).toBeGreaterThan(0)
        expect(apiPutMock).not.toHaveBeenCalled()
    })

    it('registers an unsaved changes warning for refresh navigation', async () => {
        const { container } = renderVariantTab()

        await createTwoGeneratedRows(container)

        const beforeUnload = new Event('beforeunload', { cancelable: true })
        expect(window.dispatchEvent(beforeUnload)).toBe(false)
    })
})
