import { useState, useEffect, useRef, forwardRef, useCallback } from 'react'
import { useTranslation } from 'react-i18next'
import { isRequestCanceled } from '../services/api'
import { searchPosProducts } from '../services/posDataService'
import { subscribeToPosRuntimeInvalidation } from '../services/posRuntimeCache'
import { useSettings } from '../contexts/SettingsContext'
import { useDebounce } from '../hooks/useDebounce'

const toSafeStock = (value) => Math.max(0, Number(value || 0))

const resolveWarehouseStock = (product, warehouseId) => {
    if (Array.isArray(product.variants) && product.variants.length > 0) {
        return product.variants.reduce((sum, variant) => sum + toSafeStock(variant.warehouseStock), 0)
    }

    const warehouseStock = product.warehouseStocks?.find(stock => stock.warehouseId === warehouseId)
    return toSafeStock(warehouseStock?.availableQuantity ?? warehouseStock?.quantity)
}

const ProductSearch = forwardRef(({ onProductSelect, warehouseId, autoFocus = false }, ref) => {
    const { t, i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const [query, setQuery] = useState('')
    const [results, setResults] = useState([])
    const [isLoading, setIsLoading] = useState(false)
    const [errorMessage, setErrorMessage] = useState('')
    const [selectedIndex, setSelectedIndex] = useState(-1)
    const [showResults, setShowResults] = useState(false)
    const inputRef = useRef(null)
    const requestIdRef = useRef(0)
    const requestControllerRef = useRef(null)
    const latestQueryRef = useRef('')
    const debouncedQuery = useDebounce(query, 300)

    const { formatCurrency } = useSettings()

    useEffect(() => {
        if (ref) {
            ref.current = inputRef.current
        }
    }, [ref])

    useEffect(() => {
        if (autoFocus && inputRef.current) {
            inputRef.current.focus()
        }
    }, [autoFocus])

    useEffect(() => {
        return () => {
            requestIdRef.current += 1
            requestControllerRef.current?.abort()
        }
    }, [])

    const resetResults = useCallback(() => {
        requestControllerRef.current?.abort()
        setIsLoading(false)
        setResults([])
        setErrorMessage('')
        setShowResults(false)
        setSelectedIndex(-1)
    }, [])

    const searchProducts = useCallback(async (nextQuery, { force = false } = {}) => {
        const trimmedQuery = nextQuery.trim()

        if (!warehouseId || trimmedQuery.length < 2) {
            resetResults()
            return
        }

        requestControllerRef.current?.abort()

        const controller = new AbortController()
        requestControllerRef.current = controller

        const requestId = ++requestIdRef.current
        latestQueryRef.current = trimmedQuery
        setIsLoading(true)
        setErrorMessage('')

        try {
            const nextResults = await searchPosProducts({
                query: trimmedQuery,
                warehouseId,
                signal: controller.signal,
                force,
            })

            if (requestId !== requestIdRef.current || controller.signal.aborted) {
                return
            }

            setResults(nextResults)
            setShowResults(true)
            setSelectedIndex(-1)
        } catch (error) {
            if (requestId !== requestIdRef.current || controller.signal.aborted || isRequestCanceled(error)) {
                return
            }

            console.error('Search error:', error)
            setResults([])
            setShowResults(true)
            setSelectedIndex(-1)
            setErrorMessage(error.response?.data?.message || t('productSearch.loadFailed', { defaultValue: 'Failed to load products.' }))
        } finally {
            if (requestId === requestIdRef.current && requestControllerRef.current === controller) {
                requestControllerRef.current = null
                setIsLoading(false)
            }
        }
    }, [resetResults, t, warehouseId])

    useEffect(() => {
        if (!warehouseId || debouncedQuery.length < 2) {
            latestQueryRef.current = debouncedQuery.trim()
            resetResults()
            return
        }

        void searchProducts(debouncedQuery)
    }, [debouncedQuery, resetResults, searchProducts, warehouseId])

    useEffect(() => subscribeToPosRuntimeInvalidation(() => {
        const activeQuery = latestQueryRef.current.trim()
        if (!warehouseId || activeQuery.length < 2) {
            return
        }

        void searchProducts(activeQuery, { force: true })
    }), [searchProducts, warehouseId])

    const handleKeyDown = (event) => {
        if (!showResults || results.length === 0) return

        if (event.key === 'ArrowDown') {
            event.preventDefault()
            setSelectedIndex(prev => (prev < results.length - 1 ? prev + 1 : prev))
        } else if (event.key === 'ArrowUp') {
            event.preventDefault()
            setSelectedIndex(prev => (prev > 0 ? prev - 1 : -1))
        } else if (event.key === 'Enter' && selectedIndex >= 0) {
            event.preventDefault()
            selectProduct(results[selectedIndex])
        } else if (event.key === 'Escape') {
            setShowResults(false)
            setSelectedIndex(-1)
        }
    }

    const selectProduct = (product) => {
        requestControllerRef.current?.abort()
        requestIdRef.current += 1
        setIsLoading(false)
        setErrorMessage('')
        onProductSelect(product)
        setQuery('')
        latestQueryRef.current = ''
        setResults([])
        setShowResults(false)
        setSelectedIndex(-1)

        if (inputRef.current) {
            inputRef.current.focus()
        }
    }

    return (
        <div className="relative" dir={isRTL ? 'rtl' : 'ltr'}>
            <input
                ref={inputRef}
                type="text"
                value={query}
                onChange={(event) => {
                    const nextQuery = event.target.value
                    latestQueryRef.current = nextQuery.trim()
                    requestControllerRef.current?.abort()
                    requestIdRef.current += 1
                    resetResults()
                    setIsLoading(false)
                    setQuery(nextQuery)
                    setErrorMessage('')
                }}
                onKeyDown={handleKeyDown}
                onFocus={() => {
                    if (results.length > 0 || errorMessage) {
                        setShowResults(true)
                    }
                }}
                placeholder={t('productSearch.placeholder')}
                className={`input ${isRTL ? 'pl-10 text-right' : 'pr-10 text-left'}`}
            />
            {isLoading && (
                <div className={`absolute top-1/2 -translate-y-1/2 transform ${isRTL ? 'left-3' : 'right-3'}`}>
                    <div className="h-5 w-5 animate-spin rounded-full border-2 border-primary-600 border-t-transparent"></div>
                </div>
            )}

            {showResults && (
                <div className="absolute z-50 mt-1 max-h-96 w-full overflow-y-auto rounded-lg border border-gray-200 bg-white shadow-lg">
                    {isLoading && results.length === 0 ? (
                        <div className={`px-4 py-3 text-sm text-gray-500 ${isRTL ? 'text-right' : 'text-left'}`}>
                            {t('common.loading')}
                        </div>
                    ) : null}

                    {!isLoading && errorMessage ? (
                        <div className={`space-y-3 px-4 py-3 ${isRTL ? 'text-right' : 'text-left'}`}>
                            <p className="text-sm text-red-600">{errorMessage}</p>
                            <button
                                type="button"
                                onClick={() => void searchProducts(query, { force: true })}
                                className="text-sm font-medium text-primary-700 hover:text-primary-800"
                            >
                                {t('common.retry')}
                            </button>
                        </div>
                    ) : null}

                    {!isLoading && !errorMessage && results.length > 0 ? (
                        results.map((product, index) => {
                            const stock = resolveWarehouseStock(product, warehouseId)
                            const stockTone = stock > 0 ? 'text-green-600' : 'text-red-600'

                            return (
                                <div
                                    key={product.id}
                                    onClick={() => selectProduct(product)}
                                    className={`cursor-pointer border-b border-gray-100 p-3 last:border-b-0 hover:bg-primary-50 ${index === selectedIndex ? 'bg-primary-100' : ''}`}
                                >
                                    <div className={`flex items-center gap-3 ${isRTL ? 'flex-row-reverse' : ''}`}>
                                        {product.images && (
                                            <img
                                                src={product.images.split(',')[0]}
                                                alt={product.name}
                                                className="h-12 w-12 rounded object-cover"
                                                onError={(event) => { event.target.style.display = 'none' }}
                                            />
                                        )}
                                        <div className={`flex-1 ${isRTL ? 'text-right' : 'text-left'}`}>
                                            <div className="font-medium text-gray-900">{product.name}</div>
                                            <div className="text-sm text-gray-500">
                                                {product.categoryName} - {product.barcode || product.sku}
                                            </div>
                                        </div>
                                        <div className={isRTL ? 'text-left' : 'text-right'}>
                                            <div className="font-semibold text-primary-600">
                                                {formatCurrency(product.salePrice)}
                                            </div>
                                            <div className={`text-xs ${stockTone}`}>
                                                {t('productSearch.stockLabel', { count: stock })}
                                            </div>
                                        </div>
                                    </div>
                                </div>
                            )
                        })
                    ) : null}

                    {!isLoading && !errorMessage && results.length === 0 && query.length >= 2 ? (
                        <div className={`px-4 py-3 text-sm text-gray-500 ${isRTL ? 'text-right' : 'text-left'}`}>
                            {t('productSearch.noResults')}
                        </div>
                    ) : null}
                </div>
            )}

            <div className={`mt-1 text-xs text-gray-500 ${isRTL ? 'text-right' : 'text-left'}`}>
                {warehouseId ? t('productSearch.hint') : t('pos.selectWarehouseRequired')}
            </div>
        </div>
    )
})

ProductSearch.displayName = 'ProductSearch'

export default ProductSearch
