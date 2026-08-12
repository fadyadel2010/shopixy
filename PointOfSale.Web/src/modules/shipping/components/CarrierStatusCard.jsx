/**
 * Carrier Status Card Component
 * Phase: Unified Shipping Configuration
 * 
 * Displays carrier integration status with:
 * - Connection status indicator
 * - Configure credentials action
 * - Test connection action
 * 
 * READ-ONLY STATUS DISPLAY
 * No API key entry here - just status visibility
 */

import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
    CheckCircle, AlertCircle, XCircle, Settings, RefreshCw,
    ChevronDown, ChevronUp, Shield, Truck, Package
} from 'lucide-react'
import { getCarrierInfo, useCarrierActions } from '../hooks/useShippingSetupApi'

export default function CarrierStatusCard({ carrier, onConfigure, onRefresh }) {
    const { t } = useTranslation()
    const [expanded, setExpanded] = useState(false)
    const [validating, setValidating] = useState(false)
    const [validationResult, setValidationResult] = useState(null)
    const { validateCredentials } = useCarrierActions()

    const info = getCarrierInfo(carrier.carrierCode)

    // Determine connection status
    const getConnectionStatus = () => {
        // SPECIAL CASE: Manual Fulfillment is always available - no configuration needed
        if (carrier.carrierCode === 'manual') {
            return {
                status: 'always_available',
                label: t('shippingSetup.alwaysAvailable', 'Always Available'),
                color: 'green',
                icon: CheckCircle
            }
        }

        if (!carrier.isConfigured) {
            if (['Unavailable', 'NotCertified'].includes(carrier.productionCapability)) {
                return {
                    status: 'unavailable',
                    label: carrier.productionCapability,
                    color: 'gray',
                    icon: AlertCircle
                }
            }

            return {
                status: 'not_configured',
                label: t('shippingSetup.notConfigured', 'Not Configured'),
                color: 'gray',
                icon: AlertCircle
            }
        }
        if (carrier.lastValidationSucceeded === true) {
            return {
                status: 'connected',
                label: t('shippingSetup.connected', 'Connected'),
                color: 'green',
                icon: CheckCircle
            }
        }
        if (carrier.lastValidationSucceeded === false) {
            return {
                status: 'failed',
                label: t('shippingSetup.connectionFailed', 'Connection Failed'),
                color: 'red',
                icon: XCircle
            }
        }
        return {
            status: 'pending',
            label: t('shippingSetup.pendingValidation', 'Pending Validation'),
            color: 'yellow',
            icon: AlertCircle
        }
    }

    const connectionStatus = getConnectionStatus()
    const StatusIcon = connectionStatus.icon

    const handleTestConnection = async () => {
        if (!carrier.isConfigured || carrier.connectionTestSupported === false) return

        setValidating(true)
        setValidationResult(null)

        const result = await validateCredentials(carrier.carrierCode)

        setValidationResult(result)
        setValidating(false)

        if (result.success) {
            onRefresh?.()
        }
    }

    const colorClasses = {
        green: 'bg-green-100 text-green-700 border-green-200',
        red: 'bg-red-100 text-red-700 border-red-200',
        yellow: 'bg-yellow-100 text-yellow-700 border-yellow-200',
        gray: 'bg-gray-100 text-gray-600 border-gray-200'
    }

    return (
        <div className={`rounded-xl border-2 overflow-hidden transition-all duration-200 ${info.borderClass} ${info.bgClass}`}>
            {/* Card Header */}
            <div className="p-4">
                <div className="flex items-start justify-between">
                    <div className="flex items-center gap-3">
                        <div className="text-3xl">{info.logo}</div>
                        <div>
                            <h3 className={`font-bold text-lg ${info.textClass}`}>{info.name}</h3>
                            <p className="text-sm text-gray-500">{info.region}</p>
                        </div>
                    </div>

                    {/* Status Badge */}
                    <div className={`flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-semibold border ${colorClasses[connectionStatus.color]}`}>
                        <StatusIcon size={14} />
                        {connectionStatus.label}
                    </div>
                </div>

                {/* Capabilities Indicators */}
                <div className="flex flex-wrap gap-2 mt-4">
                    {carrier.supportsCod && (
                        <span className="inline-flex items-center gap-1 px-2 py-0.5 bg-white rounded text-xs text-gray-600 border border-gray-200">
                            <Package size={12} />
                            COD
                        </span>
                    )}
                    {carrier.supportsTracking && (
                        <span className="inline-flex items-center gap-1 px-2 py-0.5 bg-white rounded text-xs text-gray-600 border border-gray-200">
                            <Truck size={12} />
                            {t('shippingSetup.tracking', 'Tracking')}
                        </span>
                    )}
                    {carrier.supportsWebhooks && (
                        <span className="inline-flex items-center gap-1 px-2 py-0.5 bg-white rounded text-xs text-gray-600 border border-gray-200">
                            <Shield size={12} />
                            {t('shippingSetup.webhooks', 'Webhooks')}
                        </span>
                    )}
                </div>

                {/* Action Buttons */}
                <div className="flex gap-2 mt-4">
                    <button
                        onClick={() => onConfigure?.(carrier)}
                        className="flex-1 flex items-center justify-center gap-2 px-3 py-2 bg-white border border-gray-200 rounded-lg text-sm font-medium text-gray-700 hover:bg-gray-50 transition-colors"
                    >
                        <Settings size={16} />
                        {carrier.carrierCode === 'manual'
                            ? t('shippingSetup.learnMore', 'Learn More')
                            : t('shippingSetup.configure', 'Configure')
                        }
                    </button>

                    {/* Test button - only for non-manual carriers that are configured */}
                    {carrier.isConfigured && carrier.carrierCode !== 'manual' && carrier.connectionTestSupported !== false && !['Unavailable', 'NotCertified'].includes(carrier.productionCapability) && (
                        <button
                            onClick={handleTestConnection}
                            disabled={validating}
                            className="flex items-center justify-center gap-2 px-3 py-2 bg-white border border-gray-200 rounded-lg text-sm font-medium text-gray-700 hover:bg-gray-50 transition-colors disabled:opacity-50"
                        >
                            <RefreshCw size={16} className={validating ? 'animate-spin' : ''} />
                            {validating
                                ? t('common.testing', 'Testing...')
                                : carrier.connectionTestMode === 'LocalOnly'
                                    ? t('shippingSetup.validateLocally', 'Validate')
                                    : t('shippingSetup.testConnection', 'Test')}
                        </button>
                    )}
                </div>

                {/* Validation Result */}
                {validationResult && (
                    <div className={`mt-3 p-2 rounded-lg text-sm ${validationResult.success ? 'bg-green-100 text-green-700' : 'bg-red-100 text-red-700'}`}>
                        {validationResult.success
                            ? t('shippingSetup.validationSuccess', '✓ Connection successful')
                            : `✗ ${validationResult.error || t('shippingSetup.validationFailed', 'Connection failed')}`
                        }
                    </div>
                )}
            </div>

            {/* Expandable Details */}
            {carrier.isConfigured && (
                <>
                    <button
                        onClick={() => setExpanded(!expanded)}
                        className="w-full flex items-center justify-center gap-1 py-2 bg-white/50 border-t border-gray-200/50 text-sm text-gray-500 hover:bg-white/80 transition-colors"
                    >
                        {expanded ? <ChevronUp size={16} /> : <ChevronDown size={16} />}
                        {expanded ? t('common.showLess', 'Show Less') : t('common.showMore', 'Show More')}
                    </button>

                    {expanded && (
                        <div className="p-4 bg-white/50 border-t border-gray-200/50 space-y-2">
                            <div className="flex justify-between text-sm">
                                <span className="text-gray-500">{t('shippingSetup.defaultService', 'Default Service')}</span>
                                <span className="font-medium text-gray-700">{carrier.defaultServiceLevel || 'standard'}</span>
                            </div>
                            {carrier.lastValidatedAtUtc && (
                                <div className="flex justify-between text-sm">
                                    <span className="text-gray-500">{t('shippingSetup.lastValidated', 'Last Validated')}</span>
                                    <span className="font-medium text-gray-700">
                                        {new Date(carrier.lastValidatedAtUtc).toLocaleDateString()}
                                    </span>
                                </div>
                            )}
                            {carrier.useSandbox && (
                                <div className="flex items-center gap-1 text-sm text-amber-600">
                                    <AlertCircle size={14} />
                                    {t('shippingSetup.sandboxMode', 'Sandbox Mode Active')}
                                </div>
                            )}
                        </div>
                    )}
                </>
            )}
        </div>
    )
}
