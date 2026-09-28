import { useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import {
    ArrowLeft,
    Eye,
    Globe,
    GripVertical,
    History,
    Image as ImageIcon,
    ListOrdered,
    Loader2,
    Plus,
    RefreshCcw,
    Save,
    Trash2,
    Undo2,
    Type,
} from 'lucide-react'
import { cmsService, extractCmsConflict } from '../services'
import { sanitizeRichTextHtml } from '@/utils/htmlSanitizer'
import { useAuthStore } from '../../../../store/authStore'
import {
    deriveCurrentCmsPageIdentityPresentation,
    deriveCurrentCmsPageVersionHistoryPresentation,
} from '../cmsPageIdentityAuthority'
import { useStorefrontAdminFrame } from '../../informationArchitecture/StorefrontAdminFrame'
import {
    AuthoringActionBar,
    AuthoringConfirmDialog,
    AuthoringConflictDialog,
    AuthoringFeedback,
    AuthoringDialog,
    AuthoringLoadingState,
    AuthoringPromptDialog,
    AuthoringStatusBadge,
    AuthoringValidationSummary,
    UnsavedChangesDialog,
    getAuthoringErrorFeedback,
    useAuthoringGuard,
} from '../../authoringPlatform'

const CONTENT_TYPES = {
    RichText: 'RichText',
    Blocks: 'Blocks',
}

function createEmptyTranslation() {
    return {
        title: '',
        contentType: CONTENT_TYPES.RichText,
        content: '',
        schemaVersion: 1,
        seo: {
            metaTitle: '',
            metaDescription: '',
            metaKeywords: '',
            openGraphImageUrl: '',
        },
    }
}

function createEmptyPageForm() {
    return {
        slug: '',
        translations: {
            en: createEmptyTranslation(),
            ar: null,
        },
    }
}

function createTextBlock() {
    return {
        type: 'text',
        data: {
            text: '',
            alignment: 'left',
        },
    }
}

function createImageBlock() {
    return {
        type: 'image',
        data: {
            imageUrl: '',
            alt: '',
            caption: '',
        },
    }
}

function createFaqBlock() {
    return {
        type: 'faq',
        data: {
            items: [{ question: '', answer: '' }],
        },
    }
}

export function CmsPageEditor() {
    const { pageId } = useParams()
    const navigate = useNavigate()
    const storefrontFrame = useStorefrontAdminFrame()
    const { t, i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const isNewPage = pageId === 'new'

    const [page, setPage] = useState(null)
    const [form, setForm] = useState(createEmptyPageForm())
    const [activeLocale, setActiveLocale] = useState('en')
    const [acceptedFingerprint, setAcceptedFingerprint] = useState(() => getPageFormFingerprint(createEmptyPageForm()))
    const [isLoading, setIsLoading] = useState(!isNewPage)
    const [isSaving, setIsSaving] = useState(false)
    const [isPublishing, setIsPublishing] = useState(false)
    const [error, setError] = useState('')
    const [notice, setNotice] = useState('')
    const [conflict, setConflict] = useState(null)
    const [isPublishConfirmOpen, setIsPublishConfirmOpen] = useState(false)
    const [isDiscardConfirmOpen, setIsDiscardConfirmOpen] = useState(false)
    const [deleteBlockIndex, setDeleteBlockIndex] = useState(null)
    const [pendingHistorySnapshot, setPendingHistorySnapshot] = useState(null)
    const [versionHistory, setVersionHistory] = useState(null)
    const [isHistoryOpen, setIsHistoryOpen] = useState(false)
    const [isHistoryLoading, setIsHistoryLoading] = useState(false)
    const [rollbackState, setRollbackState] = useState({ version: null, reason: '', confirmed: false, isRollingBack: false })
    const formRef = useRef(form)
    formRef.current = form

    const currentUser = useAuthStore((state) => state.user)
    const currentFingerprint = useMemo(() => getPageFormFingerprint(form), [form])
    const isDirty = currentFingerprint !== acceptedFingerprint
    const canPublish = !isSaving && !isPublishing && (isDirty || Boolean(page?.draft))
    const activeTranslation = form.translations[activeLocale] || createEmptyTranslation()
    const englishTranslation = form.translations.en
    const arabicTranslation = form.translations.ar

    useEffect(() => {
        storefrontFrame.setStorefrontBreadcrumbLabel?.(
            englishTranslation.title.trim() || t(isNewPage ? 'storefront.cms.pageEditor.newPageLabel' : 'storefront.cms.pageEditor.pageLabel'),
        )
        return () => storefrontFrame.setStorefrontBreadcrumbLabel?.('')
    }, [englishTranslation.title, isNewPage, storefrontFrame.setStorefrontBreadcrumbLabel, t])

    useEffect(() => {
        if (isNewPage) {
            setPage(null)
            const emptyForm = createEmptyPageForm()
            setForm(emptyForm)
            setAcceptedFingerprint(getPageFormFingerprint(emptyForm))
            setIsLoading(false)
            return
        }

        loadPage(pageId)
    }, [pageId])

    async function loadPage(id) {
        setIsLoading(true)
        setError('')
        setConflict(null)

        try {
            const data = await cmsService.getPage(id)
            adoptAuthoritativePage(data, id)
        } catch (err) {
            console.error('Failed to load CMS page', err)
            setError(t('storefront.cms.loadFailed'))
        } finally {
            setIsLoading(false)
        }
    }

    function updateForm(patch) {
        setForm((current) => ({
            ...current,
            translations: {
                ...current.translations,
                [activeLocale]: {
                    ...(current.translations[activeLocale] || createEmptyTranslation()),
                    ...patch,
                },
            },
        }))
    }

    function updateSeo(patch) {
        setForm((current) => ({
            ...current,
            translations: {
                ...current.translations,
                [activeLocale]: {
                    ...(current.translations[activeLocale] || createEmptyTranslation()),
                    seo: {
                        ...(current.translations[activeLocale]?.seo || createEmptyTranslation().seo),
                        ...patch,
                    },
                },
            },
        }))
    }

    function selectLocale(locale) {
        setForm((current) => locale === 'ar' && !current.translations.ar
            ? {
                ...current,
                translations: {
                    ...current.translations,
                    ar: createEmptyTranslation(),
                },
            }
            : current)
        setActiveLocale(locale)
    }

    function removeArabicTranslation() {
        setForm((current) => ({
            ...current,
            translations: { ...current.translations, ar: null },
        }))
        setActiveLocale('en')
    }

    async function handleSave(expectedVersionOverride = null) {
        if (!form.slug.trim() || !hasTranslationContent(englishTranslation)) {
            setError(t('storefront.cms.validation.titleAndSlugRequired'))
            return null
        }

        if (arabicTranslation && !hasTranslationContent(arabicTranslation)) {
            setError(t('storefront.cms.pageEditor.arabicIncomplete', { defaultValue: 'Arabic must include both title and content, or be removed.' }))
            return null
        }

        if (!isNewPage && !isDirty && !expectedVersionOverride) {
            return page
        }

        setIsSaving(true)
        setError('')
        setNotice('')
        setConflict(null)

        try {
            const payload = toRequestPayload(form)
            const submittedFingerprint = JSON.stringify(payload)
            if (!isNewPage) {
                payload.expectedVersion = expectedVersionOverride ?? page?.draft?.version ?? page?.published?.version
            }
            const savedPage = isNewPage
                ? await cmsService.createPage(payload)
                : await cmsService.updatePage(pageId, payload)
            const hasNewerLocalEdits = getPageFormFingerprint(formRef.current) !== submittedFingerprint
            const admittedPage = adoptAuthoritativePage(
                savedPage,
                isNewPage ? undefined : pageId,
                { preserveLocalCandidate: hasNewerLocalEdits },
            )
            setNotice(hasNewerLocalEdits
                ? t('storefront.cms.pageEditor.savedWithNewerEdits')
                : t('storefront.cms.pageEditor.saved'))

            if (isNewPage) {
                const returnTarget = new URLSearchParams(window.location.search).get('returnTo')
                if (returnTarget === 'navigation' && window.opener) {
                    window.opener.postMessage({ type: 'shopixy:cms-page-created', pageId: admittedPage.id }, window.location.origin)
                    window.close()
                    return admittedPage
                }
                navigate(`/storefront/content/pages/${admittedPage.id}`, { replace: true })
            }

            return admittedPage
        } catch (err) {
            console.error('Failed to save CMS page', err)
            const conflictState = extractCmsConflict(err)
            if (conflictState) {
                setConflict(conflictState)
                setError(conflictState.message)
                return null
            }
            const feedback = getAuthoringErrorFeedback(err)
            setError(`${feedback.title}: ${feedback.message}`)
            return null
        } finally {
            setIsSaving(false)
        }
    }

    async function handlePublish() {
        const targetPage = (isDirty || !page) ? await handleSave() : page
        if (!targetPage) {
            return
        }

        setIsPublishing(true)
        setConflict(null)
        setError('')
        setNotice('')
        try {
            const expectedVersion = targetPage.draft?.version
            if (!expectedVersion) {
                throw new Error('The page does not have a draft version to publish.')
            }
            const publishedCandidateFingerprint = getPageFormFingerprint(fromPageResponse(targetPage))
            const published = await cmsService.publishPage(targetPage.id, expectedVersion)
            const hasNewerLocalEdits = getPageFormFingerprint(formRef.current) !== publishedCandidateFingerprint
            adoptAuthoritativePage(published, targetPage.id, { preserveLocalCandidate: hasNewerLocalEdits })
            setNotice(hasNewerLocalEdits
                ? t('storefront.cms.pageEditor.publishedWithNewerEdits')
                : t('storefront.cms.pageEditor.published'))
            setIsPublishConfirmOpen(false)
        } catch (err) {
            console.error('Failed to publish CMS page', err)
            const conflictState = extractCmsConflict(err)
            if (conflictState) {
                setIsPublishConfirmOpen(false)
                setConflict(conflictState)
                setError(conflictState.message)
                return
            }
            const feedback = getAuthoringErrorFeedback(err)
            setError(`${feedback.title}: ${feedback.message}`)
        } finally {
            setIsPublishing(false)
        }
    }

    async function loadVersionHistory() {
        if (isNewPage || !pageId) {
            return null
        }

        setIsHistoryLoading(true)
        try {
            const data = await cmsService.getVersionHistory(pageId)
            const admittedHistory = deriveCurrentCmsPageVersionHistoryPresentation(data, currentUser, { expectedPageId: pageId })
            if (!admittedHistory) {
                throw new Error('CMS version history was rejected by backend page identity admission.')
            }

            setVersionHistory(admittedHistory)
            return admittedHistory
        } catch (err) {
            console.error('Failed to load CMS version history', err)
            setError(t('storefront.cms.pageEditor.historyLoadFailed'))
            return null
        } finally {
            setIsHistoryLoading(false)
        }
    }

    function adoptAuthoritativePage(response, expectedPageId = pageId, { preserveLocalCandidate = false } = {}) {
        const admittedPage = deriveCurrentCmsPageIdentityPresentation(response, currentUser, {
            expectedPageId: expectedPageId === 'new' ? undefined : expectedPageId,
        })
        if (!admittedPage) {
            throw new Error('CMS page was rejected by backend page identity admission.')
        }

        const acceptedForm = fromPageResponse(admittedPage)
        setPage(admittedPage)
        setAcceptedFingerprint(getPageFormFingerprint(acceptedForm))
        if (!preserveLocalCandidate) {
            setForm(acceptedForm)
        }
        setConflict(null)
        return admittedPage
    }

    function adoptConflictLatestVersion(conflictState, expectedPageId = pageId) {
        if (!conflictState?.latestVersion) {
            return null
        }

        return adoptAuthoritativePage(conflictState.latestVersion, expectedPageId)
    }

    async function openVersionHistory() {
        setIsHistoryOpen(true)
        await loadVersionHistory()
    }

    function previewHistoryVersion(entry) {
        if (isDirty) {
            setPendingHistorySnapshot(entry)
            return
        }
        setForm(fromVersionSnapshot(entry.snapshot))
    }

    async function handleRollbackVersion() {
        if (!rollbackState.confirmed || !rollbackState.version) {
            return
        }

        const expectedVersion = page?.draft?.version ?? page?.published?.version
        if (!expectedVersion) {
            setError(t('storefront.cms.pageEditor.rollbackRequiresVersion'))
            return
        }

        setRollbackState((current) => ({ ...current, isRollingBack: true }))
        setConflict(null)
        try {
            const rolledBack = await cmsService.rollbackPage(
                pageId,
                rollbackState.version,
                expectedVersion,
                rollbackState.reason
            )
            const admittedPage = deriveCurrentCmsPageIdentityPresentation(rolledBack, currentUser, { expectedPageId: pageId })
            if (!admittedPage) {
                throw new Error('Rolled back CMS page was rejected by backend page identity admission.')
            }

            adoptAuthoritativePage(admittedPage, pageId)
            setRollbackState({ version: null, reason: '', confirmed: false, isRollingBack: false })
            await loadVersionHistory()
        } catch (err) {
            console.error('Failed to rollback CMS page', err)
            const conflictState = extractCmsConflict(err)
            if (conflictState) {
                setConflict(conflictState)
                setError(conflictState.message)
            } else {
                setError(err?.response?.data?.message || t('storefront.cms.pageEditor.rollbackFailed'))
            }
            setRollbackState((current) => ({ ...current, isRollingBack: false }))
        }
    }

    async function handlePreview() {
        if (!form.slug.trim()) {
            setError(t('storefront.cms.validation.previewSlugRequired'))
            return
        }

        if (isNewPage) {
            setError(t('storefront.cms.validation.previewSaveFirst'))
            return
        }

        if (isDirty) {
            setError(t('storefront.cms.pageEditor.previewDirty'))
            return
        }

        try {
            const previewAccess = await cmsService.createPreviewLink(pageId)
            window.open(previewAccess.previewUrl, '_blank', 'noopener,noreferrer')
        } catch (err) {
            console.error('Failed to create CMS preview link', err)
            setError(t('storefront.cms.validation.previewLinkFailed'))
        }
    }

    function addBlock(kind) {
        const currentBlocks = Array.isArray(activeTranslation.content) ? activeTranslation.content : []
        const nextBlock = kind === 'image'
            ? createImageBlock()
            : kind === 'faq'
                ? createFaqBlock()
                : createTextBlock()

        updateForm({
            contentType: CONTENT_TYPES.Blocks,
            content: [...currentBlocks, nextBlock],
        })
    }

    function updateBlock(index, nextBlock) {
        updateForm({
            content: activeTranslation.content.map((block, blockIndex) => blockIndex === index ? nextBlock : block),
        })
    }

    function removeBlock(index) {
        setDeleteBlockIndex(index)
    }

    function confirmRemoveBlock() {
        updateForm({
            content: activeTranslation.content.filter((_, blockIndex) => blockIndex !== deleteBlockIndex),
        })
        setDeleteBlockIndex(null)
    }

    function moveBlock(index, offset) {
        const targetIndex = index + offset
        if (targetIndex < 0 || targetIndex >= activeTranslation.content.length) {
            return
        }

        const blocks = [...activeTranslation.content]
        const [selected] = blocks.splice(index, 1)
        blocks.splice(targetIndex, 0, selected)
        updateForm({ content: blocks })
    }

    function handleDiscard() {
        const acceptedForm = page ? fromPageResponse(page) : createEmptyPageForm()
        setForm(acceptedForm)
        setAcceptedFingerprint(getPageFormFingerprint(acceptedForm))
        setError('')
        setNotice(t('storefront.cms.pageEditor.discarded'))
        setConflict(null)
        setIsDiscardConfirmOpen(false)
    }

    const { requestNavigation, unsavedDialogProps } = useAuthoringGuard({
        isDirty,
        isBusy: isSaving || isPublishing,
        onSave: () => handleSave(),
        onPreview: handlePreview,
    })

    const handleLoadLatest = () => {
        if (!conflict?.latestVersion) return
        try {
            adoptConflictLatestVersion(conflict, pageId)
            setError('')
            setNotice(t('storefront.cms.pageEditor.latestLoaded'))
        } catch {
            setError(t('storefront.cms.pageEditor.latestRejected'))
        }
    }

    const handleRetryConflict = () => {
        const latestVersion = conflict?.latestVersion?.draft?.version ?? conflict?.latestVersion?.published?.version
        if (!latestVersion) return
        void handleSave(latestVersion)
    }

    if (isLoading) {
        return <AuthoringLoadingState label={t('storefront.cms.pageEditor.loading')} />
    }

    if (!isNewPage && !page) {
        return (
            <div className="rounded-[28px] border border-rose-200 bg-white p-8 shadow-sm">
                <h1 className="text-2xl font-semibold text-slate-900">{t('storefront.cms.pageEditor.unavailable')}</h1>
                <p className="mt-2 text-sm text-rose-700">{error || t('storefront.cms.pageEditor.unavailableMessage')}</p>
                <button type="button" onClick={() => loadPage(pageId)} className="mt-5 inline-flex items-center gap-2 rounded-xl bg-slate-900 px-4 py-2 text-sm font-medium text-white">
                    <RefreshCcw size={16} /> {t('storefront.cms.pageEditor.retryLoading')}
                </button>
            </div>
        )
    }

    return (
        <div className="space-y-6">
            <AuthoringActionBar
                title={isNewPage ? t('storefront.cms.pageEditor.create') : englishTranslation.title || t('storefront.cms.pageEditor.edit')}
                subtitle={page?.publishedAtUtc ? t('storefront.cms.pageEditor.lastPublished', { time: new Date(page.publishedAtUtc).toLocaleString(isRTL ? 'ar' : 'en') }) : t('storefront.cms.pageEditor.draftSubtitle')}
                backAction={{ label: t('storefront.cms.pageEditor.back'), onClick: () => requestNavigation('/storefront/content/pages') }}
                status={<AuthoringStatusBadge state={isPublishing ? 'publishing' : isSaving ? 'saving' : isDirty ? 'dirty' : page?.published ? 'published' : 'draft'} />}
                previewAction={{ label: t('storefront.cms.pageEditor.preview'), onClick: handlePreview, disabled: isNewPage || isDirty || isSaving || isPublishing, title: isDirty ? t('storefront.cms.pageEditor.saveBeforePreview') : undefined }}
                historyAction={{ label: t('storefront.cms.pageEditor.history'), onClick: openVersionHistory, disabled: isNewPage || isSaving || isPublishing }}
                discardAction={{ label: t('storefront.cms.pageEditor.discard'), onClick: () => setIsDiscardConfirmOpen(true), disabled: !isDirty || isSaving || isPublishing }}
                saveAction={{ label: t('storefront.cms.pageEditor.saveDraft'), busyLabel: t('storefront.cms.pageEditor.saving'), onClick: () => handleSave(), disabled: !isDirty || isPublishing, busy: isSaving }}
                publishAction={{ label: t('storefront.cms.pageEditor.publishPage'), busyLabel: t('storefront.cms.pageEditor.publishing'), onClick: () => setIsPublishConfirmOpen(true), disabled: !canPublish || isSaving, busy: isPublishing }}
            />

            {error ? <AuthoringFeedback kind="error" title={t('storefront.cms.pageEditor.actionFailed')} message={error} /> : null}
            {notice ? <AuthoringFeedback kind="success" title={t('storefront.cms.pageEditor.updated')} message={notice} /> : null}
            <AuthoringValidationSummary
                errors={[
                    ...(!hasTranslationContent(englishTranslation) ? [t('storefront.cms.pageEditor.titleRequired')] : []),
                    ...(!form.slug.trim() ? [t('storefront.cms.pageEditor.slugRequired')] : []),
                    ...(arabicTranslation && (!arabicTranslation.title.trim() || !hasTranslationContent(arabicTranslation))
                        ? [t('storefront.cms.pageEditor.arabicIncomplete', { defaultValue: 'Arabic must include both title and content, or be removed.' })]
                        : []),
                ]}
                title={t('storefront.cms.pageEditor.validationTitle')}
            />

            <section className="rounded-[26px] border border-slate-200 bg-white p-4 shadow-sm">
                <div className="flex flex-wrap items-center justify-between gap-3">
                    <div className="flex gap-2" role="tablist" aria-label={t('storefront.cms.pageEditor.languages', { defaultValue: 'Content languages' })}>
                        <button type="button" role="tab" aria-selected={activeLocale === 'en'} onClick={() => selectLocale('en')} className={`rounded-xl px-4 py-2 text-sm font-semibold ${activeLocale === 'en' ? 'bg-slate-900 text-white' : 'bg-slate-100 text-slate-700'}`}>
                            {t('storefront.cms.pageEditor.englishRequired', { defaultValue: 'English (required)' })}
                        </button>
                        <button type="button" role="tab" aria-selected={activeLocale === 'ar'} onClick={() => selectLocale('ar')} className={`rounded-xl px-4 py-2 text-sm font-semibold ${activeLocale === 'ar' ? 'bg-slate-900 text-white' : 'bg-slate-100 text-slate-700'}`}>
                            {t('storefront.cms.pageEditor.arabicOptional', { defaultValue: 'Arabic (optional)' })}
                        </button>
                    </div>
                    <div className="flex items-center gap-3 text-xs text-slate-500">
                        <span>{hasTranslationContent(englishTranslation) ? t('storefront.cms.pageEditor.complete', { defaultValue: 'English complete' }) : t('storefront.cms.pageEditor.incomplete', { defaultValue: 'English incomplete' })}</span>
                        <span>{arabicTranslation && hasTranslationContent(arabicTranslation) ? t('storefront.cms.pageEditor.arabicComplete', { defaultValue: 'Arabic complete' }) : t('storefront.cms.pageEditor.arabicMissing', { defaultValue: 'Arabic not supplied' })}</span>
                        {arabicTranslation && <button type="button" onClick={removeArabicTranslation} className="rounded-lg border border-rose-200 px-3 py-1.5 font-medium text-rose-700">{t('storefront.cms.pageEditor.removeArabic', { defaultValue: 'Remove Arabic' })}</button>}
                    </div>
                </div>
            </section>

            <div className="grid gap-6 xl:grid-cols-[340px_minmax(0,1fr)]" dir={activeLocale === 'ar' ? 'rtl' : 'ltr'}>
                <div className="space-y-6">
                    <section className="rounded-[28px] border border-slate-200 bg-white p-6 shadow-sm">
                        <h2 className="text-lg font-semibold text-slate-900">
                            {t('storefront.cms.pageEditor.details')}
                        </h2>
                        <div className="mt-5 space-y-4">
                            <LabeledInput
                                label={t('storefront.cms.pageEditor.titleLabel')}
                                value={activeTranslation.title}
                                onChange={(value) => updateForm({ title: value })}
                                placeholder={t('storefront.cms.pageEditor.titlePlaceholder')}
                            />
                            <LabeledInput
                                label={t('storefront.cms.pageEditor.slugLabel')}
                                value={form.slug}
                                onChange={(value) => setForm((current) => ({ ...current, slug: normalizeSlugInput(value) }))}
                                placeholder={t('storefront.cms.pageEditor.slugPlaceholder')}
                                helpText={t('storefront.cms.pageEditor.slugHelp')}
                            />
                            <label className="block">
                                <span className="mb-2 block text-sm font-medium text-slate-700">
                                    {t('storefront.cms.pageEditor.contentMode')}
                                </span>
                                <select
                                    value={activeTranslation.contentType}
                                    onChange={(event) => updateForm({
                                        contentType: event.target.value,
                                        content: event.target.value === CONTENT_TYPES.Blocks
                                            ? (Array.isArray(activeTranslation.content) ? activeTranslation.content : [])
                                            : (typeof activeTranslation.content === 'string' ? activeTranslation.content : ''),
                                    })}
                                    className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-700 outline-none transition focus:border-sky-400 focus:ring-4 focus:ring-sky-100"
                                >
                                    <option value={CONTENT_TYPES.RichText}>{t('storefront.cms.pageEditor.richText')}</option>
                                    <option value={CONTENT_TYPES.Blocks}>{t('storefront.cms.pageEditor.blocks')}</option>
                                </select>
                            </label>
                        </div>
                    </section>

                    <section className="rounded-[28px] border border-slate-200 bg-white p-6 shadow-sm">
                        <h2 className="text-lg font-semibold text-slate-900">
                            {t('storefront.cms.pageEditor.seoSettings')}
                        </h2>
                        <div className="mt-5 space-y-4">
                            <LabeledInput
                                label={t('storefront.cms.pageEditor.metaTitle')}
                                value={activeTranslation.seo.metaTitle}
                                onChange={(value) => updateSeo({ metaTitle: value })}
                            />
                            <LabeledTextarea
                                label={t('storefront.cms.pageEditor.metaDescription')}
                                value={activeTranslation.seo.metaDescription}
                                onChange={(event) => updateSeo({ metaDescription: event.target.value })}
                                rows={4}
                            />
                            <LabeledInput
                                label={t('storefront.cms.pageEditor.metaKeywords')}
                                value={activeTranslation.seo.metaKeywords}
                                onChange={(value) => updateSeo({ metaKeywords: value })}
                            />
                            <LabeledInput
                                label={t('storefront.cms.pageEditor.openGraphImage')}
                                value={activeTranslation.seo.openGraphImageUrl}
                                onChange={(value) => updateSeo({ openGraphImageUrl: value })}
                                placeholder={t('storefront.cms.pageEditor.openGraphPlaceholder')}
                            />
                        </div>
                    </section>
                </div>

                <section className="rounded-[30px] border border-slate-200 bg-white p-6 shadow-sm">
                    <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
                        <div>
                            <h2 className="text-lg font-semibold text-slate-900">
                                {t('storefront.cms.pageEditor.content')}
                            </h2>
                            <p className="mt-1 text-sm text-slate-500">
                                {activeTranslation.contentType === CONTENT_TYPES.RichText
                                    ? t('storefront.cms.pageEditor.richTextHelp')
                                    : t('storefront.cms.pageEditor.blocksHelp')}
                            </p>
                        </div>

                        {activeTranslation.contentType === CONTENT_TYPES.Blocks && (
                            <div className="flex flex-wrap gap-2">
                                <button
                                    onClick={() => addBlock('text')}
                                    className="inline-flex items-center gap-2 rounded-xl border border-slate-200 px-3 py-2 text-sm font-medium text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                                >
                                    <Type size={16} />
                                    {t('storefront.cms.pageEditor.textBlock')}
                                </button>
                                <button
                                    onClick={() => addBlock('image')}
                                    className="inline-flex items-center gap-2 rounded-xl border border-slate-200 px-3 py-2 text-sm font-medium text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                                >
                                    <ImageIcon size={16} />
                                    {t('storefront.cms.pageEditor.imageBlock')}
                                </button>
                                <button
                                    onClick={() => addBlock('faq')}
                                    className="inline-flex items-center gap-2 rounded-xl border border-slate-200 px-3 py-2 text-sm font-medium text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                                >
                                    <ListOrdered size={16} />
                                    {t('storefront.cms.pageEditor.faqBlock')}
                                </button>
                            </div>
                        )}
                    </div>

                    <div className="mt-6">
                        {activeTranslation.contentType === CONTENT_TYPES.RichText ? (
                            <RichTextEditor
                                value={typeof activeTranslation.content === 'string' ? activeTranslation.content : ''}
                                onChange={(value) => updateForm({ content: value })}
                                t={t}
                            />
                        ) : (
                            <BlocksEditor
                                blocks={Array.isArray(activeTranslation.content) ? activeTranslation.content : []}
                                onUpdate={updateBlock}
                                onMove={moveBlock}
                                onRemove={removeBlock}
                                t={t}
                            />
                        )}
                    </div>
                </section>
            </div>

            <AuthoringDialog
                open={isHistoryOpen}
                title={t('storefront.cms.pageEditor.versionHistoryTitle')}
                description={t('storefront.cms.pageEditor.versionHistoryDescription')}
                onClose={rollbackState.isRollingBack ? undefined : () => setIsHistoryOpen(false)}
                panelClassName="max-h-[90vh] !max-w-3xl overflow-hidden"
            >
                        <div className="mt-4 flex justify-end">
                            <button
                                type="button"
                                onClick={() => setIsHistoryOpen(false)}
                                disabled={rollbackState.isRollingBack}
                                className="rounded-xl border border-slate-200 px-3 py-2 text-sm font-medium text-slate-600 transition hover:bg-slate-50 disabled:opacity-50"
                            >
                                {t('storefront.cms.pageEditor.close')}
                            </button>
                        </div>
                        <div className="mt-4 max-h-[calc(90vh-180px)] overflow-y-auto">
                            {isHistoryLoading ? (
                                <AuthoringLoadingState label={t('storefront.cms.pageEditor.historyLoading')} />
                            ) : (
                                <div className="space-y-4">
                                    {(versionHistory?.versions || []).map((entry) => (
                                        <div key={entry.version} className="rounded-2xl border border-slate-200 p-4">
                                            <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
                                                <div>
                                                    <div className="flex flex-wrap items-center gap-2">
                                                        <span className="text-sm font-semibold text-slate-900">v{entry.version}</span>
                                                        {entry.isPublished && <span className="rounded-full bg-emerald-50 px-2 py-1 text-xs font-medium text-emerald-700">{t('storefront.cms.published')}</span>}
                                                        {entry.isCurrentDraft && <span className="rounded-full bg-amber-50 px-2 py-1 text-xs font-medium text-amber-700">{t('storefront.cms.pageEditor.currentDraft')}</span>}
                                                    </div>
                                                    <p className="mt-1 text-sm text-slate-700">{entry.snapshot.translations?.en?.title || entry.snapshot.title}</p>
                                                    <p className="mt-1 text-xs text-slate-500">/{entry.snapshot.slug}</p>
                                                    <p className="mt-1 text-xs text-slate-400">
                                                        {new Date(entry.updatedAtUtc).toLocaleString()}
                                                    </p>
                                                </div>
                                                <div className="flex flex-wrap gap-2">
                                                    <button
                                                        type="button"
                                                        onClick={() => previewHistoryVersion(entry)}
                                                        className="inline-flex items-center gap-2 rounded-xl border border-slate-200 px-3 py-2 text-sm font-medium text-slate-700 transition hover:bg-slate-50"
                                                    >
                                                        <Eye size={14} />
                                                        {t('storefront.cms.pageEditor.loadSnapshot')}
                                                    </button>
                                                    <button
                                                        type="button"
                                                        onClick={() => setRollbackState({ version: entry.version, reason: '', confirmed: false, isRollingBack: false })}
                                                        disabled={entry.isPublished || rollbackState.isRollingBack}
                                                        className="inline-flex items-center gap-2 rounded-xl border border-rose-200 px-3 py-2 text-sm font-medium text-rose-700 transition hover:bg-rose-50 disabled:opacity-50"
                                                    >
                                                        <Undo2 size={14} />
                                                        {t('storefront.cms.pageEditor.rollback')}
                                                    </button>
                                                </div>
                                            </div>
                                            {rollbackState.version === entry.version && (
                                                <div className="mt-4 rounded-2xl border border-rose-100 bg-rose-50 p-4">
                                                    <LabeledTextarea
                                                        label={t('storefront.cms.pageEditor.rollbackReason')}
                                                        value={rollbackState.reason}
                                                        onChange={(event) => setRollbackState((current) => ({ ...current, reason: event.target.value }))}
                                                        rows={3}
                                                    />
                                                    <label className="mt-3 flex items-start gap-3 text-sm text-rose-900">
                                                        <input
                                                            type="checkbox"
                                                            checked={rollbackState.confirmed}
                                                            onChange={(event) => setRollbackState((current) => ({ ...current, confirmed: event.target.checked }))}
                                                            className="mt-1"
                                                        />
                                                        <span>{t('storefront.cms.pageEditor.rollbackAcknowledgment')}</span>
                                                    </label>
                                                    <button
                                                        type="button"
                                                        onClick={handleRollbackVersion}
                                                        disabled={!rollbackState.confirmed || rollbackState.isRollingBack}
                                                        className="mt-4 inline-flex items-center gap-2 rounded-xl bg-rose-700 px-4 py-2 text-sm font-medium text-white transition hover:bg-rose-800 disabled:opacity-50"
                                                    >
                                                        {rollbackState.isRollingBack ? <Loader2 size={16} className="animate-spin" /> : <Undo2 size={16} />}
                                                        {t('storefront.cms.pageEditor.rollbackNow')}
                                                    </button>
                                                </div>
                                            )}
                                        </div>
                                    ))}
                                </div>
                            )}
                        </div>
            </AuthoringDialog>

            <AuthoringConfirmDialog
                open={isPublishConfirmOpen}
                title={t('storefront.cms.pageEditor.publishConfirmTitle')}
                description={t('storefront.cms.pageEditor.publishConfirmDescription')}
                confirmLabel={t('storefront.cms.pageEditor.publishPage')}
                confirmTone="publish"
                busy={isPublishing}
                onClose={() => setIsPublishConfirmOpen(false)}
                onConfirm={handlePublish}
            />
            <AuthoringConfirmDialog
                open={isDiscardConfirmOpen}
                title={t('storefront.cms.pageEditor.discardConfirmTitle')}
                description={t('storefront.cms.pageEditor.discardConfirmDescription')}
                confirmLabel={t('storefront.cms.pageEditor.discardChanges')}
                confirmTone="danger"
                onClose={() => setIsDiscardConfirmOpen(false)}
                onConfirm={handleDiscard}
            />
            <AuthoringConfirmDialog
                open={deleteBlockIndex != null}
                title={t('storefront.cms.pageEditor.deleteBlockTitle')}
                description={t('storefront.cms.pageEditor.deleteBlockDescription')}
                confirmLabel={t('storefront.cms.pageEditor.deleteBlock')}
                confirmTone="danger"
                onClose={() => setDeleteBlockIndex(null)}
                onConfirm={confirmRemoveBlock}
            />
            <AuthoringConfirmDialog
                open={pendingHistorySnapshot != null}
                title={t('storefront.cms.pageEditor.loadSnapshotTitle')}
                description={t('storefront.cms.pageEditor.loadSnapshotDescription')}
                confirmLabel={t('storefront.cms.pageEditor.loadSnapshot')}
                confirmTone="danger"
                onClose={() => setPendingHistorySnapshot(null)}
                onConfirm={() => {
                    setForm(fromVersionSnapshot(pendingHistorySnapshot.snapshot))
                    setPendingHistorySnapshot(null)
                }}
            />
            <AuthoringConflictDialog
                open={conflict != null}
                message={conflict?.message}
                hasLatest={Boolean(conflict?.latestVersion)}
                busy={isSaving || isPublishing}
                onClose={() => setConflict(null)}
                onLoadLatest={handleLoadLatest}
                onRetry={handleRetryConflict}
            />
            <UnsavedChangesDialog {...unsavedDialogProps} />
        </div>
    )
}

function fromPageResponse(page) {
    const workingVersion = page?.draft || page?.published

    if (!workingVersion) {
        return createEmptyPageForm()
    }

    return {
        slug: workingVersion.slug || '',
        translations: {
            en: fromTranslationResponse(workingVersion.translations?.en || workingVersion),
            ar: workingVersion.translations?.ar
                ? fromTranslationResponse(workingVersion.translations.ar)
                : null,
        },
    }
}

function fromTranslationResponse(translation) {
    const contentType = translation.contentType || CONTENT_TYPES.RichText
    return {
        title: translation.title || '',
        contentType,
        content: contentType === CONTENT_TYPES.Blocks
            ? (Array.isArray(translation.content) ? translation.content : [])
            : sanitizeRichTextHtml(typeof translation.content === 'string' ? translation.content : ''),
        schemaVersion: translation.schemaVersion || 1,
        seo: {
            metaTitle: translation.seo?.metaTitle || '',
            metaDescription: translation.seo?.metaDescription || '',
            metaKeywords: translation.seo?.metaKeywords || '',
            openGraphImageUrl: translation.seo?.openGraphImageUrl || '',
        },
    }
}

function fromVersionSnapshot(snapshot) {
    return fromPageResponse({ draft: snapshot })
}

function toRequestPayload(form) {
    const english = toTranslationPayload(form.translations.en)
    const arabic = form.translations.ar ? toTranslationPayload(form.translations.ar) : null
    return {
        slug: form.slug.trim(),
        title: english.title,
        contentType: english.contentType,
        content: english.content,
        schemaVersion: english.schemaVersion,
        seo: english.seo,
        translations: {
            en: english,
            ar: arabic,
        },
    }
}

function toTranslationPayload(translation) {
    return {
        title: translation.title.trim(),
        contentType: translation.contentType,
        content: translation.contentType === CONTENT_TYPES.Blocks
            ? (Array.isArray(translation.content) ? translation.content : [])
            : sanitizeRichTextHtml(typeof translation.content === 'string' ? translation.content : ''),
        schemaVersion: translation.schemaVersion || 1,
        seo: {
            metaTitle: emptyToNull(translation.seo.metaTitle),
            metaDescription: emptyToNull(translation.seo.metaDescription),
            metaKeywords: emptyToNull(translation.seo.metaKeywords),
            openGraphImageUrl: emptyToNull(translation.seo.openGraphImageUrl),
        },
    }
}

function hasTranslationContent(translation) {
    if (!translation?.title?.trim()) {
        return false
    }

    return translation.contentType === CONTENT_TYPES.Blocks
        ? Array.isArray(translation.content) && translation.content.length > 0
        : typeof translation.content === 'string' && translation.content.replace(/<[^>]*>/g, '').trim().length > 0
}

export function getPageFormFingerprint(form) {
    return JSON.stringify(toRequestPayload(form))
}

function normalizeSlugInput(value) {
    return value
        .toLowerCase()
        .replace(/[^a-z0-9-]+/g, '-')
        .replace(/--+/g, '-')
        .replace(/^-|-$/g, '')
}

function emptyToNull(value) {
    return value && value.trim() ? value.trim() : null
}

function LabeledInput({ label, onChange, helpText, ...props }) {
    return (
        <label className="block">
            <span className="mb-2 block text-sm font-medium text-slate-700">{label}</span>
            <input
                {...props}
                onChange={(event) => onChange(event.target.value)}
                className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-700 outline-none transition focus:border-sky-400 focus:ring-4 focus:ring-sky-100"
            />
            {helpText && <span className="mt-2 block text-xs text-slate-400">{helpText}</span>}
        </label>
    )
}

function LabeledTextarea({ label, ...props }) {
    return (
        <label className="block">
            <span className="mb-2 block text-sm font-medium text-slate-700">{label}</span>
            <textarea
                {...props}
                className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-700 outline-none transition focus:border-sky-400 focus:ring-4 focus:ring-sky-100"
            />
        </label>
    )
}

function RichTextEditor({ value, onChange, t }) {
    const editorRef = useRef(null)
    const isEditingRef = useRef(false)
    const linkSelectionRef = useRef(null)
    const [isLinkPromptOpen, setIsLinkPromptOpen] = useState(false)
    const [linkUrl, setLinkUrl] = useState('')
    const sanitizedValue = useMemo(() => sanitizeRichTextHtml(typeof value === 'string' ? value : ''), [value])

    useEffect(() => {
        // Only sync from props when not actively editing to avoid overwriting mid-edit
        if (editorRef.current && editorRef.current.innerHTML !== sanitizedValue && !isEditingRef.current) {
            editorRef.current.innerHTML = sanitizedValue
        }
    }, [sanitizedValue])

    function saveSelection() {
        const selection = window.getSelection()
        if (!selection || selection.rangeCount === 0) {
            return null
        }
        return selection.getRangeAt(0)
    }

    function restoreSelection(range) {
        if (!range) {
            return
        }
        const selection = window.getSelection()
        if (selection) {
            selection.removeAllRanges()
            selection.addRange(range)
        }
    }

    function runCommand(command, commandValue) {
        if (!editorRef.current) {
            return
        }

        editorRef.current.focus()
        document.execCommand(command, false, commandValue)
        
        // Keep React state sanitized while leaving the editable DOM untouched until blur.
        onChange(sanitizeRichTextHtml(editorRef.current.innerHTML))
    }

    return (
        <div className="overflow-hidden rounded-[24px] border border-slate-200">
            <div className="flex flex-wrap gap-2 border-b border-slate-200 bg-slate-50 p-3">
                <ToolbarButton label="B" onClick={() => runCommand('bold')} />
                <ToolbarButton label="I" onClick={() => runCommand('italic')} />
                <ToolbarButton label="H2" onClick={() => runCommand('formatBlock', '<h2>')} />
                <ToolbarButton label="UL" onClick={() => runCommand('insertUnorderedList')} />
                <ToolbarButton
                    label={t('storefront.cms.pageEditor.toolbarLink')}
                    onClick={() => {
                        linkSelectionRef.current = saveSelection()
                        setLinkUrl('')
                        setIsLinkPromptOpen(true)
                    }}
                />
            </div>
            <div
                ref={editorRef}
                contentEditable
                suppressContentEditableWarning
                onFocus={() => {
                    isEditingRef.current = true
                }}
                onInput={() => {
                    // Input is handled, but sanitization is deferred to onBlur to avoid lag
                    // This keeps the editor responsive while typing
                }}
                onBlur={(event) => {
                    isEditingRef.current = false
                    
                    // Sanitize on blur to clean up any unsafe content
                    const sanitized = sanitizeRichTextHtml(event.currentTarget.innerHTML)
                    
                    // Only update innerHTML if sanitization changed the content
                    if (event.currentTarget.innerHTML !== sanitized) {
                        event.currentTarget.innerHTML = sanitized
                    }
                    
                    // Always call onChange on blur to ensure state is updated
                    onChange(sanitized)
                }}
                className="min-h-[360px] px-5 py-4 text-sm leading-7 text-slate-700 outline-none"
            />
            <AuthoringPromptDialog
                open={isLinkPromptOpen}
                title={t('storefront.cms.pageEditor.addTextLink')}
                description={t('storefront.cms.pageEditor.addTextLinkDescription')}
                label={t('storefront.cms.linkUrlLabel')}
                value={linkUrl}
                placeholder={t('storefront.cms.pageEditor.linkPlaceholder')}
                confirmLabel={t('storefront.cms.pageEditor.addTextLink')}
                onChange={setLinkUrl}
                onClose={() => setIsLinkPromptOpen(false)}
                onConfirm={() => {
                    setIsLinkPromptOpen(false)
                    restoreSelection(linkSelectionRef.current)
                    runCommand('createLink', linkUrl)
                }}
            />
        </div>
    )
}

function ToolbarButton({ label, onClick }) {
    return (
        <button
            type="button"
            onClick={onClick}
            className="rounded-xl border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
        >
            {label}
        </button>
    )
}

function BlocksEditor({ blocks, onUpdate, onMove, onRemove, t }) {
    if (blocks.length === 0) {
        return (
            <div className="rounded-[24px] border border-dashed border-slate-300 bg-slate-50 px-6 py-16 text-center text-sm text-slate-500">
                {t('storefront.cms.pageEditor.blocksEmpty')}
            </div>
        )
    }

    return (
        <div className="space-y-4">
            {blocks.map((block, index) => (
                <div key={`${block.type}-${index}`} className="rounded-[24px] border border-slate-200">
                    <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-200 bg-slate-50 px-4 py-3">
                        <div className="flex items-center gap-3 text-sm font-medium text-slate-700">
                            <GripVertical size={16} className="text-slate-400" />
                            <span>{t(`storefront.cms.pageEditor.blockTypes.${block.type}`, { defaultValue: block.type })}</span>
                        </div>
                        <div className="flex gap-2">
                            <button
                                type="button"
                                onClick={() => onMove(index, -1)}
                                className="rounded-xl border border-slate-200 bg-white px-3 py-2 text-xs font-medium text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                            >
                                {t('storefront.cms.pageEditor.moveUp')}
                            </button>
                            <button
                                type="button"
                                onClick={() => onMove(index, 1)}
                                className="rounded-xl border border-slate-200 bg-white px-3 py-2 text-xs font-medium text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                            >
                                {t('storefront.cms.pageEditor.moveDown')}
                            </button>
                            <button
                                type="button"
                                onClick={() => onRemove(index)}
                                className="rounded-xl border border-rose-200 bg-white px-3 py-2 text-xs font-medium text-rose-700 transition hover:bg-rose-50"
                            >
                                <Trash2 size={14} />
                            </button>
                        </div>
                    </div>

                    <div className="p-4">
                        {block.type === 'text' && (
                            <div className="space-y-4">
                                <LabeledTextarea
                                    label={t('storefront.cms.pageEditor.text')}
                                    value={block.data?.text || ''}
                                    onChange={(event) => onUpdate(index, {
                                        ...block,
                                        data: {
                                            ...block.data,
                                            text: event.target.value,
                                        },
                                    })}
                                    rows={5}
                                />
                                <label className="block">
                                    <span className="mb-2 block text-sm font-medium text-slate-700">
                                        {t('storefront.cms.pageEditor.alignment')}
                                    </span>
                                    <select
                                        value={block.data?.alignment || 'left'}
                                        onChange={(event) => onUpdate(index, {
                                            ...block,
                                            data: {
                                                ...block.data,
                                                alignment: event.target.value,
                                            },
                                        })}
                                        className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-700 outline-none transition focus:border-sky-400 focus:ring-4 focus:ring-sky-100"
                                    >
                                        <option value="left">{t('storefront.cms.pageEditor.left')}</option>
                                        <option value="center">{t('storefront.cms.pageEditor.center')}</option>
                                        <option value="right">{t('storefront.cms.pageEditor.right')}</option>
                                    </select>
                                </label>
                            </div>
                        )}

                        {block.type === 'image' && (
                            <div className="space-y-4">
                                <LabeledInput
                                    label={t('storefront.cms.pageEditor.imageUrl')}
                                    value={block.data?.imageUrl || ''}
                                    onChange={(value) => onUpdate(index, {
                                        ...block,
                                        data: {
                                            ...block.data,
                                            imageUrl: value,
                                        },
                                    })}
                                />
                                <LabeledInput
                                    label={t('storefront.cms.pageEditor.altText')}
                                    value={block.data?.alt || ''}
                                    onChange={(value) => onUpdate(index, {
                                        ...block,
                                        data: {
                                            ...block.data,
                                            alt: value,
                                        },
                                    })}
                                />
                                <LabeledInput
                                    label={t('storefront.cms.pageEditor.caption')}
                                    value={block.data?.caption || ''}
                                    onChange={(value) => onUpdate(index, {
                                        ...block,
                                        data: {
                                            ...block.data,
                                            caption: value,
                                        },
                                    })}
                                />
                            </div>
                        )}

                        {block.type === 'faq' && (
                            <FaqEditor block={block} index={index} onUpdate={onUpdate} t={t} />
                        )}
                    </div>
                </div>
            ))}
        </div>
    )
}

function FaqEditor({ block, index, onUpdate, t }) {
    const items = Array.isArray(block.data?.items) ? block.data.items : []

    function updateFaqItem(itemIndex, patch) {
        const nextItems = items.map((item, currentIndex) =>
            currentIndex === itemIndex ? { ...item, ...patch } : item
        )

        onUpdate(index, {
            ...block,
            data: {
                ...block.data,
                items: nextItems,
            },
        })
    }

    function addFaqItem() {
        onUpdate(index, {
            ...block,
            data: {
                ...block.data,
                items: [...items, { question: '', answer: '' }],
            },
        })
    }

    function removeFaqItem(itemIndex) {
        onUpdate(index, {
            ...block,
            data: {
                ...block.data,
                items: items.filter((_, currentIndex) => currentIndex !== itemIndex),
            },
        })
    }

    return (
        <div className="space-y-4">
            {items.map((item, itemIndex) => (
                <div key={itemIndex} className="rounded-2xl border border-slate-200 p-4">
                    <div className="mb-3 flex items-center justify-between">
                        <h4 className="text-sm font-semibold text-slate-700">
                            {t('storefront.cms.pageEditor.faqItem', { number: itemIndex + 1 })}
                        </h4>
                        <button
                            type="button"
                            onClick={() => removeFaqItem(itemIndex)}
                            className="rounded-xl border border-rose-200 px-3 py-2 text-xs font-medium text-rose-700 transition hover:bg-rose-50"
                        >
                            {t('storefront.cms.pageEditor.remove')}
                        </button>
                    </div>
                    <div className="space-y-4">
                        <LabeledInput
                            label={t('storefront.cms.pageEditor.question')}
                            value={item.question || ''}
                            onChange={(value) => updateFaqItem(itemIndex, { question: value })}
                        />
                        <LabeledTextarea
                            label={t('storefront.cms.pageEditor.answer')}
                            value={item.answer || ''}
                            onChange={(event) => updateFaqItem(itemIndex, { answer: event.target.value })}
                            rows={4}
                        />
                    </div>
                </div>
            ))}

            <button
                type="button"
                onClick={addFaqItem}
                className="inline-flex items-center gap-2 rounded-xl border border-slate-200 px-4 py-2 text-sm font-medium text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
            >
                <Plus size={16} />
                {t('storefront.cms.pageEditor.addFaqItem')}
            </button>
        </div>
    )
}

export default CmsPageEditor
