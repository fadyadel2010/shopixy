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
import { useLocation, useNavigate } from 'react-router-dom'
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
    Package,
    Layers3,
    Edit2,
    X,
    Check,
    RefreshCcw,
    AlertTriangle,
} from 'lucide-react'
import {
    cmsService,
    extractNavigationAdmissionError,
    extractNavigationConflict,
} from '../services'
import { useCmsStore } from '../store'
import { isRequestCanceled } from '../../../../services/api'
import {
    AuthoringActionBar,
    AuthoringConfirmDialog,
    AuthoringConflictDialog,
    AuthoringDialog,
    AuthoringEmptyState,
    AuthoringFeedback,
    AuthoringLoadingState,
    AuthoringStatusBadge,
    UnsavedChangesDialog,
    useAuthoringGuard,
} from '../../authoringPlatform'

const LINK_TYPES = [
    { type: 'custom', label: 'Custom Page', labelAr: 'صفحة مخصصة', icon: Home },
    { type: 'cms', label: 'Page', labelAr: 'صفحة محتوى', icon: FileText },
    { type: 'category', label: 'Category', labelAr: 'فئة', icon: Link2 },
    { type: 'product', label: 'Product', labelAr: 'منتج', icon: Package },
    { type: 'collection', label: 'Collection', labelAr: 'مجموعة', icon: Layers3 },
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

const getNavigationDraftFingerprint = (value) => JSON.stringify(value?.draft ?? null)

const resolveNavigationView = (pathname, search) => {
    return new URLSearchParams(search).get('view') === 'footer' ? 'footer' : 'header'
}

export function NavigationEditor() {
    const { t, i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const navigate = useNavigate()
    const location = useLocation()

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
    const [isPublishConfirmOpen, setIsPublishConfirmOpen] = useState(false)
    const [lastSavedAt, setLastSavedAt] = useState(null)
    const [activeTab, setActiveTab] = useState(() => resolveNavigationView(location.pathname, location.search))
    const [editingLink, setEditingLink] = useState(null)
    const [showAddModal, setShowAddModal] = useState(false)
    const [deleteLinkIndex, setDeleteLinkIndex] = useState(null)
    const [targets, setTargets] = useState({ categories: [], products: [], cmsPages: [], collections: [] })

    // Refs for debounced save
    const saveTimeoutRef = useRef(null)
    const saveAbortControllerRef = useRef(null)
    const savePromiseRef = useRef(null)
    const saveRequestIdRef = useRef(0)
    const navigationLoadAbortRef = useRef(null)
    const targetsLoadAbortRef = useRef(null)
    const acceptedNavigationRef = useRef(null)

    useEffect(() => {
        setActiveTab(resolveNavigationView(location.pathname, location.search))
    }, [location.pathname, location.search])

    const selectNavigationView = (view) => {
        setActiveTab(view)
        if (location.pathname === '/storefront/cms/navigation') {
            navigate(`/storefront/cms/navigation?view=${view}`)
            return
        }
        navigate(`/storefront/navigation?view=${view}`)
    }

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
        loadTargets()
        return () => {
            reset()
            clearScheduledSave(true)
            navigationLoadAbortRef.current?.abort()
            targetsLoadAbortRef.current?.abort()
        }
    }, [clearScheduledSave, reset])

    const loadTargets = async () => {
        targetsLoadAbortRef.current?.abort()
        const controller = new AbortController()
        targetsLoadAbortRef.current = controller

        try {
            const admittedTargets = await cmsService.getNavigationTargets({ signal: controller.signal })
            setTargets(admittedTargets)
        } catch (err) {
            if (isRequestCanceled(err)) {
                return
            }
            console.error('Failed to load navigation targets:', err)
            setTargets({ categories: [], products: [], cmsPages: [], collections: [] })
        } finally {
            if (targetsLoadAbortRef.current === controller) {
                targetsLoadAbortRef.current = null
            }
        }
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
            acceptedNavigationRef.current = data
        } catch (err) {
            if (isRequestCanceled(err)) {
                return
            }
            console.error('Failed to load navigation:', err)
            const admission = extractNavigationAdmissionError(err)
            setError(admission?.message || (isRTL ? 'فشل تحميل القائمة' : 'Failed to load navigation'))
            setNavigation(null)
        } finally {
            if (navigationLoadAbortRef.current === controller) {
                navigationLoadAbortRef.current = null
            }

            if (!controller.signal.aborted) {
                setIsLoading(false)
            }
        }
    }

    const handleSave = useCallback((silent = false, candidate = null) => {
        clearScheduledSave(false)

        const saveOperation = async () => {
            const submittedNavigation = candidate ?? useCmsStore.getState().navigation
            if (!submittedNavigation?.draft) {
                return false
            }

            const controller = new AbortController()
            saveAbortControllerRef.current = controller
            const requestId = saveRequestIdRef.current + 1
            saveRequestIdRef.current = requestId
            const submittedFingerprint = getNavigationDraftFingerprint(submittedNavigation)

            setIsSaving(true)
            setSaveStatus('saving')
            setSaveErrorBanner(null)
            setConflict(null)

            try {
                const savedNavigation = await cmsService.updateNavigation(submittedNavigation, { signal: controller.signal })

                if (controller.signal.aborted) {
                    return false
                }

                const currentNavigation = useCmsStore.getState().navigation
                const hasNewerLocalEdits = getNavigationDraftFingerprint(currentNavigation) !== submittedFingerprint
                acceptedNavigationRef.current = savedNavigation
                setLastSavedAt(new Date().toISOString())

                if (hasNewerLocalEdits) {
                    setNavigation({
                        ...savedNavigation,
                        draft: currentNavigation.draft,
                        hasPendingChanges: true,
                    })
                    setNavigationDirty(true)
                    setSaveStatus('unsaved')
                    return false
                } else {
                    setNavigation(savedNavigation)
                    setNavigationDirty(false)
                    setSaveStatus('saved')
                }

                return savedNavigation
            } catch (err) {
                if (isRequestCanceled(err)) {
                    return false
                }

                console.error('Failed to save navigation:', err)
                const conflictState = extractNavigationConflict(err)
                const admissionState = extractNavigationAdmissionError(err)

                if (conflictState) {
                    setSaveStatus('conflict')
                    setConflict(conflictState)
                } else if (admissionState) {
                    setSaveStatus('error')
                    setSaveErrorBanner(admissionState.message)
                } else {
                    const message = err?.response?.data?.message
                        || err?.message
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
        }

        const queuedSave = (savePromiseRef.current ?? Promise.resolve()).then(saveOperation)
        const trackedSave = queuedSave.finally(() => {
            if (savePromiseRef.current === trackedSave) {
                savePromiseRef.current = null
            }
        })
        savePromiseRef.current = trackedSave
        return trackedSave
    }, [clearScheduledSave, isRTL, setNavigation, setNavigationDirty])

    // Debounced auto-save
    const debouncedSave = useCallback(() => {
        clearScheduledSave(false)
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
        if (!navigation?.draft || (!isNavigationDirty && !navigation.hasPendingChanges)) return

        const savedNavigation = await handleSave(true)
        if (!savedNavigation) {
            return
        }

        setIsPublishing(true)
        setPublishStatus('publishing')
        setPublishErrorBanner(null)
        setConflict(null)
        try {
            const publishedNavigation = await cmsService.publishNavigation(savedNavigation.rowVersion)
            setNavigation(publishedNavigation)
            acceptedNavigationRef.current = publishedNavigation
            setNavigationDirty(false)
            setLastSavedAt(new Date().toISOString())
            setSaveStatus('saved')
            setPublishStatus('published')
            setIsPublishConfirmOpen(false)
        } catch (err) {
            console.error('Failed to publish:', err)
            const conflictState = extractNavigationConflict(err)
            const admissionState = extractNavigationAdmissionError(err)
            
            if (conflictState) {
                // Conflict detected - show conflict banner only
                setIsPublishConfirmOpen(false)
                setPublishStatus('conflict')
                setConflict(conflictState)
            } else if (admissionState) {
                setPublishStatus('error')
                setPublishErrorBanner(admissionState.message)
            } else {
                // Generic error - show retry banner
                const message = err?.response?.data?.message
                    || err?.message
                    || (isRTL ? 'فشل نشر القوائم. حاول مرة أخرى.' : 'Failed to publish navigation. Please try again.')
                setPublishStatus('error')
                setPublishErrorBanner(message)
            }
        } finally {
            setIsPublishing(false)
        }
    }

    const { requestNavigation, unsavedDialogProps } = useAuthoringGuard({
        isDirty: isNavigationDirty,
        isBusy: isSaving || isPublishing,
        onSave: () => handleSave(false),
    })

    const handleDiscard = () => {
        if (!acceptedNavigationRef.current) return
        clearScheduledSave(true)
        setNavigation(acceptedNavigationRef.current)
        setNavigationDirty(false)
        setSaveStatus('idle')
        setSaveErrorBanner(null)
        setConflict(null)
    }

    const handleLoadLatest = () => {
        if (conflict?.latestVersion) {
            setNavigation(conflict.latestVersion)
            acceptedNavigationRef.current = conflict.latestVersion
            setNavigationDirty(false)
            setEditingLink(null)
            setShowAddModal(false)
            setSaveStatus('idle')
            setPublishStatus('idle')
            setSaveErrorBanner(null)
            setPublishErrorBanner(null)
            setConflict(null)
            return
        }
        void loadNavigation()
    }

    const handleRetryConflict = async () => {
        if (!conflict?.latestVersion || !navigation) return
        const retainedDraft = navigation
        const retryCandidate = { ...retainedDraft, rowVersion: conflict.latestVersion.rowVersion }
        setConflict(null)
        await handleSave(false, retryCandidate)
    }

    const handleExit = () => requestNavigation('/storefront')

    const getCurrentLinks = () => {
        if (!navigation?.draft) return []
        return activeTab === 'header'
            ? (navigation.draft.header || [])
            : (navigation.draft.footer || [])
    }

    const getLinkDestination = (link) => {
        if (link.type === 'cms') return link.slug ? `/${link.slug}` : link.targetId
        if (link.type === 'category') return link.slug ? `/products?category=${link.slug}` : link.targetId
        if (link.type === 'product' || link.type === 'collection') return link.path || link.targetId || link.collectionKey
        if (link.type === 'custom' || link.type === 'external') return link.path
        return ''
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
            i === index
                ? {
                    ...link,
                    ...updates,
                    validationStatus: null,
                    validationCode: null,
                    validationMessage: null,
                }
                : link
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
        return <AuthoringLoadingState label="Loading Navigation draft…" />
    }

    if (!navigation) {
        return <AuthoringFeedback kind="error" title="Navigation authoring unavailable" message={error || (isRTL ? 'تعذر تحميل التنقل' : 'Navigation authoring is unavailable.')} action={{ label: isRTL ? 'إعادة المحاولة' : 'Retry load', onClick: () => void loadNavigation(), icon: RefreshCcw }} />
    }

    const draftValidationWarnings = (navigation.validationWarnings || [])
        .filter((warning) => warning.snapshot === 'draft')

    return (
        <div className="space-y-6">
            <AuthoringActionBar
                title="Navigation"
                subtitle={lastSavedLabel && !isNavigationDirty ? `Draft last saved ${lastSavedLabel}. Header and Footer publish together.` : 'Header and Footer publish together as one Navigation scope.'}
                backAction={{ label: 'Back', onClick: handleExit }}
                status={<AuthoringStatusBadge state={isPublishing ? 'publishing' : isSaving ? 'saving' : isNavigationDirty ? 'dirty' : publishStatus === 'published' ? 'published' : 'draft'} />}
                historyAction={{ label: 'StoreConfig History', onClick: () => requestNavigation('/storefront/storeconfig-version-history') }}
                discardAction={{ label: 'Discard', onClick: handleDiscard, disabled: !isNavigationDirty || isSaving || isPublishing }}
                saveAction={{ label: 'Save Draft', busyLabel: 'Saving…', onClick: () => void handleSave(false), disabled: !isNavigationDirty || isPublishing, busy: isSaving }}
                publishAction={{ label: 'Publish Header and Footer Navigation', busyLabel: 'Publishing…', onClick: () => setIsPublishConfirmOpen(true), disabled: isSaving || (!isNavigationDirty && !navigation.hasPendingChanges), busy: isPublishing }}
            />

            {publishStatus === 'published' ? <AuthoringFeedback kind="success" title="Navigation published" message="Header and Footer Navigation are now live." /> : null}

            {/* Error */}
            {error ? <AuthoringFeedback kind="error" title="Navigation action failed" message={error} /> : null}

            {saveErrorBanner ? <AuthoringFeedback kind="error" title={isRTL ? 'تعذر حفظ تغييرات التنقل' : 'Navigation changes are not saved'} message={saveErrorBanner} action={{ label: isRTL ? 'إعادة المحاولة' : 'Retry save', onClick: () => void handleSave(false), icon: Save }} /> : null}

            {publishErrorBanner ? <AuthoringFeedback kind="error" title={isRTL ? 'فشل نشر القوائم' : 'Failed to publish navigation'} message={publishErrorBanner} action={{ label: isRTL ? 'إعادة المحاولة' : 'Retry publish', onClick: () => void handlePublish(), icon: Globe }} /> : null}

            {draftValidationWarnings.length > 0 ? <AuthoringFeedback kind="warning" title={draftValidationWarnings.length === 1 ? 'One navigation link needs attention' : `${draftValidationWarnings.length} navigation links need attention`} message="Invalid links remain editable. Repair or remove the highlighted rows before saving or publishing." /> : null}

            {/* Tabs */}
            <div className="border-b border-gray-200">
                <nav aria-label={isRTL ? 'طرق عرض التنقل' : 'Navigation views'} className="flex gap-4" role="tablist">
                    <button
                        type="button"
                        role="tab"
                        aria-selected={activeTab === 'header'}
                        onClick={() => selectNavigationView('header')}
                        className={`px-4 py-2 text-sm font-medium border-b-2 transition-colors ${activeTab === 'header'
                            ? 'border-indigo-600 text-indigo-600'
                            : 'border-transparent text-gray-500 hover:text-gray-700'
                            }`}
                    >
                        {isRTL ? 'رأس الصفحة' : 'Header'}
                        <span className="ms-2 text-xs bg-gray-100 text-gray-600 px-2 py-0.5 rounded-full">
                            {navigation?.draft?.header?.length || 0}
                        </span>
                    </button>
                    <button
                        type="button"
                        role="tab"
                        aria-selected={activeTab === 'footer'}
                        onClick={() => selectNavigationView('footer')}
                        className={`px-4 py-2 text-sm font-medium border-b-2 transition-colors ${activeTab === 'footer'
                            ? 'border-indigo-600 text-indigo-600'
                            : 'border-transparent text-gray-500 hover:text-gray-700'
                            }`}
                    >
                        {isRTL ? 'روابط التذييل' : 'Footer Links'}
                        <span className="ms-2 text-xs bg-gray-100 text-gray-600 px-2 py-0.5 rounded-full">
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
                    <AuthoringEmptyState
                        title={isRTL ? 'لا توجد روابط' : 'No Navigation links'}
                        description={isRTL ? 'أضف رابط للبدء.' : 'Add a link to this shared Navigation draft.'}
                        action={{ label: isRTL ? 'إضافة رابط' : 'Add Link', onClick: () => setShowAddModal(true), icon: Plus }}
                    />
                ) : (
                    <div className="space-y-2">
                        {getCurrentLinks().map((link, index) => {
                            const linkDef = LINK_TYPES.find((lt) => lt.type === link.type)
                            const Icon = linkDef?.icon || Link2
                            const isEditing = editingLink === index
                            const isInvalid = link.validationStatus === 'invalid'

                            return (
                                <div
                                    key={index}
                                    className={`flex min-w-0 items-start gap-3 p-3 rounded-lg border sm:items-center ${
                                        isInvalid
                                            ? 'border-amber-300 bg-amber-50'
                                            : 'border-gray-200 bg-gray-50'
                                    }`}
                                >
                                    <GripVertical size={16} className="text-gray-400 cursor-grab" />
                                    {isInvalid ? (
                                        <AlertTriangle size={16} className="text-amber-600" />
                                    ) : (
                                        <Icon size={16} className="text-gray-500" />
                                    )}

                                    {isEditing ? (
                                        <LinkEditForm
                                            link={link}
                                            targets={targets}
                                            onSave={(updates) => {
                                                handleUpdateLink(index, updates)
                                                setEditingLink(null)
                                            }}
                                            onCancel={() => setEditingLink(null)}
                                            isRTL={isRTL}
                                        />
                                    ) : (
                                        <>
                                            <div className="min-w-0 flex-1">
                                                <span className="text-sm font-medium text-gray-900">
                                                    {link.label}
                                                </span>
                                                <span className="mx-2 text-gray-300">{isRTL ? '←' : '→'}</span>
                                                <span className="break-all text-sm text-gray-500">
                                                    {getLinkDestination(link)}
                                                </span>
                                                {isInvalid && (
                                                    <p className="mt-1 text-xs font-medium text-amber-800">
                                                        {link.validationMessage || 'This navigation target is no longer valid.'}
                                                    </p>
                                                )}
                                                {!link.enabled && (
                                                    <p className="mt-1 text-xs font-medium text-gray-500">
                                                        {isRTL ? 'معطل' : 'Disabled'}
                                                    </p>
                                                )}
                                            </div>
                                            <div className="flex shrink-0 items-center gap-1">
                                                <button
                                                    onClick={() => setEditingLink(index)}
                                                    aria-label={isRTL ? 'تعديل رابط التنقل' : 'Edit navigation link'}
                                                    className="p-1.5 text-gray-400 hover:text-indigo-600 hover:bg-white rounded transition-colors"
                                                >
                                                    <Edit2 size={14} />
                                                </button>
                                                <button
                                                    onClick={() => handleMoveLink(index, 'up')}
                                                    aria-label={isRTL ? 'نقل الرابط لأعلى' : 'Move navigation link up'}
                                                    disabled={index === 0}
                                                    className="p-1.5 text-gray-400 hover:text-gray-600 disabled:opacity-30 transition-colors"
                                                >
                                                    <ChevronUp size={14} />
                                                </button>
                                                <button
                                                    onClick={() => handleMoveLink(index, 'down')}
                                                    aria-label={isRTL ? 'نقل الرابط لأسفل' : 'Move navigation link down'}
                                                    disabled={index === getCurrentLinks().length - 1}
                                                    className="p-1.5 text-gray-400 hover:text-gray-600 disabled:opacity-30 transition-colors"
                                                >
                                                    <ChevronDown size={14} />
                                                </button>
                                                <button
                                                    onClick={() => setDeleteLinkIndex(index)}
                                                    aria-label="Delete navigation link"
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
                    targets={targets}
                />
            )}
            <AuthoringConfirmDialog
                open={isPublishConfirmOpen}
                title="Publish Header and Footer Navigation?"
                description="Publishing replaces the live Header and Footer Navigation together. Other StoreConfig surfaces are not changed."
                confirmLabel="Publish Header and Footer Navigation"
                confirmTone="publish"
                busy={isPublishing}
                onClose={() => setIsPublishConfirmOpen(false)}
                onConfirm={handlePublish}
            />
            <AuthoringConfirmDialog
                open={deleteLinkIndex != null}
                title="Delete Navigation Link?"
                description="The link is removed from this draft. Save or publish is still required to commit the change."
                confirmLabel="Delete Link"
                confirmTone="danger"
                onClose={() => setDeleteLinkIndex(null)}
                onConfirm={() => {
                    handleRemoveLink(deleteLinkIndex)
                    setDeleteLinkIndex(null)
                }}
            />
            <AuthoringConflictDialog
                open={conflict != null}
                message={conflict?.message}
                hasLatest={Boolean(conflict?.latestVersion)}
                onClose={() => setConflict(null)}
                onLoadLatest={handleLoadLatest}
                onRetry={handleRetryConflict}
            />
            <UnsavedChangesDialog {...unsavedDialogProps} />
        </div>
    )
}

/**
 * Add Link Modal
 */
function AddLinkModal({ onAdd, onClose, isRTL, targets }) {
    const [linkType, setLinkType] = useState('custom')
    const [label, setLabel] = useState('')
    const [labelAr, setLabelAr] = useState('')
    const [path, setPath] = useState('')
    const [openInNewTab, setOpenInNewTab] = useState(false)
    const [targetId, setTargetId] = useState('')
    const [collectionKey, setCollectionKey] = useState('')

    const targetOptions = linkType === 'category'
        ? targets.categories
        : linkType === 'product'
            ? targets.products
            : linkType === 'cms'
                ? targets.cmsPages
                : []

    const handleTargetSelect = (selectedId) => {
        setTargetId(selectedId)
        const selected = targetOptions.find((option) => option.targetId === selectedId)
        if (selected) {
            setLabel(selected.label)
        }
    }

    const handleTypeChange = (newType) => {
        setLinkType(newType)
        setTargetId('')
        setCollectionKey('')
        setPath('')
        setLabel('')
        setLabelAr('')
    }

    const handleSubmit = (e) => {
        e.preventDefault()
        if (!label.trim()) return

        onAdd({
            label: label.trim(),
            labelAr: labelAr.trim() || null,
            type: linkType,
            targetId: ['category', 'product', 'cms'].includes(linkType) ? targetId : null,
            collectionKey: linkType === 'collection' ? collectionKey : null,
            path: linkType === 'custom' || linkType === 'external' ? path.trim() : null,
            openInNewTab,
        })
    }

    return (
        <AuthoringDialog
            open
            title={isRTL ? 'إضافة رابط' : 'Add Link'}
            onClose={onClose}
            panelClassName="!max-w-md"
        >
                <form onSubmit={handleSubmit} className="mt-4 space-y-4">
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
                            {isRTL ? 'نص الرابط بالعربية (اختياري)' : 'Arabic Label (optional)'}
                        </label>
                        <input
                            type="text"
                            dir="rtl"
                            value={labelAr}
                            onChange={(e) => setLabelAr(e.target.value)}
                            placeholder="مثال: من نحن"
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500"
                        />
                    </div>

                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                            {isRTL ? 'نص الرابط بالإنجليزية' : 'Link Label'} *
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

                    {(linkType === 'custom' || linkType === 'external') && (
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

                    {['category', 'product', 'cms'].includes(linkType) && (
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">
                                {linkType === 'category'
                                    ? (isRTL ? 'اختر الفئة' : 'Select Category')
                                    : linkType === 'product'
                                        ? (isRTL ? 'اختر المنتج' : 'Select Product')
                                        : (isRTL ? 'اختر صفحة المحتوى' : 'Select Page')} *
                            </label>
                            <select
                                value={targetId}
                                onChange={(e) => handleTargetSelect(e.target.value)}
                                className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500"
                                required
                            >
                                <option value="">
                                    {linkType === 'category'
                                        ? (isRTL ? '-- اختر فئة --' : '-- Select a category --')
                                        : linkType === 'product'
                                            ? (isRTL ? '-- اختر منتجًا --' : '-- Select a product --')
                                            : (isRTL ? '-- اختر صفحة --' : '-- Select a page --')}
                                </option>
                                {targetOptions.map((option) => (
                                    <option key={option.targetId} value={option.targetId}>
                                        {option.label}{option.state === 'draft' ? ' (Draft)' : ''}
                                    </option>
                                ))}
                            </select>
                            {targetId && (
                                <p className="mt-1 text-xs text-gray-500">
                                    Backend-issued identity: {targetId}
                                </p>
                            )}
                        </div>
                    )}

                    {linkType === 'collection' && (
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">
                                {isRTL ? 'Collection' : 'Select Collection'} *
                            </label>
                            <select
                                value={collectionKey}
                                onChange={(event) => {
                                    const key = event.target.value
                                    setCollectionKey(key)
                                    const selected = targets.collections.find((option) => option.collectionKey === key)
                                    if (selected) setLabel(selected.label)
                                }}
                                className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500"
                                required
                            >
                                <option value="">-- Select a collection --</option>
                                {targets.collections.map((option) => (
                                    <option key={option.collectionKey} value={option.collectionKey}>
                                        {option.label}
                                    </option>
                                ))}
                            </select>
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
        </AuthoringDialog>
    )
}

/**
 * Link Edit Form (inline)
 */
function LinkEditForm({ link, onSave, onCancel, isRTL, targets }) {
    const [label, setLabel] = useState(link.label || '')
    const [labelAr, setLabelAr] = useState(link.labelAr || '')
    const [path, setPath] = useState(link.path || '')
    const [targetId, setTargetId] = useState(link.targetId || '')
    const [collectionKey, setCollectionKey] = useState(link.collectionKey || '')
    const [enabled, setEnabled] = useState(link.enabled !== false)
    const [openInNewTab, setOpenInNewTab] = useState(link.openInNewTab === true)
    const targetOptions = link.type === 'category'
        ? targets.categories
        : link.type === 'product'
            ? targets.products
            : link.type === 'cms'
                ? targets.cmsPages
                : []

    const submit = (event) => {
        event.preventDefault()
        if (!label.trim()) return

        onSave({
            label: label.trim(),
            labelAr: labelAr.trim() || null,
            path: link.type === 'custom' || link.type === 'external' ? path.trim() : link.path,
            targetId: ['category', 'product', 'cms'].includes(link.type) ? targetId : link.targetId,
            collectionKey: link.type === 'collection' ? collectionKey : link.collectionKey,
            enabled,
            openInNewTab: link.type === 'external' && openInNewTab,
        })
    }

    return (
        <form onSubmit={submit} className="flex min-w-0 flex-1 flex-col gap-2 sm:flex-row sm:flex-wrap sm:items-center">
            <label className="sr-only" htmlFor="navigation-link-label">{isRTL ? 'نص الرابط' : 'Link label'}</label>
            <input
                id="navigation-link-label"
                type="text"
                value={label}
                onChange={(e) => setLabel(e.target.value)}
                placeholder={isRTL ? 'نص الرابط' : 'Label'}
                className="flex-1 px-2 py-1 text-sm border border-gray-300 rounded"
                required
            />
            <label className="sr-only" htmlFor="navigation-link-label-ar">{isRTL ? 'النص العربي' : 'Arabic label'}</label>
            <input
                id="navigation-link-label-ar"
                type="text"
                dir="rtl"
                value={labelAr}
                onChange={(e) => setLabelAr(e.target.value)}
                placeholder={isRTL ? 'النص العربي' : 'Arabic label'}
                className="flex-1 px-2 py-1 text-sm border border-gray-300 rounded"
            />
            {(link.type === 'custom' || link.type === 'external') && (
                <>
                    <label className="sr-only" htmlFor="navigation-link-path">{isRTL ? 'المسار' : 'Path'}</label>
                    <input
                        id="navigation-link-path"
                        type={link.type === 'external' ? 'url' : 'text'}
                        value={path}
                        onChange={(e) => setPath(e.target.value)}
                        placeholder={isRTL ? 'المسار' : 'Path'}
                        className="flex-1 px-2 py-1 text-sm border border-gray-300 rounded"
                        required
                    />
                </>
            )}
            {['category', 'product', 'cms'].includes(link.type) && (
                <>
                    <label className="sr-only" htmlFor="navigation-link-target">{isRTL ? 'الهدف' : 'Target'}</label>
                    <select
                        id="navigation-link-target"
                        value={targetId}
                        onChange={(event) => setTargetId(event.target.value)}
                        className="min-w-48 flex-1 px-2 py-1 text-sm border border-gray-300 rounded"
                        required
                    >
                        <option value="">{isRTL ? '-- اختر هدفًا --' : '-- Select a target --'}</option>
                        {targetOptions.map((option) => (
                            <option key={option.targetId} value={option.targetId}>
                                {option.label}{option.state === 'draft' ? ' (Draft)' : ''}
                            </option>
                        ))}
                    </select>
                </>
            )}
            {link.type === 'collection' && (
                <>
                    <label className="sr-only" htmlFor="navigation-link-collection">{isRTL ? 'المجموعة' : 'Collection'}</label>
                    <select
                        id="navigation-link-collection"
                        value={collectionKey}
                        onChange={(event) => setCollectionKey(event.target.value)}
                        className="min-w-48 flex-1 px-2 py-1 text-sm border border-gray-300 rounded"
                        required
                    >
                        <option value="">{isRTL ? '-- اختر مجموعة --' : '-- Select a collection --'}</option>
                        {targets.collections.map((option) => (
                            <option key={option.collectionKey} value={option.collectionKey}>{option.label}</option>
                        ))}
                    </select>
                </>
            )}
            <label className="inline-flex min-h-8 items-center gap-2 text-xs text-gray-700">
                <input
                    type="checkbox"
                    checked={enabled}
                    onChange={(event) => setEnabled(event.target.checked)}
                />
                {isRTL ? 'مفعل' : 'Enabled'}
            </label>
            {link.type === 'external' && (
                <label className="inline-flex min-h-8 items-center gap-2 text-xs text-gray-700">
                    <input
                        type="checkbox"
                        checked={openInNewTab}
                        onChange={(event) => setOpenInNewTab(event.target.checked)}
                    />
                    {isRTL ? 'فتح في نافذة جديدة' : 'Open in new tab'}
                </label>
            )}
            <button
                type="submit"
                aria-label={isRTL ? 'حفظ تعديلات الرابط' : 'Save navigation link edits'}
                className="p-1.5 text-green-600 hover:bg-green-50 rounded"
            >
                <Check size={14} />
            </button>
            <button
                type="button"
                onClick={onCancel}
                aria-label={isRTL ? 'إلغاء تعديل الرابط' : 'Cancel navigation link edits'}
                className="p-1.5 text-gray-400 hover:text-gray-600"
            >
                <X size={14} />
            </button>
        </form>
    )
}

export default NavigationEditor
