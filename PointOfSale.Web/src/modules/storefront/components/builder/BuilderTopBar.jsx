/**
 * Builder Top Bar
 * Phase 9.8: POS Integration
 */

import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { Eye, Upload, RotateCcw, Undo2, Redo2, ChevronDown, Save, AlertCircle, Plus, Trash2 } from 'lucide-react'
import { useBuilderStore } from '../../store'

const PAGE_OPTIONS = [
    { key: 'home', labelEn: 'Home Page', labelAr: 'الصفحة الرئيسية' },
    { key: 'product', labelEn: 'Product Page', labelAr: 'صفحة المنتج' },
    { key: 'category', labelEn: 'Category Page', labelAr: 'صفحة التصنيف' },
    { key: 'product-template', labelEn: 'Product Template', labelAr: 'قالب صفحة المنتج' },
]

const formatSavedAt = (value) => {
    if (!value) return null

    const date = new Date(value)
    if (Number.isNaN(date.getTime())) {
        return null
    }

    return new Intl.DateTimeFormat(undefined, {
        hour: 'numeric',
        minute: '2-digit',
    }).format(date)
}

export function BuilderTopBar({
    onPreview,
    onPublish,
    onRollback,
    onSave,
    templateOptions = [],
    activeTemplateId = 'default',
    onTemplateChange,
    onCreateTemplate,
    onDeleteTemplate,
    showTemplateControls = false,
    saveStatus = 'idle',
    saveError = null,
    lastSavedAt = null,
    onRetrySave,
}) {
    const { i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const navigate = useNavigate()
    const [isPageDropdownOpen, setIsPageDropdownOpen] = useState(false)

    const currentPage = useBuilderStore((s) => s.currentPage)
    const isDirty = useBuilderStore((s) => s.isDirty)
    const isSaving = useBuilderStore((s) => s.isSaving)
    const isPublishing = useBuilderStore((s) => s.isPublishing)
    const undo = useBuilderStore((s) => s.undo)
    const redo = useBuilderStore((s) => s.redo)
    const canUndo = useBuilderStore((s) => s.canUndo())
    const canRedo = useBuilderStore((s) => s.canRedo())

    const currentPageOption = PAGE_OPTIONS.find((page) => page.key === currentPage)
    const lastSavedLabel = formatSavedAt(lastSavedAt)

    const handlePageChange = (pageKey) => {
        if (isDirty) {
            const confirmed = window.confirm(
                isRTL ? 'لديك تغييرات غير محفوظة. هل تريد المتابعة؟' : 'You have unsaved changes. Continue anyway?'
            )
            if (!confirmed) {
                setIsPageDropdownOpen(false)
                return
            }
        }

        if (pageKey === 'product-template') {
            navigate('/storefront/builder?page=product-template&templateId=default')
        } else {
            navigate(`/storefront/builder?page=${pageKey}`)
        }

        setIsPageDropdownOpen(false)
    }

    return (
        <div className="h-14 bg-white border-b border-gray-200 flex items-center px-4 gap-4 shadow-sm">
            <div className="relative">
                <button
                    onClick={() => setIsPageDropdownOpen(!isPageDropdownOpen)}
                    className="flex items-center gap-2 px-3 py-2 bg-gray-100 border border-gray-200 rounded-lg hover:bg-gray-200 transition-colors"
                >
                    <span className="text-gray-900 font-medium">
                        {isRTL ? currentPageOption?.labelAr : currentPageOption?.labelEn}
                    </span>
                    <ChevronDown size={16} className="text-gray-500" />
                </button>

                {isPageDropdownOpen && (
                    <>
                        <div className="fixed inset-0 z-10" onClick={() => setIsPageDropdownOpen(false)} />
                        <div className={`absolute top-full ${isRTL ? 'right-0' : 'left-0'} mt-1 w-52 bg-white border border-gray-200 rounded-lg shadow-xl z-20 py-1`}>
                            {PAGE_OPTIONS.map((option) => (
                                <button
                                    key={option.key}
                                    onClick={() => handlePageChange(option.key)}
                                    className={`w-full px-3 py-2 text-${isRTL ? 'right' : 'left'} hover:bg-gray-100 transition-colors ${option.key === currentPage ? 'text-indigo-600 bg-indigo-50' : 'text-gray-900'}`}
                                >
                                    {isRTL ? option.labelAr : option.labelEn}
                                </button>
                            ))}
                        </div>
                    </>
                )}
            </div>

            {showTemplateControls && (
                <div className="flex items-center gap-2">
                    <select
                        value={activeTemplateId}
                        onChange={(e) => onTemplateChange?.(e.target.value)}
                        className="rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900"
                    >
                        {templateOptions.map((option) => (
                            <option key={option.id} value={option.id}>
                                {option.name}
                            </option>
                        ))}
                    </select>

                    <button
                        onClick={onCreateTemplate}
                        className="flex items-center gap-2 rounded-lg border border-gray-300 bg-white px-3 py-2 hover:bg-gray-50 transition-colors"
                    >
                        <Plus size={16} />
                        <span>{isRTL ? 'قالب جديد' : 'New Template'}</span>
                    </button>

                    <button
                        onClick={onDeleteTemplate}
                        disabled={activeTemplateId === 'default'}
                        className="flex items-center gap-2 rounded-lg border border-red-200 bg-white px-3 py-2 text-red-600 hover:bg-red-50 disabled:cursor-not-allowed disabled:opacity-50 transition-colors"
                    >
                        <Trash2 size={16} />
                        <span>{isRTL ? 'حذف' : 'Delete'}</span>
                    </button>
                </div>
            )}

            <div className="flex items-center gap-1">
                <button
                    onClick={undo}
                    disabled={!canUndo}
                    className="p-2 rounded-lg hover:bg-gray-100 disabled:opacity-30 disabled:cursor-not-allowed transition-colors"
                    title={isRTL ? 'تراجع' : 'Undo'}
                >
                    <Undo2 size={18} className="text-gray-500" />
                </button>
                <button
                    onClick={redo}
                    disabled={!canRedo}
                    className="p-2 rounded-lg hover:bg-gray-100 disabled:opacity-30 disabled:cursor-not-allowed transition-colors"
                    title={isRTL ? 'إعادة' : 'Redo'}
                >
                    <Redo2 size={18} className="text-gray-500" />
                </button>
            </div>

            {isDirty && (
                <div className="flex items-center gap-1.5 px-2 py-1 bg-yellow-100 rounded text-yellow-700 text-xs font-medium">
                    <AlertCircle size={12} />
                    <span>{isRTL ? 'تغييرات غير محفوظة' : 'Unsaved changes'}</span>
                </div>
            )}

            {saveStatus === 'saving' && (
                <div className="px-2 py-1 rounded bg-sky-100 text-sky-700 text-xs font-medium">
                    {isRTL ? 'جارٍ الحفظ...' : 'Saving...'}
                </div>
            )}

            {saveStatus === 'saved' && lastSavedLabel && !isDirty && (
                <div className="px-2 py-1 rounded bg-emerald-100 text-emerald-700 text-xs font-medium">
                    {isRTL ? `آخر حفظ ${lastSavedLabel}` : `Last saved ${lastSavedLabel}`}
                </div>
            )}

            {saveStatus === 'error' && saveError && (
                <button
                    onClick={onRetrySave}
                    className="inline-flex items-center gap-1.5 rounded bg-red-100 px-2 py-1 text-xs font-medium text-red-700 hover:bg-red-200 transition-colors"
                >
                    <AlertCircle size={12} />
                    <span>{isRTL ? 'فشل الحفظ - إعادة المحاولة' : 'Save failed - Retry'}</span>
                </button>
            )}

            <div className="flex-1" />

            <div className="flex items-center gap-2">
                <button
                    onClick={onSave}
                    disabled={!isDirty || isSaving}
                    className="flex items-center gap-2 px-3 py-2 bg-white border border-gray-300 rounded-lg hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                >
                    <Save size={16} />
                    <span>{isSaving ? (isRTL ? 'جاري الحفظ...' : 'Saving...') : (isRTL ? 'حفظ المسودة' : 'Save Draft')}</span>
                </button>

                <button
                    onClick={onPreview}
                    className="flex items-center gap-2 px-3 py-2 bg-white border border-gray-300 rounded-lg hover:bg-gray-50 transition-colors"
                >
                    <Eye size={16} />
                    <span>{isRTL ? 'معاينة' : 'Preview'}</span>
                </button>

                <button
                    onClick={onRollback}
                    className="flex items-center gap-2 px-3 py-2 bg-white border border-gray-300 rounded-lg hover:bg-gray-50 transition-colors"
                >
                    <RotateCcw size={16} />
                    <span className="hidden lg:inline">{isRTL ? 'استعادة' : 'Rollback'}</span>
                </button>

                <button
                    onClick={onPublish}
                    disabled={isPublishing}
                    className="flex items-center gap-2 px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 disabled:opacity-50 transition-colors"
                >
                    <Upload size={16} />
                    <span>{isPublishing ? (isRTL ? 'جاري النشر...' : 'Publishing...') : (isRTL ? 'نشر' : 'Publish')}</span>
                </button>
            </div>
        </div>
    )
}
