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
import { useState, useEffect, useMemo, useRef } from 'react'
import { motion } from 'framer-motion'
import affiliateApi from '../../../../services/affiliateApi'
import {
    X, ExternalLink, Copy, Check, MessageCircle, Share2,
    TrendingUp, AlertCircle, ChevronLeft, ChevronRight, Package
} from 'lucide-react'
import StockBadge from './badges/StockBadge'
import { useLanguage } from '../../i18n/LanguageContext'
import { affiliateProductMediaUrl } from '../../lib/affiliateProductMedia'

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
    const [linkError, setLinkError] = useState(false)
    const [reload, setReload] = useState(0)
    const generationRef = useRef(0)
    const submittingRef = useRef(false)
    const linkReadRef = useRef(0)

    // AF-22: Format currency properly (25 EGP instead of EGP 25.00)
    const formatPrice = (amount) => {
        const currency = resolveProductCurrency(product)
        return currency ? `${Number(amount).toLocaleString(undefined, { maximumFractionDigits: 4 })} ${currency}` : 'Currency unavailable'
    }

    // FE-1A: Prevent double fetch - guard against empty productId
    useEffect(() => {
        if (!productId) return
        const generation = ++generationRef.current
        const linkRead = ++linkReadRef.current
        let active = true
        setLoading(true)
        setProduct(null)
        setPromoLink(null)
        setLinkError(false)
        setCopied(false)
        setCurrentImageIndex(0)
        setGeneratingLink(false)
        submittingRef.current = false
        affiliateApi.getProductIntelligence(productId)
            .then(p => {
                if (!active) return
                setProduct(p)
            })
            .catch(() => { if (active) setProduct(null) })
            .finally(() => { if (active) setLoading(false) })
        // Browsing is read-only. Locate an owned link without silently creating one.
        affiliateApi.listPromotionLinks().then(links => {
            if (active && linkReadRef.current === linkRead) setPromoLink(links.find(link => link.productId === productId && link.isActive) || null)
        }).catch(() => { /* Explicit Promote is an idempotent retry/readback path. */ })
        return () => {
            active = false
            generationRef.current = generation + 1
        }
    }, [productId, reload])

    const promote = async () => {
        if (!product || submittingRef.current) return
        const generation = generationRef.current
        linkReadRef.current++
        submittingRef.current = true
        setGeneratingLink(true)
        setLinkError(false)
        try {
            const link = await affiliateApi.createPromotionLink(product.tenantId, product.productId)
            if (generationRef.current !== generation) return
            if (!link?.promotionUrl) throw new Error('Promotion destination unavailable')
            setPromoLink(link)
        } catch {
            if (generationRef.current === generation) setLinkError(true)
        } finally {
            if (generationRef.current === generation) {
                submittingRef.current = false
                setGeneratingLink(false)
            }
        }
    }

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
        const generation = generationRef.current
        navigator.clipboard.writeText(affiliateLink).then(() => {
            if (generationRef.current !== generation) return
            setCopied(true)
            setTimeout(() => { if (generationRef.current === generation) setCopied(false) }, 2000)
            // FE-1A: Copy feedback (optional toast - implement if toast library available)
            // toast?.success?.('Affiliate link copied')
        }).catch(() => { if (generationRef.current === generation) setLinkError(true) })
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
                role="dialog"
                aria-modal="true"
                aria-label={t('portal.drawer.promoteProduct')}
                initial={{ x: '100%' }}
                animate={{ x: 0 }}
                exit={{ x: '100%' }}
                transition={{ type: 'spring', damping: 30, stiffness: 300 }}
                onClick={e => e.stopPropagation()}
                className="w-full sm:w-[480px] h-full bg-white dark:bg-[#0D1117] border-l border-gray-200 dark:border-white/[0.08] overflow-y-auto"
            >
                <button onClick={onClose} aria-label={t('portal.discoverTab.close')} className="absolute top-2 end-2 z-10 p-2 rounded-lg bg-white dark:bg-[#0D1117] text-gray-600 dark:text-gray-400">
                    <X className="w-5 h-5" />
                </button>
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
                        </div>

                        {/* Gallery Carousel */}
                        {product?.images && product.images.length > 0 && (
                            <div className="relative aspect-square rounded-xl overflow-hidden bg-white dark:bg-white/[0.02] border border-gray-200 dark:border-white/[0.06] flex items-center justify-center">
                                <Package className="absolute w-10 h-10 text-gray-700" aria-hidden="true" />
                                {/* FE-1A: Image fallback for broken thumbnails */}
                                <img
                                    src={affiliateProductMediaUrl(product.images[currentImageIndex], product.storefrontUrl)}
                                    alt={product?.name || 'Product'}
                                    className="relative w-full h-full object-cover"
                                    onError={(e) => {
                                        e.currentTarget.onerror = null
                                        e.currentTarget.style.display = 'none'
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
                                        {t('portal.badges.from')} {formatPrice(product.price)}
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
                                {t('portal.drawer.totalAvailable')}: <span className="text-gray-900 dark:text-white font-medium">{product?.totalAvailableStock ?? 0} {t('portal.drawer.units')}</span>
                            </p>
                        </div>

                        {/* Variants | FE-1A: Defensive optional chaining */}
                        {product?.variants && product.variants.length > 0 && (
                            <div className="space-y-3">
                                <p className="text-sm font-medium text-gray-900 dark:text-white">{t('portal.drawer.variants')}</p>
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
                            <p className="text-sm font-medium text-gray-900 dark:text-white">{t('portal.drawer.promoteProduct')}</p>
                            <button type="button" onClick={promote} disabled={generatingLink} className="w-full py-3 rounded-lg bg-[#6C63FF] text-white disabled:opacity-50">
                                {generatingLink ? t('portal.drawer.generating') : t('portal.discoverTab.promoteAction')}
                            </button>
                            {linkError && <p role="alert" className="text-sm text-red-500">{t('portal.drawer.linkError')}</p>}

                            {/* Copy Link */}
                            <div className="flex items-center gap-2">
                                <div dir="ltr" data-testid="promotion-url" className="min-w-0 flex-1 px-3 py-2.5 rounded-lg bg-gray-50 dark:bg-[#0B0F19] border border-gray-200 dark:border-white/[0.08] text-xs text-gray-600 dark:text-gray-400 truncate font-mono">
                                    {generatingLink ? t('portal.drawer.generating') : (affiliateLink || t('portal.drawer.noLink'))}
                                </div>
                                <button
                                    onClick={copyLink}
                                    aria-label={t('portal.drawer.copyLink')}
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
                                {t('portal.drawer.visitStorefront')}
                            </a>
                        </div>
                    </div>
                )}

                {!loading && !product && (
                    <div className="p-6 text-center">
                        <AlertCircle className="w-12 h-12 text-gray-700 mx-auto mb-3" />
                        <p className="text-gray-600 dark:text-gray-400">{t('portal.drawer.loadError')}</p>
                        <button onClick={() => setReload(v => v + 1)} className="mt-4 px-4 py-2 rounded-lg border">{t('portal.discoverTab.retry')}</button>
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
