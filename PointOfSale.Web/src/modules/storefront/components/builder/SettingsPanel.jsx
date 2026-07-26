/**
 * Settings Panel
 * Phase 9.8: POS Integration
 */

import { useTranslation } from 'react-i18next'
import { X, AlertCircle } from 'lucide-react'
import { useBuilderStore, selectSelectedSection } from '../../store'
import { sectionRegistry } from '../../builder'
import { SchemaSettingsForm } from './SchemaSettingsForm'

export function SettingsPanel() {
    const { i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'

    const selectedSection = useBuilderStore(selectSelectedSection)
    const selectSection = useBuilderStore((s) => s.selectSection)
    const updateSectionSettings = useBuilderStore((s) => s.updateSectionSettings)

    if (!selectedSection) {
        return (
            <div className="w-80 h-full bg-white border-l border-gray-200 flex flex-col">
                <div className="p-4 border-b border-gray-200">
                    <h2 className="text-lg font-semibold text-gray-900">
                        {isRTL ? 'الإعدادات' : 'Settings'}
                    </h2>
                </div>
                <div className="flex-1 flex items-center justify-center p-6">
                    <div className="text-center">
                        <div className="w-12 h-12 bg-gray-100 rounded-xl flex items-center justify-center mx-auto mb-3">
                            <AlertCircle size={24} className="text-gray-400" />
                        </div>
                        <p className="text-gray-500 text-sm">
                            {isRTL ? 'اختر قسماً لتعديل إعداداته' : 'Select a section to edit its settings'}
                        </p>
                    </div>
                </div>
            </div>
        )
    }

    const definition = sectionRegistry.get(selectedSection.type)
    const EditorComponent = definition?.editor
    const settingsSchema = definition?.settingsSchema
    const Icon = definition?.icon

    const handleUpdate = (newSettings) => {
        updateSectionSettings(selectedSection.id, newSettings)
    }

    return (
        <div className="w-80 h-full bg-white border-l border-gray-200 flex flex-col">
            {/* Header */}
            <div className="p-4 border-b border-gray-200 flex items-center justify-between">
                <div className="flex items-center gap-2">
                    {Icon && <Icon size={16} className="text-indigo-600" />}
                    <h2 className="text-lg font-semibold text-gray-900">
                        {isRTL ? definition?.displayName.ar : definition?.displayName.en}
                    </h2>
                </div>
                <button
                    onClick={() => selectSection(null)}
                    className="p-1.5 rounded hover:bg-gray-100 transition-colors"
                >
                    <X size={18} className="text-gray-500" />
                </button>
            </div>

            {/* Editor */}
            <div className="flex-1 overflow-y-auto p-4">
                {settingsSchema?.length ? (
                    <SchemaSettingsForm
                        section={selectedSection}
                        schema={settingsSchema}
                        onUpdate={handleUpdate}
                    />
                ) : EditorComponent ? (
                    <EditorComponent section={selectedSection} onUpdate={handleUpdate} />
                ) : (
                    <div className="text-center text-gray-400 py-8">
                        <p>{isRTL ? 'لا يوجد محرر لهذا النوع' : 'No editor available'}</p>
                    </div>
                )}
            </div>

            {/* Footer */}
            <div className="p-4 border-t border-gray-200 bg-gray-50">
                <div className="text-xs text-gray-500">
                    <p><strong>{isRTL ? 'معرف القسم:' : 'Section ID:'}</strong> {selectedSection.id.substring(0, 16)}...</p>
                    <p className="mt-1"><strong>{isRTL ? 'النوع:' : 'Type:'}</strong> {selectedSection.type}</p>
                </div>
            </div>
        </div>
    )
}
