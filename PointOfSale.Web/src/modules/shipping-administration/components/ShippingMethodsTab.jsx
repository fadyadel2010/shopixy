import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useConfirmDialog } from '../../../common/confirm/ConfirmDialogProvider'
import api from '../../../services/api'
import { useSettings } from '../../../contexts/SettingsContext'

const GUID_REGEX = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i

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
            trackingUrlTemplate: optionalText(method.trackingUrlTemplate, 'tracking URL template'),
            availableCountries: optionalText(method.availableCountries, 'zone assignment'),
        }
    })
}

export default function ShippingMethodsTab() {
    const { t, i18n } = useTranslation()
    const { confirm } = useConfirmDialog()
    const { formatCurrency, settings } = useSettings()
    const isRTL = i18n.language === 'ar'
    const [methods, setMethods] = useState([])
    const [loading, setLoading] = useState(true)
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
        carrierName: '',
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

    useEffect(() => {
        fetchMethods()
    }, [])

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
            carrierName: '',
            trackingUrlTemplate: '',
        })
    }

    const handleSubmit = async (e) => {
        e.preventDefault()
        setError('')

        try {
            const data = {
                ...formData,
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
            carrierName: method.carrierName || '',
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
            <div className="flex justify-between items-center mb-6">
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
                    <button onClick={() => setSuccess('')} className="float-right">x</button>
                </div>
            )}

            <div className="card">
                {loading ? (
                    <div className="flex justify-center py-12">
                        <div className="animate-spin h-8 w-8 border-4 border-primary-600 border-t-transparent rounded-full"></div>
                    </div>
                ) : methods.length === 0 ? (
                    <div className="text-center py-12 text-gray-500">
                        <p className="text-4xl">Shipping</p>
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
                                            {method.carrierName && (
                                                <div className="text-sm text-gray-500">{method.carrierName}</div>
                                            )}
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
                                        <td className="px-6 py-4 text-center">
                                            <span className={`px-2 py-1 text-xs font-semibold rounded-full ${method.isActive
                                                ? 'bg-green-100 text-green-800'
                                                : 'bg-red-100 text-red-800'
                                                }`}>
                                                {method.isActive ? t('common.active') : t('common.inactive')}
                                            </span>
                                        </td>
                                        <td className="px-6 py-4 text-center">
                                            <button
                                                onClick={() => handleEdit(method)}
                                                className="text-primary-600 hover:text-primary-900 mr-3"
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
                <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50 p-4">
                    <div className="bg-white rounded-xl shadow-2xl w-full max-w-2xl max-h-[90vh] overflow-y-auto">
                        <div className="p-6 border-b">
                            <h2 className="text-xl font-bold">
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
                                    <input
                                        type="text"
                                        value={formData.carrierName}
                                        onChange={(e) => setFormData({ ...formData, carrierName: e.target.value })}
                                        className="input"
                                        placeholder="Carrier display name"
                                    />
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
                                <button type="submit" className="flex-1 btn-primary">
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
