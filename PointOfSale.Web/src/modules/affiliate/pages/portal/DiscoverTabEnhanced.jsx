/**
 * AF-32: Enhanced Discover Tab with Category Filter Bar
 *
 * STATE MACHINE:
 * - loading   → skeleton shimmer
 * - loaded    → product grid
 * - empty     → contextual empty state
 * - error     → retry banner
 *
 * FILTER ARCHITECTURE:
 * - URL is the SINGLE source of truth (useSearchParams)
 * - Category filter bar reads from URL, emits URL changes
 * - Search reads from URL, debounces, writes back to URL
 * - No duplicated filter state outside URL
 * - AbortController on every fetch to prevent race conditions
 * - Categories fetched ONCE on mount (never refetched on filter change)
 *
 * DEEP LINK SUPPORT:
 * /affiliate/portal?tab=discover&category=electronics&search=iphone
 * → Pre-selects category chip, pre-fills search, loads filtered products
 */
import { useState, useEffect, useCallback, useRef, useMemo, memo } from 'react'
import { useSearchParams } from 'react-router-dom'
import { motion, AnimatePresence } from 'framer-motion'
import affiliateApi from '../../../../services/affiliateApi'
import { Search, Package, Box, Grid3X3, RefreshCw, AlertCircle } from 'lucide-react'
import { useLanguage } from '../../i18n/LanguageContext'
import ProductCard, { ProductCardSkeleton } from './ProductCard'
import ProductDrawer from './ProductDrawer'
import SortDropdown, { DEFAULT_SORT, isValidSort } from './SortDropdown'

const fadeUp = {
    hidden: { opacity: 0, y: 20 },
    visible: (i = 0) => ({ opacity: 1, y: 0, transition: { delay: i * 0.05, duration: 0.4 } })
}

// ═══════════════════════════════════════════════════════════════════
// CATEGORY FILTER BAR (Pure, Stateless)
// ═══════════════════════════════════════════════════════════════════

const CategoryFilterBar = memo(function CategoryFilterBar({ categories, selectedSlug, onChange }) {
    return (
        <div className="flex gap-2 overflow-x-auto pb-1 scrollbar-thin scrollbar-thumb-gray-300 dark:scrollbar-thumb-white/10" id="af32-category-filter-bar">
            {/* All chip */}
            <button
                onClick={() => onChange(null)}
                className={`flex-shrink-0 px-4 py-2 rounded-xl text-sm font-medium transition-all duration-200 ${!selectedSlug
                    ? 'bg-[#6C63FF]/10 text-[#6C63FF] border border-[#6C63FF]/20 shadow-sm'
                    : 'bg-white dark:bg-white/[0.03] text-gray-600 dark:text-gray-400 border border-gray-200 dark:border-white/[0.08] hover:bg-gray-50 dark:hover:bg-white/[0.05] hover:text-gray-900 dark:hover:text-white'
                    }`}
            >
                <span className="flex items-center gap-1.5">
                    <Grid3X3 className="w-3.5 h-3.5" />
                    All
                </span>
            </button>

            {categories.map(cat => (
                <button
                    key={cat.id}
                    onClick={() => {
                        if (cat.slug !== selectedSlug) onChange(cat.slug)
                    }}
                    className={`flex-shrink-0 px-4 py-2 rounded-xl text-sm font-medium transition-all duration-200 ${selectedSlug === cat.slug
                        ? 'bg-[#6C63FF]/10 text-[#6C63FF] border border-[#6C63FF]/20 shadow-sm'
                        : 'bg-white dark:bg-white/[0.03] text-gray-600 dark:text-gray-400 border border-gray-200 dark:border-white/[0.08] hover:bg-gray-50 dark:hover:bg-white/[0.05] hover:text-gray-900 dark:hover:text-white'
                        }`}
                    id={`af32-category-chip-${cat.slug}`}
                >
                    <span className="flex items-center gap-1.5">
                        {cat.name}
                        <span className={`text-[10px] px-1.5 py-0.5 rounded-full ${selectedSlug === cat.slug
                            ? 'bg-[#6C63FF]/20 text-[#6C63FF]'
                            : 'bg-gray-100 dark:bg-white/[0.06] text-gray-500'
                            }`}>
                            {cat.productCount}
                        </span>
                    </span>
                </button>
            ))}
        </div>
    )
})

// ═══════════════════════════════════════════════════════════════════
// DISCOVER TAB (State Machine Owner)
// ═══════════════════════════════════════════════════════════════════

export default function DiscoverTabEnhanced() {
    const { t } = useLanguage()
    const [searchParams, setSearchParams] = useSearchParams()

    // AF-32H FIX: Derive filter state from URL (stable references)
    // Using .toString() for stable dependency tracking
    const paramsString = searchParams.toString()
    const categorySlug = searchParams.get('category') || null
    const urlSearch = searchParams.get('search') || ''
    // AF-33: Sort from URL (validated, with safe fallback)
    const urlSort = searchParams.get('sort') || DEFAULT_SORT
    const activeSort = isValidSort(urlSort) ? urlSort : DEFAULT_SORT

    // ── Local UI state ──
    const [data, setData] = useState(null)
    const [state, setState] = useState('loading') // loading | loaded | empty | error
    const [categories, setCategories] = useState([])
    const [categoriesLoaded, setCategoriesLoaded] = useState(false)
    const [searchInput, setSearchInput] = useState(urlSearch)
    const [page, setPage] = useState(1)
    const [inStockOnly, setInStockOnly] = useState(false)
    const [minCommissionRate, setMinCommissionRate] = useState(null)
    const [selectedProductId, setSelectedProductId] = useState(null)
    const pageSize = 20
    const abortRef = useRef(null)
    const searchTimerRef = useRef(null)
    const mountedRef = useRef(true) // AF-32H: Guard against setState after unmount

    // AF-32H: Cleanup mounted flag
    useEffect(() => {
        mountedRef.current = true
        return () => { mountedRef.current = false }
    }, [])

    // ── Sync search input → URL (debounced) ──
    useEffect(() => {
        if (searchTimerRef.current) clearTimeout(searchTimerRef.current)
        searchTimerRef.current = setTimeout(() => {
            // AF-32H FIX: Compare to current URL value, not captured closure
            if (searchInput !== urlSearch) {
                setSearchParams(prev => {
                    const newParams = new URLSearchParams(prev)
                    newParams.set('tab', 'discover')
                    if (searchInput) {
                        newParams.set('search', searchInput)
                    } else {
                        newParams.delete('search')
                    }
                    return newParams
                }, { replace: true })
                setPage(1)
            }
        }, 400)
        return () => { if (searchTimerRef.current) clearTimeout(searchTimerRef.current) }
    }, [searchInput, urlSearch, setSearchParams])

    // ── Keep searchInput in sync if URL changes externally (back/forward) ──
    // AF-32H FIX: Use stable paramsString dependency instead of searchParams.get()
    useEffect(() => {
        const urlVal = searchParams.get('search') || ''
        setSearchInput(prev => prev !== urlVal ? urlVal : prev)
    }, [paramsString])

    // ── Fetch categories (once on mount) ──
    useEffect(() => {
        affiliateApi.getDiscoverCategories()
            .then(cats => {
                setCategories(cats || [])
                setCategoriesLoaded(true)
            })
            .catch(() => {
                setCategories([])
                setCategoriesLoaded(true)
            })
    }, [])

    // ── Fetch products (re-runs when URL filters, pagination, or stock filter change) ──
    // AF-32H: fetchId pattern prevents stale responses from overwriting newer state
    const fetchIdRef = useRef(0)

    const fetchProducts = useCallback(() => {
        const fetchId = ++fetchIdRef.current

        setState('loading')
        affiliateApi.discoverProductsIntelligence({
            search: urlSearch || undefined,
            inStockOnly,
            minCommissionRate,
            categorySlug: categorySlug || undefined,
            sort: activeSort,
            page,
            pageSize
        })
            .then(d => {
                // AF-32H: Only apply if this is still the latest fetch AND component is mounted
                if (fetchIdRef.current !== fetchId || !mountedRef.current) return
                setData(d)
                if (!d || d.items?.length === 0) {
                    setState('empty')
                } else {
                    setState('loaded')
                }
            })
            .catch(err => {
                if (fetchIdRef.current !== fetchId || !mountedRef.current) return
                if (err?.name === 'CanceledError' || err?.code === 'ERR_CANCELED') return
                setData(null)
                setState('error')
            })
    }, [urlSearch, inStockOnly, minCommissionRate, categorySlug, activeSort, page])

    useEffect(() => { fetchProducts() }, [fetchProducts])

    // ── Category filter change handler ──
    // AF-32H FIX: Use functional setSearchParams to avoid stale searchParams closure
    const handleCategoryChange = useCallback((slug) => {
        setSearchParams(prev => {
            const newParams = new URLSearchParams(prev)
            newParams.set('tab', 'discover')
            if (slug) {
                newParams.set('category', slug)
            } else {
                newParams.delete('category')
            }
            return newParams
        }, { replace: true })
        setPage(1)
    }, [setSearchParams])

    // ── Stock filter toggle ──
    const handleStockToggle = useCallback(() => {
        setInStockOnly(v => !v)
        setPage(1)
    }, [])

    // AF-33: Sort change handler
    const handleSortChange = useCallback((newSort) => {
        setSearchParams(prev => {
            const newParams = new URLSearchParams(prev)
            newParams.set('tab', 'discover')
            newParams.set('sort', newSort)
            return newParams
        }, { replace: true })
        setPage(1)
    }, [setSearchParams])

    // ── Active category name for empty state ──
    // AF-32H FIX: O(1) lookup via Map instead of O(n) .find() per render
    const categoryMap = useMemo(() => {
        const map = new Map()
        categories.forEach(c => map.set(c.slug, c.name))
        return map
    }, [categories])

    const activeCategoryName = categorySlug
        ? categoryMap.get(categorySlug) || categorySlug
        : null

    return (
        <div className="space-y-5">
            {/* ═══ Search & Filters ═══ */}
            <div className="flex flex-col sm:flex-row gap-3">
                <div className="relative flex-1">
                    <Search className="absolute left-4 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-500" />
                    <input
                        type="text"
                        value={searchInput}
                        onChange={(e) => setSearchInput(e.target.value)}
                        placeholder={t('portal.discoverTab.searchPlaceholder') || 'Search products...'}
                        className="w-full pl-11 pr-4 py-3 rounded-xl bg-white dark:bg-white/[0.03] border border-gray-200 dark:border-white/[0.08] text-gray-900 dark:text-white placeholder:text-gray-600 text-sm focus:border-[#6C63FF]/50 focus:ring-2 focus:ring-[#6C63FF]/20 outline-none transition-all"
                        id="af32-discover-search"
                    />
                </div>
                <button
                    onClick={handleStockToggle}
                    className={`px-4 py-3 rounded-xl text-sm font-medium transition-all flex items-center gap-2 ${inStockOnly
                        ? 'bg-emerald-500/10 text-emerald-400 border border-emerald-500/20'
                        : 'bg-white dark:bg-white/[0.03] text-gray-600 dark:text-gray-400 border border-gray-200 dark:border-white/[0.08] hover:bg-gray-100 dark:hover:bg-white/[0.05]'
                        }`}
                >
                    <Box className="w-4 h-4" />
                    In Stock Only
                </button>
            </div>

            {/* ═══ Category Filter Bar ═══ */}
            {categoriesLoaded && categories.length > 0 && (
                <CategoryFilterBar
                    categories={categories}
                    selectedSlug={categorySlug}
                    onChange={handleCategoryChange}
                />
            )}

            {/* ═══ Sort + Results Count ═══ */}
            <div className="flex items-center justify-between">
                <SortDropdown
                    selectedSort={activeSort}
                    onChange={handleSortChange}
                />
                {state === 'loaded' && data && data.totalCount > 0 && (
                    <p className="text-sm text-gray-500">
                        {data.totalCount} {t('portal.discoverTab.productsCount') || 'products found'}
                        {urlSearch && <span> for "{urlSearch}"</span>}
                        {activeCategoryName && <span> in <span className="text-[#6C63FF] font-medium">{activeCategoryName}</span></span>}
                    </p>
                )}
            </div>

            {/* ═══ Loading ═══ */}
            {state === 'loading' && (
                <div className="grid sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4">
                    {[...Array(8)].map((_, i) => (
                        <ProductCardSkeleton key={i} />
                    ))}
                </div>
            )}

            {/* ═══ Error ═══ */}
            {state === 'error' && (
                <div className="text-center py-16">
                    <AlertCircle className="w-12 h-12 text-red-400 mx-auto mb-3" />
                    <h3 className="text-base font-semibold text-gray-900 dark:text-white mb-2">
                        Failed to load products
                    </h3>
                    <p className="text-gray-500 text-sm mb-4">Something went wrong. Please try again.</p>
                    <button
                        onClick={fetchProducts}
                        className="inline-flex items-center gap-2 px-5 py-2 rounded-lg border border-gray-200 dark:border-white/10 text-sm text-gray-700 dark:text-gray-300 hover:bg-gray-50 dark:hover:bg-white/5 transition-all"
                    >
                        <RefreshCw className="w-4 h-4" /> Retry
                    </button>
                </div>
            )}

            {/* ═══ Empty ═══ */}
            {state === 'empty' && (
                <div className="text-center py-16">
                    <Package className="w-14 h-14 text-gray-500 mx-auto mb-4" />
                    <h3 className="text-lg font-semibold text-gray-900 dark:text-white mb-2">
                        {activeCategoryName
                            ? `No products in "${activeCategoryName}"`
                            : (t('portal.discoverTab.noProducts') || 'No products available')
                        }
                    </h3>
                    <p className="text-gray-500 text-sm">
                        {urlSearch
                            ? 'Try adjusting your search or filters'
                            : activeCategoryName
                                ? 'Products will appear here as merchants add them to this category.'
                                : 'No products available at the moment'
                        }
                    </p>
                    {activeCategoryName && (
                        <button
                            onClick={() => handleCategoryChange(null)}
                            className="mt-4 inline-flex items-center gap-2 px-4 py-2 rounded-lg bg-[#6C63FF]/10 text-[#6C63FF] text-sm font-medium border border-[#6C63FF]/20 hover:bg-[#6C63FF]/20 transition-all"
                        >
                            <Grid3X3 className="w-4 h-4" />
                            Browse all products
                        </button>
                    )}
                </div>
            )}

            {/* ═══ Grid ═══ */}
            {state === 'loaded' && data?.items?.length > 0 && (
                <motion.div
                    initial="hidden"
                    animate="visible"
                    variants={{ visible: { transition: { staggerChildren: 0.04 } } }}
                    className="grid sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4"
                >
                    {data.items.map((product, i) => (
                        <motion.div key={product.productId} variants={fadeUp} custom={i}>
                            <ProductCard
                                product={product}
                                onClick={() => setSelectedProductId(product.productId)}
                            />
                        </motion.div>
                    ))}
                </motion.div>
            )}

            {/* ═══ Pagination ═══ */}
            {(state === 'loaded' || state === 'empty') && data && data.totalPages > 1 && (
                <div className="flex items-center justify-between pt-2">
                    <span className="text-sm text-gray-500">
                        {t('portal.discoverTab.page') || 'Page'} {page} {t('portal.discoverTab.of') || 'of'} {data.totalPages}
                    </span>
                    <div className="flex gap-2">
                        <button
                            onClick={() => setPage(p => Math.max(1, p - 1))}
                            disabled={page === 1}
                            className="px-4 py-2 rounded-lg border border-gray-200 dark:border-white/[0.06] text-sm text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-white disabled:opacity-30 transition-all"
                        >
                            {t('portal.discoverTab.prev') || 'Previous'}
                        </button>
                        <button
                            onClick={() => setPage(p => p + 1)}
                            disabled={page >= data.totalPages}
                            className="px-4 py-2 rounded-lg border border-gray-200 dark:border-white/[0.06] text-sm text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-white disabled:opacity-30 transition-all"
                        >
                            {t('portal.discoverTab.next') || 'Next'}
                        </button>
                    </div>
                </div>
            )}

            {/* ═══ Product Detail Drawer ═══ */}
            <AnimatePresence>
                {selectedProductId && (
                    <ProductDrawer
                        productId={selectedProductId}
                        onClose={() => setSelectedProductId(null)}
                    />
                )}
            </AnimatePresence>
        </div>
    )
}
