import React, { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { AlertTriangle, CheckCircle, Clock, MessageSquare, RefreshCw, Settings, Shield, ShoppingCart, TrendingUp } from 'lucide-react'
import { useToast } from '../components/Toast'
import api from '../services/api'

const DEFAULT_FORM = {
    enableAbandonedRecovery: false,
    firstMessageDelayMinutes: 60,
    secondMessageDelayMinutes: 1440,
    workingHoursFrom: '09:00',
    workingHoursTo: '21:00'
}

const READINESS_CODES = [
    'recovery_disabled',
    'whatsapp_not_configured',
    'whatsapp_account_disabled',
    'whatsapp_disconnected',
    'native_recovery_disabled',
    'native_database_unavailable',
    'native_scheduler_unavailable',
    'native_dispatcher_unavailable'
]

const ACCOUNT_READINESS_CODES = new Set([
    'whatsapp_not_configured',
    'whatsapp_account_disabled',
    'whatsapp_disconnected'
])

export default function AbandonedCartRecovery() {
    const { t } = useTranslation()
    const { showSuccess, showError } = useToast()
    const [loading, setLoading] = useState(true)
    const [saving, setSaving] = useState(false)
    const [settings, setSettings] = useState(null)
    const [eligibility, setEligibility] = useState(null)
    const [stats, setStats] = useState(null)
    const [configurationError, setConfigurationError] = useState(false)
    const [statsError, setStatsError] = useState(false)
    const [formData, setFormData] = useState(DEFAULT_FORM)

    const load = async () => {
        setLoading(true)
        setConfigurationError(false)
        setStatsError(false)

        const [settingsResult, eligibilityResult, statsResult] = await Promise.allSettled([
            api.get('/admin/ecommerce/abandoned-cart-recovery/settings'),
            api.get('/admin/ecommerce/abandoned-cart-recovery/eligibility'),
            api.get('/admin/ecommerce/abandoned-cart-recovery/stats')
        ])

        if (settingsResult.status === 'fulfilled') {
            const data = settingsResult.value.data
            setSettings(data)
            setFormData({
                enableAbandonedRecovery: data.enableAbandonedRecovery ?? false,
                firstMessageDelayMinutes: data.firstMessageDelayMinutes ?? 60,
                secondMessageDelayMinutes: data.secondMessageDelayMinutes ?? 1440,
                workingHoursFrom: data.workingHoursFrom ?? '09:00',
                workingHoursTo: data.workingHoursTo ?? '21:00'
            })
        } else {
            setConfigurationError(true)
        }

        if (eligibilityResult.status === 'fulfilled') {
            setEligibility(eligibilityResult.value.data)
        } else {
            setEligibility(null)
            setConfigurationError(true)
        }

        if (statsResult.status === 'fulfilled') {
            setStats(statsResult.value.data)
        } else {
            setStats(null)
            setStatsError(true)
        }

        setLoading(false)
    }

    useEffect(() => {
        void load()
    }, [])

    const dirty = settings !== null && (
        formData.enableAbandonedRecovery !== settings.enableAbandonedRecovery ||
        formData.firstMessageDelayMinutes !== settings.firstMessageDelayMinutes ||
        formData.secondMessageDelayMinutes !== settings.secondMessageDelayMinutes ||
        formData.workingHoursFrom !== settings.workingHoursFrom ||
        formData.workingHoursTo !== settings.workingHoursTo
    )

    useEffect(() => {
        const warnUnsaved = event => {
            if (!dirty) return
            event.preventDefault()
            event.returnValue = ''
        }
        window.addEventListener('beforeunload', warnUnsaved)
        return () => window.removeEventListener('beforeunload', warnUnsaved)
    }, [dirty])

    const recoveryPrerequisitesReady = Boolean(
        eligibility?.hasWhatsAppAccount &&
        eligibility?.whatsAppAccountEnabled &&
        eligibility?.whatsAppAccountConnected &&
        eligibility?.nativeSchedulerOperational &&
        eligibility?.nativeDispatcherOperational
    )
    const operational = Boolean(settings?.enableAbandonedRecovery && eligibility?.isEligible)
    const timeZoneId = eligibility?.timeZoneId || settings?.timeZoneId || 'UTC'

    const derivedReadinessCode = !eligibility?.hasWhatsAppAccount
            ? 'whatsapp_not_configured'
            : !eligibility?.whatsAppAccountEnabled
                ? 'whatsapp_account_disabled'
                : !eligibility?.whatsAppAccountConnected
                    ? 'whatsapp_disconnected'
                    : !eligibility?.nativeSchedulerOperational
                        ? 'native_scheduler_unavailable'
                        : !eligibility?.nativeDispatcherOperational
                            ? 'native_dispatcher_unavailable'
                            : !eligibility?.automationEnabled
                                ? 'recovery_disabled'
                                : 'unavailable'
    const readinessCode = READINESS_CODES.includes(eligibility?.ineligibilityCode)
        ? eligibility.ineligibilityCode
        : derivedReadinessCode
    const showAccountSettingsLink = ACCOUNT_READINESS_CODES.has(readinessCode)
    const showEnableRecoveryLink = readinessCode === 'recovery_disabled'

    const handleSave = async () => {
        if (formData.workingHoursFrom === formData.workingHoursTo) {
            showError(t('AbandonedCartRecovery.messages.invalidWorkingHours'))
            return
        }
        if (formData.enableAbandonedRecovery && !recoveryPrerequisitesReady) {
            showError(t(`AbandonedCartRecovery.readiness.${readinessCode}`))
            return
        }

        setSaving(true)
        try {
            await api.put('/admin/ecommerce/abandoned-cart-recovery/settings', formData)
            showSuccess(t('AbandonedCartRecovery.messages.saveSuccess'))
            await load()
        } catch (error) {
            const code = error.response?.data?.code
            showError(READINESS_CODES.includes(code)
                ? t(`AbandonedCartRecovery.readiness.${code}`)
                : error.response?.data?.details || error.response?.data?.error || t('AbandonedCartRecovery.messages.saveError'))
        } finally {
            setSaving(false)
        }
    }

    const statusItems = [
        { ok: !configurationError && settings !== null, label: t(settings?.isPersisted ? 'AbandonedCartRecovery.status.configurationSaved' : 'AbandonedCartRecovery.status.configurationNotSaved') },
        { ok: Boolean(eligibility?.hasWhatsAppAccount && eligibility?.whatsAppAccountEnabled), label: t('AbandonedCartRecovery.status.accountEnabled') },
        { ok: Boolean(eligibility?.whatsAppAccountConnected), label: t('AbandonedCartRecovery.status.accountConnected') },
        { ok: Boolean(settings?.enableAbandonedRecovery), label: t('AbandonedCartRecovery.status.recoveryEnabled') },
        { ok: Boolean(eligibility?.nativeSchedulerOperational && eligibility?.nativeDispatcherOperational), label: t('AbandonedCartRecovery.status.nativeRuntime') },
        { ok: operational, label: t('AbandonedCartRecovery.status.operational') }
    ]

    return (
        <div className="space-y-6" aria-busy={loading}>
            <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
                <div>
                    <h1 className="flex items-center gap-3 text-2xl font-bold text-gray-900">
                        <span className="flex h-10 w-10 items-center justify-center rounded-xl bg-emerald-600 shadow-lg">
                            <ShoppingCart className="h-5 w-5 text-white" aria-hidden="true" />
                        </span>
                        {t('AbandonedCartRecovery.header.title')}
                    </h1>
                    <p className="mt-1 text-sm text-gray-500">{t('AbandonedCartRecovery.header.subtitle')}</p>
                </div>
                <button
                    type="button"
                    onClick={() => void load()}
                    aria-label={t('AbandonedCartRecovery.actions.refresh')}
                    className="inline-flex min-h-11 items-center justify-center gap-2 rounded-lg border border-gray-300 bg-white px-4 text-sm font-medium text-gray-700 shadow-sm hover:bg-gray-50"
                >
                    <RefreshCw className={`h-4 w-4 ${loading ? 'animate-spin' : ''}`} aria-hidden="true" />
                    {t('AbandonedCartRecovery.actions.refresh')}
                </button>
            </div>

            {configurationError && (
                <div role="alert" className="rounded-xl border border-red-200 bg-red-50 p-4 text-red-800">
                    <p className="font-medium">{t('AbandonedCartRecovery.errors.configurationTitle')}</p>
                    <p className="mt-1 text-sm">{t('AbandonedCartRecovery.errors.configurationDescription')}</p>
                </div>
            )}

            <section aria-labelledby="recovery-status-title" className="rounded-2xl border border-slate-200 bg-slate-900 p-5 text-white shadow-sm sm:p-6">
                <div className="flex items-center gap-3">
                    <Shield className="h-6 w-6 text-emerald-300" aria-hidden="true" />
                    <div>
                        <h2 id="recovery-status-title" className="text-lg font-semibold">{t('AbandonedCartRecovery.status.title')}</h2>
                        <p className="text-sm text-slate-300">{t('AbandonedCartRecovery.status.timeZone', { timeZone: timeZoneId })}</p>
                    </div>
                </div>
                <div className="mt-5 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
                    {statusItems.map(item => (
                        <div key={item.label} className="flex items-center gap-2 rounded-lg bg-white/10 px-3 py-3 text-sm">
                            {item.ok
                                ? <CheckCircle className="h-4 w-4 shrink-0 text-emerald-300" aria-hidden="true" />
                                : <AlertTriangle className="h-4 w-4 shrink-0 text-amber-300" aria-hidden="true" />}
                            <span>{item.label}</span>
                        </div>
                    ))}
                </div>
            </section>

            {!operational && !loading && !configurationError && eligibility && (
                <div role="status" className="rounded-xl border-s-4 border-amber-400 bg-amber-50 p-4 text-amber-900">
                    <div className="flex items-start gap-3">
                        <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0" aria-hidden="true" />
                        <div>
                            <h2 className="font-medium">{t('AbandonedCartRecovery.connection.requiredTitle')}</h2>
                            <p className="mt-1 text-sm">{t(`AbandonedCartRecovery.readiness.${readinessCode}`)}</p>
                            {showAccountSettingsLink && (
                                <a href="/ecommerce/whatsapp" className="mt-2 inline-block text-sm font-medium underline">{t('AbandonedCartRecovery.connection.settingsLink')}</a>
                            )}
                            {showEnableRecoveryLink && (
                                <a href="#enable-recovery" className="mt-2 inline-block text-sm font-medium underline">{t('AbandonedCartRecovery.connection.enableLink')}</a>
                            )}
                        </div>
                    </div>
                </div>
            )}

            <section aria-labelledby="recovery-stats-title">
                <h2 id="recovery-stats-title" className="sr-only">{t('AbandonedCartRecovery.stats.title')}</h2>
                {statsError ? (
                    <div role="alert" className="rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-800">
                        {t('AbandonedCartRecovery.errors.statsUnavailable')}
                    </div>
                ) : stats && (
                    <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
                        {[
                            [ShoppingCart, 'abandonedCarts', stats.totalAbandoned ?? 0],
                            [MessageSquare, 'messagesSent', stats.messagesSent ?? 0],
                            [CheckCircle, 'recovered', stats.recovered ?? 0],
                            [TrendingUp, 'recoveryRate', `${stats.recoveryRate ?? 0}%`]
                        ].map(([Icon, key, value]) => (
                            <div key={key} className="rounded-xl border border-gray-100 bg-white p-4 shadow-sm sm:p-5">
                                <Icon className="mb-3 h-5 w-5 text-emerald-600" aria-hidden="true" />
                                <p className="text-xs text-gray-500 sm:text-sm">{t(`AbandonedCartRecovery.stats.${key}`)}</p>
                                <p className="mt-1 text-2xl font-bold text-gray-900">{value}</p>
                            </div>
                        ))}
                    </div>
                )}
            </section>

            <section aria-labelledby="recovery-settings-title" className="rounded-xl border border-gray-100 bg-white shadow-sm">
                <div className="border-b border-gray-100 p-5 sm:p-6">
                    <div className="flex items-center gap-3">
                        <Settings className="h-5 w-5 text-indigo-600" aria-hidden="true" />
                        <div>
                            <h2 id="recovery-settings-title" className="text-lg font-semibold text-gray-900">{t('AbandonedCartRecovery.settings.title')}</h2>
                            <p className="text-sm text-gray-500">{t('AbandonedCartRecovery.settings.subtitle')}</p>
                        </div>
                    </div>
                </div>

                <div className="space-y-6 p-5 sm:p-6">
                    <div className="flex flex-col gap-4 rounded-xl bg-gray-50 p-4 sm:flex-row sm:items-center sm:justify-between">
                        <div>
                            <label htmlFor="enable-recovery" className="font-medium text-gray-900">{t('AbandonedCartRecovery.settings.enableTitle')}</label>
                            <p className="mt-1 text-sm text-gray-500">{t('AbandonedCartRecovery.settings.enableDescription')}</p>
                        </div>
                        <input
                            id="enable-recovery"
                            type="checkbox"
                            checked={formData.enableAbandonedRecovery}
                            disabled={loading || (!formData.enableAbandonedRecovery && !recoveryPrerequisitesReady)}
                            onChange={event => setFormData(previous => ({ ...previous, enableAbandonedRecovery: event.target.checked }))}
                            className="h-6 w-6 rounded border-gray-300 text-emerald-600 focus:ring-emerald-500"
                        />
                    </div>

                    <p className="rounded-lg border border-indigo-100 bg-indigo-50 p-3 text-sm text-indigo-900">
                        {t('AbandonedCartRecovery.settings.scheduleSummary', {
                            first: t(`AbandonedCartRecovery.duration.${formData.firstMessageDelayMinutes}`),
                            second: formData.secondMessageDelayMinutes === 0
                                ? t('AbandonedCartRecovery.options.disabledNoSecondMessage')
                                : t(`AbandonedCartRecovery.duration.${formData.secondMessageDelayMinutes}`),
                            from: formData.workingHoursFrom,
                            to: formData.workingHoursTo,
                            timeZone: timeZoneId
                        })}
                    </p>

                    <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
                        <div>
                            <label htmlFor="first-delay" className="mb-2 block text-sm font-medium text-gray-700">
                                <Clock className="me-1 inline h-4 w-4" aria-hidden="true" />
                                {t('AbandonedCartRecovery.settings.firstMessageDelay')}
                            </label>
                            <select id="first-delay" value={formData.firstMessageDelayMinutes} onChange={event => setFormData(previous => ({ ...previous, firstMessageDelayMinutes: Number(event.target.value) }))} className="w-full rounded-lg border-gray-300 shadow-sm">
                                {[30, 60, 120, 180, 360, 720, 1440].map(value => <option key={value} value={value}>{t(`AbandonedCartRecovery.duration.${value}`)}</option>)}
                            </select>
                            <p className="mt-1 text-xs text-gray-500">{t('AbandonedCartRecovery.settings.firstMessageHelp')}</p>
                        </div>
                        <div>
                            <label htmlFor="second-delay" className="mb-2 block text-sm font-medium text-gray-700">
                                <Clock className="me-1 inline h-4 w-4" aria-hidden="true" />
                                {t('AbandonedCartRecovery.settings.secondMessageDelay')}
                            </label>
                            <select id="second-delay" value={formData.secondMessageDelayMinutes} onChange={event => setFormData(previous => ({ ...previous, secondMessageDelayMinutes: Number(event.target.value) }))} className="w-full rounded-lg border-gray-300 shadow-sm">
                                <option value={0}>{t('AbandonedCartRecovery.options.disabledNoSecondMessage')}</option>
                                {[720, 1440, 2880, 4320].map(value => <option key={value} value={value}>{t(`AbandonedCartRecovery.duration.${value}`)}</option>)}
                            </select>
                            <p className="mt-1 text-xs text-gray-500">{t('AbandonedCartRecovery.settings.secondMessageHelp')}</p>
                        </div>
                    </div>

                    <fieldset>
                        <legend className="mb-2 text-sm font-medium text-gray-700">{t('AbandonedCartRecovery.settings.workingHoursLabel')}</legend>
                        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                            <label className="text-xs text-gray-500">{t('AbandonedCartRecovery.settings.from')}
                                <input type="time" value={formData.workingHoursFrom} onChange={event => setFormData(previous => ({ ...previous, workingHoursFrom: event.target.value }))} className="mt-1 block min-h-11 w-full rounded-lg border-gray-300 shadow-sm" />
                            </label>
                            <label className="text-xs text-gray-500">{t('AbandonedCartRecovery.settings.to')}
                                <input type="time" value={formData.workingHoursTo} onChange={event => setFormData(previous => ({ ...previous, workingHoursTo: event.target.value }))} className="mt-1 block min-h-11 w-full rounded-lg border-gray-300 shadow-sm" />
                            </label>
                        </div>
                        <p className="mt-2 text-xs text-gray-500">{t('AbandonedCartRecovery.settings.workingHoursHelp', { timeZone: timeZoneId })}</p>
                    </fieldset>

                    <div className="flex flex-col-reverse gap-3 border-t border-gray-100 pt-4 sm:flex-row sm:items-center sm:justify-between">
                        <p aria-live="polite" className="text-sm text-gray-500">{dirty ? t('AbandonedCartRecovery.settings.unsaved') : t('AbandonedCartRecovery.settings.saved')}</p>
                        <button type="button" onClick={() => void handleSave()} disabled={saving || loading || !dirty} className="min-h-11 rounded-lg bg-indigo-600 px-6 text-sm font-medium text-white shadow-sm hover:bg-indigo-700 disabled:cursor-not-allowed disabled:opacity-50">
                            {saving ? t('AbandonedCartRecovery.settings.saving') : t('AbandonedCartRecovery.settings.save')}
                        </button>
                    </div>
                </div>
            </section>
        </div>
    )
}
