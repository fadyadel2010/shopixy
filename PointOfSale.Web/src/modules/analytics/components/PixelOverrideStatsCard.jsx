/**
 * Pixel Override Stats Card
 * Phase M1: Pixel Conversion Value Override
 * 
 * Displays warning card when pixel override is active on the Analytics Settings page.
 */
import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { AlertTriangle } from 'lucide-react'
import api from '../../../services/api'

export default function PixelOverrideStatsCard() {
    const { i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'

    const [stats, setStats] = useState(null)
    const [loading, setLoading] = useState(true)

    useEffect(() => {
        fetchStats()
    }, [])

    const fetchStats = async () => {
        try {
            const response = await api.get('/admin/pixel-tracking/stats')
            setStats(response.data)
        } catch (err) {
            console.error('Failed to fetch pixel stats:', err)
        } finally {
            setLoading(false)
        }
    }

    if (loading) {
        return (
            <div className="bg-white rounded-xl border border-gray-200 p-4 shadow-sm animate-pulse">
                <div className="h-5 bg-gray-200 rounded w-1/3 mb-2"></div>
                <div className="h-4 bg-gray-100 rounded w-2/3"></div>
            </div>
        )
    }

    // Only show if override is active
    if (!stats?.isOverrideActive) {
        return null
    }

    return (
        <div className="bg-orange-50 rounded-xl border border-orange-200 p-4 shadow-sm">
            {/* Header */}
            <div className="flex items-center gap-3 mb-3">
                <div className="p-2 bg-orange-100 rounded-lg">
                    <AlertTriangle size={20} className="text-orange-600" />
                </div>
                <div>
                    <h3 className="text-base font-semibold text-orange-800">
                        {isRTL ? 'تعديل القيمة للإعلانات مُفعّل' : 'Pixel Override Active'}
                    </h3>
                </div>
            </div>

            {/* Description */}
            <p className="text-sm text-orange-700 mb-4">
                {isRTL
                    ? 'منصات الإعلانات تتلقى قيم تحويل مُعدّلة. التقارير الداخلية تبقى دقيقة.'
                    : 'Ad platforms are receiving modified conversion values. Internal reports remain accurate.'}
            </p>

            {/* Stats Row */}
            <div className="flex items-center gap-4">
                <div className="px-3 py-2 bg-white rounded-lg border border-orange-200">
                    <span className="text-xs text-gray-500 block">
                        {isRTL ? 'الاستراتيجية' : 'Strategy'}
                    </span>
                    <span className="text-sm font-semibold text-gray-900">
                        {stats.strategy}
                    </span>
                </div>
                <div className="px-3 py-2 bg-white rounded-lg border border-orange-200">
                    <span className="text-xs text-gray-500 block">
                        {isRTL ? 'القيمة' : 'Value'}
                    </span>
                    <span className="text-sm font-semibold text-gray-900">
                        {stats.strategy === 'PERCENTAGE' ? `${stats.configValue}%` : stats.configValue}
                    </span>
                </div>
            </div>
        </div>
    )
}
