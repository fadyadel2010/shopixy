/**
 * AF-15: Discover/Promote Tab — Product grid with promotion link generation
 */
import { useState, useEffect, useCallback } from 'react'
import { motion, AnimatePresence } from 'framer-motion'
import affiliateApi from '../../../../services/affiliateApi'
import {
    Search, Package, Sparkles, Copy, Check, X,
    Share2, MessageCircle, DollarSign
} from 'lucide-react'
import { useLanguage } from '../../i18n/LanguageContext'

const fadeUp = {
    hidden: { opacity: 0, y: 20 },
    visible: (i = 0) => ({ opacity: 1, y: 0, transition: { delay: i * 0.05, duration: 0.4 } })
}

function PromoteModal({ product, onClose, t }) {
    const [copied, setCopied] = useState(false)
    const [promotionUrl, setPromotionUrl] = useState('')
    const [generating, setGenerating] = useState(true)

    useEffect(() => {
        let active = true

        if (!product?.tenantId || !product?.productId) {
            setPromotionUrl('')
            setGenerating(false)
            return () => { active = false }
        }

        setGenerating(true)
        affiliateApi.createPromotionLink(product.tenantId, product.productId)
            .then(link => {
                if (active) {
                    setPromotionUrl(typeof link?.promotionUrl === 'string' ? link.promotionUrl.trim() : '')
                }
            })
            .catch(() => {
                if (active) {
                    setPromotionUrl('')
                }
            })
            .finally(() => {
                if (active) {
                    setGenerating(false)
                }
            })

        return () => { active = false }
    }, [product?.tenantId, product?.productId])

    if (!product) return null

    const link = promotionUrl

    const copyLink = () => {
        if (!link) return
        navigator.clipboard.writeText(link).then(() => {
            setCopied(true)
            setTimeout(() => setCopied(false), 2000)
        })
    }

    const shareLinks = [
        { label: 'WhatsApp', icon: MessageCircle, color: '#25D366', url: `https://wa.me/?text=${encodeURIComponent(`Check out ${product.name}: ${link}`)}` },
        { label: 'Twitter/X', icon: Share2, color: '#1DA1F2', url: `https://twitter.com/intent/tweet?text=${encodeURIComponent(`Check out ${product.name}`)}&url=${encodeURIComponent(link)}` },
    ]

    return (
        <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 bg-gray-900/40 dark:bg-black/60 backdrop-blur-sm z-50 flex items-center justify-center p-4"
            onClick={onClose}
        >
            <motion.div
                initial={{ scale: 0.95, opacity: 0 }}
                animate={{ scale: 1, opacity: 1 }}
                exit={{ scale: 0.95, opacity: 0 }}
                onClick={e => e.stopPropagation()}
                className="w-full max-w-md bg-white/90 dark:bg-[#121826] border border-gray-200 dark:border-white/[0.08] rounded-2xl p-6 shadow-2xl"
            >
                <div className="flex items-center justify-between mb-5">
                    <h3 className="text-lg font-semibold text-gray-900 dark:text-white">{t('portal.discoverTab.modalTitle')}</h3>
                    <button onClick={onClose} className="text-gray-500 hover:text-gray-700 dark:hover:text-gray-300 transition-colors">
                        <X className="w-5 h-5" />
                    </button>
                </div>

                <p className="text-sm text-gray-600 dark:text-gray-400 mb-4">
                    {t('portal.discoverTab.shareDesc')} <span className="text-gray-900 dark:text-white font-medium">{product.name}</span>
                </p>

                {/* Link copy */}
                <div className="flex items-center gap-2 mb-6">
                    <div className="flex-1 px-3 py-3 rounded-lg bg-gray-50 dark:bg-[#0B0F19] border border-gray-200 dark:border-white/[0.08] text-xs text-gray-600 dark:text-gray-400 truncate font-mono">
                        {generating ? 'Generating link...' : (link || 'Promotion link unavailable')}
                    </div>
                    <button
                        onClick={copyLink}
                        disabled={!link || generating}
                        className={`flex-shrink-0 p-3 rounded-lg transition-all ${copied
                            ? 'bg-emerald-500/10 border border-emerald-500/20 text-emerald-400'
                            : 'bg-[#6C63FF]/10 border border-[#6C63FF]/20 text-[#6C63FF] hover:bg-[#6C63FF]/20'
                            } disabled:opacity-40 disabled:cursor-not-allowed`}
                    >
                        {copied ? <Check className="w-4 h-4" /> : <Copy className="w-4 h-4" />}
                    </button>
                </div>

                {/* Share buttons */}
                <p className="text-xs text-gray-500 uppercase tracking-wider mb-3">{t('portal.discoverTab.shareVia')}</p>
                <div className="flex gap-2">
                    {shareLinks.map(s => (
                        <a
                            key={s.label}
                            href={s.url}
                            target="_blank"
                            rel="noopener noreferrer"
                            className={`flex-1 flex items-center justify-center gap-2 py-2.5 rounded-lg border border-gray-200 dark:border-white/[0.06] bg-white dark:bg-white/[0.02] hover:bg-gray-50 dark:hover:bg-white/[0.04] text-sm text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-white transition-all ${!link ? 'pointer-events-none opacity-40' : ''}`}
                        >
                            <s.icon className="w-4 h-4" style={{ color: s.color }} />
                            {s.label}
                        </a>
                    ))}
                </div>
            </motion.div>
        </motion.div>
    )
}

export default function DiscoverTab({ initialSearch = '' }) {
    const { t } = useLanguage()
    const [data, setData] = useState(null)
    const [loading, setLoading] = useState(true)
    const [searchQuery, setSearchQuery] = useState(initialSearch)
    const [debouncedQuery, setDebouncedQuery] = useState(initialSearch)
    const [page, setPage] = useState(1)
    const [referralCode, setReferralCode] = useState(null)
    const [promoteProduct, setPromoteProduct] = useState(null)
    const pageSize = 20

    useEffect(() => {
        affiliateApi.getProfile()
            .then(p => setReferralCode(p.referralCode))
            .catch(() => setReferralCode(null))
    }, [])

    useEffect(() => {
        const timer = setTimeout(() => {
            setDebouncedQuery(searchQuery)
            setPage(1)
        }, 400)
        return () => clearTimeout(timer)
    }, [searchQuery])

    const fetchProducts = useCallback(() => {
        setLoading(true)
        affiliateApi.discoverProducts({ q: debouncedQuery || undefined, page, pageSize })
            .then(d => setData(d))
            .catch(() => setData(null))
            .finally(() => setLoading(false))
    }, [debouncedQuery, page])

    useEffect(() => { fetchProducts() }, [fetchProducts])

    return (
        <div className="space-y-6">
            {/* Search */}
            <div className="relative max-w-xl">
                <Search className="absolute left-4 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-500" />
                <input
                    id="affiliate-discover-search"
                    type="text"
                    value={searchQuery}
                    onChange={(e) => setSearchQuery(e.target.value)}
                    placeholder={t('portal.discoverTab.searchPlaceholder')}
                    className="w-full pl-11 pr-4 py-3 rounded-xl bg-white dark:bg-white/[0.03] border border-gray-200 dark:border-white/[0.08] text-gray-900 dark:text-white placeholder:text-gray-600 text-sm focus:border-[#6C63FF]/50 focus:ring-2 focus:ring-[#6C63FF]/20 outline-none transition-all"
                />
            </div>

            {/* Count */}
            {!loading && data && (
                <p className="text-sm text-gray-500">
                    {data.total} {t('portal.discoverTab.productsCount')}
                    {debouncedQuery && <span> {t('portal.discoverTab.forQuery')} "{debouncedQuery}"</span>}
                </p>
            )}

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

            {/* Empty */}
            {!loading && (!data || data.items?.length === 0) && (
                <div className="text-center py-20">
                    <Package className="w-14 h-14 text-gray-700 mx-auto mb-4" />
                    <h3 className="text-lg font-semibold text-gray-900 dark:text-white mb-2">{t('portal.discoverTab.noProducts')}</h3>
                    <p className="text-gray-500 text-sm">{t('portal.discoverTab.noProductsDesc')}</p>
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
                            <div className="aspect-square bg-gradient-to-br from-white/[0.03] to-transparent flex items-center justify-center overflow-hidden relative">
                                {product.imageUrl ? (
                                    <img src={product.imageUrl} alt={product.name}
                                        className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-500" loading="lazy" />
                                ) : (
                                    <Package className="w-10 h-10 text-gray-700" />
                                )}
                                {/* Commission rate badge */}
                                {product.commissionRate != null && (
                                    <div className="absolute top-2 right-2 px-2 py-1 rounded-lg bg-emerald-500/90 text-white text-[11px] font-bold shadow-lg">
                                        {(product.commissionRate * 100).toFixed(0)}% {t('portal.discoverTab.commissionLabel')}
                                    </div>
                                )}
                            </div>
                            <div className="p-4 space-y-2">
                                <h4 className="text-sm font-medium text-gray-900 dark:text-white line-clamp-2 leading-snug min-h-[2.5rem]">{product.name}</h4>
                                <div className="flex items-center justify-between">
                                    <span className="text-base font-bold text-gray-900 dark:text-white">
                                        {product.priceFrom != null ? `EGP ${product.priceFrom.toFixed(2)}` : '—'}
                                    </span>
                                    {product.inStock && (
                                        <span className="text-[10px] px-2 py-0.5 rounded-full bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">
                                            {t('portal.discoverTab.inStock')}
                                        </span>
                                    )}
                                </div>
                                {/* Estimated commission */}
                                {product.estimatedCommission != null && (
                                    <div className="flex items-center gap-1.5 py-1.5 px-2.5 rounded-lg bg-emerald-500/5 border border-emerald-500/10">
                                        <DollarSign className="w-3.5 h-3.5 text-emerald-400" />
                                        <span className="text-xs text-emerald-400 font-medium">
                                            {t('portal.discoverTab.youEarn')} <span className="font-bold">EGP {product.estimatedCommission.toFixed(2)}</span>
                                        </span>
                                    </div>
                                )}
                                <button
                                    onClick={() => setPromoteProduct(product)}
                                    disabled={!referralCode}
                                    className="w-full mt-1 py-2 rounded-lg bg-[#6C63FF]/10 text-[#6C63FF] text-sm font-medium border border-[#6C63FF]/20 hover:bg-[#6C63FF]/20 disabled:opacity-40 transition-all flex items-center justify-center gap-1.5"
                                >
                                    <Sparkles className="w-3.5 h-3.5" />
                                    {t('portal.discoverTab.promoteAction')}
                                </button>
                            </div>
                        </motion.div>
                    ))}
                </motion.div>
            )}

            {/* Pagination */}
            {!loading && data && Math.ceil(data.total / pageSize) > 1 && (
                <div className="flex items-center justify-between pt-2">
                    <span className="text-sm text-gray-500">
                        {t('portal.discoverTab.page')} {page} {t('portal.discoverTab.of')} {Math.ceil(data.total / pageSize)}
                    </span>
                    <div className="flex gap-2">
                        <button onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page === 1}
                            className="px-4 py-2 rounded-lg border border-gray-200 dark:border-white/[0.06] text-sm text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-white disabled:opacity-30 transition-all">
                            {t('portal.discoverTab.prev')}
                        </button>
                        <button onClick={() => setPage(p => p + 1)} disabled={page >= Math.ceil(data.total / pageSize)}
                            className="px-4 py-2 rounded-lg border border-gray-200 dark:border-white/[0.06] text-sm text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-white disabled:opacity-30 transition-all">
                            {t('portal.discoverTab.next')}
                        </button>
                    </div>
                </div>
            )}

            {/* Promote Modal */}
            <AnimatePresence>
                {promoteProduct && (
                    <PromoteModal
                        product={promoteProduct}
                        onClose={() => setPromoteProduct(null)}
                        t={t}
                    />
                )}
            </AnimatePresence>
        </div>
    )
}
