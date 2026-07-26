import React, { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import VariableHelper from './VariableHelper'
import TemplatePreview from './TemplatePreview'

export default function TemplateEditor({ template, onSave, onCancel, type }) {
    const { t } = useTranslation()
    const [content, setContent] = useState(template?.content || '')
    const [language, setLanguage] = useState(template?.language || 'en')
    const [isActive, setIsActive] = useState(template?.isActive ?? true)
    const [errors, setErrors] = useState({})

    const handleInsertVariable = (variable) => {
        setContent(prev => prev + variable)
    }

    const validate = () => {
        const newErrors = {}
        if (!content.trim()) newErrors.content = t('whatsapp.templates.validation.contentRequired')
        if (content.length > 1024) newErrors.content = t('whatsapp.templates.validation.contentTooLong')

        // Specific checks per type
        if (type === 'Otp' && !content.includes('{{otp_code}}')) {
            newErrors.content = t('whatsapp.templates.validation.otpRequiresCode')
        }
        if (type === 'OrderConfirmation' && !content.includes('{{order_number}}')) {
            newErrors.content = t('whatsapp.templates.validation.orderRecommendedNumber')
        }

        setErrors(newErrors)
        return Object.keys(newErrors).length === 0
    }

    const handleSubmit = (e) => {
        e.preventDefault()
        if (validate()) {
            onSave({ ...template, content, language, isActive, type })
        }
    }

    return (
        <form onSubmit={handleSubmit} className="space-y-6">
            <div className="grid grid-cols-1 gap-y-6 gap-x-4 sm:grid-cols-6">
                <div className="sm:col-span-3">
                    <label htmlFor="language" className="block text-sm font-medium text-gray-700">
                        {t('whatsapp.templates.language')}
                    </label>
                    <div className="mt-1">
                        <select
                            id="language"
                            name="language"
                            value={language}
                            onChange={(e) => setLanguage(e.target.value)}
                            className="shadow-sm focus:ring-indigo-500 focus:border-indigo-500 block w-full sm:text-sm border-gray-300 rounded-md"
                        >
                            <option value="en">{t('whatsapp.languages.englishWithCode')}</option>
                            <option value="ar">{t('whatsapp.languages.arabicWithCode')}</option>
                        </select>
                    </div>
                </div>

                <div className="sm:col-span-3">
                    <label className="block text-sm font-medium text-gray-700">{t('common.status')}</label>
                    <div className="mt-2 flex items-center">
                        <input
                            id="isActive"
                            name="isActive"
                            type="checkbox"
                            checked={isActive}
                            onChange={(e) => setIsActive(e.target.checked)}
                            className="h-4 w-4 text-indigo-600 focus:ring-indigo-500 border-gray-300 rounded"
                        />
                        <label htmlFor="isActive" className="ml-2 block text-sm text-gray-900">
                            {t('common.active')}
                        </label>
                    </div>
                </div>

                <div className="sm:col-span-4">
                    <label htmlFor="content" className="block text-sm font-medium text-gray-700">
                        {t('whatsapp.templates.messageContent')}
                    </label>
                    <div className="mt-1 relative">
                        <textarea
                            id="content"
                            name="content"
                            rows={8}
                            className={`shadow-sm block w-full sm:text-sm rounded-md ${errors.content ? 'border-red-300 focus:ring-red-500 focus:border-red-500' : 'border-gray-300 focus:ring-indigo-500 focus:border-indigo-500'}`}
                            value={content}
                            onChange={(e) => setContent(e.target.value)}
                            placeholder={t('whatsapp.templates.placeholder')}
                        />
                    </div>
                    {errors.content && (
                        <p className="mt-2 text-sm text-red-600" id="content-error">{errors.content}</p>
                    )}
                    <p className="mt-2 text-xs text-gray-500">{t('whatsapp.templates.supportedVariables')}</p>
                </div>

                <div className="sm:col-span-2 space-y-4">
                    <VariableHelper onInsert={handleInsertVariable} />
                </div>
            </div>

            <div className="mt-6">
                <label className="block text-sm font-medium text-gray-700 mb-2">{t('whatsapp.templates.livePreview')}</label>
                <TemplatePreview content={content} type={type} />
            </div>

            <div className="flex justify-end pt-5">
                <button
                    type="button"
                    onClick={onCancel}
                    className="bg-white py-2 px-4 border border-gray-300 rounded-md shadow-sm text-sm font-medium text-gray-700 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 mr-3"
                >
                    {t('common.cancel')}
                </button>
                <button
                    type="submit"
                    className="inline-flex justify-center py-2 px-4 border border-transparent shadow-sm text-sm font-medium rounded-md text-white bg-indigo-600 hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500"
                >
                    {t('whatsapp.templates.actions.saveTemplate')}
                </button>
            </div>
        </form>
    )
}
