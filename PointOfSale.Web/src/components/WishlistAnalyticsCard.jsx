/**
 * Wishlist Analytics Card
 * Phase 10.3.6.2 - Wishlist Analytics
 * 
 * Displays wishlist statistics and top products on the dashboard.
 */
import { useState, useEffect, useCallback } from 'react'
import { useTranslation } from 'react-i18next'
import { Heart, TrendingUp, Users, Package } from 'lucide-react'
import api from '../services/api'
import { useSettings } from '../contexts/SettingsContext'
import { useAuthStore } from '../store/authStore'
import { useLatestRequestGuard } from '../hooks/useLatestRequestGuard'

export default function WishlistAnalyticsCard() {
    const { i18n } = useTranslation()
    const { formatCurrency } = useSettings()
    const isRTL = i18n.language === 'ar'
    const tenantId = useAuthStore(state => state.user?.tenantId)
    const { beginRequest, isLatestRequest } = useLatestRequestGuard()

    const [data, setData] = useState(null)
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState(null)

    const fetchAnalytics = useCallback(async () => {
        const requestId = beginRequest()
        if (!tenantId) {
            setData(null)
            setLoading(false)
            return
        }
        try {
            setLoading(true)
            setError(null)
            const response = await api.get('/admin/wishlist-analytics')
            if (!isLatestRequest(requestId)) return
            const value = response.data
            if (!value || !Array.isArray(value.topProducts) || !value.recentActivity ||
                ![value.totalWishlistItems, value.totalCustomersWithWishlists, value.averageItemsPerWishlist, value.recentActivity.itemsAddedLast7Days].every(number => Number.isFinite(number) && number >= 0)) {
                throw new Error('Invalid wishlist analytics response.')
            }
            setData(response.data)
            setError(null)
        } catch (err) {
            if (!isLatestRequest(requestId)) return
            console.error('Failed to load wishlist analytics:', { status: err?.response?.status })
            setData(null)
            setError('Failed to load data')
        } finally {
            if (isLatestRequest(requestId)) setLoading(false)
        }
    }, [tenantId, beginRequest, isLatestRequest])

    useEffect(() => {
        void fetchAnalytics()
    }, [fetchAnalytics])

    if (loading) {
        return (
            <div className="bg-white rounded-xl border border-gray-200 p-6 shadow-sm h-full animate-pulse">
                <div className="flex items-center gap-3 mb-6">
                    <div className="w-10 h-10 bg-gray-200 rounded-lg"></div>
                    <div className="h-6 bg-gray-200 rounded w-1/3"></div>
                </div>
                <div className="grid grid-cols-3 gap-4 mb-6">
                    <div className="h-20 bg-gray-100 rounded-lg"></div>
                    <div className="h-20 bg-gray-100 rounded-lg"></div>
                    <div className="h-20 bg-gray-100 rounded-lg"></div>
                </div>
                <div className="space-y-3">
                    <div className="h-12 bg-gray-100 rounded-lg"></div>
                    <div className="h-12 bg-gray-100 rounded-lg"></div>
                    <div className="h-12 bg-gray-100 rounded-lg"></div>
                </div>
            </div>
        )
    }

    if (error || !data) {
        return <div className="rounded-xl border border-gray-200 bg-white p-6" role="alert">
            <p>{isRTL ? 'تعذر تحميل إحصائيات الأمنيات' : 'Unable to load wishlist analytics'}</p>
            <button type="button" onClick={fetchAnalytics} className="mt-2 text-indigo-700 underline">{isRTL ? 'إعادة المحاولة' : 'Retry'}</button>
        </div>
    }

    // Don't show if no wishlist activity at all
    if (data.totalWishlistItems === 0 && data.totalCustomersWithWishlists === 0) {
        return (
            <div className="bg-white rounded-xl border border-gray-200 p-6 shadow-sm h-full">
                <div className="flex items-center gap-3 mb-4">
                    <div className="p-2 bg-rose-100 rounded-lg">
                        <Heart size={20} className="text-rose-600" />
                    </div>
                    <h3 className="text-lg font-semibold text-gray-900">
                        {isRTL ? 'إحصائيات الأمنيات' : 'Wishlist Analytics'}
                    </h3>
                </div>
                <div className="text-center py-8 text-gray-500">
                    <p>{isRTL ? 'لا توجد نشاطات أمنيات حتى الآن' : 'No wishlist activity yet'}</p>
                    <button type="button" onClick={fetchAnalytics} className="mt-2 text-indigo-700 underline">{isRTL ? 'تحديث' : 'Refresh'}</button>
                </div>
            </div>
        )
    }

    const formatProductPrice = (product) => {
        const currency = typeof product.currency === 'string' && product.currency.trim()
            ? product.currency.trim()
            : ''

        if (!currency) {
            return 'Currency unavailable'
        }

        try {
            return formatCurrency(product.price, currency)
        } catch {
            return 'Currency unavailable'
        }
    }

    return (
        <div className="bg-white rounded-xl border border-gray-200 p-6 shadow-sm h-full">
            {/* Header */}
            <div className="flex items-center justify-between mb-6">
                <div className="flex items-center gap-3">
                    <div className="p-2 bg-rose-100 rounded-lg">
                        <Heart size={20} className="text-rose-600" />
                    </div>
                    <div>
                        <h3 className="text-lg font-semibold text-gray-900">
                            {isRTL ? 'إحصائيات الأمنيات' : 'Wishlist Analytics'}
                        </h3>
                        <p className="text-xs text-gray-500">
                            {isRTL ? 'آخر 7 أيام' : 'Last 7 days'}:{' '}
                            <span className="text-green-600 font-medium">
                                +{data.recentActivity.itemsAddedLast7Days} {isRTL ? 'إضافة' : 'added'}
                            </span>
                        </p>
                    </div>
                </div>
                <button
                    onClick={fetchAnalytics}
                    className="p-2 hover:bg-gray-50 rounded-lg transition-colors text-gray-400 hover:text-gray-600"
                    title={isRTL ? 'تحديث' : 'Refresh'}
                    aria-label={isRTL ? 'تحديث' : 'Refresh'}
                >
                    <TrendingUp size={18} />
                </button>
            </div>

            {/* Stats Grid */}
            <div className="grid grid-cols-3 gap-4 mb-6">
                <div className="p-3 bg-gray-50 rounded-xl border border-gray-100">
                    <div className="flex items-center gap-2 mb-1 text-gray-500 text-xs font-medium">
                        <Users size={14} />
                        {isRTL ? 'العملاء' : 'Customers'}
                    </div>
                    <div className="text-2xl font-bold text-gray-900">
                        {data.totalCustomersWithWishlists}
                    </div>
                </div>
                <div className="p-3 bg-gray-50 rounded-xl border border-gray-100">
                    <div className="flex items-center gap-2 mb-1 text-gray-500 text-xs font-medium">
                        <Package size={14} />
                        {isRTL ? 'المنتجات' : 'Items'}
                    </div>
                    <div className="text-2xl font-bold text-gray-900">
                        {data.totalWishlistItems}
                    </div>
                </div>
                <div className="p-3 bg-gray-50 rounded-xl border border-gray-100">
                    <div className="flex items-center gap-2 mb-1 text-gray-500 text-xs font-medium">
                        <TrendingUp size={14} />
                        {isRTL ? 'المتوسط' : 'Avg/Cust'}
                    </div>
                    <div className="text-2xl font-bold text-gray-900">
                        {data.averageItemsPerWishlist}
                    </div>
                </div>
            </div>

            {/* Top Products List */}
            <div>
                <h4 className="text-sm font-semibold text-gray-900 mb-3 flex items-center justify-between">
                    {isRTL ? 'المنتجات الأكثر طلباً' : 'Top Wishlisted Products'}
                    <span className="text-xs font-normal text-gray-500">
                        {isRTL ? 'عدد مرات الإضافة' : 'Times Added'}
                    </span>
                </h4>

                <div className="space-y-3">
                    {data.topProducts.slice(0, 5).map((product, index) => (
                        <div
                            key={product.productId}
                            className="flex items-center gap-3 p-2 hover:bg-gray-50 rounded-lg transition-colors group"
                        >
                            <span className={`
                                w-6 h-6 flex items-center justify-center rounded-full text-xs font-bold
                                ${index === 0 ? 'bg-yellow-100 text-yellow-700' :
                                    index === 1 ? 'bg-gray-100 text-gray-700' :
                                        index === 2 ? 'bg-orange-100 text-orange-700' : 'bg-gray-50 text-gray-500'}
                            `}>
                                {index + 1}
                            </span>

                            <div className="w-10 h-10 bg-gray-100 rounded-lg overflow-hidden flex-shrink-0 border border-gray-200">
                                {product.thumbnailImage ? (
                                    <img
                                        src={product.thumbnailImage}
                                        alt={product.productName}
                                        className="w-full h-full object-cover"
                                    />
                                ) : (
                                    <div className="w-full h-full flex items-center justify-center text-gray-400">
                                        <Package size={16} />
                                    </div>
                                )}
                            </div>

                            <div className="flex-1 min-w-0">
                                <p className="text-sm font-medium text-gray-900 truncate group-hover:text-indigo-600 transition-colors">
                                    {isRTL && product.productNameAr ? product.productNameAr : product.productName}
                                </p>
                                <p className="text-xs text-gray-500">
                                    {formatProductPrice(product)}
                                </p>
                            </div>

                            <div className="px-2 py-1 bg-rose-50 text-rose-700 rounded text-xs font-bold">
                                {product.wishlistCount}
                            </div>
                        </div>
                    ))}

                    {data.topProducts.length === 0 && (
                        <div className="text-center py-4 text-xs text-gray-400">
                            {isRTL ? 'لا توجد بيانات' : 'No data available'}
                        </div>
                    )}
                </div>
            </div>
        </div>
    )
}
