/**
 * Storefront Builder Page
 * Phase 9.8: POS Integration - Unified Admin Experience
 * 
 * This is the main Visual Builder page integrated into the POS.
 * 
 * CRITICAL: All edits modify DRAFT ONLY until explicit Publish
 */

import { useState, useEffect, useCallback, useRef } from 'react'
import { useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import {
    DndContext,
    closestCenter,
    PointerSensor,
    KeyboardSensor,
    useSensor,
    useSensors,
} from '@dnd-kit/core'
import { sortableKeyboardCoordinates } from '@dnd-kit/sortable'
import { ArrowLeft } from 'lucide-react'
import {
    BuilderTopBar,
    SectionPalette,
    BuilderCanvas,
    SettingsPanel,
    PreviewModal,
} from '../components/builder'
import { useBuilderStore, selectSections, selectCurrentPage } from '../store'
import { storeConfigService } from '../services'
import { isRequestCanceled } from '../../../services/api'
import { sectionRegistry } from '../builder'
import { filterSectionsForPage, isSectionAvailableForPage } from '../builder/pageSectionAvailability'
import { validateSectionsAgainstContract } from '../builder/sectionContractRegistry'
import { useAuthStore } from '../../../store/authStore'
import {
    deriveCurrentStoreIdentityPresentation,
    deriveCurrentVendorScopedDto,
} from '../../../auth/commerceHubVendorIdentityAuthority'
import { getDefaultSections } from '../config'
import {
    composeProductTemplateSections,
    getBuiltinProductTemplate,
    getProductTemplateOptions,
    getSupplementalProductTemplateSections,
} from '@/storefront/products/ProductTemplateRegistry'
import { StorefrontBreadcrumbs } from '../informationArchitecture'
import {
    AuthoringConfirmDialog,
    AuthoringConflictDialog,
    AuthoringFeedback,
    AuthoringPromptDialog,
    UnsavedChangesDialog,
    useAuthoringGuard,
    useAuthoringShortcuts,
} from '../authoringPlatform'

export function StorefrontBuilderPage() {
    const { t, i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const navigate = useNavigate()
    const location = useLocation()
    const [searchParams] = useSearchParams()
    const currentUser = useAuthStore((state) => state.user)
    const tenantId = currentUser?.tenantId || ''

    // Store
    const sections = useBuilderStore(selectSections)
    const currentPage = useBuilderStore(selectCurrentPage)
    const setCurrentPage = useBuilderStore((s) => s.setCurrentPage)
    const addSection = useBuilderStore((s) => s.addSection)
    const reorderSections = useBuilderStore((s) => s.reorderSections)
    const admitSections = useBuilderStore((s) => s.admitSections)
    const setSaving = useBuilderStore((s) => s.setSaving)
    const setPublishing = useBuilderStore((s) => s.setPublishing)
    const markClean = useBuilderStore((s) => s.markClean)
    const isDirty = useBuilderStore((s) => s.isDirty)
    const isSaving = useBuilderStore((s) => s.isSaving)
    const isPublishing = useBuilderStore((s) => s.isPublishing)
    const reset = useBuilderStore((s) => s.reset)

    // Local state
    const [activeId, setActiveId] = useState(null)
    const [isPublishConfirmOpen, setIsPublishConfirmOpen] = useState(false)
    const [isDeleteConfirmOpen, setIsDeleteConfirmOpen] = useState(false)
    const [isCreateTemplateOpen, setIsCreateTemplateOpen] = useState(false)
    const [newTemplateName, setNewTemplateName] = useState('')
    const [isPreviewModalOpen, setIsPreviewModalOpen] = useState(false)
    const [conflict, setConflict] = useState(null)
    const [feedback, setFeedback] = useState(null)
    const [storeConfig, setStoreConfig] = useState(null)
    const [saveStatus, setSaveStatus] = useState('idle')
    const [saveError, setSaveError] = useState(null)
    const [loadError, setLoadError] = useState(null)
    const [publishError, setPublishError] = useState(null)
    const [previewConfig, setPreviewConfig] = useState(null)
    const [previewError, setPreviewError] = useState(null)
    const [isLoadingPreview, setIsLoadingPreview] = useState(false)
    const [isLoadingWorkspace, setIsLoadingWorkspace] = useState(true)
    const [lastSavedAt, setLastSavedAt] = useState(null)

    // Refs
    const saveAbortControllerRef = useRef(null)
    const loadAbortControllerRef = useRef(null)
    const previewAbortControllerRef = useRef(null)
    const saveRequestIdRef = useRef(0)
    const loadRequestIdRef = useRef(0)

    // DnD Sensors
    const sensors = useSensors(
        useSensor(PointerSensor, { activationConstraint: { distance: 8 } }),
        useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates })
    )

    // Get page from URL - this is the source of truth
    const pageFromUrl = searchParams.get('page')
        || (location.pathname.endsWith('/catalog/product-template') ? 'product-template' : 'home')
    const templateIdFromUrl = searchParams.get('templateId') || 'default'
    const validPages = ['home', 'product-template']
    const targetPage = validPages.includes(pageFromUrl) ? pageFromUrl : 'home'
    const activeTemplateOptions = getProductTemplateOptions(storeConfig?.productTemplates)
    const isProductTemplatePage = targetPage === 'product-template'
    const activeTemplateName = activeTemplateOptions.find((option) => option.id === templateIdFromUrl)?.name
        || templateIdFromUrl

    const getActiveStorageKey = useCallback(
        () => (isProductTemplatePage ? `product-template:${templateIdFromUrl}` : targetPage),
        [isProductTemplatePage, targetPage, templateIdFromUrl]
    )

    const abortPendingSave = useCallback(() => {
        if (saveAbortControllerRef.current) {
            saveAbortControllerRef.current.abort()
            saveAbortControllerRef.current = null
            setSaving(false)
        }
    }, [setSaving])

    const getAcceptedWorkspaceRowVersion = useCallback((config) => {
        const rowVersion = typeof config?.rowVersion === 'string' ? config.rowVersion.trim() : ''
        return rowVersion || null
    }, [])

    const buildIfMatchOptions = useCallback((rowVersion, options = {}) => ({
        ...options,
        headers: {
            ...(options.headers || {}),
            'If-Match': `"${rowVersion}"`,
        },
    }), [])

    const admitWorkspaceConfig = useCallback((response, runtimeFallback = null) => {
        const acceptedAdminConfig = deriveCurrentStoreIdentityPresentation(response, currentUser)
        if (!acceptedAdminConfig || !getAcceptedWorkspaceRowVersion(acceptedAdminConfig)) {
            return null
        }

        return {
            ...(runtimeFallback || {}),
            ...(acceptedAdminConfig || {}),
            storefrontTheme: acceptedAdminConfig?.storefrontTheme || runtimeFallback?.storefrontTheme,
            currency: runtimeFallback?.currency || acceptedAdminConfig?.currency || '',
            pages: acceptedAdminConfig?.pages,
            productTemplates: acceptedAdminConfig?.productTemplates || {},
        }
    }, [currentUser, getAcceptedWorkspaceRowVersion])

    const requireWorkspaceAdmission = useCallback(() => {
        const rowVersion = getAcceptedWorkspaceRowVersion(storeConfig)
        if (rowVersion) {
            return rowVersion
        }

        const message = isRTL
            ? 'يجب تحميل مساحة العمل من الخادم قبل تنفيذ هذا الإجراء.'
            : 'The workspace must be admitted by the backend before this action can run.'
        setSaveStatus('error')
        setSaveError(message)
        return null
    }, [getAcceptedWorkspaceRowVersion, isRTL, storeConfig])

    const getAcceptedSectionsFromConfig = useCallback((config, page, templateId) => {
        if (page === 'product-template') {
            const storedTemplate = config?.productTemplates?.[templateId]
            const builtInTemplate = getBuiltinProductTemplate(templateId)
            const resolvedTemplate = storedTemplate || builtInTemplate || {
                templateId,
                name: templateId,
                sections: [],
            }

            return getSupplementalProductTemplateSections(resolvedTemplate.sections)
        }

        const pageSections = config?.pages?.[page]?.sections
        return Array.isArray(pageSections) ? pageSections : []
    }, [])

    // Sync store with URL param - whenever URL changes, update store
    useEffect(() => {
        console.log('[Builder] URL page param:', targetPage, '| Store currentPage:', currentPage)
        if (targetPage !== currentPage) {
            console.log('[Builder] Syncing store to:', targetPage)
            setCurrentPage(targetPage)
        }
    }, [targetPage, currentPage, setCurrentPage])

    useEffect(() => {
        if (isDirty) {
            setSaveStatus('unsaved')
            setSaveError(null)
        }
    }, [isDirty])

    const loadPageData = useCallback(async (page, templateId) => {
        loadAbortControllerRef.current?.abort()
        const controller = new AbortController()
        loadAbortControllerRef.current = controller
        const requestId = loadRequestIdRef.current + 1
        loadRequestIdRef.current = requestId
        setLoadError(null)
        setIsLoadingWorkspace(true)
        setStoreConfig(null)
        admitSections([])

        try {
            const [adminConfig, runtimeConfig] = await Promise.all([
                storeConfigService.getStoreConfig({ signal: controller.signal }),
                storeConfigService.getRuntimeStoreConfig({ signal: controller.signal }).catch((error) => {
                    if (isRequestCanceled(error)) {
                        throw error
                    }

                    console.warn('[Builder] Failed to load runtime storefront config:', error)
                    return null
                }),
            ])

            if (controller.signal.aborted || requestId !== loadRequestIdRef.current) {
                return
            }

            const acceptedRuntimeConfig = runtimeConfig
                ? deriveCurrentVendorScopedDto(runtimeConfig, currentUser)
                : null

            const config = admitWorkspaceConfig(adminConfig, acceptedRuntimeConfig)

            if (!config) {
                throw new Error('Current backend store identity authority is unavailable.')
            }

            console.log('[Builder] Config loaded:', {
                storefrontTheme: config?.storefrontTheme,
                pages: Object.keys(config?.pages || {}),
                productTemplates: Object.keys(config?.productTemplates || {}),
            })
            setStoreConfig(config)
            setLoadError(null)
            setSaveError(null)
            setSaveStatus('idle')

            const acceptedSections = filterSectionsForPage(
                getAcceptedSectionsFromConfig(config, page, templateId),
                page,
            )
            console.log('[Builder] Setting backend-admitted sections for', page, ':', acceptedSections.length, 'sections')
            admitSections(acceptedSections)
        } catch (error) {
            if (isRequestCanceled(error)) {
                return
            }

            console.error('Failed to load page data:', error)
            setStoreConfig(null)
            setSaveStatus('error')
            setLoadError(
                error?.response?.data?.message
                || (isRTL ? 'فشل تحميل بيانات الصفحة.' : 'Failed to load page data.')
            )
            admitSections([])
        } finally {
            if (loadAbortControllerRef.current === controller) {
                loadAbortControllerRef.current = null
            }

            if (!controller.signal.aborted && requestId === loadRequestIdRef.current) {
                setIsLoadingWorkspace(false)
            }
        }
    }, [admitSections, admitWorkspaceConfig, currentUser, getAcceptedSectionsFromConfig, isRTL])

    // Load page data when target page or product template changes
    useEffect(() => {
        console.log('[Builder] Loading data for page:', targetPage, '| template:', templateIdFromUrl)
        void loadPageData(targetPage, templateIdFromUrl)
    }, [loadPageData, targetPage, templateIdFromUrl])

    const persistPageLayout = useCallback(async (sectionsToSave = sections, rowVersionOverride = null) => {
        if (saveAbortControllerRef.current) {
            return { saved: false, error: 'A draft save is already in progress.' }
        }

        const rowVersion = rowVersionOverride || requireWorkspaceAdmission()
        if (!rowVersion) {
            return { saved: false, error: saveError }
        }

        const controller = new AbortController()
        saveAbortControllerRef.current = controller
        const requestId = saveRequestIdRef.current + 1
        saveRequestIdRef.current = requestId

        setSaving(true)
        setSaveStatus('saving')
        setSaveError(null)

        try {
            const supplementalSectionsToSave = filterSectionsForPage(sectionsToSave, targetPage)
            const pageSectionsToSave = isProductTemplatePage
                ? composeProductTemplateSections(
                    supplementalSectionsToSave,
                    templateIdFromUrl,
                    storeConfig?.productTemplates?.[templateIdFromUrl]?.sections,
                )
                : supplementalSectionsToSave
            validateSectionsAgainstContract(pageSectionsToSave)

            const storageKey = getActiveStorageKey()
            const payload = isProductTemplatePage
                ? {
                    templateId: templateIdFromUrl,
                    name: storeConfig?.productTemplates?.[templateIdFromUrl]?.name
                        || getBuiltinProductTemplate(templateIdFromUrl)?.name
                        || templateIdFromUrl,
                    sections: pageSectionsToSave,
                }
                : { sections: pageSectionsToSave }

            const response = await storeConfigService.updatePageLayout(
                storageKey,
                payload,
                buildIfMatchOptions(rowVersion, { signal: controller.signal }),
            )

            if (controller.signal.aborted || requestId !== saveRequestIdRef.current) {
                return { saved: false }
            }

            const acceptedConfig = admitWorkspaceConfig(response)
            if (!acceptedConfig) {
                throw new Error('Current backend workspace response authority is unavailable.')
            }

            setStoreConfig(acceptedConfig)
            // The editor remains interactive during the request. Never replace newer edits
            // with the accepted snapshot of an earlier Save Draft operation.
            if (useBuilderStore.getState().sections !== sectionsToSave) {
                setSaveStatus('unsaved')
                return { saved: false, error: 'The draft changed while saving. Save the latest changes before continuing.' }
            }
            admitSections(
                filterSectionsForPage(
                    getAcceptedSectionsFromConfig(acceptedConfig, targetPage, templateIdFromUrl),
                    targetPage,
                ),
                { preserveSelection: true, resetHistory: false },
            )
            markClean()
            setLastSavedAt(new Date().toISOString())
            setSaveStatus('saved')
            setConflict(null)
            setFeedback({ kind: 'success', title: t('storefront.homepageEditor.draftSaved'), message: t('storefront.homepageEditor.draftUpToDate') })
            return { saved: true, config: acceptedConfig }
        } catch (error) {
            if (isRequestCanceled(error)) {
                return { saved: false }
            }

            console.error('Save failed:', error)
            const errorMessage = error?.response?.data?.message
                || (isRTL ? 'فشل حفظ تغييرات الصفحة. حاول مرة أخرى.' : 'Failed to save page changes. Please try again.')
            setSaveStatus('error')
            setSaveError(errorMessage)
            if (error?.response?.status === 409) {
                setConflict({ message: errorMessage })
            }
            return { saved: false, error: errorMessage }
        } finally {
            if (saveAbortControllerRef.current === controller) {
                saveAbortControllerRef.current = null
            }

            if (!controller.signal.aborted && requestId === saveRequestIdRef.current) {
                setSaving(false)
            }
        }
    }, [admitSections, admitWorkspaceConfig, buildIfMatchOptions, getAcceptedSectionsFromConfig, getActiveStorageKey, isProductTemplatePage, isRTL, markClean, requireWorkspaceAdmission, saveError, sections, setSaving, storeConfig, targetPage, templateIdFromUrl])

    const handleSave = useCallback(async () => {
        await persistPageLayout(sections)
    }, [persistPageLayout, sections])

    const handleRetrySave = useCallback(() => {
        void persistPageLayout(sections)
    }, [persistPageLayout, sections])

    // Cleanup
    useEffect(() => {
        return () => {
            abortPendingSave()
            loadAbortControllerRef.current?.abort()
            previewAbortControllerRef.current?.abort()
            reset()
        }
    }, [abortPendingSave, reset])

    useAuthoringShortcuts([
        { key: 'z', ctrlOrMeta: true, onInvoke: () => useBuilderStore.getState().undo() },
        { key: 'y', ctrlOrMeta: true, onInvoke: () => useBuilderStore.getState().redo() },
    ])

    // DnD Handlers
    const handleDragStart = (event) => {
        setActiveId(String(event.active.id))
    }

    const handleDragEnd = (event) => {
        const { active, over } = event
        setActiveId(null)
        if (!over) return

        // Handle new section from palette
        if (String(active.id).startsWith('palette-')) {
            const sectionType = String(active.id).replace('palette-', '')
            if (!isSectionAvailableForPage(sectionType, targetPage)) return
            const overIndex = sections.findIndex((s) => s.id === over.id)
            addSection(sectionType, overIndex >= 0 ? overIndex : undefined, sectionRegistry)
            return
        }

        // Handle reordering
        if (active.id !== over.id) {
            const oldIndex = sections.findIndex((s) => s.id === active.id)
            const newIndex = sections.findIndex((s) => s.id === over.id)
            if (oldIndex !== -1 && newIndex !== -1) {
                reorderSections(oldIndex, newIndex)
            }
        }
    }

    // Action handlers
    const closePreviewModal = useCallback(() => {
        previewAbortControllerRef.current?.abort()
        previewAbortControllerRef.current = null
        setIsLoadingPreview(false)
        setPreviewConfig(null)
        setPreviewError(null)
        setIsPreviewModalOpen(false)
    }, [])

    const handlePreview = useCallback(async () => {
        previewAbortControllerRef.current?.abort()
        const controller = new AbortController()
        previewAbortControllerRef.current = controller

        setPreviewConfig(null)
        setPreviewError(null)
        setIsPreviewModalOpen(true)
        setIsLoadingPreview(true)

        try {
            const result = isDirty
                ? await persistPageLayout(sections)
                : { saved: true, config: storeConfig }
            if (!result.saved) {
                if (!controller.signal.aborted) {
                    setPreviewError(
                        result.error
                        || (isRTL
                            ? 'تعذر حفظ المسودة الحالية، لذلك لن يتم عرض أي محتوى حي كبديل.'
                            : 'The current draft could not be saved, so no live content will be shown as a fallback.')
                    )
                }
                return
            }

            const preview = deriveCurrentVendorScopedDto(
                await storeConfigService.getPreviewConfig({ signal: controller.signal,
                    ...(searchParams.get('themePreviewToken') ? { params: { preview: true, draftThemePreviewToken: searchParams.get('themePreviewToken') } } : {}),
                }),
                currentUser,
            )
            if (controller.signal.aborted) {
                return
            }

            if (!preview) {
                throw new Error('Current backend storefront preview authority is unavailable.')
            }

            setPreviewConfig(preview)
        } catch (error) {
            if (isRequestCanceled(error)) {
                return
            }

            console.error('Preview load failed:', error)
            setPreviewError(
                error?.response?.data?.message
                || (isRTL
                    ? 'تعذر تحميل معاينة المسودة. لم يتم عرض أي محتوى حي.'
                    : 'Draft preview could not be loaded. No live content was shown.')
            )
        } finally {
            if (previewAbortControllerRef.current === controller) {
                previewAbortControllerRef.current = null
                setIsLoadingPreview(false)
            }
        }
    }, [currentUser, isDirty, isRTL, persistPageLayout, sections, storeConfig, searchParams])

    const handlePublish = async () => {
        try {
            const result = isDirty
                ? await persistPageLayout(sections)
                : { saved: true, config: storeConfig }
            if (!result.saved) {
                return
            }

            setPublishing(true)
            setPublishError(null)
            const publishRowVersion = getAcceptedWorkspaceRowVersion(result.config) || requireWorkspaceAdmission()
            if (!publishRowVersion) {
                return
            }

            const sectionsAtPublish = useBuilderStore.getState().sections
            const response = await storeConfigService.publishPage(
                getActiveStorageKey(),
                buildIfMatchOptions(publishRowVersion),
            )
            const acceptedConfig = admitWorkspaceConfig(response, result.config)
            if (!acceptedConfig) {
                throw new Error('Current backend publish response authority is unavailable.')
            }

            setStoreConfig(acceptedConfig)
            setIsPublishConfirmOpen(false)
            if (useBuilderStore.getState().sections !== sectionsAtPublish) {
                setSaveStatus('unsaved')
                setFeedback({
                    kind: 'info',
                    title: t('storefront.homepageEditor.homepagePublished'),
                    message: t('storefront.homepageEditor.concurrentEdits'),
                })
                return
            }
            markClean()
            setLastSavedAt(new Date().toISOString())
            setSaveStatus('saved')
            setSaveError(null)
            setFeedback({
                kind: 'success',
                title: isProductTemplatePage ? t('storefront.homepageEditor.templatePublished') : t('storefront.homepageEditor.homepagePublished'),
                message: t('storefront.homepageEditor.pageLive'),
            })
        } catch (error) {
            console.error('Publish failed:', error)
            setSaveStatus('error')
            setPublishError(
                error?.response?.data?.message
                || (isRTL ? 'فشل نشر الصفحة. حاول مرة أخرى.' : 'Failed to publish the page. Please try again.')
            )
            if (error?.response?.status === 409) {
                setIsPublishConfirmOpen(false)
                setConflict({ message: error?.response?.data?.message })
            }
        } finally {
            setPublishing(false)
        }
    }

    const handleLoadDemo = () => {
        const builtInTemplate = isProductTemplatePage ? getBuiltinProductTemplate(templateIdFromUrl) : null
        const demoSections = filterSectionsForPage(
            builtInTemplate?.sections || getDefaultSections(targetPage),
            targetPage,
        )
        useBuilderStore.getState().setSections(demoSections)
    }

    const { requestNavigation, unsavedDialogProps } = useAuthoringGuard({
        isDirty,
        isBusy: isSaving || isPublishing,
        onSave: handleSave,
        onPreview: handlePreview,
    })

    const handleDiscard = useCallback(async () => {
        abortPendingSave()
        const published = targetPage === 'home' ? storeConfig?.livePages?.home?.sections : null
        if (published) {
            // Admit the discard candidate as the current edit before saving it,
            // so the save concurrency guard can still detect genuinely newer edits.
            useBuilderStore.getState().setSections(published)
            const result = await persistPageLayout(useBuilderStore.getState().sections)
            if (!result.saved) return
            admitSections(filterSectionsForPage(getAcceptedSectionsFromConfig(result.config, targetPage, templateIdFromUrl), targetPage))
            markClean()
            setFeedback({ kind: 'info', title: t('storefront.homepageEditor.changesDiscarded'), message: isRTL ? 'تمت استعادة النسخة المنشورة إلى المسودة.' : 'The published version has been restored to the draft.' })
            return
        }
        admitSections(
            filterSectionsForPage(
                getAcceptedSectionsFromConfig(storeConfig, targetPage, templateIdFromUrl),
                targetPage,
            ),
        )
        markClean()
        setSaveError(null)
        setSaveStatus('idle')
        setFeedback({ kind: 'info', title: t('storefront.homepageEditor.changesDiscarded'), message: t('storefront.homepageEditor.serverDraftRestored') })
    }, [abortPendingSave, admitSections, getAcceptedSectionsFromConfig, markClean, persistPageLayout, storeConfig, targetPage, templateIdFromUrl, isRTL, t])

    const handleLoadLatest = useCallback(async () => {
        setConflict(null)
        await loadPageData(targetPage, templateIdFromUrl)
        setFeedback({ kind: 'info', title: t('storefront.homepageEditor.latestLoaded'), message: t('storefront.homepageEditor.latestReplaced') })
    }, [loadPageData, targetPage, templateIdFromUrl])

    const handleRetryConflict = useCallback(async () => {
        const retainedSections = sections
        try {
            const latestResponse = await storeConfigService.getStoreConfig()
            const latestConfig = admitWorkspaceConfig(latestResponse, storeConfig)
            const latestRowVersion = getAcceptedWorkspaceRowVersion(latestConfig)
            if (!latestConfig || !latestRowVersion) throw new Error('The latest workspace version could not be admitted.')
            setStoreConfig(latestConfig)
            setConflict(null)
            await persistPageLayout(retainedSections, latestRowVersion)
        } catch (error) {
            setSaveError(error?.response?.data?.message || error.message || 'Your draft could not be retried.')
        }
    }, [admitWorkspaceConfig, getAcceptedWorkspaceRowVersion, persistPageLayout, sections, storeConfig])

    const handleTemplateChange = (templateId) => {
        requestNavigation(`/storefront/catalog/product-template?templateId=${encodeURIComponent(templateId)}`)
    }

    const handleCreateTemplate = () => {
        setNewTemplateName('')
        setIsCreateTemplateOpen(true)
    }

    const confirmCreateTemplate = () => {
        const templateId = newTemplateName
            .trim()
            .toLowerCase()
            .replace(/[^a-z0-9]+/g, '-')
            .replace(/^-+|-+$/g, '')

        if (!templateId) {
            return
        }

        setIsCreateTemplateOpen(false)
        requestNavigation(`/storefront/catalog/product-template?templateId=${encodeURIComponent(templateId)}`)
    }

    const handleDeleteTemplate = () => {
        if (templateIdFromUrl === 'default') {
            return
        }

        setIsDeleteConfirmOpen(true)
    }

    const confirmDeleteTemplate = async () => {
        try {
            const rowVersion = requireWorkspaceAdmission()
            if (!rowVersion) {
                return
            }

            await storeConfigService.deleteProductTemplate(
                templateIdFromUrl,
                buildIfMatchOptions(rowVersion),
            )
            setIsDeleteConfirmOpen(false)
            markClean()
            navigate('/storefront/catalog/product-template?templateId=default')
        } catch (error) {
            console.error('Delete product template failed:', error)
            setFeedback({ kind: 'error', title: t('storefront.homepageEditor.templateNotDeleted'), message: error?.response?.data?.message || t('storefront.prompts.deleteTemplateFailed') })
        }
    }

    const handleExit = () => {
        requestNavigation('/storefront')
    }

    if (!storeConfig) {
        return (
            <div className="min-h-screen 2xl:h-screen min-w-0 flex flex-col bg-gray-100 -mx-4 sm:-mx-6 lg:-mx-8 -mt-6">
                <div className="min-h-10 bg-white border-b border-gray-200 flex flex-wrap items-center justify-between gap-2 px-4 py-2">
                    <button
                        onClick={handleExit}
                        className="flex items-center gap-2 text-sm text-gray-600 hover:text-indigo-600 transition-colors"
                    >
                        <ArrowLeft size={16} className={isRTL ? 'rotate-180' : ''} />
                        {isRTL ? 'العودة إلى نظرة عامة' : 'Back to Overview'}
                    </button>
                    <StorefrontBreadcrumbs
                        appendCurrentLabel={isProductTemplatePage}
                        currentLabel={isProductTemplatePage ? activeTemplateName : ''}
                        onNavigate={(path) => requestNavigation(path)}
                    />
                </div>

                <div className="flex flex-1 items-center justify-center px-6">
                    <div className="max-w-lg rounded-2xl border border-gray-200 bg-white p-6 text-center shadow-sm">
                        <p className="text-lg font-semibold text-gray-900">
                            {loadError
                                ? (isRTL ? 'تعذر فتح مساحة العمل' : 'Workspace admission failed')
                                : (isRTL ? 'جار فتح مساحة العمل' : 'Opening workspace')}
                        </p>
                        <p className="mt-2 text-sm text-gray-600">
                            {loadError
                                || (isLoadingWorkspace
                                    ? (isRTL
                                        ? 'يتم التحقق من صلاحية المتجر والمسودة الحالية من الخادم.'
                                        : 'Checking the current store and draft with backend authority.')
                                    : (isRTL
                                        ? 'لم يتم قبول مساحة العمل من الخادم.'
                                        : 'The workspace was not admitted by the backend.'))}
                        </p>
                        {loadError && (
                            <button
                                onClick={() => loadPageData(targetPage, templateIdFromUrl)}
                                className="mt-5 inline-flex items-center justify-center rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-indigo-700"
                            >
                                {isRTL ? 'إعادة المحاولة' : 'Retry admission'}
                            </button>
                        )}
                    </div>
                </div>
            </div>
        )
    }

    return (
        <DndContext
            sensors={sensors}
            collisionDetection={closestCenter}
            onDragStart={handleDragStart}
            onDragEnd={handleDragEnd}
        >
            <div className="min-h-screen 2xl:h-screen min-w-0 flex flex-col bg-gray-100 -mx-4 sm:-mx-6 lg:-mx-8 -mt-6">
                <div className="min-h-10 border-b border-gray-200 bg-white px-4 py-2">
                    <StorefrontBreadcrumbs
                        appendCurrentLabel={isProductTemplatePage}
                        currentLabel={isProductTemplatePage ? activeTemplateName : ''}
                        onNavigate={(path) => requestNavigation(path)}
                    />
                </div>

                {/* Top Bar */}
                <BuilderTopBar
                    onBack={handleExit}
                    onPageChange={requestNavigation}
                    onPreview={handlePreview}
                    onPublish={() => setIsPublishConfirmOpen(true)}
                    onHistory={() => requestNavigation('/storefront/storeconfig-version-history')}
                    onDiscard={handleDiscard}
                    onSave={handleSave}
                    templateOptions={activeTemplateOptions}
                    activeTemplateId={templateIdFromUrl}
                    onTemplateChange={handleTemplateChange}
                    onCreateTemplate={handleCreateTemplate}
                    onDeleteTemplate={handleDeleteTemplate}
                    showTemplateControls={isProductTemplatePage}
                    saveStatus={saveStatus}
                    saveError={saveError}
                    lastSavedAt={lastSavedAt}
                    hasPendingDraft={JSON.stringify(storeConfig?.pages?.home) !== JSON.stringify(storeConfig?.livePages?.home)}
                    onRetrySave={handleRetrySave}
                />

                {feedback ? <div className="px-4 pt-3"><AuthoringFeedback {...feedback} /></div> : null}

                {loadError && (
                    <div className="border-b border-red-200 bg-red-50 px-4 py-3">
                        <div className="mx-auto flex max-w-6xl flex-col gap-3 md:flex-row md:items-center md:justify-between">
                            <div>
                                <p className="font-medium text-red-900">
                                    {isRTL ? 'تعذر تحميل بيانات الصفحة' : 'Failed to load page data'}
                                </p>
                                <p className="mt-1 text-sm text-red-700">{loadError}</p>
                            </div>
                            <button
                                onClick={() => loadPageData(targetPage, templateIdFromUrl)}
                                className="inline-flex items-center justify-center rounded-lg bg-red-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-red-700"
                            >
                                {isRTL ? 'إعادة المحاولة' : 'Retry load'}
                            </button>
                        </div>
                    </div>
                )}

                {saveError && (
                    <div className="border-b border-red-200 bg-red-50 px-4 py-3">
                        <div className="mx-auto flex max-w-6xl flex-col gap-3 md:flex-row md:items-center md:justify-between">
                            <div>
                                <p className="font-medium text-red-900">
                                    {isRTL ? 'تعذر حفظ تغييرات الصفحة' : 'Page changes are not saved'}
                                </p>
                                <p className="mt-1 text-sm text-red-700">{saveError}</p>
                            </div>
                            <button
                                onClick={handleRetrySave}
                                className="inline-flex items-center justify-center rounded-lg bg-red-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-red-700"
                            >
                                {isRTL ? 'إعادة المحاولة' : 'Retry save'}
                            </button>
                        </div>
                    </div>
                )}

                {publishError && (
                    <div className="border-b border-red-200 bg-red-50 px-4 py-3">
                        <div className="mx-auto flex max-w-6xl flex-col gap-3 md:flex-row md:items-center md:justify-between">
                            <div>
                                <p className="font-medium text-red-900">
                                    {isRTL ? 'تعذر نشر الصفحة' : 'Failed to publish page'}
                                </p>
                                <p className="mt-1 text-sm text-red-700">{publishError}</p>
                            </div>
                            <button
                                onClick={handlePublish}
                                className="inline-flex items-center justify-center rounded-lg bg-red-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-red-700"
                            >
                                {isRTL ? 'إعادة المحاولة' : 'Retry publish'}
                            </button>
                        </div>
                    </div>
                )}

                {/* Main Content */}
                <div className="min-w-0 flex-1 flex flex-col 2xl:flex-row 2xl:overflow-hidden">
                    {/* Left: Section Palette */}
                    <SectionPalette pageKey={targetPage} />

                    {/* Center: Canvas */}
                    <BuilderCanvas activeId={activeId} onLoadDemo={handleLoadDemo} storeConfig={storeConfig} tenantId={tenantId} templateId={templateIdFromUrl} />

                    {/* Right: Settings Panel */}
                    <SettingsPanel />
                </div>

                {/* Modals */}
                <AuthoringConfirmDialog
                    open={isPublishConfirmOpen}
                    title={isProductTemplatePage ? t('storefront.homepageEditor.confirmTemplate') : t('storefront.homepageEditor.confirmHomepage')}
                    description={t('storefront.homepageEditor.publishExplanation')}
                    confirmLabel={isProductTemplatePage ? t('storefront.homepageEditor.publishTemplate') : t('storefront.homepageEditor.publishHomepage')}
                    confirmTone="publish"
                    busy={isPublishing}
                    onClose={() => setIsPublishConfirmOpen(false)}
                    onConfirm={handlePublish}
                />
                <AuthoringConfirmDialog
                    open={isDeleteConfirmOpen}
                    title={t('storefront.homepageEditor.deleteTemplateTitle')}
                    description={t('storefront.homepageEditor.deleteTemplateExplanation')}
                    confirmLabel={t('storefront.homepageEditor.deleteTemplate')}
                    confirmTone="danger"
                    onClose={() => setIsDeleteConfirmOpen(false)}
                    onConfirm={confirmDeleteTemplate}
                />
                <AuthoringPromptDialog
                    open={isCreateTemplateOpen}
                    title={t('storefront.prompts.createTemplateTitle')}
                    description={t('storefront.homepageEditor.createTemplateExplanation')}
                    label={t('storefront.prompts.templateNameLabel')}
                    value={newTemplateName}
                    placeholder={t('storefront.homepageEditor.templatePlaceholder')}
                    confirmLabel={t('storefront.prompts.createTemplateTitle')}
                    onChange={setNewTemplateName}
                    onClose={() => setIsCreateTemplateOpen(false)}
                    onConfirm={confirmCreateTemplate}
                />
                <AuthoringConflictDialog
                    open={conflict != null}
                    message={conflict?.message}
                    onClose={() => setConflict(null)}
                    onLoadLatest={handleLoadLatest}
                    onRetry={handleRetryConflict}
                />
                <UnsavedChangesDialog {...unsavedDialogProps} />

                <PreviewModal
                    isOpen={isPreviewModalOpen}
                    onClose={closePreviewModal}
                    previewConfig={previewConfig}
                    previewError={previewError}
                    isLoading={isLoadingPreview}
                    currentPage={targetPage}
                    templateId={templateIdFromUrl}
                    tenantId={tenantId}
                />
            </div>
        </DndContext>
    )
}
