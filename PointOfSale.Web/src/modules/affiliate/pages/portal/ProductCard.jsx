/**
 * FE-1: Product Card Component
 * FE-1A: Micro-hardening & UX polish
 * AF-22: i18n support & currency formatting
 * 
 * Displays:
 * - Image
 * - Product name
 * - Store name
 * - Price (or "From X" if variants)
 * - Commission %
 * - Estimated commission
 * - Stock badge
 * 
 * Disabled when out of stock
 */
import { Package, DollarSign, Sparkles } from 'lucide-react'
import CommissionBadge from './badges/CommissionBadge'
import StockBadge from './badges/StockBadge'
import { useLanguage } from '../../i18n/LanguageContext'
import { affiliateProductMediaUrl } from '../../lib/affiliateProductMedia'

const resolveProductCurrency = (product) => {
    const currency = typeof product?.currency === 'string' ? product.currency.trim().toUpperCase() : ''
    return currency || null
}

export default function ProductCard({ product, onClick, disabled = false }) {
    const { t } = useLanguage()
    
    // FE-1A: Defensive optional chaining
    if (!product) return null

    // AF-22: Format currency properly (25 EGP instead of EGP 25.00)
    const formatPrice = (amount) => {
        const currency = resolveProductCurrency(product)
        return currency ? `${Number(amount).toLocaleString(undefined, { maximumFractionDigits: 4 })} ${currency}` : 'Currency unavailable'
    }

    // FE-1: Variant price rule | FE-1A: Defensive optional chaining
    const displayPrice = product?.variants && product.variants.length > 0
        ? `${t('portal.badges.from')} ${formatPrice(Math.min(...product.variants.map(v => v?.price || 0)))}`
        : formatPrice(product?.price || 0)

    const isDisabled = disabled || product?.isOutOfStock

    return (
        <div
            onClick={isDisabled ? undefined : onClick}
            className={`group rounded-xl border border-gray-200 dark:border-white/[0.06] bg-white dark:bg-white/[0.02] overflow-hidden transition-all ${
                isDisabled
                    ? 'opacity-50 cursor-not-allowed'
                    : 'hover:bg-gray-50 dark:hover:bg-white/[0.04] hover:border-[#6C63FF]/20 cursor-pointer'
            }`}
        >
            {/* Image */}
            <div className="aspect-square bg-gradient-to-br from-white/[0.03] to-transparent flex items-center justify-center overflow-hidden relative">
                <Package className="absolute w-10 h-10 text-gray-700" aria-hidden="true" />
                {/* FE-1A: Defensive optional chaining & image fallback */}
                {product?.images && product.images.length > 0 ? (
                    <img
                        src={affiliateProductMediaUrl(product.images[0], product.storefrontUrl)}
                        alt={product?.name || 'Product'}
                        className={`relative w-full h-full object-cover transition-transform duration-500 ${
                            isDisabled ? '' : 'group-hover:scale-105'
                        }`}
                        loading="lazy"
                        onError={(e) => {
                            e.currentTarget.onerror = null
                            e.currentTarget.style.display = 'none'
                        }}
                    />
                ) : null}

                {/* Commission Badge (Top Right) | FE-1A: Defensive optional chaining */}
                <div className="absolute top-2 right-2">
                    <CommissionBadge commissionRate={product?.commissionRate} />
                </div>

                {/* Stock Badge (Bottom Left) | FE-1A: Defensive optional chaining */}
                <div className="absolute bottom-2 left-2">
                    <StockBadge isOutOfStock={product?.isOutOfStock} lowStock={product?.lowStock} />
                </div>
            </div>

            {/* Content | FE-1A: Defensive optional chaining throughout */}
            <div className="p-4 space-y-2">
                {/* Product Name */}
                <h4 className="text-sm font-medium text-gray-900 dark:text-white line-clamp-2 leading-snug min-h-[2.5rem]">
                    {product?.name || 'Product'}
                </h4>

                {/* Price & Store */}
                <div className="flex items-center justify-between">
                    <span className="text-base font-bold text-gray-900 dark:text-white">
                        {displayPrice}
                    </span>
                    <span className="text-xs text-gray-500 truncate max-w-[100px]">
                        {product?.storeName || 'Store'}
                    </span>
                </div>

                {/* Estimated Commission */}
                <div className="flex items-center gap-1.5 py-1.5 px-2.5 rounded-lg bg-emerald-500/5 border border-emerald-500/10">
                    <DollarSign className="w-3.5 h-3.5 text-emerald-400 flex-shrink-0" />
                    <span className="text-xs text-emerald-400 font-medium">
                        {t('portal.badges.youEarn')} <span className="font-bold">{formatPrice(product?.estimatedCommission || 0)}</span>
                    </span>
                </div>

                {/* View Details Button */}
                <button
                    onClick={(e) => {
                        e.stopPropagation()
                        if (!isDisabled) onClick()
                    }}
                    disabled={isDisabled}
                    className={`w-full mt-1 py-2 rounded-lg text-sm font-medium border transition-all flex items-center justify-center gap-1.5 ${
                        isDisabled
                            ? 'bg-gray-500/10 text-gray-500 border-gray-500/20 cursor-not-allowed'
                            : 'bg-[#6C63FF]/10 text-[#6C63FF] border-[#6C63FF]/20 hover:bg-[#6C63FF]/20'
                    }`}
                >
                    <Sparkles className="w-3.5 h-3.5" />
                    {isDisabled ? t('portal.badges.outOfStock') : t('portal.badges.viewDetails')}
                </button>
            </div>
        </div>
    )
}

// Skeleton Loader
export function ProductCardSkeleton() {
    return (
        <div className="rounded-xl border border-gray-200 dark:border-white/[0.06] bg-white dark:bg-white/[0.02] p-4 animate-pulse">
            <div className="aspect-square bg-white dark:bg-white/[0.03] rounded-lg mb-3" />
            <div className="h-4 bg-white dark:bg-white/[0.03] rounded w-3/4 mb-2" />
            <div className="h-3 bg-white dark:bg-white/[0.03] rounded w-1/2 mb-3" />
            <div className="h-9 bg-white dark:bg-white/[0.03] rounded-lg" />
        </div>
    )
}
