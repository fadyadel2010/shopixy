import { useState, useEffect, useRef, forwardRef, useCallback } from 'react'
import { useTranslation } from 'react-i18next'
import { isRequestCanceled } from '../services/api'
import { searchPosProducts } from '../services/posDataService'
import { useDebounce } from '../hooks/useDebounce'

const normalizeCode = (value) => value.trim().toLowerCase()

const resolveExactProductMatch = (products, code) => {
    const normalizedCode = normalizeCode(code)

    const directProductMatch = products.find(product =>
        normalizeCode(product.barcode || '') === normalizedCode
        || normalizeCode(product.sku || '') === normalizedCode,
    )

    if (directProductMatch) {
        return directProductMatch
    }

    for (const product of products) {
        const matchedVariant = product.variants?.find(variant =>
            normalizeCode(variant.barcode || '') === normalizedCode
            || normalizeCode(variant.sku || '') === normalizedCode,
        )

        if (matchedVariant) {
            return {
                ...product,
                matchedVariantId: matchedVariant.id,
            }
        }
    }

    return null
}

const BarcodeScanner = forwardRef(({ onProductFound, onError, autoFocus = true, warehouseId }, ref) => {
    const { t, i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const [barcode, setBarcode] = useState('')
    const [isScanning, setIsScanning] = useState(false)
    const inputRef = useRef(null)
    const requestIdRef = useRef(0)
    const requestControllerRef = useRef(null)
    const manualSearchRef = useRef('')
    const debouncedBarcode = useDebounce(barcode, 100)

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

    const searchProduct = useCallback(async (code) => {
        const trimmedCode = code.trim()
        if (!trimmedCode) {
            return
        }

        if (!warehouseId) {
            requestControllerRef.current?.abort()
            requestIdRef.current += 1
            setIsScanning(false)
            onError(t('pos.selectWarehouseRequired'))
            return
        }

        requestControllerRef.current?.abort()
        const controller = new AbortController()
        requestControllerRef.current = controller

        const requestId = ++requestIdRef.current
        setIsScanning(true)

        try {
            const products = await searchPosProducts({
                query: trimmedCode,
                warehouseId,
                signal: controller.signal,
            })

            if (requestId !== requestIdRef.current || controller.signal.aborted) {
                return
            }

            const matchedProduct = resolveExactProductMatch(products, trimmedCode)

            if (!matchedProduct) {
                onError(t('barcodeScanner.productNotFound', { code: trimmedCode }))
                return
            }

            onProductFound(matchedProduct)
            setBarcode('')
        } catch (error) {
            if (requestId !== requestIdRef.current || controller.signal.aborted || isRequestCanceled(error)) {
                return
            }

            console.error('Barcode search error:', error)
            onError(error.response?.data?.message || t('barcodeScanner.searchError'))
        } finally {
            if (requestId === requestIdRef.current && requestControllerRef.current === controller) {
                requestControllerRef.current = null
                setIsScanning(false)
            }

            if (inputRef.current) {
                inputRef.current.focus()
            }
        }
    }, [onError, onProductFound, t, warehouseId])

    const handleKeyDown = async (event) => {
        if (event.key === 'Enter' && barcode.trim()) {
            event.preventDefault()
            manualSearchRef.current = barcode.trim()
            await searchProduct(barcode.trim())
        }
    }

    useEffect(() => {
        const nextCode = debouncedBarcode.trim()

        if (!nextCode || nextCode.length < 3) {
            return
        }

        if (!warehouseId) {
            requestControllerRef.current?.abort()
            requestIdRef.current += 1
            setIsScanning(false)
            return
        }

        if (normalizeCode(nextCode) === normalizeCode(manualSearchRef.current || '')) {
            manualSearchRef.current = ''
            return
        }

        void searchProduct(nextCode)
    }, [debouncedBarcode, searchProduct, warehouseId])

    return (
        <div className="relative" dir={isRTL ? 'rtl' : 'ltr'}>
            <input
                ref={inputRef}
                type="text"
                value={barcode}
                onChange={(event) => {
                    requestControllerRef.current?.abort()
                    requestIdRef.current += 1
                    setBarcode(event.target.value)
                }}
                onKeyDown={handleKeyDown}
                placeholder={t('barcodeScanner.placeholder')}
                className={`input ${isRTL ? 'pl-10 text-right' : 'pr-10 text-left'}`}
                disabled={isScanning || !warehouseId}
            />
            {isScanning && (
                <div className={`absolute top-1/2 -translate-y-1/2 transform ${isRTL ? 'left-3' : 'right-3'}`}>
                    <div className="h-5 w-5 animate-spin rounded-full border-2 border-primary-600 border-t-transparent"></div>
                </div>
            )}
            <div className={`mt-1 text-xs text-gray-500 ${isRTL ? 'text-right' : 'text-left'}`}>
                {warehouseId ? t('barcodeScanner.hint') : t('pos.selectWarehouseRequired')}
            </div>
        </div>
    )
})

BarcodeScanner.displayName = 'BarcodeScanner'

export default BarcodeScanner
