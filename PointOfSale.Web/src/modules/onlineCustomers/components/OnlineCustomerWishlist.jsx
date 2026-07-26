/**
 * Online Customer Wishlist Component
 * Phase 10.3.6 - Wishlist Admin View
 * 
 * Read-only wishlist display for POS Admin.
 * Shows wishlist items with product details, images, and dates.
 * 
 * SECURITY:
 * - Read-only - no mutation actions
 * - TenantId enforced by backend
 * - No sensitive PII displayed
 */

import { useState, useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import {
    Heart,
    RefreshCw,
    Package,
    Calendar,
    AlertCircle,
    Loader2,
    ExternalLink
} from 'lucide-react'
import { onlineCustomersApi } from '../services'
import { format, formatDistanceToNow } from 'date-fns'
import { ar, enUS } from 'date-fns/locale'

/**
 * Wishlist item with product details
 */
function WishlistItem({ item, getDateLocale, isRtl, t }) {
    const placeholderImage = 'data:image/svg+xml,' + encodeURIComponent(`
        <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
            <rect width="64" height="64" fill="#f3f4f6"/>
            <path d="M26 24h12v16H26z" fill="#d1d5db"/>
            <circle cx="29" cy="30" r="2" fill="#9ca3af"/>
            <path d="M26 38l4-5 3 4 5-7 6 8H26z" fill="#9ca3af"/>
        </svg>
    `)

    return (
        <div className="flex items-center gap-4 p-4 bg-gray-50 rounded-lg hover:bg-gray-100 transition-colors">
            {/* Product Image */}
            <div className="flex-shrink-0 w-16 h-16 rounded-lg overflow-hidden bg-white border">
                <img
                    src={item.productImage || placeholderImage}
                    alt={item.productName || 'Product'}
                    className="w-full h-full object-cover"
                    onError={(e) => {
                        e.target.src = placeholderImage
                    }}
                />
            </div>

            {/* Product Info */}
            <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2">
                    <h4 className="text-sm font-medium text-gray-900 truncate">
                        {item.productName || t('onlineCustomers.wishlist.unknownProduct')}
                    </h4>
                    {/* Status badge for deleted/hidden products */}
                    {item.productDeleted && (
                        <span className="inline-flex items-center px-2 py-0.5 text-xs font-medium bg-red-100 text-red-700 rounded">
                            {t('onlineCustomers.wishlist.productDeleted')}
                        </span>
                    )}
                </div>
                {item.productSku && (
                    <p className="text-xs text-gray-500 mt-0.5">
                        SKU: {item.productSku}
                    </p>
                )}
                <div className="flex items-center gap-1 mt-1 text-xs text-gray-400">
                    <Calendar className="w-3 h-3" />
                    <span>
                        {t('onlineCustomers.wishlist.addedOn')}: {format(new Date(item.addedAtUtc), 'MMM d, yyyy', { locale: getDateLocale() })}
                    </span>
                    <span className="text-gray-300">•</span>
                    <span>
                        {formatDistanceToNow(new Date(item.addedAtUtc), { addSuffix: true, locale: getDateLocale() })}
                    </span>
                </div>
            </div>

            {/* Price if available */}
            {item.productPrice !== undefined && item.productPrice !== null && (
                <div className="text-sm font-semibold text-gray-700">
                    ${item.productPrice.toFixed(2)}
                </div>
            )}
        </div>
    )
}

/**
 * Empty state component
 */
function EmptyState({ t }) {
    return (
        <div className="flex flex-col items-center justify-center py-12 text-center">
            <div className="w-16 h-16 bg-gray-100 rounded-full flex items-center justify-center mb-4">
                <Heart className="w-8 h-8 text-gray-300" />
            </div>
            <h4 className="text-sm font-medium text-gray-900 mb-1">
                {t('onlineCustomers.wishlist.empty')}
            </h4>
            <p className="text-xs text-gray-500">
                {t('onlineCustomers.wishlist.emptySubtitle')}
            </p>
        </div>
    )
}

/**
 * Error state component
 */
function ErrorState({ error, onRetry, t }) {
    return (
        <div className="flex flex-col items-center justify-center py-12 text-center">
            <div className="w-16 h-16 bg-red-50 rounded-full flex items-center justify-center mb-4">
                <AlertCircle className="w-8 h-8 text-red-400" />
            </div>
            <h4 className="text-sm font-medium text-gray-900 mb-1">
                {t('onlineCustomers.wishlist.loadError')}
            </h4>
            <p className="text-xs text-gray-500 mb-4">
                {error || t('common.error')}
            </p>
            <button
                onClick={() => onRetry()}
                className="inline-flex items-center gap-1.5 px-3 py-1.5 text-sm font-medium text-primary-600 bg-primary-50 rounded-lg hover:bg-primary-100"
            >
                <RefreshCw className="w-4 h-4" />
                {t('onlineCustomers.sessions.refresh')}
            </button>
        </div>
    )
}

/**
 * Loading skeleton
 */
function LoadingSkeleton() {
    return (
        <div className="space-y-3 animate-pulse">
            {[1, 2, 3].map((i) => (
                <div key={i} className="flex items-center gap-4 p-4 bg-gray-50 rounded-lg">
                    <div className="w-16 h-16 bg-gray-200 rounded-lg" />
                    <div className="flex-1 space-y-2">
                        <div className="h-4 bg-gray-200 rounded w-3/4" />
                        <div className="h-3 bg-gray-200 rounded w-1/2" />
                    </div>
                </div>
            ))}
        </div>
    )
}

/**
 * Online Customer Wishlist Tab Component
 */
export function OnlineCustomerWishlist({ customerId, onWishlistLoaded }) {
    const { t, i18n } = useTranslation()
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState(null)
    const [wishlist, setWishlist] = useState(null)
    const [wishlistOwnerId, setWishlistOwnerId] = useState(null)
    const requestIdRef = useRef(0)
    const customerIdRef = useRef(customerId)

    customerIdRef.current = customerId

    const isSameCustomerId = (left, right) => String(left ?? '').toLowerCase() === String(right ?? '').toLowerCase()

    useEffect(() => {
        return () => {
            requestIdRef.current += 1
        }
    }, [])

    const getDateLocale = () => i18n.language.startsWith('ar') ? ar : enUS
    const isRtl = i18n.dir() === 'rtl'

    const fetchWishlist = async (targetCustomerId = customerId, options = {}) => {
        const { resetCurrent = false, requestId: providedRequestId = null } = options

        if (!targetCustomerId) {
            requestIdRef.current += 1
            setWishlist(null)
            setWishlistOwnerId(null)
            setError(null)
            setLoading(false)
            return
        }

        const requestId = providedRequestId ?? ++requestIdRef.current

        if (resetCurrent) {
            setWishlist(null)
            setWishlistOwnerId(targetCustomerId)
        }

        setLoading(true)
        setError(null)

        try {
            const data = await onlineCustomersApi.getCustomerWishlist(targetCustomerId)

            if (requestId !== requestIdRef.current || !isSameCustomerId(customerIdRef.current, targetCustomerId)) {
                return
            }

            setWishlist(data)
            setWishlistOwnerId(targetCustomerId)
            onWishlistLoaded?.(data.totalCount)
        } catch (err) {
            if (requestId !== requestIdRef.current || !isSameCustomerId(customerIdRef.current, targetCustomerId)) {
                return
            }

            console.error('Failed to fetch wishlist:', err)
            setError(err.response?.data?.message || err.message)
        } finally {
            if (requestId === requestIdRef.current) {
                setLoading(false)
            }
        }
    }

    useEffect(() => {
        if (!customerId) {
            void fetchWishlist(null)
            return
        }

        void fetchWishlist(customerId, { resetCurrent: true })
    }, [customerId])

    const visibleWishlist = wishlist && isSameCustomerId(wishlistOwnerId, customerId) ? wishlist : null

    return (
        <div className="space-y-4">
            {/* Header */}
            <div className="flex items-center justify-between">
                <div className="flex items-center gap-2">
                    <Heart className="w-5 h-5 text-red-500" />
                    <h3 className="text-sm font-semibold text-gray-900">
                        {t('onlineCustomers.wishlist.title')}
                        {visibleWishlist && visibleWishlist.totalCount > 0 && (
                            <span className="ml-2 text-gray-500 font-normal">
                                ({visibleWishlist.totalCount} {visibleWishlist.totalCount === 1
                                    ? t('onlineCustomers.wishlist.item')
                                    : t('onlineCustomers.wishlist.items')})
                            </span>
                        )}
                    </h3>
                </div>
                <button
                    onClick={() => void fetchWishlist()}
                    disabled={loading}
                    className="p-1.5 text-gray-400 hover:text-gray-600 rounded hover:bg-gray-100 disabled:opacity-50"
                    title={t('onlineCustomers.sessions.refresh')}
                >
                    <RefreshCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
                </button>
            </div>

            {/* Subtitle */}
            <p className="text-xs text-gray-500">
                {t('onlineCustomers.wishlist.subtitle')}
            </p>

            {/* Content */}
            {loading && !visibleWishlist ? (
                <LoadingSkeleton />
            ) : error ? (
                <ErrorState error={error} onRetry={() => void fetchWishlist(customerId)} t={t} />
            ) : !visibleWishlist || visibleWishlist.totalCount === 0 ? (
                <EmptyState t={t} />
            ) : (
                <div className="space-y-3">
                    {visibleWishlist.items.map((item, index) => (
                        <WishlistItem
                            key={item.productId || index}
                            item={item}
                            getDateLocale={getDateLocale}
                            isRtl={isRtl}
                            t={t}
                        />
                    ))}
                </div>
            )}

            {/* Last updated */}
            {visibleWishlist?.lastUpdatedAtUtc && (
                <p className="text-xs text-gray-400 text-center pt-2 border-t">
                    {t('onlineCustomers.wishlist.lastUpdated')}: {' '}
                    {formatDistanceToNow(new Date(visibleWishlist.lastUpdatedAtUtc), { addSuffix: true, locale: getDateLocale() })}
                </p>
            )}
        </div>
    )
}

export default OnlineCustomerWishlist
