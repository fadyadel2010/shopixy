import { useState, useEffect, useCallback, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { useToast } from '../components/Toast'
import { useAuthStore } from '../store/authStore'
import { deriveCurrentStoreIdentityPresentation } from '../auth/commerceHubVendorIdentityAuthority'
import { canManageStorefrontSettings } from '../auth/storefrontAdminAuthority'
import { storeConfigService } from '../modules/storefront/services'

/**
 * Storefront Settings Component
 * 
 * PHASE 7.10: POS ↔ Storefront Bridge Polish
 * 
 * Controls:
 * - Storefront status (live/maintenance/disabled)
 * - Maintenance message
 * - Preview links to storefront
 * - Health indicators
 */

// Storefront domain configuration
const STOREFRONT_DOMAIN = import.meta.env.VITE_STOREFRONT_DOMAIN || 'localhost:5174'

export default function StorefrontSettings() {
    const { t, i18n } = useTranslation()
    const { showSuccess, showError, showInfo } = useToast()
    const { user } = useAuthStore()
    const isRTL = i18n.language === 'ar'

    // Get tenant ID from logged-in user
    const tenantId = user?.tenantId
    const canManageStorefront = canManageStorefrontSettings(user)

    const [loading, setLoading] = useState(true)
    const [saving, setSaving] = useState(false)
    const [baselineConfig, setBaselineConfig] = useState(null)
    const [candidateConfig, setCandidateConfig] = useState(null)
    const [loadError, setLoadError] = useState(null)
    const requestIdRef = useRef(0)
    const dirtyRef = useRef(false)
    const saveInFlightRef = useRef(false)

    const isDirty = Boolean(baselineConfig && candidateConfig && (
        baselineConfig.storefrontStatus !== candidateConfig.storefrontStatus ||
        (baselineConfig.maintenanceMessage || '') !== (candidateConfig.maintenanceMessage || '')
    ))

    useEffect(() => {
        dirtyRef.current = isDirty
    }, [isDirty])

    useEffect(() => {
        return () => {
            requestIdRef.current += 1
        }
    }, [])

    // Fetch store config on mount
    const fetchStoreConfig = useCallback(async () => {
        const requestId = ++requestIdRef.current

        if (!tenantId || !canManageStorefront) {
            setBaselineConfig(null)
            setCandidateConfig(null)
            setLoadError(null)
            setLoading(false)
            return
        }
        setLoading(true)
        try {
            const response = await storeConfigService.getStoreConfig()
            if (requestId !== requestIdRef.current) {
                return
            }

            const nextStoreConfig = deriveCurrentStoreIdentityPresentation(response, user)
            setBaselineConfig(nextStoreConfig)
            if (!dirtyRef.current) {
                setCandidateConfig(nextStoreConfig)
            }
            setLoadError(nextStoreConfig ? null : t('storefront.failedToLoad') || 'Failed to load storefront settings')
            if (!nextStoreConfig) {
                setCandidateConfig(null)
                showError(t('storefront.failedToLoad') || 'Failed to load storefront settings')
            }
        } catch (error) {
            if (requestId !== requestIdRef.current) {
                return
            }

            console.error('Error fetching store config:', error)
            setBaselineConfig(null)
            setCandidateConfig(null)
            setLoadError(t('storefront.failedToLoad') || 'Failed to load storefront settings')
            if (error.response?.status !== 404) {
                showError(t('storefront.failedToLoad') || 'Failed to load storefront settings')
            }
        } finally {
            if (requestId === requestIdRef.current) {
                setLoading(false)
            }
        }
    }, [tenantId, canManageStorefront, user, showError, t])

    useEffect(() => {
        fetchStoreConfig()
    }, [fetchStoreConfig])

    // Save store config
    const handleSave = async () => {
        if (!candidateConfig || saveInFlightRef.current) {
            return
        }

        saveInFlightRef.current = true
        setSaving(true)
        try {
            const acceptedResponse = await storeConfigService.updateStorefrontStatus({
                storefrontStatus: candidateConfig.storefrontStatus,
                maintenanceMessage: candidateConfig.maintenanceMessage,
                rowVersion: candidateConfig.rowVersion,
            })
            const acceptedConfig = deriveCurrentStoreIdentityPresentation(acceptedResponse, user)
            if (!acceptedConfig) {
                throw new Error('Store configuration mutation response was not accepted as current backend authority.')
            }
            dirtyRef.current = false
            setBaselineConfig(acceptedConfig)
            setCandidateConfig(acceptedConfig)
            setLoadError(null)
            showSuccess(t('storefront.saved') || 'Storefront settings saved')
        } catch (error) {
            console.error('Error saving store config:', error)

            // Phase 8.4: Handle email verification error
            const errorCode = error.response?.data?.code
            if (errorCode === 'EMAIL_NOT_VERIFIED') {
                showError(
                    t('storefront.emailNotVerified') ||
                    'Please verify your email before publishing your storefront. Check your inbox for the verification link.'
                )
            } else {
                showError(t('storefront.failedToSave') || 'Failed to save storefront settings')
            }
        } finally {
            saveInFlightRef.current = false
            setSaving(false)
        }
    }

    const resetCandidateToBaseline = () => {
        setCandidateConfig(baselineConfig)
    }

    // Open storefront preview
    const openStorefront = () => {
        const url = `http://${STOREFRONT_DOMAIN}`
        window.open(url, '_blank', 'noopener,noreferrer')
        showInfo(t('storefront.openingPreview') || 'Opening storefront preview...')
    }

    // Get status badge config
    const getStatusConfig = (status) => {
        switch (status) {
            case 'live':
                return { bg: 'bg-green-100', text: 'text-green-800', icon: '🟢', label: t('storefront.statusLive') || 'Live' }
            case 'maintenance':
                return { bg: 'bg-yellow-100', text: 'text-yellow-800', icon: '🟡', label: t('storefront.statusMaintenance') || 'Maintenance' }
            case 'disabled':
                return { bg: 'bg-red-100', text: 'text-red-800', icon: '🔴', label: t('storefront.statusDisabled') || 'Disabled' }
            default:
                return { bg: 'bg-gray-100', text: 'text-gray-800', icon: '⚪', label: 'Unknown' }
        }
    }

    if (loading) {
        return (
            <div className="flex items-center justify-center py-12">
                <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600"></div>
            </div>
        )
    }

    if (!canManageStorefront) {
        return (
            <div className="rounded-xl border border-gray-200 bg-gray-50 p-6 text-sm text-gray-600" dir={isRTL ? 'rtl' : 'ltr'}>
                {t('common.unauthorized') || 'You are not authorized to manage storefront settings.'}
            </div>
        )
    }

    if (loadError || !baselineConfig || !candidateConfig) {
        return (
            <div className="rounded-xl border border-gray-200 bg-gray-50 p-6 text-sm text-gray-600" dir={isRTL ? 'rtl' : 'ltr'}>
                {loadError || (t('storefront.failedToLoad') || 'Failed to load storefront settings')}
            </div>
        )
    }

    const baselineStatusConfig = getStatusConfig(baselineConfig.storefrontStatus)

    return (
        <div className="space-y-8" dir={isRTL ? 'rtl' : 'ltr'}>
            {/* Header with Status Badge */}
            <div className="flex items-center justify-between">
                <div className="flex items-center gap-4">
                    <div className="w-12 h-12 bg-blue-100 rounded-xl flex items-center justify-center">
                        <span className="text-2xl">🌐</span>
                    </div>
                    <div>
                        <h3 className="text-lg font-semibold text-gray-900">
                            {t('storefront.onlineStore') || 'Online Store'}
                        </h3>
                        <div className="flex items-center gap-2 mt-1">
                            <span className={`inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-sm font-medium ${baselineStatusConfig.bg} ${baselineStatusConfig.text}`}>
                                <span>{baselineStatusConfig.icon}</span>
                                {baselineStatusConfig.label}
                            </span>
                        </div>
                    </div>
                </div>
                <button
                    onClick={handleSave}
                    disabled={saving || !candidateConfig}
                    className="btn-primary"
                >
                    {saving ? (t('common.saving') || 'Saving...') : (t('common.save') || 'Save')}
                </button>
            </div>

            {isDirty && (
                <div className="flex items-center justify-between gap-4 rounded-lg border border-amber-200 bg-amber-50 p-4 text-sm text-amber-800">
                    <span>Unsaved storefront settings are candidate-only until the backend accepts them.</span>
                    <button type="button" onClick={resetCandidateToBaseline} className="font-semibold underline">
                        Reset Changes
                    </button>
                </div>
            )}

            {/* Storefront Status Warning Banner */}
            {baselineConfig.storefrontStatus === 'disabled' && (
                <div className="bg-red-50 border border-red-200 rounded-lg p-4">
                    <div className="flex items-start gap-3">
                        <span className="text-xl">⚠️</span>
                        <div>
                            <h4 className="font-medium text-red-800">
                                {t('storefront.storeDisabledWarning') || 'Your online store is disabled'}
                            </h4>
                            <p className="text-sm text-red-600 mt-1">
                                {t('storefront.storeDisabledDescription') || 'Customers cannot access your online store. Enable it to start accepting orders.'}
                            </p>
                        </div>
                    </div>
                </div>
            )}

            {baselineConfig.storefrontStatus === 'maintenance' && (
                <div className="bg-yellow-50 border border-yellow-200 rounded-lg p-4">
                    <div className="flex items-start gap-3">
                        <span className="text-xl">🔧</span>
                        <div>
                            <h4 className="font-medium text-yellow-800">
                                {t('storefront.maintenanceWarning') || 'Your online store is in maintenance mode'}
                            </h4>
                            <p className="text-sm text-yellow-600 mt-1">
                                {t('storefront.maintenanceDescription') || 'Customers will see a maintenance message when they visit your store.'}
                            </p>
                        </div>
                    </div>
                </div>
            )}

            {/* Quick Actions */}
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                <button
                    onClick={openStorefront}
                    className="flex items-center gap-3 p-4 bg-gradient-to-r from-blue-50 to-blue-100 rounded-xl border border-blue-200 hover:border-blue-300 transition-all group"
                >
                    <div className="w-10 h-10 bg-blue-500 rounded-lg flex items-center justify-center text-white group-hover:scale-110 transition-transform">
                        🛍️
                    </div>
                    <div className="text-left">
                        <div className="font-medium text-blue-900">{t('storefront.openStorefront') || 'Open Storefront'}</div>
                        <div className="text-xs text-blue-600">{t('storefront.viewLiveStore') || 'View your live store'}</div>
                    </div>
                    <svg className="w-5 h-5 text-blue-500 ms-auto" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14" />
                    </svg>
                </button>

                <div className="p-4 bg-gray-50 rounded-xl border border-gray-200">
                    <div className="flex items-center gap-3">
                        <div className="w-10 h-10 bg-gray-200 rounded-lg flex items-center justify-center">
                            📊
                        </div>
                        <div className="text-left">
                            <div className="font-medium text-gray-900">{t('storefront.lastUpdated') || 'Last Updated'}</div>
                            <div className="text-xs text-gray-600">
                                {baselineConfig.updatedAtUtc
                                    ? new Date(baselineConfig.updatedAtUtc).toLocaleString()
                                    : (t('storefront.never') || 'Never')}
                            </div>
                        </div>
                    </div>
                </div>
            </div>

            {/* Status Control */}
            <div className="bg-white border border-gray-200 rounded-xl p-6">
                <h4 className="text-base font-semibold text-gray-900 mb-4">
                    {t('storefront.storeStatus') || 'Store Status'}
                </h4>
                <p className="text-sm text-gray-500 mb-4">
                    {t('storefront.storeStatusDescription') || 'Control whether customers can access your online store.'}
                </p>

                <div className="space-y-3">
                    {/* Live */}
                    <label className={`flex items-center gap-4 p-4 rounded-lg border-2 cursor-pointer transition-all ${candidateConfig.storefrontStatus === 'live'
                        ? 'border-green-500 bg-green-50'
                        : 'border-gray-200 hover:border-gray-300'
                        }`}>
                        <input
                            type="radio"
                            name="storefrontStatus"
                            value="live"
                            checked={candidateConfig.storefrontStatus === 'live'}
                            onChange={(e) => setCandidateConfig(prev => ({ ...prev, storefrontStatus: e.target.value }))}
                            className="w-5 h-5 text-green-600 border-gray-300 focus:ring-green-500"
                        />
                        <div className="flex-1">
                            <div className="flex items-center gap-2">
                                <span className="text-lg">🟢</span>
                                <span className="font-medium text-gray-900">{t('storefront.statusLive') || 'Live'}</span>
                            </div>
                            <p className="text-sm text-gray-500 mt-1">
                                {t('storefront.liveDescription') || 'Store is open and accepting orders'}
                            </p>
                        </div>
                    </label>

                    {/* Maintenance */}
                    <label className={`flex items-center gap-4 p-4 rounded-lg border-2 cursor-pointer transition-all ${candidateConfig.storefrontStatus === 'maintenance'
                        ? 'border-yellow-500 bg-yellow-50'
                        : 'border-gray-200 hover:border-gray-300'
                        }`}>
                        <input
                            type="radio"
                            name="storefrontStatus"
                            value="maintenance"
                            checked={candidateConfig.storefrontStatus === 'maintenance'}
                            onChange={(e) => setCandidateConfig(prev => ({ ...prev, storefrontStatus: e.target.value }))}
                            className="w-5 h-5 text-yellow-600 border-gray-300 focus:ring-yellow-500"
                        />
                        <div className="flex-1">
                            <div className="flex items-center gap-2">
                                <span className="text-lg">🟡</span>
                                <span className="font-medium text-gray-900">{t('storefront.statusMaintenance') || 'Maintenance'}</span>
                            </div>
                            <p className="text-sm text-gray-500 mt-1">
                                {t('storefront.maintenanceStatusDescription') || 'Store is temporarily unavailable for updates'}
                            </p>
                        </div>
                    </label>

                    {/* Disabled */}
                    <label className={`flex items-center gap-4 p-4 rounded-lg border-2 cursor-pointer transition-all ${candidateConfig.storefrontStatus === 'disabled'
                        ? 'border-red-500 bg-red-50'
                        : 'border-gray-200 hover:border-gray-300'
                        }`}>
                        <input
                            type="radio"
                            name="storefrontStatus"
                            value="disabled"
                            checked={candidateConfig.storefrontStatus === 'disabled'}
                            onChange={(e) => setCandidateConfig(prev => ({ ...prev, storefrontStatus: e.target.value }))}
                            className="w-5 h-5 text-red-600 border-gray-300 focus:ring-red-500"
                        />
                        <div className="flex-1">
                            <div className="flex items-center gap-2">
                                <span className="text-lg">🔴</span>
                                <span className="font-medium text-gray-900">{t('storefront.statusDisabled') || 'Disabled'}</span>
                            </div>
                            <p className="text-sm text-gray-500 mt-1">
                                {t('storefront.disabledDescription') || 'Store is offline and not accepting orders'}
                            </p>
                        </div>
                    </label>
                </div>
            </div>

            {/* Maintenance Message */}
            {candidateConfig.storefrontStatus === 'maintenance' && (
                <div className="bg-white border border-gray-200 rounded-xl p-6">
                    <h4 className="text-base font-semibold text-gray-900 mb-2">
                        {t('storefront.maintenanceMessage') || 'Maintenance Message'}
                    </h4>
                    <p className="text-sm text-gray-500 mb-4">
                        {t('storefront.maintenanceMessageDescription') || 'This message will be shown to customers while your store is in maintenance mode.'}
                    </p>
                    <textarea
                        value={candidateConfig.maintenanceMessage || ''}
                        onChange={(e) => setCandidateConfig(prev => ({ ...prev, maintenanceMessage: e.target.value }))}
                        placeholder={t('storefront.maintenanceMessagePlaceholder') || "We're making some improvements. Please check back soon!"}
                        className="input w-full"
                        rows={3}
                    />
                </div>
            )}

            {/* Store Info Summary */}
            <div className="bg-gray-50 border border-gray-200 rounded-xl p-6">
                <h4 className="text-base font-semibold text-gray-900 mb-4">
                    {t('storefront.storeInfo') || 'Store Information'}
                </h4>
                <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                    <div className="p-3 bg-white rounded-lg">
                        <div className="text-xs text-gray-500 uppercase tracking-wide">{t('storefront.storeName') || 'Store Name'}</div>
                        <div className="font-medium text-gray-900 mt-1">{baselineConfig.storeName || '-'}</div>
                    </div>
                    <div className="p-3 bg-white rounded-lg">
                        <div className="text-xs text-gray-500 uppercase tracking-wide">{t('storefront.activeTheme') || 'Active Theme'}</div>
                        <div className="font-medium text-gray-900 mt-1 capitalize">{baselineConfig.themeId}</div>
                    </div>
                    <div className="p-3 bg-white rounded-lg">
                        <div className="text-xs text-gray-500 uppercase tracking-wide">{t('storefront.currentStatus') || 'Current Status'}</div>
                        <div className={`font-medium mt-1 ${baselineStatusConfig.text}`}>{baselineStatusConfig.label}</div>
                    </div>
                </div>
            </div>
        </div>
    )
}
