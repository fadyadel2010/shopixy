import React from 'react'
import { useTranslation } from 'react-i18next'

const VARIABLES = [
    { name: 'customer_name', descriptionKey: 'whatsapp.variables.items.customerName' },
    { name: 'order_number', descriptionKey: 'whatsapp.variables.items.orderNumber' },
    { name: 'total', descriptionKey: 'whatsapp.variables.items.total' },
    { name: 'currency', descriptionKey: 'whatsapp.variables.items.currency' },
    { name: 'items_count', descriptionKey: 'whatsapp.variables.items.itemsCount' },
    { name: 'store_name', descriptionKey: 'whatsapp.variables.items.storeName' },
    { name: 'otp_code', descriptionKey: 'whatsapp.variables.items.otpCode' }
]

export default function VariableHelper({ onInsert }) {
    const { t } = useTranslation()

    return (
        <div className="bg-gray-50 p-4 rounded-md border border-gray-200">
            <h4 className="text-sm font-medium text-gray-900 mb-2">{t('whatsapp.variables.title')}</h4>
            <p className="text-xs text-gray-500 mb-4">{t('whatsapp.variables.subtitle')}</p>

            <div className="space-y-2">
                {VARIABLES.map(v => (
                    <button
                        key={v.name}
                        onClick={() => onInsert(`{{${v.name}}}`)}
                        type="button"
                        className="w-full text-left bg-white border border-gray-300 rounded px-2 py-1.5 text-xs hover:bg-indigo-50 hover:border-indigo-300 focus:outline-none flex justify-between group"
                    >
                        <code className="text-indigo-600 font-mono font-semibold">{`{{${v.name}}}`}</code>
                        <span className="text-gray-500 group-hover:text-gray-700">{t(v.descriptionKey)}</span>
                    </button>
                ))}
            </div>
        </div>
    )
}
