import { useTranslation } from 'react-i18next'
import { useSettings } from '../contexts/SettingsContext'
import { parseStrictAmountInput } from '../utils/financialMath'

export default function CartItem({ item, onUpdateQuantity, onUpdateDiscount, onUpdateUnit, onRemove }) {
    const { t, i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const { formatCurrency } = useSettings()
    const itemSubtotal = item.quantity * item.unitPrice
    const itemTotal = itemSubtotal - item.discountAmount
    const conversionFactor = Math.max(1, Number(item.conversionFactor || 1))
    const availableQuantity = Math.max(0, Number(item.stockQuantity || 0) / conversionFactor)
    const isLowStock = availableQuantity > 0 && item.quantity >= availableQuantity * 0.8
    const selectableUnits = Array.isArray(item.productUnits) ? item.productUnits : []
    const selectedUnit = selectableUnits.find(unit => unit.unitId === item.unitId)
    const unitDisplayName = selectedUnit
        ? `${selectedUnit.unitName}${selectedUnit.unitSymbol ? ` (${selectedUnit.unitSymbol})` : ''}`
        : item.unitName

    return (
        <article
            className={`border-b border-gray-100 p-3 hover:bg-gray-50 ${isRTL ? 'text-right' : 'text-left'}`}
            dir={isRTL ? 'rtl' : 'ltr'}
        >
            <div className={`flex items-start justify-between gap-2 ${isRTL ? 'flex-row-reverse' : ''}`}>
                <div className="min-w-0 flex-1">
                    <div className="break-words font-medium text-gray-900">{item.name}</div>
                    {item.sku ? (
                        <div className="mt-0.5 break-all text-xs text-gray-500">
                            {t('cartItem.sku', 'SKU')}: {item.sku}
                        </div>
                    ) : null}
                </div>
                <button
                    type="button"
                    onClick={() => onRemove(item.productId, item.variantId, item.unitId)}
                    className="flex h-8 w-8 flex-shrink-0 items-center justify-center rounded text-lg text-red-600 hover:bg-red-50"
                    title={t('cartItem.remove')}
                    aria-label={t('cartItem.remove')}
                >
                    ×
                </button>
            </div>

            <div className="mt-1">
                {(item.variantDisplayName || (item.variantAttributes && item.variantAttributes.length > 0)) && (
                    <div className="text-xs font-medium text-primary-600">
                        {t('cartItem.variant', 'Variant')}: {item.variantDisplayName || item.variantAttributes.map(attribute => `${attribute.name}: ${attribute.value}`).join(', ')}
                    </div>
                )}
                <div className="mt-2 text-sm text-gray-600">
                    <span className="mb-1 block text-xs font-semibold uppercase tracking-wide text-gray-500">
                        {t('cartItem.unit', 'Unit')}
                    </span>
                    {selectableUnits.length > 1 ? (
                        <select
                            value={item.unitId || ''}
                            onChange={(event) => onUpdateUnit(item.productId, item.variantId, event.target.value, item.unitId)}
                            aria-label={t('cartItem.selectUnit', 'Select sale unit')}
                            className={`w-full rounded-md border-2 border-primary-300 bg-white px-3 py-2 text-sm font-medium hover:border-primary-500 focus:border-primary-500 focus:ring-2 focus:ring-primary-200 ${isRTL ? 'text-right' : 'text-left'}`}
                            title={t('cartItem.unitConversion', {
                                unit: item.unitName,
                                conversionFactor,
                            })}
                        >
                            {selectableUnits.map(unit => (
                                <option key={unit.unitId} value={unit.unitId}>
                                    {unit.unitName} ({unit.unitSymbol})
                                </option>
                            ))}
                        </select>
                    ) : (
                        <div className="rounded-md border border-gray-200 bg-gray-50 px-3 py-2 font-medium text-gray-800">
                            {unitDisplayName || t('common.unit', 'Unit')}
                        </div>
                    )}
                </div>
                {isLowStock && (
                    <div className="mt-1 text-xs text-orange-600">
                        {t('cartItem.lowStock', {
                            stock: availableQuantity.toFixed(availableQuantity % 1 === 0 ? 0 : 2),
                            unit: item.unitName,
                        })}
                    </div>
                )}
            </div>

            <div className="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-1 xl:grid-cols-2">
                <div>
                    <span className="mb-1 block text-xs font-semibold text-gray-600">{t('cartItem.quantity', 'Quantity')}</span>
                    <div className={`flex items-center ${isRTL ? 'flex-row-reverse' : ''}`}>
                        <button
                            type="button"
                            onClick={() => onUpdateQuantity(item.productId, item.variantId, item.quantity - 1, item.unitId)}
                            className="flex h-9 w-9 items-center justify-center rounded-l bg-gray-200 text-sm font-bold text-gray-700 hover:bg-gray-300"
                            aria-label={t('cartItem.decreaseQuantity', 'Decrease quantity')}
                        >
                            −
                        </button>
                        <input
                            type="number"
                            value={item.quantity}
                            onChange={(event) => {
                                const quantity = parseStrictAmountInput(event.target.value, { allowZero: true })
                                if (quantity.isValid) {
                                    onUpdateQuantity(item.productId, item.variantId, quantity.value, item.unitId)
                                }
                            }}
                            aria-label={t('cartItem.quantity', 'Quantity')}
                            className="h-9 min-w-0 flex-1 border-y border-gray-300 px-1 text-center text-sm"
                            min="0"
                            step="0.01"
                        />
                        <button
                            type="button"
                            onClick={() => onUpdateQuantity(item.productId, item.variantId, item.quantity + 1, item.unitId)}
                            className="flex h-9 w-9 items-center justify-center rounded-r bg-gray-200 text-sm font-bold text-gray-700 hover:bg-gray-300"
                            aria-label={t('cartItem.increaseQuantity', 'Increase quantity')}
                        >
                            +
                        </button>
                    </div>
                </div>

                <label className="block">
                    <span className="mb-1 block text-xs font-semibold text-gray-600">{t('cartItem.discountPlaceholder')}</span>
                    <input
                        type="number"
                        value={item.discountAmount}
                        onChange={(event) => {
                            const discount = parseStrictAmountInput(event.target.value, { allowZero: true })
                            if (discount.isValid) {
                                onUpdateDiscount(item.productId, item.variantId, discount.value, item.unitId)
                            }
                        }}
                        className={`h-9 w-full rounded border border-gray-300 px-2 text-base ${isRTL ? 'text-right' : 'text-left'}`}
                        min="0"
                        step="0.01"
                    />
                </label>
            </div>

            <div className={`mt-3 flex items-center justify-between border-t border-gray-200 pt-2 text-sm ${isRTL ? 'flex-row-reverse' : ''}`}>
                <span className="text-gray-600">{formatCurrency(item.unitPrice)} × {item.quantity}</span>
                <span className="font-semibold text-gray-900">{t('common.total')}: {formatCurrency(itemTotal)}</span>
            </div>
        </article>
    )
}
