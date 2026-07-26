/**
 * AF-31: Affiliate Category Products Page
 * Route: /affiliate/category/:slug
 *
 * STATE MACHINE:
 * - loading  → skeleton shimmer
 * - loaded   → products grid
 * - empty    → "No products in this category yet"
 * - notfound → 404 page
 * - error    → retry banner
 *
 * RULES:
 * - Supports direct deep-link access (no landing page preload)
 * - Uses categorySlug filter on existing discover/products endpoint
 * - AbortController on unmount
 * - SEO: dynamic title tag
 * - Link-based navigation back to landing
 */
import { useState, useEffect, useRef, useCallback } from 'react'
import { useParams, Link, useNavigate, useSearchParams } from 'react-router-dom'
import { motion } from 'framer-motion'
import {
    Search, Package, ArrowLeft, ArrowRight, RefreshCw,
    AlertCircle, TrendingUp, Sparkles, DollarSign,
    ChevronRight
} from 'lucide-react'
import { useLanguage } from '../i18n/LanguageContext'
import LanguageSwitcher from '../components/LanguageSwitcher'
import ThemeSwitcher from '../components/ThemeSwitcher'
import { affiliateAuthApi } from '../../../services/affiliateAuthApi'
import CommissionBadge from './portal/badges/CommissionBadge'
import SortDropdown, { DEFAULT_SORT, isValidSort } from './portal/SortDropdown'

const fadeUp = {
    hidden: { opacity: 0, y: 20 },
    visible: (i = 0) => ({ opacity: 1, y: 0, transition: { delay: i * 0.05, duration: 0.4 } })
}

export default function AffiliateCategoryPage() {
    const { slug } = useParams()
    const navigate = useNavigate()
    const [searchParams, setSearchParams] = useSearchParams()
    const { t, dir, isRTL } = useLanguage()

    // AF-33: Sort from URL
    const urlSort = searchParams.get('sort') || DEFAULT_SORT
    const activeSort = isValidSort(urlSort) ? urlSort : DEFAULT_SORT

    const [state, setState] = useState('loading') // loading | loaded | empty | notfound | error
    const [data, setData] = useState(null)
    const [categoryName, setCategoryName] = useState('')
    const [searchQuery, setSearchQuery] = useState('')
    const [debouncedQuery, setDebouncedQuery] = useState('')
    const [page, setPage] = useState(1)
    const pageSize = 20
    const abortRef = useRef(null)
    const fetchIdRef = useRef(0) // AF-32H: Prevent stale responses
    const mountedRef = useRef(true)

    // AF-22: Format currency properly
    const formatPrice = (amount, currency = 'EGP') => {
        return `${Math.round(amount)} ${currency}`
    }

    // Debounce search
    useEffect(() => {
        const timer = setTimeout(() => {
            setDebouncedQuery(searchQuery)
            setPage(1)
        }, 400)
        return () => clearTimeout(timer)
    }, [searchQuery])

    // Fetch products with category filter
    const fetchProducts = useCallback(() => {
        const fetchId = ++fetchIdRef.current
        setState('loading')
        if (abortRef.current) abortRef.current.abort()
        const controller = new AbortController()
        abortRef.current = controller

        const params = new URLSearchParams({
            page: page.toString(),
            pageSize: pageSize.toString(),
            categorySlug: slug,
            sort: activeSort
        })
        if (debouncedQuery) params.set('search', debouncedQuery)

        fetch(`/api/affiliate/discover/products?${params}`, { signal: controller.signal })
            .then(r => {
                if (!r.ok) throw new Error(`HTTP ${r.status}`)
                return r.json()
            })
            .then(d => {
                // AF-32H: Only apply if still latest fetch and mounted
                if (fetchIdRef.current !== fetchId || !mountedRef.current) return
                setData(d)
                if (!d || d.items?.length === 0) {
                    setState(d?.totalCount === 0 && !debouncedQuery ? 'empty' : 'loaded')
                } else {
                    setState('loaded')
                }
            })
            .catch(err => {
                if (fetchIdRef.current !== fetchId || !mountedRef.current) return
                if (err.name === 'AbortError') return
                console.error('[AF-31] Failed to fetch category products:', err)
                setState('error')
            })
    }, [slug, debouncedQuery, activeSort, page])

    // Resolve category name from discover/categories
    useEffect(() => {
        fetch('/api/affiliate/discover/categories')
            .then(r => r.json())
            .then(cats => {
                const found = cats?.find(c => c.slug === slug)
                if (found) {
                    setCategoryName(found.name)
                } else {
                    setCategoryName(slug.replace(/-/g, ' ').replace(/\b\w/g, l => l.toUpperCase()))
                }
            })
            .catch(() => {
                setCategoryName(slug.replace(/-/g, ' ').replace(/\b\w/g, l => l.toUpperCase()))
            })
    }, [slug])

    useEffect(() => { fetchProducts() }, [fetchProducts])
    useEffect(() => {
        mountedRef.current = true
        return () => {
            mountedRef.current = false
            if (abortRef.current) abortRef.current.abort()
        }
    }, [])

    // Dynamic title
    useEffect(() => {
        document.title = categoryName
            ? `${categoryName} | Affiliate Marketplace`
            : 'Category | Affiliate Marketplace'
    }, [categoryName])

    const handlePromote = () => {
        if (!affiliateAuthApi.isAuthenticated()) {
            navigate('/affiliate/register')
        } else {
            // AF-32H FIX: URL param is 'category' not 'categorySlug' to match AF-32
            navigate(`/affiliate/portal?tab=discover&category=${encodeURIComponent(slug)}`)
        }
    }

    return (
        <div dir={dir} className="min-h-screen bg-gray-50 dark:bg-[#0B0F19] text-gray-800 dark:text-gray-200" style={{ fontFamily: isRTL ? "'Noto Kufi Arabic', 'Inter', sans-serif" : "'Inter', -apple-system, system-ui, sans-serif" }}>
            {/* ═══ Header ═══ */}
            <header className="fixed top-0 left-0 right-0 z-50 border-b border-gray-200 dark:border-white/5 backdrop-blur-xl bg-gray-50 dark:bg-[#0B0F19]/70">
                <div className="max-w-6xl mx-auto px-6 h-16 flex items-center justify-between">
                    <Link to="/affiliate" className="flex items-center gap-3 no-underline">
                        <div className="w-9 h-9 rounded-xl bg-gradient-to-br from-[#6C63FF] to-[#8B5CF6] flex items-center justify-center shadow-lg shadow-[#6C63FF]/20">
                            <DollarSign className="w-5 h-5 text-white" />
                        </div>
                        <span className="text-lg font-bold bg-gradient-to-r from-[#6C63FF] to-[#A855F7] bg-clip-text text-transparent">
                            {t('landing.footerAffiliate')}
                        </span>
                    </Link>
                    <div className="flex items-center gap-3">
                        <ThemeSwitcher />
                        <LanguageSwitcher />
                        <Link
                            to="/affiliate/login"
                            className="px-4 py-2 text-sm text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-white transition-colors no-underline"
                        >
                            {t('landing.ctaLogin')}
                        </Link>
                        <Link
                            to="/affiliate/register"
                            className="px-5 py-2.5 rounded-lg bg-[#6C63FF]/10 text-[#6C63FF] border border-[#6C63FF]/20 hover:bg-[#6C63FF]/20 text-sm font-medium transition-all no-underline"
                        >
                            {t('landing.ctaJoin')}
                        </Link>
                    </div>
                </div>
            </header>
            <div className="h-16" />

            {/* ═══ Content ═══ */}
            <div className="max-w-6xl mx-auto px-6 py-10">
                {/* Breadcrumb */}
                <nav className="flex items-center gap-2 text-sm text-gray-500 mb-6">
                    <Link to="/affiliate" className="hover:text-gray-900 dark:hover:text-white transition-colors no-underline text-gray-500">
                        Marketplace
                    </Link>
                    <ChevronRight className="w-3.5 h-3.5" />
                    <span className="text-gray-900 dark:text-white font-medium">
                        {categoryName || slug}
                    </span>
                </nav>

                {/* Category Header */}
                <div className="flex items-center justify-between mb-8">
                    <div>
                        <h1 className="text-3xl md:text-4xl font-bold text-gray-900 dark:text-white mb-2">
                            {categoryName || slug}
                        </h1>
                        {data && (
                            <p className="text-gray-500">
                                {data.totalCount?.toLocaleString() || 0} products available
                            </p>
                        )}
                    </div>
                    <Link
                        to="/affiliate"
                        className="hidden md:inline-flex items-center gap-2 px-4 py-2 rounded-lg border border-gray-200 dark:border-white/10 text-sm text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-white hover:border-gray-300 dark:hover:border-white/20 transition-all no-underline"
                    >
                        <ArrowLeft className="w-4 h-4" />
                        All Categories
                    </Link>
                </div>

                {/* AF-33: Sort + Search */}
                <div className="flex flex-col sm:flex-row gap-3 mb-8">
                    <div className="relative flex-1">
                        <Search className="absolute left-4 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-500" />
                        <input
                            type="text"
                            value={searchQuery}
                            onChange={(e) => setSearchQuery(e.target.value)}
                            placeholder={`Search in ${categoryName || 'category'}...`}
                            className="w-full pl-11 pr-4 py-3 rounded-xl bg-white dark:bg-white/[0.03] border border-gray-200 dark:border-white/[0.08] text-gray-900 dark:text-white placeholder:text-gray-500 text-sm focus:border-[#6C63FF]/50 focus:ring-2 focus:ring-[#6C63FF]/20 outline-none transition-all"
                            id="af31-category-search"
                        />
                    </div>
                    <SortDropdown
                        selectedSort={activeSort}
                        onChange={(newSort) => {
                            setSearchParams(prev => {
                                const p = new URLSearchParams(prev)
                                p.set('sort', newSort)
                                return p
                            }, { replace: true })
                            setPage(1)
                        }}
                    />
                </div>

                {/* Loading */}
                {state === 'loading' && (
                    <div className="grid sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4">
                        {[...Array(8)].map((_, i) => (
                            <div key={i} className="rounded-xl border border-gray-200 dark:border-white/[0.06] bg-white dark:bg-white/[0.02] p-4 animate-pulse">
                                <div className="aspect-square bg-gray-100 dark:bg-white/[0.03] rounded-lg mb-3" />
                                <div className="h-4 bg-gray-100 dark:bg-white/[0.03] rounded w-3/4 mb-2" />
                                <div className="h-3 bg-gray-100 dark:bg-white/[0.03] rounded w-1/2 mb-3" />
                                <div className="h-9 bg-gray-100 dark:bg-white/[0.03] rounded-lg" />
                            </div>
                        ))}
                    </div>
                )}

                {/* Error */}
                {state === 'error' && (
                    <div className="text-center py-20">
                        <AlertCircle className="w-12 h-12 text-red-400 mx-auto mb-4" />
                        <h3 className="text-lg font-semibold text-gray-900 dark:text-white mb-2">
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

                {/* Empty */}
                {state === 'empty' && (
                    <div className="text-center py-20">
                        <Package className="w-14 h-14 text-gray-400 mx-auto mb-4" />
                        <h3 className="text-lg font-semibold text-gray-900 dark:text-white mb-2">
                            No products in this category yet
                        </h3>
                        <p className="text-gray-500 text-sm mb-6">
                            Products will appear here as merchants add them to this category. Check back soon!
                        </p>
                        <Link
                            to="/affiliate"
                            className="inline-flex items-center gap-2 px-5 py-2.5 rounded-lg bg-[#6C63FF]/10 text-[#6C63FF] border border-[#6C63FF]/20 hover:bg-[#6C63FF]/20 text-sm font-medium transition-all no-underline"
                        >
                            <ArrowLeft className="w-4 h-4" />
                            Browse all categories
                        </Link>
                    </div>
                )}

                {/* Products Grid */}
                {state === 'loaded' && data?.items?.length > 0 && (
                    <motion.div
                        initial="hidden"
                        animate="visible"
                        variants={{ visible: { transition: { staggerChildren: 0.04 } } }}
                        className="grid sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4"
                    >
                        {data.items.map((product, i) => (
                            <motion.div
                                key={product.productId}
                                variants={fadeUp}
                                custom={i}
                                className="group rounded-xl border border-gray-200 dark:border-white/[0.06] bg-white dark:bg-white/[0.02] hover:bg-gray-50 dark:hover:bg-white/[0.04] hover:border-[#6C63FF]/20 overflow-hidden transition-all"
                            >
                                {/* Image */}
                                <div className="aspect-square bg-gradient-to-br from-gray-50 to-gray-100 dark:from-white/[0.03] dark:to-transparent flex items-center justify-center overflow-hidden relative">
                                    {product.images && product.images.length > 0 ? (
                                        <img
                                            src={product.images[0]}
                                            alt={product.name}
                                            className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-500"
                                            loading="lazy"
                                        />
                                    ) : (
                                        <Package className="w-10 h-10 text-gray-400" />
                                    )}
                                    <div className="absolute top-2 right-2">
                                        <CommissionBadge commissionRate={product.commissionRate} />
                                    </div>
                                </div>

                                {/* Content */}
                                <div className="p-4 space-y-2">
                                    <h4 className="text-sm font-medium text-gray-900 dark:text-white line-clamp-2 leading-snug min-h-[2.5rem]">
                                        {product.name}
                                    </h4>

                                    <div className="flex items-center justify-between">
                                        <span className="text-base font-bold text-gray-900 dark:text-white">
                                            {formatPrice(product.price, product.currency)}
                                        </span>
                                        <span className="text-xs text-gray-500 truncate max-w-[100px]">
                                            {product.storeName}
                                        </span>
                                    </div>

                                    {/* Estimated commission */}
                                    {product.commissionRate != null && (
                                        <div className="flex items-center gap-1.5 py-1.5 px-2.5 rounded-lg bg-emerald-500/5 border border-emerald-500/10">
                                            <TrendingUp className="w-3.5 h-3.5 text-emerald-400 flex-shrink-0" />
                                            <span className="text-xs text-emerald-400 font-medium">
                                                Earn {(product.commissionRate * 100).toFixed(0)}% commission
                                            </span>
                                        </div>
                                    )}

                                    {/* CTA */}
                                    <button
                                        onClick={handlePromote}
                                        className="w-full mt-1 py-2 rounded-lg bg-[#6C63FF]/10 text-[#6C63FF] text-sm font-medium border border-[#6C63FF]/20 hover:bg-[#6C63FF]/20 transition-all flex items-center justify-center gap-1.5"
                                    >
                                        {t('landing.ctaJoin')}
                                        <ArrowRight className="w-3.5 h-3.5" />
                                    </button>
                                </div>
                            </motion.div>
                        ))}
                    </motion.div>
                )}

                {/* No results after search */}
                {state === 'loaded' && data?.items?.length === 0 && debouncedQuery && (
                    <div className="text-center py-16">
                        <Search className="w-12 h-12 text-gray-400 mx-auto mb-4" />
                        <h3 className="text-lg font-semibold text-gray-900 dark:text-white mb-2">
                            No products found for "{debouncedQuery}"
                        </h3>
                        <p className="text-gray-500 text-sm">
                            Try a different search term or browse all products in this category.
                        </p>
                    </div>
                )}

                {/* Pagination */}
                {state === 'loaded' && data && data.totalPages > 1 && (
                    <div className="flex items-center justify-center gap-3 mt-10">
                        <button
                            onClick={() => setPage(p => Math.max(1, p - 1))}
                            disabled={page === 1}
                            className="px-4 py-2 rounded-lg border border-gray-200 dark:border-white/[0.06] text-sm text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-white disabled:opacity-30 transition-all"
                        >
                            Previous
                        </button>
                        <span className="text-sm text-gray-500">
                            Page {page} of {data.totalPages}
                        </span>
                        <button
                            onClick={() => setPage(p => p + 1)}
                            disabled={page >= data.totalPages}
                            className="px-4 py-2 rounded-lg border border-gray-200 dark:border-white/[0.06] text-sm text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-white disabled:opacity-30 transition-all"
                        >
                            Next
                        </button>
                    </div>
                )}
            </div>
        </div>
    )
}
