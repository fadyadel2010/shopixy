import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
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
const confirmActionMock = vi.hoisted(() => vi.fn())
const clipboardWriteMock = vi.hoisted(() => vi.fn())
const windowOpenMock = vi.hoisted(() => vi.fn())
// Mutable RTL state so page-level Arabic direction rendering can be proven
// against the same production component.
const rtlMockState = vi.hoisted(() => ({
    current: { isRTL: false, dir: 'ltr', locale: 'en-US' },
}))
// Phase 6G.2: the page owns bulk orchestration through useBulkProductMutation;
// these doubles prove no user interaction can reach the bulk service yet.
const bulkActivateMock = vi.hoisted(() => vi.fn())
const bulkDeactivateMock = vi.hoisted(() => vi.fn())
const bulkPublishMock = vi.hoisted(() => vi.fn())
const bulkUnpublishMock = vi.hoisted(() => vi.fn())
const bulkDeleteMock = vi.hoisted(() => vi.fn())
// Stable function identity is required: Products.jsx lists `t` in a
// useCallback dependency array, so a per-render t would loop fetches forever.
// The overlay mirrors the en.json entries for the Phase 6G.3 bulk result
// strings so tests can assert explicit localized counts, not raw keys.
const mockT = vi.hoisted(() => {
    const overlay: Record<string, string> = {
        'products.bulkProcessingActivate': 'Activating {{count}} selected product(s)…',
        'products.bulkProcessingDeactivate': 'Deactivating {{count}} selected product(s)…',
        'products.bulkSuccessTitleActivate': 'Products activated',
        'products.bulkSuccessTitleDeactivate': 'Products deactivated',
        'products.bulkUnchangedTitleActivate': 'Selected products are already active',
        'products.bulkUnchangedTitleDeactivate': 'Selected products are already inactive',
        'products.bulkPartialTitle': '{{action}} completed, but some products were not changed',
        'products.bulkActionNameActivate': 'Activate',
        'products.bulkActionNameDeactivate': 'Deactivate',
        'products.bulkActionNamePublish': 'Publish',
        'products.bulkActionNameUnpublish': 'Unpublish',
        'products.bulkActionNameDelete': 'Delete',
        'products.bulkFailedTitle': 'No products were changed',
        'products.bulkNoChangesTitle': 'No products were changed',
        'products.bulkCountRequested': 'Selected: {{count}}',
        'products.bulkCountAccepted': 'Accepted: {{count}}',
        'products.bulkCountProcessedActivate': 'Activated: {{count}}',
        'products.bulkCountProcessedDeactivate': 'Deactivated: {{count}}',
        'products.bulkCountUnchangedActivate': 'Already active: {{count}}',
        'products.bulkCountUnchangedDeactivate': 'Already inactive: {{count}}',
        'products.bulkCountFailed': 'Failed: {{count}}',
        'products.bulkItemAlreadyActive': '{{name}} is already active.',
        'products.bulkItemAlreadyInactive': '{{name}} is already inactive.',
        'products.bulkItemConcurrencyConflict': '{{name}} changed before the operation completed.',
        'products.bulkItemNotFound': '{{name}} is no longer available.',
        'products.bulkItemValidationFailed': '{{name}} could not be processed.',
        'products.bulkItemUnknownUnchangedReason': '{{name}} was not changed.',
        'products.bulkItemUnknownReason': '{{name}} could not be changed.',
        'products.bulkUnknownProduct': 'Product',
        'products.bulkRequestValidationError': 'This bulk request was not valid. Please review the selection and try again.',
        'products.bulkRequestPermissionError': 'You do not have permission to perform this bulk action.',
        'products.bulkRequestConflictError': 'This bulk request conflicts with another change. Please review the products and try again.',
        'products.bulkRequestServiceError': 'The bulk operation could not be completed. Please try again later.',
        'products.bulkStartFailed': 'The bulk operation could not be started.',
        'products.bulkRefreshErrorNotice': 'The operation completed, but the products list could not be refreshed.',
        'products.bulkResultDismiss': 'Dismiss',
        'products.bulkProcessingPublish': 'Publishing {{count}} selected product(s)…',
        'products.bulkProcessingUnpublish': 'Unpublishing {{count}} selected product(s)…',
        'products.bulkSuccessTitlePublish': 'Products published',
        'products.bulkSuccessTitleUnpublish': 'Products unpublished',
        'products.bulkUnchangedTitlePublish': 'Selected products are already published',
        'products.bulkUnchangedTitleUnpublish': 'Selected products are already unpublished',
        'products.bulkCountProcessedPublish': 'Published: {{count}}',
        'products.bulkCountProcessedUnpublish': 'Unpublished: {{count}}',
        'products.bulkCountUnchangedPublish': 'Already published: {{count}}',
        'products.bulkCountUnchangedUnpublish': 'Already unpublished: {{count}}',
        'products.bulkItemAlreadyPublished': '{{name}} is already published.',
        'products.bulkItemAlreadyUnpublished': '{{name}} is already unpublished.',
        'products.bulkItemInactiveCannotPublish': 'Product is inactive and cannot be published.',
        'products.bulkItemPublicationNotReady': 'Product is not ready for publication.',
        'products.bulkItemProductName': '{{name}}:',
        'products.bulkItemStatusUnchanged': 'Unchanged',
        'products.bulkItemStatusFailed': 'Failed',
        'products.bulkResultRegionAria': 'Bulk operation result',
        // Certified column-header keys reused by the localized sort summary.
        'products.sort': 'Sort',
        'products.productName': 'Product Name',
        'products.price': 'Price',
        'products.stock': 'Stock',
        'products.status': 'Status',
        'products.sortAscending': 'Ascending',
        'products.sortDescending': 'Descending',
        'products.bulkPublishConfirmTitle': 'Publish {{count}} selected product(s)?',
        'products.bulkPublishConfirmDescription': '{{count}} selected product(s) will be made available on the published storefront. Products that are inactive or not ready for publication may be rejected by this action. Nothing will be activated automatically.',
        'products.bulkUnpublishConfirmTitle': 'Unpublish {{count}} selected product(s)?',
        'products.bulkUnpublishConfirmDescription': '{{count}} selected product(s) will be removed from the published storefront state. They will not be deactivated.',
        'products.bulkRefreshErrorNoticePublish': 'Publishing completed, but the product list could not be refreshed.',
        'products.bulkRefreshErrorNoticeUnpublish': 'Unpublishing completed, but the product list could not be refreshed.',
        'products.bulkProcessingDelete': 'Deleting {{count}} selected product(s)…',
        'products.bulkSuccessTitleDelete': 'Selected products deleted',
        'products.bulkUnchangedTitleDelete': 'Selected products were left unchanged',
        'products.bulkCountProcessedDelete': 'Deleted: {{count}}',
        'products.bulkCountUnchangedDelete': 'Unchanged: {{count}}',
        'products.bulkItemDependencyBlocked': '{{name}} cannot be deleted because it has blocking dependencies.',
        'products.bulkItemDeleteConcurrencyConflict': '{{name}} changed before deletion. Refresh the product list and try again.',
        'products.bulkItemRequestCancelled': '{{name}} was not processed because the bulk operation was cancelled.',
        'products.bulkItemIdempotencyConflict': '{{name}} could not be deleted because this delete request conflicts with an earlier request.',
        'products.bulkItemIdempotencyInProgress': '{{name}} is already being deleted by a request still in progress.',
        'products.bulkDeleteConfirmTitle': 'Delete {{count}} selected product(s)?',
        'products.bulkDeleteConfirmDescription': '{{count}} selected product(s) will be removed from active product operations. Products with blocking dependencies may fail individually. Deletion is best-effort per product.',
        'products.bulkRefreshErrorNoticeDelete': 'Deletion completed, but the product list could not be refreshed.',
        'products.bulkDeleteUncertainTitle': 'Deletion could not be confirmed',
        'products.bulkDeleteUncertainMessage': 'The delete request could not be confirmed and may still be processing. Refresh the product list before retrying; the deletion will not repeat automatically.',
        'products.bulkDeleteIdempotencyConflictMessage': 'This deletion request conflicts with an earlier request. Refresh the product list before trying again.',
        'products.bulkDeleteIdempotencyInProgressMessage': 'A deletion request is already in progress. Refresh the product list before trying again.',
        'products.bulkRequestUncertainTitle': 'The bulk request could not be confirmed',
        'products.bulkRequestUncertainMessage': 'The request could not be confirmed and may still be processing. Refresh the product list before retrying; the operation will not repeat automatically.',
        'products.bulkRequestIdempotencyConflictMessage': 'This bulk request conflicts with an earlier request. Refresh the product list before trying again.',
        'products.bulkRequestIdempotencyInProgressMessage': 'A similar bulk request is already in progress. Refresh the product list before trying again.',
        'products.bulkRequestRefreshWarning': 'The product list could not be refreshed.',
        'products.bulkRequestFailedTitle': 'The bulk request could not be completed',
        'products.bulkRequestAttentionTitle': 'The bulk request needs attention',
    }
    return (key: string, options?: Record<string, unknown>) => {
        const template = overlay[key] ?? key
        if (!options || typeof options !== 'object') return template
        return Object.entries(options).reduce(
            (acc, [name, value]) => acc.split(`{{${name}}}`).join(String(value)),
            template,
        )
    }
})

vi.mock('../services/api', () => ({
    default: { get: apiGetMock, post: apiPostMock },
}))

vi.mock('../services/bulkProductApi', () => ({
    bulkActivate: bulkActivateMock,
    bulkDeactivate: bulkDeactivateMock,
    bulkPublish: bulkPublishMock,
    bulkUnpublish: bulkUnpublishMock,
    bulkDelete: bulkDeleteMock,
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
    useConfirmDialog: () => ({ confirm: confirmMock, confirmAction: confirmActionMock }),
}))

vi.mock('../contexts/SettingsContext', () => ({
    useSettings: () => ({
        settings: { companyName: 'Test Store' },
        formatCurrency: (value: number) => `$${value}`,
    }),
}))

vi.mock('../hooks/useRTL', () => ({
    useRTL: () => ({ ...rtlMockState.current }),
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
        // xmin-backed concurrency version exactly as the listing DTO exposes it.
        rowVersion: '7',
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

function installFeedClipboard() {
    Object.defineProperty(window.navigator, 'clipboard', {
        configurable: true,
        writable: true,
        value: { writeText: clipboardWriteMock },
    })
    vi.stubGlobal('navigator', {
        ...window.navigator,
        clipboard: { writeText: clipboardWriteMock },
    })
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

// ---- Phase 6G.3 bulk action test helpers ----

function toolbarButton(label: string) {
    const button = screen.getByText(label).closest('button') as HTMLButtonElement | null
    if (!button) throw new Error(`toolbar button not found for ${label}`)
    return button
}

function createDeferred<T>() {
    let resolve!: (value: T) => void
    let reject!: (reason?: unknown) => void
    const promise = new Promise<T>((res, rej) => {
        resolve = res
        reject = rej
    })
    return { promise, resolve, reject }
}

// Mirrors the certified Phase 6G.1 response contract consumed by the page.
const bulkAggregate = (overrides: Record<string, unknown> = {}): Record<string, unknown> => ({
    requested: 1,
    accepted: 1,
    processed: 1,
    unchanged: 0,
    failed: 0,
    correlationId: 'corr-products-test',
    results: [
        { productId: 'p1', status: 'processed', action: null, code: null, message: null, previousVersion: '7', newVersion: '8' },
    ],
    ...overrides,
})

const bulkItem = (overrides: Record<string, unknown>) => ({
    productId: 'p1',
    status: 'processed',
    action: null,
    code: null,
    message: null,
    previousVersion: '7',
    newVersion: '8',
    ...overrides,
})

beforeEach(() => {
    vi.clearAllMocks()
    rtlMockState.current = { isRTL: false, dir: 'ltr', locale: 'en-US' }
    printMock.latestOptions = null
    printMock.printed = []
    apiPostMock.mockResolvedValue({ data: {} })
    confirmMock.mockResolvedValue(true)
    // Mirrors the provider's legacy confirmAction contract: decide first, run
    // the action after the decision. Delete tests assert the awaited-action
    // options; this default keeps every other consumer path realistic.
    confirmActionMock.mockImplementation(async ({ onConfirm, ...options }: {
        onConfirm?: () => unknown
    } & Record<string, unknown> = {}) => {
        const confirmed = await confirmMock(options)
        if (!confirmed) return false
        if (typeof onConfirm === 'function') return await onConfirm()
        return true
    })
    clipboardWriteMock.mockResolvedValue(undefined)
    Object.defineProperty(window, 'open', {
        configurable: true,
        value: windowOpenMock,
    })
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
        expect(toolbarButton('products.bulkActionActivate').disabled).toBe(true)

        selectRowByName('Alpha Tea')
        expect(toolbarButton('products.bulkActionActivate').disabled).toBe(false)

        selectRowByName('Beta Coffee')
        expect(screen.getByText('products.productsSelected')).toBeTruthy()

        const header = headerCheckbox()
        fireEvent.click(header)
        expect(rowCheckboxes().every((cb) => (cb as HTMLInputElement).checked)).toBe(true)

        fireEvent.click(header)
        expect(rowCheckboxes().every((cb) => !(cb as HTMLInputElement).checked)).toBe(true)
        expect(toolbarButton('products.bulkActionActivate').disabled).toBe(true)
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
            .find((b) => b.closest('th') && b.textContent?.includes('Stock'))
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
        await waitFor(() => expect(toolbarButton('products.bulkActionActivate').disabled).toBe(true))
        expect(lastProductsParams().search).toBe('Alpha')
    })

    it('clears selection when a filter changes', async () => {
        await renderLoaded()
        selectRowByName('Alpha Tea')
        fireEvent.click(screen.getByLabelText('products.featured'))
        await waitFor(() => expect(toolbarButton('products.bulkActionActivate').disabled).toBe(true))
    })

    it('clears selection on pagination', async () => {
        await renderLoaded({ totalPages: '2', totalCount: '100' })
        selectRowByName('Alpha Tea')
        fireEvent.click(screen.getByText('common.next'))
        await waitFor(() => expect(toolbarButton('products.bulkActionActivate').disabled).toBe(true))
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

    it('retains a created channel URL for Copy and Open after dialog close and reopen', async () => {
        const user = userEvent.setup()
        Object.defineProperty(window.navigator, 'clipboard', {
            configurable: true,
            value: { writeText: clipboardWriteMock },
        })
        vi.stubGlobal('navigator', {
            ...window.navigator,
            clipboard: { writeText: clipboardWriteMock },
        })
        const rawUrl = 'https://api.shopixy.test/api/public/feeds/google/page-session-secret'
        let googleActive = false

        apiGetMock.mockImplementation((url: string) => {
            if (url === '/categories') return Promise.resolve({ data: [{ id: 'c1', name: 'Cat' }] })
            if (url === '/products') {
                return Promise.resolve({
                    data: defaultProducts(),
                    headers: { 'x-total-count': '5', 'x-total-pages': '1' },
                })
            }
            if (url === '/admin/feeds/google/tokens') {
                return Promise.resolve({
                    data: googleActive
                        ? [{ tokenId: 'google-token', channel: 'google', createdAtUtc: '2026-08-20T00:00:00Z', revokedAtUtc: null }]
                        : [],
                })
            }
            if (url.startsWith('/admin/feeds/')) return Promise.resolve({ data: [] })
            return Promise.resolve({ data: [] })
        })
        apiPostMock.mockImplementation((url: string) => {
            if (url === '/admin/feeds/google/tokens') {
                googleActive = true
                return Promise.resolve({
                    data: {
                        tokenId: 'google-token',
                        channel: 'google',
                        feedUrl: rawUrl,
                        createdAtUtc: '2026-08-20T00:00:00Z',
                    },
                })
            }
            return Promise.resolve({ data: {} })
        })

        render(<Products />)
        await waitFor(() => expect(screen.getAllByRole('checkbox').length).toBeGreaterThan(1))
        await user.click(screen.getByRole('button', { name: 'productFeed.title' }))
        await user.click(await screen.findByRole('tab', { name: /Google/ }))
        await user.click(screen.getByRole('button', { name: 'productFeed.createAria' }))
        await user.click(await screen.findByRole('button', { name: 'productFeed.done' }))
        await user.click(screen.getByRole('button', { name: 'common.close' }))
        await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())

        await user.click(screen.getByRole('button', { name: 'productFeed.title' }))
        await user.click(await screen.findByRole('tab', { name: /Google/ }))
        await user.click(screen.getByRole('button', { name: 'productFeed.copyUrl' }))
        await user.click(screen.getByRole('button', { name: 'productFeed.openFeedAria' }))

        await waitFor(() => expect(clipboardWriteMock).toHaveBeenLastCalledWith(rawUrl))
        expect(windowOpenMock).toHaveBeenLastCalledWith(rawUrl, '_blank', 'noopener,noreferrer')
    })

    it('survives a direct close (no Done) and retains the created URL for Copy and Open', async () => {
        const user = userEvent.setup()
        Object.defineProperty(window.navigator, 'clipboard', {
            configurable: true,
            value: { writeText: clipboardWriteMock },
        })
        vi.stubGlobal('navigator', {
            ...window.navigator,
            clipboard: { writeText: clipboardWriteMock },
        })
        const rawUrl = 'https://api.shopixy.test/api/public/feeds/meta/page-session-secret'
        let metaActive = false

        apiGetMock.mockImplementation((url: string) => {
            if (url === '/categories') return Promise.resolve({ data: [{ id: 'c1', name: 'Cat' }] })
            if (url === '/products') {
                return Promise.resolve({
                    data: defaultProducts(),
                    headers: { 'x-total-count': '5', 'x-total-pages': '1' },
                })
            }
            if (url === '/admin/feeds/meta/tokens') {
                return Promise.resolve({
                    data: metaActive
                        ? [{ tokenId: 'meta-token', channel: 'meta', createdAtUtc: '2026-08-20T00:00:00Z', revokedAtUtc: null }]
                        : [],
                })
            }
            if (url.startsWith('/admin/feeds/')) return Promise.resolve({ data: [] })
            return Promise.resolve({ data: [] })
        })
        apiPostMock.mockImplementation((url: string) => {
            if (url === '/admin/feeds/meta/tokens') {
                metaActive = true
                return Promise.resolve({
                    data: {
                        tokenId: 'meta-token',
                        channel: 'meta',
                        feedUrl: rawUrl,
                        createdAtUtc: '2026-08-20T00:00:00Z',
                    },
                })
            }
            return Promise.resolve({ data: {} })
        })

        render(<Products />)
        await waitFor(() => expect(screen.getAllByRole('checkbox').length).toBeGreaterThan(1))

        // 2-3. Open Product Feed and select Meta.
        await user.click(screen.getByRole('button', { name: 'productFeed.title' }))
        await user.click(await screen.findByRole('tab', { name: /Meta/ }))

        // 4. Create Meta and return a distinctive exact raw URL.
        await user.click(screen.getByRole('button', { name: 'productFeed.createAria' }))

        // 5. Copy and Open exist immediately (one-time disclosure card).
        expect(await screen.findByRole('button', { name: 'productFeed.copyUrl' })).toBeTruthy()
        expect(screen.getByRole('button', { name: 'productFeed.openFeedAria' })).toBeTruthy()

        // 6. Close the dialog directly, without clicking Done first.
        await user.click(screen.getByRole('button', { name: 'common.close' }))
        await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())

        // 7-8. Reopen Product Feed and reselect Meta.
        await user.click(screen.getByRole('button', { name: 'productFeed.title' }))
        await user.click(await screen.findByRole('tab', { name: /Meta/ }))

        // 9. Copy and Open still exist against the retained session URL.
        expect(await screen.findByRole('button', { name: 'productFeed.copyUrl' })).toBeTruthy()
        expect(screen.getByRole('button', { name: 'productFeed.openFeedAria' })).toBeTruthy()

        // 10-11. Click Copy and Open and assert exact URL + target/features.
        await user.click(screen.getByRole('button', { name: 'productFeed.copyUrl' }))
        await user.click(screen.getByRole('button', { name: 'productFeed.openFeedAria' }))

        await waitFor(() => expect(clipboardWriteMock).toHaveBeenLastCalledWith(rawUrl))
        expect(windowOpenMock).toHaveBeenLastCalledWith(rawUrl, '_blank', 'noopener,noreferrer')

        // No persistent secret storage was introduced.
        expect(localStorage.length).toBe(0)
        expect(sessionStorage.length).toBe(0)
    })

    it('replaces the session URL on rotate so Copy returns the new URL, not the old one', async () => {
        const user = userEvent.setup()
        installFeedClipboard()
        const createUrl = 'https://api.shopixy.test/api/public/feeds/meta/create-secret'
        const rotatedUrl = 'https://api.shopixy.test/api/public/feeds/meta/rotated-secret'

        apiGetMock.mockImplementation((url: string) => {
            if (url === '/categories') return Promise.resolve({ data: [{ id: 'c1', name: 'Cat' }] })
            if (url === '/products') {
                return Promise.resolve({
                    data: defaultProducts(),
                    headers: { 'x-total-count': '5', 'x-total-pages': '1' },
                })
            }
            if (url.startsWith('/admin/feeds/')) return Promise.resolve({ data: [] })
            return Promise.resolve({ data: [] })
        })
        apiPostMock.mockImplementation((url: string) => {
            if (url === '/admin/feeds/meta/tokens') {
                return Promise.resolve({
                    data: { tokenId: 'meta-token', channel: 'meta', feedUrl: createUrl, createdAtUtc: '2026-08-20T00:00:00Z' },
                })
            }
            if (url.endsWith('/rotate')) {
                return Promise.resolve({
                    data: { tokenId: 'meta-token', channel: 'meta', feedUrl: rotatedUrl, createdAtUtc: '2026-08-20T01:00:00Z' },
                })
            }
            return Promise.resolve({ data: {} })
        })

        render(<Products />)
        await waitFor(() => expect(screen.getAllByRole('checkbox').length).toBeGreaterThan(1))
        await user.click(screen.getByRole('button', { name: 'productFeed.title' }))
        await user.click(await screen.findByRole('tab', { name: /Meta/ }))
        await user.click(screen.getByRole('button', { name: 'productFeed.createAria' }))
        await user.click(await screen.findByRole('button', { name: 'productFeed.done' }))

        await user.click(screen.getByRole('button', { name: 'productFeed.rotateAria' }))
        await waitFor(() => expect(apiPostMock).toHaveBeenCalledWith('/admin/feeds/meta/tokens/meta-token/rotate'))
        await user.click(await screen.findByRole('button', { name: 'productFeed.done' }))
        await user.click(screen.getByRole('button', { name: 'productFeed.copyUrl' }))

        await waitFor(() => expect(clipboardWriteMock).toHaveBeenLastCalledWith(rotatedUrl))
        expect(clipboardWriteMock).not.toHaveBeenCalledWith(createUrl)
    })

    it('keeps retained feed URLs isolated by channel', async () => {
        const user = userEvent.setup()
        installFeedClipboard()
        const metaUrl = 'https://api.shopixy.test/api/public/feeds/meta/isolation-secret'
        let metaActive = false

        apiGetMock.mockImplementation((url: string) => {
            if (url === '/categories') return Promise.resolve({ data: [{ id: 'c1', name: 'Cat' }] })
            if (url === '/products') {
                return Promise.resolve({
                    data: defaultProducts(),
                    headers: { 'x-total-count': '5', 'x-total-pages': '1' },
                })
            }
            if (url === '/admin/feeds/meta/tokens') {
                return Promise.resolve({
                    data: metaActive
                        ? [{ tokenId: 'meta-token', channel: 'meta', createdAtUtc: '2026-08-20T00:00:00Z', revokedAtUtc: null }]
                        : [],
                })
            }
            if (url.startsWith('/admin/feeds/')) return Promise.resolve({ data: [] })
            return Promise.resolve({ data: [] })
        })
        apiPostMock.mockImplementation((url: string) => {
            if (url === '/admin/feeds/meta/tokens') {
                metaActive = true
                return Promise.resolve({
                    data: {
                        tokenId: 'meta-token',
                        channel: 'meta',
                        feedUrl: metaUrl,
                        createdAtUtc: '2026-08-20T00:00:00Z',
                    },
                })
            }
            return Promise.resolve({ data: {} })
        })

        render(<Products />)
        await waitFor(() => expect(screen.getAllByRole('checkbox').length).toBeGreaterThan(1))
        await user.click(screen.getByRole('button', { name: 'productFeed.title' }))
        await user.click(await screen.findByRole('tab', { name: /Meta/ }))
        await user.click(screen.getByRole('button', { name: 'productFeed.createAria' }))
        await user.click(await screen.findByRole('button', { name: 'productFeed.done' }))

        await user.click(screen.getByRole('tab', { name: /Google/ }))
        expect(screen.queryByRole('button', { name: 'productFeed.copyUrl' })).toBeNull()
        expect(screen.queryByRole('button', { name: 'productFeed.openFeedAria' })).toBeNull()

        await user.click(screen.getByRole('tab', { name: /Meta/ }))
        await user.click(screen.getByRole('button', { name: 'productFeed.copyUrl' }))
        expect(clipboardWriteMock).toHaveBeenLastCalledWith(metaUrl)
    })

    it('clears the session URL on revoke so Copy/Open disappear', async () => {
        const user = userEvent.setup()
        installFeedClipboard()
        const rawUrl = 'https://api.shopixy.test/api/public/feeds/meta/revoke-test-secret'
        const activeToken = { tokenId: 'meta-token', channel: 'meta', createdAtUtc: '2026-08-20T00:00:00Z', revokedAtUtc: null }
        // Meta starts not configured; it only becomes active after create and
        // returns to a not-active state after revoke (list refresh is realistic).
        let metaActive = false

        apiGetMock.mockImplementation((url: string) => {
            if (url === '/categories') return Promise.resolve({ data: [{ id: 'c1', name: 'Cat' }] })
            if (url === '/products') {
                return Promise.resolve({
                    data: defaultProducts(),
                    headers: { 'x-total-count': '5', 'x-total-pages': '1' },
                })
            }
            if (url === '/admin/feeds/meta/tokens') {
                return Promise.resolve({ data: metaActive ? [activeToken] : [] })
            }
            if (url.startsWith('/admin/feeds/')) return Promise.resolve({ data: [] })
            return Promise.resolve({ data: [] })
        })
        apiPostMock.mockImplementation((url: string) => {
            if (url === '/admin/feeds/meta/tokens') {
                metaActive = true
                return Promise.resolve({
                    data: { tokenId: 'meta-token', channel: 'meta', feedUrl: rawUrl, createdAtUtc: '2026-08-20T00:00:00Z' },
                })
            }
            if (url.endsWith('/revoke')) {
                metaActive = false
                return Promise.resolve({ data: undefined })
            }
            return Promise.resolve({ data: {} })
        })

        render(<Products />)
        await waitFor(() => expect(screen.getAllByRole('checkbox').length).toBeGreaterThan(1))
        await user.click(screen.getByRole('button', { name: 'productFeed.title' }))
        await user.click(await screen.findByRole('tab', { name: /Meta/ }))

        // Initially not configured -> Create is available and Copy/Open are absent.
        expect(await screen.findByRole('button', { name: 'productFeed.createAria' })).toBeTruthy()
        expect(screen.queryByRole('button', { name: 'productFeed.copyUrl' })).toBeNull()

        await user.click(screen.getByRole('button', { name: 'productFeed.createAria' }))
        await user.click(await screen.findByRole('button', { name: 'productFeed.done' }))
        await user.click(screen.getByRole('button', { name: 'productFeed.copyUrl' }))
        await waitFor(() => expect(clipboardWriteMock).toHaveBeenLastCalledWith(rawUrl))

        await user.click(screen.getByRole('button', { name: 'productFeed.revokeAria' }))
        await waitFor(() => expect(apiPostMock).toHaveBeenCalledWith('/admin/feeds/meta/tokens/meta-token/revoke'))
        // Revoke invalidated the token -> metadata refreshes to a not-active state
        // and the retained session URL is discarded, so Copy/Open disappear.
        expect(await screen.findByRole('button', { name: 'productFeed.createAria' })).toBeTruthy()
        expect(screen.queryByRole('button', { name: 'productFeed.copyUrl' })).toBeNull()
    })

    it('clears retained feed URLs when the Products page unmounts before reopening', async () => {
        const user = userEvent.setup()
        installFeedClipboard()
        const rawUrl = 'https://api.shopixy.test/api/public/feeds/meta/unmount-secret'
        const activeToken = { tokenId: 'meta-token', channel: 'meta', createdAtUtc: '2026-08-20T00:00:00Z', revokedAtUtc: null }
        // Meta starts not configured; create flips it to active so the session
        // URL is reachable. The metadata stays active across remount, so the
        // only thing reset by unmount is the in-memory parent session URL.
        let metaActive = false

        apiGetMock.mockImplementation((url: string) => {
            if (url === '/categories') return Promise.resolve({ data: [{ id: 'c1', name: 'Cat' }] })
            if (url === '/products') {
                return Promise.resolve({
                    data: defaultProducts(),
                    headers: { 'x-total-count': '5', 'x-total-pages': '1' },
                })
            }
            if (url === '/admin/feeds/meta/tokens') {
                return Promise.resolve({ data: metaActive ? [activeToken] : [] })
            }
            if (url.startsWith('/admin/feeds/')) return Promise.resolve({ data: [] })
            return Promise.resolve({ data: [] })
        })
        apiPostMock.mockImplementation((url: string) => {
            if (url === '/admin/feeds/meta/tokens') {
                metaActive = true
                return Promise.resolve({
                    data: { tokenId: 'meta-token', channel: 'meta', feedUrl: rawUrl, createdAtUtc: '2026-08-20T00:00:00Z' },
                })
            }
            return Promise.resolve({ data: {} })
        })

        const { unmount } = render(<Products />)
        await waitFor(() => expect(screen.getAllByRole('checkbox').length).toBeGreaterThan(1))
        await user.click(screen.getByRole('button', { name: 'productFeed.title' }))
        await user.click(await screen.findByRole('tab', { name: /Meta/ }))
        await user.click(screen.getByRole('button', { name: 'productFeed.createAria' }))
        await user.click(await screen.findByRole('button', { name: 'productFeed.done' }))
        await user.click(screen.getByRole('button', { name: 'productFeed.copyUrl' }))
        await waitFor(() => expect(clipboardWriteMock).toHaveBeenLastCalledWith(rawUrl))

        // Unmounting Products discards its React memory; reopening must not retain the URL.
        unmount()
        render(<Products />)
        await waitFor(() => expect(screen.getAllByRole('checkbox').length).toBeGreaterThan(1))
        await user.click(screen.getByRole('button', { name: 'productFeed.title' }))
        await user.click(await screen.findByRole('tab', { name: /Meta/ }))
        expect(await screen.findByText('productFeed.sessionUrlUnavailable')).toBeTruthy()
        expect(screen.queryByRole('button', { name: 'productFeed.copyUrl' })).toBeNull()
    })
})

describe('Products bulk Activate/Deactivate actions (Phase 6G.3)', () => {
    it('offers no way to start without a selection and enables all five actions once products are selected', async () => {
        await renderLoaded()
        for (const label of [
            'products.bulkActionActivate',
            'products.bulkActionDeactivate',
            'products.bulkActionPublish',
            'products.bulkActionUnpublish',
            'products.bulkActionDelete',
        ]) {
            expect(toolbarButton(label).disabled, `${label} must be disabled without a selection`).toBe(true)
        }

        // Without a selection even the live controls cannot reach the
        // confirmation layer or any service.
        fireEvent.click(toolbarButton('products.bulkActionActivate'))
        fireEvent.click(toolbarButton('products.bulkActionDeactivate'))
        fireEvent.click(toolbarButton('products.bulkActionPublish'))
        fireEvent.click(toolbarButton('products.bulkActionUnpublish'))
        fireEvent.click(toolbarButton('products.bulkActionDelete'))
        expect(confirmMock).not.toHaveBeenCalled()
        expect(confirmActionMock).not.toHaveBeenCalled()
        for (const serviceFn of [bulkActivateMock, bulkDeactivateMock, bulkPublishMock, bulkUnpublishMock, bulkDeleteMock]) {
            expect(serviceFn).not.toHaveBeenCalled()
        }

        selectRowByName('Alpha Tea')

        expect(toolbarButton('products.bulkActionActivate').disabled).toBe(false)
        expect(toolbarButton('products.bulkActionDeactivate').disabled).toBe(false)
        expect(toolbarButton('products.bulkActionPublish').disabled).toBe(false)
        expect(toolbarButton('products.bulkActionUnpublish').disabled).toBe(false)
        expect(toolbarButton('products.bulkActionDelete').disabled).toBe(false)

        // Activate/Deactivate/Publish/Unpublish still bypass nothing: opening
        // the Delete confirmation dispatches zero mutations of any kind.
        confirmMock.mockReturnValueOnce(new Promise<boolean>(() => {}))
        fireEvent.click(toolbarButton('products.bulkActionDelete'))
        expect(confirmActionMock).toHaveBeenCalledTimes(1)

        for (const serviceFn of [bulkActivateMock, bulkDeactivateMock, bulkPublishMock, bulkUnpublishMock, bulkDeleteMock]) {
            expect(serviceFn).not.toHaveBeenCalled()
        }
    })

    it('starts exactly one activate request with the immutable selected A/B/C versions, announces processing, then refetches and clears selection', async () => {
        const deferred = createDeferred<ReturnType<typeof bulkAggregate>>()
        bulkActivateMock.mockReturnValueOnce(deferred.promise)
        await renderLoaded({
            products: [
                makeProduct({ id: 'p1', name: 'Alpha Tea', rowVersion: '101' }),
                makeProduct({ id: 'p2', name: 'Beta Coffee', salePrice: 20, warehouseStocks: [{ quantity: 3 }], rowVersion: '102' }),
                makeProduct({ id: 'p3', name: 'Gamma Blend', salePrice: 30, hasVariants: true, rowVersion: '103' }),
            ],
        })
        selectRowByName('Alpha Tea')
        selectRowByName('Beta Coffee')
        selectRowByName('Gamma Blend')
        const callsBeforeRefetch = productCalls().length

        fireEvent.click(toolbarButton('products.bulkActionActivate'))

        expect(bulkActivateMock).toHaveBeenCalledTimes(1)
        expect(bulkActivateMock.mock.calls[0]).toHaveLength(1)
        expect(bulkActivateMock.mock.calls[0][0]).toEqual([
            { productId: 'p1', expectedVersion: '101' },
            { productId: 'p2', expectedVersion: '102' },
            { productId: 'p3', expectedVersion: '103' },
        ])

        // Localized processing state is visible and announced while locked.
        expect(screen.getByText('Activating 3 selected product(s)…')).toBeTruthy()
        const toolbar = screen.getByText('products.productsSelected').closest('[aria-busy]')
        expect(toolbar?.getAttribute('aria-busy')).toBe('true')

        await act(async () => {
            deferred.resolve(bulkAggregate({ requested: 3, accepted: 3, processed: 3 }))
        })

        await waitFor(() => expect(screen.getByText('Products activated')).toBeTruthy())
        expect(screen.getByText('Selected: 3')).toBeTruthy()
        expect(screen.getByText('Activated: 3')).toBeTruthy()
        expect(screen.getByText('Already active: 0')).toBeTruthy()
        expect(screen.getByText('Failed: 0')).toBeTruthy()

        // Terminal aggregate -> canonical refetch + selection clearing.
        await waitFor(() => expect(productCalls().length).toBeGreaterThan(callsBeforeRefetch))
        await waitFor(() => expect(toolbarButton('products.bulkActionActivate').disabled).toBe(true))
        expect(bulkActivateMock).toHaveBeenCalledTimes(1)
    })

    it('blocks rapid duplicate clicks into exactly one request', async () => {
        const deferred = createDeferred<ReturnType<typeof bulkAggregate>>()
        bulkActivateMock.mockReturnValueOnce(deferred.promise)
        await renderLoaded()
        selectRowByName('Alpha Tea')

        const activate = toolbarButton('products.bulkActionActivate')
        fireEvent.click(activate)
        fireEvent.click(activate)

        expect(bulkActivateMock).toHaveBeenCalledTimes(1)

        await act(async () => {
            deferred.resolve(bulkAggregate())
        })
        await waitFor(() => expect(screen.getByText('Products activated')).toBeTruthy())
        expect(bulkActivateMock).toHaveBeenCalledTimes(1)
    })

    it('blocks rapid Deactivate clicks into exactly one request', async () => {
        const deferred = createDeferred<ReturnType<typeof bulkAggregate>>()
        bulkDeactivateMock.mockReturnValueOnce(deferred.promise)
        await renderLoaded()
        selectRowByName('Alpha Tea')

        const deactivate = toolbarButton('products.bulkActionDeactivate')
        fireEvent.click(deactivate)
        fireEvent.click(deactivate)

        expect(bulkDeactivateMock).toHaveBeenCalledTimes(1)
        expect(screen.getByText('Deactivating 1 selected product(s)…')).toBeTruthy()

        await act(async () => {
            deferred.resolve(bulkAggregate())
        })
        await waitFor(() => expect(screen.getByText('Products deactivated')).toBeTruthy())
        expect(bulkDeactivateMock).toHaveBeenCalledTimes(1)
    })

    it('locks every toolbar action control while a bulk mutation is pending', async () => {
        const deferred = createDeferred<ReturnType<typeof bulkAggregate>>()
        bulkActivateMock.mockReturnValueOnce(deferred.promise)
        await renderLoaded()
        selectRowByName('Alpha Tea')
        fireEvent.click(toolbarButton('products.bulkActionActivate'))

        for (const label of [
            'products.bulkActionActivate',
            'products.bulkActionDeactivate',
            'products.bulkActionPublish',
            'products.bulkActionUnpublish',
            'products.bulkActionDelete',
        ]) {
            expect(toolbarButton(label).disabled, `${label} must be disabled while mutating`).toBe(true)
        }

        await act(async () => {
            deferred.resolve(bulkAggregate())
        })
        await waitFor(() => expect(screen.getByText('Products activated')).toBeTruthy())
    })

    it('reports mixed outcomes with explicit counts, names Product B for its concurrency failure, refetches and never retries', async () => {
        const deferred = createDeferred<ReturnType<typeof bulkAggregate>>()
        bulkActivateMock.mockReturnValueOnce(deferred.promise)
        await renderLoaded()
        for (const name of ['Alpha Tea', 'Beta Coffee', 'Gamma Blend', 'Delta Roast', 'Epsilon Odd']) {
            selectRowByName(name)
        }
        const callsBeforeRefetch = productCalls().length

        fireEvent.click(toolbarButton('products.bulkActionActivate'))

        await act(async () => {
            deferred.resolve(bulkAggregate({
                requested: 5,
                accepted: 5,
                processed: 3,
                unchanged: 1,
                failed: 1,
                results: [
                    bulkItem({ productId: 'p1', status: 'processed' }),
                    bulkItem({
                        productId: 'p2',
                        status: 'failed',
                        action: 'activate',
                        code: 'CONCURRENCY_CONFLICT',
                        message: 'Product was modified concurrently by another user.',
                    }),
                    bulkItem({ productId: 'p3', status: 'processed' }),
                    bulkItem({ productId: 'p4', status: 'unchanged', action: 'activate', code: 'ALREADY_ACTIVE', newVersion: '7' }),
                    bulkItem({ productId: 'p5', status: 'processed' }),
                ],
            }))
        })

        await waitFor(() => expect(screen.getByText('Activate completed, but some products were not changed')).toBeTruthy())
        expect(screen.getByText('Selected: 5')).toBeTruthy()
        expect(screen.getByText('Activated: 3')).toBeTruthy()
        expect(screen.getByText('Already active: 1')).toBeTruthy()
        expect(screen.getByText('Failed: 1')).toBeTruthy()
        // Product B (Beta Coffee) must be identified by name via its machine
        // code — never via the backend human message.
        expect(screen.getByText('Beta Coffee changed before the operation completed.')).toBeTruthy()
        expect(screen.getByText('Delta Roast is already active.')).toBeTruthy()
        expect(screen.queryByText(/modified concurrently by another user/i)).toBeNull()

        await waitFor(() => expect(productCalls().length).toBeGreaterThan(callsBeforeRefetch))
        await waitFor(() => expect(toolbarButton('products.bulkActionActivate').disabled).toBe(true))
        await act(async () => {
            await Promise.resolve()
            await Promise.resolve()
        })
        expect(bulkActivateMock).toHaveBeenCalledTimes(1)
    })

    it('reports an all-item aggregate failure as no products were changed, mapping NOT_FOUND safely while still refetching', async () => {
        const deferred = createDeferred<ReturnType<typeof bulkAggregate>>()
        bulkDeactivateMock.mockReturnValueOnce(deferred.promise)
        await renderLoaded()
        selectRowByName('Alpha Tea')
        selectRowByName('Beta Coffee')
        const callsBeforeRefetch = productCalls().length

        fireEvent.click(toolbarButton('products.bulkActionDeactivate'))

        await act(async () => {
            deferred.resolve(bulkAggregate({
                requested: 2,
                accepted: 2,
                processed: 0,
                unchanged: 0,
                failed: 2,
                results: [
                    bulkItem({ productId: 'p1', status: 'failed', action: 'deactivate', code: 'NOT_FOUND', previousVersion: null, newVersion: null }),
                    bulkItem({ productId: 'p2', status: 'failed', action: 'deactivate', code: 'NOT_FOUND', previousVersion: null, newVersion: null }),
                ],
            }))
        })

        await waitFor(() => expect(screen.getByText('No products were changed')).toBeTruthy())
        expect(screen.getByText('Selected: 2')).toBeTruthy()
        expect(screen.getByText('Deactivated: 0')).toBeTruthy()
        expect(screen.getByText('Already inactive: 0')).toBeTruthy()
        expect(screen.getByText('Failed: 2')).toBeTruthy()
        expect(screen.getByText('Alpha Tea is no longer available.')).toBeTruthy()
        expect(screen.getByText('Beta Coffee is no longer available.')).toBeTruthy()

        await waitFor(() => expect(productCalls().length).toBeGreaterThan(callsBeforeRefetch))
        await waitFor(() => expect(toolbarButton('products.bulkActionDeactivate').disabled).toBe(true))
        expect(bulkDeactivateMock).toHaveBeenCalledTimes(1)
    })

    it('falls back safely for unknown item codes without exposing raw internals', async () => {
        bulkActivateMock.mockResolvedValueOnce(bulkAggregate({
            processed: 0,
            failed: 1,
            results: [
                bulkItem({
                    status: 'failed',
                    action: 'activate',
                    code: 'FUTURE_UNKNOWN_CODE',
                    message: 'NpgsqlException: select * from System.Products',
                }),
            ],
        }))
        await renderLoaded()
        selectRowByName('Alpha Tea')

        fireEvent.click(toolbarButton('products.bulkActionActivate'))

        await waitFor(() => expect(screen.getByText('No products were changed')).toBeTruthy())
        expect(screen.getByText('Alpha Tea could not be changed.')).toBeTruthy()
        const bodyText = document.body.textContent ?? ''
        expect(bodyText).not.toContain('FUTURE_UNKNOWN_CODE')
        expect(bodyText).not.toContain('Npgsql')
        expect(bodyText).not.toContain('select * from')
        expect(bulkActivateMock).toHaveBeenCalledTimes(1)
    })

    it('keeps a pending A/B/C operation intact when the selection changes mid-flight and starts no second request', async () => {
        const deferred = createDeferred<ReturnType<typeof bulkAggregate>>()
        bulkActivateMock.mockReturnValueOnce(deferred.promise)
        await renderLoaded()
        selectRowByName('Alpha Tea')
        selectRowByName('Beta Coffee')
        selectRowByName('Gamma Blend')

        fireEvent.click(toolbarButton('products.bulkActionActivate'))

        expect(bulkActivateMock.mock.calls[0][0]).toEqual([
            { productId: 'p1', expectedVersion: '7' },
            { productId: 'p2', expectedVersion: '7' },
            { productId: 'p3', expectedVersion: '7' },
        ])

        // The user grows the selection while the request is pending; this may
        // not alter the in-flight request or its result association.
        selectRowByName('Delta Roast')
        selectRowByName('Epsilon Odd')
        expect(bulkActivateMock).toHaveBeenCalledTimes(1)

        await act(async () => {
            deferred.resolve(bulkAggregate({ requested: 3, accepted: 3, processed: 2, unchanged: 1 }))
        })

        await waitFor(() => expect(screen.getByText('Products activated')).toBeTruthy())
        // Counts stay tied to the A/B/C snapshot, not the grown selection.
        expect(screen.getByText('Selected: 3')).toBeTruthy()
        expect(screen.getByText('Activated: 2')).toBeTruthy()
        expect(screen.getByText('Already active: 1')).toBeTruthy()
        expect(screen.queryByText('Selected: 5')).toBeNull()
        expect(bulkActivateMock).toHaveBeenCalledTimes(1)
    })

    it('uses deactivate-specific wording distinguishing deactivated from already inactive', async () => {
        bulkDeactivateMock.mockResolvedValueOnce(bulkAggregate({
            requested: 2,
            accepted: 2,
            processed: 1,
            unchanged: 1,
            results: [
                bulkItem({ productId: 'p1', status: 'processed' }),
                bulkItem({ productId: 'p2', status: 'unchanged', action: 'deactivate', code: 'ALREADY_INACTIVE', newVersion: '7' }),
            ],
        }))
        await renderLoaded()
        selectRowByName('Alpha Tea')
        selectRowByName('Beta Coffee')

        fireEvent.click(toolbarButton('products.bulkActionDeactivate'))

        expect(bulkDeactivateMock).toHaveBeenCalledTimes(1)
        expect(bulkDeactivateMock.mock.calls[0][0]).toEqual([
            { productId: 'p1', expectedVersion: '7' },
            { productId: 'p2', expectedVersion: '7' },
        ])
        await waitFor(() => expect(screen.getByText('Products deactivated')).toBeTruthy())
        expect(screen.getByText('Selected: 2')).toBeTruthy()
        expect(screen.getByText('Deactivated: 1')).toBeTruthy()
        expect(screen.getByText('Already inactive: 1')).toBeTruthy()
        expect(screen.getByText('Beta Coffee is already inactive.')).toBeTruthy()
    })

    it('keeps a successful mutation outcome separate when the products list fails to refresh afterwards', async () => {
        bulkActivateMock.mockResolvedValueOnce(bulkAggregate())
        await renderLoaded()
        selectRowByName('Alpha Tea')

        apiGetMock.mockImplementation((url: string) => {
            if (url === '/categories') return Promise.resolve({ data: [] })
            if (url === '/products') return Promise.reject(new Error('refresh transport exploded'))
            return Promise.resolve({ data: [] })
        })
        fireEvent.click(toolbarButton('products.bulkActionActivate'))

        await waitFor(() => expect(screen.getByText('Products activated')).toBeTruthy())
        await waitFor(() => expect(screen.getByText('The operation completed, but the products list could not be refreshed.')).toBeTruthy())
        expect(screen.getByText('Selected: 1')).toBeTruthy()
        expect(screen.getByText('Activated: 1')).toBeTruthy()
        // The refresh problem never relabels the mutation as failed.
        expect(screen.queryByText('The bulk operation could not be completed. Please try again later.')).toBeNull()
        expect(bulkActivateMock).toHaveBeenCalledTimes(1)
    })

    it.each([
        [400, 'This bulk request was not valid. Please review the selection and try again.'],
        [403, 'You do not have permission to perform this bulk action.'],
        [409, 'This bulk request conflicts with another change. Please review the products and try again.'],
    ])('normalizes HTTP %i request failures into one safe localized category', async (status, expectedMessage) => {
        bulkActivateMock.mockRejectedValueOnce(Object.assign(new Error(`raw internal detail for ${status}`), { status }))
        await renderLoaded()
        selectRowByName('Alpha Tea')

        fireEvent.click(toolbarButton('products.bulkActionActivate'))

        await waitFor(() => expect(screen.getByText(expectedMessage)).toBeTruthy())
        expect(screen.getByText('The bulk request could not be completed')).toBeTruthy()
        expect(document.body.textContent ?? '').not.toContain(`raw internal detail for ${status}`)
        expect(bulkActivateMock).toHaveBeenCalledTimes(1)
    })

    it.each([500, 503])('treats an Activate %s response as transport-uncertain for every action, never a definite failure', async (status) => {
        bulkActivateMock.mockRejectedValueOnce(Object.assign(new Error('Internal service detail.'), { status }))
        await renderLoaded()
        selectRowByName('Alpha Tea')
        const callsBeforeRefetch = productCalls().length

        fireEvent.click(toolbarButton('products.bulkActionActivate'))

        await waitFor(() => expect(screen.getByText('The bulk request could not be confirmed')).toBeTruthy())
        expect(screen.getByText('The request could not be confirmed and may still be processing. Refresh the product list before retrying; the operation will not repeat automatically.')).toBeTruthy()
        // Neither definite completion nor definite failure wording may appear.
        expect(screen.queryByText('The bulk operation could not be completed. Please try again later.')).toBeNull()
        expect(screen.queryByText('Products activated')).toBeNull()
        expect(document.body.textContent ?? '').not.toContain('Internal service detail.')

        const panel = screen.getByText('The bulk request could not be confirmed').closest('div.p-3')!
        expect(panel.className).toContain('bg-amber-50')
        expect(panel.className).not.toContain('bg-green-50')
        await waitFor(() => expect(productCalls().length).toBeGreaterThan(callsBeforeRefetch))
        await act(async () => {
            await Promise.resolve()
            await Promise.resolve()
        })
        expect(bulkActivateMock).toHaveBeenCalledTimes(1)
    })

    it('maps network failures to cross-action uncertainty without claiming completion or leaking internals', async () => {
        bulkDeactivateMock.mockRejectedValueOnce(Object.assign(new Error('Network unavailable.'), { status: null, code: 'NETWORK_ERROR' }))
        await renderLoaded()
        selectRowByName('Alpha Tea')

        fireEvent.click(toolbarButton('products.bulkActionDeactivate'))

        await waitFor(() => expect(screen.getByText('The bulk request could not be confirmed')).toBeTruthy())
        expect(screen.getByText('The request could not be confirmed and may still be processing. Refresh the product list before retrying; the operation will not repeat automatically.')).toBeTruthy()
        const panel = screen.getByText('The bulk request could not be confirmed').closest('div.p-3')!
        expect(panel.textContent).not.toMatch(/deactivated\b/i)
        expect(panel.textContent).not.toMatch(/deactivate failed/i)
        expect(document.body.textContent ?? '').not.toContain('Network unavailable.')
        expect(panel.className).not.toContain('bg-green-50')
        expect(bulkDeactivateMock).toHaveBeenCalledTimes(1)
    })

    it('dismisses the terminal result summary on demand', async () => {
        bulkActivateMock.mockResolvedValueOnce(bulkAggregate())
        await renderLoaded()
        selectRowByName('Alpha Tea')

        fireEvent.click(toolbarButton('products.bulkActionActivate'))
        await waitFor(() => expect(screen.getByText('Products activated')).toBeTruthy())

        fireEvent.click(screen.getByText('Dismiss'))

        expect(screen.queryByText('Products activated')).toBeNull()
        expect(screen.queryByText('Selected: 1')).toBeNull()
    })
})

describe('Products bulk Publish/Unpublish actions (Phase 6G.4)', () => {
    const publishButton = () => toolbarButton('products.bulkActionPublish')
    const unpublishButton = () => toolbarButton('products.bulkActionUnpublish')

    async function openPublishConfirmation(names: string[]) {
        await renderLoaded()
        for (const name of names) selectRowByName(name)
        fireEvent.click(publishButton())
        await waitFor(() => expect(confirmMock).toHaveBeenCalledTimes(1))
    }

    async function openUnpublishConfirmation(names: string[]) {
        await renderLoaded()
        for (const name of names) selectRowByName(name)
        fireEvent.click(unpublishButton())
        await waitFor(() => expect(confirmMock).toHaveBeenCalledTimes(1))
    }

    function confirmationOptions() {
        return confirmMock.mock.calls[confirmMock.mock.calls.length - 1][0] as {
            title: string
            description: string
            confirmLabel: string
        }
    }

    it('opens a Publish confirmation with the exact selection count, publication intent, rejection warning and no auto-activation claim', async () => {
        // Hold the dialog open so the flow cannot proceed past confirmation.
        confirmMock.mockReturnValueOnce(new Promise<boolean>(() => {}))
        await openPublishConfirmation(['Alpha Tea', 'Beta Coffee'])

        const options = confirmationOptions()
        expect(options.title).toBe('Publish 2 selected product(s)?')
        expect(options.description).toContain('published storefront')
        expect(options.description).toMatch(/inactive or not ready for publication may be rejected/i)
        expect(options.description).toMatch(/nothing will be activated automatically/i)

        // Nothing starts while the dialog is merely open.
        expect(bulkPublishMock).not.toHaveBeenCalled()
    })

    it('opens an Unpublish confirmation that removes from the storefront, keeps products active and never uses delete/archive/deactivate as the operation meaning', async () => {
        confirmMock.mockReturnValueOnce(new Promise<boolean>(() => {}))
        await openUnpublishConfirmation(['Alpha Tea', 'Beta Coffee'])

        const options = confirmationOptions()
        expect(options.title).toBe('Unpublish 2 selected product(s)?')
        expect(options.description).toContain('removed from the published storefront')
        expect(options.description).toMatch(/will not be deactivated/)
        expect(options.confirmLabel).toBe('products.bulkActionUnpublish')
        // The operation meaning is unpublish — not delete/archive; the only
        // deactivate mention is the explicit negation above.
        expect(/\b(delete|archive)\b/i.test(`${options.title} ${options.description}`)).toBe(false)
        expect(bulkUnpublishMock).not.toHaveBeenCalled()
    })

    it('makes canceling the Publish confirmation perform zero mutation calls and allows reopening afterwards', async () => {
        confirmMock.mockResolvedValueOnce(false)
        await openPublishConfirmation(['Alpha Tea'])

        await act(async () => {
            await Promise.resolve()
            await Promise.resolve()
        })
        expect(bulkPublishMock).not.toHaveBeenCalled()
        expect(screen.queryByText(/Publishing \d+ selected product/)).toBeNull()

        // The confirmation lock was released: a second click re-opens and can proceed.
        bulkPublishMock.mockResolvedValueOnce(bulkAggregate())
        fireEvent.click(publishButton())
        await waitFor(() => expect(confirmMock).toHaveBeenCalledTimes(2))
        await waitFor(() => expect(bulkPublishMock).toHaveBeenCalledTimes(1))
    })

    it('confirms into exactly one publish orchestration carrying the immutable A/B/C versions and announces processing under a busy toolbar', async () => {
        const deferred = createDeferred<ReturnType<typeof bulkAggregate>>()
        bulkPublishMock.mockReturnValueOnce(deferred.promise)
        await renderLoaded({
            products: [
                makeProduct({ id: 'p1', name: 'Alpha Tea', rowVersion: '201' }),
                makeProduct({ id: 'p2', name: 'Beta Coffee', salePrice: 20, warehouseStocks: [{ quantity: 3 }], rowVersion: '202' }),
                makeProduct({ id: 'p3', name: 'Gamma Blend', salePrice: 30, hasVariants: true, rowVersion: '203' }),
            ],
        })
        for (const name of ['Alpha Tea', 'Beta Coffee', 'Gamma Blend']) selectRowByName(name)
        const callsBeforeRefetch = productCalls().length

        fireEvent.click(publishButton())
        await waitFor(() => expect(bulkPublishMock).toHaveBeenCalledTimes(1))

        expect(confirmMock).toHaveBeenCalledTimes(1)
        expect(bulkPublishMock.mock.calls[0]).toHaveLength(1)
        expect(bulkPublishMock.mock.calls[0][0]).toEqual([
            { productId: 'p1', expectedVersion: '201' },
            { productId: 'p2', expectedVersion: '202' },
            { productId: 'p3', expectedVersion: '203' },
        ])
        expect(screen.getByText('Publishing 3 selected product(s)…')).toBeTruthy()
        const toolbar = screen.getByText('products.productsSelected').closest('[aria-busy]')
        expect(toolbar?.getAttribute('aria-busy')).toBe('true')

        await act(async () => {
            deferred.resolve(bulkAggregate({ requested: 3, accepted: 3, processed: 3 }))
        })

        await waitFor(() => expect(screen.getByText('Products published')).toBeTruthy())
        expect(screen.getByText('Selected: 3')).toBeTruthy()
        expect(screen.getByText('Published: 3')).toBeTruthy()
        expect(screen.getByText('Already published: 0')).toBeTruthy()
        expect(screen.getByText('Failed: 0')).toBeTruthy()
        await waitFor(() => expect(productCalls().length).toBeGreaterThan(callsBeforeRefetch))
        await waitFor(() => expect(publishButton().disabled).toBe(true))
        expect(bulkPublishMock).toHaveBeenCalledTimes(1)
    })

    it('guards rapid duplicate Publish clicks into one dialog and exactly one API call', async () => {
        bulkPublishMock.mockImplementationOnce(() => createDeferred<ReturnType<typeof bulkAggregate>>().promise)
        await renderLoaded()
        selectRowByName('Alpha Tea')

        fireEvent.click(publishButton())
        fireEvent.click(publishButton())

        expect(confirmMock).toHaveBeenCalledTimes(1)
        await waitFor(() => expect(bulkPublishMock).toHaveBeenCalledTimes(1))
        expect(screen.getByText('Publishing 1 selected product(s)…')).toBeTruthy()
    })

    it('guards rapid duplicate Unpublish clicks into one API call through the shared mutating lock', async () => {
        bulkUnpublishMock.mockImplementationOnce(() => createDeferred<ReturnType<typeof bulkAggregate>>().promise)
        await renderLoaded()
        selectRowByName('Alpha Tea')

        fireEvent.click(unpublishButton())
        await waitFor(() => expect(bulkUnpublishMock).toHaveBeenCalledTimes(1))

        fireEvent.click(unpublishButton())
        fireEvent.click(unpublishButton())

        expect(bulkUnpublishMock).toHaveBeenCalledTimes(1)
        expect(confirmMock).toHaveBeenCalledTimes(1)
    })

    it('shares isBulkMutating across actions so no other bulk control can fire while Publish is pending', async () => {
        bulkPublishMock.mockReturnValueOnce(createDeferred<ReturnType<typeof bulkAggregate>>().promise)
        await renderLoaded()
        selectRowByName('Alpha Tea')

        fireEvent.click(publishButton())
        await waitFor(() => expect(bulkPublishMock).toHaveBeenCalledTimes(1))

        expect(publishButton().disabled).toBe(true)
        expect(unpublishButton().disabled).toBe(true)
        expect(toolbarButton('products.bulkActionActivate').disabled).toBe(true)
        expect(toolbarButton('products.bulkActionDeactivate').disabled).toBe(true)
        expect(toolbarButton('products.bulkActionDelete').disabled).toBe(true)

        fireEvent.click(unpublishButton())
        fireEvent.click(toolbarButton('products.bulkActionDeactivate'))
        expect(bulkUnpublishMock).not.toHaveBeenCalled()
        expect(bulkDeactivateMock).not.toHaveBeenCalled()
    })

    it('classifies an all-unchanged HTTP 200 Publish aggregate under the shared neutral headline, never failure or action success', async () => {
        bulkPublishMock.mockResolvedValueOnce(bulkAggregate({
            requested: 2,
            accepted: 2,
            processed: 0,
            unchanged: 2,
            results: [
                bulkItem({ productId: 'p1', status: 'unchanged', action: 'publish', code: 'ALREADY_PUBLISHED', newVersion: null }),
                bulkItem({ productId: 'p2', status: 'unchanged', action: 'publish', code: 'ALREADY_PUBLISHED', newVersion: null }),
            ],
        }))
        await renderLoaded()
        selectRowByName('Alpha Tea')
        selectRowByName('Beta Coffee')
        const callsBeforeRefetch = productCalls().length

        fireEvent.click(publishButton())

        await waitFor(() => expect(screen.getByText('No products were changed')).toBeTruthy())
        expect(screen.queryByText('Selected products are already published')).toBeNull()
        expect(screen.getByText('Published: 0')).toBeTruthy()
        expect(screen.getByText('Already published: 2')).toBeTruthy()
        expect(screen.getByText('Failed: 0')).toBeTruthy()
        expect(screen.getByText('Alpha Tea is already published.')).toBeTruthy()
        expect(screen.getByText('Beta Coffee is already published.')).toBeTruthy()

        const panel = screen.getByText('No products were changed').closest('div.p-3')
        expect(panel?.className).not.toContain('bg-red-100')
        expect(panel?.className).toContain('bg-blue-50')
        await waitFor(() => expect(productCalls().length).toBeGreaterThan(callsBeforeRefetch))
    })

    it('reports mixed Publish outcomes as partial success with amber tone and per-item reasons', async () => {
        bulkPublishMock.mockResolvedValueOnce(bulkAggregate({
            requested: 3,
            accepted: 3,
            processed: 2,
            unchanged: 0,
            failed: 1,
            results: [
                bulkItem({ productId: 'p1', status: 'processed' }),
                bulkItem({
                    productId: 'p2',
                    status: 'failed',
                    action: 'publish',
                    code: 'CONCURRENCY_CONFLICT',
                    message: 'Product was modified concurrently by another user.',
                }),
                bulkItem({ productId: 'p3', status: 'processed' }),
            ],
        }))
        await renderLoaded()
        selectRowByName('Alpha Tea')
        selectRowByName('Beta Coffee')
        selectRowByName('Gamma Blend')

        fireEvent.click(publishButton())

        await waitFor(() => expect(screen.getByText('Publish completed, but some products were not changed')).toBeTruthy())
        expect(screen.getByText('Published: 2')).toBeTruthy()
        expect(screen.getByText('Failed: 1')).toBeTruthy()
        expect(screen.getByText('Beta Coffee changed before the operation completed.')).toBeTruthy()
        expect(screen.queryByText(/modified concurrently by another user/i)).toBeNull()

        const panel = screen.getByText('Publish completed, but some products were not changed').closest('div.p-3')
        expect(panel?.className).toContain('bg-amber-50')
    })

    it('reports an all-failed Unpublish aggregate as failure with red tone while still refetching once and never retrying', async () => {
        bulkUnpublishMock.mockResolvedValueOnce(bulkAggregate({
            requested: 2,
            accepted: 2,
            processed: 0,
            unchanged: 0,
            failed: 2,
            results: [
                bulkItem({ productId: 'p1', status: 'failed', action: 'unpublish', code: 'NOT_FOUND', previousVersion: null, newVersion: null }),
                bulkItem({ productId: 'p2', status: 'failed', action: 'unpublish', code: 'NOT_FOUND', previousVersion: null, newVersion: null }),
            ],
        }))
        await renderLoaded()
        selectRowByName('Alpha Tea')
        selectRowByName('Beta Coffee')
        const callsBeforeRefetch = productCalls().length

        fireEvent.click(unpublishButton())

        await waitFor(() => expect(screen.getByText('No products were changed')).toBeTruthy())
        expect(screen.getByText('Unpublished: 0')).toBeTruthy()
        expect(screen.getByText('Already unpublished: 0')).toBeTruthy()
        expect(screen.getByText('Failed: 2')).toBeTruthy()
        expect(screen.getByText('Alpha Tea is no longer available.')).toBeTruthy()
        const panel = screen.getByText('No products were changed').closest('div.p-3')
        expect(panel?.className).toContain('bg-red-100')

        await waitFor(() => expect(productCalls().length).toBeGreaterThan(callsBeforeRefetch))
        await act(async () => {
            await Promise.resolve()
            await Promise.resolve()
        })
        expect(bulkUnpublishMock).toHaveBeenCalledTimes(1)
    })

    it.each([
        ['PRODUCT_INACTIVE', 'Product is inactive and cannot be published.'],
        ['PUBLICATION_NOT_READY', 'Product is not ready for publication.'],
        ['VALIDATION_FAILED', 'Alpha Tea could not be processed.'],
        ['CONCURRENCY_CONFLICT', 'Alpha Tea changed before the operation completed.'],
        ['NOT_FOUND', 'Alpha Tea is no longer available.'],
    ])('maps publish per-item %s to the safe localized reason without raw diagnostics', async (code, expected) => {
        bulkPublishMock.mockResolvedValueOnce(bulkAggregate({
            requested: 1,
            accepted: 1,
            processed: 0,
            unchanged: 0,
            failed: 1,
            results: [
                bulkItem({
                    productId: 'p1',
                    status: 'failed',
                    action: 'publish',
                    code,
                    message: `Raw diagnostic for ${code}: System.Exception at Npgsql.Products.Select`,
                }),
            ],
        }))
        await renderLoaded()
        selectRowByName('Alpha Tea')

        fireEvent.click(publishButton())

        await waitFor(() => expect(screen.getByText(expected)).toBeTruthy())
        if (code === 'PRODUCT_INACTIVE' || code === 'PUBLICATION_NOT_READY') {
            expect(screen.getByText('Alpha Tea:')).toBeTruthy()
        }
        const bodyText = document.body.textContent ?? ''
        expect(bodyText).not.toContain(`Raw diagnostic for ${code}`)
        expect(bodyText).not.toContain('System.Exception')
        expect(bodyText).not.toContain('Npgsql')
        expect(bulkPublishMock).toHaveBeenCalledTimes(1)
    })

    it('states the inactive publish rejection exactly and implies no automatic activation anywhere', async () => {
        bulkPublishMock.mockResolvedValueOnce(bulkAggregate({
            requested: 1,
            accepted: 1,
            processed: 0,
            unchanged: 0,
            failed: 1,
            results: [
                bulkItem({
                    productId: 'p1',
                    status: 'failed',
                    action: 'publish',
                    code: 'PRODUCT_INACTIVE',
                    message: 'Product cannot be published; activation pipeline transition Draft->Published rejected.',
                }),
            ],
        }))
        await renderLoaded()
        selectRowByName('Alpha Tea')

        fireEvent.click(publishButton())

        expect(await screen.findByText('Product is inactive and cannot be published.')).toBeTruthy()
        const bodyText = document.body.textContent ?? ''
        expect(bodyText).not.toMatch(/activat(e|ed|ion) automatically/i)
        expect(bodyText).not.toMatch(/will be activated/i)
        expect(bodyText).not.toContain('activation pipeline')
    })

    it('falls back safely for unknown publish item codes without exposing internals', async () => {
        bulkPublishMock.mockResolvedValueOnce(bulkAggregate({
            requested: 1,
            accepted: 1,
            processed: 0,
            unchanged: 0,
            failed: 1,
            results: [
                bulkItem({
                    productId: 'p1',
                    status: 'failed',
                    action: 'publish',
                    code: 'FUTURE_UNKNOWN_CODE',
                    message: 'NpgsqlException: select * from System.Products',
                }),
            ],
        }))
        await renderLoaded()
        selectRowByName('Alpha Tea')

        fireEvent.click(publishButton())

        await waitFor(() => expect(screen.getByText('No products were changed')).toBeTruthy())
        expect(screen.getByText('Alpha Tea could not be changed.')).toBeTruthy()
        const bodyText = document.body.textContent ?? ''
        expect(bodyText).not.toContain('FUTURE_UNKNOWN_CODE')
        expect(bodyText).not.toContain('Npgsql')
        expect(bodyText).not.toContain('select * from')
    })

    it('maps unpublish ALREADY_UNPUBLISHED to a neutral unchanged reason under the shared headline outside any failure tone', async () => {
        bulkUnpublishMock.mockResolvedValueOnce(bulkAggregate({
            requested: 1,
            accepted: 1,
            processed: 0,
            unchanged: 1,
            results: [
                bulkItem({ productId: 'p1', status: 'unchanged', action: 'unpublish', code: 'ALREADY_UNPUBLISHED', newVersion: null }),
            ],
        }))
        await renderLoaded()
        selectRowByName('Alpha Tea')

        fireEvent.click(unpublishButton())

        await waitFor(() => expect(screen.getByText('No products were changed')).toBeTruthy())
        expect(screen.getByText('Alpha Tea is already unpublished.')).toBeTruthy()
        const panel = screen.getByText('No products were changed').closest('div.p-3')
        expect(panel?.className).not.toContain('bg-red-100')
    })

    it.each([
        ['VALIDATION_FAILED', 'Beta Coffee could not be processed.'],
        ['CONCURRENCY_CONFLICT', 'Beta Coffee changed before the operation completed.'],
        ['NOT_FOUND', 'Beta Coffee is no longer available.'],
        ['PRODUCT_INACTIVE', 'Beta Coffee could not be changed.'],
    ])('maps unpublish per-item %s to the safe localized reason', async (code, expected) => {
        bulkUnpublishMock.mockResolvedValueOnce(bulkAggregate({
            requested: 1,
            accepted: 1,
            processed: 0,
            unchanged: 0,
            failed: 1,
            results: [
                bulkItem({ productId: 'p2', status: 'failed', action: 'unpublish', code, message: `Raw diagnostic for ${code}` }),
            ],
        }))
        await renderLoaded()
        selectRowByName('Beta Coffee')

        fireEvent.click(unpublishButton())

        await waitFor(() => expect(screen.getByText(expected)).toBeTruthy())
        expect(document.body.textContent ?? '').not.toContain(`Raw diagnostic for ${code}`)
    })

    it('keeps unpublish result wording free of deactivated/archive/delete operation meanings', async () => {
        bulkUnpublishMock.mockResolvedValueOnce(bulkAggregate({
            requested: 1,
            accepted: 1,
            processed: 1,
            results: [bulkItem({ productId: 'p1', status: 'processed', action: 'unpublish' })],
        }))
        await renderLoaded()
        selectRowByName('Alpha Tea')

        fireEvent.click(unpublishButton())

        await waitFor(() => expect(screen.getByText('Products unpublished')).toBeTruthy())
        expect(screen.getByText('Unpublished: 1')).toBeTruthy()
        const panel = screen.getByText('Products unpublished').closest('div.p-3')!
        expect(panel.textContent).not.toMatch(/deactivated|archived|\bdeleted\b/i)
    })

    it('keeps the pending A/B/C publish payload intact when the selection grows mid-flight and starts no second request', async () => {
        const deferred = createDeferred<ReturnType<typeof bulkAggregate>>()
        bulkPublishMock.mockReturnValueOnce(deferred.promise)
        await renderLoaded()
        selectRowByName('Alpha Tea')
        selectRowByName('Beta Coffee')
        selectRowByName('Gamma Blend')

        fireEvent.click(publishButton())
        await waitFor(() => expect(bulkPublishMock).toHaveBeenCalledTimes(1))
        expect(bulkPublishMock.mock.calls[0][0]).toEqual([
            { productId: 'p1', expectedVersion: '7' },
            { productId: 'p2', expectedVersion: '7' },
            { productId: 'p3', expectedVersion: '7' },
        ])

        // Selection checkboxes are locked during mutation, but the snapshot
        // must stay authoritative regardless.
        selectRowByName('Delta Roast')
        selectRowByName('Epsilon Odd')
        expect(bulkPublishMock).toHaveBeenCalledTimes(1)

        await act(async () => {
            deferred.resolve(bulkAggregate({ requested: 3, accepted: 3, processed: 2, unchanged: 1 }))
        })

        await waitFor(() => expect(screen.getByText('Products published')).toBeTruthy())
        expect(screen.getByText('Selected: 3')).toBeTruthy()
        expect(screen.queryByText('Selected: 5')).toBeNull()
        expect(bulkPublishMock).toHaveBeenCalledTimes(1)
    })

    it('resolves per-item reasons from the pre-mutation name snapshot even when the product disappears after the canonical refetch', async () => {
        const deferred = createDeferred<ReturnType<typeof bulkAggregate>>()
        bulkPublishMock.mockReturnValueOnce(deferred.promise)
        await renderLoaded({
            products: [makeProduct({ id: 'p1', name: 'Alpha Tea', rowVersion: '301' })],
        })
        selectRowByName('Alpha Tea')

        fireEvent.click(publishButton())
        await waitFor(() => expect(bulkPublishMock).toHaveBeenCalledTimes(1))

        apiGetMock.mockImplementation((url: string) => {
            if (url === '/categories') return Promise.resolve({ data: [] })
            if (url === '/products') {
                return Promise.resolve({
                    data: [],
                    headers: { 'x-total-count': '0', 'x-total-pages': '0' },
                })
            }
            return Promise.resolve({ data: [] })
        })

        await act(async () => {
            deferred.resolve(bulkAggregate({
                requested: 1,
                accepted: 1,
                processed: 0,
                unchanged: 0,
                failed: 1,
                results: [
                    bulkItem({ productId: 'p1', status: 'failed', action: 'publish', code: 'NOT_FOUND', previousVersion: null, newVersion: null }),
                ],
            }))
        })

        await waitFor(() => expect(screen.getByText('Alpha Tea is no longer available.')).toBeTruthy())
        expect(screen.queryByText('Product is no longer available.')).toBeNull()
        expect(document.body.textContent ?? '').not.toContain('bulkUnknownProduct')
    })

    it('keeps a successful publish separate from an action-specific refresh-failure warning and never retries', async () => {
        bulkPublishMock.mockResolvedValueOnce(bulkAggregate())
        await renderLoaded()
        selectRowByName('Alpha Tea')

        apiGetMock.mockImplementation((url: string) => {
            if (url === '/categories') return Promise.resolve({ data: [] })
            if (url === '/products') return Promise.reject(new Error('refresh transport exploded'))
            return Promise.resolve({ data: [] })
        })
        fireEvent.click(publishButton())

        await waitFor(() => expect(screen.getByText('Products published')).toBeTruthy())
        await waitFor(() => expect(screen.getByText('Publishing completed, but the product list could not be refreshed.')).toBeTruthy())
        expect(screen.getByText('Selected: 1')).toBeTruthy()
        expect(screen.getByText('Published: 1')).toBeTruthy()
        expect(screen.queryByText('The operation completed, but the products list could not be refreshed.')).toBeNull()
        expect(screen.queryByText('The bulk operation could not be completed. Please try again later.')).toBeNull()
        await act(async () => {
            await Promise.resolve()
            await Promise.resolve()
        })
        expect(bulkPublishMock).toHaveBeenCalledTimes(1)
    })

    it('keeps a successful unpublish separate from its action-specific refresh-failure warning and never retries', async () => {
        bulkUnpublishMock.mockResolvedValueOnce(bulkAggregate())
        await renderLoaded()
        selectRowByName('Alpha Tea')

        apiGetMock.mockImplementation((url: string) => {
            if (url === '/categories') return Promise.resolve({ data: [] })
            if (url === '/products') return Promise.reject(new Error('refresh transport exploded'))
            return Promise.resolve({ data: [] })
        })
        fireEvent.click(unpublishButton())

        await waitFor(() => expect(screen.getByText('Products unpublished')).toBeTruthy())
        await waitFor(() => expect(screen.getByText('Unpublishing completed, but the product list could not be refreshed.')).toBeTruthy())
        expect(screen.getByText('Unpublished: 1')).toBeTruthy()
        expect(screen.queryByText('The operation completed, but the products list could not be refreshed.')).toBeNull()
        await act(async () => {
            await Promise.resolve()
            await Promise.resolve()
        })
        expect(bulkUnpublishMock).toHaveBeenCalledTimes(1)
    })

    it('clears the selection and refetches canonically after a terminal publish aggregate', async () => {
        bulkPublishMock.mockResolvedValueOnce(bulkAggregate({ requested: 2, accepted: 2, processed: 2 }))
        await renderLoaded()
        selectRowByName('Alpha Tea')
        selectRowByName('Beta Coffee')
        const callsBeforeRefetch = productCalls().length

        fireEvent.click(publishButton())

        await waitFor(() => expect(screen.getByText('Products published')).toBeTruthy())
        await waitFor(() => expect(productCalls().length).toBeGreaterThan(callsBeforeRefetch))
        await waitFor(() => expect(publishButton().disabled).toBe(true))
        expect(unpublishButton().disabled).toBe(true)
        await waitFor(() => expect(rowCheckboxes().every((cb) => !(cb as HTMLInputElement).checked)).toBe(true))
    })
})

describe('Products bulk Delete action (Phase 6G.5)', () => {
    const deleteButton = () => toolbarButton('products.bulkActionDelete')

    function lastConfirmationOptions() {
        return confirmActionMock.mock.calls[confirmActionMock.mock.calls.length - 1][0] as {
            title: string
            description: string
            confirmLabel: string
            cancelLabel: string
            variant?: string
            awaitAction?: boolean
        }
    }

    async function openDeleteConfirmation(names: string[]) {
        await renderLoaded()
        for (const name of names) selectRowByName(name)
        fireEvent.click(deleteButton())
        await waitFor(() => expect(confirmActionMock).toHaveBeenCalledTimes(1))
    }

    it('opens a destructive confirmation with the exact count and conservative soft-delete/dependency/best-effort copy, dispatching nothing while open', async () => {
        // Hold the dialog open so the flow cannot proceed past confirmation.
        confirmMock.mockReturnValueOnce(new Promise<boolean>(() => {}))
        await openDeleteConfirmation(['Alpha Tea', 'Beta Coffee'])

        const options = lastConfirmationOptions()
        expect(options.title).toBe('Delete 2 selected product(s)?')
        expect(options.description).toContain('removed from active product operations')
        expect(options.description).toMatch(/blocking dependencies/i)
        expect(options.description).toMatch(/best-effort per product/i)
        expect(options.description).not.toMatch(/permanent|erased|physically|destroyed|database/i)
        expect(options.confirmLabel).toBe('products.bulkActionDelete')
        expect(options.variant).toBe('danger')
        expect(options.awaitAction).toBe(true)

        expect(bulkDeleteMock).not.toHaveBeenCalled()
    })

    it('never reaches the bulk service directly from the page: only startBulkMutation may dispatch delete', async () => {
        bulkDeleteMock.mockResolvedValueOnce(bulkAggregate({ requested: 1, accepted: 1, processed: 1 }))
        await openDeleteConfirmation(['Alpha Tea'])

        // Exactly one orchestration entry carrying the awaited-action contract.
        expect(confirmActionMock).toHaveBeenCalledTimes(1)
        await waitFor(() => expect(bulkDeleteMock).toHaveBeenCalledTimes(1))
        expect(bulkDeleteMock.mock.calls[0]).toHaveLength(1)
    })

    it('canceling performs zero mutation calls and allows reopening and confirming into exactly one request', async () => {
        confirmMock.mockResolvedValueOnce(false)
        await openDeleteConfirmation(['Alpha Tea'])

        await act(async () => {
            await Promise.resolve()
            await Promise.resolve()
        })
        expect(bulkDeleteMock).not.toHaveBeenCalled()
        expect(screen.queryByText(/Deleting \d+ selected product/)).toBeNull()

        // The confirmation lock was released: a second click re-opens and confirms.
        const deferred = createDeferred<ReturnType<typeof bulkAggregate>>()
        bulkDeleteMock.mockReturnValueOnce(deferred.promise)
        fireEvent.click(deleteButton())
        await waitFor(() => expect(confirmActionMock).toHaveBeenCalledTimes(2))
        await waitFor(() => expect(bulkDeleteMock).toHaveBeenCalledTimes(1))

        await act(async () => {
            deferred.resolve(bulkAggregate({ requested: 1, accepted: 1, processed: 1 }))
        })
        await waitFor(() => expect(screen.getByText('Selected products deleted')).toBeTruthy())
        expect(bulkDeleteMock).toHaveBeenCalledTimes(1)
    })

    it('guards rapid duplicate Delete clicks into one dialog through the synchronous ref guard', async () => {
        confirmMock.mockReturnValueOnce(new Promise<boolean>(() => {}))
        await openDeleteConfirmation(['Alpha Tea'])

        fireEvent.click(deleteButton())

        expect(confirmActionMock).toHaveBeenCalledTimes(1)
        expect(bulkDeleteMock).not.toHaveBeenCalled()
    })

    it('confirms into exactly one immutable A/B/C payload, announces processing under a busy toolbar, and locks every control', async () => {
        const deferred = createDeferred<ReturnType<typeof bulkAggregate>>()
        bulkDeleteMock.mockReturnValueOnce(deferred.promise)
        await renderLoaded({
            products: [
                makeProduct({ id: 'p1', name: 'Alpha Tea', rowVersion: '401' }),
                makeProduct({ id: 'p2', name: 'Beta Coffee', salePrice: 20, warehouseStocks: [{ quantity: 3 }], rowVersion: '402' }),
                makeProduct({ id: 'p3', name: 'Gamma Blend', salePrice: 30, hasVariants: true, rowVersion: '403' }),
            ],
        })
        for (const name of ['Alpha Tea', 'Beta Coffee', 'Gamma Blend']) selectRowByName(name)
        const callsBeforeRefetch = productCalls().length

        fireEvent.click(deleteButton())
        await waitFor(() => expect(bulkDeleteMock).toHaveBeenCalledTimes(1))

        expect(confirmActionMock).toHaveBeenCalledTimes(1)
        // Exact immutable snapshot captured at confirmed start — later
        // selection/version changes cannot alter it.
        expect(bulkDeleteMock.mock.calls[0][0]).toEqual([
            { productId: 'p1', expectedVersion: '401' },
            { productId: 'p2', expectedVersion: '402' },
            { productId: 'p3', expectedVersion: '403' },
        ])

        expect(screen.getByText('Deleting 3 selected product(s)…')).toBeTruthy()
        const toolbar = screen.getByText('products.productsSelected').closest('[aria-busy]')
        expect(toolbar?.getAttribute('aria-busy')).toBe('true')
        for (const label of [
            'products.bulkActionActivate',
            'products.bulkActionDeactivate',
            'products.bulkActionPublish',
            'products.bulkActionUnpublish',
            'products.bulkActionDelete',
        ]) {
            expect(toolbarButton(label).disabled, `${label} must be locked while delete is pending`).toBe(true)
        }
        fireEvent.click(deleteButton())
        expect(bulkDeleteMock).toHaveBeenCalledTimes(1)

        await act(async () => {
            deferred.resolve(bulkAggregate({ requested: 3, accepted: 3, processed: 3 }))
        })

        await waitFor(() => expect(screen.getByText('Selected products deleted')).toBeTruthy())
        await waitFor(() => expect(productCalls().length).toBeGreaterThan(callsBeforeRefetch))
        expect(bulkDeleteMock).toHaveBeenCalledTimes(1)
    })

    it('reports an all-processed delete as Deleted counts without any permanent-erasure claims', async () => {
        bulkDeleteMock.mockResolvedValueOnce(bulkAggregate({ requested: 7, accepted: 7, processed: 7 }))
        await renderLoaded()
        for (const name of ['Alpha Tea', 'Beta Coffee', 'Gamma Blend', 'Delta Roast', 'Epsilon Odd']) selectRowByName(name)

        fireEvent.click(deleteButton())

        await waitFor(() => expect(screen.getByText('Selected products deleted')).toBeTruthy())
        expect(screen.getByText('Selected: 7')).toBeTruthy()
        expect(screen.getByText('Deleted: 7')).toBeTruthy()
        expect(screen.getByText('Unchanged: 0')).toBeTruthy()
        expect(screen.getByText('Failed: 0')).toBeTruthy()

        const panel = screen.getByText('Selected products deleted').closest('div.p-3')!
        expect(panel.textContent).not.toMatch(/permanently|erased|physically|destroyed|history/i)
    })

    it('identifies the dependency-blocked product from the pre-mutation snapshot even when every row disappears after refetch', async () => {
        const deferred = createDeferred<ReturnType<typeof bulkAggregate>>()
        bulkDeleteMock.mockReturnValueOnce(deferred.promise)
        await renderLoaded()
        for (const name of ['Alpha Tea', 'Beta Coffee', 'Gamma Blend']) selectRowByName(name)
        const callsBeforeRefetch = productCalls().length

        fireEvent.click(deleteButton())
        await waitFor(() => expect(bulkDeleteMock).toHaveBeenCalledTimes(1))

        // Canonical refetch returns an empty catalog: names must survive from
        // the pre-mutation capture.
        apiGetMock.mockImplementation((url: string) => {
            if (url === '/categories') return Promise.resolve({ data: [] })
            if (url === '/products') {
                return Promise.resolve({
                    data: [],
                    headers: { 'x-total-count': '0', 'x-total-pages': '0' },
                })
            }
            return Promise.resolve({ data: [] })
        })

        await act(async () => {
            deferred.resolve(bulkAggregate({
                requested: 3,
                accepted: 3,
                processed: 2,
                unchanged: 0,
                failed: 1,
                results: [
                    bulkItem({ productId: 'p1', status: 'processed' }),
                    bulkItem({
                        productId: 'p2',
                        status: 'failed',
                        action: 'delete',
                        code: 'DEPENDENCY_BLOCKED',
                        message: 'FK constraint violation on dbo.OrderLines referencing Products table',
                    }),
                    bulkItem({ productId: 'p3', status: 'processed' }),
                ],
            }))
        })

        await waitFor(() => expect(screen.getByText('Delete completed, but some products were not changed')).toBeTruthy())
        expect(screen.getByText('Selected: 3')).toBeTruthy()
        expect(screen.getByText('Deleted: 2')).toBeTruthy()
        expect(screen.getByText('Unchanged: 0')).toBeTruthy()
        expect(screen.getByText('Failed: 1')).toBeTruthy()
        expect(screen.getByText('Beta Coffee cannot be deleted because it has blocking dependencies.')).toBeTruthy()

        const bodyText = document.body.textContent ?? ''
        expect(bodyText).not.toContain('FK constraint')
        expect(bodyText).not.toContain('OrderLines')
        expect(bodyText).not.toContain('dbo.')

        await waitFor(() => expect(productCalls().length).toBeGreaterThan(callsBeforeRefetch))
        await waitFor(() => expect(deleteButton().disabled).toBe(true))
        expect(rowCheckboxes().every((cb) => !(cb as HTMLInputElement).checked)).toBe(true)
        expect(bulkDeleteMock).toHaveBeenCalledTimes(1)
    })

    it('reports an all-failed delete aggregate as No products were changed with safe reasons while still refetching once', async () => {
        bulkDeleteMock.mockResolvedValueOnce(bulkAggregate({
            requested: 2,
            accepted: 2,
            processed: 0,
            unchanged: 0,
            failed: 2,
            results: [
                bulkItem({ productId: 'p1', status: 'failed', action: 'delete', code: 'DEPENDENCY_BLOCKED', previousVersion: null, newVersion: null }),
                bulkItem({ productId: 'p2', status: 'failed', action: 'delete', code: 'CONCURRENCY_CONFLICT', previousVersion: null, newVersion: null }),
            ],
        }))
        await renderLoaded()
        selectRowByName('Alpha Tea')
        selectRowByName('Beta Coffee')
        const callsBeforeRefetch = productCalls().length

        fireEvent.click(deleteButton())

        await waitFor(() => expect(screen.getByText('No products were changed')).toBeTruthy())
        expect(screen.getByText('Deleted: 0')).toBeTruthy()
        expect(screen.getByText('Unchanged: 0')).toBeTruthy()
        expect(screen.getByText('Failed: 2')).toBeTruthy()
        expect(screen.getByText('Alpha Tea cannot be deleted because it has blocking dependencies.')).toBeTruthy()
        expect(screen.getByText('Beta Coffee changed before deletion. Refresh the product list and try again.')).toBeTruthy()
        const panel = screen.getByText('No products were changed').closest('div.p-3')
        expect(panel?.className).toContain('bg-red-100')

        await waitFor(() => expect(productCalls().length).toBeGreaterThan(callsBeforeRefetch))
        await act(async () => {
            await Promise.resolve()
            await Promise.resolve()
        })
        expect(bulkDeleteMock).toHaveBeenCalledTimes(1)
    })

    it.each([
        ['DEPENDENCY_BLOCKED', 'Beta Coffee cannot be deleted because it has blocking dependencies.'],
        ['CONCURRENCY_CONFLICT', 'Beta Coffee changed before deletion. Refresh the product list and try again.'],
        ['NOT_FOUND', 'Beta Coffee is no longer available.'],
        ['REQUEST_CANCELLED', 'Beta Coffee was not processed because the bulk operation was cancelled.'],
        ['IDEMPOTENCY_CONFLICT', 'Beta Coffee could not be deleted because this delete request conflicts with an earlier request.'],
        ['IDEMPOTENCY_IN_PROGRESS', 'Beta Coffee is already being deleted by a request still in progress.'],
    ])('maps delete per-item %s to the safe localized reason without raw diagnostics or retries', async (code, expected) => {
        bulkDeleteMock.mockResolvedValueOnce(bulkAggregate({
            requested: 1,
            accepted: 1,
            processed: 0,
            unchanged: 0,
            failed: 1,
            results: [
                bulkItem({
                    productId: 'p2',
                    status: 'failed',
                    action: 'delete',
                    code,
                    message: `Raw diagnostic for ${code}: System.Data.SqlClient exception, IdempotencyKey=secret-key claim lease 42`,
                }),
            ],
        }))
        await renderLoaded()
        selectRowByName('Beta Coffee')

        fireEvent.click(deleteButton())

        await waitFor(() => expect(screen.getByText(expected)).toBeTruthy())
        const bodyText = document.body.textContent ?? ''
        expect(bodyText).not.toContain(`Raw diagnostic for ${code}`)
        expect(bodyText).not.toContain('System.Data')
        expect(bodyText).not.toContain('IdempotencyKey')
        expect(bodyText).not.toContain('secret-key')
        expect(bodyText).not.toContain('claim lease')
        expect(bulkDeleteMock).toHaveBeenCalledTimes(1)
    })

    it('reports a cancelled-but-unchanged item outside any failure tone without counting it as success', async () => {
        bulkDeleteMock.mockResolvedValueOnce(bulkAggregate({
            requested: 1,
            accepted: 1,
            processed: 0,
            unchanged: 1,
            failed: 0,
            results: [
                bulkItem({ productId: 'p2', status: 'unchanged', action: 'delete', code: 'REQUEST_CANCELLED', newVersion: null }),
            ],
        }))
        await renderLoaded()
        selectRowByName('Beta Coffee')

        fireEvent.click(deleteButton())

        await waitFor(() => expect(screen.getByText('No products were changed')).toBeTruthy())
        expect(screen.getByText('Deleted: 0')).toBeTruthy()
        expect(screen.getByText('Unchanged: 1')).toBeTruthy()
        expect(screen.getByText('Failed: 0')).toBeTruthy()
        expect(screen.getByText('Beta Coffee was not processed because the bulk operation was cancelled.')).toBeTruthy()
        const panel = screen.getByText('No products were changed').closest('div.p-3')
        expect(panel?.className).not.toContain('bg-red-100')
        expect(panel?.className).toContain('bg-blue-50')
    })

    it('treats transport-ambiguous delete failures as unconfirmed uncertainty with exactly one request, a canonical refetch and selection clearing', async () => {
        bulkDeleteMock.mockRejectedValueOnce(Object.assign(new Error('Network unavailable.'), { status: null }))
        await renderLoaded()
        selectRowByName('Alpha Tea')
        const callsBeforeRefetch = productCalls().length

        fireEvent.click(deleteButton())

        await waitFor(() => expect(screen.getByText('Deletion could not be confirmed')).toBeTruthy())
        expect(screen.getByText('The delete request could not be confirmed and may still be processing. Refresh the product list before retrying; the deletion will not repeat automatically.')).toBeTruthy()

        // Neither definite success nor a definite delete failure may appear.
        const panel = screen.getByText('Deletion could not be confirmed').closest('div.p-3')!
        expect(panel.textContent).not.toMatch(/\bdelete failed\b/i)
        expect(panel.textContent).not.toMatch(/products deleted\b/i)
        expect(document.body.textContent ?? '').not.toContain('Network unavailable.')
        expect(panel.className).not.toContain('bg-green-50')

        await waitFor(() => expect(productCalls().length).toBeGreaterThan(callsBeforeRefetch))
        await waitFor(() => expect(deleteButton().disabled).toBe(true))
        expect(rowCheckboxes().every((cb) => !(cb as HTMLInputElement).checked)).toBe(true)
        await act(async () => {
            await Promise.resolve()
            await Promise.resolve()
        })
        expect(bulkDeleteMock).toHaveBeenCalledTimes(1)
    })

    it.each([500, 503])('treats a Delete %s response as uncertain and never automatically retries', async (status) => {
        bulkDeleteMock.mockRejectedValueOnce(Object.assign(new Error('Internal service detail.'), { status }))
        await renderLoaded()
        selectRowByName('Alpha Tea')

        fireEvent.click(deleteButton())

        await waitFor(() => expect(screen.getByText('Deletion could not be confirmed')).toBeTruthy())
        expect(screen.getByText('The delete request could not be confirmed and may still be processing. Refresh the product list before retrying; the deletion will not repeat automatically.')).toBeTruthy()
        expect(document.body.textContent ?? '').not.toContain('Internal service detail.')
        expect(screen.queryByText('The bulk operation could not be completed. Please try again later.')).toBeNull()
        await act(async () => {
            await Promise.resolve()
            await Promise.resolve()
        })
        expect(bulkDeleteMock).toHaveBeenCalledTimes(1)
    })

    it.each([
        ['IDEMPOTENCY_CONFLICT', 'This deletion request conflicts with an earlier request. Refresh the product list before trying again.'],
        ['IDEMPOTENCY_IN_PROGRESS', 'A deletion request is already in progress. Refresh the product list before trying again.'],
    ])('maps request-level %s to safe Delete messaging without retry or claim details', async (code, expectedMessage) => {
        bulkDeleteMock.mockRejectedValueOnce(Object.assign(new Error('claim=42 lease-token=secret'), {
            status: 409,
            code,
        }))
        await renderLoaded()
        selectRowByName('Alpha Tea')

        fireEvent.click(deleteButton())

        await waitFor(() => expect(screen.getByText(expectedMessage)).toBeTruthy())
        expect(document.body.textContent ?? '').not.toMatch(/claim=|lease-token|secret/)
        await act(async () => {
            await Promise.resolve()
            await Promise.resolve()
        })
        expect(bulkDeleteMock).toHaveBeenCalledTimes(1)
    })

    it('keeps a successful deletion separate from its delete-specific refresh-failure warning and never retries', async () => {
        bulkDeleteMock.mockResolvedValueOnce(bulkAggregate({ requested: 1, accepted: 1, processed: 1 }))
        await renderLoaded()
        selectRowByName('Alpha Tea')

        apiGetMock.mockImplementation((url: string) => {
            if (url === '/categories') return Promise.resolve({ data: [] })
            if (url === '/products') return Promise.reject(new Error('refresh transport exploded'))
            return Promise.resolve({ data: [] })
        })
        fireEvent.click(deleteButton())

        await waitFor(() => expect(screen.getByText('Selected products deleted')).toBeTruthy())
        await waitFor(() => expect(screen.getByText('Deletion completed, but the product list could not be refreshed.')).toBeTruthy())
        expect(screen.getByText('Selected: 1')).toBeTruthy()
        expect(screen.getByText('Deleted: 1')).toBeTruthy()
        expect(screen.queryByText('The operation completed, but the products list could not be refreshed.')).toBeNull()
        expect(screen.queryByText('The bulk operation could not be completed. Please try again later.')).toBeNull()
        await act(async () => {
            await Promise.resolve()
            await Promise.resolve()
        })
        expect(bulkDeleteMock).toHaveBeenCalledTimes(1)
    })

    it('clears the selection and refetches canonically after a terminal concurrency-conflicted delete aggregate without resubmitting versions', async () => {
        bulkDeleteMock.mockResolvedValueOnce(bulkAggregate({
            requested: 1,
            accepted: 1,
            processed: 0,
            unchanged: 0,
            failed: 1,
            results: [
                bulkItem({
                    productId: 'p1',
                    status: 'failed',
                    action: 'delete',
                    code: 'CONCURRENCY_CONFLICT',
                    previousVersion: null,
                    newVersion: null,
                }),
            ],
        }))
        await renderLoaded()
        selectRowByName('Alpha Tea')
        const callsBeforeRefetch = productCalls().length

        fireEvent.click(deleteButton())

        await waitFor(() => expect(screen.getByText('No products were changed')).toBeTruthy())
        await waitFor(() => expect(productCalls().length).toBeGreaterThan(callsBeforeRefetch))
        await waitFor(() => expect(deleteButton().disabled).toBe(true))
        expect(rowCheckboxes().every((cb) => !(cb as HTMLInputElement).checked)).toBe(true)

        // The conflict resolution path is refresh-and-retry-by-the-user only:
        // no automatic version fetch or resubmission ever happens.
        await act(async () => {
            await Promise.resolve()
            await Promise.resolve()
        })
        expect(bulkDeleteMock).toHaveBeenCalledTimes(1)
    })

    it('keeps the pending delete snapshot intact when the selection grows mid-flight', async () => {
        const deferred = createDeferred<ReturnType<typeof bulkAggregate>>()
        bulkDeleteMock.mockReturnValueOnce(deferred.promise)
        await renderLoaded()
        selectRowByName('Alpha Tea')

        fireEvent.click(deleteButton())
        await waitFor(() => expect(bulkDeleteMock).toHaveBeenCalledTimes(1))
        expect(bulkDeleteMock.mock.calls[0][0]).toEqual([
            { productId: 'p1', expectedVersion: '7' },
        ])

        selectRowByName('Beta Coffee')
        selectRowByName('Gamma Blend')
        expect(bulkDeleteMock).toHaveBeenCalledTimes(1)

        await act(async () => {
            deferred.resolve(bulkAggregate({ requested: 1, accepted: 1, processed: 1 }))
        })
        await waitFor(() => expect(screen.getByText('Selected products deleted')).toBeTruthy())
        expect(screen.queryByText('Deleted: 3')).toBeNull()
        expect(bulkDeleteMock).toHaveBeenCalledTimes(1)
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

    it('keeps every bulk-action translation present with matching interpolation placeholders', () => {
        const en = JSON.parse(readFileSync(join(currentDir, '..', 'i18n', 'locales', 'en.json'), 'utf8'))
        const ar = JSON.parse(readFileSync(join(currentDir, '..', 'i18n', 'locales', 'ar.json'), 'utf8'))
        const bulkKeys = Object.keys(en.products).filter((key) => key.startsWith('bulk'))

        expect(bulkKeys.length).toBeGreaterThan(0)
        expect(Object.keys(ar.products).filter((key) => key.startsWith('bulk')).sort()).toEqual(bulkKeys.sort())

        for (const key of bulkKeys) {
            expect(en.products[key], `missing en products.${key}`).toBeTruthy()
            expect(ar.products[key], `missing ar products.${key}`).toBeTruthy()
            const enPlaceholders = String(en.products[key]).match(/{{[^}]+}}/g) || []
            const arPlaceholders = String(ar.products[key]).match(/{{[^}]+}}/g) || []
            expect(arPlaceholders.sort(), `placeholder mismatch for products.${key}`).toEqual(enPlaceholders.sort())
        }
    })

    it('adds the Phase 6G.4 publish/unpublish strings to both locales with exact placeholder parity', () => {
        const en = JSON.parse(readFileSync(join(currentDir, '..', 'i18n', 'locales', 'en.json'), 'utf8'))
        const ar = JSON.parse(readFileSync(join(currentDir, '..', 'i18n', 'locales', 'ar.json'), 'utf8'))

        const publishUnpublishKeys = [
            'bulkProcessingPublish',
            'bulkProcessingUnpublish',
            'bulkSuccessTitlePublish',
            'bulkSuccessTitleUnpublish',
            'bulkUnchangedTitlePublish',
            'bulkUnchangedTitleUnpublish',
            'bulkCountProcessedPublish',
            'bulkCountProcessedUnpublish',
            'bulkCountUnchangedPublish',
            'bulkCountUnchangedUnpublish',
            'bulkItemAlreadyPublished',
            'bulkItemAlreadyUnpublished',
            'bulkItemInactiveCannotPublish',
            'bulkItemPublicationNotReady',
            'bulkItemProductName',
            'bulkPublishConfirmTitle',
            'bulkPublishConfirmDescription',
            'bulkUnpublishConfirmTitle',
            'bulkUnpublishConfirmDescription',
            'bulkRefreshErrorNoticePublish',
            'bulkRefreshErrorNoticeUnpublish',
        ]

        const placeholders = (value: string) => (value.match(/{{[^}]+}}/g) || []).sort().join(',')
        for (const key of publishUnpublishKeys) {
            expect(en.products[key], `en products.${key} missing`).toBeTruthy()
            expect(ar.products[key], `ar products.${key} missing`).toBeTruthy()
            expect(placeholders(String(ar.products[key])), `placeholder mismatch for products.${key}`)
                .toBe(placeholders(String(en.products[key])))
        }

        // Exact semantic anchors required by Phase 6G.4.
        expect(en.products.bulkItemInactiveCannotPublish).toBe('Product is inactive and cannot be published.')
        expect(en.products.bulkPublishConfirmDescription).toMatch(/nothing will be activated automatically/i)
        expect(en.products.bulkUnpublishConfirmDescription).toMatch(/will not be deactivated/)
        expect(en.products.bulkUnpublishConfirmDescription).toContain('removed from the published storefront')
        expect(en.products.bulkRefreshErrorNoticePublish).toBe('Publishing completed, but the product list could not be refreshed.')
    })

    it('adds the Phase 6G.5 delete strings to both locales with exact placeholder parity', () => {
        const en = JSON.parse(readFileSync(join(currentDir, '..', 'i18n', 'locales', 'en.json'), 'utf8'))
        const ar = JSON.parse(readFileSync(join(currentDir, '..', 'i18n', 'locales', 'ar.json'), 'utf8'))

        const deleteKeys = [
            'bulkProcessingDelete',
            'bulkSuccessTitleDelete',
            'bulkUnchangedTitleDelete',
            'bulkCountProcessedDelete',
            'bulkCountUnchangedDelete',
            'bulkItemDependencyBlocked',
            'bulkItemDeleteConcurrencyConflict',
            'bulkItemRequestCancelled',
            'bulkItemIdempotencyConflict',
            'bulkItemIdempotencyInProgress',
            'bulkDeleteConfirmTitle',
            'bulkDeleteConfirmDescription',
            'bulkRefreshErrorNoticeDelete',
            'bulkDeleteUncertainTitle',
            'bulkDeleteUncertainMessage',
            'bulkDeleteIdempotencyConflictMessage',
            'bulkDeleteIdempotencyInProgressMessage',
        ]

        const placeholders = (value: string) => (value.match(/{{[^}]+}}/g) || []).sort().join(',')
        for (const key of deleteKeys) {
            expect(en.products[key], `en products.${key} missing`).toBeTruthy()
            expect(ar.products[key], `ar products.${key} missing`).toBeTruthy()
            expect(placeholders(String(ar.products[key])), `placeholder mismatch for products.${key}`)
                .toBe(placeholders(String(en.products[key])))
        }

        // Exact semantic anchors required by Phase 6G.5: conservative
        // soft-delete copy, dependency warning, best-effort per-item framing,
        // and uncertainty wording for transport-ambiguous failures.
        expect(en.products.bulkSuccessTitleDelete).toBe('Selected products deleted')
        expect(en.products.bulkCountProcessedDelete).toBe('Deleted: {{count}}')
        expect(en.products.bulkDeleteConfirmTitle).toBe('Delete {{count}} selected product(s)?')
        expect(en.products.bulkDeleteConfirmDescription).toContain('removed from active product operations')
        expect(en.products.bulkDeleteConfirmDescription).toMatch(/blocking dependencies/i)
        expect(en.products.bulkDeleteConfirmDescription).toMatch(/best-effort per product/i)
        expect(String(en.products.bulkDeleteConfirmDescription)).not.toMatch(/permanent|erased|physically|destroy|database/i)
        expect(en.products.bulkRefreshErrorNoticeDelete)
            .toBe('Deletion completed, but the product list could not be refreshed.')
        expect(en.products.bulkDeleteUncertainTitle).toBe('Deletion could not be confirmed')
        expect(en.products.bulkDeleteUncertainMessage).toMatch(/may still be processing/i)
        expect(en.products.bulkDeleteUncertainMessage).toMatch(/refresh the product list before retrying/i)
    })
})

describe('Products shared bulk result integration (Phase 6G.6)', () => {
    it('routes Activate through the one shared passive result region with accepted counts, status labels and per-item reasons', async () => {
        bulkActivateMock.mockResolvedValueOnce(bulkAggregate({
            requested: 3,
            accepted: 3,
            processed: 1,
            unchanged: 1,
            failed: 1,
            results: [
                bulkItem({ productId: 'p1', status: 'processed' }),
                bulkItem({ productId: 'p2', status: 'unchanged', action: 'activate', code: 'ALREADY_ACTIVE', newVersion: '7' }),
                bulkItem({ productId: 'p3', status: 'failed', action: 'activate', code: 'CONCURRENCY_CONFLICT' }),
            ],
        }))
        await renderLoaded()
        for (const name of ['Alpha Tea', 'Beta Coffee', 'Gamma Blend']) selectRowByName(name)

        fireEvent.click(toolbarButton('products.bulkActionActivate'))

        const panel = await waitFor(() => {
            const el = screen.getByTestId('bulk-mutation-result')
            expect(within(el).getByRole('status').getAttribute('aria-live')).toBe('polite')
            return el
        })
        const region = within(panel).getByRole('status')

        // One coherent polite live region owns the concise summary; per-item
        // details stay visible right below it without being announced whole.
        expect(document.querySelectorAll('[role="status"]')).toHaveLength(1)
        expect(within(region).getByText('Activate completed, but some products were not changed')).toBeTruthy()
        expect(within(region).getByText('Selected: 3')).toBeTruthy()
        expect(within(region).getByText('Accepted: 3')).toBeTruthy()
        expect(within(region).getByText('Activated: 1')).toBeTruthy()
        // Explicit non-color status labels accompany each item row.
        expect(within(panel).getByText('Unchanged')).toBeTruthy()
        expect(within(panel).getByText('Failed')).toBeTruthy()
        expect(within(panel).getByText('Beta Coffee is already active.')).toBeTruthy()
        expect(within(panel).getByText('Gamma Blend changed before the operation completed.')).toBeTruthy()

        // The dismiss control stays passive: only the reset callback fires.
        fireEvent.click(screen.getByRole('button', { name: 'Dismiss' }))
        await waitFor(() => expect(screen.queryByTestId('bulk-mutation-result')).toBeNull())
        expect(bulkActivateMock).toHaveBeenCalledTimes(1)
    })

    it('generalizes request-level idempotency refusals beyond Delete with safe shared wording (Publish)', async () => {
        bulkPublishMock.mockRejectedValueOnce(Object.assign(new Error('claim=42 lease-token=secret'), {
            status: 409,
            code: 'IDEMPOTENCY_CONFLICT',
        }))
        await renderLoaded()
        selectRowByName('Alpha Tea')

        fireEvent.click(toolbarButton('products.bulkActionPublish'))

        await waitFor(() => expect(screen.getByText('This bulk request conflicts with an earlier request. Refresh the product list before trying again.')).toBeTruthy())
        expect(screen.getByText('The bulk request needs attention')).toBeTruthy()
        expect(screen.queryByText('No products were changed')).toBeNull()
        // The specific safe idempotency wording replaces the generic 409 text
        // and never exposes the key or transport internals; no auto-retry.
        expect(screen.queryByText('This bulk request conflicts with another change. Please review the products and try again.')).toBeNull()
        expect(document.body.textContent ?? '').not.toMatch(/claim=|lease-token|secret/)
        await act(async () => {
            await Promise.resolve()
            await Promise.resolve()
        })
        expect(bulkPublishMock).toHaveBeenCalledTimes(1)
    })

    it('treats local BULK_* validation codes as a definite start failure for a non-delete action, never uncertain', async () => {
        await renderLoaded({
            products: [makeProduct({ id: 'p1', name: 'Alpha Tea', rowVersion: '' })],
        })
        selectRowByName('Alpha Tea')

        fireEvent.click(toolbarButton('products.bulkActionDeactivate'))

        await waitFor(() => expect(screen.getByText('The bulk operation could not be started.')).toBeTruthy())
        expect(screen.queryByText(/could not be confirmed/i)).toBeNull()
        expect(bulkDeactivateMock).not.toHaveBeenCalled()
    })
})

describe('Products RTL, responsive and localized summary hardening (Phase 6G.7)', () => {
    it('renders the whole page right-to-left for Arabic with every bulk action reachable and Delete available', async () => {
        rtlMockState.current = { isRTL: true, dir: 'rtl', locale: 'ar-EG' }
        await renderLoaded()

        const pageRoot = screen.getByText('products.title').closest('div[dir="rtl"]')
        expect(pageRoot).toBeTruthy()

        selectRowByName('Alpha Tea')

        const actionsGroup = screen.getByRole('group', { name: 'products.bulkActionsLabel' })
        for (const label of [
            'products.bulkActionActivate',
            'products.bulkActionDeactivate',
            'products.bulkActionPublish',
            'products.bulkActionUnpublish',
            'products.bulkActionDelete',
        ]) {
            const button = within(actionsGroup).getByText(label).closest('button') as HTMLButtonElement
            expect(button).toBeTruthy()
            expect(button.disabled).toBe(false)
        }

        // Delete stays a first-class available action under Arabic direction.
        bulkDeleteMock.mockResolvedValueOnce(bulkAggregate())
        fireEvent.click(within(actionsGroup).getByText('products.bulkActionDelete').closest('button')!)
        expect(confirmActionMock).toHaveBeenCalledTimes(1)

        // The Product Feed entry point survives the RTL pass untouched.
        expect(screen.getByText('productFeed.title')).toBeTruthy()
    })

    it('summarizes sorting through certified locale keys instead of raw missing sort keys', async () => {
        await renderLoaded()

        // Re-query per click: the sortable header is re-created each render,
        // so the original button node is detached after the first toggle.
        const priceSortButton = () => within(screen.getByText('Price').closest('th')!).getByRole('button')
        fireEvent.click(priceSortButton())
        fireEvent.click(priceSortButton())

        expect(screen.getByText('Sort: Price · Descending')).toBeTruthy()
        expect(document.body.textContent ?? '').not.toContain('products.sortPrice')
    })

    it('wraps the header and pagination rows at narrow widths using logical spacing only', async () => {
        await renderLoaded()
        selectRowByName('Alpha Tea')
        expect(toolbarButton('products.bulkActionActivate').disabled).toBe(false)

        const source = readFileSync(join(dirname(fileURLToPath(import.meta.url)), 'Products.jsx'), 'utf8')

        // Page header and pagination footer wrap instead of overflowing.
        expect(source).toMatch(/mb-6 flex flex-wrap items-center justify-between gap-3/)
        expect(source).toMatch(/mt-4 flex flex-wrap items-center justify-between gap-3 border-t border-gray-200 pt-4/)
        expect(source).toMatch(/flex min-w-0 flex-wrap items-center gap-x-4 gap-y-1/)
        expect(source).toMatch(/max-w-xs px-6 py-4 align-top/)
        expect(source).toMatch(/break-words text-sm font-medium text-gray-900/)
        // The selection toolbar keeps its wrapping layout.
        expect(source).toMatch(/mb-4 flex flex-wrap items-center justify-between gap-3 rounded-lg/)
        // No physical directional spacing utilities anywhere on the page.
        expect(source).not.toMatch(/[\s"'`](ml|mr|pl|pr)-\d/)
    })
})
