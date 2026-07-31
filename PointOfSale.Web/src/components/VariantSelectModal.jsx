import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../contexts/SettingsContext'
import { toSafeNumber } from '../utils/financialMath'

const resolveWarehouseStock = (variant, warehouseId) => {
    const scopedWarehouseStock = variant?.warehouseStocks?.find(stock => stock.warehouseId === warehouseId)
    return Math.max(
        0,
        toSafeNumber(
            variant?.warehouseStock ??
            scopedWarehouseStock?.availableQuantity ??
            scopedWarehouseStock?.quantity,
        ),
    )
}

export default function VariantSelectModal({
    product,
    warehouseId,
    onSelect,
    onClose,
    allowOutOfStock = false,
    mode = 'sale',
}) {
    const { t, i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const { formatCurrency } = useSettings()
    const [selectedVariant, setSelectedVariant] = useState(null)

    const availableVariants = (product?.variants || [])
        .filter(variant => variant.isActive !== false)
        .map(variant => ({
            ...variant,
            warehouseStock: resolveWarehouseStock(variant, warehouseId),
        }))

    const formatAttributes = (attributes) => {
        if (!Array.isArray(attributes) || attributes.length === 0) {
            return t('variantSelectModal.defaultVariant')
        }

        return attributes.map(attribute => attribute.value).join(' / ')
    }

    const handleSelect = () => {
        if (!selectedVariant) {
            return
        }

        onSelect({
            ...product,
            variantId: selectedVariant.id,
            canonicalUnitPrice: selectedVariant.price,
            canonicalCostPrice: selectedVariant.costPrice,
            sku: selectedVariant.sku,
            barcode: selectedVariant.barcode,
            stockQuantity: selectedVariant.warehouseStock,
            variantAttributes: selectedVariant.attributes || [],
            variantDisplayName: (selectedVariant.attributes || [])
                .map(attribute => `${attribute.name}: ${attribute.value}`)
                .join(', '),
        })
    }

    return (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black bg-opacity-50" dir={isRTL ? 'rtl' : 'ltr'}>
            <div className="mx-4 flex max-h-[90vh] w-full max-w-2xl flex-col overflow-hidden rounded-lg bg-white shadow-xl">
                <div className={`flex items-center justify-between border-b border-gray-200 px-6 py-4 ${isRTL ? 'flex-row-reverse' : ''}`}>
                    <div className={isRTL ? 'text-right' : 'text-left'}>
                        <h2 className="text-xl font-semibold text-gray-900">
                            {t('variantSelectModal.selectVariant')}
                        </h2>
                        <p className="text-sm text-gray-500">{product?.name}</p>
                    </div>
                    <button
                        onClick={onClose}
                        aria-label={t('common.close')}
                        className="text-2xl text-gray-400 hover:text-gray-600"
                    >
                        x
                    </button>
                </div>

                <div className="flex-1 overflow-y-auto p-6">
                    {availableVariants.length === 0 ? (
                        <div className={`py-8 text-center text-gray-500 ${isRTL ? 'text-right' : 'text-left'}`}>
                            {t('variantSelectModal.noVariantsAvailable')}
                        </div>
                    ) : (
                        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                            {availableVariants.map((variant) => {
                                const isOutOfStock = variant.warehouseStock <= 0
                                const isSelected = selectedVariant?.id === variant.id
                                const isDisabled = !allowOutOfStock && isOutOfStock

                                return (
                                    <button
                                        key={variant.id}
                                        onClick={() => !isDisabled && setSelectedVariant(variant)}
                                        disabled={isDisabled}
                                        className={`rounded-lg border-2 p-4 transition-all ${isSelected
                                            ? 'border-primary-500 bg-primary-50 ring-2 ring-primary-200'
                                            : isDisabled
                                                ? 'cursor-not-allowed border-gray-200 bg-gray-50 opacity-50'
                                                : 'border-gray-300 bg-white hover:border-primary-400'
                                            } ${isRTL ? 'text-right' : 'text-left'}`}
                                    >
                                        <div className={`flex items-start gap-3 ${isRTL ? 'flex-row-reverse' : ''}`}>
                                            {variant.imageUrl ? (
                                                <img
                                                    src={variant.imageUrl}
                                                    alt={formatAttributes(variant.attributes)}
                                                    className="h-16 w-16 rounded object-cover"
                                                    onError={(event) => { event.target.style.display = 'none' }}
                                                />
                                            ) : product?.images ? (
                                                <img
                                                    src={product.images.split(',')[0]}
                                                    alt={product.name}
                                                    className="h-16 w-16 rounded object-cover opacity-60"
                                                    onError={(event) => { event.target.style.display = 'none' }}
                                                />
                                            ) : (
                                                <div className="flex h-16 w-16 items-center justify-center rounded bg-gray-100 text-xs text-gray-400">
                                                    SKU
                                                </div>
                                            )}

                                            <div className="flex-1">
                                                <div className="font-medium text-gray-900">
                                                    {formatAttributes(variant.attributes)}
                                                </div>
                                                <div className="text-sm text-gray-500">
                                                    {variant.sku || t('variantSelectModal.skuFallback')}
                                                </div>
                                                <div className={`mt-2 flex items-center justify-between gap-3 ${isRTL ? 'flex-row-reverse' : ''}`}>
                                                    <span className="text-lg font-bold text-primary-600">
                                                        {mode === 'purchase'
                                                            ? formatCurrency(variant.costPrice || variant.price)
                                                            : formatCurrency(variant.price)}
                                                        {mode === 'purchase' ? (
                                                            <span className={`text-xs font-normal text-gray-500 ${isRTL ? 'mr-1' : 'ml-1'}`}>
                                                                {t('variantSelectModal.costSuffix')}
                                                            </span>
                                                        ) : null}
                                                    </span>
                                                    <span className={`rounded px-2 py-0.5 text-sm ${isOutOfStock
                                                        ? 'bg-red-100 text-red-700'
                                                        : variant.warehouseStock <= 5
                                                            ? 'bg-yellow-100 text-yellow-700'
                                                            : 'bg-green-100 text-green-700'
                                                        }`}>
                                                        {isOutOfStock
                                                            ? t('variantSelectModal.outOfStock')
                                                            : t('variantSelectModal.inStock', { count: variant.warehouseStock })}
                                                    </span>
                                                </div>
                                            </div>
                                        </div>
                                    </button>
                                )
                            })}
                        </div>
                    )}
                </div>

                <div className={`flex justify-end gap-3 border-t border-gray-200 px-6 py-4 ${isRTL ? 'flex-row-reverse' : ''}`}>
                    <button
                        onClick={onClose}
                        className="btn-secondary px-6"
                    >
                        {t('common.cancel')}
                    </button>
                    <button
                        onClick={handleSelect}
                        disabled={!selectedVariant}
                        className="btn-primary px-6 disabled:cursor-not-allowed disabled:opacity-50"
                    >
                        {t('variantSelectModal.select')}
                    </button>
                </div>
            </div>
        </div>
    )
}
