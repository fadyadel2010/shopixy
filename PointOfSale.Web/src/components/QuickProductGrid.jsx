import { useState, useEffect, useRef, useCallback } from 'react'
import { useTranslation } from 'react-i18next'
import { isRequestCanceled } from '../services/api'
import { getQuickAccessProducts } from '../services/posDataService'
import { subscribeToPosRuntimeInvalidation } from '../services/posRuntimeCache'
import { useSettings } from '../contexts/SettingsContext'

export default function QuickProductGrid({ onProductSelect, warehouseId, refreshKey }) {
    const { t, i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const [items, setItems] = useState([])
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState('')
    const requestIdRef = useRef(0)
    const requestControllerRef = useRef(null)
    const { formatCurrency } = useSettings()

    const fetchSellableItems = useCallback(async ({ force = false } = {}) => {
        if (!warehouseId) {
            requestControllerRef.current?.abort()
            requestIdRef.current += 1
            setItems([])
            setError('')
            setLoading(false)
            return
        }

        requestControllerRef.current?.abort()
        const controller = new AbortController()
        requestControllerRef.current = controller

        const requestId = ++requestIdRef.current

        try {
            setLoading(true)
            setError('')

            const nextItems = await getQuickAccessProducts({
                warehouseId,
                limit: 12,
                signal: controller.signal,
                force,
            })

            if (requestId !== requestIdRef.current || controller.signal.aborted) {
                return
            }

            setItems(nextItems)
        } catch (nextError) {
            if (requestId !== requestIdRef.current || controller.signal.aborted || isRequestCanceled(nextError)) {
                return
            }

            console.error(nextError)
            setItems([])
            setError(nextError.response?.data?.message || t('quickProductGrid.loadFailed'))
        } finally {
            if (requestId === requestIdRef.current && requestControllerRef.current === controller) {
                requestControllerRef.current = null
                setLoading(false)
            }
        }
    }, [t, warehouseId])

    useEffect(() => {
        void fetchSellableItems()
        return () => {
            requestIdRef.current += 1
            requestControllerRef.current?.abort()
        }
    }, [fetchSellableItems, refreshKey])

    useEffect(() => subscribeToPosRuntimeInvalidation(() => {
        if (!warehouseId) {
            return
        }

        void fetchSellableItems({ force: true })
    }), [fetchSellableItems, warehouseId])

    const handleItemClick = (item) => {
        const productToAdd = {
            id: item.productId,
            variantId: item.variantId,
            name: item.name,
            sku: item.sku,
            barcode: item.barcode,
            salePrice: item.price,
            stockQuantity: Math.max(0, Number(item.stockQuantity || 0)),
            images: item.images,
            variantAttributes: item.attributes || [],
            variantDisplayName: item.isVariant ? item.variantLabel : null,
            taxRate: item.taxRate || 0,
            priceIncludesTax: item.priceIncludesTax || false,
            unit: item.unit || t('common.unit'),
            minimumStockLevel: item.minimumStockLevel,
            hasVariants: false,
            variants: item.variantId ? [{
                id: item.variantId,
                price: item.price,
                sku: item.sku,
                warehouseStock: item.stockQuantity,
                attributes: item.attributes,
            }] : [],
        }

        onProductSelect(productToAdd)
    }

    const getStockStatus = (item) => {
        const stock = Math.max(0, Number(item.stockQuantity || 0))
        const minimumStockLevel = Math.max(0, Number(item.minimumStockLevel || 0))

        if (stock === 0) return { text: t('quickProductGrid.outOfStock'), color: 'text-red-600 bg-red-50' }
        if (minimumStockLevel > 0 && stock <= minimumStockLevel) return { text: t('quickProductGrid.lowStock'), color: 'text-yellow-600 bg-yellow-50' }
        return { text: t('quickProductGrid.inStock'), color: 'text-green-600 bg-green-50' }
    }

    if (loading) {
        return (
            <div className="flex h-full flex-col items-center justify-center gap-3 py-12">
                <div className="h-8 w-8 animate-spin rounded-full border-4 border-primary-600 border-t-transparent"></div>
                <div className={`text-sm text-gray-500 ${isRTL ? 'text-right' : 'text-left'}`}>
                    {t('common.loading')}
                </div>
            </div>
        )
    }

    if (error) {
        return (
            <div className={`flex h-full flex-col items-center justify-center gap-3 py-8 ${isRTL ? 'text-right' : 'text-left'}`}>
                <div className="text-sm text-red-600">{error}</div>
                <button
                    type="button"
                    onClick={() => void fetchSellableItems({ force: true })}
                    className="btn-secondary"
                >
                    {t('common.retry')}
                </button>
            </div>
        )
    }

    return (
        <div className="flex h-full flex-col" dir={isRTL ? 'rtl' : 'ltr'}>
            {items.length === 0 ? (
                <div className="flex flex-1 items-center justify-center text-gray-500">
                    <div className={`text-center ${isRTL ? 'text-right' : 'text-left'}`}>
                        <div>{warehouseId ? t('quickProductGrid.noProducts') : t('pos.selectWarehouseRequired')}</div>
                    </div>
                </div>
            ) : (
                <div className="grid grid-cols-2 gap-3 overflow-y-auto md:grid-cols-3 lg:grid-cols-4">
                    {items.map(item => {
                        const stockStatus = getStockStatus(item)
                        const isOutOfStock = Number(item.stockQuantity || 0) <= 0

                        return (
                            <button
                                key={item.id}
                                onClick={() => !isOutOfStock && handleItemClick(item)}
                                disabled={isOutOfStock}
                                className={`rounded-lg border-2 p-3 transition-all hover:shadow-md ${isOutOfStock
                                    ? 'cursor-not-allowed border-gray-200 bg-gray-50 opacity-50'
                                    : 'border-gray-300 bg-white hover:border-primary-500'
                                    } ${isRTL ? 'text-right' : 'text-left'}`}
                            >
                                {item.images && (
                                    <div className="mb-2 aspect-square overflow-hidden rounded bg-gray-100">
                                        <img
                                            src={item.images.split(',')[0]}
                                            alt={item.displayName}
                                            className="h-full w-full object-cover"
                                            onError={(event) => { event.target.style.display = 'none' }}
                                        />
                                    </div>
                                )}

                                <div className="space-y-1">
                                    <div className="min-h-[2.5rem] line-clamp-2 text-sm font-medium text-gray-900">
                                        {item.displayName || item.name}
                                    </div>
                                    {item.isVariant && item.variantLabel && (
                                        <div className="truncate text-xs font-medium text-primary-600">
                                            {item.variantLabel}
                                        </div>
                                    )}
                                    <div className={`flex items-center justify-between gap-2 ${isRTL ? 'flex-row-reverse' : ''}`}>
                                        <div className="text-lg font-bold text-primary-600">
                                            {formatCurrency(item.price)}
                                        </div>
                                        <div className={`rounded px-2 py-0.5 text-xs ${stockStatus.color}`}>
                                            {stockStatus.text}
                                        </div>
                                    </div>
                                    {!isOutOfStock && (
                                        <div className="text-xs text-gray-500">
                                            {t('quickProductGrid.stockLabel', { count: Math.max(0, Number(item.stockQuantity || 0)) })}
                                        </div>
                                    )}
                                </div>
                            </button>
                        )
                    })}
                </div>
            )}
        </div>
    )
}
