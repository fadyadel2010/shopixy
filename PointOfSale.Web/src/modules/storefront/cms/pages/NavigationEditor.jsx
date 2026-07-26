/**
 * Navigation Editor
 * Phase 9.11: CMS Pages & Navigation Management
 * 
 * Route: /storefront/cms/navigation
 * 
 * Features:
 * - Two tabs: Header Navigation, Footer Navigation
 * - Editable Draft state
 * - Navigation Link Types: Internal, CMS Page, External
 * - Drag & reorder links
 * - Save Draft
 * - Publish Navigation
 */

import { useState, useEffect, useCallback, useRef } from 'react'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import {
    ArrowLeft,
    Save,
    Globe,
    Plus,
    Trash2,
    GripVertical,
    ChevronUp,
    ChevronDown,
    Loader2,
    ExternalLink,
    FileText,
    Home,
    Link2,
    Edit2,
    X,
    Check,
    RefreshCcw,
} from 'lucide-react'
import { cmsService, extractCmsConflict } from '../services'
import { useCmsStore } from '../store'
import api, { isRequestCanceled } from '../../../../services/api'

const LINK_TYPES = [
    { type: 'internal', label: 'Internal Page', labelAr: 'صفحة داخلية', icon: Home },
    { type: 'cms', label: 'CMS Page', labelAr: 'صفحة محتوى', icon: FileText },
    { type: 'category', label: 'Category', labelAr: 'فئة', icon: Link2 },
    { type: 'external', label: 'External URL', labelAr: 'رابط خارجي', icon: ExternalLink },
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

export function NavigationEditor() {
    const { t, i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const navigate = useNavigate()

    // Store
    const navigation = useCmsStore((s) => s.navigation)
    const setNavigation = useCmsStore((s) => s.setNavigation)
    const updateNavigationHeader = useCmsStore((s) => s.updateNavigationHeader)
    const updateNavigationFooter = useCmsStore((s) => s.updateNavigationFooter)
    const isNavigationDirty = useCmsStore((s) => s.isNavigationDirty)
    const setNavigationDirty = useCmsStore((s) => s.setNavigationDirty)
    const reset = useCmsStore((s) => s.reset)

    // Local state
    const [isLoading, setIsLoading] = useState(true)
    const [isSaving, setIsSaving] = useState(false)
    const [isPublishing, setIsPublishing] = useState(false)
    const [error, setError] = useState(null)
    const [saveStatus, setSaveStatus] = useState('idle')
    const [saveErrorBanner, setSaveErrorBanner] = useState(null)
    const [publishStatus, setPublishStatus] = useState('idle')
    const [publishErrorBanner, setPublishErrorBanner] = useState(null)
    const [conflict, setConflict] = useState(null)
    const [lastSavedAt, setLastSavedAt] = useState(null)
    const [activeTab, setActiveTab] = useState('header')
    const [editingLink, setEditingLink] = useState(null)
    const [showAddModal, setShowAddModal] = useState(false)
    const [categories, setCategories] = useState([]) // Categories for dropdown

    // Refs for debounced save
    const saveTimeoutRef = useRef(null)
    const saveAbortControllerRef = useRef(null)
    const saveRequestIdRef = useRef(0)
    const navigationLoadAbortRef = useRef(null)
    const categoriesLoadAbortRef = useRef(null)

    const clearScheduledSave = useCallback((abortRequest = false) => {
        if (saveTimeoutRef.current) {
            clearTimeout(saveTimeoutRef.current)
            saveTimeoutRef.current = null
        }

        if (abortRequest && saveAbortControllerRef.current) {
            saveAbortControllerRef.current.abort()
            saveAbortControllerRef.current = null
            setIsSaving(false)
        }
    }, [])

    // Load navigation and categories
    useEffect(() => {
        loadNavigation()
        loadCategories()
        return () => {
            reset()
            clearScheduledSave(true)
            navigationLoadAbortRef.current?.abort()
            categoriesLoadAbortRef.current?.abort()
        }
    }, [clearScheduledSave, reset])

    const loadCategories = async () => {
        categoriesLoadAbortRef.current?.abort()
        const controller = new AbortController()
        categoriesLoadAbortRef.current = controller

        try {
            const response = await api.get('/categories/tree', { signal: controller.signal })
            // Flatten categories for dropdown with hierarchy indication
            const flatCategories = flattenCategories(response.data || [])
            setCategories(flatCategories)
        } catch (err) {
            if (isRequestCanceled(err)) {
                return
            }
            console.error('Failed to load categories:', err)
            // Non-critical - continue without categories
        } finally {
            if (categoriesLoadAbortRef.current === controller) {
                categoriesLoadAbortRef.current = null
            }
        }
    }

    // Helper to flatten category tree with indentation
    const flattenCategories = (cats, level = 0, result = []) => {
        for (const cat of cats) {
            result.push({
                id: cat.id,
                name: cat.name,
                nameAr: cat.nameAr,
                slug: cat.slug,
                level,
                displayName: '\u00A0\u00A0'.repeat(level) + (level > 0 ? '└ ' : '') + cat.name,
            })
            if (cat.children?.length) {
                flattenCategories(cat.children, level + 1, result)
            }
        }
        return result
    }

    useEffect(() => {
        if (isNavigationDirty) {
            setSaveStatus('unsaved')
            setSaveErrorBanner(null)
        }
    }, [isNavigationDirty])

    const loadNavigation = async () => {
        navigationLoadAbortRef.current?.abort()
        const controller = new AbortController()
        navigationLoadAbortRef.current = controller

        setIsLoading(true)
        setError(null)
        setConflict(null)
        try {
            const data = await cmsService.getNavigation({ signal: controller.signal })
            setNavigation(data)
        } catch (err) {
            if (isRequestCanceled(err)) {
                return
            }
            console.error('Failed to load navigation:', err)
            setError(isRTL ? 'فشل تحميل القائمة' : 'Failed to load navigation')
            // Initialize empty if not found
            setNavigation({
                draft: { header: [], footer: [] },
                live: null,
            })
        } finally {
            if (navigationLoadAbortRef.current === controller) {
                navigationLoadAbortRef.current = null
            }

            if (!controller.signal.aborted) {
                setIsLoading(false)
            }
        }
    }

    const buildNavigationRequest = useCallback(() => {
        if (!navigation?.draft) {
            return null
        }

        return {
            rowVersion: navigation.rowVersion,
            header: (navigation.draft.header || []).map((link, index) => ({
                label: link.label,
                type: link.type,
                path: link.path,
                slug: link.slug,
                openInNewTab: link.openInNewTab || false,
                order: index,
                enabled: link.enabled !== false,
            })),
            footer: (navigation.draft.footer || []).map((link, index) => ({
                label: link.label,
                type: link.type,
                path: link.path,
                slug: link.slug,
                openInNewTab: link.openInNewTab || false,
                order: index,
                enabled: link.enabled !== false,
            })),
        }
    }, [navigation])

    const handleSave = useCallback(async (silent = false) => {
        const request = buildNavigationRequest()
        if (!request) {
            return false
        }

        clearScheduledSave(true)

        const controller = new AbortController()
        saveAbortControllerRef.current = controller
        const requestId = saveRequestIdRef.current + 1
        saveRequestIdRef.current = requestId

        setIsSaving(true)
        setSaveStatus('saving')
        setSaveErrorBanner(null)
        setConflict(null)

        try {
            const savedNavigation = await cmsService.updateNavigation(request, { signal: controller.signal })

            if (controller.signal.aborted || requestId !== saveRequestIdRef.current) {
                return false
            }

            setNavigation(savedNavigation)
            setNavigationDirty(false)
            setLastSavedAt(new Date().toISOString())
            setSaveStatus('saved')
            return savedNavigation
        } catch (err) {
            if (isRequestCanceled(err)) {
                return false
            }

            console.error('Failed to save navigation:', err)
            const conflictState = extractCmsConflict(err)
            
            if (conflictState) {
                // Conflict detected - show conflict banner only
                setSaveStatus('conflict')
                setConflict(conflictState)
            } else {
                // Generic error - show retry banner
                const message = err?.response?.data?.message
                    || (isRTL ? 'فشل حفظ تغييرات التنقل. حاول مرة أخرى.' : 'Failed to save navigation changes. Please try again.')
                setSaveStatus('error')
                setSaveErrorBanner(message)
            }

            if (!silent) {
                setError(null)
            }

            return false
        } finally {
            if (saveAbortControllerRef.current === controller) {
                saveAbortControllerRef.current = null
            }

            if (!controller.signal.aborted && requestId === saveRequestIdRef.current) {
                setIsSaving(false)
            }
        }
    }, [buildNavigationRequest, clearScheduledSave, isRTL, setNavigationDirty])

    // Debounced auto-save
    const debouncedSave = useCallback(() => {
        clearScheduledSave(true)
        setSaveStatus('unsaved')
        setSaveErrorBanner(null)
        saveTimeoutRef.current = setTimeout(() => {
            void handleSave(true)
        }, 2000)
    }, [clearScheduledSave, handleSave])

    // Trigger auto-save when editing
    useEffect(() => {
        if (isNavigationDirty && navigation) {
            debouncedSave()
        }
    }, [isNavigationDirty, navigation, debouncedSave])

    const handlePublish = async () => {
        if (!navigation?.draft) return

        if (!window.confirm(
            isRTL
                ? 'هل أنت متأكد من نشر قوائم التنقل؟'
                : 'Publish navigation? It will be visible on the storefront.'
        )) return

        const savedNavigation = await handleSave(true)
        if (!savedNavigation) {
            return
        }

        setIsPublishing(true)
        setPublishStatus('publishing')
        setPublishErrorBanner(null)
        setConflict(null)
        try {
            await cmsService.publishNavigation(savedNavigation.rowVersion)
            await loadNavigation()
            setLastSavedAt(new Date().toISOString())
            setSaveStatus('saved')
            setPublishStatus('published')
            alert(isRTL ? '✓ تم نشر القوائم!' : '✓ Navigation published!')
        } catch (err) {
            console.error('Failed to publish:', err)
            const conflictState = extractCmsConflict(err)
            
            if (conflictState) {
                // Conflict detected - show conflict banner only
                setPublishStatus('conflict')
                setConflict(conflictState)
            } else {
                // Generic error - show retry banner
                const message = err?.response?.data?.message
                    || (isRTL ? 'فشل نشر القوائم. حاول مرة أخرى.' : 'Failed to publish navigation. Please try again.')
                setPublishStatus('error')
                setPublishErrorBanner(message)
                alert(isRTL ? 'فشل النشر' : 'Failed to publish')
            }
        } finally {
            setIsPublishing(false)
        }
    }

    const handleExit = () => {
        if (isNavigationDirty) {
            if (!window.confirm(
                isRTL
                    ? 'لديك تغييرات غير محفوظة. هل تريد الخروج؟'
                    : 'You have unsaved changes. Exit anyway?'
            )) return
        }
        navigate('/storefront/cms/pages')
    }

    const getCurrentLinks = () => {
        if (!navigation?.draft) return []
        return activeTab === 'header'
            ? (navigation.draft.header || [])
            : (navigation.draft.footer || [])
    }

    const updateLinks = (newLinks) => {
        if (activeTab === 'header') {
            updateNavigationHeader(newLinks)
        } else {
            updateNavigationFooter(newLinks)
        }
    }

    const handleAddLink = (linkData) => {
        const links = getCurrentLinks()
        const newLink = {
            ...linkData,
            order: links.length,
            enabled: true,
        }
        updateLinks([...links, newLink])
        setShowAddModal(false)
    }

    const handleUpdateLink = (index, updates) => {
        const links = getCurrentLinks()
        const updatedLinks = links.map((link, i) =>
            i === index ? { ...link, ...updates } : link
        )
        updateLinks(updatedLinks)
    }

    const handleRemoveLink = (index) => {
        const links = getCurrentLinks()
        const updatedLinks = links.filter((_, i) => i !== index)
        updateLinks(updatedLinks)
    }

    const handleMoveLink = (index, direction) => {
        const links = [...getCurrentLinks()]
        const newIndex = direction === 'up' ? index - 1 : index + 1
        if (newIndex < 0 || newIndex >= links.length) return

        const [removed] = links.splice(index, 1)
        links.splice(newIndex, 0, removed)
        updateLinks(links)
    }

    const lastSavedLabel = formatSavedAt(lastSavedAt)

    if (isLoading) {
        return (
            <div className="flex items-center justify-center h-96">
                <Loader2 size={32} className="animate-spin text-indigo-600" />
            </div>
        )
    }

    return (
        <div className="space-y-6">
            {/* Header */}
            <div className="flex items-center justify-between">
                <div className="flex items-center gap-4">
                    <button
                        onClick={handleExit}
                        className="p-2 text-gray-400 hover:text-gray-600 hover:bg-gray-100 rounded-lg transition-colors"
                    >
                        <ArrowLeft size={20} className={isRTL ? 'rotate-180' : ''} />
                    </button>
                    <div>
                        <h1 className="text-2xl font-bold text-gray-900">
                            {isRTL ? 'إدارة قوائم التنقل' : 'Navigation Editor'}
                        </h1>
                        <p className="text-sm text-gray-500 mt-1">
                            {isRTL
                                ? 'تعديل روابط الهيدر والفوتر في المتجر الإلكتروني'
                                : 'Manage header and footer navigation links'}
                        </p>
                    </div>
                </div>
                <div className="flex items-center gap-3">
                    {isNavigationDirty && (
                        <span className="text-sm text-amber-600">
                            {isRTL ? '• تغييرات غير محفوظة' : '• Unsaved changes'}
                        </span>
                    )}
                    {saveStatus === 'saving' && (
                        <span className="text-sm text-sky-600">
                            {isRTL ? 'جارٍ الحفظ...' : 'Saving...'}
                        </span>
                    )}
                    {saveStatus === 'saved' && lastSavedLabel && !isNavigationDirty && (
                        <span className="text-sm text-emerald-600">
                            {isRTL ? `آخر حفظ ${lastSavedLabel}` : `Last saved ${lastSavedLabel}`}
                        </span>
                    )}
                    {publishStatus === 'publishing' && (
                        <span className="text-sm text-sky-600" role="status" aria-live="polite">
                            {isRTL ? 'جارٍ النشر...' : 'Publishing...'}
                        </span>
                    )}
                    {publishStatus === 'published' && (
                        <span className="text-sm text-emerald-600" role="status" aria-live="polite">
                            {isRTL ? '✓ تم النشر' : '✓ Published'}
                        </span>
                    )}
                    <button
                        onClick={() => void handleSave(false)}
                        disabled={isSaving}
                        className="inline-flex items-center gap-2 px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50 disabled:opacity-50 transition-colors"
                    >
                        {isSaving ? (
                            <Loader2 size={16} className="animate-spin" />
                        ) : (
                            <Save size={16} />
                        )}
                        {isRTL ? 'حفظ المسودة' : 'Save Draft'}
                    </button>
                    <button
                        onClick={handlePublish}
                        disabled={isPublishing}
                        className="inline-flex items-center gap-2 px-4 py-2 text-sm font-medium text-white bg-green-600 rounded-lg hover:bg-green-700 disabled:opacity-50 transition-colors"
                    >
                        {isPublishing ? (
                            <Loader2 size={16} className="animate-spin" />
                        ) : (
                            <Globe size={16} />
                        )}
                        {isRTL ? 'نشر' : 'Publish'}
                    </button>
                </div>
            </div>

            {/* Error */}
            {error && (
                <div className="p-4 bg-red-50 border border-red-200 rounded-lg text-red-700">
                    {error}
                </div>
            )}

            {saveErrorBanner && (
                <div className="p-4 bg-red-50 border border-red-200 rounded-lg">
                    <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
                        <div>
                            <p className="font-medium text-red-900">
                                {isRTL ? 'تعذر حفظ تغييرات التنقل' : 'Navigation changes are not saved'}
                            </p>
                            <p className="mt-1 text-sm text-red-700">{saveErrorBanner}</p>
                        </div>
                        <button
                            onClick={() => void handleSave(false)}
                            className="inline-flex items-center justify-center gap-2 rounded-lg bg-red-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-red-700"
                        >
                            <Save size={16} />
                            <span>{isRTL ? 'إعادة المحاولة' : 'Retry save'}</span>
                        </button>
                    </div>
                </div>
            )}

            {publishErrorBanner && (
                <div className="p-4 bg-red-50 border border-red-200 rounded-lg">
                    <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
                        <div>
                            <p className="font-medium text-red-900">
                                {isRTL ? 'فشل نشر القوائم' : 'Failed to publish navigation'}
                            </p>
                            <p className="mt-1 text-sm text-red-700">{publishErrorBanner}</p>
                        </div>
                        <button
                            onClick={() => void handlePublish()}
                            className="inline-flex items-center justify-center gap-2 rounded-lg bg-red-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-red-700"
                        >
                            <Globe size={16} />
                            <span>{isRTL ? 'إعادة المحاولة' : 'Retry publish'}</span>
                        </button>
                    </div>
                </div>
            )}

            {conflict && (
                <div className="rounded-2xl border border-amber-200 bg-amber-50 px-4 py-4 text-sm text-amber-900">
                    <p className="font-semibold">
                        {isRTL ? 'تم اكتشاف تعارض في القوائم' : 'A newer navigation version is available'}
                    </p>
                    <p className="mt-1 text-amber-800">{conflict.message}</p>
                    <p className="mt-1 text-amber-700">
                        {isRTL
                            ? 'تغييراتك الحالية ما زالت على الشاشة. حمّل آخر نسخة للمراجعة أو احتفظ بمسودتك للدمج اليدوي.'
                            : 'Your current edits are still on screen. Load the latest version to review it, or keep this draft visible and merge manually.'}
                    </p>
                    <div className="mt-3 flex flex-wrap gap-3">
                        <button
                            type="button"
                            onClick={() => {
                                if (conflict.latestVersion) {
                                    setNavigation(conflict.latestVersion)
                                    setNavigationDirty(false)
                                    setEditingLink(null)
                                    setShowAddModal(false)
                                    setLastSavedAt(null)
                                    setSaveStatus('idle')
                                    setPublishStatus('idle')
                                    setSaveErrorBanner(null)
                                    setPublishErrorBanner(null)
                                    setError(null)
                                    setConflict(null)
                                    return
                                }

                                void loadNavigation()
                            }}
                            className="inline-flex items-center gap-2 rounded-xl bg-amber-900 px-4 py-2 text-sm font-medium text-white transition hover:bg-amber-950"
                        >
                            <RefreshCcw size={16} />
                            {isRTL ? 'تحميل آخر نسخة' : 'Load latest version'}
                        </button>
                        <button
                            type="button"
                            onClick={() => setConflict(null)}
                            className="inline-flex items-center gap-2 rounded-xl border border-amber-300 bg-white px-4 py-2 text-sm font-medium text-amber-900 transition hover:bg-amber-100"
                        >
                            {isRTL ? 'الاحتفاظ بمسودتي' : 'Keep my draft'}
                        </button>
                    </div>
                </div>
            )}

            {/* Tabs */}
            <div className="border-b border-gray-200">
                <nav className="flex gap-4">
                    <button
                        onClick={() => setActiveTab('header')}
                        className={`px-4 py-2 text-sm font-medium border-b-2 transition-colors ${activeTab === 'header'
                            ? 'border-indigo-600 text-indigo-600'
                            : 'border-transparent text-gray-500 hover:text-gray-700'
                            }`}
                    >
                        {isRTL ? 'قائمة الهيدر' : 'Header Navigation'}
                        <span className="ml-2 text-xs bg-gray-100 text-gray-600 px-2 py-0.5 rounded-full">
                            {navigation?.draft?.header?.length || 0}
                        </span>
                    </button>
                    <button
                        onClick={() => setActiveTab('footer')}
                        className={`px-4 py-2 text-sm font-medium border-b-2 transition-colors ${activeTab === 'footer'
                            ? 'border-indigo-600 text-indigo-600'
                            : 'border-transparent text-gray-500 hover:text-gray-700'
                            }`}
                    >
                        {isRTL ? 'قائمة الفوتر' : 'Footer Navigation'}
                        <span className="ml-2 text-xs bg-gray-100 text-gray-600 px-2 py-0.5 rounded-full">
                            {navigation?.draft?.footer?.length || 0}
                        </span>
                    </button>
                </nav>
            </div>

            {/* Links Content */}
            <div className="bg-white rounded-lg shadow-sm border border-gray-200 p-6">
                <div className="flex items-center justify-between mb-6">
                    <h2 className="text-lg font-semibold text-gray-900">
                        {activeTab === 'header'
                            ? (isRTL ? 'روابط الهيدر' : 'Header Links')
                            : (isRTL ? 'روابط الفوتر' : 'Footer Links')}
                    </h2>
                    <button
                        onClick={() => setShowAddModal(true)}
                        className="inline-flex items-center gap-2 px-3 py-1.5 text-sm font-medium text-indigo-600 bg-indigo-50 rounded-lg hover:bg-indigo-100 transition-colors"
                    >
                        <Plus size={16} />
                        {isRTL ? 'إضافة رابط' : 'Add Link'}
                    </button>
                </div>

                {/* Links List */}
                {getCurrentLinks().length === 0 ? (
                    <div className="text-center py-12 text-gray-500 border-2 border-dashed border-gray-200 rounded-lg">
                        <Link2 size={32} className="mx-auto mb-3 text-gray-400" />
                        <p>
                            {isRTL ? 'لا توجد روابط. أضف رابط للبدء.' : 'No links yet. Add one to get started.'}
                        </p>
                    </div>
                ) : (
                    <div className="space-y-2">
                        {getCurrentLinks().map((link, index) => {
                            const linkDef = LINK_TYPES.find((lt) => lt.type === link.type)
                            const Icon = linkDef?.icon || Link2
                            const isEditing = editingLink === index

                            return (
                                <div
                                    key={index}
                                    className="flex items-center gap-3 p-3 bg-gray-50 rounded-lg border border-gray-200"
                                >
                                    <GripVertical size={16} className="text-gray-400 cursor-grab" />
                                    <Icon size={16} className="text-gray-500" />

                                    {isEditing ? (
                                        <LinkEditForm
                                            link={link}
                                            onSave={(updates) => {
                                                handleUpdateLink(index, updates)
                                                setEditingLink(null)
                                            }}
                                            onCancel={() => setEditingLink(null)}
                                            isRTL={isRTL}
                                        />
                                    ) : (
                                        <>
                                            <div className="flex-1">
                                                <span className="text-sm font-medium text-gray-900">
                                                    {link.label}
                                                </span>
                                                <span className="mx-2 text-gray-300">→</span>
                                                <span className="text-sm text-gray-500">
                                                    {link.type === 'cms' && `/${link.slug}`}
                                                    {link.type === 'category' && `/products?category=${link.slug}`}
                                                    {(link.type === 'internal' || link.type === 'external') && link.path}
                                                </span>
                                            </div>
                                            <div className="flex items-center gap-1">
                                                <button
                                                    onClick={() => setEditingLink(index)}
                                                    className="p-1.5 text-gray-400 hover:text-indigo-600 hover:bg-white rounded transition-colors"
                                                >
                                                    <Edit2 size={14} />
                                                </button>
                                                <button
                                                    onClick={() => handleMoveLink(index, 'up')}
                                                    disabled={index === 0}
                                                    className="p-1.5 text-gray-400 hover:text-gray-600 disabled:opacity-30 transition-colors"
                                                >
                                                    <ChevronUp size={14} />
                                                </button>
                                                <button
                                                    onClick={() => handleMoveLink(index, 'down')}
                                                    disabled={index === getCurrentLinks().length - 1}
                                                    className="p-1.5 text-gray-400 hover:text-gray-600 disabled:opacity-30 transition-colors"
                                                >
                                                    <ChevronDown size={14} />
                                                </button>
                                                <button
                                                    onClick={() => {
                                                        if (window.confirm(isRTL ? 'حذف هذا الرابط؟' : 'Delete this link?')) {
                                                            handleRemoveLink(index)
                                                        }
                                                    }}
                                                    className="p-1.5 text-gray-400 hover:text-red-600 hover:bg-red-50 rounded transition-colors"
                                                >
                                                    <Trash2 size={14} />
                                                </button>
                                            </div>
                                        </>
                                    )}
                                </div>
                            )
                        })}
                    </div>
                )}
            </div>

            {/* Add Link Modal */}
            {showAddModal && (
                <AddLinkModal
                    onAdd={handleAddLink}
                    onClose={() => setShowAddModal(false)}
                    isRTL={isRTL}
                    categories={categories}
                />
            )}
        </div>
    )
}

/**
 * Add Link Modal
 */
function AddLinkModal({ onAdd, onClose, isRTL, categories = [] }) {
    const [linkType, setLinkType] = useState('internal')
    const [label, setLabel] = useState('')
    const [path, setPath] = useState('')
    const [slug, setSlug] = useState('')
    const [openInNewTab, setOpenInNewTab] = useState(false)
    const [selectedCategoryId, setSelectedCategoryId] = useState('')

    // Handle category selection
    const handleCategorySelect = (categoryId) => {
        setSelectedCategoryId(categoryId)
        if (categoryId) {
            const cat = categories.find((c) => c.id === categoryId)
            if (cat) {
                setLabel(cat.name) // Auto-fill label
                setSlug(cat.slug || cat.id) // Use slug if available, fallback to ID
            }
        } else {
            setLabel('')
            setSlug('')
        }
    }

    // Reset category selection when type changes
    const handleTypeChange = (newType) => {
        setLinkType(newType)
        if (newType !== 'category') {
            setSelectedCategoryId('')
        }
    }

    const handleSubmit = (e) => {
        e.preventDefault()
        if (!label.trim()) return

        onAdd({
            label: label.trim(),
            type: linkType,
            path: linkType === 'internal' || linkType === 'external' ? path : undefined,
            slug: linkType === 'cms' || linkType === 'category' ? slug : undefined,
            openInNewTab,
        })
    }

    return (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
            <div className="bg-white rounded-lg shadow-xl w-full max-w-md p-6">
                <div className="flex items-center justify-between mb-4">
                    <h3 className="text-lg font-semibold text-gray-900">
                        {isRTL ? 'إضافة رابط' : 'Add Link'}
                    </h3>
                    <button
                        onClick={onClose}
                        className="p-1 text-gray-400 hover:text-gray-600"
                    >
                        <X size={20} />
                    </button>
                </div>

                <form onSubmit={handleSubmit} className="space-y-4">
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                            {isRTL ? 'نوع الرابط' : 'Link Type'}
                        </label>
                        <select
                            value={linkType}
                            onChange={(e) => handleTypeChange(e.target.value)}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500"
                        >
                            {LINK_TYPES.map((lt) => (
                                <option key={lt.type} value={lt.type}>
                                    {isRTL ? lt.labelAr : lt.label}
                                </option>
                            ))}
                        </select>
                    </div>

                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                            {isRTL ? 'نص الرابط' : 'Link Label'} *
                        </label>
                        <input
                            type="text"
                            value={label}
                            onChange={(e) => setLabel(e.target.value)}
                            placeholder={isRTL ? 'مثال: من نحن' : 'e.g. About Us'}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500"
                            required
                        />
                    </div>

                    {(linkType === 'internal' || linkType === 'external') && (
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">
                                {linkType === 'external'
                                    ? (isRTL ? 'الرابط الخارجي' : 'External URL')
                                    : (isRTL ? 'المسار' : 'Path')} *
                            </label>
                            <input
                                type={linkType === 'external' ? 'url' : 'text'}
                                value={path}
                                onChange={(e) => setPath(e.target.value)}
                                placeholder={linkType === 'external' ? 'https://...' : '/products'}
                                className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500"
                                required
                            />
                        </div>
                    )}

                    {linkType === 'category' && (
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">
                                {isRTL ? 'اختر الفئة' : 'Select Category'} *
                            </label>
                            <select
                                value={selectedCategoryId}
                                onChange={(e) => handleCategorySelect(e.target.value)}
                                className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500"
                                required
                            >
                                <option value="">{isRTL ? '-- اختر فئة --' : '-- Select a category --'}</option>
                                {categories.map((cat) => (
                                    <option key={cat.id} value={cat.id}>
                                        {cat.displayName}
                                    </option>
                                ))}
                            </select>
                            {selectedCategoryId && (
                                <p className="mt-1 text-xs text-gray-500">
                                    Slug: {slug}
                                </p>
                            )}
                        </div>
                    )}

                    {linkType === 'cms' && (
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">
                                {isRTL ? 'المعرف (Slug)' : 'Slug'} *
                            </label>
                            <input
                                type="text"
                                value={slug}
                                onChange={(e) => setSlug(e.target.value)}
                                placeholder="about"
                                className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500"
                                required
                            />
                        </div>
                    )}

                    {linkType === 'external' && (
                        <label className="flex items-center gap-2 cursor-pointer">
                            <input
                                type="checkbox"
                                checked={openInNewTab}
                                onChange={(e) => setOpenInNewTab(e.target.checked)}
                                className="w-4 h-4 text-indigo-600 border-gray-300 rounded"
                            />
                            <span className="text-sm text-gray-700">
                                {isRTL ? 'فتح في نافذة جديدة' : 'Open in new tab'}
                            </span>
                        </label>
                    )}

                    <div className="flex items-center justify-end gap-3 pt-4">
                        <button
                            type="button"
                            onClick={onClose}
                            className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50"
                        >
                            {isRTL ? 'إلغاء' : 'Cancel'}
                        </button>
                        <button
                            type="submit"
                            className="px-4 py-2 text-sm font-medium text-white bg-indigo-600 rounded-lg hover:bg-indigo-700"
                        >
                            {isRTL ? 'إضافة' : 'Add'}
                        </button>
                    </div>
                </form>
            </div>
        </div>
    )
}

/**
 * Link Edit Form (inline)
 */
function LinkEditForm({ link, onSave, onCancel, isRTL }) {
    const [label, setLabel] = useState(link.label || '')
    const [path, setPath] = useState(link.path || '')
    const [slug, setSlug] = useState(link.slug || '')

    return (
        <div className="flex-1 flex items-center gap-2">
            <input
                type="text"
                value={label}
                onChange={(e) => setLabel(e.target.value)}
                placeholder={isRTL ? 'نص الرابط' : 'Label'}
                className="flex-1 px-2 py-1 text-sm border border-gray-300 rounded"
            />
            {(link.type === 'internal' || link.type === 'external') && (
                <input
                    type="text"
                    value={path}
                    onChange={(e) => setPath(e.target.value)}
                    placeholder={isRTL ? 'المسار' : 'Path'}
                    className="flex-1 px-2 py-1 text-sm border border-gray-300 rounded"
                />
            )}
            {(link.type === 'cms' || link.type === 'category') && (
                <input
                    type="text"
                    value={slug}
                    onChange={(e) => setSlug(e.target.value)}
                    placeholder="Slug"
                    className="flex-1 px-2 py-1 text-sm border border-gray-300 rounded"
                />
            )}
            <button
                onClick={() => onSave({ label, path, slug })}
                className="p-1.5 text-green-600 hover:bg-green-50 rounded"
            >
                <Check size={14} />
            </button>
            <button
                onClick={onCancel}
                className="p-1.5 text-gray-400 hover:text-gray-600"
            >
                <X size={14} />
            </button>
        </div>
    )
}

export default NavigationEditor
