import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { readFileSync } from 'fs'
import { resolve } from 'path'
import AffiliateProducts from './AffiliateProducts'

const listProductsMock = vi.hoisted(() => vi.fn())
const getCategoryTreeMock = vi.hoisted(() => vi.fn())
const settingsCardMock = vi.hoisted(() => vi.fn())
const confirmMock = vi.hoisted(() => vi.fn())

vi.mock('../../../services/adminProductAffiliateApi', () => ({
    default: {
        listProducts: listProductsMock,
        getCategoryTree: getCategoryTreeMock,
    },
}))

vi.mock('../../../components/ProductAffiliateSettingsCard', () => ({
    default: settingsCardMock,
}))

vi.mock('../../../common/confirm/ConfirmDialogProvider', () => ({
    useConfirmDialog: () => ({ confirm: confirmMock }),
}))

vi.mock('react-i18next', () => ({
    useTranslation: () => ({
        t: (key: string, options?: Record<string, unknown>) => {
            const labels: Record<string, string> = {
                'affiliateProducts.title': 'Affiliate Products',
                'affiliateProducts.description': 'Manage affiliate participation, categories, and commissions for your products.',
                'affiliateProducts.searchLabel': 'Search products',
                'affiliateProducts.searchPlaceholder': 'Search by product name...',
                'affiliateProducts.searchAction': 'Search',
                'affiliateProducts.clearSearch': 'Clear',
                'affiliateProducts.filters.affiliateStatus': 'Affiliate Status',
                'affiliateProducts.filters.productStatus': 'Product Status',
                'affiliateProducts.filters.all': 'All',
                'affiliateProducts.filters.enabled': 'Enabled',
                'affiliateProducts.filters.disabled': 'Disabled',
                'affiliateProducts.filters.allCategories': 'All categories',
                'affiliateProducts.filters.categoriesUnavailable': 'No categories available',
                'affiliateProducts.filters.clearFilters': 'Clear filters',
                'affiliateProducts.table.product': 'Product Name',
                'affiliateProducts.table.productStatus': 'Product Status',
                'affiliateProducts.table.affiliateStatus': 'Affiliate Status',
                'affiliateProducts.table.category': 'Affiliate Category',
                'affiliateProducts.table.commissionRate': 'Commission Rate',
                'affiliateProducts.table.maxCommission': 'Max Commission',
                'affiliateProducts.table.actions': 'Actions',
                'affiliateProducts.productStatusActive': 'Active',
                'affiliateProducts.productStatusInactive': 'Inactive',
                'affiliateProducts.affiliateStatusEnabled': 'Enabled',
                'affiliateProducts.affiliateStatusDisabled': 'Disabled',
                'affiliateProducts.categoryNone': 'Not assigned',
                'affiliateProducts.noCap': 'No cap',
                'affiliateProducts.error': 'Unable to load affiliate products.',
                'affiliateProducts.retry': 'Retry',
                'affiliateProducts.emptyTitle': 'No products yet',
                'affiliateProducts.emptyHint': 'Products appear here once they are created in the catalog.',
                'affiliateProducts.emptySearchTitle': 'No matching products',
                'affiliateProducts.emptySearchHint': 'Try adjusting your search to find a product.',
                'affiliateProducts.manage': 'Manage',
                'affiliateProducts.manageTitle': 'Affiliate settings',
                'affiliateProducts.closeManage': 'Close affiliate settings',
                'affiliateProducts.pagination.summary': 'Showing {{from}}–{{to}} of {{total}}',
                'affiliateProducts.pagination.pageInfo': 'Page {{page}} of {{totalPages}}',
                'affiliateProducts.pagination.previous': 'Previous',
                'affiliateProducts.pagination.next': 'Next',
                'affiliateSettingsCard.stayLabel': 'Stay',
                'affiliateSettingsCard.leaveLabel': 'Leave',
                'affiliateSettingsCard.unsavedDialogDescription': 'Leave without saving?',
                'affiliateSettingsCard.unsavedDialogTitle': 'Unsaved affiliate changes',
            }

            const template = labels[key] || key
            if (!options) return template

            return Object.entries(options).reduce(
                (acc, [name, value]) => acc.replaceAll(`{{${name}}}`, `${value}`),
                template,
            )
        },
    }),
}))

const baseListParams = {
    search: '',
    affiliateStatus: 'all',
    productStatus: 'all',
    categoryId: null,
}

const row = (overrides = {}) => ({
    productId: 'product-1',
    productName: 'Cotton Shirt',
    productNameAr: null,
    isProductActive: true,
    isAffiliateEnabled: false,
    commissionRate: null,
    maxCommissionPerItem: null,
    affiliateCategoryId: null,
    affiliateCategoryName: null,
    affiliateParentCategoryName: null,
    ...overrides,
})

function deferred<T>() {
    let resolve!: (value: T) => void
    let reject!: (reason?: unknown) => void
    const promise = new Promise<T>((res, rej) => {
        resolve = res
        reject = rej
    })
    return { promise, resolve, reject }
}

describe('Affiliate Products page', () => {
    beforeEach(() => {
        listProductsMock.mockReset()
        getCategoryTreeMock.mockReset()
        settingsCardMock.mockReset()
        confirmMock.mockReset()
        confirmMock.mockResolvedValue(true)
        getCategoryTreeMock.mockResolvedValue([])
        settingsCardMock.mockImplementation(({
            onSettingsChanged,
            onBusyChange,
            onDirtyChange,
            onCancel,
        }: {
            onSettingsChanged?: () => void
            onBusyChange?: (busy: boolean) => void
            onDirtyChange?: (dirty: boolean) => void
            onCancel?: () => void
        }) => (
            <div>
                <span>Affiliate settings card</span>
                <button type="button" onClick={() => onSettingsChanged?.()}>simulate-settings-change</button>
                <button type="button" onClick={() => onDirtyChange?.(true)}>simulate-dirty</button>
                <button type="button" onClick={() => onBusyChange?.(true)}>simulate-busy</button>
                <button type="button" onClick={() => onBusyChange?.(false)}>simulate-idle</button>
                <button type="button" onClick={() => onCancel?.()}>simulate-cancel</button>
            </div>
        ))
    })

    it('renders successful rows with authority-separated statuses and hierarchical category display', async () => {
        listProductsMock.mockResolvedValue({
            items: [
                row(),
                row({
                    productId: 'product-2',
                    productName: 'Legacy Lamp',
                    isProductActive: false,
                    isAffiliateEnabled: true,
                    commissionRate: 0.1,
                    maxCommissionPerItem: 25,
                    affiliateCategoryId: 'category-a',
                    affiliateCategoryName: 'Phones',
                    affiliateParentCategoryName: 'Electronics',
                }),
                row({
                    productId: 'product-3',
                    productName: 'Rooted Rug',
                    isAffiliateEnabled: true,
                    commissionRate: 0.05,
                    affiliateCategoryId: 'root-category',
                    affiliateCategoryName: 'Home',
                    affiliateParentCategoryName: null,
                }),
            ],
            totalCount: 3,
            totalPages: 1,
        })

        render(<AffiliateProducts />)

        expect(await screen.findByText('Cotton Shirt')).toBeInTheDocument()
        expect(screen.getByText('Legacy Lamp')).toBeInTheDocument()

        const cottonRow = screen.getByText('Cotton Shirt').closest('tr')!
        expect(cottonRow).toHaveTextContent('Active')
        expect(cottonRow).toHaveTextContent('Disabled')
        expect(cottonRow).toHaveTextContent('Not assigned')
        expect(cottonRow).toHaveTextContent('—')
        expect(cottonRow).toHaveTextContent('No cap')

        const legacyRow = screen.getByText('Legacy Lamp').closest('tr')!
        expect(legacyRow).toHaveTextContent('Inactive')
        expect(legacyRow).toHaveTextContent('Enabled')
        expect(legacyRow).toHaveTextContent('Electronics > Phones')
        expect(legacyRow).toHaveTextContent('10.00%')
        expect(legacyRow).toHaveTextContent('25.00')

        const rootedRow = screen.getByText('Rooted Rug').closest('tr')!
        expect(rootedRow).toHaveTextContent('Home')
        expect(rootedRow.textContent).not.toContain('>')

        expect(listProductsMock).toHaveBeenCalledWith(1, 20, baseListParams)
    })

    it('shows skeleton loading rows before data arrives', () => {
        listProductsMock.mockImplementation(() => new Promise(() => {}))

        render(<AffiliateProducts />)

        const tables = document.querySelectorAll('table tbody tr.animate-pulse')
        expect(tables.length).toBeGreaterThan(0)
        expect(screen.queryByText('Manage')).not.toBeInTheDocument()
    })

    it('shows an empty state without products and a search-aware empty state', async () => {
        listProductsMock.mockResolvedValue({ items: [], totalCount: 0, totalPages: 0 })

        const { container } = render(<AffiliateProducts />)

        expect(await screen.findByText('No products yet')).toBeInTheDocument()

        fireEvent.change(container.querySelector('#affiliate-products-search')!, {
            target: { value: 'nomatch' },
        })
        fireEvent.click(screen.getByRole('button', { name: 'Search' }))

        await waitFor(() => expect(listProductsMock).toHaveBeenCalledWith(
            1, 20, { ...baseListParams, search: 'nomatch' },
        ))
        expect(await screen.findByText('No matching products')).toBeInTheDocument()
    })

    it('shows a safe error state with retry that refetches', async () => {
        listProductsMock.mockRejectedValueOnce(new Error('network down'))

        render(<AffiliateProducts />)

        expect(await screen.findByRole('alert')).toHaveTextContent('Unable to load affiliate products.')

        listProductsMock.mockResolvedValue({
            items: [row()],
            totalCount: 1,
            totalPages: 1,
        })
        fireEvent.click(screen.getByRole('button', { name: 'Retry' }))

        expect(await screen.findByText('Cotton Shirt')).toBeInTheDocument()
        expect(listProductsMock).toHaveBeenCalledTimes(2)
    })

    it('performs server-owned search through the API boundary and supports clearing it', async () => {
        listProductsMock.mockResolvedValue({ items: [], totalCount: 0, totalPages: 0 })
        const { container } = render(<AffiliateProducts />)

        await screen.findByText('No products yet')

        fireEvent.change(container.querySelector('#affiliate-products-search')!, {
            target: { value: '  shirt  ' },
        })
        fireEvent.click(screen.getByRole('button', { name: 'Search' }))

        await waitFor(() => expect(listProductsMock).toHaveBeenLastCalledWith(
            1, 20, { ...baseListParams, search: 'shirt' },
        ))
        expect(screen.getByRole('button', { name: 'Clear' })).toBeInTheDocument()

        fireEvent.click(screen.getByRole('button', { name: 'Clear' }))

        await waitFor(() => expect(listProductsMock).toHaveBeenLastCalledWith(1, 20, baseListParams))
    })

    it('sends server-owned affiliate/product/category filters and resets the page to 1', async () => {
        // Bounded result set so pagination controls are available up front.
        listProductsMock.mockResolvedValue({ items: [], totalCount: 45, totalPages: 3 })
        getCategoryTreeMock.mockResolvedValue([
            { id: 'category-a', name: 'Electronics', isActive: true, subcategories: [] },
        ])
        const { container } = render(<AffiliateProducts />)

        await screen.findByText('No products yet')
        await waitFor(() => expect(container.querySelector('#affiliate-filter-category')).not.toBeDisabled())

        fireEvent.click(screen.getByRole('button', { name: 'Next' }))
        await waitFor(() => expect(listProductsMock).toHaveBeenLastCalledWith(2, 20, baseListParams))

        fireEvent.change(container.querySelector('#affiliate-filter-affiliate-status')!, {
            target: { value: 'enabled' },
        })
        await waitFor(() => expect(listProductsMock).toHaveBeenLastCalledWith(
            1, 20, { ...baseListParams, affiliateStatus: 'enabled' },
        ))

        fireEvent.change(container.querySelector('#affiliate-filter-product-status')!, {
            target: { value: 'inactive' },
        })
        await waitFor(() => expect(listProductsMock).toHaveBeenLastCalledWith(
            1, 20, { ...baseListParams, affiliateStatus: 'enabled', productStatus: 'inactive' },
        ))

        fireEvent.change(container.querySelector('#affiliate-filter-category')!, {
            target: { value: 'category-a' },
        })
        await waitFor(() => expect(listProductsMock).toHaveBeenLastCalledWith(
            1, 20, {
                search: '',
                affiliateStatus: 'enabled',
                productStatus: 'inactive',
                categoryId: 'category-a',
            },
        ))

        fireEvent.click(screen.getByRole('button', { name: 'Clear filters' }))
        await waitFor(() => expect(listProductsMock).toHaveBeenLastCalledWith(1, 20, baseListParams))
    })

    it('populates the read-only category filter from global taxonomy master data', async () => {
        getCategoryTreeMock.mockResolvedValue([
            {
                id: 'root-1',
                name: 'Electronics',
                isActive: true,
                subcategories: [{ id: 'sub-1', name: 'Phones', isActive: true }],
            },
        ])
        listProductsMock.mockResolvedValue({ items: [], totalCount: 0, totalPages: 0 })

        render(<AffiliateProducts />)

        const categoryFilter = await screen.findByLabelText(/affiliate category/i)
        await waitFor(() => expect(categoryFilter).not.toBeDisabled())

        const rootOption = screen.getByRole('option', { name: 'Electronics' }) as HTMLOptionElement
        const childOption = screen.getByRole('option', { name: 'Electronics > Phones' }) as HTMLOptionElement
        expect(rootOption.value).toBe('root-1')
        expect(childOption.value).toBe('sub-1')

        expect(screen.queryByRole('button', { name: /add category/i })).not.toBeInTheDocument()
        expect(document.querySelector('#affiliate-filter-category')?.tagName).toBe('SELECT')
    })

    it('never overwrites newer list context with a stale response', async () => {
        const staleRead = deferred<{ items: unknown[]; totalCount: number; totalPages: number }>()
        const freshRead = deferred<{ items: unknown[]; totalCount: number; totalPages: number }>()

        listProductsMock
            .mockReturnValueOnce(staleRead.promise)
            .mockReturnValueOnce(freshRead.promise)

        const { container } = render(<AffiliateProducts />)

        fireEvent.change(container.querySelector('#affiliate-products-search')!, {
            target: { value: 'lamp' },
        })
        fireEvent.click(screen.getByRole('button', { name: 'Search' }))

        freshRead.resolve({
            items: [row({ productId: 'fresh-1', productName: 'Fresh Result' })],
            totalCount: 25,
            totalPages: 2,
        })
        await screen.findByText('Fresh Result')

        // The older request (no search) resolves late and must be discarded.
        staleRead.resolve({
            items: [row({ productId: 'stale-1', productName: 'Stale Result' })],
            totalCount: 99,
            totalPages: 7,
        })

        expect(await screen.findByText('Fresh Result')).toBeInTheDocument()
        expect(screen.queryByText('Stale Result')).not.toBeInTheDocument()
        expect(screen.getByText(/Showing 1–20 of 25/)).toBeInTheDocument()
        expect(listProductsMock).toHaveBeenNthCalledWith(2, 1, 20, { ...baseListParams, search: 'lamp' })
    })

    it('pages through bounded results using server pagination metadata', async () => {
        listProductsMock.mockResolvedValue({
            items: [row()],
            totalCount: 45,
            totalPages: 3,
        })

        render(<AffiliateProducts />)

        await screen.findByText('Cotton Shirt')

        expect(screen.getByText(/Showing 1–20 of 45/)).toBeInTheDocument()

        const nextButton = screen.getByRole('button', { name: 'Next' })
        const previousButton = screen.getByRole('button', { name: 'Previous' })
        expect(previousButton).toBeDisabled()

        fireEvent.click(nextButton)

        await waitFor(() => expect(listProductsMock).toHaveBeenLastCalledWith(2, 20, baseListParams))
        expect(screen.getByText(/Page 2 of 3/)).toBeInTheDocument()
        expect(screen.getByText(/Showing 21–40 of 45/)).toBeInTheDocument()
    })

    it('reconciles a server-clamped out-of-range page into the active page context', async () => {
        listProductsMock
            .mockResolvedValueOnce({ items: [row()], page: 1, totalCount: 45, totalPages: 3 })
            .mockResolvedValue({ items: [row()], page: 1, totalCount: 20, totalPages: 1 })

        render(<AffiliateProducts />)
        await screen.findByText('Cotton Shirt')

        fireEvent.click(screen.getByRole('button', { name: 'Next' }))

        await waitFor(() => expect(listProductsMock).toHaveBeenCalledWith(2, 20, baseListParams))
        await waitFor(() => expect(listProductsMock).toHaveBeenLastCalledWith(1, 20, baseListParams))
    })

    it('opens the existing affiliate settings card to manage a product and refreshes after changes', async () => {
        listProductsMock.mockResolvedValue({
            items: [row()],
            totalCount: 1,
            totalPages: 1,
        })

        render(<AffiliateProducts />)

        const manageButton = await screen.findByRole('button', { name: 'Manage: Cotton Shirt' })
        manageButton.focus()
        fireEvent.click(manageButton)

        const dialog = await screen.findByRole('dialog')
        expect(dialog).toHaveAttribute('aria-modal', 'true')
        expect(dialog).toHaveAccessibleDescription('Cotton Shirt')
        expect(screen.getByText('Affiliate settings card')).toBeInTheDocument()
        const closeButton = screen.getByRole('button', { name: 'Close affiliate settings' })
        expect(closeButton).toHaveFocus()
        expect(settingsCardMock.mock.calls[0]?.[0]).toEqual(
            expect.objectContaining({
                productId: 'product-1',
                onSettingsChanged: expect.any(Function),
            }),
        )

        const readsBeforeRefresh = listProductsMock.mock.calls.length
        fireEvent.click(screen.getByRole('button', { name: 'simulate-settings-change' }))

        await waitFor(() => expect(listProductsMock.mock.calls.length).toBe(readsBeforeRefresh + 1))
        expect(listProductsMock).toHaveBeenLastCalledWith(1, 20, baseListParams)

        const lastDialogButton = screen.getByRole('button', { name: 'simulate-cancel' })
        lastDialogButton.focus()
        fireEvent.keyDown(document, { key: 'Tab' })
        expect(closeButton).toHaveFocus()

        fireEvent.keyDown(document, { key: 'Escape' })
        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
        expect(manageButton).toHaveFocus()
    })

    it('confirms dirty dialog cancellation and blocks closing during a mutation', async () => {
        listProductsMock.mockResolvedValue({
            items: [row()],
            page: 1,
            totalCount: 1,
            totalPages: 1,
        })
        confirmMock.mockResolvedValueOnce(false).mockResolvedValueOnce(true)

        render(<AffiliateProducts />)
        fireEvent.click(await screen.findByRole('button', { name: 'Manage: Cotton Shirt' }))

        fireEvent.click(screen.getByRole('button', { name: 'simulate-dirty' }))
        fireEvent.click(screen.getByRole('button', { name: 'Close affiliate settings' }))
        await waitFor(() => expect(confirmMock).toHaveBeenCalledTimes(1))
        expect(screen.getByRole('dialog')).toBeInTheDocument()

        fireEvent.click(screen.getByRole('button', { name: 'simulate-busy' }))
        const closeButton = screen.getByRole('button', { name: 'Close affiliate settings' })
        expect(closeButton).toBeDisabled()
        fireEvent.keyDown(document, { key: 'Escape' })
        expect(screen.getByRole('dialog')).toBeInTheDocument()

        fireEvent.click(screen.getByRole('button', { name: 'simulate-idle' }))
        fireEvent.click(screen.getByRole('button', { name: 'simulate-cancel' }))
        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
        expect(confirmMock).toHaveBeenCalledTimes(2)
    })

    it('never exposes category administration affordances', async () => {
        listProductsMock.mockResolvedValue({
            items: [row()],
            totalCount: 1,
            totalPages: 1,
        })

        render(<AffiliateProducts />)

        await screen.findByText('Cotton Shirt')

        expect(screen.queryByText(/create.*category/i)).not.toBeInTheDocument()
        expect(screen.queryByText(/manage categories/i)).not.toBeInTheDocument()
        expect(document.querySelectorAll('input[type="text"]').length).toBe(1)
    })

    it('keeps the route, sidebar entry, and module export wired for Owner/Admin', () => {
        const appSource = readFileSync(resolve(process.cwd(), 'src/App.jsx'), 'utf8')
        expect(appSource).toContain("import { AdminAffiliatesList, AdminAffiliateDetail, AffiliateProducts } from './modules/affiliate'")
        expect(appSource).toContain('path="admin/affiliates/products"')

        const sidebarSource = readFileSync(resolve(process.cwd(), 'src/components/Sidebar.jsx'), 'utf8')
        expect(sidebarSource).toContain("key: 'admin-affiliate-products'")
        expect(sidebarSource).toContain("path: '/admin/affiliates/products'")
        expect(sidebarSource).toContain("label: t('nav.affiliateProducts', 'Affiliate Products')")

        const moduleIndex = readFileSync(resolve(process.cwd(), 'src/modules/affiliate/index.js'), 'utf8')
        expect(moduleIndex).toContain("export { default as AffiliateProducts } from './pages/AffiliateProducts'")
    })
})
