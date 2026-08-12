/**
 * Carrier Configuration Modal
 * Phase: Carrier Configuration UI Wiring (Final 10%)
 * 
 * REUSABLE MODAL FOR ALL CARRIERS
 * 
 * This modal works for Bosta, Aramex, Mylerz, DHL - same UI for all.
 * Carrier-specific differences come ONLY from backend data.
 * 
 * APIs Used:
 * - GET  /api/admin/carriers           - Load current configuration
 * - PUT  /api/admin/carriers/{code}    - Save configuration
 * - POST /api/admin/carriers/{code}/validate - Test connection
 * - POST /api/admin/carriers/{code}/toggle   - Enable/disable
 * 
 * NO BACKEND CHANGES - Pure API consumer
 */

import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import {
    X, Eye, EyeOff, Check, AlertCircle, RefreshCw,
    Shield, TestTube, Save, ToggleLeft, ToggleRight, Loader2
} from 'lucide-react'
import api from '../../../services/api'
import { getCarrierInfo } from '../hooks/useShippingSetupApi'

export default function CarrierConfigurationModal({
    carrierCode,
    carrierName,
    open,
    onClose,
    onSaved
}) {
    const { t } = useTranslation()

    // Form state
    const [formData, setFormData] = useState({
        isEnabled: false,
        useSandbox: true,
        apiKey: '',
        apiSecret: '',
        webhookSecret: '',
        accountId: '',
        extraConfig: {}
    })

    // UI state
    const [loading, setLoading] = useState(false)
    const [saving, setSaving] = useState(false)
    const [testing, setTesting] = useState(false)
    const [toggling, setToggling] = useState(false)
    const [showApiKey, setShowApiKey] = useState(false)
    const [showApiSecret, setShowApiSecret] = useState(false)
    const [showWebhookSecret, setShowWebhookSecret] = useState(false)
    const [error, setError] = useState('')
    const [success, setSuccess] = useState('')
    const [testResult, setTestResult] = useState(null)
    const [showDisableConfirm, setShowDisableConfirm] = useState(false)

    // Original data (to detect changes)
    const [originalData, setOriginalData] = useState(null)
    const [isConfigured, setIsConfigured] = useState(false)
    const [configurationMetadata, setConfigurationMetadata] = useState(null)

    const carrierInfo = getCarrierInfo(carrierCode)

    // Load current configuration on modal open
    useEffect(() => {
        if (open && carrierCode) {
            loadConfiguration()
        } else {
            // Reset state when modal closes
            resetState()
        }
    }, [open, carrierCode])

    const resetState = () => {
        setFormData({
            isEnabled: false,
            useSandbox: true,
            apiKey: '',
            apiSecret: '',
            webhookSecret: '',
            accountId: '',
            extraConfig: {}
        })
        setOriginalData(null)
        setIsConfigured(false)
        setConfigurationMetadata(null)
        setError('')
        setSuccess('')
        setTestResult(null)
        setShowApiKey(false)
        setShowApiSecret(false)
        setShowWebhookSecret(false)
        setShowDisableConfirm(false)
    }

    const loadConfiguration = async () => {
        setLoading(true)
        setError('')

        try {
            const response = await api.get('/admin/carriers')
            const carriers = response.data || []
            const carrier = carriers.find(c => c.carrierCode === carrierCode)

            if (carrier) {
                const data = {
                    isEnabled: carrier.isEnabled || false,
                    useSandbox: carrier.useSandbox ?? true,
                    apiKey: carrier.apiKey || '',
                    apiSecret: carrier.apiSecret || '',
                    webhookSecret: '',
                    accountId: carrier.accountId || '',
                    extraConfig: {}
                }

                setFormData(data)
                setOriginalData(data)
                setIsConfigured(carrier.isConfigured === true || carrier.hasApiKey === true)
                setConfigurationMetadata(carrier)
            }
        } catch (err) {
            console.error('Failed to load carrier configuration:', err)
            setError(err.response?.data?.message || t('carrierConfig.loadError', 'Failed to load configuration'))
        } finally {
            setLoading(false)
        }
    }

    // Save configuration
    const handleSave = async () => {
        setSaving(true)
        setError('')
        setSuccess('')

        try {
            const extraConfig = Object.fromEntries(
                Object.entries(formData.extraConfig || {})
                    .map(([key, value]) => [key, value.trim()])
                    .filter(([, value]) => !!value)
            )

            const payload = {
                isEnabled: formData.isEnabled,
                useSandbox: formData.useSandbox,
                apiKey: formData.apiKey || null,
                apiSecret: formData.apiSecret || null,
                webhookSecret: formData.webhookSecret || null,
                supportsWebhooks: configurationMetadata?.webhookSupported === true,
                accountId: formData.accountId || null,
                configuration: Object.keys(extraConfig).length ? JSON.stringify(extraConfig) : null
            }

            await api.put(`/admin/carriers/${carrierCode}`, payload)

            setSuccess(t('carrierConfig.saveSuccess', 'Configuration saved successfully'))
            setIsConfigured(configurationCredentialFields.every((field) => !field.required || getCredentialFieldValue(field).trim()))
            setFormData(prev => ({ ...prev, webhookSecret: '' }))
            setOriginalData({ ...formData, webhookSecret: '' })

            // Notify parent to refresh
            onSaved?.()

            // Clear success message after 3 seconds
            setTimeout(() => setSuccess(''), 3000)
        } catch (err) {
            console.error('Failed to save carrier configuration:', err)
            setError(err.response?.data?.message || t('carrierConfig.saveError', 'Failed to save configuration'))
        } finally {
            setSaving(false)
        }
    }

    // Test connection
    const handleTestConnection = async () => {
        setTesting(true)
        setError('')
        setTestResult(null)

        try {
            const startTime = Date.now()
            const response = await api.post(`/admin/carriers/${carrierCode}/validate`)
            const latency = Date.now() - startTime

            setTestResult({
                success: response.data?.success !== false,
                message: response.data?.errorMessage || response.data?.message || t('carrierConfig.connectionSuccess', 'Connection successful'),
                latency
            })

            // Refresh to get updated validation timestamp
            onSaved?.()
        } catch (err) {
            console.error('Connection test failed:', err)
            setTestResult({
                success: false,
                message: err.response?.data?.message || t('carrierConfig.connectionFailed', 'Connection failed')
            })
        } finally {
            setTesting(false)
        }
    }

    // Toggle carrier enabled/disabled
    const handleToggle = async () => {
        // If enabling, just toggle
        // If disabling, show confirmation first
        if (formData.isEnabled && !showDisableConfirm) {
            setShowDisableConfirm(true)
            return
        }

        setToggling(true)
        setError('')
        setShowDisableConfirm(false)

        try {
            await api.post(`/admin/carriers/${carrierCode}/toggle`)

            // Update local state
            setFormData(prev => ({ ...prev, isEnabled: !prev.isEnabled }))
            setSuccess(
                formData.isEnabled
                    ? t('carrierConfig.disableSuccess', 'Carrier disabled')
                    : t('carrierConfig.enableSuccess', 'Carrier enabled')
            )

            // Notify parent to refresh
            onSaved?.()

            setTimeout(() => setSuccess(''), 3000)
        } catch (err) {
            console.error('Failed to toggle carrier:', err)
            setError(err.response?.data?.message || t('carrierConfig.toggleError', 'Failed to toggle carrier'))
        } finally {
            setToggling(false)
        }
    }

    const hasChanges = originalData && JSON.stringify(formData) !== JSON.stringify(originalData)
    const configurationCredentialFields = configurationMetadata?.credentialFields || []
    const connectionTestSupported = configurationMetadata?.connectionTestSupported !== false
    const connectionTestMode = configurationMetadata?.connectionTestMode || 'Remote'
    const productionBlocked = ['Unavailable', 'NotCertified'].includes(configurationMetadata?.productionCapability)
    const validationCurrent = configurationMetadata?.validationMatchesCurrentConfiguration === true
    const connected = configurationMetadata?.lastValidationSucceeded === true && validationCurrent
    const carrierState = productionBlocked || configurationMetadata?.lastValidationSucceeded === false
        ? t('carrierConfig.blocked', 'Blocked / provider unavailable')
        : !isConfigured
            ? t('carrierConfig.notConfigured', 'Not configured')
            : !connected
                ? t('carrierConfig.credentialsSaved', 'Credentials saved')
                : formData.isEnabled
                    ? t('carrierConfig.readyForCheckout', 'Ready for checkout')
                    : t('carrierConfig.connected', 'Connected')

    const getCredentialFieldValue = (field) => {
        if (field.target === 'ApiKey') return formData.apiKey
        if (field.target === 'ApiSecret') return formData.apiSecret
        if (field.target === 'AccountId') return formData.accountId
        if (field.target?.startsWith('ExtraConfig.')) {
            return formData.extraConfig?.[field.target.slice('ExtraConfig.'.length)] || ''
        }

        return ''
    }

    const setCredentialFieldValue = (field, value) => {
        if (field.target === 'ApiKey') {
            setFormData(prev => ({ ...prev, apiKey: value }))
            return
        }
        if (field.target === 'ApiSecret') {
            setFormData(prev => ({ ...prev, apiSecret: value }))
            return
        }
        if (field.target === 'AccountId') {
            setFormData(prev => ({ ...prev, accountId: value }))
            return
        }
        if (field.target?.startsWith('ExtraConfig.')) {
            const key = field.target.slice('ExtraConfig.'.length)
            setFormData(prev => ({
                ...prev,
                extraConfig: {
                    ...prev.extraConfig,
                    [key]: value
                }
            }))
        }
    }

    if (!open) return null

    return (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-4">
            <div className="bg-white rounded-2xl shadow-2xl w-full max-w-2xl max-h-[90vh] overflow-hidden flex flex-col">
                {/* Header */}
                <div className={`p-6 border-b border-gray-100 ${carrierInfo.bgClass}`}>
                    <div className="flex items-center justify-between">
                        <div className="flex items-center gap-4">
                            <div className="text-4xl">{carrierInfo.logo}</div>
                            <div>
                                <h2 className="text-xl font-bold text-gray-900">
                                    {t('carrierConfig.title', 'Configure {{carrier}}', { carrier: carrierName || carrierInfo.name })}
                                </h2>
                                <p className="text-sm text-gray-500">
                                    {carrierInfo.region} • {carrierCode}
                                </p>
                            </div>
                        </div>
                        <button
                            onClick={onClose}
                            className="p-2 hover:bg-white/50 rounded-lg transition-colors"
                        >
                            <X size={20} className="text-gray-500" />
                        </button>
                    </div>

                    {/* Status badges */}
                    <div className="flex gap-2 mt-4">
                        <span className={`px-2.5 py-1 rounded-full text-xs font-semibold ${formData.isEnabled
                            ? 'bg-green-100 text-green-700'
                            : 'bg-gray-100 text-gray-600'
                            }`}>
                            {formData.isEnabled ? t('common.enabled', 'Enabled') : t('common.disabled', 'Disabled')}
                        </span>
                        <span className={`px-2.5 py-1 rounded-full text-xs font-semibold ${isConfigured
                            ? 'bg-blue-100 text-blue-700'
                            : 'bg-amber-100 text-amber-700'
                            }`}>
                            {carrierState}
                        </span>
                        {formData.useSandbox && (
                            <span className="px-2.5 py-1 rounded-full text-xs font-semibold bg-amber-100 text-amber-700">
                                {t('carrierConfig.sandbox', 'Sandbox')}
                            </span>
                        )}
                    </div>
                </div>

                {/* Body */}
                <div className="flex-1 overflow-y-auto p-6 space-y-6">
                    {/* MANUAL FULFILLMENT - Special Case: No configuration needed */}
                    {carrierCode === 'manual' ? (
                        <div className="space-y-6">
                            {/* Info Banner */}
                            <div className="p-6 bg-gradient-to-br from-slate-50 to-slate-100 border border-slate-200 rounded-2xl">
                                <div className="flex items-start gap-4">
                                    <div className="text-4xl">🛠️</div>
                                    <div>
                                        <h3 className="text-lg font-bold text-slate-900 mb-2">
                                            {t('carrierConfig.manualTitle', 'Manual Fulfillment')}
                                        </h3>
                                        <p className="text-slate-600 mb-4">
                                            {t('carrierConfig.manualDesc', 'Handle shipping yourself without a carrier API. Perfect for local delivery, pickup orders, or custom shipping arrangements.')}
                                        </p>
                                        <ul className="space-y-2 text-sm text-slate-600">
                                            <li className="flex items-center gap-2">
                                                <span className="text-green-500">✓</span>
                                                {t('carrierConfig.manualBenefit1', 'No API credentials required')}
                                            </li>
                                            <li className="flex items-center gap-2">
                                                <span className="text-green-500">✓</span>
                                                {t('carrierConfig.manualBenefit2', 'Update tracking manually in admin panel')}
                                            </li>
                                            <li className="flex items-center gap-2">
                                                <span className="text-green-500">✓</span>
                                                {t('carrierConfig.manualBenefit3', 'Works for any delivery method')}
                                            </li>
                                            <li className="flex items-center gap-2">
                                                <span className="text-green-500">✓</span>
                                                {t('carrierConfig.manualBenefit4', 'Always available - no configuration needed')}
                                            </li>
                                        </ul>
                                    </div>
                                </div>
                            </div>

                            {/* How it Works */}
                            <div className="p-5 bg-white border border-gray-200 rounded-xl">
                                <h4 className="font-semibold text-gray-900 mb-3">
                                    {t('carrierConfig.howItWorks', 'How It Works')}
                                </h4>
                                <div className="space-y-3">
                                    <div className="flex items-start gap-3">
                                        <div className="w-6 h-6 rounded-full bg-indigo-100 text-indigo-600 flex items-center justify-center text-sm font-bold flex-shrink-0">1</div>
                                        <div>
                                            <div className="font-medium text-gray-900">{t('carrierConfig.step1Title', 'Customer places order')}</div>
                                            <div className="text-sm text-gray-500">{t('carrierConfig.step1Desc', 'Selects a shipping method linked to Manual Fulfillment')}</div>
                                        </div>
                                    </div>
                                    <div className="flex items-start gap-3">
                                        <div className="w-6 h-6 rounded-full bg-indigo-100 text-indigo-600 flex items-center justify-center text-sm font-bold flex-shrink-0">2</div>
                                        <div>
                                            <div className="font-medium text-gray-900">{t('carrierConfig.step2Title', 'Shipment created as Pending')}</div>
                                            <div className="text-sm text-gray-500">{t('carrierConfig.step2Desc', 'Appears in your admin shipments list')}</div>
                                        </div>
                                    </div>
                                    <div className="flex items-start gap-3">
                                        <div className="w-6 h-6 rounded-full bg-indigo-100 text-indigo-600 flex items-center justify-center text-sm font-bold flex-shrink-0">3</div>
                                        <div>
                                            <div className="font-medium text-gray-900">{t('carrierConfig.step3Title', 'You handle delivery')}</div>
                                            <div className="text-sm text-gray-500">{t('carrierConfig.step3Desc', 'Ship via your own method, local courier, or customer pickup')}</div>
                                        </div>
                                    </div>
                                    <div className="flex items-start gap-3">
                                        <div className="w-6 h-6 rounded-full bg-green-100 text-green-600 flex items-center justify-center text-sm font-bold flex-shrink-0">4</div>
                                        <div>
                                            <div className="font-medium text-gray-900">{t('carrierConfig.step4Title', 'Update status manually')}</div>
                                            <div className="text-sm text-gray-500">{t('carrierConfig.step4Desc', 'Mark as shipped, delivered, etc. in the admin panel')}</div>
                                        </div>
                                    </div>
                                </div>
                            </div>

                            {/* Status Indicator */}
                            <div className="p-4 bg-green-50 border border-green-200 rounded-xl flex items-center gap-3">
                                <div className="w-10 h-10 rounded-full bg-green-100 flex items-center justify-center">
                                    <Check className="text-green-600" size={20} />
                                </div>
                                <div>
                                    <div className="font-semibold text-green-800">
                                        {t('carrierConfig.manualReady', 'Ready to Use')}
                                    </div>
                                    <div className="text-sm text-green-600">
                                        {t('carrierConfig.manualReadyDesc', 'Manual Fulfillment is always available. Link it to a shipping method to start using.')}
                                    </div>
                                </div>
                            </div>
                        </div>
                    ) : loading ? (
                        <div className="flex items-center justify-center py-12">
                            <Loader2 className="animate-spin text-indigo-600" size={32} />
                        </div>
                    ) : (
                        <>
                            {/* Error Message */}
                            {error && (
                                <div className="p-4 bg-red-50 border border-red-200 rounded-xl flex items-center gap-3">
                                    <AlertCircle className="text-red-500 flex-shrink-0" size={20} />
                                    <span className="text-red-700">{error}</span>
                                    <button onClick={() => setError('')} className="ml-auto text-red-500">×</button>
                                </div>
                            )}

                            {/* Success Message */}
                            {success && (
                                <div className="p-4 bg-green-50 border border-green-200 rounded-xl flex items-center gap-3">
                                    <Check className="text-green-500 flex-shrink-0" size={20} />
                                    <span className="text-green-700">{success}</span>
                                </div>
                            )}

                            {/* Test Result */}
                            {testResult && (
                                <div className={`p-4 rounded-xl flex items-center gap-3 ${testResult.success
                                    ? 'bg-green-50 border border-green-200'
                                    : 'bg-red-50 border border-red-200'
                                    }`}>
                                    {testResult.success ? (
                                        <Check className="text-green-500 flex-shrink-0" size={20} />
                                    ) : (
                                        <AlertCircle className="text-red-500 flex-shrink-0" size={20} />
                                    )}
                                    <div className="flex-1">
                                        <span className={testResult.success ? 'text-green-700' : 'text-red-700'}>
                                            {testResult.message}
                                        </span>
                                        {testResult.latency && (
                                            <span className="text-sm text-gray-500 ml-2">
                                                ({testResult.latency}ms)
                                            </span>
                                        )}
                                    </div>
                                    <button
                                        onClick={() => setTestResult(null)}
                                        className={testResult.success ? 'text-green-500' : 'text-red-500'}
                                    >
                                        ×
                                    </button>
                                </div>
                            )}

                            {/* Enable/Disable Toggle */}
                            <div className="flex items-center justify-between p-4 bg-gray-50 rounded-xl">
                                <div>
                                    <h3 className="font-semibold text-gray-900">
                                        {t('carrierConfig.carrierStatus', 'Carrier Status')}
                                    </h3>
                                    <p className="text-sm text-gray-500">
                                        {formData.isEnabled
                                            ? t('carrierConfig.enabledDesc', 'This carrier is available for shipping')
                                            : t('carrierConfig.disabledDesc', 'This carrier cannot be used for shipping')
                                        }
                                    </p>
                                </div>

                                {showDisableConfirm ? (
                                    <div className="flex items-center gap-2">
                                        <span className="text-sm text-amber-600">
                                            {t('carrierConfig.confirmDisable', 'Disable?')}
                                        </span>
                                        <button
                                            onClick={handleToggle}
                                            disabled={toggling}
                                            className="px-3 py-1 bg-red-600 text-white text-sm rounded-lg hover:bg-red-700"
                                        >
                                            {t('common.yes', 'Yes')}
                                        </button>
                                        <button
                                            onClick={() => setShowDisableConfirm(false)}
                                            className="px-3 py-1 bg-gray-200 text-gray-700 text-sm rounded-lg hover:bg-gray-300"
                                        >
                                            {t('common.no', 'No')}
                                        </button>
                                    </div>
                                ) : (
                                    <button
                                        onClick={handleToggle}
                                        disabled={toggling || !isConfigured || productionBlocked}
                                        className="p-1 disabled:opacity-50"
                                        title={productionBlocked
                                            ? t('carrierConfig.notProductionReady', 'This carrier is unavailable for production activation')
                                            : !isConfigured
                                                ? t('carrierConfig.configureFirst', 'Configure credentials first')
                                                : ''}
                                    >
                                        {toggling ? (
                                            <Loader2 className="animate-spin text-gray-400" size={28} />
                                        ) : formData.isEnabled ? (
                                            <ToggleRight size={36} className="text-green-500" />
                                        ) : (
                                            <ToggleLeft size={36} className="text-gray-300" />
                                        )}
                                    </button>
                                )}
                            </div>

                            {/* Environment Selection */}
                            {configurationMetadata?.accountRequired !== false && (
                            <div>
                                <label className="block text-sm font-medium text-gray-700 mb-2">
                                    {t('carrierConfig.environment', 'Environment')}
                                </label>
                                <div className="flex gap-3">
                                    <button
                                        type="button"
                                        onClick={() => setFormData(prev => ({ ...prev, useSandbox: true }))}
                                        className={`flex-1 py-3 px-4 rounded-xl border-2 transition-all ${formData.useSandbox
                                            ? 'border-amber-500 bg-amber-50 text-amber-700'
                                            : 'border-gray-200 bg-white text-gray-600 hover:border-gray-300'
                                            }`}
                                    >
                                        <div className="font-semibold">🧪 {t('carrierConfig.sandbox', 'Sandbox')}</div>
                                        <div className="text-xs mt-1">{t('carrierConfig.sandboxDesc', 'Test environment')}</div>
                                    </button>
                                    <button
                                        type="button"
                                        onClick={() => setFormData(prev => ({ ...prev, useSandbox: false }))}
                                        className={`flex-1 py-3 px-4 rounded-xl border-2 transition-all ${!formData.useSandbox
                                            ? 'border-green-500 bg-green-50 text-green-700'
                                            : 'border-gray-200 bg-white text-gray-600 hover:border-gray-300'
                                            }`}
                                    >
                                        <div className="font-semibold">🚀 {t('carrierConfig.production', 'Production')}</div>
                                        <div className="text-xs mt-1">{t('carrierConfig.productionDesc', 'Live environment')}</div>
                                    </button>
                                </div>
                            </div>
                            )}

                            {/* API Credentials */}
                            <div className="space-y-4">
                                <h3 className="font-semibold text-gray-900 flex items-center gap-2">
                                    <Shield size={18} className="text-indigo-600" />
                                    {t('carrierConfig.credentials', 'API Credentials')}
                                </h3>

                                {productionBlocked ? (
                                    <div className="p-4 bg-amber-50 border border-amber-200 rounded-xl text-sm text-amber-800">
                                        {configurationMetadata?.productionCapability || 'Unavailable'}
                                    </div>
                                ) : configurationCredentialFields.length === 0 ? (
                                    <div className="p-4 bg-slate-50 border border-slate-200 rounded-xl text-sm text-slate-700">
                                        {t('carrierConfig.noCredentialsRequired', 'No credentials are required for this carrier.')}
                                    </div>
                                ) : (
                                    configurationCredentialFields.map((field) => {
                                        const showSecret = field.target === 'ApiSecret' ? showApiSecret : showApiKey
                                        const setShowSecret = field.target === 'ApiSecret' ? setShowApiSecret : setShowApiKey
                                        return (
                                            <div key={field.key}>
                                                <label className="block text-sm font-medium text-gray-700 mb-1">
                                                    {field.label}{field.required ? ' *' : ''}
                                                </label>
                                                <div className="relative">
                                                    <input
                                                        type={field.isSecret && !showSecret ? 'password' : 'text'}
                                                        value={getCredentialFieldValue(field)}
                                                        onChange={e => setCredentialFieldValue(field, e.target.value)}
                                                        placeholder={field.label}
                                                        className="w-full px-4 py-2.5 pr-12 border border-gray-200 rounded-xl focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                                                    />
                                                    {field.isSecret && (
                                                        <button
                                                            type="button"
                                                            onClick={() => setShowSecret(!showSecret)}
                                                            className="absolute right-3 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600"
                                                        >
                                                            {showSecret ? <EyeOff size={18} /> : <Eye size={18} />}
                                                        </button>
                                                    )}
                                                </div>
                                            </div>
                                        )
                                    })
                                )}
                                {connectionTestMode === 'LocalOnly' && (
                                    <p className="text-xs text-gray-500">
                                        {t('carrierConfig.localOnlyValidation', 'This provider validates credential presence locally; no remote API connectivity test is performed.')}
                                    </p>
                                )}
                                {configurationMetadata?.webhookSupported === true && (
                                    <div>
                                        <label className="block text-sm font-medium text-gray-700 mb-1">
                                            {t('carrierConfig.webhookSecret', 'Webhook signing secret')}
                                        </label>
                                        <div className="relative">
                                            <input
                                                type={showWebhookSecret ? 'text' : 'password'}
                                                value={formData.webhookSecret}
                                                onChange={event => setFormData(prev => ({ ...prev, webhookSecret: event.target.value }))}
                                                placeholder={configurationMetadata?.hasWebhookSecret
                                                    ? t('carrierConfig.rotateWebhookSecret', 'Enter a new value to rotate the configured secret')
                                                    : t('carrierConfig.configureWebhookSecret', 'Configure webhook signing secret')}
                                                autoComplete="new-password"
                                                className="w-full px-4 py-2.5 pr-12 border border-gray-200 rounded-xl focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                                            />
                                            <button
                                                type="button"
                                                onClick={() => setShowWebhookSecret(value => !value)}
                                                className="absolute right-3 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600"
                                                aria-label={t('carrierConfig.toggleWebhookSecretVisibility', 'Toggle webhook secret visibility')}
                                            >
                                                {showWebhookSecret ? <EyeOff size={18} /> : <Eye size={18} />}
                                            </button>
                                        </div>
                                        <p className="mt-1 text-xs text-gray-500">
                                            {configurationMetadata?.hasWebhookSecret
                                                ? t('carrierConfig.webhookSecretConfigured', 'A signing secret is configured. It is never returned by the API.')
                                                : t('carrierConfig.webhookSecretRequired', 'Required before signed webhook events can be trusted.')}
                                        </p>
                                    </div>
                                )}
                            </div>
                        </>
                    )}
                </div>

                {/* Footer Actions */}
                <div className="p-6 border-t border-gray-100 bg-gray-50">
                    {carrierCode === 'manual' ? (
                        /* Manual Fulfillment - Just a close button */
                        <div className="flex justify-end">
                            <button
                                onClick={onClose}
                                className="px-6 py-2.5 bg-indigo-600 text-white font-medium rounded-xl hover:bg-indigo-700 transition-colors"
                            >
                                {t('common.close', 'Close')}
                            </button>
                        </div>
                    ) : (
                        /* Regular Carriers - Test & Save buttons */
                        <div className="flex items-center justify-between">
                            {/* Test Connection */}
                            <button
                                onClick={handleTestConnection}
                                disabled={testing || loading || !connectionTestSupported || productionBlocked || !isConfigured}
                                className="flex items-center gap-2 px-4 py-2.5 bg-white border border-gray-200 text-gray-700 font-medium rounded-xl hover:bg-gray-50 disabled:opacity-50 transition-colors"
                            >
                                {testing ? (
                                    <Loader2 className="animate-spin" size={18} />
                                ) : (
                                    <TestTube size={18} />
                                )}
                                {testing
                                    ? t('carrierConfig.testing', 'Testing...')
                                    : connectionTestMode === 'LocalOnly'
                                        ? t('carrierConfig.validateLocally', 'Validate Locally')
                                        : t('carrierConfig.testConnection', 'Test Connection')
                                }
                            </button>

                            {/* Save & Close */}
                            <div className="flex items-center gap-3">
                                <button
                                    onClick={onClose}
                                    className="px-4 py-2.5 text-gray-600 hover:text-gray-900 font-medium transition-colors"
                                >
                                    {t('common.cancel', 'Cancel')}
                                </button>
                                <button
                                    onClick={handleSave}
                                    disabled={saving || loading || !hasChanges}
                                    className="flex items-center gap-2 px-6 py-2.5 bg-indigo-600 text-white font-medium rounded-xl hover:bg-indigo-700 disabled:opacity-50 transition-colors"
                                >
                                    {saving ? (
                                        <Loader2 className="animate-spin" size={18} />
                                    ) : (
                                        <Save size={18} />
                                    )}
                                    {saving ? t('common.saving', 'Saving...') : t('common.save', 'Save')}
                                </button>
                            </div>
                        </div>
                    )}
                </div>
            </div>
        </div>
    )
}
