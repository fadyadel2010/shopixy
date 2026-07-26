/**
 * FE-1: Affiliate Landing Page Discover Component
 * AF-22: i18n support & currency formatting
 * 
 * Public-facing product discovery for non-authenticated users
 * 
 * Differences from portal version:
 * - Hides stock numbers
 * - Hides variants
 * - Shows commission %
 * - CTA → Sign up to promote
 * - No promote buttons
 */
import { useState, useEffect, useCallback } from 'react'
import { motion } from 'framer-motion'
import { Search, Package, TrendingUp, ArrowRight } from 'lucide-react'
import CommissionBadge from '../portal/badges/CommissionBadge'
import { useLanguage } from '../../i18n/LanguageContext'

const fadeUp = {
    hidden: { opacity: 0, y: 20 },
    visible: (i = 0) => ({ opacity: 1, y: 0, transition: { delay: i * 0.05, duration: 0.4 } })
}

export default function AffiliateLandingDiscover({ onSignUpClick }) {
    const { t } = useLanguage()
    const [data, setData] = useState(null)
    const [loading, setLoading] = useState(true)
    const [searchQuery, setSearchQuery] = useState('')
    const [debouncedQuery, setDebouncedQuery] = useState('')
    const [page, setPage] = useState(1)
    const pageSize = 12

    // AF-22: Format currency properly (25 EGP instead of EGP 25.00)
    const formatPrice = (amount, currency = 'EGP') => {
        return `${Math.round(amount)} ${currency}`
    }

    useEffect(() => {
        const timer = setTimeout(() => {
            setDebouncedQuery(searchQuery)
            setPage(1)
        }, 400)
        return () => clearTimeout(timer)
    }, [searchQuery])

    const fetchProducts = useCallback(() => {
        setLoading(true)
        // Use public API endpoint (no auth required)
        fetch(`/api/affiliate/discover/products?page=${page}&pageSize=${pageSize}${debouncedQuery ? `&search=${encodeURIComponent(debouncedQuery)}` : ''}`)
            .then(r => r.json())
            .then(d => setData(d))
            .catch(() => setData(null))
            .finally(() => setLoading(false))
    }, [debouncedQuery, page])

    useEffect(() => { fetchProducts() }, [fetchProducts])

    return (
        <div className="space-y-8">
            {/* Header */}
            <div className="text-center max-w-2xl mx-auto">
                <h2 className="text-3xl font-bold text-gray-900 dark:text-white mb-3">
                    {t('landing.featuredTitle')}
                </h2>
                <p className="text-gray-600 dark:text-gray-400">
                    {t('landing.featuredSubtitle')}
                </p>
            </div>

            {/* Search */}
            <div className="relative max-w-xl mx-auto">
                <Search className="absolute left-4 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-500" />
                <input
                    type="text"
                    value={searchQuery}
                    onChange={(e) => setSearchQuery(e.target.value)}
                    placeholder={t('landing.searchProducts')}
                    className="w-full pl-11 pr-4 py-3 rounded-xl bg-white dark:bg-white/[0.03] border border-gray-200 dark:border-white/[0.08] text-gray-900 dark:text-white placeholder:text-gray-600 text-sm focus:border-[#6C63FF]/50 focus:ring-2 focus:ring-[#6C63FF]/20 outline-none transition-all"
                />
            </div>

            {/* Loading */}
            {loading && (
                <div className="grid sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4">
                    {[...Array(8)].map((_, i) => (
                        <div key={i} className="rounded-xl border border-gray-200 dark:border-white/[0.06] bg-white dark:bg-white/[0.02] p-4 animate-pulse">
                            <div className="aspect-square bg-white dark:bg-white/[0.03] rounded-lg mb-3" />
                            <div className="h-4 bg-white dark:bg-white/[0.03] rounded w-3/4 mb-2" />
                            <div className="h-3 bg-white dark:bg-white/[0.03] rounded w-1/2 mb-3" />
                            <div className="h-9 bg-white dark:bg-white/[0.03] rounded-lg" />
                        </div>
                    ))}
                </div>
            )}

            {/* Empty State */}
            {!loading && (!data || data.items?.length === 0) && (
                <div className="text-center py-20">
                    <Package className="w-14 h-14 text-gray-700 mx-auto mb-4" />
                    <h3 className="text-lg font-semibold text-gray-900 dark:text-white mb-2">{t('landing.noProducts')}</h3>
                    <p className="text-gray-500 text-sm">{t('landing.noProductsDesc')}</p>
                </div>
            )}

            {/* Grid */}
            {!loading && data?.items?.length > 0 && (
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
                            <div className="aspect-square bg-gradient-to-br from-white/[0.03] to-transparent flex items-center justify-center overflow-hidden relative">
                                {product.images && product.images.length > 0 ? (
                                    <img
                                        src={product.images[0]}
                                        alt={product.name}
                                        className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-500"
                                        loading="lazy"
                                    />
                                ) : (
                                    <Package className="w-10 h-10 text-gray-700" />
                                )}

                                {/* Commission Badge */}
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

                                {/* Commission Highlight */}
                                <div className="flex items-center gap-1.5 py-1.5 px-2.5 rounded-lg bg-emerald-500/5 border border-emerald-500/10">
                                    <TrendingUp className="w-3.5 h-3.5 text-emerald-400 flex-shrink-0" />
                                    <span className="text-xs text-emerald-400 font-medium">
                                        {t('landing.youEarn')} {(product.commissionRate * 100).toFixed(0)}% {t('landing.commission')}
                                    </span>
                                </div>

                                {/* CTA Button */}
                                <button
                                    onClick={onSignUpClick}
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

            {/* Pagination */}
            {!loading && data && data.totalPages > 1 && (
                <div className="flex items-center justify-center gap-2">
                    <button
                        onClick={() => setPage(p => Math.max(1, p - 1))}
                        disabled={page === 1}
                        className="px-4 py-2 rounded-lg border border-gray-200 dark:border-white/[0.06] text-sm text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-white disabled:opacity-30 transition-all"
                    >
                        {t('portal.discoverTab.prev')}
                    </button>
                    <span className="text-sm text-gray-500">
                        {t('portal.discoverTab.page')} {page} {t('portal.discoverTab.of')} {data.totalPages}
                    </span>
                    <button
                        onClick={() => setPage(p => p + 1)}
                        disabled={page >= data.totalPages}
                        className="px-4 py-2 rounded-lg border border-gray-200 dark:border-white/[0.06] text-sm text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-white disabled:opacity-30 transition-all"
                    >
                        {t('portal.discoverTab.next')}
                    </button>
                </div>
            )}

            {/* CTA Section */}
            <div className="text-center py-12 border-t border-gray-200 dark:border-white/[0.06]">
                <h3 className="text-2xl font-bold text-gray-900 dark:text-white mb-3">
                    {t('landing.ctaTitle1')} {t('landing.ctaTitle2')}
                </h3>
                <p className="text-gray-600 dark:text-gray-400 mb-6">
                    {t('landing.ctaDesc')}
                </p>
                <button
                    onClick={onSignUpClick}
                    className="px-8 py-3 rounded-lg bg-[#6C63FF] text-gray-900 dark:text-white text-sm font-medium hover:bg-[#5850E6] transition-all inline-flex items-center gap-2"
                >
                    {t('landing.ctaButton')}
                    <ArrowRight className="w-4 h-4" />
                </button>
            </div>
        </div>
    )
}
