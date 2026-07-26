import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import api from '../services/api'
import { useSettings } from '../contexts/SettingsContext'

const EMPTY_FORM = {
    displayName: '',
    displayNameAr: '',
    description: '',
    descriptionAr: '',
    sortOrder: '0',
    fee: '0',
    feeType: 'fixed',
    minOrderAmount: '',
    maxOrderAmount: '',
    isEnabled: false
}

const EMPTY_CONFIG_FORM = {
    isSandbox: true,
    configuration: {},
    reset: false,
    confirmReset: false
}

function extractValidationErrors(payload) {
    if (!Array.isArray(payload?.validationErrors)) {
        return []
    }

    return payload.validationErrors.filter((value) => typeof value === 'string' && value.trim())
}

function sanitizeConfiguration(configuration) {
    return Object.entries(configuration || {}).reduce((result, [key, value]) => {
        const trimmedValue = typeof value === 'string' ? value.trim() : ''
        if (trimmedValue) {
            result[key] = trimmedValue
        }

        return result
    }, {})
}

function normalizeMethod(method) {
    const requiresConfiguration = Boolean(method.requiresConfiguration)
    const configurationErrors = Array.isArray(method.configurationErrors)
        ? method.configurationErrors.filter((value) => typeof value === 'string' && value.trim())
        : []

    return {
        ...method,
        requiresConfiguration: Boolean(requiresConfiguration),
        isConfigurationValid: requiresConfiguration ? method.isConfigurationValid === true : true,
        configurationErrors,
        isEnabled: Boolean(method.isEnabled),
        isSandbox: method.isSandbox ?? true,
        sortOrder: method.sortOrder ?? 999,
        fee: method.fee ?? 0,
        feeType: method.feeType || 'fixed'
    }
}

function mergeMethods(methods) {
    return methods.map(normalizeMethod).sort((a, b) => (a.sortOrder || 999) - (b.sortOrder || 999))
}

export default function PaymentMethods() {
    const { t, i18n } = useTranslation()
    const { formatCurrency, settings } = useSettings()
    const isRTL = i18n.language === 'ar'

    const [methods, setMethods] = useState([])
    const [loading, setLoading] = useState(true)
    const [showModal, setShowModal] = useState(false)
    const [showConfigModal, setShowConfigModal] = useState(false)
    const [editingMethod, setEditingMethod] = useState(null)
    const [formData, setFormData] = useState(EMPTY_FORM)
    const [configData, setConfigData] = useState(EMPTY_CONFIG_FORM)
    const [error, setError] = useState('')
    const [errorDetails, setErrorDetails] = useState([])
    const [success, setSuccess] = useState('')

    useEffect(() => {
        fetchMethods()
    }, [])

    const clearFeedback = () => {
        setError('')
        setErrorDetails([])
        setSuccess('')
    }

    const applyApiError = (err, fallbackMessage) => {
        const payload = err?.response?.data
        setError(payload?.message || fallbackMessage)
        setErrorDetails(extractValidationErrors(payload))
    }

    const fetchMethods = async () => {
        try {
            setLoading(true)
            const response = await api.get('/admin/payment-methods')
            setMethods(mergeMethods(response.data || []))
        } catch (err) {
            console.error('Failed to fetch payment methods:', err)
            setMethods([])
            applyApiError(err, t('common.error'))
        } finally {
            setLoading(false)
        }
    }

    const closeEditModal = () => {
        setShowModal(false)
        setEditingMethod(null)
        setFormData(EMPTY_FORM)
    }

    const closeConfigModal = () => {
        setShowConfigModal(false)
        setEditingMethod(null)
        setConfigData(EMPTY_CONFIG_FORM)
    }

    const handleSubmit = async (e) => {
        e.preventDefault()
        clearFeedback()

        try {
            const data = {
                ...formData,
                sortOrder: parseInt(formData.sortOrder, 10) || 0,
                fee: parseFloat(formData.fee) || 0,
                minOrderAmount: formData.minOrderAmount ? parseFloat(formData.minOrderAmount) : null,
                maxOrderAmount: formData.maxOrderAmount ? parseFloat(formData.maxOrderAmount) : null
            }

            await api.put(`/admin/payment-methods/${editingMethod.code}`, data)
            setSuccess(t('common.updatedSuccessfully'))
            closeEditModal()
            await fetchMethods()
        } catch (err) {
            applyApiError(err, t('common.error'))
        }
    }

    const handleConfigSubmit = async (e) => {
        e.preventDefault()
        clearFeedback()

        const configuration = sanitizeConfiguration(configData.configuration)
        if (configData.reset) {
            if (Object.keys(configuration).length > 0) {
                setError('Configuration reset cannot include new provider values.')
                setErrorDetails(['Clear all provider fields before submitting an explicit reset.'])
                return
            }

            if (!configData.confirmReset) {
                setError('Configuration reset requires explicit confirmation.')
                setErrorDetails(['Enable the confirmation checkbox before clearing stored payment credentials.'])
                return
            }
        } else if (Object.keys(configuration).length === 0) {
            setError('Empty configuration payloads do not overwrite existing secrets.')
            setErrorDetails(['Provide one or more provider fields, or use reset plus confirm reset to clear configuration intentionally.'])
            return
        }

        try {
            const payload = {
                isSandbox: configData.isSandbox,
                configuration,
                reset: configData.reset,
                confirmReset: configData.confirmReset
            }

            await api.put(`/admin/payment-methods/${editingMethod.code}/configure`, payload)
            setSuccess(configData.reset
                ? 'Payment configuration was reset safely and the method was disabled.'
                : t('common.updatedSuccessfully'))
            closeConfigModal()
            await fetchMethods()
        } catch (err) {
            applyApiError(err, t('common.error'))
        }
    }

    const handleEdit = (method) => {
        clearFeedback()
        setEditingMethod(method)
        setFormData({
            displayName: method.displayName || '',
            displayNameAr: method.displayNameAr || '',
            description: method.description || '',
            descriptionAr: method.descriptionAr || '',
            sortOrder: (method.sortOrder || 0).toString(),
            fee: (method.fee || 0).toString(),
            feeType: method.feeType || 'fixed',
            minOrderAmount: method.minOrderAmount?.toString() || '',
            maxOrderAmount: method.maxOrderAmount?.toString() || '',
            isEnabled: method.isEnabled || false
        })
        setShowModal(true)
    }

    const handleConfig = (method) => {
        clearFeedback()
        setEditingMethod(method)
        setConfigData({
            isSandbox: method.isSandbox ?? true,
            configuration: {},
            reset: false,
            confirmReset: false
        })
        setShowConfigModal(true)
    }

    const handleToggle = async (method) => {
        clearFeedback()

        try {
            if (method.isEnabled) {
                await api.post(`/admin/payment-methods/${method.code}/disable`)
                setSuccess(t('paymentMethods.disabled', { name: method.displayName }))
                await fetchMethods()
                return
            }

            if (method.requiresConfiguration && !method.isConfigurationValid) {
                setError(`${method.displayName} requires valid configuration before it can be enabled.`)
                setErrorDetails(method.configurationErrors.length > 0
                    ? method.configurationErrors
                    : ['Complete the required provider fields, then try enabling the method again.'])
                handleConfig(method)
                return
            }

            await api.post(`/admin/payment-methods/${method.code}/enable`)
            setSuccess(t('paymentMethods.enabled', { name: method.displayName }))
            await fetchMethods()
        } catch (err) {
            applyApiError(err, t('common.error'))
            await fetchMethods()
        }
    }

    const handleResetToggle = (checked) => {
        setConfigData((current) => ({
            ...current,
            reset: checked,
            confirmReset: checked ? current.confirmReset : false,
            configuration: checked ? {} : current.configuration
        }))
    }

    const getConfigFields = (code) => {
        switch (code) {
            case 'STRIPE':
                return [
                    { key: 'publishableKey', label: 'Publishable Key', placeholder: 'pk_live_...' },
                    { key: 'secretKey', label: 'Secret Key', placeholder: 'sk_live_...', sensitive: true },
                    { key: 'webhookSecret', label: 'Webhook Secret', placeholder: 'whsec_...', sensitive: true }
                ]
            case 'PAYMOB':
                return [
                    { key: 'apiKey', label: 'API Key', placeholder: 'Your Paymob API key', sensitive: true },
                    { key: 'integrationId', label: 'Integration ID', placeholder: 'Your Paymob integration ID' },
                    { key: 'iframeId', label: 'iFrame ID', placeholder: 'Your Paymob iframe ID' },
                    { key: 'hmacSecret', label: 'HMAC Secret', placeholder: 'Your Paymob HMAC secret', sensitive: true }
                ]
            case 'FAWRY':
                return [
                    { key: 'merchantCode', label: 'Merchant Code', placeholder: 'Your Fawry merchant code' },
                    { key: 'apiKey', label: 'API Key', placeholder: 'Your Fawry API key', sensitive: true },
                    { key: 'webhookSecret', label: 'Webhook Secret', placeholder: 'Your Fawry webhook secret', sensitive: true }
                ]
            case 'MANUALWALLET':
                return [
                    { key: 'walletNumber', label: isRTL ? 'رقم المحفظة' : 'Wallet Number', placeholder: isRTL ? 'مثال: 01012345678' : 'e.g. 01012345678' },
                    { key: 'walletName', label: isRTL ? 'اسم المحفظة' : 'Wallet Name', placeholder: isRTL ? 'مثال: Vodafone Cash' : 'e.g. Vodafone Cash' }
                ]
            default:
                return []
        }
    }

    const formatFee = (method) => {
        if (!method.fee || method.fee === 0) {
            return '-'
        }

        return method.feeType === 'percentage'
            ? `${method.fee}%`
            : formatCurrency(method.fee)
    }

    const getConfigurationBadge = (method) => {
        if (!method.requiresConfiguration) {
            return <span className="text-gray-400">-</span>
        }

        const className = method.isConfigurationValid
            ? 'bg-green-100 text-green-800'
            : 'bg-red-100 text-red-800'

        return (
            <span className={`px-2 py-1 text-xs font-semibold rounded-full ${className}`}>
                {method.isConfigurationValid ? 'Valid' : 'Invalid'}
            </span>
        )
    }

    return (
        <div className="p-6">
            <div className="flex justify-between items-center mb-6">
                <div>
                    <h1 className="text-2xl font-bold text-gray-900">{t('paymentMethods.title', 'Payment Methods')}</h1>
                    <p className="text-sm text-gray-600">{t('paymentMethods.subtitle', 'Configure payment methods for your storefront')}</p>
                </div>
            </div>

            {error && (
                <div className="mb-4 rounded-lg border border-red-300 bg-red-50 p-4 text-red-800">
                    <p className="font-medium">{error}</p>
                    {errorDetails.length > 0 && (
                        <ul className="mt-2 list-disc pl-5 text-sm">
                            {errorDetails.map((detail, index) => (
                                <li key={`${detail}-${index}`}>{detail}</li>
                            ))}
                        </ul>
                    )}
                </div>
            )}

            {success && (
                <div className="mb-4 rounded-lg border border-green-300 bg-green-50 p-4 text-green-800">
                    <div className="flex items-start justify-between gap-3">
                        <p className="font-medium">{success}</p>
                        <button type="button" onClick={() => setSuccess('')} className="text-sm font-semibold text-green-800">
                            {t('common.close', 'Close')}
                        </button>
                    </div>
                </div>
            )}

            <div className="card">
                {loading ? (
                    <div className="flex justify-center py-12">
                        <div className="h-8 w-8 animate-spin rounded-full border-4 border-primary-600 border-t-transparent"></div>
                    </div>
                ) : methods.length === 0 ? (
                    <div className="py-12 text-center text-gray-500">
                        <p>{t('paymentMethods.noMethods', 'No payment methods available')}</p>
                        {error && (
                            <button
                                type="button"
                                onClick={fetchMethods}
                                className="mt-4 rounded-lg border border-primary-300 px-4 py-2 text-sm font-medium text-primary-700 hover:bg-primary-50"
                            >
                                {t('common.retry', 'Retry')}
                            </button>
                        )}
                    </div>
                ) : (
                    <div className="overflow-x-auto">
                        <table className="min-w-full divide-y divide-gray-200">
                            <thead className="bg-gray-50">
                                <tr>
                                    <th className="px-6 py-3 text-start text-xs font-medium uppercase text-gray-500">{t('common.name')}</th>
                                    <th className="px-6 py-3 text-start text-xs font-medium uppercase text-gray-500">{t('paymentMethods.type', 'Type')}</th>
                                    <th className="px-6 py-3 text-start text-xs font-medium uppercase text-gray-500">{t('paymentMethods.fee', 'Fee')}</th>
                                    <th className="px-6 py-3 text-center text-xs font-medium uppercase text-gray-500">{t('paymentMethods.configured', 'Configuration')}</th>
                                    <th className="px-6 py-3 text-center text-xs font-medium uppercase text-gray-500">{t('common.status')}</th>
                                    <th className="px-6 py-3 text-center text-xs font-medium uppercase text-gray-500">{t('common.actions')}</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-gray-200">
                                {methods.map((method) => (
                                    <tr key={method.code} className="hover:bg-gray-50">
                                        <td className="px-6 py-4 align-top">
                                            <div>
                                                <div className="font-medium text-gray-900">
                                                    {isRTL && method.displayNameAr ? method.displayNameAr : method.displayName}
                                                </div>
                                                <div className="text-sm text-gray-500">{method.code}</div>
                                                {method.requiresConfiguration && !method.isConfigurationValid && method.configurationErrors.length > 0 && (
                                                    <p className="mt-1 text-xs text-red-600">
                                                        {method.configurationErrors[0]}
                                                    </p>
                                                )}
                                            </div>
                                        </td>
                                        <td className="px-6 py-4 align-top">
                                            <span className={`rounded-full px-2 py-1 text-xs font-semibold ${method.supportsOnline
                                                ? 'bg-indigo-100 text-indigo-800'
                                                : 'bg-amber-100 text-amber-800'
                                            }`}>
                                                {method.supportsOnline ? t('paymentMethods.online', 'Online') : t('paymentMethods.offline', 'Offline')}
                                            </span>
                                        </td>
                                        <td className="px-6 py-4 align-top text-gray-900">
                                            {formatFee(method)}
                                        </td>
                                        <td className="px-6 py-4 text-center align-top">
                                            {getConfigurationBadge(method)}
                                        </td>
                                        <td className="px-6 py-4 text-center align-top">
                                            <span className={`rounded-full px-2 py-1 text-xs font-semibold ${method.isEnabled
                                                ? 'bg-green-100 text-green-800'
                                                : 'bg-red-100 text-red-800'
                                            }`}>
                                                {method.isEnabled ? t('common.active') : t('common.inactive')}
                                            </span>
                                        </td>
                                        <td className="px-6 py-4 text-center align-top">
                                            <div className="flex flex-wrap justify-center gap-2">
                                                <button
                                                    type="button"
                                                    onClick={() => handleToggle(method)}
                                                    className="rounded-lg border border-gray-300 px-3 py-1 text-sm font-medium text-gray-700 hover:bg-gray-100"
                                                    title={method.isEnabled ? t('common.disable') : t('common.enable')}
                                                >
                                                    {method.isEnabled ? t('common.disable') : t('common.enable')}
                                                </button>
                                                <button
                                                    type="button"
                                                    onClick={() => handleEdit(method)}
                                                    className="rounded-lg border border-primary-300 px-3 py-1 text-sm font-medium text-primary-700 hover:bg-primary-50"
                                                >
                                                    {t('common.edit')}
                                                </button>
                                                {method.requiresConfiguration && (
                                                    <button
                                                        type="button"
                                                        onClick={() => handleConfig(method)}
                                                        className="rounded-lg border border-amber-300 px-3 py-1 text-sm font-medium text-amber-700 hover:bg-amber-50"
                                                        title={t('paymentMethods.configure', 'Configure')}
                                                    >
                                                        {t('paymentMethods.configure', 'Configure')}
                                                    </button>
                                                )}
                                            </div>
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                )}
            </div>

            {showModal && (
                <div className="fixed inset-0 z-50 flex items-center justify-center bg-black bg-opacity-50 p-4">
                    <div className="max-h-[90vh] w-full max-w-2xl overflow-y-auto rounded-xl bg-white shadow-2xl">
                        <div className="border-b p-6">
                            <h2 className="text-xl font-bold">
                                {t('paymentMethods.editMethod', 'Edit Payment Method')}
                            </h2>
                        </div>

                        <form onSubmit={handleSubmit} className="space-y-4 p-6">
                            <div className="grid grid-cols-2 gap-4">
                                <div>
                                    <label className="label">{t('paymentMethods.displayNameEn', 'Display Name (EN)')} *</label>
                                    <input
                                        type="text"
                                        value={formData.displayName}
                                        onChange={(e) => setFormData({ ...formData, displayName: e.target.value })}
                                        className="input"
                                        required
                                    />
                                </div>
                                <div>
                                    <label className="label">{t('paymentMethods.displayNameAr', 'Display Name (AR)')}</label>
                                    <input
                                        type="text"
                                        value={formData.displayNameAr}
                                        onChange={(e) => setFormData({ ...formData, displayNameAr: e.target.value })}
                                        className="input"
                                        dir="rtl"
                                    />
                                </div>
                            </div>

                            <div className="grid grid-cols-2 gap-4">
                                <div>
                                    <label className="label">{t('common.description')} (EN)</label>
                                    <textarea
                                        value={formData.description}
                                        onChange={(e) => setFormData({ ...formData, description: e.target.value })}
                                        className="input"
                                        rows="2"
                                    />
                                </div>
                                <div>
                                    <label className="label">{t('common.description')} (AR)</label>
                                    <textarea
                                        value={formData.descriptionAr}
                                        onChange={(e) => setFormData({ ...formData, descriptionAr: e.target.value })}
                                        className="input"
                                        rows="2"
                                        dir="rtl"
                                    />
                                </div>
                            </div>

                            <div className="grid grid-cols-3 gap-4">
                                <div>
                                    <label className="label">{t('paymentMethods.sortOrder', 'Sort Order')}</label>
                                    <input
                                        type="number"
                                        value={formData.sortOrder}
                                        onChange={(e) => setFormData({ ...formData, sortOrder: e.target.value })}
                                        className="input"
                                        min="0"
                                    />
                                </div>
                                <div>
                                    <label className="label">{t('paymentMethods.fee', 'Fee')}</label>
                                    <input
                                        type="number"
                                        step="0.01"
                                        value={formData.fee}
                                        onChange={(e) => setFormData({ ...formData, fee: e.target.value })}
                                        className="input"
                                        min="0"
                                    />
                                </div>
                                <div>
                                    <label className="label">{t('paymentMethods.feeType', 'Fee Type')}</label>
                                    <select
                                        value={formData.feeType}
                                        onChange={(e) => setFormData({ ...formData, feeType: e.target.value })}
                                        className="input"
                                    >
                                        <option value="fixed">{t('paymentMethods.fixed', 'Fixed')}</option>
                                        <option value="percentage">{t('paymentMethods.percentage', 'Percentage')}</option>
                                    </select>
                                </div>
                            </div>

                            <div className="grid grid-cols-2 gap-4">
                                <div>
                                    <label className="label">{t('paymentMethods.minOrder', 'Min Order Amount')} ({settings?.currencySymbol || '$'})</label>
                                    <input
                                        type="number"
                                        step="0.01"
                                        value={formData.minOrderAmount}
                                        onChange={(e) => setFormData({ ...formData, minOrderAmount: e.target.value })}
                                        className="input"
                                        placeholder={t('common.optional')}
                                    />
                                </div>
                                <div>
                                    <label className="label">{t('paymentMethods.maxOrder', 'Max Order Amount')} ({settings?.currencySymbol || '$'})</label>
                                    <input
                                        type="number"
                                        step="0.01"
                                        value={formData.maxOrderAmount}
                                        onChange={(e) => setFormData({ ...formData, maxOrderAmount: e.target.value })}
                                        className="input"
                                        placeholder={t('common.optional')}
                                    />
                                </div>
                            </div>

                            <div className="flex gap-3 pt-4">
                                <button type="button" onClick={closeEditModal} className="btn-secondary flex-1">
                                    {t('common.cancel')}
                                </button>
                                <button type="submit" className="btn-primary flex-1">
                                    {t('common.update')}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}

            {showConfigModal && editingMethod && (
                <div className="fixed inset-0 z-50 flex items-center justify-center bg-black bg-opacity-50 p-4">
                    <div className="max-h-[90vh] w-full max-w-lg overflow-y-auto rounded-xl bg-white shadow-2xl">
                        <div className="border-b p-6">
                            <h2 className="text-xl font-bold">
                                {t('paymentMethods.configure', 'Configure')} {editingMethod.displayName}
                            </h2>
                            <p className="mt-1 text-sm text-gray-500">
                                Provider secrets are never returned to the browser. Leave a field blank to keep the stored value unchanged.
                            </p>
                        </div>

                        <form onSubmit={handleConfigSubmit} className="space-y-4 p-6">
                            {editingMethod.requiresConfiguration && !editingMethod.isConfigurationValid && editingMethod.configurationErrors.length > 0 && (
                                <div className="rounded-lg border border-red-300 bg-red-50 p-4 text-red-800">
                                    <p className="font-medium">Configuration issues</p>
                                    <ul className="mt-2 list-disc pl-5 text-sm">
                                        {editingMethod.configurationErrors.map((detail, index) => (
                                            <li key={`${detail}-${index}`}>{detail}</li>
                                        ))}
                                    </ul>
                                </div>
                            )}

                            <div className="rounded-lg bg-amber-50 p-4">
                                <div className="flex items-center justify-between gap-4">
                                    <div>
                                        <p className="font-medium text-amber-800">{t('paymentMethods.sandboxMode', 'Sandbox Mode')}</p>
                                        <p className="text-xs text-amber-700">{t('paymentMethods.sandboxDescription', 'Use test credentials for development')}</p>
                                    </div>
                                    <label className="relative inline-flex cursor-pointer items-center">
                                        <input
                                            type="checkbox"
                                            checked={configData.isSandbox}
                                            onChange={(e) => setConfigData({ ...configData, isSandbox: e.target.checked })}
                                            className="sr-only peer"
                                        />
                                        <div className="peer h-6 w-11 rounded-full bg-gray-200 after:absolute after:left-[2px] after:top-[2px] after:h-5 after:w-5 after:rounded-full after:border after:border-gray-300 after:bg-white after:transition-all after:content-[''] peer-checked:bg-amber-500 peer-checked:after:translate-x-full peer-checked:after:border-white"></div>
                                    </label>
                                </div>
                            </div>

                            <div className="rounded-lg border border-blue-200 bg-blue-50 p-4 text-sm text-blue-800">
                                Empty or null configuration never wipes stored secrets. To clear credentials, use the explicit reset section below and confirm the action.
                            </div>

                            {getConfigFields(editingMethod.code).map((field) => (
                                <div key={field.key}>
                                    <label className="label">{field.label}</label>
                                    <input
                                        type={field.sensitive ? 'password' : 'text'}
                                        placeholder={field.placeholder}
                                        value={configData.configuration[field.key] || ''}
                                        onChange={(e) => setConfigData((current) => ({
                                            ...current,
                                            reset: false,
                                            confirmReset: false,
                                            configuration: {
                                                ...current.configuration,
                                                [field.key]: e.target.value
                                            }
                                        }))}
                                        className="input font-mono"
                                        disabled={configData.reset}
                                    />
                                </div>
                            ))}

                            <div className="rounded-lg border border-red-200 bg-red-50 p-4">
                                <label className="flex items-start gap-3 text-sm text-red-800">
                                    <input
                                        type="checkbox"
                                        checked={configData.reset}
                                        onChange={(e) => handleResetToggle(e.target.checked)}
                                        className="mt-1"
                                    />
                                    <span>
                                        Reset the stored provider configuration.
                                        This will disable the payment method immediately to prevent invalid checkout exposure.
                                    </span>
                                </label>

                                {configData.reset && (
                                    <label className="mt-3 flex items-start gap-3 text-sm text-red-900">
                                        <input
                                            type="checkbox"
                                            checked={configData.confirmReset}
                                            onChange={(e) => setConfigData((current) => ({ ...current, confirmReset: e.target.checked }))}
                                            className="mt-1"
                                        />
                                        <span>I understand this clears the stored configuration and requires reconfiguration before the method can be enabled again.</span>
                                    </label>
                                )}
                            </div>

                            <div className="flex gap-3 pt-4">
                                <button type="button" onClick={closeConfigModal} className="btn-secondary flex-1">
                                    {t('common.cancel')}
                                </button>
                                <button type="submit" className="btn-primary flex-1">
                                    {configData.reset ? 'Reset Configuration' : t('common.save')}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}
        </div>
    )
}
