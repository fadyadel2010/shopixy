/**
 * Shipping Quote Card Component
 * Phase: Storefront Shipping Experience
 * 
 * Displays a single shipping option for selection.
 * NO sorting/filtering logic - displays exactly as backend returns.
 */

import { useTranslation } from 'react-i18next'
import { Truck, Clock, Check } from 'lucide-react'

export default function ShippingQuoteCard({
    quote,
    selected,
    onSelect,
    disabled = false
}) {
    const { t, i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'

    return (
        <button
            onClick={() => onSelect(quote)}
            disabled={disabled}
            className={`w-full text-left p-4 rounded-xl border-2 transition-all ${selected
                    ? 'border-blue-500 bg-blue-50 ring-2 ring-blue-200'
                    : 'border-gray-200 bg-white hover:border-blue-300 hover:bg-gray-50'
                } ${disabled ? 'opacity-50 cursor-not-allowed' : 'cursor-pointer'}`}
        >
            <div className="flex items-start justify-between gap-4">
                {/* Left: Carrier Info */}
                <div className="flex-1">
                    <div className="flex items-center gap-2 mb-1">
                        <Truck size={18} className={selected ? 'text-blue-600' : 'text-gray-500'} />
                        <span className="font-semibold text-gray-900">
                            {quote.carrierName}
                        </span>
                        {quote.isFreeShipping && (
                            <span className="px-2 py-0.5 text-xs font-medium bg-green-100 text-green-700 rounded-full">
                                {t('storefront.shipping.free', 'FREE')}
                            </span>
                        )}
                    </div>

                    <p className="text-sm text-gray-600 mb-2">
                        {quote.serviceName}
                    </p>

                    <div className="flex items-center gap-4 text-xs text-gray-500">
                        {/* Delivery Estimate */}
                        <span className="flex items-center gap-1">
                            <Clock size={12} />
                            {quote.estimatedDelivery}
                        </span>

                        {/* COD Badge */}
                        {quote.supportsCod && (
                            <span className="px-2 py-0.5 bg-amber-100 text-amber-700 rounded-full">
                                {t('storefront.shipping.codAvailable', 'COD Available')}
                            </span>
                        )}
                    </div>
                </div>

                {/* Right: Price */}
                <div className={`text-right ${isRTL ? 'text-left' : 'text-right'}`}>
                    {quote.isFreeShipping && quote.originalCost > 0 && (
                        <p className="text-sm text-gray-400 line-through">
                            {quote.originalCost.toLocaleString()} {quote.currency}
                        </p>
                    )}
                    <p className={`text-lg font-bold ${quote.isFreeShipping
                            ? 'text-green-600'
                            : 'text-gray-900'
                        }`}>
                        {quote.cost === 0
                            ? t('storefront.shipping.free', 'FREE')
                            : `${quote.cost.toLocaleString()} ${quote.currency}`
                        }
                    </p>
                </div>

                {/* Selection Indicator */}
                {selected && (
                    <div className="w-6 h-6 bg-blue-500 rounded-full flex items-center justify-center">
                        <Check size={14} className="text-white" />
                    </div>
                )}
            </div>
        </button>
    )
}
