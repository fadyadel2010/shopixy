import React, { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus, Pencil, Trash2 } from 'lucide-react'
import { useWhatsAppApi } from '../hooks/useWhatsAppApi'
import { useToast } from '../../../components/Toast'
import TemplateEditor from '../components/TemplateEditor'
import { getWhatsAppLanguageKey, getWhatsAppTemplateTypeKey } from '../i18n'

export default function WhatsAppTemplatesPage() {
    const { t } = useTranslation()
    const { getTemplates, createTemplate, updateTemplate, deleteTemplate } = useWhatsAppApi()
    const { showSuccess, showError } = useToast()

    const [templates, setTemplates] = useState([])
    const [isEditorOpen, setIsEditorOpen] = useState(false)
    const [editingTemplate, setEditingTemplate] = useState(null)
    const [newTemplateType, setNewTemplateType] = useState('OrderConfirmation') // Default for new templates

    const fetchTemplates = async () => {
        try {
            const data = await getTemplates()
            setTemplates(data || [])
        } catch (err) {
            showError(err.message)
        }
    }

    useEffect(() => {
        fetchTemplates()
    }, [])

    const handleCreate = (type) => {
        setNewTemplateType(type)
        setEditingTemplate(null)
        setIsEditorOpen(true)
    }

    const handleEdit = (template) => {
        setNewTemplateType(template.type)
        setEditingTemplate(template)
        setIsEditorOpen(true)
    }

    const handleDelete = async (id) => {
        if (!confirm(t('whatsapp.templates.confirm.delete'))) return
        try {
            await deleteTemplate(id)
            showSuccess(t('whatsapp.templates.messages.deleted'))
            fetchTemplates()
        } catch (err) {
            showError(err.message)
        }
    }

    const handleSave = async (data) => {
        try {
            if (editingTemplate) {
                await updateTemplate(editingTemplate.id, data)
                showSuccess(t('whatsapp.templates.messages.updated'))
            } else {
                await createTemplate(data)
                showSuccess(t('whatsapp.templates.messages.created'))
            }
            setIsEditorOpen(false)
            fetchTemplates()
        } catch (err) {
            showError(err.message)
        }
    }

    // Group templates by type
    const orderTemplates = templates.filter(t => t.type === 'OrderConfirmation')
    const otpTemplates = templates.filter(t => t.type === 'Otp')

    return (
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
            <div className="sm:flex sm:items-center">
                <div className="sm:flex-auto">
                    <h1 className="text-2xl font-semibold text-gray-900">{t('whatsapp.templates.title')}</h1>
                    <p className="mt-2 text-sm text-gray-700">
                        {t('whatsapp.templates.subtitle')}
                    </p>
                </div>
            </div>

            {/* Editor Modal */}
            {isEditorOpen && (
                <div className="fixed inset-0 z-10 overflow-y-auto" aria-labelledby="modal-title" role="dialog" aria-modal="true">
                    <div className="flex items-end justify-center min-h-screen pt-4 px-4 pb-20 text-center sm:block sm:p-0">
                        <div className="fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity" onClick={() => setIsEditorOpen(false)}></div>
                        <span className="hidden sm:inline-block sm:align-middle sm:h-screen" aria-hidden="true">&#8203;</span>
                        <div className="inline-block align-bottom bg-white rounded-lg px-4 pt-5 pb-4 text-left overflow-hidden shadow-xl transform transition-all sm:my-8 sm:align-middle sm:max-w-2xl sm:w-full sm:p-6">
                            <h3 className="text-lg leading-6 font-medium text-gray-900 mb-4">
                                {editingTemplate
                                    ? t('whatsapp.templates.actions.editTemplate')
                                    : t('whatsapp.templates.actions.newTemplate', {
                                        type: t(getWhatsAppTemplateTypeKey(newTemplateType) || '') || newTemplateType
                                    })}
                            </h3>
                            <TemplateEditor
                                template={editingTemplate}
                                type={newTemplateType}
                                onSave={handleSave}
                                onCancel={() => setIsEditorOpen(false)}
                            />
                        </div>
                    </div>
                </div>
            )}

            {/* Template Sections */}
            <div className="mt-8 space-y-12">
                {/* Order Confirmation Section */}
                <section>
                    <div className="flex justify-between items-center mb-4">
                        <h2 className="text-lg font-medium text-gray-900">{t('whatsapp.types.orderConfirmation')}</h2>
                        <button
                            onClick={() => handleCreate('OrderConfirmation')}
                            className="inline-flex items-center px-3 py-1.5 border border-transparent shadow-sm text-xs font-medium rounded text-white bg-indigo-600 hover:bg-indigo-700 focus:outline-none"
                        >
                            <Plus className="-ml-0.5 mr-2 h-4 w-4" />
                            {t('whatsapp.templates.actions.create')}
                        </button>
                    </div>
                    {orderTemplates.length > 0 ? (
                        <div className="bg-white shadow overflow-hidden sm:rounded-md">
                            <ul className="divide-y divide-gray-200">
                                {orderTemplates.map(template => (
                                    <TemplateItem key={template.id} template={template} onEdit={handleEdit} onDelete={handleDelete} />
                                ))}
                            </ul>
                        </div>
                    ) : (
                        <div className="text-sm text-gray-500 italic">{t('whatsapp.templates.empty')}</div>
                    )}
                </section>

                {/* OTP Section */}
                <section>
                    <div className="flex justify-between items-center mb-4">
                        <h2 className="text-lg font-medium text-gray-900">{t('whatsapp.types.otp')}</h2>
                        <button
                            onClick={() => handleCreate('Otp')}
                            className="inline-flex items-center px-3 py-1.5 border border-transparent shadow-sm text-xs font-medium rounded text-white bg-indigo-600 hover:bg-indigo-700 focus:outline-none"
                        >
                            <Plus className="-ml-0.5 mr-2 h-4 w-4" />
                            {t('whatsapp.templates.actions.create')}
                        </button>
                    </div>
                    {otpTemplates.length > 0 ? (
                        <div className="bg-white shadow overflow-hidden sm:rounded-md">
                            <ul className="divide-y divide-gray-200">
                                {otpTemplates.map(template => (
                                    <TemplateItem key={template.id} template={template} onEdit={handleEdit} onDelete={handleDelete} />
                                ))}
                            </ul>
                        </div>
                    ) : (
                        <div className="text-sm text-gray-500 italic">{t('whatsapp.templates.empty')}</div>
                    )}
                </section>
            </div>
        </div>
    )
}

function TemplateItem({ template, onEdit, onDelete }) {
    const { t } = useTranslation()
    const languageKey = getWhatsAppLanguageKey(template.language)

    return (
        <li className="px-4 py-4 sm:px-6 hover:bg-gray-50 flex justify-between items-center">
            <div className="flex-1 min-w-0">
                <div className="flex items-center justify-between">
                    <p className={`text-sm font-medium truncate ${template.isActive ? 'text-indigo-600' : 'text-gray-500'}`}>
                        {languageKey ? t(languageKey === 'whatsapp.languages.english' ? 'whatsapp.languages.englishWithCode' : 'whatsapp.languages.arabicWithCode') : template.language}
                        {!template.isActive && <span className="ml-2 text-xs text-gray-400">({t('common.inactive')})</span>}
                    </p>
                </div>
                <div className="mt-2 flex">
                    <p className="flex items-center text-sm text-gray-500 line-clamp-2">
                        {template.content}
                    </p>
                </div>
            </div>
            <div className="ml-5 flex-shrink-0 flex space-x-2">
                <button
                    onClick={() => onEdit(template)}
                    className="p-1 rounded-full text-gray-400 hover:text-gray-500"
                >
                    <Pencil className="h-5 w-5" />
                </button>
                <button
                    onClick={() => onDelete(template.id)}
                    className="p-1 rounded-full text-gray-400 hover:text-red-500"
                >
                    <Trash2 className="h-5 w-5" />
                </button>
            </div>
        </li>
    )
}
