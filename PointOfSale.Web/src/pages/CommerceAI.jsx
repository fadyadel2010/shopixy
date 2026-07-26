import { useState, useEffect, useCallback } from 'react'
import { useTranslation } from 'react-i18next'
import api from '../services/api'
import { useToast } from '../components/Toast'
import { useAuthStore } from '../store/authStore'

/**
 * Commerce AI Control Page
 * Phase 7: Commerce AI Experience Control
 */

export default function CommerceAI() {
    const { t, i18n } = useTranslation()
    const { showSuccess, showError } = useToast()
    const { user } = useAuthStore()
    const isRTL = i18n.language === 'ar'
    const tenantId = user?.tenantId

    const [loading, setLoading] = useState(true)
    const [saving, setSaving] = useState(false)

    // Full store config state
    const [storeConfig, setStoreConfig] = useState(null)
    const [insights, setInsights] = useState(null) // Phase 9

    // Local state for AI config (for immediate preview responsiveness)
    const [aiConfig, setAiConfig] = useState({
        enabled: false,
        widgetStyle: 'minimal',
        widgetPosition: 'bottom-right',
        autoGreetingEnabled: false,
        autoGreetingMessage: '',
        personality: 'friendly'
    })

    // Fetch Store Config
    const fetchConfig = useCallback(async () => {
        if (!tenantId) return
        setLoading(true)
        try {
            const response = await api.get('/admin/store-config')
            if (response.data) {
                setStoreConfig(response.data)
                // Initialize local AI state from response
                if (response.data.commerceAi) {
                    setAiConfig(response.data.commerceAi)
                }
            }
        } catch (error) {
            console.error('Error fetching Store Config:', error)
            showError('Failed to load configuration')
        } finally {
            setLoading(false)
        }
    }, [tenantId, showError])

    // Fetch Insights (Phase 9)
    const fetchInsights = useCallback(async () => {
        if (!tenantId) return
        try {
            const response = await api.get('/admin/commerce-ai/insights')
            if (response.data) {
                setInsights(response.data)
            }
        } catch (error) {
            console.error('Error fetching AI Insights:', error)
            // Silent fail for insights - don't block main UI
        }
    }, [tenantId])

    useEffect(() => {
        fetchConfig()
        fetchInsights()
    }, [fetchConfig, fetchInsights])

    // Save Changes
    const handleSave = async () => {
        if (!storeConfig) return
        setSaving(true)
        try {
            // Construct full update payload
            const payload = {
                ...storeConfig,
                commerceAi: aiConfig
            }

            // Remove read-only fields
            delete payload.id
            delete payload.tenantId
            delete payload.updatedAtUtc
            delete payload.createdAtUtc
            delete payload.themePresetKey
            delete payload.themeDraftOverridesJson
            delete payload.themeLiveOverridesJson

            const response = await api.put('/admin/store-config', payload)

            if (response.data) {
                setStoreConfig(response.data)
                showSuccess(t('common.saved', 'Settings saved successfully'))
            }
        } catch (error) {
            console.error('Error saving Commerce AI settings:', error)
            showError(t('common.saveFailed', 'Failed to save settings'))
        } finally {
            setSaving(false)
        }
    }

    // Helper to update local state
    const updateAiConfig = (key, value) => {
        setAiConfig(prev => ({
            ...prev,
            [key]: value
        }))
    }

    if (loading) {
        return (
            <div className="flex items-center justify-center py-12">
                <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-violet-600"></div>
            </div>
        )
    }

    return (
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8" dir={isRTL ? 'rtl' : 'ltr'}>
            {/* Header */}
            <div className="flex items-start justify-between mb-8">
                <div className="flex items-center gap-4">
                    <div className="w-12 h-12 bg-gradient-to-br from-violet-500 to-purple-600 rounded-xl flex items-center justify-center shadow-lg">
                        <span className="text-2xl">🤖</span>
                    </div>
                    <div>
                        <h1 className="text-2xl font-bold text-gray-900">
                            {t('commerceAi.title', 'Commerce AI Experience')}
                        </h1>
                        <p className="text-gray-500 mt-1">
                            {t('commerceAi.subtitle', 'Customize how the AI assistant interacts with your customers.')}
                        </p>
                    </div>
                </div>
                <button
                    onClick={handleSave}
                    disabled={saving}
                    className="inline-flex items-center px-4 py-2 border border-transparent shadow-sm text-sm font-medium rounded-md text-white bg-violet-600 hover:bg-violet-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-violet-500 disabled:opacity-50"
                >
                    {saving ? t('common.saving', 'Saving...') : t('common.saveChanges', 'Save Changes')}
                </button>
            </div>

            <div className="flex flex-col lg:flex-row gap-8">
                {/* Left Column: Controls */}
                <div className="flex-1 space-y-6">

                    {/* 0. Activity Insights (Phase 9) */}
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                        <div className="bg-white border border-gray-200 rounded-xl p-6 shadow-sm flex flex-col items-center justify-center text-center">
                            <span className="text-4xl mb-2">💬</span>
                            <div className="text-3xl font-bold text-gray-900 mb-1">
                                {insights?.chatOpened || 0}
                            </div>
                            <div className="text-sm font-medium text-gray-500">
                                {t('commerceAi.chatOpened', 'Chat Opened')}
                            </div>
                            <div className="text-xs text-gray-400 mt-2">
                                {t('commerceAi.last30Days', 'Last 30 Days')}
                            </div>
                        </div>

                        <div className="bg-white border border-gray-200 rounded-xl p-6 shadow-sm flex flex-col items-center justify-center text-center">
                            <span className="text-4xl mb-2">🛒</span>
                            <div className="text-3xl font-bold text-gray-900 mb-1">
                                {insights?.draftCreated || 0}
                            </div>
                            <div className="text-sm font-medium text-gray-500">
                                {t('commerceAi.draftOrders', 'Draft Orders')}
                            </div>
                            <div className="text-xs text-green-500 mt-2 font-medium">
                                {t('commerceAi.valueSignal', 'High Value Signal')}
                            </div>
                        </div>
                    </div>

                    {/* 1. Main Toggle */}
                    <div className="bg-white border border-gray-200 rounded-xl p-6 shadow-sm">
                        <div className="flex items-center justify-between">
                            <div>
                                <h3 className="text-lg font-medium text-gray-900">
                                    {t('commerceAi.enableAssistant', 'Enable Shop Assistant')}
                                </h3>
                                <p className="text-sm text-gray-500 mt-1">
                                    {aiConfig.enabled
                                        ? t('commerceAi.activeMessage', 'Assistant is active and visible to customers.')
                                        : t('commerceAi.inactiveMessage', 'Assistant is hidden from the storefront.')}
                                </p>
                            </div>
                            <button
                                onClick={() => updateAiConfig('enabled', !aiConfig.enabled)}
                                className={`relative inline-flex h-8 w-14 items-center rounded-full transition-colors duration-300 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-violet-500 ${aiConfig.enabled ? 'bg-green-500' : 'bg-gray-300'
                                    }`}
                            >
                                <span className={`inline-block h-6 w-6 transform rounded-full bg-white shadow-lg transition-transform duration-300 ${aiConfig.enabled ? 'translate-x-7' : 'translate-x-1'
                                    }`} />
                            </button>
                        </div>
                    </div>

                    {/* Disable overlay if not enabled */}
                    <div className={`space-y-6 transition-opacity duration-300 ${!aiConfig.enabled ? 'opacity-50 pointer-events-none' : ''}`}>

                        {/* 2. Widget Style */}
                        <div className="bg-white border border-gray-200 rounded-xl p-6 shadow-sm">
                            <h3 className="text-lg font-medium text-gray-900 mb-4">
                                {t('commerceAi.widgetStyle', 'Widget Style')}
                            </h3>
                            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                                {[
                                    { id: 'minimal', name: 'Minimal Bubble', icon: '💬' },
                                    { id: 'floating', name: 'Floating Assistant', icon: '🤖' },
                                    { id: 'sales', name: 'Sales Assistant', icon: '🛍️' },
                                    { id: 'compact', name: 'Compact Icon', icon: '✨' }
                                ].map((style) => (
                                    <div
                                        key={style.id}
                                        onClick={() => updateAiConfig('widgetStyle', style.id)}
                                        className={`cursor-pointer border rounded-lg p-4 flex items-center gap-3 transition-all ${aiConfig.widgetStyle === style.id
                                            ? 'border-violet-500 bg-violet-50 ring-1 ring-violet-500'
                                            : 'border-gray-200 hover:border-gray-300'
                                            }`}
                                    >
                                        <span className="text-2xl">{style.icon}</span>
                                        <span className="font-medium text-gray-700">{style.name}</span>
                                        {aiConfig.widgetStyle === style.id && (
                                            <span className="ml-auto text-violet-600">✓</span>
                                        )}
                                    </div>
                                ))}
                            </div>
                        </div>

                        {/* 3. Widget Position */}
                        <div className="bg-white border border-gray-200 rounded-xl p-6 shadow-sm">
                            <h3 className="text-lg font-medium text-gray-900 mb-4">
                                {t('commerceAi.position', 'Position')}
                            </h3>
                            <div className="flex bg-gray-100 p-1 rounded-lg">
                                {[
                                    { id: 'bottom-left', label: 'Bottom Left' },
                                    { id: 'bottom-right', label: 'Bottom Right' },
                                    { id: 'middle-right', label: 'Middle Right' }
                                ].map((pos) => (
                                    <button
                                        key={pos.id}
                                        onClick={() => updateAiConfig('widgetPosition', pos.id)}
                                        className={`flex-1 py-2 px-4 rounded-md text-sm font-medium transition-all ${aiConfig.widgetPosition === pos.id
                                            ? 'bg-white text-gray-900 shadow-sm'
                                            : 'text-gray-500 hover:text-gray-700'
                                            }`}
                                    >
                                        {pos.label}
                                    </button>
                                ))}
                            </div>
                        </div>

                        {/* 4. Automatic Greeting */}
                        <div className="bg-white border border-gray-200 rounded-xl p-6 shadow-sm">
                            <div className="flex items-center justify-between mb-4">
                                <div>
                                    <h3 className="text-lg font-medium text-gray-900">
                                        {t('commerceAi.autoGreeting', 'Automatic Greeting')}
                                    </h3>
                                    <p className="text-sm text-gray-500">
                                        {t('commerceAi.greetingDesc', 'Send a welcome message automatically when the customer opens the store.')}
                                    </p>
                                </div>
                                <button
                                    onClick={() => updateAiConfig('autoGreetingEnabled', !aiConfig.autoGreetingEnabled)}
                                    className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors duration-300 focus:outline-none ${aiConfig.autoGreetingEnabled ? 'bg-violet-600' : 'bg-gray-200'
                                        }`}
                                >
                                    <span className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform duration-300 ${aiConfig.autoGreetingEnabled ? 'translate-x-6' : 'translate-x-1'
                                        }`} />
                                </button>
                            </div>

                            {aiConfig.autoGreetingEnabled && (
                                <div className="mt-4">
                                    <label className="block text-sm font-medium text-gray-700 mb-1">
                                        {t('commerceAi.message', 'Message')}
                                    </label>
                                    <textarea
                                        value={aiConfig.autoGreetingMessage}
                                        onChange={(e) => updateAiConfig('autoGreetingMessage', e.target.value)}
                                        maxLength={120}
                                        rows={3}
                                        className="w-full border-gray-300 rounded-lg shadow-sm focus:ring-violet-500 focus:border-violet-500"
                                        placeholder={t('commerceAi.messagePlaceholder', 'Hi! How can I help you today?')}
                                    />
                                    <p className="text-xs text-gray-500 mt-1 text-right">
                                        {aiConfig.autoGreetingMessage.length}/120
                                    </p>
                                </div>
                            )}
                        </div>

                        {/* 5. Assistant Personality */}
                        <div className="bg-white border border-gray-200 rounded-xl p-6 shadow-sm">
                            <h3 className="text-lg font-medium text-gray-900 mb-4">
                                {t('commerceAi.personality', 'Assistant Personality')}
                            </h3>
                            <div className="space-y-3">
                                {[
                                    { id: 'friendly', name: 'Friendly', desc: 'Warm, casual, and helpful.' },
                                    { id: 'professional', name: 'Professional', desc: 'Formal, concise, and efficient.' },
                                    { id: 'sales-focused', name: 'Sales-focused', desc: 'Proactive in suggesting products and deals.' }
                                ].map((p) => (
                                    <label key={p.id} className="flex items-start gap-3 p-3 border rounded-lg cursor-pointer hover:bg-gray-50 transition-colors">
                                        <input
                                            type="radio"
                                            name="personality"
                                            value={p.id}
                                            checked={aiConfig.personality === p.id}
                                            onChange={() => updateAiConfig('personality', p.id)}
                                            className="mt-1 h-4 w-4 text-violet-600 focus:ring-violet-500 border-gray-300"
                                        />
                                        <div>
                                            <span className="block text-sm font-medium text-gray-900">{p.name}</span>
                                            <span className="block text-xs text-gray-500">{p.desc}</span>
                                        </div>
                                    </label>
                                ))}
                            </div>
                        </div>

                    </div>
                </div>

                {/* Right Column: Live Preview */}
                <div className="w-full lg:w-96 flex-shrink-0">
                    <div className="sticky top-8 space-y-4">
                        <div className="bg-gray-900 rounded-2xl p-1 shadow-2xl relative overflow-hidden aspect-[9/16] border-4 border-gray-800">
                            {/* Mock Storefront Background */}
                            <div className="absolute inset-0 bg-white">
                                <div className="h-14 bg-gray-50 border-b flex items-center justify-center">
                                    <div className="w-24 h-4 bg-gray-200 rounded"></div>
                                </div>
                                <div className="p-4 space-y-4">
                                    <div className="h-32 bg-gray-100 rounded-lg"></div>
                                    <div className="grid grid-cols-2 gap-2">
                                        <div className="h-24 bg-gray-100 rounded-lg"></div>
                                        <div className="h-24 bg-gray-100 rounded-lg"></div>
                                    </div>
                                </div>
                            </div>

                            {/* Mock Chat Widget */}
                            {aiConfig.enabled && (
                                <div className={`absolute p-4 transition-all duration-300 ${aiConfig.widgetPosition === 'bottom-left' ? 'bottom-0 left-0' :
                                    aiConfig.widgetPosition === 'middle-right' ? 'bottom-1/2 right-0 translate-y-1/2' :
                                        'bottom-0 right-0' /* bottom-right default */
                                    }`}>

                                    {/* Expanded Preview (Fake) */}
                                    <div className="flex flex-col items-end gap-2">
                                        {/* Auto Greeting Bubble */}
                                        {aiConfig.autoGreetingEnabled && aiConfig.autoGreetingMessage && (
                                            <div className="bg-white p-3 rounded-lg rounded-br-none shadow-lg max-w-[200px] text-sm text-gray-800 mb-2 border border-gray-100 animate-fade-in-up">
                                                {aiConfig.autoGreetingMessage}
                                            </div>
                                        )}

                                        {/* Widget Trigger */}
                                        <div className={`
                                            flex items-center justify-center shadow-lg cursor-pointer hover:scale-105 transition-transform
                                            ${aiConfig.widgetStyle === 'minimal' ? 'w-14 h-14 rounded-full bg-violet-600 text-white' : ''}
                                            ${aiConfig.widgetStyle === 'floating' ? 'px-4 h-12 rounded-full bg-violet-600 text-white gap-2' : ''}
                                            ${aiConfig.widgetStyle === 'sales' ? 'w-16 h-16 rounded-full bg-gradient-to-r from-pink-500 to-violet-500 text-white' : ''}
                                            ${aiConfig.widgetStyle === 'compact' ? 'w-10 h-10 rounded-lg bg-gray-900 text-white' : ''}
                                        `}>
                                            <span className="text-xl">
                                                {aiConfig.widgetStyle === 'minimal' && '💬'}
                                                {aiConfig.widgetStyle === 'floating' && (
                                                    <><span>Chat</span> <span>Now</span></>
                                                )}
                                                {aiConfig.widgetStyle === 'sales' && '🛍️'}
                                                {aiConfig.widgetStyle === 'compact' && '✨'}
                                            </span>
                                        </div>
                                    </div>
                                </div>
                            )}

                            {/* Preview Label overlay */}
                            <div className="absolute top-2 left-1/2 transform -translate-x-1/2 bg-black/50 text-white text-[10px] px-2 py-0.5 rounded-full backdrop-blur-sm">
                                Live Preview
                            </div>
                        </div>
                        <p className="text-center text-sm text-gray-500">
                            Updates in real-time. Try changing the settings!
                        </p>
                    </div>
                </div>
            </div>
        </div>
    )
}
