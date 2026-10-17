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
    Save,
    Globe,
    Plus,
    Trash2,
    GripVertical,
    ChevronUp,
    ChevronDown,
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
import { HeaderAnnouncementSettings } from '../components/HeaderAnnouncementSettings'
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
    { type: 'custom', labelKey: 'storefront.cms.navigationEditor.linkTypes.custom', icon: Home },
    { type: 'cms', labelKey: 'storefront.cms.navigationEditor.linkTypes.cms', icon: FileText },
    { type: 'category', labelKey: 'storefront.cms.navigationEditor.linkTypes.category', icon: Link2 },
    { type: 'product', labelKey: 'storefront.cms.navigationEditor.linkTypes.product', icon: Package },
    { type: 'collection', labelKey: 'storefront.cms.navigationEditor.linkTypes.collection', icon: Layers3 },
    { type: 'external', labelKey: 'storefront.cms.navigationEditor.linkTypes.external', icon: ExternalLink },
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
    const [, setSaveStatus] = useState('idle')
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
    const [targetsStatus, setTargetsStatus] = useState('loading')
    const [targetsError, setTargetsError] = useState(false)
    const [preferredCmsTargetId, setPreferredCmsTargetId] = useState('')

    // Refs for debounced save
    const saveTimeoutRef = useRef(null)
    const saveAbortControllerRef = useRef(null)
    const savePromiseRef = useRef(null)
    const saveRequestIdRef = useRef(0)
    const navigationLoadAbortRef = useRef(null)
    const targetsLoadAbortRef = useRef(null)
    const acceptedNavigationRef = useRef(null)
    const publishInFlightRef = useRef(false)
    const mountedRef = useRef(true)

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

    const loadTargets = useCallback(async () => {
        targetsLoadAbortRef.current?.abort()
        const controller = new AbortController()
        targetsLoadAbortRef.current = controller
        setTargetsStatus('loading')
        setTargetsError(false)

        try {
            const admittedTargets = await cmsService.getNavigationTargets({ signal: controller.signal })
            if (controller.signal.aborted) return null
            setTargets(admittedTargets)
            setTargetsStatus('ready')
            return admittedTargets
        } catch (err) {
            if (controller.signal.aborted || isRequestCanceled(err)) {
                return null
            }
            console.error('Failed to load navigation targets:', err)
            setTargetsStatus('error')
            setTargetsError(true)
            return null
        } finally {
            if (targetsLoadAbortRef.current === controller) {
                targetsLoadAbortRef.current = null
            }
        }
    }, [])

    useEffect(() => {
        const handleCreatedPage = async (event) => {
            if (event.origin !== window.location.origin || event.data?.type !== 'shopixy:cms-page-created') {
                return
            }

            const pageId = typeof event.data.pageId === 'string' ? event.data.pageId : ''
            const refreshedTargets = await loadTargets()
            if (pageId && refreshedTargets?.cmsPages.some((page) => page.targetId === pageId)) {
                setPreferredCmsTargetId(pageId)
            }
        }

        window.addEventListener('message', handleCreatedPage)
        return () => window.removeEventListener('message', handleCreatedPage)
    }, [loadTargets])

    // Load navigation and selectable targets independently so one failure cannot hide the other.
    useEffect(() => {
        mountedRef.current = true
        loadNavigation()
        void loadTargets()
        return () => {
            mountedRef.current = false
            reset()
            clearScheduledSave(true)
            navigationLoadAbortRef.current?.abort()
            targetsLoadAbortRef.current?.abort()
        }
    }, [clearScheduledSave, loadTargets, reset])

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
            if (controller.signal.aborted) return
            setNavigation(data)
            acceptedNavigationRef.current = data
        } catch (err) {
            if (controller.signal.aborted || isRequestCanceled(err)) {
                return
            }
            console.error('Failed to load navigation:', err)
            const admission = extractNavigationAdmissionError(err)
            setError(admission?.message || t('storefront.cms.navigationEditor.loadFailed'))
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
            if (!mountedRef.current) return false
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
                if (controller.signal.aborted || isRequestCanceled(err)) {
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
                        || t('storefront.cms.navigationEditor.saveFailedMessage')
                    setSaveStatus('error')
                    setSaveErrorBanner(message)
                }

                if (err?.code === 'NAVIGATION_CLIENT_INVALID' && err?.details) {
                    const menu = err.details.menu === 'footer' ? 'footer' : 'header'
                    setActiveTab(menu)
                    setEditingLink(Number.isInteger(err.details.index) ? err.details.index : null)
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
    }, [clearScheduledSave, setNavigation, setNavigationDirty, t])

    // Debounced auto-save
    const debouncedSave = useCallback(() => {
        if (publishInFlightRef.current) return
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
        if (publishInFlightRef.current || !navigation?.draft || (!isNavigationDirty && !navigation.hasPendingChanges)) return
        publishInFlightRef.current = true
        setIsPublishing(true)

        const savedNavigation = await handleSave(true)
        if (!savedNavigation || !mountedRef.current) {
            publishInFlightRef.current = false
            setIsPublishing(false)
            return
        }

        setIsPublishing(true)
        setPublishStatus('publishing')
        setPublishErrorBanner(null)
        setConflict(null)
        try {
            const publishedNavigation = await cmsService.publishNavigation(savedNavigation.rowVersion)
            if (!mountedRef.current) return
            const currentNavigation = useCmsStore.getState().navigation
            const hasNewerEdits = getNavigationDraftFingerprint(currentNavigation) !== getNavigationDraftFingerprint(savedNavigation)
            setNavigation(hasNewerEdits ? { ...publishedNavigation, draft: currentNavigation.draft, hasPendingChanges: true } : publishedNavigation)
            acceptedNavigationRef.current = publishedNavigation
            setNavigationDirty(hasNewerEdits)
            setLastSavedAt(new Date().toISOString())
            setSaveStatus(hasNewerEdits ? 'unsaved' : 'saved')
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
                    || t('storefront.cms.navigationEditor.publishFailedMessage')
                setPublishStatus('error')
                setPublishErrorBanner(message)
            }
        } finally {
            publishInFlightRef.current = false
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
        if (navigation.live) {
            const candidate = { ...navigation, draft: navigation.live }
            setNavigation(candidate)
            setNavigationDirty(true)
            void handleSave(false, candidate)
            return
        }
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
        return <AuthoringLoadingState label={t('storefront.cms.navigationEditor.loading')} />
    }

    if (!navigation) {
        return <AuthoringFeedback kind="error" title={t('storefront.cms.navigationEditor.unavailable')} message={error || t('storefront.cms.navigationEditor.unavailableMessage')} action={{ label: t('storefront.cms.navigationEditor.retryLoad'), onClick: () => void loadNavigation(), icon: RefreshCcw }} />
    }

    const draftValidationWarnings = (navigation.validationWarnings || [])
        .filter((warning) => warning.snapshot === 'draft')

    return (
        <div className="space-y-6">
            <AuthoringActionBar
                title={t('storefront.cms.navigationEditor.title')}
                subtitle={lastSavedLabel && !isNavigationDirty ? t('storefront.cms.navigationEditor.lastSaved', { time: lastSavedLabel }) : t('storefront.cms.navigationEditor.subtitle')}
                backAction={{ label: t('storefront.cms.navigationEditor.back'), onClick: handleExit }}
                status={<AuthoringStatusBadge state={isPublishing ? 'publishing' : isSaving ? 'saving' : isNavigationDirty ? 'dirty' : publishStatus === 'published' ? 'published' : 'draft'} />}
                historyAction={{ label: t('storefront.cms.navigationEditor.history'), onClick: () => requestNavigation('/storefront/storeconfig-version-history') }}
                discardAction={{ label: t('storefront.cms.navigationEditor.discard'), onClick: handleDiscard, disabled: (!isNavigationDirty && !navigation.hasPendingChanges) || isSaving || isPublishing }}
                saveAction={{ label: t('storefront.cms.navigationEditor.saveDraft'), busyLabel: t('storefront.cms.navigationEditor.saving'), onClick: () => void handleSave(false), disabled: !isNavigationDirty || isPublishing, busy: isSaving }}
                publishAction={{ label: t('storefront.cms.navigationEditor.publish'), busyLabel: t('storefront.cms.navigationEditor.publishing'), onClick: () => setIsPublishConfirmOpen(true), disabled: isSaving || (!isNavigationDirty && !navigation.hasPendingChanges), busy: isPublishing }}
            />

            {publishStatus === 'published' ? <AuthoringFeedback kind="success" title={t('storefront.cms.navigationEditor.publishedTitle')} message={t('storefront.cms.navigationEditor.publishedMessage')} /> : null}

            {/* Error */}
            {error ? <AuthoringFeedback kind="error" title={t('storefront.cms.navigationEditor.actionFailed')} message={error} /> : null}

            {saveErrorBanner ? <AuthoringFeedback kind="error" title={t('storefront.cms.navigationEditor.notSaved')} message={saveErrorBanner} action={{ label: t('storefront.cms.navigationEditor.retrySave'), onClick: () => void handleSave(false), icon: Save }} /> : null}

            {publishErrorBanner ? <AuthoringFeedback kind="error" title={t('storefront.cms.navigationEditor.publishFailed')} message={publishErrorBanner} action={{ label: t('storefront.cms.navigationEditor.retryPublish'), onClick: () => void handlePublish(), icon: Globe }} /> : null}

            {draftValidationWarnings.length > 0 ? <AuthoringFeedback kind="warning" title={t('storefront.cms.navigationEditor.invalidCount', { count: draftValidationWarnings.length })} message={t('storefront.cms.navigationEditor.invalidHelp')} /> : null}

            {/* Tabs */}
            <div className="border-b border-gray-200">
                <nav aria-label={t('storefront.cms.navigationEditor.views')} className="flex gap-4" role="tablist">
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
                        {t('storefront.cms.navigationEditor.header')}
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
                        {t('storefront.cms.navigationEditor.footer')}
                        <span className="ms-2 text-xs bg-gray-100 text-gray-600 px-2 py-0.5 rounded-full">
                            {navigation?.draft?.footer?.length || 0}
                        </span>
                    </button>
                </nav>
            </div>

            {/* Links Content */}
            {activeTab === 'header' && <HeaderAnnouncementSettings value={navigation.draft.announcement} onChange={(announcement) => {
                setNavigation({ ...navigation, draft: { ...navigation.draft, announcement }, hasPendingChanges: true })
                setNavigationDirty(true)
            }} />}
            <div className="bg-white rounded-lg shadow-sm border border-gray-200 p-6">
                <div className="flex items-center justify-between mb-6">
                    <h2 className="text-lg font-semibold text-gray-900">
                        {activeTab === 'header'
                            ? t('storefront.cms.navigationEditor.headerLinks')
                            : t('storefront.cms.navigationEditor.footerLinks')}
                    </h2>
                    <button
                        onClick={() => setShowAddModal(true)}
                        className="inline-flex items-center gap-2 px-3 py-1.5 text-sm font-medium text-indigo-600 bg-indigo-50 rounded-lg hover:bg-indigo-100 transition-colors"
                    >
                        <Plus size={16} />
                        {t('storefront.cms.navigationEditor.addLink')}
                    </button>
                </div>

                {/* Links List */}
                {getCurrentLinks().length === 0 ? (
                    <AuthoringEmptyState
                        title={t('storefront.cms.navigationEditor.emptyTitle')}
                        description={t('storefront.cms.navigationEditor.emptyDescription')}
                        action={{ label: t('storefront.cms.navigationEditor.addLink'), onClick: () => setShowAddModal(true), icon: Plus }}
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
                                            t={t}
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
                                                        {t(`storefront.cms.navigationEditor.validation.${link.validationCode}`, { defaultValue: link.validationMessage || t('storefront.cms.navigationEditor.validation.invalid') })}
                                                    </p>
                                                )}
                                                {!link.enabled && (
                                                    <p className="mt-1 text-xs font-medium text-gray-500">
                                                        {t('storefront.cms.navigationEditor.disabled')}
                                                    </p>
                                                )}
                                            </div>
                                            <div className="flex shrink-0 items-center gap-1">
                                                {isInvalid ? (
                                                    <button
                                                        type="button"
                                                        onClick={() => setEditingLink(index)}
                                                        className="rounded-lg border border-amber-300 bg-white px-3 py-1.5 text-xs font-semibold text-amber-800 transition hover:bg-amber-100"
                                                    >
                                                        {t('storefront.cms.navigationEditor.repair')}
                                                    </button>
                                                ) : (
                                                    <button
                                                        onClick={() => setEditingLink(index)}
                                                        aria-label={t('storefront.cms.navigationEditor.editLink')}
                                                        className="p-1.5 text-gray-400 hover:text-indigo-600 hover:bg-white rounded transition-colors"
                                                    >
                                                        <Edit2 size={14} />
                                                    </button>
                                                )}
                                                <button
                                                    onClick={() => handleMoveLink(index, 'up')}
                                                    aria-label={t('storefront.cms.navigationEditor.moveUp')}
                                                    disabled={index === 0}
                                                    className="p-1.5 text-gray-400 hover:text-gray-600 disabled:opacity-30 transition-colors"
                                                >
                                                    <ChevronUp size={14} />
                                                </button>
                                                <button
                                                    onClick={() => handleMoveLink(index, 'down')}
                                                    aria-label={t('storefront.cms.navigationEditor.moveDown')}
                                                    disabled={index === getCurrentLinks().length - 1}
                                                    className="p-1.5 text-gray-400 hover:text-gray-600 disabled:opacity-30 transition-colors"
                                                >
                                                    <ChevronDown size={14} />
                                                </button>
                                                <button
                                                    onClick={() => setDeleteLinkIndex(index)}
                                                    aria-label={t('storefront.cms.navigationEditor.deleteLink')}
                                                    className={isInvalid
                                                        ? 'rounded-lg border border-rose-200 bg-white px-3 py-1.5 text-xs font-semibold text-rose-700 transition hover:bg-rose-50'
                                                        : 'p-1.5 text-gray-400 hover:text-red-600 hover:bg-red-50 rounded transition-colors'}
                                                >
                                                    {isInvalid ? t('storefront.cms.navigationEditor.remove') : <Trash2 size={14} />}
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
                    t={t}
                    targets={targets}
                    targetsStatus={targetsStatus}
                    targetsError={targetsError ? t('storefront.cms.navigationEditor.targets.loadFailed') : ''}
                    onRetryTargets={() => void loadTargets()}
                    preferredCmsTargetId={preferredCmsTargetId}
                    onCreatePage={() => {
                        const pageWindow = window.open('/storefront/content/pages/new?returnTo=navigation', '_blank')
                        pageWindow?.focus()
                    }}
                />
            )}
            <AuthoringConfirmDialog
                open={isPublishConfirmOpen}
                title={t('storefront.cms.navigationEditor.publishConfirmTitle')}
                description={t('storefront.cms.navigationEditor.publishConfirmDescription')}
                confirmLabel={t('storefront.cms.navigationEditor.publish')}
                confirmTone="publish"
                busy={isPublishing}
                onClose={() => setIsPublishConfirmOpen(false)}
                onConfirm={handlePublish}
            />
            <AuthoringConfirmDialog
                open={deleteLinkIndex != null}
                title={t('storefront.cms.navigationEditor.deleteConfirmTitle')}
                description={t('storefront.cms.navigationEditor.deleteConfirmDescription')}
                confirmLabel={t('storefront.cms.navigationEditor.deleteLink')}
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
function AddLinkModal({ onAdd, onClose, t, targets, targetsStatus, targetsError, onRetryTargets, onCreatePage, preferredCmsTargetId }) {
    const [linkType, setLinkType] = useState('cms')
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

    useEffect(() => {
        if (linkType !== 'cms' || !preferredCmsTargetId) return
        const selected = targets.cmsPages.find((option) => option.targetId === preferredCmsTargetId)
        if (!selected) return
        setTargetId(selected.targetId)
        setLabel(selected.label)
    }, [linkType, preferredCmsTargetId, targets.cmsPages])

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
            title={t('storefront.cms.navigationEditor.addLink')}
            onClose={onClose}
            panelClassName="!max-w-md"
        >
                <form onSubmit={handleSubmit} className="mt-4 space-y-4">
                    <div>
                        <label htmlFor="navigation-link-type" className="block text-sm font-medium text-gray-700 mb-1">
                            {t('storefront.cms.navigationEditor.linkType')}
                        </label>
                        <select
                            id="navigation-link-type"
                            value={linkType}
                            onChange={(e) => handleTypeChange(e.target.value)}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500"
                        >
                            {LINK_TYPES.map((lt) => (
                                <option key={lt.type} value={lt.type}>
                                    {t(lt.labelKey)}
                                </option>
                            ))}
                        </select>
                    </div>

                    {['category', 'product', 'cms'].includes(linkType) && (
                        <div>
                            <label htmlFor="navigation-link-target" className="block text-sm font-medium text-gray-700 mb-1">
                                {linkType === 'category'
                                    ? t('storefront.cms.navigationEditor.selectCategory')
                                    : linkType === 'product'
                                        ? t('storefront.cms.navigationEditor.selectProduct')
                                        : t('storefront.cms.navigationEditor.selectPage')} *
                            </label>
                            {targetsStatus === 'loading' ? (
                                <p className="rounded-lg border border-slate-200 bg-slate-50 px-3 py-3 text-sm text-slate-600" role="status">
                                    {t('storefront.cms.navigationEditor.targets.loading')}
                                </p>
                            ) : targetsStatus === 'error' ? (
                                <div className="rounded-lg border border-rose-200 bg-rose-50 p-3 text-sm text-rose-800" role="alert">
                                    <p>{targetsError}</p>
                                    <button type="button" onClick={onRetryTargets} className="mt-2 font-semibold underline">
                                        {t('storefront.cms.navigationEditor.retry')}
                                    </button>
                                </div>
                            ) : linkType === 'cms' && targetOptions.length === 0 ? (
                                <div className="rounded-lg border border-dashed border-slate-300 bg-slate-50 p-4 text-sm text-slate-700" role="status">
                                    <p className="font-medium">{t('storefront.cms.navigationEditor.targets.noPages')}</p>
                                    <p className="mt-1 text-slate-500">{t('storefront.cms.navigationEditor.targets.noPagesHelp')}</p>
                                    <div className="mt-3 flex flex-wrap gap-2">
                                        <button type="button" onClick={onCreatePage} className="rounded-lg bg-slate-900 px-3 py-2 font-medium text-white">
                                            {t('storefront.cms.navigationEditor.targets.createPage')}
                                        </button>
                                        <button type="button" onClick={onRetryTargets} className="rounded-lg border border-slate-300 bg-white px-3 py-2 font-medium text-slate-700">
                                            {t('storefront.cms.navigationEditor.targets.refreshPages')}
                                        </button>
                                    </div>
                                </div>
                            ) : (
                                <select
                                    id="navigation-link-target"
                                    value={targetId}
                                    onChange={(e) => handleTargetSelect(e.target.value)}
                                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500"
                                    required
                                >
                                    <option value="">
                                        {linkType === 'category'
                                            ? t('storefront.cms.navigationEditor.targetPlaceholders.category')
                                            : linkType === 'product'
                                                ? t('storefront.cms.navigationEditor.targetPlaceholders.product')
                                                : t('storefront.cms.navigationEditor.targetPlaceholders.cms')}
                                    </option>
                                    {targetOptions.map((option) => (
                                        <option key={option.targetId} value={option.targetId}>
                                            {option.label}{option.state === 'draft' ? ` (${t('storefront.cms.draft')})` : ''}
                                        </option>
                                    ))}
                                </select>
                            )}
                        </div>
                    )}

                    {linkType === 'collection' && (
                        <div>
                            <label htmlFor="navigation-link-collection" className="block text-sm font-medium text-gray-700 mb-1">
                                {t('storefront.cms.navigationEditor.selectCollection')} *
                            </label>
                            <select
                                id="navigation-link-collection"
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
                                <option value="">{t('storefront.cms.navigationEditor.targetPlaceholders.collection')}</option>
                                {targets.collections.map((option) => (
                                    <option key={option.collectionKey} value={option.collectionKey}>
                                        {option.label}
                                    </option>
                                ))}
                            </select>
                        </div>
                    )}

                    <div>
                        <label htmlFor="navigation-link-label-ar" className="block text-sm font-medium text-gray-700 mb-1">
                            {t('storefront.cms.navigationEditor.arabicLabel')}
                        </label>
                        <input
                            id="navigation-link-label-ar"
                            type="text"
                            dir="rtl"
                            value={labelAr}
                            onChange={(e) => setLabelAr(e.target.value)}
                            placeholder={t('storefront.cms.navigationEditor.arabicLabelPlaceholder')}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500"
                        />
                    </div>

                    <div>
                        <label htmlFor="navigation-link-label" className="block text-sm font-medium text-gray-700 mb-1">
                            {t('storefront.cms.navigationEditor.englishLabel')} *
                        </label>
                        <input
                            id="navigation-link-label"
                            type="text"
                            value={label}
                            onChange={(e) => setLabel(e.target.value)}
                            placeholder={t('storefront.cms.navigationEditor.englishLabelPlaceholder')}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500"
                            required
                        />
                    </div>

                    {(linkType === 'custom' || linkType === 'external') && (
                        <div>
                            <label htmlFor="navigation-link-path" className="block text-sm font-medium text-gray-700 mb-1">
                                {linkType === 'external'
                                    ? t('storefront.cms.navigationEditor.externalUrl')
                                    : t('storefront.cms.navigationEditor.path')} *
                            </label>
                            <input
                                id="navigation-link-path"
                                type={linkType === 'external' ? 'url' : 'text'}
                                value={path}
                                onChange={(e) => setPath(e.target.value)}
                                placeholder={t(linkType === 'external'
                                    ? 'storefront.cms.navigationEditor.externalUrlPlaceholder'
                                    : 'storefront.cms.navigationEditor.pathPlaceholder')}
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
                                {t('storefront.cms.openNewTab')}
                            </span>
                        </label>
                    )}

                    <div className="flex items-center justify-end gap-3 pt-4">
                        <button
                            type="button"
                            onClick={onClose}
                            className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50"
                        >
                            {t('storefront.cms.navigationEditor.cancel')}
                        </button>
                        <button
                            type="submit"
                            disabled={
                                (['category', 'product', 'cms'].includes(linkType) && (targetsStatus !== 'ready' || !targetId))
                                || (linkType === 'collection' && !collectionKey)
                            }
                            className="px-4 py-2 text-sm font-medium text-white bg-indigo-600 rounded-lg hover:bg-indigo-700 disabled:cursor-not-allowed disabled:opacity-50"
                        >
                            {t('storefront.cms.navigationEditor.add')}
                        </button>
                    </div>
                </form>
        </AuthoringDialog>
    )
}

/**
 * Link Edit Form (inline)
 */
function LinkEditForm({ link, onSave, onCancel, t, targets }) {
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
            <label className="sr-only" htmlFor="navigation-link-label">{t('storefront.cms.navigationEditor.englishLabel')}</label>
            <input
                id="navigation-link-label"
                type="text"
                value={label}
                onChange={(e) => setLabel(e.target.value)}
                placeholder={t('storefront.cms.navigationEditor.englishLabelShort')}
                className="flex-1 px-2 py-1 text-sm border border-gray-300 rounded"
                required
            />
            <label className="sr-only" htmlFor="navigation-link-label-ar">{t('storefront.cms.navigationEditor.arabicLabelShort')}</label>
            <input
                id="navigation-link-label-ar"
                type="text"
                dir="rtl"
                value={labelAr}
                onChange={(e) => setLabelAr(e.target.value)}
                placeholder={t('storefront.cms.navigationEditor.arabicLabelShort')}
                className="flex-1 px-2 py-1 text-sm border border-gray-300 rounded"
            />
            {(link.type === 'custom' || link.type === 'external') && (
                <>
                    <label className="sr-only" htmlFor="navigation-link-path">{t('storefront.cms.navigationEditor.path')}</label>
                    <input
                        id="navigation-link-path"
                        type={link.type === 'external' ? 'url' : 'text'}
                        value={path}
                        onChange={(e) => setPath(e.target.value)}
                        placeholder={t('storefront.cms.navigationEditor.path')}
                        className="flex-1 px-2 py-1 text-sm border border-gray-300 rounded"
                        required
                    />
                </>
            )}
            {['category', 'product', 'cms'].includes(link.type) && (
                <>
                    <label className="sr-only" htmlFor="navigation-link-target">{t('storefront.cms.navigationEditor.target')}</label>
                    <select
                        id="navigation-link-target"
                        value={targetId}
                        onChange={(event) => setTargetId(event.target.value)}
                        className="min-w-48 flex-1 px-2 py-1 text-sm border border-gray-300 rounded"
                        required
                    >
                        <option value="">{t('storefront.cms.navigationEditor.targetPlaceholders.target')}</option>
                        {targetOptions.map((option) => (
                            <option key={option.targetId} value={option.targetId}>
                                {option.label}{option.state === 'draft' ? ` (${t('storefront.cms.draft')})` : ''}
                            </option>
                        ))}
                    </select>
                </>
            )}
            {link.type === 'collection' && (
                <>
                    <label className="sr-only" htmlFor="navigation-link-collection">{t('storefront.cms.navigationEditor.linkTypes.collection')}</label>
                    <select
                        id="navigation-link-collection"
                        value={collectionKey}
                        onChange={(event) => setCollectionKey(event.target.value)}
                        className="min-w-48 flex-1 px-2 py-1 text-sm border border-gray-300 rounded"
                        required
                    >
                        <option value="">{t('storefront.cms.navigationEditor.targetPlaceholders.collection')}</option>
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
                {t('storefront.cms.navigationEditor.enabled')}
            </label>
            {link.type === 'external' && (
                <label className="inline-flex min-h-8 items-center gap-2 text-xs text-gray-700">
                    <input
                        type="checkbox"
                        checked={openInNewTab}
                        onChange={(event) => setOpenInNewTab(event.target.checked)}
                    />
                    {t('storefront.cms.openNewTab')}
                </label>
            )}
            <button
                type="submit"
                aria-label={t('storefront.cms.navigationEditor.saveLinkEdits')}
                className="p-1.5 text-green-600 hover:bg-green-50 rounded"
            >
                <Check size={14} />
            </button>
            <button
                type="button"
                onClick={onCancel}
                aria-label={t('storefront.cms.navigationEditor.cancelLinkEdits')}
                className="p-1.5 text-gray-400 hover:text-gray-600"
            >
                <X size={14} />
            </button>
        </form>
    )
}

export default NavigationEditor
