import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useConfirmDialog } from '../../../common/confirm/ConfirmDialogProvider'
import api from '../../../services/api'
import { useSettings } from '../../../contexts/SettingsContext'
import { fetchSupportedCarriers, getCarrierDisplayName } from '../shippingAdministrationApi'
import { getShippingMethodCountryOptions, parseShippingMethodCountryScope, serializeShippingMethodCountryScope } from '../shippingMethodCountryScope'

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

function optionalWarehouseId(value) {
    if (value === null || value === undefined) return null
    if (typeof value !== 'string' || !GUID_REGEX.test(value)) {
        throw new Error('Shipping method read authority rejected malformed fulfillment warehouse identity.')
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
        parseShippingMethodCountryScope(method.availableCountries)

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
            fulfillmentWarehouseId: optionalWarehouseId(method.fulfillmentWarehouseId),
            trackingUrlTemplate: optionalText(method.trackingUrlTemplate, 'tracking URL template'),
            availableCountries: method.availableCountries ?? null,
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
    return carrier?.serviceLevels?.[0]?.code || ''
}

function isServiceLevelAllowed(carrier, serviceLevel) {
    if (!carrier?.serviceLevels?.length) {
        return false
    }

    return carrier.serviceLevels.some((level) => level.code === serviceLevel)
}

function isCarrierReadyForActiveMethod(carrier, carrierAccounts) {
    if (!carrier) return false
    if (['Unavailable', 'NotCertified'].includes(carrier.productionCapability)) return false
    if (carrier.readinessRequired !== true) return true
    return carrierAccounts.some((account) => (
        account.carrierCode === carrier.carrierCode
        && account.integrationReadiness?.canExecute === true
    ))
}

export default function ShippingMethodsTab({ warehouses = [], carrierAccounts = [] }) {
    const { t, i18n } = useTranslation()
    const { confirm } = useConfirmDialog()
    const { formatCurrency, settings } = useSettings()
    const isRTL = i18n.language === 'ar'
    const countryOptions = useMemo(() => getShippingMethodCountryOptions(isRTL ? 'ar' : 'en'), [isRTL])
    const countryLabelByCode = useMemo(() => new Map(countryOptions.map(({ code, label }) => [code, label])), [countryOptions])
    const countryText = (key, en, ar) => t(`shippingAdministration.methods.countries.${key}`, { defaultValue: isRTL ? ar : en })
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
        countryScope: null,
    })
    const [countryScopeError, setCountryScopeError] = useState('')
    const [error, setError] = useState('')
    const [success, setSuccess] = useState('')
    const [busy, setBusy] = useState(false)
    const pending = useRef(false)
    const mounted = useRef(true)
    const readId = useRef(0)

    const fetchMethods = async () => {
        const id = ++readId.current
        try {
            setLoading(true)
            setError('')
            const response = await api.get('/shippingmethods?activeOnly=false', {
                headers: {
                    'Cache-Control': 'no-store',
                    Pragma: 'no-cache',
                },
            })
            if (!mounted.current || readId.current !== id) return false
            setMethods(admitAdminShippingMethods(response.data))
            return true
        } catch (err) {
            if (!mounted.current || readId.current !== id) return false
            setMethods([])
            setError(err?.code === 'INVALID_SHIPPING_COUNTRY_SCOPE'
                ? countryText('invalidSaved', 'Saved shipping country scope is invalid. Methods could not be loaded safely.', 'نطاق دول الشحن المحفوظ غير صالح. تعذر تحميل طرق الشحن بأمان.')
                : t('common.failedToLoad'))
            return false
        } finally {
            if (mounted.current && readId.current === id) setLoading(false)
        }
    }

    const fetchCarriers = async () => {
        try {
            setCarriersLoading(true)
            setCarrierLoadError('')
            const carriers = await fetchSupportedCarriers()
            if (mounted.current) setSupportedCarriers(carriers)
        } catch (err) {
            if (!mounted.current) return
            setSupportedCarriers([])
            setCarrierLoadError(err.message || t('shippingAdministration.methods.carrierLoadFailed'))
        } finally {
            if (mounted.current) setCarriersLoading(false)
        }
    }

    useEffect(() => {
        mounted.current = true
        fetchMethods()
        fetchCarriers()
        return () => { mounted.current = false; readId.current += 1 }
    }, [])

    const runMutation = async (operation, message, accepted) => {
        if (pending.current || !mounted.current) return
        pending.current = true
        setBusy(true)
        setSuccess('')
        setError('')
        readId.current += 1
        try {
            await operation()
            if (!mounted.current) return
            accepted?.()
            if (await fetchMethods()) setSuccess(message)
            else if (mounted.current) setError(t('phase5.savedReloadFailed', 'Saved, but fresh data could not be reloaded. Retry the read before making another change.'))
        } catch (err) {
            if (mounted.current) setError(err.response?.data?.message || t('common.error'))
        } finally {
            pending.current = false
            if (mounted.current) setBusy(false)
        }
    }

    const selectedCarrier = findCarrier(supportedCarriers, formData.carrierCode)
    const selectedCarrierReady = isCarrierReadyForActiveMethod(selectedCarrier, carrierAccounts)
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
        setCountryScopeError('')
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
            countryScope: null,
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
        setCountryScopeError('')

        let availableCountries
        try {
            availableCountries = serializeShippingMethodCountryScope(formData.countryScope)
            if (editingMethod && availableCountries === serializeShippingMethodCountryScope(parseShippingMethodCountryScope(editingMethod.availableCountries))) {
                availableCountries = editingMethod.availableCountries
            }
        } catch {
            setCountryScopeError(countryText('required', 'Select at least one valid shipping country, or explicitly choose all countries.', 'اختر دولة شحن صالحة واحدة على الأقل، أو اختر جميع الدول صراحةً.'))
            return
        }

        try {
            const normalizedCarrierCode = normalizeCarrierCode(formData.carrierCode)
            const carrier = findCarrier(supportedCarriers, normalizedCarrierCode)
            if (!carrier) {
                setError(t('shippingAdministration.methods.selectSupportedCarrier'))
                return
            }

            if (formData.isActive && !isCarrierReadyForActiveMethod(carrier, carrierAccounts)) {
                setError(t('shippingAdministration.methods.carrierNotReady'))
                return
            }

            if (!isServiceLevelAllowed(carrier, formData.serviceLevel)) {
                setError(t('shipping.closure.serviceUnavailable'))
                return
            }
            const normalizedServiceLevel = formData.serviceLevel

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
                availableCountries,
                baseRate: parseFloat(formData.baseRate) || 0,
                perKgRate: parseFloat(formData.perKgRate) || 0,
                freeShippingThreshold: formData.freeShippingThreshold ? parseFloat(formData.freeShippingThreshold) : null,
                estimatedDeliveryDaysMin: formData.estimatedDeliveryDaysMin === '' ? 1 : Number(formData.estimatedDeliveryDaysMin),
                estimatedDeliveryDaysMax: formData.estimatedDeliveryDaysMax === '' ? 5 : Number(formData.estimatedDeliveryDaysMax),
                sortOrder: parseInt(formData.sortOrder) || 0,
            }

            await runMutation(() => editingMethod
                ? api.put(`/shippingmethods/${editingMethod.id}`, data, ...(editingMethod.revision ? [{ headers: { 'If-Match': editingMethod.revision } }] : []))
                : api.post('/shippingmethods', data), editingMethod ? t('common.updatedSuccessfully') : t('shipping.created'), () => {
                setShowModal(false)
                resetForm()
            })
        } catch (err) {
            setError(err.response?.data?.message || t('common.error'))
        }
    }

    const handleEdit = (method) => {
        const normalizedCarrierCode = normalizeCarrierCode(method.carrierCode)
        const carrier = findCarrier(supportedCarriers, normalizedCarrierCode)
        const serviceLevel = method.serviceLevel || getDefaultServiceLevel(carrier)
        setEditingMethod(method)
        setCountryScopeError('')
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
            serviceLevel,
            fulfillmentWarehouseId: method.fulfillmentWarehouseId || '',
            trackingUrlTemplate: method.trackingUrlTemplate || '',
            countryScope: parseShippingMethodCountryScope(method.availableCountries),
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

        if (!confirmed || !mounted.current) return
        const method = methods.find(item => item.id === id)
        await runMutation(() => api.delete(`/shippingmethods/${id}`, ...(method?.revision ? [{ headers: { 'If-Match': method.revision } }] : [])), t('common.deletedSuccessfully'))
    }

    const openAddModal = () => {
        resetForm()
        setShowModal(true)
    }

    return (
        <fieldset disabled={busy} className="min-w-0">
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
                <div className="mb-4 p-3 bg-red-100 border border-red-400 text-red-700 rounded-lg" role="alert">
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
                ) : error && methods.length === 0 ? (
                    <div className="p-6 text-center">
                        <p>{t('common.failedToLoad')}</p>
                        <button type="button" onClick={fetchMethods} className="btn-secondary mt-3">{t('common.retry')}</button>
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
                                    <th className="px-6 py-3 text-start text-xs font-medium text-gray-500 uppercase">{countryText('title', 'Shipping countries', 'دول الشحن')}</th>
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
                                            {method.freeShippingThreshold != null
                                                ? `${formatCurrency(method.freeShippingThreshold)}+`
                                                : '-'}
                                        </td>
                                        <td className="px-6 py-4 text-gray-900">
                                            {method.estimatedDeliveryDaysMin}-{method.estimatedDeliveryDaysMax} {t('shipping.days')}
                                        </td>
                                        <td className="px-6 py-4 text-gray-900">
                                            {parseShippingMethodCountryScope(method.availableCountries)?.map((code) => countryLabelByCode.get(code)).join(', ')
                                                ?? countryText('all', 'All countries', 'جميع الدول')}
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
                                        aria-label={t('shipping.nameEn')}
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
                                        aria-label={t('shipping.baseRate')}
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

                            <fieldset className="rounded-lg border border-gray-200 p-4 space-y-3">
                                <legend className="px-1 font-medium">{countryText('title', 'Shipping countries', 'دول الشحن')}</legend>
                                <p id="shipping-country-scope-help" className="text-sm text-gray-600">
                                    {countryText('help', 'These rates are shared by all selected countries. Country scope is separate from carrier credentials and does not configure city rates or guarantee carrier coverage.', 'تُطبق هذه الأسعار على جميع الدول المختارة. نطاق الدول منفصل عن بيانات ربط شركة الشحن، ولا يحدد أسعار المدن أو يضمن تغطية شركة الشحن.')}
                                </p>
                                <label className="label" htmlFor="shipping-country-scope">{countryText('scope', 'Country scope', 'نطاق الدول')}</label>
                                <select
                                    id="shipping-country-scope"
                                    aria-describedby="shipping-country-scope-help"
                                    value={formData.countryScope === null ? 'all' : 'selected'}
                                    onChange={(e) => {
                                        setCountryScopeError('')
                                        setFormData({ ...formData, countryScope: e.target.value === 'all' ? null : [] })
                                    }}
                                    className="input"
                                >
                                    <option value="all">{countryText('all', 'All countries', 'جميع الدول')}</option>
                                    <option value="selected">{countryText('selected', 'Selected countries only', 'الدول المختارة فقط')}</option>
                                </select>
                                {formData.countryScope !== null && (
                                    <>
                                        <label className="label" htmlFor="shipping-country-codes">{countryText('select', 'Select shipping countries', 'اختر دول الشحن')}</label>
                                        <select
                                            id="shipping-country-codes"
                                            multiple
                                            size={6}
                                            value={formData.countryScope}
                                            aria-invalid={!!countryScopeError}
                                            aria-describedby="shipping-country-selection-help shipping-country-scope-error"
                                            onChange={(e) => {
                                                setCountryScopeError('')
                                                setFormData({ ...formData, countryScope: Array.from(e.target.selectedOptions, (option) => option.value) })
                                            }}
                                            className="input"
                                        >
                                            {countryOptions.map(({ code, label }) => <option key={code} value={code}>{label}</option>)}
                                        </select>
                                        <p id="shipping-country-selection-help" className="text-xs text-gray-600">
                                            {countryText('selectionHelp', 'Select one or more countries. Use Ctrl/Cmd to select multiple countries on a desktop.', 'اختر دولة واحدة أو أكثر. استخدم Ctrl/Cmd لاختيار عدة دول على الكمبيوتر.')}
                                        </p>
                                    </>
                                )}
                                <div id="shipping-country-scope-error">
                                    {countryScopeError && <p role="alert" className="text-sm text-red-700">{countryScopeError}</p>}
                                </div>
                            </fieldset>

                            <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                                <div>
                                    <label className="label" htmlFor="shipping-min-days">{t('shipping.minDays')}</label>
                                    <input
                                        id="shipping-min-days"
                                        min="0"
                                        type="number"
                                        value={formData.estimatedDeliveryDaysMin}
                                        onChange={(e) => setFormData({ ...formData, estimatedDeliveryDaysMin: e.target.value })}
                                        className="input"
                                    />
                                </div>
                                <div>
                                    <label className="label" htmlFor="shipping-max-days">{t('shipping.maxDays')}</label>
                                    <input
                                        id="shipping-max-days"
                                        min={formData.estimatedDeliveryDaysMin || '0'}
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
                                                aria-label={t('shipping.closure.carrier')}
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
                                                {supportedCarriers.map((carrier) => {
                                                    const ready = isCarrierReadyForActiveMethod(carrier, carrierAccounts)
                                                    return <option key={carrier.carrierCode} value={carrier.carrierCode} disabled={formData.isActive && !ready}>
                                                        {ready ? carrier.displayName : `${carrier.displayName} — ${t('shippingAdministration.methods.carrierNotReadyShort')}`}
                                                    </option>
                                                })}
                                            </select>
                                            {unsupportedLegacyCarrierCode && (
                                                <p className="mt-1 text-xs text-amber-700">
                                                    {t('shippingAdministration.methods.unsupportedLegacyHelp')}
                                                </p>
                                            )}
                                            {selectedCarrier && !selectedCarrierReady && (
                                                <p className="mt-1 text-xs text-amber-700">
                                                    {t('shippingAdministration.methods.carrierNotReady')}
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
                                        aria-label={t('shipping.serviceLevel', 'Service Level')}
                                        value={formData.serviceLevel}
                                        onChange={(e) => setFormData({ ...formData, serviceLevel: e.target.value })}
                                        className="input"
                                    >
                                        {!isServiceLevelAllowed(selectedCarrier, formData.serviceLevel) && <option value={formData.serviceLevel} disabled>{formData.serviceLevel || t('shippingAdministration.methods.selectCarrierFirst')}</option>}
                                        {selectedCarrierServiceLevels.map((level) => (
                                            <option key={level.code} value={level.code}>
                                                {level.name} ({level.typicalDaysMin}-{level.typicalDaysMax} {t('shipping.days')})
                                            </option>
                                        ))}
                                    </select>
                                ) : (
                                    <div className="input flex items-center text-gray-500">
                                        {selectedCarrier
                                            ? t('shipping.closure.serviceUnavailable')
                                            : t('shippingAdministration.methods.selectCarrierFirst')}
                                    </div>
                                )}
                            </div>

                            <div>
                                <label className="label">{t('shippingAdministration.methods.fulfillmentWarehouse')}</label>
                                <select
                                    aria-label={t('shippingAdministration.methods.fulfillmentWarehouse')}
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
                                    disabled={carriersLoading || !!carrierLoadError || !selectedCarrier || (formData.isActive && !selectedCarrierReady) || !isServiceLevelAllowed(selectedCarrier, formData.serviceLevel)}
                                    className="flex-1 btn-primary"
                                >
                                    {editingMethod ? t('common.update') : t('common.save')}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}
        </fieldset>
    )
}
