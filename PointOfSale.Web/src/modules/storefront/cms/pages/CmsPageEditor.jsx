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

const CONTENT_TYPES = {
    RichText: 'RichText',
    Blocks: 'Blocks',
}

function createEmptyPageForm() {
    return {
        slug: '',
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
    const { i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const isNewPage = pageId === 'new'

    const [page, setPage] = useState(null)
    const [form, setForm] = useState(createEmptyPageForm())
    const [isLoading, setIsLoading] = useState(!isNewPage)
    const [isSaving, setIsSaving] = useState(false)
    const [isPublishing, setIsPublishing] = useState(false)
    const [error, setError] = useState('')
    const [conflict, setConflict] = useState(null)
    const [versionHistory, setVersionHistory] = useState(null)
    const [isHistoryOpen, setIsHistoryOpen] = useState(false)
    const [isHistoryLoading, setIsHistoryLoading] = useState(false)
    const [rollbackState, setRollbackState] = useState({ version: null, reason: '', confirmed: false, isRollingBack: false })

    const storefrontBaseUrl = import.meta.env.VITE_STOREFRONT_URL || 'http://localhost:5174'
    const currentUser = useAuthStore((state) => state.user)
    const currentTenantId = currentUser?.tenantId || ''

    useEffect(() => {
        if (isNewPage) {
            setPage(null)
            setForm(createEmptyPageForm())
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
            const admittedPage = deriveCurrentCmsPageIdentityPresentation(data, currentUser, { expectedPageId: id })
            if (!admittedPage) {
                throw new Error('CMS page was rejected by backend page identity admission.')
            }

            setPage(admittedPage)
            setForm(fromPageResponse(admittedPage))
        } catch (err) {
            console.error('Failed to load CMS page', err)
            setError(isRTL ? 'تعذر تحميل الصفحة.' : 'Unable to load page.')
        } finally {
            setIsLoading(false)
        }
    }

    function updateForm(patch) {
        setForm((current) => ({ ...current, ...patch }))
    }

    function updateSeo(patch) {
        setForm((current) => ({
            ...current,
            seo: {
                ...current.seo,
                ...patch,
            },
        }))
    }

    async function handleSave() {
        if (!form.slug.trim() || !form.title.trim()) {
            alert(isRTL ? 'العنوان والرابط مطلوبان.' : 'Title and slug are required.')
            return null
        }

        setIsSaving(true)
        setError('')
        setConflict(null)

        try {
            const payload = toRequestPayload(form)
            if (!isNewPage) {
                payload.expectedVersion = page?.draft?.version ?? page?.published?.version
            }
            const savedPage = isNewPage
                ? await cmsService.createPage(payload)
                : await cmsService.updatePage(pageId, payload)
            const admittedPage = deriveCurrentCmsPageIdentityPresentation(savedPage, currentUser, {
                expectedPageId: isNewPage ? undefined : pageId,
            })
            if (!admittedPage) {
                throw new Error('Saved CMS page was rejected by backend page identity admission.')
            }

            setPage(admittedPage)
            setForm(fromPageResponse(admittedPage))

            if (isNewPage) {
                navigate(`/storefront/cms/pages/${admittedPage.id}`, { replace: true })
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
            const message = err?.response?.data?.message
                || (isRTL ? 'فشل حفظ المسودة.' : 'Failed to save draft.')
            setError(message)
            return null
        } finally {
            setIsSaving(false)
        }
    }

    async function handlePublish() {
        const targetPage = page || await handleSave()
        if (!targetPage) {
            return
        }

        if (!window.confirm(
            isRTL
                ? 'سيتم نشر هذه المسودة لتصبح مرئية في المتجر. هل تريد المتابعة؟'
                : 'Publish this draft to the storefront now?'
        )) {
            return
        }

        setIsPublishing(true)
        setConflict(null)
        try {
            const expectedVersion = targetPage.draft?.version
            if (!expectedVersion) {
                throw new Error('The page does not have a draft version to publish.')
            }
            const published = await cmsService.publishPage(targetPage.id, expectedVersion)
            const admittedPage = deriveCurrentCmsPageIdentityPresentation(published, currentUser, {
                expectedPageId: targetPage.id,
            })
            if (!admittedPage) {
                throw new Error('Published CMS page was rejected by backend page identity admission.')
            }

            setPage(admittedPage)
            setForm(fromPageResponse(admittedPage))
        } catch (err) {
            console.error('Failed to publish CMS page', err)
            const conflictState = extractCmsConflict(err)
            if (conflictState) {
                setConflict(conflictState)
                setError(conflictState.message)
                return
            }
            alert(err?.response?.data?.message || (isRTL ? 'فشل نشر الصفحة.' : 'Failed to publish page.'))
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
            setError(isRTL ? 'تعذر تحميل سجل الإصدارات.' : 'Unable to load version history.')
            return null
        } finally {
            setIsHistoryLoading(false)
        }
    }

    async function openVersionHistory() {
        setIsHistoryOpen(true)
        await loadVersionHistory()
    }

    function previewHistoryVersion(entry) {
        setForm(fromVersionSnapshot(entry.snapshot))
    }

    async function handleRollbackVersion() {
        if (!rollbackState.confirmed || !rollbackState.version) {
            return
        }

        const expectedVersion = page?.draft?.version ?? page?.published?.version
        if (!expectedVersion) {
            setError(isRTL ? 'لا توجد نسخة حالية مقبولة للاستعادة.' : 'Rollback requires current backend version authority.')
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

            setPage(admittedPage)
            setForm(fromPageResponse(admittedPage))
            setRollbackState({ version: null, reason: '', confirmed: false, isRollingBack: false })
            await loadVersionHistory()
        } catch (err) {
            console.error('Failed to rollback CMS page', err)
            const conflictState = extractCmsConflict(err)
            if (conflictState) {
                setConflict(conflictState)
                setError(conflictState.message)
            } else {
                setError(err?.response?.data?.message || (isRTL ? 'فشل استعادة النسخة.' : 'Failed to rollback version.'))
            }
            setRollbackState((current) => ({ ...current, isRollingBack: false }))
        }
    }

    async function handlePreview() {
        if (!form.slug.trim()) {
            alert(isRTL ? 'احفظ الصفحة أولاً أو أدخل رابطاً صالحاً للمعاينة.' : 'Add a valid slug before previewing.')
            return
        }

        if (isNewPage) {
            alert(isRTL ? 'احفظ الصفحة أولاً لإنشاء مسودة قابلة للمعاينة.' : 'Save the page first so a preview draft exists.')
            return
        }

        try {
            if (!currentTenantId) {
                throw new Error('Current backend vendor identity is unavailable.')
            }
            const previewAccess = await cmsService.createPreviewLink(pageId)
            const previewUrl = `${storefrontBaseUrl}/${previewAccess.slug}?tenant=${encodeURIComponent(currentTenantId)}&preview=true&previewToken=${encodeURIComponent(previewAccess.previewToken)}`
            window.open(previewUrl, '_blank', 'noopener,noreferrer')
        } catch (err) {
            console.error('Failed to create CMS preview link', err)
            alert(isRTL ? 'تعذر إنشاء رابط معاينة آمن.' : 'Unable to create a secure preview link.')
        }
    }

    function addBlock(kind) {
        setForm((current) => {
            const currentBlocks = Array.isArray(current.content) ? current.content : []
            const nextBlock = kind === 'image'
                ? createImageBlock()
                : kind === 'faq'
                    ? createFaqBlock()
                    : createTextBlock()

            return {
                ...current,
                contentType: CONTENT_TYPES.Blocks,
                content: [...currentBlocks, nextBlock],
            }
        })
    }

    function updateBlock(index, nextBlock) {
        setForm((current) => ({
            ...current,
            content: current.content.map((block, blockIndex) => blockIndex === index ? nextBlock : block),
        }))
    }

    function removeBlock(index) {
        setForm((current) => ({
            ...current,
            content: current.content.filter((_, blockIndex) => blockIndex !== index),
        }))
    }

    function moveBlock(index, offset) {
        const targetIndex = index + offset
        if (targetIndex < 0 || targetIndex >= form.content.length) {
            return
        }

        setForm((current) => {
            const blocks = [...current.content]
            const [selected] = blocks.splice(index, 1)
            blocks.splice(targetIndex, 0, selected)
            return {
                ...current,
                content: blocks,
            }
        })
    }

    if (isLoading) {
        return (
            <div className="flex min-h-[320px] items-center justify-center rounded-[28px] border border-slate-200 bg-white">
                <Loader2 size={32} className="animate-spin text-slate-500" />
            </div>
        )
    }

    return (
        <div className="space-y-6">
            <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
                <div className="flex items-start gap-4">
                    <button
                        onClick={() => navigate('/storefront/cms/pages')}
                        className="rounded-2xl border border-slate-200 bg-white p-3 text-slate-600 transition hover:border-slate-300 hover:bg-slate-50"
                    >
                        <ArrowLeft size={18} className={isRTL ? 'rotate-180' : ''} />
                    </button>
                    <div>
                        <p className="text-sm font-semibold uppercase tracking-[0.2em] text-sky-600">
                            {isRTL ? 'محرر الصفحات' : 'Page Editor'}
                        </p>
                        <h1 className="mt-2 text-3xl font-semibold text-slate-900">
                            {isNewPage
                                ? (isRTL ? 'صفحة جديدة' : 'Create CMS Page')
                                : form.title || (isRTL ? 'تحرير الصفحة' : 'Edit CMS Page')}
                        </h1>
                        <p className="mt-2 text-sm text-slate-500">
                            {page?.publishedAtUtc
                                ? `${isRTL ? 'آخر نشر:' : 'Last published:'} ${new Date(page.publishedAtUtc).toLocaleString()}`
                                : (isRTL ? 'هذه الصفحة ما زالت في وضع المسودة.' : 'This page is still in draft mode.')}
                        </p>
                    </div>
                </div>

                <div className="flex flex-wrap gap-3">
                    <button
                        onClick={handlePreview}
                        disabled={isNewPage}
                        className="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-4 py-2 text-sm font-medium text-slate-700 transition hover:border-slate-300 hover:bg-slate-50 disabled:opacity-50"
                    >
                        <Eye size={16} />
                        {isRTL ? 'معاينة' : 'Preview'}
                    </button>
                    <button
                        onClick={openVersionHistory}
                        disabled={isNewPage}
                        className="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-4 py-2 text-sm font-medium text-slate-700 transition hover:border-slate-300 hover:bg-slate-50 disabled:opacity-50"
                    >
                        <History size={16} />
                        {isRTL ? 'سجل الإصدارات' : 'Version History'}
                    </button>
                    <button
                        onClick={handleSave}
                        disabled={isSaving}
                        className="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-4 py-2 text-sm font-medium text-slate-700 transition hover:border-slate-300 hover:bg-slate-50 disabled:opacity-50"
                    >
                        {isSaving ? <Loader2 size={16} className="animate-spin" /> : <Save size={16} />}
                        {isRTL ? 'حفظ المسودة' : 'Save Draft'}
                    </button>
                    <button
                        onClick={handlePublish}
                        disabled={isPublishing}
                        className="inline-flex items-center gap-2 rounded-xl bg-emerald-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-emerald-500 disabled:opacity-50"
                    >
                        {isPublishing ? <Loader2 size={16} className="animate-spin" /> : <Globe size={16} />}
                        {isRTL ? 'نشر' : 'Publish'}
                    </button>
                </div>
            </div>

            {error && (
                <div className="rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">
                    {error}
                </div>
            )}

            {conflict && (
                <div className="rounded-2xl border border-amber-200 bg-amber-50 px-4 py-4 text-sm text-amber-900">
                    <p className="font-semibold">
                        {isRTL ? 'تم اكتشاف تعارض في النسخة' : 'A newer CMS version is available'}
                    </p>
                    <p className="mt-1 text-amber-800">{conflict.message}</p>
                    <p className="mt-1 text-amber-700">
                        {isRTL
                            ? 'تغييراتك الحالية ما زالت على الشاشة. حمّل آخر نسخة للمراجعة أو احتفظ بمسودتك الحالية للدمج اليدوي.'
                            : 'Your current edits are still on screen. Load the latest version to review it, or keep this draft visible and merge manually.'}
                    </p>
                    <div className="mt-3 flex flex-wrap gap-3">
                        <button
                            type="button"
                            onClick={() => {
                                if (conflict.latestVersion) {
                                    const admittedPage = deriveCurrentCmsPageIdentityPresentation(
                                        conflict.latestVersion,
                                        currentUser,
                                        { expectedPageId: pageId }
                                    )
                                    if (admittedPage) {
                                        setPage(admittedPage)
                                        setForm(fromPageResponse(admittedPage))
                                        setConflict(null)
                                        setError('')
                                    } else {
                                        setError('Latest version was rejected by page identity admission.')
                                    }
                                }
                            }}
                            disabled={!conflict.latestVersion}
                            className="inline-flex items-center gap-2 rounded-xl bg-amber-900 px-4 py-2 text-sm font-medium text-white transition hover:bg-amber-950 disabled:cursor-not-allowed disabled:opacity-50 disabled:hover:bg-amber-900"
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

            <div className="grid gap-6 xl:grid-cols-[340px_minmax(0,1fr)]">
                <div className="space-y-6">
                    <section className="rounded-[28px] border border-slate-200 bg-white p-6 shadow-sm">
                        <h2 className="text-lg font-semibold text-slate-900">
                            {isRTL ? 'معلومات الصفحة' : 'Page Details'}
                        </h2>
                        <div className="mt-5 space-y-4">
                            <LabeledInput
                                label={isRTL ? 'العنوان' : 'Title'}
                                value={form.title}
                                onChange={(value) => updateForm({ title: value })}
                                placeholder={isRTL ? 'مثال: من نحن' : 'Example: About Us'}
                            />
                            <LabeledInput
                                label={isRTL ? 'الرابط' : 'Slug'}
                                value={form.slug}
                                onChange={(value) => updateForm({ slug: normalizeSlugInput(value) })}
                                placeholder="about-us"
                                helpText={isRTL ? 'سيظهر في المسار النهائي مثل /about-us' : 'This becomes the storefront path, for example /about-us'}
                            />
                            <label className="block">
                                <span className="mb-2 block text-sm font-medium text-slate-700">
                                    {isRTL ? 'نمط المحتوى' : 'Content Mode'}
                                </span>
                                <select
                                    value={form.contentType}
                                    onChange={(event) => updateForm({
                                        contentType: event.target.value,
                                        content: event.target.value === CONTENT_TYPES.Blocks
                                            ? (Array.isArray(form.content) ? form.content : [])
                                            : (typeof form.content === 'string' ? form.content : ''),
                                    })}
                                    className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-700 outline-none transition focus:border-sky-400 focus:ring-4 focus:ring-sky-100"
                                >
                                    <option value={CONTENT_TYPES.RichText}>{isRTL ? 'نص غني' : 'Rich text'}</option>
                                    <option value={CONTENT_TYPES.Blocks}>{isRTL ? 'كتل مرنة' : 'Blocks'}</option>
                                </select>
                            </label>
                        </div>
                    </section>

                    <section className="rounded-[28px] border border-slate-200 bg-white p-6 shadow-sm">
                        <h2 className="text-lg font-semibold text-slate-900">
                            {isRTL ? 'إعدادات SEO' : 'SEO Settings'}
                        </h2>
                        <div className="mt-5 space-y-4">
                            <LabeledInput
                                label={isRTL ? 'عنوان الميتا' : 'Meta title'}
                                value={form.seo.metaTitle}
                                onChange={(value) => updateSeo({ metaTitle: value })}
                            />
                            <LabeledTextarea
                                label={isRTL ? 'وصف الميتا' : 'Meta description'}
                                value={form.seo.metaDescription}
                                onChange={(event) => updateSeo({ metaDescription: event.target.value })}
                                rows={4}
                            />
                            <LabeledInput
                                label={isRTL ? 'الكلمات المفتاحية' : 'Meta keywords'}
                                value={form.seo.metaKeywords}
                                onChange={(value) => updateSeo({ metaKeywords: value })}
                            />
                            <LabeledInput
                                label={isRTL ? 'صورة Open Graph' : 'Open Graph image URL'}
                                value={form.seo.openGraphImageUrl}
                                onChange={(value) => updateSeo({ openGraphImageUrl: value })}
                                placeholder="https://cdn.example.com/page-cover.jpg"
                            />
                        </div>
                    </section>
                </div>

                <section className="rounded-[30px] border border-slate-200 bg-white p-6 shadow-sm">
                    <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
                        <div>
                            <h2 className="text-lg font-semibold text-slate-900">
                                {isRTL ? 'المحتوى' : 'Content'}
                            </h2>
                            <p className="mt-1 text-sm text-slate-500">
                                {form.contentType === CONTENT_TYPES.RichText
                                    ? (isRTL ? 'استخدم محرر النصوص المنسق ثم راجع النتيجة في المعاينة.' : 'Use the rich text editor and preview the result before publishing.')
                                    : (isRTL ? 'أنشئ الصفحة من كتل قابلة للتوسعة لتظل جاهزة للباني المرئي مستقبلاً.' : 'Build the page from reusable blocks so it stays ready for a future visual builder.')}
                            </p>
                        </div>

                        {form.contentType === CONTENT_TYPES.Blocks && (
                            <div className="flex flex-wrap gap-2">
                                <button
                                    onClick={() => addBlock('text')}
                                    className="inline-flex items-center gap-2 rounded-xl border border-slate-200 px-3 py-2 text-sm font-medium text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                                >
                                    <Type size={16} />
                                    {isRTL ? 'نص' : 'Text'}
                                </button>
                                <button
                                    onClick={() => addBlock('image')}
                                    className="inline-flex items-center gap-2 rounded-xl border border-slate-200 px-3 py-2 text-sm font-medium text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                                >
                                    <ImageIcon size={16} />
                                    {isRTL ? 'صورة' : 'Image'}
                                </button>
                                <button
                                    onClick={() => addBlock('faq')}
                                    className="inline-flex items-center gap-2 rounded-xl border border-slate-200 px-3 py-2 text-sm font-medium text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                                >
                                    <ListOrdered size={16} />
                                    FAQ
                                </button>
                            </div>
                        )}
                    </div>

                    <div className="mt-6">
                        {form.contentType === CONTENT_TYPES.RichText ? (
                            <RichTextEditor
                                value={typeof form.content === 'string' ? form.content : ''}
                                onChange={(value) => updateForm({ content: value })}
                                isRTL={isRTL}
                            />
                        ) : (
                            <BlocksEditor
                                blocks={Array.isArray(form.content) ? form.content : []}
                                onUpdate={updateBlock}
                                onMove={moveBlock}
                                onRemove={removeBlock}
                                isRTL={isRTL}
                            />
                        )}
                    </div>
                </section>
            </div>

            {isHistoryOpen && (
                <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/50 px-4 py-8">
                    <div className="max-h-[90vh] w-full max-w-3xl overflow-hidden rounded-[28px] bg-white shadow-2xl">
                        <div className="flex items-center justify-between border-b border-slate-200 px-6 py-4">
                            <div>
                                <h2 className="text-lg font-semibold text-slate-900">
                                    {isRTL ? 'سجل إصدارات الصفحة' : 'Page Version History'}
                                </h2>
                                <p className="text-sm text-slate-500">
                                    {isRTL ? 'المعاينة والاستعادة تستخدمان أدلة النسخ المقبولة من الخادم.' : 'Preview and rollback use backend-admitted version evidence.'}
                                </p>
                            </div>
                            <button
                                type="button"
                                onClick={() => setIsHistoryOpen(false)}
                                disabled={rollbackState.isRollingBack}
                                className="rounded-xl border border-slate-200 px-3 py-2 text-sm font-medium text-slate-600 transition hover:bg-slate-50 disabled:opacity-50"
                            >
                                {isRTL ? 'إغلاق' : 'Close'}
                            </button>
                        </div>
                        <div className="max-h-[calc(90vh-90px)] overflow-y-auto p-6">
                            {isHistoryLoading ? (
                                <div className="flex min-h-[220px] items-center justify-center">
                                    <Loader2 size={28} className="animate-spin text-slate-500" />
                                </div>
                            ) : (
                                <div className="space-y-4">
                                    {(versionHistory?.versions || []).map((entry) => (
                                        <div key={entry.version} className="rounded-2xl border border-slate-200 p-4">
                                            <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
                                                <div>
                                                    <div className="flex flex-wrap items-center gap-2">
                                                        <span className="text-sm font-semibold text-slate-900">v{entry.version}</span>
                                                        {entry.isPublished && <span className="rounded-full bg-emerald-50 px-2 py-1 text-xs font-medium text-emerald-700">{isRTL ? 'منشور' : 'Published'}</span>}
                                                        {entry.isCurrentDraft && <span className="rounded-full bg-amber-50 px-2 py-1 text-xs font-medium text-amber-700">{isRTL ? 'مسودة حالية' : 'Current draft'}</span>}
                                                    </div>
                                                    <p className="mt-1 text-sm text-slate-700">{entry.snapshot.title}</p>
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
                                                        {isRTL ? 'معاينة' : 'Preview'}
                                                    </button>
                                                    <button
                                                        type="button"
                                                        onClick={() => setRollbackState({ version: entry.version, reason: '', confirmed: false, isRollingBack: false })}
                                                        disabled={entry.isPublished || rollbackState.isRollingBack}
                                                        className="inline-flex items-center gap-2 rounded-xl border border-rose-200 px-3 py-2 text-sm font-medium text-rose-700 transition hover:bg-rose-50 disabled:opacity-50"
                                                    >
                                                        <Undo2 size={14} />
                                                        {isRTL ? 'استعادة' : 'Rollback'}
                                                    </button>
                                                </div>
                                            </div>
                                            {rollbackState.version === entry.version && (
                                                <div className="mt-4 rounded-2xl border border-rose-100 bg-rose-50 p-4">
                                                    <LabeledTextarea
                                                        label={isRTL ? 'سبب الاستعادة (اختياري)' : 'Rollback reason (optional)'}
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
                                                        <span>{isRTL ? 'أفهم أن الخادم سيحل اللقطة وينشئ نسخة منشورة جديدة.' : 'I understand the backend will resolve the snapshot and create a new published version.'}</span>
                                                    </label>
                                                    <button
                                                        type="button"
                                                        onClick={handleRollbackVersion}
                                                        disabled={!rollbackState.confirmed || rollbackState.isRollingBack}
                                                        className="mt-4 inline-flex items-center gap-2 rounded-xl bg-rose-700 px-4 py-2 text-sm font-medium text-white transition hover:bg-rose-800 disabled:opacity-50"
                                                    >
                                                        {rollbackState.isRollingBack ? <Loader2 size={16} className="animate-spin" /> : <Undo2 size={16} />}
                                                        {isRTL ? 'استعادة الآن' : 'Rollback now'}
                                                    </button>
                                                </div>
                                            )}
                                        </div>
                                    ))}
                                </div>
                            )}
                        </div>
                    </div>
                </div>
            )}
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
        title: workingVersion.title || '',
        contentType: workingVersion.contentType || CONTENT_TYPES.RichText,
        content: workingVersion.contentType === CONTENT_TYPES.Blocks
            ? (Array.isArray(workingVersion.content) ? workingVersion.content : [])
            : sanitizeRichTextHtml(typeof workingVersion.content === 'string' ? workingVersion.content : ''),
        schemaVersion: workingVersion.schemaVersion || 1,
        seo: {
            metaTitle: workingVersion.seo?.metaTitle || '',
            metaDescription: workingVersion.seo?.metaDescription || '',
            metaKeywords: workingVersion.seo?.metaKeywords || '',
            openGraphImageUrl: workingVersion.seo?.openGraphImageUrl || '',
        },
    }
}

function fromVersionSnapshot(snapshot) {
    return fromPageResponse({ draft: snapshot })
}

function toRequestPayload(form) {
    return {
        slug: form.slug.trim(),
        title: form.title.trim(),
        contentType: form.contentType,
        content: form.contentType === CONTENT_TYPES.Blocks
            ? (Array.isArray(form.content) ? form.content : [])
            : sanitizeRichTextHtml(typeof form.content === 'string' ? form.content : ''),
        schemaVersion: form.schemaVersion || 1,
        seo: {
            metaTitle: emptyToNull(form.seo.metaTitle),
            metaDescription: emptyToNull(form.seo.metaDescription),
            metaKeywords: emptyToNull(form.seo.metaKeywords),
            openGraphImageUrl: emptyToNull(form.seo.openGraphImageUrl),
        },
    }
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

function RichTextEditor({ value, onChange, isRTL }) {
    const editorRef = useRef(null)
    const isEditingRef = useRef(false)
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
                    label={isRTL ? 'رابط' : 'Link'}
                    onClick={() => {
                        const url = window.prompt(isRTL ? 'أدخل الرابط' : 'Enter URL')
                        if (url) {
                            runCommand('createLink', url)
                        }
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

function BlocksEditor({ blocks, onUpdate, onMove, onRemove, isRTL }) {
    if (blocks.length === 0) {
        return (
            <div className="rounded-[24px] border border-dashed border-slate-300 bg-slate-50 px-6 py-16 text-center text-sm text-slate-500">
                {isRTL ? 'أضف أول كتلة للبدء.' : 'Add your first block to start building the page.'}
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
                            <span>{block.type}</span>
                        </div>
                        <div className="flex gap-2">
                            <button
                                type="button"
                                onClick={() => onMove(index, -1)}
                                className="rounded-xl border border-slate-200 bg-white px-3 py-2 text-xs font-medium text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                            >
                                {isRTL ? 'لأعلى' : 'Up'}
                            </button>
                            <button
                                type="button"
                                onClick={() => onMove(index, 1)}
                                className="rounded-xl border border-slate-200 bg-white px-3 py-2 text-xs font-medium text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                            >
                                {isRTL ? 'لأسفل' : 'Down'}
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
                                    label={isRTL ? 'النص' : 'Text'}
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
                                        {isRTL ? 'المحاذاة' : 'Alignment'}
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
                                        <option value="left">{isRTL ? 'يسار' : 'Left'}</option>
                                        <option value="center">{isRTL ? 'وسط' : 'Center'}</option>
                                        <option value="right">{isRTL ? 'يمين' : 'Right'}</option>
                                    </select>
                                </label>
                            </div>
                        )}

                        {block.type === 'image' && (
                            <div className="space-y-4">
                                <LabeledInput
                                    label={isRTL ? 'رابط الصورة' : 'Image URL'}
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
                                    label={isRTL ? 'النص البديل' : 'Alt text'}
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
                                    label={isRTL ? 'التسمية' : 'Caption'}
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
                            <FaqEditor block={block} index={index} onUpdate={onUpdate} isRTL={isRTL} />
                        )}
                    </div>
                </div>
            ))}
        </div>
    )
}

function FaqEditor({ block, index, onUpdate, isRTL }) {
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
                            {isRTL ? `سؤال ${itemIndex + 1}` : `FAQ Item ${itemIndex + 1}`}
                        </h4>
                        <button
                            type="button"
                            onClick={() => removeFaqItem(itemIndex)}
                            className="rounded-xl border border-rose-200 px-3 py-2 text-xs font-medium text-rose-700 transition hover:bg-rose-50"
                        >
                            {isRTL ? 'حذف' : 'Remove'}
                        </button>
                    </div>
                    <div className="space-y-4">
                        <LabeledInput
                            label={isRTL ? 'السؤال' : 'Question'}
                            value={item.question || ''}
                            onChange={(value) => updateFaqItem(itemIndex, { question: value })}
                        />
                        <LabeledTextarea
                            label={isRTL ? 'الإجابة' : 'Answer'}
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
                {isRTL ? 'إضافة سؤال' : 'Add FAQ Item'}
            </button>
        </div>
    )
}

export default CmsPageEditor
