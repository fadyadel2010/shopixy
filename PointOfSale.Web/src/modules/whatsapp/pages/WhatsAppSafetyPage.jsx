import React, { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import KillSwitchToggle from '../components/KillSwitchToggle'
import { useWhatsAppApi } from '../hooks/useWhatsAppApi'
import { useToast } from '../../../components/Toast'
import { ShieldCheck, ShieldAlert } from 'lucide-react'

export default function WhatsAppSafetyPage() {
    const { t } = useTranslation()
    const { getKillSwitchStatus, activateKillSwitch, deactivateKillSwitch } = useWhatsAppApi()
    const { showSuccess, showError } = useToast()
    const [status, setStatus] = useState({ isKilled: false, reason: null, activatedAt: null, activatedBy: null })
    const [refreshKey, setRefreshKey] = useState(0)

    const fetchStatus = useCallback(async () => {
        try {
            const data = await getKillSwitchStatus()
            setStatus(data)
        } catch (err) {
            console.error('Failed to fetch kill switch status:', err)
            showError(err.message || t('whatsapp.safety.messages.statusFetchFailed'))
        }
    }, [getKillSwitchStatus, showError, t])

    useEffect(() => {
        fetchStatus()
    }, [fetchStatus, refreshKey])

    const handleActivate = async (reason) => {
        try {
            await activateKillSwitch(reason)
            showSuccess(t('whatsapp.safety.messages.activated'))
            setRefreshKey(k => k + 1)
        } catch (err) {
            showError(err.message)
        }
    }

    const handleDeactivate = async () => {
        try {
            await deactivateKillSwitch()
            showSuccess(t('whatsapp.safety.messages.deactivated'))
            setRefreshKey(k => k + 1)
        } catch (err) {
            showError(err.message)
        }
    }

    return (
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
            <div className="sm:flex sm:items-center mb-8">
                <div className="sm:flex-auto">
                    <h1 className="text-2xl font-semibold text-gray-900">{t('whatsapp.safety.title')}</h1>
                    <p className="mt-2 text-sm text-gray-700">
                        {t('whatsapp.safety.subtitle')}
                    </p>
                </div>
            </div>

            <div className="space-y-8">
                {/* Status Banner */}
                <div className={`rounded-md p-4 ${status.isKilled ? 'bg-red-50 border-l-4 border-red-400' : 'bg-green-50 border-l-4 border-green-400'}`}>
                    <div className="flex">
                        <div className="flex-shrink-0">
                            {status.isKilled ? (
                                <ShieldAlert className="h-5 w-5 text-red-400" aria-hidden="true" />
                            ) : (
                                <ShieldCheck className="h-5 w-5 text-green-400" aria-hidden="true" />
                            )}
                        </div>
                        <div className="ml-3">
                            <h3 className={`text-sm font-medium ${status.isKilled ? 'text-red-800' : 'text-green-800'}`}>
                                {t('whatsapp.safety.systemStatus')}: {status.isKilled ? t('whatsapp.safety.messagingHalted') : t('whatsapp.safety.operational')}
                            </h3>
                            {status.isKilled && (
                                <div className="mt-2 text-sm text-red-700">
                                    <p>{t('whatsapp.safety.reasonLabel')}: {status.reason}</p>
                                    <p>
                                        {t('whatsapp.safety.activatedAt')}: {status.activatedAt ? new Date(status.activatedAt).toLocaleString() : t('common.none')}
                                    </p>
                                </div>
                            )}
                        </div>
                    </div>
                </div>

                {/* Kill Switch Control */}
                <KillSwitchToggle
                    isKilled={status.isKilled}
                    onActivate={handleActivate}
                    onDeactivate={handleDeactivate}
                />

                {/* Audit Log Hint */}
                <div className="bg-white shadow sm:rounded-lg overflow-hidden">
                    <div className="px-4 py-5 sm:px-6 bg-gray-50 border-b border-gray-200">
                        <h3 className="text-lg leading-6 font-medium text-gray-900">{t('whatsapp.safety.auditLogTitle')}</h3>
                    </div>
                    <div className="p-6 text-center text-gray-500 text-sm">
                        {t('whatsapp.safety.auditLogPrefix')}{' '}
                        <a href="/ecommerce/whatsapp/logs" className="text-indigo-600 hover:text-indigo-900">
                            {t('whatsapp.safety.auditLogLink')}
                        </a>{' '}
                        {t('whatsapp.safety.auditLogSuffix')}
                    </div>
                </div>
            </div>
        </div>
    )
}
