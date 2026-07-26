/**
 * Pixel Tracking Configuration Page
 * Phase M1: Pixel Conversion Value Override
 * 
 * Admin page for configuring pixel conversion value overrides.
 */
import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { AlertTriangle, Save, Radio, Eye } from 'lucide-react'
import api from '../../../services/api'

export default function PixelTrackingConfigPage() {
    const { i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'

    const [loading, setLoading] = useState(true)
    const [saving, setSaving] = useState(false)
    const [message, setMessage] = useState(null)
    const [config, setConfig] = useState({
        isEnabled: false,
        strategy: 'PERCENTAGE',
        value: 100,
        appliesTo: []
    })

    const [preview, setPreview] = useState(null)
    const [previewOrderTotal, setPreviewOrderTotal] = useState(600)

    useEffect(() => {
        fetchConfig()
    }, [])

    // Live preview effect
    useEffect(() => {
        const fetchPreview = async () => {
            try {
                const res = await api.get(`/admin/pixel-tracking/preview?orderTotal=${previewOrderTotal}&strategyStr=${config.strategy}&value=${config.value}`)
                setPreview(res.data)
            } catch (err) {
                console.error('Preview failed', err)
            }
        }

        if (config.value) {
            fetchPreview()
        }
    }, [config.strategy, config.value, previewOrderTotal])

    const fetchConfig = async () => {
        try {
            const response = await api.get('/admin/pixel-tracking/config')
            setConfig({
                isEnabled: response.data.isEnabled,
                strategy: response.data.strategy,
                value: response.data.value,
                appliesTo: response.data.appliesTo || []
            })
        } catch (error) {
            console.error('Failed to load pixel configuration', error)
            setMessage({ type: 'error', text: 'Failed to load pixel configuration' })
        } finally {
            setLoading(false)
        }
    }

    const handleSave = async () => {
        setSaving(true)
        try {
            await api.put('/admin/pixel-tracking/config', config)
            setMessage({ type: 'success', text: 'Pixel tracking configuration updated' })
        } catch (error) {
            console.error('Failed to update configuration', error)
            setMessage({ type: 'error', text: 'Failed to update configuration' })
        } finally {
            setSaving(false)
        }
    }

    const handleChange = (field, value) => {
        setConfig(prev => ({
            ...prev,
            [field]: value
        }))
    }

    const handlePlatformChange = (platform) => {
        setConfig(prev => {
            const current = prev.appliesTo || []
            const exists = current.includes(platform)
            return {
                ...prev,
                appliesTo: exists
                    ? current.filter(p => p !== platform)
                    : [...current, platform]
            }
        })
    }

    if (loading) {
        return (
            <div className="max-w-5xl mx-auto p-6">
                <div className="animate-pulse">
                    <div className="h-8 bg-gray-200 rounded w-1/3 mb-4"></div>
                    <div className="h-40 bg-gray-100 rounded-xl"></div>
                </div>
            </div>
        )
    }

    return (
        <div className="max-w-5xl mx-auto p-6">
            {/* Header */}
            <div className="mb-6">
                <h1 className="text-2xl font-bold text-gray-900">
                    {isRTL ? 'إعدادات تتبع البكسل' : 'Pixel Conversion Tracking'}
                </h1>
                <p className="text-gray-600 mt-1">
                    {isRTL
                        ? 'تكوين قيم التحويل المُرسلة لمنصات الإعلانات'
                        : 'Configure conversion values sent to ad platforms'}
                </p>
            </div>

            {/* Warning Banner */}
            <div className="bg-orange-50 border border-orange-200 rounded-xl p-4 mb-6 flex items-start gap-3">
                <AlertTriangle size={20} className="text-orange-600 flex-shrink-0 mt-0.5" />
                <div>
                    <p className="text-sm text-orange-800 font-medium">
                        {isRTL ? 'مهم!' : 'Important!'}
                    </p>
                    <p className="text-sm text-orange-700">
                        {isRTL
                            ? 'التغييرات هنا تؤثر فقط على البيانات المُرسلة لمنصات الإعلانات (Meta، Google، TikTok). قيم الطلبات الفعلية وتقارير الإيرادات والتحليلات الداخلية ستبقى دقيقة 100%.'
                            : 'Changes here affect ONLY data sent to ad platforms (Meta, Google, TikTok). Actual order values, revenue reports, and internal analytics will remain 100% accurate.'}
                    </p>
                </div>
            </div>

            {/* Status Message */}
            {message && (
                <div className={`rounded-xl p-4 mb-6 ${message.type === 'success' ? 'bg-green-50 border border-green-200 text-green-800' : 'bg-red-50 border border-red-200 text-red-800'}`}>
                    {message.text}
                    <button onClick={() => setMessage(null)} className="float-right text-sm underline">Dismiss</button>
                </div>
            )}

            <div className="grid grid-cols-1 lg:grid-cols-5 gap-6">
                {/* Configuration Card */}
                <div className="lg:col-span-3 bg-white rounded-xl border border-gray-200 p-6 shadow-sm">
                    {/* Enable Toggle */}
                    <div className="flex items-center justify-between mb-6 pb-4 border-b border-gray-100">
                        <h2 className="text-lg font-semibold text-gray-900">
                            {isRTL ? 'التكوين' : 'Configuration'}
                        </h2>
                        <label className="flex items-center gap-3 cursor-pointer">
                            <span className={`text-sm font-medium ${config.isEnabled ? 'text-green-600' : 'text-gray-500'}`}>
                                {config.isEnabled
                                    ? (isRTL ? 'مُفعّل' : 'Enabled')
                                    : (isRTL ? 'معطّل' : 'Disabled')}
                            </span>
                            <div
                                className={`w-12 h-6 rounded-full p-1 cursor-pointer transition-colors ${config.isEnabled ? 'bg-green-500' : 'bg-gray-300'}`}
                                onClick={() => handleChange('isEnabled', !config.isEnabled)}
                            >
                                <div className={`w-4 h-4 bg-white rounded-full shadow-sm transform transition-transform ${config.isEnabled ? 'translate-x-6' : 'translate-x-0'}`}></div>
                            </div>
                        </label>
                    </div>

                    {/* Strategy Selection */}
                    <div className="mb-6">
                        <label className="block text-sm font-medium text-gray-700 mb-3">
                            {isRTL ? 'الاستراتيجية' : 'Override Strategy'}
                        </label>
                        <div className="flex gap-4">
                            <label className={`flex-1 p-4 rounded-xl border-2 cursor-pointer transition-all ${config.strategy === 'PERCENTAGE' ? 'border-indigo-500 bg-indigo-50' : 'border-gray-200 hover:border-gray-300'}`}>
                                <input
                                    type="radio"
                                    name="strategy"
                                    value="PERCENTAGE"
                                    checked={config.strategy === 'PERCENTAGE'}
                                    onChange={(e) => handleChange('strategy', e.target.value)}
                                    className="sr-only"
                                />
                                <div className="flex items-center gap-2 mb-1">
                                    <div className={`w-4 h-4 rounded-full border-2 flex items-center justify-center ${config.strategy === 'PERCENTAGE' ? 'border-indigo-500' : 'border-gray-400'}`}>
                                        {config.strategy === 'PERCENTAGE' && <div className="w-2 h-2 bg-indigo-500 rounded-full"></div>}
                                    </div>
                                    <span className="font-medium text-gray-900">{isRTL ? 'نسبة مئوية' : 'Percentage (%)'}</span>
                                </div>
                                <p className="text-xs text-gray-500 ml-6">
                                    {isRTL ? 'إرسال نسبة من قيمة الطلب' : 'Send a percentage of order value'}
                                </p>
                            </label>
                            <label className={`flex-1 p-4 rounded-xl border-2 cursor-pointer transition-all ${config.strategy === 'FIXED' ? 'border-indigo-500 bg-indigo-50' : 'border-gray-200 hover:border-gray-300'}`}>
                                <input
                                    type="radio"
                                    name="strategy"
                                    value="FIXED"
                                    checked={config.strategy === 'FIXED'}
                                    onChange={(e) => handleChange('strategy', e.target.value)}
                                    className="sr-only"
                                />
                                <div className="flex items-center gap-2 mb-1">
                                    <div className={`w-4 h-4 rounded-full border-2 flex items-center justify-center ${config.strategy === 'FIXED' ? 'border-indigo-500' : 'border-gray-400'}`}>
                                        {config.strategy === 'FIXED' && <div className="w-2 h-2 bg-indigo-500 rounded-full"></div>}
                                    </div>
                                    <span className="font-medium text-gray-900">{isRTL ? 'قيمة ثابتة' : 'Fixed Amount'}</span>
                                </div>
                                <p className="text-xs text-gray-500 ml-6">
                                    {isRTL ? 'إرسال قيمة ثابتة لكل طلب' : 'Send a fixed value for every order'}
                                </p>
                            </label>
                        </div>
                    </div>

                    {/* Value Input */}
                    <div className="mb-6">
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            {config.strategy === 'PERCENTAGE'
                                ? (isRTL ? 'النسبة المئوية (0-100)' : 'Percentage Value (0-100)')
                                : (isRTL ? 'القيمة الثابتة' : 'Fixed Amount Value')}
                        </label>
                        <input
                            type="number"
                            value={config.value}
                            onChange={(e) => handleChange('value', parseFloat(e.target.value) || 0)}
                            className="w-full px-4 py-3 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                            min="0"
                            max={config.strategy === 'PERCENTAGE' ? 100 : undefined}
                        />
                        <p className="text-xs text-gray-500 mt-2">
                            {config.strategy === 'PERCENTAGE'
                                ? (isRTL ? 'مثال: 70 تعني إرسال 70% من قيمة الطلب الفعلية' : 'Example: 70 means sending 70% of the actual order value.')
                                : (isRTL ? 'مثال: 10 تعني إرسال 10.00 لكل طلب' : 'Example: 10 means sending 10.00 for every order.')}
                        </p>
                    </div>

                    {/* Platform Selection */}
                    <div className="mb-6">
                        <label className="block text-sm font-medium text-gray-700 mb-3">
                            {isRTL ? 'تطبيق على المنصات' : 'Apply to Platforms'}
                        </label>
                        <div className="flex flex-wrap gap-3">
                            {['Meta', 'Google', 'TikTok'].map((platform) => (
                                <label
                                    key={platform}
                                    className={`flex items-center gap-2 px-4 py-2 rounded-lg border-2 cursor-pointer transition-all ${config.appliesTo.includes(platform)
                                        ? 'border-indigo-500 bg-indigo-50 text-indigo-700'
                                        : 'border-gray-200 hover:border-gray-300'
                                        }`}
                                >
                                    <input
                                        type="checkbox"
                                        checked={config.appliesTo.includes(platform)}
                                        onChange={() => handlePlatformChange(platform)}
                                        className="sr-only"
                                    />
                                    <div className={`w-4 h-4 rounded border-2 flex items-center justify-center ${config.appliesTo.includes(platform) ? 'border-indigo-500 bg-indigo-500' : 'border-gray-400'
                                        }`}>
                                        {config.appliesTo.includes(platform) && (
                                            <svg className="w-3 h-3 text-white" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={3} d="M5 13l4 4L19 7" />
                                            </svg>
                                        )}
                                    </div>
                                    <span className="font-medium">
                                        {platform === 'Meta' ? 'Meta (Facebook/Instagram)' : platform === 'Google' ? 'Google Ads' : 'TikTok Ads'}
                                    </span>
                                </label>
                            ))}
                        </div>
                    </div>

                    {/* Save Button */}
                    <div className="flex justify-end pt-4 border-t border-gray-100">
                        <button
                            onClick={handleSave}
                            disabled={saving}
                            className="flex items-center gap-2 px-6 py-3 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors font-medium"
                        >
                            <Save size={18} />
                            {saving
                                ? (isRTL ? 'جارٍ الحفظ...' : 'Saving...')
                                : (isRTL ? 'حفظ التكوين' : 'Save Configuration')}
                        </button>
                    </div>
                </div>

                {/* Preview Card */}
                <div className="lg:col-span-2 bg-gray-50 rounded-xl border border-gray-200 p-6 shadow-sm">
                    <div className="flex items-center gap-2 mb-4">
                        <Eye size={18} className="text-gray-500" />
                        <h2 className="text-lg font-semibold text-gray-900">
                            {isRTL ? 'معاينة مباشرة' : 'Live Preview'}
                        </h2>
                    </div>
                    <p className="text-sm text-gray-600 mb-4">
                        {isRTL
                            ? 'شاهد كيف سيتم الإبلاغ عن طلب نموذجي لبكسلاتك.'
                            : 'See how a sample order would be reported to your pixels.'}
                    </p>

                    {/* Sample Order Input */}
                    <div className="mb-4">
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            {isRTL ? 'إجمالي الطلب النموذجي' : 'Sample Order Total'}
                        </label>
                        <input
                            type="number"
                            value={previewOrderTotal}
                            onChange={(e) => setPreviewOrderTotal(parseFloat(e.target.value) || 0)}
                            className="w-full px-4 py-2 bg-white border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                        />
                    </div>

                    {/* Preview Result */}
                    {preview && config.isEnabled ? (
                        <div className="bg-white rounded-lg border border-gray-200 p-4">
                            <div className="grid grid-cols-2 gap-4 mb-4">
                                <div>
                                    <span className="text-xs text-gray-500 block mb-1">
                                        {isRTL ? 'القيمة الفعلية' : 'Actual Value'}
                                    </span>
                                    <span className="text-xl font-bold text-gray-900">
                                        {preview.actualOrderTotal?.toFixed(2)}
                                    </span>
                                </div>
                                <div>
                                    <span className="text-xs text-indigo-600 font-medium block mb-1">
                                        {isRTL ? 'المُبلّغ للبكسل' : 'Reported to Pixel'}
                                    </span>
                                    <span className="text-xl font-bold text-indigo-600">
                                        {preview.pixelReportedValue?.toFixed(2)}
                                    </span>
                                </div>
                            </div>
                            <div className="pt-3 border-t border-gray-100">
                                <div className="flex justify-between text-sm mb-1">
                                    <span className="text-gray-600">{isRTL ? 'الفرق:' : 'Difference:'}</span>
                                    <span className={preview.overrideDifference < 0 ? 'text-red-600 font-medium' : 'text-green-600 font-medium'}>
                                        {preview.overridePercentage} ({preview.overrideDifference?.toFixed(2)})
                                    </span>
                                </div>
                                <p className="text-xs text-gray-500">
                                    {isRTL ? 'الاستراتيجية:' : 'Strategy:'} {preview.strategyDescription}
                                </p>
                            </div>
                        </div>
                    ) : (
                        <div className="bg-white rounded-lg border border-gray-200 p-4 text-center">
                            <p className="text-gray-500 text-sm">
                                {isRTL
                                    ? 'التعديل معطّل. سيتم إرسال القيم الفعلية.'
                                    : 'Override is disabled. Actual values will be sent.'}
                            </p>
                        </div>
                    )}
                </div>
            </div>
        </div>
    )
}
