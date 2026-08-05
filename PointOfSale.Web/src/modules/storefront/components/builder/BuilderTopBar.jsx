import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { AlertCircle, ChevronDown, Plus, Redo2, Trash2, Undo2 } from 'lucide-react'

import { useBuilderStore } from '../../store'
import { AuthoringActionBar, AuthoringStatusBadge } from '../../authoringPlatform'

const PAGE_OPTIONS = [
    { key: 'home', labelEn: 'Homepage', labelAr: 'الصفحة الرئيسية', path: '/storefront/homepage' },
    { key: 'product-template', labelEn: 'Product Template', labelAr: 'قالب صفحة المنتج', path: '/storefront/catalog/product-template?templateId=default' },
]

const formatSavedAt = (value) => {
    if (!value) return null
    const date = new Date(value)
    if (Number.isNaN(date.getTime())) return null
    return new Intl.DateTimeFormat(undefined, { hour: 'numeric', minute: '2-digit' }).format(date)
}

export function BuilderTopBar({
    onBack,
    onPageChange,
    onPreview,
    onPublish,
    onHistory,
    onDiscard,
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
    const [isPageDropdownOpen, setIsPageDropdownOpen] = useState(false)
    const currentPage = useBuilderStore((state) => state.currentPage)
    const isDirty = useBuilderStore((state) => state.isDirty)
    const isSaving = useBuilderStore((state) => state.isSaving)
    const isPublishing = useBuilderStore((state) => state.isPublishing)
    const undo = useBuilderStore((state) => state.undo)
    const redo = useBuilderStore((state) => state.redo)
    const canUndo = useBuilderStore((state) => state.canUndo())
    const canRedo = useBuilderStore((state) => state.canRedo())
    const currentPageOption = PAGE_OPTIONS.find((page) => page.key === currentPage) || PAGE_OPTIONS[0]
    const title = showTemplateControls ? `Product Template · ${activeTemplateId}` : 'Homepage'
    const lastSavedLabel = formatSavedAt(lastSavedAt)

    const statusState = isPublishing
        ? 'publishing'
        : isSaving || saveStatus === 'saving'
            ? 'saving'
            : saveError
                ? 'error'
                : isDirty
                    ? 'dirty'
                    : 'draft'
    const statusLabel = saveStatus === 'saved' && lastSavedLabel && !isDirty
        ? `Draft saved ${lastSavedLabel}`
        : undefined

    const handlePageChange = (option) => {
        onPageChange?.(option.path)
        setIsPageDropdownOpen(false)
    }

    const leading = (
        <div className="flex flex-wrap items-center gap-2">
            <div className="relative">
                <button type="button" onClick={() => setIsPageDropdownOpen((open) => !open)} aria-haspopup="menu" aria-expanded={isPageDropdownOpen} className="flex min-h-10 items-center gap-2 rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm font-medium text-gray-800 hover:bg-gray-50">
                    <span>{isRTL ? currentPageOption.labelAr : currentPageOption.labelEn}</span>
                    <ChevronDown size={16} aria-hidden="true" />
                </button>
                {isPageDropdownOpen ? (
                    <>
                        <button type="button" aria-label="Close page menu" className="fixed inset-0 z-10" onClick={() => setIsPageDropdownOpen(false)} />
                        <div role="menu" className={`absolute top-full z-20 mt-1 w-52 rounded-lg border border-gray-200 bg-white py-1 shadow-xl ${isRTL ? 'right-0' : 'left-0'}`}>
                            {PAGE_OPTIONS.map((option) => (
                                <button type="button" role="menuitem" key={option.key} onClick={() => handlePageChange(option)} className={`w-full px-3 py-2 text-sm hover:bg-gray-100 ${isRTL ? 'text-right' : 'text-left'} ${option.key === currentPage ? 'bg-indigo-50 text-indigo-700' : 'text-gray-900'}`}>
                                    {isRTL ? option.labelAr : option.labelEn}
                                </button>
                            ))}
                        </div>
                    </>
                ) : null}
            </div>

            {showTemplateControls ? (
                <>
                    <select value={activeTemplateId} onChange={(event) => onTemplateChange?.(event.target.value)} aria-label="Product template" className="min-h-10 rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900">
                        {templateOptions.map((option) => <option key={option.id} value={option.id}>{option.name}</option>)}
                    </select>
                    <button type="button" onClick={onCreateTemplate} className="inline-flex min-h-10 items-center gap-2 rounded-lg border border-gray-300 px-3 py-2 text-sm hover:bg-gray-50"><Plus size={16} aria-hidden="true" />New Template</button>
                    <button type="button" onClick={onDeleteTemplate} disabled={activeTemplateId === 'default'} className="inline-flex min-h-10 items-center gap-2 rounded-lg border border-red-200 px-3 py-2 text-sm text-red-700 hover:bg-red-50 disabled:opacity-50"><Trash2 size={16} aria-hidden="true" />Delete</button>
                </>
            ) : null}

            <div className="flex items-center" role="group" aria-label="Editing history">
                <button type="button" onClick={undo} disabled={!canUndo} className="rounded-lg p-2 hover:bg-gray-100 disabled:opacity-30" aria-label="Undo"><Undo2 size={18} aria-hidden="true" /></button>
                <button type="button" onClick={redo} disabled={!canRedo} className="rounded-lg p-2 hover:bg-gray-100 disabled:opacity-30" aria-label="Redo"><Redo2 size={18} aria-hidden="true" /></button>
            </div>
            {saveStatus === 'error' && saveError ? <button type="button" onClick={onRetrySave} className="inline-flex items-center gap-1.5 rounded bg-red-100 px-2 py-1 text-xs font-medium text-red-700 hover:bg-red-200"><AlertCircle size={12} aria-hidden="true" />Save failed · Retry</button> : null}
        </div>
    )

    return (
        <AuthoringActionBar
            title={title}
            subtitle="Changes stay in draft until this page is explicitly published."
            backAction={{ label: 'Back', onClick: onBack }}
            status={<AuthoringStatusBadge state={statusState}>{statusLabel}</AuthoringStatusBadge>}
            leading={leading}
            previewAction={{ label: 'Preview Saved Draft', onClick: onPreview, disabled: isSaving || isPublishing, title: 'Saves this draft before opening preview' }}
            historyAction={{ label: 'StoreConfig History', onClick: onHistory, disabled: isSaving || isPublishing }}
            discardAction={{ label: 'Discard', onClick: onDiscard, disabled: !isDirty || isSaving || isPublishing }}
            saveAction={{ label: 'Save Draft', busyLabel: 'Saving…', onClick: onSave, disabled: !isDirty || isPublishing, busy: isSaving }}
            publishAction={{ label: showTemplateControls ? 'Publish Product Template' : 'Publish Homepage', busyLabel: 'Publishing…', onClick: onPublish, disabled: isSaving, busy: isPublishing }}
        />
    )
}
