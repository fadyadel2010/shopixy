import { useState, useEffect, useCallback, useRef } from 'react'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { deriveCurrentStoreIdentityPresentation } from '../auth/commerceHubVendorIdentityAuthority'
import { useAuthStore } from '../store/authStore'
import { storeConfigService } from '../modules/storefront/services'

/**
 * Storefront Status Card Component
 * 
 * PHASE 7.10: POS ↔ Storefront Bridge Polish
 * 
 * Displays storefront health indicators on the POS Dashboard:
 * - Current status (Live/Maintenance/Disabled)
 * - Active theme
 * - Last config update
 * - Quick link to open storefront
 */

const STOREFRONT_DOMAIN = import.meta.env.VITE_STOREFRONT_DOMAIN || 'localhost:5174'

export default function StorefrontStatusCard() {
    const { t, i18n } = useTranslation()
    const navigate = useNavigate()
    const { user } = useAuthStore()
    const isRTL = i18n.language === 'ar'
    const dir = isRTL ? 'rtl' : 'ltr'
    const tenantId = user?.tenantId

    const [loading, setLoading] = useState(true)
    const [storeConfig, setStoreConfig] = useState(null)
    const [error, setError] = useState(null)
    const requestIdRef = useRef(0)

    useEffect(() => {
        return () => {
            requestIdRef.current += 1
        }
    }, [])

    const fetchStoreConfig = useCallback(async () => {
        const requestId = ++requestIdRef.current

        try {
            setLoading(true)
            const response = await storeConfigService.getStoreConfig()

            if (requestId !== requestIdRef.current) {
                return
            }

            const nextStoreConfig = deriveCurrentStoreIdentityPresentation(response, user)
            setStoreConfig(nextStoreConfig)
            setError(nextStoreConfig ? null : t('storefront.failedToLoadStatus', 'Unable to load storefront status'))
        } catch (err) {
            if (requestId !== requestIdRef.current) {
                return
            }

            console.error('Error fetching store config:', err)
            setError(t('storefront.failedToLoadStatus', 'Unable to load storefront status'))
        } finally {
            if (requestId === requestIdRef.current) {
                setLoading(false)
            }
        }
    }, [t, user])

    useEffect(() => {
        if (tenantId) {
            void fetchStoreConfig()
        } else {
            requestIdRef.current += 1
            setStoreConfig(null)
            setError(null)
            setLoading(false)
        }
    }, [fetchStoreConfig, tenantId])

    const getStatusConfig = (status) => {
        switch (status) {
            case 'live':
                return {
                    bg: 'from-green-50 to-green-100',
                    border: 'border-green-200',
                    text: 'text-green-800',
                    subtext: 'text-green-600',
                    icon: '🟢',
                    label: t('storefront.statusLive', 'Live'),
                    description: t('storefront.liveDescription', 'Store is open and accepting orders')
                }
            case 'maintenance':
                return {
                    bg: 'from-yellow-50 to-yellow-100',
                    border: 'border-yellow-200',
                    text: 'text-yellow-800',
                    subtext: 'text-yellow-600',
                    icon: '🟡',
                    label: t('storefront.statusMaintenance', 'Maintenance'),
                    description: t('storefront.maintenanceStatusDescription', 'Store is temporarily unavailable')
                }
            case 'disabled':
                return {
                    bg: 'from-red-50 to-red-100',
                    border: 'border-red-200',
                    text: 'text-red-800',
                    subtext: 'text-red-600',
                    icon: '🔴',
                    label: t('storefront.statusDisabled', 'Disabled'),
                    description: t('storefront.disabledDescription', 'Store is offline')
                }
            default:
                return {
                    bg: 'from-gray-50 to-gray-100',
                    border: 'border-gray-200',
                    text: 'text-gray-800',
                    subtext: 'text-gray-600',
                    icon: '⚪',
                    label: t('storefront.statusUnknown', 'Unknown'),
                    description: t('storefront.unknownStatusDescription', 'Status unknown')
                }
        }
    }

    const openStorefront = () => {
        window.open(`http://${STOREFRONT_DOMAIN}`, '_blank', 'noopener,noreferrer')
    }

    const goToSettings = () => {
        navigate('/settings')
    }

    if (loading) {
        return (
            <div className="card bg-gradient-to-br from-blue-50 to-blue-100 border-blue-200" dir={dir}>
                <div className="flex items-center justify-center py-4">
                    <div className="animate-spin rounded-full h-6 w-6 border-b-2 border-blue-600"></div>
                </div>
            </div>
        )
    }

    if (error || !storeConfig) {
        return (
            <div className="card bg-gradient-to-br from-gray-50 to-gray-100 border-gray-200" dir={dir}>
                <div className="flex items-center justify-between">
                    <div>
                        <p className="text-sm font-medium text-gray-600">
                            {t('storefront.onlineStore', 'Online Store')}
                        </p>
                        <p className="text-sm text-gray-500 mt-2">
                            {t('storefront.notConfigured', 'Not configured')}
                        </p>
                        <button
                            onClick={goToSettings}
                            className="text-sm text-blue-600 hover:text-blue-700 mt-2 underline"
                        >
                            {t('storefront.setupNow', 'Set up now')} →
                        </button>
                    </div>
                    <div className="text-4xl">🌐</div>
                </div>
            </div>
        )
    }

    const status = storeConfig.storefrontStatus
    const statusConfig = getStatusConfig(status)

    return (
        <div className={`card bg-gradient-to-br ${statusConfig.bg} ${statusConfig.border}`} dir={dir}>
            <div className="flex items-center justify-between">
                <div className="flex-1">
                    <div className="flex items-center gap-2">
                        <p className={`text-sm font-medium ${statusConfig.subtext}`}>
                            {t('storefront.onlineStore', 'Online Store')}
                        </p>
                        <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium ${statusConfig.text} bg-white/50`}>
                            {statusConfig.icon} {statusConfig.label}
                        </span>
                    </div>
                    <p className={`text-lg font-bold ${statusConfig.text} mt-2`}>
                        {storeConfig.storeName}
                    </p>
                    <div className="flex items-center gap-4 mt-2">
                        <button
                            onClick={openStorefront}
                            className={`text-sm ${statusConfig.subtext} hover:underline flex items-center gap-1`}
                        >
                            {t('storefront.viewStore', 'View Store')}
                            <svg className="w-3 h-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14" />
                            </svg>
                        </button>
                        <button
                            onClick={goToSettings}
                            className={`text-sm ${statusConfig.subtext} hover:underline`}
                        >
                            {t('storefront.manage', 'Manage')} →
                        </button>
                    </div>
                </div>
                <div className="text-4xl">🌐</div>
            </div>

            {/* Show warning for non-live states */}
            {status !== 'live' && (
                <div className={`mt-3 pt-3 border-t ${status === 'disabled' ? 'border-red-200' : 'border-yellow-200'}`}>
                    <p className={`text-xs ${statusConfig.subtext}`}>
                        {statusConfig.description}
                    </p>
                </div>
            )}
        </div>
    )
}
