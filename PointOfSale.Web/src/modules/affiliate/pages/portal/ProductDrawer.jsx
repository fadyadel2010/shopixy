/**
 * FE-1: Product Detail Drawer Component
 * FE-1A: Micro-hardening & UX polish
 * AF-22: i18n support & currency formatting
 * 
 * Slides from right with full product intelligence:
 * 1. Gallery carousel
 * 2. Price & Earnings
 * 3. Inventory (total stock + variants)
 * 4. Store section
 * 5. Promotion actions
 * 
 * Affiliate link: backend-issued promotionUrl.
 */
import { useState, useEffect, useMemo } from 'react'
import { motion } from 'framer-motion'
import affiliateApi from '../../../../services/affiliateApi'
import {
    X, ExternalLink, Copy, Check, MessageCircle, Share2,
    TrendingUp, AlertCircle, ChevronLeft, ChevronRight
} from 'lucide-react'
import StockBadge from './badges/StockBadge'
import { useLanguage } from '../../i18n/LanguageContext'

const resolveProductCurrency = (product) => {
    const currency = typeof product?.currency === 'string' ? product.currency.trim().toUpperCase() : ''
    return currency || null
}

export default function ProductDrawer({ productId, onClose }) {
    const { t } = useLanguage()
    const [product, setProduct] = useState(null)
    const [loading, setLoading] = useState(true)
    const [generatingLink, setGeneratingLink] = useState(false)
    const [promoLink, setPromoLink] = useState(null)
    const [copied, setCopied] = useState(false)
    const [currentImageIndex, setCurrentImageIndex] = useState(0)

    // AF-22: Format currency properly (25 EGP instead of EGP 25.00)
    const formatPrice = (amount) => {
        const currency = resolveProductCurrency(product)
        return currency ? `${Math.round(amount)} ${currency}` : 'Currency unavailable'
    }

    // FE-1A: Prevent double fetch - guard against empty productId
    useEffect(() => {
        if (!productId) return
        setLoading(true)
        affiliateApi.getProductIntelligence(productId)
            .then(p => {
                setProduct(p)
                // AF-15: Auto-generate the backend promotion link record
                if (p.tenantId && p.productId) {
                    setGeneratingLink(true)
                    affiliateApi.createPromotionLink(p.tenantId, p.productId)
                        .then(link => setPromoLink(link))
                        .catch(err => console.warn('[ProductDrawer] Failed to generate promotion link:', err?.message))
                        .finally(() => setGeneratingLink(false))
                }
            })
            .catch(() => setProduct(null))
            .finally(() => setLoading(false))
    }, [productId])

    // FE-1A: Close drawer with ESC key
    useEffect(() => {
        const handler = (e) => {
            if (e.key === 'Escape') onClose()
        }
        window.addEventListener('keydown', handler)
        return () => window.removeEventListener('keydown', handler)
    }, [onClose])

    // PASS F3: URL copy/display uses the backend-issued promotionUrl only.
    const affiliateLink = useMemo(() => {
        if (typeof promoLink?.promotionUrl !== 'string') return ''
        return promoLink.promotionUrl.trim()
    }, [promoLink?.promotionUrl])

    if (!productId) return null

    const copyLink = () => {
        if (!affiliateLink) return
        navigator.clipboard.writeText(affiliateLink).then(() => {
            setCopied(true)
            setTimeout(() => setCopied(false), 2000)
            // FE-1A: Copy feedback (optional toast - implement if toast library available)
            // toast?.success?.('Affiliate link copied')
        })
    }

    // FE-1A: Defensive optional chaining in image navigation
    const nextImage = () => {
        if (!product?.images || product.images.length <= 1) return
        setCurrentImageIndex((i) => (i + 1) % product.images.length)
    }

    const prevImage = () => {
        if (!product?.images || product.images.length <= 1) return
        setCurrentImageIndex((i) => (i - 1 + product.images.length) % product.images.length)
    }

    return (
        <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 bg-gray-900/40 dark:bg-black/60 backdrop-blur-sm z-50 flex items-end sm:items-center justify-end"
            onClick={onClose}
        >
            <motion.div
                initial={{ x: '100%' }}
                animate={{ x: 0 }}
                exit={{ x: '100%' }}
                transition={{ type: 'spring', damping: 30, stiffness: 300 }}
                onClick={e => e.stopPropagation()}
                className="w-full sm:w-[480px] h-full bg-white dark:bg-[#0D1117] border-l border-gray-200 dark:border-white/[0.08] overflow-y-auto"
            >
                {loading && <DrawerSkeleton />}

                {!loading && product && (
                    <div className="p-6 space-y-6">
                        {/* Header */}
                        <div className="flex items-start justify-between">
                            <div className="flex-1">
                                {/* FE-1A: Defensive optional chaining */}
                                <h2 className="text-xl font-bold text-gray-900 dark:text-white mb-1">{product?.name || 'Product'}</h2>
                                <p className="text-sm text-gray-600 dark:text-gray-400">{product?.storeName || 'Store'}</p>
                            </div>
                            <button
                                onClick={onClose}
                                className="p-2 rounded-lg hover:bg-gray-100 dark:hover:bg-white/[0.05] text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-white transition-colors"
                            >
                                <X className="w-5 h-5" />
                            </button>
                        </div>

                        {/* Gallery Carousel */}
                        {product?.images && product.images.length > 0 && (
                            <div className="relative aspect-square rounded-xl overflow-hidden bg-white dark:bg-white/[0.02] border border-gray-200 dark:border-white/[0.06]">
                                {/* FE-1A: Image fallback for broken thumbnails */}
                                <img
                                    src={product.images[currentImageIndex]}
                                    alt={product?.name || 'Product'}
                                    className="w-full h-full object-cover"
                                    onError={(e) => {
                                        e.currentTarget.src = '/img/placeholder.png'
                                    }}
                                />
                                {product.images.length > 1 && (
                                    <>
                                        <button
                                            onClick={prevImage}
                                            className="absolute left-2 top-1/2 -translate-y-1/2 p-2 rounded-full bg-gray-900/40 dark:bg-black/50 hover:bg-gray-900/40 dark:bg-black/70 text-gray-900 dark:text-white transition-colors"
                                        >
                                            <ChevronLeft className="w-5 h-5" />
                                        </button>
                                        <button
                                            onClick={nextImage}
                                            className="absolute right-2 top-1/2 -translate-y-1/2 p-2 rounded-full bg-gray-900/40 dark:bg-black/50 hover:bg-gray-900/40 dark:bg-black/70 text-gray-900 dark:text-white transition-colors"
                                        >
                                            <ChevronRight className="w-5 h-5" />
                                        </button>
                                        <div className="absolute bottom-2 left-1/2 -translate-x-1/2 flex gap-1">
                                            {product.images.map((_, i) => (
                                                <div
                                                    key={i}
                                                    className={`w-1.5 h-1.5 rounded-full transition-colors ${i === currentImageIndex ? 'bg-white' : 'bg-white/30'
                                                        }`}
                                                />
                                            ))}
                                        </div>
                                    </>
                                )}
                            </div>
                        )}

                        {/* Price & Earnings */}
                        <div className="grid grid-cols-2 gap-3">
                            <div className="p-4 rounded-xl bg-white dark:bg-white/[0.02] border border-gray-200 dark:border-white/[0.06]">
                                <p className="text-xs text-gray-500 mb-1">{t('portal.drawer.productPrice')}</p>
                                {/* FE-1: Variant price rule | FE-1A: Defensive optional chaining | AF-22: Currency formatting */}
                                {product?.variants && product.variants.length > 0 ? (
                                    <p className="text-lg font-bold text-gray-900 dark:text-white">
                                        {t('portal.badges.from')} {formatPrice(Math.min(...product.variants.map(v => v.price)))}
                                    </p>
                                ) : (
                                    <p className="text-lg font-bold text-gray-900 dark:text-white">
                                        {formatPrice(product?.price || 0)}
                                    </p>
                                )}
                            </div>
                            <div className="p-4 rounded-xl bg-emerald-500/5 border border-emerald-500/20">
                                <p className="text-xs text-emerald-400 mb-1">{t('portal.drawer.yourCommission')}</p>
                                <p className="text-lg font-bold text-emerald-400">
                                    {formatPrice(product?.estimatedCommission || 0)}
                                </p>
                                <p className="text-[10px] text-emerald-400/70 mt-0.5">
                                    {((product?.commissionRate || 0) * 100).toFixed(0)}% {t('portal.drawer.rate')}
                                </p>
                            </div>
                        </div>

                        {/* Earnings Breakdown | FE-1A: Defensive optional chaining | AF-22: Currency formatting */}
                        <div className="p-4 rounded-xl bg-emerald-500/5 border border-emerald-500/10">
                            <div className="flex items-center gap-2 mb-2">
                                <TrendingUp className="w-4 h-4 text-emerald-400" />
                                <p className="text-sm font-medium text-emerald-400">{t('portal.drawer.youEarnPerSale')}</p>
                            </div>
                            <p className="text-2xl font-bold text-emerald-400">
                                {formatPrice(product?.estimatedCommission || 0)}
                            </p>
                            <p className="text-xs text-emerald-400/70 mt-1">
                                {t('portal.drawer.basedOn')} {((product?.commissionRate || 0) * 100).toFixed(0)}% {t('portal.drawer.commissionRate')}
                            </p>
                        </div>

                        {/* Inventory Status | FE-1A: Defensive optional chaining */}
                        <div className="p-4 rounded-xl bg-white dark:bg-white/[0.02] border border-gray-200 dark:border-white/[0.06]">
                            <div className="flex items-center justify-between mb-3">
                                <p className="text-sm font-medium text-gray-900 dark:text-white">{t('portal.drawer.inventoryStatus')}</p>
                                <StockBadge isOutOfStock={product?.isOutOfStock} lowStock={product?.lowStock} />
                            </div>
                            <p className="text-xs text-gray-600 dark:text-gray-400">
                                {t('portal.drawer.totalAvailable')}: <span className="text-gray-900 dark:text-white font-medium">{product?.totalStock || 0} {t('portal.drawer.units')}</span>
                            </p>
                        </div>

                        {/* Variants | FE-1A: Defensive optional chaining */}
                        {product?.variants && product.variants.length > 0 && (
                            <div className="space-y-3">
                                <p className="text-sm font-medium text-gray-900 dark:text-white">Available Variants</p>
                                <div className="space-y-2">
                                    {product.variants.map(v => (
                                        <div
                                            key={v.variantId}
                                            className="p-3 rounded-lg bg-white dark:bg-white/[0.02] border border-gray-200 dark:border-white/[0.06] flex items-center justify-between"
                                        >
                                            <div>
                                                <p className="text-sm text-gray-900 dark:text-white font-medium">{v?.name || 'Variant'}</p>
                                                <p className="text-xs text-gray-600 dark:text-gray-400">
                                                    {formatPrice(v?.price || 0)}
                                                </p>
                                            </div>
                                            <div className="text-right">
                                                <p className="text-xs text-gray-600 dark:text-gray-400">Stock</p>
                                                <p className={`text-sm font-medium ${(v?.stock || 0) > 0 ? 'text-emerald-400' : 'text-red-400'}`}>
                                                    {v?.stock || 0}
                                                </p>
                                            </div>
                                        </div>
                                    ))}
                                </div>
                            </div>
                        )}

                        {/* Description | FE-1A: Defensive optional chaining */}
                        {product?.description && (
                            <div className="space-y-2">
                                <p className="text-sm font-medium text-gray-900 dark:text-white">Description</p>
                                <p className="text-sm text-gray-600 dark:text-gray-400 leading-relaxed">{product.description}</p>
                            </div>
                        )}

                        {/* Store Section | FE-1A: Defensive optional chaining & image fallback */}
                        <div className="p-4 rounded-xl bg-white dark:bg-white/[0.02] border border-gray-200 dark:border-white/[0.06]">
                            <p className="text-xs text-gray-500 mb-2">Store</p>
                            <div className="flex items-center gap-3">
                                {product?.storeLogo && (
                                    <img
                                        src={product.storeLogo}
                                        alt={product?.storeName || 'Store'}
                                        className="w-10 h-10 rounded-lg"
                                        onError={(e) => {
                                            e.currentTarget.style.display = 'none'
                                        }}
                                    />
                                )}
                                <div>
                                    <p className="text-sm font-medium text-gray-900 dark:text-white">{product?.storeName || 'Store'}</p>
                                    <p className="text-xs text-gray-600 dark:text-gray-400">@{product?.storeSlug || 'store'}</p>
                                </div>
                            </div>
                        </div>

                        {/* Promotion Actions */}
                        <div className="space-y-3 pt-2 border-t border-gray-200 dark:border-white/[0.06]">
                            <p className="text-sm font-medium text-gray-900 dark:text-white">Promote This Product</p>

                            {/* Copy Link */}
                            <div className="flex items-center gap-2">
                                <div className="flex-1 px-3 py-2.5 rounded-lg bg-gray-50 dark:bg-[#0B0F19] border border-gray-200 dark:border-white/[0.08] text-xs text-gray-600 dark:text-gray-400 truncate font-mono">
                                    {generatingLink ? 'Generating link...' : (affiliateLink || 'Promotion link unavailable')}
                                </div>
                                <button
                                    onClick={copyLink}
                                    disabled={!affiliateLink || generatingLink}
                                    className={`flex-shrink-0 p-2.5 rounded-lg transition-all ${copied
                                            ? 'bg-emerald-500/10 border border-emerald-500/20 text-emerald-400'
                                            : 'bg-[#6C63FF]/10 border border-[#6C63FF]/20 text-[#6C63FF] hover:bg-[#6C63FF]/20'
                                        } disabled:opacity-40`}
                                >
                                    {copied ? <Check className="w-4 h-4" /> : <Copy className="w-4 h-4" />}
                                </button>
                            </div>

                            {/* Share Buttons | FE-1A: Defensive optional chaining */}
                            <div className="grid grid-cols-2 gap-2">
                                <a
                                    href={`https://wa.me/?text=${encodeURIComponent(`Check out ${product?.name || 'this product'}: ${affiliateLink}`)}`}
                                    target="_blank"
                                    rel="noopener noreferrer"
                                    className={`flex items-center justify-center gap-2 py-2.5 rounded-lg border border-gray-200 dark:border-white/[0.06] bg-white dark:bg-white/[0.02] hover:bg-gray-50 dark:hover:bg-white/[0.04] text-sm text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-white transition-all ${!affiliateLink ? 'pointer-events-none opacity-40' : ''}`}
                                >
                                    <MessageCircle className="w-4 h-4" style={{ color: '#25D366' }} />
                                    WhatsApp
                                </a>
                                <a
                                    href={`https://www.facebook.com/sharer/sharer.php?u=${encodeURIComponent(affiliateLink)}`}
                                    target="_blank"
                                    rel="noopener noreferrer"
                                    className={`flex items-center justify-center gap-2 py-2.5 rounded-lg border border-gray-200 dark:border-white/[0.06] bg-white dark:bg-white/[0.02] hover:bg-gray-50 dark:hover:bg-white/[0.04] text-sm text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-white transition-all ${!affiliateLink ? 'pointer-events-none opacity-40' : ''}`}
                                >
                                    <Share2 className="w-4 h-4" style={{ color: '#1877F2' }} />
                                    Facebook
                                </a>
                            </div>

                            {/* Visit Storefront */}
                            <a
                                href={affiliateLink}
                                target="_blank"
                                rel="noopener noreferrer"
                                className={`w-full flex items-center justify-center gap-2 py-3 rounded-lg bg-[#6C63FF] text-gray-900 dark:text-white text-sm font-medium hover:bg-[#5850E6] transition-all ${!affiliateLink ? 'pointer-events-none opacity-40' : ''}`}
                            >
                                <ExternalLink className="w-4 h-4" />
                                Visit Storefront
                            </a>
                        </div>
                    </div>
                )}

                {!loading && !product && (
                    <div className="p-6 text-center">
                        <AlertCircle className="w-12 h-12 text-gray-700 mx-auto mb-3" />
                        <p className="text-gray-600 dark:text-gray-400">Failed to load product details</p>
                    </div>
                )}
            </motion.div>
        </motion.div>
    )
}

// Drawer Skeleton
function DrawerSkeleton() {
    return (
        <div className="p-6 space-y-4">
            <div className="h-8 bg-white dark:bg-white/[0.03] rounded animate-pulse" />
            <div className="aspect-square bg-white dark:bg-white/[0.03] rounded-xl animate-pulse" />
            <div className="h-4 bg-white dark:bg-white/[0.03] rounded w-3/4 animate-pulse" />
            <div className="h-4 bg-white dark:bg-white/[0.03] rounded w-1/2 animate-pulse" />
        </div>
    )
}
