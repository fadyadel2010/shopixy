import React from 'react'
import { useTranslation } from 'react-i18next'

export default function DeliveryFunnel({ metrics }) {
    const { t } = useTranslation()

    // Default metrics if not provided
    const data = metrics || {
        emitted: 0,
        dispatched: 0,
        sent: 0,
        delivered: 0
    }

    // Calculate percentages relative to Emitted
    const getPercent = (value) => {
        if (!data.emitted) return 0
        return Math.round((value / data.emitted) * 100)
    }

    const stages = [
        { label: t('whatsapp.analytics.funnel.emitted'), value: data.emitted, color: 'bg-blue-500', percent: getPercent(data.emitted) },
        { label: t('whatsapp.analytics.funnel.dispatched'), value: data.dispatched, color: 'bg-indigo-500', percent: getPercent(data.dispatched) },
        { label: t('whatsapp.analytics.funnel.sentToWhatsapp'), value: data.sent, color: 'bg-green-500', percent: getPercent(data.sent) },
        // { label: 'Delivered', value: data.delivered, color: 'bg-emerald-500', percent: getPercent(data.delivered) } // Delivery receipts often disabled
    ]

    return (
        <div className="bg-white shadow rounded-lg p-6">
            <h3 className="text-lg leading-6 font-medium text-gray-900 mb-6">{t('whatsapp.analytics.funnel.title')}</h3>
            <div className="space-y-4">
                {stages.map((stage, idx) => (
                    <div key={idx} className="relative">
                        <div className="flex items-center justify-between text-sm font-medium text-gray-700 mb-1">
                            <span>{stage.label}</span>
                            <span>{stage.value.toLocaleString()} ({stage.percent}%)</span>
                        </div>
                        <div className="w-full bg-gray-200 rounded-full h-4 overflow-hidden">
                            <div
                                className={`${stage.color} h-4 rounded-full transition-all duration-500 ease-out`}
                                style={{ width: `${stage.percent}%` }}
                            ></div>
                        </div>
                        {idx < stages.length - 1 && (
                            <div className="absolute left-1/2 -bottom-4 w-0.5 h-4 bg-gray-300 transform -translate-x-1/2 z-0"></div>
                        )}
                    </div>
                ))}
            </div>
            <p className="mt-6 text-xs text-gray-500 text-center">
                {t('whatsapp.analytics.funnel.dropOffNote')}
            </p>
        </div>
    )
}
