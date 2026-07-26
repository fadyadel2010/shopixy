/**
 * AF-27B §3 / AF-28: My Landing Pages Tab
 * 
 * Displays all affiliate-eligible products with pre-built landing page URLs.
 * Allows affiliates to:
 * - See all available landing pages
 * - Choose landing page template (Classic, Storytelling, Minimal Fast Checkout)
 * - Copy landing page URL with one click (includes template param)
 * - Preview landing page in new tab
 * - See product info (price, commission, stock status)
 * 
 * DATA SOURCE: GET /api/affiliate/landing-pages (MongoDB-only)
 * 
 * ARCHITECTURAL BOUNDARIES:
 * - Read-only UI — no writes
 * - No cart/checkout logic
 * - No commission calculations (displayed from projection)
 */
import { useState, useEffect } from 'react'
import { motion } from 'framer-motion'
import { affiliateAxios } from '../../../../services/affiliateAuthApi'
import { Copy, Check, ExternalLink, Image, AlertCircle, Loader2, Globe, Palette } from 'lucide-react'
import { useLanguage } from '../../i18n/LanguageContext'

const TEMPLATE_OPTIONS = [
    { value: 'Classic:v1', label: 'Classic', description: 'Standard product layout' },
    { value: 'Storytelling:v1', label: 'Storytelling', description: 'Narrative high-conversion' },
    { value: 'MinimalFastCheckout:v1', label: 'Minimal', description: 'Quick purchase focus' }
]

export default function LandingPagesTab({ formatCurrency }) {
    const { t } = useLanguage()
    const [pages, setPages] = useState([])
    const [affiliateCode, setAffiliateCode] = useState('')
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState(null)
    const [copiedId, setCopiedId] = useState(null)
    const [page, setPage] = useState(1)
    const [totalPages, setTotalPages] = useState(1)
    // AF-28: Track selected template per product (key: productId, value: templateType)
    const [selectedTemplates, setSelectedTemplates] = useState({})

    useEffect(() => {
        fetchLandingPages()
    }, [page])

    const fetchLandingPages = async () => {
        setLoading(true)
        setError(null)
        try {
            const res = await affiliateAxios.get('/affiliate/landing-pages', {
                params: { page, pageSize: 20 }
            })
            setPages(res.data.landingPages || [])
            setAffiliateCode(res.data.affiliateCode || '')
            setTotalPages(res.data.totalPages || 1)

            // Initialize selected templates from backend defaults
            const defaults = {}
            for (const lp of (res.data.landingPages || [])) {
                defaults[lp.productId] = lp.templateType || 'Classic:v1'
            }
            setSelectedTemplates(prev => ({ ...prev, ...defaults }))
        } catch (err) {
            console.warn('[LandingPagesTab] Failed to fetch landing pages:', err?.response?.status || err.message)
            setPages([])
            if (err?.response?.status === 401) {
                setError('session')
            } else {
                setError('unavailable')
            }
        } finally {
            setLoading(false)
        }
    }

    // Build URL with template param when non-default
    const buildUrl = (lp, templateType) => {
        const base = lp.absoluteLandingUrl
        if (!templateType || templateType === 'Classic:v1') {
            return base
        }
        const separator = base.includes('?') ? '&' : '?'
        return `${base}${separator}template=${encodeURIComponent(templateType)}`
    }

    const getEffectiveUrl = (lp) => {
        const template = selectedTemplates[lp.productId] || 'Classic:v1'
        return buildUrl(lp, template)
    }

    const copyUrl = (lp) => {
        const url = getEffectiveUrl(lp)
        navigator.clipboard.writeText(url).then(() => {
            setCopiedId(lp.productId)
            setTimeout(() => setCopiedId(null), 2000)
        })
    }

    const previewPage = (lp) => {
        const url = getEffectiveUrl(lp)
        window.open(url, '_blank')
    }

    const handleTemplateChange = (productId, templateType) => {
        setSelectedTemplates(prev => ({ ...prev, [productId]: templateType }))
    }

    if (loading) {
        return (
            <div className="space-y-3">
                {[...Array(4)].map((_, i) => (
                    <div key={i} className="h-24 rounded-xl border border-gray-200 dark:border-white/[0.06] bg-white dark:bg-white/[0.02] animate-pulse" />
                ))}
            </div>
        )
    }

    if (error === 'unavailable') {
        return (
            <div className="text-center py-20">
                <AlertCircle className="w-14 h-14 text-gray-700 mx-auto mb-4" />
                <h3 className="text-lg font-semibold text-gray-900 dark:text-white mb-2">Landing Pages Unavailable</h3>
                <p className="text-gray-500 text-sm max-w-sm mx-auto">
                    Landing pages feature is being set up. Please check back later.
                </p>
            </div>
        )
    }

    if (error === 'session') {
        return (
            <div className="text-center py-20">
                <Globe className="w-14 h-14 text-gray-700 mx-auto mb-4" />
                <h3 className="text-lg font-semibold text-gray-900 dark:text-white mb-2">Session Expired</h3>
                <p className="text-gray-500 text-sm">Please log in again to view your landing pages.</p>
            </div>
        )
    }

    if (!pages.length) {
        return (
            <div className="text-center py-20">
                <Globe className="w-14 h-14 text-gray-400 mx-auto mb-4" />
                <h3 className="text-lg font-semibold text-gray-900 dark:text-white mb-2">No Landing Pages Yet</h3>
                <p className="text-gray-500 text-sm max-w-sm mx-auto">
                    Landing pages will appear here once products are available for affiliate promotion.
                    Visit the Discover tab to explore products.
                </p>
            </div>
        )
    }

    return (
        <div className="space-y-4">
            {/* Header */}
            <div className="flex items-center justify-between">
                <div>
                    <p className="text-sm text-gray-500">
                        {pages.length} landing page{pages.length !== 1 ? 's' : ''} available
                    </p>
                    {affiliateCode && (
                        <p className="text-xs text-gray-400 mt-0.5">
                            Your code: <code className="text-[#6C63FF] bg-[#6C63FF]/5 px-1.5 py-0.5 rounded font-mono">{affiliateCode}</code>
                        </p>
                    )}
                </div>
            </div>

            {/* Landing Pages Grid */}
            <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
                {pages.map((lp, i) => {
                    const currentTemplate = selectedTemplates[lp.productId] || 'Classic:v1'
                    const effectiveUrl = getEffectiveUrl(lp)

                    return (
                        <motion.div
                            key={lp.productId}
                            initial={{ opacity: 0, y: 10 }}
                            animate={{ opacity: 1, y: 0 }}
                            transition={{ delay: i * 0.05 }}
                            className="rounded-xl border border-gray-200 dark:border-white/[0.06] bg-white dark:bg-white/[0.02] overflow-hidden hover:border-[#6C63FF]/30 transition-all duration-200"
                        >
                            {/* Product Image */}
                            <div className="aspect-[16/9] bg-gray-100 dark:bg-white/[0.03] relative overflow-hidden">
                                {lp.mainImage ? (
                                    <img
                                        src={lp.mainImage}
                                        alt={lp.productName}
                                        className="w-full h-full object-cover"
                                    />
                                ) : (
                                    <div className="w-full h-full flex items-center justify-center">
                                        <Image className="w-10 h-10 text-gray-300 dark:text-gray-600" />
                                    </div>
                                )}

                                {/* Stock Badge */}
                                {lp.isOutOfStock && (
                                    <span className="absolute top-2 right-2 px-2 py-0.5 rounded-full text-[10px] font-medium bg-red-500/90 text-white">
                                        Out of Stock
                                    </span>
                                )}

                                {/* Commission Badge */}
                                <span className="absolute top-2 left-2 px-2 py-0.5 rounded-full text-[10px] font-medium bg-emerald-500/90 text-white">
                                    {lp.commissionRatePercentage}% commission
                                </span>
                            </div>

                            {/* Product Details */}
                            <div className="p-4 space-y-3">
                                <div>
                                    <h3 className="font-semibold text-sm text-gray-900 dark:text-white line-clamp-1">
                                        {lp.productName}
                                    </h3>
                                    <p className="text-xs text-gray-500 mt-0.5">
                                        {lp.storeName} · {formatCurrency(lp.price, lp.currency)}
                                    </p>
                                </div>

                                {/* Est. Commission */}
                                <div className="flex items-center justify-between text-xs">
                                    <span className="text-gray-500">Est. Commission</span>
                                    <span className="text-emerald-400 font-medium">
                                        {formatCurrency(lp.estimatedCommission, lp.currency)}
                                    </span>
                                </div>

                                {/* AF-28: Template Selector */}
                                <div className="bg-gray-50 dark:bg-white/[0.03] rounded-lg p-2.5">
                                    <div className="flex items-center gap-1.5 mb-1.5">
                                        <Palette className="w-3 h-3 text-[#6C63FF]" />
                                        <span className="text-[10px] font-semibold text-gray-600 dark:text-gray-400 uppercase tracking-wider">
                                            Landing Page Design
                                        </span>
                                    </div>
                                    <div className="flex gap-1">
                                        {TEMPLATE_OPTIONS.map(opt => (
                                            <button
                                                key={opt.value}
                                                onClick={() => handleTemplateChange(lp.productId, opt.value)}
                                                className={`flex-1 py-1.5 px-1 rounded-md text-[10px] font-medium transition-all duration-150 ${currentTemplate === opt.value
                                                    ? 'bg-[#6C63FF] text-white shadow-sm shadow-[#6C63FF]/25'
                                                    : 'bg-white dark:bg-white/[0.05] text-gray-500 dark:text-gray-400 border border-gray-200 dark:border-white/[0.08] hover:border-[#6C63FF]/40 hover:text-[#6C63FF]'
                                                    }`}
                                                title={opt.description}
                                            >
                                                {opt.label}
                                            </button>
                                        ))}
                                    </div>
                                </div>

                                {/* URL Display */}
                                <div className="bg-gray-50 dark:bg-white/[0.03] rounded-lg px-3 py-2">
                                    <code className="text-[11px] text-gray-600 dark:text-gray-400 font-mono break-all">
                                        {effectiveUrl}
                                    </code>
                                </div>

                                {/* Actions */}
                                <div className="flex gap-2">
                                    <button
                                        onClick={() => copyUrl(lp)}
                                        className={`flex-1 flex items-center justify-center gap-1.5 py-2 rounded-lg text-xs font-medium transition-all ${copiedId === lp.productId
                                            ? 'bg-emerald-500/10 text-emerald-400 border border-emerald-500/20'
                                            : 'bg-[#6C63FF]/10 text-[#6C63FF] border border-[#6C63FF]/20 hover:bg-[#6C63FF]/20'
                                            }`}
                                    >
                                        {copiedId === lp.productId ? (
                                            <><Check className="w-3.5 h-3.5" /> Copied!</>
                                        ) : (
                                            <><Copy className="w-3.5 h-3.5" /> Copy URL</>
                                        )}
                                    </button>
                                    <button
                                        onClick={() => previewPage(lp)}
                                        className="flex items-center justify-center gap-1.5 px-3 py-2 rounded-lg text-xs font-medium bg-gray-100 dark:bg-white/[0.03] text-gray-600 dark:text-gray-400 border border-gray-200 dark:border-white/[0.06] hover:border-[#6C63FF]/30 transition-all"
                                        title="Preview Landing Page"
                                    >
                                        <ExternalLink className="w-3.5 h-3.5" />
                                    </button>
                                </div>
                            </div>
                        </motion.div>
                    )
                })}
            </div>

            {/* Pagination */}
            {totalPages > 1 && (
                <div className="flex items-center justify-center gap-2 pt-4">
                    <button
                        onClick={() => setPage(p => Math.max(1, p - 1))}
                        disabled={page <= 1}
                        className="px-3 py-1.5 rounded-lg text-xs font-medium bg-gray-100 dark:bg-white/[0.03] text-gray-600 dark:text-gray-400 disabled:opacity-40 hover:bg-gray-200 dark:hover:bg-white/[0.06] transition-all"
                    >
                        Previous
                    </button>
                    <span className="text-xs text-gray-500">
                        Page {page} of {totalPages}
                    </span>
                    <button
                        onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                        disabled={page >= totalPages}
                        className="px-3 py-1.5 rounded-lg text-xs font-medium bg-gray-100 dark:bg-white/[0.03] text-gray-600 dark:text-gray-400 disabled:opacity-40 hover:bg-gray-200 dark:hover:bg-white/[0.06] transition-all"
                    >
                        Next
                    </button>
                </div>
            )}
        </div>
    )
}
