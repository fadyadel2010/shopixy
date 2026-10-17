/**
 * Shipping Option Form Modal
 * Phase: Unified Shipping Configuration
 * 
 * Modal form for creating/editing shipping options.
 * Links shipping options to carriers and service levels.
 */

import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { X, Truck, DollarSign, Clock, AlertCircle } from 'lucide-react'
import { getCarrierInfo } from '../hooks/useShippingSetupApi'

export default function ShippingOptionFormModal({
    isOpen,
    onClose,
    onSave,
    option = null,
    carriers = [],
    loading = false,
    currencySymbol = '$'
}) {
    const { t } = useTranslation()
    const isEditing = !!option?.id

    const [formData, setFormData] = useState({
        name: '',
        nameAr: '',
        description: '',
        carrierCode: '',
        serviceLevel: 'standard',
        baseRate: '0',
        perKgRate: '0',
        freeShippingThreshold: '',
        estimatedDeliveryDaysMin: '1',
        estimatedDeliveryDaysMax: '5',
        isActive: true,
        sortOrder: '0'
    })

    const [errors, setErrors] = useState({})

    // Populate form when editing
    useEffect(() => {
        if (option) {
            setFormData({
                name: option.name || '',
                nameAr: option.nameAr || '',
                description: option.description || '',
                carrierCode: option.carrierCode || option.carrierName?.toLowerCase() || '',
                serviceLevel: option.serviceLevel || 'standard',
                baseRate: option.baseRate?.toString() || '0',
                perKgRate: option.perKgRate?.toString() || '0',
                freeShippingThreshold: option.freeShippingThreshold?.toString() || '',
                estimatedDeliveryDaysMin: option.estimatedDeliveryDaysMin?.toString() || '1',
                estimatedDeliveryDaysMax: option.estimatedDeliveryDaysMax?.toString() || '5',
                isActive: option.isActive ?? true,
                sortOrder: option.sortOrder?.toString() || '0'
            })
        } else {
            // Reset for new option
            setFormData({
                name: '',
                nameAr: '',
                description: '',
                carrierCode: carriers.find(c => c.isEnabled)?.carrierCode || '',
                serviceLevel: 'standard',
                baseRate: '0',
                perKgRate: '0',
                freeShippingThreshold: '',
                estimatedDeliveryDaysMin: '1',
                estimatedDeliveryDaysMax: '5',
                isActive: true,
                sortOrder: '0'
            })
        }
        setErrors({})
    }, [option, carriers, isOpen])

    // Get service levels for selected carrier
    const selectedCarrier = carriers.find(c => c.carrierCode === formData.carrierCode)
    const serviceLevels = selectedCarrier?.serviceLevels || []
    const carrierInfo = getCarrierInfo(formData.carrierCode)

    const validate = () => {
        const newErrors = {}

        if (!formData.name.trim()) {
            newErrors.name = t('common.required', 'Required')
        }
        if (!formData.carrierCode) {
            newErrors.carrierCode = t('shippingSetup.selectCarrier', 'Please select a carrier')
        }
        if (parseFloat(formData.estimatedDeliveryDaysMin) > parseFloat(formData.estimatedDeliveryDaysMax)) {
            newErrors.estimatedDeliveryDaysMax = t('shippingSetup.invalidDayRange', 'Max must be >= min')
        }

        setErrors(newErrors)
        return Object.keys(newErrors).length === 0
    }

    const handleSubmit = (e) => {
        e.preventDefault()

        if (!validate()) return

        const data = {
            ...formData,
            // Keep carrierName for backwards compatibility with existing API
            carrierName: carrierInfo.name,
            baseRate: parseFloat(formData.baseRate) || 0,
            perKgRate: parseFloat(formData.perKgRate) || 0,
            freeShippingThreshold: formData.freeShippingThreshold
                ? parseFloat(formData.freeShippingThreshold)
                : null,
            estimatedDeliveryDaysMin: parseInt(formData.estimatedDeliveryDaysMin) || 1,
            estimatedDeliveryDaysMax: parseInt(formData.estimatedDeliveryDaysMax) || 5,
            sortOrder: parseInt(formData.sortOrder) || 0
        }

        onSave(data)
    }

    if (!isOpen) return null

    // Filter to only enabled carriers
    const enabledCarriers = carriers.filter(c => c.isConfigured || c.isEnabled)

    return (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-4">
            <div className="bg-white rounded-2xl shadow-2xl w-full max-w-2xl max-h-[90vh] overflow-y-auto">
                {/* Header */}
                <div className="flex items-center justify-between p-6 border-b border-gray-100">
                    <div className="flex items-center gap-3">
                        <div className="w-10 h-10 bg-indigo-100 rounded-xl flex items-center justify-center">
                            <Truck className="text-indigo-600" size={22} />
                        </div>
                        <div>
                            <h2 className="text-xl font-bold text-gray-900">
                                {isEditing
                                    ? t('shippingSetup.editOption', 'Edit Shipping Option')
                                    : t('shippingSetup.addOption', 'Add Shipping Option')
                                }
                            </h2>
                            <p className="text-sm text-gray-500">
                                {t('shippingSetup.optionDescription', 'This is what customers see at checkout')}
                            </p>
                        </div>
                    </div>
                    <button
                        onClick={onClose}
                        className="p-2 hover:bg-gray-100 rounded-lg transition-colors"
                    >
                        <X size={20} className="text-gray-400" />
                    </button>
                </div>

                {/* Form */}
                <form onSubmit={handleSubmit} className="p-6 space-y-6">
                    {/* Display Names */}
                    <div>
                        <h3 className="text-sm font-semibold text-gray-700 mb-3">
                            {t('shippingSetup.displayName', 'Display Name (What customer sees)')}
                        </h3>
                        <div className="grid grid-cols-2 gap-4">
                            <div>
                                <label className="block text-xs font-medium text-gray-500 mb-1">
                                    English *
                                </label>
                                <input
                                    type="text"
                                    value={formData.name}
                                    onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                                    placeholder="e.g., Express Delivery"
                                    className={`w-full px-3 py-2 border rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500 ${errors.name ? 'border-red-300' : 'border-gray-200'
                                        }`}
                                />
                                {errors.name && <p className="text-xs text-red-500 mt-1">{errors.name}</p>}
                            </div>
                            <div>
                                <label className="block text-xs font-medium text-gray-500 mb-1">
                                    العربية
                                </label>
                                <input
                                    type="text"
                                    dir="rtl"
                                    value={formData.nameAr}
                                    onChange={(e) => setFormData({ ...formData, nameAr: e.target.value })}
                                    placeholder="مثل: التوصيل السريع"
                                    className="w-full px-3 py-2 border border-gray-200 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                                />
                            </div>
                        </div>
                    </div>

                    {/* Carrier Selection */}
                    <div>
                        <h3 className="text-sm font-semibold text-gray-700 mb-3 flex items-center gap-2">
                            <Truck size={16} />
                            {t('shippingSetup.carrierMapping', 'Carrier Mapping')}
                        </h3>

                        {enabledCarriers.length === 0 ? (
                            <div className="p-4 bg-amber-50 border border-amber-200 rounded-xl flex items-center gap-3">
                                <AlertCircle className="text-amber-500" size={20} />
                                <div className="text-sm text-amber-700">
                                    {t('shippingSetup.noCarriersConfigured', 'No carriers configured. Please configure a carrier first.')}
                                </div>
                            </div>
                        ) : (
                            <div className="grid grid-cols-2 gap-4">
                                <div>
                                    <label className="block text-xs font-medium text-gray-500 mb-1">
                                        {t('shippingSetup.carrier', 'Carrier')} *
                                    </label>
                                    <select
                                        value={formData.carrierCode}
                                        onChange={(e) => setFormData({
                                            ...formData,
                                            carrierCode: e.target.value,
                                            serviceLevel: 'standard'
                                        })}
                                        className={`w-full px-3 py-2 border rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500 ${errors.carrierCode ? 'border-red-300' : 'border-gray-200'
                                            }`}
                                    >
                                        <option value="">{t('shippingSetup.selectCarrier', 'Select carrier...')}</option>
                                        {carriers.map(carrier => {
                                            const info = getCarrierInfo(carrier.carrierCode)
                                            return (
                                                <option key={carrier.carrierCode} value={carrier.carrierCode}>
                                                    {info.logo} {info.name} {!carrier.isConfigured && '(Not configured)'}
                                                </option>
                                            )
                                        })}
                                    </select>
                                    {errors.carrierCode && <p className="text-xs text-red-500 mt-1">{errors.carrierCode}</p>}
                                </div>
                                <div>
                                    <label className="block text-xs font-medium text-gray-500 mb-1">
                                        {t('shippingSetup.serviceLevel', 'Service Level')}
                                    </label>
                                    <select
                                        value={formData.serviceLevel}
                                        onChange={(e) => setFormData({ ...formData, serviceLevel: e.target.value })}
                                        className="w-full px-3 py-2 border border-gray-200 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                                        disabled={!formData.carrierCode}
                                    >
                                        {serviceLevels.length > 0 ? (
                                            serviceLevels.map(level => (
                                                <option key={level.code} value={level.code}>
                                                    {level.name} ({level.typicalDaysMin}-{level.typicalDaysMax} days)
                                                </option>
                                            ))
                                        ) : (
                                            <>
                                                <option value="standard">Standard</option>
                                                <option value="express">Express</option>
                                                <option value="same_day">Same Day</option>
                                            </>
                                        )}
                                    </select>
                                </div>
                            </div>
                        )}
                    </div>

                    {/* Pricing */}
                    <div>
                        <h3 className="text-sm font-semibold text-gray-700 mb-3 flex items-center gap-2">
                            <DollarSign size={16} />
                            {t('shippingSetup.pricing', 'Pricing Strategy')}
                        </h3>
                        <div className="grid grid-cols-3 gap-4">
                            <div>
                                <label className="block text-xs font-medium text-gray-500 mb-1">
                                    {t('shippingSetup.baseRate', 'Base Rate')} ({currencySymbol})
                                </label>
                                <input
                                    type="number"
                                    step="0.01"
                                    min="0"
                                    value={formData.baseRate}
                                    onChange={(e) => setFormData({ ...formData, baseRate: e.target.value })}
                                    className="w-full px-3 py-2 border border-gray-200 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                                />
                            </div>
                            <div>
                                <label className="block text-xs font-medium text-gray-500 mb-1">
                                    {t('shippingSetup.perKgRate', 'Per KG Rate')} ({currencySymbol})
                                </label>
                                <input
                                    type="number"
                                    step="0.01"
                                    min="0"
                                    value={formData.perKgRate}
                                    onChange={(e) => setFormData({ ...formData, perKgRate: e.target.value })}
                                    className="w-full px-3 py-2 border border-gray-200 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                                />
                            </div>
                            <div>
                                <label className="block text-xs font-medium text-gray-500 mb-1">
                                    {t('shippingSetup.freeThreshold', 'Free Shipping Over')} ({currencySymbol})
                                </label>
                                <input
                                    type="number"
                                    step="0.01"
                                    min="0"
                                    value={formData.freeShippingThreshold}
                                    onChange={(e) => setFormData({ ...formData, freeShippingThreshold: e.target.value })}
                                    placeholder={t('common.optional', 'Optional')}
                                    className="w-full px-3 py-2 border border-gray-200 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                                />
                            </div>
                        </div>
                    </div>

                    {/* Delivery Time */}
                    <div>
                        <h3 className="text-sm font-semibold text-gray-700 mb-3 flex items-center gap-2">
                            <Clock size={16} />
                            {t('shippingSetup.deliveryTime', 'Delivery Time')}
                        </h3>
                        <div className="grid grid-cols-3 gap-4">
                            <div>
                                <label className="block text-xs font-medium text-gray-500 mb-1">
                                    {t('shippingSetup.minDays', 'Min Days')}
                                </label>
                                <input
                                    type="number"
                                    min="0"
                                    value={formData.estimatedDeliveryDaysMin}
                                    onChange={(e) => setFormData({ ...formData, estimatedDeliveryDaysMin: e.target.value })}
                                    className="w-full px-3 py-2 border border-gray-200 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                                />
                            </div>
                            <div>
                                <label className="block text-xs font-medium text-gray-500 mb-1">
                                    {t('shippingSetup.maxDays', 'Max Days')}
                                </label>
                                <input
                                    type="number"
                                    min="0"
                                    value={formData.estimatedDeliveryDaysMax}
                                    onChange={(e) => setFormData({ ...formData, estimatedDeliveryDaysMax: e.target.value })}
                                    className={`w-full px-3 py-2 border rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500 ${errors.estimatedDeliveryDaysMax ? 'border-red-300' : 'border-gray-200'
                                        }`}
                                />
                                {errors.estimatedDeliveryDaysMax && (
                                    <p className="text-xs text-red-500 mt-1">{errors.estimatedDeliveryDaysMax}</p>
                                )}
                            </div>
                            <div>
                                <label className="block text-xs font-medium text-gray-500 mb-1">
                                    {t('shippingSetup.sortOrder', 'Sort Order')}
                                </label>
                                <input
                                    type="number"
                                    min="0"
                                    value={formData.sortOrder}
                                    onChange={(e) => setFormData({ ...formData, sortOrder: e.target.value })}
                                    className="w-full px-3 py-2 border border-gray-200 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                                />
                            </div>
                        </div>
                    </div>

                    {/* Active Toggle */}
                    <div className="flex items-center gap-3 p-4 bg-gray-50 rounded-xl">
                        <input
                            type="checkbox"
                            id="isActive"
                            checked={formData.isActive}
                            onChange={(e) => setFormData({ ...formData, isActive: e.target.checked })}
                            className="w-5 h-5 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
                        />
                        <label htmlFor="isActive" className="flex-1">
                            <span className="font-medium text-gray-900">{t('shippingSetup.activeOption', 'Active')}</span>
                            <p className="text-sm text-gray-500">{t('shippingSetup.activeDescription', 'When active, customers can select this option at checkout')}</p>
                        </label>
                    </div>

                    {/* Actions */}
                    <div className="flex gap-3 pt-4 border-t border-gray-100">
                        <button
                            type="button"
                            onClick={onClose}
                            className="flex-1 py-2.5 px-4 border border-gray-200 text-gray-700 font-medium rounded-xl hover:bg-gray-50 transition-colors"
                        >
                            {t('common.cancel', 'Cancel')}
                        </button>
                        <button
                            type="submit"
                            disabled={loading}
                            className="flex-1 py-2.5 px-4 bg-indigo-600 text-white font-medium rounded-xl hover:bg-indigo-700 transition-colors disabled:opacity-50"
                        >
                            {loading
                                ? t('common.saving', 'Saving...')
                                : isEditing
                                    ? t('common.update', 'Update')
                                    : t('common.create', 'Create')
                            }
                        </button>
                    </div>
                </form>
            </div>
        </div>
    )
}
