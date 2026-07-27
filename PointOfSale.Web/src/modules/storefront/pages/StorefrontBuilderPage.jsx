/**
 * Storefront Builder Page
 * Phase 9.8: POS Integration - Unified Admin Experience
 * 
 * This is the main Visual Builder page integrated into the POS.
 * 
 * CRITICAL: All edits modify DRAFT ONLY until explicit Publish
 */

import { useState, useEffect, useCallback, useRef } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
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
    PublishModal,
    RollbackModal,
    PreviewModal,
} from '../components/builder'
import { useBuilderStore, selectSections, selectCurrentPage } from '../store'
import { storeConfigService } from '../services'
import { isRequestCanceled } from '../../../services/api'
import { sectionRegistry } from '../builder'
import { useAuthStore } from '../../../store/authStore'
import {
    deriveCurrentStoreIdentityPresentation,
    deriveCurrentVendorScopedDto,
} from '../../../auth/commerceHubVendorIdentityAuthority'
import { getDefaultSections } from '../config'
import {
    getBuiltinProductTemplate,
    getProductTemplateOptions,
} from '@/storefront/products/ProductTemplateRegistry'

export function StorefrontBuilderPage() {
    const { t, i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const navigate = useNavigate()
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
    const reset = useBuilderStore((s) => s.reset)

    // Local state
    const [activeId, setActiveId] = useState(null)
    const [isPublishModalOpen, setIsPublishModalOpen] = useState(false)
    const [isRollbackModalOpen, setIsRollbackModalOpen] = useState(false)
    const [isPreviewModalOpen, setIsPreviewModalOpen] = useState(false)
    /** @type {[import('../types/builder').VersionHistoryEntry[], Function]} */
    const [versions, setVersions] = useState([])
    const [isLoadingVersions, setIsLoadingVersions] = useState(false)
    const [isRollingBack, setIsRollingBack] = useState(false)
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
    const saveTimeoutRef = useRef(null)
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
    const pageFromUrl = searchParams.get('page') || 'home'
    const templateIdFromUrl = searchParams.get('templateId') || 'default'
    const validPages = ['home', 'product', 'category', 'product-template']
    const targetPage = validPages.includes(pageFromUrl) ? pageFromUrl : 'home'
    const activeTemplateOptions = getProductTemplateOptions(storeConfig?.productTemplates)
    const isProductTemplatePage = targetPage === 'product-template'

    const getActiveStorageKey = useCallback(
        () => (isProductTemplatePage ? `product-template:${templateIdFromUrl}` : targetPage),
        [isProductTemplatePage, targetPage, templateIdFromUrl]
    )

    const clearScheduledSave = useCallback((abortRequest = false) => {
        if (saveTimeoutRef.current) {
            clearTimeout(saveTimeoutRef.current)
            saveTimeoutRef.current = null
        }

        if (abortRequest && saveAbortControllerRef.current) {
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

            return Array.isArray(resolvedTemplate.sections) ? resolvedTemplate.sections : []
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

    const loadPageData = useCallback(async (page, templateId = templateIdFromUrl) => {
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

            const acceptedSections = getAcceptedSectionsFromConfig(config, page, templateId)
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
    }, [admitSections, admitWorkspaceConfig, currentUser, getAcceptedSectionsFromConfig, isRTL, templateIdFromUrl])

    // Load page data when target page or product template changes
    useEffect(() => {
        console.log('[Builder] Loading data for page:', targetPage, '| template:', templateIdFromUrl)
        void loadPageData(targetPage, templateIdFromUrl)
    }, [loadPageData, targetPage, templateIdFromUrl])

    useEffect(() => {
        const syncThemePreview = () => {
            if (document.visibilityState === 'hidden') {
                return
            }

            void loadPageData(targetPage, templateIdFromUrl)
        }

        window.addEventListener('focus', syncThemePreview)
        document.addEventListener('visibilitychange', syncThemePreview)

        return () => {
            window.removeEventListener('focus', syncThemePreview)
            document.removeEventListener('visibilitychange', syncThemePreview)
        }
    }, [loadPageData, targetPage, templateIdFromUrl])

    const persistPageLayout = useCallback(async (sectionsToSave = sections) => {
        clearScheduledSave(true)

        const rowVersion = requireWorkspaceAdmission()
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
            const storageKey = getActiveStorageKey()
            const payload = isProductTemplatePage
                ? {
                    templateId: templateIdFromUrl,
                    name: storeConfig?.productTemplates?.[templateIdFromUrl]?.name
                        || getBuiltinProductTemplate(templateIdFromUrl)?.name
                        || templateIdFromUrl,
                    sections: sectionsToSave,
                }
                : { sections: sectionsToSave }

            const response = await storeConfigService.updatePageLayout(
                storageKey,
                payload,
                buildIfMatchOptions(rowVersion, { signal: controller.signal }),
            )

            if (controller.signal.aborted || requestId !== saveRequestIdRef.current) {
                return { saved: false }
            }

            const acceptedConfig = admitWorkspaceConfig(response, storeConfig)
            if (!acceptedConfig) {
                throw new Error('Current backend workspace response authority is unavailable.')
            }

            setStoreConfig(acceptedConfig)
            admitSections(
                getAcceptedSectionsFromConfig(acceptedConfig, targetPage, templateIdFromUrl),
                { preserveSelection: true, resetHistory: false },
            )
            markClean()
            setLastSavedAt(new Date().toISOString())
            setSaveStatus('saved')
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
            return { saved: false, error: errorMessage }
        } finally {
            if (saveAbortControllerRef.current === controller) {
                saveAbortControllerRef.current = null
            }

            if (!controller.signal.aborted && requestId === saveRequestIdRef.current) {
                setSaving(false)
            }
        }
    }, [admitSections, admitWorkspaceConfig, buildIfMatchOptions, clearScheduledSave, getAcceptedSectionsFromConfig, getActiveStorageKey, isProductTemplatePage, isRTL, markClean, requireWorkspaceAdmission, saveError, sections, setSaving, storeConfig?.productTemplates, targetPage, templateIdFromUrl])

    // Debounced auto-save
    const debouncedSave = useCallback((sectionsToSave) => {
        clearScheduledSave(true)
        setSaveStatus('unsaved')
        setSaveError(null)
        saveTimeoutRef.current = setTimeout(() => {
            void persistPageLayout(sectionsToSave)
        }, 1500)
    }, [clearScheduledSave, persistPageLayout])

    // Trigger auto-save when sections change
    useEffect(() => {
        if (isDirty) {
            debouncedSave(sections)
        }
    }, [sections, isDirty, debouncedSave])

    const handleSave = useCallback(async () => {
        await persistPageLayout(sections)
    }, [persistPageLayout, sections])

    const handleRetrySave = useCallback(() => {
        void persistPageLayout(sections)
    }, [persistPageLayout, sections])

    // Cleanup
    useEffect(() => {
        return () => {
            clearScheduledSave(true)
            loadAbortControllerRef.current?.abort()
            previewAbortControllerRef.current?.abort()
            reset()
        }
    }, [clearScheduledSave, reset])

    // Keyboard shortcuts
    useEffect(() => {
        const handleKeyDown = (e) => {
            if ((e.ctrlKey || e.metaKey) && e.key === 's') {
                e.preventDefault()
                handleSave()
            }
            if ((e.ctrlKey || e.metaKey) && e.key === 'z') {
                e.preventDefault()
                useBuilderStore.getState().undo()
            }
            if ((e.ctrlKey || e.metaKey) && e.key === 'y') {
                e.preventDefault()
                useBuilderStore.getState().redo()
            }
        }
        window.addEventListener('keydown', handleKeyDown)
        return () => window.removeEventListener('keydown', handleKeyDown)
    }, [handleSave])

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
            const result = await persistPageLayout(sections)
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
                await storeConfigService.getPreviewConfig({ signal: controller.signal }),
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
    }, [currentUser, isRTL, persistPageLayout, saveError, sections])

    const handlePublish = async () => {
        try {
            const result = await persistPageLayout(sections)
            if (!result.saved) {
                return
            }

            setPublishing(true)
            setPublishError(null)
            const publishRowVersion = getAcceptedWorkspaceRowVersion(result.config) || requireWorkspaceAdmission()
            if (!publishRowVersion) {
                return
            }

            const response = await storeConfigService.publishPage(
                getActiveStorageKey(),
                buildIfMatchOptions(publishRowVersion),
            )
            const acceptedConfig = admitWorkspaceConfig(response, result.config)
            if (!acceptedConfig) {
                throw new Error('Current backend publish response authority is unavailable.')
            }

            setStoreConfig(acceptedConfig)
            setIsPublishModalOpen(false)
            markClean()
            setLastSavedAt(new Date().toISOString())
            setSaveStatus('saved')
            setSaveError(null)
            alert(isRTL ? '✓ تم نشر الصفحة بنجاح!' : '✓ Page published successfully!')
        } catch (error) {
            console.error('Publish failed:', error)
            setSaveStatus('error')
            setPublishError(
                error?.response?.data?.message
                || (isRTL ? 'فشل نشر الصفحة. حاول مرة أخرى.' : 'Failed to publish the page. Please try again.')
            )
            alert(isRTL ? 'فشل النشر. حاول مرة أخرى.' : 'Failed to publish. Please try again.')
        } finally {
            setPublishing(false)
        }
    }

    const handleRollback = async (version, reason) => {
        try {
            setIsRollingBack(true)
            const selectedVersion = versions.find((entry) => entry.version === version)
            if (!selectedVersion) {
                throw new Error(isRTL ? 'الإصدار المحدد غير متاح.' : 'The selected version is no longer available.')
            }

            const rowVersion = requireWorkspaceAdmission()
            if (!rowVersion) {
                return
            }

            await storeConfigService.rollback(
                selectedVersion.version,
                { confirm: true, reason },
                buildIfMatchOptions(rowVersion),
            )
            setIsRollbackModalOpen(false)
            await loadPageData(targetPage, templateIdFromUrl)
            alert(isRTL ? `✓ تم الاستعادة للإصدار ${version}` : `✓ Rolled back to version ${version}`)
        } catch (error) {
            console.error('Rollback failed:', error)
            alert(isRTL ? 'فشل الاستعادة. حاول مرة أخرى.' : 'Failed to rollback. Please try again.')
        } finally {
            setIsRollingBack(false)
        }
    }

    const openRollbackModal = async () => {
        setIsRollbackModalOpen(true)
        setIsLoadingVersions(true)
        try {
            const response = await storeConfigService.getVersionHistory()
            setVersions(response?.versions || [])
        } catch (error) {
            console.error('Version history load failed:', error)
            setVersions([])
            alert(isRTL ? 'فشل تحميل سجل الإصدارات.' : 'Failed to load version history.')
        } finally {
            setIsLoadingVersions(false)
        }
    }

    const handleLoadDemo = () => {
        const builtInTemplate = isProductTemplatePage ? getBuiltinProductTemplate(templateIdFromUrl) : null
        const demoSections = builtInTemplate?.sections || getDefaultSections(currentPage)
        useBuilderStore.getState().setSections(demoSections)
    }

    const handleTemplateChange = (templateId) => {
        navigate(`/storefront/builder?page=product-template&templateId=${encodeURIComponent(templateId)}`)
    }

    const handleCreateTemplate = () => {
        const templateName = window.prompt(isRTL ? 'اسم القالب الجديد' : 'New template name')
        if (!templateName) {
            return
        }

        const templateId = templateName
            .trim()
            .toLowerCase()
            .replace(/[^a-z0-9]+/g, '-')
            .replace(/^-+|-+$/g, '')

        if (!templateId) {
            return
        }

        navigate(`/storefront/builder?page=product-template&templateId=${encodeURIComponent(templateId)}`)
    }

    const handleDeleteTemplate = async () => {
        if (templateIdFromUrl === 'default') {
            return
        }

        const confirmed = window.confirm(
            isRTL ? 'هل تريد حذف قالب المنتج هذا؟' : 'Do you want to delete this product template?'
        )
        if (!confirmed) {
            return
        }

        try {
            const rowVersion = requireWorkspaceAdmission()
            if (!rowVersion) {
                return
            }

            await storeConfigService.deleteProductTemplate(
                templateIdFromUrl,
                buildIfMatchOptions(rowVersion),
            )
            navigate('/storefront/builder?page=product-template&templateId=default')
        } catch (error) {
            console.error('Delete product template failed:', error)
            alert(isRTL ? 'فشل حذف القالب.' : 'Failed to delete template.')
        }
    }

    const handleExit = () => {
        if (isDirty) {
            const confirmed = window.confirm(
                isRTL ? 'لديك تغييرات غير محفوظة. هل أنت متأكد من الخروج؟' : 'You have unsaved changes. Are you sure you want to exit?'
            )
            if (!confirmed) return
        }
        navigate('/storefront')
    }

    if (!storeConfig) {
        return (
            <div className="h-screen flex flex-col bg-gray-100 -m-6 -mt-6">
                <div className="h-10 bg-white border-b border-gray-200 flex items-center px-4">
                    <button
                        onClick={handleExit}
                        className="flex items-center gap-2 text-sm text-gray-600 hover:text-indigo-600 transition-colors"
                    >
                        <ArrowLeft size={16} className={isRTL ? 'rotate-180' : ''} />
                        {isRTL ? 'العودة للإعدادات' : 'Back to Settings'}
                    </button>
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
            <div className="h-screen flex flex-col bg-gray-100 -m-6 -mt-6">
                {/* Back Link */}
                <div className="h-10 bg-white border-b border-gray-200 flex items-center px-4">
                    <button
                        onClick={handleExit}
                        className="flex items-center gap-2 text-sm text-gray-600 hover:text-indigo-600 transition-colors"
                    >
                        <ArrowLeft size={16} className={isRTL ? 'rotate-180' : ''} />
                        {isRTL ? 'العودة للإعدادات' : 'Back to Settings'}
                    </button>
                </div>

                {/* Top Bar */}
                <BuilderTopBar
                    onPreview={handlePreview}
                    onPublish={() => setIsPublishModalOpen(true)}
                    onRollback={openRollbackModal}
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
                    onRetrySave={handleRetrySave}
                />

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
                <div className="flex-1 flex overflow-hidden">
                    {/* Left: Section Palette */}
                    <SectionPalette />

                    {/* Center: Canvas */}
                    <BuilderCanvas activeId={activeId} onLoadDemo={handleLoadDemo} storeConfig={storeConfig} tenantId={tenantId} />

                    {/* Right: Settings Panel */}
                    <SettingsPanel />
                </div>

                {/* Modals */}
                <PublishModal
                    isOpen={isPublishModalOpen}
                    onClose={() => setIsPublishModalOpen(false)}
                    onConfirm={handlePublish}
                    pageKey={currentPage}
                    isPublishing={useBuilderStore.getState().isPublishing}
                />

                <RollbackModal
                    isOpen={isRollbackModalOpen}
                    onClose={() => setIsRollbackModalOpen(false)}
                    onConfirm={handleRollback}
                    versions={versions}
                    isLoading={isLoadingVersions}
                    isRollingBack={isRollingBack}
                />

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
