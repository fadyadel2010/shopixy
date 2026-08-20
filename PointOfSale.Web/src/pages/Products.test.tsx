import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import Products from './Products.jsx'

const apiGetMock = vi.hoisted(() => vi.fn())
const apiPostMock = vi.hoisted(() => vi.fn())
const invalidatePosRuntimeCacheMock = vi.hoisted(() => vi.fn())
const showSuccessMock = vi.hoisted(() => vi.fn())
const showErrorMock = vi.hoisted(() => vi.fn())
const navigateMock = vi.hoisted(() => vi.fn())
const confirmMock = vi.hoisted(() => vi.fn())
// Stable function identity is required: Products.jsx lists `t` in a
// useCallback dependency array, so a per-render t would loop fetches forever.
const mockT = vi.hoisted(() => (key: string, options?: Record<string, unknown>) => {
    if (!options || typeof options !== 'object') return key
    return Object.entries(options).reduce(
        (acc, [name, value]) => acc.split(`{{${name}}}`).join(String(value)),
        key,
    )
})

vi.mock('../services/api', () => ({
    default: { get: apiGetMock, post: apiPostMock },
}))

vi.mock('../services/posRuntimeCache', () => ({
    invalidatePosRuntimeCache: invalidatePosRuntimeCacheMock,
}))

vi.mock('../components/Toast', () => ({
    useToast: () => ({
        showSuccess: showSuccessMock,
        showError: showErrorMock,
    }),
}))

vi.mock('../common/confirm/ConfirmDialogProvider', () => ({
    useConfirmDialog: () => ({ confirm: confirmMock }),
}))

vi.mock('../contexts/SettingsContext', () => ({
    useSettings: () => ({
        settings: { companyName: 'Test Store' },
        formatCurrency: (value: number) => `$${value}`,
    }),
}))

vi.mock('../hooks/useRTL', () => ({
    useRTL: () => ({ isRTL: false, dir: 'ltr' }),
}))

vi.mock('react-router-dom', () => ({
    useNavigate: () => navigateMock,
}))

vi.mock('react-i18next', () => ({
    useTranslation: () => ({
        t: mockT,
    }),
}))

vi.mock('../components/ProductStockModal', () => ({
    default: ({ productId, productName }: { productId: string; productName: string }) => (
        <div data-testid="stock-modal">{productId}:{productName}</div>
    ),
}))

vi.mock('../components/BarcodeDisplay', () => ({
    default: () => <div data-testid="barcode-modal" />,
}))

vi.mock('../components/ValidationDeleteModal', () => ({
    default: ({ onDeleteSuccess, onDeactivateSuccess }: {
        onDeleteSuccess: () => void
        onDeactivateSuccess: () => void
    }) => (
        <div data-testid="delete-modal">
            <button type="button" onClick={onDeleteSuccess}>trigger-delete-success</button>
            <button type="button" onClick={onDeactivateSuccess}>trigger-deactivate-success</button>
        </div>
    ),
}))

// react-to-print integration double: captures the hook options of every
// render and snapshots the printable node at the moment printing is invoked
// (the page resets the PDF scope right after printing, so the dataset must
// be captured at call time, not asserted afterwards).
const printMock = vi.hoisted(() => ({
    latestOptions: null as Record<string, unknown> | null,
    printed: [] as Array<{ documentTitle: unknown; content: string }>,
}))

vi.mock('react-to-print', () => ({
    useReactToPrint: (options: Record<string, unknown>) => {
        printMock.latestOptions = options
        return () => {
            const contentRef = options.contentRef as { current: HTMLElement | null } | undefined
            printMock.printed.push({
                documentTitle: options.documentTitle,
                content: contentRef?.current?.textContent ?? '',
            })
        }
    },
}))

function makeProduct(overrides: Record<string, unknown>) {
    return {
        id: 'p-unknown',
        name: 'Unknown',
        categoryId: 'c1',
        categoryName: 'Cat',
        images: '',
        sku: '',
        barcode: '',
        salePrice: 10,
        isActive: true,
        isFeatured: false,
        isSearchable: true,
        allowBackorder: false,
        isOnlineVisible: false,
        slug: '',
        trackStock: true,
        hasVariants: false,
        lowStockThreshold: 10,
        minimumStockLevel: 0,
        publishStatus: 'Published',
        warehouseStocks: [],
        ...overrides,
    }
}

function defaultProducts() {
    return [
        makeProduct({
            id: 'p1', name: 'Alpha Tea', salePrice: 10, publishStatus: 'Published',
            warehouseStocks: [{ quantity: 50 }],
        }),
        makeProduct({
            id: 'p2', name: 'Beta Coffee', salePrice: 20,
            warehouseStocks: [{ quantity: 3 }],
        }),
        makeProduct({
            id: 'p3', name: 'Gamma Blend', salePrice: 30, hasVariants: true,
            warehouseStocks: [{ quantity: 20 }],
        }),
        makeProduct({
            id: 'p4', name: 'Delta Roast', salePrice: 40, trackStock: false,
        }),
        makeProduct({
            id: 'p5', name: 'Epsilon Odd', salePrice: 50, publishStatus: 'GarbageValue',
            warehouseStocks: [{ quantity: 100 }],
        }),
    ]
}

function mockProductsApi(options: {
    products?: unknown[]
    totalPages?: string
    totalCount?: string
} = {}) {
    const products = options.products ?? defaultProducts()
    apiGetMock.mockImplementation((url: string) => {
        if (url === '/categories') {
            return Promise.resolve({ data: [{ id: 'c1', name: 'Cat' }] })
        }
        if (url === '/products') {
            return Promise.resolve({
                data: products,
                headers: {
                    'x-total-count': options.totalCount ?? String(products.length),
                    'x-total-pages': options.totalPages ?? '1',
                },
            })
        }
        return Promise.resolve({ data: [] })
    })
}

function productCalls() {
    return apiGetMock.mock.calls.filter((call) => call[0] === '/products')
}

function lastProductsParams(): Record<string, unknown> {
    const calls = productCalls()
    return calls[calls.length - 1]?.[1]?.params ?? {}
}

async function renderLoaded(options: Parameters<typeof mockProductsApi>[0] = {}) {
    mockProductsApi(options)
    render(<Products />)
    if ((options.products ?? defaultProducts()).length > 0) {
        await waitFor(() => expect(screen.getAllByRole('checkbox').length).toBeGreaterThan(1))
    } else {
        await waitFor(() => expect(productCalls().length).toBeGreaterThan(0))
    }
}

function headerCheckbox() {
    return screen.getByRole('checkbox', { name: 'products.selectAllVisible' }) as HTMLInputElement
}

function rowCheckboxes() {
    return screen.getAllByRole('checkbox')
        .filter((cb) => (cb as HTMLElement).closest('tbody'))
}

function selectRowByName(name: string) {
    const row = screen.getByText(name).closest('tr')
    if (!row) throw new Error(`row not found for ${name}`)
    fireEvent.click(within(row).getAllByRole('checkbox')[0])
}

beforeEach(() => {
    vi.clearAllMocks()
    printMock.latestOptions = null
    printMock.printed = []
    apiPostMock.mockResolvedValue({ data: {} })
    confirmMock.mockResolvedValue(true)
})

// jsdom lacks the pointer-capture APIs Radix relies on for the export menu.
beforeAll(() => {
    if (typeof window.PointerEvent === 'undefined') {
        class PointerEventPolyfill extends MouseEvent {
            pointerId: number
            pointerType: string
            constructor(type: string, props: PointerEventInit = {}) {
                super(type, props)
                this.pointerId = props.pointerId ?? 0
                this.pointerType = props.pointerType ?? 'mouse'
            }
        }
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        ;(window as any).PointerEvent = PointerEventPolyfill
    }
    if (!Element.prototype.hasPointerCapture) {
        Element.prototype.hasPointerCapture = () => false
        Element.prototype.setPointerCapture = () => { /* noop */ }
        Element.prototype.releasePointerCapture = () => { /* noop */ }
    }
    if (!Element.prototype.scrollIntoView) {
        Element.prototype.scrollIntoView = () => { /* noop */ }
    }
})

describe('Products listing — stock presentation (F-6)', () => {
    it('shows quantity plus state badge for simple tracked products', async () => {
        await renderLoaded()
        const alphaRow = screen.getByText('Alpha Tea').closest('tr')!
        expect(within(alphaRow).getByText('50')).toBeTruthy()
        expect(within(alphaRow).getByText('products.inStock')).toBeTruthy()
        const betaRow = screen.getByText('Beta Coffee').closest('tr')!
        expect(within(betaRow).getByText('3')).toBeTruthy()
        expect(within(betaRow).getByText('products.lowStock')).toBeTruthy()
    })

    it('shows Managed by variants without a parent-level stock badge for variant products', async () => {
        await renderLoaded()
        const gammaRow = screen.getByText('Gamma Blend').closest('tr')!
        expect(within(gammaRow).getByText('products.managedByVariants')).toBeTruthy()
        expect(within(gammaRow).queryByText('products.inStock')).toBeNull()
        expect(within(gammaRow).queryByText('products.lowStock')).toBeNull()
        expect(within(gammaRow).queryByText('products.outOfStock')).toBeNull()
    })

    it('shows a non-actionable dash for untracked products', async () => {
        await renderLoaded()
        const deltaRow = screen.getByText('Delta Roast').closest('tr')!
        expect(within(deltaRow).getByText('—')).toBeTruthy()
        expect(within(deltaRow).queryByTitle('products.viewStock')).toBeNull()
    })
})

describe('Products listing — stock drill-down (F-5)', () => {
    it('opens the existing ProductStockModal from tracked stock cells', async () => {
        await renderLoaded()
        const drillDownButtons = screen.getAllByTitle('products.viewStock')
        // Alpha, Beta, Gamma and Epsilon are tracked; Delta is untracked.
        expect(drillDownButtons).toHaveLength(4)
        fireEvent.click(drillDownButtons[0])
        await waitFor(() => expect(screen.getByTestId('stock-modal').textContent).toBe('p1:Alpha Tea'))
    })
})

describe('Products listing — publication normalization (F-4)', () => {
    it('fails closed to Draft for unknown publication statuses', async () => {
        await renderLoaded()
        const epsilonRow = screen.getByText('Epsilon Odd').closest('tr')!
        expect(within(epsilonRow).getByText('products.draft')).toBeTruthy()
        expect(within(epsilonRow).queryByText('products.published')).toBeNull()
    })
})

describe('Products listing — bulk selection lifecycle (Phase 2 + F-10)', () => {
    it('selects, deselects, select-all, and shows the toolbar', async () => {
        await renderLoaded()
        expect(screen.queryByText('products.productsSelected')).toBeNull()

        selectRowByName('Alpha Tea')
        expect(screen.getByText('products.productsSelected')).toBeTruthy()

        selectRowByName('Beta Coffee')
        expect(screen.getByText('products.productsSelected')).toBeTruthy()

        const header = headerCheckbox()
        fireEvent.click(header)
        expect(rowCheckboxes().every((cb) => (cb as HTMLInputElement).checked)).toBe(true)

        fireEvent.click(header)
        expect(rowCheckboxes().every((cb) => !(cb as HTMLInputElement).checked)).toBe(true)
        expect(screen.queryByText('products.productsSelected')).toBeNull()
    })

    it('marks the header checkbox indeterminate for partial selection', async () => {
        await renderLoaded()
        selectRowByName('Alpha Tea')
        const header = headerCheckbox()
        expect(header.indeterminate).toBe(true)
        expect(header.checked).toBe(false)
    })

    it('keeps selection when only the sort order changes (F-10)', async () => {
        await renderLoaded()
        selectRowByName('Alpha Tea')
        const sortButton = screen.getAllByRole('button')
            .find((b) => b.textContent?.includes('products.stock'))
        expect(sortButton).toBeTruthy()
        fireEvent.click(sortButton!)
        await waitFor(() => expect(screen.getByText('Alpha Tea')).toBeTruthy())
        expect(screen.getByText('products.productsSelected')).toBeTruthy()
    })

    it('clears selection when a search is submitted', async () => {
        await renderLoaded()
        selectRowByName('Alpha Tea')
        expect(screen.getByText('products.productsSelected')).toBeTruthy()
        const searchInput = screen.getByLabelText('products.searchPlaceholder')
        fireEvent.change(searchInput, { target: { value: 'Alpha' } })
        fireEvent.submit(searchInput.closest('form')!)
        await waitFor(() => expect(screen.queryByText('products.productsSelected')).toBeNull())
        expect(lastProductsParams().search).toBe('Alpha')
    })

    it('clears selection when a filter changes', async () => {
        await renderLoaded()
        selectRowByName('Alpha Tea')
        fireEvent.click(screen.getByLabelText('products.featured'))
        await waitFor(() => expect(screen.queryByText('products.productsSelected')).toBeNull())
    })

    it('clears selection on pagination', async () => {
        await renderLoaded({ totalPages: '2', totalCount: '100' })
        selectRowByName('Alpha Tea')
        fireEvent.click(screen.getByText('common.next'))
        await waitFor(() => expect(screen.queryByText('products.productsSelected')).toBeNull())
        expect(lastProductsParams().page).toBe(2)
    })
})

describe('Products listing — search clear semantics (F-7)', () => {
    it('clears draft and committed search, resets page, and preserves other filters', async () => {
        await renderLoaded()

        // Apply a category filter first so we can prove it survives search clear.
        const categorySelect = screen.getAllByRole('combobox')[0]
        fireEvent.change(categorySelect, { target: { value: 'c1' } })
        await waitFor(() => expect(lastProductsParams().categoryId).toBe('c1'))

        const searchInput = screen.getByLabelText('products.searchPlaceholder')
        fireEvent.change(searchInput, { target: { value: 'Alpha' } })
        fireEvent.submit(searchInput.closest('form')!)
        await waitFor(() => expect(lastProductsParams().search).toBe('Alpha'))

        fireEvent.click(screen.getByLabelText('common.clear'))
        await waitFor(() => expect((searchInput as HTMLInputElement).value).toBe(''))
        await waitFor(() => expect(lastProductsParams().search).toBeUndefined())
        expect(lastProductsParams().categoryId).toBe('c1')
        expect(lastProductsParams().page).toBe(1)
    })
})

describe('Products listing — delete/deactivate success path (F-1)', () => {
    it('invalidates the POS runtime cache without ReferenceError and refetches', async () => {
        await renderLoaded()
        const deleteButtons = screen.getAllByText('common.delete')
        fireEvent.click(deleteButtons[0])
        await waitFor(() => expect(screen.getByTestId('delete-modal')).toBeTruthy())
        const callsBefore = productCalls().length

        fireEvent.click(screen.getByText('trigger-delete-success'))

        await waitFor(() => expect(invalidatePosRuntimeCacheMock)
            .toHaveBeenCalledWith({ reason: 'product-updated' }))
        expect(showSuccessMock).toHaveBeenCalledWith('common.deletedSuccessfully')
        await waitFor(() => expect(productCalls().length).toBeGreaterThan(callsBefore))
    })

    it('invalidates the POS runtime cache on deactivate success as well', async () => {
        await renderLoaded()
        fireEvent.click(screen.getAllByText('common.delete')[0])
        await waitFor(() => expect(screen.getByTestId('delete-modal')).toBeTruthy())

        fireEvent.click(screen.getByText('trigger-deactivate-success'))

        await waitFor(() => expect(invalidatePosRuntimeCacheMock)
            .toHaveBeenCalledWith({ reason: 'product-updated' }))
        expect(showSuccessMock).toHaveBeenCalledWith('products.deactivated')
    })
})

describe('Products listing — empty states (F-3)', () => {
    it('shows the catalog-empty message when there are no products at all', async () => {
        await renderLoaded({ products: [], totalCount: '0', totalPages: '0' })
        await waitFor(() => expect(screen.getByText('products.noProducts')).toBeTruthy())
        expect(screen.queryByText('products.noMatchingProducts')).toBeNull()
    })

    it('shows the filtered-empty message when a committed search matches nothing', async () => {
        mockProductsApi()
        render(<Products />)
        await waitFor(() => expect(screen.getAllByRole('checkbox').length).toBeGreaterThan(1))

        const searchInput = screen.getByLabelText('products.searchPlaceholder')
        fireEvent.change(searchInput, { target: { value: 'nothing-matches' } })
        fireEvent.submit(searchInput.closest('form')!)

        apiGetMock.mockImplementation((url: string) => {
            if (url === '/products') {
                return Promise.resolve({
                    data: [],
                    headers: { 'x-total-count': '0', 'x-total-pages': '0' },
                })
            }
            return Promise.resolve({ data: [] })
        })
        fireEvent.click(screen.getByLabelText('common.clear'))
        fireEvent.change(searchInput, { target: { value: 'zzz' } })
        fireEvent.submit(searchInput.closest('form')!)

        await waitFor(() => expect(screen.getByText('products.noMatchingProducts')).toBeTruthy())
        expect(screen.queryByText('products.noProductsSelected')).toBeNull()
    })
})

describe('Products listing — PDF export integration (Phase 3 remediation)', () => {
    const openExportMenu = async (user: ReturnType<typeof userEvent.setup>) => {
        await user.click(screen.getByRole('button', { name: 'products.exportMenuAria' }))
    }

    // Menu groups render Excel first, then PDF — index 1 is the PDF item.
    const clickPdfItem = async (user: ReturnType<typeof userEvent.setup>, label: string) => {
        await openExportMenu(user)
        await user.click(screen.getAllByText(label)[1])
    }

    it('configures react-to-print with the installed v3 contentRef API and a stable print target ref', async () => {
        await renderLoaded()
        const options = printMock.latestOptions!
        expect(options).toBeTruthy()
        // v2 legacy callback must not be used — the installed v3 ignores it.
        expect('content' in options).toBe(false)
        // A stable ref object is wired to ProductExportPrintArea; the node is
        // mounted on demand before printing (verified by the print snapshots
        // below, which read contentRef.current at print time).
        const contentRef = options.contentRef as { current: HTMLElement | null }
        expect(contentRef).toBeTruthy()
        expect('current' in contentRef).toBe(true)
        expect(options.documentTitle).toBe('shopixy-products-current-view')
    })

    it('prints exactly the current view dataset without additional product requests', async () => {
        const user = userEvent.setup()
        await renderLoaded()
        const callsBefore = productCalls().length

        await clickPdfItem(user, 'products.exportCurrentView')

        await waitFor(() => expect(printMock.printed).toHaveLength(1))
        const printed = printMock.printed[0]
        expect(printed.documentTitle).toBe('shopixy-products-current-view')
        for (const name of ['Alpha Tea', 'Beta Coffee', 'Gamma Blend', 'Delta Roast', 'Epsilon Odd']) {
            expect(printed.content).toContain(name)
        }
        expect(productCalls().length).toBe(callsBefore)
    })

    it('prints only the selected visible products for the selected scope', async () => {
        const user = userEvent.setup()
        await renderLoaded()
        selectRowByName('Alpha Tea')
        selectRowByName('Beta Coffee')
        const callsBefore = productCalls().length

        await clickPdfItem(user, 'products.exportSelected')

        await waitFor(() => expect(printMock.printed).toHaveLength(1))
        const printed = printMock.printed[0]
        expect(printed.documentTitle).toBe('shopixy-products-selected')
        expect(printed.content).toContain('Alpha Tea')
        expect(printed.content).toContain('Beta Coffee')
        expect(printed.content).not.toContain('Gamma Blend')
        expect(printed.content).not.toContain('Delta Roast')
        expect(printed.content).not.toContain('Epsilon Odd')
        expect(productCalls().length).toBe(callsBefore)
    })

    it('keeps PDF selected unavailable when nothing is selected', async () => {
        const user = userEvent.setup()
        await renderLoaded()
        await openExportMenu(user)

        const selectedItems = screen.getAllByText('products.exportSelected')
        expect(selectedItems).toHaveLength(2)
        for (const item of selectedItems) {
            expect(item.closest('[data-disabled]')).not.toBeNull()
        }

        await user.click(selectedItems[1]).catch(() => { /* disabled item may reject */ })
        expect(printMock.printed).toHaveLength(0)
    })
})

describe('Products embedded Product Feed management', () => {
    it('shows the catalog-level Product Feed button and opens the embedded dialog without navigating', async () => {
        const user = userEvent.setup()
        await renderLoaded()

        await user.click(screen.getByRole('button', { name: 'productFeed.title' }))

        expect(navigateMock).not.toHaveBeenCalled()
        expect(await screen.findByRole('dialog')).toBeTruthy()
        for (const channel of ['Meta', 'Google', 'TikTok', 'Snapchat']) {
            expect(screen.getByRole('tab', { name: new RegExp(channel) })).toBeTruthy()
        }
        expect(apiGetMock).toHaveBeenCalledWith('/admin/feeds/meta/tokens')
        expect(apiGetMock).toHaveBeenCalledWith('/admin/feeds/google/tokens')
        expect(apiGetMock).toHaveBeenCalledWith('/admin/feeds/tiktok/tokens')
        expect(apiGetMock).toHaveBeenCalledWith('/admin/feeds/snapchat/tokens')

        await user.click(screen.getByRole('button', { name: 'common.close' }))
        await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    })
})

describe('Products i18n hygiene (F-2/F-3/F-14)', () => {
    const currentDir = dirname(fileURLToPath(import.meta.url))

    function productsBlockKeys(locale: 'en' | 'ar') {
        const raw = readFileSync(join(currentDir, '..', 'i18n', 'locales', `${locale}.json`), 'utf8')
        const lines = raw.split('\n')
        const keys: string[] = []
        let inProducts = false
        for (const line of lines) {
            if (line.startsWith('  "products": {')) { inProducts = true; continue }
            if (inProducts && line.startsWith('  }')) { inProducts = false; continue }
            if (inProducts) {
                const match = /^\s{4}"([^"]+)":/.exec(line)
                if (match) keys.push(match[1])
            }
        }
        return keys
    }

    it('has no duplicate keys in the products namespace in either locale', () => {
        for (const locale of ['en', 'ar'] as const) {
            const keys = productsBlockKeys(locale)
            const duplicates = keys.filter((key, index) => keys.indexOf(key) !== index)
            expect(duplicates, `duplicate products keys in ${locale}`).toEqual([])
        }
    })

    it('keeps the original ProductForm labels and adds the listing keys in both locales', async () => {
        const en = JSON.parse(readFileSync(join(currentDir, '..', 'i18n', 'locales', 'en.json'), 'utf8'))
        const ar = JSON.parse(readFileSync(join(currentDir, '..', 'i18n', 'locales', 'ar.json'), 'utf8'))

        // ProductForm retains its original wording (F-2 regression repair).
        expect(en.products.simpleProduct).toBe('Simple Product')
        expect(en.products.variantProduct).toBe('Variant Product')
        expect(typeof ar.products.simpleProduct).toBe('string')
        expect(typeof ar.products.variantProduct).toBe('string')

        // Listing badges/filters use the distinctly scoped type keys.
        expect(en.products.simpleProductType).toBeTruthy()
        expect(en.products.variantProductType).toBeTruthy()
        expect(ar.products.simpleProductType).toBeTruthy()
        expect(ar.products.variantProductType).toBeTruthy()

        // Filtered-empty key exists in both locales (F-3).
        expect(en.products.noMatchingProducts).toBeTruthy()
        expect(ar.products.noMatchingProducts).toBeTruthy()

        // Genuinely unused Phase 1-2 keys were pruned (F-14).
        for (const key of ['stockStateInStock', 'stockStateLowStock', 'stockStateOutOfStock', 'appliedFilters', 'bulkActions', 'all']) {
            expect(en.products[key], `en products.${key} should be removed`).toBeUndefined()
            expect(ar.products[key], `ar products.${key} should be removed`).toBeUndefined()
        }
    })

    it('keeps Product Feed translations complete with matching interpolation placeholders', () => {
        const en = JSON.parse(readFileSync(join(currentDir, '..', 'i18n', 'locales', 'en.json'), 'utf8'))
        const ar = JSON.parse(readFileSync(join(currentDir, '..', 'i18n', 'locales', 'ar.json'), 'utf8'))

        const flatten = (value: Record<string, unknown>, prefix = ''): Record<string, string> => (
            Object.entries(value).reduce((acc, [key, entry]) => {
                const path = prefix ? `${prefix}.${key}` : key
                if (entry && typeof entry === 'object' && !Array.isArray(entry)) {
                    return { ...acc, ...flatten(entry as Record<string, unknown>, path) }
                }
                acc[path] = String(entry)
                return acc
            }, {} as Record<string, string>)
        )

        const enKeys = flatten(en.productFeed)
        const arKeys = flatten(ar.productFeed)

        expect(Object.keys(arKeys).sort()).toEqual(Object.keys(enKeys).sort())
        for (const key of Object.keys(enKeys)) {
            const enPlaceholders = enKeys[key].match(/{{[^}]+}}/g) || []
            const arPlaceholders = arKeys[key].match(/{{[^}]+}}/g) || []
            expect(arPlaceholders.sort(), `placeholder mismatch for productFeed.${key}`).toEqual(enPlaceholders.sort())
        }
    })
})
