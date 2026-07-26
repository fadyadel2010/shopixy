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

    return (
        <div
            className={`flex items-center gap-2 border-b border-gray-100 p-3 hover:bg-gray-50 ${isRTL ? 'flex-row-reverse text-right' : 'text-left'}`}
            dir={isRTL ? 'rtl' : 'ltr'}
        >
            <div className="min-w-[200px] flex-[2]">
                <div className="truncate font-medium text-gray-900">{item.name}</div>
                {(item.variantDisplayName || (item.variantAttributes && item.variantAttributes.length > 0)) && (
                    <div className="text-xs font-medium text-primary-600">
                        {item.variantDisplayName || item.variantAttributes.map(attribute => `${attribute.name}: ${attribute.value}`).join(', ')}
                    </div>
                )}
                <div className={`mt-1 flex items-center gap-3 text-sm text-gray-500 flex-wrap ${isRTL ? 'flex-row-reverse' : ''}`}>
                    <span className="whitespace-nowrap font-semibold">{formatCurrency(item.unitPrice)} x {item.quantity}</span>
                    {item.productUnits && item.productUnits.length > 0 ? (
                        <select
                            value={item.unitId || ''}
                            onChange={(event) => onUpdateUnit(item.productId, item.variantId, event.target.value, item.unitId)}
                            className={`min-w-[140px] rounded-md border-2 border-primary-300 bg-white px-3 py-1 text-sm font-medium hover:border-primary-500 focus:border-primary-500 focus:ring-2 focus:ring-primary-200 ${isRTL ? 'text-right' : 'text-left'}`}
                            title={t('cartItem.unitConversion', {
                                unit: item.unitName,
                                conversionFactor,
                            })}
                        >
                            {item.productUnits.map(unit => (
                                <option key={unit.unitId} value={unit.unitId}>
                                    {unit.unitName} ({unit.unitSymbol})
                                </option>
                            ))}
                        </select>
                    ) : (
                        <span className="font-medium">{item.unitName}</span>
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

            <div className={`flex items-center gap-1 ${isRTL ? 'flex-row-reverse' : ''}`}>
                <button
                    onClick={() => onUpdateQuantity(item.productId, item.variantId, item.quantity - 1, item.unitId)}
                    className="flex h-7 w-7 items-center justify-center rounded bg-gray-200 text-sm font-bold text-gray-700 hover:bg-gray-300"
                >
                    -
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
                    className="w-14 rounded border border-gray-300 px-1 py-1 text-center text-sm"
                    min="0"
                    step="0.01"
                />
                <button
                    onClick={() => onUpdateQuantity(item.productId, item.variantId, item.quantity + 1, item.unitId)}
                    className="flex h-7 w-7 items-center justify-center rounded bg-gray-200 text-sm font-bold text-gray-700 hover:bg-gray-300"
                >
                    +
                </button>
            </div>

            <div style={{ width: '140px' }}>
                <input
                    type="number"
                    value={item.discountAmount}
                    onChange={(event) => {
                        const discount = parseStrictAmountInput(event.target.value, { allowZero: true })
                        if (discount.isValid) {
                            onUpdateDiscount(item.productId, item.variantId, discount.value, item.unitId)
                        }
                    }}
                    placeholder={t('cartItem.discountPlaceholder')}
                    style={{ width: '100%', fontSize: '16px', padding: '6px 8px' }}
                    className={`rounded border border-gray-300 ${isRTL ? 'text-right' : 'text-left'}`}
                    min="0"
                    step="0.01"
                />
            </div>

            <div className={`w-24 text-sm font-semibold text-gray-900 ${isRTL ? 'text-left' : 'text-right'}`}>
                {formatCurrency(itemTotal)}
            </div>

            <button
                onClick={() => onRemove(item.productId, item.variantId, item.unitId)}
                className="flex h-7 w-7 flex-shrink-0 items-center justify-center rounded text-red-600 hover:bg-red-50"
                title={t('cartItem.remove')}
            >
                x
            </button>
        </div>
    )
}
