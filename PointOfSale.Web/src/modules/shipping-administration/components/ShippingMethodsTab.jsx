import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useConfirmDialog } from '../../../common/confirm/ConfirmDialogProvider'
import api from '../../../services/api'
import { useSettings } from '../../../contexts/SettingsContext'
import { fetchSupportedCarriers, getCarrierDisplayName } from '../shippingAdministrationApi'

const GUID_REGEX = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i
const DEFAULT_CARRIER_CODE = 'manual'
const DEFAULT_SERVICE_LEVEL = 'standard'

function requireText(value, field) {
    if (typeof value !== 'string' || !value.trim()) {
        throw new Error(`Shipping method read authority rejected missing ${field}.`)
    }

    return value.trim()
}

function optionalText(value, field) {
    if (value === null || value === undefined) {
        return null
    }

    if (typeof value !== 'string') {
        throw new Error(`Shipping method read authority rejected malformed ${field}.`)
    }

    const normalized = value.trim()
    return normalized || null
}

function requireNonNegativeNumber(value, field) {
    if (typeof value !== 'number' || !Number.isFinite(value) || value < 0) {
        throw new Error(`Shipping method read authority rejected invalid ${field}.`)
    }

    return value
}

function requireInteger(value, field, min = 0) {
    if (!Number.isInteger(value) || value < min) {
        throw new Error(`Shipping method read authority rejected invalid ${field}.`)
    }

    return value
}

export function admitAdminShippingMethods(payload) {
    if (!Array.isArray(payload)) {
        throw new Error('Shipping method read authority rejected malformed DTO list.')
    }

    const seenIds = new Set()

    return payload.map((method) => {
        if (!method || typeof method !== 'object') {
            throw new Error('Shipping method read authority rejected malformed DTO.')
        }

        const id = requireText(method.id, 'identity')
        if (!GUID_REGEX.test(id) || seenIds.has(id)) {
            throw new Error('Shipping method read authority rejected duplicate or malformed identity.')
        }
        seenIds.add(id)

        if (typeof method.isActive !== 'boolean') {
            throw new Error('Shipping method read authority rejected malformed enabled state.')
        }

        const minDays = requireInteger(method.estimatedDeliveryDaysMin, 'estimated delivery minimum', 0)
        const maxDays = requireInteger(method.estimatedDeliveryDaysMax, 'estimated delivery maximum', minDays)

        return {
            ...method,
            id,
            name: requireText(method.name, 'name'),
            nameAr: optionalText(method.nameAr, 'localized name'),
            description: optionalText(method.description, 'description'),
            baseRate: requireNonNegativeNumber(method.baseRate, 'base rate'),
            perKgRate: requireNonNegativeNumber(method.perKgRate, 'per kg rate'),
            freeShippingThreshold: method.freeShippingThreshold === null || method.freeShippingThreshold === undefined
                ? null
                : requireNonNegativeNumber(method.freeShippingThreshold, 'free shipping threshold'),
            estimatedDeliveryDaysMin: minDays,
            estimatedDeliveryDaysMax: maxDays,
            sortOrder: requireInteger(method.sortOrder, 'display order'),
            carrierName: optionalText(method.carrierName, 'carrier'),
            carrierCode: optionalText(method.carrierCode, 'carrier code'),
            serviceLevel: optionalText(method.serviceLevel, 'service level') || DEFAULT_SERVICE_LEVEL,
            trackingUrlTemplate: optionalText(method.trackingUrlTemplate, 'tracking URL template'),
            availableCountries: optionalText(method.availableCountries, 'zone assignment'),
        }
    })
}

function normalizeCarrierCode(value) {
    return typeof value === 'string' && value.trim()
        ? value.trim().toLowerCase()
        : DEFAULT_CARRIER_CODE
}

function findCarrier(supportedCarriers, carrierCode) {
    return supportedCarriers.find((carrier) => carrier.carrierCode === carrierCode) || null
}

function getDefaultServiceLevel(carrier) {
    return carrier?.serviceLevels?.[0]?.code || DEFAULT_SERVICE_LEVEL
}

function isServiceLevelAllowed(carrier, serviceLevel) {
    if (!carrier?.serviceLevels?.length) {
        return serviceLevel === DEFAULT_SERVICE_LEVEL
    }

    return carrier.serviceLevels.some((level) => level.code === serviceLevel)
}

export default function ShippingMethodsTab({ warehouses = [] }) {
    const { t, i18n } = useTranslation()
    const { confirm } = useConfirmDialog()
    const { formatCurrency, settings } = useSettings()
    const isRTL = i18n.language === 'ar'
    const [methods, setMethods] = useState([])
    const [supportedCarriers, setSupportedCarriers] = useState([])
    const [loading, setLoading] = useState(true)
    const [carriersLoading, setCarriersLoading] = useState(true)
    const [carrierLoadError, setCarrierLoadError] = useState('')
    const [showModal, setShowModal] = useState(false)
    const [editingMethod, setEditingMethod] = useState(null)
    const [formData, setFormData] = useState({
        name: '',
        nameAr: '',
        description: '',
        baseRate: '0',
        perKgRate: '0',
        freeShippingThreshold: '',
        estimatedDeliveryDaysMin: '1',
        estimatedDeliveryDaysMax: '5',
        isActive: true,
        sortOrder: '0',
        carrierCode: DEFAULT_CARRIER_CODE,
        serviceLevel: DEFAULT_SERVICE_LEVEL,
        fulfillmentWarehouseId: '',
        trackingUrlTemplate: '',
    })
    const [error, setError] = useState('')
    const [success, setSuccess] = useState('')

    const fetchMethods = async () => {
        try {
            setLoading(true)
            const response = await api.get('/shippingmethods?activeOnly=false', {
                headers: {
                    'Cache-Control': 'no-store',
                    Pragma: 'no-cache',
                },
            })
            setMethods(admitAdminShippingMethods(response.data))
        } catch (err) {
            setError(t('common.failedToLoad'))
        } finally {
            setLoading(false)
        }
    }

    const fetchCarriers = async () => {
        try {
            setCarriersLoading(true)
            setCarrierLoadError('')
            setSupportedCarriers(await fetchSupportedCarriers())
        } catch (err) {
            setSupportedCarriers([])
            setCarrierLoadError(err.message || t('shippingAdministration.methods.carrierLoadFailed'))
        } finally {
            setCarriersLoading(false)
        }
    }

    useEffect(() => {
        fetchMethods()
        fetchCarriers()
    }, [])

    const selectedCarrier = findCarrier(supportedCarriers, formData.carrierCode)
    const selectedCarrierServiceLevels = selectedCarrier?.serviceLevels || []
    const activeWarehouses = warehouses.filter((warehouse) => warehouse.isActive !== false)
    const singleActiveWarehouseId = activeWarehouses.length === 1 ? activeWarehouses[0].id : ''
    const unsupportedLegacyCarrierCode = formData.carrierCode && !selectedCarrier
        ? formData.carrierCode
        : ''

    const resolveCarrierDisplayName = (method) => (
        getCarrierDisplayName(supportedCarriers, normalizeCarrierCode(method.carrierCode))
        || method.carrierName
        || normalizeCarrierCode(method.carrierCode)
    )

    const resolveWarehouseDisplayName = (warehouseId) => {
        const warehouse = warehouses.find((item) => item.id === warehouseId)
        if (!warehouse) return t('shippingAdministration.methods.notConfigured')
        return warehouse.code ? `${warehouse.name} (${warehouse.code})` : warehouse.name
    }

    const resetForm = () => {
        setEditingMethod(null)
        setFormData({
            name: '',
            nameAr: '',
            description: '',
            baseRate: '0',
            perKgRate: '0',
            freeShippingThreshold: '',
            estimatedDeliveryDaysMin: '1',
            estimatedDeliveryDaysMax: '5',
            isActive: true,
            sortOrder: '0',
            carrierCode: DEFAULT_CARRIER_CODE,
            serviceLevel: getDefaultServiceLevel(findCarrier(supportedCarriers, DEFAULT_CARRIER_CODE)),
            fulfillmentWarehouseId: singleActiveWarehouseId,
            trackingUrlTemplate: '',
        })
    }

    const updateCarrierSelection = (carrierCode) => {
        const normalizedCarrierCode = normalizeCarrierCode(carrierCode)
        const carrier = findCarrier(supportedCarriers, normalizedCarrierCode)
        setFormData({
            ...formData,
            carrierCode: normalizedCarrierCode,
            serviceLevel: getDefaultServiceLevel(carrier),
        })
    }

    const handleSubmit = async (e) => {
        e.preventDefault()
        setError('')

        try {
            const normalizedCarrierCode = normalizeCarrierCode(formData.carrierCode)
            const carrier = findCarrier(supportedCarriers, normalizedCarrierCode)
            if (!carrier) {
                setError(t('shippingAdministration.methods.selectSupportedCarrier'))
                return
            }

            const normalizedServiceLevel = isServiceLevelAllowed(carrier, formData.serviceLevel)
                ? formData.serviceLevel
                : getDefaultServiceLevel(carrier)

            if (formData.isActive && !formData.fulfillmentWarehouseId) {
                setError(t('shippingAdministration.methods.activeWarehouseRequired'))
                return
            }

            const data = {
                name: formData.name,
                nameAr: formData.nameAr,
                description: formData.description,
                carrierCode: normalizedCarrierCode,
                serviceLevel: normalizedServiceLevel,
                fulfillmentWarehouseId: formData.fulfillmentWarehouseId || null,
                isActive: formData.isActive,
                trackingUrlTemplate: formData.trackingUrlTemplate,
                baseRate: parseFloat(formData.baseRate) || 0,
                perKgRate: parseFloat(formData.perKgRate) || 0,
                freeShippingThreshold: formData.freeShippingThreshold ? parseFloat(formData.freeShippingThreshold) : null,
                estimatedDeliveryDaysMin: parseInt(formData.estimatedDeliveryDaysMin) || 1,
                estimatedDeliveryDaysMax: parseInt(formData.estimatedDeliveryDaysMax) || 5,
                sortOrder: parseInt(formData.sortOrder) || 0,
            }

            if (editingMethod) {
                await api.put(`/shippingmethods/${editingMethod.id}`, data)
                setSuccess(t('common.updatedSuccessfully'))
            } else {
                await api.post('/shippingmethods', data)
                setSuccess(t('shipping.created'))
            }

            setShowModal(false)
            fetchMethods()
            resetForm()
        } catch (err) {
            setError(err.response?.data?.message || t('common.error'))
        }
    }

    const handleEdit = (method) => {
        const normalizedCarrierCode = normalizeCarrierCode(method.carrierCode)
        const carrier = findCarrier(supportedCarriers, normalizedCarrierCode)
        const serviceLevel = method.serviceLevel || getDefaultServiceLevel(carrier)
        setEditingMethod(method)
        setFormData({
            name: method.name,
            nameAr: method.nameAr || '',
            description: method.description || '',
            baseRate: method.baseRate.toString(),
            perKgRate: method.perKgRate.toString(),
            freeShippingThreshold: method.freeShippingThreshold?.toString() || '',
            estimatedDeliveryDaysMin: method.estimatedDeliveryDaysMin.toString(),
            estimatedDeliveryDaysMax: method.estimatedDeliveryDaysMax.toString(),
            isActive: method.isActive,
            sortOrder: method.sortOrder.toString(),
            carrierCode: normalizedCarrierCode,
            serviceLevel: isServiceLevelAllowed(carrier, serviceLevel) ? serviceLevel : getDefaultServiceLevel(carrier),
            fulfillmentWarehouseId: method.fulfillmentWarehouseId || '',
            trackingUrlTemplate: method.trackingUrlTemplate || '',
        })
        setShowModal(true)
    }

    const handleDelete = async (id) => {
        const confirmed = await confirm({
            confirmLabel: t('common.delete'),
            description: t('common.deleteConfirm', { item: t('shipping.shippingMethod') }),
            title: t('common.warning'),
            variant: 'danger',
        })

        if (!confirmed) return

        try {
            await api.delete(`/shippingmethods/${id}`)
            setSuccess(t('common.deletedSuccessfully'))
            fetchMethods()
        } catch (err) {
            setError(err.response?.data?.message || t('common.deleteFailed'))
        }
    }

    const openAddModal = () => {
        resetForm()
        setShowModal(true)
    }

    return (
        <div>
            <div className="mb-6 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                <div>
                    <h2 className="text-xl font-bold text-gray-900">{t('shipping.title')}</h2>
                    <p className="text-sm text-gray-600">{t('shipping.subtitle')}</p>
                </div>
                <button onClick={openAddModal} className="btn-primary">
                    + {t('shipping.addMethod')}
                </button>
            </div>

            {error && (
                <div className="mb-4 p-3 bg-red-100 border border-red-400 text-red-700 rounded-lg">
                    {error}
                </div>
            )}
            {success && (
                <div className="mb-4 p-3 bg-green-100 border border-green-400 text-green-700 rounded-lg">
                    {success}
                    <button onClick={() => setSuccess('')} className="ms-3" aria-label={t('common.close')}>x</button>
                </div>
            )}

            <div className="card">
                {loading ? (
                    <div className="flex justify-center py-12">
                        <div className="animate-spin h-8 w-8 border-4 border-primary-600 border-t-transparent rounded-full"></div>
                    </div>
                ) : methods.length === 0 ? (
                    <div className="text-center py-12 text-gray-500">
                        <p className="text-lg font-semibold">{t('shipping.shipping')}</p>
                        <p className="mt-4">{t('shipping.noMethods')}</p>
                    </div>
                ) : (
                    <div className="overflow-x-auto">
                        <table className="min-w-full divide-y divide-gray-200">
                            <thead className="bg-gray-50">
                                <tr>
                                    <th className="px-6 py-3 text-start text-xs font-medium text-gray-500 uppercase">{t('common.name')}</th>
                                    <th className="px-6 py-3 text-start text-xs font-medium text-gray-500 uppercase">{t('shipping.baseRate')}</th>
                                    <th className="px-6 py-3 text-start text-xs font-medium text-gray-500 uppercase">{t('shipping.perKgRate')}</th>
                                    <th className="px-6 py-3 text-start text-xs font-medium text-gray-500 uppercase">{t('shipping.freeThreshold')}</th>
                                    <th className="px-6 py-3 text-start text-xs font-medium text-gray-500 uppercase">{t('shipping.deliveryDays')}</th>
                                    <th className="px-6 py-3 text-start text-xs font-medium text-gray-500 uppercase">{t('shippingAdministration.methods.fulfillmentWarehouse')}</th>
                                    <th className="px-6 py-3 text-center text-xs font-medium text-gray-500 uppercase">{t('common.status')}</th>
                                    <th className="px-6 py-3 text-center text-xs font-medium text-gray-500 uppercase">{t('common.actions')}</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-gray-200">
                                {methods.map((method) => (
                                    <tr key={method.id} className="hover:bg-gray-50">
                                        <td className="px-6 py-4">
                                            <div className="font-medium text-gray-900">
                                                {isRTL && method.nameAr ? method.nameAr : method.name}
                                            </div>
                                            <div className="text-sm text-gray-500">{resolveCarrierDisplayName(method)}</div>
                                        </td>
                                        <td className="px-6 py-4 text-gray-900">{formatCurrency(method.baseRate)}</td>
                                        <td className="px-6 py-4 text-gray-900">{formatCurrency(method.perKgRate)}/kg</td>
                                        <td className="px-6 py-4 text-gray-900">
                                            {method.freeShippingThreshold
                                                ? `${formatCurrency(method.freeShippingThreshold)}+`
                                                : '-'}
                                        </td>
                                        <td className="px-6 py-4 text-gray-900">
                                            {method.estimatedDeliveryDaysMin}-{method.estimatedDeliveryDaysMax} {t('shipping.days')}
                                        </td>
                                        <td className="px-6 py-4 text-gray-900">
                                            {method.fulfillmentWarehouseId ? (
                                                resolveWarehouseDisplayName(method.fulfillmentWarehouseId)
                                            ) : (
                                                <span className="text-amber-700">{t('shippingAdministration.methods.notConfigured')}</span>
                                            )}
                                        </td>
                                        <td className="px-6 py-4 text-center">
                                            <span className={`px-2 py-1 text-xs font-semibold rounded-full ${method.isActive && method.fulfillmentWarehouseId
                                                ? 'bg-green-100 text-green-800'
                                                : method.isActive
                                                    ? 'bg-amber-100 text-amber-800'
                                                    : 'bg-red-100 text-red-800'
                                                }`}>
                                                {method.isActive
                                                    ? method.fulfillmentWarehouseId
                                                        ? t('common.active')
                                                        : t('shippingAdministration.methods.activeWarehouseMissing')
                                                    : t('common.inactive')}
                                            </span>
                                        </td>
                                        <td className="px-6 py-4 text-center">
                                            <button
                                                onClick={() => handleEdit(method)}
                                                className="me-3 text-primary-600 hover:text-primary-900"
                                            >
                                                {t('common.edit')}
                                            </button>
                                            <button
                                                onClick={() => handleDelete(method.id)}
                                                className="text-red-600 hover:text-red-900"
                                            >
                                                {t('common.delete')}
                                            </button>
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                )}
            </div>

            {showModal && (
                <div className="fixed inset-0 z-50 flex items-center justify-center bg-black bg-opacity-50 p-4" role="presentation">
                    <div className="max-h-[90vh] w-full max-w-2xl overflow-y-auto rounded-xl bg-white shadow-2xl" role="dialog" aria-modal="true" aria-labelledby="shipping-method-dialog-title">
                        <div className="p-6 border-b">
                            <h2 id="shipping-method-dialog-title" className="text-xl font-bold">
                                {editingMethod ? t('shipping.editMethod') : t('shipping.addMethod')}
                            </h2>
                        </div>

                        <form onSubmit={handleSubmit} className="p-6 space-y-4">
                            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                                <div>
                                    <label className="label">{t('shipping.nameEn')} *</label>
                                    <input
                                        type="text"
                                        value={formData.name}
                                        onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                                        className="input"
                                        required
                                    />
                                </div>
                                <div>
                                    <label className="label">{t('shipping.nameAr')}</label>
                                    <input
                                        type="text"
                                        value={formData.nameAr}
                                        onChange={(e) => setFormData({ ...formData, nameAr: e.target.value })}
                                        className="input"
                                        dir="rtl"
                                    />
                                </div>
                            </div>

                            <div>
                                <label className="label">{t('common.description')}</label>
                                <textarea
                                    value={formData.description}
                                    onChange={(e) => setFormData({ ...formData, description: e.target.value })}
                                    className="input"
                                    rows="2"
                                />
                            </div>

                            <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                                <div>
                                    <label className="label">{t('shipping.baseRate')} ({settings?.currencySymbol || '$'})</label>
                                    <input
                                        type="number"
                                        step="0.01"
                                        value={formData.baseRate}
                                        onChange={(e) => setFormData({ ...formData, baseRate: e.target.value })}
                                        className="input"
                                    />
                                </div>
                                <div>
                                    <label className="label">{t('shipping.perKgRate')} ({settings?.currencySymbol || '$'})</label>
                                    <input
                                        type="number"
                                        step="0.01"
                                        value={formData.perKgRate}
                                        onChange={(e) => setFormData({ ...formData, perKgRate: e.target.value })}
                                        className="input"
                                    />
                                </div>
                                <div>
                                    <label className="label">{t('shipping.freeThreshold')} ({settings?.currencySymbol || '$'})</label>
                                    <input
                                        type="number"
                                        step="0.01"
                                        value={formData.freeShippingThreshold}
                                        onChange={(e) => setFormData({ ...formData, freeShippingThreshold: e.target.value })}
                                        className="input"
                                        placeholder={t('common.optional')}
                                    />
                                </div>
                            </div>

                            <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                                <div>
                                    <label className="label">{t('shipping.minDays')}</label>
                                    <input
                                        type="number"
                                        value={formData.estimatedDeliveryDaysMin}
                                        onChange={(e) => setFormData({ ...formData, estimatedDeliveryDaysMin: e.target.value })}
                                        className="input"
                                    />
                                </div>
                                <div>
                                    <label className="label">{t('shipping.maxDays')}</label>
                                    <input
                                        type="number"
                                        value={formData.estimatedDeliveryDaysMax}
                                        onChange={(e) => setFormData({ ...formData, estimatedDeliveryDaysMax: e.target.value })}
                                        className="input"
                                    />
                                </div>
                                <div>
                                    <label className="label">{t('shipping.sortOrder')}</label>
                                    <input
                                        type="number"
                                        value={formData.sortOrder}
                                        onChange={(e) => setFormData({ ...formData, sortOrder: e.target.value })}
                                        className="input"
                                    />
                                </div>
                            </div>

                            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                                <div>
                                    <label className="label">{t('shipping.carrier')}</label>
                                    {carriersLoading ? (
                                        <div className="input flex items-center text-gray-500" role="status">{t('shippingAdministration.methods.loadingCarriers')}</div>
                                    ) : carrierLoadError ? (
                                        <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
                                            {carrierLoadError}
                                        </div>
                                    ) : (
                                        <>
                                            <select
                                                value={formData.carrierCode}
                                                onChange={(e) => updateCarrierSelection(e.target.value)}
                                                className="input"
                                                required
                                            >
                                                {unsupportedLegacyCarrierCode && (
                                                    <option value={unsupportedLegacyCarrierCode} disabled>
                                                        {t('shippingAdministration.methods.unsupportedLegacyCarrier', { carrierCode: unsupportedLegacyCarrierCode })}
                                                    </option>
                                                )}
                                                {supportedCarriers.map((carrier) => (
                                                    <option key={carrier.carrierCode} value={carrier.carrierCode}>
                                                        {carrier.displayName}
                                                    </option>
                                                ))}
                                            </select>
                                            {unsupportedLegacyCarrierCode && (
                                                <p className="mt-1 text-xs text-amber-700">
                                                    {t('shippingAdministration.methods.unsupportedLegacyHelp')}
                                                </p>
                                            )}
                                        </>
                                    )}
                                </div>
                                <div>
                                    <label className="label">{t('shipping.trackingUrl')}</label>
                                    <input
                                        type="text"
                                        value={formData.trackingUrlTemplate}
                                        onChange={(e) => setFormData({ ...formData, trackingUrlTemplate: e.target.value })}
                                        className="input"
                                        placeholder="https://track.example/{tracking_number}"
                                    />
                                </div>
                            </div>

                            <div>
                                <label className="label">{t('shipping.serviceLevel', 'Service Level')}</label>
                                {selectedCarrierServiceLevels.length > 0 ? (
                                    <select
                                        value={formData.serviceLevel}
                                        onChange={(e) => setFormData({ ...formData, serviceLevel: e.target.value })}
                                        className="input"
                                    >
                                        {selectedCarrierServiceLevels.map((level) => (
                                            <option key={level.code} value={level.code}>
                                                {level.name} ({level.typicalDaysMin}-{level.typicalDaysMax} {t('shipping.days')})
                                            </option>
                                        ))}
                                    </select>
                                ) : (
                                    <div className="input flex items-center text-gray-500">
                                        {selectedCarrier
                                            ? t('shippingAdministration.methods.standardServiceFallback')
                                            : t('shippingAdministration.methods.selectCarrierFirst')}
                                    </div>
                                )}
                            </div>

                            <div>
                                <label className="label">{t('shippingAdministration.methods.fulfillmentWarehouse')}</label>
                                <select
                                    value={formData.fulfillmentWarehouseId}
                                    onChange={(e) => setFormData({ ...formData, fulfillmentWarehouseId: e.target.value })}
                                    className="input"
                                >
                                    <option value="">{t('shippingAdministration.methods.notConfigured')}</option>
                                    {activeWarehouses.map((warehouse) => (
                                        <option key={warehouse.id} value={warehouse.id}>
                                            {warehouse.code ? `${warehouse.name} (${warehouse.code})` : warehouse.name}
                                        </option>
                                    ))}
                                </select>
                                {editingMethod && !formData.fulfillmentWarehouseId && (
                                    <p className="mt-1 text-xs text-amber-700">
                                        {t('shippingAdministration.methods.legacyWarehouseHelp')}
                                    </p>
                                )}
                                {formData.isActive && !formData.fulfillmentWarehouseId && (
                                    <p className="mt-1 text-xs text-red-700">
                                        {t('shippingAdministration.methods.activeWarehouseHelp')}
                                    </p>
                                )}
                            </div>

                            <label className="flex items-center gap-2">
                                <input
                                    type="checkbox"
                                    checked={formData.isActive}
                                    onChange={(e) => setFormData({ ...formData, isActive: e.target.checked })}
                                    className="rounded"
                                />
                                <span>{t('common.active')}</span>
                            </label>

                            <div className="flex gap-3 pt-4">
                                <button
                                    type="button"
                                    onClick={() => setShowModal(false)}
                                    className="flex-1 btn-secondary"
                                >
                                    {t('common.cancel')}
                                </button>
                                <button
                                    type="submit"
                                    disabled={carriersLoading || !!carrierLoadError || !selectedCarrier}
                                    className="flex-1 btn-primary"
                                >
                                    {editingMethod ? t('common.update') : t('common.save')}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}
        </div>
    )
}
