import React, { useMemo } from 'react'
import { useTranslation } from 'react-i18next'

export default function TemplatePreview({ content }) {
    const { t } = useTranslation()

    const sampleData = useMemo(() => ({
        customer_name: t('whatsapp.preview.sampleData.customerName'),
        order_number: '#10234',
        total: '1,250.00',
        currency: 'SAR',
        items_count: '3',
        store_name: t('whatsapp.preview.sampleData.storeName'),
        otp_code: '123456'
    }), [t])

    const previewText = useMemo(() => {
        let text = content || ''
        Object.entries(sampleData).forEach(([key, value]) => {
            text = text.replaceAll(`{{${key}}}`, value)
        })
        return text
    }, [content, sampleData])

    return (
        <div className="bg-white rounded-lg shadow-sm border border-gray-200 overflow-hidden">
            <div className="bg-[#008069] text-white px-4 py-2 text-sm font-medium flex items-center">
                <span>{t('whatsapp.preview.title')}</span>
            </div>
            <div className="bg-[#e5ddd5] p-4 min-h-[200px] flex flex-col space-y-2">
                <div className="bg-white p-2 rounded-lg shadow-sm self-start max-w-[90%] text-sm text-gray-800 whitespace-pre-wrap relative speech-bubble">
                    {previewText || <span className="text-gray-400 italic">{t('whatsapp.preview.empty')}</span>}
                    <div className="text-[10px] text-gray-400 text-right mt-1">{t('whatsapp.preview.timestamp')}</div>
                </div>
            </div>
            <div className="p-2 bg-gray-50 text-xs text-gray-500 text-center border-t">
                {t('whatsapp.preview.footer')}
            </div>
        </div>
    )
}
