import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
    EXPORT_SCOPE_CURRENT_VIEW,
    EXPORT_SCOPE_SELECTED,
    buildProductExportFilename,
    buildProductExportRows,
    getProductsForExport,
} from './productExport'
import { exportProductsToExcel } from './productExcelExport'

// ---- xlsx module double ----------------------------------------------------
// Verifies workbook construction + filename without depending on binary
// file internals (kept deliberately non-brittle per phase requirements).
const { writeFileSpy } = vi.hoisted(() => ({ writeFileSpy: vi.fn() }))

interface MockWorkbook {
    SheetNames: string[]
    Sheets: Record<string, { aoa?: unknown[][] }>
    Props?: Record<string, unknown>
}

vi.mock('xlsx', () => ({
    utils: {
        aoa_to_sheet: (aoa: unknown[][]) => ({ aoa }),
        book_new: (): MockWorkbook => ({ SheetNames: [], Sheets: {} }),
        book_append_sheet: (wb: MockWorkbook, ws: { aoa?: unknown[][] }, name: string) => {
            wb.SheetNames.push(name)
            wb.Sheets[name] = ws
        },
        encode_cell: ({ r, c }: { r: number; c: number }) => `R${r}C${c}`,
    },
    writeFile: writeFileSpy,
}))

// ---- i18n / currency doubles ------------------------------------------------
const translations: Record<string, string> = {
    'products.product': 'Product',
    'products.sku': 'SKU',
    'products.type': 'Type',
    'products.category': 'Category',
    'products.price': 'Price',
    'products.stock': 'Stock',
    'products.stockState': 'Stock State',
    'products.status': 'Status',
    'products.publication': 'Publication',
    'products.title': 'Products',
    'products.managedByVariants': 'Managed by variants',
    'products.inStock': 'In Stock',
    'products.lowStock': 'Low Stock',
    'products.outOfStock': 'Out of Stock',
    'products.active': 'Active',
    'products.inactive': 'Inactive',
    'products.draft': 'Draft',
    'products.published': 'Published',
    'products.archived': 'Archived',
    'products.simpleProductType': 'Simple Product',
    'products.variantProductType': 'Variant Product',
    'products.exportReportTitle': 'Products Report',
    'products.exportScope': 'Export scope',
    'products.exportSelected': 'Selected Products',
    'products.exportCurrentView': 'Current View',
}

const t = (key: string) => translations[key] ?? key
const formatCurrency = (value: unknown) =>
    (value === null || value === undefined ? '' : `${Number(value).toFixed(2)} EGP`)

// Mirrors the enriched rows produced by Products.jsx (totalStock, stockState
// and normalizedPublishStatus are derived there and consumed by the export).
const makeProduct = (overrides: Record<string, unknown> = {}) => ({
    id: 'p-1',
    name: 'Coffee',
    sku: 'COF-1',
    salePrice: 25.5,
    trackStock: true,
    hasVariants: false,
    totalStock: 10,
    stockState: 'inStock',
    normalizedPublishStatus: 1,
    isActive: true,
    categoryName: 'Drinks',
    ...overrides,
})

beforeEach(() => {
    writeFileSpy.mockClear()
})

// ---- Dataset selection ------------------------------------------------------
describe('getProductsForExport', () => {
    const sortedProducts = [
        makeProduct({ id: 'a' }),
        makeProduct({ id: 'b' }),
        makeProduct({ id: 'c' }),
    ]

    it('Current View export contains exactly the visible (sorted) rows, in order', () => {
        const result = getProductsForExport({
            scope: EXPORT_SCOPE_CURRENT_VIEW,
            sortedProducts,
            selectedProductIds: [],
        })

        expect(result.map((p: { id: string }) => p.id)).toEqual(['a', 'b', 'c'])
    })

    it('Selected export contains only selected visible IDs, preserving view order', () => {
        const result = getProductsForExport({
            scope: EXPORT_SCOPE_SELECTED,
            sortedProducts,
            selectedProductIds: ['c', 'a'],
        })

        expect(result.map((p: { id: string }) => p.id)).toEqual(['a', 'c'])
    })

    it('never leaks hidden/stale selected IDs from another page', () => {
        const result = getProductsForExport({
            scope: EXPORT_SCOPE_SELECTED,
            sortedProducts,
            selectedProductIds: ['a', 'stale-id-from-another-page'],
        })

        expect(result.map((p: { id: string }) => p.id)).toEqual(['a'])
    })

    it('Selected export is empty when nothing is selected', () => {
        const result = getProductsForExport({
            scope: EXPORT_SCOPE_SELECTED,
            sortedProducts,
            selectedProductIds: [],
        })

        expect(result).toEqual([])
    })

    it('is defensive against missing inputs', () => {
        expect(
            getProductsForExport({ scope: EXPORT_SCOPE_CURRENT_VIEW, sortedProducts: null, selectedProductIds: null }),
        ).toEqual([])
    })
})

// ---- Row mapping / business presentation -------------------------------------
describe('buildProductExportRows', () => {
    it('maps publication states (Published/Draft/Archived)', () => {
        const [published] = buildProductExportRows([makeProduct({ normalizedPublishStatus: 1 })], { t, formatCurrency })
        const [draft] = buildProductExportRows([makeProduct({ normalizedPublishStatus: 0 })], { t, formatCurrency })
        const [archived] = buildProductExportRows([makeProduct({ normalizedPublishStatus: 2 })], { t, formatCurrency })

        expect(published.publicationLabel).toBe('Published')
        expect(draft.publicationLabel).toBe('Draft')
        expect(archived.publicationLabel).toBe('Archived')
    })

    it('keeps the listing stock semantics for tracked simple products', () => {
        const [inStock] = buildProductExportRows([makeProduct({ totalStock: 10, stockState: 'inStock' })], { t, formatCurrency })
        const [lowStock] = buildProductExportRows([makeProduct({ totalStock: 2, stockState: 'lowStock' })], { t, formatCurrency })
        const [outOfStock] = buildProductExportRows([makeProduct({ totalStock: 0, stockState: 'outOfStock' })], { t, formatCurrency })

        expect(inStock.stockValue).toBe(10)
        expect(inStock.stockStateLabel).toBe('In Stock')
        expect(lowStock.stockStateLabel).toBe('Low Stock')
        expect(outOfStock.stockStateLabel).toBe('Out of Stock')
    })

    it('renders variant-managed products as "Managed by variants" with no stock state', () => {
        const [row] = buildProductExportRows(
            [makeProduct({ hasVariants: true, stockState: null, totalStock: 0 })],
            { t, formatCurrency },
        )

        expect(row.stockDisplay).toBe('Managed by variants')
        expect(row.stockValue).toBeNull()
        expect(row.stockStateLabel).toBe('')
        expect(row.typeLabel).toBe('Variant Product')
    })

    it('renders untracked products with the "—" placeholder and no stock state', () => {
        const [row] = buildProductExportRows(
            [makeProduct({ trackStock: false, stockState: 'untracked', totalStock: 0 })],
            { t, formatCurrency },
        )

        expect(row.stockDisplay).toBe('—')
        expect(row.stockValue).toBeNull()
        expect(row.stockStateLabel).toBe('—')
    })

    it('exports the numeric price alongside the formatted currency display', () => {
        const [row] = buildProductExportRows([makeProduct({ salePrice: 25.5 })], { t, formatCurrency })

        expect(row.priceValue).toBe(25.5)
        expect(row.priceDisplay).toBe('25.50 EGP')
    })

    it('falls back to "—" for missing name, SKU, category and price', () => {
        const [row] = buildProductExportRows(
            [makeProduct({ name: '', sku: '', categoryName: undefined, category: undefined, salePrice: null })],
            { t, formatCurrency },
        )

        expect(row.name).toBe('—')
        expect(row.sku).toBe('—')
        expect(row.category).toBe('—')
        expect(row.priceValue).toBeNull()
        expect(row.priceDisplay).toBe('—')
    })

    it('maps Active/Inactive status like the table', () => {
        const [active] = buildProductExportRows([makeProduct({ isActive: true })], { t, formatCurrency })
        const [inactive] = buildProductExportRows([makeProduct({ isActive: false })], { t, formatCurrency })

        expect(active.statusLabel).toBe('Active')
        expect(inactive.statusLabel).toBe('Inactive')
    })
})

// ---- Filenames ----------------------------------------------------------------
describe('buildProductExportFilename', () => {
    it('produces deterministic, scope-specific filenames', () => {
        expect(buildProductExportFilename({ format: 'xlsx', scope: EXPORT_SCOPE_CURRENT_VIEW }))
            .toBe('shopixy-products-current-view.xlsx')
        expect(buildProductExportFilename({ format: 'xlsx', scope: EXPORT_SCOPE_SELECTED }))
            .toBe('shopixy-products-selected.xlsx')
        expect(buildProductExportFilename({ format: 'pdf', scope: EXPORT_SCOPE_CURRENT_VIEW }))
            .toBe('shopixy-products-current-view.pdf')
        expect(buildProductExportFilename({ format: 'pdf', scope: EXPORT_SCOPE_SELECTED }))
            .toBe('shopixy-products-selected.pdf')
    })
})

// ---- Excel generation -----------------------------------------------------------
describe('exportProductsToExcel', () => {
    it('writes a real .xlsx workbook with the expected header and numeric cells', async () => {
        await exportProductsToExcel([makeProduct()], {
            scope: EXPORT_SCOPE_CURRENT_VIEW,
            t,
            formatCurrency,
            storeName: 'My Store',
        })

        expect(writeFileSpy).toHaveBeenCalledTimes(1)
        const [workbook, filename] = writeFileSpy.mock.calls[0]

        expect(filename).toBe('shopixy-products-current-view.xlsx')
        expect(workbook.SheetNames).toEqual(['Products'])

        const sheet = workbook.Sheets['Products']
        const [header, firstRow] = sheet.aoa as unknown[][]
        expect(header).toEqual([
            'Product', 'SKU', 'Type', 'Category', 'Price', 'Stock', 'Stock State', 'Status', 'Publication',
        ])
        expect(firstRow[0]).toBe('Coffee')
        expect(firstRow[4]).toBe(25.5) // numeric price, not a formatted string
        expect(firstRow[5]).toBe(10) // numeric stock
        expect(firstRow[7]).toBe('Active')
        expect(firstRow[8]).toBe('Published')

        // Report metadata lives in workbook properties, not in the data sheet.
        expect(workbook.Props?.Title).toBe('Products Report')
        expect(workbook.Props?.Author).toBe('My Store')
        expect(String(workbook.Props?.Subject)).toContain('Current View')
    })

    it('uses the selected-scope filename and metadata for Selected exports', async () => {
        await exportProductsToExcel([makeProduct()], {
            scope: EXPORT_SCOPE_SELECTED,
            t,
            formatCurrency,
            storeName: '',
        })

        const [workbook, filename] = writeFileSpy.mock.calls[0]
        expect(filename).toBe('shopixy-products-selected.xlsx')
        expect(String(workbook.Props?.Subject)).toContain('Selected Products')
        expect(workbook.Props?.Author).toBe('Shopixy')
    })
})
