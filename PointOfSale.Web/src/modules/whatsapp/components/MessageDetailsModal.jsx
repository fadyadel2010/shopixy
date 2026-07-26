import React from 'react'
import { useTranslation } from 'react-i18next'
import { getWhatsAppMessageStatusKey, getWhatsAppTemplateTypeKey } from '../i18n'

export default function MessageDetailsModal({ message, onClose }) {
    const { t } = useTranslation()

    if (!message) return null

    // Helper to format JSON content safely
    const formatContent = () => {
        try {
            // Mask OTP if present in content
            let content = message.content
            if (message.type === 'Otp' && content) {
                content = content.replace(/\d{4,8}/g, '****')
            }
            return content
        } catch (e) {
            return message.content
        }
    }

    const statusKey = getWhatsAppMessageStatusKey(message.status)
    const typeKey = getWhatsAppTemplateTypeKey(message.type)

    return (
        <div className="fixed z-10 inset-0 overflow-y-auto" aria-labelledby="modal-title" role="dialog" aria-modal="true">
            <div className="flex items-end justify-center min-h-screen pt-4 px-4 pb-20 text-center sm:block sm:p-0">
                <div className="fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity" onClick={onClose}></div>
                <span className="hidden sm:inline-block sm:align-middle sm:h-screen" aria-hidden="true">&#8203;</span>
                <div className="inline-block align-bottom bg-white rounded-lg px-4 pt-5 pb-4 text-left overflow-hidden shadow-xl transform transition-all sm:my-8 sm:align-middle sm:max-w-lg sm:w-full sm:p-6">
                    <div>
                        <h3 className="text-lg leading-6 font-medium text-gray-900" id="modal-title">
                            {t('whatsapp.logs.details.title')}
                        </h3>
                        <div className="mt-4 space-y-4">
                            <div>
                                <label className="block text-sm font-medium text-gray-500">{t('whatsapp.logs.columns.recipient')}</label>
                                <p className="mt-1 text-sm text-gray-900">{message.phoneNumber}</p>
                            </div>
                            <div>
                                <label className="block text-sm font-medium text-gray-500">{t('common.status')}</label>
                                <p className="mt-1 text-sm text-gray-900">
                                    {statusKey ? t(statusKey) : message.status}
                                    {message.retryCount > 0 && ` ${t('whatsapp.logs.details.retryCount', { count: message.retryCount })}`}
                                </p>
                            </div>
                            <div>
                                <label className="block text-sm font-medium text-gray-500">{t('whatsapp.logs.columns.type')}</label>
                                <p className="mt-1 text-sm text-gray-900">{typeKey ? t(typeKey) : message.type}</p>
                            </div>
                            <div>
                                <label className="block text-sm font-medium text-gray-500">{t('whatsapp.logs.details.content')}</label>
                                <div className="mt-1 p-2 bg-gray-50 rounded text-sm text-gray-800 whitespace-pre-wrap font-mono relative">
                                    {formatContent()}
                                    {message.type === 'Otp' && (
                                        <span className="absolute top-1 right-1 text-[10px] text-gray-400 border border-gray-200 rounded px-1">
                                            {t('whatsapp.logs.details.masked')}
                                        </span>
                                    )}
                                </div>
                            </div>
                            {message.errorMessage && (
                                <div className="bg-red-50 p-2 rounded">
                                    <label className="block text-sm font-medium text-red-500">{t('common.error')}</label>
                                    <p className="mt-1 text-sm text-red-700">{message.errorMessage}</p>
                                </div>
                            )}
                            <div>
                                <label className="block text-sm font-medium text-gray-500">{t('whatsapp.logs.details.timestamp')}</label>
                                <p className="mt-1 text-sm text-gray-900">{new Date(message.createdAtUtc).toLocaleString()}</p>
                            </div>
                        </div>
                    </div>
                    <div className="mt-5 sm:mt-6">
                        <button
                            type="button"
                            className="inline-flex justify-center w-full rounded-md border border-gray-300 shadow-sm px-4 py-2 bg-white text-base font-medium text-gray-700 hover:bg-gray-50 focus:outline-none sm:text-sm"
                            onClick={onClose}
                        >
                            {t('common.close')}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    )
}
