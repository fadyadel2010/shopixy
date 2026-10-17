/**
 * Shipping Option Row Component
 * Phase: Unified Shipping Configuration
 * 
 * Displays a single shipping option (what customer sees at checkout)
 * with carrier mapping and flow preview.
 * 
 * This replaces the legacy "Shipping Methods" row with carrier integration.
 */

import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
    Edit2, Trash2, ToggleLeft, ToggleRight,
    ArrowRight, Clock
} from 'lucide-react'
import { getCarrierInfo } from '../hooks/useShippingSetupApi'

export default function ShippingOptionRow({
    option,
    carriers,
    onEdit,
    onDelete,
    onToggle,
    formatCurrency
}) {
    const { t, i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const [showFlow, setShowFlow] = useState(false)

    // Get carrier info for this option
    const carrierCode = option.carrierCode || option.carrierName?.toLowerCase()
    const carrierInfo = getCarrierInfo(carrierCode)
    const carrierConfig = carriers?.find(c => c.carrierCode === carrierCode)

    // Determine pricing display
    const getPricingDisplay = () => {
        if (option.freeShippingThreshold && option.baseRate === 0) {
            return {
                primary: t('shippingSetup.free', 'FREE'),
                secondary: `${t('shippingSetup.over', 'Over')} ${formatCurrency(option.freeShippingThreshold)}`
            }
        }

        let primary = formatCurrency(option.baseRate)
        let secondary = null

        if (option.perKgRate > 0) {
            secondary = `+ ${formatCurrency(option.perKgRate)}/kg`
        }

        if (option.freeShippingThreshold) {
            secondary = `${t('shippingSetup.freeOver', 'Free over')} ${formatCurrency(option.freeShippingThreshold)}`
        }

        return { primary, secondary }
    }

    const pricing = getPricingDisplay()
    const displayName = isRTL && option.nameAr ? option.nameAr : option.name

    // Determine if COD is supported
    const supportsCod = carrierConfig?.supportsCod ?? true

    return (
        <div className={`bg-white border rounded-xl overflow-hidden transition-all duration-200 ${option.isActive ? 'border-gray-200' : 'border-gray-100 opacity-60'
            }`}>
            {/* Main Row */}
            <div className="p-4 flex items-center gap-4">
                {/* Carrier Logo */}
                <div className={`w-12 h-12 rounded-xl flex items-center justify-center text-2xl ${carrierInfo.bgClass} ${carrierInfo.borderClass} border`}>
                    {carrierInfo.logo}
                </div>

                {/* Option Details */}
                <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2">
                        <h4 className="font-semibold text-gray-900 truncate">{displayName}</h4>
                        {!option.isActive && (
                            <span className="px-2 py-0.5 bg-gray-100 text-gray-500 text-xs rounded-full">
                                {t('common.inactive', 'Inactive')}
                            </span>
                        )}
                    </div>
                    <div className="flex items-center gap-3 mt-1 text-sm text-gray-500">
                        <span className={`font-medium ${carrierInfo.textClass}`}>{carrierInfo.name}</span>
                        {option.serviceLevel && (
                            <>
                                <span className="text-gray-300">•</span>
                                <span className="uppercase text-xs font-mono">{option.serviceLevel}</span>
                            </>
                        )}
                    </div>
                </div>

                {/* Pricing */}
                <div className="text-right">
                    <div className={`font-bold ${pricing.primary === 'FREE' ? 'text-green-600' : 'text-gray-900'}`}>
                        {pricing.primary}
                    </div>
                    {pricing.secondary && (
                        <div className="text-xs text-gray-500">{pricing.secondary}</div>
                    )}
                </div>

                {/* Delivery Time */}
                <div className="flex items-center gap-1.5 text-sm text-gray-600 bg-gray-50 px-3 py-1.5 rounded-lg">
                    <Clock size={14} />
                    {option.estimatedDeliveryDaysMin}-{option.estimatedDeliveryDaysMax} {t('shippingSetup.days', 'days')}
                </div>

                {/* COD Badge */}
                <div className={`px-2.5 py-1 rounded-lg text-xs font-medium ${supportsCod
                        ? 'bg-emerald-100 text-emerald-700'
                        : 'bg-gray-100 text-gray-500'
                    }`}>
                    {supportsCod ? '💵 COD' : t('shippingSetup.noCoD', 'No COD')}
                </div>

                {/* Toggle */}
                <button
                    onClick={() => onToggle?.(option)}
                    className="p-1 hover:bg-gray-100 rounded-lg transition-colors"
                    title={option.isActive ? t('common.deactivate') : t('common.activate')}
                >
                    {option.isActive ? (
                        <ToggleRight size={28} className="text-green-500" />
                    ) : (
                        <ToggleLeft size={28} className="text-gray-300" />
                    )}
                </button>

                {/* Actions */}
                <div className="flex items-center gap-1">
                    <button
                        onClick={() => onEdit?.(option)}
                        className="p-2 text-gray-400 hover:text-indigo-600 hover:bg-indigo-50 rounded-lg transition-colors"
                        title={t('common.edit')}
                    >
                        <Edit2 size={18} />
                    </button>
                    <button
                        onClick={() => onDelete?.(option)}
                        className="p-2 text-gray-400 hover:text-red-600 hover:bg-red-50 rounded-lg transition-colors"
                        title={t('common.delete')}
                    >
                        <Trash2 size={18} />
                    </button>
                </div>
            </div>

            {/* Flow Preview Toggle */}
            <button
                onClick={() => setShowFlow(!showFlow)}
                className="w-full flex items-center justify-center gap-2 py-2 bg-gray-50 border-t border-gray-100 text-xs text-gray-500 hover:bg-gray-100 transition-colors"
            >
                <ArrowRight size={14} />
                {showFlow ? t('shippingSetup.hideFlow', 'Hide Flow') : t('shippingSetup.showFlow', 'Show Flow Preview')}
            </button>

            {/* Flow Mapping Preview */}
            {showFlow && (
                <div className="px-4 pb-4 pt-2 bg-gradient-to-r from-indigo-50 via-white to-emerald-50">
                    <div className="flex items-center justify-between gap-3 text-sm">
                        {/* Step 1: Customer Selection */}
                        <div className="flex-1 text-center p-3 bg-white rounded-xl border border-indigo-200 shadow-sm">
                            <div className="text-2xl mb-1">🛒</div>
                            <div className="font-medium text-gray-700">{t('shippingSetup.customerSelects', 'Customer selects')}</div>
                            <div className="text-indigo-600 font-semibold truncate">{displayName}</div>
                        </div>

                        <ArrowRight className="text-gray-300 flex-shrink-0" size={24} />

                        {/* Step 2: System Sends */}
                        <div className="flex-1 text-center p-3 bg-white rounded-xl border border-amber-200 shadow-sm">
                            <div className="text-2xl mb-1">{carrierInfo.logo}</div>
                            <div className="font-medium text-gray-700">{t('shippingSetup.systemSends', 'System sends')}</div>
                            <div className="text-amber-600 font-semibold">
                                {carrierInfo.name} / {option.serviceLevel || 'STANDARD'}
                            </div>
                        </div>

                        <ArrowRight className="text-gray-300 flex-shrink-0" size={24} />

                        {/* Step 3: Shipment Created */}
                        <div className="flex-1 text-center p-3 bg-white rounded-xl border border-emerald-200 shadow-sm">
                            <div className="text-2xl mb-1">📦</div>
                            <div className="font-medium text-gray-700">{t('shippingSetup.shipmentCreated', 'Shipment created')}</div>
                            <div className="text-emerald-600 font-semibold">{t('shippingSetup.readyForPickup', 'ReadyForPickup')}</div>
                        </div>
                    </div>

                    <p className="text-xs text-gray-500 text-center mt-3">
                        ⚡ {t('shippingSetup.instantShipment', 'Shipment is created instantly on order completion - no manual steps')}
                    </p>
                </div>
            )}
        </div>
    )
}
