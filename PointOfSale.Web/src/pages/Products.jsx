import { useState, useEffect, useRef, useCallback, useMemo } from 'react'
import api from '../services/api'
import ProductStockModal from '../components/ProductStockModal'
import BarcodeDisplay from '../components/BarcodeDisplay'
import ValidationDeleteModal from '../components/ValidationDeleteModal'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../contexts/SettingsContext'
import { useToast } from '../components/Toast'
import { useNavigate } from 'react-router-dom'
import { useRTL } from '../hooks/useRTL'
import { useConfirmDialog } from '../common/confirm/ConfirmDialogProvider'
import { useBulkProductMutation } from '../hooks/useBulkProductMutation'
import { invalidatePosRuntimeCache } from '../services/posRuntimeCache'
import { normalizePublishStatus } from '../utils/productStatus'
import { useReactToPrint } from 'react-to-print'
import ProductExportMenu from '../components/ProductExportMenu'
import ProductExportPrintArea from '../components/ProductExportPrintArea'
import ProductFeedDialog from '../components/ProductFeedDialog'
import BulkMutationResult from '../components/bulk/BulkMutationResult'
import { formatDateTime } from '../utils/dateFormat'
import { useHasPermission } from '../hooks/usePosSessionPermission'
import {
    EXPORT_SCOPE_CURRENT_VIEW,
    EXPORT_SCOPE_SELECTED,
    buildProductExportFilename,
    buildProductExportRows,
    getProductsForExport,
} from '../utils/productExport'
import { exportProductsToExcel } from '../utils/productExcelExport'

// Sort summary labels reuse the certified column-header keys. Dynamic
// per-field locale keys (products.sortName, …) do not exist in either locale
// and would leak raw keys into the UI.
const SORT_FIELD_LABEL_KEYS = Object.freeze({
    name: 'products.productName',
    price: 'products.price',
    stock: 'products.stock',
    status: 'products.status',
})

const PUBLICATION_FILTER_VALUES = Object.freeze({
    draft: 0,
    published: 1,
    archived: 2,
})

// Keep the component type stable across page renders so activating a sort
// control does not replace the focused DOM node and strand keyboard users.
function SortableHeader({ field, label, sortField, sortDirection, onSort, alignClass, t }) {
    const isActive = sortField === field
    const isAsc = isActive && sortDirection === 'asc'
    const isDesc = isActive && sortDirection === 'desc'
    const indicator = isAsc ? ' \u25B2' : isDesc ? ' \u25BC' : ''

    return (
        <th
            scope="col"
            className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${alignClass}`}
            aria-sort={isAsc ? 'ascending' : isDesc ? 'descending' : 'none'}
        >
            <button
                type="button"
                onClick={() => onSort(field)}
                className="inline-flex items-center gap-1 text-gray-500 hover:text-gray-700 focus:outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
                aria-label={t('products.sortBy', { field: label }) || `Sort by ${label}`}
            >
                <span>{label}</span>
                <span aria-hidden="true">{indicator}</span>
            </button>
        </th>
    )
}

// Stock state thresholds use the existing product data:
//  - TrackStock false => "untracked" (no stock state indicator)
//  - hasVariants => null (no parent-level badge; the authoritative low-stock
//    rule is per-variant on the backend, so a parent-level badge would be
//    misleading — the cell shows "Managed by variants" instead)
//  - totalStock === 0 => out of stock
//  - totalStock <= LowStockThreshold (when >0) or totalStock <= MinimumStockLevel => low stock
const computeStockState = (product, totalStock) => {
    if (!product.trackStock) {
        return 'untracked'
    }
    if (product.hasVariants) {
        return null
    }
    if (totalStock <= 0) {
        return 'outOfStock'
    }
    const threshold = product.lowStockThreshold > 0
        ? product.lowStockThreshold
        : product.minimumStockLevel
    if (totalStock <= threshold) {
        return 'lowStock'
    }
    return 'inStock'
}

export default function Products() {
    const { t } = useTranslation()
    const { isRTL, dir, locale } = useRTL()
    const { settings, formatCurrency } = useSettings()
    const { showSuccess, showError } = useToast()
    const { confirm, confirmAction } = useConfirmDialog()
    const navigate = useNavigate()
    const canManageCatalog = useHasPermission('catalog.manage')
    const canDeleteCatalog = useHasPermission('catalog.delete')
    const canPublishCatalog = useHasPermission('catalog.publish')
    const canAdjustInventory = useHasPermission('inventory.adjust')
    const canBulkMutateCatalog = canManageCatalog || canDeleteCatalog || canPublishCatalog

    const [products, setProducts] = useState([])
    const [categories, setCategories] = useState([])
    const [categoriesError, setCategoriesError] = useState('')
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState('')

    // Search (committed vs. draft)
    const [search, setSearch] = useState('')
    const [committedSearch, setCommittedSearch] = useState('')
    const searchInputRef = useRef(null)

    // Filters
    const [categoryFilter, setCategoryFilter] = useState('')
    const [statusFilter, setStatusFilter] = useState('')
    const [typeFilter, setTypeFilter] = useState('')
    const [publicationFilter, setPublicationFilter] = useState('')
    const [stockFilter, setStockFilter] = useState('')
    const [featuredFilter, setFeaturedFilter] = useState(false)
    const [searchableFilter, setSearchableFilter] = useState(false)
    const [backorderFilter, setBackorderFilter] = useState(false)

    // Sorting (client-side on current page)
    const [sortField, setSortField] = useState('name')
    const [sortDirection, setSortDirection] = useState('asc')

    const [page, setPage] = useState(1)
    const pageSize = 50
    const [totalCount, setTotalCount] = useState(0)
    const [totalPages, setTotalPages] = useState(0)

    const [stockModal, setStockModal] = useState(null)
    const [barcodeModal, setBarcodeModal] = useState(null)
    const [deleteModal, setDeleteModal] = useState(null)
    const [productFeedOpen, setProductFeedOpen] = useState(false)
    const [sessionFeedUrls, setSessionFeedUrls] = useState({})
    const productFeedTriggerRef = useRef(null)

    const handleSessionFeedUrlChange = useCallback((channel, feedUrl) => {
        setSessionFeedUrls((current) => {
            if (!feedUrl) {
                const next = { ...current }
                delete next[channel]
                return next
            }

            return {
                ...current,
                [channel]: feedUrl,
            }
        })
    }, [])

    // ---- Bulk selection (UI-only) ----
    // selectedProductIds tracks the stable authoritative product.id (Guid) of
    // each selected row. Selection is scoped to the currently loaded page/result
    // set only — never cross-page. No business logic is mutated by selection;
    // it is pure UI state consumed solely by the bulk-action toolbar.
    const [selectedProductIds, setSelectedProductIds] = useState([])
    const selectAllCheckboxRef = useRef(null)

    const clearSelection = useCallback(() => {
        setSelectedProductIds([])
    }, [])

    const categoriesRequestIdRef = useRef(0)
    const productsRequestIdRef = useRef(0)
    const hasStaleProductRows = loading && products.length > 0

    useEffect(() => {
        return () => {
            categoriesRequestIdRef.current += 1
            productsRequestIdRef.current += 1
        }
    }, [])

    const fetchCategories = useCallback(async () => {
        const requestId = ++categoriesRequestIdRef.current

        try {
            const response = await api.get('/categories')

            if (requestId !== categoriesRequestIdRef.current) {
                return
            }

            setCategories(response.data)
            setCategoriesError('')
        } catch {
            if (requestId === categoriesRequestIdRef.current) {
                setCategoriesError(t('categories.failedToLoad'))
                console.error('Failed to load categories')
            }
        }
    }, [t])

    const fetchProducts = useCallback(async () => {
        const requestId = ++productsRequestIdRef.current

        try {
            setLoading(true)
            // A new request means a new dataset may arrive; clear any stale
            // page-scoped selection so it cannot attach to different rows.
            clearSelection()
            const params = {}
            if (committedSearch) params.search = committedSearch
            if (categoryFilter) params.categoryId = categoryFilter
            if (statusFilter) params.isActive = statusFilter === 'active'
            if (typeFilter) params.hasVariants = typeFilter === 'variant'
            if (publicationFilter) params.publishStatus = PUBLICATION_FILTER_VALUES[publicationFilter]
            if (stockFilter) params.stockState = stockFilter
            if (featuredFilter) params.isFeatured = true
            if (searchableFilter) params.isSearchable = true
            if (backorderFilter) params.allowBackorder = true
            params.sortBy = sortField
            params.sortDirection = sortDirection
            params.page = page
            params.pageSize = pageSize

            const response = await api.get('/products', { params })

            if (requestId !== productsRequestIdRef.current) {
                return true
            }

            const newTotalPages = Number(response.headers['x-total-pages'] ?? 0)

            // Clamp current page if it exceeds the new total pages BEFORE committing data
            if (page > newTotalPages && newTotalPages > 0) {
                setTotalPages(newTotalPages)
                setPage(newTotalPages)
                // Increment requestId so finally block won't clear loading - re-fetch will handle it
                productsRequestIdRef.current += 1
                // Refresh is delegated to the follow-up fetch this triggers.
                return true
            } else if (newTotalPages === 0 && page !== 1) {
                setTotalPages(newTotalPages)
                setPage(1)
                // Increment requestId so finally block won't clear loading - re-fetch will handle it
                productsRequestIdRef.current += 1
                // Refresh is delegated to the follow-up fetch this triggers.
                return true
            }

            // Only commit data if we're on a valid page
            setProducts(response.data)
            setTotalCount(Number(response.headers['x-total-count'] ?? response.data.length ?? 0))
            setTotalPages(newTotalPages)
            setError('')
            return true
        } catch {
            if (requestId === productsRequestIdRef.current) {
                setError(t('products.failedToLoad'))
                console.error('Failed to load products')
                return false
            }
            // A newer canonical request superseded this one. Its outcome owns
            // the table state, so this stale failure is not a refresh failure.
            return true
        } finally {
            if (requestId === productsRequestIdRef.current) {
                setLoading(false)
            }
        }
    }, [
        committedSearch, categoryFilter, statusFilter, typeFilter,
        publicationFilter, stockFilter, featuredFilter, searchableFilter,
        backorderFilter, sortField, sortDirection, page, pageSize,
        clearSelection, t,
    ])

    // ---- Phase 6G.2/6G.3/6G.4/6G.5: bulk mutation orchestration ownership ----
    // The page owns the bulk lifecycle: snapshots resolve synchronously against
    // these authoritative loaded rows and the current page-owned selection,
    // while terminal outcomes reuse the canonical fetchProducts above (which
    // clears page-scoped selection at request start). Activate, Deactivate,
    // Publish, Unpublish and Delete are wired to visible toolbar controls;
    // Publish/Unpublish/Delete require an explicit localized confirmation.
    const {
        action: bulkAction,
        selectedItems: bulkSelectedItems,
        result: bulkResult,
        error: bulkError,
        refreshError: bulkRefreshError,
        isBulkMutating,
        startBulkMutation,
        resetBulkMutation,
    } = useBulkProductMutation({
        products,
        selectedProductIds,
        refreshProducts: fetchProducts,
    })

    // Display names must be captured at operation start: the canonical refetch
    // clears the selection, but per-item result details still have to resolve
    // to real product names afterwards. The snapshot is keyed by productId so
    // it stays valid independently of the selection lifecycle.
    const [bulkProductNames, setBulkProductNames] = useState({})

    const beginBulkAction = useCallback((actionKind) => {
        if (isBulkMutating || selectedProductIds.length === 0) {
            return false
        }
        setBulkProductNames(Object.fromEntries(products.map((row) => [row.id, row.name])))
        return startBulkMutation(actionKind)
    }, [isBulkMutating, selectedProductIds.length, products, startBulkMutation])

    const handleBulkActivate = useCallback(() => beginBulkAction('activate'), [beginBulkAction])
    const handleBulkDeactivate = useCallback(() => beginBulkAction('deactivate'), [beginBulkAction])

    // Publish/Unpublish require an explicit localized confirmation before the
    // single allowed startBulkMutation call. A synchronous ref guard prevents
    // rapid duplicate clicks from opening a second dialog; cancel resolves
    // with false and makes zero mutation calls.
    const awaitingBulkConfirmationRef = useRef(false)

    const requestConfirmedBulkAction = useCallback(async (actionKind) => {
        if (isBulkMutating || selectedProductIds.length === 0) {
            return
        }
        if (awaitingBulkConfirmationRef.current) {
            return
        }
        awaitingBulkConfirmationRef.current = true
        try {
            const count = selectedProductIds.length
            const confirmOptions = actionKind === 'publish'
                ? {
                    title: t('products.bulkPublishConfirmTitle', { count }),
                    description: t('products.bulkPublishConfirmDescription', { count }),
                    confirmLabel: t('products.bulkActionPublish'),
                    cancelLabel: t('products.cancel'),
                }
                : {
                    title: t('products.bulkUnpublishConfirmTitle', { count }),
                    description: t('products.bulkUnpublishConfirmDescription', { count }),
                    confirmLabel: t('products.bulkActionUnpublish'),
                    cancelLabel: t('products.cancel'),
                }
            const confirmed = await confirm(confirmOptions)
            if (!confirmed) {
                return
            }
            beginBulkAction(actionKind)
        } finally {
            awaitingBulkConfirmationRef.current = false
        }
    }, [confirm, isBulkMutating, selectedProductIds.length, beginBulkAction, t])

    const handleBulkPublish = useCallback(() => {
        void requestConfirmedBulkAction('publish')
    }, [requestConfirmedBulkAction])

    const handleBulkUnpublish = useCallback(() => {
        void requestConfirmedBulkAction('unpublish')
    }, [requestConfirmedBulkAction])

    // ---- Phase 6G.5: bulk Delete ----
    // Delete is destructive, so it must never be one-click. The existing
    // confirmAction concept runs the dialog in awaited-action mode: the
    // dialog stays open with a real pending state while the confirmed
    // mutation starts, and cancel/Escape/close dispatches zero mutations.
    // A synchronous ref guard prevents rapid duplicate clicks from opening a
    // second dialog; once confirmed, beginBulkAction snapshots names and
    // delegates to startBulkMutation('delete') exactly once.
    const awaitingBulkDeleteConfirmationRef = useRef(false)

    const handleBulkDelete = useCallback(() => {
        if (isBulkMutating || selectedProductIds.length === 0) {
            return
        }
        if (awaitingBulkDeleteConfirmationRef.current) {
            return
        }
        awaitingBulkDeleteConfirmationRef.current = true
        const count = selectedProductIds.length

        void (async () => {
            try {
                await confirmAction({
                    title: t('products.bulkDeleteConfirmTitle', { count }),
                    description: t('products.bulkDeleteConfirmDescription', { count }),
                    confirmLabel: t('products.bulkActionDelete'),
                    cancelLabel: t('products.cancel'),
                    variant: 'danger',
                    awaitAction: true,
                    onConfirm: () => beginBulkAction('delete'),
                })
            } finally {
                awaitingBulkDeleteConfirmationRef.current = false
            }
        })()
    }, [confirmAction, isBulkMutating, selectedProductIds.length, beginBulkAction, t])

    const handleDismissBulkResult = useCallback(() => {
        resetBulkMutation()
    }, [resetBulkMutation])

    const resolveBulkProductName = useCallback((productId) => {
        const name = productId ? bulkProductNames[productId] : null
        return typeof name === 'string' && name.trim().length > 0 ? name : t('products.bulkUnknownProduct')
    }, [bulkProductNames, t])

    useEffect(() => {
        void fetchCategories()
    }, [fetchCategories])

    useEffect(() => {
        void fetchProducts()
    }, [fetchProducts])

    // ---- Derived data: display-only enrichment of the authoritative server page ----
    const enrichedProducts = useMemo(() => {
        return products.map((product) => {
            const totalStock = product.warehouseStocks?.reduce(
                (sum, ws) => sum + Number(ws.availableQuantity ?? ws.quantity ?? 0),
                0,
            ) || 0
            return {
                ...product,
                totalStock,
                stockState: computeStockState(product, totalStock),
                // Normalize once so both string ("Published") and numeric (1)
                // representations from the API are handled consistently.
                normalizedPublishStatus: normalizePublishStatus(product.publishStatus),
            }
        })
    }, [products])

    const sortedProducts = enrichedProducts

    // ---- Applied filter tracking ----
    const hasActiveFilters = !!(
        categoryFilter
        || statusFilter
        || typeFilter
        || publicationFilter
        || stockFilter
        || featuredFilter
        || searchableFilter
        || backorderFilter
    )

    const activeFilterCount = [
        categoryFilter, statusFilter, typeFilter, publicationFilter, stockFilter,
    ].filter(Boolean).length + [
        featuredFilter, searchableFilter, backorderFilter
    ].filter(Boolean).length

    // ---- Event handlers ----
    const handleSearch = (e) => {
        e.preventDefault()
        const normalizedSearch = search.trim()
        setSearch(normalizedSearch)
        setPage(1)
        setCommittedSearch(normalizedSearch)
        clearSelection()

        if (normalizedSearch === committedSearch && page === 1) {
            void fetchProducts()
        }
    }

    // Clear the search draft AND the committed search driving the API query,
    // then return to page 1. committedSearch/page are fetchProducts deps, so
    // the fetch effect re-runs exactly once — no duplicate requests. Non-search
    // filters are intentionally preserved (use Clear Filters to reset those).
    const handleClearSearch = () => {
        setSearch('')
        setCommittedSearch('')
        setPage(1)
        clearSelection()
        searchInputRef.current?.focus()
    }

    const handleAdd = () => {
        navigate('/products/new/edit')
    }

    const handleEdit = (productId) => {
        navigate(`/products/${productId}/edit`)
    }

    const handleDelete = (product) => {
        setDeleteModal({
            id: product.id,
            name: product.name,
            rowVersion: product.rowVersion
        })
    }

    const handleDeleteSuccess = () => {
        invalidatePosRuntimeCache({ reason: 'product-updated' })
        showSuccess(t('common.deletedSuccessfully'))
        void fetchProducts()
    }

    const handleDeactivateSuccess = () => {
        invalidatePosRuntimeCache({ reason: 'product-updated' })
        showSuccess(t('products.deactivated'))
        void fetchProducts()
    }

    const handleShowStock = (product) => {
        setStockModal({
            productId: product.id,
            productName: product.name
        })
    }

    const handleShowBarcode = (barcode) => {
        setBarcodeModal(barcode)
    }

    const handleClearFilters = () => {
        setSearch('')
        setCommittedSearch('')
        setCategoryFilter('')
        setStatusFilter('')
        setTypeFilter('')
        setPublicationFilter('')
        setStockFilter('')
        setFeaturedFilter(false)
        setSearchableFilter(false)
        setBackorderFilter(false)
        setPage(1)
        clearSelection()
    }

    const handleSort = (field) => {
        setPage(1)
        clearSelection()
        if (sortField === field) {
            setSortDirection((currentDirection) => currentDirection === 'asc' ? 'desc' : 'asc')
        } else {
            setSortField(field)
            setSortDirection('asc')
        }
    }

    // ---- Phase 3: Export (Excel + PDF) ----
    // Exports never re-filter, re-sort or re-fetch: they consume
    // `sortedProducts` (the exact current view) intersected with the Phase 2
    // selection state, so the file always matches what the user sees.
    const [exporting, setExporting] = useState(null)
    const [pdfExportRequest, setPdfExportRequest] = useState(null)
    const printAreaRef = useRef(null)
    const exportLockRef = useRef(false)
    const pdfExportScope = pdfExportRequest?.scope || null

    // A failed replacement request leaves the previous rows visible so the
    // page does not flash empty, but those rows no longer represent the active
    // query controls. Never allow that stale dataset to be exported.
    const exportAvailable = !loading && !hasStaleProductRows && !error && sortedProducts.length > 0

    // Human-readable summary of the currently active filters, used only as
    // PDF report metadata (never as a second filtering pipeline).
    const activeFilterSummary = useMemo(() => {
        const parts = []
        if (committedSearch) parts.push(`${t('products.search')}: "${committedSearch}"`)
        if (categoryFilter) {
            const category = categories.find((cat) => cat.id === categoryFilter)
            if (category) parts.push(`${t('products.category')}: ${category.name}`)
        }
        if (statusFilter) {
            parts.push(`${t('products.status')}: ${statusFilter === 'active' ? t('products.active') : t('products.inactive')}`)
        }
        if (typeFilter) {
            parts.push(`${t('products.type')}: ${typeFilter === 'simple' ? t('products.simpleProductType') : t('products.variantProductType')}`)
        }
        if (publicationFilter) {
            const publicationLabels = {
                published: t('products.published'),
                draft: t('products.draft'),
                archived: t('products.archived'),
            }
            parts.push(`${t('products.publication')}: ${publicationLabels[publicationFilter] || publicationFilter}`)
        }
        if (stockFilter) {
            const stockLabels = {
                inStock: t('products.inStock'),
                lowStock: t('products.lowStock'),
                outOfStock: t('products.outOfStock'),
            }
            parts.push(`${t('products.stock')}: ${stockLabels[stockFilter] || stockFilter}`)
        }
        if (featuredFilter) parts.push(t('products.featured'))
        if (searchableFilter) parts.push(t('products.searchable'))
        if (backorderFilter) parts.push(t('products.backorderAllowed'))
        return parts
    }, [
        committedSearch, categoryFilter, categories, statusFilter, typeFilter,
        publicationFilter, stockFilter, featuredFilter, searchableFilter, backorderFilter, t,
    ])

    // PDF printing is asynchronous. Snapshot the click-time rows instead of
    // allowing later table/selection changes to mutate the pending document.
    const pdfProducts = useMemo(() => pdfExportRequest?.products || [], [pdfExportRequest?.products])

    const pdfRows = useMemo(
        () => buildProductExportRows(pdfProducts, { t, formatCurrency }),
        [pdfProducts, t, formatCurrency]
    )

    const handleExportExcel = useCallback(async (scope, products) => {
        setExporting('excel')
        try {
            await exportProductsToExcel(products, {
                scope,
                t,
                formatCurrency,
                storeName: settings.companyName,
                isRTL,
            })
            showSuccess(t('products.exportSuccess'))
        } catch {
            console.error('Product Excel export failed')
            showError(t('products.exportFailed'))
        } finally {
            exportLockRef.current = false
            setExporting(null)
        }
    }, [t, formatCurrency, settings.companyName, isRTL, showSuccess, showError])

    const finishPdfExport = useCallback(() => {
        exportLockRef.current = false
        setPdfExportRequest(null)
        setExporting(null)
    }, [])

    const handlePdfPrintError = useCallback((errorLocation) => {
        console.error(`Product PDF export failed (${errorLocation})`)
        showError(t('products.exportFailed'))
        finishPdfExport()
    }, [finishPdfExport, showError, t])

    // PDF export uses the established react-to-print report pattern (same as
    // BarcodePrinting): the browser print pipeline renders Arabic shaping +
    // RTL correctly, which the bundled jsPDF cannot do without embedded
    // Arabic fonts. The installed react-to-print v3 API requires `contentRef`
    // (the legacy v2 `content` callback is no longer read).
    const printProductsReport = useReactToPrint({
        contentRef: printAreaRef,
        documentTitle: buildProductExportFilename({
            format: 'pdf',
            scope: pdfExportScope || EXPORT_SCOPE_CURRENT_VIEW,
        }).replace(/\.pdf$/i, ''),
        onAfterPrint: finishPdfExport,
        onPrintError: handlePdfPrintError,
    })

    const handleExportPdf = useCallback((scope, products) => {
        setExporting('pdf')
        setPdfExportRequest({ scope, products })
    }, [])

    // Open the print dialog once the print area has committed the dataset of
    // the requested scope (the effect runs after the re-render commit).
    useEffect(() => {
        if (!pdfExportRequest) return

        try {
            printProductsReport()
        } catch {
            handlePdfPrintError('print')
        }
    }, [pdfExportRequest, printProductsReport, handlePdfPrintError])

    const handleExport = useCallback((format, scope) => {
        if (exportLockRef.current || exporting) return
        const products = getProductsForExport({ scope, sortedProducts, selectedProductIds })
        if (products.length === 0) return

        exportLockRef.current = true
        if (format === 'excel') {
            void handleExportExcel(scope, products)
        } else {
            handleExportPdf(scope, products)
        }
    }, [exporting, sortedProducts, selectedProductIds, handleExportExcel, handleExportPdf])

    const pdfHeaders = [
        t('products.product'),
        t('products.sku'),
        t('products.type'),
        t('products.category'),
        t('products.price'),
        t('products.stock'),
        t('products.status'),
        t('products.publication'),
    ]

    const startLabel = totalCount > 0 ? (page - 1) * pageSize + 1 : 0
    const endLabel = totalCount > 0 ? Math.min(page * pageSize, totalCount) : 0

    const tableTextAlignClass = isRTL ? 'text-right' : 'text-left'
    const actionAlignClass = isRTL ? 'text-left' : 'text-right'

    const selectableIds = useMemo(
        () => sortedProducts.map((p) => p.id),
        [sortedProducts]
    )
    const selectedSet = useMemo(
        () => new Set(selectedProductIds),
        [selectedProductIds]
    )

    const isAllSelected = selectableIds.length > 0 && selectableIds.every((id) => selectedSet.has(id))
    const isSomeSelected = selectedProductIds.length > 0 && !isAllSelected
    const isHeaderChecked = isAllSelected
    const isHeaderIndeterminate = isSomeSelected && !isAllSelected

    const handleSelectOne = useCallback((productId) => {
        if (loading) return
        setSelectedProductIds((prev) => {
            const set = new Set(prev)
            if (set.has(productId)) {
                set.delete(productId)
            } else {
                set.add(productId)
            }
            return Array.from(set)
        })
    }, [loading])

    const handleSelectAll = useCallback(() => {
        if (loading) return
        if (isAllSelected) {
            setSelectedProductIds([])
        } else {
            setSelectedProductIds([...selectableIds])
        }
    }, [isAllSelected, loading, selectableIds])

    // Sync the native indeterminate property on the header checkbox. Native
    // <input type="checkbox"> has no JSX attribute for indeterminate, so we set
    // it via a ref after every render that changes the selection state.
    useEffect(() => {
        if (selectAllCheckboxRef.current) {
            selectAllCheckboxRef.current.indeterminate = isHeaderIndeterminate
        }
    }, [isHeaderIndeterminate])

    // Publication badge config: matches the canonical enum (Draft=0, Published=1, Archived=2)
    const getPublicationBadge = (normalizedStatus) => {
        if (normalizedStatus === 1) {
            return { labelKey: 'products.published', className: 'bg-indigo-100 text-indigo-800' }
        }
        if (normalizedStatus === 2) {
            return { labelKey: 'products.archived', className: 'bg-gray-100 text-gray-700' }
        }
        return { labelKey: 'products.draft', className: 'bg-amber-100 text-amber-700' }
    }

    // Stock cell content: show numeric qty when tracked; meaningful label for
    // variant-managed or untracked products (never a misleading pseudo-value).
    // Tracked rows open the existing ProductStockModal drill-down (restored
    // baseline behavior). The modal reads GET /products/{id}/stock, which
    // aggregates the product's variant stocks per warehouse — accurate for
    // both simple and variant-managed products. Product-level stock
    // adjustments inside the modal are rejected by the backend for variant
    // products with an explicit message, so nothing is faked here.
    const renderStockCell = (product) => {
        if (!product.trackStock) {
            return <span className="text-sm text-gray-400">—</span>
        }
        const content = product.hasVariants
            ? <span className="text-sm text-gray-500">{t('products.managedByVariants')}</span>
            : <span className="text-sm font-medium text-gray-900">{product.totalStock}</span>
        return (
            <button
                type="button"
                disabled={hasStaleProductRows}
                onClick={(e) => {
                    e.stopPropagation()
                    handleShowStock(product)
                }}
                className="inline-flex items-center rounded hover:opacity-80 focus:outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 disabled:cursor-not-allowed disabled:opacity-60"
                title={t('products.viewStock')}
                aria-label={`${t('products.viewStock')}: ${product.name || t('products.product')}`}
            >
                {content}
            </button>
        )
    }

    // Stock state badge (shown alongside the quantity in the Stock column)
    const getStockStateBadge = (stockState) => {
        switch (stockState) {
            case 'outOfStock':
                return { labelKey: 'products.outOfStock', className: 'bg-red-100 text-red-800' }
            case 'lowStock':
                return { labelKey: 'products.lowStock', className: 'bg-amber-100 text-amber-800' }
            case 'inStock':
                return { labelKey: 'products.inStock', className: 'bg-emerald-100 text-emerald-800' }
            default:
                return null
        }
    }

    return (
        <div dir={dir}>
            <div className="mb-6 flex flex-wrap items-center justify-between gap-3">
                <h2 className="text-2xl font-bold text-gray-900">{t('products.title')}</h2>
                <div className="flex flex-wrap items-center gap-2">
                    {canPublishCatalog && (
                        <button
                            type="button"
                            ref={productFeedTriggerRef}
                            onClick={() => setProductFeedOpen(true)}
                            className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-primary-500"
                        >
                            {t('productFeed.title')}
                        </button>
                    )}
                    <ProductExportMenu
                        onExport={handleExport}
                        selectedCount={selectedProductIds.length}
                        available={exportAvailable}
                        exporting={exporting}
                    />
                    {canManageCatalog && (
                        <button onClick={handleAdd} className="btn-primary">+ {t('products.addProduct')}</button>
                    )}
                </div>
            </div>

            {canPublishCatalog && (
                <ProductFeedDialog
                    open={productFeedOpen}
                    onOpenChange={setProductFeedOpen}
                    sessionFeedUrls={sessionFeedUrls}
                    onSessionFeedUrlChange={handleSessionFeedUrlChange}
                    restoreFocusRef={productFeedTriggerRef}
                />
            )}

            {/* Search section - single prominent search input + search button */}
            <form onSubmit={handleSearch} className="mb-4 flex flex-col gap-2 sm:flex-row">
                <div className="relative flex-1">
                    <div className="absolute inset-y-0 start-0 flex items-center ps-3 pointer-events-none">
                        <svg className="w-4 h-4 text-gray-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                                d="M21 21l-4.35-4.35m0 0A7.5 7.5 0 1110.5 3a7.5 7.5 0 017.5 7.5z" />
                        </svg>
                    </div>
                    <input
                        ref={searchInputRef}
                        type="text"
                        placeholder={t('products.searchPlaceholder')}
                        value={search}
                        onChange={(e) => setSearch(e.target.value)}
                        className="input ps-10"
                        aria-label={t('products.searchPlaceholder')}
                    />
                    {search && (
                        <button
                            type="button"
                            onClick={handleClearSearch}
                            className="absolute inset-y-0 end-0 flex items-center pe-2 text-gray-400 hover:text-gray-600"
                            aria-label={t('common.clear')}
                        >
                            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                                    d="M6 18L18 6M6 6l12 12" />
                            </svg>
                        </button>
                    )}
                </div>
                <button type="submit" className="btn-primary">
                    {t('common.search')}
                </button>
            </form>

            {/* Results summary */}
            <div className="mb-4 text-sm text-gray-600" aria-live="polite" aria-atomic="true">
                {loading
                    ? t('common.loading')
                    : error && products.length === 0
                        ? error
                        : `${t('products.showingOf', { start: startLabel, end: endLabel, total: totalCount })}`}
            </div>

            {/* Filters */}
            <div className="card mb-6">
                {categoriesError && (
                    <div role="alert" className="border-b border-amber-200 bg-amber-50 px-6 py-3 text-sm text-amber-800">
                        {categoriesError}
                        <button
                            type="button"
                            onClick={() => void fetchCategories()}
                            className="ms-3 font-medium underline"
                        >
                            {t('common.retry')}
                        </button>
                    </div>
                )}
                {/* Filter header */}
                <div className="flex items-center justify-between border-b border-gray-100 px-6 py-3">
                    <h3 className="text-sm font-semibold text-gray-900">{t('products.filters')}</h3>
                    {hasActiveFilters && (
                        <span className="text-xs text-gray-500">
                            {t('products.appliedFiltersCount', { count: activeFilterCount })}
                        </span>
                    )}
                </div>

                <form onSubmit={handleSearch} className="p-6 space-y-4">
                    {/* Row 1 — Primary filters: Category, Status, Product Type, Publication */}
                    <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-4 gap-4">
                        <div>
                            <label htmlFor="products-category-filter" className="label">{t('products.category')}</label>
                            <select
                                id="products-category-filter"
                                value={categoryFilter}
                                onChange={(e) => {
                                    setPage(1)
                                    setCategoryFilter(e.target.value)
                                    clearSelection()
                                }}
                                className="input"
                            >
                                <option value="">{t('products.allCategories')}</option>
                                {categories.map(cat => (
                                    <option key={cat.id} value={cat.id}>
                                        {cat.parentCategoryName ? `${cat.parentCategoryName} > ` : ''}{cat.name}
                                    </option>
                                ))}
                            </select>
                        </div>
                        <div>
                            <label htmlFor="products-status-filter" className="label">{t('products.status')}</label>
                            <select
                                id="products-status-filter"
                                value={statusFilter}
                                onChange={(e) => {
                                    setPage(1)
                                    setStatusFilter(e.target.value)
                                    clearSelection()
                                }}
                                className="input"
                            >
                                <option value="">{t('products.allStatus')}</option>
                                <option value="active">{t('products.active')}</option>
                                <option value="inactive">{t('products.inactive')}</option>
                            </select>
                        </div>
                        <div>
                            <label htmlFor="products-type-filter" className="label">{t('products.type')}</label>
                            <select
                                id="products-type-filter"
                                value={typeFilter}
                                onChange={(e) => {
                                    setPage(1)
                                    setTypeFilter(e.target.value)
                                    clearSelection()
                                }}
                                className="input"
                            >
                                <option value="">{t('products.allProductTypes')}</option>
                                <option value="simple">{t('products.simpleProductType')}</option>
                                <option value="variant">{t('products.variantProductType')}</option>
                            </select>
                        </div>
                        <div>
                            <label htmlFor="products-publication-filter" className="label">{t('products.publication')}</label>
                            <select
                                id="products-publication-filter"
                                value={publicationFilter}
                                onChange={(e) => {
                                    setPage(1)
                                    setPublicationFilter(e.target.value)
                                    clearSelection()
                                }}
                                className="input"
                            >
                                <option value="">{t('products.allPublication')}</option>
                                <option value="published">{t('products.published')}</option>
                                <option value="draft">{t('products.draft')}</option>
                                <option value="archived">{t('products.archived')}</option>
                            </select>
                        </div>
                    </div>

                    {/* Row 2 — Inventory / attribute filters: Stock, Featured, Searchable, Backorder */}
                    <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-4 gap-4 items-end">
                        <div>
                            <label htmlFor="products-stock-filter" className="label">{t('products.stock')}</label>
                            <select
                                id="products-stock-filter"
                                value={stockFilter}
                                onChange={(e) => {
                                    setPage(1)
                                    setStockFilter(e.target.value)
                                    clearSelection()
                                }}
                                className="input"
                            >
                                <option value="">{t('products.allStockStates')}</option>
                                <option value="inStock">{t('products.inStock')}</option>
                                <option value="lowStock">{t('products.lowStock')}</option>
                                <option value="outOfStock">{t('products.outOfStock')}</option>
                            </select>
                        </div>
                        <div className="flex items-end gap-2">
                            <div className="flex items-center gap-2">
                                <input
                                    type="checkbox"
                                    id="featuredFilter"
                                    checked={featuredFilter}
                                    onChange={(e) => {
                                        setPage(1)
                                        setFeaturedFilter(e.target.checked)
                                        clearSelection()
                                    }}
                                    className="h-4 w-4 rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                                />
                                <label htmlFor="featuredFilter" className="text-sm text-gray-700">
                                    {t('products.featured')}
                                </label>
                            </div>
                        </div>
                        <div className="flex items-end gap-2">
                            <div className="flex items-center gap-2">
                                <input
                                    type="checkbox"
                                    id="searchableFilter"
                                    checked={searchableFilter}
                                    onChange={(e) => {
                                        setPage(1)
                                        setSearchableFilter(e.target.checked)
                                        clearSelection()
                                    }}
                                    className="h-4 w-4 rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                                />
                                <label htmlFor="searchableFilter" className="text-sm text-gray-700">
                                    {t('products.searchable')}
                                </label>
                            </div>
                        </div>
                        <div className="flex items-end gap-2">
                            <div className="flex items-center gap-2">
                                <input
                                    type="checkbox"
                                    id="backorderFilter"
                                    checked={backorderFilter}
                                    onChange={(e) => {
                                        setPage(1)
                                        setBackorderFilter(e.target.checked)
                                        clearSelection()
                                    }}
                                    className="h-4 w-4 rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                                />
                                <label htmlFor="backorderFilter" className="text-sm text-gray-700">
                                    {t('products.backorderAllowed')}
                                </label>
                            </div>
                        </div>
                    </div>

                    {/* Filter footer: Clear Filters (left) — no duplicate Search button */}
                    <div className="flex items-center justify-between pt-2 border-t border-gray-100">
                        <button
                            type="button"
                            onClick={handleClearFilters}
                            className="text-sm text-primary-600 hover:text-primary-700 font-medium"
                        >
                            {t('products.clearFilters')}
                        </button>
                    </div>
                </form>
            </div>

            {error && (
                <div role="alert" className="mb-4 p-3 bg-red-100 border border-red-400 text-red-700 rounded">
                    {error}
                    <button
                        type="button"
                        onClick={() => void fetchProducts()}
                        className="ms-3 text-sm font-medium underline"
                    >
                        {t('common.retry')}
                    </button>
                </div>
            )}

            {/* Bulk selection toolbar. Activate/Deactivate are live in 6G.3;
                Publish/Unpublish are live in 6G.4 behind an explicit
                confirmation dialog; Delete is live in 6G.5 behind a
                destructive awaited-action confirmation that can never start
                the mutation without an explicit confirmation click. */}
            {canBulkMutateCatalog && (
            <div
                className="mb-4 flex flex-wrap items-center justify-between gap-3 rounded-lg border border-gray-200 bg-gray-50 px-4 py-2.5"
                aria-busy={isBulkMutating || undefined}
            >
                    <div className="flex items-center gap-3">
                        <span className="text-sm font-medium text-gray-900">
                            {t('products.productsSelected', { count: selectedProductIds.length })}
                        </span>
                    </div>
                    <div
                        className="flex flex-wrap items-center gap-2"
                        role="group"
                        aria-label={t('products.bulkActionsLabel')}
                    >
                        {canManageCatalog && <>
                            <button
                                type="button"
                                disabled={selectedProductIds.length === 0 || isBulkMutating}
                                onClick={handleBulkActivate}
                                className="px-3 py-1.5 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-100 focus:outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 disabled:cursor-not-allowed disabled:opacity-60"
                            >
                                {t('products.bulkActionActivate')}
                            </button>
                            <button
                                type="button"
                                disabled={selectedProductIds.length === 0 || isBulkMutating}
                                onClick={handleBulkDeactivate}
                                className="px-3 py-1.5 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-100 focus:outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 disabled:cursor-not-allowed disabled:opacity-60"
                            >
                                {t('products.bulkActionDeactivate')}
                            </button>
                        </>}
                        {canPublishCatalog && <>
                            <button
                                type="button"
                                disabled={selectedProductIds.length === 0 || isBulkMutating}
                                onClick={handleBulkPublish}
                                className="px-3 py-1.5 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-100 focus:outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 disabled:cursor-not-allowed disabled:opacity-60"
                            >
                                {t('products.bulkActionPublish')}
                            </button>
                            <button
                                type="button"
                                disabled={selectedProductIds.length === 0 || isBulkMutating}
                                onClick={handleBulkUnpublish}
                                className="px-3 py-1.5 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-100 focus:outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 disabled:cursor-not-allowed disabled:opacity-60"
                            >
                                {t('products.bulkActionUnpublish')}
                            </button>
                        </>}
                        {canDeleteCatalog && (
                            <button
                                type="button"
                                disabled={selectedProductIds.length === 0 || isBulkMutating}
                                onClick={handleBulkDelete}
                                className="px-3 py-1.5 text-sm font-medium text-red-600 bg-white border border-red-300 rounded-lg hover:bg-red-50 focus:outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 disabled:cursor-not-allowed disabled:opacity-60"
                            >
                                {t('products.bulkActionDelete')}
                            </button>
                        )}
                        <button
                            type="button"
                            onClick={clearSelection}
                            disabled={selectedProductIds.length === 0 || isBulkMutating}
                            className="px-3 py-1.5 text-sm font-medium text-primary-600 hover:text-primary-700 rounded-lg focus:outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 disabled:cursor-not-allowed disabled:opacity-60"
                        >
                            {t('products.clearSelection')}
                        </button>
                    </div>
            </div>
            )}

            {/* Phase 6G.6: one shared, strictly passive result panel owns the
                processing announcement and terminal presentation for all five
                bulk actions (Activate/Deactivate/Publish/Unpublish/Delete).
                The page keeps lifecycle ownership: it passes the normalized
                hook state plus the stable pre-mutation name snapshot and a
                passive dismiss callback; refresh problems stay a separate
                notice that never relabels the mutation itself as failed. */}
            {canBulkMutateCatalog && <BulkMutationResult
                action={bulkAction}
                isMutating={isBulkMutating}
                processingCount={bulkSelectedItems.length}
                result={bulkResult}
                error={bulkError}
                refreshError={bulkRefreshError}
                resolveProductName={resolveBulkProductName}
                onDismiss={handleDismissBulkResult}
            />}

            <div className="card" aria-busy={loading || undefined}>
                {hasStaleProductRows ? (
                    <div className="border-b border-gray-200 bg-gray-50 px-6 py-2 text-xs text-gray-500">
                        {t('common.loading')}
                    </div>
                ) : null}
                {loading && products.length === 0 ? (
                    <div className="p-4 space-y-3">
                        {Array.from({ length: 8 }).map((_, i) => (
                            <div key={i} className="h-5 bg-gray-200 rounded animate-pulse"></div>
                        ))}
                    </div>
                ) : error && products.length === 0 ? null : sortedProducts.length === 0 ? (
                    <div className="text-center py-12">
                        <svg className="w-12 h-12 mx-auto text-gray-300 mb-4" fill="none" stroke="currentColor"
                            viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1}
                                d="M3 7v10a2 2 0 002 2h14a2 2 0 002-2V7" />
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1}
                                d="M8 5a4 4 0 018 0v4H8V5z" />
                        </svg>
                        <p className="text-gray-500 mb-2">
                            {hasActiveFilters || committedSearch
                                ? t('products.noMatchingProducts')
                                : t('products.noProducts')}
                        </p>
                        {(hasActiveFilters || committedSearch) && (
                            <button
                                type="button"
                                onClick={handleClearFilters}
                                className="btn-primary"
                            >
                                {t('products.clearFilters')}
                            </button>
                        )}
                    </div>
                ) : (
                    <div className="overflow-x-auto">
                        <table
                            className="min-w-full divide-y divide-gray-200"
                            aria-label={t('products.title')}
                        >
                            <thead className="bg-gray-50">
                                <tr>
                                    {/* Select-all column (first column) */}
                                    {canBulkMutateCatalog && <th
                                        scope="col"
                                        className="px-3 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider"
                                        aria-label={t('products.selectAllVisible')}
                                    >
                                        <div className="flex items-center justify-center">
                                            <input
                                                type="checkbox"
                                                ref={selectAllCheckboxRef}
                                                checked={isHeaderChecked}
                                                disabled={loading || selectableIds.length === 0}
                                                onChange={(e) => {
                                                    e.stopPropagation()
                                                    handleSelectAll()
                                                }}
                                                aria-label={t('products.selectAllVisible')}
                                                className="h-4 w-4 rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                                            />
                                        </div>
                                    </th>}
                                    <SortableHeader
                                        field="name"
                                        label={t('products.productName')}
                                        sortField={sortField}
                                        sortDirection={sortDirection}
                                        onSort={handleSort}
                                        alignClass={tableTextAlignClass}
                                        t={t}
                                    />
                                    <th
                                        scope="col"
                                        className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}
                                    >
                                        {t('products.type')}
                                    </th>
                                    <th
                                        scope="col"
                                        className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}
                                    >
                                        {t('products.category')}
                                    </th>
                                    <SortableHeader
                                        field="price"
                                        label={t('products.price')}
                                        sortField={sortField}
                                        sortDirection={sortDirection}
                                        onSort={handleSort}
                                        alignClass={tableTextAlignClass}
                                        t={t}
                                    />
                                    <SortableHeader
                                        field="stock"
                                        label={t('products.stock')}
                                        sortField={sortField}
                                        sortDirection={sortDirection}
                                        onSort={handleSort}
                                        alignClass={tableTextAlignClass}
                                        t={t}
                                    />
                                    <SortableHeader
                                        field="status"
                                        label={t('products.status')}
                                        sortField={sortField}
                                        sortDirection={sortDirection}
                                        onSort={handleSort}
                                        alignClass={tableTextAlignClass}
                                        t={t}
                                    />
                                    <th
                                        scope="col"
                                        className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}
                                    >
                                        {t('products.publication')}
                                    </th>
                                    <th
                                        scope="col"
                                        className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${actionAlignClass}`}
                                    >
                                        {t('products.actions')}
                                    </th>
                                </tr>
                            </thead>
                            <tbody className="bg-white divide-y divide-gray-200">
                                {sortedProducts.map((product) => {
                                    const pubBadge = getPublicationBadge(product.normalizedPublishStatus)
                                    const stockBadge = getStockStateBadge(product.stockState)
                                    return (
                                        <tr
                                            key={product.id}
                                            aria-disabled={hasStaleProductRows}
                                            className={
                                                hasStaleProductRows
                                                    ? 'pointer-events-none opacity-60'
                                                    : selectedSet.has(product.id)
                                                        ? 'cursor-pointer hover:bg-gray-50 bg-primary-50'
                                                        : canManageCatalog ? 'cursor-pointer hover:bg-gray-50' : 'hover:bg-gray-50'
                                            }
                                            onClick={hasStaleProductRows || !canManageCatalog ? undefined : () => handleEdit(product.id)}
                                        >
                                            {/* Selection checkbox (first column). The wrapper isolates checkbox
                                                interaction from the row click: clicking a checkbox or its label
                                                must select the row, never navigate to the editor. */}
                                            {canBulkMutateCatalog && <td className="px-3 py-4 whitespace-nowrap">
                                                <div
                                                    className="flex items-center justify-center"
                                                    onClick={(e) => e.stopPropagation()}
                                                >
                                                    <input
                                                        type="checkbox"
                                                        checked={selectedSet.has(product.id)}
                                                        disabled={hasStaleProductRows}
                                                        onChange={(e) => {
                                                            e.stopPropagation()
                                                            handleSelectOne(product.id)
                                                        }}
                                                        aria-label={t('products.selectProduct', { name: product.name || '' })}
                                                        className="h-4 w-4 rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                                                    />
                                                </div>
                                            </td>}
                                            {/* Product (primary identity column) */}
                                            <td className="max-w-xs px-6 py-4 align-top">
                                                <div className="flex items-center gap-3">
                                                    {product.images ? (
                                                        <img
                                                            src={product.images.split(',')[0]}
                                                            alt={product.name}
                                                            className="w-10 h-10 object-cover rounded"
                                                            onError={(e) => e.target.style.display = 'none'}
                                                        />
                                                    ) : (
                                                        <div className="w-10 h-10 flex items-center justify-center rounded bg-gray-100 text-gray-400">
                                                            <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1}
                                                                    d="M4 16l4.586-4.586A2 2 0 0112 12h4a2 2 0 012 2v4a2 2 0 01-2 2H4a2 2 0 01-2-2v-2a2 2 0 012-2z" />
                                                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1}
                                                                    d="M4 6h16M4 10h16" />
                                                            </svg>
                                                        </div>
                                                    )}
                                                    <div className="min-w-0">
                                                        <div
                                                            className="break-words text-sm font-medium text-gray-900"
                                                            title={product.name}
                                                        >
                                                            {product.name || '—'}
                                                        </div>
                                                        {product.sku && (
                                                            <div className="break-all text-xs text-gray-500" title={product.sku}>
                                                                {t('products.sku')}: {product.sku}
                                                            </div>
                                                        )}
                                                    </div>
                                                </div>
                                            </td>

                                            {/* Type badge */}
                                            <td className="px-6 py-4 whitespace-nowrap">
                                                <span
                                                    className={`px-2 inline-flex text-xs leading-5 font-semibold rounded-full ${
                                                        product.hasVariants
                                                            ? 'bg-purple-100 text-purple-800'
                                                            : 'bg-blue-100 text-blue-800'
                                                    }`}
                                                    title={product.hasVariants
                                                        ? t('products.variantProductType')
                                                        : t('products.simpleProductType')}
                                                >
                                                    {product.hasVariants
                                                        ? t('products.variantProductType')
                                                        : t('products.simpleProductType')}
                                                </span>
                                            </td>

                                            {/* Category */}
                                            <td className="max-w-xs truncate px-6 py-4 whitespace-nowrap text-sm text-gray-500"
                                                title={product.categoryName || product.category?.name}>
                                                {product.categoryName || product.category?.name || '—'}
                                            </td>

                                            {/* Price */}
                                            <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                                {formatCurrency(product.salePrice)}
                                            </td>

                                            {/* Stock (qty + state badge) */}
                                            <td className="px-6 py-4 whitespace-nowrap">
                                                <div className="flex items-center gap-2">
                                                    {renderStockCell(product)}
                                                    {stockBadge && (
                                                        <span
                                                            className={`px-1.5 inline-flex text-xs leading-5 font-semibold rounded-full ${stockBadge.className}`}
                                                            title={t(stockBadge.labelKey)}
                                                        >
                                                            {t(stockBadge.labelKey)}
                                                        </span>
                                                    )}
                                                </div>
                                            </td>

                                            {/* Status */}
                                            <td className="px-6 py-4 whitespace-nowrap">
                                                <span
                                                    className={`px-2 inline-flex text-xs leading-5 font-semibold rounded-full ${
                                                        product.isActive
                                                            ? 'bg-green-100 text-green-800'
                                                            : 'bg-gray-100 text-gray-800'
                                                    }`}
                                                    title={product.isActive ? t('products.active') : t('products.inactive')}
                                                >
                                                    {product.isActive ? t('products.active') : t('products.inactive')}
                                                </span>
                                            </td>

                                            {/* Publication */}
                                            <td className="px-6 py-4 whitespace-nowrap">
                                                <span
                                                    className={`px-2 inline-flex text-xs leading-5 font-semibold rounded-full ${pubBadge.className}`}
                                                    title={t(pubBadge.labelKey)}
                                                >
                                                    {t(pubBadge.labelKey)}
                                                </span>
                                            </td>

                                            {/* Actions */}
                                            <td className={`px-6 py-4 whitespace-nowrap text-sm font-medium ${actionAlignClass}`}>
                                                <div className={`flex items-center gap-2 ${isRTL ? 'justify-start' : 'justify-end'}`}>
                                                    {canManageCatalog && <button
                                                        type="button"
                                                        disabled={hasStaleProductRows}
                                                        onClick={(e) => {
                                                            e.stopPropagation()
                                                            handleEdit(product.id)
                                                        }}
                                                        className="text-primary-600 hover:text-primary-900 disabled:cursor-not-allowed disabled:text-primary-300"
                                                        title={t('common.edit')}
                                                        aria-label={`${t('common.edit')}: ${product.name || t('products.product')}`}
                                                    >
                                                        {t('common.edit')}
                                                    </button>}
                                                    {canDeleteCatalog && <button
                                                        type="button"
                                                        disabled={hasStaleProductRows}
                                                        onClick={(e) => {
                                                            e.stopPropagation()
                                                            handleDelete(product)
                                                        }}
                                                        className="text-red-600 hover:text-red-900 disabled:cursor-not-allowed disabled:text-red-300"
                                                        title={t('common.delete')}
                                                        aria-label={`${t('common.delete')}: ${product.name || t('products.product')}`}
                                                    >
                                                        {t('common.delete')}
                                                    </button>}
                                                    {product.isOnlineVisible && product.slug && settings.storefrontUrl && (
                                                        <a
                                                            href={`${settings.storefrontUrl}/products/${encodeURIComponent(product.slug)}`}
                                                            target="_blank"
                                                            rel="noopener noreferrer"
                                                            aria-disabled={hasStaleProductRows}
                                                            onClick={(e) => e.stopPropagation()}
                                                            tabIndex={hasStaleProductRows ? -1 : undefined}
                                                            className={`text-blue-600 hover:text-blue-900 flex items-center gap-1 ${
                                                                hasStaleProductRows ? 'pointer-events-none text-blue-300' : ''
                                                            }`}
                                                            title={t('products.viewOnStorefront')}
                                                            aria-label={`${t('products.viewOnStorefront')}: ${product.name || t('products.product')}`}
                                                        >
                                                            <svg
                                                                className="w-4 h-4"
                                                                fill="none"
                                                                stroke="currentColor"
                                                                viewBox="0 0 24 24"
                                                            >
                                                                <path
                                                                    strokeLinecap="round"
                                                                    strokeLinejoin="round"
                                                                    strokeWidth={2}
                                                                    d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14"
                                                                />
                                                            </svg>
                                                        </a>
                                                    )}
                                                    <button
                                                        type="button"
                                                        disabled={hasStaleProductRows || !product.barcode}
                                                        onClick={(e) => {
                                                            e.stopPropagation()
                                                            handleShowBarcode(product.barcode)
                                                        }}
                                                        className="text-gray-400 hover:text-gray-600 disabled:cursor-not-allowed disabled:text-gray-300"
                                                        title={t('products.viewBarcode')}
                                                        aria-label={`${t('products.viewBarcode')}: ${product.name || t('products.product')}`}
                                                    >
                                                        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                                                                d="M12 9.5V5a3 3 0 10-6 0v4.5a3 3 0 006 0zM6 9.5V5a3 3 0 016 0v4.5a3 3 0 01-6 0z" />
                                                        </svg>
                                                    </button>
                                                </div>
                                            </td>
                                        </tr>
                                    )
                                })}
                            </tbody>
                        </table>
                    </div>
                )}
            </div>

            {/* Result summary + Sorting indicator + Pagination */}
            <div className="mt-4 flex flex-wrap items-center justify-between gap-3 border-t border-gray-200 pt-4">
                <div className="flex min-w-0 flex-wrap items-center gap-x-4 gap-y-1">
                    <div className="break-words text-sm text-gray-600">
                        {loading
                            ? t('common.loading')
                            : error && products.length === 0
                                ? error
                                : `${t('products.showingOf', { start: startLabel, end: endLabel, total: totalCount })}`}
                    </div>
                    {sortField && (
                        <span className="break-words text-xs text-gray-500">
                            {t('products.sort')}: {t(SORT_FIELD_LABEL_KEYS[sortField] ?? 'products.sort')} · {t(`products.sort${sortDirection === 'asc' ? 'Ascending' : 'Descending'}`)}
                        </span>
                    )}
                </div>
                {totalPages > 1 && (
                    <div className="flex flex-wrap gap-2">
                        <button
                            type="button"
                            onClick={() => {
                                setPage((currentPage) => Math.max(1, currentPage - 1))
                                clearSelection()
                            }}
                            disabled={page === 1}
                            className="px-4 py-2 border border-gray-300 rounded-lg text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
                        >
                            {t('common.previous')}
                        </button>
                        <button
                            type="button"
                            onClick={() => {
                                setPage((currentPage) => Math.min(totalPages, currentPage + 1))
                                clearSelection()
                            }}
                            disabled={page >= totalPages}
                            className="px-4 py-2 border border-gray-300 rounded-lg text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
                        >
                            {t('common.next')}
                        </button>
                    </div>
                )}
            </div>

            {/* Stock Modal */}
            {stockModal && (
                <ProductStockModal
                    productId={stockModal.productId}
                    productName={stockModal.productName}
                    canAdjust={canAdjustInventory}
                    onClose={() => setStockModal(null)}
                />
            )}

            {/* Barcode Display Modal */}
            {barcodeModal && (
                <BarcodeDisplay
                    barcode={barcodeModal}
                    onClose={() => setBarcodeModal(null)}
                />
            )}

            {/* Validation Delete Modal */}
            {canDeleteCatalog && deleteModal && (
                <ValidationDeleteModal
                    isOpen={!!deleteModal}
                    onClose={() => setDeleteModal(null)}
                    entityType="product"
                    entityId={deleteModal.id}
                    entityName={deleteModal.name}
                    expectedVersion={deleteModal.rowVersion}
                    onDeleteSuccess={handleDeleteSuccess}
                    onDeactivateSuccess={handleDeactivateSuccess}
                />
            )}

            {/* Phase 3: print-only Products Report (hidden on screen).
                Mounted on demand when a PDF export is requested: the print
                effect runs after this commit, so the ref is attached before
                react-to-print reads it. Keeping it unmounted otherwise avoids
                duplicating the visible rows in the live DOM. react-to-print
                clones this node into a print iframe, so the hidden ancestor
                never affects the printed output. */}
            {pdfExportScope && (
                <div className="hidden">
                    <ProductExportPrintArea
                        ref={printAreaRef}
                        rows={pdfRows}
                        headers={pdfHeaders}
                        title={t('products.exportReportTitle')}
                        storeName={settings.companyName}
                        scopeLabel={`${t('products.exportScope')}: ${pdfExportScope === EXPORT_SCOPE_SELECTED ? t('products.exportSelected') : t('products.exportCurrentView')}`}
                        exportedAt={`${t('products.exportedAt')}: ${formatDateTime(new Date(), locale)}`}
                        countLabel={t('products.exportProductsCount', { count: pdfRows.length })}
                        filtersSummary={activeFilterSummary.length > 0
                            ? `${t('products.exportActiveFilters')}: ${activeFilterSummary.join(' · ')}`
                            : ''}
                        isRTL={isRTL}
                        dir={dir}
                    />
                </div>
            )}
        </div>
    )
}
