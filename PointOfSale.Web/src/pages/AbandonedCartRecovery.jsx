import React, { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { RefreshCw, ShoppingCart, MessageSquare, Settings, AlertTriangle, CheckCircle, Clock, TrendingUp, Zap, Shield } from 'lucide-react'
import { useToast } from '../components/Toast'
import api from '../services/api' // Use configured api client with auth/context interceptors

export default function AbandonedCartRecovery() {
    const { t } = useTranslation()
    const { showSuccess, showError } = useToast()

    // State
    const [loading, setLoading] = useState(false)
    const [settings, setSettings] = useState(null)
    const [whatsAppAccount, setWhatsAppAccount] = useState(null)
    const [stats, setStats] = useState(null)
    const [saving, setSaving] = useState(false)

    // Form state
    const [formData, setFormData] = useState({
        enableAbandonedRecovery: false,
        firstMessageDelayMinutes: 60,
        secondMessageDelayMinutes: 1440,
        workingHoursFrom: '09:00',
        workingHoursTo: '21:00'
    })

    // Fetch settings
    const fetchSettings = async () => {
        setLoading(true)
        try {
            const response = await api.get('/admin/ecommerce/abandoned-cart-recovery/settings')
            const data = response.data
            setSettings(data)
            setFormData({
                enableAbandonedRecovery: data.enableAbandonedRecovery || false,
                firstMessageDelayMinutes: data.firstMessageDelayMinutes || 60,
                secondMessageDelayMinutes: data.secondMessageDelayMinutes || 1440,
                workingHoursFrom: data.workingHoursFrom || '09:00',
                workingHoursTo: data.workingHoursTo || '21:00'
            })
        } catch (err) {
            console.error('Failed to fetch settings:', err)
        } finally {
            setLoading(false)
        }
    }

    // Fetch WhatsApp account status
    const fetchWhatsAppAccount = async () => {
        try {
            const response = await api.get('/admin/whatsapp/accounts')
            const data = response.data
            // Find an account that's enabled and connected
            const connectedAccount = data.find(acc => acc.enabled && acc.status === 'Connected')
            setWhatsAppAccount(connectedAccount || null)
        } catch (err) {
            console.error('Failed to fetch WhatsApp account:', err)
        }
    }

    // Fetch stats
    const fetchStats = async () => {
        try {
            const response = await api.get('/admin/ecommerce/abandoned-cart-recovery/stats')
            setStats(response.data)
        } catch (err) {
            console.error('Failed to fetch stats:', err)
        }
    }

    useEffect(() => {
        fetchSettings()
        fetchWhatsAppAccount()
        fetchStats()
    }, [])

    // Save settings
    const handleSave = async () => {
        if (formData.enableAbandonedRecovery && !whatsAppAccount) {
            showError(t('AbandonedCartRecovery.messages.connectWhatsAppBeforeEnable'))
            return
        }

        setSaving(true)
        try {
            await api.put('/admin/ecommerce/abandoned-cart-recovery/settings', formData)
            showSuccess(t('AbandonedCartRecovery.messages.saveSuccess'))
            fetchSettings()
        } catch (err) {
            showError(err.response?.data?.error || err.message)
        } finally {
            setSaving(false)
        }
    }

    const canEnable = whatsAppAccount !== null

    return (
        <div className="space-y-6">
            {/* Header */}
            <div className="flex justify-between items-center">
                <div>
                    <h1 className="text-2xl font-bold text-gray-900 flex items-center gap-3">
                        <div className="w-10 h-10 bg-gradient-to-br from-green-500 to-emerald-600 rounded-xl flex items-center justify-center shadow-lg">
                            <ShoppingCart className="h-5 w-5 text-white" />
                        </div>{t('AbandonedCartRecovery.header.title')}</h1>
                    <p className="text-sm text-gray-500 mt-1">{t('AbandonedCartRecovery.header.subtitle')}</p>
                </div>
                <button
                    onClick={() => { fetchSettings(); fetchWhatsAppAccount(); fetchStats(); }}
                    className="inline-flex items-center p-2 border border-gray-300 rounded-md shadow-sm text-sm font-medium text-gray-700 bg-white hover:bg-gray-50"
                >
                    <RefreshCw className={`h-5 w-5 ${loading ? 'animate-spin' : ''}`} />
                </button>
            </div>

            {/* Hero Section - Feature Explanation */}
            <div className="bg-gradient-to-br from-emerald-500 via-green-500 to-teal-500 rounded-2xl shadow-xl p-8 text-white relative overflow-hidden">
                <div className="absolute top-0 right-0 w-64 h-64 bg-white/10 rounded-full -mr-32 -mt-32"></div>
                <div className="absolute bottom-0 left-0 w-48 h-48 bg-white/5 rounded-full -ml-24 -mb-24"></div>

                <div className="relative z-10">
                    <div className="flex items-center gap-2 mb-4">
                        <Zap className="h-6 w-6" />
                        <span className="text-lg font-semibold">{t('AbandonedCartRecovery.hero.badge')}</span>
                    </div>

                    <h2 className="text-3xl font-bold mb-4">{t('AbandonedCartRecovery.hero.title')}</h2>

                    <p className="text-lg text-white/90 mb-6 max-w-2xl">
                        {t('AbandonedCartRecovery.hero.descriptionPrefix')}
                        <span className="font-bold">{t('AbandonedCartRecovery.hero.abandonedRate')}</span>
                        {t('AbandonedCartRecovery.hero.descriptionMiddle')}
                        <span className="font-bold">{t('AbandonedCartRecovery.hero.recoveredRate')}</span>
                        {t('AbandonedCartRecovery.hero.descriptionSuffix')}
                    </p>

                    <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                        <div className="bg-white/10 backdrop-blur-sm rounded-xl p-4">
                            <div className="text-3xl font-bold">{'\u23F1'}</div>
                            <h3 className="font-semibold mt-2">{t('AbandonedCartRecovery.hero.cards.smartTiming.title')}</h3>
                            <p className="text-sm text-white/80 mt-1">{t('AbandonedCartRecovery.hero.cards.smartTiming.description')}</p>
                        </div>
                        <div className="bg-white/10 backdrop-blur-sm rounded-xl p-4">
                            <div className="text-3xl font-bold">{'\u{1F4AC}'}</div>
                            <h3 className="font-semibold mt-2">{t('AbandonedCartRecovery.hero.cards.whatsAppDelivery.title')}</h3>
                            <p className="text-sm text-white/80 mt-1">{t('AbandonedCartRecovery.hero.cards.whatsAppDelivery.description')}</p>
                        </div>
                        <div className="bg-white/10 backdrop-blur-sm rounded-xl p-4">
                            <div className="text-3xl font-bold">{'\u{1F6E1}'}</div>
                            <h3 className="font-semibold mt-2">{t('AbandonedCartRecovery.hero.cards.privacyFirst.title')}</h3>
                            <p className="text-sm text-white/80 mt-1">{t('AbandonedCartRecovery.hero.cards.privacyFirst.description')}</p>
                        </div>
                    </div>
                </div>
            </div>

            {/* Stats Cards */}
            {stats && (
                <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
                    <div className="bg-white rounded-xl shadow-sm border border-gray-100 p-5">
                        <div className="flex items-center gap-3">
                            <div className="w-12 h-12 bg-yellow-100 rounded-xl flex items-center justify-center">
                                <ShoppingCart className="h-6 w-6 text-yellow-600" />
                            </div>
                            <div>
                                <p className="text-sm text-gray-500">{t('AbandonedCartRecovery.stats.abandonedCarts')}</p>
                                <p className="text-2xl font-bold text-gray-900">{stats.totalAbandoned || 0}</p>
                            </div>
                        </div>
                    </div>
                    <div className="bg-white rounded-xl shadow-sm border border-gray-100 p-5">
                        <div className="flex items-center gap-3">
                            <div className="w-12 h-12 bg-blue-100 rounded-xl flex items-center justify-center">
                                <MessageSquare className="h-6 w-6 text-blue-600" />
                            </div>
                            <div>
                                <p className="text-sm text-gray-500">{t('AbandonedCartRecovery.stats.messagesSent')}</p>
                                <p className="text-2xl font-bold text-gray-900">{stats.messagesSent || 0}</p>
                            </div>
                        </div>
                    </div>
                    <div className="bg-white rounded-xl shadow-sm border border-gray-100 p-5">
                        <div className="flex items-center gap-3">
                            <div className="w-12 h-12 bg-green-100 rounded-xl flex items-center justify-center">
                                <CheckCircle className="h-6 w-6 text-green-600" />
                            </div>
                            <div>
                                <p className="text-sm text-gray-500">{t('AbandonedCartRecovery.stats.recovered')}</p>
                                <p className="text-2xl font-bold text-gray-900">{stats.recovered || 0}</p>
                            </div>
                        </div>
                    </div>
                    <div className="bg-white rounded-xl shadow-sm border border-gray-100 p-5">
                        <div className="flex items-center gap-3">
                            <div className="w-12 h-12 bg-emerald-100 rounded-xl flex items-center justify-center">
                                <TrendingUp className="h-6 w-6 text-emerald-600" />
                            </div>
                            <div>
                                <p className="text-sm text-gray-500">{t('AbandonedCartRecovery.stats.recoveryRate')}</p>
                                <p className="text-2xl font-bold text-gray-900">{stats.recoveryRate || 0}%</p>
                            </div>
                        </div>
                    </div>
                </div>
            )}

            {/* WhatsApp Connection Status */}
            {!canEnable && (
                <div className="bg-amber-50 border-l-4 border-amber-400 p-4 rounded-r-lg">
                    <div className="flex items-start gap-3">
                        <AlertTriangle className="h-5 w-5 text-amber-500 mt-0.5 flex-shrink-0" />
                        <div>
                            <h3 className="font-medium text-amber-800">{t('AbandonedCartRecovery.connection.requiredTitle')}</h3>
                            <p className="text-sm text-amber-700 mt-1">
                                {t('AbandonedCartRecovery.connection.requiredPrefix')}
                                <a href="/ecommerce/whatsapp" className="underline font-medium hover:text-amber-900">{t('AbandonedCartRecovery.connection.settingsLink')}</a>
                                {t('AbandonedCartRecovery.connection.requiredSuffix')}
                            </p>
                        </div>
                    </div>
                </div>
            )}

            {canEnable && (
                <div className="bg-green-50 border-l-4 border-green-400 p-4 rounded-r-lg">
                    <div className="flex items-start gap-3">
                        <CheckCircle className="h-5 w-5 text-green-500 mt-0.5 flex-shrink-0" />
                        <div>
                            <h3 className="font-medium text-green-800">{t('AbandonedCartRecovery.connection.connectedTitle')}</h3>
                            <p className="text-sm text-green-700 mt-1">{t('AbandonedCartRecovery.connection.connectedDescription', {
                                name: whatsAppAccount?.displayName,
                                phone: whatsAppAccount?.phoneNumber,
                            })}
                            </p>
                        </div>
                    </div>
                </div>
            )}

            {/* Settings Card */}
            <div className="bg-white rounded-xl shadow-sm border border-gray-100">
                <div className="p-6 border-b border-gray-100">
                    <div className="flex items-center gap-3">
                        <div className="w-10 h-10 bg-indigo-100 rounded-lg flex items-center justify-center">
                            <Settings className="h-5 w-5 text-indigo-600" />
                        </div>
                        <div>
                            <h2 className="text-lg font-semibold text-gray-900">{t('AbandonedCartRecovery.settings.title')}</h2>
                            <p className="text-sm text-gray-500">{t('AbandonedCartRecovery.settings.subtitle')}</p>
                        </div>
                    </div>
                </div>

                <div className="p-6 space-y-6">
                    {/* Enable Toggle */}
                    <div className="flex items-center justify-between p-4 bg-gray-50 rounded-xl">
                        <div>
                            <h3 className="font-medium text-gray-900">{t('AbandonedCartRecovery.settings.enableTitle')}</h3>
                            <p className="text-sm text-gray-500 mt-1">{t('AbandonedCartRecovery.settings.enableDescription')}</p>
                        </div>
                        <label className="relative inline-flex items-center cursor-pointer">
                            <input
                                type="checkbox"
                                className="sr-only peer"
                                checked={formData.enableAbandonedRecovery}
                                disabled={!canEnable}
                                onChange={e => setFormData(prev => ({ ...prev, enableAbandonedRecovery: e.target.checked }))}
                            />
                            <div className={`w-14 h-7 rounded-full peer peer-focus:ring-4 transition-colors duration-200
                                ${!canEnable ? 'bg-gray-200 cursor-not-allowed' :
                                    formData.enableAbandonedRecovery ? 'bg-green-500 peer-focus:ring-green-300' : 'bg-gray-300 peer-focus:ring-gray-300'}
                                after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:rounded-full after:h-6 after:w-6 after:transition-all after:shadow-md
                                ${formData.enableAbandonedRecovery ? 'after:translate-x-7' : ''}`}
                            ></div>
                        </label>
                    </div>

                    {/* Timing Settings */}
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-2">
                                <Clock className="inline h-4 w-4 mr-1 text-gray-400" />{t('AbandonedCartRecovery.settings.firstMessageDelay')}</label>
                            <select
                                value={formData.firstMessageDelayMinutes}
                                onChange={e => setFormData(prev => ({ ...prev, firstMessageDelayMinutes: parseInt(e.target.value) }))}
                                className="w-full rounded-lg border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500"
                            >
                                <option value={30}>{t('AbandonedCartRecovery.options.30Minutes')}</option>
                                <option value={60}>{t('AbandonedCartRecovery.options.1Hour')}</option>
                                <option value={120}>{t('AbandonedCartRecovery.options.2Hours')}</option>
                                <option value={180}>{t('AbandonedCartRecovery.options.3Hours')}</option>
                                <option value={360}>{t('AbandonedCartRecovery.options.6Hours')}</option>
                                <option value={720}>{t('AbandonedCartRecovery.options.12Hours')}</option>
                                <option value={1440}>{t('AbandonedCartRecovery.options.24Hours')}</option>
                            </select>
                            <p className="text-xs text-gray-500 mt-1">{t('AbandonedCartRecovery.settings.firstMessageHelp')}</p>
                        </div>

                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-2">
                                <Clock className="inline h-4 w-4 mr-1 text-gray-400" />{t('AbandonedCartRecovery.settings.secondMessageDelay')}</label>
                            <select
                                value={formData.secondMessageDelayMinutes}
                                onChange={e => setFormData(prev => ({ ...prev, secondMessageDelayMinutes: parseInt(e.target.value) }))}
                                className="w-full rounded-lg border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500"
                            >
                                <option value={0}>{t('AbandonedCartRecovery.options.disabledNoSecondMessage')}</option>
                                <option value={720}>{t('AbandonedCartRecovery.options.12HoursAfterFirst')}</option>
                                <option value={1440}>{t('AbandonedCartRecovery.options.24HoursAfterFirst')}</option>
                                <option value={2880}>{t('AbandonedCartRecovery.options.48HoursAfterFirst')}</option>
                                <option value={4320}>{t('AbandonedCartRecovery.options.72HoursAfterFirst')}</option>
                            </select>
                            <p className="text-xs text-gray-500 mt-1">{t('AbandonedCartRecovery.settings.secondMessageHelp')}</p>
                        </div>
                    </div>

                    {/* Working Hours */}
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            <Clock className="inline h-4 w-4 mr-1 text-gray-400" />{t('AbandonedCartRecovery.settings.workingHoursLabel')}</label>
                        <div className="flex items-center gap-4">
                            <div className="flex-1">
                                <label className="text-xs text-gray-500">{t('AbandonedCartRecovery.settings.from')}</label>
                                <input
                                    type="time"
                                    value={formData.workingHoursFrom}
                                    onChange={e => setFormData(prev => ({ ...prev, workingHoursFrom: e.target.value }))}
                                    className="w-full rounded-lg border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500"
                                />
                            </div>
                            <span className="text-gray-400 mt-4">{t('AbandonedCartRecovery.settings.to')}</span>
                            <div className="flex-1">
                                <label className="text-xs text-gray-500">{t('AbandonedCartRecovery.settings.to')}</label>
                                <input
                                    type="time"
                                    value={formData.workingHoursTo}
                                    onChange={e => setFormData(prev => ({ ...prev, workingHoursTo: e.target.value }))}
                                    className="w-full rounded-lg border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500"
                                />
                            </div>
                        </div>
                        <p className="text-xs text-gray-500 mt-1">{t('AbandonedCartRecovery.settings.workingHoursHelp')}</p>
                    </div>

                    {/* Save Button */}
                    <div className="flex justify-end pt-4 border-t border-gray-100">
                        <button
                            onClick={handleSave}
                            disabled={saving}
                            className="inline-flex items-center px-6 py-2.5 border border-transparent text-sm font-medium rounded-lg text-white bg-indigo-600 hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 disabled:opacity-50 disabled:cursor-not-allowed shadow-sm"
                        >
                            {saving ? (
                                <>
                                    <RefreshCw className="animate-spin -ml-1 mr-2 h-4 w-4" />{t('AbandonedCartRecovery.settings.saving')}</>
                            ) : (
                                t('AbandonedCartRecovery.settings.save')
                            )}
                        </button>
                    </div>
                </div>
            </div>

            {/* How It Works */}
            <div className="bg-white rounded-xl shadow-sm border border-gray-100 p-6">
                <h2 className="text-lg font-semibold text-gray-900 mb-6 flex items-center gap-2">
                    <Shield className="h-5 w-5 text-indigo-500" />{t('AbandonedCartRecovery.howItWorks.title')}</h2>

                <div className="grid grid-cols-1 md:grid-cols-4 gap-6">
                    <div className="text-center">
                        <div className="w-16 h-16 bg-indigo-100 rounded-full flex items-center justify-center mx-auto mb-4">
                            <span className="text-2xl">{'\u{1F6D2}'}</span>
                        </div>
                        <h3 className="font-medium text-gray-900 mb-2">{t('AbandonedCartRecovery.howItWorks.step1.title')}</h3>
                        <p className="text-sm text-gray-500">{t('AbandonedCartRecovery.howItWorks.step1.description')}</p>
                    </div>
                    <div className="text-center">
                        <div className="w-16 h-16 bg-yellow-100 rounded-full flex items-center justify-center mx-auto mb-4">
                            <span className="text-2xl">{'\u23F3'}</span>
                        </div>
                        <h3 className="font-medium text-gray-900 mb-2">{t('AbandonedCartRecovery.howItWorks.step2.title')}</h3>
                        <p className="text-sm text-gray-500">{t('AbandonedCartRecovery.howItWorks.step2.description')}</p>
                    </div>
                    <div className="text-center">
                        <div className="w-16 h-16 bg-green-100 rounded-full flex items-center justify-center mx-auto mb-4">
                            <span className="text-2xl">{'\u{1F4F2}'}</span>
                        </div>
                        <h3 className="font-medium text-gray-900 mb-2">{t('AbandonedCartRecovery.howItWorks.step3.title')}</h3>
                        <p className="text-sm text-gray-500">{t('AbandonedCartRecovery.howItWorks.step3.description')}</p>
                    </div>
                    <div className="text-center">
                        <div className="w-16 h-16 bg-emerald-100 rounded-full flex items-center justify-center mx-auto mb-4">
                            <span className="text-2xl">{'\u{1F4B8}'}</span>
                        </div>
                        <h3 className="font-medium text-gray-900 mb-2">{t('AbandonedCartRecovery.howItWorks.step4.title')}</h3>
                        <p className="text-sm text-gray-500">{t('AbandonedCartRecovery.howItWorks.step4.description')}</p>
                    </div>
                </div>
            </div>

            {/* Safety & Privacy Note */}
            <div className="bg-blue-50 border border-blue-200 rounded-xl p-4">
                <div className="flex items-start gap-3">
                    <Shield className="h-5 w-5 text-blue-500 mt-0.5 flex-shrink-0" />
                    <div>
                        <h3 className="font-medium text-blue-900">{t('AbandonedCartRecovery.safety.title')}</h3>
                        <ul className="text-sm text-blue-800 mt-2 space-y-1">
                            <li><strong>{t('AbandonedCartRecovery.safety.rateLimitingLabel')}</strong> {t('AbandonedCartRecovery.safety.rateLimitingDescription')}</li>
                            <li><strong>{t('AbandonedCartRecovery.safety.optOutLabel')}</strong> {t('AbandonedCartRecovery.safety.optOutDescription')}</li>
                            <li><strong>{t('AbandonedCartRecovery.safety.workingHoursLabel')}</strong> {t('AbandonedCartRecovery.safety.workingHoursDescription')}</li>
                            <li><strong>{t('AbandonedCartRecovery.safety.duplicatePreventionLabel')}</strong> {t('AbandonedCartRecovery.safety.duplicatePreventionDescription')}</li>
                        </ul>
                    </div>
                </div>
            </div>
        </div>
    )
}
