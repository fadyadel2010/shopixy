import React, { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { RefreshCw } from 'lucide-react'
import DeliveryFunnel from '../components/DeliveryFunnel'
import { useWhatsAppAnalytics } from '../hooks/useWhatsAppAnalytics'
import { useToast } from '../../../components/Toast'

export default function WhatsAppAnalyticsPage() {
    const { t } = useTranslation()
    const { getAnalytics, loading, error } = useWhatsAppAnalytics()
    const { showError } = useToast()
    const [metrics, setMetrics] = useState(null)
    const [dateRange, setDateRange] = useState({ from: '', to: '' }) // Defaults to backend logic (e.g. 30 days)

    const fetchAnalytics = async () => {
        try {
            const data = await getAnalytics(dateRange.from, dateRange.to)
            setMetrics(data)
        } catch (err) {
            showError(err.message)
        }
    }

    useEffect(() => {
        fetchAnalytics()
    }, [dateRange])

    const kpiCards = [
        { name: t('whatsapp.analytics.kpis.totalMessages'), value: metrics?.totalMessages || 0, color: 'text-gray-900' },
        { name: t('whatsapp.analytics.kpis.successRate'), value: `${metrics?.successRate || 0}%`, color: 'text-green-600' },
        { name: t('whatsapp.analytics.kpis.failed'), value: metrics?.failedMessages || 0, color: 'text-red-600' },
        { name: t('whatsapp.analytics.kpis.retries'), value: metrics?.retryCount || 0, color: 'text-yellow-600' },
    ]

    return (
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
            <div className="sm:flex sm:items-center justify-between mb-8">
                <div>
                    <h1 className="text-2xl font-semibold text-gray-900">{t('whatsapp.analytics.title')}</h1>
                    <p className="mt-2 text-sm text-gray-700">
                        {t('whatsapp.analytics.subtitle')}
                    </p>
                </div>
                <button
                    onClick={fetchAnalytics}
                    className="inline-flex items-center p-2 border border-gray-300 rounded-md shadow-sm text-sm font-medium text-gray-700 bg-white hover:bg-gray-50 focus:outline-none"
                    aria-label={t('common.refresh')}
                    title={t('common.refresh')}
                >
                    <RefreshCw className={`h-5 w-5 ${loading ? 'animate-spin' : ''}`} />
                </button>
            </div>

            {/* KPI Grid */}
            <div className="grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-4 mb-8">
                {kpiCards.map((card) => (
                    <div key={card.name} className="bg-white overflow-hidden shadow rounded-lg px-4 py-5 sm:p-6">
                        <dt className="text-sm font-medium text-gray-500 truncate">{card.name}</dt>
                        <dd className={`mt-1 text-3xl font-semibold ${card.color}`}>{card.value}</dd>
                    </div>
                ))}
            </div>

            <div className="grid grid-cols-1 gap-8 lg:grid-cols-2">
                {/* Delivery Funnel */}
                <DeliveryFunnel metrics={metrics?.funnel} />

                {/* Recent Failures / Insights */}
                <div className="bg-white shadow rounded-lg p-6">
                    <h3 className="text-lg leading-6 font-medium text-gray-900 mb-4">{t('whatsapp.analytics.operationalInsights')}</h3>
                    <div className="space-y-4">
                        <div className="bg-yellow-50 p-4 rounded-md">
                            <h4 className="text-sm font-medium text-yellow-800">{t('whatsapp.analytics.circuitBreakerEvents')}</h4>
                            <p className="mt-1 text-2xl font-semibold text-yellow-600">{metrics?.circuitBreakerEvents || 0}</p>
                            <p className="text-xs text-yellow-600 mt-1">{t('whatsapp.analytics.circuitBreakerDescription')}</p>
                        </div>
                        <div className="bg-red-50 p-4 rounded-md">
                            <h4 className="text-sm font-medium text-red-800">{t('whatsapp.analytics.killSwitchActivations')}</h4>
                            <p className="mt-1 text-2xl font-semibold text-red-600">{metrics?.killSwitchActivations || 0}</p>
                            <p className="text-xs text-red-600 mt-1">{t('whatsapp.analytics.killSwitchDescription')}</p>
                        </div>
                    </div>
                    <div className="mt-6 text-center">
                        <a href="/ecommerce/whatsapp/logs" className="text-sm text-indigo-600 hover:text-indigo-900 font-medium">
                            {t('whatsapp.analytics.viewDetailedLogs')} &rarr;
                        </a>
                    </div>
                </div>
            </div>
        </div>
    )
}
