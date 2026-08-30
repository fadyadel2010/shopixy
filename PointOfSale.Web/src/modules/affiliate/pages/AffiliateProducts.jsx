/**
 * Shopixy Affiliate Refactor A/C: Affiliate Products page (POS Admin, Owner/Admin only).
 *
 * PURPOSE:
 * Single bounded surface for affiliate product participation management. Product
 * identity/status comes from the server response (Product authority); affiliate
 * status maps to the persisted policy IsEnabled — it is never inferred from product
 * active status. Category names come from the global AffiliateCategory master data.
 *
 * RULES:
 * - Server-owned search, filters, and pagination (no client-side filtering of the catalog).
 * - Search/filter changes reset the page to 1 before querying the server.
 * - Rapid list requests are sequence-guarded so stale responses never overwrite
 *   newer search/filter/page context.
 * - Manage opens the existing ProductAffiliateSettingsCard inside a dialog; after a
 *   settings change the list refreshes through the card's optional callback while
 *   preserving current search/filter/page context.
 * - No category administration affordance exists here: categories are read-only,
 *   system-owned master data managed by Super Admin surfaces only.
 */
import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import adminProductAffiliateApi from '../../../services/adminProductAffiliateApi'
import ProductAffiliateSettingsCard from '../../../components/ProductAffiliateSettingsCard'
import { useConfirmDialog } from '../../../common/confirm/ConfirmDialogProvider'

const PAGE_SIZE = 20

function formatCommissionRate(rate) {
    if (rate == null) return null
    const percent = Number(rate) * 100
    if (!Number.isFinite(percent)) return null
    return `${percent.toFixed(2)}%`
}

function formatMaxCommission(value) {
    if (value == null) return null
    const parsed = Number(value)
    if (!Number.isFinite(parsed)) return null
    return parsed.toFixed(2)
}

function StatusBadge({ tone, label }) {
    return (
        <span
            className={`inline-flex items-center gap-1 whitespace-nowrap rounded-full border px-2.5 py-0.5 text-xs font-semibold ${tone}`}
        >
            <span aria-hidden="true" className="h-1.5 w-1.5 rounded-full bg-current" />
            {label}
        </span>
    )
}

function SkeletonRow() {
    return (
        <tr className="animate-pulse" aria-hidden="true">
            {[0, 1, 2, 3, 4, 5, 6].map((cell) => (
                <td key={cell} className="px-4 py-3">
                    <div className="h-4 rounded bg-gray-200" />
                </td>
            ))}
        </tr>
    )
}

export default function AffiliateProducts() {
    const { t } = useTranslation()
    const { confirm } = useConfirmDialog()
    const [items, setItems] = useState([])
    const [pagination, setPagination] = useState({ totalCount: 0, totalPages: 0 })
    const [page, setPage] = useState(1)
    const [search, setSearch] = useState('')
    const [searchInput, setSearchInput] = useState('')
    const [affiliateStatus, setAffiliateStatus] = useState('all')
    const [productStatus, setProductStatus] = useState('all')
    const [categoryId, setCategoryId] = useState('')
    const [filterCategories, setFilterCategories] = useState([])
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState(false)
    const [managing, setManaging] = useState(null)
    const [manageBusy, setManageBusy] = useState(false)
    const [manageDirty, setManageDirty] = useState(false)
    const requestSeq = useRef(0)
    const manageDialogRef = useRef(null)
    const manageCloseButtonRef = useRef(null)
    const manageOpenerRef = useRef(null)
    const manageBusyRef = useRef(false)
    const manageDirtyRef = useRef(false)

    const handleManageBusyChange = useCallback((nextBusy) => {
        manageBusyRef.current = nextBusy
        setManageBusy(nextBusy)
    }, [])

    const handleManageDirtyChange = useCallback((nextDirty) => {
        manageDirtyRef.current = nextDirty
        setManageDirty(nextDirty)
    }, [])

    // Category master data is read/select-only here; it only feeds the filter control.
    useEffect(() => {
        let active = true
        adminProductAffiliateApi.getCategoryTree()
            .then((tree) => {
                if (active && Array.isArray(tree)) {
                    setFilterCategories(tree)
                }
            })
            .catch(() => {
                // Filter degrades to an empty non-editable state; never blocks the table.
                if (active) {
                    setFilterCategories([])
                }
            })
        return () => {
            active = false
        }
    }, [])

    const fetchProducts = useCallback(({ showLoading = true } = {}) => {
        const currentRequest = ++requestSeq.current

        if (showLoading) {
            setLoading(true)
        }

        return adminProductAffiliateApi.listProducts(page, PAGE_SIZE, {
            search,
            affiliateStatus,
            productStatus,
            categoryId: categoryId || null,
        })
            .then((data) => {
                if (currentRequest !== requestSeq.current) return
                setItems(Array.isArray(data?.items) ? data.items : [])
                setPagination({
                    totalCount: data?.totalCount ?? 0,
                    totalPages: data?.totalPages ?? 0,
                })
                if (Number.isInteger(data?.page) && data.page >= 1 && data.page !== page) {
                    setPage(data.page)
                }
                setError(false)
            })
            .catch(() => {
                if (currentRequest !== requestSeq.current) return
                setItems([])
                setPagination({ totalCount: 0, totalPages: 0 })
                setError(true)
            })
            .finally(() => {
                if (currentRequest === requestSeq.current) {
                    setLoading(false)
                }
            })
    }, [page, search, affiliateStatus, productStatus, categoryId])

    useEffect(() => {
        void fetchProducts()
    }, [fetchProducts])

    useEffect(() => () => {
        requestSeq.current += 1
    }, [])

    useEffect(() => {
        if (!managing) {
            handleManageBusyChange(false)
            handleManageDirtyChange(false)
        }
    }, [handleManageBusyChange, handleManageDirtyChange, managing])

    const requestCloseManageDialog = useCallback(async () => {
        if (manageBusyRef.current) return

        if (manageDirtyRef.current) {
            const confirmed = await confirm({
                cancelLabel: t('affiliateSettingsCard.stayLabel'),
                confirmLabel: t('affiliateSettingsCard.leaveLabel'),
                description: t('affiliateSettingsCard.unsavedDialogDescription'),
                title: t('affiliateSettingsCard.unsavedDialogTitle'),
                variant: 'warning',
            })
            if (!confirmed) return
        }

        setManaging(null)
    }, [confirm, t])

    useEffect(() => {
        if (!managing) return undefined

        manageCloseButtonRef.current?.focus()

        const getFocusableElements = () => Array.from(
            manageDialogRef.current?.querySelectorAll(
                'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"]):not([disabled])',
            ) ?? [],
        )

        const isTopmostDialog = () => {
            const dialogs = Array.from(document.querySelectorAll('[role="dialog"][aria-modal="true"]'))
            return dialogs.at(-1) === manageDialogRef.current
        }

        const handleKeyDown = (event) => {
            if (!isTopmostDialog()) return

            if (event.key === 'Escape') {
                void requestCloseManageDialog()
                return
            }

            if (event.key === 'Tab' && manageDialogRef.current) {
                const focusableElements = getFocusableElements()
                if (focusableElements.length === 0) {
                    event.preventDefault()
                    manageDialogRef.current.focus()
                    return
                }

                const firstElement = focusableElements[0]
                const lastElement = focusableElements[focusableElements.length - 1]
                const focusIsInside = document.activeElement instanceof Node
                    && manageDialogRef.current.contains(document.activeElement)

                if (!focusIsInside) {
                    event.preventDefault()
                    const focusTarget = event.shiftKey ? lastElement : firstElement
                    focusTarget.focus()
                } else if (event.shiftKey && document.activeElement === firstElement) {
                    event.preventDefault()
                    lastElement.focus()
                } else if (!event.shiftKey && document.activeElement === lastElement) {
                    event.preventDefault()
                    firstElement.focus()
                }
            }
        }

        document.addEventListener('keydown', handleKeyDown)
        return () => {
            document.removeEventListener('keydown', handleKeyDown)
            if (manageOpenerRef.current?.isConnected) {
                manageOpenerRef.current.focus()
            }
        }
    }, [managing, requestCloseManageDialog])

    const handleSearchSubmit = (event) => {
        event.preventDefault()
        setSearch(searchInput.trim())
        setPage(1)
    }

    const handleClearSearch = () => {
        setSearchInput('')
        setSearch('')
        setPage(1)
    }

    const handleAffiliateStatusChange = (event) => {
        setAffiliateStatus(event.target.value)
        setPage(1)
    }

    const handleProductStatusChange = (event) => {
        setProductStatus(event.target.value)
        setPage(1)
    }

    const handleCategoryFilterChange = (event) => {
        setCategoryId(event.target.value)
        setPage(1)
    }

    const hasActiveFilters = affiliateStatus !== 'all' || productStatus !== 'all' || categoryId !== ''

    const handleClearFilters = () => {
        setAffiliateStatus('all')
        setProductStatus('all')
        setCategoryId('')
        setPage(1)
    }

    const hasActiveSearch = search.length > 0
    const showPrev = page > 1
    const showNext = !loading && !error && pagination.totalPages > page

    return (
        <div className="space-y-6">
            <div>
                <h2 className="text-3xl font-bold text-gray-900">{t('affiliateProducts.title')}</h2>
                <p className="mt-1 text-gray-600">{t('affiliateProducts.description')}</p>
            </div>

            <form onSubmit={handleSearchSubmit} className="card" role="search">
                <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
                    <label className="sr-only" htmlFor="affiliate-products-search">
                        {t('affiliateProducts.searchLabel')}
                    </label>
                    <input
                        id="affiliate-products-search"
                        type="text"
                        value={searchInput}
                        onChange={(event) => setSearchInput(event.target.value)}
                        placeholder={t('affiliateProducts.searchPlaceholder')}
                        className="w-full flex-1 rounded-lg border border-gray-300 px-4 py-2.5 text-sm outline-none focus:border-primary-500 focus:ring-2 focus:ring-primary-500 sm:max-w-sm"
                    />
                    <button type="submit" className="btn-secondary shrink-0">
                        {t('affiliateProducts.searchAction')}
                    </button>
                    {hasActiveSearch && (
                        <button
                            type="button"
                            onClick={handleClearSearch}
                            className="shrink-0 px-3 py-2.5 text-sm text-gray-500 hover:text-gray-700"
                        >
                            {t('affiliateProducts.clearSearch')}
                        </button>
                    )}
                </div>
                <div className="mt-3 grid grid-cols-1 gap-3 border-t border-gray-100 pt-3 sm:grid-cols-3">
                    <div>
                        <label className="mb-1 block text-xs font-medium text-gray-600" htmlFor="affiliate-filter-affiliate-status">
                            {t('affiliateProducts.filters.affiliateStatus')}
                        </label>
                        <select
                            id="affiliate-filter-affiliate-status"
                            value={affiliateStatus}
                            onChange={handleAffiliateStatusChange}
                            className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-primary-500 focus:ring-2 focus:ring-primary-500"
                        >
                            <option value="all">{t('affiliateProducts.filters.all')}</option>
                            <option value="enabled">{t('affiliateProducts.filters.enabled')}</option>
                            <option value="disabled">{t('affiliateProducts.filters.disabled')}</option>
                        </select>
                    </div>
                    <div>
                        <label className="mb-1 block text-xs font-medium text-gray-600" htmlFor="affiliate-filter-product-status">
                            {t('affiliateProducts.filters.productStatus')}
                        </label>
                        <select
                            id="affiliate-filter-product-status"
                            value={productStatus}
                            onChange={handleProductStatusChange}
                            className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-primary-500 focus:ring-2 focus:ring-primary-500"
                        >
                            <option value="all">{t('affiliateProducts.filters.all')}</option>
                            <option value="active">{t('affiliateProducts.productStatusActive')}</option>
                            <option value="inactive">{t('affiliateProducts.productStatusInactive')}</option>
                        </select>
                    </div>
                    <div>
                        <label className="mb-1 block text-xs font-medium text-gray-600" htmlFor="affiliate-filter-category">
                            {t('affiliateProducts.table.category')}
                        </label>
                        <select
                            id="affiliate-filter-category"
                            value={categoryId}
                            onChange={handleCategoryFilterChange}
                            disabled={filterCategories.length === 0}
                            aria-disabled={filterCategories.length === 0 || undefined}
                            className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-primary-500 focus:ring-2 focus:ring-primary-500 disabled:bg-gray-50 disabled:text-gray-400"
                        >
                            <option value="">
                                {filterCategories.length === 0
                                    ? t('affiliateProducts.filters.categoriesUnavailable')
                                    : t('affiliateProducts.filters.allCategories')}
                            </option>
                            {filterCategories.map((main) => (
                                <optgroup key={main.id} label={main.name}>
                                    <option value={main.id}>{main.name}</option>
                                    {(main.subcategories ?? []).map((sub) => (
                                        <option key={sub.id} value={sub.id}>
                                            {`${main.name} > ${sub.name}`}
                                        </option>
                                    ))}
                                </optgroup>
                            ))}
                        </select>
                    </div>
                </div>
                {hasActiveFilters && (
                    <div className="mt-3 flex justify-end">
                        <button
                            type="button"
                            onClick={handleClearFilters}
                            className="px-3 py-1.5 text-sm text-gray-500 hover:text-gray-700"
                        >
                            {t('affiliateProducts.filters.clearFilters')}
                        </button>
                    </div>
                )}
            </form>

            {error && (
                <div className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-red-700" role="alert">
                    <p className="text-sm font-medium">{t('affiliateProducts.error')}</p>
                    <button
                        type="button"
                        onClick={() => {
                            void fetchProducts()
                        }}
                        className="btn-secondary mt-3"
                    >
                        {t('affiliateProducts.retry')}
                    </button>
                </div>
            )}

            {!error && (
                <div className="card overflow-hidden">
                    <div className="overflow-x-auto">
                        <table className="min-w-full divide-y divide-gray-200">
                            <caption className="sr-only">{t('affiliateProducts.title')}</caption>
                            <thead className="bg-gray-50">
                                <tr>
                                    <th scope="col" className="px-4 py-3 text-start text-xs font-medium uppercase tracking-wider text-gray-500">{t('affiliateProducts.table.product')}</th>
                                    <th scope="col" className="px-4 py-3 text-start text-xs font-medium uppercase tracking-wider text-gray-500">{t('affiliateProducts.table.productStatus')}</th>
                                    <th scope="col" className="px-4 py-3 text-start text-xs font-medium uppercase tracking-wider text-gray-500">{t('affiliateProducts.table.affiliateStatus')}</th>
                                    <th scope="col" className="px-4 py-3 text-start text-xs font-medium uppercase tracking-wider text-gray-500">{t('affiliateProducts.table.category')}</th>
                                    <th scope="col" className="px-4 py-3 text-end text-xs font-medium uppercase tracking-wider text-gray-500">{t('affiliateProducts.table.commissionRate')}</th>
                                    <th scope="col" className="px-4 py-3 text-end text-xs font-medium uppercase tracking-wider text-gray-500">{t('affiliateProducts.table.maxCommission')}</th>
                                    <th scope="col" className="px-4 py-3 text-center text-xs font-medium uppercase tracking-wider text-gray-500">{t('affiliateProducts.table.actions')}</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-gray-200 bg-white">
                                {loading ? (
                                    [...Array(5)].map((_, index) => <SkeletonRow key={index} />)
                                ) : items.length === 0 ? (
                                    <tr>
                                        <td colSpan={7}>
                                            <div className="flex flex-col items-center justify-center py-16 text-center">
                                                <h3 className="mb-2 text-lg font-semibold text-gray-700">
                                                    {hasActiveSearch || hasActiveFilters
                                                        ? t('affiliateProducts.emptySearchTitle')
                                                        : t('affiliateProducts.emptyTitle')}
                                                </h3>
                                                <p className="max-w-sm text-sm text-gray-500">
                                                    {hasActiveSearch || hasActiveFilters
                                                        ? t('affiliateProducts.emptySearchHint')
                                                        : t('affiliateProducts.emptyHint')}
                                                </p>
                                            </div>
                                        </td>
                                    </tr>
                                ) : (
                                    items.map((item) => {
                                        const rateLabel = formatCommissionRate(item.commissionRate)
                                        const maxLabel = formatMaxCommission(item.maxCommissionPerItem)
                                        return (
                                            <tr key={item.productId}>
                                                <td className="px-4 py-3 text-sm font-medium text-gray-900">{item.productName}</td>
                                                <td className="px-4 py-3">
                                                    <StatusBadge
                                                        tone={item.isProductActive
                                                            ? 'border-emerald-200 bg-emerald-50 text-emerald-700'
                                                            : 'border-slate-200 bg-slate-100 text-slate-600'}
                                                        label={item.isProductActive
                                                            ? t('affiliateProducts.productStatusActive')
                                                            : t('affiliateProducts.productStatusInactive')}
                                                    />
                                                </td>
                                                <td className="px-4 py-3">
                                                    <StatusBadge
                                                        tone={item.isAffiliateEnabled
                                                            ? 'border-indigo-200 bg-indigo-50 text-indigo-700'
                                                            : 'border-gray-200 bg-gray-50 text-gray-500'}
                                                        label={item.isAffiliateEnabled
                                                            ? t('affiliateProducts.affiliateStatusEnabled')
                                                            : t('affiliateProducts.affiliateStatusDisabled')}
                                                    />
                                                </td>
                                                <td className="px-4 py-3 text-sm text-gray-700">
                                                    {item.affiliateCategoryName
                                                        ? (item.affiliateParentCategoryName
                                                            ? `${item.affiliateParentCategoryName} > ${item.affiliateCategoryName}`
                                                            : item.affiliateCategoryName)
                                                        : (
                                                            <span className="text-gray-400">{t('affiliateProducts.categoryNone')}</span>
                                                        )}
                                                </td>
                                                <td className="px-4 py-3 text-sm tabular-nums text-gray-700">
                                                    {rateLabel ?? <span className="text-gray-400">—</span>}
                                                </td>
                                                <td className="px-4 py-3 text-sm tabular-nums text-gray-700">
                                                    {maxLabel ?? <span className="text-gray-400">{t('affiliateProducts.noCap')}</span>}
                                                </td>
                                                <td className="px-4 py-3 text-center">
                                                    <button
                                                        type="button"
                                                         onClick={(event) => {
                                                             manageOpenerRef.current = event.currentTarget
                                                             handleManageBusyChange(false)
                                                             handleManageDirtyChange(false)
                                                             setManaging(item)
                                                         }}
                                                        aria-label={`${t('affiliateProducts.manage')}: ${item.productName}`}
                                                        className="btn-secondary text-sm"
                                                    >
                                                        {t('affiliateProducts.manage')}
                                                    </button>
                                                </td>
                                            </tr>
                                        )
                                    })
                                )}
                            </tbody>
                        </table>
                    </div>
                </div>
            )}

            {(showPrev || showNext) && (
                <div className="flex items-center justify-between px-2">
                    <p className="text-sm text-gray-600">
                        {t('affiliateProducts.pagination.summary', {
                            from: Math.min((page - 1) * PAGE_SIZE + 1, pagination.totalCount),
                            to: Math.min(page * PAGE_SIZE, pagination.totalCount),
                            total: pagination.totalCount,
                        })}
                    </p>
                    <div className="flex gap-2">
                        <button
                            type="button"
                            onClick={() => setPage((current) => Math.max(1, current - 1))}
                            disabled={!showPrev}
                            className="btn-secondary text-sm disabled:opacity-50"
                        >
                            {t('affiliateProducts.pagination.previous')}
                        </button>
                        <span className="flex items-center px-3 text-sm text-gray-600">
                            {t('affiliateProducts.pagination.pageInfo', {
                                page,
                                totalPages: Math.max(pagination.totalPages, 1),
                            })}
                        </span>
                        <button
                            type="button"
                            onClick={() => setPage((current) => current + 1)}
                            disabled={!showNext}
                            className="btn-secondary text-sm disabled:opacity-50"
                        >
                            {t('affiliateProducts.pagination.next')}
                        </button>
                    </div>
                </div>
            )}

            {managing && (
                <div className="fixed inset-0 z-50 overflow-y-auto bg-black/40 p-4">
                    <div
                        ref={manageDialogRef}
                        role="dialog"
                        aria-modal="true"
                        aria-labelledby="affiliate-product-manage-title"
                        aria-describedby="affiliate-product-manage-description"
                        aria-busy={manageBusy || undefined}
                        tabIndex={-1}
                        className="mx-auto my-8 w-full max-w-2xl rounded-2xl bg-white shadow-xl"
                    >
                        <div className="flex items-center justify-between border-b border-gray-100 px-6 py-4">
                            <div>
                                <h3 id="affiliate-product-manage-title" className="text-lg font-semibold text-gray-900">
                                    {t('affiliateProducts.manageTitle')}
                                </h3>
                                <p id="affiliate-product-manage-description" className="text-sm text-gray-500">{managing.productName}</p>
                            </div>
                            <button
                                ref={manageCloseButtonRef}
                                type="button"
                                onClick={() => {
                                    void requestCloseManageDialog()
                                }}
                                disabled={manageBusy}
                                aria-label={t('affiliateProducts.closeManage')}
                                className="rounded-lg p-2 text-gray-500 transition-colors hover:bg-gray-100 hover:text-gray-700 disabled:cursor-not-allowed disabled:opacity-50"
                            >
                                <svg aria-hidden="true" className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                                </svg>
                            </button>
                        </div>
                        <div className="px-6 py-5">
                            <ProductAffiliateSettingsCard
                                productId={managing.productId}
                                onBusyChange={handleManageBusyChange}
                                onDirtyChange={handleManageDirtyChange}
                                onCancel={() => {
                                    void requestCloseManageDialog()
                                }}
                                onSettingsChanged={() => {
                                    void fetchProducts({ showLoading: false })
                                }}
                            />
                        </div>
                    </div>
                </div>
            )}
        </div>
    )
}
